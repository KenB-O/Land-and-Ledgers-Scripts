// Lane 4 (Muse Pass, Nov 20 2026) — Task and Obligation Lifecycle
// Inspector core (Tool 03).
//
// Pure-System lifecycle analysis: NO UnityEngine, NO Editor, NO gameplay
// authority references, so this file joins the Roslyn harness closure and
// is covered by TaskLifecycleInspectorTests. The Editor window
// (TaskLifecycleInspectorWindow) only MAPS live TaskAuthority / freight /
// furniture state onto the plain snapshot types below; this class never
// reads live game state and never becomes a parallel source of truth — it
// observes evidence handed to it and names the authority that owns the
// answer. It assigns no work, changes no routes, and moves no goods,
// money, food, transport or labour.
//
// The Inspector is the LIFECYCLE view; the Investigator (Lane 2) is the
// CAUSAL view. The Inspector answers "what state is this obligation in,
// how long has it been there, what deadline (if any) bounds it, and what
// terminal disposition (if any) closed it"; the Investigator answers
// "why is it stuck". Both build on Lane 1 (IncidentWarningAuthority +
// RefusalCauseClass) and both apply the same circumstance test:
// per-state stall thresholds (InProgress 6h, Assigned/ResourcesReserved
// 12h, WaitingOnDependency/WaitingOnPassiveProcess 48h, other incl.
// Queued 72h — IncidentInvestigator.StallThresholdSeconds) are the FLOOR
// for "stalled"; severity comes from circumstances, never from elapsed
// time alone (Agent G item 7).
//
// SURFACE, DO NOT FIX (Lane 4 rule):
// - The Queued gap: StaleInProgressSweep covers InProgress tasks only
//   (C R-3 CONFIRMED); Queued tasks have no sweep and no liveness bound.
//   The Inspector surfaces every past-threshold Queued task as
//   StalledNoAssignee and names Lane 9d as the repair owner. It does not
//   add a sweep.
// - The F6-2 post-load strand: the pre-load terminal path IS logged
//   (SimulationDrivers.cs:12006 — FailAbandonedShipment + CancelAgreement);
//   only the post-load/committed else-branch is silent ("remains held",
//   :12019). The Inspector surfaces that strand as TerminalButUnrecovered;
//   Lane 9d owns the fix (ReopenTerminalRequest / un-commit path).
// - The interrupted domestic.fuel-handling stall (C R-18 SOLVED): all
//   interrupted fuel-handling tasks, never re-queued, caused by the
//   missing Firewood activity point. The Inspector makes it inspectable
//   with its authority (DomesticLaborDriver) and its earliest confirmed
//   blocker (the firewood reason verbatim). The Firewood authoring repair
//   is Lane 9c (Codex/scene-side; Muse fallback A4) — not this lane.
// - The furniture install mismatch: the current production driver mints
//   and resolves furnishing.install tasks. The Inspector still surfaces a
//   real task/order mismatch when captured evidence says the task completed
//   but the saved order did not advance. Its canned case is synthetic and
//   must not be presented as evidence that the resolver is absent.
//
// M-05: never use skill as the explanation for a failed task when the
// real problem is a missing physical destination — no refusal path reads
// capability, so a skill-only stated cause yields UNKNOWN with the
// missing physical-destination evidence named. Refusals are classified
// with Lane 1's M-R14 six-class taxonomy (RefusalCauseClass); the
// Investigator's DestinationBlockerKind is mapped, never re-invented.
using System;
using System.Collections.Generic;
using LandLedgers.Tasks;

namespace LandLedgers.Diagnostics
{
    /// <summary>Obligation types the Inspector can show a lifecycle for.</summary>
    public enum ObligationKind
    {
        Unknown = 0,
        Task = 1,
        ShoppingTrip = 2,
        FreightRequest = 3,
        FreightAgreement = 4,
        FreightShipment = 5,
        FurnitureOrder = 6,
        FurnitureInstall = 7,
        ConstructionCommission = 8,
        ProductionBatch = 9,
        ReplenishmentOrder = 10,
    }

    /// <summary>
    /// Where an obligation sits in its lifecycle. "Terminal but
    /// unrecovered" (F-C3) is its own disposition, distinct from
    /// "completed" and "failed": a failure path landed the obligation in a
    /// state no code path can leave.
    /// </summary>
    public enum LifecycleDisposition
    {
        Unknown = 0,
        Healthy = 1,
        WithinThreshold = 2,
        StalledNoAssignee = 3,
        StalledAssigneeIdle = 4,
        StalledLegitimateWait = 5,
        InterruptedNeverRequeued = 6,
        TerminalButUnrecovered = 7,
        UnboundedWait = 8,
        PastDeadline = 9,
        TerminalRecovered = 10,
        TerminalCompleted = 11,
    }

    /// <summary>
    /// One obligation's lifecycle state, in plain form. The Editor window
    /// maps live TaskAuthority / freight / furniture state onto this; the
    /// Inspector never depends on the live types.
    /// </summary>
    public sealed class ObligationLifecycleSnapshot
    {
        public string ObligationId = string.Empty;
        public ObligationKind Kind = ObligationKind.Unknown;
        public string DefinitionId = string.Empty;
        public string Label = string.Empty;
        public string Status = string.Empty;
        public double StateEnteredSimSeconds = -1;
        public double LastProgressSimSeconds = -1;
        public double FirstObservedSimSeconds = -1;
        public double NowSimSeconds = -1;
        public int DayIndex = -1;
        public string OwnerId = string.Empty;
        public string PersonId = string.Empty;
        public int PersonNumericId = -1;
        public int HouseholdId = -1;
        public string BusinessId = string.Empty;
        public int WorkedMinutes;
        public int RemainingMinutes = -1;
        public int PlannedMinutes = -1;
        public int InterruptionCount;
        public string LastTransition = string.Empty;
        public string RefusalReason = string.Empty;
        public string RecoveryState = string.Empty;
        public string ReservationId = string.Empty;
        public bool HasAssignee;
        public bool HasReservation;
        public double DeadlineSimSeconds = -1;
        public string DeadlineKind = string.Empty;
        /// <summary>True when a sweep, deadline, or driver bounds this state. False is itself a finding (UnboundedWait).</summary>
        public bool HasLivenessBound;
        /// <summary>Named terminal disposition when the obligation is terminal (e.g. "Complete", "Refused — reopenable"). Empty otherwise.</summary>
        public string TerminalDisposition = string.Empty;
        public bool AgreementCancelled;
        public bool LotCommitted;
        public bool HasReopenPath;
        public int RecoveryAttempts;
        public int RecoveryAttemptLimit;
        public bool IsPreLoad;
        public bool IsPostLoadCommitted;
        public bool OrderPaid;
        public bool InstallTaskCompleted;
        public bool InstallOutcomeResolved;
        public bool PlacementRefused;
        public string RelatedTaskId = string.Empty;
        public string RelatedAgreementId = string.Empty;
        public string RelatedShipmentId = string.Empty;
        public string RelatedLotId = string.Empty;
        public bool SkillMentionedAsOnlyCause;
        public bool WorkstationOccupied;
        public string OccupantDescription = string.Empty;
        public bool WorkerBusyOnOtherTask;
        public bool IntegrityViolation;
        public string IntegrityDetail = string.Empty;
    }

