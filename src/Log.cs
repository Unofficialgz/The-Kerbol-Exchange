namespace OreMarket
{
    internal static class Log
    {
        internal static void Info(string message) => UnityEngine.Debug.Log("[OreMarket] " + message);
        internal static void Error(string message) => UnityEngine.Debug.LogError("[OreMarket] " + message);
    }
}
