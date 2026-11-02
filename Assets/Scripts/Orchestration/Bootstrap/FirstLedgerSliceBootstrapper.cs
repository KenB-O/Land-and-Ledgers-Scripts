using System;
using System.Collections.Generic;
using LandLedgers.Civic;
using System.Text;
using LandLedgers.CameraSystem;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.BoardingHouse;
using LandLedgers.Economy.Creation;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Transport;
using LandLedgers.Orchestration.Bootstrap;
using LandLedgers.Orchestration.Scenarios;
using LandLedgers.Orchestration.Scenarios.Development;
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

        [SerializeField, Tooltip("Enables the small three-business roster for focused Main Scene playtesting; canonical opening defaults remain unchanged.")]
        private bool useCompactPlaytestOpeningRoster;

        [SerializeField, Tooltip("Playtest only: treat the player as a regular NPC. Forwards to SimulationDrivers.TreatPlayerAsNpc on startup and on Continue. OR logic: true forces NPC mode on, false leaves a restored save value untouched and never clears a saved true. Codex must set this in Main Scene Inspector; it is code only here.")]
        private bool treatPlayerAsNpcPlaytest;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [Header("Development Population Fixture")]
        [SerializeField, Tooltip("Optional Pass XL fixture. Effective only when explicitly enabled for a fresh New Game; ordinary settings and Continue/restore are unchanged.")]
        private SixPersonDevelopmentFixture developmentPopulationFixture;

        [SerializeField, Tooltip("Explicitly opts this development startup into the assigned six-person fixture.")]
        private bool enableDevelopmentPopulationFixture;

        [SerializeField, Tooltip("Opt-in New Game profile: append the configured ExpandedSliceFixture to its six-person base. Ordinary scenarios and the six-person profile are unchanged.")]
        private bool enableExpandedSliceDevelopmentFixture;

        [SerializeField, Tooltip("Optional expanded fixture asset. If empty, the development asset at Assets/Core/Population/ExpandedSliceFixture.asset is resolved in the Editor.")]
        private ExpandedSliceFixture expandedSliceDevelopmentFixture;
#endif

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

        public string LastStartupTimingSummary => lastStartupTimingSummary ?? string.Empty;
        public WorldStartupIntent StartupIntent => startupIntent;
        public bool IsInitialized => initialized;
        public bool IsStartupReadyForManagementUi => initialized || initializing;

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

            // Pass XII Worker A (LL-PLAY-001): a New Game starts with a cash
            // stake, never an automatic player business. Clear the store
            // runtime's auto-create flag unconditionally, for every startup
            // intent, before any runtime Start can initialize: the old code
            // cleared it only when a ScenarioDirector was present, so a
            // scene without one auto-formed "player_general_store" on the
            // store runtime's Start and flipped an NPC building to
            // player-owned. Continue is safe too: a restored store
            // short-circuits initialization, while a save with no store
            // must not invent one on load either. Execution order keeps
            // this ahead of the store runtime (bootstrapper 240, store 260).
            storeRuntime?.SetCreatePlayerBusinessOnInitialize(false);
            ReportDuplicateSceneAuthorities();

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

                // A loaded campaign bypasses the fresh-world setup below. Rebind
                // scenario/player authorities here as well so development tools and
                // the authored scenario remain usable after a continue/reload.
                ConfigureScenarioAuthorities();
                // Phase 2: forward the playtest bool AFTER the save load,
                // so SaveLoadManager's restored flag is the base and this
                // bool can only turn NPC mode on (OR logic), never clear
                // a saved true. See PlayerNpcPlaytestForwarding.
                ForwardPlayerNpcPlaytestFlag();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                EnsureScenarioValidationHarness();