    /// <summary>Outcome of the lifecycle inspection of one obligation.</summary>
    public sealed class LifecycleAssessment
    {
        public string ObligationId = string.Empty;
        public ObligationKind Kind = ObligationKind.Unknown;
        public string DefinitionId = string.Empty;
        public string Status = string.Empty;
        public LifecycleDisposition Disposition = LifecycleDisposition.Unknown;
        public IncidentSeverity Severity = IncidentSeverity.Unknown;
        public RefusalCauseClass BlockerClass = RefusalCauseClass.Unclassified;
        public double StallThresholdSeconds = -1;
        public double StalledSeconds = -1;
        public bool HasAssignee;
        public string Explanation = string.Empty;
        public string EarliestBlocker = string.Empty;
        public double EarliestBlockerSeconds = -1;
        public string ResponsibleAuthority = string.Empty;
        public string AuthoritySourcePath = string.Empty;
        public int AuthoritySourceLine = -1;
        /// <summary>Who owns the repair (e.g. "Lane 9d"). Empty when no repair is owed or none is known.</summary>
        public string RepairOwner = string.Empty;
        public string TerminalDisposition = string.Empty;
        public List<string> MissingEvidence = new List<string>();
        public string SourceNote = string.Empty;

        /// <summary>True when the disposition warrants an emission to the Lane 1 core.</summary>
        public bool IsIncident
        {
            get
            {
                switch (Disposition)
                {
                    case LifecycleDisposition.StalledNoAssignee:
                    case LifecycleDisposition.StalledAssigneeIdle:
                    case LifecycleDisposition.InterruptedNeverRequeued:
                    case LifecycleDisposition.TerminalButUnrecovered:
                    case LifecycleDisposition.UnboundedWait:
                    case LifecycleDisposition.PastDeadline:
                        return true;
                    default:
                        return false;
                }
            }
        }
    }

    /// <summary>Coverage summary over a set of assessments.</summary>
    public sealed class LifecycleCoverage
    {
        public int TotalAssessed;
        public int IncidentCount;
        public Dictionary<LifecycleDisposition, int> ByDisposition = new Dictionary<LifecycleDisposition, int>();
        public Dictionary<IncidentSeverity, int> BySeverity = new Dictionary<IncidentSeverity, int>();
        public Dictionary<ObligationKind, int> ByKind = new Dictionary<ObligationKind, int>();
        /// <summary>Obligation kinds with zero observations in this set — not observed is not a pass.</summary>
        public List<ObligationKind> KindsNotObserved = new List<ObligationKind>();
    }

    /// <summary>
    /// Task and Obligation Lifecycle Inspector core (Lane 4, Tool 03).
    /// Stateless pure functions over plain snapshots. Diagnostics OBSERVE
    /// authoritative state; this class assigns no work, changes no routes,
    /// moves no goods, money, food, transport or labour, and creates no
    /// competing task system — TaskAuthority stays the only task authority.
    /// </summary>
    public static class TaskLifecycleInspector
    {
        /// <summary>
        /// Terminal TaskStatus values: they refuse every transition in
        /// TaskLifecyclePolicy (C R-2 CONFIRMED). Failed is NOT in this set:
        /// Failed -&gt; ReworkRequired is its one legal onward transition, so
        /// a Failed task is terminal-pending-rework, not terminal.
        /// </summary>
        public static bool IsTerminalStatus(string status)
        {
            return string.Equals(status, "Complete", StringComparison.Ordinal)
                || string.Equals(status, "Cancelled", StringComparison.Ordinal)
                || string.Equals(status, "ReworkRequired", StringComparison.Ordinal);
        }

        /// <summary>
        /// Named terminal disposition for a task status, or null when the
        /// status is not terminal. Every terminal disposition is named —
        /// the cancellation model carries no day stamp, which the
        /// Cancelled text says plainly (PLAYTEST_DIAGNOSTIC_SCHEMA / G F-G2).
        /// </summary>
        public static string TerminalDispositionName(string status)
        {
            if (string.Equals(status, "Complete", StringComparison.Ordinal))
                return "Complete — terminal, refuses all transitions (TaskLifecyclePolicy).";
            if (string.Equals(status, "Cancelled", StringComparison.Ordinal))
                return "Cancelled — terminal, refuses all transitions; the task model carries no cancellation day stamp, only a retained-history count.";
            if (string.Equals(status, "ReworkRequired", StringComparison.Ordinal))
                return "ReworkRequired — terminal for this instance; the corrective task is linked via FollowUpTaskIds.";
            if (string.Equals(status, "Failed", StringComparison.Ordinal))
                return "Failed — terminal-pending-rework; the only legal onward transition is Failed -> ReworkRequired.";
            return null;
        }

        /// <summary>
        /// Is this (from -&gt; to) transition legal under the explicit
        /// TaskLifecyclePolicy table? Unparseable status strings are illegal
        /// by construction — the Inspector never guesses a transition.
        /// </summary>
        public static bool IsLegalTransition(string fromStatus, string toStatus)
        {
            TaskStatus from;
            TaskStatus to;
            if (!TryParseStatus(fromStatus, out from) || !TryParseStatus(toStatus, out to)) return false;
            return TaskLifecyclePolicy.CanTransition(from, to);
        }

        /// <summary>One-line legality note for a transition (window display).</summary>
        public static string TransitionLegalityNote(string fromStatus, string toStatus)
        {
            if (IsLegalTransition(fromStatus, toStatus))
                return fromStatus + " -> " + toStatus + " is a legal lifecycle transition (TaskLifecyclePolicy).";
            return fromStatus + " -> " + toStatus + " is NOT a legal lifecycle transition — TaskAuthority refuses it loudly, never silently applies it.";
        }

        /// <summary>Dispatches on the snapshot kind. Null snapshot yields UNKNOWN, never a throw.</summary>
        public static LifecycleAssessment Inspect(ObligationLifecycleSnapshot snapshot)
        {
            if (snapshot == null)
            {
                var unknown = new LifecycleAssessment();
                unknown.Severity = IncidentSeverity.Unknown;
                unknown.Explanation = "No obligation snapshot was supplied.";
                unknown.MissingEvidence.Add("obligation snapshot (id, kind, status, observation times)");
                return unknown;
            }

            switch (snapshot.Kind)
            {
                case ObligationKind.Task:
                    return InspectTask(snapshot);
                case ObligationKind.FreightRequest:
                case ObligationKind.FreightAgreement:
                    return InspectFreightRequest(snapshot);
                case ObligationKind.FreightShipment:
                    return InspectFreightShipment(snapshot);
                case ObligationKind.FurnitureOrder:
                case ObligationKind.FurnitureInstall:
                    return InspectFurnitureOrder(snapshot);
                default:
                    return InspectGenericObligation(snapshot);
            }
        }

