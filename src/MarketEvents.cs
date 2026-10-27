using System;
using System.Collections.Generic;
using System.Globalization;
namespace OreMarket
{
    internal sealed class EventSettings
    {
        internal double Ordinary=.02, Significant=.004, Major=.003, Extreme=.0035;
        internal int Concurrent=3;
        private static int Concurrency(ConfigNode n) { double v=MarketSettings.Field(n,"maximumConcurrentEvents",3,1,20);if(v!=Math.Truncate(v))throw new FormatException("Non-integer event concurrency");return (int)v; }
        internal static EventSettings Read(ConfigNode n)
        {
            var settings=new EventSettings {
                Ordinary=MarketSettings.Field(n,"ordinaryEventChance",.02,0,1),
                Significant=MarketSettings.Field(n,"significantBoomChance",.004,0,1),
                Major=MarketSettings.Field(n,"majorBoomChance",.003,0,1),
                Extreme=MarketSettings.Field(n,"extremeBoomChance",.0035,0,1),
                Concurrent=Concurrency(n) };
            if(settings.Ordinary+settings.Significant+settings.Major>1)throw new FormatException("Event weights exceed one");
            return settings;
        }
    }
    public sealed class MarketNews
    {
        public string Origin {get; internal set;}
        public string EventId {get; internal set;}
        public int Tier {get; internal set;}
        public string Severity => Tier==3?"EXTREME":Tier==2?"MAJOR":Tier==1?"SIGNIFICANT":"ORDINARY";
        public string Headline {get; internal set;}
        public string Description {get; internal set;}
        public string Scope {get; internal set;}
        public double Remaining {get; internal set;}
        public bool Positive {get; internal set;}
        public bool Aftermath {get; internal set;}
    }
    internal sealed class EconomicEvent
    {
        internal int Kind, Tier;
        internal string Scope, Id;
        internal double Remaining, Fade, FadeTotal, Pressure, Ceiling;
        internal double Strength {
            get { double t=Math.Max(0,Math.Min(1,Fade/FadeTotal));return Pressure*(Remaining>0?1:t*t*(3-2*t)); }
        }
    }
    public sealed partial class MarketBook
    {
        private uint eventRandom;
        private HashSet<string> eligible;
        internal void SetEligibleOrigins(IEnumerable<string> origins) {eligible=new HashSet<string>(origins,StringComparer.Ordinal);}
        private uint EventRandom { get { if(eventRandom==0) eventRandom=Hash("ECONOMY",seed); return eventRandom; } }
        private double GlobalNext() { uint n=EventRandom;n^=n<<13;n^=n>>17;n^=n<<5;eventRandom=n;return n/4294967296.0; }
        private List<Market> Ordered() { var keys=new List<string>(markets.Keys);keys.Sort(StringComparer.Ordinal);var result=new List<Market>(keys.Count);foreach(var key in keys)if(eligible==null||eligible.Contains(key))result.Add(markets[key]);return result; }
        public void SetSystem(string origin,string system) { if(markets.TryGetValue(origin,out Market m)) m.System=system; }
        private static readonly string[] Headlines={"Industrial expansion","Construction project","Mining surplus","Supply shortage","Refinery shutdown","Logistics disruption","Rare resource crisis","Exceptional demand boom","Interplanetary Resource Crisis"};
        private static readonly string[] Descriptions={"Industrial buyers need more Ore.","A large construction project needs more Ore.","Extra mining supply is pushing prices down.","Ore supplies are running short.","Refinery closures have reduced Ore demand.","Shipping delays have reduced local Ore demand.","Buyers are competing for scarce Ore supplies.","Industrial Ore demand has surged.","Ore shortages across the system have driven up demand."};
        internal static string EventHeadline(int kind,int tier) => Headlines[tier==3&&kind==7?8:kind];
        internal static string TierName(int tier) => tier==3?"EXTREME":tier==2?"MAJOR":tier==1?"SIGNIFICANT":"ORDINARY";
        private static MarketNews NewsItem(Market m,EconomicEvent e,double progress) => new MarketNews {
            EventId=e.Id,Tier=e.Tier,Origin=m.Identity,Headline=EventHeadline(e.Kind,e.Tier),Description=Descriptions[e.Kind],Scope=e.Scope,
            Remaining=Math.Max(0,(e.Remaining>0?e.Remaining:e.Fade)-progress),Positive=e.Pressure>0,Aftermath=e.Remaining<=0 };
        public MarketNews[] News()
        {
            var result=new List<MarketNews>();var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(var m in Ordered()) {
                if(m.Event!=null&&seen.Add(m.Event.Id))result.Add(NewsItem(m,m.Event,Progress));
                foreach(var e in m.Aftermaths)if(seen.Add(e.Id))result.Add(NewsItem(m,e,Progress));
            }
            result.Sort((a,b)=>a.Aftermath.CompareTo(b.Aftermath));return result.ToArray();
        }
        private static bool Available(Market m) => m.Event==null && m.Cooldown<=0 && m.Aftermaths.Count<2;
        private void TickEvents()
        {
            var list=Ordered();var occupied=new HashSet<string>(StringComparer.Ordinal);var all=new HashSet<string>(StringComparer.Ordinal);
            double time=Elapsed-Progress;
            foreach(var m in list) {
                m.Cooldown=Math.Max(0,m.Cooldown-settings.Interval);
                for(int i=m.Aftermaths.Count-1;i>=0;i--) {
                    var fade=m.Aftermaths[i];fade.Fade=Math.Max(0,fade.Fade-settings.Interval);
                    if(fade.Fade<=0)m.Aftermaths.RemoveAt(i);else all.Add(fade.Id);
                }
                var e=m.Event;if(e==null)continue;
                e.Remaining=Math.Max(0,e.Remaining-settings.Interval);
                if(e.Remaining<=0) {
                    RecordMarker(m,e,time,true);m.Event=null;m.Aftermaths.Add(e);
                    m.Cooldown=600+Next(m)*600;
                } else occupied.Add(e.Id);
                all.Add(e.Id);
            }
            int count=occupied.Count,total=all.Count;
            // Active slots are released at expiry. A separate combined cap bounds fading headlines.
            int totalLimit=settings.Events.Concurrent+3;
            double global=GlobalNext();
            if(global<settings.Events.Extreme && count<settings.Events.Concurrent && total<totalLimit) {
                var candidates=list.FindAll(Available);
                if(candidates.Count>0) {Start(candidates[(int)(GlobalNext()*candidates.Count)],3,8);count++;total++;}
            }
            foreach(var m in list) {
                if(!Available(m)||count>=settings.Events.Concurrent||total>=totalLimit)continue;
                double roll=Next(m);int tier=roll<settings.Events.Major?2:roll<settings.Events.Major+settings.Events.Significant?1:roll<settings.Events.Major+settings.Events.Significant+settings.Events.Ordinary?0:-1;
                if(tier<0)continue;
                int kind=tier==0?(int)(Next(m)*6):tier==2?6:7;
                Start(m,tier,kind);count++;total++;
                if(tier==0 && m.System!=null && Next(m)<.2) {
                    var peers=list.FindAll(p=>p.System==m.System&&p!=m);
                    if(peers.Count>0 && peers.TrueForAll(Available)) {
                        m.Event.Scope=m.System+" system";
                        m.Markers[m.Markers.Count-1].Scope=m.Event.Scope;
                        foreach(var peer in peers) {
                            var e=m.Event;peer.Event=new EconomicEvent {Id=e.Id,Kind=e.Kind,Tier=e.Tier,Scope=e.Scope,Remaining=e.Remaining,Fade=e.Fade,FadeTotal=e.FadeTotal,Pressure=e.Pressure,Ceiling=e.Ceiling};
                            RecordMarker(peer,peer.Event,time,false);
                        }
                    }
                }
            }
        }
        private void Start(Market m,int tier,int kind)
        {
            int[] minimum={10,15,20,25}, maximum={20,30,40,45};
            double minutes=minimum[tier]+(int)(Next(m)*(maximum[tier]-minimum[tier]+1));
            double target=tier==0?.10+Next(m)*.15:tier==1?.15+Next(m)*.15:tier==2?.5+Next(m)*.5:1+Next(m)*1.2;
            bool positive=kind!=2&&kind!=4&&kind!=5;
            int fadeMin=tier==0?5:tier==1?8:10,fadeMax=tier==0?10:tier==1?12:15;
            double fade=(fadeMin+(int)(Next(m)*(fadeMax-fadeMin+1)))*60;
            m.Event=new EconomicEvent {Id=Updates.ToString(CultureInfo.InvariantCulture)+":"+m.Identity,Kind=kind,Tier=tier,Scope=MarketQuotes.Label(m.Identity),Remaining=minutes*60,Fade=fade,FadeTotal=fade,
                Pressure=(positive?1:-1)*Math.Log(1+target)/minutes*(1-settings.Momentum),Ceiling=tier==3?4:tier==2?3.5:3};
            RecordMarker(m,m.Event,Elapsed-Progress,false);
        }
        private static void SaveEvent(Market m,ConfigNode n)
        {
            n.AddValue("cooldown",N(m.Cooldown));if(m.System!=null)n.AddValue("system",m.System);
            if(m.Event!=null) WriteEvent(m.Event,n.AddNode("EVENT"));
            foreach(var e in m.Aftermaths)WriteEvent(e,n.AddNode("AFTERMATH"));
            SaveMarkers(m,n);
        }
        private static void WriteEvent(EconomicEvent e,ConfigNode v)
        {
            v.AddValue("id",e.Id);v.AddValue("kind",e.Kind.ToString(CultureInfo.InvariantCulture));v.AddValue("tier",e.Tier.ToString(CultureInfo.InvariantCulture));v.AddValue("scope",e.Scope);
            v.AddValue("remaining",N(e.Remaining));v.AddValue("fade",N(e.Fade));v.AddValue("fadeTotal",N(e.FadeTotal));v.AddValue("pressure",N(e.Pressure));v.AddValue("ceiling",N(e.Ceiling));
        }
        private void LoadEvent(Market m,ConfigNode n,bool modern)
        {
            m.Cooldown=Number(n,"cooldown");if(m.Cooldown<0||m.Cooldown>1200)throw new FormatException("Invalid event cooldown");
            var systems=n.GetValues("system");if(systems.Length>1)throw new FormatException("Duplicate market system");m.System=systems.Length==1?systems[0]:null;if(m.System!=null&&!OreOrigins.Valid(m.System))throw new FormatException("Invalid market system");
            var nodes=n.GetNodes("EVENT");if(nodes.Length>1)throw new FormatException("Duplicate event");if(nodes.Length==1) {
                var e=ReadEvent(nodes[0]);if(e.Remaining>0)m.Event=e;else m.Aftermaths.Add(e);
            }
            var fades=n.GetNodes("AFTERMATH");if(fades.Length+m.Aftermaths.Count>2)throw new FormatException("Excessive aftermath overlap");
            foreach(var v in fades) {var e=ReadEvent(v);if(e.Remaining!=0||e.Fade<=0)throw new FormatException("Invalid aftermath");m.Aftermaths.Add(e);}
            if(m.Event!=null&&m.Aftermaths.Count>=2)throw new FormatException("Excessive concurrent influence on market");
            var identities=new HashSet<string>(StringComparer.Ordinal);
            if(m.Event!=null)identities.Add(m.Event.Id);
            foreach(var e in m.Aftermaths)if(!identities.Add(e.Id))throw new FormatException("Duplicate event influence");
            if(modern)LoadMarkers(m,n);
        }
        private static EconomicEvent ReadEvent(ConfigNode v)
        {
            double kind=Number(v,"kind"),tier=Number(v,"tier");
            var e=new EconomicEvent {Id=One(v,"id"),Kind=(int)kind,Tier=(int)tier,Scope=One(v,"scope"),Remaining=Number(v,"remaining"),Fade=Number(v,"fade"),FadeTotal=Number(v,"fadeTotal"),Pressure=Number(v,"pressure"),Ceiling=Number(v,"ceiling")};
            if(e.Id.Length==0||e.Id.Length>320||kind!=e.Kind||tier!=e.Tier||kind<0||kind>8||(kind==8&&tier!=3)||tier<0||tier>3||e.Remaining<0||e.Remaining>2700||e.Fade<0||e.Fade>e.FadeTotal||e.FadeTotal<300||e.FadeTotal>900||Math.Abs(e.Pressure)>.1||e.Ceiling<2.5||e.Ceiling>4||e.Scope.Length>512)throw new FormatException("Invalid economic event");
            return e;
        }
    }
}
