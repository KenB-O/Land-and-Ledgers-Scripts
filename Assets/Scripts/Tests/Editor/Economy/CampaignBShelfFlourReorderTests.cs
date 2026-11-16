// CAMPAIGN-B (DEF-018), package 11: store-shelf flour replenishment —
// verify-then-fix. VERDICT: REFUTED. Shelf flour HAS a replenishment path
// in the current tree, so NO production file is changed by this package.
//
// The defect's evidence is stale (pre-Slice-16/18 tree):
//   (a) "StoreRestockDriver.cs:7 confirms the daily review runs with the
//       supplier list EMPTY" — the driver's header comment is stale. Slice 16
//       (GeneralStoreRuntimeManager.EnsureRegionalSuppliersExist, line 2676)
//       creates all twelve RegionalSupplierAdapters and registers them into
//       BOTH drivers (player + development NPC), and sets the delivered-cost
//       function from the opening freight card. The comment was never updated;
//       the code moved on. (Comment fix held for the DEF-036/037 batch —
//       coordinator gate NOT cleared for this package.)
//   (b) "No call in the audited drivers restocks store flour" — Slice 18
//       added the item-level chain. EvaluateRestock's item-managed branch
//       orders a NAMED item (flour) from the cheapest regional supplier whose
//       delivered cost sits below the item's own selling price, and
//       AdvanceDay ships that named item (ShipItem) and receives it as an
//       identified retail lot (ReceivePaidStock with the item id).
//
// The two paths, pinned by these tests with the REAL service, planner,
// adapters, registry, and delivered-cost calculator against a stub port
// shaped like the production NPC port (item-managed staple_food, flour
// target 60, lot 12, "10 lb sack", retail 29c):
//   DAILY (flour-specific): flour sold out (0/60) -> 60u ordered from
//     Redfield Grain & Feed @ 18c (delivered 22c < 29c retail, lead 2 days)
//     -> AdvanceDay ships 60u flour and books 60 identified flour lots,
//     cash -1080c, supplier ledger +1080c. Small shortfalls wait: a 4-unit
//     shortfall rounds to one 12u lot whose delivered cost (31c Millbrook /
//     37c Redfield) exceeds the 29c retail price, so no order is placed;
//     at a 24-unit shortfall the lot prices at 26c (Millbrook) and the
//     order goes out. The shelf drains to the ~0.25 health threshold
//     before the daily review attempts flour at all.
//   WEEKLY (category pool, NOT flour-specific): the weekly reorder buys
//     staple_food from the cheapest supplier by delivered cost — Ashford
//     General Mercantile, sugar 5c, delivered 8c for 60u — and Ship()
//     ships cheapest-first (60u sugar), booked as undifferentiated
//     staple_food category units. It tops up the category pool; it does
//     NOT restock flour lots. Flour lots depend on the daily chain.
//
// TERM NOTE (coordinator's DEF-018 gate cited "48c/sack, 1 sack = 18 bakery
// units, 10 lb/sack" as the authored Millbrook term to reuse): that 48c
// figure is the BAKERY aid-table term in MillbrookGrainCo.asset (bakery
// flour/sugar/yeast/wheat inventory, 230 sacks, reserve 460). The STORE
// flour term already authored in the registry (Slice 13) is DIFFERENT:
// Millbrook offers the store's flour SKU ("10 lb sack") at 20c — the
// asset's wholesale anchor 18c plus 2c for the 50-mile haul — starting 96.
// Redfield offers the same SKU at 18c. These tests pin the 20c/18c store
// terms (RegistryPinsAuthoredMillbrookStoreFlourTerm). If the 48c bakery
// term were ever applied to shelf flour it would CONFLICT with the
// authored 20c store offer for the same SKU; that reconciliation is a
// coordinator/owner question, not something this package codes. Since
// the defect is refuted, no price is (re)used by this package at all.
//
// All money figures below are derived from authored data, shown in the
// comments: flour retail 29c = round(20c landed x 1.0 markup x 1.45
// staple_food markup) clamped to the [28,45]c band
// (GetCostPlusPriceCents); weekly staple_food landed cost 13c =
// round((20+2+13+7+6+18+42)/7 x 0.88); freight 125c Ashford / 225c
// Redfield (25c base + 1c/mi round trip over 50/100 mi). No price,
// quantity, or supplier term is invented here.
//
// SCOPE LIMITS (honest): the manager wiring above the service —
// DriveDailyRestock (manager:2595), EnsureRegionalSuppliersExist (2676),
// OnWeekChanged -> ResolveWeeklyPayrollAndReorder (1557) -> 
// ResolveCostedPendingReorders (4161) — is source-verified only; the
// manager is harness-excluded (Unity types: FindAnyObjectByType, Debug,
// MonoBehaviour). No live Unity run has yet shown a flour order placed in
// the dev scene; that proof is owed by the Codex ~30-day simulation test.
// The weekly receipt test runs the REAL planner against a stub
// BusinessRuntimeState implementing the contract the planner's own call
// sites require (GetCategoryStock / CurrentCashCents / TrySpendCents /
// ApplyDeliveredShipment / AddCashCents); the REAL state's
// ApplyDeliveredShipment semantics are Unity-verified only.
using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Population;
using LandLedgers.World.Journeys;
using LandLedgers.World.Regional;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    [TestFixture]
    public sealed class CampaignBShelfFlourReorderTests
    {
        // Authored flour numbers (GeneralStoreBusinessDefinition.asset):
        // target 60, lot 12, "10 lb sack"; retail 29c (see header).
        private const string Flour = "flour";
        private const string StapleFood = "staple_food";
        private const int FlourTarget = 60;
        private const int FlourLot = 12;
        private const int FlourRetailCents = 29;
        private const int StoreStartingCashCents = 45000;

        // Manager's delivered-cost function, copied exactly
        // (GeneralStoreRuntimeManager.EnsureRegionalSuppliersExist, 2686):
        // empty item -> category calculator, named item -> item calculator,
        // both on the opening freight card (25c base, 1c/mi, round trip).
        private static Func<IUpstreamGoodsSupplier, string, string, int, int> DeliveredCost()
        {
            return (supplier, categoryId, itemId, units) =>
                string.IsNullOrWhiteSpace(itemId)
                    ? RegionalDeliveredCost.DeliveredCentsPerUnit(supplier, categoryId, units,
                        RegionalFreightRateCard.OpeningCompany)
                    : RegionalDeliveredCost.DeliveredCentsPerUnitForItem(supplier, itemId, units,
                        RegionalFreightRateCard.OpeningCompany);
        }

        private static List<IUpstreamGoodsSupplier> AllRegionalSuppliers()
        {
            return RegionalSupplierSet.AsSuppliers(RegionalSupplierSet.CreateAll());
        }

        // Stub port shaped like the production NPC port
        // (GeneralStoreTradingPort over BuildHouseholdTradingPort): the flour
        // offer is its own item-managed offer (package size 10 groups it
        // apart from the other staple_food items), target/lot/unit from the
        // asset, retail 29c. ReceivePaidStock books identified flour lots
        // and debits cash atomically, as the production port does.
        private sealed class ShelfFlourStorePort : IItemLevelRestockTradingPort,
            IPaidStoreReceivingPort, IIdentifiedRetailStockReceivingPort
        {
            public int CashCents = StoreStartingCashCents;
            public int FlourLots;
            public readonly List<GeneralStoreCategoryOffer> Offers = new List<GeneralStoreCategoryOffer>
            {
                new GeneralStoreCategoryOffer
                {
                    CategoryId = StapleFood,
                    OfferedItemIds = new List<string> { Flour },
                    StoreUnitsPerItemUnit = 1,
                    HouseholdUnitsPerStoreUnit = 10,
                    UsesItemLevelInventory = true,
                },
            };

            public string StoreBusinessId => "general-store";
            public string StoreName => "General Store";
            public string LocationId => "building:1";
            public IReadOnlyList<GeneralStoreCategoryOffer> CategoryOffers => Offers;
            public StoreTradingCapability TradingCapability => new StoreTradingCapability { Staffed = true };
            public int CurrentCashCents => CashCents;
            public bool OffersDelivery => false;
            public bool OffersTradeCredit => false;
            public int TradeCreditLimitCents => 0;

            public int CategoryStockUnits(string categoryId) =>
                string.Equals(categoryId, StapleFood, StringComparison.OrdinalIgnoreCase) ? FlourLots : 0;
            public int CategoryTargetStockUnits(string categoryId) =>
                string.Equals(categoryId, StapleFood, StringComparison.OrdinalIgnoreCase) ? FlourTarget : 0;
            public int CategoryPricePerUnitCents(string categoryId) =>
                string.Equals(categoryId, StapleFood, StringComparison.OrdinalIgnoreCase) ? FlourRetailCents : 0;
            public int CategoryReceivableUnits(string categoryId) => 10000;
            public int DeliveryFeeCents(string itemId, int units) => 0;

            public bool TryConsumeStoreUnits(string categoryId, int units, out int consumedUnits)
            {
                consumedUnits = 0;
                return false;
            }

            public void RecordCashSettlement(string categoryId, int unitsSold, int revenueCents)
            {
            }

            public void RecordCreditSettlement(string categoryId, int unitsSold, int amountCents, string obligationId)
            {
            }

            public string SpendCash(int amountCents, string purpose)
            {
                if (amountCents < 0) return "negative spend refused";
                if (amountCents > CashCents) return "insufficient cash";
                CashCents -= amountCents;
                return null;
            }

            public string ReceiveStock(string categoryId, int units, string provenanceLabel)
            {
                if (units <= 0) return "no units to receive";
                if (string.Equals(categoryId, StapleFood, StringComparison.OrdinalIgnoreCase)) FlourLots += units;
                return null;
            }

            public bool UsesItemLevelInventory(string itemId) =>
                string.Equals(itemId, Flour, StringComparison.OrdinalIgnoreCase);
            public int ItemStockUnits(string itemId) => ItemRestockStockUnits(itemId);
            public int ItemPricePerUnitCents(string itemId) =>
                string.Equals(itemId, Flour, StringComparison.OrdinalIgnoreCase) ? FlourRetailCents : 0;

            public bool TryConsumeItemStockUnits(string itemId, int units, out int consumedUnits)
            {
                consumedUnits = 0;
                if (!string.Equals(itemId, Flour, StringComparison.OrdinalIgnoreCase) || units <= 0) return false;
                int take = Math.Min(units, FlourLots);
                if (take <= 0) return false;
                FlourLots -= take;
                consumedUnits = take;
                return true;
            }

            public void RecordItemCashSettlement(string categoryId, int unitsSold, int revenueCents)
            {
            }

            public int ItemRestockStockUnits(string itemId) =>
                string.Equals(itemId, Flour, StringComparison.OrdinalIgnoreCase) ? FlourLots : 0;
            public int ItemRetailPricePerStoreUnitCents(string itemId) =>
                string.Equals(itemId, Flour, StringComparison.OrdinalIgnoreCase) ? FlourRetailCents : 0;
            public int ItemReorderTargetUnits(string itemId) =>
                string.Equals(itemId, Flour, StringComparison.OrdinalIgnoreCase) ? FlourTarget : 0;
            public int ItemReorderLotSize(string itemId) =>
                string.Equals(itemId, Flour, StringComparison.OrdinalIgnoreCase) ? FlourLot : 0;
            public string ItemUnitLabel(string itemId) =>
                string.Equals(itemId, Flour, StringComparison.OrdinalIgnoreCase) ? "10 lb sack" : string.Empty;

            public string ReceivePaidStock(string lotId, string itemId, string categoryId, string unitLabel,
                int units, int costCents, string provenance, int dayIndex)
            {
                if (units <= 0) return "no units to receive";
                if (costCents < 0 || costCents > CashCents) return "insufficient cash for paid receipt";
                CashCents -= costCents;
                if (string.Equals(itemId, Flour, StringComparison.OrdinalIgnoreCase)) FlourLots += units;
                return null;
            }

            public string ReceiveRetailItemLot(string lotId, string itemId, string categoryId, string unitLabel,
                int units, string provenance, int dayIndex)
            {
                if (units <= 0) return "no units to receive";
                if (string.Equals(itemId, Flour, StringComparison.OrdinalIgnoreCase)) FlourLots += units;
                return null;
            }

            public string ReceiveCostedRetailItemLot(string lotId, string itemId, string categoryId, string unitLabel,
                int units, string provenance, int dayIndex, int acquisitionCostCents)
            {
                return ReceiveRetailItemLot(lotId, itemId, categoryId, unitLabel, units, provenance, dayIndex);
            }
        }

        [Test]
        public void DailyChainOrdersSoldOutFlourFromRedfield()
        {
            var port = new ShelfFlourStorePort { FlourLots = 0 };
            var service = new StoreRestockService();
            List<IUpstreamGoodsSupplier> suppliers = AllRegionalSuppliers();

            // Production threshold base is 0.25 (lowStockWarningThreshold01);
            // sold-out flour (health 0) is well under it.
            service.EvaluateRestock(port, suppliers, 0, 0.25f, DeliveredCost());

            Assert.AreEqual(1, service.Orders.Count, "one flour order expected; diagnostics: " + string.Join(" | ", service.Diagnostics));
            StoreProcurementOrder order = service.Orders[0];
            Assert.AreEqual(Flour, order.ItemId);
            Assert.AreEqual(StapleFood, order.CategoryId);
            Assert.AreEqual(60, order.UnitsOrdered);
            // 60u: Redfield (18c goods + 225c freight) / 60 = 22c delivered
            // beats Millbrook (20c + 125c) / 60 = 23c; both under the 29c retail.
            Assert.AreEqual("redfield_grain_feed", order.UpstreamSupplierId);
            Assert.AreEqual(18, order.UnitCostCents);
            Assert.AreEqual(22, order.DeliveredCostCentsPerUnit);
            Assert.AreEqual(2, order.ExpectedArrivalDayIndex, "Redfield 100 mi / 96 mi-per-day = 2 days");
            Assert.AreEqual(StoreProcurementStage.Ordered, order.Stage);
        }

        [Test]
        public void DailyChainReceivesFlourAsIdentifiedLot()
        {
            var port = new ShelfFlourStorePort { FlourLots = 0 };
            var service = new StoreRestockService();
            List<IUpstreamGoodsSupplier> suppliers = AllRegionalSuppliers();
            service.EvaluateRestock(port, suppliers, 0, 0.25f, DeliveredCost());
            Assert.AreEqual(1, service.Orders.Count);

            RegionalSupplierAdapter redfield = null;
            foreach (IUpstreamGoodsSupplier supplier in suppliers)
            {
                var adapter = supplier as RegionalSupplierAdapter;
                if (adapter != null && adapter.SupplierId == "redfield_grain_feed") redfield = adapter;
            }
            Assert.IsNotNull(redfield);
            Assert.AreEqual(20000, redfield.CashCents);

            service.AdvanceDay(port, suppliers, 2, new List<string>());

            Assert.AreEqual(60, port.FlourLots, "flour lots restocked by the named-item receipt");
            Assert.AreEqual(StoreStartingCashCents - 60 * 18, port.CashCents, "60u x 18c goods paid");
            Assert.AreEqual(20000 + 60 * 18, redfield.CashCents, "seller ledger credited exactly what the store paid");
            Assert.AreEqual(96 - 60, redfield.OnHandUnits(Flour), "supplier flour debited from real stock");
            Assert.AreEqual(StoreProcurementStage.Received, service.Orders[0].Stage);
        }

        [Test]
        public void DailyChainNearlyFullFlourWaitsOnNoLossCheck()
        {
            // 56/60: 4 short, rounded up to one 12u lot. Delivered 31c
            // (Millbrook: 240c goods + 125c freight, /12) and 37c (Redfield:
            // 216c + 225c, /12) both exceed the 29c retail price, so the
            // no-loss check withholds the order instead of buying at a loss.
            // Threshold 1.0 forces the attempt at any health so the check
            // itself — not the health gate — is what refuses.
            var port = new ShelfFlourStorePort { FlourLots = 56 };
            var service = new StoreRestockService();
            service.EvaluateRestock(port, AllRegionalSuppliers(), 0, 1.0f, DeliveredCost());

            Assert.AreEqual(0, service.Orders.Count, "no order: a 12u lot loses money at 29c retail");
            Assert.AreEqual(56, port.FlourLots);
            bool namesNoProfit = false;
            foreach (string line in service.Diagnostics)
                if (line.IndexOf("no profitable upstream offer", StringComparison.OrdinalIgnoreCase) >= 0) namesNoProfit = true;
            Assert.IsTrue(namesNoProfit, "diagnostic must name the no-profit refusal; got: " + string.Join(" | ", service.Diagnostics));
        }

        [Test]
        public void DailyChainOrdersFlourOnceShortfallReachesTwoLots()
        {
            // 36/60: 24 short = two 12u lots. Delivered 26c (Millbrook:
            // 480c + 125c, /24) beats Redfield 28c (432c + 225c, /24) and
            // clears the 29c retail price, so the order goes out.
            var port = new ShelfFlourStorePort { FlourLots = 36 };
            var service = new StoreRestockService();
            List<IUpstreamGoodsSupplier> suppliers = AllRegionalSuppliers();
            service.EvaluateRestock(port, suppliers, 0, 1.0f, DeliveredCost());

            Assert.AreEqual(1, service.Orders.Count);
            StoreProcurementOrder order = service.Orders[0];
            Assert.AreEqual(Flour, order.ItemId);
            Assert.AreEqual(24, order.UnitsOrdered);
            Assert.AreEqual("millbrook_grain_co", order.UpstreamSupplierId);
            Assert.AreEqual(20, order.UnitCostCents);
            Assert.AreEqual(26, order.DeliveredCostCentsPerUnit);
            Assert.AreEqual(1, order.ExpectedArrivalDayIndex, "Ashford 50 mi / 96 mi-per-day = 1 day");

            service.AdvanceDay(port, suppliers, 1, new List<string>());
            Assert.AreEqual(60, port.FlourLots, "24u received back to the 60 target");
            Assert.AreEqual(StoreStartingCashCents - 24 * 20, port.CashCents);
        }

        [Test]
        public void DeliveredCostCrossoverMillbrookVsRedfield()
        {
            // The crossover the chain sees: near town wins small (freight
            // dominates), far town wins large (goods price dominates).
            var millbrook = new RegionalSupplierAdapter(RegionalBusinessRegistry.MillbrookGrainCo);
            var redfield = new RegionalSupplierAdapter(RegionalBusinessRegistry.RedfieldGrainFeed);
            var card = RegionalFreightRateCard.OpeningCompany;

            Assert.AreEqual(125, RegionalDeliveredCost.FreightCentsForSoloShipment(50f, card));
            Assert.AreEqual(225, RegionalDeliveredCost.FreightCentsForSoloShipment(100f, card));
            Assert.AreEqual(31, RegionalDeliveredCost.DeliveredCentsPerUnitForItem(millbrook, Flour, 12, card));
            Assert.AreEqual(37, RegionalDeliveredCost.DeliveredCentsPerUnitForItem(redfield, Flour, 12, card));
            Assert.AreEqual(23, RegionalDeliveredCost.DeliveredCentsPerUnitForItem(millbrook, Flour, 60, card));
            Assert.AreEqual(22, RegionalDeliveredCost.DeliveredCentsPerUnitForItem(redfield, Flour, 60, card));
            // Category calculator prices the category's cheapest item:
            // Redfield staple_food quotes potatoes 11c, not flour 18c.
            Assert.AreEqual(15, RegionalDeliveredCost.DeliveredCentsPerUnit(redfield, StapleFood, 60, card));
        }

        [Test]
        public void DriverWithRegisteredSuppliersRunsDailyReview()
        {
            // Directly refutes the defect's premise: with the twelve adapters
            // registered (Slice 16), the driver's daily review places a real
            // flour order instead of reporting an empty supplier list.
            var port = new ShelfFlourStorePort { FlourLots = 0 };
            var driver = new StoreRestockDriver();
            foreach (RegionalSupplierAdapter adapter in RegionalSupplierSet.CreateAll())
                driver.RegisterSupplier(adapter);
            Assert.AreEqual(12, driver.Suppliers.Count);
            driver.DeliveredCostPerUnitCents = DeliveredCost();

            Assert.IsTrue(driver.DriveDaily(port, 0, 0.25f));
            Assert.AreEqual(1, driver.Restock.Orders.Count);
            Assert.AreEqual(Flour, driver.Restock.Orders[0].ItemId);

            // Second drive the same day is a no-op: no duplicate order.
            Assert.IsTrue(driver.DriveDaily(port, 0, 0.25f));
            Assert.AreEqual(1, driver.Restock.Orders.Count);
        }

        [Test]
        public void WeeklyChooserPicksCheapestStapleFoodSupplier()
        {
            // 60u staple_food by delivered cost: Ashford Mercantile sugar 5c
            // (300c + 125c freight) / 60 = 8c beats Redfield General sugar 6c
            // (360c + 225c) / 60 = 10c, Redfield Grain potatoes 11c = 15c,
            // and Millbrook flour 20c = 23c. The weekly reorder buys the
            // category's cheapest item — it is NOT flour-specific.
            List<IUpstreamGoodsSupplier> suppliers = AllRegionalSuppliers();
            bool chosen = StoreWeeklyReorderPlanner.TryChooseNamedSupplier(
                suppliers, StapleFood, 60, DeliveredCost(),
                out IUpstreamGoodsSupplier supplier, out int goodsCost, out int deliveredCost);

            Assert.IsTrue(chosen, "a registered supplier must cover staple_food");
            Assert.AreEqual("ashford_general_mercantile", supplier.SupplierId);
            Assert.AreEqual(5, goodsCost);
            Assert.AreEqual(8, deliveredCost);
        }

        [Test]
        public void WeeklyNamedReceiptBooksCategoryUnitsAsSugarNotFlour()
        {
            // The weekly receipt ships cheapest-first and books
            // undifferentiated category units: 60u staple_food from Ashford
            // Mercantile ships as 60u SUGAR (100 on hand), not flour. The
            // buyer pays the authored landed cost 13c/u (see header); the
            // seller earns the 5c goods quote per unit.
            List<IUpstreamGoodsSupplier> suppliers = AllRegionalSuppliers();
            RegionalSupplierAdapter mercantile = null;
            foreach (IUpstreamGoodsSupplier supplier in suppliers)
            {
                var adapter = supplier as RegionalSupplierAdapter;
                if (adapter != null && adapter.SupplierId == "ashford_general_mercantile") mercantile = adapter;
            }
            Assert.IsNotNull(mercantile);
            Assert.AreEqual(100, mercantile.OnHandUnits("sugar"));

            var storeState = new BusinessRuntimeState();
            storeState.SetCurrentCashCents(StoreStartingCashCents);
            CategoryStockState stock = storeState.EnsureCategoryStock(StapleFood, 0, 120);
            var diagnostics = new List<string>();

            StoreWeeklyReorderPlanner.NamedSupplierReceiptResult result =
                StoreWeeklyReorderPlanner.ReceiveNamedSupplierShipment(
                    mercantile, storeState, StapleFood, 60, 13, 0, diagnostics);

            Assert.AreEqual(60, result.AcceptedUnits);
            Assert.AreEqual(60, result.ShippedUnits);
            Assert.AreEqual(0, result.ShortfallUnits);
            Assert.AreEqual(string.Empty, result.RefusalReason);
            Assert.AreEqual(60, stock.CurrentStockUnits, "category pool topped up");
            Assert.AreEqual(StoreStartingCashCents - 60 * 13, storeState.CurrentCashCents, "buyer pays landed cost");
            Assert.AreEqual(40, mercantile.OnHandUnits("sugar"), "sugar debited from real stock");
            Assert.AreEqual(20000 + 60 * 5, mercantile.CashCents, "seller ledger credited at the goods quote");
            bool namesSugar = false;
            foreach (string line in diagnostics)
                if (line.IndexOf("60x sugar", StringComparison.OrdinalIgnoreCase) >= 0) namesSugar = true;
            Assert.IsTrue(namesSugar, "ship diagnostic must name the sugar breakdown; got: " + string.Join(" | ", diagnostics));
            // Flour untouched anywhere: the weekly path never names flour.
            RegionalSupplierAdapter redfield = null;
            foreach (IUpstreamGoodsSupplier supplier in suppliers)
            {
                var adapter = supplier as RegionalSupplierAdapter;
                if (adapter != null && adapter.SupplierId == "redfield_grain_feed") redfield = adapter;
            }
            Assert.AreEqual(96, redfield.OnHandUnits(Flour));
        }

        [Test]
        public void RegistryPinsAuthoredMillbrookStoreFlourTerm()
        {
            // The STORE flour term (Slice 13): Millbrook offers the store's
            // flour SKU ("10 lb sack") at 20c — asset wholesale 18c plus 2c
            // for the 50-mile haul — starting 96. Redfield offers the same
            // SKU at 18c. The 48c/sack bakery aid-table term lives in
            // MillbrookGrainCo.asset and is NOT a registry offer: no
            // supplier here quotes flour at 48c.
            RegionalBusinessRegistry.RegionalBusinessDefinition millbrook =
                RegionalBusinessRegistry.MillbrookGrainCo;
            Assert.AreEqual(1, millbrook.Offers.Length, "Millbrook's registry rows are the store flour offer only; bakery rows stay in the asset");
            Assert.AreEqual(Flour, millbrook.Offers[0].ItemId);
            Assert.AreEqual("10 lb sack", millbrook.Offers[0].Unit);
            Assert.AreEqual(20, millbrook.Offers[0].WholesalePriceCents);
            Assert.AreEqual(96, millbrook.Offers[0].StartingUnits);
            Assert.AreEqual("staple_food", RegionalItemCategories.CategoryForItem(Flour));

            RegionalBusinessRegistry.RegionalBusinessDefinition redfield =
                RegionalBusinessRegistry.RedfieldGrainFeed;
            Assert.AreEqual(18, RegionalBusinessRegistry.WholesalePrice(redfield, Flour));
            Assert.AreEqual(96, new RegionalSupplierAdapter(redfield).OnHandUnits(Flour));

            foreach (RegionalBusinessRegistry.RegionalBusinessDefinition business in RegionalBusinessRegistry.All)
                foreach (RegionalBusinessRegistry.RegionalOffer offer in business.Offers)
                    Assert.AreNotEqual(48, offer.WholesalePriceCents,
                        "no registry offer quotes 48c — the bakery term is not a store term");
        }
    }
}