        /// <summary>
        /// Inspects one TaskAuthority task. Threshold AND measured duration
        /// are both recorded (C-10), and "no assignee" is distinguished
        /// from "assignee idle" as its own disposition — the two have
        /// different owners (the Queued gap vs a stuck worker).
        /// </summary>
        public static LifecycleAssessment InspectTask(ObligationLifecycleSnapshot snapshot)
        {
            var result = NewAssessment(snapshot);

            string terminal = TerminalDispositionName(snapshot.Status);
            if (terminal != null)
            {
                result.Disposition = LifecycleDisposition.TerminalCompleted;
                result.Severity = IncidentSeverity.Information;
                result.TerminalDisposition = terminal;
                result.Explanation = "Task " + snapshot.ObligationId + " (" + snapshot.DefinitionId + ") is "
                    + snapshot.Status + ": " + terminal;
                result.ResponsibleAuthority = ResolveTaskAuthority(snapshot.DefinitionId, snapshot.RecoveryState);
                return result;
            }

            result.StallThresholdSeconds = IncidentInvestigator.StallThresholdSeconds(snapshot.Status);
            result.StalledSeconds = snapshot.NowSimSeconds >= 0 && snapshot.LastProgressSimSeconds >= 0
                ? Math.Max(0d, snapshot.NowSimSeconds - snapshot.LastProgressSimSeconds) : -1d;

            // The interrupted-never-requeued strand (C R-18): an interrupt
            // with no re-queue, still sitting Queued/Interrupted past the
            // threshold with zero work. The last transition IS the blocker.
            bool interruptedStrand = string.Equals(snapshot.LastTransition, "InterruptTask", StringComparison.OrdinalIgnoreCase)
                && snapshot.InterruptionCount >= 1
                && (string.Equals(snapshot.Status, "Queued", StringComparison.Ordinal)
                    || string.Equals(snapshot.Status, "Interrupted", StringComparison.Ordinal))
                && result.StalledSeconds >= 0 && result.StalledSeconds >= result.StallThresholdSeconds;
            if (interruptedStrand)
            {
                result.Disposition = LifecycleDisposition.InterruptedNeverRequeued;
                FillTaskAuthorityAndBlocker(result, snapshot);
                result.Severity = IncidentSeverity.Warning;
                result.EarliestBlocker = "InterruptTask at " + snapshot.LastProgressSimSeconds.ToString("0.#")
                    + "s: " + FirstNonEmpty(snapshot.RefusalReason, snapshot.RecoveryState);
                result.EarliestBlockerSeconds = snapshot.LastProgressSimSeconds;
                result.Explanation = "Task " + snapshot.ObligationId + " (" + snapshot.DefinitionId + ") was interrupted "
                    + snapshot.InterruptionCount + " time(s) and never re-queued: " + (result.StalledSeconds / 3600d).ToString("0.0")
                    + " simulated hours with no progress (threshold " + (result.StallThresholdSeconds / 3600d).ToString("0.##")
                    + "h for " + snapshot.Status + "), worked " + snapshot.WorkedMinutes + " minutes, remaining "
                    + snapshot.RemainingMinutes + ". An interrupted task that is never re-queued is a strand, not a queue.";
                if (IsFuelHandling(snapshot))
                {
                    result.RepairOwner = "DomesticLaborDriver recovery plus authored residential activity access";
                    result.SourceNote = "Historical CI-5F80FDC40F evidence: retained-run T9/T10/T170 (60-day) and T8/T9/T11 "
                        + "(5-day) fuel-handling tasks were interrupted while the shared home prefab lacked a Firewood point. "
                        + "The integration prefab now authors 'firewood-handling'; verify current instance routing before attributing a new stall.";
                }
                else
                {
                    result.RepairOwner = "Lane 9d (Queued gap — no sweep covers Queued tasks; StaleInProgressSweep is InProgress-only)";
                }
                return result;
            }

            StallClassification stall = ClassifyTaskStall(snapshot);
            result.Severity = stall.Severity;
            result.Explanation = stall.Explanation;
            result.MissingEvidence = stall.MissingEvidence;
            result.BlockerClass = MapBlockerKind(stall.BlockerKind);
            result.ResponsibleAuthority = ResolveTaskAuthority(snapshot.DefinitionId, snapshot.RecoveryState);
            if (!string.IsNullOrWhiteSpace(snapshot.RefusalReason))
            {
                result.EarliestBlocker = snapshot.RefusalReason;
                result.EarliestBlockerSeconds = snapshot.LastProgressSimSeconds;
            }

            bool pastThreshold = result.StalledSeconds >= 0 && result.StalledSeconds >= result.StallThresholdSeconds;
            if (!pastThreshold)
            {
                result.Disposition = LifecycleDisposition.WithinThreshold;
                if (result.Severity == IncidentSeverity.Warning || result.Severity == IncidentSeverity.Critical)
                {
                    // The circumstance test never upgrades a below-threshold
                    // task to WARNING on elapsed time alone; an integrity
                    // violation is the one exception — ClassifyStall already
                    // returned Critical for it, so keep it and say so.
                    if (result.Severity != IncidentSeverity.Critical)
                    {
                        result.Severity = IncidentSeverity.Information;
                        result.Explanation = "Within the per-state stall threshold (" + (result.StallThresholdSeconds / 3600d).ToString("0.##")
                            + "h for " + snapshot.Status + "); no stall incident. " + result.Explanation;
                    }
                }
                return result;
            }

            if (!snapshot.HasAssignee && string.Equals(snapshot.Status, "Queued", StringComparison.Ordinal))
            {
                result.Disposition = LifecycleDisposition.StalledNoAssignee;
                result.Severity = IncidentSeverity.Warning;
                result.RepairOwner = "Lane 9d (Queued gap — StaleInProgressSweep covers InProgress only; no sweep covers Queued tasks)";
                result.Explanation = "Queued task " + snapshot.ObligationId + " (" + snapshot.DefinitionId + ") has NO assignee and "
                    + "no progress for " + (result.StalledSeconds / 3600d).ToString("0.0") + " simulated hours (threshold "
                    + (result.StallThresholdSeconds / 3600d).ToString("0.##") + "h for Queued). This is the Queued gap, not a stuck "
                    + "worker: no sweep covers Queued tasks, so the task sits with no owner and no outcome. "
                    + "Distinguished from 'assignee idle' — there is no assignee to be idle.";
                return result;
            }

            if (stall.Severity == IncidentSeverity.Information)
            {
                result.Disposition = LifecycleDisposition.StalledLegitimateWait;
                return result;
            }

            if (stall.Severity == IncidentSeverity.Warning)
            {
                result.Disposition = LifecycleDisposition.StalledAssigneeIdle;
                result.Explanation = "Task " + snapshot.ObligationId + " (" + snapshot.DefinitionId + ") has an assignee"
                    + (snapshot.HasAssignee ? "" : " (none recorded)")
                    + " and no progress for " + (result.StalledSeconds / 3600d).ToString("0.0") + " simulated hours (threshold "
                    + (result.StallThresholdSeconds / 3600d).ToString("0.##") + "h for " + snapshot.Status + "), worked "
                    + snapshot.WorkedMinutes + " minutes, remaining " + snapshot.RemainingMinutes + ". " + stall.Explanation
                    + (snapshot.HasReservation || !string.IsNullOrWhiteSpace(snapshot.ReservationId)
                        ? " Reservation held: " + snapshot.ReservationId + "."
                        : " No reservation is held.");
                return result;
            }

            result.Disposition = LifecycleDisposition.Unknown;
            return result;
        }

