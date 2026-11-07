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
    /// Plan 04 (2026-11-06) N-slice: the bakery's daily notions step —
    /// receive due arrivals, feed the day's need (1 notion/batch) from
    /// sellable store lots, order the remaining shortfall for the
    /// pipeline. Fixture mirrors Series2Pass6BreadDriversTests (real
    /// market + procurement service + notions supply + bakery runtime)
    /// and BakeryWholesaleDailyDriverTests (real production driver,
    /// oven + kit gates, wholesale account) — the driver tests pass
    /// nulls for the store transfer seam, so the bake itself is
    /// observed on the bakery shelf with no transfer.
    ///
    /// Prices, quantities, and transit figures are test parameters,
    /// never canon — except the production-terms test, which pins the
    /// catalog's authored terms (10c/unit, 14 transit days) as the
    /// values the forwarder reads from ImportCatalog. Unity
    /// compilation, Test Runner, PlayMode, and the forwarder call site
    /// are NOT claimed; SimulationDrivers is harness-excluded, so the
    /// forwarder is source-reviewed only and exercised here through
    /// the same public APIs it calls.
    /// </summary>
    [TestFixture]
    public sealed class BakeryNotionsDailySupplyTests
    {
        private const string BakeryInstanceId = "pierce-bakery-1";
        private const string StoreInstanceId = "general-store-1";

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
            public Series2NotionsSupply Notions;
            public BakeryShopRuntime Bakery = new BakeryShopRuntime(BakeryInstanceId);
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public Series2BakeryProductionDriver ProductionDriver;
        }

        private static Setup NewSetup(int storeCash = 5000, int bakeryCash = 2000)
        {
            var s = new Setup();
            s.StoreRuntime.BindBusinessIdentity(StoreInstanceId);
            s.StoreRuntime.AddCashCents(storeCash);
            s.StoreRuntime.EnsureCategoryStock("staple_food", 0, 1000);
            s.BakeryRuntime.BindBusinessIdentity(BakeryInstanceId);
            s.BakeryRuntime.AddCashCents(bakeryCash);
            s.StoreService = new OffMapNamedSkuProcurementService(
                StoreInstanceId, s.Market, new RuntimeCashPort(s.StoreRuntime));
            s.Procurement = new Series2FlourProcurementDriver(s.StoreService);
            s.Notions = new Series2NotionsSupply(s.Procurement);
            Assert.IsNull(s.Bakery.AddOven("bakehouse", new List<string>
            {
                "oven-chamber-1", "kneading-table-1", "proofing-rack-1", "bake-peels-1",
            }, s.Diag), "oven installation should succeed");
            s.ProductionDriver = new Series2BakeryProductionDriver(
                s.Bakery, new Series2InStockBakeryFuelSource(s.Bakery));
            return s;
        }

        private static Series2CounterpartyRegistration Registration(
            string supplierId, string skuId, int unitCost, int freight, int supply, int transitDays)
        {
            var reg = new Series2CounterpartyRegistration
            {
                SupplierId = supplierId,
                SupplierName = supplierId + " Co.",
                SkuId = skuId,
                UnitGoodsCostCents = unitCost,
                FreightSurchargeCents = freight,
                InitialBudgetCents = 50000,
                TransitDays = transitDays,
            };
            reg.SupplyUnits[skuId] = supply;
            return reg;
        }

        /// <summary>Production terms, as the forwarder reads them from
        /// ImportCatalog: 10c/unit, 14 transit days, railhead origin,
        /// finite external order of FiniteExternalOrderUnits.</summary>
        private static Series2CounterpartyRegistration ProductionRegistration()
        {
            var reg = new Series2CounterpartyRegistration
            {
                SupplierId = BakeryNotionsDailySupply.NotionsSupplierId,
                SupplierName = BakeryNotionsDailySupply.NotionsSupplierName,
                SkuId = BakeryFlourSupply.NotionsMaterialId,
                UnitGoodsCostCents = 10,
                FreightSurchargeCents = 0,
                InitialBudgetCents = 0,
                TransitDays = 14,
            };
            reg.SupplyUnits[BakeryFlourSupply.NotionsMaterialId] =
                BakeryNotionsDailySupply.FiniteExternalOrderUnits;
            return reg;
        }

        private static void OrderAndReceive(Setup s, Series2CounterpartyRegistration reg, int units, int orderDay)
        {
            Assert.IsNull(reg.RegisterInto(s.Market, s.Diag), "counterparty registration should succeed");
            NamedSkuOrder order = s.Notions.PlaceNotionsOrder(
                reg, units, orderDay, NamedSkuPaymentMode.CashOnArrival, null, s.Diag);
            Assert.IsNotNull(order, "order should be placed; diag: " + string.Join("; ", s.Diag));
            List<NamedSkuLot> lots = s.Notions.ReceiveArrivals(orderDay + reg.TransitDays, null, s.Diag);
            Assert.AreEqual(1, lots.Count, "one lot should arrive; diag: " + string.Join("; ", s.Diag));
        }

        private static BakeryNotionsDailySupplyInputs InputsFor(
            Setup s, Series2CounterpartyRegistration reg, int batchesToday, int dayIndex) =>
            new BakeryNotionsDailySupplyInputs
            {
                NotionsSupply = s.Notions,
                StoreRuntime = s.StoreRuntime,
                BakeryRuntime = s.BakeryRuntime,
                Bakery = s.Bakery,
                IdRegistry = s.Ids,
                Registration = reg,
                StoreBusinessId = StoreInstanceId,
                BakeryBusinessId = BakeryInstanceId,
                BatchesToday = batchesToday,
                DayIndex = dayIndex,
            };

        private static void SeedInputs(Setup s, int flourLb, int notions, int fuel)
        {
            if (flourLb > 0)
                Assert.IsNull(s.Bakery.FlourStock.ReceiveLot(new BakeryFlourLot
                {
                    LotId = s.Ids.Allocate(EntityKind.Lot),
                    FlourName = BakeryBreadCatalog.FlourItemId,
                    Units = flourLb,
                    AcquiredDayIndex = 0,
                    ImportOrderId = "test-flour-order",
                    OriginName = "Test mill",
                }, s.Diag));
            if (notions > 0)
                Assert.IsNull(s.Bakery.FlourStock.ReceiveLot(new BakeryFlourLot
                {
                    LotId = s.Ids.Allocate(EntityKind.Lot),
                    FlourName = BakeryBreadCatalog.NotionsItemId,
                    Units = notions,
                    AcquiredDayIndex = 0,
                    ImportOrderId = "test-notions-order",
                    OriginName = "Test notions",
                }, s.Diag));
            if (fuel > 0)
                Assert.IsNull(s.Bakery.FuelStock.ReceiveLot(new BakeryFuelLot
                {
                    LotId = s.Ids.Allocate(EntityKind.Lot),
                    FuelName = BakeryBreadCatalog.FuelItemId,
                    Units = fuel,
                    AcquiredDayIndex = 0,
                    ImportOrderId = "test-fuel-order",
                    OriginName = "Test fuel",
                    IsBootstrapEndowment = false,
                }, s.Diag));
        }

        private static void SeedStoreFlour(Setup s, int sacks)
        {
            if (sacks <= 0) return;
            Assert.IsNull(s.StoreRuntime.TryAddRetailItemLot(
                "opening-endowment:dev:general-store:seed:flour", "flour", "staple_food", "10 lb sack",
                sacks, StoreInstanceId, StoreInstanceId,
                "DevelopmentTownConfiguration opening inventory, general-store flour", 0),
                "store flour lot seeding should succeed");
        }

        private static BakeryWholesaleAccount BookAccount(Setup s, int units, int cadence, int price, int dueDay)
        {
            var account = new BakeryWholesaleAccount
            {
                BuyerBusinessId = StoreInstanceId,
                BuyerType = BusinessType.GeneralStore,
                ProductId = BakeryBreadCatalog.BreadLoafId,
                UnitsPerDelivery = units,
                DeliveryCadenceDays = cadence,
                PricePerUnitCents = price,
                Freshness = BakeryWholesaleFreshness.FreshOrDayOld,
                NextDeliveryDayIndex = dueDay,
            };
            Assert.IsNull(s.Bakery.AddWholesaleAccount(account, s.Diag), "account booking should succeed");
            return account;
        }

        private static BakeryWholesaleDailyInputs DriverInputsFor(
            Setup s, Series2CounterpartyRegistration reg, int dayIndex) =>
            new BakeryWholesaleDailyInputs
            {
                Bakery = s.Bakery,
                ProductionDriver = s.ProductionDriver,
                DayIndex = dayIndex,
                BakerKitUsable = true,
                OvenDefinition = WorkstationCatalog.BakeOven,
                FindOvenComponent = OvenFinder(),
                IdRegistry = s.Ids,
                StorePort = null,
                StoreCashPort = new Series2BusinessRuntimeCashPort(s.StoreRuntime),
                BakeryCashPort = new Series2BusinessRuntimeCashPort(s.BakeryRuntime),
                StoreBusinessId = StoreInstanceId,
                BakeryBusinessId = BakeryInstanceId,
                Intake = null,
                StoreCatalog = null,
                StoreRuntimeState = s.StoreRuntime,
                BakeryRuntimeState = s.BakeryRuntime,
                StoreFlourUnitPriceCents = 29,
                MillbrookQuote = new MillbrookFlourQuote
                {
                    SacksAvailable = 0,
                    GoodsPricePerSackCents = null,
                    OneWayMiles = 50f,
                    FreightQuoteCents = 125,
                    FreightAvailable = false,
                    FreightUnavailableReason = "freight blocked: no Millbrook shipment has departed",
                },
                MillbrookSupplier = null,
                ImportFlourPricePerLbCents = 3,
                ImportTransitDays = 14,
                ImportOrderPathExists = false,
                NotionsSupply = s.Notions,
                NotionsRegistration = reg,
            };

        [Test]
        public void FeedFromArrivedLots_BinGainsExactlyTheDaysNotions_CashMovesOnce()
        {
            var s = NewSetup();
            var reg = Registration("test-notions", "notions", 10, 0, 50, 1);
            OrderAndReceive(s, reg, 5, 0);
            Assert.AreEqual(4950, s.StoreRuntime.CurrentCashCents, "store pays 5u x 10c on arrival");
            Assert.AreEqual(5, s.Procurement.SellableUnits("notions"));

            BakeryNotionsDailySupplyResult result =
                BakeryNotionsDailySupply.RunDay(InputsFor(s, reg, 1, 1), s.Diag);

            Assert.IsTrue(result.SupplyWired);
            Assert.AreEqual(1, result.NotionsNeeded, "one batch needs one notion");
            Assert.AreEqual(1, result.NotionsFed, "exactly the day's need is fed");
            Assert.AreEqual(0, result.NotionsOrdered, "the feed covers the need, so nothing is ordered");
            Assert.AreEqual(1, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.NotionsItemId), "bin gains exactly 1 notion");
            Assert.AreEqual(4, s.Procurement.SellableUnits("notions"), "one store lot unit consumed");
            Assert.AreEqual(1990, s.BakeryRuntime.CurrentCashCents, "bakery pays exactly 10c for the feed");
            Assert.AreEqual(4960, s.StoreRuntime.CurrentCashCents, "store regains the 10c feed price; net -40c for the 5u arrival");
            Assert.IsTrue(string.Join(" | ", s.Diag).Contains("notions quote 10c/unit"),
                "diagnostics log the notions quote; diag: " + string.Join(" | ", s.Diag));
        }

        [Test]
        public void ProductionTerms_OrderArrivesAfterFourteenDays_PriceTenCents()
        {
            var s = NewSetup();
            var reg = ProductionRegistration();
            Assert.IsNull(reg.RegisterInto(s.Market, s.Diag), "production counterparty registration should succeed");

            BakeryNotionsDailySupplyResult ordered =
                BakeryNotionsDailySupply.RunDay(InputsFor(s, reg, 1, 2), s.Diag);

            Assert.IsTrue(ordered.SupplyWired);
            Assert.AreEqual(0, ordered.NotionsFed, "nothing sellable yet, so nothing is fed");
            Assert.AreEqual(1, ordered.NotionsOrdered, "the day's shortfall is ordered for the pipeline");
            Assert.AreEqual(16, ordered.NotionsArrivalDayIndex, "14 transit days from day 2");
            Assert.AreEqual(5000, s.StoreRuntime.CurrentCashCents, "no cash moves at order time (CashOnArrival)");
            Assert.AreEqual(2000, s.BakeryRuntime.CurrentCashCents, "no cash moves at order time");
            Assert.AreEqual(10, s.StoreService.Orders[0].TotalCostCents, "one notion at the catalog 10c/unit");

            BakeryNotionsDailySupplyResult fed =
                BakeryNotionsDailySupply.RunDay(InputsFor(s, reg, 1, 16), s.Diag);

            Assert.AreEqual(1, fed.NotionsFed, "the arrival feeds the day's need on day 16");
            Assert.AreEqual(0, fed.NotionsOrdered, "the feed covers the need, so nothing is ordered");
            Assert.AreEqual(1, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.NotionsItemId), "bin gains the notion on arrival day");
            Assert.AreEqual(1990, s.BakeryRuntime.CurrentCashCents, "bakery pays 10c for the feed");
            Assert.AreEqual(5000, s.StoreRuntime.CurrentCashCents, "store paid 10c on arrival and regained 10c on the feed");
        }

        [Test]
        public void PrecheckPassesOnNotionsOnceFed_BakeConsumesTheNotion()
        {
            var s = NewSetup();
            var reg = Registration("test-notions", "notions", 10, 0, 50, 1);
            SeedInputs(s, 24, 0, 2);
            OrderAndReceive(s, reg, 5, 0);

            BakeryNotionsDailySupplyResult fed =
                BakeryNotionsDailySupply.RunDay(InputsFor(s, reg, 1, 2), s.Diag);
            Assert.AreEqual(1, fed.NotionsFed);

            Assert.IsNotNull(s.ProductionDriver.PlanBatches(BakeryBreadCatalog.BreadLoafId, 1, 2, s.Diag));
            Assert.IsNull(s.ProductionDriver.Precheck(
                2, true, WorkstationCatalog.BakeOven, OvenFinder(), null, s.Diag),
                "Precheck passes on notions once fed; diag: " + string.Join(" | ", s.Diag));
            int baked = s.ProductionDriver.RunScheduledDay(
                2, true, WorkstationCatalog.BakeOven, OvenFinder(), null, s.Diag, s.Ids);
            Assert.AreEqual(1, baked, "one batch bakes; diag: " + string.Join(" | ", s.Diag));
            Assert.AreEqual(0, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.NotionsItemId), "the notion is consumed by the bake");
            Assert.AreEqual(12, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId), "flour 24 -> 12 lb");
            Assert.AreEqual(1, s.Bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId), "fuel 2 -> 1");
        }

        [Test]
        public void DuplicateSameDayCall_BuysNothingTwice()
        {
            var s = NewSetup();
            var reg = Registration("test-notions", "notions", 10, 0, 50, 1);
            OrderAndReceive(s, reg, 5, 0);

            BakeryNotionsDailySupplyResult first =
                BakeryNotionsDailySupply.RunDay(InputsFor(s, reg, 1, 1), s.Diag);
            Assert.AreEqual(1, first.NotionsFed);
            Assert.AreEqual(1990, s.BakeryRuntime.CurrentCashCents);
            Assert.AreEqual(4960, s.StoreRuntime.CurrentCashCents);

            BakeryNotionsDailySupplyResult second =
                BakeryNotionsDailySupply.RunDay(InputsFor(s, reg, 1, 1), s.Diag);
            Assert.AreEqual(0, second.NotionsFed, "the bin already holds the day's need");
            Assert.AreEqual(0, second.NotionsOrdered, "no second order the same day");
            Assert.AreEqual(1, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.NotionsItemId), "bin still holds exactly 1 notion");
            Assert.AreEqual(4, s.Procurement.SellableUnits("notions"), "no second lot unit consumed");
            Assert.AreEqual(1990, s.BakeryRuntime.CurrentCashCents, "cash unchanged by the second call");
            Assert.AreEqual(4960, s.StoreRuntime.CurrentCashCents, "cash unchanged by the second call");
        }

        [Test]
        public void SecondSameDayOrderRefused_OneOrderPerDay()
        {
            var s = NewSetup();
            var reg = Registration("test-notions", "notions", 10, 0, 50, 1);
            Assert.IsNull(reg.RegisterInto(s.Market, s.Diag));

            BakeryNotionsDailySupplyResult first =
                BakeryNotionsDailySupply.RunDay(InputsFor(s, reg, 1, 2), s.Diag);
            Assert.AreEqual(1, first.NotionsOrdered, "the shortfall is ordered once");
            Assert.AreEqual(1, s.StoreService.Orders.Count);

            BakeryNotionsDailySupplyResult second =
                BakeryNotionsDailySupply.RunDay(InputsFor(s, reg, 1, 2), s.Diag);
            Assert.AreEqual(0, second.NotionsOrdered, "no second order the same day");
            Assert.AreEqual(1, s.StoreService.Orders.Count, "still exactly one order");
            Assert.AreEqual(5000, s.StoreRuntime.CurrentCashCents, "no cash moves on a refused second order");
            Assert.AreEqual(2000, s.BakeryRuntime.CurrentCashCents, "no cash moves on a refused second order");
        }

        [Test]
        public void OrderBeyondFiniteSupply_Refuses_NothingMoves()
        {
            var s = NewSetup();
            var reg = Registration("test-notions", "notions", 10, 0, 1, 1);
            Assert.IsNull(reg.RegisterInto(s.Market, s.Diag));

            BakeryNotionsDailySupplyResult result =
                BakeryNotionsDailySupply.RunDay(InputsFor(s, reg, 2, 2), s.Diag);

            Assert.AreEqual(0, result.NotionsOrdered, "two notions cannot be ordered from a supply of one");
            Assert.AreEqual(0, result.NotionsFed);
            Assert.IsTrue(result.Refusal != null && result.Refusal.Contains("refused"),
                "the refusal names the failed order; refusal: " + result.Refusal);
            Assert.AreEqual(0, s.StoreService.Orders.Count, "no order is recorded");
            Assert.AreEqual(5000, s.StoreRuntime.CurrentCashCents, "no cash moves");
            Assert.AreEqual(2000, s.BakeryRuntime.CurrentCashCents, "no cash moves");
        }

        [Test]
        public void UnwiredSupply_RefusesLoudly_OrdersAndFeedsNothing()
        {
            var s = NewSetup();
            var reg = Registration("test-notions", "notions", 10, 0, 50, 1);
            Assert.IsNull(reg.RegisterInto(s.Market, s.Diag));

            var noSupply = InputsFor(s, reg, 1, 2);
            noSupply.NotionsSupply = null;
            BakeryNotionsDailySupplyResult unwired =
                BakeryNotionsDailySupply.RunDay(noSupply, s.Diag);
            Assert.IsTrue(!unwired.SupplyWired);
            Assert.IsTrue(unwired.Refusal != null && unwired.Refusal.Contains("unwired"),
                "the refusal names the unwired supply; refusal: " + unwired.Refusal);
            Assert.AreEqual(0, unwired.NotionsFed);
            Assert.AreEqual(0, unwired.NotionsOrdered);

            var noRegistration = InputsFor(s, reg, 1, 2);
            noRegistration.Registration = null;
            BakeryNotionsDailySupplyResult unregistered =
                BakeryNotionsDailySupply.RunDay(noRegistration, s.Diag);
            Assert.IsTrue(!unregistered.SupplyWired);
            Assert.IsTrue(unregistered.Refusal != null && unregistered.Refusal.Contains("unwired"),
                "the refusal names the missing registration; refusal: " + unregistered.Refusal);
            Assert.AreEqual(0, unregistered.NotionsFed);
            Assert.AreEqual(0, unregistered.NotionsOrdered);

            Assert.AreEqual(5000, s.StoreRuntime.CurrentCashCents, "no cash moves");
            Assert.AreEqual(2000, s.BakeryRuntime.CurrentCashCents, "no cash moves");
        }

        [Test]
        public void ServiceBelongsToAnotherBusiness_Refuses_NothingMoves()
        {
            var s = NewSetup();
            var reg = Registration("test-notions", "notions", 10, 0, 50, 1);
            Assert.IsNull(reg.RegisterInto(s.Market, s.Diag));
            var otherService = new OffMapNamedSkuProcurementService(
                "other-store-1", s.Market, new RuntimeCashPort(s.StoreRuntime));
            var otherNotions = new Series2NotionsSupply(new Series2FlourProcurementDriver(otherService));

            var inputs = InputsFor(s, reg, 1, 2);
            inputs.NotionsSupply = otherNotions;
            BakeryNotionsDailySupplyResult result = BakeryNotionsDailySupply.RunDay(inputs, s.Diag);

            Assert.IsTrue(!result.SupplyWired);
            Assert.IsTrue(result.Refusal != null && result.Refusal.Contains("other-store-1"),
                "the refusal names the owning business; refusal: " + result.Refusal);
            Assert.AreEqual(0, result.NotionsFed);
            Assert.AreEqual(0, result.NotionsOrdered);
            Assert.AreEqual(5000, s.StoreRuntime.CurrentCashCents, "no cash moves");
            Assert.AreEqual(2000, s.BakeryRuntime.CurrentCashCents, "no cash moves");
        }

        [Test]
        public void DriverFeedsNotionsThenBakes_EndToEnd()
        {
            // Flour comes from the store (2 sacks @ 29c, slice 2A);
            // notions come through the pipeline, not SeedInputs: one
            // notion ordered day 1 (transit 1) arrives day 2 and feeds
            // the bin before Precheck. The account is due tomorrow, so
            // the bake is observed on the shelf with no transfer (the
            // store transfer seam is null in this fixture).
            var s = NewSetup();
            var reg = Registration("test-notions", "notions", 10, 0, 50, 1);
            Assert.IsNull(reg.RegisterInto(s.Market, s.Diag));
            Assert.IsNotNull(s.Notions.PlaceNotionsOrder(
                reg, 1, 1, NamedSkuPaymentMode.CashOnArrival, null, s.Diag),
                "the day-1 notions order should be placed; diag: " + string.Join("; ", s.Diag));
            SeedInputs(s, 0, 0, 1);
            SeedStoreFlour(s, 2);
            BookAccount(s, 24, 1, 2, 3);

            BakeryWholesaleDailyResult result =
                BakeryWholesaleDailyDriver.RunDay(DriverInputsFor(s, reg, 2), s.Diag);

            Assert.AreEqual(1, result.BatchesPlanned);
            Assert.AreEqual(2, result.FlourSacksPurchased);
            Assert.AreEqual(1, result.NotionsUnitsFed, "the arrived notion feeds the bin before Precheck");
            Assert.AreEqual(0, result.NotionsOrdered, "the feed covers the need, so nothing is ordered");
            Assert.AreEqual(1, result.BatchesBaked, "the first pipeline-fed bake");
            Assert.AreEqual(0, result.TransfersCompleted, "the account is not due today");
            Assert.AreEqual(1, s.Bakery.BreadShelf.Count, "one batch baked");
            Assert.AreEqual(8, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId), "bin flour 20 -> 8 lb");
            Assert.AreEqual(0, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.NotionsItemId), "the notion is consumed by the bake");
            Assert.AreEqual(0, s.Bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId), "fuel 1 -> 0");
            Assert.AreEqual(1932, s.BakeryRuntime.CurrentCashCents, "bakery 2,000 - 58c flour - 10c notions");
            Assert.AreEqual(5058, s.StoreRuntime.CurrentCashCents, "store 5,000 - 10c arrival + 58c flour + 10c notions");
            string diag = string.Join(" | ", s.Diag);
            Assert.IsTrue(diag.Contains("notions quote 10c/unit"),
                "diagnostics log the notions quote; diag: " + diag);
        }

        [Test]
        public void DriverSecondSameDayCall_BuysNothingTwice()
        {
            // The bake refuses on fuel, so the fed notion stays in the
            // bin: the repeated same-day call must feed and order nothing
            // twice, move no cash, and leave exactly one delivery record
            // state (no bake, no transfer).
            var s = NewSetup();
            var reg = Registration("test-notions", "notions", 10, 0, 50, 1);
            Assert.IsNull(reg.RegisterInto(s.Market, s.Diag));
            Assert.IsNotNull(s.Notions.PlaceNotionsOrder(
                reg, 1, 1, NamedSkuPaymentMode.CashOnArrival, null, s.Diag),
                "the day-1 notions order should be placed; diag: " + string.Join("; ", s.Diag));
            SeedInputs(s, 0, 0, 0);
            SeedStoreFlour(s, 2);
            BookAccount(s, 24, 1, 2, 2);

            BakeryWholesaleDailyResult first =
                BakeryWholesaleDailyDriver.RunDay(DriverInputsFor(s, reg, 2), s.Diag);
            Assert.AreEqual(1, first.NotionsUnitsFed, "the first call feeds the day's notion");
            Assert.AreEqual(0, first.BatchesBaked, "no fuel, so nothing bakes");
            Assert.IsTrue(first.Refusal != null && first.Refusal.Contains("fuel"),
                "first call refuses on fuel; refusal: " + first.Refusal);
            Assert.AreEqual(1932, s.BakeryRuntime.CurrentCashCents);
            Assert.AreEqual(5058, s.StoreRuntime.CurrentCashCents);
            Assert.AreEqual(1, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.NotionsItemId), "the notion stays in the bin");

            BakeryWholesaleDailyResult second =
                BakeryWholesaleDailyDriver.RunDay(DriverInputsFor(s, reg, 2), s.Diag);
            Assert.AreEqual(0, second.NotionsUnitsFed, "the bin already holds the day's need");
            Assert.AreEqual(0, second.NotionsOrdered, "no second order the same day");
            Assert.AreEqual(0, second.BatchesBaked);
            Assert.AreEqual(0, second.TransfersCompleted);
            Assert.AreEqual(1932, s.BakeryRuntime.CurrentCashCents, "cash unchanged by the second call");
            Assert.AreEqual(5058, s.StoreRuntime.CurrentCashCents, "cash unchanged by the second call");
            Assert.AreEqual(1, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.NotionsItemId), "bin still holds exactly 1 notion");
            Assert.AreEqual(1, s.StoreService.Orders.Count, "still exactly one notions order");
        }

        [Test]
        public void Driver_RefeedsExistingPlannedBatch_WhenNoNewWholesaleBatchIsCreated()
        {
            var s = NewSetup();
            var reg = ProductionRegistration();
            Assert.IsNull(reg.RegisterInto(s.Market, s.Diag));
            SeedInputs(s, 24, 0, 1);
            BookAccount(s, 24, 30, 2, 1);

            BakeryWholesaleDailyResult first = BakeryWholesaleDailyDriver.RunDay(
                DriverInputsFor(s, reg, 1), s.Diag);
            Assert.AreEqual(1, first.BatchesPlanned);
            Assert.AreEqual(1, first.NotionsOrdered,
                "the planned batch should place one real notions order");
            Assert.AreEqual(0, first.BatchesBaked,
                "the order is in transit, so no notions are fabricated or batch completed");
            Assert.AreEqual(1, s.Bakery.Batches.Count);
            Assert.AreEqual(BakeryBatchStage.Planned, s.Bakery.Batches[0].Stage);

            // The existing planned batch already covers wholesale demand,
            // so the next day's planner creates no new ID. Its real import
            // arrival must still be received and fed to that active batch.
            BakeryWholesaleDailyResult retry = BakeryWholesaleDailyDriver.RunDay(
                DriverInputsFor(s, reg, 15), s.Diag);

            Assert.AreEqual(0, retry.BatchesPlanned,
                "no second batch should be created while the original covers demand");
            Assert.AreEqual(1, retry.NotionsUnitsFed,
                "the pending import should transfer from the store procurement lot into Bakery custody");
            Assert.AreEqual(1, retry.BatchesBaked,
                "the existing batch can now proceed through the normal production authority");
            Assert.AreEqual(1, s.Bakery.Batches.Count,
                "retrying the existing work must not duplicate production");
            Assert.AreEqual(BakeryBatchStage.Baked, s.Bakery.Batches[0].Stage);
            Assert.AreEqual(0, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.NotionsItemId),
                "the legitimate notion is consumed by the completed batch");
            Assert.AreEqual(1, s.Bakery.BreadShelf.Count,
                "one identifiable bread batch is produced");
            Assert.AreEqual(1, s.StoreService.Orders.Count,
                "recovery must not place a duplicate supplier order");
        }
    }
}
