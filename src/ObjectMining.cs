using System;
using System.Linq;
using System.Reflection;
namespace OreMarket
{
    // Read-only adapter for the exact installed stock recipe implementation. Never
    // invoke GetAttachedPotato, PrepareRecipe, CheckForImpact, or stock lifecycle methods.
    internal static class ObjectMining
    {
        private static readonly FieldInfo AsteroidSource = typeof(ModuleAsteroidDrill).GetField("_potato", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo CometSource = typeof(ModuleCometDrill).GetField("_potato", BindingFlags.NonPublic | BindingFlags.Instance);
        internal static bool IsObjectDrill(PartModule source) => source != null &&
            (source.GetType() == typeof(ModuleAsteroidDrill) || source.GetType() == typeof(ModuleCometDrill));
        internal static bool TryIdentity(bool comet, string prefabUrl, int variant, out string origin)
        {
            origin = null;
            string prefix = comet ? "Procedural/PC_" : "Procedural/PA_";
            // Stock states 1/2 select normal/secondary procedural generation. Neither
            // is a special price authorization; all exact stock sources share pricing.
            if ((variant != 1 && variant != 2) || prefabUrl == null || !prefabUrl.StartsWith(prefix, StringComparison.Ordinal) || prefabUrl.Length != prefix.Length + 1) return false;
            string letter = prefabUrl.Substring(prefix.Length);
            var d = ObjectMarkets.Find(ObjectMarkets.Prefix + (comet ? "Comet:" : "Asteroid:") + letter);
            if (d == null || d.Special) return false;
            origin = d.Identity; return true;
        }
        internal static bool TrySource(PartModule source, out string origin, out uint sourceId, out string reason)
        {
            origin = null; sourceId = 0; reason = "Unsupported or ambiguous stock object source";
            if (!IsObjectDrill(source) || source.part?.partInfo == null ||
                (source.part.partInfo.name != "MiniDrill" && source.part.partInfo.name != "RadialDrill") ||
                source.vessel == null || !source.vessel.loaded || !((BaseConverter)source).IsActivated) return false;
            bool comet = source.GetType() == typeof(ModuleCometDrill);
            var field = comet ? CometSource : AsteroidSource;
            Part rock = field?.GetValue(source) as Part;
            if (rock == null || rock.persistentId == 0 || rock.vessel != source.vessel || !source.vessel.parts.Contains(rock)) return false;
            var asteroids = rock.Modules.Cast<PartModule>().Where(m => m.GetType() == typeof(ModuleAsteroid)).Cast<ModuleAsteroid>().ToArray();
            var comets = rock.Modules.Cast<PartModule>().Where(m => m.GetType() == typeof(ModuleComet)).Cast<ModuleComet>().ToArray();
            if (comet ? comets.Length != 1 || asteroids.Length != 0 : asteroids.Length != 1 || comets.Length != 0) return false;
            if (rock.partInfo?.name != (comet ? "PotatoComet" : "PotatoRoid")) return false;
            var infos = rock.Modules.Cast<PartModule>().Where(m => m.GetType() == (comet ? typeof(ModuleCometInfo) : typeof(ModuleAsteroidInfo))).Cast<ModuleSpaceObjectInfo>().ToArray();
            var ores = rock.Modules.Cast<PartModule>().Where(m => m.GetType() == (comet ? typeof(ModuleCometResource) : typeof(ModuleAsteroidResource))).Cast<ModuleSpaceObjectResource>().Where(m => m.resourceName == "Ore").ToArray();
            if (infos.Length != 1 || ores.Length != 1 || !TrackingState.ValidAmount(infos[0].currentMassVal) ||
                !TrackingState.ValidAmount(infos[0].massThresholdVal) || infos[0].currentMassVal <= infos[0].massThresholdVal ||
                !TrackingState.ValidAmount(ores[0].abundance) || ores[0].abundance <= 0 || ores[0].abundance > 1) return false;
            if (!TryIdentity(comet, comet ? comets[0].prefabBaseURL : asteroids[0].prefabBaseURL,
                comet ? comets[0].currentState : asteroids[0].currentState, out origin))
            {
                string url = comet ? comets[0].prefabBaseURL : asteroids[0].prefabBaseURL;
                int variant = comet ? comets[0].currentState : asteroids[0].currentState;
                string prefix = comet ? "Procedural/PC_" : "Procedural/PA_";
                reason = variant != 1 && variant != 2 ? "Special/unknown object variant unsupported" :
                    url != null && url.StartsWith(prefix, StringComparison.Ordinal) && url.Length == prefix.Length+1 ?
                    "No " + (comet ? "comet" : "asteroid") + " Ore market for physical class " + url.Substring(prefix.Length) :
                    "Stock object class identity not proven";
                reason += " (drill=" + source.GetType().Name + " source=" + rock.persistentId + " url=" + url + " state=" + variant + ")";
                return false;
            }
            sourceId = rock.persistentId; reason = ""; return true;
        }
    }
    internal static class SaleQuotes
    {
        internal static bool HasMarket(TrackingState state) => state != null &&
            (ObjectMarkets.Reserved(state.Origin) ? state.HasObjectProof : OreOrigins.HasMarket(state.Origin));
        internal static bool TryGetUnitPrice(TrackingState state, out double price)
        {
            price = 0;
            if (!HasMarket(state)) return false;
            return ObjectMarkets.Reserved(state.Origin) ? MarketQuotes.TryGetUnitPrice(state.Origin, out price) : OrePricing.TryGetUnitPrice(state.Origin, out price);
        }
    }
}
