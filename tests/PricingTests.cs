using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using OreMarket;
using KSP.UI.Screens;

internal static class PricingTests
{
    internal static void UseUniformFixture()
    {
        var config = new ConfigNode(); config.AddValue("fallbackPrice", "100");
        if (!OrePricing.Load(new[] { config })) throw new Exception("Uniform regression fixture failed");
    }

    // Reads the shipped simple fixture into doubles; does not claim to emulate
    // KSP's full ConfigNode/ModuleManager parser. Runtime uses GameDatabase.
    internal static ConfigNode Shipped()
    {
        string text = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Prices.cfg"));
        text = Regex.Replace(text, @"//[^\r\n]*", "");
        if (Regex.Matches(text, @"(?m)^\s*OREMARKET_PRICES\b").Count != 1) throw new Exception("Expected single shipped config");
        var node = new ConfigNode();
        foreach (Match match in Regex.Matches(text, @"(?m)^\s*fallbackPrice\s*=\s*([^\r\n]+)")) node.AddValue("fallbackPrice", match.Groups[1].Value.Trim());
        foreach (Match body in Regex.Matches(text, @"\bBODY\s*\{([^{}]*)\}"))
        {
            var child = node.AddNode("BODY");
            foreach (Match field in Regex.Matches(body.Groups[1].Value, @"(?m)^\s*(name|price)\s*=\s*([^\r\n]+)")) child.AddValue(field.Groups[1].Value, field.Groups[2].Value.Trim());
        }
        foreach (Match block in Regex.Matches(text, @"\bMARKET\s*\{([^{}]*)\}"))
        {
            var child = node.AddNode("MARKET");
            foreach (Match field in Regex.Matches(block.Groups[1].Value, @"(?m)^\s*(\w+)\s*=\s*([^\r\n]+)")) child.AddValue(field.Groups[1].Value,field.Groups[2].Value.Trim());
        }
        foreach(Match block in Regex.Matches(text,@"\bOBJECT\s*\{([^{}]*)\}")) {
            var child=node.AddNode("OBJECT");foreach(Match field in Regex.Matches(block.Groups[1].Value,@"(?m)^\s*(name|price)\s*=\s*([^\r\n]+)"))child.AddValue(field.Groups[1].Value,field.Groups[2].Value.Trim());
        }
        return node;
    }
    private static void Fire(ProtoVessel vessel) => GameEvents.onVesselRecoveryProcessingComplete.Fire(vessel, null, 0.25f);
    internal static void Run(Action<string, bool> check)
    {
        PlanetaryTests.Seed();
        var defaults = Shipped();
        check("config: shipped runtime file loads", OrePricing.Load(new[] { defaults }));
        string[] names = { "Kerbin", "Minmus", "Mun", "Ike", "Duna", "Gilly", "Dres", "Pol", "Bop", "Vall", "Moho", "Eeloo", "Laythe", "Tylo", "Eve" };
        double[] prices = { 0, 30, 40, 65, 75, 80, 105, 115, 125, 150, 170, 175, 190, 250, 350 };
        check("config: all and only requested stock defaults shipped", defaults.GetNodes("BODY").Length == names.Length);
        for (int i = 0; i < names.Length; i++)
        {
            bool available = OrePricing.TryGetUnitPrice(names[i], out double price);
            check("config: requested price " + names[i], price == prices[i] && available == (prices[i] > 0));
        }
        check("config: Eve escape cargo is valued above Laythe", OrePricing.Quote("Eve", 1, 1) > OrePricing.Quote("Laythe", 1, 1));
        check("config: conservative modded-body fallback is 10", OrePricing.Quote("CustomBody", 50, 50) == 500);
        check("config: fallback cannot grant an unknown identity eligibility", OrePricing.Quote("MissingBody", 50, 50) == 0);
        var unsupported = defaults.AddNode("BODY"); unsupported.AddValue("name", "Jool"); unsupported.AddValue("price", "10000");
        check("config: explicit gas-giant price does not enable unsupported origin", OrePricing.Load(new[] { defaults }) && OrePricing.Quote("Jool", 50, 50) == 0);
        defaults = Shipped();
        defaults.GetNodes("BODY").Single(n => n.GetValue("name") == "Kerbin").SetValue("price", "999", true);
        check("config: Kerbin price is forced zero despite override", OrePricing.Load(new[] { defaults }) && OrePricing.Quote("Kerbin", 50, 50) == 0);
        var config = new ConfigNode(); config.AddValue("fallbackPrice", "25");
        check("config: Kerbin price stays zero when omitted", OrePricing.Load(new[] { config }) && OrePricing.Quote("Kerbin", 50, 50) == 0);
        check("config: edited fallback applies to legitimate unlisted body", OrePricing.Quote("CustomBody", 4, 4) == 100);
        var custom = config.AddNode("BODY"); custom.AddValue("name", "CustomBody"); custom.AddValue("price", "12.5");
        check("config: body override replaces fallback", OrePricing.Load(new[] { config }) && OrePricing.Quote("CustomBody", 4, 4) == 50);
        custom.SetValue("price", "0", true);
        check("config: explicit zero body price disables sale without fallback", OrePricing.Load(new[] { config }) && OrePricing.Quote("CustomBody", 4, 4) == 0);
        foreach (string bad in new[] { "-1", "NaN", "Infinity", "1,5", "garbage", "1e9999" })
        {
            var invalid = Shipped(); invalid.SetValue("fallbackPrice", bad, true);
            check("config: invalid fallback fails closed " + bad, !OrePricing.Load(new[] { invalid }) && OrePricing.Quote("Mun", 50, 50) == 0);
            invalid = Shipped(); invalid.GetNodes("BODY")[0].SetValue("price", bad, true);
            check("config: invalid entry fails closed " + bad, !OrePricing.Load(new[] { invalid }) && OrePricing.Quote("Mun", 50, 50) == 0);
        }
        config = Shipped(); config.AddValue("fallbackPrice", "15");
        check("config: duplicate fallback rejected", !OrePricing.Load(new[] { config }));
        config = Shipped(); config.nodes.Add(config.GetNodes("BODY")[0]);
        check("config: duplicate body identity rejected", !OrePricing.Load(new[] { config }));
        check("config: absent config cannot fall back to legacy universal price", !OrePricing.Load(new ConfigNode[0]) && OrePricing.Quote("Mun", 50, 50) == 0);
        check("config: ambiguous root config rejected", !OrePricing.Load(new[] { Shipped(), Shipped() }));
        config = Shipped(); config.RemoveValues("fallbackPrice");
        check("config: missing fallback rejected", !OrePricing.Load(new[] { config }));
        config = Shipped(); config.GetNodes("BODY")[0].RemoveValues("price");
        check("config: missing body price rejected", !OrePricing.Load(new[] { config }));
        config = Shipped(); config.GetNodes("BODY")[0].AddValue("price", "1");
        check("config: duplicate body price field rejected", !OrePricing.Load(new[] { config }));
        config = Shipped(); config.GetNodes("BODY")[0].SetValue("name", "", true);
        check("config: empty identity rejected", !OrePricing.Load(new[] { config }));
        check("config: null root data fails closed", !OrePricing.Load(null));
        var culture = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE"); config = Shipped(); config.SetValue("fallbackPrice", "12.5", true); check("config: decimal parsing is invariant", OrePricing.Load(new[] { config }) && OrePricing.Quote("CustomBody", 4, 4) == 50); }
        finally { CultureInfo.CurrentCulture = culture; }

        var sales = new RecoverySales(); sales.OnAwake();
        try
        {
            for (int i = 0; i < names.Length; i++)
            {
                OrePricing.Load(new[] { Shipped() }); RecoveryTests.Reset(sales);
                var cargo = RecoveryTests.Cargo(origin: names[i], mined: 50);
                Fire(cargo);
                check("configured handler: " + names[i] + " 50 units", Funding.Instance.Calls == (prices[i] > 0 ? 1 : 0) && Funding.Instance.Funds == 1000 + 50 * prices[i]);
                if (prices[i] > 0)
                {
                    check("configured receipt: unit price and total " + names[i], MessageSystem.Instance.Messages.Single().message.Contains("Price: " + prices[i].ToString(CultureInfo.InvariantCulture) + " funds/unit") && MessageSystem.Instance.Messages[0].message.Contains("Requested bonus: " + (50 * prices[i]).ToString(CultureInfo.InvariantCulture)));
                    var claims = new ConfigNode(); sales.OnSave(claims); sales.OnLoad(claims); Fire(cargo);
                    check("configured handler: persistent duplicate rejected " + names[i], Funding.Instance.Calls == 1);
                }
            }
            OrePricing.Load(new[] { Shipped() }); RecoveryTests.Reset(sales);
            var stored = RecoveryTests.Cargo(origin: "Minmus", mined: 50);
            var trackerData = stored.protoPartSnapshots[0].modules[0].moduleValues.GetNode(OreMarketTank.NodeName);
            check("config: saved provenance contains no locked-in price", trackerData.GetValue("price") == null && OreMarketTank.ReadState(trackerData).Origin == "Minmus");
            config = Shipped(); config.GetNodes("BODY").Single(n => n.GetValue("name") == "Minmus").SetValue("price", "35", true);
            OrePricing.Load(new[] { config }); Fire(stored);
            check("configured handler: existing saved cargo uses edited recovery-time price", Funding.Instance.LastAmount == 1750);
            OrePricing.Load(new[] { Shipped() }); RecoveryTests.Reset(sales);
            var partial = RecoveryTests.Cargo(origin: "Duna", remaining: 37.5);
            Funding.Instance.Funds += 321; Fire(partial);
            check("configured handler: Duna consumption plus separate stock refund", Funding.Instance.Funds == 1321 + 37.5 * 75);
            RecoveryTests.Reset(sales); Fire(RecoveryTests.Cargo(origin: "CustomBody"));
            check("configured handler: modded-body fallback payout", Funding.Instance.LastAmount == 500);
            RecoveryTests.Reset(sales); HighLogic.CurrentGame.Mode = Game.Modes.SCIENCE_SANDBOX; Fire(RecoveryTests.Cargo());
            check("configured handler: career gate retained", Funding.Instance.Calls == 0);
            RecoveryTests.Reset(sales); var vessel = RecoveryTests.Cargo(); vessel.orbitSnapShot.ReferenceBodyIndex = 2; Fire(vessel);
            check("configured handler: Kerbin-only recovery retained", Funding.Instance.Calls == 0);
        }
        finally { sales.OnDestroy(); }
        OrePricing.Load(new[] { Shipped() });
    }
}


