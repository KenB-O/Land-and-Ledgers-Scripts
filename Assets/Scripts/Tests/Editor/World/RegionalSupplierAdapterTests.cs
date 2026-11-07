// Slice 14 (2026-11-05): regional businesses as live upstream suppliers
// for the store's daily restock chain. Proves quotes, lead times, shipping
// from real per-item stock, weekly production, and the full
// order -> ship -> pay -> receive loop with conservation.
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Population;
using LandLedgers.World.Regional;
using NUnit.Framework;

namespace LandLedgers.Tests.World
{
    public sealed class RegionalSupplierAdapterTests
    {
        private sealed class StubStorePort : IGeneralStoreTradingPort
        {
            public string StoreBusinessId { get; set; } = "general-store-1";
            public string StoreName { get; set; } = "General Store";
            public string LocationId { get; set; } = "general-store";
            public Dictionary<string, int> Stock = new Dictionary<string, int>();
            public Dictionary<string, int> Prices = new Dictionary<string, int>();
            public Dictionary<string, int> Targets = new Dictionary<string, int>();
            public List<GeneralStoreCategoryOffer> OfferList = new List<GeneralStoreCategoryOffer>();
            public int CashBalance;

            public IReadOnlyList<GeneralStoreCategoryOffer> CategoryOffers => OfferList;
            public StoreTradingCapability TradingCapability => new StoreTradingCapability { Staffed = true };
            public int CurrentCashCents => CashBalance;
            public bool OffersDelivery => false;
            public bool OffersTradeCredit => false;
            public int TradeCreditLimitCents => 0;
            public int CategoryStockUnits(string categoryId) => Stock.TryGetValue(categoryId, out int s) ? s : 0;
            public int CategoryTargetStockUnits(string categoryId) => Targets.TryGetValue(categoryId, out int t) ? t : 0;
            public int CategoryPricePerUnitCents(string categoryId) => Prices.TryGetValue(categoryId, out int p) ? p : 0;
            public bool TryConsumeStoreUnits(string categoryId, int units, out int consumedUnits)
            {
                consumedUnits = 0;
                return false;
            }
            public void RecordCashSettlement(string categoryId, int unitsSold, int revenueCents)
            {
                CashBalance += System.Math.Max(0, revenueCents);
            }
            public void RecordCreditSettlement(string categoryId, int unitsSold, int amountCents, string obligationId) { }
            public string SpendCash(int amountCents, string purpose)
            {
                if (amountCents < 0) return "negative spend";
                if (CashBalance < amountCents) return "cannot afford";
                CashBalance -= amountCents;
                return null;
            }
            public string ReceiveStock(string categoryId, int units, string provenanceLabel)
            {
                Stock[categoryId] = CategoryStockUnits(categoryId) + System.Math.Max(0, units);
                return null;
            }
            public int CategoryReceivableUnits(string categoryId) => int.MaxValue;
            public int DeliveryFeeCents(string itemId, int units) => 0;
        }

        [Test]
        public void Quote_IsCheapestInStockItem_AndRisesAsStockSellsOut()
        {
            var adapter = new RegionalSupplierAdapter(RegionalBusinessRegistry.AshfordMercantile);
            // Cheapest staple_food offer with stock: sugar at 5c.
            Assert.AreEqual(5, adapter.QuoteUnitCostCents("staple_food"));
            // Sell all the sugar (100u): the quote must rise to the next
            // cheapest in-stock item, salt at 7c — never quote stock that
            // does not exist.
            var diag = new List<string>();
            Assert.AreEqual(100, adapter.Ship("staple_food", 100, 0, diag));
            Assert.AreEqual(7, adapter.QuoteUnitCostCents("staple_food"));
        }

        [Test]
        public void LeadTime_IsDistanceBased_AshfordOneDay_RedfieldTwoDays()
        {
            var ashford = new RegionalSupplierAdapter(RegionalBusinessRegistry.AshfordMercantile);
            var redfield = new RegionalSupplierAdapter(RegionalBusinessRegistry.RedfieldGeneralStore);
            Assert.AreEqual(1, ashford.LeadTimeDays("staple_food"));
            Assert.AreEqual(2, redfield.LeadTimeDays("staple_food"));
            // Millbrook's fixture says the same: 50 miles, 1 transit day.
            var millbrook = new RegionalSupplierAdapter(RegionalBusinessRegistry.MillbrookGrainCo);
            Assert.AreEqual(1, millbrook.LeadTimeDays("staple_food"));
        }

