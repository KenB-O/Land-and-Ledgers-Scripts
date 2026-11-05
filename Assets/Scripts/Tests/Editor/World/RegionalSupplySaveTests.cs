// Slice 17 (2026-11-05): save/load round trip for the regional supply
// chain. Proves the service's orders/deliveries/sequences and the
// adapters' per-item stock survive capture -> fresh instances -> restore,
// that restoring twice is idempotent, and that invalid saved entries are
// skipped or clamped with a diagnostic instead of trusted.
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Population;
using LandLedgers.World.Regional;
using NUnit.Framework;

namespace LandLedgers.Tests.World
{
    public sealed class RegionalSupplySaveTests
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

        private sealed class FixtureSupplier : IUpstreamGoodsSupplier
        {
            public string SupplierId { get; set; } = "fixture";
            public string SupplierName { get; set; } = "Fixture";
            public Dictionary<string, int> Stock = new Dictionary<string, int>();
            public Dictionary<string, int> Quotes = new Dictionary<string, int>();
            public int QuoteUnitCostCents(string categoryId) => Quotes.TryGetValue(categoryId, out int q) ? q : 0;
            public int LeadTimeDays(string categoryId) => 1;
            public int AvailableUnits(string categoryId) => Stock.TryGetValue(categoryId, out int s) ? s : 0;
            public int Ship(string categoryId, int units, int dayIndex, List<string> diagnostics)
            {
                int have = AvailableUnits(categoryId);
                int shipped = System.Math.Min(System.Math.Max(0, units), have);
                Stock[categoryId] = have - shipped;
                return shipped;
            }
        }

        private static StoreRestockService ServiceWithOrders(out StubStorePort port)
        {
            port = new StubStorePort();
            port.OfferList.Add(new GeneralStoreCategoryOffer { CategoryId = "staple_food", OfferedItemIds = new List<string> { "flour" }, StoreUnitsPerItemUnit = 1 });
            port.OfferList.Add(new GeneralStoreCategoryOffer { CategoryId = "meat", OfferedItemIds = new List<string> { "beef" }, StoreUnitsPerItemUnit = 1 });
            port.Stock["staple_food"] = 10;
            port.Targets["staple_food"] = 100;
            port.Prices["staple_food"] = 35;
            port.Stock["meat"] = 5;
            port.Targets["meat"] = 40;
            port.Prices["meat"] = 14;
            port.CashBalance = 100000;

            var supplier = new FixtureSupplier();
            supplier.Stock["staple_food"] = 500;
            supplier.Quotes["staple_food"] = 10;
            supplier.Stock["meat"] = 100;
            supplier.Quotes["meat"] = 6;
            var suppliers = new List<IUpstreamGoodsSupplier> { supplier };

            var service = new StoreRestockService();
            service.EvaluateRestock(port, suppliers, 0, 0.5f);
            service.AdvanceDay(port, suppliers, 1, new List<string>());
            return service;
        }

