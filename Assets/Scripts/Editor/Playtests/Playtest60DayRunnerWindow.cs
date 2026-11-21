#if UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LandLedgers.Economy;
using LandLedgers.Economy.Core;
using LandLedgers.FirstLedger;
using LandLedgers.Orchestration.Bootstrap;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Diagnostics;
using LandLedgers.Population;
using LandLedgers.Persistence;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace LandLedgers.Editor.Playtests
{
    public sealed class Playtest60DayRunnerWindow : EditorWindow
    {
        private SimulationSpeed speed = SimulationSpeed.Speed250x;
        private int targetDays = 60;
        private bool customDuration;
        private bool traceEnabled;
        private string tracePersonIds = "";
        private string traceHouseholdIds = "";
        private NpcTraceLevel traceLevel = NpcTraceLevel.Detailed;
        private bool traceAutoStall = true;
        private bool traceAutoSelect = true;
        private double traceSamplingSeconds = 300;
        private double tracePrehistorySeconds = 86400;
        private double traceObservationSeconds;
        private int traceHistoryLimit = 5000;
        private int traceEventLimit = 20000;
        private string traceExportPath = "";
        private Vector2 scrollPosition;

        private void OnEnable()
        {
            // Keep an already-open docked window on the new visible name too;
            // retaining the class and .meta GUID preserves Editor references.
            titleContent = new GUIContent("Playtest");
        }

        [MenuItem("Land & Ledgers/Diagnostics/Playtest")]
        public static void Open()
        {
            GetWindow<Playtest60DayRunnerWindow>("Playtest");
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            EditorGUILayout.LabelField("Playtest", EditorStyles.largeLabel);
            EditorGUILayout.HelpBox("Runs the open scene through its existing New Game bootstrap and TimeManager. Calendar completion is reported separately from economic health.", MessageType.Info);
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Run setup", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(Playtest60DayRunCoordinator.IsActive || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                int durationChoice = EditorGUILayout.IntPopup("Duration", customDuration ? Playtest60DayRunCoordinator.CustomDurationChoice : targetDays,
                    Playtest60DayRunCoordinator.DurationLabels, Playtest60DayRunCoordinator.DurationChoices);
                customDuration = durationChoice == Playtest60DayRunCoordinator.CustomDurationChoice;
                if (customDuration)
                    targetDays = Playtest60DayRunCoordinator.ResolveDurationSelection(durationChoice, EditorGUILayout.IntField("Custom completed days", targetDays));
                else
                    targetDays = durationChoice;
                speed = (SimulationSpeed)EditorGUILayout.EnumPopup("Simulation speed", speed);
                EditorGUILayout.LabelField("Scene", EditorSceneManager.GetActiveScene().path);
                EditorGUILayout.LabelField("Seed", "Uses the deterministic seed configured on TownGenerationSettings; the resolved value is recorded in run metadata.");
                EditorGUILayout.HelpBox("Startup validation runs before day 1. Console capture and daily evidence are written to the run folder. The tool does not save the scene.", MessageType.None);
                EditorGUILayout.Space(4);
                traceEnabled = EditorGUILayout.Toggle("Enable NPC activity trace", traceEnabled);
                using (new EditorGUI.DisabledScope(!traceEnabled))
                {
                    tracePersonIds = EditorGUILayout.TextField("Person IDs", tracePersonIds);
                    traceHouseholdIds = EditorGUILayout.TextField("Household IDs", traceHouseholdIds);
                    traceLevel = (NpcTraceLevel)EditorGUILayout.EnumPopup("Trace level", traceLevel);
                    traceAutoStall = EditorGUILayout.Toggle("Automatic stall detection", traceAutoStall);
                    traceAutoSelect = EditorGUILayout.Toggle("Auto-select anomalies", traceAutoSelect);
                    traceSamplingSeconds = EditorGUILayout.DoubleField("Sample interval (sim seconds)", traceSamplingSeconds);
                    tracePrehistorySeconds = EditorGUILayout.DoubleField("Prehistory duration (sim seconds)", tracePrehistorySeconds);
                    traceObservationSeconds = EditorGUILayout.DoubleField("Observation duration (sim seconds; 0 = whole run)", traceObservationSeconds);
                    traceHistoryLimit = EditorGUILayout.IntField("Prehistory retention", traceHistoryLimit);
                    traceEventLimit = EditorGUILayout.IntField("Event retention", traceEventLimit);
                    traceExportPath = EditorGUILayout.TextField("Trace export path (blank = run folder)", traceExportPath);
                }
                if (targetDays <= 0)
                    EditorGUILayout.HelpBox("Custom duration must be a positive number of completed in-game days.", MessageType.Error);
                using (new EditorGUI.DisabledScope(targetDays <= 0))
                {
                    if (GUILayout.Button("Start Playtest"))
                    {
                        var traceOptions = new Playtest60DayRunCoordinator.ActivityTraceOptions
                        {
                            enabled = traceEnabled,
                            personIds = tracePersonIds,
                            householdIds = traceHouseholdIds,
                            level = traceLevel.ToString(),
                            autoStallDetection = traceAutoStall,
                            autoSelectAnomalies = traceAutoSelect,
                            sampleIntervalSeconds = traceSamplingSeconds,
                            prehistoryDurationSeconds = tracePrehistorySeconds,
                            observationDurationSeconds = traceObservationSeconds,
                            historyLimit = traceHistoryLimit,
                            eventLimit = traceEventLimit,
                            exportPath = traceExportPath
                        };
                        if (!Playtest60DayRunCoordinator.TryStart(targetDays, speed, traceOptions, out string message))
                            EditorUtility.DisplayDialog("Playtest Not Started", message, "OK");
                    }
                }
            }
            EditorGUILayout.EndVertical();

            Playtest60DayRunCoordinator.RunSession session = Playtest60DayRunCoordinator.Current;
            if (session != null)
            {
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField("Current run", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Run", session.runId ?? "(none)");
                EditorGUILayout.LabelField("State", session.status ?? "(unknown)");
                EditorGUILayout.LabelField("Progress", $"{session.completedDays}/{session.targetDays} days");
                EditorGUILayout.LabelField("Result", session.result ?? "(pending)");
                EditorGUILayout.LabelField("Economic assessment", session.economicAssessment ?? "NOT_EVALUATED");
                if (!string.IsNullOrEmpty(session.blocker))
                    EditorGUILayout.HelpBox(session.blocker, MessageType.Warning);
                if (!string.IsNullOrEmpty(session.outputDirectory))
                    EditorGUILayout.SelectableLabel(session.outputDirectory, EditorStyles.textField, GUILayout.Height(34));

                using (new EditorGUI.DisabledScope(!Playtest60DayRunCoordinator.IsActive))
                {
                    if (GUILayout.Button("Stop and mark run aborted"))
                        Playtest60DayRunCoordinator.Abort("Stopped by the owner through the playtest runner.");
                }
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();
        }

        private void Update()
        {
            if (Playtest60DayRunCoordinator.IsActive)
                Repaint();
        }
    }

    [InitializeOnLoad]
    internal static class Playtest60DayRunCoordinator
    {
        internal const int CustomDurationChoice = -1;
        internal static readonly int[] DurationChoices = { 5, 10, 15, 30, 60, 90, CustomDurationChoice };
        internal static readonly string[] DurationLabels = { "5 days", "10 days", "15 days", "30 days", "60 days", "90 days", "Custom…" };
        internal static bool IsValidDuration(int days) => days > 0;

        internal static int ResolveDurationSelection(int selectedDuration, int customDays) =>
            selectedDuration == CustomDurationChoice ? customDays : selectedDuration;

        private static string DomainMarkerKey => SessionKey + ".DomainMarker";
        internal enum StartupGateDecision
        {
            Waiting = 0,
            Ready = 1,
            Invalid = 2,
            TimedOut = 3
        }

        private const string SessionKey = "LandLedgers.Playtest60Day.Session";
        private const string RunRoot = "Reports/Playtests/Runs";
        private static readonly ConcurrentQueue<ConsoleEvent> PendingConsole = new();
        private static readonly string domainMarker = Guid.NewGuid().ToString("N");
        private static RunSession current;
        private static bool subscribedToLogs;
        private static bool awaitingStartupGate;
        private static bool exceptionObserved;
        private static TimeManager subscribedTimeManager;
        private static double startupWaitStartedAt;
        private static double nextRuntimeContinuityCheckAt;
        private static double nextTraceSampleWallTime;
        private static double traceStartedAtSeconds = -1;
        private static bool traceExported;
        private static string currentSimulationContext = "NOT_STARTED";

        [Serializable]
        internal sealed class ActivityTraceOptions
        {
            public bool enabled;
            public string personIds = string.Empty;
            public string householdIds = string.Empty;
            public string level = "Detailed";
            public bool autoStallDetection = true;
            public bool autoSelectAnomalies = true;
            public double sampleIntervalSeconds = 300;
            public double prehistoryDurationSeconds = 86400;
            public double observationDurationSeconds;
            public int historyLimit = 5000;
            public int eventLimit = 20000;
            public string exportPath = string.Empty;
        }

        [Serializable]
        internal sealed class RunSession
        {
            public string runId;
            public string status;
            public string result;
            public string blocker;
            public string outputDirectory;
            public string wallStartUtc;
            public string wallEndUtc;
            public string unityVersion;
            public string branch;
            public string commit;
            public bool trackedWorkingTreeDirty;
            public int trackedWorkingTreeChangeCount;
            public string trackedWorkingTreeState = "NOT_CAPTURED";
            public string untrackedWorkingTreeState = "NOT_CAPTURED: untracked asset-pack volume is intentionally not enumerated by the runner";
            public string scene;
            public string scenario;
            public string startupMode;
            public string seedSource;
            public int seed;
            public int targetDays;
            public int completedDays;
            public List<int> missingDailySnapshotOffsets = new();
            public int unexpectedDomainReloadCount;
            public int startAbsoluteDayIndex = -1;
            public int endAbsoluteDayIndex = -1;
            public string startGameDate = "NOT_CAPTURED";
            public string endGameDate = "NOT_CAPTURED";
            public string speedName;
            public float selectedMultiplier;
            public float dayLengthSeconds;
            public int expectedResidents;
            public int expectedHouseholds;
            public int expectedBusinesses;
            public int actualResidents = -1;
            public int actualPersonRecords = -1;
            public int actualHouseholds = -1;
            public int actualBusinesses = -1;
            // Retained as the legacy total-event field for serialized-run compatibility.
            public int logCount;
            // Authoritative event count, distinct from informational LogType.Log messages.
            public int totalConsoleEventCount;
            public int informationalLogCount;
            public int warningCount;
            public int errorCount;
            public int exceptionCount;
            public bool startupGatePassed;
            public ActivityTraceOptions activityTrace = new ActivityTraceOptions();
            public string activityTraceStatus = "DISABLED";
            public string activityTraceSessionId = string.Empty;
            public int activityTraceEventCount;
            public int activityTraceAnomalyCount;
            public int activityTraceDroppedEventCount;
            public string activityTraceExportPath = string.Empty;
            public string economicAssessment = "NOT_EVALUATED: calendar completion is not an economic pass";
            public string finalInvariant = "NOT_CAPTURED";
        }

        [Serializable]
        private sealed class FinalState
        {
            public string runId;
            public string result;
            public string economicAssessment;
            public int daysCompleted;
            public bool calendarIntervalCompleted;
            public string finalInvariant;
            public int[] missingDailySnapshotOffsets;
            public List<string> unavailableDetails = new();
        }

        [Serializable]
        private sealed class ConsoleEvent
        {
            public long sequence;
            public string wallUtc;
            public string severity;
            public string simulationContext;
            public string message;
            public string stackTrace;
        }

        [Serializable]
        private sealed class EntitySnapshot
        {
            public string entityType;
            public string id;
            public string ownerOrName;
            public string locationOrPremises;
            public int cashCents = -1;
            public string cashStatus = "NOT_CAPTURED";
            public int inventoryLotCount = -1;
            public int inventoryUnits = -1;
            public string inventoryStatus = "NOT_CAPTURED";
            public string detail = "NOT_CAPTURED";
        }

        [Serializable]
        private sealed class DaySnapshot
        {
            public string runId;
            public int absoluteDayIndex;
            public string gameDate;
            public string wallUtc;
            public string speed;
            public int residentCount = -1;
            public int personRecordCount = -1;
            public int activeHouseholdMembershipCount = -1;
            public int activeShoppingTripCount = -1;
            public string activeShoppingTrips = "NOT_CAPTURED";
            public int householdCount = -1;
            public int businessCount = -1;
            public long householdCashCents = -1;
            public long businessCashCents = -1;
            public long householdLotUnits = -1;
            public long retailLotUnits = -1;
            public int mealDayIndex = -1;
            public int mealsEaten = -1;
            public int mealsMissed = -1;
            public int householdsServedMeals = -1;
            public int mealPathPurchases = -1;
            public int mealPathSpendCents = -1;
            public int queuedTasks = -1;
            public int assignedTasks = -1;
            public int inProgressTasks = -1;
            public int pausedTasks = -1;
            public int dependencyWaitingTasks = -1;
            public int passiveWaitingTasks = -1;
            public int resourcesReservedTasks = -1;
            public int reworkTasks = -1;
            public int interruptedTasks = -1;
            public int completedTasksToday = -1;
            public int failedTasksToday = -1;
            public int cancelledTasksRetained = -1;
            public string mealTotals = "NOT_CAPTURED: inspect [FoodEconomy] events in console-events.jsonl";
            public string taskCompletion = "NOT_CAPTURED: no public aggregate seam";
            public string movementOutcomes = "NOT_CAPTURED: authoritative route outcomes not exposed as a daily snapshot";
            public string freightAndConstruction = "NOT_CAPTURED: use authoritative event logs and Pass 9 state records";
        }

        [Serializable]
        internal sealed class ActivityTaskTraceSnapshot
        {
            public string recordType;
            public string runId;
            public int absoluteDayIndex = -1;
            public string capturedUtc;
            public float capturedGameSeconds = -1f;
            public string traceCoverage = "NOT_CAPTURED";
            public int authoritativeOpenTaskCount = -1;
            public int openTasksWithTimingRecord = -1;
            public int openTasksWithoutTimingRecord = -1;
            public int currentDayReservationCount = -1;
            public string taskId;
            public string definitionId;
            public string taskStatus;
            public string taskOutcome;
            public string ownerId;
            public string assigneeId;
            public string targetDescription;
            public string requiredCapability;
            public string workLocationId;
            public int workedMinutes = -1;
            public int plannedMinutes = -1;
            public int remainingMinutes = -1;
            public int interruptionCount = -1;
            public string reservationHandleId;
            public string dueEventId;
            public string dependencyDescription;
            public string lastTransition;
            public int lastTransitionDay = -1;
            public float lastTransitionSeconds = -1f;
            public float lastProgressSeconds = -1f;
            public string lastBlockReason;
            public string lastRefusal;
            public string lastRefusalMethod;
            public int lastRefusalDay = -1;
            public string lastNoWorkerReason;
            public int lastNoWorkerDay = -1;
            public int consecutiveNoWorkerDays = -1;
            public string timingClockPrecision;
            public bool timingRebuiltAfterLoad;
            public string timingRecordStatus;
            public string physicalRouteState = "NOT_CAPTURED: no movement readout supplied";
            public int physicalRoutePersonId = -1;
            public float physicalRouteRemainingDistanceMeters = -1f;
            public float physicalRouteEstimatedGameSecondsToArrival = -1f;
            public int personHomeBuildingId = -1;
            public int personWorkplaceBuildingId = -1;
            public int personCurrentDestinationBuildingId = -1;
            public string personScheduleState = "NOT_CAPTURED";
            public string personRouteMode = "NOT_CAPTURED";
            public bool personMoverVisible;
            public bool personRouteActive;
            public string personOperationalReadoutStatus = "NOT_CAPTURED";
            public int physicalRouteReadoutsAttempted = -1;
            public int physicalRouteReadoutsAvailable = -1;
            public int physicalRouteReadoutsUnavailable = -1;
            public string reservationId;
            public int personId = -1;
            public string activity;
            public int startMinuteOfDay = -1;
            public int durationMinutes = -1;
            public int endMinuteOfDay = -1;
            public string reservationLabel;
            public bool released;
        }

        internal delegate bool PersonMovementReadoutProvider(
            int personId, out string status, out float remainingDistanceMeters,
            out float estimatedGameSecondsToArrival);

        internal delegate bool PersonOperationalReadoutProvider(
            int personId, out int homeBuildingId, out int workplaceBuildingId,
            out int currentDestinationBuildingId, out string scheduleState,
            out string routeMode, out bool moverVisible, out bool routeActive);

        static Playtest60DayRunCoordinator()
        {
            RestoreSession();
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload += FlushPendingConsole;
            EditorApplication.quitting += OnEditorQuitting;
            if (IsActive)
                AttachLogCapture();
        }

        internal static RunSession Current => current;
        internal static bool IsActive => current != null && (current.status == "ARMED" || current.status == "RUNNING");

        internal static bool TryStart(int days, SimulationSpeed simulationSpeed, out string message)
        {
            return TryStart(days, simulationSpeed, new ActivityTraceOptions(), out message);
        }

        internal static bool TryStart(int days, SimulationSpeed simulationSpeed, ActivityTraceOptions traceOptions, out string message)
        {
            message = string.Empty;
            traceOptions ??= new ActivityTraceOptions();
            if (!IsValidDuration(days))
            {
                message = "Playtest duration must be a positive number of completed in-game days.";
                return false;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode || IsActive)
            {
                message = "Unity is already entering Play Mode or another Playtest is active.";
                return false;
            }

            Scene scene = EditorSceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                message = "No loaded Unity scene is available.";
                return false;
            }

            FirstLedgerSliceBootstrapper bootstrapper = UnityEngine.Object.FindFirstObjectByType<FirstLedgerSliceBootstrapper>(FindObjectsInactive.Include);
            if (bootstrapper == null || bootstrapper.StartupIntent != WorldStartupIntent.NewGame)
            {
                message = "The active scene must contain the New Game FirstLedgerSliceBootstrapper.";
                return false;
            }

            SerializedObject bootstrapSerialized = new(bootstrapper);
            SerializedProperty enabledProperty = bootstrapSerialized.FindProperty("enableDevelopmentTownConfiguration");
            if (enabledProperty == null || !enabledProperty.boolValue)
            {
                message = "The active bootstrapper is not configured to use DevelopmentTownConfiguration.";
                return false;
            }

            DevelopmentTownConfiguration configuration = AssetDatabase.LoadAssetAtPath<DevelopmentTownConfiguration>(
                "Assets/Core/Population/DevelopmentTownConfiguration.asset");
            if (configuration == null)
            {
                message = "DevelopmentTownConfiguration.asset could not be loaded from its canonical project path.";
                return false;
            }
            if (!configuration.Validate(out string validationError))
            {
                message = "INVALID STARTUP GATE. No Play Mode was started. " + validationError;
                return false;
            }

            TownWorldController town = UnityEngine.Object.FindFirstObjectByType<TownWorldController>(FindObjectsInactive.Include);
            TimeManager time = UnityEngine.Object.FindFirstObjectByType<TimeManager>(FindObjectsInactive.Include);
            if (town == null || town.Settings == null || time == null || time.Settings == null)
            {
                message = "The active scene must bind TownWorldController, TownGenerationSettings and TimeManager before a run.";
                return false;
            }
            if ((long)time.CurrentAbsoluteDayIndex + days > int.MaxValue)
            {
                message = "The requested duration exceeds the supported absolute calendar range.";
                return false;
            }
            if (!town.Settings.useManualSeedOverride && town.Settings.startupMode != WorldGenerationStartupMode.FixedTestingWorld && !town.Settings.useFixedDebugSeed)
            {
                message = "Choose a deterministic seed in the existing TownGenerationSettings (Manual Seed Override or Fixed Testing World) before starting.";
                return false;
            }

            string root = System.IO.Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(root))
            {
                message = "Unity did not provide a project root path.";
                return false;
            }

            string runId = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string output = Path.Combine(root, RunRoot, runId);
            string trackedStatus = ReadGit("status --porcelain --untracked-files=no", root);
            int trackedChangeCount = CountNonEmptyLines(trackedStatus);
            string trackedState = ResolveWorkingTreeState(trackedChangeCount);
            System.IO.Directory.CreateDirectory(output);
            exceptionObserved = false;
            while (PendingConsole.TryDequeue(out _)) { }
            current = new RunSession
            {
                runId = runId,
                status = "ARMED",
                result = "PENDING",
                outputDirectory = output,
                wallStartUtc = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion,
                branch = ReadGit("rev-parse --abbrev-ref HEAD", root),
                commit = ReadGit("rev-parse HEAD", root),
                trackedWorkingTreeChangeCount = trackedChangeCount,
                trackedWorkingTreeDirty = trackedChangeCount > 0,
                trackedWorkingTreeState = trackedState,
                scene = scene.path,
                scenario = configuration.ConfigurationId,
                startupMode = bootstrapper.StartupIntent.ToString(),
                seedSource = "Configured deterministic TownGenerationSettings seed; resolved seed recorded after startup.",
                seed = town.Settings.useManualSeedOverride ? town.Settings.manualSeedOverride : town.Settings.startupMode == WorldGenerationStartupMode.FixedTestingWorld ? town.Settings.fixedTestingWorldSeed : town.Settings.fixedDebugSeed,
                targetDays = days,
                speedName = simulationSpeed.ToString(),
                selectedMultiplier = time.Settings.GetMultiplier(simulationSpeed),
                dayLengthSeconds = time.Settings.GameSecondsPerDay,
                expectedResidents = configuration.GeneratePopulationFromSettings ? -1 : configuration.People.Count,
                expectedHouseholds = configuration.Households.Count,
                expectedBusinesses = configuration.Businesses.Count,
                activityTrace = JsonUtility.FromJson<ActivityTraceOptions>(JsonUtility.ToJson(traceOptions)),
                activityTraceStatus = traceOptions.enabled ? "ARMED" : "DISABLED"
            };
            traceExported = false;
            traceStartedAtSeconds = -1;
            System.IO.Directory.CreateDirectory(Path.Combine(root, RunRoot));
            File.WriteAllText(Path.Combine(output, "metadata.json"), JsonUtility.ToJson(current, true));
            File.WriteAllText(Path.Combine(output, "console-full.txt"), "# Complete Unity Console capture; raw messages and stack traces are preserved.\r\n");
            File.WriteAllText(Path.Combine(output, "console-events.jsonl"), string.Empty);
            File.WriteAllText(Path.Combine(output, "activity-task-trace.jsonl"), string.Empty);
            File.WriteAllText(Path.Combine(output, "daily-summary.csv"),
                "absoluteDayIndex,gameDate,wallUtc,speed,residents,personRecords,activeHouseholdMembershipCount,households,businesses,householdCashCents,businessCashCents,householdLotUnits,retailLotUnits,mealDayIndex,householdsServedMeals,mealsEaten,mealsMissed,mealPathPurchases,mealPathSpendCents,queuedTasks,assignedTasks,inProgressTasks,pausedTasks,dependencyWaitingTasks,passiveWaitingTasks,resourcesReservedTasks,reworkTasks,interruptedTasks,completedTasksToday,failedTasksToday,cancelledTasksRetained,mealTotals,taskCompletion,movementOutcomes,freightAndConstruction,activeShoppingTripCount,activeShoppingTrips\r\n");
            File.WriteAllText(Path.Combine(output, "entity-snapshots.jsonl"), string.Empty);
            WriteRunIndex(root, current);
            SaveSession();
            AttachLogCapture();
            if (traceOptions.enabled)
            {
                SimulationSystemsHub traceHub = UnityEngine.Object.FindFirstObjectByType<SimulationSystemsHub>(FindObjectsInactive.Include);
                if (traceHub != null)
                {
                    NpcActivityTraceRecorder trace = traceHub.ActivityTrace;
                    ConfigureActivityTrace(trace, traceOptions, current, time, startRecording: false);
                    trace.BeginMonitoring();
                }
                else current.activityTraceStatus = "ARMED: hub will be connected after runtime startup";
            }
            EditorApplication.isPlaying = true;
            message = "Run armed: " + output;
            return true;
        }

        internal static void Abort(string reason)
        {
            if (current == null) return;
            current.status = "ABORTED";
            current.result = "INVALID";
            current.blocker = reason ?? "Aborted.";
            current.wallEndUtc = DateTime.UtcNow.ToString("O");
            FinalizeActivityTrace();
            WriteFinalArtifacts();
            if (EditorApplication.isPlaying)
            {
                TimeManager time = UnityEngine.Object.FindFirstObjectByType<TimeManager>();
                time?.SetSpeed(SimulationSpeed.Paused);
                EditorApplication.isPlaying = false;
            }
            UnsubscribeDayEvents();
            DetachLogCapture();
            SaveSession();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode && current != null && current.status == "ARMED")
            {
                awaitingStartupGate = true;
                startupWaitStartedAt = EditorApplication.timeSinceStartup;
            }
            else if (change == PlayModeStateChange.ExitingPlayMode && current != null
                && (current.status == "ARMED" || current.status == "RUNNING"))
            {
                current.status = "ABORTED";
                current.result = current.completedDays >= current.targetDays ? "COMPLETED" : "INCOMPLETE";
                if (current.completedDays < current.targetDays && string.IsNullOrEmpty(current.blocker))
                    current.blocker = "Play Mode ended before the configured number of complete simulation days.";
                current.wallEndUtc = DateTime.UtcNow.ToString("O");
                FinalizeActivityTrace();
                WriteFinalArtifacts();
                SaveSession();
            }
            else if (change == PlayModeStateChange.EnteredEditMode)
            {
                awaitingStartupGate = false;
                DetachLogCapture();
                if (current != null && ShouldFinalizeAfterCaptureDetach(current.status))
                {
                    WriteFinalArtifacts();
                    SaveSession();
                }
            }
        }

        private static void Update()
        {
            if (current == null) return;
            DrainConsoleQueue();
            RestoreRuntimeDayChangedSubscription();

            if (awaitingStartupGate && EditorApplication.isPlaying)
            {
                if (exceptionObserved)
                {
                    Abort("An uncaught runtime exception occurred before the startup gate passed.");
                    return;
                }
                bool gatePassed = TryPassRuntimeStartupGate(out string blocker, out TimeManager time);
                FirstLedgerSliceBootstrapper bootstrapper = UnityEngine.Object.FindFirstObjectByType<FirstLedgerSliceBootstrapper>();
                StartupGateDecision decision = ResolveStartupGateDecision(gatePassed,
                    bootstrapper != null && bootstrapper.IsInitialized,
                    bootstrapper != null && bootstrapper.HasStartupFailed,
                    EditorApplication.timeSinceStartup - startupWaitStartedAt > 30d);
                if (decision == StartupGateDecision.Invalid)
                {
                    current.blocker = blocker;
                    Abort("INVALID STARTUP: " + blocker);
                    return;
                }
                if (decision == StartupGateDecision.TimedOut)
                {
                    Abort("Startup gate timed out after 30 real seconds; no simulated day was accepted. Last check: " + blocker);
                    return;
                }
                if (decision == StartupGateDecision.Waiting) return;

                current.startupGatePassed = true;
                current.status = "RUNNING";
                SessionState.SetString(DomainMarkerKey, domainMarker);
                nextRuntimeContinuityCheckAt = EditorApplication.timeSinceStartup;
                awaitingStartupGate = false;
                current.startAbsoluteDayIndex = time.CurrentAbsoluteDayIndex;
                current.endAbsoluteDayIndex = current.startAbsoluteDayIndex + current.targetDays;
                current.startGameDate = time.CurrentDate.ToString();
                current.seed = UnityEngine.Object.FindFirstObjectByType<TownWorldController>()?.LastSeedResolution.Seed ?? current.seed;
                current.finalInvariant = "PASS: bootstrap, town, population, households, businesses, hubs, drivers and required Pass 9 holders are present.";
                Volatile.Write(ref currentSimulationContext, "day=" + time.CurrentAbsoluteDayIndex + "; date=" + time.CurrentDate);
                time.DayChanged += OnDayChanged;
                subscribedTimeManager = time;
                time.SetSpeed(Enum.TryParse(current.speedName, out SimulationSpeed selected) ? selected : SimulationSpeed.Speed1x);
                if (current.activityTrace != null && current.activityTrace.enabled)
                    StartActivityTrace(time);
                SaveSession();
            }

            if (current.status == "RUNNING" && EditorApplication.isPlaying)
            {
                if (exceptionObserved)
                {
                    Abort("An uncaught runtime exception was captured; run aborted for investigation.");
                    return;
                }
                if (current.unexpectedDomainReloadCount > 0)
                {
                    Abort("INVALID RUN: Unity scripting domain reloaded during Playtest; runtime state and evidence continuity cannot be trusted.");
                    return;
                }
                TimeManager time = UnityEngine.Object.FindFirstObjectByType<TimeManager>();
                if (time == null)
                {
                    Abort("TimeManager disappeared while the run was active.");
                    return;
                }
                if (EditorApplication.timeSinceStartup >= nextRuntimeContinuityCheckAt)
                {
                    nextRuntimeContinuityCheckAt = EditorApplication.timeSinceStartup + 1d;
                    string continuityFailure = FindRuntimeContinuityFailure();
                    if (!string.IsNullOrEmpty(continuityFailure))
                    {
                        Abort("INVALID RUN: required gameplay runtime state was lost: " + continuityFailure);
                        return;
                    }
                }
                UpdateActivityTrace(time);
                int elapsed = time.CurrentAbsoluteDayIndex - current.startAbsoluteDayIndex;
                if (elapsed < 0)
                {
                    Abort("Authoritative absolute simulation day index moved backwards.");
                    return;
                }
                if (elapsed >= current.targetDays)
                {
                    AppendMissingSnapshotsThrough(current.completedDays, current.targetDays);
                    current.completedDays = current.targetDays;
                    current.endAbsoluteDayIndex = time.CurrentAbsoluteDayIndex;
                    current.endGameDate = time.CurrentDate.ToString();
                    current.status = "COMPLETED";
                    current.result = current.missingDailySnapshotOffsets.Count == 0
                        ? "CALENDAR_COMPLETE_REQUIRES_REVIEW" : "INVALID";
                    current.wallEndUtc = DateTime.UtcNow.ToString("O");
                    current.economicAssessment = "NOT_EVALUATED: requested calendar interval completed; inspect daily activity, transactions, warnings and invariants before judging economic health.";
                    current.finalInvariant = current.missingDailySnapshotOffsets.Count == 0
                        ? "CALENDAR COMPLETE: authoritative clock crossed the configured interval; no economic success is implied."
                        : "INVALID: calendar interval elapsed but one or more daily snapshots are missing.";
                    if (current.missingDailySnapshotOffsets.Count > 0)
                        current.blocker = "Missing daily snapshots at offsets: " + string.Join(", ", current.missingDailySnapshotOffsets) + ". Calendar time elapsed, but evidence is incomplete.";
                    time.SetSpeed(SimulationSpeed.Paused);
                    UnsubscribeDayEvents();
                    FinalizeActivityTrace();
                    WriteFinalArtifacts();
                    SaveSession();
                    EditorApplication.delayCall += () => EditorApplication.isPlaying = false;
                }
                else if (elapsed > current.completedDays)
                {
                    // The DayChanged subscription records each boundary. This is a
                    // backstop for elapsed dates if a handler was detached by a
                    // domain reload; such a gap is explicitly written as missing.
                    AppendMissingSnapshotsThrough(current.completedDays, elapsed);
                    current.completedDays = elapsed;
                    SaveSession();
                }
            }
        }

        private static void StartActivityTrace(TimeManager time)
        {
            SimulationSystemsHub hub = UnityEngine.Object.FindFirstObjectByType<SimulationSystemsHub>();
            if (hub == null)
            {
                current.activityTraceStatus = "BLOCKED: SimulationSystemsHub is unavailable after startup";
                return;
            }

            NpcActivityTraceRecorder trace = hub.ActivityTrace;
            ConfigureActivityTrace(trace, current.activityTrace, current, time, startRecording: false);
            trace.BeginMonitoring();
            trace.StartRecording(Enum.TryParse(current.activityTrace.level, out NpcTraceLevel level) ? level : NpcTraceLevel.Detailed, true);
            traceStartedAtSeconds = time.PreciseAbsoluteGameSeconds;
            nextTraceSampleWallTime = EditorApplication.timeSinceStartup;
            current.activityTraceSessionId = trace.SessionId;
            current.activityTraceStatus = "RECORDING";
        }

        private static void ConfigureActivityTrace(NpcActivityTraceRecorder trace, ActivityTraceOptions options,
            RunSession session, TimeManager time, bool startRecording)
        {
            if (trace == null || options == null || session == null) return;
            trace.AutoStallDetection = options.autoStallDetection;
            trace.AutoSelectAnomalies = options.autoSelectAnomalies;
            trace.SampleIntervalSeconds = Math.Max(1, options.sampleIntervalSeconds);
            trace.PrehistoryDurationSeconds = options.prehistoryDurationSeconds;
            trace.HistoryLimit = Math.Max(1, options.historyLimit);
            trace.RetentionLimit = Math.Max(1, options.eventLimit);
            trace.SetSelection(ParseIds(options.personIds), ParseIds(options.householdIds), "Playtest runner selection");
            trace.SourceBranch = session.branch ?? "NOT_CAPTURED";
            trace.SourceRevision = session.commit ?? "NOT_CAPTURED";
            trace.Scenario = session.scenario ?? "NOT_CAPTURED";
            trace.Seed = session.seed;
            trace.SelectedSpeed = time != null ? time.CurrentSpeed.ToString() : session.speedName;
            trace.BeginMonitoring();
            if (startRecording)
                trace.StartRecording(Enum.TryParse(options.level, out NpcTraceLevel level) ? level : NpcTraceLevel.Detailed, true);
        }

        private static void UpdateActivityTrace(TimeManager time)
        {
            if (current == null || current.activityTrace == null || !current.activityTrace.enabled || time == null) return;
            SimulationSystemsHub hub = UnityEngine.Object.FindFirstObjectByType<SimulationSystemsHub>();
            if (hub == null)
            {
                current.activityTraceStatus = "INTERRUPTED: SimulationSystemsHub disappeared";
                return;
            }
            NpcActivityTraceRecorder trace = hub.ActivityTrace;
            if (!trace.Recording) return;
            double now = time.PreciseAbsoluteGameSeconds;
            trace.SetCurrentDay(time.CurrentAbsoluteDayIndex);
            double observation = current.activityTrace.observationDurationSeconds;
            if (observation > 0 && traceStartedAtSeconds >= 0 && now - traceStartedAtSeconds >= observation)
            {
                trace.StopRecording("Configured simulated-time observation duration elapsed.");
                current.activityTraceStatus = "OBSERVATION COMPLETE";
                FinalizeActivityTrace();
                return;
            }
            if (EditorApplication.timeSinceStartup < nextTraceSampleWallTime) return;
            nextTraceSampleWallTime = EditorApplication.timeSinceStartup + 0.2d;

            PopulationPathingDirector pathing = UnityEngine.Object.FindFirstObjectByType<PopulationPathingDirector>();
            bool sampled = trace.SampleDeep(hub.Tasks, now, time.CurrentAbsoluteDayIndex, personId =>
                pathing != null && pathing.TryGetMovementReadout(personId, out string status, out float meters, out float eta)
                    ? status + "; remaining=" + meters.ToString("0.##") + "m; eta=" + eta.ToString("0.##") + "s"
                    : "No movement readout",
                taskId => GetDomesticRecovery(hub.DomesticLaborDriver, taskId));
            if (sampled && trace.Level >= NpcTraceLevel.Detailed)
                CaptureDeepPersonSnapshots(trace, hub, pathing, time, now);
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

        private static void CaptureDeepPersonSnapshots(NpcActivityTraceRecorder trace, SimulationSystemsHub hub,
            PopulationPathingDirector pathing, TimeManager time, double now)
        {
            PopulationManager population = UnityEngine.Object.FindFirstObjectByType<PopulationManager>();
            if (population?.State?.people == null) return;
            var ids = new HashSet<int>(trace.SelectedPeople);
            if (population.Memberships != null)
                foreach (int householdId in trace.SelectedHouseholds)
                    foreach (int personId in population.Memberships.GetActiveMembers(householdId)) ids.Add(personId);
            if (trace.SelectedPeople.Count == 0 && trace.SelectedHouseholds.Count == 0)
                foreach (PersonState person in population.State.people)
                    if (person != null && !person.isOffMap && person.deathDayIndex < 0) ids.Add(person.id);

            List<WorkTask> tasks = hub.Tasks.GetAllOpenTasks();
            foreach (int personId in ids)
            {
                PersonState person = population.State.people.FirstOrDefault(candidate => candidate != null && candidate.id == personId);
                if (person == null) continue;
                string movement = "No movement readout";
                float remaining = 0f;
                float eta = 0f;
                bool hasMovement = pathing != null && pathing.TryGetMovementReadout(personId, out movement, out remaining, out eta);
                Vector3? position = pathing != null && pathing.TryGetPresentationTransform(personId, out Transform actor) ? actor.position : (Vector3?)null;
                string routeIdentity = string.Empty, routeMode = string.Empty, scheduleState = string.Empty, activityLocationId = string.Empty, moverState = string.Empty;
                int routeGeneration = -1, routeWaypoint = -1, routeWaypointCount = 0;
                Vector3 routeDestination = Vector3.zero;
                bool moverVisible = false, routeActive = false, hasRouteDestination = false;
                if (pathing != null && pathing.TryGetMovementTraceReadout(personId, out routeIdentity, out routeGeneration,
                    out routeWaypoint, out routeWaypointCount, out routeDestination, out hasRouteDestination,
                    out routeMode, out scheduleState, out activityLocationId, out moverVisible, out routeActive, out moverState)) { }
                string taskSummary = string.Join(" | ", tasks.Where(task => task != null && task.HasAssignee && task.AssigneeId.Id == personId)
                    .Select(task => task.TaskId + ":" + task.DefinitionId + ":" + task.Status + ":" + task.MinutesWorked + "/" + task.PlannedMinutes));
                trace.RecordPersonSnapshot(personId, population.Memberships?.GetActiveHouseholdId(personId) ?? person.householdId,
                    time.CurrentAbsoluteDayIndex, now, person.scheduleState.ToString(), person.currentDestinationBuildingId,
                    position, hasMovement ? movement : "No active physical route", remaining, eta, taskSummary,
                    routeIdentity, routeGeneration, routeWaypoint, routeWaypointCount,
                    hasRouteDestination ? routeDestination : (Vector3?)null, routeMode, scheduleState,
                    activityLocationId, moverVisible, routeActive, moverState);
            }
        }

        private static HashSet<int> ParseIds(string source)
        {
            var result = new HashSet<int>();
            if (string.IsNullOrWhiteSpace(source)) return result;
            foreach (string part in source.Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string value = part.Trim();
                if (value.StartsWith("P", StringComparison.OrdinalIgnoreCase) || value.StartsWith("H", StringComparison.OrdinalIgnoreCase)) value = value.Substring(1);
                if (int.TryParse(value, out int id) && id >= 0) result.Add(id);
            }
            return result;
        }

        private static void FinalizeActivityTrace()
        {
            if (traceExported || current == null || current.activityTrace == null || !current.activityTrace.enabled) return;
            SimulationSystemsHub hub = UnityEngine.Object.FindFirstObjectByType<SimulationSystemsHub>(FindObjectsInactive.Include);
            NpcActivityTraceRecorder trace = hub != null ? hub.ActivityTrace : null;
            if (trace == null)
            {
                current.activityTraceStatus = "NOT EXPORTED: activity recorder unavailable";
                traceExported = true;
                return;
            }
            trace.StopRecording("Playtest trace export.");
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string directory = string.IsNullOrWhiteSpace(current.activityTrace.exportPath)
                ? current.outputDirectory
                : Path.IsPathRooted(current.activityTrace.exportPath)
                    ? current.activityTrace.exportPath
                    : Path.Combine(root, current.activityTrace.exportPath);
            try
            {
                System.IO.Directory.CreateDirectory(directory);
                trace.WriteJsonLines(Path.Combine(directory, "npc-activity-events.jsonl"));
                trace.WriteReadableReport(Path.Combine(directory, "npc-activity-timeline.txt"));
                trace.WriteAnomalyReport(Path.Combine(directory, "npc-activity-anomalies.txt"));
                File.WriteAllText(Path.Combine(directory, "npc-activity-manifest.json"), JsonUtility.ToJson(new NpcActivityTraceManifest
                {
                    sessionId = trace.SessionId,
                    sourceBranch = trace.SourceBranch,
                    sourceRevision = trace.SourceRevision,
                    scenario = trace.Scenario,
                    seed = trace.Seed,
                    selectedSpeed = trace.SelectedSpeed,
                    traceLevel = trace.Level.ToString(),
                    samplingIntervalSimulatedSeconds = trace.SampleIntervalSeconds,
                    retainedEvents = trace.Events.Count,
                    anomalyCount = trace.Anomalies.Count,
                    droppedHistoryEvents = trace.DroppedHistoryEvents,
                    droppedSessionEvents = trace.DroppedSessionEvents
                }, true));
                current.activityTraceSessionId = trace.SessionId;
                current.activityTraceEventCount = trace.Events.Count;
                current.activityTraceAnomalyCount = trace.Anomalies.Count;
                current.activityTraceDroppedEventCount = trace.DroppedSessionEvents;
                current.activityTraceExportPath = directory;
                current.activityTraceStatus = "EXPORTED";
            }
            catch (Exception ex)
            {
                current.activityTraceStatus = "EXPORT FAILED: " + ex.GetType().Name + ": " + ex.Message;
                current.blocker = string.IsNullOrEmpty(current.blocker) ? current.activityTraceStatus : current.blocker + "\n" + current.activityTraceStatus;
            }
            traceExported = true;
        }

        private static bool TryPassRuntimeStartupGate(out string blocker, out TimeManager time)
        {
            blocker = string.Empty;
            time = UnityEngine.Object.FindFirstObjectByType<TimeManager>();
            FirstLedgerSliceBootstrapper bootstrapper = UnityEngine.Object.FindFirstObjectByType<FirstLedgerSliceBootstrapper>();
            TownWorldController town = UnityEngine.Object.FindFirstObjectByType<TownWorldController>();
            PopulationManager population = UnityEngine.Object.FindFirstObjectByType<PopulationManager>();
            SimulationSystemsHub hub = UnityEngine.Object.FindFirstObjectByType<SimulationSystemsHub>();
            SimulationDrivers drivers = UnityEngine.Object.FindFirstObjectByType<SimulationDrivers>();
            SharedBusinessRuntimeManager businesses = UnityEngine.Object.FindFirstObjectByType<SharedBusinessRuntimeManager>();
            SaveLoadManager save = UnityEngine.Object.FindFirstObjectByType<SaveLoadManager>();

            if (bootstrapper == null) { blocker = "FirstLedgerSliceBootstrapper is missing."; return false; }
            if (bootstrapper.HasStartupFailed) { blocker = bootstrapper.BootstrapStatus; return false; }
            if (!bootstrapper.IsInitialized) { blocker = "Bootstrapper has not reported initialized."; return false; }
            if (time == null || time.Settings == null) { blocker = "TimeManager or SimulationTimeSettings is missing."; return false; }
            if (time.Settings.GameSecondsPerDay != 86400f) { blocker = "Simulation day length is not the approved 86400 game seconds."; return false; }
            if (town == null || town.Grid == null || town.Buildings.Count == 0) { blocker = "Generated town or navigation grid is missing."; return false; }
            if (population?.State == null) { blocker = "PopulationManager has no authoritative PopulationState."; return false; }
            if (businesses == null) { blocker = "SharedBusinessRuntimeManager is missing."; return false; }
            if (hub == null || hub.Tasks == null) { blocker = "SimulationSystemsHub or TaskAuthority is missing."; return false; }
            if (drivers == null) { blocker = "SimulationDrivers is missing."; return false; }
            if (save?.Series2Pass9State == null) { blocker = "Series2Pass9 state is missing."; return false; }
            current.actualResidents = population.State.LivingResidentCount;
            current.actualPersonRecords = population.PersonRecordCount;
            if (current.actualResidents <= 0)
            { blocker = $"Population has no living on-map residents; person records={current.actualPersonRecords}."; return false; }
            if (current.expectedResidents >= 0
                && !population.State.HasExactLivingResidentCount(current.expectedResidents))
            { blocker = $"Population has {current.actualResidents}/{current.expectedResidents} configured living residents; person records={current.actualPersonRecords}."; return false; }
            current.actualHouseholds = population.State.households.Count;
            if (!HasExpectedStartupCount(current.actualHouseholds, current.expectedHouseholds))
            { blocker = $"Population has {current.actualHouseholds}/{current.expectedHouseholds} configured households at startup; count must match exactly."; return false; }
            current.actualBusinesses = businesses.Businesses?.Count ?? 0;
            if (!HasExpectedStartupCount(current.actualBusinesses, current.expectedBusinesses))
            { blocker = $"SharedBusinessRuntimeManager has {current.actualBusinesses}/{current.expectedBusinesses} configured businesses at startup; count must match exactly."; return false; }

            Series2Pass9SaveState pass9 = save.Series2Pass9State;
            var missingPass9 = new List<string>();
            if (pass9.StandRegistry == null) missingPass9.Add(nameof(pass9.StandRegistry));
            if (pass9.LoggingOperation == null) missingPass9.Add(nameof(pass9.LoggingOperation));
            if (pass9.LoggingCamp == null) missingPass9.Add(nameof(pass9.LoggingCamp));
            if (pass9.Sawmill == null) missingPass9.Add(nameof(pass9.Sawmill));
            if (pass9.LumberYard == null) missingPass9.Add(nameof(pass9.LumberYard));
            if (pass9.BakeryRuntime == null) missingPass9.Add(nameof(pass9.BakeryRuntime));
            if (missingPass9.Count > 0)
            { blocker = "Required Pass 9 holders are absent: " + string.Join(", ", missingPass9); return false; }

            // Validate the actual production driver's principal shared
            // references rather than inferring wiring from scene presence.
            if (!ReferenceEquals(ReadField(drivers, "hub"), hub)
                || !ReferenceEquals(ReadField(drivers, "timeManager"), time)
                || !ReferenceEquals(ReadField(drivers, "sharedBusinessRuntime"), businesses))
            { blocker = "SimulationDrivers does not reference the active hub, TimeManager and SharedBusinessRuntimeManager."; return false; }
            return true;
        }

        private static string FindRuntimeContinuityFailure()
        {
            if (UnityEngine.Object.FindFirstObjectByType<PopulationManager>()?.State == null)
                return "PopulationManager or authoritative PopulationState disappeared.";
            if (UnityEngine.Object.FindFirstObjectByType<SimulationSystemsHub>()?.Tasks == null)
                return "SimulationSystemsHub or TaskAuthority disappeared.";
            if (UnityEngine.Object.FindFirstObjectByType<SharedBusinessRuntimeManager>() == null)
                return "SharedBusinessRuntimeManager disappeared.";
            if (UnityEngine.Object.FindFirstObjectByType<SimulationDrivers>() == null)
                return "SimulationDrivers disappeared.";
            SaveLoadManager save = UnityEngine.Object.FindFirstObjectByType<SaveLoadManager>();
            if (save?.Series2Pass9State == null)
                return "SaveLoadManager or Pass 9 runtime state disappeared.";
            Series2Pass9SaveState pass9 = save.Series2Pass9State;
            if (pass9.StandRegistry == null || pass9.LoggingOperation == null || pass9.LoggingCamp == null
                || pass9.Sawmill == null || pass9.LumberYard == null || pass9.BakeryRuntime == null)
                return "one or more required Pass 9 runtime holders disappeared.";
            return string.Empty;
        }

        internal static StartupGateDecision ResolveStartupGateDecision(bool gatePassed,
            bool bootstrapInitialized, bool bootstrapFailed, bool timeoutElapsed)
        {
            if (gatePassed) return StartupGateDecision.Ready;
            if (bootstrapFailed || bootstrapInitialized) return StartupGateDecision.Invalid;
            return timeoutElapsed ? StartupGateDecision.TimedOut : StartupGateDecision.Waiting;
        }

        internal static bool HasExpectedStartupCount(int actual, int expected) =>
            expected >= 0 && actual == expected;

        internal static bool ShouldRestoreDayChangedSubscription(string status, bool editorIsPlaying, bool hasTimeManager) =>
            string.Equals(status, "RUNNING", StringComparison.Ordinal)
            && editorIsPlaying
            && hasTimeManager;

        internal static bool IsUnexpectedDomainReload(string status, bool editorIsPlaying,
            string savedMarker, string activeMarker) =>
            string.Equals(status, "RUNNING", StringComparison.Ordinal)
            && editorIsPlaying
            && !string.IsNullOrEmpty(savedMarker)
            && !string.Equals(savedMarker, activeMarker, StringComparison.Ordinal);

        internal static bool ShouldFinalizeAfterCaptureDetach(string status) =>
            string.Equals(status, "COMPLETED", StringComparison.Ordinal);

        internal static List<int> MissingSnapshotOffsets(int lastCapturedOffset, int throughOffset)
        {
            var missing = new List<int>();
            for (int offset = Math.Max(0, lastCapturedOffset) + 1; offset <= throughOffset; offset++)
                missing.Add(offset);
            return missing;
        }

        private static void AppendMissingSnapshotsThrough(int lastCapturedOffset, int throughOffset)
        {
            foreach (int offset in MissingSnapshotOffsets(lastCapturedOffset, throughOffset))
            {
                if (current?.missingDailySnapshotOffsets != null
                    && !current.missingDailySnapshotOffsets.Contains(offset))
                    current.missingDailySnapshotOffsets.Add(offset);
                AppendMissingDay(offset);
            }
        }

        private static void RestoreRuntimeDayChangedSubscription()
        {
            if (current == null || !string.Equals(current.status, "RUNNING", StringComparison.Ordinal)
                || !EditorApplication.isPlaying) return;
            TimeManager time = UnityEngine.Object.FindAnyObjectByType<TimeManager>();
            if (time == null || !ShouldRestoreDayChangedSubscription(current.status, true, true)
                || subscribedTimeManager == time) return;

            if (subscribedTimeManager != null)
                subscribedTimeManager.DayChanged -= OnDayChanged;
            time.DayChanged -= OnDayChanged;
            time.DayChanged += OnDayChanged;
            subscribedTimeManager = time;
            Volatile.Write(ref currentSimulationContext,
                "day=" + time.CurrentAbsoluteDayIndex + "; date=" + time.CurrentDate);
        }

        private static object ReadField(object target, string name) =>
            target?.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(target);

        private static void OnDayChanged(SimulationDateChangedContext context)
        {
            if (current == null || current.status != "RUNNING") return;
            int offset = Mathf.Clamp(context.CurrentDate.AbsoluteDayIndex - current.startAbsoluteDayIndex, 0, current.targetDays);
            if (offset <= current.completedDays) return;
            AppendMissingSnapshotsThrough(current.completedDays, offset - 1);
            current.completedDays = offset;
            Volatile.Write(ref currentSimulationContext, "day=" + context.CurrentDate.AbsoluteDayIndex + "; date=" + context.CurrentDate);
            AppendDaySnapshot(context.CurrentDate);
            SaveSession();
        }

        private static void UnsubscribeDayEvents()
        {
            if (subscribedTimeManager == null) return;
            subscribedTimeManager.DayChanged -= OnDayChanged;
            subscribedTimeManager = null;
        }

        private static void AppendDaySnapshot(SimulationDate date)
        {
            PopulationManager population = UnityEngine.Object.FindFirstObjectByType<PopulationManager>();
            SimulationSystemsHub hub = UnityEngine.Object.FindFirstObjectByType<SimulationSystemsHub>();
            SharedBusinessRuntimeManager businesses = UnityEngine.Object.FindFirstObjectByType<SharedBusinessRuntimeManager>();
            PopulationPathingDirector pathing = UnityEngine.Object.FindFirstObjectByType<PopulationPathingDirector>();
            DaySnapshot day = new()
            {
                runId = current.runId,
                absoluteDayIndex = date.AbsoluteDayIndex,
                gameDate = date.ToString(),
                wallUtc = DateTime.UtcNow.ToString("O"),
                speed = UnityEngine.Object.FindFirstObjectByType<TimeManager>()?.CurrentSpeed.ToString() ?? "NOT_CAPTURED",
                residentCount = population?.State?.LivingResidentCount ?? -1,
                personRecordCount = population?.PersonRecordCount ?? -1,
                activeHouseholdMembershipCount = population?.Memberships?.ActiveCount ?? -1,
                householdCount = population?.State?.households?.Count ?? -1,
                businessCount = businesses?.Businesses?.Count ?? -1
            };
            // DayChanged fires at the start of the new date. Meal totals
            // therefore belong to the just-completed date, and are read from
            // the persisted meal authority rather than the day-start needs
            // pass (which intentionally leaves meals for their clock slots).
            int completedMealDayIndex = date.AbsoluteDayIndex - 1;
            if (completedMealDayIndex >= 0 && hub?.MealLog != null && population?.State?.households != null)
            {
                var mealRecords = population.State.households
                    .Where(household => household != null)
                    .SelectMany(household => hub.MealLog.GetMeals(household.id))
                    .Where(record => record != null && record.DayIndex == completedMealDayIndex)
                    .ToList();
                var missedRecords = population.State.households
                    .Where(household => household != null)
                    .SelectMany(household => hub.MealLog.GetMissedMeals(household.id))
                    .Where(record => record != null && record.DayIndex == completedMealDayIndex)
                    .ToList();
                day.mealDayIndex = completedMealDayIndex;
                day.householdsServedMeals = mealRecords.Select(record => record.HouseholdId).Distinct().Count();
                day.mealsEaten = mealRecords.Count;
                day.mealsMissed = missedRecords.Count;
                // Meal costs are distinct from retail shopping. Do not copy
                // the legacy daily purchase report into these fields; the
                // current physical shopping event stream is not yet exposed
                // as a durable daily aggregate.
                day.mealPathPurchases = -1;
                day.mealPathSpendCents = -1;
                day.mealTotals = $"completedDay={completedMealDayIndex}; householdsWithMeals={day.householdsServedMeals}; " +
                    $"eaten={day.mealsEaten}; missed={day.mealsMissed}; physicalShoppingPurchases=NOT_CAPTURED; " +
                    "purchase totals are not inferred from the legacy meal-path report.";
            }
            else
            {
                day.mealTotals = completedMealDayIndex < 0
                    ? "NOT_CAPTURED: no completed simulation day exists before this snapshot"
                    : "NOT_CAPTURED: household meal log or scenario population is unavailable";
            }
            if (hub?.Tasks != null)
            {
                DailyTaskLifecycleSnapshot tasks = hub.Tasks.CaptureDailyLifecycleSnapshot(date.AbsoluteDayIndex);
                day.queuedTasks = tasks.Queued;
                day.assignedTasks = tasks.Assigned;
                day.inProgressTasks = tasks.InProgress;
                day.pausedTasks = tasks.Paused;
                day.dependencyWaitingTasks = tasks.WaitingOnDependency;
                day.passiveWaitingTasks = tasks.WaitingOnPassiveProcess;
                day.resourcesReservedTasks = tasks.ResourcesReserved;
                day.reworkTasks = tasks.ReworkRequired;
                day.interruptedTasks = tasks.Interrupted;
                day.completedTasksToday = tasks.CompletedOnDay;
                day.failedTasksToday = tasks.FailedOnDay;
                day.cancelledTasksRetained = tasks.CancelledRetained;
                day.taskCompletion = $"open: queued={tasks.Queued}; assigned={tasks.Assigned}; inProgress={tasks.InProgress}; paused={tasks.Paused}; waitingDependency={tasks.WaitingOnDependency}; waitingPassive={tasks.WaitingOnPassiveProcess}; resourcesReserved={tasks.ResourcesReserved}; rework={tasks.ReworkRequired}; interrupted={tasks.Interrupted}; terminalToday: completed={tasks.CompletedOnDay}; failed={tasks.FailedOnDay}; cancelledRetainedHistory={tasks.CancelledRetained} (cancellations have no day stamp in the current task model)";
                AppendActivityTaskTrace(date, hub);
            }
            else
            {
                day.taskCompletion = "NOT_CAPTURED: SimulationSystemsHub or TaskAuthority is unavailable";
            }
            ShoppingTripDriver shopping = hub?.ShoppingTripDriver;
            if (shopping != null)
            {
                var activeTrips = new List<string>();
                foreach (PlannedShoppingTrip trip in shopping.ActiveTrips)
                {
                    if (trip == null || !trip.IsActive) continue;
                    string movement = "NOT_CAPTURED";
                    float remainingMeters = -1f;
                    float etaGameSeconds = -1f;
                    if (pathing != null && pathing.TryGetMovementReadout(trip.ShopperPersonId,
                            out string status, out float distance, out float eta))
                    {
                        movement = status ?? string.Empty;
                        remainingMeters = distance;
                        etaGameSeconds = eta;
                    }
                    string route = "NOT_CAPTURED";
                    if (pathing != null && pathing.TryGetMovementTraceReadout(trip.ShopperPersonId,
                            out string identity, out int generation, out int waypoint, out int waypointCount,
                            out Vector3 destination, out bool hasDestination, out string routeMode,
                            out string schedule, out string activityLocation, out bool visible,
                            out bool routeActive, out string moverState))
                    {
                        route = $"id={identity};gen={generation};waypoint={waypoint}/{waypointCount};" +
                            $"destination={(hasDestination ? destination.ToString("F2") : "none")};mode={routeMode};" +
                            $"schedule={schedule};activity={activityLocation};visible={visible};active={routeActive};mover={moverState}";
                    }
                    string tripNeed = trip.Need != null
                        ? trip.Need.NeedSequence + ":" + trip.Need.ItemId
                        : "unknown";
                    string liveness =
                        $"{trip.TripId}:H{trip.HouseholdId}/P{trip.ShopperPersonId}/{trip.State}/need={tripNeed}" +
                        $"/seller={trip.SellerLocationIdForDiagnostics}/outboundTask={trip.OutboundTaskIdForDiagnostics}" +
                        $"/routeWatch[leg={trip.ActiveRouteLegKind};started={trip.ActiveRouteStartedGameSeconds:0.##};" +
                        $"deadline={trip.ActiveRouteDeadlineGameSeconds:0.##};lastProgress={trip.ActiveRouteLastProgressGameSeconds:0.##};" +
                        $"remaining={trip.ActiveRouteLastRemainingSeconds:0.##};sampled={trip.ActiveRouteHasProgressSample}]" +
                        $"/movement[{movement};remainingM={remainingMeters:0.##};etaGameS={etaGameSeconds:0.##};{route}]" +
                        $"/failure={trip.FailureReason}";
                    activeTrips.Add(liveness);
                }
                day.activeShoppingTripCount = activeTrips.Count;
                day.activeShoppingTrips = activeTrips.Count == 0 ? "none" : string.Join(" | ", activeTrips);
                AppendEntitySnapshot(new EntitySnapshot
                {
                    entityType = "shopping-trip-liveness",
                    id = "day-" + date.AbsoluteDayIndex,
                    ownerOrName = "ShoppingTripDriver",
                    detail = day.activeShoppingTrips
                });
            }
            long householdLotUnits = 0;
            long householdCash = 0;
            bool householdInventoryMissing = false;
            bool householdLedgerMissing = false;
            if (population?.State?.households != null && hub != null)
            {
                foreach (HouseholdState household in population.State.households)
                {
                    HouseholdLedger ledger = hub.HouseholdLedgers.Get(household.id);
                    if (ledger == null) householdLedgerMissing = true;
                    else householdCash += ledger.GetBalanceCents();
                    HouseholdInventory inventory = hub.HouseholdInventories.Get(household.id);
                    if (inventory == null) householdInventoryMissing = true;
                    else
                    {
                        foreach (HouseholdItemLot lot in inventory.Lots)
                        {
                            householdLotUnits += Math.Max(0, lot.QuantityUnits);
                            AppendEntitySnapshot(new EntitySnapshot
                            {
                                entityType = "household-lot",
                                id = "H" + household.id + ":" + lot.LotId,
                                ownerOrName = household.householdName ?? string.Empty,
                                locationOrPremises = household.homeBuildingId.ToString(),
                                inventoryLotCount = 1,
                                inventoryUnits = lot.QuantityUnits,
                                inventoryStatus = "CAPTURED_FROM_HouseholdInventoryRegistry",
                                detail = "item=" + lot.ItemId + "; held=" + lot.HeldUnits + "; source=" + lot.SourceLabel
                            });
                        }
                    }
                    AppendEntitySnapshot(new EntitySnapshot
                    {
                        entityType = "household",
                        id = "H" + household.id,
                        ownerOrName = household.householdName ?? string.Empty,
                        locationOrPremises = household.homeBuildingId.ToString(),
                        cashCents = ledger?.GetBalanceCents() ?? -1,
                        cashStatus = ledger != null ? "CAPTURED_FROM_HouseholdLedger" : "NOT_CAPTURED_NO_LEDGER",
                        inventoryLotCount = inventory?.Lots?.Count ?? -1,
                        inventoryStatus = inventory != null ? "CAPTURED_FROM_HouseholdInventoryRegistry" : "NOT_CAPTURED_NO_INVENTORY",
                        detail = population.Memberships == null
                            ? "NOT_CAPTURED_NO_HOUSEHOLD_MEMBERSHIP_REGISTRY"
                            : "activeMemberIds=" + string.Join("|", population.Memberships.GetActiveMembers(household.id))
                    });
                }
            }
            day.householdCashCents = householdLedgerMissing ? -1 : householdCash;
            day.householdLotUnits = householdInventoryMissing ? -1 : householdLotUnits;

            long businessCash = 0;
            long retailUnits = 0;
            bool businessInventoryMissing = false;
            if (businesses?.Businesses != null)
            {
                foreach (BusinessInstanceState business in businesses.Businesses)
                {
                    if (business?.RuntimeState == null) continue;
                    businessCash += business.RuntimeState.CurrentCashCents;
                    IReadOnlyList<BusinessRetailItemLotState> lots = business.RuntimeState.RetailItemLots;
                    if (lots == null) businessInventoryMissing = true;
                    else foreach (BusinessRetailItemLotState lot in lots) retailUnits += Math.Max(0, lot.QuantityUnits);
                    AppendEntitySnapshot(new EntitySnapshot
                    {
                        entityType = "business",
                        id = business.InstanceId,
                        ownerOrName = business.RuntimeDisplayName,
                        locationOrPremises = business.AssignedBuildingId.ToString(),
                        cashCents = business.RuntimeState.CurrentCashCents,
                        cashStatus = "CAPTURED_FROM_BusinessRuntimeState",
                        inventoryLotCount = lots?.Count ?? -1,
                        inventoryUnits = lots == null ? -1 : lots.Sum(lot => Math.Max(0, lot.QuantityUnits)),
                        inventoryStatus = lots != null ? "CAPTURED_FROM_BusinessRetailItemLots" : "NOT_CAPTURED_NO_RETAIL_LOT_VIEW"
                    });
                    if (lots != null)
                        foreach (BusinessRetailItemLotState lot in lots)
                            AppendEntitySnapshot(new EntitySnapshot
                            {
                                entityType = "business-lot",
                                id = business.InstanceId + ":" + lot.LotId,
                                ownerOrName = lot.OwnerBusinessId,
                                locationOrPremises = lot.CustodianBusinessId,
                                inventoryLotCount = 1,
                                inventoryUnits = lot.QuantityUnits,
                                inventoryStatus = "CAPTURED_FROM_BusinessRuntimeState",
                                detail = "item=" + lot.ItemId + "; reserved=" + lot.ReservedUnits + "; provenance=" + lot.Provenance
                            });
                }
            }
            day.businessCashCents = businessCash;
            day.retailLotUnits = businessInventoryMissing ? -1 : retailUnits;
            string root = System.IO.Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(root)) return;
            string folder = current.outputDirectory;
            AppendLine(Path.Combine(folder, "daily-summary.csv"),
                Csv(day.absoluteDayIndex.ToString()) + "," + Csv(day.gameDate) + "," + Csv(day.wallUtc) + "," + Csv(day.speed) + "," +
                day.residentCount + "," + day.personRecordCount + "," + day.activeHouseholdMembershipCount + "," + day.householdCount + "," + day.businessCount + "," + day.householdCashCents + "," +
                day.businessCashCents + "," + day.householdLotUnits + "," + day.retailLotUnits + "," + day.mealDayIndex + "," +
                day.householdsServedMeals + "," + day.mealsEaten + "," + day.mealsMissed + "," + day.mealPathPurchases + "," + day.mealPathSpendCents + "," +
                day.queuedTasks + "," + day.assignedTasks + "," + day.inProgressTasks + "," + day.pausedTasks + "," + day.dependencyWaitingTasks + "," +
                day.passiveWaitingTasks + "," + day.resourcesReservedTasks + "," + day.reworkTasks + "," + day.interruptedTasks + "," +
                day.completedTasksToday + "," + day.failedTasksToday + "," + day.cancelledTasksRetained + "," + Csv(day.mealTotals) + "," +
                Csv(day.taskCompletion) + "," + Csv(day.movementOutcomes) + "," + Csv(day.freightAndConstruction) + "," +
                day.activeShoppingTripCount + "," + Csv(day.activeShoppingTrips));
            AppendLine(Path.Combine(folder, "entity-snapshots.jsonl"), JsonUtility.ToJson(day));
        }

        private static void AppendMissingDay(int offset)
        {
            AppendLine(Path.Combine(current.outputDirectory, "daily-summary.csv"),
                "NOT_CAPTURED,day-offset-" + offset + "," + Csv(DateTime.UtcNow.ToString("O")) + ",NOT_CAPTURED,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1," +
                Csv("NOT_CAPTURED_EVENT_GAP") + "," + Csv("NOT_CAPTURED") + "," + Csv("NOT_CAPTURED") + "," + Csv("NOT_CAPTURED") + ",-1," + Csv("NOT_CAPTURED"));
        }

        private static void AppendEntitySnapshot(EntitySnapshot entity) =>
            AppendLine(Path.Combine(current.outputDirectory, "entity-snapshots.jsonl"), JsonUtility.ToJson(entity));

        private static void AppendActivityTaskTrace(SimulationDate date, SimulationSystemsHub hub)
        {
            PopulationPathingDirector movementDirector = UnityEngine.Object.FindFirstObjectByType<PopulationPathingDirector>();
            PersonMovementReadoutProvider movementReadout = movementDirector != null
                ? new PersonMovementReadoutProvider(movementDirector.TryGetMovementReadout)
                : null;
            PersonOperationalReadoutProvider operationalReadout = movementDirector != null
                ? new PersonOperationalReadoutProvider(movementDirector.TryGetPersonOperationalReadout)
                : null;
            List<ActivityTaskTraceSnapshot> snapshots = BuildActivityTaskTraceSnapshots(
                current != null ? current.runId : string.Empty,
                date.AbsoluteDayIndex,
                UnityEngine.Object.FindFirstObjectByType<TimeManager>()?.AbsoluteGameSeconds ?? -1f,
                hub?.Tasks,
                hub?.DurationDiagnostics,
                hub?.PersonSchedules,
                movementReadout,
                operationalReadout);
            var lines = new System.Text.StringBuilder();
            foreach (ActivityTaskTraceSnapshot snapshot in snapshots)
                lines.Append(JsonUtility.ToJson(snapshot)).Append("\r\n");
            if (lines.Length > 0)
                File.AppendAllText(Path.Combine(current.outputDirectory, "activity-task-trace.jsonl"), lines.ToString());
        }

        internal static List<ActivityTaskTraceSnapshot> BuildActivityTaskTraceSnapshots(
            string runId,
            int absoluteDayIndex,
            float capturedGameSeconds,
            TaskAuthority tasks,
            TaskDurationDiagnostics timing,
            PersonScheduleTracker schedules,
            PersonMovementReadoutProvider movementReadout = null,
            PersonOperationalReadoutProvider operationalReadout = null)
        {
            var result = new List<ActivityTaskTraceSnapshot>();
            int routeReadoutsAttempted = 0;
            int routeReadoutsAvailable = 0;
            int routeReadoutsUnavailable = 0;
            if (tasks == null)
            {
                result.Add(new ActivityTaskTraceSnapshot
                {
                    recordType = "snapshot-summary",
                    runId = runId ?? string.Empty,
                    absoluteDayIndex = absoluteDayIndex,
                    capturedUtc = DateTime.UtcNow.ToString("O"),
                    capturedGameSeconds = capturedGameSeconds,
                    traceCoverage = "NOT_CAPTURED: TaskAuthority is unavailable",
                    authoritativeOpenTaskCount = -1,
                    openTasksWithTimingRecord = -1,
                    openTasksWithoutTimingRecord = -1,
                    currentDayReservationCount = schedules != null ? CountCurrentDayReservations(schedules, absoluteDayIndex) : -1,
                    physicalRouteReadoutsAttempted = 0,
                    physicalRouteReadoutsAvailable = 0,
                    physicalRouteReadoutsUnavailable = 0
                });
                return result;
            }

            List<WorkTask> openTasks = tasks.GetAllOpenTasks();
            int withTiming = 0;
            int withoutTiming = 0;
            var tracedTaskIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (WorkTask task in openTasks)
            {
                if (task == null) continue;
                TaskTimingRecord record = timing?.GetRecord(task.TaskId.ToString());
                if (record != null) withTiming++;
                else withoutTiming++;
                ActivityTaskTraceSnapshot taskSnapshot = CreateTaskTraceSnapshot(
                    runId, absoluteDayIndex, capturedGameSeconds, task, record, movementReadout, operationalReadout);
                result.Add(taskSnapshot);
                if (movementReadout != null && taskSnapshot.physicalRoutePersonId >= 0)
                {
                    routeReadoutsAttempted++;
                    if (taskSnapshot.physicalRouteState.StartsWith("UNAVAILABLE:", StringComparison.Ordinal))
                        routeReadoutsUnavailable++;
                    else if (!taskSnapshot.physicalRouteState.StartsWith("NOT_CAPTURED:", StringComparison.Ordinal))
                        routeReadoutsAvailable++;
                }
                tracedTaskIds.Add(task.TaskId.ToString());
            }

            // Include tasks that reached a dated terminal state today. Historical
            // terminal records are not repeated on every later day.
            if (timing != null)
            {
                foreach (TaskTimingRecord record in timing.GetAllRecords())
                {
                    if (record == null || string.IsNullOrEmpty(record.TaskId)
                        || tracedTaskIds.Contains(record.TaskId)) continue;
                    WorkTask task = tasks.FindTask(record.TaskId);
                    if (task == null || (task.Status != LandLedgers.Tasks.TaskStatus.Complete
                        && task.Status != LandLedgers.Tasks.TaskStatus.Failed)
                        || task.CompletedDayIndex != absoluteDayIndex) continue;
                    ActivityTaskTraceSnapshot taskSnapshot = CreateTaskTraceSnapshot(
                        runId, absoluteDayIndex, capturedGameSeconds, task, record, movementReadout, operationalReadout);
                    result.Add(taskSnapshot);
                    if (movementReadout != null && taskSnapshot.physicalRoutePersonId >= 0)
                    {
                        routeReadoutsAttempted++;
                        if (taskSnapshot.physicalRouteState.StartsWith("UNAVAILABLE:", StringComparison.Ordinal))
                            routeReadoutsUnavailable++;
                        else if (!taskSnapshot.physicalRouteState.StartsWith("NOT_CAPTURED:", StringComparison.Ordinal))
                            routeReadoutsAvailable++;
                    }
                    tracedTaskIds.Add(record.TaskId);
                }
            }

            int reservationCount = schedules != null
                ? CountCurrentDayReservations(schedules, absoluteDayIndex)
                : -1;
            foreach (ActivityTaskTraceSnapshot taskSnapshot in result)
            {
                if (taskSnapshot.recordType == "task")
                {
                    taskSnapshot.authoritativeOpenTaskCount = openTasks.Count;
                    taskSnapshot.openTasksWithTimingRecord = withTiming;
                    taskSnapshot.openTasksWithoutTimingRecord = withoutTiming;
                    taskSnapshot.currentDayReservationCount = reservationCount;
                }
            }
            if (schedules != null)
            {
                foreach (PersonActivityReservation reservation in schedules.Reservations)
                {
                    if (reservation == null || reservation.Released || reservation.DayIndex != absoluteDayIndex) continue;
                    ActivityTaskTraceSnapshot reservationSnapshot = new ActivityTaskTraceSnapshot
                    {
                        recordType = "activity-reservation",
                        runId = runId ?? string.Empty,
                        absoluteDayIndex = absoluteDayIndex,
                        capturedUtc = DateTime.UtcNow.ToString("O"),
                        capturedGameSeconds = capturedGameSeconds,
                        traceCoverage = "AUTHORITATIVE_UNRELEASED_PERSON_SCHEDULE_RESERVATION",
                        reservationId = reservation.ReservationId,
                        personId = reservation.PersonId,
                        activity = reservation.Activity.ToString(),
                        startMinuteOfDay = reservation.StartMinuteOfDay,
                        durationMinutes = reservation.DurationMinutes,
                        endMinuteOfDay = reservation.EndMinuteOfDay,
                        reservationLabel = reservation.Label,
                        released = reservation.Released,
                        physicalRouteState = "NOT_CAPTURED: no movement readout supplied"
                    };
                    if (movementReadout != null)
                    {
                        routeReadoutsAttempted++;
                        if (TryCapturePhysicalMovement(reservationSnapshot, reservation.PersonId, movementReadout))
                            routeReadoutsAvailable++;
                        else
                            routeReadoutsUnavailable++;
                    }
                    TryCapturePersonOperationalState(reservationSnapshot, reservation.PersonId, operationalReadout);
                    result.Add(reservationSnapshot);
                }
            }
            string routeCoverage = movementReadout == null
                ? "physical route state NOT_CAPTURED because the movement readout authority is unavailable"
                : $"physical route readouts {routeReadoutsAvailable}/{routeReadoutsAttempted} available, {routeReadoutsUnavailable} without a managed mover";
            result.Insert(0, new ActivityTaskTraceSnapshot
            {
                recordType = "snapshot-summary",
                runId = runId ?? string.Empty,
                absoluteDayIndex = absoluteDayIndex,
                capturedUtc = DateTime.UtcNow.ToString("O"),
                capturedGameSeconds = capturedGameSeconds,
                traceCoverage = withoutTiming == 0
                    ? "AUTHORITATIVE_OPEN_TASKS_AND_UNRELEASED_SCHEDULES; " + routeCoverage
                    : "AUTHORITATIVE_OPEN_TASKS; some timing sidecars were not observed; " + routeCoverage,
                authoritativeOpenTaskCount = openTasks.Count,
                openTasksWithTimingRecord = withTiming,
                openTasksWithoutTimingRecord = withoutTiming,
                currentDayReservationCount = reservationCount,
                physicalRouteReadoutsAttempted = routeReadoutsAttempted,
                physicalRouteReadoutsAvailable = routeReadoutsAvailable,
                physicalRouteReadoutsUnavailable = routeReadoutsUnavailable
            });
            return result;
        }

        internal static ActivityTaskTraceSnapshot CreateTaskTraceSnapshot(
            string runId,
            int absoluteDayIndex,
            float capturedGameSeconds,
            WorkTask task,
            TaskTimingRecord timing,
            PersonMovementReadoutProvider movementReadout = null,
            PersonOperationalReadoutProvider operationalReadout = null)
        {
            if (task == null) return null;
            var snapshot = new ActivityTaskTraceSnapshot
            {
                recordType = "task",
                runId = runId ?? string.Empty,
                absoluteDayIndex = absoluteDayIndex,
                capturedUtc = DateTime.UtcNow.ToString("O"),
                capturedGameSeconds = capturedGameSeconds,
                taskId = task.TaskId.ToString(),
                definitionId = task.DefinitionId,
                taskStatus = task.Status.ToString(),
                taskOutcome = task.Outcome.ToString(),
                ownerId = task.OwnerId.ToString(),
                assigneeId = task.HasAssignee ? task.AssigneeId.ToString() : string.Empty,
                targetDescription = task.TargetDescription ?? string.Empty,
                requiredCapability = task.RequiredCapability ?? string.Empty,
                workLocationId = task.LocationId ?? string.Empty,
                workedMinutes = task.MinutesWorked,
                plannedMinutes = task.PlannedMinutes,
                remainingMinutes = task.MinutesRemaining,
                interruptionCount = task.InterruptionCount,
                reservationHandleId = task.ReservationHandleId,
                dueEventId = task.DueEventId,
                dependencyDescription = task.DependencyDescription,
                lastTransition = timing?.LastTransitionKind ?? string.Empty,
                lastTransitionDay = timing != null ? timing.LastTransitionDay : -1,
                lastTransitionSeconds = timing != null ? timing.LastTransitionSeconds : -1f,
                lastProgressSeconds = timing != null ? timing.LastProgressSeconds : -1f,
                lastBlockReason = timing?.LastBlockReason ?? string.Empty,
                lastRefusal = timing?.LastRefusal ?? string.Empty,
                lastRefusalMethod = timing?.LastRefusalMethod ?? string.Empty,
                lastRefusalDay = timing != null ? timing.LastRefusalDay : -1,
                lastNoWorkerReason = timing?.LastNoWorkerReason ?? string.Empty,
                lastNoWorkerDay = timing != null ? timing.LastNoWorkerDay : -1,
                consecutiveNoWorkerDays = timing != null ? timing.ConsecutiveNoWorkerDays : -1,
                timingClockPrecision = timing != null ? timing.ClockPrecision.ToString() : "NOT_OBSERVED",
                timingRebuiltAfterLoad = timing != null && timing.RebuiltAfterLoad,
                timingRecordStatus = timing != null ? "CAPTURED_FROM_TaskDurationDiagnostics" : "NOT_OBSERVED",
                physicalRouteState = task.HasAssignee && task.AssigneeId.Kind == LandLedgers.Primitives.EntityKind.Person
                    ? "NOT_CAPTURED: no movement readout supplied"
                    : task.HasAssignee
                        ? "NOT_APPLICABLE: assignee is not a person identity"
                        : "NOT_APPLICABLE: task has no assigned person"
            };
            if (task.HasAssignee && task.AssigneeId.Kind == LandLedgers.Primitives.EntityKind.Person)
            {
                TryCapturePhysicalMovement(snapshot, task.AssigneeId.Id, movementReadout);
                TryCapturePersonOperationalState(snapshot, task.AssigneeId.Id, operationalReadout);
            }
            return snapshot;
        }

        private static bool TryCapturePersonOperationalState(
            ActivityTaskTraceSnapshot snapshot,
            int personId,
            PersonOperationalReadoutProvider operationalReadout)
        {
            if (snapshot == null) return false;
            if (operationalReadout == null)
            {
                snapshot.personOperationalReadoutStatus = "NOT_CAPTURED: operational movement authority is unavailable";
                return false;
            }

            if (!operationalReadout(personId,
                    out int homeBuildingId,
                    out int workplaceBuildingId,
                    out int currentDestinationBuildingId,
                    out string scheduleState,
                    out string routeMode,
                    out bool moverVisible,
                    out bool routeActive))
            {
                snapshot.personOperationalReadoutStatus = $"UNAVAILABLE: no managed person readout for P{personId}";
                return false;
            }

            snapshot.personHomeBuildingId = homeBuildingId;
            snapshot.personWorkplaceBuildingId = workplaceBuildingId;
            snapshot.personCurrentDestinationBuildingId = currentDestinationBuildingId;
            snapshot.personScheduleState = scheduleState ?? string.Empty;
            snapshot.personRouteMode = routeMode ?? string.Empty;
            snapshot.personMoverVisible = moverVisible;
            snapshot.personRouteActive = routeActive;
            snapshot.personOperationalReadoutStatus = "CAPTURED_FROM_PopulationPathingDirector";
            return true;
        }

        private static bool TryCapturePhysicalMovement(
            ActivityTaskTraceSnapshot snapshot,
            int personId,
            PersonMovementReadoutProvider movementReadout)
        {
            if (snapshot == null) return false;
            snapshot.physicalRoutePersonId = personId;
            if (movementReadout == null)
            {
                snapshot.physicalRouteState = "NOT_CAPTURED: movement readout authority is unavailable";
                return false;
            }

            if (!movementReadout(personId, out string status, out float remainingDistance, out float eta))
            {
                snapshot.physicalRouteState = $"UNAVAILABLE: no managed mover readout for person P{personId}";
                return false;
            }

            snapshot.physicalRouteState = string.IsNullOrWhiteSpace(status) ? "Movement status unspecified" : status;
            snapshot.physicalRouteRemainingDistanceMeters = IsFiniteNonnegative(remainingDistance) ? remainingDistance : -1f;
            snapshot.physicalRouteEstimatedGameSecondsToArrival = IsFiniteNonnegative(eta) ? eta : -1f;
            return true;
        }

        private static bool IsFiniteNonnegative(float value)
        {
            return value >= 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static int CountCurrentDayReservations(PersonScheduleTracker schedules, int dayIndex)
        {
            if (schedules == null) return -1;
            int count = 0;
            foreach (PersonActivityReservation reservation in schedules.Reservations)
                if (reservation != null && !reservation.Released && reservation.DayIndex == dayIndex) count++;
            return count;
        }

        private static void AttachLogCapture()
        {
            if (subscribedToLogs) return;
            Application.logMessageReceivedThreaded += OnUnityLog;
            subscribedToLogs = true;
        }

        private static void DetachLogCapture()
        {
            if (!subscribedToLogs) return;
            Application.logMessageReceivedThreaded -= OnUnityLog;
            subscribedToLogs = false;
            DrainConsoleQueue();
        }

        private static void OnUnityLog(string condition, string stackTrace, LogType type)
        {
            if (current == null || (current.status != "ARMED" && current.status != "RUNNING")) return;
            int sequence = Interlocked.Increment(ref current.totalConsoleEventCount);
            Interlocked.Increment(ref current.logCount);
            if (type == LogType.Log) Interlocked.Increment(ref current.informationalLogCount);
            if (type == LogType.Warning) Interlocked.Increment(ref current.warningCount);
            if (type == LogType.Error || type == LogType.Assert || type == LogType.Exception)
                Interlocked.Increment(ref current.errorCount);
            if (type == LogType.Exception)
            {
                Interlocked.Increment(ref current.exceptionCount);
                Volatile.Write(ref exceptionObserved, true);
            }
            PendingConsole.Enqueue(new ConsoleEvent
            {
                sequence = sequence,
                wallUtc = DateTime.UtcNow.ToString("O"),
                severity = type.ToString(),
                simulationContext = Volatile.Read(ref currentSimulationContext),
                message = condition ?? string.Empty,
                stackTrace = stackTrace ?? string.Empty
            });
        }

        private static void DrainConsoleQueue()
        {
            if (current == null || string.IsNullOrWhiteSpace(current.outputDirectory)) return;
            string fullPath = Path.Combine(current.outputDirectory, "console-full.txt");
            string jsonlPath = Path.Combine(current.outputDirectory, "console-events.jsonl");
            System.Text.StringBuilder fullBatch = new();
            System.Text.StringBuilder jsonlBatch = new();
            while (PendingConsole.TryDequeue(out ConsoleEvent item))
            {
                string line = "[" + item.sequence + "] [" + item.wallUtc + "] [" + item.severity + "] [" + item.simulationContext + "] " + item.message;
                if (!string.IsNullOrEmpty(item.stackTrace)) line += "\r\n" + item.stackTrace;
                fullBatch.Append(line).Append("\r\n");
                jsonlBatch.Append(JsonUtility.ToJson(item)).Append("\r\n");
            }
            if (fullBatch.Length > 0) File.AppendAllText(fullPath, fullBatch.ToString());
            if (jsonlBatch.Length > 0) File.AppendAllText(jsonlPath, jsonlBatch.ToString());
        }

        private static void WriteFinalArtifacts()
        {
            if (current == null || string.IsNullOrWhiteSpace(current.outputDirectory)) return;
            DrainConsoleQueue();
            TimeManager time = UnityEngine.Object.FindFirstObjectByType<TimeManager>();
            if (time != null && !ShouldFinalizeAfterCaptureDetach(current.status))
            {
                current.endAbsoluteDayIndex = time.CurrentAbsoluteDayIndex;
                current.endGameDate = time.CurrentDate.ToString();
            }
            File.WriteAllText(Path.Combine(current.outputDirectory, "metadata.json"), JsonUtility.ToJson(current, true));
            var finalState = new FinalState
            {
                runId = current.runId,
                result = current.result,
                economicAssessment = current.economicAssessment,
                daysCompleted = current.completedDays,
                calendarIntervalCompleted = current.completedDays >= current.targetDays,
                finalInvariant = current.finalInvariant,
                missingDailySnapshotOffsets = current.missingDailySnapshotOffsets?.ToArray() ?? Array.Empty<int>(),
                unavailableDetails = new List<string>
                {
                    "Physical movement outcomes: authoritative per-day route success/failure aggregate is not currently exposed.",
                    "Freight settlement and construction stage aggregates: subsystem-owned records are not yet captured by this runner.",
                    "Cancellation lacks a day stamp in WorkTask; task snapshots report retained cancellation count rather than a daily count.",
                    "Untracked working-tree entries are not enumerated because the project contains large owner-local asset packs."
                }
            };
            File.WriteAllText(Path.Combine(current.outputDirectory, "final-state.json"), JsonUtility.ToJson(finalState, true));
            File.WriteAllText(Path.Combine(current.outputDirectory, "playtest-report.md"),
                "# Playtest " + current.runId + "\r\n\r\n" +
                "- Result: **" + current.result + "**\r\n- Calendar interval: " + (current.completedDays >= current.targetDays ? "complete" : "incomplete") + "\r\n" +
                "- Economic assessment: **" + current.economicAssessment + "**\r\n- Status: " + current.status + "\r\n- Completed days: " + current.completedDays + "/" + current.targetDays + "\r\n" +
                "- Branch/SHA: `" + current.branch + "` / `" + current.commit + "`\r\n- Unity: " + current.unityVersion + "\r\n- Scene/scenario: `" + current.scene + "` / `" + current.scenario + "`\r\n" +
                "- Calendar: " + current.startGameDate + " to " + current.endGameDate + "\r\n- Seed: " + current.seedSource + " — " + current.seed + "\r\n" +
                "- Tracked working tree: " + FormatWorkingTreeSummary(
                    current.trackedWorkingTreeState, current.trackedWorkingTreeChangeCount) + "\r\n" +
                "- Console totals: " + FormatConsoleTotals(
                    current.totalConsoleEventCount, current.informationalLogCount, current.warningCount,
                    current.errorCount, current.exceptionCount) + "\r\n" +
                "- Blocker: " + (string.IsNullOrEmpty(current.blocker) ? "none recorded" : current.blocker) + "\r\n");
            AppendRunIndex(current);
        }

        private static void AppendRunIndex(RunSession session)
        {
            string root = System.IO.Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(root)) return;
            string index = Path.Combine(root, "Reports", "Playtests", "PLAYTEST_RUN_INDEX.md");
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(index));
            string row = "| " + session.runId + " | " + session.result + " | " + session.completedDays + "/" + session.targetDays + " | `" + session.branch + "` | `" + session.commit + "` | `" + session.outputDirectory + "` |\r\n";
            if (!File.Exists(index))
                File.WriteAllText(index, "# Playtest Run Index\r\n\r\n| Run ID | Result | Days | Branch | Commit | Artifact directory |\r\n|---|---|---:|---|---|---|\r\n");
            string[] lines = File.ReadAllLines(index);
            bool replaced = false;
            for (int i = 0; i < lines.Length; i++)
            {
                if (!lines[i].StartsWith("| " + session.runId + " |", StringComparison.Ordinal)) continue;
                lines[i] = row.TrimEnd('\r', '\n');
                replaced = true;
                break;
            }
            File.WriteAllLines(index, replaced ? lines : lines.Concat(new[] { row.TrimEnd('\r', '\n') }).ToArray());
        }

        private static void WriteRunIndex(string root, RunSession session)
        {
            string index = Path.Combine(root, "Reports", "Playtests", "PLAYTEST_RUN_INDEX.md");
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(index));
            if (!File.Exists(index))
                File.WriteAllText(index, "# Playtest Run Index\r\n\r\n| Run ID | Result | Days | Branch | Commit | Artifact directory |\r\n|---|---|---:|---|---|---|\r\n");
            AppendRunIndex(session);
        }

        private static void SaveSession()
        {
            if (current == null) return;
            SessionState.SetString(SessionKey, JsonUtility.ToJson(current));
            if (!string.IsNullOrWhiteSpace(current.outputDirectory))
                File.WriteAllText(Path.Combine(current.outputDirectory, "metadata.json"), JsonUtility.ToJson(current, true));
        }

        private static void RestoreSession()
        {
            string json = SessionState.GetString(SessionKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json)) return;
            try { current = JsonUtility.FromJson<RunSession>(json); }
            catch { current = null; }
            if (current != null && current.missingDailySnapshotOffsets == null)
                current.missingDailySnapshotOffsets = new List<int>();
            string previousDomainMarker = SessionState.GetString(DomainMarkerKey, string.Empty);
            if (current != null && IsUnexpectedDomainReload(current.status, EditorApplication.isPlaying,
                    previousDomainMarker, domainMarker))
            {
                current.unexpectedDomainReloadCount++;
                current.status = "ABORTED";
                current.result = "INVALID";
                current.blocker = "Unity scripting domain reloaded during an active Playtest; runtime evidence continuity is invalid.";
                current.wallEndUtc = DateTime.UtcNow.ToString("O");
                WriteFinalArtifacts();
                SaveSession();
                EditorApplication.delayCall += () =>
                {
                    UnityEngine.Object.FindFirstObjectByType<TimeManager>()?.SetSpeed(SimulationSpeed.Paused);
                    if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
                };
                return;
            }
            // Entering Play Mode can reload the domain before isPlaying flips
            // to true. Preserve an armed session during that transition so the
            // runtime startup gate can decide whether the run is valid.
            if (current != null && (current.status == "ARMED" || current.status == "RUNNING")
                && !EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                current.status = "ABORTED";
                current.result = "INCOMPLETE";
                current.blocker = "Unity Editor resumed without the active Play Mode run.";
                current.wallEndUtc = DateTime.UtcNow.ToString("O");
                WriteFinalArtifacts();
                SaveSession();
            }
        }

        private static void OnEditorQuitting()
        {
            if (IsActive)
            {
                current.status = "ABORTED";
                current.result = "INCOMPLETE";
                current.blocker = "Unity Editor quit before the run completed.";
                current.wallEndUtc = DateTime.UtcNow.ToString("O");
                WriteFinalArtifacts();
                SaveSession();
            }
            DetachLogCapture();
        }

        private static void AppendLine(string path, string line)
        {
            File.AppendAllText(path, line + "\r\n");
        }

        private static void FlushPendingConsole()
        {
            DrainConsoleQueue();
            if (IsActive) SaveSession();
        }

        private static string Csv(string value) => "\"" + (value ?? string.Empty).Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";

        internal static string ReadGit(string arguments, string root)
        {
            Process process = null;
            try
            {
                process = Process.Start(new ProcessStartInfo("git", arguments)
                {
                    WorkingDirectory = root,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                });

                if (process == null) return "NOT_CAPTURED";

                // Drain both redirected pipes while Git is running. Waiting for
                // exit before reading stdout can deadlock when porcelain status
                // contains enough owner-local changes to fill the pipe buffer.
                Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                Task<string> stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(2000))
                {
                    try { process.Kill(); } catch { }
                    try { process.WaitForExit(1000); } catch { }
                    return "NOT_CAPTURED";
                }

                process.WaitForExit();
                Task.WaitAll(stdout, stderr);
                return process.ExitCode == 0 ? stdout.Result.Trim() : "NOT_CAPTURED";
            }
            catch { return "NOT_CAPTURED"; }
            finally { process?.Dispose(); }
        }

        internal static int CountNonEmptyLines(string value) =>
            value == null || string.Equals(value.Trim(), "NOT_CAPTURED", StringComparison.Ordinal)
                ? -1
                : value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;

        internal static string ResolveWorkingTreeState(int changeCount) =>
            changeCount < 0 ? "NOT_CAPTURED" : changeCount > 0 ? "DIRTY" : "CLEAN";

        internal static string FormatWorkingTreeSummary(string state, int changeCount)
        {
            if (string.Equals(state, "DIRTY", StringComparison.Ordinal))
                return "dirty (" + Math.Max(0, changeCount) + " changed tracked files)";
            if (string.Equals(state, "CLEAN", StringComparison.Ordinal)) return "clean";
            return "NOT_CAPTURED (Git working-tree status could not be read)";
        }

        internal static string FormatConsoleTotals(
            int totalEvents, int informationalLogs, int warnings, int errors, int exceptions) =>
            totalEvents + " total events (" + informationalLogs + " informational logs, " + warnings +
            " warnings, " + errors + " errors, " + exceptions + " exceptions)";
    }
}
#endif
