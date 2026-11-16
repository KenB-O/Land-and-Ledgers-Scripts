using System;
using System.Collections.Generic;
using System.Linq;
using LandLedgers.Economy.GeneralStore;
using LandLedgers.Economy.Retail;
using LandLedgers.FirstLedger;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.Editor.Population
{
    /// <summary>
    /// Pass IV (Worker 4): shopping through REAL time. Proves:
    /// - PlanTrip plans (shopper, seller, legs, per-leg person-time windows)
    ///   without executing anything.
    /// - The driver moves the trip through real time via due events: outbound
    ///   travel → counter settlement (real ledger outflow, real store stock,
    ///   custody batch) → in-store time → return → physical receipt into
    ///   household lots. Goods never teleport; the no-teleport invariant holds
    ///   mid-trip (household inventory untouched until receipt).
    /// - Tick-size independence: 5-minute ticks and 6-hour ticks produce
    ///   identical consequential outcomes.
    /// - Store-closed-at-arrival and stockout paths walk home empty-handed
    ///   through the real return leg; the need stays open.
    /// - Save/load mid-trip round-trips and resumes to the same outcome.
    /// </summary>
    [TestFixture]
    public sealed class ShoppingTripDriverTests
    {
        private sealed class TestStore : IItemResolvingSupplier
        {
            public string SupplierBusinessId { get; set; } = "store-1";
            public string SupplierName { get; set; } = "General Store";
            public string LocationId { get; set; } = "store";
            public Dictionary<string, int> Stock = new Dictionary<string, int>();
            public Dictionary<string, int> Prices = new Dictionary<string, int>();
            public int CashReceived;
            public string LastReceiptIdValue = string.Empty;
            public int ReceiptSequence;
            public bool Closed;

            public bool HasCategory(string categoryId) => true;
            public int StockUnits(string categoryId) => 0;
            public int PricePerUnitCents(string categoryId) => 0;
            public int Sell(string categoryId, int requestedUnits, int dayIndex, List<string> diagnostics) => 0;

            public bool OffersItem(string itemId) => Stock.ContainsKey(itemId);
            public string CategoryForItem(string itemId) => "staple_food";
            public int ItemStockUnits(string itemId) => Stock.TryGetValue(itemId, out int s) ? s : 0;
            public int ItemPricePerUnitCents(string itemId) => Prices.TryGetValue(itemId, out int p) ? p : 0;
            public string CheckTradingCapability(int absoluteDayIndex, int minuteOfDay) =>
                Closed ? "closed for the night" : null;

            public string LastReceiptId => LastReceiptIdValue;

            public int SellItem(string itemId, int requestedUnits, int dayIndex, ShoppingSettlement settlement, List<string> diagnostics)
            {
                int have = ItemStockUnits(itemId);
                int sold = System.Math.Min(System.Math.Max(0, requestedUnits), have);
                if (sold > 0)
                {
                    Stock[itemId] = have - sold;
                    LastReceiptIdValue = $"rcpt-{++ReceiptSequence}";
                    if (settlement != null && settlement.Mode == ShoppingSettlementMode.CashFromLedger)
                    {
                        CashReceived += settlement.AmountCents;
                    }
                }

                return sold;
            }
        }

        private sealed class Fixture
        {
            public readonly PopulationState Population = new PopulationState();
            public readonly HouseholdMembershipRegistry Membership = new HouseholdMembershipRegistry();
            public readonly HouseholdLedgerRegistry Ledgers = new HouseholdLedgerRegistry();
            public readonly HouseholdInventoryRegistry Inventories = new HouseholdInventoryRegistry();
            public readonly HouseholdNeedRegistry Needs = new HouseholdNeedRegistry();
            public readonly SupplierDirectory Directory = new SupplierDirectory();
            public readonly JourneyModel Journeys = new JourneyModel();
            public readonly TaskAuthority Tasks = new TaskAuthority();
            public readonly WorkTimeBudgetStore Budgets = new WorkTimeBudgetStore();
            public readonly PersonScheduleTracker Tracker = new PersonScheduleTracker();
            public readonly ShopperSellerKnowledge Knowledge = new ShopperSellerKnowledge();
            public readonly SimpleDueEventScheduler DueEvents = new SimpleDueEventScheduler();
            public readonly TestStore Store = new TestStore();
            public readonly HouseholdShoppingLoop Loop;
            public readonly ShoppingTripDriver Driver;
            public readonly Dictionary<int, string> Locations = new Dictionary<int, string>();
            public readonly List<string> Diag = new List<string>();

            public const int HouseholdId = 7;
            public const int ShopperId = 1;
            public const int Day = 100;

            public Fixture()
            {
                Population.people.Add(new PersonState { id = 1, firstName = "Tomas", lastName = "Morrow", age = 34, ageBand = AgeBand.Adult18Plus });
                Population.people.Add(new PersonState { id = 2, firstName = "Petra", lastName = "Morrow", age = 8, ageBand = AgeBand.Child0To9 });
                Population.households.Add(new HouseholdState { id = HouseholdId, householdName = "Morrow" });
                Membership.Register(new HouseholdMembership { MembershipId = 1, PersonId = 1, HouseholdId = HouseholdId, StartDayIndex = 0 });
                Membership.Register(new HouseholdMembership { MembershipId = 2, PersonId = 2, HouseholdId = HouseholdId, StartDayIndex = 0 });

                Journeys.RegisterLocation(new JourneyLocation("home", JourneyLocationKind.Farmstead, "Morrow Farm", 0f, 0f));
                Journeys.RegisterLocation(new JourneyLocation("store", JourneyLocationKind.Store, "General Store", 2f, 0f));
                Journeys.AddEdge("home", "store", 2.0f, "river road");

                Store.Stock[HouseholdItemCatalog.FlourId] = 100;
                Store.Prices[HouseholdItemCatalog.FlourId] = 120;
                Directory.Register(Store);
                Knowledge.LearnSupplier(1, "store-1", "lives in the same town", 0);

                Locations[1] = "home";
                Locations[2] = "home";

                HouseholdShoppingLoop.RegisterTaskDefinitions(Tasks);
                var options = new ShoppingLoopOptions
                {
                    PersonLocationId = pid => Locations.TryGetValue(pid, out string l) ? l : "home",
                    SetPersonLocation = (pid, l) => Locations[pid] = l,
                    DepartureMinuteOfDay = 600,
                    InStoreMinutes = 15,
                    AllowDelivery = false,
                    Diagnostics = Diag,
                };
                Loop = new HouseholdShoppingLoop(
                    Population, Membership, Ledgers, Inventories, Needs, Directory, Journeys,
                    Tasks, Budgets, Tracker, Knowledge,
                    obligations: null, entityIds: null,
                    personEntityId: pid => EntityId.For(EntityKind.Person, pid),
                    spendingPolicyProvider: null);
                LoopOptions = options;
                Driver = new ShoppingTripDriver(Loop);

                // Fund the household.
                Ledgers.GetOrCreate(HouseholdId).RecordInflow(0, 50000,
                    HouseholdIncomeSource.WageEmployment, "emp-1", "seed wages", "test");
            }

            public ShoppingLoopOptions LoopOptions;

            public HouseholdPurchasingNeed OpenNeed(string itemId, int units)
            {
                HouseholdPurchasingNeed need = Needs.CreateNeed(HouseholdId, itemId, Day);
                need.UnitsNeeded = units;
                need.IsNecessity = true;
                return need;
            }

            /// <summary>Runs the driver from day start until no active trips remain (or a tick cap).</summary>
            public void RunUntilDone(int tickSeconds, int maxTicks = 2000)
            {
                float start = Day * 86400f;
                for (int t = 0; t < maxTicks && Driver.ActiveTrips.Count > 0; t++)
                {
                    float now = start + t * tickSeconds;
                    Driver.AdvanceTick(now, Day, DueEvents, Diag);
                    DrainDue(now);
                }

                // Drain anything left (large steps schedule completions past the
                // final tick boundary): keep popping until the queue is empty.
                for (int i = 0; i < 50 && (DueEvents.ScheduledCount > 0 || Driver.ActiveTrips.Count > 0); i++)
                {
                    DrainDue(start + 3 * 86400f);
                }
            }

            private void DrainDue(float now)
            {
                // Handle in due order; use each event's own due time as the
                // clock so arrival-time checks see honest minutes.
                foreach (DueEvent ev in DueEvents.PopDue(now))
                {
                    Driver.HandleDueEvent(ev, Day, ev.DueGameSeconds, DueEvents, Diag);
                }
            }
        }

        [Test]
        public void FailedTripPlanningCancelsAssignedAndQueuedLegsAndReturnsBudget()
        {
            var f = new Fixture();
            EntityId worker = EntityId.For(EntityKind.Person, Fixture.ShopperId);
            WorkTask outbound = f.Tasks.CreateTaskWithPlannedMinutes(HouseholdShoppingLoop.ShoppingTaskId, worker, Fixture.Day, 10);
            WorkTask shopping = f.Tasks.CreateTaskWithPlannedMinutes(HouseholdShoppingLoop.ShoppingTaskId, worker, Fixture.Day, 15);
            WorkTask returning = f.Tasks.CreateTaskWithPlannedMinutes(HouseholdShoppingLoop.ShoppingTaskId, worker, Fixture.Day, 10);
            Assert.IsTrue(f.Tasks.AssignTask(outbound.TaskId, worker, f.Budgets, out _));
            Assert.IsTrue(f.Tasks.AssignTask(shopping.TaskId, worker, f.Budgets, out _));
            Assert.Greater(f.Budgets.GetOrCreate(worker).MinutesCommitted, 0);
            var legs = new HouseholdShoppingLoop.PlannedTrip { Outbound = outbound, InStore = shopping, Return = returning };
            typeof(HouseholdShoppingLoop).GetMethod("CancelPlannedLegs", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(f.Loop, new object[] { legs });
            Assert.AreEqual(0, f.Tasks.GetOpenTasks(worker).Count);
            Assert.AreEqual(0, f.Budgets.GetOrCreate(worker).MinutesCommitted);
            Assert.IsFalse(outbound.IsOpen); Assert.IsFalse(shopping.IsOpen); Assert.IsFalse(returning.IsOpen);
        }

        [Test]
        public void MissedDepartureExpiresPlanAndReleasesItsReservations()
        {
            var f = new Fixture();
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            f.Driver.PlanForHousehold(Fixture.HouseholdId, f.Needs, Fixture.Day, f.LoopOptions, f.Diag);
            Assert.AreEqual(1, f.Driver.ActiveTrips.Count);
            PlannedShoppingTrip trip = f.Driver.ActiveTrips[0];
            Assert.AreEqual(3, trip.WindowReservationIds.Count);

            f.Driver.AdvanceTick((Fixture.Day + 1) * 86400f, Fixture.Day + 1, f.DueEvents, f.Diag);

            Assert.AreEqual(PlannedShoppingTrip.TripState.Failed, trip.State);
            StringAssert.Contains("Missed shopping departure", trip.FailureReason);
            Assert.AreEqual(0, trip.WindowReservationIds.Count);
            Assert.AreEqual(0, f.Driver.ActiveTrips.Count);
            Assert.IsTrue(f.Tracker.IsFree(trip.ShopperPersonId, Fixture.Day, trip.DepartureMinuteOfDay, 1));
            Assert.IsFalse(trip.Legs.Outbound.IsOpen);
            Assert.IsFalse(trip.Legs.InStore.IsOpen);
            Assert.IsFalse(trip.Legs.Return.IsOpen);
            Assert.Contains(need, f.Needs.GetOpenNeeds(Fixture.HouseholdId));
            Assert.AreEqual(0, f.Store.CashReceived);
        }

        [Test]
        public void PlanTrip_PlansWithoutExecuting()
        {
            var f = new Fixture();
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);

            PlannedShoppingTrip trip = f.Loop.PlanTrip(need, Fixture.Day, f.LoopOptions);

            Assert.AreEqual(PlannedShoppingTrip.TripState.Planned, trip.State, trip.FailureReason);
            Assert.AreEqual(Fixture.ShopperId, trip.ShopperPersonId);
            Assert.IsNotNull(trip.Legs);
            Assert.IsNotNull(trip.Legs.Outbound);
            Assert.IsNotNull(trip.Legs.InStore);
            Assert.IsNotNull(trip.Legs.Return);
            Assert.AreEqual(3, trip.WindowReservationIds.Count, "one window per leg");
            // Nothing executed yet: store stock and household inventory untouched.
            Assert.AreEqual(100, f.Store.Stock[HouseholdItemCatalog.FlourId]);
            Assert.AreEqual(0, f.Store.CashReceived);
            // The shopper's windows are really reserved.
            Assert.IsFalse(f.Tracker.IsFree(Fixture.ShopperId, Fixture.Day, 600, 240));
        }

        [Test]
        public void PlanTrip_SearchesLaterFreeWindowInsteadOfRejectingTheNeed()
        {
            var f = new Fixture();
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            Assert.IsNull(f.Tracker.TryReserve(Fixture.ShopperId, PersonActivityKind.Work,
                Fixture.Day, 600, 120, "existing work commitment"));

            PlannedShoppingTrip trip = f.Loop.PlanTrip(need, Fixture.Day, f.LoopOptions);

            Assert.AreEqual(PlannedShoppingTrip.TripState.Planned, trip.State, trip.FailureReason);
            Assert.GreaterOrEqual(trip.DepartureMinuteOfDay, 720,
                "The trip should wait for the actual committed window to end.");
            Assert.IsFalse(f.Tracker.IsFree(Fixture.ShopperId, Fixture.Day, 600, 120),
                "The search must preserve the pre-existing work reservation.");
            List<PersonActivityReservation> tripReservations = f.Tracker.Overlapping(
                Fixture.ShopperId, Fixture.Day, trip.DepartureMinuteOfDay,
                trip.Legs.OutboundMinutes + 15 + trip.Legs.ReturnMinutes);
            Assert.AreEqual(3, tripReservations.Count,
                "The selected window should contain only the three reservations owned by this trip.");
            foreach (PersonActivityReservation reservation in tripReservations)
            {
                Assert.AreNotEqual(PersonActivityKind.Work, reservation.Activity,
                    "The existing work commitment must not overlap the selected trip.");
            }
            Assert.AreEqual(100, f.Store.Stock[HouseholdItemCatalog.FlourId],
                "Planning must not settle a sale or create household goods.");
        }

        [Test]
        public void PlannedRouteTravelReservationAdvancesItsTaskProgress()
        {
            var f = new Fixture();
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);

            PlannedShoppingTrip trip = f.Loop.PlanTrip(need, Fixture.Day, f.LoopOptions);
            Assert.AreEqual(PlannedShoppingTrip.TripState.Planned, trip.State, trip.FailureReason);
            WorkTask outbound = trip.Legs.Outbound;
            Assert.IsNotNull(outbound);
            Assert.IsTrue(f.Tasks.StartTask(outbound.TaskId, Fixture.Day, out string reason), reason);

            var reservations = f.Tracker.ActiveFor(Fixture.ShopperId, Fixture.Day);
            int progressMinutes = AutonomousTaskProgress.ScheduledMinutes(
                outbound, reservations, Fixture.Day, 600,
                600 + trip.Legs.OutboundMinutes,
                $"task:{outbound.TaskId}|{outbound.DefinitionId}");

            Assert.AreEqual(trip.Legs.OutboundMinutes, progressMinutes,
                "the real-time progress authority must recognize the travel reservation booked by shopping");
        }

        [Test]
        public void FullTripThroughRealTime_SettlesAndReceives()
        {
            var f = new Fixture();
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);

            f.Driver.PlanForHousehold(Fixture.HouseholdId, f.Needs, Fixture.Day, f.LoopOptions, f.Diag);
            Assert.AreEqual(1, f.Driver.ActiveTrips.Count);

            int inventoryBefore = f.Inventories.GetOrCreate(Fixture.HouseholdId)
                .GetAvailableUnits(HouseholdItemCatalog.FlourId);

            f.RunUntilDone(tickSeconds: 300);

            Assert.AreEqual(0, f.Driver.ActiveTrips.Count, "trip should be done\n" + string.Join("\n", f.Diag));
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status);
            // Conservation: household paid 10u x 120c, store stock dropped 10u.
            Assert.AreEqual(50000 - 1200, f.Ledgers.GetOrCreate(Fixture.HouseholdId).GetBalanceCents());
            Assert.AreEqual(1200, f.Store.CashReceived);
            Assert.AreEqual(90, f.Store.Stock[HouseholdItemCatalog.FlourId]);
            // Physical receipt only at the end: inventory grew by exactly 10u.
            int inventoryAfter = f.Inventories.GetOrCreate(Fixture.HouseholdId)
                .GetAvailableUnits(HouseholdItemCatalog.FlourId);
            Assert.AreEqual(inventoryBefore + 10, inventoryAfter);
            // Shopper is home and free again.
            Assert.AreEqual("home", f.Locations[Fixture.ShopperId]);
            Assert.AreEqual(0, f.Loop.ActiveCustodyBatches.Count);
        }

        [Test]
        public void NonProgressingAuthoritativeRouteEventuallyFailsAndCancelsWithoutArrival()
        {
            var f = new Fixture();
            f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            bool cancelled = false;
            float remaining = 120f;
            var driver = new ShoppingTripDriver(f.Loop, (p, l) => true, (p, l) => false,
                (p, l) => true, (p, l) => remaining, (p, l) => cancelled = true, 60f);
            driver.PlanForHousehold(Fixture.HouseholdId, f.Needs, Fixture.Day, f.LoopOptions, f.Diag);
            var trip = driver.ActiveTrips[0];
            float start = Fixture.Day * 86400f + trip.DepartureMinuteOfDay * 60f;
            driver.AdvanceTick(start, Fixture.Day, f.DueEvents, f.Diag);
            var ev = f.DueEvents.PopDue(start + 86400f)[0];
            driver.HandleDueEvent(ev, Fixture.Day, ev.DueGameSeconds, f.DueEvents, f.Diag);
            float now = ev.DueGameSeconds;
            for (int i = 0; i < 5 && trip.IsActive; i++)
            {
                now += 60f;
                remaining += 1f; // Moving farther away is not route progress.
                foreach (var due in f.DueEvents.PopDue(now))
                    driver.HandleDueEvent(due, Fixture.Day, due.DueGameSeconds, f.DueEvents, f.Diag);
            }
            Assert.AreEqual(PlannedShoppingTrip.TripState.Failed, trip.State);
            Assert.IsTrue(cancelled);
            Assert.IsFalse(driver.HasDepartedTripForPerson(trip.ShopperPersonId));
            Assert.AreEqual(0, f.Store.CashReceived);
            Assert.AreEqual(0, trip.WindowReservationIds.Count);
        }

        [Test]
        public void ProgressingLongRouteIsNotFailedForExceedingInitialEstimate()
        {
            var f = new Fixture();
            f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            float remaining = 600f;
            var driver = new ShoppingTripDriver(f.Loop, (p, l) => true, (p, l) => false,
                (p, l) => true, (p, l) => remaining, null, 60f);
            driver.PlanForHousehold(Fixture.HouseholdId, f.Needs, Fixture.Day, f.LoopOptions, f.Diag);
            var trip = driver.ActiveTrips[0];
            float start = Fixture.Day * 86400f + trip.DepartureMinuteOfDay * 60f;
            driver.AdvanceTick(start, Fixture.Day, f.DueEvents, f.Diag);
            var ev = f.DueEvents.PopDue(start + 86400f)[0];
            driver.HandleDueEvent(ev, Fixture.Day, ev.DueGameSeconds, f.DueEvents, f.Diag);
            float now = ev.DueGameSeconds;
            for (int i = 0; i < 15; i++)
            {
                now += 60f;
                remaining -= 20f;
                foreach (var due in f.DueEvents.PopDue(now))
                    driver.HandleDueEvent(due, Fixture.Day, due.DueGameSeconds, f.DueEvents, f.Diag);
            }
            Assert.AreEqual(PlannedShoppingTrip.TripState.Outbound, trip.State);
            Assert.AreEqual(0, f.Store.CashReceived);
        }

        [Test]
        public void FailedReturnRecoversPaidCustodyOnlyAfterPhysicalHomeArrival()
        {
            var f = new Fixture();
            var need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            string physicalLocation = "home";
            bool returnAvailable = false;
            f.Budgets.EnsureDay(Fixture.Day);
            var driver = new ShoppingTripDriver(f.Loop, (p, l) => l != "home" || returnAvailable,
                (p, l) => l == physicalLocation, (p, l) => true);
            driver.PlanForHousehold(Fixture.HouseholdId, f.Needs, Fixture.Day, f.LoopOptions, f.Diag);
            var trip = driver.ActiveTrips[0];
            float departure = Fixture.Day * 86400f + trip.DepartureMinuteOfDay * 60f;
            driver.AdvanceTick(departure, Fixture.Day, f.DueEvents, f.Diag);
            physicalLocation = "store";
            var outbound = f.DueEvents.PopDue(departure + 86400f)[0];
            driver.HandleDueEvent(outbound, Fixture.Day, outbound.DueGameSeconds, f.DueEvents, f.Diag);
            var inStore = f.DueEvents.PopDue(outbound.DueGameSeconds + 86400f)[0];
            driver.HandleDueEvent(inStore, Fixture.Day, inStore.DueGameSeconds, f.DueEvents, f.Diag);
            Assert.AreEqual(PlannedShoppingTrip.TripState.Failed, trip.State);
            Assert.AreEqual(1200, f.Store.CashReceived);
            Assert.AreEqual(90, f.Store.Stock[HouseholdItemCatalog.FlourId]);
            Assert.AreEqual(1, f.Loop.ActiveCustodyBatches.Count);
            Assert.AreEqual(0, f.Inventories.GetOrCreate(Fixture.HouseholdId).GetTotalUnits(HouseholdItemCatalog.FlourId));
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status);
            f.Budgets.EnsureDay(Fixture.Day + 1);
            driver.PlanForHousehold(Fixture.HouseholdId, f.Needs, Fixture.Day + 1, f.LoopOptions, f.Diag);
            Assert.AreEqual(1, driver.ActiveTrips.Count, "the paid batch gets a reserved recovery leg rather than another purchase");
            Assert.AreEqual(PlannedShoppingTrip.TripState.RecoveryPlanned, trip.State);
            returnAvailable = true;
            float recoveryDeparture = (Fixture.Day + 1) * 86400f + trip.DepartureMinuteOfDay * 60f;
            driver.AdvanceTick(recoveryDeparture, Fixture.Day + 1, f.DueEvents, f.Diag);
            var returnDue = f.DueEvents.PopDue(recoveryDeparture + 86400f)[0];
            driver.HandleDueEvent(returnDue, Fixture.Day + 1, returnDue.DueGameSeconds, f.DueEvents, f.Diag);
            Assert.AreEqual(0, f.Inventories.GetOrCreate(Fixture.HouseholdId).GetTotalUnits(HouseholdItemCatalog.FlourId));
            physicalLocation = "home";
            var retry = f.DueEvents.PopDue(returnDue.DueGameSeconds + 60f)[0];
            driver.HandleDueEvent(retry, Fixture.Day + 1, retry.DueGameSeconds, f.DueEvents, f.Diag);
            Assert.AreEqual(PlannedShoppingTrip.TripState.Done, trip.State);
            Assert.AreEqual(10, f.Inventories.GetOrCreate(Fixture.HouseholdId).GetTotalUnits(HouseholdItemCatalog.FlourId));
            Assert.AreEqual(0, f.Loop.ActiveCustodyBatches.Count);
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status);
            driver.HandleDueEvent(retry, Fixture.Day + 1, retry.DueGameSeconds, f.DueEvents, f.Diag);
            Assert.AreEqual(10, f.Inventories.GetOrCreate(Fixture.HouseholdId).GetTotalUnits(HouseholdItemCatalog.FlourId));
            Assert.AreEqual(1200, f.Store.CashReceived);
            Assert.AreEqual(90, f.Store.Stock[HouseholdItemCatalog.FlourId]);
        }

        [Test]
        public void FailedOutboundRouteAbortsWithoutPaymentAndReleasesWindows()
        {
            var f = new Fixture();
            var need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            var driver = new ShoppingTripDriver(f.Loop, (p, l) => true, (p, l) => false, (p, l) => false);
            driver.PlanForHousehold(Fixture.HouseholdId, f.Needs, Fixture.Day, f.LoopOptions, f.Diag);
            var trip = driver.ActiveTrips[0];
            float departure = Fixture.Day * 86400f + trip.DepartureMinuteOfDay * 60f;
            driver.AdvanceTick(departure, Fixture.Day, f.DueEvents, f.Diag);
            var ev = f.DueEvents.PopDue(departure + 86400f)[0];
            driver.HandleDueEvent(ev, Fixture.Day, ev.DueGameSeconds, f.DueEvents, f.Diag);
            Assert.AreEqual(PlannedShoppingTrip.TripState.Failed, trip.State);
            Assert.AreEqual(0, driver.ActiveTrips.Count);
            Assert.AreEqual(0, trip.WindowReservationIds.Count);
            Assert.AreEqual(0, f.Store.CashReceived);
            Assert.AreEqual(100, f.Store.Stock[HouseholdItemCatalog.FlourId]);
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status);
        }

        [TestCase(true)]
        [TestCase(null)]
        public void ActiveOrUnknownOutboundRouteCanContinuePastExpectedArrival(bool? routeState)
        {
            var f = new Fixture();
            f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            var driver = new ShoppingTripDriver(f.Loop, (p, l) => true, (p, l) => false, (p, l) => routeState);
            driver.PlanForHousehold(Fixture.HouseholdId, f.Needs, Fixture.Day, f.LoopOptions, f.Diag);
            var trip = driver.ActiveTrips[0];
            float departure = Fixture.Day * 86400f + trip.DepartureMinuteOfDay * 60f;
            driver.AdvanceTick(departure, Fixture.Day, f.DueEvents, f.Diag);
            var ev = f.DueEvents.PopDue(departure + 86400f)[0];
            driver.HandleDueEvent(ev, Fixture.Day, ev.DueGameSeconds, f.DueEvents, f.Diag);
            Assert.AreEqual(PlannedShoppingTrip.TripState.Outbound, trip.State);
            Assert.AreEqual(1, driver.ActiveTrips.Count);
            Assert.AreEqual(0, f.Store.CashReceived);
        }

        [Test]
        public void PhysicalArrivalGate_PreventsCounterSettlementUntilStoreArrival()
        {
            var f = new Fixture();
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            string target = string.Empty;
            bool physicallyArrived = false;
            var driver = new ShoppingTripDriver(f.Loop,
                (personId, location) => { target = location; physicallyArrived = false; return true; },
                (personId, location) => physicallyArrived && target == location);
            driver.PlanForHousehold(Fixture.HouseholdId, f.Needs, Fixture.Day, f.LoopOptions, f.Diag);

            float now = Fixture.Day * 86400f + 600 * 60f;
            Assert.IsFalse(driver.HasDepartedTripForPerson(Fixture.ShopperId));
            driver.AdvanceTick(now, Fixture.Day, f.DueEvents, f.Diag);
            Assert.IsTrue(driver.HasDepartedTripForPerson(Fixture.ShopperId));
            DueEvent outboundDue = f.DueEvents.PopDue(now + 86400f)[0];
            driver.HandleDueEvent(outboundDue, Fixture.Day, outboundDue.DueGameSeconds,
                f.DueEvents, f.Diag);
            Assert.AreEqual(50000, f.Ledgers.GetOrCreate(Fixture.HouseholdId).GetBalanceCents(),
                "elapsed estimate without physical arrival cannot settle the counter");
            Assert.AreEqual(100, f.Store.Stock[HouseholdItemCatalog.FlourId]);
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status);

            physicallyArrived = true;
            DueEvent retry = f.DueEvents.PopDue(outboundDue.DueGameSeconds + 60f)[0];
            driver.HandleDueEvent(retry, Fixture.Day, retry.DueGameSeconds, f.DueEvents, f.Diag);
            Assert.AreEqual(48800, f.Ledgers.GetOrCreate(Fixture.HouseholdId).GetBalanceCents(),
                "settlement occurs once the actual store arrival predicate succeeds");
            Assert.AreEqual(90, f.Store.Stock[HouseholdItemCatalog.FlourId]);

            // Finish the in-store action and allow the physical return arrival.
            DueEvent inStore = f.DueEvents.PopDue(retry.DueGameSeconds + 86400f)[0];
            driver.HandleDueEvent(inStore, Fixture.Day, inStore.DueGameSeconds, f.DueEvents, f.Diag);
            physicallyArrived = true;
            DueEvent returning = f.DueEvents.PopDue(inStore.DueGameSeconds + 86400f)[0];
            driver.HandleDueEvent(returning, Fixture.Day, returning.DueGameSeconds, f.DueEvents, f.Diag);
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status);
            Assert.IsFalse(driver.HasDepartedTripForPerson(Fixture.ShopperId));
            Assert.AreEqual(10, f.Inventories.GetOrCreate(Fixture.HouseholdId)
                .GetAvailableUnits(HouseholdItemCatalog.FlourId));
        }

        [Test]
        public void ZeroDurationRoute_WaitsForPhysicalArrivalBeforePurchase()
        {
            var f = new Fixture();
            f.Store.LocationId = "home";
            bool arrived = false;
            bool routeStarted = false;
            var driver = new ShoppingTripDriver(f.Loop,
                (personId, locationId) => { routeStarted = true; return true; },
                (personId, locationId) => arrived);
            f.OpenNeed(HouseholdItemCatalog.FlourId, 10);

            driver.PlanForHousehold(Fixture.HouseholdId, f.Needs, Fixture.Day, f.LoopOptions, f.Diag);
            Assert.AreEqual(1, driver.ActiveTrips.Count);
            PlannedShoppingTrip trip = driver.ActiveTrips[0];
            Assert.IsNull(trip.Legs.Outbound, "the logical model treats identical locations as zero duration");

            float departure = Fixture.Day * 86400f + trip.DepartureMinuteOfDay * 60f;
            driver.AdvanceTick(departure, Fixture.Day, f.DueEvents, f.Diag);

            Assert.IsTrue(routeStarted);
            Assert.AreEqual(PlannedShoppingTrip.TripState.Outbound, trip.State);
            Assert.AreEqual(0, f.Store.CashReceived, "no counter settlement before physical arrival");
            Assert.AreEqual(100, f.Store.ItemStockUnits(HouseholdItemCatalog.FlourId));
            Assert.AreEqual(0, f.Inventories.GetOrCreate(Fixture.HouseholdId).GetTotalUnits(HouseholdItemCatalog.FlourId));

            arrived = true;
            foreach (DueEvent ev in f.DueEvents.PopDue(departure + 60f))
                driver.HandleDueEvent(ev, Fixture.Day, ev.DueGameSeconds, f.DueEvents, f.Diag);

            Assert.Greater(f.Store.CashReceived, 0, "settlement proceeds after physical arrival is confirmed");
            Assert.AreEqual(90, f.Store.ItemStockUnits(HouseholdItemCatalog.FlourId));
            Assert.AreEqual(0, f.Inventories.GetOrCreate(Fixture.HouseholdId).GetTotalUnits(HouseholdItemCatalog.FlourId),
                "purchased goods remain in trip custody until home receipt");

            foreach (DueEvent ev in f.DueEvents.PopDue(departure + 1200f))
                driver.HandleDueEvent(ev, Fixture.Day, ev.DueGameSeconds, f.DueEvents, f.Diag);
            foreach (DueEvent ev in f.DueEvents.PopDue(departure + 1260f))
                driver.HandleDueEvent(ev, Fixture.Day, ev.DueGameSeconds, f.DueEvents, f.Diag);

            Assert.AreEqual(PlannedShoppingTrip.TripState.Done, trip.State);
            Assert.AreEqual(10, f.Inventories.GetOrCreate(Fixture.HouseholdId).GetTotalUnits(HouseholdItemCatalog.FlourId));
            Assert.AreEqual(0, driver.ActiveTrips.Count);
        }

        [Test]
        public void NoTeleport_MidTripInventoryUntouched()
        {
            var f = new Fixture();
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            bool observerFired = false;

            f.Loop.MidTripObserver = (result, batch) =>
            {
                observerFired = true;
                // At custody time the goods must NOT be in the household inventory yet.
                int atCustody = f.Inventories.GetOrCreate(Fixture.HouseholdId)
                    .GetAvailableUnits(HouseholdItemCatalog.FlourId);
                Assert.AreEqual(0, atCustody, "no teleport: inventory must be untouched mid-trip");
                Assert.AreEqual(GoodsCustodyState.InHand, batch.Custody);
            };

            f.Driver.PlanForHousehold(Fixture.HouseholdId, f.Needs, Fixture.Day, f.LoopOptions, f.Diag);
            f.RunUntilDone(tickSeconds: 300);

            Assert.IsTrue(observerFired, "expected the mid-trip observer to fire");
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status);
        }

        [Test]
        public void TickSizeIndependence_SmallVsLargeSteps()
        {
            var small = RunTrip(tickSeconds: 300);
            var large = RunTrip(tickSeconds: 21600);

            Assert.AreEqual(small.Balance, large.Balance, "ledger balance must match across tick sizes");
            Assert.AreEqual(small.StoreStock, large.StoreStock);
            Assert.AreEqual(small.StoreCash, large.StoreCash);
            Assert.AreEqual(small.Inventory, large.Inventory);
            Assert.AreEqual(small.NeedStatus, large.NeedStatus);
            Assert.AreEqual(small.WorkedMinutes, large.WorkedMinutes, "person work time must match");
        }

        private sealed class TripOutcome
        {
            public int Balance;
            public int StoreStock;
            public int StoreCash;
            public int Inventory;
            public PurchasingNeedStatus NeedStatus;
            public int WorkedMinutes;
        }

        private static TripOutcome RunTrip(int tickSeconds)
        {
            var f = new Fixture();
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            f.Driver.PlanForHousehold(Fixture.HouseholdId, f.Needs, Fixture.Day, f.LoopOptions, f.Diag);
            f.RunUntilDone(tickSeconds);
            return new TripOutcome
            {
                Balance = f.Ledgers.GetOrCreate(Fixture.HouseholdId).GetBalanceCents(),
                StoreStock = f.Store.Stock[HouseholdItemCatalog.FlourId],
                StoreCash = f.Store.CashReceived,
                Inventory = f.Inventories.GetOrCreate(Fixture.HouseholdId).GetAvailableUnits(HouseholdItemCatalog.FlourId),
                NeedStatus = need.Status,
                WorkedMinutes = f.Budgets.GetOrCreate(EntityId.For(EntityKind.Person, Fixture.ShopperId)).MinutesWorked,
            };
        }

        [Test]
        public void StockoutAtCounter_ReturnsEmpty_NeedStaysOpen()
        {
            var f = new Fixture();
            f.Store.Stock[HouseholdItemCatalog.FlourId] = 0; // empty shelves at the counter
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);

            f.Driver.PlanForHousehold(Fixture.HouseholdId, f.Needs, Fixture.Day, f.LoopOptions, f.Diag);
            f.RunUntilDone(tickSeconds: 300);

            Assert.AreEqual(0, f.Driver.ActiveTrips.Count);
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status, "a stockout is never a sale");
            Assert.AreEqual(50000, f.Ledgers.GetOrCreate(Fixture.HouseholdId).GetBalanceCents(), "no money moved");
            Assert.AreEqual("home", f.Locations[Fixture.ShopperId], "shopper walked home");
            Assert.IsEmpty(f.Tasks.GetAllOpenTasks().Where(task => task.DefinitionId == HouseholdShoppingLoop.ShoppingTaskId),
                "A stockout ends the trip empty-handed; its unstarted in-store task must not remain assigned.");
            Assert.IsEmpty(f.Tracker.ActiveFor(Fixture.ShopperId, Fixture.Day)
                .Where(reservation => reservation.Activity == PersonActivityKind.Shopping),
                "The unused in-store window is released while the return journey remains legitimate.");
        }

        [Test]
        public void StoreClosedAtArrival_ReturnsEmpty_NeedStaysOpen()
        {
            var f = new Fixture();
            f.Store.Closed = true;
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);

            f.Driver.PlanForHousehold(Fixture.HouseholdId, f.Needs, Fixture.Day, f.LoopOptions, f.Diag);
            f.RunUntilDone(tickSeconds: 300);

            Assert.AreEqual(0, f.Driver.ActiveTrips.Count);
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status);
            Assert.AreEqual(50000, f.Ledgers.GetOrCreate(Fixture.HouseholdId).GetBalanceCents());
            Assert.IsEmpty(f.Tasks.GetAllOpenTasks().Where(task => task.DefinitionId == HouseholdShoppingLoop.ShoppingTaskId),
                "A closed store must not leave its unstarted in-store task assigned.");
        }

        [Test]
        public void SaveLoad_MidTrip_ResumesToSameOutcome()
        {
            var f = new Fixture();
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            f.Driver.PlanForHousehold(Fixture.HouseholdId, f.Needs, Fixture.Day, f.LoopOptions, f.Diag);

            // Advance to mid-outbound, then save everything.
            float start = Fixture.Day * 86400f;
            f.Driver.AdvanceTick(start + 610 * 60f, Fixture.Day, f.DueEvents, f.Diag);
            Assert.AreEqual(1, f.Driver.ActiveTrips.Count);

            // Capture all authority state (clear-then-load).
            var tasksDto = f.Tasks.CaptureSaveDto();
            var budgetsDto = f.Budgets.CaptureSaveDto();
            var schedulesDto = f.Tracker.CaptureSaveDto();
            var driverDto = f.Driver.CaptureSaveDto();
            var dueDto = f.DueEvents.CaptureSaveDto();

            // Restore into a FRESH fixture (new authorities, same scenario wiring).
            var g = new Fixture();
            float gStart = Fixture.Day * 86400f;
            g.Tasks.LoadFromSaveDto(tasksDto);
            g.Budgets.LoadFromSaveDto(budgetsDto, Fixture.Day);
            g.Tracker.LoadFromSaveDto(schedulesDto);
            g.Driver.LoadFromSaveDto(driverDto);
            g.DueEvents.LoadFromSaveDto(dueDto);
            // The restored need object: re-point the trip at the fresh registry's need.
            HouseholdPurchasingNeed gNeed = g.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            gNeed.UnitsNeeded = need.UnitsNeeded;
            foreach (PlannedShoppingTrip t in g.Driver.ActiveTrips)
            {
                t.Need = gNeed;
            }

            g.Driver.RehydrateAfterLoad(g.Tasks, id => (IGoodsSupplier)g.Store,
                Fixture.Day, gStart + 610 * 60f, g.DueEvents, g.Diag);
            Assert.AreEqual(1, g.Driver.ActiveTrips.Count);

            // Continue the restored sim to completion.
            for (int t = 122; t < 2000 && g.Driver.ActiveTrips.Count > 0; t++)
            {
                float now = gStart + t * 300f;
                g.Driver.AdvanceTick(now, Fixture.Day, g.DueEvents, g.Diag);
                foreach (DueEvent ev in g.DueEvents.PopDue(now))
                {
                    g.Driver.HandleDueEvent(ev, Fixture.Day, ev.DueGameSeconds, g.DueEvents, g.Diag);
                }
            }

            for (int i = 0; i < 50 && (g.DueEvents.ScheduledCount > 0 || g.Driver.ActiveTrips.Count > 0); i++)
            {
                foreach (DueEvent ev in g.DueEvents.PopDue(gStart + 3 * 86400f))
                {
                    g.Driver.HandleDueEvent(ev, Fixture.Day, ev.DueGameSeconds, g.DueEvents, g.Diag);
                }
            }

            Assert.AreEqual(0, g.Driver.ActiveTrips.Count, "restored trip should finish\n" + string.Join("\n", g.Diag));
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, gNeed.Status);
            // The restored run settled through the REAL store: stock and cash moved.
            // (The fresh fixture's store is the settlement counterparty.)
            Assert.AreEqual(90, g.Store.Stock[HouseholdItemCatalog.FlourId]);
            Assert.AreEqual(1200, g.Store.CashReceived);
        }

        [Test]
        public void SaveLoad_MidTrip_NeedReunifiedWithRegistry_DeliveryDecrementsRegistryNeed()
        {
            var f = new Fixture();
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            f.Driver.PlanForHousehold(Fixture.HouseholdId, f.Needs, Fixture.Day, f.LoopOptions, f.Diag);

            // Advance to mid-outbound, then save everything (same shape as
            // SaveLoad_MidTrip_ResumesToSameOutcome, up to the restore).
            float start = Fixture.Day * 86400f;
            f.Driver.AdvanceTick(start + 610 * 60f, Fixture.Day, f.DueEvents, f.Diag);
            Assert.AreEqual(1, f.Driver.ActiveTrips.Count);

            var tasksDto = f.Tasks.CaptureSaveDto();
            var budgetsDto = f.Budgets.CaptureSaveDto();
            var schedulesDto = f.Tracker.CaptureSaveDto();
            var driverDto = f.Driver.CaptureSaveDto();
            var dueDto = f.DueEvents.CaptureSaveDto();

            // Restore into a FRESH fixture (new authorities, same scenario wiring).
            var g = new Fixture();
            float gStart = Fixture.Day * 86400f;
            g.Tasks.LoadFromSaveDto(tasksDto);
            g.Budgets.LoadFromSaveDto(budgetsDto, Fixture.Day);
            g.Tracker.LoadFromSaveDto(schedulesDto);
            g.Driver.LoadFromSaveDto(driverDto);
            g.DueEvents.LoadFromSaveDto(dueDto);
            // NOTE: deliberately NO manual re-point of trip.Need here — the
            // production fix (QA-19) must do it. The registry need is created
            // with the same (HouseholdId 7, NeedSequence 0) as the saved trip.
            HouseholdPurchasingNeed gNeed = g.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            gNeed.UnitsNeeded = need.UnitsNeeded;

            g.Driver.RehydrateAfterLoad(g.Tasks, id => (IGoodsSupplier)g.Store,
                Fixture.Day, gStart + 610 * 60f, g.DueEvents, g.Diag);
            Assert.AreEqual(1, g.Driver.ActiveTrips.Count);

            // The pin: the restored trip's Need IS the registry's need object.
            PlannedShoppingTrip restoredTrip = g.Driver.ActiveTrips[0];
            Assert.IsTrue(object.ReferenceEquals(restoredTrip.Need, gNeed),
                "restored trip's Need is still the DTO orphan, not the registry need");

            int unitsBefore = gNeed.UnitsNeeded;
            Assert.AreEqual(10, unitsBefore);

            // Continue the restored sim to completion (same drain as the
            // existing test).
            for (int t = 122; t < 2000 && g.Driver.ActiveTrips.Count > 0; t++)
            {
                float now = gStart + t * 300f;
                g.Driver.AdvanceTick(now, Fixture.Day, g.DueEvents, g.Diag);
                foreach (DueEvent ev in g.DueEvents.PopDue(now))
                {
                    g.Driver.HandleDueEvent(ev, Fixture.Day, ev.DueGameSeconds, g.DueEvents, g.Diag);
                }
            }

            for (int i = 0; i < 50 && (g.DueEvents.ScheduledCount > 0 || g.Driver.ActiveTrips.Count > 0); i++)
            {
                foreach (DueEvent ev in g.DueEvents.PopDue(gStart + 3 * 86400f))
                {
                    g.Driver.HandleDueEvent(ev, Fixture.Day, ev.DueGameSeconds, g.DueEvents, g.Diag);
                }
            }

            Assert.AreEqual(0, g.Driver.ActiveTrips.Count, "restored trip should finish\n" + string.Join("\n", g.Diag));
            // The delivery actually happened through the real store...
            Assert.AreEqual(90, g.Store.Stock[HouseholdItemCatalog.FlourId]);
            Assert.AreEqual(1200, g.Store.CashReceived);
            // ...and the REGISTRY need shrank by the delivered units, to zero.
            Assert.AreEqual(0, gNeed.UnitsNeeded,
                "registry need did not shrink: delivery decremented the orphan (got " + gNeed.UnitsNeeded + ", expected 0)");
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, gNeed.Status);
        }

        // ---- Save/Continue during an active trip (this slice, F1/F2) ----
        //
        // The Fixture's driver has null route delegates, so the existing
        // SaveLoad tests never take the post-load route branch: in
        // production the pathing director exists and reports NO active
        // route after a load, which aborted every mid-route trip at its
        // first leg due. These tests model that post-load state with
        // mutable delegates (no route active until one is (re)started)
        // and pin the rehydrate route restart and the recovery
        // persistence.

        [Test]
        public void SaveLoad_MidOutbound_PostLoadRouteRestart_ResumesToSameOutcome()
        {
            var f = new Fixture();
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            f.Driver.PlanForHousehold(Fixture.HouseholdId, f.Needs, Fixture.Day, f.LoopOptions, f.Diag);

            // Advance to mid-outbound, then save everything.
            float start = Fixture.Day * 86400f;
            f.Driver.AdvanceTick(start + 610 * 60f, Fixture.Day, f.DueEvents, f.Diag);
            Assert.AreEqual(1, f.Driver.ActiveTrips.Count);
            Assert.AreEqual(PlannedShoppingTrip.TripState.Outbound, f.Driver.ActiveTrips[0].State);

            var tasksDto = f.Tasks.CaptureSaveDto();
            var budgetsDto = f.Budgets.CaptureSaveDto();
            var schedulesDto = f.Tracker.CaptureSaveDto();
            var driverDto = f.Driver.CaptureSaveDto();
            var dueDto = f.DueEvents.CaptureSaveDto();

            // Restore into a FRESH fixture under a driver modelling the
            // post-load production routes: no route is active until one is
            // (re)started, and arrival is a separate flag.
            var g = new Fixture();
            float gStart = Fixture.Day * 86400f;
            bool routeActive = false;
            bool arrived = false;
            var gDriver = new ShoppingTripDriver(g.Loop,
                (p, l) => { routeActive = true; return true; },
                (p, l) => arrived,
                (p, l) => routeActive,
                (p, l) => routeActive ? (float?)300f : null,
                (p, l) => { routeActive = false; },
                60f);
            g.Tasks.LoadFromSaveDto(tasksDto);
            g.Budgets.LoadFromSaveDto(budgetsDto, Fixture.Day);
            g.Tracker.LoadFromSaveDto(schedulesDto);
            g.DueEvents.LoadFromSaveDto(dueDto);
            gDriver.LoadFromSaveDto(driverDto);
            HouseholdPurchasingNeed gNeed = g.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            gNeed.UnitsNeeded = need.UnitsNeeded;

            gDriver.RehydrateAfterLoad(g.Tasks, id => (IGoodsSupplier)g.Store,
                Fixture.Day, gStart + 610 * 60f, g.DueEvents, g.Diag);
            Assert.AreEqual(1, gDriver.ActiveTrips.Count);
            // The pin: rehydrate restarted the physical route that the
            // save could not carry.
            Assert.IsTrue(routeActive, "rehydrate did not restart the outbound route\n" + string.Join("\n", g.Diag));

            // Pop the outbound leg due while the shopper has not arrived:
            // the trip must retry on its live route, not abort.
            PlannedShoppingTrip restored = gDriver.ActiveTrips[0];
            DueEvent outboundDue = null;
            foreach (DueEvent ev in g.DueEvents.PopDue(gStart + 86400f))
            {
                if (ev.PayloadRef == restored.Legs.Outbound.TaskId.ToString()) outboundDue = ev;
            }
            Assert.IsNotNull(outboundDue, "outbound leg due not restored");
            gDriver.HandleDueEvent(outboundDue, Fixture.Day, outboundDue.DueGameSeconds, g.DueEvents, g.Diag);
            Assert.AreEqual(PlannedShoppingTrip.TripState.Outbound, restored.State);
            Assert.AreEqual(1, gDriver.ActiveTrips.Count);

            // Arrival on the retry due: the trip proceeds to settlement.
            arrived = true;
            foreach (DueEvent ev in g.DueEvents.PopDue(outboundDue.DueGameSeconds + 61f))
            {
                gDriver.HandleDueEvent(ev, Fixture.Day, ev.DueGameSeconds, g.DueEvents, g.Diag);
            }

            // Drain to completion (same shape as the existing test).
            for (int t = 122; t < 2000 && gDriver.ActiveTrips.Count > 0; t++)
            {
                float now = gStart + t * 300f;
                gDriver.AdvanceTick(now, Fixture.Day, g.DueEvents, g.Diag);
                foreach (DueEvent ev in g.DueEvents.PopDue(now))
                {
                    gDriver.HandleDueEvent(ev, Fixture.Day, ev.DueGameSeconds, g.DueEvents, g.Diag);
                }
            }
            for (int i = 0; i < 50 && (g.DueEvents.ScheduledCount > 0 || gDriver.ActiveTrips.Count > 0); i++)
            {
                foreach (DueEvent ev in g.DueEvents.PopDue(gStart + 3 * 86400f))
                {
                    gDriver.HandleDueEvent(ev, Fixture.Day, ev.DueGameSeconds, g.DueEvents, g.Diag);
                }
            }

            Assert.AreEqual(0, gDriver.ActiveTrips.Count, "restored trip should finish\n" + string.Join("\n", g.Diag));
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, gNeed.Status);
            Assert.AreEqual(90, g.Store.Stock[HouseholdItemCatalog.FlourId]);
            Assert.AreEqual(1200, g.Store.CashReceived);
        }

        [Test]
        public void SaveLoad_MidReturn_PostLoadRouteRestart_ReceivesAtHome()
        {
            var f = new Fixture();
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            PlannedShoppingTrip trip = DriveToArrival(f, out float arrivalGameSeconds);
            Assert.AreEqual(PlannedShoppingTrip.TripState.InStore, trip.State);
            Assert.IsNotNull(trip.Batch);
            Assert.AreEqual(1, f.Loop.ActiveCustodyBatches.Count);
            Assert.AreEqual(1200, f.Store.CashReceived);

            // Complete the in-store leg so the return leg is airborne with
            // the paid batch in hand, then save mid-return.
            DueEvent inStoreDue = null;
            foreach (DueEvent ev in f.DueEvents.PopDue(arrivalGameSeconds + 86400f))
            {
                if (ev.PayloadRef == trip.Legs.InStore.TaskId.ToString()) inStoreDue = ev;
            }
            Assert.IsNotNull(inStoreDue, "in-store leg due not scheduled");
            f.Driver.HandleDueEvent(inStoreDue, Fixture.Day, inStoreDue.DueGameSeconds, f.DueEvents, f.Diag);
            Assert.AreEqual(PlannedShoppingTrip.TripState.Returning, trip.State);
            float savedAt = inStoreDue.DueGameSeconds;

            var tasksDto = f.Tasks.CaptureSaveDto();
            var budgetsDto = f.Budgets.CaptureSaveDto();
            var schedulesDto = f.Tracker.CaptureSaveDto();
            var driverDto = f.Driver.CaptureSaveDto();
            var dueDto = f.DueEvents.CaptureSaveDto();
            var custodyDto = f.Loop.CaptureCustodySaveDto();

            var g = new Fixture();
            float gStart = Fixture.Day * 86400f;
            bool routeActive = false;
            bool arrived = false;
            var gDriver = new ShoppingTripDriver(g.Loop,
                (p, l) => { routeActive = true; return true; },
                (p, l) => arrived,
                (p, l) => routeActive,
                (p, l) => routeActive ? (float?)300f : null,
                (p, l) => { routeActive = false; },
                60f);
            g.Tasks.LoadFromSaveDto(tasksDto);
            g.Budgets.LoadFromSaveDto(budgetsDto, Fixture.Day);
            g.Tracker.LoadFromSaveDto(schedulesDto);
            g.DueEvents.LoadFromSaveDto(dueDto);
            g.Loop.LoadCustodyFromSaveDto(custodyDto);
            gDriver.LoadFromSaveDto(driverDto);
            HouseholdPurchasingNeed gNeed = g.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            gNeed.UnitsNeeded = need.UnitsNeeded;

            gDriver.RehydrateAfterLoad(g.Tasks, id => (IGoodsSupplier)g.Store,
                Fixture.Day, savedAt, g.DueEvents, g.Diag);
            Assert.AreEqual(1, gDriver.ActiveTrips.Count);
            Assert.IsTrue(routeActive, "rehydrate did not restart the return route\n" + string.Join("\n", g.Diag));

            PlannedShoppingTrip restored = gDriver.ActiveTrips[0];
            Assert.AreEqual(PlannedShoppingTrip.TripState.Returning, restored.State);
            DueEvent returnDue = null;
            foreach (DueEvent ev in g.DueEvents.PopDue(gStart + 86400f))
            {
                if (ev.PayloadRef == restored.Legs.Return.TaskId.ToString()) returnDue = ev;
            }
            Assert.IsNotNull(returnDue, "return leg due not restored");
            gDriver.HandleDueEvent(returnDue, Fixture.Day, returnDue.DueGameSeconds, g.DueEvents, g.Diag);
            Assert.AreEqual(PlannedShoppingTrip.TripState.Returning, restored.State);
            Assert.AreEqual(1, gDriver.ActiveTrips.Count);

            arrived = true;
            foreach (DueEvent ev in g.DueEvents.PopDue(returnDue.DueGameSeconds + 61f))
            {
                gDriver.HandleDueEvent(ev, Fixture.Day, ev.DueGameSeconds, g.DueEvents, g.Diag);
            }

            Assert.AreEqual(0, gDriver.ActiveTrips.Count, "restored trip should finish\n" + string.Join("\n", g.Diag));
            Assert.AreEqual(PlannedShoppingTrip.TripState.Done, restored.State);
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, gNeed.Status);
            Assert.AreEqual(10, g.Inventories.GetOrCreate(Fixture.HouseholdId).GetTotalUnits(HouseholdItemCatalog.FlourId));
            Assert.AreEqual(0, g.Loop.ActiveCustodyBatches.Count);
            // Settlement ran before the save (f.Store, 1200c): the restored
            // run must NOT settle again — no second charge, no second sale.
            Assert.AreEqual(0, g.Store.CashReceived);
            Assert.AreEqual(100, g.Store.Stock[HouseholdItemCatalog.FlourId]);
        }

        [Test]
        public void SaveLoad_PaidReturnRecovery_PersistsAndCompletes()
        {
            var f = new Fixture();
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            bool returnAvailable = false;
            string physicalLocation = "home";
            var driver = new ShoppingTripDriver(f.Loop,
                (p, l) => l != "home" || returnAvailable,
                (p, l) => l == physicalLocation,
                (p, l) => true);
            driver.PlanForHousehold(Fixture.HouseholdId, f.Needs, Fixture.Day, f.LoopOptions, f.Diag);
            PlannedShoppingTrip trip = driver.ActiveTrips[0];
            float departure = Fixture.Day * 86400f + trip.DepartureMinuteOfDay * 60f;
            driver.AdvanceTick(departure, Fixture.Day, f.DueEvents, f.Diag);
            physicalLocation = "store";
            DueEvent outbound = f.DueEvents.PopDue(departure + 86400f)[0];
            driver.HandleDueEvent(outbound, Fixture.Day, outbound.DueGameSeconds, f.DueEvents, f.Diag);
            DueEvent inStore = f.DueEvents.PopDue(outbound.DueGameSeconds + 86400f)[0];
            driver.HandleDueEvent(inStore, Fixture.Day, inStore.DueGameSeconds, f.DueEvents, f.Diag);
            // Return route unavailable: abort, paid batch queued for recovery.
            Assert.AreEqual(PlannedShoppingTrip.TripState.Failed, trip.State);
            Assert.AreEqual(0, driver.ActiveTrips.Count);
            Assert.AreEqual(1200, f.Store.CashReceived);
            Assert.AreEqual(90, f.Store.Stock[HouseholdItemCatalog.FlourId]);
            Assert.AreEqual(1, f.Loop.ActiveCustodyBatches.Count);
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status);

            var tasksDto = f.Tasks.CaptureSaveDto();
            var budgetsDto = f.Budgets.CaptureSaveDto();
            var schedulesDto = f.Tracker.CaptureSaveDto();
            var driverDto = driver.CaptureSaveDto();
            var dueDto = f.DueEvents.CaptureSaveDto();
            var custodyDto = f.Loop.CaptureCustodySaveDto();

            // Restore under post-load delegates: the holder is at the store.
            var g = new Fixture();
            var gDriver = new ShoppingTripDriver(g.Loop,
                (p, l) => l != "home" || returnAvailable,
                (p, l) => l == physicalLocation,
                (p, l) => true,
                (p, l) => (float?)300f,
                null,
                60f);
            g.Tasks.LoadFromSaveDto(tasksDto);
            g.Budgets.LoadFromSaveDto(budgetsDto, Fixture.Day);
            g.Tracker.LoadFromSaveDto(schedulesDto);
            g.DueEvents.LoadFromSaveDto(dueDto);
            g.Loop.LoadCustodyFromSaveDto(custodyDto);
            gDriver.LoadFromSaveDto(driverDto);
            Assert.IsTrue(gDriver.NeedsRehydrateAfterLoad, "recovery-only save must still rehydrate");
            HouseholdPurchasingNeed gNeed = g.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            gNeed.UnitsNeeded = need.UnitsNeeded;
            gDriver.RehydrateAfterLoad(g.Tasks, id => (IGoodsSupplier)g.Store,
                Fixture.Day, Fixture.Day * 86400f + 610 * 60f, g.DueEvents, g.Diag);
            Assert.IsFalse(gDriver.NeedsRehydrateAfterLoad);

            // Next day: the pending recovery is planned (not a fresh purchase).
            returnAvailable = true;
            g.Budgets.EnsureDay(Fixture.Day + 1);
            gDriver.PlanForHousehold(Fixture.HouseholdId, g.Needs, Fixture.Day + 1, g.LoopOptions, g.Diag);
            Assert.AreEqual(1, gDriver.ActiveTrips.Count, "paid recovery was lost across save/load\n" + string.Join("\n", g.Diag));
            PlannedShoppingTrip recovery = gDriver.ActiveTrips[0];
            Assert.AreEqual(PlannedShoppingTrip.TripState.RecoveryPlanned, recovery.State);
            Assert.AreEqual(trip.TripId, recovery.TripId);

            float recoveryDeparture = (Fixture.Day + 1) * 86400f + recovery.DepartureMinuteOfDay * 60f;
            gDriver.AdvanceTick(recoveryDeparture, Fixture.Day + 1, g.DueEvents, g.Diag);
            DueEvent returnDue = null;
            foreach (DueEvent ev in g.DueEvents.PopDue(recoveryDeparture + 86400f))
            {
                if (ev.PayloadRef == recovery.Legs.Return.TaskId.ToString()) returnDue = ev;
            }
            Assert.IsNotNull(returnDue, "recovery return leg due not scheduled");
            gDriver.HandleDueEvent(returnDue, Fixture.Day + 1, returnDue.DueGameSeconds, g.DueEvents, g.Diag);
            // The return leg is airborne now (BeginReturnLeg set Returning
            // at departure); the retry below is the arrival gate, not a
            // second departure.
            Assert.AreEqual(PlannedShoppingTrip.TripState.Returning, recovery.State);
            physicalLocation = "home";
            foreach (DueEvent ev in g.DueEvents.PopDue(returnDue.DueGameSeconds + 61f))
            {
                gDriver.HandleDueEvent(ev, Fixture.Day + 1, ev.DueGameSeconds, g.DueEvents, g.Diag);
            }

            Assert.AreEqual(0, gDriver.ActiveTrips.Count, "recovery trip should finish\n" + string.Join("\n", g.Diag));
            Assert.AreEqual(PlannedShoppingTrip.TripState.Done, recovery.State);
            Assert.AreEqual(10, g.Inventories.GetOrCreate(Fixture.HouseholdId).GetTotalUnits(HouseholdItemCatalog.FlourId));
            Assert.AreEqual(0, g.Loop.ActiveCustodyBatches.Count);
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, gNeed.Status);
            // Settlement ran before the save (f.Store, 1200c): the recovery
            // must NOT charge again — no second charge, no second sale.
            Assert.AreEqual(0, g.Store.CashReceived);
            Assert.AreEqual(100, g.Store.Stock[HouseholdItemCatalog.FlourId]);
        }

        // ---- Plan 16: General Store employee service (sub-items 1-4) ----
        //
        // The new production members (loop resolver properties, trip
        // ServiceTaskId / ServiceTaskPlannedMinutes) are reached through
        // reflection here so this file still compiles against unmodified
        // production code: the red-first run must show per-test failures,
        // not a whole-fixture compile error. TaskAuthority is public, so
        // service-task lookup/completion in test setup uses f.Tasks
        // directly; the loop's FindServiceTask / CompleteServiceTask are
        // still exercised through the driver in tests 2 and 7.

        private static void AddClerk(Fixture f)
        {
            f.Population.people.Add(new PersonState { id = 3, firstName = "Clerk", lastName = "Test", age = 30, ageBand = AgeBand.Adult18Plus });
            f.Budgets.EnsureDay(Fixture.Day);
        }

        private static CustomerQueue WireService(Fixture f, LostSaleLog log, int clerkPersonId)
        {
            var queue = new CustomerQueue(log);
            // The fixture registers only the shopping leg definitions
            // (HouseholdShoppingLoop.RegisterTaskDefinitions); the store
            // catalog is registered at boot in production
            // (SimulationSystemsHub.cs:1063), so register it here.
            GeneralStoreTaskCatalog.RegisterAll(f.Tasks);
            System.Reflection.PropertyInfo queueProp = typeof(HouseholdShoppingLoop).GetProperty("ResolveCustomerQueue");
            Assert.IsNotNull(queueProp, "Plan 16 ResolveCustomerQueue property missing");
            queueProp.SetValue(f.Loop, (System.Func<string, CustomerQueue>)(businessId => businessId == "store-1" ? queue : null));
            System.Reflection.PropertyInfo clerkProp = typeof(HouseholdShoppingLoop).GetProperty("ResolveServiceClerkPersonId");
            Assert.IsNotNull(clerkProp, "Plan 16 ResolveServiceClerkPersonId property missing");
            clerkProp.SetValue(f.Loop, (System.Func<string, int>)(businessId => businessId == "store-1" ? clerkPersonId : -1));
            return queue;
        }

        private static bool IsAwaitingService(PlannedShoppingTrip trip)
        {
            // Red-first: AwaitingService does not exist in unmodified
            // production, so the state is compared by name — this compiles
            // before and after the slice (Plan 16 reflection precedent).
            return trip.State.ToString() == "AwaitingService";
        }

        private static EntityId ServiceTaskIdOf(PlannedShoppingTrip trip)
        {
            // ServiceTaskId is internal: plain GetField only finds public
            // fields, so request non-public instance fields explicitly.
            System.Reflection.FieldInfo field = typeof(PlannedShoppingTrip).GetField("ServiceTaskId",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(field, "Plan 16 ServiceTaskId field missing");
            return (EntityId)field.GetValue(trip);
        }

        private static int ServiceTaskPlannedMinutesOf(PlannedShoppingTrip trip)
        {
            System.Reflection.FieldInfo field = typeof(PlannedShoppingTrip).GetField("ServiceTaskPlannedMinutes",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(field, "Plan 16 ServiceTaskPlannedMinutes field missing");
            return (int)field.GetValue(trip);
        }

        private static PlannedShoppingTrip DriveToArrival(Fixture f, out float arrivalGameSeconds, Action afterPlan = null)
        {
            f.Driver.PlanForHousehold(Fixture.HouseholdId, f.Needs, Fixture.Day, f.LoopOptions, f.Diag);
            Assert.AreEqual(1, f.Driver.ActiveTrips.Count);
            if (afterPlan != null) afterPlan();
            PlannedShoppingTrip trip = f.Driver.ActiveTrips[0];
            float departure = Fixture.Day * 86400f + trip.DepartureMinuteOfDay * 60f;
            f.Driver.AdvanceTick(departure, Fixture.Day, f.DueEvents, f.Diag);
            DueEvent outbound = null;
            foreach (DueEvent ev in f.DueEvents.PopDue(departure + 86400f))
            {
                if (ev.PayloadRef == trip.Legs.Outbound.TaskId.ToString())
                {
                    outbound = ev;
                    break;
                }
            }

            Assert.IsNotNull(outbound, "outbound leg due event not scheduled");
            arrivalGameSeconds = outbound.DueGameSeconds;
            f.Driver.HandleDueEvent(outbound, Fixture.Day, outbound.DueGameSeconds, f.DueEvents, f.Diag);
            return trip;
        }

        private static DueEvent PopServiceDueEvent(Fixture f, PlannedShoppingTrip trip, float arrivalGameSeconds)
        {
            float serviceDue = arrivalGameSeconds + ServiceTaskPlannedMinutesOf(trip) * 60f;
            DueEvent serviceEv = null;
            foreach (DueEvent ev in f.DueEvents.PopDue(serviceDue + 1f))
            {
                if (ev.PayloadRef == ServiceTaskIdOf(trip).ToString())
                {
                    serviceEv = ev;
                }
                else
                {
                    f.Driver.HandleDueEvent(ev, Fixture.Day, ev.DueGameSeconds, f.DueEvents, f.Diag);
                }
            }

            return serviceEv;
        }

        [Test]
        public void Arrival_EnqueuesShopperInStoreQueue()
        {
            var f = new Fixture();
            var log = new LostSaleLog();
            AddClerk(f);
            CustomerQueue queue = WireService(f, log, 3);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            PlannedShoppingTrip trip = DriveToArrival(f, out float arrival);
            // Slice 17: settlement waits for service completion — nothing
            // settles at arrival when a service task was started.
            Assert.AreEqual(0, f.Store.CashReceived);
            Assert.AreEqual(1, queue.WaitingCount);
            WaitingCustomer waiting = queue.Waiting[0];
            // Plan 16 §1.3: the ref reuses the in-store leg format
            // shop:{HouseholdId}:{NeedSequence} (fixture household is 7).
            Assert.AreEqual($"shop:{Fixture.HouseholdId}:{need.NeedSequence}", waiting.CustomerRef);
            Assert.AreEqual(trip.ShopperPersonId, waiting.ActingPersonId);
            Assert.AreEqual("staple_food", waiting.CategoryId);
            Assert.AreEqual(trip.Candidate.UnitsPlanned, waiting.UnitsWanted);
            Assert.AreEqual(trip.Candidate.UnitPriceCents, waiting.PriceCentsPerUnit);
            Assert.AreEqual(CustomerQueue.DefaultPatienceMinutes, waiting.PatienceMinutesRemaining);
            Assert.AreEqual(Fixture.Day, waiting.QueuedDayIndex);
            Assert.IsTrue(ServiceTaskIdOf(trip).IsValid);
            WorkTask serviceTask = f.Tasks.FindTask(ServiceTaskIdOf(trip));
            Assert.IsNotNull(serviceTask);
            Assert.AreEqual(GeneralStoreTaskCatalog.TendCustomerId, serviceTask.DefinitionId);
            // Slice 17: the 1200 lands after the service due, not at arrival.
            DueEvent serviceEv = PopServiceDueEvent(f, trip, arrival);
            Assert.IsNotNull(serviceEv, "service due event not scheduled");
            f.Driver.HandleDueEvent(serviceEv, Fixture.Day, serviceEv.DueGameSeconds, f.DueEvents, f.Diag);
            Assert.AreEqual(1200, f.Store.CashReceived);
        }

        [Test]
        public void ServiceTaskCompletion_PopsQueue()
        {
            var f = new Fixture();
            var log = new LostSaleLog();
            AddClerk(f);
            CustomerQueue queue = WireService(f, log, 3);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            PlannedShoppingTrip trip = DriveToArrival(f, out float arrival);
            string servedRef = queue.Waiting[0].CustomerRef;
            Assert.AreEqual($"shop:{Fixture.HouseholdId}:{need.NeedSequence}", servedRef);
            DueEvent serviceEv = PopServiceDueEvent(f, trip, arrival);
            Assert.IsNotNull(serviceEv, "service due event not scheduled");
            f.Driver.HandleDueEvent(serviceEv, Fixture.Day, serviceEv.DueGameSeconds, f.DueEvents, f.Diag);
            Assert.AreEqual(TaskStatus.Complete, f.Tasks.FindTask(ServiceTaskIdOf(trip)).Status);
            Assert.AreEqual(0, queue.WaitingCount);
            StringAssert.Contains(servedRef, string.Join("\n", f.Diag));
        }

        [Test]
        public void NoClerk_EnqueuesWithoutTask()
        {
            var f = new Fixture();
            var log = new LostSaleLog();
            CustomerQueue queue = WireService(f, log, -1);
            f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            PlannedShoppingTrip trip = DriveToArrival(f, out _);
            Assert.AreEqual(1, queue.WaitingCount);
            Assert.IsTrue(!ServiceTaskIdOf(trip).IsValid);
            StringAssert.Contains("no clerk", string.Join("\n", f.Diag));
        }

        [Test]
        public void QueueFull_SkipsEnqueueWithoutLostSale()
        {
            var f = new Fixture();
            var log = new LostSaleLog();
            AddClerk(f);
            CustomerQueue queue = WireService(f, log, 3);
            for (int i = 0; i < 5; i++)
            {
                Assert.IsTrue(queue.Enqueue("prefill-" + i, Fixture.ShopperId, "staple_food", 1, 120, Fixture.Day));
            }

            f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            PlannedShoppingTrip trip = DriveToArrival(f, out _);
            Assert.AreEqual(5, queue.WaitingCount);
            for (int i = 0; i < 5; i++)
            {
                Assert.AreEqual("prefill-" + i, queue.Waiting[i].CustomerRef);
            }

            Assert.AreEqual(0, log.Count);
            Assert.IsTrue(!ServiceTaskIdOf(trip).IsValid);
            StringAssert.Contains("queue full", string.Join("\n", f.Diag));
        }

        private sealed class EmptyCategoryStore : IItemResolvingSupplier
        {
            public string SupplierBusinessId { get; set; } = "store-1";
            public string SupplierName { get; set; } = "General Store";
            public string LocationId { get; set; } = "store";
            public Dictionary<string, int> Stock = new Dictionary<string, int>();
            public Dictionary<string, int> Prices = new Dictionary<string, int>();
            public int CashReceived;
            public string LastReceiptIdValue = string.Empty;
            public int ReceiptSequence;
            public bool Closed;

            public bool HasCategory(string categoryId) => true;
            public int StockUnits(string categoryId) => 0;
            public int PricePerUnitCents(string categoryId) => 0;
            public int Sell(string categoryId, int requestedUnits, int dayIndex, List<string> diagnostics) => 0;

            public bool OffersItem(string itemId) => Stock.ContainsKey(itemId);
            public string CategoryForItem(string itemId) => string.Empty;
            public int ItemStockUnits(string itemId) => Stock.TryGetValue(itemId, out int s) ? s : 0;
            public int ItemPricePerUnitCents(string itemId) => Prices.TryGetValue(itemId, out int p) ? p : 0;
            public string CheckTradingCapability(int absoluteDayIndex, int minuteOfDay) =>
                Closed ? "closed for the night" : null;

            public string LastReceiptId => LastReceiptIdValue;

            public int SellItem(string itemId, int requestedUnits, int dayIndex, ShoppingSettlement settlement, List<string> diagnostics)
            {
                int have = ItemStockUnits(itemId);
                int sold = System.Math.Min(System.Math.Max(0, requestedUnits), have);
                if (sold > 0)
                {
                    Stock[itemId] = have - sold;
                    LastReceiptIdValue = $"rcpt-{++ReceiptSequence}";
                    if (settlement != null && settlement.Mode == ShoppingSettlementMode.CashFromLedger)
                    {
                        CashReceived += settlement.AmountCents;
                    }
                }

                return sold;
            }
        }

        [Test]
        public void UnresolvableCategory_SkipsEnqueue()
        {
            var f = new Fixture();
            var log = new LostSaleLog();
            AddClerk(f);
            CustomerQueue queue = WireService(f, log, 3);
            var emptyStore = new EmptyCategoryStore();
            emptyStore.Stock[HouseholdItemCatalog.FlourId] = 100;
            emptyStore.Prices[HouseholdItemCatalog.FlourId] = 120;
            f.Directory.Register(emptyStore);
            f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            PlannedShoppingTrip trip = DriveToArrival(f, out _);
            Assert.AreEqual(0, queue.WaitingCount);
            Assert.IsTrue(!ServiceTaskIdOf(trip).IsValid);
            Assert.AreEqual(1200, emptyStore.CashReceived);
            Assert.AreEqual(90, emptyStore.Stock[HouseholdItemCatalog.FlourId]);
        }

        [Test]
        public void NullResolvers_BehaveAsToday()
        {
            var f = new Fixture();
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            f.Driver.PlanForHousehold(Fixture.HouseholdId, f.Needs, Fixture.Day, f.LoopOptions, f.Diag);
            Assert.AreEqual(1, f.Driver.ActiveTrips.Count);
            f.RunUntilDone(tickSeconds: 300);
            Assert.AreEqual(0, f.Driver.ActiveTrips.Count, "trip should be done\n" + string.Join("\n", f.Diag));
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status);
            Assert.AreEqual(50000 - 1200, f.Ledgers.GetOrCreate(Fixture.HouseholdId).GetBalanceCents());
            Assert.AreEqual(1200, f.Store.CashReceived);
            Assert.AreEqual(90, f.Store.Stock[HouseholdItemCatalog.FlourId]);
        }

        [Test]
        public void ServiceCompletionRefusal_SettlesAnywayAndPopsQueue()
        {
            var f = new Fixture();
            var log = new LostSaleLog();
            AddClerk(f);
            CustomerQueue queue = WireService(f, log, 3);
            f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            PlannedShoppingTrip trip = DriveToArrival(f, out float arrival);
            Assert.AreEqual(1, queue.WaitingCount);
            WorkTask task = f.Tasks.FindTask(ServiceTaskIdOf(trip));
            Assert.IsNotNull(task);
            int remaining = System.Math.Max(1, task.MinutesRemaining);
            Assert.IsTrue(f.Tasks.RecordWorkProgress(task.TaskId, remaining, f.Budgets, Fixture.Day, out bool reached, out string rejection), rejection);
            Assert.IsTrue(reached);
            Assert.IsTrue(f.Tasks.CompleteTask(task.TaskId, TaskOutcome.Success, Fixture.Day, f.Budgets));
            DueEvent serviceEv = PopServiceDueEvent(f, trip, arrival);
            Assert.IsNotNull(serviceEv, "service due event not scheduled");
            f.Driver.HandleDueEvent(serviceEv, Fixture.Day, serviceEv.DueGameSeconds, f.DueEvents, f.Diag);
            // Slice 17 (d1, ruled by note 79): the failure is task
            // bookkeeping, not a customer refusal — settle anyway and pop
            // the queue, as on success. (Plan 22 §12 named one existing-test
            // update; this is the second, forced by the same ruling — the
            // old assertion pinned the pre-gate "customer stays queued"
            // disposition that (d1) replaces. Flagged for Agent 3.)
            Assert.AreEqual(0, queue.WaitingCount);
            Assert.AreEqual(TaskStatus.Complete, f.Tasks.FindTask(ServiceTaskIdOf(trip)).Status);
            Assert.IsNotNull(trip.Batch);
            Assert.AreEqual(1200, f.Store.CashReceived);
            Assert.AreEqual(PlannedShoppingTrip.TripState.InStore, trip.State);
            StringAssert.Contains("did not complete", string.Join("\n", f.Diag));
        }

        // ---- Plan 20: Plan 16 follow-ups (CancelTask disposal) ----
        //
        // T1/T2 drive the arrival path with a clerk whose budget is
        // exhausted (T1) or who is already occupied (T2); the created
        // TendCustomer task is found through the authority's flat task
        // list because the refused task never gets a stored handle
        // (trip.ServiceTaskId is only set on start).
        //
        // T3-T5 as filed (suspended-skip, unpaid-skip, unparseable-skip
        // on TryGetServiceClerkPersonId) CANNOT run in the Roslyn
        // harness: GeneralStoreRuntimeManager.cs is harness-excluded and
        // cannot be compiled in — it needs twelve unshimmable
        // scene-runtime types (TownWorldController,
        // TownPulseRuntimeManager, PopulationManager,
        // PlayerPortfolioManager, LogisticsRuntimeManager,
        // LandLedgersHUDController, SharedBusinessRuntimeManager,
        // GeneralStoreTradingPort, GeneralStoreSaveDto,
        // HouseholdAffinityEvaluator, HouseholdBusinessAffinityState,
        // BusinessCashAutoTransferMode). Every test file that directly
        // references the manager is likewise harness-excluded. The
        // T3-T5 bodies are preserved in the session checkpoint for an
        // Agent 3 ruling on placement (Unity-only test file vs dropped);
        // items (b)/(c) below are implemented as ruled and verified by
        // source inspection plus the full harness, NOT by a test.

        private static WorkTask FindTendCustomerTask(Fixture f)
        {
            // TaskAuthority keeps a flat list of every task it created;
            // the refused TendCustomer task is reachable here in both
            // the red state (Queued/Assigned, still open) and the green
            // state (Cancelled) because GetOpenTasks only returns open
            // tasks.
            System.Reflection.FieldInfo field = typeof(TaskAuthority).GetField("tasks",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(field, "Plan 20 TaskAuthority.tasks field missing");
            var all = (System.Collections.Generic.List<WorkTask>)field.GetValue(f.Tasks);
            foreach (WorkTask t in all)
            {
                if (t.DefinitionId == GeneralStoreTaskCatalog.TendCustomerId)
                {
                    return t;
                }
            }

            return null;
        }

        [Test]
        public void AssignRefusedTask_IsCancelled_CustomerStaysQueued()
        {
            var f = new Fixture();
            var log = new LostSaleLog();
            AddClerk(f);
            CustomerQueue queue = WireService(f, log, 3);
            // Exhaust the clerk's whole day budget (600 minutes) so the
            // TendCustomer AssignTask refuses.
            EntityId clerk = EntityId.For(EntityKind.Person, 3);
            Assert.IsTrue(f.Budgets.GetOrCreate(clerk).TryCommit(600, out _), "clerk budget setup failed");
            Assert.AreEqual(0, f.Budgets.GetOrCreate(clerk).MinutesRemaining);
            f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            PlannedShoppingTrip trip = DriveToArrival(f, out _);
            Assert.AreEqual(1, queue.WaitingCount);
            Assert.IsTrue(!ServiceTaskIdOf(trip).IsValid);
            StringAssert.Contains("assign refused", string.Join("\n", f.Diag));
            WorkTask serviceTask = FindTendCustomerTask(f);
            Assert.IsNotNull(serviceTask, "no TendCustomer task was created");
            Assert.AreEqual(TaskStatus.Cancelled, serviceTask.Status);
        }

        [Test]
        public void StartRefusedTask_IsCancelled_AndBudgetReleased()
        {
            var f = new Fixture();
            var log = new LostSaleLog();
            AddClerk(f);
            CustomerQueue queue = WireService(f, log, 3);
            EntityId clerk = EntityId.For(EntityKind.Person, 3);
            // Occupy the clerk first: an InProgress task, so the
            // TendCustomer StartTask refuses on occupancy.
            WorkTask occupying = f.Tasks.CreateTaskWithPlannedMinutes(HouseholdShoppingLoop.ShoppingTaskId, clerk, Fixture.Day, 10);
            Assert.IsTrue(f.Tasks.AssignTask(occupying.TaskId, clerk, f.Budgets, out string assignRejection), assignRejection);
            Assert.IsTrue(f.Tasks.StartTask(occupying.TaskId, Fixture.Day, out string startRejection), startRejection);
            int committedBefore = f.Budgets.GetOrCreate(clerk).MinutesCommitted;
            Assert.Greater(committedBefore, 0);
            f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            PlannedShoppingTrip trip = DriveToArrival(f, out _);
            Assert.AreEqual(1, queue.WaitingCount);
            Assert.IsTrue(!ServiceTaskIdOf(trip).IsValid);
            StringAssert.Contains("start refused", string.Join("\n", f.Diag));
            WorkTask serviceTask = FindTendCustomerTask(f);
            Assert.IsNotNull(serviceTask, "no TendCustomer task was created");
            Assert.AreEqual(TaskStatus.Cancelled, serviceTask.Status);
            Assert.AreEqual(committedBefore, f.Budgets.GetOrCreate(clerk).MinutesCommitted);
        }

        [Test]
        public void StoreBusinessEntityId_NeverFreightThree()
        {
            // Moved here from GeneralStoreTaskCatalogTests.cs per
            // coordinator Decision B (Plan 16 §3): that file is
            // harness-excluded, so the test could never appear in a
            // full log. Reached through reflection so this file still
            // compiles against unmodified production code (red-first):
            // the helper does not exist yet before Plan 16.
            System.Reflection.MethodInfo helper = typeof(GeneralStoreTaskCatalog).GetMethod("StoreBusinessEntityId");
            Assert.IsNotNull(helper, "Plan 16 StoreBusinessEntityId helper missing");
            foreach (string id in new[] { "player_general_store", "general-store-1", "biz-store", "store-1" })
            {
                var first = (EntityId)helper.Invoke(null, new object[] { id });
                var second = (EntityId)helper.Invoke(null, new object[] { id });
                Assert.AreNotEqual(EntityId.For(EntityKind.Business, 3), first);
                Assert.AreEqual(first, second);
            }
        }

        /// <summary>
        /// QA-20 (Plan 17): pre-save shape shared by the two persist tests —
        /// one completed trip (trip-100-0), one still-active trip
        /// (trip-100-1), and one failed plan (trip-100-2). The failed plan
        /// consumes a sequence but never enters activeTrips, so its id exists
        /// in no restorable record except the persisted counter — it is the
        /// persist-vs-seed discriminator.
        /// </summary>
        private static List<string> PlanCompletedActiveAndFailedTrips(Fixture f)
        {
            HouseholdPurchasingNeed needA = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            f.Driver.PlanForHousehold(Fixture.HouseholdId, f.Needs, Fixture.Day, f.LoopOptions, f.Diag);
            Assert.AreEqual(1, f.Driver.ActiveTrips.Count);
            string tripAId = f.Driver.ActiveTrips[0].TripId;
            Assert.AreEqual("trip-100-0", tripAId);

            f.RunUntilDone(tickSeconds: 300);
            Assert.AreEqual(0, f.Driver.ActiveTrips.Count, "trip A should complete\n" + string.Join("\n", f.Diag));
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, needA.Status);
            Assert.AreEqual(90, f.Store.Stock[HouseholdItemCatalog.FlourId]);
            Assert.AreEqual(1200, f.Store.CashReceived);

            f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            f.Driver.PlanForHousehold(Fixture.HouseholdId, f.Needs, Fixture.Day, f.LoopOptions, f.Diag);
            Assert.AreEqual(1, f.Driver.ActiveTrips.Count);
            string tripBId = f.Driver.ActiveTrips[0].TripId;
            Assert.AreEqual("trip-100-1", tripBId);

            HouseholdPurchasingNeed needF = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            needF.UnitsNeeded = 0;
            PlannedShoppingTrip failed = f.Loop.PlanTrip(needF, Fixture.Day, f.LoopOptions);
            Assert.AreEqual(PlannedShoppingTrip.TripState.Failed, failed.State);
            Assert.AreEqual("trip-100-2", failed.TripId);

            return new List<string> { tripAId, tripBId, failed.TripId };
        }

        [Test]
        public void TripSequence_PersistedAcrossCustodySaveLoad_DoesNotReissuePreSaveIds()
        {
            var f = new Fixture();
            List<string> preSaveIds = PlanCompletedActiveAndFailedTrips(f);

            HouseholdShoppingCustodySaveDto custodyDto = f.Loop.CaptureCustodySaveDto();
            // Reached through reflection so this file still compiles
            // against unmodified production code (red-first): the field
            // does not exist yet before Plan 17.
            System.Reflection.FieldInfo field = typeof(HouseholdShoppingCustodySaveDto).GetField("NextTripSequence");
            Assert.IsNotNull(field, "HouseholdShoppingCustodySaveDto must carry NextTripSequence (QA-20, Plan 17).");
            Assert.AreEqual(3, (int)field.GetValue(custodyDto));

            var g = new Fixture();
            g.Loop.LoadCustodyFromSaveDto(custodyDto);
            HouseholdPurchasingNeed needG = g.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            PlannedShoppingTrip newTrip = g.Loop.PlanTrip(needG, Fixture.Day, g.LoopOptions);
            Assert.AreEqual(PlannedShoppingTrip.TripState.Planned, newTrip.State);
            Assert.AreEqual("trip-100-3", newTrip.TripId);
            Assert.IsTrue(!preSaveIds.Contains(newTrip.TripId));
        }

        [Test]
        public void TripSequence_PersistedAcrossDriverAndCustodySaveLoad_DoesNotReissuePreSaveIds()
        {
            var f = new Fixture();
            List<string> preSaveIds = PlanCompletedActiveAndFailedTrips(f);
            List<PlannedShoppingTripSaveDto> driverDto = f.Driver.CaptureSaveDto();
            HouseholdShoppingCustodySaveDto custodyDto = f.Loop.CaptureCustodySaveDto();

            var g = new Fixture();
            // Driver-then-custody load order: the custody load must NOT
            // wipe the seed raise from the driver load (Max form, Plan 17 §1.2(b)).
            g.Driver.LoadFromSaveDto(driverDto);
            g.Loop.LoadCustodyFromSaveDto(custodyDto);
            Assert.AreEqual(1, g.Driver.ActiveTrips.Count);
            Assert.AreEqual("trip-100-1", g.Driver.ActiveTrips[0].TripId);

            g.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            g.Driver.PlanForHousehold(Fixture.HouseholdId, g.Needs, Fixture.Day, g.LoopOptions, g.Diag);
            Assert.AreEqual(2, g.Driver.ActiveTrips.Count);
            Assert.AreEqual("trip-100-3", g.Driver.ActiveTrips[1].TripId);
            // The restored trip B legitimately keeps "trip-100-1" (a
            // pre-save id); what must be fresh is the NEW trip's id, and
            // the two live ids must be distinct.
            Assert.IsTrue(!preSaveIds.Contains(g.Driver.ActiveTrips[1].TripId));
            Assert.AreEqual(2, g.Driver.ActiveTrips.Select(t => t.TripId).Distinct().Count());
        }

        [Test]
        public void TripSequence_RestoredActiveTrips_SeedPastThemOnOldSave()
        {
            var f = new Fixture();
            f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            f.Driver.PlanForHousehold(Fixture.HouseholdId, f.Needs, Fixture.Day, f.LoopOptions, f.Diag);
            Assert.AreEqual(1, f.Driver.ActiveTrips.Count);
            Assert.AreEqual("trip-100-0", f.Driver.ActiveTrips[0].TripId);

            // Deliberately NO custody DTO is captured or loaded — this
            // simulates a save written BEFORE this fix.
            var g = new Fixture();
            g.Driver.LoadFromSaveDto(f.Driver.CaptureSaveDto());
            Assert.AreEqual(1, g.Driver.ActiveTrips.Count);
            Assert.AreEqual("trip-100-0", g.Driver.ActiveTrips[0].TripId);

            g.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            // The first fresh need carries the same NeedSequence as the
            // restored trip's need, so PlanForHousehold skips it (an active
            // trip already exists for that need — HasActiveTripFor). The
            // second need must be a DIFFERENT item: CreateNeed returns the
            // existing open need for the same (household, item) key, so a
            // second flour need would be the same object. Salt (stocked in
            // the fresh fixture's store here) gets a new sequence and is
            // planned as the new trip.
            g.Store.Stock[HouseholdItemCatalog.SaltId] = 100;
            g.Store.Prices[HouseholdItemCatalog.SaltId] = 120;
            g.OpenNeed(HouseholdItemCatalog.SaltId, 10);
            g.Driver.PlanForHousehold(Fixture.HouseholdId, g.Needs, Fixture.Day, g.LoopOptions, g.Diag);
            Assert.AreEqual(2, g.Driver.ActiveTrips.Count);
            Assert.AreEqual("trip-100-1", g.Driver.ActiveTrips[1].TripId);
            Assert.AreEqual(2, g.Driver.ActiveTrips.Select(t => t.TripId).Distinct().Count());
        }

        // Slice 18 Tier 1 (Plan 21): patience tick + stale cleanup. Tier 2
        // (customer release) is deferred to Slice 18b after Slice 17 (note 70
        // ruling (a2)) — no release is built or tested here.
        [Test]
        public void PatienceTick_DecaysByElapsedMinutes()
        {
            var f = new Fixture();
            var log = new LostSaleLog();
            // Slice 18b (note 89): the trip must be AWAITING SERVICE at
            // arrival, not settled — the GENERAL sweep removes settled
            // InStore entries by design, so exact decay can only be
            // measured on an AwaitingService entry. Clerk + service wired
            // (the S1 pattern); the service due (arrival + 4 min) stays
            // pending because dues are no longer drained after arrival.
            AddClerk(f);
            CustomerQueue queue = WireService(f, log, 3);
            f.LoopOptions.InStoreMinutes = 30;
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            f.Driver.PlanForHousehold(Fixture.HouseholdId, f.Needs, Fixture.Day, f.LoopOptions, f.Diag);
            Assert.AreEqual(1, f.Driver.ActiveTrips.Count);
            PlannedShoppingTrip trip = f.Driver.ActiveTrips[0];
            // Step the clock in 5-minute increments from departure, draining
            // dues at each step as RunUntilDone does: the queue watermark
            // tracks real time, so the queue only ever decays the minutes
            // since the last tick. A jump straight to arrival would
            // over-decay by the 40-minute walk (the watermark sits at
            // departure), so the exact-decay assertions need fine steps.
            // Once arrival is detected the drain stops entirely: the
            // service due must stay pending (AdvanceTick does not process
            // dues), so the task stays InProgress and the trip stays
            // AwaitingService — and only InStore/AtStore entries are
            // swept, so the AwaitingService entry is spared.
            float departure = Fixture.Day * 86400f + trip.DepartureMinuteOfDay * 60f;
            float arrival = -1f;
            bool checkedTen = false;
            for (float now = departure; now <= departure + 3600f; now += 300f)
            {
                f.Driver.AdvanceTick(now, Fixture.Day, f.DueEvents, f.Diag);
                if (arrival < 0f)
                {
                    foreach (DueEvent ev in f.DueEvents.PopDue(now))
                    {
                        if (arrival < 0f && trip.Legs != null && trip.Legs.Outbound != null
                            && ev.PayloadRef == trip.Legs.Outbound.TaskId.ToString())
                        {
                            arrival = ev.DueGameSeconds;
                        }

                        f.Driver.HandleDueEvent(ev, Fixture.Day, ev.DueGameSeconds, f.DueEvents, f.Diag);
                    }
                }

                if (arrival >= 0f && !checkedTen && now >= arrival + 10 * 60f)
                {
                    checkedTen = true;
                    Assert.AreEqual(10, queue.Waiting[0].PatienceMinutesRemaining, "10 elapsed minutes must decay patience 20 -> 10");
                    Assert.AreEqual(1, queue.Waiting.Count, "AwaitingService entry is spared by the sweep");
                }

                if (arrival >= 0f && now >= arrival + 15 * 60f)
                {
                    Assert.AreEqual(5, queue.Waiting[0].PatienceMinutesRemaining, "15 elapsed minutes must decay patience 20 -> 5");
                    Assert.AreEqual(1, queue.Waiting.Count, "AwaitingService entry still present at +15 min");
                    Assert.AreEqual(PlannedShoppingTrip.TripState.AwaitingService, trip.State, "trip still awaiting service at +15 min");
                    Assert.IsNull(trip.Batch, "trip not settled at +15 min");
                    Assert.AreEqual(0, trip.UnitsSettled, "no units settled at +15 min");
                    break;
                }
            }

            Assert.IsTrue(checkedTen, "the +10 min check ran");
            Assert.AreEqual(PlannedShoppingTrip.TripState.AwaitingService, trip.State, "trip still awaiting service at end");
        }

        // ---- Slice 18b (Plan 23): balk release + settled-entry sweep (note 84, SOUND).
        // B1 and B3 are red-first; the release and the sweep are the new behaviour.
        [Test]
        public void BalkRelease_AwaitingServiceCustomer_ReturnsEmpty()
        {
            var f = new Fixture();
            var log = new LostSaleLog();
            AddClerk(f);
            CustomerQueue queue = WireService(f, log, 3);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            PlannedShoppingTrip trip = DriveToArrival(f, out float arrival);
            Assert.IsTrue(IsAwaitingService(trip));
            Assert.IsTrue(ServiceTaskIdOf(trip).IsValid);
            Assert.AreEqual(0, f.Store.CashReceived);
            Assert.IsNull(trip.Batch);
            WorkTask serviceTaskBefore = f.Tasks.FindTask(ServiceTaskIdOf(trip));
            Assert.IsNotNull(serviceTaskBefore);
            Assert.AreEqual(TaskStatus.InProgress, serviceTaskBefore.Status);
            // Slice 18b: accelerate patience (white-box, same class as Tier 1's
            // long-leg fixtures — the timing is accelerated, the trip state is
            // real). Do NOT touch the service due: it fires at arrival + 4 min
            // and AdvanceTick does not process dues, so the task stays
            // InProgress through the balk.
            queue.Waiting[0].PatienceMinutesRemaining = 1;
            f.Driver.AdvanceTick(arrival + 15 * 60f, Fixture.Day, f.DueEvents, f.Diag);
            // The release keeps the lost sale — this customer did not settle.
            Assert.AreEqual(1, log.Count, "released customer keeps the QueueBalk lost sale");
            List<LostSaleEvent> balks = log.EventsFor(string.Empty);
            Assert.AreEqual(1, balks.Count);
            Assert.AreEqual(LostSaleReason.QueueBalk, balks[0].Reason);
            Assert.AreEqual(PlannedShoppingTrip.TripState.Returning, trip.State);
            Assert.IsNull(trip.Batch);
            Assert.AreEqual(0, trip.UnitsSettled);
            Assert.AreEqual(0, f.Store.CashReceived);
            Assert.AreEqual(TaskStatus.Cancelled, f.Tasks.FindTask(ServiceTaskIdOf(trip)).Status);
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status, "need stays open after the release");
            Assert.IsNotNull(trip.Legs.Return, "return leg due is scheduled");
            // Stale-due no-op: the service due may or may not have been
            // cancelled with the task (seam, note 84 Finding 1 — in the
            // fixture the scheduler is not installed, so it is not) —
            // under either seam the outcome is the same.
            foreach (DueEvent ev in f.DueEvents.PopDue(arrival + 5 * 60f))
            {
                f.Driver.HandleDueEvent(ev, Fixture.Day, ev.DueGameSeconds, f.DueEvents, f.Diag);
            }

            Assert.AreEqual(0, f.Store.CashReceived);
            Assert.AreEqual(PlannedShoppingTrip.TripState.Returning, trip.State);
            f.RunUntilDone(tickSeconds: 300);
            Assert.AreEqual(0, f.Driver.ActiveTrips.Count);
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status);
            Assert.AreEqual(0, f.Store.CashReceived);
        }

        [Test]
        public void ServedEntry_LingersAfterFrontPop_SweptSilently()
        {
            var f = new Fixture();
            var log = new LostSaleLog();
            AddClerk(f);
            // WireService ONCE: a second call would create a new queue.
            CustomerQueue queue = WireService(f, log, -1);
            f.LoopOptions.InStoreMinutes = 30;
            HouseholdPurchasingNeed needA = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            // Second household (customer B): person 4, household 8.
            f.Population.people.Add(new PersonState { id = 4, firstName = "Bram", lastName = "Kett", age = 30, ageBand = AgeBand.Adult18Plus });
            f.Population.households.Add(new HouseholdState { id = 8, householdName = "Kett" });
            f.Membership.Register(new HouseholdMembership { MembershipId = 3, PersonId = 4, HouseholdId = 8, StartDayIndex = 0 });
            f.Knowledge.LearnSupplier(4, "store-1", "lives in the same town", 0);
            f.Ledgers.GetOrCreate(8).RecordInflow(0, 50000, HouseholdIncomeSource.WageEmployment, "emp-1", "seed wages", "test");
            HouseholdPurchasingNeed needB = f.Needs.CreateNeed(8, HouseholdItemCatalog.FlourId, Fixture.Day);
            needB.UnitsNeeded = 10;
            needB.IsNecessity = true;
            // The clerk resolver returns -1 for B's arrival and 3 for A's:
            // a closure over a mutable local, via the WireService reflection
            // pattern — WireService itself is NOT called twice.
            int clerkId = -1;
            System.Reflection.PropertyInfo clerkProp = typeof(HouseholdShoppingLoop).GetProperty("ResolveServiceClerkPersonId");
            Assert.IsNotNull(clerkProp, "Plan 16 ResolveServiceClerkPersonId property missing");
            clerkProp.SetValue(f.Loop, (System.Func<string, int>)(businessId => businessId == "store-1" ? clerkId : -1));
            // Drive manually — DriveToArrival asserts ActiveTrips == 1.
            f.Driver.PlanForHousehold(Fixture.HouseholdId, f.Needs, Fixture.Day, f.LoopOptions, f.Diag);
            f.Driver.PlanForHousehold(8, f.Needs, Fixture.Day, f.LoopOptions, f.Diag);
            Assert.AreEqual(2, f.Driver.ActiveTrips.Count);
            PlannedShoppingTrip tripA = null, tripB = null;
            foreach (PlannedShoppingTrip t in f.Driver.ActiveTrips)
            {
                if (t.HouseholdId == Fixture.HouseholdId) tripA = t;
                else if (t.HouseholdId == 8) tripB = t;
            }

            Assert.IsNotNull(tripA);
            Assert.IsNotNull(tripB);
            float departure = Fixture.Day * 86400f + tripA.DepartureMinuteOfDay * 60f;
            f.Driver.AdvanceTick(departure, Fixture.Day, f.DueEvents, f.Diag);
            DueEvent outboundA = null, outboundB = null;
            foreach (DueEvent ev in f.DueEvents.PopDue(departure + 86400f))
            {
                if (ev.PayloadRef == tripA.Legs.Outbound.TaskId.ToString()) outboundA = ev;
                else if (ev.PayloadRef == tripB.Legs.Outbound.TaskId.ToString()) outboundB = ev;
            }

            Assert.IsNotNull(outboundA, "A outbound leg due event not scheduled");
            Assert.IsNotNull(outboundB, "B outbound leg due event not scheduled");
            float arrival = outboundA.DueGameSeconds;
            Assert.AreEqual(outboundB.DueGameSeconds, arrival, "both trips arrive together");
            // B arrives first with no clerk: settles immediately via (g1),
            // InStore, entry [B].
            f.Driver.HandleDueEvent(outboundB, Fixture.Day, arrival, f.DueEvents, f.Diag);
            Assert.AreEqual(PlannedShoppingTrip.TripState.InStore, tripB.State);
            Assert.IsNotNull(tripB.Batch);
            Assert.AreEqual(1, queue.WaitingCount);
            // A arrives with the clerk: service starts, AwaitingService,
            // entry [B, A].
            clerkId = 3;
            f.Driver.HandleDueEvent(outboundA, Fixture.Day, arrival, f.DueEvents, f.Diag);
            Assert.IsTrue(IsAwaitingService(tripA));
            Assert.AreEqual(2, queue.WaitingCount);
            Assert.AreEqual(HouseholdShoppingLoop.ServiceCustomerRefFor(tripB), queue.Waiting[0].CustomerRef);
            Assert.AreEqual(HouseholdShoppingLoop.ServiceCustomerRefFor(tripA), queue.Waiting[1].CustomerRef);
            // Serve A: PopServiceDueEvent's window is arrival + 4 min + 1s,
            // so B's InStore leg due (arrival + 30 min) is NOT popped.
            // ServeNext pops waiting[0] = B (front, not the served trip —
            // the mismatch); A settles and starts the InStore leg.
            DueEvent serviceEv = PopServiceDueEvent(f, tripA, arrival);
            Assert.IsNotNull(serviceEv, "service due event not scheduled");
            f.Driver.HandleDueEvent(serviceEv, Fixture.Day, serviceEv.DueGameSeconds, f.DueEvents, f.Diag);
            Assert.AreEqual(1, queue.WaitingCount);
            Assert.AreEqual(HouseholdShoppingLoop.ServiceCustomerRefFor(tripA), queue.Waiting[0].CustomerRef, "A's entry lingers after the front-pop");
            Assert.AreEqual(PlannedShoppingTrip.TripState.InStore, tripA.State);
            Assert.IsNotNull(tripA.Batch);
            Assert.AreEqual(2400, f.Store.CashReceived);
            Assert.AreEqual(PlannedShoppingTrip.TripState.InStore, tripB.State);
            Assert.IsNotNull(tripB.Batch);
            f.Driver.AdvanceTick(arrival + 25 * 60f, Fixture.Day, f.DueEvents, f.Diag);
            Assert.AreEqual(0, queue.WaitingCount, "A's lingering entry was swept, no false QueueBalk");
            Assert.AreEqual(0, log.Count, "no false lost sale for a settled customer");
            Assert.AreEqual(PlannedShoppingTrip.TripState.InStore, tripA.State);
            Assert.IsNotNull(tripA.Batch);
            Assert.AreEqual(PlannedShoppingTrip.TripState.InStore, tripB.State);
            Assert.IsNotNull(tripB.Batch);
            Assert.IsTrue(!f.Diag.Any(line => line.Contains("balked")), "sweep is silent");
        }

        [Test]
        public void SettledEntry_SweptSilently_NoLostSale()
        {
            var f = new Fixture();
            var log = new LostSaleLog();
            CustomerQueue queue = WireService(f, log, -1);
            f.LoopOptions.InStoreMinutes = 30;
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            PlannedShoppingTrip trip = DriveToArrival(f, out float arrival);
            Assert.AreEqual(1, queue.WaitingCount);
            f.Driver.AdvanceTick(arrival + 25 * 60f, Fixture.Day, f.DueEvents, f.Diag);
            Assert.AreEqual(0, queue.WaitingCount, "settled entry must be swept before Tick, never balked");
            Assert.AreEqual(0, log.Count, "no QueueBalk lost sale for a settled customer");
            Assert.AreEqual(PlannedShoppingTrip.TripState.InStore, trip.State, "leg still open at +25 min");
            // Slice 18b: the sweep is silent — no "balked" line at all.
            Assert.IsTrue(!f.Diag.Any(line => line.Contains("balked")), "swept entry writes no balk diag");
            Assert.IsNotNull(trip.Batch);
            Assert.AreEqual(1200, f.Store.CashReceived, "settlement untouched by the sweep");
        }

        [Test]
        public void LegCompletion_RemovesStaleEntry_Silently()
        {
            var f = new Fixture();
            var log = new LostSaleLog();
            CustomerQueue queue = WireService(f, log, -1);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            PlannedShoppingTrip trip = DriveToArrival(f, out float arrival);
            Assert.AreEqual(1, queue.WaitingCount);
            // Run the InStore leg due (default 15 min); pop past the due time
            // so the boundary is not in question. The return leg due is
            // scheduled during handling, after the pop, so it cannot leak in.
            foreach (DueEvent ev in f.DueEvents.PopDue(arrival + 16 * 60f))
            {
                f.Driver.HandleDueEvent(ev, Fixture.Day, ev.DueGameSeconds, f.DueEvents, f.Diag);
            }

            Assert.AreEqual(PlannedShoppingTrip.TripState.Returning, trip.State);
            Assert.AreEqual(0, queue.WaitingCount, "leg completion must remove the stale queue entry");
            Assert.AreEqual(0, log.Count, "no false lost sale on a served leg");
        }

        [Test]
        public void SettleFailure_RemovesEntry_Silently()
        {
            var f = new Fixture();
            var log = new LostSaleLog();
            CustomerQueue queue = WireService(f, log, -1);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            // The planner rejects a zero-stock supplier ("no available stock
            // (real lots exhausted)"), so a settle failure is only reachable
            // when the shelves empty BETWEEN planning and arrival (another
            // buyer) — zero the stock after planning, before the outbound
            // leg is driven.
            PlannedShoppingTrip trip = DriveToArrival(f, out _,
                afterPlan: () => f.Store.Stock[HouseholdItemCatalog.FlourId] = 0);
            Assert.AreEqual(PlannedShoppingTrip.TripState.Returning, trip.State, "failed arrival walks home");
            Assert.AreEqual(0, queue.WaitingCount, "failed arrival must remove the queue entry");
            Assert.AreEqual(0, log.Count, "no false lost sale on a failed arrival");
        }

        [Test]
        public void TickSizeIndependence_SettledSweep()
        {
            // 5-minute ticks.
            PatienceOutcome small = RunPatienceScenario(tickSeconds: 300);
            // 15-minute ticks.
            PatienceOutcome large = RunPatienceScenario(tickSeconds: 900);
            Assert.AreEqual(0, small.QueueCount, "5-min ticks: settled entry must be swept out");
            Assert.AreEqual(0, large.QueueCount, "15-min ticks: settled entry must be swept out");
            Assert.AreEqual(0, small.LostSales, "5-min ticks: no QueueBalk for a settled customer");
            Assert.AreEqual(0, large.LostSales, "15-min ticks: no QueueBalk for a settled customer");
            Assert.AreEqual(small.NeedStatus, large.NeedStatus, "need outcome must match across tick sizes");
            Assert.AreEqual(0, small.ActiveTrips, "5-min ticks: trip completes");
            Assert.AreEqual(0, large.ActiveTrips, "15-min ticks: trip completes");
        }

        private sealed class PatienceOutcome
        {
            public int QueueCount;
            public int LostSales;
            public PurchasingNeedStatus NeedStatus;
            public int ActiveTrips;
        }

        private static PatienceOutcome RunPatienceScenario(int tickSeconds)
        {
            var f = new Fixture();
            var log = new LostSaleLog();
            CustomerQueue queue = WireService(f, log, -1);
            f.LoopOptions.InStoreMinutes = 45;
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            f.Driver.PlanForHousehold(Fixture.HouseholdId, f.Needs, Fixture.Day, f.LoopOptions, f.Diag);
            f.RunUntilDone(tickSeconds);
            return new PatienceOutcome
            {
                QueueCount = queue.WaitingCount,
                LostSales = log.Count,
                NeedStatus = need.Status,
                ActiveTrips = f.Driver.ActiveTrips.Count,
            };
        }

        // ---- Slice 17 (Plan 22): settle gate — settlement waits for the
        // clerk's TendCustomer service to complete (note 79, SOUND).
        // S1, S3, S4, S5 are red-first; S2 pins the unchanged no-service
        // rule (g1) and passes before and after.
        [Test]
        public void ServiceCompletion_SettlesAfterService()
        {
            var f = new Fixture();
            var log = new LostSaleLog();
            AddClerk(f);
            CustomerQueue queue = WireService(f, log, 3);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            PlannedShoppingTrip trip = DriveToArrival(f, out float arrival);
            Assert.IsNull(trip.Batch);
            Assert.AreEqual(0, trip.UnitsSettled);
            Assert.AreEqual(0, f.Store.CashReceived);
            Assert.IsTrue(IsAwaitingService(trip));
            Assert.AreEqual(1, queue.WaitingCount);
            DueEvent serviceEv = PopServiceDueEvent(f, trip, arrival);
            Assert.IsNotNull(serviceEv, "service due event not scheduled");
            f.Driver.HandleDueEvent(serviceEv, Fixture.Day, serviceEv.DueGameSeconds, f.DueEvents, f.Diag);
            Assert.IsNotNull(trip.Batch);
            Assert.AreEqual(10, trip.UnitsSettled);
            Assert.AreEqual(1200, f.Store.CashReceived);
            Assert.AreEqual(PlannedShoppingTrip.TripState.InStore, trip.State);
            Assert.AreEqual(0, queue.WaitingCount);
        }

        [Test]
        public void NoClerk_SettlesAtArrivalAsToday()
        {
            var f = new Fixture();
            var log = new LostSaleLog();
            CustomerQueue queue = WireService(f, log, -1);
            f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            PlannedShoppingTrip trip = DriveToArrival(f, out _);
            Assert.IsNotNull(trip.Batch);
            Assert.AreEqual(PlannedShoppingTrip.TripState.InStore, trip.State);
            Assert.AreEqual(1200, f.Store.CashReceived);
            Assert.AreEqual(1, queue.WaitingCount);
        }

        [Test]
        public void ServiceCompletionFailure_SettlesAnyway()
        {
            var f = new Fixture();
            var log = new LostSaleLog();
            AddClerk(f);
            CustomerQueue queue = WireService(f, log, 3);
            f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            PlannedShoppingTrip trip = DriveToArrival(f, out float arrival);
            Assert.IsNull(trip.Batch);
            Assert.IsTrue(IsAwaitingService(trip));
            Assert.IsTrue(f.Tasks.CompleteTask(ServiceTaskIdOf(trip), TaskOutcome.Success, Fixture.Day, f.Budgets));
            DueEvent serviceEv = PopServiceDueEvent(f, trip, arrival);
            Assert.IsNotNull(serviceEv, "service due event not scheduled");
            f.Driver.HandleDueEvent(serviceEv, Fixture.Day, serviceEv.DueGameSeconds, f.DueEvents, f.Diag);
            Assert.IsNotNull(trip.Batch);
            Assert.AreEqual(PlannedShoppingTrip.TripState.InStore, trip.State);
            Assert.AreEqual(1200, f.Store.CashReceived);
            StringAssert.Contains("did not complete", string.Join("\n", f.Diag));
        }

        [Test]
        public void SettleFailureAfterService_ReturnsEmpty()
        {
            var f = new Fixture();
            var log = new LostSaleLog();
            AddClerk(f);
            CustomerQueue queue = WireService(f, log, 3);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            PlannedShoppingTrip trip = DriveToArrival(f, out float arrival,
                afterPlan: () => f.Store.Stock[HouseholdItemCatalog.FlourId] = 0);
            Assert.IsNull(trip.Batch);
            Assert.IsTrue(IsAwaitingService(trip));
            DueEvent serviceEv = PopServiceDueEvent(f, trip, arrival);
            Assert.IsNotNull(serviceEv, "service due event not scheduled");
            f.Driver.HandleDueEvent(serviceEv, Fixture.Day, serviceEv.DueGameSeconds, f.DueEvents, f.Diag);
            Assert.AreEqual(PlannedShoppingTrip.TripState.Returning, trip.State);
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status);
            Assert.AreEqual(0, queue.WaitingCount);
            Assert.AreEqual(0, log.Count);
            Assert.AreEqual(0, f.Store.CashReceived);
        }

        [Test]
        public void SaveLoad_AwaitingService_ResumesWait()
        {
            var f = new Fixture();
            var log = new LostSaleLog();
            AddClerk(f);
            CustomerQueue queue = WireService(f, log, 3);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            PlannedShoppingTrip trip = DriveToArrival(f, out float arrival);
            Assert.IsNull(trip.Batch);
            Assert.IsTrue(IsAwaitingService(trip));
            string serviceTaskIdString = ServiceTaskIdOf(trip).ToString();
            EntityId serviceTaskId = ServiceTaskIdOf(trip);
            int plannedMinutes = ServiceTaskPlannedMinutesOf(trip);
            WorkTask preTask = f.Tasks.FindTask(serviceTaskId);
            Assert.IsNotNull(preTask, "service task not found pre-save");
            Assert.AreEqual(TaskStatus.InProgress, preTask.Status);
            EntityId preAssignee = preTask.AssigneeId;

            // Capture all authority state (clear-then-load), as the :677-690 pattern.
            var tasksDto = f.Tasks.CaptureSaveDto();
            var budgetsDto = f.Budgets.CaptureSaveDto();
            var schedulesDto = f.Tracker.CaptureSaveDto();
            var driverDto = f.Driver.CaptureSaveDto();
            var dueDto = f.DueEvents.CaptureSaveDto();

            // Restore into a FRESH fixture (new authorities, same scenario wiring).
            var g = new Fixture();
            g.Tasks.LoadFromSaveDto(tasksDto);
            g.Budgets.LoadFromSaveDto(budgetsDto, Fixture.Day);
            g.Tracker.LoadFromSaveDto(schedulesDto);
            g.Driver.LoadFromSaveDto(driverDto);
            g.DueEvents.LoadFromSaveDto(dueDto);
            HouseholdPurchasingNeed gNeed = g.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            gNeed.UnitsNeeded = need.UnitsNeeded;
            foreach (PlannedShoppingTrip t in g.Driver.ActiveTrips)
            {
                t.Need = gNeed;
            }

            // The save happened at arrival, so restore "now" is the arrival time.
            g.Driver.RehydrateAfterLoad(g.Tasks, id => (IGoodsSupplier)g.Store,
                Fixture.Day, arrival, g.DueEvents, g.Diag);
            Assert.AreEqual(1, g.Driver.ActiveTrips.Count);
            PlannedShoppingTrip restored = g.Driver.ActiveTrips[0];
            // Slice 19 (f2): the wait resumes — nothing settles on restore
            // while the service task still lives.
            Assert.IsTrue(IsAwaitingService(restored));
            Assert.IsNull(restored.Batch);
            Assert.AreEqual(0, restored.UnitsSettled);
            Assert.AreEqual(0, g.Store.CashReceived);
            Assert.AreEqual(serviceTaskId, ServiceTaskIdOf(restored));
            Assert.AreEqual(plannedMinutes, ServiceTaskPlannedMinutesOf(restored));
            WorkTask restoredTask = g.Tasks.FindTask(serviceTaskId);
            Assert.IsNotNull(restoredTask, "service task not found after restore");
            Assert.AreEqual(TaskStatus.InProgress, restoredTask.Status);
            Assert.AreEqual(preAssignee, restoredTask.AssigneeId);

            // The restored service due is owned again: the map entry was
            // re-registered on rehydrate. Pop only up to arrival + 5 min so
            // the in-store due (arrival + 15) is not touched.
            DueEvent serviceEv = null;
            foreach (DueEvent ev in g.DueEvents.PopDue(arrival + 5 * 60f))
            {
                if (ev.PayloadRef == serviceTaskIdString)
                {
                    serviceEv = ev;
                }
            }

            Assert.IsNotNull(serviceEv, "service due event not restored");
            Assert.IsTrue(g.Driver.OwnsDueEvent(serviceEv), "restored service due is not owned by the driver");
        }

        [Test]
        public void SaveLoad_AwaitingService_ServiceDueCompletesAfterRestore()
        {
            var f = new Fixture();
            var log = new LostSaleLog();
            AddClerk(f);
            CustomerQueue fQueue = WireService(f, log, 3);
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            PlannedShoppingTrip trip = DriveToArrival(f, out float arrival);
            Assert.IsNull(trip.Batch);
            Assert.IsTrue(IsAwaitingService(trip));
            string serviceTaskIdString = ServiceTaskIdOf(trip).ToString();
            string customerRef = HouseholdShoppingLoop.ServiceCustomerRefFor(trip);
            Assert.AreEqual(1, fQueue.WaitingCount);

            var tasksDto = f.Tasks.CaptureSaveDto();
            var budgetsDto = f.Budgets.CaptureSaveDto();
            var schedulesDto = f.Tracker.CaptureSaveDto();
            var driverDto = f.Driver.CaptureSaveDto();
            var dueDto = f.DueEvents.CaptureSaveDto();
            var queueDto = fQueue.CaptureSaveDto();

            // Restore into a FRESH fixture (new authorities, same scenario wiring).
            var g = new Fixture();
            CustomerQueue gQueue = WireService(g, log, 3);
            gQueue.LoadFromSaveDto(queueDto);
            Assert.AreEqual(1, gQueue.WaitingCount);
            Assert.AreEqual(customerRef, gQueue.Waiting[0].CustomerRef);
            g.Tasks.LoadFromSaveDto(tasksDto);
            g.Budgets.LoadFromSaveDto(budgetsDto, Fixture.Day);
            g.Tracker.LoadFromSaveDto(schedulesDto);
            g.Driver.LoadFromSaveDto(driverDto);
            g.DueEvents.LoadFromSaveDto(dueDto);
            HouseholdPurchasingNeed gNeed = g.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            gNeed.UnitsNeeded = need.UnitsNeeded;
            foreach (PlannedShoppingTrip t in g.Driver.ActiveTrips)
            {
                t.Need = gNeed;
            }

            // The save happened at arrival, so restore "now" is the arrival time.
            g.Driver.RehydrateAfterLoad(g.Tasks, id => (IGoodsSupplier)g.Store,
                Fixture.Day, arrival, g.DueEvents, g.Diag);
            Assert.AreEqual(1, g.Driver.ActiveTrips.Count);
            PlannedShoppingTrip restored = g.Driver.ActiveTrips[0];
            Assert.IsTrue(IsAwaitingService(restored));
            Assert.IsNull(restored.Batch);
            Assert.AreEqual(0, restored.UnitsSettled);
            Assert.AreEqual(0, g.Store.CashReceived);

            // The restored service due fires exactly as live: the task
            // completes, the queue pops, the trip settles at the counter.
            DueEvent serviceEv = null;
            foreach (DueEvent ev in g.DueEvents.PopDue(arrival + 5 * 60f))
            {
                if (ev.PayloadRef == serviceTaskIdString)
                {
                    serviceEv = ev;
                }
            }

            Assert.IsNotNull(serviceEv, "service due event not restored");
            Assert.IsTrue(g.Driver.OwnsDueEvent(serviceEv), "restored service due is not owned by the driver");
            g.Driver.HandleDueEvent(serviceEv, Fixture.Day, serviceEv.DueGameSeconds, g.DueEvents, g.Diag);
            Assert.AreEqual(TaskStatus.Complete, g.Tasks.FindTask(ServiceTaskIdOf(restored)).Status);
            Assert.AreEqual(0, gQueue.WaitingCount);
            Assert.IsNotNull(restored.Batch);
            Assert.AreEqual(10, restored.UnitsSettled);
            Assert.AreEqual(1200, g.Store.CashReceived);
            Assert.AreEqual(PlannedShoppingTrip.TripState.InStore, restored.State);
            Assert.IsTrue(g.DueEvents.ScheduledCount > 0, "in-store leg due not scheduled");
            Assert.AreEqual(PurchasingNeedStatus.Open, gNeed.Status);

            // Drive the trip home: exactly-once settlement, no second settle.
            g.RunUntilDone(tickSeconds: 300);
            Assert.AreEqual(0, g.Driver.ActiveTrips.Count);
            Assert.AreEqual(1200, g.Store.CashReceived);
        }

        [Test]
        public void SaveLoad_AwaitingService_TaskGone_SettlesOnRestore()
        {
            // Phase (a): the service task is missing from the saved tasks.
            {
                var f = new Fixture();
                var log = new LostSaleLog();
                AddClerk(f);
                CustomerQueue queue = WireService(f, log, 3);
                f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
                PlannedShoppingTrip trip = DriveToArrival(f, out float arrival);
                Assert.IsTrue(IsAwaitingService(trip));
                EntityId serviceTaskId = ServiceTaskIdOf(trip);

                var tasksDto = f.Tasks.CaptureSaveDto();
                tasksDto.tasks.RemoveAll(t => t.TaskId == serviceTaskId);
                var budgetsDto = f.Budgets.CaptureSaveDto();
                var schedulesDto = f.Tracker.CaptureSaveDto();
                var driverDto = f.Driver.CaptureSaveDto();
                var dueDto = f.DueEvents.CaptureSaveDto();

                var g = new Fixture();
                g.Tasks.LoadFromSaveDto(tasksDto);
                g.Budgets.LoadFromSaveDto(budgetsDto, Fixture.Day);
                g.Tracker.LoadFromSaveDto(schedulesDto);
                g.Driver.LoadFromSaveDto(driverDto);
                g.DueEvents.LoadFromSaveDto(dueDto);
                HouseholdPurchasingNeed gNeed = g.OpenNeed(HouseholdItemCatalog.FlourId, 10);
                foreach (PlannedShoppingTrip t in g.Driver.ActiveTrips)
                {
                    t.Need = gNeed;
                }

                g.Driver.RehydrateAfterLoad(g.Tasks, id => (IGoodsSupplier)g.Store,
                    Fixture.Day, arrival, g.DueEvents, g.Diag);
                Assert.AreEqual(1, g.Driver.ActiveTrips.Count);
                PlannedShoppingTrip restored = g.Driver.ActiveTrips[0];
                Assert.IsNotNull(restored.Batch);
                Assert.AreEqual(10, restored.UnitsSettled);
                Assert.AreEqual(PlannedShoppingTrip.TripState.InStore, restored.State);
                Assert.AreEqual(1200, g.Store.CashReceived);
                StringAssert.Contains("lost its live service task", string.Join("\n", g.Diag));
            }

            // Phase (b): the service task was cancelled before the save.
            // DEVIATION from plan §6.3: the Cancelled-specific reason text
            // is NOT asserted here. The red tree has no persisted
            // ServiceTaskId to look up, so its reason is necessarily "no
            // service task id" and a Cancelled-reason assertion cannot pass
            // red; per the plan's own rule (T2 fails red => the test is
            // wrong) the reason is pinned only by the generic line, while
            // the Cancelled status of the restored task is pinned directly.
            {
                var f = new Fixture();
                var log = new LostSaleLog();
                AddClerk(f);
                CustomerQueue queue = WireService(f, log, 3);
                f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
                PlannedShoppingTrip trip = DriveToArrival(f, out float arrival);
                Assert.IsTrue(IsAwaitingService(trip));
                EntityId serviceTaskId = ServiceTaskIdOf(trip);
                Assert.IsTrue(f.Tasks.CancelTask(serviceTaskId, f.Budgets), "cancel of the service task refused");

                var tasksDto = f.Tasks.CaptureSaveDto();
                var budgetsDto = f.Budgets.CaptureSaveDto();
                var schedulesDto = f.Tracker.CaptureSaveDto();
                var driverDto = f.Driver.CaptureSaveDto();
                var dueDto = f.DueEvents.CaptureSaveDto();

                var g = new Fixture();
                g.Tasks.LoadFromSaveDto(tasksDto);
                g.Budgets.LoadFromSaveDto(budgetsDto, Fixture.Day);
                g.Tracker.LoadFromSaveDto(schedulesDto);
                g.Driver.LoadFromSaveDto(driverDto);
                g.DueEvents.LoadFromSaveDto(dueDto);
                HouseholdPurchasingNeed gNeed = g.OpenNeed(HouseholdItemCatalog.FlourId, 10);
                foreach (PlannedShoppingTrip t in g.Driver.ActiveTrips)
                {
                    t.Need = gNeed;
                }

                g.Driver.RehydrateAfterLoad(g.Tasks, id => (IGoodsSupplier)g.Store,
                    Fixture.Day, arrival, g.DueEvents, g.Diag);
                Assert.AreEqual(1, g.Driver.ActiveTrips.Count);
                PlannedShoppingTrip restored = g.Driver.ActiveTrips[0];
                Assert.IsNotNull(restored.Batch);
                Assert.AreEqual(10, restored.UnitsSettled);
                Assert.AreEqual(PlannedShoppingTrip.TripState.InStore, restored.State);
                Assert.AreEqual(1200, g.Store.CashReceived);
                StringAssert.Contains("lost its live service task", string.Join("\n", g.Diag));
                Assert.AreEqual(TaskStatus.Cancelled, g.Tasks.FindTask(serviceTaskId).Status);
            }
        }

        [Test]
        public void SaveLoad_QueueEntry_PatienceInitialTravels()
        {
            var f = new Fixture();
            var log = new LostSaleLog();
            AddClerk(f);
            CustomerQueue fQueue = WireService(f, log, 3);
            f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            PlannedShoppingTrip trip = DriveToArrival(f, out float arrival);
            Assert.AreEqual(1, fQueue.WaitingCount);
            WaitingCustomer pre = fQueue.Waiting[0];
            int preInitial = pre.PatienceMinutesInitial;
            int preRemaining = pre.PatienceMinutesRemaining;
            Assert.AreEqual(CustomerQueue.DefaultPatienceMinutes, preInitial);
            Assert.AreEqual(CustomerQueue.DefaultPatienceMinutes, preRemaining);

            var gQueue = new CustomerQueue(new LostSaleLog());
            gQueue.LoadFromSaveDto(fQueue.CaptureSaveDto());
            Assert.AreEqual(1, gQueue.WaitingCount);
            WaitingCustomer restored = gQueue.Waiting[0];
            Assert.AreEqual(pre.CustomerRef, restored.CustomerRef);
            Assert.AreEqual(preRemaining, restored.PatienceMinutesRemaining);
            Assert.AreEqual(preInitial, restored.PatienceMinutesInitial);

            // Balk the restored entry: the waited computation reads
            // Initial - Remaining, so a zero Initial would understate it.
            gQueue.Tick(25, Fixture.Day);
            List<WaitingCustomer> balked = gQueue.TakeBalked();
            Assert.AreEqual(1, balked.Count);
            Assert.AreEqual(preInitial, balked[0].PatienceMinutesInitial);
        }

        // Slice 25 (note 94): production save/load wiring for the
        // ShoppingTripDriver — hub field, hub driver reference, restore-time
        // rehydrate seam, driver-owned pending flag.

        private static void DrainRestoredTripToCompletion(Fixture g, float gStart)
        {
            for (int t = 122; t < 2000 && g.Driver.ActiveTrips.Count > 0; t++)
            {
                float now = gStart + t * 300f;
                g.Driver.AdvanceTick(now, Fixture.Day, g.DueEvents, g.Diag);
                foreach (DueEvent ev in g.DueEvents.PopDue(now))
                {
                    g.Driver.HandleDueEvent(ev, Fixture.Day, ev.DueGameSeconds, g.DueEvents, g.Diag);
                }
            }

            for (int i = 0; i < 50 && (g.DueEvents.ScheduledCount > 0 || g.Driver.ActiveTrips.Count > 0); i++)
            {
                foreach (DueEvent ev in g.DueEvents.PopDue(gStart + 3 * 86400f))
                {
                    g.Driver.HandleDueEvent(ev, Fixture.Day, ev.DueGameSeconds, g.DueEvents, g.Diag);
                }
            }
        }

        [Test]
        public void Hub_SaveLoad_ShoppingTripsSurviveProductionOrder()
        {
            // Slice 25 fallback (plan §7 step 1(c), adapted): the hub
            // round-trip does NOT run in the harness. SimulationSystemsHub.cs
            // and SystemsSaveDtos.cs are deliberately harness-excluded (the
            // hub's Unity-scene dependency web; the DTO file duplicates shim
            // types and references harness-absent types — adding it fails
            // with 84 errors), and P6TravelCommsJourneyTests.cs is excluded
            // from the harness sources. This test therefore proves the
            // production restore ORDER at the DTO level, by hand: need open
            // in g first (SaveLoadManager :394), tasks, custody, dues, then
            // the driver list — the same order hub.LoadFromSaveDto runs. The
            // hub's own capture/restore lines are covered by Agent 3 source
            // review. The test keeps its plan name so the T1–T5 inventory
            // matches the plan.
            var f = new Fixture();
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            f.Driver.PlanForHousehold(Fixture.HouseholdId, f.Needs, Fixture.Day, f.LoopOptions, f.Diag);

            // Advance to mid-outbound, then save everything.
            float start = Fixture.Day * 86400f;
            f.Driver.AdvanceTick(start + 610 * 60f, Fixture.Day, f.DueEvents, f.Diag);
            Assert.AreEqual(1, f.Driver.ActiveTrips.Count);

            var tasksDto = f.Tasks.CaptureSaveDto();
            var budgetsDto = f.Budgets.CaptureSaveDto();
            var schedulesDto = f.Tracker.CaptureSaveDto();
            var driverDto = f.Driver.CaptureSaveDto();
            var dueDto = f.DueEvents.CaptureSaveDto();
            Assert.AreEqual(1, driverDto.Count);

            // Restore into a FRESH fixture, in the production order.
            var g = new Fixture();
            float gStart = Fixture.Day * 86400f;
            // Step 1 (SaveLoadManager :394, note 94 condition C1): open
            // the need in g BEFORE the driver list load, so the load-time
            // need re-point lands on g's registry need. No hand re-point
            // follows: the load itself must do it.
            HouseholdPurchasingNeed gNeed = g.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            gNeed.UnitsNeeded = need.UnitsNeeded;
            g.Tasks.LoadFromSaveDto(tasksDto);
            g.Budgets.LoadFromSaveDto(budgetsDto, Fixture.Day);
            g.Tracker.LoadFromSaveDto(schedulesDto);
            g.DueEvents.LoadFromSaveDto(dueDto);
            g.Driver.LoadFromSaveDto(driverDto);
            Assert.AreEqual(1, g.Driver.ActiveTrips.Count);
            Assert.IsTrue(g.Driver.NeedsRehydrateAfterLoad);

            // The seam call, exactly as ApplyRestoredShoppingTrips makes it.
            g.Driver.RehydrateAfterLoad(g.Tasks, id => (IGoodsSupplier)g.Store,
                Fixture.Day, gStart + 610 * 60f, g.DueEvents, g.Diag);
            Assert.IsTrue(!g.Driver.NeedsRehydrateAfterLoad);

            // Drive to completion (the SaveLoad_MidTrip_ResumesToSameOutcome drain).
            DrainRestoredTripToCompletion(g, gStart);
            Assert.AreEqual(0, g.Driver.ActiveTrips.Count, "restored trip should finish\n" + string.Join("\n", g.Diag));
            // The Fulfilled assertion reads g's registry need (opened
            // before the driver list load, re-pointed at load time).
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, gNeed.Status);
            Assert.AreEqual(90, g.Store.Stock[HouseholdItemCatalog.FlourId]);
            Assert.AreEqual(1200, g.Store.CashReceived);
        }

        [Test]
        public void SaveLoad_RehydrateOrder_TasksBeforeDriver()
        {
            var f = new Fixture();
            f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            f.Driver.PlanForHousehold(Fixture.HouseholdId, f.Needs, Fixture.Day, f.LoopOptions, f.Diag);
            float start = Fixture.Day * 86400f;
            f.Driver.AdvanceTick(start + 610 * 60f, Fixture.Day, f.DueEvents, f.Diag);
            Assert.AreEqual(1, f.Driver.ActiveTrips.Count);

            var tasksDto = f.Tasks.CaptureSaveDto();
            var driverDto = f.Driver.CaptureSaveDto();
            var dueDto = f.DueEvents.CaptureSaveDto();

            // Phase (a) wrong order: driver list restored, rehydrate BEFORE
            // tasks restore. Pins existing RehydrateAfterLoad behaviour.
            var g = new Fixture();
            g.Driver.LoadFromSaveDto(driverDto);
            HouseholdPurchasingNeed gNeedCopy = g.Driver.ActiveTrips[0].Need;
            g.Driver.RehydrateAfterLoad(g.Tasks, id => (IGoodsSupplier)g.Store,
                Fixture.Day, start + 610 * 60f, g.DueEvents, g.Diag);
            Assert.AreEqual(0, g.Driver.ActiveTrips.Count);
            StringAssert.Contains("lost its leg tasks across save/load", string.Join("\n", g.Diag));
            Assert.AreEqual(PurchasingNeedStatus.Open, gNeedCopy.Status, "aborted trip must leave the need open");

            // Phase (b) right order: tasks first, then driver list, then rehydrate.
            var h = new Fixture();
            h.Tasks.LoadFromSaveDto(tasksDto);
            h.Driver.LoadFromSaveDto(driverDto);
            h.DueEvents.LoadFromSaveDto(dueDto);
            h.Driver.RehydrateAfterLoad(h.Tasks, id => (IGoodsSupplier)h.Store,
                Fixture.Day, start + 610 * 60f, h.DueEvents, h.Diag);
            Assert.AreEqual(1, h.Driver.ActiveTrips.Count);
            Assert.IsTrue(!string.Join("\n", h.Diag).Contains("lost its leg tasks"));
        }

        [Test]
        public void SaveLoad_ProductionRestore_RefusalAndMissingBehaviour()
        {
            // Phase (a) leg task missing: remove the outbound leg task from
            // the captured tasks DTO, restore in production order, rehydrate.
            var f = new Fixture();
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            f.Driver.PlanForHousehold(Fixture.HouseholdId, f.Needs, Fixture.Day, f.LoopOptions, f.Diag);
            float start = Fixture.Day * 86400f;
            f.Driver.AdvanceTick(start + 610 * 60f, Fixture.Day, f.DueEvents, f.Diag);
            Assert.AreEqual(1, f.Driver.ActiveTrips.Count);
            PlannedShoppingTrip trip = f.Driver.ActiveTrips[0];
            var tasksDto = f.Tasks.CaptureSaveDto();
            var driverDto = f.Driver.CaptureSaveDto();
            var dueDto = f.DueEvents.CaptureSaveDto();
            tasksDto.tasks.RemoveAll(t => t.TaskId == trip.OutboundTaskId);

            var g = new Fixture();
            float gStart = Fixture.Day * 86400f;
            HouseholdPurchasingNeed gNeed = g.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            gNeed.UnitsNeeded = need.UnitsNeeded;
            g.Tasks.LoadFromSaveDto(tasksDto);
            g.Driver.LoadFromSaveDto(driverDto);
            g.DueEvents.LoadFromSaveDto(dueDto);
            g.Driver.RehydrateAfterLoad(g.Tasks, id => (IGoodsSupplier)g.Store,
                Fixture.Day, gStart + 610 * 60f, g.DueEvents, g.Diag);
            Assert.AreEqual(0, g.Driver.ActiveTrips.Count);
            StringAssert.Contains("lost its leg tasks across save/load", string.Join("\n", g.Diag));
            Assert.AreEqual(PurchasingNeedStatus.Open, gNeed.Status, "aborted trip must leave the need open");
            Assert.AreEqual(0, g.Store.CashReceived);

            // Phase (b) supplier missing: drive to arrival (AwaitingService),
            // restore in production order, rehydrate with a null delegate.
            var f2 = new Fixture();
            var log2 = new LostSaleLog();
            AddClerk(f2);
            WireService(f2, log2, 3);
            f2.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            PlannedShoppingTrip trip2 = DriveToArrival(f2, out float arrival2);
            Assert.IsTrue(IsAwaitingService(trip2));
            var tasksDto2 = f2.Tasks.CaptureSaveDto();
            var driverDto2 = f2.Driver.CaptureSaveDto();
            var dueDto2 = f2.DueEvents.CaptureSaveDto();
            var g2 = new Fixture();
            HouseholdPurchasingNeed gNeed2 = g2.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            g2.Tasks.LoadFromSaveDto(tasksDto2);
            g2.Driver.LoadFromSaveDto(driverDto2);
            g2.DueEvents.LoadFromSaveDto(dueDto2);
            g2.Driver.RehydrateAfterLoad(g2.Tasks, id => null,
                Fixture.Day, arrival2, g2.DueEvents, g2.Diag);
            Assert.AreEqual(0, g2.Driver.ActiveTrips.Count);
            StringAssert.Contains("lost its supplier across save/load", string.Join("\n", g2.Diag));
            Assert.AreEqual(PurchasingNeedStatus.Open, gNeed2.Status, "aborted trip must leave the need open");

            // Phase (c) service task gone: the (f1) fallback settles on restore.
            var f3 = new Fixture();
            var log3 = new LostSaleLog();
            AddClerk(f3);
            WireService(f3, log3, 3);
            HouseholdPurchasingNeed need3 = f3.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            PlannedShoppingTrip trip3 = DriveToArrival(f3, out float arrival3);
            Assert.IsTrue(IsAwaitingService(trip3));
            EntityId serviceTaskId3 = ServiceTaskIdOf(trip3);
            var tasksDto3 = f3.Tasks.CaptureSaveDto();
            var driverDto3 = f3.Driver.CaptureSaveDto();
            var dueDto3 = f3.DueEvents.CaptureSaveDto();
            tasksDto3.tasks.RemoveAll(t => t.TaskId == serviceTaskId3);

            var g3 = new Fixture();
            HouseholdPurchasingNeed gNeed3 = g3.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            gNeed3.UnitsNeeded = need3.UnitsNeeded;
            g3.Tasks.LoadFromSaveDto(tasksDto3);
            g3.Driver.LoadFromSaveDto(driverDto3);
            g3.DueEvents.LoadFromSaveDto(dueDto3);
            g3.Driver.RehydrateAfterLoad(g3.Tasks, id => (IGoodsSupplier)g3.Store,
                Fixture.Day, arrival3, g3.DueEvents, g3.Diag);
            Assert.AreEqual(1, g3.Driver.ActiveTrips.Count);
            PlannedShoppingTrip restored3 = g3.Driver.ActiveTrips[0];
            Assert.IsNotNull(restored3.Batch);
            Assert.AreEqual(10, restored3.UnitsSettled);
            Assert.AreEqual(PlannedShoppingTrip.TripState.InStore, restored3.State);
            Assert.AreEqual(1200, g3.Store.CashReceived);
            StringAssert.Contains("lost its live service task", string.Join("\n", g3.Diag));
        }

        [Test]
        public void SaveLoad_OldSave_NullTripList_LoadsEmpty_And_DoubleRestoreIsIdempotent()
        {
            // Slice 25 fallback (plan §7 step 1(c), adapted — same reason as
            // T1): the hub round-trip does NOT run in the harness, so the
            // old-save and double-restore pins run at the driver-DTO level.
            // A null trip list is what the hub passes for an old save
            // (SystemsSaveDto.shoppingTrips deserializes as null); the
            // driver's own null-guard is what makes that load empty. The
            // hub's null-slot handling itself is covered by Agent 3 source
            // review.
            var g = new Fixture();
            g.Driver.LoadFromSaveDto(null);
            Assert.AreEqual(0, g.Driver.ActiveTrips.Count);
            Assert.IsTrue(!g.Driver.NeedsRehydrateAfterLoad);

            // Phase (b) double restore: same driver DTO restored twice,
            // rehydrated twice. Clear-then-load must not duplicate the trip.
            var f = new Fixture();
            HouseholdPurchasingNeed need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            f.Driver.PlanForHousehold(Fixture.HouseholdId, f.Needs, Fixture.Day, f.LoopOptions, f.Diag);
            float start = Fixture.Day * 86400f;
            f.Driver.AdvanceTick(start + 610 * 60f, Fixture.Day, f.DueEvents, f.Diag);
            Assert.AreEqual(1, f.Driver.ActiveTrips.Count);
            var tasksDto = f.Tasks.CaptureSaveDto();
            var budgetsDto = f.Budgets.CaptureSaveDto();
            var schedulesDto = f.Tracker.CaptureSaveDto();
            var driverDto = f.Driver.CaptureSaveDto();
            var dueDto = f.DueEvents.CaptureSaveDto();
            Assert.AreEqual(1, driverDto.Count);

            var g2 = new Fixture();
            float gStart = Fixture.Day * 86400f;
            HouseholdPurchasingNeed gNeed = g2.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            gNeed.UnitsNeeded = need.UnitsNeeded;
            g2.Tasks.LoadFromSaveDto(tasksDto);
            g2.Budgets.LoadFromSaveDto(budgetsDto, Fixture.Day);
            g2.Tracker.LoadFromSaveDto(schedulesDto);
            g2.DueEvents.LoadFromSaveDto(dueDto);
            g2.Driver.LoadFromSaveDto(driverDto);
            Assert.AreEqual(1, g2.Driver.ActiveTrips.Count);
            Assert.IsTrue(g2.Driver.NeedsRehydrateAfterLoad);
            g2.Driver.RehydrateAfterLoad(g2.Tasks, id => (IGoodsSupplier)g2.Store,
                Fixture.Day, gStart + 610 * 60f, g2.DueEvents, g2.Diag);
            Assert.AreEqual(1, g2.Driver.ActiveTrips.Count);
            Assert.IsTrue(!g2.Driver.NeedsRehydrateAfterLoad);
            Assert.AreEqual(0, g2.Store.CashReceived);

            // Restore the SAME DTO a second time into g2, then rehydrate again.
            g2.Driver.LoadFromSaveDto(driverDto);
            Assert.AreEqual(1, g2.Driver.ActiveTrips.Count);
            Assert.IsTrue(g2.Driver.NeedsRehydrateAfterLoad);
            g2.Driver.RehydrateAfterLoad(g2.Tasks, id => (IGoodsSupplier)g2.Store,
                Fixture.Day, gStart + 610 * 60f, g2.DueEvents, g2.Diag);
            Assert.AreEqual(1, g2.Driver.ActiveTrips.Count);
            Assert.IsTrue(!g2.Driver.NeedsRehydrateAfterLoad);
            Assert.AreEqual(0, g2.Store.CashReceived);

            // Drive to completion: exactly-once settlement.
            DrainRestoredTripToCompletion(g2, gStart);
            Assert.AreEqual(0, g2.Driver.ActiveTrips.Count, "restored trip should finish\n" + string.Join("\n", g2.Diag));
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, gNeed.Status);
            Assert.AreEqual(90, g2.Store.Stock[HouseholdItemCatalog.FlourId]);
            Assert.AreEqual(1200, g2.Store.CashReceived);
        }

        [Test]
        public void RehydratePendingFlag_Lifecycle()
        {
            var f = new Fixture();
            f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            f.Driver.PlanForHousehold(Fixture.HouseholdId, f.Needs, Fixture.Day, f.LoopOptions, f.Diag);
            float start = Fixture.Day * 86400f;
            f.Driver.AdvanceTick(start + 610 * 60f, Fixture.Day, f.DueEvents, f.Diag);
            Assert.AreEqual(1, f.Driver.ActiveTrips.Count);
            // A live planned trip is not pending: no load has happened.
            Assert.IsTrue(!f.Driver.NeedsRehydrateAfterLoad);

            var dtos = f.Driver.CaptureSaveDto();
            var tasksDto = f.Tasks.CaptureSaveDto();
            var dueDto = f.DueEvents.CaptureSaveDto();
            var g = new Fixture();
            g.Tasks.LoadFromSaveDto(tasksDto);
            g.DueEvents.LoadFromSaveDto(dueDto);
            g.Driver.LoadFromSaveDto(dtos);
            Assert.IsTrue(g.Driver.NeedsRehydrateAfterLoad);

            g.Driver.RehydrateAfterLoad(g.Tasks, id => (IGoodsSupplier)g.Store,
                Fixture.Day, start + 610 * 60f, g.DueEvents, g.Diag);
            Assert.IsTrue(!g.Driver.NeedsRehydrateAfterLoad);
            Assert.AreEqual(1, g.Driver.ActiveTrips.Count);

            int duesBefore = g.DueEvents.ScheduledCount;
            g.Driver.RehydrateAfterLoad(g.Tasks, id => (IGoodsSupplier)g.Store,
                Fixture.Day, start + 610 * 60f, g.DueEvents, g.Diag);
            Assert.AreEqual(1, g.Driver.ActiveTrips.Count);
            // Note 94 condition C3: no due is minted on a second rehydrate
            // of an outbound trip (RehydrateAfterLoad mints a leg due only
            // for AtStore / AwaitingService-(f1) trips), so the count is
            // unchanged. This assertion does not pin replace-by-id.
            Assert.AreEqual(duesBefore, g.DueEvents.ScheduledCount);
            Assert.IsTrue(!g.Driver.NeedsRehydrateAfterLoad);

            var h = new Fixture();
            h.Driver.LoadFromSaveDto(new List<PlannedShoppingTripSaveDto>());
            Assert.IsTrue(!h.Driver.NeedsRehydrateAfterLoad);
        }
    }
}
