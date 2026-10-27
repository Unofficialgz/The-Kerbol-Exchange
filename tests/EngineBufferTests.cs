using System;
using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Linq;
using OreMarket;
public sealed class EngineBufferDouble { public double OriginalMaxAmount=0; public PartResource Resource; public Propellant Propellant; }
internal static class EngineBufferTests
{
    private static Type moduleType;
    private static Type BuildType()
    {
        var a=AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("BackgroundThrust"){Version=new Version(0,6,12,0)},AssemblyBuilderAccess.Run);
        var t=a.DefineDynamicModule("BackgroundFixture").DefineType("BackgroundThrust.BackgroundEngine",TypeAttributes.Public,typeof(PartModule));t.DefineDefaultConstructor(MethodAttributes.Public);
        t.DefineField("buffers",typeof(Hashtable),FieldAttributes.Public);t.DefineField("<Engine>k__BackingField",typeof(ModuleEngines),FieldAttributes.Public);
        var m=t.DefineMethod("PossibleRefund",MethodAttributes.Public,typeof(void),new[]{typeof(string)});var il=m.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldfld,typeof(PartModule).GetField("part"));il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Ldc_R8,-1d);
        il.Emit(OpCodes.Callvirt,typeof(Part).GetMethod("RequestResource"));il.Emit(OpCodes.Pop);il.Emit(OpCodes.Ret);return t.CreateType();
    }
    private static PartModule Attach(Part part)
    {
        var engine=new ModuleEnginesFX{part=part};engine.propellants.Add(new Propellant{name="MonoPropellant",id=1});part.Modules.Add(engine);
        var module=(PartModule)Activator.CreateInstance(moduleType);module.part=part;part.Modules.Add(module);
        var mono=new PartResource{resourceName="MonoPropellant",amount=0,maxAmount=0};part.Resources.Add(mono);
        moduleType.GetField("buffers").SetValue(module,new Hashtable{{"MonoPropellant",new EngineBufferDouble{Resource=mono}}});
        moduleType.GetField("<Engine>k__BackingField").SetValue(module,engine);
        if(part.partInfo.partConfig==null)part.partInfo.partConfig=new ConfigNode();var n=part.partInfo.partConfig.AddNode("MODULE");n.AddValue("name","BackgroundEngine");return module;
    }
    private static ConfigNode Saved(string resource="MonoPropellant")
    {
        var n=new ConfigNode();n.AddValue("name","BackgroundEngine");n.AddValue("IsEnabled","True");n.AddValue("isEnabled","False");n.AddNode("EVENTS");n.AddNode("ACTIONS");n.AddNode("UPGRADESAPPLIED");
        var b=n.AddNode("BUFFER");b.AddValue("ResourceName",resource);b.AddValue("OriginalMaxAmount","0");return n;
    }
    internal static void Run(Action<string,bool> check)
    {
        moduleType=BuildType();PartResourceLibrary.Instance.Definitions.Clear();
        foreach(var x in new[]{new PartResourceDefinition{name="MonoPropellant",id=1},new PartResourceDefinition{name="LiquidFuel",id=2},new PartResourceDefinition{name="Ore",id=3}})PartResourceLibrary.Instance.Definitions[x.name]=x;
        PlanetaryTests.Seed();OrePricing.Load(new[]{PricingTests.Shipped()});
        var cargo=RecoveryTests.Cargo("Mun",50);var tank=cargo.protoPartSnapshots[0].partInfo.partPrefab;var module=Attach(tank);
        check("0712: actual failure shape remains type-level unsafe",!ModuleSafetyInspector.CanTrack(moduleType,out string reason)&&reason.Contains("RequestResource"));
        check("0712: proven non-Ore engine and saved Mono buffer accepted",EngineBufferSafety.ProvesNonOre(module)&&EngineBufferSafety.RecoveredSafe(moduleType,tank,Saved()));
        var buffer=(EngineBufferDouble)((Hashtable)moduleType.GetField("buffers").GetValue(module))["MonoPropellant"];
        var engine=tank.Modules.OfType<ModuleEngines>().Single();engine.propellants[0].id=3;
        check("0712: benign name hiding Ore resource ID rejected",!EngineBufferSafety.ProvesNonOre(module));engine.propellants[0].id=1;
        engine.propellants[0].name="Ore";check("0712: Ore engine rejected",!EngineBufferSafety.ProvesNonOre(module));engine.propellants[0].name="MonoPropellant";
        PartResourceLibrary.Instance.Definitions["OreAlias"]=new PartResourceDefinition{name="OreAlias",id=3};engine.propellants[0].name="OreAlias";engine.propellants[0].id=3;
        check("0712: alternative name resolving to stock Ore ID rejected",!EngineBufferSafety.ProvesNonOre(module));engine.propellants[0].name="MonoPropellant";engine.propellants[0].id=1;
        var alternate=new ModuleEngines{part=tank};alternate.propellants.Add(new Propellant{name="Ore",id=3});tank.Modules.Add(alternate);
        check("0712: inactive Ore engine mode rejected",!EngineBufferSafety.ProvesNonOre(module));tank.Modules.Remove(alternate);
        buffer.Resource=tank.Resources.Get("Ore");check("0712: non-Ore key pointing at Ore resource rejected",!EngineBufferSafety.ProvesNonOre(module));buffer.Resource=tank.Resources.Get("MonoPropellant");
        var buffers=(Hashtable)moduleType.GetField("buffers").GetValue(module);buffers["Ore"]=new EngineBufferDouble();check("0712: stale Ore buffer rejected",!EngineBufferSafety.ProvesNonOre(module));buffers.Remove("Ore");
        buffers["UnknownResource"]=new EngineBufferDouble();check("0712: unresolved buffer resource rejected",!EngineBufferSafety.ProvesNonOre(module));buffers.Remove("UnknownResource");
        buffer.OriginalMaxAmount=double.NaN;check("0712: malformed buffer capacity rejected",!EngineBufferSafety.ProvesNonOre(module));buffer.OriginalMaxAmount=0;
        buffer.Propellant=new Propellant{name="Ore",id=3};check("0712: stale buffer Ore propellant rejected",!EngineBufferSafety.ProvesNonOre(module));buffer.Propellant=null;
        check("0712: saved Ore buffer rejected",!EngineBufferSafety.RecoveredSafe(moduleType,tank,Saved("Ore")));
        var saved=Saved();saved.GetNode("BUFFER").AddValue("ResourceName","Ore");check("0712: ambiguous saved buffer identity rejected",!EngineBufferSafety.RecoveredSafe(moduleType,tank,saved));
        saved=Saved();saved.GetNode("BUFFER").SetValue("OriginalMaxAmount","-1",true);check("0712: invalid saved buffer rejected",!EngineBufferSafety.RecoveredSafe(moduleType,tank,saved));
        int logged=OreMarket.Log.Lines.Count;
        for(int i=0;i<25;i++)EngineBufferSafety.Report(module,true,"dedup regression");
        check("0712: module/resource/operation diagnostic is emitted once per context",OreMarket.Log.Lines.Count==logged+1&&OreMarket.Log.Lines.Last().Contains("resources=MonoPropellant")&&OreMarket.Log.Lines.Last().Contains("operation=dedup regression"));
        var invalid=TrackingState.New(10,150);invalid.Invalidate("Unsupported module operation in ModuleB9PartSwitch");invalid=TrackingState.Load(invalid.Save());invalid.MiningObject(ObjectMarkets.Prefix+"Asteroid:C",10,10,true,Guid.NewGuid().ToString("N"),"1:SmallTank",1);
        check("0712: compatibility update never restores persisted invalid provenance",invalid.Invalid&&invalid.Eligible==0);
        check("0712: matching initialized buffer scope passes loaded custody",VesselRules.Loaded(tank.vessel,tank.Modules.OfType<OreMarketTank>().Single(),out reason));
        cargo.protoPartSnapshots[0].modules.Add(new ProtoPartModuleSnapshot{moduleName="BackgroundEngine",moduleValues=Saved()});
        check("0712: safe saved buffers pass recovery validation",VesselRules.Recovered(cargo,out _,out reason));
        var sales=new RecoverySales();sales.OnAwake();try {RecoveryTests.Reset(sales);GameEvents.onVesselRecoveryProcessingComplete.Fire(cargo,null,1);check("0712: planetary payout preserved with proven non-Ore buffers",Funding.Instance.Calls==1&&Funding.Instance.LastAmount==2000);GameEvents.onVesselRecoveryProcessingComplete.Fire(cargo,null,1);check("0712: duplicate protection preserved",Funding.Instance.Calls==1);}finally{sales.OnDestroy();}
        var observer=new MiningObserver();observer.Awake();try {
            foreach(string origin in new[]{"Mun","Asteroid:C","Comet:F"}) {
                var vessel=new Vessel{Landed=origin=="Mun",mainBody=OreOrigins.Resolve("Mun")};var storage=new Part{persistentId=401,vessel=vessel};storage.partInfo=new AvailablePart{name="SmallTank",partPrefab=storage};storage.Resources.Add(new PartResource{amount=20,maxAmount=150});vessel.parts.Add(storage);
                var tracker=new OreMarketTank{part=storage};storage.Modules.Add(tracker);var tool=new Part{persistentId=402,vessel=vessel};tool.partInfo=new AvailablePart{name="MiniDrill",partPrefab=tool};vessel.parts.Add(tool);BaseConverter drill;
                if(origin=="Mun")drill=new ModuleResourceHarvester{part=tool,ResourceName="Ore"};
                else {bool comet=origin.StartsWith("Comet",StringComparison.Ordinal);var rock=new Part{persistentId=403,vessel=vessel};rock.partInfo=new AvailablePart{name=comet?"PotatoComet":"PotatoRoid",partPrefab=rock};vessel.parts.Add(rock);
                    if(comet){rock.Modules.Add(new ModuleComet{part=rock,prefabBaseURL="Procedural/PC_F"});rock.Modules.Add(new ModuleCometInfo{part=rock});rock.Modules.Add(new ModuleCometResource{part=rock});var d=new ModuleCometDrill{part=tool};d.SetSource(rock);drill=d;}
                    else{rock.Modules.Add(new ModuleAsteroid{part=rock,prefabBaseURL="Procedural/PA_C"});rock.Modules.Add(new ModuleAsteroidInfo{part=rock});rock.Modules.Add(new ModuleAsteroidResource{part=rock});var d=new ModuleAsteroidDrill{part=tool};d.SetSource(rock);drill=d;}
                }
                tool.Modules.Add(drill);var enginePart=new Part{persistentId=404,vessel=vessel};enginePart.partInfo=new AvailablePart{name="omsEngine",partPrefab=enginePart};vessel.parts.Add(enginePart);Attach(enginePart);tracker.OnStart(PartModule.StartState.Flying);
                for(int i=0;i<2;i++){storage.Resources.Get("Ore").amount+=10;drill.SetProcessedTime(100+i);Planetarium.UniversalTime=100+i;GameEvents.OnResourceConverterOutput.Fire(drill,"Ore",10);tracker.FixedUpdate();}
                check("0712: "+origin+" stock mining survives unrelated engine buffers",!tracker.State.Invalid&&tracker.State.Eligible==10&&tracker.State.Origin==(origin=="Mun"?origin:ObjectMarkets.Prefix+origin));
                vessel.packed=true;GameEvents.onVesselGoOnRails.Fire(vessel);storage.Resources.Get("Ore").amount+=5;drill.SetProcessedTime(20000);Planetarium.UniversalTime=20000;GameEvents.OnResourceConverterOutput.Fire(drill,"Ore",5);
                var node=new ConfigNode();tracker.OnSave(node);tracker.OnLoad(node);tracker.OnStart(PartModule.StartState.Flying);check("0712: warp/save/load with buffer profile preserves "+origin,tracker.State.Eligible==15&&!tracker.State.Invalid);
                storage.Resources.Get("Ore").amount+=1;tracker.FixedUpdate();check("0712: unexplained Ore still invalidates "+origin,tracker.State.Invalid&&tracker.State.Eligible==0);
            }
        }finally{observer.OnDestroy();}
    }
}
