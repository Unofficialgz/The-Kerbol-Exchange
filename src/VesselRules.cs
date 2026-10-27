using System;
using System.Collections.Generic;
using System.Linq;

namespace OreMarket
{
    internal static class VesselRules
    {
        internal static bool StockTank(string name) => name == "SmallTank" || name == "LargeTank" || name == "RadialOreTank";
        internal static bool StockDrill(PartModule module) => module != null && module.GetType() == typeof(ModuleResourceHarvester) &&
            module.part != null && module.part.partInfo != null &&
            (module.part.partInfo.name == "MiniDrill" || module.part.partInfo.name == "RadialDrill");

        internal static string Manifest(IEnumerable<Part> parts) => string.Join(";", parts.Select(p => p.persistentId + ":" + p.partInfo.name).OrderBy(s => s, StringComparer.Ordinal));
        internal static string Manifest(IEnumerable<ProtoPartSnapshot> parts) => string.Join(";", parts.Select(p => p.persistentId + ":" + p.partName).OrderBy(s => s, StringComparer.Ordinal));

        private static bool ModuleAllowed(PartModule module, out string reason)
        {
            reason = "";
            if (module is OreMarketTank) return true;
            bool engineProfile = EngineBufferSafety.KnownType(module.GetType()) && EngineBufferSafety.ProvesNonOre(module);
            if (module.GetType().Assembly.GetName().Name != "Assembly-CSharp" && !ModuleSafetyInspector.CanTrack(module.GetType(), out reason) && !CosmeticSwitchSafety.ProvesNoResourceSwitch(module) && !engineProfile)
            { EngineBufferSafety.Report(module, false, reason); reason = "Unsupported module operation in " + module.moduleName + ": " + reason; return false; }
            if (engineProfile) EngineBufferSafety.Report(module, true, "non-Ore engine/propellant/buffer scope proven");
            // Reject unsupported producers even when inactive. Consumers only debit the balance.
            ConfigNode config = module.part.partInfo.partConfig;
            if (config != null)
            {
                foreach (ConfigNode node in config.GetNodes("MODULE"))
                    if (node.GetValue("name") == module.moduleName && ConfiguresOreOperation(node) &&
                        !(module is ModuleResourceHarvester) && !(module is ModuleAsteroidDrill) &&
                        !(module is ModuleCometDrill) && !(module is ModuleFuelJettison) && !(module is BaseConverter))
                    { reason = "Unsupported Ore module: " + module.moduleName; return false; }
            }
            if ((StockDrill(module) || ObjectMining.IsObjectDrill(module)) && module is BaseConverter stockProducer && stockProducer.inputList.Any(r => r.ResourceName == "Ore"))
            { reason = "Unsupported Ore-consuming drill recipe"; return false; }
            if (module is BaseConverter converter && !(module is ModuleResourceHarvester) && !ObjectMining.IsObjectDrill(module) &&
                converter.outputList.Any(r => r.ResourceName == "Ore"))
            { reason = "Unsupported Ore-producing converter present (even if disabled)"; return false; }
            if (module is ModuleResourceHarvester harvester && harvester.ResourceName == "Ore" &&
                (!StockDrill(module) || harvester.HarvesterType != 0))
            { reason = "Unsupported Ore harvester"; return false; }
            if (module is ModuleEngines engine && engine.propellants.Any(p => p.name == "Ore") ||
                module is ModuleRCS rcs && (rcs.resourceName == "Ore" || rcs.propellants.Any(p => p.name == "Ore")) ||
                module is ModuleResourceIntake intake && intake.resourceName == "Ore")
            { reason = "Unsupported Ore propellant/intake"; return false; }
            return true;
        }

        private static bool ConfiguresOreOperation(ConfigNode node)
        {
            // An informational resourceName/label is not an operation. Explicit
            // resource recipes still require supported stock handling.
            if ((node.name == "INPUT_RESOURCE" || node.name == "OUTPUT_RESOURCE" || node.name == "RESOURCE") && MentionsOre(node)) return true;
            return node.nodes.Cast<ConfigNode>().Any(ConfiguresOreOperation);
        }
        private static bool MentionsOre(ConfigNode node)
        {
            foreach (ConfigNode.Value value in node.values) if (value.value == "Ore") return true;
            foreach (ConfigNode child in node.nodes) if (MentionsOre(child)) return true;
            return false;
        }

