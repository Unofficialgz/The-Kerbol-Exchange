using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
namespace OreMarket
{
    // An optional behavior profile for the installed, inspected B9 build, not a
    // module-name exemption. The generic type assessment stays conservative.
    internal static class CosmeticSwitchSafety
    {
        private static readonly HashSet<string> ModuleFields = new HashSet<string>(new[] {
            "name", "moduleID", "switcherDescription", "switcherDescriptionPlural", "affectDragCubes",
            "affectFARVoxels", "switchInFlight", "advancedTweakablesOnly", "uiGroupName", "uiGroupDisplayName" }, StringComparer.Ordinal);
        private static readonly HashSet<string> SubtypeFields = new HashSet<string>(new[] {
            "name", "title", "descriptionSummary", "descriptionDetail", "transform", "primaryColor",
            "secondaryColor", "defaultSubtypePriority" }, StringComparer.Ordinal);
        private static readonly HashSet<string> SnapshotFields = new HashSet<string>(new[] {
            "name", "moduleID", "isEnabled", "stagingEnabled", "currentSubtype" }, StringComparer.Ordinal);
        internal static bool KnownType(Type type) => type != null && type.FullName == "B9PartSwitch.ModuleB9PartSwitch" &&
            type.Assembly.GetName().Name == "B9PartSwitch" && type.Assembly.GetName().Version == new Version(2,21,0,4);
        private static object Read(object owner, string field) => owner?.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(owner);
        private static bool EmptyList(object owner, string field) => Read(owner, field) is IList list && list.Count == 0;
        private static bool FieldsAllowed(ConfigNode node, HashSet<string> fields) => node.values.Cast<ConfigNode.Value>().All(v => fields.Contains(v.name));
        internal static bool ProvesNoResourceSwitch(PartModule module)
        {
            try
            {
                if (module == null || !KnownType(module.GetType()) || module.part?.partInfo?.partConfig == null) return false;
                string id = Read(module, "moduleID") as string;
                if (string.IsNullOrEmpty(id)) return false;
                var matches = module.part.partInfo.partConfig.GetNodes("MODULE").Where(n =>
                    n.GetValue("name") == "ModuleB9PartSwitch" && n.GetValue("moduleID") == id).ToArray();
                if (matches.Length != 1) return false;
                ConfigNode config = matches[0];
                if (!FieldsAllowed(config, ModuleFields) || config.GetValues("moduleID").Length != 1 || config.GetValues("name").Length != 1 ||
                    config.nodes.Cast<ConfigNode>().Any(n => n.name != "SUBTYPE")) return false;
                var configured = config.GetNodes("SUBTYPE");
                if (configured.Length == 0 || configured.Any(n => !FieldsAllowed(n, SubtypeFields) ||
                    n.nodes.Count != 0 || n.GetValues("name").Length != 1 || string.IsNullOrEmpty(n.GetValue("name"))) ||
                    configured.Select(n => n.GetValue("name")).Distinct(StringComparer.Ordinal).Count() != configured.Length) return false;
                // Inspect ALL subtypes, not just the selected appearance. Unknown,
                // uninitialized, resource-capable, parent/child or MODULE switching
                // configurations do not receive this profile.
                if (!(Read(module,"subtypes") is IList subtypes) || subtypes.Count != configured.Length ||
                    !EmptyList(module,"children") || !string.IsNullOrEmpty(Read(module,"parentID") as string)) return false;
                for (int i=0; i<subtypes.Count; i++)
                {
                    object subtype = subtypes[i];
                    object tank = Read(subtype,"tankType");
                    if ((Read(subtype,"subtypeName") as string) != configured[i].GetValue("name") ||
                        (Read(tank,"tankName") as string) != "Structural" || Convert.ToDouble(Read(tank,"tankMass")) != 0 ||
                        Convert.ToDouble(Read(tank,"tankCost")) != 0 || !EmptyList(tank,"resources") || !EmptyList(subtype,"moduleModifierInfos")) return false;
                }
                return true;
            }
            catch (Exception) { return false; }
        }
        internal static bool RecoveredSafe(Type type, Part prefab, ConfigNode snapshot)
        {
            try
            {
                if (!KnownType(type) || prefab == null || snapshot == null || !FieldsAllowed(snapshot, SnapshotFields) ||
                    snapshot.GetValues("moduleID").Length != 1 || snapshot.nodes.Cast<ConfigNode>().Any(n =>
                        (n.name != "EVENTS" && n.name != "ACTIONS" && n.name != "UPGRADESAPPLIED") || n.values.Count != 0 || n.nodes.Count != 0)) return false;
                string id = snapshot.GetValue("moduleID");
                var modules = prefab.Modules.Cast<PartModule>().Where(m => m.GetType() == type && (Read(m,"moduleID") as string) == id).ToArray();
                if (modules.Length != 1 || !ProvesNoResourceSwitch(modules[0])) return false;
                string selected = snapshot.GetValue("currentSubtype");
                var subtypes = Read(modules[0],"subtypes") as IList;
                return snapshot.GetValues("currentSubtype").Length == 1 && subtypes != null &&
                    subtypes.Cast<object>().Any(s => (Read(s,"subtypeName") as string) == selected);
            }
            catch (Exception) { return false; }
        }
    }
}
