using LandLedgers.Economy;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Pass XII Worker C follow-up E-DEFECT-01: ResolveSettledSale must not
    /// credit revenue for goods it did not deliver. Reproduction first:
    /// on Pass XI code a mill with flour stock 0 settles a 20-unit sale
    /// for 1100c, returns true, and adds 1100c to the till while
    /// delivering 0 units (CategoryStockState.RecordDailySales clamps
    /// the stock decrement, but the cash credit is not clamped). The
    /// same defect fires on partial stock: stock 5, sale 20 credits
    /// the full 20-unit revenue while delivering 5.
    ///
    /// Control cases that must keep passing: a sale covered by stock
    /// settles exactly once, and the pre-existing zero-unit shape
    /// (ExternalTradeTests: 0 units, revenue, named buyer) is preserved
    /// — the fix refuses only when stock &lt; units sold, so a 0-unit
    /// sale still settles. No existing test expectation is changed here.
    /// </summary>
    [TestFixture]
    public sealed class Pass12CSettledSaleStockTests
    {
        private static BusinessRuntimeState MillWithFlour(int stockUnits, int cashCents)
        {
            var mill = new BusinessRuntimeState();
            mill.BindBusinessIdentity("mill-1");
            mill.SetCurrentCashCents(cashCents);
            mill.EnsureCategoryStock("flour", stockUnits, 200);
            return mill;
        }

        [Test]
        public void SettledSale_ZeroStock_RefusesWithNoMutation()
        {
            BusinessRuntimeState mill = MillWithFlour(0, 5000);

            bool settled = mill.ResolveSettledSale("flour", 20, 1100, "bakery-1");

            Assert.IsFalse(settled, "no stock means no delivery, so no sale and no revenue.");
            Assert.AreEqual(5000, mill.CurrentCashCents, "till must not move on a refused sale.");
            Assert.AreEqual(0, mill.GetCategoryStock("flour").CurrentStockUnits);
            Assert.AreEqual(0, mill.LastDailyRevenueCents, "no revenue for undelivered goods.");
            Assert.AreEqual(0, mill.WeekToDateRevenueCents);
        }

        [Test]
        public void SettledSale_PartialStock_RefusesWithNoMutation()
        {
            BusinessRuntimeState mill = MillWithFlour(5, 5000);

            bool settled = mill.ResolveSettledSale("flour", 20, 1100, "bakery-1");

            Assert.IsFalse(settled, "5 units on hand cannot deliver a 20-unit sale.");
            Assert.AreEqual(5000, mill.CurrentCashCents, "no partial revenue for a refused sale.");
            Assert.AreEqual(5, mill.GetCategoryStock("flour").CurrentStockUnits,
                "refusal must not consume the 5 units on hand.");
            Assert.AreEqual(0, mill.LastDailyRevenueCents);
        }

        [Test]
        public void SettledSale_ExactStock_SettlesExactlyOnce()
        {
            BusinessRuntimeState mill = MillWithFlour(20, 5000);

            bool settled = mill.ResolveSettledSale("flour", 20, 1100, "bakery-1");

            Assert.IsTrue(settled);
            Assert.AreEqual(6100, mill.CurrentCashCents);
            Assert.AreEqual(0, mill.GetCategoryStock("flour").CurrentStockUnits);
            Assert.AreEqual(1100, mill.LastDailyRevenueCents);
        }

        [Test]
        public void SettledSale_ZeroUnits_PreservesExistingShape()
        {
            BusinessRuntimeState mill = MillWithFlour(0, 5000);

            bool settled = mill.ResolveSettledSale("flour", 0, 12000, "external:kansas-city-grain");

            Assert.IsTrue(settled, "0 units is never stock-short; the pre-existing zero-unit shape survives.");
            Assert.AreEqual(17000, mill.CurrentCashCents);
        }

        [Test]
        public void SettledSale_MissingBuyer_StillRefuses()
        {
            BusinessRuntimeState mill = MillWithFlour(100, 5000);

            Assert.IsFalse(mill.ResolveSettledSale("flour", 10, 550, string.Empty));
            Assert.IsFalse(mill.ResolveSettledSale("flour", 10, 550, null));
            Assert.AreEqual(5000, mill.CurrentCashCents);
            Assert.AreEqual(100, mill.GetCategoryStock("flour").CurrentStockUnits);
        }
    }
}