        /// <summary>
        /// Inspects one freight request / agreement. The F6-2 strand is
        /// the proving case: a terminal-Accepted request whose agreement
        /// was cancelled by the recovery's terminal disposition, with no
        /// reopen path — SetStatus refuses terminal changes and
        /// ReopenRefusedRequest is Refused-only, so the lot can never get
        /// a new meeting. F-02 (committed lot + cancelled agreement + no
        /// un-commit method) is the same defect class.
        /// </summary>
        public static LifecycleAssessment InspectFreightRequest(ObligationLifecycleSnapshot snapshot)
        {
            var result = NewAssessment(snapshot);
            string status = snapshot.Status ?? string.Empty;

            if (string.Equals(status, "Accepted", StringComparison.Ordinal) && snapshot.AgreementCancelled)
            {
                result.Disposition = LifecycleDisposition.TerminalButUnrecovered;
                result.Severity = IncidentSeverity.Critical;
                result.ResponsibleAuthority = "FreightServiceAgreements (freight request/agreement authority)";
                result.AuthoritySourcePath = "Assets/Scripts/Domains/Economy/Businesses/Freight/FreightServiceAgreements.cs";
                result.AuthoritySourceLine = 336;
                result.RepairOwner = "Lane 9d (F6-2 — extend ReopenRefusedRequest or add ReopenTerminalRequest; add an un-commit/refund path for F-02 orphaned lots)";
                result.EarliestBlocker = "Recovery terminal disposition: FailAbandonedShipment + CancelAgreement "
                    + "(SimulationDrivers.cs:12006) cancelled the agreement and left the request Accepted.";
                result.EarliestBlockerSeconds = snapshot.LastProgressSimSeconds;
                result.Explanation = "Freight request " + snapshot.ObligationId + " is terminal-Accepted and its agreement was cancelled, "
                    + "but no code path can move it: SetStatus refuses terminal changes (FreightServiceAgreements.cs:336), "
                    + "ReopenRefusedRequest is Refused-only (:379), and the CreateRequest duplicate guard refuses a new request "
                    + "for the same lot" + (!string.IsNullOrWhiteSpace(snapshot.RelatedLotId) ? " (" + snapshot.RelatedLotId + ")" : "")
                    + ". The lot can never get a new meeting — a terminal state with no recovery path (F-C3)."
                    + (snapshot.LotCommitted
                        ? " The cargo lot is committed with no un-commit/refund method (F-02): the lot is orphaned alongside the request."
                        : string.Empty);
                result.SourceNote = "F6-2 NARROWED (C R-7 re-verification): the pre-load terminal path IS logged; "
                    + "the strand is the terminal-Accepted request the recovery leaves behind.";
                return result;
            }

            if (snapshot.LotCommitted && snapshot.AgreementCancelled
                && !string.Equals(status, "Accepted", StringComparison.Ordinal))
            {
                result.Disposition = LifecycleDisposition.TerminalButUnrecovered;
                result.Severity = IncidentSeverity.Critical;
                result.ResponsibleAuthority = "FreightServiceAgreements (freight request/agreement authority)";
                result.AuthoritySourcePath = "Assets/Scripts/Domains/Economy/Businesses/Freight/FreightServiceAgreements.cs";
                result.AuthoritySourceLine = 501;
                result.RepairOwner = "Lane 9d (F-02 — add an un-commit/refund path for orphaned lots)";
                result.EarliestBlocker = "CancelAgreement (:501) sets Status=Cancelled only; it does not touch the lot or the request.";
                result.Explanation = "Freight request " + snapshot.ObligationId + " has a committed lot"
                    + (!string.IsNullOrWhiteSpace(snapshot.RelatedLotId) ? " (" + snapshot.RelatedLotId + ")" : "")
                    + " and a cancelled agreement, with no un-commit/refund method: the lot is orphaned (F-02)."
                    + " No code path returns the cargo to its owner or recommits it.";
                return result;
            }

            if (string.Equals(status, "Accepted", StringComparison.Ordinal))
            {
                result.Disposition = LifecycleDisposition.Healthy;
                result.Severity = IncidentSeverity.Information;
                result.ResponsibleAuthority = "FreightServiceAgreements (freight request/agreement authority)";
                result.Explanation = "Freight request " + snapshot.ObligationId + " is Accepted with a live agreement"
                    + (!string.IsNullOrWhiteSpace(snapshot.RelatedAgreementId) ? " (" + snapshot.RelatedAgreementId + ")" : "")
                    + " — in flight, no strand evidenced.";
                return result;
            }

            if (string.Equals(status, "Refused", StringComparison.Ordinal))
            {
                result.Disposition = LifecycleDisposition.TerminalCompleted;
                result.Severity = IncidentSeverity.Information;
                result.TerminalDisposition = "Refused — terminal, reopenable via ReopenRefusedRequest (FreightServiceAgreements.cs:379) when the lot is still shippable.";
                result.ResponsibleAuthority = "FreightServiceAgreements (freight request/agreement authority)";
                result.Explanation = "Freight request " + snapshot.ObligationId + " is Refused: " + result.TerminalDisposition;
                return result;
            }

            if (string.Equals(status, "Open", StringComparison.Ordinal)
                || string.Equals(status, "MeetingScheduled", StringComparison.Ordinal)
                || string.Equals(status, "Quoted", StringComparison.Ordinal)
                || string.Equals(status, "AtMeeting", StringComparison.Ordinal))
            {
                result.Disposition = LifecycleDisposition.Healthy;
                result.Severity = IncidentSeverity.Information;
                result.ResponsibleAuthority = "FreightServiceAgreements (freight request/agreement authority)";
                result.Explanation = "Freight request " + snapshot.ObligationId + " is " + status + " — inside its normal lifecycle"
                    + " (request → meeting → quote → acceptance). No strand evidenced.";
                result.MissingEvidence.Add("ETA/overdue field — LogisticsShipmentState carries CreatedDayIndex only (E-18); a request has no recorded deadline to be past.");
                return result;
            }

            result.Disposition = LifecycleDisposition.Unknown;
            result.Severity = IncidentSeverity.Unknown;
            result.Explanation = "Freight request " + snapshot.ObligationId + " has status '" + status + "', which the Inspector does not recognise.";
            result.MissingEvidence.Add("a recognised freight request status (Open / MeetingScheduled / Quoted / Refused / Accepted / AtMeeting)");
            return result;
        }