        internal static bool Loaded(Vessel vessel, OreMarketTank tracker, out string reason, bool requireLoaded = true)
        {
            reason = "Missing vessel or tracker";
            if (vessel == null || tracker == null || (requireLoaded && !vessel.loaded)) return false;
            int tanks = 0; var identities = new HashSet<uint>(); var batches = new HashSet<string>(StringComparer.Ordinal);
            foreach (Part part in vessel.parts)
            {
                if (part.partInfo == null || part.persistentId == 0 || !identities.Add(part.persistentId)) { reason = "Missing part identity"; return false; }
                if (part.Resources.Get("Ore") != null) {
                    tanks++;
                    if (!StockTank(part.partInfo.name) || part.Resources.Cast<PartResource>().Count(r => r.resourceName == "Ore") != 1 || part.Modules.Cast<PartModule>().OfType<OreMarketTank>().Count() != 1)
                    { reason = "Each Ore storage part needs one stock tank tracker"; return false; }
                    var state=part.Modules.Cast<PartModule>().OfType<OreMarketTank>().Single().State;
                    if(state!=null&&!state.BindTank(part.persistentId)) {reason="Tank identity mismatch";return false;}
                    if(state!=null && state.BatchId.Length>0 && (!Guid.TryParse(state.BatchId,out var batch) || !batches.Add(batch.ToString("N"))))
                    { reason="Duplicate provenance batch across tanks"; return false; }
                }
                foreach (PartModule module in part.Modules) if (!ModuleAllowed(module, out reason)) return false;
            }
            if (tanks < 1 || !StockTank(tracker.part.partInfo.name)) { reason = "Stock Ore storage required"; return false; }
            if (SafeObjectRelease.Holds(tracker) || StockCargoLogistics.Holds(tracker)) {reason="";return true;}
            if (!tracker.State.Invalid && tracker.State.BatchId.Length > 0 && (tracker.State.VesselId != vessel.id.ToString("N") || tracker.State.Manifest != Manifest(vessel.parts)))
            { reason = "Vessel identity or parts changed after mining"; return false; }
            reason = ""; return true;
        }

        internal static bool Recovered(ProtoVessel vessel, out ProtoPartSnapshot tank, out string reason)
        {
            bool valid = RecoveredTanks(vessel, out var tanks, out reason);
            tank = valid ? tanks[0] : null; return valid;
        }
        internal static bool RecoveredTanks(ProtoVessel vessel, out List<ProtoPartSnapshot> tanks, out string reason)
        {
            tanks = new List<ProtoPartSnapshot>(); reason = "Missing recovered vessel snapshot";
            if (vessel == null || vessel.protoPartSnapshots == null || vessel.orbitSnapShot == null) return false;
            int body = vessel.orbitSnapShot.ReferenceBodyIndex;
            CelestialBody recoveredBody = FlightGlobals.Bodies.FirstOrDefault(b => b.flightGlobalsIndex == body);
            if (recoveredBody == null || recoveredBody.bodyName != "Kerbin" ||
                (!vessel.landed && !vessel.splashed)) { reason = "Recovery is not on Kerbin's surface"; return false; }
            var identities = new HashSet<uint>();
            foreach (ProtoPartSnapshot part in vessel.protoPartSnapshots)
            {
                if (part.partInfo == null || part.partInfo.partPrefab == null || part.persistentId == 0 || !identities.Add(part.persistentId))
                { reason = "Missing recovered part identity/prefab"; return false; }
                foreach (PartModule module in part.partInfo.partPrefab.Modules)
                    if (!ModuleAllowed(module, out reason)) return false;
                foreach (ProtoPartModuleSnapshot module in part.modules)
                {
                    Type type = AssemblyLoader.GetClassByName(typeof(PartModule), module.moduleName);
                    if (type == null || (type != typeof(OreMarketTank) && type.Assembly.GetName().Name != "Assembly-CSharp" && !ModuleSafetyInspector.CanTrack(type, out reason) && !CosmeticSwitchSafety.RecoveredSafe(type, part.partInfo.partPrefab, module.moduleValues) && !EngineBufferSafety.RecoveredSafe(type, part.partInfo.partPrefab, module.moduleValues)))
                    { reason = "Unsupported recovered module operation in " + module.moduleName + ": " + reason; return false; }
                }
                foreach (ProtoPartResourceSnapshot resource in part.resources)
                {
                    if (resource.resourceName != "Ore") continue;
                    if (!StockTank(part.partName) || tanks.Contains(part)) { reason = "Non-stock or duplicate Ore resource recovered"; return false; }
                    tanks.Add(part);
                }
            }
            if (tanks.Count == 0) { reason = "No physical Ore tank recovered"; return false; }
            reason = ""; return true;
        }
    }
}







