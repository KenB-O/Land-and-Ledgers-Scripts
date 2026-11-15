// Campaign C (DEF-050), package 10: pure, zero-Unity-dependency wording
// for a freight dispatch hold that can never release.
//
// What was verified (Nov 15, 2026, maintenance branch + this lane):
// a Millbrook shipment is created Planned with requiresDispatchAuthorization
// = true, and the hold releases ONLY through
// FreightProductionDriver.AuthorizeShipmentDispatch, which is called ONLY
// from SimulationDrivers.HandleCompletedFreightDispatchTask — the
// successful-completion path for a dispatch task. Every other way a
// dispatch task can end strands the shipment:
//
//  * Failed — CompleteTaskWithCausalOutcome resolves the 1-minute dispatch
//    handshake through the causal resolver, whose RecoverableFailure and
//    SeriousFailure outcomes are real (CausalTaskOutcomeResolver). A failed
//    task has Status Failed, so the completion handler returns early
//    (Status != Complete) and the short-tick reconciler
//    (ReconcileCompletedFreightDispatchTasks) only ever looks at Complete
//    tasks. No handler exists for a non-completed dispatch task anywhere.
//  * Cancelled — same as Failed: no handler, no re-creation.
//  * Completed but refused — the handler's identity checks (agreement
//    link, shipment Planned+unloaded, assignee == accepted operator,
//    task terms == shipment equipment, draft-team bind, physical driver
//    identity, authorization) refuse permanently: the identities and the
//    write-once flags they check never change after preparation. The
//    refusal is logged once (LogFreightAgreementBlockerOnce) and the
//    reconciler re-runs the handler every short tick, re-refusing
//    forever, silently after day 1. (The one transient sub-case — the
//    operator not visually at staging — self-heals: the reconciler
//    authorizes the moment he arrives. It is not a strand.)
//  * Pruned — 7 days after terminalization the DEF-049 prune removes the
//    Failed / Cancelled / Complete task (the external-reference predicate
//    protects shopping-trip tasks only), so the recorded task id stops
//    resolving and CreatePreparedFreightDispatchTask logs once that "no
//    replacement was created" — and returns. The prune does not cause the
//    strand (the hold is already unreleasable the moment the task
//    terminalizes without authorization); it only changes which dead
//    branch the daily driver hits.
//
// Why no re-creation path exists: agreement.DispatchTaskId is write-once
// (FreightServiceAgreements.RegisterDispatchTask refuses a different id,
// and nothing ever clears it), and CreatePreparedFreightDispatchTask is
// called from exactly three sites — all on the hitching/preparation path
// (daily prepared-assignment drive, preparation, staging-travel
// completion) — none on a dispatch-failure path. A recorded task that
// still resolves with the matching stable reference returns silently
// with no new task, and a recorded id that stops resolving returns with
// "no replacement was created".
//
// What does NOT bound the hold: FreightProductionDriver.AdvanceTick skips
// held shipments entirely, so a held shipment never advances and never
// expires; there is no agreement expiry and no journey expiry (the
// journey is created at departure, which never happens while held).
//
// What IS fixed here is diagnostic, in three layers, all behaviour-neutral:
//  (1) A terminal recorded dispatch task now logs once per day (day-keyed,
//      LogRefusalDayKeyed), naming the task, its terminal status and
//      outcome, the day it terminalized, and the hold age — instead of
//      the daily driver returning silently.
//  (2) A recorded dispatch task id that no longer resolves now logs once
//      per day instead of once per session, naming the recorded id and
//      the hold age since acceptance.
//  (3) The prerequisite refusals on the same creation path (unresolved
//      context, operator mismatch, state mismatch, team attachment,
//      unlinked existing task, and the daily driver's missing-prerequisite
//      line) now log once per day instead of once per session, so a held
//      shipment whose task was never created stays visible too. They
//      retry daily and can self-heal; only their logging cadence changes.
//
// This class only names the reason; it changes no gate, schedule, price,
// or movement semantics, creates no task, and never decides the
// disposition. Whether a hold should expire — and if a dispatch task
// fails or is cancelled, whether a new dispatch task should be created,
// the shipment should return to Planned for re-preparation, or the
// agreement should be cancelled — is A-to-Z Check 10 question 4, an
// owner decision, and is NOT invented here.
using System;