        /// <summary>
        /// Inspects one freight shipment. The proving cases are the
        /// unbounded staging wait (zero deadline matches in
        /// FreightDispatchStaging.cs — C R-8 CONFIRMED) and the silent
        /// post-load strand (the else-branch at SimulationDrivers.cs:12019
        /// only day-keys "remains held" — no disposition, no warning).
        /// </summary>
        public static LifecycleAssessment InspectFreightShipment(ObligationLifecycleSnapshot snapshot)
        {
            var result = NewAssessment(snapshot);
            string status = snapshot.Status ?? string.Empty;

            if (string.Equals(status, "Completed", StringComparison.Ordinal))
            {
                result.Disposition = LifecycleDisposition.TerminalCompleted;
                result.Severity = IncidentSeverity.Information;
                result.TerminalDisposition = "Completed — terminal disposition of a delivered shipment.";
                result.ResponsibleAuthority = "FreightProductionDriver / FreightServiceAgreements (freight delivery authority)";
                result.Explanation = "Shipment " + snapshot.ObligationId + " completed.";
                return result;
            }

            if (string.Equals(status, "Failed", StringComparison.Ordinal) && snapshot.IsPreLoad && snapshot.AgreementCancelled)
            {
                result.Disposition = LifecycleDisposition.TerminalRecovered;
                result.Severity = IncidentSeverity.Information;
                result.TerminalDisposition = "Failed before load — agreement cancelled through its existing authority, source cargo remains with its owner (SimulationDrivers.cs:12006, logged [FreightDispatch] warning).";
                result.ResponsibleAuthority = "FreightProductionDriver / FreightServiceAgreements (freight delivery authority)";
                result.AuthoritySourcePath = "Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs";
                result.AuthoritySourceLine = 12006;
                result.Explanation = "Shipment " + snapshot.ObligationId + " entered the existing pre-load failure path after bounded dispatch "
                    + "recovery (" + snapshot.RecoveryAttempts + "/" + snapshot.RecoveryAttemptLimit + " attempts): the shipment failed, "
                    + "the agreement was cancelled, and the source lot was not loaded or debited. This path IS logged — it is a "
                    + "terminal disposition, not a strand.";
                return result;
            }

            bool recoveryExhausted = snapshot.RecoveryAttemptLimit > 0 && snapshot.RecoveryAttempts >= snapshot.RecoveryAttemptLimit;
            if (recoveryExhausted && snapshot.IsPostLoadCommitted)
            {
                result.Disposition = LifecycleDisposition.TerminalButUnrecovered;
                result.Severity = IncidentSeverity.Critical;
                result.ResponsibleAuthority = "FreightProductionDriver / FreightServiceAgreements (freight delivery authority)";
                result.AuthoritySourcePath = "Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs";
                result.AuthoritySourceLine = 12019;
                result.RepairOwner = "Lane 9d (F6-2 post-load strand — the new CRITICAL warning targets this else-branch; add a disposition for the committed cargo)";
                result.EarliestBlocker = "Dispatch task recovery exhausted (" + snapshot.RecoveryAttempts + "/" + snapshot.RecoveryAttemptLimit
                    + " attempts) after load/commit: the shipment 'remains held' (SimulationDrivers.cs:12019) with no disposition.";
                result.EarliestBlockerSeconds = snapshot.LastProgressSimSeconds;
                result.Explanation = "Shipment " + snapshot.ObligationId + " is post-load/committed and its dispatch task recovery is exhausted, "
                    + "but the recovery's else-branch only day-keys a refusal ('remains held') — it neither fails the shipment nor cancels "
                    + "the agreement, so the cargo sits with no recorded outcome. This is the silent half of F6-2: the pre-load path "
                    + "is logged and disposed, the post-load path is not.";
                return result;
            }

            if (!snapshot.HasLivenessBound && snapshot.DeadlineSimSeconds < 0
                && (string.Equals(status, "Planned", StringComparison.Ordinal)
                    || string.Equals(status, "Loading", StringComparison.Ordinal)))
            {
                result.Disposition = LifecycleDisposition.UnboundedWait;
                result.Severity = IncidentSeverity.Warning;
                result.ResponsibleAuthority = "FreightDispatchStaging / FreightProductionDriver (freight dispatch authority)";
                result.AuthoritySourcePath = "Assets/Scripts/Domains/Economy/Businesses/Freight/FreightDispatchStaging.cs";
                result.RepairOwner = "Lane 9d (C-4 staging deadline; W SF-1 / E-18 ETA + overdue fields on LogisticsShipmentState)";
                result.EarliestBlocker = "No staging deadline exists: zero deadline/timeout/bound/max-wait/stale matches in FreightDispatchStaging.cs (C R-8 CONFIRMED).";
                result.Explanation = "Shipment " + snapshot.ObligationId + " waits in staging (" + status + ") with NO deadline and NO liveness bound: "
                    + "nothing unblocks it, nothing fails it, and LogisticsShipmentState carries no ETA/overdue field (E-18 / W SF-1), "
                    + "so the wait cannot even be measured as overdue. An unbounded wait the tool can show — and the repair must bound.";
                result.MissingEvidence.Add("a staging deadline or an ETA/overdue field on the shipment");
                return result;
            }

            if (snapshot.DeadlineSimSeconds >= 0 && snapshot.NowSimSeconds >= 0
                && snapshot.NowSimSeconds > snapshot.DeadlineSimSeconds)
            {
                result.Disposition = LifecycleDisposition.PastDeadline;
                result.Severity = IncidentSeverity.Warning;
                result.ResponsibleAuthority = "FreightProductionDriver / FreightServiceAgreements (freight delivery authority)";
                result.Explanation = "Shipment " + snapshot.ObligationId + " is past its " + snapshot.DeadlineKind
                    + " deadline (" + ((snapshot.NowSimSeconds - snapshot.DeadlineSimSeconds) / 3600d).ToString("0.0")
                    + " simulated hours over) with no recorded outcome.";
                return result;
            }

            result.Disposition = LifecycleDisposition.Healthy;
            result.Severity = IncidentSeverity.Information;
            result.ResponsibleAuthority = "FreightProductionDriver / FreightServiceAgreements (freight delivery authority)";
            result.Explanation = "Shipment " + snapshot.ObligationId + " is " + status + " — inside its normal lifecycle."
                + (snapshot.DeadlineSimSeconds >= 0 ? " Deadline (" + snapshot.DeadlineKind + ") not yet reached." : " No deadline is recorded for it.");
            if (snapshot.DeadlineSimSeconds < 0)
                result.MissingEvidence.Add("a deadline or ETA on the shipment (LogisticsShipmentState has none — E-18)");
            return result;
        }

        /// <summary>
        /// Inspects one furniture order / install. A completed task whose
        /// saved order remains unresolved is still a serious lifecycle
        /// mismatch. The current source has a resolver; this assessment
        /// identifies the observed state and directs investigation to the
        /// task-completion/order-persistence boundary rather than claiming
        /// that the resolver is missing.
        /// </summary>
        public static LifecycleAssessment InspectFurnitureOrder(ObligationLifecycleSnapshot snapshot)
        {
            var result = NewAssessment(snapshot);

            if (snapshot.PlacementRefused)
            {
                result.Disposition = LifecycleDisposition.TerminalCompleted;
                result.Severity = IncidentSeverity.Information;
                result.TerminalDisposition = "Placement refused — goods remain in household custody; the order state is recorded.";
                result.ResponsibleAuthority = "FurnitureInstallBinding (furniture placement authority)";
                result.Explanation = "Furniture order " + snapshot.ObligationId + " was refused at placement: "
                    + result.TerminalDisposition;
                return result;
            }

            if (snapshot.OrderPaid && snapshot.InstallTaskCompleted && !snapshot.InstallOutcomeResolved)
            {
                result.Disposition = LifecycleDisposition.TerminalButUnrecovered;
                result.Severity = IncidentSeverity.Warning;
                result.ResponsibleAuthority = "FurnishingInstallTaskDriver / Pass 9 furniture-order persistence";
                result.AuthoritySourcePath = "Assets/Scripts/Domains/Economy/GenericBusiness/FurnishingInstallTaskDriver.cs";
                result.AuthoritySourceLine = 366;
                result.RepairOwner = "Trace install-task completion, order-line advancement, and Save/Continue restoration";
                result.EarliestBlocker = "Captured evidence has a completed furnishing.install task but an unresolved paid order; "
                    + "check resolver execution and persisted order state.";
                result.EarliestBlockerSeconds = snapshot.LastProgressSimSeconds;
                result.Explanation = "Furniture order " + snapshot.ObligationId + " is paid and its furnishing.install task"
                    + (!string.IsNullOrWhiteSpace(snapshot.RelatedTaskId) ? " (" + snapshot.RelatedTaskId + ")" : "")
                    + " completed, but the saved order remains unresolved. This is a task/order lifecycle mismatch; the current "
                    + "source has an outcome resolver, so trace whether it ran or whether restoration lost its update. The goods "
                    + "remain in household custody; the order is neither installed nor failed.";
                result.SourceNote = "Synthetic failure-shaped snapshot retained for regression coverage. Current source includes "
                    + "FurnishingInstallTaskDriver task minting and completion resolution; no live mismatch has been captured on this HEAD.";
                return result;
            }

            if (snapshot.OrderPaid && !snapshot.InstallTaskCompleted)
            {
                result.Disposition = LifecycleDisposition.Healthy;
                result.Severity = IncidentSeverity.Information;
                result.ResponsibleAuthority = "FurnitureStorePurchase / FurnitureInstallBinding (furniture order authority)";
                result.Explanation = "Furniture order " + snapshot.ObligationId + " is paid and its install task"
                    + (!string.IsNullOrWhiteSpace(snapshot.RelatedTaskId) ? " (" + snapshot.RelatedTaskId + ")" : "")
                    + " is still open — awaiting installation, no strand evidenced.";
                return result;
            }

            result.Disposition = LifecycleDisposition.Healthy;
            result.Severity = IncidentSeverity.Information;
            result.ResponsibleAuthority = "FurnitureStorePurchase / FurnitureInstallBinding (furniture order authority)";
            result.Explanation = "Furniture order " + snapshot.ObligationId + " is unpaid or not yet installed — no install obligation is outstanding.";
            return result;
        }

