using System;
using System.Collections.Generic;

namespace LandLedgers.Diagnostics
{
    /// <summary>
    /// Lane 1 (§4/§17) — the lifecycle of one incident, mirroring the
    /// household supply-need cadence that already works in source
    /// (ObserveHouseholdSupplyIncident): OPEN once, CAUSE-CHANGED only when
    /// the cause changes, ESCALATED on a standing condition, RESOLVED when
    /// the need closes — plus CLOSED, which the household system lacks
    /// (E W-5: no [Incident][CLOSED] emitter exists; 155 incidents opened,
    /// 0 closed in the Nov 17 60-day run).
    /// </summary>
    public enum IncidentStatus
    {
        /// <summary>The incident is open: the condition was observed and has not resolved.</summary>
        Open = 0,

        /// <summary>The cause changed while the incident stayed open (a new cause-changed line is emitted).</summary>
        CauseChanged = 1,

        /// <summary>The same legitimate condition has stood past its escalation horizon.</summary>
        Escalated = 2,

        /// <summary>The underlying condition no longer holds (need closed, batch unblocked, route reachable).</summary>
        Resolved = 3,

        /// <summary>The incident record is closed with a disposition (fixed, explained-legitimate, blocked-with-reason).</summary>
        Closed = 4,
    }

    /// <summary>One entry in an incident's status history: the transition, when it happened, and why.</summary>
    public sealed class IncidentStatusChange
    {
        public IncidentStatus FromStatus;
        public IncidentStatus ToStatus;
        public int DayIndex;
        public double SimSeconds;
        public string Note;

        public IncidentStatusChange(IncidentStatus fromStatus, IncidentStatus toStatus, int dayIndex, double simSeconds, string note)
        {
            FromStatus = fromStatus;
            ToStatus = toStatus;
            DayIndex = dayIndex;
            SimSeconds = simSeconds;
            Note = note ?? string.Empty;
        }
    }

    /// <summary>
    /// The input DTO for reporting an occurrence to
    /// <see cref="IncidentWarningAuthority"/>. Plain public fields, like
    /// NpcActivityTraceEvent: the caller fills what it knows and leaves the
    /// rest empty — an unknown correlation id is empty, never invented.
    /// The authority fills in the stable id, timestamps, occurrence counts
    /// and status history.
    /// </summary>
    public sealed class IncidentReport
    {
        /// <summary>Stable signature id (a CI-* checklist id where one exists, else any stable key).</summary>
        public string SignatureId;

        /// <summary>Severity of this occurrence (circumstances, not frequency).</summary>
        public IncidentSeverity Severity;

        /// <summary>One-line human-readable summary of the condition.</summary>
        public string Summary;

        /// <summary>Simulated day index of the occurrence (-1 when unknown; day-keyed dedup then falls back to session-once).</summary>
        public int DayIndex;

        /// <summary>Absolute simulated seconds of the occurrence (-1 when unknown).</summary>
        public double SimSeconds;

        /// <summary>Wall-clock time of the occurrence, when the caller holds it; null = NOT_CAPTURED. The core never reads the live clock itself.</summary>
        public DateTime? WallClockUtc;

        /// <summary>Owning system (the subsystem that owns the question, e.g. "Freight", "Bakery", "Pathing").</summary>
        public string OwningSystem;

        /// <summary>Recovery authority: who/what can resolve this (a system, a driver, an owner decision).</summary>
        public string RecoveryAuthority;

        /// <summary>Wait state in words (e.g. "Queued 72h, no assignee").</summary>
        public string WaitState;

        /// <summary>Retry boundary in words (e.g. "attempt 1 of 2; no further retries").</summary>
        public string RetryBoundary;

        /// <summary>Causal class of this occurrence (M-R14). Unclassified when the caller does not know.</summary>
        public RefusalCauseClass CauseClass;

        /// <summary>Evidence for the causal class (the refusal reason verbatim, or the observed blocker).</summary>
        public string CauseEvidence;

        /// <summary>Correlation ids — fill what the occurrence knows, leave the rest empty.</summary>
        public string TaskId;
        public string PersonId;
        public string ResidentId;
        public string HouseholdId;
        public string BusinessId;
        public string BuildingId;
        public string OrderId;
        public string ShipmentId;
        public string TransactionId;

