using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Core
{
    /// <summary>
    /// Pass VIII Worker 7: a dead person is never offered work — beyond
    /// recruitment (Pass VI), at task assignment, at task start, and at
    /// off-map departure (migration). The guards sit in the production
    /// authorities as optional seams, so every caller is covered.
    /// - AssignTask refuses a worker the seam marks unavailable.
    /// - StartTask refuses when the assignee became unavailable after
    ///   assignment (death between assignment and start).
    /// - OffMapJourneyAuthority.DepartPerson refuses an unavailable traveler.
    /// - Living workers are unaffected (no behavior change when the seam
    ///   is null, and no false refusals when it is installed).
    /// </summary>
    [TestFixture]
    public sealed class Pass8DeadWorkerGuardTests
    {
        private static EntityId Person(int id) => EntityId.For(EntityKind.Person, id);
        private static EntityId Business(int id) => EntityId.For(EntityKind.Business, id);

        private sealed class Fixture
        {
            public TaskAuthority Tasks = new TaskAuthority();
            public WorkTimeBudgetStore Budgets = new WorkTimeBudgetStore();
            public HashSet<int> Dead = new HashSet<int>();

            public Fixture()
            {
                string reason;
                Assert.IsTrue(Tasks.RegisterDefinition(new TaskDefinition("test.work", "Test work", 60), out reason), reason);
                Budgets.EnsureDay(1);
                // The production wiring marks the deceased unavailable.
                Tasks.IsWorkerUnavailable = worker =>
                    worker.Kind == EntityKind.Person && Dead.Contains(worker.Id);
            }

            public WorkTask NewTask()
            {
                return Tasks.CreateTask("test.work", Business(1), 1);
            }
        }

        private static JourneyModel JourneyModelWithGateway()
        {
            var model = new JourneyModel();
            model.RegisterLocation(new JourneyLocation("mill-site", JourneyLocationKind.Mill, "Grist Mill", 0.5f, 3f));
            model.RegisterLocation(new JourneyLocation("town-depot", JourneyLocationKind.OffMapGateway, "Rail Depot", 0f, 0f));
            model.AddEdge("mill-site", "town-depot", 3f, "depot road");
            return model;
        }

        [Test]
        public void AssignTask_RefusesADeadWorker()
        {
            var f = new Fixture();
            f.Dead.Add(7);
            WorkTask task = f.NewTask();

            string reason;
            Assert.IsFalse(f.Tasks.AssignTask(task.TaskId, Person(7), f.Budgets, out reason));
            Assert.IsNotNull(reason);
            StringAssert.Contains("unavailable", reason);
            Assert.AreEqual(TaskStatus.Queued, task.Status, "a refused assignment leaves the task queued");
            Assert.AreEqual(600, f.Budgets.GetOrCreate(Person(7)).MinutesRemaining,
                "no budget was booked for the dead");
        }

        [Test]
        public void StartTask_RefusesAWorkerWhoDiedAfterAssignment()
        {
            var f = new Fixture();
            WorkTask task = f.NewTask();
            string reason;
            Assert.IsTrue(f.Tasks.AssignTask(task.TaskId, Person(7), f.Budgets, out reason), reason);

            // Death between assignment and start.
            f.Dead.Add(7);
            Assert.IsFalse(f.Tasks.StartTask(task.TaskId, 1, out reason));
            Assert.IsNotNull(reason);
            StringAssert.Contains("unavailable", reason);
            Assert.AreEqual(TaskStatus.Assigned, task.Status);
        }

        [Test]
        public void LivingWorkers_AssignAndStartNormally()
        {
            var f = new Fixture();
            f.Dead.Add(7); // someone else died; P3 is alive
            WorkTask task = f.NewTask();
            string reason;
            Assert.IsTrue(f.Tasks.AssignTask(task.TaskId, Person(3), f.Budgets, out reason), reason);
            Assert.IsTrue(f.Tasks.StartTask(task.TaskId, 1, out reason), reason);
            Assert.IsTrue(f.Tasks.RecordWork(task.TaskId, 60, f.Budgets, 1, out reason), reason);
            Assert.AreEqual(TaskStatus.Complete, task.Status);
        }

        [Test]
        public void NoSeam_NoBehaviorChange()
        {
            var tasks = new TaskAuthority();
            string reason;
            Assert.IsTrue(tasks.RegisterDefinition(new TaskDefinition("test.work", "Test work", 60), out reason), reason);
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(1);
            WorkTask task = tasks.CreateTask("test.work", Business(1), 1);
            Assert.IsTrue(tasks.AssignTask(task.TaskId, Person(7), budgets, out reason), reason);
        }

        [Test]
        public void DepartPerson_RefusesADeadTraveler()
        {
            var journeys = new OffMapJourneyAuthority(JourneyModelWithGateway());
            journeys.IsPersonUnavailable = personId => personId == 7;
            var diag = new List<string>();

            OffMapJourneyRecord record = journeys.DepartPerson(
                7, "mill-site", "distant markets", 2, 10,
                "test trip", null, null, 1, item => 0, null, diag);
            Assert.IsNull(record, "a dead person is never sent off-map");
            Assert.AreEqual(0, journeys.ActiveJourneyCount);
            Assert.IsTrue(string.Join(" ", diag.ToArray()).Contains("unavailable"));
        }

        [Test]
        public void DepartPerson_LivingTravelerDepartsNormally()
        {
            var journeys = new OffMapJourneyAuthority(JourneyModelWithGateway());
            journeys.IsPersonUnavailable = personId => personId == 7;
            var diag = new List<string>();

            OffMapJourneyRecord record = journeys.DepartPerson(
                3, "mill-site", "distant markets", 2, 10,
                "test trip", null, null, 1, item => 0, null, diag);
            Assert.IsNotNull(record, "living travelers depart: " + string.Join("; ", diag));
            Assert.AreEqual(1, journeys.ActiveJourneyCount);
        }
    }
}
