using System;
using System.Collections.Generic;
using NUnit.Framework;
using LandLedgers.Economy;
using LandLedgers.Economy.Core;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.BreadDrivers;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Economy.Logistics;
using LandLedgers.Economy.Ordering;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Plan 04 Slice 0 (2026-11-07): multi-day bakery-supplies
    /// discriminator + probes. Tests only; no production file is
    /// touched. Fixture combines BakeryWholesaleDailyDriverTests
    /// (real production driver, oven + kit gates, wholesale account,
    /// store 5,000c, bakery 2,000c, staple_food 0/1000, port price 5c)
    /// with BakeryNotionsDailySupplyTests (real market + procurement
    /// service + notions supply; production terms 10c/unit, 14 transit
    /// days, 30u finite supply).
    ///
    /// Fixed-behaviour form: each test asserts the correct end state, so
    /// a passing test means the transition works and a failing test names
    /// the broken seam. REVISION (Plan 04 follow-up, 2026-11-07,
    /// coordinator decision ~20:40 EST): Tests 2 and 3 were rewritten
    /// from the over-ordering reading to the NOT-A-DEFECT priming
    /// reading Slice 2 proved (14 priming orders = 14 future daily
    /// bakes; orders placed while flour-blocked parallelise the two
    /// pipelines). Expected at HEAD: ALL tests PASS, including the
    /// rewritten Tests 2 and 3 and the Slice 2 priming test.
    ///
    /// Fixture deviations from the plan text (test-side, recorded):
    /// (a) Test 1 seeds its 12 lb of test flour on day 1, not day 15:
    /// Precheck checks flour BEFORE notions, so flour seeded only on
    /// day 15 would make days 2-14 refuse on flour and the plan's
    /// notions-refusal assertion could never hold. Isolation intent is
    /// preserved: the flour is test-seeded, never purchased (the store
    /// holds 0 sacks). (b) Test 4 books the account due day 1, not
    /// day 2, so the day-1 re-runs can transfer; all re-runs stay on
    /// day 1. (c) Test 5 uses a 1-day-transit test registration so the
    /// day-3 order arrives day 4; the production 14-day terms are
    /// pinned by Tests 1 and 2 instead.
    ///
    /// Unity compilation, Test Runner, PlayMode, and the forwarder call
    /// site are NOT claimed; SimulationDrivers is harness-excluded, so
    /// the forwarder is source-reviewed only and exercised here through
    /// the same public APIs it calls.
    /// </summary>
    [TestFixture]
    public sealed class BakerySuppliesTests
    {
        private const string BakeryInstanceId = "pierce-bakery-1";
        private const string StoreInstanceId = "general-store-1";

        private static readonly string[] OvenComponentIds =
        {
            "oven-chamber-1", "kneading-table-1", "proofing-rack-1", "bake-peels-1",
        };

        // ---- production port over a real BusinessRuntimeState --------
        // Copied from BakeryWholesaleDailyDriverTests: the production
        // GeneralStoreTradingPort wraps a MonoBehaviour manager and cannot
        // be constructed in Roslyn; this seam implements the same
        // production interface over the same BusinessRuntimeState the
        // wholesale cash port uses, so wholesale cash and retail cash
        // reconcile in ONE runtime. It is a test seam, not a second cash
        // authority: every cash move lands in the shared runtime.
        private sealed class RuntimeStorePort : IGeneralStoreTradingPort
        {
            public BusinessRuntimeState Runtime;
            public Dictionary<string, int> Prices = new Dictionary<string, int>();
            public int Cap = int.MaxValue;
            public List<GeneralStoreCategoryOffer> OfferList = new List<GeneralStoreCategoryOffer>();

            public string StoreBusinessId => "general-store-1";
            public string StoreName => "General Store";
            public string LocationId => "general-store";
            public IReadOnlyList<GeneralStoreCategoryOffer> CategoryOffers => OfferList;
            public StoreTradingCapability TradingCapability => new StoreTradingCapability { Staffed = true };
            public int CurrentCashCents => Runtime != null ? Runtime.CurrentCashCents : 0;
            public bool OffersDelivery => false;
            public bool OffersTradeCredit => false;
            public int TradeCreditLimitCents => 0;

            public int CategoryStockUnits(string categoryId)
            {
                CategoryStockState stock = Runtime != null ? Runtime.GetCategoryStock(categoryId) : null;
                return stock != null ? stock.CurrentStockUnits : 0;
            }
            public int CategoryTargetStockUnits(string categoryId)
            {
                CategoryStockState stock = Runtime != null ? Runtime.GetCategoryStock(categoryId) : null;
                return stock != null ? stock.TargetStockUnits : 0;
            }
            public int CategoryPricePerUnitCents(string categoryId)
            {
                return Prices.TryGetValue(categoryId, out int p) ? p : 0;
            }
            public bool TryConsumeStoreUnits(string categoryId, int units, out int consumedUnits)
            {
                consumedUnits = 0;
                return Runtime != null && Runtime.TryConsumeCategoryStockUnits(categoryId, units, out consumedUnits);
            }
            public void RecordCashSettlement(string categoryId, int unitsSold, int revenueCents)
            {
                if (Runtime != null) Runtime.RecordGenericRetailSettlement(categoryId, unitsSold, revenueCents);
            }
            public void RecordCreditSettlement(string categoryId, int unitsSold, int amountCents, string obligationId) { }
            public string SpendCash(int amountCents, string purpose)
            {
                if (Runtime == null) return "no runtime";
                if (Runtime.CurrentCashCents < amountCents) return "cannot afford";
                Runtime.SpendCents(amountCents);
                return null;
            }
            public string ReceiveStock(string categoryId, int units, string provenanceLabel)
            {
                if (Runtime == null) return "no runtime";
                if (units > Cap) return $"capped port refuses {units}u (cap {Cap})";
                int accepted = Runtime.AddCategoryStockUnits(categoryId, units);
                if (accepted < units) return $"store accepted {accepted}/{units}u {categoryId}: category full.";
                return null;
            }
            public int CategoryReceivableUnits(string categoryId)
            {
                CategoryStockState stock = Runtime != null ? Runtime.GetCategoryStock(categoryId) : null;
                if (stock == null) return 0;
                if (Cap < int.MaxValue) return Math.Min(Cap, stock.TargetStockUnits > 0 ? Math.Max(0, stock.TargetStockUnits - stock.CurrentStockUnits) : Cap);
                return stock.TargetStockUnits > 0 ? Math.Max(0, stock.TargetStockUnits - stock.CurrentStockUnits) : int.MaxValue;
            }
            public int DeliveryFeeCents(string itemId, int units) => 0;
        }

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

        private sealed class Slice
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
            public RuntimeStorePort StorePort;
            public Pass3StoreCatalog Catalog = new Pass3StoreCatalog();
            public Pass3WholesaleIntake Intake = new Pass3WholesaleIntake();
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

        private static Slice NewSlice(int storeCash = 5000, int bakeryCash = 2000)
        {
            var s = new Slice();
            s.StoreRuntime.BindBusinessIdentity(StoreInstanceId);
            s.StoreRuntime.AddCashCents(storeCash);
            s.StoreRuntime.EnsureCategoryStock("staple_food", 0, 1000);
            s.BakeryRuntime.BindBusinessIdentity(BakeryInstanceId);
            s.BakeryRuntime.AddCashCents(bakeryCash);
            s.StoreService = new OffMapNamedSkuProcurementService(
                StoreInstanceId, s.Market, new RuntimeCashPort(s.StoreRuntime));
            s.Procurement = new Series2FlourProcurementDriver(s.StoreService);
            s.Notions = new Series2NotionsSupply(s.Procurement);
            Assert.IsNull(s.Bakery.AddOven("bakehouse", new List<string>(OvenComponentIds), s.Diag),
                "oven installation should succeed");
            s.ProductionDriver = new Series2BakeryProductionDriver(
                s.Bakery, new Series2InStockBakeryFuelSource(s.Bakery));
            s.StorePort = new RuntimeStorePort { Runtime = s.StoreRuntime };
            s.StorePort.Prices["staple_food"] = 5;
            s.StorePort.OfferList.Add(new GeneralStoreCategoryOffer
            {
                CategoryId = "staple_food",
                OfferedItemIds = new List<string> { "bread" },
                StoreUnitsPerItemUnit = 1,
            });
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

        private static void OrderAndReceive(Slice s, Series2CounterpartyRegistration reg, int units, int orderDay)
        {
            Assert.IsNull(reg.RegisterInto(s.Market, s.Diag), "counterparty registration should succeed");
            NamedSkuOrder order = s.Notions.PlaceNotionsOrder(
                reg, units, orderDay, NamedSkuPaymentMode.CashOnArrival, null, s.Diag);
            Assert.IsNotNull(order, "order should be placed; diag: " + string.Join("; ", s.Diag));
            List<NamedSkuLot> lots = s.Notions.ReceiveArrivals(orderDay + reg.TransitDays, null, s.Diag);
            Assert.AreEqual(1, lots.Count, "one lot should arrive; diag: " + string.Join("; ", s.Diag));
        }

        private static void SeedInputs(Slice s, int flourLb, int notions, int fuel)
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

        private static void SeedStoreFlour(Slice s, int sacks)
        {
            if (sacks <= 0) return;
            Assert.IsNull(s.StoreRuntime.TryAddRetailItemLot(
                "opening-endowment:dev:general-store:seed:flour", "flour", "staple_food", "10 lb sack",
                sacks, StoreInstanceId, StoreInstanceId,
                "DevelopmentTownConfiguration opening inventory, general-store flour", 0),
                "store flour lot seeding should succeed");
        }

        private static void RestockStoreFlour(Slice s, int sacks)
        {
            // Test-only restock between days (Test 6): a distinct lot id
            // from the opening seed, so the restock never collides with it.
            if (sacks <= 0) return;
            Assert.IsNull(s.StoreRuntime.TryAddRetailItemLot(
                "test-restock:general-store:flour", "flour", "staple_food", "10 lb sack",
                sacks, StoreInstanceId, StoreInstanceId,
                "BakerySuppliesTests test restock between bake days", 0),
                "store flour restock should succeed");
        }

        private static BakeryWholesaleAccount BookAccount(Slice s, int units, int cadence, int price, int dueDay)
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
            Slice s, Series2CounterpartyRegistration reg, int dayIndex,
            bool wireNotions = true, OffMapAidBusinessRuntime millbrookSupplier = null,
            bool threadCumulative = false,
            BakeryWholesaleDailyResult prior = default(BakeryWholesaleDailyResult)) =>
            new BakeryWholesaleDailyInputs
            {
                Bakery = s.Bakery,
                ProductionDriver = s.ProductionDriver,
                DayIndex = dayIndex,
                BakerKitUsable = true,
                OvenDefinition = WorkstationCatalog.BakeOven,
                FindOvenComponent = OvenFinder(),
                IdRegistry = s.Ids,
                StorePort = s.StorePort,
                StoreCashPort = new Series2BusinessRuntimeCashPort(s.StoreRuntime),
                BakeryCashPort = new Series2BusinessRuntimeCashPort(s.BakeryRuntime),
                StoreBusinessId = StoreInstanceId,
                BakeryBusinessId = BakeryInstanceId,
                Intake = s.Intake,
                StoreCatalog = s.Catalog,
                StoreRuntimeState = s.StoreRuntime,
                BakeryRuntimeState = s.BakeryRuntime,
                StoreFlourUnitPriceCents = 29,
                MillbrookQuote = millbrookSupplier != null
                    ? new MillbrookFlourQuote
                    {
                        SacksAvailable = 23,
                        GoodsPricePerSackCents = 48,
                        OneWayMiles = 50f,
                        FreightQuoteCents = 125,
                        FreightAvailable = false,
                        FreightUnavailableReason = "freight blocked: no Millbrook shipment has departed",
                    }
                    : new MillbrookFlourQuote
                    {
                        SacksAvailable = 0,
                        GoodsPricePerSackCents = null,
                        OneWayMiles = 50f,
                        FreightQuoteCents = 125,
                        FreightAvailable = false,
                        FreightUnavailableReason = "freight blocked: no Millbrook shipment has departed (staging/T49 — Obj 16–18)",
                    },
                MillbrookSupplier = millbrookSupplier,
                ImportFlourPricePerLbCents = 3,
                ImportTransitDays = 14,
                ImportOrderPathExists = false,
                NotionsSupply = wireNotions ? s.Notions : null,
                NotionsRegistration = wireNotions ? reg : null,
                // Plan 04 Slice 4: the test threads cumulative totals
                // across days exactly as the forwarder does.
                PriorCumulativeBatchesBaked = prior.CumulativeBatchesBaked,
                PriorCumulativeFlourCashSpentCents = prior.CumulativeFlourCashSpentCents,
                PriorCumulativeFlourSacksPurchased = prior.CumulativeFlourSacksPurchased,
                PriorCumulativeFlourPoundsPurchased = prior.CumulativeFlourPoundsPurchased,
                PriorCumulativeNotionsCashSpentCents = prior.CumulativeNotionsCashSpentCents,
                PriorCumulativeWholesaleRevenueCents = prior.CumulativeWholesaleRevenueCents,
                CumulativeActualsThreaded = threadCumulative,
            };

        private static BakeryWholesaleDailyResult RunDay(
            Slice s, Series2CounterpartyRegistration reg, int dayIndex,
            bool wireNotions = true, OffMapAidBusinessRuntime millbrookSupplier = null,
            bool threadCumulative = false,
            BakeryWholesaleDailyResult prior = default(BakeryWholesaleDailyResult)) =>
            BakeryWholesaleDailyDriver.RunDay(
                DriverInputsFor(s, reg, dayIndex, wireNotions, millbrookSupplier, threadCumulative, prior), s.Diag);

        private static BakeryFlourLot FindBinLot(Slice s, string flourName)
        {
            foreach (BakeryFlourLot lot in s.Bakery.FlourStock.Lots)
                if (lot != null && string.Equals(lot.FlourName, flourName, StringComparison.Ordinal))
                    return lot;
            return null;
        }

        private static OffMapAidBusinessDefinition MillbrookDefinition()
        {
            // Row values copied verbatim from the authored Millbrook asset
            // (10 lb / 18 bakery units / 48c / 230 starting), as in
            // BakeryWholesaleDailyDriverTests and MillbrookFlourFreightCustodyTests.
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
                        StartingUnits = 230,
                        OrderUnit = "10 lb sack",
                        InventoryUnitsPerOrderUnit = 10,
                        BakeryUnitsPerOrderUnit = 18,
                        OrderPriceCents = 48,
                    },
                },
            };
        }

        private static void ReceiveMillbrookLot(
            Slice s, MillbrookFlourFreightCustodyAdapter adapter, OffMapAidCargoLot lot, int dayIndex)
        {
            var shipment = new LogisticsShipmentState
            {
                shipmentId = "test-shipment:" + lot.LotId,
                sourceBusinessInstanceId = "millbrook_grain_co",
                destinationBusinessInstanceId = BakeryInstanceId,
                cargoLotId = lot.LotId,
                cargoSkuId = MillbrookFlourFreightCustodyAdapter.FlourSkuId,
                plannedQuantityUnits = lot.PackageUnits,
                remainingQuantityUnits = lot.PackageUnits,
                state = LogisticsShipmentStatus.Planned,
            };
            Assert.IsTrue(adapter.CommitSourceLot(shipment, lot.PackageUnits, out string pickupReason),
                $"commit of lot '{lot.LotId}' should succeed; reason: {pickupReason}");
            Assert.IsTrue(adapter.ReceiveCargoLot(shipment, lot.PackageUnits, dayIndex, out string receiptReason),
                $"receipt of lot '{lot.LotId}' should succeed; reason: {receiptReason}");
        }

        [Test]
        public void NotionsPipeline_Day1Order_ArrivesDay15_FeedsBakesTransfers_CashReconciles()
        {
            // THE discriminator: one notions order day 1, arrival day 15,
            // feed, same-batch bake, transfer, both cash legs reconciled.
            // Bin empty of notions, store flour 0 sacks (no local purchase
            // interferes); flour is test-seeded 12 lb on day 1 (see the
            // class header: Precheck is flour-first, so a day-15-only
            // seed would refuse on flour, not notions, days 2-14).
            Slice s = NewSlice();
            Series2CounterpartyRegistration reg = ProductionRegistration();
            Assert.IsNull(reg.RegisterInto(s.Market, s.Diag), "production counterparty registration should succeed");
            SeedInputs(s, 12, 0, 40);
            BakeryWholesaleAccount account = BookAccount(s, 24, 1, 2, 2);

            BakeryWholesaleDailyResult day1 = RunDay(s, reg, 1);
            Assert.AreEqual(1, day1.BatchesPlanned, "day 1 plans the batch for the day-2 delivery");
            Assert.AreEqual(1, day1.NotionsOrdered, "one notion ordered day 1");
            Assert.AreEqual(15, day1.NotionsArrivalDayIndex, "14 transit days from day 1");
            Assert.AreEqual(0, day1.BatchesBaked, "nothing bakes before the arrival");
            Assert.AreEqual(1, s.StoreService.Orders.Count, "exactly one notions order exists after day 1");
            NamedSkuOrder order = s.StoreService.Orders[0];
            Assert.AreEqual(1, order.OrderDayIndex, "the order is a day-1 order");
            Assert.AreEqual(15, order.ExpectedArrivalDayIndex, "the order arrives day 15");
            Assert.AreEqual(10, order.TotalCostCents, "one notion at the catalog 10c/unit");
            Assert.AreEqual(5000, s.StoreRuntime.CurrentCashCents, "no cash moves at order time (CashOnArrival)");
            Assert.AreEqual(2000, s.BakeryRuntime.CurrentCashCents, "no cash moves at order time");
            Assert.AreEqual(10, s.Market.TotalOutflowCents,
                "the market books the 10c purchase outflow at order time (RecordExternalPurchase: cash out across the boundary to the market)");
            string batchId = s.Bakery.Batches[0].BatchId;

            for (int day = 2; day <= 14; day++)
            {
                BakeryWholesaleDailyResult r = RunDay(s, reg, day);
                Assert.AreEqual(0, r.BatchesPlanned, $"already covered after day 1, day {day}");
                Assert.AreEqual(1, s.ProductionDriver.PlannedBatchIds.Count, $"one active batch, day {day}");
                Assert.AreEqual(batchId, s.ProductionDriver.PlannedBatchIds[0], $"same batch id, day {day}");
                Assert.AreEqual(0, r.BatchesBaked, $"nothing bakes before the notions arrival, day {day}");
                Assert.IsTrue(r.Refusal != null && r.Refusal.Contains("notions"),
                    $"day {day} refuses on notions; refusal: {r.Refusal}");
                Assert.AreEqual(BakeryBatchStage.Planned, s.Bakery.Batches[0].Stage, $"the batch stays Planned, day {day}");
                Assert.AreEqual(batchId, s.Bakery.Batches[0].BatchId, $"same batch id, day {day}");
            }

            // Day 15: receive the arrival first (the store pays the
            // market 10c), then run the day (feed, bake, transfer).
            List<NamedSkuLot> arrived = s.Notions.ReceiveArrivals(15, null, s.Diag);
            Assert.AreEqual(1, arrived.Count, "exactly one lot arrives day 15");
            Assert.AreEqual(4990, s.StoreRuntime.CurrentCashCents, "store pays 10c at receipt, before the feed");
            Assert.AreEqual(1, s.StoreService.Lots.Count, "exactly one store service lot for the arrival");
            NamedSkuLot lot = s.StoreService.Lots[0];
            Assert.AreEqual(BakeryFlourSupply.NotionsMaterialId, lot.SkuId, "the lot is the notions SKU");
            Assert.AreEqual(StoreInstanceId, lot.BusinessId, "the lot belongs to the store service");
            Assert.AreEqual(1, lot.QuantityUnits, "the lot holds the one ordered notion");

            BakeryWholesaleDailyResult day15 = RunDay(s, reg, 15);
            Assert.AreEqual(1, day15.NotionsUnitsFed, "the arrival feeds the day's need on day 15");
            Assert.AreEqual(0, day15.NotionsOrdered, "the feed covers the need, so nothing is ordered on arrival day");
            Assert.AreEqual(1, day15.BatchesBaked, "the SAME batch id bakes on arrival day");
            Assert.AreEqual(batchId, s.Bakery.Batches[0].BatchId, "same batch id bakes");
            Assert.AreEqual(BakeryBatchStage.Baked, s.Bakery.Batches[0].Stage, "the batch is Baked on day 15");
            Assert.AreEqual(1, day15.TransfersCompleted, "the transfer completes on arrival day");
            Assert.AreEqual(24, day15.UnitsStocked, "24 loaves stocked");
            Assert.AreEqual(4952, s.StoreRuntime.CurrentCashCents, "store 5,000 - 10c receipt + 10c feed - 48c wholesale");
            Assert.AreEqual(2038, s.BakeryRuntime.CurrentCashCents, "bakery 2,000 - 10c feed + 48c wholesale");
            Assert.AreEqual(1, s.StoreService.Lots.Count, "no second lot exists for the day-15 arrival");
            Assert.AreEqual(1, s.Bakery.WholesaleDeliveries.Count, "exactly one delivery record");
            Assert.AreEqual(3, account.NextDeliveryDayIndex, "account advanced once (2 -> 3)");
            Assert.AreEqual(0, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.NotionsItemId),
                "the notion is consumed by the bake");
            // The fed bin lot itself is fully consumed by the bake and
            // dropped by design ("drop emptied lots so the bin never
            // carries ghost stock"), so its provenance is pinned where it
            // survives: the feed diagnostic and the delivery record's
            // provenance chain, both naming the store service.
            string diag15 = string.Join(" | ", s.Diag);
            Assert.IsTrue(diag15.Contains("from general-store-1 became 1 unit(s) 'bakery-notions' in the bakery bin"),
                "the feed diagnostic names the store service as the source; diag: " + diag15);
            BakeryWholesaleDeliveryRecord record = s.Bakery.WholesaleDeliveries[0];
            bool notionsProvenanceNamesStore = false;
            foreach (string line in record.ProvenanceLines)
                if (line != null && line.Contains("bakery-notions") && line.Contains("general-store-1"))
                    notionsProvenanceNamesStore = true;
            Assert.IsTrue(notionsProvenanceNamesStore,
                "the delivery record's provenance chain names the store service for the fed notion; lines: "
                + string.Join(" | ", record.ProvenanceLines));
        }

        [Test]
        public void NotionsPipeline_Priming_FourteenOrders_ForFourteenFutureBakes()
        {
            // NOT-A-DEFECT REVISION (Plan 04 follow-up, 2026-11-07,
            // coordinator decision ~20:40 EST): the 14 orders placed
            // over days 1-14 for one blocked batch are PRIMING, not
            // over-ordering. Each day's order is earmarked for a distinct
            // future daily bake (order day d arrives day d+14 and bakes
            // that day), because the per-day shortfall is the account's
            // DAILY demand rate (1 batch/day), not the single active
            // batch's need. Slice 2 proved this against Set B: bakes on
            // days 15-18 needed orders placed days 1-4; subtracting
            // in-transit units (Branch A) would order one notion per
            // transit window instead of one per bake, a 15-day bake
            // cycle from a cold start. DO NOT "fix" this test back to
            // 1 order / 1u: that expectation is unsatisfiable together
            // with daily baking, and the priming test below
            // (NotionsPipeline_PrimedWindowBakesThenDrains) pins the
            // same 14 consecutive daily bakes days 15-28 plus the
            // pipeline drain that follows. This test owns the
            // order-count side and the order-to-bake linkage; the
            // priming test owns the drain cycle.
            Slice s = NewSlice();
            Series2CounterpartyRegistration reg = ProductionRegistration();
            Assert.IsNull(reg.RegisterInto(s.Market, s.Diag), "production counterparty registration should succeed");
            SeedInputs(s, 0, 0, 40);
            BookAccount(s, 24, 1, 2, 2);

            for (int day = 1; day <= 14; day++)
            {
                SeedInputs(s, 12, 0, 0);
                BakeryWholesaleDailyResult r = RunDay(s, reg, day);
                if (day == 1)
                    Assert.AreEqual(1, r.BatchesPlanned, "day 1 plans the batch");
                Assert.AreEqual(1, r.NotionsOrdered, $"one priming notion ordered, day {day}");
                Assert.AreEqual(day, s.StoreService.Orders.Count, $"exactly {day} priming order(s) after day {day}");
                Assert.AreEqual(0, r.BatchesBaked, $"nothing bakes before the first arrival, day {day}");
                Assert.IsTrue(r.Refusal != null && r.Refusal.Contains("notions"),
                    $"day {day} refuses on notions; refusal: {r.Refusal}");
            }
            int orderedUnits = 0;
            foreach (NamedSkuOrder o in s.StoreService.Orders)
                if (o != null) orderedUnits += o.UnitsOrdered;
            Assert.AreEqual(14, s.StoreService.Orders.Count,
                "14 priming orders over days 1-14, one per future daily bake — not one per active batch");
            Assert.AreEqual(14, orderedUnits, "14u ordered in total, one per future bake");
            Assert.AreEqual(140, s.Market.TotalOutflowCents, "14 orders x 10c booked as market outflow at order time");
            for (int i = 0; i < 14; i++)
            {
                NamedSkuOrder o = s.StoreService.Orders[i];
                Assert.AreEqual(i + 1, o.OrderDayIndex, $"priming order {i + 1} placed day {i + 1}");
                Assert.AreEqual(i + 15, o.ExpectedArrivalDayIndex, $"priming order {i + 1} arrives day {i + 15} for that day's bake");
                Assert.AreEqual(1, o.UnitsOrdered, $"priming order {i + 1} is 1u");
                Assert.AreEqual(10, o.TotalCostCents, $"priming order {i + 1} costs 10c");
            }
            // The 30u finite supply leaves 16u after the 14 priming
            // units are committed: supply pressure, not an ordering
            // defect (a standing wholesale term is D-1-adjacent).
            Assert.AreEqual(16, 30 - orderedUnits, "30u finite supply minus 14 priming units leaves 16u");

            for (int day = 15; day <= 28; day++)
            {
                SeedInputs(s, 12, 0, 0);
                BakeryWholesaleDailyResult r = RunDay(s, reg, day);
                Assert.AreEqual(1, r.BatchesBaked, $"the primed pipeline bakes daily, day {day}");
                Assert.AreEqual(1, r.TransfersCompleted, $"transfer completes, day {day}");
                Assert.AreEqual(0, r.NotionsOrdered, $"no replacement order on an arrival day (feed covers), day {day}");
                Assert.AreEqual(1, r.NotionsUnitsFed, $"the day-{day - 14} priming order's unit feeds the day {day} bake");
                Assert.AreEqual(day - 14, s.StoreService.Lots.Count, $"one arrival lot per priming order, day {day}");
            }
            Assert.AreEqual(14, s.Bakery.WholesaleDeliveries.Count,
                "14 consecutive daily bakes = 14 deliveries: every priming unit is consumed by its own day's bake");
            Assert.AreEqual(14, s.StoreService.Lots.Count, "14 arrival lots, one per priming order");
            Assert.AreEqual(0, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.NotionsItemId),
                "no priming notion is left unconsumed after day 28");
            Assert.AreEqual(2532, s.BakeryRuntime.CurrentCashCents,
                "bakery 2,000 - 140c notions feeds + 672c wholesale (14 x 48c)");
            Assert.AreEqual(4328, s.StoreRuntime.CurrentCashCents,
                "store 5,000 - 140c receipts + 140c feeds - 672c wholesale");
        }

        [Test]
        public void NotionsPipeline_FlourBlockedDays_PrimingOrders_EnableEarlyFirstBake()
        {
            // NOT-A-DEFECT REVISION (Plan 04 follow-up, 2026-11-07,
            // coordinator decision ~20:40 EST): notions orders ARE still
            // placed (1/day) on flour-blocked days, because the pipeline
            // primes future bakes that run once flour arrives. Ordering
            // notions while flour-blocked parallelises the two pipelines;
            // it is not waste. Slice 2 proved this against Set B: the
            // first bake (day 15) required a notions order placed on day
            // 1, days BEFORE flour landed (~day 7 via freight), so
            // suppressing notions orders until flour is on hand
            // (Branch B) delays the first bake to (flour day + 14).
            // DO NOT "fix" this test back to 0 orders on flour-blocked
            // days: that expectation is unsatisfiable together with the
            // day-15 first bake, and the two tests as previously written
            // were mutually unsatisfiable (their first 5 days identical).
            //
            // This test demonstrates the priming value in both
            // directions. Main slice: flour is blocked days 1-6 and
            // lands day 7, notions orders are placed 1/day throughout,
            // and the first bake is day 15 — flour day + 8, NOT flour
            // day + 14 — because the 5 orders placed on the
            // flour-blocked days 1-5 feed the bakes on days 15-19.
            // Control slice (the Branch B world): the notions leg is
            // unwired until flour day 7, so no priming order exists
            // before flour lands; the first order is placed day 7 and
            // the first bake is day 21, exactly flour day + 14. The
            // +14 wait applies ONLY where no priming orders were placed.
            Slice s = NewSlice();
            Series2CounterpartyRegistration reg = ProductionRegistration();
            Assert.IsNull(reg.RegisterInto(s.Market, s.Diag), "production counterparty registration should succeed");
            SeedInputs(s, 0, 0, 40);
            BookAccount(s, 24, 1, 2, 2);

            // Days 1-6: flour-blocked, notions wired — 1 order/day.
            for (int day = 1; day <= 6; day++)
            {
                BakeryWholesaleDailyResult r = RunDay(s, reg, day);
                if (day == 1)
                    Assert.AreEqual(1, r.BatchesPlanned, "day 1 plans the batch");
                Assert.AreEqual(1, r.NotionsOrdered, $"a priming notion is ordered even though the batch is flour-blocked, day {day}");
                Assert.AreEqual(day, s.StoreService.Orders.Count, $"exactly {day} priming order(s) after day {day}");
                Assert.AreEqual(0, r.BatchesBaked, $"nothing bakes while flour-blocked, day {day}");
                Assert.IsTrue(r.Refusal != null && r.Refusal.Contains("flour"),
                    $"day {day} refuses on flour (flour is checked before notions); refusal: {r.Refusal}");
            }

            // Days 7-14: flour lands (test-seeded 12 lb/day from day 7,
            // as Set B's freight flour landed ~day 7); the batch is now
            // notions-blocked, and priming orders continue 1/day.
            int firstBakeDay = -1;
            for (int day = 7; day <= 14; day++)
            {
                SeedInputs(s, 12, 0, 0);
                BakeryWholesaleDailyResult r = RunDay(s, reg, day);
                Assert.AreEqual(1, r.NotionsOrdered, $"one priming notion ordered, day {day}");
                Assert.AreEqual(day, s.StoreService.Orders.Count, $"exactly {day} priming order(s) after day {day}");
                Assert.AreEqual(0, r.BatchesBaked, $"nothing bakes before the first arrival, day {day}");
                Assert.IsTrue(r.Refusal != null && r.Refusal.Contains("notions"),
                    $"day {day} refuses on notions; refusal: {r.Refusal}");
                if (r.BatchesBaked > 0 && firstBakeDay < 0) firstBakeDay = day;
            }
            Assert.AreEqual(14, s.StoreService.Orders.Count,
                "14 priming orders by day 14 (6 placed while flour-blocked, 8 while notions-blocked)");
            Assert.AreEqual(140, s.Market.TotalOutflowCents, "14 orders x 10c booked as market outflow at order time");

            // Days 15-19: the 5 orders placed on the flour-blocked days
            // 1-5 arrive and feed 5 consecutive daily bakes — the first
            // bake is day 15, flour day + 8, with no additional 14-day
            // wait after flour arrival.
            for (int day = 15; day <= 19; day++)
            {
                SeedInputs(s, 12, 0, 0);
                BakeryWholesaleDailyResult r = RunDay(s, reg, day);
                Assert.AreEqual(1, r.BatchesBaked, $"the primed pipeline bakes daily, day {day}");
                Assert.AreEqual(1, r.TransfersCompleted, $"transfer completes, day {day}");
                Assert.AreEqual(0, r.NotionsOrdered, $"no replacement order on an arrival day (feed covers), day {day}");
                Assert.AreEqual(1, r.NotionsUnitsFed, $"the day-{day - 14} priming order's unit feeds the day {day} bake");
                if (r.BatchesBaked > 0 && firstBakeDay < 0) firstBakeDay = day;
            }
            Assert.AreEqual(15, firstBakeDay,
                "first bake day 15 = flour-arrival day 7 + 8, NOT flour day + 14: the flour-blocked-day priming orders removed the wait");
            Assert.IsTrue(firstBakeDay < 7 + 14,
                "the first bake precedes the flour-day + 14 earliest (day 21) that applies only with no priming orders");
            Assert.AreEqual(5, s.Bakery.WholesaleDeliveries.Count,
                "5 consecutive bakes days 15-19, one per flour-blocked-day priming order");
            Assert.AreEqual(5, s.StoreService.Lots.Count, "5 arrival lots by day 19, one per consumed priming order");
            Assert.AreEqual(2190, s.BakeryRuntime.CurrentCashCents,
                "bakery 2,000 - 50c notions feeds + 240c wholesale (5 x 48c)");
            Assert.AreEqual(4760, s.StoreRuntime.CurrentCashCents,
                "store 5,000 - 50c receipts + 50c feeds - 240c wholesale");

            // CONTROL — the Branch B world (no priming orders before
            // flour lands): the notions leg stays unwired days 1-6, so
            // no order exists when flour lands day 7; the first order is
            // placed day 7 and the first bake is day 21, exactly flour
            // day + 14. This is the earliest first bake available
            // WITHOUT priming — the 6 days the main slice saves.
            Slice c = NewSlice();
            Series2CounterpartyRegistration creg = ProductionRegistration();
            Assert.IsNull(creg.RegisterInto(c.Market, c.Diag), "production counterparty registration should succeed");
            SeedInputs(c, 0, 0, 40);
            BookAccount(c, 24, 1, 2, 2);

            int controlFirstBakeDay = -1;
            for (int day = 1; day <= 21; day++)
            {
                if (day >= 7) SeedInputs(c, 12, 0, 0);
                BakeryWholesaleDailyResult r = day < 7
                    ? RunDay(c, null, day, wireNotions: false)
                    : RunDay(c, creg, day);
                if (day == 1)
                    Assert.AreEqual(1, r.BatchesPlanned, "control day 1 plans the batch");
                if (day < 7)
                {
                    Assert.AreEqual(0, c.StoreService.Orders.Count, $"control: no notions order while the leg is unwired, day {day}");
                    Assert.AreEqual(0, r.BatchesBaked, $"control: nothing bakes while flour-blocked, day {day}");
                    Assert.IsTrue(r.Refusal != null && r.Refusal.Contains("flour"),
                        $"control day {day} refuses on flour; refusal: {r.Refusal}");
                }
                else if (day < 21)
                {
                    Assert.AreEqual(1, r.NotionsOrdered, $"control: one notion ordered, day {day}");
                    Assert.AreEqual(day - 6, c.StoreService.Orders.Count, $"control: exactly {day - 6} order(s) after day {day}");
                    Assert.AreEqual(0, r.BatchesBaked, $"control: nothing bakes before the first arrival, day {day}");
                    Assert.IsTrue(r.Refusal != null && r.Refusal.Contains("notions"),
                        $"control day {day} refuses on notions; refusal: {r.Refusal}");
                }
                else
                {
                    Assert.AreEqual(1, r.BatchesBaked, "control: the first bake is day 21");
                    Assert.AreEqual(1, r.TransfersCompleted, "control: the transfer completes day 21");
                    Assert.AreEqual(0, r.NotionsOrdered, "control: no replacement order on the arrival day (feed covers)");
                    Assert.AreEqual(1, r.NotionsUnitsFed, "control: the day-7 order's unit feeds the day 21 bake");
                }
                if (r.BatchesBaked > 0 && controlFirstBakeDay < 0) controlFirstBakeDay = day;
            }
            Assert.AreEqual(21, controlFirstBakeDay,
                "control first bake day 21 = flour-arrival day 7 + 14 transit days: the earliest possible first bake with no priming orders");
            Assert.AreEqual(14, c.StoreService.Orders.Count, "control: 14 orders by day 21 (days 7-20)");
            Assert.AreEqual(1, c.Bakery.WholesaleDeliveries.Count, "control: exactly one delivery by day 21");
            Assert.AreEqual(2038, c.BakeryRuntime.CurrentCashCents, "control bakery 2,000 - 10c feed + 48c wholesale");
            Assert.AreEqual(4952, c.StoreRuntime.CurrentCashCents, "control store 5,000 - 10c receipt + 10c feed - 48c wholesale");
            Assert.AreEqual(6, controlFirstBakeDay - firstBakeDay,
                "priming the pipeline during the flour-blocked window brings the first bake 6 days earlier (day 15 vs day 21)");
        }

        [Test]
        public void Flour_OneSackHaulCannotBake_TwoSackReorderDeliversAndBakes()
        {
            // One Millbrook sack = 10 lb; a batch needs 12 lb. The
            // opening 1-sack lot alone cannot bake; the daily driver must
            // place a 2-sack reorder (12 lb need -> ceil(12/10) = 2), and
            // after BOTH lots land (bin 30 lb) the batch bakes. The
            // account is due day 1 so the day-1 re-runs can transfer.
            Slice s = NewSlice();
            var supplier = new OffMapAidBusinessRuntime(MillbrookDefinition());
            var adapter = new MillbrookFlourFreightCustodyAdapter(supplier, s.Bakery, s.Ids);
            var bakeryCash = new Series2BusinessRuntimeCashPort(s.BakeryRuntime);

            Assert.IsNull(supplier.TryPlaceOrder(
                "opening-flour-b1", BakeryInstanceId, "Glen Pierce", 1, bakeryCash, 1, out OffMapAidCargoLot openingLot),
                "the opening 1-sack Millbrook order should place");
            Assert.AreEqual("aidlot-millbrook_grain_co:opening-flour-b1", openingLot.LotId, "the opening lot id");
            Assert.AreEqual(1952, s.BakeryRuntime.CurrentCashCents, "bakery 2,000 - 48c opening order, debited once");
            Assert.AreEqual(48, supplier.CashCents, "Millbrook +48c");
            Assert.AreEqual(220, supplier.GetInventoryUnits("bakery_flour_sack"), "Millbrook 230 - 10 lb");
            Assert.AreEqual(openingLot.LotId,
                MillbrookFlourReorder.FindShippableLot(supplier, BakeryInstanceId)?.LotId,
                "the opening 1-sack lot ships first (list order)");

            SeedInputs(s, 0, 1, 40);
            BookAccount(s, 24, 1, 2, 1);

            BakeryWholesaleDailyResult day1 = RunDay(s, null, 1, wireNotions: false, millbrookSupplier: supplier);
            Assert.AreEqual(1, day1.BatchesPlanned, "day 1 plans the batch");
            Assert.AreEqual(0, day1.FlourSacksPurchased, "no local purchase: the store holds 0 sacks");
            Assert.IsTrue(!string.IsNullOrWhiteSpace(day1.MillbrookReorderLotId),
                "a 2-sack Millbrook reorder is placed");
            Assert.IsTrue(day1.MillbrookReorderLotId.Contains("flour-reorder-d1-n2"),
                "reorder lot id names the reorder; lot id: " + day1.MillbrookReorderLotId);
            Assert.AreEqual(2, day1.MillbrookReorderSacks, "the reorder covers 2 sacks / 20 lb");
            Assert.AreEqual(0, day1.BatchesBaked, "nothing bakes day 1: no flour on hand");
            Assert.IsTrue(day1.Refusal != null && day1.Refusal.Contains("flour"),
                "the bake refuses on flour day 1; refusal: " + day1.Refusal);
            Assert.AreEqual(1856, s.BakeryRuntime.CurrentCashCents, "bakery 2,000 - 48c opening - 96c reorder");
            Assert.AreEqual(144, supplier.CashCents, "Millbrook +48c opening + 96c reorder");
            Assert.AreEqual(2, supplier.Lots.Count, "exactly two Millbrook lots");
            OffMapAidCargoLot reorderLot = null;
            foreach (OffMapAidCargoLot candidate in supplier.Lots)
                if (candidate != null && string.Equals(candidate.LotId, day1.MillbrookReorderLotId, StringComparison.Ordinal))
                    reorderLot = candidate;
            Assert.IsNotNull(reorderLot, "the reorder lot is in the supplier lots");
            Assert.AreEqual(2, reorderLot.PackageUnits, "the reorder lot is 2 sacks");
            Assert.AreEqual(20, reorderLot.InventoryUnits, "the reorder lot is 20 lb");
            Assert.AreEqual(openingLot.LotId,
                MillbrookFlourReorder.FindShippableLot(supplier, BakeryInstanceId)?.LotId,
                "the opening 1-sack lot still ships first with both lots outstanding");

            // The 1-sack lot alone (10 lb) still cannot bake.
            ReceiveMillbrookLot(s, adapter, openingLot, 1);
            Assert.AreEqual(10, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId), "bin 10 lb after the 1-sack receipt");
            BakeryWholesaleDailyResult afterOneSack = RunDay(s, null, 1, wireNotions: false, millbrookSupplier: supplier);
            Assert.AreEqual(0, afterOneSack.BatchesBaked, "10 lb alone cannot bake a 12 lb batch");
            Assert.IsTrue(afterOneSack.Refusal != null && afterOneSack.Refusal.Contains("flour 10 lb on hand, need 12 lb"),
                "the bake refuses naming flour 10 vs 12 lb; refusal: " + afterOneSack.Refusal);
            Assert.AreEqual(1856, s.BakeryRuntime.CurrentCashCents, "no second reorder: the outstanding 2-sack lot covers the 1-sack remainder");
            Assert.AreEqual(2, supplier.Lots.Count, "still exactly two Millbrook lots");

            // After the 2-sack lot lands (bin 30 lb) the batch bakes and transfers.
            ReceiveMillbrookLot(s, adapter, reorderLot, 1);
            Assert.AreEqual(30, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId), "bin 30 lb after both receipts");
            var millbrookBinLots = new List<BakeryFlourLot>();
            foreach (BakeryFlourLot lot in s.Bakery.FlourStock.Lots)
                if (lot != null && string.Equals(lot.FlourName, BakeryBreadCatalog.FlourItemId, StringComparison.Ordinal))
                    millbrookBinLots.Add(lot);
            Assert.AreEqual(2, millbrookBinLots.Count, "exactly two bakery bin flour lots after both receipts, both from Millbrook");
            Assert.IsTrue(!string.Equals(millbrookBinLots[0].ImportOrderId, millbrookBinLots[1].ImportOrderId, StringComparison.Ordinal),
                "the two bin lots carry distinct ImportOrderIds");
            BakeryWholesaleDailyResult afterTwoSacks = RunDay(s, null, 1, wireNotions: false, millbrookSupplier: supplier);
            Assert.AreEqual(1, afterTwoSacks.BatchesBaked, "the batch bakes once both lots land");
            Assert.AreEqual(1, afterTwoSacks.TransfersCompleted, "the transfer completes");
            Assert.AreEqual(24, afterTwoSacks.UnitsStocked, "24 loaves stocked");
            Assert.AreEqual(1904, s.BakeryRuntime.CurrentCashCents, "bakery 2,000 - 48c opening - 96c reorder + 48c wholesale");
            Assert.AreEqual(144, supplier.CashCents, "Millbrook cash is unchanged by delivery (+144c total)");
            Assert.AreEqual(18, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId), "bin 30 - 12 lb after the bake");
            // The bake dispenses FIFO: the 10 lb lot is fully consumed and
            // dropped by design ("drop emptied lots so the bin never
            // carries ghost stock"), so one lot remains — the 2-sack lot.
            var remainingBinLots = new List<BakeryFlourLot>();
            foreach (BakeryFlourLot lot in s.Bakery.FlourStock.Lots)
                if (lot != null && string.Equals(lot.FlourName, BakeryBreadCatalog.FlourItemId, StringComparison.Ordinal))
                    remainingBinLots.Add(lot);
            Assert.AreEqual(1, remainingBinLots.Count, "one bin flour lot remains after the FIFO bake");
            Assert.AreEqual(reorderLot.LotId, remainingBinLots[0].ImportOrderId, "the remaining lot is the 2-sack lot");
            Assert.AreEqual(18, remainingBinLots[0].Units, "the 2-sack lot holds 20 - 2 lb after the bake");
        }

        [Test]
        public void PlannedVsCovered_RefusedBatchStaysPlanned_RetriesWithSameId_NoDuplicate()
        {
            // A notions-blocked batch stays Planned, is retried every day
            // with the SAME batch id, and no second batch is ever planned
            // while it covers the account. Days 2-3 run with no notions
            // service at all (unwired); day 4 wires the service +
            // registration with 1u sellable in the store lots.
            Slice s = NewSlice();
            Series2CounterpartyRegistration reg = Registration(
                "test-notions", BakeryFlourSupply.NotionsMaterialId, 10, 0, 50, 1);
            Assert.IsNull(reg.RegisterInto(s.Market, s.Diag), "test counterparty registration should succeed");
            SeedInputs(s, 12, 0, 40);
            BakeryWholesaleAccount account = BookAccount(s, 24, 1, 2, 2);

            string batchId = null;
            for (int day = 2; day <= 3; day++)
            {
                int diagBefore = s.Diag.Count;
                BakeryWholesaleDailyResult r = RunDay(s, null, day, wireNotions: false);
                if (day == 2)
                {
                    Assert.AreEqual(1, r.BatchesPlanned, "day 2 plans the batch");
                    batchId = s.Bakery.Batches[0].BatchId;
                }
                else
                {
                    Assert.AreEqual(0, r.BatchesPlanned, "day 3 plans nothing: already covered");
                }
                Assert.AreEqual(1, s.ProductionDriver.PlannedBatchIds.Count, $"one active batch, day {day}");
                Assert.AreEqual(batchId, s.ProductionDriver.PlannedBatchIds[0], $"same batch id, day {day}");
                Assert.AreEqual(0, r.BatchesBaked, $"nothing bakes without notions, day {day}");
                Assert.IsTrue(r.Refusal != null && r.Refusal.Contains("notions"),
                    $"day {day} refuses on notions; refusal: {r.Refusal}");
                Assert.AreEqual(BakeryBatchStage.Planned, s.Bakery.Batches[0].Stage, $"the batch stays Planned, day {day}");
                Assert.AreEqual(1, s.Bakery.Batches.Count, $"no second batch id ever appears, day {day}");
                // Plan 04 Slice 4: the blocked batch is named once per
                // day with the refusal's own cause, the plan day, the
                // covered account and the retry.
                string dayDiag = string.Join(" | ", s.Diag.GetRange(diagBefore, s.Diag.Count - diagBefore));
                Assert.IsTrue(dayDiag.Contains($"[BakeryProduction] batch {batchId} blocked day {day} (notions)"),
                    $"day {day} names the blocked batch with the notions cause; diag: {dayDiag}");
                Assert.IsTrue(dayDiag.Contains($"planned 2, covered 24u for account {account.AccountId}"),
                    $"day {day} names the plan day and the covered account; diag: {dayDiag}");
                Assert.IsTrue(dayDiag.Contains("producible: no (notions), retries tomorrow with the same batch id"),
                    $"day {day} names the retry; diag: {dayDiag}");
            }

            // Day 4: wire the service + registration; the day-3 order
            // (1-day transit) arrives day 4 with 1u sellable.
            Assert.IsNotNull(s.Notions.PlaceNotionsOrder(
                reg, 1, 3, NamedSkuPaymentMode.CashOnArrival, null, s.Diag),
                "the day-3 notions order should place; diag: " + string.Join("; ", s.Diag));
            List<NamedSkuLot> arrived = s.Notions.ReceiveArrivals(4, null, s.Diag);
            Assert.AreEqual(1, arrived.Count, "the 1u arrival lands day 4");
            Assert.AreEqual(1, s.Procurement.SellableUnits(BakeryFlourSupply.NotionsMaterialId),
                "1u sellable in the store lots on day 4");

            BakeryWholesaleDailyResult day4 = RunDay(s, reg, 4);
            Assert.AreEqual(0, day4.BatchesPlanned, "day 4 plans nothing: the existing batch covers");
            Assert.AreEqual(1, s.ProductionDriver.PlannedBatchIds.Count, "one active batch on day 4");
            Assert.AreEqual(batchId, s.ProductionDriver.PlannedBatchIds[0], "the SAME batch id bakes day 4");
            Assert.AreEqual(1, day4.NotionsUnitsFed, "the arrived notion feeds the bin");
            Assert.AreEqual(1, day4.BatchesBaked, "the same batch bakes day 4");
            Assert.AreEqual(1, day4.TransfersCompleted, "the transfer completes day 4");
            Assert.AreEqual(BakeryBatchStage.Baked, s.Bakery.Batches[0].Stage, "the batch is Baked on day 4");
            Assert.AreEqual(1, s.Bakery.WholesaleDeliveries.Count, "exactly one delivery record");
            Assert.AreEqual(3, account.NextDeliveryDayIndex, "account advanced once (2 -> 3)");
            Assert.AreEqual(1, s.Bakery.Batches.Count, "no second batch id ever appears");
        }

        [Test]
        public void BlockedBatch_ThreeDays_LogsBlockedLineDaily_NoDuplicate()
        {
            // Plan 04 Slice 4: a notions-blocked batch logs the
            // blocked-batch line once per day with the day count advancing,
            // the same batch id, the notions cause and the retry — and no
            // second batch is ever planned. Days 2-4 run unwired; day 5
            // wires the service + registration and the SAME batch bakes.
            Slice s = NewSlice();
            Series2CounterpartyRegistration reg = Registration(
                "test-notions", BakeryFlourSupply.NotionsMaterialId, 10, 0, 50, 1);
            Assert.IsNull(reg.RegisterInto(s.Market, s.Diag), "test counterparty registration should succeed");
            SeedInputs(s, 12, 0, 40);
            BakeryWholesaleAccount account = BookAccount(s, 24, 1, 2, 2);

            string batchId = null;
            for (int day = 2; day <= 4; day++)
            {
                int diagBefore = s.Diag.Count;
                BakeryWholesaleDailyResult r = RunDay(s, null, day, wireNotions: false);
                if (day == 2)
                {
                    Assert.AreEqual(1, r.BatchesPlanned, "day 2 plans the batch");
                    batchId = s.Bakery.Batches[0].BatchId;
                }
                else
                {
                    Assert.AreEqual(0, r.BatchesPlanned, $"day {day} plans nothing: already covered");
                }
                Assert.AreEqual(1, s.ProductionDriver.PlannedBatchIds.Count, $"one active batch, day {day}");
                Assert.AreEqual(batchId, s.ProductionDriver.PlannedBatchIds[0], $"same batch id, day {day}");
                Assert.AreEqual(0, r.BatchesBaked, $"nothing bakes without notions, day {day}");
                Assert.IsTrue(r.Refusal != null && r.Refusal.Contains("notions"),
                    $"day {day} refuses on notions; refusal: {r.Refusal}");
                Assert.AreEqual(BakeryBatchStage.Planned, s.Bakery.Batches[0].Stage, $"the batch stays Planned, day {day}");
                Assert.AreEqual(1, s.Bakery.Batches.Count, $"no second batch id ever appears, day {day}");
                string dayDiag = string.Join(" | ", s.Diag.GetRange(diagBefore, s.Diag.Count - diagBefore));
                Assert.IsTrue(dayDiag.Contains($"[BakeryProduction] batch {batchId} blocked day {day} (notions)"),
                    $"day {day} logs the blocked line with the advancing day count; diag: {dayDiag}");
                Assert.IsTrue(dayDiag.Contains($"planned 2, covered 24u for account {account.AccountId}"),
                    $"day {day} names the plan day and the covered account; diag: {dayDiag}");
                Assert.IsTrue(dayDiag.Contains("retries tomorrow with the same batch id"),
                    $"day {day} names the retry; diag: {dayDiag}");
            }

            // Day 5: wire the service + registration; the day-4 order
            // (1-day transit) arrives day 5 with 1u sellable.
            Assert.IsNotNull(s.Notions.PlaceNotionsOrder(
                reg, 1, 4, NamedSkuPaymentMode.CashOnArrival, null, s.Diag),
                "the day-4 notions order should place; diag: " + string.Join("; ", s.Diag));
            List<NamedSkuLot> arrived = s.Notions.ReceiveArrivals(5, null, s.Diag);
            Assert.AreEqual(1, arrived.Count, "the 1u arrival lands day 5");
            Assert.AreEqual(1, s.Procurement.SellableUnits(BakeryFlourSupply.NotionsMaterialId),
                "1u sellable in the store lots on day 5");

            BakeryWholesaleDailyResult day5 = RunDay(s, reg, 5);
            Assert.AreEqual(0, day5.BatchesPlanned, "day 5 plans nothing: the existing batch covers");
            Assert.AreEqual(1, s.ProductionDriver.PlannedBatchIds.Count, "one active batch on day 5");
            Assert.AreEqual(batchId, s.ProductionDriver.PlannedBatchIds[0], "the SAME batch id bakes day 5");
            Assert.AreEqual(1, day5.NotionsUnitsFed, "the arrived notion feeds the bin");
            Assert.AreEqual(1, day5.BatchesBaked, "the same batch bakes day 5");
            Assert.AreEqual(1, day5.TransfersCompleted, "the transfer completes day 5");
            Assert.AreEqual(BakeryBatchStage.Baked, s.Bakery.Batches[0].Stage, "the batch is Baked on day 5");
            Assert.AreEqual(1, s.Bakery.WholesaleDeliveries.Count, "exactly one delivery record");
            Assert.AreEqual(1, s.Bakery.Batches.Count, "no second batch id ever appears");
        }

        [Test]
        public void Margin_FiveBakeDays_StandardCostVsActualCash()
        {
            // Reconciliation probe: five bake days buy 2, 1, 1, 1, 1 sacks
            // (60 lb, 174c at 29c) while the economics line reports the
            // 58c standard cost every day. Notions ride 5u of sellable
            // store lots (test-seeded pipeline stock, 50c at receipt); the
            // store is restocked by the test only between days 4 and 5.
            // Plan 04 Slice 4: the cumulative reconciliation line is now
            // asserted PRESENT, threaded across the five days like the
            // forwarder threads it — day 6 (5th bake) reads flour 174c
            // (6 sacks, 60 lb), notions 50c, revenue 240c, actual
            // 44.8c/batch (margin +3.2c/batch actual vs -20c standard).
            Slice s = NewSlice();
            Series2CounterpartyRegistration reg = Registration(
                "test-notions", BakeryFlourSupply.NotionsMaterialId, 10, 0, 50, 1);
            Assert.IsNull(reg.RegisterInto(s.Market, s.Diag), "test counterparty registration should succeed");
            OrderAndReceive(s, reg, 5, 0);
            Assert.AreEqual(4950, s.StoreRuntime.CurrentCashCents, "store pays 5u x 10c on arrival");
            Assert.AreEqual(5, s.Procurement.SellableUnits(BakeryFlourSupply.NotionsMaterialId),
                "5u sellable in the store lots from day 1");

            SeedInputs(s, 0, 0, 40);
            SeedStoreFlour(s, 4);
            BookAccount(s, 24, 1, 2, 2);

            int[] expectedSacks = { 2, 1, 1, 1, 1 };
            int totalFlourCash = 0;
            BakeryWholesaleDailyResult prior = default(BakeryWholesaleDailyResult);
            for (int i = 0; i < 5; i++)
            {
                int day = 2 + i;
                if (day == 5) RestockStoreFlour(s, 2);
                int diagBefore = s.Diag.Count;
                BakeryWholesaleDailyResult r = RunDay(s, reg, day, threadCumulative: true, prior: prior);
                prior = r;
                Assert.AreEqual(1, r.BatchesBaked, $"bake day {day}");
                Assert.AreEqual(1, r.TransfersCompleted, $"transfer day {day}");
                Assert.AreEqual(expectedSacks[i], r.FlourSacksPurchased, $"purchase sequence day {day}");
                Assert.AreEqual(1, r.NotionsUnitsFed, $"1 notion fed day {day}");
                totalFlourCash += r.FlourSacksPurchased * 29;
                string dayDiag = string.Join(" | ", s.Diag.GetRange(diagBefore, s.Diag.Count - diagBefore));
                Assert.IsTrue(dayDiag.Contains("flour 58c + notions 10c = 68c/batch"),
                    $"day {day} states the standard cost; diag: {dayDiag}");
                Assert.IsTrue(dayDiag.Contains("margin -20c/batch"),
                    $"day {day} states the standard margin; diag: {dayDiag}");
                Assert.IsTrue(dayDiag.Contains($"actual flour purchase today: {expectedSacks[i]} sack(s)"),
                    $"day {day} names the actual purchase; diag: {dayDiag}");
                Assert.IsTrue(dayDiag.Contains("cumulative actuals:"),
                    $"day {day} logs the cumulative reconciliation line; diag: {dayDiag}");
                if (day == 5)
                {
                    Assert.IsTrue(dayDiag.Contains("cumulative actuals: 4 batch(es) baked, flour 145c (5 sacks, 50 lb), notions 40c, revenue 192c"),
                        $"day 5 cumulative after 4 bakes; diag: {dayDiag}");
                }
                if (day == 6)
                {
                    Assert.IsTrue(dayDiag.Contains("cumulative actuals: 5 batch(es) baked, flour 174c (6 sacks, 60 lb), notions 50c, revenue 240c, actual 44.8c/batch vs standard 68c/batch (margin 3.2c/batch actual vs -20c standard)"),
                        $"day 6 cumulative after 5 bakes; diag: {dayDiag}");
                    Assert.AreEqual(5, r.CumulativeBatchesBaked, "cumulative batches baked");
                    Assert.AreEqual(174, r.CumulativeFlourCashSpentCents, "cumulative flour cash");
                    Assert.AreEqual(6, r.CumulativeFlourSacksPurchased, "cumulative flour sacks");
                    Assert.AreEqual(60, r.CumulativeFlourPoundsPurchased, "cumulative flour pounds");
                    Assert.AreEqual(50, r.CumulativeNotionsCashSpentCents, "cumulative notions cash");
                    Assert.AreEqual(240, r.CumulativeWholesaleRevenueCents, "cumulative wholesale revenue");
                }
            }

            Assert.AreEqual(0, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId),
                "bin flour 0 lb after day 5");
            Assert.AreEqual(174, totalFlourCash, "60 lb for 174c total flour cash");
            Assert.AreEqual(2016, s.BakeryRuntime.CurrentCashCents,
                "bakery 2,000 - 174c flour - 50c notions + 240c wholesale");
            Assert.AreEqual(4934, s.StoreRuntime.CurrentCashCents,
                "store 5,000 - 50c notions arrival + 174c flour - 240c wholesale + 50c notions feeds");
            // Actual per-batch: flour 174/5 = 34.8c + notions 10c = 44.8c
            // vs 48c revenue = +3.2c/batch actual (vs -20c standard).
            Assert.IsTrue(Math.Abs(44.8 - (174.0 / 5.0 + 10.0)) < 0.001, "actual cost 44.8c/batch");
            Assert.IsTrue(Math.Abs(3.2 - (48.0 - (174.0 / 5.0 + 10.0))) < 0.001, "actual margin +3.2c/batch");
            Assert.IsTrue(string.Join(" | ", s.Diag).Contains("cumulative actuals: 5 batch(es) baked, flour 174c (6 sacks, 60 lb), notions 50c, revenue 240c, actual 44.8c/batch"),
                "the cumulative reconciliation line is present across the run");
        }

        [Test]
        public void Fuel_FortyUnits_BakesFortyDays_ThenRefusesWithD2D7Named()
        {
            // 40u of fuel bakes 40 days, then the bake refuses on fuel.
            // Flour + notions are test-seeded per day (labelled
            // test-seeded inputs), so only fuel can block. The D-2/D-7
            // diagnostic is asserted on the BuildFuelLine output for the
            // exhausted day (the forwarder builds that line; the driver
            // itself only refuses on fuel).
            Slice s = NewSlice();
            SeedInputs(s, 0, 0, 40);
            BookAccount(s, 24, 1, 2, 2);

            for (int day = 2; day <= 41; day++)
            {
                SeedInputs(s, 12, 1, 0);
                BakeryWholesaleDailyResult r = RunDay(s, null, day, wireNotions: false);
                Assert.AreEqual(1, r.BatchesBaked, $"bake day {day}");
                Assert.AreEqual(1, r.TransfersCompleted, $"transfer day {day}");
                Assert.AreEqual(40 - (day - 1),
                    s.Bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId),
                    $"fuel decrements exactly, day {day} (no unit appears from any other source)");
            }

            SeedInputs(s, 12, 1, 0);
            BakeryWholesaleDailyResult refused = RunDay(s, null, 42, wireNotions: false);
            Assert.AreEqual(0, refused.BatchesBaked, "day 41 bakes nothing: fuel exhausted");
            Assert.IsTrue(refused.Refusal != null && refused.Refusal.Contains("fuel"),
                "day 41 refuses on fuel; refusal: " + refused.Refusal);
            Assert.AreEqual(0, s.Bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId),
                "fuel on hand is 0 on day 41");
            Assert.AreEqual(BakeryBatchStage.Planned,
                s.Bakery.Batches[s.Bakery.Batches.Count - 1].Stage,
                "the blocked batch stays Planned");

            string fuelLine = BakeryFuelBlockerDiagnostics.BuildFuelLine(new BakeryFuelBlockerInputs
            {
                BatchesPlanned = 1,
                FuelUnitsOnHand = 0,
                AuthoredFactor = null,
                YardIntakeExistsInDailyFlow = false,
                DealerSourceAttached = false,
                DealerFuelUnitsOnHand = 0,
            });
            Assert.IsTrue(fuelLine.Contains("D-2") && fuelLine.Contains("D-7"),
                "the day-41 fuel line names D-2 and D-7; line: " + fuelLine);
            Assert.IsTrue(fuelLine.Contains("no dealer source attached"),
                "the line states no dealer source; line: " + fuelLine);
            Assert.IsTrue(fuelLine.Contains("no live fuel-yard intake"),
                "the line states no yard intake; line: " + fuelLine);
        }

        // ---- Slice 2 probe (no production repair owed) ----------------
        // Slice 0 Tests 2 and 3 failed as specified (14 orders over days
        // 1-14 for one blocked batch; 5 orders over flour-blocked days
        // 1-5), and the plan's Slice 2 named Branches A (subtract
        // in-transit units for an unchanged batch set) and B (skip the
        // notions order leg while flour-blocked). The coder verified
        // both branches against source + Set B evidence BEFORE editing
        // and found NEITHER is owed — the "over-ordering" is the pipeline
        // priming, not a defect:
        //
        // 1. The code documents the design explicitly
        //    (BakeryNotionsDailySupply.cs): "In-transit units are
        //    earmarked for the future days whose shortfalls ordered them;
        //    subtracting them would order one notion per transit window
        //    instead of one per bake (a bake every 14 days, not a daily
        //    bake)." Branch A is exactly the subtraction the comment
        //    warns against, and it produces exactly the predicted
        //    outcome: from a cold start, 1 order (day 1) -> bake day 15
        //    -> 1 order (day 16) -> bake day 30 -> ... a 15-day cycle,
        //    never daily baking.
        // 2. Set B proves the priming orders are load-bearing: bakes on
        //    days 15-18 (4 consecutive days) required arrivals on days
        //    15, 16, 17, 18, i.e. orders placed on days 1, 2, 3, 4.
        //    Under Branch A only the day-1 order exists, so days 16-18
        //    would not bake. The per-day shortfall is the account's
        //    DAILY demand rate (1 batch/day), not the single active
        //    batch's need: each day's order serves a distinct future
        //    bake. The "same batch, same need" framing confuses the
        //    stock (one active batch) with the flow (one batch/day).
        // 3. Branch B fails the same way: Set B's first bake (day 15)
        //    required a notions order placed on day 1, days BEFORE flour
        //    landed (~day 7 via freight). Suppressing notions orders
        //    until flour is on hand delays the first bake to
        //    (flour day + 14). Ordering notions while flour-blocked
        //    parallelises the two pipelines; it is not waste.
        // 4. The committed units are not wasted: they arrive as sellable
        //    store lots and feed the days 15-28 bakes below. The 30u
        //    finite-supply pressure is a supply-term issue (a standing
        //    wholesale term is already owed, D-1-adjacent), not an
        //    ordering defect.
        // 5. Tests 2 and 3 therefore stay failing as PLAN-CONFLICT
        //    known failures, not defects: Test 2 expects 1 order where
        //    priming needs 14, and Test 3 expects 0 orders where priming
        //    needs 1/day. No implementation can satisfy both (the first
        //    5 days are identical in both tests; 0 orders in 5 days and
        //    1 order in 14 days would need an order to appear on a later
        //    flour-blocked day for no reason). The coordinator/planner
        //    must decide: revise Tests 2/3 to the not-a-defect reading,
        //    or direct a different repair.
        //    DECIDED (coordinator, ~20:40 EST 2026-11-07) and DONE
        //    (Plan 04 follow-up, tests only): Tests 2 and 3 were
        //    rewritten to the not-a-defect priming reading above
        //    (NotionsPipeline_Priming_FourteenOrders_ForFourteenFutureBakes
        //    and NotionsPipeline_FlourBlockedDays_PrimingOrders_EnableEarlyFirstBake).
        //    The plan-conflict failure set is closed by that revision;
        //    the drain cycle in point 6 stays an open follow-up
        //    (pipeline-depth / reorder-point policy decision).
        // 6. The GENUINE pipeline finding, pinned below and recorded as
        //    follow-up (not repaired here — it needs a pipeline-depth /
        //    reorder-point policy decision, which neither Slice 2 branch
        //    specifies): the pipeline DRAINS after the primed window.
        //    On arrival days the feed covers the need, so no replacement
        //    order is placed; after the last primed arrival (day 28)
        //    there is nothing in flight, so baking stalls days 29-42
        //    while orders resume 1/day, then bakes days 43-56 — a
        //    14-on / 14-off cycle. The plan's Slice 2 regression test
        //    ("5 days baking 1 AND ordering 1/day") is unsatisfiable as
        //    specified: on any day the bake succeeds the post-feed
        //    shortfall is 0, so the order is 0 by construction.
        //    The test below pins the priming value (14 consecutive daily
        //    bakes) and the drain cycle instead.
        [Test]
        public void NotionsPipeline_PrimedWindowBakesThenDrains()
        {
            Slice s = NewSlice();
            Series2CounterpartyRegistration reg = ProductionRegistration();
            Assert.IsNull(reg.RegisterInto(s.Market, s.Diag), "production counterparty registration should succeed");
            SeedInputs(s, 0, 0, 40);
            BookAccount(s, 24, 1, 2, 2);

            for (int day = 1; day <= 14; day++)
            {
                SeedInputs(s, 12, 0, 0);
                BakeryWholesaleDailyResult r = RunDay(s, reg, day);
                Assert.AreEqual(0, r.BatchesBaked, $"nothing bakes before the first arrival, day {day}");
            }
            Assert.AreEqual(14, s.StoreService.Orders.Count,
                "14 priming orders (1/day) over days 1-14 — one per future daily bake, not one per active batch");

            for (int day = 15; day <= 28; day++)
            {
                SeedInputs(s, 12, 0, 0);
                BakeryWholesaleDailyResult r = RunDay(s, reg, day);
                Assert.AreEqual(1, r.BatchesBaked, $"the primed pipeline bakes daily, day {day}");
                Assert.AreEqual(1, r.TransfersCompleted, $"transfer completes, day {day}");
                Assert.AreEqual(0, r.NotionsOrdered, $"no replacement order on an arrival day (feed covers), day {day}");
                Assert.AreEqual(1, r.NotionsUnitsFed, $"1u fed from the day's arrival, day {day}");
            }
            Assert.AreEqual(14, s.Bakery.WholesaleDeliveries.Count, "14 consecutive daily bakes = 14 deliveries");
            Assert.AreEqual(14, s.StoreService.Lots.Count, "14 arrival lots, one per priming order");
            Assert.AreEqual(2532, s.BakeryRuntime.CurrentCashCents,
                "bakery 2,000 - 140c notions feeds + 672c wholesale (14 x 48c)");
            Assert.AreEqual(4328, s.StoreRuntime.CurrentCashCents,
                "store 5,000 - 140c receipts + 140c feeds - 672c wholesale");

            // RECORDED FINDING (follow-up, not repaired here): the
            // pipeline drains. No order was placed on any bake day, so
            // after the last primed arrival (day 28) nothing is in
            // flight: baking stalls while orders resume 1/day.
            for (int day = 29; day <= 42; day++)
            {
                SeedInputs(s, 12, 0, 0);
                BakeryWholesaleDailyResult r = RunDay(s, reg, day);
                Assert.AreEqual(0, r.BatchesBaked, $"no bake once the primed window drains, day {day}");
                Assert.AreEqual(1, r.NotionsOrdered, $"orders resume 1/day on stall days, day {day}");
            }
            Assert.AreEqual(28, s.StoreService.Orders.Count, "14 priming + 14 stall-day orders");
            Assert.AreEqual(14, s.StoreService.Lots.Count, "no new arrivals yet (stall-day orders arrive days 43-56)");
        }

        [Test]
        public void SaveLoad_MidPipeline_OrderAndBatchRestoreExactlyOnce()
        {
            // After Test 1's day-1 order (1 order in flight, arrival day
            // 15, batch Planned): capture the procurement ExportState +
            // driver planned state + bakery save DTO, restore into fresh
            // objects with the SAME market, and prove the order, the batch,
            // and the day-15 arrival all restore exactly once.
            Slice s = NewSlice();
            Series2CounterpartyRegistration reg = ProductionRegistration();
            Assert.IsNull(reg.RegisterInto(s.Market, s.Diag), "production counterparty registration should succeed");
            SeedInputs(s, 12, 0, 40);
            BookAccount(s, 24, 1, 2, 2);

            BakeryWholesaleDailyResult day1 = RunDay(s, reg, 1);
            Assert.AreEqual(1, day1.BatchesPlanned, "day 1 plans the batch");
            Assert.AreEqual(1, s.StoreService.Orders.Count, "one order in flight after day 1");
            string orderId = s.StoreService.Orders[0].OrderId;
            string batchId = s.Bakery.Batches[0].BatchId;
            Assert.AreEqual(BakeryBatchStage.Planned, s.Bakery.Batches[0].Stage, "the batch is Planned at capture");

            NamedSkuProcurementSaveDto procurementDto = s.StoreService.ExportState();
            BakeryShopRuntime.BakeryShopRuntimeSaveDto bakeryDto = s.Bakery.CaptureSaveDto();
            var plannedIds = new List<string>(s.ProductionDriver.PlannedBatchIds);
            int plannedDay = s.ProductionDriver.PlannedDayIndex;

            var f = new Slice();
            f.Diag = s.Diag;
            f.Market = s.Market;
            f.Ids = s.Ids;
            f.StoreRuntime.BindBusinessIdentity(StoreInstanceId);
            f.StoreRuntime.AddCashCents(5000);
            f.StoreRuntime.EnsureCategoryStock("staple_food", 0, 1000);
            f.BakeryRuntime.BindBusinessIdentity(BakeryInstanceId);
            f.BakeryRuntime.AddCashCents(2000);
            f.StoreService = new OffMapNamedSkuProcurementService(
                StoreInstanceId, f.Market, new RuntimeCashPort(f.StoreRuntime));
            f.StoreService.ImportState(procurementDto);
            f.Procurement = new Series2FlourProcurementDriver(f.StoreService);
            f.Notions = new Series2NotionsSupply(f.Procurement);
            f.Bakery = new BakeryShopRuntime(BakeryInstanceId);
            f.Bakery.LoadFromSaveDto(bakeryDto);
            f.ProductionDriver = new Series2BakeryProductionDriver(
                f.Bakery, new Series2InStockBakeryFuelSource(f.Bakery));
            f.ProductionDriver.RestorePlannedState(plannedDay, plannedIds, f.Diag);
            f.StorePort = new RuntimeStorePort { Runtime = f.StoreRuntime };
            f.StorePort.Prices["staple_food"] = 5;
            f.StorePort.OfferList.Add(new GeneralStoreCategoryOffer
            {
                CategoryId = "staple_food",
                OfferedItemIds = new List<string> { "bread" },
                StoreUnitsPerItemUnit = 1,
            });

            Assert.AreEqual(1, f.StoreService.Orders.Count, "exactly 1 order restored");
            Assert.AreEqual(orderId, f.StoreService.Orders[0].OrderId, "same order id restored");
            Assert.AreEqual(15, f.StoreService.Orders[0].ExpectedArrivalDayIndex, "same arrival day restored");
            Assert.AreEqual(1, f.Bakery.Batches.Count, "the same batch restored");
            Assert.AreEqual(batchId, f.Bakery.Batches[0].BatchId, "same batch id restored");
            Assert.AreEqual(BakeryBatchStage.Planned, f.Bakery.Batches[0].Stage, "the batch restores as Planned");
            Assert.AreEqual(1, f.ProductionDriver.PlannedBatchIds.Count, "one planned batch id restored");
            Assert.AreEqual(batchId, f.ProductionDriver.PlannedBatchIds[0], "the restored planned id is the batch id");

            BakeryWholesaleDailyResult repeat = RunDay(f, reg, 1);
            Assert.AreEqual(0, repeat.NotionsOrdered, "no second order placed by a repeat RunDay on the restored state");
            Assert.AreEqual(1, f.StoreService.Orders.Count, "still exactly one order after the repeat run");
            Assert.AreEqual(0, repeat.BatchesBaked, "nothing bakes before the arrival on the restored state");

            BakeryWholesaleDailyResult day15 = RunDay(f, reg, 15);
            Assert.AreEqual(1, f.StoreService.Lots.Count, "the day-15 arrival still creates exactly one lot");
            Assert.AreEqual(1, day15.NotionsUnitsFed, "exactly one feed on the restored state");
            Assert.AreEqual(1, day15.BatchesBaked, "the restored batch bakes on arrival day");
            Assert.AreEqual(batchId, f.Bakery.Batches[0].BatchId, "the same restored batch id bakes");
            Assert.AreEqual(1, day15.TransfersCompleted, "the transfer completes on the restored state");
            Assert.AreEqual(24, day15.UnitsStocked, "24 loaves stocked");
            Assert.AreEqual(4952, f.StoreRuntime.CurrentCashCents, "restored store 5,000 - 10c receipt + 10c feed - 48c wholesale");
            Assert.AreEqual(2038, f.BakeryRuntime.CurrentCashCents, "restored bakery 2,000 - 10c feed + 48c wholesale");
        }

        [Test]
        public void RefusedFeed_BakeryCashShort_RestoresStoreLotsAndCash()
        {
            // Plan 04 Slice 6 Test 9: the daily-driver seam pin for the
            // FeedToBin rollback. The store holds 1 notions lot (1u,
            // arrived, store paid 10c at receipt); the bakery holds 5c,
            // short of the 10c feed price. The feed refuses, the store lot
            // is restored to sellable via UndoConsumption, no cash moves,
            // the bin gains 0 notions and the batch stays Planned. Funding
            // the bakery to 15c then lets the SAME feed succeed exactly
            // once. The registration carries a 1u finite supply, so the
            // refused day's replacement order cannot create a second
            // arrival lot before the re-run (the day-2 order leg refuses
            // on exhausted supply, logged, no crash).
            Slice s = NewSlice(5000, 5);
            Series2CounterpartyRegistration reg = Registration(
                "test-notions", BakeryFlourSupply.NotionsMaterialId, 10, 0, 1, 1);
            Assert.IsNull(reg.RegisterInto(s.Market, s.Diag), "test counterparty registration should succeed");
            SeedInputs(s, 12, 0, 40);
            BookAccount(s, 24, 1, 2, 2);

            // Day-1 order (1u, 1-day transit) arrives day 2: store pays 10c.
            Assert.IsNotNull(s.Notions.PlaceNotionsOrder(
                reg, 1, 1, NamedSkuPaymentMode.CashOnArrival, null, s.Diag),
                "the day-1 notions order should place; diag: " + string.Join("; ", s.Diag));
            List<NamedSkuLot> arrived = s.Notions.ReceiveArrivals(2, null, s.Diag);
            Assert.AreEqual(1, arrived.Count, "the 1u arrival lands day 2");
            Assert.AreEqual(1, s.Procurement.SellableUnits(BakeryFlourSupply.NotionsMaterialId),
                "1u sellable in the store lots on day 2");
            Assert.AreEqual(4990, s.StoreRuntime.CurrentCashCents, "store 5,000 - 10c receipt");
            Assert.AreEqual(5, s.BakeryRuntime.CurrentCashCents, "bakery starts at 5c");

            string batchId;
            {
                int diagBefore = s.Diag.Count;
                BakeryWholesaleDailyResult day2 = RunDay(s, reg, 2);
                Assert.AreEqual(1, day2.BatchesPlanned, "day 2 plans the batch");
                batchId = s.Bakery.Batches[0].BatchId;
                Assert.AreEqual(0, day2.NotionsUnitsFed, "the feed refuses: bakery cannot pay 10c with 5c");
                Assert.AreEqual(0, day2.BatchesBaked, "nothing bakes without the fed notion");
                Assert.IsTrue(day2.Refusal != null && day2.Refusal.Contains("notions"),
                    "the bake refuses on notions day 2; refusal: " + day2.Refusal);
                string dayDiag = string.Join(" | ", s.Diag.GetRange(diagBefore, s.Diag.Count - diagBefore));
                Assert.IsTrue(dayDiag.Contains("bakery cash cannot pay the feed price; consumption undone, nothing moved"),
                    "the feed refusal names the cash shortfall and the rollback; diag: " + dayDiag);
                Assert.AreEqual(1, s.Procurement.SellableUnits(BakeryFlourSupply.NotionsMaterialId),
                    "the store lot is restored to sellable 1u via UndoConsumption");
                Assert.AreEqual(5, s.BakeryRuntime.CurrentCashCents, "bakery cash stays 5c");
                Assert.AreEqual(4990, s.StoreRuntime.CurrentCashCents, "store cash unchanged by the feed attempt");
                Assert.AreEqual(0, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.NotionsItemId),
                    "the bin gains 0 notions");
                Assert.AreEqual(BakeryBatchStage.Planned, s.Bakery.Batches[0].Stage, "the batch stays Planned");
                Assert.AreEqual(0, day2.NotionsOrdered, "no replacement order: the 1u finite supply is exhausted");
                Assert.AreEqual(1, s.StoreService.Orders.Count, "still exactly one order (the received day-1 order)");
            }

            // Fund the bakery to 15c; the SAME feed succeeds exactly once.
            s.BakeryRuntime.AddCashCents(10);
            Assert.AreEqual(15, s.BakeryRuntime.CurrentCashCents, "bakery funded to 15c");
            BakeryWholesaleDailyResult day3 = RunDay(s, reg, 3);
            Assert.AreEqual(0, day3.BatchesPlanned, "day 3 plans nothing: the existing batch covers");
            Assert.AreEqual(batchId, s.ProductionDriver.PlannedBatchIds[0], "the SAME batch id bakes day 3");
            Assert.AreEqual(1, day3.NotionsUnitsFed, "the feed succeeds exactly once");
            Assert.AreEqual(1, day3.BatchesBaked, "the same batch bakes day 3");
            Assert.AreEqual(1, day3.TransfersCompleted, "the transfer completes day 3");
            Assert.AreEqual(0, s.Procurement.SellableUnits(BakeryFlourSupply.NotionsMaterialId),
                "store lot 0u sellable after the one feed");
            Assert.AreEqual(0, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.NotionsItemId),
                "bin notions 0u after the bake (fed 1u, baked 1u)");
            Assert.AreEqual(53, s.BakeryRuntime.CurrentCashCents, "bakery 15c funded - 10c feed + 48c wholesale");
            Assert.AreEqual(4952, s.StoreRuntime.CurrentCashCents, "store 4,990c + 10c feed - 48c wholesale");
            Assert.AreEqual(1, s.Bakery.WholesaleDeliveries.Count, "exactly one delivery record");
        }

        [Test]
        public void SaveLoad_MidFlourPipeline_ReorderLotSurvives_NoDuplicateOrder()
        {
            // Plan 04 Slice 6 Test 10: after Test 4's day-1 reorder (2-sack
            // lot placed, 96c debited), capture the Millbrook supplier DTO
            // + bakery save DTO, restore into fresh objects, and prove the
            // lot restores once, a repeat PlaceReorder refuses as
            // already-covered, and the restored lot still ships and delivers
            // exactly once. Uses the existing OffMapAidBusinessRuntime DTO
            // paths; Plan 03 Slice 4 is the save/load precedent (and the
            // Millbrook SaveRoundTrip test pins double-import idempotency).
            Slice s = NewSlice();
            var supplier = new OffMapAidBusinessRuntime(MillbrookDefinition());
            var adapter = new MillbrookFlourFreightCustodyAdapter(supplier, s.Bakery, s.Ids);
            var bakeryCash = new Series2BusinessRuntimeCashPort(s.BakeryRuntime);

            Assert.IsNull(supplier.TryPlaceOrder(
                "opening-flour-b1", BakeryInstanceId, "Glen Pierce", 1, bakeryCash, 1, out OffMapAidCargoLot openingLot),
                "the opening 1-sack Millbrook order should place");
            SeedInputs(s, 0, 1, 40);
            BookAccount(s, 24, 1, 2, 1);

            BakeryWholesaleDailyResult day1 = RunDay(s, null, 1, wireNotions: false, millbrookSupplier: supplier);
            Assert.AreEqual(1, day1.BatchesPlanned, "day 1 plans the batch");
            Assert.IsTrue(!string.IsNullOrWhiteSpace(day1.MillbrookReorderLotId),
                "a 2-sack Millbrook reorder is placed");
            Assert.IsTrue(day1.MillbrookReorderLotId.Contains("flour-reorder-d1-n2"),
                "reorder lot id names the reorder; lot id: " + day1.MillbrookReorderLotId);
            Assert.AreEqual(2, day1.MillbrookReorderSacks, "the reorder covers 2 sacks / 20 lb");
            Assert.AreEqual(0, day1.BatchesBaked, "nothing bakes day 1: no flour on hand");
            Assert.AreEqual(1856, s.BakeryRuntime.CurrentCashCents, "bakery 2,000 - 48c opening - 96c reorder");
            Assert.AreEqual(144, supplier.CashCents, "Millbrook +48c opening + 96c reorder");
            Assert.AreEqual(2, supplier.Lots.Count, "exactly two Millbrook lots");
            string batchId = s.Bakery.Batches[0].BatchId;

            OffMapAidBusinessSaveDto supplierDto = supplier.ExportState();
            BakeryShopRuntime.BakeryShopRuntimeSaveDto bakeryDto = s.Bakery.CaptureSaveDto();
            var plannedIds = new List<string>(s.ProductionDriver.PlannedBatchIds);
            int plannedDay = s.ProductionDriver.PlannedDayIndex;

            var f = new Slice();
            f.Diag = s.Diag;
            f.Market = s.Market;
            f.Ids = s.Ids;
            f.StoreRuntime.BindBusinessIdentity(StoreInstanceId);
            f.StoreRuntime.AddCashCents(5000);
            f.StoreRuntime.EnsureCategoryStock("staple_food", 0, 1000);
            f.BakeryRuntime.BindBusinessIdentity(BakeryInstanceId);
            f.BakeryRuntime.AddCashCents(1856);
            f.StoreService = new OffMapNamedSkuProcurementService(
                StoreInstanceId, f.Market, new RuntimeCashPort(f.StoreRuntime));
            f.Procurement = new Series2FlourProcurementDriver(f.StoreService);
            f.Notions = new Series2NotionsSupply(f.Procurement);
            f.Bakery = new BakeryShopRuntime(BakeryInstanceId);
            f.Bakery.LoadFromSaveDto(bakeryDto);
            f.ProductionDriver = new Series2BakeryProductionDriver(
                f.Bakery, new Series2InStockBakeryFuelSource(f.Bakery));
            f.ProductionDriver.RestorePlannedState(plannedDay, plannedIds, f.Diag);
            f.StorePort = new RuntimeStorePort { Runtime = f.StoreRuntime };
            f.StorePort.Prices["staple_food"] = 5;
            f.StorePort.OfferList.Add(new GeneralStoreCategoryOffer
            {
                CategoryId = "staple_food",
                OfferedItemIds = new List<string> { "bread" },
                StoreUnitsPerItemUnit = 1,
            });
            var restoredSupplier = new OffMapAidBusinessRuntime(MillbrookDefinition());
            Assert.IsNull(restoredSupplier.ImportState(supplierDto), "import of the exported supplier state should succeed");
            var restoredAdapter = new MillbrookFlourFreightCustodyAdapter(restoredSupplier, f.Bakery, f.Ids);

            Assert.AreEqual(2, restoredSupplier.Lots.Count, "exactly two Millbrook lots restored");
            OffMapAidCargoLot restoredOpening = null;
            OffMapAidCargoLot restoredReorder = null;
            foreach (OffMapAidCargoLot candidate in restoredSupplier.Lots)
            {
                if (candidate == null) continue;
                if (string.Equals(candidate.LotId, openingLot.LotId, StringComparison.Ordinal)) restoredOpening = candidate;
                if (string.Equals(candidate.LotId, day1.MillbrookReorderLotId, StringComparison.Ordinal)) restoredReorder = candidate;
            }
            Assert.IsNotNull(restoredOpening, "the opening lot restores with the same id");
            Assert.IsNotNull(restoredReorder, "the reorder lot restores once with the same id");
            Assert.AreEqual(2, restoredReorder.PackageUnits, "the restored reorder lot is 2 sacks");
            Assert.AreEqual(20, restoredReorder.InventoryUnits, "the restored reorder lot is 20 lb");
            Assert.AreEqual(144, restoredSupplier.CashCents, "restored Millbrook cash 144c");
            Assert.AreEqual(200, restoredSupplier.GetInventoryUnits("bakery_flour_sack"), "restored Millbrook stock 230 - 10 - 20 lb");
            Assert.AreEqual(1, f.Bakery.Batches.Count, "the same batch restored");
            Assert.AreEqual(batchId, f.Bakery.Batches[0].BatchId, "same batch id restored");
            Assert.AreEqual(BakeryBatchStage.Planned, f.Bakery.Batches[0].Stage, "the batch restores as Planned");

            // A repeat PlaceReorder for the same need refuses as
            // already-covered: no duplicate order, no second debit.
            string repeatRefusal = MillbrookFlourReorder.PlaceReorder(
                restoredSupplier, new Series2BusinessRuntimeCashPort(f.BakeryRuntime),
                BakeryInstanceId, "Glen Pierce", 2, 1, f.Diag, out OffMapAidCargoLot duplicateLot);
            Assert.IsTrue(!string.IsNullOrWhiteSpace(repeatRefusal),
                "a repeat reorder must refuse; diag: " + string.Join("; ", f.Diag));
            Assert.IsTrue(repeatRefusal.Contains("already covers"),
                "the refusal names the already-covered outstanding lot; refusal: " + repeatRefusal);
            Assert.IsNull(duplicateLot, "no duplicate lot is created");
            Assert.AreEqual(2, restoredSupplier.Lots.Count, "still exactly two Millbrook lots");
            Assert.AreEqual(1856, f.BakeryRuntime.CurrentCashCents, "no second debit from the refused reorder");
            Assert.AreEqual(144, restoredSupplier.CashCents, "Millbrook cash unchanged by the refused reorder");

            // The driver re-run on the restored state places no new reorder
            // either (same already-covered refusal inside the driver).
            BakeryWholesaleDailyResult repeat = RunDay(f, null, 1, wireNotions: false, millbrookSupplier: restoredSupplier);
            Assert.IsTrue(string.IsNullOrWhiteSpace(repeat.MillbrookReorderLotId),
                "no new reorder from the driver re-run; lot id: " + repeat.MillbrookReorderLotId);
            Assert.AreEqual(0, repeat.BatchesBaked, "nothing bakes before the lots land on the restored state");
            Assert.AreEqual(2, restoredSupplier.Lots.Count, "still exactly two Millbrook lots after the re-run");

            // The restored lots still ship and deliver exactly once: the
            // opening 1-sack lot first (list order), then the 2-sack
            // reorder lot; bin 30 lb, no duplicate delivery.
            Assert.AreEqual(openingLot.LotId,
                MillbrookFlourReorder.FindShippableLot(restoredSupplier, BakeryInstanceId)?.LotId,
                "the restored opening 1-sack lot ships first");
            ReceiveMillbrookLot(f, restoredAdapter, restoredOpening, 1);
            Assert.AreEqual(10, f.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId), "bin 10 lb after the restored 1-sack receipt");
            ReceiveMillbrookLot(f, restoredAdapter, restoredReorder, 1);
            Assert.AreEqual(30, f.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId), "bin 30 lb after both restored receipts (no duplicate delivery)");
            Assert.AreEqual(2, restoredSupplier.Lots.Count, "still exactly two Millbrook lots after both receipts");

            BakeryWholesaleDailyResult afterTwoSacks = RunDay(f, null, 1, wireNotions: false, millbrookSupplier: restoredSupplier);
            Assert.AreEqual(batchId, f.ProductionDriver.PlannedBatchIds[0], "the SAME restored batch id bakes");
            Assert.AreEqual(1, afterTwoSacks.BatchesBaked, "the restored batch bakes once both restored lots land");
            Assert.AreEqual(1, afterTwoSacks.TransfersCompleted, "the transfer completes on the restored state");
            Assert.AreEqual(24, afterTwoSacks.UnitsStocked, "24 loaves stocked");
            Assert.AreEqual(1904, f.BakeryRuntime.CurrentCashCents, "restored bakery 1,856c + 48c wholesale");
            Assert.AreEqual(144, restoredSupplier.CashCents, "Millbrook cash is unchanged by delivery (+144c total)");
            Assert.AreEqual(18, f.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId), "bin 30 - 12 lb after the bake");
            Assert.AreEqual(1, f.Bakery.WholesaleDeliveries.Count, "exactly one delivery record on the restored state");
        }

        // ---- Priority 3 Slice 1 (2026-11-08): pins the smoke-test
        // flour arithmetic and the 14-on / 14-off reproduction --------
        // Tests only; no production code. The existing tests seed flour
        // directly each day (SeedInputs(s, 12, 0, 0)), which bypasses
        // the freight lag and therefore cannot exhibit the stall. These
        // four tests add the lag: flour arrives through the Millbrook
        // receipt seam (ReceiveMillbrookLot) and notions through the
        // real 14-day pipeline. All four are expected to PASS — they pin
        // the mechanism, and a failure rewrites plan §2, so none is
        // worked around here.
        private static void AssertBlockedLineNamesCause(Slice s, int diagBefore, string batchId, int day, string cause)
        {
            string dayDiag = string.Join(" | ", s.Diag.GetRange(diagBefore, s.Diag.Count - diagBefore));
            Assert.IsTrue(dayDiag.Contains($"[BakeryProduction] batch {batchId} blocked day {day} ({cause})"),
                $"the blocked line names the {cause} cause; diag: {dayDiag}");
            Assert.IsTrue(dayDiag.Contains($"producible: no ({cause})"),
                $"the blocked line reads producible: no ({cause}); diag: {dayDiag}");
        }

        [Test]
        public void Flour_TwoSackDelivery_BakesOnce_LeavesEightLbResidue_BlocksNextBake()
        {
            // Priority 3 Slice 1 (H-A1): a 20-lb bin (one 2-sack
            // Millbrook lot received through the freight receipt seam)
            // bakes exactly once and leaves 8 lb of residue that can
            // never bake a 12-lb batch alone. The next day's driver
            // orders only today's 4-lb shortfall rounded up to 1 sack,
            // and the batch refuses on flour with the blocked line
            // reading covered 24u / producible: no (flour) — the exact
            // smoke-test figure (8 lb vs 12 lb).
            Slice s = NewSlice();
            var supplier = new OffMapAidBusinessRuntime(MillbrookDefinition());
            var adapter = new MillbrookFlourFreightCustodyAdapter(supplier, s.Bakery, s.Ids);
            var bakeryCash = new Series2BusinessRuntimeCashPort(s.BakeryRuntime);

            Assert.IsNull(supplier.TryPlaceOrder(
                "test-flour-2sack", BakeryInstanceId, "Glen Pierce", 2, bakeryCash, 1, out OffMapAidCargoLot twoSackLot),
                "the 2-sack Millbrook order should place");
            Assert.AreEqual(1904, s.BakeryRuntime.CurrentCashCents, "bakery 2,000 - 96c for 2 sacks at 48c");
            Assert.AreEqual(96, supplier.CashCents, "Millbrook +96c for the 2-sack order");
            Assert.AreEqual(210, supplier.GetInventoryUnits("bakery_flour_sack"), "Millbrook 230 - 20 lb");
            ReceiveMillbrookLot(s, adapter, twoSackLot, 1);
            Assert.AreEqual(20, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId),
                "bin 20 lb after the 2-sack receipt");

            SeedInputs(s, 0, 1, 40);
            BookAccount(s, 24, 1, 2, 1);

            BakeryWholesaleDailyResult day1 = RunDay(s, null, 1, wireNotions: false, millbrookSupplier: supplier);
            Assert.AreEqual(1, day1.BatchesBaked, "day 1 bakes the one batch the 20-lb bin supports");
            Assert.AreEqual(1, day1.TransfersCompleted, "the transfer completes day 1");
            Assert.AreEqual(24, day1.UnitsStocked, "24 loaves stocked");
            Assert.AreEqual(8, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId),
                "bin 20 - 12 lb after the bake: the 8-lb residue");
            Assert.AreEqual(1952, s.BakeryRuntime.CurrentCashCents, "bakery 1,904c + 48c wholesale");

            int diagBefore = s.Diag.Count;
            BakeryWholesaleDailyResult day2 = RunDay(s, null, 2, wireNotions: false, millbrookSupplier: supplier);
            Assert.AreEqual(0, day2.BatchesBaked, "day 2 bakes nothing on the 8-lb residue");
            Assert.IsTrue(day2.Refusal != null && day2.Refusal.Contains("flour"),
                "day 2 refuses on flour; refusal: " + day2.Refusal);
            Assert.AreEqual(4, day2.FlourNeedPoundsRemaining, "the shortfall is 12 - 8 = 4 lb");
            Assert.AreEqual(1, day2.MillbrookReorderSacks, "the driver orders only today's shortfall rounded up: 1 sack");
            Assert.IsTrue(!string.IsNullOrWhiteSpace(day2.MillbrookReorderLotId),
                "the 1-sack reorder is placed day 2");
            string dayDiag = string.Join(" | ", s.Diag.GetRange(diagBefore, s.Diag.Count - diagBefore));
            Assert.IsTrue(dayDiag.Contains("covered 24u"),
                "the blocked line still calls the planned batch covered; diag: " + dayDiag);
            Assert.IsTrue(dayDiag.Contains("producible: no (flour)"),
                "the blocked line states the batch is not producible on flour; diag: " + dayDiag);
            Assert.AreEqual(1904, s.BakeryRuntime.CurrentCashCents, "bakery 1,952c - 48c for the 1-sack reorder");
            Assert.AreEqual(144, supplier.CashCents, "Millbrook +96c (2-sack) + 48c (1-sack reorder)");
            Assert.AreEqual(200, supplier.GetInventoryUnits("bakery_flour_sack"), "Millbrook 230 - 20 - 10 lb");
        }

        [Test]
        public void Flour_ReorderInTransit_NoDuplicateOrder_WhileOutstanding()
        {
            // Priority 3 Slice 1 (H-A1): the single-outstanding-order
            // guard. With the day-2 1-sack reorder lot placed,
            // uncommitted and unreceived, the day-3 driver refuses a
            // second reorder naming the outstanding lot and orders
            // nothing — so the 8-lb residue cannot be closed sooner by
            // double-ordering while the first sack is still in the
            // freight pipeline.
            Slice s = NewSlice();
            var supplier = new OffMapAidBusinessRuntime(MillbrookDefinition());
            var adapter = new MillbrookFlourFreightCustodyAdapter(supplier, s.Bakery, s.Ids);
            var bakeryCash = new Series2BusinessRuntimeCashPort(s.BakeryRuntime);

            Assert.IsNull(supplier.TryPlaceOrder(
                "test-flour-2sack", BakeryInstanceId, "Glen Pierce", 2, bakeryCash, 1, out OffMapAidCargoLot twoSackLot),
                "the 2-sack Millbrook order should place");
            ReceiveMillbrookLot(s, adapter, twoSackLot, 1);
            Assert.AreEqual(20, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId),
                "bin 20 lb after the 2-sack receipt");

            SeedInputs(s, 0, 1, 40);
            BookAccount(s, 24, 1, 2, 1);

            BakeryWholesaleDailyResult day1 = RunDay(s, null, 1, wireNotions: false, millbrookSupplier: supplier);
            Assert.AreEqual(1, day1.BatchesBaked, "day 1 bakes once from the 20-lb bin");
            Assert.AreEqual(8, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId), "bin 8 lb after the bake");

            BakeryWholesaleDailyResult day2 = RunDay(s, null, 2, wireNotions: false, millbrookSupplier: supplier);
            Assert.AreEqual(0, day2.BatchesBaked, "day 2 bakes nothing on the 8-lb residue");
            Assert.AreEqual(1, day2.MillbrookReorderSacks, "day 2 orders the 4-lb shortfall as 1 sack");
            Assert.IsTrue(!string.IsNullOrWhiteSpace(day2.MillbrookReorderLotId),
                "the 1-sack reorder is placed day 2");
            string reorderLotId = day2.MillbrookReorderLotId;
            Assert.AreEqual(2, supplier.Lots.Count, "exactly two Millbrook lots (received 2-sack + outstanding 1-sack reorder)");
            Assert.AreEqual(1904, s.BakeryRuntime.CurrentCashCents, "bakery 2,000 - 96c (2-sack) + 48c wholesale - 48c (1-sack reorder)");

            int diagBefore = s.Diag.Count;
            BakeryWholesaleDailyResult day3 = RunDay(s, null, 3, wireNotions: false, millbrookSupplier: supplier);
            Assert.IsTrue(string.IsNullOrWhiteSpace(day3.MillbrookReorderLotId),
                "no second reorder while the 1-sack lot is outstanding; lot id: " + day3.MillbrookReorderLotId);
            Assert.AreEqual(0, day3.MillbrookReorderSacks, "no sacks reordered day 3");
            Assert.AreEqual(0, day3.BatchesBaked, "still nothing bakes day 3");
            Assert.IsTrue(day3.Refusal != null && day3.Refusal.Contains("flour"),
                "day 3 still refuses on flour; refusal: " + day3.Refusal);
            string dayDiag = string.Join(" | ", s.Diag.GetRange(diagBefore, s.Diag.Count - diagBefore));
            Assert.IsTrue(dayDiag.Contains("already covers"),
                "the day-3 refusal names the already-covered outstanding lot; diag: " + dayDiag);
            Assert.IsTrue(dayDiag.Contains(reorderLotId),
                "the refusal names the outstanding 1-sack lot; diag: " + dayDiag);
            int reorderLots = 0;
            foreach (OffMapAidCargoLot lot in supplier.Lots)
                if (lot != null && lot.LotId != null && lot.LotId.Contains("flour-reorder-")) reorderLots++;
            Assert.AreEqual(1, reorderLots, "exactly 1 reorder lot exists");
            Assert.AreEqual(2, supplier.Lots.Count, "still exactly two Millbrook lots");
            Assert.AreEqual(1904, s.BakeryRuntime.CurrentCashCents, "no second debit: bakery cash unchanged by the refused reorder");
        }

        [Test]
        public void NotionsPipeline_SixtyDayCycle_TwoFullPeriods()
        {
            // Priority 3 Slice 1 (H-B1/H-A2): pins the 14-on / 14-off
            // notions cycle over a full 60 days. Flour is test-seeded
            // 12 lb/day so only the notions leg can block; fuel is
            // seeded to 40u (28 bakes in the run, inside the cap).
            // Priming orders days 1-14 feed bakes days 15-28; no order
            // is placed on a bake day, so the pipeline drains and
            // baking stalls days 29-42 while orders resume 1/day;
            // those arrivals feed bakes days 43-56; the second stall
            // resumes days 57-60.
            //
            // Finite-counterparty note: the production registration
            // carries the authored 30u finite supply, so the second
            // stall window can order only 2 more units (days 57-58,
            // orders 29-30) before the supply exhausts; days 59-60
            // order nothing. The 14 + 14 order counts below are the two
            // full 14-order windows (priming and first stall); the cap
            // itself is pinned by Slice 6.
            Slice s = NewSlice();
            Series2CounterpartyRegistration reg = ProductionRegistration();
            Assert.IsNull(reg.RegisterInto(s.Market, s.Diag), "production counterparty registration should succeed");
            SeedInputs(s, 0, 0, 40);
            BookAccount(s, 24, 1, 2, 2);

            for (int day = 1; day <= 14; day++)
            {
                SeedInputs(s, 12, 0, 0);
                BakeryWholesaleDailyResult r = RunDay(s, reg, day);
                Assert.AreEqual(1, r.NotionsOrdered, $"one priming notion ordered, day {day}");
                Assert.AreEqual(0, r.BatchesBaked, $"nothing bakes before the first arrival, day {day}");
                Assert.IsTrue(r.Refusal != null && r.Refusal.Contains("notions"),
                    $"day {day} refuses on notions; refusal: {r.Refusal}");
            }
            Assert.AreEqual(14, s.StoreService.Orders.Count, "14 priming orders over days 1-14");
            for (int i = 0; i < 14; i++)
            {
                Assert.AreEqual(i + 15, s.StoreService.Orders[i].ExpectedArrivalDayIndex,
                    $"priming order {i + 1} arrives day {i + 15} (14-day lag)");
                Assert.AreEqual(1, s.StoreService.Orders[i].UnitsOrdered,
                    $"priming order {i + 1} is 1u");
            }

            for (int day = 15; day <= 28; day++)
            {
                SeedInputs(s, 12, 0, 0);
                BakeryWholesaleDailyResult r = RunDay(s, reg, day);
                Assert.AreEqual(1, r.BatchesBaked, $"the primed pipeline bakes daily, day {day}");
                Assert.AreEqual(1, r.TransfersCompleted, $"transfer completes, day {day}");
                Assert.AreEqual(0, r.NotionsOrdered, $"no replacement order on an arrival day (feed covers), day {day}");
                Assert.AreEqual(1, r.NotionsUnitsFed, $"the day-{day - 14} priming order's unit feeds the day {day} bake");
            }
            Assert.AreEqual(14, s.StoreService.Lots.Count, "14 arrival lots, one per priming order");
            Assert.AreEqual(14, s.Bakery.WholesaleDeliveries.Count, "14 consecutive daily bakes = 14 deliveries");

            for (int day = 29; day <= 42; day++)
            {
                SeedInputs(s, 12, 0, 0);
                int diagBefore = s.Diag.Count;
                BakeryWholesaleDailyResult r = RunDay(s, reg, day);
                Assert.AreEqual(0, r.BatchesBaked, $"no bake once the primed window drains, day {day}");
                Assert.AreEqual(1, r.NotionsOrdered, $"orders resume 1/day on stall days, day {day}");
                Assert.IsTrue(r.Refusal != null && r.Refusal.Contains("notions"),
                    $"day {day} refuses on notions; refusal: {r.Refusal}");
                AssertBlockedLineNamesCause(s, diagBefore, s.Bakery.Batches[s.Bakery.Batches.Count - 1].BatchId, day, "notions");
            }
            Assert.AreEqual(28, s.StoreService.Orders.Count, "14 priming + 14 stall-day orders (14 + 14)");
            for (int i = 14; i < 28; i++)
            {
                Assert.AreEqual(s.StoreService.Orders[i].OrderDayIndex + 14, s.StoreService.Orders[i].ExpectedArrivalDayIndex,
                    $"stall-day order {i + 1} arrives 14 days after its order day");
                Assert.AreEqual(1, s.StoreService.Orders[i].UnitsOrdered,
                    $"stall-day order {i + 1} is 1u");
            }

            for (int day = 43; day <= 56; day++)
            {
                SeedInputs(s, 12, 0, 0);
                BakeryWholesaleDailyResult r = RunDay(s, reg, day);
                Assert.AreEqual(1, r.BatchesBaked, $"the second pipeline window bakes daily, day {day}");
                Assert.AreEqual(1, r.TransfersCompleted, $"transfer completes, day {day}");
                Assert.AreEqual(0, r.NotionsOrdered, $"no replacement order on an arrival day (feed covers), day {day}");
                Assert.AreEqual(1, r.NotionsUnitsFed, $"the day-{day - 14} stall-window order's unit feeds the day {day} bake");
            }
            Assert.AreEqual(28, s.StoreService.Lots.Count, "28 arrival lots, one per order placed days 1-42");
            Assert.AreEqual(28, s.Bakery.WholesaleDeliveries.Count, "28 daily bakes = 28 deliveries over the run");

            for (int day = 57; day <= 60; day++)
            {
                SeedInputs(s, 12, 0, 0);
                int diagBefore = s.Diag.Count;
                BakeryWholesaleDailyResult r = RunDay(s, reg, day);
                Assert.AreEqual(0, r.BatchesBaked, $"the stall resumes once the second window drains, day {day}");
                Assert.AreEqual(day <= 58 ? 1 : 0, r.NotionsOrdered,
                    day <= 58
                        ? $"day {day} orders the 1u shortfall (finite supply still has room)"
                        : $"day {day} orders nothing: the 30u finite supply is exhausted");
                Assert.IsTrue(r.Refusal != null && r.Refusal.Contains("notions"),
                    $"day {day} refuses on notions; refusal: {r.Refusal}");
                AssertBlockedLineNamesCause(s, diagBefore, s.Bakery.Batches[s.Bakery.Batches.Count - 1].BatchId, day, "notions");
            }
            Assert.AreEqual(30, s.StoreService.Orders.Count,
                "30 orders total: 14 priming + 14 first-stall + 2 second-stall, exhausting the 30u finite supply");
            Assert.AreEqual(300, s.Market.TotalOutflowCents, "30 orders x 10c booked as market outflow at order time");
            Assert.AreEqual(28, s.StoreService.Lots.Count, "no further arrivals inside the 60-day run (days 57-58 orders arrive days 71-72)");
            Assert.AreEqual(3064, s.BakeryRuntime.CurrentCashCents,
                "bakery 2,000 - 280c notions feeds (28 x 10c) + 1,344c wholesale (28 x 48c)");
            Assert.AreEqual(3656, s.StoreRuntime.CurrentCashCents,
                "store 5,000 - 280c receipts + 280c feeds - 1,344c wholesale");
            Assert.AreEqual(12, s.Bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId),
                "fuel 40 - 28 bakes = 12u left: fuel never blocks this run");
        }

        [Test]
        public void FlourAndNotions_BlockedCauseWord_MatchesFirstFailingInput()
        {
            // Priority 3 Slice 1 (A2): the blocked-batch cause word is
            // the first failing Precheck input in check order (flour,
            // notions, fuel, kit, oven, budget) — both in
            // result.Refusal and in the blocked-batch line. Each leg is
            // a fresh slice with exactly the inputs that leg needs, so
            // only one gate can fail at a time, plus two order legs
            // where two inputs fail at once and the earlier check wins
            // (flour over notions, notions over fuel).
            //
            // Leg 1 — flour short (and notions short too): flour is
            // checked first, so the cause is flour even though notions
            // also fails.
            {
                Slice s = NewSlice();
                SeedInputs(s, 0, 0, 40);
                BookAccount(s, 24, 1, 2, 2);
                int diagBefore = s.Diag.Count;
                BakeryWholesaleDailyResult r = RunDay(s, null, 2, wireNotions: false);
                Assert.AreEqual(1, r.BatchesPlanned, "leg 1 plans the batch");
                Assert.AreEqual(0, r.BatchesBaked, "leg 1 bakes nothing");
                Assert.IsTrue(r.Refusal != null && r.Refusal.Contains("flour"),
                    "leg 1 refuses on flour (checked before notions); refusal: " + r.Refusal);
                AssertBlockedLineNamesCause(s, diagBefore, s.Bakery.Batches[0].BatchId, 2, "flour");
            }

            // Leg 2 — flour sufficient, notions short: the cause is notions.
            {
                Slice s = NewSlice();
                SeedInputs(s, 12, 0, 40);
                BookAccount(s, 24, 1, 2, 2);
                int diagBefore = s.Diag.Count;
                BakeryWholesaleDailyResult r = RunDay(s, null, 2, wireNotions: false);
                Assert.AreEqual(1, r.BatchesPlanned, "leg 2 plans the batch");
                Assert.AreEqual(0, r.BatchesBaked, "leg 2 bakes nothing");
                Assert.IsTrue(r.Refusal != null && r.Refusal.Contains("notions"),
                    "leg 2 refuses on notions; refusal: " + r.Refusal);
                AssertBlockedLineNamesCause(s, diagBefore, s.Bakery.Batches[0].BatchId, 2, "notions");
            }

            // Leg 3 — flour sufficient, notions short, fuel 0: notions
            // is checked before fuel, so the cause is still notions.
            {
                Slice s = NewSlice();
                SeedInputs(s, 12, 0, 0);
                BookAccount(s, 24, 1, 2, 2);
                int diagBefore = s.Diag.Count;
                BakeryWholesaleDailyResult r = RunDay(s, null, 2, wireNotions: false);
                Assert.AreEqual(1, r.BatchesPlanned, "leg 3 plans the batch");
                Assert.AreEqual(0, r.BatchesBaked, "leg 3 bakes nothing");
                Assert.IsTrue(r.Refusal != null && r.Refusal.Contains("notions"),
                    "leg 3 refuses on notions (checked before fuel); refusal: " + r.Refusal);
                AssertBlockedLineNamesCause(s, diagBefore, s.Bakery.Batches[0].BatchId, 2, "notions");
            }

            // Leg 4 — flour + notions sufficient, fuel 0: the cause is fuel.
            {
                Slice s = NewSlice();
                SeedInputs(s, 12, 1, 0);
                BookAccount(s, 24, 1, 2, 2);
                int diagBefore = s.Diag.Count;
                BakeryWholesaleDailyResult r = RunDay(s, null, 2, wireNotions: false);
                Assert.AreEqual(1, r.BatchesPlanned, "leg 4 plans the batch");
                Assert.AreEqual(0, r.BatchesBaked, "leg 4 bakes nothing");
                Assert.IsTrue(r.Refusal != null && r.Refusal.Contains("fuel"),
                    "leg 4 refuses on fuel; refusal: " + r.Refusal);
                AssertBlockedLineNamesCause(s, diagBefore, s.Bakery.Batches[0].BatchId, 2, "fuel");
            }
        }

        // ---- Priority 3 Slice 2 (2026-11-08): Precheck custody
        // accounting (H-A3) ------------------------------------------
        // Defect: Precheck demands batches x 12 lb in the bin for every
        // adopted batch regardless of stage, while PrepBatchToProofing
        // has already dispensed 12 lb into a Prepped/Proofing batch's
        // FlourInCustody. A stranded batch is therefore refused on flour
        // forever even though its flour is held, and the driver keeps
        // naming flour for a batch that needs none. The same
        // double-count applies to notions (NotionsUsed).
        //
        // STRAND-MECHANISM DEVIATION FROM THE PLAN (verified in source
        // before writing the test): the plan strands the batch with
        // SetProofingRackSlots(0), but production REFUSES a non-positive
        // slot count — BakeryShopRuntime.SetProofingRackSlots returns the
        // refusal "proofing rack needs a positive slot count" and the old
        // capacity stands (pinned by
        // BakeryDoughSchedulingTests.SetProofingRackSlots_RefusesNonPositive).
        // Slots therefore cannot be forced to 0. The strand here uses the
        // rack's real capacity instead: slots = 1 gives 120 proof-minutes
        // per day (one slot x 120m), exactly one bread batch's proof, so
        // three directly-planned batches prepped the same day compete for
        // it — the first proofs and bakes, the other two strand in
        // Proofing holding their dispensed custody (12 lb flour + 1
        // notion each) with the bin at 8 lb. The next day the rack lets
        // one more proof and bake; restoring slots to 4 on the third day
        // lets the last held batch proof and bake. The substance the plan
        // pins is unchanged: a held batch must not be re-charged for
        // flour/notions it already holds, and the same batch must bake
        // with no further flour purchase once the rack lets it through.
        // The driver flour block still computes its shortfall from bin
        // stock only (FlourNeedPoundsRemaining 16 lb day 2, 4 lb day 3);
        // that block is OUT OF SCOPE for Slice 2 (Precheck only), so its
        // shortfall figure is asserted as the unchanged behaviour, and
        // "no new reorder" is pinned in this unwired fixture (no
        // Millbrook supplier, store holds 0 sacks): no sacks are bought
        // and no reorder lot is placed on any day.
        private static int SumCustodyUnits(List<BakeryFlourDispenseLine> lines)
        {
            int total = 0;
            if (lines != null)
                foreach (BakeryFlourDispenseLine line in lines)
                    if (line != null) total += Math.Max(0, line.UnitsTaken);
            return total;
        }

        private static List<BakeryDoughBatch> BatchesInStage(Slice s, BakeryBatchStage stage)
        {
            var found = new List<BakeryDoughBatch>();
            foreach (BakeryDoughBatch batch in s.Bakery.Batches)
                if (batch != null && batch.Stage == stage) found.Add(batch);
            return found;
        }

        private static BakeryDoughBatch FindBatch(Slice s, string batchId)
        {
            foreach (BakeryDoughBatch batch in s.Bakery.Batches)
                if (batch != null && string.Equals(batch.BatchId, batchId, StringComparison.Ordinal)) return batch;
            return null;
        }

        [Test]
        public void Precheck_PreppedBatchHoldingCustody_NotChargedAgainForFlour()
        {
            // Priority 3 Slice 2 (H-A3): the reproducing test. Expected
            // to FAIL before the fix (day 2 refuses on flour for batches
            // holding their 12 lb in custody) and PASS after it.
            Slice s = NewSlice();
            SeedInputs(s, 44, 3, 3);
            BookAccount(s, 24, 1, 2, 1);
            Assert.IsNull(s.Bakery.SetProofingRackSlots(1, s.Diag),
                "one rack slot (120 proof-minutes/day) should set; diag: " + string.Join("; ", s.Diag));

            // Three batches planned directly (not via the wholesale
            // planner): the planner would plan at most one, and the
            // strand needs three batches competing for one slot's rack
            // credit. The account is already covered 3 x 24u, so the
            // planner plans nothing further on any day of the run.
            List<string> directIds = s.ProductionDriver.PlanBatches(BakeryBreadCatalog.BreadLoafId, 3, 1, s.Diag);
            Assert.IsNotNull(directIds, "three batches should plan directly; diag: " + string.Join("; ", s.Diag));
            Assert.AreEqual(3, directIds.Count, "three batches planned directly");

            // Day 1: all three prep (bin 44 -> 8 lb, notions 3 -> 0,
            // fuel 3 -> 2); the rack proofs only the first, so it bakes
            // and the other two strand in Proofing holding custody.
            BakeryWholesaleDailyResult day1 = RunDay(s, null, 1, wireNotions: false);
            Assert.AreEqual(0, day1.BatchesPlanned, "day 1 plans nothing: the three direct batches already cover the account");
            Assert.AreEqual(1, day1.BatchesBaked, "day 1 bakes exactly one batch: the rack proofs one per day at 1 slot");
            Assert.AreEqual(1, day1.TransfersCompleted, "the transfer completes day 1");
            Assert.AreEqual(24, day1.UnitsStocked, "24 loaves stocked day 1");
            Assert.AreEqual(8, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId),
                "bin 44 - 3 x 12 lb dispensed at prep = 8 lb after day 1");
            Assert.AreEqual(0, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.NotionsItemId),
                "bin notions 3 - 3 dispensed at prep = 0 after day 1");
            Assert.AreEqual(2, s.Bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId),
                "fuel 3 - 1 firing day 1 = 2u");
            List<BakeryDoughBatch> strandedDay1 = BatchesInStage(s, BakeryBatchStage.Proofing);
            Assert.AreEqual(2, strandedDay1.Count, "two batches strand in Proofing after day 1 (rack credit exhausted)");
            foreach (BakeryDoughBatch batch in strandedDay1)
            {
                Assert.AreEqual(12, SumCustodyUnits(batch.FlourInCustody),
                    $"stranded batch {batch.BatchId} holds its 12 lb flour in custody");
                Assert.AreEqual(1, SumCustodyUnits(batch.NotionsUsed),
                    $"stranded batch {batch.BatchId} holds its 1 notion in custody");
                Assert.AreEqual(120, batch.ProofMinutesRemaining,
                    $"stranded batch {batch.BatchId} got no proof credit day 1");
            }
            List<BakeryDoughBatch> bakedDay1 = BatchesInStage(s, BakeryBatchStage.Baked);
            Assert.AreEqual(1, bakedDay1.Count, "one batch baked day 1");
            Assert.AreEqual(0, SumCustodyUnits(bakedDay1[0].FlourInCustody),
                "the baked batch's custody cleared into the bread at baking");
            Assert.AreEqual(0, SumCustodyUnits(bakedDay1[0].NotionsUsed),
                "the baked batch's notions cleared into the bread at baking");

            // Day 2 (rack still 1 slot): the two held batches must NOT
            // be refused on flour — each already holds its 12 lb — even
            // though the bin reads only 8 lb. Before the fix this
            // assertion fails: Precheck demands 2 x 12 lb in the bin
            // and refuses "flour 8 lb on hand, need 24 lb".
            BakeryWholesaleDailyResult day2 = RunDay(s, null, 2, wireNotions: false);
            Assert.IsTrue(day2.Refusal == null || !day2.Refusal.Contains("flour"),
                "day-2 Precheck must not refuse on flour for the held batches; refusal: " + day2.Refusal);
            Assert.AreEqual(0, day2.BatchesPlanned, "day 2 plans nothing: the two held batches still cover the account");
            Assert.AreEqual(1, day2.BatchesBaked, "day 2 bakes one held batch (the rack proofs one per day at 1 slot)");
            Assert.AreEqual(1, day2.TransfersCompleted, "the transfer completes day 2");
            Assert.AreEqual(8, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId),
                "bin still 8 lb after day 2: the bake consumed custody flour, never bin flour");
            Assert.AreEqual(0, day2.FlourSacksPurchased, "no flour bought day 2");
            Assert.IsTrue(string.IsNullOrWhiteSpace(day2.MillbrookReorderLotId),
                "no Millbrook reorder placed day 2; lot id: " + day2.MillbrookReorderLotId);
            Assert.AreEqual(16, day2.FlourNeedPoundsRemaining,
                "the driver flour block still names the bin-only shortfall (2 x 12 - 8 = 16 lb); that block is out of scope for Slice 2 and unchanged");
            Assert.AreEqual(1, s.Bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId),
                "fuel 2 - 1 firing day 2 = 1u");
            List<BakeryDoughBatch> strandedDay2 = BatchesInStage(s, BakeryBatchStage.Proofing);
            Assert.AreEqual(1, strandedDay2.Count, "one batch still strands in Proofing after day 2");
            Assert.AreEqual(12, SumCustodyUnits(strandedDay2[0].FlourInCustody),
                "the still-held batch keeps its 12 lb flour in custody");
            Assert.AreEqual(1, SumCustodyUnits(strandedDay2[0].NotionsUsed),
                "the still-held batch keeps its 1 notion in custody");
            string lastHeldBatchId = strandedDay2[0].BatchId;
            Assert.AreEqual(2096, s.BakeryRuntime.CurrentCashCents,
                "bakery 2,000 + 48c + 48c wholesale (no flour/notions cash moves: inputs were test-seeded)");
            Assert.AreEqual(4904, s.StoreRuntime.CurrentCashCents,
                "store 5,000 - 48c - 48c wholesale");

            // Day 3: restore the rack to 4 slots. The last held batch
            // proofs and bakes with no further flour purchase: the bin
            // stays at 8 lb and no reorder is placed for it.
            Assert.IsNull(s.Bakery.SetProofingRackSlots(4, s.Diag),
                "restoring 4 rack slots should succeed; diag: " + string.Join("; ", s.Diag));
            BakeryWholesaleDailyResult day3 = RunDay(s, null, 3, wireNotions: false);
            Assert.IsTrue(day3.Refusal == null || !day3.Refusal.Contains("flour"),
                "day-3 Precheck must not refuse on flour for the held batch; refusal: " + day3.Refusal);
            Assert.AreEqual(0, day3.BatchesPlanned, "day 3 plans nothing: the held batch still covers the account");
            Assert.AreEqual(1, day3.BatchesBaked, "day 3 bakes the last held batch");
            Assert.AreEqual(1, day3.TransfersCompleted, "the transfer completes day 3");
            BakeryDoughBatch lastHeld = FindBatch(s, lastHeldBatchId);
            Assert.IsNotNull(lastHeld, "the day-2 held batch is still on the books day 3");
            Assert.AreEqual(BakeryBatchStage.Baked, lastHeld.Stage,
                "the SAME held batch bakes day 3 — no re-plan, no new batch id");
            Assert.AreEqual(0, BatchesInStage(s, BakeryBatchStage.Proofing).Count,
                "no batch strands after day 3");
            Assert.AreEqual(8, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId),
                "bin still 8 lb after day 3: the held batch baked on custody flour alone");
            Assert.AreEqual(0, day3.FlourSacksPurchased, "no flour bought day 3");
            Assert.IsTrue(string.IsNullOrWhiteSpace(day3.MillbrookReorderLotId),
                "no Millbrook reorder placed day 3; lot id: " + day3.MillbrookReorderLotId);
            Assert.AreEqual(4, day3.FlourNeedPoundsRemaining,
                "the driver flour block still names the bin-only shortfall (12 - 8 = 4 lb); out of scope for Slice 2 and unchanged");
            Assert.AreEqual(0, s.Bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId),
                "fuel 1 - 1 firing day 3 = 0u");
            Assert.AreEqual(3, s.Bakery.WholesaleDeliveries.Count,
                "three daily bakes = three deliveries, one per held batch");
            Assert.AreEqual(2144, s.BakeryRuntime.CurrentCashCents,
                "bakery 2,000 + 3 x 48c wholesale");
            Assert.AreEqual(4856, s.StoreRuntime.CurrentCashCents,
                "store 5,000 - 3 x 48c wholesale");
        }

        [Test]
        public void Precheck_PlannedBatch_StillChargedFullFlourNeed()
        {
            // Priority 3 Slice 2 (H-A3), complementary pin: a Planned-stage
            // batch with nothing in custody still requires the full 12 lb
            // in the bin. Guards the repair against over-correction — the
            // custody credit applies only to batches that actually hold
            // dispensed custody (Prepped/Proofing), never to a batch that
            // has not prepped yet. Passes before and after the fix.
            Slice s = NewSlice();
            SeedInputs(s, 8, 1, 40);
            BookAccount(s, 24, 1, 2, 2);

            BakeryWholesaleDailyResult r = RunDay(s, null, 2, wireNotions: false);
            Assert.AreEqual(1, r.BatchesPlanned, "the batch is planned day 2");
            Assert.AreEqual(0, r.BatchesBaked, "nothing bakes on the 8-lb residue");
            Assert.IsTrue(r.Refusal != null && r.Refusal.Contains("flour"),
                "the flour refusal stands for a Planned batch; refusal: " + r.Refusal);
            Assert.IsTrue(r.Refusal.Contains("flour 8 lb on hand, need 12 lb"),
                "the refusal charges the full 12 lb for the one Planned batch; refusal: " + r.Refusal);
            Assert.AreEqual(4, r.FlourNeedPoundsRemaining, "the shortfall is 12 - 8 = 4 lb");
            Assert.AreEqual(BakeryBatchStage.Planned, s.Bakery.Batches[0].Stage, "the batch stays Planned");
            Assert.AreEqual(0, SumCustodyUnits(s.Bakery.Batches[0].FlourInCustody),
                "the Planned batch holds no flour in custody");
            Assert.AreEqual(0, SumCustodyUnits(s.Bakery.Batches[0].NotionsUsed),
                "the Planned batch holds no notions in custody");
            Assert.AreEqual(8, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId),
                "bin still 8 lb: nothing was dispensed");
            Assert.AreEqual(0, r.FlourSacksPurchased, "no flour bought: the store holds 0 sacks");
        }
    }
}