#endif
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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ExpandedSliceFixture expandedFixture = ResolveExpandedSliceFixture();
            bool useExpandedSliceProfile = startupIntent == WorldStartupIntent.NewGame
                && enableExpandedSliceDevelopmentFixture && expandedFixture != null;
            bool useSixPersonDevelopmentProfile = startupIntent == WorldStartupIntent.NewGame
                && !useExpandedSliceProfile
                && enableDevelopmentPopulationFixture
                && developmentPopulationFixture != null;
            if ((useSixPersonDevelopmentProfile || useExpandedSliceProfile) && generateTownOnPlay && townWorld.Grid == null)
            {
                SixPersonDevelopmentFixture baseFixture = useExpandedSliceProfile
                    ? expandedFixture.SixPersonFixture : developmentPopulationFixture;
                int requestedHomes = useExpandedSliceProfile ? ExpandedSliceFixture.RequiredHouseholdCount : baseFixture.Households.Count;
                int requestedBusinesses = useExpandedSliceProfile ? ExpandedSliceFixture.RequiredBusinessCount : OpeningBusinessRoster.SixPersonDevelopment().Count;
                if (!townWorld.TryApplyDevelopmentResidentialHomeCount(
                    requestedHomes, out string developmentWorldReason, requestedBusinesses))
                {
                    bootstrapStatus = $"Development fixture startup stopped before world generation: {developmentWorldReason}";
                    Debug.LogError($"[WorldStartup] {bootstrapStatus}", this);
                    CompleteStartupTiming(totalTimer, timing, "development world configuration failed");
                    initializing = false;
                    return;
                }

                Debug.Log($"[WorldStartup] {developmentWorldReason}", this);
            }
#endif

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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (useSixPersonDevelopmentProfile || useExpandedSliceProfile)
            {
                int requiredResidentialHomes = useExpandedSliceProfile
                    ? ExpandedSliceFixture.RequiredHouseholdCount : developmentPopulationFixture.Households.Count;
                int requiredCommercialBuildings = useExpandedSliceProfile
                    ? ExpandedSliceFixture.RequiredBusinessCount : OpeningBusinessRoster.SixPersonDevelopment().Count;
                int realResidentialHomes = 0;
                int realCommercialBuildings = 0;
                int realTotalBuildings = townWorld.Buildings.Count;
                StringBuilder buildingRoster = new();
                foreach (PlacedBuilding building in townWorld.Buildings)
                {
                    if (building?.definition == null)
                    {
                        continue;
                    }

                    if (building.definition.CanHostHouseholds && !building.definition.CanHostWorkplace
                        && townWorld.TryGetPlotById(building.plotId, out TownPlot plot)
                        && plot != null && plot.zone == PlotZone.Residential)
                    {
                        realResidentialHomes++;
                    }

                    if (building.definition.CanHostWorkplace
                        && building.definition.PrimaryUse != BuildingUseType.Civic)
                    {
                        realCommercialBuildings++;
                    }

                    if (buildingRoster.Length > 0)
                    {
                        buildingRoster.Append("; ");
                    }

                    buildingRoster.Append($"{building.id}:{building.definition.BuildingId}");
                }

                int requiredTotalBuildings = requiredResidentialHomes + requiredCommercialBuildings;
                if (realResidentialHomes != requiredResidentialHomes
                    || realCommercialBuildings != requiredCommercialBuildings
                    || realTotalBuildings != requiredTotalBuildings)
                {
                    bootstrapStatus = $"Development startup stopped: standalone homes={realResidentialHomes}/{requiredResidentialHomes}, storefronts={realCommercialBuildings}/{requiredCommercialBuildings}, total buildings={realTotalBuildings}/{requiredTotalBuildings}. No Businesses or Persons were initialized.";
                    Debug.LogError($"[WorldStartup] {bootstrapStatus}", this);
                    CompleteStartupTiming(totalTimer, timing, "development homes unavailable");
                    initializing = false;
                    return;
                }

                Debug.Log($"[WorldStartup] Development profile verified: {realResidentialHomes} standalone homes, " +
                          $"{realCommercialBuildings} storefronts. Buildings: {buildingRoster}.", this);
            }
