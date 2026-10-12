using System;
using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Economy;
using LandLedgers.Economy.Butcher;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Creation;
using LandLedgers.Economy.DraftPower;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Farming;
using LandLedgers.Economy.Farming.Integration;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.Transport;
using LandLedgers.Economy.Liabilities;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Farming.Risk;
using LandLedgers.Economy.Postal;
using LandLedgers.Economy.Recruitment;
using LandLedgers.Economy.Labor;
using LandLedgers.Economy.Delegation;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.Barber;
using LandLedgers.Economy.Businesses.BoardingHouse;
using LandLedgers.Economy.Businesses.Doctor;
using LandLedgers.Economy.Businesses.GrainMill;
using LandLedgers.Economy.Businesses.Hotel;
using LandLedgers.Economy.Businesses.Logging;
using LandLedgers.Economy.Businesses.Mine;
using LandLedgers.Economy.Businesses.Newspaper;
using LandLedgers.Economy.Businesses.Restaurant;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Economy.Businesses.Tailor;
using LandLedgers.Economy.Farming.Crops;
using LandLedgers.Economy.Farming.Dairy;
using LandLedgers.Economy.Farming.Livestock;
using LandLedgers.Economy.Farming.Timber;
using LandLedgers.FirstLedger;
using LandLedgers.ReadModels.Valuation;
using LandLedgers.World.Journeys;
using LandLedgers.World.Property;
using LandLedgers.Skills;
using LandLedgers.Tasks;
using LandLedgers.Time;
using UnityEngine;

namespace LandLedgers.Orchestration.Systems
{
    /// <summary>
    /// CLN-1: the scene-level home for the standalone simulation authorities accumulated
    /// across HF/TTS/DEV/BIZ/FRM. These authorities own real game state but had no
    /// MonoBehaviour holder and no save-pipeline wiring; this hub gives them both.
    /// Kennedy attaches this to the one scene (one instance); the SaveLoadManager
    /// finds it via FindAnyObjectByType and persists its section.
    /// The hub owns the authorities and the save boundary — it contains no simulation
    /// logic itself (drivers live in CLN-2).
    /// </summary>
    public sealed class SimulationSystemsHub : MonoBehaviour
    {
        [SerializeField]
        private EntityIdRegistry idRegistry = new EntityIdRegistry();

        [SerializeField]
        private AnimalRegistry animalRegistry;

        [SerializeField]
        private HouseholdLedgerRegistry householdLedgers = new HouseholdLedgerRegistry();

        /// <summary>Phase B: per-household real item-lot inventories.</summary>
        private HouseholdInventoryRegistry householdInventories;

        /// <summary>Phase B: per-household meal logs (served, missed, preparations).</summary>
        private HouseholdMealLogRegistry mealLog;

        /// <summary>Phase B: household purchasing needs (demand, never sales).</summary>
        private HouseholdNeedRegistry purchasingNeeds;
        [SerializeField]
        private TaskAuthority taskAuthority = new TaskAuthority();

        [SerializeField]
        private SkillService skillService = new SkillService();

        [SerializeField]
        private WorkTimeBudgetStore workTimeBudgets = new WorkTimeBudgetStore();

        private GenericProductionRuntimeRegistry genericProductionRuntime;

        [SerializeField]
        private EnterpriseValuationReadModel valuation = new EnterpriseValuationReadModel();

        [SerializeField]
        private FreightResourcePool freightPool = new FreightResourcePool();

        [SerializeField]
        private TransportAssetRegistry transportAssets = new TransportAssetRegistry();

        [SerializeField]
        private List<ButcherRuntime> butcherRuntimes = new List<ButcherRuntime>();

        [SerializeField]
        private FarmFlowLedger farmFlows = new FarmFlowLedger();

        [SerializeField]
        private BusinessCapabilityRegistry capabilityRegistry = new BusinessCapabilityRegistry();

        [SerializeField]
        private BusinessOperatingLedger operatingLedger = new BusinessOperatingLedger();

        [SerializeField]
        private EmploymentRelationshipRegistry employments = new EmploymentRelationshipRegistry();

        [SerializeField]
        private FarmSliceSystems farmSlice = new FarmSliceSystems();

        /// <summary>SWN-3: business liabilities (loans, payables) — honest BIZ-5 inputs.</summary>
        private BusinessLiabilityLedger liabilityLedger = new BusinessLiabilityLedger();
        private FinancialObligationAuthority financialObligationAuthority = new FinancialObligationAuthority();
        private CreditOfferWorkflow creditOfferWorkflow = new CreditOfferWorkflow();
        private CreditRegistry creditRegistry = new CreditRegistry();

        public EntityIdRegistry Ids => idRegistry ??= new EntityIdRegistry();
        public AnimalRegistry Animals => animalRegistry ??= new AnimalRegistry(Ids);
        public HouseholdLedgerRegistry HouseholdLedgers => householdLedgers ??= new HouseholdLedgerRegistry();
        /// <summary>Phase B: per-household real item-lot inventories.</summary>
        public HouseholdInventoryRegistry HouseholdInventories => householdInventories ??= new HouseholdInventoryRegistry();
        /// <summary>Phase B: per-household meal logs.</summary>
        public HouseholdMealLogRegistry MealLog => mealLog ??= new HouseholdMealLogRegistry();
        /// <summary>Phase B: household purchasing needs.</summary>
        public HouseholdNeedRegistry PurchasingNeeds => purchasingNeeds ??= new HouseholdNeedRegistry();
        public TaskAuthority Tasks => taskAuthority ??= new TaskAuthority();
        public SkillService Skills => skillService ??= new SkillService();
        public WorkTimeBudgetStore WorkTimeBudgets => workTimeBudgets ??= new WorkTimeBudgetStore();
        public GenericProductionRuntimeRegistry GenericProduction =>
            genericProductionRuntime ??= new GenericProductionRuntimeRegistry(Tasks, WorkTimeBudgets);
        public EnterpriseValuationReadModel Valuation => valuation ??= new EnterpriseValuationReadModel();
        public FreightResourcePool FreightPool => freightPool ??= new FreightResourcePool();
        public TransportAssetRegistry TransportAssets => transportAssets ??= new TransportAssetRegistry();

