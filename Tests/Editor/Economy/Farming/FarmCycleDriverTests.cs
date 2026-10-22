using System.Collections.Generic;
using LandLedgers.Economy.Farming.Crops;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy.Farming
{
    /// <summary>
    /// Pass IX WS3 (A4-18, A4-19): the farm cycle driver. Fields produce
    /// ONLY from driven (completed) TaskAuthority field work: planting
    /// from a completed plant task with named seed, tending from a
    /// completed tend task, and harvest lots credited at task completion —
    /// never at weekly resolution, never from growth time alone, and never
    /// twice for the same task.
    /// </summary>
    [TestFixture]
    public sealed class FarmCycleDriverTests
    {
        private const string FarmId = "farm-1";
        private const int SpringDay = 10;

        private sealed class Fixture
        {
            public CropFieldAuthority Authority = new CropFieldAuthority();
            public CropChain Chain;
            public TaskAuthority Tasks = new TaskAuthority();
            public WorkTimeBudgetStore Budgets = new WorkTimeBudgetStore();
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public EntityId Worker;
            public List<string> Diag = new List<string>();
        }

        private static Fixture NewFixture()
        {
            var f = new Fixture();
            f.Chain = new CropChain(f.Authority);
            CropChain.RegisterTaskDefinitions(f.Tasks);
            f.Budgets.EnsureDay(SpringDay);
            f.Worker = EntityId.For(EntityKind.Person, 501);
            var field = new CropFieldState("wheat-40", FarmId, 40f)
            {
                Suitability01 = 0.8f,
                Moisture01 = 0.7f,
                GrowthState = CropGrowthState.Prepared,
            };
            Assert.IsNull(f.Authority.RegisterField(field));
            return f;
        }

        private static void CompleteFieldTask(Fixture f, string definitionId, string fieldId,
            string termsRef, int dayIndex)
        {
            f.Budgets.EnsureDay(dayIndex);
            EntityId owner = FarmCycleDriver.BusinessEntityIdFor(FarmId);
            WorkTask task = f.Tasks.CreateTaskWithPlannedMinutes(
                definitionId, owner, dayIndex, 30, fieldId, termsRef);
            Assert.IsTrue(f.Tasks.AssignTask(task.TaskId, f.Worker, f.Budgets, out string a), a);
            Assert.IsTrue(f.Tasks.StartTask(task.TaskId, dayIndex, out string s), s);
            int guard = 0;
            while (task.MinutesRemaining > 0 && guard++ < 20)
            {
                Assert.IsTrue(f.Tasks.RecordWorkProgress(task.TaskId, 60, f.Budgets, dayIndex,
                    out bool done, out string w), w);
                if (done) break;
            }
            Assert.IsTrue(f.Tasks.CompleteTask(task.TaskId, TaskOutcome.Success, dayIndex, f.Budgets));
        }

        [Test]
        public void DrivenCycle_PlantTendHarvest_ProducesOnlyFromCompletedTasks()
        {
            Fixture f = NewFixture();
            var driver = new FarmCycleDriver();

            // No completed tasks: the drive produces nothing at all.
            FarmCycleDriver.FarmDriveResult empty = driver.DriveCompletedFieldTasks(
                f.Chain, FarmId, f.Tasks, f.Ids, SpringDay, f.Diag);
            Assert.AreEqual(0, empty.FieldsPlanted);
            Assert.AreEqual(0, empty.UnitsHarvested);
            Assert.AreEqual(CropGrowthState.Prepared, f.Authority.GetField("wheat-40").GrowthState,
                "an undriven field just sits there (A4-18)");

            // Plant from a completed plant task (named seed provenance).
            CompleteFieldTask(f, CropChain.PlantFieldTaskId, "wheat-40",
                "crop:Wheat;seed:80;source:general store", SpringDay);
            FarmCycleDriver.FarmDriveResult planted = driver.DriveCompletedFieldTasks(
                f.Chain, FarmId, f.Tasks, f.Ids, SpringDay, f.Diag);
            Assert.AreEqual(1, planted.FieldsPlanted, string.Join("; ", f.Diag));
            Assert.AreEqual(CropGrowthState.Planted, f.Authority.GetField("wheat-40").GrowthState);
            Assert.AreEqual("general store", f.Authority.GetField("wheat-40").SeedSource);

            // Tend from a completed tend task.
            CompleteFieldTask(f, CropChain.TendFieldTaskId, "wheat-40", "", 40);
            FarmCycleDriver.FarmDriveResult tended = driver.DriveCompletedFieldTasks(
                f.Chain, FarmId, f.Tasks, f.Ids, 40, f.Diag);
            Assert.AreEqual(1, tended.FieldsTended, string.Join("; ", f.Diag));
            Assert.Greater(f.Authority.GetField("wheat-40").Tended01, 0f);

            // Growth time alone (no harvest task) still produces nothing,
            // even once the field is Ready.
            int harvestDay = SpringDay + 95; // wheat GrowDays = 95
            FarmCycleDriver.FarmDriveResult noHarvest = driver.DriveCompletedFieldTasks(
                f.Chain, FarmId, f.Tasks, f.Ids, harvestDay, f.Diag);
            Assert.AreEqual(CropGrowthState.Ready, f.Authority.GetField("wheat-40").GrowthState);
            Assert.AreEqual(0, noHarvest.UnitsHarvested, "ripening is not production (A4-19)");
            Assert.AreEqual(0, noHarvest.HarvestedLots.Count);

            // Harvest from a completed harvest task: the lot is credited now,
            // at task completion, with full provenance.
            CompleteFieldTask(f, CropChain.HarvestFieldTaskId, "wheat-40", "", harvestDay);
            FarmCycleDriver.FarmDriveResult harvested = driver.DriveCompletedFieldTasks(
                f.Chain, FarmId, f.Tasks, f.Ids, harvestDay, f.Diag);
            Assert.AreEqual(1, harvested.FieldsHarvested, string.Join("; ", f.Diag));
            Assert.AreEqual(1, harvested.HarvestedLots.Count);
            CropLot lot = harvested.HarvestedLots[0];
            Assert.Greater(lot.QuantityUnits, 0);
            Assert.AreEqual("wheat-40", lot.FieldId);
            Assert.AreEqual(FarmId, lot.FarmId);
            Assert.AreEqual("general store", lot.SeedSource, "seed provenance flows into the harvest lot");
            Assert.AreEqual(CropGrowthState.Harvested, f.Authority.GetField("wheat-40").GrowthState);

            // Exactly once: re-driving never re-credits the same tasks.
            FarmCycleDriver.FarmDriveResult again = driver.DriveCompletedFieldTasks(
                f.Chain, FarmId, f.Tasks, f.Ids, harvestDay, f.Diag);
            Assert.AreEqual(0, again.TasksConsumed);
            Assert.AreEqual(0, again.UnitsHarvested);
        }

        [Test]
        public void PlantTask_WithoutSeedSource_RefusedLoudly_NothingPlanted()
        {
            Fixture f = NewFixture();
            var driver = new FarmCycleDriver();
            CompleteFieldTask(f, CropChain.PlantFieldTaskId, "wheat-40", "crop:Wheat;seed:80", SpringDay);

            FarmCycleDriver.FarmDriveResult result = driver.DriveCompletedFieldTasks(
                f.Chain, FarmId, f.Tasks, f.Ids, SpringDay, f.Diag);
            Assert.AreEqual(0, result.FieldsPlanted, "sourceless seed is refused — no orphan inputs");
            Assert.AreEqual(CropGrowthState.Prepared, f.Authority.GetField("wheat-40").GrowthState);
            Assert.IsTrue(f.Diag.Exists(d => d.Contains("seed source")), string.Join("; ", f.Diag));
        }

        [Test]
        public void ConsumedSet_SaveLoad_DoesNotReplant()
        {
            Fixture f = NewFixture();
            var driver = new FarmCycleDriver();
            CompleteFieldTask(f, CropChain.PlantFieldTaskId, "wheat-40",
                "crop:Wheat;seed:80;source:general store", SpringDay);
            Assert.AreEqual(1, driver.DriveCompletedFieldTasks(
                f.Chain, FarmId, f.Tasks, f.Ids, SpringDay, f.Diag).FieldsPlanted);

            var restored = new FarmCycleDriver();
            restored.LoadFromSaveDto(driver.CaptureSaveDto());
            FarmCycleDriver.FarmDriveResult again = restored.DriveCompletedFieldTasks(
                f.Chain, FarmId, f.Tasks, f.Ids, SpringDay, f.Diag);
            Assert.AreEqual(0, again.FieldsPlanted, "a restored driver never re-records a consumed task");
        }
    }
}
