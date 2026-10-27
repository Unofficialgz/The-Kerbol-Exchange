using System;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
using KSP.UI.Screens;

namespace OreMarket
{
    // KSP owns registration and persistence; never edit save files directly.
    [KSPScenario(ScenarioCreationOptions.AddToNewCareerGames | ScenarioCreationOptions.AddToExistingCareerGames, GameScenes.FLIGHT, GameScenes.SPACECENTER, GameScenes.TRACKSTATION)]
    public sealed class RecoverySales : ScenarioModule
    {
        private static RecoverySales instance;
        private readonly TransactionLedger ledger = new TransactionLedger();
        private readonly HashSet<Guid> reportedRejections = new HashSet<Guid>();
        private bool subscribed;
        private bool loaded;

        public override void OnAwake()
        {
            base.OnAwake();
            if (instance != null && instance != this) { UnityEngine.Object.Destroy(this); return; }
            instance = this;
            GameEvents.onVesselRecoveryProcessingComplete.Add(OnRecoveryComplete);
            subscribed = true;
            Log.Info("career recovery listener subscribed (processing-complete event)");
        }

        public void OnDestroy()
        {
            if (!subscribed) return;
            GameEvents.onVesselRecoveryProcessingComplete.Remove(OnRecoveryComplete);
            if (instance == this) instance = null;
            subscribed = false;
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            ledger.Clear();
            reportedRejections.Clear();
            foreach (string key in node.GetValues("claimed")) ledger.Restore(key);
            loaded = true;
            Log.Info("Recovery claims loaded: keys=" + ledger.Entries.Count());
        }

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            node.RemoveValues("claimed");
            foreach (string key in ledger.Entries.OrderBy(k => k, StringComparer.Ordinal)) node.AddValue("claimed", key);
        }

        private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        // Stock MessageSystem owns display, queuing and normal game-save persistence.
        // Receipt failure never changes a transaction or retries Funding.
        private static void Receipt(string title, string text, bool success)
        {
            try
            {
                if (MessageSystem.Instance == null) { Log.Info("Recovery receipt unavailable: stock MessageSystem missing; see transaction log"); return; }
                MessageSystem.Instance.AddMessage(new MessageSystem.Message(title, text,
                    success ? MessageSystemButton.MessageButtonColor.GREEN : MessageSystemButton.MessageButtonColor.ORANGE,
                    success ? MessageSystemButton.ButtonIcons.COMPLETE : MessageSystemButton.ButtonIcons.ALERT), true);
                Log.Info("Recovery receipt posted: " + title);
            }
            catch (Exception e) { Log.Error("Recovery receipt failed; payment is not retried: " + e); }
        }

        private void RejectCargo(ProtoVessel vessel, string reason, string detail)
        {
            Log.Info("No sale: vessel=" + vessel.vesselID.ToString("N") + " reason=" + reason + " " + detail);
            if (!reportedRejections.Add(vessel.vesselID)) return;
            Receipt("The Kerbol Exchange: no sale", "No bonus funds were awarded.\n" + reason + "\n" + detail +
                "\nVessel: " + vessel.vesselID.ToString("N") + "\nStock recovery funds are separate.", false);
        }

