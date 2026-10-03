using System;
using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Economy;
using LandLedgers.Economy.Butcher;
using LandLedgers.Economy.Creation;
using LandLedgers.Economy.Farming;
using LandLedgers.Economy.Farming.Integration;
using LandLedgers.Economy.Freight;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.ReadModels.Valuation;
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

        public IReadOnlyList<ButcherRuntime> ButcherRuntimes => butcherRuntimes;

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
