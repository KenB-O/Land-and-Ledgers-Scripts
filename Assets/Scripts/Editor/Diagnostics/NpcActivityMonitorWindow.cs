#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using LandLedgers.Diagnostics;
using LandLedgers.FirstLedger;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Population;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace LandLedgers.Editor.Diagnostics
{
    public sealed class NpcActivityMonitorWindow : EditorWindow
    {
        private string personIds = "";
        private string householdIds = "";
        private string eventFilter = "";
        private NpcTraceLevel level = NpcTraceLevel.Detailed;
        private bool autoStallDetection = true;
        private bool autoSelectAnomalies = true;
        private double samplingInterval = 300;
        private int prehistoryLimit = 5000;
        private int retentionLimit = 20000;
        private Vector2 scroll;
        private Vector2 eventScroll;
        private int selectedEvent = -1;
        private string exportPath = "";
        private double nextWallSample;
        private NpcActivityTraceRecorder lastRecorder;

        [MenuItem("Land & Ledgers/Diagnostics/NPC Activity Monitor")]
        public static void Open()
        {
            NpcActivityMonitorWindow window = GetWindow<NpcActivityMonitorWindow>("NPC Activity Monitor");
            window.minSize = new Vector2(580, 500);
            window.Show();
        }

        private void OnEnable()
        {
            personIds = SessionState.GetString("LandLedgers.NpcTrace.Persons", personIds);
            householdIds = SessionState.GetString("LandLedgers.NpcTrace.Households", householdIds);
            level = (NpcTraceLevel)SessionState.GetInt("LandLedgers.NpcTrace.Level", (int)level);
            EditorApplication.update += UpdateTrace;
        }

        private void OnDisable()
        {
            EditorApplication.update -= UpdateTrace;
            PersistSelection();
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("NPC Activity Monitor", EditorStyles.largeLabel);
            EditorGUILayout.HelpBox("Observes the live task and population authorities. Recording never assigns work, changes routes, or alters simulation state.", MessageType.Info);

            SimulationSystemsHub hub = UnityEngine.Object.FindFirstObjectByType<SimulationSystemsHub>();
            TimeManager time = UnityEngine.Object.FindFirstObjectByType<TimeManager>();
            PopulationManager population = UnityEngine.Object.FindFirstObjectByType<PopulationManager>();
            NpcActivityTraceRecorder trace = hub != null ? hub.ActivityTrace : null;
            if (trace == null || hub == null)
            {
                EditorGUILayout.HelpBox("Enter Play Mode in the Main Scene to connect to SimulationSystemsHub.", MessageType.Warning);
                EditorGUILayout.EndScrollView();
                return;
            }
            if (lastRecorder != null && !ReferenceEquals(lastRecorder, trace)) trace.RecordAuthorityRestored(time != null ? time.CurrentAbsoluteDayIndex : -1);
            lastRecorder = trace;

            DrawSelection(population, trace);
            DrawControls(trace);
            DrawCurrentState(population, hub, time, trace);
            DrawAnomalies(trace);
            DrawTimeline(trace);
            EditorGUILayout.EndScrollView();
        }

        private void DrawSelection(PopulationManager population, NpcActivityTraceRecorder trace)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Targets", EditorStyles.boldLabel);
            personIds = EditorGUILayout.TextField("Person IDs (comma separated)", personIds);
            householdIds = EditorGUILayout.TextField("Household IDs (comma separated)", householdIds);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Use all living residents"))
            {
                personIds = population?.State?.people == null ? "" : string.Join(",", population.State.people
                    .Where(person => person != null && !person.isOffMap && person.deathDayIndex < 0).Select(person => person.id));
                householdIds = "";
            }
            if (GUILayout.Button("Use current household members"))
            {
                HashSet<int> selected = ParseIds(householdIds);
                if (selected.Count == 0) selected = ParseIds(personIds);
                if (population?.Memberships != null)
                    personIds = string.Join(",", selected.SelectMany(population.Memberships.GetActiveMembers).Distinct());
            }
            if (GUILayout.Button("Apply targets")) ApplySelection(trace, "developer selection");
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("Selected people", trace.SelectedPeople.Count == 0 ? "All people matching household filter" : string.Join(", ", trace.SelectedPeople.Select(id => "P" + id)));
            EditorGUILayout.LabelField("Selected households", trace.SelectedHouseholds.Count == 0 ? "None" : string.Join(", ", trace.SelectedHouseholds.Select(id => "H" + id)));
            EditorGUILayout.EndVertical();
        }

        private void DrawControls(NpcActivityTraceRecorder trace)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Recording", EditorStyles.boldLabel);
            level = (NpcTraceLevel)EditorGUILayout.EnumPopup("Trace level", level);
            autoStallDetection = EditorGUILayout.Toggle("Automatic stall detection", autoStallDetection);
            autoSelectAnomalies = EditorGUILayout.Toggle("Auto-select anomalous people", autoSelectAnomalies);
            samplingInterval = EditorGUILayout.DoubleField("Deep sample interval (simulated seconds)", samplingInterval);
            prehistoryLimit = EditorGUILayout.IntField("Prehistory event limit", prehistoryLimit);
            retentionLimit = EditorGUILayout.IntField("Session event limit", retentionLimit);
            trace.AutoStallDetection = autoStallDetection;
            trace.AutoSelectAnomalies = autoSelectAnomalies;
            trace.SampleIntervalSeconds = samplingInterval;
            trace.HistoryLimit = prehistoryLimit;
            trace.RetentionLimit = retentionLimit;
            EditorGUILayout.LabelField("State", !trace.Monitoring ? "Not monitoring" : trace.Recording ? "Recording" : "Monitoring (paused)");
            EditorGUILayout.LabelField("Session", string.IsNullOrEmpty(trace.SessionId) ? "(none)" : trace.SessionId);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Start monitoring + record"))
            {
                ApplySelection(trace, "developer selection");
                CaptureMetadata(trace);
                trace.BeginMonitoring();
                trace.StartRecording(level, true);
            }
            using (new EditorGUI.DisabledScope(!trace.Monitoring))
            {
                if (GUILayout.Button(trace.Recording ? "Pause" : "Resume")) trace.PauseRecording(trace.Recording);
                if (GUILayout.Button("Stop")) trace.StopRecording();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("Retained / dropped", $"{trace.Events.Count} / {trace.DroppedSessionEvents} session; {trace.DroppedHistoryEvents} prehistory");
            EditorGUILayout.BeginHorizontal();
            exportPath = EditorGUILayout.TextField("Export folder", exportPath);
            if (GUILayout.Button("…", GUILayout.Width(28))) exportPath = EditorUtility.OpenFolderPanel("NPC Activity Trace Export", exportPath, "");
            using (new EditorGUI.DisabledScope(!trace.Monitoring || string.IsNullOrWhiteSpace(exportPath)))
                if (GUILayout.Button("Export", GUILayout.Width(70))) Export(trace);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawCurrentState(PopulationManager population, SimulationSystemsHub hub, TimeManager time, NpcActivityTraceRecorder trace)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Live state", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Simulation time", time != null ? $"day {time.CurrentAbsoluteDayIndex}, {time.GameSecondsIntoDay:0}s into day, speed {time.CurrentSpeed}" : "unavailable");
            List<int> people = ResolvePeople(population, trace);
            LandLedgers.FirstLedger.PopulationPathingDirector pathing = UnityEngine.Object.FindFirstObjectByType<LandLedgers.FirstLedger.PopulationPathingDirector>();
            foreach (int id in people.Take(20))
            {
                PersonState person = population?.State?.people?.FirstOrDefault(item => item != null && item.id == id);
                if (person == null) continue;
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("P" + id + " " + person.DisplayName, GUILayout.Width(185));
                EditorGUILayout.LabelField("H" + (population.Memberships?.GetActiveHouseholdId(id) ?? person.householdId), GUILayout.Width(45));
                EditorGUILayout.LabelField(person.scheduleState.ToString(), GUILayout.Width(120));
                EditorGUILayout.LabelField("destination B" + person.currentDestinationBuildingId);
                if (pathing != null && pathing.TryGetMovementReadout(id, out string status, out float remaining, out float eta))
                    EditorGUILayout.LabelField(status + $"; ETA {eta:0}s", GUILayout.MinWidth(165));
                if (pathing != null && GUILayout.Button("Frame", GUILayout.Width(50))) FramePerson(pathing, id);
                EditorGUILayout.EndHorizontal();

                List<WorkTask> open = hub.Tasks.GetAllOpenTasks().Where(task => task != null && task.HasAssignee && task.AssigneeId.Id == id).ToList();
                foreach (WorkTask task in open)
                    EditorGUILayout.LabelField("  Task " + task.TaskId + " " + task.DefinitionId + " " + task.Status +
                        " work=" + task.MinutesWorked + "/" + task.PlannedMinutes + " owner=" + task.OwnerId +
                        (string.IsNullOrEmpty(task.CustomerRef) ? "" : " op=" + task.CustomerRef));
            }
            if (people.Count == 0) EditorGUILayout.HelpBox("No live person is selected or matches the active household filter.", MessageType.None);
            EditorGUILayout.EndVertical();
        }

        private void DrawAnomalies(NpcActivityTraceRecorder trace)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Detected anomalies", EditorStyles.boldLabel);
            if (trace.Anomalies.Count == 0) EditorGUILayout.LabelField("No trace anomalies have been detected.");
            for (int i = Math.Max(0, trace.Anomalies.Count - 30); i < trace.Anomalies.Count; i++)
            {
                NpcActivityTraceAnomaly anomaly = trace.Anomalies[i];
                EditorGUILayout.HelpBox($"{anomaly.category}: {anomaly.personId} H{anomaly.householdId} task={anomaly.taskId}\n{anomaly.reason}\nlast progress {anomaly.lastProgressSeconds:0}s; detected {anomaly.detectedAtSeconds:0}s", MessageType.Warning);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawTimeline(NpcActivityTraceRecorder trace)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Timeline", EditorStyles.boldLabel);
            eventFilter = EditorGUILayout.TextField("Search", eventFilter);
            eventScroll = EditorGUILayout.BeginScrollView(eventScroll, GUILayout.MinHeight(180));
            for (int i = 0; i < trace.Events.Count; i++)
            {
                NpcActivityTraceEvent item = trace.Events[i];
                string row = $"#{item.sequence} {item.simulatedSeconds:0}s day {item.dayIndex} {item.eventType}/{item.transition} {item.personId} H{item.householdId} task={item.taskId} {item.taskStatus}";
                if (!string.IsNullOrEmpty(eventFilter) && row.IndexOf(eventFilter, StringComparison.OrdinalIgnoreCase) < 0 &&
                    (item.reason ?? string.Empty).IndexOf(eventFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (GUILayout.Toggle(selectedEvent == i, row, "Button")) selectedEvent = i;
            }
            EditorGUILayout.EndScrollView();
            if (selectedEvent >= 0 && selectedEvent < trace.Events.Count)
                EditorGUILayout.TextArea(JsonUtility.ToJson(trace.Events[selectedEvent], true), GUILayout.MinHeight(90));
            EditorGUILayout.EndVertical();
        }

        private void UpdateTrace()
        {
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup < nextWallSample) return;
            nextWallSample = EditorApplication.timeSinceStartup + 0.2d;
            SimulationSystemsHub hub = UnityEngine.Object.FindFirstObjectByType<SimulationSystemsHub>();
            TimeManager time = UnityEngine.Object.FindFirstObjectByType<TimeManager>();
            PopulationManager population = UnityEngine.Object.FindFirstObjectByType<PopulationManager>();
            if (hub == null || time == null) return;
            NpcActivityTraceRecorder trace = hub.ActivityTrace;
            if (!trace.Monitoring) return;
            trace.SetCurrentDay(time.CurrentAbsoluteDayIndex);
            LandLedgers.FirstLedger.PopulationPathingDirector pathing = UnityEngine.Object.FindFirstObjectByType<LandLedgers.FirstLedger.PopulationPathingDirector>();
            bool sampled = trace.SampleDeep(hub.Tasks, time.PreciseAbsoluteGameSeconds, time.CurrentAbsoluteDayIndex,
                personId => GetMovement(pathing, personId),
                taskId => GetDomesticRecovery(hub.DomesticLaborDriver, taskId));
            if (sampled && trace.Level >= NpcTraceLevel.Detailed && population?.State?.people != null)
            {
                foreach (int id in ResolvePeople(population, trace))
                {
                    PersonState person = population.State.people.FirstOrDefault(item => item != null && item.id == id);
                    if (person == null) continue;
                    string movement = GetMovement(pathing, id);
                    string status = string.Empty;
                    float remaining = 0f;
                    float eta = 0f;
                    bool moving = pathing != null && pathing.TryGetMovementReadout(id, out status, out remaining, out eta);
                    Vector3? position = pathing != null && pathing.TryGetPresentationTransform(id, out Transform actor) ? actor.position : (Vector3?)null;
                    string routeIdentity = string.Empty, routeMode = string.Empty, scheduleState = string.Empty, activityLocationId = string.Empty, moverState = string.Empty;
                    int routeGeneration = -1, routeWaypoint = -1, routeWaypointCount = 0;
                    Vector3 routeDestination = Vector3.zero;
                    bool moverVisible = false, routeActive = false, hasRouteDestination = false;
                    if (pathing != null && pathing.TryGetMovementTraceReadout(id, out routeIdentity, out routeGeneration,
                        out routeWaypoint, out routeWaypointCount, out routeDestination, out hasRouteDestination,
                        out routeMode, out scheduleState, out activityLocationId, out moverVisible, out routeActive, out moverState)) { }
                    trace.RecordPersonSnapshot(id, population.Memberships?.GetActiveHouseholdId(id) ?? person.householdId,
                        time.CurrentAbsoluteDayIndex, time.PreciseAbsoluteGameSeconds, person.scheduleState.ToString(),
                        person.currentDestinationBuildingId, position, moving ? status : movement,
                        moving ? remaining : 0f, moving ? eta : 0f, BuildTaskSummary(hub.Tasks, id),
                        routeIdentity, routeGeneration, routeWaypoint, routeWaypointCount,
                        hasRouteDestination ? routeDestination : (Vector3?)null, routeMode, scheduleState,
                        activityLocationId, moverVisible, routeActive, moverState);
                }
            }
            RepaintAll();
        }

        private static string GetDomesticRecovery(DomesticLaborDriver driver, string taskId)
        {
            if (driver?.RouteRecoveries == null || string.IsNullOrWhiteSpace(taskId)) return string.Empty;
            foreach (DomesticLaborDriver.DomesticRouteRecoveryRecord record in driver.RouteRecoveries)
            {
                if (record == null || !string.Equals(record.TaskId.ToString(), taskId, StringComparison.Ordinal)) continue;
                string state = record.WaitForCapabilityChange ? "WAITING_FOR_CAPABILITY_CHANGE" : "RETRY_BACKOFF";
                return "owner=DomesticLaborDriver; state=" + state + "; task=" + taskId
                    + "; retryAfterDay=" + record.RetryAfterDay
                    + "; failureCount=" + record.FailureCount
                    + "; activityLocation=" + (record.ActivityLocationId ?? string.Empty)
                    + "; blocker=" + (record.LastFailureReason ?? string.Empty)
                    + (record.WaitForCapabilityChange ? "; workerReservation=none" : string.Empty);
            }
            return string.Empty;
        }

        private static string BuildTaskSummary(TaskAuthority tasks, int personId)
        {
            return string.Join(" | ", tasks.GetAllOpenTasks().Where(task => task != null && task.HasAssignee && task.AssigneeId.Id == personId)
                .Select(task => task.TaskId + ":" + task.DefinitionId + ":" + task.Status + ":" + task.MinutesWorked + "/" + task.PlannedMinutes));
        }

        private static string GetMovement(LandLedgers.FirstLedger.PopulationPathingDirector pathing, int id)
        {
            if (pathing != null && pathing.TryGetMovementReadout(id, out string status, out float distance, out float eta))
                return status + "; remaining=" + distance.ToString("0.##") + "m; eta=" + eta.ToString("0.##") + "s";
            return "No movement readout";
        }

        private static List<int> ResolvePeople(PopulationManager population, NpcActivityTraceRecorder trace)
        {
            var result = new HashSet<int>(trace.SelectedPeople);
            if (population?.Memberships != null)
                foreach (int household in trace.SelectedHouseholds)
                    foreach (int person in population.Memberships.GetActiveMembers(household)) result.Add(person);
            if (trace.SelectedPeople.Count == 0 && trace.SelectedHouseholds.Count == 0 && population?.State?.people != null)
                foreach (PersonState person in population.State.people)
                    if (person != null && !person.isOffMap && person.deathDayIndex < 0) result.Add(person.id);
            return result.OrderBy(id => id).ToList();
        }

        private void ApplySelection(NpcActivityTraceRecorder trace, string reason)
        {
            trace.SetSelection(ParseIds(personIds), ParseIds(householdIds), reason);
            PersistSelection();
        }

        private void PersistSelection()
        {
            SessionState.SetString("LandLedgers.NpcTrace.Persons", personIds ?? "");
            SessionState.SetString("LandLedgers.NpcTrace.Households", householdIds ?? "");
            SessionState.SetInt("LandLedgers.NpcTrace.Level", (int)level);
        }

        private static HashSet<int> ParseIds(string text)
        {
            var result = new HashSet<int>();
            if (string.IsNullOrWhiteSpace(text)) return result;
            foreach (string part in text.Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string value = part.Trim();
                if (value.StartsWith("P", StringComparison.OrdinalIgnoreCase) || value.StartsWith("H", StringComparison.OrdinalIgnoreCase)) value = value.Substring(1);
                if (int.TryParse(value, out int id) && id >= 0) result.Add(id);
            }
            return result;
        }

        private static void FramePerson(LandLedgers.FirstLedger.PopulationPathingDirector pathing, int id)
        {
            if (pathing != null && pathing.TryGetPresentationTransform(id, out Transform actor))
            {
                Selection.activeTransform = actor;
                EditorGUIUtility.PingObject(actor.gameObject);
                SceneView.lastActiveSceneView?.FrameSelected();
            }
        }

        private static void CaptureMetadata(NpcActivityTraceRecorder trace)
        {
            string root = System.IO.Directory.GetParent(Application.dataPath)?.FullName;
            trace.SourceBranch = Git(root, "rev-parse --abbrev-ref HEAD");
            trace.SourceRevision = Git(root, "rev-parse HEAD");
            TimeManager time = UnityEngine.Object.FindFirstObjectByType<TimeManager>();
            trace.SelectedSpeed = time != null ? time.CurrentSpeed.ToString() : "NOT_CAPTURED";
            LandLedgers.World.TownWorldController town = UnityEngine.Object.FindFirstObjectByType<LandLedgers.World.TownWorldController>();
            trace.Seed = town != null ? town.LastSeedResolution.Seed : -1;
            trace.Scenario = AssetDatabase.LoadAssetAtPath<DevelopmentTownConfiguration>(
                "Assets/Core/Population/DevelopmentTownConfiguration.asset")?.ConfigurationId ?? "Main Scene";
        }

        private static string Git(string root, string args)
        {
            if (string.IsNullOrWhiteSpace(root)) return "NOT_CAPTURED";
            try
            {
                using (var process = Process.Start(new ProcessStartInfo("git", args)
                { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true }))
                {
                    string output = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit(2000);
                    return process.ExitCode == 0 ? output : "NOT_CAPTURED";
                }
            }
            catch (Exception ex) { Debug.LogWarning("NPC Activity Monitor could not capture Git metadata: " + ex.Message); return "NOT_CAPTURED"; }
        }

        private void Export(NpcActivityTraceRecorder trace)
        {
            System.IO.Directory.CreateDirectory(exportPath);
            trace.WriteJsonLines(Path.Combine(exportPath, "npc-activity-events.jsonl"));
            trace.WriteReadableReport(Path.Combine(exportPath, "npc-activity-timeline.txt"));
            trace.WriteAnomalyReport(Path.Combine(exportPath, "npc-activity-anomalies.txt"));
            File.WriteAllText(Path.Combine(exportPath, "npc-activity-manifest.json"), JsonUtility.ToJson(new NpcActivityTraceManifest
            {
                sessionId = trace.SessionId, sourceBranch = trace.SourceBranch, sourceRevision = trace.SourceRevision,
                scenario = trace.Scenario, seed = trace.Seed, selectedSpeed = trace.SelectedSpeed, traceLevel = trace.Level.ToString(),
                samplingIntervalSimulatedSeconds = trace.SampleIntervalSeconds, retainedEvents = trace.Events.Count,
                anomalyCount = trace.Anomalies.Count, droppedHistoryEvents = trace.DroppedHistoryEvents, droppedSessionEvents = trace.DroppedSessionEvents
            }, true));
            EditorUtility.RevealInFinder(exportPath);
        }

        private static void RepaintAll()
        {
            NpcActivityMonitorWindow[] windows = Resources.FindObjectsOfTypeAll<NpcActivityMonitorWindow>();
            for (int i = 0; i < windows.Length; i++) windows[i].Repaint();
        }
    }
}
#endif
