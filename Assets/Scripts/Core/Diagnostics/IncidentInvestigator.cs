// Lane 2 (Muse Pass, Nov 20 2026) — Simulation Incident Investigator core.
//
// Pure-System causal analysis: NO UnityEngine, NO Editor, NO gameplay
// authority references, so this file joins the Roslyn harness closure and
// is covered by IncidentInvestigatorTests. The Editor window
// (IncidentInvestigatorWindow) and the trace recorder only MAP their own
// event types onto the plain input types below; this class never reads live
// game state and never becomes a parallel source of truth — it observes
// evidence handed to it and names the authority that owns the answer.
//
// Severity comes from CIRCUMSTANCES, never from elapsed time alone
// (Agent G item 7): an occupied workstation is INFORMATION, a worker idle
// all day without a valid cause is WARNING, an integrity violation is
// CRITICAL, and insufficient evidence is UNKNOWN. The recorder's per-state
// stall thresholds (InProgress 6h, Assigned/ResourcesReserved 12h,
// WaitingOnDependency/WaitingOnPassiveProcess 48h, other incl. Queued 72h)
// are the floor for "stalled"; the circumstance test decides the severity.
//
// M-05: no refusal path reads capability. Skill must NEVER be used as the
// explanation for a failed task when the real problem is a missing physical
// destination — a skill-only stated cause yields UNKNOWN with the missing
// physical-destination evidence named, never a WARNING blamed on skill.
//
// M-R14: physical-task causal classification distinguishes the five
// destination states plus origin-invalid as a sixth class.
using System;
using System.Collections.Generic;

namespace LandLedgers.Diagnostics
{
    // Severity is the single canonical scale in IncidentSeverity.cs
    // (Lane 1): Unknown = 0, Information = 1, Warning = 2, Critical = 3.
    // Lane 2's local copy was removed at the Lane 1 merge — one scale,
    // no emitter invents its own.

    /// <summary>
    /// Causal class of a physical-task refusal / interrupt (M-R14).
    /// Five destination states plus origin-invalid (M-06) as a sixth class.
    /// </summary>
    public enum DestinationBlockerKind
    {
        None = 0,
        DestinationMissing = 1,
        DestinationInaccessible = 2,
        DestinationOccupiedOrReserved = 3,
        WorkerCannotPerform = 4,
        OtherObligationTakesPriority = 5,
        OriginInvalid = 6,
    }

    /// <summary>
    /// One observed trace row, in plain form. The recorder and the Editor
    /// window map their own event types onto this; the investigator never
    /// depends on the recorder's Unity-guarded types.
    /// </summary>
    public sealed class IncidentEvidenceEvent
    {
        public double SimulatedSeconds = -1;
        public int DayIndex = -1;
        public string EventType = string.Empty;
        public string Transition = string.Empty;
        public string TaskId = string.Empty;
        public string TaskDefinitionId = string.Empty;
        public string TaskStatus = string.Empty;
        public string TaskOwnerId = string.Empty;
        public string PersonId = string.Empty;
        public int PersonNumericId = -1;
        public int HouseholdId = -1;
        public string Reason = string.Empty;
        public string Detail = string.Empty;
        public string RecoveryState = string.Empty;
        public string ActivityLocationId = string.Empty;
        public string ReservationId = string.Empty;
        public int WorkedMinutes = -1;
        public int RemainingMinutes = -1;
        public int PlannedMinutes = -1;
        public int InterruptionCount;
    }

    /// <summary>One entry of the causal timeline, in time order.</summary>
    public sealed class CausalTimelineEntry
    {
        public double SimulatedSeconds;
        public int DayIndex = -1;
        public string Label = string.Empty;
        public string Detail = string.Empty;
        public bool IsBlocker;
        public DestinationBlockerKind BlockerKind = DestinationBlockerKind.None;
    }

    /// <summary>Everything the circumstance test needs for one stalled task.</summary>
    public sealed class StallEvidence
    {
        public string TaskId = string.Empty;
        public string TaskDefinitionId = string.Empty;
        public string TaskStatus = string.Empty;
        public string TaskOwnerId = string.Empty;
        public string PersonId = string.Empty;
        public int HouseholdId = -1;
        public int WorkedMinutes;
        public int RemainingMinutes;
        public int InterruptionCount;
        public string LastTransition = string.Empty;
        public double FirstObservedSeconds = -1;
        public double LastProgressSeconds = -1;
        public double NowSeconds = -1;
        public string RecoveryState = string.Empty;
        public string RefusalReason = string.Empty;
        public string ReservationId = string.Empty;
        public bool WorkstationOccupied;
        public string OccupantDescription = string.Empty;
        public bool WorkerBusyOnOtherTask;
        public bool IntegrityViolation;
        public string IntegrityDetail = string.Empty;
        /// <summary>True when the ONLY stated cause for the failure names skill/capability (M-05).</summary>
        public bool SkillMentionedAsOnlyCause;
    }