#endif

            // The authored SimulationDrivers component can Awake before this
            // bootstrap creates the TownGrid. Rebind after world generation so
            // hub-owned parcel services receive the actual live grid on a fresh
            // New Game, not only after a later save restore.
            FindAnyObjectByType<SimulationDrivers>()?.Rebind();

            ConfigurePopulationManager();
            ConfigureCivicFoundation();
            AppendStartupTiming(timing, stepTimer, "population/civic configure");

            // Population birth dates, derived ages, seasonal demand and due
            // events must be generated against the selected scenario date.
            // The scenario remains inactive until the player's Start action.
            ScenarioStartPanelController scenarioStart = FindAnyObjectByType<ScenarioStartPanelController>();
            if (scenarioStart != null && !scenarioStart.PrepareScenarioClockForWorldGeneration())
            {
                Debug.LogError("[WorldStartup] The selected scenario date could not be applied before population generation.", this);
            }

            // PopulationManager.GeneratePopulationSnapshot initializes the shared
            // Business runtime while building the initial settlement. Apply the
            // selected roster before that call; applying it after population
            // generation silently leaves the canonical default roster in place.
            ConfigureOpeningBusinessRoster();

            if (generatePopulationOnPlay && populationManager != null)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (startupIntent == WorldStartupIntent.NewGame && useExpandedSliceProfile)
                {
                    populationManager.GeneratePopulationSnapshot(expandedFixture.SixPersonFixture);
                    systemsHub ??= FindAnyObjectByType<SimulationSystemsHub>();
                    populationManager.HouseholdLedgers ??= systemsHub != null ? systemsHub.HouseholdLedgers : null;
                    PopulationGenerationResult expandedResult = PopulationGenerator.AppendExpandedDevelopmentFixture(
                        townWorld, populationSettings, populationManager.State, expandedFixture,
                        populationManager.Lifecycle, populationManager.Kinship,
                        populationManager.HouseholdLedgers, timeManager != null ? timeManager.CurrentAbsoluteDayIndex : 0,
                        timeManager != null ? timeManager.CurrentDate.Year : 1870,
                        timeManager != null ? timeManager.CurrentDate.Month : 1,
                        timeManager != null ? timeManager.CurrentDate.DayOfMonth : 1);
                    if (expandedResult.HasErrors)
                    {
                        bootstrapStatus = expandedResult.Summary;
                        Debug.LogError("[WorldStartup] " + bootstrapStatus, this);
                        CompleteStartupTiming(totalTimer, timing, "expanded fixture generation failed");
                        initializing = false;
                        return;
                    }
                    Debug.Log("[WorldStartup] " + expandedResult.Summary, this);
                    populationManager.RecalculateHouseholdIncomeAndSummaries(expandedResult.Summary);
                    populationManager.SweepUnledgeredHouseholdCashToLedgers("expanded development fixture generation");
                }
                else if (startupIntent == WorldStartupIntent.NewGame
                    && enableDevelopmentPopulationFixture
                    && developmentPopulationFixture != null)
                {
                    populationManager.GeneratePopulationSnapshot(developmentPopulationFixture);
                }
                else
                {
                    populationManager.GeneratePopulationSnapshot();
                }
#else
                populationManager.GeneratePopulationSnapshot();
