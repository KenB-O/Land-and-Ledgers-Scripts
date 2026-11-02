// Review 2 vertical slice Pass D1: bakery -> store wholesale -> store
// shelf sale -> household meal, on production driver/adapter classes
// only. This is the chain the second read-only review found unwired:
// the bakery adapter sold direct (Pass 10) and the Pass 3 wholesale
// catalog held shelf stock (Pass 7), but no caller chained bakery
// shelf -> Pass 3 BACK -> settlement -> Pass 3 shelf -> LIVE store
// category pool -> shopping-loop purchase -> lot meal. The new
// Review2BakeryToStoreWholesaleDriver is that caller; these tests
// prove the chain reconciles exactly, and pin every refusal mode
// that must move nothing.
//
// Prices are test parameters supplied by the caller, never canon:
// wholesale 2c (the store definition's bread wholesale price) and
// retail 5c (the definition's bread baseline price) are the
// evidence a caller may pass in, not defaults of the driver.
// The live category is staple_food, because bread is a staple_food
// item in the definition. The household is a generic test person,
// never named Ethan/Tia/Shane, and never presented as the slice
// fixture. No .asset, fixture, or Main Scene claim is made here.
using System;
using System.Collections.Generic;
using NUnit.Framework;
using LandLedgers.Economy;
using LandLedgers.Economy.BreadDrivers;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Ordering;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;

namespace LandLedgers.Tests.Economy
{
    public sealed class Review2VerticalD1Tests
    {
        // ---- production port over a real BusinessRuntimeState --------
        // The production GeneralStoreTradingPort wraps a MonoBehaviour
        // manager and cannot be constructed in Roslyn; this seam
        // implements the same production interface over the same
        // BusinessRuntimeState the wholesale cash port uses, so
        // wholesale cash and retail cash reconcile in ONE runtime.
        // It is a test seam, not a second cash authority: every cash
        // move lands in the shared runtime.
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

        private sealed class RefusingCreditPort : IBusinessCashPort
        {
            public int BalanceCents => 999999;
            public string TryDebit(int amountCents, string reason) => null;
            public string Credit(int amountCents, string reason) => "test port refuses credit";
        }

        private sealed class Slice
        {
            public List<string> Diag = new List<string>();
            public BakeryShopRuntime Bakery = new BakeryShopRuntime("pierce-bakery-1");
            public BusinessRuntimeState BakeryRuntime = new BusinessRuntimeState();
            public BusinessRuntimeState StoreRuntime = new BusinessRuntimeState();
            public RuntimeStorePort StorePort;
            public Pass3StoreCatalog Catalog = new Pass3StoreCatalog();
            public Pass3WholesaleIntake Intake = new Pass3WholesaleIntake();
            public EntityIdRegistry Ids = new EntityIdRegistry();
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