        [Test]
        public void Service_RoundTrip_PreservesOrdersDeliveriesAndSequences()
        {
            StoreRestockService service = ServiceWithOrders(out StubStorePort port);
            Assert.AreEqual(2, service.Orders.Count);
            Assert.AreEqual(2, service.Deliveries.Count);

            var dto = new RegionalSupplySaveDto();
            service.CaptureInto(dto);
            var restored = new StoreRestockService();
            var diag = new List<string>();
            restored.RestoreFrom(dto, diag);
            Assert.AreEqual(0, diag.Count);
            Assert.AreEqual(2, restored.Orders.Count);
            Assert.AreEqual(2, restored.Deliveries.Count);
            for (int i = 0; i < service.Orders.Count; i++)
            {
                Assert.AreEqual(service.Orders[i].OrderId, restored.Orders[i].OrderId);
                Assert.AreEqual(service.Orders[i].UnitsOrdered, restored.Orders[i].UnitsOrdered);
                Assert.AreEqual(service.Orders[i].UnitCostCents, restored.Orders[i].UnitCostCents);
                Assert.AreEqual(service.Orders[i].Stage, restored.Orders[i].Stage);
                Assert.AreEqual(service.Orders[i].UpstreamSupplierId, restored.Orders[i].UpstreamSupplierId);
            }
            for (int i = 0; i < service.Deliveries.Count; i++)
            {
                Assert.AreEqual(service.Deliveries[i].DeliveryId, restored.Deliveries[i].DeliveryId);
                Assert.AreEqual(service.Deliveries[i].Units, restored.Deliveries[i].Units);
                Assert.AreEqual(service.Deliveries[i].Received, restored.Deliveries[i].Received);
            }

            // Idempotent: restoring the same DTO again reproduces the state.
            restored.RestoreFrom(dto, diag);
            Assert.AreEqual(2, restored.Orders.Count);
            Assert.AreEqual(2, restored.Deliveries.Count);

            // The restored service keeps working: a new day places a new
            // order with a fresh sequence (no id collision with restored ones).
            var supplier = new FixtureSupplier();
            supplier.Stock["staple_food"] = 500;
            supplier.Quotes["staple_food"] = 10;
            supplier.Stock["meat"] = 100;
            supplier.Quotes["meat"] = 6;
            port.Stock["staple_food"] = 10;
            restored.EvaluateRestock(port, new List<IUpstreamGoodsSupplier> { supplier }, 5, 0.5f);
            Assert.AreEqual(3, restored.Orders.Count);
            Assert.AreNotEqual(restored.Orders[0].OrderId, restored.Orders[2].OrderId);
        }

        [Test]
        public void Service_Restore_SkipsInvalidEntries()
        {
            var dto = new RegionalSupplySaveDto();
            // Slice 24: po-good is InTransit (not Ordered) because its live
            // delivery dlv-good only legitimately pairs with an InTransit
            // order — a live delivery for an Ordered order is now skipped
            // at restore, so the "good" row must be a consistent pair.
            dto.Orders.Add(new StoreProcurementOrder { OrderId = "po-good", StoreBusinessId = "general-store-1", CategoryId = "staple_food", UnitsOrdered = 10, UnitCostCents = 5, Stage = StoreProcurementStage.InTransit });
            dto.Orders.Add(new StoreProcurementOrder { OrderId = "", StoreBusinessId = "general-store-1", CategoryId = "staple_food", UnitsOrdered = 10, UnitCostCents = 5, Stage = StoreProcurementStage.Ordered });
            dto.Orders.Add(new StoreProcurementOrder { OrderId = "po-negative", StoreBusinessId = "general-store-1", CategoryId = "staple_food", UnitsOrdered = -5, UnitCostCents = 5, Stage = StoreProcurementStage.Ordered });
            dto.Orders.Add(new StoreProcurementOrder { OrderId = "po-bad-stage", StoreBusinessId = "general-store-1", CategoryId = "staple_food", UnitsOrdered = 10, UnitCostCents = 5, Stage = (StoreProcurementStage)999 });
            dto.Deliveries.Add(new StoreDelivery { DeliveryId = "dlv-good", OrderId = "po-good", CategoryId = "staple_food", Units = 10 });
            dto.Deliveries.Add(new StoreDelivery { DeliveryId = "", OrderId = "po-good", CategoryId = "staple_food", Units = 10 });
            dto.Deliveries.Add(new StoreDelivery { DeliveryId = "dlv-negative", OrderId = "po-good", CategoryId = "staple_food", Units = -3 });
            dto.NextOrderSequence = 7;
            dto.NextDeliverySequence = 3;

            var restored = new StoreRestockService();
            var diag = new List<string>();
            restored.RestoreFrom(dto, diag);
            Assert.AreEqual(1, restored.Orders.Count);
            Assert.AreEqual("po-good", restored.Orders[0].OrderId);
            Assert.AreEqual(1, restored.Deliveries.Count);
            Assert.AreEqual(5, diag.Count, "Each invalid entry must be diagnosed.");
        }

