using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Logging;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Economy.Farming.Timber;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;

// F6 Tier 2 (2026-11-13): the F6 driver records a fully consumed log lot
// as Processed in the ported LogLotCustodyLedger, at the completion step
// only, with the original lot id derived from JobId. Tests are red-first:
// they fail (CS1501, no 7-argument constructor overload) until the
// driver wiring lands.
namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public sealed class SawmillProductionDriverLedgerTests
    {
        private const int RunDay = 205;
        private static readonly EntityId Sawyer = EntityId.For(EntityKind.Person, 5002);

        private sealed class World
        {
            public LoggingStandRegistry Stands = new LoggingStandRegistry();
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public LogLotCustodyLedger Ledger;
            public EntityId Worker = EntityId.For(EntityKind.Person, 5);
            public List<string> Diagnostics = new List<string>();

            public SawmillShopRuntime Mill;
            public TaskAuthority Tasks;
            public WorkTimeBudgetStore Budgets;
            public EquipmentTaskGate Gate;
            public SawmillLabourCounters Counters;
            public SawmillProductionDriver Driver;

            public World(int day, bool withLedger)
            {
                Assert.IsNull(Stands.RegisterStand(new TimberStand
                {
                    StandId = "stand-sawmill-district",
                    DisplayName = "Sawmill District Pines",
                    LocationId = "sawmill-district",
                    Species = "white pine",
                    StandingTimberUnits = 10,
                }));
                Assert.IsNull(Stands.GrantTimberRights("stand-sawmill-district", "logging-business-1", 200, "town-land-office"));
                Ledger = new LogLotCustodyLedger(Diagnostics);
                Mill = new SawmillShopRuntime("mill-1", Ids);
                Tasks = new TaskAuthority();
                Budgets = new WorkTimeBudgetStore();
                Budgets.EnsureDay(day);
                Gate = BuildGate();
                Counters = new SawmillLabourCounters();
                Driver = withLedger
                    ? new SawmillProductionDriver(Mill, Tasks, Budgets, Gate, Counters, Diagnostics, Ledger)
                    : new SawmillProductionDriver(Mill, Tasks, Budgets, Gate, Counters, Diagnostics);
            }

            public LogLot FellLot(int units, int dayIndex)
            {
                LogLot lot = TimberHarvest.FellLogs(Ids, Stands.GetStand("stand-sawmill-district"), "logging-business-1", units, Worker, dayIndex, Diagnostics);
                Assert.IsNotNull(lot, "FellLogs must produce a lot on a granted stand.");
                return lot;
            }

            public string LotIdOf(LogLot lot)
            {
                return lot.LotId.ToString();
            }

            public void DriveToDelivered(LogLot lot)
            {
                string lotId = LotIdOf(lot);
                string refusal;
                Assert.IsTrue(Ledger.CollectLot(lotId, "carrier-1", 201, out refusal),
                    "CollectLot refused: " + refusal);
                Assert.IsNull(Ledger.LoadLot(lotId, "carrier-1", 202));
                Assert.IsNull(Ledger.MarkInTransit(lotId, "carrier-1", 203));
                Assert.IsNull(Ledger.MarkDelivered(lotId, "mill-1", 204));
            }

            public void ReceiveIntoMill(LogLot lot)
            {
                Assert.IsTrue(Driver.ReceiveLogLot(lot, RunDay, out string refusal),
                    "ReceiveLogLot refused: " + refusal);
                Assert.IsNull(refusal);
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

        private static int CountContaining(List<string> diagnostics, string needle)
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
        public void D1_FullRunMarksDeliveredLotProcessedExactlyOnce()
        {
            var world = new World(RunDay, withLedger: true);
            LogLot lot = world.FellLot(2, 200);
            Assert.IsNull(world.Ledger.RegisterLot(lot, "sawmill-district", "logging-business-1", "world-tree-1", 200));
            world.DriveToDelivered(lot);
            world.ReceiveIntoMill(lot);
            // The run day is 205, the day after the day-204 delivery (C1).
            SawmillProductionJob job = Begin(world, lot, 2, RunDay);

            Assert.IsTrue(Run(world, job, RunDay));

            Assert.AreEqual(SawmillProductionJobState.Complete, job.State);
            Assert.AreEqual(8, job.LumberUnitsTotal);
            Assert.AreEqual(60, world.Counters.TotalSawMinutes);
            LogLotCustodyRecord record = world.Ledger.GetLot(world.LotIdOf(lot));
            Assert.IsNotNull(record);
            Assert.AreEqual(LogLotCustodyState.Processed, record.State);
            Assert.AreEqual(205, record.ProcessedDayIndex);
            Assert.AreEqual("mill-1", record.CustodianId);
            Assert.AreEqual(1, CountContaining(world.Diagnostics, "recorded as Processed"));
        }

        [Test]
        public void D2_PartialRunsDoNotMarkProcessedUntilFullConsumption()
        {
            var world = new World(RunDay, withLedger: true);
            LogLot lot = world.FellLot(4, 200);
            string originalLotId = world.LotIdOf(lot);
            Assert.IsNull(world.Ledger.RegisterLot(lot, "sawmill-district", "logging-business-1", "world-tree-1", 200));
            world.DriveToDelivered(lot);
            world.ReceiveIntoMill(lot);
            SawmillProductionJob job = Begin(world, lot, 1, RunDay);

            for (int run = 1; run <= 3; run++)
            {
                Assert.IsTrue(Run(world, job, RunDay));
                Assert.AreEqual(SawmillProductionJobState.Sawing, job.State);
                LogLotCustodyRecord record = world.Ledger.GetLot(originalLotId);
                Assert.IsNotNull(record);
                Assert.AreEqual(LogLotCustodyState.Delivered, record.State);
            }

            Assert.IsTrue(Run(world, job, RunDay));
            Assert.AreEqual(SawmillProductionJobState.Complete, job.State);
            LogLotCustodyRecord processed = world.Ledger.GetLot(originalLotId);
            Assert.IsNotNull(processed);
            Assert.AreEqual(LogLotCustodyState.Processed, processed.State);
            Assert.AreEqual(120, world.Counters.TotalSawMinutes);
            Assert.AreEqual(16, job.LumberUnitsTotal);
            // The remainder's fresh id was never registered (§4.7 gap, pinned).
            Assert.IsNull(world.Ledger.GetLot(job.LotId));
            Assert.AreEqual(1, CountContaining(world.Diagnostics, "recorded as Processed"));
        }

        [Test]
        public void D3_NullLedgerLeavesProductionUnchanged()
        {
            var world = new World(RunDay, withLedger: false);
            LogLot lot = world.FellLot(2, 200);
            world.ReceiveIntoMill(lot);
            SawmillProductionJob job = Begin(world, lot, 2, RunDay);

            Assert.IsTrue(Run(world, job, RunDay));

            Assert.AreEqual(SawmillProductionJobState.Complete, job.State);
            Assert.AreEqual(8, job.LumberUnitsTotal);
            Assert.AreEqual(60, world.Counters.TotalSawMinutes);
            Assert.IsTrue(CountContaining(world.Diagnostics, "no custody ledger") >= 1);
        }

        [Test]
        public void D4_UnregisteredLotStillCompletesWithALoudDiagnostic()
        {
            var world = new World(RunDay, withLedger: true);
            LogLot lot = world.FellLot(2, 200);
            world.ReceiveIntoMill(lot);
            SawmillProductionJob job = Begin(world, lot, 2, RunDay);

            Assert.IsTrue(Run(world, job, RunDay));

            Assert.AreEqual(SawmillProductionJobState.Complete, job.State);
            Assert.AreEqual(60, world.Counters.TotalSawMinutes);
            Assert.IsTrue(CountContaining(world.Diagnostics, "could not record lot") >= 1);
            Assert.IsTrue(CountContaining(world.Diagnostics, world.LotIdOf(lot)) >= 1);
            Assert.AreEqual(0, world.Ledger.LotCount);
        }

        [Test]
        public void D5_LotRegisteredButNotDeliveredStillCompletesWithALoudDiagnostic()
        {
            var world = new World(RunDay, withLedger: true);
            LogLot lot = world.FellLot(2, 200);
            Assert.IsNull(world.Ledger.RegisterLot(lot, "sawmill-district", "logging-business-1", "world-tree-1", 200));
            world.ReceiveIntoMill(lot);
            SawmillProductionJob job = Begin(world, lot, 2, RunDay);

            Assert.IsTrue(Run(world, job, RunDay));

            Assert.AreEqual(SawmillProductionJobState.Complete, job.State);
            Assert.AreEqual(60, world.Counters.TotalSawMinutes);
            LogLotCustodyRecord record = world.Ledger.GetLot(world.LotIdOf(lot));
            Assert.IsNotNull(record);
            Assert.AreEqual(LogLotCustodyState.Created, record.State);
            Assert.IsTrue(CountContaining(world.Diagnostics, "only a Delivered lot can be processed") >= 1);
        }

        [Test]
        public void D6_FailProductionNeverMarksProcessed()
        {
            var world = new World(RunDay, withLedger: true);
            LogLot lot = world.FellLot(2, 200);
            Assert.IsNull(world.Ledger.RegisterLot(lot, "sawmill-district", "logging-business-1", "world-tree-1", 200));
            world.DriveToDelivered(lot);
            world.ReceiveIntoMill(lot);
            SawmillProductionJob job = Begin(world, lot, 2, RunDay);

            Assert.IsTrue(world.Driver.FailProduction(job.JobId, RunDay, out string refusal));
            Assert.IsNull(refusal);

            Assert.AreEqual(SawmillProductionJobState.Failed, job.State);
            LogLotCustodyRecord record = world.Ledger.GetLot(world.LotIdOf(lot));
            Assert.IsNotNull(record);
            Assert.AreEqual(LogLotCustodyState.Delivered, record.State);
            Assert.AreEqual(2, world.Mill.LogStock.TotalLogUnits);
            Assert.AreEqual(0, world.Counters.TotalSawMinutes);
        }

        [Test]
        public void D7_SaveLoadRoundTripThenCompletionMarksProcessed()
        {
            var world = new World(RunDay, withLedger: true);
            LogLot lot = world.FellLot(4, 200);
            string originalLotId = world.LotIdOf(lot);
            Assert.IsNull(world.Ledger.RegisterLot(lot, "sawmill-district", "logging-business-1", "world-tree-1", 200));
            world.DriveToDelivered(lot);
            world.ReceiveIntoMill(lot);
            SawmillProductionJob job = Begin(world, lot, 1, RunDay);
            Assert.IsTrue(Run(world, job, RunDay));
            Assert.AreEqual(SawmillProductionJobState.Sawing, job.State);
            Assert.AreEqual(30, world.Counters.TotalSawMinutes);

            SawmillProductionDriver.SawmillProductionDriverSaveDto driverDto = world.Driver.ToSaveDto();
            LogLotCustodyLedger.LogLotCustodyLedgerSaveDto ledgerDto = world.Ledger.CaptureSaveDto();
            var freshDiagnostics = new List<string>();
            var freshLedger = new LogLotCustodyLedger(freshDiagnostics);
            freshLedger.LoadFromSaveDto(ledgerDto);
            var fresh = new SawmillProductionDriver(world.Mill, world.Tasks, world.Budgets, world.Gate, world.Counters, freshDiagnostics, freshLedger);
            fresh.LoadFromSaveDto(driverDto);

            Assert.AreEqual(1, fresh.ProductionJobCount);
            SawmillProductionJob restored = fresh.GetJob(job.JobId);
            Assert.IsNotNull(restored);
            Assert.AreEqual(SawmillProductionJobState.Sawing, restored.State);
            Assert.AreEqual(30, world.Counters.TotalSawMinutes);
            LogLotCustodyRecord restoredRecord = freshLedger.GetLot(originalLotId);
            Assert.IsNotNull(restoredRecord);
            Assert.AreEqual(LogLotCustodyState.Delivered, restoredRecord.State);

            Assert.IsTrue(fresh.RunProduction(restored.JobId, RunDay, out string r2));
            Assert.IsTrue(fresh.RunProduction(restored.JobId, RunDay, out string r3));
            Assert.IsTrue(fresh.RunProduction(restored.JobId, RunDay, out string r4));
            Assert.AreEqual(SawmillProductionJobState.Complete, restored.State);
            Assert.AreEqual(0, world.Mill.LogStock.TotalLogUnits);
            Assert.AreEqual(120, world.Counters.TotalSawMinutes);
            LogLotCustodyRecord processed = freshLedger.GetLot(originalLotId);
            Assert.IsNotNull(processed);
            Assert.AreEqual(LogLotCustodyState.Processed, processed.State);
        }
    }
}