        /// <summary>One physical-capability blocker named by this occurrence (appended once to the incident's list).</summary>
        public string PhysicalCapabilityBlocker;

        /// <summary>One evidence reference (file:line, run id, checklist id — appended once to the incident's list).</summary>
        public string EvidenceReference;

        /// <summary>One related incident's signature id (appended once to the incident's list).</summary>
        public string RelatedIncidentId;

        public IncidentReport()
        {
            SignatureId = string.Empty;
            Severity = IncidentSeverity.Unknown;
            Summary = string.Empty;
            DayIndex = -1;
            SimSeconds = -1;
            WallClockUtc = null;
            OwningSystem = string.Empty;
            RecoveryAuthority = string.Empty;
            WaitState = string.Empty;
            RetryBoundary = string.Empty;
            CauseClass = RefusalCauseClass.Unclassified;
            CauseEvidence = string.Empty;
            TaskId = string.Empty;
            PersonId = string.Empty;
            ResidentId = string.Empty;
            HouseholdId = string.Empty;
            BusinessId = string.Empty;
            BuildingId = string.Empty;
            OrderId = string.Empty;
            ShipmentId = string.Empty;
            TransactionId = string.Empty;
            PhysicalCapabilityBlocker = string.Empty;
            EvidenceReference = string.Empty;
            RelatedIncidentId = string.Empty;
        }
    }

    /// <summary>What <see cref="SimulationIncident.RecordOccurrence"/> observed about one occurrence.</summary>
    public struct IncidentOccurrenceOutcome
    {
        /// <summary>True when this is the incident's first occurrence ever.</summary>
        public bool IsFirstOccurrence;

        /// <summary>True when the causal class differs from the class on the previous occurrence.</summary>
        public bool CauseChanged;

        /// <summary>True when this occurrence's severity is higher than any severity seen before.</summary>
        public bool SeverityUpgraded;

        /// <summary>The severity before this occurrence (for the escalation line).</summary>
        public IncidentSeverity PreviousSeverity;

        /// <summary>The causal class before this occurrence (for the cause-changed line).</summary>
        public RefusalCauseClass PreviousCauseClass;
    }

    /// <summary>
    /// Lane 1 (§4/§17) — one incident: the shared model every other lane
    /// builds on. One record per stable signature id (the signature IS the
    /// identity, like the household incident id "H{household}/need/{item}"):
    /// occurrences accumulate on it across days, with per-day counts, first-
    /// and last-seen correlation ids, a status history, and a severity that
    /// only ever escalates upward.
    ///
    /// The model OBSERVES authoritative state — it never becomes a parallel
    /// source of truth. It holds no ledger, no obligation, no inventory, no
    /// custody, and it never calls back into any gameplay authority. It is
    /// plain C# with no Unity dependency.
    /// </summary>
    public sealed class SimulationIncident
    {
        /// <summary>Stable id of this incident — the signature id itself.</summary>
        public string Id { get; private set; }

        /// <summary>Stable signature id (a CI-* checklist id where one exists).</summary>
        public string SignatureId { get; private set; }

        /// <summary>One-line summary from the first occurrence (updated only when empty).</summary>
        public string Summary { get; set; }

        /// <summary>Current status (Open at creation).</summary>
        public IncidentStatus Status { get; private set; }

        /// <summary>Highest severity observed (only ever escalates upward).</summary>
        public IncidentSeverity Severity { get; private set; }

        /// <summary>Severity of the first occurrence.</summary>
        public IncidentSeverity FirstSeverity { get; private set; }

        /// <summary>Owning system (the subsystem that owns the question).</summary>
        public string OwningSystem { get; set; }

        /// <summary>Recovery authority: who/what can resolve this.</summary>
        public string RecoveryAuthority { get; set; }

        /// <summary>Wait state in words, latest observed.</summary>
        public string WaitState { get; set; }

        /// <summary>Retry boundary in words, latest observed.</summary>
        public string RetryBoundary { get; set; }

        /// <summary>Causal class of the latest occurrence (M-R14).</summary>
        public RefusalCauseClass CauseClass { get; private set; }

