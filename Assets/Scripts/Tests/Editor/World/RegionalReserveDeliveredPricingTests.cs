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

        // Priority 6 Slice 3 (2026-11-08): retry / partial-fill conservation
        // acceptance at the seam level (tests only, no production change).
        //
        // These tests deliberately MIRROR the GSRM computation rather than
        // driving GSRM: they run the two-day partial-fill -> retry sequence
        // through the real seams (TryChooseNamedSupplier +
        // RegionalDeliveredCost.DeliveredCentsPerUnit + Ship + a local charge
        // tally of shipped x delivered, mirroring the GSRM computation) and
        // assert cumulative exactly-once accounting. GSRM
        // (GeneralStoreRuntimeManager) is harness-excluded, so this does NOT
        // prove GSRM calls the seams in this order, calls Ship once per
        // drive, or charges once per drive — those are source review (plan
        // section 1) + Unity (plan section 6). The acceptance is not
        // overstated: what is harness-proven here is the pricing and
        // settlement arithmetic at the seams.
        //
        // Day 1: drain Ashford to 3u cheese, ship 3u at 52c/u = 156c, lost
        // demand 2u x 52c = 104c. Day 2: need the remaining 2u, Redfield
        // ships 2u at 120c/u = 240c, lost demand 0. Cumulative: 5u shipped,
        // 396c charged.

        [Test]
        public void PartialFillThenRetry_ConservesExactlyOnce()
        {
            List<RegionalSupplierAdapter> adapters = RegionalSupplierSet.CreateAll();
            RegionalSupplierAdapter ashford = adapters.Find(a => a.SupplierId == "ashford_butcher");
            RegionalSupplierAdapter redfield = adapters.Find(a => a.SupplierId == "redfield_butcher");
            var diag = new List<string>();
            RegionalFreightRateCard card = RegionalFreightRateCard.OpeningCompany;

            // Day 1: drain Ashford to 3u of cheese, then drive the need of 5u.
            Assert.AreEqual(181, ashford.Ship("meat", 181, 0, diag));
            Assert.AreEqual(3, ashford.AvailableUnits("meat"));

            bool foundDay1 = StoreWeeklyReorderPlanner.TryChooseNamedSupplier(
                RegionalSupplierSet.AsSuppliers(adapters), "meat", 5, DeliveredDelegate(),
                out IUpstreamGoodsSupplier chosenDay1, out int goodsDay1, out int deliveredDay1);
            Assert.IsTrue(foundDay1);
            Assert.AreEqual("ashford_butcher", chosenDay1.SupplierId);
            Assert.AreEqual(10, goodsDay1);
            Assert.AreEqual(52, deliveredDay1);
            Assert.AreEqual(52, RegionalDeliveredCost.DeliveredCentsPerUnit(ashford, "meat", 3, card));

            int shipCallsDay1 = 0;
            int shippedDay1 = ashford.Ship("meat", 3, 0, diag);
            shipCallsDay1++;
            Assert.AreEqual(1, shipCallsDay1, "Exactly one Ship call on the day-1 drive.");
            Assert.AreEqual(3, shippedDay1);
            int chargedDay1 = shippedDay1 * deliveredDay1;
            Assert.AreEqual(156, chargedDay1, "Day 1: 3u x 52c/u = 156c.");
            int lostDay1 = (5 - shippedDay1) * deliveredDay1;
            Assert.AreEqual(104, lostDay1, "Day 1 lost demand: 2u x 52c/u = 104c, carried only, never charged.");
            Assert.AreEqual(0, ashford.AvailableUnits("meat"), "Ashford meat is exhausted after day 1.");
            Assert.AreEqual(184, redfield.AvailableUnits("meat"), "Redfield is untouched by the day-1 drive.");
            Assert.AreEqual(72, redfield.OnHandUnits("beef"));

            // Day 2: the remaining deficit is 2u. Ashford has 0u and is
            // skipped by the chooser; Redfield ships 2u, priced fresh at
            // the shipped count 2 — not carried over from day 1's 52c/u.
            bool foundDay2 = StoreWeeklyReorderPlanner.TryChooseNamedSupplier(
                RegionalSupplierSet.AsSuppliers(adapters), "meat", 2, DeliveredDelegate(),
                out IUpstreamGoodsSupplier chosenDay2, out int goodsDay2, out int deliveredDay2);
            Assert.IsTrue(foundDay2);
            Assert.AreEqual("redfield_butcher", chosenDay2.SupplierId);
            Assert.AreEqual(7, goodsDay2, "Redfield beef is the cheapest in-stock meat there.");
            Assert.AreEqual(120, deliveredDay2, "Day 2: ceil((2 x 7 + 225) / 2) = 120c/u, priced fresh at shipped count 2.");
            Assert.IsTrue(deliveredDay2 != deliveredDay1, "The day-2 price is not day 1's 52c/u carried over.");
            Assert.AreEqual(120, RegionalDeliveredCost.DeliveredCentsPerUnit(redfield, "meat", 2, card));

            int shipCallsDay2 = 0;
            int shippedDay2 = redfield.Ship("meat", 2, 0, diag);
            shipCallsDay2++;
            Assert.AreEqual(1, shipCallsDay2, "Exactly one Ship call on the day-2 drive.");
            Assert.AreEqual(2, shippedDay2);
            int chargedDay2 = shippedDay2 * deliveredDay2;
            Assert.AreEqual(240, chargedDay2, "Day 2: 2u x 120c/u = 240c.");
            int lostDay2 = (2 - shippedDay2) * deliveredDay2;
            Assert.AreEqual(0, lostDay2, "Day 2 fills the remaining deficit, so lost demand is 0.");
            Assert.AreEqual(70, redfield.OnHandUnits("beef"), "Redfield beef 72 -> 70.");
            Assert.AreEqual(182, redfield.AvailableUnits("meat"), "Redfield meat 184 -> 182.");

            // Cumulative exactly-once accounting: 5u shipped, 396c charged,
            // and no day-1 amount charged twice (396 is 156 + 240, not
            // 156 + 156 + 240).
            Assert.AreEqual(5, shippedDay1 + shippedDay2, "Cumulative shipped: 3u + 2u = 5u, the full need.");
            Assert.AreEqual(396, chargedDay1 + chargedDay2, "Cumulative charged: 156c + 240c = 396c.");
            Assert.AreEqual(99604, 100000 - (chargedDay1 + chargedDay2), "Fixture wallet after both drives: 100000 - 396 = 99604.");
            Assert.AreEqual(104, lostDay1, "Cumulative lost demand after day 1 is 104c.");
            Assert.AreEqual(0, lostDay2, "Cumulative lost demand after day 2 is 0c (the deficit was filled).");
        }

        [Test]
        public void RetryDoesNotRepriceDayOne_Shipment()
        {
            // Drive the same two-day sequence, then re-assert the day-1
            // figures are unchanged: the retry re-prices only the remaining
            // deficit, it does not re-price (or re-charge) day 1's shipment.
            List<RegionalSupplierAdapter> adapters = RegionalSupplierSet.CreateAll();
            RegionalSupplierAdapter ashford = adapters.Find(a => a.SupplierId == "ashford_butcher");
            RegionalSupplierAdapter redfield = adapters.Find(a => a.SupplierId == "redfield_butcher");
            var diag = new List<string>();
            RegionalFreightRateCard card = RegionalFreightRateCard.OpeningCompany;

            Assert.AreEqual(181, ashford.Ship("meat", 181, 0, diag));
            bool foundDay1 = StoreWeeklyReorderPlanner.TryChooseNamedSupplier(
                RegionalSupplierSet.AsSuppliers(adapters), "meat", 5, DeliveredDelegate(),
                out IUpstreamGoodsSupplier chosenDay1, out int goodsDay1, out int deliveredDay1);
            Assert.IsTrue(foundDay1);
            Assert.AreEqual("ashford_butcher", chosenDay1.SupplierId);
            Assert.AreEqual(52, deliveredDay1);
            Assert.AreEqual(3, ashford.Ship("meat", 3, 0, diag));
            int chargedDay1 = 3 * deliveredDay1;
            Assert.AreEqual(156, chargedDay1);

            bool foundDay2 = StoreWeeklyReorderPlanner.TryChooseNamedSupplier(
                RegionalSupplierSet.AsSuppliers(adapters), "meat", 2, DeliveredDelegate(),
                out IUpstreamGoodsSupplier chosenDay2, out int goodsDay2, out int deliveredDay2);
            Assert.IsTrue(foundDay2);
            Assert.AreEqual("redfield_butcher", chosenDay2.SupplierId);
            Assert.AreEqual(120, deliveredDay2);
            Assert.AreEqual(2, redfield.Ship("meat", 2, 0, diag));
            int chargedDay2 = 2 * deliveredDay2;
            Assert.AreEqual(240, chargedDay2);

            // After the day-2 ship, the day-1 figures are unchanged.
            Assert.AreEqual(0, ashford.AvailableUnits("meat"), "Ashford is still exhausted; the retry took nothing from it.");
            Assert.AreEqual(156, chargedDay1, "The day-1 total is still 156c after the retry.");
            Assert.AreEqual(52, deliveredDay1, "The day-1 delivered price is still 52c/u after the retry.");
            Assert.AreEqual(10, goodsDay1, "The day-1 goods price is still the 10c cheese quote.");
            Assert.AreEqual(0, ashford.QuoteUnitCostCents("meat"),
                "Ashford is exhausted, so it quotes nothing — day 1's 52c/u lives in the recorded drive figures, not in a re-price of empty stock.");
            Assert.AreEqual(0, RegionalDeliveredCost.DeliveredCentsPerUnit(ashford, "meat", 3, card),
                "An exhausted supplier prices at 0; the retry does not re-derive day 1's price from empty stock.");
            Assert.AreEqual(396, chargedDay1 + chargedDay2, "Cumulative charged is still 156c + 240c = 396c.");
        }

        [Test]
        public void UnfilledRemainder_IsNeverCharged()
        {
            // Day 1 only: the 2u shortfall is carried as lost demand, never
            // charged. The charged total equals shipped units only.
            List<RegionalSupplierAdapter> adapters = RegionalSupplierSet.CreateAll();
            RegionalSupplierAdapter ashford = adapters.Find(a => a.SupplierId == "ashford_butcher");
            var diag = new List<string>();

            Assert.AreEqual(181, ashford.Ship("meat", 181, 0, diag));
            bool found = StoreWeeklyReorderPlanner.TryChooseNamedSupplier(
                RegionalSupplierSet.AsSuppliers(adapters), "meat", 5, DeliveredDelegate(),
                out IUpstreamGoodsSupplier chosen, out int goods, out int delivered);
            Assert.IsTrue(found);
            Assert.AreEqual("ashford_butcher", chosen.SupplierId);
            Assert.AreEqual(10, goods);
            Assert.AreEqual(52, delivered);

            int shipped = ashford.Ship("meat", 3, 0, diag);
            Assert.AreEqual(3, shipped);
            int charged = shipped * delivered;
            Assert.AreEqual(156, charged, "Charged total is shipped units only: 3u x 52c/u = 156c.");

            // Charging the requested 5u at 52c/u would be 260c — exactly
            // the 2u x 52c/u = 104c lost demand more than the charged total.
            // That 104c is a carried figure only; it is never charged.
            int requestedAtDelivered = 5 * delivered;
            Assert.AreEqual(260, requestedAtDelivered);
            Assert.AreEqual(104, requestedAtDelivered - charged, "The gap is exactly the lost demand, 2u x 52c/u.");
            Assert.AreEqual(104, (5 - shipped) * delivered, "Lost demand: 2 unfilled units x 52c/u = 104c.");
            Assert.AreEqual(0, ashford.AvailableUnits("meat"));
        }
    }
}