        /// <summary>
        /// Generic lifecycle for shopping trips, construction commissions,
        /// production batches and replenishment orders: terminal disposition
        /// when named, deadline when recorded, unbounded wait when neither
        /// a deadline nor a liveness bound covers a non-terminal state.
        /// Shopping Outbound HAS a bound (24h absolute deadline, C R-5
        /// CONFIRMED), so a shopping trip with no recorded deadline is
        /// missing evidence, not an unbounded wait.
        /// </summary>
        public static LifecycleAssessment InspectGenericObligation(ObligationLifecycleSnapshot snapshot)
        {
            var result = NewAssessment(snapshot);

            if (!string.IsNullOrWhiteSpace(snapshot.TerminalDisposition))
            {
                result.Disposition = LifecycleDisposition.TerminalCompleted;
                result.Severity = IncidentSeverity.Information;
                result.TerminalDisposition = snapshot.TerminalDisposition;
                result.Explanation = KindLabel(snapshot.Kind) + " " + snapshot.ObligationId + " closed: " + snapshot.TerminalDisposition;
                return result;
            }

            if (snapshot.DeadlineSimSeconds >= 0 && snapshot.NowSimSeconds >= 0
                && snapshot.NowSimSeconds > snapshot.DeadlineSimSeconds)
            {
                result.Disposition = LifecycleDisposition.PastDeadline;
                result.Severity = IncidentSeverity.Warning;
                result.Explanation = KindLabel(snapshot.Kind) + " " + snapshot.ObligationId + " is past its "
                    + snapshot.DeadlineKind + " deadline (" + ((snapshot.NowSimSeconds - snapshot.DeadlineSimSeconds) / 3600d).ToString("0.0")
                    + " simulated hours over) with no recorded outcome.";
                return result;
            }

            if (!snapshot.HasLivenessBound && snapshot.DeadlineSimSeconds < 0)
            {
                if (snapshot.Kind == ObligationKind.ShoppingTrip)
                {
                    result.Disposition = LifecycleDisposition.Unknown;
                    result.Severity = IncidentSeverity.Unknown;
                    result.Explanation = KindLabel(snapshot.Kind) + " " + snapshot.ObligationId
                        + " has no recorded deadline, but shopping Outbound HAS a 24h absolute deadline (C R-5 CONFIRMED) — "
                        + "the deadline was not captured for this trip, so the Inspector cannot judge its liveness.";
                    result.MissingEvidence.Add("the trip's Outbound deadline (ActiveRouteDeadlineGameSeconds / MaxShoppingRouteDeadlineSeconds = 24h)");
                    return result;
                }

                result.Disposition = LifecycleDisposition.UnboundedWait;
                result.Severity = IncidentSeverity.Warning;
                result.Explanation = KindLabel(snapshot.Kind) + " " + snapshot.ObligationId + " is " + snapshot.Status
                    + " with NO deadline and NO liveness bound: nothing unblocks it and nothing fails it. "
                    + "An unbounded wait the tool can show — a repair must bound it or name its sweep.";
                result.MissingEvidence.Add("a deadline, or the sweep/driver that bounds this state");
                return result;
            }

            result.Disposition = LifecycleDisposition.Healthy;
            result.Severity = IncidentSeverity.Information;
            result.Explanation = KindLabel(snapshot.Kind) + " " + snapshot.ObligationId + " is " + snapshot.Status
                + " — inside its normal lifecycle."
                + (snapshot.DeadlineSimSeconds >= 0 ? " Deadline (" + snapshot.DeadlineKind + ") not yet reached." : string.Empty);
            return result;
        }

        /// <summary>
        /// Summarises a set of assessments: counts per disposition /
        /// severity / kind, and the kinds with zero observations (not
        /// observed is not a pass — a kind the Inspector never saw is a
        /// coverage gap, not a clean bill of health).
        /// </summary>
        public static LifecycleCoverage Summarize(IReadOnlyList<LifecycleAssessment> assessments)
        {
            var coverage = new LifecycleCoverage();
            if (assessments != null)
            {
                foreach (LifecycleAssessment assessment in assessments)
                {
                    if (assessment == null) continue;
                    coverage.TotalAssessed++;
                    if (assessment.IsIncident) coverage.IncidentCount++;
                    Increment(coverage.ByDisposition, assessment.Disposition);
                    Increment(coverage.BySeverity, assessment.Severity);
                    Increment(coverage.ByKind, assessment.Kind);
                }
            }

            foreach (ObligationKind kind in AllKinds())
            {
                if (!coverage.ByKind.ContainsKey(kind)) coverage.KindsNotObserved.Add(kind);
            }
            return coverage;
        }

        /// <summary>Every obligation kind the Inspector knows about.</summary>
        public static ObligationKind[] AllKinds()
        {
            return new[]
            {
                ObligationKind.Task,
                ObligationKind.ShoppingTrip,
                ObligationKind.FreightRequest,
                ObligationKind.FreightAgreement,
                ObligationKind.FreightShipment,
                ObligationKind.FurnitureOrder,
                ObligationKind.FurnitureInstall,
                ObligationKind.ConstructionCommission,
                ObligationKind.ProductionBatch,
                ObligationKind.ReplenishmentOrder,
            };
        }

        /// <summary>
        /// Inspects and, when the disposition is an incident, emits the
        /// finding to the Lane 1 core through the Investigator's null-safe
        /// EmitToCore. Healthy / within-threshold / terminal-completed
        /// assessments are never emitted — the core must not fill with
        /// non-incidents.
        /// </summary>
        public static LifecycleAssessment InspectAndEmit(ObligationLifecycleSnapshot snapshot)
        {
            LifecycleAssessment assessment = Inspect(snapshot);
            EmitToCore(assessment);
            return assessment;
        }

        /// <summary>Emits one incident assessment to the Lane 1 core. Non-incidents are not emitted.</summary>
        public static void EmitToCore(LifecycleAssessment assessment)
        {
            if (assessment == null || !assessment.IsIncident) return;
            var finding = new IncidentFinding
            {
                IncidentId = "LIFECYCLE-" + assessment.Kind + "-" + assessment.ObligationId,
                Title = "Lifecycle: " + KindLabel(assessment.Kind) + " " + assessment.ObligationId + " — " + assessment.Disposition,
                Severity = assessment.Severity,
                BlockerKind = MapToDestinationBlocker(assessment.BlockerClass),
                ResponsibleAuthority = assessment.ResponsibleAuthority ?? string.Empty,
                AuthoritySourcePath = assessment.AuthoritySourcePath ?? string.Empty,
                AuthoritySourceLine = assessment.AuthoritySourceLine,
                EarliestBlocker = assessment.EarliestBlocker ?? string.Empty,
                EarliestBlockerSeconds = assessment.EarliestBlockerSeconds,
                Explanation = assessment.Explanation ?? string.Empty,
                MissingEvidence = assessment.MissingEvidence ?? new List<string>(),
                EvidenceComplete = assessment.MissingEvidence == null || assessment.MissingEvidence.Count == 0,
                SourceNote = assessment.SourceNote ?? string.Empty,
                TaskId = assessment.Kind == ObligationKind.Task ? assessment.ObligationId : string.Empty,
            };
            IncidentInvestigator.EmitToCore(finding);
        }

