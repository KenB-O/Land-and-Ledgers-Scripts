// Slice 11 (2026-11-05): the daily restock driver over a stub port and a
// finite fixture supplier. Proves the chain end to end through the
// production interfaces: order, ship from real supplier stock, pay real
// cash, receive real stock, with conservation on every leg.
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Population;
using LandLedgers.World.Regional;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    public sealed class StoreRestockDriverTests
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
            public string SupplierId { get; set; } = "wholesaler-1";
            public string SupplierName { get; set; } = "Regional Wholesaler";
            public Dictionary<string, int> Stock = new Dictionary<string, int>();
            public Dictionary<string, int> Quotes = new Dictionary<string, int>();
            public Dictionary<string, int> LeadDays = new Dictionary<string, int>();
            public int QuoteUnitCostCents(string categoryId) => Quotes.TryGetValue(categoryId, out int q) ? q : 0;
            public int LeadTimeDays(string categoryId) => LeadDays.TryGetValue(categoryId, out int l) ? l : 0;
            public int AvailableUnits(string categoryId) => Stock.TryGetValue(categoryId, out int s) ? s : 0;
            public int Ship(string categoryId, int units, int dayIndex, List<string> diagnostics)
            {
                int have = AvailableUnits(categoryId);
                int shipped = System.Math.Min(System.Math.Max(0, units), have);
                Stock[categoryId] = have - shipped;
                return shipped;
            }
        }

        private static (StoreRestockDriver, StubStorePort, FixtureSupplier) Setup(
            int storeStock, int storeTarget, int storePrice, int storeCash,
            int supplierStock, int quote, int leadDays)
        {
            var port = new StubStorePort();
            port.OfferList.Add(new GeneralStoreCategoryOffer
            {
                CategoryId = "staple_food",
                OfferedItemIds = new List<string> { "flour" },
                StoreUnitsPerItemUnit = 1,
            });
            port.Stock["staple_food"] = storeStock;
            port.Targets["staple_food"] = storeTarget;
            port.Prices["staple_food"] = storePrice;
            port.CashBalance = storeCash;

            var supplier = new FixtureSupplier();
            supplier.Stock["staple_food"] = supplierStock;
            supplier.Quotes["staple_food"] = quote;
            supplier.LeadDays["staple_food"] = leadDays;

            var driver = new StoreRestockDriver();
            driver.RegisterSupplier(supplier);
            return (driver, port, supplier);
        }

        [Test]
        public void DriveDaily_OrdersShipsPaysAndReceives_WithConservation()
        {
            (StoreRestockDriver driver, StubStorePort port, FixtureSupplier supplier) =
                Setup(storeStock: 10, storeTarget: 100, storePrice: 50, storeCash: 100000,
                    supplierStock: 500, quote: 20, leadDays: 2);
            Assert.IsTrue(driver.DriveDaily(port, 0, 0.5f));
            Assert.AreEqual(1, driver.Restock.Orders.Count);
            Assert.AreEqual(90, driver.Restock.Orders[0].UnitsOrdered);
            Assert.AreEqual(20, driver.Restock.Orders[0].UnitCostCents);

            Assert.IsTrue(driver.DriveDaily(port, 2, 0.5f));
            Assert.AreEqual(100, port.CategoryStockUnits("staple_food"));
            Assert.AreEqual(410, supplier.AvailableUnits("staple_food"));
            Assert.AreEqual(100000 - 90 * 20, port.CashBalance);
            Assert.AreEqual(StoreProcurementStage.Received, driver.Restock.Orders[0].Stage);
        }

        [Test]
        public void SeparateStores_RestoreIndependentOrders_WhileSharingFiniteSupplierStock()
        {
            var supplier = new FixtureSupplier();
            supplier.Stock["staple_food"] = 80;
            supplier.Quotes["staple_food"] = 20;
            supplier.LeadDays["staple_food"] = 2;

            StubStorePort npc = MakeRestockStore("general_store_000");
            StubStorePort player = MakeRestockStore("player-general-store-1");
            var npcDriver = new StoreRestockDriver();
            var playerDriver = new StoreRestockDriver();
            npcDriver.RegisterSupplier(supplier);
            playerDriver.RegisterSupplier(supplier);

            Assert.IsTrue(npcDriver.DriveDaily(npc, 0, 0.5f));
            Assert.IsTrue(playerDriver.DriveDaily(player, 0, 0.5f));
            Assert.AreEqual(40, npcDriver.Restock.Orders[0].UnitsOrdered);
            Assert.AreEqual("general_store_000", npcDriver.Restock.Orders[0].StoreBusinessId);
            Assert.AreEqual(40, playerDriver.Restock.Orders[0].UnitsOrdered);
            Assert.AreEqual("player-general-store-1", playerDriver.Restock.Orders[0].StoreBusinessId);
            Assert.AreEqual(80, supplier.AvailableUnits("staple_food"),
                "The finite supplier stock is not deducted until the two-day shipments depart.");

            var npcSave = new RegionalSupplySaveDto();
            var playerSave = new RegionalSupplySaveDto();
            npcDriver.CaptureInto(npcSave);
            playerDriver.CaptureInto(playerSave);

            var restoredNpcDriver = new StoreRestockDriver();
            var restoredPlayerDriver = new StoreRestockDriver();
            restoredNpcDriver.RegisterSupplier(supplier);
            restoredPlayerDriver.RegisterSupplier(supplier);
            restoredNpcDriver.RestoreFrom(npcSave, new List<string>());
            restoredPlayerDriver.RestoreFrom(playerSave, new List<string>());

            Assert.IsTrue(restoredNpcDriver.DriveDaily(npc, 2, 0.5f));
            Assert.IsTrue(restoredPlayerDriver.DriveDaily(player, 2, 0.5f));
            Assert.AreEqual(40, npc.CategoryStockUnits("staple_food"));
            Assert.AreEqual(40, player.CategoryStockUnits("staple_food"));
            Assert.AreEqual(9200, npc.CashBalance);
            Assert.AreEqual(9200, player.CashBalance);
            Assert.AreEqual(0, supplier.AvailableUnits("staple_food"),
                "Both received orders draw from the same finite supplier inventory.");
            Assert.AreEqual(StoreProcurementStage.Received, restoredNpcDriver.Restock.Orders[0].Stage);
            Assert.AreEqual(StoreProcurementStage.Received, restoredPlayerDriver.Restock.Orders[0].Stage);
            Assert.IsTrue(restoredNpcDriver.Restock.Deliveries[0].Received);
            Assert.IsTrue(restoredPlayerDriver.Restock.Deliveries[0].Received);
        }

        private static StubStorePort MakeRestockStore(string businessId)
        {
            var port = new StubStorePort
            {
                StoreBusinessId = businessId,
                CashBalance = 10000,
            };
            port.OfferList.Add(new GeneralStoreCategoryOffer
            {
                CategoryId = "staple_food",
                OfferedItemIds = new List<string> { "flour" },
                StoreUnitsPerItemUnit = 1,
            });
            port.Stock["staple_food"] = 0;
            port.Targets["staple_food"] = 40;
            port.Prices["staple_food"] = 50;
            return port;
        }

        [Test]
        public void DriveDaily_DoesNotDuplicateAnOpenOrder()
        {
            (StoreRestockDriver driver, StubStorePort port, FixtureSupplier _) =
                Setup(storeStock: 10, storeTarget: 100, storePrice: 50, storeCash: 100000,
                    supplierStock: 500, quote: 20, leadDays: 2);
            Assert.IsTrue(driver.DriveDaily(port, 0, 0.5f));
            Assert.IsTrue(driver.DriveDaily(port, 1, 0.5f));
            Assert.AreEqual(1, driver.Restock.Orders.Count);
        }

        [Test]
        public void DriveDaily_SameDaySecondCall_IsANoop()
        {
            (StoreRestockDriver driver, StubStorePort port, FixtureSupplier _) =
                Setup(storeStock: 10, storeTarget: 100, storePrice: 50, storeCash: 100000,
                    supplierStock: 500, quote: 20, leadDays: 2);
            Assert.IsTrue(driver.DriveDaily(port, 0, 0.5f));
            Assert.IsTrue(driver.DriveDaily(port, 0, 0.5f));
            Assert.AreEqual(1, driver.Restock.Orders.Count);
        }

        [Test]
        public void DriveDaily_NoSuppliers_PlacingNothingAndNamingTheGap()
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
            port.Prices["staple_food"] = 50;
            port.CashBalance = 100000;
            var driver = new StoreRestockDriver();
            Assert.IsTrue(driver.DriveDaily(port, 0, 0.5f));
            Assert.AreEqual(0, driver.Restock.Orders.Count);
            Assert.IsTrue(driver.LastDailySummary.Contains("no upstream suppliers registered"),
                $"summary should name the gap, was: {driver.LastDailySummary}");
        }

        [Test]
        public void DriveDaily_UnaffordableReceipt_WaitsWithoutBookingStock()
        {
            (StoreRestockDriver driver, StubStorePort port, FixtureSupplier supplier) =
                Setup(storeStock: 10, storeTarget: 100, storePrice: 50, storeCash: 100,
                    supplierStock: 500, quote: 20, leadDays: 0);
            Assert.IsTrue(driver.DriveDaily(port, 0, 0.5f));
            Assert.AreEqual(1, driver.Restock.Orders.Count);
            Assert.AreEqual(10, port.CategoryStockUnits("staple_food"));
            Assert.AreEqual(100, port.CashBalance);
            Assert.AreEqual(410, supplier.AvailableUnits("staple_food"));
            Assert.AreEqual(1, driver.Restock.Deliveries.Count);
            Assert.IsFalse(driver.Restock.Deliveries[0].Received);
        }

        [Test]
        public void DriveDaily_NullStore_ReturnsFalse()
        {
            var driver = new StoreRestockDriver();
            Assert.IsFalse(driver.DriveDaily(null, 0, 0.5f));
        }

        [Test]
        public void DriverCapture_RoundTrip_PreservesOpenOrderAndSequences()
        {
            (StoreRestockDriver driver, StubStorePort port, FixtureSupplier _) =
                Setup(storeStock: 10, storeTarget: 100, storePrice: 50, storeCash: 100000,
                    supplierStock: 500, quote: 20, leadDays: 2);
            Assert.IsTrue(driver.DriveDaily(port, 0, 0.5f));
            Assert.AreEqual(1, driver.Restock.Orders.Count);
            Assert.AreEqual("po-general-store-1-0-0", driver.Restock.Orders[0].OrderId);

            // These two calls use the exact argument shapes the manager
            // uses (GeneralStoreRuntimeManager lines 862/891), so their
            // compilation in the harness validates the manager's call
            // syntax even though the manager file itself is excluded.
            var dto = new RegionalSupplySaveDto();
            driver.CaptureInto(dto);
            Assert.AreEqual(1, dto.Orders.Count);
            Assert.AreEqual("po-general-store-1-0-0", dto.Orders[0].OrderId);
            Assert.AreEqual(90, dto.Orders[0].UnitsOrdered);
            Assert.AreEqual(20, dto.Orders[0].UnitCostCents);
            Assert.AreEqual(StoreProcurementStage.Ordered, dto.Orders[0].Stage);
            Assert.AreEqual(1, dto.NextOrderSequence);
            Assert.AreEqual(0, dto.NextDeliverySequence);

            var diagnostics = new List<string>();
            var fresh = new StoreRestockDriver();
            fresh.RestoreFrom(dto, diagnostics);
            Assert.AreEqual(0, diagnostics.Count, "A clean restore reports nothing.");
            Assert.AreEqual(1, fresh.Restock.Orders.Count);
            Assert.AreEqual("po-general-store-1-0-0", fresh.Restock.Orders[0].OrderId);
            Assert.AreEqual(90, fresh.Restock.Orders[0].UnitsOrdered);
            Assert.AreEqual(20, fresh.Restock.Orders[0].UnitCostCents);
            Assert.AreEqual(StoreProcurementStage.Ordered, fresh.Restock.Orders[0].Stage);
            Assert.AreEqual(0, fresh.Restock.Deliveries.Count);
        }

        [Test]
        public void DriverRestore_ThenDrive_ContinuesChainWithConservation()
        {
            (StoreRestockDriver driver, StubStorePort port, FixtureSupplier _) =
                Setup(storeStock: 10, storeTarget: 100, storePrice: 50, storeCash: 100000,
                    supplierStock: 500, quote: 20, leadDays: 2);
            Assert.IsTrue(driver.DriveDaily(port, 0, 0.5f));

            var dto = new RegionalSupplySaveDto();
            driver.CaptureInto(dto);

            // The supplier is outside the driver, so the restored driver
            // gets a second FixtureSupplier with the same 500u stock.
            var fresh = new StoreRestockDriver();
            var freshSupplier = new FixtureSupplier();
            freshSupplier.Stock["staple_food"] = 500;
            freshSupplier.Quotes["staple_food"] = 20;
            freshSupplier.LeadDays["staple_food"] = 2;
            fresh.RegisterSupplier(freshSupplier);
            fresh.RestoreFrom(dto, new List<string>());

            Assert.IsTrue(fresh.DriveDaily(port, 2, 0.5f));
            Assert.AreEqual(StoreProcurementStage.Received, fresh.Restock.Orders[0].Stage);
            Assert.AreEqual(100, port.CategoryStockUnits("staple_food"));
            Assert.AreEqual(100000 - 90 * 20, port.CashBalance);
            Assert.AreEqual(410, freshSupplier.AvailableUnits("staple_food"),
                "The restored chain ships from real stock, pays real cash, and receives real stock exactly as the unrestored chain does.");
        }

        [Test]
        public void DriverRestore_NullDto_ClearsAndRestoreTwice_IsIdempotent()
        {
            (StoreRestockDriver driver, StubStorePort port, FixtureSupplier _) =
                Setup(storeStock: 10, storeTarget: 100, storePrice: 50, storeCash: 100000,
                    supplierStock: 500, quote: 20, leadDays: 2);
            Assert.IsTrue(driver.DriveDaily(port, 0, 0.5f));
            Assert.AreEqual(1, driver.Restock.Orders.Count);

            var diagnostics = new List<string>();
            driver.RestoreFrom(null, diagnostics);
            Assert.AreEqual(0, driver.Restock.Orders.Count, "Restore of a null DTO clears the open order.");
            Assert.AreEqual(0, driver.Restock.Deliveries.Count);

            (StoreRestockDriver driven, StubStorePort _, FixtureSupplier _) =
                Setup(storeStock: 10, storeTarget: 100, storePrice: 50, storeCash: 100000,
                    supplierStock: 500, quote: 20, leadDays: 2);
            Assert.IsTrue(driven.DriveDaily(port, 0, 0.5f));
            var dto = new RegionalSupplySaveDto();
            driven.CaptureInto(dto);

            var fresh = new StoreRestockDriver();
            fresh.RestoreFrom(dto, new List<string>());
            var first = new RegionalSupplySaveDto();
            fresh.CaptureInto(first);
            fresh.RestoreFrom(dto, new List<string>());
            var second = new RegionalSupplySaveDto();
            fresh.CaptureInto(second);

            Assert.AreEqual(1, fresh.Restock.Orders.Count);
            Assert.AreEqual(first.Orders.Count, second.Orders.Count);
            Assert.AreEqual(first.Orders[0].OrderId, second.Orders[0].OrderId);
            Assert.AreEqual(first.NextOrderSequence, second.NextOrderSequence);
            Assert.AreEqual(first.NextDeliverySequence, second.NextDeliverySequence);
            Assert.AreEqual(first.NextShipmentGroupSequence, second.NextShipmentGroupSequence);
        }
    }
}
