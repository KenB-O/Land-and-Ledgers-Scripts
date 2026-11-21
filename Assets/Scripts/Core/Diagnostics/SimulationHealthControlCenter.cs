// Lane 6 (Muse Pass, Nov 20 2026) — Simulation Health and Issue Control
// Center (Tool 12), non-Editor logic. The central window that surfaces
// everything the diagnostic core holds: retained incidents, occurrence
// counts, severities, dedup state, retention/eviction counts, daily
// summaries, export — with configurable thresholds and the issue register
// view. This file is plain C# with no Unity dependency so it can join the
// Roslyn harness closure with tests; the Editor window itself
// (SimulationHealthControlCenterWindow.cs) is a thin view over it.
//
// The Center OBSERVES authoritative state and never becomes a parallel
// source of truth: it reads the IncidentWarningAuthority's retained
// incidents, it never moves goods, money, food, transport or labour, and
// it never suppresses a warning — a warning-count decrease is shown per
// signature so a repair is provable and a suppression is visible (W R-13:
// report distinct signatures, occurrences per signature per day, and the
// §22 register OPEN/IMPLEMENTED/VERIFIED status side by side — never a
// single-number dashboard).
//
// Two things this lane records as UNREAD, not invented:
// - C R-14: the Decision-11 no-worker escalation thresholds (warn ~1d /
//   escalate 3d / exception 7d) were not located in source. The 7-day
//   escalation sweep here is the incident-level standing horizon
//   (mirroring the household 7-day cadence), not that ladder.
// - The §22 issue register itself is Lane 11 work. Until it lands, the
//   register view here is the interim per-signature status store
//   (IssueRegister below), persisted by the window; it does not claim to
//   be the §22 register.
using System;
using System.Collections.Generic;
using System.Text;

namespace LandLedgers.Diagnostics
{
    /// <summary>
    /// §22 register status for one signature, matching the checklist
    /// ledger statuses (CONSOLE_INVESTIGATION_CHECKLIST_2026-11-19.md:
    /// 287 OPEN / 58 IMPLEMENTED-UNVERIFIED / 6 EXPLAINED-LEGITIMATE /
    /// 2 BLOCKED WITH REASON / 1 INVESTIGATING). A signature with no
    /// entry is shown as Open (unregistered), never as Verified.
    /// </summary>
    public enum IssueRegisterStatus
    {
        /// <summary>The issue is open: not yet fixed, explained, or blocked.</summary>
        Open = 0,

        /// <summary>Under investigation; the cause is not yet established.</summary>
        Investigating = 1,

        /// <summary>A fix is implemented in source but not yet verified by a run.</summary>
        ImplementedUnverified = 2,

        /// <summary>Verified fixed (a run evidences the fix).</summary>
        Verified = 3,

        /// <summary>Explained as legitimate behaviour, not a defect.</summary>
        ExplainedLegitimate = 4,

        /// <summary>Blocked with a recorded reason (owner decision owed, missing authority, ...).</summary>
        BlockedWithReason = 5,
    }

    /// <summary>Stable labels for <see cref="IssueRegisterStatus"/> (register view, export).</summary>
    public static class IssueRegisterStatusText
    {
        /// <summary>The canonical label for a register status.</summary>
        public static string Label(IssueRegisterStatus status)
        {
            switch (status)
            {
                case IssueRegisterStatus.Investigating: return "INVESTIGATING";
                case IssueRegisterStatus.ImplementedUnverified: return "IMPLEMENTED-UNVERIFIED";
                case IssueRegisterStatus.Verified: return "VERIFIED";
                case IssueRegisterStatus.ExplainedLegitimate: return "EXPLAINED-LEGITIMATE";
                case IssueRegisterStatus.BlockedWithReason: return "BLOCKED WITH REASON";
                default: return "OPEN";
            }
        }

        /// <summary>Parses a label back to a status; Open when unrecognised (never throws).</summary>
        public static IssueRegisterStatus Parse(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                return IssueRegisterStatus.Open;
            }

            string trimmed = label.Trim();
            if (string.Equals(trimmed, "INVESTIGATING", StringComparison.OrdinalIgnoreCase))
            {
                return IssueRegisterStatus.Investigating;
            }