        // ---- Proving cases (recorded run / source evidence — no live run on this HEAD) ----

        /// <summary>
        /// Proving case 1 (C R-18 SOLVED): the interrupted
        /// domestic.fuel-handling strand, canned from the retained-run
        /// record — T9, owner H7, interrupted, never re-queued, worked 0,
        /// remaining 41, no assignee, no reservation, no work location.
        /// </summary>
        public static ObligationLifecycleSnapshot KnownFuelHandlingStrandSnapshot()
        {
            const double hour = 3600d;
            return new ObligationLifecycleSnapshot
            {
                ObligationId = "T9",
                Kind = ObligationKind.Task,
                DefinitionId = "domestic.fuel-handling",
                Label = "Fetch and handle household fuel",
                Status = "Queued",
                StateEnteredSimSeconds = 2 * hour,
                LastProgressSimSeconds = 2 * hour,
                FirstObservedSimSeconds = 0,
                NowSimSeconds = 85.5 * hour,
                DayIndex = 3,
                OwnerId = "H7",
                HouseholdId = -1,
                WorkedMinutes = 0,
                RemainingMinutes = 41,
                PlannedMinutes = 41,
                InterruptionCount = 1,
                LastTransition = "InterruptTask",
                RefusalReason = "firewood: no firewood storage is authored on the home.",
                RecoveryState = "owner=DomesticLaborDriver; state=WAITING_FOR_CAPABILITY_CHANGE; "
                    + "retryAfterDay=2147483647; activityLocation=firewood: no firewood storage is authored on the home.; workerReservation=none",
                HasAssignee = false,
                HasReservation = false,
                HasLivenessBound = false,
            };
        }

        /// <summary>Proving case 1 run through the lifecycle inspection.</summary>
        public static LifecycleAssessment InspectKnownFuelHandlingStrand()
        {
            LifecycleAssessment assessment = InspectTask(KnownFuelHandlingStrandSnapshot());
            assessment.SourceNote = "Proving case 1 (C R-18 SOLVED): recorded evidence from the retained 60-day run "
                + "(T9/T10/T170) and 5-day run (T8/T9/T11) — interrupted domestic.fuel-handling tasks, never re-queued. "
                + "No live run exists on this HEAD; this is the retained-run record, not a fresh measurement.";
            return assessment;
        }

        /// <summary>
        /// Proving case 2 (F6-2, narrowed): a terminal-Accepted freight
        /// request whose agreement the recovery cancelled, post-load, with
        /// the recovery limit reached and no reopen path.
        /// </summary>
        public static ObligationLifecycleSnapshot KnownFreightRequestStrandSnapshot()
        {
            return new ObligationLifecycleSnapshot
            {
                ObligationId = "request-1",
                Kind = ObligationKind.FreightRequest,
                Label = "Freight service request for a committed lot",
                Status = "Accepted",
                LastProgressSimSeconds = 9 * 86400d,
                NowSimSeconds = 12 * 86400d,
                DayIndex = 12,
                AgreementCancelled = true,
                LotCommitted = true,
                HasReopenPath = false,
                RecoveryAttempts = 2,
                RecoveryAttemptLimit = 2,
                IsPostLoadCommitted = true,
                RelatedLotId = "lot-millbrook-flour-1",
                RelatedAgreementId = "freight-agreement:request-1",
                RefusalReason = "Prepared freight dispatch failed after 2 bounded re-preparation attempt(s).",
            };
        }

        /// <summary>Proving case 2 run through the lifecycle inspection.</summary>
        public static LifecycleAssessment InspectKnownFreightRequestStrand()
        {
            LifecycleAssessment assessment = InspectFreightRequest(KnownFreightRequestStrandSnapshot());
            assessment.SourceNote = "Proving case 2 (F6-2, narrowed per C R-7): the pre-load terminal path IS logged "
                + "(SimulationDrivers.cs:12006); the strand is the terminal-Accepted request left behind with no reopen path. "
                + "No live run exists on this HEAD; this is the source-level defect shape, not a fresh measurement.";
            return assessment;
        }

        /// <summary>
        /// Proving case 3 (C R-8): a shipment waiting in staging with no
        /// deadline and no liveness bound.
        /// </summary>
        public static ObligationLifecycleSnapshot KnownStagingWaitSnapshot()
        {
            return new ObligationLifecycleSnapshot
            {
                ObligationId = "freight-shipment:request-1",
                Kind = ObligationKind.FreightShipment,
                Label = "Freight shipment awaiting dispatch staging",
                Status = "Planned",
                LastProgressSimSeconds = 4 * 86400d,
                NowSimSeconds = 11 * 86400d,
                DayIndex = 11,
                DeadlineSimSeconds = -1,
                HasLivenessBound = false,
                RelatedAgreementId = "freight-agreement:request-1",
            };
        }

        /// <summary>Proving case 3 run through the lifecycle inspection.</summary>
        public static LifecycleAssessment InspectKnownStagingWait()
        {
            LifecycleAssessment assessment = InspectFreightShipment(KnownStagingWaitSnapshot());
            assessment.SourceNote = "Proving case 3 (C R-8 CONFIRMED unbounded): zero deadline/timeout/bound/max-wait/stale "
                + "matches in FreightDispatchStaging.cs. No live run exists on this HEAD; this is the source-level defect shape.";
            return assessment;
        }

        /// <summary>
        /// Synthetic proving case 4: a paid furniture order whose completed
        /// furnishing.install task has not advanced the order. This tests
        /// mismatch classification, not current-source resolver absence.
        /// </summary>
        public static ObligationLifecycleSnapshot KnownFurnitureInstallStrandSnapshot()
        {
            return new ObligationLifecycleSnapshot
            {
                ObligationId = "furniture-order-1",
                Kind = ObligationKind.FurnitureOrder,
                Label = "Paid furniture order awaiting install outcome",
                Status = "Open",
                LastProgressSimSeconds = 6 * 86400d,
                NowSimSeconds = 9 * 86400d,
                DayIndex = 9,
                OrderPaid = true,
                InstallTaskCompleted = true,
                InstallOutcomeResolved = false,
                RelatedTaskId = "furnishing.install-1",
            };
        }

        /// <summary>Proving case 4 run through the lifecycle inspection.</summary>
        public static LifecycleAssessment InspectKnownFurnitureInstallStrand()
        {
            LifecycleAssessment assessment = InspectFurnitureOrder(KnownFurnitureInstallStrandSnapshot());
            assessment.SourceNote = "Synthetic failure-shaped snapshot only: the current source contains the "
                + "FurnishingInstallTaskDriver outcome resolver. No live task/order mismatch has been captured on this HEAD.";
            return assessment;
        }

        // ---- Internals ----

        private static LifecycleAssessment NewAssessment(ObligationLifecycleSnapshot snapshot)
        {
            return new LifecycleAssessment
            {
                ObligationId = snapshot.ObligationId ?? string.Empty,
                Kind = snapshot.Kind,
                DefinitionId = snapshot.DefinitionId ?? string.Empty,
                Status = snapshot.Status ?? string.Empty,
                HasAssignee = snapshot.HasAssignee,
                StallThresholdSeconds = -1,
                StalledSeconds = -1,
            };
        }

