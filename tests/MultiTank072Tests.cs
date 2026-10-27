using System;
using System.Linq;
using System.Collections.Generic;
using OreMarket;
using KSP.UI.Screens;

// Production observers/state/recovery executed against offline KSP doubles.
internal static class MultiTank072Tests
{
    internal sealed class Rig
    {
        internal Vessel Vessel = new Vessel();
        internal readonly List<OreMarketTank> Tanks = new List<OreMarketTank>();
        internal ModuleResourceHarvester Drill;
        internal BaseConverter Asteroid, Comet;
        private uint offset;
        internal Part AddPart(uint id,string name) {
            var p=new Part{persistentId=id+offset,vessel=Vessel};p.partInfo=new AvailablePart{name=name,partPrefab=p};Vessel.parts.Add(p);return p;
        }
        internal Rig(int count=3,double preload=0,uint idOffset=0) {
            offset=idOffset;
            for(int i=0;i<count;i++) {
                var p=AddPart((uint)(100+i),i%2==0?"SmallTank":"LargeTank");
                p.Resources.Add(new PartResource{part=p,amount=i==0?preload:0,maxAmount=1500});
                var t=new OreMarketTank{part=p};p.Modules.Add(t);Tanks.Add(t);
            }
            var tool=AddPart(200,"MiniDrill");Drill=new ModuleResourceHarvester{part=tool,ResourceName="Ore"};tool.Modules.Add(Drill);
            var rock=AddPart(201,"PotatoRoid");rock.Modules.Add(new ModuleAsteroid{part=rock,prefabBaseURL="Procedural/PA_H"});
            rock.Modules.Add(new ModuleAsteroidInfo{part=rock});rock.Modules.Add(new ModuleAsteroidResource{part=rock});
            var a=new ModuleAsteroidDrill{part=tool};a.SetSource(rock);tool.Modules.Add(a);Asteroid=a;
            var comet=AddPart(202,"PotatoComet");comet.Modules.Add(new ModuleComet{part=comet,prefabBaseURL="Procedural/PC_H"});
            comet.Modules.Add(new ModuleCometInfo{part=comet});comet.Modules.Add(new ModuleCometResource{part=comet});
            var c=new ModuleCometDrill{part=tool};c.SetSource(comet);tool.Modules.Add(c);Comet=c;
            foreach(var t in Tanks)t.OnStart(PartModule.StartState.Flying);
        }
        internal void Output(BaseConverter drill,double time,params double[] units) {
            new ResourceConverter().ProcessRecipe(time,new ConversionRecipe { Execute=()=> {
                var set=new PartSet(); set.Cached.lists.Add(Tanks.Select(t=>t.part.Resources.Get("Ore")).ToList());
                var allowed=set.GetList(PartResourceLibrary.Instance.GetDefinition("Ore").id).lists.SelectMany(g=>g).ToArray();
                double sum=units.Sum(),stored=0;
                for(int i=0;i<units.Length;i++) {
                    var requested=Tanks[i].part.Resources.Get("Ore");
                    var target=allowed.Contains(requested)&&requested.amount<requested.maxAmount ? requested : allowed.FirstOrDefault(t=>t.amount<t.maxAmount);
                    if(target==null)continue;
                    double addition=Math.Min(units[i],target.maxAmount-target.amount);target.amount+=addition;stored+=addition;
                }
                if(stored<=0)return;
                drill.SetProcessedTime(time);Planetarium.UniversalTime=time;
                GameEvents.OnResourceConverterOutput.Fire(drill,"Ore",sum);
            }},drill.part,drill,1);
            foreach(var t in Tanks)t.FixedUpdate();
        }
        internal void Transfer(int source,int target,double amount,bool fail=false) {
            var s=StockOreTransfers.Begin(Tanks[target].part,new[]{Tanks[source].part});
            if (!s.Safe) {StockOreTransfers.Complete(s,false);return;}
            Tanks[source].part.Resources.Get("Ore").amount-=amount;Tanks[target].part.Resources.Get("Ore").amount+=amount;
            StockOreTransfers.Complete(s,fail);
            foreach(var t in Tanks)t.FixedUpdate();
        }
        internal double Verified => Tanks.Sum(t=>t.State.Eligible);
        internal double Amount => Tanks.Sum(t=>t.Amount);
        internal double Origin(string origin) => Tanks.SelectMany(t=>t.State.Lots).Where(l=>l.Origin==origin).Sum(l=>l.Quantity);
        internal void Reload() {
            GameEvents.onVesselUnloaded.Fire(Vessel);
            foreach(var t in Tanks){var n=new ConfigNode();t.OnSave(n);t.OnLoad(n);t.OnStart(PartModule.StartState.Flying);}
        }
        internal ProtoVessel Snapshot() {
            var proto=new ProtoVessel{vesselID=Vessel.id,landed=true};proto.orbitSnapShot.ReferenceBodyIndex=1;
            foreach(var p in Vessel.parts){var s=new ProtoPartSnapshot{persistentId=p.persistentId,partName=p.partInfo.name,partInfo=p.partInfo};
                foreach(var m in p.Modules){var ms=new ProtoPartModuleSnapshot{moduleName=m.moduleName};m.OnSave(ms.moduleValues);s.modules.Add(ms);}
                foreach(var r in p.Resources)s.resources.Add(new ProtoPartResourceSnapshot{resourceName=r.resourceName,amount=r.amount,maxAmount=r.maxAmount});
                proto.protoPartSnapshots.Add(s);
            }return proto;
        }
    }
    private static bool Near(double a,double b)=>Math.Abs(a-b)<1e-7;
    private static string Dump(TrackingState s)=>string.Join(";",s.Save().Where(x=>x.Key!="reason").OrderBy(x=>x.Key).Select(x=>x.Key+"="+x.Value));
    internal static void Run(Action<string,bool> check)
    {
        PlanetaryTests.Seed();OrePricing.Load(new[]{PricingTests.Shipped()});
        var observer=new MiningObserver();observer.Awake();var sales=new RecoverySales();sales.OnAwake();
        try {
            var r=new Rig(3,20);r.Output(r.Drill,100,1,1,1);r.Output(r.Drill,101,10,20,30);
            check("072: one stock drill fills three tracked tanks without spending nominal three times",Near(r.Verified,60)&&r.Tanks.All(t=>!t.State.Invalid));
            check("072: preload and first continuity output remain unverified",Near(r.Amount-r.Verified,23));
            double total=r.Verified;r.Transfer(0,1,15.5);
            check("072: proportional transfer preserves total verified cargo",Near(r.Verified,total)&&Near(r.Tanks[0].State.Eligible,5)&&Near(r.Tanks[1].State.Eligible,25));
            r.Transfer(1,2,r.Tanks[1].Amount);
            check("072: full mixed-content transfer empties source without duplicating cargo",Near(r.Verified,total)&&r.Tanks[1].Amount==0&&r.Tanks[1].State.Eligible==0&&r.Tanks.All(t=>!t.State.Invalid));
            check("072: tank batches remain unique after moving provenance",r.Tanks.Select(t=>t.State.BatchId).Distinct().Count()==3);
            // Genuine schema-4 fixtures from the previous multi-origin rules, not new mixed production.
            for(int i=0;i<3;i++) {
                double[] mun={5,0,55},duna={2,4,6},asteroid={3,6,9},comet={4,8,12},physical={27.5,21,97.5};
                var data=r.Tanks[i].State.Save();data["schema"]="4";data["lastAmount"]=physical[i].ToString("R",System.Globalization.CultureInfo.InvariantCulture);
                var entries=new[]{Tuple.Create("Mun",0u,mun[i]),Tuple.Create("Duna",0u,duna[i]),Tuple.Create(ObjectMarkets.Prefix+"Asteroid:H",201u,asteroid[i]),Tuple.Create(ObjectMarkets.Prefix+"Comet:H",202u,comet[i])}.Where(x=>x.Item3>0).ToArray();
                data["origin"]=entries[0].Item1;data["objectSourceId"]=entries[0].Item2.ToString();data["lotCount"]=entries.Length.ToString();data["eligible"]=entries.Sum(x=>x.Item3).ToString("R",System.Globalization.CultureInfo.InvariantCulture);
                if(data["batchId"].Length==0){data["batchId"]=Guid.NewGuid().ToString("N");data["vesselId"]=r.Vessel.id.ToString("N");data["manifest"]=VesselRules.Manifest(r.Vessel.parts);}
                for(int j=0;j<entries.Length;j++){data["lot"+j+"_origin"]=entries[j].Item1;data["lot"+j+"_source"]=entries[j].Item2.ToString();data["lot"+j+"_quantity"]=entries[j].Item3.ToString("R",System.Globalization.CultureInfo.InvariantCulture);}
                var node=new ConfigNode();var tracking=node.AddNode(OreMarketTank.NodeName);foreach(var e in data)tracking.AddValue(e.Key,e.Value);
                r.Tanks[i].part.Resources.Get("Ore").amount=physical[i];r.Tanks[i].OnLoad(node);r.Tanks[i].OnStart(PartModule.StartState.Flying);
            }
            check("0721: legacy second-body balances survive migration",Near(r.Origin("Mun"),60)&&Near(r.Origin("Duna"),12)&&r.Tanks.All(t=>t.State.LegacyMixed&&!t.State.Invalid));
            check("0721: legacy H asteroid/comet proofs stay separate",Near(r.Origin(ObjectMarkets.Prefix+"Asteroid:H"),18)&&Near(r.Origin(ObjectMarkets.Prefix+"Comet:H"),24));
            var before=r.Tanks.Select(t=>Dump(t.State)).ToArray();r.Reload();
            check("072: per-tank multi-origin state restores exactly",r.Tanks.Select((t,i)=>Dump(t.State)==before[i]).All(x=>x));
            r.Vessel.packed=true;GameEvents.onVesselGoOnRails.Fire(r.Vessel);r.Output(r.Asteroid,50000,1,1,1);r.Output(r.Asteroid,90000,1,2,3);
            GameEvents.onVesselGoOffRails.Fire(r.Vessel);r.Vessel.packed=false;
            check("072: time warp and reload continuity retain old cargo and credit later output",Near(r.Origin(ObjectMarkets.Prefix+"Asteroid:H"),18)&&r.Tanks.All(t=>!t.State.Invalid));
            var quantities=r.Tanks[2].State.Lots.ToDictionary(l=>l.Origin,l=>l.Quantity);double verified=r.Tanks[2].State.Eligible;
            r.Tanks[2].part.Resources.Get("Ore").amount-=verified/4;r.Tanks[2].FixedUpdate();
            check("072: consumption removes verified material first, proportionally across origins",r.Tanks[2].State.Lots.All(l=>Near(l.Quantity,quantities[l.Origin]*.75)));
            // Multi-origin recovery with current quotes, one funding call and atomic claims.
            RecoveryTests.Reset(sales);
            var market=new MarketScenario();market.OnAwake();market.OnLoad(new ConfigNode());
            try {
                MarketScenario.ActiveBook.Advance(600);
                double expected=r.Tanks.Sum(t=>t.State.Lots.Sum(l=>LotQuotes.Price(l,out double price)?l.Quantity*price:0));
                var snap=r.Snapshot();var stale=r.Snapshot();Funding.Instance.Funds+=321;var dialog=new MissionRecoveryDialog{fundsEarned=321,totalFunds=1321};
                GameEvents.onVesselRecoveryProcessingComplete.Fire(snap,dialog,1);
                if (Funding.Instance.Calls!=1 || !Near(Funding.Instance.LastAmount,expected)) Console.WriteLine("072 recovery detail: expected="+expected+" actual="+Funding.Instance.LastAmount+" calls="+Funding.Instance.Calls+"\n"+string.Join("\n",Log.Lines.Skip(Math.Max(0,Log.Lines.Count-20))));
                check("072: multi-tank recovery pays each origin at its authoritative live price",expected>0&&Funding.Instance.Calls==1&&Near(Funding.Instance.LastAmount,expected));
                check("072: stock refund stays separate from aggregated OreMarket bonus",Near(Funding.Instance.Funds,1321+expected)&&dialog.fundsEarned==321&&dialog.totalFunds==1321);
                check("072: receipt lists planetary, asteroid and comet origins with prices",MessageSystem.Instance.Messages.Single().message.Contains("Duna Ore")&&MessageSystem.Instance.Messages[0].message.Contains("Asteroid & Comet Ore")&&MessageSystem.Instance.Messages[0].message.Contains("Asteroid & Comet Ore"));
                var claims=new ConfigNode();sales.OnSave(claims);sales.OnLoad(claims);
                GameEvents.onVesselRecoveryProcessingComplete.Fire(stale,null,1);
                check("072: all tank batch and vessel claims persist and block duplicates",Funding.Instance.Calls==1&&claims.GetValues("claimed").Length==4);
            } finally {market.OnDestroy();}
            // Rejections affect only uncertain tanks where possible.
            r=new Rig();r.Output(r.Drill,100,1,1,1);r.Output(r.Drill,101,10,20,30);
            r.Tanks[0].part.Resources.Get("Ore").amount+=1;r.Tanks[0].FixedUpdate();
            check("072: unrelated cheated addition does not invalidate other tanks",r.Tanks[0].State.Invalid&&Near(r.Verified,50)&&!r.Tanks[1].State.Invalid&&!r.Tanks[2].State.Invalid);
            r.Transfer(0,1,5);
            check("072: invalid source transfers only unverified material",Near(r.Tanks[1].State.Eligible,20)&&r.Tanks[0].State.Invalid);
            r.Transfer(1,0,5);
            check("072: invalid destination never regains eligibility",r.Tanks[0].State.Invalid&&r.Tanks[0].State.Eligible==0&&Near(r.Verified,50));
            RecoveryTests.Reset(sales);GameEvents.onVesselRecoveryProcessingComplete.Fire(r.Snapshot(),null,1);
            check("072: recovery preserves payment for unrelated valid tanks",Funding.Instance.Calls==1&&Funding.Instance.LastAmount>0);
            foreach(string failure in new[]{"imbalance","direction","capacity","exception","manifest","unobserved"}) {
                r=new Rig();r.Output(r.Drill,100,1,1,1);r.Output(r.Drill,101,10,20,30);
                if(failure=="unobserved") {r.Tanks[0].part.Resources.Get("Ore").amount-=5;r.Tanks[1].part.Resources.Get("Ore").amount+=5;foreach(var t in r.Tanks)t.FixedUpdate();}
                else {
                    var s=StockOreTransfers.Begin(r.Tanks[1].part,new[]{r.Tanks[0].part});
                    r.Tanks[0].part.Resources.Get("Ore").amount-=5;r.Tanks[1].part.Resources.Get("Ore").amount+=failure=="imbalance"?6:5;
                    if(failure=="direction"){r.Tanks[2].part.Resources.Get("Ore").amount-=1;r.Tanks[1].part.Resources.Get("Ore").amount+=1;}
                    if(failure=="capacity")r.Tanks[1].part.Resources.Get("Ore").maxAmount+=1;
                    if(failure=="manifest")r.AddPart(999,"probeCore");
                    StockOreTransfers.Complete(s,failure=="exception");
                }
                check("072: reject unsafe transfer without creating verified Ore: "+failure,r.Tanks[1].State.Invalid&&r.Verified<=60);
                check("072: reject unsafe transfer retains unaffected tank: "+failure,failure=="direction"||(!r.Tanks[2].State.Invalid&&Near(r.Tanks[2].State.Eligible,30)));
                check("072: observer transaction lock always released: "+failure,r.Tanks.All(t=>!StockOreTransfers.Busy(t)));
            }
            r=new Rig();r.Output(r.Drill,100,1,1,1);r.Output(r.Drill,101,10,20,30);
            var other=new Rig();var cross=StockOreTransfers.Begin(r.Tanks[1].part,new[]{other.Tanks[0].part});
            r.Tanks[1].part.Resources.Get("Ore").amount+=1;StockOreTransfers.Complete(cross,false);
            check("072: cross-vessel transfer never credits destination",r.Tanks[1].State.Invalid&&r.Tanks[0].State.Eligible==10);
            r=new Rig();r.Output(r.Drill,100,1,1,1);r.Output(r.Drill,101,10,20,30);
            r.Vessel.mainBody.bodyName="Duna";r.Output(r.Drill,102,1,1,1);
            r.Tanks[0].part.Resources.Get("Ore").amount-=5; // No FixedUpdate before the next drilling operation.
            r.Output(r.Drill,103,5,0,0);
            check("072: pre-production observation prevents consumption being hidden by another origin",Near(r.Tanks[0].State.Lots.Where(l=>l.Origin=="Mun").Sum(l=>l.Quantity),5)&&Near(r.Tanks[0].State.Lots.Where(l=>l.Origin=="Duna").Sum(l=>l.Quantity),0));
            r=new Rig();r.Drill.inputList.Add(new ResourceRatio{ResourceName="Ore"});r.Output(r.Drill,100,1,1,1);
            check("072: resource-consuming custom drill recipe remains unsupported",r.Tanks.All(t=>t.State.Invalid));
            r=new Rig();r.Output(r.Drill,100,1,1,1);
            foreach(var t in r.Tanks)t.part.Resources.Get("Ore").amount+=10;
            r.Drill.SetProcessedTime(101);Planetarium.UniversalTime=101;GameEvents.OnResourceConverterOutput.Fire(r.Drill,"Ore",10);
            check("072: global output budget cannot be multiplied by tank count",r.Verified==0&&r.Tanks.All(t=>t.State.Invalid));
            r=new Rig();r.Output(r.Drill,100,1,1,1);r.Output(r.Drill,101,10,20,30);
            r.Tanks[0].part.Resources.Get("Ore").maxAmount=3000;r.Tanks[0].FixedUpdate();
            check("072: resize invalidates only the affected tank balance",r.Tanks[0].State.Invalid&&Near(r.Verified,50));
            GameEvents.onPartDeCouple.Fire(r.Tanks[0].part);
            check("072: staging still invalidates all mined custody",r.Tanks.All(t=>t.State.Invalid));
            // Real installed Harmony patches the offline stock UI double. The original still runs.
            var patch=new StockOreTransfers();patch.Awake();r=new Rig();r.Output(r.Drill,100,1,1,1);r.Output(r.Drill,101,10,20,30);
            var ui=new UIPartActionResourceTransfer{resource=r.Tanks[1].part.Resources.Get("Ore"),State=2};
            ui.Targets.Add(new UIPartActionResourceTransfer{resource=r.Tanks[0].part.Resources.Get("Ore"),State=1});
            int calls=0;ui.UpdateOperation=()=>{calls++;r.Tanks[0].part.Resources.Get("Ore").amount-=5;r.Tanks[1].part.Resources.Get("Ore").amount+=5;};ui.UpdateItem();
            check("072: real Harmony brackets stock-UI double without skipping original",calls==1&&Near(r.Verified,60)&&r.Tanks.All(t=>!t.State.Invalid)&&r.Tanks[0].State.Eligible<10);
            ui.UpdateOperation=()=>{calls++;r.Tanks[0].part.Resources.Get("Ore").amount-=1;throw new InvalidOperationException("stock test failure");};
            bool threw=false;try{ui.UpdateItem();}catch(InvalidOperationException){threw=true;}
            check("072: Harmony finalizer preserves original exception and clears lock",threw&&calls==2&&r.Tanks[0].State.Invalid&&r.Tanks.All(t=>!StockOreTransfers.Busy(t)));
            r=new Rig();r.Output(r.Drill,100,1,1,1);r.Output(r.Drill,101,10,20,30);
            ui=new UIPartActionResourceTransfer{resource=r.Tanks[1].part.Resources.Get("Ore"),State=2};
            ui.Targets.Add(new UIPartActionResourceTransfer{resource=r.Tanks[0].part.Resources.Get("Ore"),State=1});
            ui.Targets.Add(new UIPartActionResourceTransfer{resource=r.Tanks[2].part.Resources.Get("Ore"),State=1});
            ui.UpdateOperation=()=>{r.Tanks[0].part.Resources.Get("Ore").amount-=3;r.Tanks[2].part.Resources.Get("Ore").amount-=7;r.Tanks[1].part.Resources.Get("Ore").amount+=10;};ui.UpdateItem();
            check("072: stock UI can combine multiple donors using their individual verified proportions",Near(r.Verified,60)&&Near(r.Tanks[1].State.Eligible,20+3*10d/11+7*30d/31)&&r.Tanks.All(t=>!t.State.Invalid));
            // Persistence / malformed state / legacy migration / long conservation sequence.
            r=new Rig(3,100);r.Output(r.Drill,100,1,1,1);r.Output(r.Drill,101,10,20,30);
            var rng=new Random(720);
            for(int i=0;i<2000;i++) {
                int a=rng.Next(3),b=(a+1+rng.Next(2))%3;double amount=r.Tanks[a].Amount*rng.NextDouble();double old=r.Verified;
                r.Transfer(a,b,amount);
                if(r.Tanks.Any(t=>t.State.Invalid)||!Near(r.Verified,old)||r.Tanks.Any(t=>t.State.Eligible>t.Amount+1e-7))throw new Exception("072 conservation failed at "+i);
                if(i%19==0)r.Reload();
            }
            check("072: 2000 randomized transfers and repeated reloads conserve provenance",Near(r.Verified,60)&&Near(r.Amount,163));
            r=new Rig();string manifest=VesselRules.Manifest(r.Vessel.parts);
            for(int i=0;i<3;i++){double amount=10*(i+1);r.Tanks[i].part.Resources.Get("Ore").amount=amount;r.Tanks[i].State.Mining(i==1?"Duna":"Mun",amount,amount,true,r.Vessel.id.ToString("N"),manifest);}
            for(int i=0;i<2000;i++) {
                int a=rng.Next(3),b=(a+1+rng.Next(2))%3;r.Transfer(a,b,r.Tanks[a].Amount*rng.NextDouble());
                if(r.Tanks.Any(t=>t.State.Invalid||t.State.Eligible>t.Amount))throw new Exception("072 pure-verified transfer bound failed at "+i);
                if(i%19==0)r.Reload();
            }
            check("072: 2000 fully verified mixed-origin transfers round downward and survive reload",Near(r.Verified,60)&&r.Tanks.All(t=>!t.State.Invalid));
            var saved=r.Tanks[0].State.Save();saved["lot0_quantity"]="NaN";
            check("072: malformed lot quantities fail closed",TrackingState.Load(saved).Invalid);
            var uncredited=TrackingState.New(20,100);uncredited.Mining("Mun",2,15,false,r.Vessel.id.ToString("N"),"100:SmallTank");
            check("072: excluded first output with net consumption persists as unverified",!TrackingState.Load(uncredited.Save()).Invalid&&uncredited.Eligible==0&&uncredited.LastAmount==15);
            var legacy=TrackingState.New(0,100);legacy.Mining("Mun",10,10,true,r.Vessel.id.ToString("N"),"100:SmallTank");
            foreach(string schema in new[]{"2","3"}){var data=legacy.Save();data["schema"]=schema;if(schema=="2")data.Remove("objectSourceId");var loaded=TrackingState.Load(data);
                check("072: legacy schema "+schema+" becomes one intact provenance lot",!loaded.Invalid&&loaded.Eligible==10&&loaded.Lots.Single().Origin=="Mun"&&loaded.BatchId==legacy.BatchId);}
            r=new Rig();r.Output(r.Drill,100,1,1,1);r.Output(r.Drill,101,10,10,30);
            var copied=new ConfigNode();r.Tanks[0].OnSave(copied);
            var copiedNode=copied.GetNode(OreMarketTank.NodeName);copiedNode.SetValue("batchId",Guid.Parse(copiedNode.GetValue("batchId")).ToString("D"),true);
            r.Tanks[1].OnLoad(copied);r.Tanks[1].OnStart(PartModule.StartState.Flying);
            check("072: copied live tank batch cannot be laundered through a new destination",!VesselRules.Loaded(r.Vessel,r.Tanks[0],out _));
            r.Transfer(1,2,5);
            check("072: ambiguous copied batch transfers receive no provenance",r.Tanks[2].State.Eligible==30&&r.Tanks[1].State.Eligible==0&&r.Tanks[1].State.Invalid);
            r=new Rig();r.Output(r.Drill,100,1,1,1);r.Output(r.Drill,101,10,20,30);
            RecoveryTests.Reset(sales);var duplicateSnapshot=r.Snapshot();
            string copiedBatch=duplicateSnapshot.protoPartSnapshots[0].modules[0].moduleValues.GetNode(OreMarketTank.NodeName).GetValue("batchId");
            duplicateSnapshot.protoPartSnapshots[1].modules[0].moduleValues.GetNode(OreMarketTank.NodeName).SetValue("batchId",Guid.Parse(copiedBatch).ToString("D"),true);
            GameEvents.onVesselRecoveryProcessingComplete.Fire(duplicateSnapshot,null,1);
            check("072: duplicate snapshot batch rejects whole payment before reserving claims",Funding.Instance.Calls==0);
            var ledger=new TransactionLedger();string vessel=Guid.NewGuid().ToString("N"),batch=Guid.NewGuid().ToString("N");
            check("072: repeated tank batch rejects atomically",!ledger.TryClaimMany(vessel,new[]{batch,batch},100)&&!ledger.Entries.Any());
        } finally {observer.OnDestroy();sales.OnDestroy();}
    }
}
