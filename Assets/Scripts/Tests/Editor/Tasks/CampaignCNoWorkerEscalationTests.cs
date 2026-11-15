using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Tasks
{
    /// <summary>
    /// CAMPAIGN-C, DEF-025 (owner Decision 11, Nov 15, 2026): the L8 no-worker
    /// escalation ladder — warn after approximately one simulated day without a
    /// viable worker, escalate after three days, raise a higher-priority exception
    /// after seven days if unresolved; duplicate log spam suppressed; the task
    /// recovers naturally when a suitable worker becomes available; the
    /// notification names the actual blocker; never hires or assigns anything.
    /// Tests only; no production code. Drives the real TaskAuthority with a fake
    /// advancing clock and the real TaskDurationDiagnostics service as the
    /// installed observer (the TaskDurationDiagnosticsTests idiom).
    /// </summary>
    [TestFixture]
    public sealed class CampaignCNoWorkerEscalationTests
    {
        private const string NoWorkers = "unassignable: no workers available";

        private static EntityId Person(int id)
        {
            return EntityId.For(EntityKind.Person, id);
        }

        private static EntityId Business(int id)
        {
            return EntityId.For(EntityKind.Business, id);
        }

        /// <summary>Absolute game seconds for a fixture day + seconds into that day (day-start convention, plan §1.1).</summary>
        private static float Day(int day, int secondsIntoDay)
        {
            return day * 86400f + secondsIntoDay;
        }

        /// <summary>Fake simulation clock: the test advances it explicitly, in seconds.</summary>
        private sealed class FakeClock
        {
            public float Now;

            public float Read()
            {
                return Now;
            }
        }

        private static TaskAuthority CreateAuthorityWithDiagnostics(out TaskDurationDiagnostics diagnostics, FakeClock clock)
        {
            var authority = new TaskAuthority();
            string reason;
            Assert.IsTrue(authority.RegisterDefinition(new TaskDefinition("test.sweep", "Sweep", 10), out reason), reason);
            diagnostics = new TaskDurationDiagnostics();
            authority.SetTimingObserver(diagnostics);
            if (clock != null)
            {
                authority.SetTimingClock(clock.Read);
            }

            return authority;
        }

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

        private static int CountLinesContaining(List<string> lines, string fragment)
        {
            int count = 0;
            foreach (string line in lines)
            {
                if (line.IndexOf(fragment, StringComparison.Ordinal) >= 0)
                {
                    count++;
                }
            }

            return count;
        }

        private static string FindOverrunLine(List<string> lines, int day)
        {
            string prefix = "[TaskDuration] overrun day " + day + ":";
            foreach (string line in lines)
            {
                if (line.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return line;
                }
            }

            return null;
        }

        // Decision 11, rung 1: a Queued task the assignment drive confirmed
        // workerless warns after approximately one simulated day, naming the
        // actual blocker. Before a full day has passed it stays silent.
        [Test]
        public void NoWorker_WarnAfterOneDay_NamesBlocker()
        {
            var clock = new FakeClock { Now = Day(0, 28800) };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics(out diagnostics, clock);
            List<string> lines = CaptureLines(diagnostics);

            WorkTask task = authority.CreateTask("test.sweep", Business(1), 0);
            Assert.AreEqual(TaskStatus.Queued, task.Status);

            // Day 0: fed workerless, but the span is under a day — silent.
            diagnostics.RecordNoWorker(task.TaskId.ToString(), NoWorkers, 0);
            clock.Now = Day(0, 43200);
            diagnostics.Evaluate(clock.Now, 0);
            Assert.AreEqual(0, CountLinesContaining(lines, "L8"),
                "day 0: under a day without a worker — no warning yet");
            Assert.AreEqual(0, lines.Count, "nothing overran on day 0");

            // Day 1: fed again, span past a day — one warning naming the blocker.
            diagnostics.RecordNoWorker(task.TaskId.ToString(), NoWorkers, 1);
            clock.Now = Day(1, 43200);
            TaskTimingVerdict verdict = diagnostics.Classify(diagnostics.GetRecord(task.TaskId), clock.Now, 1);
            Assert.IsNotNull(verdict);
            Assert.AreEqual(TaskTimingVerdictKind.Escalated, verdict.Verdict);
            Assert.AreEqual(TaskTimingReasonClass.L8NoWorkerExists, verdict.ReasonClass);
            Assert.AreEqual(1, verdict.NoWorkerStage, "day 1: warn rung");
            Assert.AreEqual(NoWorkers, verdict.EvidenceText, "the notification names the actual blocker");
            diagnostics.Evaluate(clock.Now, 1);
            Assert.AreEqual(1, CountLinesStartingWith(lines, "[TaskDuration] overrun day 1:"),
                "exactly one warning line");
            string warning = FindOverrunLine(lines, 1);
            Assert.IsNotNull(warning);
            StringAssert.Contains("(L8: " + NoWorkers + ")", warning);
            StringAssert.Contains("no-worker warn", warning);
        }

        // Decision 11, rung 2: the streak past EscalationDays (3) escalates.
        // Days 1-2 stay on the warn rung; day 3 (streak 4) escalates.
        [Test]
        public void NoWorker_EscalatesAfterThreeDays()
        {
            var clock = new FakeClock { Now = Day(0, 28800) };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics(out diagnostics, clock);
            List<string> lines = CaptureLines(diagnostics);

            WorkTask task = authority.CreateTask("test.sweep", Business(1), 0);

            for (int day = 0; day <= 3; day++)
            {
                clock.Now = Day(day, 43200);
                diagnostics.RecordNoWorker(task.TaskId.ToString(), NoWorkers, day);
                TaskTimingVerdict verdict = diagnostics.Classify(diagnostics.GetRecord(task.TaskId), clock.Now, day);
                Assert.IsNotNull(verdict, "day " + day);
                if (day == 0)
                {
                    Assert.AreEqual(TaskTimingVerdictKind.Legitimate, verdict.Verdict, "day 0: under a day");
                    Assert.AreEqual(0, verdict.NoWorkerStage, "day 0: no rung yet");
                }
                else
                {
                    Assert.AreEqual(TaskTimingReasonClass.L8NoWorkerExists, verdict.ReasonClass, "day " + day);
                    if (day <= 2)
                    {
                        Assert.AreEqual(TaskTimingVerdictKind.Escalated, verdict.Verdict, "day " + day);
                        Assert.AreEqual(1, verdict.NoWorkerStage, "day " + day + ": warn rung");
                    }
                    else
                    {
                        Assert.AreEqual(TaskTimingVerdictKind.Escalated, verdict.Verdict, "day 3: escalated");
                        Assert.AreEqual(2, verdict.NoWorkerStage, "day 3: streak 4 past EscalationDays 3");
                    }
                }
            }

            diagnostics.Evaluate(clock.Now, 3);
            Assert.AreEqual(1, CountLinesStartingWith(lines, "[TaskDuration] overrun day 3:"));
            string warning = FindOverrunLine(lines, 3);
            Assert.IsNotNull(warning);
            StringAssert.Contains("no-worker escalated", warning);
        }

        // Decision 11, rung 3: the streak past NoWorkerExceptionDays (7) raises
        // the higher-priority exception. Day 6 (streak 7) is still escalated;
        // day 7 (streak 8) is the exception.
        [Test]
        public void NoWorker_ExceptionAfterSevenDays()
        {
            var clock = new FakeClock { Now = Day(0, 28800) };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics(out diagnostics, clock);
            List<string> lines = CaptureLines(diagnostics);

            WorkTask task = authority.CreateTask("test.sweep", Business(1), 0);

            for (int day = 0; day <= 7; day++)
            {
                clock.Now = Day(day, 43200);
                diagnostics.RecordNoWorker(task.TaskId.ToString(), NoWorkers, day);
                TaskTimingVerdict verdict = diagnostics.Classify(diagnostics.GetRecord(task.TaskId), clock.Now, day);
                Assert.IsNotNull(verdict, "day " + day);
                if (day == 0)
                {
                    Assert.AreEqual(TaskTimingVerdictKind.Legitimate, verdict.Verdict, "day 0: under a day");
                    Assert.AreEqual(0, verdict.NoWorkerStage, "day 0: no rung yet");
                }
                else if (day <= 2)
                {
                    Assert.AreEqual(1, verdict.NoWorkerStage, "day " + day + ": warn rung");
                }
                else if (day <= 6)
                {
                    Assert.AreEqual(2, verdict.NoWorkerStage, "day " + day + ": escalated rung");
                }
                else
                {
                    Assert.AreEqual(TaskTimingVerdictKind.Escalated, verdict.Verdict, "day 7: exception rung");
                    Assert.AreEqual(3, verdict.NoWorkerStage, "day 7: streak 8 past NoWorkerExceptionDays 7");
                }
            }

            diagnostics.Evaluate(clock.Now, 7);
            Assert.AreEqual(1, CountLinesStartingWith(lines, "[TaskDuration] overrun day 7:"));
            string warning = FindOverrunLine(lines, 7);
            Assert.IsNotNull(warning);
            StringAssert.Contains("no-worker EXCEPTION", warning);
        }

        // Decision 11, duplicate suppression: the per-task cap allows one warning
        // per task per day — a second evaluation the same day adds nothing — and
        // one fresh line per day after that, never a repeat within a day.
        [Test]
        public void NoWorker_NoDuplicateSpam_SameDayAndAcrossDays()
        {
            var clock = new FakeClock { Now = Day(0, 28800) };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics(out diagnostics, clock);
            List<string> lines = CaptureLines(diagnostics);

            WorkTask task = authority.CreateTask("test.sweep", Business(1), 0);
            diagnostics.RecordNoWorker(task.TaskId.ToString(), NoWorkers, 0);

            diagnostics.RecordNoWorker(task.TaskId.ToString(), NoWorkers, 1);
            clock.Now = Day(1, 43200);
            diagnostics.Evaluate(clock.Now, 1);
            diagnostics.Evaluate(clock.Now, 1);
            Assert.AreEqual(1, CountLinesStartingWith(lines, "[TaskDuration] overrun day 1:"),
                "the per-task cap allows one warning per task per day");

            diagnostics.RecordNoWorker(task.TaskId.ToString(), NoWorkers, 2);
            clock.Now = Day(2, 43200);
            diagnostics.Evaluate(clock.Now, 2);
            Assert.AreEqual(1, CountLinesStartingWith(lines, "[TaskDuration] overrun day 2:"));
            Assert.AreEqual(2, CountLinesStartingWith(lines, "[TaskDuration] overrun day"),
                "one line per day, never a repeat within a day");
        }

        // Decision 11, natural recovery: assignment clears the streak, so the
        // task stops warning as workerless the moment a worker exists.
        [Test]
        public void NoWorker_RecoveryOnAssignment_ClearsStreak()
        {
            var clock = new FakeClock { Now = Day(0, 28800) };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics(out diagnostics, clock);
            List<string> lines = CaptureLines(diagnostics);
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(0);

            WorkTask task = authority.CreateTask("test.sweep", Business(1), 0);
            diagnostics.RecordNoWorker(task.TaskId.ToString(), NoWorkers, 0);
            diagnostics.RecordNoWorker(task.TaskId.ToString(), NoWorkers, 1);
            clock.Now = Day(1, 43200);
            diagnostics.Evaluate(clock.Now, 1);
            Assert.AreEqual(1, CountLinesStartingWith(lines, "[TaskDuration] overrun day 1:"));

            // Day 2: a worker becomes available — assignment clears the streak.
            budgets.EnsureDay(2);
            string reason;
            Assert.IsTrue(authority.AssignTask(task.TaskId, Person(1), budgets, 2, out reason), reason);
            TaskTimingRecord record = diagnostics.GetRecord(task.TaskId);
            Assert.AreEqual(0, record.ConsecutiveNoWorkerDays, "assignment clears the streak");
            Assert.AreEqual(-1, record.LastNoWorkerDay);
            Assert.AreEqual(string.Empty, record.LastNoWorkerReason);
            Assert.AreEqual(TaskStatus.Assigned, task.Status);

            // Day 2 Evaluate (no feed — the task has a worker now): no NEW L8 line.
            // The list still holds day 1's warning + summary lines, so the count
            // must be unchanged, not zero.
            int l8Before = CountLinesContaining(lines, "L8");
            Assert.Greater(l8Before, 0, "day 1 must have warned first");
            clock.Now = Day(2, 43200);
            diagnostics.Evaluate(clock.Now, 2);
            Assert.AreEqual(l8Before, CountLinesContaining(lines, "L8"),
                "after recovery the task no longer warns as workerless");
            Assert.AreEqual(0, CountLinesStartingWith(lines, "[TaskDuration] overrun day 2:"),
                "day 2 emits no overrun line at all");
        }

        // A no-worker record for a task the service never saw is dropped, never
        // invented: timing is only recorded for observed tasks.
        [Test]
        public void NoWorker_UnknownTask_Dropped_NeverInvented()
        {
            var clock = new FakeClock { Now = Day(3, 28800) };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics(out diagnostics, clock);
            List<string> lines = CaptureLines(diagnostics);

            diagnostics.RecordNoWorker("T-nope", NoWorkers, 3);
            Assert.IsNull(diagnostics.GetRecord("T-nope"), "a no-worker record never invents a timing record");
            clock.Now = Day(3, 43200);
            diagnostics.Evaluate(clock.Now, 3);
            Assert.AreEqual(0, lines.Count, "nothing to warn about");
        }

        // The ladder needs the feed: a streak the drive did not confirm today is
        // stale, not a live stall — the task stays silent until fed again.
        [Test]
        public void NoWorker_StaleStreak_DoesNotWarn()
        {
            var clock = new FakeClock { Now = Day(0, 28800) };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics(out diagnostics, clock);
            List<string> lines = CaptureLines(diagnostics);

            WorkTask task = authority.CreateTask("test.sweep", Business(1), 0);
            diagnostics.RecordNoWorker(task.TaskId.ToString(), NoWorkers, 0);
            diagnostics.RecordNoWorker(task.TaskId.ToString(), NoWorkers, 1);
            clock.Now = Day(1, 43200);
            diagnostics.Evaluate(clock.Now, 1);
            Assert.AreEqual(1, CountLinesStartingWith(lines, "[TaskDuration] overrun day 1:"));

            // Days 2-3: no feed (the drive did not confirm workerlessness) — silent.
            clock.Now = Day(3, 43200);
            TaskTimingVerdict verdict = diagnostics.Classify(diagnostics.GetRecord(task.TaskId), clock.Now, 3);
            Assert.AreEqual(TaskTimingVerdictKind.Legitimate, verdict.Verdict,
                "a streak the drive did not confirm today is stale, not a live stall");
            Assert.AreEqual(0, verdict.NoWorkerStage);
            diagnostics.Evaluate(clock.Now, 3);
            Assert.AreEqual(0, CountLinesStartingWith(lines, "[TaskDuration] overrun day 3:"));
        }

        // A gap in fed days resets the streak: fed days 0-3 (streak 4, escalated),
        // unfed days 4-5, fed again day 6 — back to the warn rung, not escalated.
        [Test]
        public void NoWorker_GapInFedDays_ResetsStreak()
        {
            var clock = new FakeClock { Now = Day(0, 28800) };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics(out diagnostics, clock);
            List<string> lines = CaptureLines(diagnostics);

            WorkTask task = authority.CreateTask("test.sweep", Business(1), 0);
            for (int day = 0; day <= 3; day++)
            {
                diagnostics.RecordNoWorker(task.TaskId.ToString(), NoWorkers, day);
            }

            clock.Now = Day(3, 43200);
            TaskTimingVerdict day3 = diagnostics.Classify(diagnostics.GetRecord(task.TaskId), clock.Now, 3);
            Assert.AreEqual(2, day3.NoWorkerStage, "day 3: streak 4 past EscalationDays");

            diagnostics.RecordNoWorker(task.TaskId.ToString(), NoWorkers, 6);
            clock.Now = Day(6, 43200);
            TaskTimingRecord record = diagnostics.GetRecord(task.TaskId);
            Assert.AreEqual(1, record.ConsecutiveNoWorkerDays, "a gap in fed days resets the streak");
            TaskTimingVerdict day6 = diagnostics.Classify(record, clock.Now, 6);
            Assert.AreEqual(1, day6.NoWorkerStage, "day 6: back to the warn rung");
            diagnostics.Evaluate(clock.Now, 6);
            string warning = FindOverrunLine(lines, 6);
            Assert.IsNotNull(warning);
            StringAssert.Contains("no-worker warn", warning);
        }

        // The streak counts workerless days, not identical reasons: a reason
        // change does not reset it, and the latest reason names the current
        // blocker in the notification.
        [Test]
        public void NoWorker_ReasonChange_KeepsStreak_NamesLatestBlocker()
        {
            const string second = "unassignable: P1 only 30m remaining (< 60m)";
            var clock = new FakeClock { Now = Day(0, 28800) };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics(out diagnostics, clock);
            List<string> lines = CaptureLines(diagnostics);

            WorkTask task = authority.CreateTask("test.sweep", Business(1), 0);
            diagnostics.RecordNoWorker(task.TaskId.ToString(), NoWorkers, 0);
            diagnostics.RecordNoWorker(task.TaskId.ToString(), second, 1);

            TaskTimingRecord record = diagnostics.GetRecord(task.TaskId);
            Assert.AreEqual(2, record.ConsecutiveNoWorkerDays, "the streak counts workerless days, not identical reasons");
            Assert.AreEqual(second, record.LastNoWorkerReason, "the latest reason names the current blocker");

            clock.Now = Day(1, 43200);
            TaskTimingVerdict verdict = diagnostics.Classify(record, clock.Now, 1);
            Assert.AreEqual(second, verdict.EvidenceText);
            diagnostics.Evaluate(clock.Now, 1);
            string warning = FindOverrunLine(lines, 1);
            Assert.IsNotNull(warning);
            StringAssert.Contains("(L8: " + second + ")", warning);
        }

        // Part A guard: queued waiting alone never warns — the ladder needs the
        // assignment drive's feed. Without it, five days queued stays silent.
        [Test]
        public void NoWorker_QueuedWithoutFeed_StaysSilent()
        {
            var clock = new FakeClock { Now = Day(0, 28800) };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics(out diagnostics, clock);
            List<string> lines = CaptureLines(diagnostics);

            WorkTask task = authority.CreateTask("test.sweep", Business(1), 0);
            Assert.AreEqual(TaskStatus.Queued, task.Status);

            for (int day = 0; day <= 5; day++)
            {
                clock.Now = Day(day, 43200);
                diagnostics.Evaluate(clock.Now, day);
            }

            Assert.AreEqual(0, lines.Count,
                "queued waiting alone never warns — the ladder needs the assignment drive's feed (Part A guard)");
        }

        // Decision 11, read-only: the ladder never assigns, hires, or moves the
        // task — after eight days to the exception rung the task is still Queued
        // with no assignee.
        [Test]
        public void NoWorker_LadderNeverAssignsOrHires_ReadOnly()
        {
            var clock = new FakeClock { Now = Day(0, 28800) };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics(out diagnostics, clock);
            List<string> lines = CaptureLines(diagnostics);

            WorkTask task = authority.CreateTask("test.sweep", Business(1), 0);
            for (int day = 0; day <= 7; day++)
            {
                clock.Now = Day(day, 43200);
                diagnostics.RecordNoWorker(task.TaskId.ToString(), NoWorkers, day);
                diagnostics.Evaluate(clock.Now, day);
            }

            Assert.AreEqual(TaskStatus.Queued, task.Status, "the ladder never moves the task");
            Assert.IsFalse(task.HasAssignee, "no worker was invented or assigned");
            Assert.AreEqual(1, CountLinesStartingWith(lines, "[TaskDuration] overrun day 7:"));
            string warning = FindOverrunLine(lines, 7);
            Assert.IsNotNull(warning);
            StringAssert.Contains("no-worker EXCEPTION", warning);
        }

        // Cancelling a workerless task ends the ladder silently: no terminal L8
        // line, no further warning.
        [Test]
        public void NoWorker_CancelledTask_EndsLadderSilently()
        {
            var clock = new FakeClock { Now = Day(0, 28800) };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics(out diagnostics, clock);
            List<string> lines = CaptureLines(diagnostics);
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(0);

            WorkTask task = authority.CreateTask("test.sweep", Business(1), 0);
            diagnostics.RecordNoWorker(task.TaskId.ToString(), NoWorkers, 0);
            diagnostics.RecordNoWorker(task.TaskId.ToString(), NoWorkers, 1);
            clock.Now = Day(1, 43200);
            diagnostics.Evaluate(clock.Now, 1);
            Assert.AreEqual(1, CountLinesStartingWith(lines, "[TaskDuration] overrun day 1:"));

            budgets.EnsureDay(2);
            Assert.IsTrue(authority.CancelTask(task.TaskId, budgets));
            Assert.AreEqual(TaskStatus.Cancelled, task.Status);

            // Day 2 Evaluate: the L8 ladder ends — no NEW L8 line. (Note: the
            // terminal classification itself may still emit a completed-overrun
            // line for the cancelled task, because CancelTask never sets
            // CompletedSeconds so the terminal span runs to nowSeconds — a
            // pre-existing terminal-path quirk, out of scope for DEF-025. The
            // ladder's guarantee is only that no L8 line appears.)
            int l8Before = CountLinesContaining(lines, "L8");
            Assert.Greater(l8Before, 0, "day 1 must have warned first");
            clock.Now = Day(2, 43200);
            diagnostics.Evaluate(clock.Now, 2);
            Assert.AreEqual(0, CountLinesStartingWith(lines, "[TaskDuration] overrun day 2:"));
            Assert.AreEqual(l8Before, CountLinesContaining(lines, "L8"),
                "a cancelled task warns no further as workerless");
        }
    }
}
