// Campaign C, package 10 — DEF-050 (freight dispatch hold has no
// timeout), Nov 15, 2026.
//
// VERDICT (verify-then-fix): the hold is genuinely unbounded, and no
// timeout is invented here. Verified, not assumed:
//  * The hold releases ONLY through FreightProductionDriver.
//    AuthorizeShipmentDispatch, called ONLY from
//    SimulationDrivers.HandleCompletedFreightDispatchTask — the
//    successful-completion path for a dispatch task.
//    FreightProductionDriver.AdvanceTick skips held shipments entirely,
//    so a held shipment never advances and never expires; there is no
//    agreement expiry and no journey expiry (the journey is created at
//    departure, which never happens while held).
//  * A dispatch task CAN terminalize without authorizing: the 1-minute
//    handshake completes through CompleteTaskWithCausalOutcome, whose
//    causal outcome can be RecoverableFailure / SeriousFailure (real
//    resolver paths), landing the task in Status Failed; any CancelTask
//    path lands it in Cancelled. Neither status reaches the completion
//    handler (Status != Complete) or the short-tick reconciler (Complete
//    only), and no handler for a non-completed dispatch task exists.
//  * agreement.DispatchTaskId is write-once (RegisterDispatchTask refuses
//    a different id; nothing ever clears it), and
//    CreatePreparedFreightDispatchTask is called from exactly three
//    sites — all on the hitching/preparation path — none on a
//    dispatch-failure path. A recorded task that still resolves with the
//    matching stable reference returns silently with no new task; a
//    recorded id that stops resolving returns with "no replacement was
//    created" (logged once per session).
//  * The DEF-049 prune converts the strand, it does not cause it: 7 days
//    after terminalization the Failed / Cancelled / Complete task is
//    pruned (the external-reference predicate protects shopping-trip
//    tasks only), so the recorded id stops resolving. The hold is
//    already unreleasable the moment the task terminalizes without
//    authorization. The predicate is deliberately NOT extended to
//    dispatch tasks — preserving a dead record forever would not fix
//    the hold.
//  * A completed-but-refused task is a third strand mode: the handler's
//    identity checks refuse permanently (the identities and write-once
//    flags never change after preparation), logged once, re-refused by
//    the reconciler every short tick. The one transient sub-case — the
//    operator not visually at staging — self-heals on arrival and is
//    not a strand.
//
// What IS fixed here is diagnostic, in three layers, all
// behaviour-neutral (no task created, no timeout, no expiry, no
// cancellation policy):
//  (1) A terminal recorded dispatch task now logs once per day
//      (day-keyed, LogRefusalDayKeyed), naming the task, its terminal
//      status and outcome, the day it terminalized, and the hold age.
//  (2) A recorded dispatch task id that no longer resolves now logs
//      once per day instead of once per session, naming the recorded
//      id and the hold age since acceptance.
//  (3) The prerequisite refusals on the same creation path (unresolved
//      context, operator mismatch, state mismatch, team attachment,
//      unlinked existing task, and the daily driver's
//      missing-prerequisite line) now log once per day instead of once
//      per session, so a held shipment whose task was never created
//      stays visible too. They retry daily and can self-heal; only
//      their logging cadence changes.
//
// The disposition — whether a hold should expire, and if a dispatch
// task fails or is cancelled, whether a new dispatch task should be
// created, the shipment should return to Planned for re-preparation,
// or the agreement should be cancelled — is A-to-Z Check 10 question
// 4, an owner decision (new question (m)), and is NOT invented here.
//
// Two test kinds, same file:
//  (1) Real unit tests for the pure reason helper — every strand
//      reason, the live-path nulls, first-failure ordering, and the
//      day-count wording.
//  (2) Structural tests pinning the Unity-bound wiring by reading the
//      production files — SimulationDrivers is too large a closure for
//      the focused harness, so the wiring itself (not Unity behaviour)
//      is what is asserted here, exactly like the package-2/3/4/5/7/8/9
//      tests.
// The repo root comes from LAND_LEDGERS_REPO, then a walk up from the
// current directory and the test assembly location (campaign layout:
// <root>/work-c/Assets/...). A missing repo fails loudly, never silently
// passes.
using System;
using System.IO;
using System.Text;
using LandLedgers.Economy.Transport;
using NUnit.Framework;

