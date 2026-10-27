using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace OreMarket
{
    // Eligibility only removes stock-selected candidates; stock owns all quantities,
    // priorities, flow locks, capacity checks, resource costs and failure results.
    internal static class SingleOriginStorage
    {
        [ThreadStatic] private static Scope current;
        internal sealed class Scope
        {
            internal Scope Previous;
            internal Vessel Vessel;
            internal string Origin;
            internal bool Closed;
        }
        internal static bool TransferAllowed(OreMarketTank destination, IEnumerable<OreMarketTank> sources, out string origin)
        {
            origin="";
            if(destination?.State==null || destination.State.Invalid || destination.State.LegacyMixed)return false;
            string selected=null;
            foreach(var source in sources) {
                if(source?.State==null || source.State.LegacyMixed)return false;
                if(source.Amount==0)continue;
                string item=source.State.TransferOrigin;
                if(selected!=null && !ObjectMarkets.SameCommodity(selected,item))return false;
                selected=item;
            }
            origin=selected??"";
            return destination.State.AcceptsOrigin(origin);
        }
        internal static Scope Enter(PartModule source)
        {
            var s=new Scope {Previous=current,Vessel=source.vessel,Origin=""};current=s;
            if(ObjectMining.IsObjectDrill(source)) {
                if(ObjectMining.TrySource(source,out string origin,out _,out _))s.Origin=origin;
            } else if(VesselRules.StockDrill(source) && source is ModuleResourceHarvester h && h.ResourceName=="Ore" && h.HarvesterType==0 &&
                source.vessel.loaded && source.vessel.Landed && ((BaseConverter)source).IsActivated && OreOrigins.Valid(source.vessel.mainBody?.bodyName))
                s.Origin=source.vessel.mainBody.bodyName;
            return s;
        }
        internal static void Exit(Scope scope)
        {
            if(scope==null || scope.Closed)return;
            scope.Closed=true;current=scope.Previous;
        }
        internal static bool SupportedRecipe(ConversionRecipe recipe)
        {
            var ore=PartResourceLibrary.Instance.GetDefinition("Ore");
            if(ore==null || recipe==null)return false;
            foreach(var output in recipe.Outputs.Where(r=>r.ResourceName=="Ore")) {
                var mode=output.FlowMode==ResourceFlowMode.NULL ? ore.resourceFlowMode : output.FlowMode;
                if((int)mode<1 || (int)mode>7)return false; // Non-PartSet/no-flow routing is deliberately unsupported.
            }
            return true;
        }
        internal static void Install(Harmony harmony)
        {
            var target=typeof(PartSet).GetMethod("GetOrCreateList",BindingFlags.Instance|BindingFlags.NonPublic,
                null,new[]{typeof(int),typeof(bool),typeof(bool)},null);
            if(target==null)throw new MissingMethodException("KSP PartSet.GetOrCreateList(int,bool,bool)");
            harmony.Patch(target,null,new HarmonyMethod(typeof(SingleOriginStorage),nameof(Filter)));
        }
        private static PartSet.ResourcePrioritySet EmptySet() => new PartSet.ResourcePrioritySet {
            lists=new List<List<PartResource>>(),set=new HashSet<PartResource>() };
        private static void Filter(int __0, bool __1, ref PartSet.ResourcePrioritySet __result)
        {
            var scope=current;
            if(scope==null || __1 || __result==null)return; // Consumption and other resource calls remain stock.
            var ore=PartResourceLibrary.Instance.GetDefinition("Ore");
            if(ore==null || __0!=ore.id)return;
            try {
                var original=__result;
                var all=original.lists.SelectMany(g=>g).ToArray();
                if(!all.Any(r=>r.part?.vessel==scope.Vessel))return;
                var replacement=EmptySet();
                bool removed=false;
                foreach(var group in original.lists) {
                    var candidates=new List<PartResource>();
                    foreach(var resource in group) {
                        var tracker=resource.part?.Modules.Cast<PartModule>().OfType<OreMarketTank>().SingleOrDefault();
                        bool allowed=resource.part?.vessel==scope.Vessel && scope.Origin.Length>0 && tracker?.State!=null &&
                            ReferenceEquals(resource,tracker.part.Resources.Get("Ore")) && tracker.State.AcceptsOrigin(scope.Origin);
                        if(allowed){candidates.Add(resource);replacement.set.Add(resource);}else removed=true;
                    }
                    if(candidates.Count>0)replacement.lists.Add(candidates);
                }
                if(removed)__result=replacement; // Never mutate the shared stock cache.
            } catch(Exception e) {
                __result=EmptySet();
                Log.Error("Ore storage routing failed closed: "+e.GetType().Name);
            }
        }
    }
}
