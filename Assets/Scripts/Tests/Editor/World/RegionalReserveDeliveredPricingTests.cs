// Priority 6 Slice 1 (2026-11-08): 12c household reserve discrepancy —
// delivered-pricing harness pins (tests only, no production change).
//
// The failing Unity test
// HouseholdReserveInventoryTests.PartialFillReattemptsOnlyTheRemainingDeficit
// priced the 3 shipped units at the beef price (6c) although the fixture's
// own drain leaves only cheese (10c) in stock at Ashford. Production charges
// 3 x 52c = 156c (goods 3 x 10c + 125c freight, ceiling-rounded); the test
// expected 3 x 48c = 144c. The 12c is entirely 3u x (10c - 6c) goods price.
// These pins prove the quote-after-drain -> delivered-price -> total chain
// at the seam level (adapter, delivered cost, chooser, registries), so the
// 12c cannot regress silently if anyone "fixes" production toward 144c.
// They are expected to PASS on current code: a failure rewrites plan
// section 2, it does not authorise a production change.
// See 06-plan-12c-household-reserve-discrepancy.md.
using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.World.Regional;
using NUnit.Framework;

namespace LandLedgers.Tests.World
{
    public sealed class RegionalReserveDeliveredPricingTests
    {
        private static RegionalSupplierAdapter AshfordButcher()
        {
            return RegionalSupplierSet.CreateAll()
                .Find(a => a.SupplierId == "ashford_butcher");
        }

        private static RegionalSupplierAdapter RedfieldButcher()
        {
            return RegionalSupplierSet.CreateAll()
                .Find(a => a.SupplierId == "redfield_butcher");
        }

        private static Func<IUpstreamGoodsSupplier, string, string, int, int> DeliveredDelegate()
        {
            RegionalFreightRateCard card = RegionalFreightRateCard.OpeningCompany;
            return (supplier, categoryId, itemId, units) =>
                RegionalDeliveredCost.DeliveredCentsPerUnit(supplier, categoryId, units, card);
        }

        [Test]
        public void PartialFill_PricesShippedCheeseNotAbsentBeef()
        {
            // Ashford butcher offers beef 6c x 72u, bacon 10c x 64u, cheese
            // 10c x 48u (184u meat). The drain ships 181u cheapest-first,
            // leaving 3u of cheese only.
            RegionalSupplierAdapter ashford = AshfordButcher();
            var diag = new List<string>();
            Assert.AreEqual(181, ashford.Ship("meat", 181, 0, diag));
            Assert.AreEqual(3, ashford.AvailableUnits("meat"));
            Assert.AreEqual(3, ashford.OnHandUnits("cheese"));
            Assert.AreEqual(0, ashford.OnHandUnits("beef"),
                "Beef is out of stock after the drain, so its 6c price must never be charged.");

            // The quote names the cheapest item WITH stock: cheese at 10c,
            // not the absent beef at 6c.
            Assert.AreEqual(10, ashford.QuoteUnitCostCents("meat"));

            // Delivered over the shipped count 3: (3 x 10 + 125) / 3,
            // ceiling-rounded = 52c/u.
            RegionalFreightRateCard card = RegionalFreightRateCard.OpeningCompany;
            Assert.AreEqual(52, RegionalDeliveredCost.DeliveredCentsPerUnit(ashford, "meat", 3, card));

            // The implied total is 3 x 52 = 156c; the exact goods + freight
            // is 3 x 10 + 125 = 155c (the 1c is the ceiling rounding).
            Assert.AreEqual(156, 3 * 52);
            Assert.AreEqual(155, 3 * 10 + 125);
        }

        [Test]
        public void FullFill_PricesBeefWhileBeefIsInStock()
        {
            // Control case: undrained Ashford butcher, beef in stock, so
            // the 6c beef price IS the correct quote — mirroring the sibling
            // test NamedSupplierReservePurchaseShipsRealStock.
            RegionalSupplierAdapter ashford = AshfordButcher();
            Assert.AreEqual(6, ashford.QuoteUnitCostCents("meat"));
            Assert.AreEqual(184, ashford.AvailableUnits("meat"));

            RegionalFreightRateCard card = RegionalFreightRateCard.OpeningCompany;
            Assert.AreEqual(31, RegionalDeliveredCost.DeliveredCentsPerUnit(ashford, "meat", 5, card),
                "(5 x 6 + 125) / 5 = 31 exactly, no rounding.");
            Assert.AreEqual(155, 5 * 31, "Goods 30c + freight 125c.");
            Assert.AreEqual(155, 5 * 6 + 125);
        }

