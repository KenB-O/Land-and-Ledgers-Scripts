// Campaign C (DEF-074) — Reservation authority seam wired.
//
// The reservation release already existed: every terminal transition
// routes through ReleaseTaskResources (TaskAuthorityLifecycle.cs), which
// calls reservationAuthority.ReleaseAllForTask(task id) when an authority
// is installed. But SetReservationAuthority had zero production callers,
// so the branch was dead in production and reservations held by a task
// were never released when the task terminalized. This package installs
// the hub's reservation authority on the hub's TaskAuthority at the
// composition root (SimulationDrivers.Awake, next to the sibling
// SetWorkTimeBudgetStore and SetDueEventScheduler seams) and adds a
// diagnostic that logs only when a terminal transition actually releases
// a reservation.
//
// Three test kinds, same file:
//  (1) Behavioural TaskAuthority tests (real authority + real
//      EquipmentReservationService): cancel / complete / interrupt /
//      rework-required each release the task's reservations; without an
//      installed authority the reservations survive (the seam is
//      optional, the old behaviour pinned).
//  (2) Two tests pinning the owner-key form gap (the headline known
//      edge): production reservations owned by a task use the "task:"-
//      prefixed form (ManagerWorkAllocator, TaskIdentityConventions.
//      ReservationOwnerKey), while ReleaseTaskResources releases the
//      bare task-id string — so the prefixed form survives a terminal
//      transition even with the authority installed; and StartTask does
//      not auto-reserve, because the request builder is a second,
//      still-uninstalled seam.
//  (3) Structural tests pinning the SimulationDrivers wiring by reading
//      the production file — SimulationDrivers is too large a closure for
//      the focused harness, so the wiring itself (not Unity behaviour) is
//      what is asserted here, exactly like the package-6 prune and
//      package-15 due-event tests.
// The repo root comes from LAND_LEDGERS_REPO, then a walk up from the
// current directory and the test assembly location (campaign layout:
// <root>/work-c/Assets/...), mirroring CampaignCTaskPruningTests. A
// missing repo fails loudly, never silently passes.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LandLedgers.Economy.Equipment;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Tasks
{
    [TestFixture]
    public sealed class CampaignCReservationAuthorityTests
    {
        private static EntityId Person(int id) => EntityId.For(EntityKind.Person, id);
        private static EntityId Business(int id) => EntityId.For(EntityKind.Business, id);

        private static TaskAuthority CreateAuthority(string definitionId = "test.release-reservation", int baseMinutes = 10)
        {
            var authority = new TaskAuthority();
            string reason;
            Assert.IsTrue(authority.RegisterDefinition(new TaskDefinition(definitionId, "ReleaseReservation", baseMinutes), out reason), reason);
            return authority;
        }

        private static WorkTask CreateAssignedStarted(TaskAuthority authority, WorkTimeBudgetStore budgets, string definitionId = "test.release-reservation")
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
        /// Reserves one exclusive resource for the task, owned by the bare
        /// task-id string — the form ReleaseTaskResources releases
        /// (TaskAuthorityLifecycle: the SetReservationRequestBuilder doc
        /// requires the builder to use the task's EntityId string so
        /// ReleaseAllForTask(task.TaskId.ToString()) matches).
        /// </summary>
        private static void ReserveForTask(EquipmentReservationService service, WorkTask task, string resourceKey = "workstation:bench-1")
        {
            var request = new ReservationRequest(task.TaskId.ToString(), "test reservation for " + task.TaskId);
            request.Items.Add(new ReservationItem(resourceKey, 1f, 0f, 3600f));
            string refusal = service.TryReserveAll(request, out ReservationHandle handle);
            Assert.IsNull(refusal, "test setup: reservation must be granted — " + refusal);
            Assert.IsNotNull(handle);
        }

        /// <summary>
        /// Reserves one exclusive resource for the task, owned by the
        /// "task:"-prefixed form — the form production actually uses
        /// (ManagerWorkAllocator: "task:" + task.TaskId;
        /// TaskIdentityConventions.ReservationOwnerKey). The bare-form
        /// release in ReleaseTaskResources does NOT match this owner.
        /// </summary>
        private static void ReserveForTaskPrefixed(EquipmentReservationService service, WorkTask task, string resourceKey = "workstation:bench-1")
        {
            var request = new ReservationRequest("task:" + task.TaskId, "test reservation for " + task.TaskId);
            request.Items.Add(new ReservationItem(resourceKey, 1f, 0f, 3600f));
            string refusal = service.TryReserveAll(request, out ReservationHandle handle);
            Assert.IsNull(refusal, "test setup: reservation must be granted — " + refusal);
            Assert.IsNotNull(handle);
        }

        [Test]
        public void CancelTask_WithReservationAuthorityInstalled_ReleasesTaskReservation()
        {
            TaskAuthority authority = CreateAuthority();
            var reservations = new EquipmentReservationService();
            authority.SetReservationAuthority(reservations);
            var budgets = new WorkTimeBudgetStore();
            WorkTask task = CreateAssignedStarted(authority, budgets);
            ReserveForTask(reservations, task);
            Assert.AreEqual(1, reservations.ReservationsForTask(task.TaskId.ToString()).Count);
            Assert.AreEqual(1, reservations.LiveReservationCount);

            Assert.IsTrue(authority.CancelTask(task.TaskId, budgets));
            Assert.AreEqual(TaskStatus.Cancelled, task.Status);
            Assert.AreEqual(0, reservations.ReservationsForTask(task.TaskId.ToString()).Count,
                "the cancelled task's reservation must not survive");
            Assert.AreEqual(0, reservations.LiveReservationCount,
                "no leaked reservation may remain held after the terminal transition");
        }

        [Test]
        public void CancelTask_WithoutReservationAuthorityInstalled_ReservationSurvives()
        {
            TaskAuthority authority = CreateAuthority();
            var reservations = new EquipmentReservationService();
            var budgets = new WorkTimeBudgetStore();
            WorkTask task = CreateAssignedStarted(authority, budgets);
            ReserveForTask(reservations, task);
            Assert.AreEqual(1, reservations.LiveReservationCount);

            Assert.IsTrue(authority.CancelTask(task.TaskId, budgets));
            Assert.AreEqual(TaskStatus.Cancelled, task.Status);
            Assert.AreEqual(1, reservations.LiveReservationCount,
                "with no authority installed the seam is optional — the reservation survives, "
                + "which is exactly the pre-fix production behaviour this package closes by installing it");
            Assert.AreEqual(1, reservations.ReservationsForTask(task.TaskId.ToString()).Count,
                "the surviving reservation is still held under the task's owner id");
        }

        [Test]
        public void CompleteTask_WithReservationAuthorityInstalled_ReleasesReservation()
        {
            TaskAuthority authority = CreateAuthority();
            var reservations = new EquipmentReservationService();
            authority.SetReservationAuthority(reservations);
            var budgets = new WorkTimeBudgetStore();
            WorkTask task = CreateAssignedStarted(authority, budgets);
            ReserveForTask(reservations, task);

            Assert.IsTrue(authority.CompleteTask(task.TaskId, TaskOutcome.Success, 1, budgets));
            Assert.AreEqual(TaskStatus.Complete, task.Status);
            Assert.AreEqual(0, reservations.ReservationsForTask(task.TaskId.ToString()).Count,
                "completion is a terminal transition — its reservation must not survive either");
        }

        [Test]
        public void InterruptTask_WithReservationAuthorityInstalled_ReleasesReservation()
        {
            TaskAuthority authority = CreateAuthority();
            var reservations = new EquipmentReservationService();
            authority.SetReservationAuthority(reservations);
            var budgets = new WorkTimeBudgetStore();
            WorkTask task = CreateAssignedStarted(authority, budgets);
            ReserveForTask(reservations, task);
            string reason;

            Assert.IsTrue(authority.InterruptTask(task.TaskId, budgets, out reason), reason);
            Assert.AreEqual(TaskStatus.Queued, task.Status,
                "interruption requeues the task for reassignment");
            Assert.AreEqual(0, reservations.ReservationsForTask(task.TaskId.ToString()).Count,
                "the interrupted task's reservation is released — a fresh reservation is "
                + "made when it restarts, once a request builder exists (see the StartTask test below)");
        }

        [Test]
        public void MarkReworkRequired_WithReservationAuthorityInstalled_ReleasesReservation()
        {
            TaskAuthority authority = CreateAuthority();
            var reservations = new EquipmentReservationService();
            authority.SetReservationAuthority(reservations);
            var budgets = new WorkTimeBudgetStore();
            WorkTask task = CreateAssignedStarted(authority, budgets);
            string reason;

            Assert.IsTrue(authority.CompleteTask(task.TaskId, TaskOutcome.RecoverableFailure, 1, budgets));
            Assert.AreEqual(TaskStatus.Failed, task.Status);
            // A reservation held while the task was open is still pending at
            // the moment the failure is marked rework-required.
            ReserveForTask(reservations, task);
            Assert.AreEqual(1, reservations.LiveReservationCount);

            Assert.IsTrue(authority.MarkReworkRequired(task.TaskId, default(EntityId), "test rework", out reason), reason);
            Assert.AreEqual(TaskStatus.ReworkRequired, task.Status);
            Assert.AreEqual(0, reservations.ReservationsForTask(task.TaskId.ToString()).Count,
                "the fourth terminal path through ReleaseTaskResources releases too");
        }

        [Test]
        public void CancelTask_ReleasesOnlyThatTasksReservations()
        {
            TaskAuthority authority = CreateAuthority();
            var reservations = new EquipmentReservationService();
            authority.SetReservationAuthority(reservations);
            var budgets = new WorkTimeBudgetStore();
            WorkTask first = CreateAssignedStarted(authority, budgets);
            WorkTask second = authority.CreateTask("test.release-reservation", Business(1), 1);
            Assert.IsTrue(authority.AssignTask(second.TaskId, Person(2), budgets, out string reason), reason);
            Assert.IsTrue(authority.StartTask(second.TaskId, 1, out reason), reason);
            ReserveForTask(reservations, first, "workstation:bench-1");
            ReserveForTask(reservations, second, "workstation:bench-2");
            Assert.AreEqual(2, reservations.LiveReservationCount);

            Assert.IsTrue(authority.CancelTask(first.TaskId, budgets));
            Assert.AreEqual(1, reservations.LiveReservationCount,
                "only the cancelled task's reservation is released");
            Assert.AreEqual(0, reservations.ReservationsForTask(first.TaskId.ToString()).Count);
            Assert.AreEqual(1, reservations.ReservationsForTask(second.TaskId.ToString()).Count,
                "the surviving reservation belongs to the task that was not cancelled");
        }

        [Test]
        public void CancelTask_UninstalledAuthority_IsNotConsulted()
        {
            TaskAuthority authority = CreateAuthority();
            var installed = new EquipmentReservationService();
            var uninstalled = new EquipmentReservationService();
            authority.SetReservationAuthority(installed);
            var budgets = new WorkTimeBudgetStore();
            WorkTask task = CreateAssignedStarted(authority, budgets);
            ReserveForTask(uninstalled, task);
            Assert.AreEqual(1, uninstalled.LiveReservationCount);

            Assert.IsTrue(authority.CancelTask(task.TaskId, budgets));
            Assert.AreEqual(1, uninstalled.LiveReservationCount,
                "the release consults the installed authority only — a reservation on "
                + "another authority instance is untouched");
            Assert.AreEqual(0, installed.LiveReservationCount);
        }

        [Test]
        public void CancelTask_TaskWithNoReservation_SucceedsSilently()
        {
            TaskAuthority authority = CreateAuthority();
            var reservations = new EquipmentReservationService();
            authority.SetReservationAuthority(reservations);
            var budgets = new WorkTimeBudgetStore();
            WorkTask task = CreateAssignedStarted(authority, budgets);

            Assert.IsTrue(authority.CancelTask(task.TaskId, budgets));
            Assert.AreEqual(TaskStatus.Cancelled, task.Status);
            Assert.AreEqual(0, reservations.LiveReservationCount,
                "cancelling a task with no reservation is a no-op on the authority, never an error");
        }

        [Test]
        public void SetReservationAuthority_InstallsAndIsReplaceable()
        {
            var authority = new TaskAuthority();
            var first = new EquipmentReservationService();
            var second = new EquipmentReservationService();

            authority.SetReservationAuthority(first);
            Assert.IsTrue(ReferenceEquals(first, authority.ReservationAuthority),
                "the installed authority is the one the authority consults");
            authority.SetReservationAuthority(second);
            Assert.IsTrue(ReferenceEquals(second, authority.ReservationAuthority),
                "installing again replaces the previous authority");
            authority.SetReservationAuthority(null);
            Assert.IsNull(authority.ReservationAuthority,
                "installing null restores the no-release behaviour");
        }

        [Test]
        public void CancelTask_PrefixedOwnerForm_IsNotReleasedByBareFormRelease()
        {
            // HEADLINE KNOWN EDGE (DEF-074): production reservations owned
            // by a task use the "task:"-prefixed owner form
            // (ManagerWorkAllocator: "task:" + task.TaskId;
            // TaskIdentityConventions.ReservationOwnerKey), while
            // ReleaseTaskResources releases the bare task-id string
            // (task.TaskId.ToString(), e.g. "T12"). The two strings are
            // never equal, so the release — now live after this install —
            // still matches no production reservation. This test pins that
            // gap in code: it must be revisited when the owner-key form
            // is ruled on (release the prefixed form too, or reserve under
            // the bare form). It is NOT a failure of the install.
            TaskAuthority authority = CreateAuthority();
            var reservations = new EquipmentReservationService();
            authority.SetReservationAuthority(reservations);
            var budgets = new WorkTimeBudgetStore();
            WorkTask task = CreateAssignedStarted(authority, budgets);
            ReserveForTaskPrefixed(reservations, task);
            Assert.AreEqual(1, reservations.ReservationsForTask("task:" + task.TaskId).Count);
            Assert.AreEqual(0, reservations.ReservationsForTask(task.TaskId.ToString()).Count,
                "the prefixed owner and the bare task id are different owner keys");

            Assert.IsTrue(authority.CancelTask(task.TaskId, budgets));
            Assert.AreEqual(TaskStatus.Cancelled, task.Status);
            Assert.AreEqual(1, reservations.ReservationsForTask("task:" + task.TaskId).Count,
                "the prefixed-form reservation survives the terminal transition — "
                + "the bare-form release does not match it");
            Assert.AreEqual(1, reservations.LiveReservationCount);
        }

        [Test]
        public void StartTask_WithAuthorityButNoBuilder_DoesNotAutoReserve()
        {
            // Second dead seam (DEF-074, known edge): StartTask auto-reserves
            // only when BOTH the authority AND a request builder are installed
            // (TaskAuthority:414). SetReservationRequestBuilder has zero
            // production callers, so even with the authority installed no
            // reservation is created at start — the release branch has
            // nothing to release for a task that never reserved. This test
            // pins that the install alone does not change start behaviour.
            TaskAuthority authority = CreateAuthority();
            var reservations = new EquipmentReservationService();
            authority.SetReservationAuthority(reservations);
            var budgets = new WorkTimeBudgetStore();
            WorkTask task = CreateAssignedStarted(authority, budgets);

            Assert.AreEqual(TaskStatus.InProgress, task.Status);
            Assert.AreEqual(0, reservations.LiveReservationCount,
                "no builder installed means StartTask reserves nothing, even with the authority installed");
            Assert.AreEqual(0, reservations.ReservationsForTask(task.TaskId.ToString()).Count);
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

            Assert.IsTrue(false, "Campaign C (DEF-074): could not locate the Land & Ledgers repo. "
                + "Set LAND_LEDGERS_REPO, or run from the campaign tree. Searched:\n"
                + tried + "  LAND_LEDGERS_REPO=" + (fromEnv ?? "<unset>"));
            return null;
        }

        private static string ReadRepoFile(string relativePath)
        {
            string root = FindRepoRoot();
            string full = Path.Combine(root, relativePath);
            Assert.IsTrue(File.Exists(full), "Campaign C (DEF-074): expected repo file missing: " + full);
            return File.ReadAllText(full);
        }

        private static string MethodBody(string source, string signature)
        {
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.IsTrue(start >= 0, "Campaign C (DEF-074): method not found: " + signature);
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
        public void SimulationDriversAwake_InstallsHubReservationAuthority()
        {
            string code = StripNonCode(ReadRepoFile(DriversPath));
            string budgetSeam = "hub.Tasks.SetWorkTimeBudgetStore(hub.WorkTimeBudgets);";
            string dueSeam = "hub.Tasks.SetDueEventScheduler(hub.DueEvents);";
            string reservationSeam = "hub.Tasks.SetReservationAuthority(hub.ReservationAuthority);";
            int budgetAt = code.IndexOf(budgetSeam, StringComparison.Ordinal);
            int dueAt = code.IndexOf(dueSeam, StringComparison.Ordinal);
            int reservationAt = code.IndexOf(reservationSeam, StringComparison.Ordinal);
            Assert.IsTrue(budgetAt >= 0, "Campaign C (DEF-074): sibling seam not found in SimulationDrivers.cs");
            Assert.IsTrue(dueAt >= 0, "Campaign C (DEF-074): DEF-062 sibling seam not found in SimulationDrivers.cs");
            Assert.IsTrue(reservationAt >= 0,
                "Campaign C (DEF-074): SimulationDrivers.Awake must install the hub's reservation authority "
                + "on the hub's TaskAuthority — SetReservationAuthority had zero production callers");
            Assert.IsTrue(reservationAt > dueAt,
                "Campaign C (DEF-074): the reservation install sits with the other hub seam installs in Awake");
        }

        [Test]
        public void SetReservationAuthority_HasProductionCaller()
        {
            string code = StripNonCode(ReadRepoFile(DriversPath));
            Assert.IsTrue(code.Contains("SetReservationAuthority("),
                "Campaign C (DEF-074): no production caller of SetReservationAuthority exists — "
                + "the seam is still uninstalled (a comment mentioning it does not count)");
        }

        [Test]
        public void ReleaseTaskResources_ReleasesByOwnerIdAndLogsOnlyWhenSomethingReleased()
        {
            string lifecycle = ReadRepoFile(LifecyclePath);
            string body = StripNonCode(MethodBody(lifecycle, "private void ReleaseTaskResources(WorkTask task)"));
            Assert.IsTrue(body.Contains("reservationAuthority.ReleaseAllForTask(task.TaskId.ToString())"),
                "Campaign C (DEF-074): ReleaseTaskResources must release the task's reservations by owner id");
            Assert.IsTrue(body.Contains("ReservationsForTask(task.TaskId.ToString())"),
                "Campaign C (DEF-074): the diagnostic measures before/after ReservationsForTask so it "
                + "fires only when a reservation was actually released");
            Assert.IsTrue(body.Contains("Debug.Log("),
                "Campaign C (DEF-074): the release is logged when it happens");
            Assert.IsTrue(body.Contains("releasedReservations > 0"),
                "Campaign C (DEF-074): the log is gated on a real release, so terminal "
                + "transitions for tasks with no reservation stay silent");
        }
    }
}
