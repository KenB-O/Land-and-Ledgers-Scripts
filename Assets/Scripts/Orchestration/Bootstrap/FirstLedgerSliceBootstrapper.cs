using System;
using System.Collections.Generic;
using LandLedgers.Civic;
using System.Text;
using LandLedgers.CameraSystem;
using LandLedgers.Economy;
using LandLedgers.Economy.Core;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Businesses.BoardingHouse;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.LumberYard;
using LandLedgers.Economy.Businesses.Logging;
using LandLedgers.Economy.Businesses.Sawmill;
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
using EntityId = LandLedgers.Primitives.EntityId;
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
                // Q04 Slice 2 (re-plan §3, AUD-275): ConfigureMillbrookAidTable
                // needs no business instance — it loads the definition from
                // Resources and constructs the runtime itself — so this
                // pre-load call genuinely works, unlike the Pass 9 owner
                // (Slice 1, created inside the LoadDefaultSlot window
                // instead). The drivers instance must be fetched first: the
                // Continue branch holds none (the New Game body fetches it
                // at :546). The runtime survives the load intact (nothing
                // in LoadDefaultSlot clears or replaces MillbrookAidRuntime),
                // and the Pass XL restore imports the saved section into it.
                // An old save (no Millbrook section) restores as starting
                // stock with cash 0 and no lots, and the next save writes a
                // Millbrook section at starting units (re-plan §1.9).
                SimulationDrivers continueDrivers = FindAnyObjectByType<SimulationDrivers>();
                ConfigureMillbrookAidTable(continueDrivers);
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
                int requiredHomes = townConfiguration.GeneratePopulationFromSettings
                    ? townConfiguration.GeneratedPopulationSettings.maximumStartingHouseholds
                    : townConfiguration.Households.Count;
                int requiredBusinessBuildings = townConfiguration.GeneratePopulationFromSettings
                    ? -1
                    : townConfiguration.Businesses.Count;
                if (!townWorld.TryApplyDevelopmentResidentialHomeCount(
                    requiredHomes, out string developmentWorldReason, requiredBusinessBuildings))
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
                int requiredResidentialHomes = townConfiguration.GeneratePopulationFromSettings
                    ? townConfiguration.GeneratedPopulationSettings.maximumStartingHouseholds
                    : townConfiguration.Households.Count;
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

                    if (building.definition.CanHostHouseholds
                        && (townConfiguration.GeneratePopulationFromSettings || !building.definition.CanHostWorkplace)
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
                bool invalidRoster = townConfiguration.GeneratePopulationFromSettings
                    ? realResidentialHomes < requiredResidentialHomes
                    : realResidentialHomes != requiredResidentialHomes
                        || realCommercialBuildings != requiredCommercialBuildings
                        || realTotalBuildings != requiredTotalBuildings;
                if (invalidRoster)
                {
                    bootstrapStatus = townConfiguration.GeneratePopulationFromSettings
                        ? $"Development startup stopped: physical residential household capacity={realResidentialHomes}/{requiredResidentialHomes}. No Businesses or Persons were initialized."
                        : $"Development startup stopped: standalone homes={realResidentialHomes}/{requiredResidentialHomes}, storefronts={realCommercialBuildings}/{requiredCommercialBuildings}, total buildings={realTotalBuildings}/{requiredTotalBuildings}. No Businesses or Persons were initialized.";
                    Debug.LogError($"[WorldStartup] {bootstrapStatus}", this);
                    CompleteStartupTiming(totalTimer, timing, "development homes unavailable");
                    initializing = false;
                    return;
                }

                Debug.Log(townConfiguration.GeneratePopulationFromSettings
                    ? $"[WorldStartup] Generated development profile has {realResidentialHomes} physical household spaces for a maximum of {requiredResidentialHomes} starting households. Buildings: {buildingRoster}."
                    : $"[WorldStartup] Development profile verified: {realResidentialHomes} standalone homes, {realCommercialBuildings} storefronts. Buildings: {buildingRoster}.", this);
            }
#endif

            // The authored SimulationDrivers component can Awake before this
            // bootstrap creates the TownGrid. Rebind after world generation so
            // hub-owned parcel services receive the actual live grid on a fresh
            // New Game, not only after a later save restore.
            FindAnyObjectByType<SimulationDrivers>()?.Rebind();

            // R34: seed the legal parcel registry from the town plots on a fresh
            // New Game, so the registry is populated from the same plots the
            // physical world already has. New-Game-only: a Continue restores the
            // registry from the save (hub LoadFromSaveDto), and a re-run is a
            // no-op — InitializeSlice's already-initialized guard plus the seam's
            // per-plot link guard. Runs in every build, like the notions
            // registration below.
            if (startupIntent == WorldStartupIntent.NewGame)
            {
                systemsHub ??= FindAnyObjectByType<SimulationSystemsHub>();
                if (systemsHub != null && townWorld != null)
                {
                    int seededParcels = systemsHub.Parcels.SeedParcelsFromTownPlots(townWorld.Plots, string.Empty);
                    Debug.Log($"[WorldStartup] Seeded {seededParcels} legal parcels from {townWorld.Plots.Count} town plots.", this);
                }
            }

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
                    if (townConfiguration.GeneratePopulationFromSettings)
                        populationManager.GeneratePopulationSnapshot();
                    else
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
            ConfigureLiveLumberYardSaveOwner();
            ConfigureLiveSawmillSaveOwner();
            ConfigureLumberYardConstructionBindings();
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
                    SeedDevelopmentBakeryOpeningInputs();
                if (startupIntent == WorldStartupIntent.NewGame)
                    SeedDevelopmentBusinessOpeningEquipment();
                if (startupIntent == WorldStartupIntent.NewGame)
                    SeedBakeryWholesaleAccounts();
                if (startupIntent == WorldStartupIntent.NewGame && simulationDrivers != null && developmentNpcStore?.RuntimeState != null)
                    ConfigurePlaytestHorseFeed(developmentNpcStore, simulationDrivers);
            }
