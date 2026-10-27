using System;
using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using System.Linq;
using OreMarket;
public sealed class CosmeticTankDouble { public string tankName="Structural"; public float tankMass=0,tankCost=0; public ArrayList resources = new ArrayList(); }
public sealed class CosmeticSubtypeDouble
{
    public string subtypeName="Stripe";
    public CosmeticTankDouble tankType=new CosmeticTankDouble();
    public ArrayList moduleModifierInfos=new ArrayList();
}
internal static class CosmeticSwitchTests
{
    private static Type profileType;
    private static Type BuildType()
    {
        var assembly=AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("B9PartSwitch"){Version=new Version(2,21,0,4)},AssemblyBuilderAccess.Run);
        var builder=assembly.DefineDynamicModule("B9Fixture").DefineType("B9PartSwitch.ModuleB9PartSwitch",TypeAttributes.Public,typeof(PartModule));
        builder.DefineDefaultConstructor(MethodAttributes.Public);
        foreach(string field in new[]{"moduleID","parentID"})builder.DefineField(field,typeof(string),FieldAttributes.Public);
        foreach(string field in new[]{"subtypes","children"})builder.DefineField(field,typeof(ArrayList),FieldAttributes.Public);
        // Reproduce the original static rejection: capability exists in the type,
        // while the configured cosmetic instance never executes it.
        var method=builder.DefineMethod("PotentialResourceRemoval",MethodAttributes.Public,typeof(void),new[]{typeof(PartResource)});
        var il=method.GetILGenerator();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldfld,typeof(PartModule).GetField("part"));
        il.Emit(OpCodes.Ldfld,typeof(Part).GetField("Resources"));il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt,typeof(PartResourceList).GetMethod("Remove",BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly));
        il.Emit(OpCodes.Pop);il.Emit(OpCodes.Ret);
        return builder.CreateType();
    }
    private static ConfigNode Config()
    {
        var root=new ConfigNode();var module=root.AddNode("MODULE");module.AddValue("name","ModuleB9PartSwitch");module.AddValue("moduleID","lookSwitch");module.AddValue("switcherDescription","Variant");module.AddValue("affectDragCubes","false");
        foreach(string name in new[]{"Stripe","Metal","White"}) {var st=module.AddNode("SUBTYPE");st.AddValue("name",name);st.AddValue("transform","DockingPort"+name);st.AddValue("primaryColor","#999999");}
        return root;
    }
    private static PartModule Cosmetic(Part part)
    {
        var m=(PartModule)Activator.CreateInstance(profileType);m.part=part;
        profileType.GetField("moduleID").SetValue(m,"lookSwitch");profileType.GetField("parentID").SetValue(m,"");
        profileType.GetField("children").SetValue(m,new ArrayList());
        var subtypes=new ArrayList();foreach(string name in new[]{"Stripe","Metal","White"})subtypes.Add(new CosmeticSubtypeDouble{subtypeName=name});
        profileType.GetField("subtypes").SetValue(m,subtypes);part.partInfo.partConfig=Config();part.Modules.Add(m);return m;
    }
    private static Part Part()
    {
        var p=new Part{persistentId=91,vessel=new Vessel()};p.partInfo=new AvailablePart{name="dockingPort2",partPrefab=p};p.vessel.parts.Add(p);return p;
    }
    private static ConfigNode Snapshot()
    {
        var n=new ConfigNode();n.AddValue("name","ModuleB9PartSwitch");n.AddValue("moduleID","lookSwitch");n.AddValue("currentSubtype","Metal");n.AddValue("isEnabled","True");n.AddNode("EVENTS");n.AddNode("ACTIONS");n.AddNode("UPGRADESAPPLIED");return n;
    }
    internal static void Run(Action<string,bool> check)
    {
        profileType=BuildType();var p=Part();var m=Cosmetic(p);
        check("B9 hotfix: reproduces original type-only resource-removal rejection",!ModuleSafetyInspector.CanTrack(profileType,out string reason)&&reason.Contains("Remove"));
        check("B9 hotfix: matching cosmetic config and all resource-empty subtypes proven",CosmeticSwitchSafety.ProvesNoResourceSwitch(m));
        check("B9 hotfix: matching saved appearance remains safe at recovery",CosmeticSwitchSafety.RecoveredSafe(profileType,p,Snapshot()));
        var st=(CosmeticSubtypeDouble)((ArrayList)profileType.GetField("subtypes").GetValue(m))[2];st.tankType.resources.Add("Ore");
        check("B9 hotfix: inactive Ore-capable subtype rejected",!CosmeticSwitchSafety.ProvesNoResourceSwitch(m));
        st.tankType.resources.Clear();st.tankType.resources.Add("LiquidFuel");check("B9 hotfix: resource switching outside narrow cosmetic profile still rejected",!CosmeticSwitchSafety.ProvesNoResourceSwitch(m));st.tankType.resources.Clear();
        st.tankType.tankName="NonStructural";check("B9 hotfix: empty nonstructural tank is not assumed cosmetic",!CosmeticSwitchSafety.ProvesNoResourceSwitch(m));st.tankType.tankName="Structural";
        st.moduleModifierInfos.Add(new object());check("B9 hotfix: runtime MODULE modifiers rejected",!CosmeticSwitchSafety.ProvesNoResourceSwitch(m));st.moduleModifierInfos.Clear();
        ((ArrayList)profileType.GetField("children").GetValue(m)).Add(new object());check("B9 hotfix: child volume network rejected",!CosmeticSwitchSafety.ProvesNoResourceSwitch(m));profileType.GetField("children").SetValue(m,new ArrayList());
        foreach(string field in new[]{"parentID","baseVolume"}) {
            var config=p.partInfo.partConfig.GetNode("MODULE");config.AddValue(field,"1");check("B9 hotfix: resource/parent configuration rejected "+field,!CosmeticSwitchSafety.ProvesNoResourceSwitch(m));config.RemoveValues(field);
        }
        var subtype=p.partInfo.partConfig.GetNode("MODULE").GetNodes("SUBTYPE")[2];
        foreach(string field in new[]{"tankType","volumeAdded","volumeAddedToParent","percentFilled"}) {subtype.AddValue(field,"Ore");check("B9 hotfix: unsupported subtype setting rejected "+field,!CosmeticSwitchSafety.ProvesNoResourceSwitch(m));subtype.RemoveValues(field);}
        subtype.AddNode("MODULE");check("B9 hotfix: configured module modification rejected",!CosmeticSwitchSafety.ProvesNoResourceSwitch(m));subtype.RemoveNodes("MODULE");
        var saved=Snapshot();saved.AddValue("baseVolume","100");check("B9 hotfix: unsafe saved override rejected",!CosmeticSwitchSafety.RecoveredSafe(profileType,p,saved));
        saved=Snapshot();saved.SetValue("moduleID","wrong",true);check("B9 hotfix: mismatched snapshot rejected",!CosmeticSwitchSafety.RecoveredSafe(profileType,p,saved));
        saved=Snapshot();saved.SetValue("currentSubtype","Ore",true);check("B9 hotfix: unknown saved subtype rejected",!CosmeticSwitchSafety.RecoveredSafe(profileType,p,saved));
        st.tankType=null;check("B9 hotfix: uninitialized runtime subtype fails closed",!CosmeticSwitchSafety.ProvesNoResourceSwitch(m));st.tankType=new CosmeticTankDouble();
        check("B9 hotfix: configuration changes are reassessed, not cached as permission",CosmeticSwitchSafety.ProvesNoResourceSwitch(m));
        PlanetaryTests.Seed();OrePricing.Load(new[]{PricingTests.Shipped()});
        var cargo=RecoveryTests.Cargo("Mun",50);var tankSnapshot=cargo.protoPartSnapshots[0];
        // Add a cosmetic module to an existing part: no vessel/part custody changes.
        var prefab=tankSnapshot.partInfo.partPrefab;var cosmetic=Cosmetic(prefab);
        tankSnapshot.modules.Add(new ProtoPartModuleSnapshot{moduleName=cosmetic.moduleName,moduleValues=Snapshot()});
        check("B9 hotfix: supported vessel with cosmetic B9 passes loaded rules",VesselRules.Loaded(prefab.vessel,prefab.Modules.OfType<OreMarketTank>().Single(),out reason));
        check("B9 hotfix: recovered cosmetic module passes instance and saved checks",VesselRules.Recovered(cargo,out _,out reason));
        foreach(bool comet in new[]{false,true}) {
            var v=new Vessel{Landed=false};var tank=new Part{persistentId=301,vessel=v};tank.partInfo=new AvailablePart{name="SmallTank",partPrefab=tank};tank.Resources.Add(new PartResource{amount=20,maxAmount=150});
            var tracker=new OreMarketTank{part=tank};tank.Modules.Add(tracker);v.parts.Add(tank);
            var tool=new Part{persistentId=302,vessel=v};tool.partInfo=new AvailablePart{name="MiniDrill",partPrefab=tool};v.parts.Add(tool);
            var rock=new Part{persistentId=303,vessel=v};rock.partInfo=new AvailablePart{name=comet?"PotatoComet":"PotatoRoid",partPrefab=rock};v.parts.Add(rock);
            var appearance=Part();appearance.vessel=v;appearance.persistentId=304;v.parts.Add(appearance);Cosmetic(appearance);
            BaseConverter drill;
            if(comet) {rock.Modules.Add(new ModuleComet{part=rock,prefabBaseURL="Procedural/PC_F"});rock.Modules.Add(new ModuleCometInfo{part=rock});rock.Modules.Add(new ModuleCometResource{part=rock});var d=new ModuleCometDrill{part=tool};d.SetSource(rock);drill=d;}
            else {rock.Modules.Add(new ModuleAsteroid{part=rock,prefabBaseURL="Procedural/PA_C"});rock.Modules.Add(new ModuleAsteroidInfo{part=rock});rock.Modules.Add(new ModuleAsteroidResource{part=rock});var d=new ModuleAsteroidDrill{part=tool};d.SetSource(rock);drill=d;}
            tool.Modules.Add(drill);tracker.OnStart(PartModule.StartState.Flying);var observer=new MiningObserver();observer.Awake();
            try {
                for(int i=0;i<2;i++){tank.Resources.Get("Ore").amount+=10;drill.SetProcessedTime(100+i);Planetarium.UniversalTime=100+i;GameEvents.OnResourceConverterOutput.Fire(drill,"Ore",10);tracker.FixedUpdate();}
                check("B9 hotfix: genuine "+drill.GetType().Name+" callback credits separate tank despite cosmetic B9",tracker.State.Eligible==10&&!tracker.State.Invalid&&tracker.State.Origin==ObjectMarkets.Prefix+(comet?"Comet:F":"Asteroid:C"));
                tank.Resources.Get("Ore").amount+=1;tracker.FixedUpdate();check("B9 hotfix: cosmetic compatibility does not authorize injected Ore "+comet,tracker.State.Invalid&&tracker.State.Eligible==0);
                if(!comet) {((ModuleAsteroid)rock.Modules[0]).prefabBaseURL="Procedural/PA_H";check("extended classes: Class H asteroid gets its own identity",ObjectMining.TrySource(drill,out string extended,out _,out _)&&extended==ObjectMarkets.Prefix+"Asteroid:H");}
            } finally {observer.OnDestroy();}
        }
        var sales=new RecoverySales();sales.OnAwake();try {RecoveryTests.Reset(sales);GameEvents.onVesselRecoveryProcessingComplete.Fire(cargo,null,1);check("B9 hotfix: verified planetary recovery still pays with cosmetic switch",Funding.Instance.Calls==1&&Funding.Instance.LastAmount==2000);}finally{sales.OnDestroy();}
    }
}
