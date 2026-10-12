using System.Collections.Generic;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Time
{
    /// <summary>
    /// Pass IV (Worker 1): the due-event scheduler is ordered, deterministic and
    /// idempotent — small steps and large steps produce the same event stream.
    /// </summary>
    [TestFixture]
    public sealed class DueEventSchedulerTests
    {
        private DueEventScheduler CreateScheduler()
        {
            return new DueEventScheduler();
        }

        [Test]
        public void PopDue_ReturnsEventsInDueThenSequenceOrder()
        {
            var scheduler = CreateScheduler();
            string ignored;
            // Scheduled out of order, with two events sharing a due time.
            Assert.IsNull(scheduler.Schedule(DueEventKind.TaskCompletionDue, 300f, "task-c", out ignored));
            Assert.IsNull(scheduler.Schedule(DueEventKind.TendingDue, 100f, "task-a", out ignored));
            Assert.IsNull(scheduler.Schedule(DueEventKind.PassiveProcessComplete, 200f, "proc-b", out ignored));
            Assert.IsNull(scheduler.Schedule(DueEventKind.TendingDue, 100f, "task-a2", out ignored));

            List<DueEvent> due = scheduler.PopDue(1000f);

            Assert.AreEqual(4, due.Count);
            Assert.AreEqual(100f, due[0].DueGameSeconds);
            Assert.AreEqual(100f, due[1].DueGameSeconds);
            Assert.Less(due[0].Sequence, due[1].Sequence, "same due time breaks ties by sequence");
            Assert.AreEqual(200f, due[2].DueGameSeconds);
            Assert.AreEqual(300f, due[3].DueGameSeconds);
            Assert.AreEqual(0, scheduler.ScheduledCount);
        }

        [Test]
        public void PopDue_OnlyPopsDueEvents()
        {
            var scheduler = CreateScheduler();
            string ignored;
            scheduler.Schedule(DueEventKind.TaskCompletionDue, 100f, "task-a", out ignored);
            scheduler.Schedule(DueEventKind.TaskCompletionDue, 500f, "task-b", out ignored);

            List<DueEvent> due = scheduler.PopDue(200f);
            Assert.AreEqual(1, due.Count);
            Assert.AreEqual("task-a", due[0].PayloadRef);
            Assert.AreEqual(1, scheduler.ScheduledCount);
            Assert.AreEqual("task-b", scheduler.PeekNext().PayloadRef);
        }

        [Test]
        public void Schedule_SameIdTwice_ReplacesInsteadOfDuplicating()
        {
            var scheduler = CreateScheduler();
            string firstId, secondId;
            Assert.IsNull(scheduler.Schedule(DueEventKind.TaskCompletionDue, 100f, "task-a", out firstId));
            Assert.IsNull(scheduler.Schedule(DueEventKind.TaskCompletionDue, 900f, "task-a", out secondId));

            Assert.AreEqual(firstId, secondId, "stable id per (kind, payload)");
            Assert.AreEqual(1, scheduler.ScheduledCount);

            List<DueEvent> due = scheduler.PopDue(500f);
            Assert.AreEqual(0, due.Count, "the reschedule replaced the earlier due time");
            due = scheduler.PopDue(1000f);
            Assert.AreEqual(1, due.Count);
        }

        [Test]
        public void Schedule_InvalidDueTime_Refused()
        {
            var scheduler = CreateScheduler();
            string eventId;

            Assert.IsNotNull(scheduler.Schedule(DueEventKind.TaskCompletionDue, -5f, "task-a", out eventId));
            Assert.IsNull(eventId);
            Assert.IsNotNull(scheduler.Schedule(DueEventKind.TaskCompletionDue, float.NaN, "task-a", out eventId));
            Assert.AreEqual(0, scheduler.ScheduledCount);
        }

        [Test]
        public void CancelForPayload_RemovesOnlyMatchingEvents()
        {
            var scheduler = CreateScheduler();
            string ignored;
            scheduler.Schedule(DueEventKind.TaskCompletionDue, 100f, "task-a", out ignored);
            scheduler.Schedule(DueEventKind.TendingDue, 200f, "task-a", out ignored);
            scheduler.Schedule(DueEventKind.TaskCompletionDue, 300f, "task-b", out ignored);

            scheduler.CancelForPayload("task-a");

            Assert.AreEqual(1, scheduler.ScheduledCount);
            Assert.AreEqual("task-b", scheduler.PeekNext().PayloadRef);

            scheduler.Cancel("missing-id"); // no-op, must not throw
            scheduler.CancelForPayload("missing-payload"); // no-op, must not throw
            Assert.AreEqual(1, scheduler.ScheduledCount);
        }

        [Test]
        public void EventIds_MatchDueEventIdConventions()
        {
            var scheduler = CreateScheduler();
            string eventId;
            scheduler.Schedule(DueEventKind.TaskCompletionDue, 100f, "task-abc", out eventId);
            Assert.AreEqual(DueEventIdConventions.ForTask("task-abc", DueEventKind.TaskCompletionDue), eventId);
        }

        [Test]
        public void Determinism_SmallStepsAndLargeSteps_PopIdenticalStreams()
        {
            var small = BuildSeededSchedule(seed: 42, count: 60);
            var large = BuildSeededSchedule(seed: 42, count: 60);

            var smallOrder = new List<string>();
            for (float t = 0f; t <= 10000f; t += 250f)
            {
                foreach (DueEvent dueEvent in small.PopDue(t))
                {
                    smallOrder.Add(dueEvent.EventId + "@" + dueEvent.DueGameSeconds);
                }
            }

            var largeOrder = new List<string>();
            foreach (DueEvent dueEvent in large.PopDue(10000f))
            {
                largeOrder.Add(dueEvent.EventId + "@" + dueEvent.DueGameSeconds);
            }

            Assert.AreEqual(largeOrder.Count, smallOrder.Count);
            CollectionAssert.AreEqual(largeOrder, smallOrder,
                "the same schedule must pop the identical ordered stream in small and large steps");
        }

        [Test]
        public void Determinism_SameSeed_SameOrder()
        {
            var first = BuildSeededSchedule(seed: 7, count: 40);
            var second = BuildSeededSchedule(seed: 7, count: 40);

            var firstOrder = new List<string>();
            var secondOrder = new List<string>();
            foreach (DueEvent dueEvent in first.PopDue(10000f))
            {
                firstOrder.Add(dueEvent.EventId);
            }
            foreach (DueEvent dueEvent in second.PopDue(10000f))
            {
                secondOrder.Add(dueEvent.EventId);
            }

            CollectionAssert.AreEqual(firstOrder, secondOrder);
        }

        [Test]
        public void SaveLoad_RoundTrip_PreservesScheduleAndSequence()
        {
            var scheduler = CreateScheduler();
            string ignored;
            scheduler.Schedule(DueEventKind.TaskCompletionDue, 300f, "task-c", out ignored);
            scheduler.Schedule(DueEventKind.TendingDue, 100f, "task-a", out ignored);
            scheduler.Schedule(DueEventKind.PassiveProcessComplete, 200f, "proc-b", out ignored);

            DueEventSchedulerSaveDto dto = scheduler.CaptureSaveDto();

            var restored = CreateScheduler();
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(3, restored.ScheduledCount);
            List<DueEvent> due = restored.PopDue(1000f);
            Assert.AreEqual(3, due.Count);
            Assert.AreEqual(100f, due[0].DueGameSeconds);
            Assert.AreEqual(200f, due[1].DueGameSeconds);
            Assert.AreEqual(300f, due[2].DueGameSeconds);

            // The sequence cursor survives: a new event sorts after every restored one.
            string newId;
            Assert.IsNull(restored.Schedule(DueEventKind.ShiftBoundary, 100f, "late", out newId));
            List<DueEvent> late = restored.PopDue(100f);
            Assert.AreEqual(1, late.Count);
            Assert.AreEqual(newId, late[0].EventId);
        }

        [Test]
        public void SaveLoad_IdempotentRestore()
        {
            var scheduler = CreateScheduler();
            string ignored;
            scheduler.Schedule(DueEventKind.TaskCompletionDue, 100f, "task-a", out ignored);

            DueEventSchedulerSaveDto dto = scheduler.CaptureSaveDto();

            var restored = CreateScheduler();
            restored.LoadFromSaveDto(dto);
            restored.LoadFromSaveDto(dto); // clear-then-load: second load must not duplicate

            Assert.AreEqual(1, restored.ScheduledCount);
        }

        private static DueEventScheduler BuildSeededSchedule(int seed, int count)
        {
            var scheduler = new DueEventScheduler();
            var random = new System.Random(seed);
            for (int i = 0; i < count; i++)
            {
                // Deterministic pseudo-random dues with frequent ties.
                float due = (float)(random.Next(0, 40) * 250);
                string eventId;
                scheduler.Schedule(
                    (DueEventKind)(1 + random.Next(0, 9)),
                    due,
                    "payload-" + i.ToString("D3"),
                    out eventId);
            }

            return scheduler;
        }
    }
}
