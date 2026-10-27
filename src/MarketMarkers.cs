using System;
using System.Collections.Generic;
using System.Globalization;
namespace OreMarket
{
    public sealed class MarketEventMarker
    {
        public string EventId {get;internal set;}
        public string Headline {get;internal set;}
        public string Scope {get;internal set;}
        public double Time {get;internal set;}
        public bool Positive {get;internal set;}
        public bool Ending {get;internal set;}
        internal int Kind;
    }
    public sealed partial class MarketBook
    {
        private static void RecordMarker(Market m,EconomicEvent e,double time,bool ending)
        {
            m.Markers.Add(new MarketEventMarker {EventId=e.Id,Kind=e.Tier==3&&e.Kind==7?8:e.Kind,Headline=EventHeadline(e.Kind,e.Tier),Scope=e.Scope,Time=time,Positive=e.Pressure>0,Ending=ending});
            PruneMarkers(m,time);
        }
        private static void PruneMarkers(Market m,double now)
        {
            int count=0;while(count<m.Markers.Count&&m.Markers[count].Time<=now-86400)count++;
            if(count>0)m.Markers.RemoveRange(0,count);
            if(m.Markers.Count>512)m.Markers.RemoveRange(0,m.Markers.Count-512);
        }
        public MarketEventMarker[] Markers(string origin,double seconds)
        {
            if(!markets.TryGetValue(origin,out Market m))return Array.Empty<MarketEventMarker>();
            return m.Markers.FindAll(p=>p.Time>Elapsed-Math.Min(86400,Math.Max(0,seconds))).ToArray();
        }
        private static void SaveMarkers(Market m,ConfigNode n)
        {
            foreach(var p in m.Markers) {
                var v=n.AddNode("MARKER");v.AddValue("id",p.EventId);v.AddValue("kind",p.Kind.ToString(CultureInfo.InvariantCulture));v.AddValue("scope",p.Scope);
                v.AddValue("time",N(p.Time));v.AddValue("positive",p.Positive?"True":"False");v.AddValue("ending",p.Ending?"True":"False");
            }
        }
        private void LoadMarkers(Market m,ConfigNode n)
        {
            var nodes=n.GetNodes("MARKER");if(nodes.Length>512)throw new FormatException("Oversized event marker history");
            double last=-1;var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(var v in nodes) {
                double kind=Number(v,"kind"),time=Number(v,"time");string id=One(v,"id"),scope=One(v,"scope");
                if(kind<0||kind>8||kind!=Math.Truncate(kind)||time<last||time<0||time>Elapsed+1e-6||id.Length==0||id.Length>320||scope.Length>512||
                    !bool.TryParse(One(v,"positive"),out bool positive)||!bool.TryParse(One(v,"ending"),out bool ending)||!seen.Add(id+":"+ending))throw new FormatException("Invalid event marker");
                last=time;m.Markers.Add(new MarketEventMarker {EventId=id,Kind=(int)kind,Headline=Headlines[(int)kind],Scope=scope,Time=time,Positive=positive,Ending=ending});
            }
            PruneMarkers(m,Elapsed);
        }
    }
}
