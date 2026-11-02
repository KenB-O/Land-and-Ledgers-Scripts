using System;
using System.Collections.Generic;
using NUnit.Framework;
using LandLedgers.Economy;
using LandLedgers.Economy.BreadDrivers;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.FuelDealer;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Economy.Core;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Ordering;
using LandLedgers.Population;
using LandLedgers.Primitives;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Series 2 Pass 7 (selling): Roslyn acceptance, production classes
    /// as the system under test. No orchestrator shortcuts, no direct
    /// inventory injection for sales (bakery shelf stock comes from a
    /// real Pass 6 bake; fuel stock comes from real intake lots with
    /// provenance; store stock comes through the fake port's real
    /// consume path). All prices, quantities, and freight figures are
    /// test parameters, never canon. No Ethan/Tia/Shane fixture data
    /// is used here — test households are generic single-person
    /// inputs, and fixture authoring stays blocked on Kennedy inputs.
    /// Unity compilation, Test Runner, PlayMode, and scene wiring are
    /// NOT claimed. The Unity port test
    /// (GeneralStoreItemOfferIdentityTests) covers the live manager
    /// port; this file covers the builder, adapters, and settlement
    /// that the manager port feeds.
    /// </summary>
    [TestFixture]
    public sealed class Series2Pass7SellingTests
    {
        private sealed class RuntimeCashPort : IBusinessIdentityCashPort
        {
            private readonly BusinessRuntimeState runtime;
            public RuntimeCashPort(BusinessRuntimeState runtime) { this.runtime = runtime; }
            public string BusinessId => runtime != null ? runtime.BusinessId : string.Empty;
            public int BalanceCents => runtime != null ? runtime.CurrentCashCents : 0;
            public string TryDebit(int amountCents, string reason)
            {
                if (runtime == null) return "no runtime";
                return runtime.TrySpendCents(amountCents) ? null : "insufficient business cash";
            }
            public string Credit(int amountCents, string reason)
            {
                if (runtime == null) return "no runtime";
                runtime.AddCashCents(amountCents);
                return null;
            }
        }

        private sealed class FakeStorePort : IGeneralStoreTradingPort
        {
            public string StoreBusinessId { get; set; } = "general-store-1";
            public string StoreName { get; set; } = "General Store";
            public string LocationId { get; set; } = "general-store";
            public Dictionary<string, int> Stock = new Dictionary<string, int>();
            public Dictionary<string, int> Prices = new Dictionary<string, int>();
            public List<GeneralStoreCategoryOffer> OfferList = new List<GeneralStoreCategoryOffer>();
            public int CashBalance;

            public IReadOnlyList<GeneralStoreCategoryOffer> CategoryOffers => OfferList;
            public StoreTradingCapability TradingCapability => new StoreTradingCapability { Staffed = true };
            public int CurrentCashCents => CashBalance;
            public bool OffersDelivery => false;
            public bool OffersTradeCredit => false;
            public int TradeCreditLimitCents => 0;
            public int CategoryStockUnits(string categoryId) => Stock.TryGetValue(categoryId, out int s) ? s : 0;
            public int CategoryTargetStockUnits(string categoryId) => int.MaxValue;
            public int CategoryPricePerUnitCents(string categoryId) => Prices.TryGetValue(categoryId, out int p) ? p : 0;
            public bool TryConsumeStoreUnits(string categoryId, int units, out int consumedUnits)
            {
                consumedUnits = 0;
                int have = CategoryStockUnits(categoryId);
                int take = Math.Min(Math.Max(0, units), have);
                if (take <= 0) return false;
                Stock[categoryId] = have - take;
                consumedUnits = take;
                return true;
            }
            public void RecordCashSettlement(string categoryId, int unitsSold, int revenueCents)
            {
                CashBalance += Math.Max(0, revenueCents);
            }
            public void RecordCreditSettlement(string categoryId, int unitsSold, int amountCents, string obligationId) { }
            public string SpendCash(int amountCents, string purpose)
            {
                if (CashBalance < amountCents) return "cannot afford";
                CashBalance -= amountCents;
                return null;
            }
            public string ReceiveStock(string categoryId, int units, string provenanceLabel)
            {
                Stock[categoryId] = CategoryStockUnits(categoryId) + units;
                return null;
            }
            public int CategoryReceivableUnits(string categoryId) => int.MaxValue;
            public int DeliveryFeeCents(string itemId, int units) => 0;
        }

        private static Func<string, WorkstationComponentView?> OvenFinder()
        {
            var kinds = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "oven-chamber-1", "oven-chamber" },
                { "kneading-table-1", "kneading-table" },
                { "proofing-rack-1", "proofing-rack" },
                { "bake-peels-1", "bake-peels" },
            };
            return assetId => kinds.TryGetValue(assetId, out string kind)
                ? (WorkstationComponentView?)new WorkstationComponentView
                {
                    AssetId = assetId,
                    Kind = kind,
                    Condition01 = 0.9f,
                    IsUsable = true,
                }
                : null;
        }

        private sealed class BakerySetup
        {
            public List<string> Diag = new List<string>();
            public BusinessRuntimeState BakeryRuntime = new BusinessRuntimeState();
            public BakeryShopRuntime Bakery = new BakeryShopRuntime("pierce-bakery-1");
            public EntityIdRegistry Ids = new EntityIdRegistry();
        }

        private static BakerySetup BakeOneBatch(int bakeryCash, int bakeDay)
        {
            var s = new BakerySetup();
            s.BakeryRuntime.BindBusinessIdentity("pierce-bakery-1");
            s.BakeryRuntime.AddCashCents(bakeryCash);
            Assert.IsNull(s.Bakery.AddOven("bakehouse", new List<string>
            {
                "oven-chamber-1", "kneading-table-1", "proofing-rack-1", "bake-peels-1",
            }, s.Diag), "oven installation should succeed");
            Assert.IsNull(s.Bakery.FlourStock.ReceiveLot(new BakeryFlourLot
            {
                LotId = s.Ids.Allocate(EntityKind.Lot),
                FlourName = BakeryBreadCatalog.FlourItemId,
                Units = 24,
                AcquiredDayIndex = 0,
                ImportOrderId = "test-flour-order",
                OriginName = "Test mill",
            }, s.Diag), "flour lot should be received");
            Assert.IsNull(s.Bakery.FlourStock.ReceiveLot(new BakeryFlourLot
            {
                LotId = s.Ids.Allocate(EntityKind.Lot),
                FlourName = BakeryBreadCatalog.NotionsItemId,
                Units = 2,
                AcquiredDayIndex = 0,
                ImportOrderId = "test-notions-order",
                OriginName = "Test notions",
            }, s.Diag), "notions lot should be received");
            Assert.IsNull(s.Bakery.FuelStock.ReceiveLot(new BakeryFuelLot
            {
                LotId = s.Ids.Allocate(EntityKind.Lot),
                FuelName = BakeryBreadCatalog.FuelItemId,
                Units = 2,
                AcquiredDayIndex = 0,
                ImportOrderId = "test-fuel-order",
                OriginName = "Test fuel",
            }, s.Diag), "fuel lot should be received");
            var driver = new Series2BakeryProductionDriver(
                s.Bakery, new Series2InStockBakeryFuelSource(s.Bakery));
            Assert.IsNotNull(driver.PlanBatches(BakeryBreadCatalog.BreadLoafId, 1, bakeDay, s.Diag));
            int baked = driver.RunScheduledDay(
                bakeDay, true, WorkstationCatalog.BakeOven, OvenFinder(), null, s.Diag, s.Ids);
            Assert.AreEqual(1, baked, "one batch should bake; diag: " + string.Join("; ", s.Diag));
            Assert.AreEqual(24, s.Bakery.BreadShelf[0].UnitCount, "one batch yields 24 loaves");
            return s;
        }

        private static ShoppingSettlement CashSettlement()
        {
            return new ShoppingSettlement { Mode = ShoppingSettlementMode.CashFromLedger };
        }

        // ---- Trading-port builder ----

        [Test]
        public void TradingPortBuilder_StapleFood_OffersSevenItemsPerItem()
        {
            var diag = new List<string>();
            List<GeneralStoreCategoryOffer> offers = Series2TradingPortBuilder.BuildPerItemOffers(
                "staple_food",
                new List<string> { "flour", "bread", "potatoes", "salt", "sugar", "coffee", "tea" },
                diag);
            Assert.AreEqual(7, offers.Count, "all seven staple_food items must be offered, one offer each; diag: " + string.Join("; ", diag));
            foreach (GeneralStoreCategoryOffer offer in offers)
            {
                Assert.AreEqual(1, offer.OfferedItemIds.Count, "per-item offers carry exactly one item, never an aggregate");
                Assert.AreEqual("staple_food", offer.CategoryId);
            }
        }

        [Test]
        public void TradingPortBuilder_NonHouseholdItems_NeverOfferedUnderRepresentative()
        {
            var diag = new List<string>();
            List<GeneralStoreCategoryOffer> meat = Series2TradingPortBuilder.BuildPerItemOffers(
                "meat", new List<string> { "beef", "bacon", "cheese" }, diag);
            Assert.AreEqual(0, meat.Count, "beef/bacon/cheese are not household items and must never be offered as preserved_meat");
            List<GeneralStoreCategoryOffer> goods = Series2TradingPortBuilder.BuildPerItemOffers(
                "household_goods",
                new List<string> { "soap", "candles", "lamp_oil", "lantern", "thread", "buttons" },
                diag);
            Assert.AreEqual(2, goods.Count, "only soap and lamp_oil are household items in this list (candles plural != candle, lantern/thread/buttons are not catalog items)");
        }

        // ---- Store adapter, shared pool ----

        [Test]
        public void StoreAdapter_SharedPool_TotalSalesAcrossItemsCappedByPool()
        {
            var diag = new List<string>();
            List<GeneralStoreCategoryOffer> offers = Series2TradingPortBuilder.BuildPerItemOffers(
                "staple_food",
                new List<string> { "flour", "bread", "potatoes", "salt", "sugar", "coffee", "tea" },
                diag);
            var port = new FakeStorePort { CashBalance = 1000 };
            port.OfferList.AddRange(offers);
            port.Stock["staple_food"] = 10;
            port.Prices["staple_food"] = 5;
            var adapter = new GeneralStoreSupplierAdapter(port);
            Assert.AreEqual(6, adapter.SellItem("flour", 6, 2, CashSettlement(), diag), "flour sale should fill");
            Assert.AreEqual(4, adapter.SellItem("bread", 6, 2, CashSettlement(), diag),
                "bread sale from the same pool must partial-fill to the 4 remaining pool units, never exceed the pool");
            Assert.AreEqual(0, adapter.SellItem("potatoes", 1, 2, CashSettlement(), diag), "exhausted pool sells nothing");
            Assert.AreEqual(0, port.CategoryStockUnits("staple_food"), "pool must be exactly exhausted, never negative");
            Assert.AreEqual(1000 + 10 * 5, port.CashBalance, "store cash gains exactly the 10 pool units sold, at the category price");
        }

        [Test]
        public void StoreAdapter_SingleItemCategory_BehavesAsBefore()
        {
            var diag = new List<string>();
            var port = new FakeStorePort { CashBalance = 500 };
            port.OfferList.Add(new GeneralStoreCategoryOffer
            {
                CategoryId = "fuel_wood",
                OfferedItemIds = new List<string> { "fuel_wood" },
                StoreUnitsPerItemUnit = 1,
            });
            port.Stock["fuel_wood"] = 8;
            port.Prices["fuel_wood"] = 7;
            var adapter = new GeneralStoreSupplierAdapter(port);
            Assert.AreEqual(3, adapter.SellItem("fuel_wood", 3, 2, CashSettlement(), diag));
            Assert.AreEqual(5, port.CategoryStockUnits("fuel_wood"));
            Assert.AreEqual(500 + 21, port.CashBalance, "single-item sale posts exactly units x category price, as before");
            Assert.AreEqual(0, adapter.SellItem("flour", 1, 2, CashSettlement(), diag), "an item the store does not offer sells nothing");
        }

        // ---- Bakery adapter ----

        [Test]
        public void BakeryAdapter_FreshSale_CashReconciles()
        {
            BakerySetup s = BakeOneBatch(bakeryCash: 2000, bakeDay: 2);
            var adapter = new Series2BakerySupplierAdapter(
                s.Bakery, new RuntimeCashPort(s.BakeryRuntime),
                "pierce-bakery-1", "Pierce Bakery", "bakery");
            adapter.DayIndex = 2;
            Assert.IsTrue(adapter.OffersItem("bread"), "household bread must be offered");
            Assert.IsFalse(adapter.OffersItem("bake.bread-loaf"), "the shop product id is not a household item and is not offered as one");
            Assert.AreEqual(8, adapter.ItemPricePerUnitCents("bread"), "fresh loaf price is the schedule fresh price (test parameter schedule, not canon)");
            Assert.AreEqual(10, adapter.SellItem("bread", 10, 2, CashSettlement(), s.Diag),
                "fresh sale should fill; diag: " + string.Join("; ", s.Diag));
            Assert.AreEqual(2000 + 80, s.BakeryRuntime.CurrentCashCents, "bakery cash gains exactly 10 x 8c");
            Assert.AreEqual(14, s.Bakery.BreadShelf[0].UnitCount, "shelf loses exactly the units sold");
        }

        [Test]
        public void BakeryAdapter_DayOldSale_PricesAtDiscount_NotFresh()
        {
            BakerySetup s = BakeOneBatch(bakeryCash: 2000, bakeDay: 2);
            var adapter = new Series2BakerySupplierAdapter(
                s.Bakery, new RuntimeCashPort(s.BakeryRuntime),
                "pierce-bakery-1", "Pierce Bakery", "bakery");
            adapter.DayIndex = 3;
            Assert.AreEqual(4, adapter.ItemPricePerUnitCents("bread"), "day-old loaf prices at the 50% schedule discount");
            Assert.AreEqual(5, adapter.SellItem("bread", 5, 3, CashSettlement(), s.Diag));
            Assert.AreEqual(2000 + 20, s.BakeryRuntime.CurrentCashCents, "day-old revenue is 5 x 4c, not 5 x fresh price");
        }

        [Test]
        public void BakeryAdapter_MixedConditions_SellsOldestConditionOnly()
        {
            // Two batches on consecutive days: day-2 lot (day-old on day 3)
            // plus a day-3 lot (fresh on day 3). A 30-loaf request must
            // sell only the 24 day-old loaves at the day-old price, never
            // top up with fresh loaves at a second price in one call.
            BakerySetup s = BakeOneBatch(bakeryCash: 2000, bakeDay: 2);
            // Second batch: more inputs, planned and baked on day 3.
            Assert.IsNull(s.Bakery.FlourStock.ReceiveLot(new BakeryFlourLot
            {
                LotId = s.Ids.Allocate(EntityKind.Lot),
                FlourName = BakeryBreadCatalog.FlourItemId,
                Units = 12,
                AcquiredDayIndex = 2,
                ImportOrderId = "test-flour-order-2",
                OriginName = "Test mill",
            }, s.Diag));
            Assert.IsNull(s.Bakery.FlourStock.ReceiveLot(new BakeryFlourLot
            {
                LotId = s.Ids.Allocate(EntityKind.Lot),
                FlourName = BakeryBreadCatalog.NotionsItemId,
                Units = 1,
                AcquiredDayIndex = 2,
                ImportOrderId = "test-notions-order-2",
                OriginName = "Test notions",
            }, s.Diag));
            Assert.IsNull(s.Bakery.FuelStock.ReceiveLot(new BakeryFuelLot
            {
                LotId = s.Ids.Allocate(EntityKind.Lot),
                FuelName = BakeryBreadCatalog.FuelItemId,
                Units = 1,
                AcquiredDayIndex = 2,
                ImportOrderId = "test-fuel-order-2",
                OriginName = "Test fuel",
            }, s.Diag));
            var driver = new Series2BakeryProductionDriver(
                s.Bakery, new Series2InStockBakeryFuelSource(s.Bakery));
            Assert.IsNotNull(driver.PlanBatches(BakeryBreadCatalog.BreadLoafId, 1, 3, s.Diag));
            Assert.AreEqual(1, driver.RunScheduledDay(
                3, true, WorkstationCatalog.BakeOven, OvenFinder(), null, s.Diag, s.Ids),
                "second batch should bake on day 3; diag: " + string.Join("; ", s.Diag));
            Assert.AreEqual(2, s.Bakery.BreadShelf.Count, "two lots must be on the shelf");

            var adapter = new Series2BakerySupplierAdapter(
                s.Bakery, new RuntimeCashPort(s.BakeryRuntime),
                "pierce-bakery-1", "Pierce Bakery", "bakery");
            adapter.DayIndex = 3;
            Assert.AreEqual(4, adapter.ItemPricePerUnitCents("bread"), "quoted price is the oldest (day-old) lot's price");
            Assert.AreEqual(24, adapter.SellItem("bread", 30, 3, CashSettlement(), s.Diag),
                "sale caps to the oldest day's 24 loaves; diag: " + string.Join("; ", s.Diag));
            Assert.AreEqual(2000 + 24 * 4, s.BakeryRuntime.CurrentCashCents, "revenue is 24 day-old loaves only");
            Assert.AreEqual(24, s.Bakery.BreadShelf[1].UnitCount, "the fresh lot is untouched by the capped sale");
        }

        [Test]
        public void BakeryAdapter_DayNotSet_And_CreditNotOffered_RefuseWithoutMovingStock()
        {
            BakerySetup s = BakeOneBatch(bakeryCash: 2000, bakeDay: 2);
            var adapter = new Series2BakerySupplierAdapter(
                s.Bakery, new RuntimeCashPort(s.BakeryRuntime),
                "pierce-bakery-1", "Pierce Bakery", "bakery");
            Assert.AreEqual(0, adapter.SellItem("bread", 5, 2, CashSettlement(), s.Diag),
                "with no day set, trading must refuse rather than price at a stale day");
            Assert.AreEqual(24, s.Bakery.BreadShelf[0].UnitCount);
            adapter.DayIndex = 2;
            var credit = new ShoppingSettlement
            {
                Mode = ShoppingSettlementMode.TradeCreditObligation,
                ObligationId = "test-obligation-1",
            };
            Assert.AreEqual(0, adapter.SellItem("bread", 5, 2, credit, s.Diag),
                "credit must refuse when the bakery does not offer it, with stock intact");
            Assert.AreEqual(24, s.Bakery.BreadShelf[0].UnitCount);
            Assert.AreEqual(2000, s.BakeryRuntime.CurrentCashCents);
        }

        [Test]
        public void BakeryAdapter_TradeCreditOffered_PostsNoCash()
        {
            BakerySetup s = BakeOneBatch(bakeryCash: 2000, bakeDay: 2);
            var adapter = new Series2BakerySupplierAdapter(
                s.Bakery, new RuntimeCashPort(s.BakeryRuntime),
                "pierce-bakery-1", "Pierce Bakery", "bakery");
            adapter.DayIndex = 2;
            adapter.OffersTradeCreditValue = true;
            adapter.TradeCreditLimitCentsValue = 10000;
            var credit = new ShoppingSettlement
            {
                Mode = ShoppingSettlementMode.TradeCreditObligation,
                ObligationId = "test-obligation-2",
            };
            Assert.AreEqual(5, adapter.SellItem("bread", 5, 2, credit, s.Diag));
            Assert.AreEqual(2000, s.BakeryRuntime.CurrentCashCents, "a credit sale posts no cash; the receivable is the loop's obligation");
            Assert.AreEqual(19, s.Bakery.BreadShelf[0].UnitCount);
        }

        // ---- Fuel adapter ----

        private static Series2FuelDealerSupplierAdapter NewFuelAdapter(
            out FuelYardByproductIntake intake, out BusinessRuntimeState dealerRuntime, out EntityIdRegistry ids)
        {
            intake = new FuelYardByproductIntake();
            dealerRuntime = new BusinessRuntimeState();
            dealerRuntime.BindBusinessIdentity("fuel-dealer-1");
            dealerRuntime.AddCashCents(1000);
            ids = new EntityIdRegistry();
            var diag = new List<string>();
            Assert.IsNull(intake.ReceiveByproductLot("fuel-dealer-1", new SlabOffcutFuelLot
            {
                LotId = ids.Allocate(EntityKind.Lot),
                FuelWoodUnits = 20,
                SourceMillBusinessId = "mill-1",
                SourceLogLotId = "log-1",
                StandId = "stand-1",
                Species = "pine",
                ProducedDayIndex = 0,
            }, diag), "intake lot with full provenance should be received; diag: " + string.Join("; ", diag));
            return new Series2FuelDealerSupplierAdapter(
                intake, new RuntimeCashPort(dealerRuntime),
                "fuel-dealer-1", "Fuel Dealer", "fuel-yard", 28);
        }

        [Test]
        public void FuelAdapter_Sale_And_Shortfall()
        {
            Series2FuelDealerSupplierAdapter adapter = NewFuelAdapter(
                out FuelYardByproductIntake intake, out BusinessRuntimeState dealerRuntime, out EntityIdRegistry ids);
            var diag = new List<string>();
            Assert.IsTrue(adapter.OffersItem("fuel_wood"));
            Assert.AreEqual(20, adapter.ItemStockUnits("fuel_wood"));
            Assert.AreEqual(28, adapter.ItemPricePerUnitCents("fuel_wood"), "dealer price is a caller parameter (28c test value), not canon");
            Assert.AreEqual(5, adapter.SellItem("fuel_wood", 5, 2, CashSettlement(), diag));
            Assert.AreEqual(1000 + 140, dealerRuntime.CurrentCashCents, "dealer cash gains 5 x 28c");
            Assert.AreEqual(15, intake.TotalFuelWoodUnitsFor("fuel-dealer-1"), "intake loses exactly the units dispensed");
            Assert.AreEqual(0, adapter.SellItem("fuel_wood", 99, 2, CashSettlement(), diag),
                "shortfall must dispense nothing, never a partial lot");
            Assert.AreEqual(15, intake.TotalFuelWoodUnitsFor("fuel-dealer-1"), "failed shortfall sale leaves intake untouched");
            Assert.AreEqual(1000 + 140, dealerRuntime.CurrentCashCents);
        }

        [Test]
        public void FuelAdapter_CreditNotOffered_RefusesWithoutDispensing()
        {
            Series2FuelDealerSupplierAdapter adapter = NewFuelAdapter(
                out FuelYardByproductIntake intake, out BusinessRuntimeState dealerRuntime, out EntityIdRegistry ids);
            var diag = new List<string>();
            var credit = new ShoppingSettlement
            {
                Mode = ShoppingSettlementMode.TradeCreditObligation,
                ObligationId = "test-fuel-obligation",
            };
            Assert.AreEqual(0, adapter.SellItem("fuel_wood", 5, 2, credit, diag),
                "credit must refuse when the dealer does not offer it");
            Assert.AreEqual(20, intake.TotalFuelWoodUnitsFor("fuel-dealer-1"));
        }

        // ---- Wholesale settlement + shelf move ----

        private static Pass3WholesaleIntakeResult IntakeBread(
            Pass3WholesaleIntake intake, Pass3StoreCatalog catalog, string lotId, int units, int wholesalePrice, int bakedDay, int transferDay)
        {
            var diag = new List<string>();
            Pass3WholesaleIntakeResult result = intake.Receive(
                lotId, "bread", units, wholesalePrice, bakedDay, transferDay, catalog,
                "general-store-1", "pierce-bakery-1", diag);
            Assert.IsNotNull(result, "intake should succeed; diag: " + string.Join("; ", diag));
            return result;
        }

        [Test]
        public void Wholesale_CashSettlement_Then_DoubleSettlementRefused()
        {
            var catalog = new Pass3StoreCatalog();
            var intake = new Pass3WholesaleIntake();
            Pass3WholesaleIntakeResult result = IntakeBread(intake, catalog, "lot-w1", 10, 5, 2, 2);
            Assert.AreEqual(10, catalog.Find("bread").BackUnits, "intake lands in back storage only");
            Assert.AreEqual(0, catalog.Find("bread").ShelfUnits);
            Assert.AreEqual(50, result.Settlement.AmountCents, "settlement is units x wholesale price");
            Assert.IsFalse(result.Settlement.Settled);

            var storeRuntime = new BusinessRuntimeState();
            storeRuntime.BindBusinessIdentity("general-store-1");
            storeRuntime.AddCashCents(1000);
            var bakeryRuntime = new BusinessRuntimeState();
            bakeryRuntime.BindBusinessIdentity("pierce-bakery-1");
            bakeryRuntime.AddCashCents(500);
            var diag = new List<string>();
            Series2WholesaleSettlementResult settled = Series2WholesaleSettlement.SettleCash(
                result.Settlement, new RuntimeCashPort(storeRuntime),
                new RuntimeCashPort(bakeryRuntime), diag);
            Assert.IsTrue(settled.Settled, "cash settlement should succeed; diag: " + string.Join("; ", diag));
            Assert.IsTrue(result.Settlement.Settled);
            Assert.AreEqual(950, storeRuntime.CurrentCashCents, "store pays exactly 50c");
            Assert.AreEqual(550, bakeryRuntime.CurrentCashCents, "bakery receives exactly 50c");

            Series2WholesaleSettlementResult again = Series2WholesaleSettlement.SettleCash(
                result.Settlement, new RuntimeCashPort(storeRuntime),
                new RuntimeCashPort(bakeryRuntime), diag);
            Assert.IsFalse(again.Settled, "double settlement must refuse");
            Assert.AreEqual(950, storeRuntime.CurrentCashCents, "second settlement moves no cash");
            Assert.AreEqual(550, bakeryRuntime.CurrentCashCents);
        }

        [Test]
        public void Wholesale_InsufficientPayerCash_RefusesWithNothingMoved()
        {
            var catalog = new Pass3StoreCatalog();
            var intake = new Pass3WholesaleIntake();
            Pass3WholesaleIntakeResult result = IntakeBread(intake, catalog, "lot-w2", 10, 5, 2, 2);
            var storeRuntime = new BusinessRuntimeState();
            storeRuntime.BindBusinessIdentity("general-store-1");
            storeRuntime.AddCashCents(10);
            var bakeryRuntime = new BusinessRuntimeState();
            bakeryRuntime.BindBusinessIdentity("pierce-bakery-1");
            bakeryRuntime.AddCashCents(500);
            var diag = new List<string>();
            Series2WholesaleSettlementResult settled = Series2WholesaleSettlement.SettleCash(
                result.Settlement, new RuntimeCashPort(storeRuntime),
                new RuntimeCashPort(bakeryRuntime), diag);
            Assert.IsFalse(settled.Settled, "a payer that cannot cover the amount must not settle");
            Assert.IsFalse(result.Settlement.Settled);
            Assert.AreEqual(10, storeRuntime.CurrentCashCents);
            Assert.AreEqual(500, bakeryRuntime.CurrentCashCents);
        }

        [Test]
        public void Wholesale_CashSettlement_RefusesCashPortsForDifferentBusinesses()
        {
            var catalog = new Pass3StoreCatalog();
            Pass3WholesaleIntakeResult result = IntakeBread(
                new Pass3WholesaleIntake(), catalog, "lot-wrong-party", 10, 5, 2, 2);
            var actualStore = new BusinessRuntimeState();
            actualStore.BindBusinessIdentity("unrelated-store");
            actualStore.AddCashCents(1000);
            var actualBakery = new BusinessRuntimeState();
            actualBakery.BindBusinessIdentity("pierce-bakery-1");
            actualBakery.AddCashCents(500);
            var diag = new List<string>();

            Series2WholesaleSettlementResult settled = Series2WholesaleSettlement.SettleCash(
                result.Settlement,
                new RuntimeCashPort(actualStore),
                new RuntimeCashPort(actualBakery), diag);

            Assert.IsFalse(settled.Settled, "a matching balance is insufficient when the payer identity is wrong");
            Assert.IsFalse(result.Settlement.Settled);
            Assert.AreEqual(1000, actualStore.CurrentCashCents, "mismatch must move no payer cash");
            Assert.AreEqual(500, actualBakery.CurrentCashCents, "mismatch must move no payee cash");
        }

        [Test]
        public void Wholesale_Intake_RequiresRealDistinctBusinessPartiesWithoutConsumingLot()
        {
            var catalog = new Pass3StoreCatalog();
            var intake = new Pass3WholesaleIntake();
            var diag = new List<string>();

            Assert.IsNull(intake.Receive("lot-party-retry", "bread", 10, 5, 2, 2,
                catalog, "general-store-1", "", diag));
            Assert.AreEqual(0, catalog.Find("bread")?.BackUnits ?? 0,
                "invalid party data must not create or mutate stock");

            Pass3WholesaleIntakeResult retry = intake.Receive("lot-party-retry", "bread", 10, 5, 2, 2,
                catalog, "general-store-1", "pierce-bakery-1", diag);
            Assert.IsNotNull(retry, "a refused attempt must not consume the lot identity");
            Assert.AreEqual("general-store-1", retry.Settlement.PayerBusinessId);
            Assert.AreEqual("pierce-bakery-1", retry.Settlement.PayeeBusinessId);
            Assert.AreEqual(10, catalog.Find("bread").BackUnits);
        }

        [Test]
        public void Wholesale_CreditSettlement_MovesNoCash_And_CreatesRealObligation()
        {
            var catalog = new Pass3StoreCatalog();
            var intake = new Pass3WholesaleIntake();
            Pass3WholesaleIntakeResult result = IntakeBread(intake, catalog, "lot-w3", 10, 5, 2, 2);
            var authority = new FinancialObligationAuthority();
            var ids = new EntityIdRegistry();
            var creditPort = new TradeCreditAuthorityPort(authority, ids);
            var diag = new List<string>();
            Series2WholesaleSettlementResult settled = Series2WholesaleSettlement.SettleOnCredit(
                result.Settlement, creditPort, 2, diag);
            Assert.IsTrue(settled.Settled, "credit settlement should succeed; diag: " + string.Join("; ", diag));
            Assert.IsFalse(string.IsNullOrWhiteSpace(settled.ObligationId), "a real obligation id must be returned");
            Assert.IsNotNull(authority.Find(settled.ObligationId), "the obligation must exist in the one debt authority");
            Assert.AreEqual(50, authority.Find(settled.ObligationId).OriginalPrincipalCents);
        }

        [Test]
        public void Wholesale_IntakeGuards_Duplicate_Stale_And_ShelfMove()
        {
            var catalog = new Pass3StoreCatalog();
            var intake = new Pass3WholesaleIntake();
            var diag = new List<string>();
            Pass3WholesaleIntakeResult first = IntakeBread(intake, catalog, "lot-w4", 10, 5, 2, 2);
            Assert.IsNull(intake.Receive("lot-w4", "bread", 10, 5, 2, 2, catalog,
                "general-store-1", "pierce-bakery-1", diag),
                "the same lot must never intake twice");
            Assert.AreEqual(10, catalog.Find("bread").BackUnits, "duplicate intake moves no stock");
            Assert.IsNull(intake.Receive("lot-w5", "bread", 10, 5, 2, 4, catalog,
                "general-store-1", "pierce-bakery-1", diag),
                "a lot more than one day past its baked day is stale and refused");
            Assert.AreEqual(10, catalog.Find("bread").BackUnits);

            Assert.IsNotNull(Series2WholesaleShelfMove.RequireRetailPriceSet(catalog.Find("bread")),
                "a line still at its wholesale price must not be sold as retail without an owner price decision");
            Assert.IsTrue(catalog.Find("bread").SetPrice(8, diag), "owner sets retail price as an instant decision");
            Assert.IsNull(Series2WholesaleShelfMove.RequireRetailPriceSet(catalog.Find("bread")));

            Series2ShelfMoveResult move = Series2WholesaleShelfMove.MoveToShelf(catalog, "bread", 6, diag);
            Assert.IsTrue(move.Moved, "back-to-shelf move should succeed; diag: " + string.Join("; ", diag));
            Assert.AreEqual("gs.move-back-to-shelf", move.TaskActionId, "the move must be named as the Pass 2 back-to-shelf task");
            Assert.AreEqual(2, move.TaskMinutesPerLot, "Pass 2 back-to-shelf task is 2 minutes per lot");
            Assert.AreEqual(4, catalog.Find("bread").BackUnits);
            Assert.AreEqual(6, catalog.Find("bread").ShelfUnits);
            Series2ShelfMoveResult refused = Series2WholesaleShelfMove.MoveToShelf(catalog, "bread", 99, diag);
            Assert.IsFalse(refused.Moved, "moving more than back stock holds must refuse");
            Assert.AreEqual(4, catalog.Find("bread").BackUnits, "refused move leaves stock untouched");
            Assert.AreEqual(6, catalog.Find("bread").ShelfUnits);
            Assert.IsNotNull(first.Settlement, "intake settlement record must exist alongside the stock record");
        }

        [Test]
        public void SupplierDirectory_HoldsBakeryAndFuelAdapters()
        {
            BakerySetup s = BakeOneBatch(bakeryCash: 2000, bakeDay: 2);
            var bakeryAdapter = new Series2BakerySupplierAdapter(
                s.Bakery, new RuntimeCashPort(s.BakeryRuntime),
                "pierce-bakery-1", "Pierce Bakery", "bakery");
            bakeryAdapter.DayIndex = 2;
            Series2FuelDealerSupplierAdapter fuelAdapter = NewFuelAdapter(
                out FuelYardByproductIntake intake, out BusinessRuntimeState dealerRuntime, out EntityIdRegistry ids);
            var directory = new SupplierDirectory();
            Series2BakerySupplierAdapter.RegisterIn(directory, bakeryAdapter);
            Series2FuelDealerSupplierAdapter.RegisterIn(directory, fuelAdapter);
            Assert.AreEqual(2, directory.All.Count, "both new adapters must register in the one directory");
            Assert.IsTrue(bakeryAdapter.OffersItem("bread"));
            Assert.IsTrue(fuelAdapter.OffersItem("fuel_wood"));
            Assert.IsFalse(bakeryAdapter.OffersItem("fuel_wood"), "adapters offer only their own goods, never each other's");
            Assert.IsFalse(fuelAdapter.OffersItem("bread"));
        }
    }
}
