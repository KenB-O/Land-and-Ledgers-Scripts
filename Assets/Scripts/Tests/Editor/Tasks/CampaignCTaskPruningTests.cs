// Campaign C (DEF-049) — TaskAuthority never prunes tasks, so saves grow
// unboundedly (Check 10 F-15: CaptureSaveDto persisted every task ever
// created). This package adds a bounded daily prune: terminal tasks
// (Complete / Failed / Cancelled) past a 7-day retention floor, never a
// task an open task lists as a prerequisite, and never a task a live
// shopping trip still references (rehydrate requires every leg/service id
// to resolve, and leg tasks complete mid-trip).
//
// Three test kinds, same file:
//  (1) Pure TaskAuthority prune rules (no loop, no driver).
//  (2) Live-trip guard end to end (real loop + driver fixture, same shape
//      as the package-1 paid-return fixture).
//  (3) Structural tests pinning the SimulationDrivers wiring by reading the
//      production file — SimulationDrivers is too large a closure for the
//      focused harness, so the wiring itself (not Unity behaviour) is what
//      is asserted here, exactly like the package-2 guard and package-3
//      rebind tests.
// The repo root comes from LAND_LEDGERS_REPO, then a walk up from the
// current directory and the test assembly location (campaign layout:
// <root>/work-c/Assets/...), mirroring UnwiredSystemsRegistryTests. A
// missing repo fails loudly, never silently passes.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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

namespace LandLedgers.EditorTests.Tasks
{
    [TestFixture]
    public sealed class CampaignCTaskPruningTests
    {
        private const int Day = 100;

        private static EntityId Business(int id) => EntityId.For(EntityKind.Business, id);
        private static EntityId Person(int id) => EntityId.For(EntityKind.Person, id);

        private static TaskAuthority CreateAuthority()
        {
            var authority = new TaskAuthority();
            string reason;
            Assert.IsTrue(authority.RegisterDefinition(new TaskDefinition("test.prune", "Prune", 10), out reason), reason);
            return authority;
        }

        private static WorkTask CreateTask(TaskAuthority authority, int day = Day)
        {
            return authority.CreateTask("test.prune", Business(1), day);
        }

        private static void CompleteAt(TaskAuthority authority, WorkTask task, int day)
        {
            var budgets = new WorkTimeBudgetStore();
            Assert.IsTrue(authority.CompleteTask(task.TaskId, TaskOutcome.Success, day, budgets),
                "CompleteTask refused for " + task.TaskId);
        }

        private static void FailAt(TaskAuthority authority, WorkTask task, int day)
        {
            var budgets = new WorkTimeBudgetStore();
            Assert.IsTrue(authority.CompleteTask(task.TaskId, TaskOutcome.RecoverableFailure, day, budgets),
                "CompleteTask (failure) refused for " + task.TaskId);
        }

        // ---------- (1) Pure prune rules ----------

        [Test]
        public void RetentionConstant_IsSevenDays()
        {
            Assert.AreEqual(7, TaskAuthority.TerminalTaskRetentionDays);
        }

        [Test]
        public void CompleteTask_KeptBeforeRetentionFloor()
        {
            TaskAuthority authority = CreateAuthority();
            WorkTask task = CreateTask(authority);
            CompleteAt(authority, task, Day);
            Assert.AreEqual(0, authority.PruneTerminalTasks(Day + 6));
            Assert.IsNotNull(authority.FindTask(task.TaskId));
        }

        [Test]
        public void CompleteTask_PrunedAtRetentionFloor()
        {
            TaskAuthority authority = CreateAuthority();
            WorkTask task = CreateTask(authority);
            CompleteAt(authority, task, Day);
            Assert.AreEqual(1, authority.PruneTerminalTasks(Day + 7));
            Assert.IsNull(authority.FindTask(task.TaskId));
            Assert.IsNull(authority.FindTask(task.TaskId.ToString()));
        }

        [Test]
        public void FailedTask_PrunedAfterRetention()
        {
            TaskAuthority authority = CreateAuthority();
            WorkTask task = CreateTask(authority);
            FailAt(authority, task, Day);
            Assert.AreEqual(0, authority.PruneTerminalTasks(Day + 6));
            Assert.AreEqual(1, authority.PruneTerminalTasks(Day + 7));
            Assert.IsNull(authority.FindTask(task.TaskId));
        }

