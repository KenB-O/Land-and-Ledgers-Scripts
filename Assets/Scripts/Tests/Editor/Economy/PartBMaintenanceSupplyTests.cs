using System.Collections.Generic;
using NUnit.Framework;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Core;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Combined Maintenance Pass 06 + 12 + 13, Part B (2026-11-15):
    /// economy supply reliability — horse feed, fuel wood, bakery fuel
    /// and inputs, suppliers and procurement, unit/package conversions.
    ///
    /// Defects pinned here (all confirmed against the checkpoint tree
    /// before the fix; see the pass report for file/line references):
    ///  1. OffMapAidBusinessRuntime.TryPlaceOrder hardcoded
    ///     bakery_flour_sack, so no Timberline fuel row, feed row, or
    ///     any other authored aid-table row could ever be ordered — and
    ///     ImportState refused any lot with BakeryUnits 0 on load, which
    ///     is the legitimate figure for a fuel row.
    ///  2. ExternalMarketAuthority had finite counterparty supply with
    ///     a budget-only restock and NO goods-supply restock, so the
    ///     General Store's opening 20-sack horse-feed order could be
    ///     bought exactly once and never again; the freight horse then
    ///     starves (40 feed units, ~16 working days).
    ///  3. No driver ever reordered horse feed for the store.
    ///
    /// Prices and quantities below are test parameters, never canon —
    /// except the production-terms feed test, which pins the authored
    /// opening order (20 sacks at 15c from Millbrook Grain Co.) as the
    /// terms the standing reorder repeats. Unity compilation, Test
    /// Runner, PlayMode, and the SimulationDrivers wiring are NOT
    /// claimed; SimulationDrivers is harness-excluded, so the driver
    /// call site is source-reviewed only and exercised here through
    /// the same public APIs it calls.
    /// </summary>
    [TestFixture]
    public sealed class PartBMaintenanceSupplyTests
    {
        private const string StoreInstanceId = "general-store-1";
        private const string FeedSku = "store_horse_feed_sack";
        private const string FeedSupplierId = "millbrook_grain_co";

        private sealed class FakeBusinessCashPort : IBusinessCashPort
        {
            public int BalanceCents { get; private set; }
            public FakeBusinessCashPort(int balanceCents) => BalanceCents = balanceCents;
            public string TryDebit(int amountCents, string reason)
            {
                if (amountCents <= 0 || amountCents > BalanceCents)
                    return "test cash port refused debit";
                BalanceCents -= amountCents;
                return null;
            }
            public string Credit(int amountCents, string reason)
            {
                if (amountCents <= 0) return "test cash port refused credit";
                BalanceCents += amountCents;
                return null;
            }
        }

        private static OffMapAidBusinessDefinition TimberlineStyleDefinition()
        {
            // Test parameters, not canon: no Timberline asset exists in
            // the tree (that absence is a documented blocker, not a
            // value this test invents). The shape is what matters — a
            // fuel row with no bakery-unit conversion.
            return new OffMapAidBusinessDefinition
            {
                BusinessId = "timberline_fuel_supply",
                DisplayName = "Timberline Fuel Supply",
                PickupRouteMilesFromTown = 80f,
                Inventory = new List<OffMapAidInventoryRow>
                {
                    new OffMapAidInventoryRow
                    {
                        ItemId = "fuelwood_bundle",
                        DisplayName = "Fuelwood Bundle",
                        InventoryUnit = "bundle",
                        StartingUnits = 100,
                        OrderUnit = "bundle",
                        InventoryUnitsPerOrderUnit = 1,
                        BakeryUnitsPerOrderUnit = 0,
                        OrderPriceCents = 28,
                    },
                },
                Production = new List<OffMapAidProductionRow>
                {
                    new OffMapAidProductionRow
                    {
                        ItemId = "fuelwood_bundle",
                        WeeklyMinimumUnits = 10,
                        WeeklyMaximumUnits = 10,
                        TargetReserveUnits = 100,
                    },
                },
            };
        }

        private static OffMapAidBusinessDefinition MillbrookDefinition()
        {
            // Row values copied verbatim from the authored
            // MillbrookGrainCo.asset (460 lb starting, 48c/sack,
            // 10 lb per sack, 18 bakery units per sack).
            return new OffMapAidBusinessDefinition
            {
                BusinessId = "millbrook_grain_co",
                DisplayName = "Millbrook Grain Co.",
                PickupRouteMilesFromTown = 50f,
                Inventory = new List<OffMapAidInventoryRow>
                {
                    new OffMapAidInventoryRow
                    {
                        ItemId = "bakery_flour_sack",
                        DisplayName = "Flour",
                        InventoryUnit = "lb",
                        StartingUnits = 460,
                        OrderUnit = "10 lb sack",
                        InventoryUnitsPerOrderUnit = 10,
                        BakeryUnitsPerOrderUnit = 18,
                        OrderPriceCents = 48,
                    },
                },
            };
        }

        [Test]
        public void FuelRowCanBeOrderedThroughTheGenericAidTable()
        {
            var supplier = new OffMapAidBusinessRuntime(TimberlineStyleDefinition());
            var cash = new FakeBusinessCashPort(20000);
            var diag = new List<string>();

            Assert.IsNull(supplier.TryPlaceOrderForItem(
                "fuel-order-1", "fuelwood_bundle", "fuel-dealer-1", "Jacob Boniecki",
                10, cash, 1, out OffMapAidCargoLot lot), "a Timberline-style fuel row must be orderable");
            Assert.IsNotNull(lot);
            Assert.AreEqual("fuelwood_bundle", lot.SkuId);
            Assert.AreEqual(10, lot.PackageUnits);
            Assert.AreEqual(10, lot.InventoryUnits);
            Assert.AreEqual(0, lot.BakeryUnits, "a fuel row carries no bakery-unit conversion");
            Assert.AreEqual(280, lot.TotalCostCents);
            Assert.AreEqual(90, supplier.GetInventoryUnits("fuelwood_bundle"));
            Assert.AreEqual(20000 - 280, cash.BalanceCents);
            Assert.AreEqual(280, supplier.CashCents);
            Assert.AreEqual(1, supplier.Lots.Count);
            Assert.AreEqual(1, supplier.CashTransactions.Count);
        }

        [Test]
        public void FuelRowReplenishesWeeklyToItsAuthoredTarget()
        {
            var supplier = new OffMapAidBusinessRuntime(TimberlineStyleDefinition());
            var cash = new FakeBusinessCashPort(20000);
            Assert.IsNull(supplier.TryPlaceOrderForItem(
                "fuel-order-1", "fuelwood_bundle", "fuel-dealer-1", "Jacob Boniecki",
                20, cash, 1, out _));
            Assert.AreEqual(80, supplier.GetInventoryUnits("fuelwood_bundle"));

            Assert.AreEqual(0, supplier.ApplyWeeklyReplenishment(6).Count, "no top-up before day 7");
            supplier.ApplyWeeklyReplenishment(7);
            Assert.AreEqual(90, supplier.GetInventoryUnits("fuelwood_bundle"), "week 1 adds exactly 10");
            supplier.ApplyWeeklyReplenishment(14);
            Assert.AreEqual(100, supplier.GetInventoryUnits("fuelwood_bundle"), "week 2 refills to the 100 target");
            supplier.ApplyWeeklyReplenishment(21);
            Assert.AreEqual(100, supplier.GetInventoryUnits("fuelwood_bundle"), "no top-up above the target");
        }

        [Test]
        public void FuelLotReceivesAtItsDestinationAndRoundTripsThroughSave()
        {
            var supplier = new OffMapAidBusinessRuntime(TimberlineStyleDefinition());
            var cash = new FakeBusinessCashPort(20000);
            Assert.IsNull(supplier.TryPlaceOrderForItem(
                "fuel-order-1", "fuelwood_bundle", "fuel-dealer-1", "Jacob Boniecki",
                10, cash, 1, out OffMapAidCargoLot lot));

            // Pre-fix, ImportState refused this lot on load (BakeryUnits 0).
            var restored = new OffMapAidBusinessRuntime(TimberlineStyleDefinition());
            Assert.IsNull(restored.ImportState(supplier.ExportState()), "a fuel lot must survive save/load");
            Assert.AreEqual(1, restored.Lots.Count);
            Assert.AreEqual(0, restored.Lots[0].BakeryUnits);
            Assert.AreEqual(90, restored.GetInventoryUnits("fuelwood_bundle"));

            Assert.IsNull(restored.CommitFreightPickup(lot.LotId, "shipment-1", 10));
            Assert.IsNull(restored.ReceiveAtDestination(lot.LotId, "shipment-1", "fuel-dealer-1", out int bakeryUnits));
            Assert.AreEqual(0, bakeryUnits);
            Assert.IsTrue(restored.Lots[0].Received);
            Assert.AreEqual("fuel-dealer-1", restored.Lots[0].DestinationBusinessId);
            Assert.IsNull(restored.ReceiveAtDestination(lot.LotId, "shipment-1", "fuel-dealer-1", out _),
                "same-shipment re-receipt is idempotent");
            Assert.IsNotNull(restored.ReceiveAtDestination(lot.LotId, "shipment-2", "fuel-dealer-1", out _),
                "a different shipment must not re-receive the lot");
        }

        [Test]
        public void UnknownAidItemIsRefusedWithoutMovingCashOrStock()
        {
            var supplier = new OffMapAidBusinessRuntime(TimberlineStyleDefinition());
            var cash = new FakeBusinessCashPort(20000);
            Assert.IsNotNull(supplier.TryPlaceOrderForItem(
                "bad-order-1", "not_a_real_item", "fuel-dealer-1", "Jacob Boniecki",
                5, cash, 1, out OffMapAidCargoLot lot));
            Assert.IsNull(lot);
            Assert.AreEqual(20000, cash.BalanceCents);
            Assert.AreEqual(100, supplier.GetInventoryUnits("fuelwood_bundle"));
            Assert.AreEqual(0, supplier.Lots.Count);
        }

        [Test]
        public void FlourOrderPathIsUnchangedByTheGeneralisation()
        {
            var supplier = new OffMapAidBusinessRuntime(MillbrookDefinition());
            var cash = new FakeBusinessCashPort(20000);
            Assert.IsNull(supplier.TryPlaceOrder(
                "opening-flour-b1", "bakery-1", "Glen Pierce", 2, cash, 1, out OffMapAidCargoLot lot));
            Assert.AreEqual("bakery_flour_sack", lot.SkuId);
            Assert.AreEqual(20, lot.InventoryUnits, "2 sacks at 10 lb");
            Assert.AreEqual(36, lot.BakeryUnits, "2 sacks at 18 bakery units");
            Assert.AreEqual(96, lot.TotalCostCents, "2 sacks at 48c");
            Assert.AreEqual(440, supplier.GetInventoryUnits("bakery_flour_sack"));
            Assert.AreEqual(20000 - 96, cash.BalanceCents);

            Assert.IsNotNull(supplier.TryPlaceOrder(
                "opening-flour-b1", "bakery-1", "Glen Pierce", 2, cash, 2, out _),
                "the same request id must not debit twice");
            Assert.AreEqual(20000 - 96, cash.BalanceCents);
            Assert.AreEqual(1, supplier.Lots.Count);
        }

        private static ExternalMarketAuthority FeedMarket(int feedSupplyUnits)
        {
            var market = new ExternalMarketAuthority();
            Assert.IsNull(market.RegisterCounterparty(new ExternalCounterparty
            {
                CounterpartyId = FeedSupplierId,
                DisplayName = "Millbrook Grain Co.",
                TransitDays = 1,
                InitialBudgetCents = 0,
                SupplyUnits = new Dictionary<string, int> { [FeedSku] = feedSupplyUnits },
            }));
            return market;
        }

        private static OffMapNamedSkuProcurementService FeedService(
            ExternalMarketAuthority market, FakeBusinessCashPort storeCash)
        {
            return new OffMapNamedSkuProcurementService(
                StoreInstanceId, market, storeCash);
        }

        [Test]
        public void MarketSupplyRestockEnablesASecondFeedOrder()
        {
            var market = FeedMarket(20);
            var storeCash = new FakeBusinessCashPort(5000);
            var service = FeedService(market, storeCash);
            market.SetDayIndex(0);

            NamedSkuOrder first = service.PlaceOrder(FeedSku, 20, 15, 0, FeedSupplierId, 0, NamedSkuPaymentMode.CashOnArrival, null);
            Assert.IsNotNull(first, "opening order places against the finite 20-sack supply");
            Assert.IsNull(service.PlaceOrder(FeedSku, 20, 15, 0, FeedSupplierId, 0, NamedSkuPaymentMode.CashOnArrival, null),
                "pre-fix behaviour pinned: with supply exhausted, no second order is possible");

            Assert.IsTrue(market.RestockCounterpartySupply(
                FeedSupplierId, FeedSku, 20, "standing horse-feed reorder", new List<string>()),
                "the modeled supply restock must succeed");
            Assert.IsNotNull(service.PlaceOrder(FeedSku, 20, 15, 0, FeedSupplierId, 1, NamedSkuPaymentMode.CashOnArrival, null),
                "after restock, a second order places");
        }

        [Test]
        public void MarketSupplyRestockRefusesUnknownsAndNonPositiveCounts()
        {
            var market = FeedMarket(20);
            var diag = new List<string>();
            Assert.IsFalse(market.RestockCounterpartySupply("no_such_supplier", FeedSku, 20, "test", diag));
            Assert.IsFalse(market.RestockCounterpartySupply(FeedSupplierId, string.Empty, 20, "test", diag));
            Assert.IsFalse(market.RestockCounterpartySupply(FeedSupplierId, FeedSku, 0, "test", diag));
            Assert.IsFalse(market.RestockCounterpartySupply(FeedSupplierId, FeedSku, -5, "test", diag));
            Assert.AreEqual(20, market.GetCounterparty(FeedSupplierId).SupplyUnits[FeedSku],
                "refused restocks change nothing");
        }

        [Test]
        public void RestockedSupplySurvivesMarketSaveRoundTrip()
        {
            var market = FeedMarket(0);
            Assert.IsTrue(market.RestockCounterpartySupply(FeedSupplierId, FeedSku, 20, "test", new List<string>()));
            var restored = new ExternalMarketAuthority();
            restored.ImportState(market.ExportState());
            Assert.AreEqual(20, restored.GetCounterparty(FeedSupplierId).SupplyUnits[FeedSku]);
        }

        private static StoreHorseFeedReorderInputs ReorderInputs(
            OffMapNamedSkuProcurementService service, int storeSacksOnHand, int dayIndex)
        {
            return new StoreHorseFeedReorderInputs
            {
                Service = service,
                SupplierId = StoreHorseFeedReorder.MillbrookSupplierId,
                SkuId = StoreHorseFeedReorder.FeedSkuId,
                UnitPriceCents = StoreHorseFeedReorder.FeedPriceCentsPerSack,
                LotSacks = StoreHorseFeedReorder.OpeningLotSacks,
                StoreSacksOnHand = storeSacksOnHand,
                DayIndex = dayIndex,
            };
        }

        [Test]
        public void FeedReorderDoesNothingWhileAFullLotIsCovered()
        {
            var market = FeedMarket(0);
            var storeCash = new FakeBusinessCashPort(5000);
            var service = FeedService(market, storeCash);
            var diag = new List<string>();

            StoreHorseFeedReorderResult result = StoreHorseFeedReorder.RunDay(ReorderInputs(service, 20, 5), diag);
            Assert.IsFalse(result.Ordered);
            Assert.IsNull(result.Order);
            Assert.AreEqual(0, service.Orders.Count);
            Assert.AreEqual(0, market.GetCounterparty(FeedSupplierId).SupplyUnits[FeedSku],
                "no restock when no order is needed");
        }

        [Test]
        public void FeedReorderPlacesOneLotOnOpeningTermsAndNeverDuplicatesIt()
        {
            var market = FeedMarket(0);
            var storeCash = new FakeBusinessCashPort(5000);
            var service = FeedService(market, storeCash);
            var diag = new List<string>();

            StoreHorseFeedReorderResult result = StoreHorseFeedReorder.RunDay(ReorderInputs(service, 0, 5), diag);
            Assert.IsTrue(result.Ordered);
            Assert.IsNotNull(result.Order);
            Assert.AreEqual(StoreHorseFeedReorder.FeedSkuId, result.Order.SkuId);
            Assert.AreEqual(20, result.Order.UnitsOrdered);
            Assert.AreEqual(15, result.Order.UnitGoodsCostCents);
            Assert.AreEqual(300, result.Order.TotalCostCents, "20 sacks at 15c, no freight surcharge");
            Assert.AreEqual(6, result.Order.ExpectedArrivalDayIndex, "1-day transit from day 5");
            Assert.AreEqual(0, market.GetCounterparty(FeedSupplierId).SupplyUnits[FeedSku],
                "the restocked supply is consumed by the order itself");

            StoreHorseFeedReorderResult duplicate = StoreHorseFeedReorder.RunDay(ReorderInputs(service, 0, 5), diag);
            Assert.IsFalse(duplicate.Ordered, "an outstanding feed order blocks a duplicate");
            Assert.AreEqual(1, service.Orders.Count);
        }

        [Test]
        public void FeedReorderRequestsOnlyTheDeficitToItsShelfTarget()
        {
            var market = FeedMarket(0);
            var storeCash = new FakeBusinessCashPort(5000);
            var service = FeedService(market, storeCash);
            var diag = new List<string>();

            StoreHorseFeedReorderResult result =
                StoreHorseFeedReorder.RunDay(ReorderInputs(service, 13, 5), diag);

            Assert.IsTrue(result.Ordered);
            Assert.AreEqual(7, result.Order.UnitsOrdered,
                "13 physical sacks against the 20-sack target require only seven new sacks.");
            Assert.AreEqual(105, result.Order.TotalCostCents, "only the seven ordered sacks settle at 15c each.");
            Assert.AreEqual(0, market.GetCounterparty(FeedSupplierId).SupplyUnits[FeedSku],
                "the supplier's added stock is consumed only by this priced order.");
            StringAssert.Contains("target coverage 20, already covered 13 sack(s)", string.Join("\n", diag));
        }

        [Test]
        public void PartialFeedReorderWaitsForFundsAtArrivalWithoutCreatingAFreeLot()
        {
            var market = FeedMarket(0);
            var storeCash = new FakeBusinessCashPort(100);
            var service = FeedService(market, storeCash);

            StoreHorseFeedReorderResult result = StoreHorseFeedReorder.RunDay(
                ReorderInputs(service, storeSacksOnHand: 13, dayIndex: 5), new List<string>());
            Assert.IsTrue(result.Ordered, result.Refusal);
            Assert.AreEqual(7, result.Order.UnitsOrdered);
            Assert.AreEqual(105, result.Order.TotalCostCents);

            List<NamedSkuLot> arrivals = service.ReceiveArrivals(6, null);

            Assert.AreEqual(0, arrivals.Count,
                "the seven-sack purchase cannot become stock when the store cannot pay its 105c invoice");
            Assert.AreEqual(NamedSkuOrderStage.WaitingPayment, result.Order.Stage);
            Assert.AreEqual(100, storeCash.BalanceCents, "the refused debit moves no cash");
            Assert.AreEqual(0, service.Lots.Count, "no unpaid feed lot is created");
            Assert.AreEqual(0, market.GetCounterparty(FeedSupplierId)
                .SupplyUnits[FeedSku],
                "the seven finite sacks are committed to the placed trade; payment failure creates no local lot or refund of reserved supplier stock");
        }

        [Test]
        public void FeedReorderEndToEndPaysOnArrivalAndReordersAgainWhenStockRunsOut()
        {
            var market = FeedMarket(20);
            var storeCash = new FakeBusinessCashPort(5000);
            var service = FeedService(market, storeCash);
            market.SetDayIndex(0);

            // Opening order, as the bootstrapper places it; the store
            // transfers the arrived lot to the shelf (modeled here by
            // consuming the service lot, as PurchasedRetailLotReceipt
            // does in production), leaving the store covered until the
            // shelf empties.
            NamedSkuOrder opening = service.PlaceOrder(FeedSku, 20, 15, 0, FeedSupplierId, 0, NamedSkuPaymentMode.CashOnArrival, null);
            Assert.IsNotNull(opening);
            Assert.AreEqual(1, service.ReceiveArrivals(1, null).Count);
            Assert.AreEqual(4700, storeCash.BalanceCents, "300c paid on arrival");
            Assert.IsNotNull(service.ConsumeLots(FeedSku, 20, "test-shelf-transfer", 1));

            // Shelf full: no reorder. Shelf empty: one reorder, paid on arrival.
            Assert.IsFalse(StoreHorseFeedReorder.RunDay(ReorderInputs(service, 20, 2), new List<string>()).Ordered);
            StoreHorseFeedReorderResult reorder =
                StoreHorseFeedReorder.RunDay(ReorderInputs(service, 0, 2), new List<string>());
            Assert.IsTrue(reorder.Ordered);
            Assert.AreEqual(1, service.ReceiveArrivals(3, null).Count, "reorder arrives after 1-day transit");
            Assert.AreEqual(4400, storeCash.BalanceCents, "second 300c paid on arrival, never before");
            Assert.AreEqual(20, service.SellableUnits(FeedSku));
        }

        [Test]
        public void FeedReorderRefusesLoudlyWhenTheSupplierIsNotRegistered()
        {
            // Release builds register no feed counterparty (the feed
            // registration is dev-only): the planner must refuse, never
            // order from nobody and never crash.
            var market = new ExternalMarketAuthority();
            var storeCash = new FakeBusinessCashPort(5000);
            var service = FeedService(market, storeCash);

            StoreHorseFeedReorderResult result =
                StoreHorseFeedReorder.RunDay(ReorderInputs(service, 0, 5), new List<string>());
            Assert.IsFalse(result.Ordered);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.Refusal));
            Assert.AreEqual(0, service.Orders.Count);
        }
    }
}
