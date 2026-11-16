using System;
using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.FirstLedger;
using LandLedgers.Persistence;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>Plan 20, T3-T5 (Agent 3 note 67 ruling (1)): Unity-only tests
    /// for TryGetServiceClerkPersonId (GeneralStoreRuntimeManager.cs:434-468,
    /// the IsPaidActive clerk gate, Plan 20 item (b), and the TryParse-continue
    /// nit, item (c)).
    ///
    /// HARNESS-EXCLUDED, Unity only: GeneralStoreRuntimeManager.cs cannot
    /// compile in the Roslyn harness (21 CS0246 errors across 12 unshimmable
    /// scene-runtime types; reproduced by Agent 3, note 67). This file is
    /// listed with a "!" line in the shared sources.txt and must never be
    /// added to the harness. Run in Unity (Codex/Sol); until that run exists
    /// the (b)/(c) behavior stands as "source inspection only" (note 67
    /// ruling (3)).
    ///
    /// The manager is constructed via AddComponent + reflection, following
    /// GeneralStoreEconomyRepairTests.cs. No scene, store definition,
    /// population, or time manager is needed: the method under test reads
    /// only runtimeState.WorkerSlots. Specs preserved from the Plan 20
    /// checkpoint (Agent 1 session, T3-T5 bodies).</summary>
    public sealed class GeneralStoreRuntimeManagerTests
    {
        [Test]
        public void SuspendedClerk_IsSkipped()
        {
            GameObject root = new("GeneralStoreRuntimeManager_SuspendedClerk");
            try
            {
                GeneralStoreRuntimeManager manager = NewManager(root);
                SetSlots(manager,
                    ClerkSlot("3", paidActive: true, suspended: true),
                    ClerkSlot("4", paidActive: true, suspended: false));
                Assert.IsTrue(manager.TryGetServiceClerkPersonId(out int personId));
                Assert.AreEqual(4, personId);

                SetSlots(manager,
                    ClerkSlot("3", paidActive: true, suspended: true));
                Assert.IsFalse(manager.TryGetServiceClerkPersonId(out _));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void UnpaidClerk_IsSkipped_UnderIsPaidActiveRule()
        {
            GameObject root = new("GeneralStoreRuntimeManager_UnpaidClerk");
            try
            {
                GeneralStoreRuntimeManager manager = NewManager(root);
                SetSlots(manager,
                    ClerkSlot("3", paidActive: false, suspended: false));
                Assert.IsFalse(manager.TryGetServiceClerkPersonId(out _));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void UnparseableClerkId_IsSkipped_NextSlotReturned()
        {
            GameObject root = new("GeneralStoreRuntimeManager_UnparseableClerkId");
            try
            {
                GeneralStoreRuntimeManager manager = NewManager(root);
                SetSlots(manager,
                    ClerkSlot("not-a-number", paidActive: true, suspended: false),
                    ClerkSlot("4", paidActive: true, suspended: false));
                Assert.IsTrue(manager.TryGetServiceClerkPersonId(out int personId));
                Assert.AreEqual(4, personId);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void OwnerClerkSlot_ResolvesToOwnerPersonId()
        {
            // CAMPAIGN-B (DEF-042): the NPC store's owner-occupied clerk
            // slot ("owner:1", written by TryAssignOwnerOperator) resolves
            // to the owner — before the fix int.TryParse failed and the
            // slot was skipped, so the store had no usable clerk.
            GameObject root = new("GeneralStoreRuntimeManager_OwnerClerk");
            try
            {
                GeneralStoreRuntimeManager manager = NewManager(root);
                SetSlots(manager,
                    ClerkSlot("owner:1", paidActive: true, suspended: false));
                Assert.IsTrue(manager.TryGetServiceClerkPersonId(out int personId));
                Assert.AreEqual(1, personId);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void OwnerClerkSlotByDisplayName_IsSkipped()
        {
            // "owner:{name}" has no person id; it is skipped, not invented.
            GameObject root = new("GeneralStoreRuntimeManager_OwnerClerkByName");
            try
            {
                GeneralStoreRuntimeManager manager = NewManager(root);
                SetSlots(manager,
                    ClerkSlot("owner:Emma", paidActive: true, suspended: false),
                    ClerkSlot("4", paidActive: true, suspended: false));
                Assert.IsTrue(manager.TryGetServiceClerkPersonId(out int personId));
                Assert.AreEqual(4, personId);

                SetSlots(manager,
                    ClerkSlot("owner:Emma", paidActive: true, suspended: false));
                Assert.IsFalse(manager.TryGetServiceClerkPersonId(out _));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void OwnerClerkSlot_ResolvesThroughStateOverload()
        {
            // CAMPAIGN-B (DEF-042): the state overload serves a store other
            // than the manager's own (the dev NPC store) from its own slots.
            GameObject root = new("GeneralStoreRuntimeManager_OwnerClerkOverload");
            try
            {
                GeneralStoreRuntimeManager manager = NewManager(root);
                BusinessRuntimeState npcState = new BusinessRuntimeState();
                FieldInfo slotsField = typeof(BusinessRuntimeState).GetField(
                    "workerSlots", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.NotNull(slotsField, "BusinessRuntimeState.workerSlots field missing");
                slotsField.SetValue(npcState, new List<WorkerSlotState>(
                    new[] { ClerkSlot("owner:1", paidActive: true, suspended: false) }));
                Assert.IsTrue(manager.TryGetServiceClerkPersonId(npcState, out int personId));
                Assert.AreEqual(1, personId);
                Assert.IsFalse(manager.TryGetServiceClerkPersonId(null, out _));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ShelfPrice_Flour_RisesWhenStockIsLow_AndFallsWhenStockIsFull()
        {
            // DEF-013 follow-up (Decision 9): the retail shelf price uses
            // the same supply/demand pressure as the transfer price.
            // Authored flour: landed 20c, sensitivity 0.35, band [28, 45],
            // staple_food markup 1.45 (no current business is set, so the
            // policy margin adjustment is 0). Empty stock (pressure +1)
            // prices 39c, half stock (pressure 0) 29c, full stock
            // (pressure -1) 28c (band floor). Bread is deliberately not
            // used: its price is pinned at the 5c floor across the whole
            // pressure range.
            GeneralStoreBusinessDefinition definition = AssetDatabase.LoadAssetAtPath<GeneralStoreBusinessDefinition>(DefinitionPath);
            Assert.NotNull(definition, "Authored General Store definition missing");
            GameObject root = new("GeneralStoreRuntimeManager_ShelfPriceFlour");
            try
            {
                GeneralStoreRuntimeManager manager = NewManager(root);
                BusinessInstanceState business = BusinessInstanceState.Create(
                    "shelf-price-flour", definition, 60, BusinessOwnerIdentity.Npc(1, "Test owner", "Test"));
                SetStore(manager, definition, business.RuntimeState);
                CategoryStockState stock = business.RuntimeState.GetCategoryStock("staple_food");
                Assert.NotNull(stock, "staple_food stock state missing");
                Assert.Greater(stock.TargetStockUnits, 10, "staple_food target too small for a half-stock pin");

                stock.SetCurrentStockForTests(0);
                Assert.AreEqual(39, manager.GetItemSellingPriceCents("flour"));
                Assert.AreEqual(OracleFlourPrice(definition, stock), manager.GetItemSellingPriceCents("flour"));

                stock.SetCurrentStockForTests(stock.TargetStockUnits / 2);
                Assert.AreEqual(29, manager.GetItemSellingPriceCents("flour"));
                Assert.AreEqual(OracleFlourPrice(definition, stock), manager.GetItemSellingPriceCents("flour"));

                stock.SetCurrentStockForTests(stock.TargetStockUnits);
                Assert.AreEqual(28, manager.GetItemSellingPriceCents("flour"));
                Assert.AreEqual(OracleFlourPrice(definition, stock), manager.GetItemSellingPriceCents("flour"));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ShelfPrice_CategoryAverage_MovesWithStock()
        {
            // The category-average shelf price equals the oracle average
            // (each item priced at the resolved pressure with the 1.45
            // category markup) at empty, half, and full stock, and falls
            // strictly as stock rises. If the manager still passed 0f,
            // all three levels would equal the half-stock average.
            GeneralStoreBusinessDefinition definition = AssetDatabase.LoadAssetAtPath<GeneralStoreBusinessDefinition>(DefinitionPath);
            Assert.NotNull(definition, "Authored General Store definition missing");
            GameObject root = new("GeneralStoreRuntimeManager_ShelfPriceAverage");
            try
            {
                GeneralStoreRuntimeManager manager = NewManager(root);
                BusinessInstanceState business = BusinessInstanceState.Create(
                    "shelf-price-average", definition, 60, BusinessOwnerIdentity.Npc(1, "Test owner", "Test"));
                SetStore(manager, definition, business.RuntimeState);
                CategoryStockState stock = business.RuntimeState.GetCategoryStock("staple_food");
                Assert.NotNull(stock, "staple_food stock state missing");

                stock.SetCurrentStockForTests(0);
                int emptyAverage = manager.GetAverageCategorySellingPriceCents("staple_food");
                Assert.AreEqual(OracleStapleFoodAverage(definition, stock), emptyAverage);

                stock.SetCurrentStockForTests(stock.TargetStockUnits / 2);
                int halfAverage = manager.GetAverageCategorySellingPriceCents("staple_food");
                Assert.AreEqual(OracleStapleFoodAverage(definition, stock), halfAverage);

                stock.SetCurrentStockForTests(stock.TargetStockUnits);
                int fullAverage = manager.GetAverageCategorySellingPriceCents("staple_food");
                Assert.AreEqual(OracleStapleFoodAverage(definition, stock), fullAverage);

                Assert.IsTrue(emptyAverage > halfAverage, $"empty {emptyAverage} should exceed half {halfAverage}");
                Assert.IsTrue(halfAverage > fullAverage, $"half {halfAverage} should exceed full {fullAverage}");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ShelfPrice_UnboundManager_ReturnsNoPressurePrice()
        {
            // A manager asked for a price before it is bound to a runtime
            // keeps today's behaviour exactly: the flour price is the
            // no-pressure price.
            GeneralStoreBusinessDefinition definition = AssetDatabase.LoadAssetAtPath<GeneralStoreBusinessDefinition>(DefinitionPath);
            Assert.NotNull(definition, "Authored General Store definition missing");
            GameObject root = new("GeneralStoreRuntimeManager_ShelfPriceUnbound");
            try
            {
                GeneralStoreRuntimeManager manager = NewManager(root);
                FieldInfo definitionField = typeof(GeneralStoreRuntimeManager).GetField(
                    "storeDefinition", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.NotNull(definitionField, "GeneralStoreRuntimeManager.storeDefinition field missing");
                definitionField.SetValue(manager, definition);
                Assert.AreEqual(29, manager.GetItemSellingPriceCents("flour"));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void BandCheck_LowStock_ReportsCappedWhenPressureAdjustedPriceHitsBand()
        {
            // Checkpoint-58 follow-up (Decision 9): the "capped" flag must
            // evaluate the pressure-adjusted price actually charged, not
            // the no-pressure price. Store margin +0.30, so the
            // staple_food markup is 1.75. At empty stock the pressure is
            // +1 and tea prices at round(42 x 1.75 x 1.3) = 96, over its
            // band max of 80; at no pressure it is round(42 x 1.75) = 74,
            // inside the band. The old 0f evaluation under-reports
            // "capped" here. The oracle proves the test discriminates: it
            // reports capped at the fixed pressure and not at 0f.
            GeneralStoreBusinessDefinition definition = AssetDatabase.LoadAssetAtPath<GeneralStoreBusinessDefinition>(DefinitionPath);
            Assert.NotNull(definition, "Authored General Store definition missing");
            GameObject root = new("GeneralStoreRuntimeManager_BandCheckLowStock");
            try
            {
                GeneralStoreRuntimeManager manager = NewManager(root);
                BusinessInstanceState business = BusinessInstanceState.Create(
                    "band-check-low-stock", definition, 60, BusinessOwnerIdentity.Npc(1, "Test owner", "Test"));
                SetStore(manager, definition, business.RuntimeState);
                SetStoreMarginAdjustment(manager, 0.30f);
                SetAllCategoryStock(business.RuntimeState, _ => 0);

                Assert.IsTrue(OracleAnyCompressed(definition, business.RuntimeState, 0.30f, null),
                    "Oracle: some item should compress at the fixed pressure");
                Assert.IsFalse(OracleAnyCompressed(definition, business.RuntimeState, 0.30f, 0f),
                    "Oracle: no item should compress at no pressure, or the test cannot discriminate");
                string line = BuildStorePricingLineViaReflection(manager);
                Assert.IsTrue(line.Contains("some prices capped"), $"Expected capped flag at empty stock: {line}");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void BandCheck_FullStock_DoesNotOverReportCapped()
        {
            // Store margin +0.50, so the staple_food markup is 1.95. At
            // full stock the pressure is -1 and tea prices at round(42 x
            // 1.95 x 0.7) = 57, inside its band; at no pressure it is
            // round(42 x 1.95) = 82, over the band max of 80. The old 0f
            // evaluation over-reports "capped" here. The oracle proves
            // the test discriminates: it reports not capped at the fixed
            // pressure and capped at 0f.
            GeneralStoreBusinessDefinition definition = AssetDatabase.LoadAssetAtPath<GeneralStoreBusinessDefinition>(DefinitionPath);
            Assert.NotNull(definition, "Authored General Store definition missing");
            GameObject root = new("GeneralStoreRuntimeManager_BandCheckFullStock");
            try
            {
                GeneralStoreRuntimeManager manager = NewManager(root);
                BusinessInstanceState business = BusinessInstanceState.Create(
                    "band-check-full-stock", definition, 60, BusinessOwnerIdentity.Npc(1, "Test owner", "Test"));
                SetStore(manager, definition, business.RuntimeState);
                SetStoreMarginAdjustment(manager, 0.50f);
                SetAllCategoryStock(business.RuntimeState, target => target);

                Assert.IsFalse(OracleAnyCompressed(definition, business.RuntimeState, 0.50f, null),
                    "Oracle: no item should compress at the fixed pressure");
                Assert.IsTrue(OracleAnyCompressed(definition, business.RuntimeState, 0.50f, 0f),
                    "Oracle: some item should compress at no pressure, or the test cannot discriminate");
                string line = BuildStorePricingLineViaReflection(manager);
                Assert.IsFalse(line.Contains("some prices capped"), $"Expected no capped flag at full stock: {line}");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void BandCheck_HalfStock_ReportsCappedWhenBandGenuinelyHit()
        {
            // Control: store margin +0.50, all categories at half stock,
            // so the pressure is 0 and tea prices at round(42 x 1.95) =
            // 82, over its band max of 80. The flag still fires when the
            // band is genuinely hit — the fix must not silence it.
            GeneralStoreBusinessDefinition definition = AssetDatabase.LoadAssetAtPath<GeneralStoreBusinessDefinition>(DefinitionPath);
            Assert.NotNull(definition, "Authored General Store definition missing");
            GameObject root = new("GeneralStoreRuntimeManager_BandCheckHalfStock");
            try
            {
                GeneralStoreRuntimeManager manager = NewManager(root);
                BusinessInstanceState business = BusinessInstanceState.Create(
                    "band-check-half-stock", definition, 60, BusinessOwnerIdentity.Npc(1, "Test owner", "Test"));
                SetStore(manager, definition, business.RuntimeState);
                SetStoreMarginAdjustment(manager, 0.50f);
                SetAllCategoryStock(business.RuntimeState, target => target / 2);

                Assert.IsTrue(OracleAnyCompressed(definition, business.RuntimeState, 0.50f, null),
                    "Oracle: some item should compress at half stock");
                string line = BuildStorePricingLineViaReflection(manager);
                Assert.IsTrue(line.Contains("some prices capped"), $"Expected capped flag at half stock: {line}");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private const string DefinitionPath = "Assets/Core/Economy/GeneralStoreBusinessDefinition.asset";

        private static void SetStore(GeneralStoreRuntimeManager manager, GeneralStoreBusinessDefinition definition, BusinessRuntimeState state)
        {
            FieldInfo definitionField = typeof(GeneralStoreRuntimeManager).GetField(
                "storeDefinition", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(definitionField, "GeneralStoreRuntimeManager.storeDefinition field missing");
            definitionField.SetValue(manager, definition);
            FieldInfo stateField = typeof(GeneralStoreRuntimeManager).GetField(
                "runtimeState", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(stateField, "GeneralStoreRuntimeManager.runtimeState field missing");
            stateField.SetValue(manager, state);
        }

        private static string BuildStorePricingLineViaReflection(GeneralStoreRuntimeManager manager)
        {
            // Checkpoint-60 follow-up: BuildStorePricingLine is private,
            // so a direct call is CS0122 in Unity. Invoke it through
            // reflection, following this file's SetStore /
            // SetStoreMarginAdjustment pattern.
            MethodInfo method = typeof(GeneralStoreRuntimeManager).GetMethod(
                "BuildStorePricingLine", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method, "GeneralStoreRuntimeManager.BuildStorePricingLine method missing");
            return (string)method.Invoke(manager, null);
        }

        private static void SetStoreMarginAdjustment(GeneralStoreRuntimeManager manager, float adjustment)
        {
            FieldInfo adjustmentField = typeof(GeneralStoreRuntimeManager).GetField(
                "storeMarginAdjustment01", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(adjustmentField, "GeneralStoreRuntimeManager.storeMarginAdjustment01 field missing");
            adjustmentField.SetValue(manager, adjustment);
        }

        private static void SetAllCategoryStock(BusinessRuntimeState state, Func<int, int> levelForTarget)
        {
            for (int i = 0; i < state.CategoryStock.Count; i++)
            {
                CategoryStockState stock = state.CategoryStock[i];
                stock.SetCurrentStockForTests(levelForTarget(stock.TargetStockUnits));
            }
        }

        private static bool OracleAnyCompressed(GeneralStoreBusinessDefinition definition, BusinessRuntimeState state, float marginAdjustment, float? forcedPressure)
        {
            // Mirrors IsCategoryPriceCompressedByBand over every authored
            // item: the category markup is the definition default plus
            // the store margin adjustment, and the pressure is either
            // forced (to prove the test discriminates) or resolved from
            // live stock exactly as the manager resolves it.
            foreach (ItemDefinition item in definition.Items)
            {
                if (item == null)
                {
                    continue;
                }

                ItemCategoryDefinition category = definition.FindCategory(item.CategoryId);
                // Checkpoint-60 follow-up (footnote 3): the missing-category
                // fallback mirrors production GetCategoryMarkupMultiplier,
                // which falls back to the definition's default retail
                // markup (authored 1.4), not 1f.
                float categoryMarkup = category != null ? category.DefaultMarkupMultiplier : definition.Business.Economy.DefaultRetailMarkupMultiplier;
                float markup = Mathf.Max(1f, categoryMarkup + marginAdjustment);
                float pressure = forcedPressure ?? RetailPricingDefinition.ResolveCategorySupplyDemandPressure01(state, item.CategoryId);
                if (item.Pricing.IsMarginCompressedByBand(markup, pressure))
                {
                    return true;
                }
            }

            return false;
        }

        private static int OracleFlourPrice(GeneralStoreBusinessDefinition definition, CategoryStockState stock)
        {
            foreach (ItemDefinition item in definition.Items)
            {
                if (item != null && string.Equals(item.ItemId, "flour", StringComparison.OrdinalIgnoreCase))
                {
                    float pressure = RetailPricingDefinition.ResolveSupplyDemandPressure01(stock.StockHealth01);
                    return Mathf.Max(5, item.Pricing.GetCostPlusPriceCents(1.45f, pressure));
                }
            }

            Assert.Fail("Authored flour item missing from the General Store definition.");
            return 0;
        }

        private static int OracleStapleFoodAverage(GeneralStoreBusinessDefinition definition, CategoryStockState stock)
        {
            float pressure = RetailPricingDefinition.ResolveSupplyDemandPressure01(stock.StockHealth01);
            int total = 0;
            int count = 0;
            foreach (ItemDefinition item in definition.Items)
            {
                if (item == null || !string.Equals(item.CategoryId, "staple_food", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                total += item.Pricing.GetCostPlusPriceCents(1.45f, pressure);
                count++;
            }

            return count == 0 ? 5 : Mathf.Max(5, total / count);
        }

        private static GeneralStoreRuntimeManager NewManager(GameObject root)
        {
            GeneralStoreRuntimeManager manager = root.AddComponent<GeneralStoreRuntimeManager>();
            Assert.NotNull(manager);
            return manager;
        }

        private static void SetSlots(GeneralStoreRuntimeManager manager, params WorkerSlotState[] slots)
        {
            BusinessRuntimeState state = new BusinessRuntimeState();
            FieldInfo slotsField = typeof(BusinessRuntimeState).GetField(
                "workerSlots", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(slotsField, "BusinessRuntimeState.workerSlots field missing");
            slotsField.SetValue(state, new List<WorkerSlotState>(slots));
            FieldInfo stateField = typeof(GeneralStoreRuntimeManager).GetField(
                "runtimeState", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(stateField, "GeneralStoreRuntimeManager.runtimeState field missing");
            stateField.SetValue(manager, state);
        }

        private static WorkerSlotState ClerkSlot(string workerId, bool paidActive, bool suspended)
        {
            return WorkerSlotState.FromSaveDto(new WorkerSlotSaveDto
            {
                slotId = "clerk_helper",
                slotDisplayName = "clerk_helper",
                assignedWorkerId = workerId,
                assignedWorkerDisplayName = workerId,
                weeklyWageCents = 100,
                requiredForOpening = false,
                paidActive = paidActive,
                suspendedForMissedPayroll = suspended,
            });
        }
    }
}
