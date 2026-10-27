// Minimal OFFLINE KSP doubles for exercising the real VesselRules/OreMarketTank
// adapters. They do not simulate Unity, stock drilling, scene order or recovery.
using System;
using System.Collections.Generic;
using System.Linq;

[AttributeUsage(AttributeTargets.Field)]
public sealed class KSPField : Attribute
{
    public bool guiActive;
    public string guiName;
}
[AttributeUsage(AttributeTargets.Method)]
public sealed class KSPEvent : Attribute
{
    public bool active, guiActive, guiActiveEditor;
    public string guiName;
}
public sealed class BaseEventDetails
{
    private readonly Dictionary<string, object> values = new Dictionary<string, object>();
    public T Get<T>(string name) => (T)values[name];
    public void Set<T>(string name, T value) { values[name] = value; }
}
public sealed class BaseField { public bool guiActive; }
public sealed class BaseFieldList {
 private readonly Dictionary<string,BaseField> fields=new Dictionary<string,BaseField>();
 public BaseField this[string name] {get {if(!fields.ContainsKey(name))fields[name]=new BaseField();return fields[name];}}
}
public class PartModule
{
    public readonly BaseFieldList Fields=new BaseFieldList();
    public enum StartState { Flying }
    public Part part;
    public Vessel vessel => part?.vessel;
    public string moduleName => GetType().Name;
    public virtual void OnStart(StartState state) { }
    public virtual void OnLoad(ConfigNode node) { }
    public virtual void OnSave(ConfigNode node) { }
}
public sealed class PartResource
{
    public string resourceName = "Ore";
    public Part part;
    public double amount, maxAmount;
}
public sealed class PartResourceList : List<PartResource>
{
    public new bool Remove(PartResource resource) => base.Remove(resource);
    public PartResource Get(string name) => this.FirstOrDefault(r => r.resourceName == name);
}
public sealed class Part
{
    public uint persistentId;
    public AvailablePart partInfo;
    public Vessel vessel;
    public readonly PartResourceList Resources = new PartResourceList();
    public readonly List<PartModule> Modules = new List<PartModule>();
    public Action TopologyOperation;
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] public void Couple(Part other){TopologyOperation?.Invoke();}
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] public void Undock(DockedVesselInfo info){TopologyOperation?.Invoke();}
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] public void decouple(float force){TopologyOperation?.Invoke();}
    public double RequestResource(string name, double amount) => 0; // Only inspected; not a resource simulation.
}
public sealed class AvailablePart
{
    public string name;
    public Part partPrefab;
    public ConfigNode partConfig = new ConfigNode();
}
public sealed class CelestialBody
{
    public string bodyName = "Mun";
    public CelestialBody referenceBody;
    public int flightGlobalsIndex;
    public bool hasSolidSurface = true, isStar;
    public string bodyDisplayName;
}
public sealed class Vessel
{
    public Guid id = Guid.NewGuid();
    public bool loaded = true, packed, Landed = true;
    public CelestialBody mainBody = new CelestialBody();
    public readonly List<Part> parts = new List<Part>();
}
public class BaseConverter : PartModule
{
    protected double lastUpdateTime;
    public bool IsActivated = true;
    public string status = "Stopped";
    public void SetProcessedTime(double time) { lastUpdateTime = time; }
    public readonly List<ResourceRatio> inputList = new List<ResourceRatio>();
    public readonly List<ResourceRatio> outputList = new List<ResourceRatio>();
}
public sealed class ResourceRatio { public string ResourceName; public ResourceFlowMode FlowMode = ResourceFlowMode.NULL; }
public sealed class ModuleResourceHarvester : BaseConverter
{
    public int HarvesterType;
    public string ResourceName;
    public float HarvestThreshold = 0.025f;
    public object PrepareRecipe(double interval) { HarvestProbe.Attempts++; return null; }
    public string GetInfo() { HarvestProbe.Attempts++; return "Probe should never execute"; }
}
public static class HarvestProbe { public static int Attempts; }
public sealed class ModuleAnimationGroup : PartModule { public bool isDeployed; }
public sealed class ModuleAsteroidDrill : BaseConverter { private Part _potato; public void SetSource(Part p) { _potato=p; } public Part TestSource => _potato; }
public sealed class ModuleCometDrill : BaseConverter { private Part _potato; public void SetSource(Part p) { _potato=p; } public Part TestSource => _potato; }
public sealed class ModuleFuelJettison : PartModule { }
public class ModuleEngines : PartModule { public readonly List<Propellant> propellants = new List<Propellant>(); }
public class ModuleRCS : PartModule
{
    public string resourceName;
    public readonly List<Propellant> propellants = new List<Propellant>();
}
public sealed class Propellant { public string name; public int id; }
public class ModuleEnginesFX : ModuleEngines { }
public sealed class PartResourceDefinition { public string name; public int id; public ResourceFlowMode resourceFlowMode = ResourceFlowMode.ALL_VESSEL; }
public sealed class PartResourceLibrary { public static PartResourceLibrary Instance = new PartResourceLibrary(); public readonly System.Collections.Generic.Dictionary<string,PartResourceDefinition> Definitions=new System.Collections.Generic.Dictionary<string,PartResourceDefinition>(); public PartResourceDefinition GetDefinition(string name) => Definitions.TryGetValue(name,out var d)?d:null; }
public sealed class ModuleResourceIntake : PartModule { public string resourceName; }
public sealed class ProtoVessel
{
    public Guid vesselID;
    public bool landed, splashed;
    public OrbitSnapshot orbitSnapShot = new OrbitSnapshot();
    public readonly List<ProtoPartSnapshot> protoPartSnapshots = new List<ProtoPartSnapshot>();
}
public sealed class OrbitSnapshot { public int ReferenceBodyIndex; }
public sealed class ProtoPartSnapshot
{
    public uint persistentId;
    public string partName;
    public AvailablePart partInfo;
    public readonly List<ProtoPartModuleSnapshot> modules = new List<ProtoPartModuleSnapshot>();
    public readonly List<ProtoPartResourceSnapshot> resources = new List<ProtoPartResourceSnapshot>();
}
public sealed class ProtoPartModuleSnapshot { public string moduleName; public ConfigNode moduleValues = new ConfigNode(); }
public sealed class ProtoPartResourceSnapshot { public string resourceName; public double amount, maxAmount; }
public static class FlightGlobals { public static Vessel ActiveVessel; public static readonly List<CelestialBody> Bodies = new List<CelestialBody>(); }
public static class HighLogic { public static GameScenes LoadedScene = GameScenes.FLIGHT; public static string SaveFolder = "TestSave"; public static bool LoadedSceneIsFlight = true; public static Game CurrentGame = new Game(); }
public static class AssemblyLoader
{
    public static Type GetClassByName(Type parent, string name) => typeof(AssemblyLoader).Assembly.GetTypes().Concat(AppDomain.CurrentDomain.GetAssemblies().Where(a => a.IsDynamic).SelectMany(a => a.GetTypes()))
        .FirstOrDefault(t => parent.IsAssignableFrom(t) && t.Name == name);
}
public sealed class ConfigNode
{
    public sealed class Value { public string name, value; }
    public string name;
    public readonly List<Value> values = new List<Value>();
    public readonly List<ConfigNode> nodes = new List<ConfigNode>();
    public string[] GetValues(string key) => values.Where(v => v.name == key).Select(v => v.value).ToArray();
    public void RemoveValues(string key) { values.RemoveAll(v => v.name == key); }
    public bool SetValue(string key, string value, bool create) { var old = values.FirstOrDefault(v => v.name == key); if (old != null) old.value = value; else if (create) AddValue(key, value); else return false; return true; }
    public string GetValue(string key) => values.FirstOrDefault(v => v.name == key)?.value;
    public ConfigNode[] GetNodes(string key) => nodes.Where(n => n.name == key).ToArray();
    public void AddNode(ConfigNode node) { nodes.Add(node); }
    public ConfigNode GetNode(string key) => nodes.FirstOrDefault(n => n.name == key);
    public void RemoveNodes(string key) { nodes.RemoveAll(n => n.name == key); }
    public ConfigNode AddNode(string key) { var node = new ConfigNode { name = key }; nodes.Add(node); return node; }
    public void AddValue(string key, string value) { values.Add(new Value { name = key, value = value }); }
}
namespace OreMarket
{
    internal static class Log
    {
        internal static readonly List<string> Lines = new List<string>();
        internal static void Info(string message) { Lines.Add(message); }
        internal static void Error(string message) { Lines.Add(message); }
    }
}

