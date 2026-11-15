// Campaign C (DEF-063) — Task-duration warning caps hide stalls past the cap.
//
// The §4.2 caps (12 warnings/day, 3/definition/day, 1/task/day; 512 tracked
// records) counted what they omitted but never named it: the daily summary
// said "8 omitted by cap" and "evicted 2 record(s)" without saying which
// tasks stalled or which records were evicted. This package records the
// identities on the same diagnostics surface (the LogSink): an
// "[TaskDuration] omitted" line names the open overruns the caps omitted
// this evaluation, in record order, and a "[TaskDuration] evicted" line
// names the records evicted from the bounded store since the last summary,
// in eviction order. The caps themselves are unchanged — only the omitted
// set becomes visible. Both lines are emitted only as part of the daily
// summary emission (at most once per day), and only when they have
// something to name, so quiet days gain no new lines.
//
// Three test kinds, same file:
//  (1) Behavioural cap tests (real authority + real diagnostics): the
//      global cap and the per-definition cap each name exactly the omitted
//      tasks (the ones actually omitted, not a sample) and none of the
//      warned tasks; under the cap no omitted line appears.
//  (2) Behavioural eviction tests: the evicted line names exactly the
//      evicted records and none of the survivors, and the retained list is
//      cleared when the summary is emitted (a later day with no new
//      evictions emits no second evicted line).
//  (3) Structural tests: the cap defaults are unchanged (12 / 3 / 1 / 512),
//      and a second evaluation on a day whose summary was already emitted
//      reports neither a second summary nor a second omitted line.
using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Tasks
{
    [TestFixture]
    public sealed class CampaignCOmittedTaskIdentitiesTests
    {
        private static EntityId Person(int id) => EntityId.For(EntityKind.Person, id);
        private static EntityId Business(int id) => EntityId.For(EntityKind.Business, id);

        /// <summary>Fake simulation clock: the test advances it explicitly, in seconds.</summary>
        private sealed class FakeClock
        {
            public float Now;
        }

        private static float Day(int day, int secondsIntoDay)
        {
            return day * 86400f + secondsIntoDay;
        }

        private static TaskAuthority CreateAuthority(string definitionId = "test.sweep", int baseMinutes = 10)
        {
            var authority = new TaskAuthority();
            string reason;
            Assert.IsTrue(authority.RegisterDefinition(new TaskDefinition(definitionId, "Sweep", baseMinutes), out reason), reason);
            return authority;
        }

        private static TaskAuthority CreateAuthorityWithDiagnostics(out TaskDurationDiagnostics diagnostics, FakeClock clock)
        {
            TaskAuthority authority = CreateAuthority();
            diagnostics = new TaskDurationDiagnostics();
            authority.SetTimingObserver(diagnostics);
            authority.SetTimingClock(() => clock.Now);
            return authority;
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

        private static string FindLine(List<string> lines, string prefix)
        {
            foreach (string line in lines)
            {
                if (line.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return line;
                }
            }

            return null;
        }

        /// <summary>The identity fragment the warning and summary lines use for a task: task id plus definition id.</summary>
        private static string IdentityFragment(WorkTask task, string definitionId)
        {
            return "task " + task.TaskId + " '" + definitionId + "'";
        }

        [Test]
        public void Caps_Unchanged_DefaultsAre12PerDay3PerDefinition1PerTask512Records()
        {
            TaskDurationDiagnosticSettings defaults = TaskDurationDiagnosticSettings.Defaults;
            Assert.AreEqual(12, defaults.MaxWarningsPerDay,
                "the global warning cap is unchanged by this package");
            Assert.AreEqual(3, defaults.MaxWarningsPerDefinitionPerDay,
                "the per-definition warning cap is unchanged by this package");
            Assert.AreEqual(1, defaults.MaxWarningsPerTaskPerDay,
                "the per-task warning cap is unchanged by this package");
            Assert.AreEqual(512, defaults.MaxTrackedRecords,
                "the record-store bound is unchanged by this package");
            Assert.AreEqual(512, TaskDurationDiagnostics.DefaultMaxTrackedRecords);
        }

        // DEF-063: the global cap (12/day) omits 8 of 20 overrunning tasks on
        // distinct definitions. The omitted line names exactly those 8 — the
        // ones actually omitted, in record order — and none of the 12 warned
        // tasks.
        [Test]
        public void GlobalCap_OmittedIdentities_NamedInOmittedLine()
        {
            var clock = new FakeClock { Now = Day(10, 28800) };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics(out diagnostics, clock);
            List<string> lines = CaptureLines(diagnostics);
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(10);

            var tasks = new List<WorkTask>();
            for (int i = 0; i < 20; i++)
            {
                string definitionId = "test.cap" + i;
                RegisterDefinition(authority, definitionId, 10);
                tasks.Add(StartStuckTask(authority, budgets, definitionId, Business(1), Person(100 + i), 10));
            }

            clock.Now = Day(12, 28800);
            diagnostics.Evaluate(clock.Now, 12);

            Assert.AreEqual(12, CountLinesStartingWith(lines, "[TaskDuration] overrun"),
                "MaxWarningsPerDay 12: at most 12 warning lines across all tasks");
            Assert.AreEqual(1, CountLinesStartingWith(lines, "[TaskDuration] summary"),
                "exactly one daily summary line");
            Assert.AreEqual(1, CountLinesStartingWith(lines, "[TaskDuration] omitted"),
                "exactly one omitted-identities line");
            string omitted = FindLine(lines, "[TaskDuration] omitted");
            Assert.IsNotNull(omitted);
            Assert.IsTrue(omitted.StartsWith("[TaskDuration] omitted day 12: 8 omitted by cap: ", StringComparison.Ordinal),
                "the omitted line carries the day and the omitted count: " + omitted);
            // Record order is insertion order, so the first 12 created warn
            // and the last 8 are omitted.
            for (int i = 0; i < 12; i++)
            {
                Assert.IsTrue(omitted.IndexOf(IdentityFragment(tasks[i], "test.cap" + i), StringComparison.Ordinal) < 0,
                    "a warned task must not appear in the omitted line");
            }
            for (int i = 12; i < 20; i++)
            {
                StringAssert.Contains(IdentityFragment(tasks[i], "test.cap" + i), omitted,
                    "every omitted task must be named, not a sample");
            }
            string summary = FindLine(lines, "[TaskDuration] summary");
            Assert.IsNotNull(summary);
            StringAssert.Contains("8 omitted by cap", summary,
                "the summary count is unchanged by the new line");
        }

        // DEF-063: the per-definition cap (3/day) omits 2 of 5 overrunning tasks
        // of one definition. The omitted line names exactly those 2.
        [Test]
        public void PerDefinitionCap_OmittedIdentities_NamedInOmittedLine()
        {
            var clock = new FakeClock { Now = Day(10, 28800) };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics(out diagnostics, clock);
            List<string> lines = CaptureLines(diagnostics);
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(10);

            var tasks = new List<WorkTask>();
            for (int i = 0; i < 5; i++)
            {
                tasks.Add(StartStuckTask(authority, budgets, "test.sweep", Business(1), Person(200 + i), 10));
            }

            clock.Now = Day(12, 28800);
            diagnostics.Evaluate(clock.Now, 12);

            Assert.AreEqual(3, CountLinesStartingWith(lines, "[TaskDuration] overrun"),
                "MaxWarningsPerDefinitionPerDay 3: at most 3 lines for one definition per day");
            Assert.AreEqual(1, CountLinesStartingWith(lines, "[TaskDuration] omitted"),
                "exactly one omitted-identities line");
            string omitted = FindLine(lines, "[TaskDuration] omitted");
            Assert.IsNotNull(omitted);
            Assert.IsTrue(omitted.StartsWith("[TaskDuration] omitted day 12: 2 omitted by cap: ", StringComparison.Ordinal),
                "the omitted line carries the day and the omitted count: " + omitted);
            for (int i = 0; i < 3; i++)
            {
                Assert.IsTrue(omitted.IndexOf(IdentityFragment(tasks[i], "test.sweep"), StringComparison.Ordinal) < 0,
                    "a warned task must not appear in the omitted line");
            }
            for (int i = 3; i < 5; i++)
            {
                StringAssert.Contains(IdentityFragment(tasks[i], "test.sweep"), omitted,
                    "every omitted task must be named");
            }
        }

        // DEF-063: under the cap, nothing is omitted, so no omitted line and no
        // evicted line appear — quiet days gain no new lines.
        [Test]
        public void UnderCap_NoOmittedLine_NoEvictedLine()
        {
            var clock = new FakeClock { Now = Day(10, 28800) };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics(out diagnostics, clock);
            List<string> lines = CaptureLines(diagnostics);
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(10);

            for (int i = 0; i < 3; i++)
            {
                string definitionId = "test.cap" + i;
                RegisterDefinition(authority, definitionId, 10);
                StartStuckTask(authority, budgets, definitionId, Business(1), Person(100 + i), 10);
            }

            clock.Now = Day(12, 28800);
            diagnostics.Evaluate(clock.Now, 12);

            Assert.AreEqual(3, CountLinesStartingWith(lines, "[TaskDuration] overrun"));
            Assert.AreEqual(1, CountLinesStartingWith(lines, "[TaskDuration] summary"));
            Assert.AreEqual(0, CountLinesStartingWith(lines, "[TaskDuration] omitted"),
                "no warning was omitted, so no omitted line");
            Assert.AreEqual(0, CountLinesStartingWith(lines, "[TaskDuration] evicted"),
                "no record was evicted, so no evicted line");
            string summary = FindLine(lines, "[TaskDuration] summary");
            Assert.IsNotNull(summary);
            StringAssert.Contains("0 omitted by cap", summary);
        }

        /// <summary>Builds the eviction scenario from the existing bounded-store test: a store of 5 holding 5 terminal records, then 2 open stuck tasks evict the 2 oldest terminals.</summary>
        private static void BuildEvictionScenario(out TaskDurationDiagnostics diagnostics, out List<string> lines, out List<EntityId> terminalIds)
        {
            var clock = new FakeClock { Now = Day(10, 28800) };
            TaskAuthority authority = CreateAuthorityWithDiagnostics(out diagnostics, clock);
            diagnostics.MaxTrackedRecords = 5;
            lines = CaptureLines(diagnostics);
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(10);

            terminalIds = new List<EntityId>();
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
            StartStuckTask(authority, budgets, "test.sweep", Business(1), Person(1), 10);
            StartStuckTask(authority, budgets, "test.sweep", Business(1), Person(2), 10);

            Assert.AreEqual(5, diagnostics.RecordCount, "the store never grows past MaxTrackedRecords");
            Assert.AreEqual(2, diagnostics.EvictedRecordCount, "two terminal records were evicted to make room");
            Assert.IsNull(diagnostics.GetRecord(terminalIds[0]), "oldest terminal evicted first");
            Assert.IsNull(diagnostics.GetRecord(terminalIds[1]), "second-oldest terminal evicted next");
            Assert.IsNotNull(diagnostics.GetRecord(terminalIds[4]), "the newest terminal survives while older terminals remain to evict");
        }

        // DEF-063: the 2 evicted records are named on the evicted line — the
        // ones actually evicted, not a sample — and none of the survivors.
        // No warning was omitted, so no omitted line appears.
        [Test]
        public void EvictedIdentities_NamedInEvictedLine()
        {
            TaskDurationDiagnostics diagnostics;
            List<string> lines;
            List<EntityId> terminalIds;
            BuildEvictionScenario(out diagnostics, out lines, out terminalIds);

            diagnostics.Evaluate(Day(12, 28800), 12);

            string summary = FindLine(lines, "[TaskDuration] summary");
            Assert.IsNotNull(summary);
            StringAssert.Contains("evicted 2 record(s)", summary,
                "the eviction count is reported on the summary line (§4.2)");
            Assert.AreEqual(1, CountLinesStartingWith(lines, "[TaskDuration] evicted"),
                "exactly one evicted-identities line");
            string evicted = FindLine(lines, "[TaskDuration] evicted");
            Assert.IsNotNull(evicted);
            Assert.IsTrue(evicted.StartsWith("[TaskDuration] evicted day 12: 2 evicted: ", StringComparison.Ordinal),
                "the evicted line carries the day and the evicted count: " + evicted);
            StringAssert.Contains("task " + terminalIds[0] + " 'test.sweep'", evicted,
                "the oldest evicted record must be named");
            StringAssert.Contains("task " + terminalIds[1] + " 'test.sweep'", evicted,
                "the second evicted record must be named");
            for (int i = 2; i < 5; i++)
            {
                Assert.IsTrue(evicted.IndexOf("task " + terminalIds[i] + " 'test.sweep'", StringComparison.Ordinal) < 0, "a surviving record must not appear in the evicted line");
            }
            Assert.AreEqual(0, CountLinesStartingWith(lines, "[TaskDuration] omitted"),
                "no warning was omitted, so no omitted line");
        }

        // DEF-063: the retained evicted list is cleared when the summary is
        // emitted — a later day with no new evictions emits no second
        // evicted line and no omitted line.
        [Test]
        public void EvictedIdentities_ClearedAfterSummary_NoSecondEvictedLine()
        {
            TaskDurationDiagnostics diagnostics;
            List<string> lines;
            List<EntityId> terminalIds;
            BuildEvictionScenario(out diagnostics, out lines, out terminalIds);

            diagnostics.Evaluate(Day(12, 28800), 12);
            Assert.AreEqual(1, CountLinesStartingWith(lines, "[TaskDuration] evicted"),
                "day 12 names the evicted records once");

            diagnostics.Evaluate(Day(13, 28800), 13);
            Assert.AreEqual(1, CountLinesStartingWith(lines, "[TaskDuration] summary day 13"),
                "day 13 still earns a summary (the 2 open overruns persist)");
            Assert.AreEqual(1, CountLinesStartingWith(lines, "[TaskDuration] evicted"),
                "no second evicted line: the retained list was cleared at the day-12 summary and nothing new was evicted");
            Assert.AreEqual(0, CountLinesStartingWith(lines, "[TaskDuration] omitted"),
                "no warning was omitted on either day, so no omitted line at all");
        }

        // DEF-063: the omitted line shares the summary's once-per-day fate. A
        // second evaluation on a day whose summary was already emitted
        // reports neither a second summary nor a second omitted line (the
        // count already behaved this way before this change; the identities
        // inherit it).
        [Test]
        public void SecondEvaluationSameDay_EmitsNeitherSummaryNorOmittedLine()
        {
            var clock = new FakeClock { Now = Day(10, 28800) };
            TaskDurationDiagnostics diagnostics;
            TaskAuthority authority = CreateAuthorityWithDiagnostics(out diagnostics, clock);
            List<string> lines = CaptureLines(diagnostics);
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(10);

            for (int i = 0; i < 20; i++)
            {
                string definitionId = "test.cap" + i;
                RegisterDefinition(authority, definitionId, 10);
                StartStuckTask(authority, budgets, definitionId, Business(1), Person(100 + i), 10);
            }

            clock.Now = Day(12, 28800);
            diagnostics.Evaluate(clock.Now, 12);
            Assert.AreEqual(12, CountLinesStartingWith(lines, "[TaskDuration] overrun"));
            Assert.AreEqual(1, CountLinesStartingWith(lines, "[TaskDuration] summary"));
            Assert.AreEqual(1, CountLinesStartingWith(lines, "[TaskDuration] omitted"));

            diagnostics.Evaluate(clock.Now, 12);
            Assert.AreEqual(12, CountLinesStartingWith(lines, "[TaskDuration] overrun"),
                "the second evaluation the same day warns nothing more: the per-day caps are already spent");
            Assert.AreEqual(1, CountLinesStartingWith(lines, "[TaskDuration] summary"),
                "the summary is emitted at most once per day");
            Assert.AreEqual(1, CountLinesStartingWith(lines, "[TaskDuration] omitted"),
                "the omitted line is emitted only as part of the summary emission");
        }
    }
}
