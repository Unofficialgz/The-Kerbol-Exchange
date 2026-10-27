using System;
using System.Collections.Generic;
using System.Globalization;

namespace OreMarket.Terminal
{
    internal sealed class MarketBody
    {
        internal string Identity, Parent;
        internal bool Star;
        internal double Price;
        internal double BasePrice = double.NaN;
        internal MarketPeriod[] History = Array.Empty<MarketPeriod>();
        internal bool Live = false;
    }
    internal sealed class Commodity
    {
        internal readonly string Identity, Title, PriceText, PriceShort, HundredText, Reminder, IconKey, BasePriceText;
        internal readonly double Price;
        internal readonly ObjectMarketDefinition ObjectMarket;
        internal bool Tradeable => ObjectMarket==null || !ObjectMarket.Special;
        internal string RowTitle => ObjectMarket==null?Title:ObjectMarket.Special?"MAGIC BOULDER":ObjectMarket.Name;
        internal string Secondary => ObjectMarket==null?"":ObjectMarket.Secondary;
        internal readonly WatchlistSnapshot Watch;
        internal int Depth;
        internal Commodity(string identity, double price, double basePrice = double.NaN, MarketPeriod[] history = null, bool live = false)
        {
            Watch = WatchlistSnapshot.Create(price, history ?? Array.Empty<MarketPeriod>(), live);
            IconKey = identity + "|";
            Identity = identity;ObjectMarket=ObjectMarkets.Find(identity); Title = ObjectMarket==null?identity+" Ore":ObjectMarket.Title; Price = price;
            PriceShort = price.ToString("0.##", CultureInfo.InvariantCulture);
            PriceText = price.ToString("0.######", CultureInfo.InvariantCulture) + " funds / unit";
            BasePriceText = (double.IsNaN(basePrice) ? price : basePrice).ToString("0.######", CultureInfo.InvariantCulture) + " funds / unit";
            Reminder = "Recover tracked Ore on Kerbin in career mode to sell at the current price.";
            if(ObjectMarket!=null && ObjectMarket.Special)Reminder="Not tradeable. No verified sales are available for this commodity.";
            if(ObjectMarket!=null && !ObjectMarket.Special)Reminder="Recover tracked Ore on Kerbin in career mode to sell at the current price.";
            double total = price * 100;
            HundredText = double.IsInfinity(total) ? "Value exceeds numeric range" : total.ToString("N2", CultureInfo.InvariantCulture) + " funds";
        }
    }
    internal sealed class MarketGroup
    {
        internal string Identity, Title, IconKey;
        internal bool Expandable, ObjectGroup;
        internal readonly List<string> Moons = new List<string>();
        internal readonly List<Commodity> Markets = new List<Commodity>();
    }
    internal static class MarketCatalog
    {
        internal static List<MarketGroup> Build(IList<MarketBody> input)
        {
            var bodies = new Dictionary<string, MarketBody>(StringComparer.Ordinal);
            var duplicate = new HashSet<string>(StringComparer.Ordinal);
            foreach (var body in input)
            {
                if (body == null || string.IsNullOrWhiteSpace(body.Identity)) continue;
                if (bodies.ContainsKey(body.Identity)) duplicate.Add(body.Identity);
                else bodies.Add(body.Identity, body);
            }
            var groups = new List<MarketGroup>();
            var roots = new Dictionary<string, MarketGroup>(StringComparer.Ordinal);
            foreach (var body in input)
            {
                if (body == null || string.IsNullOrWhiteSpace(body.Identity) || body.Star || body.Price <= 0 || double.IsNaN(body.Price) || double.IsInfinity(body.Price) || duplicate.Contains(body.Identity)) continue;
                MarketBody root = body;
                var visited = new HashSet<string>(StringComparer.Ordinal);
                bool valid = true;
                while (!string.IsNullOrEmpty(root.Parent) && root.Parent != root.Identity)
                {
                    if (!visited.Add(root.Identity) || duplicate.Contains(root.Parent) || !bodies.TryGetValue(root.Parent, out MarketBody parent)) { valid = false; break; }
                    if (parent.Star) break;
                    root = parent;
                }
                if (!valid || root.Star) continue;
                if (!roots.TryGetValue(root.Identity, out MarketGroup group))
                {
                    bool moons = false;
                    foreach (var candidate in input)
                        if (candidate != null && candidate.Identity != root.Identity && candidate.Parent == root.Identity && !candidate.Star) { moons = true; break; }
                    group = new MarketGroup { Identity = root.Identity, Expandable = moons, Title = root.Identity + (moons ? " System" : "") };
                    foreach (var candidate in input)
                        if (candidate != null && candidate.Identity != root.Identity && candidate.Parent == root.Identity && !candidate.Star && !duplicate.Contains(candidate.Identity))
                            group.Moons.Add(candidate.Identity);
                    group.IconKey = root.Identity + "|" + string.Join("|", group.Moons);
                    roots.Add(root.Identity, group); groups.Add(group);
                }
                int depth = 0; var ancestor = body;
                while (ancestor.Identity != root.Identity) { depth++; ancestor = bodies[ancestor.Parent]; }
                group.Markets.Add(new Commodity(body.Identity, body.Price, body.BasePrice, body.History, body.Live) { Depth = depth });
            }
            foreach (var group in groups)
                for (int i = 1; i < group.Markets.Count; i++)
                    if (group.Markets[i].Identity == group.Identity)
                    { var parent = group.Markets[i]; group.Markets.RemoveAt(i); group.Markets.Insert(0, parent); break; }
            // Stable partition preserves catalogue order inside each section.
            var ordered = new List<MarketGroup>();
            foreach (var group in groups) if (group.Expandable) ordered.Add(group);
            foreach (var group in groups) if (!group.Expandable) ordered.Add(group);
            return ordered;
        }
    }
    // Empty history is a price reference, not a historical series.
    internal sealed class WatchlistSnapshot
    {
        internal readonly double[] Samples;
        internal readonly int Direction;
        internal readonly string ChangeText;
        private WatchlistSnapshot(double[] samples, int direction, string change)
        { Samples = samples; Direction = direction; ChangeText = change; }
        internal static WatchlistSnapshot Create(double current, MarketPeriod[] periods, bool live = false)
        {
            if (periods == null || periods.Length == 0)
                return new WatchlistSnapshot(Array.Empty<double>(), 0, live ? "New" : "Fixed");
            var samples = new double[periods.Length + 1]; samples[0] = periods[0].Open;
            for (int i = 0; i < periods.Length; i++) samples[i + 1] = periods[i].Close;
            double first = samples[0], last = current;
            int direction = last.CompareTo(first);
            string change = first > 0 ? ((last - first) / first * 100).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "%" : "n/a";
            return new WatchlistSnapshot(samples, direction, change);
        }
    }
    internal static class SidebarLayout
    {
        internal const float RowHeight = 58, ObjectRowHeight=70, HeaderHeight = 48, GroupGap = 8, DividerHeight = 38;
        internal static float RowsHeight(IList<Commodity> markets) {float h=0;foreach(var m in markets)h+=m.ObjectMarket==null?RowHeight:ObjectRowHeight;return h;}
        internal static float ContentHeight(IList<MarketGroup> groups, HashSet<string> collapsed)
        {
            float height = 0; bool divided = false;
            foreach (var group in groups)
            {
                if(group.ObjectGroup)height+=12;
                if (!group.Expandable && !divided) { height += DividerHeight; divided = true; }
                height += group.Expandable ? HeaderHeight + GroupGap + (collapsed.Contains(group.Identity) ? 0 : RowsHeight(group.Markets)) : RowsHeight(group.Markets);
            }
            return height;
        }
    }
    internal static class TerminalPalette
    {
        internal const uint Background = 0x101010, Panel = 0x1B1B1B, Selected = 0x303030,
            Border = 0x383838, Hover = 0x262626, Primary = 0xE8E8E8,
            Secondary = 0xA0A0A0, Baseline = 0xD0D0D0;
    }
    internal static class PlanetArt
    {
        internal const int Size = 40, SystemSize = 64;
        internal const int PlanetX = 26, PlanetY = 32, OrbitRadius = 25, MoonX = 51;
        internal const uint OrbitColor = 0xD8D8D8;
        internal static int TextureSize(IList<string> moons) => moons != null && moons.Count > 0 ? SystemSize : Size;
        internal static uint Color(string identity)
        {
            switch (identity)
            {
                case "Kerbin": return 0x4E9BB8; case "Mun": return 0xAAA8A0; case "Minmus": return 0xA4D8BC;
                case "Eve": return 0xA977C7; case "Gilly": return 0xB2A48A; case "Duna": return 0xC46B50;
                case "Ike": return 0x929087; case "Jool": return 0x91BD56; case "Laythe": return 0x699FC1;
                case "Vall": return 0xA9CDDA; case "Tylo": return 0xB3A893; case "Bop": return 0x9A8268;
                case "Pol": return 0xC5C379; case "Moho": return 0xA98C76; case "Dres": return 0xA4A6AC;
                case "Eeloo": return 0xDAE6E8;
                default:
                    uint hash = 2166136261;
                    foreach (char c in identity ?? "") hash = unchecked((hash ^ c) * 16777619);
                    return ((96 + (hash & 127)) << 16) | ((96 + ((hash >> 8) & 127)) << 8) | (96 + ((hash >> 16) & 127));
            }
        }
        internal static uint[] Pixels(string identity, IList<string> moons)
        {
            int size = TextureSize(moons);
            if(ObjectMarkets.Reserved(identity))return ObjectSidebar.RockIcon(identity,size);
            var pixels = new uint[size * size];
            bool system = moons != null && moons.Count > 0;
            int cx = system ? PlanetX : 20, cy = system ? PlanetY : 20;
            if (system)
            {
                // Exactly one orbit centred on the larger left-hand planet.
                // Analytic edge coverage keeps the thin light outline legible
                // when its cached texture is reduced to the heading size.
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                {
                    double distance = Math.Sqrt((x-cx)*(x-cx) + (y-cy)*(y-cy));
                    double coverage = Math.Max(0, Math.Min(1, 1.1 - Math.Abs(distance - OrbitRadius)));
                    if (coverage > 0) pixels[y*size+x] = (OrbitColor << 8) | (uint)Math.Round(coverage * 255);
                }
                // Shared right-hand stem, behind the moon markers. Even a
                // one-moon system retains a short visible connecting stroke.
                int count = Math.Min(8, moons.Count);
                int top = count == 1 ? 40 : 60, bottom = count == 1 ? 24 : 4;
                for (int y = bottom; y <= top; y++) pixels[y*size+MoonX] = (OrbitColor << 8) | 255;
                int radius = count <= 5 ? 4 : count <= 7 ? 3 : 2;
                for (int i = 0; i < count; i++)
                {
                    int y = count == 1 ? PlanetY : 54 - (int)Math.Round(i * 44.0 / (count - 1));
                    Circle(pixels, size, MoonX, y, radius, Color(moons[i]));
                }
            }
            Circle(pixels, size, cx, cy, system ? 13 : 14, Color(identity));
            if (identity == "Kerbin")
            { Circle(pixels, size, cx - 3, cy + 3, 4, 0x73AB79); Circle(pixels, size, cx + 4, cy - 6, 3, 0x73AB79); }
            return pixels;
        }
        private static void Circle(uint[] pixels, int size, int cx, int cy, int radius, uint color)
        {
            for (int y = Math.Max(0, cy - radius); y <= Math.Min(size - 1, cy + radius); y++)
                for (int x = Math.Max(0, cx - radius); x <= Math.Min(size - 1, cx + radius); x++)
                    if ((x-cx)*(x-cx) + (y-cy)*(y-cy) <= radius*radius) pixels[y*size+x] = (color << 8) | 255;
        }
    }
    internal enum ChartMode { Candle, Line, Area, Bar }
    internal enum MarketRange { Minutes15, Hour1, Hours6, Hours24 }
    internal struct MarketPeriod
    {
        internal readonly double Start, Open, High, Low, Close, End;
        internal MarketPeriod(double start, double open, double high, double low, double close, double end = double.NaN)
        {
            if (!Finite(start) || !Finite(open) || !Finite(high) || !Finite(low) || !Finite(close) ||
                start < 0 || low < 0 || high < low || open < low || open > high || close < low || close > high)
                throw new ArgumentException("Invalid OHLC period");
            if (!double.IsNaN(end) && (!Finite(end) || end < start)) throw new ArgumentException("Invalid period end");
            Start = start; Open = open; High = high; Low = low; Close = close; End = double.IsNaN(end) ? start : end;
        }
        private static bool Finite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);
    }
    internal interface IMarketHistory
    {
        bool TryGetPeriods(string identity, MarketRange range, out MarketPeriod[] periods);
    }
    internal sealed class UnavailableHistory : IMarketHistory
    {
        public bool TryGetPeriods(string identity, MarketRange range, out MarketPeriod[] periods)
        { periods = Array.Empty<MarketPeriod>(); return false; }
    }
    internal sealed class TerminalSession
    {
        internal string Selected;
        private object saveOwner;
        internal bool Welcome { get; private set; } = true;
        internal void BeginSave(object owner)
        {
            if (ReferenceEquals(saveOwner, owner)) return;
            saveOwner = owner; Welcome = true;
        }
        internal void LeaveSave() { saveOwner = null; Welcome = true; }
        internal ChartMode Mode = ChartMode.Candle;
        internal MarketRange Range = MarketRange.Hour1;
        internal readonly HashSet<string> Collapsed = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> knownGroups = new HashSet<string>(StringComparer.Ordinal);
        internal Commodity Selection { get; private set; }
        internal void Bind(IList<MarketGroup> groups)
        {
            // Initialize each group once per KSP session; refreshes must not undo user choices.
            foreach(var group in groups)if(group.Expandable && knownGroups.Add(group.Identity))Collapsed.Add(group.Identity);
            Selected=ObjectMarkets.Commodity(Selected);
            Selection = null;
            foreach (var group in groups)
                foreach (var market in group.Markets)
                    if (market.Identity == Selected) Selection = market;
            if (Selection == null)
                foreach (var group in groups) { if (group.Markets.Count > 0) { Selection = group.Markets[0]; break; } }
            Selected = Selection?.Identity;
        }
        internal void Select(Commodity commodity) { Selection = commodity; Selected = commodity.Identity; Welcome = false; }
        internal static double FitScale(double requested, double width, double height)
        {
            if (double.IsNaN(requested) || double.IsInfinity(requested)) requested = 1;
            return Math.Max(0.1, Math.Min(Math.Max(0.75, Math.Min(1.75, requested)), Math.Min((width - 24) / 1040, (height - 24) / 660)));
        }
    }
}