// No TweakScale name check exists in production. This double exhibits the same
// observable amount/capacity writes and is accepted based on behavior, not identity.
public sealed class ObservableScaler : PartModule
{
    public void Resize(double multiplier)
    {
        PartResource resource = part.Resources.Get("Ore");
        resource.maxAmount *= multiplier;
        resource.amount *= multiplier;
    }
}
public sealed class UnsafeTransfer : PartModule
{
    public void Move()
    {
        part.Resources.Get("Ore").maxAmount = 100;
        part.RequestResource("Ore", -10);
    }
}
public sealed class HarmlessPartModule : PartModule
{
    public string ReadLabel() => "information only";
}

public sealed class KSPAddon : Attribute
{
    public enum Startup { Flight, Instantly }
    public KSPAddon(Startup scene, bool once) { }
}
namespace UnityEngine
{
    public sealed class GameObject { public bool activeInHierarchy = true; }
    public class MonoBehaviour { protected static void Destroy(object value) { } }
    public static class Application { public static bool isFocused = true; }
    public static class Time { public static float realtimeSinceStartup; }
}
public static class Planetarium
{
    public static double UniversalTime;
    public static bool Pause;
    public static double GetUniversalTime() => UniversalTime;
}
public static class TimeWarp { public static float fixedDeltaTime = 0.02f; }
public sealed class EventData<T>
{
    private event Action<T> callbacks;
    public void Add(Action<T> handler) { callbacks += handler; }
    public void Remove(Action<T> handler) { callbacks -= handler; }
    public void Fire(T arg) { callbacks?.Invoke(arg); }
}
public sealed class EventData<T, U, V>
{
    private event Action<T, U, V> callbacks;
    public void Add(Action<T, U, V> handler) { callbacks += handler; }
    public void Remove(Action<T, U, V> handler) { callbacks -= handler; }
    public void Fire(T first, U second, V third) { callbacks?.Invoke(first, second, third); }
}
public static class GameEvents
{
    public static readonly EventVoid onGamePause=new EventVoid(), onGameUnpause=new EventVoid();
    public static readonly EventData<GameScenes> onGameSceneLoadRequested = new EventData<GameScenes>();
    public sealed class FromToAction<T, U> { public T from; public U to; }
    public static readonly EventData<ProtoVessel, KSP.UI.Screens.MissionRecoveryDialog, float> onVesselRecoveryProcessingComplete = new EventData<ProtoVessel, KSP.UI.Screens.MissionRecoveryDialog, float>();
    public static readonly EventData<PartModule, string, double> OnResourceConverterOutput = new EventData<PartModule, string, double>();
    public static readonly EventData<FromToAction<Part, Part>> onPartCouple = new EventData<FromToAction<Part, Part>>();
    public static readonly EventData<Part> onPartDeCouple = new EventData<Part>();
    public static readonly EventData<Vessel> onVesselWasModified = new EventData<Vessel>();
    public static readonly EventData<Part> onPartResourceListChange = new EventData<Part>();
    public static readonly EventData<Vessel> onVesselGoOffRails = new EventData<Vessel>();
    public static readonly EventData<Vessel> onVesselGoOnRails = new EventData<Vessel>();
    public static readonly EventData<Vessel> onVesselUnloaded = new EventData<Vessel>();
}
public sealed class PowerConsumer : PartModule
{
    public void ProcessPower(double amount)
    {
        part.RequestResource("ElectricCharge", amount);
        part.RequestResource("ElectricCharge", -amount);
    }
}
public sealed class OreConsumer : PartModule
{
    public void ProcessOre(double amount) { part.RequestResource("Ore", amount); }
}
public sealed class UnknownConsumer : PartModule
{
    public void ProcessResource(string name, double amount) { part.RequestResource(name, amount); }
}
public sealed class MixedConsumer : PartModule
{
    public void ProcessResource(double amount)
    {
        part.RequestResource("ElectricCharge", amount);
        part.RequestResource("Ore", amount);
    }
}
public sealed class ConditionalConsumer : PartModule
{
    public void ProcessResource(bool choice, double amount)
    {
        part.RequestResource(choice ? "ElectricCharge" : "Ore", amount);
    }
}
public sealed class ComputedAmountConsumer : PartModule
{
    public void ProcessPower(double amount) { part.RequestResource("ElectricCharge", Math.Max(0, amount) * 0.5); }
}


