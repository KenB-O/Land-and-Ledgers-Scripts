using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Bakery;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W2A: the product catalog is data; prices come from the schedule, unknown
    /// products price at zero and never schedule.
    /// </summary>
    [TestFixture]
    public sealed class BakeryBreadCatalogTests
    {
        [Test]
        public void UnknownProduct_NeverKnown_NeverScheduled()
        {
            Assert.IsFalse(BakeryBreadCatalog.IsKnownProduct("bake.croissant-1870"));
            Assert.IsFalse(BakeryBreadCatalog.IsKnownProduct(null));
            Assert.IsFalse(BakeryBreadCatalog.IsKnownProduct(string.Empty));

            Assert.IsTrue(BakeryBreadCatalog.IsKnownProduct(BakeryBreadCatalog.BreadLoafId));
            Assert.IsTrue(BakeryBreadCatalog.IsKnownProduct(BakeryBreadCatalog.RollsId));
            Assert.IsTrue(BakeryBreadCatalog.IsKnownProduct(BakeryBreadCatalog.PieId));
        }

        [Test]
        public void Spec_DataSane_PositiveFlourPositiveYield()
        {
            foreach (string productId in new[]
                { BakeryBreadCatalog.BreadLoafId, BakeryBreadCatalog.RollsId, BakeryBreadCatalog.PieId })
            {
                var spec = BakeryBreadCatalog.GetSpec(productId);
                Assert.AreEqual(productId, spec.ProductId);
                Assert.Greater(spec.FlourUnitsPerBatch, 0, $"{productId}: flour per batch");
                Assert.Greater(spec.NotionUnitsPerBatch, 0, $"{productId}: notions per batch");
                Assert.Greater(spec.YieldUnitsPerBatch, 0, $"{productId}: yield per batch");
                Assert.Greater(spec.PrepMinutes, 0, $"{productId}: prep minutes");
                Assert.Greater(spec.BakeMinutes, 0, $"{productId}: bake minutes");
            }
        }

        [Test]
        public void FreshPrices_ComeFromSchedule_NeverGuessed()
        {
            var schedule = new BakeryPriceSchedule(
                breadLoafFreshCents: 9, rollFreshCents: 3, pieFreshCents: 30, dayOldDiscountPct: 50);

            Assert.AreEqual(9, BakeryBreadCatalog.GetFreshPriceCents(BakeryBreadCatalog.BreadLoafId, schedule));
            Assert.AreEqual(3, BakeryBreadCatalog.GetFreshPriceCents(BakeryBreadCatalog.RollsId, schedule));
            Assert.AreEqual(30, BakeryBreadCatalog.GetFreshPriceCents(BakeryBreadCatalog.PieId, schedule));
            Assert.AreEqual(0, BakeryBreadCatalog.GetFreshPriceCents("bake.croissant-1870", schedule));
        }

        [Test]
        public void DayOldPrice_AppliesDiscountFromSchedule()
        {
            var schedule = new BakeryPriceSchedule(
                breadLoafFreshCents: 8, rollFreshCents: 2, pieFreshCents: 25, dayOldDiscountPct: 50);

            Assert.AreEqual(4, BakeryBreadCatalog.GetDayOldPriceCents(BakeryBreadCatalog.BreadLoafId, schedule));
            Assert.AreEqual(1, BakeryBreadCatalog.GetDayOldPriceCents(BakeryBreadCatalog.RollsId, schedule));
            Assert.AreEqual(12, BakeryBreadCatalog.GetDayOldPriceCents(BakeryBreadCatalog.PieId, schedule));

            var noDiscount = new BakeryPriceSchedule(8, 2, 25, 100);
            Assert.AreEqual(8, BakeryBreadCatalog.GetDayOldPriceCents(BakeryBreadCatalog.BreadLoafId, noDiscount));
        }

        [Test]
        public void OvenStationId_MatchesWorkstationCatalog()
        {
            // Tech X §3.5: the bake oven definition is the single authority;
            // the catalog's station id must name it exactly.
            Assert.AreEqual("bake-oven", BakeryBreadCatalog.BakeOvenStationId);
        }
    }
}
