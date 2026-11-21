using System;
using System.Collections.Generic;
using System.Linq;
using LandLedgers.Economy.Equipment;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.Editor.Population
{
    /// <summary>
    /// Lane 9e: a store shared-stock case is reservation-covered — no
    /// double-sale. The store holds 3 sacks of flour (the store flour-lot
    /// case: reservations are in store units, sacks, not household pounds).
    /// One household's planned trip holds the last sacks; a second promise
    /// against the same stock is refused; abort releases; and running the
    /// first trip to settlement sells the sacks exactly once, so the second
    /// household cannot buy them afterwards.
    /// </summary>
    [TestFixture]
    public sealed class Lane9eShoppingReservationTests
    {
        private sealed class TestStore : IItemResolvingSupplier, IPackageUnitSupplier
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
            public int HouseholdUnitsPerStoreUnit(string itemId) =>
                string.Equals(itemId, HouseholdItemCatalog.FlourId, StringComparison.Ordinal) ? 10 : 1;

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
        public void SharedStockHold_BlocksSecondPromise_AndReleasesOnAbort()
        {
            var f = new Fixture();
            HouseholdPurchasingNeed needA = f.OpenNeed(Fixture.HouseholdId, HouseholdItemCatalog.FlourId, 30);

            f.Driver.PlanForHousehold(Fixture.HouseholdId, f.Needs, Fixture.Day, f.LoopOptions, f.Diag);
            Assert.AreEqual(1, f.Driver.ActiveTrips.Count, "the first household's trip must plan\n" + string.Join("\n", f.Diag));
            PlannedShoppingTrip tripA = f.Driver.ActiveTrips.Single();
            Assert.AreEqual(PlannedShoppingTrip.TripState.Planned, tripA.State, tripA.FailureReason);
            Assert.AreEqual(3, tripA.Candidate.UnitsPlanned, "30 lb of flour is 3 sacks in store units");
            Assert.IsFalse(string.IsNullOrWhiteSpace(tripA.StockReservationId),
                "the planned trip holds its sacks on the unified authority");

            float atDeparture = RetailStockReservation.GameSeconds(Fixture.Day, tripA.DepartureMinuteOfDay);
            Assert.AreEqual(0, RetailStockReservation.AvailableUnits(
                    f.Authority, "store-1", HouseholdItemCatalog.FlourId, 3, atDeparture),
                "the held sacks are not promiseable to anyone else at departure");

            string secondHold = RetailStockReservation.TryReserveUnits(
                f.Authority, "store-1", HouseholdItemCatalog.FlourId, 3, 3, "trip-b", "shopping trip for H8",
                atDeparture, atDeparture + 4 * 3600f, null);
            Assert.IsNull(secondHold, "a second promise of the same 3 sacks must be refused");

            f.Loop.AbortTrip(tripA, "test abort", Fixture.Day, f.Diag);
            Assert.AreEqual(PlannedShoppingTrip.TripState.Failed, tripA.State);
            Assert.IsTrue(string.IsNullOrWhiteSpace(tripA.StockReservationId), "abort clears the trip's hold id");
            Assert.AreEqual(0, f.Authority.ReservationsForTask(tripA.TripId).Count, "abort releases the hold");
            Assert.AreEqual(3, RetailStockReservation.AvailableUnits(
                    f.Authority, "store-1", HouseholdItemCatalog.FlourId, 3, atDeparture),
                "abort frees the sacks for a fresh plan");
            Assert.AreEqual(3, f.Store.Stock[HouseholdItemCatalog.FlourId], "abort sells nothing");

            // The need stays open and replans: a direct PlanTrip (the
            // driver's ActiveTrips pruning runs on its own ticks, not on a
            // direct abort) must plan again and hold the sacks again.
            Assert.Contains(needA, f.Needs.GetOpenNeeds(Fixture.HouseholdId), "abort leaves the need open");
            PlannedShoppingTrip tripA2 = f.Loop.PlanTrip(needA, Fixture.Day, f.LoopOptions);
            Assert.AreEqual(PlannedShoppingTrip.TripState.Planned, tripA2.State, tripA2.FailureReason);
            Assert.IsFalse(string.IsNullOrWhiteSpace(tripA2.StockReservationId),
                "the replanned trip holds its sacks again");
            Assert.AreNotEqual(tripA.StockReservationId, tripA2.StockReservationId,
                "the replanned hold is a fresh reservation");
        }

        [Test]
        public void LastSacksSoldOnce_SecondHouseholdCannotBuyThem()
        {
            var f = new Fixture();
            HouseholdPurchasingNeed needA = f.OpenNeed(Fixture.HouseholdId, HouseholdItemCatalog.FlourId, 30);

            f.Driver.PlanForHousehold(Fixture.HouseholdId, f.Needs, Fixture.Day, f.LoopOptions, f.Diag);
            Assert.AreEqual(1, f.Driver.ActiveTrips.Count);
            PlannedShoppingTrip tripA = f.Driver.ActiveTrips.Single();
            Assert.AreEqual(PlannedShoppingTrip.TripState.Planned, tripA.State, tripA.FailureReason);

            f.RunUntilDone(tickSeconds: 300);

            Assert.AreEqual(0, f.Driver.ActiveTrips.Count, "trip should be done\n" + string.Join("\n", f.Diag));
            Assert.AreEqual(PlannedShoppingTrip.TripState.Done, tripA.State, "trip state: " + tripA.FailureReason);
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, needA.Status);
            Assert.AreEqual(0, f.Store.Stock[HouseholdItemCatalog.FlourId], "the last 3 sacks are sold");
            Assert.AreEqual(360, f.Store.CashReceived, "3 sacks x 120c, exactly once");
            Assert.AreEqual(50000 - 360, f.Ledgers.GetOrCreate(Fixture.HouseholdId).GetBalanceCents());
            Assert.IsTrue(string.IsNullOrWhiteSpace(tripA.StockReservationId), "a done trip holds nothing");
            Assert.AreEqual(0, f.Authority.LiveReservationCount, "no hold survives the settled trip");

            HouseholdPurchasingNeed needB = f.OpenNeed(Fixture.SecondHouseholdId, HouseholdItemCatalog.FlourId, 30);
            f.Driver.PlanForHousehold(Fixture.SecondHouseholdId, f.Needs, Fixture.Day, f.LoopOptions, f.Diag);
            Assert.AreEqual(0, f.Driver.ActiveTrips.Count, "no sacks remain, so no trip plans for the second household");
            Assert.AreEqual(PurchasingNeedStatus.Open, needB.Status, "the second household's need stays open");
            Assert.AreEqual(50000, f.Ledgers.GetOrCreate(Fixture.SecondHouseholdId).GetBalanceCents(),
                "the failed plan debits nothing");
            Assert.AreEqual(360, f.Store.CashReceived, "no second sale");
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
            public readonly EquipmentReservationService Authority = new EquipmentReservationService();
            public readonly TestStore Store = new TestStore();
            public readonly HouseholdShoppingLoop Loop;
            public readonly ShoppingTripDriver Driver;
            public readonly Dictionary<int, string> Locations = new Dictionary<int, string>();
            public readonly List<string> Diag = new List<string>();

            public const int HouseholdId = 7;
            public const int SecondHouseholdId = 8;
            public const int Day = 100;

            public Fixture()
            {
                Population.people.Add(new PersonState { id = 1, firstName = "Tomas", lastName = "Morrow", age = 34, ageBand = AgeBand.Adult18Plus });
                Population.people.Add(new PersonState { id = 2, firstName = "Petra", lastName = "Morrow", age = 8, ageBand = AgeBand.Child0To9 });
                Population.people.Add(new PersonState { id = 3, firstName = "Mara", lastName = "Morrow", age = 30, ageBand = AgeBand.Adult18Plus });
                Population.households.Add(new HouseholdState { id = HouseholdId, householdName = "Morrow" });
                Population.households.Add(new HouseholdState { id = SecondHouseholdId, householdName = "Morrow Second" });
                Membership.Register(new HouseholdMembership { MembershipId = 1, PersonId = 1, HouseholdId = HouseholdId, StartDayIndex = 0 });
                Membership.Register(new HouseholdMembership { MembershipId = 2, PersonId = 2, HouseholdId = HouseholdId, StartDayIndex = 0 });
                Membership.Register(new HouseholdMembership { MembershipId = 3, PersonId = 3, HouseholdId = SecondHouseholdId, StartDayIndex = 0 });

                Journeys.RegisterLocation(new JourneyLocation("home", JourneyLocationKind.Farmstead, "Morrow Farm", 0f, 0f));
                Journeys.RegisterLocation(new JourneyLocation("store", JourneyLocationKind.Store, "General Store", 2f, 0f));
                Journeys.AddEdge("home", "store", 2.0f, "river road");

                // 3 sacks of flour (1 sack = 10 lb), 120c a sack.
                Store.Stock[HouseholdItemCatalog.FlourId] = 3;
                Store.Prices[HouseholdItemCatalog.FlourId] = 120;
                Directory.Register(Store);
                Knowledge.LearnSupplier(1, "store-1", "lives in the same town", 0);
                Knowledge.LearnSupplier(3, "store-1", "lives in the same town", 0);

                Locations[1] = "home";
                Locations[2] = "home";
                Locations[3] = "home";

                HouseholdShoppingLoop.RegisterTaskDefinitions(Tasks);
                LoopOptions = new ShoppingLoopOptions
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
                    spendingPolicyProvider: null,
                    reservationAuthority: Authority);
                Driver = new ShoppingTripDriver(Loop);

                Ledgers.GetOrCreate(HouseholdId).RecordInflow(0, 50000,
                    HouseholdIncomeSource.WageEmployment, "emp-1", "seed wages", "test");
                Ledgers.GetOrCreate(SecondHouseholdId).RecordInflow(0, 50000,
                    HouseholdIncomeSource.WageEmployment, "emp-2", "seed wages", "test");
            }

            public ShoppingLoopOptions LoopOptions;

            public HouseholdPurchasingNeed OpenNeed(int householdId, string itemId, int units)
            {
                HouseholdPurchasingNeed need = Needs.CreateNeed(householdId, itemId, Day);
                need.UnitsNeeded = units;
                need.IsNecessity = true;
                return need;
            }

            public void RunUntilDone(int tickSeconds, int maxTicks = 2000)
            {
                float start = Day * 86400f;
                for (int t = 0; t < maxTicks && Driver.ActiveTrips.Count > 0; t++)
                {
                    float now = start + t * tickSeconds;
                    DrainDue(now);
                    Driver.AdvanceTick(now, Day, DueEvents, Diag);
                }

                for (int i = 0; i < 50 && (DueEvents.ScheduledCount > 0 || Driver.ActiveTrips.Count > 0); i++)
                {
                    DrainDue(start + 3 * 86400f);
                }
            }

            private void DrainDue(float now)
            {
                for (int pass = 0; pass < 256; pass++)
                {
                    List<DueEvent> due = DueEvents.PopDue(now);
                    if (due.Count == 0) return;
                    foreach (DueEvent ev in due)
                        Driver.HandleDueEvent(ev, Day, ev.DueGameSeconds, DueEvents, Diag);
                }
                Diag.Add($"Test driver due-event drain reached its 256-event guard at {now}.");
            }
        }
    }
}