        [Test]
        public void Adapter_RoundTrip_PreservesStockAndProductionWeek()
        {
            var adapter = new RegionalSupplierAdapter(RegionalBusinessRegistry.AshfordMercantile);
            var diag = new List<string>();
            adapter.Ship("staple_food", 150, 0, diag);
            adapter.DriveWeeklyProduction(0);
            adapter.DriveWeeklyProduction(14); // two weeks of production
            int week = adapter.LastProductionWeek;
            Assert.AreEqual(2, week);

            var dto = new RegionalSupplySaveDto();
            adapter.CaptureInto(dto);
            var restored = new RegionalSupplierAdapter(RegionalBusinessRegistry.AshfordMercantile);
            restored.RestoreFrom(dto, diag);
            foreach (RegionalBusinessRegistry.RegionalOffer offer in RegionalBusinessRegistry.AshfordMercantile.Offers)
                Assert.AreEqual(adapter.OnHandUnits(offer.ItemId), restored.OnHandUnits(offer.ItemId),
                    $"Stock mismatch for {offer.ItemId} after restore.");
            Assert.AreEqual(2, restored.LastProductionWeek);

            // Idempotent restore.
            restored.RestoreFrom(dto, diag);
            foreach (RegionalBusinessRegistry.RegionalOffer offer in RegionalBusinessRegistry.AshfordMercantile.Offers)
                Assert.AreEqual(adapter.OnHandUnits(offer.ItemId), restored.OnHandUnits(offer.ItemId));
        }

        [Test]
        public void Adapter_Restore_SkipsForeignRows_ClampsNegatives()
        {
            var dto = new RegionalSupplySaveDto();
            dto.SupplierStocks.Add(new RegionalSupplierStockRow { SupplierId = "ashford_general_mercantile", ItemId = "sugar", Units = 42, LastProductionWeek = 3 });
            dto.SupplierStocks.Add(new RegionalSupplierStockRow { SupplierId = "redfield_general_store", ItemId = "sugar", Units = 999, LastProductionWeek = 3 });
            dto.SupplierStocks.Add(new RegionalSupplierStockRow { SupplierId = "ashford_general_mercantile", ItemId = "no_such_item", Units = 5, LastProductionWeek = 3 });
            dto.SupplierStocks.Add(new RegionalSupplierStockRow { SupplierId = "ashford_general_mercantile", ItemId = "salt", Units = -9, LastProductionWeek = 3 });

            var adapter = new RegionalSupplierAdapter(RegionalBusinessRegistry.AshfordMercantile);
            var diag = new List<string>();
            adapter.RestoreFrom(dto, diag);
            Assert.AreEqual(42, adapter.OnHandUnits("sugar"));
            Assert.AreEqual(0, adapter.OnHandUnits("salt"), "Negative units clamp to zero.");
            Assert.AreEqual(3, adapter.LastProductionWeek);
            Assert.AreEqual(2, diag.Count, "Unknown item and negative units must be diagnosed; foreign rows are ignored silently.");
            // Untouched items reset to starting stock (clear-then-load).
            Assert.AreEqual(80, adapter.OnHandUnits("coffee"));
        }

        [Test]
        public void WholeDto_RoundTrip_EndToEnd()
        {
            StoreRestockService service = ServiceWithOrders(out StubStorePort port);
            var adapters = RegionalSupplierSet.CreateAll();
            var dto = new RegionalSupplySaveDto();
            service.CaptureInto(dto);
            foreach (RegionalSupplierAdapter adapter in adapters)
                adapter.CaptureInto(dto);

            var restoredService = new StoreRestockService();
            restoredService.RestoreFrom(dto, new List<string>());
            Assert.AreEqual(service.Orders.Count, restoredService.Orders.Count);
            var restoredAdapters = RegionalSupplierSet.CreateAll();
            foreach (RegionalSupplierAdapter adapter in restoredAdapters)
                adapter.RestoreFrom(dto, new List<string>());
            for (int i = 0; i < adapters.Count; i++)
            {
                foreach (RegionalBusinessRegistry.RegionalOffer offer in RegionalBusinessRegistry.All[i].Offers)
                    Assert.AreEqual(adapters[i].OnHandUnits(offer.ItemId),
                        restoredAdapters[i].OnHandUnits(offer.ItemId),
                        $"Stock mismatch for {adapters[i].SupplierId}/{offer.ItemId}.");
            }
        }

