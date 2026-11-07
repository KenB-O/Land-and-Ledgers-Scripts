using System;
using System.Collections.Generic;
using LandLedgers.Civic;
using System.Text;
using LandLedgers.CameraSystem;
using LandLedgers.Economy;
using LandLedgers.Economy.Core;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Businesses.BoardingHouse;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.BreadDrivers;
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
using UnityEngine.Serialization;
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
        [SerializeField, FormerlySerializedAs("enableExpandedSliceDevelopmentFixture"), Tooltip("Opt-in New Game development town configuration. Ordinary scenarios and Continue/restore are unchanged.")]
        private bool enableDevelopmentTownConfiguration;

        [SerializeField, Tooltip("Single active development-town configuration. If empty, the Editor resolves Assets/Core/Population/DevelopmentTownConfiguration.asset.")]
        private DevelopmentTownConfiguration developmentTownConfiguration;
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
        [NonSerialized] private Review2VerticalD3SaveState review2D3SaveState = new Review2VerticalD3SaveState();
        [NonSerialized] private BakeryShopRuntime liveBakeryRuntime;

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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                // Development simulation switches are authored in the active
                // town configuration rather than the population save payload.
                // Reapply and attach that same configuration before returning
                // from Continue so immortality debuff suppression, mortality,
                // and fertility stay consistent on the very first resumed day.
                if (enableDevelopmentTownConfiguration)
                {
                    DevelopmentTownConfiguration continuedTownConfiguration = ResolveDevelopmentTownConfiguration();
                    if (continuedTownConfiguration != null)
                    {
                        populationManager?.ApplyDevelopmentPopulationSettings(continuedTownConfiguration);
                        FindAnyObjectByType<SimulationDrivers>()?.AttachDevelopmentTownConfiguration(continuedTownConfiguration);
                    }
                }