        // Pass IV Worker 2: the unified reservation authority (persisted),
        // the hub-owned draft-power service (reserves through the authority),
        // and the equipment capacity service (6 capacity models).
        private EquipmentReservationService reservations;
        private DraftPowerService draftPower;
        private EquipmentCapacityService equipmentCapacities;

        /// <summary>
        /// Pass IV: the ONE reservation authority — person time, equipment,
        /// workstations, workspaces, lots, motive power, animals, capacity.
        /// Persisted via the save pipeline.
        /// </summary>
        public EquipmentReservationService Reservations => reservations ??= new EquipmentReservationService();

        /// <summary>Pass IV: the unified authority as its contract interface.</summary>
        public IUnifiedReservationAuthority ReservationAuthority => Reservations;

        /// <summary>
        /// Pass IV: the hub-owned draft-power service. It reserves animals and
        /// drivers through <see cref="Reservations"/>; its hold index persists
        /// alongside the authority's holds.
        /// </summary>
        public DraftPowerService DraftPower => draftPower ??= new DraftPowerService(Reservations);

        /// <summary>
        /// Pass IV: equipment capacity semantics (6 models). Seeded with the
        /// authored kind profiles; instance lookups fall back through kinds.
        /// </summary>
        public EquipmentCapacityService EquipmentCapacities =>
            equipmentCapacities ??= CreateEquipmentCapacityService();

        private EquipmentCapacityService CreateEquipmentCapacityService()
        {
            var service = new EquipmentCapacityService(Reservations);
            EquipmentConditionProfiles.RegisterAll(service);
            service.KindLookup = ResolveEquipmentKind;
            return service;
        }

        /// <summary>Pass IV: resolves an equipment key to its kind for kind-level profile fallback.</summary>
        private string ResolveEquipmentKind(string equipmentKey)
        {
            if (string.IsNullOrWhiteSpace(equipmentKey)) return null;
            if (equipmentKey.StartsWith(ReservationKeyConventions.AssetPrefix, StringComparison.Ordinal))
            {
                string assetId = equipmentKey.Substring(ReservationKeyConventions.AssetPrefix.Length);
                var asset = TransportAssets.Get(assetId);
                return asset?.Kind;
            }
            return null;
        }

        /// <summary>
        /// Pass IV: installs the equipment execution gate (Canon 4.1) on the
        /// hub's TaskAuthority. Suppliers are wired to hub-owned truth — the
        /// unified reservation authority, the wagon asset registry, and the
        /// TimeManager timeline when a clock is supplied. No phantom equipment:
        /// kinds with no production register refuse loudly until provisioned.
        /// Called by SimulationDrivers in the production driver path.
        /// </summary>
        public void InstallEquipmentTaskGate(Func<float> nowGameSeconds = null)
        {
            var gate = new EquipmentTaskGate
            {
                AssetsSupplier = CollectGateAssets,
                KitsSupplier = CollectGateKits,
                AccessResolver = new EquipmentAccessResolver(),
                ReservationsSupplier = () => Reservations,
                NowGameSecondsSupplier = nowGameSeconds,
            };
            Tasks.SetExecutionGate(gate);
        }

        /// <summary>Pass IV: production equipment assets visible to the task gate.</summary>
        private List<EquipmentAsset> CollectGateAssets()
        {
            var assets = new List<EquipmentAsset>();
            foreach (var asset in TransportAssets.Assets)
                if (asset != null) assets.Add(asset);
            return assets;
        }

        /// <summary>
        /// Pass IV: production toolkits visible to the task gate. No production
        /// kit register exists yet (BusinessEquipmentRegister has no production
        /// holders) — kit requirements refuse loudly until kits are provisioned.
        /// </summary>
        private List<ToolKitInstance> CollectGateKits() => new List<ToolKitInstance>();
        public FarmFlowLedger FarmFlows => farmFlows ??= new FarmFlowLedger();
        public BusinessCapabilityRegistry Capabilities => capabilityRegistry ??= new BusinessCapabilityRegistry();
        public BusinessOperatingLedger OperatingLedger => operatingLedger ??= new BusinessOperatingLedger();
        public EmploymentRelationshipRegistry Employments => employments ??= new EmploymentRelationshipRegistry();
        public FarmSliceSystems FarmSlice => farmSlice ??= new FarmSliceSystems();
        /// <summary>SWN-3: the business liability ledger (valuation-attached).</summary>
        public BusinessLiabilityLedger Liabilities
        {
            get
            {
                liabilityLedger ??= new BusinessLiabilityLedger();
                liabilityLedger.AttachValuation(Valuation);
                liabilityLedger.AttachFinancialAuthority(FinancialObligations);
                return liabilityLedger;
            }
        }

        /// <summary>
        /// Shared finance authority. Compatibility ledgers and instrument registries
        /// should project into this authority rather than own a second balance.
        /// </summary>
        public FinancialObligationAuthority FinancialObligations =>
            financialObligationAuthority ??= new FinancialObligationAuthority();

        /// <summary>Shared request/offer negotiation workflow over the finance authority.</summary>
        public CreditOfferWorkflow CreditOffers => creditOfferWorkflow ??= new CreditOfferWorkflow();

        /// <summary>Document/instrument index bound to the shared obligation authority.</summary>
        public CreditRegistry CreditInstruments
        {
            get
            {
                creditRegistry ??= new CreditRegistry();
                creditRegistry.AttachFinancialAuthority(FinancialObligations);
                return creditRegistry;
            }
        }

        private PostalService postalService;

        /// <summary>NX-2A: the postal network authority (offices, mail, contracts).</summary>
        public PostalService Postal => postalService ??= new PostalService();

        // P6: travel & communication authorities. The hub owns them so the
        // drivers (CLN-2) can advance them daily and the save pipeline (CLN-1)
        // can round-trip them. Scene bootstrap registers the actual content
        // (journey locations/edges, post offices, weather seed) — the hub
        // never invents geography.
        private JourneyModel journeyModel;
        private RouteConditionService routeConditions;
        private MoneyOrderService moneyOrderService;
        private RegisteredMailService registeredMailService;
        private RecruitmentService recruitmentService;
        private int weatherSeed;

