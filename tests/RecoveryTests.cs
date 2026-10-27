using System;
using System.Linq;
using OreMarket;
using KSP.UI.Screens;

// Executes the actual production RecoverySales scenario and event handler.
// Only the surrounding KSP objects/events/currency service are doubles.
internal static class RecoveryTests
{
    private static ConfigNode Copy(ConfigNode source)
    {
        var target = new ConfigNode { name = source.name };
        foreach (var value in source.values) target.AddValue(value.name, value.value);
        foreach (var child in source.nodes) target.nodes.Add(Copy(child));
        return target;
    }

    internal static ProtoVessel Cargo(string origin = "Mun", double mined = 50, double preload = 0, double? remaining = null)
    {
        var vessel = new Vessel();
        var part = new Part { persistentId = 11, vessel = vessel };
        part.partInfo = new AvailablePart { name = "SmallTank", partPrefab = part };
        part.Resources.Add(new PartResource { resourceName = "Ore", amount = preload, maxAmount = 150 });
        var tracker = new OreMarketTank { part = part }; part.Modules.Add(tracker); vessel.parts.Add(part);
        var drill = new Part { persistentId = 12, vessel = vessel };
        drill.partInfo = new AvailablePart { name = "MiniDrill", partPrefab = drill }; vessel.parts.Add(drill);
        drill.Modules.Add(new ModuleResourceHarvester { part = drill, ResourceName = "Ore" });
        tracker.OnStart(PartModule.StartState.Flying);
        if (mined > 0)
        {
            part.Resources.Get("Ore").amount += mined;
            tracker.State.Mining(origin, mined, part.Resources.Get("Ore").amount, true,
                vessel.id.ToString("N"), VesselRules.Manifest(vessel.parts));
        }
        var proto = new ProtoVessel { vesselID = vessel.id, landed = true };
        proto.orbitSnapShot.ReferenceBodyIndex = 1;
        foreach (var p in vessel.parts)
        {
            var snapshot = new ProtoPartSnapshot { persistentId = p.persistentId, partName = p.partInfo.name, partInfo = p.partInfo };
            foreach (var m in p.Modules)
            {
                var module = new ProtoPartModuleSnapshot { moduleName = m.moduleName };
                m.OnSave(module.moduleValues); snapshot.modules.Add(module);
            }
            foreach (var r in p.Resources) snapshot.resources.Add(new ProtoPartResourceSnapshot {
                resourceName = r.resourceName, amount = remaining ?? r.amount, maxAmount = r.maxAmount });
            proto.protoPartSnapshots.Add(snapshot);
        }
        return proto;
    }

    private static ProtoVessel Clone(ProtoVessel source)
    {
        var target = new ProtoVessel { vesselID = source.vesselID, landed = source.landed, splashed = source.splashed };
        target.orbitSnapShot.ReferenceBodyIndex = source.orbitSnapShot.ReferenceBodyIndex;
        foreach (var p in source.protoPartSnapshots)
        {
            var part = new ProtoPartSnapshot { persistentId = p.persistentId, partName = p.partName, partInfo = p.partInfo };
            foreach (var m in p.modules) part.modules.Add(new ProtoPartModuleSnapshot { moduleName = m.moduleName, moduleValues = Copy(m.moduleValues) });
            foreach (var r in p.resources) part.resources.Add(new ProtoPartResourceSnapshot { resourceName = r.resourceName, amount = r.amount, maxAmount = r.maxAmount });
            target.protoPartSnapshots.Add(part);
        }
        return target;
    }
    private static void Fire(ProtoVessel vessel, MissionRecoveryDialog dialog = null, float factor = 1)
        => GameEvents.onVesselRecoveryProcessingComplete.Fire(vessel, dialog, factor);
    internal static void Reset(RecoverySales sales)
    {
        sales.OnLoad(new ConfigNode()); Funding.Instance = new Funding { Funds = 1000 };
        HighLogic.CurrentGame = new Game(); ScreenMessages.Instance.ActiveMessages.Clear();
        MessageSystem.Instance = new MessageSystem();
    }