#endif
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
            DevelopmentTownConfiguration townConfiguration = ResolveDevelopmentTownConfiguration();
            bool useDevelopmentTownProfile = startupIntent == WorldStartupIntent.NewGame
                && enableDevelopmentTownConfiguration && townConfiguration != null;
            if (useDevelopmentTownProfile && !townConfiguration.Validate(out string configError))
            {
                bootstrapStatus = "Development town startup stopped: " + configError;
                Debug.LogError("[WorldStartup] " + bootstrapStatus, this);
                CompleteStartupTiming(totalTimer, timing, "development configuration invalid");
                initializing = false;
                return;
            }
            if (useDevelopmentTownProfile && generateTownOnPlay && townWorld.Grid == null)
            {
                if (!townWorld.TryApplyDevelopmentResidentialHomeCount(
                    townConfiguration.Households.Count, out string developmentWorldReason, townConfiguration.Businesses.Count))
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
            if (useDevelopmentTownProfile)
            {
                int requiredResidentialHomes = townConfiguration.Households.Count;
                int requiredCommercialBuildings = townConfiguration.Businesses.Count;
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
                if (startupIntent == WorldStartupIntent.NewGame && useDevelopmentTownProfile)
                {
                    systemsHub ??= FindAnyObjectByType<SimulationSystemsHub>();
                    populationManager.HouseholdLedgers ??= systemsHub != null ? systemsHub.HouseholdLedgers : null;
                    populationManager.GeneratePopulationSnapshot(townConfiguration);
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

            // Plan 01 Slice 1a (P0-1): rebind after generation so
            // SimulationDrivers re-reads populationManager.Memberships from
            // the post-generation registry instead of keeping any
            // pre-generation binding made by the Rebind before generation.
            // The registry is now a single same-instance object, so this
            // rebind is belt-and-braces for the whole A family.
            if (generatePopulationOnPlay && populationManager != null)
            {
                FindAnyObjectByType<SimulationDrivers>()?.Rebind();
            }

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
            ConfigureLiveBakerySaveOwner();
            SimulationDrivers simulationDrivers = FindAnyObjectByType<SimulationDrivers>();
            simulationDrivers?.AttachBusinessRuntime(sharedBusinessRuntime);
            simulationDrivers?.AttachDevelopmentTownConfiguration(ResolveDevelopmentTownConfiguration());
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ApplyDevelopmentTownOpeningCash();
#endif
            ConfigureMillbrookAidTable(simulationDrivers);
            sharedBusinessRuntime?.ReconcileGeneratedWorkplaceAssignments();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            BusinessInstanceState developmentNpcStore = null;
            if (enableDevelopmentTownConfiguration && ResolveDevelopmentTownConfiguration() != null)
            {
                developmentNpcStore = FindDevelopmentNpcGeneralStore();
                if (startupIntent == WorldStartupIntent.NewGame)
                    SeedDevelopmentBusinessOpeningInventory();
                if (startupIntent == WorldStartupIntent.NewGame)
                    SeedDevelopmentBakeryOpeningFuel();
                if (startupIntent == WorldStartupIntent.NewGame)
                    SeedDevelopmentBusinessOpeningEquipment();
                if (startupIntent == WorldStartupIntent.NewGame)
                    SeedBakeryWholesaleAccounts();
                if (startupIntent == WorldStartupIntent.NewGame && simulationDrivers != null && developmentNpcStore?.RuntimeState != null)
                    ConfigurePlaytestHorseFeed(developmentNpcStore, simulationDrivers);
            }
#endif
            // Register the live store with the shared supplier directory. The
            // household membership binding remains an explicit scenario/world
            // concern; registering the seller alone cannot execute a purchase.
            if (simulationDrivers != null && storeRuntime != null)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                simulationDrivers.RegisterGeneralStoreSupplier(developmentNpcStore != null
                    ? storeRuntime.BuildHouseholdTradingPort(developmentNpcStore, simulationDrivers.StoreFeedRegistry)
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (enableDevelopmentTownConfiguration && ResolveDevelopmentTownConfiguration() != null)
            {
                OpeningTransportPresentationController transportPresentation =
                    GetComponent<OpeningTransportPresentationController>()
                    ?? gameObject.AddComponent<OpeningTransportPresentationController>();
                transportPresentation.Present(
                    systemsHub,
                    sharedBusinessRuntime != null ? sharedBusinessRuntime.Businesses : null,
                    townWorld,
                    populationManager != null ? populationManager.State : null);
            }
#endif
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
            // Slice S2: re-attach the Pass 9 equipment registers restored
            // from the save into the hub's per-business dictionary, so the
            // task gate and the bakery drive read the restored kits/assets
            // on Continue. The save data is authoritative; re-registering
            // replaces by business id, never merges, and is idempotent.
            systemsHub ??= FindAnyObjectByType<SimulationSystemsHub>();
            if (systemsHub != null && saveLoadManager?.Series2Pass9State?.EquipmentRegisters != null)
                foreach (BusinessEquipmentRegister restored in saveLoadManager.Series2Pass9State.EquipmentRegisters)
                    systemsHub.RegisterBusinessEquipment(restored);
            // Slice S9: restore the Pass 9 off-map journeys into the live
            // authority after a load. Null-safe end to end: a null DTO
            // restores empty (old saves carry no section).
            SimulationDrivers drivers = FindAnyObjectByType<SimulationDrivers>();
            drivers?.RestoreOffMapJourneys(saveLoadManager?.Series2Pass9State?.OffMapJourneys);
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
                // Plan 01 Slice 1a (P0-1): PopulationManager is the
                // household-membership authority; a duplicate in the scene
                // is the leading hypothesis for the 285-day memberless
                // symptom (generation on one instance, daily drivers
                // reading the other), so it must be audited like the rest.
                BuildAuthorityCensus<PopulationManager>("PopulationManager"),
                // Plan 03 Slice 0 (P0-2): TownWorldController is the
                // journey-location authority the drivers resolve by type;
                // a duplicate in the scene is the leading hypothesis for
                // the 285-day empty journey model (generation on one
                // instance, daily drivers reading an ungenerated other),
                // so it must be audited like the rest.
                BuildAuthorityCensus<TownWorldController>("TownWorldController"),
                BuildAuthorityCensus<CivicFoundationManager>("CivicFoundation"),
                BuildAuthorityCensus<ScenarioDirector>("ScenarioDirector"),
                BuildAuthorityCensus<FirstLedgerSliceBootstrapper>("FirstLedgerSliceBootstrapper"),
            };
            foreach (string error in BootstrapAuthorityAudit.DescribeDuplicates(censuses))
            {
                Debug.LogError($"[WorldStartup] {error}", this);
            }
            // Plan 03 Slice 0 (P0-2): name the town-world instance the
            // bootstrapper holds (GameObject name + instance id + Grid
            // state) at startup, so Codex can compare it with the drivers'
            // held instance logged by SimulationDrivers and tell a
            // duplicate/wrong instance from a roadless grid.
            if (townWorld != null)
            {
                Debug.Log($"[WorldStartup] TownWorldController instance (bootstrapper): '{townWorld.gameObject.name}' " +
                    $"(instance {townWorld.GetInstanceID()}), GenerationPassCount={townWorld.GenerationPassCount}, " +
                    $"Grid={(townWorld.Grid != null ? "present" : "null")}.", this);
            }
            else
            {
                Debug.Log("[WorldStartup] TownWorldController instance (bootstrapper): none held " +
                    "(no serialized instance; FindAnyObjectByType found none).", this);
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
            Debug.LogError("[WorldStartup] Development opening-stock endowment was not applied: configured NPC General Store owner was not found.", this);
            return null;
        }

        private void SeedDevelopmentBusinessOpeningInventory()
        {
            DevelopmentTownConfiguration configuration = ResolveDevelopmentTownConfiguration();
            if (enableDevelopmentTownConfiguration && configuration != null)
            {
                int developmentSeed = townWorld?.Settings != null ? townWorld.Settings.seed : 0;
                foreach (DevelopmentTownConfiguration.BusinessOpeningInventory entry in configuration.OpeningInventory)
                {
                    if (entry == null || !configuration.TryGetBusiness(entry.businessKey, out DevelopmentTownConfiguration.BusinessDefinition slot))
                    {
                        Debug.LogError("[WorldStartup] Development opening inventory has an unresolved business key.", this);
                        return;
                    }
                    BusinessType type = ToBusinessType(slot.operationId);
                    BusinessInstanceState business = FindDevelopmentBusiness(type);
                    if (business?.RuntimeState == null)
                    {
                        Debug.LogError($"[WorldStartup] Opening inventory for '{entry.businessKey}' refused: the configured business runtime does not exist.", this);
                        return;
                    }
                    if (!ScenarioOpeningStockEndowment.TryApply(configuration, entry.businessKey, business,
                            storeRuntime?.StoreDefinition, developmentSeed,
                            timeManager != null ? timeManager.CurrentAbsoluteDayIndex : 0,
                            out string inventoryReport, out string inventoryError))
                    {
                        Debug.LogError($"[WorldStartup] {inventoryError}", this);
                        return;
                    }
                    Debug.Log($"[WorldStartup] {inventoryReport}", this);
                }
                return;
            }

            // No fixture-specific inventory fallback: all configured opening stock
            // must pass through the generic DevelopmentTownConfiguration entries.
        }

        /// <summary>
        /// Applies the finite Bakery fuel endowment explicitly authored by
        /// the development-town configuration. Continue restores the saved
        /// Bakery fuel lots and never seeds this NewGame-only allocation.
        /// </summary>
        private void SeedDevelopmentBakeryOpeningFuel()
        {
            DevelopmentTownConfiguration configuration = ResolveDevelopmentTownConfiguration();
            if (!enableDevelopmentTownConfiguration || configuration == null || configuration.OpeningBakeryFuelUnits <= 0)
                return;

            string bakeryKey = null;
            foreach (DevelopmentTownConfiguration.BusinessDefinition slot in configuration.Businesses)
                if (slot != null && ToBusinessType(slot.operationId) == BusinessType.Bakery)
                {
                    bakeryKey = slot.key;
                    break;
                }
            if (string.IsNullOrWhiteSpace(bakeryKey))
            {
                Debug.LogError("[WorldStartup] Opening Bakery fuel refused: no Bakery business is configured.", this);
                return;
            }

            BusinessInstanceState bakeryBusiness = FindDevelopmentBusiness(BusinessType.Bakery);
            Series2Pass9SaveState pass9 = saveLoadManager?.Series2Pass9State;
            systemsHub ??= FindAnyObjectByType<SimulationSystemsHub>();
            if (bakeryBusiness?.RuntimeState == null || pass9?.BakeryRuntime == null || systemsHub == null)
            {
                Debug.LogError($"[WorldStartup] Opening Bakery fuel for '{bakeryKey}' refused: business runtime, Pass 9 Bakery runtime, or lot-id authority is missing.", this);
                return;
            }

            var diagnostics = new List<string>();
            if (!LandLedgers.Economy.Businesses.Bakery.ScenarioOpeningBakeryFuelEndowment.TryApply(
                    pass9.BakeryRuntime, bakeryBusiness.RuntimeState, systemsHub.Ids,
                    configuration.ConfigurationId, bakeryKey,
                    townWorld?.Settings != null ? townWorld.Settings.seed : configuration.SeedOffset,
                    configuration.OpeningBakeryFuelUnits,
                    timeManager != null ? timeManager.CurrentAbsoluteDayIndex : 0,
                    diagnostics, out string report, out string error))
            {
                Debug.LogError($"[WorldStartup] {error}", this);
                return;
            }

            Debug.Log($"[WorldStartup] {report}", this);
            foreach (string line in diagnostics) Debug.Log($"[WorldStartup] {line}", this);
        }

        /// <summary>
        /// Slice S2 (2026-11-05, plan 02 §4): NewGame-only seeding of
        /// scenario-owned opening equipment for the Bakery, beside the
        /// opening-inventory seeding above. Finds the Bakery the same
        /// way ConfigureLiveBakerySaveOwner does, gets/creates its
        /// hub-owned equipment register, and applies exactly what the
        /// configuration lists (zero by default). The register is kept on
        /// the Pass 9 state so the save seam persists it; on Continue the
        /// registers come back from the save via MarkRestoredFromSave,
        /// never from a second seeding. Refusals are Debug.LogError, the
        /// same posture as the inventory seeding.
        /// </summary>
        private void SeedDevelopmentBusinessOpeningEquipment()
        {
            DevelopmentTownConfiguration configuration = ResolveDevelopmentTownConfiguration();
            if (!enableDevelopmentTownConfiguration || configuration == null) return;
            string bakeryKey = null;
            foreach (DevelopmentTownConfiguration.BusinessDefinition slot in configuration.Businesses)
                if (slot != null && ToBusinessType(slot.operationId) == BusinessType.Bakery)
                {
                    bakeryKey = slot.key;
                    break;
                }
            if (string.IsNullOrWhiteSpace(bakeryKey))
            {
                Debug.LogError("[WorldStartup] Opening equipment for the bakery refused: no bakery business is configured.", this);
                return;
            }
            BusinessInstanceState bakeryBusiness = FindDevelopmentBusiness(BusinessType.Bakery);
            if (bakeryBusiness?.RuntimeState == null)
            {
                Debug.LogError($"[WorldStartup] Opening equipment for '{bakeryKey}' refused: the configured bakery runtime does not exist.", this);
                return;
            }
            Series2Pass9SaveState pass9 = saveLoadManager?.Series2Pass9State;
            if (pass9?.BakeryRuntime == null)
            {
                Debug.LogError($"[WorldStartup] Opening equipment for '{bakeryKey}' refused: no live bakery runtime is registered on the Pass 9 state.", this);
                return;
            }
            systemsHub ??= FindAnyObjectByType<SimulationSystemsHub>();
            if (systemsHub == null)
            {
                Debug.LogError($"[WorldStartup] Opening equipment for '{bakeryKey}' refused: no SimulationSystemsHub is present.", this);
                return;
            }
            BusinessEquipmentRegister register = systemsHub.EquipmentRegisterFor(bakeryBusiness.InstanceId);
            var diag = new List<string>();
            if (!ScenarioOpeningEquipmentEndowment.TryApply(configuration, bakeryKey, pass9.BakeryRuntime,
                    bakeryBusiness.RuntimeState, register,
                    timeManager != null ? timeManager.CurrentAbsoluteDayIndex : 0,
                    diag, out string equipmentReport, out string equipmentError))
            {
                Debug.LogError($"[WorldStartup] {equipmentError}", this);
                return;
            }
            if (!pass9.EquipmentRegisters.Exists(existing => existing != null
                    && string.Equals(existing.BusinessInstanceId, bakeryBusiness.InstanceId, StringComparison.Ordinal)))
                pass9.EquipmentRegisters.Add(register);
            Debug.Log($"[WorldStartup] {equipmentReport}", this);
            foreach (string line in diag) Debug.Log($"[WorldStartup] {line}", this);
        }

        /// <summary>
        /// Plan 01 (2026-11-06): NewGame-only booking of the scenario-owned
        /// bakery wholesale accounts, beside the opening-equipment seeding
        /// above. Resolves the bakery key exactly like the equipment
        /// seeder, finds the bakery and general-store businesses via the
        /// existing FindDevelopmentBusiness, and books exactly what the
        /// configuration lists (zero by default; the applicator books the
        /// demand-derived fallback only when the config lists no wholesale
        /// accounts at all). On Continue the accounts persist in the save
        /// DTO and are never re-booked. Refusals are Debug.LogError, the
        /// same posture as the equipment seeding.
        /// </summary>
        private void SeedBakeryWholesaleAccounts()
        {
            DevelopmentTownConfiguration configuration = ResolveDevelopmentTownConfiguration();
            if (!enableDevelopmentTownConfiguration || configuration == null) return;
            string bakeryKey = null;
            foreach (DevelopmentTownConfiguration.BusinessDefinition slot in configuration.Businesses)
                if (slot != null && ToBusinessType(slot.operationId) == BusinessType.Bakery)
                {
                    bakeryKey = slot.key;
                    break;
                }
            if (string.IsNullOrWhiteSpace(bakeryKey))
            {
                Debug.LogError("[WorldStartup] Wholesale accounts for the bakery refused: no bakery business is configured.", this);
                return;
            }
            BusinessInstanceState bakeryBusiness = FindDevelopmentBusiness(BusinessType.Bakery);
            if (bakeryBusiness?.RuntimeState == null)
            {
                Debug.LogError($"[WorldStartup] Wholesale accounts for '{bakeryKey}' refused: the configured bakery runtime does not exist.", this);
                return;
            }
            BusinessInstanceState storeBusiness = FindDevelopmentBusiness(BusinessType.GeneralStore);
            if (storeBusiness?.RuntimeState == null)
            {
                Debug.LogError($"[WorldStartup] Wholesale accounts for '{bakeryKey}' refused: the configured general-store runtime does not exist.", this);
                return;
            }
            Series2Pass9SaveState pass9 = saveLoadManager?.Series2Pass9State;
            if (pass9?.BakeryRuntime == null)
            {
                Debug.LogError($"[WorldStartup] Wholesale accounts for '{bakeryKey}' refused: no live bakery runtime is registered on the Pass 9 state.", this);
                return;
            }
            var diag = new List<string>();
            if (!ScenarioOpeningWholesaleAccounts.TryApply(configuration, bakeryKey, pass9.BakeryRuntime,
                    bakeryBusiness.RuntimeState, storeBusiness,
                    timeManager != null ? timeManager.CurrentAbsoluteDayIndex : 0,
                    diag, out string wholesaleReport, out string wholesaleError))
            {
                Debug.LogError($"[WorldStartup] {wholesaleError}", this);
                return;
            }
            Debug.Log($"[WorldStartup] {wholesaleReport}", this);
            foreach (string line in diag) Debug.Log($"[WorldStartup] {line}", this);
        }

        private void ConfigureOpeningBusinessRoster()
        {
            sharedBusinessRuntime ??= FindAnyObjectByType<SharedBusinessRuntimeManager>();
            System.Collections.Generic.IReadOnlyList<OpeningRosterEntry> roster =
                useCompactPlaytestOpeningRoster ? OpeningBusinessRoster.CompactPlaytest() : null;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (startupIntent == WorldStartupIntent.NewGame && enableDevelopmentTownConfiguration)
            {
                DevelopmentTownConfiguration configuration = ResolveDevelopmentTownConfiguration();
                roster = configuration != null ? BuildDevelopmentOpeningRoster(configuration) : null;
            }
#endif
            sharedBusinessRuntime?.ConfigureOpeningBusinessRoster(roster);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private DevelopmentTownConfiguration ResolveDevelopmentTownConfiguration()
        {
            if (developmentTownConfiguration != null) return developmentTownConfiguration;
#if UNITY_EDITOR
            developmentTownConfiguration = UnityEditor.AssetDatabase.LoadAssetAtPath<DevelopmentTownConfiguration>(
                "Assets/Core/Population/DevelopmentTownConfiguration.asset");
#endif
            if (enableDevelopmentTownConfiguration && developmentTownConfiguration == null)
                Debug.LogError("[WorldStartup] Development town configuration is enabled but Assets/Core/Population/DevelopmentTownConfiguration.asset could not be loaded in the Editor.", this);
            if (enableDevelopmentTownConfiguration && developmentTownConfiguration != null
                && !developmentTownConfiguration.Validate(out string validationError))
                Debug.LogError($"[WorldStartup] Development town configuration is invalid: {validationError}", this);
            return developmentTownConfiguration;
        }

        private static BusinessType ToBusinessType(string operationId) => operationId?.Trim().ToLowerInvariant() switch
        {
            "general-store" => BusinessType.GeneralStore,
            "bakery" => BusinessType.Bakery,
            "fuel-dealer" => BusinessType.FuelDealer,
            "livery-freight" or "freight-company" => BusinessType.LiveryFreight,
            _ => throw new ArgumentException($"Unsupported configured business operation ID '{operationId}'.", nameof(operationId))
        };

        private BusinessInstanceState FindDevelopmentBusiness(BusinessType type)
        {
            if (sharedBusinessRuntime?.Businesses == null) return null;
            foreach (BusinessInstanceState business in sharedBusinessRuntime.Businesses)
                if (business != null && business.BusinessType == type && business.Owner?.OwnerKind == BusinessOwnerKind.Npc)
                    return business;
            return null;
        }

        private static List<OpeningRosterEntry> BuildDevelopmentOpeningRoster(DevelopmentTownConfiguration configuration)
        {
            var roster = new List<OpeningRosterEntry>();
            if (configuration == null) return roster;
            foreach (DevelopmentTownConfiguration.BusinessDefinition slot in configuration.Businesses)
            {
                BusinessType type = ToBusinessType(slot.operationId);
                int ownerId = configuration.ResolveRuntimePersonId(slot.ownerPersonKey);
                configuration.TryGetPerson(slot.ownerPersonKey, out DevelopmentTownConfiguration.PersonDefinition owner);
                string ownerName = owner != null ? owner.firstName + " " + owner.lastName : "Development configuration";
                var premises = type switch
                {
                    BusinessType.GeneralStore => new PremisesRequirement(customerFacing: true, minAreaSqFt: 800),
                    BusinessType.Bakery => new PremisesRequirement(customerFacing: true, foodHandling: true, minAreaSqFt: 500),
                    BusinessType.FuelDealer => new PremisesRequirement(yardStorage: true, minAreaSqFt: 600),
                    _ => new PremisesRequirement()
                };
                string displayName = slot.operationId switch
                {
                    "general-store" => ownerName + " General Store",
                    "bakery" => ownerName + " Bakery",
                    "fuel-dealer" => ownerName + " Fuel Dealer",
                    "livery-freight" or "freight-company" => ownerName + " Freight Company",
                    _ => slot.key
                };
                roster.Add(new OpeningRosterEntry(type, displayName, ownerName,
                    premises, slot.openingCashCents, ownerId));
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
            GetComponent<OpeningTransportPresentationController>()
                ?.BindPopulationPathingDirector(populationPathingDirector);
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

            ConfigureReview2D3SaveWiring();

            if (civicFoundation == null)
            {
                civicFoundation = CivicFoundationManager.FindOrCreate();
            }
        }

        private void ConfigureReview2D3SaveWiring()
        {
            if (saveLoadManager == null)
            {
                return;
            }

            review2D3SaveState ??= new Review2VerticalD3SaveState();
            saveLoadManager.Review2VerticalD3SaveState = review2D3SaveState;

            SimulationDrivers drivers = FindAnyObjectByType<SimulationDrivers>();
            if (drivers == null)
            {
                Debug.LogWarning("Review 2 D3 save binding deferred: the authored SimulationDrivers component is missing. Feed registrations will not be saved until a driver exists.", this);
                return;
            }

            drivers.AttachReview2D3SaveState(review2D3SaveState);

            List<string> diagnostics = new List<string>();
            string refusal = Review2SecondScriptsSaveWiring.BindDriverRegistry(
                review2D3SaveState,
                drivers.StoreFeedRegistry,
                diagnostics);
            if (!string.IsNullOrWhiteSpace(refusal))
            {
                Debug.LogWarning(refusal, this);
            }
        }

        private void ConfigureLiveBakerySaveOwner()
        {
            if (sharedBusinessRuntime == null || saveLoadManager == null)
            {
                Debug.LogWarning("[BakeryRuntime] Live owner was not created: shared business runtime or SaveLoadManager is missing.", this);
                return;
            }

            BusinessInstanceState bakeryBusiness = null;
            foreach (BusinessInstanceState candidate in sharedBusinessRuntime.Businesses)
            {
                if (candidate == null || candidate.BusinessType != BusinessType.Bakery) continue;
                bakeryBusiness = candidate;
                break;
            }

            if (bakeryBusiness == null || string.IsNullOrWhiteSpace(bakeryBusiness.InstanceId))
            {
                Debug.LogWarning("[BakeryRuntime] Live owner was not created: no authored Bakery business instance is present in the shared runtime.", this);
                return;
            }

            Series2Pass9SaveState pass9 = saveLoadManager.Series2Pass9State;
            if (pass9 == null)
            {
                pass9 = new Series2Pass9SaveState();
                saveLoadManager.Series2Pass9State = pass9;
            }

            liveBakeryRuntime = pass9.BakeryRuntime ?? pass9.BakeryDriver?.Bakery;
            if (liveBakeryRuntime == null)
            {
                liveBakeryRuntime = new BakeryShopRuntime(bakeryBusiness.InstanceId);
            }
            else if (!string.Equals(liveBakeryRuntime.BusinessInstanceId, bakeryBusiness.InstanceId, StringComparison.Ordinal))
            {
                Debug.LogError($"[BakeryRuntime] Refusing a second Bakery runtime: save owner is '{liveBakeryRuntime.BusinessInstanceId}', authored Bakery is '{bakeryBusiness.InstanceId}'.", this);
                return;
            }

            pass9.BakeryRuntime = liveBakeryRuntime;
            // W6 (W-5, Obj 9 design): bind the runtime to its owner person
            // (Glen) — the seam for baker person-time booking. No behaviour
            // change: nothing reads OwnerPersonId yet, and a refused bind
            // only warns (the runtime stays unbound until the owner is
            // bound). Bound here for both the fresh and the restored
            // runtime: the link is re-bound at bootstrap, not saved.
            string bakeryOwnerBindRefusal = liveBakeryRuntime.BindOwnerPerson(bakeryBusiness.Owner, null);
            if (bakeryOwnerBindRefusal != null)
                Debug.LogWarning($"[BakeryRuntime] {bakeryOwnerBindRefusal}", this);
            LandLedgers.Economy.BreadDrivers.ISeries2BakeryFuelSource liveFuelSource = pass9.FuelSource
                ?? (LandLedgers.Economy.BreadDrivers.ISeries2BakeryFuelSource)
                    new LandLedgers.Economy.BreadDrivers.Series2InStockBakeryFuelSource(liveBakeryRuntime);
            if (pass9.BakeryDriver == null)
            {
                // Verify only provenance-tracked fuel already in the bakery.
                // Dealer supply is attached only when its real intake authority
                // exists; this fallback never creates fuel.
                pass9.BakeryDriver = new Series2BakeryProductionDriver(liveBakeryRuntime, liveFuelSource);
            }
            else pass9.BakeryDriver.AttachFuelSource(liveFuelSource);
            // Detailed freight state is owned by the same Pass 9 save section
            // before any daily driver can create or restore a shipment.
            // SimulationDrivers reuses this instance; it must not keep a
            // transient parallel FreightProductionDriver.
            if (pass9.FreightDriver == null)
            {
                pass9.FreightDriver = new LandLedgers.Economy.Freight.FreightProductionDriver();
                Debug.Log("[FreightSave] Pass 9 save owner created the live freight driver before simulation startup.", this);
            }
            Debug.Log($"[BakeryRuntime] Bootstrap owns one live BakeryShopRuntime for '{bakeryBusiness.RuntimeDisplayName}' ({bakeryBusiness.InstanceId}); its Pass 9 state is registered with SaveLoadManager.", this);
        }

        private void ConfigureMillbrookAidTable(SimulationDrivers drivers)
        {
            const string resourcePath = "Core/Economy/OffMapAidBusinesses/MillbrookGrainCo";
            OffMapAidBusinessDefinition definition = Resources.Load<OffMapAidBusinessDefinition>(resourcePath);
            if (definition == null)
            {
                Debug.LogError($"[OffMapAid] Millbrook definition was not found at Resources/{resourcePath}.asset.", this);
                return;
            }

            string validation = definition.ValidateDefinition();
            if (validation != null)
            {
                Debug.LogError($"[OffMapAid] Millbrook definition is invalid: {validation}", this);
                return;
            }

            OffMapAidBusinessRuntime runtime = saveLoadManager != null ? saveLoadManager.MillbrookAidRuntime : null;
            if (runtime == null || !string.Equals(runtime.BusinessId, definition.BusinessId, StringComparison.Ordinal))
                runtime = new OffMapAidBusinessRuntime(definition);
            if (saveLoadManager != null) saveLoadManager.MillbrookAidRuntime = runtime;
            if (drivers != null) drivers.AttachMillbrookAidRuntime(runtime);
            // Plan 04 Slice 0 (P0-3): name the drivers instance that
            // received the attach (GameObject name + instance id + whether
            // it holds the SimulationDrivers authority claim), so Codex can
            // compare it with the driving instance logged by
            // DriveMillbrookFreightNegotiation and tell a wrong-instance
            // attach (A1) from a replaced driving instance (A2). The claim
            // is read from the guard, not from the drivers, so this log
            // stays honest even when the attach landed on the idle instance.
            // Log-only; no behavior change.
            string attachIdentity = drivers == null
                ? "no SimulationDrivers instance received the attach (drivers null)"
                : $"attached to drivers instance '{drivers.gameObject.name}' (instance {drivers.GetInstanceID()}, " +
                  $"authority claim held={SingleInstanceAuthorityGuard.IsClaimedBy("SimulationDrivers", drivers)})";
            Debug.Log($"[OffMapAid] Loaded {definition.DisplayName} ({definition.BusinessId}) from Resources: " +
                $"flour={runtime.GetInventoryUnits("bakery_flour_sack")} lb, wheat={runtime.GetInventoryUnits("wheat_grain")} lb, " +
                $"sugar={runtime.GetInventoryUnits("sugar")} lb, yeast={runtime.GetInventoryUnits("yeast")} lb; " +
                "off-map table runtime is attached to the daily driver and save authority; " + attachIdentity + ".", this);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void ApplyDevelopmentTownOpeningCash()
        {
            DevelopmentTownConfiguration configuration = ResolveDevelopmentTownConfiguration();
            if (startupIntent != WorldStartupIntent.NewGame || !enableDevelopmentTownConfiguration || configuration == null)
                return;

            BusinessInstanceState markerBusiness = null;
            var businessesByKey = new Dictionary<string, BusinessInstanceState>(StringComparer.OrdinalIgnoreCase);
            if (sharedBusinessRuntime?.Businesses != null)
            {
                foreach (BusinessInstanceState candidate in sharedBusinessRuntime.Businesses)
                {
                    if (candidate?.RuntimeState == null) continue;
                    foreach (DevelopmentTownConfiguration.BusinessCashTarget target in configuration.BusinessCashTargets)
                    {
                        if (!configuration.TryGetBusiness(target.businessKey, out DevelopmentTownConfiguration.BusinessDefinition slot)) continue;
                        int ownerId = configuration.ResolveRuntimePersonId(slot.ownerPersonKey);
                        if (candidate.BusinessType == ToBusinessType(slot.operationId)
                            && candidate.Owner?.PersonId == ownerId && !businessesByKey.ContainsKey(target.businessKey))
                            businessesByKey.Add(target.businessKey, candidate);
                    }
                    if (candidate.BusinessType == BusinessType.GeneralStore) markerBusiness = candidate;
                }
            }

            if (markerBusiness?.RuntimeState == null || populationManager?.State == null
                || systemsHub?.HouseholdLedgers == null)
            {
                Debug.LogError("[DevelopmentFinance] Expanded opening allocation refused: business, population, or household ledger authority is missing.", this);
                return;
            }

            const string source = "DevelopmentTownConfiguration opening allocation";
            const string reason = "$200 per business and $200 per NPC resident for development testing liquidity";

            foreach (DevelopmentTownConfiguration.BusinessCashTarget target in configuration.BusinessCashTargets)
            {
                if (!businessesByKey.TryGetValue(target.businessKey, out BusinessInstanceState business))
                {
                    Debug.LogError($"[DevelopmentFinance] Business '{target.businessKey}' allocation refused: its runtime is missing.", this);
                    return;
                }
                string allocationId = "small-prompt-8-development-cash:source=expanded-scenario:reason=opening-liquidity:business=" + business.BusinessType;
                if (!business.RuntimeState.HasAppliedScenarioEndowment(allocationId)
                    && business.RuntimeState.CurrentCashCents > target.targetCashCents)
                {
                    Debug.LogError($"[DevelopmentFinance] {target.businessKey} allocation refused: actual starting balance {business.RuntimeState.CurrentCashCents}c exceeds configured target {target.targetCashCents}c.", this);
                    return;
                }
            }
            foreach (DevelopmentTownConfiguration.HouseholdCashTarget target in configuration.HouseholdCashTargets)
            {
                int personId = configuration.ResolveRuntimePersonId(target.anchorPersonKey);
                PersonState person = populationManager.State.GetPerson(personId);
                if (person == null || person.householdId < 0 || systemsHub.HouseholdLedgers.Get(person.householdId) == null)
                {
                    Debug.LogError($"[DevelopmentFinance] Household anchored by '{target.anchorPersonKey}' allocation refused: resident or existing shared household ledger is missing.", this);
                    return;
                }
                string allocationId = "small-prompt-8-development-cash:source=expanded-scenario:reason=opening-liquidity:household=" + person.householdId;
                HouseholdLedger ledger = systemsHub.HouseholdLedgers.Get(person.householdId);
                if (!markerBusiness.RuntimeState.HasAppliedScenarioEndowment(allocationId)
                    && ledger.GetBalanceCents() > target.targetCashCents)
                {
                    Debug.LogError($"[DevelopmentFinance] {target.anchorPersonKey} allocation refused: actual starting balance {ledger.GetBalanceCents()}c exceeds configured target {target.targetCashCents}c.", this);
                    return;
                }
            }

            foreach (DevelopmentTownConfiguration.BusinessCashTarget target in configuration.BusinessCashTargets)
            {
                BusinessInstanceState business = businessesByKey[target.businessKey];
                string allocationId = "small-prompt-8-development-cash:source=expanded-scenario:reason=opening-liquidity:business=" + business.BusinessType;
                BusinessRuntimeState cash = business.RuntimeState;
                if (cash.HasAppliedScenarioEndowment(allocationId)) continue;
                int before = cash.CurrentCashCents;
                int amount = target.targetCashCents - before;
                if (amount > 0) cash.AddCashCents(amount);
                cash.RecordAppliedScenarioEndowment(allocationId);
                Debug.Log($"[DevelopmentFinance] opening allocation applied: source='{source}', reason='{reason}', recipient='{target.businessKey}' ({business.InstanceId}), amount={amount}c, balance={before}->{cash.CurrentCashCents}c.", this);
            }

            foreach (DevelopmentTownConfiguration.HouseholdCashTarget target in configuration.HouseholdCashTargets)
            {
                PersonState person = populationManager.State.GetPerson(configuration.ResolveRuntimePersonId(target.anchorPersonKey));
                HouseholdLedger ledger = systemsHub.HouseholdLedgers.Get(person.householdId);
                string allocationId = "small-prompt-8-development-cash:source=expanded-scenario:reason=opening-liquidity:household=" + person.householdId;
                if (markerBusiness.RuntimeState.HasAppliedScenarioEndowment(allocationId)) continue;
                int before = ledger.GetBalanceCents();
                int amount = target.targetCashCents - before;
                if (amount > 0)
                {
                    string refusal = ledger.RecordInflow(
                        timeManager != null ? timeManager.CurrentAbsoluteDayIndex : 0,
                        amount,
                        HouseholdIncomeSource.OtherDocumented,
                        allocationId,
                        reason,
                        source);
                    if (!string.IsNullOrWhiteSpace(refusal))
                    {
                        Debug.LogError($"[DevelopmentFinance] {target.anchorPersonKey} allocation refused by household ledger: {refusal}", this);
                        return;
                    }
                }
                markerBusiness.RuntimeState.RecordAppliedScenarioEndowment(allocationId);
                Debug.Log($"[DevelopmentFinance] opening allocation applied: source='{source}', reason='{reason}', recipient='{target.anchorPersonKey}' (H{person.householdId}), amount={amount}c, balance={before}->{ledger.GetBalanceCents()}c.", this);
            }

            foreach (HouseholdState household in populationManager.State.households)
            {
                if (household == null) continue;
                int balance = systemsHub.HouseholdLedgers.Get(household.id)?.GetBalanceCents() ?? 0;
                Debug.Log($"[ScenarioFinance] opening household balance: H{household.id} '{household.householdName ?? "Household"}'={balance}c.", this);
            }
            foreach (BusinessInstanceState business in businessesByKey.Values)
                Debug.Log($"[ScenarioFinance] opening business balance: '{business.RuntimeDisplayName}' ({business.InstanceId})={business.RuntimeState.CurrentCashCents}c.", this);
        }
#endif

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void ConfigurePlaytestHorseFeed(BusinessInstanceState storeBusiness, SimulationDrivers drivers)
        {
            const string itemId = "store_horse_feed_sack";
            const string categoryId = "animal_feed";
            const string supplierId = "millbrook_grain_co";
            var diagnostics = new List<string>();
            string itemRefusal = review2D3SaveState.RegisterRuntimeItem(
                new HouseholdItemDefinition(itemId, "Horse Feed", "sack", categoryId, 1, false), diagnostics);
            if (itemRefusal != null)
            {
                Debug.LogError($"[FeedSupply] household catalog registration refused: {itemRefusal}", this);
                return;
            }

            string registrationRefusal = drivers.RegisterStoreFeedItem(new Review2StoreFeedItem
            {
                BusinessInstanceId = storeBusiness.InstanceId,
                FeedCategoryId = categoryId,
                FeedItemId = itemId,
                DisplayName = "Horse Feed",
                UnitLabel = "sack",
                RetailPriceCents = 15,
                FeedUnitsPerStoreUnitNumerator = 2,
                FeedUnitsPerStoreUnitDenominator = 1,
            }, diagnostics);
            if (registrationRefusal != null)
            {
                Debug.LogError($"[FeedSupply] store feed registration refused: {registrationRefusal}", this);
                return;
            }

            // Temporary Small Prompt 2 playtest registration, not a Canon supplier:
            // the owner's specified 20 sacks at 15c each are a finite external order.
            if (storeBusiness.RuntimeState.HasAppliedScenarioEndowment("small-prompt-2-horse-feed-order"))
            {
                Debug.Log("[FeedSupply] opening feed order already recorded; no replacement order or free stock created.", this);
                return;
            }

            var market = new ExternalMarketAuthority();
            var counterparty = new ExternalCounterparty
            {
                CounterpartyId = supplierId,
                DisplayName = "Millbrook Grain Co.",
                TransitDays = 1,
                InitialBudgetCents = 0,
                SupplyUnits = new Dictionary<string, int> { [itemId] = 20 },
            };
            string marketRefusal = market.RegisterCounterparty(counterparty);
            if (marketRefusal != null)
            {
                Debug.LogError($"[FeedSupply] external supplier registration refused: {marketRefusal}", this);
                return;
            }

            // Plan 04 N-slice (2026-11-06): notions counterparty for the
            // bakery, same market, same finite-external-order shape as the
            // feed precedent above. Terms are read from the catalog's
            // authored registration (BakeryFlourSupply
            // .EnsureBakeryImportables: "Off-map provision wholesaler,
            // via railhead", 250 mi, 14 transit days, 10c/unit) — never
            // hardcoded here. Supply is a finite external order of
            // BakeryNotionsDailySupply.FiniteExternalOrderUnits units —
            // the authored bootstrap endowment size, NOT canon; when it
            // is exhausted the driver refuses loudly and a standing
            // wholesale supply term is owed (D-1-adjacent). No freight
            // surcharge: no freight rate is authored for the railhead
            // route (Kennedy must approve a production freight rate
            // before one is wired). No order is placed here — the daily
            // driver orders the day's shortfall, so the pipeline primes
            // from the first planned bake.
            var notionsImportDiag = new List<string>();
            LandLedgers.Economy.Businesses.Bakery.BakeryFlourSupply.EnsureBakeryImportables(notionsImportDiag);
            LandLedgers.Economy.Trade.ImportMaterial notionsMaterial =
                LandLedgers.Economy.Trade.ImportCatalog.Get(
                    LandLedgers.Economy.Businesses.Bakery.BakeryFlourSupply.NotionsMaterialId);
            if (notionsMaterial == null)
            {
                // Notions failure must not kill the feed order below:
                // log it and continue without a notions counterparty —
                // the daily driver will name the missing counterparty
                // loudly when it tries to order.
                Debug.LogError("[NotionsSupply] notions import material is not registered; no notions counterparty registered.", this);
            }
            else
            {
                var notionsCounterparty = new ExternalCounterparty
                {
                    CounterpartyId = LandLedgers.Economy.BreadDrivers.BakeryNotionsDailySupply.NotionsSupplierId,
                    DisplayName = LandLedgers.Economy.BreadDrivers.BakeryNotionsDailySupply.NotionsSupplierName,
                    TransitDays = notionsMaterial.TransitDays,
                    InitialBudgetCents = 0,
                    SupplyUnits = new Dictionary<string, int>
                    {
                        [LandLedgers.Economy.Businesses.Bakery.BakeryFlourSupply.NotionsMaterialId] =
                            LandLedgers.Economy.BreadDrivers.BakeryNotionsDailySupply.FiniteExternalOrderUnits,
                    },
                };
                string notionsRefusal = market.RegisterCounterparty(notionsCounterparty);
                if (notionsRefusal != null)
                {
                    Debug.LogError($"[NotionsSupply] external supplier registration refused: {notionsRefusal}", this);
                }
                else
                {
                    Debug.Log($"[NotionsSupply] '{notionsCounterparty.DisplayName}' registered for SKU " +
                        $"'{LandLedgers.Economy.Businesses.Bakery.BakeryFlourSupply.NotionsMaterialId}' at {notionsMaterial.PricePerUnitCents}c/unit, " +
                        $"transit {notionsCounterparty.TransitDays} days, finite supply " +
                        $"{LandLedgers.Economy.BreadDrivers.BakeryNotionsDailySupply.FiniteExternalOrderUnits}u; " +
                        "the daily driver orders the day's shortfall (BakeryNotionsDailySupply).", this);
                }
            }

            var procurement = new OffMapNamedSkuProcurementService(
                storeBusiness.InstanceId, market,
                new Series2BusinessRuntimeCashPort(storeBusiness.RuntimeState));
            int dayIndex = timeManager != null ? timeManager.CurrentAbsoluteDayIndex : 0;
            NamedSkuOrder order = procurement.PlaceOrder(
                itemId, 20, 15, 0, supplierId, dayIndex, NamedSkuPaymentMode.CashOnArrival, null);
            if (order == null)
            {
                Debug.LogError("[FeedSupply] General Store feed purchase was refused; no feed stock was created. " +
                    string.Join(" | ", procurement.Diagnostics), this);
                return;
            }

            storeBusiness.RuntimeState.RecordAppliedScenarioEndowment("small-prompt-2-horse-feed-order");
            drivers.AttachStoreFeedProcurement(procurement, storeBusiness);
            if (saveLoadManager != null)
            {
                if (saveLoadManager.PassXlProcurementService == null)
                    saveLoadManager.PassXlProcurementService = procurement;
                else if (!ReferenceEquals(saveLoadManager.PassXlProcurementService, procurement))
                    Debug.LogWarning("[FeedSupply] SaveLoadManager already owns a different PassXl procurement service; feed procurement was not substituted into that slot.", this);
            }
            Debug.Log($"[FeedSupply] General Store ordered {order.UnitsOrdered} sack(s) from {counterparty.DisplayName} " +
                $"at {order.UnitGoodsCostCents}c/sack, total {order.TotalCostCents}c; arrival day {order.ExpectedArrivalDayIndex}; " +
                $"cash before payment={storeBusiness.RuntimeState.CurrentCashCents}c. No category stock was seeded for free.", this);
        }
#endif
    }
}
