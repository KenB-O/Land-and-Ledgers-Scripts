using LandLedgers.Orchestration.Player;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Orchestration
{
    /// <summary>
    /// TTS-4: the player is a Person who works through the shared task authority.
    /// The director issues intents, books the same work-time budget as NPCs, and
    /// notifies the avatar (Unity side). Headless mode (null avatar) is supported.
    /// </summary>
    [TestFixture]
    public sealed class PlayerDirectorTests
    {
        private sealed class TestAvatar : IPlayerAvatar
        {
            public string CurrentLocationId { get; set; }
            public bool IsBusy { get; private set; }
            public string LastMoveDestination { get; private set; }
            public EntityId LastTaskId { get; private set; }
            public int CancelCount { get; private set; }

            public void ExecuteMove(string destinationLocationId, int travelMinutes)
            {
                LastMoveDestination = destinationLocationId;
                IsBusy = true;
            }

            public void ExecuteTask(EntityId taskId)
            {
                LastTaskId = taskId;
                IsBusy = true;
            }

            public void ExecuteCancel()
            {
                CancelCount++;
                IsBusy = false;
            }
        }

        private static EntityId Player(int id)
        {
            return EntityId.For(EntityKind.Person, id);
        }

        [Test]
        public void MoveIntent_BooksBudget_SetsTravelling_NotifiesAvatar()
        {
            var director = new PlayerDirector(Player(1), "homestead");
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(1);
            var avatar = new TestAvatar();

            string reason;
            Assert.IsTrue(director.TryIssueMoveIntent("general-store", 25, budgets, avatar, out reason), reason);

            Assert.IsTrue(director.IsTravelling);
            Assert.IsTrue(director.IsBusy);
            Assert.AreEqual("general-store", avatar.LastMoveDestination);
            Assert.AreEqual(600 - 25, budgets.GetOrCreate(Player(1)).MinutesRemaining);
        }

        [Test]
        public void MovementProgress_Completes_UpdatesLocation()
        {
            var director = new PlayerDirector(Player(2), "homestead");
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(1);

            string reason;
            Assert.IsTrue(director.TryIssueMoveIntent("mill", 10, budgets, null, out reason), reason);

            director.RecordMovementProgress(4, budgets);
            Assert.IsTrue(director.IsTravelling);
            Assert.AreEqual("homestead", director.CurrentLocationId);

            director.RecordMovementProgress(6, budgets);
            Assert.IsFalse(director.IsTravelling);
            Assert.AreEqual("mill", director.CurrentLocationId);
            Assert.AreEqual(10, budgets.GetOrCreate(Player(2)).MinutesWorked);
        }

        [Test]
        public void TaskIntent_AssignsThroughAuthority_BooksBudget()
        {
            var director = new PlayerDirector(Player(3), "general-store");
            var authority = new TaskAuthority();
            string reason;
            Assert.IsTrue(authority.RegisterDefinition(new TaskDefinition("test.sweep", "Sweep", 10), out reason));
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(1);
            var avatar = new TestAvatar();

            WorkTask task = authority.CreateTask("test.sweep", EntityId.For(EntityKind.Business, 1), 1);
            Assert.IsTrue(director.TryIssueTaskIntent(task.TaskId, authority, budgets, 1, avatar, out reason), reason);

            Assert.AreEqual(task.TaskId, director.CurrentTaskId);
            Assert.AreEqual(TaskStatus.InProgress, task.Status);
            Assert.AreEqual(Player(3), task.AssigneeId);
            Assert.AreEqual(task.TaskId, avatar.LastTaskId);
            Assert.AreEqual(590, director.BudgetMinutesRemaining(budgets));
        }

        [Test]
        public void BusyPlayer_RejectsNewIntent_WithReason()
        {
            var director = new PlayerDirector(Player(4), "homestead");
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(1);

            string reason;
            Assert.IsTrue(director.TryIssueMoveIntent("mill", 10, budgets, null, out reason), reason);
            Assert.IsFalse(director.TryIssueMoveIntent("general-store", 5, budgets, null, out reason));
            Assert.IsNotNull(reason);
            StringAssert.Contains("busy", reason.ToLower());
        }

        [Test]
        public void CancelCurrent_InterruptsTask_ReleasesBudget_NotifiesAvatar()
        {
            var director = new PlayerDirector(Player(5), "general-store");
            var authority = new TaskAuthority();
            string reason;
            Assert.IsTrue(authority.RegisterDefinition(new TaskDefinition("test.sweep", "Sweep", 10), out reason));
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(1);
            var avatar = new TestAvatar();

            WorkTask task = authority.CreateTask("test.sweep", EntityId.For(EntityKind.Business, 1), 1);
            Assert.IsTrue(director.TryIssueTaskIntent(task.TaskId, authority, budgets, 1, avatar, out reason), reason);
            Assert.IsTrue(authority.RecordWork(task.TaskId, 4, budgets, 1, out reason), reason);

            Assert.IsTrue(director.CancelCurrent(authority, budgets, avatar));
            Assert.AreEqual(TaskStatus.Queued, task.Status); // interrupted back to queue
            Assert.AreEqual(1, avatar.CancelCount);
            Assert.IsFalse(director.IsBusy);
            // 4 worked minutes stay spent; 6 unworked return.
            Assert.AreEqual(596, director.BudgetMinutesRemaining(budgets));
        }

        [Test]
        public void CancelCurrent_WhenIdle_DoesNothing()
        {
            var director = new PlayerDirector(Player(6), "homestead");
            Assert.IsFalse(director.CancelCurrent(null, null, null));
        }
    }
}