public sealed class ScreenMessage { public string message; public float startTime; }
public sealed class ScreenMessages
{
    public static ScreenMessages Instance = new ScreenMessages();
    public static ScreenMessage PostScreenMessage(string message, float duration, ScreenMessageStyle style) { var m = new ScreenMessage { message = message }; Instance.ActiveMessages.Add(m); return m; }
    public readonly List<ScreenMessage> ActiveMessages = new List<ScreenMessage>();
}

public enum ScreenMessageStyle { UPPER_CENTER }
public sealed class Game { public enum Modes { CAREER, SANDBOX, SCIENCE_SANDBOX } public int Seed = 123; public Modes Mode = Modes.CAREER; }
[Flags] public enum ScenarioCreationOptions { AddToNewCareerGames = 1, AddToExistingCareerGames = 64, AddToAllGames = 126 }
public enum GameScenes { FLIGHT, SPACECENTER, TRACKSTATION, EDITOR, MAINMENU, LOADING }
public static class PauseMenu { public static bool exists = true, isOpen; }
public static class FlightDriver { public static bool Pause; }
public sealed class KSPScenario : Attribute { public KSPScenario(ScenarioCreationOptions options, params GameScenes[] scenes) { } }
public class ScenarioModule
{
    public virtual void OnAwake() { }
    public virtual void OnLoad(ConfigNode node) { }
    public virtual void OnSave(ConfigNode node) { }
}
namespace UnityEngine { public class Object { public static void Destroy(object value) { } } }
namespace KSP.UI.Screens { public sealed class MissionRecoveryDialog { public double fundsEarned, totalFunds; } }
public enum TransactionReasons { VesselRecovery }
// Offline currency double: it deliberately does not implement KSP strategies or recovery.
public sealed class Funding
{
    public static Funding Instance;
    public double Funds;
    public int Calls;
    public double LastAmount;
    public Action DuringAdd;
    public bool ThrowAfterAdd;
    public void AddFunds(double amount, TransactionReasons reason)
    {
        Funds += amount; Calls++; LastAmount = amount;
        DuringAdd?.Invoke();
        if (ThrowAfterAdd) throw new InvalidOperationException("Simulated uncertain currency callback");
    }
}

