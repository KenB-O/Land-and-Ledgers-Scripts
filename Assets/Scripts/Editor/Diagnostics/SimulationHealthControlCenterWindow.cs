// Lane 6 (Muse Pass, Nov 20 2026) — Simulation Health and Issue Control
// Center (Tool 12). Menu: Land & Ledgers/Diagnostics/Simulation Health
// Control Center. The window Kennedy opens first: everything the
// diagnostic core holds — retained incidents, occurrence counts,
// severities, dedup state, retention/eviction counts, daily summaries,
// export — with configurable thresholds and the issue register view.
//
// Observation only: it assigns no work, changes no routes, and moves no
// goods, money, food, transport or labour. Thresholds are persisted in
// EditorPrefs (not hard-coded per run); the register view is the interim
// per-signature status store until Lane 11 builds the §22 register.
// The window never references the double-precision timeline (DEF-046):
// the day comes from the authority, never from TimeManager.
//
// Verification: this file is UnityEditor-bound and CANNOT join the Roslyn
// harness closure. It is verified with ParseCheck only (must print
// PARSE CLEAN). The window cannot be opened without Unity here; the logic
// it displays lives in SimulationHealthControlCenter (Core, harness
// tested). No live run exists on this HEAD.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LandLedgers.Diagnostics;
using LandLedgers.Orchestration.Systems;
using UnityEditor;
using UnityEngine;

namespace LandLedgers.Editor.Diagnostics
{
    public sealed class SimulationHealthControlCenterWindow : EditorWindow
    {
        private const string PrefsPrefix = "LandLedgers.HealthCenter.";
        private const string PrefsRetained = PrefsPrefix + "MaxRetainedIncidents";
        private const string PrefsWarningsPerDay = PrefsPrefix + "MaxWarningsPerDay";
        private const string PrefsWarningsPerSignature = PrefsPrefix + "MaxWarningsPerSignaturePerDay";
        private const string PrefsEscalationHorizon = PrefsPrefix + "EscalationHorizonDays";
        private const string PrefsRegister = PrefsPrefix + "Register";

        private readonly IssueRegister register = new IssueRegister();

        private Vector2 scroll;
        private Vector2 tableScroll;
        private Vector2 detailScroll;
        private string status = "No refresh run yet.";

        // Threshold fields (persisted in EditorPrefs; applied to the shared authority on Apply).
        private int maxRetained = 512;
        private int maxWarningsPerDay = 12;
        private int maxWarningsPerSignaturePerDay = 3;
        private int escalationHorizonDays = 7;

        // Filter fields (session-only).
        private int severityFilter;
        private string systemFilter = "";
        private string entityFilter = "";
        private int causeFilter;
        private int registerFilter;
        private string textFilter = "";

        private string selectedSignature = "";
        private string closeDisposition = "fixed";
        private int registerStatusSelection;
        private string registerAuthority = "";
        private string registerNextAction = "";
        private string registerEvidence = "";

        private List<HealthSignatureDelta> snapshotDiff;
        private string snapshotDiffNote = "";

        [MenuItem("Land & Ledgers/Diagnostics/Simulation Health Control Center")]
        public static void Open()
        {
            SimulationHealthControlCenterWindow window = GetWindow<SimulationHealthControlCenterWindow>("Health Control Center");
            window.minSize = new Vector2(760, 640);
            window.Show();
        }

        private void OnEnable()
        {
            LoadThresholds();
            LoadRegister();
        }

