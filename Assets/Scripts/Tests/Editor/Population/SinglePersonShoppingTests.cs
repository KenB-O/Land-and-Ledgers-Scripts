using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Labor;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    /// <summary>
    /// Plan 05 Slice 0 — discriminator tests for single-person households and
    /// realistic shopping (revised priority 2, Plan 02 extension).
    ///
    /// Every discriminator asserts the DESIRED fixed behaviour and must FAIL on
    /// the abd99de baseline in the way stated; controls assert current correct
    /// behaviour and must PASS. Expected Slice 0 outcome (rev1 section 4):
    /// tests 1, 2, 3 (240-min) FAIL at HEAD — 1/2 on the overlap assertion,
    /// 3 on Tomas's rest refusal; new test 3b (540 stacks) FAILS at HEAD
    /// (trip executes, started 0); tests 4 and the staggered control FAIL at
    /// HEAD on the rest mechanism (specified PASS — the stop trigger); tests
    /// 5, 6, 8 FAIL at HEAD as specified; test 9 FAILS at HEAD as specified
    /// (Slice 1); tests 7, 10, 11, off-map control, AWG-bread control PASS
    /// at HEAD and post-fix.
    ///
    /// The production drive order under test (SimulationDrivers.OnDayChanged,
    /// :1706/:1752/:1780+) is: DailyActivityPlanner.PlanHouseholdDay, then the
    /// shortage monitor + embodied shopping (DriveDailyNeeds), and LAST
    /// DriveAutonomousLabor, where ManagerWorkSchedulerDrive.StartAssignedTasks
    /// reserves Work from 08:00 stacking per person. The tests drive that
    /// component order in the harness (SimulationDrivers itself is
    /// harness-excluded) with a work task standing in for the labour system:
    /// the test creates the assigned task and its MinutesCommitted is the
    /// fixture-supplied shift; the test comment says so wherever it matters.
    ///
    /// MECHANISM NOTE (corrected from the plan's section 4b wording after
    /// the Slice 0 run — see the slice report for file:line evidence):
    /// the plan says "the shopping reservation takes ~480+ and
    /// StartAssignedTasks refuses the overlap". That is NOT what happens at
    /// HEAD, for two separate reasons, and the discriminators below fail on
    /// the real mechanisms instead:
    /// (a) BUDGET: the shopping loop assigns every trip leg to the shopper
    /// through TaskAuthority.AssignTask (HouseholdShoppingLoop.PlanTripLegs,
    /// "Assign every leg to the shopper — the TTS-1 budget gates real
    /// time"), which commits the leg minutes against the 600-min day budget.
    /// A 95-min trip leaves 505 min; a 540-min work task then cannot be
    /// assigned ("P1 has 505 of 600 daily work minutes remaining; the task
    /// needs 540"). The shopper's paid work IS displaced — by budget
    /// exhaustion, not by a reservation refusal. AMENDMENT (rev1 section
    /// 3a): the 540-min figure is a STACK TOTAL (the day's stacked
    /// assignments totalling N minutes; StartAssignedTasks stacks per
    /// person), not a single catalog task — the largest single production
    /// task is 240-min field work (AutonomousWorkGenerator.cs:62). Tests
    /// 1/2/3 therefore use realistic 240-min tasks; 540/600 totals stand
    /// only where the purpose is "no feasible window" (tests 5, 6, 8, 3b).
    /// (b) REST: a NON-shopper's Work start at 480 is refused by their own
    /// [480,510] rest sprinkle from PlanDay (default DesiredRestMinutes 60;
    /// PersonScheduleTracker.TryReserve refuses ANY active overlap, and
    /// nothing in the work path preempts rest — only the shopping loop uses
    /// TryReservePreemptingRest). "P1 is already Resting [480-510] ...
    /// cannot also do Work [480-720]." This is what fails the test 4
    /// control and the test 3 staggered control, both specified PASS.
    /// (c) The shopping reservation itself is released when the trip
    /// completes (ExecuteBatch releases it just before TripExecuted = true,
    /// HouseholdShoppingLoop.cs:879-881) and released reservations never
    /// block TryReserve — so the overlap refusal the plan describes never
    /// fires for a completed trip. The overlap assertions in tests 1-3 are
    /// therefore UNEVALUATED at HEAD in the 540-min shape (the work-start
    /// assertion fails first); they stand for the post-fix world, where work
    /// reserved first must make the search avoid the overlap — and, in the
    /// revised 240-min shape, they ARE evaluated at HEAD, because there the
    /// work task starts and the trip window [480,575] overlaps the Work
    /// window [480,720]. CRITICAL for Slice 4: moving the work reservation
    /// ahead of shopping does NOT by itself fix (b) — PlanDay still runs
    /// first and the [480,510] rest still refuses a 480 Work start — nor
    /// does it fix (a) for a shift that leaves too little budget for the
    /// trip. Rev1 section 2 answers (b) (Work preempts Resting via the
    /// existing TryReservePreemptingRest seam in StartAssignedTasks) and
    /// section 3 answers (a) (work claims the budget first; the trip then
    /// fails honestly when the remainder cannot cover it).
    /// </summary>
    [TestFixture]
    public sealed class SinglePersonShoppingTests
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

        /// <summary>Test port over a real BusinessRuntimeState. Stock reads
        /// delegate to the live state (exactly as the real port does); prices
        /// are the authored baselines (bread 5c/loaf, flour 35c/sack). Copied
        /// in miniature from StoreOfferVisibilityTests.LiveStatePort because
        /// that class is private to its fixture.</summary>
        private sealed class LiveStatePort : IItemLevelGeneralStoreTradingPort, IIdentifiedRetailStockReceivingPort
        {
            public BusinessRuntimeState State;
            public string StoreBusinessId { get; set; } = "general_store_060";
            public string StoreName { get; set; } = "General Store";
            public string LocationId { get; set; } = "building:60";
            public List<GeneralStoreCategoryOffer> OfferList = new List<GeneralStoreCategoryOffer>();
            public StoreTradingCapability Capability { get; set; } = new StoreTradingCapability();
            public int CashBalance;
            public int CreditSettlements;

            public LiveStatePort(BusinessRuntimeState state)
            {
                State = state;
                OfferList.Add(new GeneralStoreCategoryOffer
                {
                    CategoryId = "staple_food",
                    OfferedItemIds = new List<string> { HouseholdItemCatalog.BreadId },
                    StoreUnitsPerItemUnit = 1,
                    HouseholdUnitsPerStoreUnit = 1,
                    UsesItemLevelInventory = true,
                });
            }

            public IReadOnlyList<GeneralStoreCategoryOffer> CategoryOffers => OfferList;
            public StoreTradingCapability TradingCapability => Capability;
            public int CurrentCashCents => CashBalance;
            public bool OffersDelivery => false;
            public bool OffersTradeCredit => false;
            public int TradeCreditLimitCents => 0;

            private GeneralStoreCategoryOffer FindOffer(string itemId)
            {
                if (string.IsNullOrWhiteSpace(itemId)) return null;
                foreach (GeneralStoreCategoryOffer offer in OfferList)
                {
                    if (offer?.OfferedItemIds == null) continue;
                    foreach (string id in offer.OfferedItemIds)
                    {
                        if (string.Equals(id, itemId, System.StringComparison.OrdinalIgnoreCase)) return offer;
                    }
                }

                return null;
            }

            public int CategoryStockUnits(string categoryId)
            {
                CategoryStockState stock = State != null ? State.GetCategoryStock(categoryId) : null;
                return stock != null ? stock.CurrentStockUnits : 0;
            }

            public int CategoryTargetStockUnits(string categoryId)
            {
                CategoryStockState stock = State != null ? State.GetCategoryStock(categoryId) : null;
                return stock != null ? stock.TargetStockUnits : 0;
            }

            public int CategoryPricePerUnitCents(string categoryId) => 0;

            public bool TryConsumeStoreUnits(string categoryId, int units, out int consumedUnits)
            {
                consumedUnits = 0;
                return false;
            }

            public void RecordCashSettlement(string categoryId, int unitsSold, int revenueCents)
            {
                CashBalance += System.Math.Max(0, revenueCents);
            }

            public void RecordCreditSettlement(string categoryId, int unitsSold, int amountCents, string obligationId)
            {
                CreditSettlements++;
            }

            public string SpendCash(int amountCents, string purpose) => null;
            public string ReceiveStock(string categoryId, int units, string provenanceLabel) => null;
            public int CategoryReceivableUnits(string categoryId) => int.MaxValue;
            public int DeliveryFeeCents(string itemId, int units) => 0;
            public bool UsesItemLevelInventory(string itemId) => FindOffer(itemId)?.UsesItemLevelInventory == true;

            public int ItemStockUnits(string itemId)
            {
                GeneralStoreCategoryOffer offer = FindOffer(itemId);
                if (offer == null) return 0;
                return offer.UsesItemLevelInventory
                    ? (State?.GetRetailItemStockUnits(itemId, offer.CategoryId) ?? 0) / System.Math.Max(1, offer.StoreUnitsPerItemUnit)
                    : CategoryStockUnits(offer.CategoryId) / System.Math.Max(1, offer.StoreUnitsPerItemUnit);
            }

            public int ItemPricePerUnitCents(string itemId)
            {
                GeneralStoreCategoryOffer offer = FindOffer(itemId);
                if (offer == null) return 0;
                int storeUnitPrice = string.Equals(itemId, HouseholdItemCatalog.BreadId, System.StringComparison.OrdinalIgnoreCase) ? 5 : 0;
                return storeUnitPrice * System.Math.Max(1, offer.StoreUnitsPerItemUnit);
            }

            public bool TryConsumeItemStockUnits(string itemId, int units, out int consumedUnits)
            {
                consumedUnits = 0;
                GeneralStoreCategoryOffer offer = FindOffer(itemId);
                if (offer == null || State == null) return false;
                return State.TryConsumeRetailItemStockUnits(itemId, offer.CategoryId, units, out consumedUnits);
            }

            public void RecordItemCashSettlement(string categoryId, int unitsSold, int revenueCents)
            {
                CashBalance += System.Math.Max(0, revenueCents);
            }

            public string ReceiveRetailItemLot(string lotId, string itemId, string categoryId, string unitLabel,
                int units, string provenance, int dayIndex)
            {
                if (State == null) return "Store runtime unavailable.";
                return State.TryAddRetailItemLot(lotId, itemId, categoryId, unitLabel, units,
                    StoreBusinessId, StoreBusinessId, provenance, dayIndex);
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
            public int Day = 0; // Monday (day-of-week index 0)

            public static Fixture CreateSingle(bool female, double routeMiles = 2.0)
            {
                var f = new Fixture();
                f.SetupCommon(routeMiles);
                f.AddPerson(1, female ? "Anna" : "Tomas", "Solo", 34, female);
                return f;
            }

            public static Fixture CreatePair(double routeMiles = 2.0)
            {
                var f = new Fixture();
                f.SetupCommon(routeMiles);
                f.AddPerson(1, "Tomas", "Morrow", 34, false);
                f.AddPerson(2, "Anna", "Morrow", 32, true);
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

            /// <summary>Remote on-map workplace: a long foot route from home
            /// and from the store. The location delegate below resolves a
            /// person through their schedule state, mirroring production
            /// (SimulationDrivers.ResolvePersonJourneyLocation): AtWork with a
            /// workplace building resolves to the workplace, anything else to
            /// the map location the test tracks.</summary>
            public void AddRemoteWorkplace()
            {
                Journeys.RegisterLocation(new JourneyLocation("forest-camp", JourneyLocationKind.Farmstead, "Forest Camp", 20f, 0f));
                Journeys.AddEdge("home", "forest-camp", 20.0f, "forest track");
                Journeys.AddEdge("forest-camp", "store", 18.0f, "forest track");
                PersonState person = Population.GetPerson(1);
                person.workplaceBuildingId = 900;
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

            public HouseholdPurchasingNeed OpenNeed(string itemId, int units)
            {
                HouseholdPurchasingNeed need = Needs.CreateNeed(HouseholdId, itemId, Day);
                need.UnitsNeeded = units;
                need.Urgency01 = 0.9f;
                need.ReasonSummary = "test need";
                return need;
            }

            public HouseholdPurchasingNeed BreadNeed() => Needs.GetOpenNeed(HouseholdId, HouseholdItemCatalog.BreadId);

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

            /// <summary>Creates one assigned-work stand-in task for the day.
            /// The test is the stand-in for the labour system: the task's
            /// planned minutes are the fixture-supplied shift length, and
            /// StartAssignedTasks starts each person's first task at 08:00
            /// (480), exactly as the production drive does.</summary>
            public WorkTask CreateWorkTask(int personId, int minutes)
            {
                WorkTask task = Tasks.CreateTaskWithPlannedMinutes(
                    AutonomousWorkGenerator.GeneratedWorkCatalog.ProductionWorkOrderId,
                    EntityId.For(EntityKind.Business, 9001), Day, minutes);
                Assert.IsNotNull(task, "the work task must be created");
                return task;
            }

            /// <summary>Starts assigned work the production way: one
            /// WorkAssignmentReport through ManagerWorkSchedulerDrive.
            /// Returns tasks started; refusal reasons land in diagnostics.
            /// This is the COMPOSED path (reserve + assign + start together),
            /// used by tests that drive work-start without shopping; the
            /// production-day driver below uses the SPLIT path instead.</summary>
            public int StartWork(List<WorkTask> workTasks, List<int> personIds, List<int> minutesList, List<string> workDiagnostics)
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

                return ManagerWorkSchedulerDrive.StartAssignedTasks(
                    report, taskByTextId, Tasks, Tracker, Budgets, Day,
                    workDiagnostics ?? Diagnostics,
                    ManagerWorkSchedulerDrive.DefaultTaskLabel, null);
            }

            /// <summary>Plan 05 Slice 4 — SPLIT half 1: reserves + assigns
            /// the day's work (Work reservation + budget commit) without
            /// starting it, mirroring DriveWorkAssignmentBeforeShopping.
            /// Returns tasks assigned; the assigned tasks + reservation ids
            /// come back for the late StartReservedWork call.</summary>
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

            /// <summary>Plan 05 Slice 4 — SPLIT half 2: starts work
            /// previously reserved + assigned by ReserveWork, mirroring
            /// StartPreAssignedBusinessWork in DriveAutonomousLabor's
            /// per-business position (after shopping). Returns tasks started.</summary>
            public int StartReservedWork(List<WorkTask> assignedTasks,
                Dictionary<string, string> reservationIdByTaskId, List<string> workDiagnostics)
            {
                return ManagerWorkSchedulerDrive.StartReservedTasks(
                    assignedTasks, reservationIdByTaskId, Tasks, Tracker, Day,
                    workDiagnostics ?? Diagnostics);
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

            public PersonActivityReservation FindReservationAt(int personId, PersonActivityKind kind, int startMinute)
            {
                foreach (PersonActivityReservation r in Tracker.Reservations)
                {
                    if (r != null && r.PersonId == personId && r.DayIndex == Day && r.Activity == kind
                        && r.StartMinuteOfDay == startMinute)
                    {
                        return r;
                    }
                }

                return null;
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

            public int ShoppingReservationCount(int personId)
            {
                int count = 0;
                foreach (PersonActivityReservation r in Tracker.Reservations)
                {
                    if (r != null && r.PersonId == personId && r.DayIndex == Day
                        && r.Activity == PersonActivityKind.Shopping)
                    {
                        count++;
                    }
                }

                return count;
            }

            public void Fund(int cents)
            {
                HouseholdLedger ledger = Ledgers.GetOrCreate(HouseholdId);
                string problem = ledger.RecordInflow(0, cents, HouseholdIncomeSource.OwnerContribution,
                    "test-capital", "test funding", "test");
                Assert.IsNull(problem, problem);
            }

            public int Balance() => Ledgers.GetOrCreate(HouseholdId).GetBalanceCents();

            public HouseholdReserveState Reserve() =>
                Population.GetHousehold(HouseholdId).GetReserve("staple_food");

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

            /// <summary>Plan 05 Slice 6 (S6-1) — the meal leg. Advances to
            /// the next day, closes the prior day's work via CompleteTask
            /// (day-0 work must be closed at day end because StartTask
            /// refuses while the person already holds an InProgress task —
            /// Slice 5 field correction), serves the household's meals for
            /// the new day from real inventory through
            /// HouseholdMealService.ServeHouseholdDay, and returns that
            /// day's meal records from the log.</summary>
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
            /// START. Mirrors SimulationDrivers.OnDayChanged after Slice 4
            /// (DriveDailyActivityPlans :1706, then
            /// DriveWorkAssignmentBeforeShopping ahead of DriveDailyNeeds
            /// :1752, then shopping inside it, then the late start in
            /// DriveAutonomousLabor's per-business position). The split is
            /// required because StartTask refuses when the person already
            /// holds an InProgress task and the trip legs start as tasks
            /// for the shopper through the same authority — starting work
            /// whole before shopping breaks the trip itself.
            /// SimulationDrivers itself is harness-excluded; this is the
            /// harness-reachable equivalent. Returns the batch (null when
            /// no plan was created).</summary>
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

        [Test]
        public void SingleEmployedMan_ShopDisplacesPaidWork()
        {
            // Discriminator A. Production order: plan -> monitor -> shop ->
            // work-start, one 240-min (480-720) work task for the lone man.
            // 240 min is the largest single production task in the catalog
            // (field work, AutonomousWorkGenerator.cs:62), so a 95-min trip
            // fits after the shift. Desired: work starts AND the trip window
            // does not overlap the Work reservation (or no trip + need open
            // naming the shift). OBSERVED at HEAD (rev1 section 3c): the
            // shopper's rest was released by the shopping trip, so the Work
            // reservation succeeds, AssignTask (240 <= 505) succeeds, the
            // work task STARTS — and the trip window [480,575] overlaps the
            // Work window [480,720], so the overlap assertion fires.
            Fixture f = Fixture.CreateSingle(female: false);
            f.Fund(10000);
            WorkTask work = f.CreateWorkTask(1, 240);
            var workDiag = new List<string>();
            int started;
            HouseholdShoppingLoop.BatchShoppingResult batch = f.DriveProductionDay(
                new List<WorkTask> { work }, new List<int> { 1 }, new List<int> { 240 },
                out started, workDiag);

            Assert.IsNotNull(batch, "a plan must be created for the open bread need");
            Assert.IsTrue(batch.TripExecuted, "the trip must execute: " + batch.FailureReason);
            Assert.AreEqual(1, started,
                "the work task must start (refusals: " + string.Join(" | ", workDiag) + ")");
            Assert.AreEqual(TaskStatus.InProgress, work.Status, "the work task must be started, not merely assigned");
            PersonActivityReservation shopping = f.FindReservation(1, PersonActivityKind.Shopping);
            PersonActivityReservation workRes = f.FindReservation(1, PersonActivityKind.Work);
            Assert.IsNotNull(shopping, "the trip must have held a shopping reservation");
            Assert.IsNotNull(workRes, "the work task must hold a Work reservation");
            Assert.IsTrue(!Overlaps(shopping, workRes),
                "HEAD FAILURE: the trip window [" + shopping.StartMinuteOfDay + "-" + shopping.EndMinuteOfDay +
                "] overlaps the Work reservation [" + workRes.StartMinuteOfDay + "-" + workRes.EndMinuteOfDay +
                "] — the man shopped and worked the same person-time.");
        }

        [Test]
        public void SingleEmployedWoman_ShopDisplacesPaidWork()
        {
            // Discriminator B: identical to A with a woman living alone. The
            // outcome must not depend on sex (shopper preference is
            // irrelevant for a single-person household). 240-min task per
            // rev1 section 3c (catalog maximum, AutonomousWorkGenerator.cs:62);
            // at HEAD the work task starts and the overlap assertion fires.
            Fixture f = Fixture.CreateSingle(female: true);
            f.Fund(10000);
            WorkTask work = f.CreateWorkTask(1, 240);
            var workDiag = new List<string>();
            int started;
            HouseholdShoppingLoop.BatchShoppingResult batch = f.DriveProductionDay(
                new List<WorkTask> { work }, new List<int> { 1 }, new List<int> { 240 },
                out started, workDiag);

            Assert.IsNotNull(batch, "a plan must be created for the open bread need");
            Assert.IsTrue(batch.TripExecuted, "the trip must execute: " + batch.FailureReason);
            Assert.AreEqual(1, started,
                "the work task must start (refusals: " + string.Join(" | ", workDiag) + ")");
            Assert.AreEqual(TaskStatus.InProgress, work.Status, "the work task must be started, not merely assigned");
            PersonActivityReservation shopping = f.FindReservation(1, PersonActivityKind.Shopping);
            PersonActivityReservation workRes = f.FindReservation(1, PersonActivityKind.Work);
            Assert.IsNotNull(shopping, "the trip must have held a shopping reservation");
            Assert.IsNotNull(workRes, "the work task must hold a Work reservation");
            Assert.IsTrue(!Overlaps(shopping, workRes),
                "HEAD FAILURE: the trip window [" + shopping.StartMinuteOfDay + "-" + shopping.EndMinuteOfDay +
                "] overlaps the Work reservation [" + workRes.StartMinuteOfDay + "-" + workRes.EndMinuteOfDay +
                "] — the woman shopped and worked the same person-time.");
        }

        [Test]
        public void TwoEmployedAdults_SameShift_ShopDisplacesWork()
        {
            // Discriminator C. Two adults, both employed, same 240-min shift
            // (480-720) — 240 min is the catalog maximum
            // (AutonomousWorkGenerator.cs:62). Desired: both work tasks start
            // AND no trip overlaps either shift. OBSERVED at HEAD (rev1
            // section 3c): started == 1, not 2 — Anna (shopper) starts, Tomas
            // (non-shopper) is refused by his [480,510] rest sprinkle
            // ("already Resting [480-510] ... cannot also do Work
            // [480-720]"); his rest is intact because he did not shop.
            Fixture f = Fixture.CreatePair();
            f.Fund(10000);
            WorkTask work1 = f.CreateWorkTask(1, 240);
            WorkTask work2 = f.CreateWorkTask(2, 240);
            var workDiag = new List<string>();
            int started;
            HouseholdShoppingLoop.BatchShoppingResult batch = f.DriveProductionDay(
                new List<WorkTask> { work1, work2 }, new List<int> { 1, 2 }, new List<int> { 240, 240 },
                out started, workDiag);

            Assert.IsNotNull(batch, "a plan must be created for the open bread need");
            Assert.IsTrue(batch.TripExecuted, "the trip must execute: " + batch.FailureReason);
            Assert.AreEqual(2, started,
                "both work tasks must start (refusals: " + string.Join(" | ", workDiag) + ")");
            Assert.AreEqual(TaskStatus.InProgress, work1.Status, "Tomas's work task must be started");
            Assert.AreEqual(TaskStatus.InProgress, work2.Status, "Anna's work task must be started");
            PersonActivityReservation shopping = f.FindReservation(batch.ShopperPersonId, PersonActivityKind.Shopping);
            PersonActivityReservation workRes = f.FindReservation(batch.ShopperPersonId, PersonActivityKind.Work);
            Assert.IsNotNull(shopping, "the trip must have held a shopping reservation");
            Assert.IsNotNull(workRes, "the shopper must hold a Work reservation");
            Assert.IsTrue(!Overlaps(shopping, workRes),
                "HEAD FAILURE: the shopper's trip window [" + shopping.StartMinuteOfDay + "-" + shopping.EndMinuteOfDay +
                "] overlaps their Work reservation [" + workRes.StartMinuteOfDay + "-" + workRes.EndMinuteOfDay + "].");
        }

        [Test]
        public void TwoEmployedAdults_SameShift_FullDay_NoTripHonest()
        {
            // Test 3b (rev1 section 4 item 4). The ORIGINAL 540-each shape,
            // asserting the honest no-trip branch: the 540-min figure here
            // stands in for the day's stacked assignments totalling 540
            // minutes (StartAssignedTasks stacks per person), not a single
            // catalog task. Desired post-fix: both work tasks start, no trip
            // executes, need stays open, failure reason contains
            // "No shopper in any searched window" AND "work minutes left"
            // (the budget text from PersonScheduleTracker.cs:762). Predicted
            // FAIL at HEAD (trip executes, started 0). This is the
            // multi-person budget-exhaustion pin; without it the budget
            // mechanism has no two-adult test.
            Fixture f = Fixture.CreatePair();
            f.Fund(10000);
            WorkTask work1 = f.CreateWorkTask(1, 540);
            WorkTask work2 = f.CreateWorkTask(2, 540);
            var workDiag = new List<string>();
            int started;
            HouseholdShoppingLoop.BatchShoppingResult batch = f.DriveProductionDay(
                new List<WorkTask> { work1, work2 }, new List<int> { 1, 2 }, new List<int> { 540, 540 },
                out started, workDiag);

            Assert.IsNotNull(batch, "a plan must be created for the open bread need");
            Assert.IsTrue(!batch.TripExecuted,
                "HEAD FAILURE: a trip executed although both adults' stacked work totals 540 min each " +
                "(failure reason: " + batch.FailureReason + ")");
            Assert.AreEqual(2, started,
                "both work tasks must start (refusals: " + string.Join(" | ", workDiag) + ")");
            Assert.AreEqual(TaskStatus.InProgress, work1.Status, "Tomas's work task must be started");
            Assert.AreEqual(TaskStatus.InProgress, work2.Status, "Anna's work task must be started");
            Assert.IsNotNull(f.BreadNeed(), "the need must stay open");
            Assert.AreEqual(PurchasingNeedStatus.Open, f.BreadNeed().Status, "the need must stay open");
            Assert.IsTrue(batch.FailureReason.Contains("No shopper in any searched window"),
                "the failure must name the searched window, got: " + batch.FailureReason);
            Assert.IsTrue(batch.FailureReason.Contains("work minutes left"),
                "the failure must name the budget shortfall, got: " + batch.FailureReason);
        }

        [Test]
        public void TwoEmployedAdults_StaggeredShifts_FreeMemberShops()
        {
            // Control variant of C: Tomas works the full 480-1020 shift, Anna
            // has no work that day (her shift does not fall today — the free
            // adult). Desired: Anna shops, Tomas's work starts, purchase
            // settles, no overlaps. NOTE on "staggered": StartAssignedTasks
            // always starts a person's first task at 480, so the drive cannot
            // express a later-starting shift for Anna; the free-adult case is
            // the staggered shape the drive can express. OBSERVED at HEAD:
            // FAILS — Tomas's Work start is refused by his [480,510] rest
            // sprinkle ("already Resting [480-510] ... cannot also do Work
            // [480-1020]"), although Anna shops and the purchase settles.
            // Specified PASS; fails at HEAD on the rest mechanism
            // (non-shopper's [480,510] rest refuses the 480 Work start);
            // passes once Slice 4 lands the rest rule (rev1 section 2).
            Fixture f = Fixture.CreatePair();
            f.Fund(10000);
            WorkTask work1 = f.CreateWorkTask(1, 540);
            var workDiag = new List<string>();
            int started;
            HouseholdShoppingLoop.BatchShoppingResult batch = f.DriveProductionDay(
                new List<WorkTask> { work1 }, new List<int> { 1 }, new List<int> { 540 },
                out started, workDiag);

            Assert.IsNotNull(batch, "a plan must be created for the open bread need");
            Assert.IsTrue(batch.TripExecuted, "the free adult must shop: " + batch.FailureReason);
            Assert.AreEqual(2, batch.ShopperPersonId, "Anna (free) shops; Tomas is at work");
            Assert.AreEqual(1, started,
                "Tomas's work task must start (refusals: " + string.Join(" | ", workDiag) + ")");
            Assert.AreEqual(TaskStatus.InProgress, work1.Status, "Tomas's work task must be started");
            Assert.AreEqual(0, f.OpenNeedCount(HouseholdItemCatalog.BreadId), "the need must be fulfilled by the trip");
            Assert.AreEqual(10000 - 70, f.Balance(), "buyer debit must equal 14 loaves x the 5c price");
            Assert.IsFalse(f.ActiveReservationsOverlap(1), "Tomas holds no overlapping reservations");
            Assert.IsFalse(f.ActiveReservationsOverlap(2), "Anna holds no overlapping reservations");
        }

        [Test]
        public void OneAdultAvailable_BuysAndWorkStarts()
        {
            // Control D. Tomas employed with a shift ending well before
            // close (480-720, 240 min), Anna free. Desired: purchase settles
            // with Anna, Tomas's work task starts, no reservation overlaps.
            // OBSERVED at HEAD: FAILS — Tomas's Work start is refused by
            // his [480,510] rest sprinkle ("already Resting [480-510] ...
            // cannot also do Work [480-720]"), although Anna shops and the
            // purchase settles. Specified PASS; fails at HEAD on the rest
            // mechanism (non-shopper's [480,510] rest refuses the 480 Work
            // start); passes once Slice 4 lands the rest rule (rev1 section 2).
            Fixture f = Fixture.CreatePair();
            f.Fund(10000);
            WorkTask work1 = f.CreateWorkTask(1, 240);
            var workDiag = new List<string>();
            int started;
            HouseholdShoppingLoop.BatchShoppingResult batch = f.DriveProductionDay(
                new List<WorkTask> { work1 }, new List<int> { 1 }, new List<int> { 240 },
                out started, workDiag);

            Assert.IsNotNull(batch, "a plan must be created for the open bread need");
            Assert.IsTrue(batch.TripExecuted, "the free adult must shop: " + batch.FailureReason);
            Assert.AreEqual(2, batch.ShopperPersonId, "Anna (free) shops; Tomas is at work");
            Assert.AreEqual(1, started,
                "Tomas's work task must start (refusals: " + string.Join(" | ", workDiag) + ")");
            Assert.AreEqual(TaskStatus.InProgress, work1.Status, "Tomas's work task must be started");
            Assert.AreEqual(10000 - 70, f.Balance(), "buyer debit must equal 14 loaves x the 5c price");
            Assert.AreEqual(2, f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId), "seller stock must fall by 14");
            Assert.IsFalse(f.ActiveReservationsOverlap(1), "Tomas holds no overlapping reservations");
            Assert.IsFalse(f.ActiveReservationsOverlap(2), "Anna holds no overlapping reservations");
        }

        [Test]
        public void ShiftCoversEveryWindow_NoTripNoDisplacement()
        {
            // Discriminator E. Single employed adult, shift 480-1080 (600
            // min) covering every store opening minute. Desired: no trip is
            // executed, no shopping reservation is held, the work task
            // starts, the need stays open with a reason naming the shift
            // coverage. OBSERVED at HEAD: the trip is booked over the shift
            // (and the 600-min work task would then also be refused on
            // budget, 505 left after the 95-min trip), so the no-trip
            // assertion fails as specified.
            Fixture f = Fixture.CreateSingle(female: false);
            f.Fund(10000);
            WorkTask work = f.CreateWorkTask(1, 600);
            var workDiag = new List<string>();
            int started;
            HouseholdShoppingLoop.BatchShoppingResult batch = f.DriveProductionDay(
                new List<WorkTask> { work }, new List<int> { 1 }, new List<int> { 600 },
                out started, workDiag);

            Assert.IsNotNull(batch, "a plan must be created for the open bread need");
            Assert.IsTrue(!batch.TripExecuted,
                "HEAD FAILURE: a trip executed over a full-coverage shift (failure reason: " + batch.FailureReason + ")");
            Assert.AreEqual(0, f.ShoppingReservationCount(1), "no shopping reservation may be held");
            Assert.AreEqual(1, started,
                "the work task must start (refusals: " + string.Join(" | ", workDiag) + ")");
            Assert.IsNotNull(f.BreadNeed(), "the need must stay open");
            Assert.AreEqual(PurchasingNeedStatus.Open, f.BreadNeed().Status, "the need must stay open");
            Assert.IsTrue(batch.FailureReason.Contains("No shopper in any searched window"),
                "the failure must name the searched window, got: " + batch.FailureReason);
            Assert.IsTrue(batch.FailureReason.Contains("work minutes left"),
                "the failure must name the budget shortfall (rev1 section 3c), got: " + batch.FailureReason);
        }

        [Test]
        public void RemoteWorker_NotTeleportedHome()
        {
            // Discriminator F. Single employed adult assigned to a remote
            // on-map workplace (forest camp, 20 mi from home, 18 mi from the
            // store), shift covering the day. The remote assignment (work
            // task + workplaceBuildingId) is in place before shopping runs,
            // but in production order the person's schedule state at shopping
            // time is still AtHome — nobody has sent him to work yet
            // (SendWorkersToWork runs only at startup) — so the loop plans
            // from home. Desired: no trip departs from home (any trip is
            // planned from the resolved workplace with honest travel minutes,
            // or no trip happens and the need stays open). At HEAD the trip
            // departs from home, so the departure-location assertion fails.
            // The location delegate resolves through schedule state, mirroring
            // production ResolvePersonJourneyLocation; the first resolved
            // location recorded during the batch is the departure location.
            Fixture f = Fixture.CreateSingle(female: false);
            f.AddRemoteWorkplace();
            f.Fund(10000);
            WorkTask work = f.CreateWorkTask(1, 540);
            var workDiag = new List<string>();
            int started;
            HouseholdShoppingLoop.BatchShoppingResult batch = f.DriveProductionDay(
                new List<WorkTask> { work }, new List<int> { 1 }, new List<int> { 540 },
                out started, workDiag);

            Assert.IsNotNull(batch, "a plan must be created for the open bread need");
            string departure = f.ResolvedLocations.Count > 0 ? f.ResolvedLocations[0] : "(none resolved)";
            Assert.IsTrue(!batch.TripExecuted || departure == "forest-camp",
                "HEAD FAILURE: the trip departed from '" + departure + "' (home), not from the remote workplace " +
                "'forest-camp' — the worker was teleported home to shop. Work started: " + started +
                " (refusals: " + string.Join(" | ", workDiag) + ").");
            // Rev1 section 5 (R-REMOTE): post-fix this test passes via the
            // no-trip branch (work [480,1020], 60 min left < 95-min trip, no
            // window, need open) and proves nothing about departure location;
            // the location question moves to a Slice 4 location-consistency
            // test, and a workplace-departed trip is deferred to priority 6.
            if (!batch.TripExecuted)
            {
                Assert.IsNotNull(f.BreadNeed(), "with no trip the need must stay open");
            }
        }

        [Test]
        public void RemoteWorker_OffMap_NoShopper()
        {
            // Control variant of F (Plan 02 Slice 4 pin, re-asserted for a
            // single-person household): the same worker off-map on a freight
            // journey -> no shopper, need stays open, nothing moves.
            Fixture f = Fixture.CreateSingle(female: false);
            f.Fund(10000);
            f.Journeys.RegisterLocation(new JourneyLocation("gateway", JourneyLocationKind.OffMapGateway, "Depot", 4f, 0f));
            f.Journeys.AddEdge("home", "gateway", 4.0f, "depot road");
            var authority = new OffMapJourneyAuthority(f.Journeys);
            Assert.IsNotNull(authority.DepartPerson(1, "home", "Millbrook", 1, 2, "freight",
                null, null, 0, _ => 0, null, f.Diagnostics));
            Assert.IsTrue(authority.IsOffMapPerson(1), "P1 must read off-map");
            f.OffMapJourneys = authority;
            f.Loop.OffMapJourneys = authority;

            f.PlanDay();
            HouseholdPurchasingNeed need = f.RaiseBreadNeedViaMonitor();
            Assert.IsNotNull(need, "the monitor must open the bread need");
            List<HouseholdErrandPlan> plans = f.Coordinator.PlanDay(f.HouseholdId, f.Day, f.Context(), f.Diagnostics);
            Assert.AreEqual(1, plans.Count, "one plan batches the open bread need");
            HouseholdShoppingLoop.BatchShoppingResult batch =
                f.Coordinator.ExecutePlan(plans[0], f.Day, f.Context(), f.Loop, f.Options(), f.Diagnostics);

            Assert.IsNotNull(batch);
            Assert.IsTrue(!batch.TripExecuted, "no shopper is available off-map, so no trip may execute");
            Assert.IsTrue(batch.FailureReason.Contains("shopper"),
                "the failure must name the missing shopper, got: " + batch.FailureReason);
            Assert.IsNotNull(f.BreadNeed(), "the need must stay open");
            Assert.AreEqual(10000, f.Balance(), "no money may move without a shopper");
            Assert.AreEqual(16, f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId), "no stock may move without a shopper");
        }

        [Test]
        public void SundayClosed_MondayBuys_SingleHousehold()
        {
            // Control G: re-pins Plan 02 test 5 for a single-person
            // household. Sunday (day-of-week index 6) attempt fails on
            // trading hours, need stays open; Monday the identical need is
            // served. Expected PASS at HEAD.
            Fixture f = Fixture.CreateSingle(female: false);
            f.Day = 6; // Sunday
            f.Fund(10000);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.BreadId, 10);

            HouseholdShoppingLoop.BatchShoppingResult sunday = f.Loop.ExecuteBatch(
                new List<HouseholdPurchasingNeed> { need }, 6, f.Options(), "sunday-closed");
            Assert.IsFalse(sunday.TripExecuted, "Sunday trading is closed");
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
        public void NoFeasibleOpportunity_StaysOpenWithoutLooping()
        {
            // Discriminator H. The test 5 household driven for three
            // consecutive trading days with the shift still covering every
            // window. Desired per day: the batch fails with a reason naming
            // the blocker; after day 3 there is still exactly ONE open bread
            // need (no duplicates), zero trips executed, zero shopping
            // reservations held, zero growth in plan/claim records. At HEAD
            // day 1 books a trip, so the zero-trip assertion fails. The need
            // is raised through the real monitor each day (it re-opens /
            // refreshes the same single need while stock is zero; a fulfilled
            // need is never re-bought).
            Fixture f = Fixture.CreateSingle(female: false);
            f.Fund(10000);
            int trips = 0;
            int shoppingReservationsEver = 0;
            int firstSequence = -1;
            for (int day = 0; day <= 2; day++)
            {
                f.Day = day;
                f.Budgets.EnsureDay(day);
                WorkTask work = f.CreateWorkTask(1, 600);
                var workDiag = new List<string>();
                int started;
                HouseholdShoppingLoop.BatchShoppingResult batch = f.DriveProductionDay(
                    new List<WorkTask> { work }, new List<int> { 1 }, new List<int> { 600 },
                    out started, workDiag);
                if (batch != null && batch.TripExecuted) trips++;
                shoppingReservationsEver += f.ShoppingReservationCount(1);
                // Trip assertion FIRST: at HEAD day 0 books a trip, so this
                // is the specified failure ("day 1 books a trip, so the
                // zero-trip assertion fails"). The need assertions below only
                // run once no trip executes (the post-fix world), where the
                // need stays open and the same sequence persists.
                Assert.IsTrue(batch == null || !batch.TripExecuted,
                    "day " + day + " HEAD FAILURE: a trip executed although the shift covers every window");
                Assert.IsTrue(batch == null || batch.FailureReason.Contains("No shopper in any searched window"),
                    "day " + day + ": the batch must fail naming the searched window, got: " +
                    (batch == null ? "(no batch)" : batch.FailureReason));
                Assert.IsTrue(batch == null || batch.FailureReason.Contains("work minutes left"),
                    "day " + day + ": the batch failure must name the budget shortfall (rev1 section 3c), got: " +
                    (batch == null ? "(no batch)" : batch.FailureReason));
                HouseholdPurchasingNeed need = f.BreadNeed();
                Assert.IsNotNull(need, "day " + day + ": a bread need must be open");
                if (firstSequence < 0) firstSequence = need.NeedSequence;
                Assert.AreEqual(firstSequence, need.NeedSequence,
                    "day " + day + ": the same single need must persist, not a fresh duplicate");
                Assert.AreEqual(1, f.OpenNeedCount(HouseholdItemCatalog.BreadId),
                    "day " + day + ": exactly one open bread need may exist");
            }

            Assert.AreEqual(0, trips, "zero trips may execute over the three days");
            Assert.AreEqual(0, shoppingReservationsEver, "zero shopping reservations may ever be created");
            Assert.AreEqual(1, f.OpenNeedCount(HouseholdItemCatalog.BreadId), "exactly one open bread need after day 3");
        }

        [Test]
        public void AutonomousWorkGenerator_OpensUnservableNeed()
        {
            // Discriminator AWG. GenerateForHousehold with preserved_meat at
            // 1 day of supply (threshold 7) and a bread control, WITH the
            // Slice 1 recognition predicate supplied (bread recognized, meat
            // not — the method takes the optional predicate since Slice 1).
            // Desired: no preserved_meat need is created; the bread need IS
            // created, exactly once, and a second call creates no duplicate.
            // At HEAD (no predicate) the meat need was created; Slice 1
            // supplies the predicate and this test passes.
            var needs = new HouseholdNeedRegistry();
            var generator = new AutonomousWorkGenerator();
            var diag = new List<string>();
            var inputs = new AutonomousWorkGenerator.HouseholdGenerationInputs
            {
                HouseholdId = 7,
                HouseholdOwner = EntityId.For(EntityKind.Household, 7),
                Needs = needs,
                DayIndex = 100,
                LowDaysThreshold = 7f,
                IsRecognizedByKnownSeller = item => item == HouseholdItemCatalog.BreadId,
                DaysOfSupplyByItem = new Dictionary<string, float>
                {
                    { HouseholdItemCatalog.PreservedMeatId, 1f },
                    { HouseholdItemCatalog.BreadId, 1f },
                },
            };

            AutonomousWorkGenerator.GenerationReport report = generator.GenerateForHousehold(inputs, diag);

            Assert.IsTrue(needs.GetOpenNeed(7, HouseholdItemCatalog.PreservedMeatId) == null,
                "the generator opened an unservable preserved_meat need — no seller " +
                "recognizes it, so it can never be shopped. Created keys: " +
                string.Join(",", report.CreatedNeedKeys));
            HouseholdPurchasingNeed bread = needs.GetOpenNeed(7, HouseholdItemCatalog.BreadId);
            Assert.IsNotNull(bread, "the bread need must be created");
            Assert.AreEqual(1, report.CreatedNeedKeys.Count, "only the bread need may be created");
            AutonomousWorkGenerator.GenerationReport second = generator.GenerateForHousehold(inputs, diag);
            Assert.AreEqual(0, second.CreatedNeedKeys.Count, "a second call must create no duplicate need");
            Assert.IsNotNull(needs.GetOpenNeed(7, HouseholdItemCatalog.BreadId), "the bread need must persist");
        }

        [Test]
        public void AutonomousWorkGenerator_BreadOnly_CreatesOnce()
        {
            // Control for AWG: the LivingWorldSim shape (bread only) creates
            // the bread need once and never duplicates it on repeat calls.
            // Expected PASS at HEAD.
            var needs = new HouseholdNeedRegistry();
            var generator = new AutonomousWorkGenerator();
            var diag = new List<string>();
            var inputs = new AutonomousWorkGenerator.HouseholdGenerationInputs
            {
                HouseholdId = 7,
                HouseholdOwner = EntityId.For(EntityKind.Household, 7),
                Needs = needs,
                DayIndex = 100,
                LowDaysThreshold = 7f,
                DaysOfSupplyByItem = new Dictionary<string, float>
                {
                    { HouseholdItemCatalog.BreadId, 1f },
                },
            };

            AutonomousWorkGenerator.GenerationReport first = generator.GenerateForHousehold(inputs, diag);
            Assert.AreEqual(1, first.CreatedNeedKeys.Count);
            Assert.AreEqual("7:bread", first.CreatedNeedKeys[0]);
            Assert.IsNotNull(needs.GetOpenNeed(7, HouseholdItemCatalog.BreadId));
            AutonomousWorkGenerator.GenerationReport second = generator.GenerateForHousehold(inputs, diag);
            Assert.AreEqual(0, second.CreatedNeedKeys.Count, "a second call must create no duplicate need");
        }

        [Test]
        public void OpeningLot_IsSellable_AndFallbackPortSellsFromCategoryStock()
        {
            // Port pins (verify-don't-assume on half b). (a) Real adapter +
            // test port over a real BusinessRuntimeState: one identified bread
            // lot of 18u added via TryAddRetailItemLot (the production seam)
            // -> ItemStockUnits("bread") == 18; buy 6 -> stock 12, buyer
            // outflow 30c == store cash in, no receivable. (b) Fallback-port
            // shape: a test port with UsesItemLevelInventory == false over
            // pooled category stock of 18u -> the adapter sells 6u from
            // category stock with the same conservation. Both predicted PASS;
            // if (b) fails, Slice 2's repair scope expands to the fallback
            // port and the prediction is corrected in the slice report, not
            // silently re-worded. The seed->lot->port chain itself is NOT
            // asserted in the harness (those files are not compiled); it is
            // source-review + Unity-owed.
            var state = new BusinessRuntimeState();
            state.BindBusinessIdentity("general_store_060");
            state.EnsureCategoryStock("staple_food", 0, 100);
            var livePort = new LiveStatePort(state) { CashBalance = 50000 };
            var liveAdapter = new GeneralStoreSupplierAdapter(livePort);

            Assert.AreEqual(0, liveAdapter.ItemStockUnits("bread"), "no sellable bread before the lot lands");
            Assert.IsNull(livePort.ReceiveRetailItemLot(
                "lot:test:bread:18", "bread", "staple_food", "loaf", 18,
                "wholesale-receipt:test", 0));
            Assert.AreEqual(18, liveAdapter.ItemStockUnits("bread"), "the 18-loaf lot must read as sellable stock");
            Assert.IsTrue(liveAdapter.OffersItem("bread"), "stocked bread at 5c must be a viable offer");

            var diag = new List<string>();
            int sold = liveAdapter.SellItem("bread", 6, 0, null, diag);
            Assert.AreEqual(6, sold, "the adapter must sell 6 loaves: " + string.Join(" | ", diag));
            Assert.AreEqual(12, liveAdapter.ItemStockUnits("bread"), "stock must fall by exactly the units sold");
            Assert.AreEqual(50030, livePort.CashBalance, "store cash must gain exactly 6 x 5c");
            Assert.IsTrue(!string.IsNullOrWhiteSpace(liveAdapter.LastReceiptId), "a receipt id must be recorded");
            Assert.AreEqual(0, livePort.CreditSettlements, "a cash sale must record no receivable");

            // Buyer side of the same 30c: the household ledger pays exactly
            // what the store received.
            var ledgers = new HouseholdLedgerRegistry();
            HouseholdLedger ledger = ledgers.GetOrCreate(7);
            Assert.IsNull(ledger.RecordInflow(0, 10000, HouseholdIncomeSource.OwnerContribution,
                "test-capital", "test funding", "test"));
            Assert.IsNull(ledger.RecordOutflow(0, 30, "bread purchase", "general_store_060"));
            Assert.AreEqual(10000 - 30, ledger.GetBalanceCents(), "buyer outflow must equal 6 x 5c");
            Assert.AreEqual(30, livePort.CashBalance - 50000, "buyer outflow must equal store cash in");

            // (b) Fallback port: UsesItemLevelInventory == false, pooled
            // category stock of 18u at 5c.
            var fallback = new FakeStorePort();
            fallback.OfferList.Add(new GeneralStoreCategoryOffer
            {
                CategoryId = "staple_food",
                OfferedItemIds = new List<string> { HouseholdItemCatalog.BreadId },
                StoreUnitsPerItemUnit = 1,
                UsesItemLevelInventory = false,
            });
            fallback.Stock["staple_food"] = 18;
            fallback.Prices["staple_food"] = 5;
            var fallbackAdapter = new GeneralStoreSupplierAdapter(fallback);

            Assert.AreEqual(18, fallbackAdapter.ItemStockUnits("bread"),
                "the fallback port must read pooled category stock as sellable bread");
            Assert.IsTrue(fallbackAdapter.OffersItem("bread"), "pooled bread at 5c must be a viable offer");
            int fallbackSold = fallbackAdapter.SellItem("bread", 6, 0, null, diag);
            Assert.AreEqual(6, fallbackSold, "the fallback port must sell 6u from category stock: " + string.Join(" | ", diag));
            Assert.AreEqual(12, fallbackAdapter.ItemStockUnits("bread"), "category stock must fall by exactly the units sold");
            Assert.AreEqual(50030, fallback.CashBalance, "store cash must gain exactly 6 x 5c on the category path");
            Assert.IsTrue(!string.IsNullOrWhiteSpace(fallbackAdapter.LastReceiptId), "a receipt id must be recorded");
            Assert.AreEqual(0, fallback.CreditSettlements, "a cash sale must record no receivable");
        }

        [Test]
        public void WorkReservationPresent_WindowSearchStepsAroundIt()
        {
            // Control 11. Single employed adult with a Work reservation
            // placed BEFORE shopping (simulating the world Slice 4 creates),
            // shift 540-1020, trip estimate ~55 min (1.0-mi route: 20 min
            // each way + 15 in store). Desired: the trip departs in a legal
            // window (after 1020 with arrival inside store hours) or fails
            // honestly if none fits; the Work reservation is intact. Expected
            // PASS at HEAD — it proves the search itself is sound and the
            // defect is purely the missing reservation at shopping time. If
            // this test fails, Slice 4's premise is wrong: stop and re-file.
            Fixture f = Fixture.CreateSingle(female: false, routeMiles: 1.0);
            f.Fund(10000);
            HouseholdPurchasingNeed need = f.RaiseBreadNeedViaMonitor();
            Assert.IsNotNull(need, "the monitor must open the bread need");

            Assert.IsNull(f.Tracker.TryReserve(1, PersonActivityKind.Work, f.Day, 540, 480, "test shift 540-1020"),
                "the pre-shopping Work reservation must be placed");
            f.PlanDay();

            HouseholdShoppingLoop.BatchShoppingResult batch = f.Loop.ExecuteBatch(
                new List<HouseholdPurchasingNeed> { need }, f.Day, f.Options(), "work-reserved-first");
            Assert.IsNotNull(batch);
            Assert.IsTrue(f.HasReservation(1, PersonActivityKind.Work, 540, 1020, released: false),
                "the Work reservation must be intact after shopping");
            if (batch.TripExecuted)
            {
                Assert.AreEqual(1, batch.ShopperPersonId, "the lone adult shops");
                Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status);
                PersonActivityReservation shopping = f.FindReservation(1, PersonActivityKind.Shopping);
                PersonActivityReservation workRes = f.FindReservation(1, PersonActivityKind.Work);
                Assert.IsNotNull(shopping, "the trip must have held a shopping reservation");
                Assert.IsNotNull(workRes, "the Work reservation must exist");
                Assert.IsTrue(!Overlaps(shopping, workRes), "the trip window must not overlap the Work reservation");
                // Legal window per the plan: before 540 with return before
                // the shift, or after 1020 with arrival inside store hours.
                // (The 1.0-mi trip is 55 min, so departure 480 — window
                // [480,535], return before the 540 shift start — is the
                // legal pre-shift window; asserting only "departs at 1020"
                // would be wrong. First version of this test made exactly
                // that mistake and failed on a legal 480 departure.)
                Assert.IsTrue(shopping.EndMinuteOfDay <= 540 || shopping.StartMinuteOfDay >= 1020,
                    "the trip must depart in a legal window (before the 540 shift start or after the 1020 " +
                    "shift end); window was [" + shopping.StartMinuteOfDay + "-" + shopping.EndMinuteOfDay + "].");
            }
            else
            {
                Assert.IsTrue(batch.FailureReason.Contains("No shopper in any searched window"),
                    "an honest failure must name the searched window, got: " + batch.FailureReason);
                Assert.AreEqual(PurchasingNeedStatus.Open, need.Status, "the need must stay open on honest failure");
            }
        }

        [Test]
        public void WorkFirst_ClaimsBudgetBeforeShopping()
        {
            // Slice 3 question (a') pin, FLIPPED by Slice 4. Drives the NEW
            // production order phase by phase (planner -> work RESERVE +
            // ASSIGN, THEN monitor -> shopping, THEN work START — mirroring
            // SimulationDrivers.OnDayChanged after Slice 4:
            // DriveWorkAssignmentBeforeShopping ahead of DriveDailyNeeds,
            // start late in DriveAutonomousLabor) and observes the budget
            // BETWEEN the phases: the 540-min work task commits first, so
            // when shopping runs the shopper has only 60 of 600 minutes
            // left and the 95-min trip is refused honestly on budget. This
            // is the one same-day-shopping dependence of the assignment step
            // (the shared WorkTimeBudgetStore), now claimed in the right
            // order; open tasks, the employment registry and the owner list
            // do not depend on shopping. (Slice 3 version drove shopping
            // first and pinned the 505-min intermediate budget; Slice 4
            // reverses it. The start is late because StartTask refuses when
            // the person already holds an InProgress task and the trip legs
            // start as tasks for the shopper — see the split note on
            // ReserveAndAssignTasks.)
            Fixture f = Fixture.CreateSingle(female: false);
            f.Fund(10000);
            WorkTask work = f.CreateWorkTask(1, 540);

            f.PlanDay();
            var workDiag = new List<string>();
            int assigned = f.ReserveWork(
                new List<WorkTask> { work }, new List<int> { 1 }, new List<int> { 540 }, workDiag,
                out List<WorkTask> assignedTasks, out Dictionary<string, string> reservationIdByTaskId);
            Assert.AreEqual(1, assigned,
                "the 540-min work task must be reserved + assigned first (refusals: " + string.Join(" | ", workDiag) + ")");
            Assert.AreEqual(TaskStatus.Assigned, work.Status, "the work task must be assigned, not yet started");

            WorkTimeBudget budgetAfterWork = f.Budgets.GetOrCreate(EntityId.For(EntityKind.Person, 1));
            Assert.AreEqual(60, budgetAfterWork.MinutesRemaining,
                "the 540-min work task must have committed first (600 - 540 = 60) before any shopping runs");

            HouseholdPurchasingNeed need = f.RaiseBreadNeedViaMonitor();
            Assert.IsNotNull(need, "the monitor must open the bread need before shopping runs");
            List<HouseholdErrandPlan> plans = f.Coordinator.PlanDay(f.HouseholdId, f.Day, f.Context(), f.Diagnostics);
            Assert.AreEqual(1, plans.Count, "one plan batches the open bread need");
            HouseholdShoppingLoop.BatchShoppingResult batch =
                f.Coordinator.ExecutePlan(plans[0], f.Day, f.Context(), f.Loop, f.Options(), f.Diagnostics);
            Assert.IsNotNull(batch);
            Assert.IsTrue(!batch.TripExecuted,
                "no trip may execute once work has claimed the budget (failure reason: " + batch.FailureReason + ")");
            Assert.IsTrue(batch.FailureReason.Contains("No shopper in any searched window"),
                "the failure must name the searched window, got: " + batch.FailureReason);
            Assert.IsTrue(batch.FailureReason.Contains("work minutes left"),
                "the failure must name the budget shortfall, got: " + batch.FailureReason);
            Assert.IsNotNull(f.BreadNeed(), "the need must stay open");
            Assert.AreEqual(PurchasingNeedStatus.Open, f.BreadNeed().Status, "the need must stay open");

            int started = f.StartReservedWork(assignedTasks, reservationIdByTaskId, workDiag);
            Assert.AreEqual(1, started, "the late start must start the assigned work task");
            Assert.AreEqual(TaskStatus.InProgress, work.Status, "the work task must be started");
        }

        [Test]
        public void WorkStart_DoesNotRelocateWorker()
        {
            // Slice 3 question (d') pin. Starting work must NOT relocate
            // the worker or set scheduleState to AtWork: StartAssignedTasks
            // / StartTask write no schedule state (no `scheduleState =` in
            // Core/Tasks or the labor drive), and ResolvePersonJourneyLocation
            // (SimulationDrivers.cs:8044-8060) reads scheduleState. So after
            // a 240-min work task starts, the person is still AtHome — a
            // trip departing from a remote workplace after a shift cannot
            // be produced by reservation order alone (R-REMOTE, deferred
            // to priority 6). Passes at HEAD and post-fix.
            Fixture f = Fixture.CreateSingle(female: false);
            f.Fund(10000);
            WorkTask work = f.CreateWorkTask(1, 240);
            var workDiag = new List<string>();
            int started;
            HouseholdShoppingLoop.BatchShoppingResult batch = f.DriveProductionDay(
                new List<WorkTask> { work }, new List<int> { 1 }, new List<int> { 240 },
                out started, workDiag);

            Assert.IsNotNull(batch);
            Assert.AreEqual(1, started,
                "the work task must start (refusals: " + string.Join(" | ", workDiag) + ")");
            Assert.AreEqual(TaskStatus.InProgress, work.Status, "the work task must be started");
            PersonState person = f.Population.GetPerson(1);
            Assert.IsNotNull(person, "the person must exist");
            Assert.IsTrue(person.scheduleState != PopulationScheduleState.AtWork,
                "starting work must not set scheduleState to AtWork; it is " + person.scheduleState);
            Assert.AreEqual(PopulationScheduleState.AtHome, person.scheduleState,
                "the worker stays AtHome — nothing in the work path relocates them");
        }

        [Test]
        public void WorkStart_OverRestSprinkle()
        {
            // Slice 4 test 2 (rev1 section 2): after PlanDay, StartAssignedTasks
            // with a 480-start task succeeds, the [480,510] rest is released
            // and NAMED in diagnostics, the Work reservation is intact.
            // (Test 11 does NOT cover this — it places Work at 540, where no
            // rest exists.) FAILS at HEAD (strict TryReserve refuses the rest
            // overlap); passes once Slice 4 lands the rest rule.
            Fixture f = Fixture.CreateSingle(female: false);
            PersonActivityReservation restBefore = null;
            f.PlanDay();
            restBefore = f.FindReservationAt(1, PersonActivityKind.Resting, 480);
            Assert.IsNotNull(restBefore, "PlanDay must sprinkle a [480,510] rest before work starts");
            Assert.IsTrue(!restBefore.Released, "the rest must start unreleased");
            Assert.AreEqual(510, restBefore.EndMinuteOfDay, "the rest sprinkle is [480,510]");

            WorkTask work = f.CreateWorkTask(1, 240);
            var workDiag = new List<string>();
            int started = f.StartWork(
                new List<WorkTask> { work }, new List<int> { 1 }, new List<int> { 240 }, workDiag);
            Assert.AreEqual(1, started,
                "the work task must start over the rest sprinkle (refusals: " + string.Join(" | ", workDiag) + ")");
            Assert.AreEqual(TaskStatus.InProgress, work.Status, "the work task must be started");
            PersonActivityReservation restAfter = f.FindReservationAt(1, PersonActivityKind.Resting, 480);
            Assert.IsNotNull(restAfter, "the rest reservation record must still exist");
            Assert.IsTrue(restAfter.Released, "the [480,510] rest must be released by the Work reservation");
            Assert.IsTrue(f.HasReservation(1, PersonActivityKind.Work, 480, 720, released: false),
                "the Work reservation [480,720] must be intact");
            string allDiag = string.Join(" | ", workDiag);
            Assert.IsTrue(allDiag.Contains("released rest"),
                "the released rest must be NAMED in diagnostics, got: " + allDiag);
            Assert.IsTrue(allDiag.Contains("Resting [480-510]"),
                "the diagnostic must name the released rest window, got: " + allDiag);
        }

        [Test]
        public void PostShiftWindow_ShopsAfterWork()
        {
            // Slice 4 test 3: single employed adult, 240-min task (480-720,
            // the catalog maximum — rev1 section 3a). Post-fix the trip
            // departs AFTER the shift: departure 720, window [720,815],
            // arrival inside store hours, work intact, purchase settles.
            // This pins the departure minute, which tests 1/2 do not assert.
            Fixture f = Fixture.CreateSingle(female: false);
            f.Fund(10000);
            WorkTask work = f.CreateWorkTask(1, 240);
            var workDiag = new List<string>();
            int started;
            HouseholdShoppingLoop.BatchShoppingResult batch = f.DriveProductionDay(
                new List<WorkTask> { work }, new List<int> { 1 }, new List<int> { 240 },
                out started, workDiag);

            Assert.IsNotNull(batch, "a plan must be created for the open bread need");
            Assert.AreEqual(1, started,
                "the work task must start (refusals: " + string.Join(" | ", workDiag) + ")");
            Assert.AreEqual(TaskStatus.InProgress, work.Status, "the work task must be started");
            Assert.IsTrue(batch.TripExecuted, "the post-shift trip must execute: " + batch.FailureReason);
            PersonActivityReservation shopping = f.FindReservation(1, PersonActivityKind.Shopping);
            PersonActivityReservation workRes = f.FindReservation(1, PersonActivityKind.Work);
            Assert.IsNotNull(shopping, "the trip must have held a shopping reservation");
            Assert.IsNotNull(workRes, "the work task must hold a Work reservation");
            Assert.AreEqual(720, shopping.StartMinuteOfDay,
                "the trip must depart at 720, after the 480-720 shift; window was [" +
                shopping.StartMinuteOfDay + "-" + shopping.EndMinuteOfDay + "]");
            Assert.AreEqual(815, shopping.EndMinuteOfDay, "the 95-min trip from 720 ends at 815");
            Assert.IsTrue(!Overlaps(shopping, workRes), "the trip window must not overlap the Work reservation");
            Assert.AreEqual(0, f.OpenNeedCount(HouseholdItemCatalog.BreadId), "the need must be fulfilled by the trip");
            Assert.AreEqual(10000 - 70, f.Balance(), "buyer debit must equal 14 loaves x the 5c price");
        }

        [Test]
        public void SingleEmployedMan_Acceptance_PurchaseToMeal()
        {
            // Slice 6 S6-1 (M1): the single employed man (H-A) carried from
            // purchase through to consumption. Day 0 drives the Slice 4
            // production day (planner -> work reserve+assign -> monitor ->
            // coordinator PlanDay/ExecutePlan -> work start) and asserts
            // A-H points 1-7 and 10; day 1 drives the meal leg (points
            // 8-9). Setup is the authored fixture shape: Tomas, 34, alone,
            // 240-min shift 480-720, store stocked 16 loaves at 5c, funded
            // 10000c. Exact conservation: 14 loaves x 5c = 70c, balance
            // 9930, store stock 16 -> 2.
            SinglePersonPurchaseToMeal_Acceptance(female: false);
        }

        [Test]
        public void SingleEmployedWoman_Acceptance_PurchaseToMeal()
        {
            // Slice 6 S6-1 (M2): M1 assertion-for-assertion with a woman
            // living alone (Anna, 34). The outcome must not depend on sex
            // (shopper preference is irrelevant for a single-person
            // household).
            SinglePersonPurchaseToMeal_Acceptance(female: true);
        }

        private void SinglePersonPurchaseToMeal_Acceptance(bool female)
        {
            Fixture f = Fixture.CreateSingle(female);
            f.Fund(10000);

            HouseholdReserveState reserve = f.Reserve();
            Assert.IsNotNull(reserve, "the household must hold a staple_food reserve");
            int reserveTarget = reserve.targetUnits;
            Assert.Greater(reserveTarget, 0, "the staple_food target must be computed from the catalog, not assumed");
            int reserveBefore = reserve.currentUnits;

            int storeCashBefore = f.Port.CashBalance;
            int storeStockBefore = f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId);
            int totalCashBefore = storeCashBefore + f.Balance();
            int totalBreadBefore = storeStockBefore + f.BreadUnits();
            Assert.AreEqual(50000, storeCashBefore, "the fixture store opens with 50000c");
            Assert.AreEqual(16, storeStockBefore, "the fixture store opens with 16 loaves");
            Assert.AreEqual(0, f.BreadUnits(), "the household opens with no bread");

            // (1) A valid need with UnitsNeeded > 0 before the trip, raised
            // by the real monitor from zero stock (2 loaves/day expected
            // use, so 14u at urgency 1.0).
            HouseholdPurchasingNeed need = f.RaiseBreadNeedViaMonitor();
            Assert.IsNotNull(need, "the monitor must open the bread need before the trip");
            Assert.AreEqual(14, need.UnitsNeeded, "the monitor opens a 14u bread need for a single person at zero stock");
            Assert.AreEqual(1f, need.Urgency01, "bread at zero stock is maximally urgent");
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status, "the need must be open before the trip");

            WorkTask work = f.CreateWorkTask(1, 240);
            var workDiag = new List<string>();
            int started;
            HouseholdShoppingLoop.BatchShoppingResult batch = f.DriveProductionDay(
                new List<WorkTask> { work }, new List<int> { 1 }, new List<int> { 240 },
                out started, workDiag);

            Assert.IsNotNull(batch, "a plan must be created for the open bread need");
            Assert.IsTrue(batch.TripExecuted, "the post-shift trip must execute: " + batch.FailureReason);
            Assert.AreEqual(1, batch.ShopperPersonId, "the lone adult shops");

            // (2) The right person shops, in a window that does not
            // overlap any Work reservation: the trip departs 720, window
            // [720,815], after the Work [480,720].
            PersonActivityReservation shopping = f.FindReservation(1, PersonActivityKind.Shopping);
            PersonActivityReservation workRes = f.FindReservation(1, PersonActivityKind.Work);
            Assert.IsNotNull(shopping, "the trip must have held a shopping reservation");
            Assert.IsNotNull(workRes, "the work task must hold a Work reservation");
            Assert.AreEqual(720, shopping.StartMinuteOfDay, "the trip must depart at 720, after the 480-720 shift");
            Assert.AreEqual(815, shopping.EndMinuteOfDay, "the 95-min trip from 720 ends at 815");
            Assert.IsTrue(!Overlaps(shopping, workRes), "the trip window must not overlap the Work reservation");

            // (3) Travels from the real location: legs execute and the
            // shopper's location moves home -> store -> home — no teleport.
            // ResolvedLocations records PersonLocationId READS, which the
            // loop only performs at departure, so the store visit is proven
            // the way the EndToEnd fixture proves it: the SetPersonLocation
            // WRITES the legs perform (HouseholdShoppingLoop.cs:859,866).
            var hops = new List<string>();
            foreach (LocationHop hop in f.Transitions)
            {
                if (hop.PersonId == 1) hops.Add(hop.LocationId);
            }

            Assert.AreEqual(2, hops.Count, "the shopper must move exactly twice: to the store, then home");
            Assert.AreEqual("store", hops[0], "first move is home -> store");
            Assert.AreEqual("home", hops[1], "second move is store -> home");

            // (4) Paid work intact: the work task starts.
            Assert.AreEqual(1, started, "the work task must start (refusals: " + string.Join(" | ", workDiag) + ")");
            Assert.AreEqual(TaskStatus.InProgress, work.Status, "the work task must be started, not merely assigned");
            Assert.IsTrue(f.HasReservation(1, PersonActivityKind.Work, 480, 720, released: false),
                "the Work reservation [480,720] must still be held");

            ShoppingTripResult breadResult = FindNeedResult(batch, HouseholdItemCatalog.BreadId);
            Assert.IsNotNull(breadResult, "the batch must carry its own bread result");
            Assert.IsTrue(breadResult.Success, breadResult.FailureReason);
            Assert.AreEqual(14, breadResult.UnitsAcquired, "the trip must acquire the full 14-loaf need");

            // (5) Exact price: outflow = units sold x 5c, receipt recorded.
            Assert.AreEqual(70, breadResult.BuyerOutflowCents, "14 loaves x 5c");
            Assert.IsTrue(!string.IsNullOrEmpty(breadResult.ReceiptId), "the settlement must record a receipt id");
            Assert.IsTrue(string.IsNullOrEmpty(breadResult.PartialFillReason),
                "a fully fulfilled trip carries no partial-fill reason");

            // (6) Cash and inventory reconcile: store cash +outflow,
            // store stock -units, buyer outflow == store cash in, no
            // receivable.
            Assert.AreEqual(storeCashBefore + 70, f.Port.CashBalance, "seller cash must gain exactly 70c");
            Assert.AreEqual(storeStockBefore - 14, f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId),
                "seller stock must fall by exactly the 14 loaves sold");
            Assert.AreEqual(breadResult.BuyerOutflowCents, breadResult.StoreCashInCents,
                "buyer outflow must equal store cash in");
            Assert.AreEqual(0, breadResult.StoreReceivableCents, "cash settlement creates no receivable");
            Assert.AreEqual(10000 - 70, f.Balance(), "buyer debit must equal 14 loaves x the 5c price");

            // (7) Identifiable goods: a lot with purchase provenance +
            // receipt id, custody ReceivedAtHousehold.
            HouseholdItemLot lot = f.BreadLot();
            Assert.IsNotNull(lot, "the household must hold a bread lot after the purchase");
            Assert.AreEqual(14, lot.QuantityUnits, "UnitsAcquired must equal the settled units");
            Assert.IsTrue(lot.SourceLabel.Contains("purchased 14u bread from"),
                "the lot must carry purchase provenance, got: " + lot.SourceLabel);
            Assert.IsTrue(lot.SourceLabel.Contains(breadResult.ReceiptId),
                "the lot provenance must carry the receipt id, got: " + lot.SourceLabel);
            Assert.IsTrue(HasEvent(breadResult, ShoppingEventKind.Custody),
                "the custody transfer into the shopper's hands must be recorded");
            Assert.IsTrue(HasEvent(breadResult, ShoppingEventKind.Receipt),
                "the physical receipt at the household must be recorded");
            Assert.AreEqual(0, f.Loop.ActiveCustodyBatches.Count,
                "no custody batch may be left stranded after receipt");
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status, "the need must be fulfilled by the trip");
            Assert.AreEqual(0, need.UnitsNeeded, "the fulfilled need must have nothing left");

            // (9, receipt half) The reserve mirror credits the receipt
            // (clamped to target).
            int credited = Math.Min(reserveTarget, reserveBefore + 14);
            Assert.AreEqual(credited, reserve.currentUnits,
                "the receipt must credit the reserve by the units received (clamped to target)");

            // (10, pre-meals) Total cash and total bread conserved; the
            // fulfilled need is never re-bought.
            Assert.AreEqual(totalCashBefore, f.Port.CashBalance + f.Balance(),
                "cash must move, not be created or destroyed");
            Assert.AreEqual(totalBreadBefore, f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId) + f.BreadUnits(),
                "bread must move, not be created or destroyed");
            List<HouseholdErrandPlan> replans = f.Coordinator.PlanDay(f.HouseholdId, f.Day, f.Context(), f.Diagnostics);
            Assert.AreEqual(0, replans.Count, "no open need remains, so no second plan — and no second sale — may form");
            Assert.AreEqual(2, f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId),
                "the store stock must be untouched by the re-plan");

            // --- Day 1: the meal leg (points 8-9). ---
            int receivedLotId = lot.LotId;
            IReadOnlyList<HouseholdMealRecord> meals = f.DriveNextDayMeal(work);
            Assert.Greater(meals.Count, 0, "at least one meal must be served on day 1");

            // (8) At least one meal served from the purchased lot, with
            // the lot id in the meal log.
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

            Assert.IsTrue(lotReferenced,
                "at least one day-1 meal record must reference the purchased lot id " + receivedLotId);
            Assert.Greater(eaten, 0, "the day-1 meals must have eaten bread from the purchased lot");

            // (9, consumption half) The meal consumption mirror decrements
            // the reserve by the loaves eaten.
            Assert.AreEqual(Math.Max(0, credited - eaten), reserve.currentUnits,
                "meal consumption must decrement the reserve by the loaves eaten");
            Assert.AreEqual(14 - eaten, f.BreadUnits(),
                "the household must hold 14 loaves minus the loaves eaten");

            // (10, post-meals) Bread conservation with consumption
            // accounted; cash unchanged by meals; need stays fulfilled.
            Assert.AreEqual(totalBreadBefore,
                f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId) + f.BreadUnits() + eaten,
                "bread must be conserved across purchase and meals");
            Assert.AreEqual(totalCashBefore, f.Port.CashBalance + f.Balance(),
                "cash must stay conserved after meals");
            Assert.AreEqual(0, f.OpenNeedCount(HouseholdItemCatalog.BreadId),
                "the fulfilled need must never be re-bought");
        }

        [Test]
        public void TwoEmployedAdults_SameShift_Acceptance_PostShiftShopper()
        {
            // Slice 6 S6-2 (M3): two employed adults (H-C), Tomas + Anna,
            // both 240-min shifts 480-720. Both work tasks start; one
            // member shops post-shift — the shopper per the policy
            // preference order (adult woman first,
            // ShopperAssignmentPolicy.ChooseShopperWithWindowSearch,
            // PersonScheduleTracker.cs:671+; the existing staggered tests
            // already pin Anna/P2 as shopper), so Anna (P2) shops, trip
            // departs 720, window [720,815]; purchase settles 70c; no trip
            // overlaps either Work reservation; next-day meal from the
            // purchased lot; exact cash and inventory conservation; lot
            // with purchase provenance + receipt id; reserve credited then
            // decremented by the meal.
            TwoAdultPurchaseToMeal_Acceptance(
                tomasMinutes: 240, annaMinutes: 240, expectedShopper: 2,
                expectedDepart: 720, expectedEnd: 815, expectedStarted: 2);
        }

        [Test]
        public void TwoEmployedAdults_StaggeredShifts_Acceptance_FreeMemberShopsToMeal()
        {
            // Slice 6 S6-2 (M4): member available during store hours
            // (H-D). Tomas 540-min shift 480-1020, Anna free (no work
            // today). Anna shops — she is free at the earliest candidate
            // departure, so the trip departs 480, window [480,575];
            // Tomas's work starts; purchase settles 70c; no reservation
            // overlaps for either member; next-day meal from the purchased
            // lot; exact conservation; lot provenance + receipt id;
            // reserve credited then decremented. (Scheduling half already
            // proved by TwoEmployedAdults_StaggeredShifts_FreeMemberShops;
            // Slice 6 adds the meal/conservation legs.)
            TwoAdultPurchaseToMeal_Acceptance(
                tomasMinutes: 540, annaMinutes: 0, expectedShopper: 2,
                expectedDepart: 480, expectedEnd: 575, expectedStarted: 1);
        }

        private void TwoAdultPurchaseToMeal_Acceptance(int tomasMinutes, int annaMinutes,
            int expectedShopper, int expectedDepart, int expectedEnd, int expectedStarted)
        {
            Fixture f = Fixture.CreatePair();
            f.Fund(10000);

            HouseholdReserveState reserve = f.Reserve();
            Assert.IsNotNull(reserve, "the household must hold a staple_food reserve");
            int reserveTarget = reserve.targetUnits;
            Assert.Greater(reserveTarget, 0, "the staple_food target must be computed from the catalog, not assumed");
            int reserveBefore = reserve.currentUnits;

            int storeCashBefore = f.Port.CashBalance;
            int storeStockBefore = f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId);
            int totalCashBefore = storeCashBefore + f.Balance();
            int totalBreadBefore = storeStockBefore + f.BreadUnits();
            Assert.AreEqual(50000, storeCashBefore, "the fixture store opens with 50000c");
            Assert.AreEqual(16, storeStockBefore, "the fixture store opens with 16 loaves");
            Assert.AreEqual(0, f.BreadUnits(), "the household opens with no bread");

            // (1) A valid need with UnitsNeeded > 0 before the trip, raised
            // by the real monitor from zero stock (2 loaves/day expected
            // use, so 14u at urgency 1.0).
            HouseholdPurchasingNeed need = f.RaiseBreadNeedViaMonitor();
            Assert.IsNotNull(need, "the monitor must open the bread need before the trip");
            Assert.AreEqual(14, need.UnitsNeeded, "the monitor opens a 14u bread need for a two-adult household at zero stock");
            Assert.AreEqual(1f, need.Urgency01, "bread at zero stock is maximally urgent");
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status, "the need must be open before the trip");

            WorkTask work1 = tomasMinutes > 0 ? f.CreateWorkTask(1, tomasMinutes) : null;
            WorkTask work2 = annaMinutes > 0 ? f.CreateWorkTask(2, annaMinutes) : null;
            var workTasks = new List<WorkTask>();
            var personIds = new List<int>();
            var minutesList = new List<int>();
            if (work1 != null)
            {
                workTasks.Add(work1);
                personIds.Add(1);
                minutesList.Add(tomasMinutes);
            }

            if (work2 != null)
            {
                workTasks.Add(work2);
                personIds.Add(2);
                minutesList.Add(annaMinutes);
            }

            var workDiag = new List<string>();
            int started;
            HouseholdShoppingLoop.BatchShoppingResult batch = f.DriveProductionDay(
                workTasks, personIds, minutesList, out started, workDiag);

            Assert.IsNotNull(batch, "a plan must be created for the open bread need");
            Assert.IsTrue(batch.TripExecuted, "the trip must execute: " + batch.FailureReason);
            Assert.AreEqual(expectedShopper, batch.ShopperPersonId,
                "the shopper must be the policy-preferred member (adult woman first)");

            // (2) The right person shops, in a window that does not
            // overlap the shopper's own Work reservation (per-member
            // person-time; the other member's reservation is checked
            // intact below, not for clock overlap — see the note there).
            PersonActivityReservation shopping = f.FindReservation(expectedShopper, PersonActivityKind.Shopping);
            Assert.IsNotNull(shopping, "the trip must have held a shopping reservation");
            Assert.AreEqual(expectedDepart, shopping.StartMinuteOfDay,
                "the trip must depart at " + expectedDepart + "; window was [" +
                shopping.StartMinuteOfDay + "-" + shopping.EndMinuteOfDay + "]");
            Assert.AreEqual(expectedEnd, shopping.EndMinuteOfDay,
                "the trip window must end at " + expectedEnd);
            PersonActivityReservation workRes1 = f.FindReservation(1, PersonActivityKind.Work);
            PersonActivityReservation workRes2 = f.FindReservation(2, PersonActivityKind.Work);
            if (work1 != null)
            {
                Assert.IsNotNull(workRes1, "Tomas must hold a Work reservation");
                Assert.IsTrue(f.HasReservation(1, PersonActivityKind.Work, 480, 480 + tomasMinutes, released: false),
                    "Tomas's Work reservation [480-" + (480 + tomasMinutes) + "] must still be held");
            }
            else
            {
                Assert.IsNull(workRes1, "Tomas holds no work today, so he must hold no Work reservation");
            }

            if (work2 != null)
            {
                Assert.IsNotNull(workRes2, "Anna must hold a Work reservation");
                Assert.IsTrue(f.HasReservation(2, PersonActivityKind.Work, 480, 480 + annaMinutes, released: false),
                    "Anna's Work reservation [480-" + (480 + annaMinutes) + "] must still be held");
            }
            else
            {
                Assert.IsNull(workRes2, "Anna holds no work today, so she must hold no Work reservation");
            }

            // The trip window must not overlap the SHOPPER's own Work
            // reservation (same person, same person-time). It is NOT
            // compared against the other member's Work reservation: two
            // different people may hold reservations over the same clock
            // minutes — M4's [480,575] trip overlaps Tomas's [480,1020]
            // shift in clock time and that is legitimate, because Anna,
            // not Tomas, is shopping. Per-member overlap is pinned by
            // ActiveReservationsOverlap below.
            PersonActivityReservation shopperWorkRes =
                expectedShopper == 1 ? workRes1 : workRes2;
            Assert.IsTrue(!Overlaps(shopping, shopperWorkRes),
                "the trip window must not overlap the shopper's own Work reservation");

            Assert.IsTrue(!f.ActiveReservationsOverlap(1), "Tomas holds no overlapping reservations");
            Assert.IsTrue(!f.ActiveReservationsOverlap(2), "Anna holds no overlapping reservations");

            // (3) Travels from the real location: legs execute and the
            // shopper's location moves home -> store -> home — no teleport.
            // Proven from the SetPersonLocation WRITES the legs perform
            // (HouseholdShoppingLoop.cs:859,866), as in S6-1: the loop only
            // reads the location at departure. The non-shopper must not
            // move at all.
            int nonShopper = expectedShopper == 1 ? 2 : 1;
            var hops = new List<string>();
            var nonShopperHops = new List<string>();
            foreach (LocationHop hop in f.Transitions)
            {
                if (hop.PersonId == expectedShopper) hops.Add(hop.LocationId);
                if (hop.PersonId == nonShopper) nonShopperHops.Add(hop.LocationId);
            }

            Assert.AreEqual(2, hops.Count, "the shopper must move exactly twice: to the store, then home");
            Assert.AreEqual("store", hops[0], "first move is home -> store");
            Assert.AreEqual("home", hops[1], "second move is store -> home");
            Assert.AreEqual(0, nonShopperHops.Count, "the non-shopper must not travel");

            // (4) Paid work intact: every assigned work task starts.
            Assert.AreEqual(expectedStarted, started,
                "every assigned work task must start (refusals: " + string.Join(" | ", workDiag) + ")");
            if (work1 != null)
            {
                Assert.AreEqual(TaskStatus.InProgress, work1.Status, "Tomas's work task must be started, not merely assigned");
            }

            if (work2 != null)
            {
                Assert.AreEqual(TaskStatus.InProgress, work2.Status, "Anna's work task must be started, not merely assigned");
            }

            ShoppingTripResult breadResult = FindNeedResult(batch, HouseholdItemCatalog.BreadId);
            Assert.IsNotNull(breadResult, "the batch must carry its own bread result");
            Assert.IsTrue(breadResult.Success, breadResult.FailureReason);
            Assert.AreEqual(14, breadResult.UnitsAcquired, "the trip must acquire the full 14-loaf need");

            // (5) Exact price: outflow = units sold x 5c, receipt recorded.
            Assert.AreEqual(70, breadResult.BuyerOutflowCents, "14 loaves x 5c");
            Assert.IsTrue(!string.IsNullOrEmpty(breadResult.ReceiptId), "the settlement must record a receipt id");
            Assert.IsTrue(string.IsNullOrEmpty(breadResult.PartialFillReason),
                "a fully fulfilled trip carries no partial-fill reason");

            // (6) Cash and inventory reconcile: store cash +outflow,
            // store stock -units, buyer outflow == store cash in, no
            // receivable.
            Assert.AreEqual(storeCashBefore + 70, f.Port.CashBalance, "seller cash must gain exactly 70c");
            Assert.AreEqual(storeStockBefore - 14, f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId),
                "seller stock must fall by exactly the 14 loaves sold");
            Assert.AreEqual(breadResult.BuyerOutflowCents, breadResult.StoreCashInCents,
                "buyer outflow must equal store cash in");
            Assert.AreEqual(0, breadResult.StoreReceivableCents, "cash settlement creates no receivable");
            Assert.AreEqual(10000 - 70, f.Balance(), "buyer debit must equal 14 loaves x the 5c price");

            // (7) Identifiable goods: a lot with purchase provenance +
            // receipt id, custody ReceivedAtHousehold.
            HouseholdItemLot lot = f.BreadLot();
            Assert.IsNotNull(lot, "the household must hold a bread lot after the purchase");
            Assert.AreEqual(14, lot.QuantityUnits, "UnitsAcquired must equal the settled units");
            Assert.IsTrue(lot.SourceLabel.Contains("purchased 14u bread from"),
                "the lot must carry purchase provenance, got: " + lot.SourceLabel);
            Assert.IsTrue(lot.SourceLabel.Contains(breadResult.ReceiptId),
                "the lot provenance must carry the receipt id, got: " + lot.SourceLabel);
            Assert.IsTrue(HasEvent(breadResult, ShoppingEventKind.Custody),
                "the custody transfer into the shopper's hands must be recorded");
            Assert.IsTrue(HasEvent(breadResult, ShoppingEventKind.Receipt),
                "the physical receipt at the household must be recorded");
            Assert.AreEqual(0, f.Loop.ActiveCustodyBatches.Count,
                "no custody batch may be left stranded after receipt");
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status, "the need must be fulfilled by the trip");
            Assert.AreEqual(0, need.UnitsNeeded, "the fulfilled need must have nothing left");

            // (9, receipt half) The reserve mirror credits the receipt
            // (clamped to target).
            int credited = Math.Min(reserveTarget, reserveBefore + 14);
            Assert.AreEqual(credited, reserve.currentUnits,
                "the receipt must credit the reserve by the units received (clamped to target)");

            // (10, pre-meals) Total cash and total bread conserved; the
            // fulfilled need is never re-bought.
            Assert.AreEqual(totalCashBefore, f.Port.CashBalance + f.Balance(),
                "cash must move, not be created or destroyed");
            Assert.AreEqual(totalBreadBefore, f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId) + f.BreadUnits(),
                "bread must move, not be created or destroyed");
            List<HouseholdErrandPlan> replans = f.Coordinator.PlanDay(f.HouseholdId, f.Day, f.Context(), f.Diagnostics);
            Assert.AreEqual(0, replans.Count, "no open need remains, so no second plan — and no second sale — may form");
            Assert.AreEqual(2, f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId),
                "the store stock must be untouched by the re-plan");

            // --- Day 1: the meal leg (points 8-9), reusing the S6-1
            // helper. DriveNextDayMeal closes one prior-day work task;
            // the other adult's work (when both worked) is closed first
            // so nobody is left holding an InProgress task into day 1.
            WorkTask closeWork = work2 ?? work1;
            WorkTask otherWork = work2 != null ? work1 : null;
            if (otherWork != null)
            {
                Assert.IsTrue(f.Tasks.CompleteTask(otherWork.TaskId, TaskOutcome.Success, 0, f.Budgets),
                    "the other adult's day-0 work must complete at day end");
            }

            int receivedLotId = lot.LotId;
            IReadOnlyList<HouseholdMealRecord> meals = f.DriveNextDayMeal(closeWork);
            Assert.Greater(meals.Count, 0, "at least one meal must be served on day 1");

            // (8) At least one meal served from the purchased lot, with
            // the lot id in the meal log.
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

            Assert.IsTrue(lotReferenced,
                "at least one day-1 meal record must reference the purchased lot id " + receivedLotId);
            Assert.Greater(eaten, 0, "the day-1 meals must have eaten bread from the purchased lot");

            // (9, consumption half) The meal consumption mirror decrements
            // the reserve by the loaves eaten.
            Assert.AreEqual(Math.Max(0, credited - eaten), reserve.currentUnits,
                "meal consumption must decrement the reserve by the loaves eaten");
            Assert.AreEqual(14 - eaten, f.BreadUnits(),
                "the household must hold 14 loaves minus the loaves eaten");

            // (10, post-meals) Bread conservation with consumption
            // accounted; cash unchanged by meals; need stays fulfilled.
            Assert.AreEqual(totalBreadBefore,
                f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId) + f.BreadUnits() + eaten,
                "bread must be conserved across purchase and meals");
            Assert.AreEqual(totalCashBefore, f.Port.CashBalance + f.Balance(),
                "cash must stay conserved after meals");
            Assert.AreEqual(0, f.OpenNeedCount(HouseholdItemCatalog.BreadId),
                "the fulfilled need must never be re-bought");
        }

        [Test]
        public void ShiftOverlappingStoreHours_PreShiftWindow_Acceptance()
        {
            // Slice 6 S6-3 (M5): shift overlapping store hours (H-E window
            // case). Single man; Work reservation pre-placed 540-1020;
            // 1.0-mi route, 55-min trip. The legal pre-shift window is
            // [480,535] (return before the 540 shift start); Slice 4 test
            // 11 proved the window search steps around the reservation,
            // Slice 6 adds the purchase-to-meal legs: the purchase settles
            // (14 loaves, 70c) and the next-day meal is served from the
            // lot. No shopping over Work. Shape follows
            // WorkReservationPresent_WindowSearchStepsAroundIt: the
            // reservation is pre-placed (no work task is created — the
            // reservation IS the shift), so the meal leg closes no prior
            // work task (DriveNextDayMeal(null)).
            Fixture f = Fixture.CreateSingle(female: false, routeMiles: 1.0);
            f.Fund(10000);

            HouseholdReserveState reserve = f.Reserve();
            Assert.IsNotNull(reserve, "the household must hold a staple_food reserve");
            int reserveTarget = reserve.targetUnits;
            Assert.Greater(reserveTarget, 0, "the staple_food target must be computed from the catalog, not assumed");
            int reserveBefore = reserve.currentUnits;

            int storeCashBefore = f.Port.CashBalance;
            int storeStockBefore = f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId);
            int totalCashBefore = storeCashBefore + f.Balance();
            int totalBreadBefore = storeStockBefore + f.BreadUnits();
            Assert.AreEqual(50000, storeCashBefore, "the fixture store opens with 50000c");
            Assert.AreEqual(16, storeStockBefore, "the fixture store opens with 16 loaves");
            Assert.AreEqual(0, f.BreadUnits(), "the household opens with no bread");

            // (1) A valid need with UnitsNeeded > 0 before the trip, raised
            // by the real monitor from zero stock (2 loaves/day expected
            // use, so 14u at urgency 1.0).
            HouseholdPurchasingNeed need = f.RaiseBreadNeedViaMonitor();
            Assert.IsNotNull(need, "the monitor must open the bread need before the trip");
            Assert.AreEqual(14, need.UnitsNeeded, "the monitor opens a 14u bread need for a single person at zero stock");
            Assert.AreEqual(1f, need.Urgency01, "bread at zero stock is maximally urgent");
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status, "the need must be open before the trip");

            Assert.IsNull(f.Tracker.TryReserve(1, PersonActivityKind.Work, f.Day, 540, 480, "test shift 540-1020"),
                "the pre-shopping Work reservation must be placed");
            f.PlanDay();

            HouseholdShoppingLoop.BatchShoppingResult batch = f.Loop.ExecuteBatch(
                new List<HouseholdPurchasingNeed> { need }, f.Day, f.Options(), "work-reserved-first");
            Assert.IsNotNull(batch);
            Assert.IsTrue(f.HasReservation(1, PersonActivityKind.Work, 540, 1020, released: false),
                "the Work reservation must be intact after shopping");
            if (!batch.TripExecuted)
            {
                // Honest-failure branch (plan-allowed): the failure must
                // name the searched window and the need stays open with
                // nothing moved.
                Assert.IsTrue(batch.FailureReason.Contains("No shopper in any searched window"),
                    "an honest failure must name the searched window, got: " + batch.FailureReason);
                Assert.AreEqual(PurchasingNeedStatus.Open, need.Status, "the need must stay open on honest failure");
                Assert.AreEqual(10000, f.Balance(), "no money may move without a trip");
                Assert.AreEqual(16, f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId), "no stock may move without a trip");
                return;
            }

            Assert.AreEqual(1, batch.ShopperPersonId, "the lone adult shops");

            // (2) The right person shops, in the legal pre-shift window
            // [480,535], which does not overlap the Work [540,1020].
            PersonActivityReservation shopping = f.FindReservation(1, PersonActivityKind.Shopping);
            PersonActivityReservation workRes = f.FindReservation(1, PersonActivityKind.Work);
            Assert.IsNotNull(shopping, "the trip must have held a shopping reservation");
            Assert.IsNotNull(workRes, "the Work reservation must exist");
            Assert.AreEqual(480, shopping.StartMinuteOfDay,
                "the trip must depart at 480, in the pre-shift window; window was [" +
                shopping.StartMinuteOfDay + "-" + shopping.EndMinuteOfDay + "]");
            Assert.AreEqual(535, shopping.EndMinuteOfDay, "the 55-min trip from 480 ends at 535, before the 540 shift start");
            Assert.IsTrue(!Overlaps(shopping, workRes), "the trip window must not overlap the Work reservation");

            // (3) Travels from the real location: legs execute and the
            // shopper's location moves home -> store -> home — no teleport.
            // Proven from the SetPersonLocation WRITES the legs perform
            // (HouseholdShoppingLoop.cs:859,866), as in S6-1.
            var hops = new List<string>();
            foreach (LocationHop hop in f.Transitions)
            {
                if (hop.PersonId == 1) hops.Add(hop.LocationId);
            }

            Assert.AreEqual(2, hops.Count, "the shopper must move exactly twice: to the store, then home");
            Assert.AreEqual("store", hops[0], "first move is home -> store");
            Assert.AreEqual("home", hops[1], "second move is store -> home");

            ShoppingTripResult breadResult = FindNeedResult(batch, HouseholdItemCatalog.BreadId);
            Assert.IsNotNull(breadResult, "the batch must carry its own bread result");
            Assert.IsTrue(breadResult.Success, breadResult.FailureReason);
            Assert.AreEqual(14, breadResult.UnitsAcquired, "the trip must acquire the full 14-loaf need");

            // (5) Exact price: outflow = units sold x 5c, receipt recorded.
            Assert.AreEqual(70, breadResult.BuyerOutflowCents, "14 loaves x 5c");
            Assert.IsTrue(!string.IsNullOrEmpty(breadResult.ReceiptId), "the settlement must record a receipt id");
            Assert.IsTrue(string.IsNullOrEmpty(breadResult.PartialFillReason),
                "a fully fulfilled trip carries no partial-fill reason");

            // (6) Cash and inventory reconcile: store cash +outflow,
            // store stock -units, buyer outflow == store cash in, no
            // receivable.
            Assert.AreEqual(storeCashBefore + 70, f.Port.CashBalance, "seller cash must gain exactly 70c");
            Assert.AreEqual(storeStockBefore - 14, f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId),
                "seller stock must fall by exactly the 14 loaves sold");
            Assert.AreEqual(breadResult.BuyerOutflowCents, breadResult.StoreCashInCents,
                "buyer outflow must equal store cash in");
            Assert.AreEqual(0, breadResult.StoreReceivableCents, "cash settlement creates no receivable");
            Assert.AreEqual(10000 - 70, f.Balance(), "buyer debit must equal 14 loaves x the 5c price");

            // (7) Identifiable goods: a lot with purchase provenance +
            // receipt id, custody ReceivedAtHousehold.
            HouseholdItemLot lot = f.BreadLot();
            Assert.IsNotNull(lot, "the household must hold a bread lot after the purchase");
            Assert.AreEqual(14, lot.QuantityUnits, "UnitsAcquired must equal the settled units");
            Assert.IsTrue(lot.SourceLabel.Contains("purchased 14u bread from"),
                "the lot must carry purchase provenance, got: " + lot.SourceLabel);
            Assert.IsTrue(lot.SourceLabel.Contains(breadResult.ReceiptId),
                "the lot provenance must carry the receipt id, got: " + lot.SourceLabel);
            Assert.IsTrue(HasEvent(breadResult, ShoppingEventKind.Custody),
                "the custody transfer into the shopper's hands must be recorded");
            Assert.IsTrue(HasEvent(breadResult, ShoppingEventKind.Receipt),
                "the physical receipt at the household must be recorded");
            Assert.AreEqual(0, f.Loop.ActiveCustodyBatches.Count,
                "no custody batch may be left stranded after receipt");
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status, "the need must be fulfilled by the trip");
            Assert.AreEqual(0, need.UnitsNeeded, "the fulfilled need must have nothing left");

            // (9, receipt half) The reserve mirror credits the receipt
            // (clamped to target).
            int credited = Math.Min(reserveTarget, reserveBefore + 14);
            Assert.AreEqual(credited, reserve.currentUnits,
                "the receipt must credit the reserve by the units received (clamped to target)");

            // (10, pre-meals) Total cash and total bread conserved; the
            // fulfilled need is never re-bought.
            Assert.AreEqual(totalCashBefore, f.Port.CashBalance + f.Balance(),
                "cash must move, not be created or destroyed");
            Assert.AreEqual(totalBreadBefore, f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId) + f.BreadUnits(),
                "bread must move, not be created or destroyed");
            List<HouseholdErrandPlan> replans = f.Coordinator.PlanDay(f.HouseholdId, f.Day, f.Context(), f.Diagnostics);
            Assert.AreEqual(0, replans.Count, "no open need remains, so no second plan — and no second sale — may form");
            Assert.AreEqual(2, f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId),
                "the store stock must be untouched by the re-plan");

            // --- Day 1: the meal leg (points 8-9), reusing the S6-1
            // helper. No work task was created in this test (the shift is
            // a bare reservation), so no prior-day work is closed.
            int receivedLotId = lot.LotId;
            IReadOnlyList<HouseholdMealRecord> meals = f.DriveNextDayMeal(null);
            Assert.Greater(meals.Count, 0, "at least one meal must be served on day 1");

            // (8) At least one meal served from the purchased lot, with
            // the lot id in the meal log.
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

            Assert.IsTrue(lotReferenced,
                "at least one day-1 meal record must reference the purchased lot id " + receivedLotId);
            Assert.Greater(eaten, 0, "the day-1 meals must have eaten bread from the purchased lot");

            // (9, consumption half) The meal consumption mirror decrements
            // the reserve by the loaves eaten.
            Assert.AreEqual(Math.Max(0, credited - eaten), reserve.currentUnits,
                "meal consumption must decrement the reserve by the loaves eaten");
            Assert.AreEqual(14 - eaten, f.BreadUnits(),
                "the household must hold 14 loaves minus the loaves eaten");

            // (10, post-meals) Bread conservation with consumption
            // accounted; cash unchanged by meals; need stays fulfilled.
            Assert.AreEqual(totalBreadBefore,
                f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId) + f.BreadUnits() + eaten,
                "bread must be conserved across purchase and meals");
            Assert.AreEqual(totalCashBefore, f.Port.CashBalance + f.Balance(),
                "cash must stay conserved after meals");
            Assert.AreEqual(0, f.OpenNeedCount(HouseholdItemCatalog.BreadId),
                "the fulfilled need must never be re-bought");
        }

        [Test]
        public void SundayClosed_MondayBuys_Acceptance_PurchaseToMeal()
        {
            // Slice 6 S6-3 (M7): store closed during a legitimate
            // opportunity (Sunday/Monday pair). Single man; Sunday day 6,
            // Monday day 7. Sunday: no trip, failure names "closed", need
            // stays open, balances and stock unchanged. Monday: the SAME
            // need (same NeedSequence) buys — trip executes, 10u
            // hand-created need settles 50c (Slice 5
            // SundayClosed_MondayBuys_SingleHousehold's shape). Tuesday:
            // at least one meal served from Monday's lot. Sunday's work is
            // closed at day end via CompleteTask before Monday's drive.
            // Sunday closure is intended behaviour (StoreTradingHours
            // OpenDayOfWeekIndices 0-5), not a defect; no missing policy.
            Fixture f = Fixture.CreateSingle(female: false);
            f.Day = 6; // Sunday
            f.Fund(10000);

            HouseholdReserveState reserve = f.Reserve();
            Assert.IsNotNull(reserve, "the household must hold a staple_food reserve");
            int reserveTarget = reserve.targetUnits;
            Assert.Greater(reserveTarget, 0, "the staple_food target must be computed from the catalog, not assumed");
            int reserveBefore = reserve.currentUnits;

            int storeCashBefore = f.Port.CashBalance;
            int storeStockBefore = f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId);
            int totalCashBefore = storeCashBefore + f.Balance();
            int totalBreadBefore = storeStockBefore + f.BreadUnits();
            Assert.AreEqual(50000, storeCashBefore, "the fixture store opens with 50000c");
            Assert.AreEqual(16, storeStockBefore, "the fixture store opens with 16 loaves");
            Assert.AreEqual(0, f.BreadUnits(), "the household opens with no bread");

            // Sunday work: planned and started the production way, then
            // closed at day end so Monday's trip legs can be assigned
            // (TaskAuthority.StartTask refuses while the person already
            // holds an InProgress task — Slice 5 field correction).
            f.PlanDay();
            WorkTask sundayWork = f.CreateWorkTask(1, 240);
            var workDiag = new List<string>();
            int sundayStarted = f.StartWork(
                new List<WorkTask> { sundayWork }, new List<int> { 1 }, new List<int> { 240 }, workDiag);
            Assert.AreEqual(1, sundayStarted,
                "Sunday's work task must start (refusals: " + string.Join(" | ", workDiag) + ")");
            Assert.AreEqual(TaskStatus.InProgress, sundayWork.Status, "Sunday's work task must be started, not merely assigned");

            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.BreadId, 10);
            Assert.AreEqual(10, need.UnitsNeeded, "the hand-created need is 10u");
            int firstSequence = need.NeedSequence;

            // --- Sunday day 6: closed. ---
            HouseholdShoppingLoop.BatchShoppingResult sunday = f.Loop.ExecuteBatch(
                new List<HouseholdPurchasingNeed> { need }, 6, f.Options(), "sunday-closed");
            Assert.IsNotNull(sunday);
            Assert.IsTrue(!sunday.TripExecuted, "Sunday trading is closed");
            Assert.IsTrue(sunday.FailureReason.Contains("closed"),
                "the Sunday failure must name the closure, got: " + sunday.FailureReason);
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status, "the need must stay open on Sunday");
            Assert.AreEqual(10, need.UnitsNeeded, "the need must keep its full 10u on Sunday");
            Assert.AreEqual(firstSequence, need.NeedSequence, "the need must be the same need on Sunday");
            Assert.AreEqual(0, f.ShoppingReservationCount(1), "no shopping reservation may be held on Sunday");
            Assert.AreEqual(10000, f.Balance(), "no money may move on Sunday");
            Assert.AreEqual(50000, f.Port.CashBalance, "store cash must be unchanged on Sunday");
            Assert.AreEqual(16, f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId), "no stock may move on Sunday");
            Assert.AreEqual(0, f.BreadUnits(), "the household must still hold no bread");

            // Close Sunday's work at day end before Monday's drive.
            Assert.IsTrue(f.Tasks.CompleteTask(sundayWork.TaskId, TaskOutcome.Success, 0, f.Budgets),
                "Sunday's work must complete at day end");

            // --- Monday day 7: the SAME need buys. ---
            f.Day = 7;
            f.Budgets.EnsureDay(7);
            HouseholdShoppingLoop.BatchShoppingResult monday = f.Loop.ExecuteBatch(
                new List<HouseholdPurchasingNeed> { need }, 7, f.Options(), "monday-open");
            Assert.IsNotNull(monday);
            Assert.IsTrue(monday.TripExecuted, "the identical trip on Monday must succeed: " + monday.FailureReason);
            Assert.AreEqual(1, monday.ShopperPersonId, "the lone adult shops");
            Assert.AreEqual(firstSequence, need.NeedSequence,
                "Monday must serve the SAME need that stayed open Sunday, not a fresh duplicate");
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status, "the need must be fulfilled by the Monday trip");
            Assert.AreEqual(0, need.UnitsNeeded, "the fulfilled need must have nothing left");

            // The Monday trip departs at the first feasible departure
            // (480, no work reservation today), window [480,575].
            PersonActivityReservation shopping = f.FindReservation(1, PersonActivityKind.Shopping);
            Assert.IsNotNull(shopping, "the trip must have held a shopping reservation");
            Assert.AreEqual(480, shopping.StartMinuteOfDay,
                "the trip must depart at 480; window was [" +
                shopping.StartMinuteOfDay + "-" + shopping.EndMinuteOfDay + "]");
            Assert.AreEqual(575, shopping.EndMinuteOfDay, "the 95-min trip from 480 ends at 575");

            // Travels from the real location: home -> store -> home, from
            // the SetPersonLocation WRITES the legs perform, as in S6-1.
            var hops = new List<string>();
            foreach (LocationHop hop in f.Transitions)
            {
                if (hop.PersonId == 1) hops.Add(hop.LocationId);
            }

            Assert.AreEqual(2, hops.Count, "the shopper must move exactly twice: to the store, then home");
            Assert.AreEqual("store", hops[0], "first move is home -> store");
            Assert.AreEqual("home", hops[1], "second move is store -> home");

            ShoppingTripResult breadResult = FindNeedResult(monday, HouseholdItemCatalog.BreadId);
            Assert.IsNotNull(breadResult, "the batch must carry its own bread result");
            Assert.IsTrue(breadResult.Success, breadResult.FailureReason);
            Assert.AreEqual(10, breadResult.UnitsAcquired, "the trip must acquire the full 10-loaf need");

            // Exact price: outflow = units sold x 5c, receipt recorded.
            Assert.AreEqual(50, breadResult.BuyerOutflowCents, "10 loaves x 5c");
            Assert.IsTrue(!string.IsNullOrEmpty(breadResult.ReceiptId), "the settlement must record a receipt id");
            Assert.IsTrue(string.IsNullOrEmpty(breadResult.PartialFillReason),
                "a fully fulfilled trip carries no partial-fill reason");

            // Cash and inventory reconcile: store cash +outflow, store
            // stock -units, buyer outflow == store cash in, no receivable.
            Assert.AreEqual(storeCashBefore + 50, f.Port.CashBalance, "seller cash must gain exactly 50c");
            Assert.AreEqual(storeStockBefore - 10, f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId),
                "seller stock must fall by exactly the 10 loaves sold");
            Assert.AreEqual(breadResult.BuyerOutflowCents, breadResult.StoreCashInCents,
                "buyer outflow must equal store cash in");
            Assert.AreEqual(0, breadResult.StoreReceivableCents, "cash settlement creates no receivable");
            Assert.AreEqual(10000 - 50, f.Balance(), "buyer debit must equal 10 loaves x the 5c price");

            // Identifiable goods: a lot with purchase provenance + receipt
            // id, custody ReceivedAtHousehold.
            HouseholdItemLot lot = f.BreadLot();
            Assert.IsNotNull(lot, "the household must hold a bread lot after the purchase");
            Assert.AreEqual(10, lot.QuantityUnits, "UnitsAcquired must equal the settled units");
            Assert.IsTrue(lot.SourceLabel.Contains("purchased 10u bread from"),
                "the lot must carry purchase provenance, got: " + lot.SourceLabel);
            Assert.IsTrue(lot.SourceLabel.Contains(breadResult.ReceiptId),
                "the lot provenance must carry the receipt id, got: " + lot.SourceLabel);
            Assert.IsTrue(HasEvent(breadResult, ShoppingEventKind.Custody),
                "the custody transfer into the shopper's hands must be recorded");
            Assert.IsTrue(HasEvent(breadResult, ShoppingEventKind.Receipt),
                "the physical receipt at the household must be recorded");
            Assert.AreEqual(0, f.Loop.ActiveCustodyBatches.Count,
                "no custody batch may be left stranded after receipt");

            // The reserve mirror credits the receipt (clamped to target).
            int credited = Math.Min(reserveTarget, reserveBefore + 10);
            Assert.AreEqual(credited, reserve.currentUnits,
                "the receipt must credit the reserve by the units received (clamped to target)");

            // Total cash and total bread conserved; the fulfilled need is
            // never re-bought.
            Assert.AreEqual(totalCashBefore, f.Port.CashBalance + f.Balance(),
                "cash must move, not be created or destroyed");
            Assert.AreEqual(totalBreadBefore, f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId) + f.BreadUnits(),
                "bread must move, not be created or destroyed");
            List<HouseholdErrandPlan> replans = f.Coordinator.PlanDay(f.HouseholdId, f.Day, f.Context(), f.Diagnostics);
            Assert.AreEqual(0, replans.Count, "no open need remains, so no second plan — and no second sale — may form");
            Assert.AreEqual(6, f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId),
                "the store stock must be untouched by the re-plan");

            // --- Tuesday day 8: the meal leg (points 8-9), reusing the
            // S6-1 helper. No Monday work task was created (Monday's drive
            // is the existing test's direct ExecuteBatch shape), so no
            // prior-day work is closed here; Sunday's work was already
            // closed before Monday's drive.
            int receivedLotId = lot.LotId;
            IReadOnlyList<HouseholdMealRecord> meals = f.DriveNextDayMeal(null);
            Assert.Greater(meals.Count, 0, "at least one meal must be served on Tuesday");

            // At least one meal served from Monday's lot, with the lot id
            // in the meal log.
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

            Assert.IsTrue(lotReferenced,
                "at least one Tuesday meal record must reference Monday's purchased lot id " + receivedLotId);
            Assert.Greater(eaten, 0, "the Tuesday meals must have eaten bread from Monday's lot");

            // The meal consumption mirror decrements the reserve by the
            // loaves eaten.
            Assert.AreEqual(Math.Max(0, credited - eaten), reserve.currentUnits,
                "meal consumption must decrement the reserve by the loaves eaten");
            Assert.AreEqual(10 - eaten, f.BreadUnits(),
                "the household must hold 10 loaves minus the loaves eaten");

            // Bread conservation with consumption accounted; cash unchanged
            // by meals; need stays fulfilled.
            Assert.AreEqual(totalBreadBefore,
                f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId) + f.BreadUnits() + eaten,
                "bread must be conserved across purchase and meals");
            Assert.AreEqual(totalCashBefore, f.Port.CashBalance + f.Balance(),
                "cash must stay conserved after meals");
            Assert.AreEqual(0, f.OpenNeedCount(HouseholdItemCatalog.BreadId),
                "the fulfilled need must never be re-bought");
        }

        [Test]
        public void NoDoubleBookingAfterReorder()
        {
            // Slice 4 test 4: after a day with both a trip and work, the
            // person holds no overlapping reservations of any kind and the
            // shopping reservation is released on completion (Plan 02 Slice 1
            // invariant, re-pinned under the new order).
            Fixture f = Fixture.CreateSingle(female: false);
            f.Fund(10000);
            WorkTask work = f.CreateWorkTask(1, 240);
            var workDiag = new List<string>();
            int started;
            HouseholdShoppingLoop.BatchShoppingResult batch = f.DriveProductionDay(
                new List<WorkTask> { work }, new List<int> { 1 }, new List<int> { 240 },
                out started, workDiag);

            Assert.IsNotNull(batch, "a plan must be created for the open bread need");
            Assert.IsTrue(batch.TripExecuted, "the trip must execute: " + batch.FailureReason);
            Assert.AreEqual(1, started,
                "the work task must start (refusals: " + string.Join(" | ", workDiag) + ")");
            Assert.IsFalse(f.ActiveReservationsOverlap(1), "the person must hold no overlapping reservations");
            PersonActivityReservation shopping = f.FindReservation(1, PersonActivityKind.Shopping);
            Assert.IsNotNull(shopping, "the trip must have held a shopping reservation");
            Assert.IsTrue(shopping.Released, "the shopping reservation must be released on completion");
            Assert.IsTrue(f.HasReservation(1, PersonActivityKind.Work, 480, 720, released: false),
                "the Work reservation [480,720] must still be held");
        }

        [Test]
        public void RemoteShopper_LocationConsistent_NoTripFromHome()
        {
            // Slice 4 test 6 (rev1 section 5b): location-consistency pin.
            // Two-adult household, member 0 (Tomas) at home, member 1 (Anna,
            // the shopper, woman-first) with scheduleState = AtWork and a
            // remote workplace (forest-camp, 18 mi from the store). The
            // pre-check, estimate, and legs must all resolve the workplace:
            // 18 mi at the fixture's 20 min/mi = 360 min one way, a 735-min
            // trip — budget (600) and store hours both refuse it — with NO
            // leg planned from home. The trip must fail honestly on the
            // workplace travel minutes: the corrected search exits early
            // with "No shopping window: the 735-min trip cannot fit ..."
            // (last feasible departure 345 < 480), so the reason names the
            // 735-min workplace trip, not the 95-min home trip and not a
            // leg-assignment failure. PROBE: fails at HEAD (the estimate
            // uses member-0 home, 95 min, so the search finds a window and
            // the trip fails late at leg assignment: "Cannot assign T2: P2
            // has 225 of 600 daily work minutes remaining; the task needs
            // 360").
            Fixture f = Fixture.CreatePair();
            f.AddRemoteWorkplace();
            PersonState anna = f.Population.GetPerson(2);
            Assert.IsNotNull(anna, "Anna must exist");
            anna.scheduleState = PopulationScheduleState.AtWork;
            anna.workplaceBuildingId = 900;
            f.Fund(10000);

            HouseholdShoppingLoop.BatchShoppingResult batch = f.DriveProductionDay(
                new List<WorkTask>(), new List<int>(), new List<int>(),
                out int started, new List<string>());
            Assert.AreEqual(0, started, "no work is assigned in this location probe");

            Assert.IsNotNull(batch, "a plan must be created for the open bread need");
            Assert.IsTrue(!batch.TripExecuted,
                "no trip may execute: from the workplace the trip needs 735 min (failure reason: " +
                batch.FailureReason + ")");
            Assert.IsNotNull(f.BreadNeed(), "the need must stay open");
            Assert.AreEqual(PurchasingNeedStatus.Open, f.BreadNeed().Status, "the need must stay open");
            Assert.IsTrue(batch.FailureReason.Contains("735-min trip"),
                "the failure must name the WORKPLACE trip length (735 min), not the 95-min home trip, got: " +
                batch.FailureReason);
            Assert.IsTrue(batch.FailureReason.Contains("last feasible departure 345"),
                "the failure must name the window boundary for the 735-min trip, got: " + batch.FailureReason);
            Assert.AreEqual(0, f.ShoppingReservationCount(1), "no shopping reservation may be held for Tomas");
            Assert.AreEqual(0, f.ShoppingReservationCount(2), "no shopping reservation may be held for Anna");
        }

        [Test]
        public void LongestOpenNeed_NamedInSummary()
        {
            // Slice 5 test 1 (original plan section 10, rev1 section 6).
            // A household with an impossible errand (full-coverage 600-min
            // shift) and a second, servable-in-principle need (flour, real
            // offer at 35c/sack, 1 sack = 10 lb — the EndToEnd flour-offer
            // shape): the summary must name the LONGEST-OPEN need — bread,
            // opened day 0 — with its age and its first failure reason.
            // The summary line itself lives in DriveEmbodiedShopping
            // (SimulationDrivers.cs, harness-excluded), so this test pins,
            // through the real loop/monitor/registry, exactly the three
            // facts the line prints — identity, age, first failure reason —
            // and the line's wording is source-review + Unity-owed. The
            // expected day-1 line is: "[FoodEconomy] shopping summary day
            // 1: 0 purchase(s), 0c spent; longest open: H7 'bread' open
            // since day 0 (1d) — Unavailable shopper: No shopper in any
            // searched window from 480 to 985 (trip needs 95 min):
            // departure 480: P1: only 0 work minutes left, trip needs 95.
            // ... Needs stay open.; still open: H7 bread (...); H7 flour
            // (...)" (both still-open entries carry the same no-shopper
            // reason; the full reason lists every 15-min departure).
            // FIELD CORRECTION (verified in source, not assumed): the plan
            // names LastEvaluatedDayIndex as the longest-open key, but that
            // field is refreshed to the current day on every monitor
            // evaluation (HouseholdShortage.cs:267) — after day 1 bread and
            // flour TIE at 1, so the literal rule cannot identify a
            // longest-open need at all. CreatedDayIndex is set once at
            // creation (HouseholdShortage.cs:389) and never reassigned, so
            // it is the field the Slice 5 production line uses; this test
            // pins the tie (both LastEvaluatedDayIndex == 1) and the
            // CreatedDayIndex order (0 < 1) that resolves it.
            Fixture f = Fixture.CreateSingle(female: false);
            f.Port.OfferList.Add(new GeneralStoreCategoryOffer
            {
                CategoryId = "staple_food",
                OfferedItemIds = new List<string> { HouseholdItemCatalog.FlourId },
                StoreUnitsPerItemUnit = 1,
                HouseholdUnitsPerStoreUnit = 10,
                UsesItemLevelInventory = true,
            });
            f.Port.ItemLevelItems.Add(HouseholdItemCatalog.FlourId);
            f.Port.ItemStock[HouseholdItemCatalog.FlourId] = 5;
            f.Port.ItemPrices[HouseholdItemCatalog.FlourId] = 35;
            f.Fund(10000);

            // Day 0: bread opens under the full-coverage shift; no trip.
            WorkTask work0 = f.CreateWorkTask(1, 600);
            var workDiag0 = new List<string>();
            HouseholdShoppingLoop.BatchShoppingResult batch0 = f.DriveProductionDay(
                new List<WorkTask> { work0 }, new List<int> { 1 }, new List<int> { 600 },
                out int started0, workDiag0);
            Assert.AreEqual(1, started0, "day 0: the work task must start");
            Assert.IsNotNull(batch0, "day 0: a plan must be created for the open bread need");
            Assert.IsTrue(!batch0.TripExecuted, "day 0: no trip may execute over a full-coverage shift");
            // Day 0's work completes at day end: TaskAuthority.StartTask
            // refuses a new task while the person already holds an
            // InProgress task (the Slice 4 occupancy finding), so without
            // this close day 1's task could never start.
            Assert.IsTrue(f.Tasks.CompleteTask(work0.TaskId, TaskOutcome.Success, 0, f.Budgets),
                "day 0's work must complete at day end");
            HouseholdPurchasingNeed bread0 = f.BreadNeed();
            Assert.IsNotNull(bread0, "day 0: the bread need must stay open");
            Assert.AreEqual(0, bread0.CreatedDayIndex, "day 0: bread opens on day 0");
            ShoppingTripResult breadResult0 = FindNeedResult(batch0, HouseholdItemCatalog.BreadId);
            Assert.IsNotNull(breadResult0, "day 0: the bread need must carry its own per-need result");
            Assert.IsTrue(breadResult0.FailureReason.Contains("No shopper in any searched window"),
                "day 0: the bread per-need reason must name the searched window, got: " + breadResult0.FailureReason);
            Assert.IsTrue(breadResult0.FailureReason.Contains("work minutes left"),
                "day 0: the bread per-need reason must name the budget shortfall, got: " + breadResult0.FailureReason);

            // Day 1: a second need opens (flour, hand-created — the test
            // says so; the monitor is not asked to raise it). Both needs
            // fail on the same no-shopper reason; bread is longest-open.
            f.Day = 1;
            f.Budgets.EnsureDay(1);
            HouseholdPurchasingNeed flour = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            Assert.AreEqual(1, flour.CreatedDayIndex, "day 1: flour opens on day 1");
            WorkTask work1 = f.CreateWorkTask(1, 600);
            var workDiag1 = new List<string>();
            HouseholdShoppingLoop.BatchShoppingResult batch1 = f.DriveProductionDay(
                new List<WorkTask> { work1 }, new List<int> { 1 }, new List<int> { 600 },
                out int started1, workDiag1);
            Assert.AreEqual(1, started1, "day 1: the work task must start");
            Assert.IsNotNull(batch1, "day 1: a plan must be created for the open needs");
            Assert.IsTrue(!batch1.TripExecuted, "day 1: no trip may execute over a full-coverage shift");

            HouseholdPurchasingNeed bread = f.BreadNeed();
            Assert.IsNotNull(bread, "day 1: the bread need must stay open");
            Assert.IsNotNull(f.Needs.GetOpenNeed(f.HouseholdId, HouseholdItemCatalog.FlourId),
                "day 1: the flour need must stay open");
            // Longest-open identity + age, by creation day: bread (day 0)
            // predates flour (day 1), so on day 1 bread is 1 day old.
            Assert.IsTrue(bread.CreatedDayIndex < flour.CreatedDayIndex,
                "bread (created day " + bread.CreatedDayIndex + ") must be the longest-open need, ahead of flour (created day " +
                flour.CreatedDayIndex + ")");
            Assert.AreEqual(1, f.Day - bread.CreatedDayIndex, "day 1: the longest-open need is 1 day old");
            // The literal plan key cannot resolve this: the day-1 monitor
            // evaluation refreshed bread to today, tying flour exactly.
            Assert.AreEqual(1, bread.LastEvaluatedDayIndex,
                "day 1: the monitor refreshed bread's LastEvaluatedDayIndex to today");
            Assert.AreEqual(bread.LastEvaluatedDayIndex, flour.LastEvaluatedDayIndex,
                "LastEvaluatedDayIndex ties at day 1 for both needs — it cannot identify the longest-open need; CreatedDayIndex does");
            // First failure reason for the longest-open need: the
            // budget-blocked search text the summary prints.
            ShoppingTripResult breadResult1 = FindNeedResult(batch1, HouseholdItemCatalog.BreadId);
            Assert.IsNotNull(breadResult1, "day 1: the bread need must carry its own per-need result");
            Assert.IsTrue(breadResult1.FailureReason.Contains("No shopper in any searched window"),
                "day 1: the longest-open need's reason must name the searched window, got: " + breadResult1.FailureReason);
            Assert.IsTrue(breadResult1.FailureReason.Contains("work minutes left"),
                "day 1: the longest-open need's reason must name the budget shortfall, got: " + breadResult1.FailureReason);
            Assert.AreEqual(0, f.ShoppingReservationCount(1), "no shopping reservation may ever be created");
        }

        [Test]
        public void UrgentNeed_AttemptedFirst()
        {
            // Slice 5 test 2 (original plan section 10, rev1 section 6):
            // the urgency-sort pin. Two monitor-set open needs — bread at
            // urgency 1.0 (0 days of supply, 2/day) and flour at low
            // urgency (6 days of supply on hand at 1/day, urgency
            // 1 - 6/7) — with cash for exactly ONE of them (70c: the
            // 14-loaf bread need at 5c). The batch sort
            // (HouseholdShoppingLoop.cs:643, Urgency01 desc) must make the
            // high-urgency need claim the shared ledger budget first, so
            // bread is served in full and flour stays open. Counterfactual
            // that makes this a real end-to-end pin: had flour been
            // evaluated first it would claim 35c and bread would be
            // affordability-downsized to 7 loaves — the observed full bread
            // service plus unserved flour can only happen with bread first.
            // The flour failure names the OTHER budget text — Plan 02 Slice
            // 5's cash-affordability 'budget short'
            // (HouseholdShoppingLoop.cs:2241) — which must not be confused
            // with the time-budget 'work minutes left' search text
            // (PersonScheduleTracker.cs:762).
            Fixture f = Fixture.CreateSingle(female: false);
            f.Port.OfferList.Add(new GeneralStoreCategoryOffer
            {
                CategoryId = "staple_food",
                OfferedItemIds = new List<string> { HouseholdItemCatalog.FlourId },
                StoreUnitsPerItemUnit = 1,
                HouseholdUnitsPerStoreUnit = 10,
                UsesItemLevelInventory = true,
            });
            f.Port.ItemLevelItems.Add(HouseholdItemCatalog.FlourId);
            f.Port.ItemStock[HouseholdItemCatalog.FlourId] = 5;
            f.Port.ItemPrices[HouseholdItemCatalog.FlourId] = 35;
            f.Fund(70);
            string seedProblem = f.Inventories.GetOrCreate(f.HouseholdId).AddLot(
                HouseholdItemCatalog.FlourId, 6, 0, "test seed: 6u flour on hand");
            Assert.IsNull(seedProblem, seedProblem);

            // Raise both needs through the real monitor in one evaluation.
            f.Monitor.Evaluate(
                f.Population.GetHousehold(f.HouseholdId),
                f.Inventories.GetOrCreate(f.HouseholdId),
                new Dictionary<string, int>
                {
                    { HouseholdItemCatalog.BreadId, 2 },
                    { HouseholdItemCatalog.FlourId, 1 },
                },
                false,
                new HouseholdSupplyAccess { HasGeneralStore = true, HasCash = true },
                0, 0, f.Day,
                HouseholdShortageMonitor.KnownSellerRecognitionPredicate(f.Directory, f.Knowledge, f.MemberIds));
            HouseholdPurchasingNeed bread = f.BreadNeed();
            HouseholdPurchasingNeed flour = f.Needs.GetOpenNeed(f.HouseholdId, HouseholdItemCatalog.FlourId);
            Assert.IsNotNull(bread, "the monitor must open the bread need");
            Assert.IsNotNull(flour, "the monitor must open the flour need");
            Assert.AreEqual(1f, bread.Urgency01, "bread at 0 days of supply is maximally urgent");
            Assert.AreEqual(14, bread.UnitsNeeded, "bread need is 14u (7 days at 2/day)");
            Assert.IsTrue(flour.Urgency01 > 0f && flour.Urgency01 < 0.5f,
                "flour at 6 days of supply is low-urgency, got: " + flour.Urgency01);
            Assert.AreEqual(1, flour.UnitsNeeded, "flour need is 1u (1 day short of the 7-day threshold)");
            Assert.IsTrue(bread.Urgency01 > flour.Urgency01, "bread must outrank flour on urgency");

            // No work today: the person is free to shop. Drive the plan.
            f.PlanDay();
            List<HouseholdErrandPlan> plans = f.Coordinator.PlanDay(f.HouseholdId, f.Day, f.Context(), f.Diagnostics);
            Assert.AreEqual(1, plans.Count, "one plan must batch both open needs");
            HouseholdShoppingLoop.BatchShoppingResult batch =
                f.Coordinator.ExecutePlan(plans[0], f.Day, f.Context(), f.Loop, f.Options(), f.Diagnostics);

            Assert.IsNotNull(batch, "a batch must be produced");
            Assert.AreEqual(2, batch.NeedResults.Count, "both needs must carry per-need results");
            Assert.IsTrue(batch.TripExecuted, "the trip must execute: " + batch.FailureReason);
            Assert.AreEqual(1, batch.ServedNeedCount, "exactly one need may be served on 70c");
            ShoppingTripResult breadResult = FindNeedResult(batch, HouseholdItemCatalog.BreadId);
            ShoppingTripResult flourResult = FindNeedResult(batch, HouseholdItemCatalog.FlourId);
            Assert.IsNotNull(breadResult, "bread must carry a per-need result");
            Assert.IsNotNull(flourResult, "flour must carry a per-need result");
            Assert.IsTrue(breadResult.Success, "the high-urgency bread need must be served, got: " + breadResult.FailureReason);
            Assert.AreEqual(14, breadResult.UnitsAcquired, "bread must be served in full, not downsized");
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, bread.Status, "the bread need must be fulfilled");
            Assert.AreEqual(0, bread.UnitsNeeded, "the bread need must have nothing left");
            Assert.IsTrue(!flourResult.Success, "the low-urgency flour need must stay unserved");
            Assert.AreEqual(PurchasingNeedStatus.Open, flour.Status, "the flour need must stay open");
            Assert.AreEqual(1, flour.UnitsNeeded, "the flour need must keep its full 1u");
            Assert.IsTrue(flourResult.FailureReason.Contains("budget short"),
                "the flour reason must name the cash-affordability budget short, got: " + flourResult.FailureReason);
            Assert.IsTrue(flourResult.FailureReason.Contains("unaffordable"),
                "the flour reason must name unaffordability, got: " + flourResult.FailureReason);
            Assert.AreEqual(6, f.Inventories.GetOrCreate(f.HouseholdId).GetAvailableUnits(HouseholdItemCatalog.FlourId),
                "no flour may be bought — the household still holds its 6u");
            Assert.AreEqual(5, f.Port.ItemStock[HouseholdItemCatalog.FlourId], "the store must still hold all 5 flour sacks");
            Assert.AreEqual(0, f.Balance(), "the 70c must be spent in full on the bread");
        }

        [Test]
        public void ImpossibleErrand_FiveDays_NoAccumulation()
        {
            // Slice 5 test 3 (original plan section 10, rev1 section 6):
            // Slice 0 test 8 extended to five days on the corrected
            // mechanisms (600-min task, work-first, rest rule). Still one
            // open need, zero trips, zero leaked reservations — and the
            // per-need reason the Slice 5 summary line prints names the
            // blocker every day. The summary line itself lives in
            // DriveEmbodiedShopping (SimulationDrivers.cs,
            // harness-excluded), so the blocker-named-every-day assertion
            // is made on the per-need failure reason the summary reads;
            // the line is source-review + Unity-owed.
            Fixture f = Fixture.CreateSingle(female: false);
            f.Fund(10000);
            int trips = 0;
            int shoppingReservationsEver = 0;
            int firstSequence = -1;
            for (int day = 0; day <= 4; day++)
            {
                f.Day = day;
                f.Budgets.EnsureDay(day);
                WorkTask work = f.CreateWorkTask(1, 600);
                var workDiag = new List<string>();
                int started;
                HouseholdShoppingLoop.BatchShoppingResult batch = f.DriveProductionDay(
                    new List<WorkTask> { work }, new List<int> { 1 }, new List<int> { 600 },
                    out started, workDiag);
                if (batch != null && batch.TripExecuted) trips++;
                shoppingReservationsEver += f.ShoppingReservationCount(1);
                Assert.IsTrue(batch == null || !batch.TripExecuted,
                    "day " + day + ": no trip may execute although the shift covers every window");
                Assert.IsTrue(batch == null || batch.FailureReason.Contains("No shopper in any searched window"),
                    "day " + day + ": the batch must fail naming the searched window, got: " +
                    (batch == null ? "(no batch)" : batch.FailureReason));
                Assert.IsTrue(batch == null || batch.FailureReason.Contains("work minutes left"),
                    "day " + day + ": the batch failure must name the budget shortfall, got: " +
                    (batch == null ? "(no batch)" : batch.FailureReason));
                // Slice 5 verification: the PER-NEED reason — the reason
                // the daily summary prints for this open need — carries
                // the search's budget text (PersonScheduleTracker.cs:762),
                // not the Plan 02 Slice 5 cash-affordability 'budget
                // short' (HouseholdShoppingLoop.cs:2241).
                ShoppingTripResult breadResult = FindNeedResult(batch, HouseholdItemCatalog.BreadId);
                Assert.IsNotNull(breadResult, "day " + day + ": the bread need must carry its own per-need result");
                Assert.IsTrue(breadResult.FailureReason.Contains("No shopper in any searched window"),
                    "day " + day + ": the per-need reason must name the searched window, got: " + breadResult.FailureReason);
                Assert.IsTrue(breadResult.FailureReason.Contains("work minutes left"),
                    "day " + day + ": the per-need reason must name the budget shortfall, got: " + breadResult.FailureReason);
                HouseholdPurchasingNeed need = f.BreadNeed();
                Assert.IsNotNull(need, "day " + day + ": a bread need must be open");
                if (firstSequence < 0) firstSequence = need.NeedSequence;
                Assert.AreEqual(firstSequence, need.NeedSequence,
                    "day " + day + ": the same single need must persist, not a fresh duplicate");
                Assert.AreEqual(1, f.OpenNeedCount(HouseholdItemCatalog.BreadId),
                    "day " + day + ": exactly one open bread need may exist");
            }

            Assert.AreEqual(0, trips, "zero trips may execute over the five days");
            Assert.AreEqual(0, shoppingReservationsEver, "zero shopping reservations may ever be created");
            Assert.AreEqual(1, f.OpenNeedCount(HouseholdItemCatalog.BreadId), "exactly one open bread need after day 5");
        }

        [Test]
        public void RemoteWorker_SubstantialTravel_NeedPreserved_NamesRemotePolicy()
        {
            // Plan 05 Slice 6 (S6-4), M6: honest failure with the missing
            // policy named. Single employed man at a remote on-map
            // workplace (forest-camp, 20 mi from home, 18 mi from the
            // store) with a 540-min shift, driven 2 days. The worker is AT
            // the workplace when shopping is considered (scheduleState
            // AtWork, as in the Slice 4 location-consistency probe
            // RemoteShopper_LocationConsistent_NoTripFromHome), so the loop
            // prices the trip from the workplace: 18 mi at 20 min/mi =
            // 360 min one way, a 735-min trip that fits neither the
            // 600-min budget nor store hours (last feasible departure 345
            // < 480). No trip executes, no trip departs from home, no
            // shopping reservation is held, the need stays open with the
            // same NeedSequence, and nothing moves.
            // MISSING DESIGN DEPENDENCY (named here and in the slice
            // report, NOT in any production diagnostic string): R-REMOTE —
            // workplace-departed trips / commute shopping. No
            // dispatch/pathing design exists for when a worker is sent to
            // work, when they return, or whether shopping departs from the
            // workplace, so no legitimate opportunity exists at all under
            // current Canon and architecture. The honest failure IS the
            // correct behaviour; nothing here is repaired, and no leave
            // policy, day-off roster, or R-REMOTE design is invented.
            Fixture f = Fixture.CreateSingle(female: false);
            f.AddRemoteWorkplace();
            PersonState worker = f.Population.GetPerson(1);
            Assert.IsNotNull(worker, "the single worker must exist");
            worker.scheduleState = PopulationScheduleState.AtWork;
            f.Fund(10000);
            int firstSequence = -1;
            for (int day = 0; day <= 1; day++)
            {
                f.Day = day;
                f.Budgets.EnsureDay(day);
                // The worker stays at the workplace across the drive:
                // nothing in the reserve/assign/start path resets
                // scheduleState (TaskAuthority and
                // ManagerWorkSchedulerDrive never write it; the loop only
                // writes it on trip legs, and no trip executes here).
                Assert.AreEqual(PopulationScheduleState.AtWork, f.Population.GetPerson(1).scheduleState,
                    "day " + day + ": the worker must still be at the remote workplace when shopping is considered");
                WorkTask work = f.CreateWorkTask(1, 540);
                var workDiag = new List<string>();
                int resolvedBefore = f.ResolvedLocations.Count;
                int transitionsBefore = f.Transitions.Count;
                int started;
                HouseholdShoppingLoop.BatchShoppingResult batch = f.DriveProductionDay(
                    new List<WorkTask> { work }, new List<int> { 1 }, new List<int> { 540 },
                    out started, workDiag);
                Assert.AreEqual(1, started,
                    "day " + day + ": the 540-min work task must start (refusals: " + string.Join(" | ", workDiag) + ")");
                Assert.IsNotNull(batch, "day " + day + ": a plan must be created for the open bread need");
                Assert.IsTrue(!batch.TripExecuted,
                    "day " + day + ": no trip may execute from the remote workplace (failure reason: " + batch.FailureReason + ")");
                // No trip departs from home: no SetPersonLocation write
                // moves the worker to the store (asserted from the
                // Transitions writes, not from PersonLocationId reads —
                // S6-1 lesson), and every location the loop resolved
                // during the drive is the workplace, never home.
                for (int i = transitionsBefore; i < f.Transitions.Count; i++)
                {
                    Assert.IsTrue(!string.Equals(f.Transitions[i].LocationId, "store", StringComparison.Ordinal),
                        "day " + day + ": no trip leg may move the worker to the store");
                }

                Assert.IsTrue(f.ResolvedLocations.Count > resolvedBefore,
                    "day " + day + ": the loop must resolve the shopper's location during the drive");
                for (int i = resolvedBefore; i < f.ResolvedLocations.Count; i++)
                {
                    Assert.AreEqual("forest-camp", f.ResolvedLocations[i],
                        "day " + day + ": every resolved location during the drive must be the workplace, not home");
                }

                Assert.AreEqual(0, f.ShoppingReservationCount(1),
                    "day " + day + ": no shopping reservation may be held");
                HouseholdPurchasingNeed need = f.BreadNeed();
                Assert.IsNotNull(need, "day " + day + ": the bread need must stay open");
                Assert.AreEqual(PurchasingNeedStatus.Open, need.Status, "day " + day + ": the need must stay open");
                if (firstSequence < 0) firstSequence = need.NeedSequence;
                Assert.AreEqual(firstSequence, need.NeedSequence,
                    "day " + day + ": the same single need must persist, not a fresh duplicate");
                Assert.AreEqual(1, f.OpenNeedCount(HouseholdItemCatalog.BreadId),
                    "day " + day + ": exactly one open bread need may exist");
                Assert.IsTrue(batch.FailureReason.Contains("735-min trip"),
                    "day " + day + ": the failure must name the WORKPLACE trip length (735 min), got: " + batch.FailureReason);
                Assert.IsTrue(batch.FailureReason.Contains("last feasible departure 345"),
                    "day " + day + ": the failure must name the window boundary for the 735-min trip, got: " + batch.FailureReason);
                ShoppingTripResult breadResult = FindNeedResult(batch, HouseholdItemCatalog.BreadId);
                Assert.IsNotNull(breadResult, "day " + day + ": the bread need must carry its own per-need result");
                Assert.IsTrue(breadResult.FailureReason.Contains("735-min trip"),
                    "day " + day + ": the per-need reason must name the WORKPLACE trip length, got: " + breadResult.FailureReason);
                Assert.IsTrue(breadResult.FailureReason.Contains("last feasible departure 345"),
                    "day " + day + ": the per-need reason must name the window boundary, got: " + breadResult.FailureReason);
                Assert.AreEqual(10000, f.Balance(), "day " + day + ": no money may move without a trip");
                Assert.AreEqual(16, f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId),
                    "day " + day + ": no stock may move without a trip");
                if (day == 0)
                {
                    Assert.IsTrue(f.Tasks.CompleteTask(work.TaskId, TaskOutcome.Success, 0, f.Budgets),
                        "day 0's work must complete at day end");
                }
            }

            Assert.AreEqual(1, f.OpenNeedCount(HouseholdItemCatalog.BreadId), "exactly one open bread need after day 2");
            Assert.AreEqual(10000, f.Balance(), "no money may move over the two days");
            Assert.AreEqual(16, f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId), "no stock may move over the two days");
        }

        [Test]
        public void GenuinelyImpossible_FiveDays_NeedPreserved_NamesMissingPolicy()
        {
            // Plan 05 Slice 6 (S6-4), M8: honest failure with the missing
            // policies named. Re-pins Slice 5's
            // ImpossibleErrand_FiveDays_NoAccumulation outcome (zero trips,
            // zero shopping reservations, exactly one open bread need with
            // the same NeedSequence over 5 days under a 600-min shift
            // covering every store minute) PLUS the new assertions: the
            // longest-open age reaches 4 days by day 4 (CreatedDayIndex 0
            // — the Slice 5 field correction: LastEvaluatedDayIndex is
            // refreshed on every evaluation and cannot identify the
            // longest-open need) and the per-need reason names the searched
            // window + budget every day. Each day's work is closed at day
            // end via CompleteTask so the next day's work can start (Slice
            // 5 field correction), and the work task is asserted started
            // each day.
            // MISSING POLICIES (named here and in the slice report, NOT in
            // any production diagnostic string): an employment leave policy
            // — Canon has none, and no universal leave may be invented —
            // and a day-off roster — no model exists in code or Canon.
            // Either could change the outcome; neither is invented here.
            // The honest failure IS the correct behaviour under current
            // Canon and architecture; nothing here is repaired.
            Fixture f = Fixture.CreateSingle(female: false);
            f.Fund(10000);
            int trips = 0;
            int shoppingReservationsEver = 0;
            int firstSequence = -1;
            for (int day = 0; day <= 4; day++)
            {
                f.Day = day;
                f.Budgets.EnsureDay(day);
                WorkTask work = f.CreateWorkTask(1, 600);
                var workDiag = new List<string>();
                int started;
                HouseholdShoppingLoop.BatchShoppingResult batch = f.DriveProductionDay(
                    new List<WorkTask> { work }, new List<int> { 1 }, new List<int> { 600 },
                    out started, workDiag);
                Assert.AreEqual(1, started,
                    "day " + day + ": the 600-min work task must start (refusals: " + string.Join(" | ", workDiag) + ")");
                if (batch != null && batch.TripExecuted) trips++;
                shoppingReservationsEver += f.ShoppingReservationCount(1);
                Assert.IsTrue(batch == null || !batch.TripExecuted,
                    "day " + day + ": no trip may execute although the shift covers every window");
                Assert.IsTrue(batch == null || batch.FailureReason.Contains("No shopper in any searched window"),
                    "day " + day + ": the batch must fail naming the searched window, got: " +
                    (batch == null ? "(no batch)" : batch.FailureReason));
                Assert.IsTrue(batch == null || batch.FailureReason.Contains("work minutes left"),
                    "day " + day + ": the batch failure must name the budget shortfall, got: " +
                    (batch == null ? "(no batch)" : batch.FailureReason));
                ShoppingTripResult breadResult = FindNeedResult(batch, HouseholdItemCatalog.BreadId);
                Assert.IsNotNull(breadResult, "day " + day + ": the bread need must carry its own per-need result");
                Assert.IsTrue(breadResult.FailureReason.Contains("No shopper in any searched window"),
                    "day " + day + ": the per-need reason must name the searched window, got: " + breadResult.FailureReason);
                Assert.IsTrue(breadResult.FailureReason.Contains("work minutes left"),
                    "day " + day + ": the per-need reason must name the budget shortfall, got: " + breadResult.FailureReason);
                HouseholdPurchasingNeed need = f.BreadNeed();
                Assert.IsNotNull(need, "day " + day + ": a bread need must be open");
                if (firstSequence < 0) firstSequence = need.NeedSequence;
                Assert.AreEqual(firstSequence, need.NeedSequence,
                    "day " + day + ": the same single need must persist, not a fresh duplicate");
                Assert.AreEqual(1, f.OpenNeedCount(HouseholdItemCatalog.BreadId),
                    "day " + day + ": exactly one open bread need may exist");
                Assert.AreEqual(0, need.CreatedDayIndex,
                    "day " + day + ": the preserved need must still be the day-0 need (CreatedDayIndex 0)");
                Assert.AreEqual(day, f.Day - need.CreatedDayIndex,
                    "day " + day + ": the longest-open need must be " + day + " day(s) old");
                if (day < 4)
                {
                    Assert.IsTrue(f.Tasks.CompleteTask(work.TaskId, TaskOutcome.Success, 0, f.Budgets),
                        "day " + day + "'s work must complete at day end");
                }
            }

            Assert.AreEqual(0, trips, "zero trips may execute over the five days");
            Assert.AreEqual(0, shoppingReservationsEver, "zero shopping reservations may ever be created");
            Assert.AreEqual(1, f.OpenNeedCount(HouseholdItemCatalog.BreadId), "exactly one open bread need after day 5");
            HouseholdPurchasingNeed finalNeed = f.BreadNeed();
            Assert.IsNotNull(finalNeed, "the bread need must still be open after day 4");
            Assert.AreEqual(0, finalNeed.CreatedDayIndex, "the preserved need was created on day 0");
            Assert.AreEqual(4, f.Day - finalNeed.CreatedDayIndex, "by day 4 the longest-open need is 4 days old");
            Assert.AreEqual(10000, f.Balance(), "no money may move over the five days");
            Assert.AreEqual(16, f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId), "no stock may move over the five days");
        }

        [Test]
        public void Acceptance_ThreeDayRepeat_NoRegistryGrowth()
        {
            // Plan 05 Slice 6 (S6-5), M9: 3-day repeat (H-A across three
            // consecutive trading days). Needs re-open as stock is eaten,
            // trips repeat, purchases settle each day they are needed, one
            // open need per item at a time, no duplicate plan/claim
            // records, no leaked reservations, totals conserved across all
            // 3 days, fulfilled needs never re-bought. Each day's work is
            // closed at day end via CompleteTask (DriveNextDayMeal) before
            // the next day's drive (Slice 5 field correction).
            //
            // M9 RE-OPEN LEVER — verified in source BEFORE writing this
            // test (plan section 2 M9 fixture note). Lever (b): 2u of bread
            // seeded on hand, so the day-0 shortfall is 12u and the 3-day
            // total is exactly the 16 loaves the fixture store holds.
            //   - Monitor sizing (HouseholdShortage.cs:58 LowDaysThreshold
            //     7f; :232-261): shortfall = (7 - stock/2) * 2 = 14 - stock
            //     at 2 loaves/day expected use. Zero stock opens 14u; 12
            //     on hand re-opens 2u. The need is re-sized to the
            //     shortfall on every evaluation — a fulfilled need is never
            //     re-bought; the next day opens a NEW need record.
            //   - Real consumption (HouseholdMeals.cs:111-170, :455-470):
            //     the default policy serves Morning + Evening while staple
            //     days-of-supply (stock / 6) is below the 3-day midday
            //     threshold, and bread's serving is 1 loaf/meal
            //     (HouseholdInventory.cs:70), so the real meals eat exactly
            //     2 loaves/day while stock stays in [2, 18). No consumption
            //     rate is invented: 14 bought - 2 eaten = 12 on hand, and
            //     the monitor re-opens 2u the next day.
            //   - Why lever (a) limited funds CANNOT drive this repeat:
            //     it only downsizes the day-0 purchase, the monitor then
            //     re-sizes the still-open need to the full shortfall the
            //     next day (HouseholdShortage.cs:261 overwrites
            //     UnitsNeeded), no income arrives in the fixture, so later
            //     days would fail on cash — and the 3-day demand stays
            //     14 + 2 + 2 = 18 loaves regardless, which exceeds the
            //     fixture store's 16. The store, not cash, is the binding
            //     constraint, so the seed (lever b) is the supported lever:
            //     day 0 buys 12u (60c), days 1-2 buy 2u (10c each),
            //     16 loaves / 80c total, store exactly emptied.
            //   - Nothing else is invented: need sizes (12, 2, 2) come
            //     from the monitor formula, eaten amounts (2/day) from the
            //     real meal policy, prices from the authored 5c/loaf.
            Fixture f = Fixture.CreateSingle(female: false);
            f.Fund(10000);
            string seedProblem = f.Inventories.GetOrCreate(f.HouseholdId).AddLot(
                HouseholdItemCatalog.BreadId, 2, 0, "test seed: 2u bread on hand (S6-5 lever b)");
            Assert.IsNull(seedProblem, seedProblem);
            Assert.AreEqual(2, f.BreadUnits(), "the seeded household opens with 2 loaves");

            HouseholdReserveState reserve = f.Reserve();
            Assert.IsNotNull(reserve, "the household must hold a staple_food reserve");
            int reserveTarget = reserve.targetUnits;
            Assert.Greater(reserveTarget, 0, "the staple_food target must be computed from the catalog, not assumed");
            // Read, not assumed: the seed lot is added directly to
            // inventory, so the reserve starts wherever the receipt mirror
            // left it (0 unless a receipt credited it) — the per-day
            // formula below is computed from this read value.
            int expectedReserve = reserve.currentUnits;

            int totalCashBefore = f.Port.CashBalance + f.Balance();
            int totalBreadBefore = f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId) + f.BreadUnits();
            Assert.AreEqual(50000, f.Port.CashBalance, "the fixture store opens with 50000c");
            Assert.AreEqual(16, f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId), "the fixture store opens with 16 loaves");
            Assert.AreEqual(60000, totalCashBefore, "50000c store + 10000c household");
            Assert.AreEqual(18, totalBreadBefore, "16 store loaves + 2 seeded loaves");

            int[] expectedUnits = { 12, 2, 2 };
            var needSequences = new List<int>();
            var receiptIds = new List<string>();
            var planIds = new List<string>();
            int totalEaten = 0;
            int totalBought = 0;
            int totalSpent = 0;

            for (int day = 0; day <= 2; day++)
            {
                f.Day = day;
                f.Budgets.EnsureDay(day);
                int proposedBefore = CountDiagnosticLines(f.Diagnostics, "proposed for H7");
                int completedBefore = CountDiagnosticLines(f.Diagnostics, "completed — ");
                int transitionsBefore = f.Transitions.Count;
                int storeCashBefore = f.Port.CashBalance;
                int storeStockBefore = f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId);
                int balanceBefore = f.Balance();
                int stockBefore = f.BreadUnits();

                // The need re-opens as stock is eaten: the monitor sizes
                // it to the shortfall (14 - stock on hand). Raising it
                // here is idempotent — DriveProductionDay re-evaluates the
                // same stock and reaches the same size — and it captures
                // the need record (sequence, size) before the trip
                // fulfills it.
                HouseholdPurchasingNeed need = f.RaiseBreadNeedViaMonitor();
                Assert.IsNotNull(need, "day " + day + ": the need must re-open as stock is eaten");
                Assert.AreEqual(expectedUnits[day], need.UnitsNeeded,
                    "day " + day + ": the re-opened need must size to the shortfall (stock was " + stockBefore + ")");
                Assert.AreEqual(14 - stockBefore, need.UnitsNeeded,
                    "day " + day + ": the need must equal 14 - stock on hand by the monitor formula");
                Assert.AreEqual(PurchasingNeedStatus.Open, need.Status, "day " + day + ": the need must be open before the trip");
                Assert.AreEqual(1, f.OpenNeedCount(HouseholdItemCatalog.BreadId),
                    "day " + day + ": one open need per item at a time");
                int sequence = need.NeedSequence;
                Assert.IsTrue(!needSequences.Contains(sequence),
                    "day " + day + ": the re-opened need must be a NEW need record, not the fulfilled one re-bought");
                needSequences.Add(sequence);

                WorkTask work = f.CreateWorkTask(1, 240);
                var workDiag = new List<string>();
                int started;
                HouseholdShoppingLoop.BatchShoppingResult batch = f.DriveProductionDay(
                    new List<WorkTask> { work }, new List<int> { 1 }, new List<int> { 240 },
                    out started, workDiag);
                Assert.IsNotNull(batch, "day " + day + ": a plan must be created for the open bread need");
                Assert.IsTrue(batch.TripExecuted, "day " + day + ": the trip must repeat: " + batch.FailureReason);
                Assert.AreEqual(1, started,
                    "day " + day + ": the work task must start (refusals: " + string.Join(" | ", workDiag) + ")");
                Assert.AreEqual(TaskStatus.InProgress, work.Status, "day " + day + ": the work task must be started, not merely assigned");

                // The trip window: departs 720, [720,815], no overlap
                // with the shopper's own Work [480,720] (S6-2 lesson: the
                // window is asserted only against the shopper's own Work
                // reservation; per-member overlap is pinned by
                // ActiveReservationsOverlap). Exactly one shopping
                // reservation is created today, and it is released on
                // completion — no leaked reservation.
                PersonActivityReservation shopping = f.FindReservation(1, PersonActivityKind.Shopping);
                PersonActivityReservation workRes = f.FindReservation(1, PersonActivityKind.Work);
                Assert.IsNotNull(shopping, "day " + day + ": the trip must have held a shopping reservation");
                Assert.IsNotNull(workRes, "day " + day + ": the work task must hold a Work reservation");
                Assert.AreEqual(720, shopping.StartMinuteOfDay,
                    "day " + day + ": the trip must depart at 720; window was [" +
                    shopping.StartMinuteOfDay + "-" + shopping.EndMinuteOfDay + "]");
                Assert.AreEqual(815, shopping.EndMinuteOfDay, "day " + day + ": the 95-min trip from 720 ends at 815");
                Assert.IsTrue(!Overlaps(shopping, workRes),
                    "day " + day + ": the trip window must not overlap the Work reservation");
                Assert.IsTrue(!f.ActiveReservationsOverlap(1),
                    "day " + day + ": the person must hold no overlapping reservations");
                Assert.AreEqual(1, f.ShoppingReservationCount(1),
                    "day " + day + ": exactly one shopping reservation may be created");
                Assert.IsTrue(shopping.Released,
                    "day " + day + ": the shopping reservation must be released on completion");

                // Travels from the real location: home -> store -> home,
                // from the SetPersonLocation WRITES the legs perform
                // (S6-1 lesson), sliced to this day's transitions.
                var hops = new List<string>();
                for (int i = transitionsBefore; i < f.Transitions.Count; i++)
                {
                    if (f.Transitions[i].PersonId == 1) hops.Add(f.Transitions[i].LocationId);
                }

                Assert.AreEqual(2, hops.Count, "day " + day + ": the shopper must move exactly twice: to the store, then home");
                Assert.AreEqual("store", hops[0], "day " + day + ": first move is home -> store");
                Assert.AreEqual("home", hops[1], "day " + day + ": second move is store -> home");

                // The purchase settles when needed: exact price, receipt
                // id, a distinct receipt each day, no partial fill.
                ShoppingTripResult breadResult = FindNeedResult(batch, HouseholdItemCatalog.BreadId);
                Assert.IsNotNull(breadResult, "day " + day + ": the batch must carry its own bread result");
                Assert.IsTrue(breadResult.Success, breadResult.FailureReason);
                Assert.AreEqual(expectedUnits[day], breadResult.UnitsAcquired,
                    "day " + day + ": the trip must acquire the full re-opened need");
                Assert.AreEqual(5 * expectedUnits[day], breadResult.BuyerOutflowCents,
                    "day " + day + ": " + expectedUnits[day] + " loaves x 5c");
                Assert.IsTrue(!string.IsNullOrEmpty(breadResult.ReceiptId), "day " + day + ": the settlement must record a receipt id");
                Assert.IsTrue(!receiptIds.Contains(breadResult.ReceiptId),
                    "day " + day + ": each day's purchase must carry a distinct receipt");
                receiptIds.Add(breadResult.ReceiptId);
                Assert.IsTrue(string.IsNullOrEmpty(breadResult.PartialFillReason),
                    "day " + day + ": a fully fulfilled trip carries no partial-fill reason");
                Assert.AreEqual(0, breadResult.StoreReceivableCents, "day " + day + ": cash settlement creates no receivable");
                Assert.AreEqual(breadResult.BuyerOutflowCents, breadResult.StoreCashInCents,
                    "day " + day + ": buyer outflow must equal store cash in");

                // The fulfilled need is never re-bought.
                Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status, "day " + day + ": the need must be fulfilled by the trip");
                Assert.AreEqual(0, need.UnitsNeeded, "day " + day + ": the fulfilled need must have nothing left");
                Assert.IsNull(f.BreadNeed(), "day " + day + ": no open bread need may remain after the trip");
                Assert.AreEqual(0, f.OpenNeedCount(HouseholdItemCatalog.BreadId),
                    "day " + day + ": no open bread need may remain after the trip");

                // Cash and inventory reconcile for the day; totals
                // conserved with consumption accounted.
                Assert.AreEqual(storeCashBefore + 5 * expectedUnits[day], f.Port.CashBalance,
                    "day " + day + ": seller cash must gain exactly the day's outflow");
                Assert.AreEqual(storeStockBefore - expectedUnits[day], f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId),
                    "day " + day + ": seller stock must fall by exactly the loaves sold");
                Assert.AreEqual(balanceBefore - 5 * expectedUnits[day], f.Balance(),
                    "day " + day + ": buyer debit must equal the day's outflow");
                Assert.AreEqual(totalCashBefore, f.Port.CashBalance + f.Balance(),
                    "day " + day + ": cash must move, not be created or destroyed");
                Assert.AreEqual(totalBreadBefore,
                    f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId) + f.BreadUnits() + totalEaten,
                    "day " + day + ": bread must be conserved with consumption accounted");
                totalBought += expectedUnits[day];
                totalSpent += 5 * expectedUnits[day];

                // The reserve mirror credits the receipt (clamped to
                // target), as in S6-1.
                expectedReserve = Math.Min(reserveTarget, expectedReserve + expectedUnits[day]);
                Assert.AreEqual(expectedReserve, reserve.currentUnits,
                    "day " + day + ": the receipt must credit the reserve by the units received (clamped to target)");

                // No duplicate plan/claim records: exactly one plan is
                // PROPOSED today (one coordinator "proposed" diagnostic)
                // and exactly one COMPLETES (one "completed" diagnostic),
                // the batch carries that plan's id for this day, no active
                // plan remains, and the served need's claim is released; a
                // re-plan the same day forms nothing and moves no stock.
                // NOTE (verified in source, HouseholdErrandCoordinator.cs:
                // 569-580): NoteTerminal REMOVES a terminal plan record
                // from the registry list and drops its claims entry, so
                // plan creation is pinned here via the batch PlanId + the
                // coordinator's proposed/completed diagnostics, NOT via
                // Plans.All.Count — which reads 0 after every completed
                // trip, terminal plans included.
                Assert.AreEqual(proposedBefore + 1, CountDiagnosticLines(f.Diagnostics, "proposed for H7"),
                    "day " + day + ": exactly one plan may be proposed");
                Assert.AreEqual(completedBefore + 1, CountDiagnosticLines(f.Diagnostics, "completed — "),
                    "day " + day + ": exactly one plan may complete");
                Assert.IsTrue(!string.IsNullOrEmpty(batch.PlanId), "day " + day + ": the batch must carry its plan id");
                Assert.IsTrue(batch.PlanId.StartsWith("errand-7-" + day + "-", StringComparison.Ordinal),
                    "day " + day + ": the plan id must belong to this day, got: " + batch.PlanId);
                Assert.IsTrue(!planIds.Contains(batch.PlanId),
                    "day " + day + ": each day's plan must be a distinct plan record");
                planIds.Add(batch.PlanId);
                Assert.AreEqual(0, f.Coordinator.Plans.ActiveFor(f.HouseholdId).Count,
                    "day " + day + ": no active plan may remain after execution");
                Assert.IsTrue(!f.Coordinator.Plans.IsNeedClaimed(f.HouseholdId, sequence),
                    "day " + day + ": the served need's claim must be released");
                List<HouseholdErrandPlan> replans = f.Coordinator.PlanDay(f.HouseholdId, f.Day, f.Context(), f.Diagnostics);
                Assert.AreEqual(0, replans.Count,
                    "day " + day + ": no open need remains, so no second plan — and no second sale — may form");
                Assert.AreEqual(storeStockBefore - expectedUnits[day], f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId),
                    "day " + day + ": the store stock must be untouched by the re-plan");

                // Consumption: close the day's work at day end via
                // CompleteTask (inside DriveNextDayMeal) and serve the
                // next day's meals from the real inventory. The real meals
                // eat exactly 2 loaves (Morning + Evening, 1 loaf each),
                // which is what re-opens the 2u need tomorrow.
                IReadOnlyList<HouseholdMealRecord> meals = f.DriveNextDayMeal(work);
                int eaten = 0;
                foreach (HouseholdMealRecord record in meals)
                {
                    if (record == null || record.ItemsConsumed == null) continue;
                    foreach (MealItemConsumption consumed in record.ItemsConsumed)
                    {
                        if (consumed != null
                            && string.Equals(consumed.ItemId, HouseholdItemCatalog.BreadId, StringComparison.Ordinal))
                        {
                            eaten += consumed.QuantityUnits;
                        }
                    }
                }

                Assert.AreEqual(2, eaten,
                    "day " + (day + 1) + ": the next day's meals must eat exactly 2 loaves (Morning + Evening, 1 loaf each)");
                totalEaten += eaten;
                Assert.AreEqual(12, f.BreadUnits(),
                    "day " + (day + 1) + ": 14 loaves after the purchase minus 2 eaten");
                expectedReserve = Math.Max(0, expectedReserve - eaten);
                Assert.AreEqual(expectedReserve, reserve.currentUnits,
                    "day " + (day + 1) + ": meal consumption must decrement the reserve by the loaves eaten");
            }

            // Registry across all 3 days: exactly one bread need per day —
            // the fulfilled needs stay as history, never re-bought, and no
            // duplicate open need ever forms for the same item.
            var allNeeds = new List<HouseholdPurchasingNeed>();
            f.Needs.ExportState(allNeeds);
            int breadNeedCount = 0;
            foreach (HouseholdPurchasingNeed n in allNeeds)
            {
                if (n != null && n.HouseholdId == f.HouseholdId
                    && string.Equals(n.ItemId, HouseholdItemCatalog.BreadId, StringComparison.Ordinal))
                {
                    breadNeedCount++;
                }
            }

            Assert.AreEqual(3, breadNeedCount, "exactly 3 bread needs (one per day), not a growing pile of duplicates");
            Assert.AreEqual(3, new HashSet<int>(needSequences).Count, "each day's need must be a distinct need record");
            Assert.AreEqual(3, new HashSet<string>(receiptIds).Count, "each day's purchase must carry a distinct receipt");
            Assert.AreEqual(3, new HashSet<string>(planIds).Count, "each day's plan must be a distinct plan record");
            Assert.AreEqual(3, CountDiagnosticLines(f.Diagnostics, "proposed for H7"), "exactly 3 plans proposed (one per day)");
            Assert.AreEqual(3, CountDiagnosticLines(f.Diagnostics, "completed — "), "exactly 3 plans completed (one per day)");
            Assert.AreEqual(0, f.Coordinator.Plans.ActiveFor(f.HouseholdId).Count, "no active plan may remain after day 3");
            Assert.AreEqual(16, totalBought, "12 + 2 + 2 loaves bought over three days");
            Assert.AreEqual(80, totalSpent, "16 loaves x 5c");
            Assert.AreEqual(10000 - 80, f.Balance(), "the buyer must be debited exactly 80c over three days");
            Assert.AreEqual(0, f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId),
                "the store must be exactly emptied by the three purchases");
            Assert.AreEqual(50000 + 80, f.Port.CashBalance, "the seller must gain exactly 80c");
            Assert.AreEqual(totalCashBefore, f.Port.CashBalance + f.Balance(), "cash must stay conserved across all 3 days");
            Assert.AreEqual(totalBreadBefore,
                f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId) + f.BreadUnits() + totalEaten,
                "bread must stay conserved across all 3 days (0 in store + 12 at home + 6 eaten = 18)");
            Assert.AreEqual(6, totalEaten, "2 loaves eaten on each of the 3 meal days");
            Assert.AreEqual(12, f.BreadUnits(), "the household must hold 12 loaves after the day-3 meals");
            Assert.AreEqual(0, f.OpenNeedCount(HouseholdItemCatalog.BreadId), "no open bread need may remain");
        }

        [Test]
        public void Acceptance_SummaryInputs_LongestOpenAgeAndReason()
        {
            // Plan 05 Slice 6 (S6-5), M10: summary inputs across the
            // repeat. The three facts the daily summary line prints —
            // longest-open identity (CreatedDayIndex), age in days, first
            // failure reason — pinned through the real loop/monitor/
            // registry on each day of a three-consecutive-trading-days
            // repeat (days 0-2), extending Slice 5's day-1 pin
            // (LongestOpenNeed_NamedInSummary). The summary line itself
            // lives in DriveEmbodiedShopping (SimulationDrivers.cs:
            // 7936-8008, harness-excluded), so this test pins its INPUTS
            // only; the line is source-review + Unity-owed.
            //
            // WHY THE FAILING LEG: the summary's longest-open + first-
            // failure inputs only exist when a need stays OPEN after an
            // attempted day — the summary reads the first failure recorded
            // today per need, or "no attempt today" when the need was never
            // attempted (SimulationDrivers.cs:7976-8001). On the M9 buying
            // leg every need is fulfilled the same day, so there is no
            // longest-open need and no failure reason to pin. M10 therefore
            // drives the same three-day repeat structure on the failing
            // leg — the 600-min full-coverage shift, as in M8 — where the
            // longest-open need fails with the search's budget text every
            // day. What is NEW versus M8 (a single need): TWO open needs
            // (bread day 0, flour day 1), so this test pins longest-open
            // SELECTION by CreatedDayIndex across the repeat, not just the
            // age and reason of the only need.
            //
            // THE REASON DISTINCTION (SimulationDrivers.cs:7955-7968
            // comment): a budget-blocked trip carries the SEARCH budget
            // text ("only N work minutes left, trip needs M",
            // PersonScheduleTracker.cs:762, inside "No shopper in any
            // searched window"), a different branch from the
            // CASH-affordability 'budget short'
            // (HouseholdShoppingLoop.cs:2241). The longest-open need's
            // per-need reason must carry the former and NOT the latter.
            // Each day's work is closed at day end via CompleteTask so the
            // next day's work can start (Slice 5 field correction).
            Fixture f = Fixture.CreateSingle(female: false);
            f.Port.OfferList.Add(new GeneralStoreCategoryOffer
            {
                CategoryId = "staple_food",
                OfferedItemIds = new List<string> { HouseholdItemCatalog.FlourId },
                StoreUnitsPerItemUnit = 1,
                HouseholdUnitsPerStoreUnit = 10,
                UsesItemLevelInventory = true,
            });
            f.Port.ItemLevelItems.Add(HouseholdItemCatalog.FlourId);
            f.Port.ItemStock[HouseholdItemCatalog.FlourId] = 5;
            f.Port.ItemPrices[HouseholdItemCatalog.FlourId] = 35;
            f.Fund(10000);

            int firstBreadSequence = -1;
            int firstFlourSequence = -1;
            int trips = 0;
            int shoppingReservationsEver = 0;

            for (int day = 0; day <= 2; day++)
            {
                f.Day = day;
                f.Budgets.EnsureDay(day);
                if (day == 1)
                {
                    // The second need opens on day 1 (hand-created — the
                    // test says so; the monitor is not asked to raise it),
                    // as in Slice 5's LongestOpenNeed_NamedInSummary.
                    HouseholdPurchasingNeed flour = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
                    Assert.AreEqual(1, flour.CreatedDayIndex, "day 1: flour opens on day 1");
                    firstFlourSequence = flour.NeedSequence;
                }

                WorkTask work = f.CreateWorkTask(1, 600);
                var workDiag = new List<string>();
                int started;
                HouseholdShoppingLoop.BatchShoppingResult batch = f.DriveProductionDay(
                    new List<WorkTask> { work }, new List<int> { 1 }, new List<int> { 600 },
                    out started, workDiag);
                Assert.AreEqual(1, started,
                    "day " + day + ": the 600-min work task must start (refusals: " + string.Join(" | ", workDiag) + ")");
                Assert.IsNotNull(batch, "day " + day + ": a plan must be created for the open needs");
                Assert.IsTrue(!batch.TripExecuted, "day " + day + ": no trip may execute over a full-coverage shift");
                if (batch.TripExecuted) trips++;
                shoppingReservationsEver += f.ShoppingReservationCount(1);

                // The batch carries one per-need result per open need —
                // the same per-need reasons the summary's first-failure map
                // reads (one attempt per need per day, so the per-need
                // reason IS the first failure reason).
                if (day == 0)
                {
                    Assert.AreEqual(1, batch.NeedResults.Count, "day 0: only the bread need is open");
                }
                else
                {
                    Assert.AreEqual(2, batch.NeedResults.Count,
                        "day " + day + ": both open needs must carry per-need results");
                }

                // Longest-open identity + age, by creation day: bread
                // (day 0) predates flour (day 1), so on day d bread is d
                // days old.
                HouseholdPurchasingNeed bread = f.BreadNeed();
                Assert.IsNotNull(bread, "day " + day + ": the bread need must stay open");
                Assert.AreEqual(PurchasingNeedStatus.Open, bread.Status, "day " + day + ": the bread need must stay open");
                if (firstBreadSequence < 0) firstBreadSequence = bread.NeedSequence;
                Assert.AreEqual(firstBreadSequence, bread.NeedSequence,
                    "day " + day + ": the same single bread need must persist, not a fresh duplicate");
                Assert.AreEqual(0, bread.CreatedDayIndex,
                    "day " + day + ": the longest-open need was created on day 0");
                Assert.AreEqual(day, f.Day - bread.CreatedDayIndex,
                    "day " + day + ": the longest-open need must be " + day + " day(s) old");
                Assert.AreEqual(1, f.OpenNeedCount(HouseholdItemCatalog.BreadId),
                    "day " + day + ": exactly one open bread need may exist");

                HouseholdPurchasingNeed flourNeed = f.Needs.GetOpenNeed(f.HouseholdId, HouseholdItemCatalog.FlourId);
                if (day >= 1)
                {
                    Assert.IsNotNull(flourNeed, "day " + day + ": the flour need must stay open");
                    Assert.AreEqual(firstFlourSequence, flourNeed.NeedSequence,
                        "day " + day + ": the same single flour need must persist, not a fresh duplicate");
                    Assert.AreEqual(1, flourNeed.CreatedDayIndex,
                        "day " + day + ": flour was created on day 1, after bread");
                    Assert.IsTrue(bread.CreatedDayIndex < flourNeed.CreatedDayIndex,
                        "day " + day + ": bread (created day " + bread.CreatedDayIndex +
                        ") must be the longest-open need, ahead of flour (created day " + flourNeed.CreatedDayIndex + ")");
                    Assert.AreEqual(1, f.OpenNeedCount(HouseholdItemCatalog.FlourId),
                        "day " + day + ": exactly one open flour need may exist");
                }

                // The literal plan key cannot resolve this on day 1: the
                // day-1 monitor evaluation refreshed bread to today, tying
                // flour exactly — CreatedDayIndex is what resolves it.
                if (day == 1)
                {
                    Assert.AreEqual(1, bread.LastEvaluatedDayIndex,
                        "day 1: the monitor refreshed bread's LastEvaluatedDayIndex to today");
                    Assert.AreEqual(bread.LastEvaluatedDayIndex, flourNeed.LastEvaluatedDayIndex,
                        "LastEvaluatedDayIndex ties at day 1 for both needs — it cannot identify the longest-open need; CreatedDayIndex does");
                }

                if (day == 2)
                {
                    // Flour was never re-evaluated (the monitor only
                    // evaluates bread; the loop refreshes
                    // LastEvaluatedDayIndex only on receipt,
                    // HouseholdShoppingLoop.cs:420,:1729,:1794), so its
                    // last-evaluated day stays 1 while bread's moves to 2.
                    // The longest-open key is still CreatedDayIndex, not
                    // this field.
                    Assert.AreEqual(2, bread.LastEvaluatedDayIndex,
                        "day 2: the monitor refreshed bread's LastEvaluatedDayIndex to today");
                    Assert.AreEqual(1, flourNeed.LastEvaluatedDayIndex,
                        "day 2: flour was never re-evaluated, so its LastEvaluatedDayIndex stays 1");
                }

                // First failure reason for the longest-open need: the
                // search's budget text — and NOT the cash-affordability
                // text.
                ShoppingTripResult breadResult = FindNeedResult(batch, HouseholdItemCatalog.BreadId);
                Assert.IsNotNull(breadResult, "day " + day + ": the bread need must carry its own per-need result");
                Assert.IsTrue(breadResult.FailureReason.Contains("No shopper in any searched window"),
                    "day " + day + ": the longest-open need's reason must name the searched window, got: " + breadResult.FailureReason);
                Assert.IsTrue(breadResult.FailureReason.Contains("work minutes left"),
                    "day " + day + ": the longest-open need's reason must name the search's budget shortfall, got: " + breadResult.FailureReason);
                Assert.IsTrue(!breadResult.FailureReason.Contains("budget short"),
                    "day " + day + ": the longest-open need's reason must NOT carry the cash-affordability 'budget short' text, got: " + breadResult.FailureReason);
                Assert.AreEqual(0, f.ShoppingReservationCount(1),
                    "day " + day + ": no shopping reservation may ever be created");

                if (day < 2)
                {
                    Assert.IsTrue(f.Tasks.CompleteTask(work.TaskId, TaskOutcome.Success, 0, f.Budgets),
                        "day " + day + "'s work must complete at day end");
                }
            }

            Assert.AreEqual(0, trips, "zero trips may execute over the three days");
            Assert.AreEqual(0, shoppingReservationsEver, "zero shopping reservations may ever be created");
            Assert.AreEqual(1, f.OpenNeedCount(HouseholdItemCatalog.BreadId), "exactly one open bread need after day 2");
            Assert.AreEqual(1, f.OpenNeedCount(HouseholdItemCatalog.FlourId), "exactly one open flour need after day 2");
            HouseholdPurchasingNeed finalBread = f.BreadNeed();
            Assert.IsNotNull(finalBread, "the bread need must still be open after day 2");
            Assert.AreEqual(0, finalBread.CreatedDayIndex, "the longest-open need was created on day 0");
            Assert.AreEqual(2, f.Day - finalBread.CreatedDayIndex, "by day 2 the longest-open need is 2 days old");
            Assert.AreEqual(10000, f.Balance(), "no money may move over the three days");
            Assert.AreEqual(16, f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId), "no bread stock may move over the three days");
            Assert.AreEqual(5, f.Port.ItemStockUnits(HouseholdItemCatalog.FlourId), "no flour stock may move over the three days");
        }

        private static int CountDiagnosticLines(List<string> diagnostics, string substring)
        {
            if (diagnostics == null || string.IsNullOrEmpty(substring)) return 0;
            int count = 0;
            foreach (string line in diagnostics)
            {
                if (!string.IsNullOrEmpty(line) && line.Contains(substring)) count++;
            }

            return count;
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
    }
}
