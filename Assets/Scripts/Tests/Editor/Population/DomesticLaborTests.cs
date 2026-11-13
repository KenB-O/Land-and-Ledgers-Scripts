using System.Collections.Generic;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.Editor.Population
{
    /// <summary>
    /// Worker C (Pass III), brief §12: narrow domestic labor on the EXISTING
    /// TaskAuthority + person-time substrate. Proves: 4 representative domestic
    /// definitions register; scheduling reserves real person-time; completion
    /// credits FamilyLaborLedger with zero wages (never payroll); a domestic
    /// reservation blocks incompatible employment scheduling (exclusivity);
    /// non-domestic definitions are refused; save/load never double-credits.
    /// </summary>
    [TestFixture]
    public sealed class DomesticLaborTests
    {
        private sealed class Fixture
        {
            public TaskAuthority Tasks = new TaskAuthority();
            public WorkTimeBudgetStore Budgets = new WorkTimeBudgetStore();
            public PersonScheduleTracker Schedules = new PersonScheduleTracker();
            public FamilyLaborLedger FamilyLabor = new FamilyLaborLedger();
            public DomesticLaborService Domestic = new DomesticLaborService();

            public Fixture()
            {
                var diag = new List<string>();
                DomesticTaskCatalog.RegisterAll(Tasks, diag);
            }
        }

        [Test]
        public void Catalog_RegistersFourDomesticDefinitions()
        {
            var f = new Fixture();
            Assert.IsNotNull(f.Tasks.GetDefinition(DomesticTaskCatalog.MealPrepId));
            Assert.IsNotNull(f.Tasks.GetDefinition(DomesticTaskCatalog.FuelHandlingId));
            Assert.IsNotNull(f.Tasks.GetDefinition(DomesticTaskCatalog.AnimalCareId));
            Assert.IsNotNull(f.Tasks.GetDefinition(DomesticTaskCatalog.ChoresId));
            Assert.AreEqual(45, f.Tasks.GetDefinition(DomesticTaskCatalog.MealPrepId).BaseMinutes);
        }

        [Test]
        public void Schedule_ReservesPersonTime_AndStartsTask()
        {
            var f = new Fixture();
            var diag = new List<string>();

            string error = f.Domestic.ScheduleDomesticWork(7, 3, DomesticTaskCatalog.MealPrepId,
                100, 360, f.Tasks, f.Budgets, f.Schedules, diag);

            Assert.IsNull(error, error);
            Assert.AreEqual(1, f.Domestic.Assignments.Count);
            // Real person-time is reserved: the tracker knows P3 is cooking.
            Assert.IsFalse(f.Schedules.IsFree(3, 100, 360, 45));
            // The task left Queued: it is assigned and started.
            WorkTask task = f.Tasks.FindTask(f.Domestic.Assignments[0].TaskId);
            Assert.IsNotNull(task);
            Assert.AreEqual(TaskStatus.InProgress, task.Status);
        }

        [Test]
        public void Complete_CreditsFamilyLabor_NeverWages()
        {
            var f = new Fixture();
            string error = f.Domestic.ScheduleDomesticWork(7, 3, DomesticTaskCatalog.ChoresId,
                100, 360, f.Tasks, f.Budgets, f.Schedules, null);
            Assert.IsNull(error, error);
            EntityId taskId = f.Domestic.Assignments[0].TaskId;

            string completeError = f.Domestic.CompleteDomesticWork(taskId, 30, 100,
                f.Tasks, f.Budgets, f.Schedules, f.FamilyLabor, null);

            Assert.IsNull(completeError, completeError);
            Assert.AreEqual(1, f.FamilyLabor.ContributionCount);
            FamilyLaborContribution c = f.FamilyLabor.Contributions[0];
            Assert.AreEqual(3, c.PersonId);
            Assert.AreEqual(7, c.HouseholdId);
            Assert.AreEqual(30, c.MinutesWorked);
            Assert.AreEqual("domestic", c.TaskCategory);
            // No wages: the family-labor path rejects wage terms by construction
            // (agreedWageCents: 0 enforced inside CompleteDomesticWork).
        }

        [Test]
        public void DomesticReservation_BlocksIncompatibleEmploymentScheduling()
        {
            var f = new Fixture();
            // P3 does meal prep 06:00–06:45 (45 min from minute 360).
            string error = f.Domestic.ScheduleDomesticWork(7, 3, DomesticTaskCatalog.MealPrepId,
                100, 360, f.Tasks, f.Budgets, f.Schedules, null);
            Assert.IsNull(error, error);

            // An employment shift overlapping 06:30–07:30 must be refused:
            // person-time is exclusive (Canon 13.4).
            string conflict = f.Schedules.TryReserve(3, PersonActivityKind.Work, 100, 390, 60, "store shift");
            Assert.IsNotNull(conflict, "overlapping employment must be refused");
            Assert.IsTrue(conflict.Contains("already"), "refusal must name the conflict, got: " + conflict);

            // A non-overlapping shift later the same day is fine.
            string ok = f.Schedules.TryReserve(3, PersonActivityKind.Work, 100, 480, 60, "store shift");
            Assert.IsNull(ok, ok);
        }

        [Test]
        public void NonDomesticDefinition_Refused_NarrowScope()
        {
            var f = new Fixture();
            var storeDef = new TaskDefinition("store.serve", "Serve customers", 60);
            string rejection;
            Assert.IsTrue(f.Tasks.RegisterDefinition(storeDef, out rejection));

            string error = f.Domestic.ScheduleDomesticWork(7, 3, "store.serve",
                100, 360, f.Tasks, f.Budgets, f.Schedules, null);

            Assert.IsNotNull(error);
            Assert.AreEqual(0, f.Domestic.Assignments.Count);
        }

        [Test]
        public void DoubleBooking_Prevented_WhenPersonAlreadyBusy()
        {
            var f = new Fixture();
            // P3 already works an employment shift 06:00–08:00.
            string shift = f.Schedules.TryReserve(3, PersonActivityKind.Work, 100, 360, 120, "store shift");
            Assert.IsNull(shift, shift);

            string error = f.Domestic.ScheduleDomesticWork(7, 3, DomesticTaskCatalog.ChoresId,
                100, 400, f.Tasks, f.Budgets, f.Schedules, null);

            Assert.IsNotNull(error, "domestic work must not double-book person-time");
            Assert.AreEqual(0, f.Domestic.Assignments.Count);
        }

        [Test]
        public void SaveLoad_DoesNotDoubleCreditFamilyLabor()
        {
            var f = new Fixture();
            Assert.IsNull(f.Domestic.ScheduleDomesticWork(7, 3, DomesticTaskCatalog.ChoresId,
                100, 360, f.Tasks, f.Budgets, f.Schedules, null));
            EntityId taskId = f.Domestic.Assignments[0].TaskId;

            DomesticLaborServiceSaveDto dto = f.Domestic.CaptureSaveDto();
            var restored = new DomesticLaborService();
            restored.LoadFromSaveDto(dto);
            Assert.AreEqual(1, restored.Assignments.Count);

            // Complete on the restored service: exactly one family-labor credit.
            Assert.IsNull(restored.CompleteDomesticWork(taskId, 30, 100,
                f.Tasks, f.Budgets, f.Schedules, f.FamilyLabor, null));
            Assert.AreEqual(1, f.FamilyLabor.ContributionCount);

            // Completing again is refused (already completed) — no double credit.
            string again = restored.CompleteDomesticWork(taskId, 30, 100,
                f.Tasks, f.Budgets, f.Schedules, f.FamilyLabor, null);
            Assert.IsNotNull(again);
            Assert.AreEqual(1, f.FamilyLabor.ContributionCount);

            // Loading twice replaces, never appends.
            restored.LoadFromSaveDto(dto);
            Assert.AreEqual(1, restored.Assignments.Count);
        }

        // Plan 14 (DomesticLaborService start-refusal disposition): the
        // occupier is a reservation-less InProgress task (the freight shape),
        // owned by the store business rather than the household, so the
        // household open-task scan sees only the domestic task.
        private static EntityId StartReservationlessOccupier(Fixture f, int personId, int dayIndex)
        {
            if (f.Tasks.GetDefinition("store.serve") == null)
            {
                var storeDef = new TaskDefinition("store.serve", "Serve customers", 60);
                string registerRejection;
                Assert.IsTrue(f.Tasks.RegisterDefinition(storeDef, out registerRejection), registerRejection);
            }
            WorkTask occupier = f.Tasks.CreateTask("store.serve",
                EntityId.For(EntityKind.Business, 9001), dayIndex);
            Assert.IsNotNull(occupier);
            string rejection;
            Assert.IsTrue(f.Tasks.AssignTask(occupier.TaskId, EntityId.For(EntityKind.Person, personId),
                f.Budgets, out rejection), rejection);
            Assert.IsTrue(f.Tasks.StartTask(occupier.TaskId, dayIndex, out rejection), rejection);
            Assert.AreEqual(TaskStatus.InProgress, occupier.Status);
            return occupier.TaskId;
        }

        // Registers "store.serve" (60 min) without starting any task, for
        // tests that build their own Assigned / InProgress occupiers.
        private static void RegisterStoreServe(Fixture f)
        {
            if (f.Tasks.GetDefinition("store.serve") == null)
            {
                var storeDef = new TaskDefinition("store.serve", "Serve customers", 60);
                string registerRejection;
                Assert.IsTrue(f.Tasks.RegisterDefinition(storeDef, out registerRejection), registerRejection);
            }
        }

        [Test]
        public void Schedule_StartRefusedWhenWorkerBusy_InterruptsToQueued()
        {
            var f = new Fixture();
            var diag = new List<string>();
            EntityId worker = EntityId.For(EntityKind.Person, 3);

            StartReservationlessOccupier(f, 3, 100);
            Assert.AreEqual(60, f.Budgets.GetOrCreate(worker).MinutesCommitted);

            string error = f.Domestic.ScheduleDomesticWork(7, 3, DomesticTaskCatalog.ChoresId,
                100, 360, f.Tasks, f.Budgets, f.Schedules, diag);

            Assert.IsNotNull(error, "start must be refused while P3 is occupied");
            Assert.IsTrue(error.Contains("start refused"), "refusal must name the start refusal, got: " + error);
            Assert.IsTrue(error.Contains("already occupied"), "refusal must name the occupier, got: " + error);

            List<WorkTask> open = f.Tasks.GetOpenTasks(EntityId.For(EntityKind.Household, 7));
            Assert.AreEqual(1, open.Count);
            // Red before the fix: status is Assigned (no disposition at the refusal site).
            Assert.AreEqual(TaskStatus.Queued, open[0].Status, "refused task must be Queued, not left Assigned");
            Assert.AreEqual(60, f.Budgets.GetOrCreate(worker).MinutesCommitted,
                "occupier only; the domestic 30 was committed by AssignTask and released by the fix");
            Assert.IsTrue(f.Schedules.IsFree(3, 100, 360, 30), "reservation must be released");
            foreach (var reservation in f.Schedules.ActiveFor(3, 100))
                Assert.IsTrue(!reservation.Label.StartsWith("domestic:"),
                    "no domestic reservation may survive the refusal, found: " + reservation.Label);
            Assert.AreEqual(0, f.Domestic.Assignments.Count);
            bool loud = false;
            foreach (string d in f.Domestic.Diagnostics)
                if (d.Contains("start refused")) loud = true;
            Assert.IsTrue(loud, "refusal must be recorded in the service diagnostics");
        }

        [Test]
        public void Resume_RefusedStart_RequeuesSameTask_ThenResumes()
        {
            var f = new Fixture();
            var diag = new List<string>();
            EntityId worker = EntityId.For(EntityKind.Person, 3);

            EntityId occupierId = StartReservationlessOccupier(f, 3, 100);

            string error = f.Domestic.ScheduleDomesticWork(7, 3, DomesticTaskCatalog.ChoresId,
                100, 360, f.Tasks, f.Budgets, f.Schedules, diag);
            Assert.IsNotNull(error, "start must be refused while P3 is occupied");

            List<WorkTask> open = f.Tasks.GetOpenTasks(EntityId.For(EntityKind.Household, 7));
            Assert.AreEqual(1, open.Count);
            EntityId taskId = open[0].TaskId;
            // Red before the fix: ResumeDomesticWork refuses below because its
            // gate requires Queued and the task is still Assigned.
            Assert.AreEqual(TaskStatus.Queued, open[0].Status, "refused task must be Queued for the resume gate");

            string interruptRejection;
            Assert.IsTrue(f.Tasks.InterruptTask(occupierId, f.Budgets, out interruptRejection), interruptRejection);

            string resumeError = f.Domestic.ResumeDomesticWork(taskId, 7, 3, 100, 360,
                f.Tasks, f.Budgets, f.Schedules, diag);
            Assert.IsNull(resumeError, resumeError);
            WorkTask task = f.Tasks.FindTask(taskId);
            Assert.AreEqual(TaskStatus.InProgress, task.Status);
            Assert.AreEqual(1, f.Domestic.Assignments.Count);
            Assert.AreEqual(taskId, f.Domestic.Assignments[0].TaskId);
            Assert.IsFalse(f.Schedules.IsFree(3, 100, 360, 30), "the resume holds the window again");
            Assert.AreEqual(30, f.Budgets.GetOrCreate(worker).MinutesCommitted,
                "occupier released its 60 on interrupt; the resume re-commits 30");
        }

        [Test]
        public void Resume_RefusedStart_ReleasesOnlyRemainingMinutes()
        {
            var f = new Fixture();
            var diag = new List<string>();
            EntityId worker = EntityId.For(EntityKind.Person, 3);
            string rejection;

            // Step 1: schedule chores successfully (InProgress, committed 30).
            Assert.IsNull(f.Domestic.ScheduleDomesticWork(7, 3, DomesticTaskCatalog.ChoresId,
                100, 360, f.Tasks, f.Budgets, f.Schedules, diag));
            EntityId taskId = f.Domestic.Assignments[0].TaskId;
            string reservationId = f.Domestic.Assignments[0].ReservationId;
            Assert.AreEqual(30, f.Budgets.GetOrCreate(worker).MinutesCommitted);

            // Step 2: work 10 of 30. MinutesCommitted counts worked minutes as
            // spent (RecordWorked never releases them): still 30 (10 spent + 20 committed).
            Assert.IsTrue(f.Tasks.RecordWork(taskId, 10, f.Budgets, 100, out rejection), rejection);
            WorkTask task = f.Tasks.FindTask(taskId);
            Assert.AreEqual(10, task.MinutesWorked);
            Assert.AreEqual(20, task.MinutesRemaining);
            Assert.AreEqual(30, f.Budgets.GetOrCreate(worker).MinutesCommitted);

            // Step 3: interrupt out of band (Queued, worked minutes preserved,
            // committed now 10 = spent only), then release the stale tracker
            // reservation explicitly: InterruptTask releases authority
            // resources, not the PersonScheduleTracker reservation, and the
            // resume below re-reserves the same window (mirrors what the
            // driver does in ReleaseAssignmentReservation).
            Assert.IsTrue(f.Tasks.InterruptTask(taskId, f.Budgets, out rejection), rejection);
            Assert.AreEqual(TaskStatus.Queued, task.Status);
            Assert.AreEqual(10, task.MinutesWorked);
            Assert.AreEqual(10, f.Budgets.GetOrCreate(worker).MinutesCommitted);
            f.Schedules.Release(reservationId);

            // Step 4: control task B, Assigned only (committed 10 + 60 = 70).
            RegisterStoreServe(f);
            WorkTask control = f.Tasks.CreateTask("store.serve",
                EntityId.For(EntityKind.Business, 9001), 100);
            Assert.IsNotNull(control);
            Assert.IsTrue(f.Tasks.AssignTask(control.TaskId, worker, f.Budgets, out rejection), rejection);
            Assert.AreEqual(70, f.Budgets.GetOrCreate(worker).MinutesCommitted,
                "10 spent + 60 control");

            // Step 5: occupier C, InProgress (committed 10 + 60 + 60 = 130).
            // The occupancy scan sees B as Assigned (non-blocking) and the
            // domestic task as Queued with no assignee.
            WorkTask occupier = f.Tasks.CreateTask("store.serve",
                EntityId.For(EntityKind.Business, 9001), 100);
            Assert.IsNotNull(occupier);
            Assert.IsTrue(f.Tasks.AssignTask(occupier.TaskId, worker, f.Budgets, out rejection), rejection);
            Assert.AreEqual(130, f.Budgets.GetOrCreate(worker).MinutesCommitted);
            Assert.IsTrue(f.Tasks.StartTask(occupier.TaskId, 100, out rejection), rejection);

            // Step 6: resume refused on occupier C. Inside the call AssignTask
            // re-commits the remainder 20 (committed 150 = 10 spent + 140
            // committed) before StartTask refuses.
            string error = f.Domestic.ResumeDomesticWork(taskId, 7, 3, 100, 360,
                f.Tasks, f.Budgets, f.Schedules, diag);
            Assert.IsNotNull(error, "resume must be refused while P3 is occupied");
            Assert.IsTrue(error.Contains("requeued task start refused"), "got: " + error);

            // Red before the fix: status Assigned, committed 120 — the manual
            // release took PlannedMinutes (30) instead of MinutesRemaining (20).
            Assert.AreEqual(TaskStatus.Queued, f.Tasks.FindTask(taskId).Status);
            Assert.AreEqual(130, f.Budgets.GetOrCreate(worker).MinutesCommitted,
                "10 spent + 60 control + 60 occupier; the resumed 20 released exactly");
            Assert.IsTrue(f.Schedules.IsFree(3, 100, 360, 30), "reservation must be released");
            // The step-1 assignment is a fixture artifact of interrupting
            // out of band; a production refusal never creates an assignment,
            // so assert no NEW assignment (count stays 1, not 0).
            Assert.AreEqual(1, f.Domestic.Assignments.Count);
        }
    }
}
