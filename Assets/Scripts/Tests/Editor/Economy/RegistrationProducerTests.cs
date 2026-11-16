using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Businesses.Logging;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Economy.Farming.Timber;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using NUnit.Framework;

// A2 plan 26 (2026-11-13): the registration producer — the custody ledger
// is driven from the live path. Lots are registered at felling and driven
// Created -> Collected -> Loaded -> InTransit -> Delivered at the four choke
// points, so a live-registered lot reaches Processed at the F6 completion
// step. Tests are red-first: they fail (no record, unknown-lot refusals)
// until the ledger hooks land.
namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public sealed class RegistrationProducerTests
    {
        private static readonly EntityId Sawyer = EntityId.For(EntityKind.Person, 5002);

        private sealed class World
        {
            public LoggingStandRegistry Stands = new LoggingStandRegistry();
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public LogLotCustodyLedger Ledger;
            public List<string> Diagnostics = new List<string>();
            public EntityId Worker = EntityId.For(EntityKind.Person, 5);
            public LoggingOperationRuntime Runtime;
            public SawmillShopRuntime Mill;
            public TaskAuthority Tasks;
            public WorkTimeBudgetStore Budgets;
            public EquipmentTaskGate Gate;
            public SawmillLabourCounters Counters;
            public SawmillProductionDriver Driver;

            public World(int runDay, bool withLedger, float kilogramsPerLog = 300f)
            {
                Assert.IsNull(Stands.RegisterStand(new TimberStand
                {
                    StandId = "stand-sawmill-district",
                    DisplayName = "Sawmill District Pines",
                    LocationId = "sawmill-district",
                    Species = "white pine",
                    KilogramsPerLog = kilogramsPerLog,
                    StandingTimberUnits = 10,
                }));
                Assert.IsNull(Stands.GrantTimberRights("stand-sawmill-district", "logging-business-1", 200, "town-land-office"));
                Ledger = new LogLotCustodyLedger(Diagnostics);
                Runtime = withLedger
                    ? new LoggingOperationRuntime("logging-1", Ids, Ledger)
                    : new LoggingOperationRuntime("logging-1", Ids);
                Mill = new SawmillShopRuntime("mill-1", Ids);
                Tasks = new TaskAuthority();
                Budgets = new WorkTimeBudgetStore();
                Budgets.EnsureDay(runDay);
                Gate = BuildGate();
                Counters = new SawmillLabourCounters();
                Driver = withLedger
                    ? new SawmillProductionDriver(Mill, Tasks, Budgets, Gate, Counters, Diagnostics, Ledger)
                    : new SawmillProductionDriver(Mill, Tasks, Budgets, Gate, Counters, Diagnostics);
            }

            public LogLot FellViaRuntime(int units, int dayIndex)
            {
                LogLot lot = Runtime.RunFelling(Stands, "stand-sawmill-district", "logging-business-1", units, Worker, dayIndex, Diagnostics, FellingEquipmentGate());
                Assert.IsNotNull(lot, "RunFelling must produce a lot on a granted stand.");
                return lot;
            }

            public LogHaul Dispatch(LogLot lot, int dayIndex)
            {
                LogHaul haul = Runtime.DispatchLogHaul(lot.LotId, "mill-1", "mill-1-yard", LandingToMillJourney(), "landing-1", dayIndex, Diagnostics);
                Assert.IsNotNull(haul, "DispatchLogHaul must produce a haul on a routable journey.");
                return haul;
            }

            public void Advance(int minutes, int dayIndex)
            {
                Runtime.AdvanceHauls(minutes, Diagnostics, dayIndex);
            }

            public void Deliver(int dayIndex)
            {
                Runtime.DeliverArrivedHauls(Mill.LogStock, Diagnostics, dayIndex);
            }

            public string LotIdOf(LogLot lot)
            {
                return lot.LotId.ToString();
            }
        }

        private static EquipmentTaskGate BuildGate()
        {
            var registry = new BusinessWorkstations("mill-1");
            var station = registry.GetOrCreate("sawmill-saw-line");
            station.SpaceId = "mill-saw-floor";
            station.InstallComponent("saw-line-1");
            station.InstallComponent("log-carriage-1");
            station.InstallComponent("drive-system-1");
            station.MotivePowerSources.Add(new MotivePowerSource(MotivePowerKind.Steam, "recorded steam engine"));
            return new EquipmentTaskGate
            {
                WorkstationsFor = (kind, id) => registry,
                FindWorkstationDefinition = id => id == "sawmill-saw-line" ? WorkstationCatalog.SawmillSawLine : null,
                FindComponent = id => id == "saw-line-1"
                    ? new WorkstationComponentView { AssetId = id, Kind = "saw-line", Condition01 = 1f, IsUsable = true }
                    : id == "log-carriage-1"
                        ? new WorkstationComponentView { AssetId = id, Kind = "log-carriage", Condition01 = 1f, IsUsable = true }
                        : id == "drive-system-1"
                            ? new WorkstationComponentView { AssetId = id, Kind = "drive-system", Condition01 = 1f, IsUsable = true }
                            : (WorkstationComponentView?)null,
                SupportSupplier = () =>
                {
                    var support = new SupportContext();
                    support.ConsumableUnits[SupportContext.Key("consumable", "mill-lubrication")] = 1;
                    support.AvailableRepairCapabilities.Add("saw-filing");
                    return support;
                },
            };
        }

        private static EquipmentTaskGate FellingEquipmentGate()
        {
            return new EquipmentTaskGate
            {
                AssetsSupplier = () => new List<EquipmentAsset>
                {
                    new EquipmentAsset
                    {
                        AssetId = "test-felling-axe",
                        Kind = "felling-axe",
                        OwnerKind = "business",
                        OwnerId = "logging-1",
                        Condition01 = 1f,
                    },
                },
            };
        }

        private static JourneyModel LandingToMillJourney()
        {
            var model = new JourneyModel();
            Assert.IsNull(model.RegisterLocation(new JourneyLocation(
                "landing-1", JourneyLocationKind.Waypoint, "North Landing", 2f, 0f)));
            Assert.IsNull(model.RegisterLocation(new JourneyLocation(
                "mill-1-yard", JourneyLocationKind.Mill, "Local Sawmill", 0f, 0f)
                { BusinessId = "mill-1" }));
            Assert.IsNull(model.AddEdge("landing-1", "mill-1-yard", 2f, "river road"));
            return model;
        }

        private static int CountContaining(IReadOnlyList<string> diagnostics, string needle)
        {
            int count = 0;
            foreach (string line in diagnostics)
            {
                if (line != null && line.Contains(needle)) count++;
            }
            return count;
        }

        private static SawmillProductionJob Begin(World world, LogLot lot, int logsToSaw, int dayIndex)
        {
            SawmillProductionJob job = world.Driver.BeginProduction(world.LotIdOf(lot), Sawyer, logsToSaw, dayIndex, out string refusal);
            Assert.IsNotNull(job, "BeginProduction refused: " + refusal);
            Assert.IsNull(refusal);
            return job;
        }

        private static bool Run(World world, SawmillProductionJob job, int dayIndex)
        {
            return world.Driver.RunProduction(job.JobId, dayIndex, out string refusal);
        }

        [Test]
        public void T1_RegisterAtFelling()
        {
            var world = new World(203, withLedger: true);
            LogLot lot = world.FellViaRuntime(2, 200);

            LogLotCustodyRecord record = world.Ledger.GetLot(world.LotIdOf(lot));
            Assert.IsNotNull(record);
            Assert.AreEqual(LogLotCustodyState.Created, record.State);
            Assert.AreEqual("sawmill-district", record.HarvestLocationId);
            Assert.AreEqual("logging-business-1", record.CustodianId);
            Assert.AreEqual(2, record.LogUnits);
            Assert.AreEqual(600f, record.MassKg, 0.01f);
            Assert.AreEqual("stand-sawmill-district", record.StandId);
            Assert.AreEqual(200, record.FelledDayIndex);
            Assert.AreEqual(1, world.Runtime.LandingStock.Count);
            Assert.AreEqual(2, world.Runtime.LandingLogUnits);
        }

        [Test]
        public void T2_NullLedgerFellingStillFells()
        {
            var world = new World(203, withLedger: false);
            LogLot lot = world.FellViaRuntime(2, 200);

            Assert.IsNotNull(lot);
            Assert.AreEqual(1, world.Runtime.LandingStock.Count);
            Assert.AreEqual(2, world.Runtime.LandingLogUnits);
            Assert.IsTrue(CountContaining(world.Runtime.Diagnostics, "no custody ledger") >= 1,
                "The missing ledger is diagnosed, never silent.");
        }

        [Test]
        public void T3_RefusedFellingRegistersNothing()
        {
            var world = new World(203, withLedger: true);
            LogLot lot = world.Runtime.RunFelling(world.Stands, "stand-sawmill-district", "poacher", 2,
                world.Worker, 200, world.Diagnostics, FellingEquipmentGate());

            Assert.IsNull(lot, "Felling without rights is refused.");
            Assert.AreEqual(0, world.Runtime.LandingStock.Count);
            Assert.AreEqual(0, world.Ledger.LotCount);
        }

        [Test]
        public void T4_DispatchDrivesCollectAndLoad()
        {
            var world = new World(203, withLedger: true);
            LogLot lot = world.FellViaRuntime(1, 200);
            LogHaul haul = world.Dispatch(lot, 201);

            LogLotCustodyRecord record = world.Ledger.GetLot(world.LotIdOf(lot));
            Assert.IsNotNull(record);
            Assert.AreEqual(LogLotCustodyState.Loaded, record.State);
            Assert.AreEqual("logging-1", record.CustodianId);
            Assert.AreEqual(201, record.CollectedDayIndex);
            Assert.AreEqual(201, record.LoadedDayIndex);
            Assert.AreEqual(0, world.Runtime.LandingLogUnits, "The lot left the landing.");
            Assert.IsNotNull(haul.Lot, "The haul holds the lot.");
            Assert.AreEqual(world.LotIdOf(lot), haul.Lot.LotId.ToString());
        }

        [Test]
        public void T5_SplitCargoReRegisteredUnderFreshId()
        {
            // C-2 (OD-C4): three 600 kg logs (1800 kg) against the 1200 kg
            // logging wagon — 2 fit, 1 stays, so the cargo splits and
            // carries a fresh id. (At the old 450 kg wagon two 300 kg logs
            // split; the split behaviour is what this test pins.)
            var world = new World(203, withLedger: true, kilogramsPerLog: 600f);
            LogLot lot = world.FellViaRuntime(3, 200);
            string originalLotId = world.LotIdOf(lot);
            LogHaul haul = world.Dispatch(lot, 201);
            string cargoLotId = haul.Lot.LotId.ToString();

            Assert.IsFalse(string.Equals(cargoLotId, originalLotId, StringComparison.Ordinal),
                "The split cargo carries a fresh id.");
            LogLotCustodyRecord cargo = world.Ledger.GetLot(cargoLotId);
            Assert.IsNotNull(cargo, "The cargo is registered under its fresh id.");
            Assert.AreEqual(LogLotCustodyState.Loaded, cargo.State);
            Assert.AreEqual(2, cargo.LogUnits);
            Assert.AreEqual(1200f, cargo.MassKg, 0.01f);
            Assert.AreEqual("sawmill-district", cargo.HarvestLocationId);
            Assert.AreEqual("logging-1", cargo.CustodianId);

            LogLotCustodyRecord original = world.Ledger.GetLot(originalLotId);
            Assert.IsNotNull(original, "The original felling record still exists.");
            Assert.AreEqual(LogLotCustodyState.Created, original.State);
            Assert.AreEqual(3, original.LogUnits, "The felling snapshot is unchanged.");

            Assert.AreEqual(1, world.Runtime.LandingStock.Count);
            Assert.AreEqual(1, world.Runtime.LandingLogUnits, "The balance stays at the landing.");
        }

        [Test]
        public void T6_AdvanceDrivesInTransit()
        {
            var world = new World(203, withLedger: true);
            LogLot lot = world.FellViaRuntime(1, 200);
            world.Dispatch(lot, 201);
            LogHaul haul = world.Runtime.Hauls[0];
            world.Advance(1000, 201);

            Assert.IsTrue(haul.Status == LogHaulStatus.InTransit || haul.Status == LogHaulStatus.Arrived,
                "The 1000 minutes cover loading and the transit.");
            LogLotCustodyRecord record = world.Ledger.GetLot(world.LotIdOf(lot));
            Assert.IsNotNull(record);
            Assert.AreEqual(LogLotCustodyState.InTransit, record.State);
            Assert.AreEqual("logging-1", record.CustodianId);
            Assert.AreEqual(201, record.InTransitDayIndex);
        }

        [Test]
        public void T7_DeliveryDrivesDelivered()
        {
            var world = new World(203, withLedger: true);
            LogLot lot = world.FellViaRuntime(1, 200);
            world.Dispatch(lot, 201);
            world.Advance(1000, 201);
            world.Deliver(202);

            LogLotCustodyRecord record = world.Ledger.GetLot(world.LotIdOf(lot));
            Assert.IsNotNull(record);
            Assert.AreEqual(LogLotCustodyState.Delivered, record.State);
            Assert.AreEqual("mill-1", record.CustodianId);
            Assert.AreEqual(202, record.DeliveredDayIndex);
            Assert.AreEqual(1, world.Mill.LogStock.TotalLogUnits, "The lot is in the mill yard.");

            // Second half (note 108 C2): an unregistered lot is still
            // delivered physically, with a loud diagnostic and no invented
            // record. The lot is felled by a ledger-less runtime and moved
            // into a ledger-holding runtime through the save DTO — the
            // established way to move runtime state between instances.
            var second = new World(203, withLedger: true);
            var bare = new LoggingOperationRuntime("logging-1", second.Ids);
            LogLot unregistered = bare.RunFelling(second.Stands, "stand-sawmill-district", "logging-business-1", 1,
                second.Worker, 200, second.Diagnostics, FellingEquipmentGate());
            Assert.IsNotNull(unregistered);
            var carrier = new LoggingOperationRuntime("logging-1", second.Ids, second.Ledger);
            carrier.LoadFromSaveDto(bare.CaptureSaveDto());
            LogHaul haul2 = carrier.DispatchLogHaul(unregistered.LotId, "mill-1", "mill-1-yard",
                LandingToMillJourney(), "landing-1", 201, second.Diagnostics);
            Assert.IsNotNull(haul2);
            carrier.AdvanceHauls(1000, second.Diagnostics, 201);
            carrier.DeliverArrivedHauls(second.Mill.LogStock, second.Diagnostics, 202);

            Assert.AreEqual(1, second.Mill.LogStock.TotalLogUnits, "The unregistered lot is still delivered.");
            Assert.AreEqual(0, second.Ledger.LotCount, "No record is invented for the unregistered lot.");
            Assert.IsTrue(CountContaining(second.Diagnostics, "unknown lot") >= 1,
                "The delivery diagnostic names the unknown lot.");
            Assert.IsTrue(CountContaining(second.Diagnostics, unregistered.LotId.ToString()) >= 1,
                "The diagnostic names the lot id.");
        }

        [Test]
        public void T8_FullChainEndsProcessed()
        {
            // Lighter logs (200f): the 2-log lot (400 kg) fits the wagon
            // intact, so one yard lot holds 2 logs for BeginProduction(2).
            // (At the default 300f/log the lot is 600 kg — it also fits
            // the C-2 1200 kg logging wagon; the lighter logs predate C-2.)
            var world = new World(203, withLedger: true, kilogramsPerLog: 200f);
            LogLot lot = world.FellViaRuntime(2, 200);
            world.Dispatch(lot, 201);
            world.Advance(1000, 201);
            world.Deliver(202);
            SawmillProductionJob job = Begin(world, lot, 2, 203);
            Assert.IsTrue(Run(world, job, 203));

            Assert.AreEqual(SawmillProductionJobState.Complete, job.State);
            LogLotCustodyRecord record = world.Ledger.GetLot(world.LotIdOf(lot));
            Assert.IsNotNull(record);
            Assert.AreEqual(LogLotCustodyState.Processed, record.State);
            Assert.AreEqual(203, record.ProcessedDayIndex);
            Assert.AreEqual("mill-1", record.CustodianId);
            Assert.AreEqual(8, job.LumberUnitsTotal);
            Assert.AreEqual(60, world.Counters.TotalSawMinutes);
            Assert.AreEqual(1, CountContaining(world.Diagnostics, "recorded as Processed"));
        }

        [Test]
        public void T9_UnregisteredDispatchStillDispatches()
        {
            var world = new World(203, withLedger: true);
            LogLot lot = world.FellViaRuntime(1, 200);
            string lotId = world.LotIdOf(lot);
            Assert.AreEqual(1, world.Ledger.LotCount, "The lot is registered at felling.");

            // Note 108 C2: the ledger is private readonly with no setter,
            // so the record is dropped by moving the runtime state into a
            // second runtime holding a fresh empty ledger, through the
            // save DTO.
            var freshLedger = new LogLotCustodyLedger(new List<string>());
            var carrier = new LoggingOperationRuntime("logging-1", world.Ids, freshLedger);
            carrier.LoadFromSaveDto(world.Runtime.CaptureSaveDto());
            LogHaul haul = carrier.DispatchLogHaul(lot.LotId, "mill-1", "mill-1-yard",
                LandingToMillJourney(), "landing-1", 201, world.Diagnostics);

            Assert.IsNotNull(haul, "The haul still dispatches.");
            Assert.AreEqual(0, carrier.LandingLogUnits, "The lot left the landing.");
            Assert.IsNotNull(haul.Lot, "The haul holds the lot.");
            Assert.AreEqual(0, freshLedger.LotCount, "Nothing is invented.");
            Assert.IsTrue(CountContaining(world.Diagnostics, "unknown lot") >= 1,
                "The diagnostic names the unknown lot at Collect.");
            Assert.IsTrue(CountContaining(world.Diagnostics, lotId) >= 1,
                "The diagnostic names the lot id.");
        }

        [Test]
        public void T10_RemainderNotRegistered()
        {
            // Lighter logs (100f): the 4-log lot (400 kg) fits the wagon
            // intact, so the four 1-log runs can draw from one yard lot.
            // (At the default 300f/log the lot is 1200 kg — it also fits
            // the C-2 1200 kg logging wagon; the lighter logs predate C-2.)
            var world = new World(203, withLedger: true, kilogramsPerLog: 100f);
            LogLot lot = world.FellViaRuntime(4, 200);
            string originalLotId = world.LotIdOf(lot);
            world.Dispatch(lot, 201);
            world.Advance(1000, 201);
            world.Deliver(202);
            SawmillProductionJob job = Begin(world, lot, 1, 203);

            for (int run = 1; run <= 3; run++)
            {
                Assert.IsTrue(Run(world, job, 203));
                Assert.AreEqual(SawmillProductionJobState.Sawing, job.State);
                Assert.IsNull(world.Ledger.GetLot(job.LotId),
                    "The remainder's fresh id was never registered.");
                LogLotCustodyRecord record = world.Ledger.GetLot(originalLotId);
                Assert.IsNotNull(record);
                Assert.AreEqual(LogLotCustodyState.Delivered, record.State);
            }

            Assert.IsTrue(Run(world, job, 203));
            Assert.AreEqual(SawmillProductionJobState.Complete, job.State);
            LogLotCustodyRecord processed = world.Ledger.GetLot(originalLotId);
            Assert.IsNotNull(processed);
            Assert.AreEqual(LogLotCustodyState.Processed, processed.State);
            Assert.AreEqual(120, world.Counters.TotalSawMinutes);
            Assert.AreEqual(16, job.LumberUnitsTotal);
            Assert.IsNull(world.Ledger.GetLot(job.LotId));
            Assert.AreEqual(1, CountContaining(world.Diagnostics, "recorded as Processed"));
        }

        [Test]
        public void T11_AbandonedJobRemainderEdge()
        {
            // Lighter logs (100f) so the 4-log lot (400 kg) fits the wagon
            // intact, as in T10.
            var world = new World(203, withLedger: true, kilogramsPerLog: 100f);
            LogLot lot = world.FellViaRuntime(4, 200);
            string originalLotId = world.LotIdOf(lot);
            world.Dispatch(lot, 201);
            world.Advance(1000, 201);
            world.Deliver(202);
            SawmillProductionJob job = Begin(world, lot, 1, 203);
            Assert.IsTrue(Run(world, job, 203));
            Assert.AreEqual(SawmillProductionJobState.Sawing, job.State);

            string remainderId = job.LotId;
            Assert.IsTrue(world.Driver.FailProduction(job.JobId, 203, out string failRefusal));
            Assert.IsNull(failRefusal);

            SawmillProductionJob fresh = world.Driver.BeginProduction(remainderId, Sawyer, 3, 203, out string beginRefusal);
            Assert.IsNotNull(fresh, "BeginProduction refused: " + beginRefusal);
            Assert.IsNull(beginRefusal);
            Assert.IsTrue(world.Driver.RunProduction(fresh.JobId, 203, out string runRefusal),
                "RunProduction refused: " + runRefusal);

            Assert.AreEqual(SawmillProductionJobState.Complete, fresh.State);
            Assert.AreEqual(1, CountContaining(world.Diagnostics, "could not record lot"),
                "The abandoned-job remainder is diagnosed, not fixed.");
            Assert.IsTrue(CountContaining(world.Diagnostics, remainderId) >= 1,
                "The diagnostic names the remainder id.");
            LogLotCustodyRecord original = world.Ledger.GetLot(originalLotId);
            Assert.IsNotNull(original);
            Assert.AreEqual(LogLotCustodyState.Delivered, original.State, "The original record stays Delivered — never Processed.");
        }

        [Test]
        public void T12_LedgerSaveRoundTripPreservesChain()
        {
            var world = new World(203, withLedger: true);
            // Lot A: driven to InTransit (T6 shape) through the world's runtime.
            LogLot lotA = world.FellViaRuntime(1, 200);
            world.Dispatch(lotA, 201);
            world.Advance(1000, 201);
            // Lot B: driven to Delivered (T7 shape) through a second runtime
            // sharing the same ledger — DeliverArrivedHauls delivers every
            // Arrived haul in its runtime, so a second runtime keeps A's
            // record InTransit.
            var runtimeB = new LoggingOperationRuntime("logging-1", world.Ids, world.Ledger);
            LogLot lotB = runtimeB.RunFelling(world.Stands, "stand-sawmill-district", "logging-business-1", 1,
                world.Worker, 200, world.Diagnostics, FellingEquipmentGate());
            Assert.IsNotNull(lotB);
            LogHaul haulB = runtimeB.DispatchLogHaul(lotB.LotId, "mill-1", "mill-1-yard",
                LandingToMillJourney(), "landing-1", 201, world.Diagnostics);
            Assert.IsNotNull(haulB);
            runtimeB.AdvanceHauls(1000, world.Diagnostics, 201);
            runtimeB.DeliverArrivedHauls(world.Mill.LogStock, world.Diagnostics, 202);

            LogLotCustodyLedger.LogLotCustodyLedgerSaveDto dto = world.Ledger.CaptureSaveDto();
            var fresh = new LogLotCustodyLedger(new List<string>());
            fresh.LoadFromSaveDto(dto);

            Assert.AreEqual(2, fresh.LotCount);
            LogLotCustodyRecord recordA = fresh.GetLot(world.LotIdOf(lotA));
            Assert.IsNotNull(recordA);
            Assert.AreEqual(LogLotCustodyState.InTransit, recordA.State);
            Assert.AreEqual("logging-1", recordA.CustodianId);
            Assert.AreEqual(201, recordA.InTransitDayIndex);
            Assert.AreEqual("logging-business-1", recordA.OwnerId);
            Assert.AreEqual(300f, recordA.MassKg, 0.01f);
            LogLotCustodyRecord recordB = fresh.GetLot(world.LotIdOf(lotB));
            Assert.IsNotNull(recordB);
            Assert.AreEqual(LogLotCustodyState.Delivered, recordB.State);
            Assert.AreEqual("mill-1", recordB.CustodianId);
            Assert.AreEqual(202, recordB.DeliveredDayIndex);
            Assert.AreEqual("logging-business-1", recordB.OwnerId);
            Assert.AreEqual(300f, recordB.MassKg, 0.01f);
        }
    }
}