namespace LandLedgers.Editor.Population
{
    [TestFixture]
    public sealed class CampaignCDispatchHoldStrandTests
    {
        private const string DriversPath =
            "Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs";
        private const string SaveLoadManagerPath =
            "Assets/Scripts/Infrastructure/Persistence/SaveLoadManager.cs";

        private const string ShipmentId = "shipment-millbrook-flour-001";
        private const string AgreementId = "agreement-millbrook-001";
        private const string TaskId = "task:dispatch-001";
        private const string TaskRef = "freight-dispatch:agreement-millbrook-001:shipment-millbrook-flour-001";

        // ---------- (1) Unit tests for the pure helper ----------

        [Test]
        public void NoRecordedTaskId_ReturnsNull()
        {
            // No task id recorded yet: the task was never created. That
            // stall belongs to the prerequisite branches, not to this
            // helper — Describe must stay silent so the driver does not
            // log a strand line for a hold that has not stranded yet.
            var state = new DispatchHoldStrandReason.DispatchHoldTaskState(
                ShipmentId, AgreementId, string.Empty, false, string.Empty, TaskRef,
                false, string.Empty, string.Empty, 10, 12);
            Assert.IsNull(DispatchHoldStrandReason.Describe(state, 45));
        }

        [Test]
        public void OpenQueuedTask_ReturnsNull()
        {
            // The recorded task still resolves and is still open: the live
            // path. The daily driver must stay silent.
            var state = new DispatchHoldStrandReason.DispatchHoldTaskState(
                ShipmentId, AgreementId, TaskId, true, TaskRef, TaskRef,
                true, "Queued", "None", 40, 12);
            Assert.IsNull(DispatchHoldStrandReason.Describe(state, 45));
        }

        [Test]
        public void OpenInProgressTask_ReturnsNull()
        {
            var state = new DispatchHoldStrandReason.DispatchHoldTaskState(
                ShipmentId, AgreementId, TaskId, true, TaskRef, TaskRef,
                true, "InProgress", "None", 40, 12);
            Assert.IsNull(DispatchHoldStrandReason.Describe(state, 45));
        }

        [Test]
        public void FailedTask_NamesStatusOutcomeAndAge()
        {
            // The causal failure strand: task failed on day 41, today is
            // day 45. The reason must name the status, the outcome, the
            // terminal day, the age, both ids, and that no re-creation
            // path exists, and must point at the owner decision.
            var state = new DispatchHoldStrandReason.DispatchHoldTaskState(
                ShipmentId, AgreementId, TaskId, true, TaskRef, TaskRef,
                false, "Failed", "RecoverableFailure", 41, 12);
            string reason = DispatchHoldStrandReason.Describe(state, 45);
            Assert.IsTrue(reason.Contains("Failed"), "Reason was: " + reason);
            Assert.IsTrue(reason.Contains("RecoverableFailure"), "Reason was: " + reason);
            Assert.IsTrue(reason.Contains("day 41"), "Reason was: " + reason);
            Assert.IsTrue(reason.Contains("4 days ago"), "Reason was: " + reason);
            Assert.IsTrue(reason.Contains(ShipmentId), "Reason was: " + reason);
            Assert.IsTrue(reason.Contains(TaskId), "Reason was: " + reason);
            Assert.IsTrue(reason.Contains("no dispatch task is re-created"), "Reason was: " + reason);
            Assert.IsTrue(reason.Contains("Check 10 Q4"), "Reason was: " + reason);
        }

        [Test]
        public void SeriousFailureTask_NamesSeriousFailure()
        {
            var state = new DispatchHoldStrandReason.DispatchHoldTaskState(
                ShipmentId, AgreementId, TaskId, true, TaskRef, TaskRef,
                false, "Failed", "SeriousFailure", 30, 12);
            string reason = DispatchHoldStrandReason.Describe(state, 37);
            Assert.IsTrue(reason.Contains("SeriousFailure"), "Reason was: " + reason);
            Assert.IsTrue(reason.Contains("7 days ago"), "Reason was: " + reason);
        }

