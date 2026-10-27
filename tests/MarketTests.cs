using System;
using System.Linq;
using OreMarket;
using OreMarket.Terminal;
using KSP.UI.Screens;

internal static class MarketTests
{
    private static ConfigNode Save(MarketBook b) { var n=new ConfigNode(); b.Save(n); return n; }
    internal static void Run(Action<string,bool> check)
    {
        var shipped=MarketSettings.Read(PricingTests.Shipped().GetNodes("MARKET"));
        check("market config: shipped interval, bounds and retention load",shipped.Interval==60&&shipped.Minimum==.65&&shipped.Maximum==2.5&&shipped.HistoryLimit==1440);
        var settingNode=new ConfigNode();settingNode.AddValue("intervalSeconds","30");settingNode.AddValue("historyLimit","64");
        var configured=MarketSettings.Read(new[]{settingNode});check("market config: optional configuration overrides defaults",configured.Interval==30&&configured.HistoryLimit==64);
        var custom=new MarketBook(configured,1);custom.Ensure("Mun",40);custom.Advance(30);check("market config: configured interval actually controls ticks",custom.Updates==1);
        settingNode.SetValue("intervalSeconds","0",true);bool badConfig=false;try{MarketSettings.Read(new[]{settingNode});}catch(FormatException){badConfig=true;}
        check("market config: invalid intervals are rejected",badConfig);
        var settings=new MarketSettings();
        var book=new MarketBook(settings,123);
        check("market: origins begin at independent configured bases",book.Ensure("Mun",40)==40&&book.Ensure("Duna",75)==75&&book.Ensure("Kerbin",100)==0);
        book.Advance(59.5); check("market: no update before exactly sixty active seconds",book.Updates==0&&book.Ensure("Mun",40)==40&&book.Progress==59.5);
        book.Advance(.5); check("market: exactly one update at sixty seconds",book.Updates==1&&book.Progress==0&&book.History("Mun").Length==1);
        double live=book.Ensure("Mun",40); var first=book.History("Mun")[0];
        check("market: OHLC contains only observed endpoint prices",first.Open==40&&first.Close==live&&first.High==Math.Max(40,live)&&first.Low==Math.Min(40,live)&&first.Time==60);
        book.Advance(120);check("market: missed active frames record precisely two further intervals",book.Updates==3&&book.History("Mun").Length==3&&book.History("Mun")[1].Time==120);
        var clock=new ActiveMarketClock();
        check("clock: initial sample cannot charge previous loading time",clock.Sample(100,true)==0);
        check("clock: real seconds accrue independently of any UT value",clock.Sample(115,true)==15);
        check("clock: entering pause excludes boundary sample",clock.Sample(116,false)==0);
        check("clock: paused and first resumed samples exclude whole pause gap",clock.Sample(500,false)==0&&clock.Sample(1000,true)==0&&clock.Sample(1001,true)==1);
        var restored=MarketBook.Load(Save(book),settings);
        check("market: save/load retains price, history, elapsed and fractional timer",restored.Ensure("Mun",40)==book.Ensure("Mun",40)&&restored.Elapsed==book.Elapsed&&restored.Progress==book.Progress&&restored.History("Mun").Last().Close==book.History("Mun").Last().Close);
        bool deterministic=true,bounded=true,smooth=true,independent=false; int risingRuns=0,run=0; double previous=book.Ensure("Mun",40), previousDuna=book.Ensure("Duna",75);
        for(int i=0;i<3000;i++)
        {
            book.Advance(60);restored.Advance(60);
            double p=book.Ensure("Mun",40), q=restored.Ensure("Mun",40), d=book.Ensure("Duna",75);
            deterministic &= p==q; bounded &= p>=16&&p<=160&&d>=30&&d<=300;
            smooth &= Math.Abs(p/previous-1)<=.080000001;
            independent |= Math.Abs(p/previous-d/previousDuna)>.000001;
            if(p>previous) { run++; risingRuns=Math.Max(run,risingRuns); } else run=0;
            previous=p;previousDuna=d;
        }
        check("market: thousands of saved RNG continuations are identical after reload",deterministic);
        check("market: Mun and Duna remain inside forty to four hundred percent",bounded);
        check("market: each update changes at most eight percent",smooth);
        check("market: each origin follows its own process",independent);
        check("market: persistent trends can last several updates",risingRuns>=5);
        var limitedSettings=new MarketSettings {HistoryLimit=3}; var limited=new MarketBook(limitedSettings,4);limited.Ensure("Mun",40);limited.Advance(600);
        check("market: oldest history is pruned without resetting price",limited.History("Mun").Length==3&&limited.History("Mun")[0].Time==480&&limited.Updates==10);
        check("market: time-range selection uses actual active timestamps",limited.History("Mun",60).Length==1&&limited.History("Mun",0).Length==0);
        var partial=new MarketBook(settings,4);partial.Ensure("Mun",40);partial.Advance(47.25);var partialCopy=MarketBook.Load(Save(partial),settings);partialCopy.Advance(12.75);
        check("market: fractional timer survives save and next interval",partialCopy.Updates==1&&partialCopy.Progress==0&&partialCopy.Elapsed==60);
        var other=new MarketBook(settings,5);other.Ensure("Mun",40);other.Advance(60);
        var solo=new MarketBook(settings,123); var withExtra=new MarketBook(settings,123);
        solo.Ensure("Mun",40);withExtra.Ensure("Mun",40);withExtra.Ensure("Extra",10);solo.Advance(600);withExtra.Advance(600);
        check("market: adding another eligible origin cannot reroll existing paths",solo.Ensure("Mun",40)==withExtra.Ensure("Mun",40));
        check("market: independent save seeds have independent prices",other.Ensure("Mun",40)!=partialCopy.Ensure("Mun",40));
        check("market: malformed identities and unsafe prices cannot create markets",other.Ensure("Kerbin",40)==0&&other.Ensure(null,40)==0&&other.Ensure("Huge",double.MaxValue)==0);
        var malformed=Save(partial);malformed.SetValue("progress","NaN",true);bool rejected=false;try{MarketBook.Load(malformed,settings);}catch(FormatException){rejected=true;}
        check("market: corrupt timer is rejected rather than rerolled",rejected);
        var display=new Commodity("Mun",45.5,40);
        check("market UI: live price and configured base remain separate",display.PriceText=="45.5 funds / unit"&&display.BasePriceText=="40 funds / unit"&&display.HundredText=="4,550.00 funds");
        var periods=ChartHistory.Convert(limited.History("Mun"));
        var watch=WatchlistSnapshot.Create(periods.Last().Close,periods);
        check("history: change uses actual first open and latest close",watch.Direction==periods.Last().Close.CompareTo(periods[0].Open)&&watch.Samples[0]==periods[0].Open&&watch.Samples.Last()==periods.Last().Close);
        var currentChange=WatchlistSnapshot.Create(periods.Last().Close*1.1,periods);
        check("history: live quote changes never rewrite recorded sparkline samples",currentChange.Samples.Last()==periods.Last().Close&&currentChange.Direction==(periods.Last().Close*1.1).CompareTo(periods[0].Open));
        check("history: active-time labels preserve minute and day boundaries",ChartHistory.Timestamp(60)=="00:01:00"&&ChartHistory.Timestamp(86460)=="1d 00:01:00");
        check("history: live mini-chart uses cached observed series",ChartRaster.Sparkline(watch,65,12).Any(pixel=>pixel!=0));
        check("history: unobserved live market is labelled New rather than Fixed",WatchlistSnapshot.Create(100,new MarketPeriod[0],true).ChangeText=="New");
        var initialWatch=WatchlistSnapshot.Create(100,new MarketPeriod[0]);
        check("history: initial neutral reference invents no samples",initialWatch.Samples.Length==0&&ChartRaster.Sparkline(initialWatch,65,12).Count(pixel=>pixel!=0)==65);
        foreach(ChartMode mode in Enum.GetValues(typeof(ChartMode)))
        {
            var image=new ChartRaster(periods,mode,120,60);
            check("history: actual data renders chart mode "+mode,image.Pixels.Length==7200&&image.Pixels.Any(p=>p!=0)&&image.Minimum<=periods.Min(p=>p.Low)&&image.Maximum>=periods.Max(p=>p.High));
        }
        var extreme=new ChartRaster(new[]{new MarketPeriod(0,1e308,1.797e308,1e308,1.797e308)},ChartMode.Candle,64,32);
        check("history: finite extreme configured quotes cannot overflow chart bounds",MarketSettings.Finite(extreme.Minimum)&&MarketSettings.Finite(extreme.Maximum)&&extreme.Pixels.Any(pixel=>pixel!=0));
        var aggregated=ChartHistory.ForRange(ChartHistory.Convert(book.History("Mun")),MarketRange.Hours24);
        var original=book.History("Mun");
        check("history: chart aggregation preserves real OHLC extrema and endpoints",aggregated.Length<=100&&aggregated[0].Open==original[0].Open&&aggregated.Last().Close==original.Last().Close&&aggregated.Max(p=>p.High)==original.Max(p=>p.High)&&aggregated.Min(p=>p.Low)==original.Min(p=>p.Low));
        check("history: ranges have exact active fifteen-minute/hour/six-hour/day durations",ChartHistory.RangeSeconds(MarketRange.Minutes15)==900&&ChartHistory.RangeSeconds(MarketRange.Hour1)==3600&&ChartHistory.RangeSeconds(MarketRange.Hours6)==21600&&ChartHistory.RangeSeconds(MarketRange.Hours24)==86400);
        check("history: absent data does not generate chart pixels",new ChartRaster(new MarketPeriod[0],ChartMode.Candle,120,60).Pixels.All(p=>p==0));
        check("history: requested candle durations are fixed by range",ChartHistory.CandleSeconds(MarketRange.Minutes15)==60&&ChartHistory.CandleSeconds(MarketRange.Hour1)==60&&ChartHistory.CandleSeconds(MarketRange.Hours6)==300&&ChartHistory.CandleSeconds(MarketRange.Hours24)==900);
        var fixture=Enumerable.Range(0,1440).Select(i=>new MarketPeriod((i+1)*60,100+i,110+i,90+i,102+i)).ToArray();
        foreach(var range in new[]{MarketRange.Minutes15,MarketRange.Hour1,MarketRange.Hours6,MarketRange.Hours24})
        {
            var visible=fixture.Where(p=>p.Start>86400-ChartHistory.RangeSeconds(range)).ToArray();
            var grouped=ChartHistory.ForRange(visible,range);
            int expected=range==MarketRange.Minutes15?15:range==MarketRange.Hour1?60:range==MarketRange.Hours6?72:96;
            check("history: complete range has exactly requested bars "+range,grouped.Length==expected);
            check("history: range grouping preserves real endpoints and extrema "+range,grouped[0].Open==visible[0].Open&&grouped.Last().Close==visible.Last().Close&&grouped.Max(p=>p.High)==visible.Max(p=>p.High)&&grouped.Min(p=>p.Low)==visible.Min(p=>p.Low));
        }
        var six=ChartHistory.ForRange(fixture.Take(6).ToArray(),MarketRange.Hours6);
        check("history: five-minute buckets use first open, max high, min low and last close",six.Length==2&&six[0].Open==100&&six[0].High==114&&six[0].Low==90&&six[0].Close==106&&six[1].Open==105&&six[1].Close==107);
        check("history: short early history creates a real partial fifteen-minute candle",ChartHistory.ForRange(fixture.Take(3).ToArray(),MarketRange.Hours24).Length==1);
        check("history: gaps are not populated with invented candles",ChartHistory.ForRange(new[]{fixture[0],fixture[10]},MarketRange.Hours6).Length==2);
        var shifted=ChartHistory.ForRange(fixture.Skip(2).Take(4).ToArray(),MarketRange.Hours6);
        check("history: rolling window does not shift five-minute bucket boundaries",shifted.Length==2&&shifted[0].Open==102&&shifted[0].Close==106&&shifted[1].Open==105);
        check("history: empty early range stays empty",ChartHistory.ForRange(new MarketPeriod[0],MarketRange.Hours24).Length==0);
        check("history: renderer does not silently regroup requested candles",new ChartRaster(fixture.Take(120).ToArray(),ChartMode.Candle,657,145).Bars==120);
        check("retention: rolling default keeps exactly the last 1440 minute records",book.History("Mun").Length==1440&&book.History("Mun")[0].Time>book.Elapsed-86400);
        var legacySettings=new ConfigNode();legacySettings.AddValue("historyLimit","4096");
        check("retention: old configuration cannot restore the 68-hour limit",MarketSettings.Read(new[]{legacySettings}).HistoryLimit==1440);
        book.Advance(17.25); // Retention migration must preserve a partially completed interval too.
        var legacy=Save(book);var munNode=legacy.GetNodes("BODY").Single(n=>n.GetValue("name")=="Mun");var recent=munNode.GetValues("p");munNode.RemoveValues("p");
        munNode.AddValue("p",first.Time.ToString(System.Globalization.CultureInfo.InvariantCulture)+","+first.Open.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+first.High.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+first.Low.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+","+first.Close.ToString("R",System.Globalization.CultureInfo.InvariantCulture));
        foreach(var value in recent)munNode.AddValue("p",value);
        var migrated=MarketBook.Load(legacy,settings);
        check("retention: legacy saved history is trimmed without changing price or timer",migrated.History("Mun").Length==1440&&migrated.Ensure("Mun",40)==book.Ensure("Mun",40)&&migrated.Progress==book.Progress&&migrated.Elapsed==book.Elapsed&&migrated.Updates==book.Updates);
        bool continuation=true;for(int i=0;i<120;i++){book.Advance(60);migrated.Advance(60);continuation&=book.Ensure("Mun",40)==migrated.Ensure("Mun",40);}
        check("retention: trimming history preserves exact random/trend continuation",continuation);
        var slowSettings=new MarketSettings{Interval=3600,HistoryLimit=1440};var slow=new MarketBook(slowSettings,12);slow.Ensure("Mun",40);slow.Advance(30*3600);
        check("retention: records older than twenty-four active hours are removed even at a slower configured cadence",slow.History("Mun").Length==24);
        var kerbin=PlanetArt.Pixels("Kerbin",new[]{"Mun","Minmus"});var jool=PlanetArt.Pixels("Jool",new[]{"Laythe","Vall","Tylo","Bop","Pol"});
        check("icons: right guide extends beyond top and bottom moon radii",kerbin[60*64+51]!=0&&kerbin[4*64+51]!=0&&jool[60*64+51]!=0&&jool[4*64+51]!=0);
        ScenarioTests(check);
        PricingTests.UseUniformFixture();
    }
    private static void ScenarioTests(Action<string,bool> check)
    {
        PlanetaryTests.Seed();PricingTests.UseUniformFixture();HighLogic.CurrentGame=new Game();HighLogic.SaveFolder="A";HighLogic.LoadedScene=GameScenes.FLIGHT;
        var scenario=new MarketScenario();scenario.OnAwake();scenario.OnLoad(new ConfigNode());
        try
        {
            scenario.Sample(0);scenario.Sample(59);check("scenario: actual runtime adapter accumulates real time",MarketScenario.ActiveBook.Updates==0&&MarketScenario.ActiveBook.Progress==59);
            TimeWarp.fixedDeltaTime=10000;Planetarium.UniversalTime=1e15;scenario.Sample(60);
            check("scenario: extreme warp and UT change still cause one real-minute update",MarketScenario.ActiveBook.Updates==1);
            double progress=MarketScenario.ActiveBook.Progress;
            PauseMenu.isOpen=true;scenario.Sample(70);scenario.Sample(1000);PauseMenu.isOpen=false;scenario.Sample(1001);
            FlightDriver.Pause=true;scenario.Sample(2000);FlightDriver.Pause=false;scenario.Sample(2001);
            UnityEngine.Application.isFocused=false;scenario.Sample(3000);UnityEngine.Application.isFocused=true;scenario.Sample(3001);
            check("scenario: stock pause, flight pause and focus loss exclude inactive gaps",MarketScenario.ActiveBook.Progress==progress);
            HighLogic.LoadedScene=GameScenes.MAINMENU;scenario.Sample(4000);HighLogic.LoadedScene=GameScenes.FLIGHT;scenario.Sample(4001);
            check("scenario: outside active gameplay scenes pauses timer",MarketScenario.ActiveBook.Progress==progress);
            var saved=new ConfigNode();scenario.OnSave(saved);double quote=MarketScenario.ActiveBook.Ensure("Mun",100);
            var pricesBefore=MarketScenario.ActiveBook.History("Mun");
            scenario.OnLoad(saved);scenario.Sample(5000);
            check("scenario: scene/load rebind preserves quote and history without catchup",MarketScenario.ActiveBook.Ensure("Mun",100)==quote&&MarketScenario.ActiveBook.History("Mun").Length==pricesBefore.Length);
            var activeGame=HighLogic.CurrentGame;HighLogic.CurrentGame=null;scenario.Sample(6000);
            check("scenario: no active save disables clock and live state",!MarketScenario.Ready);
            HighLogic.CurrentGame=activeGame;scenario.Sample(6001);
            GameEvents.onGameSceneLoadRequested.Fire(GameScenes.EDITOR);double beforeTransition=MarketScenario.ActiveBook.Progress;scenario.Sample(10000);
            check("scenario: scene transition gaps never accrue time",MarketScenario.ActiveBook.Progress==beforeTransition);
            scenario.OnLoad(saved);scenario.OnApplicationFocus(false);scenario.OnApplicationFocus(true);scenario.Sample(20000);
            check("scenario: focus callbacks exclude gaps even when updates were suspended",MarketScenario.ActiveBook.Progress==beforeTransition);
            HighLogic.SaveFolder="B";
            HighLogic.CurrentGame=new Game();check("scenario: mismatched save identity fails closed",!OrePricing.TryGetUnitPrice("Mun",out double ignored));
            scenario.OnLoad(new ConfigNode());check("scenario: new save begins independently at base with no history",OrePricing.TryGetUnitPrice("Mun",out double p)&&p==100&&MarketScenario.History("Mun",double.PositiveInfinity).Length==0);
            scenario.OnLoad(saved);MarketScenario.ActiveBook.Advance(600);
            OrePricing.TryGetUnitPrice("Mun",out double live);
            var sales=new RecoverySales();sales.OnAwake();sales.OnLoad(new ConfigNode());Funding.Instance=new Funding{Funds=1000};MessageSystem.Instance=new MessageSystem();
            try
            {
                var cargo=RecoveryTests.Cargo("Mun",50,0,25);
                GameEvents.onVesselRecoveryProcessingComplete.Fire(cargo,null,1);
                check("market recovery: bonus uses remaining units and current authoritative quote",Funding.Instance.Calls==1&&Math.Abs(Funding.Instance.LastAmount-25*live)<1e-8&&Funding.Instance.Funds==1000+25*live);
                GameEvents.onVesselRecoveryProcessingComplete.Fire(cargo,null,1);
                var claims=new ConfigNode();sales.OnSave(claims);sales.OnLoad(claims);GameEvents.onVesselRecoveryProcessingComplete.Fire(cargo,null,1);
                check("market recovery: repeated and persisted callbacks still pay once",Funding.Instance.Calls==1);
                check("market recovery: Kerbin remains excluded under live pricing",!OrePricing.TryGetUnitPrice("Kerbin",out ignored));
            }
            finally{sales.OnDestroy();}
            var corrupt=new ConfigNode();corrupt.AddNode("MARKET_STATE").AddValue("version","999");scenario.OnLoad(corrupt);
            check("scenario: corrupt existing state disables quotes without resetting it",!MarketScenario.Ready&&!OrePricing.TryGetUnitPrice("Mun",out ignored));
            var retained=new ConfigNode();scenario.OnSave(retained);check("scenario: unsupported saved state is retained on save",retained.GetNode("MARKET_STATE").GetValue("version")=="999");
        }
        finally{scenario.OnDestroy();TimeWarp.fixedDeltaTime=.02f;Planetarium.UniversalTime=0;PauseMenu.isOpen=false;FlightDriver.Pause=false;UnityEngine.Application.isFocused=true;HighLogic.CurrentGame=new Game();}
    }
}
