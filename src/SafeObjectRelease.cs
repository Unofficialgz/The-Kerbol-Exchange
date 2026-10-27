using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
namespace OreMarket {
 internal static class SafeObjectRelease {
  private static Scope active;
  private static readonly FieldInfo Other=typeof(ModuleGrappleNode).GetField("otherPart",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
  private static readonly FieldInfo Node=typeof(ModuleGrappleNode).GetField("grappleNode",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
  internal sealed class Cargo { internal OreMarketTank Tank; internal TrackingState State; internal PartResource Resource; internal double Amount,Capacity; internal string Saved; }
  internal sealed class Scope { internal Part Rock; internal Part[] Parts; internal uint[] Ids; internal Cargo[] Cargo; internal bool Finished; }
  internal static bool Holds(OreMarketTank tank)=>active!=null&&active.Cargo.Any(c=>ReferenceEquals(c.Tank,tank));
  internal static bool Defers(Vessel vessel)=>active!=null&&active.Cargo.Any(c=>c.Tank.vessel==vessel);
  internal static void Install(Harmony h) {
   foreach(string name in new[]{"Release","Decouple"})h.Patch(typeof(ModuleGrappleNode).GetMethod(name),new HarmonyMethod(typeof(SafeObjectRelease),nameof(Before)),new HarmonyMethod(typeof(SafeObjectRelease),nameof(After)),null,new HarmonyMethod(typeof(SafeObjectRelease),nameof(Finally)));
  }
  private static void Before(ModuleGrappleNode __instance,out Scope __state) {
   __state=null;
   try { var node=Node?.GetValue(__instance) as AttachNode;
    // Stock reverses node endpoints when the captured object becomes the root.
    var rock=Resolve(__instance,node,Other?.GetValue(__instance) as Part);
    __state=Begin(__instance,rock);
    Log.Info("Claw release witness: claw="+__instance.part?.persistentId+" owner="+node?.owner?.persistentId+" attached="+node?.attachedPart?.persistentId+" other="+(Other?.GetValue(__instance) as Part)?.persistentId+" object="+rock?.persistentId+" vessel="+__instance.vessel?.id+" captured="+(__state!=null)+" reason="+decline);
   }catch(Exception e){Log.Info("Release observation unavailable: "+e.GetType().Name);}
  }
  private static string decline;
  private static Scope Decline(string reason){decline=reason;return null;}
  internal static Part Resolve(ModuleGrappleNode claw,AttachNode node,Part other) {
   if(claw?.part==null)return null;
   var candidates=new[]{node?.owner,node?.attachedPart,other}.Where(p=>p!=null&&p!=claw.part).Distinct().ToArray();
   // Exact opposite endpoint only; never search the vessel for a convenient rock.
   return candidates.Length==1?candidates[0]:null;
  }
  private static void After(Scope __state){if(__state!=null)Complete(__state,false);}
  private static Exception Finally(Exception __exception,Scope __state){if(__state!=null&&!__state.Finished)Complete(__state,true);return __exception;}
  private static string Proof(TrackingState s)=>string.Join(";",s.Save().Where(e=>e.Key!="vesselId"&&e.Key!="manifest"&&e.Key!="reason").OrderBy(e=>e.Key).Select(e=>e.Key+"="+e.Value));
  internal static Scope Begin(ModuleGrappleNode claw,Part rock) {
   decline="none";var v=claw?.vessel;
   if(active!=null||claw==null||claw.GetType()!=typeof(ModuleGrappleNode)||v==null||!v.loaded||rock==null||rock.vessel!=v||!v.parts.Contains(rock)||!v.parts.Contains(claw.part)||claw.part==rock)return Decline("invalid claw/object attachment or nested observation");
   if(claw.part.partInfo?.name!="GrapplingDevice"&&claw.part.partInfo?.name!="GrapplingDeviceJr")return Decline("unsupported claw part");
   var ast=rock.Modules.Cast<PartModule>().Where(m=>m.GetType()==typeof(ModuleAsteroid)).Cast<ModuleAsteroid>().ToArray();
   var com=rock.Modules.Cast<PartModule>().Where(m=>m.GetType()==typeof(ModuleComet)).Cast<ModuleComet>().ToArray();
   bool comet=com.Length==1&&ast.Length==0;
   if(!(comet||ast.Length==1&&com.Length==0)||rock.partInfo?.name!=(comet?"PotatoComet":"PotatoRoid")||rock.Resources.Get("Ore")!=null||rock.Modules.OfType<OreMarketTank>().Any())return Decline("unsupported object modules or object contains Ore storage");
   if(!ObjectMining.TryIdentity(comet,comet?com[0].prefabBaseURL:ast[0].prefabBaseURL,comet?com[0].currentState:ast[0].currentState,out _))return Decline("unproven object identity");
   var running=v.parts.SelectMany(p=>p.Modules.Cast<PartModule>()).FirstOrDefault(m=>(VesselRules.StockDrill(m)||ObjectMining.IsObjectDrill(m))&&((BaseConverter)m).IsActivated);
   if(running!=null)return Decline("harvesting still active: module="+running.GetType().Name+" part="+running.part?.persistentId);
   var tanks=v.parts.SelectMany(p=>p.Modules.Cast<PartModule>()).OfType<OreMarketTank>().ToArray();
   foreach(var t in tanks)t.Observe();
   if(tanks.Length==0)return Decline("no tracked tanks");
   foreach(var t in tanks){
    if(t.State==null||t.State.Invalid||StockOreTransfers.Busy(t))return Decline("tank="+t.part.persistentId+" invalid/uninitialized/busy; "+t.State?.Reason);
    if(!VesselRules.Loaded(v,t,out string reason))return Decline("tank="+t.part.persistentId+" custody="+reason);
   }
   // Cargo may already have been transferred/staged into a tanker. Existing
   // verified cargo is bound to this exact assembly by Loaded above; releasing
   // a proven ordinary object cannot mint any new source quantity.
   if(!tanks.Any(t=>t.State.Eligible>0))return Decline("no verified cargo");
   var s=new Scope{Rock=rock,Parts=v.parts.ToArray(),Ids=v.parts.Select(p=>p.persistentId).ToArray(),Cargo=tanks.Select(t=>new Cargo{Tank=t,State=t.State,Resource=t.part.Resources.Get("Ore"),Amount=t.Amount,Capacity=t.Capacity,Saved=Proof(t.State)}).ToArray()};
   active=s;return s;
  }
  internal static void Complete(Scope s,bool failed) {
   if(s.Finished)return;s.Finished=true;
   try {
    var ship=s.Cargo[0].Tank.vessel;var remaining=s.Parts.Where(p=>p!=s.Rock).ToArray();
    bool unchanged=!failed&&ship!=null&&s.Parts.All(p=>p.vessel==ship&&ship.parts.Contains(p))&&ship.parts.Count==s.Parts.Length&&s.Parts.Select((p,i)=>p.persistentId==s.Ids[i]).All(x=>x)&&s.Cargo.All(c=>ReferenceEquals(c.State,c.Tank.State)&&ReferenceEquals(c.Resource,c.Tank.part.Resources.Get("Ore"))&&c.State.TankId==c.Tank.part.persistentId&&!c.State.Invalid&&c.Amount==c.Tank.Amount&&c.Capacity==c.Tank.Capacity&&Proof(c.State)==c.Saved);
    if(unchanged){Log.Info("Object release made no structural change; cargo retained");return;}
    bool safe=!failed&&ship!=null&&ship.loaded&&ship.id!=Guid.Empty&&ship!=s.Rock.vessel&&s.Rock.vessel!=null&&s.Rock.vessel.id!=Guid.Empty&&s.Rock.vessel.id!=ship.id&&s.Rock.vessel.parts.Count==1&&s.Rock.vessel.parts[0]==s.Rock&&ship.parts.Count==remaining.Length&&remaining.All(p=>p.vessel==ship&&ship.parts.Contains(p))&&s.Parts.Select((p,i)=>p.persistentId==s.Ids[i]).All(x=>x);
    safe &= s.Cargo.All(c=>c.Tank.vessel==ship&&ReferenceEquals(c.State,c.Tank.State)&&ReferenceEquals(c.Resource,c.Tank.part.Resources.Get("Ore"))&&c.State.TankId==c.Tank.part.persistentId&&!c.State.Invalid&&c.Amount==c.Tank.Amount&&c.Capacity==c.Tank.Capacity&&Proof(c.State)==c.Saved);
    active=null;
    if(safe) {string manifest=VesselRules.Manifest(ship.parts);foreach(var c in s.Cargo)c.State.RebindAfterRelease(ship.id.ToString("N"),manifest);foreach(var c in s.Cargo)if(!VesselRules.Loaded(ship,c.Tank,out _))safe=false;}
    if(!safe){
     Log.Info("Release conservation rejected: exception="+failed+" ship="+ship?.id+" shipParts="+ship?.parts.Count+" expectedParts="+remaining.Length+" objectVessel="+s.Rock.vessel?.id+" objectParts="+s.Rock.vessel?.parts.Count);
     foreach(var c in s.Cargo){Log.Info("Release tank proof: part="+c.Tank.part.persistentId+" savedTank="+c.State.TankId+" actual="+c.Tank.Amount+" prior="+c.Amount+" capacity="+c.Tank.Capacity+" priorCapacity="+c.Capacity+" stateSame="+ReferenceEquals(c.State,c.Tank.State)+" resourceSame="+ReferenceEquals(c.Resource,c.Tank.part.Resources.Get("Ore"))+" proofSame="+(Proof(c.State)==c.Saved));c.Tank.Invalidate("Unverified vessel split during object release");}
    }
    Log.Info("Object release "+(safe?"verified":"rejected")+": object="+s.Rock.persistentId+" tanks="+s.Cargo.Length+" vessel="+ship?.id);
   }catch(Exception e){foreach(var c in s.Cargo)c.Tank.Invalidate("Object release observer exception");Log.Error("Object release failed closed: "+e);}finally{active=null;foreach(var c in s.Cargo)c.Tank.RefreshIfReady();}
  }
 }
}