        [Test]
        public void CancelledTask_PrunedAfterRetentionAnchoredOnCreationDay()
        {
            // Cancelled tasks record no completion day (MarkCancelled takes no
            // day), so the creation day is the anchor: a task created on Day
            // and cancelled any time after is prunable on Day + 7.
            TaskAuthority authority = CreateAuthority();
            WorkTask task = CreateTask(authority);
            var budgets = new WorkTimeBudgetStore();
            Assert.IsTrue(authority.CancelTask(task.TaskId, budgets));
            Assert.AreEqual(0, authority.PruneTerminalTasks(Day + 6));
            Assert.AreEqual(1, authority.PruneTerminalTasks(Day + 7));
            Assert.IsNull(authority.FindTask(task.TaskId));
        }

        [Test]
        public void OpenTasks_NeverPruned()
        {
            TaskAuthority authority = CreateAuthority();
            var budgets = new WorkTimeBudgetStore();
            budgets.EnsureDay(Day);
            WorkTask queued = CreateTask(authority);
            WorkTask assigned = CreateTask(authority);
            WorkTask inProgress = CreateTask(authority);
            string reason;
            Assert.IsTrue(authority.AssignTask(assigned.TaskId, Person(1), budgets, out reason), reason);
            Assert.IsTrue(authority.AssignTask(inProgress.TaskId, Person(2), budgets, out reason), reason);
            Assert.IsTrue(authority.StartTask(inProgress.TaskId, Day, out reason), reason);
            Assert.AreEqual(0, authority.PruneTerminalTasks(Day + 30));
            Assert.IsNotNull(authority.FindTask(queued.TaskId));
            Assert.IsNotNull(authority.FindTask(assigned.TaskId));
            Assert.IsNotNull(authority.FindTask(inProgress.TaskId));
            Assert.AreEqual(3, authority.GetAllOpenTasks().Count);
        }

        [Test]
        public void ReworkRequired_NotPruned()
        {
            // ReworkRequired is terminal for the instance but keeps its
            // corrective-task link; it is out of this pass's scope.
            TaskAuthority authority = CreateAuthority();
            WorkTask task = CreateTask(authority);
            FailAt(authority, task, Day);
            string reason;
            Assert.IsTrue(authority.MarkReworkRequired(task.TaskId, default(EntityId), "test", out reason), reason);
            Assert.AreEqual(TaskStatus.ReworkRequired, task.Status);
            Assert.AreEqual(0, authority.PruneTerminalTasks(Day + 30));
            Assert.IsNotNull(authority.FindTask(task.TaskId));
        }

        [Test]
        public void PrerequisiteTarget_SurvivesWhileDependentOpen()
        {
            TaskAuthority authority = CreateAuthority();
            WorkTask first = CreateTask(authority);
            WorkTask second = CreateTask(authority);
            second.PrerequisiteTaskIds.Add(first.TaskId);
            CompleteAt(authority, first, Day);
            // Second is still open: the prereq must resolve Complete.
            Assert.AreEqual(0, authority.PruneTerminalTasks(Day + 8));
            Assert.IsNotNull(authority.FindTask(first.TaskId));
            // Once the dependent is terminal too, the prereq is prunable.
            CompleteAt(authority, second, Day + 8);
            Assert.AreEqual(1, authority.PruneTerminalTasks(Day + 8));
            Assert.IsNull(authority.FindTask(first.TaskId));
            Assert.IsNotNull(authority.FindTask(second.TaskId));
        }

        [Test]
        public void ExternalPredicate_FlaggedTaskSurvives()
        {
            TaskAuthority authority = CreateAuthority();
            WorkTask flagged = CreateTask(authority);
            WorkTask plain = CreateTask(authority);
            CompleteAt(authority, flagged, Day);
            CompleteAt(authority, plain, Day);
            authority.IsTaskExternallyReferenced = id => id == flagged.TaskId;
            Assert.AreEqual(1, authority.PruneTerminalTasks(Day + 8));
            Assert.IsNotNull(authority.FindTask(flagged.TaskId));
            Assert.IsNull(authority.FindTask(plain.TaskId));
        }

