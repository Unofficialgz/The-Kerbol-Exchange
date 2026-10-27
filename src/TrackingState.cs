using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OreMarket
{
    public sealed class OreLot
    {
        public string Origin { get; internal set; }
        public uint SourceId { get; internal set; }
        public double Quantity { get; internal set; }
        internal OreLot Copy(double quantity) => new OreLot { Origin = Origin, SourceId = SourceId, Quantity = quantity };
        internal bool Proof => OreOrigins.Valid(Origin) && (ObjectMarkets.Reserved(Origin) ?
            SourceId != 0 && ObjectMarkets.Find(Origin) != null && !ObjectMarkets.Find(Origin).Special : SourceId == 0);
        internal string Key => Origin + "|" + SourceId.ToString(CultureInfo.InvariantCulture);
    }

    // Amounts live per tank; verified lots never include the implicit unverified remainder.
    public sealed class TrackingState
    {
        public const double Price = OrePricing.DefaultUnitPrice;
        public const double Epsilon = 1e-7;
        private readonly List<OreLot> lots = new List<OreLot>();
        public IEnumerable<OreLot> Lots => lots.Select(l => l.Copy(l.Quantity));
        public bool LegacyMixed { get; private set; }
        internal bool AcceptsOrigin(string origin) => !Invalid && !LegacyMixed &&
            (LastAmount == 0 || Origin.Length == 0 || (origin.Length > 0 && ObjectMarkets.SameCommodity(Origin,origin)));
        internal string TransferOrigin => Invalid ? "" : Origin;
        private void Empty(double capacity)
        {
            lots.Clear(); LastAmount=0; Capacity=capacity; Origin=""; ObjectSourceId=0;
            BatchId=""; VesselId=""; Manifest=""; Invalid=false; LegacyMixed=false; Reason="Waiting for Ore";
        }
        public uint TankId { get; private set; }
        internal bool BindTank(uint id) {
            if(id==0 || TankId!=0&&TankId!=id){Invalidate("Tank persistent identity mismatch");return false;}
            TankId=id;return true;
        }
        internal void RebindAfterLogistics(string vessel,string manifest) { if(!Invalid&&BatchId.Length>0){VesselId=vessel;Manifest=manifest;Reason=LegacyMixed?"Legacy mixed cargo retained; storage locked":"Verified cargo retained after stock vessel separation/merge";} }
        public double Eligible => lots.Sum(l => l.Quantity);
        public double LastAmount { get; private set; }
        public double Capacity { get; private set; }
        public string Origin { get; private set; } = "";
        public bool Invalid { get; private set; }
        public string Reason { get; private set; } = "Waiting for Ore";
        public string BatchId { get; private set; } = "";
        public string VesselId { get; private set; } = "";
        public string Manifest { get; private set; } = "";
        public uint ObjectSourceId { get; private set; }
        public bool HasObjectProof => ObjectSourceId != 0 && ObjectMarkets.Find(Origin) != null && !ObjectMarkets.Find(Origin).Special;
        public static bool ValidAmount(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0;
        public static double Bonus(double eligible, double actual) => OrePricing.Calculate(eligible, actual, OrePricing.DefaultUnitPrice);
        public static TrackingState New(double actual, double capacity = double.MaxValue)
        {
            var state = new TrackingState { Capacity = capacity, LastAmount = ValidAmount(actual) ? actual : 0 };
            if (!ValidAmount(actual) || !ValidAmount(capacity) || actual > capacity) state.Invalidate("Invalid physical Ore amount/capacity");
            else if (actual > 0) state.Reason = "Existing Ore is unverified";
            if (actual == 0 && ValidAmount(capacity)) state.Empty(capacity);
            return state;
        }
        public void Invalidate(string reason) { if (!Invalid) Reason = reason; lots.Clear(); Invalid = true; }
        public void ObserveUnverified(double actual, double capacity)
        {
            if (actual == 0 && ValidAmount(capacity)) { Empty(capacity); return; }
            if (!Invalid && BatchId.Length == 0 && Eligible == 0 && ValidAmount(actual) && ValidAmount(capacity) && actual <= capacity)
            { LastAmount = actual; Capacity = capacity; Reason = "Waiting for Ore"; return; }
            ObserveCapacity(capacity); Observe(actual);
        }
        public void Pause(string reason) { if (LastAmount == 0) { Reason="Waiting for Ore"; return; } if (!Invalid) Reason = "Tracking paused: " + reason + "; existing verified cargo retained"; }
        public void Resume() { if (LastAmount == 0) Reason="Waiting for Ore"; else if (!Invalid) Reason=LegacyMixed ? "Legacy mixed cargo: consume or recover; storage locked" : "Assigned " + OreOrigins.Label(Origin) + "; waiting for drilling"; }
        public void OnScaleOperation() { if (BatchId.Length > 0) Invalidate("Scale operation after mining; batch invalidated"); }
        public void ObserveCapacity(double capacity) { if (!ValidAmount(capacity) || capacity != Capacity) Invalidate("Ore capacity modified"); }
        // Consumption debits verified material first, proportionally across its origins.
        private void Consume(double amount)
        {
            double total = Eligible;
            double factor = total > 0 ? Math.Max(0, 1 - amount / total) : 0;
            if (lots.Count == 1) lots[0].Quantity = Math.Max(0,lots[0].Quantity-amount);
            else foreach (var lot in lots) lot.Quantity *= factor;
            lots.RemoveAll(l => l.Quantity == 0);
        }
        public void Observe(double actual)
        {
            if (!ValidAmount(actual) || actual > Capacity) { Invalidate("Invalid physical Ore amount"); return; }
            if (actual == 0) { Empty(Capacity); return; }
            if (actual > LastAmount) Invalidate("Unattributed Ore addition");
            else if (actual < LastAmount) { Consume(LastAmount - actual); if (!Invalid) Reason = "Ore removed; verified balance reduced"; }
            LastAmount = actual; BoundPhysical(actual);
        }
        private void BoundPhysical(double actual)
        {
            double excess=Eligible-actual;
            if(excess>Epsilon) { Invalidate("Verified balance exceeds physical Ore"); return; }
            // Round only downward when floating-point addition crosses the physical bound.
            for(int i=0;Eligible>actual&&i<lots.Count+2;i++) {
                var largest=lots.OrderByDescending(l=>l.Quantity).First();
                double reduced=Math.Max(0,largest.Quantity-(Eligible-actual));
                largest.Quantity=reduced>0 ? BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(reduced)-1) : 0;
            }
            lots.RemoveAll(l=>l.Quantity==0);
            if(Eligible>actual)Invalidate("Unrepresentable verified balance");
        }
        internal void RebindAfterRelease(string vessel,string manifest) { if(!Invalid&&BatchId.Length>0){VesselId=vessel;Manifest=manifest;Reason=LegacyMixed?"Legacy mixed cargo retained; consume or recover; storage locked":"Verified cargo retained after object release";} }
        private bool Identity(string vessel, string manifest)
        {
            if (!Guid.TryParse(vessel, out var v) || v == Guid.Empty || string.IsNullOrEmpty(manifest))
            { Invalidate("Missing vessel identity"); return false; }
            if (BatchId.Length > 0 && (VesselId != vessel || Manifest != manifest))
            { Invalidate("Vessel identity or parts changed"); return false; }
            if (BatchId.Length == 0) { BatchId = Guid.NewGuid().ToString("N"); VesselId = vessel; Manifest = manifest; }
            return true;
        }
        private bool Add(OreLot value)
        {
            if (!value.Proof || !ValidAmount(value.Quantity) ||
                (value.SourceId != 0 && lots.Any(l => l.SourceId == value.SourceId && l.Origin != value.Origin)))
            { Invalidate("Missing or changed verified object source"); return false; }
            var lot = lots.FirstOrDefault(l => l.Key == value.Key);
            if (lot == null) { if (lots.Count >= 256) { Invalidate("Too many provenance lots"); return false; } lots.Add(value.Copy(value.Quantity)); }
            else lot.Quantity += value.Quantity;
            if (Origin.Length == 0) { Origin = value.Origin; ObjectSourceId = value.SourceId; }
            return true;
        }
        internal void MiningObject(string origin, double nominal, double actual, bool credit, string vessel, string manifest, uint sourceId)
            => Produce(origin, sourceId, nominal, actual, credit, vessel, manifest);
        public void Mining(string origin, double nominal, double actual, bool credit, string vessel, string manifest)
            => Produce(origin, 0, nominal, actual, credit, vessel, manifest);
        private void Produce(string origin, uint source, double nominal, double actual, bool credit, string vessel, string manifest)
        {
            if (Invalid) { Observe(actual); return; }
            var proof = new OreLot { Origin = origin, SourceId = source };
            if (!proof.Proof || !ValidAmount(nominal) || nominal <= 0 || !ValidAmount(actual) || actual > Capacity)
            { Invalidate("Missing origin or invalid stock output"); return; }
            if (!AcceptsOrigin(origin)) {
                if (actual != LastAmount) { Invalidate("Incompatible Ore addition outside supported routing"); LastAmount=actual; }
                return;
            }
            if (!Identity(vessel, manifest)) return;
            if (Origin.Length == 0) { Origin = origin; ObjectSourceId = source; }
            double increase = actual - LastAmount;
            if (increase < 0) { if (!credit) Consume(nominal); Observe(actual); return; }
            if (increase > nominal + Epsilon) { Invalidate("Tank balance does not match stock output"); LastAmount = actual; return; }
            // Excluded continuity output never promotes the unverified remainder.
            if (source != 0 && ((ObjectSourceId == source && Origin != origin) || lots.Any(l => l.SourceId == source && l.Origin != origin)))
            { Invalidate("Object class changed after mining"); return; }
            if (credit && increase > 0) { proof.Quantity = Math.Min(increase, nominal); Add(proof); }
            else if (!credit) Consume(Math.Max(0, nominal - increase));
            LastAmount = actual; BoundPhysical(actual);
            if (Invalid) return;
            Reason = credit ? "Verified " + (lots.Select(l => ObjectMarkets.Commodity(l.Origin)).Distinct().Count() > 1 ? "mixed Ore origins" : OreOrigins.Label(origin)) :
                "Tracking paused: new output uncredited; existing verified cargo retained";
        }
        internal void MiningUncredited(double nominal, double actual)
        {
            if (!ValidAmount(actual) || actual > Capacity || actual - LastAmount > nominal + Epsilon) { Observe(actual); return; }
            if (actual < LastAmount) { Consume(nominal); Observe(actual); }
            else { Consume(Math.Max(0, nominal - (actual - LastAmount))); LastAmount = actual; }
        }
        // Called only by the synchronous, conservation-checked stock UI transfer observer.
        internal List<OreLot> RemoveTransfer(double quantity, double actual)
        {
            var result = new List<OreLot>();
            if (!ValidAmount(quantity) || !ValidAmount(actual) || quantity > LastAmount || actual > LastAmount || Math.Abs(LastAmount-quantity-actual)>Epsilon || Invalid) return result;
            double fraction = LastAmount > 0 ? quantity / LastAmount : 0;
            foreach (var lot in lots) { double moved = lot.Quantity * fraction; result.Add(lot.Copy(moved)); lot.Quantity -= moved; }
            lots.RemoveAll(l => l.Quantity == 0); LastAmount = actual;
            if(actual==0)Empty(Capacity);
            return result;
        }
        internal void ReceiveTransfer(double actual, IEnumerable<OreLot> incoming, string vessel, string manifest, string assignment = null, uint assignmentSource = 0)
        {
            if (Invalid) { Observe(actual); return; }
            var received = incoming.ToArray();
            string origin=assignment ?? received.FirstOrDefault()?.Origin ?? "";
            if (!AcceptsOrigin(origin) || received.Any(l=>!ObjectMarkets.SameCommodity(l.Origin,origin))) {
                Invalidate("Incompatible transfer outside supported stock operation"); LastAmount=actual; return;
            }
            if (Origin.Length==0 && origin.Length>0) { Origin=origin; ObjectSourceId=assignmentSource!=0 ? assignmentSource : received.FirstOrDefault()?.SourceId ?? 0; }
            if (received.Any(l => l.Quantity > 0) && !Identity(vessel, manifest)) return;
            foreach (var lot in received) if (lot.Quantity > 0 && !Add(lot)) return;
            LastAmount = actual;
            BoundPhysical(actual);
            Reason = Invalid ? Reason : "Verified stock Ore transfer; origin quantities retained";
        }
        public Dictionary<string,string> Save()
        {
            var data = new Dictionary<string,string> {
                {"schema","6"},{"tankId",TankId.ToString(CultureInfo.InvariantCulture)},{"legacyMixed",LegacyMixed.ToString()},{"objectSourceId",ObjectSourceId.ToString(CultureInfo.InvariantCulture)},
                {"eligible",Eligible.ToString("R",CultureInfo.InvariantCulture)},{"lastAmount",LastAmount.ToString("R",CultureInfo.InvariantCulture)},
                {"capacity",Capacity.ToString("R",CultureInfo.InvariantCulture)},{"origin",Origin},{"invalid",Invalid.ToString()},
                {"reason",Reason},{"batchId",BatchId},{"vesselId",VesselId},{"manifest",Manifest}, {"lotCount",lots.Count.ToString(CultureInfo.InvariantCulture)} };
            for (int i=0;i<lots.Count;i++) { var l=lots[i]; string key="lot"+i+"_";
                data[key+"origin"]=l.Origin; data[key+"source"]=l.SourceId.ToString(CultureInfo.InvariantCulture); data[key+"quantity"]=l.Quantity.ToString("R",CultureInfo.InvariantCulture); }
            return data;
        }
        public static TrackingState Load(IDictionary<string,string> data)
        {
            var s=New(0);
            try {
                if(data==null)throw new FormatException();
                foreach(var entry in data)if(entry.Value==null)throw new FormatException();
                string schema=data["schema"]; if(schema!="2"&&schema!="3"&&schema!="4"&&schema!="5"&&schema!="6")throw new FormatException();
                if(schema=="6")s.TankId=uint.Parse(data["tankId"],CultureInfo.InvariantCulture);
                s.LastAmount=double.Parse(data["lastAmount"],CultureInfo.InvariantCulture); s.Capacity=double.Parse(data["capacity"],CultureInfo.InvariantCulture);
                double eligible=double.Parse(data["eligible"],CultureInfo.InvariantCulture);
                s.Origin=data["origin"]; s.ObjectSourceId=schema=="2"?0:uint.Parse(data["objectSourceId"],CultureInfo.InvariantCulture);
                s.Invalid=bool.Parse(data["invalid"]);s.Reason=data["reason"];s.BatchId=data["batchId"];s.VesselId=data["vesselId"];s.Manifest=data["manifest"];
                if(!ValidAmount(eligible)||!ValidAmount(s.LastAmount)||!ValidAmount(s.Capacity)||s.LastAmount>s.Capacity||eligible>s.LastAmount||(s.Invalid&&eligible!=0))throw new FormatException();
                if(s.BatchId.Length>0&&(!Guid.TryParse(s.BatchId,out var b)||b==Guid.Empty||!Guid.TryParse(s.VesselId,out var v)||v==Guid.Empty||s.Manifest.Length==0||!OreOrigins.Valid(s.Origin)))throw new FormatException();
                if(eligible>0&&s.BatchId.Length==0)throw new FormatException();
                if(s.Origin.Length>0&&!new OreLot{Origin=s.Origin,SourceId=s.ObjectSourceId}.Proof)throw new FormatException();
                if(schema=="4" || schema=="5" || schema=="6") {
                    int count=int.Parse(data["lotCount"],CultureInfo.InvariantCulture);if(count<0||count>256)throw new FormatException();
                    var seen=new HashSet<string>(StringComparer.Ordinal);
                    for(int i=0;i<count;i++){string key="lot"+i+"_";var l=new OreLot{Origin=data[key+"origin"],SourceId=uint.Parse(data[key+"source"],CultureInfo.InvariantCulture),Quantity=double.Parse(data[key+"quantity"],CultureInfo.InvariantCulture)};
                        if(!l.Proof||!ValidAmount(l.Quantity)||l.Quantity<=0||!seen.Add(l.Key)||!s.Add(l))throw new FormatException();}
                    if(s.Eligible!=eligible)throw new FormatException();
                } else if(eligible>0) { if(!s.Add(new OreLot{Origin=s.Origin,SourceId=s.ObjectSourceId,Quantity=eligible}))throw new FormatException(); }
                if(s.Invalid&&s.lots.Count>0)throw new FormatException();
                bool mixed=s.lots.Select(l=>ObjectMarkets.Commodity(l.Origin)).Distinct(StringComparer.Ordinal).Skip(1).Any();
                s.LegacyMixed=schema=="5"||schema=="6" ? bool.Parse(data["legacyMixed"]) : s.lots.Select(l=>l.Origin).Distinct(StringComparer.Ordinal).Skip(1).Any();
                if(mixed&&!s.LegacyMixed)throw new FormatException();
                if(!s.LegacyMixed && s.lots.Any(l=>!ObjectMarkets.SameCommodity(s.Origin,l.Origin)))throw new FormatException();
                // Preserve the saved assignment/source header. Multiple physical rock
                // classes can now be lots of one commodity; do not relabel on load.
                if(s.LastAmount==0)s.Empty(s.Capacity);
                else if(s.LegacyMixed&&!s.Invalid)s.Reason="Legacy mixed cargo: consume or recover; storage locked";
                else if(!s.Invalid&&s.BatchId.Length==0)s.Reason="Existing Ore is unverified";
                if(!s.Invalid&&s.Eligible>0&&OreOrigins.HasMarket(s.Origin)&&s.Reason==OreOrigins.Label(s.Origin)+"; no market yet") s.Reason=OreOrigins.Label(s.Origin)+"; verified";
            } catch(Exception e) when(e is FormatException||e is KeyNotFoundException||e is ArgumentException||e is OverflowException) { s.Invalidate("Missing, malformed or unsupported tracking data"); }
            return s;
        }
    }

    internal static class LotQuotes
    {
        internal static bool Price(OreLot lot, out double price)
        {
            price=0; if (!lot.Proof) return false;
            return ObjectMarkets.Reserved(lot.Origin) ? MarketQuotes.TryGetUnitPrice(lot.Origin,out price) : OrePricing.TryGetUnitPrice(lot.Origin,out price);
        }
    }

    public static class RecoveryPolicy
    {
        public static double Quote(TrackingState state, string vesselId, string manifest,
            double actual, double capacity, bool career, bool kerbinSurface)
        {
            if (state == null || !career || !kerbinSurface) return 0;
            state.ObserveCapacity(capacity);
            state.Observe(actual);
            if (state.Invalid || state.VesselId != vesselId || state.Manifest != manifest) return 0;
            return state.Lots.Sum(l => LotQuotes.Price(l, out double price) ? OrePricing.Calculate(l.Quantity, l.Quantity, price) : 0);
        }
    }

    public sealed class TransactionLedger
    {
        private readonly HashSet<string> entries = new HashSet<string>(StringComparer.Ordinal);
        public IEnumerable<string> Entries => entries;
        public void Clear() => entries.Clear();
        public void Restore(string key) { if (!string.IsNullOrEmpty(key)) entries.Add(key); }
        public bool TryClaimMany(string vesselId, IEnumerable<string> batchIds, double payout)
        {
            var ids = batchIds.ToArray();
            if (!TrackingState.ValidAmount(payout) || payout <= 0 || !Guid.TryParse(vesselId, out var vessel) || vessel == Guid.Empty || ids.Length == 0) return false;
            var keys = new HashSet<string>(StringComparer.Ordinal) { "v:" + vessel.ToString("N") };
            foreach (string id in ids) {
                if (!Guid.TryParse(id, out var batch) || batch == Guid.Empty || !keys.Add("b:" + batch.ToString("N"))) return false;
            }
            if (keys.Any(entries.Contains)) return false;
            foreach (string key in keys) entries.Add(key);
            return true;
        }
        public bool TryClaim(string vesselId, string batchId, double payout)
        {
            if (!TrackingState.ValidAmount(payout) || payout <= 0 ||
                !Guid.TryParse(vesselId, out var vessel) || vessel == Guid.Empty ||
                !Guid.TryParse(batchId, out var batch) || batch == Guid.Empty) return false;
            string v = "v:" + vessel.ToString("N"), b = "b:" + batch.ToString("N");
            if (entries.Contains(v) || entries.Contains(b)) return false;
            entries.Add(v); entries.Add(b); // Reserve BEFORE Funding; never retry an uncertain payout.
            return true;
        }
    }
}









