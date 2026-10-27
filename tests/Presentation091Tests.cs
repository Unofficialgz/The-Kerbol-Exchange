using System;
using System.Linq;
using OreMarket;
using OreMarket.Terminal;
internal static class Presentation091Tests {
 internal static void Run(Action<string,bool> check) {
  check("091: stock system texture requires exact moon order",TerminalAssets.BodyPath("Kerbin",new[]{"Mun","Minmus"})=="KerbolExchange/Textures/Systems/Kerbin"&&TerminalAssets.BodyPath("Kerbin",new[]{"Minmus","Mun"})==null);
  check("091: modded moon layouts and bodies use actual generated fallback",TerminalAssets.BodyPath("Kerbin",new[]{"Mun","Minmus","PackMoon"})==null&&TerminalAssets.BodyPath("PackWorld",null)==null);
  check("091: shared market loads a single combined icon",ObjectMarkets.PhysicalDefinitions.All(d=>TerminalAssets.BodyPath(d.Identity,null)=="KerbolExchange/Textures/Comet and Asteroid Icon"));
  var db=new GameDatabase();GameDatabase.Instance=db;var art=new UnityEngine.Texture2D();db.Textures[TerminalAssets.Root+"Test"]=art;
  check("091: borrowed texture cached without repeated database access",ReferenceEquals(TerminalAssets.Load(TerminalAssets.Root+"Test"),art)&&ReferenceEquals(TerminalAssets.Load(TerminalAssets.Root+"Test"),art)&&db.Calls==1);
  var miss=TerminalAssets.Load(TerminalAssets.Root+"MissingTest");TerminalAssets.Load(TerminalAssets.Root+"MissingTest");check("091: missing texture cached safely",miss==null&&db.Calls==2);
  db.Fail=true;check("091: database errors preserve fallback",TerminalAssets.Load(TerminalAssets.Root+"ErrorTest")==null);GameDatabase.Instance=null;
  check("091: active and aftermath timers clearly distinct",TerminalHelp.EventTime(125,false)=="02:05 active remaining"&&TerminalHelp.EventTime(125,true)=="02:05 aftermath remaining"&&TerminalHelp.EventTime(20,false)=="00:20 active remaining");
  check("091: invalid event time handled",TerminalHelp.EventTime(double.NaN,true)=="Time unavailable");
  PlanetaryTests.Seed();OrePricing.Load(new[]{PricingTests.Shipped()});var observer=new MiningObserver();observer.Awake();
  try{
   var empty=new MultiTank072Tests.Rig(1);check("091: empty tank shows Waiting for Ore",empty.Tanks[0].oreOrigin=="Waiting for Ore"&&!empty.Tanks[0].Fields["trackingStatus"].guiActive);
   empty.Output(empty.Drill,100,1);empty.Output(empty.Drill,101,10);var t=empty.Tanks[0];var saved=t.State.Save();t.Refresh();
   check("091: normal cargo has two information rows",typeof(OreMarketTank).GetField("saleable").GetCustomAttributes(typeof(KSPField),false).Cast<KSPField>().Single().guiName=="Marketable Ore"&&!typeof(OreMarketTank).GetField("verifiedOre").GetCustomAttributes(typeof(KSPField),false).Cast<KSPField>().Single().guiActive&&!t.Fields["trackingStatus"].guiActive);
   check("091: display refresh never modifies provenance",saved.All(e=>t.State.Save()[e.Key]==e.Value));
   check("091: partially verified cargo visibly distinguished",t.saleable.Contains("partly verified")&&t.oreOrigin=="Mun Ore"&&t.State.Eligible==10);
   var preload=new MultiTank072Tests.Rig(1,20);check("091: preloaded cargo stays visibly unverified",preload.Tanks[0].oreOrigin=="Unverified Ore"&&preload.Tanks[0].saleable=="0 (unverified)");
   t.Invalidate("test blocked operation");check("091: genuine failures remain visible",t.Fields["trackingStatus"].guiActive&&t.trackingStatus=="test blocked operation"&&t.State.Eligible==0);
   var mixed=new MultiTank072Tests.Rig(1);mixed.Output(mixed.Drill,100,1);mixed.Output(mixed.Drill,101,10);var data=mixed.Tanks[0].State.Save();data["schema"]="4";data["lotCount"]="2";data["lot0_quantity"]="5";data["lot1_origin"]="Duna";data["lot1_source"]="0";data["lot1_quantity"]="5";
   var node=new ConfigNode();var tracking=node.AddNode(OreMarketTank.NodeName);foreach(var e in data)tracking.AddValue(e.Key,e.Value);mixed.Tanks[0].OnLoad(node);mixed.Tanks[0].OnStart(PartModule.StartState.Flying);
   check("091: legacy mixed warning visible without cargo loss",mixed.Tanks[0].State.LegacyMixed&&mixed.Tanks[0].Fields["trackingStatus"].guiActive&&mixed.Tanks[0].State.Eligible==10);
  }finally{observer.OnDestroy();}
 }
}
