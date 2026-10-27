using System;
using System.Linq;
using OreMarket;
using OreMarket.Terminal;
internal static class Events061Tests
{
    private static ConfigNode Save(MarketBook b) {var n=new ConfigNode();b.Save(n);return n;}
    private static string Dump(ConfigNode n)=>(n.name??"")+"{"+string.Join("|",n.values.Select(v=>v.name+"="+v.value))+string.Join("",n.nodes.Select(Dump))+"}";
    private static MarketSettings Forced(int tier)
    {
        var s=new MarketSettings();s.Events=new EventSettings {Ordinary=tier==0?1:0,Significant=tier==1?1:0,Major=tier==2?1:0,Extreme=tier==3?1:0,Concurrent=1};return s;
    }
    internal static void Run(Action<string,bool> check)
    {
        var e=new EconomicEvent {Pressure=.01,Remaining=0,Fade=600,FadeTotal=600};double previous=e.Strength;bool smooth=true;
        for(int i=1;i<=10;i++){e.Fade=600-i*60;double v=e.Strength;smooth&=v>=0&&v<previous;previous=v;}
        check("061: aftermath smoothstep pressure fades monotonically to zero",smooth&&previous==0);
        e.Fade=300;check("061: midpoint pressure is half and negative effects fade symmetrically",e.Strength==.005);
        e.Pressure=-.01;check("061: negative aftermath retains sign",e.Strength==-.005);
        var s=Forced(3);var b=new MarketBook(s,781);b.Ensure("Mun",40);b.Advance(60);
        var start=b.Markers("Mun",86400)[0];
        check("061: start marker records actual tick and affected Ore",start.Time==60&&!start.Ending&&start.Positive&&start.Scope=="Mun Ore");
        check("061: markers are isolated by selected origin",b.Markers("Duna",86400).Length==0);
        var snapshot=Save(b);snapshot.GetNodes("BODY")[0].GetNode("EVENT").SetValue("remaining","60",true);
        b=MarketBook.Load(snapshot,s);b.Ensure("Duna",75);b.Advance(60);
        check("061: expiry frees active slot while previous pressure fades",b.News().Count(n=>!n.Aftermath)==1&&b.News().Any(n=>n.Origin=="Mun"&&n.Aftermath)&&b.News().Any(n=>n.Origin=="Duna"&&!n.Aftermath));
        var markers=b.Markers("Mun",86400);
        check("061: expiry marker uses actual active tick and original event identity",markers.Length==2&&markers[1].Ending&&markers[1].Time==120&&markers[1].EventId==start.EventId);
        b.Advance(17.25);var saved=Save(b);var loaded=MarketBook.Load(saved,s);
        check("061: overlapping active/fading effects, markers and fractional timer round-trip exactly",Dump(saved)==Dump(Save(loaded))&&loaded.SecondsRemaining==42.75);
        bool deterministic=true;for(int i=0;i<200;i++){b.Advance(60);loaded.Advance(60);deterministic&=Dump(Save(b))==Dump(Save(loaded));}
        check("061: future event and marker continuation remains exact after quickload",deterministic);
        for(int tier=0;tier<4;tier++) {
            var cfg=Forced(tier);int min=tier==0?300:tier==1?480:600,max=tier==0?600:tier==1?720:900;bool valid=true;
            for(uint seed=1;seed<=100;seed++) {
                var test=new MarketBook(cfg,seed);test.Ensure("Mun",40);test.Advance(60);var v=Save(test).GetNodes("BODY")[0].GetNode("EVENT");
                double fade=double.Parse(v.GetValue("fade"),System.Globalization.CultureInfo.InvariantCulture);valid&=fade>=min&&fade<=max;
            }
            check("061: tier "+tier+" uses requested aftermath duration",valid);
        }
        // Old active state retains exact prices/process, without fabricated historical starts.
        var legacy=Save(new MarketBook(s,1));legacy.SetValue("version","2",true);
        var legacyActive=new MarketBook(s,3);legacyActive.Ensure("Mun",40);legacyActive.Advance(60);legacy=Save(legacyActive);legacy.SetValue("version","2",true);legacy.GetNodes("BODY")[0].RemoveNodes("MARKER");
        var migrated=MarketBook.Load(legacy,s);
        check("061: 0.6.0 active-event save keeps quote/RNG/timer and has no invented marker",migrated.Ensure("Mun",40)==legacyActive.Ensure("Mun",40)&&migrated.News()[0].EventId==legacyActive.News()[0].EventId&&migrated.Markers("Mun",86400).Length==0&&Save(migrated).GetNodes("BODY")[0].GetValue("rng")==legacy.GetNodes("BODY")[0].GetValue("rng"));
        legacy.GetNodes("BODY")[0].GetNode("EVENT").SetValue("remaining","0",true);
        migrated=MarketBook.Load(legacy,s);check("061: legacy stabilizing event migrates to separate aftermath",migrated.News().Length==1&&migrated.News()[0].Aftermath&&Save(migrated).GetNodes("BODY")[0].GetNodes("EVENT").Length==0);
        // Real observations, all modes and ranges; marker coordinates follow actual period times.
        foreach(MarketRange range in Enum.GetValues(typeof(MarketRange)))foreach(ChartMode mode in Enum.GetValues(typeof(ChartMode))) {
            var raw=Enumerable.Range(1,120).Select(i=>new MarketPeriod(i*60,40,41,39,40.5)).ToArray();
            var periods=ChartHistory.ForRange(raw,range);var geometry=new ChartGeometry(periods.Length,558,mode);
            bool aligned=true;for(int i=0;i<periods.Length;i++)aligned&=Math.Abs(EventMarkerGeometry.X(periods[i].End,periods,geometry,60)-geometry.X(i))<1e-8;
            check("061: marker endpoints align "+range+" / "+mode,aligned);
            check("061: marker hit test snaps within tolerance "+range+" / "+mode,EventMarkerGeometry.Nearest(12,new[]{10.0,30.0},4)==0&&EventMarkerGeometry.Nearest(22,new[]{10.0,30.0},4)==-1);
        }
        var grouped=ChartHistory.ForRange(Enumerable.Range(1,5).Select(i=>new MarketPeriod(i*60,1,1,1,1)).ToArray(),MarketRange.Hours6);
        var line=new ChartGeometry(1,558,ChartMode.Line);
        check("061: intraperiod marker uses real timestamp interpolation",Math.Abs(EventMarkerGeometry.X(120,grouped,line,60)-line.X(0)*.4)<1e-8);
        check("061: markers outside recorded chart extent are not fabricated",double.IsNaN(EventMarkerGeometry.X(301,grouped,line,60))&&double.IsNaN(EventMarkerGeometry.X(-1,grouped,line,60)));
        var tiny=MarketBook.Load(snapshot,s);tiny.Advance(86460);check("061: marker history rolls out after 24 active hours",tiny.Markers("Mun",86400).All(m=>m.Time>tiny.Elapsed-86400)&&Save(tiny).GetNodes("BODY")[0].GetNodes("MARKER").Length<=512);
        bool bounded=true,overlap=false;var many=new MarketBook(new MarketSettings(),415);for(int i=0;i<14;i++)many.Ensure("Body"+i,100);
        for(int i=0;i<10000;i++) {many.Advance(60);var news=many.News();bounded&=news.Count(n=>!n.Aftermath)<=3&&news.Length<=6;overlap|=news.Any(n=>n.Aftermath)&&news.Any(n=>!n.Aftermath);}
        check("061: active and total influence limits bound overlapping headlines",bounded&&overlap);
        check("061: tooltips explain recovery price, genuine OHLC and aftermath",TerminalHelp.CurrentPrice.Contains("recover")&&TerminalHelp.Candles.Contains("Open, High, Low and Close")&&TerminalHelp.Aftermath.Contains("do not reset"));
        check("061: change explanation identifies one-hour interval and short history",TerminalHelp.Change(new[]{new MarketPeriod(60,1,1,1,1)}).Contains("last hour")&&TerminalHelp.Change(new[]{new MarketPeriod(60,1,1,1,1)}).Contains("less history"));
        check("061: compact guide includes custody, career recovery and unlocked prices",TerminalHelp.Guide.Contains("compatible stock tanks")&&TerminalHelp.Guide.Contains("career mode")&&TerminalHelp.Guide.Contains("not locked")&&TerminalHelp.Guide.Contains("preloaded"));
        var bad=Save(legacyActive);var marker=bad.GetNodes("BODY")[0].GetNode("MARKER");marker.SetValue("time","9999999",true);bool rejected=false;
        try{MarketBook.Load(bad,s);}catch(FormatException){rejected=true;}check("061: corrupt future marker fails closed",rejected);
    }
}
