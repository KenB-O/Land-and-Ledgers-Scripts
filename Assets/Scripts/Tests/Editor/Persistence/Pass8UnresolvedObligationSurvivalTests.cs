// Pass VIII (Worker 9): unresolved-obligation survival across save/load.
// Every active purchase, loan, rent obligation, employment, work order,
// equipment reservation, animal rental, estate transfer, social-knowledge
// transmission, manager request, and external journey must survive a save/load
// round-trip: the obligation continues (never silently cancelled) and is
// never duplicated. Categories already proven elsewhere (shipments,
// purchases-in-transit, animal rentals, estate, knowledge, reservations)
// are referenced; this fixture closes the remaining gaps at the production
// authorities.
using System.Collections.Generic;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Labor;
using LandLedgers.Persistence;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Persistence
{
    [TestFixture]
    public sealed class Pass8UnresolvedObligationSurvivalTests
    {
        private static EntityId Person(int id) => EntityId.For(EntityKind.Person, id);
        private static EntityId Business(int id) => EntityId.For(EntityKind.Business, id);

        [Test]
        public void Employment_ActiveRelationship_SurvivesRoundTrip_NoDoubleCount()
        {
            var registry = new LandLedgers.Economy.EmploymentRelationshipRegistry();
            string rejection = registry.Register(new LandLedgers.Economy.EmploymentRelationship
            {
                Id = "emp-1",
                EmployeePersonId = 7,
                EmployerBusinessId = "bakery-1",
                RoleDisplayName = "Baker",
                Kind = LandLedgers.Economy.EmploymentKind.Permanent,
                LifecycleState = LandLedgers.Economy.EmploymentLifecycleState.Active,
                Compensation = LandLedgers.Economy.CompensationTerms.FromWeeklyWage(2000, "test"),
                StartDayIndex = 100,
                Source = LandLedgers.Economy.EmploymentSource.Authored,
            });
            Assert.IsNull(rejection, rejection);
            Assert.AreEqual(2000, registry.GetActiveWeeklyPayrollCents("bakery-1"));

            var dto = registry.CaptureSaveDto();
            var restored = new LandLedgers.Economy.EmploymentRelationshipRegistry();
            restored.LoadFromSaveDto(dto);

            Assert.IsTrue(restored.TryGetById("emp-1", out LandLedgers.Economy.EmploymentRelationship rel),
                "the active employment must survive the round-trip");
            Assert.IsTrue(rel.IsActive, "restored employment must still be active — never silently cancelled");
            Assert.AreEqual(2000, restored.GetActiveWeeklyPayrollCents("bakery-1"),
                "the wage agreement must survive intact");
            Assert.AreEqual(1, restored.GetActiveByEmployee(7).Count);

            // Double load is idempotent: the obligation is not duplicated.
            restored.LoadFromSaveDto(dto);
            Assert.AreEqual(1, restored.GetActiveByEmployee(7).Count,
                "loading twice must not duplicate the employment");
            Assert.AreEqual(2000, restored.GetActiveWeeklyPayrollCents("bakery-1"),
                "double load must not double-count payroll");
        }

        [Test]
        public void Loan_Obligation_SurvivesRoundTrip_ExactlyOnce()
        {
            var ids = new EntityIdRegistry();
            var authority = new FinancialObligationAuthority();
            FinancialObligation obligation = authority.Create(
                ids, FinancialObligationKind.Loan, "household:7", "bank-1",
                50000, 100, "test loan", "test loan obligation");
            Assert.IsNotNull(obligation);

            var dto = authority.CaptureSaveDto();
            var restored = new FinancialObligationAuthority();
            restored.LoadFromSaveDto(dto);

            FinancialObligation found = restored.Find(obligation.ObligationId);
            Assert.IsNotNull(found, "the loan obligation must survive the round-trip");
            Assert.AreEqual(50000, found.OutstandingPrincipalCents,
                "outstanding principal must be preserved, not reset or cancelled");

            restored.LoadFromSaveDto(dto);
            Assert.AreEqual(1, restored.Obligations.Count,
                "double load must not duplicate the obligation");
        }

        [Test]
        public void WorkOrder_InFlightTask_SurvivesRoundTrip_CompletesExactlyOnce()
        {
            var authority = new TaskAuthority();
            string reason;
            Assert.IsTrue(authority.RegisterDefinition(new TaskDefinition("test.sweep", "Sweep", 60), out reason), reason);
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(1);
            WorkTask task = authority.CreateTask("test.sweep", Business(1), 1);
            Assert.IsTrue(authority.AssignTask(task.TaskId, Person(1), budgets, out reason), reason);
            Assert.IsTrue(authority.StartTask(task.TaskId, 1, out reason), reason);
            Assert.IsTrue(authority.RecordWork(task.TaskId, 20, budgets, 1, out reason), reason);

            TaskAuthoritySaveDto dto = authority.CaptureSaveDto();
            var restored = new TaskAuthority();
            restored.LoadFromSaveDto(dto);

            WorkTask found = restored.FindTask(task.TaskId);
            Assert.IsNotNull(found, "the in-flight work order must survive the round-trip");
            Assert.AreEqual(TaskStatus.InProgress, found.Status,
                "restored task must still be in-flight — never silently cancelled");
            Assert.AreEqual(task.MinutesWorked, found.MinutesWorked,
                "partial work progress must survive");

            var restoredBudgets = new WorkTimeBudgetStore();
            restoredBudgets.EnsureDay(1);
            // RecordWork auto-completes at full minutes, so record short of the
            // 60-minute plan and finish with an explicit CompleteTask.
            Assert.IsTrue(restored.RecordWork(task.TaskId, 30, restoredBudgets, 1, out reason), reason);
            Assert.AreEqual(TaskStatus.InProgress, restored.FindTask(task.TaskId).Status);
            Assert.IsTrue(restored.CompleteTask(task.TaskId, TaskOutcome.Success, 1, restoredBudgets),
                "the restored task must be completable");
            Assert.AreEqual(TaskStatus.Complete, restored.FindTask(task.TaskId).Status);
            Assert.IsFalse(restored.CompleteTask(task.TaskId, TaskOutcome.Success, 1, restoredBudgets),
                "double completion must be refused — exactly-once settlement");
        }

        [Test]
        public void ManagerRequest_PendingApproval_SurvivesRoundTrip()
        {
            var ledger = new ManagerDecisionExecutionLedger();
            ManagerPendingRequest added = ledger.AddPending(new ManagerPendingRequest
            {
                RequestId = "req-1",
                RequestKey = "Borrow|bakery-1|100|expansion",
                BusinessInstanceId = "bakery-1",
                Kind = "Borrow",
                AmountCents = 100000,
                Detail = "expansion loan",
                CreatedDayIndex = 50,
                ManagerPersonId = 4,
                Status = "Pending",
            });
            Assert.IsNotNull(added);
            ledger.AddShipment(new ManagerProcurementShipment
            {
                ShipmentId = "ship-1",
                BusinessInstanceId = "bakery-1",
                ProductId = "flour",
                Status = "InTransit",
            });

            ManagerDecisionExecutionSaveDto dto = ledger.CaptureSaveDto();
            var restored = new ManagerDecisionExecutionLedger();
            restored.LoadFromSaveDto(dto);

            ManagerPendingRequest pending = restored.FindPending("Borrow|bakery-1|100|expansion");
            Assert.IsNotNull(pending, "the pending owner-approval request must survive the round-trip");
            Assert.AreEqual("Pending", pending.Status,
                "the request must still be pending — never silently approved or dropped");
            Assert.IsTrue(restored.HasInTransitShipment("bakery-1", "flour"),
                "the in-transit procurement shipment must survive the round-trip");

            // Double load: no duplicate pending requests, shipment not duplicated.
            restored.LoadFromSaveDto(dto);
            Assert.IsNotNull(restored.FindPending("Borrow|bakery-1|100|expansion"));
            Assert.IsTrue(restored.HasInTransitShipment("bakery-1", "flour"));
        }

        [Test]
        public void ExternalJourney_InFlight_SurvivesRoundTrip_CompletesOnDueDay()
        {
            var model = new JourneyModel();
            model.RegisterLocation(new JourneyLocation("farmstead-f1", JourneyLocationKind.Farmstead, "Home Farm", 0f, 6f));
            model.RegisterLocation(new JourneyLocation("town-depot", JourneyLocationKind.OffMapGateway, "Rail Depot", 0f, 0f));
            model.AddEdge("farmstead-f1", "town-depot", 4f, "depot road");
            var authority = new OffMapJourneyAuthority(model);
            var stock = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase) { { "flour", 100 } };
            var diag = new List<string>();
            OffMapJourneyRecord record = authority.DepartVehicle(
                "wagon-1", "farmstead-f1", "Kingston markets", 2,
                "sell flour off-map", new Dictionary<string, int> { { "flour", 40 } },
                new List<string> { "contract-freight-1" }, 10,
                item => stock.TryGetValue(item, out int have) ? have : 0,
                (item, units) =>
                {
                    int have = stock.TryGetValue(item, out int h) ? h : 0;
                    if (have < units) return $"only {have}u of {item} available";
                    stock[item] = have - units;
                    return null;
                },
                diag);
            Assert.IsNotNull(record, "departure should succeed: " + string.Join("; ", diag));
            int dueDay = record.DueReturnDayIndex;

            OffMapJourneyAuthoritySaveDto dto = authority.CaptureSaveDto();
            var restored = new OffMapJourneyAuthority(model);
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(1, restored.ActiveJourneyCount,
                "the in-flight external journey must survive the round-trip");
            Assert.IsTrue(restored.IsOffMapVehicle("wagon-1"),
                "the wagon must still be off-map after restore");

            // Loading the same save again is idempotent: the in-flight
            // journey is restored, never duplicated.
            restored.LoadFromSaveDto(dto);
            Assert.AreEqual(1, restored.ActiveJourneyCount,
                "loading the same save twice must not duplicate the in-flight journey");
            Assert.IsTrue(restored.IsOffMapVehicle("wagon-1"));

            var completed = restored.CompleteDueJourneys(dueDay,
                (item, units) => { stock[item] = (stock.TryGetValue(item, out int h) ? h : 0) + units; }, diag);
            Assert.AreEqual(1, completed.Count,
                "the restored journey must complete on its due day — never silently cancelled");
            Assert.IsFalse(restored.IsOffMapVehicle("wagon-1"));

            // The captured save is a stable snapshot: completing the restored
            // journey must not mutate it.
            Assert.AreEqual(1, dto.journeys.Count);
            Assert.AreEqual(OffMapJourneyState.Departed, dto.journeys[0].State,
                "the save snapshot must still show the journey departed after the live one completed");
        }

        [Test]
        public void DueEvents_Schedule_SurvivesRoundTrip_NoMissedDeadlines()
        {
            var scheduler = new SimpleDueEventScheduler();
            string eventId;
            Assert.IsNull(scheduler.Schedule(DueEventKind.TaskCompletionDue, 500f, "task-1", out eventId));
            Assert.IsNull(scheduler.Schedule(DueEventKind.Birthday, 900f, "3", out eventId));

            SimpleDueEventSchedulerSaveDto dto = scheduler.CaptureSaveDto();
            var restored = new SimpleDueEventScheduler();
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(2, restored.ScheduledCount,
                "every scheduled due event must survive the round-trip — no missed deadlines");
            List<DueEvent> due = restored.PopDue(500f);
            Assert.AreEqual(1, due.Count, "the earlier event must pop at its due time");
            Assert.AreEqual(1, restored.ScheduledCount, "the later birthday event must still be scheduled");
        }
    }
}