        [Test]
        public void DeliveredPrice_IsComputedAtShippedCount_NotRequestedCount()
        {
            RegionalFreightRateCard card = RegionalFreightRateCard.OpeningCompany;

            // Undrained (beef 6c): a 3-unit shipment prices at 48c/u, a
            // 5-unit shipment at 31c/u — the price depends on the shipped
            // count, not the requested count.
            RegionalSupplierAdapter fresh = AshfordButcher();
            Assert.AreEqual(48, RegionalDeliveredCost.DeliveredCentsPerUnit(fresh, "meat", 3, card),
                "ceil((3 x 6 + 125) / 3) = 48.");
            Assert.AreEqual(31, RegionalDeliveredCost.DeliveredCentsPerUnit(fresh, "meat", 5, card),
                "ceil((5 x 6 + 125) / 5) = 31.");

            // Drained (cheese 10c): a 3-unit shipment prices at 52c/u; the
            // 5-unit price would be 35c/u.
            RegionalSupplierAdapter drained = AshfordButcher();
            var diag = new List<string>();
            Assert.AreEqual(181, drained.Ship("meat", 181, 0, diag));
            Assert.AreEqual(52, RegionalDeliveredCost.DeliveredCentsPerUnit(drained, "meat", 3, card),
                "ceil((3 x 10 + 125) / 3) = 52.");
            Assert.AreEqual(35, RegionalDeliveredCost.DeliveredCentsPerUnit(drained, "meat", 5, card),
                "ceil((5 x 10 + 125) / 5) = 35.");

            // Pricing a 3-unit shipment at the 5-unit price would charge
            // 3 x 35 = 105c for 155c of goods + freight — 50c destroyed,
            // freight under-recovered. The 3-unit price is the one that
            // applies to a 3-unit shipment.
            Assert.AreEqual(105, 3 * 35);
            Assert.AreEqual(155, 3 * 10 + 125);
            Assert.AreEqual(50, 155 - 105);
        }

        [Test]
        public void Chooser_TieAt52c_KeepsFirstRegisteredSupplier()
        {
            // After the Ashford drain, need 5u meat: Ashford at orderSize 3
            // prices 52c/u; Redfield at orderSize 5 prices ceil((5 x 7 +
            // 225) / 5) = 52c/u. The tie is kept by the first-registered
            // supplier (Ashford, registered before Redfield) because the
            // chooser only replaces on a strict improvement.
            List<RegionalSupplierAdapter> adapters = RegionalSupplierSet.CreateAll();
            RegionalSupplierAdapter ashford = adapters.Find(a => a.SupplierId == "ashford_butcher");
            var diag = new List<string>();
            Assert.AreEqual(181, ashford.Ship("meat", 181, 0, diag));

            bool found = StoreWeeklyReorderPlanner.TryChooseNamedSupplier(
                RegionalSupplierSet.AsSuppliers(adapters), "meat", 5, DeliveredDelegate(),
                out IUpstreamGoodsSupplier chosen, out int goods, out int delivered);
            Assert.IsTrue(found);
            Assert.AreEqual("ashford_butcher", chosen.SupplierId,
                "Tie at 52c/u: the first-registered supplier wins.");
            Assert.AreEqual(10, goods, "Cheese is the cheapest in-stock meat at Ashford after the drain.");
            Assert.AreEqual(52, delivered);

            // The Redfield side of the tie, checked directly: beef 7c,
            // freight 225c, ceil((35 + 225) / 5) = 52c/u.
            RegionalSupplierAdapter redfield = adapters.Find(a => a.SupplierId == "redfield_butcher");
            Assert.AreEqual(7, redfield.QuoteUnitCostCents("meat"));
            Assert.AreEqual(52, RegionalDeliveredCost.DeliveredCentsPerUnit(
                redfield, "meat", 5, RegionalFreightRateCard.OpeningCompany));
        }

        [Test]
        public void CeilingRounding_AddsOneCentToBothTotals_NetZeroToDifference()
        {
            // Beef case: exact 3 x 6 + 125 = 143c, charged 3 x 48 = 144c.
            int beefExact = 3 * 6 + 125;
            int beefCharged = 3 * 48;
            Assert.AreEqual(143, beefExact);
            Assert.AreEqual(144, beefCharged);
            Assert.AreEqual(1, beefCharged - beefExact, "Ceiling rounding adds +1c to the beef total.");

            // Cheese case: exact 3 x 10 + 125 = 155c, charged 3 x 52 = 156c.
            int cheeseExact = 3 * 10 + 125;
            int cheeseCharged = 3 * 52;
            Assert.AreEqual(155, cheeseExact);
            Assert.AreEqual(156, cheeseCharged);
            Assert.AreEqual(1, cheeseCharged - cheeseExact, "Ceiling rounding adds +1c to the cheese total.");

            // The rounding is +1c on both sides, so it contributes 0c net
            // to the 12c difference: the difference between the charged
            // totals is exactly the goods delta, 3u x (10c - 6c) = 12c.
            Assert.AreEqual(12, cheeseCharged - beefCharged);
            Assert.AreEqual(12, cheeseExact - beefExact);
            Assert.AreEqual(12, 3 * (10 - 6));
        }

        [Test]
        public void DrainLeavesThreeUnitsAtTenCents_RegardlessOfTieOrder()
        {
            // Bacon and cheese are both 10c, so even if Ship's
            // cheapest-first order between the two 10c items varied, the
            // remainder is still 3u priced at 10c either way.
            RegionalSupplierAdapter ashford = AshfordButcher();
            var diag = new List<string>();
            Assert.AreEqual(181, ashford.Ship("meat", 181, 0, diag));
            Assert.AreEqual(3, ashford.AvailableUnits("meat"));
            Assert.AreEqual(10, ashford.QuoteUnitCostCents("meat"),
                "Whatever 10c item survives the drain, the quote is 10c.");
            Assert.AreEqual(0, ashford.OnHandUnits("beef"));
            Assert.AreEqual(0, ashford.OnHandUnits("bacon"));
            Assert.AreEqual(3, ashford.OnHandUnits("cheese"));
            Assert.AreEqual(3,
                ashford.OnHandUnits("beef") + ashford.OnHandUnits("bacon") + ashford.OnHandUnits("cheese"),
                "The 3 remaining units are exactly the cheese.");
        }
    }
}
