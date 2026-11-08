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

        private static Slice NewSlice(int storeCash = 5000, int bakeryCash = 2000, int storeTarget = 1000)
        {
            var s = new Slice();
            s.StoreRuntime.BindBusinessIdentity(StoreInstanceId);
            s.StoreRuntime.AddCashCents(storeCash);
            s.StoreRuntime.EnsureCategoryStock("staple_food", 0, storeTarget);
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
            BakeryWholesaleDailyResult prior = default(BakeryWholesaleDailyResult),
            int notionsDepth = 0, int flourDepth = 0) =>
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
                NotionsPipelineDepthDays = notionsDepth,
                FlourPipelineDepthDays = flourDepth,
            };

        private static BakeryWholesaleDailyResult RunDay(
            Slice s, Series2CounterpartyRegistration reg, int dayIndex,
            bool wireNotions = true, OffMapAidBusinessRuntime millbrookSupplier = null,
            bool threadCumulative = false,
            BakeryWholesaleDailyResult prior = default(BakeryWholesaleDailyResult),
            int notionsDepth = 0, int flourDepth = 0) =>
            BakeryWholesaleDailyDriver.RunDay(
                DriverInputsFor(s, reg, dayIndex, wireNotions, millbrookSupplier, threadCumulative, prior, notionsDepth, flourDepth), s.Diag);

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

        // ---- Priority 3 Slice 3 (2026-11-08): pipeline-coverage
        // diagnostics (log-only production change) -------------------
        // Defect: the investigation cannot separate H-A1 from H-A2/H-A3
        // live, because no single line states the Bakery's full pipeline
        // position: bin, in-custody, in-transit with arrival days,
        // days-of-cover at the account's daily rate, and next arrival
        // day. The driver logs refusals and per-day actuals but never
        // the coverage position a reorder decision needs. These two
        // tests assert the line's contents in source; they are expected
        // to FAIL before the diagnostic is added (no such line exists)
        // and PASS after it.
        //
        // FLOUR-ARRIVAL DEVIATION FROM THE PLAN (verified in source
        // before writing the test): the plan's test 2 asks for "the next
        // flour arrival day (or 'none in flight')", but no Millbrook lot
        // carries a scheduled arrival day — OffMapAidCargoLot has
        // CreatedDayIndex only, and no transit-days figure exists in the
        // driver-held Millbrook quote or freight path (the pickup ~day+3
        // / receipt ~day+7 figures in plan §1 are one Set B observation,
        // not an authored schedule). Inventing a day would violate the
        // plan's own "compute only from state the driver already holds".
        // The line therefore states the outstanding lot with its order
        // day and "arrival unscheduled (freight pipeline)", and the
        // "next arrival day" field is driven by the notions order list
        // (ExpectedArrivalDayIndex), the only scheduled arrivals the
        // driver holds. Test 2 pins exactly that: leg A names the
        // outstanding reorder lot + order day + unscheduled (no invented
        // day), leg B (nothing in flight anywhere) reads "none in
        // flight" on both fields.
        //
        // DAYS-OF-COVER DEFINITION (the plan names the field, not the
        // formula): min(flour batch-days, notions batch-days) at 1
        // batch/day, where flour batch-days = (bin + custody +
        // in-transit lb) / 12 and notions batch-days = (on hand + in
        // flight) / 1. Custody flour counts because it is committed to
        // the active batch's bake, which is one of the covered daily
        // bakes; the line states the formula's inputs so the figure is
        // auditable from the line itself.
        private static List<string> CoverageLinesForDay(Slice s, int diagBefore, int day)
        {
            var found = new List<string>();
            for (int i = diagBefore; i < s.Diag.Count; i++)
            {
                string line = s.Diag[i];
                if (line != null && line.StartsWith(
                    "[BakeryProduction] pipeline coverage day " + day + ":", StringComparison.Ordinal))
                    found.Add(line);
            }
            return found;
        }

        private static int CountCoverageLines(Slice s, int day)
        {
            int count = 0;
            foreach (string line in s.Diag)
                if (line != null && line.StartsWith(
                    "[BakeryProduction] pipeline coverage day " + day + ":", StringComparison.Ordinal))
                    count++;
            return count;
        }

        private static void AssertCoverageLineNeverLabelsPlannedYieldAsStock(string line)
        {
            // The line must NEVER label planned yield as stock: no
            // "covered" anywhere in the line (stronger than the plan's
            // "no covered without planned adjacent" — this line carries
            // no "covered" at all), and the planned yield is stated as
            // planned yield, explicitly "not stock".
            Assert.IsTrue(!line.Contains("covered"),
                "the coverage line must never say 'covered' — planned yield is not stock; line: " + line);
            Assert.IsTrue(line.Contains("planned yield 24u/batch, not stock"),
                "the coverage line states the planned yield as planned yield, never as stock; line: " + line);
        }

        [Test]
        public void CoverageLine_ReportsBinCustodyInTransit_AndDaysOfCover()
        {
            // Priority 3 Slice 3: drive 3 days of the priming window
            // (flour test-seeded 12 lb/day, notions through the real
            // 14-day pipeline) and assert one pipeline coverage line
            // per day carrying bin flour, flour in custody, outstanding
            // Millbrook sacks, notions on hand, notions in flight by
            // arrival day, days-of-cover at 1 batch/day, and the next
            // arrival day — with producible: no (notions) from the
            // day's Precheck outcome, matching the day's actual refusal.
            Slice s = NewSlice();
            Series2CounterpartyRegistration reg = ProductionRegistration();
            Assert.IsNull(reg.RegisterInto(s.Market, s.Diag), "production counterparty registration should succeed");
            SeedInputs(s, 0, 0, 40);
            BookAccount(s, 24, 1, 2, 2);

            // Day 1: bin 12 lb, nothing baked (notions-blocked), 1
            // notion ordered arriving day 15. Cover = min(12/12, 1/1) = 1.
            SeedInputs(s, 12, 0, 0);
            int diagBefore1 = s.Diag.Count;
            BakeryWholesaleDailyResult day1 = RunDay(s, reg, 1);
            Assert.AreEqual(0, day1.BatchesBaked, "day 1 bakes nothing before the first notions arrival");
            Assert.IsTrue(day1.Refusal != null && day1.Refusal.Contains("notions"),
                "day 1 refuses on notions; refusal: " + day1.Refusal);
            List<string> lines1 = CoverageLinesForDay(s, diagBefore1, 1);
            Assert.AreEqual(1, lines1.Count, "exactly one coverage line on day 1");
            string line1 = lines1[0];
            Assert.IsTrue(line1.Contains("bin flour 12 lb"), "day 1 line carries bin flour 12 lb; line: " + line1);
            Assert.IsTrue(line1.Contains("flour in custody 0 lb"), "day 1 line carries flour in custody 0 lb; line: " + line1);
            Assert.IsTrue(line1.Contains("notions on hand 0u"), "day 1 line carries notions on hand 0u; line: " + line1);
            Assert.IsTrue(line1.Contains("notions in custody 0u"), "day 1 line carries notions in custody 0u; line: " + line1);
            Assert.IsTrue(line1.Contains("fuel 40u"), "day 1 line carries fuel 40u; line: " + line1);
            Assert.IsTrue(line1.Contains("Millbrook in transit: none in flight"),
                "day 1 line carries no outstanding Millbrook sacks (no supplier wired); line: " + line1);
            Assert.IsTrue(line1.Contains("notions in flight: 1u (day 15: 1u)"),
                "day 1 line carries the 1u notion in flight arriving day 15; line: " + line1);
            Assert.IsTrue(line1.Contains("days-of-cover 1 at 1 batch/day"),
                "day 1 line carries days-of-cover 1; line: " + line1);
            Assert.IsTrue(line1.Contains("next arrival day 15"),
                "day 1 line carries next arrival day 15; line: " + line1);
            Assert.IsTrue(line1.Contains("first failing input: notions"),
                "day 1 line names notions as the first failing input; line: " + line1);
            Assert.IsTrue(line1.Contains("producible: no (notions)"),
                "day 1 line carries producible: no (notions) from the Precheck outcome; line: " + line1);
            AssertCoverageLineNeverLabelsPlannedYieldAsStock(line1);

            // Day 2: bin 24 lb (12 + 12 seeded, nothing baked), 2
            // notions in flight (arriving days 15, 16). Cover = 2.
            SeedInputs(s, 12, 0, 0);
            int diagBefore2 = s.Diag.Count;
            BakeryWholesaleDailyResult day2 = RunDay(s, reg, 2);
            Assert.AreEqual(0, day2.BatchesBaked, "day 2 bakes nothing before the first notions arrival");
            Assert.IsTrue(day2.Refusal != null && day2.Refusal.Contains("notions"),
                "day 2 refuses on notions; refusal: " + day2.Refusal);
            List<string> lines2 = CoverageLinesForDay(s, diagBefore2, 2);
            Assert.AreEqual(1, lines2.Count, "exactly one coverage line on day 2");
            string line2 = lines2[0];
            Assert.IsTrue(line2.Contains("bin flour 24 lb"), "day 2 line carries bin flour 24 lb; line: " + line2);
            Assert.IsTrue(line2.Contains("flour in custody 0 lb"), "day 2 line carries flour in custody 0 lb; line: " + line2);
            Assert.IsTrue(line2.Contains("notions on hand 0u"), "day 2 line carries notions on hand 0u; line: " + line2);
            Assert.IsTrue(line2.Contains("notions in flight: 2u (day 15: 1u, day 16: 1u)"),
                "day 2 line carries 2u notions in flight by arrival day; line: " + line2);
            Assert.IsTrue(line2.Contains("days-of-cover 2 at 1 batch/day"),
                "day 2 line carries days-of-cover 2; line: " + line2);
            Assert.IsTrue(line2.Contains("next arrival day 15"),
                "day 2 line carries next arrival day 15 (the earliest in-flight arrival); line: " + line2);
            Assert.IsTrue(line2.Contains("first failing input: notions"),
                "day 2 line names notions as the first failing input; line: " + line2);
            Assert.IsTrue(line2.Contains("producible: no (notions)"),
                "day 2 line carries producible: no (notions) from the Precheck outcome; line: " + line2);
            AssertCoverageLineNeverLabelsPlannedYieldAsStock(line2);

            // Day 3: bin 36 lb, 3 notions in flight (days 15-17). Cover = 3.
            SeedInputs(s, 12, 0, 0);
            int diagBefore3 = s.Diag.Count;
            BakeryWholesaleDailyResult day3 = RunDay(s, reg, 3);
            Assert.AreEqual(0, day3.BatchesBaked, "day 3 bakes nothing before the first notions arrival");
            Assert.IsTrue(day3.Refusal != null && day3.Refusal.Contains("notions"),
                "day 3 refuses on notions; refusal: " + day3.Refusal);
            List<string> lines3 = CoverageLinesForDay(s, diagBefore3, 3);
            Assert.AreEqual(1, lines3.Count, "exactly one coverage line on day 3");
            string line3 = lines3[0];
            Assert.IsTrue(line3.Contains("bin flour 36 lb"), "day 3 line carries bin flour 36 lb; line: " + line3);
            Assert.IsTrue(line3.Contains("flour in custody 0 lb"), "day 3 line carries flour in custody 0 lb; line: " + line3);
            Assert.IsTrue(line3.Contains("notions on hand 0u"), "day 3 line carries notions on hand 0u; line: " + line3);
            Assert.IsTrue(line3.Contains("notions in flight: 3u (day 15: 1u, day 16: 1u, day 17: 1u)"),
                "day 3 line carries 3u notions in flight by arrival day; line: " + line3);
            Assert.IsTrue(line3.Contains("days-of-cover 3 at 1 batch/day"),
                "day 3 line carries days-of-cover 3; line: " + line3);
            Assert.IsTrue(line3.Contains("next arrival day 15"),
                "day 3 line carries next arrival day 15; line: " + line3);
            Assert.IsTrue(line3.Contains("first failing input: notions"),
                "day 3 line names notions as the first failing input; line: " + line3);
            Assert.IsTrue(line3.Contains("producible: no (notions)"),
                "day 3 line carries producible: no (notions) from the Precheck outcome; line: " + line3);
            AssertCoverageLineNeverLabelsPlannedYieldAsStock(line3);

            // Bounded: exactly one coverage line per day over the run,
            // and each day's line precedes that day's blocked-batch line
            // (the coverage line is appended before RunScheduledDay).
            Assert.AreEqual(1, CountCoverageLines(s, 1), "exactly one coverage line for day 1 in the whole diag");
            Assert.AreEqual(1, CountCoverageLines(s, 2), "exactly one coverage line for day 2 in the whole diag");
            Assert.AreEqual(1, CountCoverageLines(s, 3), "exactly one coverage line for day 3 in the whole diag");
            string day3Diag = string.Join(" | ", s.Diag.GetRange(diagBefore3, s.Diag.Count - diagBefore3));
            int coverageIndex = day3Diag.IndexOf("[BakeryProduction] pipeline coverage day 3:", StringComparison.Ordinal);
            int blockedIndex = day3Diag.IndexOf("[BakeryProduction] batch ", StringComparison.Ordinal);
            Assert.IsTrue(coverageIndex >= 0 && blockedIndex >= 0 && coverageIndex < blockedIndex,
                "the day-3 coverage line precedes the day-3 blocked-batch line (before RunScheduledDay); diag: " + day3Diag);
        }

        [Test]
        public void CoverageLine_BlockedDay_NamesFirstFailingInput_AndNextArrival()
        {
            // Priority 3 Slice 3: on a flour-blocked day the coverage
            // line names flour as the first failing input and states the
            // flour pipeline position — the outstanding reorder lot with
            // its order day and "arrival unscheduled" (see the
            // flour-arrival deviation note above: no scheduled flour
            // arrival day exists in driver-held state, so none is
            // invented), or "none in flight" when nothing is outstanding.
            //
            // Leg A — a 2-sack Millbrook reorder is outstanding: bin 0
            // lb, notions sufficient (1u seeded), so flour is the first
            // failing input and the line must name the reorder lot.
            {
                Slice s = NewSlice();
                var supplier = new OffMapAidBusinessRuntime(MillbrookDefinition());
                Series2CounterpartyRegistration reg = ProductionRegistration();
                Assert.IsNull(reg.RegisterInto(s.Market, s.Diag), "production counterparty registration should succeed");
                SeedInputs(s, 0, 1, 40);
                BookAccount(s, 24, 1, 2, 1);

                int diagBefore = s.Diag.Count;
                BakeryWholesaleDailyResult day1 = RunDay(s, reg, 1, millbrookSupplier: supplier);
                Assert.AreEqual(0, day1.BatchesBaked, "leg A bakes nothing with no flour on hand");
                Assert.IsTrue(day1.Refusal != null && day1.Refusal.Contains("flour"),
                    "leg A refuses on flour; refusal: " + day1.Refusal);
                Assert.AreEqual(2, day1.MillbrookReorderSacks, "leg A orders the 12-lb shortfall as 2 sacks");
                Assert.IsTrue(!string.IsNullOrWhiteSpace(day1.MillbrookReorderLotId),
                    "leg A places the 2-sack reorder");
                List<string> lines = CoverageLinesForDay(s, diagBefore, 1);
                Assert.AreEqual(1, lines.Count, "leg A: exactly one coverage line on day 1");
                string line = lines[0];
                Assert.IsTrue(line.Contains("bin flour 0 lb"), "leg A line carries bin flour 0 lb; line: " + line);
                Assert.IsTrue(line.Contains("flour in custody 0 lb"), "leg A line carries flour in custody 0 lb; line: " + line);
                Assert.IsTrue(line.Contains("Millbrook in transit: 2 sack(s) (20 lb)"),
                    "leg A line carries the 2 outstanding Millbrook sacks / 20 lb; line: " + line);
                Assert.IsTrue(line.Contains(day1.MillbrookReorderLotId),
                    "leg A line names the outstanding reorder lot; line: " + line);
                Assert.IsTrue(line.Contains("ordered day 1"),
                    "leg A line carries the reorder's order day; line: " + line);
                Assert.IsTrue(line.Contains("arrival unscheduled (freight pipeline)"),
                    "leg A line states the flour arrival is unscheduled instead of inventing a day; line: " + line);
                Assert.IsTrue(line.Contains("notions on hand 1u"), "leg A line carries notions on hand 1u; line: " + line);
                Assert.IsTrue(line.Contains("notions in flight: none in flight"),
                    "leg A line carries no notions in flight (the seeded notion covers the need, no order placed); line: " + line);
                Assert.IsTrue(line.Contains("next arrival day none in flight"),
                    "leg A line carries no scheduled next arrival day (flour arrival is unscheduled, notions none in flight); line: " + line);
                Assert.IsTrue(line.Contains("first failing input: flour"),
                    "leg A line names flour as the first failing input; line: " + line);
                Assert.IsTrue(line.Contains("producible: no (flour)"),
                    "leg A line carries producible: no (flour) from the Precheck outcome; line: " + line);
                AssertCoverageLineNeverLabelsPlannedYieldAsStock(line);
            }

            // Leg B — nothing in flight anywhere: no Millbrook supplier,
            // notions wired but the seeded notion covers the need so no
            // order is placed. Both transit fields read "none in flight".
            {
                Slice s = NewSlice();
                Series2CounterpartyRegistration reg = ProductionRegistration();
                Assert.IsNull(reg.RegisterInto(s.Market, s.Diag), "production counterparty registration should succeed");
                SeedInputs(s, 0, 1, 40);
                BookAccount(s, 24, 1, 2, 2);

                int diagBefore = s.Diag.Count;
                BakeryWholesaleDailyResult r = RunDay(s, reg, 2);
                Assert.AreEqual(0, r.BatchesBaked, "leg B bakes nothing with no flour on hand");
                Assert.IsTrue(r.Refusal != null && r.Refusal.Contains("flour"),
                    "leg B refuses on flour; refusal: " + r.Refusal);
                Assert.IsTrue(string.IsNullOrWhiteSpace(r.MillbrookReorderLotId),
                    "leg B places no Millbrook reorder (no supplier wired); lot id: " + r.MillbrookReorderLotId);
                List<string> lines = CoverageLinesForDay(s, diagBefore, 2);
                Assert.AreEqual(1, lines.Count, "leg B: exactly one coverage line on day 2");
                string line = lines[0];
                Assert.IsTrue(line.Contains("bin flour 0 lb"), "leg B line carries bin flour 0 lb; line: " + line);
                Assert.IsTrue(line.Contains("Millbrook in transit: none in flight"),
                    "leg B line carries no flour in flight; line: " + line);
                Assert.IsTrue(line.Contains("notions in flight: none in flight"),
                    "leg B line carries no notions in flight; line: " + line);
                Assert.IsTrue(line.Contains("next arrival day none in flight"),
                    "leg B line carries next arrival day none in flight; line: " + line);
                Assert.IsTrue(line.Contains("first failing input: flour"),
                    "leg B line names flour as the first failing input; line: " + line);
                Assert.IsTrue(line.Contains("producible: no (flour)"),
                    "leg B line carries producible: no (flour) from the Precheck outcome; line: " + line);
                AssertCoverageLineNeverLabelsPlannedYieldAsStock(line);
            }
        }

        // ---- Priority 3 Slice 4 (2026-11-08): parameterized forward
        // ordering (mechanism only; live activation BLOCKED on B-1) --
        //
        // FORMULA CORRECTION (coordinator flag, resolved by the test
        // expectation, not by inventing policy): read literally, the
        // plan's formulas give zero orders at depth 0 — max(0, 0 x
        // batchesPerDay - onHand - ...) = 0 — which contradicts the
        // depth-0 test expectation that depth 0 reproduces today's
        // 14-on / 14-off cycle exactly. The test expectation governs:
        // depth 0 MUST reproduce today's ordering behaviour exactly
        // (today's shortfall computation unchanged, same guards).
        // Implemented depth therefore means: depth 0 = today's
        // behaviour (order today's post-feed shortfall, notions; order
        // today's shortfall rounded up to whole sacks, flour); depth
        // D > 0 adds forward cover on top of today's shortfall, to a
        // total target of (D + 1) x one day's demand — today plus D
        // future days — net of on-hand, sellable (notions) and
        // in-transit arriving within the depth window. The production
        // default is 0 (current behaviour) until the owner names the
        // policy (B-1); no nonzero depth is wired in production by
        // this slice. The depth is a parameter, not a value: no price,
        // rate, quantity, or package size is invented here.
        //
        // DRIVER-SIDE CUSTODY NOTE (coordinator flag, Slice 2 scope):
        // the daily driver's flour procurement block still computes
        // its shortfall from bin stock only. That is preserved
        // exactly at depth 0, and the depth > 0 computation keeps the
        // same bin-only basis: it subtracts bin on-hand and
        // outstanding in-transit Millbrook lb, and does NOT subtract
        // flour already dispensed into a batch's custody (Slice 2's
        // custody fix is Precheck-only). Driver-side custody
        // awareness is a separate follow-up, not silently fixed here.
        private static Series2CounterpartyRegistration ProductionRegistrationWithSupply(int supplyUnits)
        {
            Series2CounterpartyRegistration reg = ProductionRegistration();
            reg.SupplyUnits[BakeryFlourSupply.NotionsMaterialId] = supplyUnits;
            return reg;
        }

        private static int TotalNotionsOrderedUnits(Slice s)
        {
            int total = 0;
            if (s.StoreService.Orders != null)
                foreach (NamedSkuOrder order in s.StoreService.Orders)
                    if (order != null) total += Math.Max(0, order.UnitsOrdered);
            return total;
        }

        // Notions cover visible before a day's RunDay: bin on hand +
        // sellable store lots + unreceived orders arriving within the
        // depth window (arrival day in [day, day + depth]). An order
        // arriving today still counts: step (a) receives it before the
        // feed, so it covers today.
        private static int NotionsCoverBeforeDay(Slice s, int day, int depth)
        {
            int cover = s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.NotionsItemId);
            cover += Math.Max(0, s.Notions.Procurement.SellableUnits(BakeryFlourSupply.NotionsMaterialId));
            if (s.StoreService.Orders != null)
                foreach (NamedSkuOrder order in s.StoreService.Orders)
                {
                    if (order == null) continue;
                    if (!string.Equals(order.SkuId, BakeryFlourSupply.NotionsMaterialId, StringComparison.Ordinal)) continue;
                    if (order.Stage == NamedSkuOrderStage.Received || order.Stage == NamedSkuOrderStage.Cancelled) continue;
                    if (order.ExpectedArrivalDayIndex < day || order.ExpectedArrivalDayIndex > day + depth) continue;
                    cover += Math.Max(0, order.UnitsOrdered);
                }
            return cover;
        }

        private static int OutstandingMillbrookLb(OffMapAidBusinessRuntime supplier)
        {
            int total = 0;
            if (supplier == null || supplier.Lots == null) return 0;
            foreach (OffMapAidCargoLot lot in supplier.Lots)
            {
                if (lot == null) continue;
                if (lot.Received) continue;
                if (!string.Equals(lot.BuyerBusinessId, BakeryInstanceId, StringComparison.Ordinal)) continue;
                total += Math.Max(0, lot.InventoryUnits);
            }
            return total;
        }

        [Test]
        public void ForwardOrder_Notions_DepthZero_PreservesCurrentCycle()
        {
            // Priority 3 Slice 4: depth 0 reproduces the 14-on /
            // 14-off cycle exactly — the mechanism, unactivated,
            // changes nothing. Same 60-day drive as Slice 1's
            // NotionsPipeline_SixtyDayCycle_TwoFullPeriods, with the
            // depth parameter explicitly 0. Passes before the mechanism
            // lands (it is today's behaviour) and must keep passing
            // after it.
            Slice s = NewSlice();
            Series2CounterpartyRegistration reg = ProductionRegistration();
            Assert.IsNull(reg.RegisterInto(s.Market, s.Diag), "production counterparty registration should succeed");
            SeedInputs(s, 0, 0, 40);
            BookAccount(s, 24, 1, 2, 2);

            for (int day = 1; day <= 14; day++)
            {
                SeedInputs(s, 12, 0, 0);
                BakeryWholesaleDailyResult r = RunDay(s, reg, day, notionsDepth: 0);
                Assert.AreEqual(1, r.NotionsOrdered, $"depth 0: one priming notion ordered, day {day}");
                Assert.AreEqual(0, r.BatchesBaked, $"depth 0: nothing bakes before the first arrival, day {day}");
            }
            for (int day = 15; day <= 28; day++)
            {
                SeedInputs(s, 12, 0, 0);
                BakeryWholesaleDailyResult r = RunDay(s, reg, day, notionsDepth: 0);
                Assert.AreEqual(1, r.BatchesBaked, $"depth 0: the primed pipeline bakes daily, day {day}");
                Assert.AreEqual(0, r.NotionsOrdered, $"depth 0: no replacement order on an arrival day, day {day}");
            }
            for (int day = 29; day <= 42; day++)
            {
                SeedInputs(s, 12, 0, 0);
                BakeryWholesaleDailyResult r = RunDay(s, reg, day, notionsDepth: 0);
                Assert.AreEqual(0, r.BatchesBaked, $"depth 0: no bake once the primed window drains, day {day}");
                Assert.AreEqual(1, r.NotionsOrdered, $"depth 0: orders resume 1/day on stall days, day {day}");
            }
            for (int day = 43; day <= 56; day++)
            {
                SeedInputs(s, 12, 0, 0);
                BakeryWholesaleDailyResult r = RunDay(s, reg, day, notionsDepth: 0);
                Assert.AreEqual(1, r.BatchesBaked, $"depth 0: the second pipeline window bakes daily, day {day}");
                Assert.AreEqual(0, r.NotionsOrdered, $"depth 0: no replacement order on an arrival day, day {day}");
            }
            for (int day = 57; day <= 60; day++)
            {
                SeedInputs(s, 12, 0, 0);
                BakeryWholesaleDailyResult r = RunDay(s, reg, day, notionsDepth: 0);
                Assert.AreEqual(0, r.BatchesBaked, $"depth 0: the stall resumes once the second window drains, day {day}");
            }
            Assert.AreEqual(30, s.StoreService.Orders.Count,
                "depth 0: 30 orders total (14 priming + 14 first-stall + 2 second-stall), as in Slice 1");
            Assert.AreEqual(28, s.Bakery.WholesaleDeliveries.Count, "depth 0: 28 daily bakes = 28 deliveries");
            Assert.AreEqual(3064, s.BakeryRuntime.CurrentCashCents,
                "depth 0: bakery 2,000 - 280c notions feeds + 1,344c wholesale, as in Slice 1");
            Assert.AreEqual(3656, s.StoreRuntime.CurrentCashCents,
                "depth 0: store 5,000 - 280c receipts + 280c feeds - 1,344c wholesale, as in Slice 1");
            Assert.AreEqual(12, s.Bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId),
                "depth 0: fuel 40 - 28 bakes = 12u left");
        }

        [Test]
        public void ForwardOrder_Notions_DepthFourteen_SustainsDailyBakes()
        {
            // Priority 3 Slice 4: with pipeline depth 14
            // (caller-supplied — one full 14-day transit of forward
            // cover on top of today's need), the notions leg sustains
            // a daily bake from the first arrival through day 60 with
            // no stall. Flour is test-seeded 12 lb/day so only the
            // notions leg can block; fuel is seeded to 60u (46 bakes).
            //
            // Fixture counterparty note: the production registration
            // carries the authored 30u finite supply, and a sustained
            // 60-day run orders 60u — so the fixture seeds the
            // counterparty to 100u, exactly as Slice 5 seeds it to 60u
            // for its acceptance. The 30u production cap itself is
            // untouched and is Slice 6's pin, not this slice's.
            //
            // Expected shape (target = (14 + 1) x 1u = 15u of cover):
            // day 1 orders 15u in one order (today + 14 forward);
            // days 2-14 order nothing (15u in transit covers); from
            // day 15 the pipeline feeds one bake a day and orders one
            // replacement a day, except day 15 itself, whose 15u
            // arrival already covers the target after the feed. Total
            // ordered = 15 + 45 = 60u = 46 baked + 14u steady-state
            // pipeline stock (in transit at day 60).
            // Fixture store-cap note: 46 bakes stock 46 x 24 = 1,104
            // loaves, past the shared fixture's 1,000u staple_food
            // target - so this test raises the store target to 2,000u.
            // The cap is a fixture artefact, not the mechanism under
            // test; the transfer would otherwise refuse on a full
            // category from the 42nd bake.
            Slice s = NewSlice(storeTarget: 2000);
            Series2CounterpartyRegistration reg = ProductionRegistrationWithSupply(100);
            Assert.IsNull(reg.RegisterInto(s.Market, s.Diag), "counterparty registration should succeed");
            SeedInputs(s, 0, 0, 60);
            BookAccount(s, 24, 1, 2, 2);

            int ordersBefore;
            for (int day = 1; day <= 60; day++)
            {
                SeedInputs(s, 12, 0, 0);
                int coverBefore = NotionsCoverBeforeDay(s, day, 14);
                ordersBefore = s.StoreService.Orders.Count;
                BakeryWholesaleDailyResult r = RunDay(s, reg, day, notionsDepth: 14);
                int ordersPlacedToday = s.StoreService.Orders.Count - ordersBefore;
                Assert.IsTrue(ordersPlacedToday <= 1,
                    $"at most one notions order per day (exactly-once ordering), day {day}: {ordersPlacedToday} placed");
                if (coverBefore >= 15)
                    Assert.AreEqual(0, r.NotionsOrdered,
                        $"no order on a day whose cover already meets the 15u target (cover {coverBefore}), day {day}");
                if (day <= 14)
                {
                    Assert.AreEqual(0, r.BatchesBaked, $"nothing bakes before the first arrival, day {day}");
                    Assert.IsTrue(r.Refusal != null && r.Refusal.Contains("notions"),
                        $"day {day} refuses on notions; refusal: {r.Refusal}");
                }
                else
                {
                    Assert.AreEqual(1, r.BatchesBaked, $"depth 14 sustains a daily bake, day {day}");
                    Assert.AreEqual(1, r.TransfersCompleted, $"transfer completes, day {day}");
                }
            }

            Assert.AreEqual(15, s.StoreService.Orders[0].UnitsOrdered,
                "day 1 orders today + 14 forward = 15u in one order");
            Assert.AreEqual(15, s.StoreService.Orders[0].ExpectedArrivalDayIndex,
                "the day-1 order arrives day 15 (14-day lag)");
            for (int i = 1; i < s.StoreService.Orders.Count; i++)
            {
                Assert.AreEqual(1, s.StoreService.Orders[i].UnitsOrdered,
                    $"replacement order {i + 1} is 1u");
                Assert.AreEqual(s.StoreService.Orders[i].OrderDayIndex + 14, s.StoreService.Orders[i].ExpectedArrivalDayIndex,
                    $"replacement order {i + 1} arrives 14 days after its order day");
            }
            Assert.AreEqual(46, s.StoreService.Orders.Count,
                "46 orders over the run: 1 bulk priming order + 45 daily replacements (day 15 orders nothing)");
            Assert.AreEqual(60, TotalNotionsOrderedUnits(s),
                "total ordered 60u = 46 baked + 14u steady-state pipeline stock, never more");
            Assert.AreEqual(46, s.Bakery.WholesaleDeliveries.Count, "46 daily bakes = 46 deliveries");
            Assert.AreEqual(14, s.Bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId),
                "fuel 60 - 46 bakes = 14u left: fuel never blocks this run");
            Assert.AreEqual(3748, s.BakeryRuntime.CurrentCashCents,
                "bakery 2,000 - 460c notions feeds (46 x 10c) + 2,208c wholesale (46 x 48c)");
            Assert.AreEqual(2792, s.StoreRuntime.CurrentCashCents,
                "store 5,000 - 460c receipts + 460c feeds - 2,208c wholesale");
            Assert.AreEqual(600, s.Market.TotalOutflowCents, "60 orders x 10c booked as market outflow at order time");
            int inTransitAtEnd = 0;
            foreach (NamedSkuOrder order in s.StoreService.Orders)
                if (order != null && order.Stage != NamedSkuOrderStage.Received && order.Stage != NamedSkuOrderStage.Cancelled)
                    inTransitAtEnd += Math.Max(0, order.UnitsOrdered);
            Assert.AreEqual(14, inTransitAtEnd,
                "steady-state pipeline stock at day 60 is 14u in transit (orders days 47-60, arriving days 61-74)");
        }

        [Test]
        public void ForwardOrder_Flour_DepthCoversFreightLead_SustainsDailyBakes()
        {
            // Priority 3 Slice 4b: with the flour depth set to cover
            // the modelled freight lead, the flour leg sustains a
            // daily bake once the pipeline primes, through day 60.
            // The modelled lead is 7 days (Set B: pickup ~day+3,
            // receipt ~day+7); the depth is 14 — one lead of cover
            // plus one lead of buffer — because depth equal to the
            // lead alone does NOT sustain daily bakes under the
            // existing single-outstanding-order guard: the guard
            // refuses a top-up while an outstanding lot covers the
            // requested sacks, so with depth 7 the bin drains between
            // lumpy sack arrivals and stalls (verified in a scratch
            // run at depth 7: bakes stall on days 17 and 20-21). That
            // is a policy finding for B-1 (the depth must exceed the
            // lead, or the guard needs revisiting in a later slice),
            // not a defect in the mechanism: the mechanism orders
            // exactly the forward target net of on-hand and
            // in-transit, and the guard still refuses duplicates. No
            // value is invented here — both the lead (7) and the depth
            // (14) are caller-supplied test values.
            //
            // REPAIR (Slice 4b, partial-fill ordering): Slice 4
            // pinned a stall from day 57 — daily bakes days 8-56
            // (49 bakes), then the drained pipeline's 18-sack forward
            // request met the all-or-nothing insufficient-stock guard
            // while Millbrook held only 16 sacks ("Millbrook holds
            // 16 sack(s); order requires 18"), so the driver ordered
            // 0 instead of the 16 available and no bake was possible
            // days 57-60. MillbrookFlourReorder.PlaceReorder now
            // clamps instead of refusing: when 0 < available <
            // requested it orders the available sacks (one lot, one
            // debit at the authored 48c/sack) and logs the clamp.
            // The re-simulation (plan 03b §3(i)) predicts the repair
            // sustains daily bakes days 8-60 (53 bakes) with clamped
            // orders on days 51 and 56; the test run governs — the
            // clamp days below are confirmed from the run, and the
            // day-60 snapshot (bin 34 lb, in transit 80 lb, Millbrook
            // 0 lb, 75 sacks, bakery 944c) is the repaired
            // reconciliation. Total supply is 230 lb start + 520 lb
            // deterministic weekly replenishment over weeks 1-8 =
            // 750 lb, fully ordered by day 60: the run ends at genuine
            // H-B4 exhaustion, not a guard stall.
            //
            // Notions are test-seeded 1u/day and the notions step is
            // unwired, so only the flour leg can block; fuel is seeded
            // to 60u. The store holds 0 sacks, so all flour comes
            // through Millbrook; lots are received through the real
            // receipt seam exactly 7 days after their order day.
            // Store target raised to 2,000u: 53 bakes x 24 loaves =
            // 1,272u past the shared fixture's 1,000u staple_food cap.
            Slice s = NewSlice(storeTarget: 2000);
            // Authored Millbrook production (plan section 2 H-B4):
            // 40-80 lb/week toward a 460-lb target reserve. The shared
            // MillbrookDefinition() carries inventory only, so this
            // test adds the production row and applies the weekly
            // replenishment each day; without it the 230-lb starting
            // stock alone cannot cover 53 bakes (636 lb) and the
            // supplier runs dry around day 27.
            OffMapAidBusinessDefinition millbrookDef = MillbrookDefinition();
            millbrookDef.Production.Add(new OffMapAidProductionRow
            {
                ItemId = "bakery_flour_sack",
                WeeklyMinimumUnits = 40,
                WeeklyMaximumUnits = 80,
                TargetReserveUnits = 460,
            });
            var supplier = new OffMapAidBusinessRuntime(millbrookDef);
            var adapter = new MillbrookFlourFreightCustodyAdapter(supplier, s.Bakery, s.Ids);
            SeedInputs(s, 0, 1, 60);
            BookAccount(s, 24, 1, 2, 1);

            const int leadDays = 7;
            const int flourDepth = 14;
            int firstBakeDay = 0;
            int firstStallDay = 0;
            int bakes = 0;
            int totalReorderSacks = 0;
            int totalReplenishedLb = 0;
            int maxConsecutiveEightLbDays = 0;
            int consecutiveEightLbDays = 0;
            var clampDays = new List<int>();
            string firstStallDayDiag = string.Empty;
            for (int day = 1; day <= 60; day++)
            {
                SeedInputs(s, 0, 1, 0);
                var due = new List<OffMapAidCargoLot>();
                foreach (OffMapAidCargoLot lot in supplier.Lots)
                    if (lot != null && !lot.Received && lot.CreatedDayIndex + leadDays <= day)
                        due.Add(lot);
                foreach (OffMapAidCargoLot lot in due)
                    ReceiveMillbrookLot(s, adapter, lot, day);

                int millbrookBefore = supplier.GetInventoryUnits("bakery_flour_sack");
                supplier.ApplyWeeklyReplenishment(day);
                totalReplenishedLb += Math.Max(0, supplier.GetInventoryUnits("bakery_flour_sack") - millbrookBefore);

                int lotsBefore = supplier.Lots.Count;
                int diagBefore = s.Diag.Count;
                BakeryWholesaleDailyResult r = RunDay(s, null, day, wireNotions: false,
                    millbrookSupplier: supplier, flourDepth: flourDepth);
                Assert.IsTrue(supplier.Lots.Count - lotsBefore <= 1,
                    $"at most one Millbrook reorder lot per day, day {day}");
                totalReorderSacks += r.MillbrookReorderSacks;
                string dayDiag = string.Join(" | ", s.Diag.GetRange(diagBefore, s.Diag.Count - diagBefore));
                if (dayDiag.Contains("clamped"))
                    clampDays.Add(day);
                int binLb = s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId);
                if (r.BatchesBaked == 1)
                {
                    if (firstBakeDay == 0) firstBakeDay = day;
                    Assert.AreEqual(0, firstStallDay,
                        $"no bake gap before day {day}: bakes ran daily from day {firstBakeDay}, then stalled day {firstStallDay}");
                    bakes++;
                    Assert.AreEqual(1, r.TransfersCompleted, $"transfer completes on a bake day, day {day}");
                }
                else if (firstBakeDay > 0 && firstStallDay == 0)
                {
                    firstStallDay = day;
                    firstStallDayDiag = dayDiag;
                    Assert.IsTrue(r.Refusal != null && r.Refusal.Contains("flour"),
                        $"the first stall day refuses on flour; refusal: {r.Refusal}");
                }
                if (binLb == 8)
                {
                    consecutiveEightLbDays++;
                    if (consecutiveEightLbDays > maxConsecutiveEightLbDays)
                        maxConsecutiveEightLbDays = consecutiveEightLbDays;
                }
                else
                {
                    consecutiveEightLbDays = 0;
                }
            }

            Assert.AreEqual(8, firstBakeDay,
                "the first bake is day 8: the day-1 order is received exactly one 7-day lead later");
            Assert.AreEqual(0, firstStallDay,
                "no stall through day 60: daily bakes run days 8-60 with no gap; first stall day diag: " + firstStallDayDiag);
            Assert.AreEqual(53, bakes, "53 daily bakes, days 8-60 with no gap, after the partial-fill repair");
            Assert.AreEqual(53, s.Bakery.WholesaleDeliveries.Count, "53 daily bakes = 53 deliveries");
            Assert.AreEqual(520, totalReplenishedLb,
                "the deterministic weekly replenishment totals 520 lb over weeks 1-8 (40-80 lb/week, authored range)");
            Assert.AreEqual(0, supplier.GetInventoryUnits("bakery_flour_sack"),
                "Millbrook holds 0 lb at the end of the run: the total authored supply (230 + 520 = 750 lb) is fully ordered — genuine H-B4 exhaustion, not a guard stall");
            Assert.IsTrue(clampDays.Count >= 1,
                "at least one clamped order occurred: the partial-fill repair fired instead of refusing; clamp days: "
                + string.Join(",", clampDays));
            Assert.IsTrue(clampDays.Contains(51) && clampDays.Contains(56),
                "clamped orders fire on days 51 and 56 (re-simulation prediction, confirmed by the run); clamp days: "
                + string.Join(",", clampDays));
            Assert.AreEqual(75, totalReorderSacks,
                "75 sacks ordered over the run (10 x 75 = 750 lb = total authored supply)");
            Assert.AreEqual(34, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId),
                "the bin rests at 34 lb at the end");
            Assert.AreEqual(80, OutstandingMillbrookLb(supplier),
                "80 lb is in transit at the end of the run");
            Assert.AreEqual(10 * totalReorderSacks, 12 * bakes
                + s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId) + OutstandingMillbrookLb(supplier),
                "flour reconciles exactly: ordered pounds = baked pounds + bin + in transit (no flour created or lost)");
            Assert.IsTrue(maxConsecutiveEightLbDays <= leadDays,
                $"the bin never rests at 8 lb for more than the {leadDays}-day lead window (max run {maxConsecutiveEightLbDays} days)");
            Assert.AreEqual(48 * totalReorderSacks, supplier.CashCents,
                "Millbrook is paid exactly 48c per reorder sack, once each (exactly-once settlement)");
            Assert.AreEqual(230 + totalReplenishedLb - 10 * totalReorderSacks, supplier.GetInventoryUnits("bakery_flour_sack"),
                "Millbrook stock reconciles: 230 start + weekly replenishment - reordered pounds");
            Assert.AreEqual(944, s.BakeryRuntime.CurrentCashCents,
                "bakery cash reconciles: 2,000 - 48c per reorder sack (75 x 48) + 48c per wholesale transfer (53 x 48) = 944c");
            Assert.AreEqual(2000 - 48 * totalReorderSacks + 48 * bakes, s.BakeryRuntime.CurrentCashCents,
                "bakery cash reconciles: 2,000 - 48c per reorder sack + 48c per wholesale transfer");
        }

        [Test]
        public void ForwardOrder_NeverOrdersBeyondCoveredDemand()
        {
            // Priority 3 Slice 4: with depth N, total ordered units
            // over the run equal total baked demand plus the
            // steady-state pipeline stock, never more; and no order is
            // placed on a day whose in-transit + on-hand already
            // covers depth N. Depth N = 21 here (greater than the
            // 14-day transit, so in-transit units always arrive
            // within the depth window and the windowed cover check is
            // exact): target = (21 + 1) x 1u = 22u. Day 1 orders 22u
            // in one order; days 2-14 order nothing (22u in transit
            // covers); from day 15 the pipeline feeds one bake a day
            // and orders one replacement a day, except day 15 itself.
            // Total ordered = 22 + 45 = 67u = 46 baked + 21u
            // steady-state pipeline stock (7u sellable + 14u in
            // transit at day 60). Fixture counterparty seeded to
            // 100u, as in the depth-14 test; fuel seeded to 60u.
            // Store target raised to 2,000u as in the depth-14 test
            // (46 bakes x 24 loaves = 1,104u past the 1,000u cap).
            Slice s = NewSlice(storeTarget: 2000);
            Series2CounterpartyRegistration reg = ProductionRegistrationWithSupply(100);
            Assert.IsNull(reg.RegisterInto(s.Market, s.Diag), "counterparty registration should succeed");
            SeedInputs(s, 0, 0, 60);
            BookAccount(s, 24, 1, 2, 2);

            const int depth = 21;
            int target = depth + 1;
            int ordersBefore;
            for (int day = 1; day <= 60; day++)
            {
                SeedInputs(s, 12, 0, 0);
                int coverBefore = NotionsCoverBeforeDay(s, day, depth);
                ordersBefore = s.StoreService.Orders.Count;
                BakeryWholesaleDailyResult r = RunDay(s, reg, day, notionsDepth: depth);
                Assert.IsTrue(s.StoreService.Orders.Count - ordersBefore <= 1,
                    $"at most one notions order per day, day {day}");
                if (coverBefore >= target)
                    Assert.AreEqual(0, r.NotionsOrdered,
                        $"no order on a day whose in-transit + on-hand already covers depth {depth} " +
                        $"(cover {coverBefore} >= target {target}), day {day}");
                if (day <= 14)
                {
                    Assert.AreEqual(0, r.BatchesBaked, $"nothing bakes before the first arrival, day {day}");
                }
                else
                {
                    Assert.AreEqual(1, r.BatchesBaked, $"depth {depth} sustains a daily bake, day {day}");
                }
            }

            Assert.AreEqual(22, s.StoreService.Orders[0].UnitsOrdered,
                "day 1 orders today + 21 forward = 22u in one order");
            Assert.AreEqual(67, TotalNotionsOrderedUnits(s),
                "total ordered 67u = 46 baked demand + 21u steady-state pipeline stock, never more");
            Assert.AreEqual(46, s.Bakery.WholesaleDeliveries.Count, "46 daily bakes = 46 deliveries");
            int pipelineAtEnd = s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.NotionsItemId)
                + Math.Max(0, s.Notions.Procurement.SellableUnits(BakeryFlourSupply.NotionsMaterialId));
            foreach (NamedSkuOrder order in s.StoreService.Orders)
                if (order != null && order.Stage != NamedSkuOrderStage.Received && order.Stage != NamedSkuOrderStage.Cancelled)
                    pipelineAtEnd += Math.Max(0, order.UnitsOrdered);
            Assert.AreEqual(21, pipelineAtEnd,
                "steady-state pipeline stock at day 60 is 21u = depth (sellable + in transit + bin)");
            Assert.AreEqual(46 + pipelineAtEnd, TotalNotionsOrderedUnits(s),
                "total ordered equals baked demand plus steady-state pipeline stock, exactly");
        }

        // ---- Priority 3 Slice 5 (2026-11-08): 60-day sustained-production acceptance ----
        // Tests only; no production code. The combined drive runs BOTH
        // supply legs through the real BakeryWholesaleDailyDriver with
        // the Slice 4 depth parameters set (notions depth 14, flour
        // depth 14 covering the modelled 7-day freight lead), fuel
        // seeded to 60u in the fixture, notions counterparty seeded to
        // 60u in the fixture, and the Millbrook definition as authored
        // (230 lb start, weekly replenishment 40-80 lb toward 460 lb).
        // The fixture seeds fuel and notions supply precisely because
        // the live caps (40 fuel units, 30-unit notions counterparty)
        // stop a live 60-day run at ~day 40 (fuel) and ~day 30 of
        // ordering (notions): this acceptance proves the machinery in
        // source, and the live acceptance is BLOCKED on plan §8 B-2
        // (fuel) and B-3 (notions standing terms) plus B-1 (depth).
        // Slice 5 must NOT be presented as live sustained production.
        //
        // Flour-leg anchor: the Slice 4b flour-only drive (notions
        // test-seeded, first bake day 8) pins 53 bakes days 8-60,
        // Millbrook 0 lb, bin 34 lb, in transit 80 lb, 75 sacks, bakery
        // 944c (ForwardOrder_Flour_DepthCoversFreightLead_SustainsDailyBakes).
        // The combined drive's first bake is day 15 instead (gated by
        // the first notions arrival), so it bakes 46 times and orders
        // less flour — Millbrook does not exhaust here — but the flour
        // leg must satisfy the SAME reconciliation identities (ordered
        // = baked + bin + in transit; Millbrook 230 + replenished -
        // ordered; bakery cash per the per-leg formula).
        private sealed class CombinedRun
        {
            public Slice S;
            public Series2CounterpartyRegistration Reg;
            public OffMapAidBusinessRuntime Supplier;
            public MillbrookFlourFreightCustodyAdapter Adapter;
            public int TotalReplenishedLb;
        }

        private static OffMapAidBusinessDefinition MillbrookDefinitionWithProduction()
        {
            // Authored Millbrook production (plan §2 H-B4): 40-80 lb/week
            // toward a 460-lb target reserve. The shared
            // MillbrookDefinition() carries inventory only, so the
            // combined drive adds the production row and applies the
            // weekly replenishment each day (same shape as the Slice 4
            // flour test); without it the 230-lb starting stock alone
            // cannot cover the run and the supplier runs dry.
            OffMapAidBusinessDefinition millbrookDef = MillbrookDefinition();
            millbrookDef.Production.Add(new OffMapAidProductionRow
            {
                ItemId = "bakery_flour_sack",
                WeeklyMinimumUnits = 40,
                WeeklyMaximumUnits = 80,
                TargetReserveUnits = 460,
            });
            return millbrookDef;
        }

        private static CombinedRun NewCombinedRun()
        {
            Slice s = NewSlice(storeTarget: 2000);
            Series2CounterpartyRegistration reg = ProductionRegistrationWithSupply(60);
            Assert.IsNull(reg.RegisterInto(s.Market, s.Diag), "counterparty registration should succeed");
            SeedInputs(s, 0, 0, 60);
            BookAccount(s, 24, 1, 2, 2);
            var supplier = new OffMapAidBusinessRuntime(MillbrookDefinitionWithProduction());
            var adapter = new MillbrookFlourFreightCustodyAdapter(supplier, s.Bakery, s.Ids);
            return new CombinedRun { S = s, Reg = reg, Supplier = supplier, Adapter = adapter };
        }

        // One combined day: receive Millbrook lots due under the
        // modelled 7-day lead, apply the authored weekly replenishment,
        // then drive the real daily driver with both depths set.
        private static BakeryWholesaleDailyResult DriveCombinedDay(CombinedRun run, int day)
        {
            Slice s = run.S;
            const int leadDays = 7;
            var due = new List<OffMapAidCargoLot>();
            foreach (OffMapAidCargoLot lot in run.Supplier.Lots)
                if (lot != null && !lot.Received && lot.CreatedDayIndex + leadDays <= day)
                    due.Add(lot);
            foreach (OffMapAidCargoLot lot in due)
                ReceiveMillbrookLot(s, run.Adapter, lot, day);

            int millbrookBefore = run.Supplier.GetInventoryUnits("bakery_flour_sack");
            run.Supplier.ApplyWeeklyReplenishment(day);
            run.TotalReplenishedLb += Math.Max(0, run.Supplier.GetInventoryUnits("bakery_flour_sack") - millbrookBefore);

            return RunDay(s, run.Reg, day, millbrookSupplier: run.Supplier, notionsDepth: 14, flourDepth: 14);
        }

        // One-line totals snapshot for cross-run comparison (Slice 5
        // tests 2-3): every figure the combined drive must reproduce.
        private static string TotalsSummary(CombinedRun run, int bakes, int transfers, int flourSacks)
        {
            Slice s = run.S;
            int inTransitNotions = 0;
            foreach (NamedSkuOrder order in s.StoreService.Orders)
                if (order != null && order.Stage != NamedSkuOrderStage.Received && order.Stage != NamedSkuOrderStage.Cancelled)
                    inTransitNotions += Math.Max(0, order.UnitsOrdered);
            return string.Format(
                "bakes={0} transfers={1} deliveries={2} notionsOrders={3} notionsUnits={4} notionsInTransit={5} notionsBin={6} notionsSellable={7} flourSacks={8} flourLots={9} binFlour={10} inTransitFlour={11} millbrookStock={12} millbrookCash={13} replenished={14} bakeryCash={15} storeCash={16} marketOutflow={17} fuelLeft={18} storeStock={19}",
                bakes, transfers, s.Bakery.WholesaleDeliveries.Count,
                s.StoreService.Orders.Count, TotalNotionsOrderedUnits(s), inTransitNotions,
                s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.NotionsItemId),
                s.Notions.Procurement.SellableUnits(BakeryFlourSupply.NotionsMaterialId),
                flourSacks, run.Supplier.Lots.Count,
                s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId),
                OutstandingMillbrookLb(run.Supplier),
                run.Supplier.GetInventoryUnits("bakery_flour_sack"), run.Supplier.CashCents,
                run.TotalReplenishedLb, s.BakeryRuntime.CurrentCashCents, s.StoreRuntime.CurrentCashCents,
                s.Market.TotalOutflowCents, s.Bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId),
                s.StoreRuntime.GetCategoryStock("staple_food").CurrentStockUnits);
        }

        [Test]
        public void Sustained_SixtyDays_DailyBakes_ExactlyOnce_AllLegs()
        {
            // Priority 3 Slice 5: the 60-day sustained-production
            // acceptance. One combined drive through the real
            // BakeryWholesaleDailyDriver with notions depth 14 and
            // flour depth 14, fuel seeded to 60u, notions counterparty
            // seeded to 60u, Millbrook as authored. The first bake is
            // day 15 (gated by the first notions arrival on the 14-day
            // lead); from day 15 through day 60 a bake + transfer must
            // complete every day. Every order id, lot id, receipt id,
            // delivery record, and 48c settlement must appear exactly
            // once; cash must conserve end to end on every leg; store
            // stock must accumulate exactly 24 loaves per transfer
            // (no sale in the fixture).
            //
            // Exact end-state numbers below are the deterministic
            // trajectory of this drive (confirmed by a probe run before
            // pinning): 46 bakes, 66 flour sacks in 9 lots, bin 28 lb,
            // in transit 80 lb, Millbrook 90 lb left, bakery 580c,
            // store 2,792c. They are pinned only alongside the
            // reconciliation identities that must hold for ANY correct
            // run (flour ordered = baked + bin + in transit; Millbrook
            // 230 + replenished - ordered; bakery/store cash per the
            // per-leg formula) — the identities are the check, the
            // numbers pin the trajectory. The Slice 4b flour-only
            // anchor (53 bakes days 8-60, 75 sacks, Millbrook 0 lb,
            // bin 34 lb, in transit 80 lb, bakery 944c) is the same
            // identities on the flour leg in isolation, where the first
            // bake is day 8 because notions are test-seeded there; the
            // combined drive bakes 7 fewer times (days 8-14 are
            // notions-gated), orders 9 fewer sacks, and leaves
            // Millbrook with 90 lb instead of exhausting it.
            CombinedRun run = NewCombinedRun();
            Slice s = run.S;
            var notionsOrderIds = new HashSet<string>();
            var millbrookLotIds = new HashSet<string>();
            var deliveryIds = new HashSet<string>();
            var receiptIds = new HashSet<string>();
            var millbrookTxIds = new HashSet<string>();
            var binLotIds = new HashSet<string>();
            int firstBakeDay = 0;
            int firstStallDay = 0;
            int bakes = 0;
            int transfers = 0;
            int flourSacks = 0;
            int expectedStoreStock = 0;
            for (int day = 1; day <= 60; day++)
            {
                int ordersBefore = s.StoreService.Orders.Count;
                int lotsBefore = run.Supplier.Lots.Count;
                int deliveriesBefore = s.Bakery.WholesaleDeliveries.Count;
                int receiptsBefore = s.StoreService.ReceivedReceiptIds.Count;
                BakeryWholesaleDailyResult r = DriveCombinedDay(run, day);
                Assert.IsTrue(run.Supplier.Lots.Count - lotsBefore <= 1,
                    $"at most one Millbrook reorder lot per day, day {day}");
                Assert.IsTrue(s.StoreService.Orders.Count - ordersBefore <= 1,
                    $"at most one notions order per day, day {day}");
                flourSacks += r.MillbrookReorderSacks;

                for (int i = ordersBefore; i < s.StoreService.Orders.Count; i++)
                {
                    string orderId = s.StoreService.Orders[i].OrderId;
                    Assert.IsTrue(!string.IsNullOrWhiteSpace(orderId), $"notions order {i} carries an id, day {day}");
                    Assert.IsTrue(notionsOrderIds.Add(orderId), $"notions order id '{orderId}' appears exactly once (day {day})");
                }
                if (!string.IsNullOrWhiteSpace(r.MillbrookReorderLotId))
                    Assert.IsTrue(millbrookLotIds.Add(r.MillbrookReorderLotId),
                        $"Millbrook lot id '{r.MillbrookReorderLotId}' appears exactly once (day {day})");
                for (int i = deliveriesBefore; i < s.Bakery.WholesaleDeliveries.Count; i++)
                {
                    BakeryWholesaleDeliveryRecord delivery = s.Bakery.WholesaleDeliveries[i];
                    Assert.IsTrue(deliveryIds.Add(delivery.DeliveryId),
                        $"delivery id '{delivery.DeliveryId}' appears exactly once (day {day})");
                    Assert.AreEqual(24, delivery.Units, $"delivery {i} ships 24 loaves, day {day}");
                    Assert.AreEqual(48, delivery.TotalCents, $"delivery {i} settles 48c, day {day}");
                    Assert.AreEqual(day, delivery.DayIndex, $"delivery {i} is dated its bake day, day {day}");
                }
                for (int i = receiptsBefore; i < s.StoreService.ReceivedReceiptIds.Count; i++)
                {
                    string receiptId = s.StoreService.ReceivedReceiptIds[i];
                    Assert.IsTrue(!string.IsNullOrWhiteSpace(receiptId), $"receipt {i} carries an id, day {day}");
                    Assert.IsTrue(receiptIds.Add(receiptId), $"receipt id '{receiptId}' appears exactly once (day {day})");
                }
                var liveBinLotIds = new HashSet<string>();
                foreach (BakeryFlourLot lot in s.Bakery.FlourStock.Lots)
                {
                    if (lot == null) continue;
                    Assert.IsTrue(liveBinLotIds.Add(lot.LotId.ToString()),
                        $"bin lot id '{lot.LotId}' appears at most once among live bin lots, day {day}");
                    binLotIds.Add(lot.LotId.ToString());
                }

                if (r.BatchesBaked == 1)
                {
                    if (firstBakeDay == 0) firstBakeDay = day;
                    Assert.AreEqual(0, firstStallDay,
                        $"no bake gap before day {day}: bakes ran daily from day {firstBakeDay}, then stalled day {firstStallDay}");
                    bakes++;
                    transfers += r.TransfersCompleted;
                    Assert.AreEqual(1, r.TransfersCompleted, $"transfer completes on a bake day, day {day}");
                    Assert.AreEqual(24, r.UnitsStocked, $"24 loaves stocked on a bake day, day {day}");
                    expectedStoreStock += 24;
                }
                else
                {
                    Assert.AreEqual(0, r.TransfersCompleted, $"no transfer without a bake, day {day}");
                    Assert.AreEqual(0, r.UnitsStocked, $"nothing stocked without a bake, day {day}");
                    if (firstBakeDay > 0 && firstStallDay == 0) firstStallDay = day;
                }
                Assert.AreEqual(expectedStoreStock, s.StoreRuntime.GetCategoryStock("staple_food").CurrentStockUnits,
                    $"store stock after day {day} equals prior stock + 24 per transfer - 0 sold (no sale in the fixture)");
            }

            Assert.AreEqual(15, firstBakeDay, "the first bake is day 15: the day-1 notions order arrives on the 14-day lead");
            Assert.AreEqual(0, firstStallDay, "no stall through day 60: daily bakes run days 15-60 with no gap");
            Assert.AreEqual(46, bakes, "46 daily bakes, days 15-60 with no gap");
            Assert.AreEqual(46, transfers, "46 transfers complete, one per bake");
            Assert.AreEqual(46, s.Bakery.WholesaleDeliveries.Count, "46 daily bakes = 46 delivery records");
            Assert.AreEqual(46, deliveryIds.Count, "46 distinct delivery ids, one per delivery record");

            Assert.AreEqual(46, s.StoreService.Orders.Count, "46 notions orders over the run: 1 bulk priming order + 45 daily replacements (day 15 orders nothing)");
            Assert.AreEqual(46, notionsOrderIds.Count, "46 distinct notions order ids, no duplicate order");
            Assert.AreEqual(60, TotalNotionsOrderedUnits(s), "total notions ordered 60u = 46 baked + 14u steady-state pipeline stock");
            Assert.AreEqual(15, s.StoreService.Orders[0].UnitsOrdered, "day 1 orders today + 14 forward = 15u in one order");
            Assert.AreEqual(15, s.StoreService.Orders[0].ExpectedArrivalDayIndex, "the day-1 notions order arrives day 15 (14-day lag)");
            for (int i = 1; i < s.StoreService.Orders.Count; i++)
            {
                Assert.AreEqual(1, s.StoreService.Orders[i].UnitsOrdered, $"replacement order {i + 1} is 1u");
                Assert.AreEqual(s.StoreService.Orders[i].OrderDayIndex + 14, s.StoreService.Orders[i].ExpectedArrivalDayIndex,
                    $"replacement order {i + 1} arrives 14 days after its order day");
            }
            int inTransitNotions = 0;
            foreach (NamedSkuOrder order in s.StoreService.Orders)
                if (order != null && order.Stage != NamedSkuOrderStage.Received && order.Stage != NamedSkuOrderStage.Cancelled)
                    inTransitNotions += Math.Max(0, order.UnitsOrdered);
            Assert.AreEqual(14, inTransitNotions, "steady-state notions pipeline stock at day 60 is 14u in transit (orders days 47-60, arriving days 61-74)");
            Assert.AreEqual(0, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.NotionsItemId), "bin notions 0u at day 60 (every fed notion bakes the same day)");
            Assert.AreEqual(0, s.Notions.Procurement.SellableUnits(BakeryFlourSupply.NotionsMaterialId), "no notions sit sellable in the store at day 60");
            Assert.AreEqual(32, receiptIds.Count, "32 distinct notions receipt ids, one per received order (day-1 bulk order + 31 replacement orders arrived by day 60; 46u received in total)");
            Assert.AreEqual(32, s.StoreService.Lots.Count, "32 notions lots received by day 60, one per received order");

            Assert.AreEqual(66, flourSacks, "66 flour sacks ordered over the run");
            Assert.AreEqual(9, run.Supplier.Lots.Count, "9 Millbrook reorder lots over the run");
            Assert.AreEqual(9, millbrookLotIds.Count, "9 distinct Millbrook lot ids, no duplicate reorder");
            Assert.AreEqual(28, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId), "the bin rests at 28 lb at the end");
            Assert.AreEqual(80, OutstandingMillbrookLb(run.Supplier), "80 lb is in transit at the end of the run");
            Assert.AreEqual(10 * flourSacks, 12 * bakes
                + s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId) + OutstandingMillbrookLb(run.Supplier),
                "flour reconciles exactly: ordered pounds = baked pounds + bin + in transit (no flour created or lost)");
            Assert.AreEqual(520, run.TotalReplenishedLb,
                "the deterministic weekly replenishment totals 520 lb over weeks 1-8 (40-80 lb/week, authored range)");
            Assert.AreEqual(90, run.Supplier.GetInventoryUnits("bakery_flour_sack"),
                "Millbrook holds 90 lb at the end: 230 start + 520 replenished - 660 ordered (the combined run does not exhaust Millbrook, unlike the flour-only anchor)");
            Assert.AreEqual(230 + run.TotalReplenishedLb - 10 * flourSacks, run.Supplier.GetInventoryUnits("bakery_flour_sack"),
                "Millbrook stock reconciles: 230 start + weekly replenishment - reordered pounds");
            Assert.AreEqual(3168, run.Supplier.CashCents, "Millbrook is paid exactly 48c per reorder sack, once each (66 x 48)");
            Assert.AreEqual(9, run.Supplier.CashTransactions.Count, "one Millbrook cash transaction per reorder lot (9 lots, 9 settlements)");
            int millbrookTxTotal = 0;
            foreach (OffMapAidCashTransaction tx in run.Supplier.CashTransactions)
            {
                Assert.IsTrue(millbrookTxIds.Add(tx.TransactionId), $"Millbrook transaction id '{tx.TransactionId}' appears exactly once");
                Assert.AreEqual(0, tx.AmountCents % 48, $"Millbrook transaction '{tx.TransactionId}' is a whole number of 48c sacks");
                millbrookTxTotal += tx.AmountCents;
            }
            Assert.AreEqual(3168, millbrookTxTotal, "Millbrook settlement transactions sum to exactly 48c per reorder sack");

            Assert.AreEqual(580, s.BakeryRuntime.CurrentCashCents,
                "bakery cash reconciles: 2,000 - 460c notions feeds (46 x 10c) - 3,168c flour (66 x 48c) + 2,208c wholesale (46 x 48c) = 580c");
            Assert.AreEqual(2000 - 10 * bakes - 48 * flourSacks + 48 * bakes, s.BakeryRuntime.CurrentCashCents,
                "bakery cash reconciles from the per-day legs");
            Assert.AreEqual(2792, s.StoreRuntime.CurrentCashCents,
                "store cash reconciles: 5,000 - 460c notions receipts (46u received) + 460c feeds - 2,208c wholesale = 2,792c");
            Assert.AreEqual(5000 - 10 * bakes + 10 * bakes - 48 * bakes, s.StoreRuntime.CurrentCashCents,
                "store cash reconciles from the per-day legs (receipts and feeds cancel at 46u each)");
            Assert.AreEqual(600, s.Market.TotalOutflowCents, "60 notions orders x 10c booked as market outflow at order time");
            Assert.AreEqual(14, s.Bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId),
                "fuel 60 - 46 bakes = 14u left: fuel never blocks this run (fixture-seeded; live fuel caps at 40u, plan §8 B-2)");
            Assert.AreEqual(1104, s.StoreRuntime.GetCategoryStock("staple_food").CurrentStockUnits,
                "store stock 46 bakes x 24 loaves = 1,104u at day 60 (no sale in the fixture)");
            Assert.AreEqual(1104, expectedStoreStock, "store stock accumulated exactly 24 loaves per transfer over the run");
        }

        [Test]
        public void Sustained_SixtyDays_VariedDaySteps_SameOutcome()
        {
            // Priority 3 Slice 5: tick-size independence for the 60-day
            // drive. Run A advances the day loop one day at a time;
            // run B advances the same 60 days in varied chunk sizes
            // (1, 2, 3, 5, 4, ... days per chunk), driving every
            // calendar day exactly once through the real driver in
            // both runs. Both runs must produce identical bake /
            // order / transfer totals and identical end state.
            //
            // Step-size reading, stated plainly so this test is not
            // misread: BakeryWholesaleDailyDriver.RunDay processes
            // exactly one day per call with at most one active batch,
            // so a step that SKIPPED calendar days could not bake for
            // them and its totals would differ by construction — the
            // driver has no multi-day catch-up loop, and this test
            // does not pretend it does. "Varied step sizes" here means
            // the outer loop advances in varied chunk sizes while
            // every day is still driven once; what it pins is that the
            // grouping of the day drive changes nothing. The driver's
            // own catch-up path (a planned batch run on a later day
            // index through Series2BakeryProductionDriver.
            // RunScheduledDay) is pinned directly by the probe below:
            // a batch planned for day 2 bakes exactly once when run on
            // day 5, diagnosed as catch-up. The real OnDayChanged path
            // is harness-excluded (SimulationDrivers.cs), so the
            // sub-day tick variant is owed to Codex (plan §7 C-8).
            CombinedRun runA = NewCombinedRun();
            int bakesA = 0;
            int transfersA = 0;
            int flourSacksA = 0;
            for (int day = 1; day <= 60; day++)
            {
                BakeryWholesaleDailyResult r = DriveCombinedDay(runA, day);
                bakesA += r.BatchesBaked;
                transfersA += r.TransfersCompleted;
                flourSacksA += r.MillbrookReorderSacks;
            }
            string summaryA = TotalsSummary(runA, bakesA, transfersA, flourSacksA);
            Assert.AreEqual(46, bakesA, "run A (1-day steps): 46 daily bakes, days 15-60");

            CombinedRun runB = NewCombinedRun();
            int bakesB = 0;
            int transfersB = 0;
            int flourSacksB = 0;
            int[] chunks = { 1, 2, 3, 5, 4, 2, 3, 1, 5, 4, 2, 3, 1, 5, 2, 4, 3, 2, 1, 5, 2 };
            int dayB = 1;
            foreach (int chunk in chunks)
            {
                for (int i = 0; i < chunk; i++)
                {
                    BakeryWholesaleDailyResult r = DriveCombinedDay(runB, dayB);
                    bakesB += r.BatchesBaked;
                    transfersB += r.TransfersCompleted;
                    flourSacksB += r.MillbrookReorderSacks;
                    dayB++;
                }
            }
            Assert.AreEqual(61, dayB, "run B (varied chunks): the chunk sizes cover days 1-60 exactly, no day skipped or repeated");
            string summaryB = TotalsSummary(runB, bakesB, transfersB, flourSacksB);
            Assert.AreEqual(summaryA, summaryB,
                "varied chunk sizes produce the identical 60-day outcome as 1-day steps (no step size changes the outcome)");

            // Catch-up probe: a batch planned for day 2, run on day 5
            // through the production driver directly, bakes exactly
            // once and is diagnosed as catch-up, never hidden.
            Slice c = NewSlice();
            SeedInputs(c, 24, 2, 2);
            Assert.IsNotNull(c.ProductionDriver.PlanBatches(BakeryBreadCatalog.BreadLoafId, 1, 2, c.Diag),
                "the catch-up probe plans one batch for day 2");
            Assert.AreEqual(1, c.ProductionDriver.RunScheduledDay(
                5, true, WorkstationCatalog.BakeOven, OvenFinder(), null, c.Diag, c.Ids),
                "a late run bakes the planned batch; diag: " + string.Join("; ", c.Diag));
            Assert.IsTrue(c.Diag.Exists(d => d != null && d.Contains("catch-up")),
                "the late bake is diagnosed as catch-up, never hidden");
            Assert.AreEqual(1, c.Bakery.BreadShelf.Count, "the catch-up run bakes exactly one batch (one shelf lot)");
            Assert.AreEqual(24, c.Bakery.BreadShelf[0].UnitCount, "the catch-up batch shelves 24 loaves, once");
        }

        [Test]
        public void Sustained_SixtyDays_SaveLoadMidRun_ExactlyOnce()
        {
            // Priority 3 Slice 5: save/load at day 30 mid-pipeline
            // (existing save seams) and the run must complete to day
            // 60 with no duplicate order, receipt, or settlement.
            // The save captures the notions procurement state, the
            // Millbrook supplier state (including its replenishment
            // week, so no week is re-applied), the bakery DTO (bin and
            // fuel lots, accounts, delivery records, sequences), and
            // the production driver's planned state; store cash and
            // store stock are restored to their day-30 values (they
            // live in no DTO). The restored run must reproduce the
            // uninterrupted 60-day drive's totals exactly — a
            // reference drive runs first for the comparison — and the
            // union of pre-save and post-save order, lot, receipt,
            // delivery, and settlement ids must contain no duplicate.
            CombinedRun reference = NewCombinedRun();
            int refBakes = 0;
            int refTransfers = 0;
            int refFlourSacks = 0;
            for (int day = 1; day <= 60; day++)
            {
                BakeryWholesaleDailyResult r = DriveCombinedDay(reference, day);
                refBakes += r.BatchesBaked;
                refTransfers += r.TransfersCompleted;
                refFlourSacks += r.MillbrookReorderSacks;
            }
            string referenceSummary = TotalsSummary(reference, refBakes, refTransfers, refFlourSacks);

            CombinedRun run = NewCombinedRun();
            Slice s = run.S;
            int bakes = 0;
            int transfers = 0;
            int flourSacks = 0;
            for (int day = 1; day <= 30; day++)
            {
                BakeryWholesaleDailyResult r = DriveCombinedDay(run, day);
                bakes += r.BatchesBaked;
                transfers += r.TransfersCompleted;
                flourSacks += r.MillbrookReorderSacks;
            }
            Assert.AreEqual(16, bakes, "16 bakes before the save (days 15-30)");
            Assert.AreEqual(16, transfers, "16 transfers before the save");
            Assert.AreEqual(16, s.Bakery.WholesaleDeliveries.Count, "16 delivery records before the save");
            int storeCashAt30 = s.StoreRuntime.CurrentCashCents;
            int bakeryCashAt30 = s.BakeryRuntime.CurrentCashCents;
            int storeStockAt30 = s.StoreRuntime.GetCategoryStock("staple_food").CurrentStockUnits;
            int fuelAt30 = s.Bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId);
            int binFlourAt30 = s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId);
            Assert.AreEqual(384, storeStockAt30, "store stock at day 30 is 16 bakes x 24 loaves");
            Assert.AreEqual(44, fuelAt30, "fuel at day 30 is 60 - 16 bakes");

            NamedSkuProcurementSaveDto procurementDto = s.StoreService.ExportState();
            OffMapAidBusinessSaveDto supplierDto = run.Supplier.ExportState();
            BakeryShopRuntime.BakeryShopRuntimeSaveDto bakeryDto = s.Bakery.CaptureSaveDto();
            var plannedIds = new List<string>(s.ProductionDriver.PlannedBatchIds);
            int plannedDay = s.ProductionDriver.PlannedDayIndex;
            // Pre-save counts bound the union checks below: the
            // restored lists CONTAIN the pre-save entries by design,
            // so only entries past these counts are new and must not
            // collide with the pre-save id sets.
            int preOrderCount = s.StoreService.Orders.Count;
            int preLotCount = run.Supplier.Lots.Count;
            int preDeliveryCount = s.Bakery.WholesaleDeliveries.Count;
            int preReceiptCount = s.StoreService.ReceivedReceiptIds.Count;
            int preTxCount = run.Supplier.CashTransactions.Count;
            var preOrderIds = new HashSet<string>();
            foreach (NamedSkuOrder order in s.StoreService.Orders)
                Assert.IsTrue(preOrderIds.Add(order.OrderId), $"pre-save notions order id '{order.OrderId}' is unique");
            var preLotIds = new HashSet<string>();
            foreach (OffMapAidCargoLot lot in run.Supplier.Lots)
                Assert.IsTrue(preLotIds.Add(lot.LotId), $"pre-save Millbrook lot id '{lot.LotId}' is unique");
            var preDeliveryIds = new HashSet<string>();
            foreach (BakeryWholesaleDeliveryRecord delivery in s.Bakery.WholesaleDeliveries)
                Assert.IsTrue(preDeliveryIds.Add(delivery.DeliveryId), $"pre-save delivery id '{delivery.DeliveryId}' is unique");
            var preReceiptIds = new HashSet<string>();
            foreach (string receiptId in s.StoreService.ReceivedReceiptIds)
                Assert.IsTrue(preReceiptIds.Add(receiptId), $"pre-save receipt id '{receiptId}' is unique");
            var preTxIds = new HashSet<string>();
            foreach (OffMapAidCashTransaction tx in run.Supplier.CashTransactions)
                Assert.IsTrue(preTxIds.Add(tx.TransactionId), $"pre-save Millbrook transaction id '{tx.TransactionId}' is unique");

            var f = new Slice();
            f.Diag = s.Diag;
            f.Market = s.Market;
            f.Ids = s.Ids;
            f.StoreRuntime.BindBusinessIdentity(StoreInstanceId);
            f.StoreRuntime.AddCashCents(storeCashAt30);
            f.StoreRuntime.EnsureCategoryStock("staple_food", storeStockAt30, 2000);
            f.BakeryRuntime.BindBusinessIdentity(BakeryInstanceId);
            f.BakeryRuntime.AddCashCents(bakeryCashAt30);
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
            var restoredSupplier = new OffMapAidBusinessRuntime(MillbrookDefinitionWithProduction());
            Assert.IsNull(restoredSupplier.ImportState(supplierDto), "import of the exported supplier state should succeed");
            var restoredAdapter = new MillbrookFlourFreightCustodyAdapter(restoredSupplier, f.Bakery, f.Ids);

            Assert.AreEqual(s.StoreService.Orders.Count, f.StoreService.Orders.Count, "exactly the pre-save notions orders restore");
            for (int i = 0; i < f.StoreService.Orders.Count; i++)
                Assert.AreEqual(s.StoreService.Orders[i].OrderId, f.StoreService.Orders[i].OrderId,
                    $"restored notions order {i} keeps its id");
            Assert.AreEqual(run.Supplier.Lots.Count, restoredSupplier.Lots.Count, "exactly the pre-save Millbrook lots restore");
            for (int i = 0; i < restoredSupplier.Lots.Count; i++)
                Assert.AreEqual(run.Supplier.Lots[i].LotId, restoredSupplier.Lots[i].LotId,
                    $"restored Millbrook lot {i} keeps its id");
            Assert.AreEqual(s.Bakery.WholesaleDeliveries.Count, f.Bakery.WholesaleDeliveries.Count, "exactly the pre-save delivery records restore");
            for (int i = 0; i < f.Bakery.WholesaleDeliveries.Count; i++)
                Assert.AreEqual(s.Bakery.WholesaleDeliveries[i].DeliveryId, f.Bakery.WholesaleDeliveries[i].DeliveryId,
                    $"restored delivery record {i} keeps its id");
            Assert.AreEqual(s.StoreService.ReceivedReceiptIds.Count, f.StoreService.ReceivedReceiptIds.Count, "exactly the pre-save receipt ids restore");
            for (int i = 0; i < f.StoreService.ReceivedReceiptIds.Count; i++)
                Assert.AreEqual(s.StoreService.ReceivedReceiptIds[i], f.StoreService.ReceivedReceiptIds[i],
                    $"restored receipt id {i} is unchanged");
            Assert.AreEqual(run.Supplier.CashTransactions.Count, restoredSupplier.CashTransactions.Count, "exactly the pre-save Millbrook transactions restore");
            for (int i = 0; i < restoredSupplier.CashTransactions.Count; i++)
                Assert.AreEqual(run.Supplier.CashTransactions[i].TransactionId, restoredSupplier.CashTransactions[i].TransactionId,
                    $"restored Millbrook transaction {i} keeps its id");
            Assert.AreEqual(bakeryCashAt30, f.BakeryRuntime.CurrentCashCents, "bakery cash restores to its day-30 value");
            Assert.AreEqual(storeCashAt30, f.StoreRuntime.CurrentCashCents, "store cash restores to its day-30 value");
            Assert.AreEqual(storeStockAt30, f.StoreRuntime.GetCategoryStock("staple_food").CurrentStockUnits, "store stock restores to its day-30 value");
            Assert.AreEqual(fuelAt30, f.Bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId), "fuel restores to its day-30 value");
            Assert.AreEqual(binFlourAt30, f.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId), "bin flour restores to its day-30 value");
            Assert.AreEqual(run.Supplier.CashCents, restoredSupplier.CashCents, "Millbrook cash restores to its day-30 value");
            Assert.AreEqual(run.Supplier.GetInventoryUnits("bakery_flour_sack"), restoredSupplier.GetInventoryUnits("bakery_flour_sack"),
                "Millbrook stock restores to its day-30 value");

            var restoredRun = new CombinedRun { S = f, Reg = run.Reg, Supplier = restoredSupplier, Adapter = restoredAdapter, TotalReplenishedLb = run.TotalReplenishedLb };
            int firstStallDay = 0;
            for (int day = 31; day <= 60; day++)
            {
                BakeryWholesaleDailyResult r = DriveCombinedDay(restoredRun, day);
                if (r.BatchesBaked == 1)
                {
                    bakes++;
                    Assert.AreEqual(1, r.TransfersCompleted, $"transfer completes on a bake day after the restore, day {day}");
                    transfers += r.TransfersCompleted;
                }
                else if (firstStallDay == 0)
                {
                    firstStallDay = day;
                }
                flourSacks += r.MillbrookReorderSacks;
            }
            Assert.AreEqual(0, firstStallDay, "no stall after the restore: daily bakes run days 31-60 with no gap");
            Assert.AreEqual(46, bakes, "46 daily bakes total across the save/load boundary (16 before + 30 after)");
            Assert.AreEqual(46, transfers, "46 transfers total across the save/load boundary");
            Assert.AreEqual(46, f.Bakery.WholesaleDeliveries.Count, "46 delivery records total across the save/load boundary");

            // Only entries past the pre-save counts are new: the
            // restored entries themselves are already in the sets by
            // design, so re-adding them would fail spuriously.
            for (int i = preOrderCount; i < f.StoreService.Orders.Count; i++)
                Assert.IsTrue(preOrderIds.Add(f.StoreService.Orders[i].OrderId), $"notions order id '{f.StoreService.Orders[i].OrderId}' appears exactly once across the save/load boundary");
            for (int i = preLotCount; i < restoredSupplier.Lots.Count; i++)
                Assert.IsTrue(preLotIds.Add(restoredSupplier.Lots[i].LotId), $"Millbrook lot id '{restoredSupplier.Lots[i].LotId}' appears exactly once across the save/load boundary");
            for (int i = preDeliveryCount; i < f.Bakery.WholesaleDeliveries.Count; i++)
                Assert.IsTrue(preDeliveryIds.Add(f.Bakery.WholesaleDeliveries[i].DeliveryId), $"delivery id '{f.Bakery.WholesaleDeliveries[i].DeliveryId}' appears exactly once across the save/load boundary");
            for (int i = preReceiptCount; i < f.StoreService.ReceivedReceiptIds.Count; i++)
                Assert.IsTrue(preReceiptIds.Add(f.StoreService.ReceivedReceiptIds[i]), $"receipt id '{f.StoreService.ReceivedReceiptIds[i]}' appears exactly once across the save/load boundary");
            for (int i = preTxCount; i < restoredSupplier.CashTransactions.Count; i++)
                Assert.IsTrue(preTxIds.Add(restoredSupplier.CashTransactions[i].TransactionId), $"Millbrook transaction id '{restoredSupplier.CashTransactions[i].TransactionId}' appears exactly once across the save/load boundary");
            Assert.AreEqual(46, preOrderIds.Count, "46 distinct notions order ids across the whole run");
            Assert.AreEqual(46, preDeliveryIds.Count, "46 distinct delivery ids across the whole run");

            string restoredSummary = TotalsSummary(restoredRun, bakes, transfers, flourSacks);
            Assert.AreEqual(referenceSummary, restoredSummary,
                "the save/load run reproduces the uninterrupted 60-day drive's totals exactly (no duplicate order, receipt, or settlement, no lost state)");
        }
    }
}
