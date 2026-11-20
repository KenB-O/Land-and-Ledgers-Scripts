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

// F6 Tier 1 (2026-11-13): SawmillProductionDriver drives RunSawing from
// lots already delivered into the shared mill LogStock, with the
// saw-logs task minted and worked per run by the driver. Tests are
// red-first: they fail until SawmillProductionDriver.cs lands.
namespace LandLedgers.Editor.Economy
{
    [TestFixture]
    public sealed class SawmillProductionDriverTests
    {
        private const int Day = 210;
        private static readonly EntityId Sawyer = EntityId.For(EntityKind.Person, 5002);

        private sealed class World
        {
            public SawmillShopRuntime Mill;
            public TaskAuthority Tasks;
            public WorkTimeBudgetStore Budgets;
            public EquipmentTaskGate Gate;
            public SawmillLabourCounters Counters;
            public List<string> Diagnostics = new List<string>();
            public SawmillProductionDriver Driver;
        }

        private sealed class ScaledTaskDurationEstimator : ITaskDurationEstimator
        {
            private readonly float multiplier;

            public ScaledTaskDurationEstimator(float multiplier)
            {
                this.multiplier = multiplier;
            }

            public int EstimateMinutes(TaskDefinition definition, EntityId workerId)
            {
                return EstimateMinutesForBase(definition != null ? definition.BaseMinutes : 1,
                    definition != null ? definition.RequiredSkillId : null, workerId);
            }

            public int EstimateMinutesForBase(int baseMinutes, string skillId, EntityId workerId)
            {
                return Math.Max(1, (int)Math.Ceiling(baseMinutes * multiplier));
            }
        }

