using System.Collections.Generic;
using LandLedgers.Economy.Equipment;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Tasks
{
    /// <summary>
    /// Pass IV (Worker 1): TaskAuthority enforces the lifecycle policy on the new
    /// states, wires the reservation/due-event seams, and refuses illegal
    /// transitions loudly.
    /// </summary>
    [TestFixture]
    public sealed class TaskAuthorityLifecycleTests
    {
        private sealed class FakeReservationAuthority : IUnifiedReservationAuthority
        {
            public readonly Dictionary<string, ReservationHandle> live =
                new Dictionary<string, ReservationHandle>();

            public readonly List<string> releasedForTask = new List<string>();
            public string refuseReason;
            private int nextId = 1;

            public string TryReserveAll(ReservationRequest request, out ReservationHandle handle)
            {
                handle = null;
                if (refuseReason != null)
                {
                    return refuseReason;
                }

                string id = "resv-" + (nextId++);
                handle = new ReservationHandle(id, request.OwnerTaskId, new List<ReservationItem>(request.Items));
                live[id] = handle;
                return null;
            }

            public void Release(string reservationId)
            {
                live.Remove(reservationId);
            }

            public void ReleaseAllForTask(string ownerTaskId)
            {
                releasedForTask.Add(ownerTaskId);
                var ids = new List<string>();
                foreach (var entry in live)
                {
                    if (entry.Value.OwnerTaskId == ownerTaskId)
                    {
                        ids.Add(entry.Key);
                    }
                }

                foreach (string id in ids)
                {
                    live.Remove(id);
                }
            }

            public void ReleaseAllForResource(string resourceKey)
            {
                var ids = new List<string>();
                foreach (var entry in live)
                {
                    foreach (var item in entry.Value.Items)
                    {
                        if (item.ResourceKey == resourceKey) { ids.Add(entry.Key); break; }
                    }
                }

                foreach (string id in ids)
                {
                    live.Remove(id);
                }
            }

            public bool IsReserved(string resourceKey, float atGameSeconds)
            {
                return false;
            }

            public float AvailableCapacity(string resourceKey, float atGameSeconds)
            {
                return 1f;
            }

            public List<ReservationHandle> ReservationsForTask(string ownerTaskId)
            {
                var result = new List<ReservationHandle>();
                foreach (var entry in live)
                {
                    if (entry.Value.OwnerTaskId == ownerTaskId)
                    {
                        result.Add(entry.Value);
                    }
                }

                return result;
            }

            public int LiveReservationCount => live.Count;
        }

        private static EntityId Person(int id)
        {
            return EntityId.For(EntityKind.Person, id);
        }

        private static EntityId Business(int id)
        {
            return EntityId.For(EntityKind.Business, id);
        }

        private TaskAuthority CreateAuthority(string definitionId = "test.sweep", int baseMinutes = 10)
        {
            var authority = new TaskAuthority();
            string reason;
            Assert.IsTrue(authority.RegisterDefinition(new TaskDefinition(definitionId, "Sweep", baseMinutes), out reason), reason);
            return authority;
        }

        private WorkTask CreateAssignedStarted(TaskAuthority authority, WorkTimeBudgetStore budgets, string definitionId = "test.sweep")
        {
            string reason;
            budgets.EnsureDay(1);
            WorkTask task = authority.CreateTask(definitionId, Business(1), 1);
            Assert.IsTrue(authority.AssignTask(task.TaskId, Person(1), budgets, out reason), reason);
            Assert.IsTrue(authority.StartTask(task.TaskId, 1, out reason), reason);
            Assert.AreEqual(TaskStatus.InProgress, task.Status);
            return task;
        }

        [Test]
        public void DailyLifecycleSnapshot_ReportsAuthoritativeOpenAndCompletedStates()
        {
            TaskAuthority authority = CreateAuthority();
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(1);
            WorkTask completed = CreateAssignedStarted(authority, budgets);
            WorkTask queued = authority.CreateTask("test.sweep", Business(2), 1);

            Assert.IsTrue(authority.CompleteTask(completed.TaskId, TaskOutcome.Success, 1, budgets));

            DailyTaskLifecycleSnapshot dayOne = authority.CaptureDailyLifecycleSnapshot(1);
            Assert.AreEqual(1, dayOne.Queued);
            Assert.AreEqual(1, dayOne.CompletedOnDay);
            Assert.AreEqual(0, dayOne.FailedOnDay);
            Assert.AreEqual(TaskStatus.Queued, queued.Status);

            DailyTaskLifecycleSnapshot dayTwo = authority.CaptureDailyLifecycleSnapshot(2);
            Assert.AreEqual(0, dayTwo.CompletedOnDay, "Completion is attributed to the authoritative day index.");
            Assert.AreEqual(1, dayTwo.Queued);
        }

        [Test]
        public void DailyLifecycleSnapshot_ReportsRetainedCancellationSeparatelyFromDatedCompletion()
        {
            TaskAuthority authority = CreateAuthority();
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(1);
            WorkTask cancelled = CreateAssignedStarted(authority, budgets);
            WorkTask completed = authority.CreateTask("test.sweep", Business(2), 1);
            Assert.IsTrue(authority.AssignTask(completed.TaskId, Person(2), budgets, out string reason), reason);
            Assert.IsTrue(authority.StartTask(completed.TaskId, 1, out reason), reason);

            Assert.IsTrue(authority.CancelTask(cancelled.TaskId, budgets));
            Assert.IsTrue(authority.CompleteTask(completed.TaskId, TaskOutcome.Success, 1, budgets));

            DailyTaskLifecycleSnapshot snapshot = authority.CaptureDailyLifecycleSnapshot(1);
            Assert.AreEqual(1, snapshot.CancelledRetained);
            Assert.AreEqual(1, snapshot.CompletedOnDay);
        }

        [Test]
        public void StartTask_RefusesSecondExclusiveTaskForPersonUntilCurrentTaskCompletes()
        {
            TaskAuthority authority = CreateAuthority();
            WorkTimeBudgetStore budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(1);
            WorkTask first = authority.CreateTask("test.sweep", Business(1), 1);
            WorkTask second = authority.CreateTask("test.sweep", Business(2), 1);
            Assert.IsTrue(authority.AssignTask(first.TaskId, Person(7), budgets, out string reason), reason);
            Assert.IsTrue(authority.AssignTask(second.TaskId, Person(7), budgets, out reason), reason);

            Assert.IsTrue(authority.StartTask(first.TaskId, 1, out reason), reason);
            Assert.IsFalse(authority.StartTask(second.TaskId, 1, out reason));
            StringAssert.Contains(first.TaskId.ToString(), reason);
            Assert.AreEqual(TaskStatus.Assigned, second.Status);

            Assert.IsTrue(authority.CompleteTask(first.TaskId, TaskOutcome.Success, 1, budgets));
            Assert.IsTrue(authority.StartTask(second.TaskId, 1, out reason), reason);
        }

        [TestCase(TaskStatus.Paused)]
        [TestCase(TaskStatus.WaitingOnDependency)]
        [TestCase(TaskStatus.ResourcesReserved)]
        public void StartTask_RefusesConcurrentWorkWhileFirstTaskStillOwnsWorker(TaskStatus blockingState)
        {
            TaskAuthority authority = CreateAuthority();
            WorkTimeBudgetStore budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(1);
            WorkTask first = authority.CreateTask("test.sweep", Business(1), 1);
            WorkTask second = authority.CreateTask("test.sweep", Business(2), 1);
            Assert.IsTrue(authority.AssignTask(first.TaskId, Person(7), budgets, out string reason), reason);
            Assert.IsTrue(authority.AssignTask(second.TaskId, Person(7), budgets, out reason), reason);
            switch (blockingState)
            {
                case TaskStatus.Paused:
                    Assert.IsTrue(authority.StartTask(first.TaskId, 1, out reason), reason);
                    Assert.IsTrue(authority.PauseTask(first.TaskId, budgets, "temporary interruption", out reason), reason);
                    break;
                case TaskStatus.WaitingOnDependency:
                    Assert.IsTrue(authority.StartTask(first.TaskId, 1, out reason), reason);
                    Assert.IsTrue(authority.MarkWaitingOnDependency(first.TaskId, "required delivery", "waiting", out reason), reason);
                    break;
                case TaskStatus.ResourcesReserved:
                    // ResourcesReserved is reachable only from Assigned, so
                    // it is marked before any start. Starting first would
                    // make the transition illegal (InProgress ->
                    // ResourcesReserved) and the setup itself would fail.
                    Assert.IsTrue(authority.MarkResourcesReserved(first.TaskId, "fixture-reservation", "workstation held", out reason), reason);
                    break;
            }

            Assert.IsFalse(authority.StartTask(second.TaskId, 1, out reason));
            StringAssert.Contains(first.TaskId.ToString(), reason);
            Assert.AreEqual(TaskStatus.Assigned, second.Status, "The refused task remains assigned for its later turn.");

            if (blockingState == TaskStatus.Paused)
            {
                Assert.IsTrue(authority.InterruptTask(first.TaskId, budgets, out reason), reason);
            }
            else
            {
                Assert.IsTrue(authority.CancelTask(first.TaskId, budgets));
            }

            Assert.IsTrue(authority.StartTask(second.TaskId, 1, out reason),
                reason + " Ending the owning task should release the worker so the next legitimate task can start.");
        }

        [Test]
        public void AssignReassignAfterInterrupt_CommitsOnlyRemainingMinutes()
        {
            TaskAuthority authority = CreateAuthority(baseMinutes: 60);
            WorkTimeBudgetStore budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(1);
            string reason;

            WorkTask task = CreateAssignedStarted(authority, budgets);
            LandLedgers.Primitives.EntityId worker = Person(1);
            WorkTimeBudget budget = budgets.GetOrCreate(worker);
            int remainingBeforeWork = budget.MinutesRemaining;
            Assert.AreEqual(60, budget.MinutesCommitted, "first assignment commits the full 60 minutes");

            Assert.IsTrue(authority.RecordWork(task.TaskId, 30, budgets, 1, out reason), reason);
            Assert.AreEqual(30, task.MinutesRemaining, "30 of 60 minutes worked, 30 remain");
            Assert.AreEqual(30, budget.MinutesWorked, "worked minutes are recorded on the budget");

            Assert.IsTrue(authority.InterruptTask(task.TaskId, budgets, out reason), reason);
            Assert.AreEqual(TaskStatus.Queued, task.Status, "interruption re-queues the task");
            Assert.AreEqual(30, budget.MinutesCommitted, "interrupt releases only the 30 unworked minutes");

            Assert.IsTrue(authority.AssignTask(task.TaskId, worker, budgets, out reason), reason);
            Assert.AreEqual(60, budget.MinutesCommitted,
                "re-assignment commits only the 30 remaining minutes on top of the 30 already spent, not a second full 60");
            Assert.AreEqual(remainingBeforeWork, budget.MinutesRemaining,
                "interrupt plus re-assignment is budget-neutral: the day budget still reflects exactly 30 spent + 30 committed");
            Assert.AreEqual(30, task.MinutesRemaining, "the work requirement is unchanged by the fix");
        }

        [Test]
        public void InterruptReassignCycle_BudgetNeutralAtSweepScale()
        {
            TaskAuthority authority = CreateAuthority(baseMinutes: 60);
            WorkTimeBudgetStore budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(1);
            string reason;

            WorkTask task = CreateAssignedStarted(authority, budgets);
            LandLedgers.Primitives.EntityId worker = Person(1);
            WorkTimeBudget budget = budgets.GetOrCreate(worker);
            int remainingBeforeWork = budget.MinutesRemaining;
            Assert.AreEqual(60, budget.MinutesCommitted, "first assignment commits the full 60 minutes");

            Assert.IsTrue(authority.RecordWork(task.TaskId, 30, budgets, 1, out reason), reason);
            Assert.AreEqual(30, task.MinutesRemaining, "30 of 60 minutes worked, 30 remain");
            Assert.AreEqual(30, budget.MinutesWorked, "worked minutes are recorded on the budget");

            // Plan 05: the stale-InProgress sweep relies on the Slice 03
            // remainder commit, so pin it under sweep-scale repetition —
            // ten interrupt/re-assign cycles must never grow the commitment.
            for (int cycle = 0; cycle < 10; cycle++)
            {
                Assert.IsTrue(authority.InterruptTask(task.TaskId, budgets, out reason), reason);
                Assert.AreEqual(TaskStatus.Queued, task.Status, "cycle " + cycle + ": interruption re-queues the task");
                Assert.AreEqual(30, budget.MinutesCommitted,
                    "cycle " + cycle + ": interrupt releases only the 30 unworked minutes");

                Assert.IsTrue(authority.AssignTask(task.TaskId, worker, budgets, out reason), reason);
                Assert.AreEqual(60, budget.MinutesCommitted,
                    "cycle " + cycle + ": re-assignment commits only the 30 remaining minutes on top of the 30 already spent, not a second full 60");
                Assert.AreEqual(remainingBeforeWork, budget.MinutesRemaining,
                    "cycle " + cycle + ": interrupt plus re-assignment is budget-neutral");
                Assert.AreEqual(30, task.MinutesRemaining,
                    "cycle " + cycle + ": the work requirement is unchanged by the cycle");
            }
        }

        [Test]
        public void CancelAndComplete_AffectOnlyTheirTaskAndTerminalCompletionIsExactlyOnce()
        {
            TaskAuthority authority = CreateAuthority();
            WorkTimeBudgetStore budgets = new WorkTimeBudgetStore();
            FakeReservationAuthority reservations = new FakeReservationAuthority();
            authority.SetReservationAuthority(reservations);
            authority.SetReservationRequestBuilder((task, definition) =>
                new ReservationRequest(task.TaskId.ToString(), "lifecycle regression"));
            budgets.EnsureDay(1);

            WorkTask cancelled = authority.CreateTask("test.sweep", Business(1), 1);
            WorkTask unrelated = authority.CreateTask("test.sweep", Business(2), 1);
            Assert.IsTrue(authority.AssignTask(cancelled.TaskId, Person(1), budgets, out string reason), reason);
            Assert.IsTrue(authority.AssignTask(unrelated.TaskId, Person(2), budgets, out reason), reason);
            Assert.IsTrue(authority.StartTask(cancelled.TaskId, 1, out reason), reason);
            Assert.IsTrue(authority.StartTask(unrelated.TaskId, 1, out reason), reason);
            Assert.AreEqual(2, reservations.LiveReservationCount);

            Assert.IsTrue(authority.CancelTask(cancelled.TaskId, budgets));
            Assert.IsFalse(authority.CancelTask(cancelled.TaskId, budgets), "A terminal task cannot be cancelled twice.");
            Assert.AreEqual(TaskStatus.Cancelled, cancelled.Status);
            Assert.AreEqual(TaskStatus.InProgress, unrelated.Status, "Cancellation is scoped to the supplied task ID.");
            Assert.AreEqual(1, reservations.LiveReservationCount, "Only the cancelled task's reservation is released.");

            Assert.IsTrue(authority.CompleteTask(unrelated.TaskId, TaskOutcome.Success, 1, budgets));
            Assert.IsFalse(authority.CompleteTask(unrelated.TaskId, TaskOutcome.Success, 1, budgets),
                "A terminal task cannot emit a second completion.");
            Assert.AreEqual(TaskStatus.Complete, unrelated.Status);
            Assert.AreEqual(0, reservations.LiveReservationCount);
        }

        [Test]
        public void MarkResourcesReserved_Assigned_StoresHandleId()
        {
            var authority = CreateAuthority();
            var budgets = new WorkTimeBudgetStore();
            var reservations = new FakeReservationAuthority();
            authority.SetReservationAuthority(reservations);

            string reason;
            budgets.EnsureDay(1);
            WorkTask task = authority.CreateTask("test.sweep", Business(1), 1);
            Assert.IsTrue(authority.AssignTask(task.TaskId, Person(1), budgets, out reason), reason);

            Assert.IsTrue(authority.MarkResourcesReserved(task.TaskId, "resv-7", "oven needed", out reason), reason);
            Assert.AreEqual(TaskStatus.ResourcesReserved, task.Status);
            Assert.AreEqual("resv-7", task.ReservationHandleId);

            Assert.IsTrue(authority.StartTask(task.TaskId, 1, out reason), reason);
            Assert.AreEqual(TaskStatus.InProgress, task.Status);
        }

        [Test]
        public void MarkResourcesReserved_FromQueued_RefusedLoudly()
        {
            var authority = CreateAuthority();
            WorkTask task = authority.CreateTask("test.sweep", Business(1), 1);

            string reason;
            Assert.IsFalse(authority.MarkResourcesReserved(task.TaskId, "resv-1", "x", out reason));
            Assert.IsNotNull(reason);
            Assert.AreEqual(TaskStatus.Queued, task.Status);
        }

        [Test]
        public void MarkResourcesReserved_EmptyIdWithAuthority_Refused()
        {
            var authority = CreateAuthority();
            var budgets = new WorkTimeBudgetStore();
            authority.SetReservationAuthority(new FakeReservationAuthority());

            string reason;
            budgets.EnsureDay(1);
            WorkTask task = authority.CreateTask("test.sweep", Business(1), 1);
            Assert.IsTrue(authority.AssignTask(task.TaskId, Person(1), budgets, out reason), reason);

            Assert.IsFalse(authority.MarkResourcesReserved(task.TaskId, "", "x", out reason));
            Assert.IsNotNull(reason);
            Assert.AreEqual(TaskStatus.Assigned, task.Status);
        }

        [Test]
        public void StartTask_WithAuthorityAndBuilder_ReservesAtomically()
        {
            var authority = CreateAuthority();
            var budgets = new WorkTimeBudgetStore();
            var reservations = new FakeReservationAuthority();
            authority.SetReservationAuthority(reservations);
            authority.SetReservationRequestBuilder((task, definition) =>
            {
                var request = new ReservationRequest(task.TaskId.ToString(), "test");
                request.Items.Add(new ReservationItem("workstation:oven-1", 1f, 0f, 3600f));
                return request;
            });

            string reason;
            budgets.EnsureDay(1);
            WorkTask task = authority.CreateTask("test.sweep", Business(1), 1);
            Assert.IsTrue(authority.AssignTask(task.TaskId, Person(1), budgets, out reason), reason);
            Assert.IsTrue(authority.StartTask(task.TaskId, 1, out reason), reason);

            Assert.AreEqual(TaskStatus.InProgress, task.Status);
            Assert.IsFalse(string.IsNullOrEmpty(task.ReservationHandleId));
            Assert.AreEqual(1, reservations.LiveReservationCount);
        }

        [Test]
        public void StartTask_ReservationRefused_StartRefusedLoudly()
        {
            var authority = CreateAuthority();
            var budgets = new WorkTimeBudgetStore();
            var reservations = new FakeReservationAuthority { refuseReason = "oven-1 held by task:X" };
            authority.SetReservationAuthority(reservations);
            authority.SetReservationRequestBuilder((task, definition) =>
                new ReservationRequest(task.TaskId.ToString(), "test"));

            string reason;
            budgets.EnsureDay(1);
            WorkTask task = authority.CreateTask("test.sweep", Business(1), 1);
            Assert.IsTrue(authority.AssignTask(task.TaskId, Person(1), budgets, out reason), reason);

            Assert.IsFalse(authority.StartTask(task.TaskId, 1, out reason));
            Assert.IsNotNull(reason);
            StringAssert.Contains("oven-1 held by task:X", reason);
            Assert.AreEqual(TaskStatus.Assigned, task.Status);
        }

        [Test]
        public void CancelTask_ReleasesReservationsViaAuthority()
        {
            var authority = CreateAuthority();
            var budgets = new WorkTimeBudgetStore();
            var reservations = new FakeReservationAuthority();
            authority.SetReservationAuthority(reservations);

            string reason;
            WorkTask task = CreateAssignedStarted(authority, budgets);

            Assert.IsTrue(authority.CancelTask(task.TaskId, budgets));
            Assert.AreEqual(1, reservations.releasedForTask.Count);
            Assert.AreEqual(task.TaskId.ToString(), reservations.releasedForTask[0]);
            Assert.AreEqual(TaskStatus.Cancelled, task.Status);
        }

        [Test]
        public void PauseAndResume_HoldsAssignmentAndCommitment()
        {
            var authority = CreateAuthority();
            var budgets = new WorkTimeBudgetStore();
            string reason;

            WorkTask task = CreateAssignedStarted(authority, budgets);
            int committedBefore = budgets.GetOrCreate(Person(1)).MinutesCommitted;

            Assert.IsTrue(authority.PauseTask(task.TaskId, budgets, "flour delivery late", out reason), reason);
            Assert.AreEqual(TaskStatus.Paused, task.Status);
            Assert.AreEqual(committedBefore, budgets.GetOrCreate(Person(1)).MinutesCommitted,
                "Paused holds the worker commitment per the policy table");

            Assert.IsTrue(authority.ResumeTask(task.TaskId, "delivery arrived", out reason), reason);
            Assert.AreEqual(TaskStatus.InProgress, task.Status);
        }

        [Test]
        public void PauseTask_FromAssigned_Refused()
        {
            var authority = CreateAuthority();
            var budgets = new WorkTimeBudgetStore();
            string reason;
            budgets.EnsureDay(1);
            WorkTask task = authority.CreateTask("test.sweep", Business(1), 1);
            Assert.IsTrue(authority.AssignTask(task.TaskId, Person(1), budgets, out reason), reason);

            Assert.IsFalse(authority.PauseTask(task.TaskId, budgets, "x", out reason));
            Assert.IsNotNull(reason);
            Assert.AreEqual(TaskStatus.Assigned, task.Status);
        }

        [Test]
        public void WaitingOnPassiveProcess_ReleasesWorkerKeepsResources()
        {
            var authority = CreateAuthority();
            var budgets = new WorkTimeBudgetStore();
            var reservations = new FakeReservationAuthority();
            authority.SetReservationAuthority(reservations);
            authority.SetWorkTimeBudgetStore(budgets);
            string reason;

            WorkTask task = CreateAssignedStarted(authority, budgets);
            int committedBefore = budgets.GetOrCreate(Person(1)).MinutesCommitted;
            Assert.Greater(committedBefore, 0);

            Assert.IsTrue(
                authority.MarkWaitingOnPassiveProcess(task.TaskId, "due-PassiveProcessComplete-x", "dough proofing", out reason),
                reason);
            Assert.AreEqual(TaskStatus.WaitingOnPassiveProcess, task.Status);
            Assert.AreEqual("due-PassiveProcessComplete-x", task.DueEventId);
            Assert.AreEqual(0, budgets.GetOrCreate(Person(1)).MinutesCommitted,
                "the worker is released for other work while the passive process runs");

            Assert.IsTrue(authority.ResumeTask(task.TaskId, "proofed", out reason), reason);
            Assert.AreEqual(TaskStatus.InProgress, task.Status);
            Assert.AreEqual(committedBefore, budgets.GetOrCreate(Person(1)).MinutesCommitted,
                "resume re-books the remaining commitment");
        }

        [TestCase(TaskStatus.InProgress, "in-progress")]
        [TestCase(TaskStatus.Paused, "paused")]
        [TestCase(TaskStatus.WaitingOnDependency, "waiting on dependency")]
        public void ResumeTask_RefusedWhileWorkerBusy_LeavesTaskWaiting(TaskStatus blockingState, string blockingWord)
        {
            var authority = CreateAuthority(baseMinutes: 60);
            var budgets = new WorkTimeBudgetStore();
            authority.SetWorkTimeBudgetStore(budgets);
            budgets.EnsureDay(1);
            string reason;

            WorkTask first = authority.CreateTask("test.sweep", Business(1), 1);
            WorkTask second = authority.CreateTask("test.sweep", Business(2), 1);
            Assert.IsTrue(authority.AssignTask(first.TaskId, Person(7), budgets, out reason), reason);
            Assert.IsTrue(authority.AssignTask(second.TaskId, Person(7), budgets, out reason), reason);
            Assert.IsTrue(authority.StartTask(first.TaskId, 1, out reason), reason);
            Assert.IsTrue(
                authority.MarkWaitingOnPassiveProcess(first.TaskId, "due-PassiveProcessComplete-first", "dough proofing", out reason),
                reason);
            Assert.AreEqual(60, budgets.GetOrCreate(Person(7)).MinutesCommitted,
                "waiting releases the first task's 60 minutes; only the second task's 60 remain committed");

            Assert.IsTrue(authority.StartTask(second.TaskId, 1, out reason), reason);
            switch (blockingState)
            {
                case TaskStatus.Paused:
                    Assert.IsTrue(authority.PauseTask(second.TaskId, budgets, "temporary interruption", out reason), reason);
                    break;
                case TaskStatus.WaitingOnDependency:
                    Assert.IsTrue(authority.MarkWaitingOnDependency(second.TaskId, "required delivery", "waiting", out reason), reason);
                    break;
            }
            Assert.AreEqual(60, budgets.GetOrCreate(Person(7)).MinutesCommitted,
                "only the second task's minutes are committed");

            Assert.IsFalse(authority.ResumeTask(first.TaskId, "proofed", out reason));
            StringAssert.Contains(second.TaskId.ToString(), reason);
            StringAssert.Contains(blockingWord, reason);
            Assert.AreEqual(TaskStatus.WaitingOnPassiveProcess, first.Status, "the refused task stays waiting");
            Assert.AreEqual("due-PassiveProcessComplete-first", first.DueEventId,
                "a refused resume does not clear the due event id");
            Assert.AreEqual(blockingState, second.Status, "the blocking task is untouched");
            Assert.AreEqual(60, budgets.GetOrCreate(Person(7)).MinutesCommitted,
                "the guard runs before the budget re-commit, so a refused resume commits nothing");
        }

        [Test]
        public void ResumeTask_SucceedsWhenWorkerFree_RecommitsOnlyRemainingMinutes()
        {
            var authority = CreateAuthority(baseMinutes: 60);
            var budgets = new WorkTimeBudgetStore();
            authority.SetWorkTimeBudgetStore(budgets);
            string reason;

            WorkTask task = CreateAssignedStarted(authority, budgets);
            WorkTimeBudget budget = budgets.GetOrCreate(Person(1));
            int remainingBeforeWork = budget.MinutesRemaining;
            Assert.AreEqual(60, budget.MinutesCommitted, "first assignment commits the full 60 minutes");

            bool reached;
            Assert.IsTrue(authority.RecordWorkProgress(task.TaskId, 30, budgets, 1, out reached, out reason), reason);
            Assert.IsFalse(reached);
            Assert.AreEqual(30, task.MinutesRemaining, "30 of 60 minutes worked, 30 remain");

            Assert.IsTrue(
                authority.MarkWaitingOnPassiveProcess(task.TaskId, "due-PassiveProcessComplete-x", "dough proofing", out reason),
                reason);
            Assert.AreEqual(30, budget.MinutesCommitted,
                "waiting releases only the 30 unworked minutes; the 30 already worked stay spent");

            Assert.IsTrue(authority.ResumeTask(task.TaskId, "proofed", out reason), reason);
            Assert.AreEqual(TaskStatus.InProgress, task.Status);
            Assert.AreEqual(60, budget.MinutesCommitted,
                "resume re-books only the 30 remaining minutes (30 spent + 30 committed), not a second full 60");
            Assert.AreEqual(remainingBeforeWork, budget.MinutesRemaining,
                "resume is budget-neutral: the day budget still reflects exactly 30 spent + 30 committed");
        }

        [Test]
        public void ResumeInterruptReassignCycle_IsBudgetNeutral()
        {
            var authority = CreateAuthority(baseMinutes: 60);
            var budgets = new WorkTimeBudgetStore();
            authority.SetWorkTimeBudgetStore(budgets);
            string reason;

            WorkTask task = CreateAssignedStarted(authority, budgets);
            WorkTimeBudget budget = budgets.GetOrCreate(Person(1));
            int remainingBeforeWork = budget.MinutesRemaining;
            Assert.AreEqual(60, budget.MinutesCommitted, "first assignment commits the full 60 minutes");

            bool reached;
            Assert.IsTrue(authority.RecordWorkProgress(task.TaskId, 30, budgets, 1, out reached, out reason), reason);
            Assert.AreEqual(30, task.MinutesRemaining, "30 of 60 minutes worked, 30 remain");
            Assert.AreEqual(30, budget.MinutesWorked, "worked minutes are recorded on the budget");

            Assert.IsTrue(
                authority.MarkWaitingOnPassiveProcess(task.TaskId, "due-PassiveProcessComplete-x", "dough proofing", out reason),
                reason);
            Assert.AreEqual(30, budget.MinutesCommitted,
                "waiting releases only the 30 unworked minutes");

            Assert.IsTrue(authority.ResumeTask(task.TaskId, "proofed", out reason), reason);
            Assert.AreEqual(TaskStatus.InProgress, task.Status);
            Assert.AreEqual(60, budget.MinutesCommitted,
                "resume re-books only the 30 remaining minutes");

            Assert.IsTrue(authority.InterruptTask(task.TaskId, budgets, out reason), reason);
            Assert.AreEqual(TaskStatus.Queued, task.Status, "interruption re-queues the task");
            Assert.AreEqual(30, budget.MinutesCommitted, "interrupt releases only the 30 unworked minutes");

            Assert.IsTrue(authority.AssignTask(task.TaskId, Person(1), budgets, out reason), reason);
            Assert.AreEqual(60, budget.MinutesCommitted,
                "re-assignment commits only the 30 remaining minutes on top of the 30 already spent, not a second full 60");
            Assert.AreEqual(remainingBeforeWork, budget.MinutesRemaining,
                "resume, interrupt and re-assignment together are budget-neutral: the day budget still reflects exactly 30 spent + 30 committed");
            Assert.AreEqual(30, task.MinutesRemaining, "the work requirement is unchanged by the cycle");
        }

        [Test]
        public void RearmPassiveResume_ReplacesRatherThanDuplicates()
        {
            var authority = CreateAuthority();
            var scheduler = new DueEventScheduler();
            authority.SetDueEventScheduler(scheduler);

            WorkTask task = authority.CreateTask("test.sweep", Business(1), 1);
            string taskId = task.TaskId.ToString();
            string stableId = DueEventIdConventions.ForTask(taskId, DueEventKind.PassiveProcessComplete);
            Assert.AreEqual("due-PassiveProcessComplete-" + taskId, stableId,
                "the re-arm uses the stable per-task convention id");

            Assert.IsNull(scheduler.ScheduleWithEventId(stableId, DueEventKind.PassiveProcessComplete, 3600f, taskId));
            Assert.AreEqual(1, scheduler.ScheduledCount);
            Assert.IsNull(scheduler.ScheduleWithEventId(stableId, DueEventKind.PassiveProcessComplete, 7200f, taskId));
            Assert.AreEqual(1, scheduler.ScheduledCount,
                "a repeated refusal replaces the same stable id, never duplicates");
            Assert.AreEqual(stableId, scheduler.PeekNext().EventId);
            Assert.AreEqual(7200f, scheduler.PeekNext().DueGameSeconds,
                "the replacement carries the later due time");
        }

        [Test]
        public void RearmPassiveResume_FiresOnlyAfterDelay_AndCancelsByPayload()
        {
            var authority = CreateAuthority();
            var scheduler = new DueEventScheduler();
            authority.SetDueEventScheduler(scheduler);

            WorkTask task = authority.CreateTask("test.sweep", Business(1), 1);
            string taskId = task.TaskId.ToString();
            string stableId = DueEventIdConventions.ForTask(taskId, DueEventKind.PassiveProcessComplete);

            Assert.IsNull(scheduler.ScheduleWithEventId(stableId, DueEventKind.PassiveProcessComplete, 3600f, taskId));
            Assert.AreEqual(0, scheduler.PopDue(3599f).Count,
                "the re-armed event does not fire before its one-hour delay");
            Assert.AreEqual(1, scheduler.ScheduledCount);

            List<DueEvent> due = scheduler.PopDue(3600f);
            Assert.AreEqual(1, due.Count, "the re-armed event fires at its due time");
            Assert.AreEqual(stableId, due[0].EventId);
            Assert.AreEqual(0, scheduler.ScheduledCount);

            Assert.IsNull(scheduler.ScheduleWithEventId(stableId, DueEventKind.PassiveProcessComplete, 3600f, taskId));
            scheduler.CancelForPayload(taskId);
            Assert.AreEqual(0, scheduler.ScheduledCount,
                "terminal cleanup cancels a pending retry by payload, regardless of event id");
        }

        [Test]
        public void WaitingOnDependency_RecordsDescription_HoldsCommitment()
        {
            var authority = CreateAuthority();
            var budgets = new WorkTimeBudgetStore();
            string reason;

            WorkTask task = CreateAssignedStarted(authority, budgets);
            int committedBefore = budgets.GetOrCreate(Person(1)).MinutesCommitted;

            Assert.IsTrue(
                authority.MarkWaitingOnDependency(task.TaskId, "flour delivery #42", "no flour", out reason),
                reason);
            Assert.AreEqual(TaskStatus.WaitingOnDependency, task.Status);
            Assert.AreEqual("flour delivery #42", task.DependencyDescription);
            Assert.AreEqual(committedBefore, budgets.GetOrCreate(Person(1)).MinutesCommitted);

            Assert.IsTrue(authority.ResumeTask(task.TaskId, "delivery arrived", out reason), reason);
            Assert.AreEqual(TaskStatus.InProgress, task.Status);
        }

        [Test]
        public void MarkReworkRequired_Failed_LinksCorrectiveTask()
        {
            var authority = CreateAuthority();
            var budgets = new WorkTimeBudgetStore();
            string reason;

            WorkTask task = CreateAssignedStarted(authority, budgets);
            WorkTask corrective = authority.CreateTask("test.sweep", Business(1), 1);
            Assert.IsTrue(authority.CompleteTask(task.TaskId, TaskOutcome.RecoverableFailure, 1, budgets));
            Assert.AreEqual(TaskStatus.Failed, task.Status);

            Assert.IsTrue(authority.MarkReworkRequired(task.TaskId, corrective.TaskId, "re-knead", out reason), reason);
            Assert.AreEqual(TaskStatus.ReworkRequired, task.Status);
            Assert.Contains(corrective.TaskId, task.FollowUpTaskIds);
            Assert.IsFalse(task.IsOpen, "ReworkRequired is terminal for this instance");
        }

        [Test]
        public void MarkReworkRequired_FromInProgress_Refused()
        {
            var authority = CreateAuthority();
            var budgets = new WorkTimeBudgetStore();
            string reason;

            WorkTask task = CreateAssignedStarted(authority, budgets);
            Assert.IsFalse(authority.MarkReworkRequired(task.TaskId, default(EntityId), "x", out reason));
            Assert.IsNotNull(reason);
            Assert.AreEqual(TaskStatus.InProgress, task.Status);
        }

        [Test]
        public void MarkReworkRequired_UnknownCorrectiveId_Refused()
        {
            var authority = CreateAuthority();
            var budgets = new WorkTimeBudgetStore();
            string reason;

            WorkTask task = CreateAssignedStarted(authority, budgets);
            Assert.IsTrue(authority.CompleteTask(task.TaskId, TaskOutcome.SeriousFailure, 1, budgets));

            Assert.IsFalse(
                authority.MarkReworkRequired(task.TaskId, EntityId.For(EntityKind.WorkTask, 999999), "x", out reason));
            Assert.IsNotNull(reason);
            Assert.AreEqual(TaskStatus.Failed, task.Status);
        }

        [Test]
        public void CompleteTask_CancelsScheduledDueEvents()
        {
            var authority = CreateAuthority();
            var budgets = new WorkTimeBudgetStore();
            var scheduler = new DueEventScheduler();
            authority.SetDueEventScheduler(scheduler);
            string reason;

            WorkTask task = CreateAssignedStarted(authority, budgets);
            string eventId;
            Assert.IsNull(scheduler.Schedule(DueEventKind.TaskCompletionDue, 3600f, task.TaskId.ToString(), out eventId));
            Assert.AreEqual(1, scheduler.ScheduledCount);

            Assert.IsTrue(authority.CompleteTask(task.TaskId, TaskOutcome.Success, 1, budgets));
            Assert.AreEqual(0, scheduler.ScheduledCount, "terminal transitions cancel the task's due events");
        }

        [Test]
        public void InterruptTask_FromPaused_Allowed_ReleasesResources()
        {
            var authority = CreateAuthority();
            var budgets = new WorkTimeBudgetStore();
            var reservations = new FakeReservationAuthority();
            authority.SetReservationAuthority(reservations);
            string reason;

            WorkTask task = CreateAssignedStarted(authority, budgets);
            Assert.IsTrue(authority.PauseTask(task.TaskId, budgets, "x", out reason), reason);
            Assert.IsTrue(authority.InterruptTask(task.TaskId, budgets, out reason), reason);
            Assert.AreEqual(TaskStatus.Queued, task.Status);
            Assert.AreEqual(1, reservations.releasedForTask.Count);
        }

        [Test]
        public void InterruptTask_FromWaitingOnPassiveProcess_Refused()
        {
            var authority = CreateAuthority();
            var budgets = new WorkTimeBudgetStore();
            string reason;

            WorkTask task = CreateAssignedStarted(authority, budgets);
            Assert.IsTrue(authority.MarkWaitingOnPassiveProcess(task.TaskId, "due-1", "x", out reason), reason);
            Assert.IsFalse(authority.InterruptTask(task.TaskId, budgets, out reason));
            Assert.IsNotNull(reason);
            Assert.AreEqual(TaskStatus.WaitingOnPassiveProcess, task.Status);
        }

        [Test]
        public void FindTaskByIdString_RoundTrips()
        {
            var authority = CreateAuthority();
            WorkTask task = authority.CreateTask("test.sweep", Business(1), 1);
            Assert.AreSame(task, authority.FindTaskByIdString(task.TaskId.ToString()));
            Assert.IsNull(authority.FindTaskByIdString("task-999999"));
        }
    }
}
