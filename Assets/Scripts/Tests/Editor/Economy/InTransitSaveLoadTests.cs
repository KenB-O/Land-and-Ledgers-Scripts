using System;
using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Labor;
using LandLedgers.Economy.Logistics;
using LandLedgers.Economy.Storage;
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
    /// Priority U-3 — in-transit shopping batches do not survive save/load.
    /// F5 pinned the defect on BOTH paths: the immediate path keeps the batch
    /// only in the loop's private custodyBatches with no save surface, and
    /// the real-time driver's DTO round-trips the batch scalars but nothing
    /// calls it. This file first reproduces the defect precisely (a fresh
    /// loop — what a load constructs — holds no batch), then, after the
    /// repair, proves the batch survives: save while a Person holds
    /// purchased goods, load into a fresh state, Continue with the goods
    /// still in personal custody, deposit after Continue exactly once, and
    /// repeated Continue without duplication — driven identically for a
    /// player person id and an NPC person id (acceptance B).
    ///
    /// The driving fixture is the V1 S6-1 shape (single employed adult,
    /// 240-min shift 480-720, store stocked 16 loaves at 5c, funded 10000c,
    /// monitor-raised 14-loaf bread need), plus a SimpleDueEventScheduler
    /// and a ShoppingTripDriver so the SAME fixture drives the immediate
    /// path (coordinator ExecutePlan) and the real-time path (driver legs).
    /// A mid-trip save is captured inside the MidTripObserver, which the
    /// loop invokes inside a try/catch that swallows observer faults — so
    /// the observer only CAPTURES snapshots into locals and every assertion
    /// runs after the drive returns.
    ///
    /// Save/load is driven at authority level (SaveLoadManager is
    /// harness-excluded): the loop custody DTO, the driver DTO, and the
    /// inventories / ledgers / needs Export/Import round-trips — the same
    /// authorities F5 used. The store runtime is NOT part of this slice's
    /// save surfaces, so restored store stock is never asserted; the
    /// original world's store movement (16 -&gt; 2) is asserted pre-save
    /// instead (acceptance R).
    /// </summary>
    [TestFixture]
    public sealed class InTransitSaveLoadTests
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
            public ShoppingTripDriver Driver;
            public SimpleDueEventScheduler DueEvents;
            public FakeStorePort Port;
            public Dictionary<int, string> PersonLocations = new Dictionary<int, string>();
            public List<string> ResolvedLocations = new List<string>();
            public List<LocationHop> Transitions = new List<LocationHop>();
            public List<string> Diagnostics = new List<string>();
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
                DueEvents = new SimpleDueEventScheduler();

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
                Driver = new ShoppingTripDriver(Loop);
                Coordinator = new HouseholdErrandCoordinator();
                Planner = new DailyActivityPlanner();
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
                    OffMapJourneys = null,
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

            public int BreadUnits() =>
                Inventories.GetOrCreate(HouseholdId).GetAvailableUnits(HouseholdItemCatalog.BreadId);

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

            /// <summary>Opens a bread need directly (driver path: no monitor, no work).</summary>
            public HouseholdPurchasingNeed OpenBreadNeed(int units = 14)
            {
                HouseholdPurchasingNeed need = Needs.CreateNeed(HouseholdId, HouseholdItemCatalog.BreadId, Day);
                need.UnitsNeeded = units;
                need.IsNecessity = true;
                return need;
            }

            public void DrainDue(float now)
            {
                foreach (DueEvent ev in DueEvents.PopDue(now))
                {
                    Driver.HandleDueEvent(ev, Day, ev.DueGameSeconds, DueEvents, Diagnostics);
                }
            }

            /// <summary>
            /// Drives the real-time driver until the trip has settled at the
            /// counter and reached the in-store leg (batch in hand), or the
            /// tick cap is hit. Returns the settled trip, or null.
            /// </summary>
            public PlannedShoppingTrip DriveDriverUntilSettled()
            {
                float start = Day * 86400f;
                for (int t = 0; t < 400; t++)
                {
                    float now = start + t * 300f;
                    Driver.AdvanceTick(now, Day, DueEvents, Diagnostics);
                    DrainDue(now);
                    if (Driver.ActiveTrips.Count > 0)
                    {
                        PlannedShoppingTrip trip = Driver.ActiveTrips[0];
                        if (trip != null && trip.Batch != null
                            && trip.State == PlannedShoppingTrip.TripState.InStore)
                        {
                            return trip;
                        }
                    }
                }

                return null;
            }

            /// <summary>Runs a restored driver to completion (or the tick cap).</summary>
            public void RunDriverUntilDone()
            {
                float start = Day * 86400f;
                for (int t = 0; t < 2000 && Driver.ActiveTrips.Count > 0; t++)
                {
                    float now = start + t * 300f;
                    Driver.AdvanceTick(now, Day, DueEvents, Diagnostics);
                    DrainDue(now);
                }

                for (int i = 0; i < 50 && (DueEvents.ScheduledCount > 0 || Driver.ActiveTrips.Count > 0); i++)
                {
                    DrainDue(start + 3 * 86400f);
                }
            }
        }

        private sealed class LocationHop
        {
            public int PersonId;
            public string LocationId;
        }

        /// <summary>
        /// A mid-trip snapshot. Captured ONLY — never asserted — inside the
        /// MidTripObserver, which the loop invokes inside a try/catch that
        /// swallows observer faults.
        /// </summary>
        private sealed class MidTripSnapshot
        {
            public bool Observed;
            public string ObserverFault;
            public string BatchId = string.Empty;
            public string ItemId = string.Empty;
            public int Units;
            public int UnitPriceCents;
            public string ReceiptId = string.Empty;
            public string Provenance = string.Empty;
            public GoodsCustodyState Custody;
            public int HolderPersonId = -1;
            public int HouseholdId = -1;
            public int PurchaseDayIndex;
            public int LiveCustodyCount;
            public int HouseholdBreadUnits;
            public int StoreStock;
            public int Balance;
        }

        private static MidTripSnapshot CaptureMidTrip(Fixture f, ShoppingTripResult result, CarriedGoodsBatch batch)
        {
            var snap = new MidTripSnapshot { Observed = true };
            if (batch != null)
            {
                snap.BatchId = batch.BatchId ?? string.Empty;
                snap.ItemId = batch.ItemId ?? string.Empty;
                snap.Units = batch.Units;
                snap.UnitPriceCents = batch.UnitPriceCents;
                snap.ReceiptId = batch.ReceiptId ?? string.Empty;
                snap.Provenance = batch.Provenance ?? string.Empty;
                snap.Custody = batch.Custody;
                snap.HolderPersonId = batch.HolderPersonId;
                snap.HouseholdId = batch.HouseholdId;
                snap.PurchaseDayIndex = batch.PurchaseDayIndex;
            }

            snap.LiveCustodyCount = f.Loop.ActiveCustodyBatches.Count;
            snap.HouseholdBreadUnits = f.BreadUnits();
            snap.StoreStock = f.Port.ItemStockUnits(HouseholdItemCatalog.BreadId);
            snap.Balance = f.Balance();
            return snap;
        }

        [Test]
        public void ImmediatePath_MidTripSaveLoad_DropsCarriedBatch_Reproduction_Player()
        {
            ImmediatePath_MidTripSaveLoad_DropsCarriedBatch_Reproduction(true, 101);
        }

        [Test]
        public void ImmediatePath_MidTripSaveLoad_DropsCarriedBatch_Reproduction_Npc()
        {
            ImmediatePath_MidTripSaveLoad_DropsCarriedBatch_Reproduction(false, 202);
        }

        /// <summary>
        /// STEP 1 reproduction (U-3, both paths' shared fact): mid-trip the
        /// purchased goods exist ONLY in the shopping loop's custody list —
        /// the store stock is already consumed, the cash already moved, the
        /// household inventory still empty. A fresh loop that LOADS NO
        /// custody DTO — what an unrestored load constructs — still holds
        /// no batch: load is explicit, never magic. (Pre-repair this test
        /// also pinned that the loop declared no save surface at all; the
        /// repair added CaptureCustodySaveDto / LoadCustodyFromSaveDto,
        /// and survival through that surface is proven in the restore
        /// tests below.)
        /// </summary>
        private void ImmediatePath_MidTripSaveLoad_DropsCarriedBatch_Reproduction(bool female, int personId)
        {
            Fixture f = Fixture.CreateSingle(female, personId);
            f.Fund(10000);
            MidTripSnapshot midTrip = null;
            f.Loop.MidTripObserver = (result, batch) =>
            {
                try
                {
                    midTrip = CaptureMidTrip(f, result, batch);
                }
                catch (Exception ex)
                {
                    midTrip = new MidTripSnapshot { Observed = true, ObserverFault = ex.ToString() };
                }
            };

            WorkTask work = f.CreateWorkTask(personId, 240);
            var workDiag = new List<string>();
            int started;
            HouseholdShoppingLoop.BatchShoppingResult batch = f.DriveProductionDay(
                new List<WorkTask> { work }, new List<int> { personId }, new List<int> { 240 },
                out started, workDiag);

            Assert.IsNotNull(midTrip, "the mid-trip observer must have fired");
            Assert.IsNull(midTrip.ObserverFault, "the mid-trip observer must not fault: " + midTrip.ObserverFault);
            Assert.IsNotNull(batch, "a plan must be created for the open bread need");
            Assert.IsTrue(batch.TripExecuted, "the trip must execute: " + batch.FailureReason);
            Assert.AreEqual(1, midTrip.LiveCustodyCount, "mid-trip exactly one batch is in transit custody");
            Assert.AreEqual(HouseholdItemCatalog.BreadId, midTrip.ItemId, "the carried item is bread");
            Assert.AreEqual(14, midTrip.Units, "the person carries the full 14-loaf need");
            Assert.AreEqual(GoodsCustodyState.InHand, midTrip.Custody, "the batch is InHand, not yet received");
            Assert.AreEqual(personId, midTrip.HolderPersonId, "custody holder is the shopper");
            Assert.AreEqual(0, midTrip.HouseholdBreadUnits, "no teleport: the household inventory is still empty mid-trip");
            Assert.AreEqual(2, midTrip.StoreStock, "the store stock is already consumed mid-trip (16 - 14)");
            Assert.AreEqual(10000 - 70, midTrip.Balance, "the 70c purchase already settled mid-trip (R)");

            // The load simulation: a fresh loop over the same authorities.
            var freshLoop = new HouseholdShoppingLoop(
                f.Population, f.Membership, f.Ledgers, f.Inventories, f.Needs,
                f.Directory, f.Journeys, f.Tasks, f.Budgets, f.Tracker, f.Knowledge,
                new FinancialObligationAuthority(), f.EntityIds);
            Assert.AreEqual(0, freshLoop.ActiveCustodyBatches.Count,
                "U-3 REPRODUCTION: a fresh loop that loads no custody DTO holds no batch — load is explicit, never magic");
            Assert.AreEqual(0, freshLoop.PersonInventory.CarriedItems(personId).Count,
                "U-3 REPRODUCTION: the fresh loop's person inventory read model reports nothing carried");
            AssertSaveSurface(typeof(HouseholdShoppingLoop), "HouseholdShoppingLoop",
                "CaptureCustodySaveDto", "LoadCustodyFromSaveDto");

            Assert.AreEqual(14, f.BreadUnits(),
                "the live trip still completes: the household holds the 14 loaves, so the loss is a save/load artifact, not a trip failure");
        }

        [Test]
        public void DriverPath_MidTripSaveLoad_RestoresCarriedBatch_Player()
        {
            DriverPath_MidTripSaveLoad_RestoresCarriedBatch(true, 101);
        }

        [Test]
        public void DriverPath_MidTripSaveLoad_RestoresCarriedBatch_Npc()
        {
            DriverPath_MidTripSaveLoad_RestoresCarriedBatch(false, 202);
        }

        /// <summary>
        /// Real-time path restore (U-3 repair): the driver's DTO
        /// round-trips the batch onto the restored trip AND the driver
        /// re-attaches it to the loop-owned custody, so the loop's person
        /// inventory read model — the production custody answer — reports
        /// the carried batch and the trip's batch IS the loop's batch (one
        /// object, never two copies). Pre-repair this test FAILED in
        /// exactly the way F5 predicted: the read model reported nothing
        /// carried (0 vs 1), because the restored trip batch was never
        /// registered in loop custody.
        /// </summary>
        private void DriverPath_MidTripSaveLoad_RestoresCarriedBatch(bool female, int personId)
        {
            Fixture f = Fixture.CreateSingle(female, personId);
            f.Fund(10000);
            HouseholdPurchasingNeed need = f.OpenBreadNeed(14);
            f.Driver.PlanForHousehold(f.HouseholdId, f.Needs, f.Day, f.Options(), f.Diagnostics);
            Assert.AreEqual(1, f.Driver.ActiveTrips.Count, "one trip must be planned");

            PlannedShoppingTrip settled = f.DriveDriverUntilSettled();
            Assert.IsNotNull(settled, "the driver must settle at the counter: " + string.Join(" | ", f.Diagnostics));
            Assert.IsNotNull(settled.Batch, "the settled trip must carry a batch");
            Assert.AreEqual(14, settled.Batch.Units, "the settled batch holds the 14 loaves");
            Assert.AreEqual(1, f.Loop.PersonInventory.CarriedItems(personId).Count,
                "before save the loop's read model reports the carried batch");

            // Save: the driver DTO only (the only trip save surface F5 found).
            List<PlannedShoppingTripSaveDto> driverDto = f.Driver.CaptureSaveDto();
            var tasksDto = f.Tasks.CaptureSaveDto();
            var budgetsDto = f.Budgets.CaptureSaveDto();
            var schedulesDto = f.Tracker.CaptureSaveDto();
            var dueDto = f.DueEvents.CaptureSaveDto();
            Assert.AreEqual(1, driverDto.Count, "one active trip must be captured");

            // Load into a fresh fixture: fresh authorities, same scenario wiring.
            Fixture g = Fixture.CreateSingle(female, personId);
            g.Tasks.LoadFromSaveDto(tasksDto);
            g.Budgets.LoadFromSaveDto(budgetsDto, g.Day);
            g.Tracker.LoadFromSaveDto(schedulesDto);
            g.Driver.LoadFromSaveDto(driverDto);
            g.DueEvents.LoadFromSaveDto(dueDto);
            g.Driver.RehydrateAfterLoad(g.Tasks,
                id => FindSupplierByBusinessId(g, id),
                g.Day, g.Day * 86400f + 610 * 60f, g.DueEvents, g.Diagnostics);
            Assert.AreEqual(1, g.Driver.ActiveTrips.Count, "one trip must restore");
            PlannedShoppingTrip restored = g.Driver.ActiveTrips[0];
            Assert.IsNotNull(restored.Batch, "the restored trip must carry its batch (F5 proved the DTO mapping works)");
            Assert.AreEqual(14, restored.Batch.Units, "the restored trip batch holds the 14 loaves");
            Assert.AreEqual(1, g.Loop.PersonInventory.CarriedItems(personId).Count,
                "U-3 (driver path): after load the loop's read model must report the carried batch");
            Assert.IsTrue(ReferenceEquals(restored.Batch, g.Loop.ActiveCustodyBatches[0]),
                "the restored trip batch must BE the loop-owned custody batch — one batch, not two copies");
        }

        private static IGoodsSupplier FindSupplierByBusinessId(Fixture f, string businessId)
        {
            foreach (IGoodsSupplier supplier in f.Directory.All)
            {
                if (supplier != null && string.Equals(supplier.SupplierBusinessId, businessId, StringComparison.Ordinal))
                {
                    return supplier;
                }
            }

            return null;
        }

        // ---------- STEP 3: repair regression tests ----------

        /// <summary>
        /// A mid-trip save snapshot for the immediate path. Captured inside
        /// the MidTripObserver: the custody DTO, a COPY of the open need,
        /// and the inventory / ledger state FROZEN into snapshot-owned
        /// registries. The freeze is load-bearing: ExportState hands out
        /// the LIVE state objects, and the live trip deposits the lot the
        /// moment the observer returns — importing later would deep-copy
        /// the already-deposited state. Importing inside the observer
        /// (the V1 pin pattern) deep-copies mid-trip, before the receipt.
        /// The need must be copied for the same reason:
        /// HouseholdNeedRegistry.ExportState shares the live need objects,
        /// and the live trip fulfils the need right after the observer —
        /// a real save serializes to fresh copies, so the snapshot does
        /// the same.
        /// </summary>
        private sealed class ImmediateSnapshot
        {
            public MidTripSnapshot MidTrip;
            public HouseholdShoppingCustodySaveDto CustodyDto;
            public HouseholdInventoryRegistry FrozenInventories;
            public HouseholdLedgerRegistry FrozenLedgers;
            public HouseholdPurchasingNeed NeedCopy;
            public string ObserverFault;
        }

        private sealed class RestoredImmediate
        {
            public Fixture Fixture;
            public HouseholdPurchasingNeed Need;
            public CustodyTransferLog CustodyLog;
        }

        private static HouseholdPurchasingNeed CopyNeed(HouseholdPurchasingNeed need)
        {
            if (need == null) return null;
            return new HouseholdPurchasingNeed
            {
                NeedSequence = need.NeedSequence,
                HouseholdId = need.HouseholdId,
                ItemId = need.ItemId,
                UnitsNeeded = need.UnitsNeeded,
                IsNecessity = need.IsNecessity,
                Urgency01 = need.Urgency01,
                ReasonSummary = need.ReasonSummary ?? string.Empty,
                CreatedDayIndex = need.CreatedDayIndex,
                LastEvaluatedDayIndex = need.LastEvaluatedDayIndex,
                Status = need.Status,
                SuggestedResponses = need.SuggestedResponses != null
                    ? new List<string>(need.SuggestedResponses) : new List<string>(),
            };
        }

        /// <summary>Freezes inventory / ledger state into a snapshot-owned
        /// pair of registries (the import deep-copies at this instant, so
        /// later live mutations cannot contaminate the snapshot).</summary>
        private static ImmediateSnapshot FreezeImmediate(MidTripSnapshot midTrip,
            HouseholdShoppingCustodySaveDto custodyDto, HouseholdPurchasingNeed needCopy,
            HouseholdInventoryRegistry inventories, HouseholdLedgerRegistry ledgers)
        {
            var inventoryStates = new List<HouseholdInventoryState>();
            var ledgerStates = new List<HouseholdLedgerState>();
            inventories.ExportState(inventoryStates);
            ledgers.ExportState(ledgerStates);
            var frozenInventories = new HouseholdInventoryRegistry();
            frozenInventories.ImportState(inventoryStates);
            var frozenLedgers = new HouseholdLedgerRegistry();
            frozenLedgers.ImportState(ledgerStates);
            return new ImmediateSnapshot
            {
                MidTrip = midTrip,
                CustodyDto = custodyDto,
                FrozenInventories = frozenInventories,
                FrozenLedgers = frozenLedgers,
                NeedCopy = needCopy,
            };
        }

        private static RestoredImmediate RestoreImmediate(ImmediateSnapshot snap, bool female, int personId)
        {
            Fixture g = Fixture.CreateSingle(female, personId);
            var inventoryStates = new List<HouseholdInventoryState>();
            var ledgerStates = new List<HouseholdLedgerState>();
            snap.FrozenInventories.ExportState(inventoryStates);
            snap.FrozenLedgers.ExportState(ledgerStates);
            g.Inventories.ImportState(inventoryStates);
            g.Ledgers.ImportState(ledgerStates);
            HouseholdPurchasingNeed needCopy = CopyNeed(snap.NeedCopy);
            g.Needs.ImportState(new List<HouseholdPurchasingNeed> { needCopy });
            g.Loop.LoadCustodyFromSaveDto(snap.CustodyDto);
            var log = new CustodyTransferLog();
            g.Loop.CustodyLog = log;
            return new RestoredImmediate { Fixture = g, Need = needCopy, CustodyLog = log };
        }

        /// <summary>A mid-trip save snapshot for the real-time path. Captured
        /// AFTER the driver reaches the settled in-store state (the trip
        /// object is only fully updated once ArriveAtStoreAndSettle returns —
        /// the observer fires mid-settle, before trip.Batch is assigned).
        /// The original fixture is idle after the snapshot, so exporting
        /// its authorities then is a clean frozen snapshot. The need travels
        /// in the driver DTO; it is NOT imported into the fresh needs
        /// registry.</summary>
        private sealed class DriverSnapshot
        {
            public List<PlannedShoppingTripSaveDto> DriverDto;
            public HouseholdShoppingCustodySaveDto CustodyDto;
            public List<HouseholdInventoryState> InventoryStates = new List<HouseholdInventoryState>();
            public List<HouseholdLedgerState> LedgerStates = new List<HouseholdLedgerState>();
            public TaskAuthoritySaveDto TasksDto;
            public WorkTimeBudgetSaveDto BudgetsDto;
            public PersonScheduleTrackerSaveDto SchedulesDto;
            public SimpleDueEventSchedulerSaveDto DueDto;
        }

        private sealed class RestoredDriver
        {
            public Fixture Fixture;
            public PlannedShoppingTrip Trip;
            public CustodyTransferLog CustodyLog;
        }

        private static RestoredDriver RestoreDriver(DriverSnapshot snap, bool female, int personId, bool driverFirst)
        {
            Fixture g = Fixture.CreateSingle(female, personId);
            g.Inventories.ImportState(snap.InventoryStates);
            g.Ledgers.ImportState(snap.LedgerStates);
            g.Tasks.LoadFromSaveDto(snap.TasksDto);
            g.Budgets.LoadFromSaveDto(snap.BudgetsDto, g.Day);
            g.Tracker.LoadFromSaveDto(snap.SchedulesDto);
            if (driverFirst)
            {
                g.Driver.LoadFromSaveDto(snap.DriverDto);
                g.Loop.LoadCustodyFromSaveDto(snap.CustodyDto);
            }
            else
            {
                g.Loop.LoadCustodyFromSaveDto(snap.CustodyDto);
                g.Driver.LoadFromSaveDto(snap.DriverDto);
            }

            g.DueEvents.LoadFromSaveDto(snap.DueDto);
            g.Driver.RehydrateAfterLoad(g.Tasks,
                id => FindSupplierByBusinessId(g, id),
                g.Day, g.Day * 86400f + 610 * 60f, g.DueEvents, g.Diagnostics);
            var log = new CustodyTransferLog();
            g.Loop.CustodyLog = log;
            PlannedShoppingTrip trip = g.Driver.ActiveTrips.Count > 0 ? g.Driver.ActiveTrips[0] : null;
            return new RestoredDriver { Fixture = g, Trip = trip, CustodyLog = log };
        }

        private static int CountLogRecords(CustodyTransferLog log, string causeKind, string causeId)
        {
            int count = 0;
            if (log == null || log.Records == null) return 0;
            foreach (CustodyTransferRecord record in log.Records)
            {
                if (record != null && string.Equals(record.CauseKind, causeKind, StringComparison.Ordinal)
                    && (causeId == null || string.Equals(record.CauseId, causeId, StringComparison.Ordinal)))
                {
                    count++;
                }
            }

            return count;
        }

        private static void AssertCarriedBatch(PersonInventoryReadModel.CarriedItem carried,
            MidTripSnapshot expected, int personId, string label)
        {
            Assert.AreEqual(HouseholdItemCatalog.BreadId, carried.ItemId, label + ": the carried item is bread");
            Assert.AreEqual(14, carried.Units, label + ": the carried batch holds the 14 loaves");
            Assert.AreEqual(expected.BatchId, carried.BatchId, label + ": the batch id survives");
            Assert.AreEqual(expected.ReceiptId, carried.ReceiptId, label + ": the receipt id survives");
            Assert.AreEqual(expected.Provenance, carried.Provenance, label + ": the provenance survives");
            Assert.AreEqual(5, carried.UnitPriceCents, label + ": the 5c unit price survives");
            Assert.AreEqual(GoodsCustodyState.InHand, carried.Custody, label + ": the batch is InHand");
            Assert.AreEqual(personId, carried.HolderPersonId, label + ": the holder is the shopper");
            Assert.AreEqual(PersonItemCondition.Unknown, carried.Condition,
                label + ": the authority-carried condition is Unknown before and after save — never a default");
        }

        private static void AssertDepositedOnce(Fixture g, string batchId, string receiptId,
            int personId, CustodyTransferLog log, string label)
        {
            Assert.AreEqual(14, g.BreadUnits(), label + ": the household holds the 14 loaves after deposit");
            List<HouseholdItemLot> lots = g.BreadLots();
            Assert.AreEqual(1, lots.Count, label + ": the household holds exactly one bread lot — deposited exactly once");
            HouseholdItemLot lot = lots[0];
            Assert.AreEqual(14, lot.QuantityUnits, label + ": the lot holds the 14 loaves");
            Assert.IsTrue(lot.SourceLabel.Contains(receiptId),
                label + ": the lot provenance carries the receipt id, got: " + lot.SourceLabel);
            Assert.AreEqual(-1, lot.OwnerPersonId, label + ": the lot is household-owned, not person-owned");
            Assert.AreEqual(string.Empty, lot.QualityBand,
                label + ": the lot QualityBand facet survives unchanged (empty in, empty out — no invented condition)");
            Assert.AreEqual(0, g.Loop.PersonInventory.CarriedItems(personId).Count,
                label + ": after deposit the person carries nothing");
            Assert.AreEqual(0, g.Loop.ActiveCustodyBatches.Count,
                label + ": no custody batch is left stranded after deposit");
            Assert.AreEqual(10000 - 70, g.Balance(),
                label + ": the balance is still 9930 — Continue charges nothing again (R)");

            // Acceptance E: the deposited lot resolves through the F4
            // resolver with its canonical provenance key.
            string expectedKey = LotIdentity.CanonicalProvenanceKey(
                CustodyTransferLog.HouseholdInventoryAuthority, lot.LotId.ToString());
            Assert.IsTrue(lot.SourceLabel.Contains(expectedKey),
                label + ": the lot SourceLabel carries its canonical key, got: " + lot.SourceLabel);
            var resolver = new LotIdentityResolver(g.Inventories);
            LotIdentity identity = resolver.Resolve(new LotKey(
                CustodyTransferLog.HouseholdInventoryAuthority, lot.LotId.ToString()));
            Assert.IsNotNull(identity, label + ": the F4 resolver must resolve the deposited lot");
            Assert.AreEqual(HouseholdItemCatalog.BreadId, identity.ItemId, label + ": the resolved lot is bread");
            Assert.AreEqual(14, identity.Units, label + ": the resolved lot holds 14 loaves");
            Assert.AreEqual(expectedKey, identity.ProvenanceKey, label + ": the resolved provenance key is the lot's key");

            // The trip result of the depositing receipt names the lot.
            ShoppingTripResult depositResult = null;
            foreach (ShoppingTripResult candidate in g.Loop.TripHistory)
            {
                if (candidate != null && candidate.ReceivedLotId == lot.LotId)
                {
                    depositResult = candidate;
                }
            }

            Assert.IsNotNull(depositResult, label + ": a trip result must name the deposited lot id");
            Assert.AreEqual(expectedKey, depositResult.ReceivedLotKey,
                label + ": the deposit result must name the deposited lot's canonical key");

            // No duplicated log records: exactly one household-receipt
            // record for this batch, and no retail-purchase record in the
            // restored world — the purchase happened before the save and
            // must not be re-logged (or re-charged) by the Continue.
            Assert.AreEqual(1, CountLogRecords(log, CustodyTransferLog.CauseHouseholdReceipt, batchId),
                label + ": exactly one household-receipt log record for the batch");
            Assert.AreEqual(0, CountLogRecords(log, CustodyTransferLog.CauseRetailPurchase, null),
                label + ": no retail-purchase log record in the restored world");
        }

        [Test]
        public void ImmediatePath_MidTripSaveLoad_RestoresCarriedBatch_Player()
        {
            ImmediatePath_MidTripSaveLoad_RestoresCarriedBatch(true, 101);
        }

        [Test]
        public void ImmediatePath_MidTripSaveLoad_RestoresCarriedBatch_Npc()
        {
            ImmediatePath_MidTripSaveLoad_RestoresCarriedBatch(false, 202);
        }

        /// <summary>
        /// (a) Save while a Person holds purchased goods (immediate path):
        /// the restored loop reports the carried batch through the Slice 3
        /// read model with the same item, quantity, lot identity
        /// (batch + receipt ids), provenance, and condition — and the
        /// household still holds nothing (no teleport, no double count).
        /// Acceptance Q: a full inspection sweep after load mutates nothing.
        /// </summary>
        private void ImmediatePath_MidTripSaveLoad_RestoresCarriedBatch(bool female, int personId)
        {
            Fixture f = Fixture.CreateSingle(female, personId);
            f.Fund(10000);
            var snap = new ImmediateSnapshot();
            f.Loop.MidTripObserver = (result, batch) =>
            {
                try
                {
                    snap.MidTrip = CaptureMidTrip(f, result, batch);
                    snap.CustodyDto = f.Loop.CaptureCustodySaveDto();
                    // Freeze the inventory / ledger state NOW, inside the
                    // observer: ExportState aliases the live state objects
                    // and the receipt lands the moment this returns.
                    var inventoryStates = new List<HouseholdInventoryState>();
                    var ledgerStates = new List<HouseholdLedgerState>();
                    f.Inventories.ExportState(inventoryStates);
                    f.Ledgers.ExportState(ledgerStates);
                    snap.FrozenInventories = new HouseholdInventoryRegistry();
                    snap.FrozenInventories.ImportState(inventoryStates);
                    snap.FrozenLedgers = new HouseholdLedgerRegistry();
                    snap.FrozenLedgers.ImportState(ledgerStates);
                    snap.NeedCopy = CopyNeed(f.Needs.GetOpenNeed(f.HouseholdId, HouseholdItemCatalog.BreadId));
                }
                catch (Exception ex)
                {
                    snap.ObserverFault = ex.ToString();
                }
            };

            WorkTask work = f.CreateWorkTask(personId, 240);
            var workDiag = new List<string>();
            int started;
            HouseholdShoppingLoop.BatchShoppingResult batch = f.DriveProductionDay(
                new List<WorkTask> { work }, new List<int> { personId }, new List<int> { 240 },
                out started, workDiag);

            Assert.IsNull(snap.ObserverFault, "the mid-trip snapshot must not fault: " + snap.ObserverFault);
            Assert.IsNotNull(snap.MidTrip, "the mid-trip observer must have fired");
            Assert.IsNotNull(batch, "a plan must be created for the open bread need");
            Assert.IsTrue(batch.TripExecuted, "the trip must execute: " + batch.FailureReason);
            Assert.IsNotNull(snap.CustodyDto, "the custody DTO must have been captured mid-trip");
            Assert.AreEqual(1, snap.CustodyDto.Batches.Count, "the captured custody holds exactly one batch");
            Assert.IsNotNull(snap.NeedCopy, "the open need must have been snapshotted mid-trip");
            Assert.AreEqual(14, snap.NeedCopy.UnitsNeeded, "the snapshotted need still wants the 14 loaves");
            Assert.AreEqual(PurchasingNeedStatus.Open, snap.NeedCopy.Status, "the snapshotted need is still open");

            RestoredImmediate restored = RestoreImmediate(snap, female, personId);
            Fixture g = restored.Fixture;

            Assert.AreEqual(1, g.Loop.ActiveCustodyBatches.Count, "after load the loop holds exactly one custody batch");
            IReadOnlyList<PersonInventoryReadModel.CarriedItem> carried = g.Loop.PersonInventory.CarriedItems(personId);
            Assert.AreEqual(1, carried.Count, "after load the read model reports exactly one carried batch");
            AssertCarriedBatch(carried[0], snap.MidTrip, personId, "restored immediate");
            Assert.AreEqual(36, g.Loop.PersonInventory.RemainingCarryUnits(personId).UnitsRemaining,
                "after load 50 - 14 carried leaves 36 units of carry cap");
            Assert.AreEqual(0, g.BreadUnits(), "after load the household still holds none of the bread — no teleport");
            Assert.AreEqual(14, carried[0].Units + g.BreadUnits(),
                "carried plus household equals the 14 purchased loaves after load (C/K)");
            Assert.AreEqual(10000 - 70, g.Balance(), "after load the balance is the settled 9930c (R)");
            Assert.IsNotNull(g.Needs.GetOpenNeed(g.HouseholdId, HouseholdItemCatalog.BreadId),
                "after load the need is still open — the receipt has not run yet");
            Assert.AreEqual(0, restored.CustodyLog.Count, "after load the fresh custody log is empty (runtime-only by design, F5)");

            // Capture must copy, not alias: mutating the captured DTO
            // must not mutate the live loop's batch.
            HouseholdShoppingCustodySaveDto second = g.Loop.CaptureCustodySaveDto();
            Assert.AreEqual(1, second.Batches.Count, "a second capture holds the one batch");
            Assert.AreEqual(snap.MidTrip.BatchId, second.Batches[0].BatchId, "the second capture carries the same batch id");
            second.Batches[0].Units = 999;
            Assert.AreEqual(14, g.Loop.ActiveCustodyBatches[0].Units,
                "capture must copy, not alias: mutating a captured DTO must not change the live batch");

            // Acceptance Q: a full inspection sweep after load changes
            // nothing — lot counts, bread units, balance, and the carried
            // batch are identical before and after.
            int lotCountBefore = g.Inventories.GetOrCreate(g.HouseholdId).Lots.Count;
            int breadBefore = g.BreadUnits();
            int balanceBefore = g.Balance();
            int custodyBefore = g.Loop.ActiveCustodyBatches.Count;
            g.Loop.PersonInventory.CarriedItems(personId);
            g.Loop.PersonInventory.CarriedLoad(personId);
            g.Loop.PersonInventory.RemainingCarryUnits(personId);
            g.Loop.PersonInventory.HouseholdLotsOwnedBy(personId);
            var resolver = new LotIdentityResolver(g.Inventories);
            IReadOnlyList<LotIdentity> allFirst = resolver.ResolveAll(HouseholdItemCatalog.BreadId);
            IReadOnlyList<LotIdentity> allSecond = resolver.ResolveAll(HouseholdItemCatalog.BreadId);
            Assert.AreEqual(allFirst.Count, allSecond.Count, "two resolver sweeps must agree");
            Assert.AreEqual(0, allFirst.Count, "the resolver finds no bread lot yet — the bread is carried, not stored");
            Assert.AreEqual(lotCountBefore, g.Inventories.GetOrCreate(g.HouseholdId).Lots.Count, "a query sweep must not create lots (Q)");
            Assert.AreEqual(breadBefore, g.BreadUnits(), "a query sweep must not change bread units (Q)");
            Assert.AreEqual(balanceBefore, g.Balance(), "a query sweep must not move cash (Q)");
            Assert.AreEqual(custodyBefore, g.Loop.ActiveCustodyBatches.Count, "a query sweep must not change custody (Q)");
        }

        [Test]
        public void ImmediatePath_ContinueAfterLoad_DepositsExactlyOnce_Player()
        {
            ImmediatePath_ContinueAfterLoad_DepositsExactlyOnce(true, 101);
        }

        [Test]
        public void ImmediatePath_ContinueAfterLoad_DepositsExactlyOnce_Npc()
        {
            ImmediatePath_ContinueAfterLoad_DepositsExactlyOnce(false, 202);
        }

        /// <summary>
        /// (b) + (c) + (e), immediate path: Continue with the goods still in
        /// personal custody, then deposit after Continue — the loop's
        /// receipt path deposits the restored batch into household storage
        /// exactly once, the Person no longer carries it, quantity / lot
        /// identity / provenance / condition are preserved, and the balance
        /// never moves again (acceptance L: save/restore creates no
        /// additional goods; R: 14 x 5c = 70c).
        /// </summary>
        private void ImmediatePath_ContinueAfterLoad_DepositsExactlyOnce(bool female, int personId)
        {
            Fixture f = Fixture.CreateSingle(female, personId);
            f.Fund(10000);
            var snap = new ImmediateSnapshot();
            f.Loop.MidTripObserver = (result, batch) =>
            {
                try
                {
                    snap.MidTrip = CaptureMidTrip(f, result, batch);
                    snap.CustodyDto = f.Loop.CaptureCustodySaveDto();
                    // Freeze the inventory / ledger state NOW, inside the
                    // observer: ExportState aliases the live state objects
                    // and the receipt lands the moment this returns.
                    var inventoryStates = new List<HouseholdInventoryState>();
                    var ledgerStates = new List<HouseholdLedgerState>();
                    f.Inventories.ExportState(inventoryStates);
                    f.Ledgers.ExportState(ledgerStates);
                    snap.FrozenInventories = new HouseholdInventoryRegistry();
                    snap.FrozenInventories.ImportState(inventoryStates);
                    snap.FrozenLedgers = new HouseholdLedgerRegistry();
                    snap.FrozenLedgers.ImportState(ledgerStates);
                    snap.NeedCopy = CopyNeed(f.Needs.GetOpenNeed(f.HouseholdId, HouseholdItemCatalog.BreadId));
                }
                catch (Exception ex)
                {
                    snap.ObserverFault = ex.ToString();
                }
            };

            WorkTask work = f.CreateWorkTask(personId, 240);
            var workDiag = new List<string>();
            int started;
            f.DriveProductionDay(
                new List<WorkTask> { work }, new List<int> { personId }, new List<int> { 240 },
                out started, workDiag);
            Assert.IsNull(snap.ObserverFault, "the mid-trip snapshot must not fault: " + snap.ObserverFault);
            Assert.IsNotNull(snap.MidTrip, "the mid-trip observer must have fired");

            RestoredImmediate restored = RestoreImmediate(snap, female, personId);
            Fixture g = restored.Fixture;

            // (b) Continue with the goods still in personal custody.
            Assert.AreEqual(1, g.Loop.PersonInventory.CarriedItems(personId).Count,
                "Continue starts with the goods still in personal custody");
            AssertCarriedBatch(g.Loop.PersonInventory.CarriedItems(personId)[0], snap.MidTrip, personId, "continue");

            // (c) Deposit after Continue through the loop's receipt path.
            string error = g.Loop.ReceiveCarriedBatch(snap.MidTrip.BatchId, g.Day, g.Diagnostics);
            Assert.IsNull(error, "the Continue deposit must succeed: " + error);
            AssertDepositedOnce(g, snap.MidTrip.BatchId, snap.MidTrip.ReceiptId, personId, restored.CustodyLog, "continue deposit");
            Assert.IsNull(g.Needs.GetOpenNeed(g.HouseholdId, HouseholdItemCatalog.BreadId),
                "the need must be fulfilled by the Continue deposit");
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, restored.Need.Status,
                "the restored need must be fulfilled by the Continue deposit");
            Assert.AreEqual(16, g.Port.ItemStockUnits(HouseholdItemCatalog.BreadId),
                "Continue must not re-settle against the fresh store — its stock stays 16 (no second purchase)");

            // A second deposit of the same batch is refused, and nothing moves.
            string secondError = g.Loop.ReceiveCarriedBatch(snap.MidTrip.BatchId, g.Day, g.Diagnostics);
            Assert.IsTrue(!string.IsNullOrEmpty(secondError), "a second deposit of the same batch must be refused");
            Assert.AreEqual(14, g.BreadUnits(), "the refused second deposit must not change the bread");
            Assert.AreEqual(1, g.BreadLots().Count, "the refused second deposit must not duplicate the lot");
            Assert.AreEqual(1, CountLogRecords(restored.CustodyLog, CustodyTransferLog.CauseHouseholdReceipt, snap.MidTrip.BatchId),
                "the refused second deposit must not duplicate the receipt log record");
        }

        [Test]
        public void ImmediatePath_RepeatedSaveLoad_NoDuplication_Player()
        {
            ImmediatePath_RepeatedSaveLoad_NoDuplication(true, 101);
        }

        [Test]
        public void ImmediatePath_RepeatedSaveLoad_NoDuplication_Npc()
        {
            ImmediatePath_RepeatedSaveLoad_NoDuplication(false, 202);
        }

        /// <summary>
        /// (d), immediate path: save and load twice in a row without
        /// depositing in between — quantity, lot identity (batch + receipt
        /// ids), and count are identical after each load, no duplicated
        /// lots, no duplicated log records — then one deposit lands the
        /// 14 loaves exactly once, and a third save/load after the deposit
        /// keeps the 14 loaves with an empty custody.
        /// </summary>
        private void ImmediatePath_RepeatedSaveLoad_NoDuplication(bool female, int personId)
        {
            Fixture f = Fixture.CreateSingle(female, personId);
            f.Fund(10000);
            var snap = new ImmediateSnapshot();
            f.Loop.MidTripObserver = (result, batch) =>
            {
                try
                {
                    snap.MidTrip = CaptureMidTrip(f, result, batch);
                    snap.CustodyDto = f.Loop.CaptureCustodySaveDto();
                    // Freeze the inventory / ledger state NOW, inside the
                    // observer: ExportState aliases the live state objects
                    // and the receipt lands the moment this returns.
                    var inventoryStates = new List<HouseholdInventoryState>();
                    var ledgerStates = new List<HouseholdLedgerState>();
                    f.Inventories.ExportState(inventoryStates);
                    f.Ledgers.ExportState(ledgerStates);
                    snap.FrozenInventories = new HouseholdInventoryRegistry();
                    snap.FrozenInventories.ImportState(inventoryStates);
                    snap.FrozenLedgers = new HouseholdLedgerRegistry();
                    snap.FrozenLedgers.ImportState(ledgerStates);
                    snap.NeedCopy = CopyNeed(f.Needs.GetOpenNeed(f.HouseholdId, HouseholdItemCatalog.BreadId));
                }
                catch (Exception ex)
                {
                    snap.ObserverFault = ex.ToString();
                }
            };

            WorkTask work = f.CreateWorkTask(personId, 240);
            var workDiag = new List<string>();
            int started;
            f.DriveProductionDay(
                new List<WorkTask> { work }, new List<int> { personId }, new List<int> { 240 },
                out started, workDiag);
            Assert.IsNull(snap.ObserverFault, "the mid-trip snapshot must not fault: " + snap.ObserverFault);
            Assert.IsNotNull(snap.MidTrip, "the mid-trip observer must have fired");

            // Load #1.
            RestoredImmediate first = RestoreImmediate(snap, female, personId);
            Fixture g = first.Fixture;
            Assert.AreEqual(1, g.Loop.ActiveCustodyBatches.Count, "load #1: exactly one custody batch");
            AssertCarriedBatch(g.Loop.PersonInventory.CarriedItems(personId)[0], snap.MidTrip, personId, "load #1");
            Assert.AreEqual(0, g.BreadUnits(), "load #1: household still holds no bread");
            Assert.AreEqual(10000 - 70, g.Balance(), "load #1: balance is the settled 9930c");

            // Save #2 from the restored state, load #2 into a third world.
            ImmediateSnapshot snap2 = FreezeImmediate(snap.MidTrip, g.Loop.CaptureCustodySaveDto(),
                CopyNeed(g.Needs.GetOpenNeed(g.HouseholdId, HouseholdItemCatalog.BreadId)),
                g.Inventories, g.Ledgers);
            RestoredImmediate second = RestoreImmediate(snap2, female, personId);
            Fixture h = second.Fixture;
            Assert.AreEqual(1, h.Loop.ActiveCustodyBatches.Count, "load #2: exactly one custody batch — no duplication");
            AssertCarriedBatch(h.Loop.PersonInventory.CarriedItems(personId)[0], snap.MidTrip, personId, "load #2");
            Assert.AreEqual(0, h.BreadUnits(), "load #2: household still holds no bread");
            Assert.AreEqual(10000 - 70, h.Balance(), "load #2: balance is still the settled 9930c");

            // One deposit in the twice-restored world.
            string error = h.Loop.ReceiveCarriedBatch(snap.MidTrip.BatchId, h.Day, h.Diagnostics);
            Assert.IsNull(error, "the deposit after two loads must succeed: " + error);
            AssertDepositedOnce(h, snap.MidTrip.BatchId, snap.MidTrip.ReceiptId, personId, second.CustodyLog, "deposit after two loads");

            // Save #3 after the deposit, load #3: the 14 loaves persist
            // in storage, custody is empty, and nothing duplicates.
            ImmediateSnapshot snap3 = FreezeImmediate(snap.MidTrip, h.Loop.CaptureCustodySaveDto(),
                CopyNeed(h.Needs.GetOpenNeed(h.HouseholdId, HouseholdItemCatalog.BreadId)),
                h.Inventories, h.Ledgers);
            Assert.AreEqual(0, snap3.CustodyDto.Batches.Count, "save #3: custody is empty after the deposit");
            RestoredImmediate third = RestoreImmediate(snap3, female, personId);
            Fixture k = third.Fixture;
            Assert.AreEqual(0, k.Loop.ActiveCustodyBatches.Count, "load #3: custody stays empty");
            Assert.AreEqual(14, k.BreadUnits(), "load #3: the household still holds the 14 loaves");
            Assert.AreEqual(1, k.BreadLots().Count, "load #3: exactly one bread lot — no duplication");
            Assert.AreEqual(10000 - 70, k.Balance(), "load #3: balance is still 9930c");
            string refused = k.Loop.ReceiveCarriedBatch(snap.MidTrip.BatchId, k.Day, k.Diagnostics);
            Assert.IsTrue(!string.IsNullOrEmpty(refused), "load #3: depositing the already-deposited batch must be refused");
            Assert.AreEqual(14, k.BreadUnits(), "load #3: the refused deposit changes nothing");
        }

        [Test]
        public void DriverPath_ContinueAfterLoad_DepositsExactlyOnce_Player()
        {
            DriverPath_ContinueAfterLoad_DepositsExactlyOnce(true, 101);
        }

        [Test]
        public void DriverPath_ContinueAfterLoad_DepositsExactlyOnce_Npc()
        {
            DriverPath_ContinueAfterLoad_DepositsExactlyOnce(false, 202);
        }

        /// <summary>
        /// (b) + (c) + (e), real-time path: save mid-trip after settle (the
        /// trip is in the in-store leg, the batch in hand), load both the
        /// loop custody DTO and the driver DTO, and Continue by driving
        /// the restored trip to completion through the real due events —
        /// the receipt deposits the restored batch into household storage
        /// exactly once, with the same quantity, lot identity, provenance,
        /// and condition, and no second charge (acceptance L, R).
        /// </summary>
        private void DriverPath_ContinueAfterLoad_DepositsExactlyOnce(bool female, int personId)
        {
            Fixture f = Fixture.CreateSingle(female, personId);
            f.Fund(10000);
            f.OpenBreadNeed(14);
            f.Driver.PlanForHousehold(f.HouseholdId, f.Needs, f.Day, f.Options(), f.Diagnostics);
            Assert.AreEqual(1, f.Driver.ActiveTrips.Count, "one trip must be planned");

            PlannedShoppingTrip settled = f.DriveDriverUntilSettled();
            Assert.IsNotNull(settled, "the driver must settle at the counter: " + string.Join(" | ", f.Diagnostics));
            Assert.IsNotNull(settled.Batch, "the settled trip must carry a batch");
            string batchId = settled.Batch.BatchId;
            string receiptId = settled.Batch.ReceiptId;

            var snap = new DriverSnapshot
            {
                DriverDto = f.Driver.CaptureSaveDto(),
                CustodyDto = f.Loop.CaptureCustodySaveDto(),
                TasksDto = f.Tasks.CaptureSaveDto(),
                BudgetsDto = f.Budgets.CaptureSaveDto(),
                SchedulesDto = f.Tracker.CaptureSaveDto(),
                DueDto = f.DueEvents.CaptureSaveDto(),
            };
            f.Inventories.ExportState(snap.InventoryStates);
            f.Ledgers.ExportState(snap.LedgerStates);
            Assert.AreEqual(1, snap.DriverDto.Count, "one active trip must be captured");
            Assert.AreEqual(1, snap.CustodyDto.Batches.Count, "one custody batch must be captured");
            Assert.AreEqual(batchId, snap.CustodyDto.Batches[0].BatchId, "the captured custody batch is the settled batch");
            Assert.AreEqual(0, f.BreadUnits(), "mid-trip the household holds no bread in the live world either");

            // Load both DTOs (loop custody first), rehydrate, Continue.
            RestoredDriver restored = RestoreDriver(snap, female, personId, driverFirst: false);
            Fixture g = restored.Fixture;
            Assert.IsNotNull(restored.Trip, "one trip must restore");
            Assert.IsNotNull(restored.Trip.Batch, "the restored trip must carry its batch");
            Assert.AreEqual(batchId, restored.Trip.Batch.BatchId, "the restored trip batch id survives");
            Assert.AreEqual(14, restored.Trip.Batch.Units, "the restored trip batch holds the 14 loaves");
            Assert.AreEqual(receiptId, restored.Trip.Batch.ReceiptId, "the restored trip receipt id survives");
            Assert.IsTrue(ReferenceEquals(restored.Trip.Batch, g.Loop.ActiveCustodyBatches[0]),
                "the restored trip batch must BE the loop-owned custody batch — one batch, not two copies");
            Assert.AreEqual(1, g.Loop.PersonInventory.CarriedItems(personId).Count,
                "Continue starts with the goods still in personal custody");
            Assert.AreEqual(0, g.BreadUnits(), "Continue starts with the household holding no bread");
            Assert.AreEqual(10000 - 70, g.Balance(), "Continue starts at the settled 9930c");
            Assert.AreEqual(0, restored.CustodyLog.Count, "the fresh custody log is empty before the receipt");

            g.RunDriverUntilDone();

            Assert.AreEqual(0, g.Driver.ActiveTrips.Count, "the restored trip must finish: " + string.Join(" | ", g.Diagnostics));
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, restored.Trip.Need.Status,
                "the restored need must be fulfilled by the Continue");
            AssertDepositedOnce(g, batchId, receiptId, personId, restored.CustodyLog, "driver continue deposit");
            Assert.AreEqual(16, g.Port.ItemStockUnits(HouseholdItemCatalog.BreadId),
                "Continue must not re-settle against the fresh store — its stock stays 16 (no second purchase)");
            // Note: the restored trip's Need is the same in-memory object
            // the driver DTO carried (a disk save would deserialize a
            // fresh copy), so no assertion is made here about the live
            // world's need object after the restored Continue.
        }

        [Test]
        public void DriverPath_RepeatedSaveLoad_NoDuplication_Player()
        {
            DriverPath_RepeatedSaveLoad_NoDuplication(true, 101);
        }

        [Test]
        public void DriverPath_RepeatedSaveLoad_NoDuplication_Npc()
        {
            DriverPath_RepeatedSaveLoad_NoDuplication(false, 202);
        }

        /// <summary>
        /// (d), real-time path: save and load twice in a row — the second
        /// load takes the DTOs in the REVERSE order (driver first, loop
        /// custody second) to prove the re-attach holds whichever half
        /// loads last — quantity, batch / receipt ids, and count are
        /// identical after each load, no duplicated trips or batches, and
        /// the eventual Continue deposits exactly once.
        /// </summary>
        private void DriverPath_RepeatedSaveLoad_NoDuplication(bool female, int personId)
        {
            Fixture f = Fixture.CreateSingle(female, personId);
            f.Fund(10000);
            f.OpenBreadNeed(14);
            f.Driver.PlanForHousehold(f.HouseholdId, f.Needs, f.Day, f.Options(), f.Diagnostics);
            PlannedShoppingTrip settled = f.DriveDriverUntilSettled();
            Assert.IsNotNull(settled, "the driver must settle at the counter: " + string.Join(" | ", f.Diagnostics));
            string batchId = settled.Batch.BatchId;
            string receiptId = settled.Batch.ReceiptId;

            var snap = new DriverSnapshot
            {
                DriverDto = f.Driver.CaptureSaveDto(),
                CustodyDto = f.Loop.CaptureCustodySaveDto(),
                TasksDto = f.Tasks.CaptureSaveDto(),
                BudgetsDto = f.Budgets.CaptureSaveDto(),
                SchedulesDto = f.Tracker.CaptureSaveDto(),
                DueDto = f.DueEvents.CaptureSaveDto(),
            };
            f.Inventories.ExportState(snap.InventoryStates);
            f.Ledgers.ExportState(snap.LedgerStates);

            // Load #1, driver DTO first (reverse order).
            RestoredDriver first = RestoreDriver(snap, female, personId, driverFirst: true);
            Fixture g = first.Fixture;
            Assert.IsNotNull(first.Trip, "load #1: one trip must restore");
            Assert.AreEqual(batchId, first.Trip.Batch.BatchId, "load #1: the batch id survives");
            Assert.AreEqual(14, first.Trip.Batch.Units, "load #1: the batch holds the 14 loaves");
            Assert.AreEqual(receiptId, first.Trip.Batch.ReceiptId, "load #1: the receipt id survives");
            Assert.AreEqual(1, g.Loop.ActiveCustodyBatches.Count, "load #1: exactly one custody batch");
            Assert.IsTrue(ReferenceEquals(first.Trip.Batch, g.Loop.ActiveCustodyBatches[0]),
                "load #1: the trip batch must BE the loop-owned batch even when the driver loads first");
            Assert.AreEqual(1, g.Loop.PersonInventory.CarriedItems(personId).Count, "load #1: the read model reports the batch");
            Assert.AreEqual(0, g.BreadUnits(), "load #1: household holds no bread yet");
            Assert.AreEqual(10000 - 70, g.Balance(), "load #1: balance is the settled 9930c");

            // Save #2 from the restored state, load #2 (loop custody first this time).
            var snap2 = new DriverSnapshot
            {
                DriverDto = g.Driver.CaptureSaveDto(),
                CustodyDto = g.Loop.CaptureCustodySaveDto(),
                TasksDto = g.Tasks.CaptureSaveDto(),
                BudgetsDto = g.Budgets.CaptureSaveDto(),
                SchedulesDto = g.Tracker.CaptureSaveDto(),
                DueDto = g.DueEvents.CaptureSaveDto(),
            };
            g.Inventories.ExportState(snap2.InventoryStates);
            g.Ledgers.ExportState(snap2.LedgerStates);
            Assert.AreEqual(1, snap2.DriverDto.Count, "save #2: exactly one trip DTO — no duplicated trips");
            Assert.AreEqual(1, snap2.CustodyDto.Batches.Count, "save #2: exactly one custody batch — no duplicated batches");
            Assert.AreEqual(batchId, snap2.DriverDto[0].BatchId, "save #2: the trip DTO carries the same batch id");
            Assert.AreEqual(batchId, snap2.CustodyDto.Batches[0].BatchId, "save #2: the custody DTO carries the same batch id");

            RestoredDriver second = RestoreDriver(snap2, female, personId, driverFirst: false);
            Fixture h = second.Fixture;
            Assert.IsNotNull(second.Trip, "load #2: one trip must restore");
            Assert.AreEqual(batchId, second.Trip.Batch.BatchId, "load #2: the batch id survives");
            Assert.AreEqual(14, second.Trip.Batch.Units, "load #2: the batch holds the 14 loaves");
            Assert.AreEqual(receiptId, second.Trip.Batch.ReceiptId, "load #2: the receipt id survives");
            Assert.AreEqual(1, h.Loop.ActiveCustodyBatches.Count, "load #2: exactly one custody batch — no duplication");
            Assert.IsTrue(ReferenceEquals(second.Trip.Batch, h.Loop.ActiveCustodyBatches[0]),
                "load #2: the trip batch must BE the loop-owned batch");
            Assert.AreEqual(0, h.BreadUnits(), "load #2: household holds no bread yet");

            // Continue in the twice-restored world: exactly once.
            h.RunDriverUntilDone();
            Assert.AreEqual(0, h.Driver.ActiveTrips.Count, "the twice-restored trip must finish: " + string.Join(" | ", h.Diagnostics));
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, second.Trip.Need.Status,
                "the twice-restored need must be fulfilled by the Continue");
            AssertDepositedOnce(h, batchId, receiptId, personId, second.CustodyLog, "deposit after two driver loads");
        }

        [Test]
        public void LoopCustodySaveLoad_EmptyAndNullContracts()
        {
            Fixture f = Fixture.CreateSingle(female: true, personId: 101);

            // An empty loop captures an empty DTO and loads back to empty.
            HouseholdShoppingCustodySaveDto empty = f.Loop.CaptureCustodySaveDto();
            Assert.AreEqual(0, empty.Batches.Count, "an empty loop captures no batches");
            f.Loop.LoadCustodyFromSaveDto(empty);
            Assert.AreEqual(0, f.Loop.ActiveCustodyBatches.Count, "loading an empty DTO leaves custody empty");

            // A null DTO clears to empty without throwing.
            f.Loop.LoadCustodyFromSaveDto(null);
            Assert.AreEqual(0, f.Loop.ActiveCustodyBatches.Count, "loading a null DTO clears custody to empty");

            // A hand-built DTO with a duplicate id, an already-received
            // batch, and a null entry loads exactly the one valid batch.
            var dto = new HouseholdShoppingCustodySaveDto { NextBatchSequence = 0 };
            dto.Batches.Add(new CarriedGoodsBatch
            {
                BatchId = "carry-0-3", HouseholdId = 7, HolderPersonId = 101,
                ItemId = HouseholdItemCatalog.BreadId, Units = 5, UnitPriceCents = 5,
                ReceiptId = "rcpt-hand", Provenance = "hand-built", Custody = GoodsCustodyState.InHand,
                PurchaseDayIndex = 0,
            });
            dto.Batches.Add(new CarriedGoodsBatch
            {
                BatchId = "carry-0-3", HouseholdId = 7, HolderPersonId = 101,
                ItemId = HouseholdItemCatalog.BreadId, Units = 5, UnitPriceCents = 5,
                ReceiptId = "rcpt-hand", Provenance = "hand-built duplicate", Custody = GoodsCustodyState.InHand,
                PurchaseDayIndex = 0,
            });
            dto.Batches.Add(new CarriedGoodsBatch
            {
                BatchId = "carry-0-4", HouseholdId = 7, HolderPersonId = 101,
                ItemId = HouseholdItemCatalog.BreadId, Units = 9, UnitPriceCents = 5,
                ReceiptId = "rcpt-received", Provenance = "already received", Custody = GoodsCustodyState.ReceivedAtHousehold,
                PurchaseDayIndex = 0,
            });
            dto.Batches.Add(null);
            f.Loop.LoadCustodyFromSaveDto(dto);
            Assert.AreEqual(1, f.Loop.ActiveCustodyBatches.Count,
                "a duplicate id loads once, a received batch never resurrects, a null entry is skipped");
            Assert.AreEqual("carry-0-3", f.Loop.ActiveCustodyBatches[0].BatchId, "the valid batch is the one loaded");
            Assert.AreEqual("hand-built", f.Loop.ActiveCustodyBatches[0].Provenance, "the first copy wins, not the duplicate");

            // Loading the same DTO again is clear-then-load, not accumulate.
            f.Loop.LoadCustodyFromSaveDto(dto);
            Assert.AreEqual(1, f.Loop.ActiveCustodyBatches.Count, "a second load of the same DTO does not accumulate");
        }

        [Test]
        public void ReceiveCarriedBatch_RefusesUnknownAndDelivery()
        {
            Fixture f = Fixture.CreateSingle(female: true, personId: 101);
            f.Fund(10000);

            // An unknown batch id is refused and nothing moves.
            string unknown = f.Loop.ReceiveCarriedBatch("carry-9-99", f.Day, f.Diagnostics);
            Assert.IsTrue(!string.IsNullOrEmpty(unknown), "an unknown batch id must be refused");
            Assert.AreEqual(0, f.BreadUnits(), "the refused receipt must not create bread");
            Assert.AreEqual(10000, f.Balance(), "the refused receipt must not move cash");

            // An arranged-delivery batch is refused here: delivery receipt
            // is ReceiveDelivery's path, not this one.
            var dto = new HouseholdShoppingCustodySaveDto { NextBatchSequence = 1 };
            dto.Batches.Add(new CarriedGoodsBatch
            {
                BatchId = "carry-0-7", HouseholdId = 7, HolderPersonId = 101,
                ItemId = HouseholdItemCatalog.BreadId, Units = 4, UnitPriceCents = 5,
                ReceiptId = "rcpt-delivery", Provenance = "arranged delivery", Custody = GoodsCustodyState.ArrangedDelivery,
                PurchaseDayIndex = 0,
            });
            f.Loop.LoadCustodyFromSaveDto(dto);
            Assert.AreEqual(1, f.Loop.ActiveCustodyBatches.Count, "the delivery batch loads into custody");
            string delivery = f.Loop.ReceiveCarriedBatch("carry-0-7", f.Day, f.Diagnostics);
            Assert.IsTrue(!string.IsNullOrEmpty(delivery), "an arranged-delivery batch must be refused by the carried receipt");
            Assert.AreEqual(0, f.BreadUnits(), "the refused delivery receipt must not create bread");
            Assert.AreEqual(1, f.Loop.ActiveCustodyBatches.Count, "the refused delivery batch stays in custody");
        }

        /// <summary>
        /// Priority U-3: the deliberate counterpart of
        /// <see cref="AssertNoSaveSurface"/> — a type that MUST declare the
        /// named save-surface methods (reflection over its own declared
        /// public methods).
        /// </summary>
        private static void AssertSaveSurface(Type type, string label, params string[] methodNames)
        {
            foreach (string wanted in methodNames)
            {
                bool found = false;
                foreach (MethodInfo method in type.GetMethods(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (string.Equals(method.Name, wanted, StringComparison.Ordinal))
                    {
                        found = true;
                        break;
                    }
                }

                Assert.IsTrue(found, label + " must declare the save-surface method " + wanted);
            }
        }

        /// <summary>
        /// A type with no save surface at all: no public method whose name
        /// mentions saving, exporting, capturing, loading, or importing.
        /// (Reflection over the type's own declared public methods; a future
        /// save surface fails this pin loudly, which is the point — the pin
        /// must be updated deliberately, not silently.)
        /// </summary>
        private static void AssertNoSaveSurface(Type type, string label)
        {
            foreach (MethodInfo method in type.GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                string name = method.Name;
                Assert.IsTrue(
                    !name.Contains("Save") && !name.Contains("Export") && !name.Contains("Capture")
                    && !name.Contains("Load") && !name.Contains("Import"),
                    label + " must declare no save surface, but declares " + name);
            }
        }
    }
}
