using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace OreMarket
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class MiningObserver : MonoBehaviour
    {
        private static MiningObserver instance;
        private readonly Dictionary<PartModule, SamplingGuard> samples = new Dictionary<PartModule, SamplingGuard>();
        // Verified protected double in the local KSP 1.12.5 assembly; read only.
        private static readonly FieldInfo ConverterTime = typeof(BaseConverter).GetField("lastUpdateTime", BindingFlags.Instance | BindingFlags.NonPublic);
        private readonly Dictionary<PartModule, string> sampleBodies = new Dictionary<PartModule, string>();
        private readonly Dictionary<PartModule, string> contexts = new Dictionary<PartModule, string>();
        private bool subscribed;

        public void Awake()
        {
            if (instance != null && instance != this) { Destroy(this); return; }
            instance = this;
            GameEvents.OnResourceConverterOutput.Add(OnOutput);
            GameEvents.onPartCouple.Add(OnCouple);
            GameEvents.onPartDeCouple.Add(OnDecouple);
            GameEvents.onVesselWasModified.Add(OnModified);
            GameEvents.onPartResourceListChange.Add(OnResourceList);
            GameEvents.onVesselGoOnRails.Add(OnRails);
            GameEvents.onVesselUnloaded.Add(OnUnload);
            GameEvents.onVesselGoOffRails.Add(OnOffRails);
            subscribed = true;
            Log.Info("The Kerbol Exchange mining observer subscribed");
        }

        public void OnDestroy()
        {
            if (!subscribed) return;
            GameEvents.OnResourceConverterOutput.Remove(OnOutput);
            GameEvents.onPartCouple.Remove(OnCouple);
            GameEvents.onPartDeCouple.Remove(OnDecouple);
            GameEvents.onVesselWasModified.Remove(OnModified);
            GameEvents.onPartResourceListChange.Remove(OnResourceList);
            GameEvents.onVesselGoOnRails.Remove(OnRails);
            GameEvents.onVesselUnloaded.Remove(OnUnload);
            GameEvents.onVesselGoOffRails.Remove(OnOffRails);
            if (instance == this) instance = null;
            subscribed = false;
        }

        private static IEnumerable<OreMarketTank> Tanks(Vessel vessel) => vessel == null ?
            Enumerable.Empty<OreMarketTank>() : vessel.parts.SelectMany(p => p.Modules.Cast<PartModule>()).OfType<OreMarketTank>();

        private void OnOutput(PartModule source, string resource, double nominalOutput)
        {
            if (resource != "Ore" || source == null || source.vessel == null) return;
            try
            {
                Vessel vessel = source.vessel;
                if (ObjectMining.IsObjectDrill(source)) { ObserveObject(source, nominalOutput); return; }
                string body = vessel.mainBody?.bodyName;
                bool stock = VesselRules.StockDrill(source) && source is ModuleResourceHarvester harvester &&
                    harvester.HarvesterType == 0 && harvester.ResourceName == "Ore";
                bool supported = stock && vessel.loaded && vessel.Landed && OreOrigins.Valid(body) && ((BaseConverter)source).IsActivated;
                if (!samples.TryGetValue(source, out SamplingGuard guard)) samples[source] = guard = new SamplingGuard();
                if (!sampleBodies.TryGetValue(source, out string previousBody) || previousBody != body) guard.Reset();
                sampleBodies[source] = body;
                string context = "body=" + body + " loaded=" + vessel.loaded + " packed=" + vessel.packed +
                    " landed=" + vessel.Landed + " active=" + (source is BaseConverter c && c.IsActivated);
                if (!contexts.TryGetValue(source, out string oldContext) || context != oldContext)
                { contexts[source] = context; Log.Info("Ore output context: module=" + source.GetType().FullName + " " + context); }
                bool timestampAvailable = ConverterTime != null && ConverterTime.FieldType == typeof(double);
                bool credit = supported && timestampAvailable && guard.Accept((double)ConverterTime.GetValue(source), Planetarium.GetUniversalTime());
                if (!supported || !timestampAvailable) guard.Reset();
                if (!ProductionBudget(vessel, nominalOutput)) return;
                foreach (OreMarketTank tank in Tanks(vessel))
                {
                    if (tank.State == null) continue;
                    if (!stock) { tank.Invalidate("Unsupported Ore producer; output not attributed"); continue; }
                    if (!VesselRules.Loaded(vessel, tank, out string reason, false)) { tank.Invalidate(reason); continue; }
                    if (nominalOutput == 0 || (supported && guard.Duplicate)) { tank.Observe(); continue; }
                    bool tankCredit = credit;
                    if (tank.State.BatchId.Length == 0 && !tank.State.Invalid && tank.Capacity != tank.State.Capacity)
                    { tank.State.ObserveUnverified(tank.Amount, tank.Capacity); tankCredit = false; }
                    tank.State.ObserveCapacity(tank.Capacity);
                    double previous = tank.State.Eligible;
                    // Unsupported stock output may be recorded as UNVERIFIED to
                    // preserve prior cargo. It never authorizes marketable units.
                    string origin = !supported && tank.State.BatchId.Length > 0 ? tank.State.Origin : body;
                    if (!OreOrigins.Valid(origin))
                    {
                        if (tank.State.BatchId.Length == 0) tank.State.ObserveUnverified(tank.Amount, tank.Capacity);
                        else tank.Observe();
                    }
                    else if (tank.Amount != tank.State.LastAmount || Tanks(vessel).Count() == 1) {
                        if (!supported) tank.State.MiningUncredited(Portion(vessel,tank,nominalOutput),tank.Amount);
                        else tank.State.Mining(origin, Portion(vessel, tank, nominalOutput), tank.Amount, tankCredit, vessel.id.ToString("N"), VesselRules.Manifest(vessel.parts));
                    }
                    if (!supported) tank.State.Pause("stock output outside verifiable loaded surface context; new output uncredited");
                    else if (!timestampAvailable) tank.State.Pause("converter timestamp unavailable; new output uncredited");
                    else if (!tankCredit) tank.State.Pause("re-establishing converter continuity; new output uncredited");
                    tank.Refresh();
                    if (Math.Floor(previous) != Math.Floor(tank.State.Eligible))
                        Log.Info("Mining: tank=" + tank.part.persistentId + " origin=" + tank.State.Origin +
                            " actual=" + tank.Amount.ToString("R") + " verified=" + tank.State.Eligible.ToString("R") +
                            " nominal=" + nominalOutput.ToString("R") + " credited=" + tankCredit + " packed=" + vessel.packed);
                }
            }
            catch (Exception e)
            {
                foreach (OreMarketTank tank in Tanks(source.vessel)) tank.Invalidate("Mining observer exception");
                Log.Error("Mining callback failed closed: " + e);
            }
        }


        private void ObserveObject(PartModule source, double nominal)
        {
            Vessel vessel = source.vessel;
            if (!samples.TryGetValue(source, out SamplingGuard guard)) samples[source] = guard = new SamplingGuard();
            bool supported = ObjectMining.TrySource(source, out string origin, out uint sourceId, out string reason);
            string key = origin + ":" + sourceId;
            if (!sampleBodies.TryGetValue(source, out string previous) || previous != key) guard.Reset();
            sampleBodies[source] = key;
            bool timestamp = ConverterTime != null && ConverterTime.FieldType == typeof(double);
            bool credit = supported && timestamp && guard.Accept((double)ConverterTime.GetValue(source), Planetarium.GetUniversalTime());
            if (!supported || !timestamp) guard.Reset();
            if (!ProductionBudget(vessel, nominal)) return;
            foreach (OreMarketTank tank in Tanks(vessel))
            {
                if (tank.State == null) continue;
                if (!VesselRules.Loaded(vessel, tank, out string custody, false)) { tank.Invalidate(custody); continue; }
                if (nominal == 0 || (supported && guard.Duplicate)) { tank.Observe(); continue; }
                if (!supported)
                {
                    if (tank.State.BatchId.Length == 0) tank.State.ObserveUnverified(tank.Amount, tank.Capacity);
                    else if (tank.Amount != tank.State.LastAmount || Tanks(vessel).Count() == 1)
                        tank.State.MiningUncredited(Portion(vessel, tank, nominal), tank.Amount);
                    tank.State.Pause(reason + "; object output uncredited");
                }
                else
                {
                    bool tankCredit = credit;
                    if (tank.State.BatchId.Length == 0 && !tank.State.Invalid && tank.Capacity != tank.State.Capacity)
                    { tank.State.ObserveUnverified(tank.Amount, tank.Capacity); tankCredit = false; }
                    tank.State.ObserveCapacity(tank.Capacity);
                    if (tank.Amount != tank.State.LastAmount || Tanks(vessel).Count() == 1)
                        tank.State.MiningObject(origin, Portion(vessel, tank, nominal), tank.Amount, tankCredit, vessel.id.ToString("N"), VesselRules.Manifest(vessel.parts), sourceId);
                    if (!tankCredit) tank.State.Pause("re-establishing object converter continuity; new output uncredited");
                }
                tank.Refresh();
            }
            string context = supported ? "verified source=" + sourceId + " market=" + origin : reason;
            if (!contexts.TryGetValue(source, out string old) || old != context)
            { contexts[source] = context; Log.Info("Object mining: " + context + "; stock drill state is never modified"); }
        }

        // Stock callback is vessel-wide: never spend the nominal output once per tank.
        private static double Portion(Vessel vessel, OreMarketTank tank, double nominal)
            => Tanks(vessel).Count() == 1 ? nominal : Math.Max(TrackingState.Epsilon, tank.Amount - tank.State.LastAmount);
        private static bool ProductionBudget(Vessel vessel, double nominal)
        {
            var tanks = Tanks(vessel).Where(t => t.State != null).ToArray();
            if (tanks.Length <= 1) return true;
            if (!StockOreTransfers.ProductionReady) {
                foreach (var tank in tanks) if (tank.Amount != tank.State.LastAmount) tank.Invalidate("Multi-tank production observer unavailable; affected balance unverified");
                return false;
            }
            double increase = tanks.Sum(t => Math.Max(0, t.Amount - t.State.LastAmount));
            if (TrackingState.ValidAmount(nominal) && TrackingState.ValidAmount(increase) && increase <= nominal + TrackingState.Epsilon) return true;
            foreach (var tank in tanks) if (tank.Amount != tank.State.LastAmount) tank.Invalidate("Vessel Ore balance exceeds witnessed stock output");
            return false;
        }

        private void OnRails(Vessel vessel)
        {
            // Packing is a stock warp transition, not a loss of loaded custody.
            foreach (OreMarketTank tank in Tanks(vessel))
            { tank.State?.Pause("rails transition; awaiting stock production callback"); tank.RefreshIfReady(); }
            Log.Info("Rails transition; existing verified cargo retained, vessel=" + vessel?.id);
        }
        private void OnOffRails(Vessel vessel)
        {
            foreach (OreMarketTank tank in Tanks(vessel)) { tank.State?.Resume(); tank.RefreshIfReady(); }
        }
        private void OnUnload(Vessel vessel)
        {
            foreach (PartModule module in samples.Keys.Where(m => m == null || m.vessel == vessel).ToArray())
            { samples.Remove(module); sampleBodies.Remove(module); contexts.Remove(module); }
            foreach (OreMarketTank tank in Tanks(vessel))
            { tank.State?.Pause("vessel unloaded; background production is not credited"); tank.RefreshIfReady(); }
        }
        private static void BreakCustody(Vessel vessel, string reason)
        {
            foreach (OreMarketTank tank in Tanks(vessel))
                if (tank.State != null && tank.State.BatchId.Length > 0) tank.Invalidate(reason);
        }
        private void OnCouple(GameEvents.FromToAction<Part, Part> pair)
        { if(pair.from?.vessel!=null&&pair.from.vessel==pair.to?.vessel&&StockCargoLogistics.Unchanged(pair.from.vessel))return; if(StockCargoLogistics.Defers(pair.from?.vessel)||StockCargoLogistics.Defers(pair.to?.vessel))return; BreakCustody(pair.from?.vessel, "Docking/coupling after mining"); BreakCustody(pair.to?.vessel, "Docking/coupling after mining"); }
        private void OnDecouple(Part part) { if(!SafeObjectRelease.Defers(part?.vessel)&&!StockCargoLogistics.Defers(part?.vessel))BreakCustody(part?.vessel,"Staging/undocking after mining"); }
        private void OnResourceList(Part part)
        {
            // The event does not name the resource or guarantee an Ore-only edit.
            // Keep the existing invalidation; capture the first valid-batch event
            // so startup/compatibility causes can be distinguished in KSP.
            try
            {
            foreach (OreMarketTank tank in Tanks(part?.vessel))
                if (tank.State != null && !tank.State.Invalid && tank.State.BatchId.Length > 0)
                    Log.Info("Resource-list custody invalidation: sourcePart=" + part?.partInfo?.name + " id=" + part?.persistentId +
                        " vessel=" + part?.vessel?.id + " loaded=" + part?.vessel?.loaded + " packed=" + part?.vessel?.packed +
                        " resources=" + string.Join(",", part.Resources.Cast<PartResource>().Select(r => r.resourceName)) +
                        " tank=" + tank.part.persistentId + " actual=" + tank.Amount.ToString("R") + " last=" + tank.State.LastAmount.ToString("R") +
                        " capacity=" + tank.Capacity.ToString("R") + " priorCapacity=" + tank.State.Capacity.ToString("R") + " batch=" + tank.State.BatchId +
                        " caller=" + new System.Diagnostics.StackTrace(1, false));
            }
            catch (Exception e) { Log.Info("Resource-list diagnostic unavailable: " + e.GetType().Name); }
            finally { BreakCustody(part?.vessel, "Resource list modified after mining"); }
        }
        private void OnModified(Vessel vessel)
        {
            if(SafeObjectRelease.Defers(vessel)||StockCargoLogistics.Defers(vessel))return;
            foreach (OreMarketTank tank in Tanks(vessel))
                if (tank.State != null && tank.State.BatchId.Length > 0 && !VesselRules.Loaded(vessel, tank, out string reason, false)) tank.Invalidate(reason);
        }
    }
}









