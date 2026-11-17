using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// E2 (2026-11-17, workstream E): regression tests for the recalibrated
    /// construction labour values (brief section 18, scenarios N, O, P, S).
    /// LabourMinutes is TOTAL person-minutes; one worker-day is 600 minutes
    /// (WorkTimeBudget.DefaultDailyWorkMinutes), so a recalibrated package
    /// is worked across as many days as its total requires. These tests pin
    /// the per-day chunking that makes that true: CommitLabor books one
    /// day's chunk at a time, WorkCommittedLabor credits only what the
    /// day's task covers, and no package or project completes on partial
    /// work. All values here are PROVISIONAL per the E1 calibration report;
    /// if E1 comes back NOT VERIFIED, retune the numbers, not the shape.
    /// </summary>
    [TestFixture]
    public sealed class ConstructionLabourRecalibrationE2Tests
    {
        private sealed class FakeMaterialSource : IConstructionMaterialSource
        {
            public readonly Dictionary<string, int> Stock = new Dictionary<string, int>();

            public int AvailableUnits(ConstructionMaterialRequirement requirement)
            {
                return requirement == null ? 0
                    : Stock.TryGetValue(requirement.MaterialDisplayName, out int units) ? units : 0;
            }

            public string TryConsume(ConstructionMaterialRequirement requirement, int units,
                string projectLabel, int dayIndex, List<string> diagnostics, out string provenanceLabel)
            {
                provenanceLabel = string.Empty;
                int have = AvailableUnits(requirement);
                if (have < units)
                    return $"FakeMaterialSource: only {have} of '{requirement.MaterialDisplayName}'.";
                Stock[requirement.MaterialDisplayName] = have - units;
                provenanceLabel = $"lot '{requirement.MaterialDisplayName}' x{units} ({projectLabel}, day {dayIndex})";
                return null;
            }
        }

        private sealed class FakeToolCustody : IHouseholdToolCustody
        {
            private readonly HashSet<string> held = new HashSet<string>();
            public void Grant(int householdId, string toolId) => held.Add(householdId + ":" + toolId);
            public bool HouseholdHoldsTool(int householdId, string toolItemId) =>
                held.Contains(householdId + ":" + toolItemId);
        }

        private static ConstructionMaterialRequirement Material(string name, int units, string unitLabel)
        {
            return new ConstructionMaterialRequirement
            {
                RequirementId = "REQ-" + name,
                MaterialKind = ConstructionMaterialKind.Lumber,
                MaterialDisplayName = name,
                RequiredUnits = units,
                UnitLabel = unitLabel,
                ProvenanceKind = ConstructionMaterialProvenanceKind.AnyRealSource,
                SupplierBusinessId = "lumber-yard",
                SupplierDisplayName = "Lumber Yard",
            };
        }

        /// <summary>E2 fixture: one package at recalibrated scale (settler-cabin walls, 2100 person-minutes).</summary>
        private static BuildingDesign BigPackageDesign()
        {
            var design = new BuildingDesign
            {
                DesignId = "e2-big-package",
                DisplayName = "E2 big package",
                WorksKind = ConstructionWorksKind.Unspecified,
                KindLabel = "cabin",
            };
            var walls = new BuildingDesignPhase
            {
                PhaseName = "Walls", Sequence = 0, LaborMinutes = 2100,
                Notes = "E2 fixture: recalibrated-scale package, 2100 person-minutes",
            };
            walls.Materials.Add(Material("lumber", 100, "board feet"));
            walls.RequiredToolItemIds.Add("saw");
            design.Phases.Add(walls);
            design.SpaceSpecs.Add(new BuildingDesignSpaceSpec { Label = "main room", SleepingCapacity = 4 });
            return design;
        }

        /// <summary>E2 fixture: two packages, for the per-person double-booking test.</summary>
        private static BuildingDesign TwoPackageDesign()
        {
            var design = new BuildingDesign
            {
                DesignId = "e2-two-package",
                DisplayName = "E2 two packages",
                WorksKind = ConstructionWorksKind.Unspecified,
                KindLabel = "cabin",
            };
            var walls = new BuildingDesignPhase
            {
                PhaseName = "Walls", Sequence = 0, LaborMinutes = 2100, Notes = "log walls",
            };
            walls.Materials.Add(Material("lumber", 100, "board feet"));
            walls.RequiredToolItemIds.Add("saw");
            var roof = new BuildingDesignPhase
            {
                PhaseName = "Roof", Sequence = 1, LaborMinutes = 1500, Notes = "log roof",
            };
            roof.Materials.Add(Material("lumber", 100, "board feet"));
            roof.RequiredToolItemIds.Add("saw");
            design.Phases.Add(walls);
            design.Phases.Add(roof);
            design.SpaceSpecs.Add(new BuildingDesignSpaceSpec { Label = "main room", SleepingCapacity = 4 });
            return design;
        }

        private static FakeMaterialSource StockedSource()
        {
            var source = new FakeMaterialSource();
            source.Stock["lumber"] = 1000;
            return source;
        }

        private static FakeToolCustody EquippedCustody()
        {
            var custody = new FakeToolCustody();
            custody.Grant(1, "saw");
            return custody;
        }

        private static ConstructionProject StagedProject(BuildingDesign design,
            ConstructionProjectExecutor executor, List<string> diag, int dayIndex)
        {
            var catalog = new BuildingDesignCatalog();
            Assert.IsNull(catalog.RegisterDesign(design, diag));
            ConstructionProject project = executor.CreateProject(catalog, 1, design.DesignId, "parcel-9", dayIndex, diag);
            Assert.IsNotNull(project);
            Assert.IsNull(executor.CreateWorkPackages(project, design, dayIndex, diag));
            return project;
        }

        /// <summary>
        /// Works one day on a package the way the labour ports do: asks for
        /// the package's FULL required labour, and lets the executor clamp
        /// to the day's chunk. Returns the task id bound that day.
        /// </summary>
        private static string WorkOneDay(ConstructionProjectExecutor executor, ConstructionProject project,
            WorkPackage package, int personId, PersonScheduleTracker tracker, FakeToolCustody custody,
            TaskAuthority tasks, WorkTimeBudgetStore budgets, int day, List<string> diag)
        {
            Assert.IsNull(executor.CommitLabor(project, package, personId, tracker, custody, tasks, budgets,
                day, 360, package.LaborMinutesRequired, "build", diag), string.Join("; ", diag));
            string taskId = project.LaborTaskIds[project.LaborTaskIds.Count - 1];
            Assert.IsNull(executor.WorkCommittedLabor(project, package, taskId,
                package.LaborMinutesRequired, tasks, budgets, day, diag), string.Join("; ", diag));
            return taskId;
        }

        // ----------------------------------------------------------------
        // N: construction takes actual committed time
        // ----------------------------------------------------------------

        [Test]
        public void RecalibratedPackage_TakesActualCommittedTime_AcrossDays()
        {
            var diag = new List<string>();
            var executor = new ConstructionProjectExecutor();
            ConstructionProject project = StagedProject(BigPackageDesign(), executor, diag, 10);
            WorkPackage walls = project.WorkPackages[0];
            Assert.IsNull(executor.StagePackageMaterials(project, walls, StockedSource(), 11, diag));

            var tracker = new PersonScheduleTracker();
            var custody = EquippedCustody();
            var tasks = new TaskAuthority();
            var budgets = new WorkTimeBudgetStore();

            // Day 11: the full 2100 is asked for; only one worker-day (600) is committed and credited.
            Assert.IsNull(executor.CommitLabor(project, walls, 5, tracker, custody, tasks, budgets, 11, 360, 2100, "build", diag));
            WorkTask dayOneTask = tasks.FindTask(project.LaborTaskIds[project.LaborTaskIds.Count - 1]);
            Assert.IsNotNull(dayOneTask);
            Assert.AreEqual(600, dayOneTask.PlannedMinutes, "one worker-day is 600 minutes");
            Assert.IsNull(executor.WorkCommittedLabor(project, walls, dayOneTask.TaskId.ToString(), 2100, tasks, budgets, 11, diag));
            Assert.AreEqual(600, walls.LaborMinutesCommitted, "only the day's chunk credits, never the full request");
            string refusal = executor.AdvancePackage(project, walls, 11, diag);
            Assert.IsNotNull(refusal, "600 of 2100 does not finish the package");
            StringAssert.Contains("labor minutes", refusal);
            Assert.AreEqual(1, project.LaborTaskIds.Count, "one real task per day of work");

            // Days 12-13: two more 600-minute days.
            budgets.EnsureDay(12);
            WorkOneDay(executor, project, walls, 5, tracker, custody, tasks, budgets, 12, diag);
            Assert.AreEqual(1200, walls.LaborMinutesCommitted);
            Assert.IsNotNull(executor.AdvancePackage(project, walls, 12, diag));
            budgets.EnsureDay(13);
            WorkOneDay(executor, project, walls, 5, tracker, custody, tasks, budgets, 13, diag);
            Assert.AreEqual(1800, walls.LaborMinutesCommitted);
            Assert.IsNotNull(executor.AdvancePackage(project, walls, 13, diag));

            // Day 14: the remaining 300 minutes finish the package.
            budgets.EnsureDay(14);
            Assert.IsNull(executor.CommitLabor(project, walls, 5, tracker, custody, tasks, budgets, 14, 360, 2100, "build", diag));
            WorkTask lastTask = tasks.FindTask(project.LaborTaskIds[project.LaborTaskIds.Count - 1]);
            Assert.IsNotNull(lastTask);
            Assert.AreEqual(300, lastTask.PlannedMinutes, "the final day commits only the remaining labour");
            Assert.IsNull(executor.WorkCommittedLabor(project, walls, lastTask.TaskId.ToString(), 2100, tasks, budgets, 14, diag));
            Assert.AreEqual(2100, walls.LaborMinutesCommitted);
            Assert.IsNull(executor.AdvancePackage(project, walls, 14, diag));
            Assert.AreEqual(WorkPackageStatus.Complete, walls.Status);
            Assert.AreEqual(4, project.LaborTaskIds.Count, "one real task per day of work");
            Assert.AreEqual(2100, project.TotalLaborMinutesCommitted);
        }

        [Test]
        public void CommitLabor_ClampsToRemainingDailyBudget()
        {
            var diag = new List<string>();
            var executor = new ConstructionProjectExecutor();
            ConstructionProject project = StagedProject(BigPackageDesign(), executor, diag, 10);
            WorkPackage walls = project.WorkPackages[0];
            Assert.IsNull(executor.StagePackageMaterials(project, walls, StockedSource(), 11, diag));

            var tracker = new PersonScheduleTracker();
            var custody = EquippedCustody();
            var tasks = new TaskAuthority();
            var budgets = new WorkTimeBudgetStore();

            // Drain 300 of P5's 600-minute day elsewhere first, then finish
            // that work so P5 is free to start the build task (one in-progress
            // task per worker) while the 300 minutes stay spent.
            string drainRegistration;
            Assert.IsTrue(tasks.RegisterDefinition(new TaskDefinition("drain", "Drain worker budget", 300), out drainRegistration), drainRegistration);
            WorkTask drainTask = tasks.CreateTaskWithPlannedMinutes(
                "drain", EntityId.For(EntityKind.Household, 1), 11, 300);
            string drainFailure;
            Assert.IsTrue(tasks.AssignTask(drainTask.TaskId, EntityId.For(EntityKind.Person, 5), budgets, out drainFailure), drainFailure);
            Assert.IsTrue(tasks.StartTask(drainTask.TaskId, 11, out drainFailure), drainFailure);
            Assert.IsTrue(tasks.RecordWork(drainTask.TaskId, 300, budgets, 11, out drainFailure), drainFailure);
            // Recording the full 300 auto-completes the drain task, leaving
            // P5 free to start the build task with 300 minutes spent.
            Assert.AreEqual(TaskStatus.Complete, tasks.FindTask(drainTask.TaskId).Status,
                "the drain task finishes, leaving P5 free with 300 minutes spent");

            // The build commit asks for 2100; only the 300 remaining budget minutes are booked.
            Assert.IsNull(executor.CommitLabor(project, walls, 5, tracker, custody, tasks, budgets, 11, 360, 2100, "build", diag));
            WorkTask buildTask = tasks.FindTask(project.LaborTaskIds[project.LaborTaskIds.Count - 1]);
            Assert.IsNotNull(buildTask);
            Assert.AreEqual(300, buildTask.PlannedMinutes, "the commit is capped by the worker's remaining daily budget");
            Assert.IsNull(executor.WorkCommittedLabor(project, walls, buildTask.TaskId.ToString(), 2100, tasks, budgets, 11, diag));
            Assert.AreEqual(300, walls.LaborMinutesCommitted);
        }

        [Test]
        public void WorkCommittedLabor_NeverCreditsMoreThanTaskCovers()
        {
            var diag = new List<string>();
            var executor = new ConstructionProjectExecutor();
            ConstructionProject project = StagedProject(BigPackageDesign(), executor, diag, 10);
            WorkPackage walls = project.WorkPackages[0];
            Assert.IsNull(executor.StagePackageMaterials(project, walls, StockedSource(), 11, diag));

            var tracker = new PersonScheduleTracker();
            var custody = EquippedCustody();
            var tasks = new TaskAuthority();
            var budgets = new WorkTimeBudgetStore();

            Assert.IsNull(executor.CommitLabor(project, walls, 5, tracker, custody, tasks, budgets, 11, 360, 2100, "build", diag));
            string taskId = project.LaborTaskIds[project.LaborTaskIds.Count - 1];

            // Asking for the full 2100 credits only the day's 600-minute task.
            Assert.IsNull(executor.WorkCommittedLabor(project, walls, taskId, 2100, tasks, budgets, 11, diag));
            Assert.AreEqual(600, walls.LaborMinutesCommitted);
            Assert.AreEqual(TaskStatus.Complete, tasks.FindTask(taskId).Status, "the day's task is finished by its own 600 minutes");

            // A second credit against the finished task is refused, never double-counted.
            string refusal = executor.WorkCommittedLabor(project, walls, taskId, 2100, tasks, budgets, 11, diag);
            Assert.IsNotNull(refusal);
            Assert.AreEqual(600, walls.LaborMinutesCommitted);
        }

        // ----------------------------------------------------------------
        // O: no worker double-booking
        // ----------------------------------------------------------------

        [Test]
        public void DoubleBooking_IsPerPerson_NotGlobal()
        {
            var diag = new List<string>();
            var executor = new ConstructionProjectExecutor();
            ConstructionProject project = StagedProject(TwoPackageDesign(), executor, diag, 10);
            WorkPackage walls = project.WorkPackages[0];
            WorkPackage roof = project.WorkPackages[1];
            var source = StockedSource();
            Assert.IsNull(executor.StagePackageMaterials(project, walls, source, 11, diag));
            Assert.IsNull(executor.StagePackageMaterials(project, roof, source, 11, diag));

            var tracker = new PersonScheduleTracker();
            var custody = EquippedCustody();
            var tasks = new TaskAuthority();
            var budgets = new WorkTimeBudgetStore();

            // P5 works the walls package on day 11.
            Assert.IsNull(executor.CommitLabor(project, walls, 5, tracker, custody, tasks, budgets, 11, 360, 2100, "build", diag));

            // P5 cannot also work the roof package the same day — one worker, one activity at a time.
            string refusal = executor.CommitLabor(project, roof, 5, tracker, custody, tasks, budgets, 11, 360, 1500, "build", diag);
            Assert.IsNotNull(refusal, "one worker, one activity at a time");
            Assert.AreEqual(0, roof.LaborMinutesCommitted);

            // P6, with a free day, can work the roof package the same day.
            Assert.IsNull(executor.CommitLabor(project, roof, 6, tracker, custody, tasks, budgets, 11, 360, 1500, "build", diag), string.Join("; ", diag));
        }

        // ----------------------------------------------------------------
        // P: construction stalls without materials
        // ----------------------------------------------------------------

        [Test]
        public void RecalibratedPreset_StallsWithoutMaterials_NoLabourNoProgress()
        {
            var diag = new List<string>();
            var executor = new ConstructionProjectExecutor();
            BuildingDesign design = ProductionDesignPresets.SettlerCabin();
            ConstructionProject project = StagedProject(design, executor, diag, 10);
            WorkPackage sills = project.WorkPackages[0];

            // Empty yard: staging refuses and nothing is consumed.
            var emptySource = new FakeMaterialSource();
            string refusal = executor.StagePackageMaterials(project, sills, emptySource, 11, diag);
            Assert.IsNotNull(refusal);
            StringAssert.Contains("insufficient", refusal);
            Assert.AreEqual(WorkPackageStatus.Ready, sills.Status);
            Assert.AreEqual(0, project.TotalLaborMinutesCommitted);

            // Labour cannot start on an unstaged package, however large the crew.
            var tracker = new PersonScheduleTracker();
            var custody = EquippedCustody();
            var tasks = new TaskAuthority();
            var budgets = new WorkTimeBudgetStore();
            string laborRefusal = executor.CommitLabor(project, sills, 5, tracker, custody, tasks, budgets, 11, 360, 1200, "build", diag);
            Assert.IsNotNull(laborRefusal);
            Assert.AreEqual(0, project.TotalLaborMinutesCommitted);
        }

        // ----------------------------------------------------------------
        // S: completion requires finished work packages
        // ----------------------------------------------------------------

        [Test]
        public void PartialLabour_DoesNotCompletePackageOrProject()
        {
            var diag = new List<string>();
            var executor = new ConstructionProjectExecutor();
            ConstructionProject project = StagedProject(BigPackageDesign(), executor, diag, 10);
            WorkPackage walls = project.WorkPackages[0];
            Assert.IsNull(executor.StagePackageMaterials(project, walls, StockedSource(), 11, diag));

            var tracker = new PersonScheduleTracker();
            var custody = EquippedCustody();
            var tasks = new TaskAuthority();
            var budgets = new WorkTimeBudgetStore();

            // One day of real work: 600 of 2100.
            WorkOneDay(executor, project, walls, 5, tracker, custody, tasks, budgets, 11, diag);
            Assert.AreEqual(600, walls.LaborMinutesCommitted);

            string advanceRefusal = executor.AdvancePackage(project, walls, 11, diag);
            Assert.IsNotNull(advanceRefusal, "partial labour does not finish the package");
            StringAssert.Contains("labor minutes", advanceRefusal);
            Assert.AreNotEqual(WorkPackageStatus.Complete, walls.Status);

            string completeRefusal = executor.CompleteProject(project, BigPackageDesign(), new HousingAuthority(), 11, diag);
            Assert.IsNotNull(completeRefusal, "no fake completions on partial work");
            Assert.IsTrue(string.IsNullOrWhiteSpace(project.BuildingId));
            Assert.AreNotEqual(ConstructionProjectStatus.Complete, project.Status);
        }
    }
}
