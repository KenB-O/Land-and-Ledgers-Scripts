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

        [Test]
        public void ShipShort_CancelsOrderWhenSupplierShipsNothing()
        {
            var port = StoreWith("staple_food", 20, 100, 120);
            var upstream = new FakeUpstreamSupplier();
            upstream.Stock["staple_food"] = 500;
            upstream.Quotes["staple_food"] = 80;
            var service = new StoreRestockService();

            service.EvaluateRestock(port, new List<IUpstreamGoodsSupplier> { upstream }, 0, 0.5f);
            Assert.AreEqual(1, service.Orders.Count);
            StoreProcurementOrder order = service.Orders[0];
            Assert.AreEqual(80, order.UnitsOrdered);

            upstream.Stock["staple_food"] = 0;
            var diagnostics = new List<string>();
            service.AdvanceDay(port, new List<IUpstreamGoodsSupplier> { upstream }, 2, diagnostics);

            Assert.AreEqual(0, service.Deliveries.Count, "A shipment of nothing creates no delivery.");
            Assert.AreEqual(StoreProcurementStage.Cancelled, order.Stage);
            Assert.IsTrue(order.CancelReason.Contains("shipped nothing"),
                "Cancel reason names the empty shipment, got: " + order.CancelReason);
            string log = string.Join(" ", diagnostics);
            Assert.IsTrue(log.Contains("cancelled"), "Diagnostics record the cancellation, got: " + log);
            Assert.IsTrue(log.Contains("No stock conjured"), "Diagnostics confirm nothing was invented, got: " + log);
            Assert.AreEqual(50000, port.CashBalance, "Ordering plus a failed shipment moves no money.");
            Assert.AreEqual(20, port.CategoryStockUnits("staple_food"), "Ordering plus a failed shipment moves no goods.");
        }

        [Test]
        public void ShipShort_PartialShipmentDeliversOnlyWhatShipped()
        {
            var port = StoreWith("staple_food", 20, 100, 120);
            var upstream = new FakeUpstreamSupplier();
            upstream.Stock["staple_food"] = 500;
            upstream.Quotes["staple_food"] = 80;
            var service = new StoreRestockService();

            service.EvaluateRestock(port, new List<IUpstreamGoodsSupplier> { upstream }, 0, 0.5f);
            StoreProcurementOrder order = service.Orders[0];

            upstream.Stock["staple_food"] = 30;
            service.AdvanceDay(port, new List<IUpstreamGoodsSupplier> { upstream }, 2, new List<string>());

            Assert.AreEqual(1, service.Deliveries.Count, "A short shipment creates exactly one delivery.");
            Assert.IsTrue(service.Deliveries[0].Received,
                "The 30u delivery is fully received the same day (30u fits the 80u of shelf room).");
            Assert.AreEqual(0, service.Deliveries[0].Units,
                "A fully received delivery holds no units back; the 30u shipped is proven by the payment and stock below.");
            Assert.AreEqual(StoreProcurementStage.Received, order.Stage);
            Assert.AreEqual(50000 - 30 * 80, port.CashBalance, "Only the 30u actually shipped are paid for.");
            Assert.AreEqual(20 + 30, port.CategoryStockUnits("staple_food"), "Only the 30u actually shipped are booked.");
            Assert.AreEqual(0, upstream.AvailableUnits("staple_food"), "The supplier was drained by the short shipment.");

            upstream.Stock["staple_food"] = 500;
            service.EvaluateRestock(port, new List<IUpstreamGoodsSupplier> { upstream }, 3, 0.5f);
            Assert.AreEqual(2, service.Orders.Count, "The 50u shortfall self-heals on the next review instead of being silently lost.");
            StoreProcurementOrder second = service.Orders[1];
            Assert.IsTrue(second.OrderId != order.OrderId, "The shortfall order is a new order, not the old one revived.");
            Assert.AreEqual(StoreProcurementStage.Ordered, second.Stage);
            Assert.AreEqual(50, second.UnitsOrdered, "The new order covers exactly the shortfall (100 - 50).");
        }

        [Test]
        public void ReceiveShort_PartialReceiptLeavesRemainderAtDock_ThenCompletesWithoutDoublePay()
        {
            var port = StoreWith("staple_food", 0, 100, 120);
            var upstream = new FakeUpstreamSupplier();
            upstream.Stock["staple_food"] = 500;
            upstream.Quotes["staple_food"] = 80;
            var service = new StoreRestockService();

            service.EvaluateRestock(port, new List<IUpstreamGoodsSupplier> { upstream }, 0, 0.5f);
            Assert.AreEqual(1, service.Orders.Count);
            StoreProcurementOrder order = service.Orders[0];
            Assert.AreEqual(100, order.UnitsOrdered);

            // 90u reached the shelf by another receipt first (direct
            // test manipulation, stated here): only 10u of room remains.
            port.Stock["staple_food"] = 90;
            var diagnostics = new List<string>();
            service.AdvanceDay(port, new List<IUpstreamGoodsSupplier> { upstream }, 2, diagnostics);

            Assert.AreEqual(50000 - 10 * 80, port.CashBalance, "Only the 10u that fit are paid for on day 2.");
            Assert.AreEqual(100, port.CategoryStockUnits("staple_food"), "Only the 10u that fit are booked on day 2.");
            Assert.AreEqual(1, service.Deliveries.Count);
            Assert.AreEqual(90, service.Deliveries[0].Units, "The other 90u stay on the delivery, unreceived.");
            Assert.IsFalse(service.Deliveries[0].Received);
            Assert.AreEqual(StoreProcurementStage.InTransit, order.Stage, "The order stays in transit until its delivery is fully received.");
            string log = string.Join(" ", diagnostics);
            Assert.IsTrue(log.Contains("still at the dock"), "Diagnostics name the remainder honestly, got: " + log);

            port.Targets["staple_food"] = 200;
            service.AdvanceDay(port, new List<IUpstreamGoodsSupplier> { upstream }, 3, new List<string>());

            Assert.IsTrue(service.Deliveries[0].Received, "The remainder is received once the shelf has room.");
            Assert.AreEqual(StoreProcurementStage.Received, order.Stage);
            Assert.AreEqual(50000 - 100 * 80, port.CashBalance,
                "Total paid across both receipts is exactly 100 x 80c — the 10u from day 2 are never paid twice.");
            Assert.AreEqual(190, port.CategoryStockUnits("staple_food"));
            Assert.AreEqual(400, upstream.AvailableUnits("staple_food"), "Supplier stock was debited once, at ship time.");
        }

        [Test]
        public void Order_CancelledWhenSupplierDisappearsBeforeShipDay()
        {
            var port = StoreWith("staple_food", 20, 100, 120);
            var upstream = new FakeUpstreamSupplier();
            upstream.Stock["staple_food"] = 500;
            upstream.Quotes["staple_food"] = 80;
            var service = new StoreRestockService();

            service.EvaluateRestock(port, new List<IUpstreamGoodsSupplier> { upstream }, 0, 0.5f);
            StoreProcurementOrder order = service.Orders[0];

            // Contract test: the production supplier list is fixed, so
            // this pins the defensive branch for a future supplier-set
            // change rather than a live production path.
            service.AdvanceDay(port, new List<IUpstreamGoodsSupplier>(), 2, new List<string>());

            Assert.AreEqual(StoreProcurementStage.Cancelled, order.Stage);
            Assert.IsTrue(order.CancelReason.Contains("gone"),
                "Cancel reason names the missing supplier, got: " + order.CancelReason);
            Assert.AreEqual(0, service.Deliveries.Count, "A cancelled order creates no delivery.");
            Assert.AreEqual(50000, port.CashBalance);
            Assert.AreEqual(20, port.CategoryStockUnits("staple_food"));
        }

        [Test]
        public void CancelledOrder_DoesNotBlockNextReview()
        {
            var port = StoreWith("staple_food", 20, 100, 120);
            var upstream = new FakeUpstreamSupplier();
            upstream.Stock["staple_food"] = 500;
            upstream.Quotes["staple_food"] = 80;
            var service = new StoreRestockService();

            service.EvaluateRestock(port, new List<IUpstreamGoodsSupplier> { upstream }, 0, 0.5f);
            StoreProcurementOrder first = service.Orders[0];

            upstream.Stock["staple_food"] = 0;
            service.AdvanceDay(port, new List<IUpstreamGoodsSupplier> { upstream }, 2, new List<string>());
            Assert.AreEqual(StoreProcurementStage.Cancelled, first.Stage);

            upstream.Stock["staple_food"] = 500;
            service.EvaluateRestock(port, new List<IUpstreamGoodsSupplier> { upstream }, 3, 0.5f);

            Assert.AreEqual(2, service.Orders.Count, "Cancellation clears the open-order block, so the category can be re-ordered the next day.");
            StoreProcurementOrder second = service.Orders[1];
            Assert.IsTrue(second.OrderId != first.OrderId, "The re-order is a new order.");
            Assert.AreEqual(StoreProcurementStage.Ordered, second.Stage);
            Assert.AreEqual(80, second.UnitsOrdered);
        }

        [Test]
        public void Threshold_HealthExactlyAtThreshold_Orders()
        {
            var port = StoreWith("staple_food", 50, 100, 120);
            var upstream = new FakeUpstreamSupplier();
            upstream.Stock["staple_food"] = 500;
            upstream.Quotes["staple_food"] = 80;
            var service = new StoreRestockService();

            service.EvaluateRestock(port, new List<IUpstreamGoodsSupplier> { upstream }, 0, 0.5f);
            Assert.AreEqual(1, service.Orders.Count, "Health exactly at the threshold orders: the skip is strict '>', so health <= threshold is the order case.");
            Assert.AreEqual(50, service.Orders[0].UnitsOrdered, "The order covers the full shortfall (100 - 50).");
            Assert.AreEqual(StoreProcurementStage.Ordered, service.Orders[0].Stage);
        }

        [Test]
        public void Threshold_HealthJustAboveThreshold_DoesNotOrder()
        {
            var port = StoreWith("staple_food", 51, 100, 120);
            var upstream = new FakeUpstreamSupplier();
            upstream.Stock["staple_food"] = 500;
            upstream.Quotes["staple_food"] = 80;
            var service = new StoreRestockService();

            service.EvaluateRestock(port, new List<IUpstreamGoodsSupplier> { upstream }, 0, 0.5f);
            Assert.AreEqual(0, service.Orders.Count, "One unit over the line and the day passes with no order.");
        }

        [Test]
        public void ThresholdZero_EmptyStore_Orders()
        {
            var port = StoreWith("staple_food", 0, 100, 120);
            var upstream = new FakeUpstreamSupplier();
            upstream.Stock["staple_food"] = 500;
            upstream.Quotes["staple_food"] = 80;
            var service = new StoreRestockService();

            service.EvaluateRestock(port, new List<IUpstreamGoodsSupplier> { upstream }, 0, 0f);
            Assert.AreEqual(1, service.Orders.Count, "Threshold 0 still orders — but only from exactly empty.");
            Assert.AreEqual(100, service.Orders[0].UnitsOrdered);
        }

        [Test]
        public void ThresholdZero_OneUnitInStock_DoesNotOrder()
        {
            var port = StoreWith("staple_food", 1, 100, 120);
            var upstream = new FakeUpstreamSupplier();
            upstream.Stock["staple_food"] = 500;
            upstream.Quotes["staple_food"] = 80;
            var service = new StoreRestockService();

            service.EvaluateRestock(port, new List<IUpstreamGoodsSupplier> { upstream }, 0, 0f);
            Assert.AreEqual(0, service.Orders.Count, "At threshold 0, a single unit on the shelf is enough to hold the order.");
        }

        [Test]
        public void ThresholdNegative_ClampsToZero()
        {
            // Case A: empty store at a negative threshold orders — without
            // the Math.Max(0f, ...) clamp, health 0 > -0.5 would skip.
            var portA = StoreWith("staple_food", 0, 100, 120);
            var upstreamA = new FakeUpstreamSupplier();
            upstreamA.Stock["staple_food"] = 500;
            upstreamA.Quotes["staple_food"] = 80;
            var serviceA = new StoreRestockService();
            serviceA.EvaluateRestock(portA, new List<IUpstreamGoodsSupplier> { upstreamA }, 0, -0.5f);
            Assert.AreEqual(1, serviceA.Orders.Count, "A negative threshold clamps to 0, so an empty store still orders.");
            Assert.AreEqual(100, serviceA.Orders[0].UnitsOrdered);

            // Case B (fresh service): one unit in stock at a negative
            // threshold behaves exactly as threshold 0 — no order.
            var portB = StoreWith("staple_food", 1, 100, 120);
            var upstreamB = new FakeUpstreamSupplier();
            upstreamB.Stock["staple_food"] = 500;
            upstreamB.Quotes["staple_food"] = 80;
            var serviceB = new StoreRestockService();
            serviceB.EvaluateRestock(portB, new List<IUpstreamGoodsSupplier> { upstreamB }, 0, -0.5f);
            Assert.AreEqual(0, serviceB.Orders.Count, "The clamped value behaves exactly as threshold 0.");
        }

        [Test]
        public void ThresholdOne_FullStock_DoesNotOrder_ButOneMissingUnit_Orders()
        {
            // Case A: a full shelf at threshold 1 places no order — the
            // missing <= 0 guard, not a phantom order, stops it.
            var portA = StoreWith("staple_food", 100, 100, 120);
            var upstreamA = new FakeUpstreamSupplier();
            upstreamA.Stock["staple_food"] = 500;
            upstreamA.Quotes["staple_food"] = 80;
            var serviceA = new StoreRestockService();
            serviceA.EvaluateRestock(portA, new List<IUpstreamGoodsSupplier> { upstreamA }, 0, 1.0f);
            Assert.AreEqual(0, serviceA.Orders.Count, "Threshold 1 orders whenever anything is missing — nothing is missing here.");

            // Case B (fresh service): one missing unit at threshold 1 orders 1u.
            // Stated honestly: this pins threshold-1.0 behavior, not the
            // > 1 clamp — the clamp is observationally indistinguishable
            // from the missing guard (see the Slice 25 plan's "Swept").
            var portB = StoreWith("staple_food", 99, 100, 120);
            var upstreamB = new FakeUpstreamSupplier();
            upstreamB.Stock["staple_food"] = 500;
            upstreamB.Quotes["staple_food"] = 80;
            var serviceB = new StoreRestockService();
            serviceB.EvaluateRestock(portB, new List<IUpstreamGoodsSupplier> { upstreamB }, 0, 1.0f);
            Assert.AreEqual(1, serviceB.Orders.Count);
            Assert.AreEqual(1, serviceB.Orders[0].UnitsOrdered);
        }
    }
}
