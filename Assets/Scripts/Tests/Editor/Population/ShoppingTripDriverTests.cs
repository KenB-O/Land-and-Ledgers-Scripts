using System.Collections.Generic;
using System.Linq;
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
    }
}
