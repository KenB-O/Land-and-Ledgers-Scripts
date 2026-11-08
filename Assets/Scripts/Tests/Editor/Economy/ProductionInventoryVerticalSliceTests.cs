using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Labor;
using LandLedgers.Economy.Logistics;
using LandLedgers.Economy.Storage;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Priority V1 — production inventory and custody vertical slice.
    /// The smallest legitimate end-to-end physical inventory workflow
    /// through the REAL production authorities: the shopping loop buys
    /// bread at the General Store, the shopper carries it InHand, the
    /// loop's receipt path deposits it into household storage, the
    /// next-day meal consumes from the stored lot, and save/load
    /// preserves the resulting state. Every step is driven through the
    /// production loop, registry, meal service, and save surfaces — no
    /// synthetic fixture copies a business rule.
    ///
    /// The wiring under test (all in production code, always on):
    ///   - the loop consults the F2 PhysicalCapacityEvaluator at settle
    ///     time, before InHand custody is assigned, with the live
    ///     50-unit carry cap supplied (OnFootCarryCapUnits); the
    ///     verdict is recorded on LastSettleCapacityVerdict and in diag.
    ///   - the loop owns a PersonInventoryReadModel (Slice 3) as its
    ///     production custody answer and verifies each deposit through
    ///     it (the shopper no longer carries the deposited batch).
    ///   - the loop resolves each deposited lot through the F4
    ///     LotIdentityResolver and records the canonical identity on
    ///     the trip result (ReceivedLotId / ReceivedLotKey).
    ///   - the meal service resolves each consumed lot through the F4
    ///     resolver (when set) and reports the identities it consumed
    ///     in LastMealLotIdentities.
    ///
    /// Acceptance cases pinned here: A, B, C, D, E, H, I, K, L, Q, R.
    /// Point 10 covers the RESULTING state (deposited lot, after
    /// receipt and after the meal) through the existing save surfaces.
    /// It does NOT cover a mid-trip save: the in-transit limitation is
    /// pinned honestly in MidTripSaveLoad_DropsCarriedBatch_U3Limitation
    /// (U-3, the next slice), not claimed as covered.
    ///
    /// The driving fixture is the S6-1 shape from
    /// PersonInventoryReadModelTests (single employed adult, 240-min
    /// shift 480-720, store stocked 16 loaves at 5c, funded 10000c,
    /// monitor-raised 14-loaf bread need, Slice 4 production-day drive,
    /// next-day meal), parameterized by person id so the SAME sequence
    /// helper drives a player person id and an NPC person id with no
    /// special-case branch (acceptance B). The mid-trip step is observed
    /// through the loop's MidTripObserver — which swallows observer
    /// faults, so the observer only CAPTURES snapshots into locals and
    /// every assertion runs after ExecuteBatch returns.
    /// </summary>
    [TestFixture]
    public sealed class ProductionInventoryVerticalSliceTests
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
            /// monitor. The need is 7 days of the given daily use (2/day
            /// is the S6-1 shape, 14 loaves); a larger daily use raises a
            /// larger need, which is how the over-cap test reaches 70.</summary>
            public HouseholdPurchasingNeed RaiseBreadNeedViaMonitor(int dailyUseUnits = 2)
            {
                HouseholdState household = Population.GetHousehold(HouseholdId);
                HouseholdInventory inventory = Inventories.GetOrCreate(HouseholdId);
                Monitor.Evaluate(
                    household, inventory,
                    new Dictionary<string, int> { { HouseholdItemCatalog.BreadId, dailyUseUnits } },
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

            /// <summary>Every bread lot the household holds (exactly one
            /// after a single S6-1 receipt — point 7, exactly once).</summary>
            public List<HouseholdItemLot> BreadLots()
            {
                var result = new List<HouseholdItemLot>();
                foreach (HouseholdItemLot lot in Inventories.GetOrCreate(HouseholdId).Lots)
                {
                    if (lot != null && string.Equals(lot.ItemId, HouseholdItemCatalog.BreadId, StringComparison.Ordinal))
                    {
                        result.Add(lot);
                    }
                }

                return result;
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
                out int started, List<string> workDiagnostics, int dailyUseUnits = 2)
            {
                PlanDay();
                List<WorkTask> assignedTasks = null;
                Dictionary<string, string> reservationIdByTaskId = null;
                if (workTasks != null && workTasks.Count > 0)
                {
                    ReserveWork(workTasks, personIds, minutesList, workDiagnostics,
                        out assignedTasks, out reservationIdByTaskId);
                }

                HouseholdPurchasingNeed need = RaiseBreadNeedViaMonitor(dailyUseUnits);
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

        /// <summary>A mid-trip snapshot. Captured ONLY — never asserted —
        /// inside the MidTripObserver, which the loop invokes inside a
        /// try/catch that swallows observer faults.</summary>
        private sealed class MidTripSnapshot
        {
            public bool Observed;
            public List<PersonInventoryReadModel.CarriedItem> Carried = new List<PersonInventoryReadModel.CarriedItem>();
            public int HouseholdBreadUnits;
            public PhysicalCapacityVerdict? CapacityVerdict;
            public CustodyTransferRecord PurchaseRecord;
            public PersonInventoryReadModel.PersonCarryRemaining Remaining;
            public string ObserverFault;
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

        private static bool DiagContains(IReadOnlyList<string> diagnostics, string fragment)
        {
            if (diagnostics == null) return false;
            foreach (string line in diagnostics)
            {
                if (!string.IsNullOrEmpty(line) && line.Contains(fragment)) return true;
            }

            return false;
        }

        /// <summary>
        /// The V1 end-to-end workflow, driven identically for any single
        /// person id (acceptance B: the same helper, the same production
        /// authorities, no branch on the id or on player vs NPC).
        /// Points 1-10 in one workflow: acquire through the loop (1),
        /// real InHand custody (2) reported by the loop-owned read model
        /// (3), F2 capacity consulted at settle (4), deposit through the
        /// loop's receipt path (5), person carries nothing after (6),
        /// household holds the lot exactly once (7), next-day meal
        /// consumes from the stored lot without creating stock (8),
        /// ownership/custody/identity/quantity/provenance/condition
        /// preserved (9), save/load preserves the resulting state (10).
        /// </summary>
        private void SinglePerson_VerticalSlice_EndToEnd(bool female, int personId)
        {
            Fixture f = Fixture.CreateSingle(female, personId);
            f.Fund(10000);
            var custodyLog = new CustodyTransferLog();
            f.Loop.CustodyLog = custodyLog;
            f.MealService.LotIdentityResolver = new LotIdentityResolver(f.Inventories);
            PersonInventoryReadModel readModel = f.Loop.PersonInventory;
            Assert.IsNotNull(readModel, "the loop must own its person inventory read model as the production custody answer");
            int otherPersonId = personId + 1000;

            // --- Before purchase: nothing carried, no settle consulted yet. ---
            Assert.AreEqual(0, readModel.CarriedItems(personId).Count, "before purchase the person carries nothing");
            Assert.AreEqual(0, readModel.CarriedItems(otherPersonId).Count, "another person carries nothing either");
            Assert.IsTrue(!f.Loop.LastSettleCapacityVerdict.HasValue, "before any settle the loop has consulted no capacity verdict");
            Assert.AreEqual(0, f.BreadUnits(), "the household opens with no bread");
            Assert.AreEqual(10000, f.Balance(), "the household opens funded 10000c");

            // --- Mid-trip capture: after purchase, before receipt. ---
            var midTrip = new MidTripSnapshot();
            f.Loop.MidTripObserver = (trip, batch) =>
            {
                try
                {
                    midTrip.Observed = true;
                    midTrip.Carried = new List<PersonInventoryReadModel.CarriedItem>(readModel.CarriedItems(personId));
                    midTrip.HouseholdBreadUnits = f.BreadUnits();
                    midTrip.CapacityVerdict = f.Loop.LastSettleCapacityVerdict;
                    midTrip.Remaining = readModel.RemainingCarryUnits(personId);
                    midTrip.PurchaseRecord = custodyLog.LatestForCause(
                        CustodyTransferLog.CauseRetailPurchase, batch.ReceiptId);
                }
                catch (Exception ex)
                {
                    midTrip.ObserverFault = ex.ToString();
                }
            };

            WorkTask work = f.CreateWorkTask(personId, 240);
            var workDiag = new List<string>();
            int started;
            HouseholdShoppingLoop.BatchShoppingResult batch = f.DriveProductionDay(
                new List<WorkTask> { work }, new List<int> { personId }, new List<int> { 240 },
                out started, workDiag);

            Assert.IsNull(midTrip.ObserverFault, "the mid-trip observer must not fault: " + midTrip.ObserverFault);
            Assert.IsNotNull(batch, "a plan must be created for the open bread need");
            Assert.IsTrue(batch.TripExecuted, "the post-shift trip must execute: " + batch.FailureReason);
            Assert.AreEqual(personId, batch.ShopperPersonId, "the lone adult shops");
            Assert.IsTrue(midTrip.Observed, "the MidTripObserver must have fired after purchase and before receipt");

            // Point 1 + 2 + 3, mid-trip: legitimately acquired goods in
            // hand, reported by the loop's own read model — and no double
            // count with household storage (points 7/C/K, mid-trip half).
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
            Assert.AreEqual(5, carried.UnitPriceCents, "the carried batch carries the 5c unit price");
            Assert.AreEqual(PersonItemCondition.Unknown, carried.Condition,
                "a carried batch carries provenance and a receipt, not a condition — Unknown, never a default");
            Assert.AreEqual(0, midTrip.HouseholdBreadUnits,
                "mid-trip the household holds none of the purchased bread — no teleport, no double count (C/K)");
            Assert.AreEqual(14, midTrip.Carried[0].Units + midTrip.HouseholdBreadUnits,
                "carried plus household equals the 14 purchased loaves at the mid-trip step (C/K)");
            Assert.AreEqual(36, midTrip.Remaining.UnitsRemaining,
                "mid-trip 50 - 14 carried leaves 36 units of carry cap");

            // Point 4, mid-trip: the F2 evaluator's verdict is the one
            // the loop consulted at settle time, before InHand custody
            // was assigned. 14 units fit the live 50-unit cap (H); the
            // load's mass stays Unknown because bread has no authored
            // mass (I) — the verdict answers units, never an invented kg.
            Assert.IsTrue(midTrip.CapacityVerdict.HasValue, "the loop must have consulted the capacity evaluator at settle time");
            PhysicalCapacityVerdict verdict = midTrip.CapacityVerdict.Value;
            Assert.AreEqual(PhysicalContainerKind.OnFootPerson, verdict.ContainerKind, "the settle-time query is the on-foot person leg");
            Assert.AreEqual(PhysicalCapacityOutcome.Fits, verdict.Outcome, "14 loaves fit the live 50-unit carry cap: " + verdict.Reason);
            Assert.AreEqual(14, verdict.Load.TotalUnits, "the consulted load is the 14 purchased loaves");
            Assert.IsTrue(!verdict.Load.IsMassKnown, "bread mass is Unknown, so the consulted load mass is Unknown (I)");
            Assert.IsTrue(verdict.Reason.Contains("50"), "the verdict must name the live 50-unit cap, got: " + verdict.Reason);
            Assert.IsTrue(DiagContains(f.Diagnostics, "[V1] capacity consulted at settle"),
                "the settle-time capacity consultation must be recorded in diagnostics");

            // The F3 log recorded the purchase from inside the live
            // settle path (E): one retail-purchase record, same receipt,
            // store to shopper, 14 loaves.
            Assert.IsNotNull(midTrip.PurchaseRecord, "the custody log must have recorded the retail purchase at settle time");
            Assert.AreEqual(CustodyTransferLog.CauseRetailPurchase, midTrip.PurchaseRecord.CauseKind, "the purchase record kind is retail-purchase");
            Assert.AreEqual(carried.ReceiptId, midTrip.PurchaseRecord.CauseId, "the purchase record is keyed by the settlement receipt id");
            Assert.AreEqual(14, midTrip.PurchaseRecord.Units, "the purchase record moves 14 loaves");
            Assert.AreEqual(CustodyTransferLog.BusinessCustodian("store-1"), midTrip.PurchaseRecord.FromCustodian, "the purchase record names the store as the source custodian");
            Assert.AreEqual(CustodyTransferLog.PersonCustodian(personId), midTrip.PurchaseRecord.ToCustodian, "the purchase record names the shopper as the receiving custodian");

            // --- After receipt: the person carries nothing, the household holds the lot. ---
            Assert.AreEqual(0, readModel.CarriedItems(personId).Count, "after receipt the person carries nothing (point 6)");
            Assert.AreEqual(50, readModel.RemainingCarryUnits(personId).UnitsRemaining, "after receipt the full carry cap is free again");
            Assert.AreEqual(0, f.Loop.ActiveCustodyBatches.Count, "no custody batch may be left stranded after receipt");
            List<HouseholdItemLot> breadLots = f.BreadLots();
            Assert.AreEqual(1, breadLots.Count, "the household must hold exactly one bread lot — never counted as both carried and stored, never twice in storage (point 7)");
            HouseholdItemLot lot = breadLots[0];
            Assert.AreEqual(14, lot.QuantityUnits, "the household lot holds the 14 purchased loaves");
            Assert.IsTrue(lot.SourceLabel.Contains("purchased 14u bread from"),
                "the lot must carry purchase provenance, got: " + lot.SourceLabel);

            // Point 5 + 9, deposit identity: the receipt path resolved
            // the deposited lot through the F4 resolver and recorded its
            // canonical identity on the trip result — the lot's own
            // provenance key, not a bare id.
            ShoppingTripResult breadResult = FindNeedResult(batch, HouseholdItemCatalog.BreadId);
            Assert.IsNotNull(breadResult, "the batch must carry its own bread result");
            Assert.IsTrue(breadResult.Success, breadResult.FailureReason);
            Assert.AreEqual(lot.LotId, breadResult.ReceivedLotId,
                "the trip result must name the deposited lot id, got " + breadResult.ReceivedLotId + " for lot " + lot.LotId);
            string expectedKey = LotIdentity.CanonicalProvenanceKey(
                CustodyTransferLog.HouseholdInventoryAuthority, lot.LotId.ToString());
            Assert.AreEqual(expectedKey, breadResult.ReceivedLotKey,
                "the trip result must name the deposited lot's canonical provenance key");
            Assert.IsTrue(lot.SourceLabel.Contains(expectedKey),
                "the lot's own SourceLabel must carry its canonical key, got: " + lot.SourceLabel);
            Assert.IsTrue(DiagContains(f.Diagnostics, "[V1] deposit identity:"),
                "the deposit identity resolution must be recorded in diagnostics");
            Assert.IsTrue(DiagContains(f.Diagnostics, "[V1] deposit verified via PersonInventoryReadModel"),
                "the deposit must be verified through the person inventory read model in diagnostics");

            // The F4 resolver resolves the deposited lot from the live
            // authorities (E): same receipt id, same provenance key, same
            // custodian and owner the lot authority reports.
            var resolver = new LotIdentityResolver(f.Inventories);
            LotIdentity identity = resolver.Resolve(new LotKey(
                CustodyTransferLog.HouseholdInventoryAuthority, lot.LotId.ToString()));
            Assert.IsNotNull(identity, "the F4 resolver must resolve the deposited lot");
            Assert.AreEqual(HouseholdItemCatalog.BreadId, identity.ItemId, "the resolved lot is bread");
            Assert.AreEqual(14, identity.Units, "the resolved lot holds 14 loaves");
            Assert.AreEqual(CustodyTransferLog.HouseholdCustodian(f.HouseholdId), identity.Custodian, "the resolved lot custodian is the household");
            Assert.AreEqual(CustodyTransferLog.HouseholdCustodian(f.HouseholdId), identity.Owner, "the resolved lot owner is the household (OwnerPersonId -1)");
            Assert.AreEqual(expectedKey, identity.ProvenanceKey, "the resolved provenance key is the deposited lot's key");
            Assert.AreEqual(string.Empty, identity.ProvenanceNote, "a keyed lot carries no provenance gap note");

            // The F3 log recorded the receipt from inside the live
            // receipt path: one household-receipt record, keyed by the
            // carried batch id, shopper to household, 14 loaves.
            CustodyTransferRecord receiptRecord = custodyLog.LatestForCause(
                CustodyTransferLog.CauseHouseholdReceipt, carried.BatchId);
            Assert.IsNotNull(receiptRecord, "the custody log must have recorded the household receipt");
            Assert.AreEqual(14, receiptRecord.Units, "the receipt record moves 14 loaves");
            Assert.AreEqual(CustodyTransferLog.PersonCustodian(personId), receiptRecord.FromCustodian, "the receipt record names the shopper as the source custodian");
            Assert.AreEqual(CustodyTransferLog.HouseholdCustodian(f.HouseholdId), receiptRecord.ToCustodian, "the receipt record names the household as the receiving custodian");

            // Point 9, ownership vs custody (D): the receipt books the
            // lot to the HOUSEHOLD (OwnerPersonId -1), never to the
            // shopper as a person — so the lot is not listed as owned by
            // the shopper, while custody (holder was the shopper, now
            // nobody) stays a separate fact.
            Assert.AreEqual(-1, lot.OwnerPersonId, "the loop receipt books the lot to the household, not to a person owner");
            Assert.AreEqual(0, readModel.HouseholdLotsOwnedBy(personId).Count,
                "the household-owned lot is not the shopper's personal lot (D)");

            // Point 9, condition: the authorities on this path carry no
            // condition — the retail bread lot has no BakeryGoodsCondition
            // (it is store stock, not a bakery lot) and its household
            // QualityBand facet is empty. That emptiness is preserved
            // through purchase and deposit, never replaced by a default.
            Assert.AreEqual(string.Empty, lot.QualityBand,
                "the deposited lot's QualityBand facet must survive the deposit unchanged (empty in, empty out — no invented condition)");
            Assert.AreEqual(PersonItemCondition.Unknown, carried.Condition,
                "the carried batch reports Unknown condition, never a default Fresh");

            // Acceptance R: the purchase settles exactly as S6-1 does.
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

            // Acceptance Q, after receipt: the wired queries change
            // nothing — a full sweep of the loop-owned read model, the
            // resolver, and a re-evaluated capacity query leaves lot
            // counts, bread units, balance, and store stock identical.
            int lotCountBefore = f.Inventories.GetOrCreate(f.HouseholdId).Lots.Count;
            int breadBefore = f.BreadUnits();
            int balanceBefore = f.Balance();
            int storeBefore = f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId);
            readModel.CarriedItems(personId);
            readModel.CarriedItems(otherPersonId);
            readModel.CarriedLoad(personId);
            readModel.RemainingCarryUnits(personId);
            readModel.HouseholdLotsOwnedBy(personId);
            readModel.EquipmentHeld(personId);
            readModel.ItemsInCarriedContainers(personId);
            IReadOnlyList<LotIdentity> allBreadFirst = resolver.ResolveAll(HouseholdItemCatalog.BreadId);
            IReadOnlyList<LotIdentity> allBreadSecond = resolver.ResolveAll(HouseholdItemCatalog.BreadId);
            Assert.AreEqual(allBreadFirst.Count, allBreadSecond.Count, "two resolver sweeps must agree on the bread identities");
            Assert.AreEqual(1, allBreadFirst.Count, "the resolver must find exactly one bread identity — the deposited lot, never a carried double");
            var qLoad = new PhysicalLoad(new[] { (HouseholdItemCatalog.BreadId, 14) });
            var qContext = PhysicalCapacityEvaluationContext.Default;
            qContext.OnFootCarryCapUnits = 50;
            PhysicalCapacityVerdict qVerdict = f.Loop.CapacityEvaluator.Evaluate(
                new PhysicalCapacityQuery(PhysicalContainerKind.OnFootPerson, "person:" + personId, qLoad, 0), qContext);
            Assert.IsTrue(qVerdict.Fits, "re-evaluating the same 14-loaf load against the same 50-unit cap must still fit: " + qVerdict.Reason);
            Assert.AreEqual(lotCountBefore, f.Inventories.GetOrCreate(f.HouseholdId).Lots.Count, "a query sweep must not create lots (Q)");
            Assert.AreEqual(breadBefore, f.BreadUnits(), "a query sweep must not change bread units (Q)");
            Assert.AreEqual(balanceBefore, f.Balance(), "a query sweep must not move cash (Q)");
            Assert.AreEqual(storeBefore, f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId), "a query sweep must not change store stock (Q)");

            // --- Day 1: the meal leg (point 8). ---
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
            Assert.AreEqual(14 - eaten, f.BreadUnits(), "the household must hold 14 loaves minus the loaves eaten — consumption creates and duplicates nothing");
            Assert.AreEqual(0, readModel.CarriedItems(personId).Count, "after the meal the person still carries nothing");

            // The meal service resolved the consumed lot through the F4
            // resolver at consumption time: the consumed identity is the
            // deposited lot's canonical key, not a bare lot id.
            bool mealIdentityFound = false;
            foreach (LotIdentity mealIdentity in f.MealService.LastMealLotIdentities)
            {
                if (mealIdentity != null && mealIdentity.Key.Equals(new LotKey(
                    CustodyTransferLog.HouseholdInventoryAuthority, receivedLotId.ToString())))
                {
                    mealIdentityFound = true;
                    Assert.AreEqual(HouseholdItemCatalog.BreadId, mealIdentity.ItemId, "the meal-consumed identity is bread");
                    Assert.AreEqual(expectedKey, mealIdentity.ProvenanceKey, "the meal-consumed identity carries the deposited lot's provenance key");
                }
            }

            Assert.IsTrue(mealIdentityFound,
                "the meal service must have resolved the consumed lot through the F4 resolver (LastMealLotIdentities)");

            // --- Point 10 + acceptance L: save after receipt and load
            // into fresh state. The RESULTING state survives: the same
            // lot, the post-meal quantity, the provenance key, the
            // custodian, and the settlement.
            var savedInventoryStates = new List<HouseholdInventoryState>();
            var savedLedgerStates = new List<HouseholdLedgerState>();
            var savedMeals = new List<HouseholdMealRecord>();
            var savedMissed = new List<MissedMealRecord>();
            var savedPreparations = new List<MealPreparationRecord>();
            f.Inventories.ExportState(savedInventoryStates);
            f.Ledgers.ExportState(savedLedgerStates);
            f.MealLog.ExportState(savedMeals, savedMissed, savedPreparations);

            var restoredInventories = new HouseholdInventoryRegistry();
            restoredInventories.ImportState(savedInventoryStates);
            var restoredLedgers = new HouseholdLedgerRegistry();
            restoredLedgers.ImportState(savedLedgerStates);
            var restoredMealLog = new HouseholdMealLogRegistry();
            restoredMealLog.ImportState(savedMeals, savedMissed, savedPreparations);

            HouseholdInventory restoredInventory = restoredInventories.GetOrCreate(f.HouseholdId);
            HouseholdItemLot restoredLot = null;
            foreach (HouseholdItemLot candidate in restoredInventory.Lots)
            {
                if (candidate != null && candidate.LotId == receivedLotId)
                {
                    restoredLot = candidate;
                }
            }

            Assert.IsNotNull(restoredLot, "the deposited lot must survive save/load");
            Assert.AreEqual(14 - eaten, restoredLot.QuantityUnits, "the restored lot holds the post-meal quantity");
            Assert.AreEqual(lot.SourceLabel, restoredLot.SourceLabel, "the restored lot keeps its provenance, including the canonical key");
            Assert.IsTrue(restoredLot.SourceLabel.Contains(expectedKey), "the restored lot keeps its canonical provenance key");
            Assert.AreEqual(-1, restoredLot.OwnerPersonId, "the restored lot keeps its household ownership");
            Assert.AreEqual(string.Empty, restoredLot.QualityBand, "the restored lot keeps its QualityBand facet");
            Assert.AreEqual(1, f.BreadLots().Count, "the live household still holds exactly one bread lot");
            int restoredBreadLots = 0;
            foreach (HouseholdItemLot candidate in restoredInventory.Lots)
            {
                if (candidate != null && string.Equals(candidate.ItemId, HouseholdItemCatalog.BreadId, StringComparison.Ordinal))
                {
                    restoredBreadLots++;
                }
            }

            Assert.AreEqual(1, restoredBreadLots, "the restored household holds exactly one bread lot — the lot was not duplicated by the round-trip (L)");
            Assert.AreEqual(f.Balance(), restoredLedgers.GetOrCreate(f.HouseholdId).GetBalanceCents(),
                "the restored ledger keeps the settled balance (L)");
            bool restoredMealReferencesLot = false;
            foreach (HouseholdMealRecord record in restoredMealLog.GetMeals(f.HouseholdId))
            {
                if (record == null || record.ItemsConsumed == null) continue;
                foreach (MealItemConsumption consumed in record.ItemsConsumed)
                {
                    if (consumed != null && consumed.LotId == receivedLotId)
                    {
                        restoredMealReferencesLot = true;
                    }
                }
            }

            Assert.IsTrue(restoredMealReferencesLot, "the restored meal log must still reference the deposited lot id (L)");

            // The F4 resolver resolves the deposited lot from the
            // RESTORED authorities, with the same identity.
            var restoredResolver = new LotIdentityResolver(restoredInventories);
            LotIdentity restoredIdentity = restoredResolver.Resolve(new LotKey(
                CustodyTransferLog.HouseholdInventoryAuthority, receivedLotId.ToString()));
            Assert.IsNotNull(restoredIdentity, "the F4 resolver must resolve the deposited lot from the restored authorities");
            Assert.AreEqual(14 - eaten, restoredIdentity.Units, "the restored identity carries the post-meal quantity");
            Assert.AreEqual(expectedKey, restoredIdentity.ProvenanceKey, "the restored identity carries the same provenance key");
            Assert.AreEqual(CustodyTransferLog.HouseholdCustodian(f.HouseholdId), restoredIdentity.Custodian, "the restored identity keeps its custodian");
        }

        [Test]
        public void PlayerPerson_VerticalSlice_EndToEnd()
        {
            // Acceptance B, player side: person id 101 drives the S6-1
            // sequence through the production loop and the wired
            // foundations.
            SinglePerson_VerticalSlice_EndToEnd(female: false, personId: 101);
        }

        [Test]
        public void NpcPerson_VerticalSlice_EndToEnd()
        {
            // Acceptance B, NPC side: person id 202 drives the SAME
            // sequence helper through the SAME production authorities —
            // no special-case branch for player vs NPC anywhere in the
            // path.
            SinglePerson_VerticalSlice_EndToEnd(female: true, personId: 202);
        }

        /// <summary>
        /// Point 4, over-cap leg: a 70-loaf need against 60 loaves of
        /// store stock buys 60 on foot — over the live 50-unit cap, with
        /// no delivery offered. The loop consults the F2 evaluator at
        /// settle time and the verdict is DoesNotFit, naming the cap,
        /// and the over-cap outcome is RECORDED in diagnostics — the
        /// purchase is not refused, per the loop's existing enforcement
        /// (the cap gates the delivery decision at candidate evaluation
        /// and the affordability-downsize clamp; on the affordable
        /// no-delivery path SettleAtCounter has no cap refusal). A
        /// refusal here would change purchase quantities, which is a
        /// shopping-rule change, not wiring — so this test pins the
        /// recorded outcome honestly instead of inventing a refusal.
        /// </summary>
        [Test]
        public void OverCapPurchase_CapacityVerdictRecordedNotSilentlyPassed()
        {
            Fixture f = Fixture.CreateSingle(female: false, personId: 101);
            f.Fund(10000);
            f.Port.ItemStock[HouseholdItemCatalog.BreadId] = 60;
            var midTrip = new MidTripSnapshot();
            f.Loop.MidTripObserver = (trip, batch) =>
            {
                try
                {
                    midTrip.Observed = true;
                    midTrip.Carried = new List<PersonInventoryReadModel.CarriedItem>(
                        f.Loop.PersonInventory.CarriedItems(101));
                    midTrip.CapacityVerdict = f.Loop.LastSettleCapacityVerdict;
                }
                catch (Exception ex)
                {
                    midTrip.ObserverFault = ex.ToString();
                }
            };

            WorkTask work = f.CreateWorkTask(101, 240);
            var workDiag = new List<string>();
            int started;
            HouseholdShoppingLoop.BatchShoppingResult batch = f.DriveProductionDay(
                new List<WorkTask> { work }, new List<int> { 101 }, new List<int> { 240 },
                out started, workDiag, dailyUseUnits: 10);

            Assert.IsNull(midTrip.ObserverFault, "the mid-trip observer must not fault: " + midTrip.ObserverFault);
            Assert.IsNotNull(batch, "a plan must be created for the open bread need");
            Assert.IsTrue(batch.TripExecuted, "the over-cap trip must execute: " + batch.FailureReason);
            Assert.IsTrue(midTrip.Observed, "the MidTripObserver must have fired");
            Assert.AreEqual(1, midTrip.Carried.Count, "mid-trip the person carries exactly one batch");
            Assert.AreEqual(60, midTrip.Carried[0].Units, "the over-cap purchase carries 60 loaves");

            Assert.IsTrue(midTrip.CapacityVerdict.HasValue, "the loop must have consulted the capacity evaluator at settle time");
            PhysicalCapacityVerdict verdict = midTrip.CapacityVerdict.Value;
            Assert.AreEqual(PhysicalCapacityOutcome.DoesNotFit, verdict.Outcome,
                "60 loaves exceed the live 50-unit carry cap, so the consulted verdict must refuse: " + verdict.Reason);
            Assert.AreEqual(60, verdict.Load.TotalUnits, "the consulted load is the 60 purchased loaves");
            Assert.IsTrue(verdict.Reason.Contains("50"), "the refusal must name the live 50-unit cap, got: " + verdict.Reason);
            Assert.IsTrue(DiagContains(f.Diagnostics, "[V1] capacity consulted at settle"),
                "the over-cap consultation must be recorded in diagnostics");

            ShoppingTripResult breadResult = FindNeedResult(batch, HouseholdItemCatalog.BreadId);
            Assert.IsNotNull(breadResult, "the batch must carry its own bread result");
            Assert.AreEqual(60, breadResult.UnitsAcquired, "existing enforcement proceeds with the 60-loaf purchase — recorded, not refused");
            Assert.AreEqual(300, breadResult.BuyerOutflowCents, "60 loaves x 5c");
            Assert.AreEqual(1, f.BreadLots().Count, "the household holds exactly one bread lot after the over-cap receipt");
            Assert.AreEqual(60, f.BreadUnits(), "the household holds the 60 purchased loaves");
        }

        /// <summary>
        /// Point 10, in-transit limitation (U-3, pinned honestly): a
        /// save/load taken MID-TRIP — while the 14 paid-for loaves are
        /// still InHand — drops the carried batch. The loop keeps the
        /// batch only in its private custodyBatches, which no save
        /// surface carries, and the coordinator's save DTO carries no
        /// trips or batches (verified in F5). The resulting state is
        /// still correct — the live trip completes and the household
        /// holds 14 — so the loss is a save/load artifact, not a trip
        /// failure. This test records the limitation; it does NOT claim
        /// point 10 covers the in-transit state. Fixing it is U-3, the
        /// next slice.
        /// </summary>
        [Test]
        public void MidTripSaveLoad_DropsCarriedBatch_U3Limitation()
        {
            Fixture f = Fixture.CreateSingle(female: false, personId: 101);
            f.Fund(10000);
            int restoredBreadUnits = -1;
            int restoredLotCount = -1;
            bool restoredLotCarriesReceipt = false;
            string observerFault = null;
            string carriedReceiptId = null;
            f.Loop.MidTripObserver = (trip, batch) =>
            {
                try
                {
                    carriedReceiptId = batch.ReceiptId;
                    var savedInventoryStates = new List<HouseholdInventoryState>();
                    var savedLedgerStates = new List<HouseholdLedgerState>();
                    var savedMeals = new List<HouseholdMealRecord>();
                    var savedMissed = new List<MissedMealRecord>();
                    var savedPreparations = new List<MealPreparationRecord>();
                    f.Inventories.ExportState(savedInventoryStates);
                    f.Ledgers.ExportState(savedLedgerStates);
                    f.MealLog.ExportState(savedMeals, savedMissed, savedPreparations);
                    var restoredInventories = new HouseholdInventoryRegistry();
                    restoredInventories.ImportState(savedInventoryStates);
                    HouseholdInventory restored = restoredInventories.GetOrCreate(f.HouseholdId);
                    restoredBreadUnits = restored.GetAvailableUnits(HouseholdItemCatalog.BreadId);
                    restoredLotCount = restored.Lots.Count;
                    foreach (HouseholdItemLot candidate in restored.Lots)
                    {
                        if (candidate != null && !string.IsNullOrEmpty(carriedReceiptId)
                            && candidate.SourceLabel != null && candidate.SourceLabel.Contains(carriedReceiptId))
                        {
                            restoredLotCarriesReceipt = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    observerFault = ex.ToString();
                }
            };

            WorkTask work = f.CreateWorkTask(101, 240);
            var workDiag = new List<string>();
            int started;
            HouseholdShoppingLoop.BatchShoppingResult batch = f.DriveProductionDay(
                new List<WorkTask> { work }, new List<int> { 101 }, new List<int> { 240 },
                out started, workDiag);

            Assert.IsNull(observerFault, "the mid-trip save/load capture must not fault: " + observerFault);
            Assert.IsNotNull(batch, "a plan must be created for the open bread need");
            Assert.IsTrue(batch.TripExecuted, "the trip must execute: " + batch.FailureReason);
            Assert.IsTrue(!string.IsNullOrEmpty(carriedReceiptId), "a carried batch with a receipt id must have been observed mid-trip");
            Assert.AreEqual(0, restoredBreadUnits, "U-3: a mid-trip save/load restores zero bread — the carried batch has no save surface");
            Assert.AreEqual(0, restoredLotCount, "U-3: a mid-trip save/load restores no lots at all");
            Assert.IsTrue(!restoredLotCarriesReceipt, "U-3: no restored lot carries the purchase receipt id — the 14 paid-for loaves exist nowhere in the restored state");
            Assert.AreEqual(14, f.BreadUnits(), "the live trip still completes: the household holds the 14 loaves, so the loss is a save/load artifact, not a trip failure");
        }
    }
}
