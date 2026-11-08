using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Labor;
using LandLedgers.Economy.Storage;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    /// <summary>
    /// Priority 9 Slice 3 (Priority B) — the personal and player inventory
    /// read and custody layer (PersonInventoryReadModel). A READ-ONLY query
    /// layer: every test below asserts answers against the real authorities
    /// (the shopping loop's custody batches, the household inventory
    /// registry, the equipment grant resolver) and pins that reading them
    /// changes nothing.
    ///
    /// The driving fixture is the S6-1 shape copied from
    /// SinglePersonShoppingTests (single employed adult, 240-min shift
    /// 480-720, store stocked 16 loaves at 5c, funded 10000c, monitor-raised
    /// 14-loaf bread need, Slice 4 production-day drive, next-day meal),
    /// parameterized by person id so the SAME sequence helper drives a
    /// player person id and an NPC person id with no special-case branch
    /// (acceptance B). The mid-trip step is observed through the loop's
    /// MidTripObserver — which swallows observer faults, so the observer
    /// only CAPTURES read-model snapshots into locals and every assertion
    /// runs after ExecuteBatch returns.
    /// </summary>
    [TestFixture]
    public sealed class PersonInventoryReadModelTests
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
            public int CreditSettlements;
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

            public void RecordCreditSettlement(string categoryId, int unitsSold, int amountCents, string obligationId)
            {
                CreditSettlements++;
            }

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
            public HouseholdShortageMonitor Monitor;
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
            public HouseholdMealLogRegistry MealLog;
            public HouseholdMealService MealService;
            public MealSchedulingPolicy MealPolicy;
            public FakeStorePort Port;
            public Dictionary<int, string> PersonLocations = new Dictionary<int, string>();
            public List<string> ResolvedLocations = new List<string>();
            public List<LocationHop> Transitions = new List<LocationHop>();
            public List<string> Diagnostics = new List<string>();
            public OffMapJourneyAuthority OffMapJourneys;
            public List<int> MemberIds = new List<int>();

            public int HouseholdId = 7;
            public int ShopperId = 1;
            public int Day = 0; // Monday (day-of-week index 0)

            public static Fixture CreateSingle(bool female, int personId)
            {
                var f = new Fixture();
                f.SetupCommon(2.0);
                f.AddPerson(personId, female ? "Anna" : "Tomas", "Solo", 34, female);
                return f;
            }

            private void SetupCommon(double routeMiles)
            {
                Population = new PopulationState();
                Population.households.Add(new HouseholdState { id = 7 });
                Membership = new HouseholdMembershipRegistry();
                Ledgers = new HouseholdLedgerRegistry();
                Inventories = new HouseholdInventoryRegistry();
                Needs = new HouseholdNeedRegistry();
                Monitor = new HouseholdShortageMonitor(Needs);
                Journeys = new JourneyModel();
                Journeys.RegisterLocation(new JourneyLocation("home", JourneyLocationKind.Farmstead, "Solo Farm", 0f, 0f));
                Journeys.RegisterLocation(new JourneyLocation("store", JourneyLocationKind.Store, "General Store", 2f, 0f));
                Journeys.AddEdge("home", "store", (float)routeMiles, "river road");

                Tasks = new TaskAuthority();
                HouseholdShoppingLoop.RegisterTaskDefinitions(Tasks);
                AutonomousWorkGenerator.GeneratedWorkCatalog.RegisterAll(Tasks, Diagnostics);
                Budgets = new WorkTimeBudgetStore();
                Tracker = new PersonScheduleTracker();
                Knowledge = new ShopperSellerKnowledge();
                Obligations = new FinancialObligationAuthority();
                EntityIds = new EntityIdRegistry();

                // Stocked bread store: 16 identified loaves at the authored 5c.
                Port = new FakeStorePort();
                Port.OfferList.Add(new GeneralStoreCategoryOffer
                {
                    CategoryId = "staple_food",
                    OfferedItemIds = new List<string> { HouseholdItemCatalog.BreadId },
                    StoreUnitsPerItemUnit = 1,
                    UsesItemLevelInventory = true,
                });
                Port.ItemLevelItems.Add(HouseholdItemCatalog.BreadId);
                Port.ItemStock[HouseholdItemCatalog.BreadId] = 16;
                Port.ItemPrices[HouseholdItemCatalog.BreadId] = 5;

                Directory = new SupplierDirectory();
                GeneralStoreSupplierAdapter.RegisterIn(Directory, Port);

                Loop = new HouseholdShoppingLoop(
                    Population, Membership, Ledgers, Inventories, Needs,
                    Directory, Journeys, Tasks, Budgets, Tracker, Knowledge,
                    Obligations, EntityIds);
                Coordinator = new HouseholdErrandCoordinator();
                Planner = new DailyActivityPlanner();
                MealLog = new HouseholdMealLogRegistry();
                MealService = new HouseholdMealService(MealLog);
                MealPolicy = MealSchedulingPolicy.Default;
            }

            private void AddPerson(int personId, string first, string last, int age, bool female)
            {
                ShopperId = personId;
                Population.people.Add(new PersonState
                {
                    id = personId, firstName = first, lastName = last, age = age,
                    ageBand = AgeBand.Adult18Plus, isFemale = female,
                    laborAccessLevel = LaborAccessLevel.FullLaborMarket,
                });
                Assert.IsNull(Membership.Register(new HouseholdMembership
                {
                    MembershipId = personId, PersonId = personId, HouseholdId = 7, StartDayIndex = 0,
                }));
                Knowledge.LearnSupplier(personId, "store-1", "lives in the same town", 0);
                PersonLocations[personId] = "home";
                MemberIds.Add(personId);
            }

            public ShoppingLoopOptions Options()
            {
                var fixture = this;
                return new ShoppingLoopOptions
                {
                    PersonLocationId = pid =>
                    {
                        PersonState person = fixture.Population.GetPerson(pid);
                        string loc;
                        if (person != null && person.scheduleState == PopulationScheduleState.AtWork
                            && person.workplaceBuildingId >= 0)
                        {
                            loc = "forest-camp";
                        }
                        else
                        {
                            loc = fixture.PersonLocations.TryGetValue(pid, out string tracked) ? tracked : "home";
                        }

                        fixture.ResolvedLocations.Add(loc);
                        return loc;
                    },
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

            public void PlanDay()
            {
                var members = new List<int>(MemberIds);
                Planner.PlanHouseholdDay(
                    members,
                    pid => new DailyActivityPlanner.PersonDayPlanInputs
                    {
                        PersonId = pid,
                        HouseholdId = HouseholdId,
                        AgeBand = AgeBand.Adult18Plus,
                    },
                    Day, Tracker, Budgets, Diagnostics);
            }

            /// <summary>Raises the bread need through the real shortage
            /// monitor (bread recognized by the known seller; 2 loaves/day
            /// expected use, zero on hand, so 14u at urgency 1.0).</summary>
            public HouseholdPurchasingNeed RaiseBreadNeedViaMonitor()
            {
                HouseholdState household = Population.GetHousehold(HouseholdId);
                HouseholdInventory inventory = Inventories.GetOrCreate(HouseholdId);
                Monitor.Evaluate(
                    household, inventory,
                    new Dictionary<string, int> { { HouseholdItemCatalog.BreadId, 2 } },
                    false,
                    new HouseholdSupplyAccess { HasGeneralStore = true, HasCash = true },
                    0, 0, Day,
                    HouseholdShortageMonitor.KnownSellerRecognitionPredicate(Directory, Knowledge, MemberIds));
                return Needs.GetOpenNeed(HouseholdId, HouseholdItemCatalog.BreadId);
            }

            public WorkTask CreateWorkTask(int personId, int minutes)
            {
                WorkTask task = Tasks.CreateTaskWithPlannedMinutes(
                    AutonomousWorkGenerator.GeneratedWorkCatalog.ProductionWorkOrderId,
                    EntityId.For(EntityKind.Business, 9001), Day, minutes);
                Assert.IsNotNull(task, "the work task must be created");
                return task;
            }

            public int ReserveWork(List<WorkTask> workTasks, List<int> personIds, List<int> minutesList,
                List<string> workDiagnostics, out List<WorkTask> assignedTasks,
                out Dictionary<string, string> reservationIdByTaskId)
            {
                var report = new WorkAssignmentReport { BusinessInstanceId = "employer-1", DayIndex = Day };
                var taskByTextId = new Dictionary<string, WorkTask>(StringComparer.Ordinal);
                for (int i = 0; i < workTasks.Count; i++)
                {
                    report.Assignments.Add(new WorkAssignment
                    {
                        TaskId = workTasks[i].TaskId.ToString(),
                        PersonId = personIds[i],
                        MinutesCommitted = minutesList[i],
                    });
                    taskByTextId[workTasks[i].TaskId.ToString()] = workTasks[i];
                }

                return ManagerWorkSchedulerDrive.ReserveAndAssignTasks(
                    report, taskByTextId, Tasks, Tracker, Budgets, Day,
                    workDiagnostics ?? Diagnostics,
                    ManagerWorkSchedulerDrive.DefaultTaskLabel, null,
                    out assignedTasks, out reservationIdByTaskId);
            }

            public int StartReservedWork(List<WorkTask> assignedTasks,
                Dictionary<string, string> reservationIdByTaskId, List<string> workDiagnostics)
            {
                return ManagerWorkSchedulerDrive.StartReservedTasks(
                    assignedTasks, reservationIdByTaskId, Tasks, Tracker, Day,
                    workDiagnostics ?? Diagnostics);
            }

            public PersonActivityReservation FindReservation(int personId, PersonActivityKind kind)
            {
                foreach (PersonActivityReservation r in Tracker.Reservations)
                {
                    if (r != null && r.PersonId == personId && r.DayIndex == Day && r.Activity == kind)
                    {
                        return r;
                    }
                }

                return null;
            }

            public void Fund(int cents)
            {
                HouseholdLedger ledger = Ledgers.GetOrCreate(HouseholdId);
                string problem = ledger.RecordInflow(0, cents, HouseholdIncomeSource.OwnerContribution,
                    "test-capital", "test funding", "test");
                Assert.IsNull(problem, problem);
            }

            public int Balance() => Ledgers.GetOrCreate(HouseholdId).GetBalanceCents();

            public HouseholdItemLot BreadLot()
            {
                foreach (HouseholdItemLot lot in Inventories.GetOrCreate(HouseholdId).Lots)
                {
                    if (lot != null && string.Equals(lot.ItemId, HouseholdItemCatalog.BreadId, StringComparison.Ordinal))
                    {
                        return lot;
                    }
                }

                return null;
            }

            public int BreadUnits() =>
                Inventories.GetOrCreate(HouseholdId).GetAvailableUnits(HouseholdItemCatalog.BreadId);

            public int OpenNeedCount(string itemId)
            {
                int count = 0;
                foreach (HouseholdPurchasingNeed need in Needs.GetOpenNeeds(HouseholdId))
                {
                    if (need != null && need.ItemId == itemId)
                    {
                        count++;
                    }
                }

                return count;
            }

            /// <summary>Plan 05 Slice 6 (S6-1) — the meal leg. Advances to
            /// the next day, closes the prior day's work via CompleteTask,
            /// serves the household's meals for the new day from real
            /// inventory, and returns that day's meal records.</summary>
            public IReadOnlyList<HouseholdMealRecord> DriveNextDayMeal(WorkTask priorDayWork)
            {
                Day++;
                Budgets.EnsureDay(Day);
                if (priorDayWork != null)
                {
                    Assert.IsTrue(Tasks.CompleteTask(priorDayWork.TaskId, TaskOutcome.Success, 0, Budgets),
                        "the prior day's work must complete at day end");
                }

                MealDayResult result = MealService.ServeHouseholdDay(
                    Population.GetHousehold(HouseholdId), new List<int>(MemberIds),
                    Inventories.GetOrCreate(HouseholdId), MealPolicy, Day, null);
                Assert.Greater(result.MealsServed, 0,
                    "at least one meal must be served from the purchased bread (missed: " + result.MealsMissed +
                    "; diagnostics: " + string.Join(" | ", MealService.Diagnostics) + ")");
                var dayMeals = new List<HouseholdMealRecord>();
                foreach (HouseholdMealRecord record in MealLog.GetMeals(HouseholdId))
                {
                    if (record != null && record.DayIndex == Day)
                    {
                        dayMeals.Add(record);
                    }
                }

                return dayMeals;
            }

            /// <summary>Drives one day in the Slice 4 production order:
            /// planner, assigned-work RESERVE + ASSIGN, then monitor +
            /// coordinator plan + execute (shopping), then assigned-work
            /// START. Mirrors SimulationDrivers.OnDayChanged after Slice 4.</summary>
            public HouseholdShoppingLoop.BatchShoppingResult DriveProductionDay(
                List<WorkTask> workTasks, List<int> personIds, List<int> minutesList,
                out int started, List<string> workDiagnostics)
            {
                PlanDay();
                List<WorkTask> assignedTasks = null;
                Dictionary<string, string> reservationIdByTaskId = null;
                if (workTasks != null && workTasks.Count > 0)
                {
                    ReserveWork(workTasks, personIds, minutesList, workDiagnostics,
                        out assignedTasks, out reservationIdByTaskId);
                }

                HouseholdPurchasingNeed need = RaiseBreadNeedViaMonitor();
                Assert.IsNotNull(need, "the monitor must open the bread need before shopping runs");
                List<HouseholdErrandPlan> plans = Coordinator.PlanDay(HouseholdId, Day, Context(), Diagnostics);
                HouseholdShoppingLoop.BatchShoppingResult batch = null;
                if (plans.Count > 0)
                {
                    batch = Coordinator.ExecutePlan(plans[0], Day, Context(), Loop, Options(), Diagnostics);
                }

                started = 0;
                if (assignedTasks != null && assignedTasks.Count > 0)
                {
                    started = StartReservedWork(assignedTasks, reservationIdByTaskId, workDiagnostics);
                }

                return batch;
            }
        }

        private sealed class LocationHop
        {
            public int PersonId;
            public string LocationId;
        }

        /// <summary>A mid-trip read-model snapshot. Captured ONLY — never
        /// asserted — inside the MidTripObserver, which the loop invokes
        /// inside a try/catch that swallows observer faults.</summary>
        private sealed class MidTripSnapshot
        {
            public bool Observed;
            public List<PersonInventoryReadModel.CarriedItem> Carried = new List<PersonInventoryReadModel.CarriedItem>();
            public int HouseholdBreadUnits;
            public int OtherPersonCarriedCount;
            public PhysicalLoadResolution LoadResolution;
            public PersonInventoryReadModel.PersonCarryRemaining Remaining;
        }

        private static ShoppingTripResult FindNeedResult(HouseholdShoppingLoop.BatchShoppingResult batch, string itemId)
        {
            if (batch?.NeedResults == null) return null;
            foreach (ShoppingTripResult result in batch.NeedResults)
            {
                if (result != null && string.Equals(result.ItemId, itemId, StringComparison.Ordinal)) return result;
            }

            return null;
        }

        private static bool HasEvent(ShoppingTripResult result, ShoppingEventKind kind)
        {
            if (result?.Events == null) return false;
            foreach (ShoppingDiagnosticEvent e in result.Events)
            {
                if (e != null && e.Kind == kind) return true;
            }

            return false;
        }

        private static bool Overlaps(PersonActivityReservation a, PersonActivityReservation b)
        {
            if (a == null || b == null) return false;
            return a.StartMinuteOfDay < b.EndMinuteOfDay && b.StartMinuteOfDay < a.EndMinuteOfDay;
        }

        private static PersonInventoryReadModel ReadModelFor(Fixture f, EquipmentAccessResolver resolver = null)
        {
            return new PersonInventoryReadModel(
                new HouseholdShoppingReadModel(f.Loop, f.Tracker),
                f.Inventories, resolver, f.Options());
        }

        /// <summary>Asserts the full read-model sweep for one person
        /// returns identical answers twice (one authority, re-read).</summary>
        private static void AssertSweepAgrees(PersonInventoryReadModel readModel, int personId)
        {
            IReadOnlyList<PersonInventoryReadModel.CarriedItem> firstCarried = readModel.CarriedItems(personId);
            IReadOnlyList<PersonInventoryReadModel.CarriedItem> secondCarried = readModel.CarriedItems(personId);
            Assert.AreEqual(firstCarried.Count, secondCarried.Count, "two reads of the same carried state must agree");
            for (int i = 0; i < firstCarried.Count; i++)
            {
                Assert.AreEqual(firstCarried[i].BatchId, secondCarried[i].BatchId, "two reads must agree on the batch id");
                Assert.AreEqual(firstCarried[i].Units, secondCarried[i].Units, "two reads must agree on the units");
            }

            IReadOnlyList<PersonInventoryReadModel.OwnedHouseholdLot> firstLots = readModel.HouseholdLotsOwnedBy(personId);
            IReadOnlyList<PersonInventoryReadModel.OwnedHouseholdLot> secondLots = readModel.HouseholdLotsOwnedBy(personId);
            Assert.AreEqual(firstLots.Count, secondLots.Count, "two reads of the same owned lots must agree");
            for (int i = 0; i < firstLots.Count; i++)
            {
                Assert.AreEqual(firstLots[i].LotId, secondLots[i].LotId, "two reads must agree on the lot id");
                Assert.AreEqual(firstLots[i].AvailableUnits, secondLots[i].AvailableUnits, "two reads must agree on the available units");
            }
        }

        /// <summary>
        /// The S6-1 purchase-to-meal sequence through the read model, driven
        /// identically for any single person id (acceptance B: the same
        /// helper, the same read-model type, no branch on the id or on
        /// player vs NPC). Steps: before purchase the person carries
        /// nothing; mid-trip (captured by the MidTripObserver) the person
        /// carries 14 loaves InHand with batch receipt id and provenance
        /// while the household holds 0; after receipt the person carries
        /// nothing and the household holds the 14-loaf lot with the same
        /// receipt id in its source label; after the next-day meal the lot
        /// is decremented by the meal and the meal log references the lot
        /// id. Acceptance R (same settlement as S6-1: 14u, 5c, 70c, same
        /// receipt) and C (no double count at any step) are asserted at
        /// each step; Q (a full read-model sweep mutates nothing) is
        /// asserted after receipt.
        /// </summary>
        private void SinglePersonPurchaseToMeal_ReadModelSequence(bool female, int personId)
        {
            Fixture f = Fixture.CreateSingle(female, personId);
            f.Fund(10000);
            PersonInventoryReadModel readModel = ReadModelFor(f);
            int otherPersonId = personId + 1000;

            // --- Before purchase: the person carries nothing. ---
            Assert.AreEqual(0, readModel.CarriedItems(personId).Count, "before purchase the person carries nothing");
            Assert.AreEqual(0, readModel.CarriedItems(otherPersonId).Count, "another person carries nothing either");
            Assert.AreEqual(0, readModel.CarriedLoad(personId).Resolve().TotalUnits, "before purchase the carried load is empty");
            PersonInventoryReadModel.PersonCarryRemaining remainingBefore = readModel.RemainingCarryUnits(personId);
            Assert.AreEqual(50, remainingBefore.UnitsRemaining, "before purchase the full 50-unit live carry cap remains");
            Assert.IsTrue(!remainingBefore.MassRemainingKg.IsKnown, "no authored person mass limit exists, so mass remaining is Unknown");
            Assert.IsTrue(!remainingBefore.VolumeRemainingLiters.IsKnown, "no authored person volume limit exists, so volume remaining is Unknown");
            Assert.AreEqual(0, f.BreadUnits(), "the household opens with no bread");

            // --- Mid-trip capture: after purchase, before receipt. ---
            var midTrip = new MidTripSnapshot();
            f.Loop.MidTripObserver = (trip, batch) =>
            {
                midTrip.Observed = true;
                midTrip.Carried = new List<PersonInventoryReadModel.CarriedItem>(readModel.CarriedItems(personId));
                midTrip.HouseholdBreadUnits = f.BreadUnits();
                midTrip.OtherPersonCarriedCount = readModel.CarriedItems(otherPersonId).Count;
                midTrip.LoadResolution = readModel.CarriedLoad(personId).Resolve();
                midTrip.Remaining = readModel.RemainingCarryUnits(personId);
            };

            WorkTask work = f.CreateWorkTask(personId, 240);
            var workDiag = new List<string>();
            int started;
            HouseholdShoppingLoop.BatchShoppingResult batch = f.DriveProductionDay(
                new List<WorkTask> { work }, new List<int> { personId }, new List<int> { 240 },
                out started, workDiag);

            Assert.IsNotNull(batch, "a plan must be created for the open bread need");
            Assert.IsTrue(batch.TripExecuted, "the post-shift trip must execute: " + batch.FailureReason);
            Assert.AreEqual(personId, batch.ShopperPersonId, "the lone adult shops");
            Assert.IsTrue(midTrip.Observed, "the MidTripObserver must have fired after purchase and before receipt");

            // Acceptance A + C, mid-trip: legitimately acquired goods in
            // hand, household still holding none of them — no double count.
            Assert.AreEqual(1, midTrip.Carried.Count, "mid-trip the person carries exactly one batch");
            PersonInventoryReadModel.CarriedItem carried = midTrip.Carried[0];
            Assert.AreEqual(HouseholdItemCatalog.BreadId, carried.ItemId, "the carried item is bread");
            Assert.AreEqual(14, carried.Units, "the person carries the full 14-loaf need");
            Assert.AreEqual(GoodsCustodyState.InHand, carried.Custody, "the batch is InHand, not yet received");
            Assert.AreEqual(personId, carried.HolderPersonId, "custody holder is the shopper");
            Assert.AreEqual(f.HouseholdId, carried.OwningHouseholdId, "the owning household is named separately from the holder person");
            Assert.IsTrue(!string.IsNullOrEmpty(carried.BatchId), "the carried batch has a batch id");
            Assert.IsTrue(!string.IsNullOrEmpty(carried.ReceiptId), "the carried batch carries the settlement receipt id");
            Assert.IsTrue(carried.Provenance.Contains("purchased 14u bread from"),
                "the carried batch carries purchase provenance, got: " + carried.Provenance);
            Assert.IsTrue(carried.Provenance.Contains(carried.ReceiptId),
                "the carried provenance carries the receipt id, got: " + carried.Provenance);
            Assert.AreEqual(0, carried.PurchaseDayIndex, "the purchase day is day 0");
            Assert.AreEqual(5, carried.UnitPriceCents, "the carried batch carries the 5c unit price");
            Assert.AreEqual(PersonItemCondition.Unknown, carried.Condition,
                "a carried batch carries provenance and a receipt, not a condition — Unknown, never a default");
            Assert.AreEqual(0, midTrip.HouseholdBreadUnits,
                "mid-trip the household holds none of the purchased bread — no teleport, no double count (C)");
            Assert.AreEqual(14, midTrip.Carried[0].Units + midTrip.HouseholdBreadUnits,
                "carried plus household equals the 14 purchased loaves at the mid-trip step (C)");
            Assert.AreEqual(0, midTrip.OtherPersonCarriedCount,
                "a second person id sees none of the first person's carried items");
            Assert.AreEqual(14, midTrip.LoadResolution.TotalUnits, "the carried load is 14 units mid-trip");
            Assert.IsTrue(!midTrip.LoadResolution.IsMassKnown, "bread mass is Unknown, so the carried load mass is Unknown");
            Assert.AreEqual(HouseholdItemCatalog.BreadId, midTrip.LoadResolution.FirstUnknownMassItemId,
                "the carried load names bread as the first unknown-mass item");
            Assert.AreEqual(36, midTrip.Remaining.UnitsRemaining, "mid-trip 50 - 14 carried leaves 36 units of carry cap");
            Assert.IsTrue(!midTrip.Remaining.MassRemainingKg.IsKnown, "mass remaining stays Unknown mid-trip");
            Assert.IsTrue(!midTrip.Remaining.VolumeRemainingLiters.IsKnown, "volume remaining stays Unknown mid-trip");

            // --- After receipt: the person carries nothing, the household holds the lot. ---
            Assert.AreEqual(0, readModel.CarriedItems(personId).Count, "after receipt the person carries nothing");
            Assert.AreEqual(50, readModel.RemainingCarryUnits(personId).UnitsRemaining, "after receipt the full carry cap is free again");
            Assert.AreEqual(0, f.Loop.ActiveCustodyBatches.Count, "no custody batch may be left stranded after receipt");
            HouseholdItemLot lot = f.BreadLot();
            Assert.IsNotNull(lot, "the household must hold a bread lot after the purchase");
            Assert.AreEqual(14, lot.QuantityUnits, "the household lot holds the 14 purchased loaves");
            Assert.IsTrue(lot.SourceLabel.Contains("purchased 14u bread from"),
                "the lot must carry purchase provenance, got: " + lot.SourceLabel);

            // Acceptance R: the purchase settles exactly as S6-1 does.
            ShoppingTripResult breadResult = FindNeedResult(batch, HouseholdItemCatalog.BreadId);
            Assert.IsNotNull(breadResult, "the batch must carry its own bread result");
            Assert.IsTrue(breadResult.Success, breadResult.FailureReason);
            Assert.AreEqual(14, breadResult.UnitsAcquired, "the trip must acquire the full 14-loaf need");
            Assert.AreEqual(70, breadResult.BuyerOutflowCents, "14 loaves x 5c (R)");
            Assert.IsTrue(!string.IsNullOrEmpty(breadResult.ReceiptId), "the settlement must record a receipt id");
            Assert.AreEqual(carried.ReceiptId, breadResult.ReceiptId,
                "the mid-trip carried batch and the settled trip carry the same receipt id (R)");
            Assert.IsTrue(lot.SourceLabel.Contains(breadResult.ReceiptId),
                "the lot provenance must carry the receipt id, got: " + lot.SourceLabel);
            Assert.AreEqual(10000 - 70, f.Balance(), "buyer debit must equal 14 loaves x the 5c price (R)");
            Assert.AreEqual(2, f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId),
                "seller stock must fall by exactly the 14 loaves sold (R)");
            Assert.AreEqual(0, f.OpenNeedCount(HouseholdItemCatalog.BreadId), "the need must be fulfilled by the trip");
            Assert.IsTrue(HasEvent(breadResult, ShoppingEventKind.Custody), "the custody transfer into the shopper's hands must be recorded");
            Assert.IsTrue(HasEvent(breadResult, ShoppingEventKind.Receipt), "the physical receipt at the household must be recorded");

            // Acceptance D, receipt half: the receipt books the lot to the
            // HOUSEHOLD (OwnerPersonId -1), not to the shopper as a person.
            // Ownership (no person owner) and custody (holder was the
            // shopper, now nobody) stay distinct — the lot is not listed
            // as owned by the shopper.
            Assert.AreEqual(-1, lot.OwnerPersonId, "the loop receipt books the lot to the household, not to a person owner");
            Assert.AreEqual(0, readModel.HouseholdLotsOwnedBy(personId).Count,
                "the household-owned lot is not the shopper's personal lot (D)");

            // Acceptance Q, after receipt: a full read-model sweep changes
            // nothing — same lot, same units, same balance, same store.
            int lotCountBefore = f.Inventories.GetOrCreate(f.HouseholdId).Lots.Count;
            int breadBefore = f.BreadUnits();
            int balanceBefore = f.Balance();
            int storeBefore = f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId);
            AssertSweepAgrees(readModel, personId);
            AssertSweepAgrees(readModel, otherPersonId);
            readModel.EquipmentHeld(personId);
            readModel.ItemsInCarriedContainers(personId);
            readModel.CarriedLoad(personId);
            readModel.RemainingCarryUnits(personId);
            Assert.AreEqual(lotCountBefore, f.Inventories.GetOrCreate(f.HouseholdId).Lots.Count, "a read sweep must not create lots (Q)");
            Assert.AreEqual(breadBefore, f.BreadUnits(), "a read sweep must not change bread units (Q)");
            Assert.AreEqual(balanceBefore, f.Balance(), "a read sweep must not move cash (Q)");
            Assert.AreEqual(storeBefore, f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId), "a read sweep must not change store stock (Q)");

            // --- Day 1: the meal leg. ---
            int receivedLotId = lot.LotId;
            IReadOnlyList<HouseholdMealRecord> meals = f.DriveNextDayMeal(work);
            Assert.Greater(meals.Count, 0, "at least one meal must be served on day 1");
            int eaten = 0;
            bool lotReferenced = false;
            foreach (HouseholdMealRecord record in meals)
            {
                if (record == null || record.ItemsConsumed == null) continue;
                foreach (MealItemConsumption consumed in record.ItemsConsumed)
                {
                    if (consumed != null
                        && string.Equals(consumed.ItemId, HouseholdItemCatalog.BreadId, StringComparison.Ordinal)
                        && consumed.LotId == receivedLotId)
                    {
                        lotReferenced = true;
                        eaten += consumed.QuantityUnits;
                    }
                }
            }

            Assert.IsTrue(lotReferenced, "at least one day-1 meal record must reference the purchased lot id " + receivedLotId);
            Assert.Greater(eaten, 0, "the day-1 meals must have eaten bread from the purchased lot");
            Assert.AreEqual(14 - eaten, f.BreadUnits(), "the household must hold 14 loaves minus the loaves eaten");
            Assert.AreEqual(0, readModel.CarriedItems(personId).Count, "after the meal the person still carries nothing");
        }

        [Test]
        public void PlayerPerson_PurchaseToMeal_ReadModelSequence()
        {
            // Acceptance B, player side: person id 101 drives the S6-1
            // sequence through the read model.
            SinglePersonPurchaseToMeal_ReadModelSequence(female: false, personId: 101);
        }

        [Test]
        public void NpcPerson_PurchaseToMeal_ReadModelSequence()
        {
            // Acceptance B, NPC side: person id 202 drives the SAME S6-1
            // sequence helper through the SAME read-model type — no
            // special-case branch for player vs NPC anywhere in the path.
            SinglePersonPurchaseToMeal_ReadModelSequence(female: true, personId: 202);
        }

        [Test]
        public void UnknownPerson_ReturnsEmptyNeverErrors()
        {
            Fixture f = Fixture.CreateSingle(female: false, personId: 1);
            PersonInventoryReadModel readModel = ReadModelFor(f);
            foreach (int unknownId in new[] { 999, -1, 0 })
            {
                Assert.AreEqual(0, readModel.CarriedItems(unknownId).Count, "an unknown person carries nothing");
                Assert.AreEqual(0, readModel.HouseholdLotsOwnedBy(unknownId).Count, "an unknown person owns no lots");
                Assert.AreEqual(0, readModel.EquipmentHeld(unknownId).Count, "an unknown person holds no equipment");
                Assert.AreEqual(0, readModel.CarriedLoad(unknownId).Resolve().TotalUnits, "an unknown person's carried load is empty");
                Assert.AreEqual(50, readModel.RemainingCarryUnits(unknownId).UnitsRemaining, "an unknown person reports the full carry cap");
                Assert.AreEqual(0, readModel.ItemsInCarriedContainers(unknownId).Items.Count, "an unknown person has nothing in containers");
            }
        }

        [Test]
        public void HouseholdLotsOwnedBy_FiltersByOwnerAndReportsHeldUnits()
        {
            Fixture f = Fixture.CreateSingle(female: false, personId: 1);
            HouseholdInventory inventory = f.Inventories.GetOrCreate(f.HouseholdId);
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.BreadId, 10, 0, "purchased 10u bread from General Store day 0 receipt r-1", ownerPersonId: 1));
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.FlourId, 5, 0, "purchased 5u flour from General Store day 0 receipt r-2", ownerPersonId: 2));
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.BreadId, 7, 1, "purchased 7u bread from General Store day 1 receipt r-3"));
            Assert.IsNull(inventory.HoldForCommitment(HouseholdItemCatalog.BreadId, 4, "test commitment"));

            PersonInventoryReadModel readModel = ReadModelFor(f);
            IReadOnlyList<PersonInventoryReadModel.OwnedHouseholdLot> ownedBy1 = readModel.HouseholdLotsOwnedBy(1);
            Assert.AreEqual(1, ownedBy1.Count, "only the lot owned by person 1 is listed for person 1");
            Assert.AreEqual(HouseholdItemCatalog.BreadId, ownedBy1[0].ItemId, "the owned lot is bread");
            Assert.AreEqual(f.HouseholdId, ownedBy1[0].HouseholdId, "the owned lot names its household");
            Assert.AreEqual(1, ownedBy1[0].OwnerPersonId, "the owned lot names its person owner");
            // The 4-unit hold draws FIFO from the oldest bread lot — the
            // person-1 lot — so 6 remain available and 4 are held.
            Assert.AreEqual(6, ownedBy1[0].AvailableUnits, "available units exclude the held commitment units");
            Assert.AreEqual(4, ownedBy1[0].HeldUnits, "held units are reported, not hidden");
            Assert.AreEqual(10, ownedBy1[0].QuantityUnits, "quantity is the full lot size");
            Assert.AreEqual(0, ownedBy1[0].AcquiredDayIndex, "the acquired day comes from the lot");
            Assert.IsTrue(ownedBy1[0].SourceLabel.Contains("r-1"), "the source label carries the receipt, got: " + ownedBy1[0].SourceLabel);
            Assert.AreEqual(PersonItemCondition.Unknown, ownedBy1[0].Condition,
                "a household lot carries no condition field — Unknown, never a default");

            IReadOnlyList<PersonInventoryReadModel.OwnedHouseholdLot> ownedBy2 = readModel.HouseholdLotsOwnedBy(2);
            Assert.AreEqual(1, ownedBy2.Count, "person 2 sees only their own flour lot");
            Assert.AreEqual(HouseholdItemCatalog.FlourId, ownedBy2[0].ItemId, "person 2 owns the flour lot");
            Assert.AreEqual(5, ownedBy2[0].AvailableUnits, "person 2's lot is unheld");

            Assert.AreEqual(0, readModel.HouseholdLotsOwnedBy(3).Count,
                "a person with no owned lots gets an empty list, and the unowned household lot is never listed as anyone's");
            AssertSweepAgrees(readModel, 1);
        }

        [Test]
        public void HouseholdLotsOwnedBy_ScansEveryHousehold()
        {
            Fixture f = Fixture.CreateSingle(female: false, personId: 1);
            HouseholdInventory home = f.Inventories.GetOrCreate(7);
            HouseholdInventory other = f.Inventories.GetOrCreate(8);
            Assert.IsNull(home.AddLot(HouseholdItemCatalog.BreadId, 3, 0, "household 7 lot", ownerPersonId: 1));
            Assert.IsNull(other.AddLot(HouseholdItemCatalog.FlourId, 4, 2, "household 8 lot", ownerPersonId: 1));
            Assert.IsNull(other.AddLot(HouseholdItemCatalog.SaltId, 9, 2, "household 8 lot, other owner", ownerPersonId: 5));

            PersonInventoryReadModel readModel = ReadModelFor(f);
            IReadOnlyList<PersonInventoryReadModel.OwnedHouseholdLot> owned = readModel.HouseholdLotsOwnedBy(1);
            Assert.AreEqual(2, owned.Count, "owned lots are found across every household in the registry");
            var households = new HashSet<int>();
            foreach (PersonInventoryReadModel.OwnedHouseholdLot lot in owned) households.Add(lot.HouseholdId);
            Assert.IsTrue(households.Contains(7), "the household-7 lot is found");
            Assert.IsTrue(households.Contains(8), "the household-8 lot is found");
        }

        [Test]
        public void OwnershipAndCustody_RemainDistinct()
        {
            // Acceptance D, seeded half: a lot OWNED by person 1 while
            // person 1 carries nothing — ownership without custody — and
            // a lot owned by person 2 that person 1 never sees.
            Fixture f = Fixture.CreateSingle(female: false, personId: 1);
            HouseholdInventory inventory = f.Inventories.GetOrCreate(f.HouseholdId);
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.BreadId, 6, 0, "person 1's own bread", ownerPersonId: 1));
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.FlourId, 2, 0, "person 2's own flour", ownerPersonId: 2));

            PersonInventoryReadModel readModel = ReadModelFor(f);
            Assert.AreEqual(0, readModel.CarriedItems(1).Count, "ownership of a lot is not custody of it: person 1 carries nothing");
            Assert.AreEqual(1, readModel.HouseholdLotsOwnedBy(1).Count, "person 1 owns one lot");
            Assert.AreEqual(0, readModel.HouseholdLotsOwnedBy(1)[0].HeldUnits, "the owned lot is unheld");
            Assert.AreEqual(1, readModel.HouseholdLotsOwnedBy(2).Count, "person 2's lot is listed for person 2 only");
            Assert.AreEqual(0, readModel.CarriedItems(2).Count, "person 2 likewise carries nothing");
        }

        [Test]
        public void EquipmentHeld_ReturnsGrantsWithCustodyDetail()
        {
            Fixture f = Fixture.CreateSingle(female: false, personId: 1);
            var resolver = new EquipmentAccessResolver();
            resolver.AddGrant(new EquipmentAccessGrant
            {
                GrantId = "grant-plow", EquipmentKind = "moldboard-plow", AssetId = "plow-7",
                HolderKind = "person", HolderId = "1",
                Custody = EquipmentCustodyKind.Borrowed, GranterName = "neighbor farm", ExpiryDayIndex = 90,
            });
            resolver.AddGrant(new EquipmentAccessGrant
            {
                GrantId = "grant-reaper", EquipmentKind = "reaper", AssetId = "reaper-3",
                HolderKind = "person", HolderId = "1",
                Custody = EquipmentCustodyKind.Rented, GranterName = "implement dealer", ExpiryDayIndex = 30,
            });
            resolver.AddGrant(new EquipmentAccessGrant
            {
                GrantId = "grant-other", EquipmentKind = "threshing-separator", AssetId = "thresher-1",
                HolderKind = "person", HolderId = "2",
                Custody = EquipmentCustodyKind.ServiceProvider, GranterName = "traveling threshing outfit", ExpiryDayIndex = -1,
            });
            resolver.AddGrant(new EquipmentAccessGrant
            {
                GrantId = "grant-business", EquipmentKind = "reaper", AssetId = "reaper-9",
                HolderKind = "business", HolderId = "1",
                Custody = EquipmentCustodyKind.Borrowed, GranterName = "neighbor farm", ExpiryDayIndex = 90,
            });

            PersonInventoryReadModel readModel = ReadModelFor(f, resolver);
            IReadOnlyList<PersonInventoryReadModel.HeldEquipment> held = readModel.EquipmentHeld(1);
            Assert.AreEqual(2, held.Count, "person 1 holds exactly their two person grants — not person 2's, not the business's");
            PersonInventoryReadModel.HeldEquipment plow = null;
            PersonInventoryReadModel.HeldEquipment reaper = null;
            foreach (PersonInventoryReadModel.HeldEquipment item in held)
            {
                if (item.EquipmentKind == "moldboard-plow") plow = item;
                if (item.EquipmentKind == "reaper") reaper = item;
            }

            Assert.IsNotNull(plow, "the borrowed plow grant is listed");
            Assert.AreEqual("plow-7", plow.AssetId, "the grant names its asset");
            Assert.AreEqual(EquipmentCustodyKind.Borrowed, plow.Custody, "custody kind comes from the grant, not from ownership");
            Assert.AreEqual("neighbor farm", plow.Granter, "the grant names its granter");
            Assert.AreEqual(90, plow.ExpiryDayIndex, "the grant names its expiry day");
            Assert.IsNotNull(reaper, "the rented reaper grant is listed");
            Assert.AreEqual(EquipmentCustodyKind.Rented, reaper.Custody, "the reaper grant is rented custody");
            Assert.AreEqual(30, reaper.ExpiryDayIndex, "the reaper grant expires day 30");

            // The unfiltered view lists expired grants with their expiry;
            // the day-filtered view excludes them.
            Assert.AreEqual(2, readModel.EquipmentHeld(1, 30).Count, "day 30: both grants are still active (expiry is inclusive)");
            IReadOnlyList<PersonInventoryReadModel.HeldEquipment> day31 = readModel.EquipmentHeld(1, 31);
            Assert.AreEqual(1, day31.Count, "day 31: only the plow (expiry 90) is still active — the reaper expired day 30");
            Assert.AreEqual("moldboard-plow", day31[0].EquipmentKind, "day 31: the plow is the active grant");
            IReadOnlyList<PersonInventoryReadModel.HeldEquipment> day90 = readModel.EquipmentHeld(1, 90);
            Assert.AreEqual(1, day90.Count, "day 90: only the plow (expiry 90) is still active");
            Assert.AreEqual("moldboard-plow", day90[0].EquipmentKind, "day 90: the plow is the active grant");
            Assert.AreEqual(0, readModel.EquipmentHeld(1, 91).Count, "day 91: both grants have expired");

            Assert.AreEqual(1, readModel.EquipmentHeld(2).Count, "person 2 holds only their threshing grant");
            Assert.AreEqual(EquipmentCustodyKind.ServiceProvider, readModel.EquipmentHeld(2)[0].Custody, "person 2's grant is service-provider custody");
            Assert.AreEqual(-1, readModel.EquipmentHeld(2)[0].ExpiryDayIndex, "no expiry is recorded as -1");
        }

        [Test]
        public void EquipmentHeld_NullResolver_ReturnsEmpty()
        {
            Fixture f = Fixture.CreateSingle(female: false, personId: 1);
            PersonInventoryReadModel readModel = ReadModelFor(f, null);
            Assert.AreEqual(0, readModel.EquipmentHeld(1).Count, "a null equipment resolver means no equipment answers, never an error");
            Assert.AreEqual(0, readModel.EquipmentHeld(1, 0).Count, "the day-filtered view is likewise empty");
        }

        [Test]
        public void ItemsInCarriedContainers_AlwaysEmptyWithNamedReason()
        {
            Fixture f = Fixture.CreateSingle(female: false, personId: 1);
            PersonInventoryReadModel readModel = ReadModelFor(f);
            foreach (int personId in new[] { 1, 999 })
            {
                PersonInventoryReadModel.CarriedContainerContents contents = readModel.ItemsInCarriedContainers(personId);
                Assert.AreEqual(0, contents.Items.Count, "no item can be in a carried container — none exist");
                Assert.IsTrue(contents.Reason.Contains("No portable containers exist"),
                    "the empty answer names the reason, got: " + contents.Reason);
                Assert.IsTrue(contents.Reason.Contains("saddlebags"),
                    "the reason names the container kinds that do not exist, got: " + contents.Reason);
            }
        }

        [Test]
        public void ItemCondition_UnknownWhereAuthorityCarriesNone()
        {
            Fixture f = Fixture.CreateSingle(female: false, personId: 1);
            HouseholdInventory inventory = f.Inventories.GetOrCreate(f.HouseholdId);
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.BreadId, 4, 0, "seeded bread", ownerPersonId: 1));

            PersonInventoryReadModel readModel = ReadModelFor(f);
            Assert.AreEqual(PersonItemCondition.Unknown, readModel.HouseholdLotsOwnedBy(1)[0].Condition,
                "a household lot carries no condition field — Unknown, never a default Fresh");
            Assert.AreEqual(0, readModel.CarriedItems(1).Count, "nobody carries anything here");
        }

        [Test]
        public void BakeryConditionMapping_MapsFreshDayOldStale()
        {
            Assert.AreEqual(PersonItemCondition.Fresh, PersonInventoryReadModel.FromBakeryCondition(BakeryGoodsCondition.Fresh), "Fresh maps to Fresh");
            Assert.AreEqual(PersonItemCondition.DayOld, PersonInventoryReadModel.FromBakeryCondition(BakeryGoodsCondition.DayOld), "DayOld maps to DayOld");
            Assert.AreEqual(PersonItemCondition.Stale, PersonInventoryReadModel.FromBakeryCondition(BakeryGoodsCondition.Stale), "Stale maps to Stale");
            Assert.AreEqual(PersonItemCondition.Unknown, PersonInventoryReadModel.FromBakeryCondition(BakeryGoodsCondition.Unspecified),
                "Unspecified — the lot carries no condition — maps to Unknown");
        }

        [Test]
        public void ReadModelSweep_CannotMutateInventoryOrCreateStock()
        {
            // Acceptance Q, standalone: a full read-model sweep over a
            // stocked household changes no lot, no unit, no balance, and
            // no custody batch.
            Fixture f = Fixture.CreateSingle(female: false, personId: 1);
            f.Fund(10000);
            HouseholdInventory inventory = f.Inventories.GetOrCreate(f.HouseholdId);
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.BreadId, 14, 0, "seeded bread", ownerPersonId: 1));
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.FlourId, 5, 0, "seeded flour"));

            PersonInventoryReadModel readModel = ReadModelFor(f);
            int lotsBefore = inventory.Lots.Count;
            int breadBefore = f.BreadUnits();
            int flourBefore = inventory.GetAvailableUnits(HouseholdItemCatalog.FlourId);
            int balanceBefore = f.Balance();

            AssertSweepAgrees(readModel, 1);
            AssertSweepAgrees(readModel, 999);
            readModel.CarriedLoad(1);
            readModel.RemainingCarryUnits(1);
            readModel.EquipmentHeld(1);
            readModel.ItemsInCarriedContainers(1);

            Assert.AreEqual(lotsBefore, inventory.Lots.Count, "a read sweep must not create lots (Q)");
            Assert.AreEqual(breadBefore, f.BreadUnits(), "a read sweep must not change bread units (Q)");
            Assert.AreEqual(flourBefore, inventory.GetAvailableUnits(HouseholdItemCatalog.FlourId), "a read sweep must not change flour units (Q)");
            Assert.AreEqual(balanceBefore, f.Balance(), "a read sweep must not move cash (Q)");
            Assert.AreEqual(0, f.Loop.ActiveCustodyBatches.Count, "a read sweep must not create custody batches (Q)");
        }

        [Test]
        public void CarriedLoad_EmptyPersonResolvesToZeroUnits()
        {
            Fixture f = Fixture.CreateSingle(female: false, personId: 1);
            PersonInventoryReadModel readModel = ReadModelFor(f);
            PhysicalLoadResolution resolution = readModel.CarriedLoad(1).Resolve();
            Assert.AreEqual(0, resolution.TotalUnits, "a person carrying nothing has a zero-unit load");
            Assert.IsNull(resolution.FirstUnknownItemId, "an empty load names no unknown item");
        }

        [Test]
        public void RemainingCarryUnits_EmptyPersonReportsFullCapUnitsOnly()
        {
            Fixture f = Fixture.CreateSingle(female: false, personId: 1);
            PersonInventoryReadModel readModel = ReadModelFor(f);
            Assert.AreEqual(50, readModel.CarryCapacityUnits, "the read model answers against the live 50-unit shopping carry cap");
            PersonInventoryReadModel.PersonCarryRemaining remaining = readModel.RemainingCarryUnits(1);
            Assert.AreEqual(50, remaining.UnitsRemaining, "an empty-handed person has the full cap remaining");
            Assert.IsTrue(!remaining.MassRemainingKg.IsKnown, "no authored person mass limit exists: mass remaining is Unknown");
            Assert.IsTrue(!remaining.VolumeRemainingLiters.IsKnown, "no authored person volume limit exists: volume remaining is Unknown");
            Assert.IsTrue(remaining.Reason.Contains("50"), "the reason names the live cap, got: " + remaining.Reason);
        }

        [Test]
        public void ReadModel_ReturnsSnapshotsNotLiveLists()
        {
            Fixture f = Fixture.CreateSingle(female: false, personId: 1);
            HouseholdInventory inventory = f.Inventories.GetOrCreate(f.HouseholdId);
            Assert.IsNull(inventory.AddLot(HouseholdItemCatalog.BreadId, 4, 0, "seeded bread", ownerPersonId: 1));

            PersonInventoryReadModel readModel = ReadModelFor(f);
            IReadOnlyList<PersonInventoryReadModel.OwnedHouseholdLot> first = readModel.HouseholdLotsOwnedBy(1);
            IReadOnlyList<PersonInventoryReadModel.OwnedHouseholdLot> second = readModel.HouseholdLotsOwnedBy(1);
            Assert.IsTrue(!ReferenceEquals(first, second), "each read returns a fresh snapshot list, not a shared live list");
            Assert.AreEqual(first.Count, second.Count, "the snapshots agree");
            Assert.AreEqual(first[0].LotId, second[0].LotId, "the snapshots agree on the lot id");
        }

        [Test]
        public void ReadModel_RequiresShoppingAndInventoryAuthorities()
        {
            Fixture f = Fixture.CreateSingle(female: false, personId: 1);
            var shopping = new HouseholdShoppingReadModel(f.Loop, f.Tracker);
            Assert.Throws<ArgumentNullException>(() => new PersonInventoryReadModel(null, f.Inventories),
                "the custody authority is required");
            Assert.Throws<ArgumentNullException>(() => new PersonInventoryReadModel(shopping, null),
                "the inventory authority is required");
            var withNulls = new PersonInventoryReadModel(shopping, f.Inventories, null, null);
            Assert.AreEqual(50, withNulls.CarryCapacityUnits, "a null options argument falls back to the live default carry cap");
            Assert.AreEqual(0, withNulls.EquipmentHeld(1).Count, "a null equipment resolver is allowed and answers empty");
        }
    }
}
