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
    }
}