namespace KSP.UI.Screens
{
    // Stock inbox double: real persistence/display/queuing is checked in local KSP IL, not simulated here.
    public sealed class MessageSystemButton
    {
        public enum MessageButtonColor { GREEN, ORANGE }
        public enum ButtonIcons { COMPLETE, ALERT }
    }
    public sealed class MessageSystem
    {
        public static MessageSystem Instance;
        public readonly List<Message> Messages = new List<Message>();
        public bool ThrowOnAdd;
        public void AddMessage(Message message, bool animate)
        {
            if (ThrowOnAdd) throw new InvalidOperationException("Simulated inbox failure");
            Messages.Add(message);
        }
        public sealed class Message
        {
            public readonly string messageTitle, message;
            public Message(string title, string text, MessageSystemButton.MessageButtonColor color, MessageSystemButton.ButtonIcons icon)
            { messageTitle = title; message = text; }
        }
    }
}




public sealed class EventVoid
{
    private event Action handlers;
    public void Add(Action h) { handlers+=h; }
    public void Remove(Action h) { handlers-=h; }
    public void Fire() { handlers?.Invoke(); }
}

public enum UntrackedObjectClass {A,B,C,D,E,F,G,H,I}
public static class DiscoveryInfo {public static string GetSizeClassSizes(UntrackedObjectClass size) {string[] names={"Tiny","Small","Medium","Large","Huge","Huge","Massive","Gargantuan","Gargantuan"};return size+" ("+names[(int)size]+")";}}

