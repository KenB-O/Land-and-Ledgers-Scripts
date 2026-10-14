using System.Collections.Generic;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.Editor.Time
{
    /// <summary>
    /// Pass IV (Worker 4): the deterministic due-event scheduler fallback.
    /// Proves: due-order popping, deterministic tie-breaks, idempotent
    /// rescheduling (replace, never duplicate), no-op cancels, payload cancels,
    /// and save/load round-trip preserving order.
    /// </summary>
    [TestFixture]
    public sealed class SimpleDueEventSchedulerTests
    {
        [Test]
        public void PopDue_ReturnsInDueOrder_WithDeterministicTieBreak()
        {
            var scheduler = new SimpleDueEventScheduler();
            string ignored;
            scheduler.Schedule(DueEventKind.TaskCompletionDue, 300f, "task-b", out ignored);
            scheduler.Schedule(DueEventKind.TaskCompletionDue, 100f, "task-a", out ignored);
            scheduler.Schedule(DueEventKind.TaskCompletionDue, 100f, "task-c", out ignored);

            List<DueEvent> due = scheduler.PopDue(300f);

            Assert.AreEqual(3, due.Count);
            Assert.AreEqual("task-a", due[0].PayloadRef);
            Assert.AreEqual("task-c", due[1].PayloadRef); // same second: schedule order
            Assert.AreEqual("task-b", due[2].PayloadRef);
            Assert.AreEqual(0, scheduler.ScheduledCount);
        }

        [Test]
        public void Schedule_SameEventId_Replaces_NeverDuplicates()
        {
            var scheduler = new SimpleDueEventScheduler();
            string first, second;
            scheduler.Schedule(DueEventKind.TaskCompletionDue, 100f, "task-a", out first);
            scheduler.Schedule(DueEventKind.TaskCompletionDue, 500f, "task-a", out second);

            Assert.AreEqual(first, second);
            Assert.AreEqual(1, scheduler.ScheduledCount);

            // The replacement moved the due time: nothing due at 100.
            Assert.AreEqual(0, scheduler.PopDue(100f).Count);
            Assert.AreEqual(1, scheduler.PopDue(500f).Count);
        }

        [Test]
        public void Cancel_UnknownId_IsNoOp()
        {
            var scheduler = new SimpleDueEventScheduler();
            string ignored;
            scheduler.Schedule(DueEventKind.ShiftBoundary, 60f, "shift", out ignored);

            scheduler.Cancel("due-TaskCompletionDue-nope");
            Assert.AreEqual(1, scheduler.ScheduledCount);
        }

        [Test]
        public void CancelForPayload_RemovesOnlyThatPayload()
        {
            var scheduler = new SimpleDueEventScheduler();
            string ignored;
            scheduler.Schedule(DueEventKind.TaskCompletionDue, 100f, "task-a", out ignored);
            scheduler.Schedule(DueEventKind.ResourceReleaseDue, 200f, "task-a", out ignored);
            scheduler.Schedule(DueEventKind.TaskCompletionDue, 150f, "task-b", out ignored);

            scheduler.CancelForPayload("task-a");

            Assert.AreEqual(1, scheduler.ScheduledCount);
            List<DueEvent> due = scheduler.PopDue(1000f);
            Assert.AreEqual(1, due.Count);
            Assert.AreEqual("task-b", due[0].PayloadRef);
        }

        [Test]
        public void PeekNext_ReturnsEarliest_WithoutRemoving()
        {
            var scheduler = new SimpleDueEventScheduler();
            string ignored;
            scheduler.Schedule(DueEventKind.TaskCompletionDue, 400f, "late", out ignored);
            scheduler.Schedule(DueEventKind.TaskCompletionDue, 50f, "early", out ignored);

            DueEvent next = scheduler.PeekNext();
            Assert.IsNotNull(next);
            Assert.AreEqual("early", next.PayloadRef);
            Assert.AreEqual(2, scheduler.ScheduledCount);
        }

        [Test]
        public void SaveLoad_RoundTrip_PreservesOrderAndSequence()
        {
            var scheduler = new SimpleDueEventScheduler();
            string ignored;
            scheduler.Schedule(DueEventKind.TaskCompletionDue, 100f, "task-a", out ignored);
            scheduler.Schedule(DueEventKind.TaskCompletionDue, 200f, "task-b", out ignored);
            scheduler.PopDue(100f); // task-a fires before the save

            var restored = new SimpleDueEventScheduler();
            restored.LoadFromSaveDto(scheduler.CaptureSaveDto());

            Assert.AreEqual(1, restored.ScheduledCount);
            List<DueEvent> due = restored.PopDue(200f);
            Assert.AreEqual(1, due.Count);
            Assert.AreEqual("task-b", due[0].PayloadRef);
        }

        [Test]
        public void DueEventIds_AreStableConventions()
        {
            Assert.AreEqual(
                "due-TaskCompletionDue-task-1",
                DueEventIdConventions.ForTask("task-1", DueEventKind.TaskCompletionDue));
            Assert.AreEqual(
                "due-ResourceReleaseDue-resv-abc",
                DueEventIdConventions.ForReservation("abc"));
        }

        [Test]
        public void Clear_PurgesAllEvents()
        {
            // G-05: New Game / Restart must not inherit the previous campaign's
            // due events.
            var scheduler = new SimpleDueEventScheduler();
            string ignored;
            scheduler.Schedule(DueEventKind.TaskCompletionDue, 100f, "task-a", out ignored);
            scheduler.Schedule(DueEventKind.TendingDue, 200f, "task-b", out ignored);
            Assert.AreEqual(2, scheduler.ScheduledCount);

            scheduler.Clear();

            Assert.AreEqual(0, scheduler.ScheduledCount);
            Assert.IsNull(scheduler.PeekNext());
            Assert.AreEqual(0, scheduler.PopDue(100000f).Count);
        }
    }
}