namespace LandLedgers.Economy.Transport
{
    public static class DispatchHoldStrandReason
    {
        /// <summary>Snapshot of one agreement's recorded dispatch task, as
        /// read by the driver method. Status and outcome are carried as
        /// their enum names ("Failed" / "Cancelled" / "Complete",
        /// "RecoverableFailure" / "SeriousFailure" / "Success", ...) so
        /// this file stays free of the TaskAuthority types and their Unity
        /// dependencies. TerminalDayIndex follows the prune's anchor:
        /// CompletedDayIndex when recorded, else CreatedDayIndex.</summary>
        public readonly struct DispatchHoldTaskState
        {
            public DispatchHoldTaskState(string shipmentId, string agreementId,
                string taskId, bool taskRecordExists, string recordedCustomerRef,
                string expectedCustomerRef, bool taskIsOpen, string taskStatusName,
                string taskOutcomeName, int terminalDayIndex, int acceptedDayIndex)
            {
                ShipmentId = shipmentId;
                AgreementId = agreementId;
                TaskId = taskId;
                TaskRecordExists = taskRecordExists;
                RecordedCustomerRef = recordedCustomerRef;
                ExpectedCustomerRef = expectedCustomerRef;
                TaskIsOpen = taskIsOpen;
                TaskStatusName = taskStatusName;
                TaskOutcomeName = taskOutcomeName;
                TerminalDayIndex = terminalDayIndex;
                AcceptedDayIndex = acceptedDayIndex;
            }

            public string ShipmentId { get; }
            public string AgreementId { get; }
            public string TaskId { get; }
            public bool TaskRecordExists { get; }
            public string RecordedCustomerRef { get; }
            public string ExpectedCustomerRef { get; }
            public bool TaskIsOpen { get; }
            public string TaskStatusName { get; }
            public string TaskOutcomeName { get; }
            public int TerminalDayIndex { get; }
            public int AcceptedDayIndex { get; }
        }

        /// <summary>Returns null when the hold is NOT stranded on its
        /// recorded dispatch task: no task id is recorded yet (the task was
        /// never created — the prerequisite branches describe that stall),
        /// or the recorded task still resolves to this shipment's task and
        /// is still open (the live path). Otherwise a one-line reason
        /// naming the strand: a terminal recorded task, a recorded id that
        /// resolves to a different task, or a recorded id whose TaskAuthority
        /// record is gone. Never returns an empty string.</summary>
        public static string Describe(DispatchHoldTaskState state, int dayIndex)
        {
            if (string.IsNullOrWhiteSpace(state.TaskId))
                return null;
            if (!state.TaskRecordExists)
            {
                int heldDays = Math.Max(0, dayIndex - state.AcceptedDayIndex);
                return "[FreightDispatch] Shipment '" + state.ShipmentId
                    + "' stays Planned with its dispatch hold: recorded dispatch task '"
                    + state.TaskId + "' is no longer in TaskAuthority (pruned or lost after terminalizing); "
                    + "no replacement was created. Held since acceptance (day "
                    + state.AcceptedDayIndex + ", " + heldDays + " " + DayWord(heldDays) + " ago).";
            }
            if (!string.Equals(state.RecordedCustomerRef, state.ExpectedCustomerRef, StringComparison.Ordinal))
            {
                return "[FreightDispatch] Shipment '" + state.ShipmentId
                    + "' stays Planned with its dispatch hold: recorded dispatch task '"
                    + state.TaskId + "' resolves to a different task ('"
                    + state.RecordedCustomerRef + "'); no replacement was created.";
            }
            if (state.TaskIsOpen)
                return null;
            int strandedDays = Math.Max(0, dayIndex - state.TerminalDayIndex);
            return "[FreightDispatch] Shipment '" + state.ShipmentId
                + "' stays Planned with its dispatch hold: dispatch task '"
                + state.TaskId + "' terminalized " + state.TaskStatusName
                + " (" + state.TaskOutcomeName + ") on day " + state.TerminalDayIndex
                + " (" + strandedDays + " " + DayWord(strandedDays) + " ago); "
                + "no dispatch task is re-created for an already-prepared shipment, so the hold "
                + "can only release on a successful dispatch-task completion. Whether a hold "
                + "should expire is an owner decision (Check 10 Q4).";
        }

        private static string DayWord(int days)
        {
            return days == 1 ? "day" : "days";
        }
    }
}
