using System;
using System.Collections.Generic;
using System.Text;
using LandLedgers.MVP;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Reputation;
using LandLedgers.Time;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Economy
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(265)]
    public sealed partial class SharedBusinessRuntimeManager : MonoBehaviour
    {
        private const string DefaultProfilesResourcePath = "Core/Economy/BusinessProfiles";
        private const int PassiveBusinessSeedSalt = 73471;
        private const int PreferredCorePassiveBusinessCount = 10;
        private const int MaxPassiveStartupBusinessCount = 10;
        private const string CategoryMedicalService = "medical_service";
        private const string CategoryMedicineRemedies = "medicine_remedies";
        private const string CategoryLivestock = "livestock_inputs";
        private const string CategoryMeat = "meat";
        private const string CategoryCropFood = "crop_food";
        private const string CategoryStapleFood = "staple_food";
        private const string CategoryFeed = "feed_inputs";
        private const string CategoryHardware = "tools_hardware";
        private const string CategoryStandingTimber = "standing_timber";
        private const string CategorySawmillLogs = "sawmill_logs";
        private const string CategoryLumber = "lumber";
        private const string CategorySlabsOffcuts = "slabs_offcuts";
        private const string CategoryBoardingLodging = "boarding_lodging";
        private const string CategoryLiveryFeedUpkeep = "livery_feed_upkeep";
        private const string CategoryFreightService = "freight_service";
        private const string CategoryBuilderLabor = "builder_labor";
        private const string CategoryFuelWood = "fuel_wood";
        private const string CategoryGrain = "grain";
        private const string CategoryFlour = "flour";
        private const string CategoryBread = "bread";
        private const string CategoryMealInputs = "meal_inputs";
        private const string CategoryBarberConsumables = "barber_consumables";
        private const string CategoryClothing = "clothing";
        private const string CategoryMendingService = "mending_service";
        private const string CategoryMealService = "meal_service";
        private const string CategoryBarberService = "barber_bath_service";
        private const string CategoryWheelwrightRepairs = "wheelwright_repairs";
        private const string CategoryCoal = "coal";
        private const string CategoryIronOre = "iron_ore";
        private const string CategoryGoldOre = "gold_ore";
        private const string CategorySilverOre = "silver_ore";
        private const string CategoryMineSupportSupplies = "mine_support_supplies";
        private const string RegionalHardwareFallbackSourceLabel = "Regional freight hardware";
        private const float RegionalHardwareFallbackUnitCostMultiplier = 1.75f;
        private const int DefaultHardwareFallbackUnitCostCents = 438;
        private const int SharedOperatingCashFloorCents = 4500;
        private const int BoardingHouseWeeklyBoardCents = 275;
        private const int BoardingHouseFixedUpkeepCents = 275;
        private const int BoardingHouseVariableUpkeepCents = 45;
        private const int CropFarmOutputUnitsPerInput = 8;
        private const int RanchOutputUnitsPerInput = 2;
        private const int ButcherOutputUnitsPerInput = 8;
        private const int BlacksmithOutputUnitsPerInput = 4;
        private const int FuelDealerOutputUnitsPerInput = 5;
        private const int GrainMillOutputUnitsPerInput = 4;
        private const int BakeryOutputUnitsPerInput = 3;
        private const int CropFarmLocalOutletMaxUnits = 110;
        private const int RanchLocalOutletMaxUnits = 4;
        private const int ButcherLocalOutletMaxUnits = 68;
        private const int BlacksmithLocalOutletMaxUnits = 34;
        private const int FuelDealerLocalOutletMaxUnits = 58;
        private const int GrainMillLocalOutletMaxUnits = 48;
        private const int BakeryLocalOutletMaxUnits = 62;
        private const int WheelwrightLocalRepairDemandMaxUnits = 24;
        private const int TailorLocalOutletMaxUnits = 28;
        private const int MineOffMapSaleMaxUnits = 8;
        private const int SawmillOffcutLocalOutletMaxUnits = 12;
        private const int SawmillSlabsOffcutsPerLog = 1;
        private const int SawmillToLumberYardWeeklyMaxUnits = 80;
        private const int SawmillOffMapLogBuyMaxUnits = 6;
        private const int SawmillOffMapLumberSellMaxUnits = 24;
        private const int LumberYardOffMapBuyMaxUnits = 40;
        private const int LumberYardOffMapSellMaxUnits = 24;
        private const int LumberYardLocalBuilderDemandMaxUnits = 48;
        private const float HouseholdAffinityChoiceInfluence01 = 0.24f;
        private const float LocalMarketPriceCaptureSensitivity01 = 0.85f;
        private const float LocalMarketMinimumCapture01 = 0.38f;
        private const float RequiredStaffBaselineEfficiency01 = 0.8f;
        private const float OwnerOperatedFallbackEfficiency01 = 0.45f;
        private const string SupersededLegacyRecurringLocalOrderCancellationReason = "superseded legacy recurring local order";
        private static readonly IReadOnlyList<LocalRecurringOrderTemplate> DefaultLocalRecurringOrders = LocalRecurringOrderTemplate.CreateDefaultTemplates();

        [Header("Sources")]
        [SerializeField]
        private TownWorldController townWorld;

        [SerializeField]
        private PopulationManager populationManager;

        [SerializeField]
        private TimeManager timeManager;

        [SerializeField]
        private GeneralStoreRuntimeManager generalStoreRuntime;

        [SerializeField]
        private PlayerPortfolioManager playerPortfolio;

        [SerializeField]
        private LogisticsRuntimeManager logisticsRuntime;

        [SerializeField]
        private BusinessProfileDefinition[] businessProfiles = Array.Empty<BusinessProfileDefinition>();

        [Header("Staffing")]
        [SerializeField]
        private LaborAccessLevel minimumHireLaborAccess = LaborAccessLevel.YoungWorker;

        [Header("Runtime")]
        [SerializeField]
        private List<BusinessInstanceState> businesses = new();

        [SerializeField]
        private string status = "Not ready.";

        [SerializeField, TextArea(1, 4)]
        private string lastWeeklyRecurringLocalOrderSummary = "No recurring local orders resolved yet.";

        [SerializeField]
        private List<BusinessTransferAgreementState> transferAgreements = new();

        [SerializeField, TextArea(1, 4)]
        private string lastWeeklyTransferAgreementSummary = "No owned transfer agreements resolved yet.";

        private readonly HashSet<int> assignedBuildingIds = new();
        private readonly Dictionary<string, string> lastOperationSummaries = new();
        private readonly LocalRecurringOrderManager localRecurringOrderManager = new();
        private readonly BusinessReputationSellerChoice businessReputationSellerChoice = new();
        private readonly BusinessReputationEvaluator businessReputationEvaluator = new();
        private readonly HouseholdAffinityEvaluator householdAffinityEvaluator = new();
        private readonly Dictionary<string, HouseholdBusinessAffinityState> householdAffinitiesByKey = new();
        private ConstructionSupportNodeState linkedSawmillSupportNode;
        private bool subscribedToTime;

        public IReadOnlyList<BusinessInstanceState> Businesses => businesses;
        public string Status => status;
        public IReadOnlyList<LocalRecurringOrderRelationshipState> LocalOrderRelationships => localRecurringOrderManager.Relationships;
        public IReadOnlyList<BusinessTransferAgreementState> TransferAgreements => transferAgreements;
        public string LastWeeklyRecurringLocalOrderSummary => lastWeeklyRecurringLocalOrderSummary ?? string.Empty;
        public string LastWeeklyTransferAgreementSummary => lastWeeklyTransferAgreementSummary ?? string.Empty;

        public List<BusinessInstanceSaveDto> CaptureSaveDtos(string deepStoreInstanceId = null)
        {
            List<BusinessInstanceSaveDto> result = new();
            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState business = businesses[i];
                if (business == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(deepStoreInstanceId)
                    && string.Equals(business.InstanceId, deepStoreInstanceId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                result.Add(business.CaptureSaveDto());
            }

            return result;
        }

        public List<LocalRecurringOrderRelationshipSaveDto> CaptureLocalRecurringOrderRelationshipSaveDtos()
        {
            return localRecurringOrderManager.CaptureSaveDtos();
        }

        public List<BusinessTransferAgreementSaveDto> CaptureTransferAgreementSaveDtos()
        {
            List<BusinessTransferAgreementSaveDto> result = new();
            for (int i = 0; i < transferAgreements.Count; i++)
            {
                BusinessTransferAgreementState agreement = transferAgreements[i];
                if (agreement == null || string.IsNullOrWhiteSpace(agreement.AgreementId))
                {
                    continue;
                }

                result.Add(agreement.CaptureSaveDto());
            }

            return result;
        }

        public void LoadFromSaveDtos(
            IReadOnlyList<BusinessInstanceSaveDto> dtos,
            string deepStoreInstanceId = null,
            IReadOnlyList<LocalRecurringOrderRelationshipSaveDto> localRecurringOrderRelationships = null,
            string savedLocalRecurringOrderSummary = null,
            IReadOnlyList<BusinessTransferAgreementSaveDto> savedTransferAgreements = null,
            string savedTransferAgreementSummary = null)
        {
            AutoWire();
            EnsureProfilesLoaded();
            businesses.Clear();
            assignedBuildingIds.Clear();
            lastOperationSummaries.Clear();
            householdAffinitiesByKey.Clear();
            localRecurringOrderManager.Clear();
            transferAgreements.Clear();
            lastWeeklyRecurringLocalOrderSummary = "No recurring local orders resolved yet.";
            lastWeeklyTransferAgreementSummary = "No owned transfer agreements resolved yet.";

            if (dtos != null)
            {
                for (int i = 0; i < dtos.Count; i++)
                {
                    BusinessInstanceSaveDto dto = dtos[i];
                    if (dto == null)
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(deepStoreInstanceId)
                        && string.Equals(dto.instanceId, deepStoreInstanceId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    BusinessInstanceState business = BusinessInstanceState.FromSaveDto(dto);
                    if (business == null)
                    {
                        continue;
                    }

                    businesses.Add(business);
                    if (business.AssignedBuildingId >= 0)
                    {
                        assignedBuildingIds.Add(business.AssignedBuildingId);
                    }
                }
            }

            int startupRepairs = NormalizeSharedBusinessStartupState("restore");
            int created = EnsureMissingCorePassiveBusinesses();
            if (created > 0)
            {
                startupRepairs += NormalizeSharedBusinessStartupState("restore core-fill");
            }

            localRecurringOrderManager.LoadFromSaveDtos(localRecurringOrderRelationships, savedLocalRecurringOrderSummary);
            if (savedTransferAgreements != null)
            {
                for (int i = 0; i < savedTransferAgreements.Count; i++)
                {
                    BusinessTransferAgreementState agreement = BusinessTransferAgreementState.FromSaveDto(savedTransferAgreements[i]);
                    if (agreement == null || string.IsNullOrWhiteSpace(agreement.AgreementId))
                    {
                        continue;
                    }

                    transferAgreements.Add(agreement);
                }
            }

            int relationshipRepairs = NormalizeRecurringRelationshipActives(GetCurrentWeekKey());
            lastWeeklyRecurringLocalOrderSummary = localRecurringOrderManager.LastWeeklySummary;
            lastWeeklyTransferAgreementSummary = string.IsNullOrWhiteSpace(savedTransferAgreementSummary)
                ? "No owned transfer agreements resolved yet."
                : savedTransferAgreementSummary;
            status = $"Shared businesses restored. Total={businesses.Count}, CoreCreated={created}, StartupRepairs={startupRepairs}, RelationshipRepairs={relationshipRepairs}.";
        }

        public void Configure(
            TownWorldController newTownWorld,
            IReadOnlyList<BusinessProfileDefinition> profiles,
            PopulationManager newPopulationManager = null,
            TimeManager newTimeManager = null,
            GeneralStoreRuntimeManager newGeneralStoreRuntime = null,
            PlayerPortfolioManager newPlayerPortfolio = null)
        {
            townWorld = newTownWorld;
            populationManager = newPopulationManager;
            timeManager = newTimeManager;
            generalStoreRuntime = newGeneralStoreRuntime;
            playerPortfolio = newPlayerPortfolio != null ? newPlayerPortfolio : playerPortfolio;
            if (profiles == null)
            {
                businessProfiles = Array.Empty<BusinessProfileDefinition>();
            }
            else
            {
                businessProfiles = new BusinessProfileDefinition[profiles.Count];
                for (int i = 0; i < profiles.Count; i++)
                {
                    businessProfiles[i] = profiles[i];
                }
            }

            SortProfilesForDeterministicSeeding();
            SubscribeToTime();
        }

        public void InitializeIfNeeded(BusinessInstanceState existingPlayerGeneralStore = null)
        {
            AutoWire();
            if (businesses.Count > 0)
            {
                EnsureProfilesLoaded();
                int existingStartupRepairs = NormalizeSharedBusinessStartupState("existing");
                int createdExisting = EnsureMissingCorePassiveBusinesses();
                if (createdExisting > 0)
                {
                    existingStartupRepairs += NormalizeSharedBusinessStartupState("existing core-fill");
                }

                int existingRelationshipRepairs = NormalizeRecurringRelationshipActives(GetCurrentWeekKey());
                status = $"Shared businesses ready. Total={businesses.Count}, CoreCreated={createdExisting}, StartupRepairs={existingStartupRepairs}, RelationshipRepairs={existingRelationshipRepairs}.";
                return;
            }

            EnsureProfilesLoaded();
            assignedBuildingIds.Clear();
            businesses.Clear();
            localRecurringOrderManager.Clear();
            transferAgreements.Clear();
            lastWeeklyRecurringLocalOrderSummary = "No recurring local orders resolved yet.";
            lastWeeklyTransferAgreementSummary = "No owned transfer agreements resolved yet.";

            if (townWorld == null)
            {
                status = "Missing TownWorldController.";
                return;
            }

            if (townWorld.Grid == null)
            {
                townWorld.GenerateTownShell();
            }

            if (existingPlayerGeneralStore != null)
            {
                businesses.Add(existingPlayerGeneralStore);
                if (existingPlayerGeneralStore.AssignedBuildingId >= 0)
                {
                    assignedBuildingIds.Add(existingPlayerGeneralStore.AssignedBuildingId);
                }
            }

            int target = GetPassiveStartupBusinessTarget();
            int created = SeedPassiveStartStateBusinesses(target);
            int startupRepairs = NormalizeSharedBusinessStartupState("fresh seed");
            int coreCreated = EnsureMissingCorePassiveBusinesses();
            created += coreCreated;
            if (coreCreated > 0)
            {
                startupRepairs += NormalizeSharedBusinessStartupState("fresh core-fill");
            }

            int relationshipRepairs = NormalizeRecurringRelationshipActives(GetCurrentWeekKey());
            status = $"Shared businesses initialized. Total={businesses.Count}, LaunchCreated={created}/{target}, StartupRepairs={startupRepairs}, RelationshipRepairs={relationshipRepairs}.";
        }

        public void ResolveBaselineOperations()
        {
            InitializeIfNeeded();
            for (int i = 0; i < businesses.Count; i++)
            {
                businesses[i]?.ResolveWeeklyBaselineThroughput();
                businesses[i]?.ResolveDailyBaselineService();
            }
        }

        public void ResolveWeeklySharedOperations()
        {
            InitializeIfNeeded(generalStoreRuntime != null ? generalStoreRuntime.CurrentBusiness : null);
            EnsureProfilesLoaded();
            StringBuilder builder = new();
            builder.Append("Weekly shared operations: ");
            int operationCount = 0;
            int transferCount = 0;
            int cashTransferCount = 0;

            ResetWeeklySettlementForType(BusinessType.CropFarm);
            ResetWeeklySettlementForType(BusinessType.Ranch);
            ResetWeeklySettlementForType(BusinessType.Butcher);
            ResetWeeklySettlementForType(BusinessType.Blacksmith);
            ResetWeeklySettlementForType(BusinessType.Doctor);
            ResetWeeklySettlementForType(BusinessType.Sawmill);
            ResetWeeklySettlementForType(BusinessType.LumberYard);
            ResetWeeklySettlementForType(BusinessType.BoardingHouse);
            ResetWeeklySettlementForType(BusinessType.LiveryFreight);
            ResetWeeklySettlementForType(BusinessType.Builder);
            ResetWeeklySettlementForType(BusinessType.FuelDealer);
            ResetWeeklySettlementForType(BusinessType.GrainMill);
            ResetWeeklySettlementForType(BusinessType.Bakery);
            ResetWeeklySettlementForType(BusinessType.Tailor);
            ResetWeeklySettlementForType(BusinessType.Saloon);
            ResetWeeklySettlementForType(BusinessType.Barber);
            ResetWeeklySettlementForType(BusinessType.Wheelwright);
            ResetWeeklySettlementForType(BusinessType.Mine);

            cashTransferCount += ResolveAutomaticBusinessCashTransfers(BusinessCashAutoTransferMode.LowerOnly, builder);

            ResolvePayrollForType(BusinessType.CropFarm);
            ResolvePayrollForType(BusinessType.Ranch);
            ResolvePayrollForType(BusinessType.Butcher);
            ResolvePayrollForType(BusinessType.Blacksmith);
            ResolvePayrollForType(BusinessType.Doctor);
            ResolvePayrollForType(BusinessType.Sawmill);
            ResolvePayrollForType(BusinessType.LumberYard);
            ResolvePayrollForType(BusinessType.BoardingHouse);
            ResolvePayrollForType(BusinessType.LiveryFreight);
            ResolvePayrollForType(BusinessType.Builder);
            ResolvePayrollForType(BusinessType.FuelDealer);
            ResolvePayrollForType(BusinessType.GrainMill);
            ResolvePayrollForType(BusinessType.Bakery);
            ResolvePayrollForType(BusinessType.Tailor);
            ResolvePayrollForType(BusinessType.Saloon);
            ResolvePayrollForType(BusinessType.Barber);
            ResolvePayrollForType(BusinessType.Wheelwright);
            ResolvePayrollForType(BusinessType.Mine);

            operationCount += ResolveArchetypeRoutedWeeklyOperations(builder);

            transferCount += ResolveRecurringLocalOrders(builder);
            transferCount += ResolveOwnedBusinessTransferAgreements(builder);
            transferCount += ResolveLumberIndustryOffMapFallback(builder);
            transferCount += ResolveLumberYardLocalBuilderDemand(builder);

            transferCount += ResolveArchetypeRoutedLocalMarketSales(builder);

            cashTransferCount += ResolveAutomaticBusinessCashTransfers(BusinessCashAutoTransferMode.UpperOnly, builder);

            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState business = businesses[i];
                if (business == null
                    || business.BusinessType == BusinessType.GeneralStore
                    || business.BusinessType == BusinessType.CropFarm
                    || business.BusinessType == BusinessType.Ranch
                    || business.BusinessType == BusinessType.Butcher
                    || business.BusinessType == BusinessType.Blacksmith
                    || business.BusinessType == BusinessType.Doctor
                    || business.BusinessType == BusinessType.Sawmill
                    || business.BusinessType == BusinessType.LumberYard
                    || business.BusinessType == BusinessType.BoardingHouse
                    || business.BusinessType == BusinessType.Mine
                    || IsProfessionWaveBusiness(business.BusinessType))
                {
                    continue;
                }

                business.ResolveWeeklyBaselineThroughput();
                business.ResolveDailyBaselineService();
            }

            if (operationCount == 0 && transferCount == 0 && cashTransferCount == 0)
            {
                builder.Append("no active launch businesses resolved.");
            }

            ResolveOwnerDistributionsForSharedBusinesses(builder);
            status = builder.ToString();
        }

        private int ResolveAutomaticBusinessCashTransfers(BusinessCashAutoTransferMode mode, StringBuilder builder)
        {
            if (playerPortfolio == null)
            {
                return 0;
            }

            int count = 0;
            int weekKey = GetCurrentWeekKey();
            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState business = businesses[i];
                if (business == null
                    || business.RuntimeState == null
                    || business.BusinessType == BusinessType.GeneralStore
                    || business.Owner == null
                    || business.Owner.OwnerKind != BusinessOwnerKind.Player)
                {
                    continue;
                }

                if (playerPortfolio.TryResolveAutomaticBusinessCashTransfer(
                        business,
                        GetSharedBusinessSurvivalCashReserveCents(business),
                        weekKey,
                        mode,
                        out int transferred,
                        out _)
                    && transferred > 0)
                {
                    count++;
                    AppendSummarySegment(builder, $"cash transfer {GetBusinessSummaryLabel(business)} {FormatMoney(transferred)}");
                }
            }

            return count;
        }

        private int ResolveLiveryFreightOperations(StringBuilder summary)
        {
            int count = 0;
            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState business = businesses[i];
                if (business == null || business.BusinessType != BusinessType.LiveryFreight || business.RuntimeState == null)
                {
                    continue;
                }

                BusinessProfileDefinition profile = FindProfile(BusinessType.LiveryFreight);
                BusinessRuntimeState runtime = business.RuntimeState;
                float efficiency01 = GetHealthAdjustedOperatingEfficiency01(business);
                int plannedVisits = Mathf.RoundToInt(Mathf.Max(0, profile != null ? profile.BaselineDailyServiceCapacity : business.BaselineDailyServiceCapacity) * efficiency01);
                int upkeepUnits = Mathf.Max(1, Mathf.CeilToInt(plannedVisits / 6f));
                int consumed = 0;
                List<string> blockedReasons = new();
                SeedBlockedReasons(runtime, blockedReasons);
                AppendStaffingBlockedReasons(runtime, blockedReasons);

                if (plannedVisits <= 0)
                {
                    blockedReasons.Add("no staffed hauling capacity");
                }

                CategoryStockState upkeep = runtime.GetCategoryStock(CategoryLiveryFeedUpkeep);
                if (upkeep != null && plannedVisits > 0)
                {
                    ProcureInputsForWeeklyOperation(business, profile, CategoryLiveryFeedUpkeep, upkeepUnits, 1, blockedReasons);
                    if (runtime.TryConsumeCategoryStockUnits(CategoryLiveryFeedUpkeep, upkeepUnits, out int consumedUnits))
                    {
                        consumed = consumedUnits;
                    }
                    else
                    {
                        blockedReasons.Add($"input shortage: {CategoryLiveryFeedUpkeep}");
                        plannedVisits = Mathf.Max(0, plannedVisits / 2);
                    }
                }

                int repairUnits = 0;
                CategoryStockState repairs = runtime.GetCategoryStock(CategoryWheelwrightRepairs);
                if (repairs != null && plannedVisits > 0 && repairs.CurrentStockUnits > 0)
                {
                    int desiredRepairs = Mathf.Max(1, Mathf.CeilToInt(plannedVisits / 12f));
                    if (runtime.TryConsumeCategoryStockUnits(CategoryWheelwrightRepairs, desiredRepairs, out int consumedRepairs))
                    {
                        repairUnits = consumedRepairs;
                        runtime.AdjustReliability01(0.02f);
                    }
                }

                int revenue = plannedVisits * 45;
                if (revenue > 0)
                {
                    runtime.RecordDailyServiceRevenue(CategoryFreightService, plannedVisits, revenue);
                }

                business.ApplyWeeklyOperationResult(0);
                business.ApplyDailyServiceResult(plannedVisits);
                string blocked = BuildBlockedReason(blockedReasons);
                string repairRead = repairs != null ? $"; wheelwright upkeep {repairUnits}" : string.Empty;
                string result = $"livery & freight service {plannedVisits} visits/day; upkeep {consumed} {CategoryLiveryFeedUpkeep}{repairRead}; revenue {FormatMoney(revenue)}";
                runtime.RecordWeeklyOperation(consumed, plannedVisits, result, blocked);
                RecordOperationSummary(business, string.IsNullOrWhiteSpace(blocked) ? result : $"{result}; blocked: {blocked}");
                AppendSummarySegment(summary, $"{business.BusinessType} service {plannedVisits} {FormatMoney(revenue)}");
                count++;
            }

            return count;
        }

        private void ResolveOwnerDistributionsForSharedBusinesses(StringBuilder builder)
        {
            if (playerPortfolio == null)
            {
                return;
            }

            int totalDistributed = 0;
            int weekKey = GetCurrentWeekKey();
            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState business = businesses[i];
                if (business == null
                    || business.RuntimeState == null
                    || business.BusinessType == BusinessType.GeneralStore
                    || business.Owner == null
                    || business.Owner.OwnerKind != BusinessOwnerKind.Player)
                {
                    continue;
                }

                int reserve = GetSharedOperatingCashBufferCents(business);
                totalDistributed += playerPortfolio.ResolveOwnerDistribution(
                    business,
                    reserve,
                    weekKey,
                    business.RuntimeDisplayName,
                    business.RuntimeState.LastWeeklyReorderBudgetCents);
            }

            if (totalDistributed > 0)
            {
                AppendSummarySegment(builder, $"owner distributions {FormatMoney(totalDistributed)}");
            }
        }

        public BusinessInstanceState FindByType(BusinessType businessType)
        {
            for (int i = 0; i < businesses.Count; i++)
            {
                if (businesses[i] != null && businesses[i].BusinessType == businessType)
                {
                    return businesses[i];
                }
            }

            return null;
        }

        public int GetBoardingHouseBedCapacity(BusinessInstanceState business)
        {
            return business != null && business.BusinessType == BusinessType.BoardingHouse
                ? Mathf.Max(0, business.BaselineDailyServiceCapacity)
                : 0;
        }

        public int GetBoardingHouseOccupiedBeds(BusinessInstanceState business)
        {
            if (business == null
                || business.BusinessType != BusinessType.BoardingHouse
                || populationManager == null
                || populationManager.State == null
                || populationManager.State.households == null)
            {
                return 0;
            }

            int occupied = 0;
            for (int i = 0; i < populationManager.State.households.Count; i++)
            {
                HouseholdState household = populationManager.State.households[i];
                household?.EnsureSettlementStateInitialized();
                if (household == null || (!household.isBoardingHouseGuestHousehold && !household.isBoardingHouseLodging))
                {
                    continue;
                }

                string lodgingBusinessId = !string.IsNullOrWhiteSpace(household.boardingBusinessInstanceId)
                    ? household.boardingBusinessInstanceId
                    : household.lodgingBusinessInstanceId;
                bool sameBusiness = !string.IsNullOrWhiteSpace(lodgingBusinessId)
                    && string.Equals(lodgingBusinessId, business.InstanceId, StringComparison.OrdinalIgnoreCase);
                bool sameBuilding = household.homeBuildingId >= 0 && household.homeBuildingId == business.AssignedBuildingId;
                if (!sameBusiness && !sameBuilding)
                {
                    continue;
                }

                occupied += household.boarderPersonIds != null ? household.boarderPersonIds.Count : 0;
            }

            return Mathf.Max(0, occupied);
        }

        public string BuildBoardingHouseOccupancyLine(BusinessInstanceState business)
        {
            if (business == null || business.BusinessType != BusinessType.BoardingHouse)
            {
                return "Boarding House occupancy unavailable.";
            }

            int capacity = GetBoardingHouseBedCapacity(business);
            int used = GetBoardingHouseOccupiedBeds(business);
            int open = Mathf.Max(0, capacity - used);
            string settlement = populationManager != null ? populationManager.BuildTownSettlementHeadline() : string.Empty;
            string weeklyCash = $"Revenue {FormatMoney(business.LastWeeklyLocalTransferRevenueCents)} | Upkeep {FormatMoney(business.LastWeeklyLocalTransferCostCents)}";
            return string.IsNullOrWhiteSpace(settlement)
                ? $"Boarding House beds: {used}/{capacity} occupied | {open} open | {weeklyCash}"
                : $"Boarding House beds: {used}/{capacity} occupied | {open} open | {weeklyCash} | {settlement}";
        }

        public string BuildBusinessInspectionSummaryByBuildingId(int buildingId)
        {
            BusinessInstanceState business = FindByBuildingId(buildingId);
            return BuildBusinessInspectionSummary(business);
        }

        public string BuildBusinessInspectionSummary(BusinessInstanceState business)
        {
            if (business == null || business.RuntimeState == null)
            {
                return string.Empty;
            }

            PrepareRecurringRelationshipInspectionState();
            BusinessRuntimeState runtime = business.RuntimeState;
            StringBuilder builder = new();
            builder.AppendLine($"{business.RuntimeDisplayName} | {GetOwnerDisplayName(business)}");
            builder.AppendLine($"Type: {business.BusinessType} | Staff {runtime.FilledWorkerCount}/{runtime.TargetWorkerCount} | Efficiency {GetHealthAdjustedOperatingEfficiency01(business):P0}");
            builder.AppendLine(BuildOperatingLine(business));
            builder.AppendLine(BuildCapacitySummaryLine(business));

            string townContext = BuildTownContextLine(business);
            if (!string.IsNullOrWhiteSpace(townContext))
            {
                builder.AppendLine(townContext);
            }

            // Build the recurring-order inspection snapshot once so the business read stays consistent
            // across summary lines and does not re-walk relationship state more than needed.
            BusinessRecurringRelationshipInspectionSnapshot relationshipSnapshot = BuildBusinessRecurringRelationshipInspectionSnapshot(business);
            string relationshipSummary = BuildBusinessRelationshipSummary(relationshipSnapshot);
            if (!string.IsNullOrWhiteSpace(relationshipSummary))
            {
                builder.AppendLine(relationshipSummary);
            }

            string counterpartySummary = BuildBusinessActiveCounterpartyLine(business, relationshipSnapshot);
            if (!string.IsNullOrWhiteSpace(counterpartySummary))
            {
                builder.AppendLine(counterpartySummary);
            }

            string supportingTrade = BuildLocalTradeCashSupportLine(business);
            if (!string.IsNullOrWhiteSpace(supportingTrade))
            {
                builder.AppendLine(supportingTrade);
            }

            string lastOperation = GetLastOperationSummary(business);
            if (!string.IsNullOrWhiteSpace(lastOperation))
            {
                builder.AppendLine($"Status: {lastOperation}");
            }

            string blocked = GetWeeklyBlockedReason(runtime);
            if (!string.IsNullOrWhiteSpace(blocked) && !string.Equals(blocked, "None", StringComparison.OrdinalIgnoreCase))
            {
                builder.AppendLine($"Current pressure: {blocked}");
            }

            string transferSummary = GetWeeklyTransferSummary(runtime);
            if (!string.IsNullOrWhiteSpace(transferSummary) && !string.Equals(transferSummary, "none", StringComparison.OrdinalIgnoreCase))
            {
                builder.AppendLine($"Trade: {transferSummary}");
            }

            if (logisticsRuntime != null)
            {
                builder.AppendLine(logisticsRuntime.BuildBusinessLogisticsSummary(business.InstanceId));
            }

            if (business.BusinessType == BusinessType.BoardingHouse)
            {
                builder.AppendLine(BuildBoardingHouseOccupancyLine(business));
            }

            string mineInspection = BuildMineInspectionLine(business);
            if (!string.IsNullOrWhiteSpace(mineInspection))
            {
                builder.AppendLine(mineInspection);
            }

            return builder.ToString().Trim();
        }

        private static bool ShouldSurfaceRecurringRelationshipInInspection(LocalRecurringOrderRelationshipState relationship)
        {
            return relationship != null
                && (!relationship.Active
                    ? !string.Equals(relationship.CancellationReason, SupersededLegacyRecurringLocalOrderCancellationReason, StringComparison.OrdinalIgnoreCase)
                    : true);
        }

        private BusinessRecurringRelationshipInspectionSnapshot BuildBusinessRecurringRelationshipInspectionSnapshot(BusinessInstanceState business)
        {
            BusinessRecurringRelationshipInspectionSnapshot snapshot = new();
            if (business == null || string.IsNullOrWhiteSpace(business.InstanceId))
            {
                return snapshot;
            }

            IReadOnlyList<LocalRecurringOrderRelationshipState> relationships = LocalOrderRelationships;
            for (int i = 0; i < relationships.Count; i++)
            {
                LocalRecurringOrderRelationshipState relationship = relationships[i];
                if (!ShouldSurfaceRecurringRelationshipInInspection(relationship))
                {
                    continue;
                }

                bool isBuyer = string.Equals(relationship.BuyerInstanceId, business.InstanceId, StringComparison.OrdinalIgnoreCase);
                bool isSeller = string.Equals(relationship.SellerInstanceId, business.InstanceId, StringComparison.OrdinalIgnoreCase);
                if (!isBuyer && !isSeller)
                {
                    continue;
                }

                if (relationship.Active)
                {
                    if (isBuyer)
                    {
                        snapshot.ActiveBuying++;
                        snapshot.SupplierRelationship = ChooseHigherPriorityInspectionRelationship(snapshot.SupplierRelationship, relationship, asBuyer: true);
                    }

                    if (isSeller)
                    {
                        snapshot.ActiveSelling++;
                        snapshot.CustomerRelationship = ChooseHigherPriorityInspectionRelationship(snapshot.CustomerRelationship, relationship, asBuyer: false);
                    }
                }

                if (IsCleanRecurringRelationship(relationship))
                {
                    snapshot.Clean++;
                }

                if (IsCancelledRecurringRelationship(relationship))
                {
                    snapshot.Cancelled++;
                }

                if (IsActiveStrainedRecurringRelationship(relationship))
                {
                    snapshot.ActiveStrained++;
                }

                if (IsStrainedRecurringRelationship(relationship))
                {
                    snapshot.MostStrained = ChooseHigherStrainRelationship(snapshot.MostStrained, relationship);
                }
            }

            return snapshot;
        }

        private TownRecurringRelationshipInspectionSnapshot BuildTownRecurringRelationshipInspectionSnapshot()
        {
            TownRecurringRelationshipInspectionSnapshot snapshot = new();
            IReadOnlyList<LocalRecurringOrderRelationshipState> relationships = LocalOrderRelationships;
            for (int i = 0; i < relationships.Count; i++)
            {
                LocalRecurringOrderRelationshipState relationship = relationships[i];
                if (!ShouldSurfaceRecurringRelationshipInInspection(relationship))
                {
                    continue;
                }

                snapshot.Total++;
                if (relationship.Active)
                {
                    snapshot.Active++;
                }

                if (IsCleanRecurringRelationship(relationship))
                {
                    snapshot.Clean++;
                }

                if (IsCancelledRecurringRelationship(relationship))
                {
                    snapshot.Cancelled++;
                }

                if (IsActiveStrainedRecurringRelationship(relationship))
                {
                    snapshot.ActiveStrained++;
                }

                if (IsStrainedRecurringRelationship(relationship))
                {
                    snapshot.MostStrained = ChooseHigherStrainRelationship(snapshot.MostStrained, relationship);
                }
            }

            return snapshot;
        }

        private LocalRecurringOrderRelationshipState ChooseHigherPriorityInspectionRelationship(
            LocalRecurringOrderRelationshipState current,
            LocalRecurringOrderRelationshipState candidate,
            bool asBuyer)
        {
            if (candidate == null)
            {
                return current;
            }

            return current == null || CompareBusinessInspectionRelationshipPriority(candidate, current, asBuyer) < 0
                ? candidate
                : current;
        }

        private static LocalRecurringOrderRelationshipState ChooseHigherStrainRelationship(
            LocalRecurringOrderRelationshipState current,
            LocalRecurringOrderRelationshipState candidate)
        {
            if (candidate == null)
            {
                return current;
            }

            return current == null || CompareRelationshipStrain(candidate, current) > 0
                ? candidate
                : current;
        }

        // Inspection surfaces should normalize recurring relationships against the live runtime first.
        // General Store-linked validation depends on the current business instance being initialized,
        // so keep this prep in one helper instead of repeating slightly different call patterns.
        private void PrepareRecurringRelationshipInspectionState()
        {
            InitializeIfNeeded(generalStoreRuntime != null ? generalStoreRuntime.CurrentBusiness : null);
            NormalizeRecurringRelationshipActives(GetCurrentWeekKey());
        }

        public string BuildTownCommerceLedgerSummary()
        {
            PrepareRecurringRelationshipInspectionState();
            TownRecurringRelationshipInspectionSnapshot relationshipSummary = BuildTownRecurringRelationshipInspectionSnapshot();

            int boardingPressure = 0;
            int medicalPressure = 0;
            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState business = businesses[i];
                if (business == null || business.RuntimeState == null)
                {
                    continue;
                }

                if (business.BusinessType == BusinessType.BoardingHouse && GetBoardingHouseOccupiedBeds(business) >= Mathf.Max(1, GetBoardingHouseBedCapacity(business)))
                {
                    boardingPressure++;
                }

                if (business.BusinessType == BusinessType.Doctor && business.RuntimeState.FilledWorkerCount < business.RuntimeState.TargetWorkerCount)
                {
                    medicalPressure++;
                }
            }

            string weakest = relationshipSummary.MostStrained != null ? $" | weakest link {BuildRelationshipDetail(relationshipSummary.MostStrained)}" : string.Empty;
            int activeTransferAgreements = 0;
            for (int i = 0; i < transferAgreements.Count; i++)
            {
                if (transferAgreements[i] != null && transferAgreements[i].Active)
                {
                    activeTransferAgreements++;
                }
            }

            string logisticsLedger = logisticsRuntime != null ? logisticsRuntime.BuildTownLogisticsLedgerSummary() : "Logistics ledger: unavailable.";
            return $"Commerce Ledger - Local orders {relationshipSummary.Total} total, {relationshipSummary.Active} active, {relationshipSummary.Clean} clean, {relationshipSummary.ActiveStrained} active strained, {relationshipSummary.Cancelled} cancelled{weakest} | Owned transfers {activeTransferAgreements} active | Boarding pressure {boardingPressure} | Medical staffing pressure {medicalPressure}. {logisticsLedger}";

        }



        public string BuildTownProcessClimateSummary()
        {
            string commerceLedger = BuildTownCommerceLedgerSummary();
            string service = BuildTownServicePressureHeadline();
            return $"Process climate: {commerceLedger} | {service}";
        }

        public string BuildTownDecisionClimateHeadline()
        {
            string commerce = BuildTownCommerceHeadline();
            string service = BuildTownServicePressureHeadline();
            return $"Decision climate: {commerce} | {service}";
        }

        public string BuildTownOperatingClimateSummary()
        {
            string commerce = BuildTownCommerceHeadline();
            string ledger = BuildTownCommerceLedgerSummary();
            string service = BuildTownServicePressureHeadline();
            return $"Town climate: {commerce} | {ledger} | {service}";
        }

        public string BuildTownServicePressureHeadline()
        {
            InitializeIfNeeded(generalStoreRuntime != null ? generalStoreRuntime.CurrentBusiness : null);

            int boardingFull = 0;
            int doctorUnderstaffed = 0;
            int sawmillBlocked = 0;
            int lumberYardThin = 0;
            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState business = businesses[i];
                if (business == null || business.RuntimeState == null)
                {
                    continue;
                }

                if (business.BusinessType == BusinessType.BoardingHouse && GetBoardingHouseOccupiedBeds(business) >= Mathf.Max(1, GetBoardingHouseBedCapacity(business)))
                {
                    boardingFull++;
                }

                if (business.BusinessType == BusinessType.Doctor && business.RuntimeState.FilledWorkerCount < business.RuntimeState.TargetWorkerCount)
                {
                    doctorUnderstaffed++;
                }

                if (business.BusinessType == BusinessType.Sawmill)
                {
                    string blocked = GetWeeklyBlockedReason(business.RuntimeState);
                    if (!string.IsNullOrWhiteSpace(blocked) && !string.Equals(blocked, "None", StringComparison.OrdinalIgnoreCase))
                    {
                        sawmillBlocked++;
                    }
                }

                if (business.BusinessType == BusinessType.LumberYard && IsRuntimeStockThin(business.RuntimeState))
                {
                    lumberYardThin++;
                }
            }

            return $"Service pressure: Boarding full {boardingFull} | Doctor understaffed {doctorUnderstaffed} | Sawmills blocked {sawmillBlocked} | Lumber yards thin {lumberYardThin}.";
        }

        private static bool IsRuntimeStockThin(BusinessRuntimeState runtimeState)
        {
            if (runtimeState == null || runtimeState.CategoryStock == null || runtimeState.CategoryStock.Count == 0)
            {
                return false;
            }

            int currentUnits = 0;
            int targetUnits = 0;
            for (int i = 0; i < runtimeState.CategoryStock.Count; i++)
            {
                CategoryStockState stock = runtimeState.CategoryStock[i];
                if (stock == null)
                {
                    continue;
                }

                currentUnits += stock.CurrentStockUnits;
                targetUnits += stock.TargetStockUnits;
            }

            return currentUnits <= Mathf.Max(4, targetUnits / 4);
        }

        public string BuildTownCommerceHeadline()
        {
            PrepareRecurringRelationshipInspectionState();
            int activeBusinesses = 0;
            int playerOwnedBusinesses = 0;
            int pressuredBusinesses = 0;
            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState business = businesses[i];
                if (business == null || business.RuntimeState == null)
                {
                    continue;
                }

                activeBusinesses++;
                if (business.Owner != null && business.Owner.OwnerKind == BusinessOwnerKind.Player)
                {
                    playerOwnedBusinesses++;
                }

                string blocked = GetWeeklyBlockedReason(business.RuntimeState);
                if (!string.IsNullOrWhiteSpace(blocked) && !string.Equals(blocked, "None", StringComparison.OrdinalIgnoreCase))
                {
                    pressuredBusinesses++;
                }
            }

            TownRecurringRelationshipInspectionSnapshot relationshipSummary = BuildTownRecurringRelationshipInspectionSnapshot();

            string operationsRead = pressuredBusinesses > 0
                ? $"{pressuredBusinesses} strained"
                : "steady";
            string orderRead = relationshipSummary.Total > 0
                ? $"{relationshipSummary.Clean}/{relationshipSummary.Active} active local orders clean, {relationshipSummary.ActiveStrained} active strained, {relationshipSummary.Cancelled} cancelled"
                : "no recurring orders yet";

            return $"Town commerce: {activeBusinesses} live | {playerOwnedBusinesses} player-run | {operationsRead} | {orderRead}.";
        }

        private string BuildBusinessRelationshipSummary(BusinessInstanceState business)
        {
            PrepareRecurringRelationshipInspectionState();
            return BuildBusinessRelationshipSummary(BuildBusinessRecurringRelationshipInspectionSnapshot(business));
        }

        private string BuildBusinessRelationshipSummary(BusinessRecurringRelationshipInspectionSnapshot snapshot)
        {
            if (snapshot.ActiveBuying <= 0 && snapshot.ActiveSelling <= 0)
            {
                return snapshot.Cancelled > 0
                    ? $"Local orders: no active recurring agreements | {snapshot.Cancelled} cancelled history."
                    : "Local orders: no active recurring agreements.";
            }

            string core = $"Local orders: {snapshot.ActiveBuying} active buying | {snapshot.ActiveSelling} active selling | {snapshot.Clean} clean | {snapshot.ActiveStrained} active strained | {snapshot.Cancelled} cancelled.";
            return snapshot.MostStrained != null
                ? $"{core} Weak link: {BuildRelationshipDetail(snapshot.MostStrained)}."
                : $"{core} Steady local trade.";
        }

        private string BuildBusinessActiveCounterpartyLine(BusinessInstanceState business)
        {
            PrepareRecurringRelationshipInspectionState();
            return BuildBusinessActiveCounterpartyLine(business, BuildBusinessRecurringRelationshipInspectionSnapshot(business));
        }

        private string BuildBusinessActiveCounterpartyLine(BusinessInstanceState business, BusinessRecurringRelationshipInspectionSnapshot snapshot)
        {
            if (business == null || (snapshot.SupplierRelationship == null && snapshot.CustomerRelationship == null))
            {
                return string.Empty;
            }

            List<string> segments = new();
            if (snapshot.SupplierRelationship != null)
            {
                segments.Add($"current supplier {BuildRelationshipCounterpartyRead(snapshot.SupplierRelationship, business, asBuyer: true)}");
            }

            if (snapshot.CustomerRelationship != null)
            {
                segments.Add($"current buyer {BuildRelationshipCounterpartyRead(snapshot.CustomerRelationship, business, asBuyer: false)}");
            }

            return segments.Count > 0
                ? $"Current counterparties: {string.Join(" | ", segments)}."
                : string.Empty;
        }

        private int CompareBusinessInspectionRelationshipPriority(
            LocalRecurringOrderRelationshipState left,
            LocalRecurringOrderRelationshipState right,
            bool asBuyer)
        {
            bool leftActive = left != null && left.Active;
            bool rightActive = right != null && right.Active;
            if (leftActive != rightActive)
            {
                return leftActive ? -1 : 1;
            }

            int leftStrain = CalculateRelationshipStrainScore(left);
            int rightStrain = CalculateRelationshipStrainScore(right);
            if (leftStrain != rightStrain)
            {
                return rightStrain.CompareTo(leftStrain);
            }

            int leftVolume = left != null ? left.TargetQuantity : 0;
            int rightVolume = right != null ? right.TargetQuantity : 0;
            if (leftVolume != rightVolume)
            {
                return rightVolume.CompareTo(leftVolume);
            }

            string leftKey = asBuyer ? left?.SellerInstanceId : left?.BuyerInstanceId;
            string rightKey = asBuyer ? right?.SellerInstanceId : right?.BuyerInstanceId;
            return string.Compare(leftKey ?? string.Empty, rightKey ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        private string BuildRelationshipCounterpartyRead(LocalRecurringOrderRelationshipState relationship, BusinessInstanceState business, bool asBuyer)
        {
            if (relationship == null || business == null)
            {
                return string.Empty;
            }

            string counterpartyName = asBuyer
                ? BuildRelationshipPartyLabel(relationship.SellerDisplayName, relationship.SellerInstanceId)
                : BuildRelationshipPartyLabel(relationship.BuyerDisplayName, relationship.BuyerInstanceId);
            string category = asBuyer ? relationship.BuyerCategoryId : relationship.SellerCategoryId;
            string status = GetRelationshipStatusLabel(relationship);
            return $"{counterpartyName} for {category}, {status}, health {Mathf.RoundToInt(relationship.RelationshipHealth01 * 100f)}";
        }

        private struct BusinessRecurringRelationshipInspectionSnapshot
        {
            public int ActiveBuying;
            public int ActiveSelling;
            public int Clean;
            public int ActiveStrained;
            public int Cancelled;
            public LocalRecurringOrderRelationshipState MostStrained;
            public LocalRecurringOrderRelationshipState SupplierRelationship;
            public LocalRecurringOrderRelationshipState CustomerRelationship;
        }

        private struct TownRecurringRelationshipInspectionSnapshot
        {
            public int Total;
            public int Active;
            public int Clean;
            public int ActiveStrained;
            public int Cancelled;
            public LocalRecurringOrderRelationshipState MostStrained;
        }

        private static bool IsCleanRecurringRelationship(LocalRecurringOrderRelationshipState relationship)
        {
            return relationship != null
                && relationship.Active
                && (relationship.LastStatus == LocalRecurringOrderFulfillmentStatus.Clean
                    || relationship.LastStatus == LocalRecurringOrderFulfillmentStatus.Renewed
                    || relationship.LastStatus == LocalRecurringOrderFulfillmentStatus.SkippedNoNeed);
        }

        private static bool IsCancelledRecurringRelationship(LocalRecurringOrderRelationshipState relationship)
        {
            return relationship != null
                && (!relationship.Active || relationship.LastStatus == LocalRecurringOrderFulfillmentStatus.Cancelled);
        }

        private static bool IsActiveStrainedRecurringRelationship(LocalRecurringOrderRelationshipState relationship)
        {
            return relationship != null
                && relationship.Active
                && (relationship.LastStatus == LocalRecurringOrderFulfillmentStatus.Short
                    || relationship.LastStatus == LocalRecurringOrderFulfillmentStatus.Failed
                    || relationship.LastStatus == LocalRecurringOrderFulfillmentStatus.BuyerCashBreach
                    || relationship.RelationshipHealth01 < 0.35f);
        }

        private static bool IsStrainedRecurringRelationship(LocalRecurringOrderRelationshipState relationship)
        {
            if (relationship == null)
            {
                return false;
            }

            return IsCancelledRecurringRelationship(relationship)
                || relationship.LastStatus == LocalRecurringOrderFulfillmentStatus.Short
                || relationship.LastStatus == LocalRecurringOrderFulfillmentStatus.Failed
                || relationship.LastStatus == LocalRecurringOrderFulfillmentStatus.BuyerCashBreach
                || relationship.RelationshipHealth01 < 0.35f;
        }

        private static int CompareRelationshipStrain(
            LocalRecurringOrderRelationshipState left,
            LocalRecurringOrderRelationshipState right)
        {
            int leftScore = CalculateRelationshipStrainScore(left);
            int rightScore = CalculateRelationshipStrainScore(right);
            if (leftScore != rightScore)
            {
                return leftScore.CompareTo(rightScore);
            }

            return (right?.LastResolvedWeekKey ?? -1).CompareTo(left?.LastResolvedWeekKey ?? -1);
        }

        private static int CalculateRelationshipStrainScore(LocalRecurringOrderRelationshipState relationship)
        {
            if (relationship == null)
            {
                return 0;
            }

            int score = Mathf.RoundToInt((1f - relationship.RelationshipHealth01) * 100f);
            if (IsCancelledRecurringRelationship(relationship))
            {
                score += 160;
            }

            if (relationship.LastStatus == LocalRecurringOrderFulfillmentStatus.BuyerCashBreach)
            {
                score += 90;
            }
            else if (relationship.LastStatus == LocalRecurringOrderFulfillmentStatus.Failed)
            {
                score += 110;
            }
            else if (relationship.LastStatus == LocalRecurringOrderFulfillmentStatus.Short)
            {
                score += 70;
            }

            score += relationship.ConsecutiveBreachWeeks * 25;
            return score;
        }

        private static string BuildRelationshipDetail(LocalRecurringOrderRelationshipState relationship)
        {
            if (relationship == null)
            {
                return string.Empty;
            }

            string status = GetRelationshipStatusLabel(relationship);
            string reason = string.IsNullOrWhiteSpace(relationship.LastBreachReason)
                ? string.Empty
                : $" ({relationship.LastBreachReason})";
            return $"{BuildRelationshipPartyLabel(relationship.SellerDisplayName, relationship.SellerInstanceId)}->{BuildRelationshipPartyLabel(relationship.BuyerDisplayName, relationship.BuyerInstanceId)} {relationship.BuyerCategoryId} {status} {relationship.LastFulfilledUnits}/{relationship.LastRequestedUnits}, health {Mathf.RoundToInt(relationship.RelationshipHealth01 * 100f)}{reason}";
        }

        private static string BuildRelationshipPartyLabel(string displayName, string instanceId)
        {
            string display = displayName ?? string.Empty;
            string id = instanceId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(id))
            {
                return string.IsNullOrWhiteSpace(display) ? "unknown" : display;
            }

            return string.IsNullOrWhiteSpace(display) || string.Equals(display, id, StringComparison.OrdinalIgnoreCase)
                ? id
                : $"{display} ({id})";
        }

        private static string GetRelationshipStatusLabel(LocalRecurringOrderRelationshipState relationship)
        {
            if (relationship == null)
            {
                return "unknown";
            }

            if (IsCancelledRecurringRelationship(relationship))
            {
                return "cancelled";
            }

            return relationship.LastStatus switch
            {
                LocalRecurringOrderFulfillmentStatus.Clean => "clean",
                LocalRecurringOrderFulfillmentStatus.Renewed => "renewed",
                LocalRecurringOrderFulfillmentStatus.Short => "short",
                LocalRecurringOrderFulfillmentStatus.Failed => "failed",
                LocalRecurringOrderFulfillmentStatus.BuyerCashBreach => "buyer cash breach",
                LocalRecurringOrderFulfillmentStatus.SkippedNoNeed => "stock full",
                LocalRecurringOrderFulfillmentStatus.Delayed => "not due",
                _ => "unresolved"
            };
        }

        private static string BuildLocalTradeCashSupportLine(BusinessInstanceState business)
        {
            BusinessRuntimeState runtime = business != null ? business.RuntimeState : null;
            if (runtime == null)
            {
                return string.Empty;
            }

            int revenue = Mathf.Max(0, runtime.LastWeeklyLocalTransferRevenueCents);
            int cost = Mathf.Max(0, runtime.LastWeeklyLocalTransferCostCents);
            if (revenue <= 0 && cost <= 0)
            {
                return string.Empty;
            }

            string support = revenue >= cost ? "supporting trade helped keep cash moving" : "local supply spending kept stock moving";
            return $"Supporting trade: +{FormatMoney(revenue)}/-{FormatMoney(cost)}; {support}.";
        }

        public IReadOnlyList<NewcomerSettlementBoardingOption> BuildBoardingHouseSettlementOptions()
        {
            InitializeIfNeeded(generalStoreRuntime != null ? generalStoreRuntime.CurrentBusiness : null);
            List<NewcomerSettlementBoardingOption> options = new();
            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState business = businesses[i];
                if (business == null
                    || business.BusinessType != BusinessType.BoardingHouse
                    || business.RuntimeState == null
                    || business.AssignedBuildingId < 0)
                {
                    continue;
                }

                int capacity = GetBoardingHouseBedCapacity(business);
                if (capacity <= 0)
                {
                    continue;
                }

                options.Add(new NewcomerSettlementBoardingOption(
                    business.InstanceId,
                    business.AssignedBuildingId,
                    business.RuntimeDisplayName,
                    capacity,
                    business.Owner != null && business.Owner.OwnerKind == BusinessOwnerKind.Player,
                    business.RuntimeState.CurrentCashCents,
                    business.RuntimeState.FilledWorkerCount,
                    business.RuntimeState.TargetWorkerCount));
            }

            return options;
        }

        public void SetSawmillConstructionSupportNode(ConstructionSupportNodeState sawmill)
        {
            linkedSawmillSupportNode = sawmill != null && sawmill.ResourceKind == ConstructionResourceKind.Lumber
                ? sawmill
                : null;
            BusinessInstanceState sawmillBusiness = FindByType(BusinessType.Sawmill);
            if (linkedSawmillSupportNode != null && sawmillBusiness != null && sawmillBusiness.RuntimeState != null)
            {
                EnsureSawmillBusinessStockCategories(sawmillBusiness.RuntimeState, linkedSawmillSupportNode);
                SyncLinkedSawmillSupportFromBusiness(sawmillBusiness);
                sawmillBusiness.RefreshCapacityState();
                return;
            }

            SyncLinkedSawmillBusinessFromSupport();
        }

        public bool TryResolveWeeklySawmillProduction(
            ConstructionSupportNodeState sawmill,
            int weekKey,
            out string summary,
            out bool produced)
        {
            summary = string.Empty;
            produced = false;
            if (sawmill == null || sawmill.ResourceKind != ConstructionResourceKind.Lumber)
            {
                return false;
            }

            linkedSawmillSupportNode = sawmill;
            BusinessInstanceState business = FindByType(BusinessType.Sawmill);
            if (business == null || business.RuntimeState == null)
            {
                return false;
            }

            EnsureSawmillBusinessStockCategories(business.RuntimeState, linkedSawmillSupportNode);
            // Weekly production runs from shared business stock first, then mirrors the support
            // node back out for construction-facing compatibility.
            SyncLinkedSawmillSupportFromBusiness(business);
            produced = ResolveSawmillBusinessProduction(business, sawmill, weekKey, null);
            summary = sawmill.LastWeeklyProductionSummary;
            return true;
        }

        public int RecordSawmillConstructionLumberSale(int units, int unitCostCents)
        {
            int requested = Mathf.Max(0, units);
            if (requested <= 0)
            {
                return 0;
            }

            InitializeIfNeeded(generalStoreRuntime != null ? generalStoreRuntime.CurrentBusiness : null);
            BusinessInstanceState sawmill = FindByType(BusinessType.Sawmill);
            if (sawmill == null || sawmill.RuntimeState == null)
            {
                return 0;
            }

            int mirroredConsumed = 0;
            if (linkedSawmillSupportNode != null)
            {
                SyncSawmillBusinessStockFromSupport(sawmill.RuntimeState, linkedSawmillSupportNode);
                mirroredConsumed = requested;
            }
            else
            {
                CategoryStockState lumberStock = sawmill.RuntimeState.GetCategoryStock(CategoryLumber);
                if (lumberStock != null && lumberStock.CurrentStockUnits > 0)
                {
                    int mirrorUnits = Mathf.Min(requested, lumberStock.CurrentStockUnits);
                    sawmill.RuntimeState.TryConsumeCategoryStockUnits(CategoryLumber, mirrorUnits, out mirroredConsumed);
                }
            }

            int revenue = requested * Mathf.Max(0, unitCostCents);
            if (revenue > 0)
            {
                sawmill.RuntimeState.AddWeeklyLocalTransferRevenue(
                    revenue,
                    $"sold {requested} lumber to construction for {FormatMoney(revenue)}");
            }

            sawmill.RefreshCapacityState();
            AppendTransferToOperationSummary(sawmill, $"; sold {requested} lumber to construction for {FormatMoney(revenue)}");
            return mirroredConsumed;
        }

        public bool TryBuildLumberPurchaseAllocations(
            int buyerPlotId,
            int requestedUnits,
            int baselineUnitCostCents,
            List<ConstructionResourceSourceAllocation> allocations,
            out int availableUnits,
            out int weightedUnitCostCents,
            out string sourceLabel)
        {
            availableUnits = 0;
            weightedUnitCostCents = Mathf.Max(0, baselineUnitCostCents);
            sourceLabel = string.Empty;
            allocations?.Clear();

            AutoWire();
            InitializeIfNeeded(generalStoreRuntime != null ? generalStoreRuntime.CurrentBusiness : null);
            List<LumberSellerCandidate> candidates = BuildLumberSellerCandidates(buyerPlotId, requestedUnits, baselineUnitCostCents);
            if (candidates.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                availableUnits += Mathf.Max(0, candidates[i].stockUnits);
            }

            int remaining = Mathf.Max(0, requestedUnits);
            int allocatedUnits = 0;
            int allocatedCost = 0;
            for (int i = 0; i < candidates.Count && remaining > 0; i++)
            {
                LumberSellerCandidate candidate = candidates[i];
                int units = Mathf.Min(remaining, candidate.stockUnits);
                if (units <= 0)
                {
                    continue;
                }

                allocations?.Add(ConstructionResourceSourceAllocation.Create(
                    candidate.business.BusinessType,
                    candidate.business.InstanceId,
                    candidate.sourceLabel,
                    units,
                    candidate.unitPriceCents,
                    candidate.score));
                remaining -= units;
                allocatedUnits += units;
                allocatedCost += units * candidate.unitPriceCents;
            }

            if (allocatedUnits > 0)
            {
                weightedUnitCostCents = Mathf.Max(1, Mathf.CeilToInt(allocatedCost / (float)allocatedUnits));
            }
            else
            {
                weightedUnitCostCents = Mathf.Max(1, candidates[0].unitPriceCents);
            }

            sourceLabel = BuildLumberSellerSourceLabel(candidates, allocations);
            return true;
        }

        public bool TryGetSharedLumberAvailability(
            int buyerPlotId,
            int baselineUnitCostCents,
            out int availableUnits,
            out string sourceLabel)
        {
            List<ConstructionResourceSourceAllocation> scratch = new();
            return TryBuildLumberPurchaseAllocations(
                buyerPlotId,
                0,
                baselineUnitCostCents,
                scratch,
                out availableUnits,
                out _,
                out sourceLabel);
        }

        public bool TryConsumeLumberPurchaseAllocations(
            IReadOnlyList<ConstructionResourceSourceAllocation> allocations,
            int requestedUnits,
            out int consumedUnits,
            out int spendCents,
            out string message)
        {
            consumedUnits = 0;
            spendCents = 0;
            int remaining = Mathf.Max(0, requestedUnits);
            if (remaining <= 0)
            {
                message = "No lumber was required.";
                return true;
            }

            if (allocations == null || allocations.Count == 0)
            {
                message = "No shared lumber seller allocation was available.";
                return false;
            }

            List<LumberConsumptionPlan> plannedConsumptions = new();
            for (int i = 0; i < allocations.Count && remaining > 0; i++)
            {
                ConstructionResourceSourceAllocation allocation = allocations[i];
                if (allocation == null || allocation.allocatedUnits <= 0)
                {
                    continue;
                }

                BusinessInstanceState seller = FindByInstanceId(allocation.businessInstanceId);
                if (!CanSellLocalLumber(seller, out CategoryStockState stock) || stock == null)
                {
                    continue;
                }

                int plannedUnits = Mathf.Min(remaining, allocation.allocatedUnits, stock.CurrentStockUnits);
                if (plannedUnits <= 0)
                {
                    continue;
                }

                plannedConsumptions.Add(new LumberConsumptionPlan(seller, plannedUnits, allocation.unitCostCents));
                remaining -= plannedUnits;
            }

            if (remaining > 0)
            {
                message = $"Could not consume {requestedUnits} lumber from shared lumber sellers; {remaining} still missing.";
                return false;
            }

            for (int i = 0; i < plannedConsumptions.Count; i++)
            {
                LumberConsumptionPlan plan = plannedConsumptions[i];
                BusinessInstanceState seller = plan.seller;
                if (seller == null
                    || seller.RuntimeState == null
                    || !seller.RuntimeState.TryConsumeCategoryStockUnits(CategoryLumber, plan.units, out int consumed)
                    || consumed <= 0)
                {
                    message = $"Could not consume {requestedUnits} lumber from shared lumber sellers; consumption changed before closing.";
                    return false;
                }

                int revenue = consumed * Mathf.Max(0, plan.unitCostCents);
                if (revenue > 0)
                {
                    seller.RuntimeState.AddWeeklyLocalTransferRevenue(
                        revenue,
                        $"sold {consumed} lumber to construction for {FormatMoney(revenue)}");
                }

                seller.RefreshCapacityState();
                if (seller.BusinessType == BusinessType.Sawmill)
                {
                    SyncLinkedSawmillSupportFromBusiness(seller);
                }

                AppendTransferToOperationSummary(
                    seller,
                    $"; sold {consumed} lumber to construction for {FormatMoney(revenue)}");
                consumedUnits += consumed;
                spendCents += revenue;
            }

            message = $"Consumed {consumedUnits} lumber from shared lumber sellers.";
            return true;
        }

        public int TrySellHouseholdReserveUnits(
            string reserveCategoryId,
            IReadOnlyList<string> sellerCategoryIds,
            int requestedUnits,
            ref int remainingBudgetCents,
            out int spendCents)
        {
            return TrySellHouseholdReserveUnits(
                reserveCategoryId,
                sellerCategoryIds,
                requestedUnits,
                ref remainingBudgetCents,
                -1,
                out spendCents);
        }

        public int TrySellHouseholdReserveUnits(
            string reserveCategoryId,
            IReadOnlyList<string> sellerCategoryIds,
            int requestedUnits,
            ref int remainingBudgetCents,
            int householdId,
            out int spendCents)
        {
            AutoWire();
            InitializeIfNeeded(generalStoreRuntime != null ? generalStoreRuntime.CurrentBusiness : null);

            spendCents = 0;
            int remainingUnits = Mathf.Max(0, requestedUnits);
            if (remainingUnits <= 0 || remainingBudgetCents <= 0 || sellerCategoryIds == null || sellerCategoryIds.Count == 0)
            {
                return 0;
            }

            int soldUnits = 0;
            List<HouseholdReserveSellerCandidate> candidates = BuildHouseholdReserveSellerCandidates(
                reserveCategoryId,
                sellerCategoryIds,
                remainingUnits,
                remainingBudgetCents,
                householdId);
            for (int i = 0; i < candidates.Count && remainingUnits > 0 && remainingBudgetCents > 0; i++)
            {
                HouseholdReserveSellerCandidate candidate = candidates[i];
                int sold = TrySellHouseholdReserveUnitsFromBusiness(
                    candidate.business,
                    string.IsNullOrWhiteSpace(reserveCategoryId) ? candidate.sellerCategoryId : reserveCategoryId,
                    candidate.sellerCategoryId,
                    remainingUnits,
                    ref remainingBudgetCents,
                    householdId,
                    out int categorySpend);
                if (sold <= 0)
                {
                    continue;
                }

                soldUnits += sold;
                spendCents += categorySpend;
                remainingUnits -= sold;
            }

            return soldUnits;
        }

        public int TrySellTownPulseDemand(
            string reserveCategoryId,
            IReadOnlyList<string> sellerCategoryIds,
            int requestedUnits,
            out int revenueCents)
        {
            AutoWire();
            InitializeIfNeeded(generalStoreRuntime != null ? generalStoreRuntime.CurrentBusiness : null);

            revenueCents = 0;
            int remainingUnits = Mathf.Max(0, requestedUnits);
            if (remainingUnits <= 0 || sellerCategoryIds == null || sellerCategoryIds.Count == 0)
            {
                return 0;
            }

            int soldUnits = 0;
            List<HouseholdReserveSellerCandidate> candidates = BuildHouseholdReserveSellerCandidates(
                reserveCategoryId,
                sellerCategoryIds,
                remainingUnits,
                int.MaxValue,
                -1);
            for (int i = 0; i < candidates.Count && remainingUnits > 0; i++)
            {
                HouseholdReserveSellerCandidate candidate = candidates[i];
                int sold = TrySellTownPulseDemandFromBusiness(
                    candidate.business,
                    string.IsNullOrWhiteSpace(reserveCategoryId) ? candidate.sellerCategoryId : reserveCategoryId,
                    candidate.sellerCategoryId,
                    remainingUnits,
                    out int categoryRevenue);
                if (sold <= 0)
                {
                    continue;
                }

                soldUnits += sold;
                revenueCents += categoryRevenue;
                remainingUnits -= sold;
            }

            return soldUnits;
        }

        public PopulationHealthDailySnapshot ResolveDailyDoctorTreatments(PopulationState population, int absoluteDayIndex)
        {
            AutoWire();
            InitializeIfNeeded(generalStoreRuntime != null ? generalStoreRuntime.CurrentBusiness : null);
            EnsureProfilesLoaded();

            PopulationHealthDailySnapshot snapshot = new()
            {
                activeCases = CountActiveHealthCases(population),
                summary = "No active doctor service."
            };

            if (population == null || population.people == null || population.households == null || snapshot.activeCases <= 0)
            {
                snapshot.untreatedCases = snapshot.activeCases;
                return snapshot;
            }

            BusinessInstanceState doctor = FindBestOperationalDoctor();
            BusinessProfileDefinition profile = FindProfile(BusinessType.Doctor);
            if (doctor == null || doctor.RuntimeState == null || profile == null)
            {
                snapshot.untreatedCases = snapshot.activeCases;
                return snapshot;
            }

            BusinessRuntimeState runtime = doctor.RuntimeState;
            runtime.BeginDailySalesCadence();
            int capacity = Mathf.RoundToInt(Mathf.Max(0, profile.BaselineDailyServiceCapacity) * GetHealthAdjustedOperatingEfficiency01(doctor));
            if (capacity <= 0)
            {
                doctor.ApplyDailyServiceResult(0);
                runtime.RecordWeeklyOperation(0, 0, "0 doctor visits today", "no staffed doctor capacity");
                RecordOperationSummary(doctor, "0 doctor visits today; blocked: no staffed doctor capacity");
                snapshot.untreatedCases = snapshot.activeCases;
                snapshot.summary = "Doctor service had no staffed visit capacity.";
                return snapshot;
            }

            int visitPriceCents = GetAverageCategoryTransferPriceCents(doctor, profile, CategoryMedicalService);
            int remedyPriceCents = GetAverageCategoryTransferPriceCents(doctor, profile, CategoryMedicineRemedies);
            List<PersonState> patients = BuildDoctorPatientList(population);
            int treated = 0;
            int medicalSpend = 0;
            int doctorRevenue = 0;
            int remediesUsed = 0;

            for (int i = 0; i < patients.Count && treated < capacity; i++)
            {
                PersonState person = patients[i];
                PersonHealthState health = PopulationHealthEvaluator.EnsureHealthInitialized(person);
                if (health == null || !health.HasActiveCondition)
                {
                    continue;
                }

                HouseholdState household = population.GetHousehold(person.householdId);
                if (household == null || household.spendingMoneyCents < visitPriceCents)
                {
                    continue;
                }

                int treatmentDays = 1;
                int totalCharge = visitPriceCents;
                bool usedRemedy = false;
                if (health.conditionKind == HealthConditionKind.Illness
                    && household.spendingMoneyCents >= visitPriceCents + remedyPriceCents
                    && runtime.TryConsumeCategoryStockUnits(CategoryMedicineRemedies, 1, out int consumedRemedies)
                    && consumedRemedies > 0)
                {
                    usedRemedy = true;
                    remediesUsed += consumedRemedies;
                    treatmentDays++;
                    totalCharge += remedyPriceCents;
                }

                household.spendingMoneyCents = Mathf.Max(0, household.spendingMoneyCents - totalCharge);
                household.lastDailyMedicalSpendCents += totalCharge;
                household.lifetimeMedicalSpendCents += totalCharge;
                runtime.RecordDailyServiceRevenue(CategoryMedicalService, 1, visitPriceCents);
                if (usedRemedy)
                {
                    runtime.RecordDailyServiceRevenue(CategoryMedicineRemedies, 1, remedyPriceCents);
                }

                string treatmentSummary = usedRemedy
                    ? "Doctor visit with remedy stock."
                    : "Doctor visit.";
                health.ApplyTreatment(Mathf.Max(0, absoluteDayIndex), treatmentDays, treatmentSummary);
                treated++;
                medicalSpend += totalCharge;
                doctorRevenue += totalCharge;
            }

            doctor.ApplyDailyServiceResult(treated);
            string blocked = treated < snapshot.activeCases ? "unmet patient demand" : string.Empty;
            string summary = $"treated {treated}/{snapshot.activeCases} patients today";
            if (remediesUsed > 0)
            {
                summary += $", used {remediesUsed} remedies";
            }

            runtime.RecordWeeklyOperation(0, treated, summary, blocked);
            RecordOperationSummary(doctor, string.IsNullOrWhiteSpace(blocked) ? summary : $"{summary}; blocked: {blocked}");
            snapshot.treatedCases = treated;
            snapshot.untreatedCases = Mathf.Max(0, snapshot.activeCases - treated);
            snapshot.medicalSpendCents = medicalSpend;
            snapshot.doctorRevenueCents = doctorRevenue;
            snapshot.summary = summary;
            return snapshot;
        }

        public bool TryEvaluateBestHouseholdReserveSellerScore(
            string reserveCategoryId,
            IReadOnlyList<string> sellerCategoryIds,
            int requestedUnits,
            int remainingBudgetCents,
            out BusinessReputationSellerChoiceScore score)
        {
            return TryEvaluateBestHouseholdReserveSellerScore(
                reserveCategoryId,
                sellerCategoryIds,
                requestedUnits,
                remainingBudgetCents,
                -1,
                out score);
        }

        public bool TryEvaluateBestHouseholdReserveSellerScore(
            string reserveCategoryId,
            IReadOnlyList<string> sellerCategoryIds,
            int requestedUnits,
            int remainingBudgetCents,
            int householdId,
            out BusinessReputationSellerChoiceScore score)
        {
            AutoWire();
            InitializeIfNeeded(generalStoreRuntime != null ? generalStoreRuntime.CurrentBusiness : null);

            List<HouseholdReserveSellerCandidate> candidates = BuildHouseholdReserveSellerCandidates(
                reserveCategoryId,
                sellerCategoryIds,
                requestedUnits,
                remainingBudgetCents,
                householdId);
            if (candidates.Count <= 0)
            {
                score = default;
                return false;
            }

            score = candidates[0].choiceScore;
            return score.Eligible;
        }

        public bool TryTransferBusinessToPlayer(
            int buildingId,
            out BusinessInstanceState business,
            out string message,
            bool configureCashTransferDefaults = true)
        {
            InitializeIfNeeded();
            business = FindByBuildingId(buildingId);
            if (business == null)
            {
                message = $"No shared business runtime is assigned to building {buildingId:000}.";
                return false;
            }

            BusinessOwnerIdentity formerOwner = business.Owner;
            if (formerOwner != null && formerOwner.OwnerKind != BusinessOwnerKind.Player)
            {
                business.EnsureOwnerOperatorStaffing(formerOwner);
            }

            business.TransferToPlayerOwnership();
            business.ResolveWeeklyBaselineThroughput();
            business.ResolveDailyBaselineService();
            int weekKey = GetCurrentWeekKey();
            if (configureCashTransferDefaults)
            {
                playerPortfolio?.ConfigureDefaultBusinessCashTransfers(
                    business,
                    GetSharedOperatingCashBufferCents(business),
                    business.RuntimeState.LastWeeklyReorderBudgetCents,
                    weekKey);
                playerPortfolio?.RegisterCurrentBusinessCashCheckpoint(business, weekKey, true);
            }

            message = $"{business.RuntimeDisplayName} is now player-owned.";
            status = message;
            return true;
        }

        public bool TryTransferBusinessToTown(int buildingId, string receiverDisplayName, out BusinessInstanceState business, out string message)
        {
            InitializeIfNeeded();
            business = FindByBuildingId(buildingId);
            if (business == null)
            {
                message = $"No shared business runtime is assigned to building {buildingId:000}.";
                return false;
            }

            string receiver = string.IsNullOrWhiteSpace(receiverDisplayName) ? "Town" : receiverDisplayName.Trim();
            playerPortfolio?.RemoveDistributionCheckpoint(business.InstanceId);
            business.SetOwner(BusinessOwnerIdentity.Town(receiver));
            business.ResolveWeeklyBaselineThroughput();
            business.ResolveDailyBaselineService();
            message = $"{business.RuntimeDisplayName} transferred to {receiver}.";
            status = message;
            return true;
        }

        public bool TryStartPlayerBusinessAtShell(BusinessType businessType, int buildingId, out BusinessInstanceState business, out string message)
        {
            AutoWire();
            EnsureProfilesLoaded();
            business = null;

            if (businessType == BusinessType.GeneralStore)
            {
                message = "General Store activation is handled by the General Store system.";
                return false;
            }

            if (buildingId < 0)
            {
                message = "No building shell is selected.";
                return false;
            }

            if (FindByBuildingId(buildingId) != null || assignedBuildingIds.Contains(buildingId))
            {
                message = $"Building {buildingId:000} already has a business runtime.";
                return false;
            }

            BusinessProfileDefinition profile = FindProfile(businessType);
            if (profile == null)
            {
                message = $"No business profile found for {BusinessRuntimeNaming.GetBusinessTypeDisplayName(businessType)}.";
                return false;
            }

            if (townWorld == null || buildingId >= townWorld.Buildings.Count)
            {
                message = $"Building {buildingId:000} is not part of the generated town.";
                return false;
            }

            PlacedBuilding building = townWorld.Buildings[buildingId];
            if (building == null || !building.playerOwned)
            {
                message = $"Building {buildingId:000} is not a player-owned shell.";
                return false;
            }

            TownPlot plot = GetPlot(building != null ? building.plotId : -1);
            if (!BusinessSiteSuitabilityEvaluator.TryEvaluatePlayerOwnedDevelopmentFit(profile, building, plot, out PlayerDevelopmentFitResult fit))
            {
                message = fit != null ? fit.reason : $"{BusinessRuntimeNaming.GetBusinessTypeDisplayName(businessType)} cannot start at this shell.";
                return false;
            }

            string instanceId = $"player_{profile.Business.BusinessId}_{buildingId:000}";
            business = BusinessInstanceState.Create(instanceId, profile, buildingId, BusinessOwnerIdentity.Player());
            if (!TryEnsureActivationRequiredStaff(business, out string staffingMessage))
            {
                message = staffingMessage;
                status = message;
                business = null;
                return false;
            }

            if (business.BusinessType == BusinessType.Mine)
            {
                InitializeMineBusinessState(business, buildingId);
            }

            business.ResolveWeeklyBaselineThroughput();
            business.ResolveDailyBaselineService();
            businesses.Add(business);
            assignedBuildingIds.Add(buildingId);
            int weekKey = GetCurrentWeekKey();
            playerPortfolio?.ConfigureDefaultBusinessCashTransfers(
                business,
                GetSharedOperatingCashBufferCents(business),
                business.RuntimeState.LastWeeklyReorderBudgetCents,
                weekKey);
            playerPortfolio?.RegisterCurrentBusinessCashCheckpoint(business, weekKey, true);
            string staffingSegment = !string.IsNullOrWhiteSpace(staffingMessage)
                ? $" {staffingMessage}"
                : string.Empty;
            message = fit != null && fit.HasWarnings
                ? $"{business.RuntimeDisplayName} opened at building {buildingId:000}. Fit warnings: {fit.WarningSummary}.{staffingSegment}"
                : $"{business.RuntimeDisplayName} opened at building {buildingId:000}.{staffingSegment}";
            status = message;
            return true;
        }

        public bool TryEnsureActivationRequiredStaff(BusinessInstanceState business, out string message)
        {
            AutoWire();
            message = string.Empty;
            if (business == null || business.RuntimeState == null)
            {
                message = "Business shell exists, but no business runtime is available.";
                return false;
            }

            BusinessRuntimeState runtime = business.RuntimeState;
            if (runtime.RequiredWorkerCount <= 0 || runtime.ActiveRequiredWorkerCount >= runtime.RequiredWorkerCount)
            {
                return true;
            }

            int assignedCount = 0;
            string lastAssignedWorker = string.Empty;
            string lastAssignedSlot = string.Empty;
            for (int i = 0; i < runtime.WorkerSlots.Count; i++)
            {
                WorkerSlotState slot = runtime.WorkerSlots[i];
                if (slot == null || !slot.RequiredForOpening)
                {
                    continue;
                }

                if (slot.IsPaidActive)
                {
                    continue;
                }

                if (slot.IsFilled && !slot.SuspendedForMissedPayroll)
                {
                    slot.MarkPaidActive();
                    continue;
                }

                PersonState candidate = FindBestAvailableWorkerCandidate(business, slot);
                if (candidate == null)
                {
                    message = $"{business.RuntimeDisplayName} shell exists, but the business cannot open until a required worker is available for {slot.SlotDisplayName}.";
                    return false;
                }

                AssignPersonToBusinessSlot(business, slot, candidate, 75);
                assignedCount++;
                lastAssignedWorker = candidate.DisplayName;
                lastAssignedSlot = slot.SlotDisplayName;
            }

            if (runtime.RequiredWorkerCount > 0 && runtime.ActiveRequiredWorkerCount < runtime.RequiredWorkerCount)
            {
                message = $"{business.RuntimeDisplayName} shell exists, but the business cannot open until all required worker slots are active.";
                return false;
            }

            business.ResolveWeeklyBaselineThroughput();
            business.ResolveDailyBaselineService();
            if (assignedCount > 0)
            {
                populationManager?.RecalculateHouseholdIncomeAndSummaries($"{lastAssignedWorker} hired at {business.RuntimeDisplayName}.");
                message = assignedCount == 1
                    ? $"Assigned {lastAssignedWorker} to {lastAssignedSlot}."
                    : $"Assigned {assignedCount} required workers.";
            }

            return true;
        }

        public string BuildManagementText(BusinessInstanceState business, int selectedWorkerIndex)
        {
            if (business == null || business.RuntimeState == null)
            {
                return "Business unavailable.";
            }

            BusinessRuntimeState runtime = business.RuntimeState;
            StringBuilder builder = new();
            builder.AppendLine(GetBusinessManagementHeading(business.BusinessType));
            string autoTransfer = business.CashTransferRule.AutoTransferEnabled ? "on" : "off";
            builder.AppendLine($"Owner: {GetOwnerDisplayName(business)} | Staff {runtime.FilledWorkerCount}/{runtime.TargetWorkerCount} | Efficiency {GetHealthAdjustedOperatingEfficiency01(business):P0}");
            builder.AppendLine($"Working capital: cash {FormatMoney(runtime.CurrentCashCents)} | protected {FormatMoney(GetLocalTradeAdjustedSurvivalCashReserveCents(business))} | payroll {FormatMoney(runtime.FilledWeeklyPayrollCents)} | input reserve {FormatMoney(runtime.LastWeeklyReorderBudgetCents)} | auto transfer {autoTransfer}");
            builder.AppendLine(ManagerPolicyEffects.BuildControlPolicyLine(business));
            builder.AppendLine(BuildFocusLine(business));
            builder.AppendLine(BuildCapacitySummaryLine(business));
            if (business.BusinessType == BusinessType.BoardingHouse)
            {
                builder.AppendLine(BuildBoardingHouseOccupancyLine(business));
            }

            string townContext = BuildTownContextLine(business);
            if (!string.IsNullOrWhiteSpace(townContext))
            {
                builder.AppendLine(townContext);
            }

            builder.AppendLine(BuildOperatingLine(business));
            builder.AppendLine($"Last operation: {GetLastOperationSummary(business)}");
            AppendWeeklySettlementLines(builder, business);
            string supportingTrade = BuildLocalTradeCashSupportLine(business);
            if (!string.IsNullOrWhiteSpace(supportingTrade))
            {
                builder.AppendLine(supportingTrade);
            }

            AppendMineManagementLines(builder, business);

            for (int i = 0; i < runtime.WorkerSlots.Count; i++)
            {
                WorkerSlotState slot = runtime.WorkerSlots[i];
                string worker = slot.IsFilled ? slot.AssignedWorkerDisplayName : "Open";
                string state = slot.IsFilled
                    ? slot.SuspendedForMissedPayroll ? "missed payroll" : slot.IsPaidActive ? "active" : "inactive"
                    : "open";
                string apprenticeship = ApprenticeshipProgressionEvaluator.BuildAssignedWorkerProgressSegment(
                    FindPersonForWorkerSlot(slot),
                    business.BusinessType);
                builder.AppendLine($"{slot.SlotDisplayName}: {worker} | {FormatMoney(slot.WeeklyWageCents)}/wk | {state}{apprenticeship}");
            }

            AppendWorkerSelection(builder, business, selectedWorkerIndex);
            return builder.ToString();
        }

        public string BuildBusinessInventoryText(BusinessInstanceState business)
        {
            return BuildBusinessOperatingReadoutText(business);
        }

        public string BuildBusinessPricingMarginText(BusinessInstanceState business)
        {
            if (business == null || business.RuntimeState == null)
            {
                return "<b>Pricing & Margin</b>\nPrice posture unavailable.";
            }

            BusinessProfileDefinition profile = FindProfile(business.BusinessType);
            string categoryId = GetPrimaryMarketCategoryId(business.BusinessType);
            int priceCents = GetAverageCategoryTransferPriceCents(business, profile, categoryId);
            int referenceCents = GetAverageCategoryLandedCostCents(profile, categoryId);
            float priceIndex01 = CalculateLocalPriceIndex01(priceCents, referenceCents);
            float capture01 = CalculateLocalMarketCapture01(business, categoryId, priceCents, referenceCents);
            float marginAdjustment01 = ManagerPolicyEffects.CalculateMarginAdjustment01(business.ControlState, business.ManagerPolicy);
            string posture = marginAdjustment01 > 0.001f
                ? "firmer than town baseline"
                : marginAdjustment01 < -0.001f
                    ? "softer than town baseline"
                    : "near town baseline";

            StringBuilder builder = new();
            builder.AppendLine("<b>Pricing & Margin</b>");
            builder.AppendLine($"Price Posture       {posture}");
            builder.AppendLine($"Average Ticket      {FormatMoney(priceCents)} | landed basis {FormatMoney(referenceCents)}");
            builder.AppendLine($"Local Capture Read  {capture01:P0} on-map / {(1f - capture01):P0} off-map or competitors");
            builder.Append($"Price Pressure      {priceIndex01:P0} of local tolerance; stock, reputation, convenience, and availability still decide the rest");
            return builder.ToString();
        }

        public string BuildBusinessLocalMarketCaptureLine(BusinessInstanceState business)
        {
            if (business == null || business.RuntimeState == null)
            {
                return string.Empty;
            }

            BusinessProfileDefinition profile = FindProfile(business.BusinessType);
            string categoryId = GetPrimaryMarketCategoryId(business.BusinessType);
            if (string.IsNullOrWhiteSpace(categoryId))
            {
                return string.Empty;
            }

            int priceCents = GetAverageCategoryTransferPriceCents(business, profile, categoryId);
            int referenceCents = GetAverageCategoryLandedCostCents(profile, categoryId);
            float capture01 = CalculateLocalMarketCapture01(business, categoryId, priceCents, referenceCents);
            return $"Market split: {capture01:P0} on-map capture / {(1f - capture01):P0} off-map or competitors at current price posture";
        }

        public bool TryAdjustBusinessMarginPolicy(BusinessInstanceState business, int stepDelta, out string message)
        {
            message = string.Empty;
            if (business == null)
            {
                message = "Business unavailable.";
                return false;
            }

            if (business.Owner == null || business.Owner.OwnerKind != BusinessOwnerKind.Player)
            {
                message = "Only player-owned businesses can change price posture.";
                return false;
            }

            ManagerPolicyPreset current = business.ManagerPolicy;
            ManagerPolicyPreset target = stepDelta > 0
                ? current == ManagerPolicyPreset.ReputationFirst ? ManagerPolicyPreset.StabilityFirst : ManagerPolicyPreset.ProfitFirst
                : current == ManagerPolicyPreset.ProfitFirst ? ManagerPolicyPreset.StabilityFirst : ManagerPolicyPreset.ReputationFirst;

            business.SetManagerPolicy(target);
            message = $"{business.RuntimeDisplayName} price posture set to {ManagerPolicyEffects.GetDisplayName(target)}. Local capture will adjust on the next market settlement.";
            return true;
        }

        public string BuildFocusLine(BusinessInstanceState business)
        {
            if (business == null)
            {
                return "Focus: unavailable";
            }

            BusinessMainFocusState focus = business.MainFocus;
            return $"Focus: {focus.DisplayName}";
        }

        public string GetLastOperationSummary(BusinessInstanceState business)
        {
            if (business == null || string.IsNullOrWhiteSpace(business.InstanceId))
            {
                return "not resolved yet";
            }

            if (lastOperationSummaries.TryGetValue(business.InstanceId, out string summary)
                && !string.IsNullOrWhiteSpace(summary))
            {
                return summary;
            }

            return business.RuntimeState != null && !string.IsNullOrWhiteSpace(business.RuntimeState.LastWeeklyOperationSummary)
                ? business.RuntimeState.LastWeeklyOperationSummary
                : "not resolved yet";
        }

        private static void AppendWeeklySettlementLines(StringBuilder builder, BusinessInstanceState business)
        {
            if (builder == null || business == null || business.RuntimeState == null)
            {
                return;
            }

            BusinessRuntimeState runtime = business.RuntimeState;
            int cashDelta = runtime.LastWeeklyCashAfterCents - runtime.LastWeeklyCashBeforeCents;
            builder.AppendLine($"Blocked: {GetWeeklyBlockedReason(runtime)}");
            builder.AppendLine($"Inputs: {runtime.LastWeeklyInputUnitsConsumed} consumed | procure {FormatMoney(runtime.LastWeeklyInputProcurementSpendCents)}");
            builder.AppendLine($"Output: {runtime.LastWeeklyOutputUnitsProduced} units");
            builder.AppendLine($"Cash: {FormatMoney(runtime.LastWeeklyCashBeforeCents)} -> {FormatMoney(runtime.LastWeeklyCashAfterCents)} | net {FormatSignedMoney(cashDelta)} | payroll {FormatMoney(runtime.LastWeeklyPayrollCents)} | owner draw {FormatMoney(runtime.LastWeeklyOwnerDistributionCents)} | transfers +{FormatMoney(runtime.LastWeeklyLocalTransferRevenueCents)}/-{FormatMoney(runtime.LastWeeklyLocalTransferCostCents)}");
            builder.AppendLine(BuildCashBridgeSummary(runtime));
            builder.AppendLine($"Transfers: {GetWeeklyTransferSummary(runtime)}");
        }

        private static string BuildCashBridgeSummary(BusinessRuntimeState runtime)
        {
            if (runtime == null)
            {
                return "Cash changed because: runtime unavailable.";
            }

            int stockBuys = Mathf.Max(0, runtime.LastWeeklyInputProcurementSpendCents)
                + Mathf.Max(0, runtime.LastWeeklyLocalTransferCostCents);
            int transferDelta = runtime.LastWeeklyCashTransferInCents - runtime.LastWeeklyCashTransferOutCents;
            return $"Cash changed because: sales {FormatMoney(runtime.WeekToDateRevenueCents + runtime.LastWeeklyLocalTransferRevenueCents)}, COGS $0.00, payroll {FormatMoney(runtime.LastWeeklyPayrollCents)}, stock buys {FormatMoney(stockBuys)}, owner draw {FormatMoney(runtime.LastWeeklyOwnerDistributionCents)}, transfers {FormatSignedMoney(transferDelta)}.";
        }

        private static string GetWeeklyBlockedReason(BusinessRuntimeState runtime)
        {
            return runtime != null && !string.IsNullOrWhiteSpace(runtime.LastWeeklyBlockedReason)
                ? runtime.LastWeeklyBlockedReason
                : "None";
        }

        private static string GetWeeklyTransferSummary(BusinessRuntimeState runtime)
        {
            return runtime != null && !string.IsNullOrWhiteSpace(runtime.LastWeeklyTransferSummary)
                ? runtime.LastWeeklyTransferSummary
                : "none";
        }

        public string BuildCapacitySummaryLine(BusinessInstanceState business)
        {
            BusinessCapacityState capacity = business != null ? business.Capacity : null;
            if (capacity == null)
            {
                return "Capacity: unavailable";
            }

            string bottleneck = string.IsNullOrWhiteSpace(capacity.BottleneckDisplayName) || string.Equals(capacity.BottleneckDisplayName, "none", StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : $" | Bottleneck: {capacity.BottleneckDisplayName}";

            if (business != null && business.BusinessType == BusinessType.BoardingHouse)
            {
                int usedBeds = GetBoardingHouseOccupiedBeds(business);
                int bedCapacity = GetBoardingHouseBedCapacity(business);
                return $"Capacity: beds {usedBeds}/{bedCapacity} | service {capacity.CurrentServiceVisitsPerDay}/{capacity.ServiceCapacityVisitsPerDay}/day{bottleneck}";
            }

            if (business != null && business.BusinessType == BusinessType.Doctor)
            {
                int activeCases = populationManager != null ? CountActiveHealthCases(populationManager.State) : 0;
                return $"Capacity: service {capacity.CurrentServiceVisitsPerDay}/{capacity.ServiceCapacityVisitsPerDay}/day | active cases {activeCases}{bottleneck}";
            }

            return $"Capacity: storage {capacity.CurrentStoredUnits}/{capacity.StorageCapacityUnits} | processing {capacity.CurrentProcessingUnitsPerWeek}/{capacity.ProcessingCapacityUnitsPerWeek}/wk | service {capacity.CurrentServiceVisitsPerDay}/{capacity.ServiceCapacityVisitsPerDay}/day{bottleneck}";
        }

        public bool IsWorkerFireMode(BusinessInstanceState business)
        {
            return GetOpenWorkerSlotCount(business) <= 0 && GetFilledWorkerSlotCount(business) > 0;
        }

        public bool TryCycleBusinessControlState(BusinessInstanceState business, out string message)
        {
            if (business == null)
            {
                message = "Business unavailable.";
                return false;
            }

            if (business.Owner == null || business.Owner.OwnerKind != BusinessOwnerKind.Player)
            {
                message = $"{business.RuntimeDisplayName} is not player-owned.";
                return false;
            }

            BusinessControlState state = business.CycleControlState();
            message = $"{business.RuntimeDisplayName} control set to {ManagerPolicyEffects.GetDisplayName(state)}.";
            status = message;
            return true;
        }

        public bool TryCycleManagerPolicy(BusinessInstanceState business, out string message)
        {
            if (business == null)
            {
                message = "Business unavailable.";
                return false;
            }

            if (business.Owner == null || business.Owner.OwnerKind != BusinessOwnerKind.Player)
            {
                message = $"{business.RuntimeDisplayName} is not player-owned.";
                return false;
            }

            ManagerPolicyPreset policy = business.CycleManagerPolicy();
            message = $"{business.RuntimeDisplayName} policy set to {ManagerPolicyEffects.GetDisplayName(policy)}.";
            status = message;
            return true;
        }

        public int GetWorkerSelectionCount(BusinessInstanceState business)
        {
            if (business == null || business.RuntimeState == null)
            {
                return 0;
            }

            return IsWorkerFireMode(business)
                ? GetFilledWorkerSlotCount(business)
                : BuildAvailableWorkerCandidateList(business).Count;
        }

        public bool TryAssignCandidateToOpenSlot(BusinessInstanceState business, int selectedCandidateIndex, out string message)
        {
            AutoWire();
            if (business == null || business.RuntimeState == null)
            {
                message = "Business unavailable.";
                return false;
            }

            if (business.Owner == null || business.Owner.OwnerKind != BusinessOwnerKind.Player)
            {
                message = $"{business.RuntimeDisplayName} is not player-owned.";
                return false;
            }

            int openSlotIndex = FindFirstOpenWorkerSlotIndex(business);
            if (openSlotIndex < 0)
            {
                // PKG-2 (TECH §4.3 / SD-05): the fixed slot list currently caps hiring. This is the
                // causal-slot behavior under de-causalization - staffing decisions should derive from
                // worker-time demand (LaborDemandPlan, P1), not from template slot counts. Behavior
                // preserved for now; PKG-7 decomposes slots into RoleDefinition + PositionState.
                message = $"No open {BusinessRuntimeNaming.GetBusinessTypeDisplayName(business.BusinessType)} worker slots are available.";
                return false;
            }

            List<PersonState> candidates = BuildAvailableWorkerCandidateList(business);
            if (candidates.Count == 0)
            {
                message = "No available town residents meet the first-pass hiring rules.";
                return false;
            }

            PersonState person = candidates[Mathf.Clamp(selectedCandidateIndex, 0, candidates.Count - 1)];
            WorkerSlotState slot = business.RuntimeState.WorkerSlots[openSlotIndex];
            int wageCents = Mathf.Max(0, slot.WeeklyWageCents);
            AssignPersonToBusinessSlot(business, slot, person, 70);
            business.ResolveWeeklyBaselineThroughput();
            business.ResolveDailyBaselineService();
            populationManager?.RecalculateHouseholdIncomeAndSummaries($"{person.DisplayName} hired at {business.RuntimeDisplayName}.");
            message = $"Assigned {person.DisplayName} to {slot.SlotDisplayName} at {business.RuntimeDisplayName} for {FormatMoney(wageCents)}/week.";
            status = message;
            return true;
        }

        private void AssignPersonToBusinessSlot(
            BusinessInstanceState business,
            WorkerSlotState slot,
            PersonState person,
            int stabilityBaselinePercent)
        {
            if (business == null || slot == null || person == null)
            {
                return;
            }

            int wageCents = Mathf.Max(0, slot.WeeklyWageCents);
            person.workplaceBuildingId = business.AssignedBuildingId;
            person.professionId = $"{GetProfessionPrefix(business.BusinessType)}_{slot.SlotId}";
            person.professionName = slot.SlotDisplayName;
            person.wage = new WageSnapshot
            {
                weeklyWage = Mathf.CeilToInt(wageCents / 100f),
                stabilityPercent = NewcomerSettlementEvaluator.ResolveEmploymentStabilityPercent(person, stabilityBaselinePercent),
                currencyId = "dollars"
            };
            person.RecordVisibleWorkerRole(slot.SlotDisplayName, wageCents);
            ApprenticeshipProgressionEvaluator.RecordAssignment(person, business.BusinessType, business.AssignedBuildingId, slot);
            slot.Assign(person.id.ToString(), person.DisplayName, wageCents);
        }

        public bool TryFireWorkerFromSlot(BusinessInstanceState business, int selectedFilledSlotIndex, out string message)
        {
            AutoWire();
            if (business == null || business.RuntimeState == null)
            {
                message = "Business unavailable.";
                return false;
            }

            if (business.Owner == null || business.Owner.OwnerKind != BusinessOwnerKind.Player)
            {
                message = $"{business.RuntimeDisplayName} is not player-owned.";
                return false;
            }

            int slotIndex = FindFilledWorkerSlotIndexBySelection(business, selectedFilledSlotIndex);
            if (slotIndex < 0)
            {
                message = $"No hired {BusinessRuntimeNaming.GetBusinessTypeDisplayName(business.BusinessType)} workers are available to fire.";
                return false;
            }

            WorkerSlotState slot = business.RuntimeState.WorkerSlots[slotIndex];
            string workerId = slot.AssignedWorkerId;
            string workerName = slot.AssignedWorkerDisplayName;
            string slotName = slot.SlotDisplayName;
            slot.Unassign();

            if (int.TryParse(workerId, out int personId) && populationManager != null && populationManager.State != null)
            {
                PersonState person = populationManager.State.GetPerson(personId);
                if (person != null && person.workplaceBuildingId == business.AssignedBuildingId)
                {
                    ResetPersonEmployment(person);
                }
            }

            business.ResolveWeeklyBaselineThroughput();
            business.ResolveDailyBaselineService();
            message = $"Fired {workerName} from {slotName} at {business.RuntimeDisplayName}.";
            populationManager?.RecalculateHouseholdIncomeAndSummaries(message);
            status = message;
            return true;
        }

        public BusinessInstanceState FindByBuildingId(int buildingId)
        {
            if (buildingId < 0)
            {
                return null;
            }

            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState business = businesses[i];
                if (business != null && business.AssignedBuildingId == buildingId)
                {
                    return business;
                }
            }

            return null;
        }

        public bool TryFindByBuildingId(int buildingId, out BusinessInstanceState business)
        {
            business = FindByBuildingId(buildingId);
            return business != null;
        }

        public bool IsBusinessAssignmentValid(BusinessInstanceState business)
        {
            if (business == null || townWorld == null)
            {
                return false;
            }

            BusinessProfileDefinition profile = FindProfile(business.BusinessType);
            if (profile == null)
            {
                return false;
            }

            if (business.AssignedBuildingId < 0 || business.AssignedBuildingId >= townWorld.Buildings.Count)
            {
                return false;
            }

            PlacedBuilding building = townWorld.Buildings[business.AssignedBuildingId];
            return BusinessSiteSuitabilityEvaluator.CanOperate(profile, building, GetPlot(building != null ? building.plotId : -1));
        }

        private void ResetWeeklySettlementForType(BusinessType businessType)
        {
            BusinessProfileDefinition profile = FindProfile(businessType);
            if (profile == null)
            {
                return;
            }

            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState business = businesses[i];
                if (business == null || business.BusinessType != businessType || business.RuntimeState == null)
                {
                    continue;
                }

                business.RuntimeState.ResetWeekToDateSales();
                business.RuntimeState.BeginWeeklySettlementCadence(
                    profile.Business,
                    GetEffectiveReorderThreshold01(business, profile.Business),
                    false);
            }
        }

        private void ResolvePayrollForType(BusinessType businessType)
        {
            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState business = businesses[i];
                if (business == null || business.BusinessType != businessType || business.RuntimeState == null)
                {
                    continue;
                }

                business.RuntimeState.ResolveWeeklyPayroll();
                if (business.RuntimeState.LastSuspendedPayrollWorkerIds.Count > 0)
                {
                    business.RuntimeState.AppendWeeklyBlockedReason("missed payroll");
                    ReleaseSuspendedPayrollWorkersFromPopulation(business);
                }

                if (businessType == BusinessType.Blacksmith || businessType == BusinessType.Butcher)
                {
                    ApprenticeshipProgressionEvaluator.AdvancePaidWorkers(business, populationManager != null ? populationManager.State : null);
                }
                else if (businessType == BusinessType.Sawmill
                    || businessType == BusinessType.LumberYard
                    || businessType == BusinessType.BoardingHouse)
                {
                    ApprenticeshipProgressionEvaluator.AdvancePaidWorkers(business, populationManager != null ? populationManager.State : null);
                }

                int operatingReserve = GetSharedOperatingCashBufferCents(business);
                if (business.RuntimeState.CurrentCashCents < operatingReserve)
                {
                    business.RuntimeState.AppendWeeklyBlockedReason($"weak cash: below {FormatMoney(operatingReserve)} reserve");
                }
            }
        }

        private int ResolveSawmillOperationsForLinkedSupport(int weekKey, StringBuilder summary)
        {
            if (linkedSawmillSupportNode == null || !linkedSawmillSupportNode.RemoteProductionEnabled)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState business = businesses[i];
                if (business == null || business.BusinessType != BusinessType.Sawmill || business.RuntimeState == null)
                {
                    continue;
                }

                ResolveSawmillBusinessProduction(business, linkedSawmillSupportNode, weekKey, summary);
                count++;
            }

            return count;
        }

        private bool ResolveSawmillBusinessProduction(
            BusinessInstanceState business,
            ConstructionSupportNodeState sawmill,
            int weekKey,
            StringBuilder summary)
        {
            if (business == null || business.RuntimeState == null || sawmill == null || !sawmill.RemoteProductionEnabled)
            {
                return false;
            }

            if (sawmill.HasResolvedSawmillWeek(weekKey))
            {
                AppendSummarySegment(summary, $"{GetBusinessSummaryLabel(business)} sawmill already resolved");
                return sawmill.LastWeeklyLumberProducedUnits > 0;
            }

            BusinessRuntimeState runtime = business.RuntimeState;
            EnsureSawmillBusinessStock(runtime, sawmill);
            List<string> blockedReasons = new();
            SeedBlockedReasons(runtime, blockedReasons);
            AppendStaffingBlockedReasons(runtime, blockedReasons);

            bool sawyerActive = IsWorkerSlotActive(runtime, "sawyer");
            bool loggerActive = IsWorkerSlotActive(runtime, "logger");
            bool teamsterActive = IsWorkerSlotActive(runtime, "yard_teamster");
            bool foremanActive = IsWorkerSlotActive(runtime, "foreman");
            bool requiredStaffReady = runtime.RequiredWorkerCount <= 0 || runtime.ActiveRequiredWorkerCount >= runtime.RequiredWorkerCount;
            float crewEfficiency = requiredStaffReady
                ? GetHealthAdjustedOperatingEfficiency01(business)
                : 0f;
            float condition = runtime.Reliability01;
            float housingFactor = sawmill.HasWorkerCabins ? 1.05f : 0.92f;
            float loggingFactor = crewEfficiency * housingFactor * (loggerActive ? 1f : 0.72f) * (foremanActive ? 1.08f : 1f) * Mathf.Lerp(0.55f, 1f, condition);
            float sawFactor = crewEfficiency * housingFactor * (sawyerActive ? 1f : 0f) * (foremanActive ? 1.06f : 1f) * Mathf.Lerp(0.45f, 1f, condition);

            int cutCapacity = Mathf.FloorToInt(sawmill.WeeklyCutCapacityLogs * loggingFactor);
            int cutLogs = cutCapacity > 0 ? sawmill.CutStandingTimberToLogs(cutCapacity) : 0;

            if (sawmill.StandingTimberUnits <= 0 && cutLogs <= 0)
            {
                AddBlockedReason(blockedReasons, "standing timber depleted");
            }

            int requestedHaul = Mathf.Min(sawmill.LogStockUnits, Mathf.Max(0, sawmill.WeeklySawCapacityLogs));
            int hauledLogs = ResolveSawmillHauling(
                business,
                sawmill,
                requestedHaul,
                teamsterActive,
                blockedReasons,
                out string haulingMode,
                out int haulingCostCents);

            int sawCapacity = Mathf.FloorToInt(sawmill.WeeklySawCapacityLogs * sawFactor);
            bool maintenanceNeeded = hauledLogs > 0 && sawmill.HardwareMaintenanceUnitsPerWeek > 0;
            bool maintenanceOk = true;
            string maintenanceSource = string.Empty;
            if (maintenanceNeeded && !TryConsumeSawmillMaintenanceHardware(business, sawmill.HardwareMaintenanceUnitsPerWeek, out maintenanceSource, out string maintenanceBlock))
            {
                maintenanceOk = false;
                AddBlockedReason(blockedReasons, maintenanceBlock);
                runtime.AdjustReliability01(-0.10f);
                if (runtime.Reliability01 < 0.25f)
                {
                    sawCapacity = 0;
                    AddBlockedReason(blockedReasons, "mill condition below maintenance threshold");
                }
                else
                {
                    sawCapacity = Mathf.FloorToInt(sawCapacity * 0.5f);
                }
            }

            int logsByStorage = sawmill.LumberPerLog > 0 ? sawmill.LumberStorageRoomUnits / sawmill.LumberPerLog : 0;
            if (sawmill.LumberStorageRoomUnits <= 0)
            {
                AddBlockedReason(blockedReasons, "lumber storage pressure");
                runtime.AdjustReliability01(-0.05f);
            }

            if (sawmill.LogStockUnits <= 0 && cutLogs <= 0)
            {
                AddBlockedReason(blockedReasons, "no staged logs at the sawmill");
            }

            if (!requiredStaffReady)
            {
                AddBlockedReason(blockedReasons, "missing required sawyer");
            }

            int logsToProcess = Mathf.Min(sawmill.LogStockUnits, hauledLogs, sawCapacity, logsByStorage);
            int processedLogs = logsToProcess > 0
                ? sawmill.ProcessLogsToLumber(logsToProcess, out int producedLumber)
                : 0;
            int lumberProduced = processedLogs > 0 ? processedLogs * sawmill.LumberPerLog : 0;

            if (processedLogs > 0 && maintenanceOk)
            {
                runtime.AdjustReliability01(foremanActive ? 0.03f : 0.015f);
            }

            int slabsProduced = processedLogs * SawmillSlabsOffcutsPerLog;
            if (slabsProduced > 0)
            {
                runtime.AddCategoryStockUnits(CategorySlabsOffcuts, slabsProduced);
            }

            SyncSawmillBusinessStockFromSupport(runtime, sawmill);
            int slabsSold = SellLocalMarketOutput(business, CategorySlabsOffcuts, SawmillOffcutLocalOutletMaxUnits, summary);

            if (processedLogs <= 0 && hauledLogs <= 0 && sawmill.LogStockUnits > 0)
            {
                AddBlockedReason(blockedReasons, "hauling limited mill throughput");
            }

            string blocked = BuildBlockedReason(blockedReasons);
            string maintenance = !string.IsNullOrWhiteSpace(maintenanceSource)
                ? $"; maintenance {maintenanceSource}"
                : string.Empty;
            string result = $"cut {cutLogs} timber, hauled {hauledLogs} logs, processed {processedLogs} logs, produced {lumberProduced} lumber, {slabsProduced} slabs/offcuts";
            result += $"; stock {sawmill.CurrentStockUnits}/{sawmill.LumberStorageCapacityUnits} lumber, {sawmill.LogStockUnits} logs, {sawmill.StandingTimberUnits} standing timber";
            result += $"; crew {sawmill.SawmillCrewLabel}; condition {runtime.Reliability01:P0}; hauling {haulingMode}{maintenance}";
            if (slabsSold > 0)
            {
                result += $"; sold {slabsSold} slabs/offcuts";
            }

            if (!string.IsNullOrWhiteSpace(blocked))
            {
                result += $"; blocked: {blocked}";
            }

            runtime.RecordWeeklyOperation(cutLogs + processedLogs, lumberProduced + slabsProduced, result, blocked);
            business.ApplyWeeklyOperationResult(lumberProduced);
            business.ApplyDailyServiceResult(0);
            business.RefreshCapacityState();
            RecordOperationSummary(business, result);
            sawmill.RecordSawmillWeeklyResult(
                cutLogs,
                processedLogs,
                lumberProduced,
                slabsProduced,
                slabsSold,
                haulingMode,
                hauledLogs,
                haulingCostCents,
                result,
                blocked,
                weekKey);
            AppendSummarySegment(summary, $"{GetBusinessSummaryLabel(business)} sawmill {lumberProduced} lumber");
            return lumberProduced > 0;
        }

        private int ResolveSawmillHauling(
            BusinessInstanceState business,
            ConstructionSupportNodeState sawmill,
            int requestedLogs,
            bool teamsterActive,
            List<string> blockedReasons,
            out string haulingMode,
            out int haulingCostCents)
        {
            haulingCostCents = 0;
            int requested = Mathf.Max(0, requestedLogs);
            if (requested <= 0)
            {
                haulingMode = "idle";
                return 0;
            }

            if (sawmill.InternalHaulingOwned)
            {
                float factor = teamsterActive ? 1f : 0.75f;
                haulingMode = teamsterActive ? "owned/internal hauling" : "owned hauling, thin yard crew";
                return Mathf.Min(requested, Mathf.Max(1, Mathf.FloorToInt(requested * factor)));
            }

            if (!sawmill.HiredHaulingFallbackAllowed)
            {
                haulingMode = "no hauling";
                AddBlockedReason(blockedReasons, "no internal hauling and no hired fallback");
                return 0;
            }

            int planned = Mathf.Min(requested, Mathf.Max(1, Mathf.FloorToInt(requested * 0.55f)));
            int costPerLog = sawmill.HiredHaulingCostPerLogCents;
            int reserve = GetSharedOperatingCashBufferCents(business);
            int spendable = Mathf.Max(0, business.RuntimeState.CurrentCashCents - reserve);
            int affordable = costPerLog > 0 ? spendable / costPerLog : planned;
            int hauled = Mathf.Min(planned, affordable);
            haulingMode = "hired hauling fallback";
            if (hauled < planned)
            {
                AddBlockedReason(blockedReasons, "cash-limited hired hauling");
            }

            haulingCostCents = hauled * costPerLog;
            if (haulingCostCents > 0)
            {
                business.RuntimeState.AddWeeklyLocalTransferCost(
                    haulingCostCents,
                    $"hired hauling moved {hauled} sawmill logs for {FormatMoney(haulingCostCents)}");
                business.RuntimeState.AdjustReliability01(-0.03f);
            }

            return hauled;
        }

        private bool TryConsumeSawmillMaintenanceHardware(BusinessInstanceState sawmillBusiness, int requiredUnits, out string sourceLabel, out string blockedReason)
        {
            sourceLabel = string.Empty;
            blockedReason = string.Empty;
            int requested = Mathf.Max(0, requiredUnits);
            if (requested <= 0)
            {
                sourceLabel = "no hardware maintenance required";
                return true;
            }

            BusinessInstanceState blacksmith = FindByType(BusinessType.Blacksmith);
            bool activeBlacksmith = blacksmith != null
                && blacksmith.RuntimeState != null
                && GetHealthAdjustedOperatingEfficiency01(blacksmith) > 0f;
            CategoryStockState stock = activeBlacksmith
                ? blacksmith.RuntimeState.GetCategoryStock(CategoryHardware)
                : null;
            int localUnits = Mathf.Min(requested, Mathf.Max(0, stock != null ? stock.CurrentStockUnits : 0));
            int fallbackUnits = Mathf.Max(0, requested - localUnits);
            BusinessProfileDefinition blacksmithProfile = activeBlacksmith ? FindProfile(BusinessType.Blacksmith) : null;
            int localUnitPriceCents = activeBlacksmith
                ? GetAverageCategoryTransferPriceCents(blacksmith, blacksmithProfile, CategoryHardware)
                : DefaultHardwareFallbackUnitCostCents;
            int fallbackUnitPriceCents = Mathf.Max(
                DefaultHardwareFallbackUnitCostCents,
                Mathf.CeilToInt(Mathf.Max(0, localUnitPriceCents) * RegionalHardwareFallbackUnitCostMultiplier));
            int fallbackCostCents = fallbackUnits * Mathf.Max(0, fallbackUnitPriceCents);
            if (fallbackCostCents > 0)
            {
                if (sawmillBusiness == null || sawmillBusiness.RuntimeState == null)
                {
                    blockedReason = $"missing {requested} nails/simple hardware for sawmill maintenance; {RegionalHardwareFallbackSourceLabel} requires a paying sawmill account";
                    return false;
                }

                int spendable = Mathf.Max(0, sawmillBusiness.RuntimeState.CurrentCashCents - GetSharedOperatingCashBufferCents(sawmillBusiness));
                if (spendable < fallbackCostCents)
                {
                    blockedReason = $"missing {requested} nails/simple hardware for sawmill maintenance; {RegionalHardwareFallbackSourceLabel} costs {FormatMoney(fallbackCostCents)}";
                    return false;
                }
            }

            int consumed = 0;
            if (localUnits > 0)
            {
                if (!blacksmith.RuntimeState.TryConsumeCategoryStockUnits(CategoryHardware, localUnits, out consumed)
                    || consumed < localUnits)
                {
                    blockedReason = $"missing {requested} nails/simple hardware for sawmill maintenance";
                    return false;
                }
            }

            int revenue = consumed * Mathf.Max(0, localUnitPriceCents);
            if (revenue > 0)
            {
                blacksmith.RuntimeState.AddWeeklyLocalTransferRevenue(
                    revenue,
                    $"sold {consumed} Nails / Simple Hardware to sawmill maintenance for {FormatMoney(revenue)}");
                AppendTransferToOperationSummary(blacksmith, $"; sold {consumed} hardware to Small Sawmill for {FormatMoney(revenue)}");
                sawmillBusiness?.RuntimeState?.AddWeeklyLocalTransferCost(
                    revenue,
                    $"bought {consumed} hardware maintenance from {GetBusinessSummaryLabel(blacksmith)} for {FormatMoney(revenue)}");
            }

            if (fallbackCostCents > 0)
            {
                sawmillBusiness.RuntimeState.AddWeeklyLocalTransferCost(
                    fallbackCostCents,
                    $"bought {fallbackUnits} hardware maintenance through {RegionalHardwareFallbackSourceLabel} for {FormatMoney(fallbackCostCents)}");
            }

            if (blacksmith != null)
            {
                blacksmith.RefreshCapacityState();
            }

            if (sawmillBusiness != null)
            {
                sawmillBusiness.RefreshCapacityState();
            }

            if (consumed > 0 && fallbackUnits > 0)
            {
                sourceLabel = $"{blacksmith.RuntimeDisplayName} + {RegionalHardwareFallbackSourceLabel}";
            }
            else if (consumed > 0)
            {
                sourceLabel = blacksmith.RuntimeDisplayName;
            }
            else
            {
                sourceLabel = RegionalHardwareFallbackSourceLabel;
            }

            return true;
        }

        private static void EnsureSawmillBusinessStock(BusinessRuntimeState runtime, ConstructionSupportNodeState sawmill)
        {
            if (runtime == null || sawmill == null)
            {
                return;
            }

            EnsureSawmillBusinessStockCategories(runtime, sawmill);
            HydrateSawmillBusinessStockFromSupportIfMissing(runtime, sawmill);
        }

        private static void EnsureSawmillBusinessStockCategories(BusinessRuntimeState runtime, ConstructionSupportNodeState sawmill)
        {
            if (runtime == null || sawmill == null)
            {
                return;
            }

            runtime.EnsureCategoryStock(CategoryStandingTimber, sawmill.StandingTimberUnits, ConstructionSupportNodeDefinition.DefaultSawmillStandingTimberUnits);
            runtime.EnsureCategoryStock(CategorySawmillLogs, sawmill.LogStockUnits, 24);
            runtime.EnsureCategoryStock(CategoryLumber, sawmill.CurrentStockUnits, sawmill.LumberStorageCapacityUnits);
            runtime.EnsureCategoryStock(CategorySlabsOffcuts, 0, 40);
        }

        private static void SyncSawmillBusinessStockFromSupport(BusinessRuntimeState runtime, ConstructionSupportNodeState sawmill)
        {
            if (runtime == null || sawmill == null)
            {
                return;
            }

            runtime.SetCategoryStockUnits(CategoryStandingTimber, sawmill.StandingTimberUnits);
            runtime.SetCategoryStockUnits(CategorySawmillLogs, sawmill.LogStockUnits);
            runtime.SetCategoryStockUnits(CategoryLumber, sawmill.CurrentStockUnits);
        }

        private static void HydrateSawmillBusinessStockFromSupportIfMissing(BusinessRuntimeState runtime, ConstructionSupportNodeState sawmill)
        {
            if (runtime == null || sawmill == null)
            {
                return;
            }

            bool runtimeHasTrackedInventory =
                GetCategoryStockUnits(runtime, CategoryStandingTimber) > 0
                || GetCategoryStockUnits(runtime, CategorySawmillLogs) > 0
                || GetCategoryStockUnits(runtime, CategoryLumber) > 0
                || GetCategoryStockUnits(runtime, CategorySlabsOffcuts) > 0;
            bool supportHasTrackedInventory =
                sawmill.StandingTimberUnits > 0
                || sawmill.LogStockUnits > 0
                || sawmill.CurrentStockUnits > 0;
            if (runtimeHasTrackedInventory || !supportHasTrackedInventory)
            {
                return;
            }

            // Shared business stock is authoritative during active play. Only hydrate from the
            // construction support mirror when the business runtime has not been seeded yet,
            // such as older saves or fallback-only sawmill states.
            SyncSawmillBusinessStockFromSupport(runtime, sawmill);
        }

        private void SyncLinkedSawmillBusinessFromSupport()
        {
            if (linkedSawmillSupportNode == null)
            {
                return;
            }

            BusinessInstanceState sawmill = FindByType(BusinessType.Sawmill);
            if (sawmill == null || sawmill.RuntimeState == null)
            {
                return;
            }

            EnsureSawmillBusinessStock(sawmill.RuntimeState, linkedSawmillSupportNode);
            sawmill.RefreshCapacityState();
        }

        private void SyncLinkedSawmillSupportFromBusiness(BusinessInstanceState sawmill)
        {
            if (linkedSawmillSupportNode == null
                || sawmill == null
                || sawmill.BusinessType != BusinessType.Sawmill
                || sawmill.RuntimeState == null)
            {
                return;
            }

            BusinessRuntimeState runtime = sawmill.RuntimeState;
            // In this slice the shared business runtime owns live sawmill inventory. The linked
            // construction support node remains an explicit mirror so construction quotes,
            // consumption, and older save flows continue to work without a parallel stock model.
            linkedSawmillSupportNode.SetSawmillInventoryFromBusinessRuntime(
                GetCategoryStockUnits(runtime, CategoryStandingTimber),
                GetCategoryStockUnits(runtime, CategorySawmillLogs),
                GetCategoryStockUnits(runtime, CategoryLumber));
        }

        private static bool IsWorkerSlotActive(BusinessRuntimeState runtime, string slotId)
        {
            if (runtime == null || string.IsNullOrWhiteSpace(slotId))
            {
                return false;
            }

            for (int i = 0; i < runtime.WorkerSlots.Count; i++)
            {
                WorkerSlotState slot = runtime.WorkerSlots[i];
                if (slot != null
                    && slot.IsPaidActive
                    && string.Equals(slot.SlotId, slotId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private int ResolveOperationsForType(BusinessType businessType, StringBuilder summary)
        {
            int count = 0;
            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState business = businesses[i];
                if (business == null || business.BusinessType != businessType || business.RuntimeState == null)
                {
                    continue;
                }

                ResolveBusinessOperation(business, summary);
                count++;
            }

            return count;
        }

        private int ResolveMineOperations(StringBuilder summary)
        {
            int count = 0;
            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState business = businesses[i];
                if (business == null || business.BusinessType != BusinessType.Mine || business.RuntimeState == null)
                {
                    continue;
                }

                ResolveMineOperation(business, summary);
                count++;
            }

            return count;
        }

        private void ResolveMineOperation(BusinessInstanceState business, StringBuilder summary)
        {
            MineRuntimeState mine = business != null ? business.MineState : null;
            BusinessRuntimeState runtime = business != null ? business.RuntimeState : null;
            BusinessProfileDefinition profile = FindProfile(BusinessType.Mine);
            if (mine == null || runtime == null || profile == null)
            {
                return;
            }

            string outputCategoryId = string.IsNullOrWhiteSpace(mine.StockpileCategoryId)
                ? MineRuntimeState.GetStockpileCategoryId(mine.MineralKind)
                : mine.StockpileCategoryId;
            runtime.EnsureCategoryStock(outputCategoryId, 0, Mathf.Max(24, runtime.GetCategoryStock(outputCategoryId)?.TargetStockUnits ?? 0));
            List<string> blockedReasons = new();
            SeedBlockedReasons(runtime, blockedReasons);
            AppendStaffingBlockedReasons(runtime, blockedReasons);

            int producedUnits = 0;
            int soldUnits = 0;
            int soldRevenueCents = 0;
            float resolvedSafetyRisk01 = Mathf.Clamp01(mine.SafetyRisk01 + GetMineSafetyStageModifier(mine.DevelopmentStage));
            float resolvedRichness01 = mine.RemainingRichness01;
            float resolvedCampPressure01 = mine.CampPressure01;

            bool hasRequiredCrew = runtime.RequiredWorkerCount <= 0 || runtime.ActiveRequiredWorkerCount >= runtime.RequiredWorkerCount;
            if (!hasRequiredCrew)
            {
                AddBlockedReason(blockedReasons, "required mine crew inactive");
            }

            int storageRoom = GetAvailableCategoryCapacity(runtime, outputCategoryId);
            if (storageRoom <= 0)
            {
                AddBlockedReason(blockedReasons, $"output storage full: {outputCategoryId}");
            }

            if (hasRequiredCrew && storageRoom > 0)
            {
                float efficiency01 = GetHealthAdjustedOperatingEfficiency01(business);
                int plannedUnits = Mathf.RoundToInt(
                    GetMineBaseOutputUnits(mine.MineralKind)
                    * GetMineConfidenceMultiplier(mine.DepositConfidence01)
                    * GetMineStageMultiplier(mine.DevelopmentStage)
                    * Mathf.Lerp(0.55f, 1.15f, mine.RemainingRichness01)
                    * Mathf.Clamp(efficiency01, 0f, 1f));
                if (plannedUnits <= 0)
                {
                    AddBlockedReason(blockedReasons, "no staffed weekly capacity");
                }
                else
                {
                    producedUnits = runtime.AddCategoryStockUnits(outputCategoryId, Mathf.Min(plannedUnits, storageRoom));
                    if (producedUnits <= 0)
                    {
                        AddBlockedReason(blockedReasons, $"output storage full: {outputCategoryId}");
                    }
                    else
                    {
                        int maxSaleUnits = GetMineOffMapSaleUnits(mine.MineralKind);
                        if (runtime.TryConsumeCategoryStockUnits(outputCategoryId, Mathf.Min(maxSaleUnits, producedUnits), out soldUnits) && soldUnits > 0)
                        {
                            int unitPriceCents = GetAverageCategoryTransferPriceCents(business, profile, outputCategoryId);
                            soldRevenueCents = soldUnits * Mathf.Max(1, unitPriceCents);
                            runtime.AddWeeklyLocalTransferRevenue(
                                soldRevenueCents,
                                $"off-map {outputCategoryId} sale {soldUnits} units for {FormatMoney(soldRevenueCents)}");
                        }

                        resolvedRichness01 = Mathf.Clamp01(mine.RemainingRichness01 - producedUnits * GetMineDepletionPerUnit(mine.MineralKind));
                        resolvedSafetyRisk01 = Mathf.Clamp01(
                            mine.SafetyRisk01
                            + GetMineSafetyStageModifier(mine.DevelopmentStage)
                            + Mathf.Max(0f, 0.12f - efficiency01 * 0.08f));
                        resolvedCampPressure01 = Mathf.Clamp01(
                            mine.CampPressure01 * 0.72f
                            + Mathf.Clamp01(producedUnits / 18f) * (mine.MineralKind is MineralResourceKind.Gold or MineralResourceKind.Silver ? 0.36f : 0.22f));
                    }
                }
            }

            business.ApplyWeeklyOperationResult(producedUnits);
            business.ApplyDailyServiceResult(0);
            string blocked = BuildBlockedReason(blockedReasons);
            string result = $"{MineRuntimeState.GetMineralDisplayName(mine.MineralKind)} ore {producedUnits} units";
            if (soldUnits > 0)
            {
                result += $"; sold off-map {soldUnits} for {FormatMoney(soldRevenueCents)}";
            }

            result += $"; confidence {mine.DepositConfidence01:P0}; stage {MineRuntimeState.GetDevelopmentStageDisplayName(mine.DevelopmentStage)}; safety {resolvedSafetyRisk01:P0}; richness {resolvedRichness01:P0}";
            runtime.RecordWeeklyOperation(0, producedUnits, result, blocked);
            mine.RecordWeeklyOutput(
                producedUnits,
                string.IsNullOrWhiteSpace(blocked) ? result : $"{result}; blocked: {blocked}",
                resolvedSafetyRisk01,
                resolvedRichness01,
                resolvedCampPressure01);
            RecordOperationSummary(business, mine.LastWeeklyOutputSummary);
            AppendSummarySegment(summary, $"Mine {MineRuntimeState.GetMineralDisplayName(mine.MineralKind)} {producedUnits}");
        }

        private int ResolveBoardingHouseOperations(StringBuilder summary)
        {
            int count = 0;
            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState business = businesses[i];
                if (business == null || business.BusinessType != BusinessType.BoardingHouse || business.RuntimeState == null)
                {
                    continue;
                }

                BusinessRuntimeState runtime = business.RuntimeState;
                float efficiency01 = GetHealthAdjustedOperatingEfficiency01(business);
                int physicalBeds = GetBoardingHouseBedCapacity(business);
                int occupiedBeds = GetBoardingHouseOccupiedBeds(business);
                int serviceBeds = Mathf.Min(occupiedBeds, Mathf.RoundToInt(physicalBeds * efficiency01));
                List<string> blockedReasons = new();
                SeedBlockedReasons(runtime, blockedReasons);
                AppendStaffingBlockedReasons(runtime, blockedReasons);
                if (physicalBeds <= 0)
                {
                    blockedReasons.Add("no lodging beds configured");
                }

                if (occupiedBeds > serviceBeds && occupiedBeds > 0)
                {
                    blockedReasons.Add("staffing limits boarded-room service");
                }

                int stapleConsumed = ConsumeBoardingHouseSupply(runtime, CategoryStapleFood, serviceBeds, 4, blockedReasons, ref serviceBeds);
                int meatConsumed = ConsumeBoardingHouseSupply(runtime, CategoryMeat, serviceBeds, 6, blockedReasons, ref serviceBeds);
                int fuelConsumed = ConsumeBoardingHouseSupply(runtime, CategoryFuelWood, serviceBeds, 5, blockedReasons, ref serviceBeds);
                int revenue = serviceBeds * BoardingHouseWeeklyBoardCents;
                int upkeep = physicalBeds > 0
                    ? BoardingHouseFixedUpkeepCents + occupiedBeds * BoardingHouseVariableUpkeepCents
                    : 0;

                if (revenue > 0)
                {
                    runtime.AddWeeklyLocalTransferRevenue(
                        revenue,
                        $"lodged {serviceBeds} boarder(s) for {FormatMoney(revenue)}");
                }

                if (upkeep > 0)
                {
                    runtime.AddWeeklyLocalTransferCost(
                        upkeep,
                        $"paid lodging upkeep for {occupiedBeds}/{physicalBeds} occupied beds ({FormatMoney(upkeep)})");
                }

                business.ApplyWeeklyOperationResult(0);
                business.ApplyDailyServiceResult(serviceBeds);
                string blocked = BuildBlockedReason(blockedReasons);
                string result = $"lodging {occupiedBeds}/{physicalBeds} beds occupied; served {serviceBeds}; supplies {stapleConsumed} staple, {meatConsumed} meat, {fuelConsumed} fuel; revenue {FormatMoney(revenue)}; upkeep {FormatMoney(upkeep)}";
                runtime.RecordWeeklyOperation(0, serviceBeds, result, blocked);
                RecordOperationSummary(business, string.IsNullOrWhiteSpace(blocked) ? result : $"{result}; blocked: {blocked}");
                AppendSummarySegment(summary, $"BoardingHouse {occupiedBeds}/{physicalBeds} beds {FormatMoney(revenue)} upkeep {FormatMoney(upkeep)}");
                count++;
            }

            return count;
        }

        private static int ConsumeBoardingHouseSupply(
            BusinessRuntimeState runtime,
            string categoryId,
            int requestedBeds,
            int bedsPerUnit,
            List<string> blockedReasons,
            ref int serviceBeds)
        {
            if (runtime == null || string.IsNullOrWhiteSpace(categoryId) || requestedBeds <= 0)
            {
                return 0;
            }

            CategoryStockState stock = runtime.GetCategoryStock(categoryId);
            if (stock == null)
            {
                return 0;
            }

            int yield = Mathf.Max(1, bedsPerUnit);
            int needed = Mathf.CeilToInt(requestedBeds / (float)yield);
            if (needed <= 0)
            {
                return 0;
            }

            if (!runtime.TryConsumeCategoryStockUnits(categoryId, needed, out int consumed) || consumed <= 0)
            {
                AddBlockedReason(blockedReasons, $"input shortage: {categoryId}");
                serviceBeds = 0;
                return 0;
            }

            int supportedBeds = consumed * yield;
            if (supportedBeds < requestedBeds)
            {
                AddBlockedReason(blockedReasons, $"short input: {categoryId}");
                serviceBeds = Mathf.Min(serviceBeds, supportedBeds);
            }

            return consumed;
        }

        private void ResolveBusinessOperation(BusinessInstanceState business, StringBuilder summary)
        {
            if (business != null && business.BusinessType == BusinessType.Sawmill)
            {
                return;
            }

            BusinessProfileDefinition profile = FindProfile(business.BusinessType);
            if (profile == null || business.RuntimeState == null)
            {
                return;
            }

            BusinessRuntimeState runtime = business.RuntimeState;
            float operatingEfficiency01 = GetHealthAdjustedOperatingEfficiency01(business);
            int plannedUnits = Mathf.RoundToInt(Mathf.Max(0, profile.BaselineWeeklyThroughputUnits) * operatingEfficiency01);
            int consumedUnits = 0;
            int producedUnits = 0;
            string inputCategoryId = GetFirstCategoryId(profile.InputCategoryIds);
            string outputCategoryId = GetFirstCategoryId(profile.OutputCategoryIds);
            List<string> blockedReasons = new();
            SeedBlockedReasons(runtime, blockedReasons);
            AppendStaffingBlockedReasons(runtime, blockedReasons);

            if (business.ThroughputMode == BusinessThroughputMode.Service)
            {
                int serviceDays = GetWeeklyServiceOperatingDays(business.BusinessType);
                int dailyVisitCapacity = Mathf.RoundToInt(Mathf.Max(0, profile.BaselineDailyServiceCapacity) * operatingEfficiency01);
                int weeklyVisits = Mathf.Max(0, dailyVisitCapacity * serviceDays);
                int constrainedWeeklyVisits = weeklyVisits;
                int consumedServiceInputs = ConsumeServiceInputsForVisits(business, profile, ref constrainedWeeklyVisits, blockedReasons);
                int resolvedDailyVisits = serviceDays > 0 ? Mathf.CeilToInt(constrainedWeeklyVisits / (float)serviceDays) : constrainedWeeklyVisits;
                int serviceUnitPriceCents = GetServiceVisitPriceCents(business, profile);
                int serviceRevenueCents = Mathf.RoundToInt(constrainedWeeklyVisits * serviceUnitPriceCents * GetServiceRevenueMultiplier(business.BusinessType));
                if (constrainedWeeklyVisits > 0 && serviceRevenueCents > 0)
                {
                    runtime.AddWeeklyLocalTransferRevenue(
                        serviceRevenueCents,
                        $"served {constrainedWeeklyVisits} weekly visits for {FormatMoney(serviceRevenueCents)}");
                }

                business.ApplyWeeklyOperationResult(0);
                business.ApplyDailyServiceResult(resolvedDailyVisits);
                string serviceBlocked = BuildBlockedReason(blockedReasons);
                string serviceInputs = consumedServiceInputs > 0 ? $"; consumed {consumedServiceInputs} input units" : string.Empty;
                string serviceCash = serviceRevenueCents > 0 ? $"; revenue {FormatMoney(serviceRevenueCents)}" : "; no service revenue";
                string serviceSummary = $"{resolvedDailyVisits}/{profile.BaselineDailyServiceCapacity} visits/day, {constrainedWeeklyVisits}/{weeklyVisits} weekly visits{serviceInputs}{serviceCash}";
                runtime.RecordWeeklyOperation(consumedServiceInputs, constrainedWeeklyVisits, serviceSummary, serviceBlocked);
                RecordOperationSummary(business, string.IsNullOrWhiteSpace(serviceBlocked)
                    ? serviceSummary
                    : $"{serviceSummary}; blocked: {serviceBlocked}");
                AppendSummarySegment(summary, $"{BusinessRuntimeNaming.GetBusinessTypeDisplayName(business.BusinessType)} service {constrainedWeeklyVisits} {FormatMoney(serviceRevenueCents)}");
                return;
            }

            int outputUnitsPerInput = GetOutputUnitsPerInput(business.BusinessType);
            int procuredUnits = ProcureInputsForWeeklyOperation(business, profile, inputCategoryId, plannedUnits, outputUnitsPerInput, blockedReasons);
            int outputRoom = GetAvailableCategoryCapacity(runtime, outputCategoryId);
            int availableInputs = string.IsNullOrWhiteSpace(inputCategoryId)
                ? plannedUnits
                : GetCategoryStockUnits(runtime, inputCategoryId);
            int inputLimitedOutput = string.IsNullOrWhiteSpace(inputCategoryId)
                ? plannedUnits
                : availableInputs * outputUnitsPerInput;

            if (plannedUnits <= 0)
            {
                blockedReasons.Add("no staffed weekly capacity");
            }

            if (!string.IsNullOrWhiteSpace(outputCategoryId) && outputRoom <= 0)
            {
                blockedReasons.Add($"output storage full: {outputCategoryId}");
            }

            if (!string.IsNullOrWhiteSpace(inputCategoryId) && availableInputs <= 0)
            {
                blockedReasons.Add($"input shortage: {inputCategoryId}");
            }

            if (plannedUnits > 0 && !string.IsNullOrWhiteSpace(outputCategoryId) && outputRoom > 0)
            {
                int operableUnits = Mathf.Min(plannedUnits, outputRoom);
                if (!string.IsNullOrWhiteSpace(inputCategoryId))
                {
                    operableUnits = Mathf.Min(operableUnits, inputLimitedOutput);
                    int inputUnitsNeeded = Mathf.CeilToInt(operableUnits / (float)outputUnitsPerInput);
                    if (operableUnits > 0 && runtime.TryConsumeCategoryStockUnits(inputCategoryId, inputUnitsNeeded, out int consumed))
                    {
                        consumedUnits = consumed;
                        operableUnits = Mathf.Min(operableUnits, consumed * outputUnitsPerInput);
                    }
                    else
                    {
                        operableUnits = 0;
                    }
                }

                operableUnits = ConsumeSecondaryInputsForOutput(business, profile, inputCategoryId, operableUnits, blockedReasons);
                producedUnits = runtime.AddCategoryStockUnits(outputCategoryId, operableUnits);
                if (operableUnits > 0 && producedUnits <= 0)
                {
                    blockedReasons.Add($"output storage full: {outputCategoryId}");
                }
            }

            business.ApplyWeeklyOperationResult(producedUnits);
            business.ApplyDailyServiceResult(Mathf.RoundToInt(Mathf.Max(0, profile.BaselineDailyServiceCapacity) * operatingEfficiency01));

            string inputLabel = string.IsNullOrWhiteSpace(inputCategoryId) ? "no inputs" : $"{consumedUnits} {inputCategoryId}";
            string outputLabel = string.IsNullOrWhiteSpace(outputCategoryId) ? "no output" : $"{producedUnits} {outputCategoryId}";
            string blocked = BuildBlockedReason(blockedReasons);
            string procurement = procuredUnits > 0 ? $"; procured {procuredUnits} {inputCategoryId} for {FormatMoney(runtime.LastWeeklyInputProcurementSpendCents)}" : string.Empty;
            string result = $"{inputLabel} -> {outputLabel} ({plannedUnits} capacity){procurement}";
            runtime.RecordWeeklyOperation(consumedUnits, producedUnits, result, blocked);
            if (!string.IsNullOrWhiteSpace(blocked))
            {
                result += $"; blocked: {blocked}";
            }

            RecordOperationSummary(business, result);
            AppendSummarySegment(summary, $"{business.BusinessType} {producedUnits}/{plannedUnits}");
            if (business.BusinessType == BusinessType.CropFarm || business.BusinessType == BusinessType.Ranch)
            {
                townWorld?.SetAgriculturalActivityCue(business.AssignedBuildingId, producedUnits > 0, blocked);
            }
        }

        private int ConsumeServiceInputsForVisits(
            BusinessInstanceState business,
            BusinessProfileDefinition profile,
            ref int visits,
            List<string> blockedReasons)
        {
            if (business == null || business.RuntimeState == null || profile == null || visits <= 0)
            {
                return 0;
            }

            int totalConsumed = 0;
            ReadOnlySpan<string> inputCategoryIds = profile.InputCategoryIds;
            for (int i = 0; i < inputCategoryIds.Length; i++)
            {
                string categoryId = inputCategoryIds[i];
                if (string.IsNullOrWhiteSpace(categoryId))
                {
                    continue;
                }

                CategoryStockState stock = business.RuntimeState.GetCategoryStock(categoryId);
                if (stock == null)
                {
                    continue;
                }

                int visitsPerUnit = GetServiceVisitsPerInputUnit(business.BusinessType, categoryId);
                int neededUnits = Mathf.CeilToInt(visits / (float)visitsPerUnit);
                if (neededUnits <= 0)
                {
                    continue;
                }

                ProcureInputsForWeeklyOperation(business, profile, categoryId, visits, visitsPerUnit, blockedReasons);
                int availableVisits = stock.CurrentStockUnits * visitsPerUnit;
                if (availableVisits <= 0)
                {
                    AddBlockedReason(blockedReasons, $"input shortage: {categoryId}");
                    visits = 0;
                    continue;
                }

                if (availableVisits < visits)
                {
                    AddBlockedReason(blockedReasons, $"short input: {categoryId}");
                    visits = availableVisits;
                }

                int consumeUnits = Mathf.CeilToInt(visits / (float)visitsPerUnit);
                if (consumeUnits > 0 && business.RuntimeState.TryConsumeCategoryStockUnits(categoryId, consumeUnits, out int consumed))
                {
                    totalConsumed += consumed;
                }
            }

            return totalConsumed;
        }

        private static int GetWeeklyServiceOperatingDays(BusinessType businessType)
        {
            return businessType switch
            {
                BusinessType.Doctor => 6,
                BusinessType.Saloon => 6,
                BusinessType.BoardingHouse => 7,
                BusinessType.LiveryFreight => 6,
                BusinessType.Builder => 5,
                _ => 6
            };
        }

        private static float GetServiceRevenueMultiplier(BusinessType businessType)
        {
            return businessType switch
            {
                BusinessType.Barber => 1.2f,
                BusinessType.Saloon => 1.15f,
                BusinessType.Builder => 1.1f,
                BusinessType.LiveryFreight => 1.05f,
                _ => 1f
            };
        }

        private static int GetFallbackServiceVisitPriceCents(BusinessType businessType)
        {
            return businessType switch
            {
                BusinessType.Barber => 22,
                BusinessType.Saloon => 45,
                BusinessType.Builder => 95,
                BusinessType.LiveryFreight => 65,
                BusinessType.BoardingHouse => BoardingHouseWeeklyBoardCents,
                BusinessType.Doctor => 85,
                _ => 35
            };
        }

        private static int GetServiceVisitPriceCents(BusinessInstanceState business, BusinessProfileDefinition profile)
        {
            if (profile == null)
            {
                return GetFallbackServiceVisitPriceCents(business != null ? business.BusinessType : BusinessType.Barber);
            }

            string serviceCategoryId = GetFirstCategoryId(profile.ServiceCategoryIds);
            int price = !string.IsNullOrWhiteSpace(serviceCategoryId)
                ? GetAverageCategoryTransferPriceCents(business, profile, serviceCategoryId)
                : 0;
            return Mathf.Max(1, price > 1 ? price : GetFallbackServiceVisitPriceCents(business != null ? business.BusinessType : profile.Business.BusinessType));
        }

        private static int GetServiceVisitsPerInputUnit(BusinessType businessType, string categoryId)
        {
            if (businessType == BusinessType.Saloon)
            {
                return string.Equals(categoryId, CategoryFuelWood, StringComparison.OrdinalIgnoreCase) ? 8 : 2;
            }

            if (businessType == BusinessType.Barber)
            {
                return string.Equals(categoryId, CategoryFuelWood, StringComparison.OrdinalIgnoreCase) ? 10 : 6;
            }

            if (businessType == BusinessType.Doctor)
            {
                return 4;
            }

            return 6;
        }

        private int ConsumeSecondaryInputsForOutput(
            BusinessInstanceState business,
            BusinessProfileDefinition profile,
            string primaryInputCategoryId,
            int outputUnits,
            List<string> blockedReasons)
        {
            if (business == null || business.RuntimeState == null || profile == null || outputUnits <= 0)
            {
                return Mathf.Max(0, outputUnits);
            }

            int limitedOutputUnits = outputUnits;
            ReadOnlySpan<string> inputCategoryIds = profile.InputCategoryIds;
            for (int i = 0; i < inputCategoryIds.Length; i++)
            {
                string categoryId = inputCategoryIds[i];
                if (string.IsNullOrWhiteSpace(categoryId)
                    || string.Equals(categoryId, primaryInputCategoryId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                CategoryStockState stock = business.RuntimeState.GetCategoryStock(categoryId);
                if (stock == null)
                {
                    continue;
                }

                int outputPerInput = GetSecondaryOutputUnitsPerInputUnit(business.BusinessType, categoryId);
                int desiredInputUnits = Mathf.CeilToInt(limitedOutputUnits / (float)outputPerInput);
                if (desiredInputUnits <= 0)
                {
                    continue;
                }

                int supportedOutput = stock.CurrentStockUnits * outputPerInput;
                if (supportedOutput <= 0)
                {
                    AddBlockedReason(blockedReasons, $"input shortage: {categoryId}");
                    limitedOutputUnits = 0;
                    continue;
                }

                if (supportedOutput < limitedOutputUnits)
                {
                    AddBlockedReason(blockedReasons, $"short input: {categoryId}");
                    limitedOutputUnits = supportedOutput;
                }

                int consumeUnits = Mathf.CeilToInt(limitedOutputUnits / (float)outputPerInput);
                if (consumeUnits > 0)
                {
                    business.RuntimeState.TryConsumeCategoryStockUnits(categoryId, consumeUnits, out _);
                }
            }

            return Mathf.Max(0, limitedOutputUnits);
        }

        private static int GetSecondaryOutputUnitsPerInputUnit(BusinessType businessType, string categoryId)
        {
            if (businessType == BusinessType.Bakery && string.Equals(categoryId, CategoryFuelWood, StringComparison.OrdinalIgnoreCase))
            {
                return 8;
            }

            return 4;
        }

        private int ProcureInputsForWeeklyOperation(
            BusinessInstanceState business,
            BusinessProfileDefinition profile,
            string inputCategoryId,
            int plannedUnits,
            int outputUnitsPerInput,
            List<string> blockedReasons)
        {
            if (business == null
                || business.RuntimeState == null
                || profile == null
                || string.IsNullOrWhiteSpace(inputCategoryId)
                || plannedUnits <= 0)
            {
                return 0;
            }

            int inputYield = Mathf.Max(1, outputUnitsPerInput);
            CategoryStockState stock = business.RuntimeState.GetCategoryStock(inputCategoryId);
            if (stock == null)
            {
                blockedReasons?.Add($"missing input category: {inputCategoryId}");
                return 0;
            }

            int plannedInputUnits = Mathf.CeilToInt(plannedUnits / (float)inputYield);
            int productionShortfall = Mathf.Max(0, plannedInputUnits - stock.CurrentStockUnits);
            int desiredUnits = CalculateSurvivalInputProcurementUnits(stock, plannedInputUnits, productionShortfall);
            if (desiredUnits <= 0)
            {
                return 0;
            }

            int unitCost = GetAverageCategoryLandedCostCents(profile, inputCategoryId);
            if (unitCost <= 0)
            {
                blockedReasons?.Add($"input cost unavailable: {inputCategoryId}");
                return 0;
            }

            int reserve = GetSharedSpendingReserveCents(business);
            int spendableCash = Mathf.Max(0, business.RuntimeState.CurrentCashCents - reserve);
            int reorderBudget = ManagerPolicyEffects.CalculateReorderBudgetCents(
                profile.Business.Economy.WeeklyReorderReserveCents,
                business.ControlState,
                business.ManagerPolicy);
            int budget = Mathf.Min(reorderBudget, spendableCash);
            int affordableUnits = Mathf.Min(desiredUnits, budget / unitCost);
            if (affordableUnits <= 0)
            {
                blockedReasons?.Add($"cash-limited input procurement: {inputCategoryId} (reserve {FormatMoney(reserve)})");
                return 0;
            }

            int pendingUnits = stock.PendingReorderUnits;
            int receivedFromPending = pendingUnits > 0
                ? business.RuntimeState.ReceivePendingReorderUnits(inputCategoryId, Mathf.Min(affordableUnits, pendingUnits))
                : 0;
            int directUnits = affordableUnits - receivedFromPending;
            int receivedDirect = directUnits > 0 ? business.RuntimeState.AddCategoryStockUnits(inputCategoryId, directUnits) : 0;
            int receivedUnits = receivedFromPending + receivedDirect;
            int spend = receivedUnits * unitCost;
            if (spend > 0)
            {
                business.RuntimeState.SpendCents(spend);
                business.RuntimeState.RecordWeeklyInputProcurement(receivedUnits, spend);
            }

            if (receivedUnits < desiredUnits)
            {
                blockedReasons?.Add($"cash-limited input procurement: {inputCategoryId} (reserve {FormatMoney(reserve)})");
            }

            return receivedUnits;
        }

        private static int CalculateSurvivalInputProcurementUnits(
            CategoryStockState stock,
            int plannedInputUnits,
            int productionShortfall)
        {
            int shortfall = Mathf.Max(0, productionShortfall);
            int weeklyBuffer = Mathf.Max(0, plannedInputUnits);
            int pendingBuffer = stock != null ? Mathf.Min(Mathf.Max(0, stock.PendingReorderUnits), weeklyBuffer) : 0;
            return shortfall + pendingBuffer;
        }

        private void ReleaseSuspendedPayrollWorkersFromPopulation(BusinessInstanceState business)
        {
            if (business == null
                || business.RuntimeState == null
                || business.RuntimeState.LastSuspendedPayrollWorkerIds.Count == 0
                || populationManager == null
                || populationManager.State == null)
            {
                return;
            }

            int released = 0;
            for (int i = 0; i < business.RuntimeState.LastSuspendedPayrollWorkerIds.Count; i++)
            {
                if (!int.TryParse(business.RuntimeState.LastSuspendedPayrollWorkerIds[i], out int personId))
                {
                    continue;
                }

                PersonState person = populationManager.State.GetPerson(personId);
                if (person == null || person.workplaceBuildingId != business.AssignedBuildingId)
                {
                    continue;
                }

                ResetPersonEmployment(person);
                UnassignBusinessWorkerSlot(business, business.RuntimeState.LastSuspendedPayrollWorkerIds[i]);
                released++;
            }

            if (released > 0)
            {
                populationManager.RecalculateHouseholdIncomeAndSummaries($"{released} worker(s) suspended after missed payroll at {business.RuntimeDisplayName}.");
            }
        }

        private static void UnassignBusinessWorkerSlot(BusinessInstanceState business, string workerId)
        {
            if (business == null || business.RuntimeState == null || string.IsNullOrWhiteSpace(workerId))
            {
                return;
            }

            for (int i = 0; i < business.RuntimeState.WorkerSlots.Count; i++)
            {
                WorkerSlotState slot = business.RuntimeState.WorkerSlots[i];
                if (string.Equals(slot.AssignedWorkerId, workerId, StringComparison.Ordinal))
                {
                    slot.Unassign();
                    return;
                }
            }
        }

        private static int GetOutputUnitsPerInput(BusinessType businessType)
        {
            return businessType switch
            {
                BusinessType.CropFarm => CropFarmOutputUnitsPerInput,
                BusinessType.Ranch => RanchOutputUnitsPerInput,
                BusinessType.Butcher => ButcherOutputUnitsPerInput,
                BusinessType.Blacksmith => BlacksmithOutputUnitsPerInput,
                BusinessType.FuelDealer => FuelDealerOutputUnitsPerInput,
                BusinessType.GrainMill => GrainMillOutputUnitsPerInput,
                BusinessType.Bakery => BakeryOutputUnitsPerInput,
                BusinessType.Tailor => 2,
                BusinessType.Wheelwright => 2,
                _ => 1
            };
        }

        private static int GetSharedOperatingCashBufferCents(BusinessRuntimeState runtime)
        {
            if (runtime == null)
            {
                return SharedOperatingCashFloorCents;
            }

            int payrollReserve = Mathf.Max(SharedOperatingCashFloorCents, runtime.FilledWeeklyPayrollCents);
            int reorderReserve = Mathf.Max(0, Mathf.CeilToInt(runtime.LastWeeklyReorderBudgetCents * 0.35f));
            return Mathf.Max(SharedOperatingCashFloorCents, payrollReserve + reorderReserve);
        }

        private static int GetSharedOperatingCashBufferCents(BusinessInstanceState business)
        {
            if (business == null)
            {
                return SharedOperatingCashFloorCents;
            }

            return ManagerPolicyEffects.CalculateCashReserveCents(
                GetSharedOperatingCashBufferCents(business.RuntimeState),
                business.ControlState,
                business.ManagerPolicy);
        }

        private static int GetSharedBusinessSurvivalCashReserveCents(BusinessInstanceState business)
        {
            if (business == null || business.RuntimeState == null)
            {
                return SharedOperatingCashFloorCents;
            }

            int survivalReserve = PlayerPortfolioManager.CalculateSurvivalReserveCents(
                GetSharedOperatingCashBufferCents(business),
                business.RuntimeState.FilledWeeklyPayrollCents,
                business.RuntimeState.LastWeeklyReorderBudgetCents);
            int fragilityPadding = Mathf.CeilToInt(Mathf.Max(
                business.RuntimeState.FilledWeeklyPayrollCents,
                business.RuntimeState.LastWeeklyReorderBudgetCents) * 0.20f);
            return Mathf.Max(SharedOperatingCashFloorCents, survivalReserve + fragilityPadding);
        }

        public int GetLocalTradeAdjustedSurvivalCashReserveCents(BusinessInstanceState business)
        {
            if (business == null || business.RuntimeState == null)
            {
                return SharedOperatingCashFloorCents;
            }

            int baseReserve = GetSharedBusinessSurvivalCashReserveCents(business);
            int relationshipBasis = Mathf.Max(
                business.RuntimeState.FilledWeeklyPayrollCents,
                business.RuntimeState.LastWeeklyReorderBudgetCents);
            if (relationshipBasis <= 0)
            {
                return baseReserve;
            }

            float localPressure = CalculateIncomingLocalRelationshipReservePressure01(business);
            int adjustment = Mathf.RoundToInt(relationshipBasis * localPressure);
            return Mathf.Max(SharedOperatingCashFloorCents, baseReserve + adjustment);
        }

        private int GetSharedSpendingReserveCents(BusinessInstanceState business)
        {
            return GetLocalTradeAdjustedSurvivalCashReserveCents(business);
        }

        public static int CalculateSharedOperatingCashReserveCents(BusinessInstanceState business)
        {
            return GetSharedOperatingCashBufferCents(business);
        }

        public static int CalculateSharedSurvivalCashReserveCents(BusinessInstanceState business)
        {
            return GetSharedBusinessSurvivalCashReserveCents(business);
        }

        private float CalculateIncomingLocalRelationshipReservePressure01(BusinessInstanceState business)
        {
            if (business == null || string.IsNullOrWhiteSpace(business.InstanceId))
            {
                return 0f;
            }

            float support = 0f;
            float strain = 0f;
            IReadOnlyList<LocalRecurringOrderRelationshipState> relationships = LocalOrderRelationships;
            for (int i = 0; i < relationships.Count; i++)
            {
                LocalRecurringOrderRelationshipState relationship = relationships[i];
                if (!ShouldSurfaceRecurringRelationshipInInspection(relationship)
                    || !string.Equals(relationship.BuyerInstanceId, business.InstanceId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (IsCancelledRecurringRelationship(relationship))
                {
                    strain += 0.25f;
                    continue;
                }

                if (IsStrainedRecurringRelationship(relationship))
                {
                    strain += 0.14f + (1f - relationship.RelationshipHealth01) * 0.12f;
                    continue;
                }

                if (IsCleanRecurringRelationship(relationship))
                {
                    support += 0.08f + relationship.RelationshipHealth01 * 0.06f;
                }
            }

            return Mathf.Clamp(strain - support, -0.18f, 0.35f);
        }

        private static float GetEffectiveReorderThreshold01(BusinessInstanceState business, BusinessDefinition definition)
        {
            float baseThreshold = definition != null ? definition.Economy.LowStockWarningThreshold01 : 0f;
            if (business == null)
            {
                return Mathf.Clamp01(baseThreshold);
            }

            return ManagerPolicyEffects.CalculateReorderThreshold01(baseThreshold, business.ControlState, business.ManagerPolicy);
        }

        private static int GetFilledWeeklyPayrollCents(BusinessRuntimeState runtime)
        {
            if (runtime == null)
            {
                return 0;
            }

            int payroll = 0;
            for (int i = 0; i < runtime.WorkerSlots.Count; i++)
            {
                WorkerSlotState slot = runtime.WorkerSlots[i];
                if (slot != null && slot.IsFilled)
                {
                    payroll += slot.WeeklyWageCents;
                }
            }

            return payroll;
        }

        private static void SeedBlockedReasons(BusinessRuntimeState runtime, List<string> blockedReasons)
        {
            if (runtime == null || blockedReasons == null || string.IsNullOrWhiteSpace(runtime.LastWeeklyBlockedReason))
            {
                return;
            }

            string[] reasons = runtime.LastWeeklyBlockedReason.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < reasons.Length; i++)
            {
                AddBlockedReason(blockedReasons, reasons[i]);
            }
        }

        private static void AppendStaffingBlockedReasons(BusinessRuntimeState runtime, List<string> blockedReasons)
        {
            if (runtime == null || blockedReasons == null)
            {
                return;
            }

            if (runtime.LastSuspendedPayrollWorkerIds.Count > 0)
            {
                AddBlockedReason(blockedReasons, "missed payroll");
            }

            if (runtime.RequiredWorkerCount > 0 && runtime.ActiveRequiredWorkerCount < runtime.RequiredWorkerCount)
            {
                AddBlockedReason(blockedReasons, "missing required staff");
                return;
            }

            if (runtime.TargetWorkerCount > 0 && runtime.ActiveWorkerCount <= 0)
            {
                AddBlockedReason(blockedReasons, "missing active staff");
            }
        }

        private static void AddBlockedReason(List<string> blockedReasons, string reason)
        {
            if (blockedReasons == null || string.IsNullOrWhiteSpace(reason))
            {
                return;
            }

            string trimmed = reason.Trim();
            for (int i = 0; i < blockedReasons.Count; i++)
            {
                if (string.Equals(blockedReasons[i], trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            blockedReasons.Add(trimmed);
        }

        private static string BuildBlockedReason(List<string> blockedReasons)
        {
            if (blockedReasons == null || blockedReasons.Count == 0)
            {
                return string.Empty;
            }

            List<string> unique = new();
            for (int i = 0; i < blockedReasons.Count; i++)
            {
                string reason = blockedReasons[i];
                if (string.IsNullOrWhiteSpace(reason))
                {
                    continue;
                }

                bool exists = false;
                for (int j = 0; j < unique.Count; j++)
                {
                    if (string.Equals(unique[j], reason, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    unique.Add(reason);
                }
            }

            return unique.Count == 0 ? string.Empty : string.Join("; ", unique);
        }

        private int ResolveRecurringLocalOrders(StringBuilder summary)
        {
            localRecurringOrderManager.BeginWeeklyResolution();
            int relationshipRepairs = NormalizeRecurringRelationshipActives(GetCurrentWeekKey());
            int fulfilledUnits = 0;
            for (int i = 0; i < DefaultLocalRecurringOrders.Count; i++)
            {
                LocalRecurringOrderTemplate template = DefaultLocalRecurringOrders[i];
                if (template == null)
                {
                    continue;
                }

                if (template.BuyerType == BusinessType.GeneralStore)
                {
                    fulfilledUnits += ResolveRecurringGeneralStoreOrder(template, summary);
                    continue;
                }

                fulfilledUnits += ResolveRecurringBusinessOrders(template, summary);
            }

            localRecurringOrderManager.CompleteWeeklyResolution();
            lastWeeklyRecurringLocalOrderSummary = localRecurringOrderManager.LastWeeklySummary;
            if (!string.IsNullOrWhiteSpace(lastWeeklyRecurringLocalOrderSummary)
                && !string.Equals(lastWeeklyRecurringLocalOrderSummary, "No recurring local orders resolved this week.", StringComparison.OrdinalIgnoreCase))
            {
                AppendSummarySegment(summary, lastWeeklyRecurringLocalOrderSummary);
            }

            if (relationshipRepairs > 0)
            {
                AppendSummarySegment(summary, $"repaired {relationshipRepairs} stale recurring local order relationship(s)");
            }

            return fulfilledUnits;
        }

        public int ResolveOwnedBusinessTransferAgreementsForTests(int weekKey)
        {
            return ResolveOwnedBusinessTransferAgreements(weekKey, null);
        }

        public void NotifyTransferAgreementShipmentDelivered(
            string agreementId,
            string shipmentId,
            int deliveredUnits,
            int sellerRevenueCents,
            int buyerCostCents,
            string summaryLabel)
        {
            BusinessTransferAgreementState agreement = FindTransferAgreementById(agreementId);
            if (agreement == null)
            {
                return;
            }

            string moneySegment = sellerRevenueCents > 0 || buyerCostCents > 0
                ? $" for seller {FormatMoney(sellerRevenueCents)} / buyer {FormatMoney(buyerCostCents)}"
                : " at no ledger price";
            agreement.RecordDelivery(
                GetCurrentWeekKey(),
                deliveredUnits,
                Mathf.Max(sellerRevenueCents, buyerCostCents),
                $"{summaryLabel} delivered {deliveredUnits} units{moneySegment}",
                shipmentId);
        }

        public void NotifyTransferAgreementShipmentBlocked(string agreementId, string shipmentId, string blockedReason)
        {
            BusinessTransferAgreementState agreement = FindTransferAgreementById(agreementId);
            if (agreement == null)
            {
                return;
            }

            string reason = string.IsNullOrWhiteSpace(blockedReason) ? "shipment blocked" : blockedReason.Trim();
            string shipmentSegment = string.IsNullOrWhiteSpace(shipmentId) ? string.Empty : $" ({shipmentId})";
            agreement.RecordBlocked(GetCurrentWeekKey(), $"{agreement.AgreementId} blocked: {reason}{shipmentSegment}");
        }

        private int ResolveOwnedBusinessTransferAgreements(StringBuilder summary)
        {
            return ResolveOwnedBusinessTransferAgreements(GetCurrentWeekKey(), summary);
        }

        private int ResolveOwnedBusinessTransferAgreements(int weekKey, StringBuilder summary)
        {
            if (transferAgreements.Count <= 0)
            {
                lastWeeklyTransferAgreementSummary = "No owned transfer agreements resolved this week.";
                return 0;
            }

            List<string> segments = new();
            int scheduledUnits = 0;
            for (int i = 0; i < transferAgreements.Count; i++)
            {
                BusinessTransferAgreementState agreement = transferAgreements[i];
                if (agreement == null || !agreement.Active || !agreement.IsDue(weekKey))
                {
                    continue;
                }

                scheduledUnits += ResolveOwnedTransferAgreement(agreement, weekKey, segments);
            }

            lastWeeklyTransferAgreementSummary = segments.Count > 0
                ? "Owned transfer agreements: " + string.Join("; ", segments)
                : "No owned transfer agreements resolved this week.";
            if (summary != null
                && !string.Equals(lastWeeklyTransferAgreementSummary, "No owned transfer agreements resolved this week.", StringComparison.OrdinalIgnoreCase))
            {
                AppendSummarySegment(summary, lastWeeklyTransferAgreementSummary);
            }

            return scheduledUnits;
        }

        private int ResolveOwnedTransferAgreement(BusinessTransferAgreementState agreement, int weekKey, List<string> summarySegments)
        {
            if (agreement == null)
            {
                return 0;
            }

            if (agreement.Cadence != BusinessCadence.Weekly)
            {
                agreement.RecordBlocked(weekKey, $"{agreement.AgreementId} skipped: {agreement.Cadence} cadence is not yet resolved outside weekly runtime");
                summarySegments?.Add(agreement.LastFulfillmentSummary);
                return 0;
            }

            BusinessInstanceState source = FindByInstanceId(agreement.SourceBusinessInstanceId);
            BusinessInstanceState destination = FindByInstanceId(agreement.DestinationBusinessInstanceId);
            if (!IsOwnedTransferEndpoint(source) || !IsOwnedTransferEndpoint(destination))
            {
                agreement.RecordBlocked(weekKey, $"{agreement.AgreementId} blocked: owned source/destination missing");
                summarySegments?.Add(agreement.LastFulfillmentSummary);
                return 0;
            }

            if (!CanParticipateInWeeklyTransfer(source, source.BusinessType)
                || source.RuntimeState == null
                || destination.RuntimeState == null)
            {
                agreement.RecordBlocked(weekKey, $"{agreement.AgreementId} blocked: business inactive");
                summarySegments?.Add(agreement.LastFulfillmentSummary);
                return 0;
            }

            CategoryStockState sourceStock = source.RuntimeState.GetCategoryStock(agreement.SourceCategoryId);
            CategoryStockState destinationStock = destination.RuntimeState.GetCategoryStock(agreement.DestinationCategoryId);
            if (sourceStock == null || destinationStock == null)
            {
                agreement.RecordBlocked(weekKey, $"{agreement.AgreementId} blocked: category missing");
                summarySegments?.Add(agreement.LastFulfillmentSummary);
                return 0;
            }

            int availableUnits = Mathf.Max(0, sourceStock.CurrentStockUnits - agreement.SourceReserveUnits);
            if (availableUnits <= 0)
            {
                agreement.RecordBlocked(weekKey, $"{agreement.AgreementId} blocked: reserve protects {agreement.SourceCategoryId}");
                summarySegments?.Add(agreement.LastFulfillmentSummary);
                return 0;
            }

            int requestedUnits = agreement.AllocationMode == BusinessTransferAllocationMode.PercentOfAvailable
                ? Mathf.FloorToInt(availableUnits * Mathf.Clamp(agreement.AllocationValue, 0, 100) / 100f)
                : Mathf.Max(0, agreement.AllocationValue);
            requestedUnits = Mathf.Clamp(requestedUnits, 0, availableUnits);
            if (requestedUnits <= 0)
            {
                agreement.RecordBlocked(weekKey, $"{agreement.AgreementId} skipped: no allocatable units");
                summarySegments?.Add(agreement.LastFulfillmentSummary);
                return 0;
            }

            int capacityUnits = GetAvailableCategoryCapacity(destination.RuntimeState, agreement.DestinationCategoryId);
            int units = Mathf.Min(requestedUnits, capacityUnits);
            if (units <= 0)
            {
                agreement.RecordBlocked(weekKey, $"{agreement.AgreementId} blocked: destination full for {agreement.DestinationCategoryId}");
                summarySegments?.Add(agreement.LastFulfillmentSummary);
                return 0;
            }

            BusinessProfileDefinition sourceProfile = FindProfile(source.BusinessType);
            int sellerUnitPriceCents = ResolveTransferAgreementUnitPriceCents(source, sourceProfile, agreement);
            int buyerUnitPriceCents = sellerUnitPriceCents;

            logisticsRuntime ??= FindAnyObjectByType<LogisticsRuntimeManager>();
            if (logisticsRuntime == null)
            {
                agreement.RecordBlocked(weekKey, $"{agreement.AgreementId} blocked: logistics runtime unavailable");
                summarySegments?.Add(agreement.LastFulfillmentSummary);
                return 0;
            }

            LogisticsShipmentState shipment = logisticsRuntime.CreateLocalBusinessShipment(
                source,
                agreement.SourceCategoryId,
                destination,
                agreement.DestinationCategoryId,
                units,
                sellerUnitPriceCents,
                buyerUnitPriceCents,
                true,
                LogisticsShipmentDeliveryMode.AddCategoryStock,
                $"{source.RuntimeDisplayName}->{destination.RuntimeDisplayName}",
                agreement.HaulingResponsibility.ToShipmentHaulingMode(),
                destination.InstanceId,
                agreement.AgreementId,
                LogisticsSupplierClass.InternalSupplier);
            if (shipment == null
                || shipment.RoutePlan == null
                || shipment.RoutePlan.VisiblePath == null
                || shipment.RoutePlan.VisiblePath.Count <= 0)
            {
                if (string.IsNullOrWhiteSpace(agreement.LastFulfillmentSummary))
                {
                    agreement.RecordBlocked(weekKey, $"{agreement.AgreementId} blocked: no route");
                }

                summarySegments?.Add(agreement.LastFulfillmentSummary);
                return 0;
            }

            if (!source.RuntimeState.TryConsumeCategoryStockUnits(agreement.SourceCategoryId, units, out int consumedUnits) || consumedUnits <= 0)
            {
                agreement.RecordBlocked(weekKey, $"{agreement.AgreementId} blocked: source stock changed before load");
                summarySegments?.Add(agreement.LastFulfillmentSummary);
                return 0;
            }

            source.RefreshCapacityState();
            destination.RefreshCapacityState();
            int totalPriceCents = consumedUnits * Mathf.Max(0, sellerUnitPriceCents);
            string priceMode = agreement.PricingMode.ToString();
            agreement.RecordAttemptScheduled(
                weekKey,
                consumedUnits,
                totalPriceCents,
                $"{agreement.AgreementId} scheduled {consumedUnits} {agreement.SourceCategoryId} to {destination.RuntimeDisplayName} [{priceMode}] ETA {FormatGameSeconds(shipment.EstimatedRemainingGameSeconds)}",
                shipment.ShipmentId);
            summarySegments?.Add(agreement.LastFulfillmentSummary);
            return consumedUnits;
        }

        private int ResolveRecurringBusinessOrders(LocalRecurringOrderTemplate template, StringBuilder summary)
        {
            if (template == null)
            {
                return 0;
            }

            List<BusinessInstanceState> sellers = CollectRecurringOrderBusinesses(template.SellerType);
            if (sellers.Count <= 0)
            {
                return 0;
            }

            List<BusinessInstanceState> buyers = BuildRecurringOrderBuyerList(template);
            int fulfilledUnits = 0;
            int weekKey = GetCurrentWeekKey();
            for (int i = 0; i < buyers.Count; i++)
            {
                BusinessInstanceState buyer = buyers[i];
                fulfilledUnits += ResolveRecurringOrderForBuyer(
                    template,
                    buyer,
                    sellers,
                    weekKey,
                    GetSharedSpendingReserveCents(buyer),
                    CanParticipateInRecurringLocalOrder(buyer, template.BuyerType, false),
                    null,
                    (seller, resolvedBuyer) => GetAverageCategoryTransferPriceCents(seller, FindProfile(seller.BusinessType), template.SellerCategoryId),
                    (seller, resolvedBuyer) => template.SellerType == BusinessType.Sawmill && template.BuyerType == BusinessType.LumberYard
                        ? GetSawmillToLumberYardDeliveryCostPerUnitCents(seller, resolvedBuyer)
                        : 0,
                    summary);
            }

            return fulfilledUnits;
        }

        private int ResolveRecurringGeneralStoreOrder(LocalRecurringOrderTemplate template, StringBuilder summary)
        {
            AutoWire();
            if (template == null
                || generalStoreRuntime == null
                || !generalStoreRuntime.InitializeIfNeeded()
                || generalStoreRuntime.CurrentBusiness == null
                || generalStoreRuntime.RuntimeState == null)
            {
                return 0;
            }

            List<BusinessInstanceState> sellers = CollectRecurringOrderBusinesses(template.SellerType);
            if (sellers.Count <= 0)
            {
                return 0;
            }

            return ResolveRecurringOrderForBuyer(
                template,
                generalStoreRuntime.CurrentBusiness,
                sellers,
                GetCurrentWeekKey(),
                generalStoreRuntime.ProtectedBusinessCashReserveCents,
                generalStoreRuntime.CurrentBusiness.RuntimeState != null,
                generalStoreRuntime.ReceiveLocalSupply,
                (seller, resolvedBuyer) => GetGeneralStoreLocalSupplyPriceCents(
                    seller,
                    FindProfile(seller.BusinessType),
                    template.SellerCategoryId,
                    template.BuyerCategoryId),
                (seller, resolvedBuyer) => 0,
                summary);
        }

        private int ResolveRecurringOrderForBuyer(
            LocalRecurringOrderTemplate template,
            BusinessInstanceState buyer,
            IReadOnlyList<BusinessInstanceState> candidateSellers,
            int weekKey,
            int buyerCashReserveCents,
            bool buyerCanReceive,
            Func<string, int, int, string, int, int> receiveBuyerSupply,
            Func<BusinessInstanceState, BusinessInstanceState, int> resolveUnitPriceCents,
            Func<BusinessInstanceState, BusinessInstanceState, int> resolveExtraBuyerUnitCostCents,
            StringBuilder summary)
        {
            if (template == null
                || buyer == null
                || buyer.RuntimeState == null
                || candidateSellers == null
                || candidateSellers.Count <= 0
                || resolveUnitPriceCents == null
                || resolveExtraBuyerUnitCostCents == null)
            {
                return 0;
            }

            int requestedUnits = GetRecurringBuyerRequestedUnits(buyer, template);
            if (TryFindAnchoredRecurringSellerForBuyer(template, buyer, weekKey, out BusinessInstanceState anchoredSeller, out _))
            {
                return ResolveRecurringOrderAgainstSeller(
                    template,
                    anchoredSeller,
                    buyer,
                    weekKey,
                    buyerCashReserveCents,
                    buyerCanReceive,
                    receiveBuyerSupply,
                    resolveUnitPriceCents,
                    resolveExtraBuyerUnitCostCents,
                    summary);
            }

            if (requestedUnits <= 0)
            {
                return 0;
            }

            BusinessInstanceState selectedSeller = SelectBestNewRecurringSellerForBuyer(
                template,
                buyer,
                requestedUnits,
                candidateSellers,
                resolveUnitPriceCents,
                resolveExtraBuyerUnitCostCents);
            if (selectedSeller == null)
            {
                buyer.RuntimeState.AppendWeeklyBlockedReason($"no recurring local supplier: {template.BuyerCategoryId}");
                return 0;
            }

            return ResolveRecurringOrderAgainstSeller(
                template,
                selectedSeller,
                buyer,
                weekKey,
                buyerCashReserveCents,
                buyerCanReceive,
                receiveBuyerSupply,
                resolveUnitPriceCents,
                resolveExtraBuyerUnitCostCents,
                summary);
        }

        private int ResolveRecurringOrderAgainstSeller(
            LocalRecurringOrderTemplate template,
            BusinessInstanceState seller,
            BusinessInstanceState buyer,
            int weekKey,
            int buyerCashReserveCents,
            bool buyerCanReceive,
            Func<string, int, int, string, int, int> receiveBuyerSupply,
            Func<BusinessInstanceState, BusinessInstanceState, int> resolveUnitPriceCents,
            Func<BusinessInstanceState, BusinessInstanceState, int> resolveExtraBuyerUnitCostCents,
            StringBuilder summary)
        {
            if (template == null || seller == null || buyer == null)
            {
                return 0;
            }

            int unitPriceCents = Mathf.Max(0, resolveUnitPriceCents(seller, buyer));
            int extraBuyerUnitCostCents = Mathf.Max(0, resolveExtraBuyerUnitCostCents(seller, buyer));
            LocalRecurringOrderFulfillmentResult result = localRecurringOrderManager.ResolveOrder(new LocalRecurringOrderFulfillmentRequest
            {
                Template = template,
                Seller = seller,
                Buyer = buyer,
                WeekKey = weekKey,
                UnitPriceCents = unitPriceCents,
                ExtraBuyerUnitCostCents = extraBuyerUnitCostCents,
                BuyerCashReserveCents = buyerCashReserveCents,
                SellerCanFulfill = CanParticipateInRecurringLocalOrder(seller, template.SellerType, true),
                BuyerCanReceive = buyerCanReceive,
                ReceiveBuyerSupply = receiveBuyerSupply,
                ScheduleShipment = BuildRecurringOrderShipmentScheduler(template, seller, buyer)
            });

            ApplyRecurringOrderRuntimeSummary(result, seller, buyer, template, unitPriceCents, extraBuyerUnitCostCents, summary);
            return result.FulfilledUnits;
        }

        private List<BusinessInstanceState> CollectRecurringOrderBusinesses(BusinessType type)
        {
            List<BusinessInstanceState> result = new();
            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState business = businesses[i];
                if (business != null && business.BusinessType == type)
                {
                    result.Add(business);
                }
            }

            return result;
        }

        private List<BusinessInstanceState> BuildRecurringOrderBuyerList(LocalRecurringOrderTemplate template)
        {
            List<BusinessInstanceState> buyers = new();
            if (template == null)
            {
                return buyers;
            }

            int weekKey = GetCurrentWeekKey();
            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState buyer = businesses[i];
                if (buyer == null || buyer.BusinessType != template.BuyerType || buyer.RuntimeState == null)
                {
                    continue;
                }

                buyers.Add(buyer);
            }

            buyers.Sort((left, right) => CompareRecurringBuyerPriority(template, left, right, weekKey));
            return buyers;
        }

        private int CompareRecurringBuyerPriority(LocalRecurringOrderTemplate template, BusinessInstanceState left, BusinessInstanceState right, int weekKey)
        {
            bool leftAnchored = TryFindAnchoredRecurringSellerForBuyer(template, left, weekKey, out _, out _);
            bool rightAnchored = TryFindAnchoredRecurringSellerForBuyer(template, right, weekKey, out _, out _);
            if (leftAnchored != rightAnchored)
            {
                return leftAnchored ? -1 : 1;
            }

            int leftNeed = GetRecurringBuyerNeedUnits(left, template.BuyerCategoryId, template.WeeklyTargetUnits);
            int rightNeed = GetRecurringBuyerNeedUnits(right, template.BuyerCategoryId, template.WeeklyTargetUnits);
            int needCompare = rightNeed.CompareTo(leftNeed);
            if (needCompare != 0)
            {
                return needCompare;
            }

            int buildingCompare = Mathf.Max(-1, left != null ? left.AssignedBuildingId : -1).CompareTo(Mathf.Max(-1, right != null ? right.AssignedBuildingId : -1));
            if (buildingCompare != 0)
            {
                return buildingCompare;
            }

            return string.Compare(left != null ? left.InstanceId : string.Empty, right != null ? right.InstanceId : string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        private bool TryFindAnchoredRecurringSellerForBuyer(
            LocalRecurringOrderTemplate template,
            BusinessInstanceState buyer,
            int weekKey,
            out BusinessInstanceState seller,
            out LocalRecurringOrderRelationshipState relationship)
        {
            seller = null;
            relationship = null;
            if (template == null || buyer == null)
            {
                return false;
            }

            IReadOnlyList<LocalRecurringOrderRelationshipState> relationships = LocalOrderRelationships;
            for (int i = 0; i < relationships.Count; i++)
            {
                LocalRecurringOrderRelationshipState candidate = relationships[i];
                if (candidate == null
                    || !candidate.Active
                    || !string.Equals(candidate.OrderId, template.OrderId, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(candidate.BuyerInstanceId, buyer.InstanceId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                BusinessInstanceState candidateSeller = FindByInstanceId(candidate.SellerInstanceId);
                if (candidateSeller == null
                    || candidateSeller.BusinessType != template.SellerType
                    || ReferenceEquals(candidateSeller, buyer))
                {
                    continue;
                }

                if (relationship == null || CompareRecurringRelationshipPriority(candidate, relationship, weekKey) < 0)
                {
                    relationship = candidate;
                    seller = candidateSeller;
                }
            }

            return seller != null;
        }

        private static int CompareRecurringRelationshipPriority(
            LocalRecurringOrderRelationshipState left,
            LocalRecurringOrderRelationshipState right,
            int weekKey)
        {
            bool leftDue = IsRecurringRelationshipDue(left, weekKey);
            bool rightDue = IsRecurringRelationshipDue(right, weekKey);
            if (leftDue != rightDue)
            {
                return leftDue ? -1 : 1;
            }

            int healthCompare = right.RelationshipHealth01.CompareTo(left.RelationshipHealth01);
            if (healthCompare != 0)
            {
                return healthCompare;
            }

            int breachCompare = left.ConsecutiveBreachWeeks.CompareTo(right.ConsecutiveBreachWeeks);
            if (breachCompare != 0)
            {
                return breachCompare;
            }

            int resolvedCompare = right.LastResolvedWeekKey.CompareTo(left.LastResolvedWeekKey);
            if (resolvedCompare != 0)
            {
                return resolvedCompare;
            }

            return string.Compare(left.SellerInstanceId, right.SellerInstanceId, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRecurringRelationshipDue(LocalRecurringOrderRelationshipState relationship, int weekKey)
        {
            return relationship != null
                && relationship.Active
                && (relationship.NextDueWeekKey < 0 || weekKey >= relationship.NextDueWeekKey);
        }

        private BusinessInstanceState SelectBestNewRecurringSellerForBuyer(
            LocalRecurringOrderTemplate template,
            BusinessInstanceState buyer,
            int requestedUnits,
            IReadOnlyList<BusinessInstanceState> candidateSellers,
            Func<BusinessInstanceState, BusinessInstanceState, int> resolveUnitPriceCents,
            Func<BusinessInstanceState, BusinessInstanceState, int> resolveExtraBuyerUnitCostCents)
        {
            if (template == null || buyer == null || requestedUnits <= 0 || candidateSellers == null || candidateSellers.Count <= 0)
            {
                return null;
            }

            BusinessInstanceState bestSeller = null;
            int bestScore = int.MaxValue;
            for (int i = 0; i < candidateSellers.Count; i++)
            {
                BusinessInstanceState candidate = candidateSellers[i];
                if (candidate == null
                    || ReferenceEquals(candidate, buyer)
                    || !CanParticipateInRecurringLocalOrder(candidate, template.SellerType, true))
                {
                    continue;
                }

                CategoryStockState stock = candidate.RuntimeState != null ? candidate.RuntimeState.GetCategoryStock(template.SellerCategoryId) : null;
                int stockUnits = stock != null ? Mathf.Max(0, stock.CurrentStockUnits) : 0;
                if (stockUnits <= 0)
                {
                    continue;
                }

                int unitPriceCents = Mathf.Max(0, resolveUnitPriceCents(candidate, buyer));
                if (unitPriceCents <= 0)
                {
                    continue;
                }

                int extraBuyerUnitCostCents = Mathf.Max(0, resolveExtraBuyerUnitCostCents(candidate, buyer));
                int candidateScore = CalculateRecurringSellerScore(template, candidate, buyer, unitPriceCents, extraBuyerUnitCostCents, requestedUnits, stockUnits);
                if (candidateScore < bestScore
                    || (candidateScore == bestScore
                        && string.Compare(candidate.InstanceId, bestSeller != null ? bestSeller.InstanceId : string.Empty, StringComparison.OrdinalIgnoreCase) < 0))
                {
                    bestScore = candidateScore;
                    bestSeller = candidate;
                }
            }

            return bestSeller;
        }

        private int CalculateRecurringSellerScore(
            LocalRecurringOrderTemplate template,
            BusinessInstanceState seller,
            BusinessInstanceState buyer,
            int unitPriceCents,
            int extraBuyerUnitCostCents,
            int requestedUnits,
            int stockUnits)
        {
            if (template == null || seller == null || seller.RuntimeState == null)
            {
                return int.MaxValue;
            }

            TownPlot buyerPlot = GetSellerPlot(buyer);
            TownPlot sellerPlot = GetSellerPlot(seller);
            int routeDistanceCells = EstimateRouteDistanceCells(buyerPlot, sellerPlot);
            int baselineUnitPriceCents = GetAverageCategoryTransferPriceCents(FindProfile(template.SellerType), template.SellerCategoryId);
            int effectiveUnitPriceCents = Mathf.Max(1, unitPriceCents + Mathf.Max(0, extraBuyerUnitCostCents));
            int pricePenaltyCells = Mathf.RoundToInt(Mathf.Clamp(((effectiveUnitPriceCents - Mathf.Max(1, baselineUnitPriceCents)) / (float)Mathf.Max(1, baselineUnitPriceCents)) * 20f, -6f, 20f));
            int reliabilityPenaltyCells = Mathf.RoundToInt((1f - seller.RuntimeState.Reliability01) * 18f);
            int shortfallPenaltyCells = stockUnits >= requestedUnits
                ? -6
                : Mathf.RoundToInt(Mathf.Clamp(((requestedUnits - stockUnits) / (float)requestedUnits) * 18f, 0f, 18f));
            return routeDistanceCells + pricePenaltyCells + reliabilityPenaltyCells + shortfallPenaltyCells;
        }

        private int GetRecurringSellerUnitPriceCents(LocalRecurringOrderTemplate template, BusinessInstanceState seller, BusinessInstanceState buyer)
        {
            BusinessProfileDefinition sourceProfile = FindProfile(seller != null ? seller.BusinessType : template != null ? template.SellerType : default);
            if (template != null && template.BuyerType == BusinessType.GeneralStore)
            {
                return GetGeneralStoreLocalSupplyPriceCents(seller, sourceProfile, template.SellerCategoryId, template.BuyerCategoryId);
            }

            return GetAverageCategoryTransferPriceCents(seller, sourceProfile, template != null ? template.SellerCategoryId : string.Empty);
        }

        private int GetRecurringSellerExtraBuyerUnitCostCents(LocalRecurringOrderTemplate template, BusinessInstanceState seller, BusinessInstanceState buyer)
        {
            return template != null && seller != null && buyer != null && template.SellerType == BusinessType.Sawmill && template.BuyerType == BusinessType.LumberYard
                ? GetSawmillToLumberYardDeliveryCostPerUnitCents(seller, buyer)
                : 0;
        }

        // Recurring local orders are meant to read like real counterparties, not a blanket market spray.
        // Once a buyer has an active relationship for a template, keep resolving through that counterparty
        // until it breaks down or is cancelled so trust, shortfalls, and renewal history stay meaningful.
        private int GetRecurringBuyerNeedUnits(BusinessInstanceState buyer, string buyerCategoryId, int fallbackUnits)
        {
            CategoryStockState stock = buyer != null && buyer.RuntimeState != null
                ? buyer.RuntimeState.GetCategoryStock(buyerCategoryId)
                : null;
            if (stock == null)
            {
                return Mathf.Max(0, fallbackUnits);
            }

            return stock.TargetStockUnits > 0
                ? Mathf.Max(0, stock.TargetStockUnits - stock.CurrentStockUnits)
                : Mathf.Max(0, fallbackUnits);
        }

        private int GetRecurringBuyerRequestedUnits(BusinessInstanceState buyer, LocalRecurringOrderTemplate template)
        {
            return template == null
                ? 0
                : Mathf.Min(Mathf.Max(0, template.WeeklyTargetUnits), GetRecurringBuyerNeedUnits(buyer, template.BuyerCategoryId, template.WeeklyTargetUnits));
        }

        // Older saves can carry multiple active relationships for the same buyer/order slot from the
        // pre-counterparty era. Keep exactly one viable active relationship per slot and cancel the rest
        // so summaries, reserve pressure, and future matching all read as one real counterparty.
        private int NormalizeRecurringRelationshipActives(int weekKey)
        {
            int repairs = 0;
            Dictionary<string, LocalRecurringOrderRelationshipState> preferredBySlot = new(StringComparer.OrdinalIgnoreCase);
            IReadOnlyList<LocalRecurringOrderRelationshipState> relationships = LocalOrderRelationships;
            for (int i = 0; i < relationships.Count; i++)
            {
                LocalRecurringOrderRelationshipState relationship = relationships[i];
                if (relationship == null || !relationship.Active)
                {
                    continue;
                }

                LocalRecurringOrderTemplate template = FindRecurringOrderTemplate(relationship.OrderId);
                string invalidReason = GetRecurringRelationshipInvalidReason(relationship, template);
                if (!string.IsNullOrWhiteSpace(invalidReason))
                {
                    relationship.RecordCancelled(weekKey, invalidReason);
                    repairs++;
                    continue;
                }

                string slotKey = BuildRecurringRelationshipSlotKey(relationship.OrderId, relationship.BuyerInstanceId);
                if (!preferredBySlot.TryGetValue(slotKey, out LocalRecurringOrderRelationshipState currentPreferred))
                {
                    preferredBySlot[slotKey] = relationship;
                    continue;
                }

                if (CompareRecurringRelationshipPriority(relationship, currentPreferred, weekKey) < 0)
                {
                    currentPreferred.RecordCancelled(weekKey, SupersededLegacyRecurringLocalOrderCancellationReason);
                    repairs++;
                    preferredBySlot[slotKey] = relationship;
                }
                else
                {
                    relationship.RecordCancelled(weekKey, SupersededLegacyRecurringLocalOrderCancellationReason);
                    repairs++;
                }
            }

            return repairs;
        }

        private static string BuildRecurringRelationshipSlotKey(string orderId, string buyerInstanceId)
        {
            return $"{orderId ?? string.Empty}:{buyerInstanceId ?? string.Empty}";
        }

        private string GetRecurringRelationshipInvalidReason(
            LocalRecurringOrderRelationshipState relationship,
            LocalRecurringOrderTemplate template)
        {
            if (relationship == null || template == null)
            {
                return "invalid recurring local order";
            }

            if (string.IsNullOrWhiteSpace(relationship.OrderId)
                || string.IsNullOrWhiteSpace(relationship.BuyerInstanceId)
                || string.IsNullOrWhiteSpace(relationship.SellerInstanceId))
            {
                return "invalid recurring local order";
            }

            BusinessInstanceState seller = FindByInstanceId(relationship.SellerInstanceId);
            if (seller == null)
            {
                return "recurring supplier missing";
            }

            if (seller.BusinessType != template.SellerType
                || string.Equals(seller.InstanceId, relationship.BuyerInstanceId, StringComparison.OrdinalIgnoreCase))
            {
                return "recurring supplier invalid";
            }

            if (template.BuyerType == BusinessType.GeneralStore)
            {
                BusinessInstanceState store = generalStoreRuntime != null ? generalStoreRuntime.CurrentBusiness : null;
                if (store != null && !string.Equals(store.InstanceId, relationship.BuyerInstanceId, StringComparison.OrdinalIgnoreCase))
                {
                    return "recurring buyer invalid";
                }

                return string.Empty;
            }

            BusinessInstanceState buyer = FindByInstanceId(relationship.BuyerInstanceId);
            if (buyer == null)
            {
                return "recurring buyer missing";
            }

            return buyer.BusinessType != template.BuyerType
                ? "recurring buyer invalid"
                : string.Empty;
        }

        private static LocalRecurringOrderTemplate FindRecurringOrderTemplate(string orderId)
        {
            if (string.IsNullOrWhiteSpace(orderId))
            {
                return null;
            }

            for (int i = 0; i < DefaultLocalRecurringOrders.Count; i++)
            {
                LocalRecurringOrderTemplate template = DefaultLocalRecurringOrders[i];
                if (template != null && string.Equals(template.OrderId, orderId, StringComparison.OrdinalIgnoreCase))
                {
                    return template;
                }
            }

            return null;
        }

        private void ApplyRecurringOrderRuntimeSummary(
            LocalRecurringOrderFulfillmentResult result,
            BusinessInstanceState seller,
            BusinessInstanceState buyer,
            LocalRecurringOrderTemplate template,
            int unitPriceCents,
            int extraBuyerUnitCostCents,
            StringBuilder summary)
        {
            if (template == null || result.Relationship == null)
            {
                return;
            }

            if (result.FulfilledUnits > 0)
            {
                int sellerRevenue = result.FulfilledUnits * Mathf.Max(0, unitPriceCents);
                string paid = FormatMoney(result.PaidCents);
                AppendTransferToOperationSummary(
                    seller,
                    $"; recurring order sold {result.FulfilledUnits} {template.SellerCategoryId} to {buyer.RuntimeDisplayName} for {FormatMoney(sellerRevenue)}");
                AppendTransferToOperationSummary(
                    buyer,
                    $"; recurring order bought {result.FulfilledUnits} {template.BuyerCategoryId} from {seller.RuntimeDisplayName} for {paid}");
                AppendSummarySegment(
                    summary,
                    $"recurring {GetBusinessSummaryLabel(seller)}->{GetBusinessSummaryLabel(buyer)} {result.FulfilledUnits}/{result.RequestedUnits} {template.BuyerCategoryId} {paid}");

                if (template.SellerType == BusinessType.Sawmill && template.BuyerType == BusinessType.LumberYard)
                {
                    SyncLinkedSawmillSupportFromBusiness(seller);
                    if (extraBuyerUnitCostCents > 0)
                    {
                        buyer.RuntimeState?.AdjustReliability01(-0.01f);
                    }
                }
            }

            if (result.Status == LocalRecurringOrderFulfillmentStatus.BuyerCashBreach)
            {
                buyer.RuntimeState?.AppendWeeklyBlockedReason($"cash-limited recurring local order: {template.BuyerCategoryId}");
                return;
            }

            if (result.Status == LocalRecurringOrderFulfillmentStatus.Short)
            {
                buyer.RuntimeState?.AppendWeeklyBlockedReason($"short recurring local order: {template.BuyerCategoryId}");
                seller.RuntimeState?.AppendWeeklyBlockedReason($"short recurring local order: {template.SellerCategoryId}");
                return;
            }

            if (result.Status == LocalRecurringOrderFulfillmentStatus.Failed)
            {
                buyer.RuntimeState?.AppendWeeklyBlockedReason($"failed recurring local order: {template.BuyerCategoryId}");
                seller.RuntimeState?.AppendWeeklyBlockedReason($"failed recurring local order: {template.SellerCategoryId}");
            }
        }

        private bool CanParticipateInRecurringLocalOrder(BusinessInstanceState business, BusinessType businessType, bool seller)
        {
            if (business == null
                || business.BusinessType != businessType
                || business.RuntimeState == null
                || GetHealthAdjustedOperatingEfficiency01(business) <= 0f
                || business.RuntimeState.LastSuspendedPayrollWorkerIds.Count > 0)
            {
                return false;
            }

            if (business.RuntimeState.RequiredWorkerCount > 0
                && business.RuntimeState.ActiveRequiredWorkerCount < business.RuntimeState.RequiredWorkerCount)
            {
                return false;
            }

            if (seller && business.BusinessType == BusinessType.Sawmill && business.RuntimeState.Reliability01 <= 0.2f)
            {
                return false;
            }

            return townWorld == null || IsBusinessAssignmentValid(business);
        }

        private int TransferCropFarmOutputs(StringBuilder summary)
        {
            int transfers = 0;
            transfers += TransferBetweenBusinesses(BusinessType.CropFarm, CategoryCropFood, BusinessType.Ranch, CategoryFeed, 6, summary);
            transfers += TransferToGeneralStore(BusinessType.CropFarm, CategoryCropFood, CategoryStapleFood, 10, summary);
            return transfers;
        }

        private int TransferRanchOutputs(StringBuilder summary)
        {
            return TransferBetweenBusinesses(BusinessType.Ranch, CategoryLivestock, BusinessType.Butcher, CategoryLivestock, 4, summary);
        }

        private int TransferButcherOutputs(StringBuilder summary)
        {
            return TransferToGeneralStore(BusinessType.Butcher, CategoryMeat, CategoryMeat, 8, summary);
        }

        private int TransferBlacksmithOutputs(StringBuilder summary)
        {
            return TransferToGeneralStore(BusinessType.Blacksmith, CategoryHardware, CategoryHardware, 6, summary);
        }

        private int TransferSawmillLumberToLumberYard(StringBuilder summary)
        {
            int total = 0;
            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState sawmill = businesses[i];
                if (!CanSellLocalLumber(sawmill, out CategoryStockState sawmillLumber))
                {
                    continue;
                }

                if (sawmill.BusinessType != BusinessType.Sawmill || sawmillLumber == null)
                {
                    continue;
                }

                int remainingForSawmill = SawmillToLumberYardWeeklyMaxUnits;
                for (int j = 0; j < businesses.Count && remainingForSawmill > 0; j++)
                {
                    BusinessInstanceState yard = businesses[j];
                    if (yard == null
                        || yard.BusinessType != BusinessType.LumberYard
                        || yard.RuntimeState == null
                        || !CanReceiveLumberYardSupply(yard, out CategoryStockState yardLumber)
                        || yardLumber == null)
                    {
                        continue;
                    }

                    int transferred = TransferSawmillLumberToLumberYard(
                        sawmill,
                        sawmillLumber,
                        yard,
                        yardLumber,
                        remainingForSawmill,
                        summary);
                    total += transferred;
                    remainingForSawmill -= transferred;
                }
            }

            return total;
        }

        private int TransferSawmillLumberToLumberYard(
            BusinessInstanceState sawmill,
            CategoryStockState sawmillLumber,
            BusinessInstanceState yard,
            CategoryStockState yardLumber,
            int maxUnits,
            StringBuilder summary)
        {
            if (sawmill == null
                || sawmill.RuntimeState == null
                || sawmillLumber == null
                || yard == null
                || yard.RuntimeState == null
                || yardLumber == null)
            {
                return 0;
            }

            int yardRoom = Mathf.Max(0, yardLumber.TargetStockUnits - yardLumber.CurrentStockUnits);
            int sourceAvailable = Mathf.Max(0, sawmillLumber.CurrentStockUnits);
            int unitPriceCents = GetAverageCategoryTransferPriceCents(sawmill, FindProfile(BusinessType.Sawmill), CategoryLumber);
            int deliveryCostPerUnit = GetSawmillToLumberYardDeliveryCostPerUnitCents(sawmill, yard);
            int reserve = GetSharedOperatingCashBufferCents(yard);
            int spendable = Mathf.Max(0, yard.RuntimeState.CurrentCashCents - reserve);
            int affordable = unitPriceCents + deliveryCostPerUnit > 0 ? spendable / (unitPriceCents + deliveryCostPerUnit) : maxUnits;
            int units = Mathf.Min(Mathf.Max(0, maxUnits), yardRoom, sourceAvailable, affordable);
            if (units <= 0)
            {
                if (yardRoom > 0 && sourceAvailable > 0 && affordable <= 0)
                {
                    yard.RuntimeState.AppendWeeklyBlockedReason("cash-limited lumber yard buying");
                    sawmill.RuntimeState.AppendWeeklyBlockedReason("lumber yard cash-limited");
                }

                return 0;
            }

            if (!sawmill.RuntimeState.TryConsumeCategoryStockUnits(CategoryLumber, units, out int consumed) || consumed <= 0)
            {
                return 0;
            }

            int accepted = yard.RuntimeState.AddCategoryStockUnits(CategoryLumber, consumed);
            if (accepted < consumed)
            {
                sawmill.RuntimeState.AddCategoryStockUnits(CategoryLumber, consumed - accepted);
            }
            SyncLinkedSawmillSupportFromBusiness(sawmill);

            int wholesaleCost = accepted * unitPriceCents;
            int deliveryCost = accepted * deliveryCostPerUnit;
            int totalCost = wholesaleCost + deliveryCost;
            if (accepted > 0 && totalCost > 0)
            {
                sawmill.RuntimeState.AddWeeklyLocalTransferRevenue(
                    wholesaleCost,
                    $"sold {accepted} lumber to {yard.RuntimeDisplayName} for {FormatMoney(wholesaleCost)}");
                yard.RuntimeState.AddWeeklyLocalTransferCost(
                    totalCost,
                    $"bought {accepted} lumber from {sawmill.RuntimeDisplayName} for {FormatMoney(totalCost)}");
            }

            if (deliveryCost > 0)
            {
                yard.RuntimeState.AdjustReliability01(-0.01f);
            }

            sawmill.RefreshCapacityState();
            yard.RefreshCapacityState();
            AppendTransferToOperationSummary(
                sawmill,
                $"; supplied {accepted} lumber to {yard.RuntimeDisplayName} for {FormatMoney(wholesaleCost)}");
            AppendTransferToOperationSummary(
                yard,
                $"; bought {accepted} lumber from {sawmill.RuntimeDisplayName} for {FormatMoney(totalCost)}");
            AppendSummarySegment(
                summary,
                $"{GetBusinessSummaryLabel(sawmill)}->{GetBusinessSummaryLabel(yard)} {accepted} lumber {FormatMoney(totalCost)}");
            return accepted;
        }

        private int ResolveLumberIndustryOffMapFallback(StringBuilder summary)
        {
            int total = 0;
            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState business = businesses[i];
                if (business == null || business.RuntimeState == null)
                {
                    continue;
                }

                if (business.BusinessType == BusinessType.Sawmill)
                {
                    total += ResolveSawmillOffMapFallback(business, summary);
                }
                else if (business.BusinessType == BusinessType.LumberYard)
                {
                    total += ResolveLumberYardOffMapFallback(business, summary);
                }
            }

            return total;
        }

        private int ResolveSawmillOffMapFallback(BusinessInstanceState sawmill, StringBuilder summary)
        {
            if (sawmill == null || sawmill.RuntimeState == null)
            {
                return 0;
            }

            logisticsRuntime ??= FindAnyObjectByType<LogisticsRuntimeManager>();

            int changed = 0;
            CategoryStockState logs = sawmill.RuntimeState.GetCategoryStock(CategorySawmillLogs);
            if (logs != null && logs.CurrentStockUnits < 4)
            {
                int targetGap = Mathf.Max(0, logs.TargetStockUnits - logs.CurrentStockUnits);
                int buyUnits = Mathf.Min(SawmillOffMapLogBuyMaxUnits, targetGap);
                int unitCost = 220;
                int reserve = GetSharedOperatingCashBufferCents(sawmill);
                int spendable = Mathf.Max(0, sawmill.RuntimeState.CurrentCashCents - reserve);
                buyUnits = Mathf.Min(buyUnits, spendable / unitCost);
                if (buyUnits > 0)
                {
                    LogisticsShipmentState shipment = logisticsRuntime != null
                        ? logisticsRuntime.CreateOffMapInboundShipment(
                            sawmill,
                            CategorySawmillLogs,
                            buyUnits,
                            unitCost,
                            LogisticsShipmentDeliveryMode.AddCategoryStock,
                            $"{sawmill.RuntimeDisplayName} off-map logs")
                        : null;
                    if (shipment != null && shipment.RoutePlan != null && shipment.RoutePlan.VisiblePath != null && shipment.RoutePlan.VisiblePath.Count > 0)
                    {
                        AppendTransferToOperationSummary(sawmill, $"; queued {buyUnits} off-map logs");
                        AppendSummarySegment(summary, $"{GetBusinessSummaryLabel(sawmill)} off-map logs queued {buyUnits}");
                        changed += buyUnits;
                    }
                }
                else if (targetGap > 0)
                {
                    sawmill.RuntimeState.AppendWeeklyBlockedReason("cash-limited off-map log buying");
                }
            }

            CategoryStockState lumber = sawmill.RuntimeState.GetCategoryStock(CategoryLumber);
            if (lumber != null)
            {
                int reserveStock = Mathf.Min(160, Mathf.Max(0, lumber.TargetStockUnits / 2));
                int sellUnits = Mathf.Min(SawmillOffMapLumberSellMaxUnits, Mathf.Max(0, lumber.CurrentStockUnits - reserveStock));
                if (sellUnits > 0)
                {
                    LogisticsShipmentState shipment = logisticsRuntime != null
                        ? logisticsRuntime.CreateOffMapOutboundShipment(
                            sawmill,
                            CategoryLumber,
                            sellUnits,
                            65,
                            $"{sawmill.RuntimeDisplayName} surplus lumber",
                            true)
                        : null;
                    if (shipment != null
                        && shipment.RoutePlan != null
                        && shipment.RoutePlan.VisiblePath != null
                        && shipment.RoutePlan.VisiblePath.Count > 0
                        && sawmill.RuntimeState.TryConsumeCategoryStockUnits(CategoryLumber, sellUnits, out int consumed)
                        && consumed > 0)
                    {
                        AppendTransferToOperationSummary(sawmill, $"; queued {consumed} surplus lumber off-map");
                        AppendSummarySegment(summary, $"{GetBusinessSummaryLabel(sawmill)} off-map lumber queued {consumed}");
                        changed += consumed;
                    }
                }
            }

            if (changed > 0)
            {
                SyncLinkedSawmillSupportFromBusiness(sawmill);
            }

            sawmill.RefreshCapacityState();
            return changed;
        }

        private int ResolveLumberYardOffMapFallback(BusinessInstanceState yard, StringBuilder summary)
        {
            if (yard == null || yard.RuntimeState == null)
            {
                return 0;
            }

            logisticsRuntime ??= FindAnyObjectByType<LogisticsRuntimeManager>();

            CategoryStockState lumber = yard.RuntimeState.GetCategoryStock(CategoryLumber);
            if (lumber == null)
            {
                return 0;
            }

            int changed = 0;
            int lowStockFloor = Mathf.CeilToInt(lumber.TargetStockUnits * 0.35f);
            if (lumber.CurrentStockUnits < lowStockFloor)
            {
                int buyUnits = Mathf.Min(LumberYardOffMapBuyMaxUnits, Mathf.Max(0, lumber.TargetStockUnits - lumber.CurrentStockUnits));
                int unitCost = 115;
                int reserve = GetSharedOperatingCashBufferCents(yard);
                int spendable = Mathf.Max(0, yard.RuntimeState.CurrentCashCents - reserve);
                buyUnits = Mathf.Min(buyUnits, spendable / unitCost);
                if (buyUnits > 0)
                {
                    LogisticsShipmentState shipment = logisticsRuntime != null
                        ? logisticsRuntime.CreateOffMapInboundShipment(
                            yard,
                            CategoryLumber,
                            buyUnits,
                            unitCost,
                            LogisticsShipmentDeliveryMode.AddCategoryStock,
                            $"{yard.RuntimeDisplayName} off-map lumber")
                        : null;
                    if (shipment != null && shipment.RoutePlan != null && shipment.RoutePlan.VisiblePath != null && shipment.RoutePlan.VisiblePath.Count > 0)
                    {
                        AppendTransferToOperationSummary(yard, $"; queued {buyUnits} off-map lumber");
                        AppendSummarySegment(summary, $"{GetBusinessSummaryLabel(yard)} off-map lumber queued {buyUnits}");
                        changed += buyUnits;
                    }
                }
                else
                {
                    yard.RuntimeState.AppendWeeklyBlockedReason("cash-limited off-map lumber buying");
                }
            }

            int reserveCash = GetSharedOperatingCashBufferCents(yard);
            if (yard.RuntimeState.CurrentCashCents < reserveCash)
            {
                int sellUnits = Mathf.Min(LumberYardOffMapSellMaxUnits, Mathf.Max(0, lumber.CurrentStockUnits - lowStockFloor));
                if (sellUnits > 0)
                {
                    LogisticsShipmentState shipment = logisticsRuntime != null
                        ? logisticsRuntime.CreateOffMapOutboundShipment(
                            yard,
                            CategoryLumber,
                            sellUnits,
                            80,
                            $"{yard.RuntimeDisplayName} off-map lumber",
                            true)
                        : null;
                    if (shipment != null
                        && shipment.RoutePlan != null
                        && shipment.RoutePlan.VisiblePath != null
                        && shipment.RoutePlan.VisiblePath.Count > 0
                        && yard.RuntimeState.TryConsumeCategoryStockUnits(CategoryLumber, sellUnits, out int consumed)
                        && consumed > 0)
                    {
                        AppendTransferToOperationSummary(yard, $"; queued {consumed} lumber off-map");
                        AppendSummarySegment(summary, $"{GetBusinessSummaryLabel(yard)} off-map lumber queued {consumed}");
                        changed += consumed;
                    }
                }
            }

            yard.RefreshCapacityState();
            return changed;
        }

        private int ResolveLumberYardLocalBuilderDemand(StringBuilder summary)
        {
            int total = 0;
            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState yard = businesses[i];
                if (yard == null || yard.BusinessType != BusinessType.LumberYard || yard.RuntimeState == null)
                {
                    continue;
                }

                total += ResolveLumberYardLocalBuilderDemand(yard, summary);
            }

            return total;
        }

        private int ResolveLumberYardLocalBuilderDemand(BusinessInstanceState yard, StringBuilder summary)
        {
            BusinessRuntimeState runtime = yard != null ? yard.RuntimeState : null;
            if (yard == null || runtime == null)
            {
                return 0;
            }

            CategoryStockState lumber = runtime.GetCategoryStock(CategoryLumber);
            if (lumber == null)
            {
                return 0;
            }

            float efficiency01 = GetHealthAdjustedOperatingEfficiency01(yard);
            int requestedUnits = Mathf.RoundToInt(LumberYardLocalBuilderDemandMaxUnits * efficiency01);
            List<string> blockedReasons = new();
            SeedBlockedReasons(runtime, blockedReasons);
            AppendStaffingBlockedReasons(runtime, blockedReasons);
            if (requestedUnits <= 0)
            {
                AddBlockedReason(blockedReasons, "no staffed lumber yard sales capacity");
            }

            if (lumber.CurrentStockUnits <= 0)
            {
                AddBlockedReason(blockedReasons, "no stocked lumber for local builders");
            }

            int soldUnits = 0;
            int revenue = 0;
            if (requestedUnits > 0 && lumber.CurrentStockUnits > 0)
            {
                int units = Mathf.Min(requestedUnits, lumber.CurrentStockUnits);
                if (runtime.TryConsumeCategoryStockUnits(CategoryLumber, units, out int consumed) && consumed > 0)
                {
                    soldUnits = consumed;
                    int unitPrice = GetAverageCategoryTransferPriceCents(yard, FindProfile(BusinessType.LumberYard), CategoryLumber);
                    revenue = soldUnits * unitPrice;
                    runtime.AddWeeklyLocalTransferRevenue(
                        revenue,
                        $"sold {soldUnits} lumber to local builder demand for {FormatMoney(revenue)}");
                }
            }

            yard.ApplyWeeklyOperationResult(soldUnits);
            yard.ApplyDailyServiceResult(0);
            yard.RefreshCapacityState();

            string blocked = BuildBlockedReason(blockedReasons);
            string result = $"sold {soldUnits}/{Mathf.Max(0, requestedUnits)} lumber to local builder demand for {FormatMoney(revenue)}";
            runtime.RecordWeeklyOperation(0, soldUnits, result, blocked);
            RecordOperationSummary(yard, string.IsNullOrWhiteSpace(blocked) ? result : $"{result}; blocked: {blocked}");
            if (soldUnits > 0)
            {
                AppendSummarySegment(summary, $"{GetBusinessSummaryLabel(yard)} local builders {soldUnits} {FormatMoney(revenue)}");
            }

            return soldUnits;
        }

        private int GetSawmillToLumberYardDeliveryCostPerUnitCents(BusinessInstanceState sawmill, BusinessInstanceState yard)
        {
            bool sameOwner = HaveSameOwner(sawmill, yard);
            if (sameOwner && linkedSawmillSupportNode != null && linkedSawmillSupportNode.InternalHaulingOwned)
            {
                return IsWorkerSlotActive(sawmill.RuntimeState, "yard_teamster") ? 0 : 3;
            }

            int baseCost = linkedSawmillSupportNode != null && linkedSawmillSupportNode.InternalHaulingOwned ? 6 : 12;
            float liverySupport01 = GetOperationalLiverySupport01();
            return Mathf.Max(0, Mathf.RoundToInt(baseCost * Mathf.Lerp(1f, 0.5f, liverySupport01)));
        }

        public int GetBuilderLaborCapacityUnits()
        {
            int total = 0;
            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState business = businesses[i];
                if (business == null || business.BusinessType != BusinessType.Builder || business.RuntimeState == null)
                {
                    continue;
                }

                total += Mathf.RoundToInt(Mathf.Max(0, business.BaselineDailyServiceCapacity) * GetHealthAdjustedOperatingEfficiency01(business));
            }

            return total * 4;
        }

        public float GetOperationalLiverySupport01()
        {
            float best = 0f;
            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState business = businesses[i];
                if (business == null || business.BusinessType != BusinessType.LiveryFreight || business.RuntimeState == null)
                {
                    continue;
                }

                best = Mathf.Max(best, GetHealthAdjustedOperatingEfficiency01(business));
            }

            return Mathf.Clamp01(best);
        }

        private static bool HaveSameOwner(BusinessInstanceState a, BusinessInstanceState b)
        {
            if (a == null || b == null || a.Owner == null || b.Owner == null)
            {
                return false;
            }

            return a.Owner.OwnerKind == b.Owner.OwnerKind
                && a.Owner.PersonId == b.Owner.PersonId
                && string.Equals(a.Owner.DisplayName, b.Owner.DisplayName, StringComparison.OrdinalIgnoreCase);
        }

        private int SellLocalMarketOutput(
            BusinessType sourceType,
            string sourceCategoryId,
            int maxUnits,
            StringBuilder summary)
        {
            int total = 0;
            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState source = businesses[i];
                if (!CanParticipateInWeeklyTransfer(source, sourceType))
                {
                    continue;
                }

                total += SellLocalMarketOutput(source, sourceCategoryId, maxUnits, summary);
            }

            return total;
        }

        private int SellLocalMarketOutput(
            BusinessInstanceState source,
            string sourceCategoryId,
            int maxUnits,
            StringBuilder summary)
        {
            if (source == null || source.RuntimeState == null)
            {
                return 0;
            }

            BusinessProfileDefinition sourceProfile = FindProfile(source.BusinessType);
            int unitPriceCents = GetAverageCategoryTransferPriceCents(source, sourceProfile, sourceCategoryId);
            CategoryStockState sourceStock = source.RuntimeState.GetCategoryStock(sourceCategoryId);
            int baselineUnitCostCents = GetAverageCategoryLandedCostCents(sourceProfile, sourceCategoryId);
            float capture01 = CalculateLocalMarketCapture01(source, sourceCategoryId, unitPriceCents, baselineUnitCostCents);
            int localDemandUnits = Mathf.RoundToInt(Mathf.Max(0, maxUnits) * capture01);
            int offMapOrCompetitorUnits = Mathf.Max(0, Mathf.Max(0, maxUnits) - localDemandUnits);
            int units = sourceStock != null ? Mathf.Min(localDemandUnits, sourceStock.CurrentStockUnits) : 0;
            if (units <= 0 || unitPriceCents <= 0)
            {
                if (offMapOrCompetitorUnits > 0)
                {
                    source.RuntimeState.AppendWeeklyTransferSummary($"{offMapOrCompetitorUnits} {sourceCategoryId} demand went off-map or to competitors at current price");
                }

                return 0;
            }

            if (!source.RuntimeState.TryConsumeCategoryStockUnits(sourceCategoryId, units, out int consumed) || consumed <= 0)
            {
                return 0;
            }

            int revenue = consumed * unitPriceCents;
            source.RuntimeState.AddWeeklyLocalTransferRevenue(
                revenue,
                $"sold {consumed} {sourceCategoryId} through local market for {FormatMoney(revenue)}; {offMapOrCompetitorUnits} demand went off-map/competitors");
            source.RefreshCapacityState();
            AppendTransferToOperationSummary(source, $"; sold {consumed} {sourceCategoryId} through local market for {FormatMoney(revenue)}; {offMapOrCompetitorUnits} off-map/competitors");
            AppendSummarySegment(summary, $"{GetBusinessSummaryLabel(source)}->LocalMarket {consumed} {FormatMoney(revenue)} ({capture01:P0} capture)");
            return consumed;
        }

        private int TransferBetweenBusinesses(
            BusinessType sourceType,
            string sourceCategoryId,
            BusinessType destinationType,
            string destinationCategoryId,
            int maxUnits,
            StringBuilder summary)
        {
            int total = 0;
            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState source = businesses[i];
                if (!CanParticipateInWeeklyTransfer(source, sourceType))
                {
                    continue;
                }

                int remainingForSource = Mathf.Max(0, maxUnits);
                if (remainingForSource <= 0)
                {
                    continue;
                }

                for (int j = 0; j < businesses.Count && remainingForSource > 0; j++)
                {
                    BusinessInstanceState destination = businesses[j];
                    if (!CanParticipateInWeeklyTransfer(destination, destinationType))
                    {
                        continue;
                    }

                    int transferred = TransferBetweenBusinesses(
                        source,
                        sourceCategoryId,
                        destination,
                        destinationCategoryId,
                        remainingForSource,
                        summary);
                    total += transferred;
                    remainingForSource -= transferred;
                }
            }

            return total;
        }

        private int TransferBetweenBusinesses(
            BusinessInstanceState source,
            string sourceCategoryId,
            BusinessInstanceState destination,
            string destinationCategoryId,
            int maxUnits,
            StringBuilder summary)
        {
            if (source == null || destination == null || source.RuntimeState == null || destination.RuntimeState == null)
            {
                return 0;
            }

            logisticsRuntime ??= FindAnyObjectByType<LogisticsRuntimeManager>();
            if (logisticsRuntime == null)
            {
                return 0;
            }

            BusinessProfileDefinition sourceProfile = FindProfile(source.BusinessType);
            int unitPriceCents = GetAverageCategoryTransferPriceCents(source, sourceProfile, sourceCategoryId);
            int destinationCashReserve = GetSharedSpendingReserveCents(destination);
            int units = CalculateTransferUnits(
                source.RuntimeState,
                sourceCategoryId,
                destination.RuntimeState,
                destinationCategoryId,
                maxUnits,
                unitPriceCents,
                destinationCashReserve);
            if (units <= 0)
            {
                AppendCashLimitedTransferReason(
                    source.RuntimeState,
                    sourceCategoryId,
                    destination.RuntimeState,
                    destinationCategoryId,
                    maxUnits,
                    unitPriceCents,
                    destinationCashReserve);
                return 0;
            }

            LogisticsShipmentState shipment = logisticsRuntime.CreateLocalBusinessShipment(
                source,
                sourceCategoryId,
                destination,
                destinationCategoryId,
                units,
                unitPriceCents,
                unitPriceCents,
                true,
                LogisticsShipmentDeliveryMode.AddCategoryStock,
                $"{source.RuntimeDisplayName}->{destination.RuntimeDisplayName}");
            if (shipment == null
                || shipment.RoutePlan == null
                || shipment.RoutePlan.VisiblePath == null
                || shipment.RoutePlan.VisiblePath.Count <= 0)
            {
                return 0;
            }

            if (!source.RuntimeState.TryConsumeCategoryStockUnits(sourceCategoryId, units, out int consumed) || consumed <= 0)
            {
                return 0;
            }

            source.RefreshCapacityState();
            destination.RefreshCapacityState();
            if (consumed > 0)
            {
                int transferCost = consumed * unitPriceCents;
                AppendTransferToOperationSummary(source, $"; shipped {consumed} {sourceCategoryId} to {destination.RuntimeDisplayName} ETA {FormatGameSeconds(shipment.EstimatedRemainingGameSeconds)}");
                AppendTransferToOperationSummary(destination, $"; inbound {consumed} {destinationCategoryId} from {source.RuntimeDisplayName} ETA {FormatGameSeconds(shipment.EstimatedRemainingGameSeconds)}");
                AppendSummarySegment(summary, $"{GetBusinessSummaryLabel(source)}->{GetBusinessSummaryLabel(destination)} queued {consumed} {FormatMoney(transferCost)}");
            }

            return consumed;
        }

        private int TransferToGeneralStore(
            BusinessType sourceType,
            string sourceCategoryId,
            string storeCategoryId,
            int maxUnits,
            StringBuilder summary)
        {
            AutoWire();
            if (generalStoreRuntime == null || !generalStoreRuntime.InitializeIfNeeded() || generalStoreRuntime.RuntimeState == null)
            {
                return 0;
            }

            int total = 0;
            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState source = businesses[i];
                if (!CanParticipateInWeeklyTransfer(source, sourceType))
                {
                    continue;
                }

                total += TransferToGeneralStore(source, sourceCategoryId, storeCategoryId, maxUnits, summary);
            }

            return total;
        }

        private int TransferToGeneralStore(
            BusinessInstanceState source,
            string sourceCategoryId,
            string storeCategoryId,
            int maxUnits,
            StringBuilder summary)
        {
            if (source == null || source.RuntimeState == null)
            {
                return 0;
            }

            logisticsRuntime ??= FindAnyObjectByType<LogisticsRuntimeManager>();
            if (logisticsRuntime == null)
            {
                return 0;
            }

            if (GetHealthAdjustedOperatingEfficiency01(source) <= 0f)
            {
                generalStoreRuntime?.RecordLocalSupplySkip(storeCategoryId, source.RuntimeDisplayName, "seller inactive");
                source.RuntimeState.AppendWeeklyBlockedReason($"inactive local supply seller: {sourceCategoryId}");
                return 0;
            }

            BusinessProfileDefinition sourceProfile = FindProfile(source.BusinessType);
            int unitPriceCents = GetGeneralStoreLocalSupplyPriceCents(source, sourceProfile, sourceCategoryId, storeCategoryId);
            int storeCashReserve = generalStoreRuntime.ProtectedBusinessCashReserveCents;
            int units = CalculateTransferUnits(
                source.RuntimeState,
                sourceCategoryId,
                generalStoreRuntime.RuntimeState,
                storeCategoryId,
                maxUnits,
                unitPriceCents,
                storeCashReserve);
            if (units <= 0)
            {
                generalStoreRuntime.RecordLocalSupplySkip(
                    storeCategoryId,
                    source.RuntimeDisplayName,
                    BuildGeneralStoreSupplySkipReason(
                        source.RuntimeState,
                        sourceCategoryId,
                        generalStoreRuntime.RuntimeState,
                        storeCategoryId,
                        maxUnits,
                        unitPriceCents,
                        storeCashReserve));
                return 0;
            }

            LogisticsShipmentState shipment = logisticsRuntime.CreateLocalBusinessShipment(
                source,
                sourceCategoryId,
                generalStoreRuntime.CurrentBusiness,
                storeCategoryId,
                units,
                unitPriceCents,
                unitPriceCents,
                true,
                LogisticsShipmentDeliveryMode.GeneralStoreLocalSupply,
                $"{source.RuntimeDisplayName}->General Store");
            if (shipment == null
                || shipment.RoutePlan == null
                || shipment.RoutePlan.VisiblePath == null
                || shipment.RoutePlan.VisiblePath.Count <= 0)
            {
                return 0;
            }

            if (!source.RuntimeState.TryConsumeCategoryStockUnits(sourceCategoryId, units, out int consumed) || consumed <= 0)
            {
                return 0;
            }

            source.RefreshCapacityState();
            if (consumed > 0)
            {
                int transferRevenue = consumed * unitPriceCents;
                AppendTransferToOperationSummary(source, $"; shipped {consumed} {sourceCategoryId} to General Store ETA {FormatGameSeconds(shipment.EstimatedRemainingGameSeconds)}");
                AppendSummarySegment(summary, $"{GetBusinessSummaryLabel(source)}->GeneralStore queued {consumed} {FormatMoney(transferRevenue)}");
            }

            return consumed;
        }

        private sealed class LumberSellerCandidate
        {
            public BusinessInstanceState business;
            public string sourceLabel = string.Empty;
            public int stockUnits;
            public int unitPriceCents;
            public int score;
        }

        private sealed class LumberConsumptionPlan
        {
            public readonly BusinessInstanceState seller;
            public readonly int units;
            public readonly int unitCostCents;

            public LumberConsumptionPlan(BusinessInstanceState seller, int units, int unitCostCents)
            {
                this.seller = seller;
                this.units = Mathf.Max(0, units);
                this.unitCostCents = Mathf.Max(0, unitCostCents);
            }
        }

        private List<LumberSellerCandidate> BuildLumberSellerCandidates(int buyerPlotId, int requestedUnits, int baselineUnitCostCents)
        {
            List<LumberSellerCandidate> candidates = new();
            TownPlot buyerPlot = GetPlot(buyerPlotId);
            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState business = businesses[i];
                if (business == null
                    || (business.BusinessType != BusinessType.Sawmill && business.BusinessType != BusinessType.LumberYard)
                    || !CanSellLocalLumber(business, out CategoryStockState stock)
                    || stock == null)
                {
                    continue;
                }

                BusinessProfileDefinition profile = FindProfile(business.BusinessType);
                int unitPriceCents = GetAverageCategoryTransferPriceCents(business, profile, CategoryLumber);
                int score = CalculateLumberSellerScore(
                    business,
                    stock.CurrentStockUnits,
                    buyerPlot,
                    unitPriceCents,
                    baselineUnitCostCents,
                    requestedUnits);
                candidates.Add(new LumberSellerCandidate
                {
                    business = business,
                    sourceLabel = BuildLumberSellerLabel(business),
                    stockUnits = stock.CurrentStockUnits,
                    unitPriceCents = Mathf.Max(1, unitPriceCents),
                    score = score
                });
            }

            candidates.Sort((a, b) =>
            {
                int scoreCompare = a.score.CompareTo(b.score);
                if (scoreCompare != 0)
                {
                    return scoreCompare;
                }

                return string.Compare(a.sourceLabel, b.sourceLabel, StringComparison.OrdinalIgnoreCase);
            });
            return candidates;
        }

        private bool CanSellLocalLumber(BusinessInstanceState business, out CategoryStockState stock)
        {
            stock = null;
            if (business == null
                || business.RuntimeState == null
                || GetHealthAdjustedOperatingEfficiency01(business) <= 0f
                || business.RuntimeState.Reliability01 <= 0.2f
                || business.RuntimeState.LastSuspendedPayrollWorkerIds.Count > 0
                || !IsBusinessAssignmentValid(business))
            {
                return false;
            }

            if (business.RuntimeState.RequiredWorkerCount > 0
                && business.RuntimeState.ActiveRequiredWorkerCount < business.RuntimeState.RequiredWorkerCount)
            {
                return false;
            }

            stock = business.RuntimeState.GetCategoryStock(CategoryLumber);
            if (stock == null || stock.CurrentStockUnits <= 0)
            {
                return false;
            }

            if (business.BusinessType == BusinessType.Sawmill
                && linkedSawmillSupportNode != null
                && !linkedSawmillSupportNode.RemoteProductionEnabled)
            {
                return false;
            }

            return true;
        }

        private bool CanReceiveLumberYardSupply(BusinessInstanceState business, out CategoryStockState stock)
        {
            stock = null;
            if (business == null
                || business.BusinessType != BusinessType.LumberYard
                || business.RuntimeState == null
                || GetHealthAdjustedOperatingEfficiency01(business) <= 0f
                || business.RuntimeState.LastSuspendedPayrollWorkerIds.Count > 0
                || !IsBusinessAssignmentValid(business))
            {
                return false;
            }

            stock = business.RuntimeState.GetCategoryStock(CategoryLumber);
            return stock != null && stock.CurrentStockUnits < stock.TargetStockUnits;
        }

        private int CalculateLumberSellerScore(
            BusinessInstanceState business,
            int stockUnits,
            TownPlot buyerPlot,
            int unitPriceCents,
            int baselineUnitCostCents,
            int requestedUnits)
        {
            TownPlot sellerPlot = GetSellerPlot(business);
            int routeDistanceCells = EstimateRouteDistanceCells(buyerPlot, sellerPlot);
            int baseline = Mathf.Max(1, baselineUnitCostCents);
            int pricePenaltyCells = Mathf.RoundToInt(Mathf.Clamp(((unitPriceCents - baseline) / (float)baseline) * 24f, -12f, 24f));
            int reliabilityPenaltyCells = Mathf.RoundToInt((1f - business.RuntimeState.Reliability01) * 24f);
            bool townBuyer = buyerPlot == null || buyerPlot.zone != PlotZone.Agricultural;
            int deliveryPenaltyCells = 0;
            int sellerRoleBiasCells = 0;

            if (business.BusinessType == BusinessType.Sawmill)
            {
                deliveryPenaltyCells += townBuyer ? 14 : 4;
                if (linkedSawmillSupportNode != null && !linkedSawmillSupportNode.InternalHaulingOwned)
                {
                    deliveryPenaltyCells += 8;
                }
            }
            else if (business.BusinessType == BusinessType.LumberYard)
            {
                sellerRoleBiasCells = townBuyer ? -10 : 0;
            }

            int fullOrderCoverageBonusCells = requestedUnits > 0 && stockUnits >= requestedUnits ? -6 : 0;
            return routeDistanceCells
                + pricePenaltyCells
                + reliabilityPenaltyCells
                + deliveryPenaltyCells
                + sellerRoleBiasCells
                + fullOrderCoverageBonusCells;
        }

        private TownPlot GetSellerPlot(BusinessInstanceState business)
        {
            if (business == null || townWorld == null || business.AssignedBuildingId < 0 || business.AssignedBuildingId >= townWorld.Buildings.Count)
            {
                return null;
            }

            PlacedBuilding building = townWorld.Buildings[business.AssignedBuildingId];
            return GetPlot(building != null ? building.plotId : -1);
        }

        private static int EstimateRouteDistanceCells(TownPlot buyerPlot, TownPlot sellerPlot)
        {
            if (buyerPlot == null || sellerPlot == null)
            {
                return 24;
            }

            GridCoord buyer = buyerPlot.roadAccessCell;
            GridCoord seller = sellerPlot.roadAccessCell;
            return Mathf.Abs(buyer.x - seller.x) + Mathf.Abs(buyer.z - seller.z);
        }

        private BusinessInstanceState FindByInstanceId(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                return null;
            }

            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState business = businesses[i];
                if (business != null && string.Equals(business.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase))
                {
                    return business;
                }
            }

            return null;
        }

        private BusinessTransferAgreementState FindTransferAgreementById(string agreementId)
        {
            if (string.IsNullOrWhiteSpace(agreementId))
            {
                return null;
            }

            for (int i = 0; i < transferAgreements.Count; i++)
            {
                BusinessTransferAgreementState agreement = transferAgreements[i];
                if (agreement != null && string.Equals(agreement.AgreementId, agreementId, StringComparison.OrdinalIgnoreCase))
                {
                    return agreement;
                }
            }

            return null;
        }

        private static bool IsOwnedTransferEndpoint(BusinessInstanceState business)
        {
            return business != null
                && business.Owner != null
                && business.Owner.OwnerKind == BusinessOwnerKind.Player;
        }

        private static string BuildLumberSellerLabel(BusinessInstanceState business)
        {
            if (business == null)
            {
                return "Lumber seller";
            }

            return business.BusinessType == BusinessType.LumberYard
                ? $"{business.RuntimeDisplayName} town yard"
                : $"{business.RuntimeDisplayName} remote sawmill";
        }

        private static string BuildLumberSellerSourceLabel(
            IReadOnlyList<LumberSellerCandidate> candidates,
            IReadOnlyList<ConstructionResourceSourceAllocation> allocations)
        {
            if (allocations != null && allocations.Count > 0)
            {
                if (allocations.Count == 1)
                {
                    return allocations[0].sourceLabel;
                }

                bool hasYard = false;
                bool hasSawmill = false;
                for (int i = 0; i < allocations.Count; i++)
                {
                    hasYard |= allocations[i].businessType == BusinessType.LumberYard;
                    hasSawmill |= allocations[i].businessType == BusinessType.Sawmill;
                }

                if (hasYard && hasSawmill)
                {
                    return "Lumber Yard + Small Sawmill";
                }

                return hasYard ? "Lumber Yard sellers" : "Small Sawmill sellers";
            }

            if (candidates == null || candidates.Count == 0)
            {
                return "Shared lumber sellers";
            }

            return candidates.Count == 1 ? candidates[0].sourceLabel : "Shared lumber sellers";
        }

        private static bool CanParticipateInWeeklyTransfer(BusinessInstanceState business, BusinessType businessType)
        {
            return business != null
                && business.BusinessType == businessType
                && business.RuntimeState != null;
        }

        private sealed class HouseholdReserveSellerCandidate
        {
            public BusinessInstanceState business;
            public string sellerCategoryId;
            public BusinessReputationSellerChoiceScore choiceScore;
        }

        private List<HouseholdReserveSellerCandidate> BuildHouseholdReserveSellerCandidates(
            string reserveCategoryId,
            IReadOnlyList<string> sellerCategoryIds,
            int requestedUnits,
            int remainingBudgetCents,
            int householdId)
        {
            List<HouseholdReserveSellerCandidate> candidates = new();
            int request = Mathf.Max(0, requestedUnits);
            int budget = Mathf.Max(0, remainingBudgetCents);
            if (request <= 0 || budget <= 0 || sellerCategoryIds == null || sellerCategoryIds.Count == 0)
            {
                return candidates;
            }

            int candidateIndex = 0;
            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState seller = businesses[i];
                if (!CanSellHouseholdReserveUnits(seller))
                {
                    continue;
                }

                BusinessProfileDefinition profile = FindProfile(seller.BusinessType);
                if (profile == null || profile.Business == null)
                {
                    continue;
                }

                for (int categoryIndex = 0; categoryIndex < sellerCategoryIds.Count; categoryIndex++)
                {
                    string sellerCategoryId = sellerCategoryIds[categoryIndex];
                    if (string.IsNullOrWhiteSpace(sellerCategoryId) || !profile.Business.OwnsCategory(sellerCategoryId))
                    {
                        continue;
                    }

                    CategoryStockState stock = seller.RuntimeState.GetCategoryStock(sellerCategoryId);
                    if (stock == null || stock.CurrentStockUnits <= 0)
                    {
                        continue;
                    }

                    int unitPriceCents = GetAverageCategoryTransferPriceCents(seller, profile, sellerCategoryId);
                    if (unitPriceCents <= 0 || unitPriceCents > budget)
                    {
                        continue;
                    }

                    seller.EnsureBusinessReputationInitializedFromRuntime();
                    int referenceUnitPriceCents = GetAverageCategoryLandedCostCents(profile, sellerCategoryId);
                    BusinessReputationSellerChoiceScore score = businessReputationSellerChoice.Evaluate(
                        new BusinessReputationSellerChoiceInput(
                            seller.BusinessReputation,
                            stock.StockHealth01,
                            stock.CurrentStockUnits,
                            unitPriceCents,
                            referenceUnitPriceCents,
                            seller.RuntimeState.Reliability01,
                            GetHealthAdjustedOperatingEfficiency01(seller),
                            seller.RuntimeDisplayName,
                            seller.InstanceId,
                            candidateIndex,
                            sellerCategoryId));
                    score = ApplyHouseholdAffinityToSellerChoiceScore(score, householdId, seller);
                    if (!score.Eligible)
                    {
                        candidateIndex++;
                        continue;
                    }

                    candidates.Add(new HouseholdReserveSellerCandidate
                    {
                        business = seller,
                        sellerCategoryId = sellerCategoryId,
                        choiceScore = score
                    });
                    candidateIndex++;
                }
            }

            candidates.Sort((left, right) => BusinessReputationSellerChoice.Compare(left.choiceScore, right.choiceScore));
            return candidates;
        }

        private bool CanSellHouseholdReserveUnits(BusinessInstanceState business)
        {
            return business != null
                && business.BusinessType != BusinessType.GeneralStore
                && business.RuntimeState != null
                && GetHealthAdjustedOperatingEfficiency01(business) > 0f;
        }

        private BusinessInstanceState FindBestOperationalDoctor()
        {
            BusinessInstanceState best = null;
            float bestEfficiency = 0f;
            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState business = businesses[i];
                if (business == null
                    || business.BusinessType != BusinessType.Doctor
                    || business.RuntimeState == null
                    || (townWorld != null && !IsBusinessAssignmentValid(business)))
                {
                    continue;
                }

                float efficiency = GetHealthAdjustedOperatingEfficiency01(business);
                if (efficiency <= 0f)
                {
                    continue;
                }

                if (best == null || efficiency > bestEfficiency)
                {
                    best = business;
                    bestEfficiency = efficiency;
                }
            }

            return best;
        }

        private static List<PersonState> BuildDoctorPatientList(PopulationState population)
        {
            List<PersonState> patients = new();
            if (population == null || population.people == null)
            {
                return patients;
            }

            for (int i = 0; i < population.people.Count; i++)
            {
                PersonState person = population.people[i];
                if (PopulationHealthEvaluator.HasActiveCondition(person))
                {
                    patients.Add(person);
                }
            }

            patients.Sort(CompareDoctorPatients);
            return patients;
        }

        private static int CompareDoctorPatients(PersonState left, PersonState right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            int laborCompare = PopulationHealthEvaluator.IsLaborMarketWorker(right).CompareTo(PopulationHealthEvaluator.IsLaborMarketWorker(left));
            if (laborCompare != 0)
            {
                return laborCompare;
            }

            PersonHealthState leftHealth = PopulationHealthEvaluator.EnsureHealthInitialized(left);
            PersonHealthState rightHealth = PopulationHealthEvaluator.EnsureHealthInitialized(right);
            int severityCompare = rightHealth.severity.CompareTo(leftHealth.severity);
            if (severityCompare != 0)
            {
                return severityCompare;
            }

            int durationCompare = rightHealth.remainingDays.CompareTo(leftHealth.remainingDays);
            if (durationCompare != 0)
            {
                return durationCompare;
            }

            return left.id.CompareTo(right.id);
        }

        private static int CountActiveHealthCases(PopulationState population)
        {
            if (population == null || population.people == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < population.people.Count; i++)
            {
                if (PopulationHealthEvaluator.HasActiveCondition(population.people[i]))
                {
                    count++;
                }
            }

            return count;
        }

        private int TrySellHouseholdReserveUnitsFromBusiness(
            BusinessInstanceState seller,
            string reserveCategoryId,
            string sellerCategoryId,
            int requestedUnits,
            ref int remainingBudgetCents,
            int householdId,
            out int spendCents)
        {
            spendCents = 0;
            if (seller == null
                || seller.RuntimeState == null
                || string.IsNullOrWhiteSpace(sellerCategoryId)
                || requestedUnits <= 0
                || remainingBudgetCents <= 0)
            {
                return 0;
            }

            BusinessProfileDefinition profile = FindProfile(seller.BusinessType);
            if (profile == null || profile.Business == null || !profile.Business.OwnsCategory(sellerCategoryId))
            {
                return 0;
            }

            CategoryStockState stock = seller.RuntimeState.GetCategoryStock(sellerCategoryId);
            if (stock == null || stock.CurrentStockUnits <= 0)
            {
                return 0;
            }

            int unitPriceCents = GetAverageCategoryTransferPriceCents(seller, profile, sellerCategoryId);
            if (unitPriceCents <= 0)
            {
                return 0;
            }

            int staffedUnits = ScaleHouseholdSaleUnitsByOperatingEfficiency(seller, requestedUnits);
            int budgetUnits = Mathf.Max(0, remainingBudgetCents / unitPriceCents);
            int unitsSold = Mathf.Min(Mathf.Max(0, requestedUnits), staffedUnits, budgetUnits, stock.CurrentStockUnits);
            if (unitsSold <= 0)
            {
                return 0;
            }

            int revenue = unitsSold * unitPriceCents;
            seller.RuntimeState.ResolveDailySalesPlaceholder(sellerCategoryId, unitsSold, revenue);
            remainingBudgetCents -= revenue;
            spendCents = revenue;
            seller.RefreshCapacityState();
            seller.RecordBusinessReputationObservation(
                new BusinessReputationObservation(
                    requestedUnits,
                    unitsSold,
                    unitPriceCents,
                    GetAverageCategoryLandedCostCents(profile, sellerCategoryId),
                    stock.StockHealth01,
                    seller.RuntimeState.Reliability01,
                    GetHealthAdjustedOperatingEfficiency01(seller),
                    sellerCategoryId,
                    timeManager != null ? timeManager.CurrentDate.AbsoluteDayIndex : -1,
                    GetCurrentWeekKey()));
            RecordHouseholdAffinity(householdId, seller, reserveCategoryId, sellerCategoryId, requestedUnits, unitsSold, revenue, unitPriceCents);
            AppendTransferToOperationSummary(
                seller,
                $"; sold {unitsSold} {reserveCategoryId} to households for {FormatMoney(revenue)}");
            return unitsSold;
        }

        private int TrySellTownPulseDemandFromBusiness(
            BusinessInstanceState seller,
            string reserveCategoryId,
            string sellerCategoryId,
            int requestedUnits,
            out int revenueCents)
        {
            revenueCents = 0;
            if (seller == null
                || seller.RuntimeState == null
                || string.IsNullOrWhiteSpace(sellerCategoryId)
                || requestedUnits <= 0)
            {
                return 0;
            }

            BusinessProfileDefinition profile = FindProfile(seller.BusinessType);
            if (profile == null || profile.Business == null || !profile.Business.OwnsCategory(sellerCategoryId))
            {
                return 0;
            }

            CategoryStockState stock = seller.RuntimeState.GetCategoryStock(sellerCategoryId);
            if (stock == null || stock.CurrentStockUnits <= 0)
            {
                return 0;
            }

            int unitPriceCents = GetAverageCategoryTransferPriceCents(seller, profile, sellerCategoryId);
            if (unitPriceCents <= 0)
            {
                return 0;
            }

            int staffedUnits = ScaleHouseholdSaleUnitsByOperatingEfficiency(seller, requestedUnits);
            int unitsSold = Mathf.Min(Mathf.Max(0, requestedUnits), staffedUnits, stock.CurrentStockUnits);
            if (unitsSold <= 0)
            {
                return 0;
            }

            revenueCents = unitsSold * unitPriceCents;
            seller.RuntimeState.ResolveDailySalesPlaceholder(sellerCategoryId, unitsSold, revenueCents);
            seller.RefreshCapacityState();
            seller.RecordBusinessReputationObservation(
                new BusinessReputationObservation(
                    requestedUnits,
                    unitsSold,
                    unitPriceCents,
                    GetAverageCategoryLandedCostCents(profile, sellerCategoryId),
                    stock.StockHealth01,
                    seller.RuntimeState.Reliability01,
                    GetHealthAdjustedOperatingEfficiency01(seller),
                    sellerCategoryId,
                    timeManager != null ? timeManager.CurrentDate.AbsoluteDayIndex : -1,
                    GetCurrentWeekKey()));
            AppendTransferToOperationSummary(
                seller,
                $"; sold {unitsSold} {reserveCategoryId} to town pulse for {FormatMoney(revenueCents)}");
            return unitsSold;
        }

        private BusinessReputationSellerChoiceScore ApplyHouseholdAffinityToSellerChoiceScore(
            BusinessReputationSellerChoiceScore score,
            int householdId,
            BusinessInstanceState business)
        {
            if (!score.Eligible || householdId < 0 || business == null)
            {
                return score;
            }

            string key = BuildHouseholdAffinityKey(householdId, business.InstanceId);
            if (!householdAffinitiesByKey.TryGetValue(key, out HouseholdBusinessAffinityState affinity) || affinity == null)
            {
                return score;
            }

            HouseholdAffinityScoreSummary summary = householdAffinityEvaluator.Summarize(affinity);
            float affinityDelta = (summary.PreferenceWeight01 - 0.5f) * HouseholdAffinityChoiceInfluence01;
            return new BusinessReputationSellerChoiceScore(
                Mathf.Clamp01(score.Score01 + affinityDelta),
                score.ReputationHeadline01,
                score.CategoryStockHealth01,
                score.ValueFairness01,
                score.OperatingReliability01,
                score.AvailableUnits,
                score.UnitPriceCents,
                score.DisplayName,
                score.InstanceId,
                score.CandidateIndex,
                score.Eligible);
        }

        private void RecordHouseholdAffinity(
            int householdId,
            BusinessInstanceState business,
            string reserveCategoryId,
            string sellerCategoryId,
            int requestedUnits,
            int unitsSold,
            int spendCents,
            int unitPriceCents)
        {
            if (householdId < 0 || business == null || unitsSold <= 0)
            {
                return;
            }

            string key = BuildHouseholdAffinityKey(householdId, business.InstanceId);
            if (!householdAffinitiesByKey.TryGetValue(key, out HouseholdBusinessAffinityState affinity) || affinity == null)
            {
                affinity = new HouseholdBusinessAffinityState
                {
                    householdId = householdId,
                    businessId = business.InstanceId,
                    hasBusinessType = true,
                    businessType = business.BusinessType
                };
            }

            BusinessProfileDefinition profile = FindProfile(business.BusinessType);
            business.EnsureBusinessReputationInitializedFromRuntime();
            HouseholdAffinityEvent affinityEvent = new()
            {
                householdId = householdId,
                businessId = business.InstanceId,
                hasBusinessType = true,
                businessType = business.BusinessType,
                completedPurchase = true,
                spendCents = spendCents,
                priceFairness01 = BusinessReputationSellerChoice.CalculateValueFairness01(
                    unitPriceCents,
                    GetAverageCategoryLandedCostCents(profile, sellerCategoryId)),
                stockConsistency01 = requestedUnits <= 0 ? 0.5f : Mathf.Clamp01(unitsSold / (float)requestedUnits),
                trustQuality01 = businessReputationEvaluator.EvaluateHeadline01(business.BusinessReputation),
                absoluteDayIndex = timeManager != null ? timeManager.CurrentDate.AbsoluteDayIndex : -1
            };
            householdAffinitiesByKey[key] = householdAffinityEvaluator.BuildUpdatedState(affinity, affinityEvent);
        }

        private static string BuildHouseholdAffinityKey(int householdId, string businessId)
        {
            return $"{Mathf.Max(-1, householdId)}:{businessId ?? string.Empty}";
        }

        private int ScaleHouseholdSaleUnitsByOperatingEfficiency(BusinessInstanceState business, int requestedUnits)
        {
            int request = Mathf.Max(0, requestedUnits);
            if (business == null || business.RuntimeState == null || request <= 0)
            {
                return 0;
            }

            if (business.RuntimeState.TargetWorkerCount <= 0)
            {
                return request;
            }

            float efficiency = GetHealthAdjustedOperatingEfficiency01(business);
            if (efficiency <= 0f)
            {
                return 0;
            }

            return Mathf.Max(1, Mathf.FloorToInt(request * efficiency));
        }

        public float GetHealthAdjustedOperatingEfficiency01(BusinessInstanceState business)
        {
            BusinessRuntimeState runtime = business != null ? business.RuntimeState : null;
            if (runtime == null)
            {
                return 0f;
            }

            if (runtime.TargetWorkerCount <= 0)
            {
                return 1f;
            }

            float optionalLaborCapacity = 0f;
            float requiredLaborCapacity = 0f;
            int requiredWorkerCount = 0;
            int activeRequiredWorkerCount = 0;
            for (int i = 0; i < runtime.WorkerSlots.Count; i++)
            {
                WorkerSlotState slot = runtime.WorkerSlots[i];
                if (slot == null)
                {
                    continue;
                }

                if (slot.RequiredForOpening)
                {
                    requiredWorkerCount++;
                }

                if (!slot.IsPaidActive)
                {
                    continue;
                }

                float laborAvailability = GetWorkerSlotLaborAvailability01(slot);
                if (slot.RequiredForOpening)
                {
                    if (laborAvailability <= 0f)
                    {
                        continue;
                    }

                    requiredLaborCapacity += laborAvailability;
                    activeRequiredWorkerCount++;
                    continue;
                }

                optionalLaborCapacity += laborAvailability;
            }

            if (requiredWorkerCount > 0 && activeRequiredWorkerCount < requiredWorkerCount)
            {
                return 0f;
            }

            if (requiredWorkerCount <= 0)
            {
                if (optionalLaborCapacity <= 0f && CanUseOwnerOperatedFallback(business))
                {
                    return OwnerOperatedFallbackEfficiency01;
                }

                return Mathf.Clamp01(optionalLaborCapacity / runtime.TargetWorkerCount);
            }

            float requiredAvailability01 = Mathf.Clamp01(requiredLaborCapacity / requiredWorkerCount);
            int optionalSlots = Mathf.Max(0, runtime.TargetWorkerCount - requiredWorkerCount);
            if (optionalSlots <= 0)
            {
                return requiredAvailability01;
            }

            float requiredBaseline = requiredAvailability01 * RequiredStaffBaselineEfficiency01;
            float optionalContribution = Mathf.Clamp01(optionalLaborCapacity / optionalSlots) * (1f - RequiredStaffBaselineEfficiency01);
            return Mathf.Clamp01(requiredBaseline + optionalContribution);
        }

        private static bool CanUseOwnerOperatedFallback(BusinessInstanceState business)
        {
            return business != null
                && business.Owner != null
                && business.Owner.OwnerKind == BusinessOwnerKind.Player;
        }

        private float GetWorkerSlotLaborAvailability01(WorkerSlotState slot)
        {
            if (slot == null || !slot.IsPaidActive)
            {
                return 0f;
            }

            if (int.TryParse(slot.AssignedWorkerId, out int personId)
                && populationManager != null
                && populationManager.State != null)
            {
                PersonState person = populationManager.State.GetPerson(personId);
                if (person != null)
                {
                    return PopulationHealthEvaluator.GetLaborAvailability01(person);
                }
            }

            return 1f;
        }

        private static string GetBusinessSummaryLabel(BusinessInstanceState business)
        {
            if (business == null)
            {
                return "Business";
            }

            return $"{business.BusinessType}#{Mathf.Max(0, business.AssignedBuildingId):000}";
        }

        private static int CalculateTransferUnits(
            BusinessRuntimeState sourceRuntime,
            string sourceCategoryId,
            BusinessRuntimeState destinationRuntime,
            string destinationCategoryId,
            int maxUnits,
            int unitPriceCents,
            int destinationCashReserveCents)
        {
            CategoryStockState sourceStock = sourceRuntime != null ? sourceRuntime.GetCategoryStock(sourceCategoryId) : null;
            CategoryStockState destinationStock = destinationRuntime != null ? destinationRuntime.GetCategoryStock(destinationCategoryId) : null;
            if (sourceStock == null || destinationStock == null)
            {
                return 0;
            }

            int destinationNeed = destinationStock.TargetStockUnits > 0
                ? Mathf.Max(0, destinationStock.TargetStockUnits - destinationStock.CurrentStockUnits)
                : Mathf.Max(0, maxUnits);
            int spendableCash = Mathf.Max(0, destinationRuntime.CurrentCashCents - Mathf.Max(0, destinationCashReserveCents));
            int cashLimitedUnits = unitPriceCents > 0
                ? spendableCash / unitPriceCents
                : Mathf.Max(0, maxUnits);
            return Mathf.Min(Mathf.Min(Mathf.Min(Mathf.Max(0, maxUnits), sourceStock.CurrentStockUnits), destinationNeed), cashLimitedUnits);
        }

        private static void AppendCashLimitedTransferReason(
            BusinessRuntimeState sourceRuntime,
            string sourceCategoryId,
            BusinessRuntimeState destinationRuntime,
            string destinationCategoryId,
            int maxUnits,
            int unitPriceCents,
            int destinationCashReserveCents)
        {
            CategoryStockState sourceStock = sourceRuntime != null ? sourceRuntime.GetCategoryStock(sourceCategoryId) : null;
            CategoryStockState destinationStock = destinationRuntime != null ? destinationRuntime.GetCategoryStock(destinationCategoryId) : null;
            if (sourceStock == null || destinationStock == null || sourceStock.CurrentStockUnits <= 0 || unitPriceCents <= 0)
            {
                return;
            }

            int destinationNeed = destinationStock.TargetStockUnits > 0
                ? Mathf.Max(0, destinationStock.TargetStockUnits - destinationStock.CurrentStockUnits)
                : Mathf.Max(0, maxUnits);
            if (destinationNeed <= 0)
            {
                return;
            }

            int spendableCash = Mathf.Max(0, destinationRuntime.CurrentCashCents - Mathf.Max(0, destinationCashReserveCents));
            if (spendableCash >= unitPriceCents)
            {
                return;
            }

            destinationRuntime.AppendWeeklyBlockedReason($"cash-limited local transfer: {destinationCategoryId}");
        }

        private static string BuildGeneralStoreSupplySkipReason(
            BusinessRuntimeState sourceRuntime,
            string sourceCategoryId,
            BusinessRuntimeState destinationRuntime,
            string destinationCategoryId,
            int maxUnits,
            int unitPriceCents,
            int destinationCashReserveCents)
        {
            CategoryStockState sourceStock = sourceRuntime != null ? sourceRuntime.GetCategoryStock(sourceCategoryId) : null;
            CategoryStockState destinationStock = destinationRuntime != null ? destinationRuntime.GetCategoryStock(destinationCategoryId) : null;
            if (sourceStock == null || sourceStock.CurrentStockUnits <= 0)
            {
                return "no seller stock";
            }

            if (destinationStock == null)
            {
                return "category unavailable";
            }

            int destinationNeed = destinationStock.TargetStockUnits > 0
                ? Mathf.Max(0, destinationStock.TargetStockUnits - destinationStock.CurrentStockUnits)
                : Mathf.Max(0, maxUnits);
            if (destinationNeed <= 0)
            {
                return "category full";
            }

            int spendableCash = destinationRuntime != null
                ? Mathf.Max(0, destinationRuntime.CurrentCashCents - Mathf.Max(0, destinationCashReserveCents))
                : 0;
            if (unitPriceCents > 0 && spendableCash < unitPriceCents)
            {
                return "cash buffer";
            }

            return "supply capped";
        }

        private static int GetAvailableCategoryCapacity(BusinessRuntimeState runtime, string categoryId)
        {
            CategoryStockState stock = runtime != null ? runtime.GetCategoryStock(categoryId) : null;
            if (stock == null)
            {
                return 0;
            }

            return stock.TargetStockUnits > 0
                ? Mathf.Max(0, stock.TargetStockUnits - stock.CurrentStockUnits)
                : int.MaxValue;
        }

        private static int GetAverageCategoryTransferPriceCents(BusinessProfileDefinition profile, string categoryId)
        {
            return GetAverageCategoryTransferPriceCents(null, profile, categoryId);
        }

        private static int ResolveTransferAgreementUnitPriceCents(
            BusinessInstanceState source,
            BusinessProfileDefinition profile,
            BusinessTransferAgreementState agreement)
        {
            if (agreement == null)
            {
                return 0;
            }

            int marketPriceCents = GetAverageCategoryTransferPriceCents(source, profile, agreement.SourceCategoryId);
            int landedCostCents = GetAverageCategoryLandedCostCents(profile, agreement.SourceCategoryId);

            // Inventory lots do not currently carry their own historical value basis, so transfer prices are derived
            // from current category cost/market helpers. This keeps business ledgers operationally honest now; portfolio
            // elimination of internal transfer profit remains future work.
            return agreement.PricingMode switch
            {
                BusinessTransferPricingMode.Free => 0,
                BusinessTransferPricingMode.AtCostOrNominalCost => Mathf.Max(1, landedCostCents),
                BusinessTransferPricingMode.BelowMarket => Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(1, marketPriceCents) * 0.85f)),
                BusinessTransferPricingMode.AboveMarket => Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(1, marketPriceCents) * 1.15f)),
                BusinessTransferPricingMode.CustomModifier => Mathf.Max(0, Mathf.RoundToInt(Mathf.Max(1, marketPriceCents) * Mathf.Max(0f, agreement.PriceModifier))),
                _ => Mathf.Max(1, marketPriceCents)
            };
        }

        private static int GetAverageCategoryTransferPriceCents(
            BusinessInstanceState business,
            BusinessProfileDefinition profile,
            string categoryId)
        {
            if (profile == null || string.IsNullOrWhiteSpace(categoryId))
            {
                return 1;
            }

            float categoryMarkup = 1f;
            ItemCategoryDefinition category = profile.FindCategory(categoryId);
            if (category != null)
            {
                categoryMarkup = category.DefaultMarkupMultiplier;
            }
            else if (profile.Business != null)
            {
                categoryMarkup = profile.Business.Economy.DefaultRetailMarkupMultiplier;
            }

            float policyMarginAdjustment = business != null
                ? ManagerPolicyEffects.CalculateMarginAdjustment01(business.ControlState, business.ManagerPolicy)
                : 0f;
            float effectiveMarkup = Mathf.Max(1f, categoryMarkup + policyMarginAdjustment);
            int total = 0;
            int count = 0;
            ReadOnlySpan<ItemDefinition> items = profile.Items;
            for (int i = 0; i < items.Length; i++)
            {
                ItemDefinition item = items[i];
                if (item == null || !string.Equals(item.CategoryId, categoryId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                total += item.Pricing.GetCostPlusPriceCents(effectiveMarkup, 0f);
                count++;
            }

            if (count > 0)
            {
                return Mathf.Max(1, total / count);
            }

            return GetAverageCategoryLandedCostCents(profile, categoryId);
        }

        private static float CalculateLocalMarketCapture01(
            BusinessInstanceState business,
            string categoryId,
            int unitPriceCents,
            int referenceUnitCostCents)
        {
            if (business == null || business.RuntimeState == null)
            {
                return 0f;
            }

            CategoryStockState stock = business.RuntimeState.GetCategoryStock(categoryId);
            float stock01 = stock != null ? stock.StockHealth01 : business.RuntimeState.StockHealth01;
            float reliability01 = business.RuntimeState.Reliability01;
            float efficiency01 = business.OperatingEfficiency01;
            float priceIndex01 = CalculateLocalPriceIndex01(unitPriceCents, referenceUnitCostCents);
            float priceCapture01 = Mathf.Clamp01(1f - priceIndex01 * LocalMarketPriceCaptureSensitivity01);
            float groundedCapture01 =
                priceCapture01 * 0.48f
                + Mathf.Clamp01(stock01) * 0.20f
                + Mathf.Clamp01(reliability01) * 0.16f
                + Mathf.Clamp01(efficiency01) * 0.16f;

            return Mathf.Clamp(groundedCapture01, LocalMarketMinimumCapture01, 1f);
        }

        private static float CalculateLocalPriceIndex01(int unitPriceCents, int referenceUnitCostCents)
        {
            int reference = Mathf.Max(1, referenceUnitCostCents);
            float markupOverReference01 = (Mathf.Max(1, unitPriceCents) - reference) / (float)reference;
            return Mathf.Clamp01(markupOverReference01);
        }

        private static string GetPrimaryMarketCategoryId(BusinessType businessType)
        {
            return businessType switch
            {
                BusinessType.CropFarm => CategoryCropFood,
                BusinessType.Ranch => CategoryLivestock,
                BusinessType.Butcher => CategoryMeat,
                BusinessType.Blacksmith => CategoryHardware,
                BusinessType.FuelDealer => CategoryFuelWood,
                BusinessType.GrainMill => CategoryFlour,
                BusinessType.Bakery => CategoryBread,
                BusinessType.Wheelwright => CategoryWheelwrightRepairs,
                BusinessType.Sawmill => CategoryLumber,
                BusinessType.LumberYard => CategoryLumber,
                BusinessType.Doctor => CategoryMedicalService,
                BusinessType.BoardingHouse => CategoryBoardingLodging,
                BusinessType.LiveryFreight => CategoryFreightService,
                BusinessType.Builder => CategoryBuilderLabor,
                BusinessType.Tailor => CategoryClothing,
                BusinessType.Saloon => CategoryMealService,
                BusinessType.Barber => CategoryBarberService,
                _ => string.Empty
            };
        }

        private void InitializeMineBusinessState(BusinessInstanceState business, int buildingId)
        {
            if (business == null || business.BusinessType != BusinessType.Mine)
            {
                return;
            }

            MineralResourceKind kind = ResolveMineStartupKind(buildingId);
            float districtStrength01 = ResolveMineRegionalStrength01(kind);
            MineRuntimeState state = MineRuntimeState.CreateDefault(kind);
            state.ConfigureSeed(
                kind,
                Mathf.Lerp(0.42f, 0.88f, districtStrength01),
                kind == MineralResourceKind.Coal ? MineDevelopmentStage.SurfaceWorks : MineDevelopmentStage.Prospect,
                kind is MineralResourceKind.Gold or MineralResourceKind.Silver ? 0.34f : 0.24f,
                Mathf.Lerp(0.62f, 0.92f, districtStrength01),
                kind is MineralResourceKind.Gold or MineralResourceKind.Silver ? 0.18f : 0.10f);
            business.SetMineState(state);
            business.RuntimeState?.EnsureCategoryStock(state.StockpileCategoryId, 0, 36);
            business.RuntimeState?.EnsureCategoryStock(CategoryMineSupportSupplies, 6, 12);
        }

        private MineralResourceKind ResolveMineStartupKind(int buildingId)
        {
            if (townWorld == null || townWorld.RegionalResources == null || !townWorld.RegionalResources.HasMeaningfulMineralOpportunity)
            {
                return MineralResourceKind.Coal;
            }

            List<MineralResourceKind> available = new();
            for (int i = 0; i < 4; i++)
            {
                MineralResourceKind kind = (MineralResourceKind)i;
                if (townWorld.RegionalResources.GetStrongestDistrict(kind) != null
                    || townWorld.RegionalResources.GetStrongestRemoteSite(kind) != null)
                {
                    available.Add(kind);
                }
            }

            if (available.Count <= 0)
            {
                return MineralResourceKind.Coal;
            }

            return available[Mathf.Abs(buildingId) % available.Count];
        }

        private float ResolveMineRegionalStrength01(MineralResourceKind kind)
        {
            if (townWorld == null || townWorld.RegionalResources == null)
            {
                return 0.5f;
            }

            float district = townWorld.RegionalResources.GetStrongestDistrict(kind)?.Strength01 ?? 0f;
            float site = townWorld.RegionalResources.GetStrongestRemoteSite(kind)?.SiteStrength01 ?? 0f;
            return Mathf.Clamp01(Mathf.Max(district, site));
        }

        private static int GetMineBaseOutputUnits(MineralResourceKind kind)
        {
            return kind switch
            {
                MineralResourceKind.Iron => 12,
                MineralResourceKind.Gold => 5,
                MineralResourceKind.Silver => 6,
                _ => 15
            };
        }

        private static float GetMineConfidenceMultiplier(float confidence01)
        {
            return Mathf.Lerp(0.75f, 1.2f, Mathf.Clamp01(confidence01));
        }

        private static float GetMineStageMultiplier(MineDevelopmentStage stage)
        {
            return stage switch
            {
                MineDevelopmentStage.SurfaceWorks => 0.82f,
                MineDevelopmentStage.Shaft => 1f,
                MineDevelopmentStage.Expanded => 1.18f,
                _ => 0.55f
            };
        }

        private static float GetMineSafetyStageModifier(MineDevelopmentStage stage)
        {
            return stage switch
            {
                MineDevelopmentStage.SurfaceWorks => 0.03f,
                MineDevelopmentStage.Shaft => 0.07f,
                MineDevelopmentStage.Expanded => 0.10f,
                _ => 0.01f
            };
        }

        private static float GetMineDepletionPerUnit(MineralResourceKind kind)
        {
            return kind switch
            {
                MineralResourceKind.Gold => 0.012f,
                MineralResourceKind.Silver => 0.010f,
                MineralResourceKind.Iron => 0.004f,
                _ => 0.003f
            };
        }

        private static int GetMineOffMapSaleUnits(MineralResourceKind kind)
        {
            return kind switch
            {
                MineralResourceKind.Gold => 3,
                MineralResourceKind.Silver => 4,
                MineralResourceKind.Iron => 6,
                _ => MineOffMapSaleMaxUnits
            };
        }

        private int GetGeneralStoreLocalSupplyPriceCents(
            BusinessInstanceState source,
            BusinessProfileDefinition sourceProfile,
            string sourceCategoryId,
            string storeCategoryId)
        {
            int sourcePrice = GetAverageCategoryTransferPriceCents(source, sourceProfile, sourceCategoryId);
            int sourceLandedCost = GetAverageCategoryLandedCostCents(sourceProfile, sourceCategoryId);
            int storeRetailPrice = generalStoreRuntime != null
                ? generalStoreRuntime.GetAverageCategorySellingPriceCents(storeCategoryId)
                : sourcePrice;
            return CalculateLocalWholesaleTransferPriceCents(sourceLandedCost, sourcePrice, storeRetailPrice);
        }

        public static int CalculateLocalWholesaleTransferPriceCents(
            int sourceLandedCostCents,
            int sourceTransferPriceCents,
            int storeRetailSellPriceCents)
        {
            int sourceCost = Mathf.Max(1, sourceLandedCostCents);
            int sourcePrice = Mathf.Max(sourceCost, sourceTransferPriceCents);
            int survivableFloor = Mathf.Max(1, Mathf.CeilToInt(sourceCost * 1.06f));
            int wholesaleTarget = Mathf.Max(survivableFloor, Mathf.RoundToInt(sourcePrice * 0.74f));
            int storeRetailCeiling = storeRetailSellPriceCents > 0
                ? Mathf.Max(1, Mathf.RoundToInt(storeRetailSellPriceCents * 0.88f))
                : wholesaleTarget;

            if (storeRetailCeiling >= survivableFloor)
            {
                return Mathf.Clamp(wholesaleTarget, survivableFloor, storeRetailCeiling);
            }

            return survivableFloor;
        }

        private static int GetAverageCategoryLandedCostCents(BusinessProfileDefinition profile, string categoryId)
        {
            if (profile == null || string.IsNullOrWhiteSpace(categoryId))
            {
                return 1;
            }

            int total = 0;
            int count = 0;
            ReadOnlySpan<ItemDefinition> items = profile.Items;
            for (int i = 0; i < items.Length; i++)
            {
                ItemDefinition item = items[i];
                if (item == null || !string.Equals(item.CategoryId, categoryId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                total += item.Pricing.GetLandedCostCents();
                count++;
            }

            return count == 0 ? 1 : Mathf.Max(1, total / count);
        }

        private static int GetCategoryStockUnits(BusinessRuntimeState runtime, string categoryId)
        {
            CategoryStockState stock = runtime != null ? runtime.GetCategoryStock(categoryId) : null;
            return stock != null ? stock.CurrentStockUnits : 0;
        }

        private static string GetFirstCategoryId(ReadOnlySpan<string> categoryIds)
        {
            for (int i = 0; i < categoryIds.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(categoryIds[i]))
                {
                    return categoryIds[i];
                }
            }

            return string.Empty;
        }

        private void RecordOperationSummary(BusinessInstanceState business, string summary)
        {
            if (business == null || string.IsNullOrWhiteSpace(business.InstanceId))
            {
                return;
            }

            lastOperationSummaries[business.InstanceId] = string.IsNullOrWhiteSpace(summary) ? "no operation" : summary;
        }

        private void AppendTransferToOperationSummary(BusinessInstanceState business, string segment)
        {
            if (business == null || string.IsNullOrWhiteSpace(business.InstanceId) || string.IsNullOrWhiteSpace(segment))
            {
                return;
            }

            string existing = GetLastOperationSummary(business);
            if (string.Equals(existing, "not resolved yet", StringComparison.OrdinalIgnoreCase))
            {
                existing = string.Empty;
            }

            lastOperationSummaries[business.InstanceId] = existing + segment;
        }

        private static void AppendSummarySegment(StringBuilder builder, string segment)
        {
            if (builder == null || string.IsNullOrWhiteSpace(segment))
            {
                return;
            }

            if (builder.Length > "Weekly shared operations: ".Length)
            {
                builder.Append("; ");
            }

            builder.Append(segment);
        }


        private int NormalizeSharedBusinessStartupState(string reason)
        {
            EnsureProfilesLoaded();
            int repairs = 0;
            assignedBuildingIds.Clear();
            HashSet<string> seenInstanceIds = new(StringComparer.OrdinalIgnoreCase);
            List<BusinessInstanceState> normalized = new();

            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState business = businesses[i];
                if (business == null)
                {
                    repairs++;
                    continue;
                }

                BusinessProfileDefinition profile = FindProfileForStartup(business);
                repairs += business.NormalizeForStartup(
                    profile,
                    BuildStartupFallbackInstanceId(business, i),
                    GetSharedBusinessSurvivalCashReserveCents(business));

                if (string.IsNullOrWhiteSpace(business.InstanceId) || !seenInstanceIds.Add(business.InstanceId))
                {
                    repairs++;
                    continue;
                }

                if (business.AssignedBuildingId >= 0 && !assignedBuildingIds.Add(business.AssignedBuildingId))
                {
                    repairs++;
                    continue;
                }

                business.ResolveWeeklyBaselineThroughput();
                business.ResolveDailyBaselineService();
                normalized.Add(business);
            }

            if (normalized.Count != businesses.Count)
            {
                repairs++;
            }

            businesses.Clear();
            businesses.AddRange(normalized);
            return repairs;
        }

        private static string BuildStartupFallbackInstanceId(BusinessInstanceState business, int index)
        {
            BusinessType type = business != null ? business.BusinessType : BusinessType.GeneralStore;
            string typeName = type.ToString().ToLowerInvariant();
            return $"startup_{typeName}_{Mathf.Max(0, index):00}";
        }

        private BusinessProfileDefinition FindProfileForStartup(BusinessInstanceState business)
        {
            if (business == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(business.ProfileId))
            {
                EnsureProfilesLoaded();
                for (int i = 0; i < businessProfiles.Length; i++)
                {
                    BusinessProfileDefinition profile = businessProfiles[i];
                    if (profile != null && string.Equals(profile.Business.BusinessId, business.ProfileId, StringComparison.OrdinalIgnoreCase))
                    {
                        return profile;
                    }
                }
            }

            return FindProfile(business.BusinessType);
        }

        private int SeedPassiveStartStateBusinesses(int target)
        {
            if (target <= 0)
            {
                return 0;
            }

            int seed = GetPassiveBusinessSeed();
            System.Random random = new(seed);
            List<BusinessProfileDefinition> profileQueue = BuildPassiveBusinessProfileQueue(random);
            HashSet<BusinessType> seededTypes = new();
            Dictionary<BusinessType, int> seededTypeCounts = new();
            int created = 0;

            for (int i = 0; i < profileQueue.Count && created < target; i++)
            {
                BusinessProfileDefinition profile = profileQueue[i];
                if (profile == null)
                {
                    continue;
                }

                BusinessType businessType = profile.Business.BusinessType;
                if (seededTypes.Contains(businessType))
                {
                    continue;
                }

                int ownerOrdinal = GetNextSeededBusinessOrdinal(seededTypeCounts, businessType);
                if (!TryCreateBusiness(profile, random, ownerOrdinal, out BusinessInstanceState instance))
                {
                    continue;
                }

                instance.ResolveWeeklyBaselineThroughput();
                instance.ResolveDailyBaselineService();
                businesses.Add(instance);
                assignedBuildingIds.Add(instance.AssignedBuildingId);
                seededTypes.Add(businessType);
                seededTypeCounts[businessType] = ownerOrdinal;
                created++;
            }

            for (int i = 0; i < profileQueue.Count && created < target; i++)
            {
                BusinessProfileDefinition profile = profileQueue[i];
                if (profile == null)
                {
                    continue;
                }

                BusinessType businessType = profile.Business.BusinessType;
                int existingCount = seededTypeCounts.TryGetValue(businessType, out int count) ? count : 0;
                if (!CanSeedAdditionalStartDuplicate(businessType, existingCount))
                {
                    continue;
                }

                int ownerOrdinal = existingCount + 1;
                if (!TryCreateBusiness(profile, random, ownerOrdinal, out BusinessInstanceState instance))
                {
                    continue;
                }

                instance.ResolveWeeklyBaselineThroughput();
                instance.ResolveDailyBaselineService();
                businesses.Add(instance);
                assignedBuildingIds.Add(instance.AssignedBuildingId);
                seededTypeCounts[businessType] = ownerOrdinal;
                created++;
            }

            return created;
        }

        private bool TryCreateBusiness(BusinessProfileDefinition profile, System.Random random, int ownerOrdinal, out BusinessInstanceState instance)
        {
            instance = null;
            if (!TryFindEligibleBuilding(profile, random, out int buildingId))
            {
                return false;
            }

            BusinessOwnerIdentity owner = GetDefaultOwner(profile.Business.BusinessType, ownerOrdinal);
            string instanceId = $"{profile.Business.BusinessId}_{buildingId:000}";
            instance = BusinessInstanceState.Create(instanceId, profile, buildingId, owner);
            instance.EnsureOwnerOperatorStaffing(owner);
            return true;
        }

        private bool TryFindEligibleBuilding(BusinessProfileDefinition profile, System.Random random, out int buildingId)
        {
            buildingId = -1;
            if (townWorld == null || profile == null)
            {
                return false;
            }

            List<PlacedBuilding> candidates = new();
            for (int i = 0; i < townWorld.Buildings.Count; i++)
            {
                PlacedBuilding building = townWorld.Buildings[i];
                if (building == null || building.definition == null || assignedBuildingIds.Contains(building.id))
                {
                    continue;
                }

                TownPlot plot = GetPlot(building.plotId);
                if (!BusinessSiteSuitabilityEvaluator.CanOperate(profile, building, plot))
                {
                    continue;
                }

                candidates.Add(building);
            }

            if (candidates.Count == 0)
            {
                return false;
            }

            if (ShouldPreferOuterAgriculturalPlacement(profile.Business.BusinessType))
            {
                candidates.Sort((left, right) =>
                {
                    int leftScore = ScoreOuterAgriculturalCandidate(profile.Business.BusinessType, left);
                    int rightScore = ScoreOuterAgriculturalCandidate(profile.Business.BusinessType, right);
                    int scoreCompare = leftScore.CompareTo(rightScore);
                    return scoreCompare != 0 ? scoreCompare : left.id.CompareTo(right.id);
                });
            }
            else
            {
                Shuffle(candidates, random);
            }

            buildingId = candidates[0].id;
            return true;
        }

        private static bool ShouldPreferOuterAgriculturalPlacement(BusinessType businessType)
        {
            return businessType == BusinessType.CropFarm || businessType == BusinessType.Ranch;
        }

        private int ScoreOuterAgriculturalCandidate(BusinessType businessType, PlacedBuilding building)
        {
            TownPlot plot = building != null ? GetPlot(building.plotId) : null;
            AgriculturalSiteRole targetRole = businessType == BusinessType.Ranch
                ? AgriculturalSiteRole.LivestockYard
                : AgriculturalSiteRole.CropProductionYard;
            int rolePenalty = plot != null && plot.agriculturalSiteRole == targetRole ? 0 : 1;
            int centerX = townWorld != null && townWorld.Grid != null ? townWorld.Grid.Width / 2 : 0;
            int centerZ = townWorld != null && townWorld.Grid != null ? townWorld.Grid.Depth / 2 : 0;
            int distance = plot != null
                ? Mathf.Abs(plot.bounds.Center.x - centerX) + Mathf.Abs(plot.bounds.Center.z - centerZ)
                : 0;
            int hash = PositiveHash(GetPassiveBusinessSeed() + (building != null ? building.id : 0) * 97 + (int)businessType * 131) % 997;
            return rolePenalty * 1000000 - distance * 1000 + hash;
        }

        private List<BusinessProfileDefinition> BuildPassiveBusinessProfileQueue(System.Random random)
        {
            SortProfilesForDeterministicSeeding();
            List<BusinessProfileDefinition> coreProfiles = new();
            List<BusinessProfileDefinition> fillerProfiles = new();

            for (int i = 0; i < businessProfiles.Length; i++)
            {
                BusinessProfileDefinition profile = businessProfiles[i];
                if (profile == null || !CanAutoSeedAtStart(profile.Business.BusinessType))
                {
                    continue;
                }

                if (IsCoreTownBusiness(profile.Business.BusinessType))
                {
                    coreProfiles.Add(profile);
                }
                else
                {
                    fillerProfiles.Add(profile);
                }
            }

            Shuffle(coreProfiles, random);
            Shuffle(fillerProfiles, random);
            coreProfiles.AddRange(fillerProfiles);
            return coreProfiles;
        }

        private int GetPassiveStartupBusinessTarget()
        {
            int eligibleProfileCount = 0;
            for (int i = 0; i < businessProfiles.Length; i++)
            {
                BusinessProfileDefinition profile = businessProfiles[i];
                if (profile != null && CanAutoSeedAtStart(profile.Business.BusinessType))
                {
                    eligibleProfileCount++;
                }
            }

            if (eligibleProfileCount <= 0)
            {
                return 0;
            }

            int availableBuildings = 0;
            if (townWorld != null)
            {
                for (int i = 0; i < townWorld.Buildings.Count; i++)
                {
                    PlacedBuilding building = townWorld.Buildings[i];
                    if (building != null
                        && building.definition != null
                        && !building.playerOwned
                        && !assignedBuildingIds.Contains(building.id))
                    {
                        availableBuildings++;
                    }
                }
            }

            int preferred = Mathf.Min(PreferredCorePassiveBusinessCount, MaxPassiveStartupBusinessCount);
            int additionalDuplicateCapacity = GetPassiveStartupDuplicateCapacity();
            int maximumSupportedByProfiles = eligibleProfileCount + additionalDuplicateCapacity;
            return Mathf.Min(preferred, maximumSupportedByProfiles, availableBuildings);
        }

        private int EnsureMissingCorePassiveBusinesses()
        {
            if (townWorld == null)
            {
                return 0;
            }

            EnsureProfilesLoaded();
            if (townWorld.Grid == null)
            {
                townWorld.GenerateTownShell();
            }

            int created = 0;
            System.Random random = new(GetPassiveBusinessSeed());
            SortProfilesForDeterministicSeeding();
            for (int i = 0; i < businessProfiles.Length; i++)
            {
                BusinessProfileDefinition profile = businessProfiles[i];
                if (profile == null
                    || !IsCoreTownBusiness(profile.Business.BusinessType)
                    || HasBusinessOfType(profile.Business.BusinessType))
                {
                    continue;
                }

                if (!TryCreateBusiness(profile, random, 1, out BusinessInstanceState instance))
                {
                    continue;
                }

                instance.ResolveWeeklyBaselineThroughput();
                instance.ResolveDailyBaselineService();
                businesses.Add(instance);
                assignedBuildingIds.Add(instance.AssignedBuildingId);
                created++;
            }

            return created;
        }

        private bool HasBusinessOfType(BusinessType businessType)
        {
            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState business = businesses[i];
                if (business != null && business.BusinessType == businessType)
                {
                    return true;
                }
            }

            return false;
        }

        private int GetPassiveBusinessSeed()
        {
            int seed = townWorld != null && townWorld.Settings != null ? townWorld.Settings.seed : 0;
            unchecked
            {
                return seed * 397 ^ PassiveBusinessSeedSalt;
            }
        }

        private void SortProfilesForDeterministicSeeding()
        {
            if (businessProfiles == null || businessProfiles.Length <= 1)
            {
                return;
            }

            Array.Sort(businessProfiles, CompareProfilesForDeterministicSeeding);
        }

        private static int CompareProfilesForDeterministicSeeding(BusinessProfileDefinition left, BusinessProfileDefinition right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            int idCompare = string.Compare(
                left.Business.BusinessId,
                right.Business.BusinessId,
                StringComparison.OrdinalIgnoreCase);
            if (idCompare != 0)
            {
                return idCompare;
            }

            return left.Business.BusinessType.CompareTo(right.Business.BusinessType);
        }

        private static int GetNextSeededBusinessOrdinal(Dictionary<BusinessType, int> seededTypeCounts, BusinessType businessType)
        {
            return seededTypeCounts != null && seededTypeCounts.TryGetValue(businessType, out int existingCount)
                ? existingCount + 1
                : 1;
        }

        private int GetPassiveStartupDuplicateCapacity()
        {
            if (businessProfiles == null || businessProfiles.Length == 0)
            {
                return 0;
            }

            int capacity = 0;
            HashSet<BusinessType> counted = new();
            for (int i = 0; i < businessProfiles.Length; i++)
            {
                BusinessProfileDefinition profile = businessProfiles[i];
                if (profile == null)
                {
                    continue;
                }

                BusinessType businessType = profile.Business.BusinessType;
                if (!CanSeedAdditionalStartDuplicate(businessType, 1) || counted.Contains(businessType))
                {
                    continue;
                }

                counted.Add(businessType);
                capacity++;
            }

            return capacity;
        }

        private static bool CanSeedAdditionalStartDuplicate(BusinessType businessType, int existingCount)
        {
            if (existingCount <= 0 || existingCount >= 2)
            {
                return false;
            }

            return businessType == BusinessType.CropFarm
                || businessType == BusinessType.Ranch
                || businessType == BusinessType.BoardingHouse;
        }

        private static bool CanAutoSeedAtStart(BusinessType businessType)
        {
            return IsCoreTownBusiness(businessType) || IsNearTermOptionalStartupBusiness(businessType);
        }

        private static bool IsCoreTownBusiness(BusinessType businessType)
        {
            return businessType == BusinessType.Blacksmith
                || businessType == BusinessType.Butcher
                || businessType == BusinessType.Ranch
                || businessType == BusinessType.CropFarm
                || businessType == BusinessType.Doctor
                || businessType == BusinessType.Sawmill
                || businessType == BusinessType.LumberYard
                || businessType == BusinessType.BoardingHouse
                || businessType == BusinessType.LiveryFreight
                || businessType == BusinessType.Builder;
        }

        private static bool IsNearTermOptionalStartupBusiness(BusinessType businessType)
        {
            return businessType == BusinessType.FuelDealer
                || businessType == BusinessType.GrainMill
                || businessType == BusinessType.Bakery;
        }

        private static bool IsProfessionWaveBusiness(BusinessType businessType)
        {
            return businessType == BusinessType.LiveryFreight
                || businessType == BusinessType.Builder
                || businessType == BusinessType.FuelDealer
                || businessType == BusinessType.GrainMill
                || businessType == BusinessType.Bakery
                || businessType == BusinessType.Tailor
                || businessType == BusinessType.Saloon
                || businessType == BusinessType.Barber
                || businessType == BusinessType.Wheelwright;
        }

        private static void Shuffle<T>(List<T> values, System.Random random)
        {
            if (values == null || values.Count <= 1)
            {
                return;
            }

            random ??= new System.Random(0);
            for (int i = values.Count - 1; i > 0; i--)
            {
                int j = random.Next(0, i + 1);
                (values[i], values[j]) = (values[j], values[i]);
            }
        }

        private static int PositiveHash(int value)
        {
            unchecked
            {
                uint hash = (uint)value;
                hash ^= hash >> 16;
                hash *= 0x7feb352d;
                hash ^= hash >> 15;
                hash *= 0x846ca68b;
                hash ^= hash >> 16;
                return (int)(hash & 0x7fffffff);
            }
        }

        private static BusinessOwnerIdentity GetDefaultOwner(BusinessType businessType)
        {
            return GetDefaultOwner(businessType, 1);
        }

        private static BusinessOwnerIdentity GetDefaultOwner(BusinessType businessType, int ownerOrdinal)
        {
            ownerOrdinal = Mathf.Max(1, ownerOrdinal);
            int ownerId = 1000 + ((int)businessType + 1) * 100 + ownerOrdinal;
            return businessType switch
            {
                BusinessType.Blacksmith => ownerOrdinal > 1
                    ? BusinessOwnerIdentity.Npc(ownerId, "Jonas Bell", "Bell")
                    : BusinessOwnerIdentity.Npc(ownerId, "Elias Bell", "Bell"),
                BusinessType.Butcher => ownerOrdinal > 1
                    ? BusinessOwnerIdentity.Npc(ownerId, "Rose Hobbs", "Hobbs")
                    : BusinessOwnerIdentity.Npc(ownerId, "Martha Hobbs", "Hobbs"),
                BusinessType.Ranch => ownerOrdinal > 1
                    ? BusinessOwnerIdentity.Npc(ownerId, "Isaac Mercer", "Mercer")
                    : BusinessOwnerIdentity.Npc(ownerId, "Caleb Ward", "Ward"),
                BusinessType.CropFarm => ownerOrdinal > 1
                    ? BusinessOwnerIdentity.Npc(ownerId, "Thomas Bennett", "Bennett")
                    : BusinessOwnerIdentity.Npc(ownerId, "Nora Fields", "Fields"),
                BusinessType.Doctor => ownerOrdinal > 1
                    ? BusinessOwnerIdentity.Npc(ownerId, "Walter Reed", "Reed")
                    : BusinessOwnerIdentity.Npc(ownerId, "Samuel Reed", "Reed"),
                BusinessType.Sawmill => ownerOrdinal > 1
                    ? BusinessOwnerIdentity.Npc(ownerId, "Silas Pike", "Pike")
                    : BusinessOwnerIdentity.Npc(ownerId, "Jonah Pike", "Pike"),
                BusinessType.LumberYard => ownerOrdinal > 1
                    ? BusinessOwnerIdentity.Npc(ownerId, "Henry Quinn", "Quinn")
                    : BusinessOwnerIdentity.Npc(ownerId, "Ada Mercer", "Mercer"),
                BusinessType.BoardingHouse => ownerOrdinal > 1
                    ? BusinessOwnerIdentity.Npc(ownerId, "Lydia Marsh", "Marsh")
                    : BusinessOwnerIdentity.Npc(ownerId, "Agnes Porter", "Porter"),
                BusinessType.LiveryFreight => ownerOrdinal > 1
                    ? BusinessOwnerIdentity.Npc(ownerId, "Frank Hollis", "Hollis")
                    : BusinessOwnerIdentity.Npc(ownerId, "Thomas Keane", "Keane"),
                BusinessType.Builder => ownerOrdinal > 1
                    ? BusinessOwnerIdentity.Npc(ownerId, "Peter Doyle", "Doyle")
                    : BusinessOwnerIdentity.Npc(ownerId, "Samuel Briggs", "Briggs"),
                BusinessType.FuelDealer => ownerOrdinal > 1
                    ? BusinessOwnerIdentity.Npc(ownerId, "Evan Cole", "Cole")
                    : BusinessOwnerIdentity.Npc(ownerId, "Martin Hale", "Hale"),
                BusinessType.GrainMill => ownerOrdinal > 1
                    ? BusinessOwnerIdentity.Npc(ownerId, "Nell Archer", "Archer")
                    : BusinessOwnerIdentity.Npc(ownerId, "Owen Miller", "Miller"),
                BusinessType.Bakery => ownerOrdinal > 1
                    ? BusinessOwnerIdentity.Npc(ownerId, "Clara Weiss", "Weiss")
                    : BusinessOwnerIdentity.Npc(ownerId, "Anna Becker", "Becker"),
                _ => BusinessOwnerIdentity.Town()
            };
        }

        private void EnsureProfilesLoaded()
        {
            if (businessProfiles != null && businessProfiles.Length > 0)
            {
                SortProfilesForDeterministicSeeding();
                return;
            }

            businessProfiles = Resources.LoadAll<BusinessProfileDefinition>(DefaultProfilesResourcePath);
            SortProfilesForDeterministicSeeding();
        }

        private void Awake()
        {
            AutoWire();
        }

        private void Start()
        {
            SubscribeToTime();
        }

        private void OnEnable()
        {
            SubscribeToTime();
        }

        private void OnDisable()
        {
            UnsubscribeFromTime();
        }

        private void AutoWire()
        {
            townWorld ??= FindAnyObjectByType<TownWorldController>();
            populationManager ??= FindAnyObjectByType<PopulationManager>();
            timeManager ??= TimeManager.Instance != null ? TimeManager.Instance : FindAnyObjectByType<TimeManager>();
            generalStoreRuntime ??= FindAnyObjectByType<GeneralStoreRuntimeManager>();
            playerPortfolio ??= FindAnyObjectByType<PlayerPortfolioManager>();
            logisticsRuntime ??= FindAnyObjectByType<LogisticsRuntimeManager>();
        }

        private Func<LocalRecurringOrderFulfillmentRequest, int, int, int, int> BuildRecurringOrderShipmentScheduler(
            LocalRecurringOrderTemplate template,
            BusinessInstanceState seller,
            BusinessInstanceState buyer)
        {
            logisticsRuntime ??= FindAnyObjectByType<LogisticsRuntimeManager>();
            if (logisticsRuntime == null || template == null || seller == null || buyer == null)
            {
                return null;
            }

            return (request, committedUnits, sellerUnitPriceCents, buyerUnitCostCents) =>
            {
                if (committedUnits <= 0)
                {
                    return 0;
                }

                ShipmentHaulingMode haulingMode = template.HaulingResponsibility.ToShipmentHaulingMode();
                string freightPayerBusinessId = haulingMode == ShipmentHaulingMode.HiredFreight
                    ? buyer.InstanceId
                    : string.Empty;
                LogisticsShipmentDeliveryMode deliveryMode = buyer.BusinessType == BusinessType.GeneralStore
                    ? LogisticsShipmentDeliveryMode.GeneralStoreLocalSupply
                    : LogisticsShipmentDeliveryMode.AddCategoryStock;
                LogisticsShipmentState shipment = logisticsRuntime.CreateLocalBusinessShipment(
                    seller,
                    template.SellerCategoryId,
                    buyer,
                    template.BuyerCategoryId,
                    committedUnits,
                    sellerUnitPriceCents,
                    buyerUnitCostCents,
                    true,
                    deliveryMode,
                    $"{seller.RuntimeDisplayName}->{buyer.RuntimeDisplayName} recurring",
                    haulingMode,
                    freightPayerBusinessId);
                return shipment != null
                    && shipment.RoutePlan != null
                    && shipment.RoutePlan.VisiblePath != null
                    && shipment.RoutePlan.VisiblePath.Count > 0
                    ? committedUnits
                    : 0;
            };
        }

        private static string FormatGameSeconds(float gameSeconds)
        {
            TimeSpan span = TimeSpan.FromSeconds(Mathf.Max(0f, gameSeconds));
            if (span.TotalDays >= 1d)
            {
                return $"{Mathf.Max(1, Mathf.RoundToInt((float)span.TotalDays))}d";
            }

            if (span.TotalHours >= 1d)
            {
                return $"{Mathf.Max(1, Mathf.RoundToInt((float)span.TotalHours))}h";
            }

            return $"{Mathf.Max(1, Mathf.RoundToInt((float)span.TotalMinutes))}m";
        }

        private void SubscribeToTime()
        {
            AutoWire();
            if (subscribedToTime || timeManager == null)
            {
                return;
            }

            timeManager.WeekChanged += OnWeekChanged;
            timeManager.MonthChanged += OnMonthChanged;
            subscribedToTime = true;
        }

        private void UnsubscribeFromTime()
        {
            if (!subscribedToTime || timeManager == null)
            {
                subscribedToTime = false;
                return;
            }

            timeManager.WeekChanged -= OnWeekChanged;
            timeManager.MonthChanged -= OnMonthChanged;
            subscribedToTime = false;
        }

        private void OnWeekChanged(SimulationDateChangedContext context)
        {
            ResolveWeeklySharedOperations();
        }

        private void OnMonthChanged(SimulationDateChangedContext context)
        {
            InitializeIfNeeded(generalStoreRuntime != null ? generalStoreRuntime.CurrentBusiness : null);
            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessRuntimeState runtime = businesses[i]?.RuntimeState;
                runtime?.ResetMonthToDateNet();
                if (context.PreviousDate.Year != context.CurrentDate.Year)
                {
                    runtime?.ResetYearToDateNet();
                }
            }
        }

        private int GetCurrentWeekKey()
        {
            return timeManager != null ? Mathf.Max(0, timeManager.CurrentWeek) : -1;
        }

        private TownPlot GetPlot(int plotId)
        {
            if (townWorld == null || plotId < 0 || plotId >= townWorld.Plots.Count)
            {
                return null;
            }

            return townWorld.Plots[plotId];
        }

        private PersonState FindPersonForWorkerSlot(WorkerSlotState slot)
        {
            if (slot == null || populationManager == null || populationManager.State == null)
            {
                return null;
            }

            return int.TryParse(slot.AssignedWorkerId, out int personId)
                ? populationManager.State.GetPerson(personId)
                : null;
        }

        private BusinessProfileDefinition FindProfile(BusinessType businessType)
        {
            EnsureProfilesLoaded();
            for (int i = 0; i < businessProfiles.Length; i++)
            {
                BusinessProfileDefinition profile = businessProfiles[i];
                if (profile != null && profile.Business.BusinessType == businessType)
                {
                    return profile;
                }
            }

            return null;
        }

        private void AppendWorkerSelection(StringBuilder builder, BusinessInstanceState business, int selectedWorkerIndex)
        {
            List<PersonState> candidates = BuildAvailableWorkerCandidateList(business);
            if (GetOpenWorkerSlotCount(business) <= 0)
            {
                builder.AppendLine("No open slots.");
                int selectedSlotIndex = FindFilledWorkerSlotIndexBySelection(business, selectedWorkerIndex);
                if (selectedSlotIndex >= 0)
                {
                    WorkerSlotState selectedSlot = business.RuntimeState.WorkerSlots[selectedSlotIndex];
                    builder.AppendLine($"Selected: {selectedSlot.AssignedWorkerDisplayName} | {selectedSlot.SlotDisplayName} | {FormatMoney(selectedSlot.WeeklyWageCents)}/wk");
                }

                return;
            }

            if (candidates.Count == 0)
            {
                builder.AppendLine("No eligible candidates.");
                return;
            }

            int index = Mathf.Clamp(selectedWorkerIndex, 0, candidates.Count - 1);
            PersonState candidate = candidates[index];
            int openSlotIndex = FindFirstOpenWorkerSlotIndex(business);
            if (openSlotIndex >= 0)
            {
                WorkerSlotState openSlot = business.RuntimeState.WorkerSlots[openSlotIndex];
                WorkerRoleFitResult fit = WorkerRoleFitEvaluator.Evaluate(candidate, business.BusinessType, openSlot);
                string readiness = ApprenticeshipProgressionEvaluator.BuildCandidateReadinessHint(candidate, business.BusinessType, openSlot);
                string readinessSegment = string.IsNullOrWhiteSpace(readiness) ? string.Empty : $" | {readiness}";
                builder.AppendLine($"Candidate {index + 1}/{candidates.Count}: {candidate.DisplayName} | {fit.Label.ToLowerInvariant()} fit | {FormatMoney(openSlot.WeeklyWageCents)}/wk{readinessSegment}");
            }
        }

        private List<PersonState> BuildAvailableWorkerCandidateList(BusinessInstanceState business)
        {
            return BuildAvailableWorkerCandidateList(business, GetFirstOpenWorkerSlot(business));
        }

        private List<PersonState> BuildAvailableWorkerCandidateList(BusinessInstanceState business, WorkerSlotState targetSlot)
        {
            List<PersonState> candidates = new();
            PopulationState population = populationManager != null ? populationManager.State : null;
            if (business == null || population == null || population.people == null)
            {
                return candidates;
            }

            for (int i = 0; i < population.people.Count; i++)
            {
                PersonState person = population.people[i];
                if (person != null
                    && person.id >= 0
                    && NewcomerSettlementEvaluator.IsAvailableForLabor(person, minimumHireLaborAccess)
                    && person.workplaceBuildingId < 0
                    && !IsAlreadyAssignedToBusinessSlot(business, person.id))
                {
                    person.EnsureWorkerTraitsInitialized();
                    candidates.Add(person);
                }
            }

            candidates.Sort((left, right) => WorkerRoleFitEvaluator.CompareCandidates(left, right, business.BusinessType, targetSlot));
            return candidates;
        }

        private PersonState FindBestAvailableWorkerCandidate(BusinessInstanceState business, WorkerSlotState targetSlot)
        {
            List<PersonState> candidates = BuildAvailableWorkerCandidateList(business, targetSlot);
            return candidates.Count > 0 ? candidates[0] : null;
        }

        private bool IsAlreadyAssignedToBusinessSlot(BusinessInstanceState business, int personId)
        {
            if (business == null || business.RuntimeState == null)
            {
                return false;
            }

            string id = personId.ToString();
            for (int i = 0; i < business.RuntimeState.WorkerSlots.Count; i++)
            {
                if (string.Equals(business.RuntimeState.WorkerSlots[i].AssignedWorkerId, id, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static int GetOpenWorkerSlotCount(BusinessInstanceState business)
        {
            if (business == null || business.RuntimeState == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < business.RuntimeState.WorkerSlots.Count; i++)
            {
                if (!business.RuntimeState.WorkerSlots[i].IsFilled)
                {
                    count++;
                }
            }

            return count;
        }

        private static int GetFilledWorkerSlotCount(BusinessInstanceState business)
        {
            return business != null && business.RuntimeState != null ? business.RuntimeState.FilledWorkerCount : 0;
        }

        private static int FindFirstOpenWorkerSlotIndex(BusinessInstanceState business)
        {
            if (business == null || business.RuntimeState == null)
            {
                return -1;
            }

            for (int i = 0; i < business.RuntimeState.WorkerSlots.Count; i++)
            {
                if (!business.RuntimeState.WorkerSlots[i].IsFilled)
                {
                    return i;
                }
            }

            return -1;
        }

        private static WorkerSlotState GetFirstOpenWorkerSlot(BusinessInstanceState business)
        {
            int slotIndex = FindFirstOpenWorkerSlotIndex(business);
            return business != null && business.RuntimeState != null && slotIndex >= 0 && slotIndex < business.RuntimeState.WorkerSlots.Count
                ? business.RuntimeState.WorkerSlots[slotIndex]
                : null;
        }

        private static int FindFilledWorkerSlotIndexBySelection(BusinessInstanceState business, int selectedFilledSlotIndex)
        {
            if (business == null || business.RuntimeState == null || business.RuntimeState.FilledWorkerCount <= 0)
            {
                return -1;
            }

            int targetFilledIndex = WrapIndex(selectedFilledSlotIndex, business.RuntimeState.FilledWorkerCount);
            int filledIndex = 0;
            for (int i = 0; i < business.RuntimeState.WorkerSlots.Count; i++)
            {
                if (!business.RuntimeState.WorkerSlots[i].IsFilled)
                {
                    continue;
                }

                if (filledIndex == targetFilledIndex)
                {
                    return i;
                }

                filledIndex++;
            }

            return -1;
        }

        private static void ResetPersonEmployment(PersonState person)
        {
            if (person == null)
            {
                return;
            }

            ApprenticeshipProgressionEvaluator.ClearAssignment(person);
            person.RecordFormerVisibleWorkerRole(person.professionName, person.wage.weeklyWage * 100);
            person.workplaceBuildingId = -1;
            person.currentDestinationBuildingId = -1;
            person.professionId = string.Empty;
            person.professionName = "No assigned job";
            person.wage = WageSnapshot.None();
        }

        private string BuildTownContextLine(BusinessInstanceState business)
        {
            if (business == null)
            {
                return string.Empty;
            }

            if (business.BusinessType == BusinessType.BoardingHouse)
            {
                return string.Empty;
            }

            if (business.BusinessType == BusinessType.Doctor)
            {
                int activeCases = populationManager != null ? CountActiveHealthCases(populationManager.State) : 0;
                return activeCases > 0
                    ? $"Town health pressure: {activeCases} active cases | last daily visits {business.LastDailyServiceVisits}"
                    : $"Town health pressure: quiet | last daily visits {business.LastDailyServiceVisits}";
            }

            if (business.BusinessType == BusinessType.Sawmill)
            {
                if (linkedSawmillSupportNode == null)
                {
                    return "Yard context: sawmill support node unavailable.";
                }

                string hauling = linkedSawmillSupportNode.InternalHaulingOwned ? "owned hauling" : "hired hauling";
                int lumber = GetCategoryStockUnits(business.RuntimeState, CategoryLumber);
                int logs = GetCategoryStockUnits(business.RuntimeState, CategorySawmillLogs);
                int standingTimber = GetCategoryStockUnits(business.RuntimeState, CategoryStandingTimber);
                int slabsOffcuts = GetCategoryStockUnits(business.RuntimeState, CategorySlabsOffcuts);
                string byproduct = slabsOffcuts > 0 ? $" | {slabsOffcuts} slabs/offcuts" : string.Empty;
                return $"Yard context: {lumber}/{linkedSawmillSupportNode.LumberStorageCapacityUnits} lumber | {logs} logs | {standingTimber} standing timber{byproduct} | {hauling}";
            }

            if (business.BusinessType == BusinessType.LumberYard)
            {
                string hauling = linkedSawmillSupportNode != null && linkedSawmillSupportNode.InternalHaulingOwned ? "owned hauling" : "market hauling";
                int onHand = GetCategoryStockUnits(business.RuntimeState, CategoryLumber);
                int localBuilderDemand = Mathf.Min(onHand, LumberYardLocalBuilderDemandMaxUnits);
                return $"Yard context: {onHand} lumber on hand | builder demand up to {localBuilderDemand}/wk | {hauling}";
            }

            if (business.BusinessType == BusinessType.LiveryFreight)
            {
                int support = Mathf.RoundToInt(GetOperationalLiverySupport01() * 100f);
                string freightIncome = business.RuntimeState != null
                    ? FormatMoney(business.RuntimeState.LastWeeklyLocalTransferRevenueCents)
                    : FormatMoney(0);
                string shipmentRead = logisticsRuntime != null
                    ? logisticsRuntime.BuildBusinessLogisticsSummary(business.InstanceId)
                    : "Logistics: runtime unavailable.";
                return $"Logistics context: hauling support {support}% | freight income {freightIncome} | {shipmentRead}";
            }

            if (business.BusinessType == BusinessType.Builder)
            {
                return $"Construction context: builder labor capacity +{GetBuilderLaborCapacityUnits()} units";
            }

            return string.Empty;
        }

        private string BuildBusinessProductionContextLine(BusinessInstanceState business)
        {
            if (business == null || business.RuntimeState == null)
            {
                return string.Empty;
            }

            if (business.BusinessType == BusinessType.Sawmill && linkedSawmillSupportNode != null)
            {
                return $"Last saw run: cut {linkedSawmillSupportNode.LastWeeklyTimberCutUnits} | hauled {linkedSawmillSupportNode.LastWeeklyLogsHauledUnits} | processed {linkedSawmillSupportNode.LastWeeklyLogsProcessedUnits} | lumber {linkedSawmillSupportNode.LastWeeklyLumberProducedUnits} | slabs/offcuts {linkedSawmillSupportNode.LastWeeklySlabsOffcutsProducedUnits}";
            }

            if (business.BusinessType == BusinessType.LumberYard)
            {
                int onHand = GetCategoryStockUnits(business.RuntimeState, CategoryLumber);
                int builderDemand = Mathf.Min(onHand, LumberYardLocalBuilderDemandMaxUnits);
                return $"Lumber flow: {onHand} lumber on hand | builder demand cap {builderDemand}/wk | recurring transfers stay active through the town freight chain";
            }

            return string.Empty;
        }

        private static string BuildOperatingLine(BusinessInstanceState business)
        {
            if (business.BusinessType == BusinessType.BoardingHouse)
            {
                return $"Lodging service {business.LastDailyServiceVisits}/{business.BaselineDailyServiceCapacity} beds staffed";
            }

            if (business.ThroughputMode == BusinessThroughputMode.Service)
            {
                return $"Service {business.LastDailyServiceVisits}/{business.BaselineDailyServiceCapacity} visits/day";
            }

            return $"Throughput {business.LastWeeklyThroughputUnits}/{business.BaselineWeeklyThroughputUnits} units/wk";
        }

        private static string BuildStorageCapacityLine(BusinessInstanceState business)
        {
            BusinessCapacityState capacity = business != null ? business.Capacity : null;
            if (capacity == null)
            {
                return "Storage unavailable";
            }

            return $"Storage {capacity.CurrentStoredUnits}/{capacity.StorageCapacityUnits} | {capacity.StorageHealth01:P0}";
        }

        private static string BuildMineInspectionLine(BusinessInstanceState business)
        {
            MineRuntimeState mine = business != null ? business.MineState : null;
            if (mine == null)
            {
                return string.Empty;
            }

            return $"Deposit: {MineRuntimeState.GetMineralDisplayName(mine.MineralKind)} | confidence {mine.DepositConfidence01:P0} | stage {MineRuntimeState.GetDevelopmentStageDisplayName(mine.DevelopmentStage)} | safety {mine.SafetyRisk01:P0} | richness {mine.RemainingRichness01:P0} | camp pressure {mine.CampPressure01:P0}";
        }

        private static void AppendMineManagementLines(StringBuilder builder, BusinessInstanceState business)
        {
            MineRuntimeState mine = business != null ? business.MineState : null;
            if (builder == null || mine == null)
            {
                return;
            }

            builder.AppendLine(BuildMineInspectionLine(business));
            builder.AppendLine($"Last cut: {mine.LastWeeklyOutputUnits} units | stockpile {mine.StockpileCategoryId}");
            builder.AppendLine($"Mine ledger: {mine.LastWeeklyOutputSummary}");
        }

        private static void AppendMineInventoryLines(StringBuilder builder, BusinessInstanceState business)
        {
            MineRuntimeState mine = business != null ? business.MineState : null;
            if (builder == null || mine == null)
            {
                return;
            }

            builder.AppendLine(BuildMineInspectionLine(business));
            builder.AppendLine($"Last cut: {mine.LastWeeklyOutputUnits} units | stockpile {mine.StockpileCategoryId}");
        }

        private static string GetBusinessManagementHeading(BusinessType businessType)
        {
            return businessType switch
            {
                BusinessType.Ranch => "Ranch Crew",
                BusinessType.Blacksmith => "Forge Crew",
                BusinessType.Butcher => "Butcher Crew",
                BusinessType.CropFarm => "Farm Crew",
                BusinessType.Doctor => "Practice Staff",
                BusinessType.Sawmill => "Small Sawmill Crew",
                BusinessType.LumberYard => "Lumber Yard Staff",
                BusinessType.BoardingHouse => "Boarding House Staff",
                BusinessType.LiveryFreight => "Livery & Freight Staff",
                BusinessType.Builder => "Builder Crew",
                BusinessType.FuelDealer => "Fuel Yard Staff",
                BusinessType.GrainMill => "Mill Staff",
                BusinessType.Bakery => "Bakery Staff",
                BusinessType.Tailor => "Tailor Staff",
                BusinessType.Saloon => "Saloon Staff",
                BusinessType.Barber => "Barber Staff",
                BusinessType.Wheelwright => "Wheelwright Staff",
                BusinessType.Mine => "Mine Crew",
                _ => "Business Staffing"
            };
        }

        private static string GetBusinessInventoryHeading(BusinessType businessType)
        {
            return businessType switch
            {
                BusinessType.Ranch => "Livestock / Feed",
                BusinessType.Blacksmith => "Inputs / Hardware",
                BusinessType.Butcher => "Livestock / Meat",
                BusinessType.CropFarm => "Seed / Crops",
                BusinessType.Doctor => "Visits / Remedies",
                BusinessType.Sawmill => "Timber / Lumber",
                BusinessType.LumberYard => "Lumber Stock",
                BusinessType.BoardingHouse => "Lodging Accounts",
                BusinessType.LiveryFreight => "Teams / Freight Service",
                BusinessType.Builder => "Crew Capacity",
                BusinessType.FuelDealer => "Fuel Wood",
                BusinessType.GrainMill => "Grain / Flour",
                BusinessType.Bakery => "Flour / Bread",
                BusinessType.Tailor => "Cloth / Clothing",
                BusinessType.Saloon => "Meals / Service",
                BusinessType.Barber => "Barber Service",
                BusinessType.Wheelwright => "Repair Inputs",
                BusinessType.Mine => "Ore / Supplies",
                _ => "Tracked Categories"
            };
        }

        private static string GetProfessionPrefix(BusinessType businessType)
        {
            return businessType switch
            {
                BusinessType.Ranch => "ranch",
                BusinessType.Blacksmith => "blacksmith",
                BusinessType.Butcher => "butcher",
                BusinessType.CropFarm => "crop_farm",
                BusinessType.Doctor => "doctor",
                BusinessType.Sawmill => "sawmill",
                BusinessType.LumberYard => "lumber_yard",
                BusinessType.BoardingHouse => "boarding_house",
                BusinessType.LiveryFreight => "livery_freight",
                BusinessType.Builder => "builder",
                BusinessType.FuelDealer => "fuel_dealer",
                BusinessType.GrainMill => "grain_mill",
                BusinessType.Bakery => "bakery",
                BusinessType.Tailor => "tailor",
                BusinessType.Saloon => "saloon",
                BusinessType.Barber => "barber",
                BusinessType.Wheelwright => "wheelwright",
                BusinessType.Mine => "mine",
                _ => "business"
            };
        }

        private static string GetCategoryDisplayName(BusinessProfileDefinition profile, string categoryId)
        {
            ItemCategoryDefinition category = profile != null ? profile.FindCategory(categoryId) : null;
            return category != null ? category.DisplayName : categoryId;
        }

        private static string GetOwnerDisplayName(BusinessInstanceState business)
        {
            return business != null && business.Owner != null ? business.Owner.DisplayName : "Town";
        }

        private static string FormatMoney(int cents)
        {
            return "$" + (cents / 100f).ToString("N2");
        }

        private static string FormatSignedMoney(int cents)
        {
            int amount = Mathf.Abs(cents);
            string sign = cents > 0 ? "+" : cents < 0 ? "-" : string.Empty;
            return sign + "$" + (amount / 100f).ToString("N2");
        }

        private static int WrapIndex(int index, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            return index < 0 ? count - 1 : index % count;
        }
    }
}
