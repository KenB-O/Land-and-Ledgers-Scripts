using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Tasks
{
    /// <summary>
    /// CAMPAIGN-C, DEF-075 (Nov 15, 2026): CancelTask never set CompletedSeconds.
    /// The CancelTask / MarkReworkRequired transition case set only Outcome, so a
    /// cancelled task's terminal span ran to nowSeconds and post-cancellation days
    /// accrued as uncovered — a cancelled long-waiting task warned as a
    /// completed-overrun with reason "none recorded", not a real overrun.
    /// The fix bounds the terminal span at the terminal transition
    /// (CompletedSeconds, mirroring CompleteTask), set only when unset: a Failed
    /// task already carries the completion bound from CompleteTask, and rework
    /// must not extend it. Tests only; no production code. Drives the real
    /// TaskAuthority with a fake advancing clock and the real
    /// TaskDurationDiagnostics service as the installed observer (the
    /// TaskDurationDiagnosticsTests / CampaignCNoWorkerEscalationTests idiom).
    ///
    /// Retention/eviction note (resolves the E-36 caveat): the live warning
    /// genuinely fired pre-fix. There is no time-based TTL for terminal
    /// records — eviction happens only when the store hits MaxTrackedRecords
    /// (512, terminal-first) — and a cap-blocked terminal warning is retried
    /// on later evaluations instead of being lost, so a cancelled
    /// long-waiting task warned once, on the first daily Evaluate after its
    /// uncovered post-cancellation span passed the P2 threshold.
    /// </summary>
    [TestFixture]
    public sealed class CampaignCCancelledTaskTerminalSpanTests
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
            Assert.IsTrue(authority.RegisterDefinition(new TaskDefinition("test.span", "Span", 10), out reason), reason);
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

        // DEF-075: the terminal span ends at cancellation. CompletedSeconds is
        // the exact cancellation time from the clock; CompletedDay is set (the
        // CancelTask notification carries day -1, so ApplyTransition falls back
        // to the record's last transition day — the span-critical field is
        // CompletedSeconds, which is exact). Post-cancellation days do not
        // accrue as uncovered: the verdict stays Legitimate with zero uncovered
        // seconds five days later.
        [Test]
        public void CancelledTask_TerminalSpanEndsAtCancellation()
        {
            var clock = new FakeClock { Now = Day(0, 28800) };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics(out diagnostics, clock);
            CaptureLines(diagnostics);
            var budgets = new WorkTimeBudgetStore();

            WorkTask task = authority.CreateTask("test.span", Business(1), 0);
            Assert.AreEqual(TaskStatus.Queued, task.Status);

            clock.Now = Day(5, 43200);
            Assert.IsTrue(authority.CancelTask(task.TaskId, budgets));
            Assert.AreEqual(TaskStatus.Cancelled, task.Status);

            TaskTimingRecord record = diagnostics.GetRecord(task.TaskId);
            Assert.IsNotNull(record);
            Assert.AreEqual(Day(5, 43200), record.CompletedSeconds, 0.001f,
                "the terminal span must end at the cancellation time, not run to nowSeconds");
            Assert.GreaterOrEqual(record.CompletedDay, 0, "CompletedDay must be set, not -1");

            clock.Now = Day(10, 43200);
            TaskTimingVerdict verdict = diagnostics.Classify(record, clock.Now, 10);
            Assert.IsNotNull(verdict);
            Assert.AreEqual(TaskTimingVerdictKind.Legitimate, verdict.Verdict,
                "a cancelled task whose span ends at cancellation is not an overrun");
            Assert.AreEqual(0f, verdict.UncoveredSeconds, 0.001f,
                "post-cancellation days must not accrue as uncovered");
        }

        // DEF-075, the live warning: pre-fix, the first daily Evaluate after
        // the uncovered post-cancellation span passed the P2 threshold emitted
        // "[TaskDuration] completed-overrun day N: ... (none recorded: ...)".
        // Post-fix no completed-overrun line is ever emitted for the task.
        [Test]
        public void CancelledTask_DoesNotWarnAsCompletedOverrun()
        {
            var clock = new FakeClock { Now = Day(0, 28800) };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics(out diagnostics, clock);
            List<string> lines = CaptureLines(diagnostics);
            var budgets = new WorkTimeBudgetStore();

            WorkTask task = authority.CreateTask("test.span", Business(1), 0);
            clock.Now = Day(5, 43200);
            Assert.IsTrue(authority.CancelTask(task.TaskId, budgets));

            for (int day = 6; day <= 10; day++)
            {
                clock.Now = Day(day, 43200);
                diagnostics.Evaluate(clock.Now, day);
            }

            Assert.AreEqual(0, CountLinesStartingWith(lines, "[TaskDuration] completed-overrun"),
                "a cancelled task must never warn as a completed-overrun");
            Assert.AreEqual(0, lines.Count, "no diagnostic line at all for the cancelled task");
        }

        // DEF-075, the entry's exact scenario: a cancelled long-waiting task.
        // The 20 days queued are explained waiting (Part A), so the verdict
        // stays Legitimate — and the reason class is still "none recorded"
        // (there genuinely is no reason), which no longer warns because the
        // span is bounded at cancellation.
        [Test]
        public void CancelledLongWaitingTask_DoesNotWarnAsCompletedOverrun()
        {
            var clock = new FakeClock { Now = Day(0, 28800) };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics(out diagnostics, clock);
            List<string> lines = CaptureLines(diagnostics);
            var budgets = new WorkTimeBudgetStore();

            WorkTask task = authority.CreateTask("test.span", Business(1), 0);
            clock.Now = Day(20, 43200);
            Assert.IsTrue(authority.CancelTask(task.TaskId, budgets));

            for (int day = 21; day <= 30; day++)
            {
                clock.Now = Day(day, 43200);
                diagnostics.Evaluate(clock.Now, day);
            }

            Assert.AreEqual(0, CountLinesStartingWith(lines, "[TaskDuration] completed-overrun"),
                "a cancelled long-waiting task must never warn as a completed-overrun");
            Assert.AreEqual(0, lines.Count, "no diagnostic line at all for the cancelled task");

            clock.Now = Day(30, 43200);
            TaskTimingVerdict verdict = diagnostics.Classify(diagnostics.GetRecord(task.TaskId), clock.Now, 30);
            Assert.IsNotNull(verdict);
            Assert.AreEqual(TaskTimingVerdictKind.Legitimate, verdict.Verdict);
            Assert.AreEqual(TaskTimingReasonClass.NoneRecorded, verdict.ReasonClass,
                "the reason is still 'none recorded' — it just no longer warns");
            Assert.AreEqual(0f, verdict.UncoveredSeconds, 0.001f,
                "the 20 queued days are explained waiting; nothing accrues after cancellation");
        }

        // DEF-075 must not disturb DEF-025: the L8 ladder still ends
        // correctly on cancellation — no new L8 line after the cancel, and no
        // completed-overrun line either.
        [Test]
        public void CancelledTask_L8LadderStillEndsOnCancellation()
        {
            var clock = new FakeClock { Now = Day(0, 28800) };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics(out diagnostics, clock);
            List<string> lines = CaptureLines(diagnostics);
            var budgets = new WorkTimeBudgetStore();

            WorkTask task = authority.CreateTask("test.span", Business(1), 0);
            diagnostics.RecordNoWorker(task.TaskId.ToString(), NoWorkers, 0);
            diagnostics.RecordNoWorker(task.TaskId.ToString(), NoWorkers, 1);

            clock.Now = Day(1, 43200);
            diagnostics.Evaluate(clock.Now, 1);
            Assert.AreEqual(1, CountLinesContaining(lines, "no-worker"),
                "day 1: one L8 warn line before the cancellation");

            clock.Now = Day(2, 43200);
            Assert.IsTrue(authority.CancelTask(task.TaskId, budgets));

            for (int day = 3; day <= 5; day++)
            {
                clock.Now = Day(day, 43200);
                diagnostics.Evaluate(clock.Now, day);
            }

            Assert.AreEqual(1, CountLinesContaining(lines, "no-worker"),
                "no new L8 line after cancellation — the ladder ends");
            Assert.AreEqual(0, CountLinesStartingWith(lines, "[TaskDuration] completed-overrun"),
                "and no completed-overrun line either");

            clock.Now = Day(5, 43200);
            TaskTimingVerdict verdict = diagnostics.Classify(diagnostics.GetRecord(task.TaskId), clock.Now, 5);
            Assert.IsNotNull(verdict);
            Assert.AreEqual(TaskTimingVerdictKind.Legitimate, verdict.Verdict);
        }

        // DEF-075, the guard: a Failed task already carries the completion
        // bound from CompleteTask, and MarkReworkRequired must not extend it.
        // The task fails after one hour of active work against a 120-minute
        // expectation (active time inside the P2 threshold), is marked
        // rework-required five days later, and still classifies Legitimate
        // with the failure bound intact. This test passes pre-fix too — the
        // failure bound already existed — it pins the guard against the
        // unconditional-set mutation (see Mutation B).
        [Test]
        public void MarkReworkRequired_PreservesFailureBound()
        {
            var clock = new FakeClock { Now = Day(1, 28800) };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics(out diagnostics, clock);
            List<string> lines = CaptureLines(diagnostics);
            var budgets = new WorkTimeBudgetStore();
            string reason;
            Assert.IsTrue(authority.RegisterDefinition(new TaskDefinition("test.span-long", "SpanLong", 120), out reason), reason);
            budgets.EnsureDay(1);

            WorkTask task = authority.CreateTask("test.span-long", Business(1), 1);
            Assert.IsTrue(authority.AssignTask(task.TaskId, Person(1), budgets, out reason), reason);
            Assert.IsTrue(authority.StartTask(task.TaskId, 1, out reason), reason);
            Assert.AreEqual(TaskStatus.InProgress, task.Status);

            clock.Now = Day(1, 32400);
            Assert.IsTrue(authority.CompleteTask(task.TaskId, TaskOutcome.RecoverableFailure, 1, budgets));
            Assert.AreEqual(TaskStatus.Failed, task.Status);

            TaskTimingRecord record = diagnostics.GetRecord(task.TaskId);
            Assert.IsNotNull(record);
            Assert.AreEqual(Day(1, 32400), record.CompletedSeconds, 0.001f,
                "CompleteTask bounds the span at the failure time");

            clock.Now = Day(6, 43200);
            Assert.IsTrue(authority.MarkReworkRequired(task.TaskId, default(EntityId), "test rework", out reason), reason);
            Assert.AreEqual(TaskStatus.ReworkRequired, task.Status);
            Assert.AreEqual(Day(1, 32400), record.CompletedSeconds, 0.001f,
                "rework must not extend the failure bound");

            for (int day = 7; day <= 12; day++)
            {
                clock.Now = Day(day, 43200);
                diagnostics.Evaluate(clock.Now, day);
            }

            Assert.AreEqual(0, CountLinesStartingWith(lines, "[TaskDuration] completed-overrun"),
                "the rework-required task must never warn as a completed-overrun");
            Assert.AreEqual(0, lines.Count, "no diagnostic line at all for the rework-required task");

            clock.Now = Day(12, 43200);
            TaskTimingVerdict verdict = diagnostics.Classify(record, clock.Now, 12);
            Assert.IsNotNull(verdict);
            Assert.AreEqual(TaskTimingVerdictKind.Legitimate, verdict.Verdict);
            Assert.AreEqual(3600f, verdict.UncoveredSeconds, 0.001f,
                "the only uncovered time is the one hour of active work — inside the P2 threshold");
        }
    }
}
