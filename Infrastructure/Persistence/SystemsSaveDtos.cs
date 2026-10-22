using System;
using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.BoardingHouse;
using LandLedgers.Economy.Butcher;
using LandLedgers.Economy.Creation;
using LandLedgers.Economy.Farming;
using LandLedgers.Economy.Farming.Crops;
using LandLedgers.Economy.Farming.Dairy;
using LandLedgers.Economy.Farming.Delivery;
using LandLedgers.Economy.Farming.Integration;
using LandLedgers.Economy.Farming.Livestock;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.Transport;
using LandLedgers.Economy.Liabilities;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.GeneralStore;
using LandLedgers.Economy.Farming.Risk;
using LandLedgers.Economy.Postal;
using LandLedgers.Economy.Recruitment;
using LandLedgers.Economy.Labor;
using LandLedgers.Economy.Delegation;
using LandLedgers.Economy.Equipment;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.ReadModels.Valuation;
using LandLedgers.Skills;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using LandLedgers.World.Property;

namespace LandLedgers.Persistence
{
    /// <summary>
    /// CLN-1: save DTOs for the simulation systems hub (HF/TTS/DEV/BIZ/FRM state that
    /// previously had no save-pipeline wiring). Each section mirrors a standalone
    /// authority owned by <c>SimulationSystemsHub</c>.
    /// </summary>
    [Serializable]
    public sealed class SystemsSaveDto
    {
        public TaskAuthoritySaveDto tasks = new();
        public SkillServiceSaveDto skills = new();
        public WorkTimeBudgetSaveDto workTimeBudgets = new();
        public ValuationReadModelSaveDto valuation = new();
        public FreightResourcePoolSaveDto freightPool = new();
        public TransportAssetRegistrySaveDto transportAssets = new();
        public List<ButcherRuntimeSaveDto> butcherRuntimes = new();
        public FarmFlowSaveDto farmFlows = new();
        public CapabilityRegistrySaveDto capabilities = new();
        public OperatingLedgerSaveDto operatingLedger = new();
        public EmploymentRegistrySaveDto employments = new();
        /// <summary>Pass IX (A2-05, additive): production weekly attendance totals feeding payroll.</summary>
        public WeeklyAttendanceSaveDto weeklyAttendance = new();
        public FarmSliceSaveDto farmSlice = new();
        public LiabilityLedgerSaveDto liabilities = new();
        /// <summary>Shared credit authority: one current balance per obligation.</summary>
        public FinancialObligationSaveDto financialObligations = new();
        public CreditOfferWorkflowSaveDto creditOffers = new();
        /// <summary>Instrument/document index over the shared obligations.</summary>
        public CreditRegistry.CreditRegistrySaveDto creditInstruments = new();
        /// <summary>NX-2A: postal network (offices, mail in custody, contracts).</summary>
        public PostalServiceSaveDto postal = new();
        /// <summary>NX-2B: agricultural disasters (fireguards, drought episodes).</summary>
        public AgriculturalRiskSaveDto risk = new();
        /// <summary>NX-2B: livestock disease outbreaks.</summary>
        public LivestockDiseaseSaveDto disease = new();
        /// <summary>P6: journey model — locations and the road network (JRN-1).</summary>
        public JourneyModelSaveDto journeys = new();
        /// <summary>P6: weather/route conditions over the journey model (NX-2C).</summary>
        public RouteConditionSaveDto routeConditions = new();
        /// <summary>P6: postal money-order books (D4E).</summary>
        public MoneyOrderServiceSaveDto moneyOrders = new();
        /// <summary>P6: registered-mail custody chain (D4F).</summary>
        public RegisteredMailSaveDto registeredMail = new();
        /// <summary>P6: recruitment efforts, inquiries, in-flight letters (T2B).</summary>
        public RecruitmentServiceSaveDto recruiting = new();
        /// <summary>P6: weather seed for the route-condition service (bootstrap-assigned from the world seed).</summary>
        public int weatherSeed;

