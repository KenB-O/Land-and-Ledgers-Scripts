using System;
using System.Collections.Generic;
using System.Linq;
using LandLedgers.Animals;
using LandLedgers.Economy;
using LandLedgers.Persistence;
using LandLedgers.Economy.Delegation;
using LandLedgers.Economy.Estates;
using LandLedgers.Economy.Farming.Integration;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Transport;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Postal;
using LandLedgers.Economy.Businesses.BoardingHouse;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.Creation;
using LandLedgers.Economy.Labor;
using LandLedgers.Economy.Recruitment;
using LandLedgers.Orchestration.Bootstrap;
using LandLedgers.Orchestration.Player;
using LandLedgers.Orchestration.Scenarios;
using LandLedgers.Population;
using EntityId = LandLedgers.Primitives.EntityId;
using LandLedgers.Primitives;
using LandLedgers.ReadModels.Valuation;
using LandLedgers.Skills;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using LandLedgers.World;
using LandLedgers.World.Property;
using UnityEngine;

namespace LandLedgers.Orchestration.Systems
{
    /// <summary>
    /// CLN-2: thin per-tick drivers for the simulation authorities. Kennedy attaches
    /// this to the one scene (next to the SimulationSystemsHub). The drivers own no
    /// logic — they subscribe to the TimeManager's tick events and forward to the
    /// authorities owned by the hub.
    ///
    /// What goes where:
    /// - Awake: installs the skill-based duration estimator into the TaskAuthority
    ///   (TTS-3) and hands the hub's authorities to the ScenarioDirector.
    /// - DayChanged: WorkTimeBudgetStore.EnsureDay (TTS-1 daily rollover) and
    ///   ButcherRuntime.AgeLotsToDay (BIZ-4 perishability).
    /// - ShortTick: PlayerDirector.RecordMovementProgress (whole minutes elapsed).
    /// - TaskAuthority.RecordWork + SkillService.ApplyPracticeFromTask: called from
    ///   task execution sites (not a tick) — see DRIVERS.md.
    /// - Freight/butcher AdvanceGameSeconds: shipments advance inside
    ///   LogisticsRuntimeManager and FreightCompanyRuntime (already MonoBehaviours).
    /// - Scenario goal evaluation: FirstLedgerGoalEvaluator.Evaluate runs on the
    ///   scenario tick once CLN-3 provides the live IFirstLedgerGameState.
    /// - NX-2C drive-by: DailyNeedsService.ExecuteDay runs on DayChanged with
    ///   the hub's WorkTimeBudgetStore (it had no live caller — the NX-1B
    ///   nutrition teeth never bit). Needs a PopulationManager in the scene and
    ///   a non-empty journey model (SimulationDrivers.Journeys, falling back to
    ///   the hub-owned model); supplier registration is scene wiring (T1A
    ///   pattern).
    /// - P6 travel &amp; communication daily drives: the hub's RouteConditionService
    ///   advances the weather (NX-2C closures actually close roads), PostalService
    ///   dispatches/arrives mail on real schedules (NX-2A letters actually move),
    ///   MoneyOrderService advances advices/expiry (D4E), RegisteredMailService
    ///   audits overdue items (D4F), and RecruitmentService accrues inquiries and
    ///   resolves letters — postal-routed ones only on real arrival (T2B).
    /// </summary>
    public sealed class SimulationDrivers : MonoBehaviour, IPassXlPlayerFlagHolder
    {
        private const string HorseFeedSku = "store_horse_feed_sack";
        private const string HorseFeedCategory = "animal_feed";
        private const string HorseFeedUnit = "sack";
        private const int HorseFeedUnitsPerSack = 2;
        private const int HorseFeedTriggerDays = 2;
        private const int HorseFeedTargetDays = 7;
        private const int HorseShoppingCarryLimitSacks = 50;
        private const string BusinessFeedShoppingTaskId = "business-feed-shopping";
        private const string OpeningHitchTaskId = "opening-wagon-hitch-preparation";
        private const string OpeningHitchPostId = "opening-freight-hitching-post";
        private const string BusinessFeedTaskPrefix = "business-feed-trip:";
        private const string DevelopmentHitchTaskPrefix = "development-hitch:";
        private const string FreightMeetingTaskPrefix = "freight-meeting:";
        private const string FreightMeetingTaskId = "freight-service-meeting";
        private const string FreightDispatchTaskPrefix = "freight-dispatch:";
        private const string FreightDispatchTaskId = "freight-dispatch-assignment";
        private readonly HashSet<string> businessFeedNeedsOpen = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<int> businessFeedWaitingForHomeLogged = new HashSet<int>();
        private readonly Dictionary<string, string> businessFeedLastStockRefusal = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> horseCareRiskAlreadyLogged = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> taskStartRefusalAlreadyLogged = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> freightAgreementBlockersLogged = new HashSet<string>(StringComparer.Ordinal);

        [Serializable]
        private sealed class BusinessFeedTripTaskData
        {
            public string Stage = string.Empty;
            public string BusinessId = string.Empty;
            public string StoreId = string.Empty;
            public int PersonId = -1;
            public string OriginLocationId = string.Empty;
            public string StoreLocationId = string.Empty;
            public int StoreBuildingId = -1;
            public int HomeBuildingId = -1;
            public int Sacks;
            public int DayIndex;
            public string TransactionId = string.Empty;
            public BusinessFeedPurchase.Batch Batch;
        }

        [Serializable]
        private sealed class DevelopmentHitchTaskData
        {
            public string WagonId = string.Empty;
            public string HitchTaskId = string.Empty;
            public int PersonId = -1;
            public int StagingBuildingId = -1;
            public string StagingLocationId = string.Empty;
            public int DurationMinutes;
        }

        [Serializable]
        private sealed class FreightMeetingTripTaskData
        {
            public string RequestId = string.Empty;
            public int RequestingPersonId = -1;
            public int CarrierOperatorPersonId = -1;
            public int MeetingBuildingId = -1;
            public string MeetingLocationId = string.Empty;
        }

        [Serializable]
        private sealed class FreightDispatchTaskData
        {
            public string AgreementId = string.Empty;
            public string ShipmentId = string.Empty;
            public int OperatorPersonId = -1;
            public string HorseId = string.Empty;
            public string WagonId = string.Empty;
        }

        [SerializeField, Min(1), Tooltip("Provisional development-only duration for the one-time opening wagon hitch task; not economic canon.")]
        private int developmentHitchMinutes = 10;
        [SerializeField, Min(1), Tooltip("Provisional development-only time spent in a direct freight-service meeting; not economic canon.")]
        private int freightMeetingMinutes = 5;

        [SerializeField, Tooltip("CLN-1 hub owning the authorities. Found automatically if empty.")]
        private SimulationSystemsHub hub;

        [SerializeField, Tooltip("Found automatically if empty.")]
        private TimeManager timeManager;

        [SerializeField, Tooltip("Optional: receives the hub's TaskAuthority. Found automatically if empty.")]
        private ScenarioDirector scenarioDirector;

        [SerializeField, Tooltip("Optional: driven with whole minutes per ShortTick. Found automatically if empty.")]
        private PlayerDirector playerDirector;

        // Pass XL vertical slice, playtest only: when true, the player person
        // is driven only by the NPC household/business/domestic drivers.
        // Player-issued travel, player-issued task advancement, and player
        // owner-labour accounting are skipped (see PlayerNpcPlaytestPolicy).
        // Default false: ordinary player behaviour, byte-for-byte unchanged.
        // Codex: expose this bool in the Inspector on this component.
        // Phase 2: it is also persisted in PassXlSliceSaveDto and restored
        // by SaveLoadManager on Continue (see PassXlSliceSaveCoordinator).
        // The bootstrapper playtest bool (FirstLedgerSliceBootstrapper)
        // forwards as an OR override on Continue: it can turn NPC mode on,
        // but it never clears a saved true on its own.
        [SerializeField, Tooltip("Playtest only: treat the player as a regular NPC; skip player-issued travel, player-task advancement, and player owner-labour accounting.")]
        private bool treatPlayerAsNpc;

        [SerializeField, Tooltip("Optional: weekly profit posts hook into its reset. Found automatically if empty.")]
        private SharedBusinessRuntimeManager sharedBusinessRuntime;

        // Review 2 vertical slice Pass D2 (owner-decided horse feed):
        // in-memory registry of owner-added General Store feed items
        // (Review2StoreHorseFeed). Not serialized and NOT saved by
        // this driver: Codex must persist registrations or
        // re-register them on load (see Review2StoreHorseFeed's
        // persistence gap note), or a restored world falls back to
        // null for store horses until re-registration. The owner
        // operation is RegisterStoreFeedItem below; the driver
        // consults it only as the third branch of
        // ResolveHorseFeedStock, after farm and livery.
        private readonly Review2StoreFeedRegistry storeFeedRegistry = new Review2StoreFeedRegistry();
        private Review2VerticalD3SaveState review2D3SaveState;
        private OffMapNamedSkuProcurementService storeFeedProcurement;
        private BusinessInstanceState storeFeedProcurementBusiness;
        private DevelopmentTownConfiguration developmentTownConfiguration;
        private OffMapAidBusinessRuntime millbrookAidRuntime;
        private int lastMillbrookAidAttemptDay = -1;
        private string lastMillbrookAidRefusal = string.Empty;
        public Review2StoreFeedRegistry StoreFeedRegistry => storeFeedRegistry;

        public void AttachMillbrookAidRuntime(OffMapAidBusinessRuntime runtime)
        {
            millbrookAidRuntime = runtime;
        }

        public void AttachDevelopmentTownConfiguration(DevelopmentTownConfiguration configuration)
        {
            developmentTownConfiguration = configuration;
        }

        public void AttachReview2D3SaveState(Review2VerticalD3SaveState state)
        {
            review2D3SaveState = state;
        }

        public void AttachStoreFeedProcurement(
            OffMapNamedSkuProcurementService procurement,
            BusinessInstanceState storeBusiness)
        {
            storeFeedProcurement = procurement;
            storeFeedProcurementBusiness = storeBusiness;
        }

        /// <summary>
        /// D2 owner operation, for the bootstrapper / store UI to
        /// call when a store owner adds a feed item. Delegates to
        /// Review2StoreHorseFeed.AddFeedItem; creates no stock and
        /// sets no price. Returns a diagnostic on refusal, null on
        /// success. Unity-side: Codex must verify this path in
        /// Unity, as Roslyn cannot construct this MonoBehaviour.
        /// </summary>
        public string RegisterStoreFeedItem(Review2StoreFeedItem item, List<string> diag)
        {
            if (sharedBusinessRuntime == null) return "RegisterStoreFeedItem: no shared business runtime — nothing added.";
            if (item == null) return "RegisterStoreFeedItem: no feed item — nothing added.";
            if (!sharedBusinessRuntime.TryFindByInstanceId(item.BusinessInstanceId, out BusinessInstanceState business)
                || business?.RuntimeState == null)
            {
                return $"RegisterStoreFeedItem: no live runtime for store '{item.BusinessInstanceId}' — nothing added.";
            }

            if (business.BusinessType != BusinessType.GeneralStore)
            {
                return $"RegisterStoreFeedItem: business '{item.BusinessInstanceId}' is a {business.BusinessType}, not a General Store — feed items are store items, nothing added.";
            }

            return Review2StoreHorseFeed.AddFeedItem(business.RuntimeState, storeFeedRegistry, item, diag);
        }

        [SerializeField, Tooltip("Optional: drives DailyNeedsService. Found automatically if empty.")]
        private PopulationManager populationManager;

        [SerializeField, Tooltip("Town grid used to derive real journeys from road cells and building access anchors.")]
        private TownWorldController townWorld;

        /// <summary>
        /// NX-2C: the journey model for embodied needs execution. Assigned by
        /// scene bootstrap (like OnScenarioTick) — without it, daily needs
        /// cannot execute embodied purchases and are skipped with a warning.
        /// </summary>
        public JourneyModel Journeys { get; set; }

        // NX-2C drive-by: daily-needs execution state (owned by the driver).
        private DailyNeedsService dailyNeedsService;
        private HouseholdConsumptionPlanner consumptionPlanner;
        private SupplierDirectory supplierDirectory;
        private EmbodiedPurchaseExecutor purchaseExecutor;
        private HouseholdMembershipRegistry householdMemberships;
        private ShopperSellerKnowledge sellerKnowledge = new ShopperSellerKnowledge();
        private readonly Dictionary<string, LaborDemandReport> latestDemandByBusiness =
            new Dictionary<string, LaborDemandReport>(StringComparer.Ordinal);
        private bool managerExecutionConfigured;
        private HouseholdShoppingLoop householdShoppingLoop;
        private readonly LiveTenancyPayeeDirectory tenancyPayees = new LiveTenancyPayeeDirectory();
        private readonly Dictionary<EntityId, string> autonomousTaskReservations = new Dictionary<EntityId, string>();
        private bool subscribedToTimeManager;
        private int lastTownJourneyWorldSignature = int.MinValue;

        /// <summary>
        /// Pass XII Worker A (LL-PLAY-002/003): true while this instance
        /// holds the SingleInstanceAuthorityGuard claim for
        /// "SimulationDrivers". A duplicate drivers object never holds the
        /// claim, never subscribes to the clock, and never wires a business
        /// runtime, so two instances cannot both drive the same economy.
        /// Released on disable/destroy so a re-enabled instance can reclaim.
        /// </summary>
        private bool hasAuthorityClaim;

        // G-01: weekly source-side driver for NPC business lifecycles (owned by the driver).
        private NpcBusinessLifecycleDriver npcBusinessLifecycleDriver;

        // Pass IX (WS2): production wiring state for the people/lifecycle
        // drivers. The attendance accumulator and the social/estate drives
        // are plain C# (harness-tested); this MonoBehaviour only forwards
        // the hub's real authorities into them on the tick/day boundaries.
        // The accumulator is hub-owned (it rides the systems save DTO);
        // the local fallback only covers the pre-hub bootstrap window.
        private readonly WeeklyAttendanceAccumulator fallbackWeeklyAttendance = new WeeklyAttendanceAccumulator();
        private WeeklyAttendanceAccumulator weeklyAttendance =>
            hub != null ? hub.WeeklyAttendance : fallbackWeeklyAttendance;
        private readonly DailyActivityPlanner dailyActivityPlanner = new DailyActivityPlanner();
        private readonly FamilyLaborLedger domesticFamilyLabor = new FamilyLaborLedger();
        private readonly EstateAdministrationService estateAdministration = new EstateAdministrationService();
        private readonly ProbateService estateProbate = new ProbateService();
        private readonly WillContestService estateContests = new WillContestService();
        private EstateLifecycleDriver estateLifecycleDriver;
        private OffMapJourneyAuthority offMapJourneys;
        // Pass XI WS1 (PASS9-006): watermark of the last game-second value
        // forwarded to DomesticLaborDriver.AdvanceTick from OnShortTick.
        // Held in DomesticTickGate so the save/load hook can reset it
        // explicitly; the gate holds only the float, never driver state.
        private readonly DomesticTickGate domesticTickGate = new DomesticTickGate();

        /// <summary>
        /// Pass XI WS1: scenario id warned about once for objective
        /// evaluation mismatch, so a miswired start cannot spam the log
        /// once per day.
        /// </summary>
        private string scenarioEvaluateWarnedForId;

        /// <summary>
        /// Phase C: registers the REAL general store as an <see cref="IGoodsSupplier"/>
        /// in the shared <see cref="SupplierDirectory"/>, so the embodied
        /// shopping path is the one path (C5). Scene wiring calls this with a
        /// <see cref="GeneralStoreTradingPort"/> built over the live
        /// <see cref="GeneralStoreRuntimeManager"/> and the scenario's authored
        /// category offers (Codex integrates; the store's stocking is authored,
        /// never invented here).
        /// </summary>
        public void RegisterGeneralStoreSupplier(
            LandLedgers.Economy.Businesses.GeneralStore.IGeneralStoreTradingPort storePort)
        {
            supplierDirectory ??= new SupplierDirectory();
            if (hub != null)
            {
                HouseholdShoppingLoop.RegisterTaskDefinitions(hub.Tasks);
            }
            LandLedgers.Economy.Businesses.GeneralStore.GeneralStoreSupplierAdapter.RegisterIn(
                supplierDirectory, storePort);
            SeedLocalGeneralStoreKnowledge();
            BuildHouseholdShoppingLoop();
        }

        /// <summary>Attaches the scenario's real membership authority for the production shopping loop.</summary>
        public void AttachHouseholdMemberships(HouseholdMembershipRegistry memberships, ShopperSellerKnowledge knowledge = null)
        {
            householdMemberships = memberships;
            if (knowledge != null) sellerKnowledge = knowledge;
            BuildHouseholdShoppingLoop();
        }

        /// <summary>
        /// Returns the production construction-material adapter for a real
        /// commissioning household. The adapter is intentionally scoped to
        /// that household's existing inventory; it does not create stock.
        /// </summary>
        public IConstructionMaterialSource CreateHouseholdConstructionMaterialSource(int householdId)
        {
            if (hub == null) return null;
            HouseholdInventory inventory = hub.HouseholdInventories.Get(householdId);
            return inventory == null ? null : new HouseholdConstructionMaterialSource(inventory);
        }

        /// <summary>Returns the production tool-custody adapter over hub inventories.</summary>
        public IHouseholdToolCustody CreateHouseholdToolCustody()
        {
            return hub == null ? null : new HouseholdInventoryToolCustody(hub.HouseholdInventories);
        }

        // Pass XI WS2 (A-03, RUN-002): NPC construction ports. The driver
        // itself lives in NpcConstructionDriver and runs daily from
        // OnDayChanged; these bindings are the production path's inputs.
        // The labor leg is fully wired below from hub authorities. The
        // catalog, price port, material source and parcel authority have
        // no production implementation yet: no catalog asset is registered
        // in source, no yard/sawmill lot adapter exists (only the
        // household-inventory source adapter above, which fits self-supply
        // but not the commissioner's supplier-payment leg), and no parcel
        // authority adapter reads the real TitleAuthority. Until those
        // adapters are bound, the driver advances already-staged packages
        // and waits loudly on Ready packages and on completion, which
        // needs the catalog design. Nothing here invents a port.
        private BuildingDesignCatalog npcConstructionCatalog;
        private IConstructionMaterialPricePort npcConstructionPricePort;
        private IConstructionMaterialSource npcConstructionMaterialSource;
        private IConstructionParcelAuthority npcConstructionParcelAuthority;
        private INpcConstructionProcurementPort npcConstructionProcurement;

        /// <summary>
        /// Binds the NPC construction ports the production driver needs.
        /// Any argument may be null; an unbound port makes the driver wait
        /// loudly on the step that needs it instead of inventing input.
        /// Safe to call twice: the latest binding wins.
        /// </summary>
        public void BindNpcConstructionPorts(
            BuildingDesignCatalog catalog,
            IConstructionMaterialPricePort pricePort,
            IConstructionMaterialSource materialSource,
            IConstructionParcelAuthority parcelAuthority,
            INpcConstructionProcurementPort procurement)
        {
            npcConstructionCatalog = catalog;
            npcConstructionPricePort = pricePort;
            npcConstructionMaterialSource = materialSource;
            npcConstructionParcelAuthority = parcelAuthority;
            npcConstructionProcurement = procurement;
        }

        /// <summary>
        /// Pass XI WS2 (A-06): binds a player-owned bank's deposit ledger
        /// through the hub-owned binding registry. One ledger per bank,
        /// fail-loud on a second distinct ledger; the scene hookup that
        /// supplies the ledger object is outstanding under G4, so this
        /// method has no production caller yet — it is the binding point,
        /// not a binding claim.
        /// </summary>
        public string BindBankLedger(string businessInstanceId, BankDepositLedger ledger, List<string> diag)
        {
            if (hub == null) return "BindBankLedger: no hub — the binding registry is unavailable.";
            return hub.BankLedgerBindings.BindBankLedger(businessInstanceId, ledger, diag);
        }

        /// <summary>
        /// Queues one explicit NPC construction commission for the driver.
        /// The driver commissions only queued requests, never on its own;
        /// which households commission, on what evidence, is a design
        /// decision owned outside this driver, so no automatic trigger
        /// queues here.
        /// </summary>
        public void QueueNpcConstructionRequest(NpcConstructionRequest request)
        {
            if (request != null) npcConstructionRequests.Add(request);
        }

        private readonly List<NpcConstructionRequest> npcConstructionRequests = new List<NpcConstructionRequest>();

        /// <summary>
        /// Pass XI WS2: the daily NPC construction drive. Advances hub
        /// projects through the production driver (fund/stage, labor,
        /// advance, complete, occupy, cancel via the driver API) and
        /// commissions queued requests when the parcel authority is bound.
        /// </summary>
        private void DriveNpcConstruction(int absoluteDayIndex)
        {
            if (hub == null) return;
            ConstructionProjectExecutor executor = hub.ConstructionExecutor;
            if (executor != null && executor.EquipmentReservations == null)
                executor.EquipmentReservations = hub.Reservations;
            var driver = new NpcConstructionDriver(
                hub.NpcConstructionCommissioner, executor, npcConstructionCatalog,
                hub.ConstructionProjects, hub.CreditCash, hub.FinancialObligations,
                hub.Ids, hub.Housing, npcConstructionPricePort,
                npcConstructionMaterialSource, null, npcConstructionProcurement,
                new DriversConstructionLaborPort(this),
                householdId => householdMemberships != null
                    ? householdMemberships.GetActiveMembers(householdId)
                    : new List<int>());
            driver.MaterialSourceByHousehold = CreateHouseholdConstructionMaterialSource;
            var diag = new List<string>();
            if (npcConstructionParcelAuthority != null)
            {
                foreach (NpcConstructionRequest request in npcConstructionRequests)
                    driver.PendingRequests.Add(request);
                npcConstructionRequests.Clear();
                driver.CommissionPending(npcConstructionParcelAuthority, absoluteDayIndex, diag);
                // Refused commissions stay queued for the next day's drive
                // (the driver keeps refused requests in its pending list).
                foreach (NpcConstructionRequest request in driver.PendingRequests)
                    npcConstructionRequests.Add(request);
            }
            else if (npcConstructionRequests.Count > 0)
            {
                diag.Add("[SimulationDrivers] npc construction: queued commissions wait — no parcel authority bound.");
            }
            driver.Drive(absoluteDayIndex, diag);
            if (diag.Count > 0)
                Debug.Log("[SimulationDrivers] npc construction day " + absoluteDayIndex + ": " +
                    string.Join(" | ", diag));
        }

        /// <summary>
        /// The driver's labor leg over hub authorities: each active member
        /// of the commissioning household is tried in turn for own labor
        /// through the commissioner, and worked minutes credit only as the
        /// bound task is actually worked. A member whose time is refused
        /// (double-booked, no tools, no budget) is skipped for the next
        /// member; a committed member whose work fails stops the package
        /// loudly rather than crediting phantom minutes.
        /// </summary>
        private sealed class DriversConstructionLaborPort : INpcConstructionLaborPort
        {
            private readonly SimulationDrivers drivers;
            public DriversConstructionLaborPort(SimulationDrivers drivers) { this.drivers = drivers; }

            public string WorkPackage(ConstructionProject project, WorkPackage package, int dayIndex, List<string> diag)
            {
                SimulationSystemsHub hub = drivers.hub;
                if (hub == null) return "no hub — labor authorities unavailable.";
                NpcConstructionRecord record = hub.NpcConstructionCommissioner.FindRecordForProject(project.ProjectId);
                IHouseholdToolCustody custody = drivers.CreateHouseholdToolCustody();
                List<int> members = drivers.householdMemberships != null
                    ? drivers.householdMemberships.GetActiveMembers(project.HouseholdId)
                    : new List<int>();
                string lastRefusal = "no active household member could work the package.";
                foreach (int personId in members)
                {
                    if (personId < 0) continue;
                    string commit = hub.NpcConstructionCommissioner.CommitOwnLabor(
                        hub.ConstructionExecutor, project, record, package, personId,
                        hub.PersonSchedules, custody, hub.Tasks, hub.WorkTimeBudgets,
                        dayIndex, 360, package.LaborMinutesRequired, diag);
                    if (commit != null) { lastRefusal = commit; continue; }
                    string taskId = null;
                    for (int i = project.LaborTaskBindings.Count - 1; i >= 0; i--)
                    {
                        LaborTaskBinding binding = project.LaborTaskBindings[i];
                        if (binding != null && string.Equals(binding.PackageId, package.PackageId, StringComparison.Ordinal))
                        {
                            taskId = binding.TaskId;
                            break;
                        }
                    }
                    if (string.IsNullOrWhiteSpace(taskId)) return "no labor task bound to the package.";
                    return hub.ConstructionExecutor.WorkCommittedLabor(project, package, taskId,
                        package.LaborMinutesRequired, hub.Tasks, hub.WorkTimeBudgets, dayIndex, diag);
                }
                return lastRefusal;
            }
        }

        /// <summary>
        /// Registers a scene-owned boarding-house runtime with the existing
        /// hub. The runtime remains the detailed occupancy/custody authority;
        /// the hub only owns its persistence and settlement scheduling.
        /// </summary>
        public void RegisterBoardingHouseRuntime(BoardingHouseShopRuntime runtime)
        {
            if (runtime == null || hub == null) return;
            if (sharedBusinessRuntime != null && sharedBusinessRuntime.TryFindByInstanceId(runtime.BusinessInstanceId, out BusinessInstanceState business))
                runtime.BindBusiness(business);
            hub.RegisterBoardingHouseRuntime(runtime);
            hub.RegisterBoardingHouseMealSource(runtime.MealDaySource);
        }

        private void BuildHouseholdShoppingLoop()
        {
            // Local public-store awareness is independent of whether the
            // shopping loop already exists. Bootstrap can construct the loop
            // before B60 registers, then register the store later.
            SeedLocalGeneralStoreKnowledge();
            if (hub == null || populationManager?.State == null || householdMemberships == null || supplierDirectory == null)
            {
                return;
            }

            JourneyModel currentJourneys = Journeys ?? hub.Journeys;
            if (householdShoppingLoop == null)
            {
                householdShoppingLoop = new HouseholdShoppingLoop(
                    populationManager.State,
                    householdMemberships,
                    hub.HouseholdLedgers,
                    hub.HouseholdInventories,
                    hub.PurchasingNeeds,
                    supplierDirectory,
                    currentJourneys,
                    hub.Tasks,
                    hub.WorkTimeBudgets,
                    hub.PersonSchedules,
                    sellerKnowledge,
                    hub.FinancialObligations,
                    hub.Ids,
                    personEntityId: null,
                    spendingPolicyProvider: householdId => hub.HouseholdDecisionDriver.GetSpendingPolicy(householdId));
            }
            else
            {
                householdShoppingLoop.RebindPopulationAuthorities(
                    populationManager.State, householdMemberships, currentJourneys);
            }

        }

        /// <summary>
        /// Seeds only residents' ordinary local awareness of registered,
        /// specifically identified General Stores. Other supplier types remain
        /// undiscovered until their normal knowledge events occur. The
        /// ShopperSellerKnowledge authority makes repeated calls idempotent.
        /// </summary>
        private void SeedLocalGeneralStoreKnowledge()
        {
            if (populationManager?.State?.people == null || supplierDirectory == null || sellerKnowledge == null)
                return;

            int dayIndex = timeManager != null ? timeManager.CurrentAbsoluteDayIndex : 0;
            SeedLocalGeneralStoreKnowledge(
                populationManager.State.people, supplierDirectory.All, sellerKnowledge, dayIndex);
        }

        /// <summary>Pure policy seam used to protect the generic placeholder id.</summary>
        public static bool IsEligibleLocalGeneralStoreSupplier(IGoodsSupplier supplier)
        {
            return supplier is LandLedgers.Economy.Businesses.GeneralStore.GeneralStoreSupplierAdapter
                && !string.IsNullOrWhiteSpace(supplier.SupplierBusinessId)
                && !string.Equals(supplier.SupplierBusinessId, "general-store", StringComparison.Ordinal);
        }

        /// <summary>Idempotent, narrowly scoped local-store knowledge policy.</summary>
        public static void SeedLocalGeneralStoreKnowledge(
            IEnumerable<PersonState> people,
            IEnumerable<IGoodsSupplier> suppliers,
            ShopperSellerKnowledge knowledge,
            int dayIndex)
        {
            if (people == null || suppliers == null || knowledge == null) return;
            foreach (PersonState person in people)
            {
                if (person == null || person.id < 0 || person.deathDayIndex >= 0 || person.isOffMap) continue;
                foreach (IGoodsSupplier supplier in suppliers)
                {
                    if (!IsEligibleLocalGeneralStoreSupplier(supplier)) continue;
                    knowledge.LearnSupplier(person.id, supplier.SupplierBusinessId,
                        "local public General Store", dayIndex);
                }
            }
        }

        /// <summary>
        /// CLN-3 hook: scenario goal evaluation runs here. Scenario bootstraps
        /// subscribe their evaluators (e.g. FirstLedgerGoalEvaluator.Evaluate
        /// with the live game state); when no subscriber is attached, the
        /// scenario tick is a no-op. P7: a multicast event, not a settable
        /// property — two scenario bootstraps in one scene must not silently
        /// steal the tick from each other (last-writer-wins dropped one
        /// evaluator's goals entirely).
        /// </summary>
        public event Action OnScenarioTick;

        /// <summary>
        /// The bootstrap owns player identity because the Person is generated with the
        /// opening population. Replacing the stale serialized placeholder here keeps
        /// daily work-time and weekly owner-labor accounting on the real Person.
        /// </summary>
        public PlayerDirector PlayerDirector
        {
            get => playerDirector;
            set
            {
                playerDirector = value;
                if (scenarioDirector != null)
                {
                    scenarioDirector.PlayerDirector = value;
                }
            }
        }

        /// <summary>
        /// Pass XL vertical slice: the player-as-NPC playtest flag. Codex
        /// exposes the serialized backing field in the Inspector; this
        /// property is the testable / bootstrapper-facing seam. Default false.
        /// When true, SimulationDrivers skips player-issued travel, player
        /// task advancement, and player owner-labour accounting, while the
        /// autonomous drivers still drive the player person as any NPC.
        /// </summary>
        public bool TreatPlayerAsNpc
        {
            get => treatPlayerAsNpc;
            set => treatPlayerAsNpc = value;
        }

        /// <summary>Testable form of the flag rule (see PlayerNpcPlaytestPolicy).</summary>
        public bool ShouldAdvancePlayerWork()
        {
            return !treatPlayerAsNpc;
        }

        /// <summary>
        /// Rebinds the live business authority after a legacy scene bootstrap has
        /// created/configured its managers. Dynamic bootstrap order can run this
        /// component's Awake before the manager finishes its dependency wiring; the
        /// explicit handoff keeps weekly valuation and employment events on the same
        /// production authority.
        /// </summary>
        public void AttachBusinessRuntime(SharedBusinessRuntimeManager runtime)
        {
            // Pass XII Worker A: a suppressed duplicate must never wire a
            // business runtime, even if bootstrap code hands it one. An
            // instance whose Awake never ran (e.g. created inactive) gets
            // one claim attempt here; only a failed claim refuses.
            if (!hasAuthorityClaim)
            {
                hasAuthorityClaim = SingleInstanceAuthorityGuard.TryClaim(
                    "SimulationDrivers", this, out string attachRefusal);
                if (!hasAuthorityClaim)
                {
                    Debug.LogError($"[SimulationDrivers] AttachBusinessRuntime refused: {attachRefusal}", this);
                    return;
                }
            }

            sharedBusinessRuntime = runtime;
            hub ??= FindAnyObjectByType<SimulationSystemsHub>();
            timeManager ??= TimeManager.Instance != null ? TimeManager.Instance : FindAnyObjectByType<TimeManager>();
            SubscribeToTimeManager();
            if (sharedBusinessRuntime == null || hub == null)
            {
                return;
            }

            sharedBusinessRuntime.PreWeeklyResetCallback = PostWeeklyProfitToValuation;
            ConfigureManagerExecution();
            sharedBusinessRuntime.EmploymentRegistry = hub.Employments;
            sharedBusinessRuntime.WireEmploymentRegistryToBusinesses();
            WireHouseholdCashAuthorities();
            WireNpcBusinessCashStores();
            BindNpcBusinessCreation(sharedBusinessRuntime);
            InstallProductionPayrollGuards();
        }

        /// <summary>
        /// Main Scene bootstrap hook. Registers the town General Store as a
        /// postal origin only after the actual town journey graph and formed
        /// Business are available. No office or location is fabricated.
        /// </summary>
        public void EnsureGeneralStorePostOfficeForCurrentWorld()
        {
            if (hub == null) hub = FindAnyObjectByType<SimulationSystemsHub>();
            if (sharedBusinessRuntime == null) sharedBusinessRuntime = FindAnyObjectByType<SharedBusinessRuntimeManager>();
            if (townWorld == null) townWorld = FindAnyObjectByType<TownWorldController>();
            if (hub == null) return;

            EnsureTownJourneyNetwork();
            var diagnostics = new List<string>();
            EnsureGeneralStorePostOffice(diagnostics);
            foreach (string diagnostic in diagnostics)
            {
                if (diagnostic != null && diagnostic.Contains(" registered at ", StringComparison.Ordinal))
                {
                    Debug.Log($"[SimulationDrivers] {diagnostic}", this);
                }
                else
                {
                    Debug.LogWarning($"[SimulationDrivers] {diagnostic}", this);
                }
            }
        }

        /// <summary>
        /// Pass XI WS1: the production entry point for rebinding drivers
        /// after load or restore, or when a drivers object is created
        /// after bootstrap. Re-runs the FindAnyObjectByType discovery
        /// plus TimeManager subscription plus AttachBusinessRuntime
        /// wiring, and is safe to call twice: subscription is guarded by
        /// the existing subscribed flag, manager execution configuration
        /// is guarded by its own flag, and the weekly callback setter
        /// accepts the same delegate repeatedly. Restored systems then
        /// resume through ordinary drivers instead of stale references.
        /// PlayerDirector is a plain C# authority and cannot be
        /// discovered from the scene; bootstrap assignment still owns it,
        /// and Rebind forwards an already-assigned one to the director.
        /// Pass XII Worker A: Rebind gains its first production caller —
        /// FirstLedgerSliceBootstrapper.MarkRestoredFromSave, invoked by
        /// SaveLoadManager after every restore. It is no longer an
        /// unwired entry point. A suppressed duplicate instance refuses
        /// the rebind rather than rewiring over the live drivers.
        /// </summary>
        public void Rebind()
        {
            // Pass XII Worker A: same claim rule as AttachBusinessRuntime —
            // one claim attempt for an instance whose Awake never ran, then
            // refusal for a genuine duplicate.
            if (!hasAuthorityClaim)
            {
                hasAuthorityClaim = SingleInstanceAuthorityGuard.TryClaim(
                    "SimulationDrivers", this, out string rebindRefusal);
                if (!hasAuthorityClaim)
                {
                    Debug.LogError($"[SimulationDrivers] Rebind refused: {rebindRefusal}", this);
                    return;
                }
            }

            hub ??= FindAnyObjectByType<SimulationSystemsHub>();
            timeManager ??= TimeManager.Instance != null ? TimeManager.Instance : FindAnyObjectByType<TimeManager>();
            scenarioDirector ??= FindAnyObjectByType<ScenarioDirector>();
            sharedBusinessRuntime ??= FindAnyObjectByType<SharedBusinessRuntimeManager>();
            populationManager ??= FindAnyObjectByType<PopulationManager>();
            townWorld ??= FindAnyObjectByType<TownWorldController>();
            if (populationManager?.Memberships != null)
            {
                AttachHouseholdMemberships(populationManager.Memberships);
            }
            SubscribeToTimeManager();

            if (hub == null)
            {
                Debug.LogWarning("[SimulationDrivers] Rebind found no SimulationSystemsHub — drivers idle.");
                return;
            }

            hub.Tasks.RegisterDefinition(new TaskDefinition(BusinessFeedShoppingTaskId, "Business Feed Shopping", 15), out _);
            hub.Tasks.RegisterDefinition(new TaskDefinition(OpeningHitchTaskId, "Hitch Opening Freight Wagon", Math.Max(1, developmentHitchMinutes)), out _);
            hub.Tasks.RegisterDefinition(new TaskDefinition(FreightMeetingTaskId, "Freight Service Meeting", Math.Max(1, freightMeetingMinutes)), out _);
            hub.Tasks.RegisterDefinition(new TaskDefinition(FreightDispatchTaskId, "Dispatch Accepted Freight Shipment", 1), out _);

            AcquisitionMarketManager acquisitionMarket = FindAnyObjectByType<AcquisitionMarketManager>();
            hub.AttachParcelWorld(townWorld, acquisitionMarket != null ? acquisitionMarket.Titles : null);

            if (scenarioDirector != null)
            {
                scenarioDirector.TaskAuthority = hub.Tasks;
                if (playerDirector != null)
                {
                    scenarioDirector.PlayerDirector = playerDirector;
                }
            }

            if (sharedBusinessRuntime != null)
            {
                AttachBusinessRuntime(sharedBusinessRuntime);
            }
        }

