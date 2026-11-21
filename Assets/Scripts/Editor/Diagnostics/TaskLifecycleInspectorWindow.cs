// Lane 4 (Muse Pass, Nov 20 2026) — Task and Obligation Lifecycle
// Inspector window (Tool 03). Menu: Land & Ledgers/Diagnostics/Task Lifecycle Inspector.
//
// Shows the full lifecycle of every obligation type — creation to
// recovery including Save/Continue — so the owner can see why a task is
// where it is without reading source. The lifecycle view complements
// the Incident Investigator (Tool 01, the causal view): this window
// shows states, transitions, stall thresholds, reservations and
// refusals; the Investigator names the earliest confirmed blocker.
//
// Observation only: it assigns no work, changes no routes, and moves
// no goods, money, food, transport or labour. TaskAuthority stays the
// only task system.
//
// SURFACE, DO NOT FIX: the Queued gap (no sweep covers Queued tasks)
// and the F6-2 post-load strand are shown here and repaired in Lane 9d;
// the missing Firewood point behind the fuel-handling strand is Lane 9c
// (Codex/scene-side); the missing furnishing.install outcome resolver
// is Lane 9f. This window names the repair owner for each strand — it
// does not perform the repair.
//
// Verification: this file is UnityEditor-bound and CANNOT join the
// Roslyn harness closure. It is verified with ParseCheck only (must
// print PARSE CLEAN). The window cannot be opened without Unity here;
// the lifecycle logic it displays lives in TaskLifecycleInspector
// (Core, harness tested) and the proving cases run on recorded run /
// source evidence, not a live run — no live run exists on this HEAD.
//
// Live-data limits, stated plainly: TaskAuthority prunes terminal
// tasks, so the live task list covers OPEN tasks only; terminal
// history comes from the trace recorder (bounded pre-incident capture).
// Progress times come from the recorder's retained events — a task with
// no retained events shows "no retained progress time" and is
// classified UNKNOWN rather than guessed.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using LandLedgers.Diagnostics;
using LandLedgers.Economy;
using LandLedgers.Economy.Freight;
using LandLedgers.FirstLedger;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Tasks;
using LandLedgers.Time;
using UnityEditor;
using UnityEngine;

namespace LandLedgers.Editor.Diagnostics
{
    public sealed class TaskLifecycleInspectorWindow : EditorWindow
    {
        private string taskId = "";
        private bool showIncidentsOnly;
        private List<LifecycleAssessment> liveTasks = new List<LifecycleAssessment>();
        private List<LifecycleAssessment> liveFreight = new List<LifecycleAssessment>();
        private LifecycleAssessment selected;
        private LifecycleAssessment proving;
        private Vector2 scroll;
        private string status = "No inspection run yet.";

        [MenuItem("Land & Ledgers/Diagnostics/Task Lifecycle Inspector")]
        public static void Open()
        {
            TaskLifecycleInspectorWindow window = GetWindow<TaskLifecycleInspectorWindow>("Task Lifecycle Inspector");
            window.minSize = new Vector2(760, 560);
            window.Show();
        }

