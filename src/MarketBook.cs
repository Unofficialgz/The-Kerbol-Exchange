using System;
using System.Collections.Generic;
using System.Globalization;

namespace OreMarket
{
    public sealed class MarketSettings
    {
        public double Interval = 60, Minimum = .65, Maximum = 2.5, Volatility = .035, Momentum = .65, TrendStrength = .006, Reversion = .012;
        public int HistoryLimit = 1440, TrendMinimum = 10, TrendMaximum = 180;
        internal static MarketSettings Read(ConfigNode[] nodes)
        {
            var s = new MarketSettings();
            if (nodes.Length == 0) return s;
            if (nodes.Length != 1) throw new FormatException("Duplicate MARKET settings");
            var n = nodes[0];
            s.Interval = Field(n, "intervalSeconds", s.Interval, 1, 3600);
            s.Minimum = Field(n, "minimumMultiplier", s.Minimum, .4, 1);
            s.Maximum = Field(n, "maximumMultiplier", s.Maximum, 1, 4);
            s.Volatility = Field(n, "volatility", s.Volatility, .001, .04);
            s.Momentum = Field(n, "momentum", s.Momentum, 0, .95);
            s.Reversion = Field(n, "meanReversion", s.Reversion, 0, .05);
            s.TrendStrength = Field(n, "trendStrength", s.TrendStrength, 0, .02);
            // Accept legacy configuration values, but enforce the new 24-hour ceiling.
            s.HistoryLimit = Math.Min(1440,Integer(n, "historyLimit", s.HistoryLimit, 64, 10080));
            s.TrendMinimum = Integer(n, "trendMinimum", s.TrendMinimum, 2, 1000);
            s.TrendMaximum = Integer(n, "trendMaximum", Math.Max(s.TrendMaximum, s.TrendMinimum), s.TrendMinimum, 10000);
            s.Events = EventSettings.Read(n);
            return s;
        }
        internal EventSettings Events = new EventSettings();
        internal static double Field(ConfigNode n, string key, double fallback, double min, double max)
        {
            var values = n.GetValues(key);
            if (values.Length == 0) return fallback;
            if (values.Length != 1 || !double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
                !Finite(value) || value < min || value > max) throw new FormatException("Invalid MARKET " + key);
            return value;
        }
        private static int Integer(ConfigNode n, string key, int fallback, int min, int max)
        {
            double value = Field(n, key, fallback, min, max);
            if (value != Math.Truncate(value)) throw new FormatException("Expected integer MARKET " + key);
            return (int)value;
        }
        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
    public struct MarketSample
    {
        public readonly double Time, Open, High, Low, Close;
        public MarketSample(double time, double open, double high, double low, double close)
        {
            if (!MarketSettings.Finite(time) || time < 0 || !Positive(open) || !Positive(high) || !Positive(low) || !Positive(close) ||
                low > high || open < low || open > high || close < low || close > high) throw new FormatException("Invalid market OHLC");
            Time = time; Open = open; High = high; Low = low; Close = close;
        }
        private static bool Positive(double value) => MarketSettings.Finite(value) && value > 0;
    }
    // This clock never reads UT, timeScale, deltaTime, a wall-clock date or a warp rate.
    // Both adjacent observations must be active; menu/loading gaps are not charged.
    internal sealed class ActiveMarketClock
    {
        private double previous;
        private bool sampled, wasActive;
        internal double Sample(double now, bool active)
        {
            double delta = sampled && active && wasActive && now >= previous ? now - previous : 0;
            sampled = true; previous = now; wasActive = active;
            return delta;
        }
    }
    internal static class MarketDynamics
    {
        internal static double Velocity(double price,double baseline,double previous,int trend,double random,MarketSettings settings)
        {
            return EventVelocity(price,baseline,previous,trend,random,settings,0,1,settings.Minimum,settings.Maximum);
        }
        internal static double EventVelocity(double price,double baseline,double previous,int trend,double random,MarketSettings settings,double pressure,double volatility,double minimum,double maximum)
        {
            double ratio=price/baseline, log=Math.Log(ratio);
            double pull=-settings.Reversion*log*(log<0 ? 3 : .25);
            double shock=(2*random-1)*settings.Volatility*volatility;
            if(random<.04 || random>.96) shock*=1.6;
            double raw=Math.Max(-.08,Math.Min(.08,settings.Momentum*previous+shock+trend*settings.TrendStrength+pull+pressure));
            // Existing out-of-range prices migrate gradually; an expired event never resets a quote.
            if(ratio<minimum) raw+=Math.Min(.025,(minimum-ratio)*.08);
            if(ratio>maximum) raw-=Math.Min(.025,(ratio-maximum)*.025);
            double headroom=raw<0 ? (price-baseline*minimum)/price : (baseline*maximum-price)/price;
            if(headroom>=0) raw*=Math.Min(1,headroom/.16);
            else if((raw<0 && ratio<minimum)||(raw>0 && ratio>maximum)) raw*=.15;
            double absoluteHeadroom=raw<0?(price-baseline*.4)/price:(baseline*4-price)/price;
            raw*=Math.Max(0,Math.Min(1,absoluteHeadroom/.16));
            return Math.Max(-.08,Math.Min(.08,raw));
        }
    }
    public sealed partial class MarketBook
    {
        private sealed class Market
        {
            internal string Identity;
            internal double Base, Price, Velocity;
            internal uint Random;
            internal int Trend, Remaining;
            internal string System;
            internal EconomicEvent Event;
            internal readonly List<EconomicEvent> Aftermaths=new List<EconomicEvent>();
            internal readonly List<MarketEventMarker> Markers=new List<MarketEventMarker>();
            internal double Cooldown;
            internal readonly Queue<MarketSample> History = new Queue<MarketSample>();
        }
        private readonly Dictionary<string, Market> markets = new Dictionary<string, Market>(StringComparer.Ordinal);
        private readonly MarketSettings settings;
        private readonly uint seed;
        public double Progress { get; private set; }
        public double Elapsed { get; private set; }
        public long Revision { get; private set; }
        public long Updates { get; private set; }
        public double IntervalSeconds => settings.Interval;
        public double SecondsRemaining => Math.Max(0,settings.Interval-Progress);
        public MarketBook(MarketSettings settings, uint seed) { this.settings = settings; this.seed = seed; }
        private static uint Hash(string value, uint initial)
        { uint h = initial ^ 2166136261; foreach (char c in value) h = unchecked((h ^ c) * 16777619); return h == 0 ? 1 : h; }
        internal static uint SaveSeed(string folder, int gameSeed) => Hash(folder ?? "", unchecked((uint)gameSeed));
        public double Ensure(string identity, double basePrice)
        {
            identity=ObjectMarkets.Commodity(identity);
            if (!OreOrigins.Valid(identity) || identity == "Kerbin" || !MarketSettings.Finite(basePrice) || basePrice <= 0 || !MarketSettings.Finite(basePrice * 4) || basePrice * .4 <= 0) return 0;
            if (!markets.TryGetValue(identity, out Market m))
            {
                m = new Market { Identity = identity, Base = basePrice, Price = basePrice, Random = Hash(identity, seed) };
                markets.Add(identity, m); Revision++;
            }
            if (m.Base != basePrice)
            {
                m.Base = basePrice; m.Price = Clamp(m.Price, basePrice * settings.Minimum, basePrice * settings.Maximum); Revision++;
                // Config edits are explicit interventions. Historical recorded funds are never rewritten.
            }
            return m.Price;
        }
        // Do not splice incompatible class histories or stack their events. A new
        // commodity starts with no observations. Other records and economy RNG stay exact.
        internal int MigrateSpaceRocks(double baseline) {
            var old=new List<string>();
            foreach(var id in markets.Keys)if(id!=ObjectMarkets.Shared && ObjectMarkets.Find(id)!=null)old.Add(id);
            foreach(var id in old)markets.Remove(id);
            Ensure(ObjectMarkets.Shared,baseline);
            if(old.Count>0){Revision++;Log.Info("Shared space-rock market migration: retired="+old.Count+" class histories/events; new market has no fabricated history; clock/planetary records retained");}
            return old.Count;
        }
        public MarketSample[] History(string identity, double rangeSeconds = double.PositiveInfinity)
        {
            identity=ObjectMarkets.Commodity(identity);
            if (!markets.TryGetValue(identity, out Market m)) return Array.Empty<MarketSample>();
            var result = new List<MarketSample>();
            double cutoff = Elapsed - Math.Max(0, rangeSeconds);
            foreach (var item in m.History) if (item.Time > cutoff) result.Add(item);
            return result.ToArray();
        }
        public void Advance(double seconds)
        {
            if (!MarketSettings.Finite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
            if (!MarketSettings.Finite(Elapsed + seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
            Elapsed += seconds; Progress += seconds;
            while (Progress + 1e-9 >= settings.Interval)
            {
                Progress = Math.Max(0, Progress - settings.Interval); Updates++;
                double time = Elapsed - Progress;
                TickEvents();
                foreach (Market m in Ordered())
                {
                    double old = m.Price;
                    if (m.Remaining-- <= 0)
                    {
                        m.Trend = (int)(Next(m)*3)-1;
                        m.Remaining = settings.TrendMinimum + (int)(Next(m) * (settings.TrendMaximum - settings.TrendMinimum + 1));
                    }
                    double effect=m.Event==null?0:m.Event.Strength;
                    double ceiling=m.Event==null?settings.Maximum:m.Event.Ceiling;
                    bool negative=effect<0;
                    foreach(var e in m.Aftermaths) {effect+=e.Strength;negative|=e.Pressure<0;ceiling=Math.Max(ceiling,e.Ceiling);}
                    effect=Clamp(effect,-.1,.1);
                    if(Next(m)<.04) m.Trend=(int)(Next(m)*3)-1;
                    double volatility=(.75+Hash(m.Identity,seed)/4294967296.0*.5)*(m.Trend==0?.5:1);
                    m.Velocity = MarketDynamics.EventVelocity(old,m.Base,m.Velocity,m.Trend,Next(m),settings,effect, volatility*(1+Math.Abs(effect)*12), negative?.4:settings.Minimum, effect>0?ceiling:settings.Maximum);
                    m.Price = Clamp(old * (1 + m.Velocity), m.Base * .4, m.Base * 4);
                    // Both endpoints were observed prices. No intra-minute path or volume is invented.
                    m.History.Enqueue(new MarketSample(time, old, Math.Max(old, m.Price), Math.Min(old, m.Price), m.Price));
                    Prune(m,time);
                }
                Revision++;
            }
        }
        private void Prune(Market m,double now)
        {
            PruneMarkers(m,now);
            while (m.History.Count > 0 && (m.History.Count > Math.Min(1440,settings.HistoryLimit) || m.History.Peek().Time <= now-86400)) m.History.Dequeue();
        }
        private static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));
        private static double Next(Market m)
        { uint n = m.Random; n ^= n << 13; n ^= n >> 17; n ^= n << 5; m.Random = n; return n / 4294967296.0; }
        private static string N(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        public void Save(ConfigNode node)
        {
            node.AddValue("version", "4"); node.AddValue("eventRng", EventRandom.ToString(CultureInfo.InvariantCulture)); node.AddValue("seed", seed.ToString(CultureInfo.InvariantCulture)); node.AddValue("elapsed", N(Elapsed));
            node.AddValue("progress", N(Progress)); node.AddValue("updates", Updates.ToString(CultureInfo.InvariantCulture));
            var names = new List<string>(markets.Keys); names.Sort(StringComparer.Ordinal);
            foreach (string identity in names)
            {
                Market m = markets[identity]; var n = node.AddNode("BODY");
                n.AddValue("name", identity); n.AddValue("base", N(m.Base)); n.AddValue("price", N(m.Price));
                n.AddValue("velocity", N(m.Velocity)); n.AddValue("rng", m.Random.ToString(CultureInfo.InvariantCulture)); n.AddValue("trend", m.Trend.ToString(CultureInfo.InvariantCulture)); n.AddValue("remaining", m.Remaining.ToString(CultureInfo.InvariantCulture));
                SaveEvent(m,n);
                foreach (var p in m.History) n.AddValue("p", N(p.Time) + "," + N(p.Open) + "," + N(p.High) + "," + N(p.Low) + "," + N(p.Close));
            }
        }
        private static string One(ConfigNode n, string key)
        { var v = n.GetValues(key); if (v.Length != 1) throw new FormatException("Missing/duplicate market " + key); return v[0]; }
        private static double Number(ConfigNode n, string key)
        { if (!double.TryParse(One(n, key), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || !MarketSettings.Finite(v)) throw new FormatException("Invalid market " + key); return v; }
        public static MarketBook Load(ConfigNode node, MarketSettings settings)
        {
            string version=One(node,"version");
            if ((version != "1" && version != "2" && version != "3" && version != "4") || !uint.TryParse(One(node, "seed"), out uint seed)) throw new FormatException("Unsupported market version/seed");
            var book = new MarketBook(settings, seed) { Elapsed = Number(node, "elapsed"), Progress = Number(node, "progress") };
            if (book.Elapsed < 0 || book.Progress < 0 || book.Progress > book.Elapsed ||
                !long.TryParse(One(node, "updates"), out long updates) || updates < 0) throw new FormatException("Invalid market timer");
            // An intentional interval edit bounds saved progress to the new interval, without catch-up ticks.
            if (book.Progress >= settings.Interval) book.Progress = settings.Interval - 1e-6;
            book.Updates = updates;
            if(version!="1") { if(!uint.TryParse(One(node,"eventRng"),out book.eventRandom)||book.eventRandom==0) throw new FormatException("Invalid event RNG"); }
            foreach (ConfigNode n in node.GetNodes("BODY"))
            {
                string id = One(n, "name");
                if (!OreOrigins.Valid(id) || id == "Kerbin" || book.markets.ContainsKey(id)) throw new FormatException("Invalid/duplicate market identity");
                var m = new Market { Identity = id, Base = Number(n,"base"), Price = Number(n,"price"), Velocity = Number(n,"velocity") };
                if (m.Base <= 0 || !MarketSettings.Finite(m.Base * 4) || m.Price <= 0 || Math.Abs(m.Velocity) > .080001 ||
                    !uint.TryParse(One(n,"rng"), out m.Random) || m.Random == 0 || !int.TryParse(One(n,"trend"), out m.Trend) || Math.Abs((long)m.Trend) > 1 ||
                    !int.TryParse(One(n,"remaining"), out m.Remaining) || m.Remaining < 0 || m.Remaining > 10000) throw new FormatException("Invalid market process");
                if(m.Price<m.Base*.4 || m.Price>m.Base*4) throw new FormatException("Unsafe market price");
                if(version!="1") book.LoadEvent(m,n,version=="3"||version=="4");
                double last = -1;
                var samples = n.GetValues("p"); if (samples.Length > 10080) throw new FormatException("Oversized market history");
                foreach (string text in samples)
                {
                    string[] fields = text.Split(','); if (fields.Length != 5) throw new FormatException("Invalid market sample");
                    var values = new double[5];
                    for (int i=0; i<5; i++) if (!double.TryParse(fields[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i])) throw new FormatException("Invalid market sample value");
                    var p = new MarketSample(values[0], values[1], values[2], values[3], values[4]);
                    if (p.Time <= last || p.Time > book.Elapsed + 1e-6) throw new FormatException("Out-of-order market history");
                    last = p.Time; m.History.Enqueue(p); book.Prune(m,book.Elapsed);
                }
                book.markets.Add(id,m);
            }
            book.Revision = updates + 1;
            return book;
        }
    }
}
