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
using LandLedgers.Economy.BreadDrivers;
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
using LandLedgers.World.Regional;
using LandLedgers.World;
using LandLedgers.Pathing;
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
        // W4 (F-1): refused return-leg feed receipts, keyed by TransactionId
        // in the pure BusinessFeedCustodyLedger. The batch's cash already
        // moved at purchase and the store's stock was already consumed, so a
        // refused receipt must not drop the batch: it is held here and
        // retried by DriveBusinessHorseFeedProcurement before any new
        // purchase for the same need. Runtime-side only, like
        // businessFeedNeedsOpen: the durable seam for trip data is the
        // WorkTask TermsRef, and a refused receipt has no live task left to
        // ride on, so persisting it durably would need a save-format change.
        // A save/load between refusal and retry drops the held batch; the
        // need stays open, so the next drive buys fresh feed.
        private readonly BusinessFeedCustodyLedger pendingFeedReceipts = new BusinessFeedCustodyLedger();
        private readonly HashSet<int> businessFeedWaitingForHomeLogged = new HashSet<int>();
        private readonly Dictionary<string, string> businessFeedLastStockRefusal = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> horseCareRiskAlreadyLogged = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> taskStartRefusalAlreadyLogged = new HashSet<string>(StringComparer.Ordinal);
        // Slice F2/F3: day-keyed refusal log. The once-only set above
        // hides a stall that persists for days (the Nov 6 console: T44
        // sat Assigned for 10 days with nothing in the log after day 6).
        // Hitch-path and off-map-guard refusals log once per day per key
        // instead, so a persistent stall stays visible.
        private readonly Dictionary<string, int> refusalLoggedDay = new Dictionary<string, int>(StringComparer.Ordinal);
        private int hitchStateLogDayIndex = -1;
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

        /// <summary>
        /// Slice S5: terms for the dispatch staging travel task — the
        /// arrival-driven TravelTaskId task that brings Ethan to staging
        /// (his home building) before the dispatch work task may start.
        /// Mirrors DevelopmentHitchTaskData; the horse is not carried here
        /// because the hitch already attached it.
        /// </summary>
        [Serializable]
        private sealed class FreightDispatchTravelTaskData
        {
            public string AgreementId = string.Empty;
            public string ShipmentId = string.Empty;
            public int OperatorPersonId = -1;
            public int StagingBuildingId = -1;
            public string StagingLocationId = string.Empty;
            public int DurationMinutes;
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
        // Plan 04 Slice 4: running bakery actuals for the cumulative
        // reconciliation line — diagnostics only, never serialized and
        // never saved (reset on scene reload / Continue, like the
        // Millbrook attempt fields above). Threaded into
        // BakeryWholesaleDailyDriver as priors each day and read back
        // from the result; the driver logs the cumulative line, the
        // forwarder only carries the totals.
        private int bakeryCumulativeBatchesBaked;
        private int bakeryCumulativeFlourCashSpentCents;
        private int bakeryCumulativeFlourSacksPurchased;
        private int bakeryCumulativeFlourPoundsPurchased;
        private int bakeryCumulativeNotionsCashSpentCents;
        private int bakeryCumulativeWholesaleRevenueCents;
        // Plan 04 Slice 0 (P0-3): once-per-instance driving-identity log
        // (see DriveMillbrookFreightNegotiation). Log-only.
        private bool millbrookNegotiationIdentityLogged;
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
        private string lastRegionalJourneyRegistrationFailure = string.Empty;
        // Plan 03 Slice 0 (P0-2): once-per-distinct-failure-state log for the
        // journey-network gate, following the regional-registration pattern
        // above. Every silent return in EnsureTownJourneyNetwork names its
        // state here so the 285-day empty-model symptom can be discriminated
        // (townWorld null / Grid null / zero road components) instead of
        // re-occurring silently.
        private string lastTownJourneyNetworkFailure = string.Empty;
        // Plan 03 Slice 0 (P0-2): the instance id of the TownWorldController
        // whose identity was last logged, so the drivers' held instance is
        // named once per instance (including the null -> instance
        // transition) and can be compared with the bootstrapper's.
        private int lastLoggedTownWorldInstanceId = int.MinValue;

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

        // W7 (2026-11-06): BindNpcConstructionPorts removed — it had zero
        // callers, so no production path ever bound these ports. The
        // fields above stay (DriveNpcConstruction reads them and waits
        // loudly while they are unbound); the drive can never commission
        // until a binding point is re-wired by a design decision — see the
        // session checkpoint.

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

        // W7 (2026-11-06): QueueNpcConstructionRequest removed — it had
        // zero callers, so no production path ever queued a request. The
        // npcConstructionRequests list stays (DriveNpcConstruction drains
        // it); it can never be filled until a queue entry point is re-wired
        // by a design decision — see the session checkpoint.
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

            // Plan 08 Slice 4 (Q03, AUD-266): wire the live off-map journey
            // authority into the loop. Set once after the if/else so a rebind
            // does not matter; RebindPopulationAuthorities does not touch it.
            householdShoppingLoop.OffMapJourneys = OffMapJourneys;

            // Priority 4 Slice 4: wire the diagnostic into the loop so its
            // leg assign/start refusals feed the L6 streak (the loop may be
            // built before Awake installs the service; EnsureTaskDurationDiagnostics
            // re-points it then, and a null service leaves the loop unwired).
            if (hub.DurationDiagnostics != null)
            {
                householdShoppingLoop.TimingObserver = hub.DurationDiagnostics;
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

        /// <summary>
        /// Pure policy seam used to protect the generic placeholder id.
        /// A registered adapter whose business id fell back to
        /// "general-store" (the trading port's fallback when no Business
        /// is attached) is still a real storefront when it has a real
        /// building location: the placeholder itself has no building, so
        /// it stays excluded, while a building-backed storefront can no
        /// longer be registered yet invisible to every household's
        /// shopping (the overnight 2026-11-04 "unavailable seller" hole:
        /// registered, stocked, staffed, and open, but never known).
        /// </summary>
        public static bool IsEligibleLocalGeneralStoreSupplier(IGoodsSupplier supplier)
        {
            if (supplier is not LandLedgers.Economy.Businesses.GeneralStore.GeneralStoreSupplierAdapter adapter
                || string.IsNullOrWhiteSpace(adapter.SupplierBusinessId))
            {
                return false;
            }

            if (!string.Equals(adapter.SupplierBusinessId, "general-store", StringComparison.Ordinal))
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(adapter.LocationId)
                && adapter.LocationId.StartsWith("building:", StringComparison.Ordinal);
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

            // Priority 4 Slice 4: re-ensure the diagnostic after a restore
            // (the hub may be fresh, or its service may predate the load).
            EnsureTaskDurationDiagnostics();

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

            // Priority 4 Slice 4: install the task-duration diagnostic on
            // the hub's TaskAuthority, clocked by the authoritative
            // TimeManager timeline. See EnsureTaskDurationDiagnostics.
            EnsureTaskDurationDiagnostics();

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
                // W10 (Plan 05, W-16): assign the household-needs registry
                // so the weekly demand signal collects real household needs
                // (the HouseholdShortageMonitor writes them into
                // hub.PurchasingNeeds). Verified no leak into the
                // business-procurement coverage signal:
                // SharedBusinessWeeklyProcurement.TryGetDemandUnits counts
                // BuyerKind "business" intents only (pinned by
                // SharedBusinessDemandWiringTests
                // .WeeklyPass_CollectsDemandBeforeProcurement), so a
                // household intent can never cover a business need. The
                // weekly summary's "real demand: N intents" line now counts
                // household intents too. Assigned here, at bootstrap, before
                // the first CollectRealDemand / RegisterBusinessInputNeed
                // constructs the RealDemandSource (its lazy ??= captures
                // the registry at construction time).
                sharedBusinessRuntime.HouseholdNeedRegistry = hub.PurchasingNeeds;
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

        /// <summary>
        /// Priority 4 Slice 4 — installs the task-duration diagnostic on
        /// the hub's TaskAuthority, clocked by
        /// <see cref="TimeManager.AbsoluteGameSeconds"/> (plan §5 Slice 4
        /// item 1), and hands the service to the shopping loop so its leg
        /// refusals feed the diagnostic (item 3). Safe to call repeatedly:
        /// the hub install is idempotent and re-pointing the loop's
        /// observer at the same service is a plain assignment. Called from
        /// Awake, Rebind, and the top of OnDayChanged so the observer is
        /// in place before any day-drive transition. No scheduling
        /// behaviour changes: the observer only reads transitions.
        /// </summary>
        private void EnsureTaskDurationDiagnostics()
        {
            if (hub == null || timeManager == null)
            {
                return;
            }

            TaskDurationDiagnostics diagnostics =
                hub.InstallTaskDurationDiagnostics(() => timeManager.AbsoluteGameSeconds);
            if (diagnostics != null && householdShoppingLoop != null)
            {
                householdShoppingLoop.TimingObserver = diagnostics;
            }
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
            // Overnight 2026-11-04: re-apply the attached development-town
            // mortality/fertility switches every day. Population generation
            // sets them on New Game, but a Continued game never regenerates,
            // so without this push a restored development town would silently
            // revert to normal mortality and fertility. When no development
            // configuration is attached, the manager's own values are left
            // untouched (normal behaviour for every other scenario).
            if (populationManager != null && developmentTownConfiguration != null)
            {
                populationManager.ApplyDevelopmentPopulationSettings(developmentTownConfiguration);
            }

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

            // Priority 4 Slice 4: the diagnostic must be installed before
            // the day's first transition is observed (Awake/Rebind normally
            // install it; this covers any path that subscribed without them).
            EnsureTaskDurationDiagnostics();

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
            DriveBakeryWholesalePlanning(absoluteDayIndex);

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
            // Plan 05 Slice 4 (rev1 section 5b): reserve the day's assigned
            // work BEFORE shopping runs, so the window search sees the real
            // Work reservations and plans around them (or fails honestly).
            // ONLY the blanket assignment loop moves — AssignDailyWork +
            // ReserveAndAssignTasks — and it moves SPLIT: reserve + assign
            // ahead of shopping, StartTask stays late (see
            // ManagerWorkSchedulerDrive.ReserveAndAssignTasks). The manager
            // DailyCycle/WeeklyCycle decisions, AdvanceShipments,
            // BusinessLender.ProcessDay and DriveNpcOwnerRequestResolution
            // stay in DriveAutonomousLabor: the manager decisions read
            // business stock/cash that shopping changes. Work claims the
            // shared 600-min budget first; the search then refuses honestly
            // when the remainder cannot cover the trip. No budget change.
            DriveWorkAssignmentBeforeShopping(absoluteDayIndex);
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

            // Priority 4 Slice 4 (plan §5 Slice 4 item 2): the daily
            // task-duration evaluation, once per day, placed at the end of
            // the day drive so the day's transitions from every drive above
            // (work, shopping, domestic, autonomous) are included. Evaluate
            // never throws (§4.2), so the no-try/catch shape of
            // OnDayChanged (Priority 1 plan §1 T0) is preserved. Placement
            // note: the plan's "before the daily summary emission" is the
            // [TaskDuration] summary line, which Evaluate itself emits —
            // the [FoodEconomy] shopping summary is emitted inside
            // DriveDailyNeeds mid-drive, so no placement after the shopping
            // drive can precede it.
            hub.DurationDiagnostics?.Evaluate(timeManager.AbsoluteGameSeconds, absoluteDayIndex);
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
            // W2 (Obj 45): hand the off-map journey authority to the domestic
            // driver — the same predicate the F3 business-task guard reads —
            // so it never plans or starts domestic tasks for an off-map person.
            hub.DomesticLaborDriver.OffMapJourneys = OffMapJourneys;
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
        /// Slice S9: restore off-map journeys into the live authority
        /// after a load. Null-safe by the authority's own contract: a
        /// null DTO clears to empty. Called by the bootstrapper's
        /// MarkRestoredFromSave; uncalled until a save carries the
        /// section (S10 departs the first journey).
        /// </summary>
        public void RestoreOffMapJourneys(OffMapJourneyAuthoritySaveDto dto)
        {
            OffMapJourneys.LoadFromSaveDto(dto);
        }

        /// <summary>
        /// Confirms a real regional building-trigger arrival for the active
        /// freight wagon and its assigned driver. Both persistent journey
        /// records must agree on destination before either is advanced.
        /// Repeated trigger callbacks are idempotent.
        /// </summary>
        public bool ConfirmPhysicalFreightDestinationArrival(
            string vehicleId,
            int operatorPersonId,
            string vehicleJourneyId,
            string destinationLocationId)
        {
            if (string.IsNullOrWhiteSpace(vehicleId) || operatorPersonId < 0
                || string.IsNullOrWhiteSpace(vehicleJourneyId)
                || string.IsNullOrWhiteSpace(destinationLocationId))
                return false;

            OffMapJourneyRecord vehicleJourney = OffMapJourneys.FindActiveByVehicle(vehicleId);
            OffMapJourneyRecord personJourney = OffMapJourneys.FindActiveByPerson(operatorPersonId);
            if (vehicleJourney == null || personJourney == null
                || !string.Equals(vehicleJourney.JourneyId, vehicleJourneyId, StringComparison.Ordinal)
                || vehicleJourney.TravelerKind != OffMapTravelerKind.Vehicle
                || personJourney.TravelerKind != OffMapTravelerKind.Person
                || personJourney.PersonId != operatorPersonId
                || !vehicleJourney.RequiresPhysicalArrivalConfirmation
                || !personJourney.RequiresPhysicalArrivalConfirmation
                || !string.Equals(vehicleJourney.DestinationLocationId, destinationLocationId, StringComparison.Ordinal)
                || !string.Equals(personJourney.DestinationLocationId, destinationLocationId, StringComparison.Ordinal)
                || !string.Equals(vehicleJourney.GatewayLocationId, personJourney.GatewayLocationId, StringComparison.Ordinal))
            {
                return false;
            }

            var diag = new List<string>();
            bool vehicleConfirmed = vehicleJourney.PhysicalDestinationArrivalConfirmed
                || OffMapJourneys.ConfirmPhysicalDestinationArrival(vehicleJourney.JourneyId, destinationLocationId, diag);
            bool personConfirmed = personJourney.PhysicalDestinationArrivalConfirmed
                || OffMapJourneys.ConfirmPhysicalDestinationArrival(personJourney.JourneyId, destinationLocationId, diag);
            if (!vehicleConfirmed || !personConfirmed)
            {
                Debug.LogWarning("[OffMapJourney] Physical freight arrival confirmation refused: " + string.Join(" | ", diag), this);
                return false;
            }

            SyncOffMapJourneysToSaveState();
            Debug.Log($"[OffMapJourney] Physical freight arrival confirmed: vehicle journey '{vehicleJourney.JourneyId}', operator journey '{personJourney.JourneyId}', vehicle '{vehicleId}', Ethan P{operatorPersonId}, destination '{destinationLocationId}'.", this);
            return true;
        }

        /// <summary>
        /// Confirms that the already loaded freight team physically returned
        /// to the authored town origin. Both person and vehicle journey records
        /// must agree; calendar time alone cannot settle the trip.
        /// </summary>
        public bool ConfirmPhysicalFreightOriginArrival(
            string vehicleId,
            int operatorPersonId,
            string vehicleJourneyId,
            string originLocationId)
        {
            if (string.IsNullOrWhiteSpace(vehicleId) || operatorPersonId < 0
                || string.IsNullOrWhiteSpace(vehicleJourneyId)
                || string.IsNullOrWhiteSpace(originLocationId))
                return false;

            OffMapJourneyRecord vehicleJourney = OffMapJourneys.FindActiveByVehicle(vehicleId);
            OffMapJourneyRecord personJourney = OffMapJourneys.FindActiveByPerson(operatorPersonId);
            if (vehicleJourney == null || personJourney == null
                || !string.Equals(vehicleJourney.JourneyId, vehicleJourneyId, StringComparison.Ordinal)
                || vehicleJourney.TravelerKind != OffMapTravelerKind.Vehicle
                || personJourney.TravelerKind != OffMapTravelerKind.Person
                || personJourney.PersonId != operatorPersonId
                || !vehicleJourney.RequiresPhysicalArrivalConfirmation
                || !personJourney.RequiresPhysicalArrivalConfirmation
                || !vehicleJourney.PhysicalDestinationArrivalConfirmed
                || !personJourney.PhysicalDestinationArrivalConfirmed
                || !string.Equals(vehicleJourney.OriginLocationId, originLocationId, StringComparison.Ordinal)
                || !string.Equals(personJourney.OriginLocationId, originLocationId, StringComparison.Ordinal)
                || !string.Equals(vehicleJourney.DestinationLocationId, personJourney.DestinationLocationId, StringComparison.Ordinal)
                || !string.Equals(vehicleJourney.GatewayLocationId, personJourney.GatewayLocationId, StringComparison.Ordinal))
                return false;

            var diag = new List<string>();
            bool vehicleConfirmed = vehicleJourney.PhysicalOriginArrivalConfirmed
                || OffMapJourneys.ConfirmPhysicalOriginArrival(vehicleJourney.JourneyId, originLocationId, diag);
            bool personConfirmed = personJourney.PhysicalOriginArrivalConfirmed
                || OffMapJourneys.ConfirmPhysicalOriginArrival(personJourney.JourneyId, originLocationId, diag);
            if (!vehicleConfirmed || !personConfirmed)
            {
                Debug.LogWarning("[OffMapJourney] Physical freight return confirmation refused: " + string.Join(" | ", diag), this);
                return false;
            }

            SyncOffMapJourneysToSaveState();
            Debug.Log($"[OffMapJourney] Physical freight return confirmed: vehicle journey '{vehicleJourney.JourneyId}', operator journey '{personJourney.JourneyId}', vehicle '{vehicleId}', Ethan P{operatorPersonId}, origin '{originLocationId}'.", this);
            return true;
        }

        /// <summary>
        /// Slice S9: push the latest journey snapshot into the Pass 9
        /// state so any later save carries it. Called by S10 on every
        /// journey mutation (depart, return, cancel). A missing Pass 9
        /// wiring warns loudly when a journey is active instead of
        /// silently dropping it; with no active journey it is a no-op.
        /// </summary>
        private void SyncOffMapJourneysToSaveState()
        {
            SaveLoadManager save = FindAnyObjectByType<SaveLoadManager>();
            if (save?.Series2Pass9State != null)
            {
                save.Series2Pass9State.OffMapJourneys = OffMapJourneys.CaptureSaveDto();
            }
            else if (OffMapJourneys.ActiveJourneyCount > 0)
            {
                Debug.LogWarning("[OffMapJourney] Active off-map journeys exist but no Pass 9 state is assigned; the journeys will not persist.");
            }
        }

        /// <summary>
        /// Slice S9: daily return drive. Completes journeys due today
        /// exactly once (the authority is idempotent per day); with no
        /// journeys this is a no-op. Slice S12: a return is a journey
        /// mutation, so a non-empty return syncs to the Pass 9 save
        /// state (the S9 contract syncs on every mutation; without this
        /// a post-return save resurrects the journey as Departed).
        /// Person returns restore the traveler's journey location to
        /// their home building (the S5 travel-completion pattern); when
        /// the home is unknown the set is skipped, never invented.
        /// Slice S12: a vehicle return resets the linked live shipment's
        /// location to the carrier business id — but only when the
        /// shipment still carries the stale "off-map:" label (fallback
        /// path, pickup missed); a "wagon:" location written by the
        /// pickup is never overwritten, and any other value is left
        /// untouched. When no live shipment matches the wagon, nothing
        /// is written and no location is invented.
        /// </summary>
        private void ProcessFreightJourneyReturns(int absoluteDayIndex, List<string> diag)
        {
            foreach (OffMapJourneyRecord active in OffMapJourneys.AllJourneys())
            {
                if (active == null || !active.IsActive || !active.RequiresPhysicalArrivalConfirmation
                    || active.PhysicalOriginArrivalConfirmed || active.DueReturnDayIndex > absoluteDayIndex) continue;
                LogRefusalDayKeyed("physical-return:" + active.JourneyId, absoluteDayIndex,
                    $"[OffMapJourney] Journey '{active.JourneyId}' reached its estimated return day but remains active: no physical return-arrival event has been confirmed.");
            }
            List<OffMapJourneyRecord> returned = OffMapJourneys.CompleteDueJourneys(absoluteDayIndex, deliverCargo: null, diag);
            foreach (OffMapJourneyRecord record in returned)
            {
                if (record == null) continue;
                Debug.Log($"[OffMapJourney] Journey '{record.JourneyId}' returned from '{record.DestinationLabel}' on day {absoluteDayIndex} (due day {record.DueReturnDayIndex}).");
                if (record.TravelerKind == OffMapTravelerKind.Person)
                {
                    PersonState person = populationManager?.State?.GetPerson(record.PersonId);
                    if (person != null && person.homeBuildingId >= 0)
                    {
                        SetPersonJourneyLocation(record.PersonId, "building:" + person.homeBuildingId);
                    }
                    else
                    {
                        Debug.Log($"[OffMapJourney] Journey '{record.JourneyId}': P{record.PersonId} returned but home building is unknown; journey location left unchanged.");
                    }
                }
                else if (record.TravelerKind == OffMapTravelerKind.Vehicle && FreightProduction != null)
                {
                    // Slice S12 (G2, coordinator clarification on the plan's
                    // §4.1 ordering note): the return-day reset runs only
                    // for a journey that completed today in this call (the
                    // `returned` list) — never hoisted into a daily scan —
                    // and only when the shipment still carries the stale
                    // "off-map:<journeyId>" label from departure. On the
                    // normal path the S11 pickup already wrote
                    // "wagon:<wagonId>" on D+1 and that value must stand;
                    // any other value is left untouched as well.
                    foreach (LogisticsShipmentState shipment in FreightProduction.AllShipments())
                    {
                        if (shipment != null
                            && LandLedgers.Economy.Logistics.FreightShipmentService.IsLiveShipment(shipment)
                            && string.Equals(shipment.physicalWagonAssetId, record.VehicleId, StringComparison.Ordinal)
                            && (shipment.physicalLocationId ?? string.Empty).StartsWith("off-map:", StringComparison.Ordinal))
                        {
                            shipment.physicalLocationId = shipment.CarrierBusinessInstanceId;
                            break;
                        }
                    }
                    // Slice F5: visible return — the trio reactivates at
                    // the exit cell and walks back to staging. Only for a
                    // returned vehicle record with a linked LIVE shipment;
                    // a journey with no live shipment starts no leg. This
                    // is independent of the S12 reset above: on the normal
                    // path the S11 pickup already wrote "wagon:<id>" on
                    // D+1, so the reset does not fire — but the team still
                    // comes home visibly.
                    LogisticsShipmentState liveShipment = FreightProduction.AllShipments().FirstOrDefault(candidate => candidate != null
                        && LandLedgers.Economy.Logistics.FreightShipmentService.IsLiveShipment(candidate)
                        && string.Equals(candidate.physicalWagonAssetId, record.VehicleId, StringComparison.Ordinal));
                    if (liveShipment != null && !record.RequiresPhysicalArrivalConfirmation)
                        FindAnyObjectByType<OpeningTransportPresentationController>()?.BeginCoupledReturn(liveShipment);
                }
            }
            // Slice S12 (G1): a return is a journey mutation, and the S9
            // contract syncs on every mutation; without this a post-return
            // save resurrects the journey as Departed.
            if (returned.Count > 0) SyncOffMapJourneysToSaveState();
        }

        /// <summary>
        /// Slice S11 (Objective 27): daily supplier-pickup drive. While the
        /// wagon is beyond the gateway on the Millbrook haul (day D+1), the
        /// paid lot's custody moves from Millbrook into the wagon through
        /// the same adapter hooks WorkLoading uses on the return day, and
        /// the journey record keeps a purchase-manifest receipt. No task is
        /// created: the pickup is a ledger operation by the driver's own
        /// hands at the supplier (Verify-6 — no off-map person-time exists
        /// to book). The shipment stays Planned; the load-skip branch in
        /// FreightProductionDriver.AdvanceTick dispatches it on the return
        /// day. Source-reviewed only — SimulationDrivers is
        /// harness-excluded (call shapes verified against the adapter, the
        /// authority, and the shipment by reading, as S1–S10 did).
        /// </summary>
        private void ProcessFreightSupplierPickups(int absoluteDayIndex, LandLedgers.Economy.Logistics.FreightShipmentHooks hooks)
        {
            if (FreightProduction?.ServiceAgreements?.Agreements == null || millbrookAidRuntime == null) return;
            foreach (FreightServiceAgreement agreement in FreightProduction.ServiceAgreements.Agreements.ToList())
            {
                if (agreement == null || agreement.Status != FreightServiceAgreementStatus.Accepted) continue;
                LogisticsShipmentState shipment = FreightProduction.AllShipments().FirstOrDefault(candidate => candidate != null
                    && string.Equals(candidate.ShipmentId, agreement.ShipmentId, StringComparison.Ordinal));
                // (a) Only an authorized, Planned, unloaded, lot-custody
                // shipment with a bound lot can be picked up at the supplier.
                if (shipment == null || shipment.requiresDispatchAuthorization
                    || shipment.Status != LogisticsShipmentStatus.Planned || shipment.loadApplied
                    || !shipment.requiresSourceLotCustody
                    || string.IsNullOrWhiteSpace(shipment.CargoLotId)) continue;
                // (b) The wagon's active journey; none means the wagon is
                // home or never departed — skip silently.
                OffMapJourneyRecord record = OffMapJourneys.FindActiveByVehicle(shipment.physicalWagonAssetId);
                if (record == null) continue;
                // (c) The agreement must be on the journey; a journey for a
                // different order must never pick up this lot.
                if (record.ObligationIds == null || !record.ObligationIds.Contains(agreement.AgreementId)) continue;
                // (d) Physical freight waits for the destination-specific
                // regional building trigger. Calendar days remain a fallback
                // only for legacy journeys without a physical route.
                if (record.RequiresPhysicalArrivalConfirmation && !record.PhysicalDestinationArrivalConfirmed)
                {
                    LogRefusalDayKeyed("physical-pickup:" + shipment.ShipmentId, absoluteDayIndex,
                        $"[FreightPickup] Shipment '{shipment.ShipmentId}' remains pending: no physical arrival at destination '{record.DestinationLocationId}' has been confirmed.");
                    continue;
                }
                if (!record.RequiresPhysicalArrivalConfirmation
                    && absoluteDayIndex < record.DepartureDayIndex + record.GatewayTravelDays) continue;
                // (e) The lot must exist; a missing lot is an integrity
                // failure, never papered over with a fresh lot.
                OffMapAidCargoLot lot = millbrookAidRuntime.Lots.FirstOrDefault(candidate => candidate != null
                    && string.Equals(candidate.LotId, shipment.CargoLotId, StringComparison.Ordinal));
                if (lot == null)
                {
                    LogRefusalDayKeyed("supplier-pickup:" + shipment.ShipmentId, absoluteDayIndex,
                        $"[FreightPickup] Supplier pickup refused for shipment '{shipment.ShipmentId}': paid lot '{shipment.CargoLotId}' is absent from Millbrook's lots.");
                    return;
                }
                // (f) Already picked up: idempotent re-entry after a save/load.
                if (lot.SourceCommitted && string.Equals(lot.ShipmentId, shipment.ShipmentId, StringComparison.Ordinal)) continue;
                // (g) No adapter wired today: the D+2 fallback owns the
                // consequence (the return-day load re-commits in town).
                if (hooks == null || hooks.CommitSourceLot == null)
                {
                    Debug.LogWarning($"[FreightPickup] Supplier pickup skipped for shipment '{shipment.ShipmentId}': no source-lot debit authority is wired today; the return-day load owns the consequence.", this);
                    return;
                }
                // (h) Commit through the same adapter WorkLoading uses.
                if (!hooks.CommitSourceLot(shipment, shipment.PlannedQuantityUnits, out string reason))
                {
                    LogRefusalDayKeyed("supplier-pickup:" + shipment.ShipmentId, absoluteDayIndex,
                        $"[FreightPickup] Supplier pickup refused for shipment '{shipment.ShipmentId}': {reason}");
                    return;
                }
                // (i) Manifest receipt, persisted through the S9 seam.
                // Package units (the shipment's unit), not pounds: the
                // pounds stay on the lot.
                record.PurchaseManifestUnits[MillbrookFlourFreightCustodyAdapter.FlourSkuId] = shipment.PlannedQuantityUnits;
                SyncOffMapJourneysToSaveState();
                // (j) Exactly WorkLoading's post-commit writes, without a
                // task. The status stays Planned until AdvanceTick
                // dispatches it; state is never set here.
                shipment.sourceLotDebitApplied = true;
                shipment.loadApplied = true;
                shipment.sourceCommittedAtSchedule = true;
                shipment.remainingQuantityUnits = shipment.PlannedQuantityUnits;
                shipment.physicalLocationId = "wagon:" + shipment.physicalWagonAssetId;
                // (k) One greppable pickup line: sack count and pounds both
                // named, so the two unit systems are never confused.
                Debug.Log($"[FreightPickup] Picked up lot '{lot.LotId}' at Millbrook on day {absoluteDayIndex}: " +
                    $"{shipment.PlannedQuantityUnits} sack(s), {lot.InventoryUnits} {lot.InventoryUnit}, " +
                    $"wagon '{shipment.physicalWagonAssetId}', journey '{record.JourneyId}'.", this);
            }
        }

        /// <summary>
        /// Starts the same assigned team on its physical return only after the
        /// source-lot custody adapter has committed cargo into its wagon. A
        /// missing route/presentation binding leaves the active journey and
        /// cargo untouched for a later daily retry.
        /// </summary>
        private void DrivePhysicalFreightReturns(int absoluteDayIndex)
        {
            if (FreightProduction?.ServiceAgreements?.Agreements == null) return;
            OpeningTransportPresentationController presentation =
                FindAnyObjectByType<OpeningTransportPresentationController>();
            if (presentation == null) return;

            foreach (OffMapJourneyRecord journey in OffMapJourneys.AllJourneys())
            {
                if (journey == null || !journey.IsActive || !journey.RequiresPhysicalArrivalConfirmation
                    || !journey.PhysicalDestinationArrivalConfirmed || journey.PhysicalOriginArrivalConfirmed
                    || journey.TravelerKind != OffMapTravelerKind.Vehicle)
                    continue;

                FreightServiceAgreement agreement = FreightProduction.ServiceAgreements.Agreements.FirstOrDefault(candidate =>
                    candidate != null && candidate.Status == FreightServiceAgreementStatus.Accepted
                    && candidate.ShipmentId != null
                    && journey.ObligationIds != null && journey.ObligationIds.Contains(candidate.AgreementId));
                if (agreement == null) continue;
                int operatorPersonId = agreement.AcceptingOperatorPersonId;
                OffMapJourneyRecord operatorJourney = OffMapJourneys.FindActiveByPerson(operatorPersonId);
                if (operatorJourney == null || !operatorJourney.RequiresPhysicalArrivalConfirmation
                    || !operatorJourney.PhysicalDestinationArrivalConfirmed
                    || !string.Equals(operatorJourney.DestinationLocationId, journey.DestinationLocationId, StringComparison.Ordinal)
                    || !string.Equals(operatorJourney.GatewayLocationId, journey.GatewayLocationId, StringComparison.Ordinal)
                    || operatorJourney.ObligationIds == null || !operatorJourney.ObligationIds.Contains(agreement.AgreementId))
                    continue;
                LogisticsShipmentState shipment = FreightProduction.AllShipments().FirstOrDefault(candidate => candidate != null
                    && string.Equals(candidate.ShipmentId, agreement.ShipmentId, StringComparison.Ordinal)
                    && string.Equals(candidate.physicalWagonAssetId, journey.VehicleId, StringComparison.Ordinal)
                    && candidate.loadApplied
                    && LandLedgers.Economy.Logistics.FreightShipmentService.IsLiveShipment(candidate));
                if (shipment == null) continue;

                if (!presentation.BeginCoupledPhysicalReturn(shipment, journey, operatorPersonId))
                {
                    LogRefusalDayKeyed("physical-return-start:" + journey.JourneyId, absoluteDayIndex,
                        $"[OffMapJourney] Loaded freight journey '{journey.JourneyId}' is waiting to begin its physical return: the assigned team, route, or current physical position did not validate.");
                }
            }
        }

        /// <summary>
        /// Slice S10: the Millbrook site location id, by the same
        /// RegionalTownRegistry.All scan TryRegisterAcceptedFreightShipment
        /// uses. Factored out so the departure re-resolves it from the live
        /// model instead of trusting a stored id; the registration call site
        /// is unchanged.
        /// </summary>
        private string TryResolveMillbrookSiteLocationId()
        {
            if (millbrookAidRuntime == null) return null;
            foreach (RegionalTownRegistry.RegionalTownDefinition town in RegionalTownRegistry.All)
            {
                if (town?.Sites == null) continue;
                foreach (RegionalTownRegistry.RegionalBusinessSite site in town.Sites)
                {
                    if (site != null && string.Equals(site.BusinessId, millbrookAidRuntime.BusinessId, StringComparison.Ordinal))
                        return RegionalTownRegistry.SiteLocationId(town, site);
                }
            }
            return null;
        }

        /// <summary>
        /// Slice S10: depart the opening wagon + Ethan for the Millbrook
        /// haul. Wagon first, person second, cancel-on-person-failure (never
        /// a driverless wagon off-map). A false return never un-authorizes
        /// anything: the shipment stays authorized and the daily scan
        /// retries. Source-reviewed only — SimulationDrivers is
        /// harness-excluded.
        /// </summary>
        private bool TryDepartMillbrookShipment(FreightServiceAgreement agreement, LogisticsShipmentState shipment, int dayIndex)
        {
            if (agreement == null || shipment == null) return false;
            string wagonId = shipment.physicalWagonAssetId;
            int operatorId = agreement.AcceptingOperatorPersonId;

            // (1) Millbrook site, re-resolved from the live model.
            string siteLocationId = TryResolveMillbrookSiteLocationId();
            if (string.IsNullOrWhiteSpace(siteLocationId))
            {
                LogFreightAgreementBlockerOnce("departure-gateway:" + shipment.ShipmentId,
                    $"[OffMapJourney] Departure refused for shipment '{shipment.ShipmentId}': Millbrook supplier has no registered regional site.");
                return false;
            }

            OpeningTransportPresentationController transportPresentation =
                FindAnyObjectByType<OpeningTransportPresentationController>();
            if (transportPresentation == null || !transportPresentation.CanBeginCoupledDeparture(siteLocationId))
            {
                LogFreightAgreementBlockerOnce("departure-physical-site:" + shipment.ShipmentId,
                    $"[OffMapJourney] Departure refused for shipment '{shipment.ShipmentId}': physical destination trigger '{siteLocationId}' or coupled transport presentation is unavailable.");
                return false;
            }

            // (2) Re-resolve the exit point for the live model.
            if (!RegionalExitPointResolver.TryResolve(Journeys ?? hub.Journeys, "town-center", siteLocationId,
                    out RegionalExitPoint exitPoint, out string exitReason))
            {
                LogFreightAgreementBlockerOnce("departure-exit:" + shipment.ShipmentId,
                    $"[OffMapJourney] Departure refused for shipment '{shipment.ShipmentId}': exit point unresolved: {exitReason}");
                return false;
            }

            // (3) Gateway re-check (Verify-5): the authority's gateway must
            // agree with the re-resolved S7 exit, or the team never drives
            // toward a stale edge cell.
            if (!OffMapJourneys.TryResolveDepartureGateway("town-center", out string authorityGateway, out string gatewayReason))
            {
                LogFreightAgreementBlockerOnce("departure-gateway-check:" + shipment.ShipmentId,
                    $"[OffMapJourney] Departure refused for shipment '{shipment.ShipmentId}': departure gateway unresolved: {gatewayReason}");
                return false;
            }
            if (!string.Equals(authorityGateway, exitPoint.GatewayLocationId, StringComparison.Ordinal))
            {
                LogFreightAgreementBlockerOnce("departure-gateway-check:" + shipment.ShipmentId,
                    $"[OffMapJourney] Departure refused for shipment '{shipment.ShipmentId}': S7 exit names {exitPoint.GatewayLocationId}, authority names {authorityGateway} — departure refused, edge cell is stale.");
                return false;
            }

            // (4) Duplicate guard: already away — the authority would refuse
            // anyway; the check avoids log spam on the daily retry.
            if (OffMapJourneys.IsOffMapVehicle(wagonId)) return false;

            // (5) Already-departed guard: this shipment departed once already
            // (per-agreement, so a future second order can still depart). A
            // Cancelled record does not count, so a rolled-back attempt stays
            // retryable the next day. Without this the scan would re-depart
            // the wagon on the return day instead of letting the tick load it.
            foreach (OffMapJourneyRecord record in OffMapJourneys.AllJourneys())
            {
                if (record == null || record.State == OffMapJourneyState.Cancelled) continue;
                if (record.ObligationIds != null && record.ObligationIds.Contains(agreement.AgreementId)) return false;
            }

            // (6) Depart the wagon, empty: the flour is picked up at the
            // supplier (Objective 27), so no cargo and no callbacks.
            var diag = new List<string>();
            OffMapJourneyRecord wagonRecord = OffMapJourneys.DepartVehicle(
                wagonId, "town-center", exitPoint.DestinationLabel, exitPoint.OffMapLegDays,
                "Millbrook flour pickup", null, new List<string> { agreement.AgreementId },
                dayIndex, null, null, diag);
            if (wagonRecord == null)
            {
                LogFreightAgreementBlockerOnce("departure-vehicle:" + shipment.ShipmentId,
                    $"[OffMapJourney] Wagon '{wagonId}' departure refused for shipment '{shipment.ShipmentId}': {string.Join(" | ", diag)}");
                return false;
            }
            if (!OffMapJourneys.RequirePhysicalArrivalConfirmation(wagonRecord.JourneyId, siteLocationId, diag))
            {
                OffMapJourneys.CancelJourney(wagonRecord.JourneyId, "physical destination could not be bound", returnCargo: null, diag);
                LogFreightAgreementBlockerOnce("departure-destination:" + shipment.ShipmentId,
                    $"[OffMapJourney] Departure refused for shipment '{shipment.ShipmentId}': " + string.Join(" | ", diag));
                return false;
            }

            // (7) Depart Ethan with the wagon. Person failure cancels the
            // wagon journey (never a driverless wagon off-map) and refuses
            // the departure as a whole; the daily scan retries the next day.
            int expectedReturnDays = Math.Max(1, wagonRecord.DueReturnDayIndex - dayIndex);
            var personDiag = new List<string>();
            OffMapJourneyRecord personRecord = OffMapJourneys.DepartPerson(
                operatorId, "town-center", exitPoint.DestinationLabel, exitPoint.OffMapLegDays,
                expectedReturnDays, "drive the flour wagon", null,
                new List<string> { agreement.AgreementId }, dayIndex,
                item => 0, null, personDiag);
            if (personRecord == null)
            {
                OffMapJourneys.CancelJourney(wagonRecord.JourneyId, "driver departure refused", returnCargo: null, diag);
                SyncOffMapJourneysToSaveState();
                LogFreightAgreementBlockerOnce("departure-driver:" + shipment.ShipmentId,
                    $"[OffMapJourney] Wagon '{wagonId}' departed but Ethan P{operatorId} refused departure for shipment '{shipment.ShipmentId}'; wagon journey cancelled. {string.Join(" | ", personDiag)}");
                return false;
            }
            if (!OffMapJourneys.RequirePhysicalArrivalConfirmation(personRecord.JourneyId, siteLocationId, personDiag))
            {
                OffMapJourneys.CancelJourney(personRecord.JourneyId, "physical destination could not be bound", returnCargo: null, personDiag);
                OffMapJourneys.CancelJourney(wagonRecord.JourneyId, "driver destination binding failed", returnCargo: null, diag);
                SyncOffMapJourneysToSaveState();
                LogFreightAgreementBlockerOnce("departure-destination-driver:" + shipment.ShipmentId,
                    $"[OffMapJourney] Departure refused for shipment '{shipment.ShipmentId}': " + string.Join(" | ", personDiag));
                return false;
            }

            // (8) Success: the shipment's location string names the journey
            // (coder verification point, plan 09 §1.7 — no reader treats a
            // non-business-id value as an error: ResolvePhysicalLocation
            // itself returns "off-map-destination" / "off-map-origin" /
            // "shipment:<id>:quarantine" / "shipment:<id>:in-transit", the
            // save DTO copies the string verbatim, and the LRM transport
            // projection only runs over its own shipment list).
            shipment.physicalLocationId = wagonRecord.OffMapLocationLabel;
            SyncOffMapJourneysToSaveState();
            // Slice F5: an abstract journey is insufficient by itself. Start
            // the same coupled physical team before reporting departure; if
            // that fails, cancel both empty journey records and leave the
            // accepted shipment eligible for a later retry.
            if (!transportPresentation.BeginCoupledDeparture(shipment, siteLocationId))
            {
                OffMapJourneys.CancelJourney(personRecord.JourneyId, "coupled physical departure could not start", returnCargo: null, personDiag);
                OffMapJourneys.CancelJourney(wagonRecord.JourneyId, "coupled physical departure could not start", returnCargo: null, diag);
                shipment.physicalLocationId = shipment.CarrierBusinessInstanceId;
                SyncOffMapJourneysToSaveState();
                LogFreightAgreementBlockerOnce("departure-physical-start:" + shipment.ShipmentId,
                    $"[OffMapJourney] Departure refused for shipment '{shipment.ShipmentId}': coupled physical movement did not start; both empty journey records were cancelled and remain retryable.");
                return false;
            }
            Debug.Log($"[OffMapJourney] Wagon '{wagonId}' + Ethan P{operatorId} departed town-center for '{exitPoint.DestinationLabel}' via {wagonRecord.GatewayLocationId}, due back day {wagonRecord.DueReturnDayIndex}.", this);
            // Slice F5: visible departure — only on a real departure
            // (journey record created, save synced). Never from
            // dispatch-task start, authorization alone, or the daily retry
            // scan without a record: this is the only call site.
            return true;
        }

        /// <summary>
        /// Slice S10: daily retry scan for refused departures. For each
        /// accepted agreement with a linked shipment that is authorized,
        /// Planned and unloaded, where the wagon, the horse and the operator
        /// all resolve and Ethan is visually at the team (the S5 gate,
        /// re-checked on the retry path because a day may have passed since
        /// authorization), attempt the departure. Silent skips: the scan is
        /// a retry loop, and TryDepartMillbrookShipment logs its own
        /// refusals once.
        /// </summary>
        private void TryDepartAuthorizedFreightShipments(int absoluteDayIndex)
        {
            if (FreightProduction?.ServiceAgreements?.Agreements == null) return;
            foreach (FreightServiceAgreement agreement in FreightProduction.ServiceAgreements.Agreements.ToList())
            {
                if (agreement == null || agreement.Status != FreightServiceAgreementStatus.Accepted) continue;
                LogisticsShipmentState shipment = FreightProduction.AllShipments().FirstOrDefault(candidate => candidate != null
                    && string.Equals(candidate.ShipmentId, agreement.ShipmentId, StringComparison.Ordinal));
                if (shipment == null || shipment.requiresDispatchAuthorization
                    || shipment.Status != LogisticsShipmentStatus.Planned || shipment.loadApplied) continue;
                EquipmentAsset wagon = string.IsNullOrWhiteSpace(shipment.physicalWagonAssetId)
                    ? null : hub?.TransportAssets?.Get(shipment.physicalWagonAssetId);
                AnimalState horse = FindOpeningFreightHorse(agreement.CarrierBusinessId);
                int operatorId = agreement.AcceptingOperatorPersonId;
                PersonState operatorPerson = operatorId >= 0 ? populationManager?.State?.GetPerson(operatorId) : null;
                if (wagon == null || horse == null || operatorPerson == null) continue;
                if (!IsPersonVisuallyAtBuilding(operatorId, operatorPerson.homeBuildingId)) continue;
                TryDepartMillbrookShipment(agreement, shipment, absoluteDayIndex);
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
                // Slice S10: the off-map hold — AdvanceTick skips shipments
                // whose wagon is off-map. Method-group assignment; the
                // authority method takes a vehicle id and returns bool.
                IsVehicleOffMap = OffMapJourneys.IsOffMapVehicle,
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
            // Slice S9: complete journeys due today before the tick, so a
            // wagon that returns today is home for the same day's tick.
            ProcessFreightJourneyReturns(absoluteDayIndex, diag);
            // Slice S11: pick up the paid lot at the supplier on the
            // beyond-gateway day (D+1), before the departure retry scan
            // and the tick, so the pickup's flag writes are visible to the
            // same day's tick.
            ProcessFreightSupplierPickups(absoluteDayIndex, hooks);
            // A source lot becomes return cargo only after the pickup adapter
            // committed it. Start the continuous physical return once that
            // custody transition exists; retries are idempotent while moving.
            DrivePhysicalFreightReturns(absoluteDayIndex);
            // Slice S10: retry refused departures (after returns, before the
            // tick, so a departure attempted today holds the shipment for
            // today's tick). The scan's guards make it a no-op for departed
            // and returned shipments.
            TryDepartAuthorizedFreightShipments(absoluteDayIndex);
            FreightProduction.AdvanceTick(86400f, hooks, handler, diag);
            ReleaseTerminalFreightDraftTeams();
            if (diag.Count > 0)
            {
                Debug.Log("[SimulationDrivers] freight day " + absoluteDayIndex + ": " +
                    string.Join(" | ", diag));
            }
            DrivePreparedFreightDispatchAssignments(absoluteDayIndex);
            DriveMillbrookFreightNegotiation(absoluteDayIndex);
        }

        /// <summary>Slice S3: release draft-team bindings for detailed shipments
        /// that reached a terminal state, so the horse and wagon are free for
        /// the next shipment. Only releases bindings this slice created (the
        /// wagon and the horse both still name the terminal shipment); never
        /// touches a team bound to a different live shipment. Idempotent:
        /// a released binding no longer names the shipment, so a later sweep
        /// skips it.</summary>
        private void ReleaseTerminalFreightDraftTeams()
        {
            if (FreightProduction == null || hub?.TransportAssets == null || hub.ReservationAuthority == null)
                return;
            EquipmentAsset openingWagon = hub.TransportAssets.Get("opening-freight-wagon");
            if (openingWagon == null)
                return;
            foreach (LogisticsShipmentState shipment in FreightProduction.AllShipments())
            {
                if (shipment == null
                    || (shipment.Status != LogisticsShipmentStatus.Completed
                        && shipment.Status != LogisticsShipmentStatus.Failed))
                    continue;
                if (!string.Equals(shipment.physicalWagonAssetId, openingWagon.AssetId, StringComparison.Ordinal))
                    continue;
                if (!string.Equals(openingWagon.CurrentShipmentId, shipment.ShipmentId, StringComparison.Ordinal))
                    continue;
                AnimalState horse = FindOpeningFreightHorse(shipment.CarrierBusinessInstanceId);
                if (horse == null
                    || !string.Equals(horse.CurrentAssignmentId, shipment.ShipmentId, StringComparison.Ordinal))
                    continue;
                WagonAuthority.ReleaseDraftTeam(openingWagon, new List<AnimalState> { horse },
                    new AnimalAvailabilityAuthority(hub.ReservationAuthority));
                Debug.Log($"[FreightDispatch] Terminal shipment '{shipment.ShipmentId}' released its draft team (A0 + opening wagon).", this);
            }
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
            // Plan 04 Slice 0 (P0-3): name the driving instance once per
            // run (GameObject name + instance id + claim state + runtime
            // null/non-null + BusinessId when present), so Codex can
            // compare it with the attach log from ConfigureMillbrookAidTable
            // and tell a wrong-instance attach (A1) from a replaced driving
            // instance (A2). Log-only; no behavior change.
            if (!millbrookNegotiationIdentityLogged)
            {
                millbrookNegotiationIdentityLogged = true;
                Debug.Log($"[FreightAgreement] Daily negotiation driving instance: '{gameObject.name}' " +
                    $"(instance {GetInstanceID()}), authority claim held={hasAuthorityClaim}, " +
                    $"Millbrook runtime={(millbrookAidRuntime == null ? "null (unresolved)" : "attached, BusinessId=" + millbrookAidRuntime.BusinessId)}.", this);
            }
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
                LogFreightAgreementBlockerOnce("dependencies", $"[FreightAgreement] Daily request driver is connected on day {dayIndex}, but cannot evaluate the opening order because {missing} is unresolved. [driving instance '{gameObject.name}' (instance {GetInstanceID()})]");
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

            // Plan 10 E1 (slice 2B): the negotiation ships the oldest
            // uncommitted, unreceived paid lot for this bakery — the
            // opening lot while it is still uncommitted, a reorder lot
            // after it. The request id derives from the lot, never from
            // the opening-order constant, so a second haul opens a
            // second request.
            OffMapAidCargoLot lot = MillbrookFlourReorder.FindShippableLot(millbrookAidRuntime, bakery.InstanceId);
            if (lot == null || lot.SourceCommitted || lot.Received)
            {
                string lotState = lot == null
                    ? $"target paid lot absent; Millbrook='{millbrookAidRuntime.BusinessId}', lotCount={millbrookAidRuntime.Lots?.Count ?? 0}, candidates={string.Join(",", millbrookAidRuntime.Lots?.Where(item => item != null).Select(item => item.LotId + "/buyer=" + item.BuyerBusinessId) ?? Enumerable.Empty<string>())}"
                    : $"lot='{lot.LotId}', sourceCommitted={lot.SourceCommitted}, received={lot.Received}, buyer='{lot.BuyerBusinessId}', SKU='{lot.SkuId}', quantity={lot.PackageUnits} {lot.OrderUnit}";
                LogFreightAgreementBlockerOnce("lot", $"[FreightAgreement] Daily request driver cannot open a request on day {dayIndex}: {lotState}.");
                return;
            }

            FreightServiceAgreementBook book = FreightProduction.ServiceAgreements;
            string requestId = "freight-request:" + lot.OrderId;
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
                    RequestedService = "Collection of the paid Millbrook flour lot " + lot.LotId + " and delivery to Bakery B1",
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

            // Plan 07 Slice 1b (Q02, AUD-334): a request refused before the
            // meeting-retry repair is reopened when its paid lot is still
            // shippable, so the next daily drive retries the same request
            // instead of returning early on it forever. FindShippableLot
            // already returned this lot above; if it had not, the request
            // stays Refused and the drive returns as before.
            if (request.Status == FreightServiceRequestStatus.Refused
                && string.Equals(lot.LotId, request.CargoLotId, StringComparison.Ordinal))
            {
                string reopenRefusal = book.ReopenRefusedRequest(request.RequestId,
                    $"Lot '{lot.LotId}' is still shippable; reopening the refused request for a retried meeting.");
                if (reopenRefusal == null)
                    Debug.Log($"[FreightAgreement] Reopened refused request '{request.RequestId}' for lot '{lot.LotId}': the lot is still shippable, so the meeting will be retried.", this);
            }

            if (request.Status == FreightServiceRequestStatus.Accepted || request.Status == FreightServiceRequestStatus.Refused
                || request.Status == FreightServiceRequestStatus.MeetingScheduled) return;
            EquipmentAsset assignedWagon = hub?.TransportAssets?.Get("opening-freight-wagon");
            string temporaryCapacityBlocker = null;
            if (assignedWagon != null && assignedWagon.IsWagon
                && assignedWagon.OwnerKind == "business"
                && string.Equals(assignedWagon.OwnerId, carrier.InstanceId, StringComparison.Ordinal))
            {
                temporaryCapacityBlocker = FreightProduction?.Service?.FindExclusivityConflict(
                    null, assignedWagon.AssetId, EntityId.Invalid, null);
                if (string.IsNullOrWhiteSpace(temporaryCapacityBlocker) && assignedWagon.IsReserved)
                    temporaryCapacityBlocker = $"Wagon '{assignedWagon.AssetId}' is reserved by '{assignedWagon.ReservedBy}' for '{assignedWagon.ReservedFor}'.";
            }
            if (string.IsNullOrWhiteSpace(temporaryCapacityBlocker) && HasOpenAssignedTask(carrier.Owner.PersonId))
                temporaryCapacityBlocker = "Ethan has an incompatible assigned or in-progress task.";
            if (!string.IsNullOrWhiteSpace(temporaryCapacityBlocker))
            {
                string priorReason = request.RefusalReason;
                book.DeferTemporarily(request.RequestId, temporaryCapacityBlocker);
                if (!string.Equals(priorReason, temporaryCapacityBlocker, StringComparison.Ordinal))
                    Debug.Log($"[FreightAgreement] Request '{request.RequestId}' remains open; Glen's contact is deferred until B3 has capacity: {temporaryCapacityBlocker}", this);
                return;
            }
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

        private static bool IsTemporaryFreightCapacityRefusal(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason)) return false;
            return reason.IndexOf("already bound to live shipment", StringComparison.OrdinalIgnoreCase) >= 0
                || reason.IndexOf("equipment reservation", StringComparison.OrdinalIgnoreCase) >= 0
                || reason.IndexOf("incompatible assigned or in-progress task", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void LogFreightAgreementBlockerOnce(string key, string message)
        {
            if (freightAgreementBlockersLogged.Add(key))
                Debug.LogWarning(message, this);
        }

        /// <summary>
        /// Slices F2/F3: day-keyed refusal logging. Unlike the once-only
        /// set, a refusal that repeats for days stays visible (one line per
        /// day), so a persistent stall can be read from the log instead of
        /// going silent after day 1.
        /// </summary>
        private void LogRefusalDayKeyed(string key, int dayIndex, string message)
        {
            if (refusalLoggedDay.TryGetValue(key, out int loggedDay) && loggedDay == dayIndex) return;
            refusalLoggedDay[key] = dayIndex;
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
                hub.HouseholdInventories, hub.MealLog, null, populationMemberships,
                populationManager != null && populationManager.ConditionDebuffsSuppressed,
                OffMapJourneys);
            if (report.MealsMissed > 0 || report.PurchasesMade > 0)
            {
                Debug.Log($"[SimulationDrivers] daily needs day {absoluteDayIndex}: " +
                    $"{report.MealsEaten} eaten, {report.MealsMissed} missed, " +
                    $"{report.PurchasesMade} purchases ({report.SpendCents}c).");
            }

            // Keep missed meals auditable by resident. These are read-only
            // projections of the meal authority's persisted records; they do
            // not create meals or affect nutrition. Served meals are no
            // longer logged one line per person per slot (overnight
            // 2026-11-04: up to dozens of routine lines a day); the daily
            // needs summary above counts them and MealLog persists each
            // record with its consumed items.
            if (hub.MealLog != null && populationManager.State.households != null)
            {
                foreach (HouseholdState household in populationManager.State.households)
                {
                    if (household == null) continue;
                    int cashCents = hub.HouseholdLedgers.Get(household.id)?.GetBalanceCents() ?? 0;
                    var missedByPerson = hub.MealLog.GetMissedMeals(household.id)
                        .Where(missed => missed != null && missed.DayIndex == absoluteDayIndex)
                        .GroupBy(missed => missed.PersonId);
                    foreach (IGrouping<int, MissedMealRecord> missedGroup in missedByPerson)
                    {
                        MissedMealRecord firstMiss = missedGroup.First();
                        PersonState person = populationManager.State.GetPerson(firstMiss.PersonId);
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
                        string missedSlots = string.Join(", ", missedGroup.Select(missed => missed.Slot.ToString()).Distinct());
                        string reasons = string.Join("; ", missedGroup.Select(missed => missed.Reason)
                            .Where(reason => !string.IsNullOrWhiteSpace(reason)).Distinct());
                        Debug.LogWarning($"[FoodEconomy] meals missed: person='{person?.DisplayName ?? $"P{firstMiss.PersonId}"}', " +
                            $"household=H{firstMiss.HouseholdId}, missed={missedGroup.Count()} slot(s) [{missedSlots}], reason='{reasons}', " +
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

                // Batch shopping runs after meal resolution and can emit
                // dozens of per-person hardship and shortage diagnostics.
                // Keep the cause of a failed real shopping trip ahead of
                // those repetitive summaries so the log explains why new
                // retail stock did not reach household custody.
                for (int i = 0; i < diag.Count && visible.Count < maxDiagnosticsPerDay; i++)
                {
                    string message = diag[i];
                    if (!string.IsNullOrWhiteSpace(message)
                        && message.StartsWith("[FoodEconomy] purchase", StringComparison.Ordinal)
                        && !visible.Contains(message))
                        visible.Add(message);
                }
                for (int i = 0; i < diag.Count && visible.Count < maxDiagnosticsPerDay; i++)
                {
                    string message = diag[i];
                    if (string.IsNullOrWhiteSpace(message) || visible.Contains(message)) continue;
                    if (message.StartsWith("Day " + absoluteDayIndex + ": batch ", StringComparison.Ordinal)
                        || message.StartsWith("Day " + absoluteDayIndex + ": shopping FAILED", StringComparison.Ordinal)
                        || message.StartsWith("[SimulationDrivers] household errands H", StringComparison.Ordinal))
                        visible.Add(message);
                }
                for (int i = 0; i < diag.Count && visible.Count < maxDiagnosticsPerDay; i++)
                {
                    string message = diag[i];
                    if (!string.IsNullOrWhiteSpace(message) && !visible.Contains(message))
                        visible.Add(message);
                }

                // Priority 1 Slice 0: the daily shopping summary is the
                // heartbeat line for the shopping investigation; exempt it
                // from the 12-per-day cap so a bad day cannot hide it
                // behind earlier diagnostics. Always emit it first.
                for (int i = 0; i < diag.Count; i++)
                {
                    string message = diag[i];
                    if (!string.IsNullOrWhiteSpace(message)
                        && message.StartsWith("[FoodEconomy] shopping summary day ", StringComparison.Ordinal)
                        && !visible.Contains(message))
                    {
                        visible.Insert(0, message);
                        break;
                    }
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

            // W4 (F-1): a refused return-leg receipt is retried here, on the
            // next feed-drive day, BEFORE any new purchase is placed for the
            // same need. The held batch's cash already moved at purchase;
            // buying fresh feed first would double-pay for the same need.
            // A still-refused batch stays held and this drive places no new
            // purchase. After a successful retry the drive falls through and
            // recomputes the need from the restocked shelves, so a remaining
            // shortfall can still start a fresh trip the same day.
            if (pendingFeedReceipts.HasPendingForBuyer(freight.InstanceId))
            {
                bool attempted = pendingFeedReceipts.TryRetryForBuyer(
                    freight.InstanceId, freight.RuntimeState, out BusinessFeedPurchase.Result retry);
                if (attempted && retry.Succeeded)
                {
                    Debug.Log($"[BusinessFeed] B3 '{freight.InstanceId}' pending receipt '{retry.Batch.TransactionId}' succeeded on retry " +
                        $"(duplicate={retry.Duplicate}); custody ledger cleared, no new purchase placed for the retried batch.", this);
                    businessFeedNeedsOpen.Remove(freight.InstanceId);
                }
                else
                {
                    Debug.LogWarning($"[BusinessFeed] B3 '{freight.InstanceId}' pending receipt retry " +
                        (attempted ? $"refused: {retry.Refusal}; custody batch stays held and no new purchase is placed."
                            : "could not be attempted: no batch is held; no new purchase is placed."), this);
                    return;
                }
            }

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

            // Slice F3: off-map person-time guard (Verify-6). While the
            // journey record says this person is off-map (day D through
            // D+2 for the freight trip), no town task may start for them —
            // person-time stays exclusive. The guard refuses rather than
            // interrupts: no task is ever started off-map, so nothing needs
            // cancelling. Placed before the already-scheduled scan, so a
            // reservation made before departure cannot start the task
            // mid-trip either. Every Begin call in the business starters is
            // gated by this method, so the guard covers all business-task
            // starts (hitch, dispatch, feed, meeting, staging travel). The
            // domestic driver does NOT funnel through here — that gap is
            // probed by FreightOffMapPersonGuardTests and is an Obj 45
            // follow-up, not a slice failure.
            if (OffMapJourneys.IsOffMapPerson(ethan.id))
            {
                refusal = $"P{ethan.id} is off-map; no town task may start.";
                // The caller owns task-level refusal reporting. Emitting a
                // day-keyed warning here made a single retained task produce
                // one warning every day for the entire multi-day journey.
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
            if (active == null) return true;
            // Slice F2: the start-window decision lives in the pure helper
            // (BusinessTaskStartWindow.Resolve) so the harness can pin it.
            BusinessTaskStartDecision decision = BusinessTaskStartWindow.Resolve(
                active.StartMinuteOfDay, active.EndMinuteOfDay, tickStartMinute, tickEndMinute);
            if (decision == BusinessTaskStartDecision.Wait) return true;
            if (decision == BusinessTaskStartDecision.CatchUp)
            {
                // Slice F2: missed-window catch-up. At high speed the
                // 15-minute tick windows jump clean over a short reservation
                // window (the Nov 6 console: T44 assigned day 6, started
                // day 16; T168 created day 16, started day 19), and the
                // day-scoped reservation then pins the start minute in the
                // past for the rest of the day. Start late from now rather
                // than never: release the stale window and re-reserve
                // [nowMinute, nowMinute + remaining] if it is free and fits
                // today. The fresh window keeps AdvanceAutonomousWork
                // accruing progress (it only advances inside the
                // reservation window) and keeps person time exclusive
                // (TryReserve refuses on collision). The budget was already
                // committed at assignment; nothing is bypassed, shortened,
                // or re-timed. If the fresh window cannot be reserved, the
                // task stays Assigned with no reservation, so the next tick
                // re-derives a fresh window instead of pinning the stale
                // one. If it cannot fit today, it waits for tomorrow's
                // fresh derivation.
                int nowMinute = tickEndMinute;
                int remaining = Math.Max(1, task.MinutesRemaining);
                if (nowMinute + remaining > 1440) return true;
                int catchUpMinutes = IsArrivalDrivenTravelTask(task)
                    ? FindTravelReservationWindowMinutes(ethan.id, dayIndex, nowMinute)
                    : remaining;
                if (nowMinute + catchUpMinutes > 1440) return true;
                hub.PersonSchedules.Release(active.ReservationId);
                string catchUpRefusal = hub.PersonSchedules.TryReserve(ethan.id, active.Activity, dayIndex,
                    nowMinute, catchUpMinutes, TaskReservationLabel(task));
                if (catchUpRefusal != null) return true;
                active = null;
                foreach (PersonActivityReservation reservation in hub.PersonSchedules.ActiveFor(ethan.id, dayIndex))
                {
                    if (reservation != null && string.Equals(reservation.Label, TaskReservationLabel(task), StringComparison.Ordinal))
                    {
                        active = reservation;
                        break;
                    }
                }
                if (active == null) return true;
                Debug.Log($"[BusinessTask] P{ethan.id} task '{task.TaskId}' missed its scheduled window; " +
                    $"starting late at game minute {nowMinute} with a re-reserved window.", this);
            }

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

            // Plan 07 Slice 1a (Q02, AUD-334): each refusal branch decides
            // for itself whether its cause is transient while the lot is
            // still shippable. Transient branches defer through
            // DeferTemporarily and are retried; only the lot branch stays
            // terminal, because a lot that is absent, committed, received,
            // or inconsistent is no longer shippable. The capacity-conflict
            // and wagon-reservation refusals built below still defer through
            // the IsTemporaryFreightCapacityRefusal classifier.
            string refusal = null;
            bool refusalIsTransient = false;
            if (glen == null || ethan == null || bakery?.RuntimeState == null || carrier?.RuntimeState == null)
            {
                refusal = "Glen, Ethan, or a participant's business cash authority is unavailable at meeting completion.";
                refusalIsTransient = true;
            }
            else if (!IsPersonVisuallyAtBuilding(glen.id, ethan.homeBuildingId)
                || !IsPersonVisuallyAtBuilding(ethan.id, ethan.homeBuildingId))
            {
                refusal = "Both participants are not physically present at the Freight Company meeting location.";
                refusalIsTransient = true;
            }
            else if (HasOpenAssignedTask(ethan.id))
            {
                refusal = "Ethan has an incompatible assigned or in-progress task at acceptance time.";
                refusalIsTransient = true;
            }
            else if (request.Quote == null || bakery.RuntimeState.CurrentCashCents < request.Quote.TotalPriceCents)
            {
                refusal = $"Bakery B1 has {bakery?.RuntimeState?.CurrentCashCents ?? 0}c available for a {request.Quote?.TotalPriceCents ?? 0}c agreed charge.";
                refusalIsTransient = true;
            }
            else if (lot == null || lot.SourceCommitted || lot.Received || lot.PackageUnits != request.Quantity
                || !string.Equals(lot.SkuId, request.CargoSkuId, StringComparison.Ordinal)
                || !string.Equals(lot.BuyerBusinessId, bakery.InstanceId, StringComparison.Ordinal))
            {
                refusal = "The exact paid Millbrook lot is absent, committed, received, or inconsistent with the request.";
            }
            else if (horse == null || horse.OwnerKind != AnimalOwnerKind.Business
                || !string.Equals(horse.OwnerId, carrier.InstanceId, StringComparison.Ordinal)
                || !HorseAuthority.IsWorkReady(horse))
            {
                refusal = "A0 is unavailable or not work-ready for Freight Company B3.";
                refusalIsTransient = true;
            }
            else if (wagon == null || !wagon.IsWagon || wagon.OwnerKind != "business"
                || !string.Equals(wagon.OwnerId, carrier.InstanceId, StringComparison.Ordinal))
            {
                refusal = "The existing opening wagon is unavailable or is not owned by Freight Company B3.";
                refusalIsTransient = true;
            }

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
                if (refusalIsTransient || IsTemporaryFreightCapacityRefusal(refusal))
                {
                    string priorReason = request.RefusalReason;
                    string deferRefusal = book.DeferTemporarily(request.RequestId, refusal);
                    if (deferRefusal == null)
                    {
                        if (!string.Equals(priorReason, refusal, StringComparison.Ordinal))
                            Debug.Log($"[FreightAgreement] Ethan P{trip.CarrierOperatorPersonId} deferred request '{request.RequestId}' after the timed meeting because carrier capacity is temporarily occupied: {refusal}. The same request will be reconsidered when the team is free.", this);
                        return;
                    }
                    refusal = deferRefusal;
                }
                book.SetStatus(request.RequestId, FreightServiceRequestStatus.Refused, refusal);
                Debug.LogWarning($"[FreightAgreement] Ethan P{trip.CarrierOperatorPersonId} refused request '{request.RequestId}' after the timed meeting: {refusal}", this);
                return;
            }

            // Plan 07 Slice 1c (Q02, AUD-334): the only reachable Accept
            // failure once the quote is known and the operator is Ethan is
            // a duplicate agreement — the haul already has an agreement, so
            // the request joins it instead of bricking it. Any other Accept
            // failure with no agreement present is a bookkeeping refusal,
            // not a dead lot, so the request defers and the meeting retries.
            if (book.Accept(request.RequestId, trip.CarrierOperatorPersonId, dayIndex,
                    AbsoluteNowGameSeconds(), out FreightServiceAgreement agreement) != null)
            {
                FreightServiceAgreement existingAgreement = book.FindAgreementForRequest(request.RequestId);
                if (existingAgreement != null)
                {
                    request.AgreementId = existingAgreement.AgreementId;
                    book.SetStatus(request.RequestId, FreightServiceRequestStatus.Accepted);
                    Debug.Log($"[FreightAgreement] Ethan's acceptance for '{request.RequestId}' found the existing agreement '{existingAgreement.AgreementId}'; the request joins it instead of refusing.", this);
                    TryRegisterAcceptedFreightShipment(existingAgreement, dayIndex);
                    return;
                }

                string deferRefusal = book.DeferTemporarily(request.RequestId,
                    "The request could not be accepted by the service-agreement authority; retry the meeting.");
                if (deferRefusal == null)
                {
                    Debug.Log($"[FreightAgreement] Ethan's acceptance for '{request.RequestId}' was refused without an agreement; the request defers and the meeting will be retried.", this);
                    return;
                }

                refusal = deferRefusal;
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
            // Slice S4: route resolved by this registration attempt, for the
            // registration log. Empty when the shipment already existed.
            string resolvedSiteLocationId = string.Empty;
            float resolvedTransitSeconds = 0f;
            // Slice S7: exit point resolved by this registration attempt, for
            // the registration log. Empty when the shipment already existed
            // or the exit point could not be resolved (non-gating — see below).
            string resolvedExitSummary = string.Empty;
            // Slice S8: grid-exit route summary for the registration log.
            // Empty when the shipment already existed or the route could not
            // be planned (non-gating — see below).
            string resolvedExitRouteSummary = string.Empty;
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
                    // Slice S4: honest transit duration from the live journey
                    // route (supplier site -> town-center, the loaded return
                    // leg, one-way). A missing site or unroutable pair is a
                    // registration failure — the agreement stays pending for
                    // daily retry. Never create with the 120 s default as a
                    // silent fallback: that would reintroduce the dishonesty
                    // this slice exists to remove.
                    string siteLocationId = null;
                    foreach (RegionalTownRegistry.RegionalTownDefinition town in RegionalTownRegistry.All)
                    {
                        if (town?.Sites == null) continue;
                        foreach (RegionalTownRegistry.RegionalBusinessSite site in town.Sites)
                        {
                            if (site != null && string.Equals(site.BusinessId, millbrookAidRuntime.BusinessId, StringComparison.Ordinal))
                            {
                                siteLocationId = RegionalTownRegistry.SiteLocationId(town, site);
                                break;
                            }
                        }
                        if (!string.IsNullOrWhiteSpace(siteLocationId)) break;
                    }
                    if (string.IsNullOrWhiteSpace(siteLocationId))
                    {
                        reason = $"Millbrook supplier '{millbrookAidRuntime.BusinessId}' has no registered regional site.";
                        return false;
                    }
                    if (!FreightProductionDriver.TryResolveTransitGameSeconds(
                            Journeys ?? hub.Journeys, siteLocationId, "town-center",
                            out float transitSeconds, out string routeReason))
                    {
                        reason = routeReason;
                        return false;
                    }
                    resolvedSiteLocationId = siteLocationId;
                    resolvedTransitSeconds = transitSeconds;
                    // Slice S7: resolve the regional exit point for the
                    // registration log. NON-GATING by design: an
                    // exit-resolution failure is warned, never a registration
                    // failure — nothing consumes the exit point yet, so
                    // refusing a shipment over an unconsumed diagnostic
                    // would block flour delivery for a log line.
                    // Slice S8: the exit point is kept for the route planning
                    // below — the S7 resolver leaves HasGridExit false, and
                    // the S8 call fills that slot.
                    RegionalExitPoint exitPoint = null;
                    if (RegionalExitPointResolver.TryResolve(
                            Journeys ?? hub.Journeys, "town-center", siteLocationId,
                            out RegionalExitPoint resolvedExitPoint, out string exitReason))
                    {
                        exitPoint = resolvedExitPoint;
                        resolvedExitSummary = $"exit={exitPoint.GatewayLocationId} {exitPoint.GatewayRouteMiles:F1} mi " +
                            $"{exitPoint.GatewayTravelDays}d + {exitPoint.OffMapLegDays}d off-map, due-back +{exitPoint.DueReturnDayOffset}d";
                    }
                    else
                    {
                        Debug.LogWarning($"[FreightAgreement] Exit point unresolved for shipment '{stableShipmentId}': {exitReason}", this);
                    }
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
                        holdForDispatchAuthorization: true, transitDurationGameSeconds: transitSeconds);
                    if (created == null || created.IsPreLoadBlocked) return false;
                    // Slice S8: plan the staging-to-edge exit route for the
                    // registration log and for the Objective 23 movement
                    // controller. NON-GATING by design, like the S7 exit
                    // resolution: the route is not consumed by anything in
                    // S8, so a planning failure must never block flour
                    // delivery. Guarded on exitPoint != null — the S7
                    // resolution may have refused, in which case there is no
                    // bearing to select on; a missing exit point is a missing
                    // input, not a reason to invent one.
                    if (exitPoint != null)
                    {
                        LogisticsRuntimeManager logisticsRuntime = FindAnyObjectByType<LogisticsRuntimeManager>();
                        if (logisticsRuntime == null)
                        {
                            Debug.LogWarning($"[FreightAgreement] Exit route unplanned for shipment '{stableShipmentId}': logistics runtime unavailable.", this);
                        }
                        // Slice F1: plan via the carrier method, which
                        // resolves staging with presentation parity (B3
                        // anchor -> B3 plot -> operator home anchor ->
                        // operator home plot, each in-bounds and a road
                        // cell). NON-GATING, as in S8: a planning failure
                        // is warned, never a registration failure.
                        else if (logisticsRuntime.TryPlanFreightExitRouteForCarrier(
                            carrier,
                            populationManager?.State?.GetPerson(accepted.AcceptingOperatorPersonId)?.homeBuildingId ?? -1,
                            exitPoint, out LogisticsRoutePlan exitPlan, out string exitRouteReason,
                            out GridCoord stagingCell, out string stagingSource))
                        {
                            created.routePlan = exitPlan;
                            exitPoint.GridExitCoord = exitPlan.VisibleEnd;
                            exitPoint.HasGridExit = true;
                            resolvedExitRouteSummary = $", staging=({stagingCell.x}, {stagingCell.z}) via {stagingSource}, grid-exit=({exitPlan.VisibleEnd.x}, {exitPlan.VisibleEnd.z}) " +
                                $"{exitPlan.VisiblePath?.Count ?? 0} cells {exitPlan.VisibleTravelGameSeconds:F0}s";
                        }
                        else
                        {
                            Debug.LogWarning($"[FreightAgreement] Exit route unplanned for shipment '{stableShipmentId}': {exitRouteReason}", this);
                        }
                    }
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
            // Slice S4: name the route and duration. When this call resolved
            // the route, both numbers are shown; for a recovered shipment the
            // duration is read off the shipment itself (set at its creation).
            string transitSummary = !string.IsNullOrWhiteSpace(resolvedSiteLocationId) && resolvedTransitSeconds > 0f
                ? $"transit={resolvedTransitSeconds:F0}s (route {resolvedSiteLocationId}->town-center, quote one-way={request.Quote?.OneWayDistanceMiles ?? 0f:F1} mi)"
                : $"transit={shipment.transitDurationGameSeconds:F0}s";
            // Slice S7: name the exit when this call resolved it; for a
            // recovered shipment the exit was resolved at its creation and
            // is not re-derived here. Slice S8 appends the grid-exit route
            // (exit cell, visible cell count, grid-leg seconds) when this
            // call planned it.
            string exitSummary = !string.IsNullOrWhiteSpace(resolvedExitSummary) ? ", " + resolvedExitSummary + resolvedExitRouteSummary : string.Empty;
            Debug.Log($"[FreightAgreement] {(alreadyRegistered ? "Recovered existing" : "Registered")} shipment '{shipment.ShipmentId}' " +
                $"for accepted agreement '{linked.AgreementId}' ({quoteSummary}); lot='{shipment.CargoLotId}', " +
                $"status={shipment.Status}, dispatch hold={shipment.requiresDispatchAuthorization}, {transitSummary}{exitSummary}, " +
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
                if (!trip.Batch.HasValidCustodyIdentity)
                {
                    // JsonUtility can materialize an empty nested Batch when
                    // the refused checkout serialized a null batch on the
                    // return task. Never submit that placeholder to the
                    // receipt authority or custody-retry ledger.
                    Debug.Log($"[BusinessFeed] Ethan P{ethan.id} returned from B0 without a valid purchased-feed batch; " +
                        $"B3 '{trip.BusinessId}' need remains open and no receipt was attempted.", this);
                    return;
                }
                int stockBefore = buyer.RuntimeState.GetRetailItemStockUnits(HorseFeedSku, HorseFeedCategory);
                BusinessFeedPurchase.Result receipt = BusinessFeedPurchase.ReceiveAtBusiness(trip.Batch, buyer.RuntimeState);
                if (!receipt.Succeeded)
                {
                    // W4 (F-1): the batch is genuinely retained now — held in
                    // the pending-receipt custody ledger keyed by its
                    // TransactionId, for retry on the next feed-drive day.
                    // Before W4 this log line claimed retention while the
                    // batch died with the completed return task.
                    bool held = pendingFeedReceipts.TryHold(trip.Batch);
                    Debug.LogWarning($"[BusinessFeed] B3 receipt refused after Ethan returned; custody batch '{trip.TransactionId}' " +
                        (held ? "retained in the pending-receipt custody ledger for retry on the next feed drive: "
                            : "could NOT be retained in the pending-receipt custody ledger (duplicate or missing transaction id): ") +
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
            if (horse == null || wagon == null || wagon.OwnerKind != "business" || wagon.OwnerId != freight.InstanceId) return;

            FreightServiceAgreement agreement = FreightProduction?.ServiceAgreements
                ?.FindNextAwaitingPreparation(FreightProduction.AllShipments());
            if (agreement == null
                || !string.Equals(agreement.CarrierBusinessId, freight.InstanceId, StringComparison.Ordinal)) return;
            LogisticsShipmentState shipment = FreightProduction.AllShipments().FirstOrDefault(candidate => candidate != null
                && string.Equals(candidate.ShipmentId, agreement.ShipmentId, StringComparison.Ordinal));
            if (shipment == null || shipment.Status != LogisticsShipmentStatus.Planned
                || !shipment.requiresDispatchAuthorization
                || !string.Equals(shipment.freightServiceAgreementId, agreement.AgreementId, StringComparison.Ordinal)) return;

            // A completed freight run releases the team's shipment reservation,
            // but the horse remains physically hitched to its wagon. Requiring
            // another hitch task in that state strands every later accepted
            // shipment at the early-return above. Reuse the existing physical
            // attachment only when both durable identities, ownership, health,
            // and equipment readiness still validate; TaskAuthority will then
            // create/schedule the new shipment's ordinary dispatch task.
            bool alreadyAttached = horse.TransportState == HorseTransportState.AttachedToWagon
                && string.Equals(horse.AttachedWagonAssetId, wagon.AssetId, StringComparison.Ordinal)
                && wagon.HitchingAnimalIds != null
                && wagon.HitchingAnimalIds.Contains(horse.AnimalId.ToString());
            if (alreadyAttached)
            {
                if (!HorseAuthority.IsWorkReady(horse) || !wagon.IsUsable
                    || wagon.IsTransportAssigned || wagon.IsReserved
                    || !string.IsNullOrWhiteSpace(horse.CurrentAssignmentId)
                    || horse.CurrentUse != HorseUseKind.None)
                {
                    LogFreightAgreementBlockerOnce("attached-team-not-ready:" + shipment.ShipmentId,
                        $"[Hitching] Shipment '{shipment.ShipmentId}' waits to reuse the attached A0/wagon team: horse or wagon is not currently available and work-ready.");
                    return;
                }

                string reusedPreparationRefusal = FreightProduction.ServiceAgreements.MarkPreparedForDispatch(
                    agreement.AgreementId, shipment.ShipmentId, AbsoluteNowGameSeconds());
                if (reusedPreparationRefusal != null)
                {
                    LogFreightAgreementBlockerOnce("attached-team-prepare:" + shipment.ShipmentId,
                        $"[Hitching] Shipment '{shipment.ShipmentId}' retains its accepted state; attached-team preparation was refused: {reusedPreparationRefusal}");
                    return;
                }

                // Dispatch start validates this durable marker against the
                // shipment. It used to retain the previous trip's ID after
                // the terminal draft-team reservation was released, so the
                // otherwise valid second task remained queued forever.
                // Hitch-task fields are cleared because no new hitch operation
                // was performed; the team remains physically attached.
                wagon.HitchPreparationAttempted = false;
                wagon.HitchPreparationTaskId = string.Empty;
                wagon.HitchPreparationTravelTaskId = string.Empty;
                wagon.HitchPreparationShipmentId = shipment.ShipmentId;

                Debug.Log($"[Hitching] Reused already attached, available A0 '{horse.AnimalId}' and wagon '{wagon.AssetId}' " +
                    $"for accepted shipment '{shipment.ShipmentId}'; no duplicate hitch operation or equipment identity was created.", this);
                CreatePreparedFreightDispatchTask(agreement, shipment, freight, horse, wagon, absoluteDayIndex);
                return;
            }

            if (wagon.HitchingAnimalIds == null || wagon.HitchingAnimalIds.Contains(horse.AnimalId.ToString())) return;
            if (wagon.HitchPreparationAttempted
                && string.Equals(wagon.HitchPreparationShipmentId, shipment.ShipmentId, StringComparison.Ordinal))
            {
                // Queued by an earlier run (possibly before this fix sent
                // Ethan to staging): make sure the arrival-driven travel
                // exists rather than waiting on a coincidence again.
                EnsureOpeningHitchTravel(absoluteDayIndex, freight, wagon, shipment.ShipmentId);
                return;
            }
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

            // Overnight 2026-11-04: queuing no longer waits for the
            // coincidence of Ethan already being at staging. The hitch
            // work itself still requires him visually at staging when it
            // starts (the ShortTick starter keeps that gate), but when he
            // is elsewhere an arrival-driven travel task — the variant
            // the completion handler already understands — now brings
            // him there. Previously the driver returned silently here,
            // so an agreement accepted on day 3 could sit until day 15
            // waiting for Ethan's autonomous day to happen to leave him
            // idle at home with a free window. No priority change: the
            // travel task competes for Ethan's time like any other task,
            // and the hitch task still needs its work window plus horse
            // and wagon reservations before it starts.
            if (!IsPersonVisuallyAtBuilding(ethan.id, ethan.homeBuildingId))
                EnsureOpeningHitchTravel(absoluteDayIndex, freight, wagon, shipment.ShipmentId);
        }

        /// <summary>
        /// Overnight 2026-11-04: ensures one arrival-driven travel task
        /// exists to bring Ethan to A0/wagon staging for an already-queued
        /// hitch preparation. The wagon's HitchPreparationTravelTaskId is
        /// the duplicate guard and the cancellation monitor's handle: a
        /// cancelled or failed travel task resets the whole preparation
        /// through MonitorOpeningHitchCancellation, so a fresh attempt can
        /// be queued again. W3 (H1/Note-4): there is NO early return when
        /// the journey location already says staging — the location can
        /// read as staging while the visual agent is idle elsewhere, and
        /// then no travel task would ever be created and the hitch would
        /// never start. A travel task is still created in that case
        /// (TryStartExplicitRoute is a no-op success when already arrived,
        /// per the S5 precedent in EnsureFreightDispatchStagingTravel).
        /// </summary>
        private void EnsureOpeningHitchTravel(int dayIndex, BusinessInstanceState freight, EquipmentAsset wagon, string shipmentId)
        {
            if (hub?.Tasks == null || populationManager?.State == null || freight?.Owner == null || freight.Owner.PersonId < 0
                || wagon == null || string.IsNullOrWhiteSpace(wagon.HitchPreparationTaskId)
                || string.IsNullOrWhiteSpace(shipmentId)) return;
            PersonState ethan = populationManager.State.GetPerson(freight.Owner.PersonId);
            if (ethan == null || ethan.homeBuildingId < 0) return;
            if (IsPersonVisuallyAtBuilding(ethan.id, ethan.homeBuildingId)) return;
            bool hasOpenTravelTask = false;
            if (!string.IsNullOrWhiteSpace(wagon.HitchPreparationTravelTaskId))
            {
                WorkTask existingTravel = hub.Tasks.FindTaskByIdString(wagon.HitchPreparationTravelTaskId);
                if (existingTravel != null && existingTravel.IsOpen) hasOpenTravelTask = true;
                else wagon.HitchPreparationTravelTaskId = string.Empty;
            }

            string staging = TownJourneyNetworkBuilder.LocationIdForBuilding(ethan.homeBuildingId);
            string origin = ResolvePersonJourneyLocation(ethan.id);
            if (string.IsNullOrWhiteSpace(staging) || hub.Journeys.GetLocation(staging) == null) return;
            // W3: the journey location reading as staging does not mean
            // Ethan is visually there — the decision is the shared staging
            // decision (visual + open-travel only). The visual early-return
            // above already established isVisuallyAtStaging=false.
            if (!FreightDispatchStaging.NeedsHitchStagingTravel(false, hasOpenTravelTask, origin, staging)) return;
            JourneyRoute route = string.IsNullOrWhiteSpace(origin)
                ? null : hub.Journeys.FindRoute(origin, staging, TravelMode.Foot);
            if (route == null || !route.Found)
            {
                LogFreightAgreementBlockerOnce("hitch-travel-route:" + shipmentId,
                    $"[Hitching] Shipment '{shipmentId}' cannot send Ethan to staging: no route from '{origin}' to '{staging}'.");
                return;
            }

            var trip = new DevelopmentHitchTaskData
            {
                WagonId = wagon.AssetId,
                HitchTaskId = wagon.HitchPreparationTaskId,
                PersonId = ethan.id,
                StagingBuildingId = ethan.homeBuildingId,
                StagingLocationId = staging,
                DurationMinutes = Math.Max(1, route.TotalMinutes),
            };
            WorkTask travel = hub.Tasks.CreateTaskWithPlannedMinutes(JourneyTravel.TravelTaskId,
                EntityId.For(EntityKind.Business, StableBusinessRuntimeId(freight.InstanceId)), dayIndex,
                Math.Max(1, route.TotalMinutes),
                customerRef: DevelopmentHitchTaskPrefix + "travel:" + shipmentId,
                termsRef: JsonUtility.ToJson(trip));
            wagon.HitchPreparationTravelTaskId = travel.TaskId.ToString();
            if (!TryStartBusinessPersonTask(travel, ethan, dayIndex, PersonActivityKind.Travel, ethan.homeBuildingId, out string refusal))
            {
                LogRefusalDayKeyed("hitch-travel:" + travel.TaskId, dayIndex,
                    $"[Hitching] Ethan's staging travel for shipment '{shipmentId}' could not reserve time: {refusal}");
                return;
            }
            Debug.Log($"[Hitching] Ethan P{ethan.id} assigned staging travel task '{travel.TaskId}' to building {ethan.homeBuildingId} " +
                $"for shipment '{shipmentId}'; the hitch task starts only after visible arrival.", this);
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

            // Slice F2: daily hitch-state line while the hitch is open and
            // uncompleted — task status, remaining minutes, reservation
            // state — so a stall that persists for days stays visible
            // instead of going silent after day 1.
            if (hitchStateLogDayIndex != dayIndex
                && (hitch.Status == TaskStatus.Queued || hitch.Status == TaskStatus.Assigned))
            {
                hitchStateLogDayIndex = dayIndex;
                PersonActivityReservation stateReservation = hub.PersonSchedules.ActiveFor(ethan.id, dayIndex)
                    .FirstOrDefault(reservation => reservation != null
                        && string.Equals(reservation.Label, TaskReservationLabel(hitch), StringComparison.Ordinal));
                string reservationWindow = stateReservation != null
                    ? $"[{stateReservation.StartMinuteOfDay}-{stateReservation.EndMinuteOfDay}]"
                    : "none";
                bool visuallyAtStaging = IsPersonVisuallyAtBuilding(ethan.id, ethan.homeBuildingId);
                Debug.Log($"[Hitching] day {dayIndex} state: task='{hitch.TaskId}' status={hitch.Status} " +
                    $"remaining={hitch.MinutesRemaining}min reservation={reservationWindow} " +
                    $"visually-at-staging={visuallyAtStaging}.", this);
            }

            // Overnight 2026-11-04: while staging travel is the current
            // phase, drive it (mirroring the freight-meeting travel
            // starter) and leave the hitch task alone until arrival.
            if (TryStartQueuedOpeningHitchTravel(dayIndex, tickStartMinute, tickEndMinute, wagon, ethan)) return;

            if (hitch.Status != TaskStatus.Queued && hitch.Status != TaskStatus.Assigned) return;
            if (!IsPersonVisuallyAtBuilding(ethan.id, ethan.homeBuildingId))
            {
                // Slice F2: day-keyed, not silent. This is the Note-4 trap
                // signature — journey location says staging (so no travel
                // task is created) while the visual agent is elsewhere —
                // so it must stay visible while it persists.
                LogRefusalDayKeyed("away:" + hitch.TaskId, dayIndex,
                    $"[Hitching] Task '{hitch.TaskId}' waits: Ethan P{ethan.id} is not visually at staging " +
                    $"(home building {ethan.homeBuildingId}); journey location is '{ResolvePersonJourneyLocation(ethan.id)}'.");
                return;
            }
            int nowMinute = timeManager != null ? Mathf.FloorToInt(timeManager.TimeOfDay01 * 1440f) : tickStartMinute;
            PersonActivityReservation scheduled = hub.PersonSchedules.ActiveFor(ethan.id, dayIndex)
                .FirstOrDefault(reservation => reservation != null
                    && string.Equals(reservation.Label, TaskReservationLabel(hitch), StringComparison.Ordinal));
            int startMinute = scheduled != null ? scheduled.StartMinuteOfDay
                : FindAvailablePersonStartMinute(ethan.id, dayIndex, nowMinute, Math.Max(1, hitch.MinutesRemaining));
            if (startMinute < 0)
            {
                // Slice F2: day-keyed, with the §6 probe numbers — Ethan's
                // capacity factor and remaining budget minutes — so a
                // no-window stall names its cause instead of returning
                // silently.
                float capacityFactor = DailyNeedsService.WorkCapacityFactor(
                    ethan, populationManager != null && populationManager.ConditionDebuffsSuppressed);
                WorkTimeBudget budget = hub.WorkTimeBudgets?.GetOrCreate(EntityId.For(EntityKind.Person, ethan.id));
                LogRefusalDayKeyed("no-window:" + hitch.TaskId, dayIndex,
                    $"[Hitching] Task '{hitch.TaskId}' has no free work window on day {dayIndex} " +
                    $"(needs {Math.Max(1, hitch.MinutesRemaining)}min); Ethan P{ethan.id} capacity={capacityFactor:0.00}, " +
                    $"budget remaining={budget?.MinutesRemaining ?? -1} of {budget?.EffectiveBudgetMinutes ?? -1}min.");
                return;
            }
            if (startMinute > tickEndMinute) return;

            AnimalState horse = FindOpeningFreightHorse(freight.InstanceId);
            if (horse == null || !HorseAuthority.IsWorkReady(horse))
            {
                // Slice F2 (H2): no threshold change — log the exact
                // failing metric, day-keyed, so an unfit horse stays
                // visible as the blocker.
                LogRefusalDayKeyed("horse-unfit:" + hitch.TaskId, dayIndex,
                    horse == null
                        ? $"[Hitching] Task '{hitch.TaskId}' waits: no B3 horse resolves for the hitch."
                        : $"[Hitching] Task '{hitch.TaskId}' waits: A0 is not work-ready " +
                            $"(health={horse.Health01:0.00}, nutrition={horse.Nutrition01:0.00}, " +
                            $"hydration={horse.Hydration01:0.00}, fatigue={horse.Fatigue01:0.00}, " +
                            $"housing='{horse.HousingId}').");
                return;
            }
            float startSeconds = AbsoluteNowGameSeconds() + Math.Max(0, startMinute - nowMinute) * 60f;
            float endSeconds = startSeconds + Math.Max(1, hitch.MinutesRemaining) * 60f;
            var availability = new AnimalAvailabilityAuthority(hub.ReservationAuthority);
            if (horse.CurrentAssignmentId != hitch.TaskId.ToString())
            {
                string horseRefusal = HorseAuthority.TryReserveUse(horse, HorseUseKind.Draft,
                    hitch.TaskId.ToString(), availability, startSeconds, endSeconds, out _);
                if (horseRefusal != null)
                {
                    // Slice F2 (H4): day-keyed, naming the competing holder
                    // (the refusal itself does not name it).
                    LogRefusalDayKeyed("horse:" + hitch.TaskId, dayIndex,
                        $"[Hitching] Task '{hitch.TaskId}' remains queued; A0 reservation refused: {horseRefusal} " +
                        $"(holder: use={horse.CurrentUse}, assignment='{horse.CurrentAssignmentId}').");
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
                    LogRefusalDayKeyed("wagon:" + hitch.TaskId, dayIndex,
                        $"[Hitching] Task '{hitch.TaskId}' remains queued; wagon reservation refused: {wagonRefusal}.");
                    return;
                }
                wagon.HitchPreparationReservationId = wagonHold?.ReservationId ?? string.Empty;
            }

            bool wasQueued = hitch.Status == TaskStatus.Queued;
            if (!TryStartBusinessPersonTask(hitch, ethan, dayIndex, PersonActivityKind.Work, -1, out string prepareRefusal))
            {
                ReleaseOpeningHitchReservations(horse, wagon);
                LogRefusalDayKeyed("schedule:" + hitch.TaskId, dayIndex,
                    $"[Hitching] Task '{hitch.TaskId}' retained queued until Ethan has a valid work opening: {prepareRefusal}");
                return;
            }
            if (wasQueued && hitch.Status == TaskStatus.Assigned)
                Debug.Log($"[Hitching] Ethan P{ethan.id} assigned hitch task '{hitch.TaskId}' at minute {startMinute}; " +
                    $"HorseAuthority reserved A0 and ReservationAuthority reserved wagon '{wagon.AssetId}'.", this);
            if (!BeginScheduledBusinessPersonTask(hitch, ethan, dayIndex, tickStartMinute,
                    tickEndMinute, -1, out string startRefusal))
            {
                ReleaseOpeningHitchReservations(horse, wagon);
                LogRefusalDayKeyed("start:" + hitch.TaskId, dayIndex,
                    $"[Hitching] Task '{hitch.TaskId}' remains pending; start refused: {startRefusal}");
            }
        }

        /// <summary>
        /// Overnight 2026-11-04: ShortTick driver for the staging travel
        /// task created by EnsureOpeningHitchTravel. Returns true when a
        /// travel task for this hitch is the current phase (handled this
        /// tick, whether it started, is waiting on its window, or was
        /// refused), so the caller leaves the hitch task alone until
        /// arrival. Returns false when no travel task applies — including
        /// when Ethan is already visually at staging, so a stale travel
        /// record can never block the hitch start. Mirrors the
        /// freight-meeting travel starter exactly: reserve, then begin
        /// only inside the tick window.
        /// </summary>
        private bool TryStartQueuedOpeningHitchTravel(int dayIndex, int tickStartMinute, int tickEndMinute, EquipmentAsset wagon, PersonState ethan)
        {
            if (hub?.Tasks == null || wagon == null || ethan == null
                || string.IsNullOrWhiteSpace(wagon.HitchPreparationTaskId)
                || IsPersonVisuallyAtBuilding(ethan.id, ethan.homeBuildingId)) return false;
            foreach (WorkTask task in hub.Tasks.GetAllOpenTasks())
            {
                if (task == null || (task.Status != TaskStatus.Queued && task.Status != TaskStatus.Assigned)
                    || !task.CustomerRef.StartsWith(DevelopmentHitchTaskPrefix + "travel:", StringComparison.Ordinal)) continue;
                DevelopmentHitchTaskData trip = JsonUtility.FromJson<DevelopmentHitchTaskData>(task.TermsRef);
                if (trip == null || !string.Equals(trip.HitchTaskId, wagon.HitchPreparationTaskId, StringComparison.Ordinal)) continue;
                if (!TryStartBusinessPersonTask(task, ethan, dayIndex, PersonActivityKind.Travel, trip.StagingBuildingId, out string prepareRefusal))
                {
                    LogRefusalDayKeyed("travel:" + task.TaskId, dayIndex,
                        $"[Hitching] Ethan's staging travel task '{task.TaskId}' could not reserve time: {prepareRefusal}");
                    return true;
                }
                if (!BeginScheduledBusinessPersonTask(task, ethan, dayIndex, tickStartMinute, tickEndMinute, trip.StagingBuildingId, out string startRefusal))
                {
                    LogRefusalDayKeyed("travel:" + task.TaskId, dayIndex,
                        $"[Hitching] Ethan's staging travel task '{task.TaskId}' could not start: {startRefusal}");
                    return true;
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Slice S5: ensures one arrival-driven travel task exists to bring
        /// Ethan to staging (his home building, the code's existing staging
        /// definition) for an already-queued dispatch task. The duplicate
        /// guard is a scan of open tasks for the helper's travel ref — no
        /// new serialized field, so the EquipmentAsset save seam is
        /// untouched. Unlike EnsureOpeningHitchTravel, there is NO early
        /// return when the journey location already says staging (Note-4):
        /// if the journey location says staging but the visual agent never
        /// routed there, a travel task is still created — TryStartExplicitRoute
        /// is a no-op success when he is already arrived
        /// (PopulationPathingDirector.cs:697-701), so the extra task is
        /// harmless and the stuck case is closed.
        /// </summary>
        private void EnsureFreightDispatchStagingTravel(int dayIndex, FreightServiceAgreement agreement,
            LogisticsShipmentState shipment, BusinessInstanceState freight, EquipmentAsset wagon)
        {
            if (hub?.Tasks == null || populationManager?.State == null || agreement == null || shipment == null
                || freight?.Owner == null || freight.Owner.PersonId < 0 || wagon == null) return;
            PersonState ethan = populationManager.State.GetPerson(freight.Owner.PersonId);
            if (ethan == null || !FreightDispatchStaging.CanCreateTravel(ethan.homeBuildingId)) return;
            if (IsPersonVisuallyAtBuilding(ethan.id, ethan.homeBuildingId)) return;
            string travelRef = FreightDispatchStaging.TravelCustomerRef(agreement.AgreementId, shipment.ShipmentId);
            foreach (WorkTask open in hub.Tasks.GetAllOpenTasks())
            {
                if (open != null && string.Equals(open.CustomerRef, travelRef, StringComparison.Ordinal)) return;
            }

            string staging = TownJourneyNetworkBuilder.LocationIdForBuilding(ethan.homeBuildingId);
            string origin = ResolvePersonJourneyLocation(ethan.id);
            if (string.IsNullOrWhiteSpace(staging) || hub.Journeys.GetLocation(staging) == null) return;
            JourneyRoute route = string.IsNullOrWhiteSpace(origin)
                ? null : hub.Journeys.FindRoute(origin, staging, TravelMode.Foot);
            if (route == null || !route.Found)
            {
                LogFreightAgreementBlockerOnce("dispatch-travel-route:" + shipment.ShipmentId,
                    $"[FreightDispatch] Shipment '{shipment.ShipmentId}' cannot send Ethan to staging: no route from '{origin}' to '{staging}'.");
                return;
            }

            var trip = new FreightDispatchTravelTaskData
            {
                AgreementId = agreement.AgreementId,
                ShipmentId = shipment.ShipmentId,
                OperatorPersonId = ethan.id,
                StagingBuildingId = ethan.homeBuildingId,
                StagingLocationId = staging,
                DurationMinutes = Math.Max(1, route.TotalMinutes),
            };
            WorkTask travel = hub.Tasks.CreateTaskWithPlannedMinutes(JourneyTravel.TravelTaskId,
                EntityId.For(EntityKind.Business, StableBusinessRuntimeId(freight.InstanceId)), dayIndex,
                Math.Max(1, route.TotalMinutes),
                customerRef: travelRef,
                termsRef: JsonUtility.ToJson(trip));
            if (!TryStartBusinessPersonTask(travel, ethan, dayIndex, PersonActivityKind.Travel, ethan.homeBuildingId, out string refusal))
            {
                if (taskStartRefusalAlreadyLogged.Add("dispatch-travel:" + travel.TaskId))
                    Debug.LogWarning($"[FreightDispatch] Ethan's staging travel for shipment '{shipment.ShipmentId}' could not reserve time: {refusal}", this);
                return;
            }
            Debug.Log($"[FreightDispatch] Ethan P{ethan.id} assigned staging travel task '{travel.TaskId}' to building {ethan.homeBuildingId} " +
                $"for shipment '{shipment.ShipmentId}'; the dispatch task starts only after visible arrival.", this);
        }

        /// <summary>
        /// Slice S5: ShortTick driver for the staging travel task created by
        /// EnsureFreightDispatchStagingTravel. Returns true when a travel
        /// task for this dispatch is the current phase (handled this tick,
        /// whether it started, is waiting on its window, or was refused),
        /// so the caller leaves the dispatch work task alone until arrival.
        /// Returns false when no travel task applies — including when Ethan
        /// is already visually at staging, so a stale travel record can
        /// never block the dispatch start. Mirrors
        /// TryStartQueuedOpeningHitchTravel exactly.
        /// </summary>
        private bool TryStartQueuedFreightDispatchTravel(int dayIndex, int tickStartMinute, int tickEndMinute,
            FreightServiceAgreement agreement, LogisticsShipmentState shipment, PersonState ethan)
        {
            if (hub?.Tasks == null || agreement == null || shipment == null || ethan == null
                || IsPersonVisuallyAtBuilding(ethan.id, ethan.homeBuildingId)) return false;
            string travelRef = FreightDispatchStaging.TravelCustomerRef(agreement.AgreementId, shipment.ShipmentId);
            foreach (WorkTask task in hub.Tasks.GetAllOpenTasks())
            {
                if (task == null || (task.Status != TaskStatus.Queued && task.Status != TaskStatus.Assigned)
                    || !string.Equals(task.CustomerRef, travelRef, StringComparison.Ordinal)) continue;
                FreightDispatchTravelTaskData trip = JsonUtility.FromJson<FreightDispatchTravelTaskData>(task.TermsRef);
                if (trip == null || !string.Equals(trip.AgreementId, agreement.AgreementId, StringComparison.Ordinal)
                    || !string.Equals(trip.ShipmentId, shipment.ShipmentId, StringComparison.Ordinal)) continue;
                if (!TryStartBusinessPersonTask(task, ethan, dayIndex, PersonActivityKind.Travel, trip.StagingBuildingId, out string prepareRefusal))
                {
                    if (taskStartRefusalAlreadyLogged.Add(task.TaskId.ToString()))
                        Debug.LogWarning($"[FreightDispatch] Ethan's staging travel task '{task.TaskId}' could not reserve time: {prepareRefusal}", this);
                    return true;
                }
                if (!BeginScheduledBusinessPersonTask(task, ethan, dayIndex, tickStartMinute, tickEndMinute, trip.StagingBuildingId, out string startRefusal))
                {
                    if (taskStartRefusalAlreadyLogged.Add(task.TaskId.ToString()))
                        Debug.LogWarning($"[FreightDispatch] Ethan's staging travel task '{task.TaskId}' could not start: {startRefusal}", this);
                    return true;
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// Slice S5: completion handler for the dispatch staging travel
        /// task. Mirrors the hitch travel branch of
        /// HandleCompletedOpeningHitchTask without touching the horse — the
        /// horse stays attached from the hitch. Only sets Ethan's journey
        /// location to staging and logs the arrival. No-ops for every other
        /// task ref.
        /// </summary>
        private void HandleCompletedFreightDispatchTravelTask(WorkTask task, int dayIndex)
        {
            if (task == null || !FreightDispatchStaging.IsDispatchTravelRef(task.CustomerRef)) return;
            FreightDispatchTravelTaskData trip = JsonUtility.FromJson<FreightDispatchTravelTaskData>(task.TermsRef);
            PersonState ethan = trip != null ? populationManager?.State?.GetPerson(trip.OperatorPersonId) : null;
            if (trip == null || ethan == null || string.IsNullOrWhiteSpace(trip.StagingLocationId)) return;
            SetPersonJourneyLocation(ethan.id, trip.StagingLocationId);
            Debug.Log($"[FreightDispatch] Ethan P{ethan.id} reached staging after task '{task.TaskId}' " +
                $"at game minute {Mathf.FloorToInt(AbsoluteNowGameSeconds() / 60f)}; dispatch may start on arrival.", this);
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
                string operatorEntityId = operatorId >= 0
                    ? EntityId.For(EntityKind.Person, operatorId).ToString()
                    : string.Empty;
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
                    && string.Equals(wagon.CurrentDriverPersonId, operatorEntityId, StringComparison.Ordinal)
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

                // Slice S5: the dispatch work task starts only with Ethan
                // visually at staging (his home building). While he is
                // elsewhere, drive the arrival-driven travel task — created
                // below on first need — and leave the dispatch task queued.
                // The travel task is the current phase, so the work task is
                // left alone until arrival lands.
                if (TryStartQueuedFreightDispatchTravel(dayIndex, tickStartMinute, tickEndMinute,
                        agreement, shipment, operatorPerson)) continue;
                if (!IsPersonVisuallyAtBuilding(operatorId, operatorPerson.homeBuildingId))
                {
                    EnsureFreightDispatchStagingTravel(dayIndex, agreement, shipment, freight, wagon);
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
        /// <summary>
        /// Plan 05 Slice 4 (rev1 section 5b): the moved assignment loop.
        /// Runs ahead of DriveDailyNeeds in OnDayChanged so shopping sees
        /// the day's real Work reservations. Per business it is exactly the
        /// blanket pass DriveAutonomousLabor used to run last: open Queued
        /// tasks + the AddLaborWorker pool through WorkScheduler.
        /// AssignDailyWork + ReserveAndAssignTasks — SPLIT: reserve + assign
        /// only; the start stays late (StartPreAssignedBusinessWork, in
        /// DriveAutonomousLabor's per-business position), because the
        /// shopping loop starts each trip leg as a task for the shopper and
        /// StartTask refuses when the person already holds an InProgress
        /// task. The worker-availability seam is installed first, as
        /// DriveAutonomousLabor does. Tasks that fail to start here stay
        /// Queued and may still be picked up by the in-place manager
        /// Schedule executor later in the day (same two-call structure as
        /// before the move, blanket call moved earlier). This method is
        /// harness-excluded with the rest of SimulationDrivers.cs: verified
        /// by harness-reachable component tests (loop + scheduler drive)
        /// plus source-review, and the live order is Unity-owed.
        /// </summary>
        private void DriveWorkAssignmentBeforeShopping(int absoluteDayIndex)
        {
            if (hub == null || sharedBusinessRuntime == null)
            {
                return;
            }

            EnsureWorkerAvailabilitySeam();
            var diagnostics = new List<string>();
            foreach (BusinessInstanceState business in sharedBusinessRuntime.Businesses)
            {
                if (business == null || string.IsNullOrWhiteSpace(business.InstanceId))
                {
                    continue;
                }

                ReserveAndAssignBusinessWork(business, absoluteDayIndex, diagnostics);
            }

            if (diagnostics.Count > 0)
            {
                Debug.LogWarning("[SimulationDrivers] work assignment before shopping: " + string.Join(" | ", diagnostics));
            }
        }

        /// <summary>
        /// One business's blanket assignment pass: open Queued tasks and the
        /// AddLaborWorker pool (budget-derived availability) through
        /// WorkScheduler.AssignDailyWork, then ReserveAndAssignTasks
        /// (reserve + assign only — the start is StartPreAssignedBusinessWork,
        /// late, in DriveAutonomousLabor). Extracted verbatim from
        /// DriveAutonomousLabor (Plan 05 Slice 4); the manager-decision half
        /// of that drive is untouched and still runs there. No manager
        /// decision is made here: nothing in this pass reads household
        /// needs, shopping outcomes, store stock, or household cash
        /// (Slice 3 trace, questions (a')/(f')).
        /// </summary>
        private void ReserveAndAssignBusinessWork(BusinessInstanceState business, int absoluteDayIndex, List<string> diagnostics)
        {
            EntityId businessId = EntityId.For(EntityKind.Business, StableBusinessRuntimeId(business.InstanceId));
            List<WorkTask> openTasks = hub.Tasks.GetOpenTasks(businessId);
            if (openTasks == null || openTasks.Count == 0)
            {
                return;
            }

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
                if (task == null || !task.IsOpen || task.Status != TaskStatus.Queued)
                {
                    continue;
                }

                TaskDefinition definition = hub.Tasks.GetDefinition(task.DefinitionId);
                string family = definition?.DisplayName;
                if (string.IsNullOrWhiteSpace(family)) family = task.DefinitionId;
                schedulerWork.Add(new SchedulerOpenWorkItem
                {
                    TaskId = task.TaskId.ToString(),
                    Family = family,
                    MinutesRequired = Math.Max(1, task.MinutesRemaining),
                    RequiredEquipment = definition != null && definition.EquipmentClasses.Count > 0
                        ? definition.EquipmentClasses[0] : string.Empty,
                    LocationId = task.LocationId,
                });
                taskByTextId[task.TaskId.ToString()] = task;
            }

            if (schedulerWork.Count == 0 || workers.Count == 0)
            {
                return;
            }

            WorkAssignmentReport assignments = hub.WorkScheduling.AssignDailyWork(
                new SchedulerInputs
                {
                    BusinessInstanceId = business.InstanceId,
                    DayIndex = absoluteDayIndex,
                    OpenWork = schedulerWork,
                    Workers = workers,
                }, diagnostics);
            ReserveAndAssignBusinessTasks(assignments, taskByTextId, absoluteDayIndex, diagnostics);
        }

        private void ReserveAndAssignBusinessTasks(WorkAssignmentReport report, Dictionary<string, WorkTask> tasks, int dayIndex, List<string> diagnostics)
        {
            // Plan 05 Slice 4 — SPLIT half 1: reserve + assign only. The
            // assigned tasks are started late by StartPreAssignedBusinessWork.
            // One shared implementation with the manager Schedule executor
            // (ManagerWorkSchedulerDrive) — no drift.
            ManagerWorkSchedulerDrive.ReserveAndAssignTasks(
                report, tasks, hub.Tasks, hub.PersonSchedules, hub.WorkTimeBudgets,
                dayIndex, diagnostics, TaskReservationLabel,
                (taskId, reservationId) => autonomousTaskReservations[taskId] = reservationId,
                out _, out _);
        }

        /// <summary>
        /// Plan 05 Slice 4 — SPLIT half 2: starts the work reserved +
        /// assigned before shopping, in DriveAutonomousLabor's per-business
        /// position (where the blanket pass used to run). Runs after the
        /// trip legs have executed and completed synchronously during
        /// shopping, so the shopper's person-time is free again. On a start
        /// refusal the Work reservation is released and the task stays
        /// Assigned, exactly as the composed StartAssignedTasks behaved.
        /// </summary>
        private void StartPreAssignedBusinessWork(BusinessInstanceState business, int absoluteDayIndex, List<string> diagnostics)
        {
            EntityId businessId = EntityId.For(EntityKind.Business, StableBusinessRuntimeId(business.InstanceId));
            List<WorkTask> openTasks = hub.Tasks.GetOpenTasks(businessId);
            if (openTasks == null || openTasks.Count == 0)
            {
                return;
            }

            var assigned = new List<WorkTask>();
            foreach (WorkTask task in openTasks)
            {
                if (task != null && task.Status == TaskStatus.Assigned)
                {
                    assigned.Add(task);
                }
            }

            if (assigned.Count == 0)
            {
                return;
            }

            var reservationByTaskId = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<EntityId, string> entry in autonomousTaskReservations)
            {
                reservationByTaskId[entry.Key.ToString()] = entry.Value;
            }

            ManagerWorkSchedulerDrive.StartReservedTasks(
                assigned, reservationByTaskId, hub.Tasks, hub.PersonSchedules, absoluteDayIndex, diagnostics);
        }

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

                // Plan 05 Slice 4: the blanket assignment pass moved ahead
                // of DriveDailyNeeds (DriveWorkAssignmentBeforeShopping);
                // this loop now builds only the demand inputs for the
                // manager cycle. The manager Schedule executor below is the
                // in-place retry for work that failed to start there.
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
                    // Plan 05 Slice 4: no rebuild of schedulerWork here —
                    // the blanket assignment pass moved ahead of
                    // DriveDailyNeeds, and the manager Schedule executor
                    // above builds its own work list from fresh task state.
                    break;
                }

                // Plan 05 Slice 4 — SPLIT half 2: start the work reserved +
                // assigned before shopping, in this per-business position
                // (where the blanket pass used to run). The trip legs have
                // executed and completed by now, so the shopper is free.
                StartPreAssignedBusinessWork(business, absoluteDayIndex, diagnostics);
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
        /// Installs the worker-availability seam on the hub task authority
        /// (idempotent). Dead and actively off-map people are never offered
        /// town work at assignment or start, regardless of caller checks.
        /// </summary>
        private void EnsureWorkerAvailabilitySeam()
        {
            if (hub == null || hub.Tasks == null) return;
            hub.Tasks.IsWorkerUnavailable = worker =>
            {
                if (worker.Kind != LandLedgers.Primitives.EntityKind.Person) return false;
                if (populationManager?.State == null) return false;
                PersonState person = populationManager.State.GetPerson(worker.Id);
                return person == null || person.deathDayIndex >= 0
                    || OffMapJourneys.IsOffMapPerson(worker.Id);
            };
        }

        private void AddLaborWorker(int personId, HashSet<int> seen, List<SchedulerAvailableWorker> workers, int dayIndex)
        {
            if (personId < 0 || !seen.Add(personId)) return;
            // Dead or actively off-map persons are never included in the
            // scheduler's worker pool. This prevents a queued town task from
            // being repeatedly assigned to a freight operator who is still
            // on an active journey.
            if (populationManager?.State != null)
            {
                PersonState person = populationManager.State.GetPerson(personId);
                if (person == null || person.deathDayIndex >= 0
                    || OffMapJourneys.IsOffMapPerson(personId)) return;
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

        // W7 (2026-11-06): the dead due-event registry is gone —
        // RegisterDueEventHandler and the dueEventHandlers dictionary had
        // zero callers, so no worker ever registered a handler. Dispatch
        // is the built-in switch below, unchanged; an unhandled kind is
        // logged loudly, never silently skipped.

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
                // Successful due-event work is ordinary simulation activity,
                // not a warning. Keep each unexpected/refused transition loud.
                // In particular, do not route the DomesticLaborDriver's
                // successful "completed ..." summaries through Warning: at
                // accelerated speed that produced hundreds of false alarms.
                var failures = diag.Where(IsDueEventFailureDiagnostic).ToList();
                if (failures.Count > 0)
                    Debug.LogWarning("[SimulationDrivers] due-event failures: " + string.Join(" | ", failures));
            }
        }

        private static bool IsDueEventFailureDiagnostic(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;
            return message.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("could not", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("unknown", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("unavailable", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("skipped", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("no built-in handler", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("reached its due event after the task had already closed", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void DispatchDueEvent(DueEvent dueEvent, int dayIndex, List<string> diag)
        {
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
                        ", payload '" + dueEvent.PayloadRef + "') has no built-in handler — consumed without effect");
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
            if (task.Status == TaskStatus.Complete)
                HandleCompletedFreightDispatchTask(task);
        }

        /// <summary>
        /// Plans only demand already present in the Bakery's authored
        /// wholesale accounts. This does not choose a product or batch count.
        /// Actual production remains gated by configured equipment, fuel,
        /// ingredients, and the existing scheduled production authority.
        /// </summary>
        /// <summary>
        /// Plan 02 slice 2A: whether Millbrook freight can run, pinned to
        /// observable state — a shipment carrying a Millbrook lot has
        /// departed (InTransit or further). Until any such shipment has
        /// departed, freight is blocked (staging/T49 — Obj 16–18) and the
        /// comparison must rank Millbrook unavailable, never guess it
        /// deliverable. Source-reviewed only — SimulationDrivers is
        /// harness-excluded.
        /// </summary>
        private bool MillbrookFreightHasDeparted()
        {
            if (FreightProduction == null || millbrookAidRuntime == null) return false;
            string lotPrefix = "aidlot-" + millbrookAidRuntime.BusinessId + ":";
            foreach (LogisticsShipmentState shipment in FreightProduction.AllShipments())
            {
                if (shipment == null || string.IsNullOrWhiteSpace(shipment.CargoLotId)) continue;
                if (!shipment.CargoLotId.StartsWith(lotPrefix, StringComparison.Ordinal)) continue;
                if (shipment.Status == LogisticsShipmentStatus.InTransit
                    || shipment.Status == LogisticsShipmentStatus.Arrived
                    || shipment.Status == LogisticsShipmentStatus.Unloading
                    || shipment.Status == LogisticsShipmentStatus.Completed)
                    return true;
            }
            return false;
        }

        private void DriveBakeryWholesalePlanning(int dayIndex)
        {
            LandLedgers.Persistence.SaveLoadManager save = FindAnyObjectByType<LandLedgers.Persistence.SaveLoadManager>();
            Series2Pass9SaveState state = save != null ? save.Series2Pass9State : null;
            LandLedgers.Economy.Businesses.Bakery.BakeryShopRuntime bakery =
                state?.BakeryRuntime ?? state?.BakeryDriver?.Bakery;
            if (bakery == null) return;

            if (state.BakeryDriver == null)
            {
                LandLedgers.Economy.BreadDrivers.ISeries2BakeryFuelSource source = state.FuelSource
                    ?? (LandLedgers.Economy.BreadDrivers.ISeries2BakeryFuelSource)
                        new LandLedgers.Economy.BreadDrivers.Series2InStockBakeryFuelSource(bakery);
                state.BakeryDriver = new Series2BakeryProductionDriver(bakery, source);
            }
            else
            {
                LandLedgers.Economy.BreadDrivers.ISeries2BakeryFuelSource source = state.FuelSource
                    ?? (LandLedgers.Economy.BreadDrivers.ISeries2BakeryFuelSource)
                        new LandLedgers.Economy.BreadDrivers.Series2InStockBakeryFuelSource(bakery);
                state.BakeryDriver.AttachFuelSource(source);
            }

            // Plan 01 (2026-11-06, slice 1B): this method is a thin
            // forwarder over BakeryWholesaleDailyDriver. The old silent
            // return on an empty wholesale book is deleted: the driver
            // names the no-account blocker and it is logged below under
            // [BakeryProduction], like every other refusal.
            var diagnostics = new List<string>();

            // Slice S2 (2026-11-05, plan 02 §4): baker's hand kit via the
            // existing equipment authority — the EquipmentTaskGate
            // pattern (ForHolder + CheckTaskRequirements on a kit code),
            // now reading the bakery's hub-owned equipment register.
            // A bakery with no register (or no kit in it) still refuses
            // here, logged as the next blocker, never defaulted true.
            LandLedgers.Economy.Equipment.BusinessEquipmentRegister bakeryRegister =
                hub != null ? hub.EquipmentRegisterFor(bakery.BusinessInstanceId) : null;
            var kitAvailability = EquipmentRequirements.ForHolder(
                "business", bakery.BusinessInstanceId, dayIndex,
                bakeryRegister?.Assets, bakeryRegister?.Kits, null);
            var kitStub = new TaskDefinition("bakery-daily-bake", "daily bakery bake", 1);
            kitStub.EquipmentClasses.Add(EquipmentRequirementCodes.Kit(
                LandLedgers.Economy.Businesses.Bakery.BakeryBreadCatalog.BakerHandKitId));
            string kitRefusal = EquipmentRequirements.CheckTaskRequirements(kitStub, kitAvailability, diagnostics);
            bool bakerKitUsable = kitRefusal == null;
            if (!bakerKitUsable)
                diagnostics.Add("DriveBakeryWholesalePlanning: no usable baker's hand kit registered for the bakery — the kit gate is the next blocker (D-3).");

            // Oven component finder, following the proven
            // BlacksmithRuntime.EstablishForgeStationFromComponents
            // closure pattern: look up by asset id in the ovens'
            // installed components, return a real view from the
            // bakery's equipment register. Slice S2: components with
            // no asset record in the register still return null —
            // unverifiable components never satisfy the oven gate.
            var installedOvenComponents = new HashSet<string>(StringComparer.Ordinal);
            foreach (var oven in bakery.Ovens)
            {
                if (oven == null || oven.Station == null || oven.Station.ComponentAssetIds == null) continue;
                foreach (string componentId in oven.Station.ComponentAssetIds)
                {
                    if (!string.IsNullOrWhiteSpace(componentId)) installedOvenComponents.Add(componentId);
                }
            }
            Func<string, LandLedgers.Economy.Equipment.WorkstationComponentView?> findComponent = assetId =>
            {
                if (string.IsNullOrWhiteSpace(assetId) || !installedOvenComponents.Contains(assetId)) return null;
                LandLedgers.Economy.Blacksmith.EquipmentAsset asset = null;
                if (bakeryRegister?.Assets != null)
                    foreach (var candidate in bakeryRegister.Assets)
                        if (candidate != null && string.Equals(candidate.AssetId, assetId, StringComparison.Ordinal))
                        {
                            asset = candidate;
                            break;
                        }
                if (asset == null) return null;
                return new LandLedgers.Economy.Equipment.WorkstationComponentView
                {
                    AssetId = asset.AssetId,
                    Kind = asset.Kind,
                    Condition01 = asset.Condition01,
                    IsUsable = asset.IsUsable,
                };
            };

            // Plan 01 (2026-11-06, slice 1B): the store seam for the
            // daily driver. The catalog and intake are created once
            // here — both already save-round-trip on Series2Pass9SaveState;
            // creating them here is the missing production assignment.
            // The store is resolved per account via FindBusiness; the
            // live port comes from the store manager. If the store
            // manager or business is missing, the transfer step refuses
            // loudly inside the driver and bread stays on the bakery
            // shelf — never lost, never teleported.
            if (state.Catalog == null) state.Catalog = new LandLedgers.Economy.Ordering.Pass3StoreCatalog();
            if (state.WholesaleIntake == null) state.WholesaleIntake = new LandLedgers.Economy.Ordering.Pass3WholesaleIntake();

            BusinessInstanceState storeBusiness = null;
            string storeBusinessId = string.Empty;
            foreach (var account in bakery.WholesaleAccounts)
            {
                if (account == null || !account.IsActive) continue;
                BusinessInstanceState candidate = FindBusiness(account.BuyerBusinessId);
                if (candidate == null || candidate.BusinessType != BusinessType.GeneralStore) continue;
                storeBusiness = candidate;
                storeBusinessId = account.BuyerBusinessId;
                break;
            }
            if (storeBusiness == null)
                diagnostics.Add("DriveBakeryWholesalePlanning: no General Store business resolves for any wholesale account — transfer skipped, bread stays on the bakery shelf, accounts stay due.");
            LandLedgers.FirstLedger.GeneralStoreRuntimeManager storeManager =
                FindAnyObjectByType<LandLedgers.FirstLedger.GeneralStoreRuntimeManager>();
            if (storeManager == null)
                diagnostics.Add("DriveBakeryWholesalePlanning: no GeneralStoreRuntimeManager — transfer skipped, bread stays on the bakery shelf, accounts stay due.");
            IGeneralStoreTradingPort storePort = null;
            if (storeManager != null && storeBusiness != null)
                storePort = storeManager.BuildHouseholdTradingPort(storeBusiness);
            BusinessInstanceState bakeryBusiness = FindBusiness(bakery.BusinessInstanceId);

            // Plan 02 (2026-11-06, slice 2A): the flour seam, in the same
            // place the store seam above is built. Live store flour price
            // from the store manager (never hardcoded); store sacks reach
            // the driver through the store runtime state; Millbrook inputs
            // from the already-attached aid runtime with the freight quote
            // from the same rate card the negotiation uses; freight
            // availability is observable state (a Millbrook shipment has
            // departed — see MillbrookFreightHasDeparted), never a guess;
            // import figures from ImportCatalog after registration, with no
            // live order path in this slice.
            int storeFlourPriceCents = 0;
            if (storeManager != null)
                storeFlourPriceCents = storeManager.GetItemSellingPriceCents("flour");
            var millbrookQuote = new MillbrookFlourQuote();
            if (millbrookAidRuntime != null)
            {
                millbrookQuote.SacksAvailable =
                    millbrookAidRuntime.GetInventoryUnits("bakery_flour_sack") / 10;
                int millbrookPriceCents = millbrookAidRuntime.GetOrderPriceCents("bakery_flour_sack");
                millbrookQuote.GoodsPricePerSackCents =
                    millbrookPriceCents > 0 ? (int?)millbrookPriceCents : null;
                millbrookQuote.OneWayMiles = millbrookAidRuntime.PickupRouteMilesFromTown;
                DevelopmentTownConfiguration.BusinessDefinition carrierDefinition = null;
                if (developmentTownConfiguration != null
                    && developmentTownConfiguration.TryGetBusiness(
                        developmentTownConfiguration.FreightBusinessKey, out carrierDefinition)
                    && carrierDefinition?.freightRateCard != null
                    && FreightServiceQuoteCalculator.TryCalculate(
                        millbrookAidRuntime.PickupRouteMilesFromTown,
                        carrierDefinition.freightRateCard.baseServiceFeeCents,
                        carrierDefinition.freightRateCard.ratePerMileCents,
                        carrierDefinition.freightRateCard.chargeRoundTrip,
                        out FreightServiceQuote freightQuote, out string quoteRefusal))
                {
                    millbrookQuote.FreightQuoteCents = freightQuote.TotalPriceCents;
                }
                else
                {
                    millbrookQuote.FreightUnavailableReason =
                        "freight quote unavailable for the Millbrook route" +
                        (developmentTownConfiguration == null ? " (no town rate configuration)" : "");
                }
                if (millbrookQuote.FreightQuoteCents.HasValue && MillbrookFreightHasDeparted())
                {
                    millbrookQuote.FreightAvailable = true;
                }
                else if (string.IsNullOrWhiteSpace(millbrookQuote.FreightUnavailableReason))
                {
                    millbrookQuote.FreightUnavailableReason =
                        "freight blocked: no Millbrook shipment has departed (staging/T49 — Obj 16–18)";
                }
            }
            int importFlourPricePerLbCents = 0;
            int importFlourTransitDays = 0;
            int importNotionsPricePerUnitCents = 0;
            int importNotionsTransitDays = 0;
            {
                var importDiag = new List<string>();
                LandLedgers.Economy.Businesses.Bakery.BakeryFlourSupply.EnsureBakeryImportables(importDiag);
                diagnostics.AddRange(importDiag);
                LandLedgers.Economy.Trade.ImportMaterial importFlour =
                    LandLedgers.Economy.Trade.ImportCatalog.Get(
                        LandLedgers.Economy.Businesses.Bakery.BakeryFlourSupply.FlourMaterialId);
                if (importFlour != null)
                {
                    importFlourPricePerLbCents = importFlour.PricePerUnitCents;
                    importFlourTransitDays = importFlour.TransitDays;
                }
                LandLedgers.Economy.Trade.ImportMaterial importNotions =
                    LandLedgers.Economy.Trade.ImportCatalog.Get(
                        LandLedgers.Economy.Businesses.Bakery.BakeryFlourSupply.NotionsMaterialId);
                if (importNotions != null)
                {
                    importNotionsPricePerUnitCents = importNotions.PricePerUnitCents;
                    importNotionsTransitDays = importNotions.TransitDays;
                }
            }

            // Plan 04 N-slice (2026-11-06): the notions seam, in the same
            // place the flour seam above is built. The live procurement
            // service is the save slot first (SaveLoadManager
            // .PassXlProcurementService — F19, assigned by the
            // bootstrapper's feed registration in editor/development
            // builds), falling back to the attached store feed
            // procurement (same instance at NewGame). If neither exists
            // the driver logs the blocker and buys nothing — the bake
            // then refuses at Precheck on notions, honestly. The
            // registration is built from the catalog figures above
            // (10c/unit, 14 transit days, railhead origin) — never
            // hardcoded; no freight surcharge, because no freight rate
            // is authored for the railhead route (Kennedy must approve
            // a production freight rate before one is wired).
            LandLedgers.Economy.Businesses.GeneralStore.OffMapNamedSkuProcurementService notionsService =
                save != null ? save.PassXlProcurementService : null;
            if (notionsService == null) notionsService = storeFeedProcurement;
            LandLedgers.Economy.BreadDrivers.Series2NotionsSupply notionsSupply = null;
            LandLedgers.Economy.BreadDrivers.Series2CounterpartyRegistration notionsRegistration = null;
            if (notionsService == null)
            {
                diagnostics.Add("DriveBakeryWholesalePlanning: no live procurement service (save slot PassXlProcurementService and attached store feed procurement are both null) — notions cannot be ordered or fed; the bake will refuse at Precheck on notions (F19).");
            }
            else if (importNotionsPricePerUnitCents <= 0)
            {
                diagnostics.Add("DriveBakeryWholesalePlanning: notions import price is not registered — notions cannot be ordered or fed; the bake will refuse at Precheck on notions.");
            }
            else
            {
                notionsRegistration = new LandLedgers.Economy.BreadDrivers.Series2CounterpartyRegistration
                {
                    SupplierId = LandLedgers.Economy.BreadDrivers.BakeryNotionsDailySupply.NotionsSupplierId,
                    SupplierName = LandLedgers.Economy.BreadDrivers.BakeryNotionsDailySupply.NotionsSupplierName,
                    SkuId = LandLedgers.Economy.Businesses.Bakery.BakeryFlourSupply.NotionsMaterialId,
                    UnitGoodsCostCents = importNotionsPricePerUnitCents,
                    FreightSurchargeCents = 0,
                    InitialBudgetCents = 0,
                    TransitDays = importNotionsTransitDays,
                };
                notionsSupply = new LandLedgers.Economy.BreadDrivers.Series2NotionsSupply(
                    new LandLedgers.Economy.BreadDrivers.Series2FlourProcurementDriver(notionsService));
            }

            // Plan 10 E3 (Obj 9; W6 design): Glen's person budget for the
            // day. OwnerPersonId is bound at bootstrap (BindOwnerPerson);
            // -1 means unbound. Capacity comes from
            // DailyNeedsService.WorkCapacityFactor for Glen's live
            // PersonState (nutrition x health, E2-verified math), and the
            // minutes come from BakeryPersonTimeBudget.PersonMinutesPerDay
            // (600 x capacity). The hub WorkTimeBudget must resolve — it
            // is the budget the gate guards. Any missing piece logs the
            // exact gap under [BakeryProduction] and passes null: the gate
            // is inactive, never defaulted, and the bake proceeds on the
            // runtime rules exactly as before.
            int? callerDailyBudgetMinutes = null;
            {
                int ownerPersonId = bakery.OwnerPersonId;
                if (ownerPersonId < 0)
                {
                    diagnostics.Add("DriveBakeryWholesalePlanning: baker owner-person link is unbound (OwnerPersonId -1) — the person-budget gate is inactive; the bake proceeds on runtime rules (Obj 9 seam, B2 booking still owed).");
                }
                else
                {
                    PersonState owner = populationManager?.State?.GetPerson(ownerPersonId);
                    LandLedgers.Time.WorkTimeBudget ownerBudget = hub?.WorkTimeBudgets?.GetOrCreate(
                        LandLedgers.Primitives.EntityId.For(LandLedgers.Primitives.EntityKind.Person, ownerPersonId));
                    if (owner == null)
                    {
                        diagnostics.Add($"DriveBakeryWholesalePlanning: baker owner person P{ownerPersonId} does not resolve — the person-budget gate is inactive; the bake proceeds on runtime rules.");
                    }
                    else if (ownerBudget == null)
                    {
                        diagnostics.Add($"DriveBakeryWholesalePlanning: no WorkTimeBudget resolves for baker P{ownerPersonId} — the person-budget gate is inactive; the bake proceeds on runtime rules.");
                    }
                    else
                    {
                        float capacity = DailyNeedsService.WorkCapacityFactor(
                            owner, populationManager != null && populationManager.ConditionDebuffsSuppressed);
                        callerDailyBudgetMinutes = LandLedgers.Economy.Businesses.Bakery.BakeryPersonTimeBudget.PersonMinutesPerDay(capacity);

                    }
                }
            }

            var inputs = new BakeryWholesaleDailyInputs
            {
                Bakery = bakery,
                ProductionDriver = state.BakeryDriver,
                DayIndex = dayIndex,
                BakerKitUsable = bakerKitUsable,
                OvenDefinition = LandLedgers.Economy.Equipment.Workstations.WorkstationCatalog.BakeOven,
                FindOvenComponent = findComponent,
                IdRegistry = hub != null ? hub.Ids : null,
                StorePort = storePort,
                StoreCashPort = storeBusiness?.RuntimeState != null
                    ? new Series2BusinessRuntimeCashPort(storeBusiness.RuntimeState) : null,
                BakeryCashPort = bakeryBusiness?.RuntimeState != null
                    ? new Series2BusinessRuntimeCashPort(bakeryBusiness.RuntimeState) : null,
                StoreBusinessId = storeBusinessId,
                BakeryBusinessId = bakery.BusinessInstanceId,
                Intake = state.WholesaleIntake,
                StoreCatalog = state.Catalog,
                StoreRuntimeState = storeBusiness?.RuntimeState,
                BakeryRuntimeState = bakeryBusiness?.RuntimeState,
                StoreFlourUnitPriceCents = storeFlourPriceCents,
                MillbrookQuote = millbrookQuote,
                MillbrookSupplier = millbrookAidRuntime,
                ImportFlourPricePerLbCents = importFlourPricePerLbCents,
                ImportTransitDays = importFlourTransitDays,
                ImportOrderPathExists = false,
                NotionsSupply = notionsSupply,
                NotionsRegistration = notionsRegistration,
                CallerDailyBudgetMinutes = callerDailyBudgetMinutes,
                // Plan 04 Slice 4: thread the running actuals in so the
                // driver can log the cumulative reconciliation line.
                PriorCumulativeBatchesBaked = bakeryCumulativeBatchesBaked,
                PriorCumulativeFlourCashSpentCents = bakeryCumulativeFlourCashSpentCents,
                PriorCumulativeFlourSacksPurchased = bakeryCumulativeFlourSacksPurchased,
                PriorCumulativeFlourPoundsPurchased = bakeryCumulativeFlourPoundsPurchased,
                PriorCumulativeNotionsCashSpentCents = bakeryCumulativeNotionsCashSpentCents,
                PriorCumulativeWholesaleRevenueCents = bakeryCumulativeWholesaleRevenueCents,
                CumulativeActualsThreaded = true,
            };
            BakeryWholesaleDailyResult result = BakeryWholesaleDailyDriver.RunDay(inputs, diagnostics);

            // Plan 04 Slice 4: carry the running actuals forward for
            // tomorrow's cumulative line (diagnostics only — never saved).
            bakeryCumulativeBatchesBaked = result.CumulativeBatchesBaked;
            bakeryCumulativeFlourCashSpentCents = result.CumulativeFlourCashSpentCents;
            bakeryCumulativeFlourSacksPurchased = result.CumulativeFlourSacksPurchased;
            bakeryCumulativeFlourPoundsPurchased = result.CumulativeFlourPoundsPurchased;
            bakeryCumulativeNotionsCashSpentCents = result.CumulativeNotionsCashSpentCents;
            bakeryCumulativeWholesaleRevenueCents = result.CumulativeWholesaleRevenueCents;

            // M4 (Plan 04, D-S5 — wire branch): register the bakery's
            // unmet input needs so the weekly real-demand collection sees
            // them. The needs are the driver's own computation
            // (BuildInputNeeds maps the result fields); nothing new is
            // invented. Fuel is NOT registered: the driver computes no
            // fuel need (NB-3/D-2/D-7). If SBRM is unwired, the needs are
            // logged and dropped — never conjured, never a second supply
            // path. The weekly pass clears the registered needs after
            // collecting them, so daily registrations sum to one week of
            // unmet need and never accumulate across weeks.
            sharedBusinessRuntime ??= FindAnyObjectByType<SharedBusinessRuntimeManager>();
            if (sharedBusinessRuntime != null)
            {
                foreach (var need in BakeryWholesaleDailyDriver.BuildInputNeeds(result, bakery.BusinessInstanceId))
                {
                    sharedBusinessRuntime.RegisterBusinessInputNeed(need);
                    diagnostics.Add($"DriveBakeryWholesalePlanning: registered bakery input need {need.InputCategoryId} x{need.UnitsNeeded} for weekly demand collection.");
                }
            }
            else
            {
                diagnostics.Add("DriveBakeryWholesalePlanning: SharedBusinessRuntimeManager is unwired — the bakery's unmet flour/notions needs were not registered for weekly demand collection.");
            }

            // Plan 10 E4 (Obj 8): the daily fuel-blocker line, built once
            // per planned day from observed state — fuel need (batches x
            // FuelUnitsPerFiring), fuel on hand in the bakery store, the
            // authored D-2 factor from the town configuration (0 = unset),
            // and the dealer/intake state. No live FuelYardByproductIntake
            // exists in the First Ledger daily flow: the only production
            // construction is the scripted ThreeBusinessSlice (grep-verified
            // E4), so the intake state is observed as absent, not assumed.
            // The attached source is the in-stock stand-in unless a dealer
            // source was constructed (B1's work, not this slice's). This
            // line diagnoses only — no fuel moves, no source is attached
            // or replaced.
            if (result.BatchesPlanned > 0)
            {
                LandLedgers.Economy.FreightFuel.Series2DealerBakeryFuelSource dealerSource =
                    state.FuelSource as LandLedgers.Economy.FreightFuel.Series2DealerBakeryFuelSource;
                int authoredFactor = developmentTownConfiguration != null
                    ? developmentTownConfiguration.FuelDealerIntakeUnitsPerBakeryUnit : 0;
                diagnostics.Add(LandLedgers.Economy.BreadDrivers.BakeryFuelBlockerDiagnostics.BuildFuelLine(
                    new LandLedgers.Economy.BreadDrivers.BakeryFuelBlockerInputs
                    {
                        BatchesPlanned = result.BatchesPlanned,
                        FuelUnitsOnHand = bakery.FuelStock.UnitsOnHand(
                            LandLedgers.Economy.Businesses.Bakery.BakeryBreadCatalog.FuelItemId),
                        AuthoredFactor = authoredFactor > 0 ? (int?)authoredFactor : null,
                        YardIntakeExistsInDailyFlow = false,
                        DealerSourceAttached = dealerSource != null,
                        DealerFuelUnitsOnHand = dealerSource != null ? dealerSource.FuelUnitsOnHand() : 0,
                    }));
            }

            if (!string.IsNullOrWhiteSpace(result.Refusal))
                Debug.Log($"[BakeryProduction] Day {dayIndex}: {result.Refusal}", this);
            else
                Debug.Log($"[BakeryProduction] Day {dayIndex}: {result.AccountsOnBooks} wholesale account(s), " +
                    $"{result.BatchesPlanned} batch(es) planned, {result.BatchesBaked} baked, " +
                    $"{result.TransfersCompleted} transfer(s), {result.UnitsStocked}u stocked.", this);
            if (diagnostics.Count > 0)
                Debug.Log("[BakeryProduction] " + string.Join(" | ", diagnostics), this);
        }

        /// <summary>
        /// Releases a dispatch-preparation hold only after TaskAuthority has
        /// completed the exact linked assignment. The shipment's persisted
        /// authorization flag is the idempotency marker, so a retry after
        /// restore is safe and cannot authorize a different shipment.
        /// </summary>
        private void HandleCompletedFreightDispatchTask(WorkTask task)
        {
            if (task == null || task.Status != TaskStatus.Complete
                || (task.Outcome != TaskOutcome.Success && task.Outcome != TaskOutcome.AcceptableWithVariation)
                || task.DefinitionId != FreightDispatchTaskId
                || string.IsNullOrWhiteSpace(task.TermsRef)
                || FreightProduction?.ServiceAgreements == null)
                return;

            FreightDispatchTaskData taskData = JsonUtility.FromJson<FreightDispatchTaskData>(task.TermsRef);
            if (taskData == null || string.IsNullOrWhiteSpace(taskData.AgreementId)
                || string.IsNullOrWhiteSpace(taskData.ShipmentId)) return;
            FreightServiceAgreement agreement = FreightProduction.ServiceAgreements.FindAgreement(taskData.AgreementId);
            LogisticsShipmentState shipment = FreightProduction.AllShipments().FirstOrDefault(candidate => candidate != null
                && string.Equals(candidate.ShipmentId, taskData.ShipmentId, StringComparison.Ordinal));
            if (agreement == null || shipment == null || !shipment.requiresDispatchAuthorization) return;

            string expectedRef = FreightDispatchTaskPrefix + agreement.AgreementId + ":" + agreement.ShipmentId;
            BusinessInstanceState carrier = FindBusiness(agreement.CarrierBusinessId);
            int operatorId = carrier?.Owner?.PersonId ?? -1;
            string operatorEntityId = operatorId >= 0
                ? EntityId.For(EntityKind.Person, operatorId).ToString()
                : string.Empty;
            PersonState operatorPerson = operatorId >= 0 ? populationManager?.State?.GetPerson(operatorId) : null;
            AnimalState horse = FindOpeningFreightHorse(agreement.CarrierBusinessId);
            EquipmentAsset wagon = hub?.TransportAssets?.Get(taskData.WagonId);
            string refusal = agreement.Status != FreightServiceAgreementStatus.Accepted
                || !string.Equals(agreement.DispatchTaskId, task.TaskId.ToString(), StringComparison.Ordinal)
                || !string.Equals(agreement.ShipmentId, shipment.ShipmentId, StringComparison.Ordinal)
                || !string.Equals(shipment.freightServiceAgreementId, agreement.AgreementId, StringComparison.Ordinal)
                || agreement.PreparedGameSeconds <= agreement.AcceptedGameSeconds
                    ? "completed task is not linked to the accepted, chronologically prepared agreement."
                : shipment.Status != LogisticsShipmentStatus.Planned || shipment.loadApplied
                    ? "shipment is not still Planned and unloaded."
                : !task.HasAssignee || task.AssigneeId.Kind != EntityKind.Person
                    || task.AssigneeId.Id != agreement.AcceptingOperatorPersonId
                    || taskData.OperatorPersonId != agreement.AcceptingOperatorPersonId
                    || operatorId != agreement.AcceptingOperatorPersonId
                    ? "completed dispatch assignee does not match the accepted B3 operator."
                : !string.Equals(task.CustomerRef, expectedRef, StringComparison.Ordinal)
                    || !string.Equals(taskData.HorseId, horse?.AnimalId.ToString(), StringComparison.Ordinal)
                    || !string.Equals(taskData.WagonId, shipment.physicalWagonAssetId, StringComparison.Ordinal)
                    ? "completed task terms do not match the accepted shipment equipment identities."
                : null;

            if (refusal != null)
            {
                LogFreightAgreementBlockerOnce("dispatch-completion:" + task.TaskId,
                    $"[FreightDispatch] Completed task '{task.TaskId}' did not authorize shipment '{shipment.ShipmentId}': {refusal}");
                return;
            }

            // Slice S5 (defense in depth, own once-key so an earlier
            // identity refusal can never suppress it): a dispatch task
            // completed while Ethan was away must not authorize. The
            // shipment stays held; ReconcileCompletedFreightDispatchTasks
            // retries every ShortTick, so it authorizes the moment he
            // arrives — never a dead end.
            if (operatorPerson != null && !IsPersonVisuallyAtBuilding(operatorId, operatorPerson.homeBuildingId))
            {
                LogFreightAgreementBlockerOnce("dispatch-completion-location:" + task.TaskId,
                    $"[FreightDispatch] Completed task '{task.TaskId}' did not authorize shipment '{shipment.ShipmentId}': completed dispatch operator is not visually at staging; authorization waits for arrival.");
                return;
            }

            // Slice S3: bind the draft team here, after every identity check
            // has passed and before authorization. The hitch reserved the
            // horse under the hitch task id and released it on completion, so
            // no production code has bound the team yet; the bind is the
            // missing production step. Already bound to this shipment counts
            // as success (idempotent: the integration fixture pre-binds, and
            // a save/restore re-run of this handler must not double-hold).
            // Window is float.MaxValue per the existing Dispatch/mount
            // precedent; the hold is bounded by the terminal release in
            // ReleaseTerminalFreightDraftTeams, not by the window.
            bool alreadyBound = horse != null && wagon != null
                && horse.CurrentUse == HorseUseKind.Draft
                && string.Equals(horse.CurrentAssignmentId, shipment.ShipmentId, StringComparison.Ordinal)
                && string.Equals(wagon.CurrentShipmentId, shipment.ShipmentId, StringComparison.Ordinal)
                && string.Equals(wagon.CurrentDriverPersonId, operatorEntityId, StringComparison.Ordinal);
            bool justBound = false;
            if (!alreadyBound)
            {
                if (horse == null || wagon == null)
                {
                    LogFreightAgreementBlockerOnce("dispatch-bind:" + shipment.ShipmentId,
                        $"[FreightDispatch] Completed task '{task.TaskId}' did not authorize shipment '{shipment.ShipmentId}': the draft team does not resolve.");
                    return;
                }
                string bindRefusal = WagonAuthority.TryAssignDraftTeam(wagon, new List<AnimalState> { horse },
                    operatorEntityId, shipment.ShipmentId,
                    new AnimalAvailabilityAuthority(hub.ReservationAuthority),
                    AbsoluteNowGameSeconds(), float.MaxValue);
                if (bindRefusal != null)
                {
                    LogFreightAgreementBlockerOnce("dispatch-bind:" + shipment.ShipmentId,
                        $"[FreightDispatch] Completed task '{task.TaskId}' did not authorize shipment '{shipment.ShipmentId}': draft team binding refused: {bindRefusal}");
                    return;
                }
                justBound = true;
            }

            if (!string.IsNullOrWhiteSpace(shipment.physicalDriverPersonId)
                && !string.Equals(shipment.physicalDriverPersonId, operatorEntityId, StringComparison.Ordinal))
            {
                if (justBound)
                    WagonAuthority.ReleaseDraftTeam(wagon, new List<AnimalState> { horse },
                        new AnimalAvailabilityAuthority(hub.ReservationAuthority));
                LogFreightAgreementBlockerOnce("dispatch-driver-identity:" + shipment.ShipmentId,
                    $"[FreightDispatch] Completed task '{task.TaskId}' did not authorize shipment '{shipment.ShipmentId}': physical driver identity '{shipment.physicalDriverPersonId}' does not match assigned operator '{operatorEntityId}'.");
                return;
            }
            shipment.physicalDriverPersonId = operatorEntityId;
            shipment.physicalTransportBound = true;

            if (!FreightProduction.AuthorizeShipmentDispatch(shipment.ShipmentId, out string authorizationRefusal))
            {
                if (justBound)
                    WagonAuthority.ReleaseDraftTeam(wagon, new List<AnimalState> { horse },
                        new AnimalAvailabilityAuthority(hub.ReservationAuthority));
                LogFreightAgreementBlockerOnce("dispatch-authorization:" + shipment.ShipmentId,
                    $"[FreightDispatch] Completed task '{task.TaskId}' could not release shipment '{shipment.ShipmentId}' from dispatch hold: {authorizationRefusal}");
                return;
            }

            // Slice S10: depart the wagon + Ethan for Millbrook on the same
            // ShortTick (Ethan is provably at staging here — the S5 gate
            // above). A false return does not un-authorize anything: the
            // shipment stays authorized and the daily scan retries.
            TryDepartMillbrookShipment(agreement, shipment, timeManager != null ? timeManager.CurrentAbsoluteDayIndex : 0);

            Debug.Log($"[FreightDispatch] Task '{task.TaskId}' completed and authorized dispatch for agreement '{agreement.AgreementId}', " +
                $"shipment '{shipment.ShipmentId}', operator P{operatorId} at absolute simulation time {AbsoluteNowGameSeconds():0}s. " +
                "Shipment remains Planned; no movement, loading, pickup, delivery, or payment was performed.", this);
        }

        /// <summary>Recovers a save boundary where task completion persisted but hold release did not.</summary>
        private void ReconcileCompletedFreightDispatchTasks()
        {
            if (FreightProduction?.ServiceAgreements?.Agreements == null || hub?.Tasks == null) return;
            foreach (FreightServiceAgreement agreement in FreightProduction.ServiceAgreements.Agreements)
            {
                if (agreement == null || string.IsNullOrWhiteSpace(agreement.DispatchTaskId)) continue;
                WorkTask task = hub.Tasks.FindTaskByIdString(agreement.DispatchTaskId);
                if (task != null && task.Status == TaskStatus.Complete)
                    HandleCompletedFreightDispatchTask(task);
            }
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
                    || task.CustomerRef.StartsWith(FreightMeetingTaskPrefix + "travel:", StringComparison.Ordinal)
                    || FreightDispatchStaging.IsDispatchTravelRef(task.CustomerRef));
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
                HandleCompletedFreightDispatchTravelTask(task, dayIndex);
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
            if (FreightDispatchStaging.IsDispatchTravelRef(task.CustomerRef))
            {
                FreightDispatchTravelTaskData trip = JsonUtility.FromJson<FreightDispatchTravelTaskData>(task.TermsRef);
                if (trip == null) return false;
                targetBuildingId = trip.StagingBuildingId;
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

            // Plan 02 Slice 3: when the store holds zero identified lots for
            // every meal-priority item, say so once per day (this drive runs
            // once per day) instead of letting every household surface a
            // bare seller failure. No stock is created here — the line only
            // names the condition; needs stay open until a reorder or bakery
            // delivery lands.
            bool anyMealItemStocked = false;
            if (supplierDirectory != null)
            {
                foreach (string itemId in MealSchedulingPolicy.Default.MealItemPriority)
                {
                    foreach (IGoodsSupplier supplier in supplierDirectory.All)
                    {
                        if (supplier is IItemResolvingSupplier itemSupplier
                            && itemSupplier.ItemStockUnits(itemId) > 0)
                        {
                            anyMealItemStocked = true;
                            break;
                        }
                    }

                    if (anyMealItemStocked)
                    {
                        break;
                    }
                }
            }

            if (!anyMealItemStocked)
            {
                diagnostics.Add("General Store holds no identified lots for any meal item; needs stay open until a reorder or bakery delivery lands");
            }

            // Plan 02 Slice 5: accumulate the day's shopping totals for the
            // single summary line below — purchases made, cents spent, and
            // the first failure reason recorded per need that stays open.
            int shoppingPurchasesMade = 0;
            int shoppingCentsSpent = 0;
            var shoppingFailureByNeed = new Dictionary<string, string>(StringComparer.Ordinal);

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
                    OffMapJourneys = OffMapJourneys,
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

                    if (batch != null)
                    {
                        shoppingPurchasesMade += batch.ServedNeedCount;
                        if (batch.NeedResults != null)
                        {
                            foreach (ShoppingTripResult needResult in batch.NeedResults)
                            {
                                if (needResult == null) continue;
                                shoppingCentsSpent += Math.Max(0, needResult.BuyerOutflowCents);
                                if (!needResult.Success && !string.IsNullOrWhiteSpace(needResult.FailureReason))
                                {
                                    string failureKey = household.id + ":" + needResult.ItemId;
                                    if (!shoppingFailureByNeed.ContainsKey(failureKey))
                                    {
                                        shoppingFailureByNeed[failureKey] = needResult.FailureReason;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // Plan 02 Slice 5: one daily shopping summary line (not one line
            // per trip): purchases made, cents spent, and the needs still
            // open with their first failure reason each. A single
            // diagnostics entry, so the 12-per-day cap in DriveDailyNeeds
            // still bounds the day's log; the open-need list is capped at 8
            // entries with an "and N more" tail. Needs with no failure
            // recorded today were never attempted (no attempt today).
            // Plan 05 Slice 5: the line also names the longest-open need —
            // the open need opened earliest — with its age and its first
            // failure reason today. "Longest open" is measured by
            // CreatedDayIndex, NOT by LastEvaluatedDayIndex: the monitor
            // refreshes LastEvaluatedDayIndex to the current day on every
            // evaluation (HouseholdShortage.cs:267, also :211 and :226) and
            // the loop refreshes it on every receipt
            // (HouseholdShoppingLoop.cs:420, :1729, :1794), so every need
            // evaluated today ties at today and no longest-open need can be
            // identified from it — while CreatedDayIndex is set once at
            // creation (HouseholdShortage.cs:389) and never reassigned.
            // Age is today minus the creation day. The reason is the same
            // first-failure map the still-open list reads (or "no attempt
            // today" when the need was never attempted). No new reason
            // string: the per-need reason for a budget-blocked trip already
            // carries the search's budget text ("only N work minutes left,
            // trip needs M", PersonScheduleTracker.cs:762, inside the "No
            // shopper in any searched window" reason), which is a different
            // branch from Plan 02 Slice 5's cash-affordability 'budget
            // short' (HouseholdShoppingLoop.cs:2241).
            var stillOpenParts = new List<string>();
            int stillOpenTotal = 0;
            HouseholdPurchasingNeed longestOpenNeed = null;
            int longestOpenHouseholdId = -1;
            foreach (HouseholdState household in populationManager.State.households)
            {
                if (household == null || hub.PurchasingNeeds == null) continue;
                List<HouseholdPurchasingNeed> openNeeds = hub.PurchasingNeeds.GetOpenNeeds(household.id);
                if (openNeeds == null) continue;
                foreach (HouseholdPurchasingNeed openNeed in openNeeds)
                {
                    if (openNeed == null) continue;
                    stillOpenTotal++;
                    if (longestOpenNeed == null || openNeed.CreatedDayIndex < longestOpenNeed.CreatedDayIndex)
                    {
                        longestOpenNeed = openNeed;
                        longestOpenHouseholdId = household.id;
                    }

                    if (stillOpenParts.Count < 8)
                    {
                        string failureKey = household.id + ":" + openNeed.ItemId;
                        string reason = shoppingFailureByNeed.TryGetValue(failureKey, out string recorded)
                            ? recorded
                            : "no attempt today";
                        stillOpenParts.Add($"H{household.id} {openNeed.ItemId} ({reason})");
                    }
                }
            }

            string longestOpenText = string.Empty;
            if (longestOpenNeed != null)
            {
                string longestFailureKey = longestOpenHouseholdId + ":" + longestOpenNeed.ItemId;
                string longestReason = shoppingFailureByNeed.TryGetValue(longestFailureKey, out string longestRecorded)
                    ? longestRecorded
                    : "no attempt today";
                int longestAgeDays = Math.Max(0, absoluteDayIndex - longestOpenNeed.CreatedDayIndex);
                longestOpenText = $"; longest open: H{longestOpenHouseholdId} '{longestOpenNeed.ItemId}' open since day {longestOpenNeed.CreatedDayIndex} ({longestAgeDays}d) — {longestReason}";
            }

            string stillOpenText = stillOpenTotal == 0
                ? "none"
                : string.Join("; ", stillOpenParts)
                    + (stillOpenTotal > stillOpenParts.Count ? $"; and {stillOpenTotal - stillOpenParts.Count} more" : string.Empty);
            diagnostics.Add($"[FoodEconomy] shopping summary day {absoluteDayIndex}: {shoppingPurchasesMade} purchase(s), {shoppingCentsSpent}c spent{longestOpenText}; still open: {stillOpenText}");
        }

        /// <summary>
        /// Plan 03 Slice 0 (P0-2): the single lazy gate between a generated
        /// town and a populated journey model. Every failure exit used to be
        /// silent; each now logs once per distinct failure state — hub null,
        /// townWorld null (with whether the field was serialized or
        /// FindAnyObjectByType-resolved), Grid null (with GenerationPassCount),
        /// or zero road components (with the builder diagnostic, the grid's
        /// road-cell count, and the computed signature). The drivers' held
        /// townWorld instance identity is logged once per instance so it can
        /// be compared with the bootstrapper's at startup. No behavior change
        /// except logging: a failed day still retries the next day, and the
        /// signature still advances only on a successful sync.
        /// </summary>
        private void EnsureTownJourneyNetwork()
        {
            if (hub == null)
            {
                LogTownJourneyNetworkFailureOnce("hub is null; no journey model is available.");
                return;
            }
            bool townWorldWasSerialized = townWorld != null;
            townWorld ??= FindAnyObjectByType<TownWorldController>();
            LogTownWorldInstanceIdentity(townWorldWasSerialized);
            if (townWorld == null)
            {
                LogTownJourneyNetworkFailureOnce(
                    "townWorld is null (no serialized instance; FindAnyObjectByType<TownWorldController> found none).");
                return;
            }
            if (townWorld.Grid == null)
            {
                LogTownJourneyNetworkFailureOnce(
                    $"townWorld '{townWorld.gameObject.name}' (instance {townWorld.GetInstanceID()}, " +
                    $"{(townWorldWasSerialized ? "serialized" : "FindAnyObjectByType-resolved")}) " +
                    $"has no Grid (GenerationPassCount={townWorld.GenerationPassCount}).");
                return;
            }

            IReadOnlyList<BusinessInstanceState> businesses = sharedBusinessRuntime != null
                ? sharedBusinessRuntime.Businesses
                : Array.Empty<BusinessInstanceState>();
            int signature = CalculateTownJourneyWorldSignature(townWorld, businesses);
            if (signature != lastTownJourneyWorldSignature)
            {
                Vector3 townCenter = townWorld.Settings != null
                    ? townWorld.Settings.worldCenter
                    : townWorld.Grid.Origin + new Vector3(
                        townWorld.Grid.Width * townWorld.Grid.CellSizeMeters * 0.5f,
                        0f,
                        townWorld.Grid.Depth * townWorld.Grid.CellSizeMeters * 0.5f);
                TownJourneyNetworkBuilder.BuildResult result = TownJourneyNetworkBuilder.Synchronize(
                    hub.Journeys, townWorld.Grid, townCenter, townWorld.Plots, townWorld.Buildings, businesses);
                if (result.RoadComponents <= 0)
                {
                    LogTownJourneyNetworkFailureOnce(
                        $"townWorld '{townWorld.gameObject.name}' (instance {townWorld.GetInstanceID()}) " +
                        $"Grid has {CountRoadCells(townWorld.Grid)} road cells; signature={signature}; " +
                        $"builder refused: {result.Diagnostic}");
                    return;
                }

                Journeys = hub.Journeys;
                lastTownJourneyWorldSignature = signature;
                if (result.RoutableBuildings <= 0)
                    Debug.LogWarning("[SimulationDrivers] " + result.Diagnostic, this);
                else
                    Debug.Log("[SimulationDrivers] " + result.Diagnostic, this);
            }

            lastTownJourneyNetworkFailure = string.Empty;
            EnsureRegionalJourneyRoutes();
        }

        /// <summary>
        /// Plan 03 Slice 0 (P0-2): logs a journey-network failure state once
        /// until it changes, so a persistent precondition failure is named in
        /// the log without spamming it on every daily pass. Follows the
        /// regional-registration failure pattern in
        /// EnsureRegionalJourneyRoutes.
        /// </summary>
        private void LogTownJourneyNetworkFailureOnce(string failure)
        {
            if (string.Equals(lastTownJourneyNetworkFailure, failure, StringComparison.Ordinal)) return;
            lastTownJourneyNetworkFailure = failure;
            Debug.LogWarning("[SimulationDrivers] Journey network registration blocked: " + failure, this);
        }

        /// <summary>
        /// Plan 03 Slice 0 (P0-2): names the TownWorldController instance the
        /// drivers hold (GameObject name + instance id + how it was obtained)
        /// once per instance, so Codex can compare it with the bootstrapper's
        /// held instance at startup and tell a duplicate/wrong instance from
        /// a roadless grid.
        /// </summary>
        private void LogTownWorldInstanceIdentity(bool townWorldWasSerialized)
        {
            int instanceId = townWorld != null ? townWorld.GetInstanceID() : 0;
            if (instanceId == lastLoggedTownWorldInstanceId) return;
            lastLoggedTownWorldInstanceId = instanceId;
            if (townWorld == null)
            {
                Debug.Log("[SimulationDrivers] Journey network identity: drivers hold no TownWorldController " +
                    "(none serialized; FindAnyObjectByType found none).", this);
                return;
            }
            Debug.Log($"[SimulationDrivers] Journey network identity: drivers hold TownWorldController " +
                $"'{townWorld.gameObject.name}' (instance {instanceId}, " +
                $"{(townWorldWasSerialized ? "serialized" : "FindAnyObjectByType-resolved")}), " +
                $"Grid={(townWorld.Grid != null ? "present" : "null")}, " +
                $"GenerationPassCount={townWorld.GenerationPassCount}.", this);
        }

        /// <summary>Plan 03 Slice 0 (P0-2): road cells on the grid the drivers read.</summary>
        private static int CountRoadCells(TownGrid grid)
        {
            int count = 0;
            for (int z = 0; z < grid.Depth; z++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    if (grid.GetCell(new GridCoord(x, z)).IsRoad) count++;
                }
            }
            return count;
        }

        /// <summary>
        /// Registers the authored off-map regional locations only after the
        /// generated town journey graph has established its real town-center
        /// anchor. ApplyAll is idempotent, so this also repairs a missing
        /// regional registration after restore or route reconstruction without
        /// duplicating locations or replacing local roads.
        /// </summary>
        private void EnsureRegionalJourneyRoutes()
        {
            if (hub == null || hub.Journeys.GetLocation("town-center") == null) return;

            var diagnostics = new List<string>();
            int registered = RegionalTownRegistry.ApplyAll(hub.Journeys, diagnostics);
            if (registered < 0)
            {
                string failure = string.Join(" ", diagnostics);
                if (!string.Equals(lastRegionalJourneyRegistrationFailure, failure, StringComparison.Ordinal))
                {
                    lastRegionalJourneyRegistrationFailure = failure;
                    Debug.LogWarning("[SimulationDrivers] Regional journey registration refused: " + failure, this);
                }
                return;
            }

            lastRegionalJourneyRegistrationFailure = string.Empty;
            if (registered > 0)
            {
                Debug.Log($"[SimulationDrivers] Registered {registered} authored regional journey locations " +
                    "(Ashford and Redfield) on the hub JourneyModel.", this);
            }
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
                    dayIndex: absoluteDayIndex,
                    // Plan 02 Slice 2: suppress needs no legitimately known
                    // seller recognizes (recognition only — never stock).
                    // Evaluated daily, so a seller learned or registered
                    // later lets the need open the next day.
                    isRecognizedByKnownSeller: HouseholdShortageMonitor.KnownSellerRecognitionPredicate(
                        supplierDirectory, sellerKnowledge,
                        householdMemberships?.GetActiveMembers(household.id)));
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
                // Overnight 2026-11-04: the tick log used to print every
                // routine domestic event (planned, started, completed) on
                // every advancing tick. Only genuine failures surface now;
                // routine activity stays in the driver's own records.
                var domesticFailures = new List<string>();
                foreach (string line in domesticDiag)
                {
                    if (line != null && (line.Contains("failed") || line.Contains("INCONSISTENCY")
                        || line.Contains("UNRESOLVED") || line.Contains("missing")
                        || line.Contains("not registered") || line.Contains("cancelled")))
                        domesticFailures.Add(line);
                }
                if (domesticFailures.Count > 0)
                {
                    Debug.LogWarning("[SimulationDrivers] domestic labor failures: " +
                        string.Join(" | ", domesticFailures.Take(20)));
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
            ReconcileCompletedFreightDispatchTasks();

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