#endif
            }
            AppendStartupTiming(timing, stepTimer, "population generation");

            // FamilyTreeBuilder is intentionally separate from population
            // generation: its off-map relatives share PopulationState.people
            // and must not be mistaken for residents or migrant arrivals.
            // Build the authoritative kinship links once the generated
            // residents exist, before any save can capture this New Game.
            if (populationManager?.State != null && populationManager.Kinship != null)
            {
                int familySeed = townWorld.Settings != null ? townWorld.Settings.seed : 0;
                int referenceYear = timeManager != null ? timeManager.CurrentDate.Year : 0;
                if (referenceYear > 0)
                {
                    var familyDiagnostics = new System.Collections.Generic.List<string>();
                    FamilyTreeBuilder.Report familyReport = FamilyTreeWiring.ApplyAfterGeneration(
                        populationManager.State,
                        populationManager.Kinship,
                        familySeed,
                        referenceYear,
                        familyDiagnostics);
                    Debug.Log($"[WorldStartup] Family tree wired from generated residents: " +
                              $"{familyReport.PersonsProcessed} residents, " +
                              $"{familyReport.OffMapRelativesCreated} off-map relatives, " +
                              $"{familyReport.SiblingEdgesAdded} sibling links.", this);
                }
                else
                {
                    Debug.LogWarning("[WorldStartup] Family-tree wiring skipped because the authoritative scenario year is unavailable.", this);
                }
            }

            ConfigureScenarioAuthorities();

            // Phase 2: forward the playtest bool on New Game startup.
            // Population generation already set SuppressMigrantArrivals
            // for a six-person fixture; this flag is the separate
            // player-as-NPC switch, default false.
            ForwardPlayerNpcPlaytestFlag();

            ConfigureTownPulseRuntime();
            ConfigurePlayerPortfolioManager();
            ConfigureLogisticsRuntime();
            ConfigureStoreRuntime();
            // The automatic player business is already suppressed for every
            // startup intent at the top of InitializeSlice (Pass XII
            // LL-PLAY-001); the authored town roster remains NPC-owned and
            // the player's first business is formed explicitly through the
            // Businesses workflow.
            storeRuntime?.InitializeIfNeeded();
            ConfigureSharedBusinessRuntime();
            sharedBusinessRuntime?.InitializeIfNeeded(storeRuntime != null ? storeRuntime.CurrentBusiness : null);
            SimulationDrivers simulationDrivers = FindAnyObjectByType<SimulationDrivers>();
            simulationDrivers?.AttachBusinessRuntime(sharedBusinessRuntime);
            sharedBusinessRuntime?.ReconcileGeneratedWorkplaceAssignments();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            BusinessInstanceState developmentNpcStore = null;
            if (startupIntent == WorldStartupIntent.NewGame && (enableDevelopmentPopulationFixture || enableExpandedSliceDevelopmentFixture)
                && (developmentPopulationFixture != null || ResolveExpandedSliceFixture()?.SixPersonFixture != null))
            {
                developmentNpcStore = FindDevelopmentNpcGeneralStore();
                SeedDevelopmentNpcStoreOpeningStock(developmentNpcStore);
            }
#endif
            // Register the live store with the shared supplier directory. The
            // household membership binding remains an explicit scenario/world
            // concern; registering the seller alone cannot execute a purchase.
            if (simulationDrivers != null && storeRuntime != null)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                simulationDrivers.RegisterGeneralStoreSupplier(developmentNpcStore != null
                    ? storeRuntime.BuildHouseholdTradingPort(developmentNpcStore)
                    : storeRuntime.BuildHouseholdTradingPort());
#else
                simulationDrivers.RegisterGeneralStoreSupplier(storeRuntime.BuildHouseholdTradingPort());
#endif
            }
            simulationDrivers?.EnsureGeneralStorePostOfficeForCurrentWorld();
            systemsHub ??= FindAnyObjectByType<SimulationSystemsHub>();
            RegisterLiveBoardingHouseRuntimes(simulationDrivers);
            OpeningTransportStock.Ensure(
                systemsHub,
                sharedBusinessRuntime != null ? sharedBusinessRuntime.Businesses : null,
                timeManager != null ? timeManager.CurrentAbsoluteDayIndex : 0);
            RunOpeningSettlementReconciliation();
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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            EnsureScenarioValidationHarness();
#endif

            initialized = true;
            bootstrapStatus = $"Game slice ready. Buildings={townWorld.Buildings.Count}, People={(populationManager != null ? populationManager.GeneratedPersonCount : 0)}, Store={(storeRuntime != null ? storeRuntime.Status : "missing")}.";
            if (logBootstrapSummary)
            {
                Debug.Log(bootstrapStatus, this);
            }

            CompleteStartupTiming(totalTimer, timing, "fresh startup");
            initializing = false;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void EnsureScenarioValidationHarness()
        {
            if (FindAnyObjectByType<ScenarioValidationHarness>() != null)
            {
                return;
            }

            GameObject harnessObject = new GameObject("Scenario Validation Harness");
            harnessObject.AddComponent<ScenarioValidationHarness>();
            DontDestroyOnLoad(harnessObject);
        }
