using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.BreadDrivers;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Ordering;
using LandLedgers.Orchestration.Player;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Plan 10 slice E6 (Obj 10/11/12/13): the end-to-end food chain in
    /// Roslyn, with test-seeded inputs — the source-level counterpart to
    /// the owed PlayMode demonstration. Bake → transfer → stock → shop →
    /// eat, plus a mid-chain save/restore. TEST-ONLY slice: no production
    /// code changes. If any leg fails, the failing leg with its exact
    /// method and refusal is the finding — assertions are never weakened
    /// to paper over a failure. The shopping fixture mirrors
    /// HouseholdShoppingLoopTests.Fixture (same loop, same 17-step chain)
    /// but buys from the REAL store port over the shared runtime, so the
    /// purchase leg reads the same live staple_food pool the transfer
    /// stocked. Seed inputs follow the Plan 01 pattern (ReceiveLot, never
    /// the bootstrap in the production path — the bootstrap stays
    /// test-only).
    /// </summary>
    [TestFixture]
    public sealed class BakeryFoodChainEndToEndTests
    {
        private const string BakeryInstanceId = "pierce-bakery-1";
        private const string StoreInstanceId = "general-store-1";
        private const int HouseholdId = 7;
        private const int ShopperPersonId = 1;

        // ---- production port over a real BusinessRuntimeState --------
        // Copied from BakeryWholesaleDailyDriverTests: the production
        // GeneralStoreTradingPort wraps a MonoBehaviour manager and cannot
        // be constructed in Roslyn; this seam implements the same
        // production interface over the same BusinessRuntimeState the
        // wholesale cash port uses, so wholesale cash and retail cash
        // reconcile in ONE runtime.
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

            // Shopping side.
            public PopulationState Population;
            public HouseholdMembershipRegistry Membership;
            public HouseholdLedgerRegistry Ledgers;
            public HouseholdInventoryRegistry Inventories;
            public HouseholdNeedRegistry Needs;
            public SupplierDirectory Directory;
            public JourneyModel Journeys;
            public TaskAuthority Tasks;
            public WorkTimeBudgetStore Budgets;
            public PersonScheduleTracker Tracker;
            public ShopperSellerKnowledge Knowledge;
            public HouseholdShoppingLoop Loop;
            public HouseholdMealLogRegistry MealLog;
            public Dictionary<int, string> PersonLocations = new Dictionary<int, string>();
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
                OfferedItemIds = new List<string> { HouseholdItemCatalog.BreadId },
                StoreUnitsPerItemUnit = 1,
            });
            s.ProductionDriver = new Series2BakeryProductionDriver(
                s.Bakery, new Series2InStockBakeryFuelSource(s.Bakery));

            // Shopping side (mirrors HouseholdShoppingLoopTests.Fixture).
            s.Population = new PopulationState();
            s.Population.people.Add(new PersonState
            {
                id = ShopperPersonId, firstName = "Glen", lastName = "Test", age = 34,
                ageBand = AgeBand.Adult18Plus, householdId = HouseholdId,
            });
            s.Population.households.Add(new HouseholdState { id = HouseholdId });
            s.Membership = new HouseholdMembershipRegistry();
            Assert.IsNull(s.Membership.Register(new HouseholdMembership
            {
                MembershipId = 1, PersonId = ShopperPersonId, HouseholdId = HouseholdId, StartDayIndex = 0,
            }));
            s.Ledgers = new HouseholdLedgerRegistry();
            s.Inventories = new HouseholdInventoryRegistry();
            s.Needs = new HouseholdNeedRegistry();
            s.Journeys = new JourneyModel();
            s.Journeys.RegisterLocation(new JourneyLocation("home", JourneyLocationKind.Farmstead, "Test Home", 0f, 0f));
            // The store's journey location id matches the port's
            // LocationId ("general-store"), the value the loop resolves
            // when it plans the trip to the seller.
            s.Journeys.RegisterLocation(new JourneyLocation("general-store", JourneyLocationKind.Store, "General Store", 2f, 0f));
            Assert.IsNull(s.Journeys.AddEdge("home", "general-store", 2.0f, "river road"));
            s.Tasks = new TaskAuthority();
            HouseholdShoppingLoop.RegisterTaskDefinitions(s.Tasks);
            s.Budgets = new WorkTimeBudgetStore();
            s.Tracker = new PersonScheduleTracker();
            s.Knowledge = new ShopperSellerKnowledge();
            s.Directory = new SupplierDirectory();
            GeneralStoreSupplierAdapter.RegisterIn(s.Directory, s.StorePort);
            s.Knowledge.LearnSupplier(ShopperPersonId, StoreInstanceId, "lives in the same town", 0);
            s.Loop = new HouseholdShoppingLoop(
                s.Population, s.Membership, s.Ledgers, s.Inventories, s.Needs,
                s.Directory, s.Journeys, s.Tasks, s.Budgets, s.Tracker, s.Knowledge,
                new FinancialObligationAuthority(), s.Ids);
            s.MealLog = new HouseholdMealLogRegistry();
            s.PersonLocations[ShopperPersonId] = "home";
            return s;
        }

        private static void InstallOven(Slice s)
        {
            Assert.IsNull(s.Bakery.AddOven("bakehouse", new List<string>
            {
                "oven-chamber-1", "kneading-table-1", "proofing-rack-1", "bake-peels-1",
            }, s.Diag), "oven installation should succeed");
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

        private static void BookAccount(Slice s, int dueDay)
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
            };

        private static ShoppingLoopOptions ShoppingOptions(Slice s, int dayIndex) =>
            new ShoppingLoopOptions
            {
                PersonLocationId = pid => s.PersonLocations.TryGetValue(pid, out string loc) ? loc : "home",
                SetPersonLocation = (pid, loc) => s.PersonLocations[pid] = loc,
                DepartureMinuteOfDay = 600,
                InStoreMinutes = 15,
                Diagnostics = s.Diag,
            };

        private static void FundHousehold(Slice s, int cents)
        {
            HouseholdLedger ledger = s.Ledgers.GetOrCreate(HouseholdId);
            Assert.IsNull(ledger.RecordInflow(0, cents, HouseholdIncomeSource.OwnerContribution,
                "test-capital", "test funding", "test"));
        }

        private static ShoppingTripResult BuyBread(Slice s, int units, int dayIndex)
        {
            HouseholdPurchasingNeed need = s.Needs.CreateNeed(HouseholdId, HouseholdItemCatalog.BreadId, dayIndex);
            need.UnitsNeeded = units;
            need.Urgency01 = 0.9f;
            need.ReasonSummary = "test need: bread for meals";
            return s.Loop.ExecuteNeed(need, dayIndex, ShoppingOptions(s, dayIndex));
        }

        private static MealDayResult ServeMeals(Slice s, int dayIndex)
        {
            var mealService = new HouseholdMealService(s.MealLog);
            HouseholdState household = s.Population.GetHousehold(HouseholdId);
            HouseholdInventory inventory = s.Inventories.GetOrCreate(HouseholdId);
            return mealService.ServeHouseholdDay(
                household, new List<int> { ShopperPersonId }, inventory,
                MealSchedulingPolicy.Default, dayIndex, null);
        }

        [Test]
        public void Bake_Transfer_Stock_Shop_Eat_EndToEnd()
        {
            // Leg 1 — bake: account booked (24/daily/2c, due day 2), bin
            // seeded (24 lb flour, 2 notions, 2 fuel), oven + kit present.
            Slice s = NewSlice();
            InstallOven(s);
            SeedInputs(s, 24, 2, 2);
            BookAccount(s, 2);

            BakeryWholesaleDailyResult result = BakeryWholesaleDailyDriver.RunDay(InputsFor(s, 2), s.Diag);
            Assert.AreEqual(1, result.BatchesPlanned);
            Assert.AreEqual(1, result.BatchesBaked, "one batch bakes; diag: " + string.Join(" | ", s.Diag));
            Assert.AreEqual(1, result.TransfersCompleted);
            Assert.AreEqual(24, result.UnitsStocked, "24 loaves stocked to the live staple_food pool");
            Assert.AreEqual(12, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId), "flour 24 -> 12 lb");
            Assert.AreEqual(1, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.NotionsItemId), "notions 2 -> 1");
            Assert.AreEqual(1, s.Bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId), "fuel 2 -> 1");
            Assert.AreEqual(24, s.StoreRuntime.GetCategoryStock("staple_food").CurrentStockUnits, "live pool 0 -> 24");
            Assert.AreEqual(2048, s.BakeryRuntime.CurrentCashCents, "bakery 2,000 + 48c wholesale");
            Assert.AreEqual(4952, s.StoreRuntime.CurrentCashCents, "store 5,000 - 48c wholesale");
            Assert.AreEqual(1, s.Bakery.WholesaleDeliveries.Count, "exactly one delivery record");
            Assert.AreEqual(1, s.Bakery.WholesaleDeliveries[0].SellerLotIds.Count,
                "the delivery draws from exactly one seller lot");
            Assert.IsFalse(string.IsNullOrWhiteSpace(s.Bakery.WholesaleDeliveries[0].SellerLotIds[0]),
                "the delivery record names the seller lot");

            // Leg 2 — purchase: the household shopping loop buys 6 loaves
            // (unit good, 5c) from the stocked store; ReceiveAtHome credits
            // bread lots with the purchase provenance.
            FundHousehold(s, 1000);
            ShoppingTripResult trip = BuyBread(s, 6, 2);
            Assert.IsTrue(trip.Success, "purchase succeeds; failure: " + trip.FailureReason + "; diag: " + string.Join(" | ", s.Diag));
            Assert.AreEqual(6, trip.UnitsAcquired);
            Assert.AreEqual(30, trip.BuyerOutflowCents, "6 loaves x 5c");
            Assert.AreEqual(string.Empty, trip.PartialFillReason, "full purchase, no partial-fill reason");
            Assert.AreEqual(18, s.StoreRuntime.GetCategoryStock("staple_food").CurrentStockUnits, "live pool 24 -> 18");
            Assert.AreEqual(970, s.Ledgers.GetOrCreate(HouseholdId).GetBalanceCents(), "household 1,000 - 30c");
            Assert.AreEqual(4982, s.StoreRuntime.CurrentCashCents, "store 4,952 + 30c retail");
            HouseholdInventory inventory = s.Inventories.GetOrCreate(HouseholdId);
            Assert.AreEqual(6, inventory.GetAvailableUnits(HouseholdItemCatalog.BreadId), "6 bread units in household inventory");
            HouseholdItemLot breadLot = null;
            foreach (HouseholdItemLot lot in inventory.Lots)
                if (lot != null && string.Equals(lot.ItemId, HouseholdItemCatalog.BreadId, StringComparison.Ordinal))
                    breadLot = lot;
            Assert.IsNotNull(breadLot, "a bread lot exists in the household inventory");
            Assert.AreEqual(6, breadLot.QuantityUnits);
            Assert.IsTrue(!string.IsNullOrWhiteSpace(breadLot.SourceLabel) && breadLot.SourceLabel.Contains("carried home"),
                "the lot carries the purchase provenance; source: " + breadLot.SourceLabel);

            // Leg 3 — meal: the next day, the bought loaf is eaten. Two
            // substantial slots (morning + evening; the midday slot needs
            // 3+ staple days of supply and 6 loaves is ~1 day), BreadId
            // first in priority, one serving per slot.
            MealDayResult meals = ServeMeals(s, 3);
            Assert.AreEqual(2, meals.MealsServed, "both substantial slots served");
            Assert.AreEqual(0, meals.MealsMissed);
            Assert.AreEqual(4, inventory.GetAvailableUnits(HouseholdItemCatalog.BreadId), "bread 6 -> 4 after two meals");
            IReadOnlyList<HouseholdMealRecord> records = s.MealLog.GetMeals(HouseholdId);
            Assert.AreEqual(2, records.Count, "two meal records");
            foreach (HouseholdMealRecord record in records)
            {
                Assert.AreEqual(1, record.ItemsConsumed.Count, "each meal consumes one item");
                Assert.AreEqual(HouseholdItemCatalog.BreadId, record.ItemsConsumed[0].ItemId,
                    "the consumed item is the bought bread lot");
                Assert.AreEqual(breadLot.LotId, record.ItemsConsumed[0].LotId,
                    "the meal consumed the purchased lot, not a faked meal");
            }
        }

        [Test]
        public void SecondDay_SecondBatch_SecondTransfer_NoDuplicateDelivery()
        {
            Slice s = NewSlice();
            InstallOven(s);
            SeedInputs(s, 24, 2, 2);
            BookAccount(s, 2);
            BakeryWholesaleDailyResult first = BakeryWholesaleDailyDriver.RunDay(InputsFor(s, 2), s.Diag);
            Assert.AreEqual(1, first.TransfersCompleted);
            string firstLotId = s.Bakery.WholesaleDeliveries[0].SellerLotIds[0];

            // Re-seed the consumed inputs for the second bake (flour tops
            // the 12 lb remainder back to 24 lb; notions/fuel replace the
            // 1 unit each the first bake consumed).
            SeedInputs(s, 12, 1, 1);
            BakeryWholesaleDailyResult second = BakeryWholesaleDailyDriver.RunDay(InputsFor(s, 3), s.Diag);
            Assert.AreEqual(1, second.BatchesBaked, "second day bakes a second batch; diag: " + string.Join(" | ", s.Diag));
            Assert.AreEqual(1, second.TransfersCompleted, "second day transfers a second batch");
            Assert.AreEqual(24, second.UnitsStocked);
            Assert.AreEqual(2, s.Bakery.WholesaleDeliveries.Count, "exactly two delivery records");
            Assert.AreNotEqual(firstLotId, s.Bakery.WholesaleDeliveries[1].SellerLotIds[0],
                "the second delivery is a new lot, not a re-sale of the first");
            Assert.AreEqual(48, s.StoreRuntime.GetCategoryStock("staple_food").CurrentStockUnits,
                "live pool holds both deliveries (24 + 24)");
        }

        [Test]
        public void SaveRestore_MidChain_PlanAndShelfSurvive_ThenTransferWorks()
        {
            // Bake through the production driver only, then save/restore
            // mid-chain (before any transfer): the planned batch and the
            // shelf lot must survive, and the transfer must still work on
            // the restored instances.
            Slice s = NewSlice();
            InstallOven(s);
            SeedInputs(s, 24, 2, 2);
            BookAccount(s, 2);
            Assert.IsNotNull(s.ProductionDriver.PlanBatches(BakeryBreadCatalog.BreadLoafId, 1, 2, s.Diag));
            Assert.AreEqual(1, s.ProductionDriver.RunScheduledDay(
                2, true, WorkstationCatalog.BakeOven, OvenFinder(), null, s.Diag, s.Ids));
            string shelfLotId = s.Bakery.BreadShelf[0].LotId.ToString();
            string plannedBatchId = s.ProductionDriver.PlannedBatchIds[0];

            var saveState = new Series2Pass9SaveState
            {
                Catalog = s.Catalog,
                WholesaleIntake = s.Intake,
                BakeryRuntime = s.Bakery,
                BakeryDriver = s.ProductionDriver,
            };
            Series2Pass9SaveDto dto = saveState.Capture(s.Diag);

            var restoredBakery = new BakeryShopRuntime(BakeryInstanceId);
            var restoredDriver = new Series2BakeryProductionDriver(
                restoredBakery, new Series2InStockBakeryFuelSource(restoredBakery));
            var restoredState = new Series2Pass9SaveState
            {
                Catalog = new Pass3StoreCatalog(),
                WholesaleIntake = new Pass3WholesaleIntake(),
                BakeryRuntime = restoredBakery,
                BakeryDriver = restoredDriver,
            };
            restoredState.Restore(dto, null, s.Diag);

            Assert.AreEqual(1, restoredBakery.BreadShelf.Count, "the shelf lot survives the save/restore");
            Assert.AreEqual(shelfLotId, restoredBakery.BreadShelf[0].LotId.ToString());
            Assert.AreEqual(24, restoredBakery.BreadShelf[0].UnitCount);
            Assert.AreEqual(2, restoredDriver.PlannedDayIndex, "the planned day survives");
            Assert.AreEqual(1, restoredDriver.PlannedBatchIds.Count, "the planned batch survives");
            Assert.AreEqual(plannedBatchId, restoredDriver.PlannedBatchIds[0]);

            // The transfer still works on the restored instances.
            Review2WholesaleTransferResult transfer = Review2BakeryToStoreWholesaleDriver.TransferToShelf(
                new Review2WholesaleTransferOptions
                {
                    Bakery = restoredBakery,
                    BakeryProductId = BakeryBreadCatalog.BreadLoafId,
                    Units = 24,
                    WholesalePriceCents = 2,
                    RetailPriceCents = 5,
                    TransferDayIndex = 2,
                    Intake = restoredState.WholesaleIntake,
                    StoreCatalog = restoredState.Catalog,
                    StoreCashPort = new Series2BusinessRuntimeCashPort(s.StoreRuntime),
                    BakeryCashPort = new Series2BusinessRuntimeCashPort(s.BakeryRuntime),
                    StoreBusinessId = StoreInstanceId,
                    BakeryBusinessId = BakeryInstanceId,
                }, s.Diag);
            Assert.IsTrue(transfer.Transferred, "transfer works after restore; refusal: " + transfer.Refusal);
        }

        [Test]
        public void SaveRestore_AfterTransfer_DeliveryRecordSurvives()
        {
            Slice s = NewSlice();
            InstallOven(s);
            SeedInputs(s, 24, 2, 2);
            BookAccount(s, 2);
            BakeryWholesaleDailyResult result = BakeryWholesaleDailyDriver.RunDay(InputsFor(s, 2), s.Diag);
            Assert.AreEqual(1, result.TransfersCompleted);
            string deliveryLotId = s.Bakery.WholesaleDeliveries[0].SellerLotIds[0];

            var saveState = new Series2Pass9SaveState
            {
                Catalog = s.Catalog,
                WholesaleIntake = s.Intake,
                BakeryRuntime = s.Bakery,
                BakeryDriver = s.ProductionDriver,
            };
            Series2Pass9SaveDto dto = saveState.Capture(s.Diag);

            var restoredBakery = new BakeryShopRuntime(BakeryInstanceId);
            var restoredDriver = new Series2BakeryProductionDriver(
                restoredBakery, new Series2InStockBakeryFuelSource(restoredBakery));
            var restoredState = new Series2Pass9SaveState
            {
                Catalog = new Pass3StoreCatalog(),
                WholesaleIntake = new Pass3WholesaleIntake(),
                BakeryRuntime = restoredBakery,
                BakeryDriver = restoredDriver,
            };
            restoredState.Restore(dto, null, s.Diag);

            Assert.AreEqual(1, restoredBakery.WholesaleDeliveries.Count, "the delivery record survives");
            Assert.AreEqual(deliveryLotId, restoredBakery.WholesaleDeliveries[0].SellerLotIds[0]);
            Assert.AreEqual(24, restoredBakery.WholesaleDeliveries[0].Units);
            Assert.AreEqual(48, restoredBakery.WholesaleDeliveries[0].TotalCents);
            Assert.AreEqual(3, restoredBakery.WholesaleAccounts[0].NextDeliveryDayIndex, "the account advanced");
            Assert.IsTrue(restoredState.WholesaleIntake.DeliveredLotIds().Contains(deliveryLotId),
                "the intake still guards the delivered lot against re-intake");
        }

        [Test]
        public void EmptyStorePool_ShoppingFails_NoMealServedFromNothing()
        {
            // Negative leg: an empty live pool means no stock to sell, and
            // no meal is served from nothing — the missed meal is logged,
            // never faked.
            Slice s = NewSlice();
            InstallOven(s);
            SeedInputs(s, 24, 2, 2);
            BookAccount(s, 2);
            FundHousehold(s, 1000);

            ShoppingTripResult trip = BuyBread(s, 6, 2);
            Assert.IsFalse(trip.Success, "no stock to sell — the purchase must fail");
            Assert.IsTrue(!string.IsNullOrWhiteSpace(trip.FailureReason),
                "the failure names its reason");
            Assert.AreEqual(0, trip.UnitsAcquired);
            Assert.AreEqual(1000, s.Ledgers.GetOrCreate(HouseholdId).GetBalanceCents(), "no money moved");
            Assert.AreEqual(0, s.Inventories.GetOrCreate(HouseholdId).GetAvailableUnits(HouseholdItemCatalog.BreadId));

            MealDayResult meals = ServeMeals(s, 3);
            Assert.AreEqual(0, meals.MealsServed, "no meal is served from an empty larder");
            Assert.AreEqual(2, meals.MealsMissed, "both substantial slots are missed — and logged, not faked");
            Assert.AreEqual(0, s.MealLog.GetMeals(HouseholdId).Count);
            Assert.AreEqual(2, s.MealLog.GetMissedMeals(HouseholdId).Count);
        }

        [Test]
        public void SameDaySecondRunDay_TransfersNothingTwice_NoDuplicateDelivery()
        {
            // Negative leg, fixture-adjusted (see
            // BakeryWholesaleDailyDriverTests.NoDoublePurchaseOnRepeatedSameDayCall):
            // the second same-day call legitimately plans and bakes the
            // day-3 LEAD batch — PlanWholesaleBatches plans when
            // NextDeliveryDayIndex <= dayIndex + WholesalePlanningLeadDays
            // — because the first bake left exactly 12 lb flour, 1 notion
            // and 1 fuel in the bin. The idempotency that matters is that
            // the day-2 delivery is never transferred or recorded twice:
            // the lead batch sits on the shelf for day 3, the live pool
            // holds only the first delivery, and no second delivery
            // record is created.
            Slice s = NewSlice();
            InstallOven(s);
            SeedInputs(s, 24, 2, 2);
            BookAccount(s, 2);
            BakeryWholesaleDailyResult first = BakeryWholesaleDailyDriver.RunDay(InputsFor(s, 2), s.Diag);
            Assert.AreEqual(1, first.BatchesBaked);
            Assert.AreEqual(1, first.TransfersCompleted);

            BakeryWholesaleDailyResult second = BakeryWholesaleDailyDriver.RunDay(InputsFor(s, 2), s.Diag);
            Assert.AreEqual(1, second.BatchesPlanned, "second same-day call plans only the day-3 lead batch");
            Assert.AreEqual(1, second.BatchesBaked, "the lead batch bakes (bin held exactly one batch of inputs)");
            Assert.AreEqual(0, second.TransfersCompleted, "second same-day call transfers nothing twice");
            Assert.AreEqual(1, s.Bakery.WholesaleDeliveries.Count, "still exactly one delivery record");
            Assert.AreEqual(24, s.StoreRuntime.GetCategoryStock("staple_food").CurrentStockUnits,
                "the live pool is unchanged by the second call");
            int shelfUnits = 0;
            int fullLots = 0;
            foreach (BakeryBreadLot lot in s.Bakery.BreadShelf)
            {
                shelfUnits += lot.UnitCount;
                if (lot.UnitCount == 24) fullLots++;
            }
            Assert.AreEqual(24, shelfUnits, "the shelf holds exactly the lead batch's 24 loaves");
            Assert.AreEqual(1, fullLots, "exactly one full lot — the shipped lot stays on the shelf at 0 units after transfer, never re-sold");
        }
    }
}
