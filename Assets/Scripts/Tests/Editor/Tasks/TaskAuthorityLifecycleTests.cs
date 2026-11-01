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
