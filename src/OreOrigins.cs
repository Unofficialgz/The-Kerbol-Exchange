using System;

namespace OreMarket
{
    public static class OreOrigins
    {
        public static bool Valid(string body) => !string.IsNullOrWhiteSpace(body) && body.Length <= 256 &&
            body.IndexOfAny(new[] { '\r', '\n', '\0' }) < 0;
        public static string Label(string body) => ObjectMarkets.Find(body) != null ? "Asteroid & Comet Ore" : Valid(body) ? body + " Ore" : "Unverified Ore";

        // bodyName is the existing save's internal KSP identity, not displayName or
        // a flightGlobalsIndex (indices may change when planet packs are installed).
        // Never infer a mining origin from the vessel's current/recovery location.
        public static CelestialBody Resolve(string identity)
        {
            if (!Valid(identity) || FlightGlobals.Bodies == null) return null;
            CelestialBody result = null;
            foreach (CelestialBody body in FlightGlobals.Bodies)
            {
                if (body == null || !string.Equals(body.bodyName, identity, StringComparison.Ordinal)) continue;
                if (result != null) return null; // Ambiguous identities fail closed.
                result = body;
            }
            return result;
        }

        public static bool HasMarket(string identity)
        {
            CelestialBody body = Resolve(identity);
            // Eligibility alone never proves mining. Only witnessed stock surface
            // harvesting can create provenance, under the existing attribution rules.
            return body != null && body.hasSolidSurface && !body.isStar;
        }
    }
}