        private static Slice BakeAndFund(int storeCash, int bakeryCash, int bakeDay)
        {
            var s = new Slice();
            s.StoreRuntime.BindBusinessIdentity("general-store-1");
            s.StoreRuntime.AddCashCents(storeCash);
            s.StoreRuntime.EnsureCategoryStock("staple_food", 0, 1000);
            s.BakeryRuntime.BindBusinessIdentity("pierce-bakery-1");
            s.BakeryRuntime.AddCashCents(bakeryCash);
            s.StorePort = new RuntimeStorePort { Runtime = s.StoreRuntime };
            s.StorePort.Prices["staple_food"] = 5;
            s.StorePort.OfferList.Add(new GeneralStoreCategoryOffer
            {
                CategoryId = "staple_food",
                OfferedItemIds = new List<string> { "bread" },
                StoreUnitsPerItemUnit = 1,
            });
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
            }, s.Diag));
            Assert.IsNull(s.Bakery.FlourStock.ReceiveLot(new BakeryFlourLot
            {
                LotId = s.Ids.Allocate(EntityKind.Lot),
                FlourName = BakeryBreadCatalog.NotionsItemId,
                Units = 2,
                AcquiredDayIndex = 0,
                ImportOrderId = "test-notions-order",
                OriginName = "Test notions",
            }, s.Diag));
            Assert.IsNull(s.Bakery.FuelStock.ReceiveLot(new BakeryFuelLot
            {
                LotId = s.Ids.Allocate(EntityKind.Lot),
                FuelName = BakeryBreadCatalog.FuelItemId,
                Units = 2,
                AcquiredDayIndex = 0,
                ImportOrderId = "test-fuel-order",
                OriginName = "Test fuel",
                IsBootstrapEndowment = false,
            }, s.Diag));
            var driver = new Series2BakeryProductionDriver(
                s.Bakery, new Series2InStockBakeryFuelSource(s.Bakery));
            Assert.IsNotNull(driver.PlanBatches(BakeryBreadCatalog.BreadLoafId, 1, bakeDay, s.Diag));
            Assert.AreEqual(1, driver.RunScheduledDay(
                bakeDay, true, WorkstationCatalog.BakeOven, OvenFinder(), null, s.Diag, s.Ids),
                "one batch should bake; diag: " + string.Join("; ", s.Diag));
            Assert.AreEqual(24, s.Bakery.BreadShelf[0].UnitCount, "one batch yields 24 loaves");
            return s;
        }

        private static Review2WholesaleTransferOptions TransferOptions(
            Slice s, int units, int wholesale, int retail, int transferDay,
            IBusinessCashPort storePort, IBusinessCashPort bakeryPort)
        {
            return new Review2WholesaleTransferOptions
            {
                Bakery = s.Bakery,
                BakeryProductId = BakeryBreadCatalog.BreadLoafId,
                Units = units,
                WholesalePriceCents = wholesale,
                RetailPriceCents = retail,
                TransferDayIndex = transferDay,
                Intake = s.Intake,
                StoreCatalog = s.Catalog,
                StoreCashPort = storePort,
                BakeryCashPort = bakeryPort,
                StoreBusinessId = "general-store-1",
                BakeryBusinessId = "pierce-bakery-1",
            };
        }

        private static IBusinessCashPort ProductionPort(BusinessRuntimeState runtime)
        {
            return new Series2BusinessRuntimeCashPort(runtime);
        }

        [Test]
        public void TransferAndStock_ReconcilesExactly()
        {
            Slice s = BakeAndFund(5000, 2000, 2);
            Review2WholesaleTransferResult transfer = Review2BakeryToStoreWholesaleDriver.TransferToShelf(
                TransferOptions(s, 24, 2, 5, 2, ProductionPort(s.StoreRuntime), ProductionPort(s.BakeryRuntime)), s.Diag);
            Assert.IsTrue(transfer.Transferred, transfer.Refusal + "; diag: " + string.Join("; ", s.Diag));
            Assert.IsTrue(transfer.Settled);
            Assert.IsTrue(transfer.OnShelf);
            Assert.AreEqual("bread", transfer.StoreProductId, "the map translates bake.bread-loaf to store bread, loaf to loaf");
            Assert.AreEqual("Fresh", transfer.Condition, "baked day 2, transferred day 2");
            Assert.AreEqual(48, transfer.AmountCents, "24 loaves x 2c wholesale");
            Assert.AreEqual("gs.move-back-to-shelf", transfer.TaskActionId, "the shelf move must be named as the Pass 2 task");
            Assert.AreEqual(2, transfer.TaskMinutesPerLot);
            Assert.AreEqual(0, s.Bakery.BreadShelf[0].UnitCount, "the bakery shelf lot is fully taken by the wholesale");
            Assert.AreEqual(4952, s.StoreRuntime.CurrentCashCents, "store pays exactly the wholesale amount");
            Assert.AreEqual(2048, s.BakeryRuntime.CurrentCashCents, "bakery gains exactly the wholesale amount");
            Pass3ProductLine line = s.Catalog.Find("bread");
            Assert.IsNotNull(line);
            Assert.AreEqual(0, line.BackUnits, "the shelf move empties BACK");
            Assert.AreEqual(24, line.ShelfUnits, "all 24 loaves sit on the Pass 3 shelf");
            Assert.AreEqual(5, line.PriceCents, "the retail decision, not the wholesale cost, prices the shelf line");
            Assert.AreEqual(0, s.StorePort.CategoryStockUnits("staple_food"), "transfer alone does not touch the live pool");

            Review2StockingResult stocked = Review2BakeryToStoreWholesaleDriver.StockShelfToLive(
                s.Catalog, "bread", 24, "staple_food", s.StorePort, s.Diag);
            Assert.IsTrue(stocked.Stocked, stocked.Refusal);
            Assert.AreEqual(0, line.ShelfUnits, "stocking consumes the Pass 3 shelf exactly once");
            Assert.AreEqual(24, stocked.LivePoolUnitsAfter, "the live pool gains exactly the stocked units");
            Assert.AreEqual(24, s.StorePort.CategoryStockUnits("staple_food"));
            Assert.AreEqual(4952, s.StoreRuntime.CurrentCashCents, "stocking moves stock, never cash");
        }

        [Test]
        public void FullTrace_BakeToStoreToHouseholdMeal_ReconcilesExactly()
        {
            Slice s = BakeAndFund(5000, 2000, 2);
            Review2WholesaleTransferResult transfer = Review2BakeryToStoreWholesaleDriver.TransferToShelf(
                TransferOptions(s, 24, 2, 5, 2, ProductionPort(s.StoreRuntime), ProductionPort(s.BakeryRuntime)), s.Diag);
            Assert.IsTrue(transfer.Transferred, transfer.Refusal);
            Review2StockingResult stocked = Review2BakeryToStoreWholesaleDriver.StockShelfToLive(
                s.Catalog, "bread", 24, "staple_food", s.StorePort, s.Diag);
            Assert.IsTrue(stocked.Stocked, stocked.Refusal);

            // Household shopping through the production store
            // adapter, via the shopping loop, exactly as Pass 10
            // proved for the bakery adapter: shortage -> knowledge
            // -> directory -> offer -> task/travel -> cash -> lot.
            var population = new PopulationState();
            population.people.Add(new PersonState
            {
                id = 1, firstName = "Test", lastName = "Shopper", age = 34,
                ageBand = AgeBand.Adult18Plus, laborAccessLevel = LaborAccessLevel.FullLaborMarket,
            });
            population.households.Add(new HouseholdState { id = 7 });
            var membership = new HouseholdMembershipRegistry();
            Assert.IsNull(membership.Register(new HouseholdMembership
            {
                MembershipId = 1, PersonId = 1, HouseholdId = 7, StartDayIndex = 0,
            }));
            var ledgers = new HouseholdLedgerRegistry();
            var inventories = new HouseholdInventoryRegistry();
            var needs = new HouseholdNeedRegistry();
            var journeys = new JourneyModel();
            journeys.RegisterLocation(new JourneyLocation("home", JourneyLocationKind.Farmstead, "Test Home", 0f, 0f));
            journeys.RegisterLocation(new JourneyLocation("general-store", JourneyLocationKind.Store, "General Store", 2f, 0f));
            journeys.AddEdge("home", "general-store", 2.0f, "town road");
            var tasks = new TaskAuthority();
            HouseholdShoppingLoop.RegisterTaskDefinitions(tasks);
            var budgets = new WorkTimeBudgetStore();
            var tracker = new PersonScheduleTracker();
            var knowledge = new ShopperSellerKnowledge();
            var obligations = new FinancialObligationAuthority();
            var entityIds = new EntityIdRegistry();
            var directory = new SupplierDirectory();
            GeneralStoreSupplierAdapter.RegisterIn(directory, s.StorePort);
            knowledge.LearnSupplier(1, "general-store-1", "lives in the same town", 0);
            var loop = new HouseholdShoppingLoop(
                population, membership, ledgers, inventories, needs,
                directory, journeys, tasks, budgets, tracker, knowledge,
                obligations, entityIds);
            var personLocations = new Dictionary<int, string> { { 1, "home" } };
            var loopDiag = new List<string>();
            HouseholdLedger ledger = ledgers.GetOrCreate(7);
            Assert.IsNull(ledger.RecordInflow(0, 10000, HouseholdIncomeSource.OwnerContribution,
                "test-capital", "test funding", "test"));
            var monitor = new HouseholdShortageMonitor(needs);
            monitor.Evaluate(new HouseholdState { id = 7 }, inventories.GetOrCreate(7),
                new Dictionary<string, int> { { HouseholdItemCatalog.BreadId, 2 } },
                false,
                new HouseholdSupplyAccess { HasGeneralStore = true, HasCash = true },
                0, 0, 2);
            HouseholdPurchasingNeed need = needs.GetOpenNeed(7, HouseholdItemCatalog.BreadId);
            Assert.IsNotNull(need, "the shortage monitor must open a real bread need");
            Assert.AreEqual(14, need.UnitsNeeded, "empty household at 2/day against the 7-day threshold needs 14 units");
            ShoppingTripResult trip = loop.ExecuteNeed(need, 2, new ShoppingLoopOptions
            {
                PersonLocationId = pid => personLocations.TryGetValue(pid, out string loc) ? loc : "home",
                SetPersonLocation = (pid, loc) => personLocations[pid] = loc,
                DepartureMinuteOfDay = 600,
                InStoreMinutes = 15,
                Diagnostics = loopDiag,
                AllowTradeCredit = false,
            });
            Assert.IsTrue(trip.Success, trip.FailureReason + "; diag: " + string.Join("; ", loopDiag));
            Assert.AreEqual(14, trip.UnitsAcquired);
            Assert.AreEqual("General Store", trip.Counterparty, "the purchase must go through the store adapter, not a scripted sale");
            Assert.AreEqual(14 * 5, trip.BuyerOutflowCents, "14 loaves at the 5c retail price");
            Assert.AreEqual(trip.BuyerOutflowCents, trip.StoreCashInCents, "buyer outflow must equal business cash in, every cent");
            Assert.AreEqual(4952 + 70, s.StoreRuntime.CurrentCashCents, "store cash: wholesale paid, then retail gained");
            Assert.AreEqual(2048, s.BakeryRuntime.CurrentCashCents, "the retail sale moves no bakery cash");
            Assert.AreEqual(10, s.StorePort.CategoryStockUnits("staple_food"), "live pool loses exactly the units sold");
            Assert.AreEqual(14, inventories.GetOrCreate(7).GetAvailableUnits(HouseholdItemCatalog.BreadId),
                "goods enter household inventory only upon physical receipt through the loop");
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status);
            Assert.AreEqual(10000 - 70, ledgers.GetOrCreate(7).GetBalanceCents());

            // Lot meal from the purchased bread, then nutrition.
            // Midday is disabled by the caller so the two substantial
            // slots match the nutrition denominator of 2 meals/day;
            // that is a test parameter, not a canon meal rule.
            var mealService = new HouseholdMealService(new HouseholdMealLogRegistry());
            var policy = new MealSchedulingPolicy { MiddayMealEnabled = false };
            MealDayResult meals = mealService.ServeHouseholdDay(
                new HouseholdState { id = 7 }, new List<int> { 1 },
                inventories.GetOrCreate(7), policy, 2, null);
            Assert.AreEqual(2, meals.MealsServed, "morning + evening from purchased bread");
            Assert.AreEqual(0, meals.MealsMissed);
            Assert.AreEqual(12, inventories.GetOrCreate(7).GetAvailableUnits(HouseholdItemCatalog.BreadId),
                "two 1-loaf servings leave 12 loaves");
            PersonState shopper = population.people[0];
            shopper.nutrition.nutrition01 = 0.70f;
            // The 2 here is DailyNeedsService.MealsPerPersonPerDay;
            // the constant is referenced as a literal so the harness
            // does not have to pull in DailyNeedsService's
            // restaurant/boarding/hotel meal-day sources.
            Assert.IsFalse(shopper.nutrition.ApplyDay(meals.MealsServed, 2, 2),
                "a fully-fed day must not be undernourished");
            Assert.AreEqual(1.0f, shopper.nutrition.nutrition01, "0.70 + 0.30 fed-day recovery, capped at 1.0");
        }

        [Test]
        public void Transfer_StaleLot_Refuses_NothingMoves()
        {
            Slice s = BakeAndFund(5000, 2000, 2);
            Review2WholesaleTransferResult result = Review2BakeryToStoreWholesaleDriver.TransferToShelf(
                TransferOptions(s, 24, 2, 5, 4, ProductionPort(s.StoreRuntime), ProductionPort(s.BakeryRuntime)), s.Diag);
            Assert.IsFalse(result.Transferred, "a lot 2 days past baked day is stale and must be refused");
            Assert.IsFalse(result.IntakeCompleted);
            Assert.AreEqual(24, s.Bakery.BreadShelf[0].UnitCount, "stale refusal leaves the bakery shelf untouched");
            Assert.AreEqual(5000, s.StoreRuntime.CurrentCashCents);
            Assert.AreEqual(2000, s.BakeryRuntime.CurrentCashCents);
            Assert.IsNull(s.Catalog.Find("bread"), "no catalog line is created by a refused transfer");
        }

        [Test]
        public void Transfer_UnmappedProduct_Refuses_NothingMoves()
        {
            Slice s = BakeAndFund(5000, 2000, 2);
            var options = TransferOptions(s, 24, 2, 5, 2, ProductionPort(s.StoreRuntime), ProductionPort(s.BakeryRuntime));
            options.BakeryProductId = "bake.rolls";
            Review2WholesaleTransferResult result = Review2BakeryToStoreWholesaleDriver.TransferToShelf(options, s.Diag);
            Assert.IsFalse(result.Transferred);
            Assert.IsTrue(result.Refusal.Contains("no store translation"), "rolls are deliberately unmapped; the refusal must say so: " + result.Refusal);
            Assert.AreEqual(24, s.Bakery.BreadShelf[0].UnitCount);
            Assert.AreEqual(5000, s.StoreRuntime.CurrentCashCents);
            Assert.IsNull(s.Catalog.Find("bread"));
        }

        [Test]
        public void Transfer_RetailEqualsWholesale_Refuses_BeforeIntake()
        {
            Slice s = BakeAndFund(5000, 2000, 2);
            Review2WholesaleTransferResult result = Review2BakeryToStoreWholesaleDriver.TransferToShelf(
                TransferOptions(s, 24, 2, 2, 2, ProductionPort(s.StoreRuntime), ProductionPort(s.BakeryRuntime)), s.Diag);
            Assert.IsFalse(result.Transferred);
            Assert.IsFalse(result.IntakeCompleted, "the retail guard must fire before intake, not after cash moves");
            Assert.AreEqual(24, s.Bakery.BreadShelf[0].UnitCount);
            Assert.AreEqual(5000, s.StoreRuntime.CurrentCashCents);
            Assert.AreEqual(2000, s.BakeryRuntime.CurrentCashCents);
        }

        [Test]
        public void Transfer_InsufficientPayerCash_Refuses_BeforeIntake()
        {
            Slice s = BakeAndFund(10, 2000, 2);
            Review2WholesaleTransferResult result = Review2BakeryToStoreWholesaleDriver.TransferToShelf(
                TransferOptions(s, 24, 2, 5, 2, ProductionPort(s.StoreRuntime), ProductionPort(s.BakeryRuntime)), s.Diag);
            Assert.IsFalse(result.Transferred);
            Assert.IsFalse(result.IntakeCompleted, "a payer that cannot cover wholesale must be refused before intake");
            Assert.AreEqual(24, s.Bakery.BreadShelf[0].UnitCount);
            Assert.AreEqual(10, s.StoreRuntime.CurrentCashCents);
            Assert.IsNull(s.Catalog.Find("bread"));
        }

        [Test]
        public void Transfer_SettlementFailure_StandsInBack_ThenRecovers()
        {
            Slice s = BakeAndFund(5000, 2000, 2);
            Review2WholesaleTransferResult failed = Review2BakeryToStoreWholesaleDriver.TransferToShelf(
                TransferOptions(s, 24, 2, 5, 2, ProductionPort(s.StoreRuntime), new RefusingCreditPort()), s.Diag);
            Assert.IsFalse(failed.Transferred);
            Assert.IsTrue(failed.IntakeCompleted, "intake happened before settlement failed");
            Assert.IsFalse(failed.Settled);
            Assert.IsFalse(failed.OnShelf, "no shelf or live movement on a settlement failure");
            Assert.AreEqual(0, s.Bakery.BreadShelf[0].UnitCount, "the bakery shelf was taken, and that taking is not undone");
            Assert.AreEqual(5000, s.StoreRuntime.CurrentCashCents, "no cash moved on a failed settlement");
            Assert.AreEqual(2000, s.BakeryRuntime.CurrentCashCents);
            Pass3ProductLine line = s.Catalog.Find("bread");
            Assert.IsNotNull(line);
            Assert.AreEqual(24, line.BackUnits, "the bread stands in BACK, unsettled, not lost");
            Assert.AreEqual(0, line.ShelfUnits);
            Assert.IsNotNull(failed.Settlement);
            Assert.IsFalse(failed.Settlement.Settled);

            // Recovery, without re-intake (the duplicate guard would
            // refuse it): settle the returned record, then move.
            Series2WholesaleSettlementResult settled = Series2WholesaleSettlement.SettleCash(
                failed.Settlement, ProductionPort(s.StoreRuntime), ProductionPort(s.BakeryRuntime), s.Diag);
            Assert.IsTrue(settled.Settled, settled.Refusal);
            Assert.AreEqual(4952, s.StoreRuntime.CurrentCashCents);
            Assert.AreEqual(2048, s.BakeryRuntime.CurrentCashCents);
            Review2WholesaleTransferResult moved = Review2BakeryToStoreWholesaleDriver.MoveSettledLotToShelf(
                s.Catalog, "bread", 24, 5, s.Diag);
            Assert.IsTrue(moved.OnShelf, moved.Refusal);
            Assert.AreEqual(0, line.BackUnits);
            Assert.AreEqual(24, line.ShelfUnits);
            Assert.AreEqual("gs.move-back-to-shelf", moved.TaskActionId);
        }

        [Test]
        public void StockShelfToLive_CappedPort_Refuses_ThenRetrySucceeds()
        {
            Slice s = BakeAndFund(5000, 2000, 2);
            Review2WholesaleTransferResult transfer = Review2BakeryToStoreWholesaleDriver.TransferToShelf(
                TransferOptions(s, 24, 2, 5, 2, ProductionPort(s.StoreRuntime), ProductionPort(s.BakeryRuntime)), s.Diag);
            Assert.IsTrue(transfer.Transferred, transfer.Refusal);
            s.StorePort.Cap = 10;
            Review2StockingResult refused = Review2BakeryToStoreWholesaleDriver.StockShelfToLive(
                s.Catalog, "bread", 24, "staple_food", s.StorePort, s.Diag);
            Assert.IsFalse(refused.Stocked, "a live pool that can receive only 10u must refuse 24u, never partial");
            Assert.AreEqual(24, s.Catalog.Find("bread").ShelfUnits, "refused stocking leaves the Pass 3 shelf intact");
            Assert.AreEqual(0, s.StorePort.CategoryStockUnits("staple_food"));
            s.StorePort.Cap = int.MaxValue;
            Review2StockingResult stocked = Review2BakeryToStoreWholesaleDriver.StockShelfToLive(
                s.Catalog, "bread", 24, "staple_food", s.StorePort, s.Diag);
            Assert.IsTrue(stocked.Stocked, stocked.Refusal);
            Assert.AreEqual(0, s.Catalog.Find("bread").ShelfUnits);
            Assert.AreEqual(24, s.StorePort.CategoryStockUnits("staple_food"));
        }

        [Test]
        public void ConsumeShelfUnits_Refusals_DoNotMoveStock()
        {
            var line = new Pass3ProductLine { ProductId = "bread", ShelfUnits = 5, BackUnits = 0 };
            var diag = new List<string>();
            Assert.IsFalse(line.ConsumeShelfUnits(6, diag), "short shelf must refuse");
            Assert.AreEqual(5, line.ShelfUnits);
            Assert.IsFalse(line.ConsumeShelfUnits(0, diag), "zero must refuse");
            Assert.AreEqual(5, line.ShelfUnits);
            Assert.IsTrue(line.ConsumeShelfUnits(5, diag), "an exact consume must succeed");
            Assert.AreEqual(0, line.ShelfUnits);
        }
    }
}
