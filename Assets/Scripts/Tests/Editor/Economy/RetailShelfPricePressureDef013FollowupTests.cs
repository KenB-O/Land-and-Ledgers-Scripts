using System;
using LandLedgers.Economy;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// DEF-013 follow-up (Decision 9): the retail shelf prices use the same
    /// supply/demand pressure resolution as the transfer price. The helper
    /// is pinned here in the harness; the two manager call sites that use
    /// it (GeneralStoreRuntimeManager.GetItemSellingPriceCents and
    /// GetAverageCategoryUnitPrice) cannot compile in the Roslyn harness
    /// (Agent 3 note 67) and are pinned by Unity-only tests in
    /// GeneralStoreRuntimeManagerTests.cs instead.
    /// </summary>
    [TestFixture]
    public sealed class RetailShelfPricePressureDef013FollowupTests
    {
        private static BusinessRuntimeState RuntimeWith(string categoryId, int current, int target)
        {
            BusinessRuntimeState runtime = new BusinessRuntimeState();
            runtime.EnsureCategoryStock(categoryId, current, target);
            return runtime;
        }

        private static void AssertPressure(float expected, float actual)
        {
            Assert.IsTrue(Math.Abs(expected - actual) < 0.001f, $"expected pressure {expected}, got {actual}");
        }

        [Test]
        public void Helper_NullRuntime_ReturnsZeroPressure()
        {
            AssertPressure(0f, RetailPricingDefinition.ResolveCategorySupplyDemandPressure01(null, "staple_food"));
        }

        [Test]
        public void Helper_CategoryStock_MapsHealthToPressure()
        {
            AssertPressure(1f, RetailPricingDefinition.ResolveCategorySupplyDemandPressure01(
                RuntimeWith("staple_food", 0, 100), "staple_food"));
            AssertPressure(0f, RetailPricingDefinition.ResolveCategorySupplyDemandPressure01(
                RuntimeWith("staple_food", 50, 100), "staple_food"));
            AssertPressure(-1f, RetailPricingDefinition.ResolveCategorySupplyDemandPressure01(
                RuntimeWith("staple_food", 100, 100), "staple_food"));
        }

        [Test]
        public void Helper_UnknownCategory_FallsBackToBusinessWideHealth()
        {
            BusinessRuntimeState empty = RuntimeWith("other", 0, 100);
            AssertPressure(1f, RetailPricingDefinition.ResolveCategorySupplyDemandPressure01(empty, "staple_food"));

            BusinessRuntimeState full = RuntimeWith("other", 100, 100);
            AssertPressure(-1f, RetailPricingDefinition.ResolveCategorySupplyDemandPressure01(full, "staple_food"));

            BusinessRuntimeState mixed = RuntimeWith("other", 0, 100);
            mixed.EnsureCategoryStock("another", 100, 100);
            AssertPressure(0f, RetailPricingDefinition.ResolveCategorySupplyDemandPressure01(mixed, "staple_food"));
        }

        [Test]
        public void Helper_UntrackedCategoryTargetZero_HasFullHealth()
        {
            BusinessRuntimeState runtime = RuntimeWith("staple_food", 5, 0);
            AssertPressure(-1f, RetailPricingDefinition.ResolveCategorySupplyDemandPressure01(runtime, "staple_food"));
        }

        [Test]
        public void Helper_CategoryStock_WinsOverBusinessWideHealth()
        {
            BusinessRuntimeState runtime = RuntimeWith("staple_food", 0, 100);
            runtime.EnsureCategoryStock("other", 100, 100);
            Assert.AreEqual(0.5f, runtime.StockHealth01, 0.001f);
            AssertPressure(1f, RetailPricingDefinition.ResolveCategorySupplyDemandPressure01(runtime, "staple_food"));
        }

        private static RetailPricingDefinition Pricing() => new RetailPricingDefinition
        {
            baselineCents = 100,
            baseWholesaleCostCents = 50,
            importCostCents = 0,
            localMarketBand = new PriceBand { minCents = 10, maxCents = 200 },
            markupMultiplier = 1f,
            supplyDemandSensitivity = 0.5f,
        };

        [Test]
        public void ShelfPrice_RisesWhenStockIsLow_AndFallsWhenStockIsFull()
        {
            // The exact composition the manager now performs: the helper
            // resolves the pressure from live runtime stock, and the
            // resolved pressure feeds GetCostPlusPriceCents.
            RetailPricingDefinition pricing = Pricing();

            float lowPressure = RetailPricingDefinition.ResolveCategorySupplyDemandPressure01(
                RuntimeWith("staple_food", 0, 100), "staple_food");
            float halfPressure = RetailPricingDefinition.ResolveCategorySupplyDemandPressure01(
                RuntimeWith("staple_food", 50, 100), "staple_food");
            float fullPressure = RetailPricingDefinition.ResolveCategorySupplyDemandPressure01(
                RuntimeWith("staple_food", 100, 100), "staple_food");

            Assert.AreEqual(75, pricing.GetCostPlusPriceCents(1f, lowPressure));
            Assert.AreEqual(50, pricing.GetCostPlusPriceCents(1f, halfPressure));
            Assert.AreEqual(25, pricing.GetCostPlusPriceCents(1f, fullPressure));
        }

        [Test]
        public void ShelfPrice_QuoteEqualsCharge_ForTheSameStockState()
        {
            // Exact money: the charged price is the quoted price. Pressure
            // changes the price; it never creates money. The same runtime
            // and pricing resolve the same unit price on repeat calls, and
            // a multi-unit total is exactly units times the unit price.
            RetailPricingDefinition pricing = Pricing();
            BusinessRuntimeState runtime = RuntimeWith("staple_food", 0, 100);

            float pressure = RetailPricingDefinition.ResolveCategorySupplyDemandPressure01(runtime, "staple_food");
            int quoted = pricing.GetCostPlusPriceCents(1f, pressure);
            int charged = pricing.GetCostPlusPriceCents(1f,
                RetailPricingDefinition.ResolveCategorySupplyDemandPressure01(runtime, "staple_food"));

            Assert.AreEqual(75, quoted);
            Assert.AreEqual(quoted, charged);
            Assert.AreEqual(225, 3 * quoted);
        }
    }
}
