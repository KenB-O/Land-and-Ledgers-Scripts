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
    /// - DriveDay plans tasks only when an explicit household need is supplied.
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
                    CleaningNeeded = true,
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

            // A cooking need and an explicit cleaning need → two tasks.
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
        public void DriveDay_SharesHouseholdWorkAcrossAvailableAdults()
        {
            var f = new Fixture();
            f.Driver.DriveDay(f.Inputs(7, 1, 2), 100, f.Tasks, f.Budgets,
                f.Schedules, f.Domestic, f.DueEvents, new List<string>());

            Assert.AreEqual(2, f.Driver.Pending.Count);
            Assert.AreNotEqual(f.Driver.Pending[0].PersonId, f.Driver.Pending[1].PersonId,
                "the second suitable adult should take the next household task when both have real free time");
        }

        [Test]
        public void DriveDay_ShiftsDomesticWorkIntoNearbyAvailableWindow()
        {
            var f = new Fixture();
            var diag = new List<string>();
            Assert.IsNull(f.Schedules.TryReserve(3, PersonActivityKind.Work, 100, 660, 45, "work obligation"));
            DomesticLaborDriver.DomesticDayInputs inputs = f.Inputs(7, 3);
            inputs.MealsAtHomeCount = 0;

            f.Driver.DriveDay(inputs, 100, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag);

            Assert.AreEqual(1, f.Driver.Pending.Count);
            Assert.GreaterOrEqual(f.Driver.Pending[0].StartMinuteOfDay, 705,
                "chores should move beyond the existing hard reservation rather than defer on the exact 11:00 instant");
            Assert.LessOrEqual(f.Driver.Pending[0].StartMinuteOfDay, 840);
        }

        [Test]
        public void AdvanceTick_ReschedulesSameDomesticNeedWhenHardReservationAppearsAfterPlanning()
        {
            var f = new Fixture();
            var diag = new List<string>();
            DomesticLaborDriver.DomesticDayInputs inputs = f.Inputs(7, 3);
            inputs.MealsAtHomeCount = 0;
            inputs.CleaningNeeded = false;
            inputs.FuelHandlingNeeded = true;
            inputs.IsWindowUnavailable = (personId, start, duration) => start < 600;

            f.Driver.DriveDay(inputs, 100, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag);

            Assert.AreEqual(1, f.Driver.Pending.Count);
            DomesticLaborDriver.PendingDomesticTask pending = f.Driver.Pending[0];
            Assert.AreEqual(DomesticTaskCatalog.FuelHandlingId, pending.DefinitionId);
            Assert.AreEqual(600, pending.StartMinuteOfDay);
            Assert.IsNull(f.Schedules.TryReserve(3, PersonActivityKind.Work, 100, 600, 5,
                "late freight meeting reservation"));

            float conflictStart = 100 * 86400f + 600 * 60f;
            f.Driver.AdvanceTick(conflictStart, f.Tasks, f.Budgets, f.Schedules,
                f.Domestic, f.DueEvents, diag);

            Assert.AreEqual("Pending", pending.Status,
                "a newly committed meeting must defer the same need instead of failing its only start attempt");
            Assert.AreEqual(615, pending.StartMinuteOfDay,
                "the existing deterministic 15-minute availability cadence is reused");
            Assert.AreEqual(0, f.Driver.InFlight.Count);
            Assert.AreEqual(0, f.Domestic.Assignments.Count);
            StringAssert.Contains("rescheduled 'domestic.fuel-handling'", string.Join("\n", diag));
            StringAssert.DoesNotContain("scheduling failed", string.Join("\n", diag));

            float retryStart = 100 * 86400f + 615 * 60f;
            f.Driver.AdvanceTick(retryStart, f.Tasks, f.Budgets, f.Schedules,
                f.Domestic, f.DueEvents, diag);

            Assert.AreEqual("Started", pending.Status);
            Assert.AreEqual(1, f.Driver.InFlight.Count);
            Assert.AreEqual(1, f.Domestic.Assignments.Count);
            Assert.IsTrue(f.Driver.InFlight[0].TaskId.IsValid);
        }

        [Test]
        public void NecessaryDomesticWorkPreemptsRestButPreservesHardObligations()
        {
            var f = new Fixture();
            var diag = new List<string>();
            Assert.IsNull(f.Schedules.TryReserve(3, PersonActivityKind.Resting, 100, 660, 60, "voluntary rest"));
            DomesticLaborDriver.DomesticDayInputs inputs = f.Inputs(7, 3);
            inputs.MealsAtHomeCount = 0;
            f.Driver.DriveDay(inputs, 100, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag);
            float start = 100 * 86400f + f.Driver.Pending[0].StartMinuteOfDay * 60f;

            f.Driver.AdvanceTick(start, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag);

            Assert.IsTrue(f.Schedules.ActiveFor(3, 100).Exists(r => r.Activity == PersonActivityKind.Work));
            Assert.IsFalse(f.Schedules.ActiveFor(3, 100).Exists(r => r.Activity == PersonActivityKind.Resting),
                "overlapping voluntary rest yields to household work");
            StringAssert.Contains("interrupted voluntary rest", string.Join("\n", diag));
        }

        [Test]
        public void RepeatedDriveDay_DoesNotDuplicateSameDayHouseholdTasks()
        {
            var f = new Fixture();
            var inputs = f.Inputs(7, 3, 4);
            var diag = new List<string>();
            f.Driver.DriveDay(inputs, 100, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag);
            int planned = f.Driver.Pending.Count;

            f.Driver.DriveDay(inputs, 100, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag);

            Assert.AreEqual(planned, f.Driver.Pending.Count,
                "repeated day-boundary delivery must not multiply household Tasks");
        }

        [Test]
        public void RequeuedTask_ResumesSameIdentityAndCompletesOnce()
        {
            var f = new Fixture();
            var diag = new List<string>();
            const int day = 100;
            WorkTask original = f.Tasks.CreateTask(DomesticTaskCatalog.ChoresId,
                EntityId.For(EntityKind.Household, 7), day);
            Assert.IsTrue(f.Tasks.AssignTask(original.TaskId, EntityId.For(EntityKind.Person, 3),
                f.Budgets, day, out string rejection), rejection);

            DomesticLaborDriver.DomesticDayInputs inputs = f.Inputs(7, 3, 4);
            inputs.MealsAtHomeCount = 0;
            f.Driver.DriveDay(inputs, day, f.Tasks, f.Budgets,
                f.Schedules, f.Domestic, f.DueEvents, diag);
            Assert.AreEqual(1, f.Driver.Pending.Count,
                "the requeued Task should be adopted instead of creating a replacement");
            Assert.AreEqual(original.TaskId, f.Driver.Pending[0].ExistingTaskId);

            float start = day * 86400f + 660 * 60f;
            f.Driver.AdvanceTick(start, f.Tasks, f.Budgets, f.Schedules,
                f.Domestic, f.DueEvents, diag);
            foreach (DueEvent ev in f.DueEvents.PopDue(start + original.PlannedMinutes * 60f))
                f.Driver.HandleDueEvent(ev, f.Tasks, f.Budgets, f.Schedules,
                    f.Domestic, f.FamilyLabor, f.DueEvents, day, diag);

            Assert.AreEqual(TaskStatus.Complete, original.Status);
            Assert.AreEqual(1, f.FamilyLabor.ContributionCount);
            Assert.AreEqual(original.TaskId, f.Domestic.Assignments[0].TaskId,
                "recovery must preserve the existing authoritative Task ID");
        }

        [Test]
        public void NewTask_DoesNotAdoptAnOlderClosedAssignmentWithTheSameDefinition()
        {
            var f = new Fixture();
            var diag = new List<string>();
            const int oldDay = 100;
            string refusal = f.Domestic.ScheduleDomesticWork(7, 3, DomesticTaskCatalog.ChoresId,
                oldDay, 660, f.Tasks, f.Budgets, f.Schedules, diag);
            Assert.IsNull(refusal, refusal);
            EntityId oldTaskId = f.Domestic.Assignments[0].TaskId;
            Assert.IsTrue(f.Tasks.CompleteTask(oldTaskId, TaskOutcome.Success, oldDay, f.Budgets),
                "simulate the owning Task authority closing an older assignment before its stale due event is observed");

            DomesticLaborDriver.DomesticDayInputs inputs = f.Inputs(7, 3, 4);
            inputs.MealsAtHomeCount = 0;
            int newDay = oldDay + 1;
            f.Driver.DriveDay(inputs, newDay, f.Tasks, f.Budgets,
                f.Schedules, f.Domestic, f.DueEvents, diag);
            float start = newDay * 86400f + 660 * 60f;
            f.Driver.AdvanceTick(start, f.Tasks, f.Budgets, f.Schedules,
                f.Domestic, f.DueEvents, diag);

            Assert.AreEqual(1, f.Driver.InFlight.Count, string.Join("\n", diag));
            Assert.AreNotEqual(oldTaskId, f.Driver.InFlight[0].TaskId,
                "the driver must track the newly started Task, never a stale same-definition assignment");
            Assert.AreEqual(TaskStatus.InProgress, f.Tasks.FindTask(f.Driver.InFlight[0].TaskId).Status);
        }

        [Test]
        public void DriveDay_ExplicitFuelAndCleaningNeeds_AddOnlyThoseTasks()
        {
            var f = new Fixture();
            var inputs = f.Inputs(7, 3);
            inputs.ColdSeason = true;
            inputs.FuelHandlingNeeded = true;
            inputs.CleaningNeeded = true;
            inputs.MealsAtHomeCount = 0;

            f.Driver.DriveDay(inputs, 100, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.DueEvents, null);

            var kinds = new HashSet<string>();
            foreach (var p in f.Driver.Pending)
            {
                kinds.Add(p.DefinitionId);
            }

            Assert.IsTrue(kinds.Contains(DomesticTaskCatalog.FuelHandlingId), "explicit household fuel need → fuel handling");
            Assert.IsTrue(kinds.Contains(DomesticTaskCatalog.ChoresId), "explicit cleaning need → cleaning work");
            Assert.IsFalse(kinds.Contains(DomesticTaskCatalog.AnimalCareId), "unowned animals are never inferred");
        }

        [Test]
        public void DriveDay_DoesNotCreateMealFuelOrCleaningTasksFromCalendarOrHouseholdSize()
        {
            var f = new Fixture();
            DomesticLaborDriver.DomesticDayInputs inputs = f.Inputs(7, 3, 4);
            inputs.MealsAtHomeCount = 0;
            inputs.ColdSeason = true;
            inputs.CleaningNeeded = false;
            inputs.FuelHandlingNeeded = false;

            f.Driver.DriveDay(inputs, 100, f.Tasks, f.Budgets, f.Schedules,
                f.Domestic, f.DueEvents, new List<string>());

            Assert.AreEqual(0, f.Driver.Pending.Count,
                "resident count and cold-season state alone do not create domestic appointments");
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

        [Test]
        public void GenericCompletionBackstop_DoesNotPostponeDomesticDueEvent()
        {
            var f = new Fixture();
            var diag = new List<string>();
            const int day = 100;
            float start = day * 86400f + 660 * 60f;
            DomesticLaborDriver.DomesticDayInputs inputs = f.Inputs(7, 3, 4);
            inputs.MealsAtHomeCount = 0; // isolate the single baseline-chores Task
            f.Driver.DriveDay(inputs, day, f.Tasks, f.Budgets,
                f.Schedules, f.Domestic, f.DueEvents, diag);

            // Start the planned domestic work at its real scheduled window.
            f.Driver.AdvanceTick(start, f.Tasks, f.Budgets, f.Schedules,
                f.Domestic, f.DueEvents, diag);
            DomesticAssignment chores = null;
            foreach (DomesticAssignment assignment in f.Domestic.Assignments)
            {
                if (assignment != null && assignment.DefinitionId == DomesticTaskCatalog.ChoresId)
                {
                    chores = assignment;
                    break;
                }
            }
            Assert.IsNotNull(chores, "the household's real chores task should be in flight");
            WorkTask task = f.Tasks.FindTask(chores.TaskId);
            Assert.IsNotNull(task);

            // Exercise the same ownership policy used by SimulationDrivers on
            // a later tick. It must preserve the original scheduled deadline.
            Assert.IsFalse(f.Driver.RequiresGenericCompletionBackstop(task));
            float laterTick = start + 10 * 60f;
            if (f.Driver.RequiresGenericCompletionBackstop(task))
            {
                string ignored;
                f.DueEvents.Schedule(DueEventKind.TaskCompletionDue,
                    laterTick + task.MinutesRemaining * 60f, task.TaskId.ToString(), out ignored);
            }

            List<DueEvent> due = f.DueEvents.PopDue(start + task.PlannedMinutes * 60f);
            bool choresDue = false;
            foreach (DueEvent dueEvent in due)
            {
                if (dueEvent.PayloadRef == task.TaskId.ToString()) choresDue = true;
                f.Driver.HandleDueEvent(dueEvent, f.Tasks, f.Budgets, f.Schedules,
                    f.Domestic, f.FamilyLabor, f.DueEvents, day, diag);
            }

            Assert.IsTrue(choresDue, "the domestic completion event must remain at its original due time");
            Assert.AreEqual(TaskStatus.Complete, task.Status);
            Assert.AreEqual(1, f.FamilyLabor.ContributionCount);
            Assert.AreEqual(0, f.Driver.InFlight.Count);
        }

        private static int RunDay(int tickSeconds)
        {
            var f = new Fixture();
            var diag = new List<string>();
            const int day = 100;
            f.Driver.DriveDay(f.Inputs(7, 3, 4), day, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag);
            float startOfDay = day * 86400f;
            // Pop-then-advance, matching the production call order:
            // SimulationDrivers.OnShortTick pops due events (ProcessDueEvents)
            // before DomesticLaborDriver.AdvanceTick, and LivingWorldSim.AdvanceTo
            // likewise pops before advancing. Advancing first lets a large tick
            // attempt a new start while an earlier task's completion is still
            // unpopped, so the start is refused and the entry fails permanently.
            for (float now = startOfDay; now < startOfDay + 86400f; now += tickSeconds)
            {
                foreach (DueEvent ev in f.DueEvents.PopDue(now))
                {
                    f.Driver.HandleDueEvent(ev, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.FamilyLabor, f.DueEvents, day, diag);
                }
                f.Driver.AdvanceTick(now, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag);
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
        public void OrphanedInProgressDomesticTask_IsRequeuedAndReleasesItsReservationWithoutCredit()
        {
            var f = new Fixture();
            var diag = new List<string>();
            Assert.IsNull(f.Domestic.ScheduleDomesticWork(7, 3, DomesticTaskCatalog.MealPrepId,
                100, 990, f.Tasks, f.Budgets, f.Schedules, diag));
            WorkTask task = f.Domestic.Assignments[0] != null
                ? f.Tasks.FindTask(f.Domestic.Assignments[0].TaskId) : null;
            Assert.NotNull(task);
            Assert.AreEqual(TaskStatus.InProgress, task.Status);
            Assert.IsTrue(f.Schedules.ActiveFor(3, 100).Exists(r => r.Activity == PersonActivityKind.Cooking));
            Assert.AreEqual(0, f.Driver.InFlight.Count, "simulate a lost driver flight, not a completed task");

            f.Driver.ReconcileStalled(7, f.Tasks, f.Budgets, f.Schedules, f.Domestic, diag);

            Assert.AreEqual(TaskStatus.Queued, task.Status);
            Assert.IsTrue(f.Domestic.Assignments[0].Completed,
                "the orphaned assignment is retired so adoption can create a fresh physical flight");
            Assert.IsFalse(f.Schedules.ActiveFor(3, 100).Exists(r => r.Activity == PersonActivityKind.Cooking),
                "the abandoned domestic reservation must not keep occupying the person");
            Assert.AreEqual(0, f.FamilyLabor.ContributionCount,
                "reconciliation cannot fabricate worked domestic minutes");
            StringAssert.Contains("no in-flight arrival/completion owner", string.Join("\n", diag));
        }

        [Test]
        public void ReconcileStalled_PreservesInProgressDomesticTaskWithLiveFlight()
        {
            var f = new Fixture();
            var diag = new List<string>();
            DomesticLaborDriver.DomesticDayInputs inputs = f.Inputs(7, 3);
            inputs.CleaningNeeded = false;
            f.Driver.DriveDay(inputs, 100, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag);
            f.Driver.AdvanceTick(100 * 86400f + 990 * 60f, f.Tasks, f.Budgets,
                f.Schedules, f.Domestic, f.DueEvents, diag,
                (_, __) => DomesticActivityMovementResult.Traveling);
            WorkTask task = f.Tasks.GetOpenTasks(EntityId.For(EntityKind.Household, 7))
                .Find(t => t.DefinitionId == DomesticTaskCatalog.MealPrepId);
            Assert.NotNull(task);
            Assert.AreEqual(TaskStatus.InProgress, task.Status);
            Assert.AreEqual(1, f.Driver.InFlight.Count);

            f.Driver.ReconcileStalled(7, f.Tasks, f.Budgets, f.Schedules, f.Domestic, diag);

            Assert.AreEqual(TaskStatus.InProgress, task.Status,
                "a matching live flight remains responsible for physical arrival and completion");
            Assert.AreEqual(1, f.Driver.InFlight.Count);
        }

        [Test]
        public void Driver_RefusedStart_IsQueuedImmediately_AndReadoptedNextDay()
        {
            var f = new Fixture();
            var diag = new List<string>();
            DomesticLaborDriver.DomesticDayInputs inputs = f.Inputs(7, 3);
            inputs.MealsAtHomeCount = 0;
            inputs.CleaningNeeded = true;
            inputs.HasAnimals = false;

            f.Driver.DriveDay(inputs, 100, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag);
            Assert.AreEqual(1, f.Driver.Pending.Count, "exactly one planned task (chores, window 660)");
            Assert.AreEqual(DomesticTaskCatalog.ChoresId, f.Driver.Pending[0].DefinitionId);

            // Occupier created AFTER DriveDay, so planning is unaffected: a
            // reservation-less InProgress task owned by the store business.
            var storeDef = new TaskDefinition("store.serve", "Serve customers", 60);
            string rejection;
            Assert.IsTrue(f.Tasks.RegisterDefinition(storeDef, out rejection), rejection);
            WorkTask occupier = f.Tasks.CreateTask("store.serve",
                EntityId.For(EntityKind.Business, 9001), 100);
            Assert.IsNotNull(occupier);
            Assert.IsTrue(f.Tasks.AssignTask(occupier.TaskId, EntityId.For(EntityKind.Person, 3),
                f.Budgets, out rejection), rejection);
            Assert.IsTrue(f.Tasks.StartTask(occupier.TaskId, 100, out rejection), rejection);

            f.Driver.AdvanceTick(100 * 86400f + 660 * 60f, f.Tasks, f.Budgets,
                f.Schedules, f.Domestic, f.DueEvents, diag);
            Assert.AreEqual(0, f.Driver.InFlight.Count, "the refused entry must not start");
            List<WorkTask> open = f.Tasks.GetOpenTasks(EntityId.For(EntityKind.Household, 7));
            Assert.AreEqual(1, open.Count);
            EntityId taskId = open[0].TaskId;
            // Red before the fix: Assigned. This is this test's only red
            // assertion; steps 4-5 pass before and after (before, via the
            // next-day ReconcileStalled sweep; after, by direct adoption).
            Assert.AreEqual(TaskStatus.Queued, open[0].Status,
                "refused task must be Queued immediately, with no DriveDay since the refusal");

            Assert.IsTrue(f.Tasks.InterruptTask(occupier.TaskId, f.Budgets, out rejection), rejection);

            f.Driver.DriveDay(inputs, 101, f.Tasks, f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag);
            Assert.AreEqual(1, f.Driver.Pending.Count);
            Assert.AreEqual(taskId, f.Driver.Pending[0].ExistingTaskId);
            Assert.AreEqual("Pending", f.Driver.Pending[0].Status);

            f.Driver.AdvanceTick(101 * 86400f + 660 * 60f, f.Tasks, f.Budgets,
                f.Schedules, f.Domestic, f.DueEvents, diag);
            Assert.AreEqual(TaskStatus.InProgress, f.Tasks.FindTask(taskId).Status);
            Assert.AreEqual(1, f.Driver.InFlight.Count);
            Assert.AreEqual(1, f.Domestic.Assignments.Count);
            Assert.AreEqual(taskId, f.Domestic.Assignments[0].TaskId);
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

        [Test]
        public void UnsupportedPhysicalRoute_BackoffSurvivesSaveLoadAndRetriesSameTask()
        {
            var f = new Fixture();
            var diag = new List<string>();
            const int failedDay = 100;
            DomesticLaborDriver.DomesticDayInputs inputs = f.Inputs(7, 3, 4);
            inputs.CleaningNeeded = false;

            f.Driver.DriveDay(inputs, failedDay, f.Tasks, f.Budgets, f.Schedules,
                f.Domestic, f.DueEvents, diag);
            f.Driver.AdvanceTick(failedDay * 86400f + 990 * 60f, f.Tasks, f.Budgets,
                f.Schedules, f.Domestic, f.DueEvents, diag, (flight, _) =>
                {
                    flight.ActivityLocationId = "stove: blocked by installed equipment";
                    return DomesticActivityMovementResult.Unsupported;
                });

            WorkTask task = f.Tasks.GetOpenTasks(EntityId.For(EntityKind.Household, 7))
                .Find(candidate => candidate.DefinitionId == DomesticTaskCatalog.MealPrepId);
            Assert.NotNull(task);
            Assert.AreEqual(TaskStatus.Queued, task.Status,
                "a failed physical route releases the worker and preserves the same task without completion");
            Assert.AreEqual(0, f.Driver.InFlight.Count);
            Assert.AreEqual(0, f.FamilyLabor.ContributionCount);
            Assert.IsFalse(f.Schedules.ActiveFor(3, failedDay)
                .Exists(reservation => reservation.Activity == PersonActivityKind.Cooking),
                "a failed physical trip releases its domestic schedule reservation");
            Assert.AreEqual(1, f.Driver.RouteRecoveries.Count);
            Assert.AreEqual(task.TaskId, f.Driver.RouteRecoveries[0].TaskId);
            Assert.AreEqual(failedDay + 2, f.Driver.RouteRecoveries[0].RetryAfterDay,
                "the first retry is delayed instead of immediately reclaiming a worker tomorrow");

            var restored = new DomesticLaborDriver();
            restored.LoadFromSaveDto(f.Driver.CaptureSaveDto());
            Assert.AreEqual(1, restored.RouteRecoveries.Count,
                "the route-failure history and retry date survive Save/Continue");

            restored.DriveDay(inputs, failedDay + 1, f.Tasks, f.Budgets,
                f.Schedules, f.Domestic, f.DueEvents, diag);
            Assert.AreEqual(0, restored.Pending.Count,
                "the same blocked route is not assigned again on the cooldown day");

            restored.DriveDay(inputs, failedDay + 2, f.Tasks, f.Budgets,
                f.Schedules, f.Domestic, f.DueEvents, diag);
            Assert.AreEqual(1, restored.Pending.Count);
            Assert.AreEqual(task.TaskId, restored.Pending[0].ExistingTaskId,
                "recovery retries the original open task; it does not duplicate the household need");

            restored.AdvanceTick((failedDay + 2) * 86400f + 990 * 60f, f.Tasks,
                f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag, (flight, _) =>
                {
                    flight.ActivityLocationId = "stove: still blocked";
                    return DomesticActivityMovementResult.Unsupported;
                });
            Assert.AreEqual(2, restored.RouteRecoveries[0].FailureCount);
            Assert.AreEqual(failedDay + 6, restored.RouteRecoveries[0].RetryAfterDay,
                "a recurring blockage increases the cooldown instead of retrying daily");
            restored.DriveDay(inputs, failedDay + 5, f.Tasks, f.Budgets,
                f.Schedules, f.Domestic, f.DueEvents, diag);
            Assert.AreEqual(0, restored.Pending.Count);
            restored.DriveDay(inputs, failedDay + 6, f.Tasks, f.Budgets,
                f.Schedules, f.Domestic, f.DueEvents, diag);
            Assert.AreEqual(1, restored.Pending.Count);
            Assert.AreEqual(task.TaskId, restored.Pending[0].ExistingTaskId);
        }

        [Test]
        public void MissingFirewoodAnchor_WaitsForCapabilityChangeAcrossSaveLoadWithoutHoldingWorker()
        {
            var f = new Fixture();
            var diag = new List<string>();
            const int failedDay = 200;
            DomesticLaborDriver.DomesticDayInputs inputs = f.Inputs(7, 3, 4);
            inputs.MealsAtHomeCount = 0;
            inputs.CleaningNeeded = false;
            inputs.FuelHandlingNeeded = true;
            inputs.FirewoodActivityLocationAvailable = false;

            f.Driver.DriveDay(inputs, failedDay, f.Tasks, f.Budgets,
                f.Schedules, f.Domestic, f.DueEvents, diag);
            Assert.AreEqual(1, f.Driver.Pending.Count);
            Assert.AreEqual(DomesticTaskCatalog.FuelHandlingId, f.Driver.Pending[0].DefinitionId);

            f.Driver.AdvanceTick(failedDay * 86400f + 990 * 60f, f.Tasks,
                f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag, (flight, _) =>
                {
                    flight.ActivityLocationId = DomesticLaborDriver.MissingFirewoodStorageLocationId;
                    return DomesticActivityMovementResult.Unsupported;
                });

            WorkTask task = f.Tasks.GetOpenTasks(EntityId.For(EntityKind.Household, 7))
                .Find(candidate => candidate.DefinitionId == DomesticTaskCatalog.FuelHandlingId);
            Assert.NotNull(task);
            Assert.AreEqual(TaskStatus.Queued, task.Status,
                "the impossible route cannot leave the worker permanently occupied");
            Assert.AreEqual(0, f.Driver.InFlight.Count);
            int activeAssignments = 0;
            foreach (DomesticAssignment assignment in f.Domestic.Assignments)
                if (assignment != null && !assignment.Completed) activeAssignments++;
            Assert.AreEqual(0, activeAssignments,
                "failed movement releases the domestic assignment reservation without credit");
            Assert.AreEqual(0, f.FamilyLabor.ContributionCount);
            Assert.AreEqual(1, f.Driver.RouteRecoveries.Count);
            Assert.IsTrue(f.Driver.RouteRecoveries[0].WaitForCapabilityChange,
                "an absent authored pickup point must not be retried on a periodic timer");

            var restored = new DomesticLaborDriver();
            restored.LoadFromSaveDto(f.Driver.CaptureSaveDto());
            Assert.IsTrue(restored.RouteRecoveries[0].WaitForCapabilityChange,
                "the structural blocker survives Save/Continue");

            restored.DriveDay(inputs, failedDay + 1, f.Tasks, f.Budgets,
                f.Schedules, f.Domestic, f.DueEvents, diag);
            Assert.AreEqual(0, restored.Pending.Count,
                "the same impossible task is not assigned to another worker while its anchor is absent");
            Assert.AreEqual(1, f.Tasks.GetOpenTasks(EntityId.For(EntityKind.Household, 7))
                .FindAll(candidate => candidate.DefinitionId == DomesticTaskCatalog.FuelHandlingId).Count,
                "the household need remains represented by the original open task");

            inputs.FirewoodActivityLocationAvailable = true;
            restored.DriveDay(inputs, failedDay + 2, f.Tasks, f.Budgets,
                f.Schedules, f.Domestic, f.DueEvents, diag);
            Assert.AreEqual(1, restored.Pending.Count,
                "a real capability change resumes the same need");
            Assert.AreEqual(task.TaskId, restored.Pending[0].ExistingTaskId,
                "recovery reuses the original task identity");
            Assert.IsFalse(restored.RouteRecoveries[0].WaitForCapabilityChange);
        }

        [Test]
        public void MissingFirewoodAnchor_WithStoveFallback_DoesNotParkTask()
        {
            var f = new Fixture();
            var diag = new List<string>();
            const int failedDay = 200;
            DomesticLaborDriver.DomesticDayInputs inputs = f.Inputs(7, 3, 4);
            inputs.MealsAtHomeCount = 0;
            inputs.CleaningNeeded = false;
            inputs.FuelHandlingNeeded = true;
            inputs.FirewoodActivityLocationAvailable = false;
            // Fix #1 (A4): the household has a stove to fall back to, so
            // the missing Firewood point is not a capability block.
            inputs.FuelHandlingStoveFallbackAvailable = true;

            f.Driver.DriveDay(inputs, failedDay, f.Tasks, f.Budgets,
                f.Schedules, f.Domestic, f.DueEvents, diag);
            Assert.AreEqual(1, f.Driver.Pending.Count);
            Assert.AreEqual(DomesticTaskCatalog.FuelHandlingId, f.Driver.Pending[0].DefinitionId);

            // Simulate the production movement refusal the OLD leg-0 code
            // returned (no Firewood point): the flight fails and the task
            // stays Queued. With the fallback input set, the next DriveDay
            // must re-adopt it instead of parking it.
            f.Driver.AdvanceTick(failedDay * 86400f + 990 * 60f, f.Tasks,
                f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag, (flight, _) =>
                {
                    flight.ActivityLocationId = DomesticLaborDriver.MissingFirewoodStorageLocationId;
                    return DomesticActivityMovementResult.Unsupported;
                });

            WorkTask task = f.Tasks.GetOpenTasks(EntityId.For(EntityKind.Household, 7))
                .Find(candidate => candidate.DefinitionId == DomesticTaskCatalog.FuelHandlingId);
            Assert.NotNull(task);
            Assert.AreEqual(TaskStatus.Queued, task.Status);

            f.Driver.DriveDay(inputs, failedDay + 1, f.Tasks, f.Budgets,
                f.Schedules, f.Domestic, f.DueEvents, diag);
            Assert.AreEqual(1, f.Driver.Pending.Count,
                "with the stove fallback available the retained fuel task is re-adopted, not capability-waited");
            Assert.AreEqual(task.TaskId, f.Driver.Pending[0].ExistingTaskId,
                "recovery reuses the original task identity");
        }

        [Test]
        public void MissingFirewoodAnchor_ParkedTaskResumesWhenStoveFallbackAppears()
        {
            var f = new Fixture();
            var diag = new List<string>();
            const int failedDay = 200;
            DomesticLaborDriver.DomesticDayInputs inputs = f.Inputs(7, 3, 4);
            inputs.MealsAtHomeCount = 0;
            inputs.CleaningNeeded = false;
            inputs.FuelHandlingNeeded = true;
            inputs.FirewoodActivityLocationAvailable = false;
            // No fallback known yet (legacy): the task parks as before.
            inputs.FuelHandlingStoveFallbackAvailable = null;

            f.Driver.DriveDay(inputs, failedDay, f.Tasks, f.Budgets,
                f.Schedules, f.Domestic, f.DueEvents, diag);
            f.Driver.AdvanceTick(failedDay * 86400f + 990 * 60f, f.Tasks,
                f.Budgets, f.Schedules, f.Domestic, f.DueEvents, diag, (flight, _) =>
                {
                    flight.ActivityLocationId = DomesticLaborDriver.MissingFirewoodStorageLocationId;
                    return DomesticActivityMovementResult.Unsupported;
                });

            WorkTask task = f.Tasks.GetOpenTasks(EntityId.For(EntityKind.Household, 7))
                .Find(candidate => candidate.DefinitionId == DomesticTaskCatalog.FuelHandlingId);
            Assert.NotNull(task);

            var restored = new DomesticLaborDriver();
            restored.LoadFromSaveDto(f.Driver.CaptureSaveDto());
            Assert.IsTrue(restored.RouteRecoveries[0].WaitForCapabilityChange,
                "the structural blocker survives Save/Continue");

            restored.DriveDay(inputs, failedDay + 1, f.Tasks, f.Budgets,
                f.Schedules, f.Domestic, f.DueEvents, diag);
            Assert.AreEqual(0, restored.Pending.Count, "still parked while no fallback is known");

            // The stove fallback becomes available (household has a stove):
            // the same retained task resumes without a duplicate need.
            inputs.FuelHandlingStoveFallbackAvailable = true;
            restored.DriveDay(inputs, failedDay + 2, f.Tasks, f.Budgets,
                f.Schedules, f.Domestic, f.DueEvents, diag);
            Assert.AreEqual(1, restored.Pending.Count,
                "the stove fallback resumes the retained fuel task");
            Assert.AreEqual(task.TaskId, restored.Pending[0].ExistingTaskId,
                "recovery reuses the original task identity");
            Assert.IsFalse(restored.RouteRecoveries[0].WaitForCapabilityChange);
        }

        [Test]
        public void RecoveredPhysicalRoute_ClearsFailureBackoffOnlyAfterArrival()
        {
            var f = new Fixture();
            var diag = new List<string>();
            const int day = 100;
            DomesticLaborDriver.DomesticDayInputs inputs = f.Inputs(7, 3);
            inputs.CleaningNeeded = false;
            f.Driver.DriveDay(inputs, day, f.Tasks, f.Budgets, f.Schedules,
                f.Domestic, f.DueEvents, diag);
            f.Driver.AdvanceTick(day * 86400f + 990 * 60f, f.Tasks, f.Budgets,
                f.Schedules, f.Domestic, f.DueEvents, diag, (flight, _) =>
                {
                    flight.ActivityLocationId = "stove: blocked";
                    return DomesticActivityMovementResult.Unsupported;
                });

            f.Driver.DriveDay(inputs, day + 2, f.Tasks, f.Budgets, f.Schedules,
                f.Domestic, f.DueEvents, diag);
            Assert.AreEqual(1, f.Driver.Pending.Count);
            f.Driver.AdvanceTick((day + 2) * 86400f + 990 * 60f, f.Tasks, f.Budgets,
                f.Schedules, f.Domestic, f.DueEvents, diag, (flight, _) =>
                {
                    flight.ActivityLocationId = "stove";
                    return DomesticActivityMovementResult.Arrived;
                });

            Assert.AreEqual(0, f.Driver.RouteRecoveries.Count,
                "the task's backoff clears after the resident physically arrives");
            Assert.AreEqual(1, f.Driver.InFlight.Count,
                "arrival starts timed work; it does not complete or award the task immediately");
            Assert.AreEqual(0, f.FamilyLabor.ContributionCount);
        }
    }
}
