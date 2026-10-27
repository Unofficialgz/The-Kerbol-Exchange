using System;
using System.Collections.Generic;
namespace OreMarket
{
    public sealed partial class MarketBook
    {
        // Read-only exact membership snapshot, including legacy events with no chart markers.
        public MarketNews[] NewsFor(string origin)
        {
            if(string.IsNullOrEmpty(origin)||(eligible!=null&&!eligible.Contains(origin))||!markets.TryGetValue(origin,out Market m))return Array.Empty<MarketNews>();
            var result=new List<MarketNews>();if(m.Event!=null)result.Add(NewsItem(m,m.Event,Progress));
            foreach(var e in m.Aftermaths)result.Add(NewsItem(m,e,Progress));return result.ToArray();
        }
    }
}