        [Test]
        public void Ship_DebitsCheapestFirst_NeverBelowZero_PartialWhenShort()
        {
            var adapter = new RegionalSupplierAdapter(RegionalBusinessRegistry.AshfordButcher);
            // meat: beef 72u @ 6c, bacon 64u @ 10c, cheese 48u @ 10c.
            Assert.AreEqual(184, adapter.AvailableUnits("meat"));
            var diag = new List<string>();
            Assert.AreEqual(80, adapter.Ship("meat", 80, 0, diag));
            Assert.AreEqual(0, adapter.OnHandUnits("beef"), "Cheapest item ships first.");
            Assert.AreEqual(56, adapter.OnHandUnits("bacon"), "Remainder comes from the next item.");
            Assert.AreEqual(48, adapter.OnHandUnits("cheese"));
            // Ask for more than remains: partial ship, never negative.
            Assert.AreEqual(104, adapter.Ship("meat", 500, 0, diag));
            Assert.AreEqual(0, adapter.AvailableUnits("meat"));
            Assert.AreEqual(0, adapter.Ship("meat", 10, 0, diag));
            Assert.AreEqual(0, adapter.QuoteUnitCostCents("meat"), "No stock, no quote.");
        }

        [Test]
        public void WeeklyProduction_AddsMinimum_CappedAtReserve_AndIsIdempotentWithinAWeek()
        {
            var adapter = new RegionalSupplierAdapter(RegionalBusinessRegistry.AshfordApothecary);
            // patent_remedy: start 24, weekly min 8, reserve 48.
            var diag = new List<string>();
            Assert.AreEqual(24, adapter.Ship("medicine_remedies", 24, 0, diag));
            Assert.AreEqual(0, adapter.OnHandUnits("patent_remedy"));
            adapter.DriveWeeklyProduction(0); // anchors week 0, produces nothing yet
            Assert.AreEqual(0, adapter.OnHandUnits("patent_remedy"));
            adapter.DriveWeeklyProduction(0); // same week: no-op
            Assert.AreEqual(0, adapter.OnHandUnits("patent_remedy"));
            adapter.DriveWeeklyProduction(7); // one week later: +8
            Assert.AreEqual(8, adapter.OnHandUnits("patent_remedy"));
            adapter.DriveWeeklyProduction(21); // two more weeks: +16, capped at 48
            Assert.AreEqual(24, adapter.OnHandUnits("patent_remedy"));
            adapter.DriveWeeklyProduction(70); // many weeks: capped at the reserve
            Assert.AreEqual(48, adapter.OnHandUnits("patent_remedy"));
        }

        [Test]
        public void FullLoop_DriverOrdersFromCheapestRegionalSupplier_WithConservation()
        {
            var port = new StubStorePort();
            port.OfferList.Add(new GeneralStoreCategoryOffer
            {
                CategoryId = "staple_food",
                OfferedItemIds = new List<string> { "flour" },
                StoreUnitsPerItemUnit = 1,
            });
            port.Stock["staple_food"] = 10;
            port.Targets["staple_food"] = 100;
            port.Prices["staple_food"] = 35; // category average selling price
            port.CashBalance = 100000;

            var adapters = RegionalSupplierSet.CreateAll();
            var driver = new StoreRestockDriver();
            foreach (RegionalSupplierAdapter adapter in adapters)
                driver.RegisterSupplier(adapter);

            // Day 0: the cheapest staple quote anywhere is Ashford
            // Mercantile's sugar at 5c — it must win the order.
            Assert.IsTrue(driver.DriveDaily(port, 0, 0.5f));
            Assert.AreEqual(1, driver.Restock.Orders.Count);
            Assert.AreEqual("ashford_general_mercantile", driver.Restock.Orders[0].UpstreamSupplierId);
            Assert.AreEqual(5, driver.Restock.Orders[0].UnitCostCents);
            Assert.AreEqual(90, driver.Restock.Orders[0].UnitsOrdered);
            Assert.AreEqual(1, driver.Restock.Orders[0].ExpectedArrivalDayIndex, "Ashford lead time is 1 day.");

            // Day 1: the shipment arrives, the store pays, stock lands.
            int cashBefore = port.CashBalance;
            Assert.IsTrue(driver.DriveDaily(port, 1, 0.5f));
            Assert.AreEqual(100, port.CategoryStockUnits("staple_food"));
            Assert.AreEqual(cashBefore - 90 * 5, port.CashBalance, "Store paid exactly the order cost.");
            var mercantile = adapters.Find(a => a.SupplierId == "ashford_general_mercantile");
            Assert.AreEqual(10, mercantile.OnHandUnits("sugar"), "The 90 shipped units left the supplier's sugar stock (100 - 90).");
        }