        /// <summary>Evidence for the latest causal class.</summary>
        public string CauseEvidence { get; private set; }

        /// <summary>Simulated day index of the first occurrence (-1 when unknown).</summary>
        public int FirstSeenDay { get; private set; }

        /// <summary>Simulated day index of the latest occurrence (-1 when unknown).</summary>
        public int LastSeenDay { get; private set; }

        /// <summary>Absolute simulated seconds of the first occurrence (-1 when unknown).</summary>
        public double FirstSeenSimSeconds { get; private set; }

        /// <summary>Absolute simulated seconds of the latest occurrence (-1 when unknown).</summary>
        public double LastSeenSimSeconds { get; private set; }

        /// <summary>Wall-clock time of the first occurrence; null = NOT_CAPTURED.</summary>
        public DateTime? FirstSeenWallClockUtc { get; private set; }

        /// <summary>Wall-clock time of the latest occurrence; null = NOT_CAPTURED.</summary>
        public DateTime? LastSeenWallClockUtc { get; private set; }

        /// <summary>Total occurrences across all days.</summary>
        public int OccurrenceCount { get; private set; }

        /// <summary>Day index of the last emitted warning line for this incident (-1 = never emitted).</summary>
        public int LastEmittedDay { get; set; }

        // Correlation ids: first-seen and last-seen. Empty = unknown, never invented.
        public string FirstTaskId { get; private set; }
        public string LastTaskId { get; private set; }
        public string FirstPersonId { get; private set; }
        public string LastPersonId { get; private set; }
        public string FirstResidentId { get; private set; }
        public string LastResidentId { get; private set; }
        public string FirstHouseholdId { get; private set; }
        public string LastHouseholdId { get; private set; }
        public string FirstBusinessId { get; private set; }
        public string LastBusinessId { get; private set; }
        public string FirstBuildingId { get; private set; }
        public string LastBuildingId { get; private set; }
        public string FirstOrderId { get; private set; }
        public string LastOrderId { get; private set; }
        public string FirstShipmentId { get; private set; }
        public string LastShipmentId { get; private set; }
        public string FirstTransactionId { get; private set; }
        public string LastTransactionId { get; private set; }

        /// <summary>Physical-capability blockers named by occurrences (each listed once, in first-seen order).</summary>
        public List<string> PhysicalCapabilityBlockers { get; private set; }

        /// <summary>Evidence references (file:line, run id, checklist id — each listed once).</summary>
        public List<string> EvidenceReferences { get; private set; }

        /// <summary>Signature ids of related incidents (each listed once).</summary>
        public List<string> RelatedIncidentIds { get; private set; }

        /// <summary>Status history, oldest first. The first entry is the opening (Open, from Open).</summary>
        public List<IncidentStatusChange> StatusHistory { get; private set; }

        private readonly Dictionary<int, int> occurrencesByDay = new Dictionary<int, int>();

