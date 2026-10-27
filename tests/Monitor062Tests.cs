using System;
using System.Linq;
using OreMarket;
using OreMarket.Terminal;
internal static class Monitor062Tests
{
    internal static void Run(Action<string,bool> check)
    {
        var monitor=new MonitorState();var session=new TerminalSession();var market=new Commodity("Duna",75,75);
        session.Select(market);
        foreach(ChartMode mode in Enum.GetValues(typeof(ChartMode)))foreach(MarketRange range in Enum.GetValues(typeof(MarketRange))) {
            session.Mode=mode;session.Range=range;monitor.SetCompact(true);monitor.SetCompact(false);
            check("062: minimize/MAX preserves market, mode and range "+mode+" / "+range,session.Selection==market&&session.Selected=="Duna"&&session.Mode==mode&&session.Range==range&&!monitor.Compact);
        }
        check("062: compact default dimensions stay small",MonitorLayout.Width==380&&MonitorLayout.Height==260);
        check("062: compact scale remains readable on small screens",MonitorLayout.FitScale(true,1,800,600)==1&&MonitorLayout.FitScale(false,1,800,600)<1);
        check("062: oversized UI scale fits compact viewport",MonitorLayout.FitScale(true,3,640,480)*MonitorLayout.Width<=616);
        string[] headlines={"Industrial expansion","Construction project","Mining surplus","Supply shortage","Refinery shutdown","Logistics disruption","Rare resource crisis","Exceptional demand boom"};
        string[] expected={"IE","CP","MS","SS","RS","LD","RC","EB"};
        for(int i=0;i<headlines.Length;i++) {
            bool positive=i!=2&&i!=4&&i!=5;
            var item=new MarketNews {Headline=headlines[i],Scope="Duna system",Remaining=125,Positive=positive,Tier=i==7?3:0,EventId="test"};
            var result=MonitorEvent.Select("Duna",new[]{item});
            check("062: truthful event abbreviation "+headlines[i],result.Length==1&&result[0].Text==expected[i]+(positive?" +":" -")+"Duna"&&result[0].Positive==positive&&result[0].Hint.Contains(headlines[i])&&result[0].Hint.Contains("Duna system")&&result[0].Hint.Contains("02:05 active remaining"));
        }
        var events=new[]{new MarketNews{Headline="Mining surplus",EventId="a",Tier=0},new MarketNews{Headline="Exceptional demand boom",EventId="b",Tier=3,Positive=true},new MarketNews{Headline="Rare resource crisis",EventId="c",Tier=2,Positive=true},new MarketNews{Headline="Construction project",EventId="d",Tier=3,Aftermath=true}};
        var selected=MonitorEvent.Select("Tylo",events);
        check("062: strongest active events take limited slots",selected.Length==2&&selected[0].Text=="EB +Tylo"&&selected[1].Text=="RC +Tylo");
        check("062: no fabricated active events for empty or aftermath-only state",MonitorEvent.Select("Mun",Array.Empty<MarketNews>()).Length==0&&MonitorEvent.Select("Mun",new[]{events[3]}).Length==0);
        var settings=new MarketSettings();settings.Events=new EventSettings {Ordinary=1,Significant=0,Major=0,Extreme=0,Concurrent=1};bool systemFound=false;
        for(uint seed=1;seed<100&&!systemFound;seed++) {
            var book=new MarketBook(settings,seed);book.Ensure("Duna",75);book.Ensure("Ike",65);book.SetSystem("Duna","Duna");book.SetSystem("Ike","Duna");book.Advance(60);
            if(book.News()[0].Scope=="Duna system") {
                systemFound=true;var state=new ConfigNode();book.Save(state);string before=state.GetValue("eventRng");
                check("062: system membership query includes actual moon without relying on representative origin",book.NewsFor("Ike").Length==1&&book.NewsFor("Duna").Length==1&&book.NewsFor("Mun").Length==0);
                var legacy=new ConfigNode();book.Save(legacy);legacy.SetValue("version","2",true);foreach(var body in legacy.GetNodes("BODY"))body.RemoveNodes("MARKER");
                var old=MarketBook.Load(legacy,settings);
                check("062: legacy system events need no marker history for correct membership",old.NewsFor("Ike").Length==1&&old.Markers("Ike",86400).Length==0);
                for(int i=0;i<50;i++)book.NewsFor("Ike");var after=new ConfigNode();book.Save(after);
                check("062: event observation never changes timer, price or random state",book.Elapsed==60&&book.Updates==1&&after.GetValue("eventRng")==before&&state.GetNodes("BODY")[0].GetValue("rng")==after.GetNodes("BODY")[0].GetValue("rng"));
            }
        }
        check("062: exact system membership fixture exercised",systemFound);
    }
}
