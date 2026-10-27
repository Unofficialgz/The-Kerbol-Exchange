using System;
using System.Linq;
using OreMarket;

internal static class PlanetaryTests
{
    // Test fixtures only: production never enumerates a list of named planets.
    internal static readonly string[] Supported = {
        "Kerbin", "Mun", "Minmus", "Moho", "Eve", "Gilly", "Duna", "Ike", "Dres",
        "Laythe", "Vall", "Tylo", "Bop", "Pol", "Eeloo", "CustomBody" };
    internal static void Seed()
    {
        FlightGlobals.Bodies.Clear(); int index = 1;
        foreach (string name in Supported) FlightGlobals.Bodies.Add(new CelestialBody {
            bodyName = name, bodyDisplayName = "Translated " + name, flightGlobalsIndex = index++ });
        FlightGlobals.Bodies.Add(new CelestialBody { bodyName = "Jool", hasSolidSurface = false, flightGlobalsIndex = index++ });
        FlightGlobals.Bodies.Add(new CelestialBody { bodyName = "Sun", hasSolidSurface = false, isStar = true, flightGlobalsIndex = index });
    }
    internal static void Run(Action<string, bool> check)
    {
        Seed();
        string vessel = Guid.NewGuid().ToString("N"), manifest = "1:SmallTank;2:MiniDrill";
        foreach (string body in Supported)
        {
            var state = TrackingState.New(0, 1000);
            state.Mining(body, 50, 50, true, vessel, manifest);
            check("planetary: catalog identity and fixed price " + body,
                OrePricing.TryGetUnitPrice(body, out double price) == (body != "Kerbin") && price == (body == "Kerbin" ? 0 : 100) && OreOrigins.Resolve(body).bodyName == body);
            state = TrackingState.Load(state.Save());
            check("planetary: saved origin and remaining quantity " + body,
                state.Origin == body && state.Eligible == 50 && !state.Invalid && RecoveryPolicy.Quote(state, vessel, manifest, 50, 1000, true, true) == (body == "Kerbin" ? 0 : 5000));
            state.Observe(40);
            check("planetary: consumed cargo pricing " + body, RecoveryPolicy.Quote(state, vessel, manifest, 40, 1000, true, true) == (body == "Kerbin" ? 0 : 4000));
            check("planetary: recovery outside Kerbin pays zero " + body, RecoveryPolicy.Quote(state, vessel, manifest, 40, 1000, true, false) == 0);
            check("planetary: science/sandbox gate pays zero " + body, RecoveryPolicy.Quote(state, vessel, manifest, 40, 1000, false, true) == 0);
            state.Observe(40); // Transport alone has no mining callback.
            check("planetary: transport cannot relabel " + body, state.Origin == body && state.Eligible == 40);
            state.Mining(body == "Mun" ? "Duna" : "Mun", 1, 41, true, vessel, manifest);
            check("planetary: incompatible direct addition outside stock routing fails closed " + body, state.Invalid && state.Eligible == 0);
            var unverified = TrackingState.New(50, 1000);
            check("planetary: unverified stock Ore cannot sell on " + body, RecoveryPolicy.Quote(unverified, vessel, manifest, 50, 1000, true, true) == 0);
        }
        check("price examples: Minmus 50, Duna 100, Mun 300", OrePricing.Quote("Minmus", 50, 50) == 5000 && OrePricing.Quote("Duna", 100, 100) == 10000 && OrePricing.Quote("Mun", 300, 300) == 30000);
        foreach (string origin in new[] { "UnknownBody", "Jool", "Sun", "Asteroid", "Comet", "", "mun", "Translated Mun", "bad\nbody" })
            check("planetary: unsupported/non-identity origin rejected " + origin.Replace("\n", "\\n"), !OreOrigins.HasMarket(origin) && OrePricing.Quote(origin, 50, 50) == 0);
        var modded = FlightGlobals.Bodies.Single(b => b.bodyName == "CustomBody");
        check("planetary: compatible mod body resolved without display-name list", OreOrigins.Resolve("CustomBody") == modded && OrePricing.Quote("CustomBody", 50, 50) == 5000);
        var savedMod = TrackingState.New(0, 100); savedMod.Mining("CustomBody", 20, 20, true, vessel, manifest);
        var legacy = savedMod.Save(); legacy["reason"] = "CustomBody Ore; no market yet";
        var legacyLoaded = TrackingState.Load(legacy);
        check("planetary: previous valid non-Mun provenance retains quantity with updated market status", legacyLoaded.Eligible == 20 && legacyLoaded.Origin == "CustomBody" && legacyLoaded.Reason == "CustomBody Ore; verified");
        legacy["invalid"] = "True"; legacy["eligible"] = "0";
        check("planetary: universal markets never restore invalid old cargo", TrackingState.Load(legacy).Invalid && TrackingState.Load(legacy).Eligible == 0);
        modded.flightGlobalsIndex = 9876;
        check("planetary: changed catalog index preserves internal origin", OrePricing.Quote(TrackingState.Load(savedMod.Save()).Origin, 20, 20) == 2000);
        FlightGlobals.Bodies.Remove(modded);
        check("planetary: absent planet pack origin remains saved but cannot sell", TrackingState.Load(savedMod.Save()).Origin == "CustomBody" && OrePricing.Quote("CustomBody", 20, 20) == 0);
        FlightGlobals.Bodies.Add(modded);
        FlightGlobals.Bodies.Add(new CelestialBody { bodyName = "CustomBody", flightGlobalsIndex = 9999 });
        check("planetary: ambiguous body identities rejected", !OreOrigins.HasMarket("CustomBody"));
        Seed();
        foreach (double bad in new[] { -1d, 0d, double.NaN, double.PositiveInfinity })
            check("price: invalid unit price fails closed " + bad, OrePricing.Calculate(50, 50, bad) == 0);
        check("price: multiplication overflow fails closed", OrePricing.Calculate(double.MaxValue, double.MaxValue, 100) == 0);
        check("price: actual physical quantity bounds sale", OrePricing.Quote("Minmus", 50, 3) == 300);
        var awaiting = TrackingState.New(0);
        check("planetary: waiting status stays generic", awaiting.Reason == "Waiting for Ore");
        FlightGlobals.Bodies.Clear();
        check("planetary: uninitialized catalog cannot authorize payment", OrePricing.Quote("Mun", 50, 50) == 0);
        Seed();
    }
}