        // Real People / shared-credit Phase I services. These are kept under
        // the same hub boundary as the older authorities so one save cannot
        // silently omit an active household, housing, construction, NPC
        // enterprise, trade-credit, or credit-event state.
        public PersonScheduleTrackerSaveDto personSchedule = new();
        public RentCollectionService.RentCollectionServiceSaveDto rentCollection = new();
        public List<BoardingHouseShopRuntime.BoardingHouseShopRuntimeSaveDto> boardingHouseRuntimes = new();
        public NpcFormationAssetRegisterSaveDto npcFormationAssets = new();
        public List<NpcBusinessLifeRecordSaveDto> npcBusinessLifecycle = new();
        public NpcBusinessInvestigationSaveDto npcBusinessInvestigation = new();
        public NpcOpportunityObservationLogSaveDto npcOpportunityObservation = new();
        // Pass V P2-3: autonomous entrepreneurship driver state (per-person
        // observation logs and investigations).
        public NpcEntrepreneurshipDriver.NpcEntrepreneurshipDriverSaveDto npcEntrepreneurship = new();
        public NpcBusinessEventLogSaveDto npcBusinessEvents = new();
        public NpcPropertyLifeService.NpcPropertyLifeSaveDto npcPropertyLife = new();
        public ConstructionProjectExecutor.ConstructionProjectSaveDto constructionProjects = new();
        public NpcConstructionCommissioner.NpcConstructionCommissionerSaveDto npcConstructionCommissioner = new();
        public TradeCreditBook.TradeCreditBookSaveDto tradeCredit = new();
        public CreditEventLog.CreditEventLogSaveDto creditEvents = new();
        // Autonomous Labor pass services: contact graph, negotiated offers,
        // wage-settlement markers, manager instructions + activity, delegation grants.
        public ContactGraphServiceSaveDto contactGraph = new();
        // Pass VII Workstream E: transmitted information packets with provenance.
        public KnowledgeTransmissionSaveDto knowledgeTransmissions = new();        public EmploymentNegotiationSaveDto employmentNegotiation = new();
        public WageSettlementSaveDto wageSettlement = new();
        public ManagerBrainSaveDto managerBrain = new();
        public DelegationService.DelegationSaveDto delegation = new();
        // Pass VII Workstream D: manager decision execution ledger (idempotency
        // keys, owner-approval pending requests, in-transit shipments).
        public ManagerDecisionExecutionSaveDto managerDecisionExecution = new();
        // Pass III Worker A: legal parcel/holding registry and the single
        // non-title rights store. Title chain itself persists via
        // AcquisitionSaveDto.titleChain.
        public ParcelServiceSaveDto parcels = new();
        public PropertyInterestStoreSaveDto propertyInterests = new();
        // Pass III Worker B: tenancy payment loop — rent/board obligation
        // states plus settled-period idempotency markers.
        public HousingAuthority.HousingSaveDto housing = new();
        public TenancyPaymentService.TenancyPaymentSaveDto tenancyPayments = new();
        // Pass VIII Worker 2: lodging registrations — which business rooms
        // are real accommodation.
        public LodgingRegistry.LodgingRegistrySaveDto lodgingRegistry = new();
        // Pass III Worker C: household decisions, decision-execution state,
        // domestic labor assignments, and autonomy job-loss fingerprints.
        public HouseholdDecisionServiceSaveDto householdDecisions = new();
        public HouseholdDecisionDriverSaveDto householdDecisionDriver = new();
        public DomesticLaborServiceSaveDto domesticLabor = new();
        public DomesticLaborDriverSaveDto domesticLaborDriver = new();
        public HouseholdAutonomyDriverSaveDto householdAutonomy = new();
        // Pass V P2-1: batched errand plans with claims and reservations.
        public HouseholdErrandPlanRegistrySaveDto householdErrandPlans = new();
        // Pass V P2-2: durable registry of private household lenders.
        public HouseholdLenderDirectory.HouseholdLenderDirectorySaveDto householdLenders = new();
        // Pass VIII (6.3): the production business lender's servicing books
        // (charter, cash position, loan schedule state, borrower memory).
        // The obligations themselves persist via financialObligations above —
        // this section never re-creates debt.
        public BusinessLenderService.BusinessLenderServiceSaveDto businessLender =
            new BusinessLenderService.BusinessLenderServiceSaveDto();
        /// <summary>
        /// Pass IV (Worker 1): deterministic due-event schedule (task completions,
        /// passive processes, deadlines). Owned by SimulationSystemsHub.DueEvents.
        /// </summary>
        public DueEventSchedulerSaveDto dueEvents = new();
        // Pass IV Worker 2: the unified reservation authority (all live
        // reservations across every domain key space), draft-power hold index,
        // and equipment capacity registrations. Additive sections.
        public EquipmentReservationAuthoritySaveDto reservations = new();
        public DraftPowerServiceSaveDto draftPower = new();
        public EquipmentCapacityServiceSaveDto equipmentCapacities = new();
    }

    /// <summary>FVS-4: dairy chain save section.</summary>
    [Serializable]
    public sealed class DairyChainSaveDto
    {
        public List<DairyCowState> cows = new();
    }

    /// <summary>FVS-4: farm vertical-slice save section (dairy, feed, poultry, deliveries, construction).</summary>
    [Serializable]
    public sealed class FarmSliceSaveDto
    {
        public List<DairyCowState> dairyCows = new();
        public int feedStockUnits;
        public string feedSource = string.Empty;
        public List<EntityId> hens = new();
        public List<DeliveryJob> deliveryJobs = new();
        public List<FarmConstructionProject> constructionProjects = new();
        public CropChainSaveDto crop = new();
        public PigSheepChainSaveDto swine = new();
        public CheeseChainSaveDto cheese = new();
    }

