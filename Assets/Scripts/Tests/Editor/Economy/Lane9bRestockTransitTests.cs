using System.Collections.Generic;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// Lane 9b (Agent D D-6): restock deliveries carry the authored
    /// route transit instead of teleporting on paper. The order waits
    /// the supplier's LeadTimeDays as Ordered and only ships on its
    /// arrival day; the delivery it creates must record that transit
    /// (lead days x 1440 minutes) instead of TransitMinutes = 0.
    /// Kept in its own file because StoreRestockService joins the
    /// harness closure separately from the other Lane 9b tests.
    /// No test claims Unity compilation or PlayMode.
    /// </summary>
    [TestFixture]
    public sealed class Lane9bRestockTransitTests
    {
        private sealed class FakeStorePort : IGeneralStoreTradingPort, IPaidStoreReceivingPort
        {
            public string StoreBusinessId { get; set; } = "store-1";
            public string StoreName => "General Store";
            public string LocationId => "store";
            public Dictionary<string, int> Stock = new Dictionary<string, int>();
            public Dictionary<string, int> Prices = new Dictionary<string, int>();
            public Dictionary<string, int> Targets = new Dictionary<string, int>();
            public List<GeneralStoreCategoryOffer> OfferList = new List<GeneralStoreCategoryOffer>();
            public int CashBalance = 50000;

            public IReadOnlyList<GeneralStoreCategoryOffer> CategoryOffers => OfferList;
            public StoreTradingCapability TradingCapability => new StoreTradingCapability();
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

            public void RecordCashSettlement(string categoryId, int unitsSold, int revenueCents) { }
            public void RecordCreditSettlement(string categoryId, int unitsSold, int amountCents, string obligationId) { }

            public string SpendCash(int amountCents, string purpose)
            {
                int spend = System.Math.Max(0, amountCents);
                if (spend <= 0) return null;
                if (CashBalance < spend) return "cannot afford";
                CashBalance -= spend;
                return null;
            }

            public string ReceivePaidStock(string lotId, string itemId, string categoryId, string unitLabel,
                int units, int costCents, string provenance, int dayIndex)
            {
                if (CurrentCashCents < costCents) return "cannot afford";
                string problem = ReceiveStock(categoryId, units, provenance);
                return problem ?? SpendCash(costCents, provenance);
            }

            public string ReceiveStock(string categoryId, int units, string provenanceLabel)
            {
                if (units <= 0) return "nothing to receive";
                Stock[categoryId] = CategoryStockUnits(categoryId) + units;
                return null;
            }

            public int CategoryReceivableUnits(string categoryId)
            {
                int target = CategoryTargetStockUnits(categoryId);
                return target > 0 ? System.Math.Max(0, target - CategoryStockUnits(categoryId)) : int.MaxValue;
            }

            public int DeliveryFeeCents(string itemId, int units) => 0;
        }

        private sealed class FakeUpstreamSupplier : IUpstreamGoodsSupplier
        {
            public string SupplierId { get; set; } = "up-1";
            public string SupplierName { get; set; } = "Wholesaler";
            public Dictionary<string, int> Stock = new Dictionary<string, int>();
            public Dictionary<string, int> Quotes = new Dictionary<string, int>();
            public int LeadDays = 2;

            public int QuoteUnitCostCents(string categoryId) => Quotes.TryGetValue(categoryId, out int q) ? q : 0;
            public int LeadTimeDays(string categoryId) => LeadDays;
            public int AvailableUnits(string categoryId) => Stock.TryGetValue(categoryId, out int s) ? s : 0;

            public int Ship(string categoryId, int units, int dayIndex, List<string> diagnostics)
            {
                int have = AvailableUnits(categoryId);
                int shipped = System.Math.Min(System.Math.Max(0, units), have);
                if (shipped > 0) Stock[categoryId] = have - shipped;
                return shipped;
            }
        }

        [Test]
        public void RestockArrivesAfterPhysicalInterval_WithAuthoredTransit()
        {
            // D-6: supplier lead is 2 days (the authored route transit).
            // The order is placed day 0 and arrives day 2; nothing is
            // on the shelf before then, and the delivery that lands
            // records 2 days of transit (2 x 1440 minutes), not 0.
            var store = new FakeStorePort();
            store.OfferList.Add(new GeneralStoreCategoryOffer { CategoryId = "staple_food" });
            store.Targets["staple_food"] = 10;
            store.Stock["staple_food"] = 0;
            store.Prices["staple_food"] = 10;
            var supplier = new FakeUpstreamSupplier();
            supplier.Stock["staple_food"] = 100;
            supplier.Quotes["staple_food"] = 5;
            var suppliers = new List<IUpstreamGoodsSupplier> { supplier };
            var service = new StoreRestockService();
            var diagnostics = new List<string>();

            service.EvaluateRestock(store, suppliers, 0, 0.5f);

            Assert.AreEqual(1, service.Orders.Count, "One order must be placed on day 0.");
            Assert.AreEqual(2, service.Orders[0].ExpectedArrivalDayIndex, "The order arrives after the 2-day lead.");
            Assert.AreEqual(0, service.Deliveries.Count, "Nothing ships before the arrival day.");
            Assert.AreEqual(0, store.CategoryStockUnits("staple_food"), "Nothing is on the shelf before arrival.");

            service.AdvanceDay(store, suppliers, 1, diagnostics);

            Assert.AreEqual(0, service.Deliveries.Count, "Day 1 is still in transit: no delivery yet.");
            Assert.AreEqual(0, store.CategoryStockUnits("staple_food"));

            service.AdvanceDay(store, suppliers, 2, diagnostics);

            Assert.AreEqual(1, service.Deliveries.Count, "The delivery ships on the arrival day.");
            StoreDelivery delivery = service.Deliveries[0];
            Assert.AreEqual(2 * 1440, delivery.TransitMinutes,
                "The delivery must record the authored 2-day route transit in minutes, not 0.");
            Assert.IsTrue(delivery.Arrived, "The delivery arrives on the arrival day.");
            Assert.IsTrue(delivery.Received, "The delivery is received the same day it arrives.");
            Assert.AreEqual(StoreProcurementStage.Received, service.Orders[0].Stage);
            Assert.AreEqual(10, store.CategoryStockUnits("staple_food"), "The ordered units land on the shelf.");
        }

        [Test]
        public void LocalSupplierTransitIsRecordedNotZero()
        {
            // D-6: a zero-day lead is still recorded honestly (0 minutes
            // because the route is same-day, not because the field was
            // never set) — the assertion is on the mechanism, via a
            // 1-day lead producing exactly 1440 minutes.
            var store = new FakeStorePort();
            store.OfferList.Add(new GeneralStoreCategoryOffer { CategoryId = "staple_food" });
            store.Targets["staple_food"] = 10;
            store.Stock["staple_food"] = 0;
            store.Prices["staple_food"] = 10;
            var supplier = new FakeUpstreamSupplier { LeadDays = 1 };
            supplier.Stock["staple_food"] = 100;
            supplier.Quotes["staple_food"] = 5;
            var suppliers = new List<IUpstreamGoodsSupplier> { supplier };
            var service = new StoreRestockService();
            var diagnostics = new List<string>();

            service.EvaluateRestock(store, suppliers, 0, 0.5f);
            service.AdvanceDay(store, suppliers, 1, diagnostics);

            Assert.AreEqual(1, service.Deliveries.Count);
            Assert.AreEqual(1440, service.Deliveries[0].TransitMinutes,
                "A 1-day authored lead must record 1440 transit minutes.");
        }
    }
}
