using System;
using System.Linq;
using OreMarket;
internal static class Logistics073Tests {
 static bool Near(double a,double b)=>Math.Abs(a-b)<1e-7;
 static void Dock(MultiTank072Tests.Rig a,MultiTank072Tests.Rig b){
 var other=b.Vessel;var tool=a.Vessel.parts.Last();tool.TopologyOperation=()=>{
 GameEvents.onPartCouple.Fire(new GameEvents.FromToAction<Part,Part>{from=tool,to=other.parts[0]});
 foreach(var p in other.parts.ToArray()){other.parts.Remove(p);a.Vessel.parts.Add(p);p.vessel=a.Vessel;}GameEvents.onVesselWasModified.Fire(a.Vessel);
 };tool.Couple(other.parts[0]);
 }
 static void Split(MultiTank072Tests.Rig a,MultiTank072Tests.Rig b,Part[] parts){
 var tool=parts[0];var separated=new Vessel();tool.TopologyOperation=()=>{
 GameEvents.onPartDeCouple.Fire(tool);foreach(var p in parts){a.Vessel.parts.Remove(p);separated.parts.Add(p);p.vessel=separated;}GameEvents.onVesselWasModified.Fire(a.Vessel);GameEvents.onVesselWasModified.Fire(separated);
 };tool.Undock(new DockedVesselInfo());b.Vessel=separated;
 }
 internal static void Run(Action<string,bool> check){
 PlanetaryTests.Seed();OrePricing.Load(new[]{PricingTests.Shipped()});var observer=new MiningObserver();observer.Awake();var sales=new RecoverySales();sales.OnAwake();
 try{
 var a=new MultiTank072Tests.Rig();var b=new MultiTank072Tests.Rig(idOffset:1000);a.Vessel.mainBody.bodyName="Duna";b.Vessel.mainBody.bodyName="Eve";
 a.Output(a.Drill,100,1,0,0);a.Output(a.Drill,101,50,0,0);b.Output(b.Drill,100,1,0,0);b.Output(b.Drill,101,30,0,0);
 var bparts=b.Vessel.parts.ToArray();Dock(a,b);
 check("073: stock merge preserves different origins and stable tank identities",a.Tanks.Concat(b.Tanks).All(t=>!t.State.Invalid&&t.State.TankId==t.part.persistentId)&&a.Tanks[0].State.Origin=="Duna"&&b.Tanks[0].State.Origin=="Eve");
 var ui=new UIPartActionResourceTransfer{State=2,resource=b.Tanks[1].part.Resources.Get("Ore")};ui.Targets.Add(new UIPartActionResourceTransfer{State=1,resource=a.Tanks[0].part.Resources.Get("Ore")});
 ui.UpdateOperation=()=>{a.Tanks[0].part.Resources.Get("Ore").amount-=20.4;b.Tanks[1].part.Resources.Get("Ore").amount+=20.4;};ui.UpdateItem();
 check("073: docked transfer carries only proportional verified source",Near(a.Tanks[0].State.Eligible,30)&&Near(b.Tanks[1].State.Eligible,20)&&b.Tanks[1].State.Origin=="Duna");
 Split(a,b,bparts);a.Reload();b.Reload();
 check("073: undock and reload retain only physically aboard cargo",Near(a.Verified,30)&&Near(b.Verified,50)&&a.Tanks.Concat(b.Tanks).All(t=>!t.State.Invalid));
 for(int i=0;i<50;i++){Dock(a,b);Split(a,b,bparts);a.Reload();b.Reload();}
 check("073: 50 repeated dock/split/save cycles conserve cargo",Near(a.Verified+b.Verified,80)&&a.Tanks.Concat(b.Tanks).All(t=>!t.State.Invalid));
 RecoveryTests.Reset(sales);var market=new MarketScenario();market.OnAwake();market.OnLoad(new ConfigNode());MarketScenario.ActiveBook.Advance(600);
 double expected=a.Tanks.Concat(b.Tanks).Sum(t=>RecoveryPolicy.Quote(t.State,t.vessel.id.ToString("N"),VesselRules.Manifest(t.vessel.parts),t.Amount,t.Capacity,true,true));
 var sa=a.Snapshot();var sb=b.Snapshot();var staleA=a.Snapshot();var staleB=b.Snapshot();GameEvents.onVesselRecoveryProcessingComplete.Fire(sa,null,1);GameEvents.onVesselRecoveryProcessingComplete.Fire(sb,null,1);
 check("073: separated vessels each sell their own live-price cargo",Funding.Instance.Calls==2&&Near(Funding.Instance.Funds,1000+expected));var claims=new ConfigNode();sales.OnSave(claims);sales.OnLoad(claims);GameEvents.onVesselRecoveryProcessingComplete.Fire(staleA,null,1);GameEvents.onVesselRecoveryProcessingComplete.Fire(staleB,null,1);
 check("073: persisted claims block replay across both resulting vessels",Funding.Instance.Calls==2);market.OnDestroy();
 var staged=new MultiTank072Tests.Rig();staged.Output(staged.Drill,100,1,0,0);staged.Output(staged.Drill,101,25,0,0);var tankPart=staged.Tanks[0].part;var tankVessel=new Vessel();
 tankPart.TopologyOperation=()=>{GameEvents.onPartDeCouple.Fire(tankPart);staged.Vessel.parts.Remove(tankPart);tankVessel.parts.Add(tankPart);tankPart.vessel=tankVessel;GameEvents.onVesselWasModified.Fire(staged.Vessel);GameEvents.onVesselWasModified.Fire(tankVessel);};tankPart.decouple(0);
 check("073: loaded staged tank follows physical part",staged.Tanks[0].State.Eligible==25&&!staged.Tanks[0].State.Invalid&&staged.Tanks[0].State.VesselId==tankVessel.id.ToString("N"));
 var booster=staged.Vessel.parts.Last();var discarded=new Vessel();booster.TopologyOperation=()=>{GameEvents.onPartDeCouple.Fire(booster);staged.Vessel.parts.Remove(booster);discarded.parts.Add(booster);booster.vessel=discarded;};booster.decouple(0);
 check("073: discarding empty parts does not affect separated cargo",staged.Tanks[0].State.Eligible==25&&!staged.Tanks[0].State.Invalid);
 var legacy=new MultiTank072Tests.Rig();var peer=new MultiTank072Tests.Rig(idOffset:1000);var data=legacy.Tanks[0].State.Save();data["schema"]="4";data["lastAmount"]="12";data["eligible"]="10";data["origin"]="Mun";data["batchId"]=Guid.NewGuid().ToString("N");data["vesselId"]=legacy.Vessel.id.ToString("N");data["manifest"]=VesselRules.Manifest(legacy.Vessel.parts);data["lotCount"]="2";
 for(int i=0;i<2;i++){data["lot"+i+"_origin"]=i==0?"Mun":"Duna";data["lot"+i+"_source"]="0";data["lot"+i+"_quantity"]="5";}
 var node=new ConfigNode();var tracking=node.AddNode(OreMarketTank.NodeName);foreach(var e in data)tracking.AddValue(e.Key,e.Value);legacy.Tanks[0].part.Resources.Get("Ore").amount=12;legacy.Tanks[0].OnLoad(node);legacy.Tanks[0].OnStart(PartModule.StartState.Flying);
 var peerParts=peer.Vessel.parts.ToArray();Dock(legacy,peer);Split(legacy,peer,peerParts);legacy.Reload();
 check("073: legacy mixed cargo survives topology without new mixing permission",legacy.Tanks[0].State.LegacyMixed&&legacy.Tanks[0].State.Eligible==10&&!legacy.Tanks[0].State.Invalid);
 var multi=new MultiTank072Tests.Rig();multi.Output(multi.Asteroid,100,1,0,0);multi.Output(multi.Asteroid,101,7,0,0);multi.Output(multi.Drill,100,0,1,0);multi.Output(multi.Drill,101,0,11,0);multi.Output(multi.Comet,100,0,0,1);multi.Output(multi.Comet,101,0,0,13);
 check("073: independent drills credit only own output into compatible tanks",Near(multi.Verified,31)&&multi.Tanks.All(t=>!t.State.Invalid)&&multi.Tanks.Select(t=>t.State.Origin).Distinct().Count()==3);
 var unverified=new MultiTank072Tests.Rig(preload:50);var receiver=new MultiTank072Tests.Rig(idOffset:1000);Dock(unverified,receiver);check("073: docking does not certify preloaded Ore",unverified.Verified==0&&receiver.Verified==0);
 var ra=new MultiTank072Tests.Rig();var rb=new MultiTank072Tests.Rig(idOffset:1000);rb.Vessel.mainBody.bodyName="Duna";ra.Output(ra.Drill,100,1,1,1);ra.Output(ra.Drill,101,3,5,7);rb.Output(rb.Drill,100,1,1,1);rb.Output(rb.Drill,101,11,13,17);var rng=new Random(730);
 for(int cycle=0;cycle<100;cycle++){
 Dock(ra,rb);var all=ra.Vessel.parts.ToArray();var chosen=all.OrderBy(_=>rng.Next()).Take(rng.Next(1,all.Length)).ToArray();Split(ra,rb,chosen);ra.Reload();rb.Reload();
 if(!Near(ra.Verified+rb.Verified,56)||ra.Tanks.Concat(rb.Tanks).Any(t=>t.State.Invalid))throw new Exception("Random cargo partition failed at "+cycle);
 }
 check("073: 100 randomized stock partitions and reloads conserve every tank",true);
 var invalidCargo=new MultiTank072Tests.Rig();var validPeer=new MultiTank072Tests.Rig(idOffset:1000);invalidCargo.Output(invalidCargo.Drill,100,1,0,0);invalidCargo.Output(invalidCargo.Drill,101,10,0,0);invalidCargo.Tanks[0].State.Invalidate("Previously invalidated cargo");Dock(invalidCargo,validPeer);
 check("073: topology never rehabilitates nonempty invalid cargo",invalidCargo.Tanks[0].State.Invalid&&invalidCargo.Tanks[0].State.Eligible==0);
 var invalidPeerParts=invalidCargo.Vessel.parts.Where(p=>p.persistentId>=1000).ToArray();Split(invalidCargo,validPeer,invalidPeerParts);Dock(invalidCargo,validPeer);
 check("073: invalid zero-credit cargo does not require rehabilitation for repeated topology",invalidCargo.Tanks[0].State.Invalid&&invalidCargo.Tanks[0].State.Eligible==0&&validPeer.Tanks.All(t=>!t.State.Invalid));
 var releasedMiner=new MultiTank072Tests.Rig();var minedRock=((ModuleAsteroidDrill)releasedMiner.Asteroid).TestSource;var clawPart=releasedMiner.AddPart(888,"GrapplingDevice");var releaseClaw=new ModuleGrappleNode{part=clawPart,otherPart=minedRock};releaseClaw.grappleNode.attachedPart=minedRock;clawPart.Modules.Add(releaseClaw);
 releasedMiner.Output(releasedMiner.Asteroid,100,1,0,0);releasedMiner.Output(releasedMiner.Asteroid,101,10,0,0);releasedMiner.Output(releasedMiner.Drill,100,0,1,0);releasedMiner.Output(releasedMiner.Drill,101,0,5,0);releasedMiner.Transfer(0,2,releasedMiner.Tanks[0].Amount);
 var cargoPart=releasedMiner.Tanks[2].part;var cargoShip=new Vessel();cargoPart.TopologyOperation=()=>{GameEvents.onPartDeCouple.Fire(cargoPart);releasedMiner.Vessel.parts.Remove(cargoPart);cargoShip.parts.Add(cargoPart);cargoPart.vessel=cargoShip;};cargoPart.decouple(0);
 foreach(var d in releasedMiner.Vessel.parts.SelectMany(p=>p.Modules).OfType<BaseConverter>())d.IsActivated=false;
 releaseClaw.ReleaseOperation=()=>{GameEvents.onPartDeCouple.Fire(minedRock);releasedMiner.Vessel.parts.Remove(minedRock);var free=new Vessel();free.parts.Add(minedRock);minedRock.vessel=free;};releaseClaw.Release();
 check("073: release after offloading object Ore retains other commodities and tanker cargo",releasedMiner.Tanks.All(t=>!t.State.Invalid)&&releasedMiner.Tanks[1].State.Eligible==5&&releasedMiner.Tanks[2].State.Eligible==10&&releasedMiner.Tanks[2].vessel==cargoShip);
 var migrated=new MultiTank072Tests.Rig();migrated.Output(migrated.Drill,100,1,0,0);migrated.Output(migrated.Drill,101,10,0,0);var oldState=migrated.Tanks[0].State.Save();oldState["schema"]="5";oldState.Remove("tankId");var legacyNode=new ConfigNode();var legacyTracking=legacyNode.AddNode(OreMarketTank.NodeName);foreach(var e in oldState)legacyTracking.AddValue(e.Key,e.Value);migrated.Tanks[0].OnLoad(legacyNode);migrated.Tanks[0].OnStart(PartModule.StartState.Flying);
 check("073: schema-5 valid cargo binds to its persistent tank without quantity changes",migrated.Tanks[0].State.TankId==migrated.Tanks[0].part.persistentId&&migrated.Tanks[0].State.Eligible==10&&!migrated.Tanks[0].State.Invalid);
 RecoveryTests.Reset(sales);var forged=migrated.Snapshot();forged.protoPartSnapshots[0].persistentId=999;string forgedManifest=VesselRules.Manifest(forged.protoPartSnapshots);foreach(var part in forged.protoPartSnapshots)foreach(var module in part.modules){var saved=module.moduleValues.GetNode(OreMarketTank.NodeName);if(saved!=null)saved.SetValue("manifest",forgedManifest,true);}GameEvents.onVesselRecoveryProcessingComplete.Fire(forged,null,1);
 check("073: matching vessel strings cannot bypass saved tank identity",Funding.Instance.Calls==0);
 RecoveryTests.Reset(sales);var reservesOnly=new MultiTank072Tests.Rig();GameEvents.onVesselRecoveryProcessingComplete.Fire(reservesOnly.Snapshot(),null,1);
 check("073: unmined asteroid/comet reserves earn no OreMarket bonus",Funding.Instance.Calls==0);
 foreach(string bad in new[]{"quantity","clone","resource","exception"}){
 var r=new MultiTank072Tests.Rig();r.Output(r.Drill,100,1,0,0);r.Output(r.Drill,101,10,0,0);var part=r.Tanks[0].part;
 part.TopologyOperation=()=>{GameEvents.onPartDeCouple.Fire(part);if(bad=="quantity")part.Resources.Get("Ore").amount+=1;if(bad=="clone")r.AddPart(999,"SmallTank");if(bad=="resource"){double amount=part.Resources.Get("Ore").amount;part.Resources.Clear();part.Resources.Add(new PartResource{part=part,amount=amount,maxAmount=1500});}if(bad=="exception")throw new InvalidOperationException();};
 try{part.decouple(0);}catch(InvalidOperationException){}
 check("073: unsafe topology fails closed "+bad,r.Tanks[0].State.Invalid&&r.Tanks[0].State.Eligible==0);
 }
 }finally{observer.OnDestroy();sales.OnDestroy();}
 }
}
