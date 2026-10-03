using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Core
{
    /// <summary>
    /// TTS-2: every action is a task. Lifecycle queued → assigned → in-progress →
    /// complete/interrupted; per-owner priority queues; overload waits instead of
    /// inventing workers; assignment books TTS-1 work-time budget.
    /// </summary>
    [TestFixture]
    public sealed class TaskAuthorityTests
    {
        private static EntityId Person(int id)
        {
            return EntityId.For(EntityKind.Person, id);
        }

        private static EntityId Business(int id)
        {
            return EntityId.For(EntityKind.Business, id);
        }

        private TaskAuthority CreateAuthorityWithSweeping()
        {
            var authority = new TaskAuthority();
            string reason;
            var def = new TaskDefinition("test.sweep", "Sweep floor", 10);
            Assert.IsTrue(authority.RegisterDefinition(def, out reason), reason);
            return authority;
        }

        [Test]
        public void RegisterDefinition_DuplicateId_Rejected()
        {
            var authority = new TaskAuthority();
            string reason;
            Assert.IsTrue(authority.RegisterDefinition(new TaskDefinition("dup", "A", 5), out reason));
            Assert.IsFalse(authority.RegisterDefinition(new TaskDefinition("dup", "B", 5), out reason));
            Assert.IsNotNull(reason);
        }

        [Test]
        public void Lifecycle_CreateAssignStartWork_CompletesAtPlan()
        {
            var authority = CreateAuthorityWithSweeping();
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(1);

            WorkTask task = authority.CreateTask("test.sweep", Business(1), 1);
            Assert.AreEqual(TaskStatus.Queued, task.Status);

            string reason;
            Assert.IsTrue(authority.AssignTask(task.TaskId, Person(1), budgets, out reason), reason);
            Assert.AreEqual(TaskStatus.Assigned, task.Status);
            Assert.AreEqual(590, budgets.GetOrCreate(Person(1)).MinutesRemaining); // 600 - 10 planned

            Assert.IsTrue(authority.StartTask(task.TaskId, 1, out reason), reason);
            Assert.AreEqual(TaskStatus.InProgress, task.Status);

            Assert.IsTrue(authority.RecordWork(task.TaskId, 10, budgets, 1, out reason), reason);
            Assert.AreEqual(TaskStatus.Complete, task.Status);
            Assert.AreEqual(TaskOutcome.Success, task.Outcome);
        }

        [Test]
        public void StartTask_UnassignedTask_Rejected()
        {
            var authority = CreateAuthorityWithSweeping();

            string reason;
            WorkTask task = authority.CreateTask("test.sweep", Business(1), 1);
            Assert.IsFalse(authority.StartTask(task.TaskId, 1, out reason));
            Assert.IsNotNull(reason);
        }

        [Test]
        public void AssignTask_BudgetExhausted_BlocksWithReason_TaskStaysQueued()
        {
            var authority = CreateAuthorityWithSweeping();
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(1);

            string reason;
            Assert.IsTrue(budgets.GetOrCreate(Person(2), 5).TryCommit(5, out reason)); // exhaust 5-min budget

            WorkTask task = authority.CreateTask("test.sweep", Business(1), 1);
            Assert.IsFalse(authority.AssignTask(task.TaskId, Person(2), budgets, out reason));
            Assert.IsNotNull(reason);
            Assert.AreEqual(TaskStatus.Queued, task.Status); // overload waits; no worker invented
        }

        [Test]
        public void NextQueuedTask_PriorityThenFifo()
        {
            var authority = new TaskAuthority();
            string reason;
            var low = new TaskDefinition("t.low", "Low", 5);
            var urgent = new TaskDefinition("t.urgent", "Urgent", 5);
            urgent.SetDefaultPriority(TaskPriority.Urgent);
            Assert.IsTrue(authority.RegisterDefinition(low, out reason));
            Assert.IsTrue(authority.RegisterDefinition(urgent, out reason));

            WorkTask first = authority.CreateTask("t.low", Business(1), 1);
            WorkTask second = authority.CreateTask("t.low", Business(1), 1);
            WorkTask third = authority.CreateTask("t.urgent", Business(1), 1);

            // Urgent outranks both normal tasks despite being created last.
            WorkTask next = authority.NextQueuedTask(Business(1));
            Assert.AreEqual(third.TaskId, next.TaskId);

            // Owner isolation: another owner's queue is unaffected.
            Assert.IsNull(authority.NextQueuedTask(Business(2)));
        }

        [Test]
        public void NextQueuedTask_EqualPriority_FifoOrder()
        {
            var authority = CreateAuthorityWithSweeping();

            WorkTask first = authority.CreateTask("test.sweep", Business(3), 1);
            WorkTask second = authority.CreateTask("test.sweep", Business(3), 1);

            Assert.AreEqual(first.TaskId, authority.NextQueuedTask(Business(3)).TaskId);
            Assert.AreNotEqual(second.TaskId, authority.NextQueuedTask(Business(3)).TaskId);
        }
        }

        [Test]
        public void InterruptTask_ReleasesCommitment_AndRequeues()
        {
            var authority = CreateAuthorityWithSweeping();
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(1);

            string reason;
            WorkTask task = authority.CreateTask("test.sweep", Business(1), 1);
            Assert.IsTrue(authority.AssignTask(task.TaskId, Person(3), budgets, out reason), reason);
            Assert.IsTrue(authority.StartTask(task.TaskId, 1, out reason), reason);
            Assert.IsTrue(authority.RecordWork(task.TaskId, 4, budgets, 1, out reason), reason);

            Assert.IsTrue(authority.InterruptTask(task.TaskId, budgets, out reason), reason);
            Assert.AreEqual(TaskStatus.Queued, task.Status);
            Assert.AreEqual(1, task.InterruptionCount);
            Assert.AreEqual(4, task.MinutesWorked); // work done so far is preserved
            // 4 worked minutes stay spent; the 6 unworked minutes return to the budget.
            Assert.AreEqual(4, budgets.GetOrCreate(Person(3)).MinutesCommitted);
            Assert.AreEqual(596, budgets.GetOrCreate(Person(3)).MinutesRemaining);
        }

        [Test]
        public void Prerequisites_BlockAssignmentUntilComplete()
        {
            var authority = CreateAuthorityWithSweeping();
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(1);

            WorkTask first = authority.CreateTask("test.sweep", Business(1), 1);
            WorkTask second = authority.CreateTask("test.sweep", Business(1), 1);
            second.PrerequisiteTaskIds.Add(first.TaskId);

            string reason;
            Assert.IsFalse(authority.AssignTask(second.TaskId, Person(4), budgets, out reason));
            StringAssert.Contains("prerequisite", reason.ToLower());

            Assert.IsTrue(authority.AssignTask(first.TaskId, Person(4), budgets, out reason), reason);
            Assert.IsTrue(authority.StartTask(first.TaskId, 1, out reason), reason);
            Assert.IsTrue(authority.RecordWork(first.TaskId, 10, budgets, 1, out reason), reason);
            Assert.AreEqual(TaskStatus.Complete, first.Status);

            Assert.IsTrue(authority.AssignTask(second.TaskId, Person(4), budgets, out reason), reason);
        }
    }
}