    [Serializable]
    public sealed class TaskAuthoritySaveDto
    {
        public List<TaskDefinition> definitions = new();
        public List<WorkTask> tasks = new();
    }

    [Serializable]
    public sealed class SkillServiceSaveDto
    {
        public List<SkillDefinition> definitions = new();
        /// <summary>
        /// Legacy pre-Pass III entries. Read on load and deterministically
        /// migrated into capabilities; never written by new code.
        /// </summary>
        public List<SkillService.PersonSkillEntry> legacySkillStates = new();
        // Pass III Worker D: the four capability authorities + observer evidence.
        public List<AptitudeRecord> aptitudes = new();
        public List<ExperienceEntry> experienceEntries = new();
        public int experienceSequence;
        public List<DomainFamiliarity> familiarities = new();
        public List<PersonCapabilityState> capabilities = new();
        public List<CapabilityEvidenceItem> evidenceItems = new();
    }

    [Serializable]
    public sealed class WorkTimeBudgetSaveDto
    {
        public List<WorkTimeBudget> budgets = new();
    }

    [Serializable]
    public sealed class ValuationReadModelSaveDto
    {
        public List<EnterpriseValuationReadModel.BusinessExport> entries = new();
    }

    [Serializable]
    public sealed class FreightResourcePoolSaveDto
    {
        public List<FreightWagon> wagons = new();
        public List<string> reservedWagonIds = new();
    }

    [Serializable]
    public sealed class TransportAssetRegistrySaveDto
    {
        public List<LandLedgers.Economy.Blacksmith.EquipmentAsset> wagons = new();
        public LandLedgers.Animals.HorseHousingSaveDto horseHousing = new();
    }

    [Serializable]
    public sealed class ButcherRuntimeSaveDto
    {
        public string businessInstanceId = string.Empty;
        public string productionSiteId = string.Empty;
        public string retailSiteId = string.Empty;
        public List<ButcherLot> lots = new();
    }

    [Serializable]
    public sealed class FarmFlowSaveDto
    {
        public List<FarmProduceLot> produceLots = new();
        public List<FarmProduceSale> produceSales = new();
        public List<FarmLivestockSale> livestockSales = new();
        public List<WholesaleCutPurchase> wholesalePurchases = new();
    }

    [Serializable]
    public sealed class CapabilityRegistrySaveDto
    {
        public List<BusinessCapability> capabilities = new();
    }

    [Serializable]
    public sealed class OperatingLedgerSaveDto
    {
        public List<BusinessOperatingLedger.CommerceExport> records = new();
    }

    [Serializable]
    public sealed class EmploymentRegistrySaveDto
    {
        public List<EmploymentRelationship> relationships = new();
    }

    /// <summary>
    /// Pass IV Worker 2: persisted state of the unified reservation authority —
    /// every live reservation across all domain key spaces, plus per-key
    /// capacity limits for partial-capacity resources.
    /// </summary>
    [Serializable]
    public sealed class EquipmentReservationAuthoritySaveDto
    {
        public List<ReservationHandle> handles = new();
        public List<ResourceCapacityEntry> capacities = new();
    }

    /// <summary>Pass IV Worker 2: one per-key capacity limit entry.</summary>
    [Serializable]
    public sealed class ResourceCapacityEntry
    {
        public string resourceKey = string.Empty;
        public float capacity;
    }

    /// <summary>
    /// Pass IV Worker 2: persisted draft-power hold index. The holds themselves
    /// live in <see cref="EquipmentReservationAuthoritySaveDto"/>; this index
    /// lets animal/driver release paths find them after a load.
    /// </summary>
    [Serializable]
    public sealed class DraftPowerServiceSaveDto
    {
        public List<DraftPowerHoldEntry> holds = new();
    }

    /// <summary>Pass IV Worker 2: one draft hold index entry.</summary>
    [Serializable]
    public sealed class DraftPowerHoldEntry
    {
        public string resourceKey = string.Empty;
        public string reservationId = string.Empty;
        public string ownerTaskId = string.Empty;
        public string ownerLabel = string.Empty;
    }

    /// <summary>
    /// Pass IV Worker 2: persisted equipment capacity registrations
    /// (descriptor + condition profile per equipment key).
    /// </summary>
    [Serializable]
    public sealed class EquipmentCapacityServiceSaveDto
    {
        public List<CapacityRegistrationEntry> registrations = new();
    }

    /// <summary>Pass IV Worker 2: one capacity registration entry.</summary>
    [Serializable]
    public sealed class CapacityRegistrationEntry
    {
        public string equipmentKey = string.Empty;
        public EquipmentCapacityDescriptor descriptor;
        public EquipmentConditionProfile profile;
    }
}