        // Real People / shared-credit authorities introduced by the Muse
        // integration. They live here so runtime wiring and persistence use
        // the same instances; none of these services owns a second cash,
        // inventory, or debt ledger.
        private PersonScheduleTracker personScheduleTracker;
        private RentCollectionService rentCollectionService;
        private NpcFormationAssetRegister npcFormationAssetRegister;
        private NpcBusinessLifecycleService npcBusinessLifecycle;
        private NpcBusinessCreationAdapter npcBusinessCreation;
        private readonly List<NpcBusinessLifeRecord> npcBusinessLifeRecords = new List<NpcBusinessLifeRecord>();
        private NpcBusinessInvestigation npcBusinessInvestigation;
        private NpcOpportunityObservationLog npcOpportunityObservation;
        private NpcBusinessEventLog npcBusinessEvents;
        private NpcPropertyLifeService npcPropertyLife;
        private ConstructionProjectExecutor constructionProjectExecutor;
        private readonly List<ConstructionProject> constructionProjects = new List<ConstructionProject>();
        private NpcConstructionCommissioner npcConstructionCommissioner;
        private TradeCreditBook tradeCreditBook;
        private CreditEventLog creditEventLog;
        private CreditCashBridge creditCashBridge;

        // Autonomous Labor pass: labor-market authorities. Plain C# services
        // owned by the hub; none owns a second cash, inventory, or debt ledger.
        // DelegationService is an orphan rescue — it existed with tests but was
        // never registered or driven.
        private ContactGraphService contactGraphService;
        private EmploymentNegotiationService employmentNegotiationService;
        private LaborDemandAssessor laborDemandAssessor;
        private WorkScheduler workScheduler;
        private WageSettlementService wageSettlementService;
        private ManagerBrain managerBrain;
        private LaborMigrationLinker laborMigrationLinker;
        private DelegationService delegationService;
        private TenancyPaymentService tenancyPaymentService;

        // Pass III land core (Worker A): legal parcel/holding identity, the single
        // non-title rights store, and the atomic subdivision coordinator. None owns
        // a second cash, debt, or title ledger — title stays in TitleAuthority.
        private ParcelService parcelService;
        private PropertyInterestStore propertyInterestStore;
        private ParcelSubdivisionService parcelSubdivisionService;
        private HouseholdDecisionService householdDecisionService;
        private HouseholdDecisionDriver householdDecisionDriver;
        private DomesticLaborService domesticLaborService;
        private HouseholdAutonomyDriver householdAutonomyDriver;
        private HousingSearchNeedRegistry housingSearchNeedRegistry;

        /// <summary>P6: the JRN-1 journey model — the one routed distance/time authority.</summary>
        public JourneyModel Journeys => journeyModel ??= new JourneyModel();

        /// <summary>
        /// P6: NX-2C weather/route conditions. Wired as the journey model's
        /// condition provider on Awake and after every load (the provider is
        /// not serialized — re-attachment is the hub's job).
        /// </summary>
        public RouteConditionService RouteConditions => routeConditions ??= new RouteConditionService();

        /// <summary>P6: D4E postal money-order books (bound to the hub's postal service).</summary>
        public MoneyOrderService MoneyOrders => moneyOrderService ??= new MoneyOrderService(Postal);

        /// <summary>P6: D4F registered-mail custody chain (subscribes to the hub's postal handoffs).</summary>
        public RegisteredMailService RegisteredMail => registeredMailService ??= new RegisteredMailService(Postal);

        /// <summary>P6: T2B recruitment workflows (efforts, inquiries, letters).</summary>
        public RecruitmentService Recruiting => recruitmentService ??= new RecruitmentService();

        public PersonScheduleTracker PersonSchedules => personScheduleTracker ??= new PersonScheduleTracker();
        public RentCollectionService RentCollection => rentCollectionService ??= new RentCollectionService();
        public NpcFormationAssetRegister NpcFormationAssets => npcFormationAssetRegister ??= new NpcFormationAssetRegister();
        public NpcBusinessLifecycleService NpcBusinessLifecycle => npcBusinessLifecycle ??= new NpcBusinessLifecycleService();
        public NpcBusinessCreationAdapter NpcBusinessCreation => npcBusinessCreation ??= new NpcBusinessCreationAdapter(Ids, Capabilities);
        public IList<NpcBusinessLifeRecord> NpcBusinessLifeRecords => npcBusinessLifeRecords;
        public NpcBusinessInvestigation NpcBusinessInvestigation => npcBusinessInvestigation ??= new NpcBusinessInvestigation();
        public NpcOpportunityObservationLog NpcOpportunityObservation => npcOpportunityObservation ??= new NpcOpportunityObservationLog();
        public NpcBusinessEventLog NpcBusinessEvents => npcBusinessEvents ??= new NpcBusinessEventLog();
        public NpcPropertyLifeService NpcPropertyLife => npcPropertyLife ??= new NpcPropertyLifeService();
        public ConstructionProjectExecutor ConstructionExecutor => constructionProjectExecutor ??= new ConstructionProjectExecutor();
        public IList<ConstructionProject> ConstructionProjects => constructionProjects;
        public NpcConstructionCommissioner NpcConstructionCommissioner => npcConstructionCommissioner ??= new NpcConstructionCommissioner();
        public TradeCreditBook TradeCredit => tradeCreditBook ??= new TradeCreditBook();
        public CreditEventLog CreditEvents => creditEventLog ??= new CreditEventLog();
        /// <summary>
        /// Transient binding layer for finance workflows. The bridge never owns
        /// balances; it is repopulated from the authoritative cash stores after
        /// boot/load.
        /// </summary>
        public CreditCashBridge CreditCash => creditCashBridge ??= new CreditCashBridge();

        /// <summary>
        /// Autonomous Labor: known-contacts authority for recruitment.
        /// Only documented links yield candidates — never an omniscient list.
        /// </summary>
        public ContactGraphService ContactGraph => contactGraphService ??= new ContactGraphService();

        /// <summary>
        /// Autonomous Labor: negotiated employment offers (offer/counter/
        /// accept/refuse/withdraw/expire). Authority predicate = business owner
        /// (via <see cref="IsBusinessOwner"/>) or HireStaff delegation.
        /// </summary>
        public EmploymentNegotiationService EmploymentNegotiation =>
            employmentNegotiationService ??= new EmploymentNegotiationService(OfferAuthorityPredicate);