        private void OnGUI()
        {
            IncidentWarningAuthority authority = IncidentDiagnostics.SharedAuthority;
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Simulation Health and Issue Control Center", EditorStyles.largeLabel);
            EditorGUILayout.HelpBox("Observes the shared incident core (Lane 1) and the tools built on it (Investigator, Economy Auditor). It never assigns work, changes routes, or alters simulation state. A warning-count decrease is shown per signature so a repair is provable and a suppression is visible — never a single-number dashboard.", MessageType.Info);
            EditorGUILayout.HelpBox("No live run exists on this HEAD. The Center shows the shared authority's retained incidents (Investigator emissions) and the live trace when Play Mode is running.", MessageType.None);

            int dayIndex = authority.CurrentDay;
            TraceCompleteness trace = ReadTraceCompleteness();
            HealthControlCenterSnapshot snapshot = SimulationHealthControlCenter.BuildSnapshot(authority, dayIndex, register, trace);

            DrawHealthSummary(snapshot, authority);
            DrawThresholds(authority);
            DrawFilters();
            DrawIncidentTable(snapshot, authority);
            DrawSelectedDetail(snapshot, authority);
            DrawSnapshotCompare();
            DrawUnreadNotes();
            EditorGUILayout.LabelField(status, EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndScrollView();
        }

        private void DrawHealthSummary(HealthControlCenterSnapshot snapshot, IncidentWarningAuthority authority)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Health summary", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Day", snapshot.DayIndex >= 0 ? "day " + snapshot.DayIndex : "(no day-keyed emission yet)");
            EditorGUILayout.LabelField("Incidents",
                snapshot.RetainedCount + " retained; " + snapshot.DroppedCount + " dropped; "
                + snapshot.EvictedCount + " evicted; " + snapshot.TotalOccurrences + " total occurrences");
            EditorGUILayout.LabelField("Today",
                snapshot.NewSignaturesToday + " new, " + snapshot.RepeatingSignaturesToday + " repeating, "
                + snapshot.OmittedWarningsToday + " omitted by cap");
            if (snapshot.Trace.HasTrace)
            {
                EditorGUILayout.LabelField("Trace completeness",
                    snapshot.Trace.RetainedSessionEvents + " session events retained; dropped session/prehistory: "
                    + snapshot.Trace.DroppedSessionEvents + "/" + snapshot.Trace.DroppedHistoryEvents
                    + "; anomalies: " + snapshot.Trace.AnomalyCount);
                if (snapshot.Trace.DroppedSessionEvents > 0 || snapshot.Trace.DroppedHistoryEvents > 0)
                    EditorGUILayout.HelpBox("The trace dropped events inside the investigation window — the evidence below may be incomplete.", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.LabelField("Trace completeness", "(no live trace — enter Play Mode in the Main Scene to connect to SimulationSystemsHub)");
            }

            EditorGUILayout.LabelField("Checklist baseline (Nov 19, 2026 — append-only history)",
                ChecklistBaseline.Signatures + " signatures, " + ChecklistBaseline.Occurrences + " occurrences — "
                + ChecklistBaseline.Open + " OPEN, " + ChecklistBaseline.ImplementedUnverified + " IMPLEMENTED-UNVERIFIED, "
                + ChecklistBaseline.ExplainedLegitimate + " EXPLAINED-LEGITIMATE, " + ChecklistBaseline.BlockedWithReason
                + " BLOCKED WITH REASON, " + ChecklistBaseline.Investigating + " INVESTIGATING ("
                + ChecklistBaseline.SourcePath + "). A signature absent from a new run is not a fix unless the fix is evidenced.");
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Emit daily summary (today)"))
            {
                if (snapshot.DayIndex < 0)
                {
                    status = "No day seen yet — the authority has had no day-keyed emission, so there is no day to summarise.";
                }
                else
                {
                    authority.EmitDailySummary(snapshot.DayIndex);
                    status = "Daily summary emitted for day " + snapshot.DayIndex + " (see Console).";
                }
            }
            if (GUILayout.Button("Run escalation sweep now"))
            {
                if (snapshot.DayIndex < 0)
                {
                    status = "No day seen yet — the sweep needs a day index.";
                }
                else
                {
                    IReadOnlyList<string> escalated = authority.EvaluateEscalationSweep(snapshot.DayIndex, -1);
                    status = escalated.Count == 0
                        ? "Sweep for day " + snapshot.DayIndex + ": no standing incident reached the " + authority.Settings.EscalationHorizonDays + "-day horizon."
                        : "Sweep for day " + snapshot.DayIndex + ": escalated " + escalated.Count + ": " + string.Join(", ", escalated);
                }
            }
            if (GUILayout.Button("Export JSON lines...")) ExportAuthority(authority, true);
            if (GUILayout.Button("Export readable summary...")) ExportAuthority(authority, false);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawThresholds(IncidentWarningAuthority authority)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Thresholds (diagnostic parameters — persisted in EditorPrefs, applied live)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("These change only how loudly the diagnostic speaks, never any scheduling behaviour. The escalation horizon is the incident-level standing horizon (household 7-day cadence); it is NOT the Decision-11 no-worker ladder (warn ~1d / escalate 3d / exception 7d), whose thresholds were not located in source (C R-14).", MessageType.None);
            maxRetained = EditorGUILayout.IntField("Max retained incidents", maxRetained);
            maxWarningsPerDay = EditorGUILayout.IntField("Max warnings per day", maxWarningsPerDay);
            maxWarningsPerSignaturePerDay = EditorGUILayout.IntField("Max warnings per signature per day", maxWarningsPerSignaturePerDay);
            escalationHorizonDays = EditorGUILayout.IntField("Escalation horizon (days)", escalationHorizonDays);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply thresholds"))
            {
                maxRetained = Math.Max(1, maxRetained);
                maxWarningsPerDay = Math.Max(0, maxWarningsPerDay);
                maxWarningsPerSignaturePerDay = Math.Max(1, maxWarningsPerSignaturePerDay);
                escalationHorizonDays = Math.Max(1, escalationHorizonDays);
                IncidentAuthoritySettings settings = authority.Settings;
                settings.MaxRetainedIncidents = maxRetained;
                settings.MaxWarningsPerDay = maxWarningsPerDay;
                settings.MaxWarningsPerSignaturePerDay = maxWarningsPerSignaturePerDay;
                settings.EscalationHorizonDays = escalationHorizonDays;
                authority.Settings = settings;
                SaveThresholds();
                status = "Thresholds applied and persisted: retained " + maxRetained + ", warnings/day " + maxWarningsPerDay
                    + ", per-signature/day " + maxWarningsPerSignaturePerDay + ", escalation horizon " + escalationHorizonDays + "d.";
            }
            if (GUILayout.Button("Reset to defaults"))
            {
                IncidentAuthoritySettings defaults = IncidentAuthoritySettings.Defaults;
                maxRetained = defaults.MaxRetainedIncidents;
                maxWarningsPerDay = defaults.MaxWarningsPerDay;
                maxWarningsPerSignaturePerDay = defaults.MaxWarningsPerSignaturePerDay;
                escalationHorizonDays = defaults.EscalationHorizonDays;
                authority.Settings = defaults;
                SaveThresholds();
                status = "Thresholds reset to defaults and persisted.";
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawFilters()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Filters", EditorStyles.boldLabel);
            severityFilter = EditorGUILayout.Popup("Severity", severityFilter, SeverityOptions());
            systemFilter = EditorGUILayout.TextField("Owning system contains", systemFilter);
            entityFilter = EditorGUILayout.TextField("Entity id contains (task/person/household/business/building/order/shipment/transaction)", entityFilter);
            causeFilter = EditorGUILayout.Popup("Cause class", causeFilter, CauseOptions());
            registerFilter = EditorGUILayout.Popup("Register status", registerFilter, RegisterOptions());
            textFilter = EditorGUILayout.TextField("Text contains (signature or summary)", textFilter);
            EditorGUILayout.EndVertical();
        }

        private void DrawIncidentTable(HealthControlCenterSnapshot snapshot, IncidentWarningAuthority authority)
        {
            List<HealthIncidentRow> rows = SimulationHealthControlCenter.ApplyFilters(snapshot.Rows, BuildFilters());
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Incidents (" + rows.Count + " of " + snapshot.Rows.Count + " shown)", EditorStyles.boldLabel);
            if (rows.Count == 0)
            {
                EditorGUILayout.LabelField("No incidents match the filters. (Empty is also the state before any tool emits through the shared core.)");
                EditorGUILayout.EndVertical();
                return;
            }

            tableScroll = EditorGUILayout.BeginScrollView(tableScroll, GUILayout.Height(220));
            foreach (HealthIncidentRow row in rows)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Select", GUILayout.Width(52)))
                {
                    SelectRow(row);
                }
                string marker = row.SignatureId == selectedSignature ? "> " : "";
                EditorGUILayout.LabelField(marker + row.SignatureId + "  " + IncidentSeverityText.Label(row.Severity)
                    + "  " + row.Status + "  " + row.OwningSystem + "  x" + row.OccurrenceCount
                    + " (today x" + row.OccurrencesToday + ")  day " + row.FirstSeenDay + "-" + row.LastSeenDay
                    + "  " + IssueRegisterStatusText.Label(row.RegisterStatus) + (row.IsRegistered ? "" : " (unregistered)"),
                    EditorStyles.wordWrappedLabel);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawSelectedDetail(HealthControlCenterSnapshot snapshot, IncidentWarningAuthority authority)
        {
            if (string.IsNullOrEmpty(selectedSignature))
            {
                return;
            }

            SimulationIncident incident = authority.TryGetIncident(selectedSignature);
            if (incident == null)
            {
                EditorGUILayout.HelpBox("Selected incident " + selectedSignature + " is no longer retained (evicted or dropped).", MessageType.Warning);
                return;
            }

            HealthIncidentRow row = null;
            foreach (HealthIncidentRow candidate in snapshot.Rows)
            {
                if (candidate.SignatureId == incident.SignatureId)
                {
                    row = candidate;
                    break;
                }
            }

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Selected: " + incident.SignatureId, EditorStyles.boldLabel);
            detailScroll = EditorGUILayout.BeginScrollView(detailScroll, GUILayout.Height(260));
            EditorGUILayout.LabelField("Summary", incident.Summary, EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("Severity", IncidentSeverityText.Label(incident.Severity) + " (first: " + IncidentSeverityText.Label(incident.FirstSeverity) + ")");
            EditorGUILayout.LabelField("Status", incident.Status.ToString());
            EditorGUILayout.LabelField("Owning system", incident.OwningSystem);
            EditorGUILayout.LabelField("Recovery authority", incident.RecoveryAuthority);
            EditorGUILayout.LabelField("Wait state", incident.WaitState);
            EditorGUILayout.LabelField("Retry boundary", incident.RetryBoundary);
            EditorGUILayout.LabelField("Cause class", RefusalCauseClassifier.Label(incident.CauseClass) + (string.IsNullOrEmpty(incident.CauseEvidence) ? "" : " — " + incident.CauseEvidence), EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("Occurrences", incident.OccurrenceCount + " total; day span " + incident.FirstSeenDay + "-" + incident.LastSeenDay
                + (row != null && row.DaysStanding >= 0 ? "; standing " + row.DaysStanding + "d" : ""));
            EditorGUILayout.LabelField("Last task / person / household", incident.LastTaskId + " / " + incident.LastPersonId + " / " + incident.LastHouseholdId);
            EditorGUILayout.LabelField("Last business / building", incident.LastBusinessId + " / " + incident.LastBuildingId);
            if (incident.PhysicalCapabilityBlockers.Count > 0)
                EditorGUILayout.LabelField("Physical-capability blockers", string.Join("; ", incident.PhysicalCapabilityBlockers), EditorStyles.wordWrappedLabel);
            if (incident.EvidenceReferences.Count > 0)
                EditorGUILayout.LabelField("Evidence", string.Join("; ", incident.EvidenceReferences), EditorStyles.wordWrappedLabel);
            if (incident.RelatedIncidentIds.Count > 0)
                EditorGUILayout.LabelField("Related incidents", string.Join(", ", incident.RelatedIncidentIds), EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("Status history", EditorStyles.boldLabel);
            int historyStart = Math.Max(0, incident.StatusHistory.Count - 5);
            for (int i = historyStart; i < incident.StatusHistory.Count; i++)
            {
                IncidentStatusChange change = incident.StatusHistory[i];
                EditorGUILayout.LabelField("  day " + change.DayIndex + ": " + change.FromStatus + " -> " + change.ToStatus
                    + (string.IsNullOrEmpty(change.Note) ? "" : " (" + change.Note + ")"), EditorStyles.wordWrappedLabel);
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            closeDisposition = EditorGUILayout.TextField("Close disposition", closeDisposition);
            using (new EditorGUI.DisabledScope(incident.Status == IncidentStatus.Closed || incident.Status == IncidentStatus.Resolved))
            {
                if (GUILayout.Button("Close incident", GUILayout.Width(120)))
                {
                    if (authority.CloseIncident(incident.SignatureId, snapshot.DayIndex, -1, closeDisposition))
                    {
                        status = "Closed " + incident.SignatureId + " (" + closeDisposition + "); [Incident][CLOSED] emitted (see Console).";
                    }
                    else
                    {
                        status = "Could not close " + incident.SignatureId + " (already resolved/closed, or authority disabled).";
                    }
                }
            }
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(incident.LastTaskId)))
            {
                if (GUILayout.Button("Investigate task", GUILayout.Width(120)))
                {
                    IncidentInvestigatorWindow.OpenForTask(incident.LastTaskId);
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("Issue register (interim — Lane 11 builds the §22 register)", EditorStyles.boldLabel);
            registerStatusSelection = EditorGUILayout.Popup("Register status", registerStatusSelection, RegisterStatusOptions());
            registerAuthority = EditorGUILayout.TextField("Owning authority", registerAuthority);
            registerNextAction = EditorGUILayout.TextField("Next action", registerNextAction);
            registerEvidence = EditorGUILayout.TextField("Evidence reference", registerEvidence);
            if (GUILayout.Button("Save register entry"))
            {
                register.SetStatus(incident.SignatureId, (IssueRegisterStatus)registerStatusSelection,
                    registerAuthority, registerNextAction, registerEvidence);
                SaveRegister();
                status = "Register entry saved for " + incident.SignatureId + ": "
                    + IssueRegisterStatusText.Label((IssueRegisterStatus)registerStatusSelection) + ".";
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawSnapshotCompare()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Occurrence snapshot compare (repair vs suppression, per signature)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Save an occurrence snapshot after a run, then compare a later snapshot against it. A decrease is shown per signature: it is a repair only when the fix is evidenced, otherwise it is a suppression to investigate (W R-14).", MessageType.None);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Save occurrence snapshot..."))
            {
                string path = EditorUtility.SaveFilePanel("Save occurrence snapshot", "", "health-snapshot.txt", "txt");
                if (!string.IsNullOrEmpty(path))
                {
                    IncidentWarningAuthority authority = IncidentDiagnostics.SharedAuthority;
                    HealthOccurrenceSnapshot snapshot = SimulationHealthControlCenter.CaptureOccurrences(authority, authority.CurrentDay);
                    File.WriteAllText(path, SimulationHealthControlCenter.WriteOccurrenceSnapshot(snapshot));
                    status = "Occurrence snapshot saved to " + path + ".";
                }
            }
            if (GUILayout.Button("Compare with snapshot..."))
            {
                string path = EditorUtility.OpenFilePanel("Compare with occurrence snapshot", "", "txt");
                if (!string.IsNullOrEmpty(path))
                {
                    IncidentWarningAuthority authority = IncidentDiagnostics.SharedAuthority;
                    HealthOccurrenceSnapshot before;
                    if (!SimulationHealthControlCenter.TryParseOccurrenceSnapshot(File.ReadAllText(path), out before))
                    {
                        status = "Could not parse snapshot file " + path + " — not a health occurrence snapshot.";
                    }
                    else
                    {
                        HealthOccurrenceSnapshot after = SimulationHealthControlCenter.CaptureOccurrences(authority, authority.CurrentDay);
                        snapshotDiff = SimulationHealthControlCenter.DiffOccurrences(before, after);
                        int decreased = 0;
                        foreach (HealthSignatureDelta delta in snapshotDiff)
                        {
                            if (delta.Kind == HealthSignatureChangeKind.Decreased) decreased++;
                        }
                        snapshotDiffNote = "Compared day " + before.DayIndex + " (" + before.TotalOccurrences + " occurrences) with day "
                            + after.DayIndex + " (" + after.TotalOccurrences + " occurrences): " + snapshotDiff.Count
                            + " signature(s), " + decreased + " decreased.";
                        status = snapshotDiffNote;
                    }
                }
            }
            EditorGUILayout.EndHorizontal();
            if (snapshotDiff != null)
            {
                EditorGUILayout.LabelField(snapshotDiffNote, EditorStyles.wordWrappedLabel);
                foreach (HealthSignatureDelta delta in snapshotDiff)
                {
                    string line = delta.SignatureId + "  " + delta.BeforeOccurrences + " -> " + delta.AfterOccurrences
                        + " (" + (delta.Delta >= 0 ? "+" : "") + delta.Delta + ")  " + delta.Kind;
                    if (delta.Kind == HealthSignatureChangeKind.Decreased)
                    {
                        line += " — decrease: repair or suppression? Check the fix evidence.";
                    }
                    EditorGUILayout.LabelField(line, EditorStyles.wordWrappedLabel);
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawUnreadNotes()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Recorded as unread (not invented)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("No-worker escalation (C R-14): the Decision-11 thresholds (warn ~1d / escalate 3d / exception 7d) were not located in source. The Center does not implement them; the 7-day sweep is the incident-level standing horizon only.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("[Incident][CLOSED] emitter (E W-5): verified missing in this lane and implemented on the authority (CloseIncident). The household system still emits [Incident][RESOLVED] (info) when a need closes.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("§22 issue register: Lane 11 work. The register view above is the interim per-signature status store, persisted in EditorPrefs.", EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndVertical();
        }

        private void SelectRow(HealthIncidentRow row)
        {
            selectedSignature = row.SignatureId;
            IssueRegisterEntry entry = register.TryGetEntry(row.SignatureId);
            registerStatusSelection = (int)(entry != null ? entry.Status : IssueRegisterStatus.Open);
            registerAuthority = entry != null ? entry.OwningAuthority : "";
            registerNextAction = entry != null ? entry.NextAction : "";
            registerEvidence = entry != null ? entry.EvidenceReference : "";
            closeDisposition = "fixed";
        }

        private HealthControlCenterFilters BuildFilters()
        {
            var filters = new HealthControlCenterFilters();
            if (severityFilter > 0) filters.Severity = (IncidentSeverity)(severityFilter - 1);
            if (!string.IsNullOrWhiteSpace(systemFilter)) filters.OwningSystem = systemFilter.Trim();
            if (!string.IsNullOrWhiteSpace(entityFilter)) filters.EntityId = entityFilter.Trim();
            if (causeFilter > 0) filters.CauseClass = (RefusalCauseClass)(causeFilter - 1);
            if (registerFilter > 0) filters.RegisterStatus = (IssueRegisterStatus)(registerFilter - 1);
            if (!string.IsNullOrWhiteSpace(textFilter)) filters.Text = textFilter.Trim();
            return filters;
        }

        private static string[] SeverityOptions()
        {
            return new[] { "All", "UNKNOWN", "INFORMATION", "WARNING", "CRITICAL" };
        }

        private static string[] CauseOptions()
        {
            return new[]
            {
                "All", "unclassified", "destination-missing", "destination-inaccessible",
                "destination-occupied-or-reserved", "worker-cannot-perform",
                "other-obligation-takes-priority", "origin-invalid",
            };
        }

        private static string[] RegisterOptions()
        {
            return new[]
            {
                "All", "OPEN", "INVESTIGATING", "IMPLEMENTED-UNVERIFIED",
                "VERIFIED", "EXPLAINED-LEGITIMATE", "BLOCKED WITH REASON",
            };
        }

        private static string[] RegisterStatusOptions()
        {
            return new[]
            {
                "OPEN", "INVESTIGATING", "IMPLEMENTED-UNVERIFIED",
                "VERIFIED", "EXPLAINED-LEGITIMATE", "BLOCKED WITH REASON",
            };
        }

        private TraceCompleteness ReadTraceCompleteness()
        {
            var completeness = new TraceCompleteness { HasTrace = false };
            try
            {
                SimulationSystemsHub hub = UnityEngine.Object.FindFirstObjectByType<SimulationSystemsHub>();
                NpcActivityTraceRecorder trace = hub != null ? hub.ActivityTrace : null;
                if (trace == null)
                {
                    return completeness;
                }

                completeness.HasTrace = true;
                completeness.RetainedSessionEvents = trace.Events.Count;
                completeness.DroppedSessionEvents = trace.DroppedSessionEvents;
                completeness.DroppedHistoryEvents = trace.DroppedHistoryEvents;
                completeness.AnomalyCount = trace.Anomalies.Count;
            }
            catch (Exception)
            {
                // Diagnostics must never break the window: no trace, no completeness.
                completeness.HasTrace = false;
            }

            return completeness;
        }

        private void ExportAuthority(IncidentWarningAuthority authority, bool jsonLines)
        {
            string path = EditorUtility.SaveFilePanel(jsonLines ? "Export incidents (JSON lines)" : "Export incidents (readable summary)",
                "", jsonLines ? "incidents.jsonl" : "incidents.txt", jsonLines ? "jsonl" : "txt");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            using (var writer = new StreamWriter(path, false))
            {
                if (jsonLines)
                {
                    authority.ExportJsonLines(writer);
                }
                else
                {
                    authority.ExportSummary(writer);
                }
            }

            status = "Exported " + authority.RetainedCount + " retained incident(s) to " + path + ".";
        }

        private void LoadThresholds()
        {
            IncidentAuthoritySettings defaults = IncidentAuthoritySettings.Defaults;
            maxRetained = EditorPrefs.GetInt(PrefsRetained, defaults.MaxRetainedIncidents);
            maxWarningsPerDay = EditorPrefs.GetInt(PrefsWarningsPerDay, defaults.MaxWarningsPerDay);
            maxWarningsPerSignaturePerDay = EditorPrefs.GetInt(PrefsWarningsPerSignature, defaults.MaxWarningsPerSignaturePerDay);
            escalationHorizonDays = EditorPrefs.GetInt(PrefsEscalationHorizon, defaults.EscalationHorizonDays);
            IncidentWarningAuthority authority = IncidentDiagnostics.SharedAuthority;
            IncidentAuthoritySettings settings = authority.Settings;
            settings.MaxRetainedIncidents = Math.Max(1, maxRetained);
            settings.MaxWarningsPerDay = Math.Max(0, maxWarningsPerDay);
            settings.MaxWarningsPerSignaturePerDay = Math.Max(1, maxWarningsPerSignaturePerDay);
            settings.EscalationHorizonDays = Math.Max(1, escalationHorizonDays);
            authority.Settings = settings;
        }

        private void SaveThresholds()
        {
            EditorPrefs.SetInt(PrefsRetained, maxRetained);
            EditorPrefs.SetInt(PrefsWarningsPerDay, maxWarningsPerDay);
            EditorPrefs.SetInt(PrefsWarningsPerSignature, maxWarningsPerSignaturePerDay);
            EditorPrefs.SetInt(PrefsEscalationHorizon, escalationHorizonDays);
        }

        private void LoadRegister()
        {
            string saved = EditorPrefs.GetString(PrefsRegister, string.Empty);
            if (string.IsNullOrEmpty(saved))
            {
                return;
            }

            string[] lines = saved.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                string[] parts = SplitEscaped(line);
                if (parts == null || parts.Length != 5)
                {
                    continue;
                }

                int statusValue;
                if (!int.TryParse(parts[1], out statusValue)
                    || statusValue < 0 || statusValue > (int)IssueRegisterStatus.BlockedWithReason)
                {
                    continue;
                }

                register.SetStatus(parts[0], (IssueRegisterStatus)statusValue, parts[2], parts[3], parts[4]);
            }
        }

        private void SaveRegister()
        {
            var builder = new System.Text.StringBuilder();
            foreach (IssueRegisterEntry entry in register.Entries)
            {
                builder.Append(Escape(entry.SignatureId)).Append('|')
                    .Append((int)entry.Status).Append('|')
                    .Append(Escape(entry.OwningAuthority)).Append('|')
                    .Append(Escape(entry.NextAction)).Append('|')
                    .Append(Escape(entry.EvidenceReference)).Append('\n');
            }

            EditorPrefs.SetString(PrefsRegister, builder.ToString());
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace("\\", "\\\\").Replace("|", "\\|").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        private static string[] SplitEscaped(string line)
        {
            var parts = new List<string>();
            var current = new System.Text.StringBuilder();
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '\\' && i + 1 < line.Length)
                {
                    char next = line[i + 1];
                    if (next == '|') { current.Append('|'); i++; }
                    else if (next == 'n') { current.Append('\n'); i++; }
                    else if (next == 'r') { current.Append('\r'); i++; }
                    else if (next == '\\') { current.Append('\\'); i++; }
                    else { current.Append(c); }
                }
                else if (c == '|')
                {
                    parts.Add(current.ToString());
                    current.Length = 0;
                }
                else
                {
                    current.Append(c);
                }
            }

            parts.Add(current.ToString());
            return parts.ToArray();
        }
    }
}
#endif
