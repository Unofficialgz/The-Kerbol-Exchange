using System;
using System.Linq;
using OreMarket;
using OreMarket.Terminal;
using KSP.UI.Screens;
internal static class Objects071Tests
{
    private sealed class Rig
    {
        internal Vessel Vessel;
        internal OreMarketTank Tank;
        internal BaseConverter Drill;
        internal Part Rock;
        internal PartResource Ore => Tank.part.Resources.Get("Ore");
        internal void Output(double units, double time)
        {
            Ore.amount += units; Drill.SetProcessedTime(time); Planetarium.UniversalTime=time;
            GameEvents.OnResourceConverterOutput.Fire(Drill,"Ore",units); Tank.FixedUpdate();
        }
        internal ProtoVessel Snapshot()
        {
            var proto=new ProtoVessel{vesselID=Vessel.id,landed=true};proto.orbitSnapShot.ReferenceBodyIndex=1;
            foreach(var p in Vessel.parts) {
                var snap=new ProtoPartSnapshot{persistentId=p.persistentId,partName=p.partInfo.name,partInfo=p.partInfo};
                foreach(var m in p.Modules) {var ms=new ProtoPartModuleSnapshot{moduleName=m.moduleName};m.OnSave(ms.moduleValues);snap.modules.Add(ms);}
                foreach(var r in p.Resources)snap.resources.Add(new ProtoPartResourceSnapshot{resourceName=r.resourceName,amount=r.amount,maxAmount=r.maxAmount});
                proto.protoPartSnapshots.Add(snap);
            }
            return proto;
        }
    }
    private static Rig Create(string letter,double preload=0,bool? cometKind=null)
    {
        bool comet=cometKind ?? (letter[0]>='F');var v=new Vessel{Landed=false};
        Func<uint,string,Part> part=(id,name)=>{var p=new Part{persistentId=id,vessel=v};p.partInfo=new AvailablePart{name=name,partPrefab=p};v.parts.Add(p);return p;};
        var tank=part(201,"SmallTank");tank.Resources.Add(new PartResource{amount=preload,maxAmount=150});
        var tracker=new OreMarketTank{part=tank};tank.Modules.Add(tracker);
        var tool=part(202,"MiniDrill");var rock=part(203,comet?"PotatoComet":"PotatoRoid");
        BaseConverter drill;
        if(comet) {
            rock.Modules.Add(new ModuleComet{part=rock,prefabBaseURL="Procedural/PC_"+letter});
            rock.Modules.Add(new ModuleCometInfo{part=rock});rock.Modules.Add(new ModuleCometResource{part=rock});
            var d=new ModuleCometDrill{part=tool};d.SetSource(rock);drill=d;
        } else {
            rock.Modules.Add(new ModuleAsteroid{part=rock,prefabBaseURL="Procedural/PA_"+letter});
            rock.Modules.Add(new ModuleAsteroidInfo{part=rock});rock.Modules.Add(new ModuleAsteroidResource{part=rock});
            var d=new ModuleAsteroidDrill{part=tool};d.SetSource(rock);drill=d;
        }
        tool.Modules.Add(drill);tracker.OnStart(PartModule.StartState.Flying);
        return new Rig{Vessel=v,Tank=tracker,Drill=drill,Rock=rock};
    }
    internal static void Run(Action<string,bool> check)
    {
        PlanetaryTests.Seed();OrePricing.Load(new[]{PricingTests.Shipped()});
        var observer=new MiningObserver();observer.Awake();var sales=new RecoverySales();sales.OnAwake();
        try {
            foreach(var definition in ObjectMarkets.PhysicalDefinitions.Where(d=>!d.Special)) {
                string letter=definition.Kind+" "+definition.ClassLetter;
                RecoveryTests.Reset(sales);
                var rig=Create(definition.ClassLetter,20,definition.Kind==SpaceObjectKind.Comet);string origin=definition.Identity;
                check("071: exact stock source / persisted class "+letter,ObjectMining.TrySource(rig.Drill,out string found,out uint source,out _)&&found==origin&&source==203);
                check("071: preloaded Ore has no sale quote "+letter,!SaleQuotes.TryGetUnitPrice(rig.Tank.State,out _)&&rig.Tank.State.Eligible==0);
                rig.Output(1,100);rig.Output(50,101);
                check("071: separate-tank callback credits only observed stock output "+letter,!rig.Tank.State.Invalid&&rig.Tank.State.Eligible==50&&rig.Tank.State.Origin==origin&&rig.Tank.State.ObjectSourceId==203);
                GameEvents.OnResourceConverterOutput.Fire(rig.Drill,"Ore",50);
                check("071: duplicate callback cannot duplicate Ore "+letter,rig.Tank.State.Eligible==50&&!rig.Tank.State.Invalid);
                GameEvents.onVesselGoOnRails.Fire(rig.Vessel);rig.Vessel.packed=true;rig.Output(10,20000);
                GameEvents.onVesselGoOffRails.Fire(rig.Vessel);rig.Vessel.packed=false;
                check("071: long loaded warp interval retains and adds credit "+letter,rig.Tank.State.Eligible==60&&!rig.Tank.State.Invalid&&rig.Drill.IsActivated);
                rig.Ore.amount-=12;rig.Tank.FixedUpdate();
                check("071: verified consumption debited first "+letter,rig.Tank.State.Eligible==48);
                var node=new ConfigNode();rig.Tank.OnSave(node);string batch=rig.Tank.State.BatchId;
                GameEvents.onVesselUnloaded.Fire(rig.Vessel);rig.Tank.OnLoad(node);rig.Tank.OnStart(PartModule.StartState.Flying);
                check("071: source, origin, batch, quantity survive save/load "+letter,rig.Tank.State.ObjectSourceId==203&&rig.Tank.State.Origin==origin&&rig.Tank.State.BatchId==batch&&rig.Tank.State.Eligible==48);
                rig.Output(1,21000);rig.Output(2,22000);
                check("071: reload continuity excludes first output without losing cargo "+letter,rig.Tank.State.Eligible==50&&!rig.Tank.State.Invalid);
                var snap=rig.Snapshot();SaleQuotes.TryGetUnitPrice(rig.Tank.State,out double price);
                Funding.Instance.Funds+=321;var dialog=new MissionRecoveryDialog{fundsEarned=321};
                GameEvents.onVesselRecoveryProcessingComplete.Fire(snap,dialog,1);
                check("071: actual recovery callback pays live object quote "+letter,Funding.Instance.Calls==1&&Math.Abs(Funding.Instance.LastAmount-50*price)<1e-8&&dialog.fundsEarned==321&&Funding.Instance.Funds==1321+50*price);
                var claims=new ConfigNode();sales.OnSave(claims);sales.OnLoad(claims);GameEvents.onVesselRecoveryProcessingComplete.Fire(snap,null,1);
                check("071: persistent claims reject duplicate object recovery "+letter,Funding.Instance.Calls==1);
            }
            var r=Create("C");r.Output(1,100);r.Output(10,101);r.Ore.amount+=1;r.Tank.FixedUpdate();
            check("071: unexpected Ore addition invalidates",r.Tank.State.Invalid&&r.Tank.State.Eligible==0);
            r=Create("C");r.Output(1,100);r.Output(10,101);r.Ore.amount+=20;r.Drill.SetProcessedTime(102);Planetarium.UniversalTime=102;GameEvents.OnResourceConverterOutput.Fire(r.Drill,"Ore",1);
            check("071: unrelated increase exceeding callback cannot gain provenance",r.Tank.State.Invalid);
            r=Create("C");r.Output(1,100);r.Output(10,101);r.Ore.maxAmount=300;r.Tank.FixedUpdate();check("071: resize after object mining invalidates",r.Tank.State.Invalid);
            r=Create("C");r.Output(1,100);r.Output(10,101);GameEvents.onPartDeCouple.Fire(r.Rock);check("071: releasing source / staging invalidates cargo",r.Tank.State.Invalid);
            r=Create("C");r.Output(1,100);r.Output(10,101);r.Drill.IsActivated=false;r.Output(2,102);check("071: inactive-drill output is never credited",r.Tank.State.Eligible<=10&& !r.Drill.IsActivated);
            r=Create("C");r.Output(1,100);r.Output(10,101);((ModuleAsteroid)r.Rock.Modules[0]).currentState=3;r.Output(5,102);check("071: unsupported state preserves old verified cargo but credits no new units",r.Tank.State.Eligible==10&&!r.Tank.State.Invalid);
            r=Create("C");((ModuleAsteroid)r.Rock.Modules[0]).currentState=2;r.Output(1,100);r.Output(10,101);
            check("0731: stock secondary asteroid output uses ordinary shared price",r.Tank.State.Eligible==10&&SaleQuotes.TryGetUnitPrice(r.Tank.State,out double secondaryPrice)&&secondaryPrice==100);
            r=Create("C");r.Output(1,100);r.Output(10,101);r.Drill.IsActivated=false;r.Tank.FixedUpdate();r.Drill.IsActivated=true;r.Output(2,110);
            check("071: ordinary interruption/restart retains provenance",r.Tank.State.Eligible==12&&!r.Tank.State.Invalid);
            foreach(var bad in new[]{"Procedural/PA_J","Procedural/PA_Cx","Procedural/PC_C","C",null})check("071: ambiguous or wrong class URL rejected "+bad,!ObjectMining.TryIdentity(false,bad,1,out _));
            check("0731: secondary asteroid preserves physical class without special price",ObjectMining.TryIdentity(false,"Procedural/PA_C",2,out var secondaryOrigin)&&secondaryOrigin==ObjectMarkets.Prefix+"Asteroid:C");
            r=Create("C");r.Output(1,100);r.Output(10,101);var state=r.Tank.State.Save();state["schema"]="2";state.Remove("objectSourceId");
            check("071: object origin without extraction proof rejected",TrackingState.Load(state).Invalid);
            var legacy=TrackingState.New(0,150);legacy.Mining("Mun",10,10,true,Guid.NewGuid().ToString("N"),"1:SmallTank");state=legacy.Save();state["schema"]="2";state.Remove("objectSourceId");
            check("071: legacy planetary schema 2 remains valid",TrackingState.Load(state).Eligible==10&&!TrackingState.Load(state).Invalid);
            r=Create("C");r.Output(1,100);r.Output(10,101);((ModuleAsteroid)r.Rock.Modules[0]).prefabBaseURL="Procedural/PA_D";r.Output(1,102);r.Output(1,103);
            check("071: changing object class cannot relabel a batch",r.Tank.State.Invalid&&r.Tank.State.Eligible==0);
            r=Create("C");r.Output(1,100);r.Output(10,101);r.Rock.persistentId=999;r.Output(1,102);r.Output(1,103);
            check("071: changing same-class source identity invalidates",r.Tank.State.Invalid);
            r=Create("C");r.Rock.Modules.Add(new ModuleComet{part=r.Rock,prefabBaseURL="Procedural/PC_F"});
            check("071: ambiguous source kinds are rejected",!ObjectMining.TrySource(r.Drill,out _,out _,out _));
            r=Create("C");r.Rock.Modules.OfType<ModuleAsteroidInfo>().Single().currentMassVal=10;
            check("071: depleted reserve cannot authorize output",!ObjectMining.TrySource(r.Drill,out _,out _,out _));
            r=Create("C");var second=new Part{persistentId=204,vessel=r.Vessel};second.partInfo=new AvailablePart{name="LargeTank",partPrefab=second};second.Resources.Add(new PartResource{maxAmount=1500});r.Vessel.parts.Add(second);
            r.Output(1,100);r.Output(10,101);check("071: untracked additional storage still fails explicitly",r.Tank.State.Invalid&&r.Tank.trackingStatus.Contains("Each Ore storage"));
            foreach(string invalid in new[]{"mode","body","manifest","provenance"}) {
                RecoveryTests.Reset(sales);r=Create("F");r.Output(1,100);r.Output(10,101);var snap=r.Snapshot();
                if(invalid=="mode")HighLogic.CurrentGame.Mode=Game.Modes.SANDBOX;
                if(invalid=="body")snap.orbitSnapShot.ReferenceBodyIndex=2;
                if(invalid=="manifest")snap.protoPartSnapshots.RemoveAt(2);
                if(invalid=="provenance")snap.protoPartSnapshots[0].modules[0].moduleValues.GetNode(OreMarketTank.NodeName).SetValue("objectSourceId","0",true);
                GameEvents.onVesselRecoveryProcessingComplete.Fire(snap,null,1);
                check("071: object recovery rejects unsupported "+invalid,Funding.Instance.Calls==0);
            }
            RecoveryTests.Reset(sales);r=Create("C");r.Output(1,100);r.Output(10,101);
            var scenario=new MarketScenario();scenario.OnAwake();scenario.OnLoad(new ConfigNode());
            try {
                MarketScenario.ActiveBook.Advance(600);SaleQuotes.TryGetUnitPrice(r.Tank.State,out double live);
                GameEvents.onVesselRecoveryProcessingComplete.Fire(r.Snapshot(),null,1);
                check("071: authoritative dynamic quote used at recovery instead of base",Math.Abs(Funding.Instance.LastAmount-10*live)<1e-8&&live!=100);
            } finally {scenario.OnDestroy();}
            foreach(string letter in new[]{"F","G","H","I"}) {
                RecoveryTests.Reset(sales);r=Create(letter,0,false);r.Output(1,100);r.Output(10,101);
                var liveScenario=new MarketScenario();liveScenario.OnAwake();liveScenario.OnLoad(new ConfigNode());
                try {
                    MarketScenario.ActiveBook.Advance(600);SaleQuotes.TryGetUnitPrice(r.Tank.State,out double quote);
                    GameEvents.onVesselRecoveryProcessingComplete.Fire(r.Snapshot(),null,1);
                    check("extended: asteroid "+letter+" recovery uses its independent live price",Funding.Instance.Calls==1&&Math.Abs(Funding.Instance.LastAmount-10*quote)<1e-8&&quote!=100);
                } finally {liveScenario.OnDestroy();}
            }
            r=Create("H",0,false);r.Output(1,100);r.Output(10,101);int count=OreMarket.Log.Lines.Count;
            GameEvents.onPartResourceListChange.Fire(r.Drill.part);int once=OreMarket.Log.Lines.Count;GameEvents.onPartResourceListChange.Fire(r.Drill.part);
            check("extended: resource-list event still invalidates; details logged once per batch",r.Tank.State.Invalid&&r.Tank.State.Eligible==0&&once>count&&OreMarket.Log.Lines.Count==once&&OreMarket.Log.Lines.Skip(count).Any(l=>l.Contains("Resource-list custody invalidation:")&&l.Contains("batch=")));
            r=Create("H",0,false);r.Output(1,100);r.Output(10,101);r.Drill.part.Resources.Add(null);GameEvents.onPartResourceListChange.Fire(r.Drill.part);
            check("extended: diagnostic failure cannot bypass resource-list safeguard",r.Tank.State.Invalid&&r.Tank.State.Eligible==0);
            var groups=new System.Collections.Generic.List<MarketGroup>();ObjectSidebar.Append(groups);
            check("0731: one standalone shared market replaces class groups",groups.Count==1&&!groups[0].Expandable&&groups[0].Markets.Single().Identity==ObjectMarkets.Shared);
            foreach(var d in ObjectMarkets.Definitions) {
                var pixels=ObjectSidebar.RockIcon(d.Identity,40);
                check("071: bounded nonempty procedural icon "+d.Identity,pixels.Length==1600&&pixels.Any(c=>c!=0));
            }
            var magic=ObjectSidebar.RockIcon(ObjectMarkets.Magic,40);
            check("071: Magic fissures contain vivid green",magic.Any(c=>(c>>16&255)>220&&(c>>24&255)<150&&(c>>8&255)<100));
            foreach(string letter in new[]{"F","G","H","I"})check("071: comet tail stays blue "+letter,ObjectSidebar.RockIcon(ObjectMarkets.Prefix+"Comet:"+letter,40).Any(c=>(c>>8&255)>(c>>24&255)+70));
            check("071: class icons change inside constant canvas",!ObjectSidebar.RockIcon(ObjectMarkets.Prefix+"Asteroid:A",40).SequenceEqual(ObjectSidebar.RockIcon(ObjectMarkets.Prefix+"Asteroid:E",40)));
        } finally {observer.OnDestroy();sales.OnDestroy();}
    }
}
