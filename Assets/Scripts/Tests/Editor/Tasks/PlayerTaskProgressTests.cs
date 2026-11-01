using System.Collections.Generic;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Tasks
{
    /// <summary>
    /// Pass IV (Worker 1): player-issued tasks advance in real time through the
    /// PlayerTaskProgressDriver instead of stalling in InProgress.
    /// </summary>
    [TestFixture]
    public sealed class PlayerTaskProgressTests
    {
        private static EntityId Person(int id)
        {
            return EntityId.For(EntityKind.Person, id);
        }

        private static EntityId Business(int id)
        {
            return EntityId.For(EntityKind.Business, id);
        }

        private TaskAuthority CreateAuthority()
        {
            var authority = new TaskAuthority();
            string reason;
            Assert.IsTrue(authority.RegisterDefinition(new TaskDefinition("test.chop", "Chop wood", 60), out reason), reason);
            return authority;
        }

        private WorkTask StartPlayerTask(TaskAuthority authority, WorkTimeBudgetStore budgets, EntityId player)
        {
            string reason;
            budgets.EnsureDay(1);
            WorkTask task = authority.CreateTask("test.chop", Business(1), 1);
            Assert.IsTrue(authority.AssignTask(task.TaskId, player, budgets, out reason), reason);
            Assert.IsTrue(authority.StartTask(task.TaskId, 1, out reason), reason);
            return task;
        }

        [Test]
        public void AdvancePlayerTasks_RecordsProgressInRealTime()
        {
            var authority = CreateAuthority();
            var budgets = new WorkTimeBudgetStore();
            var driver = new PlayerTaskProgressDriver(authority, budgets);
            EntityId player = Person(7);

            WorkTask task = StartPlayerTask(authority, budgets, player);
            var diag = new List<string>();

            int applied = driver.AdvancePlayerTasks(player, 30, 1, null, diag);
            Assert.AreEqual(30, applied);
            Assert.AreEqual(30, task.MinutesWorked);
            Assert.AreEqual(TaskStatus.InProgress, task.Status);
            Assert.AreEqual(30, budgets.GetOrCreate(player).MinutesWorked);
        }

        [Test]
        public void AdvancePlayerTasks_ReachesPlan_CompletesWithResolverOutcome()
        {
            var authority = CreateAuthority();
            var budgets = new WorkTimeBudgetStore();
            var driver = new PlayerTaskProgressDriver(authority, budgets);
            EntityId player = Person(7);

            WorkTask task = StartPlayerTask(authority, budgets, player);
            var diag = new List<string>();

            driver.AdvancePlayerTasks(player, 30, 1, (t, d) => TaskOutcome.Success, diag);
            int applied = driver.AdvancePlayerTasks(player, 30, 1, (t, d) => TaskOutcome.AcceptableWithVariation, diag);

            Assert.AreEqual(30, applied);
            Assert.AreEqual(TaskStatus.Complete, task.Status);
            Assert.AreEqual(TaskOutcome.AcceptableWithVariation, task.Outcome);
        }

        [Test]
        public void AdvancePlayerTasks_NoResolver_RestsAtPlanWithDiagnostic()
        {
            var authority = CreateAuthority();
            var budgets = new WorkTimeBudgetStore();
            var driver = new PlayerTaskProgressDriver(authority, budgets);
            EntityId player = Person(7);

            WorkTask task = StartPlayerTask(authority, budgets, player);
            var diag = new List<string>();

            driver.AdvancePlayerTasks(player, 60, 1, null, diag);

            Assert.AreEqual(TaskStatus.InProgress, task.Status);
            Assert.AreEqual(0, task.MinutesRemaining);
            Assert.AreEqual(1, diag.Count, "the stall must be loud, not silent");
        }

        [Test]
        public void AdvancePlayerTasks_IgnoresOtherWorkersTasks()
        {
            var authority = CreateAuthority();
            var budgets = new WorkTimeBudgetStore();
            var driver = new PlayerTaskProgressDriver(authority, budgets);
            EntityId player = Person(7);
            EntityId npc = Person(8);

            WorkTask npcTask = StartPlayerTask(authority, budgets, npc);
            var diag = new List<string>();

            int applied = driver.AdvancePlayerTasks(player, 30, 1, null, diag);

            Assert.AreEqual(0, applied);
            Assert.AreEqual(0, npcTask.MinutesWorked);
        }

        [Test]
        public void AdvancePlayerTasks_InvalidPlayerOrZeroMinutes_NoOp()
        {
            var authority = CreateAuthority();
            var budgets = new WorkTimeBudgetStore();
            var driver = new PlayerTaskProgressDriver(authority, budgets);
            var diag = new List<string>();

            Assert.AreEqual(0, driver.AdvancePlayerTasks(default(EntityId), 30, 1, null, diag));
            Assert.AreEqual(0, driver.AdvancePlayerTasks(Person(7), 0, 1, null, diag));
            Assert.AreEqual(0, diag.Count);
        }
    }
}
