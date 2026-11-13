using System;
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
    /// Plan 02 Slice 4 — end-to-end acceptance for the household shopping
    /// repair. Tests only: if any test here exposes a production defect,
    /// STOP — file the defect with the failing test and do NOT improvise a
    /// repair inside this acceptance slice.
    ///
    /// The fixture drives the full daily sequence in order for FIVE
    /// households (H7-H11, two adults each, real ledgers, inventories,
    /// needs, schedules, budgets, and a found foot route home→store):
    /// DailyActivityPlanner → shortage monitor (Slice 2 recognition
    /// filter) → HouseholdErrandCoordinator.PlanDay → ExecutePlan →
    /// next-day DailyNeedsService lot-based meal resolution. The store is a
    /// stocked bread store (identified lots, 5c/loaf) plus flour sacks
    /// (35c/sack, 1 sack = 10 lb), driven through the REAL
    /// GeneralStoreSupplierAdapter and the REAL HouseholdShoppingLoop.
    ///
    /// Sizing (from the live rules, not assumed): foot speed 3 mph, so a
    /// 2.0 mi home→store route is 40 min one-way; trip estimate 80 + 15
    /// in-store = 95 min; departure 480. MealSchedulingPolicy.Default
    /// expects 3 meals/day/person (Morning + Evening + midday), bread
    /// serving 1 loaf, flour serving 2 lb — so a 2-person household's
    /// expected daily use is 6 loaves and 12 lb of flour, and the monitor
    /// opens a 6-loaf bread need from zero stock. Meals actually served are
    /// Morning + Evening (2/person/day; the midday slot needs 3 days of
    /// supply), so 4 loaves/day/household are eaten and the need re-opens
    /// the next day for 4 more loaves.
    /// </summary>
    [TestFixture]
    public sealed class HouseholdShoppingEndToEndTests
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

            /// <summary>Slice 4 retry edge: when set, the next item-stock
            /// consume fails without moving stock (stock moved between the
            /// counter check and the sale), then clears itself.</summary>
            public bool FailNextConsume;

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
                int take = Math.Min(Math.Max(0, units), have);
                if (take <= 0) return false;
                Stock[categoryId] = have - take;
                consumedUnits = take;
                return true;
            }

            public void RecordCashSettlement(string categoryId, int unitsSold, int revenueCents)
            {
                CashBalance += Math.Max(0, revenueCents);
                RecordedCashRevenue += Math.Max(0, revenueCents);
            }

            public void RecordCreditSettlement(string categoryId, int unitsSold, int amountCents, string obligationId) { }

            public string SpendCash(int amountCents, string purpose)
            {
                int spend = Math.Max(0, amountCents);
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
                return target > 0 ? Math.Max(0, target - CategoryStockUnits(categoryId)) : int.MaxValue;
            }

            public int DeliveryFeeCents(string itemId, int units) => Math.Max(0, DeliveryFeeValue);

            public bool UsesItemLevelInventory(string itemId) => ItemLevelItems.Contains(itemId);
            public int ItemStockUnits(string itemId) => ItemStock.TryGetValue(itemId, out int s) ? s : 0;
            public int ItemPricePerUnitCents(string itemId) => ItemPrices.TryGetValue(itemId, out int p) ? p : 0;

            public bool TryConsumeItemStockUnits(string itemId, int units, out int consumedUnits)
            {
                consumedUnits = 0;
                if (FailNextConsume)
                {
                    FailNextConsume = false;
                    return false;
                }

                int have = ItemStockUnits(itemId);
                int take = Math.Min(Math.Max(0, units), have);
                if (take <= 0) return false;
                ItemStock[itemId] = have - take;
                consumedUnits = take;
                return true;
            }

            public void RecordItemCashSettlement(string categoryId, int unitsSold, int revenueCents)
            {
                CashBalance += Math.Max(0, revenueCents);
                RecordedItemCashRevenue += Math.Max(0, revenueCents);
            }
        }

        private sealed class LocationHop
        {
            public int PersonId;
            public string LocationId;
        }

        private sealed class Fixture
        {
            public PopulationState Population;
            public HouseholdMembershipRegistry Membership;
            public HouseholdLedgerRegistry Ledgers;
            public HouseholdInventoryRegistry Inventories;
            public HouseholdNeedRegistry Needs;
            public SupplierDirectory Directory;
            public GeneralStoreSupplierAdapter Adapter;
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
            public DailyNeedsService DailyNeeds;
            public HouseholdConsumptionPlanner MealPlanner;
            public HouseholdMealLogRegistry MealLog;
            public MealSchedulingPolicy MealPolicy;
            public FakeStorePort Port;
            public Dictionary<int, string> PersonLocations = new Dictionary<int, string>();
            public Dictionary<int, List<int>> Members = new Dictionary<int, List<int>>();
            public Dictionary<int, string> Homes = new Dictionary<int, string>();
            public List<LocationHop> Transitions = new List<LocationHop>();
            public List<string> Diagnostics = new List<string>();
            public OffMapJourneyAuthority OffMapJourneys;

            public int Day = 0; // Monday (day-of-week index 0)

            public static readonly int[] HouseholdIds = { 7, 8, 9, 10, 11 };

            public static Fixture Create()
            {
                var f = new Fixture();
                f.Population = new PopulationState();
                f.Journeys = new JourneyModel();
                f.Journeys.RegisterLocation(new JourneyLocation("store", JourneyLocationKind.Store, "General Store", 2f, 0f));
                AddHousehold(f, 7, "home-7", 0f,
                    (1, "Tomas", "Morrow", 34, false), (2, "Anna", "Morrow", 32, true));
                AddHousehold(f, 8, "home-8", 1f,
                    (3, "Piotr", "Kowalski", 41, false), (4, "Marta", "Kowalski", 38, true));
                AddHousehold(f, 9, "home-9", 2f,
                    (5, "Jonas", "Halvorsen", 29, false), (6, "Ingrid", "Halvorsen", 27, true));
                AddHousehold(f, 10, "home-10", 3f,
                    (7, "Mateo", "Ferreira", 36, false), (8, "Lucia", "Ferreira", 33, true));
                AddHousehold(f, 11, "home-11", 4f,
                    (9, "Henrik", "Lindqvist", 45, false), (10, "Freya", "Lindqvist", 43, true));

                f.Membership = new HouseholdMembershipRegistry();
                int membershipId = 1;
                foreach (int householdId in HouseholdIds)
                {
                    foreach (int personId in f.Members[householdId])
                    {
                        Assert.IsNull(f.Membership.Register(new HouseholdMembership
                        {
                            MembershipId = membershipId++,
                            PersonId = personId,
                            HouseholdId = householdId,
                            StartDayIndex = 0,
                        }));
                    }
                }

                f.Ledgers = new HouseholdLedgerRegistry();
                f.Inventories = new HouseholdInventoryRegistry();
                f.Needs = new HouseholdNeedRegistry();
                foreach (int householdId in HouseholdIds)
                {
                    f.Journeys.AddEdge(f.Homes[householdId], "store", 2.0f, "river road");
                }

                f.Tasks = new TaskAuthority();
                HouseholdShoppingLoop.RegisterTaskDefinitions(f.Tasks);
                f.Budgets = new WorkTimeBudgetStore();
                f.Tracker = new PersonScheduleTracker();
                f.Knowledge = new ShopperSellerKnowledge();
                f.Obligations = new FinancialObligationAuthority();
                f.EntityIds = new EntityIdRegistry();

                // Stocked store: 200 identified loaves at the authored 5c,
                // and 5 flour sacks at 35c/sack (1 sack = 10 lb).
                f.Port = new FakeStorePort();
                f.Port.OfferList.Add(new GeneralStoreCategoryOffer
                {
                    CategoryId = "staple_food",
                    OfferedItemIds = new List<string> { HouseholdItemCatalog.BreadId },
                    StoreUnitsPerItemUnit = 1,
                    HouseholdUnitsPerStoreUnit = 1,
                    UsesItemLevelInventory = true,
                });
                f.Port.OfferList.Add(new GeneralStoreCategoryOffer
                {
                    CategoryId = "staple_food",
                    OfferedItemIds = new List<string> { HouseholdItemCatalog.FlourId },
                    StoreUnitsPerItemUnit = 1,
                    HouseholdUnitsPerStoreUnit = 10,
                    UsesItemLevelInventory = true,
                });
                f.Port.ItemLevelItems.Add(HouseholdItemCatalog.BreadId);
                f.Port.ItemLevelItems.Add(HouseholdItemCatalog.FlourId);
                f.Port.ItemStock[HouseholdItemCatalog.BreadId] = 200;
                f.Port.ItemPrices[HouseholdItemCatalog.BreadId] = 5;
                f.Port.ItemStock[HouseholdItemCatalog.FlourId] = 5;
                f.Port.ItemPrices[HouseholdItemCatalog.FlourId] = 35;

                f.Directory = new SupplierDirectory();
                f.Adapter = new GeneralStoreSupplierAdapter(f.Port);
                f.Directory.Register(f.Adapter);
                for (int personId = 1; personId <= 10; personId++)
                {
                    f.Knowledge.LearnSupplier(personId, "store-1", "lives in the same town", 0);
                }

                f.Loop = new HouseholdShoppingLoop(
                    f.Population, f.Membership, f.Ledgers, f.Inventories, f.Needs,
                    f.Directory, f.Journeys, f.Tasks, f.Budgets, f.Tracker, f.Knowledge,
                    f.Obligations, f.EntityIds);
                f.Coordinator = new HouseholdErrandCoordinator();
                f.Planner = new DailyActivityPlanner();
                f.DailyNeeds = new DailyNeedsService();
                f.MealPlanner = new HouseholdConsumptionPlanner();
                f.MealLog = new HouseholdMealLogRegistry();
                f.MealPolicy = MealSchedulingPolicy.Default;
                return f;
            }

            private static void AddHousehold(
                Fixture f, int householdId, string homeLocationId, float homeY,
                (int id, string first, string last, int age, bool female) adultMan,
                (int id, string first, string last, int age, bool female) adultWoman)
            {
                f.Population.people.Add(new PersonState
                {
                    id = adultMan.id, firstName = adultMan.first, lastName = adultMan.last,
                    age = adultMan.age, ageBand = AgeBand.Adult18Plus, isFemale = adultMan.female,
                    laborAccessLevel = LaborAccessLevel.FullLaborMarket,
                });
                f.Population.people.Add(new PersonState
                {
                    id = adultWoman.id, firstName = adultWoman.first, lastName = adultWoman.last,
                    age = adultWoman.age, ageBand = AgeBand.Adult18Plus, isFemale = adultWoman.female,
                    laborAccessLevel = LaborAccessLevel.FullLaborMarket,
                });
                f.Population.households.Add(new HouseholdState { id = householdId });
                f.Members[householdId] = new List<int> { adultMan.id, adultWoman.id };
                f.Homes[householdId] = homeLocationId;
                f.Journeys.RegisterLocation(new JourneyLocation(homeLocationId, JourneyLocationKind.Farmstead, homeLocationId, 0f, homeY));
                f.PersonLocations[adultMan.id] = homeLocationId;
                f.PersonLocations[adultWoman.id] = homeLocationId;
            }

            public void BeginDay(int dayIndex)
            {
                Day = dayIndex;
                // Mirrors the production drive (SimulationDrivers OnDayChanged):
                // the work-time budget rolls over to the new day.
                Budgets.EnsureDay(dayIndex);
            }

            public ShoppingLoopOptions Options()
            {
                var fixture = this;
                return new ShoppingLoopOptions
                {
                    PersonLocationId = pid => fixture.PersonLocations.TryGetValue(pid, out string loc) ? loc : "home",
                    SetPersonLocation = (pid, loc) =>
                    {
                        fixture.PersonLocations[pid] = loc;
                        fixture.Transitions.Add(new LocationHop { PersonId = pid, LocationId = loc });
                    },
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
                    OffMapJourneys = OffMapJourneys,
                };
            }

            public Dictionary<string, int> ExpectedDailyUse(int householdId, bool includeFlour = false)
            {
                int members = Members[householdId].Count;
                int mealsPerDay = MealPolicy.ExpectedMealsPerDay;
                var use = new Dictionary<string, int>
                {
                    { HouseholdItemCatalog.BreadId, members * MealPolicy.GetServingUnits(HouseholdItemCatalog.BreadId) * mealsPerDay },
                };
                if (includeFlour)
                {
                    // Flour only when a test opts in (the flour edge): the
                    // main acceptance and the repeat isolate the bread
                    // transaction, so their batch serves exactly one need.
                    use.Add(HouseholdItemCatalog.FlourId,
                        members * MealPolicy.GetServingUnits(HouseholdItemCatalog.FlourId) * mealsPerDay);
                }

                return use;
            }

            public void PlanHouseholdDay(int householdId, int dayIndex)
            {
                var memberIds = Members[householdId];
                Planner.PlanHouseholdDay(
                    new List<int>(memberIds),
                    pid => new DailyActivityPlanner.PersonDayPlanInputs
                    {
                        PersonId = pid,
                        HouseholdId = householdId,
                        AgeBand = AgeBand.Adult18Plus,
                    },
                    dayIndex, Tracker, Budgets, Diagnostics);
            }

            public void EvaluateMonitor(int householdId, int dayIndex, bool includeFlour = false)
            {
                // This scenario explicitly exercises a one-day reserve; production defaults may differ.
                var monitor = new HouseholdShortageMonitor(Needs) { LowDaysThreshold = 1f, TargetDaysThreshold = 1f };
                monitor.Evaluate(
                    Population.GetHousehold(householdId),
                    Inventories.GetOrCreate(householdId),
                    ExpectedDailyUse(householdId, includeFlour),
                    isColdSeason: false,
                    supplyAccess: new HouseholdSupplyAccess(),
                    upcomingObligationCents: 0,
                    expectedIncomeCents: 0,
                    dayIndex: dayIndex,
                    isRecognizedByKnownSeller: HouseholdShortageMonitor.KnownSellerRecognitionPredicate(
                        Directory, Knowledge, Members[householdId]));
                foreach (string line in monitor.Diagnostics)
                {
                    Diagnostics.Add(line);
                }
            }

            /// <summary>One household's full daily sequence: plan the day,
            /// raise needs from low stock, plan errands, execute the trip.</summary>
            public HouseholdShoppingLoop.BatchShoppingResult RunDayFor(int householdId, int dayIndex, bool includeFlour = false)
            {
                PlanHouseholdDay(householdId, dayIndex);
                EvaluateMonitor(householdId, dayIndex, includeFlour);
                return ShopHousehold(householdId, dayIndex);
            }

            /// <summary>Coordinator plan + execute for one household (no
            /// planner/monitor — for re-runs and retries against an already
            /// open need).</summary>
            public HouseholdShoppingLoop.BatchShoppingResult ShopHousehold(int householdId, int dayIndex)
            {
                List<HouseholdErrandPlan> plans = Coordinator.PlanDay(householdId, dayIndex, Context(), Diagnostics);
                if (plans == null || plans.Count == 0)
                {
                    return null;
                }

                Assert.AreEqual(1, plans.Count, $"H{householdId} day {dayIndex}: one plan batches its open needs");
                return Coordinator.ExecutePlan(plans[0], dayIndex, Context(), Loop, Options(), Diagnostics);
            }

            public DailyNeedsService.DayReport RunMeals(int dayIndex)
            {
                var mealDiagnostics = new List<string>();
                DailyNeedsService.DayReport report = DailyNeeds.ExecuteDay(
                    Population, MealPlanner, null, dayIndex, mealDiagnostics,
                    Budgets, inventories: Inventories, mealLog: MealLog,
                    mealPolicy: MealPolicy, memberships: Membership);
                Diagnostics.AddRange(mealDiagnostics);
                return report;
            }

            public void Fund(int householdId, int cents)
            {
                HouseholdLedger ledger = Ledgers.GetOrCreate(householdId);
                string problem = ledger.RecordInflow(0, cents, HouseholdIncomeSource.OwnerContribution,
                    "test-capital", "test funding", "test");
                Assert.IsNull(problem, problem);
            }

            public void FundAll(int cents)
            {
                foreach (int householdId in HouseholdIds)
                {
                    Fund(householdId, cents);
                }
            }

            public int Balance(int householdId) => Ledgers.GetOrCreate(householdId).GetBalanceCents();

            public HouseholdPurchasingNeed BreadNeed(int householdId) =>
                Needs.GetOpenNeed(householdId, HouseholdItemCatalog.BreadId);

            public HouseholdPurchasingNeed FlourNeed(int householdId) =>
                Needs.GetOpenNeed(householdId, HouseholdItemCatalog.FlourId);

            public HouseholdReserveState Reserve(int householdId) =>
                Population.GetHousehold(householdId).GetReserve("staple_food");

            public int BreadUnits(int householdId) =>
                Inventories.GetOrCreate(householdId).GetAvailableUnits(HouseholdItemCatalog.BreadId);

            public HouseholdItemLot BreadLot(int householdId)
            {
                foreach (HouseholdItemLot lot in Inventories.GetOrCreate(householdId).Lots)
                {
                    if (lot != null && string.Equals(lot.ItemId, HouseholdItemCatalog.BreadId, StringComparison.Ordinal))
                    {
                        return lot;
                    }
                }

                return null;
            }

            public int TotalCash()
            {
                int total = Port.CashBalance;
                foreach (int householdId in HouseholdIds)
                {
                    total += Balance(householdId);
                }

                return total;
            }

            public int TotalBread()
            {
                int total = Port.ItemStockUnits(HouseholdItemCatalog.BreadId);
                foreach (int householdId in HouseholdIds)
                {
                    total += BreadUnits(householdId);
                }

                return total;
            }

            public int CountNeeds(int householdId, string itemId)
            {
                var all = new List<HouseholdPurchasingNeed>();
                Needs.ExportState(all);
                int count = 0;
                foreach (HouseholdPurchasingNeed need in all)
                {
                    if (need != null && need.HouseholdId == householdId
                        && string.Equals(need.ItemId, itemId, StringComparison.Ordinal))
                    {
                        count++;
                    }
                }

                return count;
            }

            public ShoppingTripResult FindNeedResult(HouseholdShoppingLoop.BatchShoppingResult batch, string itemId)
            {
                if (batch?.NeedResults == null) return null;
                foreach (ShoppingTripResult r in batch.NeedResults)
                {
                    if (r != null && string.Equals(r.ItemId, itemId, StringComparison.Ordinal))
                    {
                        return r;
                    }
                }

                return null;
            }

            public bool HasEvent(ShoppingTripResult result, ShoppingEventKind kind)
            {
                if (result?.Events == null) return false;
                foreach (ShoppingDiagnosticEvent e in result.Events)
                {
                    if (e != null && e.Kind == kind) return true;
                }

                return false;
            }

            public bool HasShoppingReservation(int personId, int dayIndex, int startMinute, int endMinute, bool released)
            {
                foreach (PersonActivityReservation r in Tracker.Reservations)
                {
                    if (r != null && r.PersonId == personId && r.DayIndex == dayIndex
                        && r.Activity == PersonActivityKind.Shopping
                        && r.StartMinuteOfDay == startMinute && r.EndMinuteOfDay == endMinute
                        && r.Released == released)
                    {
                        return true;
                    }
                }

                return false;
            }

            public int ShoppingReservationCount(int personId, int dayIndex)
            {
                int count = 0;
                foreach (PersonActivityReservation r in Tracker.Reservations)
                {
                    if (r != null && r.PersonId == personId && r.DayIndex == dayIndex
                        && r.Activity == PersonActivityKind.Shopping)
                    {
                        count++;
                    }
                }

                return count;
            }

            public bool HoldsRestCoveringMinute(int personId, int dayIndex, int minute)
            {
                foreach (PersonActivityReservation r in Tracker.ActiveFor(personId, dayIndex))
                {
                    if (r != null && r.Activity == PersonActivityKind.Resting
                        && r.StartMinuteOfDay <= minute && minute < r.EndMinuteOfDay)
                    {
                        return true;
                    }
                }

                return false;
            }

            public bool DiagnosticsContain(string needle)
            {
                foreach (string line in Diagnostics)
                {
                    if (line != null && line.Contains(needle)) return true;
                }

                return false;
            }
        }

        [Test]
        public void EndToEnd_H1_BreadPurchaseToMeal()
        {
            // The plan's 10-point acceptance on ONE transaction (H1, bread),
            // with the other four households shopping the same day.
            Fixture f = Fixture.Create();
            f.FundAll(10000);
            f.BeginDay(0);

            // --- (1) a valid open bread need, raised by the shortage
            // monitor from low stock (zero loaves, 6/day expected use).
            foreach (int householdId in Fixture.HouseholdIds)
            {
                f.PlanHouseholdDay(householdId, 0);
                f.EvaluateMonitor(householdId, 0);
            }

            HouseholdPurchasingNeed h1Need = f.BreadNeed(7);
            Assert.IsNotNull(h1Need, "the monitor must raise an open bread need for H1 from zero stock");
            Assert.AreEqual(6, h1Need.UnitsNeeded, "2 members x 1 loaf serving x 3 meals/day = 6 loaves needed");

            HouseholdReserveState reserveBefore = f.Reserve(7);
            Assert.IsNotNull(reserveBefore, "H1 must hold a staple_food reserve");
            reserveBefore.currentUnits = 0;
            int reserveTarget = reserveBefore.targetUnits;
            Assert.Greater(reserveTarget, 0, "the staple_food target must be computed from the catalog, not assumed");

            int balanceBefore = f.Balance(7);
            int storeCashBefore = f.Port.CashBalance;
            int storeStockBefore = f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId);
            int totalCashBefore = f.TotalCash();
            int totalBreadBefore = f.TotalBread();
            Assert.AreEqual(10000, balanceBefore);
            Assert.AreEqual(50000, storeCashBefore);
            Assert.AreEqual(200, storeStockBefore);

            // --- (4) before settlement: the store can trade at the arrival
            // minute (480 departure + 40 min for the 2.0 mi foot route) and
            // holds stock.
            Assert.IsNull(f.Adapter.CheckTradingCapability(0, 520),
                "the store must be tradable at the 520 arrival minute");
            Assert.Greater(f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId), 0,
                "the store must hold bread before settlement");

            // --- shop all five households on the same day.
            var batches = new Dictionary<int, HouseholdShoppingLoop.BatchShoppingResult>();
            foreach (int householdId in Fixture.HouseholdIds)
            {
                HouseholdShoppingLoop.BatchShoppingResult batch = f.ShopHousehold(householdId, 0);
                Assert.IsNotNull(batch, $"H{householdId} must produce a batch");
                batches[householdId] = batch;
            }

            HouseholdShoppingLoop.BatchShoppingResult h1Batch = batches[7];
            Assert.IsTrue(h1Batch.TripExecuted, "H1's trip must execute: " + h1Batch.FailureReason);
            Assert.AreEqual(1, h1Batch.ServedNeedCount);
            Assert.AreEqual(6, h1Batch.UnitsAcquiredTotal);

            // --- (2) the shopper is an eligible household member with a real reservation.
            CollectionAssert.Contains(new[] { 1, 2 }, h1Batch.ShopperPersonId, "an eligible household member must shop");
            Assert.AreEqual(1, f.ShoppingReservationCount(h1Batch.ShopperPersonId, 0),
                "exactly one shopping reservation may ever be created for the shopper that day");
            Assert.IsTrue(f.HasShoppingReservation(h1Batch.ShopperPersonId, 0, 480, 575, released: true),
                "the shopper must hold the [480,575] shopping reservation (released on completion)");
            Assert.IsTrue(!f.HoldsRestCoveringMinute(h1Batch.ShopperPersonId, 0, 480),
                "the shopper's [480,510] rest that blocked the 08:00 departure must be released");
            Assert.IsTrue(f.HoldsRestCoveringMinute(h1Batch.ShopperPersonId, 0, 420),
                "the shopper's earlier rest [420,450] must be untouched");

            // --- (3) outbound/in-store/return legs execute and the
            // shopper's location moves home → store → home. The batch path
            // records its legs in a local legsResult and surfaces no
            // JourneyLeg event on the per-need result (that event exists
            // only on the single-need/driver paths), so leg execution is
            // proven the way the batch itself proves it: any leg failure
            // (StartTask / RecordWork) returns the batch early with
            // TripExecuted false — and TripExecuted is true here — plus the
            // location moves and the Receipt event below, which can only
            // fire after the return leg brought the shopper home.
            ShoppingTripResult breadResult = f.FindNeedResult(h1Batch, HouseholdItemCatalog.BreadId);
            Assert.IsNotNull(breadResult, "H1's batch must carry its own bread result");
            var hops = new List<string>();
            foreach (LocationHop hop in f.Transitions)
            {
                if (hop.PersonId == h1Batch.ShopperPersonId) hops.Add(hop.LocationId);
            }

            Assert.AreEqual(2, hops.Count, "the shopper must move exactly twice: to the store, then home");
            Assert.AreEqual("store", hops[0], "first move is home → store");
            Assert.AreEqual("home-7", hops[1], "second move is store → home");

            // --- (5) buyer outflow == units sold x 5c, receipt recorded.
            Assert.IsTrue(breadResult.Success, breadResult.FailureReason);
            Assert.AreEqual(6, breadResult.UnitsAcquired);
            Assert.AreEqual(30, breadResult.BuyerOutflowCents, "6 loaves x 5c");
            Assert.IsTrue(!string.IsNullOrEmpty(breadResult.ReceiptId), "the settlement must record a receipt id");
            Assert.IsTrue(string.IsNullOrEmpty(breadResult.PartialFillReason),
                "a fully fulfilled trip carries no partial-fill reason");

            // --- (6) store cash += outflow exactly, store stock -= units,
            // buyer outflow == store cash in (cash path, no receivable).
            // These are five-household totals: the point-(5) assertions
            // above already pin H1's own 30c against its result; here the
            // store must show exactly the five 30c sales and nothing else.
            Assert.AreEqual(storeCashBefore + 150, f.Port.CashBalance,
                "seller cash must gain exactly the five households' 30c each");
            Assert.AreEqual(storeStockBefore - 30, f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId),
                "seller stock must fall by exactly the 30 loaves sold");
            Assert.AreEqual(breadResult.BuyerOutflowCents, breadResult.StoreCashInCents,
                "buyer outflow must equal store cash in");
            Assert.AreEqual(0, breadResult.StoreReceivableCents, "cash settlement creates no receivable");
            Assert.AreEqual(balanceBefore - 30, f.Balance(7), "buyer debit must equal units x the 5c price");

            // --- (7) household inventory gains a lot with purchase
            // provenance / receipt id; custody reaches ReceivedAtHousehold.
            HouseholdItemLot lot = f.BreadLot(7);
            Assert.IsNotNull(lot, "H1 must hold a bread lot after the purchase");
            Assert.AreEqual(6, lot.QuantityUnits, "UnitsAcquired must equal the settled units");
            Assert.IsTrue(lot.SourceLabel.Contains("purchased 6u bread from"),
                "the lot must carry purchase provenance, got: " + lot.SourceLabel);
            Assert.IsTrue(lot.SourceLabel.Contains(breadResult.ReceiptId),
                "the lot provenance must carry the receipt id, got: " + lot.SourceLabel);
            Assert.IsTrue(f.HasEvent(breadResult, ShoppingEventKind.Custody),
                "the custody transfer into the shopper's hands must be recorded");
            Assert.IsTrue(f.HasEvent(breadResult, ShoppingEventKind.Receipt),
                "the physical receipt at the household must be recorded");
            Assert.AreEqual(0, f.Loop.ActiveCustodyBatches.Count,
                "no custody batch may be left stranded after receipt");
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, h1Need.Status);
            Assert.AreEqual(0, h1Need.UnitsNeeded);

            // --- (9, receipt half) the reserve mirror credits the receipt.
            Assert.AreEqual(Math.Min(reserveTarget, 6), reserveBefore.currentUnits,
                "the receipt must credit the reserve by the units received (clamped to target)");

            // --- (10, pre-meals) total cash and total bread conserved.
            Assert.AreEqual(totalCashBefore, f.TotalCash(), "cash must move, not be created or destroyed");
            Assert.AreEqual(totalBreadBefore, f.TotalBread(), "bread must move, not be created or destroyed");

            // The other four households bought the same way.
            foreach (int householdId in new[] { 8, 9, 10, 11 })
            {
                HouseholdShoppingLoop.BatchShoppingResult batch = batches[householdId];
                Assert.IsTrue(batch.TripExecuted, $"H{householdId} must buy: " + batch.FailureReason);
                Assert.AreEqual(6, batch.UnitsAcquiredTotal);
                Assert.AreEqual(PurchasingNeedStatus.Fulfilled, f.Needs.GetOpenNeed(householdId, HouseholdItemCatalog.BreadId) == null
                    ? PurchasingNeedStatus.Fulfilled : PurchasingNeedStatus.Open,
                    $"H{householdId}'s bread need must be fulfilled");
                Assert.AreEqual(6, f.BreadUnits(householdId));
            }

            // --- next-day meal resolution (day 1): the purchased lot feeds
            // real meals.
            int receivedLotId = lot.LotId;
            DailyNeedsService.DayReport meals = f.RunMeals(1);
            Assert.AreEqual(20, meals.MealsEaten,
                "5 households x 2 persons x Morning + Evening = 20 meals served from the purchased bread");
            Assert.AreEqual(2, f.BreadUnits(7), "H1 ate 4 of its 6 loaves (Morning + Evening for 2 persons)");

            // --- (8) at least one meal served from the purchased lot, and
            // the meal log record references that exact lot id.
            IReadOnlyList<HouseholdMealRecord> h1Meals = f.MealLog.GetMeals(7);
            Assert.Greater(h1Meals.Count, 0, "H1 must have eaten at least one meal from the purchased bread");
            bool lotReferenced = false;
            foreach (HouseholdMealRecord record in h1Meals)
            {
                if (record == null || record.ItemsConsumed == null) continue;
                foreach (MealItemConsumption consumed in record.ItemsConsumed)
                {
                    if (consumed != null
                        && string.Equals(consumed.ItemId, HouseholdItemCatalog.BreadId, StringComparison.Ordinal)
                        && consumed.LotId == receivedLotId)
                    {
                        lotReferenced = true;
                    }
                }
            }

            Assert.IsTrue(lotReferenced,
                "at least one H1 meal record must reference the purchased lot id " + receivedLotId);

            // --- (9, consumption half) the meal consumption mirror
            // decrements the reserve by the loaves eaten (4).
            Assert.AreEqual(Math.Max(0, Math.Min(reserveTarget, 6) - 4), reserveBefore.currentUnits,
                "meal consumption must decrement the reserve by the loaves eaten");

            // --- (10, post-meals) bread conservation with consumption
            // accounted: store 170 + households 10 + eaten 20 = 200.
            int eaten = 30 - 10; // bought 30 loaves across households, 10 remain
            Assert.AreEqual(200, f.TotalBread() + eaten, "bread must be conserved across purchase and meals");
            Assert.AreEqual(totalCashBefore, f.TotalCash(), "cash must stay conserved after meals");

            // --- (10, no re-buy) the fulfilled need is never bought again:
            // a fresh coordinator plan the same day finds nothing open.
            List<HouseholdErrandPlan> replans = f.Coordinator.PlanDay(7, 0, f.Context(), f.Diagnostics);
            Assert.AreEqual(0, replans.Count, "no open need remains, so no second plan — and no second sale — may form");
            Assert.AreEqual(170, f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId),
                "the store stock must be untouched by the re-plan");
        }

        [Test]
        public void ThreeDayRepeat_NeedsReopenTripsRepeatNoDoublePurchase()
        {
            Fixture f = Fixture.Create();
            f.FundAll(10000);
            var receiptIds = new List<string>();
            var needSequences = new List<int>();

            for (int day = 0; day <= 2; day++)
            {
                f.BeginDay(day);
                var batches = new Dictionary<int, HouseholdShoppingLoop.BatchShoppingResult>();
                var needsBefore = new Dictionary<int, HouseholdPurchasingNeed>();
                foreach (int householdId in Fixture.HouseholdIds)
                {
                    f.PlanHouseholdDay(householdId, day);
                    f.EvaluateMonitor(householdId, day);
                    needsBefore[householdId] = f.BreadNeed(householdId);
                }

                // Day 0 needs 6 loaves (zero stock); days 1-2 need 4 (2 left
                // after the previous day's meals, 6/day expected use).
                int expectedUnits = day == 0 ? 6 : 4;
                foreach (int householdId in Fixture.HouseholdIds)
                {
                    HouseholdPurchasingNeed need = needsBefore[householdId];
                    Assert.IsNotNull(need, $"H{householdId} day {day}: the need must re-open as stock is eaten");
                    Assert.AreEqual(expectedUnits, need.UnitsNeeded,
                        $"H{householdId} day {day}: the re-opened need must size to the shortfall");
                    if (householdId == 7) needSequences.Add(need.NeedSequence);
                }

                foreach (int householdId in Fixture.HouseholdIds)
                {
                    HouseholdShoppingLoop.BatchShoppingResult batch = f.ShopHousehold(householdId, day);
                    Assert.IsNotNull(batch, $"H{householdId} day {day}: a batch must form");
                    Assert.IsTrue(batch.TripExecuted, $"H{householdId} day {day}: the trip must repeat: " + batch.FailureReason);
                    Assert.AreEqual(expectedUnits, batch.UnitsAcquiredTotal);
                    Assert.AreEqual(PurchasingNeedStatus.Fulfilled, needsBefore[householdId].Status);
                    Assert.IsNull(f.BreadNeed(householdId),
                        $"H{householdId} day {day}: no open bread need may remain after the trip");
                    if (householdId == 7)
                    {
                        ShoppingTripResult breadResult = f.FindNeedResult(batch, HouseholdItemCatalog.BreadId);
                        Assert.IsNotNull(breadResult);
                        Assert.IsTrue(!string.IsNullOrEmpty(breadResult.ReceiptId));
                        receiptIds.Add(breadResult.ReceiptId);
                    }
                }

                DailyNeedsService.DayReport meals = f.RunMeals(day + 1);
                Assert.Greater(meals.MealsEaten, 0, $"day {day + 1}: meals must be served from the purchased bread");
                foreach (int householdId in Fixture.HouseholdIds)
                {
                    Assert.AreEqual(2, f.BreadUnits(householdId),
                        $"H{householdId} after day {day + 1} meals: 6 bought - 4 eaten = 2 left");
                }

                Assert.AreEqual(f.TotalCash(), 5 * 10000 + 50000,
                    $"day {day}: cash must stay conserved");
            }

            // Registry: exactly one bread need per household per day — the
            // fulfilled needs stay as history, never re-bought, and no
            // duplicate open need ever forms for the same item.
            Assert.AreEqual(3, f.CountNeeds(7, HouseholdItemCatalog.BreadId),
                "H1 must hold exactly 3 bread needs (one per day), not a growing pile of duplicates");
            Assert.AreEqual(3, needSequences.Count);
            Assert.AreEqual(3, new HashSet<int>(needSequences).Count, "each day's need must be a distinct need");
            Assert.AreEqual(3, new HashSet<string>(receiptIds).Count, "each day's purchase must carry a distinct receipt");
            Assert.AreEqual(200 - 70, f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId),
                "store stock must fall by exactly the 70 loaves sold over three days (30 + 20 + 20)");
            Assert.AreEqual(5 * 10000 + 50000, f.TotalCash(), "cash must stay conserved across three days");
        }

        [Test]
        public void UnavailableStock_BreadEmptyFlourServed()
        {
            Fixture f = Fixture.Create();
            f.Port.ItemStock[HouseholdItemCatalog.BreadId] = 0;
            f.FundAll(10000);
            f.BeginDay(0);

            HouseholdShoppingLoop.BatchShoppingResult batch = f.RunDayFor(7, 0, includeFlour: true);
            Assert.IsNotNull(batch);

            // Bread is recognized but holds zero stock: the need stays open
            // and nothing is spent on bread.
            HouseholdPurchasingNeed breadNeed = f.BreadNeed(7);
            Assert.IsNotNull(breadNeed, "the bread need must stay open through the stockout");
            Assert.AreEqual(6, breadNeed.UnitsNeeded);
            ShoppingTripResult breadResult = f.FindNeedResult(batch, HouseholdItemCatalog.BreadId);
            Assert.IsNotNull(breadResult, "the bread need must have its own per-need result");
            Assert.AreEqual(0, breadResult.UnitsAcquired);
            Assert.AreEqual(0, breadResult.BuyerOutflowCents, "nothing may be spent on bread that is not there");

            // Flour is stocked (5 sacks): the flour need is served whole
            // package — 12 lb needed, 2 sacks = 20 lb acquired at 35c/sack.
            Assert.IsNull(f.FlourNeed(7), "the flour need must be fulfilled");
            ShoppingTripResult flourResult = f.FindNeedResult(batch, HouseholdItemCatalog.FlourId);
            Assert.IsNotNull(flourResult, "the flour need must have its own per-need result");
            Assert.IsTrue(flourResult.Success, flourResult.FailureReason);
            Assert.AreEqual(20, flourResult.UnitsAcquired, "2 whole sacks x 10 lb");
            Assert.AreEqual(70, flourResult.BuyerOutflowCents, "2 sacks x 35c");
            Assert.AreEqual(10000 - 70, f.Balance(7), "only the flour price is spent");
            Assert.AreEqual(3, f.Port.ItemStockUnits(HouseholdItemCatalog.FlourId), "the store loses 2 sacks");
            Assert.AreEqual(0, f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId), "the store still holds no bread");
            Assert.IsTrue(batch.TripExecuted, "the trip still executes for the servable flour need");
            Assert.AreEqual(1, batch.ServedNeedCount);
        }

        [Test]
        public void InsufficientMoney_AffordabilityDownsizes()
        {
            Fixture f = Fixture.Create();
            f.Fund(7, 20); // H1 can cover 4 loaves of its 6-loaf need.
            foreach (int householdId in new[] { 8, 9, 10, 11 })
            {
                f.Fund(householdId, 10000);
            }

            f.BeginDay(0);
            HouseholdShoppingLoop.BatchShoppingResult batch = f.RunDayFor(7, 0);
            Assert.IsNotNull(batch);
            Assert.IsTrue(batch.TripExecuted, "the trip must execute for what the ledger covers: " + batch.FailureReason);

            ShoppingTripResult breadResult = f.FindNeedResult(batch, HouseholdItemCatalog.BreadId);
            Assert.IsNotNull(breadResult);
            Assert.IsTrue(breadResult.Success, breadResult.FailureReason);
            Assert.AreEqual(4, breadResult.UnitsAcquired, "20c covers exactly 4 loaves at 5c");
            Assert.AreEqual(20, breadResult.BuyerOutflowCents);
            Assert.IsTrue(breadResult.PartialFillReason.StartsWith("affordability-downsized"),
                "the downsizing must be named, not silent, got: " + breadResult.PartialFillReason);
            Assert.AreEqual(0, f.Balance(7), "the ledger is spent to zero, never below");
            Assert.IsTrue(string.IsNullOrEmpty(breadResult.ObligationId),
                "no trade-credit obligation may be created — the store offers none");
            Assert.AreEqual(0, breadResult.StoreReceivableCents, "no debt, no receivable");

            HouseholdPurchasingNeed need = f.BreadNeed(7);
            Assert.IsNotNull(need, "the need must stay open for the remainder");
            Assert.AreEqual(2, need.UnitsNeeded, "6 needed - 4 bought = 2 still needed");
        }

        [Test]
        public void ClosedStore_SundayFailsMondaySucceeds()
        {
            Fixture f = Fixture.Create();
            f.FundAll(10000);

            // Sunday (day-of-week index 6): Canon 13.4 closes the store.
            f.BeginDay(6);
            foreach (int householdId in Fixture.HouseholdIds)
            {
                HouseholdShoppingLoop.BatchShoppingResult sunday = f.RunDayFor(householdId, 6);
                Assert.IsNotNull(sunday, $"H{householdId} Sunday: a batch must form");
                Assert.IsTrue(!sunday.TripExecuted, $"H{householdId} Sunday: trading is closed");
                Assert.IsTrue(sunday.FailureReason.Contains("closed"),
                    $"H{householdId} Sunday: the failure must name the closure, got: " + sunday.FailureReason);
                Assert.IsNotNull(f.BreadNeed(householdId), $"H{householdId} Sunday: the need must stay open");
                Assert.AreEqual(10000, f.Balance(householdId));
            }

            Assert.AreEqual(200, f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId),
                "no Sunday sale may move store stock");

            // Monday (day 7): the identical trip succeeds for every household.
            f.BeginDay(7);
            foreach (int householdId in Fixture.HouseholdIds)
            {
                HouseholdShoppingLoop.BatchShoppingResult monday = f.RunDayFor(householdId, 7);
                Assert.IsNotNull(monday, $"H{householdId} Monday: a batch must form");
                Assert.IsTrue(monday.TripExecuted, $"H{householdId} Monday: the trip must succeed: " + monday.FailureReason);
                Assert.AreEqual(6, monday.UnitsAcquiredTotal);
                Assert.IsNull(f.BreadNeed(householdId), $"H{householdId} Monday: the need must be fulfilled");
            }

            Assert.AreEqual(200 - 30, f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId),
                "Monday sells exactly the 30 loaves the five households needed");
        }

        [Test]
        public void UnavailableShoppers_OnlyAdultsOffMap()
        {
            Fixture f = Fixture.Create();
            f.FundAll(10000);
            f.BeginDay(0);

            // Both H1 adults depart off-map before the day is planned.
            f.Journeys.RegisterLocation(new JourneyLocation("gateway", JourneyLocationKind.OffMapGateway, "Depot", 4f, 0f));
            f.Journeys.AddEdge("home-7", "gateway", 4.0f, "depot road");
            var authority = new OffMapJourneyAuthority(f.Journeys);
            Assert.IsNotNull(authority.DepartPerson(1, "home-7", "Millbrook", 1, 2, "freight",
                null, null, 0, _ => 0, null, f.Diagnostics));
            Assert.IsNotNull(authority.DepartPerson(2, "home-7", "Millbrook", 1, 2, "freight",
                null, null, 0, _ => 0, null, f.Diagnostics));
            Assert.IsTrue(authority.IsOffMapPerson(1), "P1 must read off-map");
            Assert.IsTrue(authority.IsOffMapPerson(2), "P2 must read off-map");
            f.OffMapJourneys = authority;
            f.Loop.OffMapJourneys = authority;

            HouseholdShoppingLoop.BatchShoppingResult batch = f.RunDayFor(7, 0);
            Assert.IsNotNull(batch);
            Assert.IsTrue(!batch.TripExecuted, "no shopper is available, so no trip may execute");
            Assert.IsTrue(batch.FailureReason.Contains("shopper"),
                "the failure must name the missing shopper, got: " + batch.FailureReason);
            Assert.IsNotNull(f.BreadNeed(7), "the need must stay open");
            Assert.AreEqual(10000, f.Balance(7), "no money may move without a shopper");
            Assert.AreEqual(200, f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId),
                "no stock may move without a shopper");
            Assert.AreEqual(0, f.Loop.ActiveCustodyBatches.Count, "no custody may form without a trip");

            // The other households still shop: the failure is H1's alone.
            HouseholdShoppingLoop.BatchShoppingResult other = f.RunDayFor(8, 0);
            Assert.IsNotNull(other);
            Assert.IsTrue(other.TripExecuted, "H8 must still buy: " + other.FailureReason);
        }

        [Test]
        public void Retries_CounterStockoutRefundsAndRetriesNextDay()
        {
            Fixture f = Fixture.Create();
            f.FundAll(10000);
            f.BeginDay(0);

            // The stock moves between the counter check and the sale: the
            // fake port refuses its next consume, so SellItem returns 0
            // after the 30c cash leg has posted.
            f.Port.FailNextConsume = true;
            HouseholdShoppingLoop.BatchShoppingResult day0 = f.RunDayFor(7, 0);
            Assert.IsNotNull(day0);
            Assert.IsTrue(day0.TripExecuted, "the trip itself happened — legs ran, the shopper returned");
            Assert.AreEqual(0, day0.ServedNeedCount, "nothing settled at the counter");
            Assert.IsTrue(!day0.Success, "a batch that settled nothing is not a success");
            Assert.IsTrue(day0.UnbatchedNeeds.Count == 1
                && string.Equals(day0.UnbatchedNeeds[0].ItemId, HouseholdItemCatalog.BreadId, StringComparison.Ordinal),
                "the unsettled bread need must be carried as unbatched, not dropped");
            Assert.IsTrue(f.DiagnosticsContain("refunded"),
                "the honest refund must be logged loudly");
            ShoppingTripResult breadResult = f.FindNeedResult(day0, HouseholdItemCatalog.BreadId);
            Assert.IsNotNull(breadResult);
            Assert.IsTrue(!breadResult.Success, "the bread result must fail");
            Assert.AreEqual(0, breadResult.UnitsAcquired);
            Assert.AreEqual(0, breadResult.BuyerOutflowCents, "the posted cash leg must be refunded to zero");
            Assert.AreEqual(0, breadResult.StoreCashInCents);
            Assert.AreEqual(10000, f.Balance(7), "the refund must leave the ledger whole");
            Assert.AreEqual(50000, f.Port.CashBalance, "the store must not keep money for unsold goods");
            Assert.AreEqual(200, f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId),
                "no stock moved — the consume was refused");
            Assert.IsNotNull(f.BreadNeed(7), "the need must stay open for the retry");
            Assert.AreEqual(0, f.Loop.ActiveCustodyBatches.Count, "no custody may form from a voided sale");

            // Next day the same need retries against the same stock — and
            // this time the sale settles.
            f.BeginDay(1);
            HouseholdShoppingLoop.BatchShoppingResult day1 = f.RunDayFor(7, 1);
            Assert.IsNotNull(day1);
            Assert.IsTrue(day1.TripExecuted, "the retry trip must execute: " + day1.FailureReason);
            Assert.AreEqual(1, day1.ServedNeedCount);
            Assert.AreEqual(6, day1.UnitsAcquiredTotal);
            Assert.IsNull(f.BreadNeed(7), "the retried need must now be fulfilled");
            Assert.AreEqual(10000 - 30, f.Balance(7), "the retry pays exactly once");
            Assert.AreEqual(50000 + 30, f.Port.CashBalance);
            Assert.AreEqual(200 - 6, f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId));
        }

        [Test]
        public void Retries_PartialFillTopsUpNextDay()
        {
            Fixture f = Fixture.Create();
            f.Port.ItemStock[HouseholdItemCatalog.BreadId] = 4;
            f.FundAll(10000);
            f.BeginDay(0);

            // Day 0: the store holds 4 of the 6 needed loaves — buy what is
            // there, keep the need open for the remainder.
            HouseholdShoppingLoop.BatchShoppingResult day0 = f.RunDayFor(7, 0);
            Assert.IsNotNull(day0);
            Assert.IsTrue(day0.TripExecuted, "the partial trip must execute: " + day0.FailureReason);
            ShoppingTripResult bread0 = f.FindNeedResult(day0, HouseholdItemCatalog.BreadId);
            Assert.IsNotNull(bread0);
            Assert.IsTrue(bread0.Success, bread0.FailureReason);
            Assert.AreEqual(4, bread0.UnitsAcquired, "the store held only 4 loaves");
            Assert.AreEqual(20, bread0.BuyerOutflowCents, "4 loaves x 5c");
            Assert.IsTrue(bread0.PartialFillReason.StartsWith("stock-limited"),
                "the shortfall must be named, not silent, got: " + bread0.PartialFillReason);
            HouseholdPurchasingNeed need = f.BreadNeed(7);
            Assert.IsNotNull(need, "the need must stay open for the remainder");
            Assert.AreEqual(2, need.UnitsNeeded, "6 needed - 4 bought = 2 still needed");
            Assert.AreEqual(4, f.BreadUnits(7));
            Assert.AreEqual(0, f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId));

            // Day 1: the store restocks, the monitor re-sizes the open need
            // to the 2-loaf shortfall (4 loaves = 0.7 days of supply), and
            // the trip tops it up.
            f.Port.ItemStock[HouseholdItemCatalog.BreadId] = 10;
            f.BeginDay(1);
            HouseholdShoppingLoop.BatchShoppingResult day1 = f.RunDayFor(7, 1);
            Assert.IsNotNull(day1);
            Assert.IsTrue(day1.TripExecuted, "the top-up trip must execute: " + day1.FailureReason);
            ShoppingTripResult bread1 = f.FindNeedResult(day1, HouseholdItemCatalog.BreadId);
            Assert.IsNotNull(bread1);
            Assert.IsTrue(bread1.Success, bread1.FailureReason);
            Assert.AreEqual(2, bread1.UnitsAcquired, "the top-up buys exactly the 2-loaf remainder");
            Assert.AreEqual(10, bread1.BuyerOutflowCents, "2 loaves x 5c");
            Assert.IsNull(f.BreadNeed(7), "the need must now be fulfilled");
            Assert.AreEqual(6, f.BreadUnits(7), "4 + 2 = the full 6 loaves");
            Assert.AreEqual(10000 - 30, f.Balance(7), "the two days together pay exactly once per loaf");
            Assert.AreEqual(8, f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId),
                "the store keeps the 8 loaves nobody bought");
        }
    }
}