    internal static void Run(Action<string, bool> check)
    {
        PlanetaryTests.Seed();
        var sales = new RecoverySales(); sales.OnAwake();
        try
        {
            Funding.Instance = new Funding { Funds = 1000 };
            Fire(Cargo()); check("recovery: unloaded claim ledger fails closed", Funding.Instance.Calls == 0);
            Reset(sales);
            var cargo = Cargo(); var stale = Clone(cargo);
            // Stock has already refunded this separately at the processing event.
            Funding.Instance.Funds += 321;
            var dialog = new MissionRecoveryDialog { fundsEarned = 321, totalFunds = 1321 };
            Fire(cargo, dialog, 0.25f);
            check("recovery: real handler pays 50 verified Mun units x 100", Funding.Instance.Calls == 1 && Funding.Instance.LastAmount == 5000);
            check("recovery: stock refund remains separate and additive", Funding.Instance.Funds == 6321 && dialog.fundsEarned == 321 && dialog.totalFunds == 1321);
            check("recovery: bonus not reduced by stock distance recovery factor", Funding.Instance.LastAmount == 5000);
            check("recovery: clear sale notification", ScreenMessages.Instance.ActiveMessages.Single().message.Contains("50 units of Mun Ore at 100 funds/unit: bonus 5,000.00"));
            check("recovery: diagnostic includes units and transaction identity", Log.Lines.Any(l => l.Contains("Transaction completed: vessel=" + cargo.vesselID.ToString("N")) && l.Contains("units=50 requested=5000 applied=5000")));
            var saved = new ConfigNode(); sales.OnSave(saved);
            check("recovery: vessel and batch claims persisted", saved.GetValues("claimed").Length == 2);
            check("recovery: sold snapshot invalidated", OreMarketTank.ReadState(cargo.protoPartSnapshots[0].modules[0].moduleValues.GetNode(OreMarketTank.NodeName)).Invalid);
            Fire(cargo); Fire(stale);
            check("recovery: repeat callback and fresh stale snapshot pay once", Funding.Instance.Calls == 1);
            Funding.Instance.DuringAdd = null;
            sales.OnLoad(saved); Fire(stale);
            check("recovery: persisted claims block stale pre-sale cargo after load", Funding.Instance.Calls == 1);
            sales.OnSave(saved);
            check("recovery: repeated save does not duplicate claim values", saved.GetValues("claimed").Length == 2);
            var changedVessel = Clone(stale); changedVessel.vesselID = Guid.NewGuid();
            var provenance = changedVessel.protoPartSnapshots[0].modules[0].moduleValues.GetNode(OreMarketTank.NodeName);
            provenance.SetValue("vesselId", changedVessel.vesselID.ToString("N"), true);
            Fire(changedVessel); check("recovery: persisted batch claim rejects changed vessel identity", Funding.Instance.Calls == 1);
            var changedBatch = Clone(stale);
            changedBatch.protoPartSnapshots[0].modules[0].moduleValues.GetNode(OreMarketTank.NodeName).SetValue("batchId", Guid.NewGuid().ToString("N"), true);
            Fire(changedBatch); check("recovery: persisted vessel claim rejects changed batch identity", Funding.Instance.Calls == 1);
            sales.OnDestroy(); sales = new RecoverySales(); sales.OnAwake(); sales.OnLoad(saved); Fire(stale);
            check("recovery: scenario recreation restores transaction protection", Funding.Instance.Calls == 1);

            Reset(sales); cargo = Cargo(); stale = Clone(cargo);
            Funding.Instance.DuringAdd = () => Fire(stale);
            Fire(cargo);
            check("recovery: claim reserved before reentrant Funding callback", Funding.Instance.Calls == 1);
            Reset(sales); cargo = Cargo(); stale = Clone(cargo);
            Funding.Instance.ThrowAfterAdd = true; Fire(cargo); Fire(stale);
            check("recovery: ambiguous currency failure never retries", Funding.Instance.Calls == 1 && Funding.Instance.Funds == 6000);
            var failedSave = new ConfigNode(); sales.OnSave(failedSave); sales.OnLoad(failedSave); Fire(stale);
            check("recovery: uncertain transaction claim survives save/load", Funding.Instance.Calls == 1);

            Reset(sales); Fire(Cargo(remaining: 37.5));
            check("recovery: final unobserved consumption debits remaining verified Ore", Funding.Instance.LastAmount == 3750);
            Reset(sales); Fire(Cargo(preload: 20, remaining: 60));
            check("recovery: consumption debits verified before preloaded Ore", Funding.Instance.LastAmount == 4000);
            Reset(sales); Fire(Cargo(mined: 0, preload: 50));
            check("recovery: preloaded unverified Ore never pays", Funding.Instance.Calls == 0);
            Reset(sales); Fire(Cargo(remaining: 0));
            check("recovery: fully consumed verified Ore never pays", Funding.Instance.Calls == 0);
            Reset(sales); Fire(Cargo(mined: 0));
            check("recovery: empty tank never pays", Funding.Instance.Calls == 0);
            Reset(sales); Fire(Cargo(origin: "UnknownBody"));
            check("recovery: unsupported origin never pays", Funding.Instance.Calls == 0);
            Reset(sales); cargo = Cargo(); cargo.orbitSnapShot.ReferenceBodyIndex = 2; Fire(cargo);
            check("recovery: Mun surface recovery never pays", Funding.Instance.Calls == 0);
            Reset(sales); cargo = Cargo(); cargo.landed = false; Fire(cargo);
            check("recovery: Kerbin orbit recovery never pays", Funding.Instance.Calls == 0);
            Reset(sales); cargo = Cargo(); cargo.landed = false; cargo.splashed = true; Fire(cargo);
            check("recovery: Kerbin splashdown eligible", Funding.Instance.LastAmount == 5000);
            foreach (var mode in new[] { Game.Modes.SANDBOX, Game.Modes.SCIENCE_SANDBOX })
            {
                Reset(sales); HighLogic.CurrentGame.Mode = mode; Fire(Cargo());
                check("recovery: unsupported game mode " + mode, Funding.Instance.Calls == 0);
            }
            Reset(sales); HighLogic.CurrentGame = null; Fire(Cargo());
            check("recovery: missing game fails closed", Funding.Instance.Calls == 0);
            Reset(sales); Funding.Instance = null; Fire(Cargo());
            check("recovery: missing Funding produces diagnostic", Log.Lines.Last().Contains("Funding unavailable"));
            Reset(sales); Fire(Cargo(remaining: 51));
            check("recovery: unexplained final addition invalidates sale", Funding.Instance.Calls == 0);
            Reset(sales); cargo = Cargo(); cargo.protoPartSnapshots[0].resources[0].maxAmount = 300; Fire(cargo);
            check("recovery: final resize invalidates sale", Funding.Instance.Calls == 0);
            Reset(sales); cargo = Cargo(); cargo.protoPartSnapshots[1].persistentId++; Fire(cargo);
            check("recovery: changed intact manifest rejected", Funding.Instance.Calls == 0);
            Reset(sales); cargo = Cargo(); cargo.protoPartSnapshots[0].modules.Clear(); Fire(cargo);
            check("recovery: missing tracker rejected", Funding.Instance.Calls == 0);
            Reset(sales); cargo = Cargo(); var node = cargo.protoPartSnapshots[0].modules[0].moduleValues;
            node.nodes.Add(Copy(node.nodes[0])); Fire(cargo);
            check("recovery: ambiguous duplicate provenance rejected", Funding.Instance.Calls == 0);
            Reset(sales); Fire(null);
            check("recovery: missing snapshot rejected", Funding.Instance.Calls == 0);

            Reset(sales); cargo = Cargo(); var trackerNode = cargo.protoPartSnapshots[0].modules[0].moduleValues.GetNode(OreMarketTank.NodeName);
            trackerNode.SetValue("invalid", "True", true); trackerNode.SetValue("eligible", "0", true);
            trackerNode.SetValue("reason", "Ore output is not supported loaded Mun surface drilling", true);
            Fire(cargo); Fire(Clone(cargo));
            check("recovery: historical invalid warp cargo never recertified or paid", Funding.Instance.Calls == 0);
            check("recovery: invalid saved tracking rejection is visible once in stock inbox", MessageSystem.Instance.Messages.Count == 1 && MessageSystem.Instance.Messages[0].message.Contains("Saved reason: Ore output is not supported loaded Mun surface drilling"));
            var noClaims = new ConfigNode(); sales.OnSave(noClaims);
            check("recovery: rejected cargo creates no financial claim", noClaims.GetValues("claimed").Length == 0);
            Reset(sales); Fire(Cargo());
            check("recovery: completed sale has stock inbox receipt with balance and units", MessageSystem.Instance.Messages.Single().messageTitle == "The Kerbol Exchange: sale complete" && MessageSystem.Instance.Messages[0].message.Contains("Applied bonus: 5000") && MessageSystem.Instance.Messages[0].message.Contains("Balance after bonus: 6000"));
            Reset(sales); cargo = Cargo(); stale = Clone(cargo); Fire(cargo); Fire(cargo); Fire(stale);
            check("recovery: duplicate financial callback cannot create second sale receipt", MessageSystem.Instance.Messages.Count == 1);
            Reset(sales); MessageSystem.Instance = null; Fire(Cargo());
            check("recovery: absent stock inbox does not block or repeat payment", Funding.Instance.Calls == 1 && Funding.Instance.Funds == 6000);
            Reset(sales); MessageSystem.Instance.ThrowOnAdd = true; cargo = Cargo(); stale = Clone(cargo); Fire(cargo); Fire(stale);
            check("recovery: stock inbox exception cannot retry or undo payment", Funding.Instance.Calls == 1 && Funding.Instance.Funds == 6000);
            Reset(sales); cargo = Cargo(); cargo.vesselID = Guid.NewGuid(); Fire(cargo);
            check("recovery: mismatched saved vessel record reports explicit rejection", Funding.Instance.Calls == 0 && MessageSystem.Instance.Messages.Single().message.Contains("identity or part manifest does not match"));
            Reset(sales); cargo = Cargo(); cargo.protoPartSnapshots[0].modules[0].moduleValues.RemoveNodes(OreMarketTank.NodeName); Fire(cargo);
            check("recovery: missing saved provenance reports no sale without funds", Funding.Instance.Calls == 0 && MessageSystem.Instance.Messages.Single().message.Contains("Missing or duplicate saved Ore provenance"));
            Reset(sales); Fire(Cargo(origin: "UnknownBody"));
            check("recovery: unsupported origin has explanatory receipt", Funding.Instance.Calls == 0 && MessageSystem.Instance.Messages.Single().message.Contains("UnknownBody Ore"));
            Reset(sales); Fire(Cargo(remaining: 37.5));
            check("recovery: receipt shows partial remaining cargo calculation", MessageSystem.Instance.Messages.Single().message.Contains("Verified Mun Ore sold: 37.5 units") && Funding.Instance.LastAmount == 3750);
            Reset(sales); HighLogic.CurrentGame.Mode = Game.Modes.SANDBOX; Fire(Cargo());
            check("recovery: sandbox never creates sale receipt", Funding.Instance.Calls == 0 && MessageSystem.Instance.Messages.Count == 0);

            Reset(sales); cargo = Cargo(); cargo.protoPartSnapshots[0].modules[0].moduleValues.GetNode(OreMarketTank.NodeName).SetValue("vesselId", "", true); Fire(cargo);
            check("recovery: missing saved vessel record cannot pay", Funding.Instance.Calls == 0);

            foreach (string origin in PlanetaryTests.Supported.Where(o => o != "Kerbin"))
            {
                Reset(sales); cargo = Cargo(origin: origin, mined: 100); stale = Clone(cargo);
                Fire(cargo);
                check("universal recovery: " + origin + " pays 10000", Funding.Instance.Calls == 1 && Funding.Instance.LastAmount == 10000);
                check("universal recovery: receipt uses " + origin, MessageSystem.Instance.Messages.Single().message.Contains("Verified " + origin + " Ore sold: 100 units") && ScreenMessages.Instance.ActiveMessages.Single().message.Contains("units of " + origin + " Ore"));
                var claims = new ConfigNode(); sales.OnSave(claims); sales.OnLoad(claims); Fire(stale);
                check("universal recovery: persisted claims prevent duplicate " + origin, Funding.Instance.Calls == 1);
            }

            // A pre-sale quicksave restores BOTH funds and claims. It replays the sale
            // once in that restored timeline; the old payout balance is not retained.
            Reset(sales); cargo = Cargo(); stale = Clone(cargo); var beforeSale = new ConfigNode(); sales.OnSave(beforeSale);
            Fire(cargo); Funding.Instance = new Funding { Funds = 1000 }; sales.OnLoad(beforeSale); Fire(stale);
            check("recovery: coherent pre-sale quickload pays once without accumulating old funds", Funding.Instance.Funds == 6000 && Funding.Instance.Calls == 1);
            Reset(sales); var duplicate = new RecoverySales(); duplicate.OnAwake(); duplicate.OnLoad(new ConfigNode()); Fire(Cargo()); duplicate.OnDestroy();
            check("recovery: duplicate scenario cannot subscribe twice", Funding.Instance.Calls == 1);
            sales.OnDestroy(); Reset(sales); Fire(Cargo());
            check("recovery: destroyed scenario unsubscribes", Funding.Instance.Calls == 0);
        }
        finally { sales.OnDestroy(); Funding.Instance = null; HighLogic.CurrentGame = new Game(); }
    }
}





