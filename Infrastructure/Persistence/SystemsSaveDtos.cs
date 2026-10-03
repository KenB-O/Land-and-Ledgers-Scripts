using System;
using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Economy;
using LandLedgers.Economy.Butcher;
using LandLedgers.Economy.Creation;
using LandLedgers.Economy.Farming;
using LandLedgers.Economy.Freight;
using LandLedgers.Economy.GeneralStore;
using LandLedgers.Population;
using LandLedgers.ReadModels.Valuation;
using LandLedgers.Skills;
using LandLedgers.Tasks;
using LandLedgers.Time;

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
        public List<ButcherRuntimeSaveDto> butcherRuntimes = new();
        public FarmFlowSaveDto farmFlows = new();
        public CapabilityRegistrySaveDto capabilities = new();
        public OperatingLedgerSaveDto operatingLedger = new();
        public EmploymentRegistrySaveDto employments = new();
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
        public List<SkillService.PersonSkillEntry> skillStates = new();
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
}