#endif

        public void MarkRestoredFromSave(string persistenceSummary = "")
        {
            AutoWire();
            ReportDuplicateSceneAuthorities();
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
            // Pass XII Worker F (LL-PLAY-001, D7): suppress AFTER the
            // store runtime is (re-)wired, so this covers whichever
            // instance ConfigureStoreRuntime found. InitializeSlice
            // suppresses for every startup intent, but a restore path
            // that reaches here without it (dev-tool restore, re-wired
            // store instance) would otherwise leave the flag at its
            // true default and ghost-create a player store on the next
            // InitializeIfNeeded. Idempotent.
            storeRuntime?.SetCreatePlayerBusinessOnInitialize(false);
            ConfigureSharedBusinessRuntime();
            ConfigurePopulationPathingDirector();
            ConfigureAcquisitionMarket();
            acquisitionMarket?.RebuildMarket();
            ConfigurePlayerDebtManager();
            ConfigureOpportunityPressureRuntime();
            ConfigureFirstSessionGuidance();
            ConfigureWorldInteractionController();
            opportunityPressureRuntime?.RefreshNotices();
            storePanel?.Refresh();
            // Pass XII Worker A: the first real production caller for
            // SimulationDrivers.Rebind. SaveLoadManager restores the hub,
            // businesses and scenario state from the file before calling
            // this hook, so the drivers re-discover the live authorities
            // instead of driving through stale references. Rebind is
            // idempotent (subscription and manager-execution guards) and
            // forwards an already-assigned PlayerDirector without clearing
            // one that bootstrap assignment has not set yet.
            FindAnyObjectByType<SimulationDrivers>()?.Rebind();
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

        /// <summary>
        /// Pass XII Worker A (LL-PLAY-002/003): counts live scene instances
        /// of the authorities that must exist exactly once and logs an
        /// error naming every duplicate. Detection only — the bootstrapper
        /// keeps running on the first found instance rather than refusing
        /// to start, and removing the extra scene object is Codex's scene
        /// work. Runs on Start and after every restore. A zero count is
        /// not an error here: the Configure* fallbacks create the missing
        /// runtime object when the scene does not author it.
        /// </summary>
        private void ReportDuplicateSceneAuthorities()
        {
            var censuses = new List<BootstrapAuthorityAudit.Census>
            {
                BuildAuthorityCensus<SimulationSystemsHub>("SimulationSystemsHub"),
                BuildAuthorityCensus<SimulationDrivers>("SimulationDrivers"),
                BuildAuthorityCensus<CivicFoundationManager>("CivicFoundation"),
                BuildAuthorityCensus<ScenarioDirector>("ScenarioDirector"),
                BuildAuthorityCensus<FirstLedgerSliceBootstrapper>("FirstLedgerSliceBootstrapper"),
            };
            foreach (string error in BootstrapAuthorityAudit.DescribeDuplicates(censuses))
            {
                Debug.LogError($"[WorldStartup] {error}", this);
            }
        }

        private static BootstrapAuthorityAudit.Census BuildAuthorityCensus<T>(string name) where T : UnityEngine.Object
        {
            var census = new BootstrapAuthorityAudit.Census { Name = name };
            foreach (T instance in FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                census.ObjectNames.Add(instance != null ? instance.name : "<unnamed>");
            }

            return census;
        }

        private void ConfigurePopulationManager()
        {
            if (populationManager == null)
            {
                return;
            }

            populationManager.Configure(townWorld, populationSettings, false);
        }

        private void RunOpeningSettlementReconciliation()
        {
            if (populationManager == null || sharedBusinessRuntime == null)
            {
                return;
            }

            sharedBusinessRuntime.ResolveProvableOpeningBusinessOwners();
            SettlementFoundationReconciliation reconciliation = SettlementFoundationReconciliation.Check(
                populationManager.State,
                sharedBusinessRuntime.Businesses);
            if (!reconciliation.IsValid)
            {
                Debug.LogError($"[WorldGenesis] {reconciliation.BuildSummary()}\n" +
                    string.Join("\n", reconciliation.Errors), this);
            }
            else if (reconciliation.Warnings.Count > 0)
            {
                Debug.LogWarning($"[WorldGenesis] {reconciliation.BuildSummary()}\n" +
                    string.Join("\n", reconciliation.Warnings), this);
            }
        }

        private void ConfigureScenarioAuthorities()
        {
            systemsHub ??= FindAnyObjectByType<SimulationSystemsHub>();
            ScenarioDirector scenarioDirector = FindAnyObjectByType<ScenarioDirector>();
            // The calendar/economic driver is a required runtime authority for a
            // playable First Ledger world. Older scene snapshots did not serialize
            // the component, which left weekly profit, owner-labor and objective
            // refresh events disconnected even though the rest of the authorities
            // existed. Create the one shared driver on the bootstrap object when a
            // legacy scene is missing it; this is wiring, not a second simulation.
            SimulationDrivers drivers = FindAnyObjectByType<SimulationDrivers>();
            if (drivers == null)
            {
                drivers = gameObject.AddComponent<SimulationDrivers>();
            }
            PopulationState population = populationManager != null ? populationManager.State : null;
            if (systemsHub == null || scenarioDirector == null || population == null)
            {
                return;
            }

            // PopulationManager owns the authoritative membership, kinship, and
            // lifecycle stack. A second lifecycle here would maintain a parallel
            // household-membership registry and could accept identities already
            // assigned by the population system.
            HouseholdLifecycleManager lifecycle = populationManager.Lifecycle;
            if (lifecycle == null)
            {
                Debug.LogWarning("[WorldStartup] PopulationManager has no household lifecycle authority; ScenarioDirector lifecycle remains unbound.", this);
            }
            else
            {
                lifecycle.EnsureEntityIds();
                scenarioDirector.Lifecycle = lifecycle;
            }
            scenarioDirector.FounderResolver = ResolveScenarioFounders;
            // Pass XI WS1: the director's objective hook needs the
            // hub-owned service. The scenario bootstraps assign the same
            // reference on Awake. Pass XI WS6 first-gate (M2)
            // correction: this assignment alone never registered
            // qualifiers for a late-awakening bootstrap — registration
            // is the bootstraps' own Awake work (they self-register
            // when the director already runs their scenario); this
            // assignment only keeps the director's objective hook
            // wired for future starts.
            scenarioDirector.ObjectiveService ??= systemsHub.ScenarioObjectives;

            PersonState firstPerson = FindFirstLivingPerson(population);
            if (firstPerson != null)
            {
                PlayerDirector playerDirector = new PlayerDirector(
                    LandLedgers.Primitives.EntityId.For(EntityKind.Person, firstPerson.id),
                    "town-core");
                scenarioDirector.PlayerDirector = playerDirector;
                if (drivers != null)
                {
                    drivers.PlayerDirector = playerDirector;
                }
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
                if (person == null || person.deathDayIndex >= 0 || person.isOffMap)
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
                if (person != null && person.deathDayIndex < 0 && !person.isOffMap)
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
            ConfigureOpeningBusinessRoster();
        }

        /// <summary>
        /// Phase 2: forwards the playtest bool to SimulationDrivers.
        /// OR logic only (see PlayerNpcPlaytestForwarding): true forces
        /// NPC mode on, false leaves the drivers value untouched. Called
        /// on New Game after ConfigureScenarioAuthorities and on
        /// Continue after LoadDefaultSlot + ConfigureScenarioAuthorities.
        /// Codex must set treatPlayerAsNpcPlaytest in Main Scene
        /// Inspector; this method alone does not wire a scene.
        /// </summary>
        private void ForwardPlayerNpcPlaytestFlag()
        {
            SimulationDrivers drivers = FindAnyObjectByType<SimulationDrivers>();
            if (drivers == null)
            {
                return;
            }

            bool npcMode = LandLedgers.Orchestration.Player.PlayerNpcPlaytestForwarding.ApplyBootstrapperFlag(
                drivers, treatPlayerAsNpcPlaytest);
            if (npcMode)
            {
                Debug.Log("[WorldStartup] Player-as-NPC playtest mode is on (bootstrapper or restored save).", this);
            }
        }

        private BusinessInstanceState FindDevelopmentNpcGeneralStore()
        {
            if (sharedBusinessRuntime?.Businesses == null) return null;
            foreach (BusinessInstanceState business in sharedBusinessRuntime.Businesses)
            {
                if (business != null && business.BusinessType == BusinessType.GeneralStore
                    && business.Owner != null && business.Owner.OwnerKind == BusinessOwnerKind.Npc
                    && business.Owner.PersonId == 1 && business.RuntimeState != null)
                    return business;
            }
            Debug.LogError("[WorldStartup] Six-person fixture opening-stock endowment was not applied: no NPC General Store owned by P001 was found.", this);
            return null;
        }

        private void SeedDevelopmentNpcStoreOpeningStock(BusinessInstanceState business)
        {
            SixPersonDevelopmentFixture stockFixture = enableExpandedSliceDevelopmentFixture
                ? ResolveExpandedSliceFixture()?.SixPersonFixture
                : developmentPopulationFixture;
            if (business?.RuntimeState == null || stockFixture == null || storeRuntime?.StoreDefinition == null)
                return;

            int seed = townWorld?.Settings != null ? townWorld.Settings.seed : 0;
            if (!ScenarioOpeningStockEndowment.TryApply(stockFixture, business,
                    storeRuntime.StoreDefinition, seed,
                    timeManager != null ? timeManager.CurrentAbsoluteDayIndex : 0,
                    out string report, out string error))
            {
                Debug.LogError($"[WorldStartup] {error}", this);
                return;
            }
            Debug.Log($"[WorldStartup] {report}", this);
        }

        private void ConfigureOpeningBusinessRoster()
        {
            sharedBusinessRuntime ??= FindAnyObjectByType<SharedBusinessRuntimeManager>();
            System.Collections.Generic.IReadOnlyList<OpeningRosterEntry> roster =
                useCompactPlaytestOpeningRoster ? OpeningBusinessRoster.CompactPlaytest() : null;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (startupIntent == WorldStartupIntent.NewGame && enableExpandedSliceDevelopmentFixture)
            {
                ExpandedSliceFixture expanded = ResolveExpandedSliceFixture();
                roster = expanded != null ? BuildExpandedOpeningRoster(expanded) : null;
            }
            else if (startupIntent == WorldStartupIntent.NewGame
                && enableDevelopmentPopulationFixture
                && developmentPopulationFixture != null)
            {
                roster = OpeningBusinessRoster.SixPersonDevelopment();
            }
#endif
            sharedBusinessRuntime?.ConfigureOpeningBusinessRoster(roster);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private ExpandedSliceFixture ResolveExpandedSliceFixture()
        {
            if (expandedSliceDevelopmentFixture != null) return expandedSliceDevelopmentFixture;
#if UNITY_EDITOR
            expandedSliceDevelopmentFixture = UnityEditor.AssetDatabase.LoadAssetAtPath<ExpandedSliceFixture>(
                "Assets/Core/Population/ExpandedSliceFixture.asset");
#endif
            if (enableExpandedSliceDevelopmentFixture && expandedSliceDevelopmentFixture == null)
                Debug.LogError("[WorldStartup] Expanded fixture is enabled but Assets/Core/Population/ExpandedSliceFixture.asset could not be loaded in the Editor.", this);
            return expandedSliceDevelopmentFixture;
        }

        private static List<OpeningRosterEntry> BuildExpandedOpeningRoster(ExpandedSliceFixture fixture)
        {
            var roster = new List<OpeningRosterEntry>();
            if (fixture == null) return roster;
            foreach (ExpandedSliceFixture.BusinessSlot slot in fixture.Businesses)
            {
                BusinessType type = slot.operation switch
                {
                    ExpandedSliceFixture.Operation.GeneralStore => BusinessType.GeneralStore,
                    ExpandedSliceFixture.Operation.Bakery => BusinessType.Bakery,
                    ExpandedSliceFixture.Operation.FuelDealer => BusinessType.FuelDealer,
                    ExpandedSliceFixture.Operation.FreightCompany => BusinessType.LiveryFreight,
                    _ => throw new ArgumentOutOfRangeException()
                };
                int ownerId = slot.ownerPersonKey.StartsWith("base:", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(slot.ownerPersonKey.Substring(5), out int personIndex)
                    ? personIndex + 1 : slot.operation == ExpandedSliceFixture.Operation.FreightCompany ? 7 : -1;
                string ownerName = slot.ownerPersonKey switch
                {
                    "base:0" => "Kennedy Baldwin-Ooms",
                    "base:2" => "Glen Pierce",
                    "base:4" => "Jacob Boniecki",
                    "ethan" => "Ethan Boniecki",
                    _ => "Development fixture"
                };
                var premises = type switch
                {
                    BusinessType.GeneralStore => new PremisesRequirement(customerFacing: true, minAreaSqFt: 800),
                    BusinessType.Bakery => new PremisesRequirement(customerFacing: true, foodHandling: true, minAreaSqFt: 500),
                    BusinessType.FuelDealer => new PremisesRequirement(yardStorage: true, minAreaSqFt: 600),
                    _ => new PremisesRequirement()
                };
                string displayName = slot.operation switch
                {
                    ExpandedSliceFixture.Operation.GeneralStore => "Baldwin-Ooms General Store",
                    ExpandedSliceFixture.Operation.Bakery => "Pierce Bakery",
                    ExpandedSliceFixture.Operation.FuelDealer => "Boniecki Fuel Dealer",
                    ExpandedSliceFixture.Operation.FreightCompany => "Boniecki Freight Company",
                    _ => slot.key
                };
                roster.Add(new OpeningRosterEntry(type, displayName, ownerName,
                    premises, slot.openingCashResolved ? slot.openingCashCents : 0, ownerId));
            }
            return roster;
        }
#endif

        /// <summary>
        /// Binds authored boarding-house Businesses to the detailed room and
        /// boarder runtime during an ordinary New Game. Previously the
        /// SimulationDrivers adapter existed, but no production bootstrap
        /// created or registered a runtime, leaving housing search with no
        /// real boarding inventory. Capacity comes from the existing generic
        /// Business authority; this method never grants beds beyond that
        /// authored capacity and never creates a second cash authority.
        /// </summary>
        private void RegisterLiveBoardingHouseRuntimes(SimulationDrivers drivers)
        {
            if (drivers == null || systemsHub == null || sharedBusinessRuntime == null)
            {
                return;
            }

            foreach (BusinessInstanceState business in sharedBusinessRuntime.Businesses)
            {
                if (business == null || business.BusinessType != BusinessType.BoardingHouse
                    || string.IsNullOrWhiteSpace(business.InstanceId))
                {
                    continue;
                }

                bool alreadyRegistered = false;
                foreach (BoardingHouseShopRuntime existing in systemsHub.BoardingHouseRuntimes)
                {
                    if (existing != null && string.Equals(existing.BusinessInstanceId,
                        business.InstanceId, System.StringComparison.Ordinal))
                    {
                        alreadyRegistered = true;
                        break;
                    }
                }

                if (alreadyRegistered)
                {
                    continue;
                }

                int bedCapacity = sharedBusinessRuntime.GetBoardingHouseBedCapacity(business);
                if (bedCapacity <= 0)
                {
                    Debug.LogWarning($"[WorldStartup] Boarding House '{business.InstanceId}' has no authored bed capacity; no rooms registered.", this);
                    continue;
                }

                var runtime = new BoardingHouseShopRuntime(business.InstanceId, systemsHub.Ids);
                string refusal = runtime.AddRoom(BoardingRoomType.SharedBed, bedCapacity, null);
                if (!string.IsNullOrWhiteSpace(refusal))
                {
                    Debug.LogWarning($"[WorldStartup] Boarding House '{business.InstanceId}' room registration refused: {refusal}", this);
                    continue;
                }

                drivers.RegisterBoardingHouseRuntime(runtime);
                Debug.Log($"[WorldStartup] Registered boarding-house runtime '{business.InstanceId}' with {bedCapacity} authored bed(s).", this);
            }
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

            // The Main Scene has a ScenarioDirector and scenario objective
            // surface. Retire the legacy General Store tutorial strip so it
            // cannot invent a second, potentially impossible objective.
            firstSessionGuidance.SetGuidanceEnabled(false);
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
                civicFoundation = CivicFoundationManager.FindOrCreate();
            }
        }
    }
}