        public SimulationIncident(string signatureId, IncidentReport firstReport)
        {
            if (string.IsNullOrWhiteSpace(signatureId))
            {
                throw new ArgumentException("A stable signature id is required.", nameof(signatureId));
            }

            if (firstReport == null)
            {
                throw new ArgumentNullException(nameof(firstReport));
            }

            Id = signatureId;
            SignatureId = signatureId;
            Summary = firstReport.Summary ?? string.Empty;
            Status = IncidentStatus.Open;
            Severity = firstReport.Severity;
            FirstSeverity = firstReport.Severity;
            OwningSystem = firstReport.OwningSystem ?? string.Empty;
            RecoveryAuthority = firstReport.RecoveryAuthority ?? string.Empty;
            WaitState = firstReport.WaitState ?? string.Empty;
            RetryBoundary = firstReport.RetryBoundary ?? string.Empty;
            CauseClass = firstReport.CauseClass;
            CauseEvidence = firstReport.CauseEvidence ?? string.Empty;
            FirstSeenDay = firstReport.DayIndex;
            LastSeenDay = firstReport.DayIndex;
            FirstSeenSimSeconds = firstReport.SimSeconds;
            LastSeenSimSeconds = firstReport.SimSeconds;
            FirstSeenWallClockUtc = firstReport.WallClockUtc;
            LastSeenWallClockUtc = firstReport.WallClockUtc;
            OccurrenceCount = 0;
            LastEmittedDay = -1;
            FirstTaskId = string.Empty;
            LastTaskId = string.Empty;
            FirstPersonId = string.Empty;
            LastPersonId = string.Empty;
            FirstResidentId = string.Empty;
            LastResidentId = string.Empty;
            FirstHouseholdId = string.Empty;
            LastHouseholdId = string.Empty;
            FirstBusinessId = string.Empty;
            LastBusinessId = string.Empty;
            FirstBuildingId = string.Empty;
            LastBuildingId = string.Empty;
            FirstOrderId = string.Empty;
            LastOrderId = string.Empty;
            FirstShipmentId = string.Empty;
            LastShipmentId = string.Empty;
            FirstTransactionId = string.Empty;
            LastTransactionId = string.Empty;
            PhysicalCapabilityBlockers = new List<string>();
            EvidenceReferences = new List<string>();
            RelatedIncidentIds = new List<string>();
            StatusHistory = new List<IncidentStatusChange>();
            StatusHistory.Add(new IncidentStatusChange(IncidentStatus.Open, IncidentStatus.Open,
                firstReport.DayIndex, firstReport.SimSeconds, "opened"));
            RecordOccurrence(firstReport);
        }

        /// <summary>
        /// Records one occurrence on this incident: occurrence counts (total
        /// and per-day), first-/last-seen correlation ids and timestamps,
        /// blocker/evidence/related merges, cause-class tracking, and
        /// severity escalation (upward only). Never invents an id: empty
        /// correlation ids leave the stored value untouched.
        /// </summary>
        public IncidentOccurrenceOutcome RecordOccurrence(IncidentReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            var outcome = new IncidentOccurrenceOutcome
            {
                IsFirstOccurrence = OccurrenceCount == 0,
                PreviousSeverity = Severity,
                PreviousCauseClass = CauseClass,
                CauseChanged = false,
                SeverityUpgraded = false,
            };

            OccurrenceCount++;
            if (report.DayIndex >= 0)
            {
                int count;
                occurrencesByDay.TryGetValue(report.DayIndex, out count);
                occurrencesByDay[report.DayIndex] = count + 1;
                LastSeenDay = report.DayIndex;
                if (FirstSeenDay < 0)
                {
                    FirstSeenDay = report.DayIndex;
                }
            }

            if (report.SimSeconds >= 0)
            {
                LastSeenSimSeconds = report.SimSeconds;
                if (FirstSeenSimSeconds < 0)
                {
                    FirstSeenSimSeconds = report.SimSeconds;
                }
            }

            if (report.WallClockUtc.HasValue)
            {
                LastSeenWallClockUtc = report.WallClockUtc;
                if (!FirstSeenWallClockUtc.HasValue)
                {
                    FirstSeenWallClockUtc = report.WallClockUtc;
                }
            }

            if (string.IsNullOrEmpty(Summary) && !string.IsNullOrEmpty(report.Summary))
            {
                Summary = report.Summary;
            }

            if (!string.IsNullOrEmpty(report.OwningSystem))
            {
                OwningSystem = report.OwningSystem;
            }

            if (!string.IsNullOrEmpty(report.RecoveryAuthority))
            {
                RecoveryAuthority = report.RecoveryAuthority;
            }

            if (!string.IsNullOrEmpty(report.WaitState))
            {
                WaitState = report.WaitState;
            }

            if (!string.IsNullOrEmpty(report.RetryBoundary))
            {
                RetryBoundary = report.RetryBoundary;
            }

            FirstTaskId = FirstOr(FirstTaskId, report.TaskId);
            LastTaskId = LastOr(LastTaskId, report.TaskId);
            FirstPersonId = FirstOr(FirstPersonId, report.PersonId);
            LastPersonId = LastOr(LastPersonId, report.PersonId);
            FirstResidentId = FirstOr(FirstResidentId, report.ResidentId);
            LastResidentId = LastOr(LastResidentId, report.ResidentId);
            FirstHouseholdId = FirstOr(FirstHouseholdId, report.HouseholdId);
            LastHouseholdId = LastOr(LastHouseholdId, report.HouseholdId);
            FirstBusinessId = FirstOr(FirstBusinessId, report.BusinessId);
            LastBusinessId = LastOr(LastBusinessId, report.BusinessId);
            FirstBuildingId = FirstOr(FirstBuildingId, report.BuildingId);
            LastBuildingId = LastOr(LastBuildingId, report.BuildingId);
            FirstOrderId = FirstOr(FirstOrderId, report.OrderId);
            LastOrderId = LastOr(LastOrderId, report.OrderId);
            FirstShipmentId = FirstOr(FirstShipmentId, report.ShipmentId);
            LastShipmentId = LastOr(LastShipmentId, report.ShipmentId);
            FirstTransactionId = FirstOr(FirstTransactionId, report.TransactionId);
            LastTransactionId = LastOr(LastTransactionId, report.TransactionId);

            AddOnce(PhysicalCapabilityBlockers, report.PhysicalCapabilityBlocker);
            AddOnce(EvidenceReferences, report.EvidenceReference);
            AddOnce(RelatedIncidentIds, report.RelatedIncidentId);

            if (report.CauseClass != RefusalCauseClass.Unclassified
                && report.CauseClass != CauseClass
                && OccurrenceCount > 1)
            {
                outcome.CauseChanged = true;
                CauseClass = report.CauseClass;
            }
            else if (OccurrenceCount == 1)
            {
                CauseClass = report.CauseClass;
            }

            if (!string.IsNullOrEmpty(report.CauseEvidence))
            {
                CauseEvidence = report.CauseEvidence;
            }

            if ((int)report.Severity > (int)Severity)
            {
                outcome.SeverityUpgraded = true;
                Severity = report.Severity;
            }

            return outcome;
        }

