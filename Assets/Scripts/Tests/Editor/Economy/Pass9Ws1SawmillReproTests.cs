using LandLedgers.Economy;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// A4-14 regression suite. REPRO HISTORY (pre-fix, this file): a test
    /// replicating the linked-node branch of
    /// SharedBusinessRuntimeManager.RecordSawmillConstructionLumberSale step
    /// for step failed with "revenue 2000c credited but only 0 units were
    /// deducted (mirroredConsumed claimed 20)" — support stock 5, request 20,
    /// sync copies stock in (deducts nothing), mirroredConsumed was set to
    /// the full request and revenue credited on all 20 units. CONFIRMED.
    /// The fix routes both branches through
    /// BusinessRuntimeState.SettleCategoryStockSale, which consumes first
    /// and credits revenue only for units actually deducted; the manager
    /// re-syncs the support mirror from the business after the sale.
    /// </summary>
    [TestFixture]
    public sealed class Pass9Ws1SawmillSaleTests
    {
        private static BusinessRuntimeState SawmillWithLumber(int stockUnits)
        {
            var runtime = new BusinessRuntimeState();
            runtime.BindBusinessIdentity("sawmill-ws1");
            runtime.SetCurrentCashCents(0);
            runtime.EnsureCategoryStock("lumber", stockUnits, 100);
            return runtime;
        }

        [Test]
        public void SawmillSale_PartialStock_CreditsRevenueOnlyForDeductedUnits()
        {
            // The repro scenario: 5 on hand, 20 requested at 100c.
            BusinessRuntimeState runtime = SawmillWithLumber(5);

            int consumed = runtime.SettleCategoryStockSale("lumber", 20, 100, "sold lumber to construction");

            Assert.AreEqual(5, consumed, "Only the 5 units on hand can be sold.");
            Assert.AreEqual(500, runtime.CurrentCashCents,
                "Revenue posts for deducted units only: 5 x 100c, not 20 x 100c.");
            Assert.AreEqual(0, runtime.GetCategoryStock("lumber").CurrentStockUnits,
                "The sold units were actually deducted.");
        }

        [Test]
        public void SawmillSale_FullStock_CreditsFullRevenue()
        {
            BusinessRuntimeState runtime = SawmillWithLumber(30);

            int consumed = runtime.SettleCategoryStockSale("lumber", 20, 100, "sold lumber to construction");

            Assert.AreEqual(20, consumed);
            Assert.AreEqual(2000, runtime.CurrentCashCents);
            Assert.AreEqual(10, runtime.GetCategoryStock("lumber").CurrentStockUnits);
        }

        [Test]
        public void SawmillSale_NoStock_CreditsNothing()
        {
            BusinessRuntimeState runtime = SawmillWithLumber(0);

            int consumed = runtime.SettleCategoryStockSale("lumber", 20, 100, "sold lumber to construction");

            Assert.AreEqual(0, consumed);
            Assert.AreEqual(0, runtime.CurrentCashCents, "No stock, no sale, no revenue.");
        }
    }
}
