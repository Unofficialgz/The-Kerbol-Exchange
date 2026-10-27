using System;
using System.Collections.Generic;
namespace OreMarket.Terminal
{
    internal static class ObjectSidebar
    {
        internal static void Append(List<MarketGroup> groups)
        {
            string id=ObjectMarkets.Shared;
            if(!MarketQuotes.TryGetBasePrice(id,out double baseline)||!MarketQuotes.TryGetUnitPrice(id,out double quote))return;
            var group=new MarketGroup{Identity=id,Title="Asteroid & Comet Ore",IconKey=id+"|",ObjectGroup=true};
            group.Markets.Add(new Commodity(id,quote,baseline,ChartHistory.Convert(MarketScenario.History(id,3600)),MarketScenario.Ready));
            groups.Add(group);
        }

        internal static uint[] RockIcon(string identity,int size)
        {
            var data=new uint[size*size];
            var definition=ObjectMarkets.Find(identity);
            bool belt=identity==ObjectMarkets.Asteroids, comet=identity==ObjectMarkets.Comets||definition?.Kind==SpaceObjectKind.Comet;
            bool magic=identity==ObjectMarkets.Magic;
            int rank=definition?.ClassLetter==null?2:definition.ClassLetter[0]-(comet?'F':'A');
            double radius=(comet?.10:.10)+rank*(comet?.018:.023);
            for(int y=0;y<size;y++)for(int x=0;x<size;x++) {
                double u=(x+.5)/size,v=(y+.5)/size;
                uint color=0;
                if(belt) {
                    for(int n=0;n<8;n++) {
                        double angle=-2.6+n*.47,cx=.5+.37*Math.Cos(angle),cy=.53+.29*Math.Sin(angle);
                        double r=.042+(n%3)*.012;
                        if(Rock(u-cx,v-cy,r,n))color=Pack(150+n*5,140+n*4,128+n*4,255);
                    }
                } else if(magic) {
                    double dx=u-.5,dy=v-.5;
                    if(Rock(dx,dy,.35,1)) {
                        bool crack=Math.Abs(dx-.07*Math.Sin(dy*23))<.022 ||
                            (dx>-.22&&dx<.25&&Math.Abs(dy-.11-.5*dx)<.018)||
                            (dx<0&&Math.Abs(dy+.12+.7*dx)<.017);
                        color=crack?Pack(110,255,65,255):Pack(67,72,64,255);
                    }
                } else {
                    double growth=comet?0:Math.Max(0,rank-4);
                    double cx=.69-growth*.018,cy=.34+growth*.008,dx=u-cx,dy=v-cy;
                    double along=(-dx+dy)*.707,across=(dx+dy)*.707;
                    double length=(comet?.37:.22)+rank*(comet?.062:.055);
                    double width=(comet?.10:.055)*(1-along/length);
                    if(along>0&&along<length&&Math.Abs(across)<width) {
                        int alpha=(int)((comet?250:210)*(1-along/length));
                        color=comet?Pack(80,200,255,alpha):Pack(255,155+(int)(70*along/length),55,alpha);
                    }
                    if(Rock(dx,dy,radius,rank))color=comet?Pack(195,235,250,255):Pack(120+(int)(-dx*65),112+(int)(-dx*60),100+(int)(-dx*55),255);
                }
                data[y*size+x]=color;
            }
            return data;
        }
        private static bool Rock(double x,double y,double radius,int seed) {
            double angle=Math.Atan2(y,x),edge=radius*(1+.13*Math.Sin(5*angle+seed)+.06*Math.Cos(3*angle));
            return x*x+y*y<edge*edge;
        }
        private static uint Pack(int r,int g,int b,int a) => ((uint)r<<24)|((uint)g<<16)|((uint)b<<8)|(uint)Math.Max(0,Math.Min(255,a));
    }
}
