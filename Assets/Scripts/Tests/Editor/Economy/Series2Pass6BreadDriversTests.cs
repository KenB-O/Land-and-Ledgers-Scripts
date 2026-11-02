using System;
using System.Collections.Generic;
using NUnit.Framework;
using LandLedgers.Economy;
using LandLedgers.Economy.BreadDrivers;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Core;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Primitives;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Series 2 Pass 6 (bread drivers): Roslyn acceptance, production
    /// classes as the system under test. The ThreeBusinessSlice /
    /// SixPersonSliceRun orchestrators are regression references only
    /// and are never used here. All prices, quantities, and freight
    /// figures are test parameters, never canon. Unity compilation,
    /// Test Runner, PlayMode, and scene wiring are NOT claimed.
    /// </summary>
    [TestFixture]
    public sealed class Series2Pass6BreadDriversTests
    {
        private sealed class RuntimeCashPort : IBusinessCashPort
        {
            private readonly BusinessRuntimeState runtime;
            public RuntimeCashPort(BusinessRuntimeState runtime) { this.runtime = runtime; }
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

        private sealed class Setup
        {
            public List<string> Diag = new List<string>();
            public ExternalMarketAuthority Market = new ExternalMarketAuthority();
            public BusinessRuntimeState StoreRuntime = new BusinessRuntimeState();
            public BusinessRuntimeState BakeryRuntime = new BusinessRuntimeState();
            public OffMapNamedSkuProcurementService StoreService;
            public Series2FlourProcurementDriver Procurement;
            public BakeryShopRuntime Bakery = new BakeryShopRuntime("pierce-bakery-1");
            public EntityIdRegistry Ids = new EntityIdRegistry();
        }

        private static Setup NewSetup(int storeCash, int bakeryCash)
        {
            var s = new Setup();
            s.StoreRuntime.BindBusinessIdentity("general-store-1");
            s.StoreRuntime.AddCashCents(storeCash);
            s.BakeryRuntime.BindBusinessIdentity("pierce-bakery-1");
            s.BakeryRuntime.AddCashCents(bakeryCash);
            s.StoreService = new OffMapNamedSkuProcurementService(
                "general-store-1", s.Market, new RuntimeCashPort(s.StoreRuntime));
            s.Procurement = new Series2FlourProcurementDriver(s.StoreService);
            Assert.IsNull(s.Bakery.AddOven("bakehouse", new List<string>
            {
                "oven-chamber-1", "kneading-table-1", "proofing-rack-1", "bake-peels-1",
            }, s.Diag), "oven installation should succeed");
            return s;
        }

        private static Series2CounterpartyRegistration Registration(
            string supplierId, string skuId, int unitCost, int freight, int supply)
        {
            var reg = new Series2CounterpartyRegistration
            {
                SupplierId = supplierId,
                SupplierName = supplierId + " Co.",
                SkuId = skuId,
                UnitGoodsCostCents = unitCost,
                FreightSurchargeCents = freight,
                InitialBudgetCents = 50000,
                TransitDays = 1,
            };
            reg.SupplyUnits[skuId] = supply;
            return reg;
        }

        private static void OrderAndReceive(Setup s, Series2CounterpartyRegistration reg, int units, int orderDay)
        {
            Assert.IsNull(reg.RegisterInto(s.Market, s.Diag), "counterparty registration should succeed");
            var notions = new Series2NotionsSupply(s.Procurement);
            NamedSkuOrder order;
            if (reg.SkuId == "notions")
                order = notions.PlaceNotionsOrder(reg, units, orderDay, NamedSkuPaymentMode.CashOnArrival, null, s.Diag);
            else
            {
                OffMapFreightQuote quote = Series2ProcurementQuoteBuilder.Build(
                    reg, units, FreightOptionKind.SellerFreight, s.Diag);
                Assert.IsNotNull(quote);
                order = s.Procurement.PlaceOrderFromQuote(
                    quote, orderDay, NamedSkuPaymentMode.CashOnArrival, null, s.Diag);
            }
            Assert.IsNotNull(order, "order should be placed; diag: " + string.Join("; ", s.Diag));
            List<NamedSkuLot> lots = s.Procurement.ReceiveArrivals(orderDay + 1, null, s.Diag);
            Assert.AreEqual(1, lots.Count, "one lot should arrive; diag: " + string.Join("; ", s.Diag));
        }

        private static void SeedFuel(Setup s, int units)
        {
            string problem = s.Bakery.FuelStock.ReceiveLot(new BakeryFuelLot
            {
                LotId = s.Ids.Allocate(EntityKind.Lot),
                FuelName = BakeryBreadCatalog.FuelItemId,
                Units = units,
                AcquiredDayIndex = 0,
                ImportOrderId = "test-fuel-order",
                OriginName = "Test fuel",
                IsBootstrapEndowment = false,
            }, s.Diag);
            Assert.IsNull(problem, "fuel seeding should succeed");
        }

        [Test]
        public void FlourToBakeToShelf_CashReconciles()
        {
            var s = NewSetup(storeCash: 5000, bakeryCash: 2000);
            OrderAndReceive(s, Registration("test-grain", "flour", 10, 250, 200), 20, 0);
            Assert.AreEqual(4550, s.StoreRuntime.CurrentCashCents, "store pays 20u x 10c + 250c freight on arrival");
            OrderAndReceive(s, Registration("test-notions", "notions", 10, 0, 50), 5, 0);
            Assert.AreEqual(4500, s.StoreRuntime.CurrentCashCents, "store pays 5u x 10c notions on arrival");

            var conversion = new Series2FlourUnitConversion
            {
                LbPerStoreUnit = 10,
                StoreUnitDescription = "10 lb sack",
                ExpectedLbPerNamedUnit = 10,
            };
            Assert.IsNull(Series2StoreToBakeryFeed.FeedFlour(
                s.StoreService, s.StoreRuntime, s.Bakery, s.BakeryRuntime, s.Ids,
                "flour", 2, conversion, 20, 2, s.Diag), "flour feed should succeed");
            Assert.AreEqual(20, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId));
            Assert.AreEqual(4540, s.StoreRuntime.CurrentCashCents, "store gains 2u x 20c feed price");
            Assert.AreEqual(1960, s.BakeryRuntime.CurrentCashCents, "bakery pays 40c feed price");

            var notions = new Series2NotionsSupply(s.Procurement);
            Assert.IsNull(notions.FeedNotionsToBakery(
                s.StoreService, s.StoreRuntime, s.Bakery, s.BakeryRuntime, s.Ids,
                "notions", 1, 1, 10, 2, s.Diag), "notions feed should succeed");
            Assert.AreEqual(1, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.NotionsItemId));
            Assert.AreEqual(4550, s.StoreRuntime.CurrentCashCents, "store gains 1u x 10c notions feed price");
            Assert.AreEqual(1950, s.BakeryRuntime.CurrentCashCents, "bakery pays 10c notions feed price");

            SeedFuel(s, 2);
            var driver = new Series2BakeryProductionDriver(
                s.Bakery, new Series2InStockBakeryFuelSource(s.Bakery));
            Assert.IsNotNull(driver.PlanBatches(BakeryBreadCatalog.BreadLoafId, 1, 2, s.Diag));
            int baked = driver.RunScheduledDay(
                2, true, WorkstationCatalog.BakeOven, OvenFinder(), null, s.Diag, s.Ids);
            Assert.AreEqual(1, baked, "one batch should bake; diag: " + string.Join("; ", s.Diag));
            Assert.AreEqual(1, s.Bakery.BreadShelf.Count);
            Assert.AreEqual(BakeryBreadCatalog.BreadLoafId, s.Bakery.BreadShelf[0].ProductId);
            Assert.AreEqual(BakeryBreadCatalog.BreadYieldPerBatch, s.Bakery.BreadShelf[0].UnitCount);
            Assert.AreEqual(BakeryGoodsCondition.Fresh, s.Bakery.BreadShelf[0].Condition);
            Assert.AreEqual(2, s.Bakery.BreadShelf[0].BakedDayIndex);
            Assert.AreEqual(8, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId), "20 lb fed minus 12 lb baked");
            Assert.AreEqual(0, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.NotionsItemId), "1 notion baked");
            Assert.AreEqual(1, s.Bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId), "2 fuel minus 1 firing");
            Assert.AreEqual(4550, s.StoreRuntime.CurrentCashCents, "baking moves no cash");
            Assert.AreEqual(1950, s.BakeryRuntime.CurrentCashCents, "baking moves no cash");
        }

        [Test]
        public void FeedFlour_BakeryCashShortfall_MovesNothing()
        {
            var s = NewSetup(storeCash: 5000, bakeryCash: 10);
            OrderAndReceive(s, Registration("test-grain", "flour", 10, 0, 200), 20, 0);
            int sellableBefore = s.Procurement.SellableUnits("flour");
            var conversion = new Series2FlourUnitConversion { LbPerStoreUnit = 10, StoreUnitDescription = "10 lb sack" };
            string problem = Series2StoreToBakeryFeed.FeedFlour(
                s.StoreService, s.StoreRuntime, s.Bakery, s.BakeryRuntime, s.Ids,
                "flour", 2, conversion, 20, 2, s.Diag);
            Assert.IsNotNull(problem, "feed must refuse when bakery cash cannot pay");
            Assert.AreEqual(sellableBefore, s.Procurement.SellableUnits("flour"), "consumption must be undone");
            Assert.AreEqual(10, s.BakeryRuntime.CurrentCashCents);
            Assert.AreEqual(0, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId));
        }

        [Test]
        public void FlourConversion_IsExplicit_OneLbVsTenLb()
        {
            var s = NewSetup(storeCash: 5000, bakeryCash: 2000);
            OrderAndReceive(s, Registration("test-grain", "flour", 10, 0, 200), 20, 0);
            var oneLb = new Series2FlourUnitConversion { LbPerStoreUnit = 1, StoreUnitDescription = "1 lb unit" };
            Assert.IsNull(Series2StoreToBakeryFeed.FeedFlour(
                s.StoreService, s.StoreRuntime, s.Bakery, s.BakeryRuntime, s.Ids,
                "flour", 2, oneLb, 0, 2, s.Diag));
            Assert.AreEqual(2, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId),
                "the run's old 1 lb conversion yields 2 lb from 2 store units, not 20");
            var mismatch = new Series2FlourUnitConversion
            {
                LbPerStoreUnit = 1,
                StoreUnitDescription = "10 lb sack",
                ExpectedLbPerNamedUnit = 10,
            };
            Assert.IsNull(mismatch.Validate(s.Diag));
            StringAssert.Contains("WARNING", string.Join("\n", s.Diag),
                "a sack description with a 1 lb applied number must warn loudly");
        }

        [Test]
        public void RunScheduledDay_NoFuel_RefusesAndBakesNothing()
        {
            var s = NewSetup(storeCash: 5000, bakeryCash: 2000);
            OrderAndReceive(s, Registration("test-grain", "flour", 10, 0, 200), 20, 0);
            OrderAndReceive(s, Registration("test-notions", "notions", 10, 0, 50), 5, 0);
            var conversion = new Series2FlourUnitConversion { LbPerStoreUnit = 10, StoreUnitDescription = "10 lb sack" };
            Assert.IsNull(Series2StoreToBakeryFeed.FeedFlour(
                s.StoreService, s.StoreRuntime, s.Bakery, s.BakeryRuntime, s.Ids,
                "flour", 2, conversion, 0, 2, s.Diag));
            var notions = new Series2NotionsSupply(s.Procurement);
            Assert.IsNull(notions.FeedNotionsToBakery(
                s.StoreService, s.StoreRuntime, s.Bakery, s.BakeryRuntime, s.Ids,
                "notions", 1, 1, 0, 2, s.Diag));
            var driver = new Series2BakeryProductionDriver(
                s.Bakery, new Series2InStockBakeryFuelSource(s.Bakery));
            Assert.IsNotNull(driver.PlanBatches(BakeryBreadCatalog.BreadLoafId, 1, 2, s.Diag));
            int baked = driver.RunScheduledDay(
                2, true, WorkstationCatalog.BakeOven, OvenFinder(), null, s.Diag, s.Ids);
            Assert.AreEqual(-1, baked, "no fuel means no bake");
            Assert.AreEqual(0, s.Bakery.BreadShelf.Count);
            Assert.AreEqual(BakeryBatchStage.Planned, s.Bakery.Batches[0].Stage, "batch stays planned");
        }

        [Test]
        public void RunScheduledDay_EarlyDay_Refuses()
        {
            var s = NewSetup(storeCash: 5000, bakeryCash: 2000);
            var driver = new Series2BakeryProductionDriver(
                s.Bakery, new Series2InStockBakeryFuelSource(s.Bakery));
            Assert.IsNotNull(driver.PlanBatches(BakeryBreadCatalog.BreadLoafId, 1, 5, s.Diag));
            int baked = driver.RunScheduledDay(
                4, true, WorkstationCatalog.BakeOven, OvenFinder(), null, s.Diag, s.Ids);
            Assert.AreEqual(-1, baked, "nothing bakes early");
            Assert.AreEqual(0, s.Bakery.BreadShelf.Count);
        }

        [Test]
        public void QuoteTotalMismatch_Refused_NoOrder()
        {
            var s = NewSetup(storeCash: 5000, bakeryCash: 2000);
            var reg = Registration("test-grain", "flour", 10, 250, 200);
            Assert.IsNull(reg.RegisterInto(s.Market, s.Diag));
            var badQuote = new OffMapFreightQuote
            {
                SupplierId = "test-grain",
                SkuId = "flour",
                QuantityUnits = 20,
                FreightOption = FreightOptionKind.SellerFreight,
                GoodsCents = 200,
                FreightCents = 250,
                DeliveredTotalCents = 999,
            };
            NamedSkuOrder order = s.Procurement.PlaceOrderFromQuote(
                badQuote, 0, NamedSkuPaymentMode.CashOnArrival, null, s.Diag);
            Assert.IsNull(order, "a tampered quote total must never become an order");
            Assert.AreEqual(0, s.StoreService.Orders.Count);
        }

        [Test]
        public void NotionsFeed_BadConversion_Refused()
        {
            var s = NewSetup(storeCash: 5000, bakeryCash: 2000);
            OrderAndReceive(s, Registration("test-notions", "notions", 10, 0, 50), 5, 0);
            var notions = new Series2NotionsSupply(s.Procurement);
            string problem = notions.FeedNotionsToBakery(
                s.StoreService, s.StoreRuntime, s.Bakery, s.BakeryRuntime, s.Ids,
                "notions", 1, 0, 10, 2, s.Diag);
            Assert.IsNotNull(problem, "a zero notions conversion must refuse, not guess");
            Assert.AreEqual(5, s.Procurement.SellableUnits("notions"));
        }

        [Test]
        public void SaveRegistration_NamesPassXlProcurementService()
        {
            var s = NewSetup(storeCash: 5000, bakeryCash: 2000);
            var registration = new Series2ProcurementSaveRegistration(s.StoreService);
            Assert.AreEqual("PassXlProcurementService", registration.TargetProperty);
            Assert.AreEqual(
                "saveLoadManager.PassXlProcurementService = flourDriver.Service;",
                registration.AssignmentLine("saveLoadManager", "flourDriver.Service"));
            OffMapNamedSkuProcurementService assigned = null;
            registration.Apply(service => assigned = service);
            Assert.AreSame(s.StoreService, assigned, "Apply hands Codex the exact service instance, nothing else");
        }
    }
}
