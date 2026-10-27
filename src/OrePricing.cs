using System;
using System.Collections.Generic;
using System.Globalization;

namespace OreMarket
{
    public static class OrePricing
    {
        public const double DefaultUnitPrice = 100d;
        private sealed class Prices
        {
            internal readonly Dictionary<string, double> Bodies;
            internal readonly double Fallback;
            internal Prices(Dictionary<string, double> bodies, double fallback) { Bodies = bodies; Fallback = fallback; }
        }
        private static Prices prices = new Prices(new Dictionary<string, double>(StringComparer.Ordinal), 0);

        internal static bool ConfigurationValid {get;private set;}
        internal static MarketSettings Settings { get; private set; } = new MarketSettings();

        // Called after GameDatabase has finished loading. Build a complete snapshot
        // before publishing it; missing/ambiguous/invalid configuration fails closed.
        internal static bool Load(ConfigNode[] configs)
        {
            prices = new Prices(new Dictionary<string, double>(StringComparer.Ordinal), 0);ConfigurationValid=false;
            try
            {
                if (configs == null || configs.Length != 1) throw new FormatException("Exactly one OREMARKET_PRICES node required");
                ConfigNode config = configs[0];
                Settings = MarketSettings.Read(config.GetNodes("MARKET"));
                double fallback = ReadPrice(config, "fallbackPrice");
                var bodies = new Dictionary<string, double>(StringComparer.Ordinal);
                foreach (ConfigNode body in config.GetNodes("BODY"))
                {
                    string[] names = body.GetValues("name");
                    if (names.Length != 1 || !OreOrigins.Valid(names[0])) throw new FormatException("Invalid or duplicate body name field");
                    if (bodies.ContainsKey(names[0])) throw new FormatException("Duplicate price identity: " + names[0]);
                    bodies.Add(names[0], ReadPrice(body, "price"));
                }
                prices = new Prices(bodies, fallback);MarketQuotes.LoadObjects(config.GetNodes("OBJECT"));ConfigurationValid=true;
                Log.Info("base prices and market settings loaded: bodies=" + bodies.Count + " fallback=" + fallback.ToString("R", CultureInfo.InvariantCulture) + " Kerbin=0 (enforced)");
                return true;
            }
            catch (Exception e) when (e is FormatException || e is ArgumentException || e is NullReferenceException)
            {
                Log.Error("Planetary sales disabled: invalid/missing OREMARKET_PRICES configuration: " + e.Message);
                return false;
            }
        }

        private static double ReadPrice(ConfigNode node, string key)
        {
            string[] values = node.GetValues(key);
            if (values.Length != 1 || !double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double price) ||
                !TrackingState.ValidAmount(price)) throw new FormatException("Invalid/duplicate non-negative finite price: " + key);
            return price;
        }

        public static bool TryGetBasePrice(string origin, out double unitPrice)
        {
            unitPrice = 0;
            if (ObjectMarkets.Reserved(origin) || !OreOrigins.HasMarket(origin) || string.Equals(origin, "Kerbin", StringComparison.Ordinal)) return false;
            Prices snapshot = prices;
            if (!snapshot.Bodies.TryGetValue(origin, out unitPrice)) unitPrice = snapshot.Fallback;
            return unitPrice > 0;
        }

        public static bool TryGetUnitPrice(string origin, out double unitPrice)
        {
            unitPrice = 0;
            return TryGetBasePrice(origin, out double baseline) && MarketScenario.Price(origin, baseline, out unitPrice);
        }

        public static double Quote(string origin, double verified, double actual)
        {
            return TryGetUnitPrice(origin, out double price) ? Calculate(verified, actual, price) : 0;
        }

        public static double Calculate(double verified, double actual, double unitPrice)
        {
            if (!TrackingState.ValidAmount(verified) || !TrackingState.ValidAmount(actual) ||
                !TrackingState.ValidAmount(unitPrice) || unitPrice <= 0) return 0;
            double result = Math.Min(verified, actual) * unitPrice;
            return TrackingState.ValidAmount(result) ? result : 0;
        }
    }
}
