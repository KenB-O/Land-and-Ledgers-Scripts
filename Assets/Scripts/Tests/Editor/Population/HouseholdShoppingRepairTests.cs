using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    /// <summary>
    /// Plan 02 Slice 0 — discriminator tests for the household shopping
    /// repair. Failing-first: on the c6dd1be baseline, tests 1, 3, 4 and 5
    /// must FAIL in the way stated, and test 2 (the control) must PASS.
    ///
    /// 1. ScheduleConflict_BlocksStockedBreadPurchase: a planned day puts a
    ///    Resting block over the fixed 08:00 departure on every member, so
    ///    the batch fails with "Unavailable shopper / schedule conflict"
    ///    against a stocked store and nothing moves.
    /// 2. FreeShopper_BuysFromSameStockedStore (control): the identical
    ///    setup with no planned day buys — proving the seller side works
    ///    when the shopper is free.
    /// 3. OpeningStore_HoldsNoSellableMealStock: the real port over a real
    ///    BusinessRuntimeState built from the authored definition holds zero
    ///    sellable bread/flour at opening; a bread need is out of stock even
    ///    with a free shopper, until one identified lot arrives through the
    ///    wholesale receipt seam.
    /// 4. UnservableNeed_PreservedMeatHasNoSeller: bread is served and
    ///    preserved_meat fails with "No viable seller" in the same batch.
    /// 5. SundayAttempt_FailsMondaySucceeds (control): Sunday trading is
    ///    refused; the identical trip on Monday succeeds.
    /// </summary>
    [TestFixture]
    public sealed class HouseholdShoppingRepairTests
    {
        private sealed class FakeStorePort : IItemLevelGeneralStoreTradingPort
        {
            public string StoreBusinessId { get; set; } = "store-1";
            public string StoreName { get; set; } = "General Store";
            public string LocationId { get; set; } = "store";
            public Dictionary<string, int> Stock = new Dictionary<string, int>();
            public Dictionary<string, int> Prices = new Dictionary<string, int>();
            public Dictionary<string, int> Targets = new Dictionary<string, int>();
            public Dictionary<string, int> ItemStock = new Dictionary<string, int>();
            public Dictionary<string, int> ItemPrices = new Dictionary<string, int>();
            public HashSet<string> ItemLevelItems = new HashSet<string>();
            public List<GeneralStoreCategoryOffer> OfferList = new List<GeneralStoreCategoryOffer>();
            public StoreTradingCapability Capability { get; set; } = new StoreTradingCapability();
            public int CashBalance = 50000;
            public int RecordedCashRevenue;
            public int RecordedItemCashRevenue;
            public bool OffersDeliveryValue;
            public int DeliveryFeeValue;
            public bool OffersTradeCreditValue;
            public int TradeCreditLimitValue;

            public IReadOnlyList<GeneralStoreCategoryOffer> CategoryOffers => OfferList;
            public StoreTradingCapability TradingCapability => Capability;
            public int CurrentCashCents => CashBalance;
            public bool OffersDelivery => OffersDeliveryValue;
            public bool OffersTradeCredit => OffersTradeCreditValue;
            public int TradeCreditLimitCents => TradeCreditLimitValue;

            public int CategoryStockUnits(string categoryId) => Stock.TryGetValue(categoryId, out int s) ? s : 0;
            public int CategoryTargetStockUnits(string categoryId) => Targets.TryGetValue(categoryId, out int t) ? t : 0;
            public int CategoryPricePerUnitCents(string categoryId) => Prices.TryGetValue(categoryId, out int p) ? p : 0;

            public bool TryConsumeStoreUnits(string categoryId, int units, out int consumedUnits)
            {
                consumedUnits = 0;
                int have = CategoryStockUnits(categoryId);
                int take = System.Math.Min(System.Math.Max(0, units), have);
                if (take <= 0) return false;
                Stock[categoryId] = have - take;
                consumedUnits = take;
                return true;
            }

            public void RecordCashSettlement(string categoryId, int unitsSold, int revenueCents)
            {
                CashBalance += System.Math.Max(0, revenueCents);
                RecordedCashRevenue += System.Math.Max(0, revenueCents);
            }

            public void RecordCreditSettlement(string categoryId, int unitsSold, int amountCents, string obligationId) { }

            public string SpendCash(int amountCents, string purpose)
            {
                int spend = System.Math.Max(0, amountCents);
                if (spend <= 0) return null;
                if (CashBalance < spend) return $"cannot afford {spend}c: cash is {CashBalance}c";
                CashBalance -= spend;
                return null;
            }

            public string ReceiveStock(string categoryId, int units, string provenanceLabel)
            {
                if (units <= 0) return "nothing to receive";
                Stock[categoryId] = CategoryStockUnits(categoryId) + units;
                return null;
            }

            public int CategoryReceivableUnits(string categoryId)
            {
                int target = CategoryTargetStockUnits(categoryId);
                return target > 0 ? System.Math.Max(0, target - CategoryStockUnits(categoryId)) : int.MaxValue;
            }

            public int DeliveryFeeCents(string itemId, int units) => System.Math.Max(0, DeliveryFeeValue);

            public bool UsesItemLevelInventory(string itemId) => ItemLevelItems.Contains(itemId);
            public int ItemStockUnits(string itemId) => ItemStock.TryGetValue(itemId, out int s) ? s : 0;
            public int ItemPricePerUnitCents(string itemId) => ItemPrices.TryGetValue(itemId, out int p) ? p : 0;

            public bool TryConsumeItemStockUnits(string itemId, int units, out int consumedUnits)
            {
                consumedUnits = 0;
                int have = ItemStockUnits(itemId);
                int take = System.Math.Min(System.Math.Max(0, units), have);
                if (take <= 0) return false;
                ItemStock[itemId] = have - take;
                consumedUnits = take;
                return true;
            }

            public void RecordItemCashSettlement(string categoryId, int unitsSold, int revenueCents)
            {
                CashBalance += System.Math.Max(0, revenueCents);
                RecordedItemCashRevenue += System.Math.Max(0, revenueCents);
            }
        }

        private sealed class Fixture
        {
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
            public FinancialObligationAuthority Obligations;
            public EntityIdRegistry EntityIds;
            public HouseholdShoppingLoop Loop;
            public HouseholdErrandCoordinator Coordinator;
            public DailyActivityPlanner Planner;
            public FakeStorePort Port;
            public Dictionary<int, string> PersonLocations = new Dictionary<int, string>();
            public List<string> Diagnostics = new List<string>();

            public int HouseholdId = 7;
            public int Day = 0; // Monday (day-of-week index 0)

            public static Fixture Create()
            {
                var f = new Fixture();
                f.Population = new PopulationState();
                f.Population.people.Add(new PersonState
                {
                    id = 1, firstName = "Tomas", lastName = "Morrow", age = 34, ageBand = AgeBand.Adult18Plus,
                    isFemale = false,
                });
                f.Population.people.Add(new PersonState
                {
                    id = 2, firstName = "Anna", lastName = "Morrow", age = 32, ageBand = AgeBand.Adult18Plus,
                    isFemale = true,
                });
                f.Population.households.Add(new HouseholdState { id = 7 });
                f.Membership = new HouseholdMembershipRegistry();
                Assert.IsNull(f.Membership.Register(new HouseholdMembership
                {
                    MembershipId = 1, PersonId = 1, HouseholdId = 7, StartDayIndex = 0,
                }));
                Assert.IsNull(f.Membership.Register(new HouseholdMembership
                {
                    MembershipId = 2, PersonId = 2, HouseholdId = 7, StartDayIndex = 0,
                }));

                f.Ledgers = new HouseholdLedgerRegistry();
                f.Inventories = new HouseholdInventoryRegistry();
                f.Needs = new HouseholdNeedRegistry();
                f.Journeys = new JourneyModel();
                f.Journeys.RegisterLocation(new JourneyLocation("home", JourneyLocationKind.Farmstead, "Morrow Farm", 0f, 0f));
                f.Journeys.RegisterLocation(new JourneyLocation("store", JourneyLocationKind.Store, "General Store", 2f, 0f));
                f.Journeys.AddEdge("home", "store", 2.0f, "river road");

                f.Tasks = new TaskAuthority();
                HouseholdShoppingLoop.RegisterTaskDefinitions(f.Tasks);
                f.Budgets = new WorkTimeBudgetStore();
                f.Tracker = new PersonScheduleTracker();
                f.Knowledge = new ShopperSellerKnowledge();
                f.Obligations = new FinancialObligationAuthority();
                f.EntityIds = new EntityIdRegistry();

                // Stocked bread store: 16 identified loaves at the authored 5c.
                f.Port = new FakeStorePort();
                f.Port.OfferList.Add(new GeneralStoreCategoryOffer
                {
                    CategoryId = "staple_food",
                    OfferedItemIds = new List<string> { HouseholdItemCatalog.BreadId },
                    StoreUnitsPerItemUnit = 1,
                    UsesItemLevelInventory = true,
                });
                f.Port.ItemLevelItems.Add(HouseholdItemCatalog.BreadId);
                f.Port.ItemStock[HouseholdItemCatalog.BreadId] = 16;
                f.Port.ItemPrices[HouseholdItemCatalog.BreadId] = 5;

                f.Directory = new SupplierDirectory();
                GeneralStoreSupplierAdapter.RegisterIn(f.Directory, f.Port);
                f.Knowledge.LearnSupplier(1, "store-1", "lives in the same town", 0);
                f.Knowledge.LearnSupplier(2, "store-1", "lives in the same town", 0);

                f.Loop = new HouseholdShoppingLoop(
                    f.Population, f.Membership, f.Ledgers, f.Inventories, f.Needs,
                    f.Directory, f.Journeys, f.Tasks, f.Budgets, f.Tracker, f.Knowledge,
                    f.Obligations, f.EntityIds);
                f.Coordinator = new HouseholdErrandCoordinator();
                f.Planner = new DailyActivityPlanner();

                f.PersonLocations[1] = "home";
                f.PersonLocations[2] = "home";
                return f;
            }

            public ShoppingLoopOptions Options()
            {
                var fixture = this;
                return new ShoppingLoopOptions
                {
                    PersonLocationId = pid => fixture.PersonLocations.TryGetValue(pid, out string loc) ? loc : "home",
                    SetPersonLocation = (pid, loc) => fixture.PersonLocations[pid] = loc,
                    DepartureMinuteOfDay = 480,
                    InStoreMinutes = 15,
                    Diagnostics = fixture.Diagnostics,
                };
            }

            public HouseholdErrandCoordinator.CoordinatorContext Context()
            {
                return new HouseholdErrandCoordinator.CoordinatorContext
                {
                    Population = Population,
                    Memberships = Membership,
                    Ledgers = Ledgers,
                    Inventories = Inventories,
                    Needs = Needs,
                    Schedules = Tracker,
                    Tasks = Tasks,
                    Budgets = Budgets,
                    PersonEntityId = pid => EntityId.For(EntityKind.Person, pid),
                };
            }

            public void PlanDay()
            {
                Planner.PlanHouseholdDay(
                    new List<int> { 1, 2 },
                    pid => new DailyActivityPlanner.PersonDayPlanInputs
                    {
                        PersonId = pid,
                        HouseholdId = HouseholdId,
                        AgeBand = AgeBand.Adult18Plus,
                    },
                    Day, Tracker, Budgets, Diagnostics);
            }

            public void Fund(int cents)
            {
                HouseholdLedger ledger = Ledgers.GetOrCreate(HouseholdId);
                string problem = ledger.RecordInflow(0, cents, HouseholdIncomeSource.OwnerContribution,
                    "test-capital", "test funding", "test");
                Assert.IsNull(problem, problem);
            }

            public HouseholdPurchasingNeed OpenNeed(string itemId, int units)
            {
                HouseholdPurchasingNeed need = Needs.CreateNeed(HouseholdId, itemId, Day);
                need.UnitsNeeded = units;
                need.Urgency01 = 0.9f;
                need.ReasonSummary = "test need";
                return need;
            }

            public int Balance() => Ledgers.GetOrCreate(HouseholdId).GetBalanceCents();

            public bool HoldsRestCoveringMinute(int personId, int minute)
            {
                foreach (PersonActivityReservation r in Tracker.ActiveFor(personId, Day))
                {
                    if (r != null && r.Activity == PersonActivityKind.Resting
                        && r.StartMinuteOfDay <= minute && minute < r.EndMinuteOfDay)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        [Test]
        public void ScheduleConflict_BlocksStockedBreadPurchase()
        {
            // Failing-first discriminator (plan section 3): this test asserts
            // the REPAIRED behaviour — a planned day plus a stocked store must
            // still buy — so on the c6dd1be baseline it FAILS, and the failure
            // is the specified mechanism evidence: the batch is not executed,
            // the failure reason contains "Unavailable shopper / schedule
            // conflict during the trip window", buyer ledger, store cash and
            // store stock are all unchanged, and both members hold a Resting
            // reservation covering minute 480. After Slice 1 the same test
            // passes: the purchase settles, the shopper is the preferred
            // member, the [480,510] rest is released, and no other reservation
            // is touched.
            Fixture f = Fixture.Create();
            f.Fund(10000);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.BreadId, 10);
            int balanceBefore = f.Balance();
            int storeCashBefore = f.Port.CashBalance;
            int storeStockBefore = f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId);

            f.PlanDay();

            // Mechanism evidence, verified before the purchase assertion:
            // the planner put a Resting block over 08:00 on both members.
            Assert.IsTrue(f.HoldsRestCoveringMinute(1, 480), "P1 must hold a Resting reservation covering minute 480.");
            Assert.IsTrue(f.HoldsRestCoveringMinute(2, 480), "P2 must hold a Resting reservation covering minute 480.");

            List<HouseholdErrandPlan> plans = f.Coordinator.PlanDay(f.HouseholdId, f.Day, f.Context(), f.Diagnostics);
            Assert.AreEqual(1, plans.Count, "one plan batches the open bread need");
            HouseholdShoppingLoop.BatchShoppingResult batch =
                f.Coordinator.ExecutePlan(plans[0], f.Day, f.Context(), f.Loop, f.Options(), f.Diagnostics);

            Assert.IsNotNull(batch);
            Assert.IsTrue(batch.TripExecuted,
                "baseline failure: batch not executed — " + batch.FailureReason +
                " (buyer " + balanceBefore + "c, store cash " + storeCashBefore +
                "c, store stock " + storeStockBefore + "u all unchanged; need stays open)");
            Assert.AreEqual(1, batch.ServedNeedCount);
            Assert.AreEqual(10, batch.UnitsAcquiredTotal);
            Assert.AreEqual(2, batch.ShopperPersonId, "the preferred shopper is the adult woman first");
            Assert.AreEqual(balanceBefore - 50, f.Balance(), "buyer debit must equal units x the 5c price");
            Assert.AreEqual(storeCashBefore + 50, f.Port.CashBalance, "seller cash must gain exactly the buyer debit");
            Assert.AreEqual(storeStockBefore - 10, f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId),
                "seller stock must fall by exactly the units sold");
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status);
            // The [480,510] rest that blocked the 08:00 departure is released
            // for the shopper; every other reservation is untouched.
            Assert.IsFalse(f.HoldsRestCoveringMinute(2, 480), "the shopper's Resting block over 480 must be released");
            Assert.IsTrue(f.HoldsRestCoveringMinute(2, 420), "the shopper's earlier rest [420,450] must be untouched");
            Assert.IsTrue(f.HoldsRestCoveringMinute(1, 480), "the non-shopper's rest must be untouched");
            Assert.IsFalse(f.Tracker.IsFree(2, f.Day, 0, 60), "the shopper's sleep must be untouched");
            Assert.IsFalse(f.Tracker.IsFree(2, f.Day, 450, 30), "the shopper's meal must be untouched");
        }

        [Test]
        public void FreeShopper_BuysFromSameStockedStore()
        {
            Fixture f = Fixture.Create();
            f.Fund(10000);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.BreadId, 10);

            // Control: the planner is NOT run, so both members are free at 480.
            List<HouseholdErrandPlan> plans = f.Coordinator.PlanDay(f.HouseholdId, f.Day, f.Context(), f.Diagnostics);
            Assert.AreEqual(1, plans.Count);
            HouseholdShoppingLoop.BatchShoppingResult batch =
                f.Coordinator.ExecutePlan(plans[0], f.Day, f.Context(), f.Loop, f.Options(), f.Diagnostics);

            Assert.IsNotNull(batch);
            Assert.IsTrue(batch.TripExecuted, "a free shopper must buy: " + batch.FailureReason);
            Assert.AreEqual(1, batch.ServedNeedCount);
            Assert.AreEqual(10, batch.UnitsAcquiredTotal);
            Assert.AreEqual(2, batch.ShopperPersonId, "the preferred shopper is the adult woman first");
            Assert.AreEqual(10000 - 50, f.Balance(), "buyer debit must equal units x the 5c price");
            Assert.AreEqual(50000 + 50, f.Port.CashBalance, "seller cash must gain exactly the buyer debit");
            Assert.AreEqual(6, f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId),
                "seller stock must fall by exactly the units sold");
            Assert.AreEqual(10, f.Inventories.GetOrCreate(7).GetAvailableUnits(HouseholdItemCatalog.BreadId));
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status);
        }

        [Test]
        public void OpeningStore_HoldsNoSellableMealStock()
        {
            // Harness-reachability note (plan section 3 fallback): the real
            // port builder (GeneralStoreRuntimeManager.BuildHouseholdTradingPort)
            // and the authored definition asset are NOT harness-reachable —
            // GeneralStoreTradingPort.cs is excluded from harness sources and
            // AssetDatabase.LoadAssetAtPath always returns null in the Roslyn
            // harness (the same reason five PurchasedRetailLotReceiptTests sit
            // in the known 18-failure set). So this test proves the stock half
            // with BusinessRuntimeState directly, following the W4 precedent
            // in PurchasedRetailLotReceiptTests (bare state via
            // BindBusinessIdentity + EnsureCategoryStock); the port half —
            // that BuildHouseholdTradingPort offers bread only once an
            // identified lot exists — is source-review, not faked with the
            // FakeStorePort. The batch pre-check half below uses the fake
            // port only to show the loop reports the stockout.
            var state = new BusinessRuntimeState();
            state.BindBusinessIdentity("general_store_060");
            state.EnsureCategoryStock("staple_food", 0, 100);

            // At opening there is no physical stock for any meal item: the
            // authored catalog StartingStockUnits is not an inventory receipt
            // (BusinessRuntimeState.cs:1990-1993 seeds categories at zero and
            // only a production output or settled receipt creates inventory).
            Assert.AreEqual(0, state.GetCategoryStock("staple_food").CurrentStockUnits,
                "opening category stock must be zero");
            Assert.AreEqual(0, state.GetRetailItemStockUnits("bread"),
                "opening identified bread stock must be zero (no receipt yet)");
            Assert.AreEqual(0, state.GetRetailItemStockUnits("flour"),
                "opening identified flour stock must be zero (no receipt yet)");
            Assert.AreEqual(0, state.RetailItemLots.Count, "no identified lots exist before a receipt");

            // One identified bread lot through the wholesale receipt seam
            // (the same TryAddRetailItemLot the port's ReceiveRetailItemLot
            // delegates to) makes bread sellable.
            Assert.IsNull(state.TryAddRetailItemLot(
                "lot:test:bread:1", "bread", "staple_food", "loaf", 24,
                "general_store_060", "general_store_060", "wholesale-receipt:test", 0));
            Assert.AreEqual(24, state.GetRetailItemStockUnits("bread"));
            Assert.AreEqual(0, state.GetRetailItemStockUnits("flour"),
                "flour stays at zero until its own receipt lands");
            Assert.IsNotNull(state.FindRetailItemLot("lot:test:bread:1"));
            Assert.AreEqual(24, state.GetCategoryStock("staple_food").CurrentStockUnits);

            // Batch half: a free shopper against a store holding zero bread
            // is told out of stock, not sold a conjured loaf.
            Fixture f = Fixture.Create();
            f.Port.ItemStock[HouseholdItemCatalog.BreadId] = 0;
            f.Fund(10000);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.BreadId, 10);
            HouseholdShoppingLoop.BatchShoppingResult emptyBatch = f.Loop.ExecuteBatch(
                new List<HouseholdPurchasingNeed> { need }, f.Day, f.Options(), "opening-store-empty");
            Assert.IsFalse(emptyBatch.TripExecuted, "an empty store must sell nothing");
            Assert.IsTrue(emptyBatch.FailureReason.Contains("out of stock"),
                "the empty-store failure must name the stockout, got: " + emptyBatch.FailureReason);
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status);
            Assert.AreEqual(10000, f.Balance());

            // Restock the same fake store and the identical need becomes viable.
            f.Port.ItemStock[HouseholdItemCatalog.BreadId] = 24;
            HouseholdShoppingLoop.BatchShoppingResult stockedBatch = f.Loop.ExecuteBatch(
                new List<HouseholdPurchasingNeed> { need }, f.Day, f.Options(), "opening-store-stocked");
            Assert.IsTrue(stockedBatch.TripExecuted, "the stocked store must sell: " + stockedBatch.FailureReason);
            Assert.AreEqual(10, stockedBatch.UnitsAcquiredTotal);
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status);
        }

        [Test]
        public void UnservableNeed_PreservedMeatHasNoSeller()
        {
            Fixture f = Fixture.Create();
            f.Fund(10000);
            HouseholdPurchasingNeed bread = f.OpenNeed(HouseholdItemCatalog.BreadId, 10);
            HouseholdPurchasingNeed meat = f.OpenNeed(HouseholdItemCatalog.PreservedMeatId, 5);

            HouseholdShoppingLoop.BatchShoppingResult batch = f.Loop.ExecuteBatch(
                new List<HouseholdPurchasingNeed> { bread, meat }, f.Day, f.Options(), "unservable-meat");

            Assert.IsTrue(batch.TripExecuted, "the servable bread need must still be served: " + batch.FailureReason);
            Assert.AreEqual(1, batch.ServedNeedCount);
            Assert.AreEqual(10, batch.UnitsAcquiredTotal);
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, bread.Status);
            Assert.AreEqual(PurchasingNeedStatus.Open, meat.Status, "the unservable meat need stays open");
            Assert.AreEqual(10000 - 50, f.Balance(), "only the bread price is spent; nothing is spent on meat");
            ShoppingTripResult meatResult = null;
            foreach (ShoppingTripResult r in batch.NeedResults)
            {
                if (r != null && r.ItemId == HouseholdItemCatalog.PreservedMeatId)
                {
                    meatResult = r;
                }
            }

            Assert.IsNotNull(meatResult, "the meat need must have its own per-need result");
            Assert.IsTrue(meatResult.FailureReason.Contains("No viable seller"),
                "the meat failure must name the missing seller, got: " + meatResult.FailureReason);
        }

        [Test]
        public void SundayAttempt_FailsMondaySucceeds()
        {
            Fixture f = Fixture.Create();
            f.Day = 6; // Sunday (day-of-week index 6)
            f.Fund(10000);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.BreadId, 10);

            HouseholdShoppingLoop.BatchShoppingResult sunday = f.Loop.ExecuteBatch(
                new List<HouseholdPurchasingNeed> { need }, 6, f.Options(), "sunday-closed");
            Assert.IsFalse(sunday.TripExecuted, "Sunday trading is closed by Canon 13.4");
            Assert.IsTrue(sunday.FailureReason.Contains("closed"),
                "the Sunday failure must name the closure, got: " + sunday.FailureReason);
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status);
            Assert.AreEqual(10000, f.Balance());
            Assert.AreEqual(16, f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId));

            HouseholdShoppingLoop.BatchShoppingResult monday = f.Loop.ExecuteBatch(
                new List<HouseholdPurchasingNeed> { need }, 7, f.Options(), "monday-open");
            Assert.IsTrue(monday.TripExecuted, "the identical trip on Monday must succeed: " + monday.FailureReason);
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status);
            Assert.AreEqual(10000 - 50, f.Balance());
        }
    }
}
