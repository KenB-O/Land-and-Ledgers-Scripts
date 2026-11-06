// Plan 05 W0/W1 (Obj 48/47): characterization tests pinning the store
// weekly-reorder behaviour, flipped in W1 to the named-supplier path. The
// manager and logistics files that drive the weekly path are
// harness-excluded, so the harness reaches the path through the same seams
// as WeeklyOffMapReorderStateTests: BusinessRuntimeState (queue / allocate
// / receive / cash) and the pure StoreWeeklyReorderPlanner (the daily-chain
// outstanding-order query, the weekly allocation decision, the
// named-supplier choice, the shipment terms, and the named receipt).
//
// What is pinned here:
//  1. (W0) An anonymous reorder receipt stocks the store and debits buyer
//     cash (the ReceiveOffMapReorderShipment state seam). The "no seller
//     credited" half is SOURCE-VERIFIED, not harness-pinned: pre-W1,
//     LogisticsRuntimeManager.TryApplyDelivery case ReceivePendingReorder
//     (:1210-1238) had no CreditSellerRevenue call, while the sibling
//     GeneralStoreLocalSupply branch (:1190-1202) did. W1 adds the call to
//     the ReceivePendingReorder branch (source-reviewed) and the named
//     receipt below pins the seller side at the planner seam.
//  2. (W1) The daily-chain order and the weekly pending quantity for the
//     same category are reconciled by the shared outstanding-order check:
//     30u ordered daily + 50u pending weekly -> weekly allocation 20.
//  3. The outstanding-order query itself (open orders + in-transit
//     deliveries, counted once, received/other-category excluded).
//  4. (W1) A named-supplier reorder: the supplier's real stock decrements
//     exactly once, the seller revenue is recorded exactly once on the
//     shipment terms, the buyer cash debits exactly once, and the store
//     stocks exactly the shipped units; a short shipment keeps the
//     shortfall owed by the shipment (Partial retry) instead of
//     re-queuing it to pending; a supplier that cannot ship refuses with
//     the reason named.
//  5. (W1) The anonymous fallback refuses loudly: the uncovered-category
//     reason names the category and the registered-supplier count, and no
//     shipment is created for it (the no-shipment half is the weekly
//     step's, source-reviewed in GeneralStoreRuntimeManager).
using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Population;
using LandLedgers.World.Regional;
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

        // 2. THE SHARED OUTSTANDING-ORDER CHECK (W1, Obj 47 — flipped from
        // W0): daily order + weekly pending are now reconciled. 30u already
        // ordered by the daily chain, 50u pending weekly — the weekly
        // allocation is the remaining 20, so the town orders 50 total for a
        // need of 50, never 80.
        [Test]
        public void DailyOrder_ReducesWeeklyAllocation_SharedOutstandingCheck()
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

            int allocation = StoreWeeklyReorderPlanner.WeeklyAllocationUnits(pending, outstanding);
            Assert.AreEqual(20, allocation);
            Assert.AreEqual(50, allocation + outstanding,
                "Weekly allocation + daily outstanding equals the 50u need exactly: no double-order.");
            Assert.AreEqual(0, StoreWeeklyReorderPlanner.WeeklyAllocationUnits(10, 30),
                "A daily order covering the whole need allocates zero weekly.");
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

        // 5. Weekly allocation never goes negative: when the daily chain
        // already ordered more than the pending quantity, the weekly
        // allocation is zero (W1 flip — W0 pinned the pre-fix value 5).
        [Test]
        public void WeeklyAllocation_NeverNegative()
        {
            Assert.AreEqual(0, StoreWeeklyReorderPlanner.WeeklyAllocationUnits(5, 30));
            Assert.AreEqual(0, StoreWeeklyReorderPlanner.WeeklyAllocationUnits(0, 10));
            Assert.AreEqual(0, StoreWeeklyReorderPlanner.WeeklyAllocationUnits(-5, 0));
            Assert.AreEqual(20, StoreWeeklyReorderPlanner.WeeklyAllocationUnits(50, 30));
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

        // 8. (W1) Named-supplier reorder, all three ledgers, exactly once:
        // a real regional adapter's stock decrements by exactly the ordered
        // units, the shipment terms record the seller revenue exactly once
        // (units x goods quote), the buyer cash debits exactly once, and
        // the store stocks exactly the shipped units.
        [Test]
        public void NamedSupplierReceipt_MovesAllThreeLedgersExactlyOnce()
        {
            var adapter = new RegionalSupplierAdapter(RegionalBusinessRegistry.AshfordMercantile);
            int stockBefore = adapter.AvailableUnits("staple_food");
            int goodsQuote = adapter.QuoteUnitCostCents("staple_food");
            Assert.IsTrue(stockBefore >= 20);
            Assert.IsTrue(goodsQuote > 0);

            var state = new BusinessRuntimeState();
            state.EnsureCategoryStock("staple_food", 50, 100);
            state.SetCurrentCashCents(10000);
            state.GetCategoryStock("staple_food").QueueReorderToTarget();
            Assert.AreEqual(50, state.GetCategoryStock("staple_food").PendingReorderUnits);

            Func<IUpstreamGoodsSupplier, string, string, int, int> delivered =
                (s, categoryId, itemId, units) => s.QuoteUnitCostCents(categoryId) + 5;
            bool found = StoreWeeklyReorderPlanner.TryChooseNamedSupplier(
                new List<IUpstreamGoodsSupplier> { adapter }, "staple_food", 20, delivered,
                out IUpstreamGoodsSupplier chosen, out int chosenGoods, out int chosenDelivered);
            Assert.IsTrue(found);
            Assert.AreEqual(adapter.SupplierId, chosen.SupplierId);
            Assert.AreEqual(goodsQuote, chosenGoods);
            Assert.AreEqual(goodsQuote + 5, chosenDelivered);

            int landedCost = 25;
            StoreWeeklyReorderPlanner.WeeklyReorderShipmentTerms terms =
                StoreWeeklyReorderPlanner.BuildShipmentTerms(chosen, "staple_food", 20, chosenGoods, landedCost, chosenDelivered);
            Assert.IsTrue(terms != null);
            Assert.AreEqual(adapter.SupplierId, terms.SupplierId);
            Assert.AreEqual("Ashford General Mercantile", terms.SupplierName);
            Assert.AreEqual(20, terms.Units);
            Assert.AreEqual(landedCost, terms.BuyerUnitCostCents);
            Assert.AreEqual(chosenDelivered, terms.DeliveredCostCentsPerUnit);
            Assert.AreEqual(20 * goodsQuote, terms.SellerRevenueCents);

            var diagnostics = new List<string>();
            StoreWeeklyReorderPlanner.NamedSupplierReceiptResult receipt =
                StoreWeeklyReorderPlanner.ReceiveNamedSupplierShipment(
                    adapter, state, "staple_food", 20, landedCost, 3, diagnostics);
            Assert.AreEqual(20, receipt.ShippedUnits);
            Assert.AreEqual(20, receipt.AcceptedUnits);
            Assert.AreEqual(20 * landedCost, receipt.BuyerSpendCents);
            Assert.AreEqual(0, receipt.ShortfallUnits);
            Assert.AreEqual(string.Empty, receipt.RefusalReason);

            Assert.AreEqual(stockBefore - 20, adapter.AvailableUnits("staple_food"),
                "Supplier stock decremented exactly once by the ordered units.");
            Assert.AreEqual(70, state.GetCategoryStock("staple_food").CurrentStockUnits);
            Assert.AreEqual(10000 - 20 * landedCost, state.CurrentCashCents,
                "Buyer cash debited exactly once.");
            Assert.AreEqual(30, state.GetCategoryStock("staple_food").PendingReorderUnits);
        }

        // 9. (W1) A short shipment keeps the shortfall owed by the shipment
        // (it is NOT re-queued to pending — the delivery branch marks
        // Partial and re-delivers the remainder, so re-queuing would
        // double-order). The supplier holds 10u of a 20u order, 10u arrive;
        // the supplier restocks (weekly production) and the retry delivers
        // the remaining 10u; each ledger moves exactly once per receipt.
        [Test]
        public void NamedSupplierReceipt_PartialShip_ShortfallStaysOwedByShipment()
        {
            var supplier = SupplierWith("up-1", "staple_food", 10, 50);
            var state = new BusinessRuntimeState();
            state.EnsureCategoryStock("staple_food", 50, 100);
            state.SetCurrentCashCents(10000);
            state.GetCategoryStock("staple_food").QueueReorderToTarget();
            Assert.AreEqual(20, state.AllocatePendingReorderUnits("staple_food", 20));
            Assert.AreEqual(30, state.GetCategoryStock("staple_food").PendingReorderUnits);

            StoreWeeklyReorderPlanner.NamedSupplierReceiptResult first =
                StoreWeeklyReorderPlanner.ReceiveNamedSupplierShipment(
                    supplier, state, "staple_food", 20, 25, 3, new List<string>());

            Assert.AreEqual(10, first.ShippedUnits);
            Assert.AreEqual(10, first.AcceptedUnits);
            Assert.AreEqual(250, first.BuyerSpendCents);
            Assert.AreEqual(10, first.ShortfallUnits);
            Assert.AreEqual(60, state.GetCategoryStock("staple_food").CurrentStockUnits);
            Assert.AreEqual(9750, state.CurrentCashCents);
            Assert.AreEqual(30, state.GetCategoryStock("staple_food").PendingReorderUnits,
                "Pending stays at 30: the 10 unshipped units are still owed by the shipment, not re-queued.");
            Assert.AreEqual(0, supplier.AvailableUnits("staple_food"));

            // Retry (the shipment's remainingQuantityUnits): the supplier
            // restocked 10u, so the remainder ships and is received.
            supplier.Stock["staple_food"] = 10;
            StoreWeeklyReorderPlanner.NamedSupplierReceiptResult retry =
                StoreWeeklyReorderPlanner.ReceiveNamedSupplierShipment(
                    supplier, state, "staple_food", 10, 25, 4, new List<string>());

            Assert.AreEqual(10, retry.ShippedUnits);
            Assert.AreEqual(10, retry.AcceptedUnits);
            Assert.AreEqual(0, retry.ShortfallUnits);
            Assert.AreEqual(70, state.GetCategoryStock("staple_food").CurrentStockUnits);
            Assert.AreEqual(9500, state.CurrentCashCents);
            Assert.AreEqual(30, state.GetCategoryStock("staple_food").PendingReorderUnits,
                "All 20 allocated units arrived; pending is the remaining 30u shortfall to target.");
        }

        // 10. (W1) A supplier that cannot ship refuses with the reason named
        // and moves nothing: no stock, no cash, no supplier decrement.
        [Test]
        public void NamedSupplierReceipt_RefusesWhenSupplierCannotShip()
        {
            var supplier = SupplierWith("up-1", "staple_food", 0, 50);
            var state = new BusinessRuntimeState();
            state.EnsureCategoryStock("staple_food", 50, 100);
            state.SetCurrentCashCents(10000);

            StoreWeeklyReorderPlanner.NamedSupplierReceiptResult receipt =
                StoreWeeklyReorderPlanner.ReceiveNamedSupplierShipment(
                    supplier, state, "staple_food", 20, 25, 3, new List<string>());

            Assert.AreEqual(0, receipt.AcceptedUnits);
            Assert.AreEqual(0, receipt.ShippedUnits);
            Assert.AreEqual(0, receipt.BuyerSpendCents);
            Assert.IsTrue(receipt.RefusalReason.Contains("up-1") || receipt.RefusalReason.Contains("Wholesaler"),
                "Refusal names the supplier: " + receipt.RefusalReason);
            Assert.IsTrue(receipt.RefusalReason.Contains("staple_food"),
                "Refusal names the category: " + receipt.RefusalReason);
            Assert.AreEqual(50, state.GetCategoryStock("staple_food").CurrentStockUnits);
            Assert.AreEqual(10000, state.CurrentCashCents);

            StoreWeeklyReorderPlanner.NamedSupplierReceiptResult nullReceipt =
                StoreWeeklyReorderPlanner.ReceiveNamedSupplierShipment(
                    null, state, "staple_food", 20, 25, 3, new List<string>());
            Assert.AreEqual(0, nullReceipt.AcceptedUnits);
            Assert.IsTrue(nullReceipt.RefusalReason.Length > 0);
            Assert.IsTrue(StoreWeeklyReorderPlanner.BuildShipmentTerms(null, "staple_food", 20, 50, 25, 30) == null);
        }

        // 11. (W1) The anonymous-fallback refusal names the category and the
        // registered-supplier gap — the loud signal the weekly step logs
        // instead of silently conjuring.
        [Test]
        public void UncoveredCategoryReason_NamesCategoryAndSupplierGap()
        {
            string reason = StoreWeeklyReorderPlanner.UncoveredCategoryReason("staple_food", 12);
            Assert.IsTrue(reason.Contains("staple_food"), reason);
            Assert.IsTrue(reason.Contains("12"), reason);
            Assert.IsTrue(reason.Contains("refused"), reason);

            string empty = StoreWeeklyReorderPlanner.UncoveredCategoryReason("medicine_remedies", 0);
            Assert.IsTrue(empty.Contains("medicine_remedies"), empty);
            Assert.IsTrue(empty.Contains("0 suppliers"), empty);
        }
    }
}