        [Test]
        public void CancelledTask_NamesCancelledWithCreatedDayAnchor()
        {
            // MarkCancelled records no completion day and no outcome
            // (outcome stays None); the driver anchors the terminal day
            // on CreatedDayIndex, exactly like the prune. The reason
            // must name Cancelled and the created-day age.
            var state = new DispatchHoldStrandReason.DispatchHoldTaskState(
                ShipmentId, AgreementId, TaskId, true, TaskRef, TaskRef,
                false, "Cancelled", "None", 20, 12);
            string reason = DispatchHoldStrandReason.Describe(state, 45);
            Assert.IsTrue(reason.Contains("Cancelled"), "Reason was: " + reason);
            Assert.IsTrue(reason.Contains("day 20"), "Reason was: " + reason);
            Assert.IsTrue(reason.Contains("25 days ago"), "Reason was: " + reason);
        }

        [Test]
        public void CompleteButRefusedTask_NamesComplete()
        {
            // The identity-mismatch strand: the task completed with
            // Success, the handler refused it, and the reconciler
            // re-refuses it every short tick. Terminal, not live.
            var state = new DispatchHoldStrandReason.DispatchHoldTaskState(
                ShipmentId, AgreementId, TaskId, true, TaskRef, TaskRef,
                false, "Complete", "Success", 30, 12);
            string reason = DispatchHoldStrandReason.Describe(state, 37);
            Assert.IsTrue(reason.Contains("Complete"), "Reason was: " + reason);
            Assert.IsTrue(reason.Contains("Success"), "Reason was: " + reason);
            Assert.IsTrue(reason.Contains("7 days ago"), "Reason was: " + reason);
        }

        [Test]
        public void MissingRecord_NamesTaskAndHoldAge()
        {
            // The prune-conversion strand: the recorded id no longer
            // resolves. The reason must name the recorded id, that no
            // replacement was created, and the hold age since acceptance
            // (accepted day 12, today day 45).
            var state = new DispatchHoldStrandReason.DispatchHoldTaskState(
                ShipmentId, AgreementId, TaskId, false, string.Empty, TaskRef,
                false, string.Empty, string.Empty, 45, 12);
            string reason = DispatchHoldStrandReason.Describe(state, 45);
            Assert.IsTrue(reason.Contains(TaskId), "Reason was: " + reason);
            Assert.IsTrue(reason.Contains("no longer in TaskAuthority"), "Reason was: " + reason);
            Assert.IsTrue(reason.Contains("no replacement was created"), "Reason was: " + reason);
            Assert.IsTrue(reason.Contains("day 12"), "Reason was: " + reason);
            Assert.IsTrue(reason.Contains("33 days ago"), "Reason was: " + reason);
        }

        [Test]
        public void RefMismatch_NamesDifferentTask()
        {
            // The recorded id resolves, but to a different task's
            // reference: no replacement is created either.
            var state = new DispatchHoldStrandReason.DispatchHoldTaskState(
                ShipmentId, AgreementId, TaskId, true, "freight-dispatch:other:other",
                TaskRef, false, "Queued", "None", 40, 12);
            string reason = DispatchHoldStrandReason.Describe(state, 45);
            Assert.IsTrue(reason.Contains("resolves to a different task"), "Reason was: " + reason);
            Assert.IsTrue(reason.Contains("freight-dispatch:other:other"), "Reason was: " + reason);
            Assert.IsTrue(reason.Contains("no replacement was created"), "Reason was: " + reason);
        }

        [Test]
        public void FutureTerminalDay_AgeClampedAtZero()
        {
            // A terminal day after today (clock disagreement) must never
            // produce a negative age in the log.
            var state = new DispatchHoldStrandReason.DispatchHoldTaskState(
                ShipmentId, AgreementId, TaskId, true, TaskRef, TaskRef,
                false, "Failed", "RecoverableFailure", 50, 12);
            string reason = DispatchHoldStrandReason.Describe(state, 45);
            Assert.IsTrue(reason.Contains("0 days ago"), "Reason was: " + reason);
            Assert.IsFalse(reason.Contains("-5"), "Reason was: " + reason);
        }

        [Test]
        public void OneDayAge_UsesSingularDay()
        {
            var state = new DispatchHoldStrandReason.DispatchHoldTaskState(
                ShipmentId, AgreementId, TaskId, true, TaskRef, TaskRef,
                false, "Failed", "RecoverableFailure", 44, 12);
            string reason = DispatchHoldStrandReason.Describe(state, 45);
            Assert.IsTrue(reason.Contains("1 day ago"), "Reason was: " + reason);
            Assert.IsFalse(reason.Contains("1 days ago"), "Reason was: " + reason);
        }

