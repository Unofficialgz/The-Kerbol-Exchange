using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OreMarket;

internal static class Program
{
    private static int passed;
    private static readonly string Vessel = Guid.NewGuid().ToString("N");
    private const string Manifest = "1:SmallTank;2:MiniDrill";
    private static void Check(string name, bool condition)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        passed++; Console.WriteLine("PASS: " + name);
    }
    private static TrackingState Mined(double amount = 10)
    {
        var state = TrackingState.New(0);
        state.Mining("Mun", amount, amount, true, Vessel, Manifest);
        return state;
    }
    private static OreMarketTank Tank(double capacity, bool scaler)
    {
        var vessel = new Vessel();
        var part = new Part { persistentId = 1, vessel = vessel };
        part.partInfo = new AvailablePart { name = "SmallTank", partPrefab = part };
        part.Resources.Add(new PartResource { maxAmount = capacity });
        vessel.parts.Add(part);
        var tank = new OreMarketTank { part = part };
        part.Modules.Add(tank);
        if (scaler) part.Modules.Add(new ObservableScaler { part = part });
        tank.OnStart(PartModule.StartState.Flying);
        return tank;
    }
    private static void Drill(OreMarketTank tank, double units)
    {
        var resource = tank.part.Resources.Get("Ore");
        resource.amount += units;
        tank.State.Mining("Mun", units, resource.amount, true, tank.vessel.id.ToString("N"), VesselRules.Manifest(tank.vessel.parts));
        tank.Refresh();
    }
    private static void CompatibilityTests()
    {
        string reason;
        var plain = Tank(150, false);
        Check("adapter: stock tank without scaler accepted", VesselRules.Loaded(plain.vessel, plain, out reason));
        Drill(plain, 50);
        plain.FixedUpdate();
        Check("adapter: stock tank verified mining preserved", plain.State.Eligible == 50 && !plain.State.Invalid);

        var tank = Tank(150, true);
        var scaler = (ObservableScaler)tank.part.Modules[1];
        Check("inspection: scaling fields detected", ModuleSafetyInspector.ObservationReason(typeof(ObservableScaler)).Length > 0);
        Check("inspection: observable scaler admitted without identity whitelist", ModuleSafetyInspector.CanTrack(typeof(ObservableScaler), out reason));
        Check("adapter: installed unchanged scaler accepted", VesselRules.Loaded(tank.vessel, tank, out reason));
        Drill(tank, 50);
        tank.FixedUpdate();
        Check("adapter: unchanged scaler preserves verified Mun Ore", tank.State.Eligible == 50 && !tank.State.Invalid);
        var noop = new BaseEventDetails(); noop.Set("factorRelative", 1f);
        tank.OnPartScaleChanged(noop);
        Check("adapter: no-op scale notification preserves cargo", tank.State.Eligible == 50 && !tank.State.Invalid);
        Check("adapter: stable scaled-capable tank quotes 5000", RecoveryPolicy.Quote(tank.State, tank.vessel.id.ToString("N"), VesselRules.Manifest(tank.vessel.parts), 50, 150, true, true) == 5000);

        var saved = new ConfigNode(); tank.OnSave(saved);
        var reloaded = Tank(150, true);
        reloaded.part.Resources.Get("Ore").amount = 50;
        reloaded.vessel.id = tank.vessel.id;
        reloaded.OnLoad(saved); reloaded.OnStart(PartModule.StartState.Flying);
        Check("adapter: tracker ConfigNode persistence with scaler", reloaded.State.Eligible == 50 && reloaded.State.BatchId == tank.State.BatchId);
        var restoreMesh = new BaseEventDetails(); restoreMesh.Set("factorRelative", 2f);
        reloaded.OnPartScaleChanged(restoreMesh);
        Check("adapter: startup mesh restoration with unchanged Ore preserves cargo", !reloaded.State.Invalid && reloaded.State.Eligible == 50);
        reloaded.FixedUpdate();
        reloaded.part.Resources.Get("Ore").amount -= 7;
        reloaded.FixedUpdate();
        Check("adapter: scaler presence does not prevent legitimate consumption", reloaded.State.Eligible == 43 && !reloaded.State.Invalid);
        var ledger = new TransactionLedger();
        double quote = RecoveryPolicy.Quote(reloaded.State, reloaded.vessel.id.ToString("N"), VesselRules.Manifest(reloaded.vessel.parts), 43, 150, true, true);
        Check("adapter: remaining cargo recovery claim", quote == 4300 && ledger.TryClaim(reloaded.State.VesselId, reloaded.State.BatchId, quote));
        Check("adapter: scaled-capable cargo cannot be claimed twice", !ledger.TryClaim(reloaded.State.VesselId, reloaded.State.BatchId, quote));

        tank = Tank(150, true); scaler = (ObservableScaler)tank.part.Modules[1];
        tank.part.Resources.Get("Ore").amount = 20;
        scaler.Resize(2);
        tank.FixedUpdate();
        Check("adapter: pre-mining resized capacity becomes unverified baseline", tank.State.Capacity == 300 && tank.State.LastAmount == 40 && tank.State.Eligible == 0 && !tank.State.Invalid);
        Drill(tank, 50);
        Check("adapter: genuine Mun mining in resized tank", tank.State.Eligible == 50 && tank.State.LastAmount == 90 && !tank.State.Invalid);
        Check("adapter: stable resized tank accepted by vessel validation", VesselRules.Loaded(tank.vessel, tank, out reason));
        saved = new ConfigNode(); tank.OnSave(saved);
        reloaded = Tank(300, true);
        reloaded.vessel.id = tank.vessel.id;
        reloaded.part.Resources.Get("Ore").amount = 90;
        reloaded.OnLoad(saved); reloaded.OnStart(PartModule.StartState.Flying);
        Check("adapter: scaled capacity and mixed Ore persist", reloaded.State.Capacity == 300 && reloaded.State.Eligible == 50 && reloaded.State.LastAmount == 90);
        Check("adapter: prefab default capacity is not used for sale", RecoveryPolicy.Quote(reloaded.State, reloaded.vessel.id.ToString("N"), VesselRules.Manifest(reloaded.vessel.parts), 90, 300, true, true) == 5000);

        reloaded.FixedUpdate();
        var up = new BaseEventDetails(); up.Set("factorRelative", 2f);
        ((ObservableScaler)reloaded.part.Modules[1]).Resize(2);
        reloaded.OnPartScaleChanged(up);
        Check("adapter: post-mining scale operation discards verified Ore", reloaded.State.Invalid && reloaded.State.Eligible == 0);
        Check("adapter: resized cargo cannot be sold", RecoveryPolicy.Quote(reloaded.State, reloaded.vessel.id.ToString("N"), VesselRules.Manifest(reloaded.vessel.parts), 180, 600, true, true) == 0);
        saved = new ConfigNode(); reloaded.OnSave(saved);
        Check("adapter: invalidated resized batch stays invalid in save", OreMarketTank.ReadState(saved.GetNode(OreMarketTank.NodeName)).Invalid);

        tank = Tank(150, true); Drill(tank, 50); tank.FixedUpdate();
        scaler = (ObservableScaler)tank.part.Modules[1]; scaler.Resize(0.5);
        tank.FixedUpdate();
        Check("adapter: unnotified shrink detected by capacity monitoring", tank.State.Invalid && tank.State.Eligible == 0);
        tank = Tank(150, true); Drill(tank, 50); tank.FixedUpdate();
        tank.part.Resources.Get("Ore").maxAmount += 1e-10;
        tank.FixedUpdate();
        Check("adapter: tiny capacity change still invalidates", tank.State.Invalid && tank.State.Eligible == 0);
        tank = Tank(150, true); Drill(tank, 50); tank.FixedUpdate();
        tank.part.Resources.Get("Ore").amount += 1;
        tank.FixedUpdate();
        Check("adapter: unnotified Ore increase invalidates", tank.State.Invalid && tank.State.Eligible == 0);
        tank = Tank(150, true); Drill(tank, 50); tank.FixedUpdate();
        tank.OnPartScaleChanged(up);
        Check("adapter: scale event invalidates even when capacity restored before poll", tank.State.Invalid && tank.State.Eligible == 0);
        tank = Tank(150, true); Drill(tank, 50); tank.FixedUpdate();
        tank.OnPartScaleChanged(new BaseEventDetails());
        Check("adapter: malformed scale event fails closed", tank.State.Invalid && tank.State.Eligible == 0);
        tank = Tank(150, true); Drill(tank, 50); tank.FixedUpdate();
        tank.part.Resources.Get("Ore").amount = 0; tank.FixedUpdate();
        ((ObservableScaler)tank.part.Modules[1]).Resize(2); tank.FixedUpdate();
        Check("adapter: physically empty tank can safely rebase after scaling", !tank.State.Invalid && tank.State.Eligible == 0 && tank.State.Origin == "" && tank.State.Reason == "Waiting for Ore");
        tank = Tank(150, true); Drill(tank, 50); tank.FixedUpdate(); tank.vessel.packed = true;
        tank.OnPartScaleChanged(up);
        Check("adapter: scale operation while packed fails closed", tank.State.Invalid && tank.State.Eligible == 0);
        tank = Tank(150, true); Drill(tank, 50);
        tank.part.Resources.Get("Ore").maxAmount = 300;
        var saveBeforePhysics = new ConfigNode(); tank.OnSave(saveBeforePhysics);
        Check("adapter: save catches capacity change before physics sampling", tank.State.Invalid && tank.State.Eligible == 0);
        tank = Tank(150, true); tank.part.Modules.Add(new HarmlessPartModule { part = tank.part });
        Check("adapter: unrelated harmless module accepted", VesselRules.Loaded(tank.vessel, tank, out reason));
        var infoConfig = tank.part.partInfo.partConfig.AddNode("MODULE");
        infoConfig.AddValue("name", nameof(HarmlessPartModule)); infoConfig.AddValue("resourceName", "Ore");
        Check("adapter: informational Ore configuration is not a mutation", VesselRules.Loaded(tank.vessel, tank, out reason));
        var outputConfig = infoConfig.AddNode("OUTPUT_RESOURCE"); outputConfig.AddValue("ResourceName", "Ore");
        Check("adapter: unsupported configured Ore producer remains rejected", !VesselRules.Loaded(tank.vessel, tank, out reason));
        tank.part.partInfo.partConfig = new ConfigNode();
        PlanetaryTests.Seed();
        var proto = new ProtoVessel { landed = true, vesselID = tank.vessel.id };
        proto.orbitSnapShot.ReferenceBodyIndex = 1;
        var protoTank = new ProtoPartSnapshot { partName = "SmallTank", persistentId = 1, partInfo = tank.part.partInfo };
        protoTank.resources.Add(new ProtoPartResourceSnapshot { resourceName = "Ore" });
        protoTank.modules.Add(new ProtoPartModuleSnapshot { moduleName = nameof(OreMarketTank) });
        protoTank.modules.Add(new ProtoPartModuleSnapshot { moduleName = nameof(ObservableScaler) });
        proto.protoPartSnapshots.Add(protoTank);
        ProtoPartSnapshot recovered;
        Check("recovery adapter: stock tank with observable scaler accepted", VesselRules.Recovered(proto, out recovered, out reason) && recovered == protoTank);
        protoTank.modules.Add(new ProtoPartModuleSnapshot { moduleName = nameof(UnsafeTransfer) });
        Check("recovery adapter: unsupported transfer snapshot still rejected", !VesselRules.Recovered(proto, out recovered, out reason));
        protoTank.modules.RemoveAt(protoTank.modules.Count - 1);
        protoTank.modules.Add(new ProtoPartModuleSnapshot { moduleName = "MissingModule" });
        Check("recovery adapter: unresolved saved module still rejected", !VesselRules.Recovered(proto, out recovered, out reason));
        Check("inspection: observable write cannot hide later unsafe transfer", !ModuleSafetyInspector.CanTrack(typeof(UnsafeTransfer), out reason));
        tank.part.Modules.Add(new UnsafeTransfer { part = tank.part });
        Check("adapter: unsupported resource operation still rejected", !VesselRules.Loaded(tank.vessel, tank, out reason));
    }
    private static ModuleResourceHarvester AttachDrill(OreMarketTank tank)
    {
        var part = new Part { persistentId = 2, vessel = tank.vessel };
        part.partInfo = new AvailablePart { name = "MiniDrill", partPrefab = part };
        var drill = new ModuleResourceHarvester { part = part, ResourceName = "Ore", HarvesterType = 0, IsActivated = true };
        part.Modules.Add(drill);
        part.Modules.Add(new PowerConsumer { part = part });
        tank.vessel.parts.Add(part);
        return drill;
    }
    private static void StockOutput(ModuleResourceHarvester drill, double quantity, double time)
    {
        // Reproduce the inspected stock ordering: distribution to another part,
        // then the synchronous converter output notification. This is a DOUBLE,
        // not execution of the KSP resource broker.
        var tank = drill.vessel.parts.SelectMany(p => p.Modules).OfType<OreMarketTank>().Single();
        tank.part.Resources.Get("Ore").amount += quantity;
        drill.SetProcessedTime(time);
        Planetarium.UniversalTime = time;
        GameEvents.OnResourceConverterOutput.Fire(drill, "Ore", quantity);
    }
    private static void AttributionTests()
    {
        string reason;
        Check("argument inspection: unrelated EC consumer admitted", ModuleSafetyInspector.CanTrack(typeof(PowerConsumer), out reason));
        Check("argument inspection: computed EC amount admitted", ModuleSafetyInspector.CanTrack(typeof(ComputedAmountConsumer), out reason));
        Check("argument inspection: literal Ore operation rejected", !ModuleSafetyInspector.CanTrack(typeof(OreConsumer), out reason));
        Check("argument inspection: variable resource remains unknown/rejected", !ModuleSafetyInspector.CanTrack(typeof(UnknownConsumer), out reason));
        Check("argument inspection: EC call cannot hide another Ore call", !ModuleSafetyInspector.CanTrack(typeof(MixedConsumer), out reason));
        Check("argument inspection: conditional resource remains unknown/rejected", !ModuleSafetyInspector.CanTrack(typeof(ConditionalConsumer), out reason));
        var observer = new MiningObserver(); observer.Awake();
        try
        {
            var tank = Tank(150, true); var drill = AttachDrill(tank);
            Check("callback: stock drill and tank are different parts", drill.part != tank.part && drill.part.Resources.Get("Ore") == null);
            Check("callback: EC consumer no longer rejects the mining vessel", VesselRules.Loaded(tank.vessel, tank, out reason));
            tank.FixedUpdate(); StockOutput(drill, 1, 100); tank.FixedUpdate();
            Check("callback: first output conservatively excluded", !tank.State.Invalid && tank.State.Eligible == 0 && tank.State.LastAmount == 1);
            StockOutput(drill, 5, 100.02); tank.FixedUpdate();
            Check("callback: separate tank stock output becomes verified", !tank.State.Invalid && tank.State.Eligible == 5 && tank.State.LastAmount == 6);
            GameEvents.OnResourceConverterOutput.Fire(drill, "Ore", 5);
            Check("callback: duplicate output notification cannot duplicate units", !tank.State.Invalid && tank.State.Eligible == 5);
            tank.part.Resources.Get("Ore").amount -= 2; tank.FixedUpdate();
            Check("callback: consumption after drilling debits verified cargo", tank.State.Eligible == 3 && !tank.State.Invalid);
            drill.IsActivated = false; tank.FixedUpdate();
            Check("callback: interruption preserves existing cargo", tank.State.Eligible == 3 && !tank.State.Invalid);
            drill.IsActivated = true; StockOutput(drill, 1, 105); tank.FixedUpdate();
            Check("callback: witnessed restart output credited across elapsed interval", tank.State.Eligible == 4 && !tank.State.Invalid);
            StockOutput(drill, 2, 105.02); tank.FixedUpdate();
            Check("callback: resumed loaded drilling credits once", tank.State.Eligible == 6 && !tank.State.Invalid);
            var saved = new ConfigNode(); tank.OnSave(saved);
            var savedPhysical = tank.Amount;
            var originalBatch = tank.State.BatchId;
            tank.OnLoad(saved); tank.OnStart(PartModule.StartState.Flying);
            GameEvents.onVesselUnloaded.Fire(tank.vessel);
            Check("callback: real tracker save/load retains mining balance", tank.State.Eligible == 6 && tank.State.BatchId == originalBatch && tank.Amount == savedPhysical);
            StockOutput(drill, 1, 110); tank.FixedUpdate();
            StockOutput(drill, 2, 110.02); tank.FixedUpdate();
            Check("callback: drilling after reload does not recreate prior units", tank.State.Eligible == 8 && !tank.State.Invalid);
            var ledger = new TransactionLedger();
            double quote = RecoveryPolicy.Quote(tank.State, tank.vessel.id.ToString("N"), VesselRules.Manifest(tank.vessel.parts), tank.Amount, tank.Capacity, true, true);
            Check("callback: newly attributed units quote correct bonus", quote == 800);
            Check("callback: recovery reserved once", ledger.TryClaim(tank.State.VesselId, tank.State.BatchId, quote));
            Check("callback: duplicate recovery denied", !ledger.TryClaim(tank.State.VesselId, tank.State.BatchId, quote));

            tank = Tank(150, false); drill = AttachDrill(tank);
            tank.part.Resources.Get("Ore").amount = 30; tank.FixedUpdate();
            Check("callback: preloaded Ore has no saleable origin", tank.State.Eligible == 0 && tank.State.Origin == "");
            StockOutput(drill, 1, 200); StockOutput(drill, 5, 200.02); tank.FixedUpdate();
            Check("callback: only new mining credited above preload", !tank.State.Invalid && tank.State.Eligible == 5 && tank.Amount == 36);
            tank.part.Resources.Get("Ore").amount += 3; tank.FixedUpdate();
            Check("callback: active drill alone cannot authorize an increase", tank.State.Invalid && tank.State.Eligible == 0);

            tank = Tank(150, false); drill = AttachDrill(tank); drill.IsActivated = false;
            tank.part.Resources.Get("Ore").amount = 50; tank.FixedUpdate();
            Check("callback: no-mining addition stays entirely unverified", tank.State.Eligible == 0 && tank.State.BatchId == "");
            Check("callback: no-mining cargo has zero sale quote", RecoveryPolicy.Quote(tank.State, tank.vessel.id.ToString("N"), VesselRules.Manifest(tank.vessel.parts), 50, 150, true, true) == 0);

            tank = Tank(150, false); drill = AttachDrill(tank);
            StockOutput(drill, 1, 300); StockOutput(drill, 2, 300.02);
            tank.part.Resources.Get("Ore").amount += 10;
            StockOutput(drill, 2, 300.04); tank.FixedUpdate();
            Check("callback: addition exceeding real output bound fails closed", tank.State.Invalid && tank.State.Eligible == 0);

            tank = Tank(150, false); drill = AttachDrill(tank);
            tank.part.Modules.Add(new UnsafeTransfer { part = tank.part });
            StockOutput(drill, 1, 400);
            string firstCause = tank.State.Reason;
            tank.part.Resources.Get("Ore").amount += 2; tank.FixedUpdate();
            Check("callback: unsupported Ore API still blocks attribution", tank.State.Invalid && firstCause.Contains("Unsupported"));
            Check("callback: later additions do not hide first validation failure", tank.State.Reason == firstCause && tank.State.Eligible == 0);

            tank = Tank(150, true); drill = AttachDrill(tank);
            StockOutput(drill, 1, 500); StockOutput(drill, 5, 500.02); tank.FixedUpdate();
            ((ObservableScaler)tank.part.Modules[1]).Resize(2); tank.FixedUpdate();
            Check("callback: capacity safeguard survives mining fix", tank.State.Invalid && tank.State.Eligible == 0);

            tank = Tank(150, false); drill = AttachDrill(tank);
            StockOutput(drill, 1, 600); StockOutput(drill, 5, 600.02); tank.FixedUpdate();
            GameEvents.onPartCouple.Fire(new GameEvents.FromToAction<Part, Part> { from = drill.part, to = tank.part });
            Check("callback: unchanged same-vessel coupling preserves cargo", !tank.State.Invalid && tank.State.Eligible == 5);

            tank = Tank(150, false); drill = AttachDrill(tank);
            tank.vessel.mainBody.bodyName = "Kerbin";
            StockOutput(drill, 1, 700); StockOutput(drill, 5, 700.02); tank.FixedUpdate();
            Check("callback: legitimate Kerbin drilling stays verified with zero sale price", !tank.State.Invalid && tank.State.Origin == "Kerbin" && tank.State.Eligible == 5 && tank.saleable.Split(' ')[0] == "0");
        }
        finally { observer.OnDestroy(); }
    }
    private static void WarpAndOriginTests()
    {
        var observer = new MiningObserver(); observer.Awake();
        try
        {
            var tank = Tank(1000, true); var drill = AttachDrill(tank);
            Check("origin: neutral waiting message before drilling", tank.State.Reason == "Waiting for Ore");
            StockOutput(drill, 1, 100); StockOutput(drill, 5, 100.02); tank.FixedUpdate();
            Check("warp: normal speed establishes five verified units", tank.State.Eligible == 5 && !tank.State.Invalid);
            TimeWarp.fixedDeltaTime = 0.08f;
            StockOutput(drill, 2, 100.10); tank.FixedUpdate();
            Check("warp: physics warp output credited", tank.State.Eligible == 7 && !tank.State.Invalid);
            tank.vessel.packed = true; TimeWarp.fixedDeltaTime = 0.02f; GameEvents.onVesselGoOnRails.Fire(tank.vessel);
            Check("warp: entering rails preserves cargo", tank.State.Eligible == 7 && !tank.State.Invalid);
            StockOutput(drill, 10, 20000); tank.FixedUpdate();
            Check("warp: loaded packed stock output credited across long interval", tank.State.Eligible == 17 && !tank.State.Invalid);
            StockOutput(drill, 3, 20000.04); tank.FixedUpdate();
            Check("warp: converter interval larger than physics step credited", tank.State.Eligible == 20 && !tank.State.Invalid);
            GameEvents.OnResourceConverterOutput.Fire(drill, "Ore", 3);
            Check("warp: repeated timestamp/output cannot duplicate credit", tank.State.Eligible == 20 && !tank.State.Invalid);
            Check("warp: observer leaves stock activation unchanged", drill.IsActivated);
            var saved = new ConfigNode(); tank.OnSave(saved);
            var oldBatch = tank.State.BatchId;
            GameEvents.onVesselUnloaded.Fire(tank.vessel);
            tank.OnLoad(saved); tank.OnStart(PartModule.StartState.Flying);
            Check("warp: packed save/load preserves valid batch", tank.State.Eligible == 20 && tank.State.BatchId == oldBatch && !tank.State.Invalid);
            StockOutput(drill, 2, 25000); tank.FixedUpdate();
            Check("warp: first output after true unload uncredited without cargo loss", tank.State.Eligible == 20 && !tank.State.Invalid);
            StockOutput(drill, 2, 26000); tank.FixedUpdate();
            Check("warp: loaded warp resumes after unload anchor", tank.State.Eligible == 22 && !tank.State.Invalid);
            tank.vessel.packed = false; GameEvents.onVesselGoOffRails.Fire(tank.vessel);
            TimeWarp.fixedDeltaTime = 0.02f; StockOutput(drill, 1, 26000.02); tank.FixedUpdate();
            Check("warp: exiting warp resumes normal attribution", tank.State.Eligible == 23 && !tank.State.Invalid);
            drill.IsActivated = false; StockOutput(drill, 4, 26010);
            Check("warp: temporarily unverifiable stock output preserves old cargo", tank.State.Eligible == 23 && !tank.State.Invalid && tank.State.Reason.Contains("uncredited"));
            saved = new ConfigNode(); tank.OnSave(saved); tank.OnLoad(saved); tank.OnStart(PartModule.StartState.Flying);
            Check("warp: temporary pause survives save without becoming invalid", tank.State.Eligible == 23 && !tank.State.Invalid);
            drill.IsActivated = true; StockOutput(drill, 1, 26011); StockOutput(drill, 2, 26012); tank.FixedUpdate();
            Check("warp: supported output resumes after temporary condition", tank.State.Eligible == 25 && !tank.State.Invalid);
            tank.vessel.loaded = false; GameEvents.onVesselUnloaded.Fire(tank.vessel);
            GameEvents.onVesselWasModified.Fire(tank.vessel);
            Check("warp: loaded flag transition is not custody invalidation", tank.State.Eligible == 25 && !tank.State.Invalid);
            StockOutput(drill, 4, 26020);
            Check("warp: witnessed unloaded stock output uncredited, old cargo retained", tank.State.Eligible == 25 && !tank.State.Invalid);
            tank.vessel.loaded = true; StockOutput(drill, 1, 26021); StockOutput(drill, 2, 26022); tank.FixedUpdate();
            Check("warp: attribution resumes after true unload", tank.State.Eligible == 27 && !tank.State.Invalid);
            double quote = RecoveryPolicy.Quote(tank.State, tank.vessel.id.ToString("N"), VesselRules.Manifest(tank.vessel.parts), tank.Amount, tank.Capacity, true, true);
            var ledger = new TransactionLedger();
            Check("warp: only supported Mun units enter payout", quote == 2700);
            Check("warp: recovery claimed once", ledger.TryClaim(tank.State.VesselId, tank.State.BatchId, quote));
            Check("warp: duplicate recovery denied", !ledger.TryClaim(tank.State.VesselId, tank.State.BatchId, quote));

            tank = Tank(1000, true); drill = AttachDrill(tank);
            StockOutput(drill, 1, 30000); StockOutput(drill, 5, 30001); tank.FixedUpdate();
            tank.vessel.packed = true; GameEvents.onVesselGoOnRails.Fire(tank.vessel);
            tank.part.Resources.Get("Ore").amount += 1; tank.FixedUpdate();
            Check("warp: unexplained packed addition invalidates cargo", tank.State.Invalid && tank.State.Eligible == 0);
            saved = new ConfigNode(); tank.OnSave(saved); tank.OnLoad(saved); tank.OnStart(PartModule.StartState.Flying);
            tank.vessel.packed = false; GameEvents.onVesselGoOffRails.Fire(tank.vessel);
            StockOutput(drill, 1, 30002);
            Check("warp: unsafe old batch not automatically repaired", tank.State.Invalid && tank.State.Eligible == 0);
            tank.ResetEmptyBatch();
            Check("reset: nonempty invalid cargo cannot be certified by reset", tank.State.Invalid);
            tank.part.Resources.Get("Ore").amount = 0; tank.ResetEmptyBatch();
            StockOutput(drill, 1, 30003); StockOutput(drill, 2, 30004); tank.FixedUpdate();
            Check("reset: explicit empty reset permits fresh genuine mining", !tank.State.Invalid && tank.State.Eligible > 0);

            tank = Tank(1000, false); drill = AttachDrill(tank);
            StockOutput(drill, 1, 40000); StockOutput(drill, 5, 40001); tank.FixedUpdate();
            tank.vessel.packed = true; GameEvents.onVesselGoOnRails.Fire(tank.vessel);
            tank.part.Resources.Get("Ore").amount += 10; StockOutput(drill, 1, 40002);
            Check("warp: extra addition within output interval still rejected", tank.State.Invalid && tank.State.Eligible == 0);

            foreach (string body in PlanetaryTests.Supported)
            {
                tank = Tank(1000, false); tank.vessel.mainBody.bodyName = body; drill = AttachDrill(tank);
                Check("origin: neutral waiting on " + body, tank.State.Reason == "Waiting for Ore");
                StockOutput(drill, 1, 50000);
                tank.vessel.packed = true; GameEvents.onVesselGoOnRails.Fire(tank.vessel);
                StockOutput(drill, 5, 51000); tank.FixedUpdate();
                tank.vessel.packed = false; GameEvents.onVesselGoOffRails.Fire(tank.vessel);
                Check("origin: actual body identified on " + body, tank.State.Origin == body && tank.State.Eligible == 5 && tank.oreOrigin == body + " Ore" && !tank.State.Invalid);
                saved = new ConfigNode(); tank.OnSave(saved); tank.OnLoad(saved); tank.OnStart(PartModule.StartState.Flying);
                Check("origin: persisted body on " + body, tank.State.Origin == body && tank.State.Eligible == 5 && !tank.State.Invalid);
                quote = RecoveryPolicy.Quote(tank.State, tank.vessel.id.ToString("N"), VesselRules.Manifest(tank.vessel.parts), tank.Amount, tank.Capacity, true, true);
                Check("origin: universal market policy on " + body, quote == (body == "Kerbin" ? 0 : 500) && tank.saleable.Split(' ')[0] == (body == "Kerbin" ? "0" : "5"));
            }
            tank = Tank(1000, false); drill = AttachDrill(tank);
            StockOutput(drill, 1, 60000); StockOutput(drill, 5, 60001);
            tank.vessel.mainBody.bodyName = "Minmus"; StockOutput(drill, 1, 60002);
            Check("origin: bypassed routing cannot admit another origin", tank.State.Invalid && tank.State.Eligible == 0);
            tank.part.Resources.Get("Ore").amount = 0; tank.ResetEmptyBatch();
            Check("origin: empty reset allowed on other bodies", !tank.State.Invalid && tank.State.Reason == "Waiting for Ore");
            Check("warp: no observer disabled the drill", drill.IsActivated);
            var excluded = TrackingState.New(0, 100);
            excluded.Mining("Mun", 10, 10, true, Vessel, Manifest);
            excluded.Mining("Mun", 5, 13, false, Vessel, Manifest);
            Check("warp: excluded production cannot hide consumption behind net gain", excluded.Eligible == 8 && !excluded.Invalid);
            excluded = TrackingState.New(0, 100); excluded.Mining("Mun", 10, 10, true, Vessel, Manifest);
            excluded.Mining("Mun", 5, 6, false, Vessel, Manifest);
            Check("warp: excluded production cannot replace consumed verified units", excluded.Eligible == 1 && !excluded.Invalid);
        }
        finally { TimeWarp.fixedDeltaTime = 0.02f; observer.OnDestroy(); }
    }
    private static void AbundanceDiagnosticTests()
    {
        Check("diagnostics: exact stock warning identified", AbundanceWarningDiagnostics.Matches("Insufficient resource abundance - need at least 2.5%"));
        Check("diagnostics: unrelated screen text ignored", !AbundanceWarningDiagnostics.Matches("OreMarket: Sold 50 units"));
        Check("diagnostics: absent text ignored", !AbundanceWarningDiagnostics.Matches(null));
        var tank = Tank(150, true); var drill = AttachDrill(tank);
        tank.vessel.mainBody.bodyName = "Kerbin";
        drill.IsActivated = false;
        drill.part.Modules.Add(new ModuleAnimationGroup { part = drill.part, isDeployed = false });
        HarvestProbe.Attempts = 0;
        string reason;
        Check("observation: inactive harvester validation succeeds without production", VesselRules.Loaded(tank.vessel, tank, out reason) && HarvestProbe.Attempts == 0);
        ModuleSafetyInspector.CanTrack(typeof(ModuleResourceHarvester), out reason);
        Check("observation: IL inspection never executes inspected methods", HarvestProbe.Attempts == 0);
        tank.FixedUpdate();
        var saved = new ConfigNode(); tank.OnSave(saved); tank.Refresh();
        Check("observation: tank UI/save never probes harvester recipes", HarvestProbe.Attempts == 0 && !drill.IsActivated);
        string snapshot = AbundanceWarningDiagnostics.Describe(tank.vessel);
        Check("diagnostics: snapshot reports stopped retracted state", snapshot.Contains("active=False") && snapshot.Contains("retracted") && snapshot.Contains("body=Kerbin"));
        Check("diagnostics: snapshot never executes harvest/info probes", HarvestProbe.Attempts == 0);
        FlightGlobals.ActiveVessel = tank.vessel;
        ScreenMessages.Instance.ActiveMessages.Clear();
        var warning = new ScreenMessage { message = "Insufficient resource abundance - need at least 2.5%", startTime = 1 };
        ScreenMessages.Instance.ActiveMessages.Add(warning);
        int before = Log.Lines.Count;
        var logger = new AbundanceWarningDiagnostics();
        UnityEngine.Time.realtimeSinceStartup = 1; logger.LateUpdate();
        Check("diagnostics: existing warning logged passively", Log.Lines.Count == before + 1 && Log.Lines.Last().Contains("NOT caller trace"));
        logger.LateUpdate();
        Check("diagnostics: unchanged warning not spammed", Log.Lines.Count == before + 1);
        warning.startTime = 2; UnityEngine.Time.realtimeSinceStartup = 2; logger.LateUpdate();
        Check("diagnostics: repeated warning logging throttled", Log.Lines.Count == before + 1);
        warning.startTime = 7; UnityEngine.Time.realtimeSinceStartup = 7; logger.LateUpdate();
        Check("diagnostics: repeated warnings summarized", Log.Lines.Count == before + 2 && Log.Lines.Last().Contains("refreshesSinceLog=2"));
        Check("diagnostics: warning text/timing/list never modified", warning.startTime == 7 && warning.message.Contains("2.5%") && ScreenMessages.Instance.ActiveMessages.Single() == warning);
        Check("diagnostics: no activation, deployment or production side effects", HarvestProbe.Attempts == 0 && !drill.IsActivated && !((ModuleAnimationGroup)drill.part.Modules.Last()).isDeployed);
        ScreenMessages.Instance.ActiveMessages.Clear(); FlightGlobals.ActiveVessel = null;
        logger.LateUpdate();
        Check("diagnostics: no-vessel snapshot safe", AbundanceWarningDiagnostics.Describe(null) == "activeVessel=none");
        // Positive control: the fake probe would be detectable if actually called.
        drill.PrepareRecipe(1);
        Check("diagnostics: probe counter positive control", HarvestProbe.Attempts == 1);
        HarvestProbe.Attempts = 0;
        var observer = new MiningObserver(); observer.Awake();
        try
        {
            tank = Tank(150, true); drill = AttachDrill(tank);
            StockOutput(drill, 1, 100); StockOutput(drill, 5, 101); tank.FixedUpdate();
            FlightGlobals.ActiveVessel = tank.vessel;
            ScreenMessages.Instance.ActiveMessages.Add(new ScreenMessage { message = warning.message, startTime = 20 });
            UnityEngine.Time.realtimeSinceStartup = 20; logger.LateUpdate();
            Check("diagnostics: existing verified Mun cargo unchanged", tank.State.Eligible == 5 && !tank.State.Invalid && tank.saleable.Split(' ')[0] == "5");
            Check("diagnostics: real mining callback does not probe recipes", HarvestProbe.Attempts == 0 && drill.IsActivated);
        }
        finally { observer.OnDestroy(); ScreenMessages.Instance.ActiveMessages.Clear(); FlightGlobals.ActiveVessel = null; }
    }
    public static int Main()
    {
        try
        {
            PlanetaryTests.Seed();
            PricingTests.UseUniformFixture();
            Check("zero bonus", TrackingState.Bonus(0, 10) == 0);
            Check("10 units = 1000", TrackingState.Bonus(10, 10) == 1000);
            Check("50 units = 5000", TrackingState.Bonus(50, 50) == 5000);
            Check("physical cap", TrackingState.Bonus(20, 3) == 300);
            foreach (double invalid in new[] { -1d, double.NaN, double.NegativeInfinity, double.PositiveInfinity })
            {
                Check("invalid eligible " + invalid, TrackingState.Bonus(invalid, 10) == 0);
                Check("invalid physical " + invalid, TrackingState.Bonus(10, invalid) == 0);
                var s = TrackingState.New(0);
                s.Mining("Mun", invalid, 10, true, Vessel, Manifest);
                Check("invalid output " + invalid, s.Invalid && s.Eligible == 0);
            }
            Check("overflow yields zero", TrackingState.Bonus(double.MaxValue, double.MaxValue) == 0);
            var state = Mined();
            Check("verified mining tracked", state.Eligible == 10 && !state.Invalid);
            state.Observe(10);
            Check("unchanged balance retained", state.Eligible == 10);
            state.Observe(9);
            Check("partial consumption debits verified balance", !state.Invalid && state.Eligible == 9);
            state.Mining("Mun", 1, 10, true, Vessel, Manifest);
            Check("later genuine mining adds to remaining balance", state.Eligible == 10);
            state = Mined(); state.Observe(11);
            Check("unknown addition invalidates", state.Invalid && state.Eligible == 0);
            state = Mined(); state.Observe(10 + 1e-10);
            Check("tiny unknown addition invalidates", state.Invalid && state.Eligible == 0);
            Check("VAB preload ineligible", TrackingState.New(10).Eligible == 0);
            state = TrackingState.New(0); state.Mining("Kerbin", 10, 10, true, Vessel, Manifest);
            Check("Kerbin origin is verified with zero reward", !state.Invalid && state.Origin == "Kerbin" && RecoveryPolicy.Quote(state, Vessel, Manifest, 10, state.Capacity, true, true) == 0);
            state = TrackingState.New(0); state.Mining("Minmus", 10, 10, true, Vessel, Manifest);
            Check("verified Minmus origin has universal market", !state.Invalid && state.Origin == "Minmus" && RecoveryPolicy.Quote(state, Vessel, Manifest, 10, state.Capacity, true, true) == 1000);
            state = TrackingState.New(0); state.Mining("Mun", 100, 2, true, Vessel, Manifest);
            Check("nominal output is only an upper bound", state.Eligible == 2);
            state.Mining("Mun", 100, 2, true, Vessel, Manifest);
            Check("full tank does not generate value", state.Eligible == 2);
            state = TrackingState.New(0); state.Mining("Mun", 1, 2, true, Vessel, Manifest);
            Check("excess balance increase rejected", state.Invalid && state.Eligible == 0);
            state = TrackingState.New(0); state.Mining("Mun", 10, 10, false, Vessel, Manifest);
            Check("catch-up output not credited", state.Eligible == 0 && !state.Invalid);
            state.Mining("Mun", 2, 12, true, Vessel, Manifest);
            Check("later loaded output credited", state.Eligible == 2);
            state = Mined(); state.Mining("Mun", 1, 11, true, Vessel, Manifest + ";3:probe");
            Check("changed topology rejected", state.Invalid && state.Eligible == 0);
            state = Mined(); state.Mining("Mun", 1, 11, true, Guid.NewGuid().ToString("N"), Manifest);
            Check("changed vessel identity rejected", state.Invalid && state.Eligible == 0);
            state = Mined(10.125);
            var saved = state.Save();
            CultureInfo original = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var loaded = TrackingState.Load(saved);
            CultureInfo.CurrentCulture = original;
            Check("culture-independent save/load", loaded.Eligible == 10.125 && loaded.LastAmount == 10.125 && !loaded.Invalid);
            Check("identity persists", loaded.BatchId == state.BatchId && loaded.VesselId == state.VesselId && loaded.Manifest == Manifest);
            loaded.Observe(10.125);
            Check("supported reload retains eligibility", loaded.Eligible == 10.125);
            loaded.Observe(10);
            Check("reload decrease debits balance", !loaded.Invalid && loaded.Eligible == 10);
            saved = state.Save(); saved["eligible"] = "11";
            Check("persisted amount above physical rejected", TrackingState.Load(saved).Invalid);
            saved = state.Save(); saved["eligible"] = "-1";
            Check("negative saved amount rejected", TrackingState.Load(saved).Invalid);
            saved = state.Save(); saved["eligible"] = "NaN";
            Check("NaN saved amount rejected", TrackingState.Load(saved).Invalid);
            saved = state.Save(); saved["schema"] = "999";
            Check("unknown schema rejected", TrackingState.Load(saved).Invalid);
            saved = state.Save(); saved.Remove("batchId");
            Check("incomplete provenance rejected", TrackingState.Load(saved).Invalid);
            saved = state.Save(); saved["batchId"] = "";
            Check("positive quantity requires batch identity", TrackingState.Load(saved).Invalid);
            state = TrackingState.New(20, 100);
            Check("transporting Ore to Mun adds no provenance", state.Eligible == 0 && state.BatchId.Length == 0);
            state.Observe(20);
            Check("landing alone cannot promote Ore", state.Eligible == 0);
            state.Mining("Mun", 10, 30, true, Vessel, Manifest);
            Check("mixed tank credits only new Mun mining", state.Eligible == 10);
            state.Observe(24);
            Check("mixed tank consumes verified units first", state.Eligible == 4);
            state = TrackingState.Load(state.Save());
            Check("partial consumption persists", state.Eligible == 4 && state.LastAmount == 24 && state.Capacity == 100 && state.Origin == "Mun");
            Check("career Kerbin sale quotes remaining verified Ore", RecoveryPolicy.Quote(state, Vessel, Manifest, 24, 100, true, true) == 400);
            Check("sandbox sale denied", RecoveryPolicy.Quote(state, Vessel, Manifest, 24, 100, false, true) == 0);
            Check("non Kerbin surface sale denied", RecoveryPolicy.Quote(state, Vessel, Manifest, 24, 100, true, false) == 0);
            Check("changed custody sale denied", RecoveryPolicy.Quote(state, Vessel, Manifest + ";extra", 24, 100, true, true) == 0);
            state.Observe(19);
            Check("consumption past verified balance leaves unverified Ore", state.Eligible == 0 && state.LastAmount == 19);
            Check("unverified remainder has zero quote", RecoveryPolicy.Quote(state, Vessel, Manifest, 19, 100, true, true) == 0);
            state = TrackingState.New(20, 100);
            state.Mining("Mun", 10, 30, true, Vessel, Manifest);
            state.Mining("Mun", 2, 27, true, Vessel, Manifest);
            Check("simultaneous net consumption never credits production twice", state.Eligible == 7 && state.LastAmount == 27);
            state.Observe(0);
            Check("empty tank has no eligible remainder", state.Eligible == 0);
            state = TrackingState.New(0, 100);
            state.Mining("Mun", 50, 50, true, Vessel, Manifest);
            var beforeSale = state.Save();
            Check("50 verified units quote exactly 5000", RecoveryPolicy.Quote(state, Vessel, Manifest, 50, 100, true, true) == 5000);
            var quickloaded = TrackingState.Load(beforeSale);
            Check("quickload restores pre-sale provenance", RecoveryPolicy.Quote(quickloaded, Vessel, Manifest, 50, 100, true, true) == 5000);
            Check("capacity modification rejects sale", RecoveryPolicy.Quote(quickloaded, Vessel, Manifest, 50, 200, true, true) == 0 && quickloaded.Invalid);
            saved = state.Save(); saved["origin"] = "";
            Check("missing saved origin rejected", TrackingState.Load(saved).Invalid);
            saved = state.Save(); saved["capacity"] = "49";
            Check("saved quantity above capacity rejected", TrackingState.Load(saved).Invalid);
            state.Observe(51);
            Check("unknown addition stays invalid after persistence", TrackingState.Load(state.Save()).Invalid);
            state = Mined(10.125);
            var ledger = new TransactionLedger();
            Check("first payout claim accepted", ledger.TryClaim(Vessel, state.BatchId, 1000));
            Check("duplicate payout rejected", !ledger.TryClaim(Vessel, state.BatchId, 1000));
            Check("same batch changed vessel rejected", !ledger.TryClaim(Guid.NewGuid().ToString(), state.BatchId, 1000));
            Check("same vessel changed batch rejected", !ledger.TryClaim(Vessel, Guid.NewGuid().ToString(), 1000));
            var restored = new TransactionLedger();
            foreach (string key in ledger.Entries) restored.Restore(key);
            Check("ledger persistence rejects duplicates", !restored.TryClaim(Vessel, state.BatchId, 1000));
            Check("Guid format cannot bypass ledger", !restored.TryClaim(Guid.Parse(Vessel).ToString("D"), Guid.Parse(state.BatchId).ToString("D"), 1000));
            foreach (double invalid in new[] { 0d, -1d, double.NaN, double.PositiveInfinity })
                Check("invalid payout rejected " + invalid, !restored.TryClaim(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), invalid));
            Check("empty identity rejected", !restored.TryClaim("", state.BatchId, 1));
            Check("zero Guid rejected", !restored.TryClaim(Guid.Empty.ToString(), state.BatchId, 1));
            var guard = new SamplingGuard();
            Check("first converter sample excluded", !guard.Accept(100, 100));
            Check("loaded physics sample accepted", guard.Accept(100.02, 100.02));
            guard.Reset();
            Check("pre-observation backlog excluded", !guard.Accept(10000, 20000));
            Check("final large catch-up interval excluded", !guard.Accept(20000, 20000));
            Check("loaded work after catch-up accepted", guard.Accept(20000.02, 20000.02));
            Check("duplicate converter timestamp excluded", !guard.Accept(20000.02, 20000.02));
            Check("backward converter clock excluded", !guard.Accept(1, 1));
            Check("NaN converter clock excluded", !guard.Accept(double.NaN, 1));
            Check("post-NaN first good sample excluded", !guard.Accept(1, 1));
            Check("future converter clock excluded", !guard.Accept(2, 1));
            string moduleReason;
            Check("informational extra module accepted", ModuleSafetyInspector.Allows(typeof(InfoModule), out moduleReason));
            state = Mined();
            if (!ModuleSafetyInspector.Allows(typeof(InfoModule), out moduleReason)) state.Invalidate(moduleReason);
            Check("harmless module preserves eligibility", state.Eligible == 10 && !state.Invalid);
            Check("direct resource writer rejected", !ModuleSafetyInspector.Allows(typeof(ResourceWriter), out moduleReason));
            Check("indirect resource writer rejected", !ModuleSafetyInspector.Allows(typeof(IndirectWriter), out moduleReason));
            Check("reflection alone does not establish a resource write", ModuleSafetyInspector.Allows(typeof(OpaqueModule), out moduleReason));
            state.Observe(11);
            Check("extra module cannot hide observed addition", state.Invalid && state.Eligible == 0);
            state = TrackingState.New(10);
            state.Mining("Mun", 1, 11, true, Vessel, Manifest);
            Check("only mined increment eligible above VAB preload", state.Eligible == 1);
            state = TrackingState.Load(Mined().Save());
            Check("compatibility change retains saved quantity", state.Eligible == 10 && !state.Invalid);
            new StockOreTransfers().Awake();
            CompatibilityTests();
            AttributionTests();
            WarpAndOriginTests();
            AbundanceDiagnosticTests();
            RecoveryTests.Run(Check);
            PlanetaryTests.Run(Check);
            PricingTests.Run(Check);
            TerminalTests.Run(Check);
            MarketTests.Run(Check);
            Chart051Tests.Run(Check);
            Events060Tests.Run(Check);
            Events061Tests.Run(Check);
            Monitor062Tests.Run(Check);
            Monitor063Tests.Run(Check);
            Objects070Tests.Run(Check);
            Objects071Tests.Run(Check);
            CosmeticSwitchTests.Run(Check);
            EngineBufferTests.Run(Check);
            ExtendedClassesTests.Run(Check);
            MultiTank072Tests.Run(Check);SingleOrigin0721Tests.Run(Check);Release0722Tests.Run(Check);Logistics073Tests.Run(Check);SharedRelease0731Tests.Run(Check);Branding090Tests.Run(Check);Presentation091Tests.Run(Check);
            // Deterministic varying amounts: no sequence can exceed the current physical balance.
            var random = new Random(12345);
            for (int run = 0; run < 100; run++)
            {
                state = TrackingState.New(0);
                double actual = 0;
                for (int tick = 0; tick < 100; tick++)
                {
                    if (tick % 3 == 0 && actual > 0)
                    {
                        actual *= random.NextDouble(); state.Observe(actual);
                        state = TrackingState.Load(state.Save());
                    }
                    double addition = random.NextDouble() * 20;
                    actual += addition;
                    state.Mining("Mun", addition, actual, true, Vessel, Manifest);
                    if (state.Eligible < 0 || state.Eligible > actual) throw new Exception("Bound invariant failed");
                }
            }
            Check("10,000 mixed mining/consumption/save-load steps preserve physical bound", true);
            Console.WriteLine("All " + passed + " assertions passed. Offline logic and adapter doubles only; KSP gameplay is not simulated.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}


public sealed class InfoModule
{
    public string Label = "information";
    public string Readout() { return Label.ToUpperInvariant(); }
}

public sealed class ResourceWriter
{
    public void Change(PartResource resource) { resource.amount = 100; }
}
public sealed class IndirectWriter
{
    public void Change(PartResource resource) { Helper(resource); }
    private static void Helper(PartResource resource) { resource.amount = 100; }
}
public sealed class OpaqueModule
{
    public void Change(System.Reflection.FieldInfo field, object resource) { field.SetValue(resource, 100d); }
}