        private static LogLot DocumentedLogLot(int lotNumber, int logs, string standId,
            string species = "white pine", int felledDayIndex = 200)
        {
            return new LogLot
            {
                LotId = EntityId.For(EntityKind.Lot, lotNumber),
                LogUnits = logs,
                MassKg = logs * 300f,
                OwnerId = "mill-1",
                StandId = standId,
                Species = species,
                FelledBy = EntityId.For(EntityKind.Person, 5001),
                FelledDayIndex = felledDayIndex,
            };
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

        private static World NewWorld(SawmillShopRuntime mill, int day = Day)
        {
            var tasks = new TaskAuthority();
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(day);
            var counters = new SawmillLabourCounters();
            var diagnostics = new List<string>();
            var gate = BuildGate();
            var driver = new SawmillProductionDriver(mill, tasks, budgets, gate, counters, diagnostics);
            return new World
            {
                Mill = mill,
                Tasks = tasks,
                Budgets = budgets,
                Gate = gate,
                Counters = counters,
                Diagnostics = diagnostics,
                Driver = driver,
            };
        }

        private static SawmillShopRuntime MillWithLogs(int logs, string species = "white pine")
        {
            var ids = new EntityIdRegistry();
            var mill = new SawmillShopRuntime("mill-1", ids);
            var diag = new List<string>();
            Assert.IsNull(mill.ReceiveLogLot(DocumentedLogLot(9101, logs, "stand-1", species), diag));
            return mill;
        }

        private static string LotId(int lotNumber)
        {
            return EntityId.For(EntityKind.Lot, lotNumber).ToString();
        }

        private static SawmillProductionDriver.SawmillProductionDriverSaveDto LinkDto(
            string jobId, string lotId, int sawyerNumber, int logsToSaw, int state)
        {
            var dto = new SawmillProductionDriver.SawmillProductionDriverSaveDto();
            dto.ProductionJobLinks.Add(new SawmillProductionDriver.SawmillProductionDriverSaveDto.SawmillProductionJobLink
            {
                JobId = jobId,
                LotId = lotId,
                SawyerKind = (int)EntityKind.Person,
                SawyerNumber = sawyerNumber,
                MillBusinessId = "mill-1",
                LogsToSaw = logsToSaw,
                State = state,
                CreatedDayIndex = Day,
            });
            return dto;
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

        // ---- BeginProduction (7) ----

        [Test]
        public void B1_BeginProduction_Refuses_LogsToSawZero()
        {
            World world = NewWorld(MillWithLogs(2));

            SawmillProductionJob job = world.Driver.BeginProduction(LotId(9101), Sawyer, 0, Day, out string refusal);

            Assert.IsNull(job);
            Assert.IsNotNull(refusal);
            Assert.AreEqual(0, world.Driver.ProductionJobCount);
        }

        [Test]
        public void B2_BeginProduction_Refuses_InvalidSawyer_And_NonPersonSawyer()
        {
            World world = NewWorld(MillWithLogs(2));

            SawmillProductionJob invalid = world.Driver.BeginProduction(LotId(9101), EntityId.Invalid, 2, Day, out string refusal);
            Assert.IsNull(invalid);
            Assert.IsNotNull(refusal);
            Assert.AreEqual(0, world.Driver.ProductionJobCount);

            SawmillProductionJob business = world.Driver.BeginProduction(LotId(9101), EntityId.For(EntityKind.Business, 9), 2, Day, out string refusal2);
            Assert.IsNull(business);
            Assert.IsNotNull(refusal2);
            Assert.AreEqual(0, world.Driver.ProductionJobCount);
        }

        [Test]
        public void B3_BeginProduction_Refuses_LotAbsentFromYard()
        {
            World world = NewWorld(new SawmillShopRuntime("mill-1", new EntityIdRegistry()));

            SawmillProductionJob job = world.Driver.BeginProduction(LotId(9101), Sawyer, 2, Day, out string refusal);

            Assert.IsNull(job);
            Assert.IsNotNull(refusal);
            Assert.AreEqual(0, world.Driver.ProductionJobCount);
            Assert.AreEqual(0, world.Mill.LogStock.TotalLogUnits);
        }

        [Test]
        public void B4_BeginProduction_Refuses_WhenLotIsNotOldest()
        {
            var ids = new EntityIdRegistry();
            var mill = new SawmillShopRuntime("mill-1", ids);
            var diag = new List<string>();
            Assert.IsNull(mill.ReceiveLogLot(DocumentedLogLot(9100, 2, "stand-1", "white pine", 100), diag));
            Assert.IsNull(mill.ReceiveLogLot(DocumentedLogLot(9101, 2, "stand-1", "white pine", 200), diag));
            World world = NewWorld(mill);

            SawmillProductionJob job = world.Driver.BeginProduction(LotId(9101), Sawyer, 2, Day, out string refusal);

            Assert.IsNull(job);
            Assert.IsNotNull(refusal);
            Assert.AreEqual(0, world.Driver.ProductionJobCount);
        }

        [Test]
        public void B5_BeginProduction_Refuses_RunLargerThanOldestLot()
        {
            World world = NewWorld(MillWithLogs(2));

            SawmillProductionJob job = world.Driver.BeginProduction(LotId(9101), Sawyer, 3, Day, out string refusal);

            Assert.IsNull(job);
            Assert.IsNotNull(refusal);
            Assert.AreEqual(2, world.Mill.LogStock.TotalLogUnits);
            Assert.AreEqual(0, world.Driver.ProductionJobCount);
        }

        [Test]
        public void B6_BeginProduction_TwiceForSameLot_Refused()
        {
            World world = NewWorld(MillWithLogs(2));

            SawmillProductionJob first = world.Driver.BeginProduction(LotId(9101), Sawyer, 2, Day, out string refusal);
            Assert.IsNotNull(first);
            Assert.IsNull(refusal);

            SawmillProductionJob second = world.Driver.BeginProduction(LotId(9101), Sawyer, 2, Day, out string refusal2);
            Assert.IsNull(second);
            Assert.IsNotNull(refusal2);
            Assert.AreEqual(1, world.Driver.ProductionJobCount);
        }

        [Test]
        public void B7_BeginProduction_Success_RecordsJob()
        {
            World world = NewWorld(MillWithLogs(2));

            SawmillProductionJob job = world.Driver.BeginProduction(LotId(9101), Sawyer, 2, Day, out string refusal);

            Assert.IsNotNull(job);
            Assert.IsNull(refusal);
            Assert.AreEqual(SawmillProductionJobState.Planned, job.State);
            Assert.AreEqual("mill-1", job.MillBusinessId);
            Assert.AreEqual(2, job.LogsToSaw);
            Assert.AreEqual(5002, job.SawyerNumber);
            Assert.IsTrue(job.TaskId == EntityId.Invalid);
        }

        // ---- ReceiveLogLot intake (3) ----

        [Test]
        public void R1_ReceiveLogLot_Refuses_DuplicateIntake_Idempotently()
        {
            World world = NewWorld(MillWithLogs(2));

            bool again = world.Driver.ReceiveLogLot(DocumentedLogLot(9101, 2, "stand-1"), Day, out string refusal);

            Assert.IsTrue(again);
            Assert.IsNull(refusal);
            Assert.AreEqual(1, world.Mill.LogStock.Lots.Count);
            Assert.AreEqual(2, world.Mill.LogStock.TotalLogUnits);
        }

        [Test]
        public void R2_ReceiveLogLot_Success_IntakesLotThatNeverWentThroughHaul()
        {
            World world = NewWorld(new SawmillShopRuntime("mill-1", new EntityIdRegistry()));

            bool ok = world.Driver.ReceiveLogLot(DocumentedLogLot(9101, 2, "stand-1"), Day, out string refusal);

            Assert.IsTrue(ok);
            Assert.IsNull(refusal);
            Assert.AreEqual(1, world.Mill.LogStock.Lots.Count);
        }

        [Test]
        public void R3_ReceiveLogLot_Refuses_NullLot_And_AnonymousLot()
        {
            World world = NewWorld(new SawmillShopRuntime("mill-1", new EntityIdRegistry()));

            bool nullOk = world.Driver.ReceiveLogLot(null, Day, out string nullRefusal);
            Assert.IsFalse(nullOk);
            Assert.IsNotNull(nullRefusal);

            LogLot anonymous = DocumentedLogLot(9101, 2, "stand-1");
            anonymous.LotId = EntityId.Invalid;
            bool anonOk = world.Driver.ReceiveLogLot(anonymous, Day, out string anonRefusal);
            Assert.IsFalse(anonOk);
            Assert.IsNotNull(anonRefusal);

            Assert.AreEqual(0, world.Mill.LogStock.TotalLogUnits);
        }

        // ---- Native haul delivery (1) ----

        [Test]
        public void D1_NativeHaulDelivery_FeedsDriver_EndToEnd()
        {
            // One log per wagon-load: a 2-log lot (600 kg) exceeds the 450 kg
            // wagon limit and splits, so D1 delivers a single 1-log lot.
            var diagnostics = new List<string>();
            var ids = new EntityIdRegistry();
            var registry = new LoggingStandRegistry();
            Assert.IsNull(registry.RegisterStand(new TimberStand
            {
                StandId = "stand-1",
                DisplayName = "Test Pines",
                LocationId = "landing-1",
                TimberRightsHolderId = "",
                Species = "white pine",
                StandingTimberUnits = 10,
            }));
            Assert.IsNull(registry.GrantTimberRights("stand-1", "logging-1", 200, "town-land-office"));
            var operations = new LoggingOperationRuntime("logging-1", ids);
            LogLot lot = operations.RunFelling(registry, "stand-1", "logging-1", 1,
                EntityId.For(EntityKind.Person, 3), 220, diagnostics, FellingEquipmentGate());
            Assert.IsNotNull(lot);

            var mill = new SawmillShopRuntime("mill-1", ids);
            LogHaul haul = operations.DispatchLogHaul(lot.LotId, "mill-1", "mill-1-yard",
                LandingToMillJourney(), "landing-1", 221, diagnostics);
            Assert.IsNotNull(haul);
            operations.AdvanceHauls(1000, diagnostics);
            Assert.AreEqual(LogHaulStatus.Arrived, haul.Status);

            operations.DeliverArrivedHauls(mill.LogStock, diagnostics);
            Assert.AreEqual(LogHaulStatus.Delivered, haul.Status);
            Assert.AreEqual(1, mill.LogStock.TotalLogUnits);

            World world = NewWorld(mill, 221);
            SawmillProductionJob job = world.Driver.BeginProduction(lot.LotId.ToString(), Sawyer, 1, 221, out string beginRefusal);
            Assert.IsNotNull(job);
            Assert.IsNull(beginRefusal);
            bool ran = world.Driver.RunProduction(job.JobId, 221, out string runRefusal);
            Assert.IsTrue(ran);
            Assert.IsNull(runRefusal);
            Assert.AreEqual(SawmillProductionJobState.Complete, job.State);
            Assert.AreEqual(4, job.LumberUnitsTotal);
            Assert.AreEqual(2, job.FuelWoodUnitsTotal);
            Assert.AreEqual(30, job.SawMinutesTotal);
        }

        // ---- RunProduction (11) ----

        [Test]
        public void P1_RunProduction_SawsWholeSoftwoodLot_ExactlyOnce()
        {
            World world = NewWorld(MillWithLogs(2));
            SawmillProductionJob job = world.Driver.BeginProduction(LotId(9101), Sawyer, 2, Day, out string beginRefusal);
            Assert.IsNotNull(job);
            Assert.IsNull(beginRefusal);

            bool ran = world.Driver.RunProduction(job.JobId, Day, out string refusal);

            Assert.IsTrue(ran);
            Assert.IsNull(refusal);
            Assert.AreEqual(SawmillProductionJobState.Complete, job.State);
            Assert.AreEqual(8, job.LumberUnitsTotal);
            Assert.AreEqual(4, job.FuelWoodUnitsTotal);
            Assert.AreEqual(60, job.SawMinutesTotal);
            Assert.AreEqual(2, job.LogsSawnTotal);
            Assert.AreEqual(1, job.SourceLumberLotIds.Count);
            Assert.AreEqual(1, job.SourceByproductLotIds.Count);
            Assert.AreEqual(0, world.Mill.LogStock.TotalLogUnits);
            Assert.AreEqual(60, world.Counters.TotalSawMinutes);
            Assert.AreEqual(8, world.Counters.TotalLumberUnits);
            Assert.AreEqual(60, world.Counters.SawMinutesForBusiness("mill-1"));
            Assert.AreEqual(8, world.Counters.LumberUnitsForBusiness("mill-1"));
            WorkTask task = world.Tasks.FindTask(job.TaskId);
            Assert.IsNotNull(task);
            Assert.AreEqual(TaskStatus.Complete, task.Status);
            Assert.AreEqual(60, task.MinutesWorked);
            Assert.AreEqual(60, task.PlannedMinutes);
        }

        [Test]
        public void P2_RunProduction_SawsWholeHardwoodLot_ExactlyOnce()
        {
            World world = NewWorld(MillWithLogs(2, "oak"));
            SawmillProductionJob job = world.Driver.BeginProduction(LotId(9101), Sawyer, 2, Day, out string beginRefusal);
            Assert.IsNotNull(job);
            Assert.IsNull(beginRefusal);

            bool ran = world.Driver.RunProduction(job.JobId, Day, out string refusal);

            Assert.IsTrue(ran);
            Assert.IsNull(refusal);
            Assert.AreEqual(SawmillProductionJobState.Complete, job.State);
            Assert.AreEqual(10, job.LumberUnitsTotal);
            Assert.AreEqual(4, job.FuelWoodUnitsTotal);
            Assert.AreEqual(90, job.SawMinutesTotal);
            Assert.AreEqual(90, world.Counters.TotalSawMinutes);
            Assert.AreEqual(10, world.Counters.TotalLumberUnits);
        }

        [Test]
        public void P3_RunProduction_OutputBelongsToCorrectMill()
        {
            World world = NewWorld(MillWithLogs(2));
            SawmillProductionJob job = world.Driver.BeginProduction(LotId(9101), Sawyer, 2, Day, out string beginRefusal);
            Assert.IsNotNull(job);
            bool ran = world.Driver.RunProduction(job.JobId, Day, out string refusal);
            Assert.IsTrue(ran);

            Assert.AreEqual(1, world.Mill.LumberStock.Lots.Count);
            SawmillLumberLot lumber = world.Mill.LumberStock.Lots[0];
            Assert.AreEqual("mill-1", lumber.MillBusinessId);
            Assert.AreEqual(LotId(9101), lumber.SourceLogLotId);
            Assert.AreEqual("stand-1", lumber.StandId);
            Assert.AreEqual("softwood-standard", lumber.ConversionProfileId);
            Assert.AreEqual(1, world.Mill.ByproductStock.Count);
            SlabOffcutFuelLot byproduct = world.Mill.ByproductStock[0];
            Assert.AreEqual("mill-1", byproduct.SourceMillBusinessId);
            Assert.AreEqual(LotId(9101), byproduct.SourceLogLotId);
            Assert.AreEqual("stand-1", byproduct.StandId);
        }

        [Test]
        public void P4_RunProduction_PartialRuns_LeaveJobSawing_UntilFullyConsumed()
        {
            World world = NewWorld(MillWithLogs(4));
            SawmillProductionJob job = world.Driver.BeginProduction(LotId(9101), Sawyer, 1, Day, out string beginRefusal);
            Assert.IsNotNull(job);

            bool first = world.Driver.RunProduction(job.JobId, Day, out string refusal);
            Assert.IsTrue(first);
            Assert.IsNull(refusal);
            EntityId firstTask = job.TaskId;
            Assert.AreEqual(SawmillProductionJobState.Sawing, job.State);
            Assert.AreEqual(3, world.Mill.LogStock.TotalLogUnits);
            Assert.AreEqual(4, job.LumberUnitsTotal);
            Assert.AreEqual(30, job.SawMinutesTotal);

            bool second = world.Driver.RunProduction(job.JobId, Day, out string refusal2);
            Assert.IsTrue(second);
            Assert.AreEqual(SawmillProductionJobState.Sawing, job.State);
            Assert.AreEqual(2, world.Mill.LogStock.TotalLogUnits);

            bool third = world.Driver.RunProduction(job.JobId, Day, out string refusal3);
            Assert.IsTrue(third);
            Assert.AreEqual(SawmillProductionJobState.Sawing, job.State);
            Assert.AreEqual(1, world.Mill.LogStock.TotalLogUnits);

            bool fourth = world.Driver.RunProduction(job.JobId, Day, out string refusal4);
            Assert.IsTrue(fourth);
            EntityId fourthTask = job.TaskId;
            Assert.AreEqual(SawmillProductionJobState.Complete, job.State);
            Assert.AreEqual(0, world.Mill.LogStock.TotalLogUnits);
            Assert.AreEqual(16, job.LumberUnitsTotal);
            Assert.AreEqual(8, job.FuelWoodUnitsTotal);
            Assert.AreEqual(120, job.SawMinutesTotal);
            Assert.AreEqual(120, world.Counters.TotalSawMinutes);
            Assert.IsTrue(firstTask != fourthTask);
            Assert.AreEqual(fourthTask, job.TaskId);
        }

        [Test]
        public void P5_RunProduction_TwiceAfterSuccess_Refused_CountersUnchanged()
        {
            World world = NewWorld(MillWithLogs(2));
            SawmillProductionJob job = world.Driver.BeginProduction(LotId(9101), Sawyer, 2, Day, out string beginRefusal);
            Assert.IsNotNull(job);
            Assert.IsTrue(world.Driver.RunProduction(job.JobId, Day, out string firstRefusal));
            int minutesBefore = world.Counters.TotalSawMinutes;
            int lumberBefore = world.Counters.TotalLumberUnits;
            int stockBefore = world.Mill.LumberStock.TotalLumberUnits;

            bool again = world.Driver.RunProduction(job.JobId, Day, out string refusal);

            Assert.IsFalse(again);
            Assert.IsNotNull(refusal);
            Assert.AreEqual(minutesBefore, world.Counters.TotalSawMinutes);
            Assert.AreEqual(lumberBefore, world.Counters.TotalLumberUnits);
            Assert.AreEqual(stockBefore, world.Mill.LumberStock.TotalLumberUnits);
        }

        [Test]
        public void P6_RunProduction_WithDullBlade_Refuses_ConsumesNothing_ThenFilesAndRuns()
        {
            World world = NewWorld(MillWithLogs(2));
            SawmillProductionJob job = world.Driver.BeginProduction(LotId(9101), Sawyer, 2, Day, out string beginRefusal);
            Assert.IsNotNull(job);
            world.Mill.Blade.Condition01 = 0.35f;
            Assert.IsTrue(world.Mill.Blade.IsDull);

            bool refused = world.Driver.RunProduction(job.JobId, Day, out string refusal);

            Assert.IsFalse(refused);
            Assert.IsNotNull(refusal);
            Assert.AreEqual(2, world.Mill.LogStock.TotalLogUnits);
            Assert.AreEqual(0, world.Counters.TotalSawMinutes);
            Assert.IsTrue(world.Mill.Blade.IsDull);
            WorkTask refusedTask = world.Tasks.FindTask(job.TaskId);
            Assert.IsNotNull(refusedTask);
            Assert.AreEqual(TaskStatus.Cancelled, refusedTask.Status);

            Assert.IsNull(world.Mill.RecordSawFiling(EntityId.For(EntityKind.Person, 5003), Day, world.Diagnostics));
            bool ran = world.Driver.RunProduction(job.JobId, Day, out string runRefusal);
            Assert.IsTrue(ran);
            Assert.IsNull(runRefusal);
            Assert.AreEqual(SawmillProductionJobState.Complete, job.State);
        }

        [Test]
        public void P7_RunProduction_WithBrokenBlade_Refuses_ConsumesNothing_FilingRefused()
        {
            World world = NewWorld(MillWithLogs(2));
            SawmillProductionJob job = world.Driver.BeginProduction(LotId(9101), Sawyer, 2, Day, out string beginRefusal);
            Assert.IsNotNull(job);
            world.Mill.Blade.Condition01 = 0.10f;
            Assert.IsTrue(world.Mill.Blade.IsBroken);

            bool refused = world.Driver.RunProduction(job.JobId, Day, out string refusal);

            Assert.IsFalse(refused);
            Assert.IsNotNull(refusal);
            Assert.AreEqual(2, world.Mill.LogStock.TotalLogUnits);
            Assert.AreEqual(0, world.Counters.TotalSawMinutes);
            Assert.IsTrue(world.Mill.Blade.IsBroken);
            Assert.IsNotNull(world.Mill.RecordSawFiling(EntityId.For(EntityKind.Person, 5003), Day, world.Diagnostics));
        }

        [Test]
        public void P8_RunProduction_WithWorkstationGateBlocking_Refuses_ConsumesNothing()
        {
            // Primary setup: a registry with NO sawmill-saw-line station.
            // If this setup does not block, the fallback is a station whose
            // FindComponent returns null for "saw-line-1".
            var tasks = new TaskAuthority();
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(Day);
            var counters = new SawmillLabourCounters();
            var diagnostics = new List<string>();
            var mill = MillWithLogs(2);
            var emptyRegistry = new BusinessWorkstations("mill-1");
            var gate = new EquipmentTaskGate
            {
                WorkstationsFor = (kind, id) => emptyRegistry,
                FindWorkstationDefinition = id => id == "sawmill-saw-line" ? WorkstationCatalog.SawmillSawLine : null,
                FindComponent = id => (WorkstationComponentView?)null,
                SupportSupplier = () =>
                {
                    var support = new SupportContext();
                    support.ConsumableUnits[SupportContext.Key("consumable", "mill-lubrication")] = 1;
                    support.AvailableRepairCapabilities.Add("saw-filing");
                    return support;
                },
            };
            var driver = new SawmillProductionDriver(mill, tasks, budgets, gate, counters, diagnostics);
            SawmillProductionJob job = driver.BeginProduction(LotId(9101), Sawyer, 2, Day, out string beginRefusal);
            Assert.IsNotNull(job);

            bool refused = driver.RunProduction(job.JobId, Day, out string refusal);

            Assert.IsFalse(refused);
            Assert.IsNotNull(refusal);
            Assert.AreEqual(2, mill.LogStock.TotalLogUnits);
            Assert.AreEqual(0, counters.TotalSawMinutes);
        }

        [Test]
        public void P9_RunProduction_BladeWearApplied_PerProfile()
        {
            World soft = NewWorld(MillWithLogs(2, "white pine"));
            SawmillProductionJob softJob = soft.Driver.BeginProduction(LotId(9101), Sawyer, 2, Day, out string softBegin);
            Assert.IsNotNull(softJob);
            Assert.IsTrue(soft.Driver.RunProduction(softJob.JobId, Day, out string softRefusal));
            Assert.AreEqual(0.92f, soft.Mill.Blade.Condition01, 0.001f);

            World hard = NewWorld(MillWithLogs(2, "oak"));
            SawmillProductionJob hardJob = hard.Driver.BeginProduction(LotId(9101), Sawyer, 2, Day, out string hardBegin);
            Assert.IsNotNull(hardJob);
            Assert.IsTrue(hard.Driver.RunProduction(hardJob.JobId, Day, out string hardRefusal));
            Assert.AreEqual(0.84f, hard.Mill.Blade.Condition01, 0.001f);
        }

        [Test]
        public void P10_RunProduction_NeverDoubleCounts_ScalarAndLotLumber()
        {
            World world = NewWorld(MillWithLogs(2));
            SawmillProductionJob job = world.Driver.BeginProduction(LotId(9101), Sawyer, 2, Day, out string beginRefusal);
            Assert.IsNotNull(job);
            Assert.IsTrue(world.Driver.RunProduction(job.JobId, Day, out string refusal));

            Assert.AreEqual(1, world.Mill.LumberStock.Lots.Count);
            Assert.AreEqual(8, world.Mill.LumberStock.TotalLumberUnits);
        }

        [Test]
        public void P11_RetryAfterDullBladeRefusal_MintsFreshTask_NeverDoubleWorks()
        {
            World world = NewWorld(MillWithLogs(2));
            SawmillProductionJob job = world.Driver.BeginProduction(LotId(9101), Sawyer, 2, Day, out string beginRefusal);
            Assert.IsNotNull(job);
            world.Mill.Blade.Condition01 = 0.35f;

            bool refused = world.Driver.RunProduction(job.JobId, Day, out string refusal);
            Assert.IsFalse(refused);
            EntityId firstTask = job.TaskId;
            WorkTask first = world.Tasks.FindTask(firstTask);
            Assert.IsNotNull(first);
            Assert.AreEqual(TaskStatus.Cancelled, first.Status);

            Assert.IsNull(world.Mill.RecordSawFiling(EntityId.For(EntityKind.Person, 5003), Day, world.Diagnostics));
            bool ran = world.Driver.RunProduction(job.JobId, Day, out string runRefusal);
            Assert.IsTrue(ran);
            Assert.IsTrue(job.TaskId != firstTask);
            WorkTask second = world.Tasks.FindTask(job.TaskId);
            Assert.IsNotNull(second);
            Assert.AreEqual(TaskStatus.Complete, second.Status);
            Assert.AreEqual(60, second.MinutesWorked);
            Assert.AreEqual(60, world.Counters.TotalSawMinutes);
        }

        [Test]
        public void P12_ExhaustedOwnerCanYieldUnstartedJobToEmployedSawyer_WithoutDuplicatingLogsOrOutput()
        {
            World world = NewWorld(MillWithLogs(2));
            EntityId owner = EntityId.For(EntityKind.Person, 5002);
            EntityId employee = EntityId.For(EntityKind.Person, 5003);
            // Opening-day shape: the owner has a legitimate 60-minute
            // management reservation and the felling drive reserves the
            // remaining 540 minutes for six 90-minute logs.
            Assert.IsTrue(world.Budgets.TryCommitTask(owner, 60, out string managementReason), managementReason);
            Assert.IsTrue(world.Budgets.TryCommitTask(owner,
                WorkTimeBudget.DefaultDailyWorkMinutes - 60, out string fellingReason), fellingReason);
            SawmillProductionJob job = world.Driver.BeginProduction(LotId(9101), owner, 2, Day, out string beginRefusal);
            Assert.IsNotNull(job, beginRefusal);

            Assert.IsFalse(world.Driver.RunProduction(job.JobId, Day, out string refusal));
            Assert.That(refusal, Does.Contain("daily work minutes remaining"));
            Assert.AreEqual(SawmillProductionJobState.Planned, job.State);
            Assert.AreEqual(2, world.Mill.LogStock.TotalLogUnits, "A refused assignment must not consume the physical log lot.");
            WorkTask refusedTask = world.Tasks.FindTask(job.TaskId);
            Assert.IsNotNull(refusedTask);
            Assert.AreEqual(TaskStatus.Cancelled, refusedTask.Status);

            Assert.IsTrue(world.Driver.TryReassignSawyerAfterUnstartedRefusal(job.JobId, employee, out string reassignRefusal), reassignRefusal);
            Assert.IsTrue(world.Driver.RunProduction(job.JobId, Day, out string retryRefusal), retryRefusal);

            Assert.AreEqual(SawmillProductionJobState.Complete, job.State);
            Assert.AreEqual(0, world.Mill.LogStock.TotalLogUnits);
            Assert.AreEqual(8, world.Mill.LumberStock.TotalLumberUnits);
            Assert.AreEqual(60, world.Budgets.GetOrCreate(employee).MinutesCommitted,
                "The successful sawyer pays the task's actual labour commitment.");
            Assert.AreEqual(WorkTimeBudget.DefaultDailyWorkMinutes,
                world.Budgets.GetOrCreate(owner).MinutesCommitted,
                "The exhausted owner's unrelated daily reservation remains intact.");
            Assert.AreEqual(60, world.Counters.TotalSawMinutes,
                "One successful physical run credits one production task.");

            Assert.IsFalse(world.Driver.TryReassignSawyerAfterUnstartedRefusal(job.JobId,
                EntityId.For(EntityKind.Person, 5004), out string completedRefusal));
            Assert.That(completedRefusal, Does.Contain("only an unstarted planned job"));
        }

        [Test]
        public void P13_SkillScaledLongerRun_WorksAssignedDuration_AndProducesOnce()
        {
            World world = NewWorld(MillWithLogs(2));
            world.Tasks.SetDurationEstimator(new ScaledTaskDurationEstimator(1.5f));
            SawmillProductionJob job = world.Driver.BeginProduction(LotId(9101), Sawyer, 2, Day,
                out string beginRefusal);
            Assert.IsNotNull(job, beginRefusal);

            bool ran = world.Driver.RunProduction(job.JobId, Day, out string refusal);

            Assert.IsTrue(ran, refusal);
            WorkTask task = world.Tasks.FindTask(job.TaskId);
            Assert.IsNotNull(task);
            Assert.AreEqual(TaskStatus.Complete, task.Status);
            Assert.AreEqual(90, task.PlannedMinutes,
                "The worker's assigned duration remains the authoritative task duration.");
            Assert.AreEqual(90, task.MinutesWorked,
                "The task must be fully worked before the mill consumes logs.");
            Assert.AreEqual(0, world.Mill.LogStock.TotalLogUnits);
            Assert.AreEqual(8, world.Mill.LumberStock.TotalLumberUnits);
            Assert.AreEqual(90, world.Budgets.GetOrCreate(Sawyer).MinutesWorked,
                "The sawyer's actual task work is charged once to the daily budget.");
            Assert.AreEqual(60, world.Counters.TotalSawMinutes,
                "The mill keeps the calibrated production minutes distinct from the task's skill-scaled duration.");
            Assert.AreEqual(SawmillProductionJobState.Complete, job.State);
        }

        // ---- FailProduction (1) ----

        [Test]
        public void F1_FailProduction_LeavesLotInYard_Untouched_FreshBeginSucceeds()
        {
            World world = NewWorld(MillWithLogs(2));
            SawmillProductionJob job = world.Driver.BeginProduction(LotId(9101), Sawyer, 2, Day, out string beginRefusal);
            Assert.IsNotNull(job);

            bool failed = world.Driver.FailProduction(job.JobId, Day, out string refusal);

            Assert.IsTrue(failed);
            Assert.IsNull(refusal);
            Assert.AreEqual(SawmillProductionJobState.Failed, job.State);
            Assert.AreEqual(2, world.Mill.LogStock.TotalLogUnits);
            Assert.AreEqual(0, world.Counters.TotalSawMinutes);

            SawmillProductionJob fresh = world.Driver.BeginProduction(LotId(9101), Sawyer, 2, Day, out string freshRefusal);
            Assert.IsNotNull(fresh);
            Assert.IsNull(freshRefusal);
        }

        // ---- Save round trip (7) ----

        [Test]
        public void S1_SaveRoundTrip_PreservesOpenSawingJob_WithTotalsAndTaskLink()
        {
            World world = NewWorld(MillWithLogs(4));
            SawmillProductionJob job = world.Driver.BeginProduction(LotId(9101), Sawyer, 1, Day, out string beginRefusal);
            Assert.IsNotNull(job);
            Assert.IsTrue(world.Driver.RunProduction(job.JobId, Day, out string runRefusal));
            Assert.AreEqual(SawmillProductionJobState.Sawing, job.State);
            EntityId runTask = job.TaskId;
            int minutesBeforeLoad = world.Counters.TotalSawMinutes;

            var fresh = new SawmillProductionDriver(world.Mill, world.Tasks, world.Budgets, world.Gate, world.Counters, new List<string>());
            fresh.LoadFromSaveDto(world.Driver.ToSaveDto());

            Assert.AreEqual(1, fresh.ProductionJobCount);
            SawmillProductionJob restored = fresh.GetJob(job.JobId);
            Assert.IsNotNull(restored);
            Assert.AreEqual(SawmillProductionJobState.Sawing, restored.State);
            Assert.AreEqual(4, restored.LumberUnitsTotal);
            Assert.AreEqual(30, restored.SawMinutesTotal);
            Assert.AreEqual(1, restored.SourceLumberLotIds.Count);
            Assert.IsTrue(restored.TaskId == runTask);
            Assert.AreEqual(minutesBeforeLoad, world.Counters.TotalSawMinutes);

            Assert.IsTrue(fresh.RunProduction(restored.JobId, Day, out string r2));
            Assert.IsTrue(fresh.RunProduction(restored.JobId, Day, out string r3));
            Assert.IsTrue(fresh.RunProduction(restored.JobId, Day, out string r4));
            Assert.AreEqual(SawmillProductionJobState.Complete, restored.State);
            Assert.AreEqual(0, world.Mill.LogStock.TotalLogUnits);
        }

        [Test]
        public void S2_NullDto_ClearsToEmpty()
        {
            World world = NewWorld(MillWithLogs(2));
            Assert.IsNotNull(world.Driver.BeginProduction(LotId(9101), Sawyer, 2, Day, out string beginRefusal));
            Assert.AreEqual(1, world.Driver.ProductionJobCount);

            world.Driver.LoadFromSaveDto(null);

            Assert.AreEqual(0, world.Driver.ProductionJobCount);
        }

        [Test]
        public void S3_LoadWithDuplicateJobId_KeepsFirst()
        {
            World world = NewWorld(MillWithLogs(2));
            var dto = LinkDto("sawmill:" + LotId(9101), LotId(9101), 5002, 2, 0);
            dto.ProductionJobLinks.Add(new SawmillProductionDriver.SawmillProductionDriverSaveDto.SawmillProductionJobLink
            {
                JobId = "sawmill:" + LotId(9101),
                LotId = LotId(9101),
                SawyerKind = (int)EntityKind.Person,
                SawyerNumber = 5003,
                MillBusinessId = "mill-1",
                LogsToSaw = 2,
                State = 0,
                CreatedDayIndex = Day,
            });

            world.Driver.LoadFromSaveDto(dto);

            Assert.AreEqual(1, world.Driver.ProductionJobCount);
            Assert.AreEqual(5002, world.Driver.GetJob("sawmill:" + LotId(9101)).SawyerNumber);
        }

        [Test]
        public void S4_LoadWithStateOutsideRange_SkipsLink()
        {
            World world = NewWorld(MillWithLogs(2));

            world.Driver.LoadFromSaveDto(LinkDto("sawmill:" + LotId(9101), LotId(9101), 5002, 2, 99));

            Assert.AreEqual(0, world.Driver.ProductionJobCount);
        }

        [Test]
        public void S5_LoadTwice_YieldsSameCount_NeverDuplicated()
        {
            World world = NewWorld(MillWithLogs(4));
            SawmillProductionJob job = world.Driver.BeginProduction(LotId(9101), Sawyer, 1, Day, out string beginRefusal);
            Assert.IsNotNull(job);
            Assert.IsTrue(world.Driver.RunProduction(job.JobId, Day, out string runRefusal));
            var dto = world.Driver.ToSaveDto();

            var fresh = new SawmillProductionDriver(world.Mill, world.Tasks, world.Budgets, world.Gate, world.Counters, new List<string>());
            fresh.LoadFromSaveDto(dto);
            Assert.AreEqual(1, fresh.ProductionJobCount);
            fresh.LoadFromSaveDto(dto);
            Assert.AreEqual(1, fresh.ProductionJobCount);
        }

        [Test]
        public void S6_LoadDropsLink_WhoseLotIsMissingFromYard()
        {
            World world = NewWorld(new SawmillShopRuntime("mill-1", new EntityIdRegistry()));

            world.Driver.LoadFromSaveDto(LinkDto("sawmill:" + LotId(9101), LotId(9101), 5002, 2, 0));

            Assert.AreEqual(0, world.Driver.ProductionJobCount);
            Assert.AreEqual(0, world.Mill.LogStock.TotalLogUnits);
        }

        [Test]
        public void S7_LoadDropsStaleTaskLink_ButKeepsJob()
        {
            World world = NewWorld(MillWithLogs(4));
            SawmillProductionJob job = world.Driver.BeginProduction(LotId(9101), Sawyer, 1, Day, out string beginRefusal);
            Assert.IsNotNull(job);
            Assert.IsTrue(world.Driver.RunProduction(job.JobId, Day, out string runRefusal));
            var dto = world.Driver.ToSaveDto();

            var freshTasks = new TaskAuthority();
            var freshBudgets = new WorkTimeBudgetStore();
            freshBudgets.EnsureDay(Day);
            var fresh = new SawmillProductionDriver(world.Mill, freshTasks, freshBudgets, world.Gate, world.Counters, new List<string>());
            fresh.LoadFromSaveDto(dto);

            Assert.AreEqual(1, fresh.ProductionJobCount);
            SawmillProductionJob restored = fresh.GetJob(job.JobId);
            Assert.IsNotNull(restored);
            Assert.AreEqual(SawmillProductionJobState.Sawing, restored.State);
            Assert.IsTrue(restored.TaskId == EntityId.Invalid);
        }

        // ---- Null handling (5) ----

        [Test]
        public void N1_NullMill_RefusesEveryMethod()
        {
            var driver = new SawmillProductionDriver(null, new TaskAuthority(), new WorkTimeBudgetStore(), BuildGate(), new SawmillLabourCounters(), new List<string>());

            SawmillProductionJob job = driver.BeginProduction(LotId(9101), Sawyer, 2, Day, out string beginRefusal);
            Assert.IsNull(job);
            Assert.IsNotNull(beginRefusal);
            Assert.IsFalse(driver.RunProduction("sawmill:" + LotId(9101), Day, out string runRefusal));
            Assert.IsNotNull(runRefusal);
            Assert.IsFalse(driver.FailProduction("sawmill:" + LotId(9101), Day, out string failRefusal));
            Assert.AreEqual(0, driver.ProductionJobCount);
        }

        [Test]
        public void N2_NullTaskAuthority_RefusesAtBegin()
        {
            var driver = new SawmillProductionDriver(MillWithLogs(2), null, new WorkTimeBudgetStore(), BuildGate(), new SawmillLabourCounters(), new List<string>());

            SawmillProductionJob job = driver.BeginProduction(LotId(9101), Sawyer, 2, Day, out string refusal);

            Assert.IsNull(job);
            Assert.IsNotNull(refusal);
            Assert.IsTrue(refusal.Contains("task authority"));
            Assert.AreEqual(0, driver.ProductionJobCount);
        }

        [Test]
        public void N3_NullGate_RefusesAtRunTime()
        {
            var tasks = new TaskAuthority();
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(Day);
            var counters = new SawmillLabourCounters();
            var mill = MillWithLogs(2);
            var driver = new SawmillProductionDriver(mill, tasks, budgets, null, counters, new List<string>());
            SawmillProductionJob job = driver.BeginProduction(LotId(9101), Sawyer, 2, Day, out string beginRefusal);
            Assert.IsNotNull(job);
            Assert.IsNull(beginRefusal);

            bool ran = driver.RunProduction(job.JobId, Day, out string refusal);

            Assert.IsFalse(ran);
            Assert.IsNotNull(refusal);
            Assert.AreEqual(2, mill.LogStock.TotalLogUnits);
            Assert.AreEqual(0, counters.TotalSawMinutes);
        }

        [Test]
        public void N4_NullCounters_RefusesAtBegin()
        {
            var driver = new SawmillProductionDriver(MillWithLogs(2), new TaskAuthority(), new WorkTimeBudgetStore(), BuildGate(), null, new List<string>());

            SawmillProductionJob job = driver.BeginProduction(LotId(9101), Sawyer, 2, Day, out string refusal);

            Assert.IsNull(job);
            Assert.IsNotNull(refusal);
            Assert.IsTrue(refusal.Contains("labour counters"));
            Assert.AreEqual(0, driver.ProductionJobCount);
        }

        [Test]
        public void N5_NullBudgetStore_RefusesAtBegin()
        {
            var driver = new SawmillProductionDriver(MillWithLogs(2), new TaskAuthority(), null, BuildGate(), new SawmillLabourCounters(), new List<string>());

            SawmillProductionJob job = driver.BeginProduction(LotId(9101), Sawyer, 2, Day, out string refusal);

            Assert.IsNull(job);
            Assert.IsNotNull(refusal);
            Assert.IsTrue(refusal.Contains("work-time budget store"));
            Assert.AreEqual(0, driver.ProductionJobCount);
        }
    }
}
