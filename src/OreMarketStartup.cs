using UnityEngine;

namespace OreMarket
{
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public sealed class OreMarketStartup : MonoBehaviour
    {
        private void Start()
        {
            Debug.Log("[OreMarket] The Kerbol Exchange loaded.");
            object[] versions = typeof(OreMarketStartup).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyFileVersionAttribute), false);
            if (versions.Length == 1) Debug.Log("[OreMarket] Core file revision " + ((System.Reflection.AssemblyFileVersionAttribute)versions[0]).Version);
            Destroy(this);
        }
    }
}