        /// <summary>Autonomous Labor: workload-derived labor demand (read-only).</summary>
        public LaborDemandAssessor LaborDemand => laborDemandAssessor ??= new LaborDemandAssessor();

        /// <summary>Autonomous Labor: assigns queued work to employed persons.</summary>
        public WorkScheduler WorkScheduling => workScheduler ??= new WorkScheduler();

        /// <summary>Autonomous Labor: idempotency-guarded wage settlement.</summary>
        public WageSettlementService WageSettlement => wageSettlementService ??= new WageSettlementService();

        /// <summary>Autonomous Labor: delegated-manager decision loop.</summary>
        public ManagerBrain Managers => managerBrain ??= new ManagerBrain();

        /// <summary>Autonomous Labor: bounded employment-linked migration evaluation.</summary>
        public LaborMigrationLinker MigrationLinker => laborMigrationLinker ??= new LaborMigrationLinker();

        /// <summary>
        /// Autonomous Labor: orphan rescue. DelegationService shipped with tests
        /// but was never registered or driven; it is owned here now.
        /// </summary>
        public DelegationService Delegation => delegationService ??= new DelegationService();

        /// <summary>
        /// Pass III Worker A: legal parcel/holding identity + plot linkage
        /// (ParcelRecord/HoldingRecord). Ownership truth stays in TitleAuthority;
        /// this service never answers "who owns it".
        /// </summary>
        public ParcelService Parcels => parcelService ??= new ParcelService();

        /// <summary>Attaches the existing world grid and title authority to land services.</summary>
        public void AttachParcelWorld(TownWorldController world, TitleAuthority titles)
        {
            if (world == null) return;
            Parcels.Grid = world.Grid;
            ParcelSubdivisions.BuildingIndex = new TownWorldParcelBuildingIndex(world);
            if (titles != null)
                ParcelSubdivisions.TitleHolderLookup = titles.CurrentHolder;
        }

        /// <summary>
        /// Pass III Worker A: the single non-title rights store
        /// (lease/easement/charge/entry/ROW/restriction/dispute/lien).
        /// </summary>
        public PropertyInterestStore PropertyInterests => propertyInterestStore ??= new PropertyInterestStore();

        /// <summary>
        /// Pass III Worker A: atomic subdivision coordinator, wired to the parcel
        /// and interest authorities. Game wiring (Codex) sets BuildingIndex from
        /// PlacedBuilding footprints and TitleHolderLookup from the title chain.
        /// </summary>
        public ParcelSubdivisionService ParcelSubdivisions =>
            parcelSubdivisionService ??= new ParcelSubdivisionService(Parcels, PropertyInterests);

        /// <summary>
        /// Pass III Worker B: the general rent/board payment loop. Owned by
        /// the hub like the other Pass II/III plain-C# services; game wiring
        /// (Codex) drives SettleDueRents once per simulated day and supplies
        /// the ITenancyPayeeDirectory.
        /// </summary>
        public TenancyPaymentService TenancyPayments => tenancyPaymentService ??= new TenancyPaymentService();
        /// Pass III Worker C: household financial pressure -> bounded decision
        /// (pure evaluator; the autonomy driver maps real state onto its input).
        /// </summary>
        public HouseholdDecisionService HouseholdDecisions =>
            householdDecisionService ??= new HouseholdDecisionService();

        /// <summary>
        /// Pass III Worker C: turns household decisions into real operations
        /// (opportunity discovery, housing search, obligations, negotiation
        /// requests, spending policies). Owns the opportunity log.
        /// </summary>
        public HouseholdDecisionDriver HouseholdDecisionDriver =>
            householdDecisionDriver ??= new HouseholdDecisionDriver(
                ContactGraph, Employments, HousingSearchNeeds, FinancialObligations, Ids, CreditCash);

        /// <summary>
        /// Pass III Worker C: narrow domestic labor (meal prep, fuel, animal
        /// care, chores) on the existing TaskAuthority + person-time.
        /// </summary>
        public DomesticLaborService DomesticLabor =>
            domesticLaborService ??= new DomesticLaborService();

        /// <summary>
        /// Pass III Worker C: NPC autonomous household responses (brief §13) —
        /// declining reserves, rent due, job loss, housing change.
        /// </summary>
        public HouseholdAutonomyDriver HouseholdAutonomy =>
            householdAutonomyDriver ??= new HouseholdAutonomyDriver();

        /// <summary>
        /// Pass III Worker C: hub-owned housing-search registry used by the
        /// RelocateCheaper decision path.
        /// </summary>
        public HousingSearchNeedRegistry HousingSearchNeeds =>
            housingSearchNeedRegistry ??= new HousingSearchNeedRegistry();

        /// <summary>
        /// Autonomous Labor: owner lookup hook for the offer authority predicate.
        /// Game wiring (Codex) sets this from the business registry. When unset,
        /// offers require HireStaff delegation.
        /// </summary>
        [NonSerialized]
        public Func<string, int, bool> IsBusinessOwner;

        /// <summary>
        /// Autonomous Labor: current absolute day index for authority checks.
        /// Set daily by SimulationDrivers; defaults to 0 (delegations with
        /// day bounds should not be evaluated before the first drive).
        /// </summary>
        public int LaborDayIndex { get; set; }

        private bool OfferAuthorityPredicate(string businessInstanceId, int personId, string act)
        {
            if (IsBusinessOwner != null)
            {
                try
                {
                    if (IsBusinessOwner(businessInstanceId, personId)) return true;
                }
                catch (Exception)
                {
                    // Owner hook failed — fall through to delegation check.
                }
            }
            if (!string.Equals(act, "HireStaff", StringComparison.OrdinalIgnoreCase)) return false;
            var diag = new List<string>();
            return Delegation.CheckAct(
                businessInstanceId, personId, DelegatedAct.HireStaff, 0, LaborDayIndex, diag)
                == AuthorityVerdict.Permitted;
        }

        /// <summary>
        /// P6: weather seed for the route-condition service. Bootstrap-assigned
        /// from the world's canonical seed (deterministic weather); persisted
        /// in the save DTO so a loaded game continues the same weather stream.
        /// </summary>
        public int WeatherSeed
        {
            get => weatherSeed;
            set => weatherSeed = Math.Max(0, value);
        }

