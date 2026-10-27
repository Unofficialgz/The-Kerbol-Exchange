using System;
using System.Collections.Generic;
using System.Linq;
using OreMarket.Terminal;

internal static class TerminalTests
{
    private static MarketBody Body(string identity, string parent, double price = 0, bool star = false)
        => new MarketBody { Identity = identity, Parent = parent, Price = price, Star = star };
    internal static void Run(Action<string, bool> check)
    {
        var input = new List<MarketBody> {
            Body("Sun", "Sun", 0, true), Body("Kerbin", "Sun"), Body("Mun", "Kerbin", 40), Body("Minmus", "Kerbin", 30),
            Body("Duna", "Sun", 75), Body("Ike", "Duna", 65), Body("Eve", "Sun", 350), Body("Gilly", "Eve", 80),
            Body("Jool", "Sun"), Body("Laythe", "Jool", 190), Body("Vall", "Jool", 150), Body("Tylo", "Jool", 250),
            Body("Bop", "Jool", 125), Body("Pol", "Jool", 115), Body("Moho", "Sun", 170), Body("Dres", "Sun", 105), Body("Eeloo", "Sun", 175) };
        var groups = MarketCatalog.Build(input);
        check("terminal: actual hierarchy creates seven planetary roots", groups.Count == 7);
        foreach (string identity in new[] { "Moho", "Dres", "Eeloo" })
        {
            var group = groups.Single(g => g.Identity == identity);
            check("terminal: direct leaf navigation " + identity, !group.Expandable && group.Title == identity && group.Markets.Count == 1 && group.Markets[0].Title == identity + " Ore");
        }
        var kerbin = groups.Single(g => g.Identity == "Kerbin");
        check("terminal: Kerbin group contains only its marketable moons", kerbin.Title == "Kerbin System" && kerbin.Expandable && kerbin.Markets.Select(m => m.Identity).SequenceEqual(new[] { "Mun", "Minmus" }));
        check("terminal: Duna parent and moon are selectable in group", groups.Single(g => g.Identity == "Duna").Markets.Select(m => m.Identity).SequenceEqual(new[] { "Duna", "Ike" }));
        var jool = groups.Single(g => g.Identity == "Jool");
        check("terminal: Jool group includes five moons and no gas-giant commodity", jool.Expandable && jool.Markets.Count == 5 && !jool.Markets.Any(m => m.Identity == "Jool"));
        check("terminal: no star or invented Other Markets row", !groups.Any(g => g.Identity == "Sun" || g.Title == "Other Markets"));
        var shuffled = new List<MarketBody>(input);
        shuffled.Insert(1, Body("EarlyLeaf", "Sun", 10));
        var partitioned = MarketCatalog.Build(shuffled);
        bool seenLeaf = false, validOrder = true;
        foreach (var group in partitioned) { if (!group.Expandable) seenLeaf = true; else if (seenLeaf) validOrder = false; }
        check("sidebar: all systems precede standalone markets even when input starts with a leaf", validOrder);
        check("sidebar: catalogue order is stable within each section", partitioned.Where(g => g.Expandable).Select(g => g.Identity).SequenceEqual(new[] { "Kerbin", "Duna", "Eve", "Jool" }) && partitioned.Where(g => !g.Expandable).First().Identity == "EarlyLeaf");
        check("sidebar: icons use actual Kerbin satellites", kerbin.Moons.SequenceEqual(new[] { "Mun", "Minmus" }));
        check("sidebar: icons use all five Jool satellites", jool.Moons.SequenceEqual(new[] { "Laythe", "Vall", "Tylo", "Bop", "Pol" }));
        check("sidebar: moon indentation follows actual ancestry", kerbin.Markets.All(m => m.Depth == 1) && groups.Single(g => g.Identity == "Duna").Markets.Select(m => m.Depth).SequenceEqual(new[] { 0, 1 }));
        var collapse = new HashSet<string>();
        float expandedHeight = SidebarLayout.ContentHeight(groups, collapse);
        check("sidebar: expanded scroll height includes one divider and all rows", expandedHeight == 4 * (SidebarLayout.HeaderHeight + SidebarLayout.GroupGap) + 14 * SidebarLayout.RowHeight + SidebarLayout.DividerHeight);
        collapse.Add("Jool");
        check("sidebar: collapsing removes precisely its five row heights", expandedHeight - SidebarLayout.ContentHeight(groups, collapse) == 5 * SidebarLayout.RowHeight);
        check("sidebar: no divider consumes space for empty catalogue", SidebarLayout.ContentHeight(new List<MarketGroup>(), collapse) == 0);
        foreach (var market in groups.SelectMany(g => g.Markets))
            check("sidebar: fixed baseline has no synthetic samples " + market.Identity, market.Watch.Samples.Length == 0 && market.Watch.Direction == 0 && market.Watch.ChangeText == "Fixed");
        var up = WatchlistSnapshot.Create(12, new[] { new MarketPeriod(0, 10, 10, 10, 10), new MarketPeriod(1, 12, 12, 12, 12) });
        var down = WatchlistSnapshot.Create(8, new[] { new MarketPeriod(0, 10, 10, 10, 10), new MarketPeriod(1, 8, 8, 8, 8) });
        var flat = WatchlistSnapshot.Create(10, new[] { new MarketPeriod(0, 10, 10, 10, 10), new MarketPeriod(1, 10, 10, 10, 10) });
        check("sidebar: future real history supplies exact closes and positive change", up.Samples.SequenceEqual(new double[] { 10, 10, 12 }) && up.Direction == 1 && up.ChangeText == "+20.0%");
        check("sidebar: future negative and unchanged history directions", down.Direction == -1 && down.ChangeText == "-20.0%" && flat.Direction == 0 && flat.ChangeText == "0.0%");
        check("sidebar: absent history remains fixed", WatchlistSnapshot.Create(100, null).Samples.Length == 0);
        var singleIcon = PlanetArt.Pixels("Dres", null);
        check("sidebar: single planet has transparent background and coloured centre", singleIcon.Length == 1600 && singleIcon[0] == 0 && singleIcon[20 * 40 + 20] == ((PlanetArt.Color("Dres") << 8) | 255));
        var kerbinIcon = PlanetArt.Pixels("Kerbin", kerbin.Moons);
        check("sidebar: Kerbin icon shows two actual moons and land", kerbinIcon[54 * 64 + 51] == ((PlanetArt.Color("Mun") << 8) | 255) && kerbinIcon[10 * 64 + 51] == ((PlanetArt.Color("Minmus") << 8) | 255) && kerbinIcon[35 * 64 + 23] == 0x73AB79FF);
        check("sidebar: different stock planets retain distinct colours", new[] { "Eve", "Duna", "Jool", "Dres", "Eeloo" }.Select(PlanetArt.Color).Distinct().Count() == 5);
        check("sidebar: unknown planet has deterministic coloured fallback", PlanetArt.Color("PackPlanet") == PlanetArt.Color("PackPlanet") && PlanetArt.Pixels("PackPlanet", new[] { "PackMoon" }).Any(pixel => pixel != 0));
        var packed = MarketCatalog.Build(new[] { Body("Star", null, 0, true), Body("Solo", "Star", 10), Body("PackPlanet", "Star", 10), Body("PackMoon", "PackPlanet", 11), Body("SubMoon", "PackMoon", 12) });
        check("sidebar: modded system sorted first with satellite icon and nested depth", packed[0].Identity == "PackPlanet" && packed[0].Moons.SequenceEqual(new[] { "PackMoon" }) && packed[0].Markets.Last().Depth == 2 && packed[1].Identity == "Solo");
        check("sidebar: dense modded system icon stays bounded", PlanetArt.Pixels("PackPlanet", Enumerable.Range(0, 30).Select(i => "Moon" + i).ToArray()).Length == 4096);
        check("visual: requested palette values retained", TerminalPalette.Background == 0x101010 && TerminalPalette.Panel == 0x1B1B1B && TerminalPalette.Selected == 0x303030 && TerminalPalette.Border == 0x383838 && TerminalPalette.Primary == 0xE8E8E8);
        foreach (uint tone in new[] { TerminalPalette.Background, TerminalPalette.Panel, TerminalPalette.Selected, TerminalPalette.Border, TerminalPalette.Hover, TerminalPalette.Primary, TerminalPalette.Secondary, TerminalPalette.Baseline, PlanetArt.OrbitColor })
            check("visual: general theme and orbit lines have no colour tint " + tone, (tone >> 16) == ((tone >> 8) & 255) && (tone >> 16) == (tone & 255));
        check("visual: modest heading increase accommodates 44-unit diagrams", SidebarLayout.HeaderHeight == 48 && SidebarLayout.RowHeight == 58);
        check("visual: separate texture sizes preserve simple commodity icons", PlanetArt.TextureSize(null) == 40 && PlanetArt.TextureSize(new string[0]) == 40 && PlanetArt.TextureSize(kerbin.Moons) == 64);
        uint line = (PlanetArt.OrbitColor << 8) | 255;
        check("visual: system has exactly one circle at known top and left orbit points", kerbinIcon[57 * 64 + 26] == line && kerbinIcon[32 * 64 + 1] == line && kerbinIcon[50 * 64 + 26] == 0 && kerbinIcon[32 * 64 + 7] == 0);
        check("visual: right-hand connecting line joins vertically stacked moons", kerbinIcon[21 * 64 + 51] == line && kerbinIcon[43 * 64 + 51] == line);
        check("visual: larger central planet is left of moons with visible separation", kerbinIcon[32 * 64 + 26] == ((PlanetArt.Color("Kerbin") << 8) | 255) && kerbinIcon[32 * 64 + 43] == 0);
        var joolIcon = PlanetArt.Pixels("Jool", jool.Moons);
        for (int i = 0; i < 5; i++)
            check("visual: Jool moon is individually coloured on shared right column " + jool.Moons[i], joolIcon[(54 - i * 11) * 64 + 51] == ((PlanetArt.Color(jool.Moons[i]) << 8) | 255));
        foreach (string parent in new[] { "Eve", "Duna" })
        {
            var group = groups.Single(g => g.Identity == parent);
            var pixels = PlanetArt.Pixels(parent, group.Moons);
            check("visual: single moon system retains marker and short connecting stroke " + parent, pixels[32 * 64 + 51] == ((PlanetArt.Color(group.Moons[0]) << 8) | 255) && pixels[38 * 64 + 51] == line);
        }
        check("visual: individual body has no orbit or connecting stem", singleIcon[20 * 40 + 1] == 0 && singleIcon[39 * 40 + 20] == 0);
        foreach (int count in new[] { 1, 2, 5, 8, 30 })
        {
            var names = Enumerable.Range(0, count).Select(i => "PackMoon" + i).ToArray();
            var pixels = PlanetArt.Pixels("PackPlanet", names);
            check("visual: dense generated diagrams have transparent outer corners " + count, pixels[0] == 0 && pixels[63] == 0 && pixels[63*64] == 0 && pixels[4095] == 0);
        }
        var session = new TerminalSession(); session.Bind(groups);
        check("terminal: candle is default", session.Mode == ChartMode.Candle);
        var dres = groups.Single(g => g.Identity == "Dres").Markets[0]; session.Select(dres);
        check("terminal: direct Dres click selects Dres Ore", session.Selected == "Dres" && session.Selection.Title == "Dres Ore");
        var duna = groups.Single(g => g.Identity == "Duna").Markets[0]; session.Select(duna);
        check("terminal: commodity view has cached configured price and 100-unit value", session.Selection.PriceText == "75 funds / unit" && session.Selection.HundredText == "7,500.00 funds");
        session.Collapsed.Add("Kerbin"); session.Mode = ChartMode.Area; session.Bind(MarketCatalog.Build(input));
        check("terminal: selection, chart mode and collapsed state survive catalog/scene rebind", session.Selected == "Duna" && session.Mode == ChartMode.Area && session.Collapsed.Contains("Kerbin"));
        var history = new UnavailableHistory();
        foreach (ChartMode mode in Enum.GetValues(typeof(ChartMode)))
        {
            session.Mode = mode;
            check("terminal: selectable chart mode has no synthetic periods " + mode, !history.TryGetPeriods(session.Selected, MarketRange.Hours24, out MarketPeriod[] periods) && periods.Length == 0);
        }
        check("terminal: four chart modes and four active-time ranges", Enum.GetValues(typeof(ChartMode)).Length == 4 && Enum.GetValues(typeof(MarketRange)).Length == 4);
        session.Bind(new List<MarketGroup>());
        check("terminal: unavailable selection clears safely", session.Selection == null && session.Selected == null);
        input.Add(Body("PackPlanet", "Sun", 10)); input.Add(Body("PackMoon", "PackPlanet", 11)); input.Add(Body("SubMoon", "PackMoon", 12));
        groups = MarketCatalog.Build(input);
        check("terminal: modded planet with nested moons creates its own real system", groups.Single(g => g.Identity == "PackPlanet").Markets.Count == 3 && groups.Single(g => g.Identity == "PackPlanet").Title == "PackPlanet System");
        var reordered = new List<MarketBody> { Body("Sun", "Sun", 0, true), Body("Moon", "Parent", 10), Body("Parent", "Sun", 15) };
        check("terminal: parent market appears first even if catalog moon precedes it", MarketCatalog.Build(reordered).Single().Markets[0].Identity == "Parent");
        check("terminal: cyclic relationships are omitted safely", MarketCatalog.Build(new[] { Body("A", "B", 10), Body("B", "A", 10) }).Count == 0);
        check("terminal: missing parent does not create invented fallback group", MarketCatalog.Build(new[] { Body("A", "Missing", 10) }).Count == 0);
        check("terminal: ambiguous identity omitted", MarketCatalog.Build(new[] { Body("Sun", "Sun", 0, true), Body("A", "Sun", 10), Body("A", "Sun", 20) }).Count == 0);
        check("terminal: malformed identities omitted", MarketCatalog.Build(new[] { Body(null, null, 10), Body(" ", null, 10) }).Count == 0);
        check("terminal: nonpositive and nonfinite prices cannot create rows", MarketCatalog.Build(new[] { Body("Sun", "Sun", 0, true), Body("A", "Sun", double.NaN), Body("B", "Sun", double.PositiveInfinity), Body("C", "Sun", -1) }).Count == 0);
        check("terminal: positive-price star is still hidden", MarketCatalog.Build(new[] { Body("Star", null, 100, true) }).Count == 0);
        foreach (var resolution in new[] { new[] { 800, 600 }, new[] { 1024, 768 }, new[] { 1280, 720 }, new[] { 1920, 1080 }, new[] { 2560, 1440 } })
            foreach (double requested in new[] { 0.75, 1, 1.25, 1.5, 2 })
            {
                double scale = TerminalSession.FitScale(requested, resolution[0], resolution[1]);
                check("terminal: geometry fits " + resolution[0] + "x" + resolution[1] + " UI scale=" + requested,
                    scale > 0 && 1040 * scale <= resolution[0] - 24 + 0.000001 && 660 * scale <= resolution[1] - 24 + 0.000001);
            }
        var candle = new MarketPeriod(0, 10, 12, 8, 11);
        check("terminal: future OHLC data boundary retains all values", candle.Start == 0 && candle.Open == 10 && candle.High == 12 && candle.Low == 8 && candle.Close == 11);
        bool rejected = false;
        try { new MarketPeriod(0, 20, 12, 8, 11); } catch (ArgumentException) { rejected = true; }
        check("terminal: invalid future candle cannot enter data boundary", rejected);
        check("terminal: current fixed values are not labelled historical movement", !duna.PriceText.Contains("%") && !duna.HundredText.Contains("%"));
    }
}
