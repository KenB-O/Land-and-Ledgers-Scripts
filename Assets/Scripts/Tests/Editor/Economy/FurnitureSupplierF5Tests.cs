using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.World.Regional;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// F5 (Agent F, expanded campaign 2026-11-16): the off-map furniture
    /// supplier, authorized by O-F2 (Kennedy, 2026-11-16) — one historically
    /// plausible, explicitly authored supplier through the existing commerce
    /// and freight systems, with real prices, catalog, availability,
    /// delivery time, payment and custody. No synthetic or instantly
    /// delivered stock.
    ///
    /// The supplier is Ashford General Mercantile (the plan's natural first
    /// candidate: a general store already sells household goods). It offers
    /// the eight first-wave furniture items at 60% of the store asset's
    /// retail baselines — inside the registry's ~55-65% band, below every
    /// band floor — with bounded stock per the registry rule (StartingUnits
    /// 2x the store asset's starting stock; weekly max 4x the reorder lot,
    /// min half of max; reserve 2x starting stock). The stove line via
    /// Redfield Ironworks is NOT authored here: the stove has no authored
    /// price in the carpentry wave (F6 leaves stove/counter/barrel unpriced,
    /// 0c, never guessed), so no stove offer is invented.
    ///
    /// What is pinned here (all through existing seams — the registry, the
    /// adapter, the weekly-reorder planner; the manager and logistics files
    /// that drive the weekly path are harness-excluded, so the harness
    /// reaches the path the same way StoreWeeklyReorderCustodyTests does):
    ///  1. The offer is authored: one supplier, eight items, finite
    ///     bounded stock, prices below the retail baselines.
    ///  2. The adapter quotes the furniture category with real stock and
    ///     a real 1-day lead time (Ashford, 50 miles at 96 miles/day).
    ///  3. The weekly reorder now names a supplier for furniture at the
    ///     real delivered cost — and the F4 refusal state (no supplier
    ///     covers furniture) is reproduced with Redfield-only suppliers.
    ///  4. A named-supplier furniture receipt moves all three ledgers
    ///     exactly once: supplier stock, buyer cash, store stock, plus the
    ///     seller's ledger credit at the goods quote.
    ///  5. A sold-out supplier stays out until its next weekly production
    ///     (bounded, auditable — no infinite inventory).
    ///  6. The delivered cost clears the category's average selling price
    ///     at any order size, so the chain will actually order (the daily
    ///     chain's no-loss gate would withhold otherwise).
    /// </summary>
    [TestFixture]
    public sealed class FurnitureSupplierF5Tests
    {
        // Retail baselines from GeneralStoreBusinessDefinition.asset (F2).
        // Wholesale prices are 60% of baseline (registry rule ~55-65%),
        // except the shelf at 50c (62.5%): 48c is pinned by
        // CampaignBShelfFlourReorderTests as the bakery flour term, and no
        // registry offer may quote it.
        private static readonly (string ItemId, int BaselineCents, int WholesaleCents,
            int StartingUnits, int WeeklyMin, int WeeklyMax, int Reserve)[] FurnitureOffers =
        {
            ("furniture.bed", 300, 180, 4, 2, 4, 8),
            ("furniture.table", 225, 135, 4, 2, 4, 8),
            ("furniture.chair", 125, 75, 8, 4, 8, 16),
            ("furniture.cabinet-small", 325, 195, 4, 2, 4, 8),
            ("furniture.cabinet-large", 425, 255, 4, 2, 4, 8),
            ("furniture.shelf", 80, 50, 8, 4, 8, 16),
            ("furniture.crate", 75, 45, 8, 4, 8, 16),
            ("furniture.bedroll", 60, 36, 8, 4, 8, 16),
        };

        // The store's weekly-reorder buyer cost for the furniture category:
        // GetAverageCategoryLandedCost averages the eight imputed landed
        // costs (round(baseline x 0.7), no authored wholesale cost, no
        // import): 210 + 158 + 88 + 228 + 298 + 56 + 52-or-53 + 42 = 1132
        // or 1133; / 8 = 141 either way. The crate half-cent rounding moves
        // the sum by 1c and the average stays 141.
        private const int FurnitureAverageLandedCostCents = 141;

        // The category's average selling price at defaults (F4 cost-plus
        // quotes: bed 294, table 221, chair 123, cabinet-small 319,
        // cabinet-large 417, shelf 78, crate 74-or-73, bedroll 59; sum 1585
        // or 1584; / 8 = 198 either way). The daily chain's no-loss gate
        // withholds an order whose delivered cost meets or exceeds this.
        private const int FurnitureAverageSellingPriceCents = 198;

        // Ashford solo freight on the opening company's card: 25c base +
        // 100 round-trip miles (50 miles one way) at 1c/mile = 125c/trip.
        private const int AshfordSoloFreightCents = 125;

        private static RegionalSupplierAdapter MercantileAdapter()
        {
            return new RegionalSupplierAdapter(RegionalBusinessRegistry.AshfordMercantile);
        }

        private static Func<IUpstreamGoodsSupplier, string, string, int, int> ProductionDeliveredCost()
        {
            // The manager's own lambda (GeneralStoreRuntimeManager,
            // EnsureRegionalSuppliersExist): category orders price through
            // DeliveredCentsPerUnit, item orders through
            // DeliveredCentsPerUnitForItem, on the opening company's card.
            return (supplier, categoryId, itemId, units) =>
                string.IsNullOrWhiteSpace(itemId)
                    ? RegionalDeliveredCost.DeliveredCentsPerUnit(
                        supplier, categoryId, units, RegionalFreightRateCard.OpeningCompany)
                    : RegionalDeliveredCost.DeliveredCentsPerUnitForItem(
                        supplier, itemId, units, RegionalFreightRateCard.OpeningCompany);
        }

        // 1. The offer is authored: one supplier, eight items, finite
        // bounded stock, prices below the retail baselines.
        [Test]
        public void Mercantile_OffersAllEightFurnitureItems_OneSupplierOnly()
        {
            var mercantile = RegionalBusinessRegistry.AshfordMercantile;
            Assert.AreEqual("ashford", mercantile.TownId);
            Assert.AreEqual("general_store", mercantile.BusinessKind);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var expected in FurnitureOffers)
            {
                RegionalBusinessRegistry.RegionalOffer offer = null;
                foreach (RegionalBusinessRegistry.RegionalOffer candidate in mercantile.Offers)
                    if (string.Equals(candidate.ItemId, expected.ItemId, StringComparison.OrdinalIgnoreCase))
                        offer = candidate;
                Assert.IsNotNull(offer, $"Ashford General Mercantile must offer {expected.ItemId}.");
                Assert.IsTrue(seen.Add(expected.ItemId));
                Assert.AreEqual("each", offer.Unit, $"{expected.ItemId} sells by the each, as in the store asset.");
                Assert.AreEqual(expected.WholesaleCents, offer.WholesalePriceCents,
                    $"{expected.ItemId} wholesale must be 60% of the {expected.BaselineCents}c baseline.");
                Assert.IsTrue(offer.WholesalePriceCents > 0 && offer.WholesalePriceCents < expected.BaselineCents,
                    $"{expected.ItemId} wholesale must be positive and below the retail baseline.");
                Assert.AreEqual(expected.StartingUnits, offer.StartingUnits,
                    $"{expected.ItemId} starting stock must be 2x the store asset's starting stock.");
                Assert.AreEqual(expected.WeeklyMin, offer.WeeklyMinimumUnits);
                Assert.AreEqual(expected.WeeklyMax, offer.WeeklyMaximumUnits,
                    $"{expected.ItemId} weekly max must be 4x the store's reorder lot.");
                Assert.AreEqual(expected.Reserve, offer.TargetReserveUnits,
                    $"{expected.ItemId} reserve must be 2x starting stock.");
            }

            // O-F2 authorizes ONE supplier: no other business offers any
            // furniture item, and Redfield covers none of them.
            foreach (var expected in FurnitureOffers)
            {
                List<RegionalBusinessRegistry.RegionalBusinessDefinition> suppliers =
                    RegionalBusinessRegistry.SuppliersForItem(expected.ItemId);
                Assert.AreEqual(1, suppliers.Count, $"{expected.ItemId} must have exactly one supplier.");
                Assert.AreEqual("ashford_general_mercantile", suppliers[0].BusinessId);
                Assert.IsTrue(!RegionalBusinessRegistry.ItemsCoveredInTown("redfield").Contains(expected.ItemId),
                    $"Redfield must not offer {expected.ItemId}.");
            }
        }

        // 2. The adapter quotes the furniture category with real stock and
        // a real 1-day lead time (Ashford, 50 miles at 96 miles/day).
        [Test]
        public void Adapter_QuotesFurnitureCategory_RealStockAndOneDayLead()
        {
            RegionalSupplierAdapter adapter = MercantileAdapter();

            // Starting stock is the sum of the eight StartingUnits: 48.
            Assert.AreEqual(48, adapter.AvailableUnits("furniture"),
                "Furniture on hand must be exactly the authored starting stock — never more, never infinite.");
            // The category quote names the cheapest in-stock item: the
            // bedroll at 36c.
            Assert.AreEqual(36, adapter.QuoteUnitCostCents("furniture"));
            Assert.AreEqual(1, adapter.LeadTimeDays("furniture"),
                "Ashford is 50 miles away: one transit day, not instant delivery.");

            IReadOnlyList<string> offered = adapter.OfferedItemsInCategory("furniture");
            Assert.AreEqual(8, offered.Count);
            foreach (var expected in FurnitureOffers)
            {
                Assert.AreEqual(expected.WholesaleCents, adapter.QuoteItemUnitCostCents(expected.ItemId),
                    $"{expected.ItemId} must quote at its authored wholesale price.");
                Assert.AreEqual(expected.StartingUnits, adapter.AvailableItemUnits(expected.ItemId));
                bool listed = false;
                foreach (string offeredId in offered)
                    if (string.Equals(offeredId, expected.ItemId, StringComparison.OrdinalIgnoreCase))
                        listed = true;
                Assert.IsTrue(listed, $"{expected.ItemId} must be listed in the furniture offers.");
            }
        }

        // 3. The weekly reorder now names a supplier for furniture at the
        // real delivered cost — and the F4 refusal state (no supplier
        // covers furniture) is reproduced with Redfield-only suppliers.
        [Test]
        public void WeeklyReorder_ChoosesMercantileForFurniture_AtRealDeliveredCost()
        {
            List<RegionalSupplierAdapter> all = RegionalSupplierSet.CreateAll();
            var suppliers = new List<IUpstreamGoodsSupplier>();
            foreach (RegionalSupplierAdapter adapter in all) suppliers.Add(adapter);

            bool found = StoreWeeklyReorderPlanner.TryChooseNamedSupplier(
                suppliers, "furniture", 4, ProductionDeliveredCost(),
                out IUpstreamGoodsSupplier chosen, out int goods, out int delivered);
            Assert.IsTrue(found, "The weekly reorder must now find a named supplier for furniture (F7 finding F4).");
            Assert.AreEqual("ashford_general_mercantile", chosen.SupplierId);
            Assert.AreEqual("Ashford General Mercantile", chosen.SupplierName);
            Assert.AreEqual(36, goods, "The goods quote is the cheapest in-stock furniture item (bedroll).");
            // Delivered per unit for a 4-unit order: ceil((36 x 4 + 125) / 4)
            // = ceil(269 / 4) = 68c (goods + one solo Ashford trip).
            Assert.AreEqual(68, delivered);

            // The before-state: with only Redfield suppliers, no one covers
            // furniture, so the chooser refuses — the loud refusal the
            // weekly step records (StoreWeeklyReorderPlanner.
            // UncoveredCategoryReason). F5 clears it by authoring the offer.
            var redfieldOnly = new List<IUpstreamGoodsSupplier>();
            foreach (RegionalSupplierAdapter adapter in all)
                if (string.Equals(adapter.TownId, "redfield", StringComparison.OrdinalIgnoreCase))
                    redfieldOnly.Add(adapter);
            Assert.IsTrue(!StoreWeeklyReorderPlanner.TryChooseNamedSupplier(
                redfieldOnly, "furniture", 4, ProductionDeliveredCost(),
                out _, out _, out _));
            Assert.IsTrue(StoreWeeklyReorderPlanner.UncoveredCategoryReason("furniture", redfieldOnly.Count)
                .Contains("furniture"));
        }

        // 4. A named-supplier furniture receipt moves all three ledgers
        // exactly once: supplier stock, buyer cash, store stock, plus the
        // seller's ledger credit at the goods quote.
        [Test]
        public void NamedSupplierReceipt_Furniture_MovesAllThreeLedgersExactlyOnce()
        {
            RegionalSupplierAdapter adapter = MercantileAdapter();
            Assert.AreEqual(48, adapter.AvailableUnits("furniture"));
            Assert.AreEqual(20000, adapter.CashCents, "The mercantile's ledger opens at its authored cash.");

            // The store's furniture shelf: empty against the 24-unit total
            // starting stock in the asset (2+2+4+2+2+4+4+4), so 24 pending.
            var state = new BusinessRuntimeState();
            state.EnsureCategoryStock("furniture", 0, 24);
            state.SetCurrentCashCents(10000);
            state.GetCategoryStock("furniture").QueueReorderToTarget();
            Assert.AreEqual(24, state.GetCategoryStock("furniture").PendingReorderUnits);
            Assert.AreEqual(4, state.AllocatePendingReorderUnits("furniture", 4));

            var diagnostics = new List<string>();
            StoreWeeklyReorderPlanner.NamedSupplierReceiptResult receipt =
                StoreWeeklyReorderPlanner.ReceiveNamedSupplierShipment(
                    adapter, state, "furniture", 4, FurnitureAverageLandedCostCents, 3, diagnostics);
            Assert.AreEqual(4, receipt.ShippedUnits);
            Assert.AreEqual(4, receipt.AcceptedUnits);
            Assert.AreEqual(4 * FurnitureAverageLandedCostCents, receipt.BuyerSpendCents);
            Assert.AreEqual(0, receipt.ShortfallUnits);
            Assert.AreEqual(string.Empty, receipt.RefusalReason);

            // Supplier stock decremented exactly once by the ordered units,
            // cheapest first: 4 bedrolls leave (8 -> 4).
            Assert.AreEqual(44, adapter.AvailableUnits("furniture"));
            Assert.AreEqual(4, adapter.OnHandUnits("furniture.bedroll"));
            Assert.AreEqual(4, adapter.OnHandUnits("furniture.bed"),
                "Only the cheapest item ships; the beds stay put.");
            // Buyer cash debited exactly once at the landed cost.
            Assert.AreEqual(10000 - 4 * FurnitureAverageLandedCostCents, state.CurrentCashCents);
            // Store stocks exactly the shipped units.
            Assert.AreEqual(4, state.GetCategoryStock("furniture").CurrentStockUnits);
            // Seller ledger credited exactly once at the pre-ship goods
            // quote (4 bedrolls x 36c).
            Assert.AreEqual(20000 + 4 * 36, adapter.CashCents);
            Assert.IsTrue(receipt.StatusLine.Contains("Ashford General Mercantile"));
        }

        // 5. A sold-out supplier stays out until its next weekly
        // production (bounded, auditable — no infinite inventory).
        [Test]
        public void SellOut_StaysOut_UntilWeeklyProduction()
        {
            RegionalSupplierAdapter adapter = MercantileAdapter();
            var diagnostics = new List<string>();

            // Drain all 48 units. The adapter ships cheapest first and
            // never below zero; a further ship moves nothing.
            Assert.AreEqual(48, adapter.Ship("furniture", 48, 0, diagnostics));
            Assert.AreEqual(0, adapter.AvailableUnits("furniture"));
            Assert.AreEqual(0, adapter.QuoteUnitCostCents("furniture"),
                "With no stock there is no quote — the chain cannot price a sold-out supplier.");
            Assert.AreEqual(0, adapter.Ship("furniture", 4, 0, diagnostics));
            Assert.IsTrue(!StoreWeeklyReorderPlanner.TryChooseNamedSupplier(
                new List<IUpstreamGoodsSupplier> { adapter }, "furniture", 4, ProductionDeliveredCost(),
                out _, out _, out _),
                "A sold-out supplier is skipped by the chooser, exactly as before F5 for uncovered categories.");

            // Weekly production: the first drive (day 0) only records the
            // week; the day-7 drive adds WeeklyMinimumUnits per item,
            // capped at the target reserve. From zero stock that is exactly
            // the minimum per item: 2+2+4+2+2+4+4+4 = 24 units.
            RegionalSupplierSet.DriveWeeklyProduction(
                new List<RegionalSupplierAdapter> { adapter }, 0);
            RegionalSupplierSet.DriveWeeklyProduction(
                new List<RegionalSupplierAdapter> { adapter }, 7);
            Assert.AreEqual(24, adapter.AvailableUnits("furniture"));
            Assert.AreEqual(2, adapter.OnHandUnits("furniture.bed"));
            Assert.AreEqual(2, adapter.OnHandUnits("furniture.table"));
            Assert.AreEqual(4, adapter.OnHandUnits("furniture.chair"));
            Assert.AreEqual(2, adapter.OnHandUnits("furniture.cabinet-small"));
            Assert.AreEqual(2, adapter.OnHandUnits("furniture.cabinet-large"));
            Assert.AreEqual(4, adapter.OnHandUnits("furniture.shelf"));
            Assert.AreEqual(4, adapter.OnHandUnits("furniture.crate"));
            Assert.AreEqual(4, adapter.OnHandUnits("furniture.bedroll"));
            Assert.AreEqual(36, adapter.QuoteUnitCostCents("furniture"),
                "After production the bedroll is back in stock, so the quote returns.");
            Assert.IsTrue(StoreWeeklyReorderPlanner.TryChooseNamedSupplier(
                new List<IUpstreamGoodsSupplier> { adapter }, "furniture", 4, ProductionDeliveredCost(),
                out IUpstreamGoodsSupplier chosen, out _, out _));
            Assert.AreEqual("ashford_general_mercantile", chosen.SupplierId);
        }

        // 6. The delivered cost clears the category's average selling price
        // at any order size, so the chain will actually order (the daily
        // chain's no-loss gate withholds an order whose delivered cost
        // meets or exceeds the selling price).
        [Test]
        public void DeliveredCost_ClearsAverageSellingPrice_AtAnyOrderSize()
        {
            RegionalSupplierAdapter adapter = MercantileAdapter();

            // Worst case is a 1-unit order: the whole solo trip lands on
            // one unit. Goods 36c (cheapest item) + 125c freight = 161c,
            // still below the 198c average selling price.
            Assert.AreEqual(36 + AshfordSoloFreightCents,
                RegionalDeliveredCost.DeliveredCentsPerUnit(
                    adapter, "furniture", 1, RegionalFreightRateCard.OpeningCompany));
            Assert.IsTrue(
                RegionalDeliveredCost.DeliveredCentsPerUnit(
                    adapter, "furniture", 1, RegionalFreightRateCard.OpeningCompany)
                < FurnitureAverageSellingPriceCents,
                "Even a 1-unit furniture order must clear the no-loss gate.");

            // Larger orders only get cheaper per unit (freight spreads).
            Assert.AreEqual(68, RegionalDeliveredCost.DeliveredCentsPerUnit(
                adapter, "furniture", 4, RegionalFreightRateCard.OpeningCompany));
            Assert.AreEqual(1, adapter.LeadTimeDays("furniture"),
                "Delivery takes a day of travel; nothing arrives instantly.");
        }
    }
}
