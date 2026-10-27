using System;
using System.Collections.Generic;
namespace OreMarket
{
    public enum SpaceObjectKind { Asteroid, Comet }
    public enum ObjectVariantEvidence { Unknown, Ordinary, ConfirmedMagicBoulder }
    public sealed class ObjectMarketDefinition
    {
        public string Identity {get;private set;}
        public SpaceObjectKind Kind {get;private set;}
        public string ClassLetter {get;private set;}
        public string StockName {get;private set;}
        public double DefaultBase {get;private set;}
        public bool Shared => Identity==ObjectMarkets.Shared;
        public bool Special => ClassLetter==null && !Shared;
        public string Name {
            get {
                if(Shared)return "Asteroid & Comet Ore";
                if(Special)return "Magic Boulder";
                var size=(UntrackedObjectClass)Enum.Parse(typeof(UntrackedObjectClass),ClassLetter);
                string stock=DiscoveryInfo.GetSizeClassSizes(size);
                int left=stock==null?-1:stock.IndexOf('('),right=stock==null?-1:stock.LastIndexOf(')');
                return left>=0&&right>left?stock.Substring(left+1,right-left-1):StockName;
            }
        }
        public string Title => Shared?Name:Special?"Magic Boulder Ore":Name+" "+Kind+" Ore / "+Secondary;
        public string Secondary => Shared?"Shared market":Special?"Special":"Class "+ClassLetter;
        internal ObjectMarketDefinition(SpaceObjectKind kind,string letter,string name,double price,bool shared=false) {
            Kind=kind;ClassLetter=letter;StockName=name;DefaultBase=price;
            Identity=shared?ObjectMarkets.Shared:letter==null?ObjectMarkets.Magic: ObjectMarkets.Prefix+kind+":"+letter;
        }
    }
    // A classification record is not provenance or a payment authorization.
    public sealed class ObjectDescriptor
    {
        public SpaceObjectKind Kind {get;internal set;}
        public UntrackedObjectClass PhysicalClass {get;internal set;}
        public ObjectVariantEvidence Variant {get;internal set;}
        public string MarketIdentity {
            get {
                if(Variant==ObjectVariantEvidence.Unknown)return null;
                if(Variant==ObjectVariantEvidence.ConfirmedMagicBoulder)return Kind==SpaceObjectKind.Asteroid?ObjectMarkets.Magic:null;
                return ObjectMarkets.Prefix+Kind+":"+PhysicalClass;
            }
        }
    }
    public static class ObjectMarkets
    {
        public const string Prefix="OreMarket:Object:",Magic=Prefix+"MagicBoulder",Asteroids=Prefix+"ASTEROIDS",Comets=Prefix+"COMETS";
        private static readonly ObjectMarketDefinition[] definitions={
            new ObjectMarketDefinition(SpaceObjectKind.Asteroid,"A","Tiny",100),new ObjectMarketDefinition(SpaceObjectKind.Asteroid,"B","Small",100),
            new ObjectMarketDefinition(SpaceObjectKind.Asteroid,"C","Medium",100),new ObjectMarketDefinition(SpaceObjectKind.Asteroid,"D","Large",100),
            new ObjectMarketDefinition(SpaceObjectKind.Asteroid,"E","Huge",100),
            new ObjectMarketDefinition(SpaceObjectKind.Asteroid,"F","Huge",100),new ObjectMarketDefinition(SpaceObjectKind.Asteroid,"G","Massive",100),
            new ObjectMarketDefinition(SpaceObjectKind.Asteroid,"H","Gargantuan",100),new ObjectMarketDefinition(SpaceObjectKind.Asteroid,"I","Gargantuan",100),
            new ObjectMarketDefinition(SpaceObjectKind.Asteroid,null,"Magic Boulder",100),
            new ObjectMarketDefinition(SpaceObjectKind.Comet,"F","Huge",100),new ObjectMarketDefinition(SpaceObjectKind.Comet,"G","Massive",100),
            new ObjectMarketDefinition(SpaceObjectKind.Comet,"H","Gargantuan",100),new ObjectMarketDefinition(SpaceObjectKind.Comet,"I","Gargantuan",100)};
        private static readonly ObjectMarketDefinition shared=new ObjectMarketDefinition(SpaceObjectKind.Asteroid,null,"Asteroid & Comet Ore",100,true);
        // Physical class records remain available for provenance, not separate markets.
        public static ObjectMarketDefinition[] Definitions => new[]{shared};
        internal static ObjectMarketDefinition[] PhysicalDefinitions => (ObjectMarketDefinition[])definitions.Clone();
        public const string Shared=Prefix+"SpaceRock";
        public static string Commodity(string id) => Find(id)!=null?Shared:id;
        internal static bool SameCommodity(string a,string b) => Commodity(a)==Commodity(b);
        public static bool Reserved(string id) => id!=null&&id.StartsWith(Prefix,StringComparison.Ordinal);
        public static ObjectMarketDefinition Find(string id) {if(id==Shared)return shared;foreach(var item in definitions)if(item.Identity==id)return item;return null;}
        public static bool TryClassify(bool asteroidModule,bool cometModule,UntrackedObjectClass size,ObjectVariantEvidence variant,out ObjectDescriptor result)
        {
            result=null;if(asteroidModule==cometModule||!Enum.IsDefined(typeof(ObjectVariantEvidence),variant))return false;
            int index=(int)size;if(asteroidModule?(index<0||index>8):(index<5||index>8))return false;
            if(cometModule&&variant==ObjectVariantEvidence.ConfirmedMagicBoulder)return false;
            result=new ObjectDescriptor{Kind=asteroidModule?SpaceObjectKind.Asteroid:SpaceObjectKind.Comet,PhysicalClass=size,Variant=variant};return true;
        }
    }
    public static class MarketQuotes
    {
        private static Dictionary<string,double> objectPrices=new Dictionary<string,double>(StringComparer.Ordinal);
        internal static void LoadObjects(ConfigNode[] nodes)
        {
            var prices=new Dictionary<string,double>(StringComparer.Ordinal);foreach(var d in ObjectMarkets.Definitions)prices[d.Identity]=d.DefaultBase;
            try {
                var seen=new HashSet<string>(StringComparer.Ordinal);
                foreach(var n in nodes) {
                    var keys=n.GetValues("name");var values=n.GetValues("price");
                    if(keys.Length!=1||values.Length!=1||ObjectMarkets.Find(keys[0])==null||!seen.Add(keys[0])||
                        !double.TryParse(values[0],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out double p)||!MarketSettings.Finite(p)||p<=0||!MarketSettings.Finite(p*4))throw new FormatException("Invalid OBJECT market price");
                    if(keys[0]==ObjectMarkets.Shared)prices[ObjectMarkets.Shared]=p; // Legacy class settings never create independent quotes.
                }
                objectPrices=prices;
            }catch(FormatException e){objectPrices=new Dictionary<string,double>(StringComparer.Ordinal);Log.Error("Object market previews disabled: "+e.Message);}
        }
        public static bool TryGetBasePrice(string identity,out double price)
        {
            price=0;if(ObjectMarkets.Reserved(identity))return OrePricing.ConfigurationValid&&ObjectMarkets.Find(identity)!=null&&objectPrices.TryGetValue(ObjectMarkets.Shared,out price);
            return OrePricing.TryGetBasePrice(identity,out price);
        }
        public static bool TryGetUnitPrice(string identity,out double price) {price=0;return TryGetBasePrice(identity,out double baseline)&&MarketScenario.Price(ObjectMarkets.Commodity(identity),baseline,out price);}
        public static string Label(string identity) {var d=ObjectMarkets.Find(identity);return d==null?OreOrigins.Label(identity):"Asteroid & Comet Ore";}
        public static string ShortLabel(string identity) {var d=ObjectMarkets.Find(identity);return d==null?identity:"Space-rock Ore";}
    }
}