        [Test]
        public void Service_Restore_SkipsLiveDeliveryWithMissingOrder()
        {
            var dto = new RegionalSupplySaveDto();
            dto.Orders.Add(new StoreProcurementOrder { OrderId = "po-good", StoreBusinessId = "general-store-1", CategoryId = "staple_food", UnitsOrdered = 10, UnitCostCents = 5, Stage = StoreProcurementStage.Ordered });
            dto.Deliveries.Add(new StoreDelivery { DeliveryId = "dlv-settled", OrderId = "po-good", CategoryId = "staple_food", Units = 0, Received = true });
            dto.Deliveries.Add(new StoreDelivery { DeliveryId = "dlv-orphan", OrderId = "po-missing", CategoryId = "staple_food", Units = 10, Arrived = true, Received = false });

            var restored = new StoreRestockService();
            var diag = new List<string>();
            restored.RestoreFrom(dto, diag);
            Assert.AreEqual(1, restored.Orders.Count);
            Assert.AreEqual(1, restored.Deliveries.Count, "The settled history still loads — the stage-aware rule does not break the round-trip case.");
            Assert.AreEqual("dlv-settled", restored.Deliveries[0].DeliveryId);
            string log = string.Join(" ", diag);
            Assert.IsTrue(log.Contains("dlv-orphan"), "The skipped orphan must be diagnosed by id, got: " + log);
            Assert.IsTrue(log.Contains("missing"), "The diagnostic must name the missing order, got: " + log);
        }

        [Test]
        public void Service_Restore_SkipsLiveDeliveryForCancelledOrder()
        {
            var dto = new RegionalSupplySaveDto();
            dto.Orders.Add(new StoreProcurementOrder { OrderId = "po-cancelled", StoreBusinessId = "general-store-1", CategoryId = "staple_food", UnitsOrdered = 10, UnitCostCents = 5, Stage = StoreProcurementStage.Cancelled, CancelReason = "upstream shipped nothing (stock exhausted)" });
            dto.Deliveries.Add(new StoreDelivery { DeliveryId = "dlv-cancelled", OrderId = "po-cancelled", CategoryId = "staple_food", Units = 10, Arrived = true, Received = false });

            var restored = new StoreRestockService();
            var diag = new List<string>();
            restored.RestoreFrom(dto, diag);
            Assert.AreEqual(1, restored.Orders.Count, "Cancelled orders are restorable stages — the order loads unchanged; only the unbacked delivery is refused.");
            Assert.AreEqual(0, restored.Deliveries.Count, "A live delivery for a cancelled order never ships, so it must not load.");
            string log = string.Join(" ", diag);
            Assert.IsTrue(log.Contains("dlv-cancelled"), "The skipped delivery must be diagnosed by id, got: " + log);
            Assert.IsTrue(log.Contains("Cancelled"), "The diagnostic must name the order stage, got: " + log);
        }

        [Test]
        public void Service_Restore_SkipsLiveDeliveryForReceivedOrder()
        {
            var dto = new RegionalSupplySaveDto();
            dto.Orders.Add(new StoreProcurementOrder { OrderId = "po-received", StoreBusinessId = "general-store-1", CategoryId = "staple_food", UnitsOrdered = 10, UnitCostCents = 5, Stage = StoreProcurementStage.Received });
            dto.Deliveries.Add(new StoreDelivery { DeliveryId = "dlv-double", OrderId = "po-received", CategoryId = "staple_food", Units = 10, Arrived = true, Received = false });

            var restored = new StoreRestockService();
            var diag = new List<string>();
            restored.RestoreFrom(dto, diag);
            Assert.AreEqual(1, restored.Orders.Count);
            Assert.AreEqual(0, restored.Deliveries.Count, "An order already Received has no live delivery left — the extra one would double-receive the same goods.");
            string log = string.Join(" ", diag);
            Assert.IsTrue(log.Contains("dlv-double"), "The skipped delivery must be diagnosed by id, got: " + log);
            Assert.IsTrue(log.Contains("Received"), "The diagnostic must name the order stage, got: " + log);
        }