            if (string.Equals(trimmed, "IMPLEMENTED-UNVERIFIED", StringComparison.OrdinalIgnoreCase))
            {
                return IssueRegisterStatus.ImplementedUnverified;
            }

            if (string.Equals(trimmed, "VERIFIED", StringComparison.OrdinalIgnoreCase))
            {
                return IssueRegisterStatus.Verified;
            }

            if (string.Equals(trimmed, "EXPLAINED-LEGITIMATE", StringComparison.OrdinalIgnoreCase))
            {
                return IssueRegisterStatus.ExplainedLegitimate;
            }

            if (string.Equals(trimmed, "BLOCKED WITH REASON", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "BLOCKED-WITH-REASON", StringComparison.OrdinalIgnoreCase))
            {
                return IssueRegisterStatus.BlockedWithReason;
            }

            return IssueRegisterStatus.Open;
        }
    }

    /// <summary>One §22-style register entry: a signature's status, owning authority, next action, and evidence reference.</summary>
    public sealed class IssueRegisterEntry
    {
        public string SignatureId;
        public IssueRegisterStatus Status;
        public string OwningAuthority;
        public string NextAction;
        public string EvidenceReference;

        public IssueRegisterEntry(string signatureId)
        {
            SignatureId = signatureId ?? string.Empty;
            Status = IssueRegisterStatus.Open;
            OwningAuthority = string.Empty;
            NextAction = string.Empty;
            EvidenceReference = string.Empty;
        }
    }

    /// <summary>
    /// The interim issue register (Lane 6): one entry per signature, in
    /// first-set order. This is the register VIEW's store until Lane 11
    /// builds the §22 register; it holds status/owner/next-action only —
    /// no gameplay state, no second ledger.
    /// </summary>
    public sealed class IssueRegister
    {
        private readonly Dictionary<string, IssueRegisterEntry> entries =
            new Dictionary<string, IssueRegisterEntry>(StringComparer.Ordinal);
        private readonly List<string> entryOrder = new List<string>();

        /// <summary>Entries, in first-set order.</summary>
        public IReadOnlyList<IssueRegisterEntry> Entries
        {
            get
            {
                var list = new List<IssueRegisterEntry>(entryOrder.Count);
                foreach (string signatureId in entryOrder)
                {
                    IssueRegisterEntry entry;
                    if (entries.TryGetValue(signatureId, out entry))
                    {
                        list.Add(entry);
                    }
                }

                return list;
            }
        }

        /// <summary>Sets (or creates) the entry for a signature. Returns the entry.</summary>
        public IssueRegisterEntry SetStatus(string signatureId, IssueRegisterStatus status,
            string owningAuthority, string nextAction, string evidenceReference)
        {
            if (string.IsNullOrWhiteSpace(signatureId))
            {
                throw new ArgumentException("A stable signature id is required.", nameof(signatureId));
            }

            string normalised = IncidentSignature.Normalize(signatureId) ?? signatureId.Trim();
            IssueRegisterEntry entry;
            if (!entries.TryGetValue(normalised, out entry))
            {
                entry = new IssueRegisterEntry(normalised);
                entries.Add(normalised, entry);
                entryOrder.Add(normalised);
            }

            entry.Status = status;
            entry.OwningAuthority = owningAuthority ?? string.Empty;
            entry.NextAction = nextAction ?? string.Empty;
            entry.EvidenceReference = evidenceReference ?? string.Empty;
            return entry;
        }

        /// <summary>Finds the entry for a signature (normalised first). Null when the signature has no entry — the Center shows it as Open (unregistered).</summary>
        public IssueRegisterEntry TryGetEntry(string signatureId)
        {
            if (string.IsNullOrWhiteSpace(signatureId))
            {
                return null;
            }

            string normalised = IncidentSignature.Normalize(signatureId);
            if (normalised == null)
            {
                return null;
            }

            IssueRegisterEntry entry;
            return entries.TryGetValue(normalised, out entry) ? entry : null;
        }

        /// <summary>Register status for a signature; Open when it has no entry.</summary>
        public IssueRegisterStatus StatusOf(string signatureId)
        {
            IssueRegisterEntry entry = TryGetEntry(signatureId);
            return entry != null ? entry.Status : IssueRegisterStatus.Open;
        }
    }

    /// <summary>
    /// The verified checklist baseline the register view measures against
    /// (G R1 CONFIRMED from the parent clone: the checklist ledger section
    /// has exactly 354 rows, 354 distinct CI ids, occurrence sum 645).
    /// Append-only history: run-local occurrences must not overwrite these
    /// numbers — a signature absent from a new run is not a fix unless the
    /// fix is evidenced (W R-14).
    /// </summary>
    public static class ChecklistBaseline
    {
        public const int Signatures = 354;
        public const int Occurrences = 645;
        public const int Open = 287;
        public const int ImplementedUnverified = 58;
        public const int ExplainedLegitimate = 6;
        public const int BlockedWithReason = 2;
        public const int Investigating = 1;
        public const string SourcePath = "Reports/Diagnostics/CONSOLE_INVESTIGATION_CHECKLIST_2026-11-19.md";
    }

    /// <summary>
    /// Trace completeness for the investigation window (G F-G3, refuted
    /// as "not surfaced" — the monitor DOES surface drop counters; the
    /// Center aggregates them). Passed in by the window from the live
    /// trace recorder when one is connected; HasTrace false means no
    /// trace session exists and the counts are unknown, never zero.
    /// </summary>
    public struct TraceCompleteness
    {
        /// <summary>True when a trace session is connected; false = unknown, not zero.</summary>
        public bool HasTrace;

        /// <summary>Session events currently retained by the recorder.</summary>
        public int RetainedSessionEvents;

        /// <summary>Session events dropped by the recorder's retention bound.</summary>
        public int DroppedSessionEvents;

        /// <summary>Prehistory events dropped by the recorder's retention bound.</summary>
        public int DroppedHistoryEvents;

        /// <summary>Anomalies currently retained by the recorder.</summary>
        public int AnomalyCount;
    }

    /// <summary>One incident as the Center displays it: a snapshot of the retained incident plus its register status.</summary>
    public sealed class HealthIncidentRow
    {
        public string SignatureId;
        public IncidentSeverity Severity;
        public IncidentStatus Status;
        public string OwningSystem;
        public string RecoveryAuthority;
        public string Summary;
        public int OccurrenceCount;
        public int OccurrencesToday;
        public int FirstSeenDay;
        public int LastSeenDay;

        /// <summary>Days the incident has stood (dayIndex - FirstSeenDay); -1 when either day is unknown.</summary>
        public int DaysStanding;

        public RefusalCauseClass CauseClass;
        public string LastTaskId;
        public string LastPersonId;
        public string LastResidentId;
        public string LastHouseholdId;
        public string LastBusinessId;
        public string LastBuildingId;
        public string LastOrderId;
        public string LastShipmentId;
        public string LastTransactionId;
        public IssueRegisterStatus RegisterStatus;

        /// <summary>True when the signature has an explicit register entry; false = shown as Open (unregistered).</summary>
        public bool IsRegistered;
    }

    /// <summary>Center filters: severity, subsystem, entity, type (cause class), verification (register) status, and free text. Every field is optional; an unset field matches everything.</summary>
    public sealed class HealthControlCenterFilters
    {
        /// <summary>Null = all severities.</summary>
        public IncidentSeverity? Severity;

        /// <summary>Substring of the owning system (case-insensitive); null/empty = all.</summary>
        public string OwningSystem;

        /// <summary>Substring matched against any correlation id (task/person/resident/household/business/building/order/shipment/transaction); null/empty = all.</summary>
        public string EntityId;

        /// <summary>Null = all cause classes.</summary>
        public RefusalCauseClass? CauseClass;

        /// <summary>Null = all register statuses.</summary>
        public IssueRegisterStatus? RegisterStatus;

        /// <summary>Substring of the signature id or summary (case-insensitive); null/empty = all.</summary>
        public string Text;

        /// <summary>True when the row passes every set filter.</summary>
        public bool Matches(HealthIncidentRow row)
        {
            if (row == null)
            {
                return false;
            }

            if (Severity.HasValue && row.Severity != Severity.Value)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(OwningSystem)
                && (row.OwningSystem == null
                    || row.OwningSystem.IndexOf(OwningSystem, StringComparison.OrdinalIgnoreCase) < 0))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(EntityId) && !MatchesEntity(row, EntityId))
            {
                return false;
            }

            if (CauseClass.HasValue && row.CauseClass != CauseClass.Value)
            {
                return false;
            }

            if (RegisterStatus.HasValue && row.RegisterStatus != RegisterStatus.Value)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(Text)
                && (row.SignatureId == null
                    || row.SignatureId.IndexOf(Text, StringComparison.OrdinalIgnoreCase) < 0)
                && (row.Summary == null
                    || row.Summary.IndexOf(Text, StringComparison.OrdinalIgnoreCase) < 0))
            {
                return false;
            }

            return true;
        }

        private static bool MatchesEntity(HealthIncidentRow row, string entityId)
        {
            return Contains(row.LastTaskId, entityId)
                || Contains(row.LastPersonId, entityId)
                || Contains(row.LastResidentId, entityId)
                || Contains(row.LastHouseholdId, entityId)
                || Contains(row.LastBusinessId, entityId)
                || Contains(row.LastBuildingId, entityId)
                || Contains(row.LastOrderId, entityId)
                || Contains(row.LastShipmentId, entityId)
                || Contains(row.LastTransactionId, entityId);
        }

        private static bool Contains(string value, string substring)
        {
            return !string.IsNullOrEmpty(value)
                && value.IndexOf(substring, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    /// <summary>One built Center view: totals, the per-day counters, trace completeness, and the incident rows (first-seen order, unfiltered — the window applies <see cref="HealthControlCenterFilters"/>).</summary>
    public sealed class HealthControlCenterSnapshot
    {
        public int DayIndex;
        public int RetainedCount;
        public int DroppedCount;
        public int EvictedCount;
        public int TotalOccurrences;
        public int NewSignaturesToday;
        public int RepeatingSignaturesToday;
        public int OmittedWarningsToday;
        public TraceCompleteness Trace;
        public List<HealthIncidentRow> Rows = new List<HealthIncidentRow>();
    }

    /// <summary>Per-signature occurrence counts at one day: the unit the Center compares across runs so a warning-count decrease is shown per signature (W R-13).</summary>
    public sealed class HealthOccurrenceSnapshot
    {
        public int DayIndex;
        public int RetainedCount;
        public int TotalOccurrences;
        public Dictionary<string, int> OccurrencesBySignature =
            new Dictionary<string, int>(StringComparer.Ordinal);
        public Dictionary<string, string> SeverityBySignature =
            new Dictionary<string, string>(StringComparer.Ordinal);
        public Dictionary<string, string> StatusBySignature =
            new Dictionary<string, string>(StringComparer.Ordinal);
    }

    /// <summary>How one signature's occurrence count moved between two snapshots.</summary>
    public enum HealthSignatureChangeKind
    {
        Unchanged = 0,
        Increased = 1,
        Decreased = 2,
        New = 3,
        Removed = 4,
    }

    /// <summary>One signature's movement between two occurrence snapshots. A decrease is reported, never hidden: it is a repair only when the fix is evidenced, otherwise it is a suppression to investigate (W R-14).</summary>
    public sealed class HealthSignatureDelta
    {
        public string SignatureId;
        public string Severity;
        public string Status;
        public int BeforeOccurrences;
        public int AfterOccurrences;
        public int Delta;
        public HealthSignatureChangeKind Kind;
    }

    /// <summary>The Center's logic: build the view from the authority, filter it, and compare occurrence snapshots across runs.</summary>
    public static class SimulationHealthControlCenter
    {
        /// <summary>Builds the Center view from the authority's retained incidents at the given day. Never throws on a null authority (returns an empty snapshot).</summary>
        public static HealthControlCenterSnapshot BuildSnapshot(IncidentWarningAuthority authority, int dayIndex,
            IssueRegister register, TraceCompleteness trace)
        {
            var snapshot = new HealthControlCenterSnapshot
            {
                DayIndex = dayIndex,
                Trace = trace,
            };
            if (authority == null)
            {
                return snapshot;
            }

            snapshot.RetainedCount = authority.RetainedCount;
            snapshot.DroppedCount = authority.DroppedIncidentCount;
            snapshot.EvictedCount = authority.EvictedIncidentCount;
            snapshot.TotalOccurrences = authority.TotalOccurrences;
            snapshot.NewSignaturesToday = authority.NewSignaturesToday;
            snapshot.RepeatingSignaturesToday = authority.RepeatingSignaturesToday;
            snapshot.OmittedWarningsToday = authority.OmittedWarningsToday;

            foreach (SimulationIncident incident in authority.RetainedIncidents)
            {
                IssueRegisterEntry entry = register != null ? register.TryGetEntry(incident.SignatureId) : null;
                var row = new HealthIncidentRow
                {
                    SignatureId = incident.SignatureId,
                    Severity = incident.Severity,
                    Status = incident.Status,
                    OwningSystem = incident.OwningSystem,
                    RecoveryAuthority = incident.RecoveryAuthority,
                    Summary = incident.Summary,
                    OccurrenceCount = incident.OccurrenceCount,
                    OccurrencesToday = dayIndex >= 0 ? incident.OccurrencesOnDay(dayIndex) : 0,
                    FirstSeenDay = incident.FirstSeenDay,
                    LastSeenDay = incident.LastSeenDay,
                    DaysStanding = dayIndex >= 0 && incident.FirstSeenDay >= 0
                        ? dayIndex - incident.FirstSeenDay
                        : -1,
                    CauseClass = incident.CauseClass,
                    LastTaskId = incident.LastTaskId,
                    LastPersonId = incident.LastPersonId,
                    LastResidentId = incident.LastResidentId,
                    LastHouseholdId = incident.LastHouseholdId,
                    LastBusinessId = incident.LastBusinessId,
                    LastBuildingId = incident.LastBuildingId,
                    LastOrderId = incident.LastOrderId,
                    LastShipmentId = incident.LastShipmentId,
                    LastTransactionId = incident.LastTransactionId,
                    RegisterStatus = entry != null ? entry.Status : IssueRegisterStatus.Open,
                    IsRegistered = entry != null,
                };
                snapshot.Rows.Add(row);
            }

            return snapshot;
        }

        /// <summary>Applies the filters to snapshot rows (null filters = no filtering). Returns a new list, in snapshot order.</summary>
        public static List<HealthIncidentRow> ApplyFilters(IReadOnlyList<HealthIncidentRow> rows,
            HealthControlCenterFilters filters)
        {
            var result = new List<HealthIncidentRow>();
            if (rows == null)
            {
                return result;
            }

            foreach (HealthIncidentRow row in rows)
            {
                if (filters == null || filters.Matches(row))
                {
                    result.Add(row);
                }
            }

            return result;
        }

        /// <summary>Captures per-signature occurrence counts from the authority at the given day.</summary>
        public static HealthOccurrenceSnapshot CaptureOccurrences(IncidentWarningAuthority authority, int dayIndex)
        {
            var snapshot = new HealthOccurrenceSnapshot
            {
                DayIndex = dayIndex,
            };
            if (authority == null)
            {
                return snapshot;
            }

            snapshot.RetainedCount = authority.RetainedCount;
            snapshot.TotalOccurrences = authority.TotalOccurrences;
            foreach (SimulationIncident incident in authority.RetainedIncidents)
            {
                snapshot.OccurrencesBySignature[incident.SignatureId] = incident.OccurrenceCount;
                snapshot.SeverityBySignature[incident.SignatureId] = IncidentSeverityText.Label(incident.Severity);
                snapshot.StatusBySignature[incident.SignatureId] = incident.Status.ToString();
            }

            return snapshot;
        }

        /// <summary>
        /// Diffs two occurrence snapshots, one delta per signature in either
        /// snapshot, in before-then-after first-seen order. A signature in
        /// neither snapshot never appears; a signature present in only one
        /// is New or Removed.
        /// </summary>
        public static List<HealthSignatureDelta> DiffOccurrences(HealthOccurrenceSnapshot before,
            HealthOccurrenceSnapshot after)
        {
            var deltas = new List<HealthSignatureDelta>();
            if (before == null || after == null)
            {
                return deltas;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, int> pair in before.OccurrencesBySignature)
            {
                seen.Add(pair.Key);
                deltas.Add(DiffOne(pair.Key, pair.Value, after));
            }

            foreach (KeyValuePair<string, int> pair in after.OccurrencesBySignature)
            {
                if (seen.Contains(pair.Key))
                {
                    continue;
                }

                deltas.Add(DiffOne(pair.Key, 0, after));
            }

            return deltas;
        }

        private static HealthSignatureDelta DiffOne(string signatureId, int beforeCount,
            HealthOccurrenceSnapshot after)
        {
            int afterCount;
            bool presentAfter = after.OccurrencesBySignature.TryGetValue(signatureId, out afterCount);
            string severity;
            string status;
            if (presentAfter)
            {
                after.SeverityBySignature.TryGetValue(signatureId, out severity);
                after.StatusBySignature.TryGetValue(signatureId, out status);
            }
            else
            {
                severity = string.Empty;
                status = string.Empty;
                afterCount = 0;
            }

            HealthSignatureChangeKind kind;
            if (!presentAfter)
            {
                kind = HealthSignatureChangeKind.Removed;
            }
            else if (beforeCount == 0)
            {
                kind = HealthSignatureChangeKind.New;
            }
            else if (afterCount > beforeCount)
            {
                kind = HealthSignatureChangeKind.Increased;
            }
            else if (afterCount < beforeCount)
            {
                kind = HealthSignatureChangeKind.Decreased;
            }
            else
            {
                kind = HealthSignatureChangeKind.Unchanged;
            }

            return new HealthSignatureDelta
            {
                SignatureId = signatureId,
                Severity = severity ?? string.Empty,
                Status = status ?? string.Empty,
                BeforeOccurrences = beforeCount,
                AfterOccurrences = afterCount,
                Delta = afterCount - beforeCount,
                Kind = kind,
            };
        }

        /// <summary>Writes an occurrence snapshot as plain lines (header + one line per signature). Deterministic, no JSON dependency.</summary>
        public static string WriteOccurrenceSnapshot(HealthOccurrenceSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            var builder = new StringBuilder();
            builder.AppendLine("# Land & Ledgers health occurrence snapshot v1");
            builder.Append("DAY ").Append(snapshot.DayIndex).AppendLine();
            builder.Append("TOTALS ").Append(snapshot.RetainedCount).Append(' ')
                .Append(snapshot.TotalOccurrences).AppendLine();
            var signatures = new List<string>(snapshot.OccurrencesBySignature.Keys);
            signatures.Sort(StringComparer.Ordinal);
            foreach (string signatureId in signatures)
            {
                string severity;
                snapshot.SeverityBySignature.TryGetValue(signatureId, out severity);
                string status;
                snapshot.StatusBySignature.TryGetValue(signatureId, out status);
                builder.Append("SIG ").Append(signatureId).Append(' ')
                    .Append(severity ?? string.Empty).Append(' ')
                    .Append(status ?? string.Empty).Append(' ')
                    .Append(snapshot.OccurrencesBySignature[signatureId]).AppendLine();
            }

            return builder.ToString();
        }

        /// <summary>Parses an occurrence snapshot written by <see cref="WriteOccurrenceSnapshot"/>. False (and a null snapshot) on any malformed line — a corrupt snapshot is never half-read.</summary>
        public static bool TryParseOccurrenceSnapshot(string text, out HealthOccurrenceSnapshot snapshot)
        {
            snapshot = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var parsed = new HealthOccurrenceSnapshot();
            bool sawDay = false;
            bool sawTotals = false;
            string[] lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                string[] parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                {
                    continue;
                }

                if (parts[0] == "DAY" && parts.Length == 2 && !sawDay)
                {
                    int day;
                    if (!int.TryParse(parts[1], out day))
                    {
                        return false;
                    }

                    parsed.DayIndex = day;
                    sawDay = true;
                }
                else if (parts[0] == "TOTALS" && parts.Length == 3 && !sawTotals)
                {
                    int retained;
                    int total;
                    if (!int.TryParse(parts[1], out retained) || !int.TryParse(parts[2], out total))
                    {
                        return false;
                    }

                    parsed.RetainedCount = retained;
                    parsed.TotalOccurrences = total;
                    sawTotals = true;
                }
                else if (parts[0] == "SIG" && parts.Length == 5)
                {
                    int occurrences;
                    if (!int.TryParse(parts[4], out occurrences))
                    {
                        return false;
                    }

                    parsed.OccurrencesBySignature[parts[1]] = occurrences;
                    parsed.SeverityBySignature[parts[1]] = parts[2];
                    parsed.StatusBySignature[parts[1]] = parts[3];
                }
                else
                {
                    return false;
                }
            }

            if (!sawDay || !sawTotals)
            {
                return false;
            }

            snapshot = parsed;
            return true;
        }
    }
}
