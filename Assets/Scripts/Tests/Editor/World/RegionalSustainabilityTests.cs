// Slice 16 (2026-11-05): the regional supply chain over 30 simulated days.
// Proves sustainability: no infinite stock, no daily reset to full, no
// sales from zero stock, conservation on the money leg, and an honest wait
// when no supplier can fill an order.
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Population;
using LandLedgers.World.Regional;
using NUnit.Framework;

namespace LandLedgers.Tests.World
{
    public sealed class RegionalSustainabilityTests
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

            public void Consume(string categoryId, int units)
            {
                Stock[categoryId] = System.Math.Max(0, CategoryStockUnits(categoryId) - units);
            }
        }

        private static StoreRestockDriver DriverWithSuppliers(List<RegionalSupplierAdapter> adapters)
        {
            var driver = new StoreRestockDriver();
            foreach (RegionalSupplierAdapter adapter in adapters)
                driver.RegisterSupplier(adapter);
            driver.DeliveredCostPerUnitCents = (supplier, category, itemId, units) =>
                RegionalDeliveredCost.DeliveredCentsPerUnit(supplier, category, units,
                    RegionalFreightRateCard.OpeningCompany);
            return driver;
        }

        private static void AssertNoNegativeStock(List<RegionalSupplierAdapter> adapters)
        {
            foreach (RegionalSupplierAdapter adapter in adapters)
            {
                foreach (RegionalBusinessRegistry.RegionalBusinessDefinition business in RegionalBusinessRegistry.All)
                {
                    if (business.BusinessId != adapter.SupplierId) continue;
                    foreach (RegionalBusinessRegistry.RegionalOffer offer in business.Offers)
                        Assert.GreaterOrEqual(adapter.OnHandUnits(offer.ItemId), 0,
                            $"{adapter.SupplierId} went negative on {offer.ItemId}.");
                }
            }
        }

        [Test]
        public void ThirtyDays_SustainedSupply_Conservation_NoDailyReset()
        {
            var port = new StubStorePort();
            port.OfferList.Add(new GeneralStoreCategoryOffer { CategoryId = "staple_food", OfferedItemIds = new List<string> { "flour" }, StoreUnitsPerItemUnit = 1 });
            port.Stock["staple_food"] = 10;
            port.Targets["staple_food"] = 100;
            port.Prices["staple_food"] = 24; // category average selling price
            port.CashBalance = 1000000;
            int cashAtStart = port.CashBalance;

            var adapters = RegionalSupplierSet.CreateAll();
            var driver = DriverWithSuppliers(adapters);
            var mercantile = adapters.Find(a => a.SupplierId == "ashford_general_mercantile");

            for (int day = 0; day < 30; day++)
            {
                RegionalSupplierSet.DriveWeeklyProduction(adapters, day);
                if (day == 7)
                {
                    Assert.GreaterOrEqual(mercantile.OnHandUnits("sugar"), 30,
                        "One week of production (min 20/week) refilled sugar to at least 30 before day 7's drive.");
                }
                Assert.IsTrue(driver.DriveDaily(port, day, 0.5f));
                AssertNoNegativeStock(adapters);
                // Households consume 8 staple units a day.
                port.Consume("staple_food", 8);

                if (day == 0)
                {
                    // The day-0 order (90u) is placed, but it ships only when
                    // due: ExpectedArrivalDayIndex is day 1 (Ashford lead
                    // time), so the mercantile's sugar is still 100 tonight.
                    // The lead time is honest transit, not instant delivery.
                    Assert.AreEqual(100, mercantile.OnHandUnits("sugar"));
                    Assert.AreEqual(1, driver.Restock.Orders.Count);
                    Assert.AreEqual("ashford_general_mercantile", driver.Restock.Orders[0].UpstreamSupplierId);
                }
                if (day == 1)
                {
                    // Day 1: the order ships (sugar 100 -> 10, cheapest
                    // first) and is received the same day. No daily reset:
                    // sugar stays 10 until weekly production refills it.
                    Assert.AreEqual(10, mercantile.OnHandUnits("sugar"),
                        "Stock must not reset overnight; only weekly production refills it.");
                    Assert.AreEqual(84, port.CategoryStockUnits("staple_food"),
                        "Stock 2 after day 0's consumption, +90 received, -8 consumed.");
                }
            }

            Assert.Greater(port.CategoryStockUnits("staple_food"), 0,
                "After 30 days of consumption the store must still hold staple stock.");
            // Money conservation: cash paid equals the goods cost of every
            // order that reached Received.
            int paid = 0;
            int receivedUnits = 0;
            foreach (StoreProcurementOrder order in driver.Restock.Orders)
            {
                if (order.Stage == StoreProcurementStage.Received)
                {
                    paid += order.UnitsOrdered * order.UnitCostCents;
                    receivedUnits += order.UnitsOrdered;
                }
            }
            Assert.AreEqual(cashAtStart - paid, port.CashBalance);
            Assert.Greater(receivedUnits, 0, "The store must have received real goods over 30 days.");
            // No order was ever placed at a delivered loss.
            foreach (StoreProcurementOrder order in driver.Restock.Orders)
            {
                Assert.Greater(24, order.DeliveredCostCentsPerUnit,
                    $"Order {order.OrderId} was placed at delivered cost {order.DeliveredCostCentsPerUnit}c, at or above the 24c selling price.");
            }
        }

        [Test]
        public void MedicineDrained_BuyerWaitsHonestly_UntilWeeklyProduction()
        {
            var port = new StubStorePort();
            port.OfferList.Add(new GeneralStoreCategoryOffer { CategoryId = "medicine_remedies", OfferedItemIds = new List<string> { "patent_remedy" }, StoreUnitsPerItemUnit = 1 });
            port.Stock["medicine_remedies"] = 0;
            port.Targets["medicine_remedies"] = 40;
            port.Prices["medicine_remedies"] = 62; // category average selling price
            port.CashBalance = 1000000;

            var adapters = RegionalSupplierSet.CreateAll();
            var driver = DriverWithSuppliers(adapters);
            var ashfordApothecary = adapters.Find(a => a.SupplierId == "ashford_apothecary");
            var redfieldApothecary = adapters.Find(a => a.SupplierId == "redfield_apothecary");

            // Day 0: the only medicine in the region is Ashford's 24
            // patent remedies. The order is 24u, not 40 — it cannot exceed
            // what the chosen supplier actually has. It ships when due
            // (day 1, Ashford lead time), not at placement.
            RegionalSupplierSet.DriveWeeklyProduction(adapters, 0);
            Assert.IsTrue(driver.DriveDaily(port, 0, 0.5f));
            Assert.AreEqual(1, driver.Restock.Orders.Count);
            Assert.AreEqual(24, driver.Restock.Orders[0].UnitsOrdered);
            Assert.AreEqual(24, ashfordApothecary.OnHandUnits("patent_remedy"),
                "Not yet shipped: the order is due day 1.");

            // Day 1: the 24 ship and arrive. Households consume 6 a day, so
            // by day 2 the store is below half stock and reviews again.
            Assert.IsTrue(driver.DriveDaily(port, 1, 0.5f));
            Assert.AreEqual(24, port.CategoryStockUnits("medicine_remedies"));
            Assert.AreEqual(0, ashfordApothecary.OnHandUnits("patent_remedy"));
            port.Consume("medicine_remedies", 6);
            Assert.IsTrue(driver.DriveDaily(port, 2, 0.5f));
            port.Consume("medicine_remedies", 6);
            // Day 2's review: Redfield still holds 16 medicine bottles at
            // 40c goods (delivered 54c < 62c), so it CAN fill a 16-unit
            // order — the buyer chooses the other town, exactly as designed.
            Assert.AreEqual(2, driver.Restock.Orders.Count);
            Assert.AreEqual("redfield_apothecary", driver.Restock.Orders[1].UpstreamSupplierId);
            Assert.AreEqual(16, driver.Restock.Orders[1].UnitsOrdered);
            Assert.IsTrue(driver.DriveDaily(port, 3, 0.5f));
            port.Consume("medicine_remedies", 6);
            // The day-2 order arrives on day 4 (Redfield lead 2 days).
            // Both apothecaries are now empty; day 4's review orders nothing.
            int ordersBefore = driver.Restock.Orders.Count;
            Assert.IsTrue(driver.DriveDaily(port, 4, 0.5f));
            Assert.AreEqual(ordersBefore, driver.Restock.Orders.Count,
                "With no supplier able to fill the order, the buyer waits — no order is faked.");
            Assert.AreEqual(0, ashfordApothecary.OnHandUnits("patent_remedy"));
            Assert.AreEqual(0, redfieldApothecary.OnHandUnits("medicine_bottle"));

            // Day 7: weekly production refills both (8 + 8 units). The
            // next review can order again.
            RegionalSupplierSet.DriveWeeklyProduction(adapters, 7);
            Assert.AreEqual(8, ashfordApothecary.OnHandUnits("patent_remedy"));
            Assert.AreEqual(8, redfieldApothecary.OnHandUnits("medicine_bottle"));
        }
    }
}
