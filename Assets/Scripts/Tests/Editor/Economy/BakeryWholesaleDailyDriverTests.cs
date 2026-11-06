using System;
using System.Collections.Generic;
using NUnit.Framework;
using LandLedgers.Economy;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.BreadDrivers;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Economy.Ordering;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Plan 01 (2026-11-06) slice 1B: the bakery wholesale daily
    /// driver — plan, bake, stock-then-transfer, record. Fixture mirrors
    /// BakeryToStoreWholesaleTransferTests.BakeAndFund (seed flour 24 lb
    /// / notions 2 / fuel 2 via ReceiveLot, one oven with the four
    /// configured component ids, kit gate true via a stub finder), with
    /// the real BakeryShopRuntime, real Series2BakeryProductionDriver
    /// with Series2InStockBakeryFuelSource, real
    /// Review2BakeryToStoreWholesaleDriver, real Pass3StoreCatalog +
    /// Pass3WholesaleIntake, and Series2BusinessRuntimeCashPort over
    /// BusinessRuntimeStates (store 5,000c, bakery 2,000c, staple_food
    /// category ensured 0/1000, port price 5c).
    ///
    /// Deviation from plan §7 test 8 (recorded in the checkpoint): the
    /// plan's "second call plans 0" does not hold — PlanWholesaleBatches
    /// plans one day ahead (WholesalePlanningLeadDays = 1), so after the
    /// first call advances the account to day 3, a second RunDay(2)
    /// still plans 1 batch for that day-3 delivery (the baked batch is
    /// Stage Baked, hence inactive, so it counts no pipeline coverage).
    /// The test therefore seeds exactly one batch of inputs and pins
    /// the idempotency invariants that do hold: the second call bakes 0
    /// (Precheck refuses on flour), transfers 0, moves no cash, and
    /// leaves exactly one delivery record; the lead-window batch stays
    /// Planned. No double-bake with inputs available would be a
    /// PlanWholesaleBatches behaviour, not this driver's — production
    /// calls RunDay once per day change.
    ///
    /// Test 6 note: "24 loaves on the shelf" is verified as 24 units
    /// stocked to the live store pool, because the same RunDay that
    /// bakes them transfers them — the bakery shelf is empty after a
    /// successful transfer, which is the transfer working. A due-tomorrow
    /// account is used so the bake itself is observed on the bakery
    /// shelf before any transfer.
    /// Unity compilation, Test Runner, PlayMode, and the forwarder call
    /// site are NOT claimed; SimulationDrivers is harness-excluded, so
    /// the forwarder is source-reviewed only and exercised here through
    /// the same public APIs it calls.
    /// </summary>
    [TestFixture]
    public sealed class BakeryWholesaleDailyDriverTests
    {
        private const string BakeryInstanceId = "pierce-bakery-1";
        private const string StoreInstanceId = "general-store-1";

        private static readonly string[] OvenComponentIds =
        {
            "oven-chamber-1", "kneading-table-1", "proofing-rack-1", "bake-peels-1",
        };

        // ---- production port over a real BusinessRuntimeState --------
        // Copied from BakeryToStoreWholesaleTransferTests: the production
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

        private sealed class Slice
        {
            public List<string> Diag = new List<string>();
            public BakeryShopRuntime Bakery = new BakeryShopRuntime(BakeryInstanceId);
            public BusinessRuntimeState BakeryRuntime = new BusinessRuntimeState();
            public BusinessRuntimeState StoreRuntime = new BusinessRuntimeState();
            public RuntimeStorePort StorePort;
            public Pass3StoreCatalog Catalog = new Pass3StoreCatalog();
            public Pass3WholesaleIntake Intake = new Pass3WholesaleIntake();
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public Series2BakeryProductionDriver ProductionDriver;
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
            s.StorePort = new RuntimeStorePort { Runtime = s.StoreRuntime };
            s.StorePort.Prices["staple_food"] = 5;
            s.StorePort.OfferList.Add(new GeneralStoreCategoryOffer
            {
                CategoryId = "staple_food",
                OfferedItemIds = new List<string> { "bread" },
                StoreUnitsPerItemUnit = 1,
            });
            s.ProductionDriver = new Series2BakeryProductionDriver(
                s.Bakery, new Series2InStockBakeryFuelSource(s.Bakery));
            return s;
        }

        private static void InstallOven(Slice s)
        {
            Assert.IsNull(s.Bakery.AddOven("bakehouse", new List<string>(OvenComponentIds), s.Diag),
                "oven installation should succeed");
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

        private static BakeryWholesaleAccount BookAccount(Slice s, int units, int cadence, int price, int dueDay,
            BakeryWholesaleFreshness freshness = BakeryWholesaleFreshness.FreshOrDayOld)
        {
            var account = new BakeryWholesaleAccount
            {
                BuyerBusinessId = StoreInstanceId,
                BuyerType = BusinessType.GeneralStore,
                ProductId = BakeryBreadCatalog.BreadLoafId,
                UnitsPerDelivery = units,
                DeliveryCadenceDays = cadence,
                PricePerUnitCents = price,
                Freshness = freshness,
                NextDeliveryDayIndex = dueDay,
            };
            Assert.IsNull(s.Bakery.AddWholesaleAccount(account, s.Diag), "account booking should succeed");
            return account;
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

        private static BakeryWholesaleDailyInputs InputsFor(Slice s, int dayIndex, bool kitUsable = true,
            Func<string, WorkstationComponentView?> finder = null) =>
            new BakeryWholesaleDailyInputs
            {
                Bakery = s.Bakery,
                ProductionDriver = s.ProductionDriver,
                DayIndex = dayIndex,
                BakerKitUsable = kitUsable,
                OvenDefinition = WorkstationCatalog.BakeOven,
                FindOvenComponent = finder ?? OvenFinder(),
                IdRegistry = s.Ids,
                StorePort = s.StorePort,
                StoreCashPort = new Series2BusinessRuntimeCashPort(s.StoreRuntime),
                BakeryCashPort = new Series2BusinessRuntimeCashPort(s.BakeryRuntime),
                StoreBusinessId = StoreInstanceId,
                BakeryBusinessId = BakeryInstanceId,
                Intake = s.Intake,
                StoreCatalog = s.Catalog,
                // Plan 02 slice 2A flour seam: live store price 29c (pinned by
                // derivation in BakeryFlourSupplierComparisonTests), import
                // figures from the catalog, no live import order path, and no
                // Millbrook freight (blocked until a shipment departs).
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
                    FreightUnavailableReason = "freight blocked: no Millbrook shipment has departed (staging/T49 — Obj 16–18)",
                },
                MillbrookSupplier = null,
                ImportFlourPricePerLbCents = 3,
                ImportTransitDays = 14,
                ImportOrderPathExists = false,
            };

        [Test]
        public void MissingAccount_NamesTheBlocker_PlansNothing()
        {
            Slice s = NewSlice();
            InstallOven(s);
            SeedInputs(s, 24, 2, 2);

            BakeryWholesaleDailyResult result = BakeryWholesaleDailyDriver.RunDay(InputsFor(s, 2), s.Diag);

            Assert.AreEqual(0, result.AccountsOnBooks);
            Assert.AreEqual(0, result.BatchesPlanned, "no account, nothing planned");
            Assert.AreEqual(0, result.BatchesBaked);
            Assert.AreEqual(0, result.TransfersCompleted);
            Assert.IsTrue(result.Refusal != null && result.Refusal.Contains("no wholesale account"),
                "result names the account blocker; refusal: " + result.Refusal);
            Assert.AreEqual(5000, s.StoreRuntime.CurrentCashCents, "cash unchanged");
            Assert.AreEqual(2000, s.BakeryRuntime.CurrentCashCents, "cash unchanged");
            Assert.AreEqual(0, s.Bakery.WholesaleDeliveries.Count, "no delivery record");
        }

        [Test]
        public void MissingFlour_PlansOne_PrecheckRefuses_BatchStaysPlanned()
        {
            Slice s = NewSlice();
            InstallOven(s);
            SeedInputs(s, 0, 2, 2);
            BakeryWholesaleAccount account = BookAccount(s, 24, 1, 2, 2);

            BakeryWholesaleDailyResult result = BakeryWholesaleDailyDriver.RunDay(InputsFor(s, 2), s.Diag);

            Assert.AreEqual(1, result.AccountsOnBooks);
            Assert.AreEqual(1, result.BatchesPlanned, "the due account plans one batch");
            Assert.AreEqual(0, result.BatchesBaked);
            Assert.AreEqual(0, result.TransfersCompleted);
            Assert.IsTrue(result.Refusal != null && result.Refusal.Contains("flour"),
                "refusal names flour; refusal: " + result.Refusal + "; diag: " + string.Join(" | ", s.Diag));
            Assert.AreEqual(1, s.Bakery.Batches.Count);
            Assert.AreEqual(BakeryBatchStage.Planned, s.Bakery.Batches[0].Stage, "the batch stays planned on refusal");
            Assert.AreEqual(5000, s.StoreRuntime.CurrentCashCents, "no cash movement");
            Assert.AreEqual(2000, s.BakeryRuntime.CurrentCashCents, "no cash movement");
            Assert.AreEqual(2, account.NextDeliveryDayIndex, "the account does not advance");
            Assert.AreEqual(0, s.Bakery.WholesaleDeliveries.Count, "no delivery record");
        }

        [Test]
        public void MissingFuel_PlansOne_PrecheckRefuses_BatchStaysPlanned()
        {
            Slice s = NewSlice();
            InstallOven(s);
            SeedInputs(s, 24, 2, 0);
            BakeryWholesaleAccount account = BookAccount(s, 24, 1, 2, 2);

            BakeryWholesaleDailyResult result = BakeryWholesaleDailyDriver.RunDay(InputsFor(s, 2), s.Diag);

            Assert.AreEqual(1, result.BatchesPlanned);
            Assert.AreEqual(0, result.BatchesBaked);
            Assert.IsTrue(result.Refusal != null && result.Refusal.Contains("fuel"),
                "refusal names fuel; refusal: " + result.Refusal + "; diag: " + string.Join(" | ", s.Diag));
            Assert.AreEqual(BakeryBatchStage.Planned, s.Bakery.Batches[0].Stage, "the batch stays planned on refusal");
            Assert.AreEqual(5000, s.StoreRuntime.CurrentCashCents, "no cash movement");
            Assert.AreEqual(2000, s.BakeryRuntime.CurrentCashCents, "no cash movement");
            Assert.AreEqual(2, account.NextDeliveryDayIndex, "the account does not advance");
            Assert.AreEqual(0, s.Bakery.WholesaleDeliveries.Count, "no delivery record");
        }

        [Test]
        public void MissingKit_PrecheckRefuses_BakesNothing()
        {
            Slice s = NewSlice();
            InstallOven(s);
            SeedInputs(s, 24, 2, 2);
            BakeryWholesaleAccount account = BookAccount(s, 24, 1, 2, 2);

            BakeryWholesaleDailyResult result = BakeryWholesaleDailyDriver.RunDay(InputsFor(s, 2, kitUsable: false), s.Diag);

            Assert.AreEqual(1, result.BatchesPlanned);
            Assert.AreEqual(0, result.BatchesBaked);
            Assert.IsTrue(result.Refusal != null && result.Refusal.Contains("kit"),
                "refusal names the kit gate; refusal: " + result.Refusal + "; diag: " + string.Join(" | ", s.Diag));
            Assert.AreEqual(BakeryBatchStage.Planned, s.Bakery.Batches[0].Stage, "the batch stays planned on refusal");
            Assert.AreEqual(2, account.NextDeliveryDayIndex, "the account does not advance");
            Assert.AreEqual(0, s.Bakery.WholesaleDeliveries.Count, "no delivery record");
        }

        [Test]
        public void MissingOven_PrecheckRefuses_BakesNothing()
        {
            Slice s = NewSlice();
            SeedInputs(s, 24, 2, 2);
            BakeryWholesaleAccount account = BookAccount(s, 24, 1, 2, 2);

            BakeryWholesaleDailyResult result = BakeryWholesaleDailyDriver.RunDay(InputsFor(s, 2), s.Diag);

            Assert.AreEqual(1, result.BatchesPlanned);
            Assert.AreEqual(0, result.BatchesBaked);
            Assert.IsTrue(result.Refusal != null && result.Refusal.Contains("oven"),
                "refusal names the oven gate; refusal: " + result.Refusal + "; diag: " + string.Join(" | ", s.Diag));
            Assert.AreEqual(BakeryBatchStage.Planned, s.Bakery.Batches[0].Stage, "the batch stays planned on refusal");
            Assert.AreEqual(2, account.NextDeliveryDayIndex, "the account does not advance");
            Assert.AreEqual(0, s.Bakery.WholesaleDeliveries.Count, "no delivery record");
        }

        [Test]
        public void SuccessfulProduction_LeadDayBake_ShelfHoldsOneBatch()
        {
            Slice s = NewSlice();
            InstallOven(s);
            SeedInputs(s, 24, 2, 2);
            BookAccount(s, 24, 1, 2, 3);

            BakeryWholesaleDailyResult result = BakeryWholesaleDailyDriver.RunDay(InputsFor(s, 2), s.Diag);

            Assert.AreEqual(1, result.BatchesPlanned, "the lead window plans tomorrow's delivery today");
            Assert.AreEqual(1, result.BatchesBaked);
            Assert.AreEqual(0, result.TransfersCompleted, "the account is not due today, so nothing transfers");
            Assert.AreEqual(1, s.Bakery.BreadShelf.Count, "one batch baked");
            Assert.AreEqual(24, s.Bakery.BreadShelf[0].UnitCount, "one batch yields 24 loaves on the shelf");
            Assert.AreEqual(12, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId), "flour 24 -> 12 lb");
            Assert.AreEqual(1, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.NotionsItemId), "notions 2 -> 1");
            Assert.AreEqual(1, s.Bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId), "fuel 2 -> 1");
            Assert.IsTrue(string.Join(" | ", s.Diag).Contains("planned labour 180m vs shop day 840m"),
                "labor 180m within the 840m day; diag: " + string.Join(" | ", s.Diag));
            Assert.AreEqual(5000, s.StoreRuntime.CurrentCashCents, "no cash moves before the delivery day");
            Assert.AreEqual(2000, s.BakeryRuntime.CurrentCashCents, "no cash moves before the delivery day");
        }

        [Test]
        public void WholesaleTransfer_ReconcilesExactly_AndRecordsDelivery()
        {
            Slice s = NewSlice();
            InstallOven(s);
            SeedInputs(s, 24, 2, 2);
            BakeryWholesaleAccount account = BookAccount(s, 24, 1, 2, 2);

            BakeryWholesaleDailyResult result = BakeryWholesaleDailyDriver.RunDay(InputsFor(s, 2), s.Diag);

            Assert.AreEqual(1, result.BatchesPlanned);
            Assert.AreEqual(1, result.BatchesBaked);
            Assert.AreEqual(1, result.TransfersCompleted);
            Assert.AreEqual(24, result.UnitsStocked);
            Assert.AreEqual(4952, s.StoreRuntime.CurrentCashCents, "store 5,000 -> 4,952c (24 x 2c)");
            Assert.AreEqual(2048, s.BakeryRuntime.CurrentCashCents, "bakery 2,000 -> 2,048c (24 x 2c)");
            Assert.AreEqual(24, s.StorePort.CategoryStockUnits("staple_food"), "live staple_food pool 0 -> 24");
            Pass3ProductLine line = s.Catalog.Find("bread");
            Assert.IsTrue(line == null || line.ShelfUnits == 0, "catalog shelf 0 after stocking");
            Assert.AreEqual(3, account.NextDeliveryDayIndex, "account advanced by cadence (2 -> 3)");
            Assert.AreEqual(1, s.Bakery.WholesaleDeliveries.Count, "exactly one delivery record");
            BakeryWholesaleDeliveryRecord record = s.Bakery.WholesaleDeliveries[0];
            Assert.IsTrue(record.FullyCovered);
            Assert.AreEqual(24, record.Units);
            Assert.AreEqual(1, record.SellerLotIds.Count, "the seller lot id is recorded");
            Assert.IsTrue(!string.IsNullOrWhiteSpace(record.SellerLotIds[0]), "seller lot id recorded");
            Assert.AreEqual(24, record.UnitsByLot[0]);
            Assert.IsTrue(record.ProvenanceLines.Count > 0, "the delivery record keeps the upstream provenance chain");
            Assert.AreEqual(0, s.Bakery.BreadShelf.Count > 0 ? s.Bakery.BreadShelf[0].UnitCount : 0,
                "the bakery shelf is empty after the transfer");
        }

        [Test]
        public void IdempotentRepeatedDriverCalls_SecondCallBakesAndTransfersNothing()
        {
            // One batch of inputs only: the lead-window plan on the
            // second call cannot bake for lack of flour (see the class
            // header for why "plans 0" does not hold).
            Slice s = NewSlice();
            InstallOven(s);
            SeedInputs(s, 12, 1, 1);
            BookAccount(s, 24, 1, 2, 2);

            BakeryWholesaleDailyResult first = BakeryWholesaleDailyDriver.RunDay(InputsFor(s, 2), s.Diag);
            Assert.AreEqual(1, first.TransfersCompleted);
            Assert.AreEqual(4952, s.StoreRuntime.CurrentCashCents);
            Assert.AreEqual(2048, s.BakeryRuntime.CurrentCashCents);
            Assert.AreEqual(1, s.Bakery.WholesaleDeliveries.Count);

            BakeryWholesaleDailyResult second = BakeryWholesaleDailyDriver.RunDay(InputsFor(s, 2), s.Diag);
            Assert.AreEqual(1, second.BatchesPlanned, "the lead window still plans the day-3 delivery; see class header");
            Assert.AreEqual(0, second.BatchesBaked, "no flour left, so the lead plan cannot bake");
            Assert.AreEqual(0, second.TransfersCompleted);
            Assert.AreEqual(0, second.UnitsStocked);
            Assert.IsTrue(second.Refusal != null && second.Refusal.Contains("flour"),
                "second call refuses on flour; refusal: " + second.Refusal);
            Assert.AreEqual(4952, s.StoreRuntime.CurrentCashCents, "cash unchanged by the second call");
            Assert.AreEqual(2048, s.BakeryRuntime.CurrentCashCents, "cash unchanged by the second call");
            Assert.AreEqual(1, s.Bakery.WholesaleDeliveries.Count, "still exactly one delivery record");
            Assert.AreEqual(2, s.Bakery.Batches.Count, "the lead-window batch is planned, not baked");
            Assert.AreEqual(BakeryBatchStage.Planned, s.Bakery.Batches[1].Stage, "the lead batch stays planned");
        }

        [Test]
        public void ShortShelf_Refuses_AccountStaysDue_ShelfUntouched()
        {
            Slice s = NewSlice();
            InstallOven(s);
            SeedInputs(s, 24, 2, 2);

            // Pre-bake exactly one batch outside the driver: 24 loaves
            // on the shelf, 12 lb flour / 1 notion / 1 fuel left.
            Assert.IsNotNull(s.ProductionDriver.PlanBatches(BakeryBreadCatalog.BreadLoafId, 1, 2, s.Diag));
            Assert.AreEqual(1, s.ProductionDriver.RunScheduledDay(
                2, true, WorkstationCatalog.BakeOven, OvenFinder(), null, s.Diag, s.Ids),
                "one batch should bake; diag: " + string.Join("; ", s.Diag));
            Assert.AreEqual(24, s.Bakery.BreadShelf[0].UnitCount, "one batch yields 24 loaves");

            BakeryWholesaleAccount account = BookAccount(s, 48, 1, 2, 2);
            BakeryWholesaleDailyResult result = BakeryWholesaleDailyDriver.RunDay(InputsFor(s, 2), s.Diag);

            Assert.AreEqual(2, result.BatchesPlanned, "48 units plan two batches");
            Assert.AreEqual(0, result.BatchesBaked, "flour for one batch cannot cover two");
            Assert.AreEqual(0, result.TransfersCompleted, "the transfer refuses on the short shelf");
            Assert.AreEqual(2, account.NextDeliveryDayIndex, "the account stays due");
            Assert.AreEqual(5000, s.StoreRuntime.CurrentCashCents, "cash unchanged");
            Assert.AreEqual(2000, s.BakeryRuntime.CurrentCashCents, "cash unchanged");
            Assert.AreEqual(24, s.Bakery.BreadShelf[0].UnitCount, "the shelf is untouched");
            Assert.AreEqual(0, s.Bakery.WholesaleDeliveries.Count, "no delivery record");
        }

        [Test]
        public void OvenAndKitRecognition_ConfiguredIdsSatisfyDriverGates()
        {
            Slice s = NewSlice();
            SeedInputs(s, 24, 2, 2);

            // Register built the way ScenarioOpeningEquipmentEndowment
            // builds it: component assets in BakeOven definition order
            // (kinds from the definition) plus the baker hand kit.
            var register = new BusinessEquipmentRegister { BusinessInstanceId = BakeryInstanceId };
            string[] kinds = { "oven-chamber", "kneading-table", "proofing-rack", "bake-peels" };
            for (int i = 0; i < OvenComponentIds.Length; i++)
                register.RegisterAsset(new EquipmentAsset
                {
                    AssetId = OvenComponentIds[i],
                    Kind = kinds[i],
                    Condition01 = 1f,
                    OwnerKind = "business",
                    OwnerId = BakeryInstanceId,
                    LocationId = BakeryInstanceId,
                });
            register.RegisterKit(new ToolKitInstance
            {
                InstanceId = $"{BakeryInstanceId}-{BakeryBreadCatalog.BakerHandKitId}-0",
                KitId = BakeryBreadCatalog.BakerHandKitId,
                Condition01 = 1f,
                OwnerKind = "business",
                OwnerId = BakeryInstanceId,
                LocationId = BakeryInstanceId,
            });
            Assert.IsNull(s.Bakery.AddOven("bakehouse", new List<string>(OvenComponentIds), s.Diag),
                "oven installation should succeed");

            Func<string, WorkstationComponentView?> finder = assetId =>
            {
                if (register?.Assets == null) return null;
                foreach (EquipmentAsset asset in register.Assets)
                    if (asset != null && string.Equals(asset.AssetId, assetId, StringComparison.Ordinal))
                        return new WorkstationComponentView
                        {
                            AssetId = asset.AssetId,
                            Kind = asset.Kind,
                            Condition01 = asset.Condition01,
                            IsUsable = asset.IsUsable,
                        };
                return null;
            };

            Assert.AreEqual(1, s.Bakery.CountReadyOvens(WorkstationCatalog.BakeOven, finder, s.Diag),
                "the oven is ready against the register-backed finder; diag: " + string.Join(" | ", s.Diag));

            var availability = EquipmentRequirements.ForHolder(
                "business", s.Bakery.BusinessInstanceId, 2, register.Assets, register.Kits, null);
            var stub = new TaskDefinition("bakery-daily-bake", "daily bakery bake", 1);
            stub.EquipmentClasses.Add(EquipmentRequirementCodes.Kit(BakeryBreadCatalog.BakerHandKitId));
            Assert.IsNull(EquipmentRequirements.CheckTaskRequirements(stub, availability, s.Diag),
                "the kit gate passes for the bakery holder; diag: " + string.Join(" | ", s.Diag));

            // The driver gate itself: a planned batch pre-checks clean
            // on the same oven + kit inputs.
            Assert.IsNotNull(s.ProductionDriver.PlanBatches(BakeryBreadCatalog.BreadLoafId, 1, 2, s.Diag));
            Assert.IsNull(s.ProductionDriver.Precheck(
                2, true, WorkstationCatalog.BakeOven, finder, null, s.Diag),
                "Precheck passes the kit and oven gates on the configured equipment; diag: " + string.Join(" | ", s.Diag));
        }

        [Test]
        public void FlourProcuredThenBaked_EndToEnd()
        {
            // Plan 02 §7 test 12: account booked (24/daily/2c, due day 2),
            // bin empty, store holds 4 sacks @ 29c. Notions/fuel are test
            // fixture inputs via SeedInputs (Plan 01 pattern) — the plan's
            // "notions seeded via BakeryFlourBootstrap" would also seed 120
            // lb flour and defeat the test's premise, so the bootstrap is
            // not used here; it stays test-only and is never a production seed.
            Slice s = NewSlice();
            InstallOven(s);
            SeedInputs(s, 0, 1, 1);
            SeedStoreFlour(s, 4);
            BookAccount(s, 24, 1, 2, 2);

            BakeryWholesaleDailyResult result = BakeryWholesaleDailyDriver.RunDay(InputsFor(s, 2), s.Diag);

            Assert.AreEqual(1, result.BatchesPlanned);
            Assert.AreEqual(2, result.FlourSacksPurchased, "2 sacks bought for the day's need");
            Assert.AreEqual(20, result.FlourPoundsPurchased);
            Assert.AreEqual(1, result.BatchesBaked);
            Assert.AreEqual(1, result.TransfersCompleted);
            Assert.AreEqual(24, result.UnitsStocked);
            Assert.AreEqual(8, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId), "bin flour 20 -> 8 lb");
            Assert.AreEqual(2, s.StoreRuntime.GetRetailItemStockUnits("flour", "staple_food"), "store flour 4 -> 2 sacks");
            Assert.AreEqual(1990, s.BakeryRuntime.CurrentCashCents, "bakery 2,000 - 58c flour + 48c wholesale");
            Assert.AreEqual(5010, s.StoreRuntime.CurrentCashCents, "store 5,000 + 58c flour - 48c wholesale");
            Assert.AreEqual(1, s.Bakery.FlourStock.Lots.Count, "exactly one bin lot for the purchase");
            string diag = string.Join(" | ", s.Diag);
            Assert.IsTrue(diag.Contains("BakeryFlourLocalPurchase: 2 sack(s) of flour (20 lb)"),
                "diagnostics name the flour purchase; diag: " + diag);
            Assert.IsTrue(diag.Contains("per-batch economics") && diag.Contains("margin -10c/batch"),
                "diagnostics name the per-batch margin line (58c flour vs 48c revenue); diag: " + diag);
        }

        [Test]
        public void NoDoublePurchaseOnRepeatedSameDayCall()
        {
            // Plan 02 §7 test 14, fixture-adjusted (see class note below):
            // the store holds exactly the 2 sacks the first call needs, so
            // the repeated same-day call cannot re-buy — the store is empty,
            // no other source is executable — and the duplicate-transaction
            // idempotency itself is pinned by
            // BakeryFlourLocalPurchaseTests.DuplicateTransactionId_IsIdempotent.
            // The plan's parenthetical ("bin already sufficient") does not
            // hold for a 1-batch/day fixture: the second call plans the
            // day-3 lead batch, which needs 12 lb against 8 lb on hand.
            Slice s = NewSlice();
            InstallOven(s);
            SeedInputs(s, 0, 1, 1);
            SeedStoreFlour(s, 2);
            BookAccount(s, 24, 1, 2, 2);

            BakeryWholesaleDailyResult first = BakeryWholesaleDailyDriver.RunDay(InputsFor(s, 2), s.Diag);
            Assert.AreEqual(2, first.FlourSacksPurchased);
            Assert.AreEqual(1, first.TransfersCompleted);
            Assert.AreEqual(1990, s.BakeryRuntime.CurrentCashCents);
            Assert.AreEqual(5010, s.StoreRuntime.CurrentCashCents);
            Assert.AreEqual(1, s.Bakery.FlourStock.Lots.Count);

            BakeryWholesaleDailyResult second = BakeryWholesaleDailyDriver.RunDay(InputsFor(s, 2), s.Diag);
            Assert.AreEqual(0, second.FlourSacksPurchased, "second call buys 0 sacks");
            Assert.AreEqual(0, second.BatchesBaked, "no flour left for the lead batch");
            Assert.AreEqual(0, second.TransfersCompleted);
            Assert.IsTrue(second.Refusal != null && second.Refusal.Contains("flour"),
                "second call refuses on flour; refusal: " + second.Refusal);
            Assert.AreEqual(1990, s.BakeryRuntime.CurrentCashCents, "cash unchanged by the second call");
            Assert.AreEqual(5010, s.StoreRuntime.CurrentCashCents, "cash unchanged by the second call");
            Assert.AreEqual(1, s.Bakery.WholesaleDeliveries.Count, "still exactly one delivery record");
            Assert.AreEqual(1, s.Bakery.FlourStock.Lots.Count, "still exactly one bin lot for the purchase");
            Assert.AreEqual(8, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId), "bin still holds the 8 lb remainder");
        }
    }
}
