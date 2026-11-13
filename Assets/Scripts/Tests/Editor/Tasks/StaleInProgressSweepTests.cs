// Plan 05 (2026-11-13, Slice 05): stale-InProgress sweep decision tests.
//
// Pins the pure decision table of StaleInProgressSweep.Decide. The
// sweep's application code inside SimulationDrivers.AdvanceAutonomousWork
// is harness-excluded and is verified by inspection against this table
// plus the real-tracker round trip in test 9 below, not by a harness
// test. Helpers mirror AutonomousTaskProgressTests (StartTask,
// Reservation factory, real PersonScheduleTracker.TryReserve usage).
using System.Collections.Generic;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Tasks
{
    [TestFixture]
    public sealed class StaleInProgressSweepTests
    {
        private static readonly EntityId Owner = EntityId.For(EntityKind.Business, 4);
        private static readonly EntityId Worker = EntityId.For(EntityKind.Person, 7);
        private static string ReservationLabel(WorkTask task) => $"task:{task.TaskId}|{task.DefinitionId}";

        private static (TaskAuthority Authority, WorkTimeBudgetStore Budgets, WorkTask Task) StartTask(
            string definitionId, int plannedMinutes, string customerRef = null, EntityId? owner = null)
        {
            var authority = new TaskAuthority();
            Assert.IsTrue(authority.RegisterDefinition(new TaskDefinition(
                definitionId, definitionId, plannedMinutes), out string reason), reason);
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(1);
            WorkTask task = authority.CreateTaskWithPlannedMinutes(
                definitionId, owner ?? Owner, 1, plannedMinutes, customerRef: customerRef ?? string.Empty);
            Assert.IsTrue(authority.AssignTask(task.TaskId, Worker, budgets, out reason), reason);
            Assert.IsTrue(authority.StartTask(task.TaskId, 1, out reason), reason);
            Assert.AreEqual(TaskStatus.InProgress, task.Status);
            return (authority, budgets, task);
        }

        private static PersonActivityReservation Reservation(PersonActivityKind activity,
            int start, int duration, WorkTask task, int day = 1, bool released = false)
        {
            return new PersonActivityReservation
            {
                ReservationId = "sched-test",
                PersonId = Worker.Id,
                Activity = activity,
                DayIndex = day,
                StartMinuteOfDay = start,
                DurationMinutes = duration,
                Label = ReservationLabel(task),
                Released = released,
            };
        }

        private static StaleInProgressVerdict Decide(
            WorkTask task, IReadOnlyList<PersonActivityReservation> reservations,
            int minuteOfDay, bool isArrivalDrivenTravel = false)
        {
            return StaleInProgressSweep.Decide(
                task, reservations, 1, minuteOfDay, ReservationLabel(task), isArrivalDrivenTravel);
        }

        [Test]
        public void ExpiredWindow_RescueReDerive_CarriesStaleActivity()
        {
            (_, _, WorkTask task) = StartTask("ordinary-work", 60);
            var reservations = new List<PersonActivityReservation>
            {
                Reservation(PersonActivityKind.Work, 480, 60, task),
            };

            StaleInProgressVerdict verdict = Decide(task, reservations, 700);

            Assert.AreEqual(StaleInProgressDecision.RescueReDerive, verdict.Decision);
            Assert.AreEqual(PersonActivityKind.Work, verdict.ActivityToReserve,
                "the fresh window keeps the stale window's activity");
        }

        [TestCase("ordinary-work", "", PersonActivityKind.Work)]
        [TestCase("business-feed-shopping", "business-feed-trip:shop:purchase-1", PersonActivityKind.Shopping)]
        [TestCase("freight-service-meeting", "freight-meeting:session:request-1", PersonActivityKind.Shopping)]
        [TestCase(JourneyTravel.TravelTaskId, "", PersonActivityKind.Travel)]
        public void NoReservation_RescueReDerive_FallbackActivity(
            string definitionId, string customerRef, PersonActivityKind expectedActivity)
        {
            (_, _, WorkTask task) = StartTask(definitionId, 60, customerRef);

            StaleInProgressVerdict verdict = Decide(task, new List<PersonActivityReservation>(), 700);

            Assert.AreEqual(StaleInProgressDecision.RescueReDerive, verdict.Decision);
            Assert.AreEqual(expectedActivity, verdict.ActivityToReserve,
                "with no reservation to carry activity from, the start-path table decides");
        }

        [TestCase(660, 120, 700, StaleInProgressDecision.UntouchedHealthy)]
        [TestCase(900, 60, 700, StaleInProgressDecision.UntouchedHealthy)]
        [TestCase(600, 100, 700, StaleInProgressDecision.RescueReDerive)]
        public void ValidOrFutureWindow_Untouched(
            int start, int duration, int minuteOfDay, StaleInProgressDecision expected)
        {
            (_, _, WorkTask task) = StartTask("ordinary-work", 60);
            var reservations = new List<PersonActivityReservation>
            {
                Reservation(PersonActivityKind.Work, start, duration, task),
            };

            StaleInProgressVerdict verdict = Decide(task, reservations, minuteOfDay);

            Assert.AreEqual(expected, verdict.Decision,
                "a window covering now or later today is healthy; one ending exactly at now has zero overlap with the current tick");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ZeroRemaining_CompletesBeforeStaleness(bool validWindow)
        {
            (TaskAuthority authority, WorkTimeBudgetStore budgets, WorkTask task) = StartTask("ordinary-work", 60);
            // Record the full 60 via RecordWorkProgress, which (unlike
            // RecordWork) does NOT auto-complete — the task stays InProgress
            // at 0 remaining, the bricked state the sweep closes.
            Assert.AreEqual(60, task.MinutesRemaining);
            Assert.IsTrue(authority.RecordWorkProgress(task.TaskId, 60, budgets, 1,
                out bool reached, out string reason), reason);
            Assert.IsTrue(reached);
            Assert.AreEqual(0, task.MinutesRemaining);
            Assert.AreEqual(TaskStatus.InProgress, task.Status);

            var reservations = new List<PersonActivityReservation>
            {
                validWindow
                    ? Reservation(PersonActivityKind.Work, 660, 120, task)
                    : Reservation(PersonActivityKind.Work, 480, 60, task),
            };

            StaleInProgressVerdict verdict = Decide(task, reservations, 700);

            Assert.AreEqual(StaleInProgressDecision.CompleteZeroRemaining, verdict.Decision,
                "zero remaining completes even with a valid window (precedence pin)");
        }

        [Test]
        public void NoFitToday_Defer()
        {
            (_, _, WorkTask task) = StartTask("ordinary-work", 300);
            var reservations = new List<PersonActivityReservation>
            {
                Reservation(PersonActivityKind.Work, 0, 30, task),
            };

            Assert.AreEqual(StaleInProgressDecision.DeferNoFitToday,
                Decide(task, reservations, 1200).Decision,
                "1200 + 300 runs past midnight");
            Assert.AreEqual(StaleInProgressDecision.RescueReDerive,
                Decide(task, reservations, 60).Decision,
                "the same task at 60 fits today and is rescued");
        }

        [TestCase("freight:load:ship-1")]
        [TestCase("freight:unload:ship-1")]
        [TestCase("fr.load-freight")]
        [TestCase("fr.unload-freight")]
        public void FreightDefinition_Skip(string definitionId)
        {
            (_, _, WorkTask task) = StartTask(definitionId, 30);
            var reservations = new List<PersonActivityReservation>
            {
                Reservation(PersonActivityKind.Work, 480, 60, task),
            };

            StaleInProgressVerdict verdict = Decide(task, reservations, 700);

            Assert.AreEqual(StaleInProgressDecision.Skip, verdict.Decision,
                "freight load/unload tasks are Agent 2's (QA-01), never the autonomous sweep's");
        }

        [TestCase("ordinary-work")]
        [TestCase("fr.load-freight")]
        public void FreightOwner_Skip(string definitionId)
        {
            (_, _, WorkTask task) = StartTask(definitionId, 30,
                owner: EntityId.For(EntityKind.Business, 3));
            var reservations = new List<PersonActivityReservation>
            {
                Reservation(PersonActivityKind.Work, 480, 60, task),
            };

            StaleInProgressVerdict verdict = Decide(task, reservations, 700);

            Assert.AreEqual(StaleInProgressDecision.Skip, verdict.Decision,
                "a task owned by the freight business (id 3) is never the autonomous sweep's, whatever its definition id");
        }

        [Test]
        public void ArrivalDrivenTravel_Skip()
        {
            (_, _, WorkTask task) = StartTask("ordinary-work", 60);
            var reservations = new List<PersonActivityReservation>
            {
                Reservation(PersonActivityKind.Work, 480, 60, task),
            };

            StaleInProgressVerdict verdict = Decide(task, reservations, 700, isArrivalDrivenTravel: true);

            Assert.AreEqual(StaleInProgressDecision.Skip, verdict.Decision,
                "arrival-driven travel completes on actor arrival, not on its window");
        }

        [Test]
        public void ReleasedOnlyReservation_Rescue()
        {
            (_, _, WorkTask task) = StartTask("ordinary-work", 60);

            var releasedOnly = new List<PersonActivityReservation>
            {
                Reservation(PersonActivityKind.Work, 480, 60, task, released: true),
            };
            Assert.AreEqual(StaleInProgressDecision.RescueReDerive,
                Decide(task, releasedOnly, 700).Decision,
                "a released reservation is invisible, same as no window");

            var wrongDay = new List<PersonActivityReservation>
            {
                Reservation(PersonActivityKind.Work, 480, 60, task, day: 2),
            };
            Assert.AreEqual(StaleInProgressDecision.RescueReDerive,
                Decide(task, wrongDay, 700).Decision,
                "a reservation for another day does not cover today");
        }

        [Test]
        public void ReDeriveRoundTrip_OnRealTracker()
        {
            (_, _, WorkTask task) = StartTask("ordinary-work", 60);
            string label = ReservationLabel(task);
            var schedules = new PersonScheduleTracker();

            Assert.IsNull(schedules.TryReserve(Worker.Id, PersonActivityKind.Work, 1, 480, 60, label));
            List<PersonActivityReservation> stale = schedules.ActiveFor(Worker.Id, 1);
            Assert.AreEqual(1, stale.Count);
            string staleId = stale[0].ReservationId;

            schedules.Release(staleId);
            Assert.IsNull(schedules.TryReserve(Worker.Id, PersonActivityKind.Work, 1, 700, 30, label),
                "the released stale window frees [700, 730]");

            List<PersonActivityReservation> active = schedules.ActiveFor(Worker.Id, 1);
            Assert.AreEqual(1, active.Count);
            Assert.AreEqual(700, active[0].StartMinuteOfDay);
            Assert.AreEqual(730, active[0].EndMinuteOfDay);
            PersonActivityReservation old = null;
            foreach (PersonActivityReservation reservation in schedules.Reservations)
            {
                if (reservation != null && reservation.ReservationId == staleId)
                {
                    old = reservation;
                }
            }

            Assert.IsNotNull(old);
            Assert.IsTrue(old.Released, "the stale reservation stays released after the re-derive");

            // Collision case: the fresh window is taken, so the re-derive
            // refuses naming the blocking activity and the task stays parked
            // (InProgress, no window) instead of double-booking person time.
            var blocked = new PersonScheduleTracker();
            Assert.IsNull(blocked.TryReserve(Worker.Id, PersonActivityKind.Work, 1, 480, 60, label));
            string blockedStaleId = blocked.ActiveFor(Worker.Id, 1)[0].ReservationId;
            Assert.IsNull(blocked.TryReserve(Worker.Id, PersonActivityKind.Shopping, 1, 700, 60, "other:shopping"));
            blocked.Release(blockedStaleId);
            string refusal = blocked.TryReserve(Worker.Id, PersonActivityKind.Work, 1, 700, 30, label);
            Assert.IsNotNull(refusal, "the colliding window must refuse the re-derive");
            StringAssert.Contains("Shopping", refusal, "the refusal names the conflicting activity");
            List<PersonActivityReservation> stillActive = blocked.ActiveFor(Worker.Id, 1);
            Assert.AreEqual(1, stillActive.Count, "no new reservation exists after the refusal");
            Assert.AreEqual("other:shopping", stillActive[0].Label);
            PersonActivityReservation parkedOld = null;
            foreach (PersonActivityReservation reservation in blocked.Reservations)
            {
                if (reservation != null && reservation.ReservationId == blockedStaleId)
                {
                    parkedOld = reservation;
                }
            }

            Assert.IsNotNull(parkedOld);
            Assert.IsTrue(parkedOld.Released, "the stale id stays released in the parked state");
        }
    }
}