        /// <summary>
        /// Pass XI WS1 (PASS9-006): save/load hook, called by
        /// SaveLoadManager after a restore (and usable on New Game
        /// clear). Resets the domestic tick watermark to the loaded
        /// clock so loading an earlier-time save in the same session
        /// does not stall domestic labor. Touches only the watermark:
        /// completed work, pending tasks, and person-time reservations
        /// are untouched.
        /// </summary>
        public void ResetDomesticTickWatermark(float loadedAbsoluteGameSeconds)
        {
            domesticTickGate.Reset(loadedAbsoluteGameSeconds);
        }

        /// <summary>
        /// Pass XI WS1: the drivers daily hook for scenario objective
        /// evaluation. Runs the hub objective service for the director's
        /// active scenario with the current absolute day index. A
        /// mismatch between director and service scenario ids (a start
        /// hook that never ran) warns once per scenario instead of
        /// evaluating the wrong run.
        /// </summary>
        private void EvaluateScenarioObjectives(int absoluteDayIndex)
        {
            if (scenarioDirector == null || !scenarioDirector.HasActiveScenario || hub == null)
            {
                return;
            }

            string activeId = scenarioDirector.ActiveScenarioId;
            if (!string.Equals(hub.ScenarioObjectives.ScenarioId, activeId, StringComparison.Ordinal))
            {
                if (!string.Equals(scenarioEvaluateWarnedForId, activeId, StringComparison.Ordinal))
                {
                    scenarioEvaluateWarnedForId = activeId;
                    Debug.LogWarning($"[SimulationDrivers] Scenario objectives not evaluating: objective service tracks '{hub.ScenarioObjectives.ScenarioId}' but the director runs '{activeId}'. Check the scenario start hook.");
                }
                return;
            }

            hub.ScenarioObjectives.Evaluate(absoluteDayIndex);
        }

        /// <summary>
        /// Pass IX (A2-05/A2-10): payroll must never pay a dead or off-map
        /// person. True when the person is unknown, dead, or off-map.
        /// </summary>
        private bool IsPersonUnavailableForPayroll(int personId)
        {
            if (populationManager?.State == null || personId < 0)
            {
                return true;
            }

            PersonState person = populationManager.State.GetPerson(personId);
            return person == null || person.deathDayIndex >= 0 || person.isOffMap;
        }

        /// <summary>
        /// Pass IX (A2-05): installs the production weekly attendance
        /// provider and the dead/off-map payroll guard on the shared
        /// business runtime — the manager's wire methods copy both onto
        /// every business runtime state, including DTO-rebuilt ones — and
        /// directly onto the general store's runtime state, whose weekly
        /// path does not pass through the shared manager's wiring.
        /// </summary>
        private void InstallProductionPayrollGuards()
        {
            if (hub == null)
            {
                return;
            }

            if (sharedBusinessRuntime != null)
            {
                sharedBusinessRuntime.WeeklyAttendanceProvider =
                    weeklyAttendance.BuildProvider(hub.Employments, IsPersonUnavailableForPayroll);
                sharedBusinessRuntime.PersonUnavailableForPayrollLookup = IsPersonUnavailableForPayroll;
                sharedBusinessRuntime.WireEmploymentRegistryToBusinesses();
                sharedBusinessRuntime.WireHouseholdCashToBusinesses();
            }

            var generalStore = FindAnyObjectByType<LandLedgers.FirstLedger.GeneralStoreRuntimeManager>();
            if (generalStore?.RuntimeState != null)
            {
                weeklyAttendance.InstallOn(generalStore.RuntimeState, hub.Employments, IsPersonUnavailableForPayroll);
                generalStore.RuntimeState.PersonUnavailableForPayrollLookup = IsPersonUnavailableForPayroll;
            }
        }

        // CLN-4: accumulated player worked minutes for the current week (owner labor).
        private int weeklyPlayerWorkedMinutes;

        private void Awake()
        {
            // Pass XII Worker A (LL-PLAY-002/003): claim sole drivers
            // authority before wiring anything. A second live drivers
            // object used to subscribe to the same TimeManager events and
            // drive the same businesses a second time. The loser logs an
            // error and stays idle; removing the duplicate GameObject from
            // the scene is Codex's scene work, not a runtime guess.
            hasAuthorityClaim = SingleInstanceAuthorityGuard.TryClaim(
                "SimulationDrivers", this, out string claimRefusal);
            if (!hasAuthorityClaim)
            {
                Debug.LogError($"[SimulationDrivers] {claimRefusal}", this);
                return;
            }

            hub ??= FindAnyObjectByType<SimulationSystemsHub>();
            timeManager ??= TimeManager.Instance != null ? TimeManager.Instance : FindAnyObjectByType<TimeManager>();
            scenarioDirector ??= FindAnyObjectByType<ScenarioDirector>();
            // PlayerDirector is a plain C# authority, not a UnityEngine.Object; scene
            // auto-discovery is not available here. Bootstrap code may assign it.
            sharedBusinessRuntime ??= FindAnyObjectByType<SharedBusinessRuntimeManager>();
            populationManager ??= FindAnyObjectByType<PopulationManager>();
            townWorld ??= FindAnyObjectByType<TownWorldController>();

            if (hub == null)
            {
                Debug.LogWarning("[SimulationDrivers] No SimulationSystemsHub found — drivers idle.");
                return;
            }

            AcquisitionMarketManager acquisitionMarket = FindAnyObjectByType<AcquisitionMarketManager>();
            hub.AttachParcelWorld(townWorld, acquisitionMarket != null ? acquisitionMarket.Titles : null);

            // Pass IV (Worker 1): the universal V2 duration estimator. Method estimators
            // own their definition ids (handling, travel); everything else falls back
            // to the causal skill estimator, preserving pre-Pass IV behavior exactly.
            // Worker capability for method estimators comes from the Pass III stores.
            var causalEstimator = new CausalTaskDurationEstimator(hub.Skills);
            Func<TaskDurationInputs, DurationEstimate> legacyFallback = inputs =>
            {
                int baseMinutes = Math.Max(1, inputs != null ? inputs.BaseMinutes : 1);
                string skillId = inputs != null ? inputs.SkillId ?? string.Empty : string.Empty;
                EntityId worker = inputs != null && inputs.WorkerPersonId >= 0
                    ? EntityId.For(EntityKind.Person, inputs.WorkerPersonId)
                    : default(EntityId);
                int minutes = causalEstimator.EstimateMinutesForBase(baseMinutes, skillId, worker);
                return DurationEstimate.FromGameSeconds(
                    minutes * DurationConventions.SecondsPerMinute, "fallback:causal-v1");
            };
            var universalEstimator = new UniversalTaskDurationEstimator(
                new ITaskDurationEstimatorV2[]
                {
                    new HandlingDurationEstimator(HandlingDurationEstimator.DefaultDefinitionIds),
                    new TravelDurationEstimator(TravelDurationEstimator.DefaultDefinitionIds),
                },
                legacyFallback);
            hub.Tasks.SetDurationEstimatorV2(universalEstimator,
                (workerId, skillId) => (float)hub.Skills.GetCapability(workerId, skillId));

            // Pass IV (Worker 1): ambient budget store so the new lifecycle states keep
            // worker commitments truthful (WaitingOnPassiveProcess releases the worker;
            // ResumeTask re-commits).
            hub.Tasks.SetWorkTimeBudgetStore(hub.WorkTimeBudgets);

            // Pass IV Worker 2: install the equipment execution gate (Canon 4.1)
            // on the production TaskAuthority. Tasks with equipment, motive-
            // power, or workspace requirements are refused loudly when no
            // usable, unreserved instance exists — one plow cannot satisfy N
            // simultaneous tasks. Suppliers are hub-owned truth (unified
            // reservation authority + asset registry); nothing is assumed.
            hub.InstallEquipmentTaskGate(AbsoluteNowGameSeconds);

            if (scenarioDirector != null)
            {
                scenarioDirector.TaskAuthority = hub.Tasks;
                scenarioDirector.PlayerDirector = playerDirector;
            }

            // CLN-4: weekly profit posts to the BIZ-5 valuation read model from the real
            // settlement path (event-fed, never per-frame).
            if (sharedBusinessRuntime != null)
            {
                sharedBusinessRuntime.PreWeeklyResetCallback = PostWeeklyProfitToValuation;

                // P1: payroll reads the PKG-6 employment authority. The hub owns the
                // registry (save-persisted); the manager wires it into every
                // business runtime state so ResolveWeeklyPayroll pays agreed
                // employment wages instead of the legacy slot-template path.
                sharedBusinessRuntime.EmploymentRegistry = hub.Employments;
                sharedBusinessRuntime.WireEmploymentRegistryToBusinesses();
                WireHouseholdCashAuthorities();
                WireNpcBusinessCashStores();
                BindNpcBusinessCreation(sharedBusinessRuntime);
                InstallProductionPayrollGuards();
            }

        }

        private void BindNpcBusinessCreation(SharedBusinessRuntimeManager runtime)
        {
            if (hub == null || runtime == null) return;
            hub.NpcBusinessCreation.TypedPremisesAssigner = (businessType, kind) =>
                runtime.TryAssignNpcBusinessPremises(businessType, kind, out int buildingId) ? buildingId : -1;
            hub.NpcBusinessCreation.BusinessCreated = business =>
            {
                if (!runtime.TryAdoptNpcBusiness(business, out string message))
                    Debug.LogWarning("[SimulationDrivers] NPC Business adoption refused: " + message);
                else
                    WireNpcBusinessCashStores();
            };
        }

        /// <summary>
        /// Pass IV Worker 2: absolute game seconds on the TimeManager timeline
        /// for reservation overlap checks in the equipment task gate.
        /// </summary>
        private float AbsoluteNowGameSeconds()
        {
            if (timeManager == null) return 0f;
            return timeManager.CurrentAbsoluteDayIndex * 86400f + timeManager.GameSecondsIntoDay;
        }

        /// <summary>
        /// Phase B (Real People): wires the single household-cash truth. The
        /// hub's HouseholdLedgerRegistry flows into the shared business
        /// runtime (doctor payments + payroll wage crediting), the population
        /// manager (genesis/settlement cash sweeps), and every business
        /// runtime state (per-worker household crediting). Person-to-household
        /// resolution comes from PopulationState via the membership fields.
        /// </summary>
        private void WireHouseholdCashAuthorities()
        {
            if (hub == null)
            {
                return;
            }

            if (sharedBusinessRuntime != null)
            {
                sharedBusinessRuntime.HouseholdLedgers = hub.HouseholdLedgers;
                sharedBusinessRuntime.PersonHouseholdIdLookup = ResolvePersonHouseholdId;
                sharedBusinessRuntime.WireHouseholdCashToBusinesses();
                // Pass IX WS1 (A3-10/A3-12): the single debt authority, so
                // payroll escrows and operating-liability accruals post named
                // Payables instead of living only in a scalar.
                sharedBusinessRuntime.ObligationAuthority = hub.FinancialObligations;
                sharedBusinessRuntime.ObligationIdRegistry = hub.Ids;
                sharedBusinessRuntime.WireObligationAuthorityToBusinesses();
            }

            if (populationManager != null)
            {
                populationManager.HouseholdLedgers = hub.HouseholdLedgers;
            }
        }

        /// <summary>
        /// Phase G: binds every live NPC Business to the shared credit cash
        /// bridge. The bridge is only a workflow adapter; BusinessRuntimeState
        /// remains the single business-cash authority.
        /// </summary>
        private void WireNpcBusinessCashStores()
        {
            if (hub == null || sharedBusinessRuntime == null)
            {
                return;
            }

            NpcBusinessRuntimeCashPort port = new NpcBusinessRuntimeCashPort(sharedBusinessRuntime);
            foreach (BusinessInstanceState business in sharedBusinessRuntime.Businesses)
            {
                if (business == null || business.Owner == null || business.Owner.OwnerKind == BusinessOwnerKind.Player)
                {
                    continue;
                }

                hub.CreditCash.Register(
                    "business:" + business.InstanceId,
                    new NpcBusinessCashStore(business.InstanceId, port));
            }
        }

        private int ResolvePersonHouseholdId(int personId)
        {
            if (populationManager == null || populationManager.State == null)
            {
                return -1;
            }

            var person = populationManager.State.GetPerson(personId);
            return person != null ? person.householdId : -1;
        }

        private void OnEnable()
        {
            // Re-claim after a disable released the claim (see OnDisable).
            // A duplicate that never held the claim stays suppressed here
            // too, so it can never subscribe to the clock.
            if (!hasAuthorityClaim)
            {
                hasAuthorityClaim = SingleInstanceAuthorityGuard.TryClaim(
                    "SimulationDrivers", this, out string claimRefusal);
                if (!hasAuthorityClaim)
                {
                    Debug.LogError($"[SimulationDrivers] {claimRefusal}", this);
                    return;
                }
            }

            SubscribeToTimeManager();
        }

        private void OnDestroy()
        {
            if (hasAuthorityClaim)
            {
                SingleInstanceAuthorityGuard.Release("SimulationDrivers", this);
                hasAuthorityClaim = false;
            }
        }

        private void SubscribeToTimeManager()
        {
            if (subscribedToTimeManager || timeManager == null)
            {
                return;
            }

            timeManager.DayChanged += OnDayChanged;
            timeManager.WeekChanged += OnWeekChanged;
            timeManager.ShortTick += OnShortTick;
            subscribedToTimeManager = true;
        }

        private void OnDisable()
        {
            if (subscribedToTimeManager && timeManager != null)
            {
                timeManager.DayChanged -= OnDayChanged;
                timeManager.WeekChanged -= OnWeekChanged;
                timeManager.ShortTick -= OnShortTick;
                subscribedToTimeManager = false;
            }

            // Pass XII Worker A: disabling releases the claim, so the one
            // live drivers object is always the one ticking. OnDestroy
            // covers the never-enabled case, where OnDisable does not run.
            if (hasAuthorityClaim)
            {
                SingleInstanceAuthorityGuard.Release("SimulationDrivers", this);
                hasAuthorityClaim = false;
            }

            if (sharedBusinessRuntime != null
                && sharedBusinessRuntime.PreWeeklyResetCallback == (Action<BusinessInstanceState>)PostWeeklyProfitToValuation)
            {
                sharedBusinessRuntime.PreWeeklyResetCallback = null;
            }
        }

        /// <summary>
        /// CLN-4: posts a business's weekly net to the valuation read model before the
        /// settlement reset clears it. Called from the real weekly settlement path.
        /// </summary>
        private void PostWeeklyProfitToValuation(BusinessInstanceState business)
        {
            if (hub == null || business == null || business.RuntimeState == null)
            {
                return;
            }

            // Pass IX WS1 (A3-06a): post the financing-excluded OPERATING
            // figure, not WeekToDateNetCents. The cash-net includes loan
            // proceeds (AdjustCashCents credits from ManagerBorrowExecutor /
            // NpcOwnerRequestResolver borrow paths), which booked borrowed
            // money as valuation "profit". WeeklyOperatingProfitCents is
            // derived the way BusinessReportBuilder's knownProfitProxy is:
            // revenue minus operating expense only.
            hub.Valuation.RecordWeeklyProfit(business.InstanceId, business.RuntimeState.WeeklyOperatingProfitCents);

            // Pass IX WS5 (A3-13): feed the market-capture factor during
            // weekly settlement. Competition pressure is the complement of
            // the existing market-capture computation; before this feed the
            // runtime scalar was only set by a monthly-cadence method with
            // no production caller, so reports carried a permanent 0.
            if (sharedBusinessRuntime != null
                && sharedBusinessRuntime.TryGetBusinessMarketReadout(business, out float capture01, out _))
            {
                MarketCaptureCompetitionFeed.FeedCompetitionPressure(business.RuntimeState, capture01);
            }
        }

        /// <summary>
        /// Pass IX WS5 (PASS8-003): the production appraisal service over the
        /// business's own registers — equipment assets from the transport /
        /// equipment register, book cost from the business's own procurement
        /// records, formation assets from the formation register. No
        /// register evidence means no appraisal (the resolver denies).
        /// </summary>
        private AssetSaleAppraisalService BuildProductionAppraisalService()
        {
            return new AssetSaleAppraisalService
            {
                FindEquipmentAsset = id => hub.TransportAssets.Get(id),
                FormationAssets = hub.NpcFormationAssets,
                EquipmentBookCostCents = (businessId, assetId) =>
                {
                    BusinessInstanceState found;
                    if (sharedBusinessRuntime == null
                        || !sharedBusinessRuntime.TryFindByInstanceId(businessId, out found)
                        || found == null)
                    {
                        return 0;
                    }

                    foreach (EquipmentProcurementState procurement in found.GenericConfiguration.EquipmentProcurements)
                    {
                        if (procurement != null && procurement.Order != null
                            && string.Equals(procurement.AssetId, assetId, StringComparison.Ordinal))
                        {
                            return Math.Max(0, procurement.Order.TotalCents);
                        }
                    }

                    return 0;
                },
            };
        }

        private void OnWeekChanged(SimulationDateChangedContext context)
        {
            if (hub == null)
            {
                return;
            }

            int absoluteDayIndex = timeManager != null
                ? timeManager.CurrentAbsoluteDayIndex
                : hub.LaborDayIndex;

            // Pass IX (A2-05): weekly reset of the attendance table — totals
            // older than last week can no longer be consulted by payroll or
            // its replay, so they are pruned at the week boundary.
            weeklyAttendance.PruneWeeksBefore(
                WeeklyAttendanceAccumulator.WeekIndexForDay(absoluteDayIndex) - 1);

            // P1: SWN-3 liability ledger -> BIZ-5 valuation. Liabilities change
            // during the week (loans drawn, payables accrued); re-sync weekly so
            // the valuation read model reflects real balances, not boot-time ones.
            hub.Liabilities.SyncAllToValuation(null);

            // CLN-4: owner labor — the player's accumulated weekly worked minutes feed
            // the valuation for each player-owned business (real work-time path).
            // Pass XL player-as-NPC playtest: skipped when the flag is on.
            // The accumulator is still reset below, so turning the flag on
            // mid-week does not carry stale player minutes into a later week.
            if (!treatPlayerAsNpc && playerDirector != null && weeklyPlayerWorkedMinutes > 0
                && sharedBusinessRuntime != null)
            {
                float weeklyHours = weeklyPlayerWorkedMinutes / 60f;
                foreach (BusinessInstanceState business in sharedBusinessRuntime.Businesses)
                {
                    if (business != null && business.Owner != null
                        && business.Owner.OwnerKind == BusinessOwnerKind.Player)
                    {
                        hub.Valuation.RegisterBusiness(business.InstanceId, "player", true);
                        // NX-1B: owner labor at a resolved replacement rate —
                        // local payroll evidence first, documented calibration
                        // fallback; never silently zero.
                        OwnerLaborRateResolver.ResolvedRate rate =
                            OwnerLaborRateResolver.Resolve(hub.Employments, business.InstanceId);
                        hub.Valuation.RecordOwnerLabor(business.InstanceId, weeklyHours, rate.CentsPerHour, 0);
                        if (rate.Source == OwnerLaborRateResolver.RateSource.CalibrationFallback)
                        {
                            Debug.LogWarning($"[SimulationDrivers] owner-labor rate for {business.InstanceId}: {rate.BasisNote}");
                        }
                    }
                }
            }

            weeklyPlayerWorkedMinutes = 0;

            // Autonomous Labor: weekly wage settlement per business, guarded
            // against double-posting by the settled-week marker.
            DriveLaborMarketWeek(absoluteDayIndex);
            DriveBoardingHouseWeeks(absoluteDayIndex);
            // Pass V P2-3: autonomous entrepreneurship — observation to
            // formation, no quotas.
            DriveEntrepreneurshipWeek(absoluteDayIndex);
            // G-01: NPC business lifecycles — weekly struggle/respond/pivot
            // evaluation for every live NPC-founded business.
            DriveNpcBusinessLifecycleWeek(absoluteDayIndex);
        }

        /// <summary>
        /// Pass V P2-3: runs the entrepreneurship driver once per week. Every
        /// evidence source is real: unfulfilled needs from the purchasing
        /// registry, suppliers from the supplier directory, premises from the
        /// runtime's unassigned buildings (query only — claiming happens at
        /// formation), equipment provenance from a live smithy, financing
        /// from the founder household's own ledger. Formation goes through
        /// the same adapter the player uses.
        /// </summary>
        private void DriveEntrepreneurshipWeek(int absoluteDayIndex)
        {
            if (hub == null || populationManager?.State == null || householdMemberships == null) return;
            var diag = new List<string>();
            hub.NpcEntrepreneurship.EvaluateWeek(absoluteDayIndex,
                new NpcEntrepreneurshipDriver.EntrepreneurshipContext
                {
                    Population = populationManager.State,
                    Memberships = householdMemberships,
                    Ledgers = hub.HouseholdLedgers,
                    Needs = hub.PurchasingNeeds,
                    Schedules = hub.PersonSchedules,
                    Events = hub.NpcBusinessEvents,
                    CreationPort = hub.NpcBusinessCreation,
                    Suppliers = supplierDirectory,
                    PersonAlreadyOwnsBusiness = PersonOwnsBusiness,
                    // PremisesFinder and EquipmentFinder intentionally remain
                    // unbound until production can return actual property
                    // rights and an actual stocked/sold equipment asset. A
                    // free building id plus fixed rent, or a provenance string
                    // without a transaction, is not evidence that the Person
                    // can lawfully open a Business.
                }, diag);
            if (diag.Count > 0)
            {
                Debug.Log("[SimulationDrivers] entrepreneurship week " + absoluteDayIndex + ": " +
                    string.Join(" | ", diag));
            }
        }

        /// <summary>
        /// G-01: runs the NPC business lifecycle driver once per week, beside
        /// the entrepreneurship drive. Sources a life record for every live
        /// NPC-founded business, builds the NpcLifecycleContext from hub
        /// authorities (real business cash via NpcBusinessRuntimeCashPort,
        /// obligations, employments, founder ledgers, formation assets, event
        /// log, ids, creditor cash bridge), and evaluates with the shared
        /// calibrated survival reserve per business (payroll + reorder based;
        /// the driver's per-type table is the fallback). Struggling
        /// businesses cut costs and seek customers with real money; the
        /// credit-loop borrow context stays null in this pass.
        /// </summary>
        private void DriveNpcBusinessLifecycleWeek(int absoluteDayIndex)
        {
            if (hub == null || sharedBusinessRuntime == null) return;
            npcBusinessLifecycleDriver ??= new NpcBusinessLifecycleDriver();

            var businesses = new List<NpcBusinessLifecycleDriver.NpcLifecycleBusinessInfo>();
            foreach (BusinessInstanceState business in sharedBusinessRuntime.Businesses)
            {
                if (business == null || business.Owner == null
                    || business.Owner.OwnerKind != BusinessOwnerKind.Npc)
                    continue;
                businesses.Add(new NpcBusinessLifecycleDriver.NpcLifecycleBusinessInfo
                {
                    BusinessInstanceId = business.InstanceId,
                    BusinessType = business.BusinessType,
                    OwnerPersonId = business.Owner.PersonId,
                    OwnerName = business.Owner.DisplayName,
                    Business = business,
                });
            }

            var diag = new List<string>();
            npcBusinessLifecycleDriver.EvaluateWeek(
                new NpcBusinessLifecycleDriver.LifecycleDriverContext
                {
                    LifeRecords = hub.NpcBusinessLifeRecords,
                    NpcBusinesses = businesses,
                    Cash = new NpcBusinessRuntimeCashPort(sharedBusinessRuntime),
                    Obligations = hub.FinancialObligations,
                    Employments = hub.Employments,
                    FounderLedgerByPersonId = personId =>
                    {
                        int householdId = ResolvePersonHouseholdId(personId);
                        return householdId >= 0 && hub.HouseholdLedgers != null
                            ? hub.HouseholdLedgers.GetOrCreate(householdId)
                            : null;
                    },
                    AssetRegister = hub.NpcFormationAssets,
                    // Pass IX WS5 (A3-08): the production liquidation port —
                    // close-down sales are real sales at the appraisal's
                    // liquidation value to a solvent buyer business, paid
                    // through the runtime cash port. (EvaluateWeek does not
                    // consume it; CloseOrderly does.)
                    Liquidation = new RegisterLiquidationPort(
                        () => sharedBusinessRuntime.Businesses,
                        new NpcBusinessRuntimeCashPort(sharedBusinessRuntime),
                        BuildProductionAppraisalService()),
                    Events = hub.NpcBusinessEvents,
                    Ids = hub.Ids,
                    CreditorCash = hub.CreditCash,
                    // Sane per-business-type tuning: the shared calibrated
                    // reserve (weekly payroll + reorder buffer + fragility
                    // padding), falling back to the driver's table.
                    SurvivalReserveCents = info => info?.Business != null
                        ? SharedBusinessRuntimeManager.CalculateSharedSurvivalCashReserveCents(info.Business)
                        : NpcBusinessLifecycleDriver.DefaultSurvivalReserveCents(
                            info != null ? info.BusinessType : BusinessType.Generic),
                    DayIndex = absoluteDayIndex,
                }, diag);
            if (diag.Count > 0)
            {
                Debug.Log("[SimulationDrivers] npc business lifecycle week " + absoluteDayIndex + ": " +
                    string.Join(" | ", diag));
            }
        }

