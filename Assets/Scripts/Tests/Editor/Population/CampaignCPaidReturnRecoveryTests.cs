// Campaign C (DEF-022) — paid shopping goods must never strand in loop
// custody after an aborted trip. Tests the three recovery wirings in
// ShoppingTripDriver: StartInStoreLeg failure, in-store leg completion
// failure, and the rehydrate dead-trip ladder (direct deposit when the
// holder is already home, physical return recovery when still at the
// store, loud diagnosis when neither).
using System;
using System.Collections.Generic;
using LandLedgers.Economy;
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
    [TestFixture]
    public sealed class CampaignCPaidReturnRecoveryTests
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

            /// <summary>Drives a planned trip through the outbound leg to the
            /// in-store leg (settlement done, batch in hand). Returns the trip.</summary>
            public PlannedShoppingTrip DriveToInStore(ShoppingTripDriver driver, string physicalLocation, ref string location)
            {
                var trip = driver.ActiveTrips[0];
                float departure = Day * 86400f + trip.DepartureMinuteOfDay * 60f;
                driver.AdvanceTick(departure, Day, DueEvents, Diag);
                location = physicalLocation;
                var outbound = DueEvents.PopDue(departure + 86400f)[0];
                driver.HandleDueEvent(outbound, Day, outbound.DueGameSeconds, DueEvents, Diag);
                return trip;
            }

            /// <summary>Drives a RecoveryPlanned trip home and returns its
            /// return-leg due event handling to the caller via the driver.</summary>
            public void DriveRecoveryHome(ShoppingTripDriver driver, PlannedShoppingTrip trip, int day, ref string location, ref bool returnAvailable)
            {
                float recoveryDeparture = day * 86400f + trip.DepartureMinuteOfDay * 60f;
                driver.AdvanceTick(recoveryDeparture, day, DueEvents, Diag);
                // Drain every due up to a day out, in order. Stale dues for
                // legs lost across save/load are dropped by the driver (no
                // leg-map entry after a load), so the return due is handled
                // wherever it falls in the order.
                foreach (DueEvent ev in DueEvents.PopDue(recoveryDeparture + 86400f))
                    driver.HandleDueEvent(ev, day, ev.DueGameSeconds, DueEvents, Diag);
                Assert.AreEqual(0, Inventories.GetOrCreate(HouseholdId).GetTotalUnits(HouseholdItemCatalog.FlourId));
                location = "home";
                // The arrival retry was scheduled during the first drain (+60s).
                foreach (DueEvent ev in DueEvents.PopDue(recoveryDeparture + 86400f))
                    driver.HandleDueEvent(ev, day, ev.DueGameSeconds, DueEvents, Diag);
            }
        }

        [Test]
        public void StartInStoreLegFailure_QueuesPaidReturnRecovery()
        {
            var f = new Fixture();
            var need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            string physicalLocation = "home";
            bool returnAvailable = true;
            f.Budgets.EnsureDay(Fixture.Day);
            var driver = new ShoppingTripDriver(f.Loop, (p, l) => l != "home" || returnAvailable,
                (p, l) => l == physicalLocation, (p, l) => true);
            driver.PlanForHousehold(Fixture.HouseholdId, f.Needs, Fixture.Day, f.LoopOptions, f.Diag);
            var trip = driver.ActiveTrips[0];
            // Planning defect: the in-store leg was never planned. Settlement
            // still runs at arrival, so the shopper holds paid goods when the
            // leg start fails.
            trip.Legs.InStore = null;
            f.DriveToInStore(driver, "store", ref physicalLocation);
            Assert.AreEqual(PlannedShoppingTrip.TripState.Failed, trip.State);
            Assert.AreEqual(1200, f.Store.CashReceived);
            Assert.AreEqual(90, f.Store.Stock[HouseholdItemCatalog.FlourId]);
            Assert.AreEqual(1, f.Loop.ActiveCustodyBatches.Count);
            Assert.AreEqual(0, f.Inventories.GetOrCreate(Fixture.HouseholdId).GetTotalUnits(HouseholdItemCatalog.FlourId));
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status);

            // Next day the paid batch gets a reserved recovery leg, not a
            // duplicate purchase — and driving it home deposits the goods.
            f.Budgets.EnsureDay(Fixture.Day + 1);
            driver.PlanForHousehold(Fixture.HouseholdId, f.Needs, Fixture.Day + 1, f.LoopOptions, f.Diag);
            Assert.AreEqual(1, driver.ActiveTrips.Count);
            Assert.AreEqual(PlannedShoppingTrip.TripState.RecoveryPlanned, trip.State);
            f.DriveRecoveryHome(driver, trip, Fixture.Day + 1, ref physicalLocation, ref returnAvailable);
            Assert.AreEqual(PlannedShoppingTrip.TripState.Done, trip.State);
            Assert.AreEqual(10, f.Inventories.GetOrCreate(Fixture.HouseholdId).GetTotalUnits(HouseholdItemCatalog.FlourId));
            Assert.AreEqual(0, f.Loop.ActiveCustodyBatches.Count);
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status);
            Assert.AreEqual(1200, f.Store.CashReceived);
        }

        [Test]
        public void InStoreLegCompletionFailure_QueuesPaidReturnRecovery()
        {
            var f = new Fixture();
            var need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            string physicalLocation = "home";
            bool returnAvailable = true;
            f.Budgets.EnsureDay(Fixture.Day);
            var driver = new ShoppingTripDriver(f.Loop, (p, l) => l != "home" || returnAvailable,
                (p, l) => l == physicalLocation, (p, l) => true);
            driver.PlanForHousehold(Fixture.HouseholdId, f.Needs, Fixture.Day, f.LoopOptions, f.Diag);
            f.DriveToInStore(driver, "store", ref physicalLocation);
            var trip = driver.ActiveTrips[0];
            Assert.AreEqual(PlannedShoppingTrip.TripState.InStore, trip.State);
            Assert.AreEqual(1200, f.Store.CashReceived);
            // Task bookkeeping failure: the in-store leg task is cancelled
            // before its due fires, so completing it is refused.
            f.Tasks.CancelTask(trip.Legs.InStore.TaskId, f.Budgets);
            var inStore = f.DueEvents.PopDue(Fixture.Day * 86400f + 86400f)[0];
            driver.HandleDueEvent(inStore, Fixture.Day, inStore.DueGameSeconds, f.DueEvents, f.Diag);
            Assert.AreEqual(PlannedShoppingTrip.TripState.Failed, trip.State);
            Assert.AreEqual(1, f.Loop.ActiveCustodyBatches.Count);
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status);

            f.Budgets.EnsureDay(Fixture.Day + 1);
            driver.PlanForHousehold(Fixture.HouseholdId, f.Needs, Fixture.Day + 1, f.LoopOptions, f.Diag);
            Assert.AreEqual(1, driver.ActiveTrips.Count);
            Assert.AreEqual(PlannedShoppingTrip.TripState.RecoveryPlanned, trip.State);
            f.DriveRecoveryHome(driver, trip, Fixture.Day + 1, ref physicalLocation, ref returnAvailable);
            Assert.AreEqual(PlannedShoppingTrip.TripState.Done, trip.State);
            Assert.AreEqual(10, f.Inventories.GetOrCreate(Fixture.HouseholdId).GetTotalUnits(HouseholdItemCatalog.FlourId));
            Assert.AreEqual(0, f.Loop.ActiveCustodyBatches.Count);
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status);
        }

        [Test]
        public void RehydrateDeadTrip_HolderAtHome_DepositsDirectly()
        {
            var f = new Fixture();
            var need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            string physicalLocation = "home";
            bool returnAvailable = true;
            f.Budgets.EnsureDay(Fixture.Day);
            var driver = new ShoppingTripDriver(f.Loop, (p, l) => l != "home" || returnAvailable,
                (p, l) => l == physicalLocation, (p, l) => true);
            driver.PlanForHousehold(Fixture.HouseholdId, f.Needs, Fixture.Day, f.LoopOptions, f.Diag);
            f.DriveToInStore(driver, "store", ref physicalLocation);
            var trip = driver.ActiveTrips[0];
            Assert.AreEqual(PlannedShoppingTrip.TripState.InStore, trip.State);
            Assert.AreEqual(1200, f.Store.CashReceived);
            Assert.AreEqual(1, f.Loop.ActiveCustodyBatches.Count);

            // Round-trip through the real save DTOs first, the way production
            // loads: the load clears the driver's leg map and re-points the
            // trip at the loop-owned custody batch.
            var dtos = driver.CaptureSaveDto();
            driver.LoadFromSaveDto(dtos);
            trip = driver.ActiveTrips[0];

            // Save/load lost the leg tasks (fresh TaskAuthority knows none
            // of them); the holder is already home, so the batch deposits
            // directly through ReceiveCarriedBatch — no travel to plan.
            physicalLocation = "home";
            var emptyTasks = new TaskAuthority();
            driver.RehydrateAfterLoad(emptyTasks, id => (IGoodsSupplier)f.Store,
                Fixture.Day, Fixture.Day * 86400f + 700 * 60f, f.DueEvents, f.Diag);
            Assert.AreEqual(0, driver.ActiveTrips.Count);
            Assert.AreEqual(PlannedShoppingTrip.TripState.Failed, trip.State);
            Assert.AreEqual(10, f.Inventories.GetOrCreate(Fixture.HouseholdId).GetTotalUnits(HouseholdItemCatalog.FlourId));
            Assert.AreEqual(0, f.Loop.ActiveCustodyBatches.Count);
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status);
            Assert.AreEqual(1200, f.Store.CashReceived);
        }

        [Test]
        public void RehydrateDeadTrip_HolderAtStore_QueuesPhysicalRecovery()
        {
            var f = new Fixture();
            var need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            string physicalLocation = "home";
            bool returnAvailable = true;
            f.Budgets.EnsureDay(Fixture.Day);
            var driver = new ShoppingTripDriver(f.Loop, (p, l) => l != "home" || returnAvailable,
                (p, l) => l == physicalLocation, (p, l) => true);
            driver.PlanForHousehold(Fixture.HouseholdId, f.Needs, Fixture.Day, f.LoopOptions, f.Diag);
            f.DriveToInStore(driver, "store", ref physicalLocation);
            var trip = driver.ActiveTrips[0];
            Assert.AreEqual(PlannedShoppingTrip.TripState.InStore, trip.State);

            // Round-trip through the real save DTOs first, the way production
            // loads: the load clears the driver's leg map and re-points the
            // trip at the loop-owned custody batch.
            var dtos = driver.CaptureSaveDto();
            driver.LoadFromSaveDto(dtos);
            trip = driver.ActiveTrips[0];

            // Leg tasks lost across save/load; the holder is still at the
            // store, so the physical paid return recovery is queued.
            var emptyTasks = new TaskAuthority();
            driver.RehydrateAfterLoad(emptyTasks, id => (IGoodsSupplier)f.Store,
                Fixture.Day, Fixture.Day * 86400f + 700 * 60f, f.DueEvents, f.Diag);
            Assert.AreEqual(0, driver.ActiveTrips.Count);
            Assert.AreEqual(1, f.Loop.ActiveCustodyBatches.Count);
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status);

            // The fixture's TaskAuthority still holds the original in-store
            // task in-progress; in the restored world that task does not
            // exist, so it cannot occupy the shopper. Cancel it here to
            // model the restored authority (cancel releases the occupancy;
            // the rehydrate lookup above already ran against the empty
            // authority). The outbound task already completed, so its
            // cancel is a harmless no-op.
            f.Tasks.CancelTask(trip.InStoreTaskId, f.Budgets);
            f.Tasks.CancelTask(trip.OutboundTaskId, f.Budgets);

            f.Budgets.EnsureDay(Fixture.Day + 1);
            driver.PlanForHousehold(Fixture.HouseholdId, f.Needs, Fixture.Day + 1, f.LoopOptions, f.Diag);
            Assert.AreEqual(1, driver.ActiveTrips.Count);
            Assert.AreEqual(PlannedShoppingTrip.TripState.RecoveryPlanned, trip.State);
            f.DriveRecoveryHome(driver, trip, Fixture.Day + 1, ref physicalLocation, ref returnAvailable);
            Assert.AreEqual(PlannedShoppingTrip.TripState.Done, trip.State);
            Assert.AreEqual(10, f.Inventories.GetOrCreate(Fixture.HouseholdId).GetTotalUnits(HouseholdItemCatalog.FlourId));
            Assert.AreEqual(0, f.Loop.ActiveCustodyBatches.Count);
            Assert.AreEqual(PurchasingNeedStatus.Fulfilled, need.Status);
        }

        [Test]
        public void RehydrateDeadTrip_HolderNowhere_DiagnosesAndBlocksDuplicatePurchase()
        {
            var f = new Fixture();
            var need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            string physicalLocation = "home";
            bool returnAvailable = true;
            f.Budgets.EnsureDay(Fixture.Day);
            var driver = new ShoppingTripDriver(f.Loop, (p, l) => l != "home" || returnAvailable,
                (p, l) => l == physicalLocation, (p, l) => true);
            driver.PlanForHousehold(Fixture.HouseholdId, f.Needs, Fixture.Day, f.LoopOptions, f.Diag);
            f.DriveToInStore(driver, "store", ref physicalLocation);
            var trip = driver.ActiveTrips[0];
            Assert.AreEqual(PlannedShoppingTrip.TripState.InStore, trip.State);

            // Round-trip through the real save DTOs first, the way production
            // loads: the load clears the driver's leg map and re-points the
            // trip at the loop-owned custody batch.
            var dtos = driver.CaptureSaveDto();
            driver.LoadFromSaveDto(dtos);
            trip = driver.ActiveTrips[0];

            // Leg tasks lost; the holder is neither at the store nor at
            // home, so no recovery can be planned — the stranding must be
            // loud, and no duplicate purchase may be planned for the need.
            physicalLocation = "nowhere";
            var emptyTasks = new TaskAuthority();
            driver.RehydrateAfterLoad(emptyTasks, id => (IGoodsSupplier)f.Store,
                Fixture.Day, Fixture.Day * 86400f + 700 * 60f, f.DueEvents, f.Diag);
            Assert.AreEqual(0, driver.ActiveTrips.Count);
            Assert.AreEqual(1, f.Loop.ActiveCustodyBatches.Count);
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status);
            bool diagnosed = false;
            foreach (string line in f.Diag)
                if (line.Contains("stays in custody after save/load")) { diagnosed = true; break; }
            Assert.IsTrue(diagnosed, "stranded paid batch must be diagnosed loudly");

            f.Budgets.EnsureDay(Fixture.Day + 1);
            driver.PlanForHousehold(Fixture.HouseholdId, f.Needs, Fixture.Day + 1, f.LoopOptions, f.Diag);
            Assert.AreEqual(0, driver.ActiveTrips.Count, "pending paid batch blocks a duplicate purchase");
            Assert.AreEqual(1, f.Loop.ActiveCustodyBatches.Count);
        }
    }
}