    /// <summary>Outcome of the circumstance test for one stalled task.</summary>
    public sealed class StallClassification
    {
        public IncidentSeverity Severity = IncidentSeverity.Unknown;
        public DestinationBlockerKind BlockerKind = DestinationBlockerKind.None;
        public string Explanation = string.Empty;
        public List<string> MissingEvidence = new List<string>();
        public double StallThresholdSeconds;
        public double StalledSeconds;
    }

    /// <summary>
    /// Evidence for the 24-loaf wholesale obligation (proving case 2).
    /// Values are authored facts, not live measurements: the obligation is
    /// authored in DevelopmentTownConfiguration.asset (E-06 SOLVED).
    /// </summary>
    public sealed class WholesaleObligationEvidence
    {
        public string BuyerBusinessKey = string.Empty;
        public string ProductId = string.Empty;
        public int UnitsPerDelivery;
        public int DeliveryCadenceDays = 1;
        public int PricePerUnitCents;
        public int FirstDeliveryDayIndex;
        public string ConfigSource = string.Empty;
        public string AppliedBy = string.Empty;
        public string BlockingGate = string.Empty;
    }

    /// <summary>A classified incident: what happened, who owns it, and the earliest confirmed blocker.</summary>
    public sealed class IncidentFinding
    {
        public string IncidentId = string.Empty;
        public string Title = string.Empty;
        public IncidentSeverity Severity = IncidentSeverity.Unknown;
        public DestinationBlockerKind BlockerKind = DestinationBlockerKind.None;
        public string ResponsibleAuthority = string.Empty;
        public string AuthoritySourcePath = string.Empty;
        public int AuthoritySourceLine = -1;
        public string EarliestBlocker = string.Empty;
        public double EarliestBlockerSeconds = -1;
        public string Explanation = string.Empty;
        public List<string> MissingEvidence = new List<string>();
        public List<CausalTimelineEntry> Timeline = new List<CausalTimelineEntry>();
        public bool EvidenceComplete;
        public string SourceNote = string.Empty;
        /// <summary>Task under investigation (empty for non-task incidents such as the wholesale obligation).</summary>
        public string TaskId = string.Empty;
        /// <summary>Assignee numeric id when known, else -1.</summary>
        public int PersonNumericId = -1;
        /// <summary>Household id when known (owner-id fallback applied), else -1.</summary>
        public int HouseholdId = -1;
    }

    /// <summary>
    /// LANE 1 SEAM (narrow, coordinator-wired at merge time).
    /// Lane 1's incident/warning core implements this one interface; the
    /// Investigator hands it classified, deduped findings and expects the
    /// core to own dedup, retention, severity surfacing and export. The core
    /// must be idempotent for a repeated IncidentId and must never throw for
    /// a duplicate — the Investigator treats a core failure as a lost
    /// emission, not as a reason to reclassify. Until Lane 1 lands,
    /// <see cref="IncidentInvestigator.IncidentCore"/> stays null and
    /// findings are returned to the caller only.
    /// Call site: <see cref="IncidentInvestigator.EmitToCore"/>.
    /// </summary>
    public interface IIncidentCore
    {
        /// <summary>Accept one classified incident. The core owns dedup and retention.</summary>
        void EmitClassifiedIncident(IncidentFinding finding);
    }

    /// <summary>
    /// Simulation Incident Investigator core (Lane 2, Tool 01).
    /// Stateless pure functions over plain evidence. Diagnostics OBSERVE
    /// authoritative state; this class assigns no work, changes no routes,
    /// moves no goods, money, food, transport or labour.
    /// </summary>
    public static class IncidentInvestigator
    {
        /// <summary>
        /// Lane 1 seam endpoint. Set by the coordinator at merge time to
        /// Lane 1's incident/warning core. Null until then; null is normal.
        /// </summary>
        public static IIncidentCore IncidentCore { get; set; }

        /// <summary>
        /// The single static call site where classified findings flow into
        /// Lane 1's core. Null-safe: with no core attached the finding is
        /// returned to the caller only and nothing is emitted.
        /// </summary>
        public static void EmitToCore(IncidentFinding finding)
        {
            if (finding == null) return;
            if (IncidentCore != null) IncidentCore.EmitClassifiedIncident(finding);
        }