        [Test]
        public void NoRegionalSupplier_ForAnUnofferedCategory_OrdersNothing()
        {
            var port = new StubStorePort();
            port.OfferList.Add(new GeneralStoreCategoryOffer
            {
                CategoryId = "luxury_goods",
                OfferedItemIds = new List<string> { "silk_cloth" },
                StoreUnitsPerItemUnit = 1,
            });
            port.Stock["luxury_goods"] = 0;
            port.Targets["luxury_goods"] = 50;
            port.Prices["luxury_goods"] = 100;
            port.CashBalance = 100000;

            var driver = new StoreRestockDriver();
            foreach (RegionalSupplierAdapter adapter in RegionalSupplierSet.CreateAll())
                driver.RegisterSupplier(adapter);
            Assert.IsTrue(driver.DriveDaily(port, 0, 0.5f));
            Assert.AreEqual(0, driver.Restock.Orders.Count, "No supplier offers luxury goods; no order is faked.");
        }

        [Test]
        public void DeliveredCost_RealFreightCard_PricesMeatOrder_GoodsPlusSoloFreight()
        {
            // Plan 06 Slice 0 companion: the exact seam the reserve repair
            // will price through — the supplier quote plus the wired
            // delivered-cost delegate (OpeningCompany card), no invented
            // rate. Meat quote is the cheapest in-stock item: beef at 6c.
            var butcher = new RegionalSupplierAdapter(RegionalBusinessRegistry.AshfordButcher);
            Assert.AreEqual(6, butcher.QuoteUnitCostCents("meat"));
            // Ashford is 50 miles: solo freight 25c + 2 x 50mi x 1c = 125c.
            // Delivered per unit for 5u: ceil((6 x 5 + 125) / 5) = 31c.
            Assert.AreEqual(31, RegionalDeliveredCost.DeliveredCentsPerUnit(butcher, "meat", 5, RegionalFreightRateCard.OpeningCompany));
            // A one-unit order carries the whole freight: 6 + 125 = 131c.
            Assert.AreEqual(131, RegionalDeliveredCost.DeliveredCentsPerUnit(butcher, "meat", 1, RegionalFreightRateCard.OpeningCompany));
        }

        [Test]
        public void RestoreFrom_NullDto_RestoresStartingStock()
        {
            // Plan 06 Slice 2 seam: a null DTO restores registry starting
            // stock (clear-then-load with nothing to load), so an old save
            // is never mistaken for a lost decrement.
            var adapter = new RegionalSupplierAdapter(RegionalBusinessRegistry.AshfordButcher);
            var diag = new List<string>();
            Assert.AreEqual(5, adapter.Ship("meat", 5, 0, diag));
            Assert.AreEqual(179, adapter.AvailableUnits("meat"));

            adapter.RestoreFrom(null, diag);

            Assert.AreEqual(184, adapter.AvailableUnits("meat"));
            Assert.AreEqual(72, adapter.OnHandUnits("beef"));
            Assert.AreEqual(64, adapter.OnHandUnits("bacon"));
            Assert.AreEqual(48, adapter.OnHandUnits("cheese"));
        }

        [Test]
        public void CaptureInto_RestoreFrom_RoundTrip_PreservesDecrement()
        {
            // Plan 06 Slice 2 seam: the save/load round trip preserves a
            // supplier decrement exactly, and a second restore is
            // idempotent (clear-then-load).
            var adapter = new RegionalSupplierAdapter(RegionalBusinessRegistry.AshfordButcher);
            var diag = new List<string>();
            Assert.AreEqual(5, adapter.Ship("meat", 5, 0, diag));
            var dto = new RegionalSupplySaveDto();
            adapter.CaptureInto(dto);

            var fresh = new RegionalSupplierAdapter(RegionalBusinessRegistry.AshfordButcher);
            Assert.AreEqual(184, fresh.AvailableUnits("meat"));
            fresh.RestoreFrom(dto, diag);

            Assert.AreEqual(179, fresh.AvailableUnits("meat"));
            Assert.AreEqual(67, fresh.OnHandUnits("beef"));
            fresh.RestoreFrom(dto, diag);
            Assert.AreEqual(179, fresh.AvailableUnits("meat"),
                "A second restore must not double-apply the decrement.");
        }

        [Test]
        public void AllTwelveBusinesses_BecomeSuppliers()
        {
            var adapters = RegionalSupplierSet.CreateAll();
            Assert.AreEqual(12, adapters.Count);
            var suppliers = RegionalSupplierSet.AsSuppliers(adapters);
            Assert.AreEqual(12, suppliers.Count);
            var ids = new HashSet<string>();
            foreach (RegionalSupplierAdapter adapter in adapters)
                Assert.IsTrue(ids.Add(adapter.SupplierId), $"Duplicate supplier id {adapter.SupplierId}.");
        }
    }
}