        /// <summary>Open the Inspector pre-targeted at one task (used by the Incident Investigator's jump-to).</summary>
        public static void OpenForTask(string taskIdToInspect)
        {
            TaskLifecycleInspectorWindow window = GetWindow<TaskLifecycleInspectorWindow>("Task Lifecycle Inspector");
            window.minSize = new Vector2(760, 560);
            window.taskId = taskIdToInspect ?? string.Empty;
            window.Show();
            window.InspectLive();
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Task and Obligation Lifecycle Inspector", EditorStyles.largeLabel);
            EditorGUILayout.HelpBox("Observes TaskAuthority and the freight/furniture authorities. It never assigns work, changes routes, or alters simulation state. Stall thresholds are per-state (InProgress 6h, Assigned/ResourcesReserved 12h, WaitingOnDependency/WaitingOnPassiveProcess 48h, other incl. Queued 72h); severity comes from circumstances, never from elapsed time alone.", MessageType.Info);

            SimulationSystemsHub hub = UnityEngine.Object.FindFirstObjectByType<SimulationSystemsHub>();
            TimeManager time = UnityEngine.Object.FindFirstObjectByType<TimeManager>();
            NpcActivityTraceRecorder trace = hub != null ? hub.ActivityTrace : null;

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Live lifecycle (open tasks + freight)", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(hub == null))
            {
                if (GUILayout.Button("Inspect live")) InspectLive();
            }
            showIncidentsOnly = EditorGUILayout.ToggleLeft("Incidents only", showIncidentsOnly, GUILayout.Width(130));
            if (GUILayout.Button("Emit incidents to core", GUILayout.Width(170))) EmitLiveIncidents();
            EditorGUILayout.EndHorizontal();
            if (hub == null)
                EditorGUILayout.HelpBox("No live hub: enter Play Mode in the Main Scene to connect to SimulationSystemsHub. The proving cases below run on recorded run / source evidence and do not need a live hub.", MessageType.Warning);
            else
                EditorGUILayout.LabelField("Live", liveTasks.Count + " open task(s), " + liveFreight.Count + " freight obligation(s) inspected"
                    + (trace == null ? " (no trace recorder — progress times unavailable, stalls classify UNKNOWN)" : " (progress times from the trace recorder)"));
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Inspect one task", EditorStyles.boldLabel);
            taskId = EditorGUILayout.TextField("Task ID (e.g. T9)", taskId);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(taskId)))
            {
                if (GUILayout.Button("Inspect task")) InspectOneTask();
                if (GUILayout.Button("Investigate (causal view)")) IncidentInvestigatorWindow.OpenForTask(taskId.Trim());
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Known strands (recorded run / source evidence — no live run on this HEAD)", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Fuel-handling strand (C R-18)")) { proving = TaskLifecycleInspector.InspectKnownFuelHandlingStrand(); status = "Proving case 1: interrupted domestic.fuel-handling tasks T9/T10/T170 (60-day) and T8/T9/T11 (5-day), retained-run record."; }
            if (GUILayout.Button("F6-2 request strand")) { proving = TaskLifecycleInspector.InspectKnownFreightRequestStrand(); status = "Proving case 2: terminal-Accepted freight request with no reopen path (F6-2, narrowed)."; }
            if (GUILayout.Button("Staging wait (C R-8)")) { proving = TaskLifecycleInspector.InspectKnownStagingWait(); status = "Proving case 3: shipment waiting in staging with no deadline and no liveness bound."; }
            if (GUILayout.Button("Furniture install strand")) { proving = TaskLifecycleInspector.InspectKnownFurnitureInstallStrand(); status = "Proving case 4: paid furniture order whose furnishing.install task completed with no outcome resolver."; }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            EditorGUILayout.LabelField(status, EditorStyles.wordWrappedLabel);
            DrawAssessmentList("Open tasks", liveTasks);
            DrawAssessmentList("Freight obligations", liveFreight);
            if (proving != null) DrawAssessment("Proving case", proving, hub, time);
            if (selected != null) DrawAssessment("Selected", selected, hub, time);
            EditorGUILayout.EndScrollView();
        }

        private void InspectLive()
        {
            IncidentDiagnostics.WireInvestigator();
            SimulationSystemsHub hub = UnityEngine.Object.FindFirstObjectByType<SimulationSystemsHub>();
            TimeManager time = UnityEngine.Object.FindFirstObjectByType<TimeManager>();
            NpcActivityTraceRecorder trace = hub != null ? hub.ActivityTrace : null;
            SimulationDrivers drivers = UnityEngine.Object.FindFirstObjectByType<SimulationDrivers>();
            double now = time != null ? time.AbsoluteGameSeconds : -1;
            int day = time != null ? time.CurrentAbsoluteDayIndex : -1;

            liveTasks = new List<LifecycleAssessment>();
            if (hub != null)
            {
                foreach (WorkTask task in hub.Tasks.GetAllOpenTasks())
                {
                    if (task == null) continue;
                    liveTasks.Add(TaskLifecycleInspector.Inspect(MapTask(task, trace, now, day)));
                }
            }

            liveFreight = new List<LifecycleAssessment>();
            if (drivers != null && drivers.FreightProduction != null && drivers.FreightProduction.ServiceAgreements != null)
            {
                FreightServiceAgreementBook book = drivers.FreightProduction.ServiceAgreements;
                List<LogisticsShipmentState> shipments = drivers.FreightProduction.AllShipments();
                foreach (FreightServiceRequest request in book.Requests)
                {
                    if (request == null) continue;
                    liveFreight.Add(TaskLifecycleInspector.Inspect(MapFreightRequest(request, book, shipments, now, day)));
                }
                foreach (LogisticsShipmentState shipment in shipments)
                {
                    if (shipment == null) continue;
                    liveFreight.Add(TaskLifecycleInspector.Inspect(MapFreightShipment(shipment, book, now, day)));
                }
            }

            LifecycleCoverage coverage = TaskLifecycleInspector.Summarize(Combined(liveTasks, liveFreight));
            status = "Inspected " + coverage.TotalAssessed + " obligation(s): " + coverage.IncidentCount + " incident(s). "
                + "Kinds with zero observations here: " + (coverage.KindsNotObserved.Count == 0 ? "none" : string.Join(", ", coverage.KindsNotObserved))
                + " — not observed is not a pass.";
        }

        private void InspectOneTask()
        {
            SimulationSystemsHub hub = UnityEngine.Object.FindFirstObjectByType<SimulationSystemsHub>();
            TimeManager time = UnityEngine.Object.FindFirstObjectByType<TimeManager>();
            NpcActivityTraceRecorder trace = hub != null ? hub.ActivityTrace : null;
            double now = time != null ? time.AbsoluteGameSeconds : -1;
            int day = time != null ? time.CurrentAbsoluteDayIndex : -1;
            string id = taskId.Trim();
            selected = null;
            foreach (LifecycleAssessment assessment in liveTasks)
            {
                if (assessment != null && string.Equals(assessment.ObligationId, id, StringComparison.Ordinal))
                {
                    selected = assessment;
                    break;
                }
            }
            if (selected == null && trace != null)
            {
                // No live open task with that id (terminal tasks are pruned
                // from TaskAuthority): inspect from the retained trace
                // events instead, so a pruned task still shows a lifecycle.
                selected = TaskLifecycleInspector.Inspect(MapTraceTask(id, trace, now, day));
            }
            status = selected != null
                ? "Inspected task " + id + " (" + selected.Disposition + ")."
                : "Task " + id + " was not found among live open tasks and has no retained trace events.";
        }

        private void EmitLiveIncidents()
        {
            IncidentDiagnostics.WireInvestigator();
            int emitted = 0;
            foreach (LifecycleAssessment assessment in Combined(liveTasks, liveFreight))
            {
                if (assessment == null || !assessment.IsIncident) continue;
                TaskLifecycleInspector.EmitToCore(assessment);
                emitted++;
            }
            status = "Emitted " + emitted + " lifecycle incident(s) to the Lane 1 core (deduped by signature; healthy and terminal-completed assessments are never emitted).";
        }

        private static List<LifecycleAssessment> Combined(List<LifecycleAssessment> first, List<LifecycleAssessment> second)
        {
            var combined = new List<LifecycleAssessment>(first.Count + second.Count);
            combined.AddRange(first);
            combined.AddRange(second);
            return combined;
        }

        private void DrawAssessmentList(string title, List<LifecycleAssessment> assessments)
        {
            if (assessments == null || assessments.Count == 0) return;
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            foreach (LifecycleAssessment assessment in assessments)
            {
                if (assessment == null) continue;
                if (showIncidentsOnly && !assessment.IsIncident) continue;
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(assessment.ObligationId + " (" + assessment.DefinitionId + ")", GUILayout.Width(220));
                EditorGUILayout.LabelField(assessment.Status, GUILayout.Width(110));
                EditorGUILayout.LabelField(assessment.Disposition.ToString(), GUILayout.Width(170));
                EditorGUILayout.LabelField(assessment.Severity.ToString(), GUILayout.Width(90));
                if (GUILayout.Button("Show", GUILayout.Width(60))) selected = assessment;
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawAssessment(string title, LifecycleAssessment item, SimulationSystemsHub hub, TimeManager time)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField(title + ": " + item.ObligationId + " — " + item.Disposition, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Kind", item.Kind.ToString());
            EditorGUILayout.LabelField("Status", item.Status);
            EditorGUILayout.LabelField("Severity", item.Severity + " — " + SeverityMeaning(item.Severity));
            EditorGUILayout.LabelField("Blocker class", item.BlockerClass.ToString());
            EditorGUILayout.LabelField("Stall threshold", item.StallThresholdSeconds >= 0 ? (item.StallThresholdSeconds / 3600d).ToString("0.##") + " simulated hours" : "(no per-state threshold applies)");
            EditorGUILayout.LabelField("Measured no-progress", item.StalledSeconds >= 0 ? (item.StalledSeconds / 3600d).ToString("0.0") + " simulated hours" : "(no retained progress time — not guessed)");
            EditorGUILayout.LabelField("Assignee", item.HasAssignee ? "assigned" : "NO ASSIGNEE — distinguished from 'assignee idle'");
            EditorGUILayout.LabelField("Explanation", item.Explanation, EditorStyles.wordWrappedLabel);
            if (!string.IsNullOrWhiteSpace(item.EarliestBlocker))
                EditorGUILayout.LabelField("Earliest confirmed blocker", item.EarliestBlocker + (item.EarliestBlockerSeconds >= 0 ? " (at " + item.EarliestBlockerSeconds.ToString("0.#") + "s)" : ""), EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("Responsible authority", item.ResponsibleAuthority, EditorStyles.wordWrappedLabel);
            if (!string.IsNullOrWhiteSpace(item.AuthoritySourcePath))
                EditorGUILayout.LabelField("Authority source", item.AuthoritySourcePath + (item.AuthoritySourceLine > 0 ? " :" + item.AuthoritySourceLine : ""));
            if (!string.IsNullOrWhiteSpace(item.RepairOwner))
                EditorGUILayout.LabelField("Repair owner", item.RepairOwner, EditorStyles.wordWrappedLabel);
            if (!string.IsNullOrWhiteSpace(item.TerminalDisposition))
                EditorGUILayout.LabelField("Terminal disposition", item.TerminalDisposition, EditorStyles.wordWrappedLabel);
            if (item.MissingEvidence != null && item.MissingEvidence.Count > 0)
                EditorGUILayout.HelpBox("Missing evidence:\n- " + string.Join("\n- ", item.MissingEvidence), MessageType.Warning);
            if (!string.IsNullOrWhiteSpace(item.SourceNote))
                EditorGUILayout.LabelField("Source note", item.SourceNote, EditorStyles.wordWrappedLabel);
            DrawJumps(item, hub);
            EditorGUILayout.EndVertical();
        }

        private void DrawJumps(LifecycleAssessment item, SimulationSystemsHub hub)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Jump to", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(item.Kind != ObligationKind.Task || string.IsNullOrWhiteSpace(item.ObligationId)))
            {
                if (GUILayout.Button("Investigate (causal view)")) IncidentInvestigatorWindow.OpenForTask(item.ObligationId);
            }
            using (new EditorGUI.DisabledScope(item.Kind != ObligationKind.Task || string.IsNullOrWhiteSpace(item.ObligationId)))
            {
                if (GUILayout.Button("Jump to task in NPC Monitor")) OpenMonitorFor(item);
            }
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(item.AuthoritySourcePath)))
            {
                if (GUILayout.Button("Jump to authority source")) OpenAuthoritySource(item);
            }
            EditorGUILayout.EndHorizontal();
            // Jump-to status: NPC framing lives in the Investigator
            // window; the business inspector does not exist yet (Lane 5,
            // Tool 04) — the authority name and source above are the
            // interim route to a freight/furniture obligation's owner.
            EditorGUILayout.HelpBox("Jump to business: not available — no business inspector exists yet (Lane 5, Tool 04). Use the authority name and source above.", MessageType.None);
            EditorGUILayout.EndVertical();
        }

        private static string SeverityMeaning(IncidentSeverity severity)
        {
            switch (severity)
            {
                case IncidentSeverity.Information: return "legitimate wait or below the stall threshold — not an incident.";
                case IncidentSeverity.Warning: return "idle without a valid cause, or a defect blocks the work.";
                case IncidentSeverity.Critical: return "integrity violation or a failure with no recovery path.";
                default: return "insufficient evidence — the missing evidence is named above.";
            }
        }

        private void OpenMonitorFor(LifecycleAssessment item)
        {
            NpcActivityMonitorWindow.Open();
        }

        private static void OpenAuthoritySource(LifecycleAssessment item)
        {
            string path = item.AuthoritySourcePath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && item.AuthoritySourceLine > 0)
                {
                    UnityEditor.CodeEditor.CodeEditor.CurrentEditor.OpenProject(path, item.AuthoritySourceLine, 1);
                    return;
                }
                UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                if (asset != null)
                {
                    EditorGUIUtility.PingObject(asset);
                    Selection.activeObject = asset;
                }
                else if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                    if (script != null) AssetDatabase.OpenAsset(script);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Task Lifecycle Inspector could not open authority source " + path + ": " + ex.Message);
            }
        }

        // ---- Live-state mapping (window only; the core never sees live types) ----

        private static ObligationLifecycleSnapshot MapTask(WorkTask task, NpcActivityTraceRecorder trace, double now, int day)
        {
            string id = task.TaskId.ToString();
            var snapshot = new ObligationLifecycleSnapshot
            {
                ObligationId = id,
                Kind = ObligationKind.Task,
                DefinitionId = task.DefinitionId ?? string.Empty,
                Status = task.Status.ToString(),
                NowSimSeconds = now,
                DayIndex = day,
                OwnerId = task.OwnerId.ToString(),
                HouseholdId = IncidentInvestigator.ResolveHouseholdId(-1, task.OwnerId.ToString()),
                PersonId = task.HasAssignee ? "P" + task.AssigneeId.Id : string.Empty,
                PersonNumericId = task.HasAssignee ? task.AssigneeId.Id : -1,
                WorkedMinutes = task.MinutesWorked,
                RemainingMinutes = task.MinutesRemaining,
                PlannedMinutes = task.PlannedMinutes,
                InterruptionCount = task.InterruptionCount,
                ReservationId = task.ReservationHandleId ?? string.Empty,
                HasReservation = !string.IsNullOrWhiteSpace(task.ReservationHandleId),
                HasAssignee = task.HasAssignee,
                HasLivenessBound = task.Status == TaskStatus.InProgress,
            };
            ApplyTraceTimes(snapshot, trace, id);
            return snapshot;
        }

        private static ObligationLifecycleSnapshot MapTraceTask(string taskId, NpcActivityTraceRecorder trace, double now, int day)
        {
            var snapshot = new ObligationLifecycleSnapshot
            {
                ObligationId = taskId,
                Kind = ObligationKind.Task,
                NowSimSeconds = now,
                DayIndex = day,
            };
            ApplyTraceTimes(snapshot, trace, taskId);
            return snapshot;
        }

        /// <summary>
        /// Progress times come from the recorder's retained events only.
        /// A task with no retained events keeps -1 times: the Inspector
        /// classifies it UNKNOWN instead of guessing a stall duration.
        /// </summary>
        private static void ApplyTraceTimes(ObligationLifecycleSnapshot snapshot, NpcActivityTraceRecorder trace, string taskId)
        {
            if (trace == null || trace.Events == null) return;
            double first = -1;
            double lastProgress = -1;
            string lastTransition = string.Empty;
            string lastReason = string.Empty;
            string lastRecovery = string.Empty;
            string lastStatus = string.Empty;
            string lastDefinition = string.Empty;
            string lastOwner = string.Empty;
            int lastPerson = -1;
            int lastHousehold = -1;
            foreach (NpcActivityTraceEvent item in trace.Events)
            {
                if (item == null || !string.Equals(item.taskId, taskId, StringComparison.Ordinal)) continue;
                if (item.simulatedSeconds < 0) continue;
                if (first < 0 || item.simulatedSeconds < first) first = item.simulatedSeconds;
                bool isAnomaly = string.Equals(item.eventType, "anomaly", StringComparison.OrdinalIgnoreCase);
                if (!isAnomaly && item.simulatedSeconds > lastProgress)
                {
                    lastProgress = item.simulatedSeconds;
                    lastTransition = item.transition ?? string.Empty;
                    lastReason = item.reason ?? string.Empty;
                    lastRecovery = item.recoveryState ?? string.Empty;
                    lastStatus = item.taskStatus ?? string.Empty;
                    lastDefinition = item.taskDefinitionId ?? string.Empty;
                    lastOwner = item.taskOwnerId ?? string.Empty;
                    lastPerson = item.personNumericId;
                    lastHousehold = item.householdId;
                }
            }
            snapshot.FirstObservedSimSeconds = first;
            snapshot.LastProgressSimSeconds = lastProgress;
            snapshot.StateEnteredSimSeconds = lastProgress;
            if (!string.IsNullOrWhiteSpace(lastTransition)) snapshot.LastTransition = lastTransition;
            if (!string.IsNullOrWhiteSpace(lastReason)) snapshot.RefusalReason = lastReason;
            if (!string.IsNullOrWhiteSpace(lastRecovery)) snapshot.RecoveryState = lastRecovery;
            if (string.IsNullOrWhiteSpace(snapshot.Status) && !string.IsNullOrWhiteSpace(lastStatus)) snapshot.Status = lastStatus;
            if (string.IsNullOrWhiteSpace(snapshot.DefinitionId) && !string.IsNullOrWhiteSpace(lastDefinition)) snapshot.DefinitionId = lastDefinition;
            if (string.IsNullOrWhiteSpace(snapshot.OwnerId) && !string.IsNullOrWhiteSpace(lastOwner)) snapshot.OwnerId = lastOwner;
            if (snapshot.PersonNumericId < 0) snapshot.PersonNumericId = lastPerson;
            if (snapshot.HouseholdId < 0) snapshot.HouseholdId = IncidentInvestigator.ResolveHouseholdId(lastHousehold, lastOwner);
        }

        private static ObligationLifecycleSnapshot MapFreightRequest(
            FreightServiceRequest request, FreightServiceAgreementBook book,
            List<LogisticsShipmentState> shipments, double now, int day)
        {
            FreightServiceAgreement agreement = null;
            if (book != null && !string.IsNullOrWhiteSpace(request.AgreementId))
            {
                foreach (FreightServiceAgreement candidate in book.Agreements)
                {
                    if (candidate != null && string.Equals(candidate.AgreementId, request.AgreementId, StringComparison.Ordinal))
                    {
                        agreement = candidate;
                        break;
                    }
                }
            }
            if (agreement == null && book != null)
            {
                foreach (FreightServiceAgreement candidate in book.Agreements)
                {
                    if (candidate != null && string.Equals(candidate.RequestId, request.RequestId, StringComparison.Ordinal))
                    {
                        agreement = candidate;
                        break;
                    }
                }
            }
            LogisticsShipmentState shipment = FindShipment(shipments, request);
            return new ObligationLifecycleSnapshot
            {
                ObligationId = request.RequestId ?? string.Empty,
                Kind = ObligationKind.FreightRequest,
                Label = "Freight service request",
                Status = request.Status.ToString(),
                NowSimSeconds = now,
                DayIndex = day,
                RefusalReason = request.RefusalReason ?? string.Empty,
                RelatedAgreementId = agreement != null ? agreement.AgreementId : (request.AgreementId ?? string.Empty),
                RelatedLotId = request.CargoLotId ?? string.Empty,
                RelatedShipmentId = shipment != null ? shipment.shipmentId : (request.ShipmentId ?? string.Empty),
                AgreementCancelled = agreement != null && agreement.Status == FreightAgreementStatus.Cancelled,
                LotCommitted = shipment != null && (shipment.sourceCommittedAtSchedule || shipment.loadApplied),
                HasReopenPath = request.Status != FreightServiceRequestStatus.Accepted,
                RecoveryAttempts = agreement != null ? agreement.DispatchTaskRecoveryAttempts : 0,
                RecoveryAttemptLimit = FreightServiceAgreement.MaximumDispatchTaskRecoveryAttempts,
                IsPreLoad = shipment != null && !shipment.loadApplied && !shipment.sourceCommittedAtSchedule && !shipment.physicalTransportBound,
                IsPostLoadCommitted = shipment != null && (shipment.loadApplied || shipment.sourceCommittedAtSchedule || shipment.physicalTransportBound),
            };
        }

        private static ObligationLifecycleSnapshot MapFreightShipment(
            LogisticsShipmentState shipment, FreightServiceAgreementBook book, double now, int day)
        {
            FreightServiceAgreement agreement = null;
            if (book != null && !string.IsNullOrWhiteSpace(shipment.freightServiceAgreementId))
            {
                foreach (FreightServiceAgreement candidate in book.Agreements)
                {
                    if (candidate != null && string.Equals(candidate.AgreementId, shipment.freightServiceAgreementId, StringComparison.Ordinal))
                    {
                        agreement = candidate;
                        break;
                    }
                }
            }
            bool preLoad = !shipment.loadApplied && !shipment.sourceCommittedAtSchedule && !shipment.physicalTransportBound;
            return new ObligationLifecycleSnapshot
            {
                ObligationId = shipment.shipmentId ?? string.Empty,
                Kind = ObligationKind.FreightShipment,
                Label = "Freight shipment",
                Status = shipment.state.ToString(),
                NowSimSeconds = now,
                DayIndex = day,
                DeadlineSimSeconds = -1,
                DeadlineKind = string.Empty,
                HasLivenessBound = false,
                RelatedAgreementId = shipment.freightServiceAgreementId ?? string.Empty,
                RelatedLotId = shipment.cargoLotId ?? string.Empty,
                AgreementCancelled = agreement != null && agreement.Status == FreightAgreementStatus.Cancelled,
                LotCommitted = shipment.sourceCommittedAtSchedule || shipment.loadApplied,
                RecoveryAttempts = agreement != null ? agreement.DispatchTaskRecoveryAttempts : 0,
                RecoveryAttemptLimit = FreightServiceAgreement.MaximumDispatchTaskRecoveryAttempts,
                IsPreLoad = preLoad,
                IsPostLoadCommitted = !preLoad,
            };
        }

        private static LogisticsShipmentState FindShipment(List<LogisticsShipmentState> shipments, FreightServiceRequest request)
        {
            if (shipments == null || request == null) return null;
            foreach (LogisticsShipmentState shipment in shipments)
            {
                if (shipment == null) continue;
                if (!string.IsNullOrWhiteSpace(request.ShipmentId)
                    && string.Equals(shipment.shipmentId, request.ShipmentId, StringComparison.Ordinal)) return shipment;
                if (!string.IsNullOrWhiteSpace(request.AgreementId)
                    && string.Equals(shipment.freightServiceAgreementId, request.AgreementId, StringComparison.Ordinal)) return shipment;
            }
            return null;
        }
    }
}
#endif
