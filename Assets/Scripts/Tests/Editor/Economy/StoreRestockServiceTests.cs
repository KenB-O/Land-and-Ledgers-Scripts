using System.Collections.Generic;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// Overnight 2026-11-04 slice 3: the General Store restock chain.
    /// The live game never drove this service (no production caller), so
    /// shelves drained and stayed empty; these tests lock in the chain
    /// itself plus the two overnight additions — the no-loss order rule
    /// and same-day same-supplier shipment consolidation — so the daily
    /// drive Codex wires later rests on proven prerequisites.
    /// </summary>
    [TestFixture]
    public sealed class StoreRestockServiceTests
    {
        private sealed class FakeStorePort : IGeneralStoreTradingPort
        {
            public string StoreBusinessId { get; set; } = "store-1";
            public string StoreName => "General Store";
            public string LocationId => "store";
            public Dictionary<string, int> Stock = new Dictionary<string, int>();
            public Dictionary<string, int> Prices = new Dictionary<string, int>();
            public Dictionary<string, int> Targets = new Dictionary<string, int>();
            public List<GeneralStoreCategoryOffer> OfferList = new List<GeneralStoreCategoryOffer>();
            public StoreTradingCapability Capability { get; set; } = new StoreTradingCapability();
            public int CashBalance = 50000;

            public IReadOnlyList<GeneralStoreCategoryOffer> CategoryOffers => OfferList;
            public StoreTradingCapability TradingCapability => Capability;
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
                int have = CategoryStockUnits(categoryId);
                int take = System.Math.Min(System.Math.Max(0, units), have);
                if (take <= 0) return false;
                Stock[categoryId] = have - take;
                consumedUnits = take;
                return true;
            }

            public void RecordCashSettlement(string categoryId, int unitsSold, int revenueCents) { }
            public void RecordCreditSettlement(string categoryId, int unitsSold, int amountCents, string obligationId) { }

            public string SpendCash(int amountCents, string purpose)
            {
                int spend = System.Math.Max(0, amountCents);
                if (spend <= 0) return null;
                if (CashBalance < spend) return $"cannot afford {spend}c: cash is {CashBalance}c";
                CashBalance -= spend;
                return null;
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

        private static FakeStorePort StoreWith(string categoryId, int stock, int target, int price)
        {
            var port = new FakeStorePort();
            port.OfferList.Add(new GeneralStoreCategoryOffer
            {
                CategoryId = categoryId,
                OfferedItemIds = new List<string>(),
                StoreUnitsPerItemUnit = 1,
            });
            port.Stock[categoryId] = stock;
            port.Targets[categoryId] = target;
            port.Prices[categoryId] = price;
            return port;
        }

        [Test]
        public void ProfitableOrder_ShipsAndReceivesWithPaymentAndStock()
        {
            var port = StoreWith("staple_food", 20, 100, 120);
            var upstream = new FakeUpstreamSupplier();
            upstream.Stock["staple_food"] = 500;
            upstream.Quotes["staple_food"] = 80;
            var service = new StoreRestockService();

            service.EvaluateRestock(port, new List<IUpstreamGoodsSupplier> { upstream }, 0, 0.5f);
            Assert.AreEqual(1, service.Orders.Count, "Below-threshold stock with no open order must order.");
            StoreProcurementOrder order = service.Orders[0];
            Assert.AreEqual(80, order.UnitsOrdered, "Order fills to target (100 - 20).");
            Assert.AreEqual(80, order.UnitCostCents);
            Assert.AreEqual(2, order.ExpectedArrivalDayIndex);

            service.AdvanceDay(port, new List<IUpstreamGoodsSupplier> { upstream }, 2, new List<string>());
            Assert.AreEqual(StoreProcurementStage.Received, order.Stage);
            Assert.AreEqual(100, port.CategoryStockUnits("staple_food"));
            Assert.AreEqual(50000 - 80 * 80, port.CashBalance, "Receipt pays real cash before stock books.");
            Assert.AreEqual(500 - 80, upstream.AvailableUnits("staple_food"), "Supplier stock is finite and debited.");
        }

        [Test]
        public void NoLossRule_WithholdsOrderAtOrAboveSellingPrice()
        {
            var port = StoreWith("staple_food", 20, 100, 120);
            var expensive = new FakeUpstreamSupplier { SupplierId = "up-exp", SupplierName = "Dear Wholesaler" };
            expensive.Stock["staple_food"] = 500;
            expensive.Quotes["staple_food"] = 130;
            var service = new StoreRestockService();

            service.EvaluateRestock(port, new List<IUpstreamGoodsSupplier> { expensive }, 0, 0.5f);
            Assert.AreEqual(0, service.Orders.Count, "A quote above the selling price must not become an order.");

            expensive.Quotes["staple_food"] = 120;
            service.EvaluateRestock(port, new List<IUpstreamGoodsSupplier> { expensive }, 0, 0.5f);
            Assert.AreEqual(0, service.Orders.Count, "A break-even quote must not become an order either: transport would already lose money.");
        }

        [Test]
        public void NoDuplicateOpenOrder_SecondReviewPlacesNothing()
        {
            var port = StoreWith("staple_food", 20, 100, 120);
            var upstream = new FakeUpstreamSupplier();
            upstream.Stock["staple_food"] = 500;
            upstream.Quotes["staple_food"] = 80;
            var service = new StoreRestockService();

            service.EvaluateRestock(port, new List<IUpstreamGoodsSupplier> { upstream }, 0, 0.5f);
            service.EvaluateRestock(port, new List<IUpstreamGoodsSupplier> { upstream }, 0, 0.5f);
            Assert.AreEqual(1, service.Orders.Count, "An open order per category must never duplicate.");
        }

        [Test]
        public void Consolidation_SameDaySameSupplierOrders_ShareOneShipmentGroup()
        {
            var port = StoreWith("staple_food", 20, 100, 120);
            port.OfferList.Add(new GeneralStoreCategoryOffer
            {
                CategoryId = "household_goods",
                OfferedItemIds = new List<string>(),
                StoreUnitsPerItemUnit = 1,
            });
            port.Stock["household_goods"] = 10;
            port.Targets["household_goods"] = 50;
            port.Prices["household_goods"] = 90;
            var upstream = new FakeUpstreamSupplier();
            upstream.Stock["staple_food"] = 500;
            upstream.Stock["household_goods"] = 500;
            upstream.Quotes["staple_food"] = 80;
            upstream.Quotes["household_goods"] = 60;
            var service = new StoreRestockService();

            service.EvaluateRestock(port, new List<IUpstreamGoodsSupplier> { upstream }, 0, 0.5f);
            Assert.AreEqual(2, service.Orders.Count, "Each category earns its own order independently.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(service.Orders[0].ShipmentGroupId));
            Assert.AreEqual(service.Orders[0].ShipmentGroupId, service.Orders[1].ShipmentGroupId,
                "Two same-day orders from one supplier must share one freight movement.");
        }

        [Test]
        public void Consolidation_SingletonOrdersStayUngrouped()
        {
            var port = StoreWith("staple_food", 20, 100, 120);
            port.OfferList.Add(new GeneralStoreCategoryOffer
            {
                CategoryId = "household_goods",
                OfferedItemIds = new List<string>(),
                StoreUnitsPerItemUnit = 1,
            });
            port.Stock["household_goods"] = 10;
            port.Targets["household_goods"] = 50;
            port.Prices["household_goods"] = 90;
            var cheapFood = new FakeUpstreamSupplier { SupplierId = "up-food", SupplierName = "Food Wholesaler" };
            cheapFood.Stock["staple_food"] = 500;
            cheapFood.Quotes["staple_food"] = 80;
            var cheapGoods = new FakeUpstreamSupplier { SupplierId = "up-goods", SupplierName = "Goods Wholesaler" };
            cheapGoods.Stock["household_goods"] = 500;
            cheapGoods.Quotes["household_goods"] = 60;
            var service = new StoreRestockService();

            service.EvaluateRestock(port, new List<IUpstreamGoodsSupplier> { cheapFood, cheapGoods }, 0, 0.5f);
            Assert.AreEqual(2, service.Orders.Count);
            Assert.IsTrue(string.IsNullOrWhiteSpace(service.Orders[0].ShipmentGroupId));
            Assert.IsTrue(string.IsNullOrWhiteSpace(service.Orders[1].ShipmentGroupId),
                "One order per supplier is not a consolidation opportunity.");
        }

        [Test]
        public void UnaffordableReceipt_WaitsWithoutBookingStock()
        {
            var port = StoreWith("staple_food", 20, 100, 120);
            port.CashBalance = 100;
            var upstream = new FakeUpstreamSupplier();
            upstream.Stock["staple_food"] = 500;
            upstream.Quotes["staple_food"] = 80;
            var service = new StoreRestockService();

            service.EvaluateRestock(port, new List<IUpstreamGoodsSupplier> { upstream }, 0, 0.5f);
            Assert.AreEqual(1, service.Orders.Count, "Ordering is not blocked by today's cash: lead time lets sales fund it.");
            service.AdvanceDay(port, new List<IUpstreamGoodsSupplier> { upstream }, 2, new List<string>());
            Assert.AreEqual(20, port.CategoryStockUnits("staple_food"), "No stock books without payment.");
            Assert.AreEqual(100, port.CashBalance);
            Assert.AreEqual(StoreProcurementStage.InTransit, service.Orders[0].Stage,
                "The delivery waits at the dock honestly instead of vanishing or conjuring stock.");
        }
    }
}
