using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OreMarket
{
    public sealed class OreMarketTank : PartModule
    {
        internal const string NodeName = "OREMARKET_TRACKING";
        internal TrackingState State { get; private set; }
        private bool started;
        private bool observingPhysics;

        [KSPField(guiActive = true, guiName = "Marketable Ore")]
        public string saleable = "0";
        [KSPField(guiActive = false, guiName = "Exchange status")]
        public string trackingStatus = "Awaiting flight";

        [KSPField(guiActive = false, guiName = "Verified Ore")]
        public string verifiedOre = "0";
        [KSPField(guiActive = true, guiName = "Ore Origin")]
        public string oreOrigin = "Unverified Ore";

        internal double Amount
        {
            get
            {
                PartResource ore = part.Resources.Get("Ore");
                return ore == null || !TrackingState.ValidAmount(ore.maxAmount) || ore.amount > ore.maxAmount ? double.NaN : ore.amount;
            }
        }

        internal double Capacity => part.Resources.Get("Ore")?.maxAmount ?? double.NaN;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            if (!HighLogic.LoadedSceneIsFlight) return;
            if (State == null) State = TrackingState.New(Amount, Capacity);
            else State.ObserveUnverified(Amount, Capacity);
            State.BindTank(part.persistentId);
            started = true;
            Refresh();
            Log.Info("Tank started: part=" + part.persistentId + " eligible=" + State.Eligible.ToString("R", CultureInfo.InvariantCulture) + " status=" + State.Reason);
        }

        // Compare Ore amount/capacity every loaded physics step.
        public void FixedUpdate()
        {
            if (!started || vessel == null || !vessel.loaded) return;
            observingPhysics = true;
            if (Amount != State.LastAmount || Capacity != State.Capacity) Observe();
        }

        internal void Observe()
        {
            if (State == null || StockOreTransfers.Busy(this) || SafeObjectRelease.Holds(this) || StockCargoLogistics.Holds(this)) return;
            bool wasInvalid = State.Invalid;
            State.ObserveUnverified(Amount, Capacity);
            if (!wasInvalid && State.Invalid) Log.Info("Tank invalidated: " + State.Reason);
            Refresh();
        }

        // Generic stock Part.SendEvent protocol emitted by the inspected scaler.
        // Startup mesh restoration is checked by persisted amount/capacity; it is
        // not itself a new flight resize. Once physics runs, an actual scale
        // operation invalidates even if capacity is restored before the next tick.
        [KSPEvent(active = true, guiActive = false, guiActiveEditor = false)]
        public void OnPartScaleChanged(BaseEventDetails details)
        {
            if (!started || State == null) return;
            if (observingPhysics && vessel != null)
            {
                float relative = float.NaN;
                try { if (details != null) relative = details.Get<float>("factorRelative"); }
                catch (System.Exception) { } // Missing/malformed event details fail closed.
                bool wasInvalid = State.Invalid;
                if (float.IsNaN(relative) || float.IsInfinity(relative) || relative != 1f)
                    State.OnScaleOperation();
                if (!wasInvalid && State.Invalid) Log.Info("Tank invalidated: " + State.Reason);
            }
            Observe();
        }
        internal void Invalidate(string reason)
        {
            if (State == null) return;
            if (!State.Invalid) Log.Info("Tank invalidated: " + reason);
            State.Invalidate(reason);
            Refresh();
        }

        internal void RefreshIfReady() { if (State != null) Refresh(); }

        internal void Refresh()
        {
            verifiedOre = State.Eligible.ToString("0.######", CultureInfo.InvariantCulture);
            var origins = State.Lots.GroupBy(l => ObjectMarkets.Commodity(l.Origin)).ToArray();
            oreOrigin = State.LastAmount==0 ? "Waiting for Ore" : origins.Length <= 1 ? OreOrigins.Label(origins.Length == 1 ? origins[0].Key : State.Origin) :
                string.Join(", ", origins.Select(g => OreOrigins.Label(g.Key) + " " + g.Sum(l => l.Quantity).ToString("0.######", CultureInfo.InvariantCulture)));
            double marketable=State.Lots.Where(l => LotQuotes.Price(l, out _)).Sum(l => l.Quantity);
            saleable=marketable.ToString("0.######",CultureInfo.InvariantCulture);
            if(State.LastAmount>0 && State.Eligible==0)oreOrigin="Unverified Ore";
            if(State.LastAmount>0 && State.Eligible==0)saleable+=" (unverified)";
            else if(State.Eligible>0 && State.Eligible<State.LastAmount)saleable+=" / "+State.LastAmount.ToString("0.######",CultureInfo.InvariantCulture)+" total (partly verified)";
            trackingStatus = State.Reason;
            if(!State.Invalid&&!State.LegacyMixed&&State.Eligible>0&&marketable==0)trackingStatus=State.Origin=="Kerbin"?"Kerbin Ore has no sale value":"Market price unavailable; verified Ore retained";
            var status=Fields[nameof(trackingStatus)];
            if(status!=null)status.guiActive=State.Invalid||State.LegacyMixed||State.Reason.StartsWith("Tracking paused:",System.StringComparison.Ordinal)||State.Eligible>0&&marketable==0&&State.Origin!="Kerbin";

        }

        [KSPEvent(guiActive = true, guiName = "Reset empty Ore tracking")]
        public void ResetEmptyBatch()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null || !vessel.Landed || Amount != 0) return;
            State = TrackingState.New(0, Capacity);
            Refresh();
            Log.Info("Empty tank reset on " + vessel.mainBody.bodyName + ": part=" + part.persistentId);
        }

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            if (!started || State == null) return;
            Observe();
            if (State.BatchId.Length > 0 && vessel != null && vessel.loaded && !VesselRules.Loaded(vessel, this, out string reason)) Invalidate(reason);
            node.RemoveNodes(NodeName);
            ConfigNode tracking = node.AddNode(NodeName);
            foreach (var entry in State.Save()) tracking.AddValue(entry.Key, entry.Value);
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            observingPhysics = false;
            ConfigNode tracking = node.GetNode(NodeName);
            State = tracking == null ? null : ReadState(tracking);
        }

        internal static TrackingState ReadState(ConfigNode node)
        {
            var values = new Dictionary<string, string>();
            foreach (ConfigNode.Value value in node.values) values[value.name] = value.value;
            return TrackingState.Load(values);
        }
    }
}









