using System;
using System.Linq;
using OreMarket;
internal static class ExtendedClassesTests {
 internal static ConfigNode Save(MarketBook b){var n=new ConfigNode();b.Save(n);return n;}
 internal static string Dump(ConfigNode n)=>(n.name??"")+"{"+string.Join("|",n.values.Select(v=>v.name+"="+v.value))+string.Join("",n.nodes.Select(Dump))+"}";
 internal static void Run(Action<string,bool> check) {
  PlanetaryTests.Seed();OrePricing.Load(new[]{PricingTests.Shipped()});
  var settings=OrePricing.Settings;var old=new MarketBook(settings,729);
  foreach(var body in FlightGlobals.Bodies)if(OrePricing.TryGetBasePrice(body.bodyName,out double p)){old.Ensure(body.bodyName,p);old.SetSystem(body.bodyName,body.bodyName);}
  old.Advance(811.25);var saved=Save(old);saved.name="MARKET_STATE";
  // Real old-format records, with valid independently recorded prices/events.
  var forced=new MarketSettings();forced.Events.Extreme=1;var legacyBook=new MarketBook(forced,88);legacyBook.Ensure("LegacyFixture",245);legacyBook.Advance(600);
  foreach(var d in ObjectMarkets.PhysicalDefinitions) {
   var raw=Save(legacyBook).GetNodes("BODY").Single();raw.SetValue("name",d.Identity,true);saved.AddNode(raw);
  }
  var planets=saved.GetNodes("BODY").Where(n=>!ObjectMarkets.Reserved(n.GetValue("name"))).Select(Dump).ToArray();
  var root=new ConfigNode();root.AddNode(saved);var scenario=new MarketScenario();scenario.OnAwake();
  try {
   scenario.OnLoad(root);var output=new ConfigNode();scenario.OnSave(output);var migrated=output.GetNode("MARKET_STATE");
   check("0731: migration preserves clock, progress, update count and economy RNG",saved.values.All(v=>migrated.GetValue(v.name)==v.value));
   check("0731: every planetary record/history/event/RNG preserved exactly",migrated.GetNodes("BODY").Where(n=>!ObjectMarkets.Reserved(n.GetValue("name"))).Select(Dump).SequenceEqual(planets));
   check("0731: only one space-rock state, no fabricated merged history or old events",migrated.GetNodes("BODY").Count(n=>ObjectMarkets.Reserved(n.GetValue("name")))==1&&MarketScenario.History(ObjectMarkets.Shared,86400).Length==0&&MarketScenario.NewsFor(ObjectMarkets.Shared).Length==0&&MarketScenario.ActiveBook.Ensure(ObjectMarkets.Shared,100)==100);
   MarketScenario.ActiveBook.Advance(600);double live=0;MarketQuotes.TryGetUnitPrice(ObjectMarkets.Shared,out live);
   check("0731: all classes and Magic quote same live price",ObjectMarkets.PhysicalDefinitions.All(d=>MarketQuotes.TryGetUnitPrice(d.Identity,out double p)&&p==live));
   check("0731: legacy history queries resolve to sole recorded series",ObjectMarkets.PhysicalDefinitions.All(d=>MarketScenario.History(d.Identity,86400).Select(p=>p.Close).SequenceEqual(MarketScenario.History(ObjectMarkets.Shared,86400).Select(p=>p.Close))));
   var state=Save(MarketScenario.ActiveBook);var clone=MarketBook.Load(state,settings);bool exact=true;
   for(int i=0;i<40;i++){MarketScenario.ActiveBook.Advance(60);clone.Advance(60);exact &= Dump(Save(MarketScenario.ActiveBook))==Dump(Save(clone));}
   check("0731: migrated economy deterministic after save/load",exact);
   var roundtrip=new ConfigNode();scenario.OnSave(roundtrip);string before=Dump(roundtrip);scenario.OnLoad(roundtrip);var after=new ConfigNode();scenario.OnSave(after);
   check("0731: shared migration idempotent, exact quickload state",Dump(after)==before);
  }finally{scenario.OnDestroy();}
  var eventSettings=new MarketSettings();eventSettings.Events=new EventSettings{Ordinary=0,Significant=0,Major=0,Extreme=1,Concurrent=1};
  var events=new MarketBook(eventSettings,55);events.Ensure(ObjectMarkets.Shared,100);events.Advance(60);
  check("0731: shared market receives one event with correct scope",events.News().Length==1&&events.News()[0].Origin==ObjectMarkets.Shared&&events.News()[0].Scope=="Asteroid & Comet Ore"&&events.News()[0].Headline=="Interplanetary Resource Crisis");
 }
}