        [Test]
        public void FirstFailureOrdering_MissingRecordWinsOverMismatch()
        {
            // No record at all AND a mismatched reference cannot both be
            // reported: the missing-record check comes first, so the
            // reason must be the missing one, not the mismatch one.
            var state = new DispatchHoldStrandReason.DispatchHoldTaskState(
                ShipmentId, AgreementId, TaskId, false, "freight-dispatch:other:other",
                TaskRef, false, "Failed", "RecoverableFailure", 41, 12);
            string reason = DispatchHoldStrandReason.Describe(state, 45);
            Assert.IsTrue(reason.Contains("no longer in TaskAuthority"), "Reason was: " + reason);
            Assert.IsFalse(reason.Contains("resolves to a different task"), "Reason was: " + reason);
        }

        [Test]
        public void FirstFailureOrdering_MismatchWinsOverTerminal()
        {
            // A record that resolves to a different task is a mismatch
            // even when the (unrelated) recorded status is terminal: the
            // mismatch check comes before the open/terminal check.
            var state = new DispatchHoldStrandReason.DispatchHoldTaskState(
                ShipmentId, AgreementId, TaskId, true, "freight-dispatch:other:other",
                TaskRef, false, "Failed", "RecoverableFailure", 41, 12);
            string reason = DispatchHoldStrandReason.Describe(state, 45);
            Assert.IsTrue(reason.Contains("resolves to a different task"), "Reason was: " + reason);
            Assert.IsFalse(reason.Contains("terminalized Failed"), "Reason was: " + reason);
        }

        // ---------- (2) Wiring (structural) ----------

        [Test]
        public void RecordedTerminalBranch_LogsDayKeyedStrand()
        {
            string raw = ReadRepoFile(DriversPath);
            string code = StripNonCode(raw);
            string method = MethodBody(code, "private void CreatePreparedFreightDispatchTask(");
            Assert.IsTrue(method.Contains("DispatchHoldStrandReason.Describe("),
                "CreatePreparedFreightDispatchTask must name the strand reason through the pure helper");
            Assert.IsTrue(method.Contains("LogRefusalDayKeyed("),
                "CreatePreparedFreightDispatchTask must log day-keyed, not once-only");
            int describe = method.IndexOf("DispatchHoldStrandReason.Describe(", StringComparison.Ordinal);
            int strandedKey = raw.IndexOf("dispatch-hold-stranded:", StringComparison.Ordinal);
            int methodStart = raw.IndexOf("private void CreatePreparedFreightDispatchTask(", StringComparison.Ordinal);
            Assert.IsTrue(strandedKey > methodStart,
                "The terminal-task strand must log under its own day-keyed refusal key");
            Assert.IsTrue(describe >= 0,
                "The strand reason must be computed through the helper");
        }

        [Test]
        public void RecordedBranch_StaysSilentWhileTaskIsOpen()
        {
            // The null check is the live-path silence: an open recorded
            // task must not log a strand line.
            string code = StripNonCode(ReadRepoFile(DriversPath));
            string method = MethodBody(code, "private void CreatePreparedFreightDispatchTask(");
            int describe = method.IndexOf("DispatchHoldStrandReason.Describe(", StringComparison.Ordinal);
            int nullCheck = method.IndexOf("if (strand != null)", describe);
            int log = method.IndexOf("LogRefusalDayKeyed(", describe);
            Assert.IsTrue(describe >= 0 && nullCheck > describe,
                "The strand reason must be null-checked before logging");
            Assert.IsTrue(log > nullCheck,
                "The strand line must log only when Describe returns a reason (terminal task)");
        }

        [Test]
        public void MissingTaskBranch_LogsDayKeyedNotOnceOnly()
        {
            string raw = ReadRepoFile(DriversPath);
            string code = StripNonCode(raw);
            string method = MethodBody(code, "private void CreatePreparedFreightDispatchTask(");
            int methodStart = raw.IndexOf("private void CreatePreparedFreightDispatchTask(", StringComparison.Ordinal);
            int missingCall = raw.IndexOf("LogRefusalDayKeyed(\"dispatch-task-missing:\"", methodStart, StringComparison.Ordinal);
            Assert.IsTrue(missingCall > methodStart,
                "The missing-task branch must call the day-keyed logger with the dispatch-task-missing key");
            Assert.IsTrue(method.Contains("LogRefusalDayKeyed("),
                "CreatePreparedFreightDispatchTask must use the day-keyed logger");
            Assert.IsFalse(method.Contains("LogFreightAgreementBlockerOnce("),
                "CreatePreparedFreightDispatchTask must not use the once-only logger anywhere after this fix");
        }