        [Test]
        public void Service_Restore_KeepsLiveDeliveryForInTransitOrder_ThenReceivesWithPayment()
        {
            var dto = new RegionalSupplySaveDto();
            dto.Orders.Add(new StoreProcurementOrder { OrderId = "po-live", StoreBusinessId = "general-store-1", CategoryId = "staple_food", UnitsOrdered = 10, UnitCostCents = 5, Stage = StoreProcurementStage.InTransit });
            dto.Deliveries.Add(new StoreDelivery { DeliveryId = "dlv-live", OrderId = "po-live", CategoryId = "staple_food", Units = 10, Arrived = true, Received = false });

            var service = new StoreRestockService();
            var diag = new List<string>();
            service.RestoreFrom(dto, diag);
            Assert.AreEqual(1, service.Deliveries.Count, "The legitimate live-delivery pairing loads.");
            Assert.AreEqual(0, diag.Count, "A clean restore reports nothing about the delivery.");

            var port = new StubStorePort();
            port.OfferList.Add(new GeneralStoreCategoryOffer { CategoryId = "staple_food", OfferedItemIds = new List<string> { "flour" }, StoreUnitsPerItemUnit = 1 });
            port.Stock["staple_food"] = 0;
            port.Targets["staple_food"] = 100;
            port.Prices["staple_food"] = 35;
            port.CashBalance = 100000;

            // Empty supplier list is correct here: the order is InTransit,
            // not Ordered, so the shipping loop skips it and only the
            // receipt loop runs.
            service.AdvanceDay(port, new List<IUpstreamGoodsSupplier>(), 1, new List<string>());
            Assert.IsTrue(service.Deliveries[0].Received);
            Assert.AreEqual(StoreProcurementStage.Received, service.Orders[0].Stage);
            Assert.AreEqual(100000 - 10 * 5, port.CashBalance, "The restored delivery pays real cash for real stock.");
            Assert.AreEqual(10, port.CategoryStockUnits("staple_food"));
        }

        [Test]
        public void AdvanceDay_DoesNotReceiveDeliveryWhoseOrderWasCancelledAfterRestore()
        {
            var dto = new RegionalSupplySaveDto();
            dto.Orders.Add(new StoreProcurementOrder { OrderId = "po-live", StoreBusinessId = "general-store-1", CategoryId = "staple_food", UnitsOrdered = 10, UnitCostCents = 5, Stage = StoreProcurementStage.InTransit });
            dto.Deliveries.Add(new StoreDelivery { DeliveryId = "dlv-live", OrderId = "po-live", CategoryId = "staple_food", Units = 10, Arrived = true, Received = false });

            var service = new StoreRestockService();
            service.RestoreFrom(dto, new List<string>());
            Assert.AreEqual(1, service.Deliveries.Count);

            // Simulate a post-load cancellation reaching memory through
            // another path (direct test manipulation, stated here).
            service.Orders[0].Stage = StoreProcurementStage.Cancelled;

            var port = new StubStorePort();
            port.OfferList.Add(new GeneralStoreCategoryOffer { CategoryId = "staple_food", OfferedItemIds = new List<string> { "flour" }, StoreUnitsPerItemUnit = 1 });
            port.Stock["staple_food"] = 0;
            port.Targets["staple_food"] = 100;
            port.Prices["staple_food"] = 35;
            port.CashBalance = 100000;

            var diag = new List<string>();
            service.AdvanceDay(port, new List<IUpstreamGoodsSupplier>(), 1, diag);
            Assert.AreEqual(100000, port.CashBalance, "No cash moves for a delivery whose order is no longer live.");
            Assert.AreEqual(0, port.CategoryStockUnits("staple_food"), "No stock books for a delivery whose order is no longer live.");
            Assert.IsFalse(service.Deliveries[0].Received);
            string log = string.Join(" ", diag);
            Assert.IsTrue(log.Contains("no live order"), "The guard must diagnose the refusal, got: " + log);
            Assert.IsTrue(log.Contains("dlv-live"), "The diagnostic must name the delivery, got: " + log);
            // Note honestly: the order == null branch of the guard is
            // unreachable through the public API once RestoreFrom filters
            // (no public delivery adder exists), so it is covered by this
            // test's guard mechanism plus the restore tests, not by a
            // direct null-order receipt — defense in depth for a future
            // delivery source, not a branch any current caller can fire.
        }
    }
}
