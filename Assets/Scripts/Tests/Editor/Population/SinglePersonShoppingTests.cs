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
            public FakeStorePort Port;
            public Dictionary<int, string> PersonLocations = new Dictionary<int, string>();
            public List<string> ResolvedLocations = new List<string>();
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
            /// Returns tasks started; refusal reasons land in diagnostics.</summary>
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

            /// <summary>Drives one day in production order: planner, monitor,
            /// coordinator plan + execute, then assigned-work start. Returns
            /// the batch (null when no plan was created).</summary>
            public HouseholdShoppingLoop.BatchShoppingResult DriveProductionDay(
                List<WorkTask> workTasks, List<int> personIds, List<int> minutesList,
                out int started, List<string> workDiagnostics)
            {
                PlanDay();
                HouseholdPurchasingNeed need = RaiseBreadNeedViaMonitor();
                Assert.IsNotNull(need, "the monitor must open the bread need before shopping runs");
                List<HouseholdErrandPlan> plans = Coordinator.PlanDay(HouseholdId, Day, Context(), Diagnostics);
                HouseholdShoppingLoop.BatchShoppingResult batch = null;
                if (plans.Count > 0)
                {
                    batch = Coordinator.ExecutePlan(plans[0], Day, Context(), Loop, Options(), Diagnostics);
                }

                started = 0;
                if (workTasks != null && workTasks.Count > 0)
                {
                    started = StartWork(workTasks, personIds, minutesList, workDiagnostics);
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
            Assert.IsTrue(batch.TripExecuted, "the trip executes at HEAD: " + batch.FailureReason);
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
            Assert.IsTrue(batch.TripExecuted, "the trip executes at HEAD: " + batch.FailureReason);
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
            Assert.IsTrue(batch.TripExecuted, "the trip executes at HEAD: " + batch.FailureReason);
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
            // 1 day of supply (threshold 7) and a bread control, NO
            // recognition predicate (the method takes none today). Desired:
            // no preserved_meat need is created; the bread need IS created,
            // exactly once, and a second call creates no duplicate. At HEAD
            // the meat need is created. (Slice 1 adds the optional predicate
            // and this test is then wired to supply it; as written here it
            // can only pass once that seam exists.)
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
                    { HouseholdItemCatalog.PreservedMeatId, 1f },
                    { HouseholdItemCatalog.BreadId, 1f },
                },
            };

            AutonomousWorkGenerator.GenerationReport report = generator.GenerateForHousehold(inputs, diag);

            Assert.IsTrue(needs.GetOpenNeed(7, HouseholdItemCatalog.PreservedMeatId) == null,
                "HEAD FAILURE: the generator opened an unservable preserved_meat need — no seller " +
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

        private static bool Overlaps(PersonActivityReservation a, PersonActivityReservation b)
        {
            if (a == null || b == null) return false;
            return a.StartMinuteOfDay < b.EndMinuteOfDay && b.StartMinuteOfDay < a.EndMinuteOfDay;
        }
    }
}
