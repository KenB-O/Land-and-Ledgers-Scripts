using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace LandLedgers.Diagnostics
{
    /// <summary>
    /// Lane 1 (§4/§17, W R-1) — diagnostic parameters for the incident
    /// authority. Every field is a DIAGNOSTIC PARAMETER — not Canon, not an
    /// authored rate, not a gameplay value. Changing one changes only how
    /// loudly the diagnostic speaks, never any scheduling behaviour.
    /// Defaults mirror TaskDurationDiagnostics §4.2 where the same concept
    /// exists (512-record bound, 12 warnings/day).
    /// </summary>
    public sealed class IncidentAuthoritySettings
    {
        /// <summary>DIAGNOSTIC PARAMETER. Bounded retained-incident store.</summary>
        public int MaxRetainedIncidents { get; set; }

        /// <summary>DIAGNOSTIC PARAMETER. At most this many warning lines per day across all signatures.</summary>
        public int MaxWarningsPerDay { get; set; }

        /// <summary>DIAGNOSTIC PARAMETER. At most this many warning lines per signature per day (day-keyed dedup already allows one first line per day; the remaining allowance is for same-day [ESCALATED] / [CAUSE-CHANGED] lines).</summary>
        public int MaxWarningsPerSignaturePerDay { get; set; }

        /// <summary>
        /// DIAGNOSTIC PARAMETER. A standing (Open / CauseChanged) incident
        /// escalates after this many days without resolving. Default 7 — the
        /// household supply-incident cadence (SimulationDrivers
        /// ObserveHouseholdSupplyIncident escalates every 7 days). This is
        /// the incident-level standing horizon; it is NOT the Decision-11
        /// no-worker ladder (warn ~1d / escalate 3d / exception 7d), whose
        /// thresholds were not located in source (C R-14) and are not
        /// invented here.
        /// </summary>
        public int EscalationHorizonDays { get; set; }

        public IncidentAuthoritySettings()
        {
            MaxRetainedIncidents = 512;
            MaxWarningsPerDay = 12;
            MaxWarningsPerSignaturePerDay = 3;
            EscalationHorizonDays = 7;
        }

        /// <summary>A fresh settings instance carrying the defaults.</summary>
        public static IncidentAuthoritySettings Defaults
        {
            get { return new IncidentAuthoritySettings(); }
        }
    }

    /// <summary>
    /// Lane 1 (§4/§17, W R-1) — the one warning authority every other lane
    /// builds on, so no tool re-invents dedup badly. It is the
    /// classification / dedup / retention / export layer ABOVE the capture
    /// layer (NpcActivityTraceRecorder stays the capture layer; it is NOT
    /// routed through this authority yet).
    ///
    /// Copied patterns (W §5.1): LogRefusalDayKeyed day-keying
    /// (SimulationDrivers.cs:5074) — one line per signature per day, with an
    /// occurrence count; the session-once HashSet.Add(key) pattern for
    /// import warnings; TaskDurationDiagnostics' omitted-by-cap reporting
    /// (the caps never silently drop: the daily summary counts the omitted
    /// warnings and names their identities) and its eviction rule (evict
    /// terminal records first, oldest first; never evict an open incident to
    /// make room — drop the new observation instead, and count it).
    ///
    /// One incident record per stable signature id. Emission:
    /// day-keyed (default) — the first occurrence of a signature on a day
    /// emits one line, later same-day occurrences only increment the count;
    /// a severity upgrade or a cause change on the same day emits an
    /// [ESCALATED] / [CAUSE-CHANGED] line instead of staying silent (the
    /// household incident cadence). Session-once — one line ever per
    /// signature, later occurrences only increment the count.
    ///
    /// The authority OBSERVES authoritative state and never becomes a
    /// parallel source of truth: it holds no ledger, no obligation, no
    /// inventory, no custody, and it never calls a state-changing method on
    /// any gameplay authority. It never suppresses a warning to reach zero:
    /// dedup collapses repeats into counts, it does not delete signal.
    /// </summary>
    public sealed class IncidentWarningAuthority
    {
        private IncidentAuthoritySettings settings = IncidentAuthoritySettings.Defaults;
        private readonly Dictionary<string, SimulationIncident> incidents =
            new Dictionary<string, SimulationIncident>(StringComparer.Ordinal);
        private readonly List<string> incidentOrder = new List<string>();

        // ---- Warning emission + bounding / rate-limiting (TaskDurationDiagnostics §4.2 pattern) ----
        // The sink defaults to Debug.LogWarning (production) and is settable for tests,
        // which bind a capturing Action<string>. The authority never calls Debug directly
        // except through the sink, so the harness can assert exact lines.
        private Action<string> logSink;
        private Action<string> infoSink;

        private bool enabled = true;
        private int warningsDay = -1;
        private int warningsToday;
        private readonly Dictionary<string, int> warningsPerSignatureToday =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<string> sessionOnceEmitted = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> signaturesSeenToday = new HashSet<string>(StringComparer.Ordinal);
        private int newSignaturesToday;
        private readonly HashSet<string> omittedIdentitiesToday = new HashSet<string>(StringComparer.Ordinal);
        private int omittedCountToday;
        private int lastSummaryDay = -1;
        private int evictedCountAtLastSummary;

        /// <summary>Identities of incidents evicted since the last summary was emitted, in eviction order. Cleared when the summary is emitted.</summary>
        private readonly List<string> evictedSinceLastSummaryIdentities = new List<string>();

        public IncidentWarningAuthority()
        {
        }

        /// <summary>The diagnostic parameters. Never null: assigning null restores the defaults.</summary>
        public IncidentAuthoritySettings Settings
        {
            get { return settings; }
            set { settings = value ?? IncidentAuthoritySettings.Defaults; }
        }

        /// <summary>
        /// Master switch. When false, Emit/EmitSessionOnce record nothing and
        /// emit nothing (the authority is silent and retains nothing new).
        /// Default true.
        /// </summary>
        public bool Enabled
        {
            get { return enabled; }
            set { enabled = value; }
        }

        /// <summary>
        /// Warnings and summaries are emitted through this sink. Defaults to
        /// <see cref="Debug.LogWarning"/>; tests may bind a capturing sink.
        /// </summary>
        public Action<string> LogSink
        {
            get { return logSink ?? (message => Debug.LogWarning(message)); }
            set { logSink = value; }
        }

        /// <summary>Expected diagnostic-store maintenance; defaults to the informational Unity Console channel.</summary>
        public Action<string> InfoSink
        {
            get { return infoSink ?? (message => Debug.Log(message)); }
            set { infoSink = value; }
        }

        /// <summary>Incidents currently retained (signatures).</summary>
        public int RetainedCount
        {
            get { return incidents.Count; }
        }

        /// <summary>New incidents dropped because the store held only open incidents (eviction rule).</summary>
        public int DroppedIncidentCount { get; private set; }

        /// <summary>Incidents evicted by the store bound so far (terminal first, oldest first).</summary>
        public int EvictedIncidentCount { get; private set; }

        /// <summary>Total occurrences recorded across all incidents.</summary>
        public int TotalOccurrences { get; private set; }

        /// <summary>New signatures seen on the current day (resets when the day rolls).</summary>
        public int NewSignaturesToday
        {
            get { return newSignaturesToday; }
        }

        /// <summary>Signatures seen before today and seen again today.</summary>
        public int RepeatingSignaturesToday
        {
            get
            {
                int repeating = signaturesSeenToday.Count - newSignaturesToday;
                return repeating < 0 ? 0 : repeating;
            }
        }

        /// <summary>Warning lines omitted by cap on the current day.</summary>
        public int OmittedWarningsToday
        {
            get { return omittedCountToday; }
        }

        /// <summary>Day index the per-day counters currently belong to (-1 = no day-keyed emission yet).</summary>
        public int CurrentDay
        {
            get { return warningsDay; }
        }

        /// <summary>Incidents retained, in first-seen order.</summary>
        public IReadOnlyList<SimulationIncident> RetainedIncidents
        {
            get
            {
                var list = new List<SimulationIncident>(incidentOrder.Count);
                foreach (string signatureId in incidentOrder)
                {
                    SimulationIncident incident;
                    if (incidents.TryGetValue(signatureId, out incident))
                    {
                        list.Add(incident);
                    }
                }

                return list;
            }
        }

        /// <summary>Reports one occurrence, day-keyed. Returns the incident, or null when disabled or dropped by the retention bound.</summary>
        public SimulationIncident Emit(IncidentReport report)
        {
            return EmitCore(report, false);
        }

        /// <summary>Reports one occurrence, session-once: one line ever per signature; later occurrences only increment the count. Returns the incident, or null when disabled or dropped.</summary>
        public SimulationIncident EmitSessionOnce(IncidentReport report)
        {
            return EmitCore(report, true);
        }

        /// <summary>Finds the retained incident for a signature id (CI-* ids pass through; anything else is normalised first). Null when absent.</summary>
        public SimulationIncident TryGetIncident(string signatureId)
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

            SimulationIncident incident;
            return incidents.TryGetValue(normalised, out incident) ? incident : null;
        }

        /// <summary>Occurrences of one signature on one simulated day (0 when absent or never observed that day).</summary>
        public int OccurrencesOnDay(string signatureId, int dayIndex)
        {
            SimulationIncident incident = TryGetIncident(signatureId);
            return incident != null ? incident.OccurrencesOnDay(dayIndex) : 0;
        }

        /// <summary>
        /// Lane 6 (Tool 12) — the 7-day escalation sweep Lane 1 left open.
        /// Evaluates every retained standing incident (Open / CauseChanged)
        /// for the given day: one first seen at least
        /// <see cref="IncidentAuthoritySettings.EscalationHorizonDays"/>
        /// days ago moves to Escalated and emits one [ESCALATED] line naming
        /// the owning system and the recovery authority (W §7). The sweep
        /// mirrors the household supply-incident cadence
        /// (ObserveHouseholdSupplyIncident escalates every 7 days); it does
        /// not change severity — ESCALATED stays at the incident's severity —
        /// and it never re-escalates an incident that is already Escalated,
        /// Resolved, or Closed. Occurrence counts are untouched: the sweep
        /// observes, it does not record a new occurrence. Returns the
        /// signature ids escalated, in first-seen order.
        /// </summary>
        public IReadOnlyList<string> EvaluateEscalationSweep(int dayIndex, double simSeconds)
        {
            var escalated = new List<string>();
            if (!enabled || dayIndex < 0)
            {
                return escalated;
            }

            RollDay(dayIndex);
            int horizon = settings != null && settings.EscalationHorizonDays >= 1
                ? settings.EscalationHorizonDays
                : IncidentAuthoritySettings.Defaults.EscalationHorizonDays;

            // Iterate a copy of the order: ChangeStatus does not mutate it,
            // but the sweep must stay correct if that ever changes.
            var order = new List<string>(incidentOrder);
            foreach (string signatureId in order)
            {
                SimulationIncident incident;
                if (!incidents.TryGetValue(signatureId, out incident))
                {
                    continue;
                }

                if (incident.Status != IncidentStatus.Open
                    && incident.Status != IncidentStatus.CauseChanged)
                {
                    continue;
                }

                if (incident.FirstSeenDay < 0)
                {
                    continue;
                }

                int standingDays = dayIndex - incident.FirstSeenDay;
                if (standingDays < horizon)
                {
                    continue;
                }

                incident.ChangeStatus(IncidentStatus.Escalated, dayIndex, simSeconds,
                    "standing " + standingDays + " day(s); escalation horizon " + horizon + " day(s)");
                escalated.Add(signatureId);

                if (!TryEmitLine(signatureId, dayIndex))
                {
                    omittedCountToday++;
                    omittedIdentitiesToday.Add(signatureId);
                    continue;
                }

                Emit("[Incident][ESCALATED] " + signatureId + " day " + dayIndex
                    + ": open " + standingDays + " day(s) (first seen day " + incident.FirstSeenDay + ")"
                    + ", system " + incident.OwningSystem
                    + ", recovery " + incident.RecoveryAuthority
                    + "; " + incident.Summary);
            }

            return escalated;
        }

        /// <summary>
        /// Lane 6 (Tool 12) — the [Incident][CLOSED] emitter (E W-5).
        /// Verified in this lane: no [CLOSED] emitter existed in the core —
        /// the household system emits [Incident][RESOLVED] (info) when a
        /// need closes, and nothing emitted [CLOSED] for a disposition
        /// (fixed, explained-legitimate, blocked-with-reason). Closes the
        /// retained incident with the given disposition, records the
        /// transition in its status history, and emits one [CLOSED] line.
        /// Returns false when disabled, when the signature is unknown, or
        /// when the incident is already Resolved or Closed (a second close
        /// emits nothing). Closing records no occurrence: dedup counts
        /// survive the close unchanged.
        /// </summary>
        public bool CloseIncident(string signatureId, int dayIndex, double simSeconds, string disposition)
        {
            if (!enabled || string.IsNullOrWhiteSpace(signatureId))
            {
                return false;
            }

            SimulationIncident incident = TryGetIncident(signatureId);
            if (incident == null)
            {
                return false;
            }

            if (incident.Status == IncidentStatus.Closed || incident.Status == IncidentStatus.Resolved)
            {
                return false;
            }

            string note = string.IsNullOrWhiteSpace(disposition) ? "closed" : disposition.Trim();
            incident.ChangeStatus(IncidentStatus.Closed, dayIndex, simSeconds, note);
            Emit("[Incident][CLOSED] " + incident.SignatureId + " day " + dayIndex
                + ": " + note + "; " + incident.Summary);
            return true;
        }

        /// <summary>
        /// Emits the daily summary line (at most once per day) through the
        /// warning sink, plus the omitted-identities line when the caps hid
        /// warnings and the evicted-identities line (info sink) when
        /// evictions happened since the last summary. Returns the summary
        /// line, or null when the summary for this day was already emitted.
        /// Copies TaskDurationDiagnostics' omitted-by-cap reporting: the
        /// caps never silently drop — the summary counts the omitted warnings
        /// and the following line names them.
        /// </summary>
        public string EmitDailySummary(int dayIndex)
        {
            if (dayIndex < 0 || lastSummaryDay == dayIndex)
            {
                return null;
            }

            RollDay(dayIndex);
            int retained = incidents.Count;
            int repeatingToday = RepeatingSignaturesToday;

            int evictedSinceLastSummary = EvictedIncidentCount - evictedCountAtLastSummary;
            string line = "[Incident] summary day " + dayIndex
                + ": " + retained + " incident(s) retained"
                + ", " + TotalOccurrences + " occurrence(s) total"
                + " (" + newSignaturesToday + " new, " + repeatingToday + " repeating, "
                + omittedCountToday + " omitted by cap)";
            if (evictedSinceLastSummary > 0)
            {
                line += "; evicted " + evictedSinceLastSummary + " incident(s)";
            }

            Emit(line);
            if (omittedIdentitiesToday.Count > 0)
            {
                Emit("[Incident] omitted day " + dayIndex
                    + ": " + omittedCountToday + " omitted by cap: "
                    + string.Join(", ", SortedCopy(omittedIdentitiesToday)));
            }

            if (evictedSinceLastSummary > 0)
            {
                EmitInfo("[Incident] evicted day " + dayIndex
                    + ": " + evictedSinceLastSummary + " evicted: "
                    + string.Join(", ", evictedSinceLastSummaryIdentities.ToArray()));
            }

            lastSummaryDay = dayIndex;
            evictedCountAtLastSummary = EvictedIncidentCount;
            evictedSinceLastSummaryIdentities.Clear();
            return line;
        }

        /// <summary>
        /// Structured export: a manifest JSON line (schema version, retained /
        /// dropped / evicted counts, total occurrences) followed by one JSON
        /// object per retained incident, in first-seen order. Hand-rolled
        /// escaping (no JSON dependency). A consumer can rebuild the incident
        /// register from this file alone.
        /// </summary>
        public void ExportJsonLines(TextWriter writer)
        {
            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            writer.WriteLine("{"
                + "\"schemaVersion\":1"
                + ",\"kind\":\"incident-manifest\""
                + ",\"retainedIncidents\":" + incidents.Count
                + ",\"droppedIncidents\":" + DroppedIncidentCount
                + ",\"evictedIncidents\":" + EvictedIncidentCount
                + ",\"totalOccurrences\":" + TotalOccurrences
                + "}");
            foreach (string signatureId in incidentOrder)
            {
                SimulationIncident incident;
                if (!incidents.TryGetValue(signatureId, out incident))
                {
                    continue;
                }

                writer.WriteLine(IncidentToJson(incident));
            }
        }

        /// <summary>
        /// Readable export: retained/dropped/evicted counts (the monitor's
        /// "Retained / dropped" line) followed by one line per retained
        /// incident with severity, status, day span and occurrence count.
        /// </summary>
        public void ExportSummary(TextWriter writer)
        {
            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            writer.WriteLine("Incidents: " + incidents.Count + " retained; "
                + DroppedIncidentCount + " dropped; " + EvictedIncidentCount + " evicted; "
                + TotalOccurrences + " total occurrences");
            foreach (string signatureId in incidentOrder)
            {
                SimulationIncident incident;
                if (!incidents.TryGetValue(signatureId, out incident))
                {
                    continue;
                }

                writer.WriteLine(incident.SignatureId
                    + " " + IncidentSeverityText.Label(incident.Severity)
                    + " " + incident.Status
                    + " day " + incident.FirstSeenDay + "-" + incident.LastSeenDay
                    + " x" + incident.OccurrenceCount
                    + " — " + incident.Summary);
            }
        }

        private SimulationIncident EmitCore(IncidentReport report, bool sessionOnce)
        {
            if (!enabled || report == null)
            {
                return null;
            }

            string signatureId = IncidentSignature.Normalize(report.SignatureId)
                ?? IncidentSignature.DeriveFromSummary(report.OwningSystem, report.Summary);
            int day = report.DayIndex;
            bool dayKeyed = !sessionOnce && day >= 0;
            if (dayKeyed)
            {
                RollDay(day);
            }

            SimulationIncident incident;
            IncidentOccurrenceOutcome outcome;
            if (!incidents.TryGetValue(signatureId, out incident))
            {
                if (!TryMakeRoom())
                {
                    DroppedIncidentCount++;
                    return null;
                }

                incident = new SimulationIncident(signatureId, report);
                incidents.Add(signatureId, incident);
                incidentOrder.Add(signatureId);
                outcome = new IncidentOccurrenceOutcome
                {
                    IsFirstOccurrence = true,
                    PreviousSeverity = incident.FirstSeverity,
                    PreviousCauseClass = incident.CauseClass,
                    CauseChanged = false,
                    SeverityUpgraded = false,
                };
                if (day >= 0 && signaturesSeenToday.Add(signatureId))
                {
                    newSignaturesToday++;
                }
            }
            else
            {
                outcome = incident.RecordOccurrence(report);
                if (day >= 0)
                {
                    signaturesSeenToday.Add(signatureId);
                }
            }

            TotalOccurrences++;
            string summary = report.Summary ?? string.Empty;

            bool firstLine = dayKeyed
                ? incident.LastEmittedDay != day
                : !sessionOnceEmitted.Contains(signatureId);
            bool changedLine = !firstLine && (outcome.SeverityUpgraded || outcome.CauseChanged);
            if (!firstLine && !changedLine)
            {
                return incident;
            }

            if (!TryEmitLine(signatureId, day))
            {
                omittedCountToday++;
                omittedIdentitiesToday.Add(signatureId);
                return incident;
            }

            if (changedLine && outcome.SeverityUpgraded)
            {
                Emit("[Incident][ESCALATED] " + signatureId + " day " + day
                    + ": severity " + IncidentSeverityText.Label(outcome.PreviousSeverity)
                    + " -> " + IncidentSeverityText.Label(incident.Severity)
                    + "; " + summary);
            }
            else if (changedLine)
            {
                Emit("[Incident][CAUSE-CHANGED] " + signatureId + " day " + day
                    + ": cause " + RefusalCauseClassifier.Label(outcome.PreviousCauseClass)
                    + " -> " + RefusalCauseClassifier.Label(incident.CauseClass)
                    + "; " + summary);
            }
            else if (dayKeyed)
            {
                Emit("[Incident] " + IncidentSeverityText.Label(incident.Severity)
                    + " " + signatureId + " day " + day
                    + ": " + summary
                    + " (occurrence " + incident.OccurrencesOnDay(day)
                    + " today, " + incident.OccurrenceCount + " total)");
            }
            else
            {
                Emit("[Incident] " + IncidentSeverityText.Label(incident.Severity)
                    + " " + signatureId
                    + ": " + summary
                    + " (occurrence " + incident.OccurrenceCount + " total)");
            }

            if (dayKeyed)
            {
                incident.LastEmittedDay = day;
            }
            else
            {
                sessionOnceEmitted.Add(signatureId);
            }

            return incident;
        }

        /// <summary>Resets the per-day warning counters when the day changes (TaskDurationDiagnostics pattern).</summary>
        private void RollDay(int dayIndex)
        {
            if (dayIndex == warningsDay)
            {
                return;
            }

            warningsDay = dayIndex;
            warningsToday = 0;
            warningsPerSignatureToday.Clear();
            signaturesSeenToday.Clear();
            newSignaturesToday = 0;
            omittedIdentitiesToday.Clear();
            omittedCountToday = 0;
        }

        /// <summary>Checks the per-day caps for one more line. False when a cap blocks it.</summary>
        private bool TryEmitLine(string signatureId, int day)
        {
            IncidentAuthoritySettings diagnosticSettings = settings ?? IncidentAuthoritySettings.Defaults;
            if (warningsToday >= diagnosticSettings.MaxWarningsPerDay)
            {
                return false;
            }

            if (day >= 0)
            {
                int perSignature;
                warningsPerSignatureToday.TryGetValue(signatureId, out perSignature);
                if (perSignature >= diagnosticSettings.MaxWarningsPerSignaturePerDay)
                {
                    return false;
                }

                warningsPerSignatureToday[signatureId] = perSignature + 1;
            }

            warningsToday++;
            return true;
        }

        /// <summary>
        /// Makes room for one new incident. Evicts terminal incidents
        /// (Resolved/Closed) first, oldest first; never evicts an open
        /// incident to make room — when the store holds only open incidents
        /// the caller drops the new observation instead.
        /// </summary>
        private bool TryMakeRoom()
        {
            IncidentAuthoritySettings diagnosticSettings = settings ?? IncidentAuthoritySettings.Defaults;
            if (incidents.Count < diagnosticSettings.MaxRetainedIncidents)
            {
                return true;
            }

            string evictKey = null;
            foreach (string candidateKey in incidentOrder)
            {
                SimulationIncident candidate;
                if (incidents.TryGetValue(candidateKey, out candidate)
                    && (candidate.Status == IncidentStatus.Resolved
                        || candidate.Status == IncidentStatus.Closed))
                {
                    evictKey = candidateKey;
                    break;
                }
            }

            if (evictKey == null)
            {
                return false;
            }

            incidents.Remove(evictKey);
            incidentOrder.Remove(evictKey);
            EvictedIncidentCount++;
            evictedSinceLastSummaryIdentities.Add(evictKey);
            return true;
        }

        /// <summary>Emits one actionable warning line through the injected warning sink.</summary>
        private void Emit(string line)
        {
            LogSink(line);
        }

        /// <summary>Emits expected diagnostic-store maintenance without classifying it as a warning.</summary>
        private void EmitInfo(string line)
        {
            InfoSink(line);
        }

        private static string[] SortedCopy(HashSet<string> values)
        {
            var copy = new List<string>(values);
            copy.Sort(StringComparer.Ordinal);
            return copy.ToArray();
        }

        private static string IncidentToJson(SimulationIncident incident)
        {
            var builder = new StringBuilder();
            builder.Append("{");
            AppendJsonString(builder, "id", incident.Id);
            builder.Append(",");
            AppendJsonString(builder, "signatureId", incident.SignatureId);
            builder.Append(",");
            AppendJsonString(builder, "severity", IncidentSeverityText.Label(incident.Severity));
            builder.Append(",");
            AppendJsonString(builder, "status", incident.Status.ToString());
            builder.Append(",");
            AppendJsonString(builder, "summary", incident.Summary);
            builder.Append(",");
            AppendJsonString(builder, "owningSystem", incident.OwningSystem);
            builder.Append(",");
            AppendJsonString(builder, "recoveryAuthority", incident.RecoveryAuthority);
            builder.Append(",");
            AppendJsonString(builder, "waitState", incident.WaitState);
            builder.Append(",");
            AppendJsonString(builder, "retryBoundary", incident.RetryBoundary);
            builder.Append(",");
            AppendJsonString(builder, "causeClass", RefusalCauseClassifier.Label(incident.CauseClass));
            builder.Append(",");
            AppendJsonString(builder, "causeEvidence", incident.CauseEvidence);
            builder.Append(",");
            AppendJsonString(builder, "taskId", incident.LastTaskId);
            builder.Append(",");
            AppendJsonString(builder, "personId", incident.LastPersonId);
            builder.Append(",");
            AppendJsonString(builder, "residentId", incident.LastResidentId);
            builder.Append(",");
            AppendJsonString(builder, "householdId", incident.LastHouseholdId);
            builder.Append(",");
            AppendJsonString(builder, "businessId", incident.LastBusinessId);
            builder.Append(",");
            AppendJsonString(builder, "buildingId", incident.LastBuildingId);
            builder.Append(",");
            AppendJsonString(builder, "orderId", incident.LastOrderId);
            builder.Append(",");
            AppendJsonString(builder, "shipmentId", incident.LastShipmentId);
            builder.Append(",");
            AppendJsonString(builder, "transactionId", incident.LastTransactionId);
            builder.Append(",");
            builder.Append("\"firstDay\":").Append(incident.FirstSeenDay);
            builder.Append(",\"lastDay\":").Append(incident.LastSeenDay);
            builder.Append(",\"firstSimSeconds\":").Append(incident.FirstSeenSimSeconds.ToString("R"));
            builder.Append(",\"lastSimSeconds\":").Append(incident.LastSeenSimSeconds.ToString("R"));
            builder.Append(",");
            if (incident.FirstSeenWallClockUtc.HasValue)
            {
                AppendJsonString(builder, "wallClockUtc", incident.FirstSeenWallClockUtc.Value.ToString("o"));
            }
            else
            {
                builder.Append("\"wallClockUtc\":null");
            }

            builder.Append(",\"occurrenceCount\":").Append(incident.OccurrenceCount);
            builder.Append(",\"occurrencesByDay\":{");
            bool firstDayEntry = true;
            foreach (int observedDay in incident.DaysObserved())
            {
                if (!firstDayEntry)
                {
                    builder.Append(",");
                }

                firstDayEntry = false;
                builder.Append("\"").Append(observedDay).Append("\":").Append(incident.OccurrencesOnDay(observedDay));
            }

            builder.Append("}");
            AppendJsonStringArray(builder, "physicalCapabilityBlockers", incident.PhysicalCapabilityBlockers);
            AppendJsonStringArray(builder, "evidenceReferences", incident.EvidenceReferences);
            AppendJsonStringArray(builder, "relatedIncidentIds", incident.RelatedIncidentIds);
            builder.Append(",\"statusHistory\":[");
            bool firstChange = true;
            foreach (IncidentStatusChange change in incident.StatusHistory)
            {
                if (!firstChange)
                {
                    builder.Append(",");
                }

                firstChange = false;
                builder.Append("{");
                AppendJsonString(builder, "from", change.FromStatus.ToString());
                builder.Append(",");
                AppendJsonString(builder, "to", change.ToStatus.ToString());
                builder.Append(",\"day\":").Append(change.DayIndex);
                builder.Append(",\"simSeconds\":").Append(change.SimSeconds.ToString("R"));
                builder.Append(",");
                AppendJsonString(builder, "note", change.Note);
                builder.Append("}");
            }

            builder.Append("]");
            builder.Append("}");
            return builder.ToString();
        }

        private static void AppendJsonString(StringBuilder builder, string key, string value)
        {
            builder.Append("\"").Append(key).Append("\":\"").Append(EscapeJson(value)).Append("\"");
        }

        private static void AppendJsonStringArray(StringBuilder builder, string key, List<string> values)
        {
            builder.Append(",\"").Append(key).Append("\":[");
            bool first = true;
            foreach (string value in values)
            {
                if (!first)
                {
                    builder.Append(",");
                }

                first = false;
                builder.Append("\"").Append(EscapeJson(value)).Append("\"");
            }

            builder.Append("]");
        }

        /// <summary>Minimal JSON string escaping (quotes, backslashes, control characters).</summary>
        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            builder.Append("\\u").Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            builder.Append(c);
                        }

                        break;
                }
            }

            return builder.ToString();
        }
    }
}