        [Test]
        public void ExternalPredicate_ThrowingPredicate_KeepsTaskAndContinues()
        {
            // A broken predicate must never delete a task it cannot vouch for,
            // and must not abort the sweep for the other tasks.
            TaskAuthority authority = CreateAuthority();
            WorkTask task = CreateTask(authority);
            CompleteAt(authority, task, Day);
            authority.IsTaskExternallyReferenced = id => throw new InvalidOperationException("boom");
            Assert.AreEqual(0, authority.PruneTerminalTasks(Day + 8));
            Assert.IsNotNull(authority.FindTask(task.TaskId));
        }

        [Test]
        public void NegativeDay_PrunesNothing()
        {
            TaskAuthority authority = CreateAuthority();
            WorkTask task = CreateTask(authority);
            CompleteAt(authority, task, Day);
            Assert.AreEqual(0, authority.PruneTerminalTasks(-1));
            Assert.IsNotNull(authority.FindTask(task.TaskId));
        }

        [Test]
        public void Prune_RemovesFromLookupsAndOwnerQueue()
        {
            TaskAuthority authority = CreateAuthority();
            WorkTask task = CreateTask(authority);
            CompleteAt(authority, task, Day);
            Assert.AreEqual(1, authority.PruneTerminalTasks(Day + 7));
            Assert.IsNull(authority.FindTask(task.TaskId));
            Assert.IsNull(authority.FindTask(task.TaskId.ToString()));
            Assert.AreEqual(0, authority.GetOpenTasks(Business(1)).Count);
            Assert.AreEqual(0, authority.GetAllOpenTasks().Count);
        }

        [Test]
        public void CaptureSaveDto_ShrinksAfterPrune()
        {
            // The defect itself, pinned: the save carried every task ever
            // created; after the prune it carries only live state.
            TaskAuthority authority = CreateAuthority();
            WorkTask a = CreateTask(authority);
            WorkTask b = CreateTask(authority);
            WorkTask live = CreateTask(authority);
            CompleteAt(authority, a, Day);
            CompleteAt(authority, b, Day);
            Assert.AreEqual(3, authority.CaptureSaveDto().tasks.Count);
            Assert.AreEqual(2, authority.PruneTerminalTasks(Day + 7));
            var dto = authority.CaptureSaveDto();
            Assert.AreEqual(1, dto.tasks.Count);
            Assert.AreEqual(live.TaskId, dto.tasks[0].TaskId);
        }

        // ---------- (2) Live-trip guard end to end ----------

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

        private sealed class TripFixture
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
            public readonly ShoppingLoopOptions LoopOptions;
            public readonly Dictionary<int, string> Locations = new Dictionary<int, string>();
            public readonly List<string> Diag = new List<string>();

            public const int HouseholdId = 7;
            public const int ShopperId = 1;

            public TripFixture()
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
                    spendingPolicyProvider: null);
                Driver = new ShoppingTripDriver(Loop);

