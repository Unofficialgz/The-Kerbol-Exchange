using System;
using System.Reflection;
namespace OreMarket.Terminal
{
    internal static class TerminalHelp
    {
        internal static readonly string DisplayVersion = VersionLabel(
            ((AssemblyFileVersionAttribute)Attribute.GetCustomAttribute(typeof(TerminalHelp).Assembly, typeof(AssemblyFileVersionAttribute)))?.Version);
        internal static string VersionLabel(string metadata)
        {
            return Version.TryParse(metadata, out Version version) && version.Build >= 0
                ? "V" + version.Major + "." + version.Minor + "." + version.Build : "V?";
        }
        internal static string WelcomeMark(bool supported) => supported ? "\u2122" : "TM";
        internal const string CurrentPrice="Funds earned per Ore unit when you recover on Kerbin in career mode.";
        internal const string BasePrice="The reference price set in Prices.cfg.";
        internal const string Update="Prices update every 60 seconds while you play. Pausing stops the timer; time warp does not speed it up.";
        internal const string Candles="Open, High, Low and Close for each period. Wicks show recorded highs and lows.";
        internal const string News="Events can affect prices. Sometimes quite a lot. A rise or fall is never guaranteed.";
        internal const string Aftermath="Event effects fade after they end. Prices do not reset.";
        internal const string Guide="1. Mine stock Ore on a planet, moon, asteroid or comet.\n\n2. Keep tracked Ore in compatible stock tanks.\n\n3. Transport your cargo back to Kerbin.\n\n4. Recover the vessel in career mode to sell at the current price.\n\n5. Monitor prices and events to plan your missions.\n\nUnverified or preloaded Ore does not qualify. Prices are not locked at launch or when mining.";
        internal static string EventTime(double seconds,bool aftermath) {
            if(double.IsNaN(seconds)||double.IsInfinity(seconds)||seconds<0)return "Time unavailable";
            string time=ChartHistory.Countdown(seconds);
            return time+(aftermath?" aftermath remaining":" active remaining");
        }
        internal static string Change(MarketPeriod[] periods)
        {
            return "Price change over the last hour of play, or since the first price if there is less history.";
        }
    }
}
