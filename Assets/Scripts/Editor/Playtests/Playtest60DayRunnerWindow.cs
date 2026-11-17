#if UNITY_EDITOR
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using LandLedgers.Economy;
using LandLedgers.Economy.Core;
using LandLedgers.FirstLedger;
using LandLedgers.Orchestration.Bootstrap;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Population;
using LandLedgers.Persistence;
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

        [MenuItem("Land & Ledgers/Diagnostics/60-Day Playtest Runner")]
        public static void Open()
        {
            GetWindow<Playtest60DayRunnerWindow>("60-Day Playtest");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Controlled integrated playtest", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Uses the open scene and the existing TimeManager. Startup validation runs before day 1; an invalid configuration aborts the run. This window never saves the scene or changes project assets.",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(Playtest60DayRunCoordinator.IsActive || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                speed = (SimulationSpeed)EditorGUILayout.EnumPopup("Simulation speed", speed);
                targetDays = EditorGUILayout.IntField("Completed days", Mathf.Clamp(targetDays, 1, 365));
                if (GUILayout.Button("Start controlled run"))
                {
                    if (!Playtest60DayRunCoordinator.TryStart(targetDays, speed, out string message))
                        EditorUtility.DisplayDialog("60-Day Playtest Not Started", message, "OK");
                }
            }

            Playtest60DayRunCoordinator.RunSession session = Playtest60DayRunCoordinator.Current;
            if (session != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Run", session.runId ?? "(none)");
                EditorGUILayout.LabelField("State", session.status ?? "(unknown)");
                EditorGUILayout.LabelField("Progress", $"{session.completedDays}/{session.targetDays} days");
                EditorGUILayout.LabelField("Result", session.result ?? "(pending)");
                if (!string.IsNullOrEmpty(session.blocker))
                    EditorGUILayout.HelpBox(session.blocker, MessageType.Warning);
                if (!string.IsNullOrEmpty(session.outputDirectory))
                    EditorGUILayout.SelectableLabel(session.outputDirectory, EditorStyles.textField, GUILayout.Height(34));

                using (new EditorGUI.DisabledScope(!Playtest60DayRunCoordinator.IsActive))
                {
                    if (GUILayout.Button("Stop and mark run aborted"))
                        Playtest60DayRunCoordinator.Abort("Stopped by the owner through the playtest runner.");
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Seed policy", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "For repeatability, enable Manual Seed Override or Fixed Testing World in the existing TownGenerationSettings asset before starting. The runner records the authoritative resolved seed and refuses a random, unrepeatable start.",
                MessageType.None);
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
        private const string SessionKey = "LandLedgers.Playtest60Day.Session";
        private const string RunRoot = "Reports/Playtests/Runs";
        private static readonly ConcurrentQueue<ConsoleEvent> PendingConsole = new();
        private static RunSession current;
        private static bool subscribedToLogs;
        private static bool awaitingStartupGate;
        private static bool exceptionObserved;
        private static TimeManager subscribedTimeManager;
        private static double startupWaitStartedAt;
        private static string currentSimulationContext = "NOT_STARTED";

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
            public string scene;
            public string scenario;
            public string startupMode;
            public string seedSource;
            public int seed;
            public int targetDays;
            public int completedDays;
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
            public int actualHouseholds = -1;
            public int actualBusinesses = -1;
            public int logCount;
            public int warningCount;
            public int errorCount;
            public int exceptionCount;
            public bool startupGatePassed;
            public string finalInvariant = "NOT_CAPTURED";
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
            public int householdCount = -1;
            public int businessCount = -1;
            public long householdCashCents = -1;
            public long businessCashCents = -1;
            public long householdLotUnits = -1;
            public long retailLotUnits = -1;
            public string mealTotals = "NOT_CAPTURED: inspect [FoodEconomy] events in console-events.jsonl";
            public string taskCompletion = "NOT_CAPTURED: no public aggregate seam";
            public string movementOutcomes = "NOT_CAPTURED: authoritative route outcomes not exposed as a daily snapshot";
            public string freightAndConstruction = "NOT_CAPTURED: use authoritative event logs and Pass 9 state records";
        }

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
            message = string.Empty;
            if (EditorApplication.isPlayingOrWillChangePlaymode || IsActive)
            {
                message = "Unity is already entering Play Mode or another 60-day run is active.";
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
                scene = scene.path,
                scenario = configuration.ConfigurationId,
                startupMode = bootstrapper.StartupIntent.ToString(),
                seedSource = "Configured deterministic TownGenerationSettings seed; resolved seed recorded after startup.",
                seed = town.Settings.useManualSeedOverride ? town.Settings.manualSeedOverride : town.Settings.startupMode == WorldGenerationStartupMode.FixedTestingWorld ? town.Settings.fixedTestingWorldSeed : town.Settings.fixedDebugSeed,
                targetDays = Mathf.Clamp(days, 1, 365),
                speedName = simulationSpeed.ToString(),
                selectedMultiplier = time.Settings.GetMultiplier(simulationSpeed),
                dayLengthSeconds = time.Settings.GameSecondsPerDay,
                expectedResidents = configuration.People.Count,
                expectedHouseholds = configuration.Households.Count,
                expectedBusinesses = configuration.Businesses.Count
            };
            System.IO.Directory.CreateDirectory(Path.Combine(root, RunRoot));
            File.WriteAllText(Path.Combine(output, "metadata.json"), JsonUtility.ToJson(current, true));
            File.WriteAllText(Path.Combine(output, "console-full.txt"), "# Complete Unity Console capture; raw messages and stack traces are preserved.\r\n");
            File.WriteAllText(Path.Combine(output, "console-events.jsonl"), string.Empty);
            File.WriteAllText(Path.Combine(output, "daily-summary.csv"),
                "absoluteDayIndex,gameDate,wallUtc,speed,residents,households,businesses,householdCashCents,businessCashCents,householdLotUnits,retailLotUnits,mealTotals,taskCompletion,movementOutcomes,freightAndConstruction\r\n");
            File.WriteAllText(Path.Combine(output, "entity-snapshots.jsonl"), string.Empty);
            WriteRunIndex(root, current);
            SaveSession();
            AttachLogCapture();
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
                WriteFinalArtifacts();
                SaveSession();
            }
            else if (change == PlayModeStateChange.EnteredEditMode)
            {
                awaitingStartupGate = false;
                DetachLogCapture();
            }
        }

        private static void Update()
        {
            if (current == null) return;
            DrainConsoleQueue();

            if (awaitingStartupGate && EditorApplication.isPlaying)
            {
                if (exceptionObserved)
                {
                    Abort("An uncaught runtime exception occurred before the startup gate passed.");
                    return;
                }
                if (EditorApplication.timeSinceStartup - startupWaitStartedAt > 30d)
                {
                    Abort("Startup gate timed out after 30 real seconds; no simulated day was accepted.");
                    return;
                }

                if (!TryPassRuntimeStartupGate(out string blocker, out TimeManager time))
                {
                    FirstLedgerSliceBootstrapper bootstrapper = UnityEngine.Object.FindFirstObjectByType<FirstLedgerSliceBootstrapper>();
                    if (bootstrapper != null && (bootstrapper.HasStartupFailed || bootstrapper.IsInitialized))
                    {
                        current.blocker = blocker;
                        Abort("INVALID STARTUP: " + blocker);
                    }
                    return;
                }

                current.startupGatePassed = true;
                current.status = "RUNNING";
                current.startAbsoluteDayIndex = time.CurrentAbsoluteDayIndex;
                current.endAbsoluteDayIndex = current.startAbsoluteDayIndex + current.targetDays;
                current.startGameDate = time.CurrentDate.ToString();
                current.seed = UnityEngine.Object.FindFirstObjectByType<TownWorldController>()?.LastSeedResolution.Seed ?? current.seed;
                current.finalInvariant = "PASS: bootstrap, town, population, households, businesses, hubs, drivers and required Pass 9 holders are present.";
                Volatile.Write(ref currentSimulationContext, "day=" + time.CurrentAbsoluteDayIndex + "; date=" + time.CurrentDate);
                time.DayChanged += OnDayChanged;
                subscribedTimeManager = time;
                time.SetSpeed(Enum.TryParse(current.speedName, out SimulationSpeed selected) ? selected : SimulationSpeed.Speed1x);
                SaveSession();
            }

            if (current.status == "RUNNING" && EditorApplication.isPlaying)
            {
                if (exceptionObserved)
                {
                    Abort("An uncaught runtime exception was captured; run aborted for investigation.");
                    return;
                }
                TimeManager time = UnityEngine.Object.FindFirstObjectByType<TimeManager>();
                if (time == null)
                {
                    Abort("TimeManager disappeared while the run was active.");
                    return;
                }
                int elapsed = time.CurrentAbsoluteDayIndex - current.startAbsoluteDayIndex;
                if (elapsed < 0)
                {
                    Abort("Authoritative absolute simulation day index moved backwards.");
                    return;
                }
                if (elapsed >= current.targetDays)
                {
                    current.completedDays = current.targetDays;
                    current.endAbsoluteDayIndex = time.CurrentAbsoluteDayIndex;
                    current.endGameDate = time.CurrentDate.ToString();
                    current.status = "COMPLETED";
                    current.result = "PASS";
                    current.wallEndUtc = DateTime.UtcNow.ToString("O");
                    current.finalInvariant = "PASS: authoritative clock completed the configured calendar-day interval.";
                    time.SetSpeed(SimulationSpeed.Paused);
                    UnsubscribeDayEvents();
                    WriteFinalArtifacts();
                    SaveSession();
                    EditorApplication.delayCall += () => EditorApplication.isPlaying = false;
                }
                else if (elapsed > current.completedDays)
                {
                    // The DayChanged subscription records each boundary. This is a
                    // backstop for elapsed dates if a handler was detached by a
                    // domain reload; such a gap is explicitly written as missing.
                    for (int day = current.completedDays + 1; day <= elapsed; day++)
                        AppendMissingDay(day);
                    current.completedDays = elapsed;
                    SaveSession();
                }
            }
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
            current.actualResidents = population.State.people.Count;
            if (current.actualResidents < current.expectedResidents)
            { blocker = $"Population is {current.actualResidents}/{current.expectedResidents} residents."; return false; }
            current.actualHouseholds = population.State.households.Count;
            if (current.actualHouseholds < current.expectedHouseholds)
            { blocker = $"Population is {current.actualHouseholds}/{current.expectedHouseholds} households."; return false; }
            current.actualBusinesses = businesses.Businesses?.Count ?? 0;
            if (current.actualBusinesses < current.expectedBusinesses)
            { blocker = $"SharedBusinessRuntimeManager has {current.actualBusinesses}/{current.expectedBusinesses} configured businesses."; return false; }

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

        private static object ReadField(object target, string name) =>
            target?.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(target);

        private static void OnDayChanged(SimulationDateChangedContext context)
        {
            if (current == null || current.status != "RUNNING") return;
            current.completedDays = Mathf.Clamp(context.CurrentDate.AbsoluteDayIndex - current.startAbsoluteDayIndex, 0, current.targetDays);
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
            DaySnapshot day = new()
            {
                runId = current.runId,
                absoluteDayIndex = date.AbsoluteDayIndex,
                gameDate = date.ToString(),
                wallUtc = DateTime.UtcNow.ToString("O"),
                speed = UnityEngine.Object.FindFirstObjectByType<TimeManager>()?.CurrentSpeed.ToString() ?? "NOT_CAPTURED",
                residentCount = population?.State?.people?.Count ?? -1,
                householdCount = population?.State?.households?.Count ?? -1,
                businessCount = businesses?.Businesses?.Count ?? -1
            };
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
                        inventoryStatus = inventory != null ? "CAPTURED_FROM_HouseholdInventoryRegistry" : "NOT_CAPTURED_NO_INVENTORY"
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
                day.residentCount + "," + day.householdCount + "," + day.businessCount + "," + day.householdCashCents + "," +
                day.businessCashCents + "," + day.householdLotUnits + "," + day.retailLotUnits + "," + Csv(day.mealTotals) + "," +
                Csv(day.taskCompletion) + "," + Csv(day.movementOutcomes) + "," + Csv(day.freightAndConstruction));
            AppendLine(Path.Combine(folder, "entity-snapshots.jsonl"), JsonUtility.ToJson(day));
        }

        private static void AppendMissingDay(int offset)
        {
            AppendLine(Path.Combine(current.outputDirectory, "daily-summary.csv"),
                "NOT_CAPTURED,day-offset-" + offset + "," + Csv(DateTime.UtcNow.ToString("O")) + ",NOT_CAPTURED,-1,-1,-1,-1,-1,-1,-1," +
                Csv("NOT_CAPTURED_EVENT_GAP") + "," + Csv("NOT_CAPTURED") + "," + Csv("NOT_CAPTURED") + "," + Csv("NOT_CAPTURED"));
        }

        private static void AppendEntitySnapshot(EntitySnapshot entity) =>
            AppendLine(Path.Combine(current.outputDirectory, "entity-snapshots.jsonl"), JsonUtility.ToJson(entity));

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
            long sequence = Interlocked.Increment(ref current.logCount);
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
            if (time != null)
            {
                current.endAbsoluteDayIndex = time.CurrentAbsoluteDayIndex;
                current.endGameDate = time.CurrentDate.ToString();
            }
            File.WriteAllText(Path.Combine(current.outputDirectory, "metadata.json"), JsonUtility.ToJson(current, true));
            string finalState = "{\"runId\":\"" + current.runId + "\",\"result\":\"" + current.result +
                "\",\"daysCompleted\":" + current.completedDays + ",\"finalInvariant\":\"" + current.finalInvariant + "\",\"unavailableDetails\":[" +
                "\"mealEatenMissedByHousehold: use preserved [FoodEconomy] console events\",\"task completion aggregates: no public snapshot API\",\"physical movement outcomes: capture arrival/trip authorities in focused reports\",\"production inputs and outputs: use subsystem event ledgers\"]}";
            File.WriteAllText(Path.Combine(current.outputDirectory, "final-state.json"), finalState);
            File.WriteAllText(Path.Combine(current.outputDirectory, "playtest-report.md"),
                "# 60-Day Playtest " + current.runId + "\r\n\r\n" +
                "- Result: **" + current.result + "**\r\n- Status: " + current.status + "\r\n- Completed days: " + current.completedDays + "/" + current.targetDays + "\r\n" +
                "- Branch/SHA: `" + current.branch + "` / `" + current.commit + "`\r\n- Unity: " + current.unityVersion + "\r\n- Scene/scenario: `" + current.scene + "` / `" + current.scenario + "`\r\n" +
                "- Calendar: " + current.startGameDate + " to " + current.endGameDate + "\r\n- Seed: " + current.seedSource + " — " + current.seed + "\r\n" +
                "- Console totals: " + current.logCount + " logs, " + current.warningCount + " warnings, " + current.errorCount + " errors, " + current.exceptionCount + " exceptions\r\n" +
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
                File.WriteAllText(index, "# 60-Day Playtest Run Index\r\n\r\n| Run ID | Result | Days | Branch | Commit | Artifact directory |\r\n|---|---|---:|---|---|---|\r\n");
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
                File.WriteAllText(index, "# 60-Day Playtest Run Index\r\n\r\n| Run ID | Result | Days | Branch | Commit | Artifact directory |\r\n|---|---|---:|---|---|---|\r\n");
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
            if (current != null && (current.status == "ARMED" || current.status == "RUNNING") && !EditorApplication.isPlaying)
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

        private static void FlushPendingConsole() => DrainConsoleQueue();

        private static string Csv(string value) => "\"" + (value ?? string.Empty).Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";

        private static string ReadGit(string arguments, string root)
        {
            try
            {
                using Process process = Process.Start(new ProcessStartInfo("git", arguments)
                {
                    WorkingDirectory = root,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                });
                if (process == null || !process.WaitForExit(2000)) return "NOT_CAPTURED";
                return process.ExitCode == 0 ? process.StandardOutput.ReadToEnd().Trim() : "NOT_CAPTURED";
            }
            catch { return "NOT_CAPTURED"; }
        }
    }
}
#endif
