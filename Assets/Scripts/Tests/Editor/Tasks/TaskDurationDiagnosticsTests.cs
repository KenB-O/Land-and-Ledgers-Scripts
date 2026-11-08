using System;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Tasks
{
    /// <summary>
    /// Priority 4 Slice 0 — discriminator pins (plan 04-plan-task-duration-diagnostics.md §5 Slice 0).
    /// Tests only; no production code. Each test pins a gap from plan §1.9 (G1–G11) as a
    /// fact about the real authorities (real TaskAuthority, real WorkTask, real estimators,
    /// real WorkTimeBudgetStore). All 7 are expected to PASS: they prove the gaps as written.
    /// If any pin fails, the plan's §1 is wrong and Slice 0 stops and rewrites it.
    /// </summary>
    [TestFixture]
    public sealed class TaskDurationDiagnosticsTests
    {
        private static EntityId Person(int id)
        {
            return EntityId.For(EntityKind.Person, id);
        }

        private static EntityId Business(int id)
        {
            return EntityId.For(EntityKind.Business, id);
        }

        private static TaskAuthority CreateAuthority(string definitionId = "test.sweep", int baseMinutes = 10)
        {
            var authority = new TaskAuthority();
            string reason;
            Assert.IsTrue(authority.RegisterDefinition(new TaskDefinition(definitionId, "Sweep", baseMinutes), out reason), reason);
            return authority;
        }

        /// <summary>
        /// Asserts no public property or field on the type has a name containing any of the
        /// given substrings (case-insensitive). A pin that a piece of information is stored
        /// nowhere on the record.
        /// </summary>
        private static void AssertNoPublicMemberNamedContaining(Type type, params string[] substrings)
        {
            foreach (System.Reflection.PropertyInfo property in type.GetProperties())
            {
                foreach (string substring in substrings)
                {
                    Assert.IsTrue(property.Name.IndexOf(substring, StringComparison.OrdinalIgnoreCase) < 0,
                        type.Name + " exposes a public property named '" + property.Name + "' containing '" + substring + "'.");
                }
            }

            foreach (System.Reflection.FieldInfo field in type.GetFields())
            {
                foreach (string substring in substrings)
                {
                    Assert.IsTrue(field.Name.IndexOf(substring, StringComparison.OrdinalIgnoreCase) < 0,
                        type.Name + " exposes a public field named '" + field.Name + "' containing '" + substring + "'.");
                }
            }
        }

        private static bool OpenTasksContain(TaskAuthority authority, EntityId taskId)
        {
            foreach (WorkTask task in authority.GetAllOpenTasks())
            {
                if (task.TaskId == taskId)
                {
                    return true;
                }
            }

            return false;
        }

        // G1/G2: no clock timestamp on any task transition (day indexes only); no last-transition record.
        [Test]
        public void WorkTask_HasDayIndexesOnly_NoClockTimestamp()
        {
            TaskAuthority authority = CreateAuthority();
            WorkTask task = authority.CreateTask("test.sweep", Business(1), 2);

            Assert.AreEqual(2, task.CreatedDayIndex);
            Assert.AreEqual(-1, task.StartedDayIndex);
            Assert.AreEqual(-1, task.CompletedDayIndex);

            AssertNoPublicMemberNamedContaining(typeof(WorkTask), "GameSeconds", "Timestamp", "LastTransition");
        }

        // G1: two tasks created on the same day at different clock times are
        // indistinguishable from WorkTask fields alone — day resolution only.
        // (The test would use different clock times; the tasks carry no clock.)
        [Test]
        public void TwoTasksSameDay_DifferentClockTimes_Indistinguishable()
        {
            TaskAuthority authority = CreateAuthority();
            WorkTask first = authority.CreateTask("test.sweep", Business(1), 2);
            WorkTask second = authority.CreateTask("test.sweep", Business(1), 2);

            Assert.AreNotEqual(first.TaskId, second.TaskId, "two distinct tasks");
            Assert.AreEqual(first.CreatedDayIndex, second.CreatedDayIndex);
            Assert.AreEqual(first.StartedDayIndex, second.StartedDayIndex);
            Assert.AreEqual(first.CompletedDayIndex, second.CompletedDayIndex);
            Assert.AreEqual(first.MinutesWorked, second.MinutesWorked);
            Assert.AreEqual(first.Status, second.Status);
        }

        // G5: the estimator Basis is discarded at assignment — the adapter returns an int,
        // and no WorkTask field holds which estimator or basis produced PlannedMinutes.
        [Test]
        public void EstimatorBasis_DiscardedAtAssignment()
        {
            TaskAuthority authority = CreateAuthority("fr.load-freight", 1);
            var universal = new UniversalTaskDurationEstimator(
                new ITaskDurationEstimatorV2[] { new HandlingDurationEstimator() },
                inputs => DurationEstimate.FromGameSeconds(
                    Math.Max(1, inputs.BaseMinutes) * 60f, "test-fallback"));

            // The basis exists at the estimator level: the handling estimator owns this id.
            DurationEstimate direct = universal.Estimate(new TaskDurationInputs
            {
                DefinitionId = "fr.load-freight",
                BaseMinutes = 1,
            });
            StringAssert.StartsWith("handling:", direct.Basis);

            authority.SetDurationEstimatorV2(universal);
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(1);

            string reason;
            WorkTask task = authority.CreateTask("fr.load-freight", Business(1), 1);
            Assert.IsTrue(authority.AssignTask(task.TaskId, Person(1), budgets, out reason), reason);
            Assert.Greater(task.PlannedMinutes, 0, "assignment must set a planned duration");

            AssertNoPublicMemberNamedContaining(typeof(WorkTask), "Basis", "Estimator");
        }

        // §0: a synchronous task is born and dies inside one day drive, invisible to any
        // once-a-day poll — polling alone cannot see it.
        [Test]
        public void SynchronousTask_BornAndCompletedInsideOneDayDrive_IsInvisibleToDailyPoll()
        {
            TaskAuthority authority = CreateAuthority();
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(1);

            string reason;
            WorkTask task = authority.CreateTask("test.sweep", Business(1), 1);
            Assert.IsTrue(authority.AssignTask(task.TaskId, Person(1), budgets, out reason), reason);
            Assert.IsTrue(authority.StartTask(task.TaskId, 1, out reason), reason);
            Assert.IsTrue(authority.RecordWork(task.TaskId, task.PlannedMinutes, budgets, 1, out reason), reason);

            Assert.AreEqual(TaskStatus.Complete, task.Status);
            Assert.AreEqual(TaskOutcome.Success, task.Outcome);
            Assert.AreEqual(1, task.CompletedDayIndex);
            Assert.IsTrue(!OpenTasksContain(authority, task.TaskId),
                "a task completed inside the day drive is no longer in GetAllOpenTasks(); a daily poll never sees it");
            Assert.AreEqual(0, authority.GetOpenTasks(Business(1)).Count);
        }

        // The detectable defect class (§3.5 shape): a task assigned and started on day 0
        // with no RecordWork is still InProgress when the fixture day advances to day 2.
        [Test]
        public void MultiDayStuckTask_IsVisibleAtDayBoundaries()
        {
            TaskAuthority authority = CreateAuthority();
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(0);

            string reason;
            WorkTask task = authority.CreateTask("test.sweep", Business(1), 0);
            Assert.IsTrue(authority.AssignTask(task.TaskId, Person(1), budgets, out reason), reason);
            Assert.IsTrue(authority.StartTask(task.TaskId, 0, out reason), reason);

            // Advance the fixture day to 2 with no RecordWork.
            budgets.EnsureDay(2);

            Assert.AreEqual(TaskStatus.InProgress, task.Status);
            Assert.IsTrue(task.IsOpen);
            Assert.AreEqual(0, task.MinutesWorked);
            Assert.AreEqual(0, task.CreatedDayIndex);
            Assert.AreEqual(0, task.StartedDayIndex);
            Assert.AreEqual(-1, task.CompletedDayIndex);
            Assert.IsTrue(OpenTasksContain(authority, task.TaskId));
        }

        // G7: assign/start refusal reasons are returned to the caller and stored nowhere.
        [Test]
        public void AssignRefusalReason_StoredNowhere()
        {
            TaskAuthority authority = CreateAuthority();
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(1);

            // Exhaust the worker's 600-minute daily budget outright.
            string commitReason;
            Assert.IsTrue(budgets.GetOrCreate(Person(1)).TryCommit(WorkTimeBudget.DefaultDailyWorkMinutes, out commitReason), commitReason);
            Assert.AreEqual(0, budgets.GetOrCreate(Person(1)).MinutesRemaining);

            WorkTask task = authority.CreateTask("test.sweep", Business(1), 1);
            string refusal;
            Assert.IsFalse(authority.AssignTask(task.TaskId, Person(1), budgets, out refusal));
            Assert.IsTrue(!string.IsNullOrEmpty(refusal), "the refusal reason exists in the returned string");
            StringAssert.Contains("minutes remaining", refusal);
            Assert.AreEqual(TaskStatus.Queued, task.Status, "the refused task stays queued");

            // The refusal exists only in the returned string: the task carries no refusal field.
            AssertNoPublicMemberNamedContaining(typeof(WorkTask), "Refusal", "Rejection");
        }

        // G6: no transition events on TaskAuthority (verified by grep in Core/Tasks and Core/Time).
        [Test]
        public void NoTransitionEvents_OnTaskAuthority()
        {
            Assert.AreEqual(0, typeof(TaskAuthority).GetEvents().Length,
                "TaskAuthority exposes no public events; there is nothing for a diagnostic to subscribe to");
        }

        // ---- Slice 1: observer hook + clock seam + timing records (plan §5 Slice 1) ----
        // All Slice 1 tests drive the real TaskAuthority with a fake advancing clock
        // (Func<float> returning a test-controlled value, advanced in seconds) and the
        // real TaskDurationDiagnostics service as the installed observer. No test uses
        // wall-clock time, DateTime, Time.time, frame counts, or Update ticks.

        /// <summary>Fake simulation clock: the test advances it explicitly, in seconds.</summary>
        private sealed class FakeClock
        {
            public float Now;
            public int Reads;

            public float Read()
            {
                Reads++;
                return Now;
            }
        }

        private static TaskAuthority CreateAuthorityWithDiagnostics(out TaskDurationDiagnostics diagnostics, FakeClock clock)
        {
            TaskAuthority authority = CreateAuthority();
            diagnostics = new TaskDurationDiagnostics();
            authority.SetTimingObserver(diagnostics);
            if (clock != null)
            {
                authority.SetTimingClock(clock.Read);
            }

            return authority;
        }

        private static WorkTask CreateAssignStart(TaskAuthority authority, WorkTimeBudgetStore budgets, int day, EntityId owner)
        {
            string reason;
            WorkTask task = authority.CreateTask("test.sweep", owner, day);
            Assert.IsTrue(authority.AssignTask(task.TaskId, Person(1), budgets, day, out reason), reason);
            Assert.IsTrue(authority.StartTask(task.TaskId, day, out reason), reason);
            return task;
        }

        // Slice 1: create/assign/start seconds and days are recorded from the fake clock.
        [Test]
        public void Observer_RecordsCreateAssignStartSeconds_FromFakeClock()
        {
            var clock = new FakeClock { Now = 90000f };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics(out diagnostics, clock);
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(1);

            string reason;
            WorkTask task = authority.CreateTask("test.sweep", Business(1), 1);
            clock.Now = 93600f;
            Assert.IsTrue(authority.AssignTask(task.TaskId, Person(1), budgets, 1, out reason), reason);
            clock.Now = 97200f;
            Assert.IsTrue(authority.StartTask(task.TaskId, 1, out reason), reason);

            TaskTimingRecord record = diagnostics.GetRecord(task.TaskId);
            Assert.IsNotNull(record, "the observer must hold a record for a task it saw created");
            Assert.AreEqual("test.sweep", record.DefinitionId);
            Assert.AreEqual(Business(1), record.OwnerId);
            Assert.AreEqual(Person(1), record.AssigneeId);
            Assert.AreEqual(90000f, record.CreatedSeconds, 0.001f);
            Assert.AreEqual(1, record.CreatedDay);
            Assert.AreEqual(93600f, record.AssignedSeconds, 0.001f);
            Assert.AreEqual(1, record.AssignedDay);
            Assert.AreEqual(97200f, record.StartedSeconds, 0.001f);
            Assert.AreEqual(1, record.StartedDay);
            Assert.AreEqual("StartTask", record.LastTransitionKind);
            Assert.AreEqual(97200f, record.LastTransitionSeconds, 0.001f);
            Assert.AreEqual(1, record.LastTransitionDay);
            Assert.AreEqual(97200f, record.LastProgressSeconds, 0.001f);
            Assert.AreEqual(TaskTimingClockPrecision.Full, record.ClockPrecision);
            Assert.AreEqual(3600f, record.AccumulatedQueueSeconds, 0.001f, "queue time ends at assignment");
            Assert.AreEqual(3600f, record.AccumulatedAssignedWaitingSeconds, 0.001f, "assigned-waiting ends at start");
            Assert.AreEqual(0f, record.AccumulatedActiveSeconds, 0.001f);
            Assert.AreEqual(-1f, record.CompletedSeconds, 0.001f, "not completed yet");
        }

        // Slice 1 / §2.2: active seconds accumulate only while InProgress; paused time
        // goes to the paused/blocked bucket, not the active bucket.
        [Test]
        public void Observer_AccumulatesActiveSeconds_OnlyWhileInProgress()
        {
            var clock = new FakeClock { Now = 0f };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics(out diagnostics, clock);
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(0);

            WorkTask task = CreateAssignStart(authority, budgets, 0, Business(1));
            Assert.AreEqual(TaskStatus.InProgress, task.Status);

            clock.Now = 3600f;
            string reason;
            Assert.IsTrue(authority.PauseTask(task.TaskId, budgets, "flour delivery late", out reason), reason);
            clock.Now = 7200f;
            Assert.IsTrue(authority.ResumeTask(task.TaskId, "delivery arrived", out reason), reason);
            clock.Now = 9000f;
            Assert.IsTrue(authority.CompleteTask(task.TaskId, TaskOutcome.Success, 0, budgets));

            TaskTimingRecord record = diagnostics.GetRecord(task.TaskId);
            Assert.IsNotNull(record);
            Assert.AreEqual(5400f, record.AccumulatedActiveSeconds, 0.001f,
                "3600 s before the pause + 1800 s after the resume; the 3600 s paused are not active");
            Assert.AreEqual(3600f, record.AccumulatedPausedSeconds, 0.001f, "the paused bucket holds the pause");
            Assert.AreEqual(3600f, record.AccumulatedBlockedSeconds, 0.001f,
                "paused time is blocked time (M7, split by reason class)");
            Assert.AreEqual(9000f, record.CompletedSeconds, 0.001f);
            Assert.AreEqual(0, record.CompletedDay);
            Assert.AreEqual("CompleteTask", record.LastTransitionKind);
        }

        // Slice 1 / M7: blocked seconds accumulate under WaitingOnDependency with the
        // dependency description stored verbatim as the block reason.
        [Test]
        public void Observer_BlockedSeconds_AccumulateUnderWaitingOnDependency_WithReason()
        {
            var clock = new FakeClock { Now = 0f };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics(out diagnostics, clock);
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(0);

            WorkTask task = CreateAssignStart(authority, budgets, 0, Business(1));

            string reason;
            Assert.IsTrue(
                authority.MarkWaitingOnDependency(task.TaskId, "flour delivery #42", "no flour", out reason), reason);
            Assert.AreEqual("flour delivery #42", task.DependencyDescription);

            clock.Now = 7200f;
            Assert.IsTrue(authority.ResumeTask(task.TaskId, "delivery arrived", out reason), reason);

            TaskTimingRecord record = diagnostics.GetRecord(task.TaskId);
            Assert.IsNotNull(record);
            Assert.AreEqual(7200f, record.AccumulatedBlockedSeconds, 0.001f);
            Assert.AreEqual("flour delivery #42", record.LastBlockReason, "the reason text is stored verbatim");
            Assert.AreEqual(7200f, record.AccumulatedBlockedSecondsByReason["flour delivery #42"], 0.001f);
            Assert.AreEqual(0f, record.AccumulatedActiveSeconds, 0.001f,
                "no active seconds accrued: the task went straight from start into waiting");
        }

        // Slice 1 / §2.2: a synchronous task (born and completed inside one day drive,
        // at one clock time) is still recorded, with active seconds ~0 and worked == planned.
        [Test]
        public void Observer_SynchronousTask_StillRecorded()
        {
            var clock = new FakeClock { Now = 5000f };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics(out diagnostics, clock);
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(0);

            string reason;
            WorkTask task = authority.CreateTask("test.sweep", Business(1), 0);
            Assert.IsTrue(authority.AssignTask(task.TaskId, Person(1), budgets, 0, out reason), reason);
            Assert.IsTrue(authority.StartTask(task.TaskId, 0, out reason), reason);
            Assert.IsTrue(authority.RecordWork(task.TaskId, task.PlannedMinutes, budgets, 0, out reason), reason);

            Assert.AreEqual(TaskStatus.Complete, task.Status);
            TaskTimingRecord record = diagnostics.GetRecord(task.TaskId);
            Assert.IsNotNull(record, "a synchronous task must still leave a timing record");
            Assert.AreEqual(task.PlannedMinutes, record.MinutesWorkedLastSeen);
            Assert.AreEqual(task.PlannedMinutes, record.PlannedMinutesLastSeen);
            Assert.AreEqual(0f, record.AccumulatedActiveSeconds, 0.001f, "all transitions at one clock time");
            Assert.AreEqual(5000f, record.CreatedSeconds, 0.001f);
            Assert.AreEqual(5000f, record.CompletedSeconds, 0.001f);
            Assert.AreEqual("CompleteTask", record.LastTransitionKind);
        }

        // Slice 1: entering WaitingOnPassiveProcess records the due event id on the record.
        [Test]
        public void Observer_WaitingOnPassiveProcess_RecordsDueEventId()
        {
            var clock = new FakeClock { Now = 100f };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics(out diagnostics, clock);
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(0);

            WorkTask task = CreateAssignStart(authority, budgets, 0, Business(1));

            string reason;
            Assert.IsTrue(
                authority.MarkWaitingOnPassiveProcess(task.TaskId, "due-123", "dough proofing", out reason), reason);
            Assert.AreEqual("due-123", task.DueEventId);

            clock.Now = 700f;
            Assert.IsTrue(authority.ResumeTask(task.TaskId, "proofed", out reason), reason);

            // ResumeTask clears the task's DueEventId; the record must have captured it at entry.
            Assert.AreEqual(string.Empty, task.DueEventId);
            TaskTimingRecord record = diagnostics.GetRecord(task.TaskId);
            Assert.IsNotNull(record);
            Assert.AreEqual("due-123", record.LastDueEventId);
            Assert.AreEqual("due-123", record.LastBlockReason);
            Assert.AreEqual(600f, record.AccumulatedBlockedSeconds, 0.001f);
            Assert.AreEqual(600f, record.AccumulatedBlockedSecondsByReason["due-123"], 0.001f);
        }

        // Slice 1 / A6: with no observer installed, behaviour is unchanged and the
        // authority performs no clock read at all — the clock is consulted only to
        // serve an installed observer.
        [Test]
        public void NoObserverInstalled_BehaviourUnchanged()
        {
            var clock = new FakeClock { Now = 5000f };
            TaskAuthority authority = CreateAuthority();
            authority.SetTimingClock(clock.Read);

            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(1);

            string reason;
            WorkTask task = authority.CreateTask("test.sweep", Business(1), 1);
            Assert.IsTrue(authority.AssignTask(task.TaskId, Person(1), budgets, out reason), reason);
            Assert.IsTrue(authority.StartTask(task.TaskId, 1, out reason), reason);
            Assert.IsTrue(authority.RecordWork(task.TaskId, task.PlannedMinutes, budgets, 1, out reason), reason);

            Assert.AreEqual(TaskStatus.Complete, task.Status);
            Assert.AreEqual(TaskOutcome.Success, task.Outcome);
            Assert.AreEqual(1, task.CompletedDayIndex);
            Assert.AreEqual(0, clock.Reads, "no observer installed, so the clock must never be read");
        }

        // Slice 1 / §2.4: observer installed but no clock — records are day-only
        // precision, flagged as such, with day-start seconds.
        [Test]
        public void ClockAbsent_RecordsDayOnlyPrecision_Flagged()
        {
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics(out diagnostics, null);
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(2);

            string reason;
            WorkTask task = authority.CreateTask("test.sweep", Business(1), 2);
            Assert.IsTrue(authority.AssignTask(task.TaskId, Person(1), budgets, 2, out reason), reason);
            Assert.IsTrue(authority.StartTask(task.TaskId, 2, out reason), reason);

            TaskTimingRecord record = diagnostics.GetRecord(task.TaskId);
            Assert.IsNotNull(record);
            Assert.AreEqual(TaskTimingClockPrecision.DayOnly, record.ClockPrecision);
            Assert.AreEqual(2 * 86400f, record.CreatedSeconds, 0.001f, "day-only: day-start seconds");
            Assert.AreEqual(2, record.CreatedDay);
            Assert.AreEqual(2 * 86400f, record.AssignedSeconds, 0.001f);
            Assert.AreEqual(2 * 86400f, record.StartedSeconds, 0.001f);
            foreach (TaskTimingRecord each in diagnostics.GetAllRecords())
            {
                Assert.AreEqual(TaskTimingClockPrecision.DayOnly, each.ClockPrecision,
                    "degraded mode is explicit on every record, never silent");
            }
        }

        // Slice 1: interrupting an in-progress task preserves worked minutes, returns
        // it to Queued, and records the transition.
        [Test]
        public void Observer_Interruption_PreservesWorkedMinutes_AndRecordsTransition()
        {
            var clock = new FakeClock { Now = 1000f };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics(out diagnostics, clock);
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(0);

            WorkTask task = CreateAssignStart(authority, budgets, 0, Business(1));

            string reason;
            bool reachedCompletion;
            clock.Now = 1600f;
            Assert.IsTrue(
                authority.RecordWorkProgress(task.TaskId, 4, budgets, 0, out reachedCompletion, out reason), reason);
            Assert.IsTrue(!reachedCompletion, "4 of 10 planned minutes does not complete the task");

            clock.Now = 2200f;
            Assert.IsTrue(authority.InterruptTask(task.TaskId, budgets, out reason), reason);

            Assert.AreEqual(1, task.InterruptionCount);
            Assert.AreEqual(TaskStatus.Queued, task.Status);
            Assert.AreEqual(4, task.MinutesWorked, "work done so far is preserved across the interruption");

            TaskTimingRecord record = diagnostics.GetRecord(task.TaskId);
            Assert.IsNotNull(record);
            Assert.AreEqual("InterruptTask", record.LastTransitionKind);
            Assert.AreEqual(2200f, record.LastTransitionSeconds, 0.001f);
            Assert.AreEqual(4, record.MinutesWorkedLastSeen);
            Assert.AreEqual(1200f, record.AccumulatedActiveSeconds, 0.001f,
                "active from start (1000) to interruption (2200); the progress call at 1600 is not a state exit");
        }
    }
}
