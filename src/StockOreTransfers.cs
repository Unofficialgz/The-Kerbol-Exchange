using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace OreMarket
{
    // Bracket the *stock UI transaction*, not a timing window or a balance heuristic.
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public sealed class StockOreTransfers : MonoBehaviour
    {
        private const string PatchId = "OreMarket.StockOreTransfers.072";
        private static Harmony harmony;
        internal static bool ProductionReady { get; private set; }
        private static readonly FieldInfo ResourceField = typeof(UIPartActionResourceTransfer).BaseType.GetField("resource",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
        private static PartResource Resource(UIPartActionResourceTransfer item) => ResourceField?.GetValue(item) as PartResource;
        private static readonly HashSet<string> reports = new HashSet<string>(StringComparer.Ordinal);
        private static void Report(Session session, string kind, string detail)
        {
            string key=session.Vessel.id+":"+session.Destination.persistentId+":"+kind;
            if(reports.Count>=512)reports.Clear();
            if(reports.Add(key))Log.Info("Stock Ore transfer "+kind+": vessel="+session.Vessel.id+" destination="+session.Destination.persistentId+" "+detail+" (further identical contexts omitted)");
        }
        private static readonly HashSet<OreMarketTank> busy = new HashSet<OreMarketTank>();
        internal static bool Busy(OreMarketTank tank) => busy.Contains(tank);
        public void Awake()
        {
            if (harmony != null) { Destroy(this); return; }
            try {
                harmony = new Harmony(PatchId);
                var target = typeof(UIPartActionResourceTransfer).GetMethod("UpdateItem", BindingFlags.Instance | BindingFlags.Public);
                harmony.Patch(target, new HarmonyMethod(typeof(StockOreTransfers), nameof(Before)),
                    new HarmonyMethod(typeof(StockOreTransfers), nameof(After)), null,
                    new HarmonyMethod(typeof(StockOreTransfers), nameof(Finally)));
                var recipe = typeof(ResourceConverter).GetMethod("ProcessRecipe",BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public);
                harmony.Patch(recipe,new HarmonyMethod(typeof(StockOreTransfers),nameof(BeforeProduction)),
                    new HarmonyMethod(typeof(StockOreTransfers),nameof(AfterProduction)),null,
                    new HarmonyMethod(typeof(StockOreTransfers),nameof(FinallyProduction)));
                SingleOriginStorage.Install(harmony);
                SafeObjectRelease.Install(harmony);
                StockCargoLogistics.Install(harmony);
                ProductionReady=true;
                Log.Info("Stock same-vessel Ore transfer observer installed (Harmony 2.2.1); resource mechanics unchanged");
            } catch (Exception e) { Log.Error("Stock transfer observation unavailable; transfers remain unverified: " + e); }
        }
        private static bool BeforeProduction(ConversionRecipe __1, PartModule __3, ref ConverterResults __result, out SingleOriginStorage.Scope __state)
        {
            __state=null;
            if (__3?.vessel == null || !(VesselRules.StockDrill(__3) || ObjectMining.IsObjectDrill(__3))) return true;
            try {
                foreach(var tank in __3.vessel.parts.SelectMany(p=>p.Modules.Cast<PartModule>()).OfType<OreMarketTank>()) tank.Observe();
                if(!ProductionReady || !SingleOriginStorage.SupportedRecipe(__1)) {
                    __result=new ConverterResults { Status="The Kerbol Exchange: unsupported Ore storage routing",TimeFactor=0 };return false;
                }
                __state=SingleOriginStorage.Enter(__3);return true;
            } catch(Exception e) {
                SingleOriginStorage.Exit(__state);__state=null;
                __result=new ConverterResults {Status="The Kerbol Exchange: Ore storage checks unavailable",TimeFactor=0};
                Log.Error("Pre-production balance observation failed closed: "+e);return false;
            }
        }
        private static void AfterProduction(SingleOriginStorage.Scope __state) => SingleOriginStorage.Exit(__state);
        private static Exception FinallyProduction(Exception __exception, SingleOriginStorage.Scope __state)
        { SingleOriginStorage.Exit(__state); return __exception; }

        private static bool Before(UIPartActionResourceTransfer __instance, out Session __state)
        {
            __state = null;
            try {
                if (Resource(__instance)?.resourceName != "Ore" || (int)__instance.State != 2) return true;
                Part destination = Resource(__instance).part;
                var sources = __instance.Targets.Where(t => t != null && (int)t.State == 1 && t.gameObject.activeInHierarchy)
                    .Select(Resource).ToArray();
                if (sources.Length == 0) return true;
                if (sources.Any(r => r == null || r.resourceName != "Ore" || r.part == null)) { __instance.FlowStop(); return false; }
                __state = Begin(destination, sources.Select(r => r.part).ToArray());
                if(__state==null || !__state.Safe) {
                    if(__state!=null)Complete(__state,false);
                    __instance.FlowStop();
                    LogBlocked(destination);
                    return false; // No stock mutation and no fictitious success result.
                }
                return true;
            } catch (Exception e) {
                Log.Error("Ore transfer capture failed closed: " + e.GetType().Name);
                if(__state!=null&&!__state.Finished)Complete(__state,true);
                try { __instance.FlowStop(); } catch(Exception) { }
                return false;
            }
        }
        private static void LogBlocked(Part destination)
        {
            string key="blocked:"+destination?.vessel?.id+":"+destination?.persistentId;
            if(reports.Count>=512)reports.Clear();
            if(reports.Add(key)) {
                Log.Info("Ore transfer blocked before resource changes: incompatible origin, legacy mixed cargo or unsupported custody; destination="+destination?.persistentId);
                ScreenMessages.PostScreenMessage("The Kerbol Exchange: transfer blocked. Use an empty tank or matching Ore. Legacy mixed cargo must be consumed or recovered.",6f,ScreenMessageStyle.UPPER_CENTER);
            }
        }
        private static void After(Session __state) { if (__state != null) Complete(__state, false); }
        private static Exception Finally(Exception __exception, Session __state)
        { if (__state != null && !__state.Finished) Complete(__state, true); return __exception; }

        internal sealed class Balance
        {
            internal OreMarketTank Tank;
            internal PartResource Resource;
            internal double Amount, Capacity, Eligible;
            internal bool Source;
        }
        internal sealed class Session
        {
            internal Vessel Vessel;
            internal string Manifest;
            internal Part Destination;
            internal Balance[] Balances;
            internal bool Safe, Finished;
            internal string Assignment;
            internal uint AssignmentSource;
        }
        internal static Session Begin(Part destination, Part[] sources)
        {
            var vessel = destination?.vessel;
            if (vessel == null || !vessel.loaded) return null;
            var tanks = vessel.parts.SelectMany(p => p.Modules.Cast<PartModule>()).OfType<OreMarketTank>().ToArray();
            if (tanks.Any(t => Busy(t) || t.State == null)) return null;
            foreach (var tank in tanks) tank.Observe(); // Reconcile before any stock resource mutation.
            var s = new Session { Vessel = vessel, Destination = destination, Manifest = VesselRules.Manifest(vessel.parts),
                Safe = sources.Length > 0 && sources.Distinct().Count() == sources.Length && !sources.Contains(destination) &&
                    sources.All(p => p != null && p.vessel == vessel && vessel.parts.Contains(p)) && vessel.parts.Contains(destination) };
            s.Balances = tanks.Select(t => new Balance { Tank=t, Resource=t.part.Resources.Get("Ore"), Amount=t.Amount,
                Capacity=t.Capacity, Eligible=t.State.Eligible, Source=sources.Contains(t.part) }).ToArray();
            s.Safe &= tanks.Any(t => t.part == destination) && sources.All(p => tanks.Any(t => t.part == p));
            foreach (var t in tanks) if (!VesselRules.Loaded(vessel,t,out _,false)) s.Safe=false;
            var dest=tanks.SingleOrDefault(t=>t.part==destination);
            var donors=tanks.Where(t=>sources.Contains(t.part)).ToArray();
            s.Safe &= SingleOriginStorage.TransferAllowed(dest,donors,out s.Assignment);
            s.AssignmentSource=donors.FirstOrDefault(t=>t.Amount>0&&t.State.TransferOrigin==s.Assignment)?.State.ObjectSourceId??0;
            foreach (var t in tanks) busy.Add(t);
            return s;
        }
        internal static void Complete(Session s, bool failed)
        {
            if (s.Finished) return;
            s.Finished=true;
            try {
                bool valid = s.Safe && !failed && s.Vessel.loaded && s.Manifest == VesselRules.Manifest(s.Vessel.parts);
                double added=0, removed=0;
                foreach (var b in s.Balances) {
                    var t=b.Tank;
                    double delta=t.Amount-b.Amount;
                    valid &= t.vessel==s.Vessel && ReferenceEquals(b.Resource,t.part.Resources.Get("Ore")) &&
                        t.Capacity==b.Capacity && TrackingState.ValidAmount(t.Amount) && t.Amount<=t.Capacity &&
                        t.State.LastAmount==b.Amount && t.State.Eligible==b.Eligible;
                    if (delta>0) { valid &= t.part==s.Destination; added+=delta; }
                    if (delta<0) { valid &= b.Source; removed-=delta; }
                }
                valid &= TrackingState.ValidAmount(added) && TrackingState.ValidAmount(removed) &&
                    Math.Abs(added-removed)<=TrackingState.Epsilon && (added==0)==(removed==0);
                if (!valid) {
                    foreach (var b in s.Balances)
                        if (b.Tank.Amount!=b.Amount || b.Tank.Capacity!=b.Capacity)
                            b.Tank.Invalidate("Unverifiable stock Ore transfer; affected tank rejected");
                    Report(s,"rejected","added="+added+" removed="+removed+" failed="+failed);
                    return;
                }
                if (added==0) return;
                var incoming=new List<OreLot>();
                // Debit ALL sources before adding any destination provenance.
                foreach (var b in s.Balances.Where(b=>b.Tank.Amount<b.Amount)) {
                    incoming.AddRange(b.Tank.State.RemoveTransfer(b.Amount-b.Tank.Amount, b.Tank.Amount));
                    b.Tank.State.Observe(b.Tank.Amount);
                }
                if (removed > added) foreach (var lot in incoming) lot.Quantity *= added / removed;
                var dest=s.Balances.Single(b=>b.Tank.part==s.Destination).Tank;
                dest.State.ReceiveTransfer(dest.Amount,incoming,s.Vessel.id.ToString("N"),s.Manifest,s.Assignment,s.AssignmentSource);
                Report(s,"verified","physical="+added+" verified="+incoming.Sum(l=>l.Quantity));
            } catch(Exception e) {
                foreach(var b in s.Balances) if(b.Tank.Amount!=b.Amount) b.Tank.Invalidate("Ore transfer observer exception");
                Log.Error("Ore transfer failed closed: "+e);
            } finally { foreach(var b in s.Balances) { busy.Remove(b.Tank); b.Tank.RefreshIfReady(); } }
        }
    }
}
