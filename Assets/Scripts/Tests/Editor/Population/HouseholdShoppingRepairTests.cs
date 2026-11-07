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

            public static Fixture Create(double routeMiles = 2.0)
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
                f.Journeys.AddEdge("home", "store", (float)routeMiles, "river road");

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

            public void PlanDay(System.Func<int, DailyActivityPlanner.PersonDayPlanInputs> inputsFor = null)
            {
                Planner.PlanHouseholdDay(
                    new List<int> { 1, 2 },
                    inputsFor ?? (pid => new DailyActivityPlanner.PersonDayPlanInputs
                    {
                        PersonId = pid,
                        HouseholdId = HouseholdId,
                        AgeBand = AgeBand.Adult18Plus,
                    }),
                    Day, Tracker, Budgets, Diagnostics);
            }

            public DailyActivityPlanner.PersonDayPlanInputs DefaultInputs(int personId)
            {
                return new DailyActivityPlanner.PersonDayPlanInputs
                {
                    PersonId = personId,
                    HouseholdId = HouseholdId,
                    AgeBand = AgeBand.Adult18Plus,
                };
            }

            /// <summary>Commits work-time budget directly (no reservation).</summary>
            public void DrainBudget(int personId, int minutes)
            {
                WorkTimeBudget budget = Budgets.GetOrCreate(EntityId.For(EntityKind.Person, personId));
                string rejection;
                Assert.IsTrue(budget.TryCommit(minutes, out rejection), rejection);
            }

            public int BudgetRemaining(int personId)
            {
                return Budgets.GetOrCreate(EntityId.For(EntityKind.Person, personId)).MinutesRemaining;
            }

            /// <summary>Closes the fake store early (same open days, 08:00 open).</summary>
            public void SetStoreCloseMinute(int closeMinute)
            {
                Port.Capability.Hours = new StoreTradingHours
                {
                    OpenDayOfWeekIndices = new List<int> { 0, 1, 2, 3, 4, 5 },
                    OpenMinuteOfDay = 480,
                    CloseMinuteOfDay = closeMinute,
                };
            }

            public bool HasReservation(int personId, PersonActivityKind kind, int startMinute, int endMinute, bool released)
            {
                foreach (PersonActivityReservation r in Tracker.Reservations)
                {
                    if (r != null && r.PersonId == personId && r.DayIndex == Day
                        && r.Activity == kind && r.StartMinuteOfDay == startMinute
                        && r.EndMinuteOfDay == endMinute && r.Released == released)
                    {
                        return true;
                    }
                }

                return false;
            }

            public bool ActiveReservationsOverlap(int personId)
            {
                List<PersonActivityReservation> active = Tracker.ActiveFor(personId, Day);
                for (int i = 0; i < active.Count; i++)
                {
                    for (int j = i + 1; j < active.Count; j++)
                    {
                        if (active[i].StartMinuteOfDay < active[j].EndMinuteOfDay
                            && active[j].StartMinuteOfDay < active[i].EndMinuteOfDay)
                        {
                            return true;
                        }
                    }
                }

                return false;
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

        [Test]
        public void WindowSearch_SkipsJobSeekingBlock()
        {
            Fixture f = Fixture.Create();
            f.Fund(10000);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.BreadId, 10);

            // Tomas is at work all day (non-preemptible), so Anna — the
            // unemployed preferred shopper — is the only candidate. Her
            // job-seeking block (540-720) and rest (480-510) together cover
            // every departure before 720 for the 95-minute trip.
            Assert.IsNull(f.Tracker.TryReserve(1, PersonActivityKind.Work, f.Day, 480, 600, "test work"));
            f.PlanDay(pid =>
            {
                DailyActivityPlanner.PersonDayPlanInputs inputs = f.DefaultInputs(pid);
                if (pid == 2)
                {
                    inputs.IsJobSeeker = true;
                }

                return inputs;
            });
            Assert.AreEqual(420, f.BudgetRemaining(2), "job-seeking must have committed 180 of the 600-min budget");

            List<HouseholdErrandPlan> plans = f.Coordinator.PlanDay(f.HouseholdId, f.Day, f.Context(), f.Diagnostics);
            Assert.AreEqual(1, plans.Count);
            HouseholdShoppingLoop.BatchShoppingResult batch =
                f.Coordinator.ExecutePlan(plans[0], f.Day, f.Context(), f.Loop, f.Options(), f.Diagnostics);

            Assert.IsTrue(batch.TripExecuted, "the trip must fit after the job-seeking block: " + batch.FailureReason);
            Assert.AreEqual(2, batch.ShopperPersonId, "Anna shops; Tomas is at work");
            Assert.AreEqual(10, batch.UnitsAcquiredTotal);
            Assert.IsTrue(string.Join(" | ", f.Diagnostics).Contains("departs at 720"),
                "the departure must be logged at 720, diagnostics: " + string.Join(" | ", f.Diagnostics));
            // The job-seeking block and the rest that did not overlap the
            // chosen window are intact; nothing productive was preempted.
            Assert.IsTrue(f.HasReservation(2, PersonActivityKind.JobSeeking, 540, 720, released: false),
                "job-seeking [540,720] must be intact");
            Assert.IsTrue(f.HoldsRestCoveringMinute(2, 480), "Anna's rest [480,510] must be intact (trip left at 720)");
            Assert.IsTrue(f.HasReservation(1, PersonActivityKind.Work, 480, 1080, released: false),
                "Tomas's work block must be intact");
        }

        [Test]
        public void WindowSearch_NeverPreemptsSleep()
        {
            // Long route (2.5 mi, 50 min one way, 115-min trip) and a store
            // closing at 540: the pre-check passes (arrival 530 is tradable),
            // but every departure the search can offer either overlaps
            // Anna's job-seeking block or arrives after close — so no window
            // works, and the trip must fail without touching sleep.
            Fixture f = Fixture.Create(routeMiles: 2.5);
            f.SetStoreCloseMinute(540);
            f.Fund(10000);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.BreadId, 10);

            Assert.IsNull(f.Tracker.TryReserve(1, PersonActivityKind.Work, f.Day, 480, 600, "test work"));
            f.PlanDay(pid =>
            {
                DailyActivityPlanner.PersonDayPlanInputs inputs = f.DefaultInputs(pid);
                if (pid == 2)
                {
                    inputs.IsJobSeeker = true;
                }

                return inputs;
            });

            HouseholdShoppingLoop.BatchShoppingResult batch = f.Loop.ExecuteBatch(
                new List<HouseholdPurchasingNeed> { need }, f.Day, f.Options(), "no-window-sleep-safe");

            Assert.IsFalse(batch.TripExecuted, "no window fits, so no trip may execute");
            Assert.IsTrue(batch.FailureReason.Contains("No shopper in any searched window"),
                "the search must refuse loudly with the last tried window, got: " + batch.FailureReason);
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status, "the need stays open for the next day");
            Assert.AreEqual(10000, f.Balance(), "no money moves on a refused trip");
            Assert.AreEqual(16, f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId));
            Assert.IsTrue(f.HasReservation(2, PersonActivityKind.Sleeping, 0, 420, released: false),
                "morning sleep [0,420] must be intact");
            Assert.IsTrue(f.HasReservation(2, PersonActivityKind.Sleeping, 1320, 1440, released: false),
                "evening sleep [1320,1440] must be intact");
            Assert.IsTrue(f.HasReservation(1, PersonActivityKind.Sleeping, 0, 420, released: false),
                "Tomas's morning sleep must be intact");
            int shoppingReservations = 0;
            foreach (PersonActivityReservation r in f.Tracker.Reservations)
            {
                if (r != null && r.Activity == PersonActivityKind.Shopping)
                {
                    shoppingReservations++;
                }
            }

            Assert.AreEqual(0, shoppingReservations, "no shopping window was ever reserved");
        }

        [Test]
        public void WindowSearch_SundayStaysOpen()
        {
            Fixture f = Fixture.Create();
            f.Day = 6; // Sunday (day-of-week index 6)
            f.Fund(10000);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.BreadId, 10);
            f.PlanDay();

            HouseholdShoppingLoop.BatchShoppingResult sunday = f.Loop.ExecuteBatch(
                new List<HouseholdPurchasingNeed> { need }, 6, f.Options(), "sunday-no-departure");
            Assert.IsFalse(sunday.TripExecuted, "no departure is offered on Sunday");
            Assert.IsTrue(sunday.FailureReason.Contains("closed"),
                "the Sunday failure must name the closure, got: " + sunday.FailureReason);
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status);

            // Monday the same need is served, with the same planned day.
            f.Day = 7;
            f.PlanDay();
            HouseholdShoppingLoop.BatchShoppingResult monday = f.Loop.ExecuteBatch(
                new List<HouseholdPurchasingNeed> { need }, 7, f.Options(), "monday-served");
            Assert.IsTrue(monday.TripExecuted, "Monday must serve the need: " + monday.FailureReason);
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status);
            Assert.AreEqual(10000 - 50, f.Balance());
        }

        [Test]
        public void WindowSearch_BudgetRespected()
        {
            // Preferred shopper (Anna) is budget-short even though free:
            // the other adult shops instead.
            Fixture f = Fixture.Create();
            f.Fund(10000);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.BreadId, 10);
            f.PlanDay();
            f.DrainBudget(2, 510); // Anna: 90 min left, trip needs 95.
            Assert.AreEqual(90, f.BudgetRemaining(2));
            Assert.AreEqual(600, f.BudgetRemaining(1));

            HouseholdShoppingLoop.BatchShoppingResult batch = f.Loop.ExecuteBatch(
                new List<HouseholdPurchasingNeed> { need }, f.Day, f.Options(), "budget-fallback");
            Assert.IsTrue(batch.TripExecuted, "Tomas has budget and must shop: " + batch.FailureReason);
            Assert.AreEqual(1, batch.ShopperPersonId, "the budget-short preferred shopper must not be chosen");
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status);
            Assert.IsTrue(f.HoldsRestCoveringMinute(2, 480), "Anna's rest must be untouched (she did not shop)");

            // Nobody has budget: the trip fails with the budget reason and
            // no money moves.
            Fixture g = Fixture.Create();
            g.Fund(10000);
            HouseholdPurchasingNeed need2 = g.OpenNeed(HouseholdItemCatalog.BreadId, 10);
            g.PlanDay();
            g.DrainBudget(1, 510);
            g.DrainBudget(2, 510);
            Assert.AreEqual(90, g.BudgetRemaining(1));
            Assert.AreEqual(90, g.BudgetRemaining(2));

            HouseholdShoppingLoop.BatchShoppingResult broke = g.Loop.ExecuteBatch(
                new List<HouseholdPurchasingNeed> { need2 }, g.Day, g.Options(), "budget-none");
            Assert.IsFalse(broke.TripExecuted, "no member can afford the trip time");
            Assert.IsTrue(broke.FailureReason.Contains("work minutes left"),
                "the failure must name the budget shortfall, got: " + broke.FailureReason);
            Assert.AreEqual(PurchasingNeedStatus.Open, need2.Status);
            Assert.AreEqual(10000, g.Balance(), "no money moves on a refused trip");
        }

        [Test]
        public void NoDoubleBooking()
        {
            // Plan wording note: the shopping reservation is released when
            // the trip completes (ExecuteBatch releases it before marking
            // the trip executed — the Pass V coordinated-errands test pins
            // ActiveFor == 0 after a trip). So the faithful invariant after
            // a successful trip is: exactly ONE shopping reservation was
            // ever created for the window, it is now released, and no two
            // unreleased reservations overlap for either member.
            Fixture f = Fixture.Create();
            f.Fund(10000);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.BreadId, 10);
            f.PlanDay();

            List<HouseholdErrandPlan> plans = f.Coordinator.PlanDay(f.HouseholdId, f.Day, f.Context(), f.Diagnostics);
            HouseholdShoppingLoop.BatchShoppingResult batch =
                f.Coordinator.ExecutePlan(plans[0], f.Day, f.Context(), f.Loop, f.Options(), f.Diagnostics);
            Assert.IsTrue(batch.TripExecuted, batch.FailureReason);
            int shopper = batch.ShopperPersonId;

            int shoppingCount = 0;
            PersonActivityReservation shopping = null;
            foreach (PersonActivityReservation r in f.Tracker.Reservations)
            {
                if (r != null && r.PersonId == shopper && r.DayIndex == f.Day
                    && r.Activity == PersonActivityKind.Shopping)
                {
                    shoppingCount++;
                    shopping = r;
                }
            }

            Assert.AreEqual(1, shoppingCount, "exactly one shopping reservation may be created for the trip");
            Assert.IsNotNull(shopping);
            Assert.AreEqual(480, shopping.StartMinuteOfDay, "the trip departs at 480 (rest preempted)");
            Assert.AreEqual(95, shopping.DurationMinutes, "the window is the 95-min trip estimate");
            Assert.IsTrue(shopping.Released, "the shopping reservation is released once the trip completes");
            Assert.IsFalse(f.ActiveReservationsOverlap(1), "P1 holds no overlapping reservations");
            Assert.IsFalse(f.ActiveReservationsOverlap(2), "P2 holds no overlapping reservations");
            foreach (PersonActivityReservation r in f.Tracker.ActiveFor(shopper, f.Day))
            {
                Assert.IsTrue(r.Activity != PersonActivityKind.Shopping, "no unreleased shopping reservation remains");
            }

            Assert.IsTrue(f.HasReservation(shopper, PersonActivityKind.Resting, 480, 510, released: true),
                "the shopper's [480,510] rest was released for the trip");
            Assert.IsTrue(f.HasReservation(shopper, PersonActivityKind.Resting, 420, 450, released: false),
                "the shopper's other rest was untouched");
        }
    }
}