        [Test]
        public void PrerequisiteBranches_AreDayKeyed()
        {
            // Every prerequisite refusal on the creation path must be
            // day-keyed now: a held shipment whose task was never created
            // stays visible instead of going silent after day 1. Each key
            // must sit inside a LogRefusalDayKeyed call in the raw file.
            string raw = ReadRepoFile(DriversPath);
            string code = StripNonCode(raw);
            string method = MethodBody(code, "private void CreatePreparedFreightDispatchTask(");
            foreach (string key in new[]
            {
                "dispatch-context:", "dispatch-operator:", "dispatch-state:",
                "dispatch-team:", "dispatch-link-existing:",
            })
            {
                int at = raw.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
                Assert.IsTrue(at > 0, "Expected refusal key missing from the file: " + key);
                string before = raw.Substring(Math.Max(0, at - 30), Math.Min(30, at));
                Assert.IsTrue(before.Contains("LogRefusalDayKeyed("),
                    "Refusal key " + key + " must be logged day-keyed, not once-only");
            }
            Assert.IsFalse(method.Contains("LogFreightAgreementBlockerOnce("),
                "CreatePreparedFreightDispatchTask must not use the once-only logger anywhere after this fix");
            string driveMethod = MethodBody(code, "private void DrivePreparedFreightDispatchAssignments(int dayIndex)");
            Assert.IsFalse(driveMethod.Contains("LogFreightAgreementBlockerOnce("),
                "DrivePreparedFreightDispatchAssignments must not use the once-only logger anywhere after this fix");
            int driveKey = raw.IndexOf("\"dispatch-prerequisite:\"", StringComparison.Ordinal);
            Assert.IsTrue(driveKey > 0,
                "The daily driver's missing-prerequisite line must keep its key");
            string driveBefore = raw.Substring(Math.Max(0, driveKey - 30), Math.Min(30, driveKey));
            Assert.IsTrue(driveBefore.Contains("LogRefusalDayKeyed("),
                "The daily driver's missing-prerequisite line must be logged day-keyed, not once-only");
        }

        [Test]
        public void NoTaskCreationInStrandBranches()
        {
            // The fix is diagnostic only: the recorded/missing branches
            // must not create a task. Exactly one creation site may exist
            // in the method — the original creation at the end, after the
            // recovery of an already-created-but-unlinked task.
            string code = StripNonCode(ReadRepoFile(DriversPath));
            string method = MethodBody(code, "private void CreatePreparedFreightDispatchTask(");
            int first = method.IndexOf("CreateTaskWithPlannedMinutes(", StringComparison.Ordinal);
            int second = first >= 0
                ? method.IndexOf("CreateTaskWithPlannedMinutes(", first + 1, StringComparison.Ordinal)
                : -1;
            Assert.IsTrue(first >= 0, "The single task-creation site must still exist");
            Assert.IsTrue(second < 0, "No second task-creation site may be added by this fix");
            int describe = method.IndexOf("DispatchHoldStrandReason.Describe(", StringComparison.Ordinal);
            Assert.IsTrue(describe >= 0 && describe < first,
                "The strand diagnostic must run before the creation site, in the recorded-task branch");
        }

        [Test]
        public void TerminalDayAnchorFollowsPrune()
        {
            // Cancelled tasks record no completion day; the driver must
            // anchor the terminal day exactly like the prune does
            // (CompletedDayIndex, else CreatedDayIndex), or a cancelled
            // task's strand age would read from day -1.
            string raw = ReadRepoFile(DriversPath);
            Assert.IsTrue(raw.Contains("recorded.CompletedDayIndex >= 0 ? recorded.CompletedDayIndex : recorded.CreatedDayIndex"),
                "The strand diagnostic must anchor the terminal day on CompletedDayIndex, falling back to CreatedDayIndex");
        }

