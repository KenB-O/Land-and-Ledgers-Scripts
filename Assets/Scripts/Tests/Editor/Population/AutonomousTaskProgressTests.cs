using System.Collections.Generic;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    [TestFixture]
    public sealed class AutonomousTaskProgressTests
    {
        private static readonly EntityId Owner = EntityId.For(EntityKind.Business, 4);
        private static readonly EntityId Worker = EntityId.For(EntityKind.Person, 7);
        private static string ReservationLabel(WorkTask task) => $"task:{task.TaskId}|{task.DefinitionId}";

        private static (TaskAuthority Authority, WorkTimeBudgetStore Budgets, WorkTask Task) StartTask(
            string definitionId, int plannedMinutes)
        {
            var authority = new TaskAuthority();
            Assert.IsTrue(authority.RegisterDefinition(new TaskDefinition(definitionId, definitionId, plannedMinutes), out string reason), reason);
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(1);
            WorkTask task = authority.CreateTaskWithPlannedMinutes(definitionId, Owner, 1, plannedMinutes);
            Assert.IsTrue(authority.AssignTask(task.TaskId, Worker, budgets, out reason), reason);
            Assert.IsTrue(authority.StartTask(task.TaskId, 1, out reason), reason);
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

        [Test]
        public void ReservedTravelMinutesAdvanceRouteTimeTaskAndReleaseWorkerOnCompletion()
        {
            var started = StartTask(JourneyTravel.TravelTaskId, 3);
            var reservations = new List<PersonActivityReservation>
            {
                Reservation(PersonActivityKind.Travel, 480, 3, started.Task),
            };

            foreach (var tick in new[] { (480, 481), (481, 483) })
            {
                int minutes = AutonomousTaskProgress.ScheduledMinutes(
                    started.Task, reservations, 1, tick.Item1, tick.Item2, ReservationLabel(started.Task));
                Assert.Greater(minutes, 0);
                Assert.IsTrue(started.Authority.RecordWorkProgress(started.Task.TaskId, minutes,
                    started.Budgets, 1, out bool reachedCompletion, out string reason), reason);
                if (reachedCompletion)
                    Assert.IsTrue(started.Authority.CompleteTask(started.Task.TaskId,
                        TaskOutcome.Success, 1, started.Budgets));
            }

            Assert.AreEqual(TaskStatus.Complete, started.Task.Status);
            Assert.AreEqual(3, started.Task.MinutesWorked);
            Assert.AreEqual(3, started.Budgets.GetOrCreate(Worker).WorkedMinutes);
        }

        [TestCase(PersonActivityKind.Sleeping)]
        [TestCase(PersonActivityKind.Resting)]
        [TestCase(PersonActivityKind.Work)]
        public void TravelTaskDoesNotAdvanceOutsideItsReservedTravelActivity(PersonActivityKind activity)
        {
            var started = StartTask(JourneyTravel.TravelTaskId, 4);
            int minutes = AutonomousTaskProgress.ScheduledMinutes(started.Task,
                new[] { Reservation(activity, 480, 4, started.Task) }, 1, 480, 484, ReservationLabel(started.Task));
            Assert.AreEqual(0, minutes);
            Assert.AreEqual(0, started.Task.MinutesWorked);
            Assert.AreEqual(TaskStatus.InProgress, started.Task.Status);
        }

        [Test]
        public void TravelTaskIgnoresWrongDayReleasedAndOtherPersonReservations()
        {
            var started = StartTask(JourneyTravel.TravelTaskId, 4);
            PersonActivityReservation wrongDay = Reservation(PersonActivityKind.Travel, 480, 4, started.Task, day: 2);
            PersonActivityReservation released = Reservation(PersonActivityKind.Travel, 480, 4, started.Task, released: true);
            PersonActivityReservation other = Reservation(PersonActivityKind.Travel, 480, 4, started.Task);
            other.PersonId = 12;
            int minutes = AutonomousTaskProgress.ScheduledMinutes(started.Task,
                new[] { wrongDay, released, other }, 1, 480, 484, ReservationLabel(started.Task));
            Assert.AreEqual(0, minutes);
        }

        [Test]
        public void TravelTaskWaitsWhenWorkerHasNoTravelWindowAndAdvancesWhenWindowOpensNextDay()
        {
            var started = StartTask(JourneyTravel.TravelTaskId, 2);
            Assert.AreEqual(0, AutonomousTaskProgress.ScheduledMinutes(started.Task,
                new[] { Reservation(PersonActivityKind.Travel, 700, 2, started.Task, day: 2) },
                1, 700, 702, ReservationLabel(started.Task)), "no day-1 reservation means no invented travel time");
            Assert.AreEqual(2, AutonomousTaskProgress.ScheduledMinutes(started.Task,
                new[] { Reservation(PersonActivityKind.Travel, 700, 2, started.Task, day: 2) },
                2, 700, 702, ReservationLabel(started.Task)), "the task can progress when its reserved window becomes available");
        }

        [TestCase("freight.contact")]
        [TestCase("domestic.meal")]
        public void FollowingTaskCanStartOnlyAfterTravelTaskReallyCompletes(string followingDefinition)
        {
            var authority = new TaskAuthority();
            Assert.IsTrue(authority.RegisterDefinition(new TaskDefinition(JourneyTravel.TravelTaskId, "Travel", 2), out string reason), reason);
            Assert.IsTrue(authority.RegisterDefinition(new TaskDefinition(followingDefinition, followingDefinition, 1), out reason), reason);
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(1);
            WorkTask travel = authority.CreateTaskWithPlannedMinutes(JourneyTravel.TravelTaskId, Owner, 1, 2);
            WorkTask followUp = authority.CreateTask(followingDefinition, Owner, 1);
            Assert.IsTrue(authority.AssignTask(travel.TaskId, Worker, budgets, out reason), reason);
            Assert.IsTrue(authority.AssignTask(followUp.TaskId, Worker, budgets, out reason), reason);
            Assert.IsTrue(authority.StartTask(travel.TaskId, 1, out reason), reason);
            Assert.IsFalse(authority.StartTask(followUp.TaskId, 1, out reason), "one person cannot do travel and the next activity concurrently");

            int minutes = AutonomousTaskProgress.ScheduledMinutes(travel,
                new[] { Reservation(PersonActivityKind.Travel, 900, 2, travel) },
                1, 900, 902, ReservationLabel(travel));
            Assert.AreEqual(2, minutes);
            Assert.IsTrue(authority.RecordWorkProgress(travel.TaskId, minutes, budgets, 1,
                out bool reached, out reason), reason);
            Assert.IsTrue(reached);
            Assert.IsTrue(authority.CompleteTask(travel.TaskId, TaskOutcome.Success, 1, budgets));
            Assert.IsTrue(authority.StartTask(followUp.TaskId, 1, out reason), reason);
        }

        [Test]
        public void LargeTickCountsOnlyReservedMinutesAndNeverOverworksPlan()
        {
            var started = StartTask(JourneyTravel.TravelTaskId, 5);
            var reservations = new[] { Reservation(PersonActivityKind.Travel, 485, 3, started.Task) };
            int minutes = AutonomousTaskProgress.ScheduledMinutes(started.Task,
                reservations, 1, 480, 600, ReservationLabel(started.Task));
            Assert.AreEqual(3, minutes);
            Assert.IsTrue(started.Authority.RecordWorkProgress(started.Task.TaskId, minutes,
                started.Budgets, 1, out bool reachedCompletion, out string reason), reason);
            Assert.IsFalse(reachedCompletion);
            Assert.AreEqual(2, AutonomousTaskProgress.ScheduledMinutes(started.Task,
                reservations, 1, 480, 600, ReservationLabel(started.Task)));
        }

        [Test]
        public void SaveLoadPreservesInProgressTravelAndSmallTicksCompleteItOnce()
        {
            var started = StartTask(JourneyTravel.TravelTaskId, 2);
            TaskAuthority restored = new TaskAuthority();
            restored.LoadFromSaveDto(started.Authority.CaptureSaveDto());
            WorkTask restoredTask = restored.FindTask(started.Task.TaskId);
            Assert.AreEqual(TaskStatus.InProgress, restoredTask.Status);

            var schedules = new PersonScheduleTracker();
            Assert.IsNull(schedules.TryReserve(Worker.Id, PersonActivityKind.Travel, 1,
                700, 2, ReservationLabel(restoredTask)));
            PersonScheduleTracker restoredSchedules = new PersonScheduleTracker();
            restoredSchedules.LoadFromSaveDto(schedules.CaptureSaveDto());

            int minutes = AutonomousTaskProgress.ScheduledMinutes(restoredTask,
                restoredSchedules.ActiveFor(Worker.Id, 1),
                1, 700, 702, ReservationLabel(restoredTask));
            Assert.AreEqual(2, minutes);
            Assert.IsTrue(restored.RecordWorkProgress(restoredTask.TaskId, minutes,
                started.Budgets, 1, out bool reached, out string reason), reason);
            Assert.IsTrue(reached);
            Assert.IsTrue(restored.CompleteTask(restoredTask.TaskId, TaskOutcome.Success,
                1, started.Budgets));
            Assert.IsFalse(restored.CompleteTask(restoredTask.TaskId, TaskOutcome.Success,
                1, started.Budgets), "a repeated completion must not settle the task twice");
            Assert.AreEqual(2, restoredTask.MinutesWorked);
        }

        [Test]
        public void OrdinaryWorkTasksKeepWorkAndShoppingActivityProgress()
        {
            var started = StartTask("test.sweep", 5);
            Assert.AreEqual(3, AutonomousTaskProgress.ScheduledMinutes(started.Task,
                new[] { Reservation(PersonActivityKind.Work, 10, 3, started.Task) }, 1, 10, 15, ReservationLabel(started.Task)));
            Assert.AreEqual(2, AutonomousTaskProgress.ScheduledMinutes(started.Task,
                new[] { Reservation(PersonActivityKind.Shopping, 10, 3, started.Task) }, 1, 11, 15, ReservationLabel(started.Task)));
        }
    }
}
