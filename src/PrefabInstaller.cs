using UnityEngine;
namespace OreMarket
{
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public sealed class PrefabInstaller : MonoBehaviour
    {
        public void Start()
        {
            OrePricing.Load(GameDatabase.Instance == null ? null : GameDatabase.Instance.GetConfigNodes("OREMARKET_PRICES"));
            int added = 0;
            foreach (AvailablePart available in PartLoader.LoadedPartsList)
            {
                Part prefab = available.partPrefab;
                if (prefab == null || !VesselRules.StockTank(available.name) || prefab.Resources.Get("Ore") == null) continue;
                if (prefab.Modules.Contains(nameof(OreMarketTank))) continue;
                if (prefab.AddModule(nameof(OreMarketTank), true) == null)
                    Log.Error("Could not attach tracker to " + available.name);
                else added++;
            }
            Log.Info("Attached tracker to " + added + " stock Ore tank prefabs; prices read from OREMARKET_PRICES.");
        }
    }
}



