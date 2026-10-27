using System;
using System.Linq;
using System.Collections.Generic;
using OreMarket;
using KSP.UI.Screens;
internal static class Events060Tests
{
    private static ConfigNode Save(MarketBook b) {var n=new ConfigNode();b.Save(n);return n;}
    private static string Dump(ConfigNode n)=>string.Join("|",n.values.Select(v=>v.name+"="+v.value))+string.Join("",n.nodes.Select(Dump));
    internal static void Run(Action<string,bool> check)
    {
        var settings=new MarketSettings();var b=new MarketBook(settings,7461);
        b.Ensure("Mun",40);b.Ensure("Minmus",30);b.SetSystem("Mun","Kerbin");b.SetSystem("Minmus","Kerbin");
        var noEvents=new MarketSettings();noEvents.Events=new EventSettings {Ordinary=0,Significant=0,Major=0,Extreme=0};
        var calm=new MarketBook(noEvents,1);calm.Ensure("Eve",350);calm.Advance(60000);
        check("060: zero configured event chances prevent all starts",calm.News().Length==0);
        var forced=new MarketSettings();forced.Events=new EventSettings {Ordinary=0,Significant=0,Major=0,Extreme=1,Concurrent=1};
        var boom=new MarketBook(forced,54);boom.Ensure("Mun",40);boom.Ensure("Duna",75);boom.Advance(60);
        check("060: forced global roll selects exactly one market",boom.News().Length==1&&boom.News()[0].Headline=="Interplanetary Resource Crisis");
        var first=boom.News()[0];check("060: extreme duration is 25-45 active minutes",first.Remaining>=1500&&first.Remaining<=2700);
        var saved=Save(boom);var restored=MarketBook.Load(saved,forced);
        check("060: event and global RNG restore exactly",Dump(saved)==Dump(Save(restored)));
        bool same=true;for(int i=0;i<150;i++){boom.Advance(60);restored.Advance(60);same&=Dump(Save(boom))==Dump(Save(restored));}
        check("060: all future events and market states match after quickload",same);
        // Observe the real forced event through expiration, aftermath and cooldown.
        boom=MarketBook.Load(saved,forced);string origin=first.Origin;bool fade=false,ended=false,smooth=true;
        double previous=boom.Ensure(origin,origin=="Mun"?40:75);
        for(int i=0;i<80;i++){
            boom.Advance(60);double price=boom.Ensure(origin,origin=="Mun"?40:75);smooth&=Math.Abs(price/previous-1)<=.08000001;previous=price;
            fade|=boom.News().Any(n=>n.Origin==origin&&n.Aftermath);
            if(!boom.News().Any(n=>n.Origin==origin)) {ended=true;break;}
        }
        check("060: boom has fading aftermath then cooldown without price reset",fade&&ended&&smooth);
        var cooldown=Save(boom).GetNodes("BODY").First(n=>n.GetValue("name")==origin);
        check("060: expiry cooldown remains bounded after fade",double.Parse(cooldown.GetValue("cooldown"),System.Globalization.CultureInfo.InvariantCulture)>=0);
        // Runtime event callbacks exclude even a pause/resume entirely between frame samples.
        var game=HighLogic.CurrentGame;var scene=HighLogic.LoadedScene;
        var scenario=new MarketScenario();
        try {
            HighLogic.CurrentGame=new Game();HighLogic.LoadedScene=GameScenes.SPACECENTER;
            scenario.OnAwake();scenario.OnLoad(new ConfigNode());scenario.Sample(100);scenario.Sample(110);
            double seconds=MarketScenario.SecondsRemaining,elapsed=MarketScenario.ActiveSeconds;
            GameEvents.onGamePause.Fire();scenario.Sample(1000);
            check("060: pause event freezes countdown and underlying state",MarketScenario.SecondsRemaining==seconds&&MarketScenario.ActiveSeconds==elapsed);
            GameEvents.onGameUnpause.Fire();scenario.Sample(2000);scenario.Sample(2001);
            check("060: resume has no paused-time catch-up",MarketScenario.ActiveSeconds==elapsed+1);
            Planetarium.Pause=true;scenario.Sample(2002);scenario.Sample(4000);
            check("060: non-flight planetarium pause also freezes market",MarketScenario.ActiveSeconds==elapsed+1);
            Planetarium.Pause=false;scenario.Sample(5000);scenario.Sample(5001);
            check("060: planetarium resume discards whole inactive gap",MarketScenario.ActiveSeconds==elapsed+2);
        } finally {scenario.OnDestroy();Planetarium.Pause=false;HighLogic.CurrentGame=game;HighLogic.LoadedScene=scene;}
        bool bounds=true,steps=true,concurrency=true;int starts=0,extremes=0,hard=0;double peak=0;
        string last="";
        for(int i=0;i<50000;i++) {
            double old=b.Ensure("Mun",40);b.Advance(60);double p=b.Ensure("Mun",40);peak=Math.Max(peak,p/40);
            bounds&=p>=16&&p<=160;steps&=Math.Abs(p/old-1)<=.08000001;concurrency&=b.News().Count(n=>!n.Aftermath)<=3&&b.News().Length<=6;
            if(p==16||p==160)hard++;
            string current=string.Join(",",b.News().Select(n=>n.Origin+n.Headline));
            if(current!=last) {starts++;if(b.News().Any(n=>n.Headline=="Interplanetary Resource Crisis"&&!n.Aftermath))extremes++;}last=current;
        }
        check("060: long simulation respects absolute boundaries and step limits",bounds&&steps&&hard==0);
        check("060: long simulation respects concurrency and creates varied events",concurrency&&starts>100&&extremes>0);
        check("060: large booms remain achievable",peak>2);
        Console.WriteLine("060 long simulation: 50000 minutes, 2 origins; headline transitions="+starts+", extreme-containing transitions="+extremes+", peak Mun/base="+peak.ToString("F3")+", hard boundary hits="+hard);

        for(int tier=0;tier<4;tier++) {
            var cfg=new MarketSettings();cfg.Events=new EventSettings {Ordinary=tier==0?1:0,Significant=tier==1?1:0,Major=tier==2?1:0,Extreme=tier==3?1:0};
            var eventBook=new MarketBook(cfg,1234);eventBook.Ensure("Duna",75);eventBook.Advance(60);var ev=eventBook.News()[0];
            int[] mins={10,15,20,25},maxs={20,30,40,45};
            check("060: tier "+tier+" starts with requested duration",ev.Tier==tier&&ev.Remaining>=mins[tier]*60&&ev.Remaining<=maxs[tier]*60);
            double duration=ev.Remaining;eventBook.Advance(17.25);
            check("060: event duration uses fractional authoritative progress tier "+tier,eventBook.News()[0].Remaining==duration-17.25);
            var loaded=MarketBook.Load(Save(eventBook),cfg);
            check("060: fractional event duration restores tier "+tier,loaded.News()[0].Remaining==eventBook.News()[0].Remaining&&loaded.SecondsRemaining==eventBook.SecondsRemaining);
        }
        int extremeStarts=0,ordinaryStarts=0,significantStarts=0,majorStarts=0;bool globalOnce=true;
        for(uint seed=1;seed<=6000;seed++) {
            var cfg=new MarketSettings();cfg.Events=new EventSettings {Ordinary=0,Significant=0,Major=0,Extreme=.0035};
            var one=new MarketBook(cfg,seed);one.Ensure("Mun",40);one.Advance(60);
            var many=new MarketBook(cfg,seed);for(int k=0;k<14;k++)many.Ensure("Body"+k,40);many.Advance(60);
            globalOnce&=one.News().Length==many.News().Length&&many.News().Length<=1;extremeStarts+=one.News().Length;
            cfg=new MarketSettings();cfg.Events.Extreme=0;var weighted=new MarketBook(cfg,seed);weighted.Ensure("Duna",75);weighted.Advance(60);
            if(weighted.News().Length>0){int tier=weighted.News()[0].Tier;if(tier==0)ordinaryStarts++;if(tier==1)significantStarts++;if(tier==2)majorStarts++;}
        }
        check("060: extreme roll is global, independent of number of eligible bodies",globalOnce);
        check("060: seeded start frequencies approximate configured weights",extremeStarts>=10&&extremeStarts<=55&&ordinaryStarts>=75&&ordinaryStarts<=165&&significantStarts>=8&&significantStarts<=45&&majorStarts>=5&&majorStarts<=40);
        Console.WriteLine("060 first-minute independent seeds=6000: ordinary="+ordinaryStarts+", significant="+significantStarts+", major="+majorStarts+", global extreme="+extremeStarts);
        bool system=false;
        for(uint seed=1;seed<100&&!system;seed++) {
            var cfg=new MarketSettings();cfg.Events=new EventSettings {Ordinary=1,Significant=0,Major=0,Extreme=0,Concurrent=1};
            var grouped=new MarketBook(cfg,seed);for(int i=0;i<6;i++){grouped.Ensure("Moon"+i,10);grouped.SetSystem("Moon"+i,"ModdedPrimary");}grouped.Advance(60);
            if(grouped.News()[0].Scope=="ModdedPrimary system") {
                var data=Save(grouped);system=data.GetNodes("BODY").All(n=>n.GetNodes("EVENT").Length==1)&&grouped.News().Length==1;
                check("060: full modded system event deduplicates news and survives load",system&&Dump(data)==Dump(Save(MarketBook.Load(data,cfg))));
            }
        }
        check("060: ordinary events can affect an entire actual system",system);
        int positiveUps=0,normalUps=0,negativeUps=0;
        for(int i=0;i<1000;i++) {
            double r=(i+.5)/1000;
            if(MarketDynamics.EventVelocity(100,100,0,0,r,settings,.004,1,.65,3)>0)positiveUps++;
            if(MarketDynamics.EventVelocity(100,100,0,0,r,settings,0,1,.65,2.5)>0)normalUps++;
            if(MarketDynamics.EventVelocity(100,100,0,0,r,settings,-.004,1,.4,2.5)>0)negativeUps++;
        }
        check("060: events bias movement without guaranteeing direction",positiveUps>normalUps&&negativeUps<normalUps&&positiveUps<1000&&negativeUps>0);
        bool flightFrozen=false;scenario=new MarketScenario();
        try {
            HighLogic.CurrentGame=new Game();HighLogic.LoadedScene=GameScenes.SPACECENTER;scenario.OnAwake();scenario.OnLoad(new ConfigNode());scenario.Sample(0);scenario.Sample(7);
            FlightDriver.Pause=true;scenario.Sample(8);scenario.Sample(500);flightFrozen=MarketScenario.ActiveSeconds==7;
        } finally {scenario.OnDestroy();FlightDriver.Pause=false;HighLogic.CurrentGame=game;HighLogic.LoadedScene=scene;}
        check("060: FlightDriver pause is honored outside Flight without a menu",flightFrozen);
        var configNode=new ConfigNode();configNode.AddValue("ordinaryEventChance","0.7");configNode.AddValue("significantBoomChance","0.7");bool badWeights=false;
        try{MarketSettings.Read(new[]{configNode});}catch(FormatException){badWeights=true;}check("060: impossible event weights fail configuration validation",badWeights);
        // Default fourteen-market economy, 100000 active minutes. Track actual unique starts.
        var economy=new MarketBook(settings,78291);for(int i=0;i<14;i++)economy.Ensure("Body"+i,100);
        var seen=new HashSet<string>();var initial=new Dictionary<string,double>();var peaks=new Dictionary<string,double>();var tiers=new Dictionary<string,int>();double[] previousQuotes=Enumerable.Repeat(100.0,14).ToArray();int quietMoves=0,typicalMoves=0,strongMoves=0;double largestMove=0;int[] counts=new int[4];double highest=0,total=0;int highOccupancy=0;bool safe=true;int edgeHits=0;
        for(int minute=0;minute<100000;minute++) {
            economy.Advance(60);
            foreach(var ev in economy.News()) {
                double price=economy.Ensure(ev.Origin,100);
                if(seen.Add(ev.EventId)){counts[ev.Tier]++;initial[ev.EventId]=price;peaks[ev.EventId]=price;tiers[ev.EventId]=ev.Tier;}
                peaks[ev.EventId]=Math.Max(peaks[ev.EventId],price);
            }
            for(int k=0;k<14;k++) {double quote=economy.Ensure("Body"+k,100);double move=Math.Abs(quote/previousQuotes[k]-1);previousQuotes[k]=quote;largestMove=Math.Max(largestMove,move);if(move<.01)quietMoves++;else if(move<=.05)typicalMoves++;else strongMoves++;double ratio=quote/100;highest=Math.Max(highest,ratio);total+=ratio;if(ratio>2.5)highOccupancy++;safe&=ratio>=.4&&ratio<=4;if(ratio==.4||ratio==4)edgeHits++;}
        }
        int doubling=peaks.Keys.Count(id=>peaks[id]/initial[id]>=2);
        int extremeDoubling=peaks.Keys.Count(id=>tiers[id]==3&&peaks[id]/initial[id]>=2);
        check("060: full economy stays safe without sticking to absolute limits",safe&&edgeHits==0&&highOccupancy<140000);
        check("060: extreme starts remain rarer than other tiers and large rallies occur",counts[3]>0&&counts[3]<counts[0]&&counts[3]<counts[1]&&extremeDoubling>0);
        Console.WriteLine("060 economy 100000 minutes / 14 origins: ordinary="+counts[0]+", significant="+counts[1]+", major="+counts[2]+", extreme="+counts[3]+", peak/base="+highest.ToString("F3")+", mean/base="+(total/1400000).ToString("F3")+", above2.5="+highOccupancy+"/1400000, hard hits="+edgeHits+", events with observed doubling="+doubling+", extreme events with observed doubling="+extremeDoubling);
        Console.WriteLine("060 movements: below1%="+quietMoves+", 1-5%="+typicalMoves+", above5%="+strongMoves+", maximum="+largestMove.ToString("P2"));

        var shipped=PricingTests.Shipped();var actual=MarketSettings.Read(shipped.GetNodes("MARKET"));
        var prices=new Dictionary<string,double>();
        foreach(var node in shipped.GetNodes("BODY")) {
            double value=double.Parse(node.GetValue("price"),System.Globalization.CultureInfo.InvariantCulture);
            if(value>0)prices[node.GetValue("name")]=value;
        }
        prices[ObjectMarkets.Shared]=100;
        var day=new MarketBook(actual,930);foreach(var pair in prices)day.Ensure(pair.Key,pair.Value);
        double dayMin=4,dayMax=0;bool daySafe=true;
        for(int minute=0;minute<1440;minute++) {
            day.Advance(60);
            foreach(var pair in prices){double ratio=day.Ensure(pair.Key,pair.Value)/pair.Value;dayMin=Math.Min(dayMin,ratio);dayMax=Math.Max(dayMax,ratio);daySafe&=ratio>=.4&&ratio<=4;}
        }
        check("093: shipped-price one-day economy retains all 15 independent markets within limits",prices.Count==15&&daySafe&&prices.Keys.All(k=>day.History(k).Length==1440));
        var dayCopy=MarketBook.Load(Save(day),actual);day.Advance(17.25);dayCopy.Advance(17.25);
        check("093: shipped-price economy restores history, events and fractional timer exactly",Dump(Save(day))==Dump(Save(dayCopy)));
        Console.WriteLine("093 shipped-price 24h / 15 markets: minimum/base="+dayMin.ToString("F3")+", maximum/base="+dayMax.ToString("F3"));

        PlanetaryTests.Seed();PricingTests.UseUniformFixture();scenario=new MarketScenario();
        try {
            HighLogic.CurrentGame=new Game();HighLogic.LoadedScene=GameScenes.FLIGHT;
            var liveBook=new MarketBook(forced,884);liveBook.Ensure("Mun",100);liveBook.Advance(60);
            var module=new ConfigNode();liveBook.Save(module.AddNode("MARKET_STATE"));
            scenario.OnAwake();scenario.OnLoad(module);scenario.Sample(0);scenario.Sample(17.25);
            var beforePause=new ConfigNode();scenario.OnSave(beforePause);
            GameEvents.onGamePause.Fire();scenario.Sample(1000);scenario.Sample(2000);
            var whilePaused=new ConfigNode();scenario.OnSave(whilePaused);
            check("060: active event, price, RNG, history and timer all freeze on pause",Dump(beforePause)==Dump(whilePaused));
            scenario.OnLoad(whilePaused);check("060: quickload while paused exactly restores economic state",MarketScenario.News().Any(n=>n.Origin=="Mun")&&MarketScenario.SecondsRemaining==42.75);
            GameEvents.onGameUnpause.Fire();scenario.Sample(3000);TimeWarp.fixedDeltaTime=100000;Planetarium.UniversalTime=1e16;scenario.Sample(3042.75);
            check("060: huge warp changes still advance event by one active minute",MarketScenario.ActiveBook.Updates==2&&MarketScenario.SecondsRemaining==60&&MarketScenario.News().First(n=>n.Origin=="Mun").Remaining==liveBook.News()[0].Remaining-60);
            double price;OrePricing.TryGetUnitPrice("Mun",out price);
            var sales=new RecoverySales();sales.OnAwake();sales.OnLoad(new ConfigNode());Funding.Instance=new Funding {Funds=1000};MessageSystem.Instance=new MessageSystem();
            try {
                var cargo=RecoveryTests.Cargo("Mun",50,0,25);GameEvents.onVesselRecoveryProcessingComplete.Fire(cargo,null,1);
                check("060: recovery during an active boom pays current shared quote",Funding.Instance.Calls==1&&Math.Abs(Funding.Instance.LastAmount-25*price)<1e-8);
                var claims=new ConfigNode();sales.OnSave(claims);sales.OnLoad(claims);GameEvents.onVesselRecoveryProcessingComplete.Fire(cargo,null,1);
                check("060: event-driven recovery cannot bypass persistent duplicate claims",Funding.Instance.Calls==1);
            } finally {sales.OnDestroy();}
        } finally {scenario.OnDestroy();TimeWarp.fixedDeltaTime=.02f;Planetarium.UniversalTime=0;HighLogic.CurrentGame=game;HighLogic.LoadedScene=scene;}
        var restricted=new MarketBook(forced,1);restricted.Ensure("RetiredModBody",100);restricted.SetEligibleOrigins(new[]{"Mun"});restricted.Advance(600);
        check("060: unavailable bodies retain state without events or new observations",restricted.News().Length==0&&restricted.History("RetiredModBody").Length==0&&restricted.Ensure("RetiredModBody",100)==100);
        var corrupt=Save(restored);corrupt.GetNodes("BODY").First(n=>n.GetNodes("EVENT").Length>0).GetNodes("EVENT")[0].SetValue("fade","NaN",true);
        bool rejected=false;try{MarketBook.Load(corrupt,forced);}catch(FormatException){rejected=true;}
        check("060: corrupt event state fails closed",rejected);
    }
}