public class ModuleAsteroid : PartModule { public string prefabBaseURL; public int currentState = 1; }
public class ModuleComet : PartModule { public string prefabBaseURL; public int currentState = 1; }
public class ModuleSpaceObjectInfo : PartModule { public double currentMassVal=100, massThresholdVal=10; }
public class ModuleAsteroidInfo : ModuleSpaceObjectInfo { }
public class ModuleCometInfo : ModuleSpaceObjectInfo { }
public class ModuleSpaceObjectResource : PartModule { public string resourceName="Ore"; public float abundance=.9f; }
public class ModuleAsteroidResource : ModuleSpaceObjectResource { }
public class ModuleCometResource : ModuleSpaceObjectResource { }

public class UIPartActionResourceItem { public PartResource resource; }
public sealed class UIPartActionResourceTransfer : UIPartActionResourceItem
{
    public int State;
    public UnityEngine.GameObject gameObject = new UnityEngine.GameObject();
    public readonly List<UIPartActionResourceTransfer> Targets = new List<UIPartActionResourceTransfer>();
    public void FlowStop() { State=0; }
    public Action UpdateOperation;
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public void UpdateItem() { UpdateOperation?.Invoke(); }
}

public enum ResourceFlowMode { NO_FLOW,ALL_VESSEL,STAGE_PRIORITY_FLOW,STACK_PRIORITY_SEARCH,ALL_VESSEL_BALANCE,STAGE_PRIORITY_FLOW_BALANCE,STAGE_STACK_FLOW,STAGE_STACK_FLOW_BALANCE,NULL }
public struct ConverterResults { public string Status; public double TimeFactor; }
public sealed class PartSet
{
    public sealed class ResourcePrioritySet {
        public List<List<PartResource>> lists = new List<List<PartResource>>();
        public HashSet<PartResource> set = new HashSet<PartResource>();
    }
    public ResourcePrioritySet Cached = new ResourcePrioritySet();
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private ResourcePrioritySet GetOrCreateList(int id,bool pulling,bool simulate) => Cached;
    public ResourcePrioritySet GetList(int id,bool pulling=false) => GetOrCreateList(id,pulling,false);
}
public sealed class ConversionRecipe { public Action Execute; public List<ResourceRatio> Outputs = new List<ResourceRatio>(); }
public sealed class ResourceConverter
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public ConverterResults ProcessRecipe(double time,ConversionRecipe recipe,Part part,PartModule source,float efficiency) { recipe.Execute(); return new ConverterResults {TimeFactor=1}; }
}

public sealed class AttachNode { public Part attachedPart,owner; }
public sealed class ModuleGrappleNode : PartModule {
 public Part otherPart; public AttachNode grappleNode=new AttachNode();public Action ReleaseOperation;
 [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] public void Release(){ReleaseOperation?.Invoke();}
 [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] public void Decouple(){ReleaseOperation?.Invoke();}
}

public sealed class DockedVesselInfo { }

public sealed class GameDatabase {
 public static GameDatabase Instance;
 public readonly Dictionary<string,UnityEngine.Texture2D> Textures=new Dictionary<string,UnityEngine.Texture2D>();
 public int Calls;public bool Fail;
 public UnityEngine.Texture2D GetTexture(string name,bool normal){Calls++;if(Fail)throw new InvalidOperationException();return Textures.TryGetValue(name,out var t)?t:null;}
}
namespace UnityEngine {
 public sealed class Texture2D { }
 public static class Debug {public static void LogWarning(object message) { }}
}
