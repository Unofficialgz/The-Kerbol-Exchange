using System;
using OreMarket;
using OreMarket.Terminal;
internal static class Monitor063Tests
{
    internal static void Run(Action<string,bool> check)
    {
        check("063: HUD background is nearly invisible",MonitorHud.BackgroundAlpha>0&&MonitorHud.BackgroundAlpha<.05f);
        check("063: HUD typography uses bright neutral white",MonitorHud.Text==0xF4F4F4&&MonitorHud.Secondary==0xDEDEDE);
        check("063: genuine effect colours match chart palette",MonitorHud.Rise==(ChartRaster.Green>>8)&&MonitorHud.Fall==(ChartRaster.Red>>8));
        var settings=new MarketSettings();settings.Events=new EventSettings {Ordinary=0,Significant=1,Major=0,Extreme=0,Concurrent=3};
        var book=new MarketBook(settings,781);book.Ensure("Bop",125);book.Ensure("Duna",75);book.Ensure("Ike",65);book.Ensure("Mun",40);book.Advance(60);
        check("063: unrelated active headlines never appear for selected Mun",book.News().Length==3&&MonitorEvent.Select("Mun",book.NewsFor("Mun")).Length==0);
        var duna=MonitorEvent.Select("Duna",book.NewsFor("Duna"));
        check("063: selecting affected Ore shows real EB label and full tooltip",duna.Length==1&&duna[0].Positive&&duna[0].Text=="EB +Duna"&&duna[0].Hint.Contains("Exceptional demand boom")&&duna[0].Hint.Contains("Duna Ore")&&duna[0].Hint.Contains("active remaining"));
        var snapshot=new ConfigNode();book.Save(snapshot);var loaded=MarketBook.Load(snapshot,settings);
        check("063: relevant event indicator restores with saved economics",MonitorEvent.Select("Duna",loaded.NewsFor("Duna"))[0].Hint==duna[0].Hint);
        bool negative=false;
        settings.Events=new EventSettings {Ordinary=1,Significant=0,Major=0,Extreme=0,Concurrent=1};
        for(uint seed=1;seed<100&&!negative;seed++) {
            var mining=new MarketBook(settings,seed);mining.Ensure("Mun",40);mining.Advance(60);
            var events=MonitorEvent.Select("Mun",mining.NewsFor("Mun"));
            if(events[0].Text=="MS -Mun") {negative=true;check("063: actual mining-surplus event is negative and correctly named",!events[0].Positive&&events[0].Hint.Contains("Mining surplus"));}
        }
        check("063: actual negative fixture exercised",negative);
    }
}

