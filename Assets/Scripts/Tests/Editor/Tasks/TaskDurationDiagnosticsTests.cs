using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Freight;
using LandLedgers.FirstLedger;
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

        // ---- Slice 2: expected vs actual + classification (plan §5 Slice 2) ----
        // All Slice 2 tests drive the real TaskAuthority with a fake advancing clock
        // and the real TaskDurationDiagnostics service as the installed observer, plus
        // the optional evidence lookups (supplier arrival, due-event due time, schedule
        // windows) fed in by the test. No log lines are emitted in this slice; Classify
        // returns verdicts to the caller. No test uses wall-clock time, DateTime,
        // Time.time, frame counts, or Update ticks.

        /// <summary>Absolute game seconds for a fixture day + seconds into that day (day-start convention, plan §1.1).</summary>
        private static float Day(int day, int secondsIntoDay)
        {
            return day * 86400f + secondsIntoDay;
        }

        private static TaskAuthority CreateAuthorityWithDiagnostics(string definitionId, int baseMinutes, out TaskDurationDiagnostics diagnostics, FakeClock clock)
        {
            var authority = new TaskAuthority();
            string reason;
            Assert.IsTrue(authority.RegisterDefinition(new TaskDefinition(definitionId, "Sweep", baseMinutes), out reason), reason);
            diagnostics = new TaskDurationDiagnostics();
            authority.SetTimingObserver(diagnostics);
            if (clock != null)
            {
                authority.SetTimingClock(clock.Read);
            }

            return authority;
        }

        // Slice 2 / M4 + §3.5 setup: 300 standard units -> FreightMinutes 15 -> the
        // assigned task's expected active duration is 900 s, not provisional.
        [Test]
        public void Classify_ExpectedDuration_UsesPlannedMinutes_AfterAssignment()
        {
            Assert.AreEqual(15, FreightCompanyRuntime.FreightMinutes(300, FreightGoodsClass.Standard),
                "ceil(300/20) = 15: the directive's example shape, through the real estimator math");

            var clock = new FakeClock { Now = Day(10, 28800) };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics("fr.load-freight", 1, out diagnostics, clock);
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(10);

            string reason;
            WorkTask task = authority.CreateTaskWithPlannedMinutes("fr.load-freight", Business(3), 10, 15, "ship-1");
            Assert.AreEqual(15, task.PlannedMinutes, "the override base is the planned minutes at creation");
            Assert.IsTrue(authority.AssignTask(task.TaskId, Person(1), budgets, 10, out reason), reason);
            Assert.AreEqual(15, task.PlannedMinutes,
                "the default estimator keeps the override base: EstimateMinutesForBase returns the base unchanged");

            TaskTimingVerdict verdict = diagnostics.Classify(diagnostics.GetRecord(task.TaskId), clock.Now, 10);
            Assert.IsNotNull(verdict);
            Assert.AreEqual(15, verdict.ExpectedMinutes);
            Assert.AreEqual(900f, verdict.ExpectedActiveSeconds, 0.001f);
            Assert.IsTrue(!verdict.IsProvisional, "assigned: the expectation is the booked PlannedMinutes, not provisional");
            Assert.AreEqual(TaskTimingVerdictKind.Legitimate, verdict.Verdict, "just assigned: nothing has stalled yet");
        }

        // Slice 2 / M4: before assignment the definition's BaseMinutes is the provisional
        // expectation, labelled provisional in the verdict.
        [Test]
        public void Classify_BeforeAssignment_UsesProvisionalBaseMinutes_LabelledProvisional()
        {
            var clock = new FakeClock { Now = Day(2, 28800) };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics(out diagnostics, clock);

            WorkTask task = authority.CreateTask("test.sweep", Business(1), 2);
            Assert.AreEqual(10, task.PlannedMinutes, "unassigned: PlannedMinutes is still the definition BaseMinutes");

            clock.Now = Day(2, 32400); // one hour later, still unassigned
            TaskTimingVerdict verdict = diagnostics.Classify(diagnostics.GetRecord(task.TaskId), clock.Now, 2);
            Assert.IsNotNull(verdict);
            Assert.AreEqual(10, verdict.ExpectedMinutes, "before assignment the definition BaseMinutes is the provisional expectation (M4)");
            Assert.AreEqual(600f, verdict.ExpectedActiveSeconds, 0.001f);
            Assert.IsTrue(verdict.IsProvisional, "unassigned: the expectation is provisional, labelled as such");
            Assert.AreEqual(TaskTimingVerdictKind.Legitimate, verdict.Verdict, "one hour queued: P1 grace (1 day) absorbs it");
        }

        // Slice 2 / §3.6: notions order placed day 1, arrival day 15; evaluated day 3
        // (47 h). L1 accrues the full wait; nothing is unexplained; active loading is 0.
        [Test]
        public void Classify_SupplierLeadTime_CoversFullWait_NoOverrun()
        {
            const string dependency = "notions order ord-7, arrives day 15";
            var clock = new FakeClock { Now = Day(1, 28800) };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics(out diagnostics, clock);
            diagnostics.SupplierArrivalLookup = id => id == dependency ? (int?)15 : null;
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(1);

            WorkTask task = CreateAssignStart(authority, budgets, 1, Business(1));
            string reason;
            Assert.IsTrue(authority.MarkWaitingOnDependency(task.TaskId, dependency, "notions in transit", out reason), reason);

            clock.Now = Day(3, 25200); // day 3 07:00: exactly 47 h after the day-1 08:00 order
            TaskTimingVerdict verdict = diagnostics.Classify(diagnostics.GetRecord(task.TaskId), clock.Now, 3);
            Assert.IsNotNull(verdict);
            Assert.AreEqual(TaskTimingVerdictKind.Legitimate, verdict.Verdict);
            Assert.AreEqual(TaskTimingReasonClass.L1SupplierLeadTime, verdict.ReasonClass);
            Assert.AreEqual(47f * 3600f, verdict.CoveredSeconds, 0.001f,
                "L1 accrues the full 47 h (arrival day 15 is in the future)");
            Assert.AreEqual(0f, verdict.UncoveredSeconds, 0.001f,
                "nothing unexplained: this must never read as 47 h of active loading");
            StringAssert.Contains("ord-7", verdict.EvidenceText);
            StringAssert.Contains("15", verdict.EvidenceText);
            TaskTimingRecord record = diagnostics.GetRecord(task.TaskId);
            Assert.AreEqual(0f, record.AccumulatedActiveSeconds, 0.001f,
                "active loading seconds are 0: the wait is blocked time, not active time");
        }

        // Slice 2 / §3.2 L1 + §3.6: the same wait, clock advanced past the arrival day
        // with no receipt. L1 stops accruing at the end of arrival day 15; only the
        // post-arrival span (day 16 so far) is unexplained.
        [Test]
        public void Classify_ExpiredLeadTime_BecomesUnexplained()
        {
            const string dependency = "notions order ord-7, arrives day 15";
            var clock = new FakeClock { Now = Day(1, 28800) };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics(out diagnostics, clock);
            diagnostics.SupplierArrivalLookup = id => id == dependency ? (int?)15 : null;
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(1);

            WorkTask task = CreateAssignStart(authority, budgets, 1, Business(1));
            string reason;
            Assert.IsTrue(authority.MarkWaitingOnDependency(task.TaskId, dependency, "notions in transit", out reason), reason);

            clock.Now = Day(16, 28800); // day 16 08:00: the arrival day (15) has passed with no receipt
            TaskTimingVerdict verdict = diagnostics.Classify(diagnostics.GetRecord(task.TaskId), clock.Now, 16);
            Assert.IsNotNull(verdict);
            Assert.AreEqual(TaskTimingVerdictKind.Unexplained, verdict.Verdict);
            Assert.AreEqual(TaskTimingReasonClass.L1SupplierLeadTime, verdict.ReasonClass,
                "the covering class is still L1; the post-arrival span is what is unexplained");
            Assert.AreEqual(1267200f, verdict.CoveredSeconds, 0.001f,
                "L1 covers up to the end of arrival day 15: 16*86400 - Day(1,28800)");
            Assert.AreEqual(28800f, verdict.UncoveredSeconds, 0.001f,
                "only the day-16 span (08:00 so far) is unexplained");
            StringAssert.Contains("15", verdict.EvidenceText);
        }

        // Slice 2 / §3.5: the 15-minute cargo loading task stuck two days. P1 and P2
        // both true, verdict Unexplained, uncovered seconds 172800 against the 2700 s
        // P2 threshold.
        [Test]
        public void Classify_InProgressNoProgress_TwoDays_Unexplained()
        {
            Assert.AreEqual(15, FreightCompanyRuntime.FreightMinutes(300, FreightGoodsClass.Standard));
            var clock = new FakeClock { Now = Day(10, 28800) };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics("fr.load-freight", 1, out diagnostics, clock);
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(10);

            string reason;
            WorkTask task = authority.CreateTaskWithPlannedMinutes("fr.load-freight", Business(3), 10, 15, "ship-1");
            Assert.IsTrue(authority.AssignTask(task.TaskId, Person(1), budgets, 10, out reason), reason);
            Assert.IsTrue(authority.StartTask(task.TaskId, 10, out reason), reason);
            Assert.AreEqual(15, task.PlannedMinutes);

            // Day 12, 08:00: still InProgress, 0 minutes worked, no reason recorded anywhere.
            clock.Now = Day(12, 28800);
            Assert.AreEqual(0, task.MinutesWorked);
            TaskTimingRecord record = diagnostics.GetRecord(task.TaskId);
            TaskTimingVerdict verdict = diagnostics.Classify(record, clock.Now, 12);
            Assert.IsNotNull(verdict);
            Assert.AreEqual(TaskTimingVerdictKind.Unexplained, verdict.Verdict);
            Assert.AreEqual(TaskTimingReasonClass.NoneRecorded, verdict.ReasonClass);
            Assert.AreEqual(0f, verdict.CoveredSeconds, 0.001f);
            Assert.AreEqual(172800f, verdict.UncoveredSeconds, 0.001f,
                "the whole 2-day span is unexplained; the P2 threshold is 900*2.0+900 = 2700 s");
            Assert.AreEqual(900f, verdict.ExpectedActiveSeconds, 0.001f);
            Assert.AreEqual(Day(10, 28800), verdict.LastProgressSeconds, 0.001f,
                "last progress is the start: no RecordWork ever happened");
        }

        // Slice 2 / §3.2 L4: a named dependency legitimates the wait. The same shape
        // with a future arrival day fed in classifies L1 instead (the lookup is what
        // promotes a named wait to a dated supplier lead).
        [Test]
        public void Classify_WaitingOnDependency_NamedBlocker_Legitimate()
        {
            const string dependency = "flour delivery #42, miller wagon";
            var clock = new FakeClock { Now = Day(4, 28800) };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics(out diagnostics, clock);
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(4);

            WorkTask task = CreateAssignStart(authority, budgets, 4, Business(1));
            string reason;
            Assert.IsTrue(authority.MarkWaitingOnDependency(task.TaskId, dependency, "no flour", out reason), reason);

            clock.Now = Day(4, 36000); // 2 h later
            TaskTimingVerdict l4 = diagnostics.Classify(diagnostics.GetRecord(task.TaskId), clock.Now, 4);
            Assert.IsNotNull(l4);
            Assert.AreEqual(TaskTimingVerdictKind.Legitimate, l4.Verdict);
            Assert.AreEqual(TaskTimingReasonClass.L4WaitingOnDependency, l4.ReasonClass);
            Assert.AreEqual(7200f, l4.CoveredSeconds, 0.001f);
            Assert.AreEqual(0f, l4.UncoveredSeconds, 0.001f);
            StringAssert.Contains("flour delivery #42", l4.EvidenceText);

            diagnostics.SupplierArrivalLookup = id => id == dependency ? (int?)9 : null;
            TaskTimingVerdict l1 = diagnostics.Classify(diagnostics.GetRecord(task.TaskId), clock.Now, 4);
            Assert.IsNotNull(l1);
            Assert.AreEqual(TaskTimingVerdictKind.Legitimate, l1.Verdict);
            Assert.AreEqual(TaskTimingReasonClass.L1SupplierLeadTime, l1.ReasonClass,
                "a future arrival day promotes the named wait to L1");
            Assert.AreEqual(7200f, l1.CoveredSeconds, 0.001f);
            Assert.AreEqual(0f, l1.UncoveredSeconds, 0.001f);
        }

        // Slice 2 / §3.2 L4: Paused with no reason recorded anywhere is unexplained.
        // PauseTask takes a reason string but stores it nowhere on the task (verified in
        // TaskAuthorityLifecycle.PauseTask: only MarkPaused() is called), so the record
        // holds only "Paused" and the pause explains nothing.
        [Test]
        public void Classify_PausedWithoutReason_Unexplained()
        {
            var clock = new FakeClock { Now = Day(0, 28800) };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics(out diagnostics, clock);
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(0);

            WorkTask task = CreateAssignStart(authority, budgets, 0, Business(1));
            string reason;
            Assert.IsTrue(authority.PauseTask(task.TaskId, budgets, "flour delivery late", out reason), reason);

            clock.Now = Day(2, 28800); // two days paused, no resume, no reason recorded anywhere
            TaskTimingVerdict verdict = diagnostics.Classify(diagnostics.GetRecord(task.TaskId), clock.Now, 2);
            Assert.IsNotNull(verdict);
            Assert.AreEqual(TaskTimingVerdictKind.Unexplained, verdict.Verdict);
            Assert.AreEqual(TaskTimingReasonClass.NoneRecorded, verdict.ReasonClass);
            Assert.AreEqual(0f, verdict.CoveredSeconds, 0.001f);
            Assert.AreEqual(172800f, verdict.UncoveredSeconds, 0.001f);
        }

        // Slice 2 / §3.2 L3: one night under a Sleep reservation 1320->420, reserved as
        // two windows per day by DailyActivityPlanner (evening 1320+120, morning 0+420,
        // both on the same day index). L3 covers exactly the 540 sleep minutes inside
        // the InProgress span; the rest is evaluated normally; a single night never
        // warns because P1 grace (1 day) absorbs exactly 24 h with no progress.
        [Test]
        public void Classify_SleepWindow_CoversNightHoursOnly()
        {
            var clock = new FakeClock { Now = Day(0, 28800) };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics(out diagnostics, clock);
            diagnostics.ScheduleLookup = (personId, dayIndex) =>
            {
                if (personId != 1)
                {
                    return null;
                }

                return new List<TaskDurationSleepWindow>
                {
                    new TaskDurationSleepWindow { StartMinuteOfDay = 0, DurationMinutes = 420 },
                    new TaskDurationSleepWindow { StartMinuteOfDay = 1320, DurationMinutes = 120 },
                };
            };
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(0);

            WorkTask task = CreateAssignStart(authority, budgets, 0, Business(1));

            clock.Now = Day(1, 28800); // next morning, exactly 24 h later
            TaskTimingVerdict verdict = diagnostics.Classify(diagnostics.GetRecord(task.TaskId), clock.Now, 1);
            Assert.IsNotNull(verdict);
            Assert.AreEqual(TaskTimingVerdictKind.Legitimate, verdict.Verdict,
                "a single night never warns: P1 grace (1 day) absorbs exactly 24 h with no progress");
            Assert.AreEqual(TaskTimingReasonClass.L3RestSleep, verdict.ReasonClass);
            Assert.AreEqual(32400f, verdict.CoveredSeconds, 0.001f,
                "L3 covers exactly the sleep minutes: 120 evening + 420 morning");
            Assert.AreEqual(54000f, verdict.UncoveredSeconds, 0.001f,
                "86400 s open span - 32400 s sleep = 54000 s evaluated normally");
        }

        // Slice 2 / §3.3 + §3.7: hitch task assigned day 6; every day the worker's
        // 600-minute budget is consumed by horse-feed travel tasks, so the start is
        // refused daily. Streaks 1-3 (days 6-8) are legitimate; from streak 4 (day 9)
        // it escalates, quoting the repeated reason — it does not go silent and is
        // not reclassified as unexplained.
        [Test]
        public void Classify_RepeatedBudgetRefusal_EscalatesAfterEscalationDays()
        {
            const string refusal = "Cannot start task: worker has 0 minutes remaining (budget consumed by horse-feed travel tasks)";
            var clock = new FakeClock { Now = Day(6, 28800) };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics(out diagnostics, clock);
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(6);

            string reason;
            WorkTask task = authority.CreateTask("test.sweep", Business(1), 6);
            Assert.IsTrue(authority.AssignTask(task.TaskId, Person(1), budgets, 6, out reason), reason);
            Assert.AreEqual(TaskStatus.Assigned, task.Status);

            for (int day = 6; day <= 15; day++)
            {
                clock.Now = Day(day, 28800);
                budgets.EnsureDay(day);
                diagnostics.RecordRefusal(task.TaskId.ToString(), "StartTask", refusal, day);
                TaskTimingVerdict verdict = diagnostics.Classify(diagnostics.GetRecord(task.TaskId), clock.Now, day);
                Assert.IsNotNull(verdict, "day " + day);
                Assert.AreEqual(TaskTimingReasonClass.L6BudgetScheduleRefusal, verdict.ReasonClass, "day " + day);
                StringAssert.Contains("horse-feed", verdict.EvidenceText, "day " + day);
                if (day <= 8)
                {
                    Assert.AreEqual(TaskTimingVerdictKind.Legitimate, verdict.Verdict,
                        "day " + day + ": streak " + (day - 5) + " is within EscalationDays 3");
                    Assert.AreEqual(0f, verdict.UncoveredSeconds, 0.001f, "day " + day);
                }
                else
                {
                    Assert.AreEqual(TaskTimingVerdictKind.Escalated, verdict.Verdict,
                        "day " + day + ": streak " + (day - 5) + " exceeds EscalationDays 3");
                    Assert.AreEqual(0f, verdict.UncoveredSeconds, 0.001f,
                        "day " + day + ": escalation does not reclassify the stall as unexplained");
                }
            }
        }

        // Slice 2 / §3.4: freight-style incremental work — RecordWorkProgress a little
        // each day for 5 days. Progress in the last evaluation interval suppresses the
        // stuck warning (P1 false every day), however long the total span.
        [Test]
        public void Classify_ProgressSuppressesStuckWarning()
        {
            var clock = new FakeClock { Now = Day(0, 28800) };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics("test.sweep", 60, out diagnostics, clock);
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(0);

            WorkTask task = CreateAssignStart(authority, budgets, 0, Business(1));
            Assert.AreEqual(60, task.PlannedMinutes);

            for (int day = 0; day <= 4; day++)
            {
                clock.Now = Day(day, 43200); // midday each day
                budgets.EnsureDay(day);
                string reason;
                bool reachedCompletion;
                Assert.IsTrue(authority.RecordWorkProgress(task.TaskId, 2, budgets, day, out reachedCompletion, out reason), reason);
                TaskTimingVerdict verdict = diagnostics.Classify(diagnostics.GetRecord(task.TaskId), clock.Now, day);
                Assert.IsNotNull(verdict, "day " + day);
                Assert.AreEqual(TaskTimingVerdictKind.Legitimate, verdict.Verdict,
                    "day " + day + ": progress today resets P1, so the task never warns however long its span");
                Assert.AreEqual(Day(day, 43200), verdict.LastProgressSeconds, 0.001f, "day " + day);
            }

            Assert.AreEqual(10, task.MinutesWorked, "2 min/day x 5 days");
            Assert.AreEqual(TaskStatus.InProgress, task.Status, "RecordWorkProgress never completes by itself");
        }

        // Slice 2 / §2.1: queue + assigned-waiting + active + blocked + travel sums to
        // the open span within one short tick (900 s). Driven through queue -> assign
        // -> start -> wait -> resume -> complete at distinct clock times, so the sum
        // is exact here.
        [Test]
        public void Classify_RecordArithmetic_SumsToOpenSpan()
        {
            var clock = new FakeClock { Now = 1000f };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics(out diagnostics, clock);
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(0);

            string reason;
            WorkTask task = authority.CreateTask("test.sweep", Business(1), 0); // t=1000, Queued
            clock.Now = 4600f;
            Assert.IsTrue(authority.AssignTask(task.TaskId, Person(1), budgets, 0, out reason), reason); // queue 3600
            clock.Now = 8200f;
            Assert.IsTrue(authority.StartTask(task.TaskId, 0, out reason), reason); // assigned 3600
            clock.Now = 11800f;
            Assert.IsTrue(authority.MarkWaitingOnDependency(task.TaskId, "flour delivery #42", "no flour", out reason), reason); // active 3600
            clock.Now = 19000f;
            Assert.IsTrue(authority.ResumeTask(task.TaskId, "delivery arrived", out reason), reason); // blocked 7200
            clock.Now = 22600f;
            Assert.IsTrue(authority.CompleteTask(task.TaskId, TaskOutcome.Success, 0, budgets)); // active 3600 more

            TaskTimingRecord record = diagnostics.GetRecord(task.TaskId);
            Assert.IsNotNull(record);
            float openSpan = record.CompletedSeconds - record.CreatedSeconds;
            Assert.AreEqual(21600f, openSpan, 0.001f);
            float bucketSum = record.AccumulatedQueueSeconds + record.AccumulatedAssignedWaitingSeconds
                + record.AccumulatedActiveSeconds + record.AccumulatedBlockedSeconds + record.AccumulatedTravelSeconds;
            Assert.AreEqual(21600f, bucketSum, 0.001f, "buckets sum exactly here; the plan allows 900 s");
            Assert.IsTrue(Math.Abs(bucketSum - openSpan) <= 900f,
                "§2.1: decomposition sums to the open span within one short tick");
            Assert.AreEqual(3600f, record.AccumulatedQueueSeconds, 0.001f);
            Assert.AreEqual(3600f, record.AccumulatedAssignedWaitingSeconds, 0.001f);
            Assert.AreEqual(7200f, record.AccumulatedActiveSeconds, 0.001f);
            Assert.AreEqual(7200f, record.AccumulatedBlockedSeconds, 0.001f);
        }

        // ---- Slice 3: warning emission + bounding / rate-limiting (plan §5 Slice 3) ----
        // All Slice 3 tests drive the real TaskAuthority with a fake advancing clock
        // and the real TaskDurationDiagnostics service as the installed observer, with
        // the log sink bound to a capturing List<string> instead of Debug.LogWarning.
        // No driver wiring exists yet: Evaluate is called by the tests, once per day,
        // plus once at completion. These tests are written FIRST (reproducing-first):
        // they fail to build before the Slice 3 implementation (no Evaluate / LogSink)
        // and must pass after it.

        /// <summary>Binds the service's log sink to a capturing list; returns the list.</summary>
        private static List<string> CaptureLines(TaskDurationDiagnostics diagnostics)
        {
            var lines = new List<string>();
            diagnostics.LogSink = lines.Add;
            return lines;
        }

        private static int CountLinesStartingWith(List<string> lines, string prefix)
        {
            int count = 0;
            foreach (string line in lines)
            {
                if (line.StartsWith(prefix, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        private static void RegisterDefinition(TaskAuthority authority, string definitionId, int baseMinutes)
        {
            string reason;
            Assert.IsTrue(authority.RegisterDefinition(new TaskDefinition(definitionId, "Sweep", baseMinutes), out reason), reason);
        }

        /// <summary>Creates, assigns and starts a task at the fake clock's current time; the task is then left un-progressed (stuck).</summary>
        private static WorkTask StartStuckTask(TaskAuthority authority, WorkTimeBudgetStore budgets, string definitionId, EntityId owner, EntityId worker, int day)
        {
            string reason;
            WorkTask task = authority.CreateTask(definitionId, owner, day);
            Assert.IsTrue(authority.AssignTask(task.TaskId, worker, budgets, day, out reason), reason);
            Assert.IsTrue(authority.StartTask(task.TaskId, day, out reason), reason);
            return task;
        }

        // Slice 3 / §3.5 + §4.1: the 15-minute cargo loading task stuck two days emits
        // exactly one warning line carrying every §4 field in fixed order.
        [Test]
        public void Warn_FifteenMinuteLoad_TwoDays_EmitsActionableWarning()
        {
            Assert.AreEqual(15, FreightCompanyRuntime.FreightMinutes(300, FreightGoodsClass.Standard));
            var clock = new FakeClock { Now = Day(10, 28800) };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics("fr.load-freight", 1, out diagnostics, clock);
            List<string> lines = CaptureLines(diagnostics);
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(10);

            string reason;
            WorkTask task = authority.CreateTaskWithPlannedMinutes("fr.load-freight", Business(3), 10, 15, "ship-1");
            Assert.IsTrue(authority.AssignTask(task.TaskId, Person(1), budgets, 10, out reason), reason);
            Assert.IsTrue(authority.StartTask(task.TaskId, 10, out reason), reason);
            Assert.AreEqual(15, task.PlannedMinutes);

            // Day 12, 08:00: still InProgress, 0 minutes worked, no reason recorded anywhere.
            clock.Now = Day(12, 28800);
            diagnostics.Evaluate(clock.Now, 12);

            Assert.AreEqual(1, CountLinesStartingWith(lines, "[TaskDuration] overrun day 12:"),
                "exactly one overrun warning line; the daily summary is a separate line");
            Assert.AreEqual(1, CountLinesStartingWith(lines, "[TaskDuration] summary day 12:"),
                "exactly one daily summary line");
            string warning = null;
            foreach (string line in lines)
            {
                if (line.StartsWith("[TaskDuration] overrun", StringComparison.Ordinal))
                {
                    warning = line;
                }
            }

            Assert.IsNotNull(warning);
            string expected = "[TaskDuration] overrun day 12: task " + task.TaskId.ToString()
                + " 'fr.load-freight' owner " + Business(3).ToString()
                + " worker " + Person(1).ToString()
                + " expected 15min active 2880min (172800s, worked 0/15min)"
                + " queue 0min blocked 0min (none recorded: no reason recorded)"
                + " travel 0min (none linked)"
                + " last-transition StartTask day 10 (172800s ago)"
                + " state InProgress (Active work in progress.) outcome open";
            Assert.AreEqual(expected, warning,
                "every §4.1 field in fixed order: task, definition, owner, worker, expected, active, worked, queue, blocked, travel, last transition, state, outcome");
        }

        // Slice 3 / §3.6: the legitimate two-day supplier delay emits NO warning line,
        // and no emitted line mislabels the 47 h wait as active loading.
        [Test]
        public void Warn_LegitimateTwoDaySupplierDelay_EmitsNoActiveLoadingWarning()
        {
            const string dependency = "notions order ord-7, arrives day 15";
            var clock = new FakeClock { Now = Day(1, 28800) };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics(out diagnostics, clock);
            diagnostics.SupplierArrivalLookup = id => id == dependency ? (int?)15 : null;
            List<string> lines = CaptureLines(diagnostics);
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(1);

            WorkTask task = CreateAssignStart(authority, budgets, 1, Business(1));
            string reason;
            Assert.IsTrue(authority.MarkWaitingOnDependency(task.TaskId, dependency, "notions in transit", out reason), reason);

            clock.Now = Day(3, 25200); // day 3 07:00: exactly 47 h after the day-1 08:00 order
            diagnostics.Evaluate(clock.Now, 3);

            Assert.AreEqual(0, lines.Count,
                "a legitimate L1 wait warns nothing and earns no summary line either: no overrun is open");
            string joined = string.Join("\n", lines.ToArray());
            Assert.IsTrue(joined.IndexOf("2820min", StringComparison.Ordinal) < 0,
                "no line may present the 47 h supplier wait as active loading minutes");
            Assert.AreEqual(TaskTimingVerdictKind.Legitimate,
                diagnostics.Classify(diagnostics.GetRecord(task.TaskId), clock.Now, 3).Verdict);
        }

        // Slice 3 / §4.2: one warning per task per day (day-keyed, LogRefusalDayKeyed
        // precedent). Evaluating twice on the same day warns once; the next day warns again.
        [Test]
        public void Warn_SameTask_WarnsAtMostOncePerDay()
        {
            var clock = new FakeClock { Now = Day(10, 28800) };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics(out diagnostics, clock);
            List<string> lines = CaptureLines(diagnostics);
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(10);

            StartStuckTask(authority, budgets, "test.sweep", Business(1), Person(1), 10);

            clock.Now = Day(12, 28800);
            diagnostics.Evaluate(clock.Now, 12);
            diagnostics.Evaluate(clock.Now, 12);
            Assert.AreEqual(1, CountLinesStartingWith(lines, "[TaskDuration] overrun"),
                "two evaluations on the same day: one warning line");
            Assert.AreEqual(1, CountLinesStartingWith(lines, "[TaskDuration] summary"),
                "the daily summary is also emitted at most once per day");

            clock.Now = Day(13, 28800);
            diagnostics.Evaluate(clock.Now, 13);
            Assert.AreEqual(2, CountLinesStartingWith(lines, "[TaskDuration] overrun"),
                "the next day warns again (a repeating warning, not a duplicate)");
            Assert.AreEqual(2, CountLinesStartingWith(lines, "[TaskDuration] summary"));
        }

        // Slice 3 / §4.2: the global cap (12/day) binds before the log floods. 20
        // overrunning tasks on distinct definitions (so the per-definition cap cannot
        // bind first): at most 12 warning lines + 1 summary, and the summary's
        // omittedCount accounts for the other 8.
        [Test]
        public void Warn_GlobalCap_SummaryCountsOmitted()
        {
            var clock = new FakeClock { Now = Day(10, 28800) };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics(out diagnostics, clock);
            List<string> lines = CaptureLines(diagnostics);
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(10);

            for (int i = 0; i < 20; i++)
            {
                RegisterDefinition(authority, "test.cap" + i, 10);
                StartStuckTask(authority, budgets, "test.cap" + i, Business(1), Person(100 + i), 10);
            }

            clock.Now = Day(12, 28800);
            diagnostics.Evaluate(clock.Now, 12);

            Assert.AreEqual(12, CountLinesStartingWith(lines, "[TaskDuration] overrun"),
                "MaxWarningsPerDay 12: at most 12 warning lines across all tasks");
            Assert.AreEqual(1, CountLinesStartingWith(lines, "[TaskDuration] summary"),
                "exactly one daily summary line");
            string summary = null;
            foreach (string line in lines)
            {
                if (line.StartsWith("[TaskDuration] summary", StringComparison.Ordinal))
                {
                    summary = line;
                }
            }

            Assert.IsNotNull(summary);
            StringAssert.Contains("20 open overrun(s)", summary);
            StringAssert.Contains("12 new", summary);
            StringAssert.Contains("0 repeating", summary);
            StringAssert.Contains("8 omitted by cap", summary,
                "the 8 tasks over the cap are counted in omittedCount, never silently dropped");
        }

        // Slice 3 / §4.2: the per-definition cap (3/day) stops one broken definition
        // family from flooding the log. 5 overrunning tasks of one definition: at most
        // 3 lines for that definition that day, 2 omitted.
        [Test]
        public void Warn_PerDefinitionCap_Applies()
        {
            var clock = new FakeClock { Now = Day(10, 28800) };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics(out diagnostics, clock);
            List<string> lines = CaptureLines(diagnostics);
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(10);

            for (int i = 0; i < 5; i++)
            {
                StartStuckTask(authority, budgets, "test.sweep", Business(1), Person(200 + i), 10);
            }

            clock.Now = Day(12, 28800);
            diagnostics.Evaluate(clock.Now, 12);

            Assert.AreEqual(3, CountLinesStartingWith(lines, "[TaskDuration] overrun"),
                "MaxWarningsPerDefinitionPerDay 3: at most 3 lines for one definition per day");
            Assert.AreEqual(1, CountLinesStartingWith(lines, "[TaskDuration] summary"));
            string summary = null;
            foreach (string line in lines)
            {
                if (line.StartsWith("[TaskDuration] summary", StringComparison.Ordinal))
                {
                    summary = line;
                }
            }

            Assert.IsNotNull(summary);
            StringAssert.Contains("5 open overrun(s)", summary);
            StringAssert.Contains("2 omitted by cap", summary);
        }

        // Slice 3 / §4.2: a terminal overrun is logged once at completion (as a
        // completed-overrun line) and never re-warned on later days.
        [Test]
        public void Warn_CompletedTask_WarnsOnce_ThenSilent()
        {
            var clock = new FakeClock { Now = Day(10, 28800) };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics("fr.load-freight", 1, out diagnostics, clock);
            List<string> lines = CaptureLines(diagnostics);
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(10);

            string reason;
            WorkTask task = authority.CreateTaskWithPlannedMinutes("fr.load-freight", Business(3), 10, 15, "ship-1");
            Assert.IsTrue(authority.AssignTask(task.TaskId, Person(1), budgets, 10, out reason), reason);
            Assert.IsTrue(authority.StartTask(task.TaskId, 10, out reason), reason);

            clock.Now = Day(12, 28800);
            Assert.IsTrue(authority.CompleteTask(task.TaskId, TaskOutcome.Success, 12, budgets),
                "completion at day 12 with 0 minutes worked still completes (Slice 1 precedent)");
            Assert.AreEqual(TaskStatus.Complete, task.Status);

            diagnostics.Evaluate(clock.Now, 12);
            Assert.AreEqual(1, CountLinesStartingWith(lines, "[TaskDuration] completed-overrun"),
                "exactly one terminal warning, once at completion");
            Assert.AreEqual(0, CountLinesStartingWith(lines, "[TaskDuration] overrun day"),
                "a terminal task never also warns as an open overrun");
            string terminal = null;
            foreach (string line in lines)
            {
                if (line.StartsWith("[TaskDuration] completed-overrun", StringComparison.Ordinal))
                {
                    terminal = line;
                }
            }

            Assert.IsNotNull(terminal);
            StringAssert.Contains("completed-overrun day 12:", terminal);
            StringAssert.Contains("state Complete (Finished; outcome recorded.)", terminal);
            StringAssert.Contains("outcome Success", terminal);
            StringAssert.Contains("expected 15min", terminal);
            StringAssert.Contains("active 2880min (172800s, worked 0/15min)", terminal);

            clock.Now = Day(13, 28800);
            diagnostics.Evaluate(clock.Now, 13);
            Assert.AreEqual(1, CountLinesStartingWith(lines, "[TaskDuration] completed-overrun"),
                "later evaluations emit nothing more for the terminal task");
            Assert.AreEqual(2, lines.Count,
                "day 13 adds nothing: the day-12 lines are the completed-overrun line plus its summary line");
            string day12Summary = null;
            foreach (string line in lines)
            {
                if (line.StartsWith("[TaskDuration] summary day 12", StringComparison.Ordinal))
                {
                    day12Summary = line;
                }
            }

            Assert.AreEqual(
                "[TaskDuration] summary day 12: 0 open overrun(s) (0 new, 0 repeating, 0 omitted by cap); longest open: none",
                day12Summary,
                "a terminal-only day still earns a summary (§4.1: an overrun was warned that day), with no open overrun to name");
            Assert.AreEqual(0, CountLinesStartingWith(lines, "[TaskDuration] summary day 13"),
                "day 13 has no open overrun and warns nothing, so no summary line");
        }

        // Slice 3 / §4.2: the record store is bounded (MaxTrackedRecords, test-sized):
        // terminal records are evicted first, open records are never evicted to make
        // room, and the eviction count is reported on the daily summary line.
        [Test]
        public void Warn_RecordStore_Bounded_EvictsTerminalFirst()
        {
            var clock = new FakeClock { Now = Day(10, 28800) };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics(out diagnostics, clock);
            diagnostics.MaxTrackedRecords = 5;
            List<string> lines = CaptureLines(diagnostics);
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(10);

            var terminalIds = new List<EntityId>();
            for (int i = 0; i < 5; i++)
            {
                string reason;
                WorkTask task = authority.CreateTask("test.sweep", Business(1), 10);
                Assert.IsTrue(authority.AssignTask(task.TaskId, Person(1), budgets, 10, out reason), reason);
                Assert.IsTrue(authority.StartTask(task.TaskId, 10, out reason), reason);
                Assert.IsTrue(authority.RecordWork(task.TaskId, task.PlannedMinutes, budgets, 10, out reason), reason);
                Assert.AreEqual(TaskStatus.Complete, task.Status);
                terminalIds.Add(task.TaskId);
            }

            Assert.AreEqual(5, diagnostics.RecordCount, "the store holds the 5 terminal records");
            WorkTask openA = StartStuckTask(authority, budgets, "test.sweep", Business(1), Person(1), 10);
            WorkTask openB = StartStuckTask(authority, budgets, "test.sweep", Business(1), Person(2), 10);

            Assert.AreEqual(5, diagnostics.RecordCount, "the store never grows past MaxTrackedRecords");
            Assert.AreEqual(2, diagnostics.EvictedRecordCount, "two terminal records were evicted to make room");
            Assert.IsNull(diagnostics.GetRecord(terminalIds[0]), "oldest terminal evicted first");
            Assert.IsNull(diagnostics.GetRecord(terminalIds[1]), "second-oldest terminal evicted next");
            Assert.IsNotNull(diagnostics.GetRecord(terminalIds[4]), "the newest terminal survives while older terminals remain to evict");
            Assert.IsNotNull(diagnostics.GetRecord(openA.TaskId), "an open record is never evicted");
            Assert.IsNotNull(diagnostics.GetRecord(openB.TaskId), "an open record is never evicted");

            clock.Now = Day(12, 28800);
            diagnostics.Evaluate(clock.Now, 12);
            string summary = null;
            foreach (string line in lines)
            {
                if (line.StartsWith("[TaskDuration] summary", StringComparison.Ordinal))
                {
                    summary = line;
                }
            }

            Assert.IsNotNull(summary);
            StringAssert.Contains("evicted 2 record(s)", summary,
                "the eviction count is reported on the summary line (§4.2)");
        }

        // Slice 3 / §4.2: a diagnostic bug must never abort a day drive. A record whose
        // classification throws (here: the supplier-arrival lookup throws inside
        // Classify) is skipped, Evaluate returns without an exception, at most one
        // "[TaskDuration] evaluation failed" line is logged per day, and the other
        // overrunning task still warns.
        [Test]
        public void Warn_EvaluateNeverThrows()
        {
            var clock = new FakeClock { Now = Day(10, 28800) };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics(out diagnostics, clock);
            diagnostics.SupplierArrivalLookup = id => { throw new InvalidOperationException("test lookup failure"); };
            List<string> lines = CaptureLines(diagnostics);
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(10);

            WorkTask throwing = CreateAssignStart(authority, budgets, 10, Business(1));
            string reason;
            Assert.IsTrue(
                authority.MarkWaitingOnDependency(throwing.TaskId, "order xyz", "waiting", out reason), reason);

            // A second, plain stuck task whose classification never touches the lookup.
            StartStuckTask(authority, budgets, "test.sweep", Business(1), Person(2), 10);

            clock.Now = Day(12, 28800);
            Assert.DoesNotThrow(() => diagnostics.Evaluate(clock.Now, 12),
                "Evaluate must never throw into its caller (the day drive)");
            Assert.AreEqual(1, CountLinesStartingWith(lines, "[TaskDuration] evaluation failed"),
                "at most one evaluation-failed line per day");
            Assert.AreEqual(1, CountLinesStartingWith(lines, "[TaskDuration] overrun"),
                "the other overrunning task still warns: one bad record does not abort the pass");

            diagnostics.Evaluate(clock.Now, 12);
            Assert.AreEqual(1, CountLinesStartingWith(lines, "[TaskDuration] evaluation failed"),
                "a second evaluation the same day logs no second failed line");
        }

        // Slice 3 / §4.1: the daily summary line's exact shape — open count, new /
        // repeating / omitted split, longest open with definition + days + reason class —
        // and the new/repeating split across days.
        [Test]
        public void Warn_DailySummaryLine_Shape()
        {
            var clock = new FakeClock { Now = Day(10, 28800) };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics("fr.load-freight", 1, out diagnostics, clock);
            List<string> lines = CaptureLines(diagnostics);
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(10);

            string reason;
            WorkTask first = authority.CreateTaskWithPlannedMinutes("fr.load-freight", Business(3), 10, 15, "ship-1");
            Assert.IsTrue(authority.AssignTask(first.TaskId, Person(1), budgets, 10, out reason), reason);
            Assert.IsTrue(authority.StartTask(first.TaskId, 10, out reason), reason);
            RegisterDefinition(authority, "test.sweep", 10);
            WorkTask second = StartStuckTask(authority, budgets, "test.sweep", Business(1), Person(2), 10);

            clock.Now = Day(12, 28800);
            diagnostics.Evaluate(clock.Now, 12);
            Assert.AreEqual(1, CountLinesStartingWith(lines, "[TaskDuration] summary"),
                "exactly one summary line on day 12");
            string day12 = null;
            foreach (string line in lines)
            {
                if (line.StartsWith("[TaskDuration] summary", StringComparison.Ordinal))
                {
                    day12 = line;
                }
            }

            Assert.IsNotNull(day12);
            Assert.AreEqual(
                "[TaskDuration] summary day 12: 2 open overrun(s) (2 new, 0 repeating, 0 omitted by cap); longest open: task "
                + first.TaskId.ToString() + " 'fr.load-freight' 2d, expected 15min, none recorded",
                day12);

            clock.Now = Day(13, 28800);
            diagnostics.Evaluate(clock.Now, 13);
            Assert.AreEqual(2, CountLinesStartingWith(lines, "[TaskDuration] summary"),
                "one summary line per day");
            string day13 = null;
            foreach (string line in lines)
            {
                if (line.StartsWith("[TaskDuration] summary day 13", StringComparison.Ordinal))
                {
                    day13 = line;
                }
            }

            Assert.IsNotNull(day13);
            Assert.AreEqual(
                "[TaskDuration] summary day 13: 2 open overrun(s) (0 new, 2 repeating, 0 omitted by cap); longest open: task "
                + first.TaskId.ToString() + " 'fr.load-freight' 3d, expected 15min, none recorded",
                day13);
            Assert.IsTrue(second.TaskId != first.TaskId, "the two tasks are distinct (sanity)");
        }
    }
}
