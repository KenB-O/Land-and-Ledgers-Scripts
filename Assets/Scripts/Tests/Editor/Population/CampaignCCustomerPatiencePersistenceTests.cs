// Campaign C (DEF-073) — a waiting customer's patience must survive
// save/load. The queue DTO already carried remaining + initial patience
// minutes, but Series2Pass9SaveState.CustomerQueue was never bound to the
// live store queue, so every save wrote an empty queue section and every
// load dropped the whole waiting line: a customer about to walk out got a
// fresh full wait (or never balked at all), and a restored trip waited with
// no queue entry. Tests the fix at three levels: the queue DTO round-trip,
// the driver's patience tick with and without the post-load watermark
// re-arm (fresh scene and in-session load), the balk release, the
// settled-entry sweep (DEF-042 interaction), and the two production
// bindings plus the re-arm call as structural pins.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LandLedgers.Economy;
using LandLedgers.Economy.GeneralStore;
using LandLedgers.Economy.Retail;
using LandLedgers.FirstLedger;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.Editor.Population
{
    [TestFixture]
    public sealed class CampaignCCustomerPatiencePersistenceTests
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
            public readonly CustomerQueue Queue;
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
                // The TendCustomer service task the arrival path creates for
                // a queued shopper (DEF-073 fixture): without this the
                // TaskAuthority throws Unknown TaskDefinitionId gs.tend-customer.
                GeneralStoreTaskCatalog.RegisterAll(Tasks);
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

                // The live store queue the loop resolves, the way the
                // production manager's queue is bound into the Pass 9 save
                // state. The clerk resolver stays unset by default (no clerk
                // -> settle at arrival, as today); tests that need a waiting
                // customer set it to person 1 before driving.
                Queue = new CustomerQueue();
                Loop.ResolveCustomerQueue = _ => Queue;

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

            /// <summary>Drives a planned trip through the outbound leg to the
            /// counter queue (AwaitingService): the arrival path enqueues the
            /// shopper and starts a TendCustomer task for the clerk. The
            /// clerk resolver must be set before calling. Returns the trip
            /// and the arrival game seconds (when the queue entry was
            /// created, with a full 20-minute patience budget).</summary>
            public PlannedShoppingTrip DriveToAwaitingService(ShoppingTripDriver driver, string physicalLocation, ref string location, out float arrivalGameSeconds)
            {
                var trip = driver.ActiveTrips[0];
                float departure = Day * 86400f + trip.DepartureMinuteOfDay * 60f;
                driver.AdvanceTick(departure, Day, DueEvents, Diag);
                location = physicalLocation;
                var outbound = DueEvents.PopDue(departure + 86400f)[0];
                arrivalGameSeconds = outbound.DueGameSeconds;
                driver.HandleDueEvent(outbound, Day, outbound.DueGameSeconds, DueEvents, Diag);
                Assert.AreEqual(PlannedShoppingTrip.TripState.AwaitingService, trip.State,
                    "arrival must queue the shopper and start clerk service; diag: " + string.Join(" | ", Diag));
                Assert.AreEqual(1, Queue.WaitingCount);
                Assert.AreEqual(CustomerQueue.DefaultPatienceMinutes, Queue.Waiting[0].PatienceMinutesRemaining);
                return trip;
            }

            /// <summary>Builds a driver with the fixture delegates, plans one
            /// trip for the household, and drives it to the counter queue.
            /// Returns the driver and the arrival game seconds.</summary>
            public ShoppingTripDriver DriveAwaitingServiceTrip(out float arrivalGameSeconds, out PlannedShoppingTrip trip)
            {
                var need = OpenNeed(HouseholdItemCatalog.FlourId, 10);
                string physicalLocation = "home";
                bool returnAvailable = true;
                Budgets.EnsureDay(Day);
                Loop.ResolveServiceClerkPersonId = _ => 1;
                var driver = new ShoppingTripDriver(Loop, (p, l) => l != "home" || returnAvailable,
                    (p, l) => l == physicalLocation, (p, l) => true);
                driver.PlanForHousehold(HouseholdId, Needs, Day, LoopOptions, Diag);
                trip = DriveToAwaitingService(driver, "store", ref physicalLocation, out arrivalGameSeconds);
                return driver;
            }
        }

        [Test]
        public void QueueDto_RoundTrip_PreservesDecayedPatience()
        {
            var queue = new CustomerQueue();
            Assert.IsTrue(queue.Enqueue("shop:7:1", 1, "staple_food", 10, 120, Fixture.Day));
            Assert.AreEqual(0, queue.Tick(17, Fixture.Day), "17 min must not balk a 20-min budget");
            Assert.AreEqual(3, queue.Waiting[0].PatienceMinutesRemaining);

            var dto = queue.CaptureSaveDto();
            var restored = new CustomerQueue();
            restored.LoadFromSaveDto(dto);
            Assert.AreEqual(1, restored.WaitingCount);
            Assert.AreEqual("shop:7:1", restored.Waiting[0].CustomerRef);
            Assert.AreEqual(3, restored.Waiting[0].PatienceMinutesRemaining,
                "remaining patience must survive the round-trip, not reset to the full budget");
            Assert.AreEqual(20, restored.Waiting[0].PatienceMinutesInitial,
                "the patience budget travels with the remaining minutes, so the balk diag states true minutes waited");
            Assert.AreEqual(Fixture.Day, restored.Waiting[0].QueuedDayIndex);
        }

        [Test]
        public void QueueDto_NullSection_ClearsLine()
        {
            var queue = new CustomerQueue();
            Assert.IsTrue(queue.Enqueue("shop:7:1", 1, "staple_food", 10, 120, Fixture.Day));
            Assert.AreEqual(1, queue.WaitingCount);
            // An old save with no queue section loads as an empty line, the
            // same default the pre-fix binding produced for every save.
            queue.LoadFromSaveDto(null);
            Assert.AreEqual(0, queue.WaitingCount);
        }

        [Test]
        public void FreshDriver_FirstTickSkipsDecay_WithoutRearm()
        {
            var f = new Fixture();
            float arrival;
            PlannedShoppingTrip trip;
            var driver = f.DriveAwaitingServiceTrip(out arrival, out trip);

            // Save the way production saves: trip DTOs + the queue DTO.
            var dtos = driver.CaptureSaveDto();
            var queueDto = f.Queue.CaptureSaveDto();

            // Fresh-scene load: a new driver, trips rehydrated, queue
            // restored clear-then-load into the same instance the loop
            // resolves — the production restore order.
            string physicalLocation = "store";
            bool returnAvailable = true;
            var fresh = new ShoppingTripDriver(f.Loop, (p, l) => l != "home" || returnAvailable,
                (p, l) => l == physicalLocation, (p, l) => true);
            fresh.LoadFromSaveDto(dtos);
            f.Queue.LoadFromSaveDto(queueDto);
            fresh.RehydrateAfterLoad(f.Tasks, id => (IGoodsSupplier)f.Store,
                Fixture.Day, arrival, f.DueEvents, f.Diag);
            Assert.AreEqual(PlannedShoppingTrip.TripState.AwaitingService, fresh.ActiveTrips[0].State);
            Assert.AreEqual(20, f.Queue.Waiting[0].PatienceMinutesRemaining);

            // Without the DEF-073 re-arm the fresh driver's watermark starts
            // at -1, so the first post-load tick only arms it: 15 game-min
            // of decay are skipped and the restored patience does not move.
            fresh.AdvanceTick(arrival + 900f, Fixture.Day, f.DueEvents, f.Diag);
            Assert.AreEqual(20, f.Queue.Waiting[0].PatienceMinutesRemaining,
                "first post-load tick on a fresh driver must only arm the watermark");
            // The next tick decays normally from the armed watermark.
            fresh.AdvanceTick(arrival + 1800f, Fixture.Day, f.DueEvents, f.Diag);
            Assert.AreEqual(5, f.Queue.Waiting[0].PatienceMinutesRemaining);
        }

        [Test]
        public void FreshDriver_Rearm_DecaysFromRestoredClock()
        {
            var f = new Fixture();
            float arrival;
            PlannedShoppingTrip trip;
            var driver = f.DriveAwaitingServiceTrip(out arrival, out trip);

            var dtos = driver.CaptureSaveDto();
            var queueDto = f.Queue.CaptureSaveDto();

            string physicalLocation = "store";
            bool returnAvailable = true;
            var fresh = new ShoppingTripDriver(f.Loop, (p, l) => l != "home" || returnAvailable,
                (p, l) => l == physicalLocation, (p, l) => true);
            fresh.LoadFromSaveDto(dtos);
            f.Queue.LoadFromSaveDto(queueDto);
            fresh.RehydrateAfterLoad(f.Tasks, id => (IGoodsSupplier)f.Store,
                Fixture.Day, arrival, f.DueEvents, f.Diag);
            Assert.AreEqual(PlannedShoppingTrip.TripState.AwaitingService, fresh.ActiveTrips[0].State);

            // The DEF-073 re-arm puts the watermark at the restored clock,
            // so the restored patience decays on the very first post-load tick.
            fresh.RearmQueueTickWatermark(arrival);
            fresh.AdvanceTick(arrival + 900f, Fixture.Day, f.DueEvents, f.Diag);
            Assert.AreEqual(5, f.Queue.Waiting[0].PatienceMinutesRemaining,
                "re-armed watermark must decay from the restored clock on the first tick");
        }

        [Test]
        public void RestoredPatience_Balk_Fires_AfterRearm()
        {
            var f = new Fixture();
            var need = f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            string physicalLocation = "home";
            bool returnAvailable = true;
            f.Budgets.EnsureDay(Fixture.Day);
            f.Loop.ResolveServiceClerkPersonId = _ => 1;
            var driver = new ShoppingTripDriver(f.Loop, (p, l) => l != "home" || returnAvailable,
                (p, l) => l == physicalLocation, (p, l) => true);
            driver.PlanForHousehold(Fixture.HouseholdId, f.Needs, Fixture.Day, f.LoopOptions, f.Diag);
            float arrival;
            var trip = f.DriveToAwaitingService(driver, "store", ref physicalLocation, out arrival);

            var dtos = driver.CaptureSaveDto();
            var queueDto = f.Queue.CaptureSaveDto();

            var fresh = new ShoppingTripDriver(f.Loop, (p, l) => l != "home" || returnAvailable,
                (p, l) => l == physicalLocation, (p, l) => true);
            fresh.LoadFromSaveDto(dtos);
            f.Queue.LoadFromSaveDto(queueDto);
            fresh.RehydrateAfterLoad(f.Tasks, id => (IGoodsSupplier)f.Store,
                Fixture.Day, arrival, f.DueEvents, f.Diag);
            trip = fresh.ActiveTrips[0];
            Assert.AreEqual(PlannedShoppingTrip.TripState.AwaitingService, trip.State);

            // Decay the restored patience to 5, then past zero: the
            // customer balks, the service task is cancelled, the trip
            // returns empty-handed through the real return leg, the need
            // stays open, and one QueueBalk lost sale is recorded.
            fresh.RearmQueueTickWatermark(arrival);
            fresh.AdvanceTick(arrival + 900f, Fixture.Day, f.DueEvents, f.Diag);
            Assert.AreEqual(5, f.Queue.Waiting[0].PatienceMinutesRemaining);
            fresh.AdvanceTick(arrival + 1800f, Fixture.Day, f.DueEvents, f.Diag);
            Assert.AreEqual(0, f.Queue.WaitingCount, "balked customer leaves the line");
            Assert.AreEqual(PlannedShoppingTrip.TripState.Returning, trip.State);
            Assert.AreEqual(PurchasingNeedStatus.Open, need.Status);
            Assert.AreEqual(1, f.Queue.LostSales.Count, "one QueueBalk lost sale");
            List<LostSaleEvent> events = f.Queue.LostSales.EventsFor(f.Queue.MerchantBusinessId);
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(LostSaleReason.QueueBalk, events[0].Reason);
        }

        [Test]
        public void ReusedDriver_PausesWithoutRearm_ResumesWithRearm()
        {
            var f = new Fixture();
            float arrival;
            PlannedShoppingTrip trip;
            var driver = f.DriveAwaitingServiceTrip(out arrival, out trip);

            var dtos = driver.CaptureSaveDto();
            var queueDto = f.Queue.CaptureSaveDto();

            // Play past the save point with the queue entry temporarily out
            // of the line, so the reused driver's watermark advances to
            // arrival + 1h without the entry decaying (production ticks
            // advance the watermark while the player plays; the entry only
            // decays while it is waiting).
            string customerRef = HouseholdShoppingLoop.ServiceCustomerRefFor(trip);
            Assert.IsTrue(f.Queue.TryRemove(customerRef));
            driver.AdvanceTick(arrival + 3600f, Fixture.Day, f.DueEvents, f.Diag);
            Assert.AreEqual(0, f.Queue.WaitingCount);

            // In-session load of the earlier save: the clock rewinds to the
            // save point and the trip and queue entry are restored, but the
            // reused driver's watermark stays an hour ahead of the rewound
            // clock.
            driver.LoadFromSaveDto(dtos);
            f.Queue.LoadFromSaveDto(queueDto);
            driver.RehydrateAfterLoad(f.Tasks, id => (IGoodsSupplier)f.Store,
                Fixture.Day, arrival, f.DueEvents, f.Diag);
            trip = driver.ActiveTrips[0];
            Assert.AreEqual(PlannedShoppingTrip.TripState.AwaitingService, trip.State);
            Assert.AreEqual(20, f.Queue.Waiting[0].PatienceMinutesRemaining);

            // Without the re-arm the decay pauses: the watermark is ahead of
            // the rewound clock, so the first post-load tick moves nothing.
            driver.AdvanceTick(arrival + 900f, Fixture.Day, f.DueEvents, f.Diag);
            Assert.AreEqual(20, f.Queue.Waiting[0].PatienceMinutesRemaining,
                "reused driver without re-arm must not decay while the watermark is ahead");

            // The DEF-073 re-arm puts the watermark back at the restored
            // clock; decay resumes from the restored time on the next tick.
            driver.RearmQueueTickWatermark(arrival);
            driver.AdvanceTick(arrival + 900f, Fixture.Day, f.DueEvents, f.Diag);
            Assert.AreEqual(5, f.Queue.Waiting[0].PatienceMinutesRemaining);
        }

        [Test]
        public void Sweep_RemovesSettledEntrySilently_WhileStaleEntryBalks()
        {
            var f = new Fixture();
            float arrival;
            PlannedShoppingTrip trip;
            var driver = f.DriveAwaitingServiceTrip(out arrival, out trip);

            // The trip is served (state forced to InStore the way the
            // service-completion branch leaves it); its queue entry is now
            // stale and must be swept silently, not balked.
            trip.State = PlannedShoppingTrip.TripState.InStore;
            Assert.AreEqual(1, f.Queue.WaitingCount);
            // A second entry with no trip at all: it must balk (and record
            // its lost sale) while the settled entry is swept.
            Assert.IsTrue(f.Queue.Enqueue("shop:7:99", 1, "staple_food", 5, 120, Fixture.Day));
            Assert.AreEqual(2, f.Queue.WaitingCount);

            driver.AdvanceTick(arrival + 3600f, Fixture.Day, f.DueEvents, f.Diag);
            Assert.AreEqual(0, f.Queue.WaitingCount, "swept entry and balked entry both leave the line");
            Assert.AreEqual(1, f.Queue.LostSales.Count, "only the no-trip entry records a lost sale");
            bool staleBalkDiagnosed = false;
            bool settledBalked = false;
            foreach (string line in f.Diag)
            {
                if (line.Contains("shop:7:99") && line.Contains("no active trip")) staleBalkDiagnosed = true;
                if (line.Contains("shop:7:1") && line.Contains("balked")) settledBalked = true;
            }
            Assert.IsTrue(staleBalkDiagnosed, "stale no-trip balk must be diagnosed");
            Assert.IsFalse(settledBalked, "settled entry must be swept silently, never balked");
            Assert.AreEqual(PlannedShoppingTrip.TripState.InStore, trip.State, "sweep must not touch the settled trip");
        }

        private const string BootstrapperPath = "Assets/Scripts/Orchestration/Bootstrap/FirstLedgerSliceBootstrapper.cs";
        private const string SimulationDriversPath = "Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs";
        private const string ShoppingTripDriverPath = "Assets/Scripts/Domains/Population/ShoppingTripDriver.cs";
        private const string GeneralStoreRuntimeManagerPath = "Assets/Scripts/Domains/Economy/Businesses/GeneralStore/GeneralStoreRuntimeManager.cs";

        private static string FindRepoRoot()
        {
            string fromEnv = Environment.GetEnvironmentVariable("LAND_LEDGERS_REPO");
            if (!string.IsNullOrWhiteSpace(fromEnv)
                && File.Exists(Path.Combine(fromEnv, BootstrapperPath)))
            {
                return fromEnv;
            }

            var starts = new[]
            {
                System.IO.Directory.GetCurrentDirectory(),
                AppContext.BaseDirectory,
            };
            var tried = new StringBuilder();
            foreach (string start in starts)
            {
                string dir = start;
                while (!string.IsNullOrEmpty(dir))
                {
                    foreach (string candidate in new[]
                    {
                        dir,
                        Path.Combine(dir, "work-c"),
                    })
                    {
                        tried.AppendLine("  " + candidate);
                        if (File.Exists(Path.Combine(candidate, BootstrapperPath)))
                        {
                            return candidate;
                        }
                    }
                    DirectoryInfo parent = System.IO.Directory.GetParent(dir);
                    dir = parent == null ? null : parent.FullName;
                }
            }

            Assert.IsTrue(false, "Campaign C (package 12): could not locate the Land & Ledgers repo. "
                + "Set LAND_LEDGERS_REPO, or run from the campaign tree. Searched:\n"
                + tried + "  LAND_LEDGERS_REPO=" + (fromEnv ?? "<unset>"));
            return null;
        }

        private static string ReadRepoFile(string relativePath)
        {
            string root = FindRepoRoot();
            return File.ReadAllText(Path.Combine(root, relativePath));
        }

        [Test]
        public void Binding_BootstrapperLoadPath_BindsCustomerQueue()
        {
            string text = ReadRepoFile(BootstrapperPath);
            Assert.IsTrue(text.Contains("pass9.CustomerQueue = storeManager.CustomerQueue"),
                "EnsurePass9SaveOwnerForRestore must bind the live store queue into the Pass 9 save state before the restore consumes it");
            Assert.IsTrue(text.Contains("FindAnyObjectByType<GeneralStoreRuntimeManager>()"),
                "the load path must resolve the live store manager");
        }

        [Test]
        public void Binding_LoopBuild_BindsCustomerQueue()
        {
            string text = ReadRepoFile(SimulationDriversPath);
            Assert.IsTrue(text.Contains("personHandSave.Series2Pass9State.CustomerQueue = queueStoreManager.CustomerQueue"),
                "BuildHouseholdShoppingLoop must bind the live store queue into the Pass 9 save state so New Game saves capture the waiting line");
            Assert.IsTrue(text.Contains("personHandSave.Series2Pass9State.BakeryCustomerQueue = bakeryCustomerQueue"),
                "BuildHouseholdShoppingLoop must bind the live bakery counter queue into the Pass 9 save state");
        }

        [Test]
        public void BakeryCounterQueue_SaveRestoreBeforeRuntimeBinding_PreservesWaitingAndPatience()
        {
            var source = new Series2Pass9SaveState();
            var liveQueue = new CustomerQueue
            {
                MerchantBusinessId = "bakery-1",
                MerchantName = "Glenn's Bakery",
            };
            Assert.IsTrue(liveQueue.Enqueue("shop:7:15", 1, "bakery_goods", 2, 35, 4, patienceMinutes: 20));
            liveQueue.Tick(7, 4);
            source.BakeryCustomerQueue = liveQueue;

            var diagnostics = new List<string>();
            var dto = source.Capture(diagnostics);
            var restored = new Series2Pass9SaveState();
            restored.Restore(dto, null, diagnostics);
            Assert.IsTrue(restored.HasPendingBakeryCustomerQueue,
                "Continue may restore before the bakery runtime has recreated its physical service queue");

            var reboundQueue = new CustomerQueue();
            restored.BakeryCustomerQueue = reboundQueue;

            Assert.IsFalse(restored.HasPendingBakeryCustomerQueue);
            Assert.AreEqual("bakery-1", reboundQueue.MerchantBusinessId);
            Assert.AreEqual("Glenn's Bakery", reboundQueue.MerchantName);
            Assert.AreEqual(1, reboundQueue.WaitingCount);
            Assert.AreEqual("shop:7:15", reboundQueue.Waiting[0].CustomerRef);
            Assert.AreEqual(13, reboundQueue.Waiting[0].PatienceMinutesRemaining);
        }

        [Test]
        public void FuelDealerCounterQueues_SaveRestoreBeforeBinding_PreservesWaitersAndSeparatesBusinesses()
        {
            var source = new Series2Pass9SaveState();
            CustomerQueue first = source.GetOrCreateFuelDealerCustomerQueue("fuel-yard-1", "Town Fuel Yard");
            CustomerQueue second = source.GetOrCreateFuelDealerCustomerQueue("fuel-yard-2", "North Fuel Yard");
            Assert.IsTrue(first.Enqueue("shop:7:15", 1, "fuel_wood", 2, 30, 4, patienceMinutes: 20));
            Assert.IsTrue(second.Enqueue("shop:8:16", 2, "fuel_wood", 1, 30, 4, patienceMinutes: 20));
            first.Tick(7, 4);
            second.Tick(3, 4);

            var diagnostics = new List<string>();
            Series2Pass9SaveDto dto = source.Capture(diagnostics);
            Assert.AreEqual(2, dto.FuelDealerCustomerQueues.Count);

            var restored = new Series2Pass9SaveState();
            restored.Restore(dto, null, diagnostics);
            Assert.AreEqual(2, restored.PendingFuelDealerCustomerQueueCount);

            CustomerQueue reboundFirst = restored.GetOrCreateFuelDealerCustomerQueue("fuel-yard-1", "Town Fuel Yard");
            Assert.AreEqual(1, reboundFirst.WaitingCount);
            Assert.AreEqual(13, reboundFirst.Waiting[0].PatienceMinutesRemaining);
            Assert.AreEqual("fuel-yard-1", reboundFirst.MerchantBusinessId);
            Assert.AreEqual(1, restored.PendingFuelDealerCustomerQueueCount);

            CustomerQueue reboundFirstAgain = restored.GetOrCreateFuelDealerCustomerQueue("fuel-yard-1", "Town Fuel Yard");
            Assert.AreSame(reboundFirst, reboundFirstAgain);
            Assert.AreEqual(1, reboundFirstAgain.WaitingCount, "A repeated bind must not duplicate a restored customer.");

            // Re-saving before the second dealer runtime binds must keep its
            // waiting customer pending rather than dropping it.
            Series2Pass9SaveDto intermediate = restored.Capture(diagnostics);
            Assert.AreEqual(2, intermediate.FuelDealerCustomerQueues.Count);
            CustomerQueue reboundSecond = restored.GetOrCreateFuelDealerCustomerQueue("fuel-yard-2", "North Fuel Yard");
            Assert.AreEqual(1, reboundSecond.WaitingCount);
            Assert.AreEqual(17, reboundSecond.Waiting[0].PatienceMinutesRemaining);
            Assert.AreNotSame(reboundFirst, reboundSecond);
            Assert.AreEqual(0, restored.PendingFuelDealerCustomerQueueCount);
        }

        [Test]
        public void FuelDealerCounterQueueRestore_UsesOwningEntryIdentity_WhenNestedDtoDisagrees()
        {
            var dto = new Series2Pass9SaveDto();
            var queue = new CustomerQueue
            {
                MerchantBusinessId = "wrong-dealer",
                MerchantName = "Wrong Dealer",
            };
            Assert.IsTrue(queue.Enqueue("shop:7:15", 1, "fuel_wood", 1, 30, 4, patienceMinutes: 18));
            dto.FuelDealerCustomerQueues.Add(new Pass9FuelDealerCustomerQueueEntryDto
            {
                BusinessInstanceId = "fuel-yard-1",
                Queue = queue.CaptureSaveDto(),
            });

            var restored = new Series2Pass9SaveState();
            var diagnostics = new List<string>();
            restored.Restore(dto, null, diagnostics);
            CustomerQueue rebound = restored.GetOrCreateFuelDealerCustomerQueue("fuel-yard-1", "Town Fuel Yard");

            Assert.AreEqual("fuel-yard-1", rebound.MerchantBusinessId,
                "the durable enclosing business identity must own the queue after restoration");
            Assert.AreEqual("shop:7:15", rebound.Waiting.Single().CustomerRef,
                "identity repair must preserve the legitimate waiting customer");
            Assert.IsTrue(diagnostics.Any(line => line.Contains("disagrees with owning entry 'fuel-yard-1'")),
                "malformed cross-business queue state must remain diagnostically visible");
        }

        [Test]
        public void FuelDealerCounterService_BindsRetailQueueAndPhysicalWorkers()
        {
            string text = ReadRepoFile(SimulationDriversPath);
            StringAssert.Contains("FindFuelDealerSupplierAdapter(businessId)", text);
            StringAssert.Contains("GetOrCreateFuelDealerCustomerQueue(", text);
            StringAssert.Contains("FuelDealerServiceCandidatePersonIds(fuelDealerBusiness)", text);
            StringAssert.Contains("IsPersonPhysicallyAtBuilding(candidatePersonId, fuelDealerBusiness.AssignedBuildingId)", text);
            StringAssert.Contains("FuelDealerServiceCandidatePersonIds(fuelDealerBusiness).Contains(personId)", text);
        }

        [Test]
        public void Binding_Rebind_RearmsQueueWatermark()
        {
            string driversText = ReadRepoFile(SimulationDriversPath);
            Assert.IsTrue(driversText.Contains("RearmQueueTickWatermark(timeManager.AbsoluteGameSeconds)"),
                "Rebind must re-arm the queue patience tick at the restored clock after a load");
            string tripDriverText = ReadRepoFile(ShoppingTripDriverPath);
            Assert.IsTrue(tripDriverText.Contains("public void RearmQueueTickWatermark(float restoredGameSeconds)"),
                "ShoppingTripDriver must expose the watermark re-arm");
        }

        [Test]
        public void ManagerComment_NoLongerClaimsSaveSlotMissing()
        {
            string text = ReadRepoFile(GeneralStoreRuntimeManagerPath);
            Assert.IsFalse(text.Contains("is not on this branch yet"),
                "the stale comment claiming the Pass 9 slot is not on this branch must be gone");
            Assert.IsTrue(text.Contains("DEF-073"),
                "the corrected comment must record the DEF-073 binding");
        }
    }
}