        private AgriculturalRiskService riskService;
        private LivestockDiseaseService diseaseService;

        /// <summary>NX-2B: agricultural disasters (grasshoppers, prairie fire, drought).</summary>
        public AgriculturalRiskService Risk => riskService ??= new AgriculturalRiskService();

        /// <summary>NX-2B: ranch-localized livestock disease outbreaks.</summary>
        public LivestockDiseaseService Disease => diseaseService ??= new LivestockDiseaseService();

        public IReadOnlyList<ButcherRuntime> ButcherRuntimes => butcherRuntimes;

        /// <summary>
        /// P2: W2B/W2C/W3B nutrition-link registries. Restaurant, boarding-house
        /// and hotel shop runtimes (or their meal-day ledgers) register their
        /// served-meal sources here so the daily-needs driver can count real
        /// prepared meals (Canon §2.5) instead of double-feeding households
        /// whose members genuinely ate out. Transient: the ledgers persist via
        /// their owning runtimes' own save DTOs; owners re-register after load.
        /// Empty registries mean no meals are counted — never estimates.
        /// </summary>
        private readonly List<IRestaurantMealDaySource> restaurantMealSources = new List<IRestaurantMealDaySource>();
        private readonly List<IBoardingHouseMealDaySource> boardingHouseMealSources = new List<IBoardingHouseMealDaySource>();
        private readonly List<IHotelMealDaySource> hotelMealSources = new List<IHotelMealDaySource>();

        public IReadOnlyList<IRestaurantMealDaySource> RestaurantMealSources => restaurantMealSources;
        public IReadOnlyList<IBoardingHouseMealDaySource> BoardingHouseMealSources => boardingHouseMealSources;
        public IReadOnlyList<IHotelMealDaySource> HotelMealSources => hotelMealSources;

        public void RegisterRestaurantMealSource(IRestaurantMealDaySource source)
        {
            if (source != null && !restaurantMealSources.Contains(source))
            {
                restaurantMealSources.Add(source);
            }
        }

        public void RegisterBoardingHouseMealSource(IBoardingHouseMealDaySource source)
        {
            if (source != null && !boardingHouseMealSources.Contains(source))
            {
                boardingHouseMealSources.Add(source);
            }
        }

        public void RegisterHotelMealSource(IHotelMealDaySource source)
        {
            if (source != null && !hotelMealSources.Contains(source))
            {
                hotelMealSources.Add(source);
            }
        }

        /// <summary>
        /// P2: builds the per-day meal-source composites the daily-needs
        /// service consumes. Empty registries yield empty composites (which
        /// report zero meals — identical to passing null). Public + static so
        /// EditMode tests can verify composition without a scene.
        /// </summary>
        public static void BuildMealSourceComposites(
            IReadOnlyList<IRestaurantMealDaySource> restaurantSources,
            IReadOnlyList<IBoardingHouseMealDaySource> boardingSources,
            IReadOnlyList<IHotelMealDaySource> hotelSources,
            out CompositeRestaurantMealSource restaurants,
            out CompositeBoardingHouseMealSource boardingHouses,
            out CompositeHotelMealSource hotels)
        {
            restaurants = new CompositeRestaurantMealSource(restaurantSources);
            boardingHouses = new CompositeBoardingHouseMealSource(boardingSources);
            hotels = new CompositeHotelMealSource(hotelSources);
        }

        /// <summary>Registers a butcher runtime for save tracking (one per butcher business).</summary>
        public ButcherRuntime GetOrCreateButcherRuntime(string businessInstanceId, string productionSiteId, string retailSiteId)
        {
            butcherRuntimes ??= new List<ButcherRuntime>();
            foreach (ButcherRuntime existing in butcherRuntimes)
            {
                if (existing != null && string.Equals(existing.BusinessInstanceId, businessInstanceId, StringComparison.Ordinal))
                {
                    return existing;
                }
            }

            var runtime = new ButcherRuntime(businessInstanceId, productionSiteId, retailSiteId);
            butcherRuntimes.Add(runtime);
            return runtime;
        }

        private void Awake()
        {
            idRegistry ??= new EntityIdRegistry();
            animalRegistry ??= new AnimalRegistry(idRegistry);
            householdLedgers ??= new HouseholdLedgerRegistry();
            taskAuthority ??= new TaskAuthority();
            skillService ??= new SkillService();
            workTimeBudgets ??= new WorkTimeBudgetStore();
            valuation ??= new EnterpriseValuationReadModel();
            freightPool ??= new FreightResourcePool();
            transportAssets ??= new TransportAssetRegistry();
            // Pass IV Worker 2: hub-owned reservation + capacity authorities.
            // The freight pool shares the hub's draft-power service (one animal/
            // driver truth) and reconciles its wagons onto the asset registry
            // (one wagon condition/reservation truth).
            reservations ??= new EquipmentReservationService();
            draftPower ??= new DraftPowerService(reservations);
            equipmentCapacities ??= CreateEquipmentCapacityService();
            freightPool.AttachDraftPower(draftPower);
            freightPool.AttachWagonRegistry(transportAssets);
            var wagonDiag = new List<string>();
            freightPool.MigrateWagonsToRegistry(wagonDiag);
            butcherRuntimes ??= new List<ButcherRuntime>();
            farmFlows ??= new FarmFlowLedger();
            capabilityRegistry ??= new BusinessCapabilityRegistry();
            operatingLedger ??= new BusinessOperatingLedger();
            employments ??= new EmploymentRelationshipRegistry();
            farmSlice ??= new FarmSliceSystems();
            liabilityLedger ??= new BusinessLiabilityLedger();
            liabilityLedger.AttachValuation(valuation);
            financialObligationAuthority ??= new FinancialObligationAuthority();
            liabilityLedger.AttachFinancialAuthority(financialObligationAuthority);
            creditRegistry ??= new CreditRegistry();
            creditRegistry.AttachFinancialAuthority(financialObligationAuthority);
            PlayerDebtManager playerDebt = FindAnyObjectByType<PlayerDebtManager>();
            playerDebt?.AttachFinancialAuthority(financialObligationAuthority, idRegistry);
            postalService ??= new PostalService();
            // P6: travel & communication authorities. The condition provider is
            // not serialized — (re-)attachment is the hub's job, here and after
            // every load.
            journeyModel ??= new JourneyModel();
            routeConditions ??= new RouteConditionService();
            journeyModel.ConditionProvider = routeConditions;
            moneyOrderService ??= new MoneyOrderService(postalService);
            registeredMailService ??= new RegisteredMailService(postalService);
            recruitmentService ??= new RecruitmentService();
            riskService ??= new AgriculturalRiskService();
            diseaseService ??= new LivestockDiseaseService();
            personScheduleTracker ??= new PersonScheduleTracker();
            rentCollectionService ??= new RentCollectionService();
            npcFormationAssetRegister ??= new NpcFormationAssetRegister();
            npcBusinessLifecycle ??= new NpcBusinessLifecycleService();
            npcBusinessCreation ??= new NpcBusinessCreationAdapter(Ids, Capabilities);
            npcBusinessInvestigation ??= new NpcBusinessInvestigation();
            npcOpportunityObservation ??= new NpcOpportunityObservationLog();
            npcBusinessEvents ??= new NpcBusinessEventLog();
            npcPropertyLife ??= new NpcPropertyLifeService();
            constructionProjectExecutor ??= new ConstructionProjectExecutor();
            npcConstructionCommissioner ??= new NpcConstructionCommissioner();
            tradeCreditBook ??= new TradeCreditBook();
            creditEventLog ??= new CreditEventLog();
            creditCashBridge ??= new CreditCashBridge();
            liabilityLedger.SyncAllToValuation(null);

            // P1: boot-time task-definition registration. Every static task catalog
            // registers its definitions here — before this pass, NO production caller
            // ever registered any catalog, so the first CreateTask for freight,
            // travel, farm, or shop work threw "Unknown TaskDefinitionId".
            // Registration is idempotent (the authority rejects duplicates).
            RegisterTaskCatalogs(taskAuthority, skillService, postalService, null);
        }

