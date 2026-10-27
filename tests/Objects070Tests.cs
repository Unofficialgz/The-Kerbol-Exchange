using System;
using System.Linq;
using System.Collections.Generic;
using OreMarket;
using OreMarket.Terminal;
internal static class Objects070Tests {
 internal static void Run(Action<string,bool> check) {
  PlanetaryTests.Seed();check("0731: shipped shared configuration loads",OrePricing.Load(new[]{PricingTests.Shipped()}));
  check("0731: one tradeable shared market",ObjectMarkets.Definitions.Length==1&&ObjectMarkets.Definitions[0].Identity==ObjectMarkets.Shared&&!ObjectMarkets.Definitions[0].Special);
  foreach(var d in ObjectMarkets.PhysicalDefinitions) {
   check("0731: physical identity shares base "+d.Identity,MarketQuotes.TryGetBasePrice(d.Identity,out double p)&&p==100&&!OrePricing.TryGetBasePrice(d.Identity,out _));
  }
  for(int i=0;i<9;i++) {
   check("0731: independent asteroid class identity "+i,ObjectMarkets.TryClassify(true,false,(UntrackedObjectClass)i,ObjectVariantEvidence.Ordinary,out var a)&&a.Kind==SpaceObjectKind.Asteroid&&a.PhysicalClass==(UntrackedObjectClass)i&&ObjectMarkets.Commodity(a.MarketIdentity)==ObjectMarkets.Shared);
   if(i>=5)check("0731: independent comet class identity "+i,ObjectMarkets.TryClassify(false,true,(UntrackedObjectClass)i,ObjectVariantEvidence.Ordinary,out var c)&&c.Kind==SpaceObjectKind.Comet&&c.MarketIdentity!=a.MarketIdentity&&ObjectMarkets.Commodity(c.MarketIdentity)==ObjectMarkets.Shared);
  }
  check("0731: ambiguous kinds and unsupported classes rejected",!ObjectMarkets.TryClassify(true,true,UntrackedObjectClass.A,ObjectVariantEvidence.Ordinary,out _)&&!ObjectMarkets.TryClassify(true,false,(UntrackedObjectClass)9,ObjectVariantEvidence.Ordinary,out _)&&!ObjectMarkets.TryClassify(false,true,UntrackedObjectClass.A,ObjectVariantEvidence.Ordinary,out _));
  check("0731: unknown variant is not proof",ObjectMarkets.TryClassify(true,false,UntrackedObjectClass.A,ObjectVariantEvidence.Unknown,out var unknown)&&unknown.MarketIdentity==null);
  check("0731: unknown object has no price fallback",!MarketQuotes.TryGetBasePrice(ObjectMarkets.Prefix+"Asteroid:Z",out _));
  var groups=new List<MarketGroup>();ObjectSidebar.Append(groups);
  var session=new TerminalSession{Selected=ObjectMarkets.Prefix+"Comet:H"};session.Bind(groups);
  check("0731: old UI selection resolves to shared market",groups.Count==1&&!groups[0].Expandable&&session.Selected==ObjectMarkets.Shared&&session.Selection.Title=="Asteroid & Comet Ore"&&session.Selection.Tradeable);
  var cfg=PricingTests.Shipped();cfg.GetNodes("OBJECT").Single().SetValue("price","123",true);OrePricing.Load(new[]{cfg});
  check("0731: one configurable price reaches every physical class",ObjectMarkets.PhysicalDefinitions.All(d=>MarketQuotes.TryGetBasePrice(d.Identity,out double p)&&p==123));
  var legacy=cfg.AddNode("OBJECT");legacy.AddValue("name",ObjectMarkets.Prefix+"Asteroid:H");legacy.AddValue("price","999");OrePricing.Load(new[]{cfg});
  check("0731: legacy class override cannot create another price",MarketQuotes.TryGetBasePrice(ObjectMarkets.Prefix+"Asteroid:H",out double q)&&q==123);
  var duplicate=cfg.AddNode("OBJECT");duplicate.AddValue("name",ObjectMarkets.Shared);duplicate.AddValue("price","333");OrePricing.Load(new[]{cfg});
  check("0731: duplicate configuration fails object quotes closed only",!MarketQuotes.TryGetBasePrice(ObjectMarkets.Shared,out _)&&OrePricing.TryGetBasePrice("Mun",out double mun)&&mun==40);
  var oldConfig=PricingTests.Shipped();oldConfig.nodes.RemoveAll(n=>n.name=="OBJECT");var old=oldConfig.AddNode("OBJECT");old.AddValue("name",ObjectMarkets.Prefix+"Asteroid:H");old.AddValue("price","245");OrePricing.Load(new[]{oldConfig});
  check("0731: old class-only config safely defaults shared base to 100",MarketQuotes.TryGetBasePrice(ObjectMarkets.Shared,out double fallback)&&fallback==100);
  OrePricing.Load(new[]{PricingTests.Shipped()});
  check("0731: event probabilities unchanged",OrePricing.Settings.Events.Significant==.004&&OrePricing.Settings.Events.Extreme==.0035&&OrePricing.Settings.Events.Ordinary==.02&&OrePricing.Settings.Events.Major==.003);
 }
}
