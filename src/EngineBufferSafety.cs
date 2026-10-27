using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
namespace OreMarket
{
    // Optional read-only scope proof for the installed BackgroundThrust build.
    // Type-level dynamic resource operations remain unsupported by default.
    internal static class EngineBufferSafety
    {
        private static readonly HashSet<string> Reported = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> ConfigFields = new HashSet<string>(new[] { "name", "IsEnabled", "AllowBackgroundProcessing", "isEnabled" }, StringComparer.Ordinal);
        private static readonly HashSet<string> SavedFields = new HashSet<string>(new[] { "name", "IsEnabled", "AllowBackgroundProcessing", "isEnabled", "stagingEnabled" }, StringComparer.Ordinal);
        internal static bool KnownType(Type type) => type != null && type.FullName == "BackgroundThrust.BackgroundEngine" &&
            type.Assembly.GetName().Name == "BackgroundThrust" && type.Assembly.GetName().Version == new Version(0,6,12,0);
        private static object Read(object o, string name) => o?.GetType().GetField(name, BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(o);
        private static bool Fields(ConfigNode n, HashSet<string> allowed) => n.values.Cast<ConfigNode.Value>().All(v => allowed.Contains(v.name));
        private static bool Resource(string name)
        {
            if (string.IsNullOrEmpty(name) || name == "Ore") return false;
            var library = PartResourceLibrary.Instance;
            var definition = library?.GetDefinition(name);
            var ore = library?.GetDefinition("Ore");
            return definition != null && ore != null && definition.name == name && definition.id != ore.id;
        }
        private static bool PropellantSafe(Propellant p) => p != null && Resource(p.name) && PartResourceLibrary.Instance.GetDefinition(p.name).id == p.id;
        internal static bool ProvesNonOre(PartModule module)
        {
            try
            {
                if (module == null || !KnownType(module.GetType()) || module.part?.partInfo?.partConfig == null) return false;
                var part = module.part;
                var nodes = part.partInfo.partConfig.GetNodes("MODULE").Where(n => n.GetValue("name") == "BackgroundEngine").ToArray();
                if (nodes.Length != 1 || !Fields(nodes[0], ConfigFields) || nodes[0].nodes.Count != 0 ||
                    part.Modules.Cast<PartModule>().Count(m => m.GetType() == module.GetType()) != 1) return false;
                // Check all modes; an inactive Ore mode cannot hide behind an active
                // non-Ore engine. Prefabs need no initialized cached Engine reference.
                var engines = part.Modules.Cast<PartModule>().OfType<ModuleEngines>().ToArray();
                if (engines.Length == 0 || engines.Any(e => e.part != part ||
                    (e.GetType() != typeof(ModuleEngines) && e.GetType() != typeof(ModuleEnginesFX)) ||
                    e.propellants.Count == 0 || e.propellants.Any(p => !PropellantSafe(p)))) return false;
                object selected = Read(module,"<Engine>k__BackingField");
                if (selected != null && !engines.Contains(selected as ModuleEngines)) return false;
                if (!(Read(module,"buffers") is IDictionary buffers)) return false;
                foreach (DictionaryEntry entry in buffers)
                {
                    string name = entry.Key as string;
                    if (!Resource(name) || entry.Value == null || !(Read(entry.Value,"OriginalMaxAmount") is double capacity) || !TrackingState.ValidAmount(capacity)) return false;
                    object propellant = Read(entry.Value,"Propellant");
                    if (propellant != null && (!(propellant is Propellant p) || !PropellantSafe(p) || p.name != name)) return false;
                    object stored = Read(entry.Value,"Resource");
                    PartResource local = part.Resources.Get(name);
                    if (local != null && local.resourceName != name) return false;
                    if (stored != null && (!(stored is PartResource resource) || resource.resourceName != name || !ReferenceEquals(resource, local))) return false;
                }
                return true;
            }
            catch (Exception) { return false; }
        }
        internal static bool RecoveredSafe(Type type, Part prefab, ConfigNode snapshot)
        {
            try
            {
                if (!KnownType(type) || prefab == null || snapshot == null || !Fields(snapshot,SavedFields)) return false;
                var modules = prefab.Modules.Cast<PartModule>().Where(m => m.GetType() == type).ToArray();
                if (modules.Length != 1 || !ProvesNonOre(modules[0])) return false;
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (ConfigNode child in snapshot.nodes)
                {
                    if (child.name == "BUFFER")
                    {
                        string[] ids = child.GetValues("ResourceName"), caps = child.GetValues("OriginalMaxAmount");
                        if (child.nodes.Count != 0 || child.values.Count != 2 || ids.Length != 1 || caps.Length != 1 || !Resource(ids[0]) || !names.Add(ids[0]) ||
                            !double.TryParse(caps[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double cap) || !TrackingState.ValidAmount(cap)) return false;
                    }
                    else if ((child.name != "EVENTS" && child.name != "ACTIONS" && child.name != "UPGRADESAPPLIED") || child.values.Count != 0 || child.nodes.Count != 0) return false;
                }
                return true;
            }
            catch (Exception) { return false; }
        }
        internal static void Report(PartModule module, bool accepted, string operation)
        {
            try {
            if (module?.part == null || Reported.Count >= 256) return;
            string key = module.vessel?.id + ":" + module.part.persistentId + ":" + module.GetType().FullName + ":" + accepted + ":" + operation;
            if (!Reported.Add(key)) return;
            string scope = "unproven";
            if (KnownType(module.GetType()) && Read(module,"buffers") is IDictionary buffers) scope = string.Join(",",module.part.Modules.Cast<PartModule>().OfType<ModuleEngines>().SelectMany(e=>e.propellants).Select(p=>p.name).Concat(buffers.Keys.Cast<string>()).Distinct(StringComparer.Ordinal));
            Log.Info("Module resource validation: accepted=" + accepted + " part=" + module.part.partInfo?.name + " id=" + module.part.persistentId +
                " module=" + module.GetType().FullName + " resources=" + scope + " operation=" + operation);
            } catch (Exception) { } // Diagnostics must never change validation.
        }
    }
}