        private void OnRecoveryComplete(ProtoVessel vessel, MissionRecoveryDialog dialog, float recoveryFactor)
        {
            try
            {
                if (!loaded) { Log.Info("No sale: recovery claims not loaded"); return; }
                if (HighLogic.CurrentGame == null || HighLogic.CurrentGame.Mode != Game.Modes.CAREER)
                { Log.Info("No sale: career mode required"); return; }
                if (Funding.Instance == null) { Log.Info("No sale: Funding unavailable"); return; }
                Log.Info("Recovery complete detected: vessel=" + vessel?.vesselID + " bodyIndex=" + vessel?.orbitSnapShot?.ReferenceBodyIndex + " stockRecoveryFactor=" + recoveryFactor + " dialog=" + (dialog != null));
                if (!VesselRules.RecoveredTanks(vessel, out var tanks, out string reason))
                { Log.Info("No sale: " + reason); return; }
                string vesselId=vessel.vesselID.ToString("N"), manifest=VesselRules.Manifest(vessel.protoPartSnapshots);
                if (ledger.Entries.Contains("v:"+vesselId)) { Log.Info("No sale: previously claimed vessel "+vesselId); return; }
                var seenBatches=new HashSet<string>(StringComparer.Ordinal);
                var cargo=new List<Tuple<TrackingState,ConfigNode>>();
                var lines=new List<string>(); var rejected=new List<string>(); var sold=new List<Tuple<OreLot,double>>(); double payout=0, units=0;
                foreach (var tank in tanks) {
                    var trackers=tank.modules.Where(m=>m.moduleName==nameof(OreMarketTank)).ToArray();
                    if(trackers.Length!=1) { rejected.Add("Missing or duplicate saved Ore tracker."); continue; }
                    var nodes=trackers[0].moduleValues.GetNodes(OreMarketTank.NodeName);
                    if(nodes.Length!=1) { rejected.Add("Missing or duplicate saved Ore provenance."); continue; }
                    var state=OreMarketTank.ReadState(nodes[0]);
                    if(!state.BindTank(tank.persistentId)){rejected.Add("Tank persistent identity mismatch.");continue;}
                    if (state.BatchId.Length>0 && Guid.TryParse(state.BatchId,out var storedBatch) && !seenBatches.Add(storedBatch.ToString("N"))) {
                        RejectCargo(vessel,"Duplicate provenance batch across tanks.","Ambiguous copied tracking cannot be sold."); return;
                    }
                    var ore=tank.resources.Single(r=>r.resourceName=="Ore");
                    double quote=RecoveryPolicy.Quote(state,vesselId,manifest,ore.amount,ore.maxAmount,true,true);
                    if(state.Invalid || quote<=0) {
                        string detail = state.VesselId != vesselId || state.Manifest != manifest ? "Saved vessel identity or part manifest does not match recovery." :
                            "Saved reason: " + nodes[0].GetValue("reason") + "; " + state.Reason;
                        rejected.Add("Tank "+tank.persistentId+": "+detail+" origin="+OreOrigins.Label(state.Origin));
                        Log.Info("Recovery tank rejected: part="+tank.persistentId+" reason="+state.Reason+" bonus="+Number(quote)); continue;
                    }
                    cargo.Add(Tuple.Create(state,nodes[0])); payout+=quote;
                    foreach(var lot in state.Lots) {
                        if(!LotQuotes.Price(lot,out double price) || price<=0)continue;
                        units+=lot.Quantity; sold.Add(Tuple.Create(lot,price));
                        string line="Verified "+OreOrigins.Label(lot.Origin)+" sold: "+Number(lot.Quantity)+" units\nPrice: "+Number(price)+" funds/unit\nRevenue: "+Number(lot.Quantity*price)+" funds";
                        lines.Add(line); Log.Info("Sale calculation: tank="+tank.persistentId+" batch="+state.BatchId+" "+line);
                    }
                }
                foreach (string rejectedTank in rejected) Log.Info("Recovery tank rejected: " + rejectedTank);
                if (!TrackingState.ValidAmount(payout) || payout<=0) {
                    RejectCargo(vessel,"No payable verified Ore.",string.Join("\n",rejected)); return;
                }
                // Once is quite enough.
                // All vessel and tank claims are reserved atomically before any funds call.
                if(!ledger.TryClaimMany(vesselId,cargo.Select(c=>c.Item1.BatchId),payout))
                { Log.Info("No sale: duplicate vessel/batch, repeated tank batch or invalid transaction"); return; }
                foreach(var item in cargo) {
                    item.Item1.Invalidate("Recovered; transaction reserved");
                    foreach(var entry in item.Item1.Save()) item.Item2.SetValue(entry.Key,entry.Value,true);
                }
                double before=Funding.Instance.Funds;
                Log.Info("Transaction reserved: vessel="+vesselId+" tanks="+cargo.Count+" units="+Number(units)+" bonus="+Number(payout)+
                    " balanceBeforeBonus="+Number(before)+" stockDialogFunds="+(dialog==null?"none":Number(dialog.fundsEarned)));
                Funding.Instance.AddFunds(payout,TransactionReasons.VesselRecovery);
                double applied=Funding.Instance.Funds-before;
                Log.Info("Transaction completed: vessel="+vesselId+" units="+Number(units)+" requested="+Number(payout)+" applied="+Number(applied)+" balance="+Number(Funding.Instance.Funds));
                Receipt("The Kerbol Exchange: sale complete",string.Join("\n",lines)+(rejected.Count==0?"":"\nUnpaid tanks:\n"+string.Join("\n",rejected))+"\nRequested bonus: "+Number(payout)+" funds\nApplied bonus: "+Number(applied)+
                    " funds\nBalance before bonus (stock refund already processed): "+Number(before)+"\nBalance after bonus: "+Number(Funding.Instance.Funds)+
                    "\nStock recovery funds are separate and may not include this bonus in the recovery dialog.\nVessel: "+vesselId+"\nBatches: "+string.Join(",",cargo.Select(c=>c.Item1.BatchId)),true);
                string description = sold.Count == 1 ? " units of " + OreOrigins.Label(sold[0].Item1.Origin) + " at " + sold[0].Item2.ToString("0.######",CultureInfo.InvariantCulture)+" funds/unit" : " verified Ore units across "+sold.Select(x=>ObjectMarkets.Commodity(x.Item1.Origin)).Distinct().Count()+" origins";
                ScreenMessages.PostScreenMessage("The Kerbol Exchange: sold "+units.ToString("0.######",CultureInfo.InvariantCulture)+description+
                    ": bonus "+applied.ToString("N2")+" funds.",10f,ScreenMessageStyle.UPPER_CENTER);
            }
            catch (Exception e)
            { Log.Error("Recovery callback failed closed; any reserved transaction will NOT retry: " + e); }
        }
    }
}











