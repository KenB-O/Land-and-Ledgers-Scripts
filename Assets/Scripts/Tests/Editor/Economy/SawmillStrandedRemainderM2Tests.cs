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

// M2 (2026-11-17): the sawmill drive re-attempts only open jobs and
// begins only lots delivered that day, so a lot that missed its
// delivery-day begin — delivered on a no-sawyer day, refused at begin
// while an older job blocked FIFO, or left behind by a failed or
// abandoned job — sat in the mill yard unsawn forever, and the drive
// logged "nothing-to-saw" in exactly that state. FindStrandedLotIds
// plus BeginStrandedRemainders is the recovery path: the sweep begins
// and runs each stranded lot once, oldest first. These tests pin the
// detection rules and the recovery, including the named recovery path
// for a remainder left by an abandoned partial job.
namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public sealed class SawmillStrandedRemainderM2Tests
    {
        private const int RunDay = 205;
        private static readonly EntityId Sawyer = EntityId.For(EntityKind.Person, 5002);

        private sealed class World
        {
            public LoggingStandRegistry Stands = new LoggingStandRegistry();
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public EntityId Worker = EntityId.For(EntityKind.Person, 5);
            public List<string> Diagnostics = new List<string>();

            public SawmillShopRuntime Mill;
            public TaskAuthority Tasks;
            public WorkTimeBudgetStore Budgets;
            public EquipmentTaskGate Gate;
            public SawmillLabourCounters Counters;
            public SawmillProductionDriver Driver;

            public World(int day)
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
                Mill = new SawmillShopRuntime("mill-1", Ids);
                Tasks = new TaskAuthority();
                Budgets = new WorkTimeBudgetStore();
                Budgets.EnsureDay(day);
                Gate = BuildGate();
                Counters = new SawmillLabourCounters();
                Driver = new SawmillProductionDriver(Mill, Tasks, Budgets, Gate, Counters, Diagnostics);
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

        private static int IndexContaining(List<string> diagnostics, string needle)
        {
            for (int i = 0; i < diagnostics.Count; i++)
            {
                if (diagnostics[i] != null && diagnostics[i].Contains(needle)) return i;
            }
            return -1;
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
        public void M2_StrandedLotWithNoJobIsFound()
        {
            var world = new World(RunDay);
            LogLot lot = world.FellLot(2, 200);
            world.ReceiveIntoMill(lot);

            List<string> stranded = world.Driver.FindStrandedLotIds(new List<string>());

            Assert.AreEqual(1, stranded.Count);
            Assert.AreEqual(world.LotIdOf(lot), stranded[0]);
        }

        [Test]
        public void M2_LotWithOpenJobIsNotStranded()
        {
            var world = new World(RunDay);
            LogLot lot = world.FellLot(2, 200);
            world.ReceiveIntoMill(lot);
            Begin(world, lot, 2, RunDay);

            List<string> stranded = world.Driver.FindStrandedLotIds(new List<string>());

            Assert.AreEqual(0, stranded.Count);
        }

        [Test]
        public void M2_ExcludedDeliveredLotIsNotStranded()
        {
            var world = new World(RunDay);
            LogLot lot = world.FellLot(2, 200);
            world.ReceiveIntoMill(lot);

            List<string> stranded = world.Driver.FindStrandedLotIds(new List<string> { world.LotIdOf(lot) });

            Assert.AreEqual(0, stranded.Count);
        }

        [Test]
        public void M2_LotNamedOnlyByFailedJobIsStranded()
        {
            var world = new World(RunDay);
            LogLot lot = world.FellLot(4, 200);
            world.ReceiveIntoMill(lot);
            SawmillProductionJob job = Begin(world, lot, 1, RunDay);
            Assert.IsTrue(world.Driver.FailProduction(job.JobId, RunDay, out string failRefusal), "FailProduction refused: " + failRefusal);
            Assert.IsNull(failRefusal);
            Assert.AreEqual(SawmillProductionJobState.Failed, job.State);

            List<string> stranded = world.Driver.FindStrandedLotIds(new List<string>());

            Assert.AreEqual(1, stranded.Count);
            Assert.AreEqual(world.LotIdOf(lot), stranded[0]);
        }

        [Test]
        public void M2_SweepBeginsAndCompletesStrandedLot()
        {
            var world = new World(RunDay);
            LogLot lot = world.FellLot(2, 200);
            world.ReceiveIntoMill(lot);

            int begun = world.Driver.BeginStrandedRemainders(Sawyer, RunDay, new List<string>(), out int completed);

            Assert.AreEqual(1, begun);
            Assert.AreEqual(1, completed);
            SawmillProductionJob job = world.Driver.GetJob("sawmill:" + world.LotIdOf(lot));
            Assert.IsNotNull(job);
            Assert.AreEqual(SawmillProductionJobState.Complete, job.State);
            Assert.AreEqual(8, job.LumberUnitsTotal);
            Assert.AreEqual(0, world.Mill.LogStock.TotalLogUnits);
        }

        [Test]
        public void M2_SweepRecoversRemainderAfterAbandonedPartialJob()
        {
            var world = new World(RunDay);
            LogLot lot = world.FellLot(4, 200);
            world.ReceiveIntoMill(lot);
            // The lot object is mutated in place by a partial run (the
            // remainder keeps the object, only its id changes), so the
            // original id must be captured before the run.
            string originalLotId = world.LotIdOf(lot);
            SawmillProductionJob firstJob = Begin(world, lot, 1, RunDay);
            Assert.IsTrue(Run(world, firstJob, RunDay));
            Assert.AreEqual(SawmillProductionJobState.Sawing, firstJob.State);
            Assert.AreEqual(4, firstJob.LumberUnitsTotal);
            string remainderId = firstJob.LotId;
            Assert.IsTrue(remainderId != originalLotId, "A partial run must re-identify the remainder under a fresh lot id.");
            Assert.IsTrue(world.Driver.FailProduction(firstJob.JobId, RunDay, out string failRefusal), "FailProduction refused: " + failRefusal);
            Assert.IsNull(failRefusal);

            List<string> stranded = world.Driver.FindStrandedLotIds(new List<string>());
            Assert.AreEqual(1, stranded.Count);
            Assert.AreEqual(remainderId, stranded[0]);

            int begun = world.Driver.BeginStrandedRemainders(Sawyer, RunDay, new List<string>(), out int completed);

            Assert.AreEqual(1, begun);
            Assert.AreEqual(1, completed);
            SawmillProductionJob recoveryJob = world.Driver.GetJob("sawmill:" + remainderId);
            Assert.IsNotNull(recoveryJob);
            Assert.AreEqual(SawmillProductionJobState.Complete, recoveryJob.State);
            Assert.AreEqual(12, recoveryJob.LumberUnitsTotal);
            Assert.AreEqual(0, world.Mill.LogStock.TotalLogUnits);
            Assert.AreEqual(120, world.Counters.TotalSawMinutes);
        }

        [Test]
        public void M2_SweepSkipsExcludedDeliveredLot()
        {
            var world = new World(RunDay);
            LogLot lot = world.FellLot(2, 200);
            world.ReceiveIntoMill(lot);

            int begun = world.Driver.BeginStrandedRemainders(Sawyer, RunDay, new List<string> { world.LotIdOf(lot) }, out int completed);

            Assert.AreEqual(0, begun);
            Assert.AreEqual(0, completed);
            Assert.AreEqual(2, world.Mill.LogStock.TotalLogUnits);
        }

        [Test]
        public void M2_SweepRefusesInvalidSawyer()
        {
            var world = new World(RunDay);
            LogLot lot = world.FellLot(2, 200);
            world.ReceiveIntoMill(lot);

            int begun = world.Driver.BeginStrandedRemainders(EntityId.Invalid, RunDay, new List<string>(), out int completed);

            Assert.AreEqual(0, begun);
            Assert.AreEqual(0, completed);
            Assert.AreEqual(2, world.Mill.LogStock.TotalLogUnits);
            Assert.IsTrue(CountContaining(world.Diagnostics, "no valid Person sawyer") >= 1);
        }

        [Test]
        public void M2_SweepToleratesFifoRefusal()
        {
            var world = new World(RunDay);
            LogLot older = world.FellLot(4, 200);
            LogLot newer = world.FellLot(2, 201);
            world.ReceiveIntoMill(older);
            world.ReceiveIntoMill(newer);
            SawmillProductionJob olderJob = Begin(world, older, 1, RunDay);
            Assert.IsTrue(Run(world, olderJob, RunDay));
            Assert.AreEqual(SawmillProductionJobState.Sawing, olderJob.State);

            int begun = world.Driver.BeginStrandedRemainders(Sawyer, RunDay, new List<string>(), out int completed);

            Assert.AreEqual(0, begun);
            Assert.AreEqual(0, completed);
            Assert.AreEqual(5, world.Mill.LogStock.TotalLogUnits);
            Assert.IsTrue(CountContaining(world.Diagnostics, "is not the oldest lot") >= 1);
            Assert.AreEqual(SawmillProductionJobState.Sawing, olderJob.State);
            List<string> stillStranded = world.Driver.FindStrandedLotIds(new List<string>());
            Assert.AreEqual(1, stillStranded.Count);
            Assert.AreEqual(world.LotIdOf(newer), stillStranded[0]);
        }

        [Test]
        public void M2_SweepBeginsOldestStrandedLotFirst()
        {
            var world = new World(RunDay);
            LogLot older = world.FellLot(2, 200);
            LogLot newer = world.FellLot(2, 201);
            world.ReceiveIntoMill(older);
            world.ReceiveIntoMill(newer);

            int begun = world.Driver.BeginStrandedRemainders(Sawyer, RunDay, new List<string>(), out int completed);

            Assert.AreEqual(2, begun);
            Assert.AreEqual(2, completed);
            int olderBegin = IndexContaining(world.Diagnostics, "job 'sawmill:" + world.LotIdOf(older) + "' began");
            int newerBegin = IndexContaining(world.Diagnostics, "job 'sawmill:" + world.LotIdOf(newer) + "' began");
            Assert.IsTrue(olderBegin >= 0, "No begin diagnostic for the older lot.");
            Assert.IsTrue(newerBegin >= 0, "No begin diagnostic for the newer lot.");
            Assert.IsTrue(olderBegin < newerBegin, "The older stranded lot must begin before the newer one.");
            Assert.AreEqual(0, world.Mill.LogStock.TotalLogUnits);
        }
    }
}
