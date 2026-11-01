using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Farming.Crops;
using LandLedgers.Population;
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

        // ---------- Pass IX farm integration: the reconciled weekly drive ----------
        // SharedBusinessRuntimeManager.ResolveFarmOperation delegates
        // CropFarm weekly resolution to FarmCycleDriver.DriveWeekly (the
        // entry exercised here — the manager itself is excluded from the
        // Roslyn harness subset). The manager's weekly order in
        // ResolveWeeklySharedOperations is: payroll and expenses post
        // BEFORE operation resolution, then the operation outcome is
        // recorded on the runtime. RunWeeklyLegs mirrors that order with
        // the real runtime APIs so the pass-through is proven, not assumed.

        private const int WeeklyWageCents = 300;
        private const int WeeklyExpenseCents = 200;
        private const int FarmStartingCashCents = 10000;

        private static BusinessRuntimeState NewFarmRuntime(HouseholdLedgerRegistry ledgers)
        {
            var employments = new EmploymentRelationshipRegistry();
            employments.Register(new EmploymentRelationship
            {
                Id = "farm-emp-501",
                EmployeePersonId = 501,
                EmployerBusinessId = FarmId,
                RoleDisplayName = "Field hand",
                Kind = EmploymentKind.Permanent,
                LifecycleState = EmploymentLifecycleState.Active,
                Compensation = CompensationTerms.FromWeeklyWage(WeeklyWageCents, "test"),
                StartDayIndex = 0,
                Source = EmploymentSource.Authored,
            });
            var runtime = new BusinessRuntimeState();
            runtime.BindBusinessIdentity(FarmId);
            runtime.EmploymentRegistry = employments;
            runtime.HouseholdLedgers = ledgers;
            runtime.PersonHouseholdIdLookup = pid => pid == 501 ? 11 : -1;
            runtime.PayrollDayIndex = SpringDay;
            runtime.SetCurrentCashCents(FarmStartingCashCents);
            // The abstract path credited THIS stock from staffing counts;
            // the reconciled path must never touch it (lots are the output).
            runtime.EnsureCategoryStock("crop_food", 0, 100);
            return runtime;
        }

        private static FarmCycleDriver.FarmWeeklyResult RunWeeklyLegs(
            Fixture f, FarmCycleDriver driver, BusinessRuntimeState runtime,
            HouseholdLedgerRegistry ledgers, int dayIndex, bool postPayrollAndExpenses)
        {
            if (postPayrollAndExpenses)
            {
                runtime.ResolveWeeklyPayroll();
                runtime.AddWeeklyLocalTransferCost(WeeklyExpenseCents, "farm operating expenses");
            }
            FarmCycleDriver.FarmWeeklyResult result = driver.DriveWeekly(
                f.Chain, FarmId, f.Tasks, f.Ids, dayIndex, f.Diag);
            // ResolveFarmOperation's recording pass-through.
            runtime.RecordWeeklyOperation(0, result.UnitsHarvested, result.BuildSummary(), result.BlockedReason);
            return result;
        }

        private static void AssertWagesAndExpensesPosted(
            BusinessRuntimeState runtime, HouseholdLedgerRegistry ledgers)
        {
            Assert.AreEqual(WeeklyWageCents, runtime.LastWeeklyPayrollCents,
                "weekly wages still post on the reconciled path");
            Assert.AreEqual(WeeklyWageCents, ledgers.Get(11).GetBalanceCents(),
                "the wage reaches the worker household (business out == household in)");
            Assert.AreEqual(WeeklyExpenseCents, runtime.LastWeeklyLocalTransferCostCents,
                "weekly operating expenses still post on the reconciled path");
            Assert.AreEqual(FarmStartingCashCents - WeeklyWageCents - WeeklyExpenseCents,
                runtime.CurrentCashCents, "cash reflects wages + expenses exactly once each");
        }

        [Test]
        public void WeeklyDrive_WithCompletedTasks_ProducesFarmOutputExactlyOnce()
        {
            Fixture f = NewFixture();
            var driver = new FarmCycleDriver();
            var ledgers = new HouseholdLedgerRegistry();
            BusinessRuntimeState runtime = NewFarmRuntime(ledgers);

            CompleteFieldTask(f, CropChain.PlantFieldTaskId, "wheat-40",
                "crop:Wheat;seed:80;source:general store", SpringDay);
            Assert.AreEqual(1, driver.DriveWeekly(f.Chain, FarmId, f.Tasks, f.Ids, SpringDay, f.Diag).FieldsPlanted,
                string.Join("; ", f.Diag));
            CompleteFieldTask(f, CropChain.TendFieldTaskId, "wheat-40", "", 40);
            Assert.AreEqual(1, driver.DriveWeekly(f.Chain, FarmId, f.Tasks, f.Ids, 40, f.Diag).FieldsTended,
                string.Join("; ", f.Diag));

            int harvestDay = SpringDay + 95; // wheat GrowDays = 95
            CompleteFieldTask(f, CropChain.HarvestFieldTaskId, "wheat-40", "", harvestDay);

            // One weekly drive: wages/expenses post first (production
            // order), then the delegated drive produces the harvest.
            FarmCycleDriver.FarmWeeklyResult week = RunWeeklyLegs(
                f, driver, runtime, ledgers, harvestDay, postPayrollAndExpenses: true);
            AssertWagesAndExpensesPosted(runtime, ledgers);
            Assert.AreEqual(1, week.FieldsHarvested, string.Join("; ", f.Diag));
            Assert.AreEqual(1, week.HarvestedLots.Count);
            Assert.Greater(week.UnitsHarvested, 0);
            Assert.AreEqual(week.HarvestedLots[0].QuantityUnits, week.UnitsHarvested,
                "the harvest lot is the one and only output credit of the weekly drive");
            Assert.AreEqual("general store", week.HarvestedLots[0].SeedSource);
            Assert.AreEqual(week.UnitsHarvested, runtime.LastWeeklyOutputUnitsProduced);
            Assert.AreEqual(0, runtime.GetCategoryStock("crop_food").CurrentStockUnits,
                "no abstract category-stock output beside the lot (no double production)");

            // The reconciled weekly path runs again: the completed tasks
            // were consumed exactly once, so nothing is produced again and
            // no second lot appears.
            FarmCycleDriver.FarmWeeklyResult again = driver.DriveWeekly(
                f.Chain, FarmId, f.Tasks, f.Ids, harvestDay, f.Diag);
            Assert.AreEqual(0, again.TasksConsumed);
            Assert.AreEqual(0, again.UnitsHarvested, "a second weekly drive never re-credits the same work");
            Assert.AreEqual(0, again.HarvestedLots.Count);
            Assert.AreEqual(0, runtime.GetCategoryStock("crop_food").CurrentStockUnits);
        }

        [Test]
        public void WeeklyDrive_NoCompletedTasks_ProducesNoFarmOutput_WhileWagesAndExpensesStillPost()
        {
            Fixture f = NewFixture();
            var driver = new FarmCycleDriver();
            var ledgers = new HouseholdLedgerRegistry();
            BusinessRuntimeState runtime = NewFarmRuntime(ledgers);

            FarmCycleDriver.FarmWeeklyResult week = RunWeeklyLegs(
                f, driver, runtime, ledgers, SpringDay, postPayrollAndExpenses: true);

            Assert.AreEqual(0, week.TasksConsumed);
            Assert.AreEqual(0, week.UnitsHarvested,
                "staffing counts alone no longer produce farm output (A4-18/A4-19)");
            Assert.AreEqual(0, week.HarvestedLots.Count);
            Assert.IsFalse(string.IsNullOrWhiteSpace(week.BlockedReason),
                "the empty drive reports why, loudly");
            Assert.AreEqual(CropGrowthState.Prepared, f.Authority.GetField("wheat-40").GrowthState);
            Assert.AreEqual(0, runtime.GetCategoryStock("crop_food").CurrentStockUnits,
                "the abstract path adds no category-stock crop output");
            Assert.AreEqual(0, runtime.LastWeeklyOutputUnitsProduced);
            AssertWagesAndExpensesPosted(runtime, ledgers);
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