        /// <summary>
        /// P1: registers every static task catalog with the hub's TaskAuthority.
        /// Public + static so EditMode tests can verify coverage without a scene.
        /// </summary>
        public static void RegisterTaskCatalogs(
            TaskAuthority tasks,
            SkillService skills,
            PostalService postal,
            List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            if (tasks == null)
            {
                diagnostics.Add("RegisterTaskCatalogs: no TaskAuthority — task definitions not registered.");
                return;
            }

            // Shop/service catalogs.
            GeneralStoreTaskCatalog.RegisterAll(tasks);
            BakeryBreadCatalog.RegisterAll(tasks);
            BarberServiceCatalog.RegisterAll(tasks);
            DoctorTreatmentCatalog.RegisterAll(tasks);
            NewspaperTaskCatalog.RegisterAll(tasks);
            RestaurantMealCatalog.RegisterAll(tasks);
            TailorGarmentCatalog.RegisterAll(tasks);

            // Mine + mill catalogs.
            MineAssayTaskCatalog.Register(tasks, diagnostics);
            MineHoistingTaskCatalog.Register(tasks, diagnostics);
            MineLaborRegister.MineLaborTaskCatalog.Register(tasks, diagnostics);
            MineShaftTaskCatalog.Register(tasks, diagnostics);
            GrainMillMaintenanceTasks.RegisterTaskDefinitions(tasks, diagnostics);
            SawmillMaintenanceTasks.RegisterTaskDefinitions(tasks, diagnostics);
            LoggingTaskDefinitions.RegisterTaskDefinitions(tasks, skills, diagnostics);

            // Farming chains.
            CropChain.RegisterTaskDefinitions(tasks);
            Miller.RegisterTaskDefinitions(tasks);
            CheeseChain.RegisterTaskDefinitions(tasks);
            CreameryLabor.RegisterTaskDefinitions(tasks);
            DairyChain.RegisterTaskDefinitions(tasks);
            FarmConstruction.RegisterTaskDefinitions(tasks);
            FeedLoop.RegisterTaskDefinitions(tasks);
            PoultryChain.RegisterTaskDefinitions(tasks);
            PigSheepChain.RegisterTaskDefinitions(tasks);
            TimberHarvest.RegisterTaskDefinitions(tasks);
            Sawmill.RegisterTaskDefinitions(tasks);

            // Travel + freight + post.
            JourneyTravel.RegisterTaskDefinitions(tasks);
            FreightCompanyRuntime.RegisterTaskDefinitions(tasks);
            postal?.RegisterTaskDefinitions(tasks);
            DomesticTaskCatalog.RegisterAll(tasks, diagnostics);

            diagnostics.Add("RegisterTaskCatalogs: all static task catalogs registered (duplicates rejected).");
        }

