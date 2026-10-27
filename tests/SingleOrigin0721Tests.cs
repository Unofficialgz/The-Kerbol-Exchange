using System;
using System.Linq;
using OreMarket;
internal static class SingleOrigin0721Tests {
 internal static void Run(Action<string,bool> check) {
 PlanetaryTests.Seed();OrePricing.Load(new[]{PricingTests.Shipped()});var observer=new MiningObserver();observer.Awake();
 try {
 var r=new MultiTank072Tests.Rig();r.Output(r.Drill,100,1,0,0);r.Output(r.Drill,101,10,0,0);
 r.Vessel.mainBody.bodyName="Eve";r.Output(r.Drill,102,1,0,0);r.Output(r.Drill,103,5,0,0);
 check("0721: incompatible mining routes to empty storage",r.Tanks[0].Amount==11&&r.Tanks[0].State.Eligible==10&&r.Tanks[1].State.Origin=="Eve"&&r.Tanks[1].State.Eligible==5);
 double before=r.Verified,a=r.Tanks[0].Amount,b=r.Tanks[1].Amount;
 var ui=new UIPartActionResourceTransfer{resource=r.Tanks[0].part.Resources.Get("Ore"),State=2};ui.Targets.Add(new UIPartActionResourceTransfer{resource=r.Tanks[1].part.Resources.Get("Ore"),State=1});int calls=0;ui.UpdateOperation=()=>calls++;ui.UpdateItem();
 check("0721: mismatch prevented before stock executes without cargo loss",calls==0&&ui.State==0&&r.Verified==before&&r.Tanks[0].Amount==a&&r.Tanks[1].Amount==b&&r.Tanks.All(t=>!t.State.Invalid));
 r.Transfer(0,2,a);check("0721: emptied tank clears assignment and status",r.Tanks[0].State.Origin==""&&r.Tanks[0].trackingStatus=="Waiting for Ore"&&r.Tanks[0].oreOrigin=="Waiting for Ore");
 r.Transfer(1,0,2);check("0721: empty tank accepts different commodity",r.Tanks[0].State.Origin=="Eve"&&r.Tanks[0].Amount==2&&Math.Abs(r.Verified-before)<1e-7);
 r.Reload();check("0721: assignments survive save/load",r.Tanks[0].State.Origin=="Eve"&&r.Tanks[2].State.Origin=="Mun"&&r.Tanks.All(t=>!t.State.Invalid));
 r.Vessel.packed=true;r.Output(r.Drill,10000,2,0,0);check("0721: loaded warp preserves single origins",r.Tanks.All(t=>t.State.Lots.Select(l=>l.Origin).Distinct().Count()<=1&&!t.State.Invalid));
 }finally{observer.OnDestroy();}
 }
}
