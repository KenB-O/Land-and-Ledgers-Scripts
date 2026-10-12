using System.Collections.Generic;
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