        /// <summary>Occurrences on one simulated day (0 when the incident was never observed that day).</summary>
        public int OccurrencesOnDay(int dayIndex)
        {
            int count;
            return occurrencesByDay.TryGetValue(dayIndex, out count) ? count : 0;
        }

        /// <summary>All days with at least one occurrence, ascending.</summary>
        public List<int> DaysObserved()
        {
            var days = new List<int>(occurrencesByDay.Keys);
            days.Sort();
            return days;
        }

        /// <summary>
        /// Moves the incident to a new status and records the transition in
        /// the status history. A move to the current status with no note is a
        /// no-op; with a note it records the note against the current status.
        /// </summary>
        public void ChangeStatus(IncidentStatus newStatus, int dayIndex, double simSeconds, string note)
        {
            if (newStatus == Status && string.IsNullOrEmpty(note))
            {
                return;
            }

            IncidentStatus from = Status;
            Status = newStatus;
            StatusHistory.Add(new IncidentStatusChange(from, newStatus, dayIndex, simSeconds, note));
        }

        /// <summary>Adds a related incident's signature id (once).</summary>
        public void AddRelatedIncident(string signatureId)
        {
            AddOnce(RelatedIncidentIds, signatureId);
        }

        /// <summary>Adds an evidence reference (once).</summary>
        public void AddEvidenceReference(string reference)
        {
            AddOnce(EvidenceReferences, reference);
        }

        /// <summary>Adds a physical-capability blocker (once).</summary>
        public void AddPhysicalCapabilityBlocker(string blocker)
        {
            AddOnce(PhysicalCapabilityBlockers, blocker);
        }

        /// <summary>First-seen merge: keeps the stored value once set; an empty value never overwrites.</summary>
        private static string FirstOr(string current, string value)
        {
            return string.IsNullOrEmpty(current) && !string.IsNullOrEmpty(value) ? value : current;
        }

        /// <summary>Last-seen merge: the latest non-empty value wins; an empty value leaves the stored value untouched.</summary>
        private static string LastOr(string current, string value)
        {
            return string.IsNullOrEmpty(value) ? current : value;
        }

        private static void AddOnce(List<string> list, string value)
        {
            if (string.IsNullOrEmpty(value) || list.Contains(value))
            {
                return;
            }

            list.Add(value);
        }
    }
}