        /// <summary>
        /// Per-state stall thresholds in simulated seconds, matching
        /// NpcActivityTraceRecorder.DetectStall: InProgress 6h,
        /// Assigned/ResourcesReserved 12h,
        /// WaitingOnDependency/WaitingOnPassiveProcess 48h, other (incl.
        /// Queued) 72h. Unknown/empty status takes the 72h bound.
        /// </summary>
        public static double StallThresholdSeconds(string taskStatus)
        {
            if (string.Equals(taskStatus, "InProgress", StringComparison.Ordinal)) return 6d * 3600d;
            if (string.Equals(taskStatus, "Assigned", StringComparison.Ordinal)
                || string.Equals(taskStatus, "ResourcesReserved", StringComparison.Ordinal)) return 12d * 3600d;
            if (string.Equals(taskStatus, "WaitingOnDependency", StringComparison.Ordinal)
                || string.Equals(taskStatus, "WaitingOnPassiveProcess", StringComparison.Ordinal)) return 48d * 3600d;
            return 72d * 3600d;
        }

        /// <summary>
        /// Classifies a refusal/interrupt reason string into its M-R14
        /// destination class. Heuristic over the reason text the owning
        /// system already emits — it tags reasons, it does not invent them.
        /// Unmatched reasons return <see cref="DestinationBlockerKind.None"/>
        /// with the reason preserved verbatim by the caller.
        /// </summary>
        public static DestinationBlockerKind ClassifyRefusalReason(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason)) return DestinationBlockerKind.None;
            string text = reason.ToLowerInvariant();
            if (text.Contains("startnotwalkable") || text.Contains("outside the authored walkable floor")
                || text.Contains("origin") && text.Contains("not walkable"))
                return DestinationBlockerKind.OriginInvalid;
            if (text.Contains("no firewood storage is authored") || text.Contains("not authored")
                || text.Contains("no firewood") || text.Contains("missing") && text.Contains("activity")
                || text.Contains("no known seller") || text.Contains("no stock at seller"))
                return DestinationBlockerKind.DestinationMissing;
            if (text.Contains("not reachable") || text.Contains("unreachable") || text.Contains("no physical route reaches")
                || text.Contains("destination entrance is not authored") || text.Contains("inaccessible"))
                return DestinationBlockerKind.DestinationInaccessible;
            if (text.Contains("occupied") || text.Contains("reserved") || text.Contains("already has an active task")
                || text.Contains("carrying freight") || text.Contains("scheduled route"))
                return DestinationBlockerKind.DestinationOccupiedOrReserved;
            if (text.Contains("no worker") || text.Contains("no shopper available") || text.Contains("cannot perform")
                || text.Contains("no counter service"))
                return DestinationBlockerKind.WorkerCannotPerform;
            if (text.Contains("higher priority") || text.Contains("takes priority") || text.Contains("other obligation"))
                return DestinationBlockerKind.OtherObligationTakesPriority;
            return DestinationBlockerKind.None;
        }

        /// <summary>
        /// Resolves the household for an event. The anomaly report loses the
        /// household (household=-1) while the trace records the owner
        /// (ownerId H7/H4/H6) — C R-18. Prefer the recorded household id,
        /// fall back to parsing the owner id ("H7" -&gt; 7).
        /// </summary>
        public static int ResolveHouseholdId(int householdId, string taskOwnerId)
        {
            if (householdId >= 0) return householdId;
            if (string.IsNullOrWhiteSpace(taskOwnerId)) return -1;
            string text = taskOwnerId.Trim();
            if ((text.StartsWith("H", StringComparison.OrdinalIgnoreCase)) && text.Length > 1
                && int.TryParse(text.Substring(1), out int parsed) && parsed >= 0) return parsed;
            return -1;
        }

        /// <summary>
        /// Builds the causal timeline from observed events (time order) and
        /// identifies the earliest CONFIRMED blocker: the first event whose
        /// reason/recovery text classifies to a destination class, or whose
        /// transition is an interrupt/failure carrying a reason. Detection
        /// time is never the blocker — the blocker is the first evidence
        /// that names a cause.
        /// </summary>
        public static List<CausalTimelineEntry> BuildCausalTimeline(
            IReadOnlyList<IncidentEvidenceEvent> events, out CausalTimelineEntry earliestBlocker)
        {
            earliestBlocker = null;
            var timeline = new List<CausalTimelineEntry>();
            if (events == null) return timeline;
            var ordered = new List<IncidentEvidenceEvent>(events);
            ordered.Sort((left, right) => left.SimulatedSeconds.CompareTo(right.SimulatedSeconds));
            foreach (IncidentEvidenceEvent item in ordered)
            {
                DestinationBlockerKind kind = ClassifyRefusalReason(
                    string.IsNullOrWhiteSpace(item.Reason) ? item.RecoveryState : item.Reason + " " + item.RecoveryState);
                bool isInterruptOrFailure = string.Equals(item.Transition, "InterruptTask", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(item.Transition, "Failed", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(item.EventType, "task-refusal", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(item.EventType, "worker-unavailable", StringComparison.OrdinalIgnoreCase);
                bool isBlocker = kind != DestinationBlockerKind.None
                    || (isInterruptOrFailure && !string.IsNullOrWhiteSpace(item.Reason));
                var entry = new CausalTimelineEntry
                {
                    SimulatedSeconds = item.SimulatedSeconds,
                    DayIndex = item.DayIndex,
                    Label = (item.EventType ?? string.Empty) + "/" + (item.Transition ?? string.Empty),
                    Detail = !string.IsNullOrWhiteSpace(item.Reason) ? item.Reason
                        : !string.IsNullOrWhiteSpace(item.RecoveryState) ? item.RecoveryState
                        : (item.Detail ?? string.Empty),
                    IsBlocker = isBlocker,
                    BlockerKind = kind,
                };
                timeline.Add(entry);
                if (isBlocker && earliestBlocker == null) earliestBlocker = entry;
            }
            return timeline;
        }

        /// <summary>
        /// The circumstance test (G item 7) on top of the per-state stall
        /// thresholds. Elapsed time alone NEVER produces WARNING: below the
        /// threshold the task is not stalled (INFORMATION); at/above it the
        /// circumstances decide.
        /// </summary>
        public static StallClassification ClassifyStall(StallEvidence evidence)
        {
            var result = new StallClassification();
            if (evidence == null)
            {
                result.Severity = IncidentSeverity.Unknown;
                result.Explanation = "No stall evidence was supplied.";
                result.MissingEvidence.Add("stall evidence (task, status, and observation times)");
                return result;
            }

            result.StallThresholdSeconds = StallThresholdSeconds(evidence.TaskStatus);
            result.StalledSeconds = evidence.NowSeconds >= 0 && evidence.LastProgressSeconds >= 0
                ? Math.Max(0d, evidence.NowSeconds - evidence.LastProgressSeconds) : -1d;

            if (evidence.IntegrityViolation)
            {
                result.Severity = IncidentSeverity.Critical;
                result.Explanation = "Integrity violation: " + (evidence.IntegrityDetail ?? string.Empty);
                return result;
            }

            bool hasObservation = evidence.FirstObservedSeconds >= 0 || evidence.LastProgressSeconds >= 0;
            if (!hasObservation && string.IsNullOrWhiteSpace(evidence.TaskStatus))
            {
                result.Severity = IncidentSeverity.Unknown;
                result.Explanation = "Insufficient evidence: the task was never observed, so no stall can be classified.";
                result.MissingEvidence.Add("any observation of the task (status, first/last progress times)");
                return result;
            }

            if (evidence.SkillMentionedAsOnlyCause)
            {
                // M-05: no refusal path reads capability. A skill-only stated
                // cause cannot explain a refusal; the physical destination
                // state is the missing evidence, not a skill verdict.
                result.Severity = IncidentSeverity.Unknown;
                result.Explanation = "The only stated cause names skill/capability, which no refusal path reads (M-05). "
                    + "Skill is not the explanation; the physical destination state is still unknown.";
                result.MissingEvidence.Add("physical destination state (missing / inaccessible / occupied / origin-invalid)");
                return result;
            }

            DestinationBlockerKind kind = ClassifyRefusalReason(
                string.IsNullOrWhiteSpace(evidence.RefusalReason) ? evidence.RecoveryState
                    : evidence.RefusalReason + " " + evidence.RecoveryState);
            result.BlockerKind = kind;

            if (result.StalledSeconds >= 0 && result.StalledSeconds < result.StallThresholdSeconds)
            {
                result.Severity = IncidentSeverity.Information;
                result.Explanation = "Within the per-state stall threshold (" + (result.StallThresholdSeconds / 3600d).ToString("0.##")
                    + "h for " + (evidence.TaskStatus ?? string.Empty) + "); no stall incident.";
                return result;
            }

            switch (kind)
            {
                case DestinationBlockerKind.DestinationOccupiedOrReserved:
                    if (evidence.WorkstationOccupied || !string.IsNullOrWhiteSpace(evidence.ReservationId)
                        || !string.IsNullOrWhiteSpace(evidence.OccupantDescription))
                    {
                        result.Severity = IncidentSeverity.Information;
                        result.Explanation = "The destination is occupied or reserved"
                            + (!string.IsNullOrWhiteSpace(evidence.OccupantDescription) ? " by " + evidence.OccupantDescription : string.Empty)
                            + " — the worker is legitimately waiting, not idle without cause.";
                    }
                    else
                    {
                        result.Severity = IncidentSeverity.Warning;
                        result.Explanation = "The destination is reported occupied or reserved, but no occupant or reservation is named.";
                        result.MissingEvidence.Add("occupant identity or reservation handle for the occupied destination");
                    }
                    return result;
                case DestinationBlockerKind.OtherObligationTakesPriority:
                    result.Severity = IncidentSeverity.Information;
                    result.Explanation = "Another legitimate obligation takes priority — the worker is engaged, not idle without cause.";
                    return result;
                case DestinationBlockerKind.DestinationMissing:
                case DestinationBlockerKind.DestinationInaccessible:
                case DestinationBlockerKind.OriginInvalid:
                    result.Severity = IncidentSeverity.Warning;
                    result.Explanation = kind == DestinationBlockerKind.DestinationMissing
                        ? "The physical destination is missing (not authored), so the task can never arrive — the worker is idle without a valid cause."
                        : kind == DestinationBlockerKind.DestinationInaccessible
                        ? "The physical destination exists but is inaccessible, so the task can never arrive — the worker is idle without a valid cause."
                        : "The actor's current position is outside the authored walkable floor area (origin-invalid, M-06), so no route can start — the worker is idle without a valid cause.";
                    return result;
                case DestinationBlockerKind.WorkerCannotPerform:
                    result.Severity = IncidentSeverity.Warning;
                    result.Explanation = "No worker can perform the task (" + (evidence.RefusalReason ?? evidence.RecoveryState ?? string.Empty) + ").";
                    return result;
                default:
                    break;
            }

            if (evidence.WorkerBusyOnOtherTask)
            {
                result.Severity = IncidentSeverity.Information;
                result.Explanation = "No destination blocker is evidenced and the worker is busy on other work — not idle without cause.";
                return result;
            }

            if (result.StalledSeconds >= 0 && evidence.WorkedMinutes == 0)
            {
                result.Severity = IncidentSeverity.Warning;
                result.Explanation = "No progress for " + (result.StalledSeconds / 3600d).ToString("0.0")
                    + " simulated hours with no work recorded and no valid cause evidenced — idle without cause.";
                result.MissingEvidence.Add("a named cause for the stall (destination state, reservation, or competing obligation)");
                return result;
            }

            result.Severity = IncidentSeverity.Unknown;
            result.Explanation = "A stall is evidenced but no circumstance distinguishes a legitimate wait from idle without cause.";
            result.MissingEvidence.Add("destination state, reservation state, or the worker's competing obligation");
            return result;
        }

        /// <summary>
        /// Full investigation of one stalled task from its observed events.
        /// Names the responsible authority from the definition / recovery
        /// owner, builds the causal timeline, applies the circumstance test,
        /// and emits the finding to the Lane 1 core when one is attached.
        /// </summary>
        public static IncidentFinding InvestigateTaskStall(
            IReadOnlyList<IncidentEvidenceEvent> events, double nowSeconds)
        {
            var finding = new IncidentFinding { IncidentId = "INV-TASK-UNKNOWN", Title = "Stalled task investigation" };
            if (events == null || events.Count == 0)
            {
                finding.Severity = IncidentSeverity.Unknown;
                finding.Explanation = "No trace events were supplied for this task.";
                finding.MissingEvidence.Add("trace events for the task (bounded pre-incident capture)");
                finding.SourceNote = "No evidence supplied.";
                return finding;
            }

            CausalTimelineEntry earliestBlocker;
            finding.Timeline = BuildCausalTimeline(events, out earliestBlocker);
            IncidentEvidenceEvent latest = events[events.Count - 1];
            IncidentEvidenceEvent first = events[0];
            foreach (IncidentEvidenceEvent item in events)
            {
                if (item.SimulatedSeconds > latest.SimulatedSeconds) latest = item;
                if (item.SimulatedSeconds >= 0 && (first.SimulatedSeconds < 0 || item.SimulatedSeconds < first.SimulatedSeconds)) first = item;
            }

            string taskId = latest.TaskId ?? string.Empty;
            string definitionId = latest.TaskDefinitionId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(definitionId)) definitionId = first.TaskDefinitionId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(taskId)) taskId = first.TaskId ?? string.Empty;
            finding.IncidentId = "INV-" + (string.IsNullOrWhiteSpace(definitionId) ? "TASK" : definitionId) + "-" + taskId;
            finding.Title = "Stalled task " + taskId + " (" + definitionId + ")";
            finding.TaskId = taskId;
            finding.PersonNumericId = latest.PersonNumericId;
            finding.HouseholdId = ResolveHouseholdId(latest.HouseholdId, latest.TaskOwnerId);

            string recoveryOwner = string.Empty;
            string refusalReason = string.Empty;
            foreach (IncidentEvidenceEvent item in events)
            {
                if (!string.IsNullOrWhiteSpace(item.RecoveryState)) recoveryOwner = item.RecoveryState;
                if (!string.IsNullOrWhiteSpace(item.Reason)) refusalReason = item.Reason;
            }

            finding.ResponsibleAuthority = ResolveAuthority(definitionId, recoveryOwner, out string sourcePath, out int sourceLine);
            finding.AuthoritySourcePath = sourcePath;
            finding.AuthoritySourceLine = sourceLine;
            finding.BlockerKind = earliestBlocker != null ? earliestBlocker.BlockerKind : DestinationBlockerKind.None;
            if (earliestBlocker != null)
            {
                finding.EarliestBlocker = earliestBlocker.Label + ": " + earliestBlocker.Detail;
                finding.EarliestBlockerSeconds = earliestBlocker.SimulatedSeconds;
            }

            int householdId = ResolveHouseholdId(latest.HouseholdId, latest.TaskOwnerId);
            var stall = new StallEvidence
            {
                TaskId = taskId,
                TaskDefinitionId = definitionId,
                TaskStatus = latest.TaskStatus ?? string.Empty,
                TaskOwnerId = latest.TaskOwnerId ?? string.Empty,
                PersonId = latest.PersonId ?? string.Empty,
                HouseholdId = householdId,
                WorkedMinutes = latest.WorkedMinutes >= 0 ? latest.WorkedMinutes : 0,
                RemainingMinutes = latest.RemainingMinutes,
                InterruptionCount = latest.InterruptionCount,
                LastTransition = latest.Transition ?? string.Empty,
                FirstObservedSeconds = first.SimulatedSeconds,
                LastProgressSeconds = LatestProgressSeconds(events),
                NowSeconds = nowSeconds,
                RecoveryState = recoveryOwner,
                RefusalReason = refusalReason,
                ReservationId = latest.ReservationId ?? string.Empty,
                SkillMentionedAsOnlyCause = SkillOnlyCause(refusalReason, recoveryOwner),
            };
            StallClassification classification = ClassifyStall(stall);
            finding.Severity = classification.Severity;
            if (classification.BlockerKind != DestinationBlockerKind.None) finding.BlockerKind = classification.BlockerKind;
            finding.Explanation = classification.Explanation;
            finding.MissingEvidence = classification.MissingEvidence;
            finding.EvidenceComplete = finding.MissingEvidence.Count == 0;
            finding.SourceNote = "Trace evidence for task " + taskId + "; household resolved to H"
                + (householdId >= 0 ? householdId.ToString() : "?")
                + " (owner-id fallback where the anomaly report loses it — C R-18).";
            EmitToCore(finding);
            return finding;
        }

        /// <summary>
        /// Proving case 1 (C R-18 SOLVED): the interrupted
        /// domestic.fuel-handling stall. The canned evidence replicates the
        /// retained-run record — T9/T10/T170 (60-day) and T8/T9/T11 (5-day):
        /// interrupted fuel-handling tasks, never re-queued, worked 0,
        /// remaining 41, no assignee, no reservation, no work location,
        /// caused by the missing Firewood activity point.
        /// </summary>
        public static List<IncidentEvidenceEvent> KnownFuelHandlingEvidence()
        {
            const double hour = 3600d;
            return new List<IncidentEvidenceEvent>
            {
                new IncidentEvidenceEvent
                {
                    SimulatedSeconds = 0, DayIndex = 0, EventType = "task-transition", Transition = "CreateTask",
                    TaskId = "T9", TaskDefinitionId = "domestic.fuel-handling", TaskStatus = "Queued",
                    TaskOwnerId = "H7", PersonNumericId = -1, HouseholdId = -1,
                    WorkedMinutes = 0, RemainingMinutes = 41, PlannedMinutes = 41, InterruptionCount = 0,
                    Detail = "Fetch and handle household fuel",
                },
                new IncidentEvidenceEvent
                {
                    SimulatedSeconds = 2 * hour, DayIndex = 0, EventType = "task-transition", Transition = "InterruptTask",
                    TaskId = "T9", TaskDefinitionId = "domestic.fuel-handling", TaskStatus = "Interrupted",
                    TaskOwnerId = "H7", PersonNumericId = -1, HouseholdId = -1,
                    WorkedMinutes = 0, RemainingMinutes = 41, PlannedMinutes = 41, InterruptionCount = 1,
                    Reason = "firewood: no firewood storage is authored on the home.",
                    RecoveryState = "owner=DomesticLaborDriver; state=WAITING_FOR_CAPABILITY_CHANGE; "
                        + "retryAfterDay=2147483647; activityLocation=firewood: no firewood storage is authored on the home.; workerReservation=none",
                },
                new IncidentEvidenceEvent
                {
                    SimulatedSeconds = 85.5 * hour, DayIndex = 3, EventType = "anomaly", Transition = "task-no-progress-with-recovery",
                    TaskId = "T9", TaskDefinitionId = "domestic.fuel-handling", TaskStatus = "Queued",
                    TaskOwnerId = "H7", PersonNumericId = -1, HouseholdId = -1,
                    WorkedMinutes = 0, RemainingMinutes = 41, PlannedMinutes = 41, InterruptionCount = 1,
                    Reason = "Open Queued task has had no recorded lifecycle/work progress for 85.5 simulated hours. "
                        + "Owning system recovery: owner=DomesticLaborDriver; state=WAITING_FOR_CAPABILITY_CHANGE",
                    RecoveryState = "owner=DomesticLaborDriver; state=WAITING_FOR_CAPABILITY_CHANGE; "
                        + "retryAfterDay=2147483647; activityLocation=firewood: no firewood storage is authored on the home.; workerReservation=none",
                },
            };
        }

        /// <summary>Proving case 1 run through the full investigator.</summary>
        public static IncidentFinding InvestigateKnownFuelHandlingStall()
        {
            List<IncidentEvidenceEvent> events = KnownFuelHandlingEvidence();
            IncidentFinding finding = InvestigateTaskStall(events, 85.5 * 3600d);
            finding.SourceNote = "Proving case 1 (C R-18 SOLVED): recorded evidence from the retained "
                + "60-day run (T9/T10/T170) and 5-day run (T8/T9/T11) — interrupted domestic.fuel-handling "
                + "tasks, never re-queued. No live run exists on this HEAD; this is the retained-run record, not a fresh measurement.";
            return finding;
        }

        /// <summary>
        /// Proving case 2 (E-06 SOLVED): the 24-loaf wholesale obligation,
        /// authored in DevelopmentTownConfiguration.asset (bakery block
        /// wholesaleAgreements) — not created by a driver.
        /// </summary>
        public static WholesaleObligationEvidence KnownWholesaleObligationEvidence()
        {
            return new WholesaleObligationEvidence
            {
                BuyerBusinessKey = "general-store",
                ProductId = "bake.bread-loaf",
                UnitsPerDelivery = 24,
                DeliveryCadenceDays = 1,
                PricePerUnitCents = 4,
                FirstDeliveryDayIndex = 0,
                ConfigSource = "Assets/Core/Population/DevelopmentTownConfiguration.asset (bakery business block, wholesaleAgreements)",
                AppliedBy = "FirstLedgerSliceBootstrapper.SeedBakeryWholesaleAccounts -> ScenarioOpeningWholesaleAccounts.TryApply -> BakeryShopRuntime.AddWholesaleAccount",
                BlockingGate = "SimulationDrivers.ShouldBeginStagedBakeryBatch: no batch begins while any wholesale account is active",
            };
        }

        /// <summary>Proving case 2 run through the investigator.</summary>
        public static IncidentFinding InvestigateWholesaleObligation(WholesaleObligationEvidence evidence)
        {
            var finding = new IncidentFinding
            {
                IncidentId = "INV-WHOLESALE-BAKERY-GENERAL-STORE",
                Title = "24-loaf/day wholesale obligation starves the bakery retail counter",
                Severity = IncidentSeverity.Warning,
                BlockerKind = DestinationBlockerKind.DestinationMissing,
            };
            if (evidence == null)
            {
                finding.Severity = IncidentSeverity.Unknown;
                finding.Explanation = "No wholesale obligation evidence was supplied.";
                finding.MissingEvidence.Add("wholesale agreement (buyer, product, units, price, source)");
                return finding;
            }

            finding.ResponsibleAuthority = "DevelopmentTownConfiguration.asset wholesaleAgreements (bakery block), applied by "
                + evidence.AppliedBy;
            finding.AuthoritySourcePath = "Assets/Core/Population/DevelopmentTownConfiguration.asset";
            finding.AuthoritySourceLine = -1;
            finding.EarliestBlocker = "Config entry: " + evidence.UnitsPerDelivery + "x " + evidence.ProductId
                + " daily @ " + evidence.PricePerUnitCents + "c to " + evidence.BuyerBusinessKey
                + ", first delivery day " + evidence.FirstDeliveryDayIndex
                + " — the obligation exists from day 0, before any sale is made.";
            finding.EarliestBlockerSeconds = 0;
            finding.Timeline = new List<CausalTimelineEntry>
            {
                new CausalTimelineEntry
                {
                    SimulatedSeconds = 0, DayIndex = 0, IsBlocker = true,
                    BlockerKind = DestinationBlockerKind.DestinationMissing,
                    Label = "config/wholesaleAgreements",
                    Detail = finding.EarliestBlocker,
                },
                new CausalTimelineEntry
                {
                    SimulatedSeconds = 0, DayIndex = 0, IsBlocker = true,
                    BlockerKind = DestinationBlockerKind.DestinationMissing,
                    Label = "gate/ShouldBeginStagedBakeryBatch",
                    Detail = evidence.BlockingGate
                        + " — while the General Store wholesale account is active, the staged chain never begins a batch.",
                },
            };
            finding.Explanation = "The 24-loaf/day @ 4c obligation is authored in config ("
                + evidence.ConfigSource + ") and applied at bootstrap by " + evidence.AppliedBy
                + ". Standing wholesale accounts drive exactly 1 batch/day (24 loaves) against ~42/day demand "
                + "with capacity 4 batches/day 75% idle, because " + evidence.BlockingGate
                + ". The blocker is the authored obligation plus the gate, not a missing worker or a skill gap.";
            finding.EvidenceComplete = true;
            finding.SourceNote = "Proving case 2 (E-06 SOLVED): config + bootstrap source at this HEAD; "
                + "the 1 batch/day vs ~42/day demand figures are the recorded 60-day run behaviour, not a fresh measurement. "
                + "No live run exists on this HEAD.";
            EmitToCore(finding);
            return finding;
        }

        private static double LatestProgressSeconds(IReadOnlyList<IncidentEvidenceEvent> events)
        {
            double latest = -1;
            foreach (IncidentEvidenceEvent item in events)
            {
                if (item == null) continue;
                bool progress = !string.Equals(item.EventType, "anomaly", StringComparison.OrdinalIgnoreCase);
                if (progress && item.SimulatedSeconds > latest) latest = item.SimulatedSeconds;
            }
            return latest;
        }

        private static bool SkillOnlyCause(string refusalReason, string recoveryState)
        {
            string combined = ((refusalReason ?? string.Empty) + " " + (recoveryState ?? string.Empty)).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(combined)) return false;
            bool mentionsSkill = combined.Contains("skill") || combined.Contains("capability") && combined.Contains("level");
            if (!mentionsSkill) return false;
            // A skill mention only disqualifies the explanation when no
            // physical destination evidence accompanies it.
            return ClassifyRefusalReason(refusalReason + " " + recoveryState) == DestinationBlockerKind.None
                && !combined.Contains("firewood") && !combined.Contains("authored")
                && !combined.Contains("occupied") && !combined.Contains("walkable");
        }

        private static string ResolveAuthority(string definitionId, string recoveryState, out string sourcePath, out int sourceLine)
        {
            sourcePath = string.Empty;
            sourceLine = -1;
            if (!string.IsNullOrWhiteSpace(recoveryState) && recoveryState.Contains("DomesticLaborDriver"))
            {
                sourcePath = "Assets/Scripts/Domains/Population/DomesticLaborDriver.cs";
                sourceLine = 1191;
                return "DomesticLaborDriver (domestic work authority) — ResolveDomesticActivityMovement requires a "
                    + "BuildingActivityLocationType.Firewood point; the Natural Wood house prefab authors bed, stove, "
                    + "cleaning, two storage and porch-dropoff points, but no Firewood point (checklist CI-5F80FDC40F).";
            }
            if (!string.IsNullOrWhiteSpace(definitionId) && definitionId.StartsWith("domestic.", StringComparison.Ordinal))
            {
                sourcePath = "Assets/Scripts/Domains/Population/DomesticLaborDriver.cs";
                return "DomesticLaborDriver (domestic work authority)";
            }
            if (!string.IsNullOrWhiteSpace(definitionId) && definitionId.StartsWith("shopping", StringComparison.OrdinalIgnoreCase))
            {
                sourcePath = "Assets/Scripts/Domains/Population/ShoppingTripDriver.cs";
                return "ShoppingTripDriver / HouseholdShoppingLoop (household shopping authority)";
            }
            if (!string.IsNullOrWhiteSpace(definitionId) && definitionId.IndexOf("bakery", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                sourcePath = "Assets/Scripts/Domains/Economy/Businesses/Bakery/BakeryShopRuntime.cs";
                return "BakeryShopRuntime (bakery production authority)";
            }
            if (!string.IsNullOrWhiteSpace(recoveryState))
            {
                return "Owning system named in the recovery state: " + recoveryState;
            }
            return "TaskAuthority (owning system not named by the evidence — see missing evidence)";
        }
    }
}
