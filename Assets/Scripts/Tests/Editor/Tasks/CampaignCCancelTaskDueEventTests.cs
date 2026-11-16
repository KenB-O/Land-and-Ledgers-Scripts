// Campaign C (DEF-062) — CancelTask does not cancel due events.
//
// The due-cancel code already existed: every terminal transition routes
// through ReleaseTaskResources (TaskAuthorityLifecycle.cs), which calls
// dueEventScheduler.CancelForPayload(task id) when a scheduler is
// installed. But SetDueEventScheduler had zero production callers, so the
// branch was dead in production and stale dues fired for cancelled tasks
// (firing into CompleteTaskAtDueEvent's silent Status != InProgress guard).
// This package installs the hub's scheduler on the hub's TaskAuthority at
// the composition root (SimulationDrivers.Awake, next to the sibling
// SetWorkTimeBudgetStore seam) and adds a diagnostic that logs only when
// a terminal transition actually cancels a pending due event.
//
// Three test kinds, same file:
//  (1) Behavioural TaskAuthority tests (real authority + real scheduler):
//      cancel / complete / interrupt / rework-required each cancel the
//      task's pending dues; without an installed scheduler the dues
//      survive (the seam is optional, the old behaviour pinned).
//  (2) Structural tests pinning the SimulationDrivers wiring by reading
//      the production file — SimulationDrivers is too large a closure for
//      the focused harness, so the wiring itself (not Unity behaviour) is
//      what is asserted here, exactly like the package-6 prune and
//      package-12 patience tests.
//  (3) Structural test pinning the ReleaseTaskResources diagnostic.
// The repo root comes from LAND_LEDGERS_REPO, then a walk up from the
// current directory and the test assembly location (campaign layout:
// <root>/work-c/Assets/...), mirroring CampaignCTaskPruningTests. A
// missing repo fails loudly, never silently passes.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Tasks
{
    [TestFixture]
    public sealed class CampaignCCancelTaskDueEventTests
    {
        private static EntityId Person(int id) => EntityId.For(EntityKind.Person, id);
        private static EntityId Business(int id) => EntityId.For(EntityKind.Business, id);

        private static TaskAuthority CreateAuthority(string definitionId = "test.cancel-due", int baseMinutes = 10)
        {
            var authority = new TaskAuthority();
            string reason;
            Assert.IsTrue(authority.RegisterDefinition(new TaskDefinition(definitionId, "CancelDue", baseMinutes), out reason), reason);
            return authority;
        }

        private static WorkTask CreateAssignedStarted(TaskAuthority authority, WorkTimeBudgetStore budgets, string definitionId = "test.cancel-due")
        {
            string reason;
            budgets.EnsureDay(1);
            WorkTask task = authority.CreateTask(definitionId, Business(1), 1);
            Assert.IsTrue(authority.AssignTask(task.TaskId, Person(1), budgets, out reason), reason);
            Assert.IsTrue(authority.StartTask(task.TaskId, 1, out reason), reason);
            Assert.AreEqual(TaskStatus.InProgress, task.Status);
            return task;
        }

        /// <summary>
        /// Mirrors the production TaskCompletionDue scheduling
        /// (SimulationDrivers: the driver schedules the due on the hub's
        /// scheduler with payloadRef = the task id string).
        /// </summary>
        private static void ScheduleTaskCompletionDue(IDueEventScheduler scheduler, WorkTask task, float dueGameSeconds = 3600f)
        {
            string eventId;
            Assert.IsNull(scheduler.Schedule(DueEventKind.TaskCompletionDue, dueGameSeconds, task.TaskId.ToString(), out eventId));
        }

        [Test]
        public void CancelTask_WithSchedulerInstalled_CancelsPendingTaskCompletionDue()
        {
            TaskAuthority authority = CreateAuthority();
            var scheduler = new DueEventScheduler();
            authority.SetDueEventScheduler(scheduler);
            var budgets = new WorkTimeBudgetStore();
            WorkTask task = CreateAssignedStarted(authority, budgets);
            ScheduleTaskCompletionDue(scheduler, task);
            Assert.AreEqual(1, scheduler.ScheduledCount);

            Assert.IsTrue(authority.CancelTask(task.TaskId, budgets));
            Assert.AreEqual(TaskStatus.Cancelled, task.Status);
            Assert.AreEqual(0, scheduler.ScheduledCount,
                "the cancelled task's pending TaskCompletionDue must not survive");
            Assert.AreEqual(0, scheduler.PopDue(999999f).Count,
                "no stale due may fire for the cancelled task");
        }

        [Test]
        public void CancelTask_WithoutSchedulerInstalled_DueEventSurvives()
        {
            TaskAuthority authority = CreateAuthority();
            var scheduler = new DueEventScheduler();
            var budgets = new WorkTimeBudgetStore();
            WorkTask task = CreateAssignedStarted(authority, budgets);
            ScheduleTaskCompletionDue(scheduler, task);
            Assert.AreEqual(1, scheduler.ScheduledCount);

            Assert.IsTrue(authority.CancelTask(task.TaskId, budgets));
            Assert.AreEqual(TaskStatus.Cancelled, task.Status);
            Assert.AreEqual(1, scheduler.ScheduledCount,
                "with no scheduler installed the seam is optional — the due survives, "
                + "which is exactly the pre-fix production behaviour this package closes by installing it");
            Assert.AreEqual(1, scheduler.PopDue(999999f).Count,
                "the surviving due still fires (into the silent guard) without the install");
        }

        [Test]
        public void CompleteTask_WithSchedulerInstalled_CancelsPendingDue()
        {
            TaskAuthority authority = CreateAuthority();
            var scheduler = new DueEventScheduler();
            authority.SetDueEventScheduler(scheduler);
            var budgets = new WorkTimeBudgetStore();
            WorkTask task = CreateAssignedStarted(authority, budgets);
            ScheduleTaskCompletionDue(scheduler, task);

            Assert.IsTrue(authority.CompleteTask(task.TaskId, TaskOutcome.Success, 1, budgets));
            Assert.AreEqual(TaskStatus.Complete, task.Status);
            Assert.AreEqual(0, scheduler.ScheduledCount,
                "completion is a terminal transition — its due must not survive either");
        }

        [Test]
        public void InterruptTask_WithSchedulerInstalled_CancelsPendingDue()
        {
            TaskAuthority authority = CreateAuthority();
            var scheduler = new DueEventScheduler();
            authority.SetDueEventScheduler(scheduler);
            var budgets = new WorkTimeBudgetStore();
            WorkTask task = CreateAssignedStarted(authority, budgets);
            ScheduleTaskCompletionDue(scheduler, task);
            string reason;

            Assert.IsTrue(authority.InterruptTask(task.TaskId, budgets, out reason), reason);
            Assert.AreEqual(TaskStatus.Queued, task.Status,
                "interruption requeues the task for reassignment");
            Assert.AreEqual(0, scheduler.ScheduledCount,
                "the interrupted task's due is cancelled — a fresh due is scheduled when it restarts");
        }

        [Test]
        public void MarkReworkRequired_WithSchedulerInstalled_CancelsPendingDue()
        {
            TaskAuthority authority = CreateAuthority();
            var scheduler = new DueEventScheduler();
            authority.SetDueEventScheduler(scheduler);
            var budgets = new WorkTimeBudgetStore();
            WorkTask task = CreateAssignedStarted(authority, budgets);
            string reason;

            Assert.IsTrue(authority.CompleteTask(task.TaskId, TaskOutcome.RecoverableFailure, 1, budgets));
            Assert.AreEqual(TaskStatus.Failed, task.Status);
            // A retry re-armed while the task was open is still pending at
            // the moment the failure is marked rework-required.
            ScheduleTaskCompletionDue(scheduler, task);
            Assert.AreEqual(1, scheduler.ScheduledCount);

            Assert.IsTrue(authority.MarkReworkRequired(task.TaskId, default(EntityId), "test rework", out reason), reason);
            Assert.AreEqual(TaskStatus.ReworkRequired, task.Status);
            Assert.AreEqual(0, scheduler.ScheduledCount,
                "the fourth terminal path through ReleaseTaskResources cancels too");
        }

        [Test]
        public void CancelTask_CancelsOnlyThatTasksDueEvents()
        {
            TaskAuthority authority = CreateAuthority();
            var scheduler = new DueEventScheduler();
            authority.SetDueEventScheduler(scheduler);
            var budgets = new WorkTimeBudgetStore();
            WorkTask first = CreateAssignedStarted(authority, budgets);
            WorkTask second = authority.CreateTask("test.cancel-due", Business(1), 1);
            Assert.IsTrue(authority.AssignTask(second.TaskId, Person(2), budgets, out string reason), reason);
            Assert.IsTrue(authority.StartTask(second.TaskId, 1, out reason), reason);
            ScheduleTaskCompletionDue(scheduler, first);
            ScheduleTaskCompletionDue(scheduler, second, 7200f);
            Assert.AreEqual(2, scheduler.ScheduledCount);

            Assert.IsTrue(authority.CancelTask(first.TaskId, budgets));
            Assert.AreEqual(1, scheduler.ScheduledCount,
                "only the cancelled task's due is removed");
            List<DueEvent> due = scheduler.PopDue(999999f);
            Assert.AreEqual(1, due.Count);
            Assert.AreEqual(second.TaskId.ToString(), due[0].PayloadRef,
                "the surviving due belongs to the task that was not cancelled");
        }

        [Test]
        public void CancelTask_CancelsStableReArmIdToo()
        {
            TaskAuthority authority = CreateAuthority();
            var scheduler = new DueEventScheduler();
            authority.SetDueEventScheduler(scheduler);
            var budgets = new WorkTimeBudgetStore();
            WorkTask task = CreateAssignedStarted(authority, budgets);
            string taskId = task.TaskId.ToString();
            string stableId = DueEventIdConventions.ForTask(taskId, DueEventKind.PassiveProcessComplete);

            Assert.IsNull(scheduler.ScheduleWithEventId(stableId, DueEventKind.PassiveProcessComplete, 3600f, taskId));
            Assert.AreEqual(1, scheduler.ScheduledCount);

            Assert.IsTrue(authority.CancelTask(task.TaskId, budgets));
            Assert.AreEqual(0, scheduler.ScheduledCount,
                "CancelForPayload cancels by payload, regardless of the stable event id");
            Assert.IsFalse(scheduler.TryGetDueGameSeconds(stableId, out _),
                "the re-armed retry must not fire after the cancel");
        }

        [Test]
        public void CancelTask_UninstalledScheduler_IsNotConsulted()
        {
            TaskAuthority authority = CreateAuthority();
            var installed = new DueEventScheduler();
            var uninstalled = new DueEventScheduler();
            authority.SetDueEventScheduler(installed);
            var budgets = new WorkTimeBudgetStore();
            WorkTask task = CreateAssignedStarted(authority, budgets);
            ScheduleTaskCompletionDue(uninstalled, task);
            Assert.AreEqual(1, uninstalled.ScheduledCount);

            Assert.IsTrue(authority.CancelTask(task.TaskId, budgets));
            Assert.AreEqual(1, uninstalled.ScheduledCount,
                "the cancel consults the installed scheduler only — a due on "
                + "another scheduler instance is untouched");
            Assert.AreEqual(0, installed.ScheduledCount);
        }

        [Test]
        public void CancelTask_TaskWithNoPendingDue_SucceedsSilently()
        {
            TaskAuthority authority = CreateAuthority();
            var scheduler = new DueEventScheduler();
            authority.SetDueEventScheduler(scheduler);
            var budgets = new WorkTimeBudgetStore();
            WorkTask task = CreateAssignedStarted(authority, budgets);

            Assert.IsTrue(authority.CancelTask(task.TaskId, budgets));
            Assert.AreEqual(TaskStatus.Cancelled, task.Status);
            Assert.AreEqual(0, scheduler.ScheduledCount,
                "cancelling a task with no pending due is a no-op on the scheduler, never an error");
        }

        [Test]
        public void SetDueEventScheduler_InstallsAndIsReplaceable()
        {
            var authority = new TaskAuthority();
            var first = new DueEventScheduler();
            var second = new DueEventScheduler();

            authority.SetDueEventScheduler(first);
            Assert.IsTrue(ReferenceEquals(first, authority.DueEventScheduler),
                "the installed scheduler is the one the authority consults");
            authority.SetDueEventScheduler(second);
            Assert.IsTrue(ReferenceEquals(second, authority.DueEventScheduler),
                "installing again replaces the previous scheduler");
            authority.SetDueEventScheduler(null);
            Assert.IsNull(authority.DueEventScheduler,
                "installing null restores the no-linkage behaviour");
        }

        // ---------- repo-file helpers (CampaignCTaskPruningTests precedent) ----------

        private const string DriversPath = "Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs";
        private const string LifecyclePath = "Assets/Scripts/Core/Tasks/TaskAuthorityLifecycle.cs";
        private const string AuthorityPath = "Assets/Scripts/Core/Tasks/TaskAuthority.cs";

        private static string FindRepoRoot()
        {
            string fromEnv = Environment.GetEnvironmentVariable("LAND_LEDGERS_REPO");
            if (!string.IsNullOrWhiteSpace(fromEnv)
                && File.Exists(Path.Combine(fromEnv, AuthorityPath)))
            {
                return fromEnv;
            }

            var starts = new[]
            {
                System.IO.Directory.GetCurrentDirectory(),
                AppContext.BaseDirectory,
            };
            var tried = new StringBuilder();
            foreach (string start in starts)
            {
                string dir = start;
                while (!string.IsNullOrEmpty(dir))
                {
                    foreach (string candidate in new[]
                    {
                        dir,
                        Path.Combine(dir, "work-c"),
                    })
                    {
                        tried.AppendLine("  " + candidate);
                        if (File.Exists(Path.Combine(candidate, AuthorityPath)))
                        {
                            return candidate;
                        }
                    }
                    DirectoryInfo parent = System.IO.Directory.GetParent(dir);
                    dir = parent == null ? null : parent.FullName;
                }
            }

            Assert.IsTrue(false, "Campaign C (DEF-062): could not locate the Land & Ledgers repo. "
                + "Set LAND_LEDGERS_REPO, or run from the campaign tree. Searched:\n"
                + tried + "  LAND_LEDGERS_REPO=" + (fromEnv ?? "<unset>"));
            return null;
        }

        private static string ReadRepoFile(string relativePath)
        {
            string root = FindRepoRoot();
            string full = Path.Combine(root, relativePath);
            Assert.IsTrue(File.Exists(full), "Campaign C (DEF-062): expected repo file missing: " + full);
            return File.ReadAllText(full);
        }

        private static string MethodBody(string source, string signature)
        {
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.IsTrue(start >= 0, "Campaign C (DEF-062): method not found: " + signature);
            int next = source.Length;
            foreach (string marker in new[] { "\n        private ", "\n        public ", "\n        protected ", "\n        internal " })
            {
                int at = source.IndexOf(marker, start + signature.Length, StringComparison.Ordinal);
                if (at >= 0 && at < next) next = at;
            }
            return source.Substring(start, next - start);
        }

        /// <summary>
        /// Returns source with comments, string literals and char literals
        /// blanked (newlines preserved), so assertions cannot be fooled by a
        /// mention inside a comment or a log string.
        /// </summary>
        internal static string StripNonCode(string source)
        {
            var out_ = new StringBuilder(source.Length);
            int i = 0;
            while (i < source.Length)
            {
                char c = source[i];
                char next = i + 1 < source.Length ? source[i + 1] : '\0';
                if (c == '/' && next == '/')
                {
                    while (i < source.Length && source[i] != '\n')
                    {
                        out_.Append(' ');
                        i++;
                    }
                }
                else if (c == '/' && next == '*')
                {
                    out_.Append("  ");
                    i += 2;
                    while (i < source.Length && !(source[i] == '*' && i + 1 < source.Length && source[i + 1] == '/'))
                    {
                        out_.Append(source[i] == '\n' ? '\n' : ' ');
                        i++;
                    }
                    if (i < source.Length)
                    {
                        out_.Append("  ");
                        i += 2;
                    }
                }
                else if (c == '@' && next == '"')
                {
                    out_.Append("  ");
                    i += 2;
                    while (i < source.Length)
                    {
                        if (source[i] == '"' && i + 1 < source.Length && source[i + 1] == '"')
                        {
                            out_.Append("  ");
                            i += 2;
                        }
                        else if (source[i] == '"')
                        {
                            out_.Append(' ');
                            i++;
                            break;
                        }
                        else
                        {
                            out_.Append(source[i] == '\n' ? '\n' : ' ');
                            i++;
                        }
                    }
                }
                else if (c == '"')
                {
                    out_.Append(' ');
                    i++;
                    while (i < source.Length && source[i] != '\n')
                    {
                        if (source[i] == '\\' && i + 1 < source.Length)
                        {
                            out_.Append("  ");
                            i += 2;
                        }
                        else if (source[i] == '"')
                        {
                            out_.Append(' ');
                            i++;
                            break;
                        }
                        else
                        {
                            out_.Append(' ');
                            i++;
                        }
                    }
                }
                else if (c == '\'')
                {
                    out_.Append(' ');
                    i++;
                    while (i < source.Length && source[i] != '\n')
                    {
                        if (source[i] == '\\' && i + 1 < source.Length)
                        {
                            out_.Append("  ");
                            i += 2;
                        }
                        else if (source[i] == '\'')
                        {
                            out_.Append(' ');
                            i++;
                            break;
                        }
                        else
                        {
                            out_.Append(' ');
                            i++;
                        }
                    }
                }
                else
                {
                    out_.Append(c);
                    i++;
                }
            }
            return out_.ToString();
        }

        [Test]
        public void SimulationDriversAwake_InstallsHubDueEventScheduler()
        {
            string code = StripNonCode(ReadRepoFile(DriversPath));
            string budgetSeam = "hub.Tasks.SetWorkTimeBudgetStore(hub.WorkTimeBudgets);";
            string dueSeam = "hub.Tasks.SetDueEventScheduler(hub.DueEvents);";
            int budgetAt = code.IndexOf(budgetSeam, StringComparison.Ordinal);
            int dueAt = code.IndexOf(dueSeam, StringComparison.Ordinal);
            Assert.IsTrue(budgetAt >= 0, "Campaign C (DEF-062): sibling seam not found in SimulationDrivers.cs");
            Assert.IsTrue(dueAt >= 0,
                "Campaign C (DEF-062): SimulationDrivers.Awake must install the hub's due-event scheduler "
                + "on the hub's TaskAuthority — SetDueEventScheduler had zero production callers");
            Assert.IsTrue(dueAt > budgetAt,
                "Campaign C (DEF-062): the due-event install sits with the other hub seam installs in Awake");
        }

        [Test]
        public void SetDueEventScheduler_HasProductionCaller()
        {
            string code = StripNonCode(ReadRepoFile(DriversPath));
            Assert.IsTrue(code.Contains("SetDueEventScheduler("),
                "Campaign C (DEF-062): no production caller of SetDueEventScheduler exists — "
                + "the seam is still uninstalled (a comment mentioning it does not count)");
        }

        [Test]
        public void ReleaseTaskResources_CancelsByPayloadAndLogsOnlyWhenSomethingCancelled()
        {
            string lifecycle = ReadRepoFile(LifecyclePath);
            string body = StripNonCode(MethodBody(lifecycle, "private void ReleaseTaskResources(WorkTask task)"));
            Assert.IsTrue(body.Contains("dueEventScheduler.CancelForPayload(task.TaskId.ToString())"),
                "Campaign C (DEF-062): ReleaseTaskResources must cancel the task's pending dues by payload");
            Assert.IsTrue(body.Contains("ScheduledCount"),
                "Campaign C (DEF-062): the diagnostic measures before/after ScheduledCount so it "
                + "fires only when a due was actually cancelled");
            Assert.IsTrue(body.Contains("Debug.Log("),
                "Campaign C (DEF-062): the cancellation is logged when it happens");
            Assert.IsTrue(body.Contains("cancelledDueEvents > 0"),
                "Campaign C (DEF-062): the log is gated on a real cancellation, so terminal "
                + "transitions for tasks with no pending due stay silent");
        }
    }
}
