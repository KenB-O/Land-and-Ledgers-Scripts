using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Reputation;
using LandLedgers.Time;
using LandLedgers.UI;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.FirstLedger
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(260)]
    public sealed class GeneralStoreRuntimeManager : MonoBehaviour
    {
        private const int MinimumPostReorderCashBufferCents = 25000;
        private const int MinimumLocalIntakeAllowanceCents = 3000;
        private const int StartupSurplusAboveSurvivalReserveCents = 12000;
        private const int SaturdayDayOfWeekIndex = 5;
        private const float SaturdayHouseholdDemandMultiplier = 1.35f;
        private const float SaturdayStockoutSeverityMultiplier = 1.35f;
        private const float MinimumStoreMarginAdjustment01 = -0.10f;
        private const float MaximumStoreMarginAdjustment01 = 0.50f;
        private const float StoreMarginAdjustmentStep01 = 0.05f;
        private const float SharedSellerClearPreferenceMargin01 = 0.12f;
        private const float HouseholdAwareSharedSellerClearPreferenceMargin01 = 0.12f;
        private const float HouseholdAffinityChoiceInfluence01 = 0.24f;
        private const float RequiredStaffBaselineEfficiency01 = 0.8f;
        private const float OwnerOperatedFallbackEfficiency01 = 0.62f;
        private const int FragileStoreCashReserveCents = 8000;
        private const int WeakTrendCashReserveCents = 4000;
        private const float CriticalStockHealthThreshold01 = 0.25f;

        [Header("Sources")]
        [SerializeField]
        private TownWorldController townWorld;

        [SerializeField]
        private PopulationManager populationManager;

        [SerializeField]
        private SharedBusinessRuntimeManager sharedBusinessRuntime;

        [SerializeField]
        private TimeManager timeManager;

        [SerializeField]
        private GeneralStoreBusinessDefinition storeDefinition;

        [SerializeField]
        private LandLedgersHUDController hudController;

        [SerializeField]
        private TownPulseRuntimeManager townPulseRuntime;

        [SerializeField]
        private PlayerPortfolioManager playerPortfolioManager;

        [SerializeField]
        private LogisticsRuntimeManager logisticsRuntime;

        [Header("Store Sales Tuning")]
        [SerializeField, Range(0.05f, 0.75f)]
        private float householdDailySpendShare = 0.42f;

        [SerializeField, Min(1)]
        private int fallbackHouseholdDailyBudgetCents = 115;

        [SerializeField, Min(0)]
        private int weeklyHouseholdAllowanceFallbackCents = 525;

        [SerializeField, Min(1)]
        private int minimumCategoryUnitPriceCents = 5;

        [Header("Pricing Controls")]
        [SerializeField, Range(MinimumStoreMarginAdjustment01, MaximumStoreMarginAdjustment01)]
        private float storeMarginAdjustment01;

        [Header("Debug / Verification")]
        [SerializeField]
        [Tooltip("Logs the daily sales and weekly settlement summaries when those cadences fire.")]
        private bool logBusinessCadenceDebug = true;

        [Header("Staffing")]
        [SerializeField]
        private string ownerDisplayName = "Player";

        [SerializeField]
        private LaborAccessLevel minimumHireLaborAccess = LaborAccessLevel.YoungWorker;

        [SerializeField]
        private string hiredWorkerProfessionPrefix = "general_store";

        [Header("Runtime")]
        [SerializeField]
        [Tooltip("When false, the scenario begins without a player-owned General Store. The player forms one through Businesses > Create Business.")]
        private bool createPlayerBusinessOnInitialize = true;

        [SerializeField]
        private int storeBuildingId = -1;

        [SerializeField]
        private int generatedHouseholdCount;

        [SerializeField]
        private int lastDailyCustomerHouseholds;

        [SerializeField]
        private int lastDailyUnitsSold;

        [SerializeField]
        private int lastDailyWalletSpendCents;

        [SerializeField]
        private int lastDailyCostOfGoodsSoldCents;

        [SerializeField]
        private int lastDailyHouseholdWalletBeforeCents;

        [SerializeField]
        private int lastDailyHouseholdWalletAfterCents;

        [SerializeField]
        private int lastDailyReserveLocalUnits;

        [SerializeField]
        private int lastDailyReserveLocalSpendCents;

        [SerializeField]
        private int lastDailyOnMapStoreUnitsSold;

        [SerializeField]
        private int lastDailyOnMapStoreSpendCents;

        [SerializeField]
        private int lastDailyOffMapStoreUnitsSold;

        [SerializeField]
        private int lastDailyOffMapStoreSpendCents;

        [SerializeField]
        private int lastDailyReserveOffMapUnits;

        [SerializeField]
        private int lastDailyReserveOffMapLostDemandCents;

        [SerializeField]
        private int lastDailyMissedStockUnits;

        [SerializeField]
        private int lastDailyMissedServiceUnits;

        [SerializeField]
        private int lastDailyMissedPriceUnits;

        [Header("Latest Customer Sale Provenance")]
        [SerializeField]
        private int lastCustomerHouseholdId = -1;

        [SerializeField]
        private int lastCustomerPersonId = -1;

        [SerializeField]
        private string lastCustomerSaleCategoryId = string.Empty;

        [SerializeField]
        private int lastCustomerSaleUnits;

        [SerializeField]
        private int lastCustomerSaleRevenueCents;

        [SerializeField]
        private int currentWeekCustomerHouseholds;

        [SerializeField]
        private int weekToDateCostOfGoodsSoldCents;

        [SerializeField]
        private int currentSalesWeek = -1;

        [SerializeField]
        private int lastWeeklyReorderSpendCents;

        [SerializeField]
        private int lastWeeklyReorderUnitsQueued;

        [SerializeField]
        private int lastWeeklyReorderUnitsReceived;

        [SerializeField]
        private int lastWeeklyReorderUnitsRemaining;

        [SerializeField]
        private int lastWeeklyReorderQueuedCostCents;

        [SerializeField]
        private bool lastWeeklyReorderTriggered;

        [SerializeField]
        private int lastWeeklyLocalSupplySpendCents;

        [SerializeField]
        private int lastWeeklyLocalSupplyUnitsReceived;

        [SerializeField]
        private int lastWeeklyCashConstrainedReorderUnits;

        [SerializeField]
        private int lastWeeklyCashConstrainedReorderCostCents;

        [SerializeField, TextArea(1, 4)]
        private string lastWeeklyLocalSupplySummary = "No local supply received this week.";

        [SerializeField]
        private bool reorderNeeded;

        [SerializeField]
        private string status = "Not ready";

        [SerializeField]
        private BusinessInstanceState currentBusiness;

        [SerializeField, TextArea(2, 6)]
        private string lastDailySalesDebugSummary = "No daily sales resolved yet.";

        [SerializeField, TextArea(2, 8)]
        private string lastWeeklySettlementDebugSummary = "No weekly settlement resolved yet.";

        [SerializeField, TextArea(2, 8)]
        private string lastReorderDebugSummary = "No reorder review resolved yet.";

        [SerializeField, TextArea(2, 5)]
        private string lastStaffingSummary = "No staffing changes yet.";

        [SerializeField, TextArea(2, 6)]
        private string lastCategoryDemandSummary = "No category demand resolved yet.";

        private BusinessRuntimeState runtimeState;
        private bool subscribed;
        private readonly HashSet<string> loggedMarginCompressionCategoryIds = new();
        private readonly BusinessReputationSellerChoice businessReputationSellerChoice = new();
        private readonly BusinessReputationEvaluator businessReputationEvaluator = new();
        private readonly HouseholdAffinityEvaluator householdAffinityEvaluator = new();
        private readonly Dictionary<string, HouseholdBusinessAffinityState> householdAffinitiesByKey = new();

        public BusinessRuntimeState RuntimeState => runtimeState;
        public BusinessInstanceState CurrentBusiness => currentBusiness;
        public GeneralStoreBusinessDefinition StoreDefinition => storeDefinition;
        public int StoreBuildingId => storeBuildingId;
        public int LastDailyCustomerHouseholds => lastDailyCustomerHouseholds;
        public int LastDailyUnitsSold => lastDailyUnitsSold;
        public int LastDailyReserveLocalUnits => lastDailyReserveLocalUnits;
        public int LastDailyReserveLocalSpendCents => lastDailyReserveLocalSpendCents;
        public int LastDailyOnMapStoreUnitsSold => lastDailyOnMapStoreUnitsSold;
        public int LastDailyOnMapStoreSpendCents => lastDailyOnMapStoreSpendCents;
        public int LastDailyOffMapStoreUnitsSold => lastDailyOffMapStoreUnitsSold;
        public int LastDailyOffMapStoreSpendCents => lastDailyOffMapStoreSpendCents;
        public int LastCustomerHouseholdId => lastCustomerHouseholdId;
        public int LastCustomerPersonId => lastCustomerPersonId;
        public string LastCustomerSaleCategoryId => lastCustomerSaleCategoryId ?? string.Empty;
        public int LastCustomerSaleUnits => lastCustomerSaleUnits;
        public int LastCustomerSaleRevenueCents => lastCustomerSaleRevenueCents;
        public int LastDailyReserveOffMapUnits => lastDailyReserveOffMapUnits;
        public int LastDailyReserveOffMapLostDemandCents => lastDailyReserveOffMapLostDemandCents;
        public int LastWeeklyReorderSpendCents => lastWeeklyReorderSpendCents;
        public int LastWeeklyReorderUnitsQueued => lastWeeklyReorderUnitsQueued;
        public int LastWeeklyReorderUnitsReceived => lastWeeklyReorderUnitsReceived;
        public int LastWeeklyReorderUnitsRemaining => lastWeeklyReorderUnitsRemaining;
        public int LastWeeklyReorderQueuedCostCents => lastWeeklyReorderQueuedCostCents;
        public int LastWeeklyCashConstrainedReorderUnits => lastWeeklyCashConstrainedReorderUnits;
        public int LastWeeklyCashConstrainedReorderCostCents => lastWeeklyCashConstrainedReorderCostCents;
        public bool LastWeeklyReorderTriggered => lastWeeklyReorderTriggered;
        public bool HasLogisticsRuntime => logisticsRuntime != null;
        public int LastWeeklyLocalSupplySpendCents => lastWeeklyLocalSupplySpendCents;
        public int LastWeeklyLocalSupplyUnitsReceived => lastWeeklyLocalSupplyUnitsReceived;
        public string LastWeeklyLocalSupplySummary => lastWeeklyLocalSupplySummary ?? string.Empty;
        public bool ReorderNeeded => reorderNeeded;
        public string Status => status;
        public string LastStaffingSummary => lastStaffingSummary;
        public string LastCategoryDemandSummary => lastCategoryDemandSummary;
        public int OperatingBusinessCashReserveCents => GetBusinessSurvivalCashReserveCents();

        public int CurrentCashCents => runtimeState != null ? runtimeState.CurrentCashCents : 0;
        public int EstimatedWeeklyNetCashFlowCents
        {
            get
            {
                if (runtimeState == null)
                {
                    return 0;
                }

                int payroll = runtimeState.LastWeeklyPayrollCents;
                int weekGross = GetWeekToDateGrossProfitCents();
                if (weekGross > 0)
                {
                    return weekGross - payroll;
                }

                int dailyGross = GetLastDailyGrossProfitCents();
                if (dailyGross > 0)
                {
                    return dailyGross * 7 - payroll;
                }

                return runtimeState.LastWeeklyCashAfterCents - runtimeState.LastWeeklyCashBeforeCents;
            }
        }
        public static int MinimumPostReorderCashBufferForLocalSupplyCents => MinimumPostReorderCashBufferCents;
        public int EffectivePostReorderCashBufferForLocalSupplyCents => GetEffectivePostReorderCashBufferCents();
        public int EffectiveLocalIntakeAllowanceCents => GetEffectiveLocalIntakeAllowanceCents();
        public int ProtectedBusinessCashReserveCents => GetProtectedBusinessCashReserveCents();
        public float EffectiveReorderThreshold01 => GetEffectiveReorderThreshold01();
        public int OpenWorkerSlotCount => GetOpenWorkerSlotCount();
        public int FilledWorkerSlotCount => GetFilledWorkerSlotCount();
        public float StoreMarginAdjustment01 => ClampStoreMarginAdjustment(storeMarginAdjustment01);
        public bool CanDecreaseStoreMargin => StoreMarginAdjustment01 > MinimumStoreMarginAdjustment01 + 0.001f;
        public bool CanIncreaseStoreMargin => StoreMarginAdjustment01 < MaximumStoreMarginAdjustment01 - 0.001f;

        public GeneralStoreSaveDto CaptureSaveDto()
        {
            return new GeneralStoreSaveDto
            {
                storeBuildingId = storeBuildingId,
                generatedHouseholdCount = generatedHouseholdCount,
                lastDailyCustomerHouseholds = lastDailyCustomerHouseholds,
                lastDailyUnitsSold = lastDailyUnitsSold,
                lastDailyWalletSpendCents = lastDailyWalletSpendCents,
                lastDailyCostOfGoodsSoldCents = lastDailyCostOfGoodsSoldCents,
                lastDailyHouseholdWalletBeforeCents = lastDailyHouseholdWalletBeforeCents,
                lastDailyHouseholdWalletAfterCents = lastDailyHouseholdWalletAfterCents,
                lastDailyReserveLocalUnits = lastDailyReserveLocalUnits,
                lastDailyReserveLocalSpendCents = lastDailyReserveLocalSpendCents,
                lastDailyReserveOffMapUnits = lastDailyReserveOffMapUnits,
                lastDailyReserveOffMapLostDemandCents = lastDailyReserveOffMapLostDemandCents,
                lastCustomerHouseholdId = lastCustomerHouseholdId,
                lastCustomerPersonId = lastCustomerPersonId,
                lastCustomerSaleCategoryId = lastCustomerSaleCategoryId,
                lastCustomerSaleUnits = lastCustomerSaleUnits,
                lastCustomerSaleRevenueCents = lastCustomerSaleRevenueCents,
                currentWeekCustomerHouseholds = currentWeekCustomerHouseholds,
                weekToDateCostOfGoodsSoldCents = weekToDateCostOfGoodsSoldCents,
                currentSalesWeek = currentSalesWeek,
                lastWeeklyReorderSpendCents = lastWeeklyReorderSpendCents,
                lastWeeklyReorderUnitsQueued = lastWeeklyReorderUnitsQueued,
                lastWeeklyReorderUnitsReceived = lastWeeklyReorderUnitsReceived,
                lastWeeklyReorderUnitsRemaining = lastWeeklyReorderUnitsRemaining,
                lastWeeklyReorderQueuedCostCents = lastWeeklyReorderQueuedCostCents,
                lastWeeklyReorderTriggered = lastWeeklyReorderTriggered,
                lastWeeklyLocalSupplySpendCents = lastWeeklyLocalSupplySpendCents,
                lastWeeklyLocalSupplyUnitsReceived = lastWeeklyLocalSupplyUnitsReceived,
                lastWeeklyLocalSupplySummary = lastWeeklyLocalSupplySummary,
                reorderNeeded = reorderNeeded,
                storeMarginAdjustment01 = StoreMarginAdjustment01,
                status = status,
                lastDailySalesDebugSummary = lastDailySalesDebugSummary,
                lastWeeklySettlementDebugSummary = lastWeeklySettlementDebugSummary,
                lastReorderDebugSummary = lastReorderDebugSummary,
                lastStaffingSummary = lastStaffingSummary,
                lastCategoryDemandSummary = lastCategoryDemandSummary,
                currentBusiness = currentBusiness != null ? currentBusiness.CaptureSaveDto() : null
            };
        }

        public void LoadFromSaveDto(GeneralStoreSaveDto dto)
        {
            AutoWire();
            householdAffinitiesByKey.Clear();
            if (dto == null)
            {
                storeBuildingId = -1;
                currentBusiness = null;
                runtimeState = null;
                storeMarginAdjustment01 = 0f;
                status = "General Store save data was missing.";
                PushCashToHud();
                return;
            }

            storeBuildingId = dto.storeBuildingId;
            generatedHouseholdCount = dto.generatedHouseholdCount;
            lastDailyCustomerHouseholds = dto.lastDailyCustomerHouseholds;
            lastDailyUnitsSold = dto.lastDailyUnitsSold;
            lastDailyWalletSpendCents = dto.lastDailyWalletSpendCents;
            lastDailyCostOfGoodsSoldCents = dto.lastDailyCostOfGoodsSoldCents;
            lastDailyHouseholdWalletBeforeCents = dto.lastDailyHouseholdWalletBeforeCents;
            lastDailyHouseholdWalletAfterCents = dto.lastDailyHouseholdWalletAfterCents;
            lastDailyReserveLocalUnits = Mathf.Max(0, dto.lastDailyReserveLocalUnits);
            lastDailyReserveLocalSpendCents = Mathf.Max(0, dto.lastDailyReserveLocalSpendCents);
            lastDailyReserveOffMapUnits = Mathf.Max(0, dto.lastDailyReserveOffMapUnits);
            lastDailyReserveOffMapLostDemandCents = Mathf.Max(0, dto.lastDailyReserveOffMapLostDemandCents);
            lastCustomerHouseholdId = dto.lastCustomerHouseholdId;
            lastCustomerPersonId = dto.lastCustomerPersonId;
            lastCustomerSaleCategoryId = dto.lastCustomerSaleCategoryId ?? string.Empty;
            lastCustomerSaleUnits = Mathf.Max(0, dto.lastCustomerSaleUnits);
            lastCustomerSaleRevenueCents = Mathf.Max(0, dto.lastCustomerSaleRevenueCents);
            currentWeekCustomerHouseholds = dto.currentWeekCustomerHouseholds;
            weekToDateCostOfGoodsSoldCents = dto.weekToDateCostOfGoodsSoldCents;
            currentSalesWeek = dto.currentSalesWeek;
            lastWeeklyReorderSpendCents = dto.lastWeeklyReorderSpendCents;
            lastWeeklyReorderUnitsQueued = dto.lastWeeklyReorderUnitsQueued;
            lastWeeklyReorderUnitsReceived = dto.lastWeeklyReorderUnitsReceived;
            lastWeeklyReorderUnitsRemaining = dto.lastWeeklyReorderUnitsRemaining;
            lastWeeklyReorderQueuedCostCents = dto.lastWeeklyReorderQueuedCostCents;
            lastWeeklyReorderTriggered = dto.lastWeeklyReorderTriggered;
            lastWeeklyLocalSupplySpendCents = dto.lastWeeklyLocalSupplySpendCents;
            lastWeeklyLocalSupplyUnitsReceived = dto.lastWeeklyLocalSupplyUnitsReceived;
            lastWeeklyLocalSupplySummary = string.IsNullOrWhiteSpace(dto.lastWeeklyLocalSupplySummary) ? "No local supply received this week." : dto.lastWeeklyLocalSupplySummary;
            reorderNeeded = dto.reorderNeeded;
            storeMarginAdjustment01 = ClampStoreMarginAdjustment(dto.storeMarginAdjustment01);
            status = string.IsNullOrWhiteSpace(dto.status) ? "General Store restored from save." : dto.status;
            lastDailySalesDebugSummary = string.IsNullOrWhiteSpace(dto.lastDailySalesDebugSummary) ? "No daily sales resolved yet." : dto.lastDailySalesDebugSummary;
            lastWeeklySettlementDebugSummary = string.IsNullOrWhiteSpace(dto.lastWeeklySettlementDebugSummary) ? "No weekly settlement resolved yet." : dto.lastWeeklySettlementDebugSummary;
            lastReorderDebugSummary = string.IsNullOrWhiteSpace(dto.lastReorderDebugSummary) ? "No reorder review resolved yet." : dto.lastReorderDebugSummary;
            lastStaffingSummary = string.IsNullOrWhiteSpace(dto.lastStaffingSummary) ? "No staffing changes yet." : dto.lastStaffingSummary;
            lastCategoryDemandSummary = string.IsNullOrWhiteSpace(dto.lastCategoryDemandSummary) ? "No category demand resolved yet." : dto.lastCategoryDemandSummary;
            currentBusiness = BusinessInstanceState.FromSaveDto(dto.currentBusiness);
            runtimeState = currentBusiness != null ? currentBusiness.RuntimeState : null;
            RefreshReorderState();
            RefreshCurrentBusinessCapacityState();
            PushCashToHud();
        }

        public bool TrySpendPlayerCash(int cents, string reason, out string message)
        {
            if (!InitializeIfNeeded() || runtimeState == null)
            {
                message = "Store cash unavailable.";
                return false;
            }

            int price = Mathf.Max(0, cents);
            if (runtimeState.CurrentCashCents < price)
            {
                message = $"Not enough cash for {reason}. Need {FormatMoney(price)}, have {FormatMoney(runtimeState.CurrentCashCents)}.";
                return false;
            }

            runtimeState.SpendCents(price);
            PushCashToHud();
            status = $"Spent {FormatMoney(price)} on {reason}.";
            message = $"Purchased {reason} for {FormatMoney(price)}.";
            return true;
        }

        public void RefundPlayerCash(int cents, string reason)
        {
            if (runtimeState == null || cents <= 0)
            {
                return;
            }

            int amount = Mathf.Max(0, cents);
            runtimeState.AddCashCents(amount);
            PushCashToHud();
            status = $"Refunded {FormatMoney(amount)} after failed {reason}.";
        }

        public void AddPlayerCash(int cents, string reason)
        {
            if (!InitializeIfNeeded() || runtimeState == null || cents <= 0)
            {
                return;
            }

            int amount = Mathf.Max(0, cents);
            runtimeState.AddCashCents(amount);
            PushCashToHud();
            string label = string.IsNullOrWhiteSpace(reason) ? "bank funds" : reason;
            status = $"Received {FormatMoney(amount)} from {label}.";
        }

        public bool ShouldHouseholdShopToday(int householdId)
        {
            return EvaluateHouseholdShoppingPriority(householdId) > 0;
        }

        public int EvaluateHouseholdShoppingPriority(int householdId)
        {
            return EvaluateHouseholdShoppingPriority(GetHousehold(householdId));
        }

        public void Configure(
            TownWorldController newTownWorld,
            PopulationManager newPopulationManager,
            TimeManager newTimeManager,
            GeneralStoreBusinessDefinition newStoreDefinition,
            LandLedgersHUDController newHudController,
            TownPulseRuntimeManager newTownPulseRuntime = null,
            PlayerPortfolioManager newPlayerPortfolioManager = null,
            LogisticsRuntimeManager newLogisticsRuntime = null)
        {
            townWorld = newTownWorld;
            populationManager = newPopulationManager;
            timeManager = newTimeManager;
            storeDefinition = newStoreDefinition;
            hudController = newHudController;
            townPulseRuntime = newTownPulseRuntime != null ? newTownPulseRuntime : townPulseRuntime;
            playerPortfolioManager = newPlayerPortfolioManager != null ? newPlayerPortfolioManager : playerPortfolioManager;
            logisticsRuntime = newLogisticsRuntime != null ? newLogisticsRuntime : logisticsRuntime;
            SubscribeToTime();
        }

        public bool InitializeIfNeeded()
        {
            AutoWire();
            storeMarginAdjustment01 = ClampStoreMarginAdjustment(storeMarginAdjustment01);
            if (runtimeState != null)
            {
                return true;
            }

            if (townWorld == null || storeDefinition == null)
            {
                status = "Missing town world or store definition.";
                return false;
            }

            if (townWorld.Grid == null)
            {
                townWorld.GenerateTownShell();
            }

            if (!createPlayerBusinessOnInitialize)
            {
                status = "No player General Store has been formed yet. Use Businesses > Create Business.";
                return true;
            }

            if (!FindStoreBuilding())
            {
                status = "No eligible generated business building found for the player General Store.";
                return false;
            }

            currentBusiness = BusinessInstanceState.Create("player_general_store", storeDefinition, storeBuildingId, BusinessOwnerIdentity.Player());
            runtimeState = currentBusiness.RuntimeState;
            AssignStoreWorkersFromPopulation();
            if (!TryEnsureRequiredStoreStaffForOpening(out string staffingMessage))
            {
                status = staffingMessage;
                currentBusiness = null;
                runtimeState = null;
                storeBuildingId = -1;
                return false;
            }

            currentBusiness.ResolveWeeklyBaselineThroughput();
            currentBusiness.ResolveDailyBaselineService();
            RefreshReorderState();
            PushCashToHud();
            string staffingSegment = !string.IsNullOrWhiteSpace(staffingMessage) ? $" {staffingMessage}" : string.Empty;
            status = $"{currentBusiness.RuntimeDisplayName} open at building {storeBuildingId}.{staffingSegment}";
            return true;
        }

        public void SetCreatePlayerBusinessOnInitialize(bool enabled)
        {
            createPlayerBusinessOnInitialize = enabled;
        }

        /// <summary>
        /// Binds a formed player General Store from the shared business authority to
        /// this store's operating runtime. Formation and operation remain separate:
        /// this only selects the already-created business as the store manager's
        /// current subject; it does not buy premises, fund the business, hire staff,
        /// or fabricate opening stock.
        /// </summary>
        public bool TryBindFormedPlayerBusiness(BusinessInstanceState business, out string message)
        {
            AutoWire();
            if (business == null
                || (business.BusinessType != BusinessType.GeneralStore && business.BusinessType != BusinessType.Generic))
            {
                message = "Only a formed merchant-capable Business can be bound to the retail management runtime.";
                return false;
            }

            if (business.Owner == null || business.Owner.OwnerKind != BusinessOwnerKind.Player)
            {
                message = "Only a player-owned General Store can be managed here.";
                return false;
            }

            if (runtimeState != null && currentBusiness != business)
            {
                message = $"The General Store runtime is already managing {currentBusiness?.RuntimeDisplayName ?? "another store"}.";
                return false;
            }

            currentBusiness = business;
            runtimeState = business.RuntimeState;
            storeBuildingId = business.AssignedBuildingId;
            if (business.BusinessType == BusinessType.Generic)
            {
                BindExistingMerchantStockToGenericRetail(business);
            }
            RefreshReorderState();
            RefreshCurrentBusinessCapacityState();
            PushCashToHud();
            message = $"{business.RuntimeDisplayName} is ready for operating setup. Configure premises, funding, labor, and stock before opening.";
            status = message;
            return true;
        }

        /// <summary>
        /// Migration adapter for a formed classless merchant. Existing category
        /// stock remains the single inventory authority; this only projects the
        /// legacy authored assortment into generic Retail lines so sale execution
        /// can use GenericRetailSaleAuthority.
        /// </summary>
        private void BindExistingMerchantStockToGenericRetail(BusinessInstanceState business)
        {
            if (business == null || business.RuntimeState == null) return;
            foreach (CategoryStockState stock in business.RuntimeState.CategoryStock)
            {
                if (stock == null || string.IsNullOrWhiteSpace(stock.CategoryId)
                    || business.GenericConfiguration.Retail.TryGetLine(stock.CategoryId, out _)) continue;
                int price = Mathf.Max(0, GetAverageCategorySellingPriceCents(stock.CategoryId));
                float capacity = Mathf.Max(1f, stock.TargetStockUnits * 2f);
                business.TryConfigureGenericRetailLine(
                    new GenericRetailProductLine(stock.CategoryId, "storage", stock.TargetStockUnits,
                        Mathf.Max(stock.TargetStockUnits, capacity), stock.TargetStockUnits, price),
                    capacity);
            }
        }

        public bool TryOpenAtPlayerOwnedShell(int buildingId, out BusinessInstanceState business, out string message)
        {
            AutoWire();
            business = currentBusiness;

            if (runtimeState != null)
            {
                if (storeBuildingId == buildingId)
                {
                    message = $"{currentBusiness.RuntimeDisplayName} is already open at this site.";
                    business = currentBusiness;
                    return true;
                }

                message = $"The General Store is already open at building {storeBuildingId:000}. Additional locations are not available yet.";
                return false;
            }

            if (townWorld == null || storeDefinition == null)
            {
                message = "Missing town world or General Store definition.";
                return false;
            }

            if (townWorld.Grid == null)
            {
                townWorld.GenerateTownShell();
            }

            if (buildingId < 0 || buildingId >= townWorld.Buildings.Count)
            {
                message = $"Building {buildingId:000} is not part of the generated town.";
                return false;
            }

            PlacedBuilding building = townWorld.Buildings[buildingId];
            if (building == null || building.definition == null)
            {
                message = $"Building {buildingId:000} is missing its building shell definition.";
                return false;
            }

            if (!building.playerOwned)
            {
                message = $"Building {buildingId:000} is not player-owned.";
                return false;
            }

            TownPlot plot = building != null ? GetPlot(building.plotId) : null;
            if (!BusinessSiteSuitabilityEvaluator.TryEvaluatePlayerOwnedDevelopmentFit(storeDefinition, building, plot, out PlayerDevelopmentFitResult fit))
            {
                string displayName = building != null && building.definition != null ? building.definition.DisplayName : "Selected shell";
                message = $"{displayName} cannot host the General Store. {(fit != null ? fit.reason : "Missing site fit data.")}";
                return false;
            }

            storeBuildingId = buildingId;
            currentBusiness = BusinessInstanceState.Create($"player_general_store_{buildingId:000}", storeDefinition, storeBuildingId, BusinessOwnerIdentity.Player());
            runtimeState = currentBusiness.RuntimeState;
            EnsureStartupWorkingCapital();
            AssignStoreWorkersFromPopulation();
            if (!TryEnsureRequiredStoreStaffForOpening(out string staffingMessage))
            {
                message = staffingMessage;
                status = message;
                business = null;
                currentBusiness = null;
                runtimeState = null;
                storeBuildingId = -1;
                return false;
            }

            currentBusiness.ResolveWeeklyBaselineThroughput();
            currentBusiness.ResolveDailyBaselineService();
            RefreshReorderState();
            PushCashToHud();
            business = currentBusiness;
            int weekKey = timeManager != null ? Mathf.Max(0, timeManager.CurrentWeek) : -1;
            playerPortfolioManager?.ConfigureDefaultBusinessCashTransfers(
                currentBusiness,
                GetEffectivePostReorderCashBufferCents(),
                GetEffectiveWeeklyReorderReserveCents() + GetEffectiveLocalIntakeAllowanceCents(),
                weekKey);
            playerPortfolioManager?.RegisterCurrentBusinessCashCheckpoint(currentBusiness, weekKey, true);
            string staffingSegment = !string.IsNullOrWhiteSpace(staffingMessage) ? $" {staffingMessage}" : string.Empty;
            message = fit != null && fit.HasWarnings
                ? $"{currentBusiness.RuntimeDisplayName} opened at building {storeBuildingId:000}. Fit warnings: {fit.WarningSummary}.{staffingSegment}"
                : $"{currentBusiness.RuntimeDisplayName} opened at building {storeBuildingId:000}.{staffingSegment}";
            status = message;
            return true;
        }

        [ContextMenu("Resolve Daily Sales")]
        public void ResolveDailySales()
        {
            if (!InitializeIfNeeded() || runtimeState == null)
            {
                status = string.IsNullOrWhiteSpace(status)
                    ? "No player General Store is operating yet. Form one from Businesses before resolving sales."
                    : status;
                return;
            }

            EnsureWeeklySalesBucket();
            runtimeState.BeginDailySalesCadence();
            lastDailyCustomerHouseholds = 0;
            lastDailyUnitsSold = 0;
            lastDailyWalletSpendCents = 0;
            lastDailyCostOfGoodsSoldCents = 0;
            lastDailyHouseholdWalletBeforeCents = GetTotalHouseholdSpendingMoneyCents();
            lastDailyReserveLocalUnits = 0;
            lastDailyReserveLocalSpendCents = 0;
            lastDailyOnMapStoreUnitsSold = 0;
            lastDailyOnMapStoreSpendCents = 0;
            lastDailyOffMapStoreUnitsSold = 0;
            lastDailyOffMapStoreSpendCents = 0;
            lastDailyReserveOffMapUnits = 0;
            lastDailyReserveOffMapLostDemandCents = 0;
            lastCustomerHouseholdId = -1;
            lastCustomerPersonId = -1;
            lastCustomerSaleCategoryId = string.Empty;
            lastCustomerSaleUnits = 0;
            lastCustomerSaleRevenueCents = 0;
            lastDailyMissedStockUnits = 0;
            lastDailyMissedServiceUnits = 0;
            lastDailyMissedPriceUnits = 0;
            CategoryDemandDebug.Reset();

            PopulationState population = populationManager != null ? populationManager.State : null;
            if (population == null || population.households == null)
            {
                status = "No population available for daily sales.";
                return;
            }

            generatedHouseholdCount = population.households.Count;
            int absoluteDay = timeManager != null ? timeManager.CurrentDate.AbsoluteDayIndex : 0;
            for (int i = 0; i < population.households.Count; i++)
            {
                HouseholdState household = population.households[i];
                if (household == null)
                {
                    continue;
                }

                household.lastStoreSpendCents = 0;
                household.lastDailyReserveLocalPurchaseUnits = 0;
                household.lastDailyReserveOffMapPurchaseUnits = 0;
                HouseholdReserveEvaluator.ResolveDailyUse(household, absoluteDay);
                HouseholdDemandSnapshot demand = HouseholdReserveEvaluator.BuildDemandSnapshot(household);
                household.demandSnapshot = demand;

                int householdBudget = GetHouseholdDailyBudgetCents(household);
                HouseholdReservePurchaseResult purchase = ResolveHouseholdReserveNeeds(household, householdBudget);
                int totalHouseholdSpend = purchase.StoreSpendCents + purchase.OtherLocalSpendCents + purchase.OffMapWalletSpendCents;
                if (purchase.StoreSpendCents > 0)
                {
                    lastDailyCustomerHouseholds++;
                    currentWeekCustomerHouseholds++;
                    household.lastStoreSpendCents = purchase.StoreSpendCents;
                    household.lifetimeStoreSpendCents += purchase.StoreSpendCents;
                }

                if (purchase.HasNeed)
                {
                    household.spendingMoneyCents = Mathf.Max(0, household.spendingMoneyCents - totalHouseholdSpend);
                    lastDailyWalletSpendCents += purchase.StoreSpendCents;
                    lastDailyOnMapStoreUnitsSold += purchase.StoreUnits;
                    lastDailyOnMapStoreSpendCents += purchase.StoreSpendCents;
                    lastDailyReserveLocalUnits += purchase.StoreUnits + purchase.OtherLocalUnits;
                    lastDailyReserveLocalSpendCents += purchase.StoreSpendCents + purchase.OtherLocalSpendCents;
                    lastDailyReserveOffMapUnits += purchase.OffMapUnits;
                    lastDailyReserveOffMapLostDemandCents += purchase.OffMapLostDemandCents;
                }
            }

            ResolveTownPulseDemand(absoluteDay);
            lastDailyHouseholdWalletAfterCents = GetTotalHouseholdSpendingMoneyCents();
            lastCategoryDemandSummary = CategoryDemandDebug.BuildSummary();
            RefreshReorderState();
            RefreshCurrentBusinessCapacityState();
            PushCashToHud();
            status = $"Daily sales: {lastDailyCustomerHouseholds} households, {lastDailyUnitsSold} units.";
            lastDailySalesDebugSummary = BuildDailySalesDebugSummary();
            if (logBusinessCadenceDebug)
            {
                Debug.Log(lastDailySalesDebugSummary, this);
            }
        }

        [ContextMenu("Resolve Weekly Payroll And Reorder")]
        public void ResolveWeeklyPayrollAndReorder()
        {
            if (!InitializeIfNeeded())
            {
                return;
            }

            // A classless Business uses Generic Retail/Procurement. Keep this
            // legacy manager as a presentation/compatibility adapter only; its
            // category reorder writer must never mutate a generic entity.
            if (currentBusiness != null && currentBusiness.BusinessType == BusinessType.Generic)
            {
                AssignStoreWorkersFromPopulation();
                runtimeState.ResolveWeeklyPayroll();
                currentBusiness.ResolveWeeklyBaselineThroughput();
                currentBusiness.ResolveDailyBaselineService();
                PushCashToHud();
                status = "Generic Business weekly labor settled; procurement remains on the shared supplier/shipment authority.";
                return;
            }

            AssignStoreWorkersFromPopulation();
            float reorderThreshold01 = GetEffectiveReorderThreshold01();
            bool neededBeforeSettlement = runtimeState.HasReorderNeed(reorderThreshold01);
            int cashBeforeSettlement = runtimeState.CurrentCashCents;
            FindAnyObjectByType<SharedBusinessRuntimeManager>()?.RecordWeeklyProfitForValuation(currentBusiness);
            runtimeState.BeginWeeklySettlementCadence(storeDefinition.Business, reorderThreshold01, false);
            ResolveAutomaticBusinessCashTransfer(BusinessCashAutoTransferMode.LowerOnly);
            runtimeState.ResolveWeeklyPayroll();
            ReleaseSuspendedPayrollWorkersFromPopulation();
            ApprenticeshipProgressionEvaluator.AdvancePaidWorkers(currentBusiness, populationManager != null ? populationManager.State : null);
            DepositWeeklyHouseholdIncome();
            populationManager?.ResolveWeeklySettlementProgression();
            lastWeeklyLocalSupplySpendCents = 0;
            lastWeeklyLocalSupplyUnitsReceived = 0;
            lastWeeklyLocalSupplySummary = "No local supply received this week.";
            lastWeeklyCashConstrainedReorderUnits = 0;
            lastWeeklyCashConstrainedReorderCostCents = 0;

            lastWeeklyReorderTriggered = neededBeforeSettlement;
            lastWeeklyReorderUnitsQueued = GetPendingReorderUnits();
            lastWeeklyReorderQueuedCostCents = CalculatePendingReorderCostCents();
            string reorderQueueSummary = BuildPendingReorderSummary();

            if (neededBeforeSettlement && lastWeeklyReorderUnitsQueued > 0)
            {
                ResolveCostedPendingReorders(runtimeState.CurrentCashCents);
            }
            else
            {
                lastWeeklyReorderSpendCents = 0;
                lastWeeklyReorderUnitsReceived = 0;
            }

            lastWeeklyReorderUnitsRemaining = GetPendingReorderUnits();
            RefreshReorderState();
            currentBusiness.ResolveWeeklyBaselineThroughput();
            currentBusiness.ResolveDailyBaselineService();
            PushCashToHud();
            ResolveAutomaticBusinessCashTransfer(BusinessCashAutoTransferMode.UpperOnly);
            status = $"Weekly settlement: payroll {FormatMoney(runtimeState.LastWeeklyPayrollCents)}, reorder {FormatMoney(lastWeeklyReorderSpendCents)}.";
            lastReorderDebugSummary = BuildReorderDebugSummary(reorderQueueSummary);
            lastWeeklySettlementDebugSummary = BuildWeeklySettlementDebugSummary(cashBeforeSettlement);
            int protectedReserve = GetBusinessSurvivalCashReserveCents();
            int operatingReserve = GetEffectiveWeeklyReorderReserveCents()
                + GetEffectiveLocalIntakeAllowanceCents()
                + GetGeneralStoreFragilityBufferCents();
            playerPortfolioManager?.ResolveOwnerDistribution(
                currentBusiness,
                protectedReserve,
                timeManager != null ? Mathf.Max(0, timeManager.CurrentWeek) : -1,
                "General Store",
                operatingReserve);
            if (logBusinessCadenceDebug)
            {
                Debug.Log(lastWeeklySettlementDebugSummary + "\n" + lastReorderDebugSummary, this);
            }

            // The valuation callback above must see the complete preceding week.
            // Clear the completed sales bucket only after settlement so the next
            // week starts clean without losing six days of ordinary retail trade
            // at the calendar boundary.
            runtimeState.ResetWeekToDateSales();
        }

        public string BuildStoreSummaryText()
        {
            return BuildStoreOverviewText() + "\n\n" + BuildStoreWorkersText() + "\n" + BuildStoreStockText();
        }

        public string BuildStoreInspectionSummaryByBuildingId(int buildingId)
        {
            if (buildingId <= 0 || buildingId != storeBuildingId)
            {
                return string.Empty;
            }

            return BuildStoreInspectionSummary();
        }

        public string BuildStoreInspectionSummary()
        {
            if (runtimeState == null || currentBusiness == null || storeDefinition == null)
            {
                return string.Empty;
            }

            System.Text.StringBuilder builder = new();
            builder.AppendLine($"{currentBusiness.RuntimeDisplayName} | Player-owned | General Store");
            builder.AppendLine(BuildOperatingStatusLine());

            string blockedReason = string.IsNullOrWhiteSpace(runtimeState.LastWeeklyBlockedReason)
                ? "No current blocker"
                : runtimeState.LastWeeklyBlockedReason;
            builder.AppendLine($"Cash {FormatMoney(runtimeState.CurrentCashCents)} | Protected {FormatMoney(GetProtectedBusinessCashReserveCents())} | Stock {runtimeState.StockHealth01:P0}");
            builder.AppendLine(BuildStoreSupplyReadinessLine());
            builder.AppendLine(BuildStorePostureLine());
            builder.AppendLine($"Today {FormatMoney(runtimeState.LastDailyRevenueCents)} | Gross {FormatMoney(GetLastDailyGrossProfitCents())} | {lastDailyUnitsSold} units");
            builder.AppendLine($"Week {FormatMoney(runtimeState.WeekToDateRevenueCents)} | Gross {FormatMoney(GetWeekToDateGrossProfitCents())} | Net {FormatMoney(EstimatedWeeklyNetCashFlowCents)}");
            builder.AppendLine(BuildDemandLossLine());
            builder.AppendLine(BuildSaturdayReadinessLine());
            builder.AppendLine(BuildCashBridgeSummary());
            builder.AppendLine($"Status: {blockedReason}");
            if (logisticsRuntime != null)
            {
                builder.AppendLine(logisticsRuntime.BuildBusinessLogisticsSummary(currentBusiness.InstanceId));
            }

            if (currentBusiness.Capacity != null)
            {
                builder.AppendLine(BuildCapacitySummaryLine(currentBusiness.Capacity));
            }

            if (!string.IsNullOrWhiteSpace(lastCategoryDemandSummary))
            {
                builder.AppendLine(lastCategoryDemandSummary);
            }

            return builder.ToString().Trim();
        }


        public bool TrySetStoreMarginAdjustment(float adjustment01, out string message)
        {
            float sanitized = ClampStoreMarginAdjustment(adjustment01);
            if (Mathf.Approximately(StoreMarginAdjustment01, sanitized))
            {
                message = $"Price adjustment already {FormatSignedPercent(StoreMarginAdjustment01)}.";
                return false;
            }

            storeMarginAdjustment01 = sanitized;
            loggedMarginCompressionCategoryIds.Clear();
            status = BuildStoreMarginChangeStatus();
            message = status;
            return true;
        }

        public bool TryAdjustStoreMarginSteps(int stepDelta, out string message)
        {
            int steps = stepDelta == 0 ? 0 : (stepDelta > 0 ? 1 : -1);
            if (steps == 0)
            {
                message = $"Price adjustment {FormatSignedPercent(StoreMarginAdjustment01)}.";
                return false;
            }

            return TrySetStoreMarginAdjustment(StoreMarginAdjustment01 + steps * StoreMarginAdjustmentStep01, out message);
        }

        public int GetAverageCategorySellingPriceCents(string categoryId)
        {
            return GetAverageCategoryUnitPrice(categoryId);
        }

        public int GetAverageCategoryLandedCostCents(string categoryId)
        {
            return GetAverageCategoryLandedCost(categoryId);
        }

        public int ReceiveLocalSupply(string categoryId, int units)
        {
            return ReceiveLocalSupply(categoryId, units, GetAverageCategoryLandedCost(categoryId), "Local supply");
        }

        public int ReceiveLocalSupply(string categoryId, int units, int unitCostCents, string sourceLabel)
        {
            return ReceiveLocalSupply(categoryId, units, unitCostCents, sourceLabel, 0);
        }

        public int ReceiveLocalSupply(
            string categoryId,
            int units,
            int unitCostCents,
            string sourceLabel,
            int postPurchaseCashBufferCents)
        {
            if (!InitializeIfNeeded() || runtimeState == null)
            {
                return 0;
            }

            int requestedUnits = Mathf.Max(0, units);
            int unitCost = Mathf.Max(0, unitCostCents);
            int cashBuffer = Mathf.Max(
                GetProtectedBusinessCashReserveCents(),
                postPurchaseCashBufferCents);
            if (requestedUnits <= 0)
            {
                return 0;
            }

            int spendableCash = Mathf.Max(0, runtimeState.CurrentCashCents - cashBuffer);
            int affordableUnits = unitCost > 0
                ? Mathf.Min(requestedUnits, spendableCash / unitCost)
                : requestedUnits;
            if (affordableUnits <= 0)
            {
                string bufferSegment = cashBuffer > 0 ? $" while holding {FormatMoney(cashBuffer)} cash reserve" : string.Empty;
                status = $"Could not afford local {GetCategoryDisplayName(categoryId)} supply from {SanitizeLocalSupplySource(sourceLabel)}{bufferSegment}.";
                AppendLocalSupplySummary($"{SanitizeLocalSupplySource(sourceLabel)} skipped {GetCategoryDisplayName(categoryId)}: cash buffer");
                return 0;
            }

            int acceptedUnits = runtimeState.AddCategoryStockUnits(categoryId, affordableUnits);
            int spendCents = acceptedUnits * unitCost;
            string source = SanitizeLocalSupplySource(sourceLabel);
            if (acceptedUnits <= 0)
            {
                status = $"Skipped local {GetCategoryDisplayName(categoryId)} supply from {source}: category full.";
                AppendLocalSupplySummary($"{source} skipped {GetCategoryDisplayName(categoryId)}: category full");
                return 0;
            }

            if (acceptedUnits > 0 && spendCents > 0)
            {
                runtimeState.AddWeeklyLocalTransferCost(
                    spendCents,
                    $"bought {acceptedUnits} {GetCategoryDisplayName(categoryId)} from {source} for {FormatMoney(spendCents)}");
                lastWeeklyLocalSupplySpendCents += spendCents;
            }

            if (acceptedUnits > 0)
            {
                lastWeeklyLocalSupplyUnitsReceived += acceptedUnits;
                AppendLocalSupplySummary($"{source}: {acceptedUnits} {GetCategoryDisplayName(categoryId)} @ {FormatMoney(unitCost)}");
            }

            RefreshReorderState();
            RefreshCurrentBusinessCapacityState();
            PushCashToHud();
            if (acceptedUnits > 0)
            {
                string costSegment = spendCents > 0 ? $" for {FormatMoney(spendCents)}" : string.Empty;
                status = $"Received {acceptedUnits} local {GetCategoryDisplayName(categoryId)} units from {source}{costSegment}.";
            }

            return acceptedUnits;
        }

        public void RecordLocalSupplySkip(string categoryId, string sourceLabel, string reason)
        {
            string source = SanitizeLocalSupplySource(sourceLabel);
            string category = GetCategoryDisplayName(categoryId);
            string sanitizedReason = string.IsNullOrWhiteSpace(reason) ? "unavailable" : reason.Trim();
            AppendLocalSupplySummary($"{source} skipped {category}: {sanitizedReason}");
            status = $"Skipped local {category} supply from {source}: {sanitizedReason}.";
        }

        public int ReceiveOffMapReorderShipment(string categoryId, int units, int unitCostCents, string sourceLabel)
        {
            if (!InitializeIfNeeded() || runtimeState == null)
            {
                return 0;
            }

            int requestedUnits = Mathf.Max(0, units);
            if (requestedUnits <= 0)
            {
                return 0;
            }

            int acceptedUnits = runtimeState.ApplyDeliveredShipment(categoryId, requestedUnits);
            if (acceptedUnits <= 0)
            {
                status = $"Blocked off-map reorder intake for {GetCategoryDisplayName(categoryId)}.";
                return 0;
            }

            int spendCents = acceptedUnits * Mathf.Max(0, unitCostCents);
            runtimeState.SpendCents(spendCents);
            lastWeeklyReorderSpendCents += spendCents;
            lastWeeklyReorderUnitsReceived += acceptedUnits;
            lastWeeklyReorderUnitsRemaining = GetPendingReorderUnits();
            RefreshReorderState();
            RefreshCurrentBusinessCapacityState();
            PushCashToHud();

            string source = string.IsNullOrWhiteSpace(sourceLabel) ? "Off-map freight" : sourceLabel.Trim();
            status = $"Received {acceptedUnits} off-map {GetCategoryDisplayName(categoryId)} from {source}.";
            return acceptedUnits;
        }

        public float GetEffectiveCategoryMarkupMultiplier(string categoryId)
        {
            return GetCategoryMarkupMultiplier(categoryId);
        }

        public static float ClampStoreMarginAdjustment(float adjustment01)
        {
            return Mathf.Clamp(adjustment01, MinimumStoreMarginAdjustment01, MaximumStoreMarginAdjustment01);
        }

        public static float CalculateEffectiveCategoryMarkupMultiplier(float categoryMarkupMultiplier, float storeMarginAdjustment01)
        {
            float categoryMarkup = categoryMarkupMultiplier <= 0f ? 1f : categoryMarkupMultiplier;
            return Mathf.Max(1f, categoryMarkup + ClampStoreMarginAdjustment(storeMarginAdjustment01));
        }


        public string BuildStoreOverviewText()
        {
            if (runtimeState == null || storeDefinition == null)
            {
                return "General Store unavailable.";
            }

            System.Text.StringBuilder builder = new();
            int protectedReserve = GetProtectedBusinessCashReserveCents();
            int freeAboveReserve = Mathf.Max(0, runtimeState.CurrentCashCents - protectedReserve);
            string blockedReason = string.IsNullOrWhiteSpace(runtimeState.LastWeeklyBlockedReason)
                ? "None"
                : CompactReason(runtimeState.LastWeeklyBlockedReason);
            string autoTransfer = currentBusiness != null && currentBusiness.CashTransferRule.AutoTransferEnabled ? "On" : "Off";

            builder.AppendLine("Cash & Safety");
            builder.AppendLine($"Store Cash {FormatMoney(runtimeState.CurrentCashCents)} | Operating Reserve {FormatMoney(protectedReserve)} | Available to Transfer {FormatMoney(freeAboveReserve)}");
            builder.AppendLine($"Weekly Net {FormatMoney(EstimatedWeeklyNetCashFlowCents)} | Last Owner Draw {FormatMoney(runtimeState.LastWeeklyOwnerDistributionCents)} | Auto Transfer {autoTransfer}");
            builder.AppendLine($"Working Capital Floor: Payroll {FormatMoney(runtimeState.FilledWeeklyPayrollCents)} | Reorder {FormatMoney(GetEffectiveWeeklyReorderReserveCents())} | Local Intake {FormatMoney(GetEffectiveLocalIntakeAllowanceCents())}");
            builder.AppendLine(BuildStorePostureLine());
            builder.AppendLine();

            builder.AppendLine("Trading");
            builder.AppendLine($"Today: Sales {FormatMoney(runtimeState.LastDailyRevenueCents)} | Gross {FormatMoney(GetLastDailyGrossProfitCents())} | Units {lastDailyUnitsSold}");
            builder.AppendLine($"Week: Sales {FormatMoney(runtimeState.WeekToDateRevenueCents)} | Gross {FormatMoney(GetWeekToDateGrossProfitCents())} | Units {runtimeState.WeekToDateUnitsSold}");
            builder.AppendLine(BuildDemandLossLine());
            builder.AppendLine();

            builder.AppendLine("Operating Setup");
            builder.AppendLine($"Service {BuildOperatingStatusLine()} | Stock Health {runtimeState.StockHealth01:P0}");
            builder.AppendLine(BuildSaturdayReadinessLine());
            builder.AppendLine($"Blocked This Week: {blockedReason}");
            builder.AppendLine(BuildStorePricingLine());
            if (currentBusiness != null)
            {
                builder.AppendLine($"Focus: {currentBusiness.MainFocus.DisplayName}");
            }

            return builder.ToString();
        }


        public string BuildStoreWorkersText()
        {
            return BuildStoreWorkersText(0);
        }


        public string BuildStoreWorkersText(int selectedCandidateIndex)
        {
            if (runtimeState == null || storeDefinition == null)
            {
                return "General Store unavailable.";
            }

            System.Text.StringBuilder builder = new();
            float efficiency = GetHealthAdjustedOperatingEfficiency01();
            string fallback = UsesOwnerOperatedFallback() ? " | owner-operated fallback active" : string.Empty;

            builder.AppendLine("Staffing");
            builder.AppendLine($"Owner {ownerDisplayName}");
            builder.AppendLine($"Staffing {runtimeState.FilledWorkerCount}/{runtimeState.TargetWorkerCount} | Service Efficiency {efficiency:P0}{fallback}");

            int openSlotCount = GetOpenWorkerSlotCount();
            if (openSlotCount > 0)
            {
                builder.AppendLine($"Positions Open: {openSlotCount}");
            }

            builder.AppendLine("Current Staff");
            for (int i = 0; i < runtimeState.WorkerSlots.Count; i++)
            {
                WorkerSlotState slot = runtimeState.WorkerSlots[i];
                string worker = slot.IsFilled ? slot.AssignedWorkerDisplayName : "Open";
                string statusText = slot.IsFilled
                    ? slot.SuspendedForMissedPayroll ? "missed payroll" : slot.IsPaidActive ? "active" : "inactive"
                    : "open";
                string roleLine = $"- {GetGeneralStoreRoleDisplayName(slot)}: {worker} | {FormatMoney(slot.WeeklyWageCents)}/wk | {statusText}";

                builder.AppendLine(roleLine);
            }

            builder.AppendLine();
            AppendWorkerCandidateSummary(builder, selectedCandidateIndex);
            return builder.ToString();
        }


        public int GetAvailableWorkerCandidateCount()
        {
            return BuildAvailableWorkerCandidateList().Count;
        }

        public string BuildOperatingStatusLine()
        {
            if (runtimeState == null)
            {
                return "Operating status unavailable.";
            }

            int target = runtimeState.TargetWorkerCount;
            if (target <= 0)
            {
                return "Efficiency: 100% | No staffing required";
            }

            int active = runtimeState.ActiveWorkerCount;
            int filled = runtimeState.FilledWorkerCount;
            float efficiency = GetHealthAdjustedOperatingEfficiency01();
            if (UsesOwnerOperatedFallback())
            {
                return $"Owner-operated fallback | Efficiency {efficiency:P0} | Staffed {filled}/{target}";
            }

            if (active <= 0)
            {
                return $"Not Operating | Staffed {filled}/{target}";
            }

            if (active < target)
            {
                return $"Understaffed | Efficiency {efficiency:P0} | Staffed {filled}/{target}";
            }

            return $"Efficiency: {efficiency:P0} | Staffed {filled}/{target}";
        }

        public bool TryAssignCandidateToOpenSlot(int selectedCandidateIndex, out string message)
        {
            if (!InitializeIfNeeded() || runtimeState == null)
            {
                message = "General Store unavailable.";
                return false;
            }

            int openSlotIndex = FindFirstOpenWorkerSlotIndex();
            if (openSlotIndex < 0)
            {
                message = "No open General Store worker slots are available.";
                lastStaffingSummary = message;
                return false;
            }

            List<PersonState> candidates = BuildAvailableWorkerCandidateList();
            if (candidates.Count == 0)
            {
                message = "No available town residents meet the first-pass hiring rules.";
                lastStaffingSummary = message;
                return false;
            }

            int candidateIndex = Mathf.Clamp(selectedCandidateIndex, 0, candidates.Count - 1);
            PersonState person = candidates[candidateIndex];
            WorkerSlotState slot = runtimeState.WorkerSlots[openSlotIndex];
            int wageCents = Mathf.Max(0, slot.WeeklyWageCents);
            AssignPersonToStoreSlot(slot, person, 75);
            currentBusiness.ResolveWeeklyBaselineThroughput();
            currentBusiness.ResolveDailyBaselineService();
            populationManager?.RecalculateHouseholdIncomeAndSummaries($"{person.DisplayName} hired at the General Store.");

            message = $"Assigned {person.DisplayName} to {slot.SlotDisplayName} for {FormatMoney(wageCents)}/week.";
            lastStaffingSummary = message;
            status = message;
            Debug.Log($"[General Store][Staffing] {message}", this);
            return true;
        }

        private bool TryEnsureRequiredStoreStaffForOpening(out string message)
        {
            message = string.Empty;
            if (runtimeState == null || currentBusiness == null)
            {
                message = "General Store shell exists, but no store runtime is available.";
                return false;
            }

            if (runtimeState.RequiredWorkerCount <= 0 || runtimeState.ActiveRequiredWorkerCount >= runtimeState.RequiredWorkerCount)
            {
                return true;
            }

            int assignedCount = 0;
            string lastAssignedWorker = string.Empty;
            string lastAssignedSlot = string.Empty;
            for (int i = 0; i < runtimeState.WorkerSlots.Count; i++)
            {
                WorkerSlotState slot = runtimeState.WorkerSlots[i];
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

                PersonState candidate = FindBestAvailableWorkerCandidate(slot);
                if (candidate == null)
                {
                    message = $"General Store shell exists, but the business cannot open until a required worker is available for {slot.SlotDisplayName}.";
                    lastStaffingSummary = message;
                    return false;
                }

                AssignPersonToStoreSlot(slot, candidate, 75);
                assignedCount++;
                lastAssignedWorker = candidate.DisplayName;
                lastAssignedSlot = slot.SlotDisplayName;
            }

            if (runtimeState.RequiredWorkerCount > 0 && runtimeState.ActiveRequiredWorkerCount < runtimeState.RequiredWorkerCount)
            {
                message = "General Store shell exists, but the business cannot open until all required worker slots are active.";
                lastStaffingSummary = message;
                return false;
            }

            currentBusiness.ResolveWeeklyBaselineThroughput();
            currentBusiness.ResolveDailyBaselineService();
            if (assignedCount > 0)
            {
                populationManager?.RecalculateHouseholdIncomeAndSummaries($"{lastAssignedWorker} hired at the General Store.");
                message = assignedCount == 1
                    ? $"Assigned {lastAssignedWorker} to {lastAssignedSlot}."
                    : $"Assigned {assignedCount} required workers.";
                lastStaffingSummary = message;
            }

            return true;
        }

        private void AssignPersonToStoreSlot(WorkerSlotState slot, PersonState person, int stabilityBaselinePercent)
        {
            if (slot == null || person == null)
            {
                return;
            }

            int wageCents = Mathf.Max(0, slot.WeeklyWageCents);
            int wageDollars = Mathf.CeilToInt(wageCents / 100f);
            person.workplaceBuildingId = storeBuildingId;
            person.professionId = $"{hiredWorkerProfessionPrefix}_{slot.SlotId}";
            person.professionName = slot.SlotDisplayName;
            person.wage = new WageSnapshot
            {
                weeklyWage = wageDollars,
                stabilityPercent = NewcomerSettlementEvaluator.ResolveEmploymentStabilityPercent(person, stabilityBaselinePercent),
                currencyId = "dollars"
            };
            person.RecordVisibleWorkerRole(slot.SlotDisplayName, wageCents);
            ApprenticeshipProgressionEvaluator.RecordAssignment(person, BusinessType.GeneralStore, storeBuildingId, slot);
            slot.Assign(person.id.ToString(), person.DisplayName, wageCents);
        }

        public bool TryFireWorkerFromSlot(int selectedFilledSlotIndex, out string message)
        {
            if (!InitializeIfNeeded() || runtimeState == null)
            {
                message = "General Store unavailable.";
                return false;
            }

            int slotIndex = FindFilledWorkerSlotIndexBySelection(selectedFilledSlotIndex);
            if (slotIndex < 0)
            {
                message = "No hired General Store workers are available to fire.";
                lastStaffingSummary = message;
                return false;
            }

            WorkerSlotState slot = runtimeState.WorkerSlots[slotIndex];
            string workerId = slot.AssignedWorkerId;
            string workerName = slot.AssignedWorkerDisplayName;
            string slotName = slot.SlotDisplayName;
            slot.Unassign();
            currentBusiness.ResolveWeeklyBaselineThroughput();
            currentBusiness.ResolveDailyBaselineService();

            if (int.TryParse(workerId, out int personId) && populationManager != null && populationManager.State != null)
            {
                PersonState person = populationManager.State.GetPerson(personId);
                if (person != null && person.workplaceBuildingId == storeBuildingId)
                {
                    ResetPersonEmployment(person);
                }
            }

            message = $"Fired {workerName} from {slotName}.";
            lastStaffingSummary = message;
            status = message;
            populationManager?.RecalculateHouseholdIncomeAndSummaries(message);
            Debug.Log($"[General Store][Staffing] {message}", this);
            return true;
        }


        public string BuildStoreStockText()
        {
            if (runtimeState == null || storeDefinition == null)
            {
                return "General Store unavailable.";
            }

            System.Text.StringBuilder builder = new();
            builder.AppendLine("Stock");
            if (currentBusiness != null && currentBusiness.Capacity != null)
            {
                BusinessCapacityState capacity = currentBusiness.Capacity;
                builder.AppendLine($"Storage: {capacity.CurrentStoredUnits}/{capacity.StorageCapacityUnits} | Health {capacity.StorageHealth01:P0}");
            }

            builder.AppendLine($"Reserve Shopping: bought here {lastDailyReserveLocalUnits}u / {FormatMoney(lastDailyReserveLocalSpendCents)} | lost off-map {lastDailyReserveOffMapUnits}u / {FormatMoney(lastDailyReserveOffMapLostDemandCents)}");
            builder.AppendLine($"Local Intake: {lastWeeklyLocalSupplyUnitsReceived} units this week | {FormatMoney(lastWeeklyLocalSupplySpendCents)}");
            builder.AppendLine();
            builder.AppendLine("Supply & Freight");
            builder.AppendLine(BuildStoreSupplyReadinessLine());
            string logisticsText = BuildStoreLogisticsText();
            if (!string.IsNullOrWhiteSpace(logisticsText))
            {
                builder.AppendLine(logisticsText);
            }

            builder.AppendLine(BuildSurvivalStockPriorityLine());
            builder.AppendLine(BuildSaturdayReadinessLine());
            builder.AppendLine();
            builder.AppendLine("Category Stock");
            builder.AppendLine("<mspace=0.62em>Category              Stock     Sell      Cost      Gross / Unit  Status</mspace>");
            for (int i = 0; i < runtimeState.CategoryStock.Count; i++)
            {
                CategoryStockState category = runtimeState.CategoryStock[i];
                builder.AppendLine(BuildCategoryStockTableRow(category));
            }

            builder.AppendLine();
            builder.Append(BuildDemandOriginTableText());
            return builder.ToString();
        }

        private string BuildDemandOriginTableText()
        {
            int totalSoldUnits = Mathf.Max(0, lastDailyOnMapStoreUnitsSold + lastDailyOffMapStoreUnitsSold);
            System.Text.StringBuilder builder = new();
            builder.AppendLine("Demand Origin (Today)");
            builder.AppendLine("<mspace=0.62em>Origin                Units      Sales      Share</mspace>");
            builder.AppendLine(BuildDemandOriginTableRow("On-map households", lastDailyOnMapStoreUnitsSold, lastDailyOnMapStoreSpendCents, totalSoldUnits));
            builder.AppendLine(BuildDemandOriginTableRow("Off-map trade", lastDailyOffMapStoreUnitsSold, lastDailyOffMapStoreSpendCents, totalSoldUnits));
            builder.Append(BuildDemandOriginTableRow("Off-map leakage", lastDailyReserveOffMapUnits, lastDailyReserveOffMapLostDemandCents, totalSoldUnits, "lost"));
            return builder.ToString();
        }

        private static string BuildDemandOriginTableRow(string origin, int units, int cents, int totalSoldUnits, string shareOverride = null)
        {
            string safeOrigin = TruncateDemandOriginLabel(origin, 20).PadRight(20);
            string shareText = string.IsNullOrWhiteSpace(shareOverride)
                ? (totalSoldUnits > 0 ? $"{(units / (float)totalSoldUnits):P0}" : "0%")
                : shareOverride;
            return $"<mspace=0.62em>{safeOrigin} {Mathf.Max(0, units),6} {FormatMoney(Mathf.Max(0, cents)),10} {shareText,8}</mspace>";
        }

        private static string TruncateDemandOriginLabel(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string trimmed = value.Trim();
            if (trimmed.Length <= maxLength)
            {
                return trimmed;
            }

            if (maxLength <= 1)
            {
                return trimmed.Substring(0, maxLength);
            }

            return trimmed.Substring(0, maxLength - 1) + "…";
        }


        public string BuildStoreFinancesText()
        {
            if (runtimeState == null || storeDefinition == null)
            {
                return "General Store unavailable.";
            }

            System.Text.StringBuilder builder = new();
            builder.AppendLine($"Business Cash {FormatMoney(runtimeState.CurrentCashCents)}");
            builder.AppendLine($"Today: Revenue {FormatMoney(runtimeState.LastDailyRevenueCents)} | COGS {FormatMoney(lastDailyCostOfGoodsSoldCents)} | Gross {FormatMoney(GetLastDailyGrossProfitCents())}");
            builder.AppendLine($"Reserve demand: local {FormatMoney(lastDailyReserveLocalSpendCents)} | off-map lost {FormatMoney(lastDailyReserveOffMapLostDemandCents)}");
            builder.AppendLine($"Week: Revenue {FormatMoney(runtimeState.WeekToDateRevenueCents)} | COGS {FormatMoney(weekToDateCostOfGoodsSoldCents)} | Gross {FormatMoney(GetWeekToDateGrossProfitCents())} | Net {FormatMoney(EstimatedWeeklyNetCashFlowCents)}");
            builder.AppendLine($"Payroll {FormatMoney(runtimeState.LastWeeklyPayrollCents)}/wk | Reorder queued {FormatMoney(lastWeeklyReorderQueuedCostCents)}");
            builder.AppendLine($"Local supply {FormatMoney(lastWeeklyLocalSupplySpendCents)} | {lastWeeklyLocalSupplyUnitsReceived} units");
            builder.AppendLine(BuildStorePostureLine());
            builder.AppendLine(BuildDemandLossLine());
            builder.AppendLine(BuildCashBridgeSummary());
            if (lastWeeklyReorderTriggered || lastWeeklyReorderUnitsRemaining > 0)
            {
                builder.AppendLine($"Last reorder {FormatMoney(lastWeeklyReorderSpendCents)} | {lastWeeklyReorderUnitsReceived}/{lastWeeklyReorderUnitsQueued} units");
            }

            if (lastWeeklyCashConstrainedReorderUnits > 0)
            {
                builder.AppendLine($"Reorder held for cash: {lastWeeklyCashConstrainedReorderUnits} units | {FormatMoney(lastWeeklyCashConstrainedReorderCostCents)}");
            }

            return builder.ToString();
        }

        public string BuildStoreSupplyReadinessLine()
        {
            if (runtimeState == null)
            {
                return "Supply: store records unavailable.";
            }

            int pendingUnits = Mathf.Max(0, lastWeeklyReorderUnitsRemaining);
            int heldUnits = Mathf.Max(0, lastWeeklyCashConstrainedReorderUnits);
            if (heldUnits > 0)
            {
                return $"Supply: {heldUnits} reorder unit(s) held for Store Cash ({FormatMoney(lastWeeklyCashConstrainedReorderCostCents)}).";
            }

            if (pendingUnits > 0)
            {
                return logisticsRuntime == null
                    ? $"Supply: {pendingUnits} reorder unit(s) pending; logistics runtime unavailable."
                    : $"Supply: {pendingUnits} reorder unit(s) awaiting freight delivery.";
            }

            if (lastWeeklyReorderTriggered && lastWeeklyReorderUnitsReceived > 0)
            {
                return $"Supply: freight received {lastWeeklyReorderUnitsReceived} unit(s) this week.";
            }

            if (reorderNeeded)
            {
                return "Supply: reorder needed; waiting for weekly review.";
            }

            if (lastWeeklyLocalSupplyUnitsReceived > 0)
            {
                return $"Supply: steady, with local intake {lastWeeklyLocalSupplyUnitsReceived} unit(s) this week.";
            }

            return "Supply: steady.";
        }

        public string BuildStoreLogisticsText()
        {
            if (runtimeState == null)
            {
                return string.Empty;
            }

            System.Text.StringBuilder builder = new();
            if (lastWeeklyReorderTriggered || lastWeeklyReorderUnitsQueued > 0 || lastWeeklyReorderUnitsReceived > 0 || lastWeeklyReorderUnitsRemaining > 0)
            {
                builder.Append($"Freight: queued {lastWeeklyReorderUnitsQueued}u / received {lastWeeklyReorderUnitsReceived}u / pending {lastWeeklyReorderUnitsRemaining}u");
                if (lastWeeklyReorderQueuedCostCents > 0)
                {
                    builder.Append($" | queued value {FormatMoney(lastWeeklyReorderQueuedCostCents)}");
                }
            }

            if (lastWeeklyCashConstrainedReorderUnits > 0)
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.Append($"Store Cash hold: {lastWeeklyCashConstrainedReorderUnits}u / {FormatMoney(lastWeeklyCashConstrainedReorderCostCents)}");
            }

            if (lastWeeklyLocalSupplyUnitsReceived > 0 || lastWeeklyLocalSupplySpendCents > 0)
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.Append($"Local intake: {lastWeeklyLocalSupplyUnitsReceived}u / {FormatMoney(lastWeeklyLocalSupplySpendCents)}");
            }

            string runtimeSummary = BuildStoreLogisticsRuntimeSummaryLine();
            if (!string.IsNullOrWhiteSpace(runtimeSummary))
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.Append(runtimeSummary);
            }

            return builder.ToString();
        }

        private string BuildStoreLogisticsRuntimeSummaryLine()
        {
            if (currentBusiness == null)
            {
                return string.Empty;
            }

            logisticsRuntime ??= FindAnyObjectByType<LogisticsRuntimeManager>();
            if (logisticsRuntime == null)
            {
                return lastWeeklyReorderUnitsRemaining > 0 ? "Logistics: runtime missing; freight cannot be scheduled." : string.Empty;
            }

            string summary = logisticsRuntime.BuildBusinessLogisticsSummary(currentBusiness.InstanceId);
            return string.IsNullOrWhiteSpace(summary) ? string.Empty : summary.Trim();
        }

        public string BuildStorePolicyTelemetryText()
        {
            if (storeDefinition == null)
            {
                return "Policy telemetry unavailable.";
            }

            return ManagerPolicyEffects.BuildPolicyTelemetryLine(
                MinimumPostReorderCashBufferCents,
                storeDefinition.Business.Economy.WeeklyReorderReserveCents,
                storeDefinition.Business.Economy.LowStockWarningThreshold01,
                GetCurrentControlState(),
                GetCurrentManagerPolicy());
        }

        private void Awake()
        {
            AutoWire();
        }

        private void Start()
        {
            InitializeIfNeeded();
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

        private void OnValidate()
        {
            storeMarginAdjustment01 = ClampStoreMarginAdjustment(storeMarginAdjustment01);
        }

        private void AutoWire()
        {
            townWorld ??= FindAnyObjectByType<TownWorldController>();
            populationManager ??= FindAnyObjectByType<PopulationManager>();
            sharedBusinessRuntime ??= FindAnyObjectByType<SharedBusinessRuntimeManager>();
            timeManager ??= TimeManager.Instance != null ? TimeManager.Instance : FindAnyObjectByType<TimeManager>();
            hudController ??= FindAnyObjectByType<LandLedgersHUDController>();
            townPulseRuntime ??= FindAnyObjectByType<TownPulseRuntimeManager>();
            playerPortfolioManager ??= FindAnyObjectByType<PlayerPortfolioManager>();
            logisticsRuntime ??= FindAnyObjectByType<LogisticsRuntimeManager>();
        }

        private void SubscribeToTime()
        {
            AutoWire();
            if (subscribed || timeManager == null)
            {
                return;
            }

            timeManager.DayChanged += OnDayChanged;
            timeManager.WeekChanged += OnWeekChanged;
            timeManager.MonthChanged += OnMonthChanged;
            subscribed = true;
        }

        private void UnsubscribeFromTime()
        {
            if (!subscribed || timeManager == null)
            {
                subscribed = false;
                return;
            }

            timeManager.DayChanged -= OnDayChanged;
            timeManager.WeekChanged -= OnWeekChanged;
            timeManager.MonthChanged -= OnMonthChanged;
            subscribed = false;
        }

        private void OnDayChanged(SimulationDateChangedContext context)
        {
            ResolveDailySales();
        }

        private void OnWeekChanged(SimulationDateChangedContext context)
        {
            ResolveWeeklyPayrollAndReorder();
        }

        private void OnMonthChanged(SimulationDateChangedContext context)
        {
            runtimeState?.ResetMonthToDateNet();
            if (context.PreviousDate.Year != context.CurrentDate.Year)
            {
                runtimeState?.ResetYearToDateNet();
            }
        }

        private bool FindStoreBuilding()
        {
            storeBuildingId = -1;
            PlacedBuilding best = null;
            int bestWorkerCount = -1;
            for (int i = 0; i < townWorld.Buildings.Count; i++)
            {
                PlacedBuilding building = townWorld.Buildings[i];
                if (building.definition == null || !CanHostPlayerGeneralStore(building, GetPlot(building.plotId), out _))
                {
                    continue;
                }

                if (building.playerOwned)
                {
                    storeBuildingId = building.id;
                    return true;
                }

                int workerCount = CountPopulationWorkersAtBuilding(building.id);
                if (best == null || workerCount > bestWorkerCount)
                {
                    best = building;
                    bestWorkerCount = workerCount;
                }
            }

            if (best != null)
            {
                best.playerOwned = true;
                storeBuildingId = best.id;
                return true;
            }

            return false;
        }

        private bool CanHostPlayerGeneralStore(PlacedBuilding building, TownPlot plot, out string reason)
        {
            return BusinessSiteSuitabilityEvaluator.CanOperate(storeDefinition, building, plot, out reason);
        }

        private TownPlot GetPlot(int plotId)
        {
            if (townWorld == null || plotId < 0 || plotId >= townWorld.Plots.Count)
            {
                return null;
            }

            return townWorld.Plots[plotId];
        }

        private int CountPopulationWorkersAtBuilding(int buildingId)
        {
            PopulationState population = populationManager != null ? populationManager.State : null;
            if (population == null || population.people == null || buildingId < 0)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < population.people.Count; i++)
            {
                if (population.people[i].workplaceBuildingId == buildingId)
                {
                    count++;
                }
            }

            return count;
        }

        private void AssignStoreWorkersFromPopulation()
        {
            if (runtimeState == null || populationManager == null || populationManager.State == null)
            {
                return;
            }

            PopulationState population = populationManager.State;
            for (int i = 0; i < population.people.Count; i++)
            {
                int slotIndex = FindFirstOpenImportedWorkerSlotIndex();
                if (slotIndex < 0)
                {
                    return;
                }

                PersonState person = population.people[i];
                if (person.workplaceBuildingId != storeBuildingId)
                {
                    continue;
                }

                if (IsAlreadyAssignedToStoreSlot(person.id))
                {
                    continue;
                }

                WorkerSlotState slot = runtimeState.WorkerSlots[slotIndex];
                slot.Assign(person.id.ToString(), person.DisplayName, ResolveStoreSlotWageCents(slot));
                ApprenticeshipProgressionEvaluator.RecordAssignment(person, BusinessType.GeneralStore, storeBuildingId, slot);
            }
        }


        private void AppendWorkerCandidateSummary(System.Text.StringBuilder builder, int selectedCandidateIndex)
        {
            List<PersonState> candidates = BuildAvailableWorkerCandidateList();
            if (GetOpenWorkerSlotCount() <= 0)
            {
                builder.AppendLine("Hiring");
                builder.AppendLine("No open slots. Review the current team below.");
                int selectedSlotIndex = FindFilledWorkerSlotIndexBySelection(selectedCandidateIndex);
                if (selectedSlotIndex >= 0)
                {
                    WorkerSlotState selectedSlot = runtimeState.WorkerSlots[selectedSlotIndex];
                    builder.AppendLine($"Selected Worker: {selectedSlot.AssignedWorkerDisplayName} | {GetGeneralStoreRoleDisplayName(selectedSlot)} | {FormatMoney(selectedSlot.WeeklyWageCents)}/wk");
                }

                return;
            }

            builder.AppendLine("Hiring");
            if (candidates.Count == 0)
            {
                builder.AppendLine("Open slot remains unfilled. No eligible town candidate is available right now.");
                return;
            }

            int index = Mathf.Clamp(selectedCandidateIndex, 0, candidates.Count - 1);
            PersonState candidate = candidates[index];
            int openSlotIndex = FindFirstOpenWorkerSlotIndex();
            if (openSlotIndex >= 0 && runtimeState != null && openSlotIndex < runtimeState.WorkerSlots.Count)
            {
                WorkerSlotState openSlot = runtimeState.WorkerSlots[openSlotIndex];
                WorkerRoleFitResult fit = WorkerRoleFitEvaluator.Evaluate(candidate, BusinessType.GeneralStore, openSlot);
                builder.AppendLine($"Position Open: {GetGeneralStoreRoleDisplayName(openSlot)} | Wage {FormatMoney(openSlot.WeeklyWageCents)}/wk");
                builder.AppendLine($"Recommended candidate: {candidate.DisplayName} | {fit.Label} fit");
                builder.AppendLine("Available candidates (ranked by role fit):");
                int visibleCount = Mathf.Min(candidates.Count, 5);
                for (int i = 0; i < visibleCount; i++)
                {
                    PersonState listed = candidates[i];
                    WorkerRoleFitResult listedFit = WorkerRoleFitEvaluator.Evaluate(listed, BusinessType.GeneralStore, openSlot);
                    string marker = i == index ? "*" : "-";
                    builder.AppendLine($"{marker} {listed.DisplayName} | {listedFit.Label} fit | {listed.age} years");
                }

                if (candidates.Count > visibleCount)
                {
                    builder.AppendLine($"{candidates.Count - visibleCount} additional eligible candidate(s) are available after these ranked names.");
                }
            }
        }

        private static string GetGeneralStoreRoleDisplayName(WorkerSlotState slot)
        {
            if (slot == null)
            {
                return "Position";
            }

            if (string.Equals(slot.SlotId, "clerk_helper", StringComparison.OrdinalIgnoreCase))
            {
                return "Store Clerk";
            }

            if (string.Equals(slot.SlotId, "stock_helper", StringComparison.OrdinalIgnoreCase))
            {
                return "Stock Hand";
            }

            return string.IsNullOrWhiteSpace(slot.SlotDisplayName) ? "Position" : slot.SlotDisplayName;
        }


        private int GetOpenWorkerSlotCount()
        {
            if (runtimeState == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < runtimeState.WorkerSlots.Count; i++)
            {
                if (!runtimeState.WorkerSlots[i].IsFilled)
                {
                    count++;
                }
            }

            return count;
        }

        private int GetFilledWorkerSlotCount()
        {
            if (runtimeState == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < runtimeState.WorkerSlots.Count; i++)
            {
                if (runtimeState.WorkerSlots[i].IsFilled)
                {
                    count++;
                }
            }

            return count;
        }

        private int FindFirstOpenWorkerSlotIndex()
        {
            if (runtimeState == null)
            {
                return -1;
            }

            for (int i = 0; i < runtimeState.WorkerSlots.Count; i++)
            {
                if (!runtimeState.WorkerSlots[i].IsFilled)
                {
                    return i;
                }
            }

            return -1;
        }

        private int FindFilledWorkerSlotIndexBySelection(int selectedFilledSlotIndex)
        {
            if (runtimeState == null || runtimeState.FilledWorkerCount <= 0)
            {
                return -1;
            }

            int targetFilledIndex = WrapIndex(selectedFilledSlotIndex, runtimeState.FilledWorkerCount);
            int filledIndex = 0;
            for (int i = 0; i < runtimeState.WorkerSlots.Count; i++)
            {
                if (!runtimeState.WorkerSlots[i].IsFilled)
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

        private int FindFirstOpenImportedWorkerSlotIndex()
        {
            if (runtimeState == null || storeDefinition == null)
            {
                return -1;
            }

            ReadOnlySpan<WorkerSlotDefinition> definitions = storeDefinition.Business.WorkerSlots;
            for (int i = 0; i < runtimeState.WorkerSlots.Count && i < definitions.Length; i++)
            {
                if (definitions[i].RequiredForOpening && !runtimeState.WorkerSlots[i].IsFilled)
                {
                    return i;
                }
            }

            return -1;
        }

        private List<PersonState> BuildAvailableWorkerCandidateList()
        {
            return BuildAvailableWorkerCandidateList(GetFirstOpenWorkerSlot());
        }

        private List<PersonState> BuildAvailableWorkerCandidateList(WorkerSlotState targetSlot)
        {
            List<PersonState> candidates = new();
            PopulationState population = populationManager != null ? populationManager.State : null;
            if (population == null || population.people == null)
            {
                return candidates;
            }

            for (int i = 0; i < population.people.Count; i++)
            {
                PersonState person = population.people[i];
                if (IsAvailableWorkerCandidate(person))
                {
                    candidates.Add(person);
                }
            }

            candidates.Sort((left, right) => WorkerRoleFitEvaluator.CompareCandidates(left, right, BusinessType.GeneralStore, targetSlot));
            return candidates;
        }

        private PersonState FindBestAvailableWorkerCandidate(WorkerSlotState targetSlot)
        {
            List<PersonState> candidates = BuildAvailableWorkerCandidateList(targetSlot);
            return candidates.Count > 0 ? candidates[0] : null;
        }

        private bool IsAvailableWorkerCandidate(PersonState person)
        {
            if (person == null
                || person.id < 0
                || !NewcomerSettlementEvaluator.IsAvailableForLabor(person, minimumHireLaborAccess)
                || person.workplaceBuildingId >= 0
                || IsAlreadyAssignedToStoreSlot(person.id))
            {
                return false;
            }

            person.EnsureWorkerTraitsInitialized();
            return true;
        }

        private bool IsAlreadyAssignedToStoreSlot(int personId)
        {
            if (runtimeState == null)
            {
                return false;
            }

            string id = personId.ToString();
            for (int i = 0; i < runtimeState.WorkerSlots.Count; i++)
            {
                if (string.Equals(runtimeState.WorkerSlots[i].AssignedWorkerId, id, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private WorkerSlotState GetFirstOpenWorkerSlot()
        {
            int slotIndex = FindFirstOpenWorkerSlotIndex();
            return runtimeState != null && slotIndex >= 0 && slotIndex < runtimeState.WorkerSlots.Count
                ? runtimeState.WorkerSlots[slotIndex]
                : null;
        }

        private void ResolveTownPulseDemand(int absoluteDayIndex)
        {
            if (runtimeState == null)
            {
                return;
            }

            if (townPulseRuntime == null)
            {
                // PL-03: synthetic fallback demand removed (was: households / 6 -> revenue).
                // Aggregate town demand must not post revenue without an embodied buyer;
                // the household-level purchasing loop above already resolves real demand.
                return;
            }

            IReadOnlyList<TownPulseDemandLine> demandLines = townPulseRuntime.BeginDailyResolution(absoluteDayIndex);
            if (demandLines == null || demandLines.Count == 0)
            {
                return;
            }

            for (int i = 0; i < demandLines.Count; i++)
            {
                TownPulseDemandLine line = demandLines[i];
                if (line == null)
                {
                    continue;
                }

                int requestedUnits = line.UnitsPerDay;
                int storeFulfilledUnits = ResolveTownPulseCategoryDemand(line.CategoryId, requestedUnits);
                int supportFulfilledUnits = ResolveTownPulseSupportIndustryDemand(
                    line.CategoryId,
                    Mathf.Max(0, requestedUnits - storeFulfilledUnits));
                int fulfilledUnits = Mathf.Min(requestedUnits, storeFulfilledUnits + supportFulfilledUnits);
                townPulseRuntime.RecordDailyFulfillment(line.CategoryId, requestedUnits, fulfilledUnits);
            }

            townPulseRuntime.CompleteDailyResolution(absoluteDayIndex);
        }

        public int ResolveTownPulseCategoryDemand(string categoryId, int requestedUnits)
        {
            // Generic Retail is embodied-only; the aggregate town pulse has no
            // ActingPerson/BuyerPrincipal and must not bypass the shared sale
            // authority with a legacy stock/revenue write.
            if (currentBusiness != null && currentBusiness.BusinessType == BusinessType.Generic)
                return 0;
            if (!CanGeneralStoreSellReserveCategory(categoryId) || requestedUnits <= 0)
            {
                return 0;
            }

            int staffedRequestUnits = ScaleUnitsByOperatingEfficiency(requestedUnits);
            if (staffedRequestUnits <= 0)
            {
                RecordDemandLossBreakdown(requestedUnits, 0, requestedUnits, requestedUnits);
                return 0;
            }

            CategoryStockState stock = runtimeState.GetCategoryStock(categoryId);
            int unitsSold = Mathf.Min(staffedRequestUnits, stock.CurrentStockUnits);
            RecordDemandLossBreakdown(requestedUnits, staffedRequestUnits, requestedUnits, stock.CurrentStockUnits);
            if (unitsSold <= 0)
            {
                return 0;
            }

            int unitPrice = Mathf.Max(minimumCategoryUnitPriceCents, GetAverageCategoryUnitPrice(categoryId));
            int revenue = unitsSold * unitPrice;
            runtimeState.ResolveDailySalesPlaceholder(categoryId, unitsSold, revenue);
            lastDailyOffMapStoreUnitsSold += unitsSold;
            lastDailyOffMapStoreSpendCents += revenue;
            int costOfGoodsSold = unitsSold * GetAverageCategoryLandedCost(categoryId);
            lastDailyCostOfGoodsSoldCents += costOfGoodsSold;
            weekToDateCostOfGoodsSoldCents += costOfGoodsSold;
            lastDailyUnitsSold += unitsSold;
            return unitsSold;
        }

        private int ResolveTownPulseSupportIndustryDemand(string categoryId, int requestedUnits)
        {
            if (requestedUnits <= 0)
            {
                return 0;
            }

            AutoWire();
            if (sharedBusinessRuntime == null)
            {
                return 0;
            }

            HouseholdReserveDefinition definition = HouseholdReserveCatalog.Get(categoryId);
            if (definition == null)
            {
                return 0;
            }

            return sharedBusinessRuntime.TrySellTownPulseDemand(
                definition.CategoryId,
                definition.LocalSellerCategoryIds,
                requestedUnits,
                out _);
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

        private HouseholdReservePurchaseResult ResolveHouseholdReserveNeeds(HouseholdState household, int budgetCents)
        {
            HouseholdReservePurchaseResult result = new();
            if (household == null)
            {
                return result;
            }

            List<HouseholdReserveNeed> needs = HouseholdReserveEvaluator.BuildShoppingNeeds(household);
            if (needs.Count == 0)
            {
                return result;
            }

            int absoluteDay = timeManager != null ? timeManager.CurrentDate.AbsoluteDayIndex : -1;
            if (!ShouldHouseholdShopForReserveNeedsToday(household, needs, absoluteDay))
            {
                return result;
            }

            result.HasNeed = true;
            int remainingBudget = Mathf.Max(0, budgetCents);
            needs.Sort(CompareNeedsForBudgetedShopping);
            for (int i = 0; i < needs.Count; i++)
            {
                HouseholdReserveNeed need = needs[i];
                HouseholdReserveState reserve = need.Reserve;
                if (reserve == null || need.PurchaseUnits <= 0)
                {
                    continue;
                }

                int remainingUnits = GetSaturdayAdjustedPurchaseUnits(need.PurchaseUnits);
                LocalReserveSale storeSale = default;
                LocalReserveSale otherLocalSale = default;
                int requestedUnits = remainingUnits;
                while (remainingUnits > 0 && remainingBudget > 0)
                {
                    if (!TrySellReserveCategoryFromBestLocalSeller(
                            need.Definition,
                            need.CategoryId,
                            household.id,
                            remainingUnits,
                            ref remainingBudget,
                            out BestLocalReserveSellerSale bestSale))
                    {
                        break;
                    }

                    ApplyReservePurchase(reserve, bestSale.Sale.UnitsSold);
                    remainingUnits -= bestSale.Sale.UnitsSold;
                    if (bestSale.FromStore)
                    {
                        storeSale = storeSale.Add(bestSale.Sale);
                        result.StoreUnits += bestSale.Sale.UnitsSold;
                        result.StoreSpendCents += bestSale.Sale.SpendCents;
                    }
                    else
                    {
                        otherLocalSale = otherLocalSale.Add(bestSale.Sale);
                        result.OtherLocalUnits += bestSale.Sale.UnitsSold;
                        result.OtherLocalSpendCents += bestSale.Sale.SpendCents;
                    }
                }

                if (remainingUnits > 0)
                {
                    RecordGeneralStoreHouseholdReserveMiss(household.id, need.CategoryId, requestedUnits, remainingUnits);
                    RecordGeneralStoreMissedDemand(need.CategoryId, need.PurchaseUnits, remainingUnits);
                    int offMapValue = remainingUnits * GetOffMapReserveUnitValueCents(need.CategoryId);
                    ApplyReservePurchase(reserve, remainingUnits);
                    reserve.lastOffMapPurchaseUnits += remainingUnits;
                    result.OffMapUnits += remainingUnits;
                    result.OffMapLostDemandCents += offMapValue;
                    result.OffMapWalletSpendCents += Mathf.Min(
                        offMapValue,
                        Mathf.Max(0, household.spendingMoneyCents - result.StoreSpendCents - result.OtherLocalSpendCents - result.OffMapWalletSpendCents));
                }

                reserve.lastLocalPurchaseUnits += storeSale.UnitsSold + otherLocalSale.UnitsSold;
                reserve.RecordShoppingOutcome(
                    absoluteDay,
                    requestedUnits,
                    storeSale.UnitsSold + otherLocalSale.UnitsSold,
                    remainingUnits);
                CategoryDemandDebug.Record(
                    need.CategoryId,
                    need.PurchaseUnits,
                    storeSale.UnitsSold,
                    otherLocalSale.UnitsSold,
                    Mathf.Max(0, remainingUnits));
            }

            household.lastDailyReserveLocalPurchaseUnits = result.StoreUnits + result.OtherLocalUnits;
            household.lastDailyReserveOffMapPurchaseUnits = result.OffMapUnits;
            household.SyncLegacyFoodReserveFromReserves();
            return result;
        }

        private void EnsureWeeklySalesBucket()
        {
            if (runtimeState == null || timeManager == null)
            {
                return;
            }

            int week = timeManager.CurrentDate.Week;
            if (currentSalesWeek == week)
            {
                return;
            }

            currentSalesWeek = week;
            currentWeekCustomerHouseholds = 0;
            weekToDateCostOfGoodsSoldCents = 0;
        }

        private int GetHouseholdDailyBudgetCents(HouseholdState household)
        {
            int incomeBudget = Mathf.RoundToInt(Mathf.Max(0, household.weeklyIncomeSnapshot) * 100f / 7f * householdDailySpendShare);
            int desiredBudget = Mathf.Max(fallbackHouseholdDailyBudgetCents, incomeBudget);
            if (IsSaturdayTradeDay())
            {
                desiredBudget = Mathf.RoundToInt(desiredBudget * SaturdayHouseholdDemandMultiplier);
            }

            return Mathf.Min(desiredBudget, Mathf.Max(0, household.spendingMoneyCents));
        }

        private int GetSaturdayAdjustedPurchaseUnits(int requestedUnits)
        {
            int units = Mathf.Max(0, requestedUnits);
            return IsSaturdayTradeDay() ? Mathf.Max(units, Mathf.CeilToInt(units * SaturdayHouseholdDemandMultiplier)) : units;
        }

        private static int CompareNeedsForBudgetedShopping(HouseholdReserveNeed left, HouseholdReserveNeed right)
        {
            int priorityCompare = GetBudgetShoppingPriority(left).CompareTo(GetBudgetShoppingPriority(right));
            if (priorityCompare != 0)
            {
                return priorityCompare;
            }

            int urgencyCompare = right.Urgency01.CompareTo(left.Urgency01);
            if (urgencyCompare != 0)
            {
                return urgencyCompare;
            }

            return string.Compare(left.CategoryId, right.CategoryId, StringComparison.OrdinalIgnoreCase);
        }

        private static int GetBudgetShoppingPriority(HouseholdReserveNeed need)
        {
            if (HouseholdHeatingEvaluator.IsFuelReserve(need.CategoryId))
            {
                return 0;
            }

            if (string.Equals(need.CategoryId, HouseholdReserveCatalog.StapleFoodCategoryId, StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            if (need.Definition == null)
            {
                return 9;
            }

            return need.Definition.DemandGroup switch
            {
                RetailDemandGroup.Medicine => 2,
                RetailDemandGroup.FoodProtein => 3,
                RetailDemandGroup.DailyStaple => 4,
                RetailDemandGroup.Household => string.Equals(need.CategoryId, "household_goods", StringComparison.OrdinalIgnoreCase) ? 5 : 6,
                RetailDemandGroup.Hardware => 7,
                RetailDemandGroup.Clothing => 8,
                _ => 9
            };
        }

        private bool ShouldHouseholdShopForReserveNeedsToday(HouseholdState household, IReadOnlyList<HouseholdReserveNeed> needs, int absoluteDay)
        {
            if (household == null || needs == null || needs.Count == 0 || absoluteDay < 0)
            {
                return true;
            }

            float maxUrgency = 0f;
            float maxFrustration = 0f;
            int mostRecentAttempt = -1;
            for (int i = 0; i < needs.Count; i++)
            {
                HouseholdReserveNeed need = needs[i];
                maxUrgency = Mathf.Max(maxUrgency, need.Urgency01);
                if (need.Reserve != null)
                {
                    maxFrustration = Mathf.Max(maxFrustration, need.Reserve.stockoutFrustration01);
                    mostRecentAttempt = Mathf.Max(mostRecentAttempt, need.Reserve.lastShoppingAttemptDayIndex);
                }
            }

            if (maxUrgency >= 0.72f || maxFrustration >= 0.55f)
            {
                return true;
            }

            int daysSinceAttempt = mostRecentAttempt < 0 ? int.MaxValue : absoluteDay - mostRecentAttempt;
            int cadenceDays = maxUrgency >= 0.45f ? 2 : 3;
            int householdOffset = Mathf.Abs(household.id * 37 + absoluteDay * 11) % 2;
            return daysSinceAttempt >= cadenceDays + householdOffset;
        }

        private void ApplyReservePurchase(HouseholdReserveState reserve, int units)
        {
            if (reserve == null || units <= 0)
            {
                return;
            }

            reserve.currentUnits = Mathf.Clamp(reserve.currentUnits + units, 0, reserve.targetUnits);
        }

        private void ReleaseSuspendedPayrollWorkersFromPopulation()
        {
            if (runtimeState == null || runtimeState.LastSuspendedPayrollWorkerIds.Count == 0 || populationManager == null || populationManager.State == null)
            {
                return;
            }

            int released = 0;
            for (int i = 0; i < runtimeState.LastSuspendedPayrollWorkerIds.Count; i++)
            {
                string suspendedWorkerId = runtimeState.LastSuspendedPayrollWorkerIds[i];
                if (!int.TryParse(suspendedWorkerId, out int personId))
                {
                    continue;
                }

                PersonState person = populationManager.State.GetPerson(personId);
                if (person == null || person.workplaceBuildingId != storeBuildingId)
                {
                    continue;
                }

                ResetPersonEmployment(person);
                UnassignStoreWorkerSlot(suspendedWorkerId);
                released++;
            }

            if (released > 0)
            {
                lastStaffingSummary = $"{released} worker(s) suspended after missed payroll.";
                populationManager.RecalculateHouseholdIncomeAndSummaries(lastStaffingSummary);
            }
        }

        private void UnassignStoreWorkerSlot(string workerId)
        {
            if (runtimeState == null || string.IsNullOrWhiteSpace(workerId))
            {
                return;
            }

            for (int i = 0; i < runtimeState.WorkerSlots.Count; i++)
            {
                WorkerSlotState slot = runtimeState.WorkerSlots[i];
                if (string.Equals(slot.AssignedWorkerId, workerId, StringComparison.Ordinal))
                {
                    slot.Unassign();
                    return;
                }
            }
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

        private void DepositWeeklyHouseholdIncome()
        {
            PopulationState population = populationManager != null ? populationManager.State : null;
            if (population == null || population.households == null)
            {
                return;
            }

            for (int i = 0; i < population.households.Count; i++)
            {
                HouseholdState household = population.households[i];
                int deposit = Mathf.Max(weeklyHouseholdAllowanceFallbackCents, household.weeklyIncomeSnapshot * 100);
                household.spendingMoneyCents += deposit;
                HouseholdUpgradeEconomyEvaluator.ApplyWeeklySettlementEffects(household);
            }
        }

        private int EvaluateHouseholdShoppingPriority(HouseholdState household)
        {
            if (household == null)
            {
                return 0;
            }

            household.EnsureHouseholdReservesInitialized();
            if (household.spendingMoneyCents <= 0)
            {
                household.demandSnapshot = HouseholdReserveEvaluator.BuildDemandSnapshot(household);
                return 0;
            }

            HouseholdDemandSnapshot demand = HouseholdReserveEvaluator.BuildDemandSnapshot(household);
            household.demandSnapshot = demand;

            bool reserveNeed = HouseholdReserveEvaluator.HasShoppingNeed(household);
            int needScore = Mathf.Max(0, demand.foodNeed) * 3
                + Mathf.Max(0, demand.generalGoodsNeed) * 2
                + Mathf.Max(0, demand.medicineNeed) * 4;
            if (reserveNeed)
            {
                needScore += 10;
            }

            int discretionaryScore = 0;
            if (ShouldTriggerDiscretionarySaturdayTrip(household, demand, reserveNeed))
            {
                discretionaryScore = household.spendingMoneyCents >= weeklyHouseholdAllowanceFallbackCents
                    ? 5
                    : household.spendingMoneyCents >= fallbackHouseholdDailyBudgetCents
                        ? 4
                        : 3;
            }

            return needScore + discretionaryScore;
        }

        private bool ShouldTriggerDiscretionarySaturdayTrip(
            HouseholdState household,
            HouseholdDemandSnapshot demand,
            bool reserveNeed)
        {
            if (household == null || !IsSaturdayTradeDay() || household.spendingMoneyCents <= 0)
            {
                return false;
            }

            if (reserveNeed
                || demand.foodNeed > 0
                || demand.generalGoodsNeed > 0
                || demand.medicineNeed > 0)
            {
                return true;
            }

            int spendBand = household.spendingMoneyCents >= weeklyHouseholdAllowanceFallbackCents
                ? 3
                : household.spendingMoneyCents >= fallbackHouseholdDailyBudgetCents
                    ? 2
                    : 1;
            int absoluteDayIndex = timeManager != null ? timeManager.CurrentDate.AbsoluteDayIndex : 0;
            int roll = PositiveModulo(household.id * 31 + absoluteDayIndex * 17 + spendBand * 13, 5);
            return roll < spendBand;
        }

        private static int PositiveModulo(int value, int divisor)
        {
            if (divisor <= 0)
            {
                return 0;
            }

            int result = value % divisor;
            return result < 0 ? result + divisor : result;
        }

        private HouseholdState GetHousehold(int householdId)
        {
            PopulationState population = populationManager != null ? populationManager.State : null;
            if (population == null || population.households == null)
            {
                return null;
            }

            for (int i = 0; i < population.households.Count; i++)
            {
                if (population.households[i].id == householdId)
                {
                    return population.households[i];
                }
            }

            return null;
        }

        private int GetTotalHouseholdSpendingMoneyCents()
        {
            PopulationState population = populationManager != null ? populationManager.State : null;
            if (population == null || population.households == null)
            {
                return 0;
            }

            int total = 0;
            for (int i = 0; i < population.households.Count; i++)
            {
                total += Mathf.Max(0, population.households[i].spendingMoneyCents);
            }

            return total;
        }

        private bool IsSaturdayTradeDay()
        {
            return timeManager != null && timeManager.CurrentDate.DayOfWeekIndex == SaturdayDayOfWeekIndex;
        }

        private void EnsureStartupWorkingCapital()
        {
            if (runtimeState == null)
            {
                return;
            }

            int minimumOpeningCash = GetBusinessSurvivalCashReserveCents() + StartupSurplusAboveSurvivalReserveCents;
            if (runtimeState.CurrentCashCents < minimumOpeningCash)
            {
                runtimeState.AddCashCents(minimumOpeningCash - runtimeState.CurrentCashCents);
            }
        }

        private void RecordGeneralStoreMissedDemand(string categoryId, int requestedUnits, int missedUnits)
        {
            if (currentBusiness == null || runtimeState == null || missedUnits <= 0)
            {
                return;
            }

            int requested = Mathf.Max(requestedUnits, missedUnits);
            int sold = Mathf.Max(0, requested - missedUnits);
            float severity = requested <= 0 ? 1f : Mathf.Clamp01(missedUnits / (float)requested);
            if (IsSaturdayTradeDay())
            {
                severity = Mathf.Clamp01(severity * SaturdayStockoutSeverityMultiplier);
            }

            CategoryStockState stock = runtimeState.GetCategoryStock(categoryId);
            currentBusiness.RecordBusinessReputationObservation(
                new BusinessReputationObservation(
                    requested,
                    sold,
                    GetAverageCategoryUnitPrice(categoryId),
                    GetAverageCategoryLandedCost(categoryId),
                    stock != null ? stock.StockHealth01 : runtimeState.StockHealth01,
                    runtimeState.Reliability01,
                    GetHealthAdjustedOperatingEfficiency01(),
                    categoryId,
                    timeManager != null ? timeManager.CurrentDate.AbsoluteDayIndex : -1,
                    timeManager != null ? Mathf.Max(0, timeManager.CurrentWeek) : -1));
        }

        private void RecordDemandLossBreakdown(int requestedUnits, int serviceCapUnits, int priceCapUnits, int stockCapUnits)
        {
            int unresolved = Mathf.Max(0, requestedUnits);
            int afterService = Mathf.Min(unresolved, Mathf.Max(0, serviceCapUnits));
            if (afterService < unresolved)
            {
                lastDailyMissedServiceUnits += unresolved - afterService;
            }

            unresolved = afterService;
            int afterPrice = Mathf.Min(unresolved, Mathf.Max(0, priceCapUnits));
            if (afterPrice < unresolved)
            {
                lastDailyMissedPriceUnits += unresolved - afterPrice;
            }

            unresolved = afterPrice;
            int afterStock = Mathf.Min(unresolved, Mathf.Max(0, stockCapUnits));
            if (afterStock < unresolved)
            {
                lastDailyMissedStockUnits += unresolved - afterStock;
            }
        }

        private int GetPendingReorderUnits()
        {
            if (runtimeState == null)
            {
                return 0;
            }

            int total = 0;
            for (int i = 0; i < runtimeState.CategoryStock.Count; i++)
            {
                total += runtimeState.CategoryStock[i].PendingReorderUnits;
            }

            return total;
        }

        private bool HasPendingEssentialReorders()
        {
            if (runtimeState == null)
            {
                return false;
            }

            for (int i = 0; i < runtimeState.CategoryStock.Count; i++)
            {
                CategoryStockState category = runtimeState.CategoryStock[i];
                if (category != null
                    && IsSurvivalPriorityCategory(category.CategoryId)
                    && category.PendingReorderUnits > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private void RecordCashConstrainedReorderRemainder()
        {
            if (runtimeState == null)
            {
                return;
            }

            int units = 0;
            int cost = 0;
            for (int i = 0; i < runtimeState.CategoryStock.Count; i++)
            {
                CategoryStockState category = runtimeState.CategoryStock[i];
                if (category == null || category.PendingReorderUnits <= 0)
                {
                    continue;
                }

                if (!IsSurvivalPriorityCategory(category.CategoryId) && IsLowCashTriageActive())
                {
                    continue;
                }

                units += category.PendingReorderUnits;
                cost += category.PendingReorderUnits * GetAverageCategoryLandedCost(category.CategoryId);
            }

            lastWeeklyCashConstrainedReorderUnits = Mathf.Max(0, units);
            lastWeeklyCashConstrainedReorderCostCents = Mathf.Max(0, cost);
        }

        private float GetEffectiveReorderThreshold01()
        {
            float baseThreshold = storeDefinition != null
                ? storeDefinition.Business.Economy.LowStockWarningThreshold01
                : 0f;
            return ManagerPolicyEffects.CalculateReorderThreshold01(
                baseThreshold,
                GetCurrentControlState(),
                GetCurrentManagerPolicy());
        }

        private int GetEffectiveWeeklyReorderReserveCents(int fallbackCents = 0)
        {
            int baseReserve = storeDefinition != null
                ? storeDefinition.Business.Economy.WeeklyReorderReserveCents
                : Mathf.Max(0, fallbackCents);
            return ManagerPolicyEffects.CalculateReorderBudgetCents(
                baseReserve,
                GetCurrentControlState(),
                GetCurrentManagerPolicy());
        }

        private int GetEffectivePostReorderCashBufferCents()
        {
            return ManagerPolicyEffects.CalculateCashReserveCents(
                MinimumPostReorderCashBufferCents,
                GetCurrentControlState(),
                GetCurrentManagerPolicy());
        }

        private int GetEffectiveLocalIntakeAllowanceCents()
        {
            return ManagerPolicyEffects.CalculateReorderBudgetCents(
                MinimumLocalIntakeAllowanceCents,
                GetCurrentControlState(),
                GetCurrentManagerPolicy());
        }

        private int GetProtectedBusinessCashReserveCents()
        {
            return GetBaseBusinessSurvivalCashReserveCents() + GetGeneralStoreFragilityBufferCents();
        }

        private int GetBaseBusinessSurvivalCashReserveCents()
        {
            return PlayerPortfolioManager.CalculateSurvivalReserveCents(
                GetEffectivePostReorderCashBufferCents(),
                runtimeState != null ? runtimeState.FilledWeeklyPayrollCents : 0,
                GetEffectiveWeeklyReorderReserveCents(),
                GetEffectiveLocalIntakeAllowanceCents());
        }

        private int GetBusinessSurvivalCashReserveCents()
        {
            return GetProtectedBusinessCashReserveCents();
        }

        private int GetReorderProtectionCashBufferCents()
        {
            return GetEffectivePostReorderCashBufferCents()
                + (runtimeState != null ? runtimeState.FilledWeeklyPayrollCents : 0)
                + GetEffectiveLocalIntakeAllowanceCents()
                + GetGeneralStoreFragilityBufferCents();
        }

        private int GetGeneralStoreFragilityBufferCents()
        {
            if (runtimeState == null)
            {
                return 0;
            }

            int buffer = 0;
            bool isEarlyStore = currentSalesWeek < 1
                || (timeManager != null && Mathf.Max(0, timeManager.CurrentWeek) <= 1);
            if (isEarlyStore)
            {
                buffer += FragileStoreCashReserveCents;
            }

            bool weakTrend = runtimeState.LastWeeklyCashAfterCents < runtimeState.LastWeeklyCashBeforeCents
                || EstimatedWeeklyNetCashFlowCents < 0
                || runtimeState.WeekToDateRevenueCents < (runtimeState.FilledWeeklyPayrollCents * 2);
            if (weakTrend)
            {
                buffer += WeakTrendCashReserveCents;
            }

            if (GetCriticalEssentialCategoryCount() > 0)
            {
                buffer += WeakTrendCashReserveCents;
            }

            if (IsSaturdayPreparationWindow() && !string.IsNullOrEmpty(BuildProjectedSaturdayShortfallSummary()))
            {
                buffer += WeakTrendCashReserveCents;
            }

            return buffer;
        }

        private int ResolveAutomaticBusinessCashTransfer(BusinessCashAutoTransferMode mode)
        {
            if (playerPortfolioManager == null || currentBusiness == null || runtimeState == null)
            {
                return 0;
            }

            if (mode == BusinessCashAutoTransferMode.UpperOnly && IsLowCashTriageActive())
            {
                return 0;
            }

            int weekKey = timeManager != null ? Mathf.Max(0, timeManager.CurrentWeek) : -1;
            if (playerPortfolioManager.TryResolveAutomaticBusinessCashTransfer(
                    currentBusiness,
                    GetBusinessSurvivalCashReserveCents(),
                    weekKey,
                    mode,
                    out int transferred,
                    out string message))
            {
                status = message;
                PushCashToHud();
                return transferred;
            }

            if (!string.IsNullOrWhiteSpace(message))
            {
                status = message;
            }

            return 0;
        }

        private float GetManagerPolicyMarginAdjustment01()
        {
            return ManagerPolicyEffects.CalculateMarginAdjustment01(GetCurrentControlState(), GetCurrentManagerPolicy());
        }

        private BusinessControlState GetCurrentControlState()
        {
            return currentBusiness != null ? currentBusiness.ControlState : BusinessControlState.PlayerManaged;
        }

        private ManagerPolicyPreset GetCurrentManagerPolicy()
        {
            return currentBusiness != null ? currentBusiness.ManagerPolicy : ManagerPolicyPreset.StabilityFirst;
        }

        private int CalculatePendingReorderCostCents()
        {
            if (runtimeState == null)
            {
                return 0;
            }

            int total = 0;
            for (int i = 0; i < runtimeState.CategoryStock.Count; i++)
            {
                CategoryStockState category = runtimeState.CategoryStock[i];
                total += category.PendingReorderUnits * GetAverageCategoryLandedCost(category.CategoryId);
            }

            return Mathf.Max(0, total);
        }

        private void ResolveCostedPendingReorders(int availableCashCents)
        {
            lastWeeklyReorderSpendCents = 0;
            lastWeeklyReorderUnitsReceived = 0;
            lastWeeklyCashConstrainedReorderUnits = 0;
            lastWeeklyCashConstrainedReorderCostCents = 0;

            if (runtimeState == null || availableCashCents <= 0)
            {
                return;
            }

            logisticsRuntime ??= FindAnyObjectByType<LogisticsRuntimeManager>();
            if (logisticsRuntime == null || currentBusiness == null)
            {
                return;
            }

            int weeklyReserve = GetEffectiveWeeklyReorderReserveCents(availableCashCents);
            int essentialQueuedCost = CalculateEssentialPendingReorderCostCents();
            int remainingCash = CalculateSurvivalAwareReorderSpendBudgetCents(
                availableCashCents,
                weeklyReserve,
                GetReorderProtectionCashBufferCents(),
                essentialQueuedCost,
                runtimeState.StockHealth01);
            IReadOnlyList<CategoryStockState> reorderCategories = GetSurvivalOrderedReorderCategories();
            bool triageActive = IsLowCashTriageActive();
            for (int i = 0; i < reorderCategories.Count; i++)
            {
                CategoryStockState category = reorderCategories[i];
                int pendingUnits = category.PendingReorderUnits;
                if (pendingUnits <= 0)
                {
                    continue;
                }

                int landedCost = GetAverageCategoryLandedCost(category.CategoryId);
                if (landedCost <= 0)
                {
                    continue;
                }

                if (triageActive
                    && !IsSurvivalPriorityCategory(category.CategoryId)
                    && HasPendingEssentialReorders())
                {
                    continue;
                }

                int affordableUnits = Mathf.Min(pendingUnits, remainingCash / landedCost);
                if (affordableUnits <= 0)
                {
                    continue;
                }

                int queuedUnits = runtimeState.AllocatePendingReorderUnits(category.CategoryId, affordableUnits);
                if (queuedUnits <= 0)
                {
                    continue;
                }

                LogisticsShipmentState shipment = logisticsRuntime.CreateOffMapInboundShipment(
                    currentBusiness,
                    category.CategoryId,
                    queuedUnits,
                    landedCost,
                    LogisticsShipmentDeliveryMode.ReceivePendingReorder,
                    $"Regional freight {GetCategoryDisplayName(category.CategoryId)}");
                if (shipment == null || shipment.RoutePlan == null || shipment.RoutePlan.VisiblePath == null || shipment.RoutePlan.VisiblePath.Count <= 0)
                {
                    runtimeState.GetCategoryStock(category.CategoryId)?.QueueReorderToTarget();
                    continue;
                }

                int queuedCost = queuedUnits * landedCost;
                remainingCash -= queuedCost;
                lastWeeklyReorderUnitsQueued += queuedUnits;
            }

            RecordCashConstrainedReorderRemainder();
        }

        private IReadOnlyList<CategoryStockState> GetSurvivalOrderedReorderCategories()
        {
            if (runtimeState == null)
            {
                return Array.Empty<CategoryStockState>();
            }

            List<CategoryStockState> ordered = new();
            AddReorderCategoryIfPresent(ordered, "staple_food");
            AddReorderCategoryIfPresent(ordered, "meat");
            AddReorderCategoryIfPresent(ordered, "household_goods");
            AddReorderCategoryIfPresent(ordered, "medicine_remedies");
            AddReorderCategoryIfPresent(ordered, "tools_hardware");
            AddReorderCategoryIfPresent(ordered, "clothing");

            for (int i = 0; i < runtimeState.CategoryStock.Count; i++)
            {
                CategoryStockState category = runtimeState.CategoryStock[i];
                if (category == null || ContainsCategory(ordered, category.CategoryId))
                {
                    continue;
                }

                ordered.Add(category);
            }

            ordered.Sort(CompareReorderPriority);

            return ordered;
        }

        private void AddReorderCategoryIfPresent(List<CategoryStockState> ordered, string categoryId)
        {
            CategoryStockState category = runtimeState != null ? runtimeState.GetCategoryStock(categoryId) : null;
            if (category != null && !ContainsCategory(ordered, category.CategoryId))
            {
                ordered.Add(category);
            }
        }

        private static bool ContainsCategory(IReadOnlyList<CategoryStockState> categories, string categoryId)
        {
            if (categories == null || string.IsNullOrWhiteSpace(categoryId))
            {
                return false;
            }

            for (int i = 0; i < categories.Count; i++)
            {
                if (categories[i] != null
                    && string.Equals(categories[i].CategoryId, categoryId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private int CompareReorderPriority(CategoryStockState left, CategoryStockState right)
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

            int essentialCompare = GetEssentialReorderBand(right.CategoryId).CompareTo(GetEssentialReorderBand(left.CategoryId));
            if (essentialCompare != 0)
            {
                return essentialCompare;
            }

            int shortfallCompare = GetProjectedSaturdayShortfallUnits(right).CompareTo(GetProjectedSaturdayShortfallUnits(left));
            if (shortfallCompare != 0)
            {
                return shortfallCompare;
            }

            int demandCompare = GetRecentDemandSignalUnits(right).CompareTo(GetRecentDemandSignalUnits(left));
            if (demandCompare != 0)
            {
                return demandCompare;
            }

            int baseRankCompare = ResolveBaseReorderRank(left.CategoryId).CompareTo(ResolveBaseReorderRank(right.CategoryId));
            if (baseRankCompare != 0)
            {
                return baseRankCompare;
            }

            return string.Compare(left.CategoryId, right.CategoryId, StringComparison.OrdinalIgnoreCase);
        }

        private static int GetEssentialReorderBand(string categoryId)
        {
            return IsSurvivalPriorityCategory(categoryId) ? 1 : 0;
        }

        private static int ResolveBaseReorderRank(string categoryId)
        {
            if (string.Equals(categoryId, "staple_food", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (string.Equals(categoryId, "meat", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            if (string.Equals(categoryId, "household_goods", StringComparison.OrdinalIgnoreCase))
            {
                return 2;
            }

            if (string.Equals(categoryId, "medicine_remedies", StringComparison.OrdinalIgnoreCase))
            {
                return 3;
            }

            if (string.Equals(categoryId, "tools_hardware", StringComparison.OrdinalIgnoreCase))
            {
                return 4;
            }

            if (string.Equals(categoryId, "clothing", StringComparison.OrdinalIgnoreCase))
            {
                return 5;
            }

            return 100;
        }

        public static int CalculateWeeklyReorderSpendBudgetCents(int availableCashCents, int weeklyReorderReserveCents)
        {
            return CalculateWeeklyReorderSpendBudgetCents(
                availableCashCents,
                weeklyReorderReserveCents,
                MinimumPostReorderCashBufferCents);
        }

        public static int CalculateWeeklyReorderSpendBudgetCents(
            int availableCashCents,
            int weeklyReorderReserveCents,
            int postReorderCashBufferCents)
        {
            int spendableAfterBuffer = Mathf.Max(0, Mathf.Max(0, availableCashCents) - Mathf.Max(0, postReorderCashBufferCents));
            return Mathf.Min(Mathf.Max(0, weeklyReorderReserveCents), spendableAfterBuffer);
        }

        public static int CalculateSurvivalAwareReorderSpendBudgetCents(
            int availableCashCents,
            int weeklyReorderReserveCents,
            int postReorderCashBufferCents,
            int essentialQueuedCostCents,
            float stockHealth01)
        {
            int ordinaryBudget = CalculateWeeklyReorderSpendBudgetCents(
                availableCashCents,
                weeklyReorderReserveCents,
                postReorderCashBufferCents);
            if (ordinaryBudget > 0)
            {
                return ordinaryBudget;
            }

            if (Mathf.Clamp01(stockHealth01) > CriticalStockHealthThreshold01 || essentialQueuedCostCents <= 0)
            {
                return 0;
            }

            int emergencySpendable = Mathf.Max(0, Mathf.Max(0, availableCashCents) - FragileStoreCashReserveCents);
            int cappedLifeline = Mathf.RoundToInt(Mathf.Max(0, weeklyReorderReserveCents) * 0.35f);
            return Mathf.Min(Mathf.Max(0, essentialQueuedCostCents), emergencySpendable, cappedLifeline);
        }

        private int CalculateEssentialPendingReorderCostCents()
        {
            if (runtimeState == null)
            {
                return 0;
            }

            int total = 0;
            for (int i = 0; i < runtimeState.CategoryStock.Count; i++)
            {
                CategoryStockState category = runtimeState.CategoryStock[i];
                if (category == null || category.PendingReorderUnits <= 0 || !IsSurvivalPriorityCategory(category.CategoryId))
                {
                    continue;
                }

                total += category.PendingReorderUnits * GetAverageCategoryLandedCost(category.CategoryId);
            }

            return Mathf.Max(0, total);
        }

        private string BuildPendingReorderSummary()
        {
            if (runtimeState == null)
            {
                return "Stock unavailable.";
            }

            System.Text.StringBuilder builder = new();
            for (int i = 0; i < runtimeState.CategoryStock.Count; i++)
            {
                CategoryStockState category = runtimeState.CategoryStock[i];
                if (category.PendingReorderUnits <= 0)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append("; ");
                }

                builder.Append(GetCategoryDisplayName(category.CategoryId));
                builder.Append(": ");
                builder.Append(category.PendingReorderUnits);
                builder.Append(" units");
            }

            return builder.Length == 0 ? "No categories queued." : builder.ToString();
        }

        private string BuildDailySalesDebugSummary()
        {
            string date = timeManager != null ? timeManager.CurrentDate.ToString() : "No date";
            return "[General Store][Daily] "
                + $"{date}: {lastDailyCustomerHouseholds}/{generatedHouseholdCount} households bought "
                + $"{lastDailyUnitsSold} units for {FormatMoney(runtimeState != null ? runtimeState.LastDailyRevenueCents : 0)}. "
                + $"COGS {FormatMoney(lastDailyCostOfGoodsSoldCents)}; gross {FormatMoney(GetLastDailyGrossProfitCents())}. "
                + $"Reserve demand local {lastDailyReserveLocalUnits} units / {FormatMoney(lastDailyReserveLocalSpendCents)}; "
                + $"off-map {lastDailyReserveOffMapUnits} units / {FormatMoney(lastDailyReserveOffMapLostDemandCents)} lost. "
                + $"Store wallet spend {FormatMoney(lastDailyWalletSpendCents)}; household wallets "
                + $"{FormatMoney(lastDailyHouseholdWalletBeforeCents)} -> {FormatMoney(lastDailyHouseholdWalletAfterCents)}. "
                + $"Store cash {FormatMoney(runtimeState != null ? runtimeState.CurrentCashCents : 0)}.";
        }

        private string BuildWeeklySettlementDebugSummary(int cashBeforeSettlement)
        {
            string date = timeManager != null ? timeManager.CurrentDate.ToString() : "No date";
            return "[General Store][Weekly] "
                + $"{date}: cash {FormatMoney(cashBeforeSettlement)} -> {FormatMoney(runtimeState != null ? runtimeState.CurrentCashCents : 0)}, "
                + $"net {FormatMoney((runtimeState != null ? runtimeState.CurrentCashCents : 0) - cashBeforeSettlement)}, "
                + $"payroll {FormatMoney(runtimeState != null ? runtimeState.LastWeeklyPayrollCents : 0)}, "
                + $"reorder spend {FormatMoney(lastWeeklyReorderSpendCents)}, "
                + $"cash-held reorder {lastWeeklyCashConstrainedReorderUnits}u/{FormatMoney(lastWeeklyCashConstrainedReorderCostCents)}, "
                + $"week gross {FormatMoney(GetWeekToDateGrossProfitCents())}, "
                + $"household wallet pool {FormatMoney(GetTotalHouseholdSpendingMoneyCents())}.";
        }

        private string BuildReorderDebugSummary(string reorderQueueSummary)
        {
            return "[General Store][Reorder] "
                + $"Triggered={(lastWeeklyReorderTriggered ? "yes" : "no")}; "
                + $"reserve={FormatMoney(GetEffectiveWeeklyReorderReserveCents())}; "
                + $"buffer={FormatMoney(GetEffectivePostReorderCashBufferCents())}; "
                + $"essentialQueued={FormatMoney(CalculateEssentialPendingReorderCostCents())}; "
                + $"queuedCost={FormatMoney(lastWeeklyReorderQueuedCostCents)}; "
                + $"spent={FormatMoney(lastWeeklyReorderSpendCents)}; "
                + $"cashHeld={lastWeeklyCashConstrainedReorderUnits} units/{FormatMoney(lastWeeklyCashConstrainedReorderCostCents)}; "
                + $"queued={lastWeeklyReorderUnitsQueued} units; received={lastWeeklyReorderUnitsReceived} units; remaining={lastWeeklyReorderUnitsRemaining} units. "
                + $"Queued categories: {reorderQueueSummary}";
        }

        private static int ResolveStoreSlotWageCents(WorkerSlotState slot)
        {
            return slot != null ? slot.WeeklyWageCents : 0;
        }

        private string BuildOccupancySummaryLine()
        {
            if (townWorld == null || populationManager == null || populationManager.State == null)
            {
                return "Occupancy: unavailable";
            }

            int homeSlots = 0;
            int mixedUseHomeSlots = 0;
            for (int i = 0; i < townWorld.Buildings.Count; i++)
            {
                BuildingDefinition definition = townWorld.Buildings[i].definition;
                if (definition == null)
                {
                    continue;
                }

                int capacity = definition.ResidentHouseholdCapacity;
                homeSlots += capacity;
                if (definition.IsMixedUse || definition.UsesUpperFloorResidential)
                {
                    mixedUseHomeSlots += capacity;
                }
            }

            int occupiedHomeSlots = populationManager.State.households != null ? populationManager.State.households.Count : 0;
            int mixedUseOccupied = CountMixedUseOccupiedHouseholds();
            return $"Occupancy: {occupiedHomeSlots}/{homeSlots} homes | Mixed-use: {mixedUseOccupied}/{mixedUseHomeSlots} | Boarding {populationManager.BoardingUsed}/{populationManager.BoardingCapacity} | Housing pressure {populationManager.HousingPressure01:P0}";
        }

        private int CountMixedUseOccupiedHouseholds()
        {
            PopulationState population = populationManager != null ? populationManager.State : null;
            if (population == null || population.households == null || townWorld == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < population.households.Count; i++)
            {
                int buildingId = population.households[i].homeBuildingId;
                if (buildingId < 0 || buildingId >= townWorld.Buildings.Count)
                {
                    continue;
                }

                BuildingDefinition definition = townWorld.Buildings[buildingId].definition;
                if (definition != null && (definition.IsMixedUse || definition.UsesUpperFloorResidential))
                {
                    count++;
                }
            }

            return count;
        }

        private LocalReserveSale TrySellReserveCategoryFromGeneralStore(string categoryId, int requestedUnits, ref int remainingBudgetCents)
        {
            return TrySellReserveCategoryFromGeneralStore(categoryId, -1, requestedUnits, ref remainingBudgetCents);
        }

        private LocalReserveSale TrySellReserveCategoryFromGeneralStore(string categoryId, int householdId, int requestedUnits, ref int remainingBudgetCents)
        {
            if (TrySellFromGenericRetail(categoryId, householdId, requestedUnits, ref remainingBudgetCents, out LocalReserveSale genericSale))
            {
                return genericSale;
            }
            if (!CanGeneralStoreSellReserveCategory(categoryId) || requestedUnits <= 0 || remainingBudgetCents <= 0)
            {
                return default;
            }

            int staffedRequestUnits = ScaleUnitsByOperatingEfficiency(requestedUnits);
            if (staffedRequestUnits <= 0)
            {
                RecordDemandLossBreakdown(requestedUnits, 0, requestedUnits, requestedUnits);
                return default;
            }

            CategoryStockState stock = runtimeState.GetCategoryStock(categoryId);
            int unitPrice = Mathf.Max(minimumCategoryUnitPriceCents, GetAverageCategoryUnitPrice(categoryId));
            int budgetUnits = Mathf.Max(0, remainingBudgetCents / unitPrice);
            RecordDemandLossBreakdown(requestedUnits, staffedRequestUnits, budgetUnits, stock != null ? stock.CurrentStockUnits : 0);
            int unitsSold = Mathf.Min(staffedRequestUnits, budgetUnits, stock.CurrentStockUnits);
            if (unitsSold <= 0)
            {
                RecordGeneralStoreHouseholdAffinity(householdId, categoryId, requestedUnits, 0, 0, unitPrice);
                return default;
            }

            int revenue = unitsSold * unitPrice;
            runtimeState.ResolveDailySalesPlaceholder(categoryId, unitsSold, revenue);
            lastDailyOffMapStoreUnitsSold += unitsSold;
            lastDailyOffMapStoreSpendCents += revenue;
            int costOfGoodsSold = unitsSold * GetAverageCategoryLandedCost(categoryId);
            lastDailyCostOfGoodsSoldCents += costOfGoodsSold;
            weekToDateCostOfGoodsSoldCents += costOfGoodsSold;
            remainingBudgetCents -= revenue;
            lastDailyUnitsSold += unitsSold;
            RecordGeneralStoreHouseholdSaleObservation(categoryId, householdId, requestedUnits, unitsSold, unitPrice);
            RecordGeneralStoreHouseholdAffinity(householdId, categoryId, requestedUnits, unitsSold, revenue, unitPrice);
            return new LocalReserveSale(unitsSold, revenue);
        }

        private bool TrySellFromGenericRetail(string categoryId, int householdId, int requestedUnits,
            ref int remainingBudgetCents, out LocalReserveSale sale)
        {
            sale = default;
            if (currentBusiness == null || runtimeState == null || requestedUnits <= 0 || remainingBudgetCents <= 0)
            {
                return false;
            }
            if (!currentBusiness.GenericConfiguration.Retail.TryGetLine(categoryId, out GenericRetailProductLine line))
            {
                if (currentBusiness.BusinessType != BusinessType.Generic) return false;
                BindExistingMerchantStockToGenericRetail(currentBusiness);
                if (!currentBusiness.GenericConfiguration.Retail.TryGetLine(categoryId, out line)) return false;
            }
            if (!line.EnabledForSale) return false;

            int actingPersonId = ResolveActingCustomerPersonId(householdId);
            int unitPrice = Mathf.Max(0, line.SellingPriceCents);
            int affordableUnits = unitPrice > 0 ? remainingBudgetCents / unitPrice : requestedUnits;
            int units = Mathf.Min(requestedUnits, affordableUnits);
            CategoryStockState stock = runtimeState.GetCategoryStock(categoryId);
            float genericUnits = currentBusiness.GenericConfiguration.GetInventoryQuantity(categoryId);
            // A classless Business may use the generic Inventory authority with
            // no legacy CategoryStockState. Do not let the compatibility adapter
            // turn that legitimate stock into zero available units.
            float availableGenericUnits = currentBusiness.GenericConfiguration.GetAvailableInventoryQuantity(categoryId);
            float availableUnits = stock != null ? stock.CurrentStockUnits : availableGenericUnits;
            units = Mathf.Min(units, Mathf.FloorToInt(Mathf.Max(0f, availableUnits)));
            if (units <= 0 || actingPersonId < 0) return true;

            int revenue = units * unitPrice;
            var context = new GenericRetailSaleContext
            {
                BuyerPrincipal = $"household:{householdId}",
                ActingPersonId = actingPersonId,
                SellerBusinessId = currentBusiness.InstanceId,
                ProductId = categoryId,
                Quantity = units,
                UnitPriceCents = unitPrice,
                LocationId = storeBuildingId >= 0 ? $"building:{storeBuildingId}" : string.Empty,
            };
            bool settled = GenericRetailSaleAuthority.TryExecuteSale(
                currentBusiness,
                context,
                _ =>
                {
                    runtimeState.RecordGenericRetailSettlement(categoryId, units, revenue);
                    RecordGeneralStoreHouseholdSaleObservation(categoryId, householdId, requestedUnits, units, unitPrice);
                    RecordGeneralStoreHouseholdAffinity(householdId, categoryId, requestedUnits, units, revenue, unitPrice);
                    return true;
                },
                out _);
            if (!settled) return true;
            remainingBudgetCents -= revenue;
            lastDailyUnitsSold += units;
            lastDailyOffMapStoreUnitsSold += units;
            lastDailyOffMapStoreSpendCents += revenue;
            sale = new LocalReserveSale(units, revenue);
            return true;
        }

        private LocalReserveSale TrySellReserveCategoryFromSharedBusinesses(HouseholdReserveDefinition definition, int requestedUnits, ref int remainingBudgetCents)
        {
            return TrySellReserveCategoryFromSharedBusinesses(definition, -1, requestedUnits, ref remainingBudgetCents);
        }

        private LocalReserveSale TrySellReserveCategoryFromSharedBusinesses(HouseholdReserveDefinition definition, int householdId, int requestedUnits, ref int remainingBudgetCents)
        {
            if (definition == null || requestedUnits <= 0 || remainingBudgetCents <= 0)
            {
                return default;
            }

            AutoWire();
            if (sharedBusinessRuntime == null)
            {
                return default;
            }

            int unitsSold = sharedBusinessRuntime.TrySellHouseholdReserveUnits(
                definition.CategoryId,
                definition.LocalSellerCategoryIds,
                requestedUnits,
                ref remainingBudgetCents,
                householdId,
                out int spendCents);
            return new LocalReserveSale(unitsSold, spendCents);
        }

        private bool TrySellReserveCategoryFromBestLocalSeller(
            HouseholdReserveDefinition definition,
            string categoryId,
            int householdId,
            int requestedUnits,
            ref int remainingBudgetCents,
            out BestLocalReserveSellerSale bestSale)
        {
            bestSale = default;
            AutoWire();
            bool hasStore = TryEvaluateGeneralStoreSellerChoiceScore(
                categoryId,
                householdId,
                requestedUnits,
                remainingBudgetCents,
                out BusinessReputationSellerChoiceScore storeScore);
            BusinessReputationSellerChoiceScore sharedScore = default;
            bool hasShared = definition != null
                && sharedBusinessRuntime != null
                && sharedBusinessRuntime.TryEvaluateBestHouseholdReserveSellerScore(
                    definition.CategoryId,
                    definition.LocalSellerCategoryIds,
                    requestedUnits,
                    remainingBudgetCents,
                    householdId,
                    out sharedScore);

            if (!hasStore && !hasShared)
            {
                return false;
            }

            bool chooseStore = false;
            if (hasStore)
            {
                chooseStore = !hasShared;
                if (hasShared)
                {
                    chooseStore = BusinessReputationSellerChoice.Compare(storeScore, sharedScore) < 0;
                }
            }
            if (chooseStore)
            {
                LocalReserveSale sale = TrySellReserveCategoryFromGeneralStore(categoryId, householdId, requestedUnits, ref remainingBudgetCents);
                if (sale.UnitsSold > 0)
                {
                    bestSale = new BestLocalReserveSellerSale(true, sale);
                    return true;
                }
            }
            else
            {
                LocalReserveSale sharedSale = TrySellReserveCategoryFromBestSharedBusiness(definition, householdId, requestedUnits, ref remainingBudgetCents);
                if (sharedSale.UnitsSold > 0)
                {
                    bestSale = new BestLocalReserveSellerSale(false, sharedSale);
                    return true;
                }
            }

            if (chooseStore || !hasStore)
            {
                LocalReserveSale fallbackSharedSale = TrySellReserveCategoryFromBestSharedBusiness(definition, householdId, requestedUnits, ref remainingBudgetCents);
                if (fallbackSharedSale.UnitsSold > 0)
                {
                    bestSale = new BestLocalReserveSellerSale(false, fallbackSharedSale);
                    return true;
                }
            }

            if (!chooseStore || !hasShared)
            {
                LocalReserveSale fallbackStoreSale = TrySellReserveCategoryFromGeneralStore(categoryId, householdId, requestedUnits, ref remainingBudgetCents);
                if (fallbackStoreSale.UnitsSold > 0)
                {
                    bestSale = new BestLocalReserveSellerSale(true, fallbackStoreSale);
                    return true;
                }
            }

            return false;
        }

        private LocalReserveSale TrySellReserveCategoryFromBestSharedBusiness(
            HouseholdReserveDefinition definition,
            int householdId,
            int requestedUnits,
            ref int remainingBudgetCents)
        {
            if (definition == null || requestedUnits <= 0 || remainingBudgetCents <= 0)
            {
                return default;
            }

            AutoWire();
            if (sharedBusinessRuntime == null)
            {
                return default;
            }

            int unitsSold = sharedBusinessRuntime.TrySellHouseholdReserveUnits(
                definition.CategoryId,
                definition.LocalSellerCategoryIds,
                requestedUnits,
                ref remainingBudgetCents,
                householdId,
                out int spendCents);
            return new LocalReserveSale(unitsSold, spendCents);
        }

        private bool ShouldTrySharedSellerBeforeGeneralStore(
            HouseholdReserveDefinition definition,
            string categoryId,
            int householdId,
            int requestedUnits,
            int remainingBudgetCents)
        {
            AutoWire();
            if (definition == null
                || sharedBusinessRuntime == null
                || requestedUnits <= 0
                || remainingBudgetCents <= 0)
            {
                return false;
            }

            if (!sharedBusinessRuntime.TryEvaluateBestHouseholdReserveSellerScore(
                    definition.CategoryId,
                    definition.LocalSellerCategoryIds,
                    requestedUnits,
                    remainingBudgetCents,
                    householdId,
                    out BusinessReputationSellerChoiceScore sharedScore))
            {
                return false;
            }

            if (!TryEvaluateGeneralStoreSellerChoiceScore(categoryId, householdId, requestedUnits, remainingBudgetCents, out BusinessReputationSellerChoiceScore storeScore))
            {
                return true;
            }

            float clearPreferenceMargin = GetSharedSellerClearPreferenceMargin01(categoryId, householdId);
            return sharedScore.Score01 >= storeScore.Score01 + clearPreferenceMargin;
        }

        private bool TryEvaluateGeneralStoreSellerChoiceScore(
            string categoryId,
            int requestedUnits,
            int remainingBudgetCents,
            out BusinessReputationSellerChoiceScore score)
        {
            return TryEvaluateGeneralStoreSellerChoiceScore(categoryId, -1, requestedUnits, remainingBudgetCents, out score);
        }

        private bool TryEvaluateGeneralStoreSellerChoiceScore(
            string categoryId,
            int householdId,
            int requestedUnits,
            int remainingBudgetCents,
            out BusinessReputationSellerChoiceScore score)
        {
            score = default;
            if (!CanGeneralStoreSellReserveCategory(categoryId)
                || currentBusiness == null
                || runtimeState == null
                || requestedUnits <= 0
                || remainingBudgetCents <= 0)
            {
                return false;
            }

            CategoryStockState stock = runtimeState.GetCategoryStock(categoryId);
            if (stock == null || stock.CurrentStockUnits <= 0)
            {
                return false;
            }

            int unitPrice = Mathf.Max(minimumCategoryUnitPriceCents, GetAverageCategoryUnitPrice(categoryId));
            if (unitPrice > remainingBudgetCents)
            {
                return false;
            }

            currentBusiness.EnsureBusinessReputationInitializedFromRuntime();
            score = businessReputationSellerChoice.Evaluate(
                new BusinessReputationSellerChoiceInput(
                    currentBusiness.BusinessReputation,
                    stock.StockHealth01,
                    stock.CurrentStockUnits,
                    unitPrice,
                    GetAverageCategoryLandedCost(categoryId),
                    runtimeState.Reliability01,
                    GetHealthAdjustedOperatingEfficiency01(),
                    currentBusiness.RuntimeDisplayName,
                    currentBusiness.InstanceId,
                    0,
                    categoryId));
            score = ApplyHouseholdAffinityToSellerChoiceScore(score, householdId, currentBusiness);
            return score.Eligible;
        }

        private void RecordGeneralStoreHouseholdSaleObservation(string categoryId, int householdId, int requestedUnits, int unitsSold, int unitPriceCents)
        {
            if (currentBusiness == null || runtimeState == null || unitsSold <= 0)
            {
                return;
            }

            lastCustomerHouseholdId = householdId;
            lastCustomerPersonId = ResolveActingCustomerPersonId(householdId);
            lastCustomerSaleCategoryId = categoryId ?? string.Empty;
            lastCustomerSaleUnits = unitsSold;
            lastCustomerSaleRevenueCents = unitsSold * Mathf.Max(0, unitPriceCents);

            CategoryStockState stock = runtimeState.GetCategoryStock(categoryId);
            currentBusiness.RecordBusinessReputationObservation(
                new BusinessReputationObservation(
                    requestedUnits,
                    unitsSold,
                    unitPriceCents,
                    GetAverageCategoryLandedCost(categoryId),
                    stock != null ? stock.StockHealth01 : runtimeState.StockHealth01,
                    runtimeState.Reliability01,
                    GetHealthAdjustedOperatingEfficiency01(),
                    categoryId,
                    timeManager != null ? timeManager.CurrentDate.AbsoluteDayIndex : -1,
                    timeManager != null ? Mathf.Max(0, timeManager.CurrentWeek) : -1));
        }

        private int ResolveActingCustomerPersonId(int householdId)
        {
            if (householdId < 0 || populationManager == null || populationManager.State == null)
            {
                return -1;
            }

            HouseholdState household = populationManager.State.GetHousehold(householdId);
            if (household == null || household.memberIds == null)
            {
                return -1;
            }

            for (int i = 0; i < household.memberIds.Count; i++)
            {
                PersonState person = populationManager.State.GetPerson(household.memberIds[i]);
                if (person != null
                    && person.ageBand == AgeBand.Adult18Plus
                    && person.laborAccessLevel != LaborAccessLevel.None)
                {
                    return person.id;
                }
            }

            return -1;
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

        private void RecordGeneralStoreHouseholdAffinity(
            int householdId,
            string categoryId,
            int requestedUnits,
            int unitsSold,
            int spendCents,
            int unitPriceCents)
        {
            if (householdId < 0 || currentBusiness == null || requestedUnits <= 0)
            {
                return;
            }

            string key = BuildHouseholdAffinityKey(householdId, currentBusiness.InstanceId);
            if (!householdAffinitiesByKey.TryGetValue(key, out HouseholdBusinessAffinityState affinity) || affinity == null)
            {
                affinity = new HouseholdBusinessAffinityState
                {
                    householdId = householdId,
                    businessId = currentBusiness.InstanceId,
                    hasBusinessType = true,
                    businessType = currentBusiness.BusinessType
                };
            }

            currentBusiness.EnsureBusinessReputationInitializedFromRuntime();
            CategoryStockState stock = runtimeState != null ? runtimeState.GetCategoryStock(categoryId) : null;
            float stockConsistency = requestedUnits <= 0
                ? stock != null ? stock.StockHealth01 : 0.5f
                : Mathf.Clamp01(unitsSold / (float)requestedUnits);
            HouseholdAffinityEvent affinityEvent = new()
            {
                householdId = householdId,
                businessId = currentBusiness.InstanceId,
                hasBusinessType = true,
                businessType = currentBusiness.BusinessType,
                completedPurchase = unitsSold > 0,
                spendCents = spendCents,
                priceFairness01 = BusinessReputationSellerChoice.CalculateValueFairness01(
                    unitPriceCents,
                    GetAverageCategoryLandedCost(categoryId)),
                stockConsistency01 = stockConsistency,
                trustQuality01 = businessReputationEvaluator.EvaluateLocalTrust01(
                    currentBusiness.BusinessReputation,
                    categoryId,
                    stockConsistency),
                absoluteDayIndex = timeManager != null ? timeManager.CurrentDate.AbsoluteDayIndex : -1
            };
            householdAffinitiesByKey[key] = householdAffinityEvaluator.BuildUpdatedState(affinity, affinityEvent);
        }

        private void RecordGeneralStoreHouseholdReserveMiss(int householdId, string categoryId, int requestedUnits, int missedUnits)
        {
            if (householdId < 0
                || currentBusiness == null
                || storeDefinition == null
                || storeDefinition.Business == null
                || !storeDefinition.Business.OwnsCategory(categoryId)
                || requestedUnits <= 0
                || missedUnits <= 0)
            {
                return;
            }

            int unitPrice = Mathf.Max(minimumCategoryUnitPriceCents, GetAverageCategoryUnitPrice(categoryId));
            RecordGeneralStoreHouseholdAffinity(householdId, categoryId, requestedUnits, 0, 0, unitPrice);
        }

        private static string BuildHouseholdAffinityKey(int householdId, string businessId)
        {
            return $"{Mathf.Max(-1, householdId)}:{businessId ?? string.Empty}";
        }

        private bool CanGeneralStoreSellReserveCategory(string categoryId)
        {
            if (runtimeState == null
                || storeDefinition == null
                || storeDefinition.Business == null
                || GetHealthAdjustedOperatingEfficiency01() <= 0f)
            {
                return false;
            }

            if (currentBusiness != null && currentBusiness.BusinessType == BusinessType.Generic)
            {
                if (!currentBusiness.GenericConfiguration.Retail.TryGetLine(categoryId, out GenericRetailProductLine genericLine)
                    || !genericLine.EnabledForSale) return false;
                return currentBusiness.GenericConfiguration.GetInventoryQuantity(categoryId) > 0f
                    || runtimeState.GetCategoryStock(categoryId)?.CurrentStockUnits > 0;
            }

            if (!storeDefinition.Business.OwnsCategory(categoryId)) return false;

            CategoryStockState stock = runtimeState.GetCategoryStock(categoryId);
            return stock != null && stock.CurrentStockUnits > 0;
        }

        private int GetOffMapReserveUnitValueCents(string categoryId)
        {
            return Mathf.Max(minimumCategoryUnitPriceCents, GetAverageCategoryUnitPrice(categoryId));
        }

        private int ScaleUnitsByOperatingEfficiency(int requestedUnits)
        {
            if (runtimeState == null)
            {
                return 0;
            }

            if (runtimeState.TargetWorkerCount <= 0)
            {
                return Mathf.Max(0, requestedUnits);
            }

            float efficiency = GetHealthAdjustedOperatingEfficiency01();
            if (efficiency <= 0f)
            {
                return 0;
            }

            return Mathf.Max(1, Mathf.FloorToInt(Mathf.Max(0, requestedUnits) * efficiency));
        }

        private float GetHealthAdjustedOperatingEfficiency01()
        {
            if (runtimeState == null)
            {
                return 0f;
            }

            if (runtimeState.TargetWorkerCount <= 0)
            {
                return 1f;
            }

            float optionalLaborCapacity = 0f;
            float requiredLaborCapacity = 0f;
            int requiredWorkerCount = 0;
            int activeRequiredWorkerCount = 0;
            IReadOnlyList<WorkerSlotState> slots = runtimeState.WorkerSlots;
            for (int i = 0; i < slots.Count; i++)
            {
                WorkerSlotState slot = slots[i];
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

                    activeRequiredWorkerCount++;
                    requiredLaborCapacity += laborAvailability;
                    continue;
                }

                optionalLaborCapacity += laborAvailability;
            }

            if (requiredWorkerCount > 0 && activeRequiredWorkerCount < requiredWorkerCount)
            {
                return CanUseOwnerOperatedFallback() ? OwnerOperatedFallbackEfficiency01 : 0f;
            }

            int optionalSlots = Mathf.Max(0, runtimeState.TargetWorkerCount - requiredWorkerCount);
            if (requiredWorkerCount <= 0)
            {
                return Mathf.Clamp01(optionalLaborCapacity / runtimeState.TargetWorkerCount);
            }

            if (optionalSlots <= 0)
            {
                return Mathf.Clamp01(requiredLaborCapacity / requiredWorkerCount);
            }

            float requiredBaseline = RequiredStaffBaselineEfficiency01 * Mathf.Clamp01(requiredLaborCapacity / requiredWorkerCount);
            float optionalContribution = Mathf.Clamp01(optionalLaborCapacity / optionalSlots) * (1f - RequiredStaffBaselineEfficiency01);
            return Mathf.Clamp01(requiredBaseline + optionalContribution);
        }

        private bool UsesOwnerOperatedFallback()
        {
            return runtimeState != null
                && runtimeState.TargetWorkerCount > 0
                && Mathf.Approximately(GetHealthAdjustedOperatingEfficiency01(), OwnerOperatedFallbackEfficiency01);
        }

        private bool CanUseOwnerOperatedFallback()
        {
            return currentBusiness != null
                && currentBusiness.Owner != null
                && currentBusiness.Owner.OwnerKind == BusinessOwnerKind.Player;
        }

        private float GetWorkerSlotLaborAvailability01(WorkerSlotState slot)
        {
            if (slot == null || !slot.IsPaidActive)
            {
                return 0f;
            }

            PersonState person = FindPersonForWorkerSlot(slot);
            return person != null
                ? PopulationHealthEvaluator.GetLaborAvailability01(person)
                : 1f;
        }

        private int GetAverageCategoryUnitPrice(string categoryId)
        {
            if (storeDefinition == null)
            {
                return minimumCategoryUnitPriceCents;
            }

            int total = 0;
            int count = 0;
            ReadOnlySpan<ItemDefinition> items = storeDefinition.Items;
            for (int i = 0; i < items.Length; i++)
            {
                ItemDefinition item = items[i];
                if (item == null || !string.Equals(item.CategoryId, categoryId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                float categoryMarkupMultiplier = GetCategoryMarkupMultiplier(categoryId);
                total += item.Pricing.GetCostPlusPriceCents(categoryMarkupMultiplier, 0f);
                if (item.Pricing.IsMarginCompressedByBand(categoryMarkupMultiplier, 0f)
                    && loggedMarginCompressionCategoryIds.Add(categoryId))
                {
                    Debug.LogWarning($"[General Store][Pricing] {GetCategoryDisplayName(categoryId)} price is capped by its local market band; intended cost-plus margin is compressed.", this);
                }

                count++;
            }

            return count == 0 ? minimumCategoryUnitPriceCents : Mathf.Max(minimumCategoryUnitPriceCents, total / count);
        }

        private int GetAverageCategoryLandedCost(string categoryId)
        {
            if (storeDefinition == null)
            {
                return 1;
            }

            int total = 0;
            int count = 0;
            ReadOnlySpan<ItemDefinition> items = storeDefinition.Items;
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

            int averageLandedCost = count == 0 ? 1 : Mathf.Max(1, total / count);
            return Mathf.Max(1, Mathf.RoundToInt(averageLandedCost * GetCategoryLandedCostMultiplier01(categoryId)));
        }

        private float GetCategoryLandedCostMultiplier01(string categoryId)
        {
            if (string.IsNullOrWhiteSpace(categoryId))
            {
                return 1f;
            }

            if (string.Equals(categoryId, "staple_food", StringComparison.OrdinalIgnoreCase))
            {
                return 0.88f;
            }

            if (string.Equals(categoryId, "household_goods", StringComparison.OrdinalIgnoreCase))
            {
                return 0.90f;
            }

            if (string.Equals(categoryId, "meat", StringComparison.OrdinalIgnoreCase))
            {
                return 0.92f;
            }

            if (string.Equals(categoryId, "medicine_remedies", StringComparison.OrdinalIgnoreCase))
            {
                return 0.96f;
            }

            return 1f;
        }

        private float GetSharedSellerClearPreferenceMargin01(string categoryId, int householdId)
        {
            float baseMargin = householdId >= 0
                ? HouseholdAwareSharedSellerClearPreferenceMargin01
                : SharedSellerClearPreferenceMargin01;

            if (string.Equals(categoryId, "staple_food", StringComparison.OrdinalIgnoreCase)
                || string.Equals(categoryId, "household_goods", StringComparison.OrdinalIgnoreCase)
                || string.Equals(categoryId, "medicine_remedies", StringComparison.OrdinalIgnoreCase)
                || string.Equals(categoryId, "clothing", StringComparison.OrdinalIgnoreCase))
            {
                return Mathf.Max(baseMargin, 0.16f);
            }

            return baseMargin;
        }

        private string BuildStoreMarginChangeStatus()
        {
            PricingSnapshot snapshot = BuildAverageStorePricingSnapshot();
            if (!snapshot.HasPricing)
            {
                return $"Price adjustment set to {FormatSignedPercent(StoreMarginAdjustment01)}.";
            }

            return $"Price adjustment {FormatSignedPercent(StoreMarginAdjustment01)}: avg sell {FormatMoney(snapshot.AverageSellingPriceCents)} / cost {FormatMoney(snapshot.AverageLandedCostCents)}.";
        }

        public string BuildStorePricingMarginText()
        {
            PricingSnapshot snapshot = BuildAverageStorePricingSnapshot();
            System.Text.StringBuilder builder = new();
            builder.AppendLine("<b>Pricing & Margin</b>");
            builder.AppendLine("<mspace=0.62em>Measure           | Current     | Read</mspace>");
            builder.AppendLine("<mspace=0.62em>------------------+-------------+--------------------------</mspace>");
            builder.AppendLine(BuildPricingMarginTableRow("Price Adjustment", ColorizePricingSignal(FormatSignedPercent(StoreMarginAdjustment01), StoreMarginAdjustment01), "manual shelf price move"));

            if (!snapshot.HasPricing)
            {
                builder.AppendLine(BuildPricingMarginTableRow("Avg Sell", "unavailable", "no stocked categories priced"));
                builder.AppendLine(BuildPricingMarginTableRow("Avg Cost", "unavailable", "no stocked categories priced"));
                builder.AppendLine(BuildPricingMarginTableRow("Gross / Unit", "unavailable", "no stocked categories priced"));
                builder.AppendLine(BuildPricingMarginTableRow("Gross Margin", "unavailable", "no stocked categories priced"));
                builder.AppendLine(BuildPricingMarginTableRow("Markup Over Cost", "unavailable", "no stocked categories priced"));
                builder.AppendLine("Demand Effect: no priced stock.");
                builder.Append("Trust Effect: no priced stock.");
                return builder.ToString();
            }

            float grossMargin = CalculateGrossMargin01(snapshot.AverageGrossProfitCents, snapshot.AverageSellingPriceCents);
            float markup = CalculateMarkupOverCost01(snapshot.AverageGrossProfitCents, snapshot.AverageLandedCostCents);
            builder.AppendLine(BuildPricingMarginTableRow("Avg Sell", FormatMoney(snapshot.AverageSellingPriceCents), "shelf receipts"));
            builder.AppendLine(BuildPricingMarginTableRow("Avg Cost", FormatMoney(snapshot.AverageLandedCostCents), "landed goods cost"));
            builder.AppendLine(BuildPricingMarginTableRow("Gross / Unit", ColorizeMoney(snapshot.AverageGrossProfitCents), "before payroll"));
            builder.AppendLine(BuildPricingMarginTableRow("Gross Margin", ColorizePercent(grossMargin), "on selling price"));
            builder.AppendLine(BuildPricingMarginTableRow("Markup Over Cost", ColorizePercent(markup), "over landed cost"));
            builder.AppendLine($"Demand Effect: {BuildDemandEffect(StoreMarginAdjustment01)}");
            builder.AppendLine($"Trust Effect: {BuildTrustEffect(StoreMarginAdjustment01)}");
            builder.AppendLine($"Profit Posture: {BuildPricingStance(snapshot.AverageGrossProfitCents, snapshot.AverageLandedCostCents)}");
            if (snapshot.AnyPriceCappedByBand)
            {
                builder.Append("<color=#D8B35A>Warning: some prices are capped by market band.</color>");
            }

            return builder.ToString().TrimEnd();
        }

        private static string BuildPricingMarginTableRow(string measure, string current, string read)
        {
            return $"<mspace=0.62em>{PadVisibleRight(measure, 17)} | {PadVisibleRight(current, 11)} | {read}</mspace>";
        }

        private static string PadVisibleRight(string value, int width)
        {
            value ??= string.Empty;
            int visibleLength = 0;
            bool insideTag = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '<')
                {
                    insideTag = true;
                    continue;
                }

                if (c == '>')
                {
                    insideTag = false;
                    continue;
                }

                if (!insideTag)
                {
                    visibleLength++;
                }
            }

            return visibleLength >= width ? value : value + new string(' ', width - visibleLength);
        }

        private static string ColorizePricingSignal(string value, float adjustment01)
        {
            string color = adjustment01 > 0.001f ? "#D8B35A" : adjustment01 < -0.001f ? "#8FCB8F" : "#D6D0BD";
            return $"<color={color}>{value}</color>";
        }

        private static string ColorizeMoney(int cents)
        {
            string color = cents > 0 ? "#8FCB8F" : cents < 0 ? "#D98282" : "#D6D0BD";
            return $"<color={color}>{FormatMoney(cents)}</color>";
        }

        private static string ColorizePercent(float value01)
        {
            string color = value01 >= 0.2f ? "#8FCB8F" : value01 <= 0.08f ? "#D98282" : "#D8B35A";
            return $"<color={color}>{FormatPercent(value01)}</color>";
        }

        private string BuildStorePricingLine()
        {
            PricingSnapshot snapshot = BuildAverageStorePricingSnapshot();
            if (!snapshot.HasPricing)
            {
                return $"Pricing adjustment {FormatSignedPercent(StoreMarginAdjustment01)} | no stocked categories priced";
            }

            string capped = snapshot.AnyPriceCappedByBand ? " | some prices capped" : string.Empty;
            return $"Pricing adjustment {FormatSignedPercent(StoreMarginAdjustment01)} | Avg sell {FormatMoney(snapshot.AverageSellingPriceCents)} / cost {FormatMoney(snapshot.AverageLandedCostCents)} | gross margin {FormatPercent(CalculateGrossMargin01(snapshot.AverageGrossProfitCents, snapshot.AverageSellingPriceCents))} | {BuildPricingStance(snapshot.AverageGrossProfitCents, snapshot.AverageLandedCostCents)}{capped}";
        }

        private PricingSnapshot BuildAverageStorePricingSnapshot()
        {
            if (runtimeState == null)
            {
                return default;
            }

            int totalSell = 0;
            int totalCost = 0;
            int count = 0;
            bool anyCapped = false;
            for (int i = 0; i < runtimeState.CategoryStock.Count; i++)
            {
                string categoryId = runtimeState.CategoryStock[i].CategoryId;
                totalSell += GetAverageCategoryUnitPrice(categoryId);
                totalCost += GetAverageCategoryLandedCost(categoryId);
                anyCapped |= IsCategoryPriceCompressedByBand(categoryId);
                count++;
            }

            if (count <= 0)
            {
                return default;
            }

            int averageSell = Mathf.Max(0, totalSell / count);
            int averageCost = Mathf.Max(0, totalCost / count);
            return new PricingSnapshot(averageSell, averageCost, averageSell - averageCost, anyCapped);
        }

        private string BuildCategoryPricingSegment(string categoryId)
        {
            int sell = GetAverageCategoryUnitPrice(categoryId);
            int cost = GetAverageCategoryLandedCost(categoryId);
            int gross = sell - cost;
            string capped = IsCategoryPriceCompressedByBand(categoryId) ? " | capped" : string.Empty;
            return $"sell {FormatMoney(sell)} / cost {FormatMoney(cost)} | gross {FormatMoney(gross)}/u | {BuildPricingStance(gross, cost)}{capped}";
        }

        private string BuildStorePostureLine()
        {
            string label = GetStorePostureLabel();
            return IsLowCashTriageActive()
                ? $"Posture {label} | survival buying only until essentials and cash cover recover."
                : $"Posture {label} | {ResolveStorePostureGuidance(label)}";
        }

        private string BuildDemandLossLine()
        {
            if (lastDailyMissedStockUnits <= 0
                && lastDailyMissedServiceUnits <= 0
                && lastDailyMissedPriceUnits <= 0
                && lastWeeklyCashConstrainedReorderUnits <= 0)
            {
                return "Demand Loss: ordinary household trade is being captured.";
            }

            return $"Demand Loss: stock {lastDailyMissedStockUnits}u | service {lastDailyMissedServiceUnits}u | price {lastDailyMissedPriceUnits}u | reorder cash hold {lastWeeklyCashConstrainedReorderUnits}u";
        }

        private string BuildSurvivalStockPriorityLine()
        {
            if (runtimeState == null)
            {
                return "Survival Priorities: unavailable";
            }

            List<string> pressure = new();
            AppendPriorityStockPressure(pressure, "staple_food");
            AppendPriorityStockPressure(pressure, "meat");
            AppendPriorityStockPressure(pressure, "household_goods");
            AppendPriorityStockPressure(pressure, "medicine_remedies");

            return pressure.Count == 0
                ? "Survival Priorities: staple food, meat, household goods, and medicine are stocked for ordinary trade."
                : $"Survival Priorities: {string.Join("; ", pressure)}";
        }

        private string BuildSaturdayReadinessLine()
        {
            if (runtimeState == null)
            {
                return "Saturday Readiness: unavailable";
            }

            int priorityThinCount = GetThinEssentialCategoryCount();
            string shortfall = BuildProjectedSaturdayShortfallSummary();
            bool prepWindow = IsSaturdayPreparationWindow();

            if (priorityThinCount <= 0 && string.IsNullOrEmpty(shortfall) && runtimeState.StockHealth01 >= 0.65f)
            {
                return "Saturday Readiness: strong enough for a normal Saturday rush.";
            }

            if (prepWindow && !string.IsNullOrEmpty(shortfall))
            {
                string lead = IsSaturdayTradeDay() ? "Saturday rush" : "Friday warning";
                return $"Saturday Readiness: {lead}; projected shortfall {shortfall}.";
            }

            if (runtimeState.CurrentCashCents <= GetProtectedBusinessCashReserveCents())
            {
                return $"Saturday Readiness: weak; {priorityThinCount} priority categories are thin and cash is inside reserve.";
            }

            return $"Saturday Readiness: watch list; {priorityThinCount} priority categories are thin before the weekend rush.";
        }

        private void AppendPriorityStockPressure(List<string> pressure, string categoryId)
        {
            CategoryStockState category = runtimeState != null ? runtimeState.GetCategoryStock(categoryId) : null;
            if (category == null)
            {
                return;
            }

            if (category.CurrentStockUnits <= 0)
            {
                pressure.Add($"{GetCategoryDisplayName(categoryId)} out");
                return;
            }

            if (category.StockHealth01 <= CriticalStockHealthThreshold01)
            {
                pressure.Add($"{GetCategoryDisplayName(categoryId)} critical {category.CurrentStockUnits}/{category.TargetStockUnits}");
                return;
            }

            if (category.StockHealth01 <= 0.5f)
            {
                pressure.Add($"{GetCategoryDisplayName(categoryId)} thin {category.CurrentStockUnits}/{category.TargetStockUnits}");
            }
        }

        private string BuildProjectedSaturdayShortfallSummary()
        {
            if (runtimeState == null)
            {
                return string.Empty;
            }

            List<string> segments = new();
            AppendProjectedSaturdayShortfallSegment(segments, "staple_food");
            AppendProjectedSaturdayShortfallSegment(segments, "meat");
            AppendProjectedSaturdayShortfallSegment(segments, "household_goods");
            AppendProjectedSaturdayShortfallSegment(segments, "medicine_remedies");
            return string.Join("; ", segments);
        }

        private void AppendProjectedSaturdayShortfallSegment(List<string> segments, string categoryId)
        {
            CategoryStockState category = runtimeState != null ? runtimeState.GetCategoryStock(categoryId) : null;
            if (category == null)
            {
                return;
            }

            int shortfallUnits = GetProjectedSaturdayShortfallUnits(category);
            if (shortfallUnits > 0)
            {
                segments.Add($"{GetCategoryDisplayName(categoryId)} short {shortfallUnits}u");
            }
        }

        private static bool IsSurvivalPriorityCategory(string categoryId)
        {
            return string.Equals(categoryId, "staple_food", StringComparison.OrdinalIgnoreCase)
                || string.Equals(categoryId, "meat", StringComparison.OrdinalIgnoreCase)
                || string.Equals(categoryId, "household_goods", StringComparison.OrdinalIgnoreCase)
                || string.Equals(categoryId, "medicine_remedies", StringComparison.OrdinalIgnoreCase);
        }

        private string GetStorePostureLabel()
        {
            if (runtimeState == null)
            {
                return "Fragile";
            }

            int baseReserve = GetBaseBusinessSurvivalCashReserveCents();
            bool cashInsideBaseReserve = runtimeState.CurrentCashCents <= baseReserve;
            bool negativeTrend = EstimatedWeeklyNetCashFlowCents < 0
                || runtimeState.LastWeeklyCashAfterCents < runtimeState.LastWeeklyCashBeforeCents;
            int criticalEssentials = GetCriticalEssentialCategoryCount();
            int thinEssentials = GetThinEssentialCategoryCount();
            bool saturdayShortfall = !string.IsNullOrEmpty(BuildProjectedSaturdayShortfallSummary());

            if (cashInsideBaseReserve && (criticalEssentials > 0 || negativeTrend || saturdayShortfall))
            {
                return "Fragile";
            }

            if (cashInsideBaseReserve || criticalEssentials > 0 || thinEssentials > 1 || negativeTrend)
            {
                return "Scraping Through";
            }

            if (thinEssentials == 0
                && runtimeState.StockHealth01 >= 0.75f
                && runtimeState.CurrentCashCents > GetProtectedBusinessCashReserveCents() + GetEffectiveWeeklyReorderReserveCents()
                && EstimatedWeeklyNetCashFlowCents >= 0)
            {
                return "Ready for Growth";
            }

            return "Steady";
        }

        private bool IsLowCashTriageActive()
        {
            string posture = GetStorePostureLabel();
            return string.Equals(posture, "Fragile", StringComparison.Ordinal)
                || (string.Equals(posture, "Scraping Through", StringComparison.Ordinal)
                    && runtimeState != null
                    && runtimeState.CurrentCashCents <= GetProtectedBusinessCashReserveCents());
        }

        private static string ResolveStorePostureGuidance(string posture)
        {
            return posture switch
            {
                "Fragile" => "protect staples and medicine first.",
                "Scraping Through" => "protect essentials before broad shelf fill.",
                "Ready for Growth" => "broad shelves and cash cover support expansion.",
                _ => "ordinary trade can be served without emergency cuts."
            };
        }

        private int GetCriticalEssentialCategoryCount()
        {
            return CountEssentialCategoriesAtOrBelow(CriticalStockHealthThreshold01);
        }

        private int GetThinEssentialCategoryCount()
        {
            return CountEssentialCategoriesAtOrBelow(0.5f);
        }

        private int CountEssentialCategoriesAtOrBelow(float threshold01)
        {
            if (runtimeState == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < runtimeState.CategoryStock.Count; i++)
            {
                CategoryStockState category = runtimeState.CategoryStock[i];
                if (category != null
                    && IsSurvivalPriorityCategory(category.CategoryId)
                    && category.StockHealth01 <= threshold01)
                {
                    count++;
                }
            }

            return count;
        }

        private bool IsSaturdayPreparationWindow()
        {
            return timeManager != null
                && (timeManager.CurrentDate.DayOfWeekIndex == SaturdayDayOfWeekIndex - 1
                    || timeManager.CurrentDate.DayOfWeekIndex == SaturdayDayOfWeekIndex);
        }

        private int GetRecentDemandSignalUnits(CategoryStockState category)
        {
            if (category == null)
            {
                return 0;
            }

            return Mathf.Max(category.LastDailyUnitsSold, Mathf.CeilToInt(category.WeekToDateUnitsSold / Mathf.Max(1f, GetElapsedTradeDaysThisWeek())));
        }

        private int GetProjectedSaturdayShortfallUnits(CategoryStockState category)
        {
            if (category == null)
            {
                return 0;
            }

            int projectedUnits = CalculateProjectedSaturdayDemandUnits(category);
            int availableUnits = category.CurrentStockUnits + category.PendingReorderUnits;
            return Mathf.Max(0, projectedUnits - availableUnits);
        }

        private int CalculateProjectedSaturdayDemandUnits(CategoryStockState category)
        {
            if (category == null)
            {
                return 0;
            }

            int recentSignal = GetRecentDemandSignalUnits(category);
            if (IsSurvivalPriorityCategory(category.CategoryId))
            {
                recentSignal = Mathf.Max(recentSignal, Mathf.Max(1, Mathf.CeilToInt(category.TargetStockUnits * 0.2f)));
            }

            if (IsSaturdayPreparationWindow())
            {
                recentSignal = Mathf.Max(recentSignal, Mathf.CeilToInt(recentSignal * SaturdayHouseholdDemandMultiplier));
            }

            return Mathf.Max(0, recentSignal);
        }

        private float GetElapsedTradeDaysThisWeek()
        {
            return timeManager != null ? Mathf.Max(1, timeManager.CurrentDate.DayOfWeekIndex + 1) : 7f;
        }

        private string BuildCategoryStockTableRow(CategoryStockState category)
        {
            if (category == null)
            {
                return string.Empty;
            }

            string label = FitColumn(GetCategoryDisplayName(category.CategoryId), 21);
            string stock = FitColumn($"{category.CurrentStockUnits}/{category.TargetStockUnits}", 10);
            int sell = GetAverageCategoryUnitPrice(category.CategoryId);
            int cost = GetAverageCategoryLandedCost(category.CategoryId);
            int gross = sell - cost;
            string sellText = FitColumn(FormatMoney(sell), 10);
            string costText = FitColumn(FormatMoney(cost), 10);
            string grossText = FitColumn($"{FormatMoney(gross)}/u", 14);
            string status = ColorizeStockStatus(BuildCategoryStockStatus(category), category.StockHealth01);
            return $"<mspace=0.62em>{label}{stock}{sellText}{costText}{grossText}</mspace>{status}";
        }

        private static string BuildCategoryStockStatus(CategoryStockState category)
        {
            string status = category.CurrentStockUnits <= 0
                ? "Out"
                : category.StockHealth01 <= 0.25f
                ? "Low"
                : category.StockHealth01 <= 0.5f
                ? "Thin"
                : "Steady";

            if (category.PendingReorderUnits > 0)
            {
                status += $", reorder {category.PendingReorderUnits}";
            }

            return status;
        }

        private static string ColorizeStockStatus(string status, float stockHealth01)
        {
            string color = stockHealth01 <= 0f
                ? "#D98282"
                : stockHealth01 <= 0.5f
                ? "#D8B35A"
                : "#8FCB8F";
            return $"<color={color}>{status}</color>";
        }

        private static string FitColumn(string value, int width)
        {
            string text = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
            if (text.Length > width - 1)
            {
                text = text.Substring(0, Mathf.Max(0, width - 2)) + ".";
            }

            return text.PadRight(width);
        }

        private bool IsCategoryPriceCompressedByBand(string categoryId)
        {
            if (storeDefinition == null)
            {
                return false;
            }

            float categoryMarkupMultiplier = GetCategoryMarkupMultiplier(categoryId);
            ReadOnlySpan<ItemDefinition> items = storeDefinition.Items;
            for (int i = 0; i < items.Length; i++)
            {
                ItemDefinition item = items[i];
                if (item != null
                    && string.Equals(item.CategoryId, categoryId, StringComparison.OrdinalIgnoreCase)
                    && item.Pricing.IsMarginCompressedByBand(categoryMarkupMultiplier, 0f))
                {
                    return true;
                }
            }

            return false;
        }

        private float GetCategoryMarkupMultiplier(string categoryId)
        {
            float effectiveStoreMargin = StoreMarginAdjustment01 + GetManagerPolicyMarginAdjustment01();
            ItemCategoryDefinition category = storeDefinition != null ? storeDefinition.FindCategory(categoryId) : null;
            if (category != null)
            {
                return CalculateEffectiveCategoryMarkupMultiplier(category.DefaultMarkupMultiplier, effectiveStoreMargin);
            }

            float defaultMarkup = storeDefinition != null ? storeDefinition.Business.Economy.DefaultRetailMarkupMultiplier : 1f;
            return CalculateEffectiveCategoryMarkupMultiplier(defaultMarkup, effectiveStoreMargin);
        }

        private int GetLastDailyGrossProfitCents()
        {
            return (runtimeState != null ? runtimeState.LastDailyRevenueCents : 0) - Mathf.Max(0, lastDailyCostOfGoodsSoldCents);
        }

        private int GetWeekToDateGrossProfitCents()
        {
            return (runtimeState != null ? runtimeState.WeekToDateRevenueCents : 0) - Mathf.Max(0, weekToDateCostOfGoodsSoldCents);
        }

        private string BuildCashBridgeSummary()
        {
            if (runtimeState == null)
            {
                return "Cash changed because: runtime unavailable.";
            }

            int stockBuys = Mathf.Max(0, lastWeeklyReorderSpendCents) + Mathf.Max(0, lastWeeklyLocalSupplySpendCents);
            int transferDelta = runtimeState.LastWeeklyCashTransferInCents - runtimeState.LastWeeklyCashTransferOutCents;
            return $"Cash changed because: sales {FormatMoney(runtimeState.WeekToDateRevenueCents)}, COGS {FormatMoney(weekToDateCostOfGoodsSoldCents)}, payroll {FormatMoney(runtimeState.LastWeeklyPayrollCents)}, stock buys {FormatMoney(stockBuys)}, owner draw {FormatMoney(runtimeState.LastWeeklyOwnerDistributionCents)}, transfers {FormatSignedMoney(transferDelta)}.";
        }

        private string BuildWeeklyCashBridgeLine()
        {
            if (runtimeState == null)
            {
                return "Weekly bridge unavailable.";
            }

            int startingCash = runtimeState.LastWeeklyCashBeforeCents;
            int endingCash = runtimeState.LastWeeklyCashAfterCents;
            int grossProfit = runtimeState.WeekToDateRevenueCents - Mathf.Max(0, weekToDateCostOfGoodsSoldCents);
            int stockBuys = Mathf.Max(0, lastWeeklyReorderSpendCents) + Mathf.Max(0, lastWeeklyLocalSupplySpendCents);
            string direction = endingCash > startingCash ? "healthier" : endingCash < startingCash ? "weaker" : "steady";
            return $"Weekly bridge: start {FormatMoney(startingCash)} | sales {FormatMoney(runtimeState.WeekToDateRevenueCents)} | COGS {FormatMoney(weekToDateCostOfGoodsSoldCents)} | gross {FormatMoney(grossProfit)} | payroll {FormatMoney(runtimeState.LastWeeklyPayrollCents)} | reorder {FormatMoney(lastWeeklyReorderSpendCents)} | local intake {FormatMoney(lastWeeklyLocalSupplySpendCents)} | owner draw {FormatMoney(runtimeState.LastWeeklyOwnerDistributionCents)} | end {FormatMoney(endingCash)} ({direction})";
        }

        private string GetCategoryDisplayName(string categoryId)
        {
            if (string.Equals(categoryId, "tools_hardware", StringComparison.OrdinalIgnoreCase))
            {
                return "Tools & Hardware";
            }

            if (string.Equals(categoryId, "medicine_remedies", StringComparison.OrdinalIgnoreCase))
            {
                return "Medicine & Remedies";
            }

            ItemCategoryDefinition category = storeDefinition != null ? storeDefinition.FindCategory(categoryId) : null;
            return category != null ? category.DisplayName : categoryId;
        }

        private struct HouseholdReservePurchaseResult
        {
            public bool HasNeed;
            public int StoreUnits;
            public int StoreSpendCents;
            public int OtherLocalUnits;
            public int OtherLocalSpendCents;
            public int OffMapUnits;
            public int OffMapLostDemandCents;
            public int OffMapWalletSpendCents;
        }

        private readonly struct LocalReserveSale
        {
            public LocalReserveSale(int unitsSold, int spendCents)
            {
                UnitsSold = Mathf.Max(0, unitsSold);
                SpendCents = Mathf.Max(0, spendCents);
            }

            public readonly int UnitsSold;
            public readonly int SpendCents;

            public LocalReserveSale Add(LocalReserveSale other)
            {
                return new LocalReserveSale(UnitsSold + other.UnitsSold, SpendCents + other.SpendCents);
            }
        }

        private readonly struct BestLocalReserveSellerSale
        {
            public BestLocalReserveSellerSale(bool fromStore, LocalReserveSale sale)
            {
                FromStore = fromStore;
                Sale = sale;
            }

            public readonly bool FromStore;
            public readonly LocalReserveSale Sale;
        }

        private static class CategoryDemandDebug
        {
            private readonly struct DemandTotals
            {
                public DemandTotals(int requested, int storeSold, int otherLocalSold, int offMap)
                {
                    Requested = requested;
                    StoreSold = storeSold;
                    OtherLocalSold = otherLocalSold;
                    OffMap = offMap;
                }

                public readonly int Requested;
                public readonly int StoreSold;
                public readonly int OtherLocalSold;
                public readonly int OffMap;

                public DemandTotals Add(int requested, int storeSold, int otherLocalSold, int offMap)
                {
                    return new DemandTotals(
                        Requested + requested,
                        StoreSold + storeSold,
                        OtherLocalSold + otherLocalSold,
                        OffMap + offMap);
                }
            }

            private static readonly Dictionary<string, DemandTotals> totals = new();

            public static void Reset()
            {
                totals.Clear();
            }

            public static void Record(string categoryId, int requestedUnits, int storeSoldUnits, int otherLocalSoldUnits, int offMapUnits)
            {
                string key = string.IsNullOrWhiteSpace(categoryId) ? "unknown" : categoryId;
                DemandTotals existing = totals.TryGetValue(key, out DemandTotals value) ? value : new DemandTotals();
                totals[key] = existing.Add(
                    Mathf.Max(0, requestedUnits),
                    Mathf.Max(0, storeSoldUnits),
                    Mathf.Max(0, otherLocalSoldUnits),
                    Mathf.Max(0, offMapUnits));
            }

            public static string BuildSummary()
            {
                if (totals.Count == 0)
                {
                    return "No category demand resolved.";
                }

                System.Text.StringBuilder builder = new();
                builder.AppendLine("Category demand:");
                foreach (KeyValuePair<string, DemandTotals> pair in totals)
                {
                    builder.AppendLine($" - {pair.Key}: requested {pair.Value.Requested}, store {pair.Value.StoreSold}, other local {pair.Value.OtherLocalSold}, off-map {pair.Value.OffMap}");
                }

                return builder.ToString();
            }
        }

        private void RefreshReorderState()
        {
            reorderNeeded = runtimeState != null
                && storeDefinition != null
                && runtimeState.HasReorderNeed(GetEffectiveReorderThreshold01());
        }

        private void RefreshCurrentBusinessCapacityState()
        {
            currentBusiness?.RefreshCapacityState();
        }

        private static string BuildCapacitySummaryLine(BusinessCapacityState capacity)
        {
            if (capacity == null)
            {
                return "Capacity: unavailable";
            }

            return $"Capacity: storage {capacity.CurrentStoredUnits}/{capacity.StorageCapacityUnits}u | processing {capacity.CurrentProcessingUnitsPerWeek}/{capacity.ProcessingCapacityUnitsPerWeek}u/wk | service {capacity.CurrentServiceVisitsPerDay}/{capacity.ServiceCapacityVisitsPerDay}/day | bottleneck {capacity.BottleneckDisplayName}";
        }

        private void PushCashToHud()
        {
            if (playerPortfolioManager != null)
            {
                playerPortfolioManager.PushOwnerCashToHud();
            }
        }

        private void AppendLocalSupplySummary(string segment)
        {
            if (string.IsNullOrWhiteSpace(segment))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(lastWeeklyLocalSupplySummary)
                || lastWeeklyLocalSupplySummary == "No local supply received this week.")
            {
                lastWeeklyLocalSupplySummary = segment;
                return;
            }

            lastWeeklyLocalSupplySummary += "; " + segment;
        }

        private static string SanitizeLocalSupplySource(string sourceLabel)
        {
            return string.IsNullOrWhiteSpace(sourceLabel) ? "local supplier" : sourceLabel.Trim();
        }

        private static string FormatMoney(int cents)
        {
            int absoluteCents = Mathf.Abs(cents);
            string amount = "$" + (absoluteCents / 100f).ToString("N2");
            return cents < 0 ? "-" + amount : amount;
        }

        private static string CompactReason(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return "not eligible";
            }

            string trimmed = reason.Trim();
            return trimmed.Length <= 140 ? trimmed : trimmed.Substring(0, 137) + "...";
        }

        private static string FormatSignedMoney(int cents)
        {
            int amount = Mathf.Abs(cents);
            string sign = cents > 0 ? "+" : cents < 0 ? "-" : string.Empty;
            return sign + "$" + (amount / 100f).ToString("N2");
        }

        private static string FormatSignedPercent(float value01)
        {
            int percent = Mathf.RoundToInt(value01 * 100f);
            return percent >= 0 ? $"+{percent}%" : $"{percent}%";
        }

        private static string FormatPercent(float value01)
        {
            return $"{Mathf.RoundToInt(value01 * 100f)}%";
        }

        private static float CalculateGrossMargin01(int grossCents, int sellCents)
        {
            return sellCents <= 0 ? 0f : Mathf.Max(0f, (float)grossCents / sellCents);
        }

        private static float CalculateMarkupOverCost01(int grossCents, int costCents)
        {
            return costCents <= 0 ? 0f : Mathf.Max(0f, (float)grossCents / costCents);
        }

        private static string BuildDemandEffect(float priceAdjustment01)
        {
            if (priceAdjustment01 >= 0.15f)
            {
                return "slower volume";
            }

            if (priceAdjustment01 <= -0.05f)
            {
                return "easier volume";
            }

            return "steady";
        }

        private static string BuildTrustEffect(float priceAdjustment01)
        {
            if (priceAdjustment01 >= 0.15f)
            {
                return "customers watch prices";
            }

            if (priceAdjustment01 <= -0.05f)
            {
                return "customer-friendly";
            }

            return "ordinary";
        }

        private static string BuildPricingStance(int grossCents, int costCents)
        {
            if (grossCents < 0)
            {
                return "below cost";
            }

            if (grossCents == 0)
            {
                return "break-even";
            }

            int thinMarginThreshold = Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(0, costCents) * 0.1f));
            return grossCents < thinMarginThreshold ? "thin margin" : "profitable";
        }

        private readonly struct PricingSnapshot
        {
            public PricingSnapshot(int averageSellingPriceCents, int averageLandedCostCents, int averageGrossProfitCents, bool anyPriceCappedByBand)
            {
                AverageSellingPriceCents = averageSellingPriceCents;
                AverageLandedCostCents = averageLandedCostCents;
                AverageGrossProfitCents = averageGrossProfitCents;
                AnyPriceCappedByBand = anyPriceCappedByBand;
                HasPricing = true;
            }

            public readonly int AverageSellingPriceCents;
            public readonly int AverageLandedCostCents;
            public readonly int AverageGrossProfitCents;
            public readonly bool AnyPriceCappedByBand;
            public readonly bool HasPricing;
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