        private static StallClassification ClassifyTaskStall(ObligationLifecycleSnapshot snapshot)
        {
            var evidence = new StallEvidence
            {
                TaskId = snapshot.ObligationId ?? string.Empty,
                TaskDefinitionId = snapshot.DefinitionId ?? string.Empty,
                TaskStatus = snapshot.Status ?? string.Empty,
                TaskOwnerId = snapshot.OwnerId ?? string.Empty,
                PersonId = snapshot.PersonId ?? string.Empty,
                HouseholdId = snapshot.HouseholdId,
                WorkedMinutes = snapshot.WorkedMinutes,
                RemainingMinutes = snapshot.RemainingMinutes,
                InterruptionCount = snapshot.InterruptionCount,
                LastTransition = snapshot.LastTransition ?? string.Empty,
                FirstObservedSeconds = snapshot.FirstObservedSimSeconds,
                LastProgressSeconds = snapshot.LastProgressSimSeconds,
                NowSeconds = snapshot.NowSimSeconds,
                RecoveryState = snapshot.RecoveryState ?? string.Empty,
                RefusalReason = snapshot.RefusalReason ?? string.Empty,
                ReservationId = snapshot.ReservationId ?? string.Empty,
                WorkstationOccupied = snapshot.WorkstationOccupied,
                OccupantDescription = snapshot.OccupantDescription ?? string.Empty,
                WorkerBusyOnOtherTask = snapshot.WorkerBusyOnOtherTask,
                IntegrityViolation = snapshot.IntegrityViolation,
                IntegrityDetail = snapshot.IntegrityDetail ?? string.Empty,
                SkillMentionedAsOnlyCause = snapshot.SkillMentionedAsOnlyCause,
            };
            return IncidentInvestigator.ClassifyStall(evidence);
        }

        private static void FillTaskAuthorityAndBlocker(LifecycleAssessment result, ObligationLifecycleSnapshot snapshot)
        {
            result.ResponsibleAuthority = ResolveTaskAuthority(snapshot.DefinitionId, snapshot.RecoveryState);
            // Classify with the Investigator's broader reason heuristic
            // (it matches the retained-run wording "no firewood storage is
            // authored"; the Lane 1 classifier's "no authored" pattern
            // misses it) and map onto the canonical M-R14 taxonomy.
            DestinationBlockerKind kind = IncidentInvestigator.ClassifyRefusalReason(
                FirstNonEmpty(snapshot.RefusalReason, snapshot.RecoveryState));
            result.BlockerClass = MapBlockerKind(kind);
            if (IsFuelHandling(snapshot))
            {
                result.AuthoritySourcePath = "Assets/Scripts/Domains/Population/DomesticLaborDriver.cs";
                result.AuthoritySourceLine = 1191;
            }
        }

        private static bool IsFuelHandling(ObligationLifecycleSnapshot snapshot)
        {
            return string.Equals(snapshot.DefinitionId, "domestic.fuel-handling", StringComparison.Ordinal)
                || ((snapshot.RefusalReason ?? string.Empty).IndexOf("firewood", StringComparison.OrdinalIgnoreCase) >= 0
                    && (snapshot.RecoveryState ?? string.Empty).IndexOf("DomesticLaborDriver", StringComparison.Ordinal) >= 0);
        }

        private static string ResolveTaskAuthority(string definitionId, string recoveryState)
        {
            if (!string.IsNullOrWhiteSpace(recoveryState) && recoveryState.IndexOf("DomesticLaborDriver", StringComparison.Ordinal) >= 0)
            {
                return "DomesticLaborDriver (domestic work authority) — the shared House Natural Wood prefab now authors "
                    + "the distinct exterior 'firewood-handling' point (historical missing-point evidence: "
                    + "checklist CI-5F80FDC40F); its physical route and fuel transfer still require live verification.";
            }
            if (!string.IsNullOrWhiteSpace(definitionId) && definitionId.StartsWith("domestic.", StringComparison.Ordinal))
                return "DomesticLaborDriver (domestic work authority)";
            if (!string.IsNullOrWhiteSpace(definitionId) && definitionId.StartsWith("shopping", StringComparison.OrdinalIgnoreCase))
                return "ShoppingTripDriver / HouseholdShoppingLoop (household shopping authority)";
            if (!string.IsNullOrWhiteSpace(definitionId) && definitionId.IndexOf("bakery", StringComparison.OrdinalIgnoreCase) >= 0)
                return "BakeryShopRuntime (bakery production authority)";
            if (!string.IsNullOrWhiteSpace(recoveryState))
                return "Owning system named in the recovery state: " + recoveryState;
            return "TaskAuthority (owning system not named by the evidence — see missing evidence)";
        }

        private static RefusalCauseClass MapBlockerKind(DestinationBlockerKind kind)
        {
            switch (kind)
            {
                case DestinationBlockerKind.DestinationMissing: return RefusalCauseClass.DestinationMissing;
                case DestinationBlockerKind.DestinationInaccessible: return RefusalCauseClass.DestinationInaccessible;
                case DestinationBlockerKind.DestinationOccupiedOrReserved: return RefusalCauseClass.DestinationOccupiedOrReserved;
                case DestinationBlockerKind.WorkerCannotPerform: return RefusalCauseClass.WorkerCannotPerform;
                case DestinationBlockerKind.OtherObligationTakesPriority: return RefusalCauseClass.OtherObligationTakesPriority;
                case DestinationBlockerKind.OriginInvalid: return RefusalCauseClass.OriginInvalid;
                default: return RefusalCauseClass.Unclassified;
            }
        }

        private static DestinationBlockerKind MapToDestinationBlocker(RefusalCauseClass causeClass)
        {
            switch (causeClass)
            {
                case RefusalCauseClass.DestinationMissing: return DestinationBlockerKind.DestinationMissing;
                case RefusalCauseClass.DestinationInaccessible: return DestinationBlockerKind.DestinationInaccessible;
                case RefusalCauseClass.DestinationOccupiedOrReserved: return DestinationBlockerKind.DestinationOccupiedOrReserved;
                case RefusalCauseClass.WorkerCannotPerform: return DestinationBlockerKind.WorkerCannotPerform;
                case RefusalCauseClass.OtherObligationTakesPriority: return DestinationBlockerKind.OtherObligationTakesPriority;
                case RefusalCauseClass.OriginInvalid: return DestinationBlockerKind.OriginInvalid;
                default: return DestinationBlockerKind.None;
            }
        }

        private static string KindLabel(ObligationKind kind)
        {
            switch (kind)
            {
                case ObligationKind.Task: return "task";
                case ObligationKind.ShoppingTrip: return "shopping trip";
                case ObligationKind.FreightRequest: return "freight request";
                case ObligationKind.FreightAgreement: return "freight agreement";
                case ObligationKind.FreightShipment: return "freight shipment";
                case ObligationKind.FurnitureOrder: return "furniture order";
                case ObligationKind.FurnitureInstall: return "furniture install";
                case ObligationKind.ConstructionCommission: return "construction commission";
                case ObligationKind.ProductionBatch: return "production batch";
                case ObligationKind.ReplenishmentOrder: return "replenishment order";
                default: return "obligation";
            }
        }

        private static string FirstNonEmpty(string first, string second)
        {
            return !string.IsNullOrWhiteSpace(first) ? first : (second ?? string.Empty);
        }

        private static bool TryParseStatus(string status, out TaskStatus parsed)
        {
            parsed = TaskStatus.Queued;
            if (string.IsNullOrWhiteSpace(status)) return false;
            try
            {
                parsed = (TaskStatus)Enum.Parse(typeof(TaskStatus), status.Trim(), true);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static void Increment<TKey>(Dictionary<TKey, int> counts, TKey key)
        {
            int current;
            counts.TryGetValue(key, out current);
            counts[key] = current + 1;
        }
    }
}