        [Test]
        public void CompletionPathUntouchedByStrandDiagnostic()
        {
            // The fix touches only the creation path. The completion
            // handler and the short-tick reconciler must not gain strand
            // logging — a completed task that authorizes still releases
            // the hold through the existing handler, and a refused
            // completion still goes through its own once-only blocker
            // lines (the not-at-staging case retries and self-heals).
            string code = StripNonCode(ReadRepoFile(DriversPath));
            string handler = MethodBody(code, "private void HandleCompletedFreightDispatchTask(WorkTask task)");
            Assert.IsFalse(handler.Contains("DispatchHoldStrandReason"),
                "HandleCompletedFreightDispatchTask must not gain strand diagnostics");
            string reconciler = MethodBody(code, "private void ReconcileCompletedFreightDispatchTasks()");
            Assert.IsFalse(reconciler.Contains("DispatchHoldStrandReason"),
                "ReconcileCompletedFreightDispatchTasks must not gain strand diagnostics");
            Assert.IsTrue(handler.Contains("AuthorizeShipmentDispatch("),
                "HandleCompletedFreightDispatchTask must still authorize through the production driver");
        }

        // ---------- Repo helpers (C-5 / C-7 / C-8 / C-9 precedent) ----------

        private static string FindRepoRoot()
        {
            string fromEnv = Environment.GetEnvironmentVariable("LAND_LEDGERS_REPO");
            if (!string.IsNullOrWhiteSpace(fromEnv)
                && File.Exists(Path.Combine(fromEnv, SaveLoadManagerPath)))
            {
                return fromEnv;
            }

            var starts = new[]
            {
                Directory.GetCurrentDirectory(),
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
                        if (File.Exists(Path.Combine(candidate, SaveLoadManagerPath)))
                        {
                            return candidate;
                        }
                    }
                    DirectoryInfo parent = Directory.GetParent(dir);
                    dir = parent == null ? null : parent.FullName;
                }
            }

            Assert.IsTrue(false, "Campaign C (package 10): could not locate the Land & Ledgers repo. "
                + "Set LAND_LEDGERS_REPO, or run from the campaign tree. Searched:\n"
                + tried + "  LAND_LEDGERS_REPO=" + (fromEnv ?? "<unset>"));
            return null;
        }

        private static string ReadRepoFile(string relativePath)
        {
            string root = FindRepoRoot();
            string full = Path.Combine(root, relativePath);
            Assert.IsTrue(File.Exists(full), "Campaign C (package 10): expected repo file missing: " + full);
            return File.ReadAllText(full);
        }

        /// <summary>
        /// Returns source with comments, string literals and char literals
        /// blanked (newlines preserved), so brace matching and call counting
        /// cannot be fooled by a mention inside a comment or a log string.
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
                            continue;
                        }
                        if (source[i] == '"') break;
                        out_.Append(source[i] == '\n' ? '\n' : ' ');
                        i++;
                    }
                    if (i < source.Length)
                    {
                        out_.Append(' ');
                        i++;
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
                            continue;
                        }
                        if (source[i] == '"') break;
                        out_.Append(' ');
                        i++;
                    }
                    if (i < source.Length)
                    {
                        out_.Append(' ');
                        i++;
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
                            continue;
                        }
                        if (source[i] == '\'') break;
                        out_.Append(' ');
                        i++;
                    }
                    if (i < source.Length)
                    {
                        out_.Append(' ');
                        i++;
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

        /// <summary>
        /// Returns the brace-matched body of the first method whose
        /// signature line starts with <paramref name="signature"/>, from
        /// code already stripped by StripNonCode. Fails the test when the
        /// method is not found.
        /// </summary>
        internal static string MethodBody(string strippedCode, string signature)
        {
            int sig = strippedCode.IndexOf(signature, StringComparison.Ordinal);
            Assert.IsTrue(sig >= 0, "Campaign C (package 10): method not found: " + signature);
            int open = strippedCode.IndexOf('{', sig);
            Assert.IsTrue(open >= 0, "Campaign C (package 10): method has no body: " + signature);
            int depth = 0;
            for (int i = open; i < strippedCode.Length; i++)
            {
                if (strippedCode[i] == '{') depth++;
                else if (strippedCode[i] == '}')
                {
                    depth--;
                    if (depth == 0) return strippedCode.Substring(open, i - open + 1);
                }
            }
            Assert.IsTrue(false, "Campaign C (package 10): unbalanced braces in method: " + signature);
            return null;
        }
    }
}
