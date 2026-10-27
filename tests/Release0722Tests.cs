using System;
using System.Linq;
using OreMarket;
internal static class Release0722Tests {
 internal static void Run(Action<string,bool> check){
 PlanetaryTests.Seed();OrePricing.Load(new[]{PricingTests.Shipped()});var observer=new MiningObserver();observer.Awake();var sales=new RecoverySales();sales.OnAwake();
 try{foreach(string kind in new[]{"F","H","comet"}){
 var r=new MultiTank072Tests.Rig();var rock=kind=="comet"?((ModuleCometDrill)r.Comet).TestSource:((ModuleAsteroidDrill)r.Asteroid).TestSource;
 if(kind!="comet")rock.Modules.OfType<ModuleAsteroid>().Single().prefabBaseURL="Procedural/PA_"+kind;
 var clawPart=r.AddPart(777,"GrapplingDevice");var claw=new ModuleGrappleNode{part=clawPart,otherPart=rock};claw.grappleNode.attachedPart=rock;clawPart.Modules.Add(claw);
 var drill=kind=="comet"?r.Comet:r.Asteroid;r.Output(drill,100,1,1,0);r.Output(drill,101,10,20,0);r.Output(r.Drill,102,0,0,1);r.Output(r.Drill,103,0,0,5);
 r.Reload();foreach(var d in r.Vessel.parts.SelectMany(p=>p.Modules).OfType<BaseConverter>())d.IsActivated=false;
 double qty=r.Verified;string[] batches=r.Tanks.Select(t=>t.State.BatchId).ToArray();var old=r.Vessel.id;
 claw.ReleaseOperation=()=>{GameEvents.onPartDeCouple.Fire(rock);var released=new Vessel();r.Vessel.parts.Remove(rock);released.parts.Add(rock);rock.vessel=released;r.Vessel.id=Guid.NewGuid();GameEvents.onVesselWasModified.Fire(r.Vessel);};claw.Release();
 check("0722: release retains multiple tanks and commodities "+kind,Math.Abs(r.Verified-qty)<1e-7&&r.Tanks.All(t=>!t.State.Invalid)&&r.Tanks.Select((t,i)=>t.State.BatchId==batches[i]).All(x=>x));
 check("0722: released object cannot carry copied Ore "+kind,rock.vessel.parts.Count==1&&rock.Resources.Get("Ore")==null&&rock.Modules.OfType<OreMarketTank>().Count()==0);
 check("0722: changed ship ID rebound with same batches "+kind,r.Vessel.id!=old&&r.Tanks.All(t=>t.State.VesselId==r.Vessel.id.ToString("N")));
 r.Reload();GameEvents.onVesselGoOnRails.Fire(r.Vessel);GameEvents.onVesselGoOffRails.Fire(r.Vessel);
 check("0722: release custody persists through load and warp "+kind,r.Tanks.All(t=>!t.State.Invalid)&&Math.Abs(r.Verified-qty)<1e-7);
 RecoveryTests.Reset(sales);var market=new MarketScenario();market.OnAwake();market.OnLoad(new ConfigNode());MarketScenario.ActiveBook.Advance(600);double expected=r.Tanks.Sum(t=>RecoveryPolicy.Quote(t.State,r.Vessel.id.ToString("N"),VesselRules.Manifest(r.Vessel.parts),t.Amount,t.Capacity,true,true));var snapshot=r.Snapshot();var stale=r.Snapshot();GameEvents.onVesselRecoveryProcessingComplete.Fire(snapshot,null,1);var claims=new ConfigNode();sales.OnSave(claims);sales.OnLoad(claims);GameEvents.onVesselRecoveryProcessingComplete.Fire(stale,null,1);
 check("0722: release recovery pays once per origin "+kind,expected>0&&Funding.Instance.Calls==1&&Math.Abs(Funding.Instance.LastAmount-expected)<1e-7);market.OnDestroy();
 }
 foreach(string mode in new[]{"noop","extra part","quantity","exception"}) {
 var r=new MultiTank072Tests.Rig();var rock=((ModuleAsteroidDrill)r.Asteroid).TestSource;var cp=r.AddPart(777,"GrapplingDevice");var claw=new ModuleGrappleNode{part=cp,otherPart=rock};claw.grappleNode.attachedPart=rock;cp.Modules.Add(claw);r.Output(r.Asteroid,100,1,0,0);r.Output(r.Asteroid,101,10,0,0);foreach(var d in r.Vessel.parts.SelectMany(p=>p.Modules).OfType<BaseConverter>())d.IsActivated=false;
 claw.ReleaseOperation=()=>{if(mode=="noop")return;GameEvents.onPartDeCouple.Fire(rock);var detached=new Vessel();r.Vessel.parts.Remove(rock);detached.parts.Add(rock);rock.vessel=detached;if(mode=="extra part"){var tank=r.Tanks[1].part;r.Vessel.parts.Remove(tank);detached.parts.Add(tank);tank.vessel=detached;}if(mode=="quantity")r.Tanks[0].part.Resources.Get("Ore").amount+=1;if(mode=="exception")throw new InvalidOperationException("release test");};
 try{claw.Release();}catch(InvalidOperationException){}
 check("0722: bounded release validation "+mode,mode=="noop"?!r.Tanks[0].State.Invalid&&r.Tanks[0].State.Eligible==10:r.Tanks[0].State.Invalid&&r.Tanks[0].State.Eligible==0);
 }
 var unsafeRig=new MultiTank072Tests.Rig();unsafeRig.Output(unsafeRig.Asteroid,100,1,0,0);unsafeRig.Output(unsafeRig.Asteroid,101,10,0,0);GameEvents.onPartDeCouple.Fire(unsafeRig.Tanks[0].part);
 check("0722: unrelated staging still invalidates",unsafeRig.Tanks[0].State.Invalid);
 }finally{observer.OnDestroy();sales.OnDestroy();}
 }
}
