using System;
using System.Collections.Generic;
namespace OreMarket.Terminal
{
    internal sealed class MonitorState
    {
        internal bool Compact {get;private set;}
        internal void SetCompact(bool value) {Compact=value;}
    }
    internal static class MonitorLayout
    {
        internal const float Width=380,Height=260;
        internal static double FitScale(bool compact,double requested,double width,double height)
        {
            if(!compact)return TerminalSession.FitScale(requested,width,height);
            if(double.IsNaN(requested)||double.IsInfinity(requested))requested=1;
            return Math.Max(.1,Math.Min(Math.Max(.75,Math.Min(1.75,requested)),Math.Min((width-24)/Width,(height-24)/Height)));
        }
    }
    internal static class MonitorHud
    {
        internal const float BackgroundAlpha=.035f;
        internal const uint Text=0xF4F4F4, Secondary=0xDEDEDE, Rise=0x52C282, Fall=0xD95F64;
    }
    internal sealed class MonitorEvent
    {
        internal readonly string Text,Hint;
        internal readonly bool Positive;
        internal MonitorEvent(string origin,MarketNews item)
        {
            Text=Abbreviation(item.Headline)+" "+(item.Positive?"+":"-")+MarketQuotes.ShortLabel(origin);
            Hint=item.Headline+" / "+item.Scope+" / "+TerminalHelp.EventTime(item.Remaining,item.Aftermath);
            Positive=item.Positive;
        }
        private static string Abbreviation(string headline)
        {
            switch(headline) {
                case "Industrial expansion":return "IE";
                case "Construction project":return "CP";
                case "Mining surplus":return "MS";
                case "Supply shortage":return "SS";
                case "Refinery shutdown":return "RS";
                case "Logistics disruption":return "LD";
                case "Rare resource crisis":return "RC";
                case "Exceptional demand boom":return "EB";
                case "Interplanetary Resource Crisis":return "IRC";
                default:return "EV"; // Generic event, never guess an unknown event's meaning.
            }
        }
        // Input is the authoritative selected-origin snapshot; do not infer membership from display strings.
        internal static MonitorEvent[] Select(string origin,MarketNews[] exactMembers,int limit=2)
        {
            if(string.IsNullOrEmpty(origin)||exactMembers==null||limit<=0)return Array.Empty<MonitorEvent>();
            var active=new List<MarketNews>();foreach(var item in exactMembers)if(!item.Aftermath)active.Add(item);
            active.Sort((a,b)=> {int tier=b.Tier.CompareTo(a.Tier);return tier!=0?tier:string.CompareOrdinal(a.EventId,b.EventId);});
            var result=new MonitorEvent[Math.Min(limit,active.Count)];for(int i=0;i<result.Length;i++)result[i]=new MonitorEvent(origin,active[i]);return result;
        }
    }
}