#endif
            // Plan 04 Slice 5: notions procurement is production wiring,
            // not a playtest registration — it runs on NewGame in every
            // build. In editor/development builds ConfigurePlaytestHorseFeed
            // above already registered the notions counterparty on the
            // shared feed market and owns the save slot; the method detects
            // that and returns without overwriting (AttachStoreFeedProcurement
            // overwrites unconditionally, and the dev service also carries
            // the horse-feed order). In release builds this is the only
            // registration — and Campaign B (DEF-008) extended it to also
            // register the Millbrook feed counterparty, the household
            // catalog item, and the store feed registry item, so release
            // builds get the same feed loop dev builds have. The store is
            // the NPC General Store (the bakery's wholesale buyer); if it
            // does not resolve, the method logs the gap and the daily
            // driver names the missing service (F19).
            if (startupIntent == WorldStartupIntent.NewGame && simulationDrivers != null)
                RegisterNotionsProcurement(FindDevelopmentNpcGeneralStore(), simulationDrivers);
            // Register the live store with the shared supplier directory. The
            // household membership binding remains an explicit scenario/world
            // concern; registering the seller alone cannot execute a purchase.
            if (simulationDrivers != null && storeRuntime != null)
            {
                GeneralStoreTradingPort storePort;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                storePort = developmentNpcStore != null
                    ? storeRuntime.BuildHouseholdTradingPort(developmentNpcStore, simulationDrivers.StoreFeedRegistry)
                    : storeRuntime.BuildHouseholdTradingPort();
#else
                storePort = storeRuntime.BuildHouseholdTradingPort();
#endif
                simulationDrivers.RegisterGeneralStoreSupplier(storePort);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (developmentNpcStore != null)
                    storeRuntime.ConfigureDevelopmentNpcRestock(developmentNpcStore, simulationDrivers.StoreFeedRegistry);
#endif
                // Priority 1 Slice 0 (log-only): name the registered port
                // flavour, its stock, and its staffing, so the next Unity
                // run can separate H1 (knowledge never seeded) from H2
                // (wrong-state stock read) and H3 (unstaffed). No state,
                // price, quantity, schedule, or policy change here.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                string storePortFlavour = developmentNpcStore != null ? "npc" : "fallback";
                string npcStoreStaffing = developmentNpcStore?.RuntimeState != null
                    ? developmentNpcStore.RuntimeState.FilledWorkerCount.ToString()
                    : "no NPC store";
                if (enableDevelopmentTownConfiguration && ResolveDevelopmentTownConfiguration() != null && developmentNpcStore == null)
                    Debug.LogError("[WorldStartup] Development town configuration exists but no NPC General Store resolved; registering the fallback trading port instead of the NPC port.", this);
#else
                string storePortFlavour = "fallback";
                string npcStoreStaffing = "no NPC store";
#endif
                Debug.Log($"[WorldStartup] Registered general store supplier: flavour={storePortFlavour}, " +
                    $"StoreBusinessId={storePort.StoreBusinessId}, LocationId={storePort.LocationId}, " +
                    $"ItemStockUnits(bread)={storePort.ItemStockUnits("bread")}, ItemStockUnits(flour)={storePort.ItemStockUnits("flour")}, " +
                    $"CategoryStockUnits(staple_food)={storePort.CategoryStockUnits("staple_food")}, " +
                    $"NPC store FilledWorkerCount={npcStoreStaffing}.", this);
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
            // DEF-013 (Decision 9): a fresh New Game has no legacy business
            // cash to migrate. The store's authored opening cash stays
            // business capital; the portfolio is marked initialized without
            // moving it, so the first load restores the saved portfolio
            // instead of running the legacy migration.
            bool portfolioWasInitialized = playerPortfolioManager != null && playerPortfolioManager.IsInitialized;
            playerPortfolioManager?.InitializeFreshGame(storeRuntime, sharedBusinessRuntime);
            // P4: the scenario promises "a stake of cash" (FirstLedger.asset tunable
            // 'startingCashCents'). Seed it once on a fresh game — never on load.
            if (!portfolioWasInitialized)
            {
                SeedPlayerStartingStake();
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            RunOpeningEconomyAudit();
#endif
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
            int npcGeneralStoreCount = 0;
            bool npcBusinessRosterKnown = sharedBusinessRuntime?.Businesses != null;
            if (npcBusinessRosterKnown)
            {
                foreach (BusinessInstanceState business in sharedBusinessRuntime.Businesses)
                {
                    if (business?.BusinessType == BusinessType.GeneralStore
                        && business.Owner?.OwnerKind == BusinessOwnerKind.Npc)
                        npcGeneralStoreCount++;
                }
            }
            bootstrapStatus = $"Game slice ready. Buildings={townWorld.Buildings.Count}, " +
                $"Generated people={(populationManager != null ? populationManager.GeneratedPersonCount.ToString() : "unknown")}, " +
                $"Player-owned General Store={(storeRuntime != null ? storeRuntime.Status : "not bound")}, " +
                $"NPC-owned General Stores={(npcBusinessRosterKnown ? npcGeneralStoreCount.ToString() : "unknown")}.";
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
            // Refresh derived destinations before restored journeys bind to their physical targets.
            GetComponent<PhysicalRegionalTownRuntime>()?.RefreshWorldFrame(townWorld);
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

            DevelopmentTownConfiguration configuration = ResolveDevelopmentTownConfiguration();
            PopulationGenerationSettings selectedSettings = enableDevelopmentTownConfiguration
                && configuration != null && configuration.GeneratePopulationFromSettings
                ? configuration.GeneratedPopulationSettings
                : populationSettings;
            populationManager.Configure(townWorld, selectedSettings, false);
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

        /// <summary>Finite NewGame-only production inputs authored by the development-town scenario.</summary>
        private void SeedDevelopmentBakeryOpeningInputs()
        {
            DevelopmentTownConfiguration configuration = ResolveDevelopmentTownConfiguration();
            if (!enableDevelopmentTownConfiguration || configuration == null
                || (configuration.OpeningBakeryFlourPounds <= 0 && configuration.OpeningBakeryNotionsUnits <= 0)) return;

            string bakeryKey = null;
            foreach (DevelopmentTownConfiguration.BusinessDefinition slot in configuration.Businesses)
                if (slot != null && ToBusinessType(slot.operationId) == BusinessType.Bakery)
                { bakeryKey = slot.key; break; }
            BusinessInstanceState business = FindDevelopmentBusiness(BusinessType.Bakery);
            Series2Pass9SaveState pass9 = saveLoadManager?.Series2Pass9State;
            systemsHub ??= FindAnyObjectByType<SimulationSystemsHub>();
            if (string.IsNullOrWhiteSpace(bakeryKey) || business?.RuntimeState == null || pass9?.BakeryRuntime == null || systemsHub == null)
            {
                Debug.LogError("[WorldStartup] Opening Bakery inputs refused: configured bakery, runtime, or lot-id authority is missing.", this);
                return;
            }
            var diagnostics = new List<string>();
            if (!LandLedgers.Economy.Businesses.Bakery.ScenarioOpeningBakeryInputEndowment.TryApply(
                    pass9.BakeryRuntime, business.RuntimeState, systemsHub.Ids, configuration.ConfigurationId,
                    bakeryKey, townWorld?.Settings != null ? townWorld.Settings.seed : configuration.SeedOffset,
                    configuration.OpeningBakeryFlourPounds, configuration.OpeningBakeryNotionsUnits,
                    timeManager != null ? timeManager.CurrentAbsoluteDayIndex : 0, diagnostics,
                    out string report, out string error))
            { Debug.LogError($"[WorldStartup] {error}", this); return; }
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
            "blacksmith" => BusinessType.Blacksmith,
            "butcher" => BusinessType.Butcher,
            "doctor" => BusinessType.Doctor,
            "sawmill" => BusinessType.Sawmill,
            "lumber-yard" or "lumberyard" => BusinessType.LumberYard,
            "boarding-house" or "boardinghouse" => BusinessType.BoardingHouse,
            "ranch" => BusinessType.Ranch,
            "crop-farm" or "cropfarm" => BusinessType.CropFarm,
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
                string ownerName = owner != null
                    ? owner.firstName + " " + owner.lastName
                    : configuration.GeneratePopulationFromSettings
                        ? "Generated resident " + ownerId
                        : "Development configuration";
                // The scenario's authored premisesRequired flag is the controlling
                // configuration. In particular, a route freight operator may be
                // registered without claiming a building; task-level site needs
                // still gate any later physical operation.
                PremisesRequirement premises = !slot.premisesRequired
                    ? new PremisesRequirement()
                    : type switch
                {
                    BusinessType.GeneralStore => new PremisesRequirement(customerFacing: true, minAreaSqFt: 800),
                    BusinessType.Bakery => new PremisesRequirement(customerFacing: true, foodHandling: true, minAreaSqFt: 500),
                    BusinessType.FuelDealer => new PremisesRequirement(yardStorage: true, minAreaSqFt: 600),
                    BusinessType.Blacksmith => new PremisesRequirement(workshop: true, yardStorage: true, minAreaSqFt: 600),
                    BusinessType.Butcher => new PremisesRequirement(customerFacing: true, foodHandling: true, minAreaSqFt: 500),
                    BusinessType.Doctor => new PremisesRequirement(customerFacing: true, minAreaSqFt: 400),
                    BusinessType.Sawmill => new PremisesRequirement(workshop: true, yardStorage: true, minAreaSqFt: 3000),
                    BusinessType.LumberYard => new PremisesRequirement(yardStorage: true, minAreaSqFt: 4000),
                    BusinessType.BoardingHouse => new PremisesRequirement(customerFacing: true, minAreaSqFt: 1200),
                    BusinessType.Ranch => new PremisesRequirement(animalHousing: true, minAreaSqFt: 10000),
                    BusinessType.CropFarm => new PremisesRequirement(minAreaSqFt: 20000),
                    BusinessType.LiveryFreight => new PremisesRequirement(animalHousing: true, yardStorage: true, minAreaSqFt: 2000),
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

        // Plan 25: deferred sawmill restore. Called by SaveLoadManager.LoadDefaultSlot
        // after the Pass 9 mill restore — the driver loads re-attach against yard
        // lots that do not exist until the Pass 9 restore runs. Takes the DTO as
        // a parameter and keeps no state (the loads are clear-then-load, so a
        // second call in the same session is safe by construction).
        public void ApplySawmillRestoreAfterPass9(SystemsSaveDto systems)
        {
            systemsHub ??= FindAnyObjectByType<SimulationSystemsHub>();
            if (systemsHub == null)
            {
                Debug.LogWarning("[SaveLoad] Sawmill restore after Pass 9 was not applied: no SimulationSystemsHub found; the four sawmill sections will not restore.", this);
                return;
            }

            foreach (string diagnostic in systemsHub.LoadSawmillSectionsFromSaveDto(systems))
            {
                Debug.LogWarning($"[SaveLoad] {diagnostic}");
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ReapplyFellingCampAndRightsAfterPass9();
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Note-112 Finding 1 follow-up (plan 29, note 118): on Continue from a
        // pre-slice save the Pass 9 restore passes null DTOs to the stand
        // registry and camp (Series2Pass9Save.cs:1129/:1184), whose
        // clear-then-load wipes the stands, grants, and roster the :449
        // wiring registered. Re-run the same configuration when the
        // restored registry is empty. Post-slice saves restore stands, so
        // the trigger stays off and the re-run is a no-op for them (§3.4).
        // Both this method and its call site sit inside the guard:
        // ConfigureFellingCampAndRights is dev-build only, and calling it
        // from the unguarded deferred step would break release compilation.
        private void ReapplyFellingCampAndRightsAfterPass9()
        {
            if (saveLoadManager == null)
            {
                Debug.LogWarning("[SaveLoad] Felling config was not re-applied after the Pass 9 restore: SaveLoadManager is missing.", this);
                return;
            }
            Series2Pass9SaveState pass9 = saveLoadManager.Series2Pass9State;
            if (pass9 == null)
            {
                Debug.LogWarning("[SaveLoad] Felling config was not re-applied after the Pass 9 restore: no Pass 9 state.", this);
                return;
            }
            if (pass9.StandRegistry == null)
            {
                Debug.LogWarning("[SaveLoad] Felling config was not re-applied after the Pass 9 restore: no stand registry (the :449 wiring never ran).", this);
                return;
            }
            DevelopmentTownConfiguration configuration = ResolveDevelopmentTownConfiguration();
            if (!enableDevelopmentTownConfiguration || configuration == null)
                return; // No config: nothing was registered at :449 either.
            int authoredStandCount = 0;
            foreach (DevelopmentTownConfiguration.TimberStandEntry entry in configuration.TimberStands)
                if (entry != null && !string.IsNullOrWhiteSpace(entry.standId)) authoredStandCount++;
            if (!pass9.StandRegistry.NeedsFellingConfigReapply(authoredStandCount))
                return; // Post-slice save (stands restored) or no stands authored: nothing to re-apply.
            BusinessInstanceState millBusiness = FindDevelopmentBusiness(BusinessType.Sawmill);
            if (millBusiness == null || string.IsNullOrWhiteSpace(millBusiness.InstanceId))
            {
                Debug.LogWarning("[SaveLoad] Felling config was not re-applied after the Pass 9 restore: no authored Sawmill business instance is present in the shared runtime.", this);
                return;
            }
            ConfigureFellingCampAndRights(millBusiness, pass9, configuration);
            Debug.Log("[SaveLoad] Felling config re-applied after the Pass 9 restore: the restored stand registry was empty (pre-slice save).", this);
        }
#endif

        // Q04 Slice 1 (re-plan): called by SaveLoadManager.LoadDefaultSlot
        // AFTER the shared-business restore and BEFORE the Pass XL restore.
        // The Bakery instance exists only in that window (a pre-load call
        // warn-returns: no authored Bakery business instance is present yet),
        // and the planned batch ids / runtime data are unrecoverable after
        // the restore (LoadDefaultSlot does not retain the DTO).
        public void EnsurePass9SaveOwnerForRestore()
        {
            if (saveLoadManager == null)
            {
                Debug.LogWarning("[BakeryRuntime] Pass 9 save owner was not created on restore: SaveLoadManager is missing.", this);
                return;
            }
            Series2Pass9SaveState pass9 = saveLoadManager.Series2Pass9State;
            if (pass9 == null)
            {
                pass9 = new Series2Pass9SaveState();
                saveLoadManager.Series2Pass9State = pass9;
            }
            systemsHub ??= FindAnyObjectByType<SimulationSystemsHub>();
            if (systemsHub != null)
            {
                pass9.PersonHands = systemsHub.PersonHands;
                pass9.HandheldContainers = systemsHub.HandheldContainers;
            }
            // Campaign C (DEF-073): bind the live store queue so the Pass 9
            // restore repopulates it — patience included. Without this the
            // section is never captured or restored: Series2Pass9SaveState.
            // CustomerQueue stays null, every save writes an empty queue
            // section, and every waiting customer is dropped on load (their
            // restored trip then waits with no queue entry and can never
            // balk). The manager owns the live queue; restore is
            // clear-then-load into it. Runs in the pre-restore window, so
            // this binding lands before the Pass XL restore consumes the
            // state below.
            var storeManager = FindAnyObjectByType<GeneralStoreRuntimeManager>();
            if (storeManager != null)
                pass9.CustomerQueue = storeManager.CustomerQueue;
            // Reuse, never replace: Restore is clear-then-load into the live
            // holders, and the drivers cache references to them.
            if (pass9.FreightDriver == null)
                pass9.FreightDriver = new LandLedgers.Economy.Freight.FreightProductionDriver();
            // Mirrors SimulationDrivers.cs:7173: the delivered-lot guard restores
            // only when the intake instance exists; production creates it lazily
            // on the first drive, so a real mid-session save can carry delivered
            // lot ids. (Catalog needs no pre-creation: Restore replaces it.)
            if (pass9.WholesaleIntake == null)
                pass9.WholesaleIntake = new LandLedgers.Economy.Ordering.Pass3WholesaleIntake();
            // DEF-009 follow-on (Decision 5): same window, same rule — the
            // fuel-yard intake holder must exist before the Pass XL restore
            // consumes the state. Without this the restore parks the saved
            // lots as pending and a re-save before any driver runs writes an
            // empty section, silently dropping them. With it the lots restore
            // in place, clear-then-load, exactly like the other Pass 9
            // holders above. Empty holder only; inventory arrives solely
            // through the sawmill-byproduct custody transfer.
            if (pass9.FuelIntake == null)
                pass9.FuelIntake = new FuelYardByproductIntake();
            // Creates/reuses the Bakery runtime + driver when a Bakery business
            // instance exists (binding the owner, attaching the fuel source);
            // warn-returns otherwise, leaving the state + freight driver above
            // in place for the degraded path (section 2.4).
            ConfigureLiveBakerySaveOwner();
            // C2: same window, same rule — the yard holder must exist
            // before the Pass XL restore consumes the state.
            ConfigureLiveLumberYardSaveOwner();
            // D1: same window, same rule — the mill holder must exist
            // before the Pass XL restore consumes the state.
            ConfigureLiveSawmillSaveOwner();
            // C3: same window, same rule — the price port and the yard
            // supplier registration are runtime state, re-established
            // here after every restore.
            ConfigureLumberYardConstructionBindings();
        }

        // Q04 Slice 3 (re-plan): called by SaveLoadManager.LoadDefaultSlot
        // AFTER the shared-business restore and BEFORE the Pass XL restore,
        // immediately after EnsurePass9SaveOwnerForRestore. The NPC General
        // Store exists only in that window (a pre-load call finds no store:
        // FindDevelopmentNpcGeneralStore scans sharedBusinessRuntime.Businesses,
        // which is empty until the restore). The guard inside
        // RegisterNotionsProcurement is untouched: on a first Continue load
        // the slot is null and a fresh notions-only service is created; on a
        // second load in the same session the slot is already owned and the
        // call returns without overwriting (see the staleness limitation in
        // the slice log / addendum section 1.7).
        public void EnsureNotionsProcurementForRestore()
        {
            BusinessInstanceState storeBusiness = FindDevelopmentNpcGeneralStore();
            SimulationDrivers drivers = FindAnyObjectByType<SimulationDrivers>();
            if (drivers == null || storeBusiness?.RuntimeState == null)
            {
                RegisterNotionsProcurement(storeBusiness, drivers);
                return;
            }
            OffMapNamedSkuProcurementService procurement = saveLoadManager?.PassXlProcurementService;
            if (procurement != null)
            {
                procurement.RebindBusinessCash(new Series2BusinessRuntimeCashPort(storeBusiness.RuntimeState));
                drivers.AttachStoreFeedProcurement(procurement, storeBusiness);
            }
            else RegisterNotionsProcurement(storeBusiness, drivers);

            // SaveLoadManager has rebuilt the business instances. Supplier
            // identity remains stable, but its old object is no longer live.
            if (storeRuntime != null)
                drivers.RegisterGeneralStoreSupplier(storeRuntime.BuildHouseholdTradingPort(
                    storeBusiness, drivers.StoreFeedRegistry));
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

        // C2: mirrors ConfigureLiveBakerySaveOwner for the lumber yard.
        // Creates/reuses the yard runtime + Pass 9 holder when a
        // LumberYard business instance exists; warn-returns otherwise,
        // leaving the state in place for the degraded path (the compact
        // preset has no yard). First NPC yard only: the Pass 9 slot is a
        // single field, so a second yard business has no slot to save
        // into and is warned about, never silently wired.
        private void ConfigureLiveLumberYardSaveOwner()
        {
            if (sharedBusinessRuntime == null || saveLoadManager == null)
            {
                Debug.LogWarning("[LumberYardRuntime] Live owner was not created: shared business runtime or SaveLoadManager is missing.", this);
                return;
            }

            systemsHub ??= FindAnyObjectByType<SimulationSystemsHub>();
            if (systemsHub == null)
            {
                Debug.LogWarning("[LumberYardRuntime] Live owner was not created: SimulationSystemsHub is missing.", this);
                return;
            }

            BusinessInstanceState yardBusiness = FindDevelopmentBusiness(BusinessType.LumberYard);
            if (yardBusiness == null || string.IsNullOrWhiteSpace(yardBusiness.InstanceId))
            {
                Debug.LogWarning("[LumberYardRuntime] Live owner was not created: no authored LumberYard business instance is present in the shared runtime.", this);
                return;
            }

            Series2Pass9SaveState pass9 = saveLoadManager.Series2Pass9State;
            if (pass9 == null)
            {
                pass9 = new Series2Pass9SaveState();
                saveLoadManager.Series2Pass9State = pass9;
            }

            // Reuse, never replace: the hub accessor creates the runtime
            // on first use and returns the live holder afterwards, so the
            // hub dictionary and the Pass 9 holder share one instance.
            LumberYardShopRuntime liveYardRuntime = systemsHub.LumberYardRuntimeFor(yardBusiness.InstanceId);
            if (liveYardRuntime == null)
            {
                Debug.LogWarning("[LumberYardRuntime] Live owner was not created: hub returned no runtime.", this);
                return;
            }

            if (pass9.LumberYard == null)
            {
                pass9.LumberYard = liveYardRuntime;
            }
            else if (!string.Equals(pass9.LumberYard.BusinessInstanceId, yardBusiness.InstanceId, StringComparison.Ordinal))
            {
                Debug.LogWarning($"[LumberYardRuntime] Replacing Pass 9 lumber yard holder '{pass9.LumberYard.BusinessInstanceId}' with the authored yard '{yardBusiness.InstanceId}' (the Pass 9 load's identity guard would refuse the mismatched DTO anyway).", this);
                pass9.LumberYard = liveYardRuntime;
            }

            foreach (BusinessInstanceState candidate in sharedBusinessRuntime.Businesses)
            {
                if (candidate == null || candidate.BusinessType != BusinessType.LumberYard) continue;
                if (string.Equals(candidate.InstanceId, yardBusiness.InstanceId, StringComparison.Ordinal)) continue;
                Debug.LogWarning($"[LumberYardRuntime] Second LumberYard business '{candidate.InstanceId}' is unwired: the Pass 9 slot holds one yard only, so its stock is not saved.", this);
                break;
            }

            Debug.Log($"[LumberYardRuntime] Bootstrap owns one live LumberYardShopRuntime for '{yardBusiness.RuntimeDisplayName}' ({yardBusiness.InstanceId}); its Pass 9 state is registered with SaveLoadManager.", this);
        }

        // D1: mirrors ConfigureLiveLumberYardSaveOwner for the sawmill.
        // Owns one live SawmillShopRuntime for the first NPC-owned Sawmill
        // business and hands it to the Pass 9 holder, so the daily
        // byproduct transfer (SimulationDrivers) has a live mill to draw
        // from. First mill only (C2 rule); a second mill is warned about
        // and gets nothing here. Called at NewGame and in the restore
        // window, immediately after ConfigureLiveLumberYardSaveOwner.
        private void ConfigureLiveSawmillSaveOwner()
        {
            if (sharedBusinessRuntime == null || saveLoadManager == null)
            {
                Debug.LogWarning("[SawmillRuntime] Live owner was not created: shared business runtime or SaveLoadManager is missing.", this);
                return;
            }

            systemsHub ??= FindAnyObjectByType<SimulationSystemsHub>();
            if (systemsHub == null)
            {
                Debug.LogWarning("[SawmillRuntime] Live owner was not created: SimulationSystemsHub is missing.", this);
                return;
            }

            BusinessInstanceState millBusiness = FindDevelopmentBusiness(BusinessType.Sawmill);
            if (millBusiness == null || string.IsNullOrWhiteSpace(millBusiness.InstanceId))
            {
                Debug.LogWarning("[SawmillRuntime] Live owner was not created: no authored Sawmill business instance is present in the shared runtime.", this);
                return;
            }

            Series2Pass9SaveState pass9 = saveLoadManager.Series2Pass9State;
            if (pass9 == null)
            {
                pass9 = new Series2Pass9SaveState();
                saveLoadManager.Series2Pass9State = pass9;
            }

            // Reuse, never replace: the hub accessor creates the runtime
            // on first use and returns the live holder afterwards, so the
            // hub dictionary and the Pass 9 holder share one instance.
            SawmillShopRuntime liveMillRuntime = systemsHub.SawmillRuntimeFor(millBusiness.InstanceId);
            if (liveMillRuntime == null)
            {
                Debug.LogWarning("[SawmillRuntime] Live owner was not created: hub returned no runtime.", this);
                return;
            }

            if (pass9.Sawmill == null)
            {
                pass9.Sawmill = liveMillRuntime;
            }
            else if (!string.Equals(pass9.Sawmill.BusinessInstanceId, millBusiness.InstanceId, StringComparison.Ordinal))
            {
                Debug.LogWarning($"[SawmillRuntime] Replacing Pass 9 sawmill holder '{pass9.Sawmill.BusinessInstanceId}' with the authored mill '{millBusiness.InstanceId}' (the Pass 9 load's identity guard would refuse the mismatched DTO anyway).", this);
                pass9.Sawmill = liveMillRuntime;
            }

            foreach (BusinessInstanceState candidate in sharedBusinessRuntime.Businesses)
            {
                if (candidate == null || candidate.BusinessType != BusinessType.Sawmill) continue;
                if (string.Equals(candidate.InstanceId, millBusiness.InstanceId, StringComparison.Ordinal)) continue;
                Debug.LogWarning($"[SawmillRuntime] Second Sawmill business '{candidate.InstanceId}' is unwired: the Pass 9 slot holds one mill only, so its stock is not saved.", this);
                break;
            }

            Debug.Log($"[SawmillRuntime] Bootstrap owns one live SawmillShopRuntime for '{millBusiness.RuntimeDisplayName}' ({millBusiness.InstanceId}); its Pass 9 state is registered with SaveLoadManager.", this);

            // P3 (F6 production wiring): bind the sawmill production
            // driver, counters, ledger, and logging operation runtime
            // into the daily flow. Runs on NewGame and restore alike.
            ConfigureSawmillProductionWiring(millBusiness, pass9);

            // F7 yard-transfer wiring: bind the sawmill-to-yard transfer
            // driver into the daily flow. Runs on NewGame and restore
            // alike. The yard business is resolved here with
            // FindDevelopmentBusiness, the same call
            // ConfigureLiveLumberYardSaveOwner uses (note 103 C1: the
            // call template is its :2198 site, not the :1677 method
            // definition); the yard owner runs before the mill owner in
            // both paths, so pass9.LumberYard already exists.
            BusinessInstanceState yardBusiness = FindDevelopmentBusiness(BusinessType.LumberYard);
            ConfigureSawmillYardTransferWiring(millBusiness, yardBusiness, pass9);
        }

        // P3 (F6 production wiring): constructs the sawmill production
        // driver, the labour counters, and the custody ledger, registers
        // them via the SimulationDrivers forwarders, and constructs the
        // logging operation runtime into the Pass 9 slot. Reuse, never
        // replace (the mill rule in ConfigureLiveSawmillSaveOwner): a
        // second call in the same session keeps the existing instances,
        // so a restored job record is never discarded by re-construction.
        private void ConfigureSawmillProductionWiring(BusinessInstanceState millBusiness, Series2Pass9SaveState pass9)
        {
            if (millBusiness == null || string.IsNullOrWhiteSpace(millBusiness.InstanceId) || pass9 == null)
            {
                Debug.LogWarning("[SawmillProduction] Wiring was not created: no authored Sawmill business instance or Pass 9 state.", this);
                return;
            }

            systemsHub ??= FindAnyObjectByType<SimulationSystemsHub>();
            if (systemsHub == null)
            {
                Debug.LogWarning("[SawmillProduction] Wiring was not created: SimulationSystemsHub is missing.", this);
                return;
            }

            SimulationDrivers drivers = FindAnyObjectByType<SimulationDrivers>();
            if (drivers == null)
            {
                Debug.LogWarning("[SawmillProduction] Wiring was not created: SimulationDrivers is missing.", this);
                return;
            }

            SawmillShopRuntime liveMillRuntime = systemsHub.SawmillRuntimeFor(millBusiness.InstanceId);
            if (liveMillRuntime == null)
            {
                Debug.LogWarning("[SawmillProduction] Wiring was not created: hub returned no mill runtime.", this);
                return;
            }

            // C3: the driver must receive the hub-retained gate, never a
            // second gate. InstallEquipmentTaskGate is idempotent, so this
            // is safe regardless of whether SimulationDrivers.Awake
            // already installed it.
            if (systemsHub.InstalledEquipmentTaskGate == null)
            {
                systemsHub.InstallEquipmentTaskGate();
            }
            EquipmentTaskGate gate = systemsHub.InstalledEquipmentTaskGate;
            if (gate == null)
            {
                Debug.LogWarning("[SawmillProduction] Wiring was not created: no equipment task gate could be installed.", this);
                return;
            }

            SawmillLabourCounters counters = systemsHub.SawmillLabourCounters ?? new SawmillLabourCounters();
            LogLotCustodyLedger ledger = systemsHub.LogLotCustodyLedger ?? new LogLotCustodyLedger(new List<string>());
            SawmillProductionDriver driver = systemsHub.SawmillProductionDriver;
            if (driver == null)
            {
                driver = new SawmillProductionDriver(
                    liveMillRuntime,
                    systemsHub.Tasks,
                    systemsHub.WorkTimeBudgets,
                    gate,
                    counters,
                    new List<string>(),
                    ledger);
            }
            drivers.RegisterSawmillProductionDriver(driver);
            drivers.RegisterSawmillLabourCounters(counters);
            drivers.RegisterLogLotCustodyLedger(ledger);

            // C4: construct the logging operation runtime with the LIVE
            // registry (never a second registry) and assign it to the
            // Pass 9 slot. Reuse, never replace.
            if (pass9.LoggingOperation == null)
            {
                pass9.LoggingOperation = new LoggingOperationRuntime(millBusiness.InstanceId, systemsHub.Tasks.IdRegistry, ledger);
            }

            // A2 plan 27 (P10 live felling driver, note 110): construct
            // the stand registry into the Pass 9 slot. Reuse, never
            // replace: a second call in the same session keeps the
            // existing registry. The empty registry needs no config and
            // is constructed in every build; the camp and everything
            // config-dependent sit in ConfigureFellingCampAndRights
            // below. Zero by default: a configuration with no stands
            // registers none, and the felling driver refuses loudly
            // instead of inventing values.
            if (pass9.StandRegistry == null)
            {
                pass9.StandRegistry = new LoggingStandRegistry();
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            DevelopmentTownConfiguration fellingConfiguration = ResolveDevelopmentTownConfiguration();
            if (enableDevelopmentTownConfiguration && fellingConfiguration != null)
            {
                ConfigureFellingCampAndRights(millBusiness, pass9, fellingConfiguration);
            }
#endif

            Debug.Log($"[SawmillProduction] Bootstrap wired the sawmill production driver for '{millBusiness.InstanceId}' (driver, counters, ledger, Pass 9 logging runtime, stand registry).", this);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // A2 plan 27 (P10 live felling driver, note 110): registers the
        // config-authored stands, grants the mill timber rights on each
        // (grantor "town-land-office", note 110 Q3c), builds the camp
        // for the first listed stand, rosters the mill crew (owner +
        // first active employee, the PickSawmillSawyer pattern, note
        // 110 Q4/Q9), endows opening provisions once (marker-gated,
        // note 110 C7), and installs the mill axe via the mill-typed
        // installer (note 110 C6). Zero by default: no stands, no camp,
        // no provisions, no axe — the felling driver refuses loudly
        // instead of inventing values. Runs on NewGame and Continue
        // alike; the Pass 9 restore loads saved stands and roster over
        // what is built here, and the endowment markers keep the
        // provisions and axe once-only.
        private void ConfigureFellingCampAndRights(BusinessInstanceState millBusiness, Series2Pass9SaveState pass9, DevelopmentTownConfiguration configuration)
        {
            if (millBusiness == null || string.IsNullOrWhiteSpace(millBusiness.InstanceId) || pass9 == null || configuration == null)
            {
                Debug.LogWarning("[SawmillProduction] Felling was not wired: no mill business, Pass 9 state, or development-town configuration.", this);
                return;
            }

            string millKey = null;
            foreach (DevelopmentTownConfiguration.BusinessDefinition slot in configuration.Businesses)
                if (slot != null && ToBusinessType(slot.operationId) == BusinessType.Sawmill)
                {
                    millKey = slot.key;
                    break;
                }
            if (string.IsNullOrWhiteSpace(millKey))
            {
                Debug.LogWarning("[SawmillProduction] Felling was not wired: no sawmill business is configured.", this);
                return;
            }

            int dayIndex = timeManager != null ? timeManager.CurrentAbsoluteDayIndex : 0;

            // Register every config stand and grant the mill timber
            // rights on each. A stand that is already registered (a
            // second wiring call in the same session) is left alone,
            // never re-granted: RegisterStand refuses duplicates and
            // GrantTimberRights refuses a second grant.
            foreach (DevelopmentTownConfiguration.TimberStandEntry standEntry in configuration.TimberStands)
            {
                if (standEntry == null || string.IsNullOrWhiteSpace(standEntry.standId)) continue;
                if (pass9.StandRegistry.GetStand(standEntry.standId) == null)
                {
                    string standRefusal = pass9.StandRegistry.RegisterStand(new LandLedgers.Economy.Farming.Timber.TimberStand
                    {
                        StandId = standEntry.standId,
                        DisplayName = standEntry.displayName ?? string.Empty,
                        LocationId = standEntry.locationId ?? string.Empty,
                        Species = standEntry.species ?? string.Empty,
                        KilogramsPerLog = standEntry.kilogramsPerLog,
                        MilesToMill = standEntry.milesToMill,
                        StandingTimberUnits = standEntry.standingTimberUnits,
                        // DEF-072 (Decision 15): regrowth calibration; 0 =
                        // unset, and the registry defaults capacity to the
                        // standing stock and site quality to 1 (productive).
                        CarryingCapacityUnits = standEntry.carryingCapacityUnits,
                        SiteQuality01 = standEntry.siteQuality01,
                    });
                    if (!string.IsNullOrWhiteSpace(standRefusal))
                        Debug.LogWarning($"[SawmillProduction] {standRefusal}", this);
                }
                if (string.IsNullOrWhiteSpace(pass9.StandRegistry.CurrentRightsHolder(standEntry.standId)))
                {
                    string rightsRefusal = pass9.StandRegistry.GrantTimberRights(
                        standEntry.standId, millBusiness.InstanceId, dayIndex,
                        "town-land-office", "P10 live felling driver: opening timber rights for the mill.");
                    if (!string.IsNullOrWhiteSpace(rightsRefusal))
                        Debug.LogWarning($"[SawmillProduction] {rightsRefusal}", this);
                }
            }

            // Build the camp for the first listed stand, once. campId
            // "camp-" + standId, dayEstablished 0 at NewGame (note 110
            // Q8). No stands, no camp.
            if (pass9.LoggingCamp == null)
            {
                DevelopmentTownConfiguration.TimberStandEntry firstStand = null;
                foreach (DevelopmentTownConfiguration.TimberStandEntry candidate in configuration.TimberStands)
                    if (candidate != null && !string.IsNullOrWhiteSpace(candidate.standId)) { firstStand = candidate; break; }
                if (firstStand != null)
                {
                    pass9.LoggingCamp = new LoggingCampSite(
                        "camp-" + firstStand.standId, millBusiness.InstanceId, firstStand.standId, 0);
                }
            }

            // C-1 (expanded campaign, Phase 1 forestry): register the
            // scenario-authored opening employments for the mill BEFORE
            // rostering, so the camp can field a two-person felling team
            // (the owner plus the first active employee). The registry is
            // the hub-owned one (persisted via the hub save seam); a
            // refusal warns and the roster proceeds with the owner alone,
            // and the felling driver then refuses loudly for want of a
            // crew, exactly as it does today with no employee.
            if (millBusiness.RuntimeState != null)
            {
                systemsHub ??= FindAnyObjectByType<SimulationSystemsHub>();
                var employmentDiag = new List<string>();
                if (!ScenarioOpeningEmploymentEndowment.TryApply(configuration, millKey,
                        millBusiness.InstanceId, millBusiness.RuntimeState,
                        systemsHub?.Employments, dayIndex, employmentDiag,
                        out string employmentReport, out string employmentError))
                {
                    Debug.LogWarning($"[SawmillProduction] {employmentError}", this);
                }
                else
                {
                    Debug.Log($"[SawmillProduction] {employmentReport}", this);
                    foreach (string line in employmentDiag) Debug.Log($"[SawmillProduction] {line}", this);
                }
            }

            if (pass9.LoggingCamp != null)
            {
                // Roster the mill crew: the owner first, then the first
                // active employee. AssignWorker refuses duplicates, so
                // a second wiring call is a no-op.
                var campDiag = new List<string>();
                if (millBusiness.Owner != null && millBusiness.Owner.PersonId >= 0)
                {
                    EntityId ownerId = EntityId.For(EntityKind.Person, millBusiness.Owner.PersonId);
                    string rosterRefusal = pass9.LoggingCamp.AssignWorker(ownerId, LoggingCampRoles.Logger, dayIndex, campDiag);
                    if (!string.IsNullOrWhiteSpace(rosterRefusal))
                        Debug.LogWarning($"[SawmillProduction] Mill owner could not be rostered as a feller: {rosterRefusal}", this);
                }
                if (millBusiness.RuntimeState?.EmploymentRegistry != null)
                {
                    foreach (EmploymentRelationship relationship in millBusiness.RuntimeState.EmploymentRegistry.GetActiveByEmployer(millBusiness.InstanceId))
                    {
                        if (relationship == null || relationship.EmployeePersonId < 0) continue;
                        EntityId employeeId = EntityId.For(EntityKind.Person, relationship.EmployeePersonId);
                        string rosterRefusal = pass9.LoggingCamp.AssignWorker(employeeId, LoggingCampRoles.Logger, dayIndex, campDiag);
                        if (!string.IsNullOrWhiteSpace(rosterRefusal))
                            Debug.LogWarning($"[SawmillProduction] Mill employee could not be rostered as a feller: {rosterRefusal}", this);
                        break;
                    }
                }
                foreach (string line in campDiag) Debug.Log($"[SawmillProduction] {line}", this);

                // Endow opening provisions once (marker-gated): cover
                // days x headcount x provisions per head per day, from
                // the bootstrap endowment (note 110 C7). Cover days 0
                // = no endowment. The quantity is config-sourced; an
                // absent entry endows nothing, never an invented
                // quantity.
                if (configuration.OpeningLoggingCampProvisionCoverDays > 0 && millBusiness.RuntimeState != null)
                {
                    string provisionEndowmentId = $"development-town-opening-provisions:{configuration.ConfigurationId}:{millKey}:{millBusiness.InstanceId}";
                    if (!millBusiness.RuntimeState.HasAppliedScenarioEndowment(provisionEndowmentId))
                    {
                        float quantity = Math.Max(0f, configuration.OpeningLoggingCampProvisionCoverDays
                            * pass9.LoggingCamp.CrewHeadcount
                            * pass9.LoggingCamp.Parameters.ProvisionsPerHeadPerDay);
                        if (quantity > 0f)
                        {
                            var provisionDiag = new List<string>();
                            string deliveryRefusal = pass9.LoggingCamp.SupplyLedger.RecordDelivery(
                                LoggingCampSupplyLedger.BootstrapEndowmentSupplierId,
                                quantity, dayIndex,
                                $"P10 opening provision endowment: {configuration.OpeningLoggingCampProvisionCoverDays} cover days for the logging camp crew.",
                                provisionDiag);
                            foreach (string line in provisionDiag) Debug.Log($"[SawmillProduction] {line}", this);
                            if (!string.IsNullOrWhiteSpace(deliveryRefusal))
                                Debug.LogWarning($"[SawmillProduction] Opening provision endowment refused: {deliveryRefusal}", this);
                            else if (!millBusiness.RuntimeState.RecordAppliedScenarioEndowment(provisionEndowmentId))
                                Debug.LogWarning($"[SawmillProduction] Could not record opening provision endowment '{provisionEndowmentId}'.", this);
                        }
                    }
                }
            }

            // Install the mill axe via the mill-typed installer (note
            // 110 C6): assets from the mill business key's
            // opening-equipment entry, never the slice fixture id. The
            // axe is mill equipment, not camp equipment, so it installs
            // whether or not any stand (and camp) exists.
            if (millBusiness.RuntimeState != null)
            {
                systemsHub ??= FindAnyObjectByType<SimulationSystemsHub>();
                if (systemsHub == null)
                {
                    Debug.LogWarning("[SawmillProduction] Mill axe was not installed: SimulationSystemsHub is missing.", this);
                    return;
                }
                BusinessEquipmentRegister millRegister = systemsHub.EquipmentRegisterFor(millBusiness.InstanceId);
                var axeDiag = new List<string>();
                if (!ScenarioMillOpeningEquipmentEndowment.TryApply(configuration, millKey,
                        millBusiness.InstanceId, millBusiness.RuntimeState, millRegister,
                        dayIndex, axeDiag, out string axeReport, out string axeError))
                {
                    Debug.LogError($"[SawmillProduction] {axeError}", this);
                    return;
                }
                if (!pass9.EquipmentRegisters.Exists(existing => existing != null
                        && string.Equals(existing.BusinessInstanceId, millBusiness.InstanceId, StringComparison.Ordinal)))
                    pass9.EquipmentRegisters.Add(millRegister);
                Debug.Log($"[SawmillProduction] {axeReport}", this);
                foreach (string line in axeDiag) Debug.Log($"[SawmillProduction] {line}", this);

                // C-5 (expanded campaign): receive the scenario-authored
                // opening spare blade(s) into the mill's blade inventory,
                // right after the equipment endowment — so the filer loop's
                // spare-fitting branch has a real spare when the fitted
                // blade retires. A refusal warns (the fitted blade is still
                // stamped BOOTSTRAP when blank) and the station wiring below
                // proceeds; sawing stays gated only when a blade actually
                // retires with no spare on hand.
                if (pass9.Sawmill != null)
                {
                    var spareDiag = new List<string>();
                    if (!ScenarioMillOpeningSpareBladeEndowment.TryApply(configuration, millKey,
                            millBusiness.InstanceId, millBusiness.RuntimeState, pass9.Sawmill,
                            dayIndex, spareDiag, out string spareReport, out string spareError))
                    {
                        Debug.LogWarning($"[SawmillProduction] {spareError}", this);
                    }
                    else
                    {
                        Debug.Log($"[SawmillProduction] {spareReport}", this);
                        foreach (string line in spareDiag) Debug.Log($"[SawmillProduction] {line}", this);
                    }
                }
                else
                {
                    Debug.LogWarning("[SawmillProduction] Opening spare blades were not received: no live mill runtime is registered in the Pass 9 state.", this);
                }

                // C-3 (expanded campaign): establish the mill's saw-line
                // workstation in the hub-owned workstation registry, from
                // the register the endowment just filled (the endowment
                // heals a pre-C-3 save, so the saw-line components are
                // present). Steam motive power (OD-C6, Canon §8.5A). The
                // station is re-derived on every boot, so no save seam is
                // needed; the mill's support context (saw-filing + the
                // opening lubrication store) is registered beside it.
                BusinessWorkstations millStations = systemsHub.WorkstationRegisterFor(millBusiness.InstanceId);
                var stationDiag = new List<string>();
                if (!SawmillWorkstationEstablishment.TryEstablishStation(millStations, millRegister,
                        MotivePowerKind.Steam, stationDiag, out string stationReport, out string stationError))
                {
                    Debug.LogError($"[SawmillProduction] {stationError}", this);
                }
                else Debug.Log($"[SawmillProduction] {stationReport}", this);
                foreach (string line in stationDiag) Debug.Log($"[SawmillProduction] {line}", this);
                SupportContext millSupport = systemsHub.SupportContextFor(millBusiness.InstanceId);
                SawmillWorkstationEstablishment.RegisterMillSupport(millSupport);
                // C-5 (expanded campaign): the support context has no save
                // seam of its own, so sync its lubrication key FROM the
                // mill runtime's persisted field on every boot — otherwise a
                // Continue resets a drawn-down store to the opening 30.
                // RegisterMillSupport above only sets the key when absent.
                if (pass9.Sawmill != null)
                {
                    var lubricationDiag = new List<string>();
                    string lubricationSyncRefusal = SawmillLubrication.SyncSupportFromMill(pass9.Sawmill, millSupport, lubricationDiag);
                    if (!string.IsNullOrWhiteSpace(lubricationSyncRefusal))
                        Debug.LogWarning($"[SawmillProduction] {lubricationSyncRefusal}", this);
                    foreach (string line in lubricationDiag) Debug.Log($"[SawmillProduction] {line}", this);
                }
            }
        }
#endif

        // F7 yard-transfer wiring: constructs the sawmill-to-yard transfer
        // driver and registers it via the SimulationDrivers forwarder.
        // Reuse, never replace (the mill rule in
        // ConfigureLiveSawmillSaveOwner): a second call in the same session
        // keeps the existing instance, so a restored job record is never
        // discarded by re-construction. The driver needs no equipment gate,
        // no TaskAuthority, no registry, and no Pass 9 slot of its own —
        // the mill and yard runtimes already have theirs.
        private void ConfigureSawmillYardTransferWiring(BusinessInstanceState millBusiness, BusinessInstanceState yardBusiness, Series2Pass9SaveState pass9)
        {
            if (millBusiness == null || string.IsNullOrWhiteSpace(millBusiness.InstanceId) || yardBusiness == null || string.IsNullOrWhiteSpace(yardBusiness.InstanceId) || pass9 == null)
            {
                Debug.LogWarning("[SawmillToYardTransfer] Wiring was not created: no authored Sawmill or LumberYard business instance, or no Pass 9 state.", this);
                return;
            }

            systemsHub ??= FindAnyObjectByType<SimulationSystemsHub>();
            if (systemsHub == null)
            {
                Debug.LogWarning("[SawmillToYardTransfer] Wiring was not created: SimulationSystemsHub is missing.", this);
                return;
            }

            SimulationDrivers drivers = FindAnyObjectByType<SimulationDrivers>();
            if (drivers == null)
            {
                Debug.LogWarning("[SawmillToYardTransfer] Wiring was not created: SimulationDrivers is missing.", this);
                return;
            }

            SawmillShopRuntime liveMillRuntime = systemsHub.SawmillRuntimeFor(millBusiness.InstanceId);
            if (liveMillRuntime == null)
            {
                Debug.LogWarning("[SawmillToYardTransfer] Wiring was not created: hub returned no mill runtime.", this);
                return;
            }

            LumberYardShopRuntime liveYardRuntime = systemsHub.LumberYardRuntimeFor(yardBusiness.InstanceId);
            if (liveYardRuntime == null)
            {
                Debug.LogWarning("[SawmillToYardTransfer] Wiring was not created: hub returned no yard runtime.", this);
                return;
            }

            SawmillToYardTransferDriver driver = systemsHub.SawmillToYardTransferDriver;
            if (driver == null)
            {
                driver = new SawmillToYardTransferDriver(liveMillRuntime, liveYardRuntime, new List<string>());
            }
            drivers.RegisterSawmillToYardTransferDriver(driver);

            Debug.Log($"[SawmillToYardTransfer] Bootstrap wired the sawmill-to-yard transfer driver for '{millBusiness.InstanceId}' to yard '{yardBusiness.InstanceId}'.", this);
        }

        // C3: mirrors ConfigureLiveLumberYardSaveOwner for the construction
        // bindings. Binds the C1 price port into the NPC construction
        // driver (narrow setter — the field only) and registers the
        // hub-owned yard in the supplier directory, so the driver can
        // price materials and DiscoverSupplier can find the yard. First
        // NPC yard only (C2 rule); a second yard is warned about by
        // ConfigureLiveLumberYardSaveOwner and gets nothing here.
        // No sale adapter is constructed (C4 owns it); no procurement
        // port is bound (binding one that cannot procure would regress
        // the driver). Called at NewGame and in the restore window,
        // immediately after ConfigureLiveLumberYardSaveOwner.
        private void ConfigureLumberYardConstructionBindings()
        {
            if (sharedBusinessRuntime == null)
            {
                Debug.LogWarning("[LumberYardConstruction] Bindings were not created: shared business runtime is missing.", this);
                return;
            }

            systemsHub ??= FindAnyObjectByType<SimulationSystemsHub>();
            if (systemsHub == null)
            {
                Debug.LogWarning("[LumberYardConstruction] Bindings were not created: SimulationSystemsHub is missing.", this);
                return;
            }

            BusinessInstanceState yardBusiness = FindDevelopmentBusiness(BusinessType.LumberYard);
            if (yardBusiness == null || string.IsNullOrWhiteSpace(yardBusiness.InstanceId))
            {
                Debug.LogWarning("[LumberYardConstruction] Bindings were not created: no authored LumberYard business instance is present in the shared runtime.", this);
                return;
            }

            // Idempotent: the hub accessor returns the SAME live instance
            // C2's owner setup created, never a second runtime.
            LumberYardShopRuntime liveYardRuntime = systemsHub.LumberYardRuntimeFor(yardBusiness.InstanceId);
            if (liveYardRuntime == null)
            {
                Debug.LogWarning("[LumberYardConstruction] Bindings were not created: hub returned no runtime.", this);
                return;
            }

            SimulationDrivers drivers = FindAnyObjectByType<SimulationDrivers>();
            if (drivers == null)
            {
                Debug.LogWarning("[LumberYardConstruction] Bindings were not created: SimulationDrivers is missing.", this);
                return;
            }

            var port = new LumberYardConstructionPricePort(liveYardRuntime);
            drivers.BindConstructionPricePort(port);
            drivers.RegisterLumberYardConstructionSuppliers(liveYardRuntime, port);
            BindSettlerCabinDesign(drivers);
            Debug.Log($"[LumberYardConstruction] Bound the construction price port and registered the yard supplier for '{yardBusiness.RuntimeDisplayName}' ({yardBusiness.InstanceId}).", this);
        }

        /// <summary>
        /// DEF-011 (Decision 7): registers the one production design the
        /// NPC construction trigger can select — a settler cabin in lumber
        /// only, the only material the production price port can price and
        /// the yard can supply. Framing is split across two phases so each
        /// package ships inside the 400-unit single-trip bound (volume
        /// binds first: 4 m3 / 0.01 m3 per unit); the total material is the
        /// same 600 lumber the test cabin frames with, plus a 40-lumber
        /// sill phase — real construction proceeds in stages with materials
        /// arriving per stage, and mud-sill cabins were built on wooden
        /// sills laid on grade. A fieldstone foundation phase is a
        /// follow-up: it needs a real fieldstone supplier and price, and
        /// neither may be invented. Idempotent: registering twice refuses
        /// the duplicate and the existing design stands.
        /// </summary>
        private void BindSettlerCabinDesign(SimulationDrivers drivers)
        {
            var catalog = new BuildingDesignCatalog();
            var design = new BuildingDesign
            {
                DesignId = "settler-cabin",
                DisplayName = "Settler cabin",
                WorksKind = ConstructionWorksKind.NewBuilding,
                KindLabel = "cabin",
            };
            var sills = new BuildingDesignPhase
            {
                PhaseName = "Sills", Sequence = 0, LaborMinutes = 120,
                Notes = "sill logs laid on grade",
            };
            sills.Materials.Add(new ConstructionMaterialRequirement
            {
                RequirementId = "REQ-sill-lumber",
                MaterialKind = ConstructionMaterialKind.Lumber,
                MaterialDisplayName = "lumber",
                RequiredUnits = 40,
                UnitLabel = "board feet",
                ProvenanceKind = ConstructionMaterialProvenanceKind.AnyRealSource,
                SupplierBusinessId = "lumber-yard",
                SupplierDisplayName = "Lumber Yard",
            });
            sills.RequiredToolItemIds.Add("saw");
            var walls = new BuildingDesignPhase
            {
                PhaseName = "Framing - walls", Sequence = 1, LaborMinutes = 240,
                Notes = "log walls",
            };
            walls.Materials.Add(new ConstructionMaterialRequirement
            {
                RequirementId = "REQ-wall-lumber",
                MaterialKind = ConstructionMaterialKind.Lumber,
                MaterialDisplayName = "lumber",
                RequiredUnits = 300,
                UnitLabel = "board feet",
                ProvenanceKind = ConstructionMaterialProvenanceKind.AnyRealSource,
                SupplierBusinessId = "lumber-yard",
                SupplierDisplayName = "Lumber Yard",
            });
            walls.RequiredToolItemIds.Add("saw");
            var roof = new BuildingDesignPhase
            {
                PhaseName = "Framing - roof", Sequence = 2, LaborMinutes = 240,
                Notes = "log roof",
            };
            roof.Materials.Add(new ConstructionMaterialRequirement
            {
                RequirementId = "REQ-roof-lumber",
                MaterialKind = ConstructionMaterialKind.Lumber,
                MaterialDisplayName = "lumber",
                RequiredUnits = 300,
                UnitLabel = "board feet",
                ProvenanceKind = ConstructionMaterialProvenanceKind.AnyRealSource,
                SupplierBusinessId = "lumber-yard",
                SupplierDisplayName = "Lumber Yard",
            });
            roof.RequiredToolItemIds.Add("saw");
            design.Phases.Add(sills);
            design.Phases.Add(walls);
            design.Phases.Add(roof);
            design.SpaceSpecs.Add(new BuildingDesignSpaceSpec { Label = "main room", SleepingCapacity = 4 });
            var diag = new List<string>();
            string refusal = catalog.RegisterDesign(design, diag);
            if (refusal != null)
            {
                Debug.LogWarning($"[LumberYardConstruction] Settler-cabin design not registered: {refusal}", this);
                return;
            }
            drivers.BindConstructionDesignCatalog(catalog);
            Debug.Log("[LumberYardConstruction] Registered the settler-cabin design for NPC construction commissioning.", this);
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

            const string source = "DevelopmentTownConfiguration NewGame opening allocation";
            const string reason = "One-time scenario-authored starting cash balance for development testing liquidity";

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

        /// <summary>
        /// DEF-013 (Decision 9): opening-economy audit, diagnostic only. Runs
        /// after every New Game endowment and the player stake are seeded, and
        /// verifies the authored opening figures equal the actual balances:
        /// business cash targets, household cash targets, owner stake, and
        /// zero liabilities. Logs each mismatch; never corrects anything.
        /// The horse-feed order placed earlier is CashOnArrival, so no
        /// payment has left the store yet at this point.
        /// </summary>
        private void RunOpeningEconomyAudit()
        {
            DevelopmentTownConfiguration configuration = ResolveDevelopmentTownConfiguration();
            if (startupIntent != WorldStartupIntent.NewGame || !enableDevelopmentTownConfiguration || configuration == null)
                return;

            if (sharedBusinessRuntime?.Businesses == null || populationManager?.State == null
                || systemsHub?.HouseholdLedgers == null || playerPortfolioManager == null)
            {
                Debug.LogError("[OpeningEconomyAudit] Audit refused: business, population, household ledger, or portfolio authority is missing.", this);
                return;
            }

            // Same business matching as ApplyDevelopmentTownOpeningCash above:
            // by operation type plus the authored owner's runtime person id.
            var businessesByKey = new Dictionary<string, BusinessInstanceState>(StringComparer.OrdinalIgnoreCase);
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
            }

            var authored = new OpeningEconomyAudit.AuthoredOpening();
            var actual = new OpeningEconomyAudit.ActualOpening();
            foreach (DevelopmentTownConfiguration.BusinessCashTarget target in configuration.BusinessCashTargets)
            {
                authored.BusinessCashTargets[target.businessKey] = target.targetCashCents;
                if (businessesByKey.TryGetValue(target.businessKey, out BusinessInstanceState business))
                {
                    actual.BusinessCash[target.businessKey] = business.RuntimeState.CurrentCashCents;
                }
            }

            foreach (DevelopmentTownConfiguration.HouseholdCashTarget target in configuration.HouseholdCashTargets)
            {
                authored.HouseholdCashTargets[target.anchorPersonKey] = target.targetCashCents;
                int personId = configuration.ResolveRuntimePersonId(target.anchorPersonKey);
                PersonState person = populationManager.State.GetPerson(personId);
                HouseholdLedger ledger = person != null && person.householdId >= 0
                    ? systemsHub.HouseholdLedgers.Get(person.householdId)
                    : null;
                if (ledger != null)
                {
                    actual.HouseholdCash[target.anchorPersonKey] = ledger.GetBalanceCents();
                }
            }

            ScenarioDirector director = FindAnyObjectByType<ScenarioDirector>();
            authored.OwnerStakeCents = ResolveStartingStakeCents(
                director != null ? director.GetTunable("startingCashCents") : null,
                fallbackStartingCashCents);
            actual.OwnerCashCents = playerPortfolioManager.OwnerCashCents;

            // The dev scenario authors no debts, pantry lots, or lenders at
            // opening, so the authored liability is zero.
            authored.AuthoredLiabilityCents = 0;
            actual.LiabilityCents = 0;

            List<string> diagnostics = OpeningEconomyAudit.Check(authored, actual);
            if (diagnostics.Count == 0)
            {
                Debug.Log("[OpeningEconomyAudit] Opening economy matches the authored figures.", this);
                return;
            }

            foreach (string diagnostic in diagnostics)
            {
                Debug.LogError($"[OpeningEconomyAudit] {diagnostic}", this);
            }
        }
#endif

        /// <summary>Plan 04 Slice 5: notions procurement for the bakery,
        /// production wiring — NOT a playtest registration. Runs on NewGame
        /// in every build (see the ungated call in InitializeSlice). Terms
        /// are read from the catalog's authored registration
        /// (BakeryFlourSupply.EnsureBakeryImportables: "Off-map provision
        /// wholesaler, via railhead", 250 mi, 14 transit days, 10c/unit) —
        /// never hardcoded here. Supply is a finite external order of
        /// BakeryNotionsDailySupply.FiniteExternalOrderUnits units — the
        /// authored bootstrap endowment size, NOT canon. This is the
        /// INITIAL counterparty supply, not a lifetime cap: DEF-002 gives
        /// the production wholesaler a standing term, so the daily driver
        /// restocks the counterparty supply by the order quantity before
        /// each order (BakeryNotionsDailySupply, step (c)). No freight
        /// surcharge: no freight rate is authored for the railhead route
        /// (Kennedy must approve a production freight rate before one is
        /// wired). No order is placed here — the daily driver orders the
        /// day's shortfall, so the pipeline primes from the first planned
        /// bake.
        /// TWO REGISTRATION SITES, ONE PER BUILD FLAVOR: in
        /// editor/development builds ConfigurePlaytestHorseFeed below
        /// already registered this same counterparty on the shared feed
        /// market and owns the save slot — this method detects that
        /// (PassXlProcurementService non-null) and returns WITHOUT
        /// overwriting, because AttachStoreFeedProcurement overwrites
        /// unconditionally and the dev service also carries the horse-feed
        /// order. In release builds this is the only registration. Campaign
        /// B (DEF-008): the release branch below also registers the
        /// Millbrook feed counterparty, the household catalog item, and
        /// the store feed registry item (RegisterReleaseHorseFeed), so
        /// release builds get the same feed loop dev builds have. Do NOT
        /// "simplify" by deleting either site: deleting the dev half would
        /// strip the counterparty from the shared dev service, and deleting
        /// this method would leave release builds with no notions service
        /// at all (F19).</summary>
        private void RegisterNotionsProcurement(BusinessInstanceState storeBusiness, SimulationDrivers drivers)
        {
            if (drivers == null || storeBusiness?.RuntimeState == null)
            {
                Debug.LogError("[NotionsSupply] notions procurement not registered: no drivers or no store runtime.", this);
                return;
            }
            // The development feed registration already owns a procurement
            // service (with this same notions counterparty on its market) —
            // never overwrite it. AutoWire guarantees saveLoadManager is
            // non-null before this point, so the save slot is the reliable
            // signal.
            if (saveLoadManager != null && saveLoadManager.PassXlProcurementService != null)
            {
                Debug.Log("[NotionsSupply] procurement service already registered (development feed registration owns the slot); notions counterparty already on its market, nothing replaced.", this);
                return;
            }

            var market = new ExternalMarketAuthority();
            var notionsImportDiag = new List<string>();
            LandLedgers.Economy.Businesses.Bakery.BakeryFlourSupply.EnsureBakeryImportables(notionsImportDiag);
            LandLedgers.Economy.Trade.ImportMaterial notionsMaterial =
                LandLedgers.Economy.Trade.ImportCatalog.Get(
                    LandLedgers.Economy.Businesses.Bakery.BakeryFlourSupply.NotionsMaterialId);
            if (notionsMaterial == null)
            {
                Debug.LogError("[NotionsSupply] notions import material is not registered; no notions counterparty registered.", this);
                return;
            }
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
                return;
            }
            Debug.Log($"[NotionsSupply] '{notionsCounterparty.DisplayName}' registered for SKU " +
                $"'{LandLedgers.Economy.Businesses.Bakery.BakeryFlourSupply.NotionsMaterialId}' at {notionsMaterial.PricePerUnitCents}c/unit, " +
                $"transit {notionsCounterparty.TransitDays} days, finite supply " +
                $"{LandLedgers.Economy.BreadDrivers.BakeryNotionsDailySupply.FiniteExternalOrderUnits}u; " +
                "the daily driver orders the day's shortfall (BakeryNotionsDailySupply).", this);

            // Campaign B (DEF-008): release builds never run
            // ConfigurePlaytestHorseFeed, so the Millbrook feed
            // counterparty — and the retail registrations the store needs
            // to sell the feed it orders — are registered here, on the
            // same opening terms, on this same market. A failure is logged
            // loudly and the notions service below still attaches; feed
            // simply stays unavailable until the named gap is fixed, and
            // notions never regresses.
            if (!RegisterReleaseHorseFeed(market, storeBusiness, drivers))
                Debug.LogWarning("[FeedSupply] release horse-feed registration incomplete; the standing feed reorder will refuse until the named gap is fixed. Notions procurement is unaffected.", this);

            var procurement = new OffMapNamedSkuProcurementService(
                storeBusiness.InstanceId, market,
                new Series2BusinessRuntimeCashPort(storeBusiness.RuntimeState));
            drivers.AttachStoreFeedProcurement(procurement, storeBusiness);
            if (saveLoadManager != null)
            {
                if (saveLoadManager.PassXlProcurementService == null)
                    saveLoadManager.PassXlProcurementService = procurement;
                else if (!ReferenceEquals(saveLoadManager.PassXlProcurementService, procurement))
                    Debug.LogWarning("[NotionsSupply] SaveLoadManager already owns a different PassXl procurement service; notions procurement was not substituted into that slot.", this);
            }
        }

        /// <summary>
        /// Campaign B (DEF-008): the release-build half of the horse-feed
        /// registration. ConfigurePlaytestHorseFeed (dev-only) registers
        /// three things the feed loop needs — the Millbrook counterparty
        /// on the procurement market, the household catalog item, and the
        /// store feed registry item the trading port offers from. Release
        /// builds run none of that, so the standing reorder refused loudly
        /// instead of ordering from nobody. This registers the same three
        /// things here, on the same opening terms single-sourced on
        /// StoreHorseFeedReorder, on the notions market (both
        /// counterparties share one market and one service, exactly as in
        /// dev). Returns false — after logging the precise failure loudly
        /// — when any step fails; the caller still attaches the notions
        /// service, so a feed gap never breaks notions. No opening order
        /// is placed here: with the counterparty registered, the standing
        /// reorder self-primes on its first daily drive (coverage 0, no
        /// outstanding order), so the first lot arrives one day later
        /// than the dev opening order at most.</summary>
        private bool RegisterReleaseHorseFeed(
            ExternalMarketAuthority market, BusinessInstanceState storeBusiness, SimulationDrivers drivers)
        {
            if (market == null || drivers == null || storeBusiness?.RuntimeState == null)
            {
                Debug.LogError("[FeedSupply] release horse-feed registration refused: no market, drivers, or store runtime.", this);
                return false;
            }
            if (review2D3SaveState == null)
            {
                Debug.LogError("[FeedSupply] release horse-feed registration refused: no Review2 D3 save state for the household catalog item.", this);
                return false;
            }
            var catalogDiag = new List<string>();
            string catalogRefusal = review2D3SaveState.RegisterRuntimeItem(
                StoreHorseFeedReorder.BuildFeedCatalogItem(), catalogDiag);
            if (catalogRefusal != null)
            {
                Debug.LogError($"[FeedSupply] release horse-feed registration refused: household catalog item refused: {catalogRefusal}", this);
                return false;
            }
            var feedDiag = new List<string>();
            string feedRefusal = drivers.RegisterStoreFeedItem(
                StoreHorseFeedReorder.BuildFeedRegistryItem(storeBusiness.InstanceId), feedDiag);
            if (feedRefusal != null)
            {
                Debug.LogError($"[FeedSupply] release horse-feed registration refused: store feed item refused: {feedRefusal}", this);
                return false;
            }
            string marketRefusal = market.RegisterCounterparty(
                StoreHorseFeedReorder.BuildMillbrookCounterparty());
            if (marketRefusal != null)
            {
                Debug.LogError($"[FeedSupply] release horse-feed registration refused: Millbrook counterparty refused: {marketRefusal}", this);
                return false;
            }
            Debug.Log("[FeedSupply] Millbrook Grain Co. feed counterparty registered in the release build on opening terms (20 sacks at 15c/sack, 1-day transit, cash on arrival); household catalog item and store feed item registered; the standing reorder self-primes on its first daily drive.", this);
            return true;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void ConfigurePlaytestHorseFeed(BusinessInstanceState storeBusiness, SimulationDrivers drivers)
        {
            const string itemId = "store_horse_feed_sack";
            // Part B maintenance (2026-11-15): the opening feed terms are
            // single-sourced on StoreHorseFeedReorder, which reorders on
            // these same terms; changing them there changes both the
            // opening order below and every standing reorder. Campaign B
            // (DEF-008): the catalog item, registry item, and counterparty
            // below are built by StoreHorseFeedReorder builders so the
            // release registration in RegisterNotionsProcurement uses the
            // identical terms.
            const string supplierId = StoreHorseFeedReorder.MillbrookSupplierId;
            var diagnostics = new List<string>();
            string itemRefusal = review2D3SaveState.RegisterRuntimeItem(
                StoreHorseFeedReorder.BuildFeedCatalogItem(), diagnostics);
            if (itemRefusal != null)
            {
                Debug.LogError($"[FeedSupply] household catalog registration refused: {itemRefusal}", this);
                return;
            }

            string registrationRefusal = drivers.RegisterStoreFeedItem(
                StoreHorseFeedReorder.BuildFeedRegistryItem(storeBusiness.InstanceId), diagnostics);
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
            var counterparty = StoreHorseFeedReorder.BuildMillbrookCounterparty();
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
            // the authored bootstrap endowment size, NOT canon. This is
            // the INITIAL counterparty supply, not a lifetime cap: DEF-002
            // gives the production wholesaler a standing term, so the
            // daily driver restocks the counterparty supply by the order
            // quantity before each order (BakeryNotionsDailySupply,
            // step (c)). No freight surcharge: no freight rate is
            // authored for the railhead route (Kennedy must approve a
            // production freight rate before one is wired). No order is
            // placed here — the daily driver orders the day's shortfall,
            // so the pipeline primes from the first planned bake.
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
                itemId, StoreHorseFeedReorder.OpeningLotSacks, StoreHorseFeedReorder.FeedPriceCentsPerSack,
                0, supplierId, dayIndex, NamedSkuPaymentMode.CashOnArrival, null);
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
