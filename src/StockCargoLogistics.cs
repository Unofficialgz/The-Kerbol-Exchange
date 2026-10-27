using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
namespace OreMarket {
 // One synchronous stock topology transaction; no elapsed-time acceptance window.
 internal static class StockCargoLogistics {
  private static Scope active;
  [ThreadStatic] private static int grappleDepth;
  internal sealed class TankRecord { internal OreMarketTank Tank;internal TrackingState State;internal double Amount,Capacity;internal PartResource Resource;internal string Proof; }
  internal sealed class Scope { internal Part[] Parts;internal uint[] Ids;internal TankRecord[] Tanks;internal bool Finished; }
  internal static bool Unchanged(Vessel v) {
   if(v==null||!v.loaded)return false;
   var tanks=v.parts.SelectMany(p=>p.Modules.Cast<PartModule>()).OfType<OreMarketTank>().ToArray();
   return tanks.Length>0&&tanks.All(t=>t.State!=null&&!t.State.Invalid&&!StockOreTransfers.Busy(t)&&t.Amount==t.State.LastAmount&&t.Capacity==t.State.Capacity&&VesselRules.Loaded(v,t,out _));
  }
  internal static bool Holds(OreMarketTank t)=>active!=null&&active.Tanks.Any(r=>ReferenceEquals(r.Tank,t));
  internal static bool Defers(Vessel v)=>active!=null&&active.Parts.Any(p=>p.vessel==v);
  internal static void Install(Harmony h) {
   foreach(string name in new[]{"Release","Decouple"})h.Patch(typeof(ModuleGrappleNode).GetMethod(name),new HarmonyMethod(typeof(StockCargoLogistics),nameof(GrappleBefore)),new HarmonyMethod(typeof(StockCargoLogistics),nameof(GrappleAfter)),null,new HarmonyMethod(typeof(StockCargoLogistics),nameof(GrappleFinally)));
   foreach(string name in new[]{"Couple","Undock","decouple"}) {
    var method=typeof(Part).GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Single(m=>m.Name==name&&m.GetParameters().Length==1);
    h.Patch(method,new HarmonyMethod(typeof(StockCargoLogistics),nameof(Before)),new HarmonyMethod(typeof(StockCargoLogistics),nameof(After)),null,new HarmonyMethod(typeof(StockCargoLogistics),nameof(Finally)));
   }
  }
  private static void GrappleBefore(out bool __state){__state=true;grappleDepth++;}
  private static void GrappleAfter(ref bool __state){if(__state){__state=false;grappleDepth--;}}
  private static Exception GrappleFinally(Exception __exception,ref bool __state){GrappleAfter(ref __state);return __exception;}
  private static void Before(Part __instance,object __0, out Scope __state) {
   __state=null;
   if(active!=null||grappleDepth>0)return; // Claw release belongs to the stricter object-release observer.
   try {__state=Begin(__instance.vessel,(__0 as Part)?.vessel);}catch(Exception e){Log.Info("Cargo topology witness unavailable: "+e.GetType().Name);}
  }
  private static void After(Scope __state){if(__state!=null)Complete(__state,false);}
  private static Exception Finally(Exception __exception,Scope __state){if(__state!=null&&!__state.Finished)Complete(__state,true);return __exception;}
  private static string Proof(TrackingState s)=>string.Join(";",s.Save().Where(e=>e.Key!="vesselId"&&e.Key!="manifest"&&e.Key!="reason").OrderBy(e=>e.Key).Select(e=>e.Key+"="+e.Value));
  internal static Scope Begin(Vessel first,Vessel second=null) {
   if(active!=null||first==null||!first.loaded||second!=null&&!second.loaded)return null;
   var vessels=new[]{first,second}.Where(v=>v!=null).Distinct().ToArray();
   var parts=vessels.SelectMany(v=>v.parts).ToArray();
   if(parts.Length==0||parts.Any(p=>p==null||p.partInfo==null||p.persistentId==0)||parts.Select(p=>p.persistentId).Distinct().Count()!=parts.Length)return null;
   var tanks=parts.SelectMany(p=>p.Modules.Cast<PartModule>()).OfType<OreMarketTank>().ToArray();
   if(tanks.Length==0)return null;
   foreach(var t in tanks)t.Observe();
   if(tanks.Any(t=>t.State==null||StockOreTransfers.Busy(t)||!VesselRules.Loaded(t.vessel,t,out _)))return null;
   var batches=new HashSet<Guid>();
   foreach(var t in tanks)if(t.State.BatchId.Length>0&&(!Guid.TryParse(t.State.BatchId,out var id)||!batches.Add(id)))return null;
   var s=new Scope {Parts=parts,Ids=parts.Select(p=>p.persistentId).ToArray(),Tanks=tanks.Select(t=>new TankRecord{Tank=t,State=t.State,Amount=t.Amount,Capacity=t.Capacity,Resource=t.part.Resources.Get("Ore"),Proof=Proof(t.State)}).ToArray()};
   active=s;return s;
  }
  internal static void Complete(Scope s,bool failed) {
   if(s.Finished)return;s.Finished=true;
   try {
    bool valid=!failed&&s.Parts.Select((p,i)=>p!=null&&p.persistentId==s.Ids[i]&&p.vessel!=null&&p.vessel.loaded&&p.vessel.id!=Guid.Empty).All(x=>x);
    var results=valid?s.Parts.Select(p=>p.vessel).Distinct().ToArray():new Vessel[0];
    valid &= results.Length>=1&&results.Length<=2;
    foreach(var v in results) {
     var expected=s.Parts.Where(p=>p.vessel==v).ToArray();
     valid &= v.parts.Count==expected.Length&&v.parts.Distinct().Count()==v.parts.Count&&v.parts.All(p=>expected.Contains(p));
    }
    valid &= results.Select(v=>v.id).Distinct().Count()==results.Length;
    valid &= s.Tanks.All(r=>r.Tank!=null&&ReferenceEquals(r.State,r.Tank.State)&&r.Tank.Amount==r.Amount&&r.Tank.Capacity==r.Capacity&&ReferenceEquals(r.Resource,r.Tank.part.Resources.Get("Ore"))&&Proof(r.State)==r.Proof&&r.State.TankId==r.Tank.part.persistentId);
    active=null;
    if(valid){foreach(var r in s.Tanks)r.State.RebindAfterLogistics(r.Tank.vessel.id.ToString("N"),VesselRules.Manifest(r.Tank.vessel.parts));foreach(var r in s.Tanks)if(!r.State.Invalid&&!VesselRules.Loaded(r.Tank.vessel,r.Tank,out _))valid=false;}
    if(!valid)foreach(var r in s.Tanks)r.Tank.Invalidate("Unverified stock vessel topology transaction");
    Log.Info("Stock cargo topology "+(valid?"verified":"rejected")+": tanks="+s.Tanks.Length+" resultingVessels="+results.Length);
   }catch(Exception e){foreach(var r in s.Tanks)r.Tank.Invalidate("Stock cargo topology exception");Log.Error("Cargo topology failed closed: "+e);}finally{active=null;foreach(var r in s.Tanks)r.Tank.RefreshIfReady();}
  }
 }
}
