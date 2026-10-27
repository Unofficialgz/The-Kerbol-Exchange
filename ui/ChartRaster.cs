using System;
using System.Collections.Generic;
using System.Globalization;

namespace OreMarket.Terminal
{
    internal static class ChartHistory
    {
        internal static string Countdown(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds)) return "--:--";
            int value=(int)Math.Ceiling(Math.Max(0,Math.Min(5999,seconds)));
            return (value/60).ToString("00",CultureInfo.InvariantCulture)+":"+(value%60).ToString("00",CultureInfo.InvariantCulture);
        }
        internal static string Timestamp(double seconds)
        {
            TimeSpan time = TimeSpan.FromSeconds(seconds);
            return (time.Days > 0 ? time.Days.ToString(CultureInfo.InvariantCulture) + "d " : "") + time.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        }
        internal static double RangeSeconds(MarketRange range) => range == MarketRange.Minutes15 ? 900 : range == MarketRange.Hour1 ? 3600 : range == MarketRange.Hours6 ? 21600 : 86400;
        internal static MarketPeriod[] Convert(OreMarket.MarketSample[] samples)
        {
            var result = new MarketPeriod[samples.Length];
            for (int i=0; i<samples.Length; i++) { var p = samples[i]; result[i] = new MarketPeriod(p.Time,p.Open,p.High,p.Low,p.Close); }
            return result;
        }
        internal static int CandleSeconds(MarketRange range) => range == MarketRange.Hours6 ? 300 : range == MarketRange.Hours24 ? 900 : 60;
        // Samples carry period END times. Stable buckets are aligned to active
        // gameplay time zero, not to the first visible/retained observation.
        // Partial first/last buckets contain only observations actually available.
        internal static MarketPeriod[] ForRange(MarketPeriod[] source, MarketRange range)
        {
            int seconds = CandleSeconds(range);
            var result = new List<MarketPeriod>();
            long bucket = long.MinValue; double start=0,open=0,high=0,low=0,close=0,end=0;
            foreach (var p in source)
            {
                long next = Math.Max(0,(long)Math.Ceiling((p.Start-1e-6)/seconds)-1);
                if (next != bucket)
                {
                    if (bucket != long.MinValue) result.Add(new MarketPeriod(start,open,high,low,close,end));
                    bucket=next;start=p.Start;open=p.Open;high=p.High;low=p.Low;
                }
                high=Math.Max(high,p.High);low=Math.Min(low,p.Low);close=p.Close;end=p.End;
            }
            if (bucket != long.MinValue) result.Add(new MarketPeriod(start,open,high,low,close,end));
            return result.ToArray();
        }
    }
    internal static class EventMarkerGeometry
    {
        internal static double X(double time,MarketPeriod[] periods,ChartGeometry geometry,double interval)
        {
            if(periods.Length==0||time<periods[0].Start-interval||time>periods[periods.Length-1].End)return double.NaN;
            for(int i=0;i<periods.Length;i++)if(time<=periods[i].End) {
                double start=periods[i].Start-interval,end=periods[i].End;
                double left=geometry.Left(i);
                // Endpoint markers coincide with observed closes; internal timestamps remain ordered.
                return left+(geometry.X(i)-left)*Math.Max(0,Math.Min(1,(time-start)/Math.Max(1e-9,end-start)));
            }
            return double.NaN;
        }
        internal static int Nearest(double x,double[] positions,double tolerance)
        {
            int best=-1;double distance=tolerance;
            for(int i=0;i<positions.Length;i++) {double d=Math.Abs(positions[i]-x);if(d<=distance){distance=d;best=i;}}
            return best;
        }
    }
    internal sealed class PriceAxis
    {
        internal readonly double Min,Max,Step;
        internal readonly int Decimals;
        internal readonly string[] Labels;
        internal PriceAxis(double min,double max)
        {
            double raw=Math.Max(1e-9,(max-min)/4),power=Math.Pow(10,Math.Floor(Math.Log10(raw))),unit=raw/power;
            double multiplier=unit<=1?1:unit<=2?2:unit<=2.5?2.5:unit<=5?5:10;
            Step=power*multiplier;
            Min=Math.Max(0,Math.Floor(min/Step)*Step);Max=Math.Min(double.MaxValue,Math.Ceiling(max/Step)*Step);
            if(double.IsInfinity(Max)||Max<=Min) { Min=min;Max=max; }
            int decimals=0;while(decimals<6&&Math.Abs(Math.Round(Step,decimals)-Step)>Step*1e-7)decimals++;
            Decimals=decimals;
            int count=(int)Math.Min(8,Math.Floor((Max-Min)/Step+1e-8)+1);
            Labels=new string[count];for(int i=0;i<count;i++)Labels[i]=Format(Min+i*Step,Decimals);
        }
        internal static string Format(double value,int decimals)
        { string text=value.ToString("N"+Math.Min(6,Math.Max(0,decimals)),CultureInfo.InvariantCulture);return text.Length>13?value.ToString("G4",CultureInfo.InvariantCulture):text; }
    }
    internal sealed class ChartGeometry
    {
        internal readonly double Pitch;
        internal readonly int BodyWidth;
        private readonly int count,width;
        private readonly bool compact;
        internal ChartGeometry(int count,int width,ChartMode mode)
        {
            this.count=count;this.width=width;compact=mode==ChartMode.Candle||mode==ChartMode.Bar;
            Pitch=Math.Min(14,(width-16.0)/Math.Max(1,count));
            BodyWidth=Math.Max(1,Math.Min(11,Math.Min((int)Math.Floor(Pitch*.8),Math.Max(1,(int)Pitch-1))));
        }
        internal double Left(int index) => index>0?X(index-1):compact?Math.Max(0,X(0)-Pitch/2.0):0;
        internal int X(int index) => compact?(int)Math.Round(8+(index+.5)*Pitch):(index+1)*(width-1)/Math.Max(1,count);
        internal int Nearest(double x)
        {
            if(count==0)return -2;
            int index=compact?(int)Math.Round((x-8)/Pitch-.5):(int)Math.Round(x/(width-1)*count)-1;
            return Math.Max(compact?0:-1,Math.Min(count-1,index));
        }
    }
    // Aggregation uses exact observed OHLC; chart interpolation is presentation only.
    // No imaginary wicks. The prices are already dramatic enough.
    internal sealed class ChartRaster
    {
        internal readonly uint[] Pixels;
        internal readonly double Minimum, Maximum;
        internal readonly int Bars;
        internal readonly PriceAxis Axis;
        internal readonly ChartGeometry Geometry;
        internal const uint Neutral = 0xD0D0D0FF, Green = 0x52C282FF, Red = 0xD95F64FF;
        internal ChartRaster(MarketPeriod[] recorded, ChartMode mode, int width, int height) : this(recorded,mode,width,height,double.NaN) { }
        internal ChartRaster(MarketPeriod[] recorded, ChartMode mode, int width, int height,double latest)
        {
            Pixels = new uint[width*height];
            if (recorded.Length == 0) return;
            var bars = recorded; Bars = bars.Length; Geometry=new ChartGeometry(Bars,width,mode);
            double min = bars[0].Low, max = bars[0].High;
            foreach (var bar in bars) { min = Math.Min(min,bar.Low); max = Math.Max(max,bar.High); }
            if(!double.IsNaN(latest)&&!double.IsInfinity(latest)&&latest>0) { min=Math.Min(min,latest);max=Math.Max(max,latest); }
            double pad = Math.Max((max-min)*.1,Math.Max(max*.002,1e-6)); Axis=new PriceAxis(Math.Max(0,min-pad),Math.Min(double.MaxValue,max+pad)); Minimum=Axis.Min;Maximum=Axis.Max;
            if (mode == ChartMode.Line || mode == ChartMode.Area)
            {
                uint color = bars[bars.Length-1].Close > bars[0].Open ? Green : bars[bars.Length-1].Close < bars[0].Open ? Red : Neutral;
                // Include the first actual opening quote; every subsequent vertex is an observed close.
                int previousX=0, previousY=Y(bars[0].Open,height);
                for (int i=0;i<bars.Length;i++)
                {
                    int x=Geometry.X(i), y=Y(bars[i].Close,height);
                    for(int column=previousX;column<=x;column++)
                    {
                        int row=x==previousX ? y : previousY+(y-previousY)*(column-previousX)/(x-previousX);
                        if(mode==ChartMode.Area) for(int fill=0;fill<row;fill++) Put(column,fill,(color & 0xFFFFFF00)|45,width,height);
                    }
                    Line(previousX,previousY,x,y,color,width,height); previousX=x;previousY=y;
                }
            }
            else
            {
                int body=Geometry.BodyWidth;
                for(int i=0;i<bars.Length;i++)
                {
                    var p=bars[i]; int x=Geometry.X(i);
                    int open=Y(p.Open,height), close=Y(p.Close,height);
                    uint ink=p.Close>p.Open?Green:p.Close<p.Open?Red:Neutral;
                    Line(x,Y(p.Low,height),x,Y(p.High,height),ink,width,height);
                    if(mode==ChartMode.Candle)
                        for(int dx=-body/2;dx<=(body-1)/2;dx++) Line(x+dx,Math.Min(open,close),x+dx,Math.Max(open,close),ink,width,height);
                    else { Line(x-body/2,open,x,open,ink,width,height); Line(x,close,x+body/2,close,ink,width,height); }
                }
            }
        }
        internal static uint[] Sparkline(WatchlistSnapshot quote, int width, int height)
        {
            if (quote.Samples.Length < 2)
            {
                var flat = new uint[width*height];
                for(int x=0;x<width;x++) flat[(height/2)*width+x]=Neutral;
                return flat; // Explicit current-quote reference, not history.
            }
            var periods = new MarketPeriod[quote.Samples.Length-1];
            for(int i=0;i<periods.Length;i++)
            {
                double open=quote.Samples[i],close=quote.Samples[i+1];
                periods[i]=new MarketPeriod(i,open,Math.Max(open,close),Math.Min(open,close),close);
            }
            return new ChartRaster(periods,ChartMode.Line,width,height).Pixels;
        }
        internal int Y(double price,int height) => (int)Math.Round((price-Minimum)/(Maximum-Minimum)*(height-1));
        private void Put(int x,int y,uint color,int width,int height) { if(x>=0&&x<width&&y>=0&&y<height) Pixels[y*width+x]=color; }
        private void Line(int x,int y,int endX,int endY,uint color,int width,int height)
        {
            int dx=Math.Abs(endX-x), sx=x<endX?1:-1, dy=-Math.Abs(endY-y), sy=y<endY?1:-1, error=dx+dy;
            while(true) { Put(x,y,color,width,height); if(x==endX&&y==endY)break; int twice=2*error;if(twice>=dy){error+=dy;x+=sx;}if(twice<=dx){error+=dx;y+=sy;} }
        }
    }
}
