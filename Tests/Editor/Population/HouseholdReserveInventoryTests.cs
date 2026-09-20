using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.MVP;
using LandLedgers.Persistence;
using LandLedgers.Population;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LandLedgers.EditorTests.Population
{
    public sealed class HouseholdReserveInventoryTests
    {
        [Test]
        public void HouseholdReserveInitializationCreatesCompactMvpCategories()
        {
            HouseholdState household = CreateHousehold();

            household.EnsureHouseholdReservesInitialized(true);

            Assert.AreEqual(HouseholdReserveCatalog.Count, household.reserves.Count);
            AssertReserve(household, "staple_food");
            AssertReserve(household, "meat");
            AssertReserve(household, "household_goods");
            AssertReserve(household, "fuel_wood");
            AssertReserve(household, "clothing");
            AssertReserve(household, "tools_hardware");
            AssertReserve(household, "medicine_remedies");
            Assert.AreEqual(household.GetReserve("staple_food").currentUnits, household.foodReserveUnits);
        }

        [Test]
        public void DailyUseDepletesReservesAndLowerStockCreatesHigherUrgency()
        {
            HouseholdState household = CreateHousehold();
            household.EnsureHouseholdReservesInitialized(true);
            int stapleBefore = household.GetReserve("staple_food").currentUnits;

            int used = HouseholdReserveEvaluator.ResolveDailyUse(household, 0);

            Assert.Greater(used, 0);
            Assert.Less(household.GetReserve("staple_food").currentUnits, stapleBefore);

            HouseholdReserveState meat = household.GetReserve("meat");
            HouseholdReserveState householdGoods = household.GetReserve("household_goods");
            meat.currentUnits = 0;
            householdGoods.currentUnits = 1;

            List<HouseholdReserveNeed> needs = HouseholdReserveEvaluator.BuildShoppingNeeds(household);

            HouseholdReserveNeed meatNeed = FindNeed(needs, "meat");
            HouseholdReserveNeed goodsNeed = FindNeed(needs, "household_goods");
            Assert.Greater(meatNeed.PurchaseUnits, 0);
            Assert.Greater(goodsNeed.PurchaseUnits, 0);
            Assert.Greater(meatNeed.Urgency01, goodsNeed.Urgency01);
        }

        [Test]
        public void RoughLodgingRaisesHouseholdReserveUrgencyThresholds()
        {
            HouseholdState stable = CreateHousehold();
            HouseholdState rough = CreateHousehold();
            rough.settlementArrangement = SettlementArrangement.Transient;
            rough.dwellingKind = HouseholdDwellingKind.RoughTemporaryLodging;
            rough.isTransientHousehold = true;
            stable.EnsureHouseholdReservesInitialized(true);
            rough.EnsureHouseholdReservesInitialized(true);

            HouseholdReserveState stableFood = stable.GetReserve(HouseholdReserveCatalog.StapleFoodCategoryId);
            HouseholdReserveState roughFood = rough.GetReserve(HouseholdReserveCatalog.StapleFoodCategoryId);

            Assert.Greater(roughFood.lowThresholdUnits, stableFood.lowThresholdUnits);
            StringAssert.Contains("rough temporary lodging", rough.BuildDwellingSummaryLine());
        }

        [Test]
        public void DwellingStateRoundTripsThroughPopulationSave()
        {
            GameObject sourceObject = new("Dwelling Save Source");
            GameObject targetObject = new("Dwelling Save Target");
            try
            {
                PopulationManager source = sourceObject.AddComponent<PopulationManager>();
                HouseholdState household = CreateHousehold();
                household.dwellingKind = HouseholdDwellingKind.RoughTemporaryLodging;
                household.dwellingSummary = "Rough temporary lodging / tent fallback";
                household.settlementArrangement = SettlementArrangement.Transient;
                household.isTransientHousehold = true;
                source.State.households.Add(household);

                PopulationSaveDto dto = source.CaptureSaveDto();
                PopulationManager target = targetObject.AddComponent<PopulationManager>();
                target.LoadFromSaveDto(dto);

                HouseholdState restored = target.State.households[0];
                Assert.AreEqual(HouseholdDwellingKind.RoughTemporaryLodging, restored.dwellingKind);
                StringAssert.Contains("Rough temporary", restored.dwellingSummary);
                StringAssert.Contains("labor reliability reduced", restored.BuildDwellingSummaryLine());
            }
            finally
            {
                Object.DestroyImmediate(sourceObject);
                Object.DestroyImmediate(targetObject);
            }
        }

        [Test]
        public void WinterHeatingConsumesFuelAndRecordsPressure()
        {
            HouseholdState household = CreateHousehold();
            household.EnsureHouseholdReservesInitialized(true);
            HouseholdReserveState fuel = household.GetReserve("fuel_wood");
            fuel.currentUnits = 1;

            int used = HouseholdReserveEvaluator.ResolveDailyUse(household, 0);
            HouseholdDemandSnapshot demand = HouseholdReserveEvaluator.BuildDemandSnapshot(household);

            Assert.Greater(used, 0);
            Assert.AreEqual(0, fuel.currentUnits);
            Assert.AreEqual(1, household.lastHeatingFuelUseUnits);
            Assert.Greater(household.lastHeatingFuelShortfallUnits, 0);
            Assert.Greater(household.lastWinterStrain01, 0f);
            Assert.Greater(household.lastHeatingPressure01, 0f);
            Assert.Greater(demand.heatingNeed, 0);
            Assert.Greater(demand.reservedFutureScore, 0);
        }

        [Test]
        public void ShoulderAndWarmMonthsApplyLowerHeatingStrain()
        {
            HouseholdState winter = CreateHousehold();
            HouseholdState shoulder = CreateHousehold();
            HouseholdState warm = CreateHousehold();
            winter.EnsureHouseholdReservesInitialized(true);
            shoulder.EnsureHouseholdReservesInitialized(true);
            warm.EnsureHouseholdReservesInitialized(true);

            HouseholdReserveEvaluator.ResolveDailyUse(winter, 0);
            HouseholdReserveEvaluator.ResolveDailyUse(shoulder, 56);
            HouseholdReserveEvaluator.ResolveDailyUse(warm, 84);

            Assert.Greater(winter.lastWinterStrain01, shoulder.lastWinterStrain01);
            Assert.Greater(shoulder.lastWinterStrain01, 0f);
            Assert.AreEqual(0f, warm.lastWinterStrain01);
            Assert.AreEqual(0, warm.lastHeatingFuelUseUnits);
        }

        [Test]
        public void BetterRetentionAndStoveReduceHeatingUseAndPressure()
        {
            HouseholdState drafty = CreateHousehold();
            HouseholdState efficient = CreateHousehold();
            drafty.heatRetentionTier = HouseholdHeatRetentionTier.Drafty;
            drafty.stoveTier = HouseholdStoveTier.Basic;
            efficient.heatRetentionTier = HouseholdHeatRetentionTier.Tight;
            efficient.stoveTier = HouseholdStoveTier.Efficient;
            drafty.EnsureHouseholdReservesInitialized(true);
            efficient.EnsureHouseholdReservesInitialized(true);

            int draftyUse = HouseholdHeatingEvaluator.CalculateDailyFuelUseUnits(drafty, 0);
            int efficientUse = HouseholdHeatingEvaluator.CalculateDailyFuelUseUnits(efficient, 0);
            HouseholdReserveEvaluator.ResolveDailyUse(drafty, 0);
            HouseholdReserveEvaluator.ResolveDailyUse(efficient, 0);

            Assert.Greater(draftyUse, efficientUse);
            Assert.Greater(drafty.lastWinterStrain01, efficient.lastWinterStrain01);
            Assert.Greater(drafty.lastHeatingPressure01, efficient.lastHeatingPressure01);
        }

        [Test]
        public void ReserveTargetsScaleWithConsumersAndBoarders()
        {
            HouseholdState small = CreateHousehold();
            small.memberIds = new List<int> { 1, 2 };
            small.boarderPersonIds = new List<int>();

            HouseholdState crowded = CreateHousehold();
            crowded.memberIds = new List<int> { 1, 2, 3, 4 };
            crowded.boarderPersonIds = new List<int> { 20, 21 };

            small.EnsureHouseholdReservesInitialized(true);
            crowded.EnsureHouseholdReservesInitialized(true);

            Assert.Greater(crowded.ConsumptionMemberCount, small.ConsumptionMemberCount);
            Assert.Greater(crowded.GetReserve(HouseholdReserveCatalog.StapleFoodCategoryId).targetUnits, small.GetReserve(HouseholdReserveCatalog.StapleFoodCategoryId).targetUnits);
            Assert.Greater(crowded.GetReserve("household_goods").targetUnits, small.GetReserve("household_goods").targetUnits);
            Assert.Greater(crowded.GetReserve("meat").targetUnits, small.GetReserve("meat").targetUnits);
        }

        [Test]
        public void MedicineNeedsSurfaceBeforeFullDepletionForLargerHouseholds()
        {
            HouseholdState household = CreateHousehold();
            household.memberIds = new List<int> { 1, 2, 3, 4 };
            household.EnsureHouseholdReservesInitialized(true);

            HouseholdReserveState medicine = household.GetReserve("medicine_remedies");
            medicine.currentUnits = Mathf.Max(0, medicine.targetUnits - 1);

            List<HouseholdReserveNeed> needs = HouseholdReserveEvaluator.BuildShoppingNeeds(household);

            HouseholdReserveNeed need = FindNeed(needs, "medicine_remedies");
            Assert.Greater(medicine.targetUnits, 2);
            Assert.Greater(need.PurchaseUnits, 0);
            Assert.GreaterOrEqual(need.Urgency01, 0.08f);
        }

        [Test]
        public void BoardersIncreaseDailyHouseholdGoodsUse()
        {
            HouseholdState baseHousehold = CreateHousehold();
            baseHousehold.memberIds = new List<int> { 1, 2 };
            baseHousehold.boarderPersonIds = new List<int>();
            baseHousehold.EnsureHouseholdReservesInitialized(true);

            HouseholdState boardingHousehold = CreateHousehold();
            boardingHousehold.memberIds = new List<int> { 1, 2, 3, 4 };
            boardingHousehold.boarderPersonIds = new List<int> { 10, 11 };
            boardingHousehold.EnsureHouseholdReservesInitialized(true);

            HouseholdReserveEvaluator.ResolveDailyUse(baseHousehold, 0);
            HouseholdReserveEvaluator.ResolveDailyUse(boardingHousehold, 0);

            Assert.Greater(boardingHousehold.GetReserve("household_goods").lastDailyUseUnits, baseHousehold.GetReserve("household_goods").lastDailyUseUnits);
        }

        [Test]
        public void PopulationSaveLoadPreservesReservesAndMigratesLegacyFoodReserve()
        {
            GameObject sourceObject = new("Reserve Save Source");
            GameObject targetObject = new("Reserve Save Target");
            GameObject legacyTargetObject = new("Reserve Legacy Target");
            try
            {
                PopulationManager source = sourceObject.AddComponent<PopulationManager>();
                HouseholdState household = CreateHousehold();
                household.EnsureHouseholdReservesInitialized(true);
                household.GetReserve("clothing").currentUnits = 1;
                household.GetReserve("fuel_wood").currentUnits = 3;
                household.heatRetentionTier = HouseholdHeatRetentionTier.Tight;
                household.stoveTier = HouseholdStoveTier.Efficient;
                household.preferredFuel = HouseholdFuelType.Wood;
                household.lastWinterStrain01 = 0.5f;
                household.lastHeatingPressure01 = 0.25f;
                household.lastHeatingFuelUseUnits = 2;
                household.lastHeatingFuelShortfallUnits = 1;
                source.State.households.Add(household);

                PopulationSaveDto dto = source.CaptureSaveDto();
                PopulationManager target = targetObject.AddComponent<PopulationManager>();
                target.LoadFromSaveDto(dto);

                HouseholdState restored = target.State.households[0];
                Assert.AreEqual(1, restored.GetReserve("clothing").currentUnits);
                Assert.AreEqual(3, restored.GetReserve("fuel_wood").currentUnits);
                Assert.AreEqual(HouseholdReserveCatalog.Count, restored.reserves.Count);
                Assert.AreEqual(HouseholdHeatRetentionTier.Tight, restored.heatRetentionTier);
                Assert.AreEqual(HouseholdStoveTier.Efficient, restored.stoveTier);
                Assert.AreEqual(HouseholdFuelType.Wood, restored.preferredFuel);
                Assert.AreEqual(0.5f, restored.lastWinterStrain01);
                Assert.AreEqual(0.25f, restored.lastHeatingPressure01);
                Assert.AreEqual(2, restored.lastHeatingFuelUseUnits);
                Assert.AreEqual(1, restored.lastHeatingFuelShortfallUnits);

                PopulationSaveDto legacyDto = new();
                legacyDto.households.Add(new HouseholdSaveDto
                {
                    id = 22,
                    householdName = "Legacy Household",
                    surname = "Legacy",
                    homeBuildingId = 3,
                    memberIds = new List<int> { 1, 2 },
                    foodReserveUnits = 4
                });

                PopulationManager legacyTarget = legacyTargetObject.AddComponent<PopulationManager>();
                legacyTarget.LoadFromSaveDto(legacyDto);

                HouseholdState legacy = legacyTarget.State.households[0];
                Assert.AreEqual(HouseholdReserveCatalog.Count, legacy.reserves.Count);
                Assert.AreEqual(4, legacy.GetReserve("staple_food").currentUnits);
                Assert.AreEqual(legacy.GetReserve("fuel_wood").targetUnits, legacy.GetReserve("fuel_wood").currentUnits);
                Assert.AreEqual(4, legacy.foodReserveUnits);
                Assert.AreEqual(HouseholdHeatRetentionTier.Basic, legacy.heatRetentionTier);
                Assert.AreEqual(HouseholdStoveTier.Basic, legacy.stoveTier);
                Assert.AreEqual(HouseholdFuelType.Wood, legacy.preferredFuel);
                Assert.AreEqual(0f, legacy.lastWinterStrain01);
                Assert.AreEqual(0f, legacy.lastHeatingPressure01);
            }
            finally
            {
                Object.DestroyImmediate(sourceObject);
                Object.DestroyImmediate(targetObject);
                Object.DestroyImmediate(legacyTargetObject);
            }
        }

        [Test]
        public void LocalGeneralStoreReservePurchaseReducesStockAndCapturesSale()
        {
            GameObject storeObject = new("Reserve Store Sale Test");
            try
            {
                GeneralStoreBusinessDefinition definition = LoadGeneralStoreDefinition();
                GeneralStoreRuntimeManager manager = CreateStoreRuntime(storeObject, definition, out BusinessRuntimeState runtime);
                HouseholdState household = CreateHousehold();
                household.EnsureHouseholdReservesInitialized(true);
                HouseholdReserveState staple = household.GetReserve("staple_food");
                staple.currentUnits = 0;

                CategoryStockState stock = runtime.GetCategoryStock("staple_food");
                int stockBefore = stock.CurrentStockUnits;
                int cashBefore = runtime.CurrentCashCents;

                InvokeResolveHouseholdReserveNeeds(manager, household, 100000);

                Assert.AreEqual(staple.targetUnits, staple.currentUnits);
                Assert.AreEqual(stockBefore - staple.targetUnits, stock.CurrentStockUnits);
                Assert.Greater(runtime.CurrentCashCents, cashBefore);
                Assert.AreEqual(staple.targetUnits, runtime.GetCategoryStock("staple_food").LastDailyUnitsSold);
            }
            finally
            {
                Object.DestroyImmediate(storeObject);
            }
        }

        [Test]
        public void SharedBusinessReservePurchaseCanSellFuelWoodFromSawmillOffcuts()
        {
            GameObject runtimeObject = new("Shared Fuel Reserve Sale Test");
            try
            {
                BusinessProfileDefinition sawmillProfile = LoadProfile(BusinessType.Sawmill);
                SharedBusinessRuntimeManager manager = runtimeObject.AddComponent<SharedBusinessRuntimeManager>();
                BusinessInstanceState sawmill = BusinessInstanceState.Create(
                    "test_sawmill",
                    sawmillProfile,
                    1,
                    BusinessOwnerIdentity.Npc(1010, "Sarah Mill", "Mill"));
                sawmill.EnsureOwnerOperatorStaffing(sawmill.Owner);
                CategoryStockState offcuts = sawmill.RuntimeState.GetCategoryStock("slabs_offcuts");
                offcuts.SetCurrentStockForTests(5);
                SetBusinesses(manager, new List<BusinessInstanceState> { sawmill });

                HouseholdReserveDefinition definition = HouseholdReserveCatalog.Get("fuel_wood");
                int budget = 100000;
                int sold = manager.TrySellHouseholdReserveUnits(
                    definition.CategoryId,
                    definition.LocalSellerCategoryIds,
                    3,
                    ref budget,
                    out int spend);

                Assert.AreEqual(3, sold);
                Assert.Greater(spend, 0);
                Assert.AreEqual(2, offcuts.CurrentStockUnits);
            }
            finally
            {
                Object.DestroyImmediate(runtimeObject);
            }
        }

        [Test]
        public void SharedBusinessReservePurchaseRequiresOperationalSellerWithStock()
        {
            GameObject runtimeObject = new("Shared Reserve Sale Test");
            try
            {
                BusinessProfileDefinition butcherProfile = LoadProfile(BusinessType.Butcher);
                SharedBusinessRuntimeManager manager = runtimeObject.AddComponent<SharedBusinessRuntimeManager>();
                BusinessInstanceState staffedButcher = BusinessInstanceState.Create(
                    "test_butcher",
                    butcherProfile,
                    1,
                    BusinessOwnerIdentity.Npc(1002, "Martha Hobbs", "Hobbs"));
                staffedButcher.EnsureOwnerOperatorStaffing(staffedButcher.Owner);
                SetBusinesses(manager, new List<BusinessInstanceState> { staffedButcher });

                int budget = 100000;
                int stockBefore = staffedButcher.RuntimeState.GetCategoryStock("meat").CurrentStockUnits;
                int sold = manager.TrySellHouseholdReserveUnits("meat", new[] { "meat" }, 3, ref budget, out int spend);

                Assert.Greater(sold, 0);
                Assert.Greater(spend, 0);
                Assert.AreEqual(stockBefore - sold, staffedButcher.RuntimeState.GetCategoryStock("meat").CurrentStockUnits);

                BusinessInstanceState unstaffedButcher = BusinessInstanceState.Create(
                    "test_unstaffed_butcher",
                    butcherProfile,
                    2,
                    BusinessOwnerIdentity.Npc(1003, "No Staff", "No Staff"));
                int unstaffedStockBefore = unstaffedButcher.RuntimeState.GetCategoryStock("meat").CurrentStockUnits;
                SetBusinesses(manager, new List<BusinessInstanceState> { unstaffedButcher });

                budget = 100000;
                sold = manager.TrySellHouseholdReserveUnits("meat", new[] { "meat" }, 3, ref budget, out spend);

                Assert.AreEqual(0, sold);
                Assert.AreEqual(0, spend);
                Assert.AreEqual(unstaffedStockBefore, unstaffedButcher.RuntimeState.GetCategoryStock("meat").CurrentStockUnits);
            }
            finally
            {
                Object.DestroyImmediate(runtimeObject);
            }
        }

        [Test]
        public void RankedLocalSellerChoiceCanPreferStoreBeforeWeakerSecondSharedSeller()
        {
            GameObject storeObject = new("Ranked Local Seller Store Test");
            GameObject sharedObject = new("Ranked Local Seller Shared Test");
            try
            {
                GeneralStoreBusinessDefinition definition = LoadGeneralStoreDefinition();
                GeneralStoreRuntimeManager storeManager = CreateStoreRuntime(storeObject, definition, out BusinessRuntimeState storeRuntime);
                SharedBusinessRuntimeManager sharedManager = sharedObject.AddComponent<SharedBusinessRuntimeManager>();
                BusinessProfileDefinition butcherProfile = LoadProfile(BusinessType.Butcher);
                BusinessInstanceState reliableFavorite = BusinessInstanceState.Create(
                    "aaa_reliable_favorite_butcher",
                    butcherProfile,
                    1,
                    BusinessOwnerIdentity.Npc(1101, "Reliable Favorite", "Favorite"));
                BusinessInstanceState weakerBackup = BusinessInstanceState.Create(
                    "bbb_weaker_backup_butcher",
                    butcherProfile,
                    2,
                    BusinessOwnerIdentity.Npc(1102, "Weaker Backup", "Backup"));

                reliableFavorite.EnsureOwnerOperatorStaffing(reliableFavorite.Owner);
                weakerBackup.EnsureOwnerOperatorStaffing(weakerBackup.Owner);
                weakerBackup.EnsureBusinessReputationInitializedFromRuntime();
                weakerBackup.BusinessReputation.RecordStockout("meat", 14, 2, 1f);
                weakerBackup.BusinessReputation.RecordStockout("meat", 15, 2, 1f);
                weakerBackup.BusinessReputation.RecordStockout("meat", 16, 2, 1f);

                SetBusinesses(sharedManager, new List<BusinessInstanceState> { reliableFavorite, weakerBackup });
                SetPrivateField(storeManager, "sharedBusinessRuntime", sharedManager);
                SetPrivateField(sharedManager, "generalStoreRuntime", storeManager);

                HouseholdState household = CreateHousehold();
                household.EnsureHouseholdReservesInitialized(true);
                household.GetReserve("meat").currentUnits = 0;

                reliableFavorite.RuntimeState.GetCategoryStock("meat").SetCurrentStockForTests(1);
                weakerBackup.RuntimeState.GetCategoryStock("meat").SetCurrentStockForTests(0);
                int primerBudget = 100000;
                int primed = sharedManager.TrySellHouseholdReserveUnits("meat", new[] { "meat" }, 1, ref primerBudget, household.id, out _);
                Assert.AreEqual(1, primed);

                reliableFavorite.RuntimeState.GetCategoryStock("meat").SetCurrentStockForTests(1);
                weakerBackup.RuntimeState.GetCategoryStock("meat").SetCurrentStockForTests(2);
                CategoryStockState storeMeat = storeRuntime.GetCategoryStock("meat");
                storeMeat.SetCurrentStockForTests(3);
                int sharedUnitPrice = GetSharedTransferUnitPrice(sharedManager, reliableFavorite, "meat");
                int storeUnitPrice = GetStoreCategoryUnitPrice(storeManager, "meat");
                int budget = sharedUnitPrice + storeUnitPrice;

                InvokeResolveHouseholdReserveNeeds(storeManager, household, budget);

                Assert.AreEqual(0, reliableFavorite.RuntimeState.GetCategoryStock("meat").CurrentStockUnits);
                Assert.AreEqual(2, storeMeat.CurrentStockUnits);
                Assert.AreEqual(2, weakerBackup.RuntimeState.GetCategoryStock("meat").CurrentStockUnits);
            }
            finally
            {
                Object.DestroyImmediate(sharedObject);
                Object.DestroyImmediate(storeObject);
            }
        }

        [Test]
        public void LowCashReserveShoppingPrioritizesStaplesBeforeDiscretionaryGoods()
        {
            GameObject storeObject = new("Reserve Budget Triage Test");
            try
            {
                GeneralStoreBusinessDefinition definition = LoadGeneralStoreDefinition();
                GeneralStoreRuntimeManager manager = CreateStoreRuntime(storeObject, definition, out BusinessRuntimeState runtime);
                HouseholdState household = CreateHousehold();
                household.EnsureHouseholdReservesInitialized(true);

                HouseholdReserveState staple = household.GetReserve(HouseholdReserveCatalog.StapleFoodCategoryId);
                staple.currentUnits = Mathf.Max(0, staple.lowThresholdUnits - 1);

                string discretionaryCategoryId = FindDiscretionaryCategoryAtOrBelowPrice(manager, "staple_food");
                Assert.IsFalse(string.IsNullOrWhiteSpace(discretionaryCategoryId), "Expected at least one discretionary category priced at or below staple food for budget-triage coverage.");

                HouseholdReserveState discretionary = household.GetReserve(discretionaryCategoryId);
                Assert.NotNull(discretionary);
                discretionary.currentUnits = 0;

                int staplePrice = GetStoreCategoryUnitPrice(manager, "staple_food");
                int discretionaryPrice = GetStoreCategoryUnitPrice(manager, discretionaryCategoryId);
                Assert.Greater(staplePrice, 0);
                Assert.Greater(discretionaryPrice, 0);
                Assert.LessOrEqual(discretionaryPrice, staplePrice);

                runtime.GetCategoryStock("staple_food").SetCurrentStockForTests(runtime.GetCategoryStock("staple_food").TargetStockUnits);
                runtime.GetCategoryStock(discretionaryCategoryId).SetCurrentStockForTests(runtime.GetCategoryStock(discretionaryCategoryId).TargetStockUnits);

                InvokeResolveHouseholdReserveNeeds(manager, household, staplePrice);

                Assert.Greater(staple.currentUnits, staple.lowThresholdUnits - 1, "Staples should be purchased first when cash only covers one reserve category.");
                Assert.AreEqual(0, discretionary.currentUnits, "Discretionary goods should defer when staples still need cash coverage.");
            }
            finally
            {
                Object.DestroyImmediate(storeObject);
            }
        }

        [Test]
        public void OffMapFallbackFillsNeedWithoutLocalStockOrRevenue()
        {
            GameObject storeObject = new("Reserve Off Map Test");
            try
            {
                GeneralStoreBusinessDefinition definition = LoadGeneralStoreDefinition();
                GeneralStoreRuntimeManager manager = CreateStoreRuntime(storeObject, definition, out BusinessRuntimeState runtime);
                HouseholdState household = CreateHousehold();
                household.EnsureHouseholdReservesInitialized(true);
                HouseholdReserveState meat = household.GetReserve("meat");
                meat.currentUnits = 0;
                runtime.GetCategoryStock("meat").SetCurrentStockForTests(0);
                int cashBefore = runtime.CurrentCashCents;

                object result = InvokeResolveHouseholdReserveNeeds(manager, household, 100000);

                Assert.AreEqual(meat.targetUnits, meat.currentUnits);
                Assert.AreEqual(0, runtime.GetCategoryStock("meat").CurrentStockUnits);
                Assert.AreEqual(cashBefore, runtime.CurrentCashCents);
                Assert.Greater(GetResultInt(result, "OffMapUnits"), 0);
                Assert.Greater(GetResultInt(result, "OffMapLostDemandCents"), 0);
                Assert.AreEqual(0, GetResultInt(result, "StoreUnits"));
            }
            finally
            {
                Object.DestroyImmediate(storeObject);
            }
        }

        private static HouseholdState CreateHousehold()
        {
            return new HouseholdState
            {
                id = 7,
                householdName = "Test Household",
                surname = "Test",
                homeBuildingId = 1,
                weeklyIncomeSnapshot = 20,
                spendingMoneyCents = 100000,
                memberIds = new List<int> { 1, 2, 3, 4 }
            };
        }

        private static void AssertReserve(HouseholdState household, string categoryId)
        {
            HouseholdReserveState reserve = household.GetReserve(categoryId);
            Assert.NotNull(reserve, $"{categoryId} reserve should exist.");
            Assert.GreaterOrEqual(reserve.targetUnits, reserve.lowThresholdUnits);
            Assert.AreEqual(reserve.targetUnits, reserve.currentUnits);
        }

        private static HouseholdReserveNeed FindNeed(List<HouseholdReserveNeed> needs, string categoryId)
        {
            for (int i = 0; i < needs.Count; i++)
            {
                if (needs[i].CategoryId == categoryId)
                {
                    return needs[i];
                }
            }

            Assert.Fail($"Expected reserve need for {categoryId}.");
            return default;
        }

        private static GeneralStoreBusinessDefinition LoadGeneralStoreDefinition()
        {
            GeneralStoreBusinessDefinition definition = AssetDatabase.LoadAssetAtPath<GeneralStoreBusinessDefinition>(
                "Assets/Core/Economy/GeneralStoreBusinessDefinition.asset");
            Assert.NotNull(definition);
            return definition;
        }

        private static int GetStoreCategoryUnitPrice(GeneralStoreRuntimeManager manager, string categoryId)
        {
            MethodInfo method = typeof(GeneralStoreRuntimeManager).GetMethod(
                "GetAverageCategoryUnitPrice",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            return (int)method.Invoke(manager, new object[] { categoryId });
        }

        private static int GetSharedTransferUnitPrice(SharedBusinessRuntimeManager manager, BusinessInstanceState business, string categoryId)
        {
            MethodInfo method = null;
            MethodInfo[] methods = typeof(SharedBusinessRuntimeManager).GetMethods(BindingFlags.NonPublic | BindingFlags.Static);
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name != "GetAverageCategoryTransferPriceCents")
                {
                    continue;
                }

                ParameterInfo[] parameters = methods[i].GetParameters();
                if (parameters.Length == 3
                    && parameters[0].ParameterType == typeof(BusinessInstanceState)
                    && parameters[1].ParameterType == typeof(BusinessProfileDefinition)
                    && parameters[2].ParameterType == typeof(string))
                {
                    method = methods[i];
                    break;
                }
            }

            Assert.NotNull(method);
            MethodInfo findProfileMethod = typeof(SharedBusinessRuntimeManager).GetMethod(
                "FindProfile",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(findProfileMethod);
            BusinessProfileDefinition profile = (BusinessProfileDefinition)findProfileMethod.Invoke(manager, new object[] { business.BusinessType });
            Assert.NotNull(profile);
            return (int)method.Invoke(null, new object[] { business, profile, categoryId });
        }

        private static string FindDiscretionaryCategoryAtOrBelowPrice(GeneralStoreRuntimeManager manager, string stapleCategoryId)
        {
            int staplePrice = GetStoreCategoryUnitPrice(manager, stapleCategoryId);
            string[] candidates = { "household_goods", "tools_hardware", "clothing" };
            for (int i = 0; i < candidates.Length; i++)
            {
                int candidatePrice = GetStoreCategoryUnitPrice(manager, candidates[i]);
                if (candidatePrice > 0 && candidatePrice <= staplePrice)
                {
                    return candidates[i];
                }
            }

            return string.Empty;
        }

        private static BusinessProfileDefinition LoadProfile(BusinessType businessType)
        {
            BusinessProfileDefinition[] profiles = Resources.LoadAll<BusinessProfileDefinition>("Core/Economy/BusinessProfiles");
            for (int i = 0; i < profiles.Length; i++)
            {
                if (profiles[i] != null && profiles[i].Business.BusinessType == businessType)
                {
                    return profiles[i];
                }
            }

            Assert.Fail($"Expected {businessType} profile.");
            return null;
        }

        private static GeneralStoreRuntimeManager CreateStoreRuntime(
            GameObject gameObject,
            GeneralStoreBusinessDefinition definition,
            out BusinessRuntimeState runtime)
        {
            GeneralStoreRuntimeManager manager = gameObject.AddComponent<GeneralStoreRuntimeManager>();
            BusinessInstanceState business = BusinessInstanceState.Create(
                "test_general_store",
                definition,
                1,
                BusinessOwnerIdentity.Player());

            for (int i = 0; i < business.RuntimeState.WorkerSlots.Count; i++)
            {
                WorkerSlotState slot = business.RuntimeState.WorkerSlots[i];
                slot.Assign($"worker_{i}", $"Worker {i}", slot.WeeklyWageCents);
            }

            business.ResolveWeeklyBaselineThroughput();
            business.ResolveDailyBaselineService();
            runtime = business.RuntimeState;

            SetPrivateField(manager, "storeDefinition", definition);
            SetPrivateField(manager, "currentBusiness", business);
            SetPrivateField(manager, "runtimeState", runtime);
            SetPrivateField(manager, "storeBuildingId", 1);
            return manager;
        }

        private static object InvokeResolveHouseholdReserveNeeds(GeneralStoreRuntimeManager manager, HouseholdState household, int budgetCents)
        {
            MethodInfo method = typeof(GeneralStoreRuntimeManager).GetMethod(
                "ResolveHouseholdReserveNeeds",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            return method.Invoke(manager, new object[] { household, budgetCents });
        }

        private static int GetResultInt(object result, string fieldName)
        {
            FieldInfo field = result.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(field, $"Result field {fieldName} should exist.");
            return (int)field.GetValue(result);
        }

        private static void SetBusinesses(SharedBusinessRuntimeManager manager, List<BusinessInstanceState> businesses)
        {
            SetPrivateField(manager, "businesses", businesses);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field, $"Field {fieldName} should exist on {target.GetType().Name}.");
            field.SetValue(target, value);
        }
    }
}
