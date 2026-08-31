using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.MVP;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LandLedgers.EditorTests.Economy
{
    public sealed class GeneralStoreEconomyRepairTests
    {
        [Test]
        public void LandedCostFallsBackToSeventyPercentOfBaseline()
        {
            RetailPricingDefinition pricing = new()
            {
                baselineCents = 100,
                markupMultiplier = 1f
            };

            Assert.AreEqual(70, pricing.GetLandedCostCents());
            Assert.AreEqual(84, pricing.GetCostPlusPriceCents(1.2f));
        }

        [Test]
        public void ExplicitLandedCostIncludesImportCost()
        {
            RetailPricingDefinition pricing = new()
            {
                baselineCents = 100,
                baseWholesaleCostCents = 55,
                importCostCents = 8,
                markupMultiplier = 1.1f
            };

            Assert.AreEqual(63, pricing.GetLandedCostCents());
            Assert.AreEqual(87, pricing.GetCostPlusPriceCents(1.25f));
        }

        [Test]
        public void CostPlusPriceDetectsLocalMarketBandCompression()
        {
            RetailPricingDefinition pricing = new()
            {
                baselineCents = 100,
                baseWholesaleCostCents = 80,
                importCostCents = 10,
                markupMultiplier = 1f,
                localMarketBand = new PriceBand
                {
                    minCents = 50,
                    maxCents = 100
                }
            };

            Assert.AreEqual(100, pricing.GetCostPlusPriceCents(1.5f));
            Assert.IsTrue(pricing.IsMarginCompressedByBand(1.5f));
        }

        [Test]
        public void CategoryStockCanReceivePartialReorders()
        {
            CategoryStockState stock = new("staple_food", 3, 10);

            stock.QueueReorderToTarget();
            int received = stock.ReceivePendingReorderUnits(4);

            Assert.AreEqual(4, received);
            Assert.AreEqual(7, stock.CurrentStockUnits);
            Assert.AreEqual(3, stock.PendingReorderUnits);
        }

        [Test]
        public void ReorderRefreshOnlyQueuesLowStockCategories()
        {
            CategoryStockState lowStock = new("staple_food", 2, 10);
            CategoryStockState healthyStock = new("household_goods", 8, 10);

            lowStock.RefreshPendingReorderToTarget(0.25f);
            healthyStock.QueueReorderToTarget();
            healthyStock.RefreshPendingReorderToTarget(0.25f);

            Assert.AreEqual(8, lowStock.PendingReorderUnits);
            Assert.AreEqual(0, healthyStock.PendingReorderUnits);
        }

        [Test]
        public void StoreStockTextUsesTableLikeCategoryRows()
        {
            GameObject storeObject = new("GeneralStoreRuntimeManager_StockTable");
            try
            {
                GeneralStoreRuntimeManager manager = storeObject.AddComponent<GeneralStoreRuntimeManager>();
                BusinessRuntimeState runtime = new();
                runtime.EnsureCategoryStock("staple_food", 3, 10);
                runtime.EnsureCategoryStock("meat", 1, 8);
                runtime.EnsureCategoryStock("clothing", 5, 8);
                runtime.EnsureCategoryStock("household_goods", 4, 8);
                runtime.EnsureCategoryStock("tools_hardware", 2, 6);
                runtime.EnsureCategoryStock("medicine_remedies", 2, 6);

                SetStoreDefinition(manager, LoadGeneralStoreDefinition());
                SetRuntimeState(manager, runtime);

                string stockText = manager.BuildStoreStockText();

                StringAssert.Contains("Category", stockText);
                StringAssert.Contains("Stock", stockText);
                StringAssert.Contains("Sell", stockText);
                StringAssert.Contains("Cost", stockText);
                StringAssert.Contains("Gross / Unit", stockText);
                StringAssert.Contains("Status", stockText);
                StringAssert.Contains("Staple Food", stockText);
                StringAssert.Contains("Meat", stockText);
                StringAssert.Contains("Clothing", stockText);
                StringAssert.Contains("Household Goods", stockText);
                StringAssert.Contains("Tools & Hardware", stockText);
                StringAssert.Contains("Medicine & Remedies", stockText);
                Assert.False(stockText.Contains("- Staple Food:"), stockText);
            }
            finally
            {
                Object.DestroyImmediate(storeObject);
            }
        }

        [Test]
        public void StorePricingMarginTextReportsMarginAndPosture()
        {
            GameObject storeObject = new("GeneralStoreRuntimeManager_PricingMargin");
            try
            {
                GeneralStoreRuntimeManager manager = storeObject.AddComponent<GeneralStoreRuntimeManager>();
                BusinessRuntimeState runtime = new();
                runtime.EnsureCategoryStock("staple_food", 6, 10);
                runtime.EnsureCategoryStock("meat", 4, 8);

                SetStoreDefinition(manager, LoadGeneralStoreDefinition());
                SetRuntimeState(manager, runtime);

                string text = manager.BuildStorePricingMarginText();

                StringAssert.Contains("Pricing & Margin", text);
                StringAssert.Contains("Price Adjustment", text);
                StringAssert.Contains("Gross Margin", text);
                StringAssert.Contains("Markup Over Cost", text);
                StringAssert.Contains("Avg Sell", text);
                StringAssert.Contains("Avg Cost", text);
                StringAssert.Contains("Gross / Unit", text);
                StringAssert.Contains("Demand Effect", text);
                StringAssert.Contains("Trust Effect", text);
                StringAssert.Contains("Profit Posture", text);
                Assert.False(text.Contains("Store Margin        0%"), text);
            }
            finally
            {
                Object.DestroyImmediate(storeObject);
            }
        }

        [Test]
        public void WeeklyReorderBudgetRespectsReserveAndOperatingBuffer()
        {
            Assert.AreEqual(0, GeneralStoreRuntimeManager.CalculateWeeklyReorderSpendBudgetCents(14000, 8000));
            Assert.AreEqual(5000, GeneralStoreRuntimeManager.CalculateWeeklyReorderSpendBudgetCents(20000, 8000));
            Assert.AreEqual(8000, GeneralStoreRuntimeManager.CalculateWeeklyReorderSpendBudgetCents(25000, 8000));
        }

        [Test]
        public void ImportedWorkerWageUsesStoreSlotBaseline()
        {
            MethodInfo method = typeof(GeneralStoreRuntimeManager).GetMethod(
                "ResolveStoreSlotWageCents",
                BindingFlags.NonPublic | BindingFlags.Static);
            WorkerSlotState slot = new("clerk_helper", "Clerk / Helper", 1200, true);

            Assert.NotNull(method);
            Assert.AreEqual(1200, method.Invoke(null, new object[] { slot }));
        }

        [Test]
        public void StoreMarginAdjustmentIncreasesCategorySellingPriceThroughRuntimePricing()
        {
            GeneralStoreBusinessDefinition definition = LoadGeneralStoreDefinition();
            GameObject gameObject = new("GeneralStoreMarginPricingTest");
            try
            {
                GeneralStoreRuntimeManager manager = gameObject.AddComponent<GeneralStoreRuntimeManager>();
                SetStoreDefinition(manager, definition);

                manager.TrySetStoreMarginAdjustment(0f, out _);
                int basePrice = manager.GetAverageCategorySellingPriceCents("staple_food");

                Assert.IsTrue(manager.TryAdjustStoreMarginSteps(1, out _));
                int raisedPrice = manager.GetAverageCategorySellingPriceCents("staple_food");

                Assert.Greater(raisedPrice, basePrice);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void StoreMarginAdjustmentCannotPushEffectiveMarkupBelowBreakEven()
        {
            float effectiveMarkup = GeneralStoreRuntimeManager.CalculateEffectiveCategoryMarkupMultiplier(1.02f, -0.1f);

            Assert.AreEqual(1f, effectiveMarkup, 0.0001f);
        }

        [Test]
        public void StoreMarginAdjustmentPersistsThroughSaveDto()
        {
            GameObject sourceObject = new("GeneralStoreMarginSaveSourceTest");
            GameObject targetObject = new("GeneralStoreMarginSaveTargetTest");
            try
            {
                GeneralStoreRuntimeManager source = sourceObject.AddComponent<GeneralStoreRuntimeManager>();
                GeneralStoreRuntimeManager target = targetObject.AddComponent<GeneralStoreRuntimeManager>();

                Assert.IsTrue(source.TrySetStoreMarginAdjustment(0.15f, out _));
                LandLedgers.Persistence.GeneralStoreSaveDto dto = source.CaptureSaveDto();
                target.LoadFromSaveDto(dto);

                Assert.AreEqual(0.15f, dto.storeMarginAdjustment01, 0.0001f);
                Assert.AreEqual(0.15f, target.StoreMarginAdjustment01, 0.0001f);
            }
            finally
            {
                Object.DestroyImmediate(sourceObject);
                Object.DestroyImmediate(targetObject);
            }
        }

        [Test]
        public void LocalWholesalePriceKeepsSupplierAboveLandedCost()
        {
            int price = SharedBusinessRuntimeManager.CalculateLocalWholesaleTransferPriceCents(
                sourceLandedCostCents: 100,
                sourceTransferPriceCents: 140,
                storeRetailSellPriceCents: 90);

            Assert.GreaterOrEqual(price, 108);
        }

        [Test]
        public void LocalWholesalePriceStaysBelowStoreRetailWhenMarginExists()
        {
            int price = SharedBusinessRuntimeManager.CalculateLocalWholesaleTransferPriceCents(
                sourceLandedCostCents: 100,
                sourceTransferPriceCents: 140,
                storeRetailSellPriceCents: 180);

            Assert.GreaterOrEqual(price, 108);
            Assert.LessOrEqual(price, 162);
        }

        [Test]
        public void SurvivalReserveCanIncludeLocalIntakeAllowance()
        {
            int reserve = PlayerPortfolioManager.CalculateSurvivalReserveCents(
                protectedReserveCents: 15000,
                filledWeeklyPayrollCents: 1200,
                weeklyOperatingReserveCents: 8000,
                localIntakeAllowanceCents: 4000);

            Assert.AreEqual(29400, reserve);
        }

        [Test]
        public void GeneralStoreProtectedReserveIncludesFragilityBufferEarlyOn()
        {
            GeneralStoreBusinessDefinition definition = LoadGeneralStoreDefinition();
            GameObject gameObject = new("GeneralStoreFragilityReserveTest");
            try
            {
                GeneralStoreRuntimeManager manager = gameObject.AddComponent<GeneralStoreRuntimeManager>();
                BusinessInstanceState business = BusinessInstanceState.Create(
                    "test_general_store_fragility_reserve",
                    definition,
                    1,
                    BusinessOwnerIdentity.Player());
                SetStoreDefinition(manager, definition);
                SetStoreRuntime(manager, business);

                int payroll = business.RuntimeState.FilledWeeklyPayrollCents;
                int reorderReserve = definition.Business.Economy.WeeklyReorderReserveCents;
                int expectedMinimum = PlayerPortfolioManager.CalculateSurvivalReserveCents(
                    GeneralStoreRuntimeManager.MinimumPostReorderCashBufferForLocalSupplyCents,
                    payroll,
                    reorderReserve,
                    manager.EffectiveLocalIntakeAllowanceCents) + 12000;

                Assert.GreaterOrEqual(manager.ProtectedBusinessCashReserveCents, expectedMinimum);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void GeneralStoreSharedSellerNeedsClearerLeadOnStaples()
        {
            MethodInfo method = typeof(GeneralStoreRuntimeManager).GetMethod(
                "GetSharedSellerClearPreferenceMargin01",
                BindingFlags.NonPublic | BindingFlags.Instance);
            GeneralStoreBusinessDefinition definition = LoadGeneralStoreDefinition();
            GameObject gameObject = new("GeneralStorePreferenceMarginTest");
            try
            {
                GeneralStoreRuntimeManager manager = gameObject.AddComponent<GeneralStoreRuntimeManager>();
                SetStoreDefinition(manager, definition);

                Assert.NotNull(method);
                float stapleMargin = (float)method.Invoke(manager, new object[] { "staple_food", 1 });
                float medicineMargin = (float)method.Invoke(manager, new object[] { "medicine_remedies", 1 });
                float clothingMargin = (float)method.Invoke(manager, new object[] { "clothing", 1 });
                float toolsMargin = (float)method.Invoke(manager, new object[] { "tools_hardware", 1 });

                Assert.Greater(stapleMargin, toolsMargin);
                Assert.GreaterOrEqual(stapleMargin, 0.16f);
                Assert.GreaterOrEqual(medicineMargin, 0.16f);
                Assert.GreaterOrEqual(clothingMargin, 0.16f);
                Assert.GreaterOrEqual(toolsMargin, 0.12f);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void GeneralStoreStarterDemandDefaultsAreSurvivable()
        {
            GameObject gameObject = new("GeneralStoreStarterDemandDefaultsTest");
            try
            {
                GeneralStoreRuntimeManager manager = gameObject.AddComponent<GeneralStoreRuntimeManager>();

                Assert.GreaterOrEqual(GetPrivateFloat(manager, "householdDailySpendShare"), 0.42f);
                Assert.GreaterOrEqual(GetPrivateInt(manager, "fallbackHouseholdDailyBudgetCents"), 115);
                Assert.GreaterOrEqual(GetPrivateInt(manager, "weeklyHouseholdAllowanceFallbackCents"), 525);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void GeneralStoreCheapStaplesHaveHealthierLandedCost()
        {
            GeneralStoreBusinessDefinition definition = LoadGeneralStoreDefinition();
            GameObject gameObject = new("GeneralStoreCogHealthTest");
            try
            {
                GeneralStoreRuntimeManager manager = gameObject.AddComponent<GeneralStoreRuntimeManager>();
                SetStoreDefinition(manager, definition);

                Assert.LessOrEqual(manager.GetAverageCategoryLandedCostCents("staple_food"), 20);
                Assert.LessOrEqual(manager.GetAverageCategoryLandedCostCents("household_goods"), 25);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void EstimatedWeeklyNetCashFlowReportsNegativeCashDelta()
        {
            GeneralStoreBusinessDefinition definition = LoadGeneralStoreDefinition();
            GameObject gameObject = new("GeneralStoreNegativeNetTest");
            try
            {
                GeneralStoreRuntimeManager manager = gameObject.AddComponent<GeneralStoreRuntimeManager>();
                BusinessInstanceState business = BusinessInstanceState.Create(
                    "test_general_store_negative_net",
                    definition,
                    1,
                    BusinessOwnerIdentity.Player());
                business.RuntimeState.AddCashCents(1000);
                business.RuntimeState.BeginWeeklySettlementCadence(definition.Business, 0f, false);
                business.RuntimeState.SpendCents(500);
                SetStoreRuntime(manager, business);

                Assert.AreEqual(-500, manager.EstimatedWeeklyNetCashFlowCents);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void RequiredClerkAloneProvidesStarterStoreEfficiency()
        {
            GeneralStoreBusinessDefinition definition = LoadGeneralStoreDefinition();
            GameObject gameObject = new("GeneralStoreRequiredStaffEfficiencyTest");
            try
            {
                GeneralStoreRuntimeManager manager = gameObject.AddComponent<GeneralStoreRuntimeManager>();
                BusinessInstanceState business = BusinessInstanceState.Create(
                    "test_general_store_staffing",
                    definition,
                    1,
                    BusinessOwnerIdentity.Player());
                WorkerSlotState required = business.RuntimeState.WorkerSlots[0];
                required.Assign("clerk", "Clerk", required.WeeklyWageCents);
                required.MarkPaidActive();

                SetStoreDefinition(manager, definition);
                SetStoreRuntime(manager, business);

                MethodInfo efficiencyMethod = typeof(GeneralStoreRuntimeManager).GetMethod(
                    "GetHealthAdjustedOperatingEfficiency01",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                MethodInfo scaleMethod = typeof(GeneralStoreRuntimeManager).GetMethod(
                    "ScaleUnitsByOperatingEfficiency",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                Assert.NotNull(efficiencyMethod);
                Assert.NotNull(scaleMethod);
                Assert.GreaterOrEqual((float)efficiencyMethod.Invoke(manager, null), 0.8f);
                Assert.AreEqual(8, scaleMethod.Invoke(manager, new object[] { 10 }));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void OwnerOperatedFallbackCanCarryStarterStoreThroughLowStaffing()
        {
            GeneralStoreBusinessDefinition definition = LoadGeneralStoreDefinition();
            GameObject gameObject = new("GeneralStoreOwnerFallbackEfficiencyTest");
            try
            {
                GeneralStoreRuntimeManager manager = gameObject.AddComponent<GeneralStoreRuntimeManager>();
                BusinessInstanceState business = BusinessInstanceState.Create(
                    "test_general_store_owner_fallback",
                    definition,
                    1,
                    BusinessOwnerIdentity.Player());
                SetStoreDefinition(manager, definition);
                SetStoreRuntime(manager, business);

                MethodInfo efficiencyMethod = typeof(GeneralStoreRuntimeManager).GetMethod(
                    "GetHealthAdjustedOperatingEfficiency01",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                MethodInfo scaleMethod = typeof(GeneralStoreRuntimeManager).GetMethod(
                    "ScaleUnitsByOperatingEfficiency",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                Assert.NotNull(efficiencyMethod);
                Assert.NotNull(scaleMethod);
                Assert.GreaterOrEqual((float)efficiencyMethod.Invoke(manager, null), 0.62f);
                Assert.AreEqual(6, scaleMethod.Invoke(manager, new object[] { 10 }));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void GeneralStoreReorderUsesSurvivalCategoryPriority()
        {
            GeneralStoreBusinessDefinition definition = LoadGeneralStoreDefinition();
            GameObject gameObject = new("GeneralStoreReorderPriorityTest");
            try
            {
                GeneralStoreRuntimeManager manager = gameObject.AddComponent<GeneralStoreRuntimeManager>();
                BusinessInstanceState business = BusinessInstanceState.Create(
                    "test_general_store_reorder_priority",
                    definition,
                    1,
                    BusinessOwnerIdentity.Player());
                SetStoreDefinition(manager, definition);
                SetStoreRuntime(manager, business);

                MethodInfo method = typeof(GeneralStoreRuntimeManager).GetMethod(
                    "GetSurvivalOrderedReorderCategories",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.NotNull(method);

                System.Collections.Generic.IReadOnlyList<CategoryStockState> categories =
                    (System.Collections.Generic.IReadOnlyList<CategoryStockState>)method.Invoke(manager, null);

                Assert.AreEqual("staple_food", categories[0].CategoryId);
                Assert.AreEqual("meat", categories[1].CategoryId);
                Assert.AreEqual("household_goods", categories[2].CategoryId);
                Assert.AreEqual("medicine_remedies", categories[3].CategoryId);
                Assert.AreEqual("tools_hardware", categories[4].CategoryId);
                Assert.AreEqual("clothing", categories[5].CategoryId);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void ReorderBudgetAllowsEssentialLifelineWhenStoreIsThinAndCashIsLow()
        {
            int budget = GeneralStoreRuntimeManager.CalculateSurvivalAwareReorderSpendBudgetCents(
                availableCashCents: 22000,
                weeklyReorderReserveCents: 8000,
                postReorderCashBufferCents: 25000,
                essentialQueuedCostCents: 2400,
                stockHealth01: 0.18f);

            Assert.AreEqual(2400, budget);
        }

        [Test]
        public void ReorderBudgetPreservesCashWhenStoreIsThinButBelowEmergencyFloor()
        {
            int budget = GeneralStoreRuntimeManager.CalculateSurvivalAwareReorderSpendBudgetCents(
                availableCashCents: 7000,
                weeklyReorderReserveCents: 8000,
                postReorderCashBufferCents: 25000,
                essentialQueuedCostCents: 2400,
                stockHealth01: 0.18f);

            Assert.AreEqual(0, budget);
        }

        [Test]
        public void StoreStockTextReportsSurvivalPrioritiesAndSaturdayReadiness()
        {
            GameObject storeObject = new("GeneralStoreRuntimeManager_StockPressure");
            try
            {
                GeneralStoreRuntimeManager manager = storeObject.AddComponent<GeneralStoreRuntimeManager>();
                BusinessRuntimeState runtime = new();
                runtime.EnsureCategoryStock("staple_food", 1, 10);
                runtime.EnsureCategoryStock("meat", 1, 8);
                runtime.EnsureCategoryStock("clothing", 8, 8);
                runtime.EnsureCategoryStock("household_goods", 2, 8);
                runtime.EnsureCategoryStock("tools_hardware", 6, 6);
                runtime.EnsureCategoryStock("medicine_remedies", 1, 6);

                SetStoreDefinition(manager, LoadGeneralStoreDefinition());
                SetRuntimeState(manager, runtime);

                string stockText = manager.BuildStoreStockText();

                StringAssert.Contains("Survival Priorities", stockText);
                StringAssert.Contains("Saturday Readiness", stockText);
                StringAssert.Contains("staple food", stockText.ToLowerInvariant());
                StringAssert.Contains("medicine", stockText.ToLowerInvariant());
            }
            finally
            {
                Object.DestroyImmediate(storeObject);
            }
        }

        [Test]
        public void ForecastWeightedReorderCanMoveHigherDemandClothingAheadOfTools()
        {
            GeneralStoreBusinessDefinition definition = LoadGeneralStoreDefinition();
            GameObject gameObject = new("GeneralStoreForecastPriorityTest");
            try
            {
                GeneralStoreRuntimeManager manager = gameObject.AddComponent<GeneralStoreRuntimeManager>();
                BusinessInstanceState business = BusinessInstanceState.Create(
                    "test_general_store_forecast_priority",
                    definition,
                    1,
                    BusinessOwnerIdentity.Player());
                SetStoreDefinition(manager, definition);
                SetStoreRuntime(manager, business);

                business.RuntimeState.ResolveDailySalesPlaceholder("clothing", 5, 500);
                business.RuntimeState.ResolveDailySalesPlaceholder("tools_hardware", 1, 100);

                MethodInfo method = typeof(GeneralStoreRuntimeManager).GetMethod(
                    "GetSurvivalOrderedReorderCategories",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.NotNull(method);

                System.Collections.Generic.IReadOnlyList<CategoryStockState> categories =
                    (System.Collections.Generic.IReadOnlyList<CategoryStockState>)method.Invoke(manager, null);

                Assert.Less(IndexOfCategory(categories, "clothing"), IndexOfCategory(categories, "tools_hardware"));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void LowCashTriageLeavesNonEssentialsPendingWhileEssentialsRemainUncovered()
        {
            GeneralStoreBusinessDefinition definition = LoadGeneralStoreDefinition();
            GameObject gameObject = new("GeneralStoreLowCashTriageTest");
            try
            {
                GeneralStoreRuntimeManager manager = gameObject.AddComponent<GeneralStoreRuntimeManager>();
                BusinessInstanceState business = BusinessInstanceState.Create(
                    "test_general_store_low_cash_triage",
                    definition,
                    1,
                    BusinessOwnerIdentity.Player());
                SetStoreDefinition(manager, definition);
                SetStoreRuntime(manager, business);
                SetBusinessCash(business, 9000);

                business.RuntimeState.GetCategoryStock("staple_food").SetCurrentStockForTests(0);
                business.RuntimeState.GetCategoryStock("meat").SetCurrentStockForTests(0);
                business.RuntimeState.GetCategoryStock("household_goods").SetCurrentStockForTests(0);
                business.RuntimeState.GetCategoryStock("medicine_remedies").SetCurrentStockForTests(0);
                business.RuntimeState.GetCategoryStock("clothing").SetCurrentStockForTests(0);

                business.RuntimeState.GetCategoryStock("staple_food").QueueReorderToTarget();
                business.RuntimeState.GetCategoryStock("meat").QueueReorderToTarget();
                business.RuntimeState.GetCategoryStock("household_goods").QueueReorderToTarget();
                business.RuntimeState.GetCategoryStock("medicine_remedies").QueueReorderToTarget();
                business.RuntimeState.GetCategoryStock("clothing").QueueReorderToTarget();

                MethodInfo method = typeof(GeneralStoreRuntimeManager).GetMethod(
                    "ResolveCostedPendingReorders",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.NotNull(method);

                method.Invoke(manager, new object[] { business.RuntimeState.CurrentCashCents });

                Assert.Greater(business.RuntimeState.GetCategoryStock("staple_food").CurrentStockUnits, 0);
                Assert.AreEqual(0, business.RuntimeState.GetCategoryStock("clothing").CurrentStockUnits);
                Assert.Greater(business.RuntimeState.GetCategoryStock("clothing").PendingReorderUnits, 0);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void StoreOverviewReportsPostureAndDemandLossReasons()
        {
            GeneralStoreBusinessDefinition definition = LoadGeneralStoreDefinition();
            GameObject gameObject = new("GeneralStorePostureOverviewTest");
            try
            {
                GeneralStoreRuntimeManager manager = gameObject.AddComponent<GeneralStoreRuntimeManager>();
                BusinessInstanceState business = BusinessInstanceState.Create(
                    "test_general_store_posture_overview",
                    definition,
                    1,
                    BusinessOwnerIdentity.Player());
                SetStoreDefinition(manager, definition);
                SetStoreRuntime(manager, business);
                SetBusinessCash(business, 6000);
                business.RuntimeState.GetCategoryStock("staple_food").SetCurrentStockForTests(0);
                business.RuntimeState.GetCategoryStock("medicine_remedies").SetCurrentStockForTests(0);

                SetPrivateInt(manager, "lastDailyReserveOffMapUnits", 6);
                SetPrivateInt(manager, "lastDailyReserveOffMapLostDemandCents", 180);
                SetPrivateInt(manager, "lastDailyMissedStockUnits", 4);
                SetPrivateInt(manager, "lastDailyMissedServiceUnits", 2);
                SetPrivateInt(manager, "lastDailyMissedPriceUnits", 1);
                SetPrivateInt(manager, "lastWeeklyCashConstrainedReorderUnits", 8);

                string overview = manager.BuildStoreOverviewText();

                StringAssert.Contains("Posture", overview);
                StringAssert.Contains("Demand Loss", overview);
                StringAssert.Contains("survival buying only", overview.ToLowerInvariant());
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        private static GeneralStoreBusinessDefinition LoadGeneralStoreDefinition()
        {
            GeneralStoreBusinessDefinition definition = AssetDatabase.LoadAssetAtPath<GeneralStoreBusinessDefinition>(
                "Assets/Core/Economy/GeneralStoreBusinessDefinition.asset");
            Assert.NotNull(definition);
            return definition;
        }

        private static void SetStoreDefinition(GeneralStoreRuntimeManager manager, GeneralStoreBusinessDefinition definition)
        {
            FieldInfo field = typeof(GeneralStoreRuntimeManager).GetField(
                "storeDefinition",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field);
            field.SetValue(manager, definition);
        }

        private static void SetStoreRuntime(GeneralStoreRuntimeManager manager, BusinessInstanceState business)
        {
            FieldInfo businessField = typeof(GeneralStoreRuntimeManager).GetField(
                "currentBusiness",
                BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo runtimeField = typeof(GeneralStoreRuntimeManager).GetField(
                "runtimeState",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(businessField);
            Assert.NotNull(runtimeField);
            businessField.SetValue(manager, business);
            runtimeField.SetValue(manager, business.RuntimeState);
        }

        private static void SetRuntimeState(GeneralStoreRuntimeManager manager, BusinessRuntimeState runtime)
        {
            FieldInfo runtimeField = typeof(GeneralStoreRuntimeManager).GetField(
                "runtimeState",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(runtimeField);
            runtimeField.SetValue(manager, runtime);
        }

        private static void SetBusinessCash(BusinessInstanceState business, int cashCents)
        {
            int target = Mathf.Max(0, cashCents);
            int current = business.RuntimeState.CurrentCashCents;
            if (current > target)
            {
                business.RuntimeState.SpendCents(current - target);
            }
            else if (target > current)
            {
                business.RuntimeState.AddCashCents(target - current);
            }
        }

        private static int IndexOfCategory(System.Collections.Generic.IReadOnlyList<CategoryStockState> categories, string categoryId)
        {
            for (int i = 0; i < categories.Count; i++)
            {
                if (categories[i] != null && categories[i].CategoryId == categoryId)
                {
                    return i;
                }
            }

            return -1;
        }

        private static void SetPrivateInt(GeneralStoreRuntimeManager manager, string fieldName, int value)
        {
            FieldInfo field = typeof(GeneralStoreRuntimeManager).GetField(
                fieldName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field);
            field.SetValue(manager, value);
        }

        private static float GetPrivateFloat(GeneralStoreRuntimeManager manager, string fieldName)
        {
            FieldInfo field = typeof(GeneralStoreRuntimeManager).GetField(
                fieldName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field);
            return (float)field.GetValue(manager);
        }

        private static int GetPrivateInt(GeneralStoreRuntimeManager manager, string fieldName)
        {
            FieldInfo field = typeof(GeneralStoreRuntimeManager).GetField(
                fieldName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field);
            return (int)field.GetValue(manager);
        }
    }
}