                Ledgers.GetOrCreate(HouseholdId).RecordInflow(0, 50000,
                    HouseholdIncomeSource.WageEmployment, "emp-1", "seed wages", "test");
            }

            public HouseholdPurchasingNeed OpenNeed(string itemId, int units)
            {
                HouseholdPurchasingNeed need = Needs.CreateNeed(HouseholdId, itemId, Day);
                need.UnitsNeeded = units;
                need.IsNecessity = true;
                return need;
            }

            public void InstallLiveTripPredicate()
            {
                Tasks.IsTaskExternallyReferenced = id => Driver.IsTaskReferencedByLiveTrip(id);
            }

            private void DrainDue(float now)
            {
                foreach (DueEvent ev in DueEvents.PopDue(now))
                {
                    Driver.HandleDueEvent(ev, Day, ev.DueGameSeconds, DueEvents, Diag);
                }
            }

            /// <summary>Advances 60-second ticks until stop() is true, or the cap hits (loud failure).</summary>
            public void AdvanceUntil(Func<bool> stop, int maxTicks, string what)
            {
                float start = Day * 86400f;
                for (int t = 0; t < maxTicks; t++)
                {
                    float now = start + t * 60f;
                    Driver.AdvanceTick(now, Day, DueEvents, Diag);
                    DrainDue(now);
                    if (stop())
                    {
                        return;
                    }
                }

                Assert.IsTrue(false, "Campaign C (DEF-049): trip did not reach '" + what
                    + "' within " + maxTicks + " ticks. ActiveTrips=" + Driver.ActiveTrips.Count
                    + " diag=" + string.Join(" | ", Diag));
            }
        }

        [Test]
        public void LiveTrip_LegTaskIds_AreFlaggedByPredicate()
        {
            var f = new TripFixture();
            f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            f.Driver.PlanForHousehold(TripFixture.HouseholdId, f.Needs, Day, f.LoopOptions, f.Diag);
            Assert.AreEqual(1, f.Driver.ActiveTrips.Count);
            PlannedShoppingTrip trip = f.Driver.ActiveTrips[0];
            Assert.IsNotNull(trip.Legs);
            Assert.IsNotNull(trip.Legs.Outbound);
            Assert.IsNotNull(trip.Legs.InStore);
            Assert.IsNotNull(trip.Legs.Return);
            Assert.IsTrue(f.Driver.IsTaskReferencedByLiveTrip(trip.Legs.Outbound.TaskId));
            Assert.IsTrue(f.Driver.IsTaskReferencedByLiveTrip(trip.Legs.InStore.TaskId));
            Assert.IsTrue(f.Driver.IsTaskReferencedByLiveTrip(trip.Legs.Return.TaskId));
            Assert.IsFalse(f.Driver.IsTaskReferencedByLiveTrip(default(EntityId)));
            Assert.IsFalse(f.Driver.IsTaskReferencedByLiveTrip(EntityId.For(EntityKind.WorkTask, 999999)));
        }

        [Test]
        public void CompletedLegTask_SurvivesPruneWhileTripLive()
        {
            var f = new TripFixture();
            f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            f.Driver.PlanForHousehold(TripFixture.HouseholdId, f.Needs, Day, f.LoopOptions, f.Diag);
            Assert.AreEqual(1, f.Driver.ActiveTrips.Count);
            PlannedShoppingTrip trip = f.Driver.ActiveTrips[0];
            WorkTask outbound = trip.Legs.Outbound;
            f.InstallLiveTripPredicate();

            // Drive until the outbound leg completes while the trip is live:
            // the leg task is Complete, the trip is still AtStore / InStore /
            // Returning, and a naive age-only prune would delete it — which
            // would abort the trip on the next load (rehydrate requires the
            // id to resolve).
            f.AdvanceUntil(() => outbound.Status == TaskStatus.Complete && f.Driver.ActiveTrips.Count > 0, 2000, "outbound leg complete with trip live");
            Assert.AreEqual(TaskStatus.Complete, outbound.Status);
            Assert.AreEqual(1, f.Driver.ActiveTrips.Count);

            Assert.AreEqual(0, f.Tasks.PruneTerminalTasks(Day + 8));
            Assert.IsNotNull(f.Tasks.FindTask(outbound.TaskId));

            // Control: without the predicate the same task is prunable.
            f.Tasks.IsTaskExternallyReferenced = null;
            Assert.AreEqual(1, f.Tasks.PruneTerminalTasks(Day + 8));
            Assert.IsNull(f.Tasks.FindTask(outbound.TaskId));
        }

        [Test]
        public void CompletedLegTasks_PrunedAfterTripEnds()
        {
            var f = new TripFixture();
            f.OpenNeed(HouseholdItemCatalog.FlourId, 10);
            f.Driver.PlanForHousehold(TripFixture.HouseholdId, f.Needs, Day, f.LoopOptions, f.Diag);
            Assert.AreEqual(1, f.Driver.ActiveTrips.Count);
            PlannedShoppingTrip trip = f.Driver.ActiveTrips[0];
            WorkTask outbound = trip.Legs.Outbound;
            WorkTask inStore = trip.Legs.InStore;
            WorkTask returning = trip.Legs.Return;
            f.InstallLiveTripPredicate();

            f.AdvanceUntil(() => f.Driver.ActiveTrips.Count == 0, 3000, "trip done");
            Assert.AreEqual(PlannedShoppingTrip.TripState.Done, trip.State);
            Assert.IsFalse(f.Driver.IsTaskReferencedByLiveTrip(outbound.TaskId));
            Assert.IsFalse(f.Driver.IsTaskReferencedByLiveTrip(inStore.TaskId));
            Assert.IsFalse(f.Driver.IsTaskReferencedByLiveTrip(returning.TaskId));

            f.Tasks.PruneTerminalTasks(Day + 8);
            Assert.IsNull(f.Tasks.FindTask(outbound.TaskId));
            Assert.IsNull(f.Tasks.FindTask(inStore.TaskId));
            Assert.IsNull(f.Tasks.FindTask(returning.TaskId));
        }

        // ---------- (3) Driver wiring ----------

        private const string DriversPath = "Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs";
        private const string AuthorityPath = "Assets/Scripts/Core/Tasks/TaskAuthority.cs";
        private const string TripDriverPath = "Assets/Scripts/Domains/Population/ShoppingTripDriver.cs";

        [Test]
        public void DriveTaskPruning_WiredAtEndOfOnDayChanged()
        {
            string body = MethodBody(ReadRepoFile(DriversPath), "private void OnDayChanged(SimulationDateChangedContext context)");
            string code = StripNonCode(body);
            Assert.IsTrue(code.Contains("DriveTaskPruning(absoluteDayIndex);"),
                "Campaign C (DEF-049): OnDayChanged must call DriveTaskPruning(absoluteDayIndex).");
            string drivers = ReadRepoFile(DriversPath);
            Assert.IsTrue(drivers.Contains("private void DriveTaskPruning(int absoluteDayIndex)"),
                "Campaign C (DEF-049): expected the DriveTaskPruning driver method.");
            Assert.IsTrue(drivers.Contains("hub.Tasks.PruneTerminalTasks(absoluteDayIndex)"),
                "Campaign C (DEF-049): the driver must call TaskAuthority.PruneTerminalTasks.");
        }

        [Test]
        public void PredicateInstalled_WhenShoppingDriverBuilt()
        {
            string drivers = ReadRepoFile(DriversPath);
            Assert.IsTrue(drivers.Contains("IsTaskExternallyReferenced = taskId"),
                "Campaign C (DEF-049): the shopping-driver build must install the external-reference predicate.");
            Assert.IsTrue(drivers.Contains("IsTaskReferencedByLiveTrip(taskId)"),
                "Campaign C (DEF-049): the predicate must answer from the driver's live-trip lists.");
            string tripDriver = ReadRepoFile(TripDriverPath);
            Assert.IsTrue(tripDriver.Contains("public bool IsTaskReferencedByLiveTrip(EntityId taskId)"),
                "Campaign C (DEF-049): expected ShoppingTripDriver.IsTaskReferencedByLiveTrip.");
        }

        [Test]
        public void ChangesAreMarkedCampaignC()
        {
            Assert.IsTrue(ReadRepoFile(AuthorityPath).Contains("Campaign C (DEF-049)"),
                "Campaign C (DEF-049): TaskAuthority changes must be marked so the watchdog can attribute them.");
            Assert.IsTrue(ReadRepoFile(TripDriverPath).Contains("Campaign C (DEF-049)"),
                "Campaign C (DEF-049): ShoppingTripDriver changes must be marked so the watchdog can attribute them.");
            Assert.IsTrue(ReadRepoFile(DriversPath).Contains("Campaign C (DEF-049)"),
                "Campaign C (DEF-049): SimulationDrivers changes must be marked so the watchdog can attribute them.");
        }

        // ---------- repo-file helpers (package-3 precedent) ----------

        private static string FindRepoRoot()
        {
            string fromEnv = Environment.GetEnvironmentVariable("LAND_LEDGERS_REPO");
            if (!string.IsNullOrWhiteSpace(fromEnv)
                && File.Exists(Path.Combine(fromEnv, AuthorityPath)))
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
                        if (File.Exists(Path.Combine(candidate, AuthorityPath)))
                        {
                            return candidate;
                        }
                    }
                    DirectoryInfo parent = System.IO.Directory.GetParent(dir);
                    dir = parent == null ? null : parent.FullName;
                }
            }

            Assert.IsTrue(false, "Campaign C (DEF-049): could not locate the Land & Ledgers repo. "
                + "Set LAND_LEDGERS_REPO, or run from the campaign tree. Searched:\n"
                + tried + "  LAND_LEDGERS_REPO=" + (fromEnv ?? "<unset>"));
            return null;
        }

        private static string ReadRepoFile(string relativePath)
        {
            string root = FindRepoRoot();
            string full = Path.Combine(root, relativePath);
            Assert.IsTrue(File.Exists(full), "Campaign C (DEF-049): expected repo file missing: " + full);
            return File.ReadAllText(full);
        }

        private static string MethodBody(string source, string signature)
        {
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.IsTrue(start >= 0, "Campaign C (DEF-049): method not found: " + signature);
            int next = source.Length;
            foreach (string marker in new[] { "\n        private ", "\n        public ", "\n        protected ", "\n        internal " })
            {
                int at = source.IndexOf(marker, start + signature.Length, StringComparison.Ordinal);
                if (at >= 0 && at < next) next = at;
            }
            return source.Substring(start, next - start);
        }

        /// <summary>
        /// Returns source with comments, string literals and char literals
        /// blanked (newlines preserved), so assertions cannot be fooled by a
        /// mention inside a comment or a log string.
        /// </summary>
        internal static string StripNonCode(string source)
        {
            var out_ = new StringBuilder(source.Length);
            int i = 0;
            while (i < source.Length)
            {
                char c = source[i];
                char next = i + 1 < source.Length ? source[i + 1] : '\0';
                if (c == '/' && next == '/')
                {
                    while (i < source.Length && source[i] != '\n')
                    {
                        out_.Append(' ');
                        i++;
                    }
                }
                else if (c == '/' && next == '*')
                {
                    out_.Append("  ");
                    i += 2;
                    while (i < source.Length && !(source[i] == '*' && i + 1 < source.Length && source[i + 1] == '/'))
                    {
                        out_.Append(source[i] == '\n' ? '\n' : ' ');
                        i++;
                    }
                    if (i < source.Length)
                    {
                        out_.Append("  ");
                        i += 2;
                    }
                }
                else if (c == '@' && next == '"')
                {
                    out_.Append("  ");
                    i += 2;
                    while (i < source.Length)
                    {
                        if (source[i] == '"' && i + 1 < source.Length && source[i + 1] == '"')
                        {
                            out_.Append("  ");
                            i += 2;
                        }
                        else if (source[i] == '"')
                        {
                            out_.Append(' ');
                            i++;
                            break;
                        }
                        else
                        {
                            out_.Append(source[i] == '\n' ? '\n' : ' ');
                            i++;
                        }
                    }
                }
                else if (c == '"')
                {
                    out_.Append(' ');
                    i++;
                    while (i < source.Length && source[i] != '\n')
                    {
                        if (source[i] == '\\' && i + 1 < source.Length)
                        {
                            out_.Append("  ");
                            i += 2;
                        }
                        else if (source[i] == '"')
                        {
                            out_.Append(' ');
                            i++;
                            break;
                        }
                        else
                        {
                            out_.Append(' ');
                            i++;
                        }
                    }
                }
                else if (c == '\'')
                {
                    out_.Append(' ');
                    i++;
                    while (i < source.Length && source[i] != '\n')
                    {
                        if (source[i] == '\\' && i + 1 < source.Length)
                        {
                            out_.Append("  ");
                            i += 2;
                        }
                        else if (source[i] == '\'')
                        {
                            out_.Append(' ');
                            i++;
                            break;
                        }
                        else
                        {
                            out_.Append(' ');
                            i++;
                        }
                    }
                }
                else
                {
                    out_.Append(c);
                    i++;
                }
            }
            return out_.ToString();
        }
    }
}
