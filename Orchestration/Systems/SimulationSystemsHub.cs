using System;
using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Economy;
using LandLedgers.Economy.Butcher;
using LandLedgers.Economy.Creation;
using LandLedgers.Economy.Farming;
using LandLedgers.Economy.Farming.Integration;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.Liabilities;
using LandLedgers.Economy.Farming.Risk;
using LandLedgers.Economy.Postal;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Primitives;
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
using LandLedgers.Economy.Farming.Integration;
using LandLedgers.Economy.Farming.Livestock;
using LandLedgers.Economy.Farming.Timber;
using LandLedgers.FirstLedger;
using LandLedgers.ReadModels.Valuation;
using LandLedgers.World.Journeys;
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

        [SerializeField]
        private TaskAuthority taskAuthority = new TaskAuthority();

        [SerializeField]
        private SkillService skillService = new SkillService();

        [SerializeField]
        private WorkTimeBudgetStore workTimeBudgets = new WorkTimeBudgetStore();

        [SerializeField]
        private EnterpriseValuationReadModel valuation = new EnterpriseValuationReadModel();

        [SerializeField]
        private FreightResourcePool freightPool = new FreightResourcePool();

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

        public EntityIdRegistry Ids => idRegistry ??= new EntityIdRegistry();
        public AnimalRegistry Animals => animalRegistry ??= new AnimalRegistry(Ids);
        public HouseholdLedgerRegistry HouseholdLedgers => householdLedgers ??= new HouseholdLedgerRegistry();
        public TaskAuthority Tasks => taskAuthority ??= new TaskAuthority();
        public SkillService Skills => skillService ??= new SkillService();
        public WorkTimeBudgetStore WorkTimeBudgets => workTimeBudgets ??= new WorkTimeBudgetStore();
        public EnterpriseValuationReadModel Valuation => valuation ??= new EnterpriseValuationReadModel();
        public FreightResourcePool FreightPool => freightPool ??= new FreightResourcePool();
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
                return liabilityLedger;
            }
        }

        private PostalService postalService;

        /// <summary>NX-2A: the postal network authority (offices, mail, contracts).</summary>
        public PostalService Postal => postalService ??= new PostalService();

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
            butcherRuntimes ??= new List<ButcherRuntime>();
            farmFlows ??= new FarmFlowLedger();
            capabilityRegistry ??= new BusinessCapabilityRegistry();
            operatingLedger ??= new BusinessOperatingLedger();
            employments ??= new EmploymentRelationshipRegistry();
            farmSlice ??= new FarmSliceSystems();
            liabilityLedger ??= new BusinessLiabilityLedger();
            liabilityLedger.AttachValuation(valuation);
            postalService ??= new PostalService();
            riskService ??= new AgriculturalRiskService();
            diseaseService ??= new LivestockDiseaseService();
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
            MineLaborTaskCatalog.Register(tasks, diagnostics);
            MineShaftTaskCatalog.Register(tasks, diagnostics);
            GrainMillStoneState.RegisterTaskDefinitions(tasks, diagnostics);
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
                farmFlows = FarmFlows.CaptureSaveDto(),
                capabilities = Capabilities.CaptureSaveDto(),
                operatingLedger = OperatingLedger.CaptureSaveDto(),
                employments = Employments.CaptureSaveDto(),
                farmSlice = FarmSlice.CaptureSaveDto(),
                liabilities = Liabilities.CaptureSaveDto(),
                postal = Postal.CaptureSaveDto(),
                risk = Risk.CaptureSaveDto(),
                disease = Disease.CaptureSaveDto(),
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
            FreightPool.LoadFromSaveDto(dto.freightPool);
            FarmFlows.LoadFromSaveDto(dto.farmFlows);
            Capabilities.LoadFromSaveDto(dto.capabilities, diagnostics);
            OperatingLedger.LoadFromSaveDto(dto.operatingLedger);
            Employments.LoadFromSaveDto(dto.employments);
            FarmSlice.LoadFromSaveDto(dto.farmSlice);
            Liabilities.LoadFromSaveDto(dto.liabilities);
            Postal.LoadFromSaveDto(dto.postal);
            Risk.LoadFromSaveDto(dto.risk);
            Disease.LoadFromSaveDto(dto.disease);

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
    }
}
