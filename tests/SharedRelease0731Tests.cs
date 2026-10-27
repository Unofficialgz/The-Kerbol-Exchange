using System;
using System.Linq;
using OreMarket;
internal static class SharedRelease0731Tests {
 internal static void Run(Action<string,bool> check) {
  PlanetaryTests.Seed();OrePricing.Load(new[]{PricingTests.Shipped()});var observer=new MiningObserver();observer.Awake();var sales=new RecoverySales();sales.OnAwake();
  try {
   foreach(var definition in ObjectMarkets.PhysicalDefinitions.Where(d=>!d.Special))foreach(bool reverse in new[]{false,true}) {
    var r=new MultiTank072Tests.Rig();bool comet=definition.Kind==SpaceObjectKind.Comet;var drill=comet?r.Comet:r.Asteroid;
    var rock=comet?((ModuleCometDrill)drill).TestSource:((ModuleAsteroidDrill)drill).TestSource;
    if(comet)rock.Modules.OfType<ModuleComet>().Single().prefabBaseURL="Procedural/PC_"+definition.ClassLetter;
    else {rock.Modules.OfType<ModuleAsteroid>().Single().prefabBaseURL="Procedural/PA_"+definition.ClassLetter;rock.Modules.OfType<ModuleAsteroid>().Single().currentState=reverse?2:1;}
    var part=r.AddPart(777,"GrapplingDevice");var claw=new ModuleGrappleNode{part=part,otherPart=reverse?null:rock};part.Modules.Add(claw);
    claw.grappleNode.owner=reverse?rock:part;claw.grappleNode.attachedPart=reverse?part:rock;
    r.Output(drill,100,1,1,0);r.Output(drill,101,10,20,0);r.Output(r.Drill,102,0,0,1);r.Output(r.Drill,103,0,0,5);
    foreach(var d in r.Vessel.parts.SelectMany(p=>p.Modules).OfType<BaseConverter>())d.IsActivated=false;
    if(reverse)check("0731: previous endpoint expression selects claw and declines valid root arrangement "+definition.Identity,
     ReferenceEquals(claw.grappleNode.attachedPart??claw.otherPart,claw.part)&&SafeObjectRelease.Begin(claw,claw.part)==null&&ReferenceEquals(SafeObjectRelease.Resolve(claw,claw.grappleNode,claw.otherPart),rock));
    var states=r.Tanks.Select(t=>ExtendedClassesTests.Dump(State(t.State))).ToArray();var batches=r.Tanks.Select(t=>t.State.BatchId).ToArray();double qty=r.Verified;
    var boundary=reverse?part:rock;
    claw.ReleaseOperation=()=>{if(reverse)boundary.Undock(new DockedVesselInfo());else boundary.decouple(0);};
    boundary.TopologyOperation=()=>{GameEvents.onPartDeCouple.Fire(rock);var separated=new Vessel();r.Vessel.parts.Remove(rock);separated.parts.Add(rock);rock.vessel=separated;
     if(reverse){var replacement=new Vessel();foreach(var p in r.Vessel.parts){replacement.parts.Add(p);p.vessel=replacement;}r.Vessel.parts.Clear();r.Vessel=replacement;}else r.Vessel.id=Guid.NewGuid();
     GameEvents.onVesselWasModified.Fire(r.Vessel);GameEvents.onVesselWasModified.Fire(separated);};
    try{if(reverse)claw.Release();else claw.Decouple();}finally{boundary.TopologyOperation=null;}
    string label=definition.Kind+definition.ClassLetter+" reverse="+reverse;
    check("0731: exact stock endpoint release retains quantities/origins "+label,Math.Abs(r.Verified-qty)<1e-7&&r.Tanks.All(t=>!t.State.Invalid)&&r.Tanks.Select((t,i)=>ExtendedClassesTests.Dump(State(t.State))==states[i]).All(x=>x));
    check("0731: release preserves receipts, object separates alone "+label,r.Tanks.Select((t,i)=>t.State.BatchId==batches[i]).All(x=>x)&&rock.vessel.parts.Count==1&&r.Tanks.All(t=>t.vessel==r.Vessel));
    r.Reload();r.Vessel.packed=true;GameEvents.onVesselGoOnRails.Fire(r.Vessel);GameEvents.onVesselGoOffRails.Fire(r.Vessel);r.Vessel.packed=false;
    check("0731: released provenance survives reload/warp "+label,Math.Abs(r.Verified-qty)<1e-7&&r.Tanks.All(t=>!t.State.Invalid));
    RecoveryTests.Reset(sales);var scenario=new MarketScenario();scenario.OnAwake();scenario.OnLoad(new ConfigNode());
    try{MarketScenario.ActiveBook.Advance(600);MarketQuotes.TryGetUnitPrice(ObjectMarkets.Shared,out double p);OrePricing.TryGetUnitPrice("Mun",out double mun);
     var snap=r.Snapshot();var stale=r.Snapshot();GameEvents.onVesselRecoveryProcessingComplete.Fire(snap,null,1);var claims=new ConfigNode();sales.OnSave(claims);sales.OnLoad(claims);GameEvents.onVesselRecoveryProcessingComplete.Fire(stale,null,1);
     check("0731: release pays shared live quote plus planetary once "+label,Funding.Instance.Calls==1&&Math.Abs(Funding.Instance.LastAmount-(30*p+5*mun))<1e-7);
    }finally{scenario.OnDestroy();}
   }
   var mixed=new MultiTank072Tests.Rig();mixed.Output(mixed.Asteroid,100,1,0,0);mixed.Output(mixed.Asteroid,101,10,0,0);mixed.Output(mixed.Comet,100,0,1,0);mixed.Output(mixed.Comet,101,0,20,0);
   double original=mixed.Verified;mixed.Transfer(0,1,5);mixed.Reload();
   check("0731: same shared commodity transfers retain typed source lots",!mixed.Tanks[1].State.Invalid&&!mixed.Tanks[1].State.LegacyMixed&&mixed.Tanks[1].State.Lots.Count()==2&&Math.Abs(mixed.Verified-original)<1e-7&&mixed.Tanks[1].State.Lots.Select(l=>l.Origin).Distinct().Count()==2);
   var legacy=mixed.Tanks[1].State.Save();legacy["schema"]="4";
   var legacyState=TrackingState.Load(legacy);
   check("0731: prior mixed-source schema-4 cargo remains storage-locked without loss",legacyState.LegacyMixed&&!legacyState.Invalid&&legacyState.Eligible==mixed.Tanks[1].State.Eligible&&!legacyState.AcceptsOrigin(ObjectMarkets.Prefix+"Asteroid:H"));
   var random=new Random(731);double conserved=mixed.Verified;
   for(int i=0;i<1000;i++){int from=random.Next(2),to=1-from;double amount=mixed.Tanks[from].Amount*random.NextDouble();mixed.Transfer(from,to,amount);if(i%31==0)mixed.Reload();if(mixed.Tanks.Any(t=>t.State.Invalid||t.State.Eligible>t.Amount)||Math.Abs(mixed.Verified-conserved)>1e-7)throw new Exception("Shared transfer conservation failure "+i);}
   check("0731: 1000 shared-source randomized transfers/reloads conserve provenance",Math.Abs(mixed.Verified-conserved)<1e-7);
   mixed.Output(mixed.Asteroid,102,0,1,0);mixed.Output(mixed.Asteroid,103,0,3,0);
   check("0731: shared tank accepts supported extraction from another class/type",!mixed.Tanks[1].State.Invalid&&Math.Abs(mixed.Verified-original-3)<1e-7);
   mixed.Vessel.mainBody=FlightGlobals.Bodies.Single(body=>body.bodyName=="Duna");mixed.Output(mixed.Drill,104,0,0,1);mixed.Output(mixed.Drill,105,0,0,8);double a=mixed.Tanks[1].Amount,b=mixed.Tanks[2].Amount;
   mixed.Transfer(1,2,1);check("0731: shared object Ore cannot enter occupied planetary tank",mixed.Tanks[1].Amount==a&&mixed.Tanks[2].Amount==b&&mixed.Tanks.All(t=>!t.State.Invalid));
   var many=new MultiTank072Tests.Rig();many.Output(many.Asteroid,100,1,0,0);many.Output(many.Asteroid,101,10,0,0);many.Output(many.Comet,100,0,1,0);many.Output(many.Comet,101,0,20,0);
   var session=StockOreTransfers.Begin(many.Tanks[2].part,new[]{many.Tanks[0].part,many.Tanks[1].part});
   check("0731: mixed physical sources share one authorized stock transfer",session.Safe);
   many.Tanks[0].part.Resources.Get("Ore").amount-=2;many.Tanks[1].part.Resources.Get("Ore").amount-=3;many.Tanks[2].part.Resources.Get("Ore").amount+=5;StockOreTransfers.Complete(session,false);
   string snapshot=ExtendedClassesTests.Dump(State(many.Tanks[2].State));many.Reload();
   check("0731: multi-donor assignment and typed lots restore exactly",!many.Tanks[2].State.Invalid&&many.Tanks[2].State.Lots.Count()==2&&ExtendedClassesTests.Dump(State(many.Tanks[2].State))==snapshot&&Math.Abs(many.Verified-30)<1e-7);
   var cp=mixed.AddPart(777,"GrapplingDevice");var c=new ModuleGrappleNode{part=cp};var obj=((ModuleAsteroidDrill)mixed.Asteroid).TestSource;
   check("0731: conflicting endpoint evidence rejected",SafeObjectRelease.Resolve(c,new AttachNode{owner=obj,attachedPart=((ModuleCometDrill)mixed.Comet).TestSource},obj)==null);
   check("0731: active harvesting is not covered by release witness",SafeObjectRelease.Begin(c,obj)==null);
   foreach(var d in mixed.Vessel.parts.SelectMany(p=>p.Modules).OfType<BaseConverter>())d.IsActivated=false;
   // No recovery of a previously invalidated nonempty tank through shared-price migration.
   mixed.Tanks[0].Invalidate("test invalidation");mixed.Reload();
   check("0731: shared pricing never rehabilitates invalidated cargo",mixed.Tanks[0].State.Invalid&&mixed.Tanks[0].State.Eligible==0);
  }finally{observer.OnDestroy();sales.OnDestroy();}
 }
 private static ConfigNode State(TrackingState s){var n=new ConfigNode();foreach(var e in s.Save())if(e.Key!="vesselId"&&e.Key!="manifest"&&e.Key!="reason")n.AddValue(e.Key,e.Value);return n;}
}
