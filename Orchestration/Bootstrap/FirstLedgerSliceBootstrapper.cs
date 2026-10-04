using System.Collections.Generic;
using LandLedgers.Civic;
using System.Text;
using LandLedgers.CameraSystem;
using LandLedgers.Economy;
using LandLedgers.Economy.Financing;
using LandLedgers.Orchestration.Scenarios;
using LandLedgers.Orchestration.Player;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Pathing;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Time;
using LandLedgers.UI;
using LandLedgers.World;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace LandLedgers.FirstLedger
{
    public enum WorldStartupIntent
    {
        NewGame = 0,
        ContinueDefaultSlot = 1,
        Manual = 2
    }

    [DisallowMultipleComponent]
    [DefaultExecutionOrder(240)]
    public sealed class FirstLedgerSliceBootstrapper : MonoBehaviour
    {
        [Header("Scene Systems")]
        [SerializeField]
        private TownWorldController townWorld;

        [SerializeField]
        private PathingManager pathingManager;

        [SerializeField]
        private TimeManager timeManager;

        [SerializeField]
        private PopulationManager populationManager;

        [SerializeField]
        private CivicFoundationManager civicFoundation;

        [SerializeField]
        private GeneralStoreRuntimeManager storeRuntime;

        [SerializeField]
        private LogisticsRuntimeManager logisticsRuntime;

        [SerializeField]
        private SharedBusinessRuntimeManager sharedBusinessRuntime;

        [SerializeField]
        private TownPulseRuntimeManager townPulseRuntime;

        [SerializeField]
        private OpportunityPressureRuntimeManager opportunityPressureRuntime;

        [SerializeField]
        private AcquisitionMarketManager acquisitionMarket;

        [SerializeField]
        private PlayerDebtManager playerDebtManager;

        [SerializeField]
        private PlayerPortfolioManager playerPortfolioManager;

        [Header("Scenario")]
        [SerializeField, Min(0), Tooltip("P4: fallback for the First Ledger scenario's 'startingCashCents' tunable (player stake). The scenario asset declares 250000c; the asset value wins when a ScenarioDirector is present.")]
        private int fallbackStartingCashCents = 250000;

        [SerializeField]
        private SaveLoadManager saveLoadManager;

        [SerializeField]
        private PopulationPathingDirector populationPathingDirector;

        [SerializeField]
        private GeneralStorePanelController storePanel;

        [SerializeField]
        private WorldInteractionController worldInteractionController;

        [SerializeField]
        private LandLedgersHUDController hudController;

        [SerializeField]
        private FirstSessionGuidanceManager firstSessionGuidance;

        [SerializeField]
        private SimulationSystemsHub systemsHub;

        [SerializeField]
        private PopulationGenerationSettings populationSettings;

        [SerializeField]
        private GeneralStoreBusinessDefinition storeDefinition;

        [Header("Startup")]
        [SerializeField]
        private WorldStartupIntent startupIntent = WorldStartupIntent.NewGame;

        [SerializeField]
        private WorldStartupMode startupMode = WorldStartupMode.UsePreAuthoredTerrain;

        [SerializeField]
        private PreAuthoredTerrainWorldProfile preAuthoredTerrainProfile;

        [SerializeField]
        private bool verboseWorldStartupLogging;

        [SerializeField]
        private bool generateTownOnPlay = true;

        [SerializeField]
        private bool generatePopulationOnPlay = true;

        [SerializeField]
        private bool resolveOpeningDaySales = true;

        [SerializeField]
        private bool sendInitialWorkersToWork = true;

        [SerializeField]
        private bool logBootstrapSummary = true;

        [SerializeField]
        private bool logStartupTiming = true;

        [Header("Runtime Summary")]
        [SerializeField]
        private string bootstrapStatus = "Not started";

        [SerializeField]
        private string lastStartupTimingSummary = string.Empty;

        [SerializeField]
        private int lastRestoreDayIndex = -1;

        [SerializeField]
        private int lastRestoreIssueCount;

        [SerializeField]
        private string lastRestoreCoordinatorSummary = string.Empty;

        [SerializeField]
        private string lastRestoreStoreSummary = string.Empty;

        [SerializeField]
        private string lastRestorePanelSummary = string.Empty;

        [SerializeField]
        private string lastRestorePathingSummary = string.Empty;

        [SerializeField]
        private string lastRestorePersistenceSummary = string.Empty;

        private bool initialized;
        private bool initializing;
        private HouseholdMembershipRegistry playerHouseholdMemberships;
        private KinshipRegistry playerHouseholdKinship;
        private HouseholdLifecycleManager playerHouseholdLifecycle;

        public string LastStartupTimingSummary => lastStartupTimingSummary ?? string.Empty;
        public WorldStartupIntent StartupIntent => startupIntent;
        public bool IsInitialized => initialized;

        private void Awake()
        {
            EnsureInputSystemEventSystem();
            AutoWire();
        }

        private void Start()
        {
            InitializeSlice();
        }

        [ContextMenu("Initialize Game Slice")]
        public void InitializeSlice()
        {
            Stopwatch totalTimer = ShouldLogStartupTiming() ? Stopwatch.StartNew() : null;
            Stopwatch stepTimer = totalTimer != null ? Stopwatch.StartNew() : null;
            StringBuilder timing = totalTimer != null ? new StringBuilder() : null;

            AutoWire();
            AppendStartupTiming(timing, stepTimer, "autowire");
            if (initialized || initializing)
            {
                CompleteStartupTiming(totalTimer, timing, initialized ? "already initialized" : "initialization already in progress");
                return;
            }

            initializing = true;

            if (townWorld == null)
            {
                bootstrapStatus = "Missing TownWorldController.";
                Debug.LogWarning(bootstrapStatus, this);
                CompleteStartupTiming(totalTimer, timing, "missing town world");
                initializing = false;
                return;
            }

            Debug.Log($"[WorldStartup] Startup intent: {startupIntent}", this);
            Debug.Log($"[WorldStartup] WorldStartupMode: {startupMode}", this);
            if (!TryConfigureTerrainStartup(out string terrainStartupError))
            {
                bootstrapStatus = terrainStartupError;
                Debug.LogError(bootstrapStatus, this);
                CompleteStartupTiming(totalTimer, timing, "terrain startup failed");
                initializing = false;
                return;
            }
            AppendStartupTiming(timing, stepTimer, "terrain mode initialization");

            if (startupIntent == WorldStartupIntent.ContinueDefaultSlot)
            {
                Debug.Log("[WorldStartup] Continue path selected. Load started before fresh generation; expected fresh generation pass count is zero.", this);
                string loadMessage = "SaveLoadManager is missing.";
                bool loaded = saveLoadManager != null && saveLoadManager.LoadDefaultSlot(out loadMessage);
                AppendStartupTiming(timing, stepTimer, "continue load");
                if (!loaded)
                {
                    bootstrapStatus = string.IsNullOrWhiteSpace(loadMessage)
                        ? "Continue startup failed: no usable default save was loaded."
                        : loadMessage;
                    Debug.LogError($"[WorldStartup] {bootstrapStatus}", this);
                    CompleteStartupTiming(totalTimer, timing, "continue failed before usable world");
                    initializing = false;
                    return;
                }

                initialized = true;
                Debug.Log($"[WorldStartup] Continue completed with fresh generation pass count {townWorld.GenerationPassCount}.", this);
                CompleteStartupTiming(totalTimer, timing, "continue usable world");
                initializing = false;
                return;
            }

            if (startupIntent == WorldStartupIntent.Manual)
            {
                initialized = true;
                bootstrapStatus = "Manual startup configured terrain and managers without generating or loading a world.";
                Debug.Log($"[WorldStartup] {bootstrapStatus}", this);
                CompleteStartupTiming(totalTimer, timing, "manual configuration");
                initializing = false;
                return;
            }

            Debug.Log("[WorldStartup] New Game path selected: exactly one fresh-world generation pass is permitted.", this);

            if (generateTownOnPlay && townWorld.Grid == null)
            {
                townWorld.GenerateTownShell();
            }
            AppendStartupTiming(timing, stepTimer, "town generation");

            if (generateTownOnPlay && townWorld.Grid == null)
            {
                bootstrapStatus = "New Game startup stopped because town generation did not produce a valid world.";
                Debug.LogError($"[WorldStartup] {bootstrapStatus}", this);
                CompleteStartupTiming(totalTimer, timing, "town generation failed");
                initializing = false;
                return;
            }

            ConfigurePopulationManager();
            ConfigureCivicFoundation();
            AppendStartupTiming(timing, stepTimer, "population/civic configure");

            if (generatePopulationOnPlay && populationManager != null)
            {
                populationManager.GeneratePopulationSnapshot();
            }
            AppendStartupTiming(timing, stepTimer, "population generation");

            ConfigureScenarioAuthorities();

            ConfigureTownPulseRuntime();
            ConfigurePlayerPortfolioManager();
            ConfigureLogisticsRuntime();
            ConfigureStoreRuntime();
            // First Ledger starts with a cash stake, not an automatic player business.
            // The authored town roster remains NPC-owned; the player's first business
            // is formed explicitly through the Businesses workflow.
            if (storeRuntime != null && FindAnyObjectByType<ScenarioDirector>() != null)
            {
                storeRuntime.SetCreatePlayerBusinessOnInitialize(false);
            }
            storeRuntime?.InitializeIfNeeded();
            ConfigureSharedBusinessRuntime();
            sharedBusinessRuntime?.InitializeIfNeeded(storeRuntime != null ? storeRuntime.CurrentBusiness : null);
            ConfigurePopulationPathingDirector();
            // P4: the scenario promises "a stake of cash" (FirstLedger.asset tunable
            // 'startingCashCents'). Seed it once on a fresh game — never on load.
            bool portfolioWasInitialized = playerPortfolioManager != null && playerPortfolioManager.IsInitialized;
            playerPortfolioManager?.InitializeFromLegacyBusinessCash(storeRuntime, sharedBusinessRuntime);
            if (!portfolioWasInitialized)
            {
                SeedPlayerStartingStake();
            }
            AppendStartupTiming(timing, stepTimer, "business/runtime configure");

            ConfigureAcquisitionMarket();
            ApplyFreshAcquisitionMarketSeedAuthority();
            acquisitionMarket?.RebuildMarket();
            AppendStartupTiming(timing, stepTimer, "acquisition market");

            ConfigurePlayerDebtManager();
            ConfigureOpportunityPressureRuntime();
            ConfigureFirstSessionGuidance();
            ConfigureWorldInteractionController();
            firstSessionGuidance?.ResetGuidanceForNewSession();
            AppendStartupTiming(timing, stepTimer, "decision/ui configure");

            if (resolveOpeningDaySales)
            {
                storeRuntime?.ResolveDailySales();
            }
            AppendStartupTiming(timing, stepTimer, "opening sales");

            opportunityPressureRuntime?.RefreshNotices();
            AppendStartupTiming(timing, stepTimer, "opportunity notices");

            populationPathingDirector?.InitializeIfNeeded();
            AppendStartupTiming(timing, stepTimer, "pathing setup");

            if (sendInitialWorkersToWork)
            {
                populationPathingDirector?.SendWorkersToWork();
            }
            AppendStartupTiming(timing, stepTimer, "worker dispatch");

            storePanel?.Refresh();
            AppendStartupTiming(timing, stepTimer, "ui refresh");

            initialized = true;
            bootstrapStatus = $"Game slice ready. Buildings={townWorld.Buildings.Count}, People={(populationManager != null ? populationManager.GeneratedPersonCount : 0)}, Store={(storeRuntime != null ? storeRuntime.Status : "missing")}.";
            if (logBootstrapSummary)
            {
                Debug.Log(bootstrapStatus, this);
            }

            CompleteStartupTiming(totalTimer, timing, "fresh startup");
            initializing = false;
        }

        public void MarkRestoredFromSave(string persistenceSummary = "")
        {
            AutoWire();
            Debug.Log("[WorldStartup] Startup path: save load; saved world state and placements are authoritative.", this);
            initialized = true;
            bootstrapStatus = "Game slice restored from save.";
            lastRestorePersistenceSummary = string.IsNullOrWhiteSpace(persistenceSummary)
                ? "No persistence repair notes recorded."
                : persistenceSummary.Trim();
            civicFoundation?.RefreshFromWorld();

            // Rebind all derived runtime coordinators after load. The save data is the authority;
            // market listings, agent visuals, and workflow-facing summaries must be rebuilt from it.
            ConfigureTownPulseRuntime();
            ConfigurePlayerPortfolioManager();
            ConfigureLogisticsRuntime();
            ConfigureStoreRuntime();
            ConfigureSharedBusinessRuntime();
            ConfigurePopulationPathingDirector();
            ConfigureAcquisitionMarket();
            acquisitionMarket?.RebuildMarket();
            ConfigurePlayerDebtManager();
            ConfigureOpportunityPressureRuntime();
            ConfigureFirstSessionGuidance();
            ConfigureWorldInteractionController();
            populationPathingDirector?.ResetForLoadedState();
            opportunityPressureRuntime?.RefreshNotices();
            storePanel?.Refresh();
            RefreshRestoreDiagnostics();
            if (logBootstrapSummary)
            {
                Debug.Log(BuildRestoreDiagnosticsSummary(), this);
            }
        }

        private bool TryConfigureTerrainStartup(out string error)
        {
            RegionalTerrainTileView[] terrainViews = FindObjectsByType<RegionalTerrainTileView>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            bool procedural = WorldStartupRouting.ShouldGenerateProceduralTerrain(startupMode);
            for (int i = 0; i < terrainViews.Length; i++)
            {
                terrainViews[i]?.SetRuntimeGenerationEnabled(procedural);
            }

            if (!procedural)
            {
                preAuthoredTerrainProfile ??= FindAnyObjectByType<PreAuthoredTerrainWorldProfile>();
                if (preAuthoredTerrainProfile == null)
                {
                    error = "[WorldStartup] UsePreAuthoredTerrain requires a PreAuthoredTerrainWorldProfile in the scene. Runtime procedural terrain will not be used as a fallback.";
                    return false;
                }

                if (!preAuthoredTerrainProfile.TryCreateProvider(out IWorldSurfaceProvider provider, out string providerError))
                {
                    error = $"[WorldStartup] Pre-authored terrain provider initialization failed: {providerError}";
                    return false;
                }

                Terrain primaryTerrain = preAuthoredTerrainProfile.GetPrimaryTerrain();
                TerrainCollider primaryCollider = primaryTerrain != null ? primaryTerrain.GetComponent<TerrainCollider>() : null;
                if (primaryCollider != null)
                {
                    townWorld.SetRuntimeTerrainCollider(primaryCollider, false);
                }

                townWorld.ConfigureStartup(
                    startupMode,
                    provider,
                    preAuthoredTerrainProfile.TerrainProfileId,
                    verboseWorldStartupLogging || preAuthoredTerrainProfile.VerboseStartupLogging,
                    preAuthoredTerrainProfile.MinimumDepositDistanceFromTownCore,
                    preAuthoredTerrainProfile.DefaultMaxResourceSlope,
                    preAuthoredTerrainProfile);
                Debug.Log("[WorldStartup] Runtime regional terrain generation: SKIPPED", this);
                Debug.Log($"[WorldStartup] Pre-authored terrain provider initialized and validated. Profile={preAuthoredTerrainProfile.TerrainProfileId}, Revision={preAuthoredTerrainProfile.TerrainContentRevision}, Bounds={provider.WorldBounds}.", this);
                error = string.Empty;
                return true;
            }

            TownGenerationSettings settings = townWorld.Settings;
            Vector3 center = settings != null ? settings.worldCenter : townWorld.transform.position;
            float width = settings != null ? settings.gridWidthCells * settings.cellSizeMeters : 512f;
            float depth = settings != null ? settings.gridDepthCells * settings.cellSizeMeters : 512f;
            Bounds fallbackBounds = new(center, new Vector3(width, 128f, depth));
            IWorldSurfaceProvider proceduralProvider = new ProceduralTerrainSurfaceProvider(
                townWorld.RuntimeTerrainCollider,
                fallbackBounds,
                settings != null ? settings.terrainRaycastHeight : 250f,
                settings != null ? settings.terrainRaycastDistance : 600f,
                settings != null ? settings.terrainLayers : ~0);
            townWorld.ConfigureStartup(startupMode, proceduralProvider, string.Empty, verboseWorldStartupLogging);
            Debug.Log("[WorldStartup] Runtime regional terrain generation: RUNNING (procedural mode).", this);
            error = string.Empty;
            return true;
        }

        [ContextMenu("Log Restore Diagnostics")]
        private void LogRestoreDiagnostics()
        {
            AutoWire();
            RefreshRestoreDiagnostics();
            Debug.Log(BuildRestoreDiagnosticsSummary(), this);
        }

        private void RefreshRestoreDiagnostics()
        {
            // Save data stays authoritative. These diagnostics only report whether the runtime-facing
            // coordinators were rebound cleanly after load so stale derived state is easier to spot.
            lastRestoreDayIndex = timeManager != null ? timeManager.CurrentAbsoluteDayIndex : -1;
            lastRestoreCoordinatorSummary = BuildCoordinatorSummary();
            lastRestoreStoreSummary = storeRuntime != null ? storeRuntime.Status : "Store runtime missing.";
            lastRestorePanelSummary = storePanel != null ? storePanel.CurrentPanelStatus : "Management panel missing.";
            lastRestorePathingSummary = populationPathingDirector != null
                ? populationPathingDirector.BuildDiagnosticsSummary()
                : "Population pathing director missing.";
            lastRestoreIssueCount = CountRestoreIssues();
            bootstrapStatus = lastRestoreIssueCount > 0
                ? $"Game slice restored with {lastRestoreIssueCount} restore diagnostic issue(s)."
                : "Game slice restored from save. Coordinators rebound cleanly.";
        }

        private string BuildRestoreDiagnosticsSummary()
        {
            return $"Restore diagnostics | Day={lastRestoreDayIndex}, Issues={lastRestoreIssueCount}, Coordinators=[{lastRestoreCoordinatorSummary}], Store=[{lastRestoreStoreSummary}], Panel=[{lastRestorePanelSummary}], Pathing=[{lastRestorePathingSummary}], Persistence=[{lastRestorePersistenceSummary}]";
        }

        private int CountRestoreIssues()
        {
            int issues = 0;
            if (townWorld == null || townWorld.Grid == null)
            {
                issues++;
            }

            if (timeManager == null)
            {
                issues++;
            }

            if (populationManager == null || populationManager.State == null)
            {
                issues++;
            }

            if (storeRuntime == null)
            {
                issues++;
            }

            if (logisticsRuntime == null)
            {
                issues++;
            }

            if (sharedBusinessRuntime == null)
            {
                issues++;
            }

            if (playerPortfolioManager == null)
            {
                issues++;
            }

            if (saveLoadManager == null)
            {
                issues++;
            }

            if (hudController == null)
            {
                issues++;
            }

            if (acquisitionMarket == null)
            {
                issues++;
            }

            if (playerDebtManager == null)
            {
                issues++;
            }

            if (opportunityPressureRuntime == null)
            {
                issues++;
            }

            if (populationPathingDirector == null)
            {
                issues++;
            }

            if (firstSessionGuidance == null)
            {
                issues++;
            }

            if (storePanel == null)
            {
                issues++;
            }

            return issues;
        }

        private string BuildCoordinatorSummary()
        {
            return string.Join(", ",
                BuildCoordinatorState("World", townWorld != null && townWorld.Grid != null),
                BuildCoordinatorState("Time", timeManager != null),
                BuildCoordinatorState("Population", populationManager != null && populationManager.State != null),
                BuildCoordinatorState("Store", storeRuntime != null),
                BuildCoordinatorState("Logistics", logisticsRuntime != null),
                BuildCoordinatorState("SharedBusiness", sharedBusinessRuntime != null),
                BuildCoordinatorState("Portfolio", playerPortfolioManager != null),
                BuildCoordinatorState("SaveLoad", saveLoadManager != null),
                BuildCoordinatorState("HUD", hudController != null),
                BuildCoordinatorState("Market", acquisitionMarket != null),
                BuildCoordinatorState("Debt", playerDebtManager != null),
                BuildCoordinatorState("Opportunity", opportunityPressureRuntime != null),
                BuildCoordinatorState("Pathing", populationPathingDirector != null),
                BuildCoordinatorState("Guidance", firstSessionGuidance != null),
                BuildCoordinatorState("Panel", storePanel != null));
        }

        private static string BuildCoordinatorState(string label, bool ready)
        {
            return ready ? $"{label}=ready" : $"{label}=missing";
        }

        private bool ShouldLogStartupTiming()
        {
            return logStartupTiming && Application.isPlaying;
        }

        private static void AppendStartupTiming(StringBuilder builder, Stopwatch timer, string label)
        {
            if (builder == null || timer == null)
            {
                return;
            }

            timer.Stop();
            if (builder.Length > 0)
            {
                builder.Append(", ");
            }

            builder.Append(label);
            builder.Append(' ');
            builder.Append(timer.ElapsedMilliseconds);
            builder.Append(" ms");
            timer.Restart();
        }

        private void CompleteStartupTiming(Stopwatch totalTimer, StringBuilder timing, string context)
        {
            if (totalTimer == null)
            {
                return;
            }

            totalTimer.Stop();
            lastStartupTimingSummary = $"Startup timing {context}: total {totalTimer.ElapsedMilliseconds} ms ({timing}).";
            Debug.Log($"[StartupTiming] {lastStartupTimingSummary}", this);
        }

        private void ConfigurePopulationManager()
        {
            if (populationManager == null)
            {
                return;
            }

            populationManager.Configure(townWorld, populationSettings, false);
        }

        private void ConfigureScenarioAuthorities()
        {
            systemsHub ??= FindAnyObjectByType<SimulationSystemsHub>();
            ScenarioDirector scenarioDirector = FindAnyObjectByType<ScenarioDirector>();
            PopulationState population = populationManager != null ? populationManager.State : null;
            if (systemsHub == null || scenarioDirector == null || population == null)
            {
                return;
            }

            playerHouseholdMemberships = new HouseholdMembershipRegistry();
            playerHouseholdKinship = new KinshipRegistry();
            playerHouseholdLifecycle = new HouseholdLifecycleManager(
                population,
                playerHouseholdMemberships,
                playerHouseholdKinship,
                systemsHub.Ids);
            playerHouseholdLifecycle.EnsureEntityIds();

            scenarioDirector.Lifecycle = playerHouseholdLifecycle;
            scenarioDirector.FounderResolver = ResolveScenarioFounders;

            PersonState firstPerson = FindFirstLivingPerson(population);
            if (firstPerson != null)
            {
                scenarioDirector.PlayerDirector = new PlayerDirector(
                    LandLedgers.Primitives.EntityId.For(EntityKind.Person, firstPerson.id),
                    "town-core");
            }
        }

        private List<int> ResolveScenarioFounders(string requestedDisplayName)
        {
            var resolved = new List<int>();
            PopulationState population = populationManager != null ? populationManager.State : null;
            if (population == null || population.people == null)
            {
                return resolved;
            }

            bool hasRequestedName = !string.IsNullOrWhiteSpace(requestedDisplayName);
            for (int i = 0; i < population.people.Count; i++)
            {
                PersonState person = population.people[i];
                if (person == null || person.deathDayIndex >= 0)
                {
                    continue;
                }

                if (!hasRequestedName
                    || string.Equals(person.DisplayName, requestedDisplayName.Trim(), System.StringComparison.OrdinalIgnoreCase))
                {
                    resolved.Add(person.id);
                    if (hasRequestedName || resolved.Count == 1)
                    {
                        break;
                    }
                }
            }

            return resolved;
        }

        private static PersonState FindFirstLivingPerson(PopulationState population)
        {
            if (population == null || population.people == null)
            {
                return null;
            }

            for (int i = 0; i < population.people.Count; i++)
            {
                PersonState person = population.people[i];
                if (person != null && person.deathDayIndex < 0)
                {
                    return person;
                }
            }

            return null;
        }

        private void ConfigureCivicFoundation()
        {
            if (civicFoundation == null)
            {
                return;
            }

            civicFoundation.Configure(townWorld);
        }

        private void ConfigureLogisticsRuntime()
        {
            if (logisticsRuntime == null)
            {
                GameObject runtimeObject = new("Logistics Runtime");
                logisticsRuntime = runtimeObject.AddComponent<LogisticsRuntimeManager>();
            }
        }

        private void ConfigureStoreRuntime()
        {
            if (storeRuntime == null)
            {
                return;
            }

            storeRuntime.Configure(townWorld, populationManager, timeManager, storeDefinition, hudController, townPulseRuntime, playerPortfolioManager, logisticsRuntime);
        }

        private void ConfigureTownPulseRuntime()
        {
            if (townPulseRuntime == null)
            {
                GameObject runtimeObject = new("Town Pulse Runtime");
                townPulseRuntime = runtimeObject.AddComponent<TownPulseRuntimeManager>();
            }

            townPulseRuntime.Configure(timeManager, hudController);
        }

        private void ConfigureAcquisitionMarket()
        {
            if (acquisitionMarket == null)
            {
                return;
            }

            acquisitionMarket.Configure(townWorld, storeRuntime, sharedBusinessRuntime, populationManager, timeManager, civicFoundation, playerPortfolioManager);
        }

        private void ApplyFreshAcquisitionMarketSeedAuthority()
        {
            if (acquisitionMarket == null || townWorld == null || townWorld.Settings == null)
            {
                return;
            }

            acquisitionMarket.ApplyFreshWorldSeedAuthority(townWorld.Settings.seed);
        }

        private void ConfigureSharedBusinessRuntime()
        {
            if (sharedBusinessRuntime == null)
            {
                GameObject runtimeObject = new("Shared Business Runtime");
                sharedBusinessRuntime = runtimeObject.AddComponent<SharedBusinessRuntimeManager>();
            }

            sharedBusinessRuntime.Configure(townWorld, null, populationManager, timeManager, storeRuntime, playerPortfolioManager);
        }

        private void ConfigurePopulationPathingDirector()
        {
            if (populationPathingDirector == null)
            {
                GameObject pathingDirectorObject = new("Population Pathing Director");
                populationPathingDirector = pathingDirectorObject.AddComponent<PopulationPathingDirector>();
            }

            populationPathingDirector.Configure(townWorld, populationManager, pathingManager, timeManager, storeRuntime);
        }

        private void ConfigurePlayerDebtManager()
        {
            if (playerDebtManager == null)
            {
                GameObject debtObject = new("Player Debt Manager");
                playerDebtManager = debtObject.AddComponent<PlayerDebtManager>();
            }

            playerDebtManager.Configure(timeManager, storeRuntime, acquisitionMarket, sharedBusinessRuntime, playerPortfolioManager);
        }

        private void ConfigurePlayerPortfolioManager()
        {
            if (playerPortfolioManager == null)
            {
                GameObject portfolioObject = new("Player Portfolio Manager");
                playerPortfolioManager = portfolioObject.AddComponent<PlayerPortfolioManager>();
            }

            playerPortfolioManager.Configure(timeManager, hudController);
        }

        /// <summary>
        /// P4: applies the First Ledger scenario's promised "stake of cash".
        /// The scenario asset declares it as the 'startingCashCents' tunable;
        /// the asset value wins when a ScenarioDirector is present, otherwise
        /// the serialized fallback (which mirrors the asset) applies. The stake
        /// lands in owner cash with a named ledger reason — a real endowment,
        /// never conjured mid-simulation.
        /// </summary>
        private void SeedPlayerStartingStake()
        {
            if (playerPortfolioManager == null)
            {
                return;
            }

            ScenarioDirector director = FindAnyObjectByType<ScenarioDirector>();
            int stake = ResolveStartingStakeCents(
                director != null ? director.GetTunable("startingCashCents") : null,
                fallbackStartingCashCents);
            if (stake <= 0)
            {
                return;
            }

            playerPortfolioManager.AddOwnerCash(stake, "First Ledger starting stake");
        }

        /// <summary>
        /// P4: resolves the scenario's starting-cash tunable. The asset's int
        /// tunable wins when present and positive; otherwise the fallback.
        /// </summary>
        public static int ResolveStartingStakeCents(TunableValue tunable, int fallbackCents)
        {
            if (tunable != null
                && tunable.Kind == TunableValue.ValueKind.Int
                && tunable.IntValue > 0)
            {
                return tunable.IntValue;
            }

            return Mathf.Max(0, fallbackCents);
        }

        private void ConfigureOpportunityPressureRuntime()
        {
            if (opportunityPressureRuntime == null)
            {
                GameObject opportunityObject = new("Opportunity Pressure Runtime");
                opportunityPressureRuntime = opportunityObject.AddComponent<OpportunityPressureRuntimeManager>();
            }

            opportunityPressureRuntime.Configure(
                townWorld,
                populationManager,
                storeRuntime,
                sharedBusinessRuntime,
                townPulseRuntime,
                timeManager,
                hudController);
        }

        private void ConfigureFirstSessionGuidance()
        {
            if (firstSessionGuidance == null)
            {
                GameObject guidanceObject = new("First Session Guidance");
                firstSessionGuidance = guidanceObject.AddComponent<FirstSessionGuidanceManager>();
            }

            firstSessionGuidance.Configure(
                hudController,
                storePanel,
                storeRuntime,
                acquisitionMarket,
                playerPortfolioManager,
                sharedBusinessRuntime,
                townWorld,
                timeManager);
        }

        private void ConfigureWorldInteractionController()
        {
            if (worldInteractionController == null)
            {
                GameObject interactionObject = new("World Interaction Controller");
                worldInteractionController = interactionObject.AddComponent<WorldInteractionController>();
            }

            worldInteractionController.Configure(
                FindAnyObjectByType<StrategyCameraController>(),
                townWorld,
                storePanel);
        }

        private static void EnsureInputSystemEventSystem()
        {
            EventSystem eventSystem = FindAnyObjectByType<EventSystem>();
            if (eventSystem == null)
            {
                GameObject eventSystemObject = new("UI Event System");
                eventSystem = eventSystemObject.AddComponent<EventSystem>();
                eventSystemObject.AddComponent<InputSystemUIInputModule>();
                return;
            }

            if (eventSystem.GetComponent<InputSystemUIInputModule>() == null)
            {
                eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
            }
        }

        private void AutoWire()
        {
            townWorld ??= FindAnyObjectByType<TownWorldController>();
            pathingManager ??= FindAnyObjectByType<PathingManager>();
            timeManager ??= TimeManager.Instance != null ? TimeManager.Instance : FindAnyObjectByType<TimeManager>();
            populationManager ??= FindAnyObjectByType<PopulationManager>();
            civicFoundation ??= FindAnyObjectByType<CivicFoundationManager>();
            storeRuntime ??= FindAnyObjectByType<GeneralStoreRuntimeManager>();
            logisticsRuntime ??= FindAnyObjectByType<LogisticsRuntimeManager>();
            sharedBusinessRuntime ??= FindAnyObjectByType<SharedBusinessRuntimeManager>();
            townPulseRuntime ??= FindAnyObjectByType<TownPulseRuntimeManager>();
            opportunityPressureRuntime ??= FindAnyObjectByType<OpportunityPressureRuntimeManager>();
            acquisitionMarket ??= FindAnyObjectByType<AcquisitionMarketManager>();
            playerDebtManager ??= FindAnyObjectByType<PlayerDebtManager>();
            playerPortfolioManager ??= FindAnyObjectByType<PlayerPortfolioManager>();
            saveLoadManager ??= FindAnyObjectByType<SaveLoadManager>();
            populationPathingDirector ??= FindAnyObjectByType<PopulationPathingDirector>();
            storePanel ??= FindAnyObjectByType<GeneralStorePanelController>();
            worldInteractionController ??= FindAnyObjectByType<WorldInteractionController>();
            hudController ??= FindAnyObjectByType<LandLedgersHUDController>();
            firstSessionGuidance ??= FindAnyObjectByType<FirstSessionGuidanceManager>();
            preAuthoredTerrainProfile ??= FindAnyObjectByType<PreAuthoredTerrainWorldProfile>();
            systemsHub ??= FindAnyObjectByType<SimulationSystemsHub>();

            if (saveLoadManager == null)
            {
                GameObject saveLoadObject = new("Save Load Manager");
                saveLoadManager = saveLoadObject.AddComponent<SaveLoadManager>();
            }

            if (civicFoundation == null)
            {
                GameObject civicObject = new("Civic Foundation");
                civicFoundation = civicObject.AddComponent<CivicFoundationManager>();
            }
        }
    }
}
