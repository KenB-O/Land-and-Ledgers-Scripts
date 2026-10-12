using System.Collections.Generic;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.Editor.Population
{
    /// <summary>
    /// Pass IV (Worker 4): the domestic labor live driver. Proves:
    /// - DriveDay plans need-driven domestic tasks (meals at home → meal prep,
    ///   cold season → fuel, animals → care, baseline chores).
    /// - Tasks progress through REAL time via due events: AdvanceTick starts a
    ///   task when its window arrives; HandleDueEvent completes it with a
    ///   family-labor credit (zero wages) — identical outcomes at any tick size.
    /// - The orphaned-assignment fix: a task left Assigned by a start-failure
    ///   is requeued (not lost) and re-driven.
    /// - Save/load round-trips pending + in-flight driver state.
    /// </summary>
    [TestFixture]
    public sealed class DomesticLaborDriverTests
    {
        private sealed class Fixture
        {
            public readonly TaskAuthority Tasks = new TaskAuthority();
            public readonly WorkTimeBudgetStore Budgets = new WorkTimeBudgetStore();
            public readonly PersonScheduleTracker Schedules = new PersonScheduleTracker();
            public readonly DomesticLaborService Domestic = new DomesticLaborService();
            public readonly DomesticLaborDriver Driver = new DomesticLaborDriver();
            public readonly FamilyLaborLedger FamilyLabor = new FamilyLaborLedger();
            public readonly SimpleDueEventScheduler DueEvents = new SimpleDueEventScheduler();

            public Fixture()
            {
                var diag = new List<string>();
                DomesticTaskCatalog.RegisterAll(Tasks, diag);
            }

            public DomesticLaborDriver.DomesticDayInputs Inputs(int householdId, params int[] members)
            {
                return new DomesticLaborDriver.DomesticDayInputs
                {
                    HouseholdId = householdId,
                    MemberIds = new List<int>(members),
                    AgeBandFor = _ => AgeBand.Adult18Plus,
                    MealsAtHomeCount = 2,
                    ColdSeason = false,
                    HasAnimals = false,
                };
            }
        }

        [Test]
        public void DriveDay_PlansNeedDrivenTasks_ForFreeMembers()
        {
            var f = new Fixture();
            var diag = new List<string>();

            f.Driver.DriveDay(f.Inputs(7, 3, 4), 100, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag);

            // 2 home meals → meal prep + baseline chores = 2 pending tasks.
            Assert.AreEqual(2, f.Driver.Pending.Count);
            bool hasMealPrep = false, hasChores = false;
            foreach (var p in f.Driver.Pending)
            {
                if (p.DefinitionId == DomesticTaskCatalog.MealPrepId) hasMealPrep = true;
                if (p.DefinitionId == DomesticTaskCatalog.ChoresId) hasChores = true;
                Assert.AreEqual("Pending", p.Status);
            }

            Assert.IsTrue(hasMealPrep && hasChores, "expected meal-prep + chores planned");
        }

        [Test]
        public void DriveDay_ColdSeasonAndAnimals_AddFuelAndAnimalCare()
        {
            var f = new Fixture();
            var inputs = f.Inputs(7, 3);
            inputs.ColdSeason = true;
            inputs.HasAnimals = true;
            inputs.MealsAtHomeCount = 0;

            f.Driver.DriveDay(inputs, 100, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.DueEvents, null);

            var kinds = new HashSet<string>();
            foreach (var p in f.Driver.Pending)
            {
                kinds.Add(p.DefinitionId);
            }

            Assert.IsTrue(kinds.Contains(DomesticTaskCatalog.FuelHandlingId), "cold season → fuel handling");
            Assert.IsTrue(kinds.Contains(DomesticTaskCatalog.AnimalCareId), "animals → animal care");
            Assert.IsTrue(kinds.Contains(DomesticTaskCatalog.ChoresId), "baseline chores");
        }

        [Test]
        public void FullDayThroughDueEvents_CompletesWithFamilyLaborCredit()
        {
            var f = new Fixture();
            var diag = new List<string>();
            const int day = 100;

            f.Driver.DriveDay(f.Inputs(7, 3, 4), day, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag);

            // Simulate the day in 5-minute ticks: the driver starts tasks at
            // their windows and completes them when due events fire.
            float startOfDay = day * 86400f;
            for (int tick = 0; tick < 288; tick++)
            {
                float now = startOfDay + tick * 300f;
                f.Driver.AdvanceTick(now, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag);
                List<DueEvent> due = f.DueEvents.PopDue(now);
                foreach (DueEvent ev in due)
                {
                    f.Driver.HandleDueEvent(ev, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.FamilyLabor, f.DueEvents, day, diag);
                }
            }

            Assert.AreEqual(2, f.FamilyLabor.ContributionCount, "both planned tasks should complete\n" + string.Join("\n", diag));
            foreach (FamilyLaborContribution c in f.FamilyLabor.Contributions)
            {
                Assert.AreEqual(7, c.HouseholdId);
                Assert.AreEqual("domestic", c.TaskCategory);
            }

            Assert.AreEqual(0, f.Driver.InFlight.Count);
        }

        [Test]
        public void LargeTickStep_MatchesSmallTickStep()
        {
            int small = RunDay(tickSeconds: 300);
            int large = RunDay(tickSeconds: 21600);

            Assert.AreEqual(small, large, "due-event-driven completion must not depend on tick size");
            Assert.AreEqual(2, small);
        }

        private static int RunDay(int tickSeconds)
        {
            var f = new Fixture();
            var diag = new List<string>();
            const int day = 100;
            f.Driver.DriveDay(f.Inputs(7, 3, 4), day, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag);
            float startOfDay = day * 86400f;
            for (float now = startOfDay; now < startOfDay + 86400f; now += tickSeconds)
            {
                f.Driver.AdvanceTick(now, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag);
                foreach (DueEvent ev in f.DueEvents.PopDue(now))
                {
                    f.Driver.HandleDueEvent(ev, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.FamilyLabor, f.DueEvents, day, diag);
                }
            }

            // Drain any completion due just after the last tick (large steps
            // schedule completions past the final tick boundary).
            foreach (DueEvent ev in f.DueEvents.PopDue(startOfDay + 2 * 86400f))
            {
                f.Driver.HandleDueEvent(ev, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.FamilyLabor, f.DueEvents, day, diag);
            }

            return f.FamilyLabor.ContributionCount;
        }

        [Test]
        public void OrphanedAssignedTask_IsRequeued_NotLost()
        {
            var f = new Fixture();
            var diag = new List<string>();

            // Simulate the orphaned-assignment bug: a domestic task created and
            // assigned but never started (start-failure path), with no
            // DomesticAssignment tracking it.
            WorkTask orphan = f.Tasks.CreateTask(DomesticTaskCatalog.ChoresId,
                EntityId.For(EntityKind.Household, 7), 100);
            Assert.IsNotNull(orphan);
            string rejection;
            Assert.IsTrue(f.Tasks.AssignTask(orphan.TaskId, EntityId.For(EntityKind.Person, 3), f.Budgets, out rejection), rejection);
            Assert.AreEqual(TaskStatus.Assigned, orphan.Status);

            f.Driver.ReconcileStalled(7, f.Tasks, f.Budgets, f.Schedules, f.Domestic, diag);

            Assert.AreEqual(TaskStatus.Queued, orphan.Status, "orphan must be requeued, not left Assigned forever");
            Assert.IsTrue(diag.Count > 0, "the fix must be loud");
        }

        [Test]
        public void SaveLoad_RoundTripsPendingAndInFlight()
        {
            var f = new Fixture();
            var diag = new List<string>();
            const int day = 100;

            f.Driver.DriveDay(f.Inputs(7, 3, 4), day, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag);
            // Advance to just past the first task's window so it is in flight.
            f.Driver.AdvanceTick(day * 86400f + 700 * 60f, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag);
            Assert.AreEqual(1, f.Driver.InFlight.Count);

            var restored = new DomesticLaborDriver();
            restored.LoadFromSaveDto(f.Driver.CaptureSaveDto());

            Assert.AreEqual(f.Driver.Pending.Count, restored.Pending.Count);
            Assert.AreEqual(f.Driver.InFlight.Count, restored.InFlight.Count);
            Assert.AreEqual(f.Driver.InFlight[0].TaskId, restored.InFlight[0].TaskId);
        }
    }
}
