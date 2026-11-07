using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.FirstLedger;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Time;
using LandLedgers.World.Regional;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LandLedgers.EditorTests.Population
{
    public sealed class HouseholdReserveInventoryTests
    {
        [Test]
        public void HouseholdReserveInitializationCreatesCompactSliceCategories()
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
                StringAssert.Contains("rough temporary", restored.dwellingSummary.ToLowerInvariant());
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
            household.heatRetentionTier = HouseholdHeatRetentionTier.Drafty;
            household.EnsureHouseholdReservesInitialized(true);
            HouseholdReserveState fuel = household.GetReserve("fuel_wood");
            fuel.currentUnits = 1;

            int used = HouseholdReserveEvaluator.ResolveDailyUse(
                household, CalendarAnchor.Default.ToAbsoluteDay(1880, 12, 15));
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

            // The Pass V calendar is Gregorian and absolute day 0 is the
            // March 1 anchor; the old 28-day-month values (0/56/84) no
            // longer represent winter/shoulder/warm dates.
            int winterDay = CalendarAnchor.Default.ToAbsoluteDay(1880, 12, 15);
            int shoulderDay = CalendarAnchor.Default.ToAbsoluteDay(1880, 3, 15);
            int warmDay = CalendarAnchor.Default.ToAbsoluteDay(1880, 4, 15);

            HouseholdReserveEvaluator.ResolveDailyUse(winter, winterDay);
            HouseholdReserveEvaluator.ResolveDailyUse(shoulder, shoulderDay);
            HouseholdReserveEvaluator.ResolveDailyUse(warm, warmDay);

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
        public void PartialReserveSaveMigratesLegacyFoodWhenStapleCategoryIsMissing()
        {
            GameObject targetObject = new("Partial Reserve Save Target");
            try
            {
                PopulationSaveDto dto = new();
                dto.households.Add(new HouseholdSaveDto
                {
                    id = 23,
                    householdName = "Partially Migrated",
                    surname = "Household",
                    foodReserveUnits = 4,
                    reserves = new List<HouseholdReserveSaveDto>
                    {
                        new() { categoryId = "fuel_wood", currentUnits = 2 }
                    }
                });
                PopulationManager target = targetObject.AddComponent<PopulationManager>();
                target.LoadFromSaveDto(dto);
                HouseholdState household = target.State.households[0];

                Assert.AreEqual(4, household.GetReserve("staple_food").currentUnits,
                    "a partial reserve save must not erase the legacy food balance when adding the missing staple category");
                Assert.AreEqual(2, household.GetReserve("fuel_wood").currentUnits,
                    "preserving legacy food must not overwrite already-migrated categories");
                Assert.AreEqual(4, household.foodReserveUnits,
                    "the compatibility field remains synchronized to the authoritative reserve after load");
            }
            finally
            {
                Object.DestroyImmediate(targetObject);
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
                stock.SetCurrentStockForTests(staple.targetUnits);
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
                ActivateAllWorkerSlots(sawmill, "fuel_sales_worker");
                CategoryStockState offcuts = sawmill.RuntimeState.GetCategoryStock("slabs_offcuts");
                offcuts.SetCurrentStockForTests(5);
                CategoryStockState lumber = sawmill.RuntimeState.GetCategoryStock("lumber");
                int mappedStockBefore = offcuts.CurrentStockUnits + lumber.CurrentStockUnits;
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
                int mappedStockAfter = offcuts.CurrentStockUnits + lumber.CurrentStockUnits;
                Assert.AreEqual(mappedStockBefore - 3, mappedStockAfter,
                    "The sale should reduce one of the authored local seller categories by the sold quantity.");
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
                ActivateAllWorkerSlots(staffedButcher, "butcher_sales_worker");
                staffedButcher.RuntimeState.GetCategoryStock("meat").SetCurrentStockForTests(48);
                Assert.AreEqual(BusinessRoleCoverageStatus.Covered, staffedButcher.BuildRoleCoverageSnapshot().Status,
                    "The positive seller fixture must model a covered, active butcher.");
                SetBusinesses(manager, new List<BusinessInstanceState> { staffedButcher });

                int budget = 100000;
                int stockBefore = staffedButcher.RuntimeState.GetCategoryStock("meat").CurrentStockUnits;
                BusinessRoleCoverageSnapshot coverage = staffedButcher.BuildRoleCoverageSnapshot();
                bool hasEligibleOffer = manager.TryEvaluateBestHouseholdReserveSellerScore(
                    "meat", new[] { "meat" }, 3, budget,
                    out LandLedgers.Reputation.BusinessReputationSellerChoiceScore sellerScore);
                int unitPrice = GetSharedTransferUnitPrice(manager, staffedButcher, "meat");
                int sold = manager.TrySellHouseholdReserveUnits("meat", new[] { "meat" }, 3, ref budget, out int spend);

                Assert.Greater(sold, 0,
                    $"Covered butcher sale returned zero. eligibleOffer={hasEligibleOffer}/{sellerScore.Eligible}, " +
                    $"stock={stockBefore}, unitPrice={unitPrice}, operatingEfficiency={coverage.OperatingEfficiency01}, " +
                    $"reliability={staffedButcher.RuntimeState.Reliability01}.");
                Assert.Greater(spend, 0);
                Assert.AreEqual(stockBefore - sold, staffedButcher.RuntimeState.GetCategoryStock("meat").CurrentStockUnits);

                BusinessInstanceState unstaffedButcher = BusinessInstanceState.Create(
                    "test_unstaffed_butcher",
                    butcherProfile,
                    2,
                    BusinessOwnerIdentity.Npc(1003, "No Staff", "No Staff"));
                unstaffedButcher.RuntimeState.GetCategoryStock("meat").SetCurrentStockForTests(48);
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

                string discretionaryCategoryId = FindAvailableDiscretionaryCategory(manager);
                Assert.IsFalse(string.IsNullOrWhiteSpace(discretionaryCategoryId), "Expected an authored discretionary category for budget-triage coverage.");

                HouseholdReserveState discretionary = household.GetReserve(discretionaryCategoryId);
                Assert.NotNull(discretionary);
                discretionary.currentUnits = 0;

                int staplePrice = GetStoreCategoryUnitPrice(manager, "staple_food");
                int discretionaryPrice = GetStoreCategoryUnitPrice(manager, discretionaryCategoryId);
                Assert.Greater(staplePrice, 0);
                Assert.Greater(discretionaryPrice, 0);

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
        public void NamedSupplierReservePurchaseShipsRealStock()
        {
            // Plan 06 Slice 1 (AUD-328): the off-map leg now buys from a
            // NAMED regional supplier through a real Ship() decrement.
            // This rewrites OffMapFallbackFillsNeedWithoutLocalStockOrRevenue,
            // which pinned the defect as correct behaviour (plan section 4
            // and 7: it was EXPECTED to fail against repaired code until
            // rewritten). The Slice 0 discriminator
            // OffMapReservePurchaseConjuresGoodsAndDestroysCash pinned the
            // same pre-fix behaviour and was removed in this slice for the
            // same reason; its pre-fix proof stands in commit 3633474.
            // Conservation equation, in numbers, for the 5u meat need:
            // household debit 155c = supplier goods revenue (5u x 6c beef
            // = 30c) + delivered freight (125c solo Ashford freight);
            // supplier stock falls by exactly the shipped units (5u);
            // reserve + lots rise by exactly the shipped units (5u); the
            // lots name the supplier as provenance.
            GameObject storeObject = new("Reserve Named Supplier Test");
            GameObject populationObject = new("Reserve Named Supplier Population");
            try
            {
                GeneralStoreBusinessDefinition definition = LoadGeneralStoreDefinition();
                GeneralStoreRuntimeManager manager = CreateStoreRuntime(storeObject, definition, out BusinessRuntimeState runtime);
                PopulationManager population = populationObject.AddComponent<PopulationManager>();
                HouseholdState household = CreateHousehold();
                household.weeklyIncomeSnapshot = 10000;
                household.EnsureHouseholdReservesInitialized(true);
                HouseholdReserveState meat = household.GetReserve("meat");
                for (int i = 0; i < household.reserves.Count; i++)
                {
                    if (household.reserves[i] != null && !string.Equals(household.reserves[i].categoryId, "meat", System.StringComparison.OrdinalIgnoreCase))
                    {
                        household.reserves[i].currentUnits = household.reserves[i].targetUnits;
                    }
                }
                meat.currentUnits = 0;
                int meatTarget = meat.targetUnits;
                population.State.households.Add(household);
                SetPrivateField(manager, "populationManager", population);
                var inventories = new HouseholdInventoryRegistry();
                manager.HouseholdInventories = inventories;
                foreach (string categoryId in new[] { "staple_food", "meat", "household_goods", "clothing", "tools_hardware", "medicine_remedies" })
                {
                    CategoryStockState stock = runtime.GetCategoryStock(categoryId);
                    if (stock != null)
                    {
                        stock.SetCurrentStockForTests(0);
                    }
                }
                InvokePrivateMethod(manager, "EnsureRegionalSuppliersExist");
                var adapters = (List<RegionalSupplierAdapter>)GetPrivateField(manager, "regionalSuppliers");
                Assert.NotNull(adapters);
                RegionalSupplierAdapter ashfordButcher = adapters.Find(a => a.SupplierId == "ashford_butcher");
                RegionalSupplierAdapter redfieldButcher = adapters.Find(a => a.SupplierId == "redfield_butcher");
                Assert.NotNull(ashfordButcher);
                Assert.NotNull(redfieldButcher);
                int ashfordMeatBefore = ashfordButcher.AvailableUnits("meat");
                int ashfordBeefBefore = ashfordButcher.OnHandUnits("beef");
                int redfieldMeatBefore = redfieldButcher.AvailableUnits("meat");
                int goodsQuoteBefore = ashfordButcher.QuoteUnitCostCents("meat");
                int deliveredPerUnitBefore = RegionalDeliveredCost.DeliveredCentsPerUnit(ashfordButcher, "meat", meatTarget, RegionalFreightRateCard.OpeningCompany);
                int expectedGoodsRevenue = meatTarget * goodsQuoteBefore;
                int expectedTotalDebit = meatTarget * deliveredPerUnitBefore;
                int cashBefore = household.spendingMoneyCents;
                int tillBefore = runtime.CurrentCashCents;

                manager.ResolveDailySales();

                int shipped = meat.currentUnits;
                Assert.AreEqual(meatTarget, shipped, "Expected the named-supplier leg to fill the meat need to target.");
                Assert.AreEqual(ashfordMeatBefore - shipped, ashfordButcher.AvailableUnits("meat"),
                    "Supplier stock must fall by exactly the shipped units.");
                Assert.AreEqual(ashfordBeefBefore - shipped, ashfordButcher.OnHandUnits("beef"),
                    "Cheapest item (beef) ships first.");
                Assert.AreEqual(redfieldMeatBefore, redfieldButcher.AvailableUnits("meat"),
                    "The unchosen supplier must not move.");
                Assert.AreEqual(cashBefore - expectedTotalDebit, household.spendingMoneyCents,
                    "Household cash must fall by exactly goods + delivered cost.");
                Assert.AreEqual(125, expectedTotalDebit - expectedGoodsRevenue,
                    "Delivered freight is the 125c solo Ashford freight (25c base + 2 x 50mi x 1c).");
                Assert.AreEqual(tillBefore, runtime.CurrentCashCents, "No store sale happened, so the till must not move.");
                Assert.AreEqual(0, runtime.GetCategoryStock("meat").CurrentStockUnits);
                Assert.AreEqual(0, manager.LastDailyReserveLocalUnits, "No local reserve purchase should have happened.");
                Assert.AreEqual(shipped, manager.LastDailyReserveOffMapUnits);
                Assert.AreEqual(0, manager.LastDailyReserveOffMapLostDemandCents, "A full fill leaves no lost demand.");
                HouseholdInventory inventory = inventories.Get(household.id);
                Assert.NotNull(inventory);
                Assert.AreEqual(shipped, SumLotUnits(inventory, HouseholdItemCatalog.UnspecifiedMeatId),
                    "The mirrored meat lots must rise by exactly the shipped units.");
                HouseholdItemLot meatLot = FindLot(inventory, HouseholdItemCatalog.UnspecifiedMeatId);
                Assert.NotNull(meatLot);
                StringAssert.Contains("ashford_butcher", meatLot.SourceLabel,
                    "The mirrored lots must name the supplier as provenance, not 'off-map purchase'.");
            }
            finally
            {
                Object.DestroyImmediate(populationObject);
                Object.DestroyImmediate(storeObject);
            }
        }

        [Test]
        public void NamedSupplierReservePurchaseRefusesLoudlyWhenUncovered()
        {
            // Plan 06 Slice 1 (AUD-328): fuel_wood is the one reserve
            // category no registered regional supplier covers (no supplier
            // offers any fuel item), so the purchase must refuse loudly:
            // reserve unchanged, household cash unchanged, the remainder
            // carried as lost demand, no charge and no goods. The loud
            // refusal itself is the helper's Debug.LogWarning (named
            // category + supplier gap); its observable consequences are
            // asserted here.
            GameObject storeObject = new("Reserve Uncovered Refusal Store");
            GameObject populationObject = new("Reserve Uncovered Refusal Population");
            try
            {
                GeneralStoreBusinessDefinition definition = LoadGeneralStoreDefinition();
                GeneralStoreRuntimeManager manager = CreateStoreRuntime(storeObject, definition, out BusinessRuntimeState runtime);
                PopulationManager population = populationObject.AddComponent<PopulationManager>();
                HouseholdState household = CreateHousehold();
                household.weeklyIncomeSnapshot = 10000;
                household.EnsureHouseholdReservesInitialized(true);
                HouseholdReserveState fuel = household.GetReserve("fuel_wood");
                for (int i = 0; i < household.reserves.Count; i++)
                {
                    if (household.reserves[i] != null && !string.Equals(household.reserves[i].categoryId, "fuel_wood", System.StringComparison.OrdinalIgnoreCase))
                    {
                        household.reserves[i].currentUnits = household.reserves[i].targetUnits;
                    }
                }
                fuel.currentUnits = 0;
                population.State.households.Add(household);
                SetPrivateField(manager, "populationManager", population);
                var inventories = new HouseholdInventoryRegistry();
                manager.HouseholdInventories = inventories;
                foreach (string categoryId in new[] { "staple_food", "meat", "household_goods", "clothing", "tools_hardware", "medicine_remedies" })
                {
                    CategoryStockState stock = runtime.GetCategoryStock(categoryId);
                    if (stock != null)
                    {
                        stock.SetCurrentStockForTests(0);
                    }
                }
                int offMapUnitValue = GetOffMapReserveUnitValue(manager, "fuel_wood");
                Assert.Greater(offMapUnitValue, 0);
                int cashBefore = household.spendingMoneyCents;
                int tillBefore = runtime.CurrentCashCents;

                manager.ResolveDailySales();

                Assert.AreEqual(0, fuel.currentUnits, "An uncovered category must buy nothing.");
                Assert.Greater(fuel.lastRequestedPurchaseUnits, 0, "The fixture must actually have a fuel need to refuse.");
                Assert.AreEqual(cashBefore, household.spendingMoneyCents, "A refused purchase charges nothing.");
                Assert.AreEqual(tillBefore, runtime.CurrentCashCents);
                Assert.AreEqual(0, manager.LastDailyReserveOffMapUnits, "No off-map units were bought.");
                Assert.AreEqual(0, manager.LastDailyReserveLocalUnits);
                Assert.AreEqual(fuel.lastRequestedPurchaseUnits * offMapUnitValue, manager.LastDailyReserveOffMapLostDemandCents,
                    "The unfilled remainder must be carried as lost demand at the off-map unit value.");
                Assert.IsTrue(inventories.Get(household.id) == null,
                    "A refused purchase must not create inventory lots.");
            }
            finally
            {
                Object.DestroyImmediate(populationObject);
                Object.DestroyImmediate(storeObject);
            }
        }

        [Test]
        public void PartialFillReattemptsOnlyTheRemainingDeficit()
        {
            // Plan 06 Slice 2 (AUD-328): a partial supplier fill is
            // exactly once per gated day — the shortfall is never charged,
            // and the next gated day ships only the remaining deficit.
            // This distinguishes the AUD-328 claim (fixed: one purchase
            // moves real stock and real cash exactly once) from the still
            // open AUD-335 claim (Q03: the loop never credits reserves, so
            // the same category can be bought again on a later gated day
            // because the deficit never closes). Here the deficit DOES
            // close across the two drives (3u then 2u = the 5u need), so
            // no third purchase fires.
            ReservePurchaseFixture fixture = CreateReservePurchaseFixture("meat");
            try
            {
                RegionalSupplierAdapter ashfordButcher = fixture.Adapters.Find(a => a.SupplierId == "ashford_butcher");
                RegionalSupplierAdapter redfieldButcher = fixture.Adapters.Find(a => a.SupplierId == "redfield_butcher");
                Assert.NotNull(ashfordButcher);
                Assert.NotNull(redfieldButcher);
                // Drain Ashford to 3u of meat (cheese only: beef 72 +
                // bacon 64 + 45 of 48 cheese ship out), so the 5u need can
                // only partly fill from the cheapest-delivered supplier.
                // Redfield still holds 184u but is not retried in the
                // same drive: the helper chooses ONE supplier per need
                // per day.
                var diag = new List<string>();
                Assert.AreEqual(181, ashfordButcher.Ship("meat", 181, 0, diag));
                Assert.AreEqual(3, ashfordButcher.AvailableUnits("meat"));
                Assert.AreEqual(3, ashfordButcher.OnHandUnits("cheese"));
                int redfieldMeatBefore = redfieldButcher.AvailableUnits("meat");
                HouseholdReserveState meat = fixture.Household.GetReserve("meat");
                int cashBefore = fixture.Household.spendingMoneyCents;

                fixture.Manager.ResolveDailySales();

                // Day 1: 3u ship at the 3-unit delivered price (goods 6c
                // + 125c freight over 3u = 48c/u); the 2u shortfall is
                // never charged and is carried as lost demand at that
                // same price.
                Assert.AreEqual(3, meat.currentUnits);
                Assert.AreEqual(cashBefore - 144, fixture.Household.spendingMoneyCents);
                Assert.AreEqual(3, fixture.Manager.LastDailyReserveOffMapUnits);
                Assert.AreEqual(96, fixture.Manager.LastDailyReserveOffMapLostDemandCents);
                Assert.AreEqual(0, ashfordButcher.AvailableUnits("meat"));
                Assert.AreEqual(redfieldMeatBefore, redfieldButcher.AvailableUnits("meat"));

                // Gated days pass: the fixture has no clock (absoluteDay
                // stays 0), so the last attempt is moved back past the
                // gate cadence instead — the gate's own "days since
                // attempt" branch opens on the next drive.
                meat.lastShoppingAttemptDayIndex = -10;
                int cashAfterDay1 = fixture.Household.spendingMoneyCents;

                fixture.Manager.ResolveDailySales();

                // Day 2: only the remaining 2u deficit ships, now from
                // Redfield (Ashford is empty): goods 7c + 225c freight
                // over 2u = 120c/u, 240c total. No double Ship, no double
                // charge.
                Assert.AreEqual(5, meat.currentUnits);
                Assert.AreEqual(cashAfterDay1 - 240, fixture.Household.spendingMoneyCents);
                Assert.AreEqual(2, fixture.Manager.LastDailyReserveOffMapUnits);
                Assert.AreEqual(0, fixture.Manager.LastDailyReserveOffMapLostDemandCents);
                Assert.AreEqual(0, ashfordButcher.AvailableUnits("meat"));
                Assert.AreEqual(redfieldMeatBefore - 2, redfieldButcher.AvailableUnits("meat"));
                Assert.AreEqual(70, redfieldButcher.OnHandUnits("beef"), "Cheapest item (beef) ships first.");
                HouseholdInventory inventory = fixture.Inventories.Get(fixture.Household.id);
                Assert.NotNull(inventory);
                Assert.AreEqual(5, SumLotUnits(inventory, HouseholdItemCatalog.UnspecifiedMeatId),
                    "Lots across both days total exactly the shipped units.");
            }
            finally
            {
                fixture.Destroy();
            }
        }

        [Test]
        public void GSRMRebuysFoodAlreadyHeld()
        {
            // Plan 08 Slice 0 (AUD-335, GSRM half, Unity Test Runner):
            // the loop half leaves 5u of flour lots in the household with
            // the staple_food reserve still at 0. This test drives the
            // GSRM half from exactly that state and proves the re-buy: on
            // a gated day the reserve path buys staple_food again and
            // charges the household a second time for food it already
            // holds. Expected to change in Slice 1: after the repair the
            // reserve is credited by the loop receipt, so no second
            // purchase fires and this test is rewritten into
            // GSRMDoesNotRebuyFoodAlreadyHeld.
            ReservePurchaseFixture fixture = CreateReservePurchaseFixture("staple_food");
            try
            {
                HouseholdReserveState staple = fixture.Household.GetReserve("staple_food");
                Assert.NotNull(staple, "The fixture household must hold a staple_food reserve.");
                Assert.AreEqual(0, staple.currentUnits, "The reserve starts empty, as after a loop receipt.");
                HouseholdInventory inventory = fixture.Inventories.GetOrCreate(fixture.Household.id);
                Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.FlourId, 5, 0, "loop receipt (Plan 08 Slice 0)"));
                Assert.AreEqual(5, inventory.GetAvailableUnits(HouseholdItemCatalog.FlourId),
                    "The household already holds 5u of flour lots from the loop.");

                int supplierStapleBefore = 0;
                foreach (RegionalSupplierAdapter adapter in fixture.Adapters)
                {
                    supplierStapleBefore += adapter.AvailableUnits("staple_food");
                }
                Assert.Greater(supplierStapleBefore, 0, "The fixture must hold staple_food with a regional supplier.");
                int cashBefore = fixture.Household.spendingMoneyCents;

                fixture.Manager.ResolveDailySales();

                Assert.Greater(staple.currentUnits, 0,
                    "AUD-335: the GSRM path bought staple_food again despite 5u of flour lots already held.");
                Assert.AreEqual(
                    fixture.Manager.LastDailyReserveOffMapUnits + fixture.Manager.LastDailyReserveLocalUnits,
                    staple.currentUnits, "Every purchased unit must land in the reserve.");
                Assert.Less(fixture.Household.spendingMoneyCents, cashBefore,
                    "AUD-335: the household was charged a second time for food it already holds.");
                int supplierStapleAfter = 0;
                foreach (RegionalSupplierAdapter adapter in fixture.Adapters)
                {
                    supplierStapleAfter += adapter.AvailableUnits("staple_food");
                }
                Assert.AreEqual(supplierStapleBefore - fixture.Manager.LastDailyReserveOffMapUnits, supplierStapleAfter,
                    "Off-map units shipped must leave supplier stock by exactly that count.");
                Assert.AreEqual(5, inventory.GetAvailableUnits(HouseholdItemCatalog.FlourId),
                    "The loop-held flour lots are untouched by the re-buy.");
            }
            finally
            {
                fixture.Destroy();
            }
        }

        [Test]
        public void SaveLoadPreservesSupplierDecrement()
        {
            // Plan 06 Slice 2 (AUD-328): a supplier decrement survives
            // save and load, and the next gated day neither re-ships nor
            // re-charges.
            ReservePurchaseFixture fixture = CreateReservePurchaseFixture("meat");
            GameObject freshStoreObject = new("Reserve Save Load Fresh Store");
            try
            {
                RegionalSupplierAdapter ashfordButcher = fixture.Adapters.Find(a => a.SupplierId == "ashford_butcher");
                Assert.NotNull(ashfordButcher);
                HouseholdReserveState meat = fixture.Household.GetReserve("meat");
                int cashBefore = fixture.Household.spendingMoneyCents;

                fixture.Manager.ResolveDailySales();

                Assert.AreEqual(5, meat.currentUnits);
                Assert.AreEqual(cashBefore - 155, fixture.Household.spendingMoneyCents);
                Assert.AreEqual(179, ashfordButcher.AvailableUnits("meat"));
                Assert.AreEqual(5, SumLotUnits(fixture.Inventories.Get(fixture.Household.id), HouseholdItemCatalog.UnspecifiedMeatId));

                GeneralStoreSaveDto dto = fixture.Manager.CaptureSaveDto();
                Assert.NotNull(dto);
                Assert.NotNull(dto.regionalSupply);

                GeneralStoreRuntimeManager freshManager = CreateStoreRuntime(freshStoreObject, LoadGeneralStoreDefinition(), out BusinessRuntimeState freshRuntime);
                freshManager.LoadFromSaveDto(dto);
                var freshAdapters = (List<RegionalSupplierAdapter>)GetPrivateField(freshManager, "regionalSuppliers");
                Assert.NotNull(freshAdapters);
                RegionalSupplierAdapter freshAshfordButcher = freshAdapters.Find(a => a.SupplierId == "ashford_butcher");
                Assert.NotNull(freshAshfordButcher);
                Assert.AreEqual(179, freshAshfordButcher.AvailableUnits("meat"),
                    "The restored supplier must hold the post-purchase stock, not the registry starting stock (184).");
                Assert.AreEqual(67, freshAshfordButcher.OnHandUnits("beef"));

                // The household itself is untouched by the load; bind it
                // to the fresh manager and drive the next gated day. The
                // need is gone (reserve full), so nothing ships and
                // nothing is charged.
                SetPrivateField(freshManager, "populationManager", fixture.Population);
                freshManager.HouseholdInventories = fixture.Inventories;
                meat.lastShoppingAttemptDayIndex = -10;
                int cashAfterLoad = fixture.Household.spendingMoneyCents;

                freshManager.ResolveDailySales();

                Assert.AreEqual(5, meat.currentUnits, "The load must not change the household reserve.");
                Assert.AreEqual(5, SumLotUnits(fixture.Inventories.Get(fixture.Household.id), HouseholdItemCatalog.UnspecifiedMeatId),
                    "The load must not change the household lots.");
                Assert.AreEqual(cashAfterLoad, fixture.Household.spendingMoneyCents, "No second purchase, no second charge.");
                Assert.AreEqual(0, freshManager.LastDailyReserveOffMapUnits, "No second Ship after load.");
                Assert.AreEqual(179, freshAshfordButcher.AvailableUnits("meat"));
            }
            finally
            {
                Object.DestroyImmediate(freshStoreObject);
                fixture.Destroy();
            }
        }

        [Test]
        public void NullDtoRestoresStartingStock()
        {
            // Plan 06 Slice 2 (AUD-328): a null regional supply DTO (an
            // old save) restores registry starting stock, so an old save
            // is never mistaken for a lost decrement.
            ReservePurchaseFixture fixture = CreateReservePurchaseFixture("meat");
            try
            {
                RegionalSupplierAdapter ashfordButcher = fixture.Adapters.Find(a => a.SupplierId == "ashford_butcher");
                Assert.NotNull(ashfordButcher);
                var diag = new List<string>();
                Assert.AreEqual(5, ashfordButcher.Ship("meat", 5, 0, diag));
                Assert.AreEqual(179, ashfordButcher.AvailableUnits("meat"));

                InvokePrivateMethodWithArgument(fixture.Manager, "RestoreRegionalSupply", null);

                Assert.AreEqual(184, ashfordButcher.AvailableUnits("meat"));
                Assert.AreEqual(72, ashfordButcher.OnHandUnits("beef"));
                Assert.AreEqual(64, ashfordButcher.OnHandUnits("bacon"));
                Assert.AreEqual(48, ashfordButcher.OnHandUnits("cheese"));
            }
            finally
            {
                fixture.Destroy();
            }
        }

        /// <summary>Shared fixture for the Plan 06 Slice 2 repeat / save-load tests.</summary>
        private sealed class ReservePurchaseFixture
        {
            public GameObject StoreObject;
            public GameObject PopulationObject;
            public GeneralStoreRuntimeManager Manager;
            public BusinessRuntimeState Runtime;
            public PopulationManager Population;
            public HouseholdState Household;
            public HouseholdInventoryRegistry Inventories;
            public List<RegionalSupplierAdapter> Adapters;

            public void Destroy()
            {
                Object.DestroyImmediate(PopulationObject);
                Object.DestroyImmediate(StoreObject);
            }
        }

        private static ReservePurchaseFixture CreateReservePurchaseFixture(string categoryId)
        {
            var fixture = new ReservePurchaseFixture();
            fixture.StoreObject = new GameObject("Reserve Purchase Fixture Store");
            fixture.PopulationObject = new GameObject("Reserve Purchase Fixture Population");
            GeneralStoreBusinessDefinition definition = LoadGeneralStoreDefinition();
            fixture.Manager = CreateStoreRuntime(fixture.StoreObject, definition, out BusinessRuntimeState runtime);
            fixture.Runtime = runtime;
            fixture.Population = fixture.PopulationObject.AddComponent<PopulationManager>();
            HouseholdState household = CreateHousehold();
            // Daily budget cap: weeklyIncomeSnapshot 10000 pays 60000c/day,
            // so the budget never binds and the purchase size is the need.
            household.weeklyIncomeSnapshot = 10000;
            household.EnsureHouseholdReservesInitialized(true);
            for (int i = 0; i < household.reserves.Count; i++)
            {
                if (household.reserves[i] != null && !string.Equals(household.reserves[i].categoryId, categoryId, System.StringComparison.OrdinalIgnoreCase))
                {
                    household.reserves[i].currentUnits = household.reserves[i].targetUnits;
                }
            }
            HouseholdReserveState emptyReserve = household.GetReserve(categoryId);
            Assert.NotNull(emptyReserve, $"Reserve {categoryId} should exist.");
            emptyReserve.currentUnits = 0;
            fixture.Household = household;
            fixture.Population.State.households.Add(household);
            SetPrivateField(fixture.Manager, "populationManager", fixture.Population);
            fixture.Inventories = new HouseholdInventoryRegistry();
            fixture.Manager.HouseholdInventories = fixture.Inventories;
            ZeroAllStoreCategoryStocks(runtime);
            InvokePrivateMethod(fixture.Manager, "EnsureRegionalSuppliersExist");
            fixture.Adapters = (List<RegionalSupplierAdapter>)GetPrivateField(fixture.Manager, "regionalSuppliers");
            Assert.NotNull(fixture.Adapters, "The manager should hold the twelve registered regional suppliers.");
            return fixture;
        }

        private static void ZeroAllStoreCategoryStocks(BusinessRuntimeState runtime)
        {
            foreach (string categoryId in new[] { "staple_food", "meat", "household_goods", "clothing", "tools_hardware", "medicine_remedies" })
            {
                CategoryStockState stock = runtime.GetCategoryStock(categoryId);
                if (stock != null)
                {
                    stock.SetCurrentStockForTests(0);
                }
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

        private static string FindAvailableDiscretionaryCategory(GeneralStoreRuntimeManager manager)
        {
            string[] candidates = { "household_goods", "tools_hardware", "clothing" };
            for (int i = 0; i < candidates.Length; i++)
            {
                int candidatePrice = GetStoreCategoryUnitPrice(manager, candidates[i]);
                if (candidatePrice > 0)
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

        private static int GetOffMapReserveUnitValue(GeneralStoreRuntimeManager manager, string categoryId)
        {
            MethodInfo method = typeof(GeneralStoreRuntimeManager).GetMethod(
                "GetOffMapReserveUnitValueCents",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            return (int)method.Invoke(manager, new object[] { categoryId });
        }

        private static object GetPrivateField(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field, $"Field {fieldName} should exist on {target.GetType().Name}.");
            return field.GetValue(target);
        }

        private static void InvokePrivateMethod(object target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method, $"Method {methodName} should exist on {target.GetType().Name}.");
            method.Invoke(target, null);
        }

        private static void InvokePrivateMethodWithArgument(object target, string methodName, object argument)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method, $"Method {methodName} should exist on {target.GetType().Name}.");
            method.Invoke(target, new object[] { argument });
        }

        private static int TotalAvailableUnits(List<RegionalSupplierAdapter> adapters, string categoryId)
        {
            int total = 0;
            for (int i = 0; i < adapters.Count; i++)
            {
                if (adapters[i] != null)
                {
                    total += adapters[i].AvailableUnits(categoryId);
                }
            }

            return total;
        }

        private static int SumLotUnits(HouseholdInventory inventory, string itemId)
        {
            int total = 0;
            for (int i = 0; i < inventory.Lots.Count; i++)
            {
                HouseholdItemLot lot = inventory.Lots[i];
                if (lot != null && string.Equals(lot.ItemId, itemId, System.StringComparison.OrdinalIgnoreCase))
                {
                    total += lot.QuantityUnits;
                }
            }

            return total;
        }

        private static HouseholdItemLot FindLot(HouseholdInventory inventory, string itemId)
        {
            for (int i = 0; i < inventory.Lots.Count; i++)
            {
                HouseholdItemLot lot = inventory.Lots[i];
                if (lot != null && string.Equals(lot.ItemId, itemId, System.StringComparison.OrdinalIgnoreCase))
                {
                    return lot;
                }
            }

            return null;
        }

        private static void SetBusinesses(SharedBusinessRuntimeManager manager, List<BusinessInstanceState> businesses)
        {
            SetPrivateField(manager, "businesses", businesses);
        }

        private static void ActivateAllWorkerSlots(BusinessInstanceState business, string workerPrefix)
        {
            Assert.NotNull(business);
            for (int i = 0; i < business.RuntimeState.WorkerSlots.Count; i++)
            {
                WorkerSlotState slot = business.RuntimeState.WorkerSlots[i];
                if (slot == null)
                {
                    continue;
                }

                slot.Assign($"{workerPrefix}_{i}", $"{workerPrefix} {i}", slot.WeeklyWageCents);
                slot.MarkPaidActive();
            }
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field, $"Field {fieldName} should exist on {target.GetType().Name}.");
            field.SetValue(target, value);
        }
    }
}