        /// <summary>
        /// CLN-1: captures every owned authority's state for the save pipeline.
        /// </summary>
        public SystemsSaveDto CaptureSaveDto()
        {
            var dto = new SystemsSaveDto
            {
                tasks = Tasks.CaptureSaveDto(),
                skills = Skills.CaptureSaveDto(),
                valuation = Valuation.CaptureSaveDto(),
                freightPool = FreightPool.CaptureSaveDto(),
                transportAssets = TransportAssets.CaptureSaveDto(),
                farmFlows = FarmFlows.CaptureSaveDto(),
                capabilities = Capabilities.CaptureSaveDto(),
                operatingLedger = OperatingLedger.CaptureSaveDto(),
                employments = Employments.CaptureSaveDto(),
                farmSlice = FarmSlice.CaptureSaveDto(),
                liabilities = Liabilities.CaptureSaveDto(),
                financialObligations = FinancialObligations.CaptureSaveDto(),
                creditOffers = CreditOffers.CaptureSaveDto(),
                creditInstruments = CreditInstruments.CaptureSaveDto(),
                postal = Postal.CaptureSaveDto(),
                risk = Risk.CaptureSaveDto(),
                disease = Disease.CaptureSaveDto(),
                journeys = Journeys.CaptureSaveDto(),
                routeConditions = RouteConditions.CaptureSaveDto(),
                moneyOrders = MoneyOrders.CaptureSaveDto(),
                registeredMail = RegisteredMail.CaptureSaveDto(),
                recruiting = Recruiting.CaptureSaveDto(),
                weatherSeed = weatherSeed,
                personSchedule = PersonSchedules.CaptureSaveDto(),
                rentCollection = RentCollection.CaptureSaveDto(),
                npcFormationAssets = NpcFormationAssets.CaptureSaveDto(),
                npcBusinessLifecycle = CaptureNpcBusinessLifecycle(),
                npcBusinessInvestigation = NpcBusinessInvestigation.CaptureSaveDto(),
                npcOpportunityObservation = NpcOpportunityObservation.CaptureSaveDto(),
                npcBusinessEvents = NpcBusinessEvents.CaptureSaveDto(),
                npcPropertyLife = NpcPropertyLife.CaptureSaveDto(),
                constructionProjects = ConstructionExecutor.CaptureSaveDto(new List<ConstructionProject>(ConstructionProjects)),
                npcConstructionCommissioner = NpcConstructionCommissioner.CaptureSaveDto(),
                tradeCredit = TradeCredit.CaptureSaveDto(),
                creditEvents = CreditEvents.CaptureSaveDto(),
                contactGraph = ContactGraph.CaptureSaveDto(),
                employmentNegotiation = EmploymentNegotiation.CaptureSaveDto(),
                wageSettlement = WageSettlement.CaptureSaveDto(),
                managerBrain = Managers.CaptureSaveDto(),
                delegation = Delegation.CaptureSaveDto(),
                parcels = Parcels.CaptureSaveDto(),
                propertyInterests = PropertyInterests.CaptureSaveDto(),
                tenancyPayments = TenancyPayments.CaptureSaveDto(),
                householdDecisions = HouseholdDecisions.CaptureSaveDto(),
                householdDecisionDriver = HouseholdDecisionDriver.CaptureSaveDto(),
                domesticLabor = DomesticLabor.CaptureSaveDto(),
                householdAutonomy = HouseholdAutonomy.CaptureSaveDto(),
                // Pass IV Worker 2: unified reservations, draft holds, capacity registrations.
                reservations = Reservations.CaptureSaveDto(),
                draftPower = DraftPower.CaptureSaveDto(),
                equipmentCapacities = EquipmentCapacities.CaptureSaveDto(),
            };

            dto.workTimeBudgets = WorkTimeBudgets.CaptureSaveDto();

            butcherRuntimes ??= new List<ButcherRuntime>();
            foreach (ButcherRuntime runtime in butcherRuntimes)
            {
                if (runtime != null)
                {
                    dto.butcherRuntimes.Add(runtime.CaptureSaveDto());
                }
            }

            return dto;
        }

        /// <summary>
        /// CLN-1: restores every owned authority's state from the save pipeline.
        /// Returns diagnostics for anything that could not be restored.
        /// </summary>
        public List<string> LoadFromSaveDto(SystemsSaveDto dto, int absoluteDayIndex)
        {
            var diagnostics = new List<string>();
            if (dto == null)
            {
                return diagnostics;
            }

            Tasks.LoadFromSaveDto(dto.tasks);
            Skills.LoadFromSaveDto(dto.skills);
            WorkTimeBudgets.LoadFromSaveDto(dto.workTimeBudgets, absoluteDayIndex);
            Valuation.LoadFromSaveDto(dto.valuation);
            // Pass IV Worker 2: restore the reservation authority BEFORE the
            // draft-power hold index (the index verifies against the authority).
            Reservations.LoadFromSaveDto(dto.reservations);
            FreightPool.LoadFromSaveDto(dto.freightPool);
            TransportAssets.LoadFromSaveDto(dto.transportAssets);
            FarmFlows.LoadFromSaveDto(dto.farmFlows);
            Capabilities.LoadFromSaveDto(dto.capabilities, diagnostics);
            OperatingLedger.LoadFromSaveDto(dto.operatingLedger);
            Employments.LoadFromSaveDto(dto.employments);
            FarmSlice.LoadFromSaveDto(dto.farmSlice);
            FinancialObligations.LoadFromSaveDto(dto.financialObligations);
            CreditOffers.LoadFromSaveDto(dto.creditOffers);
            CreditInstruments.LoadFromSaveDto(dto.creditInstruments);
            diagnostics.AddRange(FinancialObligations.Reconcile());
            Liabilities.LoadFromSaveDto(dto.liabilities);
            Postal.LoadFromSaveDto(dto.postal);
            Risk.LoadFromSaveDto(dto.risk);
            Disease.LoadFromSaveDto(dto.disease);
            Journeys.LoadFromSaveDto(dto.journeys);
            RouteConditions.LoadFromSaveDto(dto.routeConditions);
            MoneyOrders.LoadFromSaveDto(dto.moneyOrders);
            RegisteredMail.LoadFromSaveDto(dto.registeredMail);
            Recruiting.LoadFromSaveDto(dto.recruiting);
            PersonSchedules.LoadFromSaveDto(dto.personSchedule);
            RentCollection.LoadFromSaveDto(dto.rentCollection);
            NpcFormationAssets.LoadFromSaveDto(dto.npcFormationAssets);
            npcBusinessLifeRecords.Clear();
            if (dto.npcBusinessLifecycle != null)
            {
                foreach (NpcBusinessLifeRecordSaveDto recordDto in dto.npcBusinessLifecycle)
                {
                    if (recordDto == null || string.IsNullOrWhiteSpace(recordDto.BusinessInstanceId)) continue;
                    var record = new NpcBusinessLifeRecord();
                    record.LoadFromSaveDto(recordDto);
                    npcBusinessLifeRecords.Add(record);
                }
            }
            NpcBusinessInvestigation.LoadFromSaveDto(dto.npcBusinessInvestigation);
            NpcOpportunityObservation.LoadFromSaveDto(dto.npcOpportunityObservation);
            NpcBusinessEvents.LoadFromSaveDto(dto.npcBusinessEvents);
            NpcPropertyLife.LoadFromSaveDto(dto.npcPropertyLife, diagnostics);
            ConstructionProjects.Clear();
            foreach (ConstructionProject project in ConstructionExecutor.LoadFromSaveDto(dto.constructionProjects, diagnostics))
            {
                if (project != null) ConstructionProjects.Add(project);
            }
            NpcConstructionCommissioner.LoadFromSaveDto(dto.npcConstructionCommissioner, diagnostics);
            TradeCredit.LoadFromSaveDto(dto.tradeCredit);
            CreditEvents.LoadFromSaveDto(dto.creditEvents);
            ContactGraph.LoadFromSaveDto(dto.contactGraph);
            EmploymentNegotiation.LoadFromSaveDto(dto.employmentNegotiation);
            WageSettlement.LoadFromSaveDto(dto.wageSettlement);
            Managers.LoadFromSaveDto(dto.managerBrain);
            Delegation.LoadFromSaveDto(dto.delegation);
            Parcels.LoadFromSaveDto(dto.parcels);
            PropertyInterests.LoadFromSaveDto(dto.propertyInterests);
            TenancyPayments.LoadFromSaveDto(dto.tenancyPayments);
            HouseholdDecisions.LoadFromSaveDto(dto.householdDecisions);
            HouseholdDecisionDriver.LoadFromSaveDto(dto.householdDecisionDriver);
            DomesticLabor.LoadFromSaveDto(dto.domesticLabor);
            HouseholdAutonomy.LoadFromSaveDto(dto.householdAutonomy);
            // Pass IV Worker 2: draft-power hold index verifies against the
            // restored authority; capacity registrations restore in step with
            // authority capacity limits. The freight pool re-attaches to the
            // hub-owned services and migrates legacy wagons (idempotent).
            DraftPower.LoadFromSaveDto(dto.draftPower);
            EquipmentCapacities.LoadFromSaveDto(dto.equipmentCapacities);
            FreightPool.AttachDraftPower(DraftPower);
            FreightPool.AttachWagonRegistry(TransportAssets);
            FreightPool.MigrateWagonsToRegistry(diagnostics);
            weatherSeed = Math.Max(0, dto.weatherSeed);
            // The condition provider is not serialized — re-attach after load.
            Journeys.ConditionProvider = RouteConditions;

            butcherRuntimes ??= new List<ButcherRuntime>();
            butcherRuntimes.Clear();
            if (dto.butcherRuntimes != null)
            {
                foreach (ButcherRuntimeSaveDto runtimeDto in dto.butcherRuntimes)
                {
                    if (runtimeDto == null || string.IsNullOrWhiteSpace(runtimeDto.businessInstanceId))
                    {
                        continue;
                    }

                    var runtime = new ButcherRuntime(
                        runtimeDto.businessInstanceId,
                        runtimeDto.productionSiteId,
                        runtimeDto.retailSiteId);
                    runtime.LoadFromSaveDto(runtimeDto);
                    butcherRuntimes.Add(runtime);
                }
            }

            return diagnostics;
        }