        private bool PersonOwnsBusiness(int personId)
        {
            if (sharedBusinessRuntime == null) return false;
            foreach (BusinessInstanceState business in sharedBusinessRuntime.Businesses)
            {
                if (business?.Owner == null) continue;
                if (business.Owner.OwnerKind == BusinessOwnerKind.Npc
                    && business.Owner.PersonId == personId)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Pass IX (WS2): the daily demographic drive. PopulationManager runs
        /// births due, mortality, birthday scheduling, and the monthly
        /// conceptions/marriages/migration/splits (idempotent per day via
        /// deterministic draws). Estates opened by today's mortality advance
        /// one lifecycle stage beside it (A2-09 / PASS7-001), and served
        /// domestic pending entries older than 2 days are pruned so the
        /// persisted pending list cannot grow monotonically (A2-04). The
        /// daily activity planner (A2-03) runs later in OnDayChanged, after
        /// the work-time budgets roll — it is part of this same daily
        /// sequence, not a separate fixture-level hook.
        /// </summary>
        private void DriveDemographicDay(int absoluteDayIndex)
        {
            populationManager?.DriveDemographicDay(absoluteDayIndex);
            DriveEstateAdvancement(absoluteDayIndex);
            if (hub?.DomesticLaborDriver != null)
            {
                hub.DomesticLaborDriver.PruneStalePending(absoluteDayIndex);
            }
            DriveNpcPropertyLifeDay(absoluteDayIndex);
            DriveSchoolhouseTeacherPay(absoluteDayIndex);
        }

        /// <summary>
        /// Pass IX review R-02: the production teacher-pay drive. No
        /// school-term flow existed in production (the schoolhouse is owned
        /// by the scene's CivicFoundationManager, not the hub), so teacher
        /// pay is called here, from the day-change path, guarded to term
        /// boundaries: only on a term-start day, and only when a teacher
        /// with a household is assigned. The funded portion credits the
        /// teacher's household ledger (hub registry, by the state's
        /// teacherHouseholdId) in the same call; PayTeacher's own term
        /// no-op semantics make repeat safety exact.
        /// </summary>
        private void DriveSchoolhouseTeacherPay(int absoluteDayIndex)
        {
            if (hub == null) return;
            if (LandLedgers.Civic.SchoolhouseAssignment.TermStartFor(absoluteDayIndex) != absoluteDayIndex)
            {
                return; // term boundaries only — PayTeacher settles a whole term at its start
            }

            LandLedgers.Civic.CivicFoundationManager civic = FindAnyObjectByType<LandLedgers.Civic.CivicFoundationManager>();
            LandLedgers.Civic.SchoolhouseState schoolhouse = civic != null ? civic.Schoolhouse : null;
            if (schoolhouse == null || !schoolhouse.HasTeacher || schoolhouse.teacherHouseholdId < 0)
            {
                return;
            }

            HouseholdLedger teacherLedger = hub.HouseholdLedgers.Get(schoolhouse.teacherHouseholdId);
            var diag = new List<string>();
            bool settled = schoolhouse.PayTeacher(
                hub.CivicTreasury, hub.FinancialObligations, hub.Ids, absoluteDayIndex, diag, teacherLedger);
            if (settled || diag.Count > 0)
            {
                Debug.Log("[SimulationDrivers] schoolhouse teacher pay day " + absoluteDayIndex + ": " +
                    string.Join(" | ", diag));
            }
        }

        /// <summary>
        /// Pass IX WS6 (A1-07 driver half): the NPC property-life production
        /// step — tax audit into the hub civic treasury, a maintenance pass,
        /// and at most one distress sale — over the hub's real holdings.
        /// Gated so fixtures/worlds that never registered NPC holdings
        /// no-op cleanly: no holdings, no audit, no invented cash or buyers.
        /// The distress-sale context is supplied only when the acquisition
        /// manager (the title authority's owner) is present; without a named
        /// buyer the brokered sale refuses inside the service, loudly.
        /// </summary>
        private void DriveNpcPropertyLifeDay(int absoluteDayIndex)
        {
            if (hub?.NpcPropertyLife == null) return;
            if (hub.NpcPropertyLife.Holdings.Count == 0) return;
            var diag = new List<string>();
            AcquisitionMarketManager acquisitionMarket = FindAnyObjectByType<AcquisitionMarketManager>();
            var saleContext = acquisitionMarket != null
                ? new NpcDistressSaleContext
                {
                    Ids = hub.Ids,
                    Authority = hub.FinancialObligations,
                    Cash = hub.CreditCash,
                    Titles = acquisitionMarket.Titles,
                    Housing = hub.Housing,
                    Workflow = hub.CreditOffers,
                    Events = hub.CreditEvents,
                    TaxService = hub.NpcPropertyLife.Taxes,
                    BuyerHouseholdId = -1, // no named-buyer source in production yet — sales refuse honestly
                }
                : null;
            // Pass XI WS3 (PASS9-003) hook: feed the acquisition manager's dispute service
            // into the distress-sale context so NPC distress sales record disputes through
            // the same persisted record as the player path. Null saleContext keeps the
            // honest no-op (disputes == null records nothing).
            if (saleContext != null) saleContext.FeedDisputes(acquisitionMarket.PropertyDisputes);
            NpcPropertyLifeReport report = hub.NpcPropertyLife.DriveNpcPropertyLife(
                hub.CreditCash, hub.NpcPropertyLife.Taxes, hub.FinancialObligations,
                hub.CivicTreasury, absoluteDayIndex, diag, saleContext, hub.Ids);
            if (diag.Count > 0)
            {
                Debug.Log("[SimulationDrivers] npc property life day " + absoluteDayIndex + ": " +
                    string.Join(" | ", diag));
            }
            if (report.TaxesPaidCents > 0 || report.SalesAttempted > 0)
            {
                Debug.Log($"[SimulationDrivers] npc property life day {absoluteDayIndex}: " +
                    $"taxes paid {report.TaxesPaidCents}c, treasury credited {report.TreasuryCreditedCents}c, " +
                    $"maintenance funded {report.MaintenanceNeedsFunded}, sales attempted {report.SalesAttempted}.");
            }
        }

        private void OnDayChanged(SimulationDateChangedContext context)
        {
            if (hub == null || timeManager == null)
            {
                return;
            }

            int absoluteDayIndex = timeManager.CurrentAbsoluteDayIndex;

            DriveMillbrookAidTable(absoluteDayIndex);

            // Pass XI WS1 start contract: the day change drives dated
            // objective evaluation for the active scenario.
            EvaluateScenarioObjectives(absoluteDayIndex);

            // Pass IV (Worker 1): drain due events due at the day boundary before the
            // daily drivers advance.
            ProcessDueEvents();

            // Pass V (P0-2/P1-2) + Pass IX (WS2): the demographic drive —
            // births due, mortality, monthly demographic drivers, estate
            // advancement beside mortality, and the domestic pending prune.
            DriveDemographicDay(absoluteDayIndex);

            // Population may be generated after Awake; bind the now-real
            // household ledgers before any daily finance workflow can open a
            // cash window.
            RefreshHouseholdCashBindings();

            // Pass IX (A2-05): snapshot YESTERDAY's real worked minutes into
            // the weekly attendance accumulator BEFORE EnsureDay resets the
            // budgets — payroll's attendance provider reads these totals.
            // Re-install the payroll guards too, so any business runtime
            // rebuilt since yesterday carries them before its weekly run.
            weeklyAttendance.ObserveDay(hub.WorkTimeBudgets, absoluteDayIndex - 1);
            InstallProductionPayrollGuards();

            // CLN-4: accumulate the player's daily worked minutes for the weekly
            // owner-labor valuation post. Read BEFORE EnsureDay resets the day.
            // Pass XL player-as-NPC playtest: skipped when the flag is on, so
            // no player owner-labour minutes accumulate in NPC mode.
            if (!treatPlayerAsNpc && playerDirector != null && playerDirector.PlayerPersonId.IsValid)
            {
                var budget = hub.WorkTimeBudgets.GetOrCreate(playerDirector.PlayerPersonId);
                weeklyPlayerWorkedMinutes += budget.MinutesWorked;
            }

            // TTS-1: roll every person's daily work-time budget.
            hub.WorkTimeBudgets.EnsureDay(absoluteDayIndex);

            // Pass IX (A2-03): reserve today's baseline person-time (sleep,
            // meals, job-seeking, managing, rest) AFTER budgets roll and
            // BEFORE any labor drive, so work cannot be scheduled over
            // sleep in the live game (Canon §22 double-booking).
            DriveDailyActivityPlans(absoluteDayIndex);

            // Generic production processes are persisted on their Business and
            // advance through the same TaskAuthority on the daily simulation
            // boundary. This is execution of an already-started physical process;
            // it does not grant capability or create output from a policy alone.
            DriveGenericProduction(absoluteDayIndex);

            // Muse Phase I: trade-credit aging is a daily read/transition over
            // the shared FinancialObligationAuthority. It never creates cash
            // or principal; it only advances registered invoice terms and
            // records the resulting credit events.
            var creditDiagnostics = new List<string>();
            hub.TradeCredit.EvaluateDay(
                hub.FinancialObligations,
                absoluteDayIndex,
                hub.CreditEvents,
                creditDiagnostics);
            if (creditDiagnostics.Count > 0)
            {
                Debug.LogWarning($"[SimulationDrivers] trade-credit aging day {absoluteDayIndex}: " +
                    string.Join(" | ", creditDiagnostics));
            }

            // BIZ-4: age butcher lots (fresh → aging → spoiled).
            foreach (var runtime in hub.ButcherRuntimes)
            {
                runtime?.AgeLotsToDay(absoluteDayIndex);
            }

            // P6: travel & communication daily drives. Weather actually changes
            // (NX-2C: blizzards close roads through the journey model's
            // condition provider), mail actually moves on office schedules
            // (NX-2A: posted → in-transit → arrived → collected), money-order
            // advices arrive and stale orders expire (D4E), overdue registered
            // items are audited (D4F), and recruitment letters resolve —
            // postal-routed ones only on the mail item's real arrival (T2B).
            DriveTravelAndComms(absoluteDayIndex);

            // NX-2C drive-by: DailyNeedsService.ExecuteDay had NO live caller —
            // the NX-1B nutrition teeth never bit. Wire it here with the
            // work-time budgets so missed meals reduce usable minutes for real.
            DriveBoardingHouseDays(absoluteDayIndex);
            DriveStoreFeedProcurement(absoluteDayIndex);
            DriveBusinessHorseFeedProcurement(absoluteDayIndex);
            DriveDailyNeeds(absoluteDayIndex);
            // Pass VII Workstream B (P1-5): daily horse care had no production
            // caller — horses never tired and never needed care. Fed from the
            // farm's real feed stock now; unfed horses degrade loudly.
            DriveHorseCareDay(absoluteDayIndex);
            DriveOpeningHitchPreparation(absoluteDayIndex);
            DriveTenancyPayments(absoluteDayIndex);
            // Pass VIII Worker 2: the absent-occupant boundary — an absent
            // person (dead or departed from the town roster) must not hold
            // local accommodation indefinitely without a valid arrangement.
            // Pass IX (A2-13): the sweep receives the lenient-arrangement
            // predicate (live agreement or active return journey).
            DriveHousingAbsenceSweep(absoluteDayIndex);
            // Pass V drive-by: DomesticLaborDriver had NO live caller (tests
            // only). Wire it here: need-driven domestic tasks (meals, fuel,
            // animals, chores) scheduled on real person time through the real
            // TaskAuthority, budgets, schedule tracker, and due events.
            DriveDomesticLaborDay(absoluteDayIndex);

            // Pass XI WS2 (A-03, RUN-002): NPC construction advances daily
            // through the production driver (staged packages work and
            // advance on hub authorities; Ready packages and completion
            // wait loudly until their material and catalog ports are bound).
            DriveNpcConstruction(absoluteDayIndex);

            // Autonomous Labor: daily labor-market drives. The hub's authority
            // predicate needs the current day for delegation bounds.
            hub.LaborDayIndex = absoluteDayIndex;
            DriveLaborMarketDay(absoluteDayIndex);
            // Pass IX (A2-06 + A2-07): the daily social drive — social visits
            // are planned on real person time, co-located conversations
            // record provenance-carrying knowledge into the hub store, and
            // employed persons' job security is evaluated so corroborated
            // layoff knowledge routes into real job applications.
            DriveSocialDay(absoluteDayIndex);
            DriveAutonomousLabor(absoluteDayIndex);
        }

        private void DriveMillbrookAidTable(int absoluteDayIndex)
        {
            if (millbrookAidRuntime == null || sharedBusinessRuntime == null) return;
            foreach (string message in millbrookAidRuntime.ApplyWeeklyReplenishment(absoluteDayIndex))
                Debug.Log("[OffMapAid] " + message, this);

            if (lastMillbrookAidAttemptDay == absoluteDayIndex || millbrookAidRuntime.HasOrder("opening-flour-b1")) return;
            lastMillbrookAidAttemptDay = absoluteDayIndex;
            BusinessInstanceState bakery = null;
            foreach (BusinessInstanceState candidate in sharedBusinessRuntime.Businesses)
            {
                if (candidate != null && candidate.BusinessType == BusinessType.Bakery)
                {
                    bakery = candidate;
                    break;
                }
            }
            if (bakery?.RuntimeState == null) return;

            int stockBefore = millbrookAidRuntime.GetInventoryUnits("bakery_flour_sack");
            int supplierCashBefore = millbrookAidRuntime.CashCents;
            string buyerName = bakery.Owner != null && !string.IsNullOrWhiteSpace(bakery.Owner.DisplayName)
                ? bakery.Owner.DisplayName : bakery.RuntimeDisplayName;
            string buyerId = bakery.InstanceId;
            IBusinessCashPort buyerCash = new Series2BusinessRuntimeCashPort(bakery.RuntimeState);
            int cashBefore = buyerCash.BalanceCents;
            string payerDescription = $"Bakery business cash authority ({bakery.InstanceId})";
            string refusal = millbrookAidRuntime.TryPlaceOrder(
                "opening-flour-b1", buyerId, buyerName, 1, buyerCash, absoluteDayIndex, out OffMapAidCargoLot lot);
            if (!string.IsNullOrWhiteSpace(refusal))
            {
                if (!string.Equals(lastMillbrookAidRefusal, refusal, StringComparison.Ordinal))
                {
                    Debug.LogWarning($"[OffMapAid] flour order refused: buyer='{buyerName}' payer='{payerDescription}', " +
                        $"Millbrook flour={stockBefore} lb and cash={supplierCashBefore}c before; buyer cash={cashBefore}c; " +
                        $"reason={refusal}", this);
                    lastMillbrookAidRefusal = refusal;
                }
                return;
            }

            lastMillbrookAidRefusal = string.Empty;
            Debug.Log($"[OffMapAid] flour order completed: buyer='{buyerName}' ({bakery.InstanceId}) paid Millbrook Grain Co. " +
                $"{lot.TotalCostCents}c for {lot.PackageUnits} {lot.OrderUnit} ({lot.InventoryUnits} lb flour = " +
                $"{lot.BakeryUnits} bakery units); payer='{payerDescription}' cash {cashBefore}->{buyerCash.BalanceCents}c; " +
                $"Millbrook flour {stockBefore}->{millbrookAidRuntime.GetInventoryUnits(lot.SkuId)} lb, " +
                $"Millbrook cash {supplierCashBefore}->{millbrookAidRuntime.CashCents}c, " +
                $"pickup lot='{lot.LotId}' (not yet freight-picked-up or delivered).", this);
        }

        /// <summary>
        /// Settles agreements from the hub-owned HousingAuthority once per
        /// simulation day. Payees resolve to the same household ledgers or
        /// BusinessRuntimeState cash stores used everywhere else; an unknown
        /// payee therefore refuses the payment rather than losing tenant cash.
        /// </summary>
        /// <summary>
        /// Pass V drive-by: daily domestic labor. Each household's
        /// need-driven tasks (meals for members at home, fuel in cold season,
        /// animal care when the household owns animals, baseline chores) are
        /// planned by DomesticLaborDriver and scheduled on real person time.
        /// </summary>
        private void DriveDomesticLaborDay(int absoluteDayIndex)
        {
            if (hub == null || populationManager?.State?.households == null || householdMemberships == null) return;
            var diag = new List<string>();
            // Register once: re-registering daily would spam duplicate
            // rejections into the diagnostics.
            if (hub.Tasks.GetDefinition(DomesticTaskCatalog.MealPrepId) == null)
                DomesticTaskCatalog.RegisterAll(hub.Tasks, diag);
            bool coldSeason = IsColdSeasonMonth();
            foreach (HouseholdState household in populationManager.State.households)
            {
                if (household == null) continue;
                List<int> members = householdMemberships.GetActiveMembers(household.id);
                if (members.Count == 0) continue;
                var inputs = new DomesticLaborDriver.DomesticDayInputs
                {
                    HouseholdId = household.id,
                    MemberIds = members,
                    AgeBandFor = personId =>
                    {
                        PersonState person = populationManager.State.GetPerson(personId);
                        return person != null ? person.ageBand : AgeBand.Adult18Plus;
                    },
                    MealsAtHomeCount = members.Count,
                    ColdSeason = coldSeason,
                    HasAnimals = HouseholdOwnsAnimals(household.id, members),
                };
                hub.DomesticLaborDriver.DriveDay(inputs, absoluteDayIndex, hub.Tasks,
                    hub.WorkTimeBudgets, hub.PersonSchedules, hub.DomesticLabor,
                    hub.DueEvents, diag);
            }

            if (diag.Count > 0)
            {
                Debug.Log("[SimulationDrivers] domestic labor day " + absoluteDayIndex + ": " +
                    string.Join(" | ", diag));
            }
        }

        private bool HouseholdOwnsAnimals(int householdId, List<int> memberIds)
        {
            if (hub?.Animals == null) return false;
            string householdKey = householdId.ToString();
            var memberKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (int memberId in memberIds) memberKeys.Add(memberId.ToString());
            foreach (AnimalState animal in hub.Animals.ActiveAnimals)
            {
                if (animal == null) continue;
                if (animal.OwnerKind == AnimalOwnerKind.Household
                    && string.Equals(animal.OwnerId, householdKey, StringComparison.Ordinal))
                    return true;
                if (animal.OwnerKind == AnimalOwnerKind.Person
                    && memberKeys.Contains(animal.OwnerId ?? string.Empty))
                    return true;
            }

            return false;
        }

        private void DriveTenancyPayments(int absoluteDayIndex)
        {
            if (hub == null) return;
            tenancyPayees.Bind(populationManager, hub.HouseholdLedgers, sharedBusinessRuntime);
            var diagnostics = new List<string>();
            List<TenancySettlementResult> results = hub.TenancyPayments.SettleDueRents(
                hub.Housing,
                absoluteDayIndex,
                hub.HouseholdLedgers,
                tenancyPayees,
                hub.FinancialObligations,
                hub.Ids,
                diagnostics);
            if (results.Count > 0)
            {
                Debug.Log($"[SimulationDrivers] tenancy day {absoluteDayIndex}: " +
                    string.Join(" | ", results.Select(result =>
                        $"{result.AgreementId}:{result.Outcome}:{result.PaidCents}c")));
            }
            if (diagnostics.Count > 0)
            {
                Debug.LogWarning("[SimulationDrivers] tenancy: " + string.Join(" | ", diagnostics));
            }
        }

        /// <summary>
        /// Pass IX (A2-13): the production off-map journey authority over
        /// the hub's journey model (the hub owns the model; no production
        /// authority instance existed). The dead never travel: the
        /// availability seam refuses deceased travelers at departure.
        /// </summary>
        private OffMapJourneyAuthority OffMapJourneys
        {
            get
            {
                if (offMapJourneys == null)
                {
                    offMapJourneys = new OffMapJourneyAuthority(hub.Journeys);
                    offMapJourneys.IsPersonUnavailable = personId =>
                    {
                        PersonState person = populationManager?.State?.GetPerson(personId);
                        return person == null || person.deathDayIndex >= 0;
                    };
                }

                return offMapJourneys;
            }
        }

        /// <summary>
        /// Pass VIII Worker 2: daily absent-occupant sweep. Absent means
        /// dead (deathDayIndex >= 0) or departed from the town roster
        /// (settlement arrangement Departed, or no longer a known person).
        /// Pass IX (A2-13): the sweep now receives the tested lenient
        /// predicate (AbsenceArrangementPolicies) instead of null — a live
        /// agreement or an active return journey keeps the person's
        /// accommodation; the dead are never exempted. Each resolved
        /// person is also vacated from any boarding house that holds them
        /// so beds free in lockstep with housing occupancies.
        /// </summary>
        private void DriveHousingAbsenceSweep(int absoluteDayIndex)
        {
            if (hub == null || populationManager == null) return;
            var diagnostics = new List<string>();
            var sweep = new HousingAbsenceSweep();
            var bridge = new BoardingLodgingBridge();
            HousingAbsenceSweepResult result = sweep.Sweep(
                hub.Housing,
                IsPersonAbsent,
                absoluteDayIndex,
                diagnostics,
                hasValidAbsenceArrangement: AbsenceArrangementPolicies.LenientPolicy(
                    hub.Housing,
                    OffMapJourneys,
                    absoluteDayIndex,
                    isDeceased: personId =>
                    {
                        PersonState person = populationManager.State.GetPerson(personId);
                        return person != null && person.deathDayIndex >= 0;
                    }),
                onPersonVacated: personId =>
                {
                    if (hub.BoardingHouseRuntimes == null) return;
                    foreach (BoardingHouseShopRuntime runtime in hub.BoardingHouseRuntimes)
                    {
                        if (runtime == null) continue;
                        bridge.VacatePersonFromHouse(runtime, personId, absoluteDayIndex,
                            hub.Housing, hub.LodgingRegistry, diagnostics);
                    }
                });
            if (result.PersonsResolved > 0 || result.OccupanciesEnded > 0)
            {
                Debug.Log($"[SimulationDrivers] housing absence sweep day {absoluteDayIndex}: " +
                    $"{result.PersonsResolved} absent person(s) resolved, {result.OccupanciesEnded} occupanc(ies) ended.");
            }
            if (diagnostics.Count > 0)
            {
                Debug.LogWarning("[SimulationDrivers] housing absence: " + string.Join(" | ", diagnostics));
            }
        }

        private bool IsPersonAbsent(int personId)
        {
            if (populationManager?.State == null) return false;
            PersonState person = populationManager.State.GetPerson(personId);
            if (person == null) return true; // no longer in the town roster
            if (person.deathDayIndex >= 0) return true;
            return person.settlementArrangement == SettlementArrangement.Departed;
        }

        /// <summary>
        /// Pass IX (A2-03): plans every household's day through
        /// DailyActivityPlanner — sleep first (non-interruptible), meals,
        /// job-seeking for unemployed adults, a managing block for business
        /// owners/managers, and rest — reserved on real person time through
        /// the hub's schedule tracker and work-time budgets.
        /// </summary>
        private void DriveDailyActivityPlans(int absoluteDayIndex)
        {
            if (hub == null || populationManager?.State?.households == null || householdMemberships == null) return;
            var diag = new List<string>();
            foreach (HouseholdState household in populationManager.State.households)
            {
                if (household == null) continue;
                List<int> members = householdMemberships.GetActiveMembers(household.id);
                if (members.Count == 0) continue;
                dailyActivityPlanner.PlanHouseholdDay(
                    members,
                    personId =>
                    {
                        PersonState person = populationManager.State.GetPerson(personId);
                        bool employed = person != null && hub.Employments != null
                            && hub.Employments.GetActiveByEmployee(personId).Count > 0;
                        bool manages = person != null && sharedBusinessRuntime != null
                            && sharedBusinessRuntime.Businesses != null
                            && sharedBusinessRuntime.Businesses.Any(b => b?.Owner != null && b.Owner.PersonId == personId);
                        return new DailyActivityPlanner.PersonDayPlanInputs
                        {
                            PersonId = personId,
                            HouseholdId = household.id,
                            AgeBand = person != null ? person.ageBand : AgeBand.Adult18Plus,
                            IsJobSeeker = person != null && !employed && person.ageBand == AgeBand.Adult18Plus,
                            IsManager = manages,
                        };
                    },
                    absoluteDayIndex,
                    hub.PersonSchedules,
                    hub.WorkTimeBudgets,
                    diag);
            }

            if (diag.Count > 0)
            {
                Debug.Log("[SimulationDrivers] daily activity plans day " + absoluteDayIndex + ": " +
                    string.Join(" | ", diag.Take(20)));
            }
        }

        /// <summary>
        /// Pass IX (A2-06 + A2-07): the daily social drive (see
        /// DailySocialDrive). Venues are the town's real businesses that
        /// host socializing (saloon-type first when present); conversations
        /// and job-security routing run over the hub's real authorities.
        /// </summary>
        private void DriveSocialDay(int absoluteDayIndex)
        {
            if (hub == null || populationManager?.State == null) return;
            var diag = new List<string>();
            var venues = new List<string>();
            if (sharedBusinessRuntime != null && sharedBusinessRuntime.Businesses != null)
            {
                foreach (BusinessInstanceState business in sharedBusinessRuntime.Businesses)
                {
                    if (business == null || string.IsNullOrWhiteSpace(business.InstanceId)) continue;
                    if (business.BusinessType == BusinessType.Saloon)
                    {
                        venues.Insert(0, business.InstanceId);
                    }
                    else if (business.BusinessType == BusinessType.Hotel
                        || business.BusinessType == BusinessType.Restaurant
                        || business.BusinessType == BusinessType.BoardingHouse)
                    {
                        venues.Add(business.InstanceId);
                    }
                }
            }

            var ctx = new DailySocialDriveContext
            {
                Population = populationManager.State,
                Schedules = hub.PersonSchedules,
                Employments = hub.Employments,
                Knowledge = hub.KnowledgeTransmissions,
                Recruiting = hub.Recruiting,
                BusinessById = businessId =>
                {
                    if (sharedBusinessRuntime != null
                        && sharedBusinessRuntime.TryFindByInstanceId(businessId, out BusinessInstanceState found))
                    {
                        return found;
                    }

                    return null;
                },
                VenueBusinessIds = venues,
                DayIndex = absoluteDayIndex,
                // Deterministic draws need only a stable seed (same day +
                // person => same visit decision across save/load).
                WorldSeed = 0x5EED5,
            };
            DailySocialDayReport report = DailySocialDrive.DriveDay(ctx, diag);
            if (report.VisitsPlanned > 0 || report.ConversationsHeld > 0 || report.SeekWorkDecisions > 0)
            {
                Debug.Log($"[SimulationDrivers] social day {absoluteDayIndex}: {report.VisitsPlanned} visit(s), " +
                    $"{report.ConversationsHeld} conversation(s), {report.PacketsRecorded} packet(s), " +
                    $"{report.SeekWorkDecisions} seek-work decision(s), {report.InquiriesFiled} inquiry(ies).");
            }
            if (diag.Count > 0)
            {
                Debug.Log("[SimulationDrivers] social: " + string.Join(" | ", diag.Take(20)));
            }
        }

        /// <summary>
        /// Pass IX (A2-09): builds (once) and runs the estate lifecycle
        /// driver over the population manager's real estate service — the
        /// same EstateService mortality opens estates into — advancing each
        /// open estate one stage per day. Authorities are the hub's real
        /// ones; absent registries are skipped by the driver, never faked.
        /// </summary>
        private void DriveEstateAdvancement(int absoluteDayIndex)
        {
            if (hub == null || populationManager == null || populationManager.Estates == null) return;
            if (estateLifecycleDriver == null)
            {
                AcquisitionMarketManager acquisitionMarket = FindAnyObjectByType<AcquisitionMarketManager>();
                var estateCtx = new EstateLifecycleDriverContext
                {
                    Estates = populationManager.Estates,
                    Administration = estateAdministration,
                    Ids = hub.Ids,
                    Kinship = populationManager.Kinship,
                    Population = populationManager.State,
                    Titles = acquisitionMarket != null ? acquisitionMarket.Titles : null,
                    Obligations = hub.FinancialObligations,
                    Credit = hub.CreditInstruments,
                    Animals = hub.Animals,
                    Memberships = householdMemberships,
                    Ledgers = hub.HouseholdLedgers,
                    Contests = estateContests,
                    Probate = estateProbate,
                    CurrentDayIndex = absoluteDayIndex,
                };
                estateLifecycleDriver = new EstateLifecycleDriver(estateCtx);
            }

            var diag = new List<string>();
            int advanced = estateLifecycleDriver.AdvanceOpenEstatesOneStage(absoluteDayIndex, diag);
            if (advanced > 0 || diag.Count > 0)
            {
                Debug.Log($"[SimulationDrivers] estate advancement day {absoluteDayIndex}: {advanced} estate(s) advanced. " +
                    string.Join(" | ", diag.Take(20)));
            }
        }

        private void DriveBoardingHouseDays(int absoluteDayIndex)
        {
            if (hub == null || hub.BoardingHouseRuntimes == null) return;
            var diagnostics = new List<string>();
            foreach (BoardingHouseShopRuntime runtime in hub.BoardingHouseRuntimes)
            {
                if (runtime == null) continue;
                runtime.ExecuteDay(absoluteDayIndex, 0, diagnostics);
            }
            if (diagnostics.Count > 0)
                Debug.Log("[SimulationDrivers] boarding-house: " + string.Join(" | ", diagnostics));
        }

        private void DriveBoardingHouseWeeks(int absoluteDayIndex)
        {
            if (hub == null || hub.BoardingHouseRuntimes == null) return;
            var diagnostics = new List<string>();
            foreach (BoardingHouseShopRuntime runtime in hub.BoardingHouseRuntimes)
            {
                if (runtime == null) continue;
                List<BoarderRentDue> dues = runtime.ExecuteWeek(absoluteDayIndex, diagnostics);
                hub.RentCollection.SettleDues(
                    dues,
                    runtime,
                    hub.HouseholdLedgers,
                    householdMemberships,
                    hub.FinancialObligations,
                    hub.Ids,
                    absoluteDayIndex,
                    diagnostics);
            }
            if (diagnostics.Count > 0)
                Debug.Log("[SimulationDrivers] boarding-house weekly: " + string.Join(" | ", diagnostics));
        }

        private void DriveGenericProduction(int absoluteDayIndex)
        {
            if (hub == null || sharedBusinessRuntime == null) return;
            foreach (BusinessInstanceState business in sharedBusinessRuntime.Businesses)
            {
                if (business == null) continue;
                EntityId businessId = EntityId.For(EntityKind.Business, StableBusinessRuntimeId(business.InstanceId));

                // Policy scheduling is deliberately performed before process
                // advancement. It selects a real employed/owner Person, then
                // routes active work through the shared TaskAuthority. No policy
                // call itself creates goods.
                if (business.GenericConfiguration.ActiveProcesses.All(process => process == null || process.Completed))
                {
                    var workers = new List<EntityId>();
                    if (business.Owner != null && business.Owner.PersonId >= 0)
                    {
                        workers.Add(EntityId.For(EntityKind.Person, business.Owner.PersonId));
                    }
                    if (business.RuntimeState?.EmploymentRegistry != null)
                    {
                        foreach (EmploymentRelationship relationship in business.RuntimeState.EmploymentRegistry.GetActiveByEmployer(business.InstanceId))
                        {
                            if (relationship != null && relationship.EmployeePersonId >= 0)
                            {
                                workers.Add(EntityId.For(EntityKind.Person, relationship.EmployeePersonId));
                            }
                        }
                    }
                    hub.GenericProduction.TryStartEligiblePolicy(business, businessId, absoluteDayIndex, workers,
                        out _, out _);
                }

                if (business.GenericConfiguration.ActiveProcesses.Count == 0) continue;
                hub.GenericProduction.AdvanceBusinessProcesses(
                    business,
                    businessId,
                    absoluteDayIndex,
                    24 * 60);
            }
        }

        private static int StableBusinessRuntimeId(string value)
        {
            unchecked
            {
                int hash = 17;
                foreach (char character in value ?? string.Empty) hash = hash * 31 + character;
                return hash & 0x7fffffff;
            }
        }

        /// <summary>
        /// P6: advances the hub's travel &amp; communication authorities one day.
        /// Safe no-ops when nothing is registered yet (no offices, no weather
        /// attributes, no recruitment efforts): the services loop over empty
        /// registries instead of inventing traffic.
        /// </summary>
        private void DriveTravelAndComms(int absoluteDayIndex)
        {
            if (hub == null) return;
            var diag = new List<string>();

            // The Main Scene's General Store is the real town post office.
            // Rebuild the town-derived journey graph first, then register the
            // office against that actual building node before recruitment or
            // other daily postal activity can post a letter. The stable office
            // id and duplicate guard make New Game/Continue rebinds idempotent.
            EnsureTownJourneyNetwork();
            EnsureGeneralStorePostOffice(diag);

            hub.RouteConditions.AdvanceDay(absoluteDayIndex, hub.WeatherSeed, diag);
            if (hub.RouteConditions.IsBlizzard())
            {
                Debug.LogWarning($"[SimulationDrivers] BLIZZARD day {absoluteDayIndex} — " +
                    "roads closed; journeys route around or refuse (NX-2C).");
            }

            // R-03: wire the postage payers from real production context —
            // recruitment letters are paid by the hiring business's till
            // (resolved from the shared business runtime), and registered-
            // mail senders / return-receipt requesters are paid by their
            // own household ledger (person -> active household membership
            // -> household ledger; falling back to the person's recorded
            // household id). Unresolvable senders get no payer.
            if (sharedBusinessRuntime != null)
            {
                hub.Recruiting.SenderBusinessRuntimeResolver = id =>
                {
                    BusinessInstanceState found;
                    return sharedBusinessRuntime.TryFindByInstanceId(id, out found) ? found.RuntimeState : null;
                };
            }

            hub.RegisteredMail.PersonHouseholdLedgerResolver = personId =>
            {
                if (personId < 0) return null;
                int householdId = householdMemberships != null ? householdMemberships.GetActiveHouseholdId(personId) : -1;
                if (householdId < 0 && populationManager != null && populationManager.State != null)
                {
                    PersonState person = populationManager.State.GetPerson(personId);
                    if (person != null) householdId = person.householdId;
                }

                return householdId >= 0 ? hub.HouseholdLedgers.Get(householdId) : null;
            };

            hub.Postal.AdvanceDay(hub.Journeys, absoluteDayIndex, diag);
            hub.MoneyOrders.AdvanceDay(absoluteDayIndex, diag);
            hub.RegisteredMail.AuditOverdue(absoluteDayIndex, diag);
            // G-02: postal revenue collection — earned postage reaches the
            // owning office business's ledger.
            DrivePostalRevenue(absoluteDayIndex);

            // T2B: open efforts accrue inquiries from real persons; letters
            // resolve — postal-routed ones only when the mail item really
            // arrived (no instant information transfer).
            if (populationManager != null && populationManager.State != null)
            {
                hub.Recruiting.AdvanceDay(
                    populationManager.State, hub.Employments, absoluteDayIndex, diag, hub.Postal);
            }
        }

        private void EnsureGeneralStorePostOffice(List<string> diagnostics)
        {
            if (hub == null || sharedBusinessRuntime == null || townWorld == null)
            {
                return;
            }

            foreach (PostalOffice existing in hub.Postal.Offices)
            {
                if (existing != null && string.Equals(existing.OfficeId, "general-store", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            BusinessInstanceState store = null;
            foreach (BusinessInstanceState business in sharedBusinessRuntime.Businesses)
            {
                if (business != null && business.BusinessType == BusinessType.GeneralStore
                    && business.AssignedBuildingId >= 0)
                {
                    store = business;
                    break;
                }
            }

            if (store == null)
            {
                return;
            }

            string locationId = TownJourneyNetworkBuilder.LocationIdForBuilding(store.AssignedBuildingId);
            if (hub.Journeys.GetLocation(locationId) == null)
            {
                diagnostics?.Add($"Postal office registration deferred: General Store {store.InstanceId} has no registered journey location '{locationId}'.");
                return;
            }

            int seed = townWorld.Settings != null ? townWorld.Settings.seed : 0;
            string refusal = hub.Postal.RegisterOffice(
                "general-store",
                locationId,
                store.InstanceId,
                departureWeekdays: null,
                seed: seed,
                diag: diagnostics);
            if (refusal != null)
            {
                diagnostics?.Add(refusal);
            }
        }

        // Pass IX WS3 (PASS8-002 / A4-11): production freight ownership.
        // FreightProductionDriver is the system of record for detailed
        // shipments; one FreightCompanyRuntime per live freight business is
        // constructed here, through the production bootstrap path.
        private LandLedgers.Economy.Freight.FreightProductionDriver freightProduction;
        private LandLedgers.Economy.Logistics.FreightShipmentService freightShipmentService;

        /// <summary>
        /// Pass IX WS3: the production freight driver (detailed shipments).
        /// Scene/bootstrap code reaches the per-business FreightCompanyRuntime
        /// instances through here.
        /// </summary>
        public LandLedgers.Economy.Freight.FreightProductionDriver FreightProduction
        {
            get
            {
                LandLedgers.Persistence.SaveLoadManager save = FindAnyObjectByType<LandLedgers.Persistence.SaveLoadManager>();
                LandLedgers.Persistence.Series2Pass9SaveState pass9 = save != null ? save.Series2Pass9State : null;
                if (pass9?.FreightDriver != null && !ReferenceEquals(freightProduction, pass9.FreightDriver))
                {
                    freightProduction = pass9.FreightDriver;
                    freightShipmentService = freightProduction.Service;
                }
                freightShipmentService ??= new LandLedgers.Economy.Logistics.FreightShipmentService();
                freightProduction ??= new LandLedgers.Economy.Freight.FreightProductionDriver(freightShipmentService);
                if (pass9 != null && pass9.FreightDriver == null)
                {
                    pass9.FreightDriver = freightProduction;
                    Debug.Log("[FreightSave] SimulationDrivers registered its freight driver with the Pass 9 save owner.", this);
                }
                return freightProduction;
            }
        }

        /// <summary>
        /// Pass IX WS3: constructs the production FreightCompanyRuntime for
        /// one freight business (idempotent) and binds its ledger runtime.
        /// </summary>
        public LandLedgers.Economy.Freight.FreightCompanyRuntime EnsureFreightCompany(BusinessInstanceState business)
        {
            if (business == null || string.IsNullOrWhiteSpace(business.InstanceId))
            {
                return null;
            }

            return FreightProduction.EnsureCompany(business.InstanceId, business.RuntimeState);
        }

        /// <summary>
        /// Pass IX WS3: ensures every live LiveryFreight business has its
        /// production FreightCompanyRuntime, then advances all detailed
        /// shipments by a day of game time through the full lifecycle
        /// (loading → transport → arrival → unloading → acceptance →
        /// settlement). The receiving authority is the destination
        /// business's real category stock: accepted units are exactly the
        /// units that stock took; a destination that cannot take the goods
        /// refuses, and the shipment stays in wagon custody (Delayed) with
        /// no revenue. Shipments wait honestly when the carrier has no real
        /// handling person (owner or active employee).
        /// </summary>
        private void DriveFreightCompanies(int absoluteDayIndex)
        {
            if (hub == null || sharedBusinessRuntime == null)
            {
                return;
            }

            EntityId handler = EntityId.Invalid;
            foreach (BusinessInstanceState business in sharedBusinessRuntime.Businesses)
            {
                if (business == null || business.BusinessType != BusinessType.LiveryFreight)
                {
                    continue;
                }

                EnsureFreightCompany(business);
                if (!handler.IsValid)
                {
                    handler = ResolveFreightHandler(business);
                }
            }

            if (FreightProduction.Companies.Count == 0)
            {
                // Request/meeting diagnostics must not be hidden behind the
                // shipment driver's empty-company guard. The request path
                // performs its own participant/type checks and can explain
                // why B3 was not registered as a carrier.
                DriveMillbrookFreightNegotiation(absoluteDayIndex);
                return;
            }

            // R-04: register every live business runtime with the freight
            // driver so each shipment's named payer resolves to its real
            // till for verified collection (payer debited before the
            // carrier is credited; unresolvable payers stay uncollected).
            foreach (BusinessInstanceState business in sharedBusinessRuntime.Businesses)
            {
                if (business != null && business.RuntimeState != null)
                {
                    FreightProduction.RegisterBusinessRuntime(business.InstanceId, business.RuntimeState);
                }
            }

            var diag = new List<string>();
            // The detailed freight authority is the single owner of source-lot
            // pickup and destination custody. Register the already-authored
            // opening wagon and bind Millbrook's paid lot to the Bakery's
            // pound-based stock when a commissioned shipment reaches these
            // lifecycle callbacks. No shipment or contract is created here.
            LandLedgers.Economy.Blacksmith.EquipmentAsset openingWagon = hub.TransportAssets?.Get("opening-freight-wagon");
            if (openingWagon != null)
                FreightProduction.Service.RegisterWagon(openingWagon);

            LandLedgers.Persistence.SaveLoadManager save = FindAnyObjectByType<LandLedgers.Persistence.SaveLoadManager>();
            LandLedgers.Economy.Businesses.Bakery.BakeryShopRuntime bakery =
                save?.Series2Pass9State?.BakeryRuntime ?? save?.Series2Pass9State?.BakeryDriver?.Bakery;
            LandLedgers.Economy.Businesses.GeneralStore.MillbrookFlourFreightCustodyAdapter millbrookCustody =
                millbrookAidRuntime != null && bakery != null && hub.Ids != null
                    ? new LandLedgers.Economy.Businesses.GeneralStore.MillbrookFlourFreightCustodyAdapter(
                        millbrookAidRuntime, bakery, hub.Ids)
                    : null;
            var hooks = new LandLedgers.Economy.Logistics.FreightShipmentHooks
            {
                Tasks = hub.Tasks,
                Budgets = hub.WorkTimeBudgets,
                DayIndex = absoluteDayIndex,
                Diagnostics = diag,
                Reservations = hub.Reservations,
                NowGameSeconds = AbsoluteNowGameSeconds(),
                ReceiveShipment = ReceiveFreightIntoDestinationStock,
            };
            if (millbrookCustody != null)
            {
                hooks.CommitSourceLot = (LandLedgers.Economy.LogisticsShipmentState shipment,
                    int quantityUnits, out string reason) =>
                    millbrookCustody.CommitSourceLot(shipment, quantityUnits, out reason);
                hooks.ReceiveCargoLot = (LandLedgers.Economy.LogisticsShipmentState shipment,
                    int quantityUnits, out string reason) =>
                    millbrookCustody.ReceiveCargoLot(shipment, quantityUnits, absoluteDayIndex, out reason);
            }
            RetryPendingFreightShipmentRegistrations(absoluteDayIndex);
            FreightProduction.AdvanceTick(86400f, hooks, handler, diag);
            if (diag.Count > 0)
            {
                Debug.Log("[SimulationDrivers] freight day " + absoluteDayIndex + ": " +
                    string.Join(" | ", diag));
            }
            DrivePreparedFreightDispatchAssignments(absoluteDayIndex);
            DriveMillbrookFreightNegotiation(absoluteDayIndex);
        }

        private void DrivePreparedFreightDispatchAssignments(int dayIndex)
        {
            if (FreightProduction?.ServiceAgreements?.Agreements == null) return;
            foreach (FreightServiceAgreement agreement in FreightProduction.ServiceAgreements.Agreements.ToList())
            {
                if (agreement == null || agreement.Status != FreightServiceAgreementStatus.Accepted
                    || agreement.PreparedGameSeconds <= 0f
                    || !string.Equals(agreement.FulfilmentStatus, "Prepared for dispatch", StringComparison.Ordinal)) continue;
                BusinessInstanceState freight = FindBusiness(agreement.CarrierBusinessId);
                LogisticsShipmentState shipment = FreightProduction.AllShipments().FirstOrDefault(candidate => candidate != null
                    && string.Equals(candidate.ShipmentId, agreement.ShipmentId, StringComparison.Ordinal));
                AnimalState horse = FindOpeningFreightHorse(agreement.CarrierBusinessId);
                EquipmentAsset wagon = hub?.TransportAssets?.Get("opening-freight-wagon");
                if (freight == null || shipment == null || horse == null || wagon == null)
                {
                    string missing = string.Join(", ", new[]
                    {
                        freight == null ? "carrier business" : null,
                        shipment == null ? "linked shipment" : null,
                        horse == null ? "A0" : null,
                        wagon == null ? "assigned wagon" : null
                    }.Where(value => value != null));
                    LogFreightAgreementBlockerOnce("dispatch-prerequisite:" + agreement.AgreementId,
                        $"[FreightDispatch] Accepted agreement '{agreement.AgreementId}' remains prepared; dispatch assignment deferred because {missing} is unresolved.");
                    continue;
                }
                CreatePreparedFreightDispatchTask(agreement, shipment, freight, horse, wagon, dayIndex);
            }
        }

        private void DriveMillbrookFreightNegotiation(int dayIndex)
        {
            if (hub == null || sharedBusinessRuntime == null || populationManager?.State == null
                || millbrookAidRuntime == null || developmentTownConfiguration == null)
            {
                string missing = string.Join(", ", new[]
                {
                    hub == null ? "SimulationSystemsHub" : null,
                    sharedBusinessRuntime == null ? "SharedBusinessRuntimeManager" : null,
                    populationManager?.State == null ? "population state" : null,
                    millbrookAidRuntime == null ? "Millbrook runtime" : null,
                    developmentTownConfiguration == null ? "development-town rate configuration" : null
                }.Where(value => value != null));
                LogFreightAgreementBlockerOnce("dependencies", $"[FreightAgreement] Daily request driver is connected on day {dayIndex}, but cannot evaluate the opening order because {missing} is unresolved.");
                return;
            }
            if (!sharedBusinessRuntime.TryFindByInstanceId("business_bakery_001", out BusinessInstanceState bakery)
                && !sharedBusinessRuntime.TryFindByInstanceId("bakery_001", out bakery))
                bakery = sharedBusinessRuntime.Businesses.FirstOrDefault(b => b != null && b.BusinessType == BusinessType.Bakery);
            BusinessInstanceState carrier = null;
            bool carrierFound = sharedBusinessRuntime.TryFindByInstanceId("livery_freight_003", out carrier);
            if (bakery?.RuntimeState == null || bakery.Owner == null || bakery.Owner.PersonId < 0
                || !carrierFound
                || carrier?.Owner == null || carrier.Owner.PersonId < 0
                || carrier.BusinessType != BusinessType.LiveryFreight)
            {
                string state = $"Bakery={(bakery == null ? "missing" : $"{bakery.InstanceId},owner={bakery.Owner?.PersonId ?? -1},cashPort={bakery.RuntimeState != null}")}; " +
                    $"B3={(carrier == null ? "missing" : $"{carrier.InstanceId},owner={carrier.Owner?.PersonId ?? -1},cashPort={carrier.RuntimeState != null}")}; " +
                    $"businesses={string.Join(",", sharedBusinessRuntime.Businesses.Where(b => b != null).Select(b => b.InstanceId + "/" + b.BusinessType))}.";
                LogFreightAgreementBlockerOnce("businesses", $"[FreightAgreement] Daily request driver cannot resolve its participants on day {dayIndex}: {state}");
                return;
            }

            OffMapAidCargoLot lot = millbrookAidRuntime.Lots.FirstOrDefault(candidate => candidate != null
                && string.Equals(candidate.LotId, "aidlot-millbrook_grain_co:opening-flour-b1", StringComparison.Ordinal)
                && string.Equals(candidate.BuyerBusinessId, bakery.InstanceId, StringComparison.Ordinal));
            if (lot == null || lot.SourceCommitted || lot.Received)
            {
                string lotState = lot == null
                    ? $"target paid lot absent; Millbrook='{millbrookAidRuntime.BusinessId}', lotCount={millbrookAidRuntime.Lots?.Count ?? 0}, candidates={string.Join(",", millbrookAidRuntime.Lots?.Where(item => item != null).Select(item => item.LotId + "/buyer=" + item.BuyerBusinessId) ?? Enumerable.Empty<string>())}"
                    : $"lot='{lot.LotId}', sourceCommitted={lot.SourceCommitted}, received={lot.Received}, buyer='{lot.BuyerBusinessId}', SKU='{lot.SkuId}', quantity={lot.PackageUnits} {lot.OrderUnit}";
                LogFreightAgreementBlockerOnce("lot", $"[FreightAgreement] Daily request driver cannot open a request on day {dayIndex}: {lotState}.");
                return;
            }

            FreightServiceAgreementBook book = FreightProduction.ServiceAgreements;
            const string requestId = "freight-request:opening-flour-b1";
            FreightServiceRequest request = book.FindRequest(requestId);
            if (request == null)
            {
                request = new FreightServiceRequest
                {
                    RequestId = requestId,
                    RequestingBusinessId = bakery.InstanceId,
                    RequestingPersonId = bakery.Owner.PersonId,
                    CarrierBusinessId = carrier.InstanceId,
                    CarrierOperatorPersonId = carrier.Owner.PersonId,
                    PickupBusinessId = millbrookAidRuntime.BusinessId,
                    PickupLocationId = "offmap-pickup:" + millbrookAidRuntime.BusinessId,
                    DestinationBusinessId = bakery.InstanceId,
                    DestinationLocationId = bakery.AssignedBuildingId >= 0
                        ? TownJourneyNetworkBuilder.LocationIdForBuilding(bakery.AssignedBuildingId) : string.Empty,
                    CargoLotId = lot.LotId,
                    CargoSkuId = lot.SkuId,
                    Quantity = lot.PackageUnits,
                    QuantityUnit = lot.OrderUnit,
                    RequestedService = "One-time collection of the paid Millbrook flour lot and delivery to Bakery B1",
                    IsRecurring = false
                };
                string createRefusal = book.CreateRequest(request, out FreightServiceRequest duplicate);
                if (createRefusal != null)
                {
                    if (!string.Equals(createRefusal, "Freight request already exists; duplicate request creation refused.", StringComparison.Ordinal))
                        Debug.LogWarning($"[FreightAgreement] Request for lot '{lot.LotId}' refused: {createRefusal}", this);
                    return;
                }
                Debug.Log($"[FreightAgreement] One-time freight request '{requestId}' recorded: Glen P{bakery.Owner.PersonId}/B1 '{bakery.InstanceId}' " +
                    $"requests carrier B3 '{carrier.InstanceId}' for supplier '{millbrookAidRuntime.BusinessId}', lot='{lot.LotId}', " +
                    $"quantity={lot.PackageUnits} {lot.OrderUnit}; status=Open. No freight payment or pickup occurred.", this);
            }

            if (request.Status == FreightServiceRequestStatus.Accepted || request.Status == FreightServiceRequestStatus.Refused
                || request.Status == FreightServiceRequestStatus.MeetingScheduled) return;
            if (!string.IsNullOrWhiteSpace(request.ContactTravelTaskId))
            {
                WorkTask existingTravel = hub.Tasks.FindTaskByIdString(request.ContactTravelTaskId);
                if (existingTravel != null && existingTravel.IsOpen) return;
                request.ContactTravelTaskId = string.Empty;
            }

            PersonState glen = populationManager.State.GetPerson(request.RequestingPersonId);
            PersonState ethan = populationManager.State.GetPerson(request.CarrierOperatorPersonId);
            if (glen == null || ethan == null || ethan.homeBuildingId < 0 ||
                request.DestinationBusinessId != bakery.InstanceId)
            {
                Debug.LogWarning($"[FreightAgreement] Request '{requestId}' remains open: Glen/Ethan or B3's meeting location is unresolved " +
                    $"(Glen={glen != null}, Ethan={ethan != null}, Ethan home={ethan?.homeBuildingId}).", this);
                return;
            }

            EnsureTownJourneyNetwork();
            string origin = ResolvePersonJourneyLocation(glen.id);
            string meetingLocation = TownJourneyNetworkBuilder.LocationIdForBuilding(ethan.homeBuildingId);
            JourneyRoute route = !string.IsNullOrWhiteSpace(origin) && !string.IsNullOrWhiteSpace(meetingLocation)
                ? hub.Journeys.FindRoute(origin, meetingLocation, TravelMode.Foot) : null;
            if (route == null || !route.Found)
            {
                string refusal = route?.Diagnostic ?? "Glen's current location or Ethan's meeting location has no authored route.";
                Debug.LogWarning($"[FreightAgreement] Request '{requestId}' remains open: Glen cannot physically reach B3 staging: {refusal}", this);
                return;
            }

            var trip = new FreightMeetingTripTaskData
            {
                RequestId = requestId,
                RequestingPersonId = glen.id,
                CarrierOperatorPersonId = ethan.id,
                MeetingBuildingId = ethan.homeBuildingId,
                MeetingLocationId = meetingLocation
            };
            WorkTask travel = hub.Tasks.CreateTaskWithPlannedMinutes(JourneyTravel.TravelTaskId,
                EntityId.For(EntityKind.Business, StableBusinessRuntimeId(bakery.InstanceId)), dayIndex,
                Math.Max(1, route.TotalMinutes), customerRef: FreightMeetingTaskPrefix + "travel:" + requestId,
                termsRef: JsonUtility.ToJson(trip));
            request.ContactTravelTaskId = travel.TaskId.ToString();
            if (!TryStartBusinessPersonTask(travel, glen, dayIndex, PersonActivityKind.Travel,
                    ethan.homeBuildingId, out string assignmentRefusal))
            {
                Debug.LogWarning($"[FreightAgreement] Glen's contact travel for request '{requestId}' was not assigned: {assignmentRefusal}", this);
                return;
            }
            Debug.Log($"[FreightAgreement] Glen P{glen.id} was assigned contact travel task '{travel.TaskId}' to Ethan P{ethan.id} " +
                $"at B3 staging building {ethan.homeBuildingId}; route={route.TotalMiles:0.00} mi local, " +
                $"planned estimate={route.TotalMinutes} min, task completes only on visible arrival. Request='{requestId}'.", this);
        }

        private void LogFreightAgreementBlockerOnce(string key, string message)
        {
            if (freightAgreementBlockersLogged.Add(key))
                Debug.LogWarning(message, this);
        }

        private EntityId ResolveFreightHandler(BusinessInstanceState carrier)
        {
            if (carrier?.Owner != null && carrier.Owner.PersonId >= 0)
            {
                return EntityId.For(EntityKind.Person, carrier.Owner.PersonId);
            }

            EmploymentRelationshipRegistry registry = carrier?.RuntimeState?.EmploymentRegistry ?? hub?.Employments;
            if (registry != null && carrier != null)
            {
                foreach (EmploymentRelationship relationship in registry.GetActiveByEmployer(carrier.InstanceId))
                {
                    if (relationship != null && relationship.EmployeePersonId >= 0)
                    {
                        return EntityId.For(EntityKind.Person, relationship.EmployeePersonId);
                    }
                }
            }

            return EntityId.Invalid;
        }

        private int ReceiveFreightIntoDestinationStock(
            LogisticsShipmentState shipment, int quantityUnits, out string reason)
        {
            reason = string.Empty;
            if (shipment == null || quantityUnits <= 0 || sharedBusinessRuntime == null)
            {
                reason = "no shipment or nothing to receive";
                return 0;
            }

            if (!sharedBusinessRuntime.TryFindByInstanceId(
                    shipment.destinationBusinessInstanceId, out BusinessInstanceState destination)
                || destination?.RuntimeState == null)
            {
                reason = $"destination '{shipment.destinationBusinessInstanceId}' has no live business runtime";
                return 0;
            }

            string categoryId = !string.IsNullOrWhiteSpace(shipment.destinationCategoryId)
                ? shipment.destinationCategoryId
                : shipment.sourceCategoryId;
            if (string.IsNullOrWhiteSpace(categoryId))
            {
                reason = "shipment names no destination stock category";
                return 0;
            }

            int accepted = destination.RuntimeState.AddCategoryStockUnits(categoryId, quantityUnits);
            if (accepted < quantityUnits)
            {
                reason = $"destination stock accepted {accepted}/{quantityUnits} units of {categoryId}";
            }

            return accepted;
        }

        /// <summary>
        /// G-02: postal revenue collection — scans the post office's uncollected
        /// postage and posts it to the owning office business's ledger with
        /// provenance (Canon XIII §13.2). The pot drains only when the owning
        /// business runtime resolves; otherwise it is left intact — never
        /// lost, never manufactured.
        ///
        /// Freight note (Pass IX WS3 update): FreightCompanyRuntime instances
        /// are now production-owned — see DriveFreightCompanies in this
        /// region, which constructs one runtime per live LiveryFreight
        /// business and advances detailed shipments daily.
        /// </summary>
        private void DrivePostalRevenue(int absoluteDayIndex)
        {
            // Pass IX WS3: detailed freight advances on the same daily drive.
            DriveFreightCompanies(absoluteDayIndex);
            if (hub == null || hub.Postal == null || sharedBusinessRuntime == null) return;
            PostalService postal = hub.Postal;
            int uncollected = postal.FindUncollectedPostageCents();
            if (uncollected <= 0) return;

            BusinessRuntimeState officeRuntime = null;
            if (postal.Offices != null)
            {
                foreach (PostalOffice office in postal.Offices)
                {
                    if (office == null || string.IsNullOrWhiteSpace(office.BusinessInstanceId)) continue;
                    foreach (BusinessInstanceState business in sharedBusinessRuntime.Businesses)
                    {
                        if (business != null && string.Equals(business.InstanceId,
                            office.BusinessInstanceId, StringComparison.OrdinalIgnoreCase))
                        {
                            officeRuntime = business.RuntimeState;
                            break;
                        }
                    }

                    break; // the first office owns the service-wide pot
                }
            }

            var diag = new List<string>();
            if (officeRuntime == null)
            {
                diag.Add($"Postal revenue: {uncollected}c uncollected but no owning office business " +
                    "runtime resolved — pot left intact, never drained blind.");
            }
            else
            {
                postal.CollectAndPostPostageRevenue(officeRuntime, diag);
            }

            if (diag.Count > 0)
            {
                Debug.Log("[SimulationDrivers] postal revenue day " + absoluteDayIndex + ": " +
                    string.Join(" | ", diag));
            }
        }

        private void DriveDailyNeeds(int absoluteDayIndex)
        {
            if (hub == null) return;
            if (populationManager == null || populationManager.State == null)
            {
                return; // no population yet — nothing to feed
            }
            EnsureTownJourneyNetwork();
            // P6: fall back to the hub's journey model when the driver carries
            // no explicit one — the model is hub-owned now. Missing locations
            // block embodied errands, but not household inventory consumption.
            JourneyModel journeys = Journeys ?? hub.Journeys;
            if (!HasAnyLocation(journeys))
            {
                // A missing route network prevents embodied errands, but it
                // must not suppress local, lot-backed meal consumption. The
                // needs service can still settle food already held by the
                // household; the shopping loop will leave any unmet purchase
                // need open when no valid destination can be reached.
                Debug.LogWarning("[SimulationDrivers] No journey locations are registered; " +
                    "daily needs will consume eligible household stock, while new errands remain blocked.");
            }

            // PopulationManager owns the current membership registry and may
            // replace it on New Game or restore. Bind that authority before
            // generating or executing household purchasing plans.
            supplierDirectory ??= new SupplierDirectory();
            HouseholdMembershipRegistry populationMemberships = populationManager.Memberships;
            if (populationMemberships != null
                && !ReferenceEquals(householdMemberships, populationMemberships))
            {
                householdMemberships = populationMemberships;
            }
            BuildHouseholdShoppingLoop();

            dailyNeedsService ??= new DailyNeedsService();
            consumptionPlanner ??= new HouseholdConsumptionPlanner();
            // P2: wire the legacy person-int id space to HF-1 EntityIds so the
            // travel-time leg of embodied purchasing actually gates on the
            // person's TTS-1 work-time budget (previously always skipped).
            purchaseExecutor ??= new EmbodiedPurchaseExecutor(
                populationManager.State, hub.HouseholdLedgers, supplierDirectory, journeys,
                ResolvePersonJourneyLocation, hub.WorkTimeBudgets, pid => EntityId.For(EntityKind.Person, pid),
                hub.HouseholdInventories);

            // P2: W2B/W2C/W3B nutrition links — count real prepared meals
            // served by live eating-house runtimes (Canon §2.5) so households
            // never double-feed a member who genuinely ate out. Empty
            // registries compose to zero meals: pre-W2B/W2C/W3B behavior.
            SimulationSystemsHub.BuildMealSourceComposites(
                hub.RestaurantMealSources,
                hub.BoardingHouseMealSources,
                hub.HotelMealSources,
                out var restaurantMeals,
                out var boardingMeals,
                out var hotelMeals);

            // Kennedy's current development rule: open food errands at about
            // one day of stock, before today's meal resolution can consume
            // the last reserve. This is a scenario-side playtest setting;
            // the shortage authority's general default remains unchanged.
            var diag = new List<string>();
            DriveHouseholdShortageMonitor(absoluteDayIndex, diag);
            IEmbodiedPurchaseExecutor dailyPurchaseExecutor = householdShoppingLoop == null
                ? purchaseExecutor
                : null;
            DailyNeedsService.DayReport report = dailyNeedsService.ExecuteDay(
                populationManager.State, consumptionPlanner, dailyPurchaseExecutor,
                absoluteDayIndex, diag, hub.WorkTimeBudgets,
                restaurantMeals, boardingMeals, hotelMeals,
                hub.HouseholdInventories, hub.MealLog, null, populationMemberships);
            if (report.MealsMissed > 0 || report.PurchasesMade > 0)
            {
                Debug.Log($"[SimulationDrivers] daily needs day {absoluteDayIndex}: " +
                    $"{report.MealsEaten} eaten, {report.MealsMissed} missed, " +
                    $"{report.PurchasesMade} purchases ({report.SpendCents}c).");
            }

            // Keep the daily summary auditable by resident. These are read-only
            // projections of the meal authority's persisted records; they do
            // not create meals or affect nutrition.
            if (hub.MealLog != null && populationManager.State.households != null)
            {
                foreach (HouseholdState household in populationManager.State.households)
                {
                    if (household == null) continue;
                    foreach (HouseholdMealRecord meal in hub.MealLog.GetMeals(household.id))
                    {
                        if (meal == null || meal.DayIndex != absoluteDayIndex) continue;
                        PersonState eater = populationManager.State.GetPerson(meal.PersonId);
                        string consumed = meal.ItemsConsumed == null || meal.ItemsConsumed.Count == 0
                            ? "recorded external meal"
                            : string.Join(", ", meal.ItemsConsumed.Select(item =>
                                $"{item.ItemId} x{item.QuantityUnits}"));
                        Debug.Log($"[FoodEconomy] meal served: person='{eater?.DisplayName ?? $"P{meal.PersonId}"}', " +
                            $"household=H{meal.HouseholdId}, slot={meal.Slot}, source={meal.SourceKind}, " +
                            $"consumed={consumed}.");
                    }

                    int cashCents = hub.HouseholdLedgers.Get(household.id)?.GetBalanceCents() ?? 0;
                    foreach (MissedMealRecord missed in hub.MealLog.GetMissedMeals(household.id))
                    {
                        if (missed == null || missed.DayIndex != absoluteDayIndex) continue;
                        PersonState person = populationManager.State.GetPerson(missed.PersonId);
                        HouseholdInventory inventory = hub.HouseholdInventories.Get(household.id);
                        int availableFoodUnits = 0;
                        if (inventory != null)
                        {
                            foreach (string itemId in MealSchedulingPolicy.Default.MealItemPriority)
                            {
                                HouseholdItemDefinition definition = HouseholdItemCatalog.Get(itemId);
                                if (definition != null && definition.IsFood)
                                    availableFoodUnits += inventory.GetAvailableUnits(itemId);
                            }
                        }
                        Debug.LogWarning($"[FoodEconomy] meal missed: person='{person?.DisplayName ?? $"P{missed.PersonId}"}', " +
                            $"household=H{missed.HouseholdId}, slot={missed.Slot}, reason='{missed.Reason}', " +
                            $"food stock at meal resolution={availableFoodUnits} units, " +
                            $"household cash={cashCents}c. Shopping is a separate later step; cash alone does not prove a valid seller or stock.", this);
                    }
                }
            }

            DriveEmbodiedShopping(absoluteDayIndex, diag);
            DriveHouseholdAutonomy(absoluteDayIndex, diag);
            if (diag.Count > 0)
            {
                // Keep failed household needs observable in production logs.
                // These diagnostics explain blocked journeys, suppliers,
                // affordability and unmet meals; they do not alter state.
                const int maxDiagnosticsPerDay = 12;
                var visible = new List<string>(Math.Min(diag.Count, maxDiagnosticsPerDay));
                foreach (string message in diag)
                {
                    if (visible.Count >= maxDiagnosticsPerDay) break;
                    if (!string.IsNullOrWhiteSpace(message)
                        && message.StartsWith("[FoodEconomy] purchase", StringComparison.Ordinal))
                        visible.Add(message);
                }
                for (int i = 0; i < diag.Count && visible.Count < maxDiagnosticsPerDay; i++)
                {
                    if (!string.IsNullOrWhiteSpace(diag[i]) && !visible.Contains(diag[i]))
                        visible.Add(diag[i]);
                }

                string omitted = diag.Count > visible.Count
                    ? $" | ... {diag.Count - visible.Count} additional diagnostic(s) omitted"
                    : string.Empty;
                Debug.LogWarning($"[SimulationDrivers] household needs day {absoluteDayIndex}: " +
                    string.Join(" | ", visible) + omitted, this);
            }
        }

        /// <summary>
        /// Pass VII Workstream B (P1-5): production daily horse care. Every
        /// active horse is fed from the farm's real, finite feed stock
        /// (HorseFeedStockAdapter) and recovers through
        /// HorseAuthority.ApplyCare; horses the stock cannot cover degrade and
        /// are named loudly. A horse holding a draft reservation eats the
        /// working ration — draft power has honest upkeep.
        /// </summary>
        private void DriveBusinessHorseFeedProcurement(int absoluteDayIndex)
        {
            if (hub == null || sharedBusinessRuntime == null || populationManager?.State == null
                || hub.Animals == null || hub.Journeys == null) return;
            if (!sharedBusinessRuntime.TryFindByInstanceId("livery_freight_003", out BusinessInstanceState freight)
                || freight?.RuntimeState == null || freight.Owner == null || freight.Owner.PersonId < 0) return;

            var ownedHorses = new List<AnimalState>();
            int dailyFeedUnits = 0;
            int carriedFeedUnits = 0;
            foreach (AnimalState horse in hub.Animals.ActiveAnimals)
            {
                if (horse == null || !horse.IsActive || horse.Species != AnimalSpecies.Horse
                    || horse.OwnerKind != AnimalOwnerKind.Business || horse.OwnerId != freight.InstanceId) continue;
                ownedHorses.Add(horse);
                bool working = hub.DraftPower != null && hub.DraftPower.IsAnimalReserved(horse.AnimalId);
                dailyFeedUnits += Math.Max(1, Mathf.CeilToInt(working
                    ? HorseCareDriver.DailyFeedUnitsPerWorkingHorse
                    : HorseCareDriver.DailyFeedUnitsPerHorse));
                carriedFeedUnits += review2D3SaveState != null
                    ? review2D3SaveState.GetHorseFeedCarry(horse.AnimalId.ToString()) : 0;
            }
            if (ownedHorses.Count == 0 || dailyFeedUnits <= 0) return;

            int sacksBefore = freight.RuntimeState.GetRetailItemStockUnits(HorseFeedSku, HorseFeedCategory);
            int feedUnitsBefore = Math.Max(0, sacksBefore * HorseFeedUnitsPerSack + carriedFeedUnits);
            float daysLeft = (float)feedUnitsBefore / dailyFeedUnits;
            if (daysLeft > HorseFeedTriggerDays)
            {
                businessFeedNeedsOpen.Remove(freight.InstanceId);
                return;
            }

            if (businessFeedNeedsOpen.Add(freight.InstanceId))
                Debug.Log($"[BusinessFeed] Shortage detected for B3 '{freight.InstanceId}': " +
                    $"A0+business-owned horses have {feedUnitsBefore} usable feed unit(s) for {dailyFeedUnits} unit(s)/day " +
                    $"({daysLeft:0.0} days); trigger is {HorseFeedTriggerDays} days or less.", this);

            int sacksNeeded = BusinessFeedPurchase.PackagesToTarget(
                feedUnitsBefore, dailyFeedUnits, HorseFeedTargetDays, HorseFeedUnitsPerSack);
            if (sacksNeeded <= 0) return;
            if (sacksNeeded > HorseShoppingCarryLimitSacks)
            {
                Debug.LogWarning($"[BusinessFeed] Need retained for '{freight.InstanceId}': {daysLeft:0.0} feed days remain, " +
                    $"but {sacksNeeded} sacks exceeds Ethan's {HorseShoppingCarryLimitSacks}-item carrying limit; no purchase occurred.", this);
                return;
            }

            // Keep one need/task open through the physical purchase and return.
            // Do not revalidate the store as if Ethan were free to place a new
            // order while paid cargo is still in his custody.
            if (HasOpenBusinessFeedTask(freight.InstanceId)) return;

            var storeManager = FindAnyObjectByType<LandLedgers.FirstLedger.GeneralStoreRuntimeManager>();
            BusinessInstanceState store = null;
            foreach (BusinessInstanceState candidate in sharedBusinessRuntime.Businesses)
            {
                if (candidate != null && candidate.BusinessType == BusinessType.GeneralStore
                    && candidate.RuntimeState != null) { store = candidate; break; }
            }
            if (storeManager == null || store == null)
            {
                Debug.LogWarning($"[BusinessFeed] Feed need retained for '{freight.InstanceId}': staffed B0 General Store runtime is unavailable; " +
                    "no purchase or task completed.", this);
                return;
            }

            GeneralStoreTradingPort storePort = storeManager.BuildHouseholdTradingPort(store, storeFeedRegistry);
            if (storePort == null || !storePort.UsesItemLevelInventory(HorseFeedSku)
                || storePort.ItemPricePerUnitCents(HorseFeedSku) <= 0
                || storePort.ItemStockUnits(HorseFeedSku) < sacksNeeded)
            {
                string refusal = $"B0 cannot offer {sacksNeeded} '{HorseFeedSku}' sacks at a positive owner-set price " +
                    $"(stock={storePort?.ItemStockUnits(HorseFeedSku) ?? 0}, price={storePort?.ItemPricePerUnitCents(HorseFeedSku) ?? 0}c); no cash or stock moved.";
                if (!businessFeedLastStockRefusal.TryGetValue(freight.InstanceId, out string priorRefusal)
                    || !string.Equals(priorRefusal, refusal, StringComparison.Ordinal))
                {
                    businessFeedLastStockRefusal[freight.InstanceId] = refusal;
                    Debug.LogWarning($"[BusinessFeed] Feed need retained for '{freight.InstanceId}': {refusal}", this);
                }
                return;
            }
            businessFeedLastStockRefusal.Remove(freight.InstanceId);

            PersonState ethan = populationManager.State.GetPerson(freight.Owner.PersonId);
            if (ethan == null)
            {
                Debug.LogWarning($"[BusinessFeed] Feed need retained for '{freight.InstanceId}': owner Person P{freight.Owner.PersonId} is unavailable.", this);
                return;
            }

            if (ethan.homeBuildingId < 0)
            {
                Debug.LogWarning($"[BusinessFeed] Feed need retained for '{freight.InstanceId}': Ethan P{ethan.id} has no home/staging building for the physical errand.", this);
                return;
            }

            LandLedgers.FirstLedger.PopulationPathingDirector pathing =
                FindAnyObjectByType<LandLedgers.FirstLedger.PopulationPathingDirector>();
            bool physicallyAtHome = pathing != null
                && pathing.IsAtExplicitBuilding(ethan.id, ethan.homeBuildingId);
            if (!physicallyAtHome)
            {
                if (businessFeedWaitingForHomeLogged.Add(ethan.id))
                    Debug.Log($"[BusinessFeed] Feed need retained for '{freight.InstanceId}': Ethan P{ethan.id} is not physically at home/staging building {ethan.homeBuildingId}; " +
                        "the purchase trip waits until the existing custom navigation confirms his return there.", this);
                return;
            }
            businessFeedWaitingForHomeLogged.Remove(ethan.id);

            // A feed run begins from B3's defined home/staging location. Do not
            // trust a stale workplace/current-destination value as its origin.
            string startLocation = TownJourneyNetworkBuilder.LocationIdForBuilding(ethan.homeBuildingId);
            string returnLocation = startLocation;
            if (string.IsNullOrWhiteSpace(startLocation) || hub.Journeys.GetLocation(startLocation) == null)
            {
                Debug.LogWarning($"[BusinessFeed] Feed need retained for '{freight.InstanceId}': Ethan has no valid custom-navigation origin.", this);
                return;
            }

            int unitPrice = storePort.ItemPricePerUnitCents(HorseFeedSku);
            int totalCost = checked(unitPrice * sacksNeeded);
            var buyerCash = new Series2BusinessRuntimeCashPort(freight.RuntimeState);
            if (buyerCash.BalanceCents < totalCost)
            {
                Debug.LogWarning($"[BusinessFeed] Feed need retained for '{freight.InstanceId}': B3 has {buyerCash.BalanceCents}c, " +
                    $"purchase requires {totalCost}c; no purchase occurred.", this);
                return;
            }

            CategoryStockState businessFeed = freight.RuntimeState.GetCategoryStock(HorseFeedCategory);
            int storageNeeded = (businessFeed?.CurrentStockUnits ?? 0) + sacksNeeded;
            if ((businessFeed?.TargetStockUnits ?? 0) < storageNeeded)
            {
                int previousCapacity = businessFeed?.TargetStockUnits ?? 0;
                int capacity = freight.RuntimeState.EnsureCategoryStorageCapacity(HorseFeedCategory, storageNeeded);
                businessFeed = freight.RuntimeState.GetCategoryStock(HorseFeedCategory);
                Debug.Log($"[BusinessFeed] B3 animal-feed storage capacity reserved for its finite seven-day horse-feed need: " +
                    $"{previousCapacity}->{capacity} sack(s), current={businessFeed?.CurrentStockUnits ?? 0}, " +
                    $"purchase={sacksNeeded} sack(s). No inventory or cash was created.", this);
            }

            JourneyRoute outwardRoute = hub.Journeys.FindRoute(startLocation, storePort.LocationId, TravelMode.Foot);
            JourneyRoute returnRoute = hub.Journeys.FindRoute(storePort.LocationId, returnLocation, TravelMode.Foot);
            if (!outwardRoute.Found || !returnRoute.Found)
            {
                Debug.LogWarning($"[BusinessFeed] Feed need retained for '{freight.InstanceId}': custom navigation cannot route Ethan's " +
                    $"round trip (outbound='{outwardRoute.Diagnostic}', return='{returnRoute.Diagnostic}').", this);
                return;
            }

            var trip = new BusinessFeedTripTaskData
            {
                Stage = "outbound",
                BusinessId = freight.InstanceId,
                StoreId = store.InstanceId,
                PersonId = ethan.id,
                OriginLocationId = startLocation,
                StoreLocationId = storePort.LocationId,
                StoreBuildingId = store.AssignedBuildingId,
                HomeBuildingId = ethan.homeBuildingId,
                Sacks = sacksNeeded,
                DayIndex = absoluteDayIndex,
                TransactionId = $"business-feed:{freight.InstanceId}:{absoluteDayIndex}",
            };
            WorkTask outbound = hub.Tasks.CreateTaskWithPlannedMinutes(JourneyTravel.TravelTaskId,
                EntityId.For(EntityKind.Business, StableBusinessRuntimeId(freight.InstanceId)), absoluteDayIndex,
                Math.Max(1, outwardRoute.TotalMinutes),
                customerRef: BusinessFeedTaskPrefix + "outbound:" + trip.TransactionId,
                termsRef: JsonUtility.ToJson(trip));
            if (!TryStartBusinessPersonTask(outbound, ethan, absoluteDayIndex,
                    PersonActivityKind.Shopping, store.AssignedBuildingId, out string taskRefusal))
            {
                Debug.LogWarning($"[BusinessFeed] Feed need retained for '{freight.InstanceId}': Ethan's outbound task could not start: {taskRefusal}", this);
                return;
            }

            Debug.Log($"[BusinessFeed] Procurement task assigned for B3 '{freight.InstanceId}': Ethan P{ethan.id} " +
                $"is assigned to travel to B0 for {sacksNeeded} sacks; task='{outbound.TaskId}' remains open until confirmed arrival, B3={buyerCash.BalanceCents}c, " +
                $"B0 stock={storePort.ItemStockUnits(HorseFeedSku)} sack(s).", this);
        }

        private bool HasOpenBusinessFeedTask(string businessId)
        {
            if (hub?.Tasks == null) return false;
            foreach (WorkTask task in hub.Tasks.GetAllOpenTasks())
            {
                if (task != null && task.IsOpen
                    && task.CustomerRef.StartsWith(BusinessFeedTaskPrefix, StringComparison.Ordinal)
                    && task.TermsRef.Contains("\"BusinessId\":\"" + businessId + "\"")) return true;
            }
            return false;
        }

        private bool TryStartBusinessPersonTask(WorkTask task, PersonState ethan, int dayIndex,
            PersonActivityKind activity, int destinationBuildingId, out string refusal)
        {
            refusal = null;
            if (task == null || ethan == null || hub?.Tasks == null || hub.PersonSchedules == null)
            {
                refusal = "task, person, TaskAuthority, or PersonScheduleTracker is unavailable.";
                return false;
            }

            bool alreadyScheduled = false;
            foreach (PersonActivityReservation reservation in hub.PersonSchedules.ActiveFor(ethan.id, dayIndex))
            {
                if (reservation != null && string.Equals(reservation.Label, TaskReservationLabel(task), StringComparison.Ordinal))
                {
                    alreadyScheduled = true;
                    break;
                }
            }
            if (!alreadyScheduled)
            {
                int nowMinute = timeManager != null ? Mathf.FloorToInt(timeManager.TimeOfDay01 * 1440f) : 480;
                int startMinute = FindAvailablePersonStartMinute(ethan.id, dayIndex, nowMinute,
                    IsArrivalDrivenTravelTask(task) ? 1 : Math.Max(1, task.MinutesRemaining));
                if (startMinute < 0)
                {
                    refusal = $"P{ethan.id} has no available time window today for the task.";
                    return false;
                }

                // Do not assign and then interrupt a queued activity when today's
                // calendar is full. The task remains queued for the next legitimate
                // opening instead of becoming permanently unusable.
                if (task.Status == TaskStatus.Queued
                    && !hub.Tasks.AssignTask(task.TaskId, EntityId.For(EntityKind.Person, ethan.id),
                        hub.WorkTimeBudgets, dayIndex, out refusal)) return false;
                if (task.Status != TaskStatus.Assigned)
                {
                    refusal = $"task '{task.TaskId}' is {task.Status}, expected Queued or Assigned.";
                    return false;
                }

                PersonActivityKind reservedActivity = task.DefinitionId == JourneyTravel.TravelTaskId
                    ? PersonActivityKind.Travel : activity;
                int reservationMinutes = IsArrivalDrivenTravelTask(task)
                    ? FindTravelReservationWindowMinutes(ethan.id, dayIndex, startMinute)
                    : Math.Max(1, task.MinutesRemaining);
                string scheduleRefusal = hub.PersonSchedules.TryReserve(ethan.id, reservedActivity, dayIndex,
                    startMinute, reservationMinutes, TaskReservationLabel(task));
                if (scheduleRefusal != null)
                {
                    if (task.Status == TaskStatus.Assigned)
                        hub.Tasks.InterruptTask(task.TaskId, hub.WorkTimeBudgets, out _);
                    refusal = scheduleRefusal;
                    return false;
                }
            }
            else if (task.Status == TaskStatus.Queued)
            {
                refusal = $"task '{task.TaskId}' cannot already have a schedule reservation while still queued.";
                return false;
            }

            if (task.Status != TaskStatus.Assigned)
            {
                refusal = $"task '{task.TaskId}' is {task.Status}, expected Queued or Assigned.";
                return false;
            }

            // Assignment and the exclusive schedule window are now established.
            // StartTask is called by the ShortTick path only when simulated time
            // actually enters that window.
            return true;
        }

        private int FindTravelReservationWindowMinutes(int personId, int dayIndex, int startMinute)
        {
            int endMinute = 1440;
            foreach (PersonActivityReservation reservation in hub.PersonSchedules.ActiveFor(personId, dayIndex))
            {
                if (reservation == null || reservation.Released) continue;
                if (reservation.StartMinuteOfDay > startMinute && reservation.StartMinuteOfDay < endMinute)
                    endMinute = reservation.StartMinuteOfDay;
            }

            // A route remains active until actual arrival. This schedule lease
            // only occupies the free portion of today's calendar; extending it
            // blindly to midnight would collide with authored sleep/rest.
            return Math.Max(1, endMinute - startMinute);
        }

        private bool BeginScheduledBusinessPersonTask(WorkTask task, PersonState ethan, int dayIndex,
            int tickStartMinute, int tickEndMinute, int destinationBuildingId, out string refusal)
        {
            refusal = null;
            if (task == null || ethan == null || task.Status != TaskStatus.Assigned) return true;
            PersonActivityReservation active = null;
            foreach (PersonActivityReservation reservation in hub.PersonSchedules.ActiveFor(ethan.id, dayIndex))
            {
                if (reservation != null && string.Equals(reservation.Label, TaskReservationLabel(task), StringComparison.Ordinal))
                {
                    active = reservation;
                    break;
                }
            }
            if (active == null || tickEndMinute <= active.StartMinuteOfDay || tickStartMinute >= active.EndMinuteOfDay)
                return true;

            if (!hub.Tasks.StartTask(task.TaskId, dayIndex, out refusal)) return false;
            if (task.DefinitionId == JourneyTravel.TravelTaskId && destinationBuildingId >= 0)
            {
                LandLedgers.FirstLedger.PopulationPathingDirector presentation =
                    FindAnyObjectByType<LandLedgers.FirstLedger.PopulationPathingDirector>();
                PopulationScheduleState destinationState = PopulationScheduleState.AtWork;
                if (task.CustomerRef.StartsWith(BusinessFeedTaskPrefix, StringComparison.Ordinal))
                {
                    BusinessFeedTripTaskData trip = JsonUtility.FromJson<BusinessFeedTripTaskData>(task.TermsRef);
                    destinationState = trip?.Stage == "return"
                        ? PopulationScheduleState.ReturningHome : PopulationScheduleState.GoingShopping;
                }
                else if (task.CustomerRef.StartsWith(FreightMeetingTaskPrefix + "travel:", StringComparison.Ordinal))
                    destinationState = PopulationScheduleState.AtWork;
                if (presentation == null || !presentation.TryStartExplicitRoute(ethan.id, destinationBuildingId, destinationState))
                {
                    ReleaseTaskPersonSchedule(task, dayIndex);
                    hub.Tasks.InterruptTask(task.TaskId, hub.WorkTimeBudgets, out _);
                    refusal = $"custom world pathing could not start P{ethan.id}'s route to building {destinationBuildingId}.";
                    return false;
                }
                if (presentation.TryGetExplicitArrival(ethan.id, destinationBuildingId,
                        out Vector3 originPosition, out float initialDistance, out _))
                {
                    Debug.Log($"[BusinessTravel] Departure: person=P{ethan.id}, task='{task.TaskId}', " +
                        $"origin position={originPosition}, destination building={destinationBuildingId}, " +
                        $"initial route distance={initialDistance:0.00}m; travelling by custom navigation.", this);
                }
            }
            if (task.DefinitionId == BusinessFeedShoppingTaskId)
                ethan.scheduleState = PopulationScheduleState.AtStore;
            else if (task.CustomerRef.StartsWith(BusinessFeedTaskPrefix, StringComparison.Ordinal))
            {
                BusinessFeedTripTaskData trip = JsonUtility.FromJson<BusinessFeedTripTaskData>(task.TermsRef);
                ethan.scheduleState = trip?.Stage == "return"
                    ? PopulationScheduleState.ReturningHome : PopulationScheduleState.GoingShopping;
            }
            int actualStartMinute = Math.Max(active.StartMinuteOfDay, tickStartMinute);
            bool arrivalDrivenTravel = IsArrivalDrivenTravelTask(task);
            Debug.Log($"[BusinessTask] P{ethan.id} started task '{task.TaskId}' ({task.DefinitionId}) " +
                $"at game minute {actualStartMinute}; " + (arrivalDrivenTravel
                    ? "occupancy lasts until confirmed navigation arrival."
                    : $"planned={task.MinutesRemaining} minute(s)."), this);
            if (task.CustomerRef.StartsWith(DevelopmentHitchTaskPrefix + "travel:", StringComparison.Ordinal))
                Debug.Log($"[Hitching] Ethan P{ethan.id} began custom-navigation travel to the staged A0 and wagon; " +
                    $"task='{task.TaskId}', duration={task.MinutesRemaining} in-game minutes.", this);
            else if (!string.IsNullOrWhiteSpace(task.CustomerRef)
                && (task.CustomerRef.StartsWith("development-preparation:", StringComparison.Ordinal)
                    || task.CustomerRef.StartsWith("freight-preparation:", StringComparison.Ordinal)))
            {
                EquipmentAsset wagon = hub.TransportAssets.Get("opening-freight-wagon");
                AnimalState horse = FindOpeningFreightHorse(wagon?.OwnerId);
                if (horse != null)
                {
                    horse.TransportState = HorseTransportState.AvailableUnsecured;
                    horse.SecuredFixtureId = string.Empty;
                }
                Debug.Log($"[Hitching] Timed task '{task.TaskId}' started at the staging area; " +
                    $"A0 released from the post, wagon remains separate for {task.MinutesRemaining} in-game minutes.", this);
            }
            return true;
        }

        private int FindAvailablePersonStartMinute(int personId, int dayIndex, int nowMinute, int durationMinutes)
        {
            int candidate = Math.Max(480, nowMinute);
            List<PersonActivityReservation> existing = hub.PersonSchedules.ActiveFor(personId, dayIndex);
            while (candidate + durationMinutes <= 1440)
            {
                PersonActivityReservation overlap = null;
                foreach (PersonActivityReservation reservation in existing)
                {
                    if (reservation == null || reservation.Released) continue;
                    if (candidate < reservation.EndMinuteOfDay && reservation.StartMinuteOfDay < candidate + durationMinutes)
                    {
                        overlap = reservation;
                        break;
                    }
                }
                if (overlap == null) return candidate;
                candidate = Math.Max(candidate + 1, overlap.EndMinuteOfDay);
            }
            return -1;
        }

        private void ReleaseTaskPersonSchedule(WorkTask task, int dayIndex)
        {
            if (task == null || hub?.PersonSchedules == null || !task.HasAssignee) return;
            foreach (PersonActivityReservation reservation in hub.PersonSchedules.ActiveFor(task.AssigneeId.Id, dayIndex))
            {
                if (reservation != null
                    && string.Equals(reservation.Label, TaskReservationLabel(task), StringComparison.Ordinal))
                {
                    hub.PersonSchedules.Release(reservation.ReservationId);
                    return;
                }
            }
        }

        private void StartQueuedBusinessFeedTasks(int dayIndex, int tickStartMinute, int tickEndMinute)
        {
            if (hub?.Tasks == null || sharedBusinessRuntime == null || populationManager?.State == null) return;
            foreach (WorkTask task in hub.Tasks.GetAllOpenTasks())
            {
                if (task == null || (task.Status != TaskStatus.Queued && task.Status != TaskStatus.Assigned)
                    || !task.CustomerRef.StartsWith(BusinessFeedTaskPrefix, StringComparison.Ordinal)) continue;
                BusinessFeedTripTaskData trip = JsonUtility.FromJson<BusinessFeedTripTaskData>(task.TermsRef);
                PersonState person = trip != null ? populationManager.State.GetPerson(trip.PersonId) : null;
                if (trip == null || person == null) continue;
                int targetBuildingId = trip.Stage == "outbound" ? trip.StoreBuildingId
                    : trip.Stage == "return" ? trip.HomeBuildingId : -1;
                if (!TryStartBusinessPersonTask(task, person, dayIndex, PersonActivityKind.Shopping,
                        targetBuildingId, out string prepareRefusal))
                {
                    if (taskStartRefusalAlreadyLogged.Add(task.TaskId.ToString()))
                        Debug.LogWarning($"[BusinessFeed] Task '{task.TaskId}' could not reserve Ethan's time: {prepareRefusal}", this);
                    continue;
                }
                if (!BeginScheduledBusinessPersonTask(task, person, dayIndex, tickStartMinute,
                        tickEndMinute, targetBuildingId, out string startRefusal)
                    && taskStartRefusalAlreadyLogged.Add(task.TaskId.ToString()))
                    Debug.LogWarning($"[BusinessFeed] Task '{task.TaskId}' could not start at its scheduled time: {startRefusal}", this);
            }
        }

        private void StartQueuedFreightMeetingTasks(int dayIndex, int tickStartMinute, int tickEndMinute)
        {
            FreightServiceAgreementBook book = FreightProduction.ServiceAgreements;
            foreach (FreightServiceRequest request in book.Requests)
                if (request != null && request.Status == FreightServiceRequestStatus.AtMeeting)
                    TryScheduleFreightMeeting(request, dayIndex);

            if (hub?.Tasks == null || populationManager?.State == null) return;
            foreach (WorkTask task in hub.Tasks.GetAllOpenTasks())
            {
                if (task == null || (task.Status != TaskStatus.Queued && task.Status != TaskStatus.Assigned)
                    || !task.CustomerRef.StartsWith(FreightMeetingTaskPrefix, StringComparison.Ordinal)) continue;
                FreightMeetingTripTaskData trip = JsonUtility.FromJson<FreightMeetingTripTaskData>(task.TermsRef);
                PersonState requester = trip != null ? populationManager.State.GetPerson(trip.RequestingPersonId) : null;
                if (trip == null || requester == null) continue;
                int target = task.CustomerRef.StartsWith(FreightMeetingTaskPrefix + "travel:", StringComparison.Ordinal)
                    ? trip.MeetingBuildingId : -1;
                PersonActivityKind activity = target >= 0 ? PersonActivityKind.Travel : PersonActivityKind.Work;
                if (!TryStartBusinessPersonTask(task, requester, dayIndex, activity, target, out string prepareRefusal))
                {
                    if (taskStartRefusalAlreadyLogged.Add(task.TaskId.ToString()))
                        Debug.LogWarning($"[FreightAgreement] Glen's task '{task.TaskId}' could not reserve time: {prepareRefusal}", this);
                    continue;
                }
                TaskStatus before = task.Status;
                if (!BeginScheduledBusinessPersonTask(task, requester, dayIndex, tickStartMinute,
                        tickEndMinute, target, out string startRefusal))
                {
                    if (taskStartRefusalAlreadyLogged.Add(task.TaskId.ToString()))
                        Debug.LogWarning($"[FreightAgreement] Task '{task.TaskId}' could not start: {startRefusal}", this);
                    continue;
                }
                if (target < 0 && before == TaskStatus.Assigned && task.Status == TaskStatus.InProgress)
                {
                    FreightServiceRequest live = book.FindRequest(trip.RequestId);
                    FreightServiceQuote quote = live?.Quote;
                    if (quote != null)
                        Debug.Log($"[FreightAgreement] In-person quote presented to Glen P{trip.RequestingPersonId} at B3: " +
                            $"Millbrook Grain Co. → Bakery B1, one-way={quote.OneWayDistanceMiles:0.0} mi, " +
                            $"chargeable={quote.ChargeableMiles} mi ({quote.DistanceBasis}), base={quote.BaseServiceFeeCents}c, " +
                            $"rate={quote.RatePerMileCents}c/mi, mileage={quote.MileageChargeCents}c, total={quote.TotalPriceCents}c, " +
                            $"payer={quote.PayerBusinessId}, payment={quote.PaymentTiming}; meeting task='{task.TaskId}'.", this);
                }
            }
        }

        private void HandleCompletedFreightMeetingTravelTask(WorkTask task, int dayIndex)
        {
            if (task == null || !task.CustomerRef.StartsWith(FreightMeetingTaskPrefix + "travel:", StringComparison.Ordinal)) return;
            FreightMeetingTripTaskData trip = JsonUtility.FromJson<FreightMeetingTripTaskData>(task.TermsRef);
            FreightServiceRequest request = trip != null ? FreightProduction.ServiceAgreements.FindRequest(trip.RequestId) : null;
            PersonState glen = trip != null ? populationManager?.State?.GetPerson(trip.RequestingPersonId) : null;
            if (trip == null || request == null || glen == null || request.Status != FreightServiceRequestStatus.Open) return;
            SetPersonJourneyLocation(glen.id, trip.MeetingLocationId);
            glen.scheduleState = PopulationScheduleState.AtWork;
            request.ContactTravelTaskId = string.Empty;
            string refusal = FreightProduction.ServiceAgreements.SetStatus(request.RequestId, FreightServiceRequestStatus.AtMeeting);
            if (refusal != null)
            {
                Debug.LogWarning($"[FreightAgreement] Glen arrived for '{request.RequestId}' but the request could not enter its meeting state: {refusal}", this);
                return;
            }
            Debug.Log($"[FreightAgreement] Glen P{glen.id} physically arrived at Ethan's B3 meeting location: building {trip.MeetingBuildingId}, " +
                $"location='{trip.MeetingLocationId}', task='{task.TaskId}'. No quote has been accepted yet.", this);
            TryScheduleFreightMeeting(request, dayIndex);
        }

        private void TryScheduleFreightMeeting(FreightServiceRequest request, int dayIndex)
        {
            if (request == null || request.Status != FreightServiceRequestStatus.AtMeeting
                || hub?.Tasks == null || hub.PersonSchedules == null || populationManager?.State == null) return;
            PersonState glen = populationManager.State.GetPerson(request.RequestingPersonId);
            PersonState ethan = populationManager.State.GetPerson(request.CarrierOperatorPersonId);
            BusinessInstanceState bakery = FindBusiness(request.RequestingBusinessId);
            BusinessInstanceState carrier = FindBusiness(request.CarrierBusinessId);
            if (glen == null || ethan == null || bakery?.Owner == null || carrier?.Owner == null
                || ethan.homeBuildingId < 0 || glen.homeBuildingId < 0)
            {
                Debug.LogWarning($"[FreightAgreement] Meeting '{request.RequestId}' deferred: requester, operator, or staging building is unresolved.", this);
                return;
            }
            if (!IsPersonVisuallyAtBuilding(glen.id, ethan.homeBuildingId)
                || !IsPersonVisuallyAtBuilding(ethan.id, ethan.homeBuildingId)) return;
            if (HasOpenAssignedTask(glen.id) || HasOpenAssignedTask(ethan.id)) return;

            if (request.Quote == null)
            {
                DevelopmentTownConfiguration.BusinessDefinition carrierDefinition = null;
                if (!developmentTownConfiguration.TryGetBusiness(developmentTownConfiguration.FreightBusinessKey, out carrierDefinition)
                    || carrierDefinition?.freightRateCard == null)
                {
                    Debug.LogWarning($"[FreightAgreement] Request '{request.RequestId}' is waiting: B3 has no configured freight rate card.", this);
                    return;
                }
                float oneWayMiles = millbrookAidRuntime != null ? millbrookAidRuntime.PickupRouteMilesFromTown : 0f;
                if (!FreightServiceQuoteCalculator.TryCalculate(oneWayMiles,
                        carrierDefinition.freightRateCard.baseServiceFeeCents,
                        carrierDefinition.freightRateCard.ratePerMileCents,
                        carrierDefinition.freightRateCard.chargeRoundTrip,
                        out FreightServiceQuote quote, out string quoteRefusal))
                {
                    Debug.LogWarning($"[FreightAgreement] Request '{request.RequestId}' has no valid Millbrook quote: {quoteRefusal}", this);
                    return;
                }
                quote.QuoteId = "freight-quote:" + request.RequestId;
                quote.RateCardBusinessId = carrier.InstanceId;
                quote.PayerBusinessId = bakery.InstanceId;
                quote.DistanceBasis = carrierDefinition.freightRateCard.chargeRoundTrip
                    ? "Authored Millbrook one-way supplier route, charged as expected outward-loaded and empty-return mileage"
                    : "Authored one-way Millbrook supplier route";
                string quoteProblem = FreightProduction.ServiceAgreements.SetQuote(request.RequestId, quote);
                if (quoteProblem != null)
                {
                    Debug.LogWarning($"[FreightAgreement] Request '{request.RequestId}' quote refused: {quoteProblem}", this);
                    return;
                }
                FreightProduction.ServiceAgreements.SetStatus(request.RequestId, FreightServiceRequestStatus.AtMeeting);
            }

            int minutes = Math.Max(1, freightMeetingMinutes);
            int startMinute = FindCommonPersonStartMinute(glen.id, ethan.id, dayIndex,
                timeManager != null ? Mathf.FloorToInt(timeManager.TimeOfDay01 * 1440f) : 480, minutes);
            if (startMinute < 0) return;
            var trip = new FreightMeetingTripTaskData
            {
                RequestId = request.RequestId,
                RequestingPersonId = glen.id,
                CarrierOperatorPersonId = ethan.id,
                MeetingBuildingId = ethan.homeBuildingId,
                MeetingLocationId = TownJourneyNetworkBuilder.LocationIdForBuilding(ethan.homeBuildingId)
            };
            WorkTask meeting = hub.Tasks.CreateTaskWithPlannedMinutes(FreightMeetingTaskId,
                EntityId.For(EntityKind.Business, StableBusinessRuntimeId(bakery.InstanceId)), dayIndex,
                minutes, customerRef: FreightMeetingTaskPrefix + "session:" + request.RequestId,
                termsRef: JsonUtility.ToJson(trip));
            if (!hub.Tasks.AssignTask(meeting.TaskId, EntityId.For(EntityKind.Person, glen.id),
                    hub.WorkTimeBudgets, dayIndex, out string assignmentRefusal))
            {
                hub.Tasks.CancelTask(meeting.TaskId, hub.WorkTimeBudgets);
                Debug.LogWarning($"[FreightAgreement] Meeting task for '{request.RequestId}' could not assign Glen's time: {assignmentRefusal}", this);
                return;
            }
            string operatorLabel = "freight-meeting-operator:" + request.RequestId;
            string operatorRefusal = hub.PersonSchedules.TryReserve(ethan.id, PersonActivityKind.Work,
                dayIndex, startMinute, minutes, operatorLabel);
            if (operatorRefusal != null)
            {
                hub.Tasks.CancelTask(meeting.TaskId, hub.WorkTimeBudgets);
                Debug.LogWarning($"[FreightAgreement] Meeting task for '{request.RequestId}' could not reserve Ethan's time: {operatorRefusal}", this);
                return;
            }
            string requesterRefusal = hub.PersonSchedules.TryReserve(glen.id, PersonActivityKind.Work,
                dayIndex, startMinute, minutes, TaskReservationLabel(meeting));
            if (requesterRefusal != null)
            {
                ReleasePersonScheduleByLabel(ethan.id, dayIndex, operatorLabel);
                hub.Tasks.CancelTask(meeting.TaskId, hub.WorkTimeBudgets);
                Debug.LogWarning($"[FreightAgreement] Meeting task for '{request.RequestId}' could not reserve Glen's time: {requesterRefusal}", this);
                return;
            }
            request.MeetingTaskId = meeting.TaskId.ToString();
            request.MeetingOperatorReservationId = operatorLabel;
            FreightProduction.ServiceAgreements.SetStatus(request.RequestId, FreightServiceRequestStatus.MeetingScheduled);
            Debug.Log($"[FreightAgreement] Direct meeting reserved for Glen P{glen.id} and Ethan P{ethan.id}: " +
                $"task='{meeting.TaskId}', starts={startMinute} min, duration={minutes} in-game minutes; " +
                $"both persons reserved, quote total={request.Quote.TotalPriceCents}c. No contract or shipment yet.", this);
        }

        private int FindCommonPersonStartMinute(int firstPersonId, int secondPersonId, int dayIndex, int nowMinute, int duration)
        {
            int candidate = Math.Max(480, nowMinute);
            for (int i = 0; i < 64; i++)
            {
                int first = FindAvailablePersonStartMinute(firstPersonId, dayIndex, candidate, duration);
                int second = FindAvailablePersonStartMinute(secondPersonId, dayIndex, candidate, duration);
                if (first < 0 || second < 0) return -1;
                int next = Math.Max(first, second);
                if (next == candidate && candidate + duration <= 1440) return candidate;
                candidate = next;
            }
            return -1;
        }

        private bool HasOpenAssignedTask(int personId)
        {
            if (hub?.Tasks == null) return true;
            foreach (WorkTask task in hub.Tasks.GetAllOpenTasks())
                if (task != null && task.IsOpen && task.HasAssignee && task.AssigneeId.Id == personId) return true;
            return false;
        }

        private void ReleasePersonScheduleByLabel(int personId, int dayIndex, string label)
        {
            if (hub?.PersonSchedules == null || string.IsNullOrWhiteSpace(label)) return;
            foreach (PersonActivityReservation reservation in hub.PersonSchedules.ActiveFor(personId, dayIndex))
                if (reservation != null && string.Equals(reservation.Label, label, StringComparison.Ordinal))
                {
                    hub.PersonSchedules.Release(reservation.ReservationId);
                    return;
                }
        }

        private BusinessInstanceState FindBusiness(string businessId)
        {
            if (sharedBusinessRuntime == null || string.IsNullOrWhiteSpace(businessId)) return null;
            return sharedBusinessRuntime.Businesses.FirstOrDefault(b => b != null
                && string.Equals(b.InstanceId, businessId, StringComparison.Ordinal));
        }

        private void HandleCompletedFreightMeetingTask(WorkTask task, int dayIndex)
        {
            if (task == null || !task.CustomerRef.StartsWith(FreightMeetingTaskPrefix + "session:", StringComparison.Ordinal)) return;
            FreightMeetingTripTaskData trip = JsonUtility.FromJson<FreightMeetingTripTaskData>(task.TermsRef);
            FreightServiceAgreementBook book = FreightProduction.ServiceAgreements;
            FreightServiceRequest request = trip != null ? book.FindRequest(trip.RequestId) : null;
            if (trip == null || request == null || request.Status != FreightServiceRequestStatus.MeetingScheduled
                || !string.Equals(request.MeetingTaskId, task.TaskId.ToString(), StringComparison.Ordinal)) return;

            ReleasePersonScheduleByLabel(trip.CarrierOperatorPersonId, dayIndex, request.MeetingOperatorReservationId);
            PersonState glen = populationManager?.State?.GetPerson(trip.RequestingPersonId);
            PersonState ethan = populationManager?.State?.GetPerson(trip.CarrierOperatorPersonId);
            BusinessInstanceState bakery = FindBusiness(request.RequestingBusinessId);
            BusinessInstanceState carrier = FindBusiness(request.CarrierBusinessId);
            OffMapAidCargoLot lot = millbrookAidRuntime?.Lots?.FirstOrDefault(candidate => candidate != null
                && string.Equals(candidate.LotId, request.CargoLotId, StringComparison.Ordinal));
            EquipmentAsset wagon = hub?.TransportAssets?.Get("opening-freight-wagon");
            AnimalState horse = FindOpeningFreightHorse(request.CarrierBusinessId);

            string refusal = null;
            if (glen == null || ethan == null || bakery?.RuntimeState == null || carrier?.RuntimeState == null)
                refusal = "Glen, Ethan, or a participant's business cash authority is unavailable at meeting completion.";
            else if (!IsPersonVisuallyAtBuilding(glen.id, ethan.homeBuildingId)
                || !IsPersonVisuallyAtBuilding(ethan.id, ethan.homeBuildingId))
                refusal = "Both participants are not physically present at the Freight Company meeting location.";
            else if (HasOpenAssignedTask(ethan.id))
                refusal = "Ethan has an incompatible assigned or in-progress task at acceptance time.";
            else if (request.Quote == null || bakery.RuntimeState.CurrentCashCents < request.Quote.TotalPriceCents)
                refusal = $"Bakery B1 has {bakery?.RuntimeState?.CurrentCashCents ?? 0}c available for a {request.Quote?.TotalPriceCents ?? 0}c agreed charge.";
            else if (lot == null || lot.SourceCommitted || lot.Received || lot.PackageUnits != request.Quantity
                || !string.Equals(lot.SkuId, request.CargoSkuId, StringComparison.Ordinal)
                || !string.Equals(lot.BuyerBusinessId, bakery.InstanceId, StringComparison.Ordinal))
                refusal = "The exact paid Millbrook lot is absent, committed, received, or inconsistent with the request.";
            else if (horse == null || horse.OwnerKind != AnimalOwnerKind.Business
                || !string.Equals(horse.OwnerId, carrier.InstanceId, StringComparison.Ordinal)
                || !HorseAuthority.IsWorkReady(horse))
                refusal = "A0 is unavailable or not work-ready for Freight Company B3.";
            else if (wagon == null || !wagon.IsWagon || wagon.OwnerKind != "business"
                || !string.Equals(wagon.OwnerId, carrier.InstanceId, StringComparison.Ordinal))
                refusal = "The existing opening wagon is unavailable or is not owned by Freight Company B3.";

            if (refusal == null)
            {
                EnsureFreightCompany(carrier);
                hub.TransportAssets.Register(wagon);
                string capacityConflict = FreightProduction.Service.FindExclusivityConflict(null, wagon.AssetId,
                    EntityId.Invalid, null);
                if (!string.IsNullOrWhiteSpace(capacityConflict)) refusal = capacityConflict;
                else if (hub.Reservations != null && hub.Reservations.IsReserved(
                    ReservationKeyConventions.Asset(wagon.AssetId), AbsoluteNowGameSeconds()))
                    refusal = $"Wagon '{wagon.AssetId}' is held by another equipment reservation.";
            }

            if (refusal != null)
            {
                book.SetStatus(request.RequestId, FreightServiceRequestStatus.Refused, refusal);
                Debug.LogWarning($"[FreightAgreement] Ethan P{trip.CarrierOperatorPersonId} refused request '{request.RequestId}' after the timed meeting: {refusal}", this);
                return;
            }

            if (book.Accept(request.RequestId, trip.CarrierOperatorPersonId, dayIndex,
                    AbsoluteNowGameSeconds(), out FreightServiceAgreement agreement) != null)
            {
                refusal = "The request could not be accepted by the service-agreement authority.";
                book.SetStatus(request.RequestId, FreightServiceRequestStatus.Refused, refusal);
                Debug.LogWarning($"[FreightAgreement] Ethan's acceptance for '{request.RequestId}' was refused: {refusal}", this);
                return;
            }

            TryRegisterAcceptedFreightShipment(agreement, dayIndex);
        }

        private void RetryPendingFreightShipmentRegistrations(int dayIndex)
        {
            FreightServiceAgreementBook book = FreightProduction?.ServiceAgreements;
            if (book == null) return;
            foreach (FreightServiceAgreement agreement in book.Agreements.ToList())
                if (agreement != null && agreement.Status == FreightServiceAgreementStatus.Accepted
                    && string.IsNullOrWhiteSpace(agreement.ShipmentId)
                    && agreement.ShipmentRegistrationPending)
                    TryRegisterAcceptedFreightShipment(agreement, dayIndex);
        }

        private void TryRegisterAcceptedFreightShipment(FreightServiceAgreement agreement, int dayIndex)
        {
            if (agreement == null || string.IsNullOrWhiteSpace(agreement.AgreementId)) return;
            FreightServiceAgreementBook book = FreightProduction?.ServiceAgreements;
            FreightServiceRequest request = book?.FindRequest(agreement.RequestId);
            BusinessInstanceState bakery = FindBusiness(agreement.CustomerBusinessId);
            BusinessInstanceState carrier = FindBusiness(agreement.CarrierBusinessId);
            OffMapAidCargoLot lot = millbrookAidRuntime?.Lots?.FirstOrDefault(candidate => candidate != null
                && string.Equals(candidate.LotId, agreement.CargoLotId, StringComparison.Ordinal));
            EquipmentAsset wagon = hub?.TransportAssets?.Get("opening-freight-wagon");
            AnimalState horse = FindOpeningFreightHorse(agreement.CarrierBusinessId);
            string failure = null;
            if (book == null || request == null || bakery == null || carrier == null || lot == null || wagon == null || horse == null)
                failure = "Shipment retry prerequisites unresolved: " +
                    string.Join(", ", new[] { request == null ? "freight request" : null,
                        bakery == null ? "customer business" : null, carrier == null ? "carrier business" : null,
                        lot == null ? "paid source lot" : null, wagon == null ? "registered wagon" : null,
                        horse == null ? "assigned horse" : null }.Where(value => value != null));
            else if (lot.SourceCommitted || lot.Received || lot.PackageUnits != request.Quantity
                || !string.Equals(lot.SkuId, request.CargoSkuId, StringComparison.Ordinal))
                failure = $"Paid lot '{lot.LotId}' is no longer eligible for this pending shipment registration.";

            if (failure != null)
            {
                string prior = agreement.ShipmentRegistrationFailureReason;
                book.RecordShipmentRegistrationFailure(agreement.AgreementId, failure);
                if (!string.Equals(prior, failure, StringComparison.Ordinal))
                    Debug.LogWarning($"[FreightAgreement] Accepted agreement '{agreement.AgreementId}' awaits shipment registration: {failure}", this);
                return;
            }

            EnsureFreightCompany(carrier);
            hub.TransportAssets.Register(wagon);
            string stableShipmentId = "freight-shipment:" + agreement.RequestId;
            string priorFailure = agreement.ShipmentRegistrationFailureReason;
            string registrationRefusal = book.TryRegisterShipment(agreement.AgreementId,
                (FreightServiceAgreement accepted, out string shipmentId, out string reason) =>
                {
                    shipmentId = string.Empty;
                    reason = string.Empty;
                    LogisticsShipmentState priorShipment = FreightProduction.AllShipments().FirstOrDefault(item => item != null
                        && string.Equals(item.ShipmentId, stableShipmentId, StringComparison.Ordinal));
                    if (priorShipment != null)
                    {
                        if (priorShipment.Status != LogisticsShipmentStatus.Planned || !priorShipment.requiresDispatchAuthorization
                            || !string.Equals(priorShipment.freightServiceAgreementId, accepted.AgreementId, StringComparison.Ordinal)
                            || !string.Equals(priorShipment.CargoLotId, accepted.CargoLotId, StringComparison.Ordinal))
                        {
                            reason = $"Stable shipment id '{stableShipmentId}' already exists but does not match the accepted pending agreement/lot.";
                            return false;
                        }
                        shipmentId = priorShipment.ShipmentId;
                        return true;
                    }

                    const float poundsToKg = 0.45359237f;
                    var diagnostics = new List<string>();
                    var hooks = new LandLedgers.Economy.Logistics.FreightShipmentHooks
                    {
                        Tasks = hub.Tasks, Budgets = hub.WorkTimeBudgets, DayIndex = dayIndex,
                        Diagnostics = diagnostics, Reservations = hub.Reservations,
                        NowGameSeconds = AbsoluteNowGameSeconds()
                    };
                    LogisticsShipmentState created = FreightProduction.CreateShipment(
                        carrier.InstanceId, stableShipmentId, millbrookAidRuntime.BusinessId, bakery.InstanceId,
                        lot.PackageUnits,
                        new LandLedgers.Economy.Logistics.FreightCargoSpec
                        {
                            MassKgPerUnit = lot.InventoryUnits * poundsToKg,
                            VolumeM3PerUnit = 0.01f * Math.Max(1, lot.PackageUnits)
                        }, wagon.AssetId, hooks, EntityId.For(EntityKind.Person, accepted.AcceptingOperatorPersonId),
                        new List<EntityId> { horse.AnimalId }, accepted.AgreedPriceCents, accepted.PayerBusinessId,
                        diagnostics, out reason, accepted.AgreementId, lot.LotId, lot.SkuId,
                        holdForDispatchAuthorization: true);
                    if (created == null || created.IsPreLoadBlocked) return false;
                    shipmentId = created.ShipmentId;
                    if (diagnostics.Count > 0)
                        Debug.Log("[FreightAgreement] Shipment authority: " + string.Join(" | ", diagnostics), this);
                    return true;
                }, out bool alreadyRegistered);

            if (registrationRefusal != null)
            {
                if (!string.Equals(priorFailure, registrationRefusal, StringComparison.Ordinal))
                    Debug.LogWarning($"[FreightAgreement] Accepted agreement '{agreement.AgreementId}' retained, shipment registration pending: {registrationRefusal}", this);
                return;
            }
            FreightServiceAgreement linked = book.FindAgreement(agreement.AgreementId);
            LogisticsShipmentState shipment = FreightProduction.AllShipments().FirstOrDefault(item => item != null
                && string.Equals(item.ShipmentId, linked?.ShipmentId, StringComparison.Ordinal));
            if (shipment == null) return;
            string quoteSummary = $"chargeable={linked.ChargeableMiles} mi, base={linked.BaseServiceFeeCents}c + " +
                $"{linked.RatePerMileCents}c/mi = {linked.AgreedPriceCents}c";
            Debug.Log($"[FreightAgreement] {(alreadyRegistered ? "Recovered existing" : "Registered")} shipment '{shipment.ShipmentId}' " +
                $"for accepted agreement '{linked.AgreementId}' ({quoteSummary}); lot='{shipment.CargoLotId}', " +
                $"status={shipment.Status}, dispatch hold={shipment.requiresDispatchAuthorization}, " +
                $"no pickup, cargo transfer, or freight payment occurred.", this);
        }

        private void HandleCompletedBusinessFeedTask(WorkTask task, int dayIndex)
        {
            if (task == null || !task.CustomerRef.StartsWith(BusinessFeedTaskPrefix, StringComparison.Ordinal)) return;
            BusinessFeedTripTaskData trip = JsonUtility.FromJson<BusinessFeedTripTaskData>(task.TermsRef);
            if (trip == null || sharedBusinessRuntime == null) return;
            if (!sharedBusinessRuntime.TryFindByInstanceId(trip.BusinessId, out BusinessInstanceState buyer)
                || buyer?.RuntimeState == null) return;
            PersonState ethan = populationManager?.State?.GetPerson(trip.PersonId);
            if (ethan == null) return;

            int currentDay = timeManager != null ? timeManager.CurrentAbsoluteDayIndex : dayIndex;
            if (trip.Stage == "outbound")
            {
                SetPersonJourneyLocation(ethan.id, trip.StoreLocationId);
                ethan.scheduleState = PopulationScheduleState.GoingShopping;
                Debug.Log($"[BusinessFeed] Ethan P{ethan.id} completed outbound travel to B0 at game minute " +
                    $"{Mathf.FloorToInt(AbsoluteNowGameSeconds() / 60f)}; purchase may begin after the counter task.", this);
                WorkTask shop = hub.Tasks.CreateTaskWithPlannedMinutes(BusinessFeedShoppingTaskId,
                    EntityId.For(EntityKind.Business, StableBusinessRuntimeId(trip.BusinessId)), currentDay, 15,
                    customerRef: BusinessFeedTaskPrefix + "shop:" + trip.TransactionId,
                    termsRef: JsonUtility.ToJson(new BusinessFeedTripTaskData
                    {
                        Stage = "shop", BusinessId = trip.BusinessId, StoreId = trip.StoreId,
                        PersonId = trip.PersonId, OriginLocationId = trip.OriginLocationId,
                        StoreLocationId = trip.StoreLocationId, StoreBuildingId = trip.StoreBuildingId,
                        HomeBuildingId = trip.HomeBuildingId, Sacks = trip.Sacks,
                        DayIndex = trip.DayIndex, TransactionId = trip.TransactionId,
                    }));
                TryStartBusinessPersonTask(shop, ethan, currentDay, PersonActivityKind.Shopping, -1, out _);
                return;
            }

            if (trip.Stage == "shop")
            {
                if (!sharedBusinessRuntime.TryFindByInstanceId(trip.StoreId, out BusinessInstanceState store)
                    || store?.RuntimeState == null)
                {
                    Debug.LogWarning($"[BusinessFeed] B0 disappeared before checkout for trip '{trip.TransactionId}'; need remains open.", this);
                    return;
                }
                var manager = FindAnyObjectByType<LandLedgers.FirstLedger.GeneralStoreRuntimeManager>();
                GeneralStoreTradingPort port = manager?.BuildHouseholdTradingPort(store, storeFeedRegistry);
                trip.DayIndex = currentDay;
                if (port == null || !port.UsesItemLevelInventory(HorseFeedSku)
                    || port.ItemStockUnits(HorseFeedSku) < trip.Sacks)
                {
                    Debug.LogWarning($"[BusinessFeed] Purchase refused for B3 '{trip.BusinessId}' at B0: requested {trip.Sacks} " +
                        $"'{HorseFeedSku}' sacks, usable stock={port?.ItemStockUnits(HorseFeedSku) ?? 0}; need remains open.", this);
                    trip.Batch = null;
                }
                else
                {
                    string tradeRefusal = port.TradingCapability?.CheckTradable(currentDay, 600);
                    if (tradeRefusal != null)
                    {
                        Debug.LogWarning($"[BusinessFeed] Purchase refused at B0 for '{trip.TransactionId}': {tradeRefusal}; need remains open.", this);
                        trip.Batch = null;
                    }
                    else
                    {
                        int b3CashBefore = buyer.RuntimeState.CurrentCashCents;
                        int b0CashBefore = store.RuntimeState.CurrentCashCents;
                        int b0StockBefore = port.ItemStockUnits(HorseFeedSku);
                        var buyerCash = new Series2BusinessRuntimeCashPort(buyer.RuntimeState);
                        BusinessFeedPurchase.Result purchase = BusinessFeedPurchase.PurchaseAtStore(
                            trip.TransactionId, port, store.RuntimeState, buyer.RuntimeState, buyerCash,
                            HorseFeedSku, HorseFeedCategory, HorseFeedUnit, trip.Sacks, currentDay);
                        trip.Batch = purchase.Succeeded ? purchase.Batch : null;
                        if (!purchase.Succeeded)
                            Debug.LogWarning($"[BusinessFeed] Purchase refused for B3 '{trip.BusinessId}': {purchase.Refusal}; need remains open.", this);
                        else
                            Debug.Log($"[BusinessFeed] Ethan P{ethan.id} completed B3 purchase at B0: {trip.Sacks} sacks " +
                                $"at {purchase.Batch.UnitPriceCents}c each; B3 cash {b3CashBefore}->{buyer.RuntimeState.CurrentCashCents}c, " +
                                $"B0 cash {b0CashBefore}->{store.RuntimeState.CurrentCashCents}c, stock {b0StockBefore}->" +
                                $"{port.ItemStockUnits(HorseFeedSku)} sack(s), source lots=" +
                                string.Join(",", purchase.Batch.SourceLots.ConvertAll(part => $"{part.LotId}:{part.Units}")) +
                                $"; transaction='{trip.TransactionId}'.", this);
                    }
                }

                trip.Stage = "return";
                WorkTask returning = CreateBusinessFeedTravelTask(trip, currentDay, trip.StoreLocationId,
                    trip.OriginLocationId, trip.HomeBuildingId);
                // The purchased batch remains serialized on this queued task if Ethan
                // cannot start the return immediately; retries never debit B3 again.
                if (!TryStartBusinessPersonTask(returning, ethan, currentDay, PersonActivityKind.Shopping,
                        trip.HomeBuildingId, out string returnRefusal))
                {
                    Debug.LogWarning($"[BusinessFeed] Return task '{returning.TaskId}' retained with its exact custody batch: " +
                        returnRefusal, this);
                }
                else Debug.Log($"[BusinessFeed] Ethan P{ethan.id} return task '{returning.TaskId}' is assigned and reserved " +
                    $"for trip '{trip.TransactionId}'; custody={(trip.Batch != null ? trip.Batch.Units + " sack(s)" : "empty")}.", this);
                return;
            }

            if (trip.Stage == "return")
            {
                SetPersonJourneyLocation(ethan.id, trip.OriginLocationId);
                ethan.scheduleState = PopulationScheduleState.AtHome;
                if (trip.Batch == null)
                {
                    Debug.Log($"[BusinessFeed] Ethan P{ethan.id} returned from B0 without a purchase; B3 '{trip.BusinessId}' need remains open.", this);
                    return;
                }
                int stockBefore = buyer.RuntimeState.GetRetailItemStockUnits(HorseFeedSku, HorseFeedCategory);
                BusinessFeedPurchase.Result receipt = BusinessFeedPurchase.ReceiveAtBusiness(trip.Batch, buyer.RuntimeState);
                if (!receipt.Succeeded)
                {
                    Debug.LogWarning($"[BusinessFeed] B3 receipt refused after Ethan returned; custody batch '{trip.TransactionId}' retained: " +
                        receipt.Refusal, this);
                    return;
                }
                int stockAfter = buyer.RuntimeState.GetRetailItemStockUnits(HorseFeedSku, HorseFeedCategory);
                Debug.Log($"[BusinessFeed] Ethan P{ethan.id} returned and B3 received {trip.Batch.Units} real feed sacks; " +
                    $"stock {stockBefore}->{stockAfter}, receipt lots=" +
                    string.Join(",", trip.Batch.SourceLots.ConvertAll(part => "feed-purchase:" + trip.Batch.TransactionId + ":" + part.LotId)) +
                    $"; transaction='{trip.TransactionId}'.", this);
                businessFeedNeedsOpen.Remove(trip.BusinessId);
            }
        }

        private WorkTask CreateBusinessFeedTravelTask(BusinessFeedTripTaskData trip, int dayIndex,
            string origin, string destination, int destinationBuildingId)
        {
            JourneyRoute route = hub.Journeys.FindRoute(origin, destination, TravelMode.Foot);
            trip.Stage = "return";
            WorkTask task = hub.Tasks.CreateTaskWithPlannedMinutes(JourneyTravel.TravelTaskId,
                EntityId.For(EntityKind.Business, StableBusinessRuntimeId(trip.BusinessId)), dayIndex,
                Math.Max(1, route.Found ? route.TotalMinutes : 1),
                customerRef: BusinessFeedTaskPrefix + "return:" + trip.TransactionId,
                termsRef: JsonUtility.ToJson(trip));
            return task;
        }

        private void DriveOpeningHitchPreparation(int absoluteDayIndex)
        {
            if (hub == null || sharedBusinessRuntime == null || populationManager?.State == null
                || !sharedBusinessRuntime.TryFindByInstanceId("livery_freight_003", out BusinessInstanceState freight)
                || freight?.Owner == null || freight.Owner.PersonId < 0) return;
            AnimalState horse = null;
            foreach (AnimalState candidate in hub.Animals.ActiveAnimals)
                if (candidate != null && candidate.IsActive && candidate.Species == AnimalSpecies.Horse
                    && candidate.AnimalId.ToString() == "A0" && candidate.OwnerKind == AnimalOwnerKind.Business
                    && candidate.OwnerId == freight.InstanceId) { horse = candidate; break; }
            EquipmentAsset wagon = hub.TransportAssets.Get("opening-freight-wagon");
            if (horse == null || wagon == null || wagon.OwnerKind != "business" || wagon.OwnerId != freight.InstanceId
                || wagon.HitchingAnimalIds == null || wagon.HitchingAnimalIds.Contains(horse.AnimalId.ToString())) return;

            FreightServiceAgreement agreement = FreightProduction?.ServiceAgreements?.Agreements?.FirstOrDefault(candidate =>
                candidate != null && candidate.Status == FreightServiceAgreementStatus.Accepted
                && !string.IsNullOrWhiteSpace(candidate.ShipmentId)
                && string.Equals(candidate.CarrierBusinessId, freight.InstanceId, StringComparison.Ordinal));
            if (agreement == null || string.Equals(agreement.FulfilmentStatus, "Prepared for dispatch", StringComparison.Ordinal)) return;
            LogisticsShipmentState shipment = FreightProduction.AllShipments().FirstOrDefault(candidate => candidate != null
                && string.Equals(candidate.ShipmentId, agreement.ShipmentId, StringComparison.Ordinal));
            if (shipment == null || shipment.Status != LogisticsShipmentStatus.Planned
                || !shipment.requiresDispatchAuthorization
                || !string.Equals(shipment.freightServiceAgreementId, agreement.AgreementId, StringComparison.Ordinal)) return;
            if (wagon.HitchPreparationAttempted
                && string.Equals(wagon.HitchPreparationShipmentId, shipment.ShipmentId, StringComparison.Ordinal)) return;
            if (!HorseAuthority.IsWorkReady(horse))
            {
                LogFreightAgreementBlockerOnce("hitch-readiness:" + shipment.ShipmentId,
                    $"[Hitching] Shipment '{shipment.ShipmentId}' remains pending: A0 is not work-ready (health={horse.Health01:0.00}, nutrition={horse.Nutrition01:0.00}, fatigue={horse.Fatigue01:0.00}).");
                return;
            }

            PersonState ethan = populationManager.State.GetPerson(freight.Owner.PersonId);
            if (ethan == null) return;
            string staging = ethan.homeBuildingId >= 0
                ? TownJourneyNetworkBuilder.LocationIdForBuilding(ethan.homeBuildingId)
                : string.Empty;
            string origin = ResolvePersonJourneyLocation(ethan.id);
            if (string.IsNullOrWhiteSpace(staging) || hub.Journeys.GetLocation(staging) == null
                || (origin != staging && (string.IsNullOrWhiteSpace(origin) || !hub.Journeys.FindRoute(origin, staging, TravelMode.Foot).Found)))
            {
                Debug.LogWarning($"[Hitching] Development preparation not assigned: Ethan cannot reach A0 and wagon staging at '{staging}'.", this);
                return;
            }

            // The current opening business meeting leaves Ethan at this staging
            // location. Keep the accepted shipment pending until he is physically
            // there; the next stage can schedule an arrival-driven approach if
            // future dispatch work leaves him elsewhere.
            if (!IsPersonVisuallyAtBuilding(ethan.id, ethan.homeBuildingId)) return;

            EntityId businessOwner = EntityId.For(EntityKind.Business, StableBusinessRuntimeId(freight.InstanceId));
            WorkTask hitch = hub.Tasks.CreateTaskWithPlannedMinutes(OpeningHitchTaskId, businessOwner,
                absoluteDayIndex, Math.Max(1, developmentHitchMinutes),
                customerRef: $"freight-preparation:{agreement.AgreementId}:{shipment.ShipmentId}:A0:{wagon.AssetId}");
            string taskId = hitch.TaskId.ToString();
            wagon.HitchPreparationAttempted = true;
            wagon.HitchPreparationTaskId = taskId;
            wagon.HitchPreparationShipmentId = shipment.ShipmentId;
            Debug.Log($"[Hitching] Accepted shipment '{shipment.ShipmentId}' queued one preparation task '{taskId}' " +
                $"for Ethan P{ethan.id}; A0 and wagon reservations will be acquired only when a legitimate " +
                $"{Math.Max(1, developmentHitchMinutes)}-minute work window is available.", this);
        }

        private void StartQueuedOpeningHitchTask(int dayIndex, int tickStartMinute, int tickEndMinute)
        {
            if (hub?.Tasks == null || sharedBusinessRuntime == null || populationManager?.State == null
                || !sharedBusinessRuntime.TryFindByInstanceId("livery_freight_003", out BusinessInstanceState freight)) return;
            EquipmentAsset wagon = hub.TransportAssets.Get("opening-freight-wagon");
            if (wagon == null || string.IsNullOrWhiteSpace(wagon.HitchPreparationTaskId)) return;
            WorkTask hitch = hub.Tasks.FindTaskByIdString(wagon.HitchPreparationTaskId);
            PersonState ethan = freight.Owner != null ? populationManager.State.GetPerson(freight.Owner.PersonId) : null;
            if (hitch == null || ethan == null) return;

            if (hitch.Status != TaskStatus.Queued && hitch.Status != TaskStatus.Assigned) return;
            if (!IsPersonVisuallyAtBuilding(ethan.id, ethan.homeBuildingId)) return;
            int nowMinute = timeManager != null ? Mathf.FloorToInt(timeManager.TimeOfDay01 * 1440f) : tickStartMinute;
            PersonActivityReservation scheduled = hub.PersonSchedules.ActiveFor(ethan.id, dayIndex)
                .FirstOrDefault(reservation => reservation != null
                    && string.Equals(reservation.Label, TaskReservationLabel(hitch), StringComparison.Ordinal));
            int startMinute = scheduled != null ? scheduled.StartMinuteOfDay
                : FindAvailablePersonStartMinute(ethan.id, dayIndex, nowMinute, Math.Max(1, hitch.MinutesRemaining));
            if (startMinute < 0 || startMinute > tickEndMinute) return;

            AnimalState horse = FindOpeningFreightHorse(freight.InstanceId);
            if (horse == null || !HorseAuthority.IsWorkReady(horse)) return;
            float startSeconds = AbsoluteNowGameSeconds() + Math.Max(0, startMinute - nowMinute) * 60f;
            float endSeconds = startSeconds + Math.Max(1, hitch.MinutesRemaining) * 60f;
            var availability = new AnimalAvailabilityAuthority(hub.ReservationAuthority);
            if (horse.CurrentAssignmentId != hitch.TaskId.ToString())
            {
                string horseRefusal = HorseAuthority.TryReserveUse(horse, HorseUseKind.Draft,
                    hitch.TaskId.ToString(), availability, startSeconds, endSeconds, out _);
                if (horseRefusal != null)
                {
                    if (taskStartRefusalAlreadyLogged.Add("horse:" + hitch.TaskId))
                        Debug.LogWarning($"[Hitching] Task '{hitch.TaskId}' remains queued; A0 reservation refused: {horseRefusal}", this);
                    return;
                }
            }
            if (string.IsNullOrWhiteSpace(wagon.HitchPreparationReservationId))
            {
                var wagonRequest = new ReservationRequest(hitch.TaskId.ToString(), "accepted freight shipment hitch preparation");
                wagonRequest.Items.Add(new ReservationItem(ReservationKeyConventions.Asset(wagon.AssetId), 1f, startSeconds, endSeconds));
                string wagonRefusal = hub.ReservationAuthority.TryReserveAll(wagonRequest, out ReservationHandle wagonHold);
                if (wagonRefusal != null)
                {
                    HorseAuthority.ReleaseUse(horse, availability);
                    if (taskStartRefusalAlreadyLogged.Add("wagon:" + hitch.TaskId))
                        Debug.LogWarning($"[Hitching] Task '{hitch.TaskId}' remains queued; wagon reservation refused: {wagonRefusal}", this);
                    return;
                }
                wagon.HitchPreparationReservationId = wagonHold?.ReservationId ?? string.Empty;
            }

            bool wasQueued = hitch.Status == TaskStatus.Queued;
            if (!TryStartBusinessPersonTask(hitch, ethan, dayIndex, PersonActivityKind.Work, -1, out string prepareRefusal))
            {
                ReleaseOpeningHitchReservations(horse, wagon);
                if (taskStartRefusalAlreadyLogged.Add("schedule:" + hitch.TaskId))
                    Debug.LogWarning($"[Hitching] Task '{hitch.TaskId}' retained queued until Ethan has a valid work opening: {prepareRefusal}", this);
                return;
            }
            if (wasQueued && hitch.Status == TaskStatus.Assigned)
                Debug.Log($"[Hitching] Ethan P{ethan.id} assigned hitch task '{hitch.TaskId}' at minute {startMinute}; " +
                    $"HorseAuthority reserved A0 and ReservationAuthority reserved wagon '{wagon.AssetId}'.", this);
            if (!BeginScheduledBusinessPersonTask(hitch, ethan, dayIndex, tickStartMinute,
                    tickEndMinute, -1, out string startRefusal)
                && taskStartRefusalAlreadyLogged.Add("start:" + hitch.TaskId))
            {
                ReleaseOpeningHitchReservations(horse, wagon);
                Debug.LogWarning($"[Hitching] Task '{hitch.TaskId}' remains pending; start refused: {startRefusal}", this);
            }
        }

        private void HandleCompletedOpeningHitchTask(WorkTask task, int dayIndex)
        {
            if (task == null || !sharedBusinessRuntime.TryFindByInstanceId("livery_freight_003", out BusinessInstanceState freight)) return;
            EquipmentAsset wagon = hub.TransportAssets.Get("opening-freight-wagon");
            if (wagon == null) return;
            if (task.CustomerRef.StartsWith(DevelopmentHitchTaskPrefix + "travel:", StringComparison.Ordinal))
            {
                DevelopmentHitchTaskData trip = JsonUtility.FromJson<DevelopmentHitchTaskData>(task.TermsRef);
                if (trip == null || trip.HitchTaskId != wagon.HitchPreparationTaskId) return;
                PersonState ethan = populationManager?.State?.GetPerson(trip.PersonId);
                AnimalState horse = FindOpeningFreightHorse(freight.InstanceId);
                if (ethan == null || horse == null) return;
                SetPersonJourneyLocation(ethan.id, trip.StagingLocationId);
                horse.TransportState = HorseTransportState.AvailableUnsecured;
                horse.SecuredFixtureId = string.Empty;
                Debug.Log($"[Hitching] Ethan P{ethan.id} reached staging after task '{task.TaskId}' " +
                    $"at game minute {Mathf.FloorToInt(AbsoluteNowGameSeconds() / 60f)}; A0 released from post, " +
                    $"wagon='{wagon.AssetId}' still separate.", this);
                return;
            }

            if (!string.Equals(task.TaskId.ToString(), wagon.HitchPreparationTaskId, StringComparison.Ordinal)) return;
            AnimalState attachedHorse = FindOpeningFreightHorse(freight.InstanceId);
            if (attachedHorse == null) return;
            if (wagon.HitchingAnimalIds == null) wagon.HitchingAnimalIds = new List<string>();
            if (!wagon.HitchingAnimalIds.Contains(attachedHorse.AnimalId.ToString()))
                wagon.HitchingAnimalIds.Add(attachedHorse.AnimalId.ToString());
            attachedHorse.TransportState = HorseTransportState.AttachedToWagon;
            attachedHorse.AttachedWagonAssetId = wagon.AssetId;
            attachedHorse.SecuredFixtureId = string.Empty;
            attachedHorse.PhysicalLocationId = OpeningHitchPostId;
            FindAnyObjectByType<OpeningTransportPresentationController>()?.RefreshPresentationState(attachedHorse, wagon);
            ReleaseOpeningHitchReservations(attachedHorse, wagon);
            FreightServiceAgreement agreement = FreightProduction?.ServiceAgreements?.Agreements?.FirstOrDefault(candidate => candidate != null
                && candidate.Status == FreightServiceAgreementStatus.Accepted
                && string.Equals(candidate.ShipmentId, wagon.HitchPreparationShipmentId, StringComparison.Ordinal));
            LogisticsShipmentState shipment = FreightProduction?.AllShipments()?.FirstOrDefault(candidate => candidate != null
                && string.Equals(candidate.ShipmentId, wagon.HitchPreparationShipmentId, StringComparison.Ordinal));
            string preparationRefusal = agreement == null ? "Accepted shipment agreement is unavailable." :
                attachedHorse.OwnerKind != AnimalOwnerKind.Business
                || !string.Equals(attachedHorse.OwnerId, freight.InstanceId, StringComparison.Ordinal)
                || !string.Equals(attachedHorse.AttachedWagonAssetId, wagon.AssetId, StringComparison.Ordinal)
                || wagon.HitchingAnimalIds == null || !wagon.HitchingAnimalIds.Contains(attachedHorse.AnimalId.ToString())
                    ? "Prepared horse/wagon identity or B3 ownership does not match the accepted shipment." :
                shipment == null || shipment.Status != LogisticsShipmentStatus.Planned || !shipment.requiresDispatchAuthorization
                || !string.Equals(shipment.freightServiceAgreementId, agreement.AgreementId, StringComparison.Ordinal)
                    ? "Shipment is not the accepted agreement's planned, pickup-held shipment." :
                FreightProduction.ServiceAgreements.MarkPreparedForDispatch(agreement.AgreementId,
                    wagon.HitchPreparationShipmentId, AbsoluteNowGameSeconds());
            if (preparationRefusal != null)
                Debug.LogError($"[Hitching] Task '{task.TaskId}' attached A0, but shipment preparation could not be recorded: {preparationRefusal}", this);
            else
                CreatePreparedFreightDispatchTask(agreement, shipment, freight, attachedHorse, wagon, dayIndex);
            int workedMinutes = Math.Max(0, task.PlannedMinutes - task.MinutesRemaining);
            Debug.Log($"[Hitching] Freight preparation task '{task.TaskId}' completed after {workedMinutes} elapsed in-game " +
                $"minute(s): A0='{attachedHorse.AnimalId}' attached to wagon='{wagon.AssetId}'; " +
                $"shipment='{wagon.HitchPreparationShipmentId}' " +
                (preparationRefusal == null ? $"prepared at absolute {AbsoluteNowGameSeconds():0}s (day {dayIndex}) after agreement acceptance at {agreement.AcceptedGameSeconds:0}s (day {agreement.AcceptedDayIndex}); " : "remains not marked prepared; ") +
                $"identities unchanged, " +
                $"no pickup, cargo movement, or freight payment occurred.", this);
        }

        private void CreatePreparedFreightDispatchTask(FreightServiceAgreement agreement,
            LogisticsShipmentState shipment, BusinessInstanceState freight, AnimalState horse,
            EquipmentAsset wagon, int dayIndex)
        {
            if (hub?.Tasks == null || agreement == null || shipment == null || freight?.Owner == null
                || horse == null || wagon == null)
            {
                LogFreightAgreementBlockerOnce("dispatch-context:" + agreement?.AgreementId,
                    "[FreightDispatch] Prepared shipment retained; TaskAuthority, agreement, shipment, Ethan, A0, or wagon is unresolved.");
                return;
            }
            int operatorId = freight.Owner.PersonId;
            if (operatorId < 0 || operatorId != agreement.AcceptingOperatorPersonId)
            {
                LogFreightAgreementBlockerOnce("dispatch-operator:" + shipment.ShipmentId,
                    $"[FreightDispatch] Shipment '{shipment.ShipmentId}' remains prepared; B3 operator P{operatorId} does not match accepted operator P{agreement.AcceptingOperatorPersonId}.");
                return;
            }
            if (agreement.Status != FreightServiceAgreementStatus.Accepted
                || !string.Equals(agreement.ShipmentId, shipment.ShipmentId, StringComparison.Ordinal)
                || !string.Equals(agreement.FulfilmentStatus, "Prepared for dispatch", StringComparison.Ordinal)
                || shipment.Status != LogisticsShipmentStatus.Planned || !shipment.requiresDispatchAuthorization
                || !string.Equals(shipment.freightServiceAgreementId, agreement.AgreementId, StringComparison.Ordinal)
                || !string.Equals(shipment.physicalWagonAssetId, wagon.AssetId, StringComparison.Ordinal)
                || shipment.physicalTransportBound)
            {
                LogFreightAgreementBlockerOnce("dispatch-state:" + shipment.ShipmentId,
                    $"[FreightDispatch] Shipment '{shipment.ShipmentId}' remains prepared; accepted agreement, shipment hold, or status did not match.");
                return;
            }
            if (horse.OwnerKind != AnimalOwnerKind.Business || !string.Equals(horse.OwnerId, freight.InstanceId, StringComparison.Ordinal)
                || horse.TransportState != HorseTransportState.AttachedToWagon
                || !string.Equals(horse.AttachedWagonAssetId, wagon.AssetId, StringComparison.Ordinal)
                || wagon.OwnerKind != "business" || !string.Equals(wagon.OwnerId, freight.InstanceId, StringComparison.Ordinal)
                || wagon.HitchingAnimalIds == null || !wagon.HitchingAnimalIds.Contains(horse.AnimalId.ToString()))
            {
                LogFreightAgreementBlockerOnce("dispatch-team:" + shipment.ShipmentId,
                    $"[FreightDispatch] Shipment '{shipment.ShipmentId}' remains prepared; A0/wagon attachment or B3 ownership is invalid.");
                return;
            }

            string taskRef = FreightDispatchTaskPrefix + agreement.AgreementId + ":" + shipment.ShipmentId;
            if (!string.IsNullOrWhiteSpace(agreement.DispatchTaskId))
            {
                WorkTask recorded = hub.Tasks.FindTaskByIdString(agreement.DispatchTaskId);
                if (recorded != null && string.Equals(recorded.CustomerRef, taskRef, StringComparison.Ordinal)) return;
                LogFreightAgreementBlockerOnce("dispatch-task-missing:" + agreement.AgreementId,
                    $"[FreightDispatch] Shipment '{shipment.ShipmentId}' retains its recorded task ID '{agreement.DispatchTaskId}', but that TaskAuthority record is unavailable; no replacement was created.");
                return;
            }

            // Recover an already-created-but-not-linked task by its stable reference before creating anything.
            WorkTask existing = hub.Tasks.GetAllOpenTasks().FirstOrDefault(candidate => candidate != null
                && string.Equals(candidate.CustomerRef, taskRef, StringComparison.Ordinal));
            if (existing != null)
            {
                string recoveryRefusal = FreightProduction.ServiceAgreements.RegisterDispatchTask(
                    agreement.AgreementId, shipment.ShipmentId, existing.TaskId.ToString());
                if (recoveryRefusal != null)
                    LogFreightAgreementBlockerOnce("dispatch-link-existing:" + agreement.AgreementId,
                        $"[FreightDispatch] Existing task '{existing.TaskId}' could not be linked: {recoveryRefusal}");
                return;
            }

            var terms = new FreightDispatchTaskData
            {
                AgreementId = agreement.AgreementId,
                ShipmentId = shipment.ShipmentId,
                OperatorPersonId = operatorId,
                HorseId = horse.AnimalId.ToString(),
                WagonId = wagon.AssetId
            };
            // The existing freight driver owns the shipment's team plan. Rebind it to the verified
            // prepared team; the shipment remains authorization-held, so this cannot begin loading.
            FreightProduction.SetDispatchPlan(shipment.ShipmentId,
                EntityId.For(EntityKind.Person, operatorId), new List<EntityId> { horse.AnimalId });
            WorkTask dispatch = hub.Tasks.CreateTaskWithPlannedMinutes(FreightDispatchTaskId,
                EntityId.For(EntityKind.Business, StableBusinessRuntimeId(freight.InstanceId)), dayIndex, 1,
                customerRef: taskRef, termsRef: JsonUtility.ToJson(terms));
            string linkRefusal = FreightProduction.ServiceAgreements.RegisterDispatchTask(
                agreement.AgreementId, shipment.ShipmentId, dispatch.TaskId.ToString());
            if (linkRefusal != null)
            {
                Debug.LogError($"[FreightDispatch] TaskAuthority created '{dispatch.TaskId}' for shipment '{shipment.ShipmentId}', but agreement linking failed: {linkRefusal}. The task remains queued; no departure or pickup occurred.", this);
                return;
            }
            Debug.Log($"[FreightDispatch] One queued TaskAuthority assignment '{dispatch.TaskId}' created for Ethan P{operatorId}: " +
                $"agreement='{agreement.AgreementId}', shipment='{shipment.ShipmentId}', horse='{horse.AnimalId}', wagon='{wagon.AssetId}'. " +
                $"The FreightProductionDriver team plan is bound to Ethan and A0; the queued task is eligible for scheduling and has not started. " +
                $"Acceptance={agreement.AcceptedGameSeconds:0}s/day {agreement.AcceptedDayIndex}, preparation={agreement.PreparedGameSeconds:0}s/day {dayIndex}. Shipment remains Planned with dispatch hold; no departure, pickup, custody change, or payment occurred.", this);
        }

        private AnimalState FindOpeningFreightHorse(string businessId)
        {
            if (hub?.Animals == null) return null;
            foreach (AnimalState candidate in hub.Animals.ActiveAnimals)
                if (candidate != null && candidate.IsActive && candidate.Species == AnimalSpecies.Horse
                    && candidate.AnimalId.ToString() == "A0" && candidate.OwnerKind == AnimalOwnerKind.Business
                    && candidate.OwnerId == businessId) return candidate;
            return null;
        }

        private void ReleaseOpeningHitchReservations(AnimalState horse, EquipmentAsset wagon)
        {
            if (horse != null)
                HorseAuthority.ReleaseUse(horse, new AnimalAvailabilityAuthority(hub.ReservationAuthority));
            if (wagon != null && !string.IsNullOrWhiteSpace(wagon.HitchPreparationReservationId))
            {
                hub.ReservationAuthority.Release(wagon.HitchPreparationReservationId);
                wagon.HitchPreparationReservationId = string.Empty;
            }
        }

        private void StartQueuedFreightDispatchTasks(int dayIndex, int tickStartMinute, int tickEndMinute)
        {
            FreightServiceAgreementBook agreements = FreightProduction?.ServiceAgreements;
            if (agreements?.Agreements == null || hub?.Tasks == null || populationManager?.State == null)
                return;

            foreach (FreightServiceAgreement agreement in agreements.Agreements.ToList())
            {
                if (agreement == null || agreement.Status != FreightServiceAgreementStatus.Accepted
                    || agreement.PreparedGameSeconds <= 0f || string.IsNullOrWhiteSpace(agreement.DispatchTaskId))
                    continue;

                WorkTask task = hub.Tasks.FindTaskByIdString(agreement.DispatchTaskId);
                if (task == null || (task.Status != TaskStatus.Queued && task.Status != TaskStatus.Assigned))
                    continue;

                LogisticsShipmentState shipment = FreightProduction.AllShipments().FirstOrDefault(candidate => candidate != null
                    && string.Equals(candidate.ShipmentId, agreement.ShipmentId, StringComparison.Ordinal));
                BusinessInstanceState freight = FindBusiness(agreement.CarrierBusinessId);
                int operatorId = freight?.Owner?.PersonId ?? -1;
                PersonState operatorPerson = operatorId >= 0 ? populationManager.State.GetPerson(operatorId) : null;
                AnimalState horse = FindOpeningFreightHorse(agreement.CarrierBusinessId);
                EquipmentAsset wagon = hub.TransportAssets?.Get("opening-freight-wagon");
                FreightDispatchTaskData taskData = string.IsNullOrWhiteSpace(task.TermsRef)
                    ? null : JsonUtility.FromJson<FreightDispatchTaskData>(task.TermsRef);
                string expectedRef = FreightDispatchTaskPrefix + agreement.AgreementId + ":" + agreement.ShipmentId;
                bool horseReservedForShipment = horse != null
                    && horse.CurrentUse == HorseUseKind.Draft
                    && string.Equals(horse.CurrentAssignmentId, shipment?.ShipmentId, StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(horse.CurrentReservationId);
                bool wagonReservedForShipment = wagon != null
                    && wagon.IsTransportAssigned
                    && string.Equals(wagon.CurrentShipmentId, shipment?.ShipmentId, StringComparison.Ordinal)
                    && string.Equals(wagon.CurrentDriverPersonId, operatorId.ToString(), StringComparison.Ordinal)
                    && wagon.DraftAnimalIds != null
                    && wagon.DraftAnimalIds.Contains(horse != null ? horse.AnimalId.ToString() : string.Empty);
                string refusal = agreement.PreparedGameSeconds <= agreement.AcceptedGameSeconds
                    ? "shipment preparation does not chronologically follow agreement acceptance."
                    : shipment == null || shipment.Status != LogisticsShipmentStatus.Planned
                        || !shipment.requiresDispatchAuthorization
                        || !string.Equals(shipment.freightServiceAgreementId, agreement.AgreementId, StringComparison.Ordinal)
                        ? "the linked shipment is not still held in Planned status for dispatch preparation."
                    : freight == null || operatorPerson == null || operatorId != agreement.AcceptingOperatorPersonId
                        ? $"B3 operator P{operatorId} does not resolve to accepted operator P{agreement.AcceptingOperatorPersonId}."
                    : taskData == null || !string.Equals(task.CustomerRef, expectedRef, StringComparison.Ordinal)
                        || taskData.OperatorPersonId != operatorId
                        || !string.Equals(taskData.AgreementId, agreement.AgreementId, StringComparison.Ordinal)
                        || !string.Equals(taskData.ShipmentId, agreement.ShipmentId, StringComparison.Ordinal)
                        ? "the recorded TaskAuthority assignment does not match the accepted agreement and shipment identities."
                    : horse == null || horse.OwnerKind != AnimalOwnerKind.Business
                        || !string.Equals(horse.OwnerId, freight.InstanceId, StringComparison.Ordinal)
                        || !string.Equals(taskData.HorseId, horse.AnimalId.ToString(), StringComparison.Ordinal)
                        || horse.TransportState != HorseTransportState.AttachedToWagon
                        || !string.Equals(horse.AttachedWagonAssetId, taskData.WagonId, StringComparison.Ordinal)
                        || ((horse.CurrentUse != HorseUseKind.None || !string.IsNullOrWhiteSpace(horse.CurrentAssignmentId))
                            && !horseReservedForShipment)
                        || !HorseAuthority.IsWorkReady(horse)
                        ? "A0 is not the available, work-ready B3 horse attached to this shipment's wagon."
                    : wagon == null || !wagon.IsWagon || !wagon.IsUsable
                        || !string.Equals(wagon.AssetId, taskData.WagonId, StringComparison.Ordinal)
                        || wagon.OwnerKind != "business" || !string.Equals(wagon.OwnerId, freight.InstanceId, StringComparison.Ordinal)
                        || !string.Equals(wagon.HitchPreparationShipmentId, shipment.ShipmentId, StringComparison.Ordinal)
                        || wagon.HitchingAnimalIds == null || !wagon.HitchingAnimalIds.Contains(horse.AnimalId.ToString())
                        || !string.Equals(shipment.physicalWagonAssetId, wagon.AssetId, StringComparison.Ordinal)
                        || (wagon.IsReserved && !string.Equals(wagon.ReservedFor, shipment.ShipmentId, StringComparison.Ordinal))
                        || (wagon.IsTransportAssigned && !wagonReservedForShipment)
                        ? "the assigned wagon is unavailable, not attached to A0, or not prepared for this shipment."
                    : task.HasAssignee && task.AssigneeId.Id != operatorId
                        ? $"dispatch task '{task.TaskId}' is already assigned to P{task.AssigneeId.Id}, not Ethan P{operatorId}."
                        : null;

                if (refusal != null)
                {
                    LogFreightAgreementBlockerOnce("dispatch-start-prerequisite:" + task.TaskId,
                        $"[FreightDispatch] Queued task '{task.TaskId}' retained; start refused: {refusal}");
                    continue;
                }

                if (!TryStartBusinessPersonTask(task, operatorPerson, dayIndex, PersonActivityKind.Work,
                        -1, out string schedulingRefusal))
                {
                    LogFreightAgreementBlockerOnce("dispatch-start-schedule:" + task.TaskId,
                        $"[FreightDispatch] Task '{task.TaskId}' remains queued for Ethan P{operatorId}; schedule/task authority refused: {schedulingRefusal}");
                    continue;
                }

                TaskStatus before = task.Status;
                if (!BeginScheduledBusinessPersonTask(task, operatorPerson, dayIndex, tickStartMinute,
                        tickEndMinute, -1, out string startRefusal))
                {
                    LogFreightAgreementBlockerOnce("dispatch-start-transition:" + task.TaskId,
                        $"[FreightDispatch] Task '{task.TaskId}' remains assigned to Ethan P{operatorId}; TaskAuthority refused start: {startRefusal}");
                    continue;
                }

                if (before != TaskStatus.InProgress && task.Status == TaskStatus.InProgress)
                {
                    Debug.Log($"[FreightDispatch] Existing dispatch task '{task.TaskId}' started for Ethan P{operatorId} " +
                        $"at absolute simulation time {AbsoluteNowGameSeconds():0}s (day {dayIndex}); " +
                        $"agreement='{agreement.AgreementId}', shipment='{shipment.ShipmentId}', A0='{horse.AnimalId}', " +
                        $"wagon='{wagon.AssetId}'. Work-time reservation is active; no departure, arrival, pickup, cargo transfer, or payment occurred.", this);
                }
            }
        }

        private void MonitorOpeningHitchCancellation(int dayIndex)
        {
            EquipmentAsset wagon = hub?.TransportAssets?.Get("opening-freight-wagon");
            if (wagon == null || !wagon.HitchPreparationAttempted
                || string.IsNullOrWhiteSpace(wagon.HitchPreparationTaskId)) return;
            WorkTask hitch = hub.Tasks.FindTaskByIdString(wagon.HitchPreparationTaskId);
            WorkTask travel = string.IsNullOrWhiteSpace(wagon.HitchPreparationTravelTaskId)
                ? null : hub.Tasks.FindTaskByIdString(wagon.HitchPreparationTravelTaskId);
            bool cancelled = IsHitchTaskCancelled(hitch) || IsHitchTaskCancelled(travel);
            if (!cancelled) return;

            AnimalState horse = FindOpeningFreightHorse(wagon.OwnerId);
            if (horse != null && horse.TransportState != HorseTransportState.AttachedToWagon)
            {
                horse.TransportState = HorseTransportState.SecuredAtHitchingPost;
                horse.SecuredFixtureId = OpeningHitchPostId;
                horse.AttachedWagonAssetId = string.Empty;
            }
            if (hitch != null && hitch.IsOpen) hub.Tasks.CancelTask(hitch.TaskId, hub.WorkTimeBudgets);
            ReleaseOpeningHitchReservations(horse, wagon);
            wagon.HitchPreparationTaskId = string.Empty;
            wagon.HitchPreparationTravelTaskId = string.Empty;
            wagon.HitchPreparationShipmentId = string.Empty;
            wagon.HitchPreparationAttempted = false;
            FindAnyObjectByType<OpeningTransportPresentationController>()?.RefreshPresentationState(horse, wagon);
            Debug.LogWarning($"[Hitching] Development preparation cancelled or failed on day {dayIndex}; " +
                $"horse and wagon reservations released, A0 returned to its separate hitch-post state.", this);
        }

        private static bool IsHitchTaskCancelled(WorkTask task) => task != null
            && (task.Status == TaskStatus.Cancelled || task.Status == TaskStatus.Failed
                || task.Status == TaskStatus.Interrupted);

        private void DriveHorseCareDay(int absoluteDayIndex)
        {
            if (hub == null || hub.Animals == null) return;
            var beforeByHorse = new Dictionary<EntityId, (int StockUnits, float Nutrition01, string Owner)>();
            foreach (AnimalState horse in hub.Animals.ActiveAnimals)
            {
                if (horse == null || horse.Species != AnimalSpecies.Horse || !horse.IsActive
                    || horse.LastCareDayIndex >= absoluteDayIndex) continue;
                IHorseFeedStock source = ResolveHorseFeedStock(horse);
                beforeByHorse[horse.AnimalId] = (
                    source?.FeedStockUnits ?? 0,
                    horse.Nutrition01,
                    horse.OwnerId ?? string.Empty);
            }
            var diagnostics = new List<string>();
            int unfed = HorseCareDriver.CareDay(
                hub.Animals,
                ResolveHorseFeedStock,
                horse => horse != null && hub.DraftPower.IsAnimalReserved(horse.AnimalId),
                absoluteDayIndex,
                diagnostics);
            foreach (AnimalState horse in hub.Animals.ActiveAnimals)
            {
                if (horse == null || horse.Species != AnimalSpecies.Horse
                    || !beforeByHorse.TryGetValue(horse.AnimalId, out var before)) continue;
                IHorseFeedStock afterSource = ResolveHorseFeedStock(horse);
                int stockAfter = afterSource?.FeedStockUnits ?? 0;
                int drawnUnits = Math.Max(0, before.StockUnits - stockAfter);
                if (drawnUnits > 0)
                {
                    horseCareRiskAlreadyLogged.Remove(horse.AnimalId.ToString());
                    Debug.Log($"[AnimalCare] horse fed: horse='{horse.AnimalId}', owner='{before.Owner}', " +
                        $"feed drawn={drawnUnits} feed unit(s), stock after={stockAfter}, " +
                        $"nutrition {before.Nutrition01:0.00}->{horse.Nutrition01:0.00}.");
                }
                else if (horse.Nutrition01 < before.Nutrition01 || afterSource == null || stockAfter <= 0)
                {
                    if (horseCareRiskAlreadyLogged.Add(horse.AnimalId.ToString()))
                        Debug.LogWarning($"[AnimalCare] horse at risk: horse='{horse.AnimalId}', owner='{before.Owner}', " +
                            $"feed drawn=0, feed stock after={stockAfter}, nutrition " +
                            $"{before.Nutrition01:0.00}->{horse.Nutrition01:0.00}; " +
                            (afterSource == null ? "no registered owner feed source." : "registered source had no available feed."), this);
                }
            }
            if (diagnostics.Count > 0 && unfed == 0)
            {
                Debug.LogWarning("[SimulationDrivers] horse care day " + absoluteDayIndex + ": " +
                    string.Join(" | ", diagnostics));
            }
        }

        private void DriveStoreFeedProcurement(int absoluteDayIndex)
        {
            if (storeFeedProcurement == null || storeFeedProcurementBusiness?.RuntimeState == null) return;
            BusinessRuntimeState storeRuntime = storeFeedProcurementBusiness.RuntimeState;
            storeFeedProcurement.ReceiveArrivals(absoluteDayIndex, null);
            // ReceiveArrivals settles the purchase and persists its named
            // source lot before this retail-lot transfer. Retry any remaining
            // source lots after a refused destination receipt; already
            // received retail lot ids are an idempotency guard.
            foreach (NamedSkuLot lot in storeFeedProcurement.Lots)
            {
                if (lot == null || lot.RemainingUnits <= 0) continue;
                int transferUnits = lot.RemainingUnits;
                int cashBeforeReceipt = storeRuntime.CurrentCashCents;
                int retailStockBefore = storeRuntime.GetRetailItemStockUnits(lot.SkuId, "animal_feed");
                PurchasedRetailLotReceipt.Result receipt = PurchasedRetailLotReceipt.Transfer(
                    storeFeedProcurement, storeRuntime, lot, "animal_feed", "sack", absoluteDayIndex);
                if (!receipt.Received)
                {
                    if (!receipt.Duplicate && receipt.Units > 0)
                        Debug.LogWarning($"[FeedSupply] Retail receipt for supplier lot '{lot.LotId}' " +
                            $"({receipt.Units} sacks) refused: {receipt.Refusal} " +
                            $"Source lot restored: {receipt.SourceRestored}.", this);
                    continue;
                }
                Debug.Log($"[FeedSupply] General Store B0 received {transferUnits} purchased sack(s) of {lot.SkuId} " +
                    $"from external-market supplier lot '{lot.LotId}' (receipt {lot.ReceiptId}, order {lot.OrderId}); " +
                    $"business paid {lot.TotalCostCents}c on arrival, cash {cashBeforeReceipt}->{storeRuntime.CurrentCashCents}c; " +
                    $"identified SKU stock {retailStockBefore}->{storeRuntime.GetRetailItemStockUnits(lot.SkuId, "animal_feed")} sack(s). " +
                    $"retailLot='{receipt.RetailLotId}'; source provenance: {lot.Provenance}", this);
                var storeManager = FindAnyObjectByType<LandLedgers.FirstLedger.GeneralStoreRuntimeManager>();
                if (storeManager != null)
                    RegisterGeneralStoreSupplier(storeManager.BuildHouseholdTradingPort(
                        storeFeedProcurementBusiness, storeFeedRegistry));
            }
        }

        private IHorseFeedStock ResolveHorseFeedStock(AnimalState horse)
        {
            return ResolveOwnerScopedHorseFeedStock(horse, (ownerKind, ownerId) =>
            {
                if (hub == null || string.IsNullOrWhiteSpace(ownerId))
                    return null;

                if (ownerKind == AnimalOwnerKind.Person || ownerKind == AnimalOwnerKind.Household)
                {
                    int householdId = -1;
                    if (ownerKind == AnimalOwnerKind.Household)
                    {
                        int.TryParse(ownerId, out householdId);
                    }
                    else
                    {
                        string personText = ownerId.StartsWith("P", StringComparison.OrdinalIgnoreCase)
                            ? ownerId.Substring(1) : ownerId;
                        if (int.TryParse(personText, out int personId))
                            householdId = householdMemberships != null
                                ? householdMemberships.GetActiveHouseholdId(personId) : -1;
                    }
                    if (householdId < 0 || hub.HouseholdInventories == null) return null;
                    HouseholdInventory inventory = hub.HouseholdInventories.Get(householdId);
                    if (inventory == null) return null;
                    string carryKey = horse != null ? horse.AnimalId.ToString() : ownerId;
                    return new Review2HouseholdHorseFeed(
                        inventory, "store_horse_feed_sack", 2, 1,
                        timeManager != null ? timeManager.CurrentAbsoluteDayIndex : 0,
                        review2D3SaveState != null ? review2D3SaveState.GetHorseFeedCarry(carryKey) : 0,
                        amount => review2D3SaveState?.SetHorseFeedCarry(carryKey, amount));
                }

                if (ownerKind != AnimalOwnerKind.Business) return null;

                if (ownerId.StartsWith("farm:", StringComparison.Ordinal))
                    return hub.FarmSlice != null ? new HorseFeedStockAdapter(hub.FarmSlice.Feed) : null;

                if (sharedBusinessRuntime != null
                    && sharedBusinessRuntime.TryFindByInstanceId(ownerId, out BusinessInstanceState owner)
                    && owner?.RuntimeState != null)
                {
                    // Business category balances are reporting aggregates, not
                    // proof of feed. Require the explicit owner-added feed
                    // registration before drawing category stock; do not infer
                    // that a mixed or merely similarly named pool is feed.
                    if (owner.BusinessType == BusinessType.GeneralStore
                        && storeFeedRegistry != null)
                    {
                        Review2StoreFeedItem feedItem = storeFeedRegistry.Find(ownerId);
                        if (feedItem != null)
                        {
                            return Review2StoreHorseFeed.FeedStockForBusiness(
                                owner.RuntimeState, feedItem, null);
                        }
                    }

                    // Freight horse feed belongs to the horse's business
                    // owner. The opening development SKU is sold in sacks;
                    // one sack is explicitly converted to two care units.
                    // Partial-package carry is persisted by horse identity.
                    if (owner.BusinessType == BusinessType.LiveryFreight)
                    {
                        string carryKey = horse != null ? horse.AnimalId.ToString() : ownerId;
                        return new BusinessHorseFeedStockAdapter(
                            owner.RuntimeState,
                            "store_horse_feed_sack",
                            "animal_feed",
                            "sack",
                            2,
                            1,
                            () => review2D3SaveState != null
                                ? review2D3SaveState.GetHorseFeedCarry(carryKey) : 0,
                            amount => review2D3SaveState?.SetHorseFeedCarry(carryKey, amount));
                    }
                    return null;
                }

                return null;
            });
        }

        /// <summary>
        /// Resolves only the source registered to this animal's exact owner
        /// identity. The resolver deliberately does not fall back across
        /// businesses, households, or owner kinds.
        /// </summary>
        public static IHorseFeedStock ResolveOwnerScopedHorseFeedStock(
            AnimalState horse,
            Func<AnimalOwnerKind, string, IHorseFeedStock> registeredSourceForOwner)
        {
            if (horse == null || !horse.IsActive || string.IsNullOrWhiteSpace(horse.OwnerId)
                || registeredSourceForOwner == null)
                return null;
            return registeredSourceForOwner(horse.OwnerKind, horse.OwnerId);
        }

        private void DriveHouseholdAutonomy(int absoluteDayIndex, List<string> diag)
        {
            if (hub == null || populationManager?.State?.households == null || householdMemberships == null) return;
            // Pass V P2-2: the hardship asset-sale channel (household surplus
            // sold to real buyer households; no guaranteed buyer).
            var assetSaleService = new HouseholdAssetSaleService(
                hub.HouseholdLedgers,
                hub.HouseholdInventories,
                hub.PurchasingNeeds,
                householdId => populationManager.State.households.Exists(h => h != null && h.id == householdId),
                () =>
                {
                    var ids = new List<int>();
                    foreach (HouseholdState h in populationManager.State.households)
                        if (h != null) ids.Add(h.id);
                    return ids;
                },
                itemId =>
                {
                    int best = 0;
                    if (supplierDirectory != null)
                    {
                        foreach (IGoodsSupplier supplier in supplierDirectory.All)
                        {
                            var resolving = supplier as IItemResolvingSupplier;
                            if (resolving == null || !resolving.OffersItem(itemId)) continue;
                            int price = resolving.ItemPricePerUnitCents(itemId);
                            if (price > 0 && (best == 0 || price < best)) best = price;
                        }
                    }

                    return best;
                });
            var assetSalePort = new HouseholdToHouseholdAssetSalePort(assetSaleService);
            foreach (HouseholdState household in populationManager.State.households)
            {
                if (household == null) continue;
                hub.HouseholdAutonomy.EvaluateHouseholdDay(household.id, absoluteDayIndex,
                    new HouseholdAutonomyDriver.AutonomyContext
                    {
                        Ledgers = hub.HouseholdLedgers,
                        Memberships = householdMemberships,
                        Employments = hub.Employments,
                        ContactGraph = hub.ContactGraph,
                        Decisions = hub.HouseholdDecisions,
                        DecisionDriver = hub.HouseholdDecisionDriver,
                        HousingSearchNeeds = hub.HousingSearchNeeds,
                        FinancialObligations = hub.FinancialObligations,
                        Ids = hub.Ids,
                        Lenders = hub.HouseholdLenders,
                        PersonNameOf = personId =>
                        {
                            PersonState person = populationManager.State.GetPerson(personId);
                            return person != null
                                ? (person.firstName + " " + person.lastName).Trim()
                                : string.Empty;
                        },
                        AssetSale = assetSalePort,
                        SaleableSurplusValueFor = householdId =>
                            assetSaleService.EvaluateSaleableValue(householdId, null),
                    }, diag);
            }
            // Pass VIII Worker 7: close the decision→work loop. SeekIncome
            // discoveries land in the opportunity log; this turns unpursued
            // entries into real person-initiated applications wherever a
            // hiring effort is genuinely open. No open effort, no
            // application — the entry waits.
            DriveOpportunityPursuit(absoluteDayIndex, diag);
        }

        /// <summary>
        /// Pass VIII Worker 7: daily opportunity pursuit for every household.
        /// </summary>
        private void DriveOpportunityPursuit(int absoluteDayIndex, List<string> diag)
        {
            if (hub == null || populationManager?.State?.households == null || householdMemberships == null) return;
            var pursuit = new OpportunityPursuitService();
            var ctx = new OpportunityPursuitService.PursuitContext
            {
                OpportunityLog = hub.HouseholdDecisionDriver.OpportunityLog,
                Memberships = householdMemberships,
                Population = populationManager.State,
                Employments = hub.Employments,
                Recruitment = hub.Recruiting,
            };
            foreach (HouseholdState household in populationManager.State.households)
            {
                if (household == null) continue;
                int filed = pursuit.PursueForHousehold(household.id, absoluteDayIndex, ctx, diag);
                if (filed > 0)
                {
                    Debug.Log($"[SimulationDrivers] opportunity pursuit day {absoluteDayIndex}: " +
                        $"H{household.id} filed {filed} job application(s).");
                }
            }
        }

        /// <summary>
        /// Autonomous Labor: daily labor-market drives. Offer expiries and
        /// start-day activations advance here. Per-business manager and work-
        /// scheduler cycles are assembled by game wiring (see the Codex
        /// handoff) because they need the live business registry alongside
        /// the hub services.
        /// </summary>
        private void DriveLaborMarketDay(int absoluteDayIndex)
        {
            if (hub == null) return;
            var diag = new List<string>();
            hub.EmploymentNegotiation.AdvanceDay(absoluteDayIndex, hub.Employments, diag);
            if (diag.Count > 0)
            {
                Debug.LogWarning("[SimulationDrivers] labor-market day " + absoluteDayIndex + ": " +
                    string.Join(" | ", diag));
            }
        }

        /// <summary>
        /// Pass VII Workstream D: wires the manager decision execution service
        /// to the hub's real production authorities. The recruitment callback
        /// is the existing ExecuteManagerRecruitment path, preserved verbatim.
        /// Pass VIII (6.3): the production business lender (town bank) is
        /// arranged here — manager Borrow decisions within delegated limits
        /// reach a real funded lender through IManagerBusinessLender instead
        /// of failing honestly forever. The brain still escalates borrowing
        /// it flags for owner approval.
        /// </summary>
        private void ConfigureManagerExecution()
        {
            if (managerExecutionConfigured || hub == null || sharedBusinessRuntime == null)
            {
                return;
            }

            hub.BusinessLender.BindBusinessLookup(id =>
            {
                BusinessInstanceState found;
                return sharedBusinessRuntime.TryFindByInstanceId(id, out found) ? found : null;
            });
            var lenderDiag = new List<string>();
            string charterProblem = hub.BusinessLender.EnsureCharteredCapital(
                BusinessLenderService.DefaultCharterCapitalCents,
                "town bank charter capital (calibration — see BusinessLenderService)",
                timeManager != null ? timeManager.CurrentAbsoluteDayIndex : 0,
                lenderDiag);
            if (charterProblem != null)
            {
                Debug.LogWarning("[SimulationDrivers] business lender charter: " + charterProblem);
            }
            // Pass VIII worker 8: the lender's social-knowledge intake
            // (BusinessLenderService.BindSocialKnowledge) is source-ready but
            // intentionally unwired here. It requires a designated lending
            // officer — a real PersonState on the town bank's staff — and no
            // such person exists in production yet. Binding the store without
            // an officer would consume no packets; inventing an officer would
            // be a fake person. When bank staffing lands, call
            // hub.BusinessLender.BindSocialKnowledge(hub.KnowledgeTransmissions,
            // officerPersonId) here. Until then underwriting never sees
            // social evidence, and all existing behavior is unchanged.

            hub.ManagerDecisionExecution.Configure(new ManagerExecutionServices
            {
                FindBusiness = id =>
                {
                    BusinessInstanceState found;
                    return sharedBusinessRuntime.TryFindByInstanceId(id, out found) ? found : null;
                },
                AllBusinesses = () => sharedBusinessRuntime.Businesses,
                DemandFor = id =>
                {
                    LaborDemandReport report;
                    return latestDemandByBusiness.TryGetValue(id, out report) ? report : null;
                },
                BusinessEntityFor = id => EntityId.For(EntityKind.Business, StableBusinessRuntimeId(id)),
                Tasks = hub.Tasks,
                WorkScheduling = hub.WorkScheduling,
                PersonSchedules = hub.PersonSchedules,
                WorkTimeBudgets = hub.WorkTimeBudgets,
                Employments = hub.Employments,
                FinancialObligations = hub.FinancialObligations,
                Ids = hub.Ids,
                Recruiting = hub.Recruiting,
                SupplierKnowledge = sellerKnowledge,
                BusinessLender = hub.BusinessLender,
                // Pass VIII worker 8: the hub-owned knowledge transmission
                // store feeds the Procure executor's supplier choice. The
                // executor consults only what the manager person actually
                // heard; an unheard rumor changes nothing.
                SocialKnowledge = hub.KnowledgeTransmissions,
                OpenRecruitment = (business, demand, decision, day, diag) =>
                    ExecuteManagerRecruitment(business, demand, decision, day, diag),
            });
            managerExecutionConfigured = true;
        }

        /// <summary>
        /// Runs the labor services against the real business task queues. The
        /// labor DTOs are read models and decision inputs; TaskAuthority and
        /// PersonScheduleTracker remain the only writers for work assignment,
        /// Person time, and task state.
        /// </summary>
        private void DriveAutonomousLabor(int absoluteDayIndex)
        {
            if (hub == null || sharedBusinessRuntime == null)
            {
                return;
            }

            ConfigureManagerExecution();
            // Pass VIII Worker 7: the dead are never offered work. The seam
            // sits in the task authority so every assignment path is covered.
            EnsureWorkerAvailabilitySeam();
            var diagnostics = new List<string>();
            hub.ManagerDecisionExecution.AdvanceShipments(absoluteDayIndex, diagnostics);
            // Pass VIII (6.3): service the production business loans — real
            // interest accrual, scheduled collection from borrowers' tills,
            // and the documented default consequences.
            hub.BusinessLender.ProcessDay(absoluteDayIndex, diagnostics);
            foreach (BusinessInstanceState business in sharedBusinessRuntime.Businesses)
            {
                if (business == null || string.IsNullOrWhiteSpace(business.InstanceId))
                {
                    continue;
                }

                EntityId businessId = EntityId.For(EntityKind.Business, StableBusinessRuntimeId(business.InstanceId));
                List<WorkTask> openTasks = hub.Tasks.GetOpenTasks(businessId);
                if (openTasks == null || openTasks.Count == 0)
                {
                    continue;
                }

                var demandInputs = new LaborDemandInputs
                {
                    BusinessInstanceId = business.InstanceId,
                    DayIndex = absoluteDayIndex,
                };
                var workers = new List<SchedulerAvailableWorker>();
                var seenWorkers = new HashSet<int>();
                EmploymentRelationshipRegistry registry = business.RuntimeState?.EmploymentRegistry ?? hub.Employments;

                if (registry != null)
                {
                    foreach (EmploymentRelationship relationship in registry.GetActiveByEmployer(business.InstanceId))
                    {
                        AddLaborWorker(relationship?.EmployeePersonId ?? -1, seenWorkers, workers, absoluteDayIndex);
                    }
                }

                if (business.Owner != null && business.Owner.PersonId >= 0)
                {
                    AddLaborWorker(business.Owner.PersonId, seenWorkers, workers, absoluteDayIndex);
                }

                var schedulerWork = new List<SchedulerOpenWorkItem>();
                var taskByTextId = new Dictionary<string, WorkTask>(StringComparer.Ordinal);
                foreach (WorkTask task in openTasks)
                {
                    if (task == null || !task.IsOpen)
                    {
                        continue;
                    }

                    TaskDefinition definition = hub.Tasks.GetDefinition(task.DefinitionId);
                    string family = definition?.DisplayName;
                    if (string.IsNullOrWhiteSpace(family)) family = task.DefinitionId;
                    int minutes = Math.Max(1, task.MinutesRemaining);
                    if (task.Status == TaskStatus.Queued)
                    {
                        schedulerWork.Add(new SchedulerOpenWorkItem
                        {
                            TaskId = task.TaskId.ToString(),
                            Family = family,
                            MinutesRequired = minutes,
                            RequiredEquipment = definition != null && definition.EquipmentClasses.Count > 0
                                ? definition.EquipmentClasses[0] : string.Empty,
                            LocationId = task.LocationId,
                        });
                        taskByTextId[task.TaskId.ToString()] = task;
                    }

                    if (!demandInputs.OpenTaskMinutesByFamily.ContainsKey(family))
                    {
                        demandInputs.OpenTaskMinutesByFamily[family] = 0;
                    }
                    demandInputs.OpenTaskMinutesByFamily[family] += minutes;
                }

                demandInputs.ActiveEmployeeCount = workers.Count;
                demandInputs.AvailableEmployeeMinutes = workers.Sum(worker => worker.AvailableMinutes);
                LaborDemandReport demand = hub.LaborDemand.Assess(demandInputs, diagnostics);

                // A delegated manager is a real Person with a persisted grant.
                // No manager is synthesized for an owner or for an unconfigured
                // business; absent authority remains a visible non-operation.
                ManagerDecisionResult managerDecision = null;
                foreach (SchedulerAvailableWorker worker in workers)
                {
                    DelegatedAuthority authority = hub.Delegation.GetActiveAuthority(
                        business.InstanceId, worker.PersonId, absoluteDayIndex);
                    if (authority == null)
                    {
                        continue;
                    }

                    // Pass IX WS5 (A3-07): the cycle inputs are BUILT from
                    // live business state — shortage from runtime stock vs
                    // reorder target, maintenance from equipment condition,
                    // pricing from the market-capture/price-index readout,
                    // borrowing from the cash-vs-reserve gap with the
                    // delegation's real MaxBorrowingCents (from the live
                    // standing instructions, not the old hard-coded 0), and
                    // asset-sale proposals from lifecycle distress naming
                    // the specific register asset. The first day of each
                    // week runs the WeeklyCycle (previously no production
                    // caller); other days run the DailyCycle.
                    AssetSaleAppraisalService appraisal = BuildProductionAppraisalService();
                    var inputsBuilder = new ManagerCycleInputsBuilder(appraisal);
                    bool hasMarketReadout = sharedBusinessRuntime.TryGetBusinessMarketReadout(
                        business, out float marketCapture01, out float priceIndex01);
                    ManagerCycleInputs managerInputs = inputsBuilder.Build(new ManagerCycleLiveEvidence
                    {
                        Business = business,
                        DemandReport = demand,
                        Authority = authority,
                        Instructions = hub.Managers.Instructions.ForBusiness(business.InstanceId),
                        ManagerPersonId = worker.PersonId,
                        DayIndex = absoluteDayIndex,
                        SurvivalReserveCents = SharedBusinessRuntimeManager.CalculateSharedSurvivalCashReserveCents(business),
                        EquipmentAssets = hub.TransportAssets.Assets,
                        FormationAssets = hub.NpcFormationAssets.Assets,
                        HasMarketReadout = hasMarketReadout,
                        MarketCapture01 = marketCapture01,
                        PriceIndex01 = priceIndex01,
                        JudgmentSeed = absoluteDayIndex ^ StableBusinessRuntimeId(business.InstanceId),
                        JudgmentQuality01 = 0.8f,
                    }, diagnostics);
                    bool isWeeklyTick = WeeklyAttendanceAccumulator.WeekIndexForDay(absoluteDayIndex)
                        != WeeklyAttendanceAccumulator.WeekIndexForDay(absoluteDayIndex - 1);
                    managerDecision = isWeeklyTick
                        ? hub.Managers.WeeklyCycle(managerInputs, diagnostics)
                        : hub.Managers.DailyCycle(managerInputs, diagnostics);
                    // Pass VII Workstream D: every decision kind executes
                    // through a real production authority (or becomes a
                    // legitimate pending request, or fails with a reason).
                    // The OpenRecruitment executor calls back into the
                    // existing ExecuteManagerRecruitment path, preserved.
                    latestDemandByBusiness[business.InstanceId] = demand;
                    ManagerExecutionOutcome executionOutcome = hub.ManagerDecisionExecution.ExecuteDecision(
                        business, authority, worker.PersonId, demand, managerDecision,
                        absoluteDayIndex, diagnostics);
                    if (executionOutcome != null && !string.IsNullOrWhiteSpace(executionOutcome.Reason))
                    {
                        diagnostics.Add($"manager execution for {business.InstanceId}: {executionOutcome.Reason}.");
                    }
                    if (managerDecision != null && managerDecision.Acted &&
                        string.Equals(managerDecision.ActionKind, ManagerActKinds.Schedule, StringComparison.Ordinal))
                    {
                        // The manager's Schedule execution starts tasks through
                        // the real scheduler; rebuild from fresh task state so
                        // the blanket pass below never double-schedules work
                        // the manager already placed.
                        openTasks = hub.Tasks.GetOpenTasks(businessId);
                        schedulerWork.Clear();
                        taskByTextId.Clear();
                        foreach (WorkTask task in openTasks)
                        {
                            if (task == null || !task.IsOpen || task.Status != TaskStatus.Queued)
                            {
                                continue;
                            }

                            TaskDefinition refreshedDefinition = hub.Tasks.GetDefinition(task.DefinitionId);
                            string refreshedFamily = refreshedDefinition?.DisplayName;
                            if (string.IsNullOrWhiteSpace(refreshedFamily)) refreshedFamily = task.DefinitionId;
                            schedulerWork.Add(new SchedulerOpenWorkItem
                            {
                                TaskId = task.TaskId.ToString(),
                                Family = refreshedFamily,
                                MinutesRequired = Math.Max(1, task.MinutesRemaining),
                                RequiredEquipment = refreshedDefinition != null && refreshedDefinition.EquipmentClasses.Count > 0
                                    ? refreshedDefinition.EquipmentClasses[0] : string.Empty,
                                LocationId = task.LocationId,
                            });
                            taskByTextId[task.TaskId.ToString()] = task;
                        }
                    }
                    break;
                }

                if (schedulerWork.Count == 0 || workers.Count == 0)
                {
                    continue;
                }

                WorkAssignmentReport assignments = hub.WorkScheduling.AssignDailyWork(
                    new SchedulerInputs
                    {
                        BusinessInstanceId = business.InstanceId,
                        DayIndex = absoluteDayIndex,
                        OpenWork = schedulerWork,
                        Workers = workers,
                    }, diagnostics);
                StartAssignedBusinessTasks(assignments, taskByTextId, absoluteDayIndex, diagnostics);
            }

            // G-06: NPC-owner auto-resolution for escalated manager requests —
            // borrow routed to lender underwriting, asset sales per the
            // distress-sale policy. Player-owned requests stay pending.
            DriveNpcOwnerRequestResolution(absoluteDayIndex, diagnostics);

            if (diagnostics.Count > 0)
            {
                Debug.LogWarning("[SimulationDrivers] autonomous labor: " + string.Join(" | ", diagnostics));
            }
        }

        /// <summary>
        /// G-06: NPC-owner auto-resolution for manager pending requests.
        /// Runs after the day's manager decisions execute: escalated Borrow
        /// requests go to BusinessLenderService underwriting instead of
        /// stalling, and SellAsset requests resolve per the distress-sale
        /// policy (approve only when cash is below the survival reserve and
        /// the price meets the appraisal). Player-owned businesses keep
        /// their requests pending for the future UI. Pass IX WS5: the
        /// appraisal is the production register appraisal (named assets
        /// only — an unnamed or unevidenced sale still denies rather than
        /// approving blind), and approved sales execute in the same drive.
        /// </summary>
        private void DriveNpcOwnerRequestResolution(int absoluteDayIndex, List<string> diagnostics)
        {
            if (hub == null || sharedBusinessRuntime == null) return;
            if (hub.ManagerDecisionExecution == null || !hub.ManagerDecisionExecution.IsConfigured) return;

            var resolver = new NpcOwnerRequestResolver();
            var diag = new List<string>();
            // Pass IX WS5 (PASS8-003 + A3-08): the production appraisal
            // source. SellAsset requests now name the specific asset (from
            // the business's own registers at proposal time); the appraisal
            // reads those registers and returns -1 (no approval) when they
            // hold no evidence for the named asset.
            AssetSaleAppraisalService appraisal = BuildProductionAppraisalService();
            resolver.ResolvePending(
                new NpcOwnerRequestResolver.OwnerResolutionContext
                {
                    Ledger = hub.ManagerDecisionExecution.Ledger,
                    FindBusiness = id =>
                    {
                        BusinessInstanceState found;
                        return sharedBusinessRuntime.TryFindByInstanceId(id, out found) ? found : null;
                    },
                    SurvivalReserveCents = business =>
                        SharedBusinessRuntimeManager.CalculateSharedSurvivalCashReserveCents(business),
                    AppraiseSaleCents = appraisal.AppraiseSaleCents,
                    BusinessLender = hub.BusinessLender,
                    Obligations = hub.FinancialObligations,
                    Ids = hub.Ids,
                    DayIndex = absoluteDayIndex,
                }, diag);

            // Pass IX WS5 (A3-08): the execution step behind an approval —
            // the named asset transfers to a real buyer business and both
            // cash legs post exactly once. Player-owned requests never
            // reach Approved here (the resolver leaves them Pending), so
            // nothing executes for the player without the future UI.
            var saleExecution = new AssetSaleExecutionService();
            saleExecution.ExecuteApprovedSales(
                hub.ManagerDecisionExecution.Ledger,
                new AssetSaleExecutionContext
                {
                    FindBusiness = id =>
                    {
                        BusinessInstanceState found;
                        return sharedBusinessRuntime.TryFindByInstanceId(id, out found) ? found : null;
                    },
                    AllBusinesses = () => sharedBusinessRuntime.Businesses,
                    FindEquipmentAsset = id => hub.TransportAssets.Get(id),
                    FormationAssets = hub.NpcFormationAssets,
                    DayIndex = absoluteDayIndex,
                }, diag);
            diagnostics.AddRange(diag);
        }

        /// <summary>
        /// Pass VIII Worker 7: installs the worker-availability seam on the
        /// hub task authority (idempotent). A dead person is never offered
        /// work at assignment or start, no matter which caller forgot to check.
        /// </summary>
        private void EnsureWorkerAvailabilitySeam()
        {
            if (hub == null || hub.Tasks == null) return;
            hub.Tasks.IsWorkerUnavailable = worker =>
            {
                if (worker.Kind != LandLedgers.Primitives.EntityKind.Person) return false;
                if (populationManager?.State == null) return false;
                PersonState person = populationManager.State.GetPerson(worker.Id);
                return person != null && person.deathDayIndex >= 0;
            };
        }

        private void AddLaborWorker(int personId, HashSet<int> seen, List<SchedulerAvailableWorker> workers, int dayIndex)
        {
            if (personId < 0 || !seen.Add(personId)) return;
            // Pass VIII Worker 7: the dead are never scheduled — not through
            // employment records (ended at death) and not through the owner
            // path below.
            if (populationManager?.State != null)
            {
                PersonState person = populationManager.State.GetPerson(personId);
                if (person != null && person.deathDayIndex >= 0) return;
            }
            EntityId personEntity = EntityId.For(EntityKind.Person, personId);
            WorkTimeBudget budget = hub.WorkTimeBudgets.GetOrCreate(personEntity);
            int availableMinutes = budget.MinutesRemaining;
            // Pass VII Workstream B (P2-10): the draft-driver hold is a real
            // person-time reservation in the unified authority. A person
            // driving a draft team is not available for scheduled work —
            // worker time is single-bookable, and the hold is visible to the
            // WorkScheduler as zero remaining minutes (no second scheduler).
            if (hub != null && hub.DraftPower != null && hub.DraftPower.IsDriverReserved(personEntity))
                availableMinutes = 0;
            workers.Add(new SchedulerAvailableWorker
            {
                PersonId = personId,
                AvailableMinutes = availableMinutes,
                SkillEvidence = new Dictionary<string, float>(),
            });
        }

        private void StartAssignedBusinessTasks(WorkAssignmentReport report, Dictionary<string, WorkTask> tasks, int dayIndex, List<string> diagnostics)
        {
            // Pass VII Workstream D: one shared implementation with the manager
            // Schedule executor (ManagerWorkSchedulerDrive) — no drift.
            ManagerWorkSchedulerDrive.StartAssignedTasks(
                report, tasks, hub.Tasks, hub.PersonSchedules, hub.WorkTimeBudgets,
                dayIndex, diagnostics, TaskReservationLabel,
                (taskId, reservationId) => autonomousTaskReservations[taskId] = reservationId);
        }

        private static List<string> ScopeNames(DelegationScope scopes)
        {
            var names = new List<string>();
            if (scopes.HasFlag(DelegationScope.Purchasing)) names.Add(ManagerScopeNames.Purchasing);
            if (scopes.HasFlag(DelegationScope.Scheduling)) names.Add(ManagerScopeNames.Scheduling);
            if (scopes.HasFlag(DelegationScope.Maintenance)) names.Add(ManagerScopeNames.Maintenance);
            if (scopes.HasFlag(DelegationScope.CashHandling)) names.Add(ManagerScopeNames.CashHandling);
            if (scopes.HasFlag(DelegationScope.Inventory)) names.Add(ManagerScopeNames.Inventory);
            if (scopes.HasFlag(DelegationScope.Staffing)) names.Add(ManagerScopeNames.Staffing);
            return names;
        }

        private static string TaskReservationLabel(WorkTask task)
        {
            return task == null
                ? string.Empty
                : $"task:{task.TaskId}|{task.DefinitionId}";
        }

        private string ExecuteManagerRecruitment(
            BusinessInstanceState business,
            LaborDemandReport demand,
            ManagerDecisionResult decision,
            int dayIndex,
            List<string> diagnostics)
        {
            if (business == null || demand == null || decision == null ||
                !decision.Acted || !string.Equals(decision.ActionKind, ManagerActKinds.OpenRecruitment, StringComparison.Ordinal))
            {
                return null;
            }

            string role = "general labor";
            if (demand.Lines != null)
            {
                for (int i = 0; i < demand.Lines.Count; i++)
                {
                    LaborDemandLine line = demand.Lines[i];
                    if (line != null && !string.IsNullOrWhiteSpace(line.TaskFamily) && line.OpenMinutes > 0)
                    {
                        role = line.TaskFamily;
                        break;
                    }
                }
            }

            RecruitmentEffort effort = hub.Recruiting.OpenEffort(
                business.InstanceId,
                role,
                RecruitmentChannel.WordOfMouth,
                dayIndex,
                dayIndex ^ StableBusinessRuntimeId(business.InstanceId),
                diagnostics);
            if (effort == null)
            {
                diagnostics.Add($"manager recruitment for {business.InstanceId}: RecruitmentService refused the effort");
                return null;
            }

            // The activity record is an immutable historical read model from
            // ManagerBrain; the real effort id is still surfaced in runtime
            // diagnostics and remains save-persisted by RecruitmentService.
            diagnostics.Add($"manager recruitment for {business.InstanceId}: opened {effort.EffortId} for {role}");
            return effort.EffortId;
        }

        /// <summary>
        /// Pass IV (Worker 1): later workers (production, household) register handlers
        /// for their due-event kinds here. The driver owns TaskCompletionDue,
        /// PassiveProcessComplete and ResourceReleaseDue; unhandled kinds are logged
        /// loudly — never silently skipped.
        /// </summary>
        public void RegisterDueEventHandler(DueEventKind kind, Action<DueEvent> handler)
        {
            if (handler == null)
            {
                dueEventHandlers.Remove(kind);
            }
            else
            {
                dueEventHandlers[kind] = handler;
            }
        }

        private readonly Dictionary<DueEventKind, Action<DueEvent>> dueEventHandlers =
            new Dictionary<DueEventKind, Action<DueEvent>>();

        /// <summary>
        /// Pass IV (Worker 1): pops all due events up to the current clock time in due
        /// order and dispatches them before any further advancement.
        /// </summary>
        private void ProcessDueEvents()
        {
            if (hub == null || timeManager == null)
            {
                return;
            }

            List<DueEvent> due = hub.DueEvents.PopDue(timeManager.AbsoluteGameSeconds);
            if (due.Count == 0)
            {
                return;
            }

            var diag = new List<string>();
            foreach (DueEvent dueEvent in due)
            {
                // ShortTick is emitted immediately before the day rollover.
                // A due event exactly at midnight can therefore be dispatched
                // while TimeManager still exposes yesterday's calendar date.
                // Use the event's own absolute timestamp so date-sensitive
                // handlers (birthdays, deadlines) advance from the date the
                // event actually belongs to.
                float secondsPerDay = timeManager.Settings != null
                    ? Mathf.Max(1f, timeManager.Settings.GameSecondsPerDay)
                    : 86400f;
                int dueDayIndex = dueEvent != null && dueEvent.DueGameSeconds >= 0f
                    ? Mathf.FloorToInt(dueEvent.DueGameSeconds / secondsPerDay)
                    : timeManager.CurrentAbsoluteDayIndex;
                DispatchDueEvent(dueEvent, dueDayIndex, diag);
            }

            if (diag.Count > 0)
            {
                Debug.LogWarning("[SimulationDrivers] due events: " + string.Join(" | ", diag));
            }
        }

        private void DispatchDueEvent(DueEvent dueEvent, int dayIndex, List<string> diag)
        {
            Action<DueEvent> handler;
            if (dueEventHandlers.TryGetValue(dueEvent.Kind, out handler) && handler != null)
            {
                handler(dueEvent);
                return;
            }

            switch (dueEvent.Kind)
            {
                case DueEventKind.TaskCompletionDue:
                    // Pass IX (A2-04): domestic completions route to the
                    // domestic driver (family-labor credit through the
                    // service), not the generic completion path.
                    if (hub.DomesticLaborDriver.HasInFlightTask(dueEvent.PayloadRef))
                    {
                        hub.DomesticLaborDriver.HandleDueEvent(
                            dueEvent, hub.Tasks, hub.WorkTimeBudgets, hub.PersonSchedules,
                            hub.DomesticLabor, domesticFamilyLabor, hub.DueEvents, dayIndex, diag);
                    }
                    else
                    {
                        CompleteTaskAtDueEvent(dueEvent, dayIndex, diag);
                    }
                    break;
                case DueEventKind.PassiveProcessComplete:
                    ResumeTaskAfterPassiveProcess(dueEvent, diag);
                    break;
                case DueEventKind.ResourceReleaseDue:
                    ReleaseReservationAtDueEvent(dueEvent, diag);
                    break;
                case DueEventKind.Birthday:
                    // Pass V P0-2: birthday due-events re-derive the person's
                    // age from the birth-date authority (never increment) and
                    // reschedule next year's event.
                    HandleBirthdayDueEvent(dueEvent, dayIndex, diag);
                    break;
                default:
                    diag.Add("due event " + dueEvent.EventId + " (" + dueEvent.Kind +
                        ", payload '" + dueEvent.PayloadRef + "') has no registered handler — consumed without effect");
                    break;
            }
        }

        /// <summary>
        /// Pass V P0-2: handles a fired birthday due-event through the
        /// birth-date authority (age is re-derived, never incremented) and
        /// reschedules next year's event on the hub scheduler.
        /// </summary>
        private void HandleBirthdayDueEvent(DueEvent dueEvent, int dayIndex, List<string> diag)
        {
            if (hub == null || populationManager == null || populationManager.State == null || timeManager == null)
            {
                diag.Add("birthday event " + dueEvent.EventId + " skipped: population or clock unavailable");
                return;
            }

            CalendarAnchor anchor = timeManager.Settings != null ? timeManager.TimelineAnchor : CalendarAnchor.Default;
            float secondsPerDay = timeManager.Settings != null ? timeManager.Settings.GameSecondsPerDay : 86400f;
            BirthdayService.HandleBirthdayDueEvent(
                dueEvent, populationManager.State, hub.DueEvents, anchor, dayIndex, secondsPerDay, diag);
        }

        /// <summary>
        /// Pass IV (Worker 1): every InProgress task with an assignee carries a
        /// TaskCompletionDue backstop (idempotent reschedule by stable id), so
        /// accelerated ticks cannot skip completions for tasks outside the per-tick
        /// drivers. Completion cancels the event; the per-tick path usually wins.
        /// </summary>
        private void SyncTaskCompletionDueEvents()
        {
            if (hub == null || timeManager == null)
            {
                return;
            }

            float now = timeManager.AbsoluteGameSeconds;
            foreach (WorkTask task in hub.Tasks.GetAllOpenTasks())
            {
                // DomesticLaborDriver already scheduled a fixed elapsed-time
                // completion event. Replacing it here on every short tick
                // continually moves the due time forward and prevents the
                // household work from ever receiving credit.
                if (!hub.DomesticLaborDriver.RequiresGenericCompletionBackstop(task))
                {
                    continue;
                }

                if (IsArrivalDrivenTravelTask(task)) continue;

                string eventId;
                hub.DueEvents.Schedule(
                    DueEventKind.TaskCompletionDue,
                    now + Math.Max(0, task.MinutesRemaining) * DurationConventions.SecondsPerMinute,
                    task.TaskId.ToString(),
                    out eventId);
            }
        }

        private void CompleteTaskAtDueEvent(DueEvent dueEvent, int dayIndex, List<string> diag)
        {
            WorkTask task = hub.Tasks.FindTaskByIdString(dueEvent.PayloadRef);
            if (task == null)
            {
                diag.Add("TaskCompletionDue for unknown task '" + dueEvent.PayloadRef + "' — ignored");
                return;
            }

            if (task.Status != TaskStatus.InProgress)
            {
                return; // already handled; completion cancels its events
            }

            if (IsArrivalDrivenTravelTask(task))
            {
                // Physical travel has no duration-based completion. The custom
                // pathing actor's arrival check owns this transition.
                return;
            }

            // Top up any unrecorded minutes so budget accounting stays truthful,
            // then resolve the outcome exactly like the per-tick driver.
            if (task.MinutesRemaining > 0)
            {
                string progressReason;
                bool reached;
                hub.Tasks.RecordWorkProgress(task.TaskId, task.MinutesRemaining, hub.WorkTimeBudgets,
                    dayIndex, out reached, out progressReason);
            }

            CompleteTaskWithCausalOutcome(task, dayIndex, 1);
            if (task.IsOpen)
            {
                diag.Add("TaskCompletionDue could not complete task " + task.TaskId);
            }
            else
            {
                ReleaseAutonomousTaskReservation(task, dayIndex);
                HandleCompletedBusinessFeedTask(task, dayIndex);
                HandleCompletedOpeningHitchTask(task, dayIndex);
                HandleCompletedFreightMeetingTask(task, dayIndex);
            }
        }

        private void ResumeTaskAfterPassiveProcess(DueEvent dueEvent, List<string> diag)
        {
            WorkTask task = hub.Tasks.FindTaskByIdString(dueEvent.PayloadRef);
            if (task == null)
            {
                diag.Add("PassiveProcessComplete for unknown task '" + dueEvent.PayloadRef + "' — ignored");
                return;
            }

            if (task.Status != TaskStatus.WaitingOnPassiveProcess)
            {
                return;
            }

            string reason;
            if (!hub.Tasks.ResumeTask(task.TaskId, "passive process complete", out reason))
            {
                diag.Add("PassiveProcessComplete could not resume task " + task.TaskId + ": " + reason);
            }
        }

        private void ReleaseReservationAtDueEvent(DueEvent dueEvent, List<string> diag)
        {
            var authority = hub.Tasks.ReservationAuthority;
            if (authority == null)
            {
                diag.Add("ResourceReleaseDue " + dueEvent.EventId +
                    ": no reservation authority installed — reservation '" + dueEvent.PayloadRef + "' NOT released");
                return;
            }

            authority.Release(dueEvent.PayloadRef);
        }

        /// <summary>
        /// Pass IV (Worker 1): resolves a finished task's outcome through the causal
        /// resolver and completes it — the single completion path shared by the
        /// per-tick driver, the due-event drain, and player work.
        /// </summary>
        private void CompleteTaskWithCausalOutcome(WorkTask task, int dayIndex, int baseMinutesFallback)
        {
            hub.Tasks.CompleteTask(task.TaskId, ResolveCausalOutcome(task, dayIndex, baseMinutesFallback), dayIndex, hub.WorkTimeBudgets);
        }

        private TaskOutcome ResolveCausalOutcome(WorkTask task, int dayIndex, int baseMinutesFallback = 1)
        {
            TaskDefinition definition = hub.Tasks.GetDefinition(task.DefinitionId);
            string skillId = definition != null ? definition.RequiredSkillId : string.Empty;
            CausalOutcomeInputs inputs = hub.Skills.BuildCausalInputs(
                task.AssigneeId, skillId, definition != null ? definition.BaseMinutes : baseMinutesFallback);
            return hub.Skills.ResolveTaskCompletion(
                task.AssigneeId, skillId, inputs,
                difficulty01: 0.5, novelty01: 0.25, supervised: false,
                day: dayIndex, taskIdForDeterminism: task.TaskId.ToString());
        }

        private PlayerTaskProgressDriver playerWorkDriver;

        /// <summary>
        /// Pass IV (Worker 1): advances player-issued tasks in real time (they
        /// previously stalled in InProgress with no per-tick driver).
        /// </summary>
        private void AdvancePlayerWork(int wholeMinutes, int dayIndex)
        {
            // Pass XL player-as-NPC playtest: when the flag is on, player-
            // issued tasks do not advance on the player path. Autonomous
            // advancement (AdvanceAutonomousWork) is untouched, and NPC
            // drivers still generate and drive their own work.
            if (treatPlayerAsNpc)
            {
                return;
            }

            if (hub == null || playerDirector == null || wholeMinutes <= 0)
            {
                return;
            }

            if (!playerDirector.PlayerPersonId.IsValid)
            {
                return;
            }

            playerWorkDriver ??= new PlayerTaskProgressDriver(hub.Tasks, hub.WorkTimeBudgets);
            var diag = new List<string>();
            playerWorkDriver.AdvancePlayerTasks(
                playerDirector.PlayerPersonId, wholeMinutes, dayIndex,
                (task, d) => ResolveCausalOutcome(task, d), diag);
            if (diag.Count > 0)
            {
                Debug.LogWarning("[SimulationDrivers] player work: " + string.Join(" | ", diag));
            }
        }

        private void AdvanceAutonomousWork(int wholeMinutes, int dayIndex, int minuteOfDay)
        {
            if (hub == null || wholeMinutes <= 0 || sharedBusinessRuntime == null) return;
            int tickStartMinute = Math.Max(0, minuteOfDay - wholeMinutes);
            foreach (BusinessInstanceState business in sharedBusinessRuntime.Businesses)
            {
                if (business == null) continue;
                EntityId owner = EntityId.For(EntityKind.Business, StableBusinessRuntimeId(business.InstanceId));
                foreach (WorkTask task in hub.Tasks.GetOpenTasks(owner))
                {
                    if (task == null || task.Status != TaskStatus.InProgress || !task.HasAssignee) continue;
                    if (IsArrivalDrivenTravelTask(task)) continue;
                    List<PersonActivityReservation> activeReservations =
                        hub.PersonSchedules.ActiveFor(task.AssigneeId.Id, dayIndex);
                    int scheduledMinutes = 0;
                    for (int i = 0; i < activeReservations.Count; i++)
                    {
                        PersonActivityReservation reservation = activeReservations[i];
                        if (reservation != null &&
                            (reservation.Activity == PersonActivityKind.Work
                                || reservation.Activity == PersonActivityKind.Shopping) &&
                            string.Equals(reservation.Label, TaskReservationLabel(task), StringComparison.Ordinal) &&
                            tickStartMinute < reservation.EndMinuteOfDay &&
                            reservation.StartMinuteOfDay < minuteOfDay)
                        {
                            int overlapStart = Math.Max(tickStartMinute, reservation.StartMinuteOfDay);
                            int overlapEnd = Math.Min(minuteOfDay, reservation.EndMinuteOfDay);
                            scheduledMinutes = Math.Max(scheduledMinutes, overlapEnd - overlapStart);
                            break;
                        }
                    }
                    if (scheduledMinutes <= 0) continue;
                    int workMinutes = scheduledMinutes;
                    if (workMinutes <= 0) continue;
                    if (hub.Tasks.RecordWorkProgress(task.TaskId, workMinutes, hub.WorkTimeBudgets,
                        dayIndex, out bool reachedCompletion, out _))
                    {
                        if (reachedCompletion)
                        {
                            CompleteTaskWithCausalOutcome(task, dayIndex, wholeMinutes);
                            if (task.Status == TaskStatus.Complete)
                            {
                                HandleCompletedBusinessFeedTask(task, dayIndex);
                                HandleCompletedOpeningHitchTask(task, dayIndex);
                                HandleCompletedFreightMeetingTask(task, dayIndex);
                            }
                        }
                        if (!task.IsOpen)
                        {
                            ReleaseAutonomousTaskReservation(task, dayIndex);
                        }
                    }
                }
            }
        }

        private bool IsArrivalDrivenTravelTask(WorkTask task)
        {
            return task != null && task.DefinitionId == JourneyTravel.TravelTaskId
                && (task.CustomerRef.StartsWith(BusinessFeedTaskPrefix, StringComparison.Ordinal)
                    || task.CustomerRef.StartsWith(DevelopmentHitchTaskPrefix + "travel:", StringComparison.Ordinal)
                    || task.CustomerRef.StartsWith(FreightMeetingTaskPrefix + "travel:", StringComparison.Ordinal));
        }

        private void ResolveArrivalDrivenTravelTasks(int dayIndex)
        {
            if (hub?.Tasks == null || populationManager?.State == null) return;
            LandLedgers.FirstLedger.PopulationPathingDirector presentation =
                FindAnyObjectByType<LandLedgers.FirstLedger.PopulationPathingDirector>();
            if (presentation == null) return;

            foreach (WorkTask task in hub.Tasks.GetAllOpenTasks())
            {
                if (task == null || task.Status != TaskStatus.InProgress || !IsArrivalDrivenTravelTask(task)
                    || !task.HasAssignee || !TryGetPresentationTravelTarget(task, out int targetBuildingId)) continue;

                if (!presentation.TryGetExplicitArrival(task.AssigneeId.Id, targetBuildingId,
                        out Vector3 position, out float distance, out bool arrived) || !arrived) continue;

                CompleteTaskWithCausalOutcome(task, dayIndex, 1);
                if (task.Status != TaskStatus.Complete) continue;
                ReleaseAutonomousTaskReservation(task, dayIndex);
                Debug.Log($"[BusinessTravel] Arrival confirmed: person=P{task.AssigneeId.Id}, task='{task.TaskId}', " +
                    $"building={targetBuildingId}, position={position}, distance={distance:0.00}m; task completed by navigation arrival.", this);
                HandleCompletedBusinessFeedTask(task, dayIndex);
                HandleCompletedOpeningHitchTask(task, dayIndex);
                HandleCompletedFreightMeetingTravelTask(task, dayIndex);
            }
        }

        private bool TryGetPresentationTravelTarget(WorkTask task, out int targetBuildingId)
        {
            targetBuildingId = -1;
            if (task == null) return false;
            if (task.CustomerRef.StartsWith(BusinessFeedTaskPrefix, StringComparison.Ordinal))
            {
                BusinessFeedTripTaskData trip = JsonUtility.FromJson<BusinessFeedTripTaskData>(task.TermsRef);
                if (trip == null) return false;
                targetBuildingId = trip.Stage == "outbound" ? trip.StoreBuildingId
                    : trip.Stage == "return" ? trip.HomeBuildingId : -1;
                return targetBuildingId >= 0;
            }
            if (task.CustomerRef.StartsWith(DevelopmentHitchTaskPrefix + "travel:", StringComparison.Ordinal))
            {
                DevelopmentHitchTaskData trip = JsonUtility.FromJson<DevelopmentHitchTaskData>(task.TermsRef);
                if (trip == null) return false;
                targetBuildingId = trip.StagingBuildingId;
                return targetBuildingId >= 0;
            }
            if (task.CustomerRef.StartsWith(FreightMeetingTaskPrefix + "travel:", StringComparison.Ordinal))
            {
                FreightMeetingTripTaskData trip = JsonUtility.FromJson<FreightMeetingTripTaskData>(task.TermsRef);
                if (trip == null) return false;
                targetBuildingId = trip.MeetingBuildingId;
                return targetBuildingId >= 0;
            }
            return false;
        }

        private bool IsPersonVisuallyAtBuilding(int personId, int buildingId)
        {
            LandLedgers.FirstLedger.PopulationPathingDirector presentation =
                FindAnyObjectByType<LandLedgers.FirstLedger.PopulationPathingDirector>();
            return presentation != null && presentation.IsAtExplicitBuilding(personId, buildingId);
        }

        private void ReleaseAutonomousTaskReservation(WorkTask task, int dayIndex)
        {
            if (task == null || hub == null) return;

            if (autonomousTaskReservations.TryGetValue(task.TaskId, out string trackedReservation))
            {
                hub.PersonSchedules.Release(trackedReservation);
                autonomousTaskReservations.Remove(task.TaskId);
                return;
            }

            // The dictionary is intentionally transient. After save/load the
            // TaskAuthority and PersonScheduleTracker are authoritative; find
            // the persisted reservation by its stable task label.
            List<PersonActivityReservation> active =
                hub.PersonSchedules.ActiveFor(task.AssigneeId.Id, dayIndex);
            for (int i = 0; i < active.Count; i++)
            {
                PersonActivityReservation reservation = active[i];
                if (reservation != null &&
                    string.Equals(reservation.Label, TaskReservationLabel(task), StringComparison.Ordinal))
                {
                    hub.PersonSchedules.Release(reservation.ReservationId);
                    return;
                }
            }
        }

        /// <summary>
        /// Autonomous Labor: weekly wage settlement per business. The
        /// settled-week marker makes a repeated boundary in the same week
        /// (or a load followed by a boundary) a no-op — wages are never
        /// posted twice.
        /// </summary>
        private void DriveLaborMarketWeek(int absoluteDayIndex)
        {
            if (hub == null || sharedBusinessRuntime == null) return;
            int weekIndex = absoluteDayIndex / 7;
            var diag = new List<string>();
            foreach (BusinessInstanceState business in sharedBusinessRuntime.Businesses)
            {
                if (business?.RuntimeState == null || business.BusinessType != BusinessType.Generic)
                {
                    // Specialized domains retain their existing weekly
                    // settlement writer in SharedBusinessRuntimeManager. The
                    // shared labor writer owns Generic Businesses only, so a
                    // business can never receive two payroll passes at one
                    // calendar boundary.
                    continue;
                }
                var registry = business.RuntimeState.EmploymentRegistry ?? hub.Employments;
                hub.WageSettlement.SettleWeeklyPayroll(
                    business.RuntimeState, registry, weekIndex, absoluteDayIndex, diag);
            }
            if (diag.Count > 0)
            {
                Debug.LogWarning("[SimulationDrivers] labor-market week " + weekIndex + ": " +
                    string.Join(" | ", diag));
            }
        }

        /// <summary>
        /// Executes open purchasing needs only after the real household loop
        /// has been explicitly bound to a scenario's membership and supplier
        /// authorities. DailyNeedsService is then lot-consumption only, so
        /// this is the one production purchase writer for that scenario.
        /// </summary>
        private void DriveEmbodiedShopping(int absoluteDayIndex, List<string> diagnostics)
        {
            if (householdShoppingLoop == null || populationManager?.State?.households == null || hub == null)
            {
                return;
            }

            foreach (HouseholdState household in populationManager.State.households)
            {
                if (household == null)
                {
                    continue;
                }

                // Pass V P2-1: batch the household's open needs into one
                // planned errand through the hub-owned coordinator (one trip
                // serving several needs, one shopper, one person-time window,
                // claims so no need is ever bought twice). Needs the trip
                // cannot serve stay open and unclaimed for a later day.
                var errandCtx = new HouseholdErrandCoordinator.CoordinatorContext
                {
                    Population = populationManager.State,
                    Memberships = householdMemberships,
                    Ledgers = hub.HouseholdLedgers,
                    Inventories = hub.HouseholdInventories,
                    Needs = hub.PurchasingNeeds,
                    Schedules = hub.PersonSchedules,
                    Tasks = hub.Tasks,
                    Budgets = hub.WorkTimeBudgets,
                    PersonEntityId = id => EntityId.For(EntityKind.Person, id),
                };
                List<HouseholdErrandPlan> plans = hub.HouseholdErrands.PlanDay(
                    household.id, absoluteDayIndex, errandCtx, diagnostics);
                foreach (HouseholdErrandPlan plan in plans)
                {
                    if (plan == null) continue;
                    HouseholdShoppingLoop.BatchShoppingResult batch = hub.HouseholdErrands.ExecutePlan(
                        plan,
                        absoluteDayIndex,
                        errandCtx,
                        householdShoppingLoop,
                        new ShoppingLoopOptions
                        {
                            DepartureMinuteOfDay = 8 * 60,
                            Diagnostics = diagnostics,
                            PersonLocationId = ResolvePersonJourneyLocation,
                            SetPersonLocation = SetPersonJourneyLocation
                        },
                        diagnostics);
                    if (batch != null && !batch.TripExecuted && !string.IsNullOrWhiteSpace(batch.FailureReason))
                    {
                        diagnostics.Add($"[SimulationDrivers] household errands H{household.id}: {batch.FailureReason}");
                    }
                }
            }
        }

        private void EnsureTownJourneyNetwork()
        {
            if (hub == null) return;
            townWorld ??= FindAnyObjectByType<TownWorldController>();
            if (townWorld == null || townWorld.Grid == null) return;

            IReadOnlyList<BusinessInstanceState> businesses = sharedBusinessRuntime != null
                ? sharedBusinessRuntime.Businesses
                : Array.Empty<BusinessInstanceState>();
            int signature = CalculateTownJourneyWorldSignature(townWorld, businesses);
            if (signature == lastTownJourneyWorldSignature) return;

            Vector3 townCenter = townWorld.Settings != null
                ? townWorld.Settings.worldCenter
                : townWorld.Grid.Origin + new Vector3(
                    townWorld.Grid.Width * townWorld.Grid.CellSizeMeters * 0.5f,
                    0f,
                    townWorld.Grid.Depth * townWorld.Grid.CellSizeMeters * 0.5f);
            TownJourneyNetworkBuilder.BuildResult result = TownJourneyNetworkBuilder.Synchronize(
                hub.Journeys, townWorld.Grid, townCenter, townWorld.Plots, townWorld.Buildings, businesses);
            if (result.RoadComponents <= 0) return;

            Journeys = hub.Journeys;
            lastTownJourneyWorldSignature = signature;
            if (result.RoutableBuildings <= 0)
                Debug.LogWarning("[SimulationDrivers] " + result.Diagnostic, this);
            else
                Debug.Log("[SimulationDrivers] " + result.Diagnostic, this);
        }

        private static int CalculateTownJourneyWorldSignature(
            TownWorldController world,
            IReadOnlyList<BusinessInstanceState> businesses)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + world.GenerationPassCount;
                hash = hash * 31 + world.Grid.Width;
                hash = hash * 31 + world.Grid.Depth;
                hash = hash * 31 + world.Grid.CellSizeMeters.GetHashCode();
                for (int z = 0; z < world.Grid.Depth; z++)
                {
                    for (int x = 0; x < world.Grid.Width; x++)
                    {
                        TownCell cell = world.Grid.GetCell(new GridCoord(x, z));
                        if (!cell.IsRoad) continue;
                        hash = hash * 31 + x;
                        hash = hash * 31 + z;
                        hash = hash * 31 + (int)cell.roadType;
                    }
                }

                IReadOnlyList<PlacedBuilding> buildings = world.Buildings;
                hash = hash * 31 + buildings.Count;
                for (int i = 0; i < buildings.Count; i++)
                {
                    PlacedBuilding building = buildings[i];
                    if (building == null) continue;
                    hash = hash * 31 + building.id;
                    if (building.TryGetRoadAccessCoord(out GridCoord access))
                    {
                        hash = hash * 31 + access.x;
                        hash = hash * 31 + access.z;
                    }
                }

                IReadOnlyList<TownPlot> plots = world.Plots;
                hash = hash * 31 + plots.Count;
                for (int i = 0; i < plots.Count; i++)
                {
                    TownPlot plot = plots[i];
                    if (plot == null) continue;
                    hash = hash * 31 + plot.id;
                    hash = hash * 31 + plot.buildingId;
                    hash = hash * 31 + plot.roadAccessCell.x;
                    hash = hash * 31 + plot.roadAccessCell.z;
                }

                hash = hash * 31 + (businesses != null ? businesses.Count : 0);
                if (businesses != null)
                {
                    for (int i = 0; i < businesses.Count; i++)
                    {
                        BusinessInstanceState business = businesses[i];
                        if (business == null) continue;
                        hash = hash * 31 + StringComparer.Ordinal.GetHashCode(business.InstanceId ?? string.Empty);
                        hash = hash * 31 + business.AssignedBuildingId;
                    }
                }
                return hash;
            }
        }

        private string ResolvePersonJourneyLocation(int personId)
        {
            PersonState person = populationManager?.State?.GetPerson(personId);
            if (person == null || hub == null) return string.Empty;
            int buildingId = person.scheduleState switch
            {
                PopulationScheduleState.AtHome => person.homeBuildingId,
                PopulationScheduleState.AtWork => person.workplaceBuildingId >= 0
                    ? person.workplaceBuildingId
                    : person.currentDestinationBuildingId,
                _ => person.currentDestinationBuildingId
            };
            if (buildingId < 0) return string.Empty;
            string locationId = TownJourneyNetworkBuilder.LocationIdForBuilding(buildingId);
            return hub.Journeys.GetLocation(locationId) != null ? locationId : string.Empty;
        }

        private void SetPersonJourneyLocation(int personId, string locationId)
        {
            PersonState person = populationManager?.State?.GetPerson(personId);
            const string prefix = "building:";
            if (person == null || string.IsNullOrWhiteSpace(locationId)
                || !locationId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || !int.TryParse(locationId.Substring(prefix.Length), out int buildingId))
            {
                return;
            }

            person.currentDestinationBuildingId = buildingId;
        }

        /// <summary>
        /// Rebinds finance windows to the current household ledgers after
        /// population generation or save restore. This is explicit because
        /// PopulationManager may not have populated its state during Awake.
        /// </summary>
        public void RefreshHouseholdCashBindings()
        {
            if (hub == null || populationManager == null || populationManager.State?.households == null)
            {
                return;
            }

            foreach (HouseholdState household in populationManager.State.households)
            {
                if (household == null || household.id < 0)
                {
                    continue;
                }

                // Do not create a ledger merely to expose a finance window:
                // legacy/new-game cash migration may still be pending, and
                // manufacturing an empty ledger here would disable the
                // deliberate fallback path. Bind only ledgers that already
                // exist in the authoritative registry.
                HouseholdLedger ledger = hub.HouseholdLedgers.Get(household.id);
                if (ledger != null)
                {
                    hub.CreditCash.Register(
                        "household:" + household.id,
                        new HouseholdCashStore(household.id, ledger));
                }
            }
        }

        /// <summary>
        /// Phase B (Real People): shortage detection. After meals are served,
        /// project each household's needs from actual members, current lot
        /// inventory, expected consumption, season, and supply access. Open
        /// needs are REAL purchasing-need objects for Phase C's shopping loop;
        /// they carry no money and post no sales.
        /// </summary>
        private void DriveHouseholdShortageMonitor(int absoluteDayIndex, List<string> diag)
        {
            if (hub == null || populationManager == null || populationManager.State == null)
            {
                return;
            }

            var policy = MealSchedulingPolicy.Default;
            var monitor = new HouseholdShortageMonitor(hub.PurchasingNeeds)
            {
                LowDaysThreshold = 1f
            };
            bool isColdSeason = IsColdSeasonMonth();
            foreach (HouseholdState household in populationManager.State.households)
            {
                if (household == null)
                {
                    continue;
                }

                HouseholdInventory inventory = hub.HouseholdInventories.GetOrCreate(household.id);
                int members = Math.Max(1, household.ConsumptionMemberCount);
                var expectedDailyUse = new Dictionary<string, int>();
                foreach (string itemId in policy.MealItemPriority)
                {
                    HouseholdItemDefinition definition = HouseholdItemCatalog.Get(itemId);
                    if (definition == null || !definition.IsFood)
                    {
                        continue;
                    }

                    expectedDailyUse[itemId] =
                        members * policy.GetServingUnits(itemId) * policy.ExpectedMealsPerDay;
                }

                int ledgerBalance = hub.HouseholdLedgers.Get(household.id)?.GetBalanceCents() ?? 0;
                var supplyAccess = new HouseholdSupplyAccess
                {
                    HasGeneralStore = HasReachableStockedFoodSeller(household, policy, absoluteDayIndex),
                    // No production off-map retail supplier is currently
                    // bound to this directory, so do not advertise access
                    // that the commerce path cannot execute.
                    HasOffMapAccess = false,
                    CanProduce = false,
                    HasCash = ledgerBalance > 0,
                };

                monitor.Evaluate(
                    household,
                    inventory,
                    expectedDailyUse,
                    isColdSeason,
                    supplyAccess,
                    upcomingObligationCents: 0,
                    expectedIncomeCents: 0,
                    dayIndex: absoluteDayIndex);
            }

            foreach (string monitorDiagnostic in monitor.Diagnostics)
            {
                diag.Add($"[SimulationDrivers] {monitorDiagnostic}");
            }
        }

        private bool HasReachableStockedFoodSeller(
            HouseholdState household,
            MealSchedulingPolicy policy,
            int absoluteDayIndex)
        {
            if (household == null || policy == null || supplierDirectory == null
                || householdMemberships == null || populationManager?.State == null || hub == null)
            {
                return false;
            }

            JourneyModel journeys = Journeys ?? hub.Journeys;
            if (journeys == null) return false;
            List<int> members = householdMemberships.GetActiveMembers(household.id);
            if (members == null || members.Count == 0) return false;

            for (int supplierIndex = 0; supplierIndex < supplierDirectory.All.Count; supplierIndex++)
            {
                IGoodsSupplier supplier = supplierDirectory.All[supplierIndex];
                if (!(supplier is IItemResolvingSupplier itemSupplier)
                    || string.IsNullOrWhiteSpace(itemSupplier.LocationId)
                    || !string.IsNullOrWhiteSpace(itemSupplier.CheckTradingCapability(absoluteDayIndex, 8 * 60)))
                {
                    continue;
                }

                bool offersAvailableFood = false;
                foreach (string itemId in policy.MealItemPriority)
                {
                    string categoryId = itemSupplier.CategoryForItem(itemId);
                    if (string.IsNullOrWhiteSpace(categoryId)
                        || itemSupplier.ItemStockUnits(itemId) <= 0
                        || itemSupplier.ItemPricePerUnitCents(itemId) <= 0)
                    {
                        continue;
                    }
                    offersAvailableFood = true;
                    break;
                }
                if (!offersAvailableFood) continue;

                for (int memberIndex = 0; memberIndex < members.Count; memberIndex++)
                {
                    PersonState person = populationManager.State.GetPerson(members[memberIndex]);
                    if (person == null || person.deathDayIndex >= 0 || person.homeBuildingId < 0) continue;
                    string homeId = TownJourneyNetworkBuilder.LocationIdForBuilding(person.homeBuildingId);
                    if (journeys.FindRoute(homeId, itemSupplier.LocationId, TravelMode.Foot).Found)
                        return true;
                }
            }

            return false;
        }

        private bool IsColdSeasonMonth()
        {
            if (timeManager == null)
            {
                return false;
            }

            int month = timeManager.CurrentDate.Month;
            return month == 11 || month == 12 || month == 1 || month == 2 || month == 3;
        }

        /// <summary>P6: true when the journey model has any routable location.</summary>
        private static bool HasAnyLocation(JourneyModel journeys)
        {
            if (journeys == null) return false;
            foreach (JourneyLocation location in journeys.AllLocations())
                if (location != null) return true;
            return false;
        }

        private void OnShortTick(SimulationTickContext context)
        {
            if (hub == null)
            {
                return;
            }

            // Pass IV (Worker 1): due events pop in due order BEFORE any further
            // advancement, so accelerated ticks cannot skip task completions,
            // passive completions, or deadlines.
            ProcessDueEvents();
            SyncTaskCompletionDueEvents();

            // Pass IX (A2-04): start planned domestic tasks whose window has
            // arrived. DriveDomesticLaborDay only PLANS (appends to the
            // pending list); without this tick call no meal-prep, fuel,
            // animal-care, or chore task ever executed in production.
            // Mirrors the LivingWorldSim tick usage.
            if (timeManager != null && domesticTickGate.ShouldAdvance(timeManager.AbsoluteGameSeconds))
            {
                // Watermark: only forward motion advances domestic labor, so
                // a repeated/paused tick can never re-start a pending task.
                domesticTickGate.MarkAdvanced(timeManager.AbsoluteGameSeconds);
                var domesticDiag = new List<string>();
                hub.DomesticLaborDriver.AdvanceTick(
                    timeManager.AbsoluteGameSeconds,
                    hub.Tasks, hub.WorkTimeBudgets, hub.PersonSchedules,
                    hub.DomesticLabor, hub.DueEvents, domesticDiag);
                if (domesticDiag.Count > 0)
                {
                    Debug.Log("[SimulationDrivers] domestic labor tick: " +
                        string.Join(" | ", domesticDiag.Take(20)));
                }
            }

            int activityWindowEndMinute = Mathf.FloorToInt(context.TimeOfDay01 * 1440f);
            int activityWindowMinutes = Mathf.Max(1,
                Mathf.CeilToInt(context.TickIntervalGameSeconds / DurationConventions.SecondsPerMinute));
            StartQueuedBusinessFeedTasks(context.Date.AbsoluteDayIndex,
                Math.Max(0, activityWindowEndMinute - activityWindowMinutes), activityWindowEndMinute);
            StartQueuedFreightMeetingTasks(context.Date.AbsoluteDayIndex,
                Math.Max(0, activityWindowEndMinute - activityWindowMinutes), activityWindowEndMinute);
            StartQueuedOpeningHitchTask(context.Date.AbsoluteDayIndex,
                Math.Max(0, activityWindowEndMinute - activityWindowMinutes), activityWindowEndMinute);
            ResolveArrivalDrivenTravelTasks(context.Date.AbsoluteDayIndex);
            MonitorOpeningHitchCancellation(context.Date.AbsoluteDayIndex);

            // Player travel advances with the clock (whole minutes only).
            // Pass XL player-as-NPC playtest: when the flag is on, player-
            // issued travel does not advance. Autonomous work advancement
            // is unconditional in both branches (it was already called in
            // both branches before this change); only the player travel
            // call is gated, so restructuring here changes no autonomous
            // behaviour.
            int tickWholeMinutes = Mathf.FloorToInt(context.TickIntervalGameSeconds / 60f);
            if (tickWholeMinutes > 0)
            {
                if (playerDirector != null && !treatPlayerAsNpc)
                {
                    playerDirector.RecordMovementProgress(tickWholeMinutes, hub.WorkTimeBudgets);
                }

                AdvanceAutonomousWork(tickWholeMinutes, context.Date.AbsoluteDayIndex,
                    Mathf.FloorToInt(context.TimeOfDay01 * 1440f));
            }

            // Accepted freight dispatch is an occupied business activity, not
            // a journey. Start it through the same TaskAuthority and person
            // schedule path after autonomous progress for this tick, so the
            // newly-started one-minute assignment cannot be consumed by the
            // time window that made it eligible.
            StartQueuedFreightDispatchTasks(context.Date.AbsoluteDayIndex,
                Math.Max(0, activityWindowEndMinute - activityWindowMinutes), activityWindowEndMinute);

            // Pass IV (Worker 1): player-issued tasks progress in real time instead of
            // stalling in InProgress (extends the AdvanceAutonomousWork pattern).
            int playerWholeMinutes = Mathf.FloorToInt(context.TickIntervalGameSeconds / 60f);
            if (playerWholeMinutes > 0)
            {
                AdvancePlayerWork(playerWholeMinutes, context.Date.AbsoluteDayIndex);
            }

            // Scenario goal evaluation (CLN-3 bootstraps subscribe OnScenarioTick).
            if (scenarioDirector != null && scenarioDirector.HasActiveScenario)
            {
                OnScenarioTick?.Invoke();
            }
        }
    }

    /// <summary>
    /// Production payee directory for the shared tenancy authority. Person
    /// payees resolve through the current PopulationState; business payees
    /// resolve through the existing BusinessRuntimeState cash authority.
    /// </summary>
    internal sealed class LiveTenancyPayeeDirectory : ITenancyPayeeDirectory
    {
        private PopulationManager population;
        private HouseholdLedgerRegistry ledgers;
        private SharedBusinessRuntimeManager businesses;

        public void Bind(PopulationManager populationManager, HouseholdLedgerRegistry householdLedgers,
            SharedBusinessRuntimeManager businessRuntime)
        {
            population = populationManager;
            ledgers = householdLedgers;
            businesses = businessRuntime;
        }

        public int FindPayeeHouseholdId(string payeeHolderId)
        {
            if (string.IsNullOrWhiteSpace(payeeHolderId) || population?.State?.people == null)
                return -1;

            string value = payeeHolderId.Trim();
            if (value.StartsWith("household:", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(value.Substring("household:".Length), out int householdId))
                return ledgers?.Get(householdId) != null ? householdId : -1;

            if (value.StartsWith("person:", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(value.Substring("person:".Length), out int personId))
            {
                PersonState person = population.State.GetPerson(personId);
                return person?.householdId >= 0 && ledgers?.Get(person.householdId) != null
                    ? person.householdId
                    : -1;
            }

            return -1;
        }

        public ITenancyPayeeCashSink FindPayeeBusinessSink(string payeeHolderId)
        {
            if (businesses == null || string.IsNullOrWhiteSpace(payeeHolderId)) return null;
            string value = payeeHolderId.Trim();
            const string prefix = "business:";
            if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
            string businessId = value.Substring(prefix.Length);
            for (int i = 0; i < businesses.Businesses.Count; i++)
            {
                BusinessInstanceState business = businesses.Businesses[i];
                if (business?.RuntimeState != null
                    && string.Equals(business.InstanceId, businessId, StringComparison.OrdinalIgnoreCase))
                    return new BusinessTenancyPayeeSink(business);
            }

            return null;
        }
    }

    internal sealed class BusinessTenancyPayeeSink : ITenancyPayeeCashSink
    {
        private readonly BusinessInstanceState business;

        public BusinessTenancyPayeeSink(BusinessInstanceState business)
        {
            this.business = business;
        }

        public string PayeeHolderId => "business:" + (business?.InstanceId ?? string.Empty);
        public string DisplayName => business?.RuntimeDisplayName ?? PayeeHolderId;

        public void RecordCashInflow(int cents, string label, int dayIndex)
        {
            if (business?.RuntimeState == null || cents <= 0) return;
            business.RuntimeState.AddCashCents(cents);
        }
    }

    /// <summary>Production cash adapter for NPC Business credit workflows.</summary>
    internal sealed class NpcBusinessRuntimeCashPort : INpcBusinessCashPort
    {
        private readonly SharedBusinessRuntimeManager runtime;

        public NpcBusinessRuntimeCashPort(SharedBusinessRuntimeManager runtime)
        {
            this.runtime = runtime;
        }

        private BusinessInstanceState Find(string businessInstanceId)
        {
            if (runtime == null || string.IsNullOrWhiteSpace(businessInstanceId)) return null;
            for (int i = 0; i < runtime.Businesses.Count; i++)
            {
                BusinessInstanceState business = runtime.Businesses[i];
                if (business != null && string.Equals(business.InstanceId, businessInstanceId, StringComparison.OrdinalIgnoreCase))
                {
                    return business;
                }
            }

            return null;
        }

        public int ReadCashCents(string businessInstanceId) => Find(businessInstanceId)?.RuntimeState?.CurrentCashCents ?? 0;

        public string Spend(string businessInstanceId, int dayIndex, int amountCents, string purpose)
        {
            BusinessInstanceState business = Find(businessInstanceId);
            if (business?.RuntimeState == null) return $"NPC business '{businessInstanceId}' is unavailable.";
            if (amountCents <= 0) return "NPC business cash spend must be positive.";
            if (business.RuntimeState.CurrentCashCents < amountCents)
            {
                return $"NPC business '{businessInstanceId}' has only {business.RuntimeState.CurrentCashCents}c; {amountCents}c refused.";
            }

            business.RuntimeState.SpendCents(amountCents);
            return null;
        }

        public string Receive(string businessInstanceId, int dayIndex, int amountCents, string reason)
        {
            BusinessInstanceState business = Find(businessInstanceId);
            if (business?.RuntimeState == null) return $"NPC business '{businessInstanceId}' is unavailable.";
            if (amountCents <= 0) return "NPC business cash receipt must be positive.";
            business.RuntimeState.AddCashCents(amountCents);
            return null;
        }
    }
}
