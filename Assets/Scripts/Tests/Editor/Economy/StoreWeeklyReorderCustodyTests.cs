// Plan 05 W0 (Obj 48/47): characterization tests pinning the CURRENT
// store weekly-reorder behaviour before W1 changes it. The manager and
// logistics files that drive the weekly path are harness-excluded, so the
// harness reaches the path through the same seams as
// WeeklyOffMapReorderStateTests: BusinessRuntimeState (queue / allocate /
// receive / cash) and the pure StoreWeeklyReorderPlanner (the daily-chain
// outstanding-order query + the weekly allocation decision).
//
// What is pinned here:
//  1. An anonymous reorder receipt stocks the store and debits buyer cash
//     (the ReceiveOffMapReorderShipment state seam: ApplyDeliveredShipment
//     + SpendCents). The "no seller credited" half is SOURCE-VERIFIED, not
//     harness-pinned: LogisticsRuntimeManager.TryApplyDelivery case
//     ReceivePendingReorder (:1210-1238) has no CreditSellerRevenue call,
//     while the sibling GeneralStoreLocalSupply branch (:1190-1202) does.
//     W1 flips this test's successor: the named-supplier order carries the
//     seller revenue on the shipment and the branch calls
//     CreditSellerRevenue.
//  2. The daily-chain order and the weekly pending quantity for the same
//     category are computed independently: with 30u already ordered daily
//     and 50u pending weekly, the weekly allocation is STILL 50 — the two
//     numbers disagree, and the town orders 80 for a need of 50. This is
//     the Obj 47 double-order hazard, pinned as a characterization of the
//     hazard, not as behaviour to preserve. W1 flips
//     WeeklyAllocationUnits to max(0, pending - outstanding) and this test
//     with it.
//  3. The outstanding-order query itself (open orders + in-transit
//     deliveries, counted once, received/other-category excluded) — the
//     seam W1 wires the weekly step to.
using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    [TestFixture]
    public sealed class StoreWeeklyReorderCustodyTests
    {
        private sealed class FakeStorePort : IGeneralStoreTradingPort
        {
            public string StoreBusinessId { get; set; } = "store-1";
            public string StoreName => "General Store";
            public string LocationId => "store";
            public Dictionary<string, int> Stock = new Dictionary<string, int>();
            public Dictionary<string, int> Prices = new Dictionary<string, int>();
            public Dictionary<string, int> Targets = new Dictionary<string, int>();
            public StoreTradingCapability Capability { get; set; } = new StoreTradingCapability();
            public List<GeneralStoreCategoryOffer> OfferList = new List<GeneralStoreCategoryOffer>();
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
                return false;
            }

            public void RecordCashSettlement(string categoryId, int unitsSold, int revenueCents) { }
            public void RecordCreditSettlement(string categoryId, int unitsSold, int amountCents, string obligationId) { }

            public string SpendCash(int amountCents, string purpose)
            {
                int spend = Math.Max(0, amountCents);
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
                return target > 0 ? Math.Max(0, target - CategoryStockUnits(categoryId)) : int.MaxValue;
            }

            public int DeliveryFeeCents(string itemId, int units) => 0;
        }

        private sealed class FakeUpstreamSupplier : IUpstreamGoodsSupplier
        {
            public string SupplierId { get; set; } = "up-1";
            public string SupplierName { get; set; } = "Wholesaler";
            public Dictionary<string, int> Stock = new Dictionary<string, int>();
            public Dictionary<string, int> Quotes = new Dictionary<string, int>();
            public int LeadDays = 1;

            public int QuoteUnitCostCents(string categoryId) => Quotes.TryGetValue(categoryId, out int q) ? q : 0;
            public int LeadTimeDays(string categoryId) => LeadDays;
            public int AvailableUnits(string categoryId) => Stock.TryGetValue(categoryId, out int s) ? s : 0;

            public int Ship(string categoryId, int units, int dayIndex, List<string> diagnostics)
            {
                int have = AvailableUnits(categoryId);
                int shipped = Math.Min(Math.Max(0, units), have);
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

        private static FakeUpstreamSupplier SupplierWith(string supplierId, string categoryId, int stock, int quote)
        {
            var supplier = new FakeUpstreamSupplier { SupplierId = supplierId, SupplierName = supplierId };
            supplier.Stock[categoryId] = stock;
            supplier.Quotes[categoryId] = quote;
            return supplier;
        }

        // 1. Anonymous receipt (current path): stocks + debits buyer cash.
        // No seller is named anywhere in this seam — the receipt carries no
        // supplier id and no seller revenue; the LRM branch that applies it
        // never calls CreditSellerRevenue (source-verified, see header).
        [Test]
        public void AnonymousReceipt_StocksStoreAndDebitsBuyerCash_WithNoSellerNamed()
        {
            var state = new BusinessRuntimeState();
            state.EnsureCategoryStock("staple_food", 50, 100);
            state.SetCurrentCashCents(10000);
            state.GetCategoryStock("staple_food").QueueReorderToTarget();
            Assert.AreEqual(50, state.GetCategoryStock("staple_food").PendingReorderUnits);

            int accepted = state.ApplyDeliveredShipment("staple_food", 30);
            state.SpendCents(accepted * 20);

            Assert.AreEqual(30, accepted);
            Assert.AreEqual(80, state.GetCategoryStock("staple_food").CurrentStockUnits);
            Assert.AreEqual(10000 - 600, state.CurrentCashCents);
        }

        // 2. THE HAZARD PIN: daily order + weekly pending computed
        // independently. 30u already ordered by the daily chain, 50u pending
        // weekly — the weekly allocation is still the full 50.
        [Test]
        public void DailyOrder_AndWeeklyPending_ComputedIndependently_DoubleOrderHazard()
        {
            var service = new StoreRestockService();
            FakeStorePort port = StoreWith("staple_food", 50, 100, 120);
            var supplier = SupplierWith("up-1", "staple_food", 30, 50);
            var suppliers = new List<IUpstreamGoodsSupplier> { supplier };

            service.EvaluateRestock(port, suppliers, 0, 0.5f);
            Assert.AreEqual(1, service.Orders.Count);
            Assert.AreEqual(30, service.Orders[0].UnitsOrdered);

            var state = new BusinessRuntimeState();
            state.EnsureCategoryStock("staple_food", 50, 100);
            state.GetCategoryStock("staple_food").QueueReorderToTarget();
            int pending = state.GetCategoryStock("staple_food").PendingReorderUnits;
            Assert.AreEqual(50, pending);

            int outstanding = StoreWeeklyReorderPlanner.OutstandingOrderedUnits(service, "staple_food");
            Assert.AreEqual(30, outstanding);

            // Current behaviour: the weekly allocation ignores the
            // outstanding daily order entirely.
            int allocation = StoreWeeklyReorderPlanner.WeeklyAllocationUnits(pending, outstanding);
            Assert.AreEqual(50, allocation);
            Assert.AreEqual(80, allocation + outstanding,
                "Weekly allocation + daily outstanding exceeds the 50u need: the double-order hazard.");
        }

        // 3. Outstanding counts an open (Ordered) daily order.
        [Test]
        public void OutstandingOrderedUnits_CountsOpenDailyOrder()
        {
            var service = new StoreRestockService();
            FakeStorePort port = StoreWith("staple_food", 20, 100, 120);
            var supplier = SupplierWith("up-1", "staple_food", 100, 50);
            service.EvaluateRestock(port, new List<IUpstreamGoodsSupplier> { supplier }, 0, 0.5f);

            Assert.AreEqual(80, StoreWeeklyReorderPlanner.OutstandingOrderedUnits(service, "staple_food"));
            Assert.AreEqual(0, StoreWeeklyReorderPlanner.OutstandingOrderedUnits(service, "meat"));
            Assert.AreEqual(0, StoreWeeklyReorderPlanner.OutstandingOrderedUnits(null, "staple_food"));
        }

        // 4. Outstanding counts an in-transit delivery exactly once (the
        // order is InTransit, not Ordered, so it must not double-count),
        // and drops to zero once received.
        [Test]
        public void OutstandingOrderedUnits_CountsInTransitOnce_ThenZeroWhenReceived()
        {
            var service = new StoreRestockService();
            FakeStorePort port = StoreWith("staple_food", 20, 100, 120);
            port.CashBalance = 0; // receipt waits: cannot afford, delivery stays in transit
            var supplier = SupplierWith("up-1", "staple_food", 100, 50);
            var suppliers = new List<IUpstreamGoodsSupplier> { supplier };
            var diagnostics = new List<string>();

            service.EvaluateRestock(port, suppliers, 0, 0.5f);
            Assert.AreEqual(80, StoreWeeklyReorderPlanner.OutstandingOrderedUnits(service, "staple_food"));

            service.AdvanceDay(port, suppliers, 1, diagnostics);
            Assert.AreEqual(StoreProcurementStage.InTransit, service.Orders[0].Stage);
            Assert.AreEqual(1, service.Deliveries.Count);
            Assert.IsTrue(!service.Deliveries[0].Received);
            Assert.AreEqual(80, StoreWeeklyReorderPlanner.OutstandingOrderedUnits(service, "staple_food"),
                "In-transit delivery counted once, not once per order + once per delivery.");

            port.CashBalance = 50000;
            service.AdvanceDay(port, suppliers, 1, diagnostics);
            Assert.IsTrue(service.Deliveries[0].Received);
            Assert.AreEqual(0, StoreWeeklyReorderPlanner.OutstandingOrderedUnits(service, "staple_food"));
            Assert.AreEqual(100, port.CategoryStockUnits("staple_food"));
        }

        // 5. Weekly allocation never goes negative, even when the daily
        // chain already ordered more than the pending quantity. (Current
        // behaviour returns pending regardless; the W1 flip clamps at 0.)
        [Test]
        public void WeeklyAllocation_NeverNegative()
        {
            Assert.AreEqual(5, StoreWeeklyReorderPlanner.WeeklyAllocationUnits(5, 30));
            Assert.AreEqual(0, StoreWeeklyReorderPlanner.WeeklyAllocationUnits(0, 10));
            Assert.AreEqual(0, StoreWeeklyReorderPlanner.WeeklyAllocationUnits(-5, 0));
        }

        // 6. Named-supplier choice: cheapest DELIVERED cost wins, even when
        // another supplier's goods quote is cheaper.
        [Test]
        public void ChooseNamedSupplier_PicksCheapestDeliveredCost_WithStock()
        {
            var near = SupplierWith("near", "staple_food", 100, 50);
            var far = SupplierWith("far", "staple_food", 100, 40);
            var suppliers = new List<IUpstreamGoodsSupplier> { near, far };
            Func<IUpstreamGoodsSupplier, string, string, int, int> delivered =
                (s, categoryId, itemId, units) => s.QuoteUnitCostCents(categoryId)
                    + (string.Equals(s.SupplierId, "near", StringComparison.Ordinal) ? 10 : 30);

            bool found = StoreWeeklyReorderPlanner.TryChooseNamedSupplier(
                suppliers, "staple_food", 20, delivered,
                out IUpstreamGoodsSupplier chosen, out int goods, out int deliveredCost);

            Assert.IsTrue(found);
            Assert.AreEqual("near", chosen.SupplierId);
            Assert.AreEqual(50, goods);
            Assert.AreEqual(60, deliveredCost);
        }

        // 7. No supplier can ship: empty list, zero stock, zero quote, or an
        // unpriceable delivered cost all refuse (false) — the caller must
        // refuse loudly, never conjure.
        [Test]
        public void ChooseNamedSupplier_NoneWhenNoSupplierCanShip()
        {
            Func<IUpstreamGoodsSupplier, string, string, int, int> delivered =
                (s, categoryId, itemId, units) => s.QuoteUnitCostCents(categoryId) + 5;

            Assert.IsTrue(!StoreWeeklyReorderPlanner.TryChooseNamedSupplier(
                new List<IUpstreamGoodsSupplier>(), "staple_food", 10, delivered,
                out _, out _, out _));

            var empty = SupplierWith("empty", "staple_food", 0, 50);
            Assert.IsTrue(!StoreWeeklyReorderPlanner.TryChooseNamedSupplier(
                new List<IUpstreamGoodsSupplier> { empty }, "staple_food", 10, delivered,
                out _, out _, out _));

            var free = SupplierWith("free", "staple_food", 100, 0);
            Assert.IsTrue(!StoreWeeklyReorderPlanner.TryChooseNamedSupplier(
                new List<IUpstreamGoodsSupplier> { free }, "staple_food", 10, delivered,
                out _, out _, out _));

            var unpriceable = SupplierWith("up-1", "staple_food", 100, 50);
            Assert.IsTrue(!StoreWeeklyReorderPlanner.TryChooseNamedSupplier(
                new List<IUpstreamGoodsSupplier> { unpriceable }, "staple_food", 10,
                (s, categoryId, itemId, units) => 0,
                out _, out _, out _));

            Assert.IsTrue(!StoreWeeklyReorderPlanner.TryChooseNamedSupplier(
                new List<IUpstreamGoodsSupplier> { unpriceable }, "staple_food", 0, delivered,
                out _, out _, out _));
        }
    }
}