        private List<NpcBusinessLifeRecordSaveDto> CaptureNpcBusinessLifecycle()
        {
            var saved = new List<NpcBusinessLifeRecordSaveDto>();
            foreach (NpcBusinessLifeRecord record in npcBusinessLifeRecords)
            {
                if (record != null && !string.IsNullOrWhiteSpace(record.BusinessInstanceId))
                {
                    saved.Add(record.CaptureSaveDto());
                }
            }
            return saved;
        }

        /// <summary>
        /// CLN-1: exports animal state for the PopulationSaveDto section.
        /// </summary>
        public void ExportAnimalState(
            List<AnimalState> outAnimals,
            List<HistoricalAnimalRecord> outHistorical,
            List<LivestockCohort> outCohorts,
            List<EggBatchState> outEggBatches)
        {
            Animals.ExportState(outAnimals, outHistorical, outCohorts, outEggBatches);
        }

        /// <summary>CLN-1: imports animal state from the PopulationSaveDto section.</summary>
        public void ImportAnimalState(
            IEnumerable<AnimalState> inAnimals,
            IEnumerable<HistoricalAnimalRecord> inHistorical,
            IEnumerable<LivestockCohort> inCohorts,
            IEnumerable<EggBatchState> inEggBatches)
        {
            Animals.ImportState(inAnimals, inHistorical, inCohorts, inEggBatches);
        }

        /// <summary>CLN-1: exports household ledgers for the PopulationSaveDto section.</summary>
        public void ExportHouseholdLedgers(List<HouseholdLedgerState> outStates)
        {
            HouseholdLedgers.ExportState(outStates);
        }

        /// <summary>CLN-1: imports household ledgers from the PopulationSaveDto section.</summary>
        public void ImportHouseholdLedgers(IEnumerable<HouseholdLedgerState> inStates)
        {
            HouseholdLedgers.ImportState(inStates);
        }

        /// <summary>Phase B: exports household inventories for the PopulationSaveDto section.</summary>
        public void ExportHouseholdInventories(List<HouseholdInventoryState> outStates)
        {
            HouseholdInventories.ExportState(outStates);
        }

        /// <summary>Phase B: imports household inventories from the PopulationSaveDto section.</summary>
        public void ImportHouseholdInventories(IEnumerable<HouseholdInventoryState> inStates)
        {
            HouseholdInventories.ImportState(inStates);
        }

        /// <summary>Phase B: exports meal records for the PopulationSaveDto section.</summary>
        public void ExportMealLog(
            List<HouseholdMealRecord> outMeals,
            List<MissedMealRecord> outMissed,
            List<MealPreparationRecord> outPreparations)
        {
            MealLog.ExportState(outMeals, outMissed, outPreparations);
        }

        /// <summary>Phase B: imports meal records from the PopulationSaveDto section.</summary>
        public void ImportMealLog(
            IEnumerable<HouseholdMealRecord> meals,
            IEnumerable<MissedMealRecord> missed,
            IEnumerable<MealPreparationRecord> preparations)
        {
            MealLog.ImportState(meals, missed, preparations);
        }

        /// <summary>Phase B: exports purchasing needs for the PopulationSaveDto section.</summary>
        public void ExportPurchasingNeeds(List<HouseholdPurchasingNeed> outNeeds)
        {
            PurchasingNeeds.ExportState(outNeeds);
        }

        /// <summary>Phase B: imports purchasing needs from the PopulationSaveDto section.</summary>
        public void ImportPurchasingNeeds(IEnumerable<HouseholdPurchasingNeed> inNeeds)
        {
            PurchasingNeeds.ImportState(inNeeds);
        }
    }
}
