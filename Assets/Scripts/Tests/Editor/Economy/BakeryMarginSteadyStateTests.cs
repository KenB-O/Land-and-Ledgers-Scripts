using System;
using System.Collections.Generic;
using NUnit.Framework;
using LandLedgers.Economy;
using LandLedgers.Economy.Core;
using LandLedgers.Economy.BreadDrivers;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Economy.Ordering;
using LandLedgers.Population;
using LandLedgers.Primitives;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Plan 11 (2026-11-06) slice M3: pin the steady-state carryover
    /// sequence. TESTS ONLY — no production code is touched. One new
    /// file driving the real daily driver for five consecutive bake days
    /// with the fixed fallback account (24 units, daily, 2c) and an empty
    /// starting flour bin. If the sequence fails, that failure is the
    /// finding — assertions are never weakened to make it pass, and no
    /// production code is edited to make it pass.
    ///
    /// The sequence (plan §2 item 4): day 1 buys 2 sacks (20 lb),
    /// consumes 12, leaves 8; days 2–5 buy 1 sack each and leave 6, 4,
    /// 2, 0 lb; total 6 sacks for 5 batches (174c at the 29c live
    /// price, 34.8c a batch average). The economics line still reports
    /// the 58c standard cost on each of those days — pinned here as the
    /// reporting behaviour M1 labels, not as cash spent.
    ///
    /// Fixture note: notions (5) and fuel (5) are test-seeded inputs
    /// via ReceiveLot (Plan 01 pattern) so the five bakes are not
    /// blocked on the 14-day notions pipeline or on fuel; the FLOUR
    /// bin starts empty, which is what the sequence is about. The store
    /// is seeded with exactly the 6 sacks the sequence needs, so the
    /// test also proves the driver buys only the day's need and no more.
    /// Per-day RunDays are distinct days (2–6); a second RunDay for the
    /// SAME day plans the next lead batch (Plan 10 deviation, recorded)
    /// and is not what is pinned here.
    /// Unity compilation, Test Runner, PlayMode, and the forwarder call
    /// site are NOT claimed; SimulationDrivers is harness-excluded, so
    /// the forwarder is source-reviewed only.
    /// </summary>
    [TestFixture]
    public sealed class BakeryMarginSteadyStateTests
    {
        private const string BakeryInstanceId = "pierce-bakery-1";
        private const string StoreInstanceId = "general-store-1";
        private const int StoreFlourPriceCents = 29;

        private static readonly string[] OvenComponentIds =
        {
            "oven-chamber-1", "kneading-table-1", "proofing-rack-1", "bake-peels-1",
        };

        // Production port over a real BusinessRuntimeState, copied from
        // BakeryWholesaleDailyDriverTests: the production
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

        private static void BookFallbackAccount(Slice s, int dueDay)
        {
            var account = new BakeryWholesaleAccount
            {
                BuyerBusinessId = StoreInstanceId,
                BuyerType = BusinessType.GeneralStore,
                ProductId = BakeryBreadCatalog.BreadLoafId,
                UnitsPerDelivery = 24,
                DeliveryCadenceDays = 1,
                PricePerUnitCents = 2,
                Freshness = BakeryWholesaleFreshness.FreshOrDayOld,
                NextDeliveryDayIndex = dueDay,
            };
            Assert.IsNull(s.Bakery.AddWholesaleAccount(account, s.Diag), "account booking should succeed");
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

        private static BakeryWholesaleDailyInputs InputsFor(Slice s, int dayIndex) =>
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
                StoreFlourUnitPriceCents = StoreFlourPriceCents,
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
                // Notions registration (10c/unit from the catalog) so the
                // economics line carries the full standard cost; the
                // notions themselves come from the seeded bin stock (the
                // supply is unwired in this fixture, as in the driver
                // tests), never from a second supply path.
                NotionsRegistration = new Series2CounterpartyRegistration
                {
                    SupplierId = "notions-supplier",
                    SupplierName = "Notions Supplier",
                    SkuId = BakeryFlourSupply.NotionsMaterialId,
                    UnitGoodsCostCents = 10,
                    TransitDays = 14,
                },
            };

        private static string EconomicsLine(List<string> diag)
        {
            foreach (string line in diag)
                if (line != null && line.Contains("per-batch economics"))
                    return line;
            return string.Empty;
        }

        private static int FlourOnHand(Slice s) =>
            s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId);

        private static int StoreSacks(Slice s) =>
            s.StoreRuntime.GetRetailItemStockUnits("flour", "staple_food");

        [Test]
        public void FiveDaySequence_BuysTwoThenOneEachDay_BinEmptiesOnDayFive()
        {
            Slice s = NewSlice();
            InstallOven(s);
            SeedInputs(s, 0, 5, 5);
            SeedStoreFlour(s, 6);
            BookFallbackAccount(s, 2);

            // Day 1 (dayIndex 2): empty bin, buys 2 sacks (20 lb),
            // bakes 12 lb, leaves 8 lb; store 6 -> 4 sacks.
            var day1Diag = new List<string>();
            BakeryWholesaleDailyResult day1 = BakeryWholesaleDailyDriver.RunDay(InputsFor(s, 2), day1Diag);
            Assert.AreEqual(1, day1.BatchesPlanned, "day 1 plans one batch");
            Assert.AreEqual(2, day1.FlourSacksPurchased, "day 1 buys 2 sacks");
            Assert.AreEqual(20, day1.FlourPoundsPurchased, "day 1 receives 20 lb");
            Assert.AreEqual(1, day1.BatchesBaked, "day 1 bakes; diag: " + string.Join(" | ", day1Diag));
            Assert.AreEqual(1, day1.TransfersCompleted, "day 1 transfers");
            Assert.AreEqual(8, FlourOnHand(s), "bin 20 -> 8 lb after the day-1 bake");
            Assert.AreEqual(4, StoreSacks(s), "store 6 -> 4 sacks");

            // Day 2 (dayIndex 3): bin 8 lb, buys 1 sack, leaves 6 lb.
            var day2Diag = new List<string>();
            BakeryWholesaleDailyResult day2 = BakeryWholesaleDailyDriver.RunDay(InputsFor(s, 3), day2Diag);
            Assert.AreEqual(1, day2.BatchesPlanned);
            Assert.AreEqual(1, day2.FlourSacksPurchased, "day 2 buys 1 sack");
            Assert.AreEqual(10, day2.FlourPoundsPurchased);
            Assert.AreEqual(1, day2.BatchesBaked, "day 2 bakes; diag: " + string.Join(" | ", day2Diag));
            Assert.AreEqual(1, day2.TransfersCompleted);
            Assert.AreEqual(6, FlourOnHand(s), "bin 18 -> 6 lb after the day-2 bake");
            Assert.AreEqual(3, StoreSacks(s), "store 4 -> 3 sacks");

            // Day 3 (dayIndex 4): bin 6 lb, buys 1 sack, leaves 4 lb.
            var day3Diag = new List<string>();
            BakeryWholesaleDailyResult day3 = BakeryWholesaleDailyDriver.RunDay(InputsFor(s, 4), day3Diag);
            Assert.AreEqual(1, day3.BatchesPlanned);
            Assert.AreEqual(1, day3.FlourSacksPurchased, "day 3 buys 1 sack");
            Assert.AreEqual(10, day3.FlourPoundsPurchased);
            Assert.AreEqual(1, day3.BatchesBaked, "day 3 bakes; diag: " + string.Join(" | ", day3Diag));
            Assert.AreEqual(1, day3.TransfersCompleted);
            Assert.AreEqual(4, FlourOnHand(s), "bin 16 -> 4 lb after the day-3 bake");
            Assert.AreEqual(2, StoreSacks(s), "store 3 -> 2 sacks");

            // Day 4 (dayIndex 5): bin 4 lb, buys 1 sack, leaves 2 lb.
            var day4Diag = new List<string>();
            BakeryWholesaleDailyResult day4 = BakeryWholesaleDailyDriver.RunDay(InputsFor(s, 5), day4Diag);
            Assert.AreEqual(1, day4.BatchesPlanned);
            Assert.AreEqual(1, day4.FlourSacksPurchased, "day 4 buys 1 sack");
            Assert.AreEqual(10, day4.FlourPoundsPurchased);
            Assert.AreEqual(1, day4.BatchesBaked, "day 4 bakes; diag: " + string.Join(" | ", day4Diag));
            Assert.AreEqual(1, day4.TransfersCompleted);
            Assert.AreEqual(2, FlourOnHand(s), "bin 14 -> 2 lb after the day-4 bake");
            Assert.AreEqual(1, StoreSacks(s), "store 2 -> 1 sack");

            // Day 5 (dayIndex 6): bin 2 lb, buys 1 sack, leaves 0 lb.
            var day5Diag = new List<string>();
            BakeryWholesaleDailyResult day5 = BakeryWholesaleDailyDriver.RunDay(InputsFor(s, 6), day5Diag);
            Assert.AreEqual(1, day5.BatchesPlanned);
            Assert.AreEqual(1, day5.FlourSacksPurchased, "day 5 buys 1 sack");
            Assert.AreEqual(10, day5.FlourPoundsPurchased);
            Assert.AreEqual(1, day5.BatchesBaked, "day 5 bakes; diag: " + string.Join(" | ", day5Diag));
            Assert.AreEqual(1, day5.TransfersCompleted);
            Assert.AreEqual(0, FlourOnHand(s), "bin 12 -> 0 lb after the day-5 bake");
            Assert.AreEqual(0, StoreSacks(s), "store 1 -> 0 sacks: the driver bought exactly the sequence total");
            Assert.AreEqual(5, s.Bakery.WholesaleDeliveries.Count, "five deliveries, one per day");
        }

        [Test]
        public void SixSackTotal_AndAverageCost_ComputedFromResultFields()
        {
            Slice s = NewSlice();
            InstallOven(s);
            SeedInputs(s, 0, 5, 5);
            SeedStoreFlour(s, 6);
            BookFallbackAccount(s, 2);

            int totalSacks = 0;
            int totalPounds = 0;
            for (int dayIndex = 2; dayIndex <= 6; dayIndex++)
            {
                var dayDiag = new List<string>();
                BakeryWholesaleDailyResult result = BakeryWholesaleDailyDriver.RunDay(InputsFor(s, dayIndex), dayDiag);
                Assert.AreEqual(1, result.BatchesBaked, $"day {dayIndex} bakes; diag: " + string.Join(" | ", dayDiag));
                totalSacks += result.FlourSacksPurchased;
                totalPounds += result.FlourPoundsPurchased;
            }

            Assert.AreEqual(6, totalSacks, "five batches consume 6 sacks (2 + 1 + 1 + 1 + 1)");
            Assert.AreEqual(60, totalPounds, "6 sacks x 10 lb");
            int totalFlourCashCents = totalSacks * StoreFlourPriceCents;
            Assert.AreEqual(174, totalFlourCashCents, "6 sacks x 29c live price");
            Assert.AreEqual(34.8, totalFlourCashCents / 5.0, 0.001,
                "steady-state flour cost averages 34.8c a batch, not the 58c standard cost");
            Assert.AreEqual(2066, s.BakeryRuntime.CurrentCashCents,
                "bakery 2,000 - 174c flour + 5 x 48c wholesale");
            Assert.AreEqual(4934, s.StoreRuntime.CurrentCashCents,
                "store 5,000 + 174c flour - 5 x 48c wholesale");
            Assert.AreEqual(120, s.StoreRuntime.GetCategoryStock("staple_food").CurrentStockUnits,
                "live pool holds all five deliveries (5 x 24 loaves)");
        }

        [Test]
        public void EconomicsLine_StandardCost58c_OnEveryCarryoverDay()
        {
            Slice s = NewSlice();
            InstallOven(s);
            SeedInputs(s, 0, 5, 5);
            SeedStoreFlour(s, 6);
            BookFallbackAccount(s, 2);

            string[] expectedActual = {
                "actual flour purchase today: 2 sack(s), 20 lb, 58c spent",
                "actual flour purchase today: 1 sack(s), 10 lb, 29c spent",
                "actual flour purchase today: 1 sack(s), 10 lb, 29c spent",
                "actual flour purchase today: 1 sack(s), 10 lb, 29c spent",
                "actual flour purchase today: 1 sack(s), 10 lb, 29c spent",
            };
            for (int i = 0; i < 5; i++)
            {
                var dayDiag = new List<string>();
                BakeryWholesaleDailyResult result =
                    BakeryWholesaleDailyDriver.RunDay(InputsFor(s, 2 + i), dayDiag);
                Assert.AreEqual(1, result.BatchesBaked, $"day {2 + i} bakes");
                string line = EconomicsLine(dayDiag);
                Assert.IsTrue(line.Contains("standalone-batch"),
                    $"day {2 + i}: the standard cost is labelled standalone-batch; line: " + line);
                Assert.IsTrue(line.Contains("flour 58c + notions 10c = 68c/batch"),
                    $"day {2 + i}: the line still reports the 58c standard flour cost; line: " + line);
                Assert.IsTrue(line.Contains("margin -20c/batch"),
                    $"day {2 + i}: the line still reports the -20c standard margin; line: " + line);
                Assert.IsTrue(line.Contains(expectedActual[i]),
                    $"day {2 + i}: the line names the actual purchase; line: " + line);
            }
        }

        [Test]
        public void ZeroSackDay_BinCoversBatch_NoPurchase_BakeProceeds()
        {
            // The 0-sack day: the bin already holds the full 12 lb, so
            // nothing is bought, nothing is spent, and the bake proceeds
            // on bin stock — while the line still reports the 58c
            // standalone-batch standard cost.
            Slice s = NewSlice();
            InstallOven(s);
            SeedInputs(s, 12, 1, 1);
            SeedStoreFlour(s, 4);
            BookFallbackAccount(s, 2);

            var dayDiag = new List<string>();
            BakeryWholesaleDailyResult result = BakeryWholesaleDailyDriver.RunDay(InputsFor(s, 2), dayDiag);

            Assert.AreEqual(1, result.BatchesPlanned);
            Assert.AreEqual(0, result.FlourSacksPurchased, "no purchase when the bin covers the batch");
            Assert.AreEqual(0, result.FlourPoundsPurchased);
            Assert.AreEqual(1, result.BatchesBaked, "the bake proceeds; diag: " + string.Join(" | ", dayDiag));
            Assert.AreEqual(1, result.TransfersCompleted);
            Assert.AreEqual(0, FlourOnHand(s), "bin 12 -> 0 lb after the bake");
            Assert.AreEqual(4, StoreSacks(s), "the store keeps all 4 sacks");
            string line = EconomicsLine(dayDiag);
            Assert.IsTrue(line.Contains("flour 58c + notions 10c = 68c/batch"),
                "the standard cost still charges two full sacks on a no-purchase day; line: " + line);
            Assert.IsTrue(line.Contains("actual flour purchase today: 0 sack(s), 0 lb, 0c spent"),
                "the line names the zero purchase; line: " + line);
            Assert.IsTrue(line.Contains("flour on hand after purchase: 12 lb"),
                "the line states the carryover position; line: " + line);
        }
    }
}
