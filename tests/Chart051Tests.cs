using System;
using System.Linq;
using OreMarket;
using OreMarket.Terminal;

internal static class Chart051Tests
{
    private static string Dump(ConfigNode n) => (n.name??"")+"{"+string.Join("|",n.values.Select(v=>v.name+"="+v.value))+string.Join("",n.nodes.Select(Dump))+"}";
    internal static void Run(Action<string,bool> check)
    {
        foreach(int count in new[]{1,3,15,60,73,97})
        {
            var geometry=new ChartGeometry(count,558,ChartMode.Candle);
            check("051: compact candle bodies remain separated at count "+count,geometry.BodyWidth>=3&&geometry.BodyWidth<=11&&geometry.Pitch>geometry.BodyWidth&&geometry.Pitch<=14);
            check("051: cursor snaps to recorded candles at count "+count,Enumerable.Range(0,count).All(i=>geometry.Nearest(geometry.X(i))==i));
            check("051: cursor clamps to actual early data at count "+count,geometry.Nearest(-100)==0&&geometry.Nearest(10000)==count-1);
        }
        check("051: handful of candles is not spread across plot",new ChartGeometry(3,558,ChartMode.Candle).X(2)<60);
        var period=new MarketPeriod(60,100,110,90,105);
        var raster=new ChartRaster(new[]{period},ChartMode.Candle,558,145);int x=raster.Geometry.X(0),high=raster.Y(110,145),low=raster.Y(90,145),bodyY=raster.Y(102,145);
        check("051: genuine high and low form thin single-pixel wicks",raster.Pixels[high*558+x]==ChartRaster.Green&&raster.Pixels[low*558+x]==ChartRaster.Green&&raster.Pixels[high*558+x+1]==0&&raster.Pixels[low*558+x+1]==0);
        check("051: candle body is wider than its wick",raster.Pixels[bodyY*558+x+2]==ChartRaster.Green);
        var noWick=new ChartRaster(new[]{new MarketPeriod(60,100,105,100,105)},ChartMode.Candle,558,145);int nx=noWick.Geometry.X(0);
        check("051: missing intraperiod extremes do not invent a wick",Enumerable.Range(noWick.Y(105,145)+1,144-noWick.Y(105,145)).All(y=>noWick.Pixels[y*558+nx]==0));
        var red=new ChartRaster(new[]{new MarketPeriod(60,105,110,90,100)},ChartMode.Candle,558,145);
        check("051: falling candles use red",red.Pixels[red.Y(102,145)*558+red.Geometry.X(0)]==ChartRaster.Red);
        foreach(double price in new[]{.001,10,40,75,350,700})
        {
            var axis=new PriceAxis(price*.98,price*1.02);
            check("051: axis brackets observations with nonoverlapping tick density "+price,axis.Min<=price*.98&&axis.Max>=price*1.02&&axis.Step>0&&axis.Labels.Length>=3&&axis.Labels.Length<=8&&145.0/(axis.Labels.Length-1)>=18);
        }
        check("051: price labels adapt decimals and large values",PriceAxis.Format(.12345,3)=="0.123"&&PriceAxis.Format(350,2)=="350.00"&&PriceAxis.Format(1e100,2).Length<14);
        check("051: extreme finite axes never generate infinite tick labels",new PriceAxis(1e308,double.MaxValue).Labels.All(label=>!label.Contains("Infinity")&&!label.Contains("NaN")));
        var points=new[]{new MarketPeriod(60,100,101,99,100),new MarketPeriod(120,100,102,99,101)};
        var line=new ChartGeometry(2,558,ChartMode.Line);
        check("051: line cursor distinguishes observed opening quote and closes",line.Nearest(0)==-1&&line.Nearest(line.X(0))==0&&line.Nearest(line.X(1))==1);
        var bucket=ChartHistory.ForRange(Enumerable.Range(0,6).Select(i=>new MarketPeriod(60+i*60,100+i,110+i,90+i,102+i)).ToArray(),MarketRange.Hours6);
        check("051: aggregated hover time is actual last observation",bucket[0].Start==60&&bucket[0].End==300&&bucket[1].End==360);
        check("051: countdown displays authoritative second rounding",ChartHistory.Countdown(60)=="01:00"&&ChartHistory.Countdown(42.75)=="00:43"&&ChartHistory.Countdown(.1)=="00:01"&&ChartHistory.Countdown(double.NaN)=="--:--");
        var saved=new ConfigNode();saved.AddValue("version","1");saved.AddValue("seed","123");saved.AddValue("elapsed","6017.25");saved.AddValue("progress","17.25");saved.AddValue("updates","100");
        var body=saved.AddNode("BODY");body.AddValue("name","Eve");body.AddValue("base","350");body.AddValue("price","200");body.AddValue("velocity","-0.012");body.AddValue("rng","40001234");body.AddValue("trend","-1");body.AddValue("remaining","35");body.AddValue("p","6000,201,201,200,200");
        var settings=new MarketSettings();var restored=MarketBook.Load(saved,settings);
        var after=new ConfigNode();restored.Save(after);
        check("051: version-one existing saved price, history and process restore exactly",body.GetValue("price")==after.GetNodes("BODY")[0].GetValue("price")&&body.GetValue("rng")==after.GetNodes("BODY")[0].GetValue("rng")&&body.GetValue("velocity")==after.GetNodes("BODY")[0].GetValue("velocity")&&body.GetValue("p")==after.GetNodes("BODY")[0].GetValue("p")&&restored.Ensure("Eve",350)==200);
        check("051: saved fractional timer drives countdown",restored.SecondsRemaining==42.75&&ChartHistory.Countdown(restored.SecondsRemaining)=="00:43");
        var copy=MarketBook.Load(after,settings);bool equal=true;
        for(int i=0;i<200;i++){restored.Advance(60);copy.Advance(60);equal&=restored.Ensure("Eve",350)==copy.Ensure("Eve",350)&&restored.SecondsRemaining==copy.SecondsRemaining;}
        check("051: reload keeps deterministic next movements and timer",equal);
        restored.Advance(42.75);check("051: countdown resets only after authoritative interval",restored.SecondsRemaining==60);
        var previousGame=HighLogic.CurrentGame;var previousScene=HighLogic.LoadedScene;
        HighLogic.CurrentGame=new Game();HighLogic.LoadedScene=GameScenes.FLIGHT;
        var scenario=new MarketScenario();scenario.OnAwake();scenario.OnLoad(new ConfigNode());
        try
        {
            scenario.Sample(0);scenario.Sample(17.25);
            check("051: runtime countdown proxy matches active timer",MarketScenario.SecondsRemaining==42.75);
            PauseMenu.isOpen=true;scenario.Sample(500);scenario.Sample(1000);
            var snapshot=new ConfigNode();scenario.OnSave(snapshot);scenario.OnLoad(snapshot);
            check("051: paused save and quickload preserve countdown",MarketScenario.SecondsRemaining==42.75);
            PauseMenu.isOpen=false;scenario.Sample(2000);scenario.Sample(2042.75);
            check("051: resumed runtime countdown resets at its original remaining interval",MarketScenario.SecondsRemaining==60);
        }
        finally { scenario.OnDestroy();PauseMenu.isOpen=false;HighLogic.CurrentGame=previousGame;HighLogic.LoadedScene=previousScene; }
        int lowUp=0,baseUp=0,highDown=0;
        for(int i=0;i<1000;i++)
        {
            double r=(i+.5)/1000;
            if(MarketDynamics.Velocity(200,350,0,0,r,settings)>0)lowUp++;
            if(MarketDynamics.Velocity(350,350,0,0,r,settings)>0)baseUp++;
            if(MarketDynamics.Velocity(600,350,0,0,r,settings)<0)highDown++;
        }
        check("051: low Eve prices bias upward and high prices downward",lowUp>baseUp&&highDown>baseUp&&baseUp==500);
        check("051: low prices can still decline and high prices can still rise",MarketDynamics.Velocity(200,350,0,-1,0,settings)<0&&MarketDynamics.Velocity(600,350,0,1,.999,settings)>0);
        check("051: outward movement softens before safeguards",Math.Abs(MarketDynamics.Velocity(228,350,0,-1,0,settings))<(228-227.5)/228.0);
        var longRun=new MarketBook(settings,9182);longRun.Ensure("Eve",350);int hardHits=0;bool bounded=true;
        for(int i=0;i<20000;i++){longRun.Advance(60);double p=longRun.Ensure("Eve",350);bounded&=p>=140&&p<=1400;if(p==140||p==1400)hardHits++;}
        check("051: long price run stays bounded without repeated hard clipping",bounded&&hardHits==0);
    }
}
