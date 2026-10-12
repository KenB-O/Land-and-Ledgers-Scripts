using System;
using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Labor;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using LandLedgers.Persistence;

namespace LandLedgers.Editor.Integration
{
    /// <summary>
    /// Pass IV (Worker 4): the 14-day living-world simulation scaffold
    /// (SLICE F + extended sim). A deterministic, fully event-driven world:
    /// several households, real employment, production processes (field →
    /// grain → mill/thresher → flour → bakery → bread → store → household →
    /// meal), shared equipment contention (one thresher, two farms), a
    /// delegated manager (bakery + farm-1), real inventory and purchases, a
    /// deliberate mill breakdown + flour shortage, and a deliberate time
    /// conflict.
    ///
    /// Every consequential state change happens through due events, so 1x
    /// small steps, large steps, 10x, and 100x produce identical outcomes with
    /// the same seed. Economic integrity: household cash moves only through
    /// HouseholdLedger; business cash through the sim's single cash book;
    /// goods move only by real production/consumption/purchase; person-time
    /// only through the schedule tracker + budget store; no fake persons.
    /// </summary>
    public sealed partial class LivingWorldSim
    {
        // ---------- world constants ----------
        public const int StartDay = 200;
        public const int SimDays = 14;

        public const int H1 = 7, H2 = 8, H3 = 9, H4 = 10, H5 = 11, H6 = 12;
        public const int P1 = 1, P2 = 2, P3 = 3;   // H1: Tomas (baker), Petra, Milo (child)
        public const int P4 = 4, P5 = 5;           // H2: Sable (farmhand), Wren (clerk)
        public const int P6 = 6;                  // H3: Ash (retired)
        public const int P7 = 7;                  // H4: Dana (delegated manager)
        public const int P8 = 8;                  // H5: Otto (owner)
        public const int P9 = 9;                  // H6: Robin (farmhand, half-day)

        public const string Bakery = "bakery-1";
        public const string Farm1 = "farm-1";
        public const string Farm2 = "farm-2";
        public const string Store = "store-1";

        public const string ThresherKey = "thresher-1";
        public const string MillKey = "mill-1";
        public const string OvenKey = "oven-1";

        public const string Flour = "flour";
        public const string Bread = "bread";
        public const string Grain = "grain";
        public const string PreservedFood = "preserved_food";
        public const string MillBelt = "mill_belt";

        public sealed class Employment
        {
            public string Id = string.Empty;
            public int PersonId = -1;
            public string BusinessId = string.Empty;
            public int HouseholdId = -1;
            public int WeeklyWageCents;
        }

        // ---------- authorities (real) ----------
        public readonly SimpleDueEventScheduler DueEvents = new SimpleDueEventScheduler();
        public readonly TaskAuthority Tasks = new TaskAuthority();
        public readonly WorkTimeBudgetStore Budgets = new WorkTimeBudgetStore();
        public readonly PersonScheduleTracker Schedules = new PersonScheduleTracker();
        public readonly PopulationState Population = new PopulationState();
        public readonly HouseholdMembershipRegistry Membership = new HouseholdMembershipRegistry();
        public readonly HouseholdLedgerRegistry Ledgers = new HouseholdLedgerRegistry();
        public readonly HouseholdInventoryRegistry Inventories = new HouseholdInventoryRegistry();
        public readonly HouseholdNeedRegistry Needs = new HouseholdNeedRegistry();
        public readonly SupplierDirectory Directory = new SupplierDirectory();
        public readonly JourneyModel Journeys = new JourneyModel();
        public readonly ShopperSellerKnowledge Knowledge = new ShopperSellerKnowledge();
        public readonly SimReservations Reservations = new SimReservations();
        public readonly DomesticLaborService Domestic = new DomesticLaborService();
        public readonly FamilyLaborLedger FamilyLabor = new FamilyLaborLedger();
        public readonly DailyActivityPlanner Planner = new DailyActivityPlanner();
        public readonly AutonomousWorkGenerator Generator = new AutonomousWorkGenerator();
        public readonly ManagerWorkAllocator BakeryAllocator = new ManagerWorkAllocator();
        public readonly ManagerWorkAllocator Farm1Allocator = new ManagerWorkAllocator();
        public readonly ManagerWorkAllocator StoreAllocator = new ManagerWorkAllocator();
        public const int StoreShelfTargetBread = 60;
        public readonly HouseholdShoppingLoop ShoppingLoop;
        public readonly ShoppingTripDriver ShoppingDriver;
        public readonly DomesticLaborDriver DomesticDriver = new DomesticLaborDriver();

        public readonly List<string> Diag = new List<string>();
        public readonly Dictionary<int, string> PersonLocations = new Dictionary<int, string>();

        // ---------- sim-owned single-writer books ----------
        public readonly Dictionary<string, int> BusinessCash = new Dictionary<string, int>();
        public readonly Dictionary<string, int> BusinessGoods = new Dictionary<string, int>();
        public readonly List<Employment> Employments = new List<Employment>();
        public readonly HashSet<int> SettledPayrollWeeks = new HashSet<int>();
        public readonly List<string> PayrollArrears = new List<string>();

        // ---------- reconciliation ledgers (append-only) ----------
        public readonly List<GoodsFlow> GoodsFlows = new List<GoodsFlow>();
        public readonly List<CashTransfer> CashTransfers = new List<CashTransfer>();
        public readonly Dictionary<string, int> CompletedTasksByDefinition = new Dictionary<string, int>();
        public readonly Dictionary<string, int> FailedTasksByDefinition = new Dictionary<string, int>();
        public int TotalWorkMinutes;
        public int ThresherOccupiedMinutes;
        public int OvenOccupiedMinutes;
        public int MillOccupiedMinutes;
        public int MissedMeals;
        public readonly Dictionary<int, int> MissedMealsByHousehold = new Dictionary<int, int>();
        public int WastedUnits;
        public int ThresherContentionDeferrals;
        public int BakeRefusalsForFlour;
        public int EmploymentConflictRefusals;
        public int UnmetBreadNeeds;

        public float NowSeconds;
        public int CurrentDay = StartDay;
        public float MillCondition01 = 1f;
        public bool MillFailed;
        public bool MillBeltInstalled = true;

        public readonly SimStore Store1 = new SimStore();

        [Serializable]
        public sealed class GoodsFlow
        {
            public int DayIndex;
            public string Owner = string.Empty; // "H7" | "bakery-1"
            public string ItemId = string.Empty;
            public int DeltaUnits; // +produced/purchased, -consumed/sold/wasted
            public string Reason = string.Empty;
        }

        [Serializable]
        public sealed class CashTransfer
        {
            public int DayIndex;
            public string From = string.Empty;
            public string To = string.Empty;
            public int AmountCents;
            public string Reason = string.Empty;
        }

        public LivingWorldSim()
        {
            ShoppingLoop = new HouseholdShoppingLoop(
                Population, Membership, Ledgers, Inventories, Needs, Directory, Journeys,
                Tasks, Budgets, Schedules, Knowledge,
                obligations: null, entityIds: null,
                personEntityId: pid => EntityId.For(EntityKind.Person, pid),
                spendingPolicyProvider: null);
            ShoppingDriver = new ShoppingTripDriver(ShoppingLoop);
        }
    }

    /// <summary>Deterministic exclusive-key reservation authority (test sim only).</summary>
    public sealed class SimReservations : IUnifiedReservationAuthority
    {
        private readonly Dictionary<string, ReservationHandle> live = new Dictionary<string, ReservationHandle>();
        private int sequence;

        public int LiveReservationCount => live.Count;

        public string TryReserveAll(ReservationRequest request, out ReservationHandle handle)
        {
            handle = null;
            foreach (ReservationItem item in request.Items)
            {
                foreach (ReservationHandle existing in live.Values)
                {
                    foreach (ReservationItem held in existing.Items)
                    {
                        if (held.ResourceKey == item.ResourceKey &&
                            item.StartGameSeconds < held.EndGameSeconds &&
                            held.StartGameSeconds < item.EndGameSeconds)
                        {
                            return $"refused: {item.ResourceKey} held by {existing.OwnerTaskId}";
                        }
                    }
                }
            }

            var granted = new ReservationHandle("resv-" + (++sequence), request.OwnerTaskId,
                new List<ReservationItem>(request.Items));
            live[granted.ReservationId] = granted;
            handle = granted;
            return null;
        }

        public void Release(string reservationId) { live.Remove(reservationId); }

        public void ReleaseAllForTask(string ownerTaskId)
        {
            var dead = new List<string>();
            foreach (var kv in live)
            {
                if (kv.Value.OwnerTaskId == ownerTaskId) dead.Add(kv.Key);
            }

            foreach (string id in dead) live.Remove(id);
        }

        public bool IsReserved(string resourceKey, float atGameSeconds)
        {
            foreach (ReservationHandle h in live.Values)
            {
                foreach (ReservationItem item in h.Items)
                {
                    if (item.ResourceKey == resourceKey &&
                        atGameSeconds >= item.StartGameSeconds && atGameSeconds < item.EndGameSeconds)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public float AvailableCapacity(string resourceKey, float atGameSeconds) =>
            IsReserved(resourceKey, atGameSeconds) ? 0f : 1f;

        public List<ReservationHandle> ReservationsForTask(string ownerTaskId)
        {
            var result = new List<ReservationHandle>();
            foreach (ReservationHandle h in live.Values)
            {
                if (h.OwnerTaskId == ownerTaskId) result.Add(h);
            }

            return result;
        }

        /// <summary>Drops reservations whose windows fully passed (daily hygiene).</summary>
        public void PruneBefore(float gameSeconds)
        {
            var dead = new List<string>();
            foreach (var kv in live)
            {
                bool allPast = true;
                foreach (ReservationItem item in kv.Value.Items)
                {
                    if (item.EndGameSeconds > gameSeconds) { allPast = false; break; }
                }

                if (allPast) dead.Add(kv.Key);
            }

            foreach (string id in dead) live.Remove(id);
        }
    }

    /// <summary>The general store (S1): real stock, real prices, real sales.</summary>
    public sealed class SimStore : IItemResolvingSupplier
    {
        public string SupplierBusinessId { get; set; } = LivingWorldSim.Store;
        public string SupplierName { get; set; } = "General Store";
        public string LocationId { get; set; } = "store";
        public Dictionary<string, int> Stock = new Dictionary<string, int>();
        public Dictionary<string, int> Prices = new Dictionary<string, int>();
        /// <summary>Units actually sold, per item — the retail cross-check.</summary>
        public Dictionary<string, int> UnitsSold = new Dictionary<string, int>();
        /// <summary>Backroom stock (not on shelves yet) — restocked by clerks.</summary>
        public Dictionary<string, int> Backroom = new Dictionary<string, int>();
        public int CashReceived;
        public bool Closed;
        private int receiptSequence;

        public string LastReceiptId { get; private set; } = string.Empty;

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

        public int SellItem(string itemId, int requestedUnits, int dayIndex, ShoppingSettlement settlement, List<string> diagnostics)
        {
            int have = ItemStockUnits(itemId);
            int sold = Math.Min(Math.Max(0, requestedUnits), have);
            if (sold > 0)
            {
                Stock[itemId] = have - sold;
                int u;
                UnitsSold.TryGetValue(itemId, out u);
                UnitsSold[itemId] = u + sold;
                LastReceiptId = $"rcpt-{++receiptSequence}";
                if (settlement != null && settlement.Mode == ShoppingSettlementMode.CashFromLedger)
                {
                    CashReceived += settlement.AmountCents;
                }
            }

            return sold;
        }
    }
}

namespace LandLedgers.Editor.Integration
{
    public sealed partial class LivingWorldSim
    {
        /// <summary>Builds the whole world deterministically. No RNG anywhere.</summary>
        public void Build()
        {
            BuildStatic();
            SeedDynamic();
            SnapshotInitialStocks();
        }

        /// <summary>
        /// Static world: catalogs, people, places, employment. Never changes
        /// during the sim — safe to rebuild before a save/load restore.
        /// </summary>
        public void BuildStatic()
        {
            var diag = new List<string>();
            AutonomousWorkGenerator.GeneratedWorkCatalog.RegisterAll(Tasks, diag);
            DomesticTaskCatalog.RegisterAll(Tasks, diag);
            HouseholdShoppingLoop.RegisterTaskDefinitions(Tasks);

            // --- people ---
            AddPerson(P1, "Tomas", "Morrow", 34, AgeBand.Adult18Plus);
            AddPerson(P2, "Petra", "Morrow", 30, AgeBand.Adult18Plus);
            AddPerson(P3, "Milo", "Morrow", 8, AgeBand.Child0To9);
            AddPerson(P4, "Sable", "Alder", 40, AgeBand.Adult18Plus);
            AddPerson(P5, "Wren", "Alder", 36, AgeBand.Adult18Plus);
            AddPerson(P6, "Ash", "Birch", 70, AgeBand.Adult18Plus);
            AddPerson(P7, "Dana", "Reeve", 45, AgeBand.Adult18Plus);
            AddPerson(P8, "Otto", "Lark", 50, AgeBand.Adult18Plus);
            AddPerson(P9, "Robin", "Fern", 28, AgeBand.Adult18Plus);

            AddHousehold(H1, "Morrow"); AddHousehold(H2, "Alder"); AddHousehold(H3, "Birch");
            AddHousehold(H4, "Reeve"); AddHousehold(H5, "Lark"); AddHousehold(H6, "Fern");
            AddMember(1, P1, H1); AddMember(2, P2, H1); AddMember(3, P3, H1);
            AddMember(4, P4, H2); AddMember(5, P5, H2);
            AddMember(6, P6, H3);
            AddMember(7, P7, H4);
            AddMember(8, P8, H5);
            AddMember(9, P9, H6);

            // --- places ---
            Journeys.RegisterLocation(new JourneyLocation("home-7", JourneyLocationKind.Farmstead, "Morrow Farm", 0f, 0f));
            Journeys.RegisterLocation(new JourneyLocation("home-8", JourneyLocationKind.Farmstead, "Alder Farm", 1f, 1f));
            Journeys.RegisterLocation(new JourneyLocation("home-9", JourneyLocationKind.Farmstead, "Birch Cottage", 3f, 0f));
            Journeys.RegisterLocation(new JourneyLocation("home-10", JourneyLocationKind.Farmstead, "Reeve House", 1f, 3f));
            Journeys.RegisterLocation(new JourneyLocation("home-11", JourneyLocationKind.Farmstead, "Lark House", 2f, 2f));
            Journeys.RegisterLocation(new JourneyLocation("home-12", JourneyLocationKind.Farmstead, "Fern Cottage", 4f, 1f));
            Journeys.RegisterLocation(new JourneyLocation("bakery", JourneyLocationKind.TownBuilding, "Bakery", 2f, 0f));
            Journeys.RegisterLocation(new JourneyLocation("field-1", JourneyLocationKind.Farmstead, "Farm 1 Fields", 1f, 2f));
            Journeys.RegisterLocation(new JourneyLocation("field-2", JourneyLocationKind.Farmstead, "Farm 2 Fields", 4f, 2f));
            Journeys.RegisterLocation(new JourneyLocation("mill", JourneyLocationKind.Mill, "Mill", 1.5f, 2f));
            Journeys.RegisterLocation(new JourneyLocation("barn", JourneyLocationKind.Farmstead, "Thresher Barn", 2.5f, 2f));
            Journeys.RegisterLocation(new JourneyLocation("store", JourneyLocationKind.Store, "General Store", 2f, 1f));
            string[] homes = { "home-7", "home-8", "home-9", "home-10", "home-11", "home-12" };
            foreach (string home in homes)
            {
                Journeys.AddEdge(home, "store", 2.0f, "town road");
                Journeys.AddEdge(home, "bakery", 2.0f, "town road");
            }

            Journeys.AddEdge("home-8", "field-1", 1.0f, "farm lane");
            Journeys.AddEdge("home-12", "field-2", 1.0f, "farm lane");
            Journeys.AddEdge("field-1", "mill", 1.0f, "farm lane");
            Journeys.AddEdge("field-1", "barn", 1.5f, "farm lane");
            Journeys.AddEdge("field-2", "barn", 1.5f, "farm lane");

            foreach (int pid in new[] { P1, P2, P3, P4, P5, P6, P7, P8, P9 })
            {
                PersonLocations[pid] = "home-" + HouseholdOf(pid);
            }

            Directory.Register(Store1);

            // Shoppers know the store.
            Knowledge.LearnSupplier(P2, Store, "shops here weekly", StartDay);
            Knowledge.LearnSupplier(P5, Store, "works here", StartDay);
            Knowledge.LearnSupplier(P6, Store, "shops here weekly", StartDay);
            Knowledge.LearnSupplier(P7, Store, "shops here weekly", StartDay);
            Knowledge.LearnSupplier(P9, Store, "shops here weekly", StartDay);

            // --- employment (real relationships with agreed wages) ---
            Employments.Add(new Employment { Id = "emp-b1-1", PersonId = P1, BusinessId = Bakery, HouseholdId = H1, WeeklyWageCents = 2000 });
            Employments.Add(new Employment { Id = "emp-f1-1", PersonId = P4, BusinessId = Farm1, HouseholdId = H2, WeeklyWageCents = 1800 });
            Employments.Add(new Employment { Id = "emp-s1-1", PersonId = P5, BusinessId = Store, HouseholdId = H2, WeeklyWageCents = 1500 });
            Employments.Add(new Employment { Id = "emp-b1-mgr", PersonId = P7, BusinessId = Bakery, HouseholdId = H4, WeeklyWageCents = 2500 });
            Employments.Add(new Employment { Id = "emp-f2-1", PersonId = P9, BusinessId = Farm2, HouseholdId = H6, WeeklyWageCents = 1700 });

            Diag.AddRange(diag);
        }

        /// <summary>
        /// Dynamic seed: cash, pantries, business books, opening events.
        /// NOT rebuilt on load — the save DTO carries all of this.
        /// </summary>
        public void SeedDynamic()
        {
            // --- household cash (seeded, documented) ---
            SeedHouseholdCash(H1, 8000);
            SeedHouseholdCash(H2, 6000);
            SeedHouseholdCash(H3, 4000);
            SeedHouseholdCash(H4, 5000);
            SeedHouseholdCash(H5, 10000);
            SeedHouseholdCash(H6, 3000);

            // --- household pantries ---
            AddPantry(H1, Flour, 10, "harvest stores");
            AddPantry(H1, Bread, 6, "baked at home");
            AddPantry(H2, Flour, 8, "harvest stores");
            AddPantry(H2, Bread, 4, "baked at home");
            AddPantry(H3, Bread, 4, "bought at market");
            AddPantry(H3, PreservedFood, 6, "cellared");
            AddPantry(H4, Bread, 4, "bought at market");
            AddPantry(H4, Flour, 4, "bought at market");
            AddPantry(H6, Bread, 4, "bought at market");

            // --- businesses ---
            BusinessCash[Bakery] = 30000;
            BusinessCash[Farm1] = 20000;
            BusinessCash[Farm2] = 20000;
            BusinessCash[Store] = 40000;
            SetBizGoods(Bakery, Flour, 20);
            SetBizGoods(Bakery, Bread, 20);
            SetBizGoods(Farm1, Grain, 60);
            SetBizGoods(Farm1, Flour, 10);
            SetBizGoods(Farm2, Grain, 0);

            // --- the store: real stock, real prices ---
            Store1.Stock[Bread] = 30;
            Store1.Prices[Bread] = 150;
            Store1.Stock[Flour] = 24;
            Store1.Prices[Flour] = 120;
            Store1.Stock[MillBelt] = 1;
            Store1.Prices[MillBelt] = 800;

            // --- kick off the event-driven world ---
            NowSeconds = StartDay * 86400f;
            ScheduleSimEvent("sim:day:" + StartDay, StartDay * 86400f + 300f);   // 00:05 day boundary
            ScheduleSimEvent("sim:meal:" + StartDay, StartDay * 86400f + 19 * 3600f);
        }

        private void AddPerson(int id, string first, string last, int age, AgeBand band)
        {
            Population.people.Add(new PersonState
            {
                id = id, firstName = first, lastName = last, age = age, ageBand = band,
            });
        }

        private void AddHousehold(int id, string name)
        {
            Population.households.Add(new HouseholdState { id = id, householdName = name });
        }

        private void AddMember(int membershipId, int personId, int householdId)
        {
            Membership.Register(new HouseholdMembership
            {
                MembershipId = membershipId, PersonId = personId,
                HouseholdId = householdId, StartDayIndex = 0,
            });
        }

        private int HouseholdOf(int personId)
        {
            return Membership.GetActiveHouseholdId(personId);
        }

        private void SeedHouseholdCash(int householdId, int cents)
        {
            string rejection = Ledgers.GetOrCreate(householdId).RecordInflow(
                StartDay, cents, HouseholdIncomeSource.OwnerContribution,
                "sim-seed", "sim seed funds", "sim");
            if (rejection != null)
            {
                Diag.Add("LivingWorldSim: seed cash rejected for H" + householdId + ": " + rejection);
            }
        }

        private void AddPantry(int householdId, string itemId, int units, string source)
        {
            string rejection = Inventories.GetOrCreate(householdId).AddLot(itemId, units, StartDay, source);
            if (rejection != null)
            {
                Diag.Add("LivingWorldSim: pantry seed rejected for H" + householdId + ": " + rejection);
            }
        }

        private void ScheduleSimEvent(string payload, float atGameSeconds)
        {
            string eventId;
            DueEvents.Schedule(DueEventKind.ActivityWindowDue, atGameSeconds, payload, out eventId);
        }

        public int BizGoods(string businessId, string itemId)
        {
            int v;
            return BusinessGoods.TryGetValue(businessId + "|" + itemId, out v) ? v : 0;
        }

        private void SetBizGoods(string businessId, string itemId, int units)
        {
            BusinessGoods[businessId + "|" + itemId] = Math.Max(0, units);
        }

        private void MoveBizGoods(string businessId, string itemId, int deltaUnits, int day, string reason)
        {
            SetBizGoods(businessId, itemId, BizGoods(businessId, itemId) + deltaUnits);
            GoodsFlows.Add(new GoodsFlow
            {
                DayIndex = day, Owner = businessId, ItemId = itemId,
                DeltaUnits = deltaUnits, Reason = reason,
            });
        }

        /// <summary>Single-writer business cash transfer. Never creates money.</summary>
        private string TransferBusinessCash(string from, string to, int amountCents, int day, string reason)
        {
            if (amountCents <= 0) return "non-positive transfer";
            int have = BusinessCash.TryGetValue(from, out int f) ? f : 0;
            if (have < amountCents) return $"insufficient funds: {from} holds {have}c";
            BusinessCash[from] = have - amountCents;
            BusinessCash[to] = (BusinessCash.TryGetValue(to, out int t) ? t : 0) + amountCents;
            CashTransfers.Add(new CashTransfer
            {
                DayIndex = day, From = from, To = to, AmountCents = amountCents, Reason = reason,
            });
            return null;
        }
    }
}

namespace LandLedgers.Editor.Integration
{
    public sealed partial class LivingWorldSim
    {
        private ShoppingLoopOptions loopOptions;
        private readonly Dictionary<int, ShoppingLoopOptions> loopOptionsByHousehold = new Dictionary<int, ShoppingLoopOptions>();

        public void BuildLoopOptions()
        {
            loopOptionsByHousehold[H1] = MakeLoopOptions(P2, 600);
            loopOptionsByHousehold[H2] = MakeLoopOptions(P5, 600);
            loopOptionsByHousehold[H3] = MakeLoopOptions(P6, 600);
            loopOptionsByHousehold[H4] = MakeLoopOptions(P7, 800); // after the management round
            loopOptionsByHousehold[H6] = MakeLoopOptions(P9, 800); // Robin works 540–780
            loopOptions = loopOptionsByHousehold[H1];
        }

        private ShoppingLoopOptions MakeLoopOptions(int shopperPersonId, int departureMinute)
        {
            return new ShoppingLoopOptions
            {
                PersonLocationId = pid => PersonLocations.TryGetValue(pid, out string l) ? l : "home-7",
                SetPersonLocation = (pid, l) => PersonLocations[pid] = l,
                DepartureMinuteOfDay = departureMinute,
                InStoreMinutes = 15,
                AllowDelivery = false,
                Diagnostics = Diag,
                ShopperPersonIdOverride = shopperPersonId,
            };
        }

        /// <summary>
        /// Advances the world to targetSeconds, processing due events in due
        /// order. The step size changes NOTHING consequential: every state
        /// change is event-driven, so 60s, 600s, 3600s, and 6000s steps give
        /// identical outcomes.
        /// </summary>
        public void AdvanceTo(float targetSeconds, float stepSeconds)
        {
            int guard = 0;
            while (NowSeconds < targetSeconds && guard++ < 2000000)
            {
                float stepEnd = Math.Min(NowSeconds + stepSeconds, targetSeconds);
                foreach (DueEvent ev in DueEvents.PopDue(stepEnd))
                {
                    Dispatch(ev);
                }

                int day = (int)Math.Floor(stepEnd / 86400f);
                DomesticDriver.AdvanceTick(stepEnd, Tasks, Budgets, Schedules, Domestic, DueEvents, Diag);
                ShoppingDriver.AdvanceTick(stepEnd, day, DueEvents, Diag);
                NowSeconds = stepEnd;
            }
        }

        public void AdvanceDays(int days, float stepSeconds)
        {
            AdvanceTo((StartDay + days) * 86400f, stepSeconds);
        }

        /// <summary>Advances until predicate() is true (or the cap), for save/load probes.</summary>
        public void AdvanceUntil(Func<bool> predicate, float stepSeconds, float capSeconds)
        {
            int guard = 0;
            while (!predicate() && NowSeconds < capSeconds && guard++ < 2000000)
            {
                AdvanceTo(Math.Min(NowSeconds + stepSeconds, capSeconds), stepSeconds);
            }
        }

        /// <summary>True when any open task with this definition is InProgress.</summary>
        public bool AnyTaskInProgress(string definitionId)
        {
            foreach (string biz in new[] { Bakery, Farm1, Farm2, Store })
            {
                foreach (WorkTask t in Tasks.GetOpenTasks(BusinessOwnerEntity(biz)))
                {
                    if (t != null && t.Status == TaskStatus.InProgress &&
                        string.Equals(t.DefinitionId, definitionId, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Cancels the first open task with this definition (after-cancellation probe).</summary>
        public bool CancelFirstOpenTask(string businessId, string definitionId)
        {
            foreach (WorkTask t in Tasks.GetOpenTasks(BusinessOwnerEntity(businessId)))
            {
                if (t != null && string.Equals(t.DefinitionId, definitionId, StringComparison.Ordinal))
                {
                    bool ok = Tasks.CancelTask(t.TaskId, Budgets);
                    Diag.Add($"LivingWorldSim: cancelled {t.TaskId} ('{definitionId}'): {ok}");
                    return ok;
                }
            }

            return false;
        }

        /// <summary>Human-readable reconciliation figures for the final report.</summary>
        public string Summary()
        {
            var lines = new List<string>
            {
                $"days simulated: {CurrentDay - StartDay + 1}",
                $"cash total: {TotalCashCents()}c (expected {36000 + 110000 + 800 * SettledPayrollWeeks.Count}c)",
                $"work minutes (business tasks): {TotalWorkMinutes}",
                $"tasks completed: {TasksHash()}",
                $"thresher occupied: {ThresherOccupiedMinutes} min, contention deferrals: {ThresherContentionDeferrals}",
                $"oven occupied: {OvenOccupiedMinutes} min, mill occupied: {MillOccupiedMinutes} min",
                $"bake refusals (flour): {BakeRefusalsForFlour}",
                $"employment-conflict refusals: {EmploymentConflictRefusals}",
                $"missed meals: {MissedMeals}, wasted units: {WastedUnits}, unmet bread needs: {UnmetBreadNeeds}",
                $"payroll weeks settled: {SettledPayrollWeeks.Count}, arrears: {PayrollArrears.Count}",
                $"store retail bread sold: {(Store1.UnitsSold.TryGetValue(Bread, out int sb) ? sb : 0)}u",
                $"mill failed at end: {MillFailed}",
            };
            return string.Join("\n", lines);
        }

        private void Dispatch(DueEvent ev)
        {
            if (ev == null) return;
            switch (ev.Kind)
            {
                case DueEventKind.ActivityWindowDue:
                    if (!string.IsNullOrEmpty(ev.PayloadRef) && ev.PayloadRef.StartsWith("sim:", StringComparison.Ordinal))
                    {
                        HandleSimControl(ev);
                    }
                    else
                    {
                        HandleTaskWindowStart(ev);
                    }

                    break;
                case DueEventKind.TaskCompletionDue:
                    ShoppingDriver.HandleDueEvent(ev, CurrentDay, ev.DueGameSeconds, DueEvents, Diag);
                    DomesticDriver.HandleDueEvent(ev, Tasks, Budgets, Schedules, Domestic, FamilyLabor, DueEvents, CurrentDay, Diag);
                    HandleBusinessTaskCompletion(ev);
                    break;
                case DueEventKind.JourneyStageDue:
                    ShoppingDriver.HandleDueEvent(ev, CurrentDay, ev.DueGameSeconds, DueEvents, Diag);
                    break;
            }
        }

        private void HandleSimControl(DueEvent ev)
        {
            // Payloads: "sim:day:N", "sim:meal:N", "sim:payroll:W".
            string[] parts = ev.PayloadRef.Split(':');
            if (parts.Length < 3) return;
            switch (parts[1])
            {
                case "day":
                    RunDayCycle(int.Parse(parts[2]));
                    break;
                case "meal":
                    RunEveningMeals(int.Parse(parts[2]));
                    break;
                case "payroll":
                    RunPayroll(int.Parse(parts[2]), (int)Math.Floor(ev.DueGameSeconds / 86400f));
                    break;
            }
        }

        private void HandleTaskWindowStart(DueEvent ev)
        {
            WorkTask task = FindBusinessTask(ev.PayloadRef);
            if (task == null || task.Status != TaskStatus.Assigned) return;

            int day = (int)Math.Floor(ev.DueGameSeconds / 86400f);
            string rejection;
            if (!Tasks.StartTask(task.TaskId, day, out rejection))
            {
                Diag.Add("LivingWorldSim: StartTask refused for " + task.TaskId + ": " + rejection);
                return;
            }

            // Real presence: the worker goes to the work site.
            if (task.HasAssignee)
            {
                PersonLocations[task.AssigneeId.Id] = WorkSiteFor(task);
            }

            string completionId;
            DueEvents.Schedule(DueEventKind.TaskCompletionDue,
                ev.DueGameSeconds + Math.Max(1, task.PlannedMinutes) * 60f,
                task.TaskId.ToString(), out completionId);
            Diag.Add($"LivingWorldSim: {task.TaskId} ('{task.DefinitionId}') started, completes at +{task.PlannedMinutes} min.");
        }

        private string WorkSiteFor(WorkTask task)
        {
            string biz = OwnerBusinessId(task.OwnerId);
            switch (biz)
            {
                case Bakery: return "bakery";
                case Farm1: return task.DefinitionId == AutonomousWorkGenerator.GeneratedWorkCatalog.MillGrainId ? "mill" : "field-1";
                case Farm2: return "field-2";
                default: return "bakery";
            }
        }

        private void HandleBusinessTaskCompletion(DueEvent ev)
        {
            WorkTask task = FindBusinessTask(ev.PayloadRef);
            if (task == null || !task.IsOpen || task.Status != TaskStatus.InProgress) return;

            int day = (int)Math.Floor(ev.DueGameSeconds / 86400f);
            string biz = OwnerBusinessId(task.OwnerId);
            string def = task.DefinitionId;

            bool failed = false;
            string failReason = null;

            if (string.Equals(def, AutonomousWorkGenerator.GeneratedWorkCatalog.ProductionWorkOrderId, StringComparison.Ordinal) && biz == Bakery)
            {
                // Bake: 10 flour → 30 bread (half batch with a poor oven).
                if (BizGoods(Bakery, Flour) >= 10)
                {
                    MoveBizGoods(Bakery, Flour, -10, day, "bake batch");
                    MoveBizGoods(Bakery, Bread, 30, day, "bake batch");
                    breadProducedToday += 30;
                }
                else
                {
                    failed = true;
                    failReason = "no flour on hand";
                    BakeRefusalsForFlour++;
                }
            }
            else if (string.Equals(def, AutonomousWorkGenerator.GeneratedWorkCatalog.FieldWorkId, StringComparison.Ordinal))
            {
                MoveBizGoods(biz, Grain, 20, day, "field work");
            }
            else if (string.Equals(def, AutonomousWorkGenerator.GeneratedWorkCatalog.ThreshGrainId, StringComparison.Ordinal))
            {
                if (BizGoods(biz, Grain) >= 10)
                {
                    MoveBizGoods(biz, Grain, -10, day, "threshing");
                    MoveBizGoods(biz, Flour, 8, day, "threshing");
                    ThresherOccupiedMinutes += task.PlannedMinutes;
                }
                else
                {
                    failed = true;
                    failReason = "no grain on hand";
                }
            }
            else if (string.Equals(def, AutonomousWorkGenerator.GeneratedWorkCatalog.MillGrainId, StringComparison.Ordinal))
            {
                if (BizGoods(biz, Grain) >= 10)
                {
                    MoveBizGoods(biz, Grain, -10, day, "milling");
                    MoveBizGoods(biz, Flour, 9, day, "milling");
                    MillOccupiedMinutes += task.PlannedMinutes;
                }
                else
                {
                    failed = true;
                    failReason = "no grain on hand";
                }
            }
            else if (string.Equals(def, AutonomousWorkGenerator.GeneratedWorkCatalog.MaintenanceWorkOrderId, StringComparison.Ordinal) && biz == Farm1)
            {
                MillFailed = false;
                MillCondition01 = 1f;
                Diag.Add($"LivingWorldSim: day {day} — mill repaired, back in service.");
            }
            else if (string.Equals(def, AutonomousWorkGenerator.GeneratedWorkCatalog.ProductionWorkOrderId, StringComparison.Ordinal))
            {
                OvenOccupiedMinutes += task.PlannedMinutes;
            }
            else if (string.Equals(def, AutonomousWorkGenerator.GeneratedWorkCatalog.RestockShelvesId, StringComparison.Ordinal) &&
                biz == Store)
            {
                // The clerk moves bread from the backroom to the shelf: real
                // goods movement, honestly recorded.
                int shelfNeed = Math.Max(0, StoreShelfTargetBread - Store1.ItemStockUnits(Bread));
                int br;
                Store1.Backroom.TryGetValue(Bread, out br);
                int moved = Math.Min(shelfNeed, br);
                if (moved > 0)
                {
                    Store1.Backroom[Bread] = br - moved;
                    Store1.Stock[Bread] = Store1.ItemStockUnits(Bread) + moved;
                    GoodsFlows.Add(new GoodsFlow { DayIndex = day, Owner = Store, ItemId = Bread, DeltaUnits = 0, Reason = "shelf restock (backroom to shelf)" });
                    Diag.Add($"LivingWorldSim: day {day} — clerk restocked {moved}u bread to the shelf.");
                }
            }

            // Equipment holds release with the task.
            Reservations.ReleaseAllForTask("task:" + task.TaskId);

            string rejection;
            if (failed)
            {
                bool reached;
                Tasks.RecordWorkProgress(task.TaskId, task.PlannedMinutes, Budgets, day, out reached, out rejection);
                Tasks.CompleteTask(task.TaskId, TaskOutcome.RecoverableFailure, day, Budgets);
                Increment(FailedTasksByDefinition, def);
                Diag.Add($"LivingWorldSim: {task.TaskId} ('{def}') FAILED — {failReason}. Rework may follow.");
            }
            else
            {
                Tasks.RecordWork(task.TaskId, task.PlannedMinutes, Budgets, day, out rejection);
                Increment(CompletedTasksByDefinition, def);
                TotalWorkMinutes += task.PlannedMinutes;
            }

            if (!string.IsNullOrEmpty(rejection))
            {
                Diag.Add("LivingWorldSim: completion accounting refused for " + task.TaskId + ": " + rejection);
            }

            if (task.HasAssignee)
            {
                PersonLocations[task.AssigneeId.Id] = "home-" + HouseholdOf(task.AssigneeId.Id);
            }
        }

        private static void Increment(Dictionary<string, int> dict, string key)
        {
            int v;
            dict.TryGetValue(key, out v);
            dict[key] = v + 1;
        }

        private string OwnerBusinessId(EntityId ownerId)
        {
            if (ownerId.Kind == EntityKind.Business)
            {
                switch (ownerId.Id)
                {
                    case 101: return Bakery;
                    case 102: return Farm1;
                    case 103: return Farm2;
                    case 104: return Store;
                }
            }

            return string.Empty;
        }

        private EntityId BusinessOwnerEntity(string businessId)
        {
            switch (businessId)
            {
                case Bakery: return EntityId.For(EntityKind.Business, 101);
                case Farm1: return EntityId.For(EntityKind.Business, 102);
                case Farm2: return EntityId.For(EntityKind.Business, 103);
                case Store: return EntityId.For(EntityKind.Business, 104);
                default: return default(EntityId);
            }
        }

        private WorkTask FindBusinessTask(string taskIdString)
        {
            if (string.IsNullOrEmpty(taskIdString)) return null;
            foreach (string biz in new[] { Bakery, Farm1, Farm2, Store })
            {
                foreach (WorkTask t in Tasks.GetOpenTasks(BusinessOwnerEntity(biz)))
                {
                    if (t != null && string.Equals(t.TaskId.ToString(), taskIdString, StringComparison.Ordinal))
                    {
                        return t;
                    }
                }
            }

            return null;
        }

        private int breadProducedToday;
    }
}

namespace LandLedgers.Editor.Integration
{
    public sealed partial class LivingWorldSim
    {
        private void RunDayCycle(int day)
        {
            CurrentDay = day;
            breadProducedToday = 0;
            Budgets.EnsureDay(day);
            Reservations.PruneBefore(day * 86400f);
            Diag.Add($"LivingWorldSim: ===== day {day} begins =====");

            // 1. Daily activity planning: sleep, meals, rest — real person-time.
            PlanPersonDay(P1, H1, false, false, day);
            PlanPersonDay(P2, H1, false, false, day);
            PlanPersonDay(P3, H1, false, false, day);
            PlanPersonDay(P4, H2, false, false, day);
            PlanPersonDay(P5, H2, false, false, day);
            PlanPersonDay(P6, H3, false, false, day);
            PlanPersonDay(P7, H4, false, false, day); // the delegated manager (managing time reserved by the allocator)
            PlanPersonDay(P8, H5, false, false, day);
            PlanPersonDay(P9, H6, false, false, day);

            // 2. Domestic work for every household (P4 excluded on conflict day).
            bool conflictDay = day == StartDay + 5;
            DriveDomestic(H1, new[] { P1, P2, P3 }, 3, day);
            DriveDomestic(H2, conflictDay ? new[] { P5 } : new[] { P4, P5 }, 2, day);
            DriveDomestic(H3, new[] { P6 }, 1, day);
            DriveDomestic(H4, new[] { P7 }, 1, day);
            DriveDomestic(H5, new[] { P8 }, 1, day);
            DriveDomestic(H6, new[] { P9 }, 1, day);

            // 3. The deliberate mill breakdown (days 5–6 of the sim).
            if (day == StartDay + 5)
            {
                MillFailed = true;
                MillCondition01 = 0.1f;
                MillBeltInstalled = false; // the belt snapped — repair needs a new one
                Diag.Add($"LivingWorldSim: day {day} — the mill broke down (condition 0.1). Repair needs a mill belt.");
            }

            // 4. Autonomous work generation from legitimate needs.
            GenerateForBusinesses(day);
            GenerateForHouseholds(day);

            // 5. Farm processing tasks (mill or shared thresher).
            CreateFarmProcessingTasks(day);

            // 6. Delegated management: Dana allocates for the bakery and farm-1.
            AllocateWithFreshWindows(BakeryAllocator, Bakery, P7,
                new[] { new WorkerSpec { PersonId = P1, WorkStart = 540, WorkEnd = 1020 } }, day);
            AllocateWithFreshWindows(Farm1Allocator, Farm1, P7,
                new[] { new WorkerSpec { PersonId = P4, WorkStart = 540, WorkEnd = 1020 } }, day);
            AllocateWithFreshWindows(StoreAllocator, Store, P7,
                new[] { new WorkerSpec { PersonId = P5, WorkStart = 540, WorkEnd = 1020 } }, day);

            // 7. Owner-operated farm-2: Otto assigns Robin directly.
            OwnerAssignFarm2(day);

            // 8. Wholesale procurement (the manager's daily round).
            RunProcurement(day);

            // 9. Household shopping through real time.
            if (loopOptions == null) BuildLoopOptions();
            foreach (int h in new[] { H1, H2, H3, H4, H6 })
            {
                if (Needs.GetOpenNeeds(h).Count == 0) continue;
                bool tripActive = false;
                foreach (var t in ShoppingDriver.ActiveTrips)
                {
                    if (t != null && t.HouseholdId == h) { tripActive = true; break; }
                }

                if (!tripActive)
                {
                    ShoppingLoopOptions options;
                    if (!loopOptionsByHousehold.TryGetValue(h, out options) || options == null)
                    {
                        options = loopOptions;
                    }

                    ShoppingDriver.PlanForHousehold(h, Needs, day, options, Diag);
                }
            }

            // 10. The deliberate time conflict: Sable is on farm work 540–780.
            if (conflictDay)
            {
                string refusal = Domestic.ScheduleDomesticWork(
                    H2, P4, DomesticTaskCatalog.ChoresId, day, 600,
                    Tasks, Budgets, Schedules, Diag);
                if (refusal != null)
                {
                    EmploymentConflictRefusals++;
                    Diag.Add($"LivingWorldSim: day {day} — deliberate conflict refused loudly: {refusal}");
                }
                else
                {
                    Diag.Add($"LivingWorldSim: day {day} — WARNING: expected employment conflict did not refuse.");
                }
            }

            // 11. Sweep the store's retail cash into its cash book (real money).
            if (Store1.CashReceived > 0)
            {
                int swept = Store1.CashReceived;
                Store1.CashReceived = 0;
                BusinessCash[Store] = BusinessCash[Store] + swept;
                CashTransfers.Add(new CashTransfer
                {
                    DayIndex = day, From = "households", To = Store,
                    AmountCents = swept, Reason = "retail sweep",
                });
            }

            // 12. Tomorrow.
            ScheduleSimEvent("sim:day:" + (day + 1), (day + 1) * 86400f + 300f);
            ScheduleSimEvent("sim:meal:" + (day + 1), (day + 1) * 86400f + 19 * 3600f);
            if ((day - StartDay + 1) % 7 == 0)
            {
                ScheduleSimEvent("sim:payroll:" + ((day - StartDay + 1) / 7 - 1), day * 86400f + 20 * 3600f);
            }
        }

        private void PlanPersonDay(int personId, int householdId, bool isJobSeeker, bool isManager, int day)
        {
            var inputs = new DailyActivityPlanner.PersonDayPlanInputs
            {
                PersonId = personId,
                HouseholdId = householdId,
                IsJobSeeker = isJobSeeker,
                IsManager = isManager,
                ManagerBlockMinutes = 60,
            };
            Planner.PlanPersonDay(inputs, day, Schedules, Budgets, Diag);
        }

        private void DriveDomestic(int householdId, int[] memberIds, int mealsAtHome, int day)
        {
            var inputs = new DomesticLaborDriver.DomesticDayInputs
            {
                HouseholdId = householdId,
                MemberIds = new List<int>(memberIds),
                AgeBandFor = pid => pid == P3 ? AgeBand.Child0To9 : AgeBand.Adult18Plus,
                MealsAtHomeCount = mealsAtHome,
                ColdSeason = false,
                HasAnimals = householdId == H2,
            };
            DomesticDriver.DriveDay(inputs, day, Tasks, Budgets, Schedules, Domestic, DueEvents, Diag);
        }

        private sealed class WorkerSpec
        {
            public int PersonId;
            public int WorkStart;
            public int WorkEnd;
        }

        /// <summary>
        /// Free windows from the live schedule tracker, merged from 15-minute
        /// slots. Recomputed every pass so later assignments see earlier ones.
        /// </summary>
        private List<ManagerWorkAllocator.WorkWindow> ComputeFreeWindows(int personId, int day, int workStart, int workEnd)
        {
            var windows = new List<ManagerWorkAllocator.WorkWindow>();
            int runStart = -1;
            for (int m = workStart; m < workEnd; m += 15)
            {
                bool free = Schedules.IsFree(personId, day, m, Math.Min(15, workEnd - m));
                if (free && runStart < 0) runStart = m;
                if (!free && runStart >= 0)
                {
                    if (m - runStart >= 30)
                    {
                        windows.Add(new ManagerWorkAllocator.WorkWindow { StartMinuteOfDay = runStart, DurationMinutes = m - runStart });
                    }

                    runStart = -1;
                }
            }

            if (runStart >= 0 && workEnd - runStart >= 30)
            {
                windows.Add(new ManagerWorkAllocator.WorkWindow { StartMinuteOfDay = runStart, DurationMinutes = workEnd - runStart });
            }

            return windows;
        }

        private void AllocateWithFreshWindows(
            ManagerWorkAllocator allocator, string businessId, int managerId,
            WorkerSpec[] workers, int day)
        {
            for (int pass = 0; pass < 6; pass++)
            {
                var avail = new List<ManagerWorkAllocator.WorkerAvailability>();
                foreach (var w in workers)
                {
                    avail.Add(new ManagerWorkAllocator.WorkerAvailability
                    {
                        PersonId = w.PersonId,
                        BusinessInstanceId = businessId,
                        FreeWindows = ComputeFreeWindows(w.PersonId, day, w.WorkStart, w.WorkEnd),
                    });
                }

                var inputs = new ManagerWorkAllocator.AllocationInputs
                {
                    BusinessInstanceId = businessId,
                    BusinessOwner = BusinessOwnerEntity(businessId),
                    ManagerPersonId = managerId,
                    DelegatedScopes = new List<string> { ManagerScopeNames.Scheduling, ManagerScopeNames.Staffing },
                    Tasks = Tasks,
                    Budgets = Budgets,
                    Schedules = Schedules,
                    Reservations = Reservations,
                    Workers = avail,
                    DueEvents = DueEvents,
                    DayIndex = day,
                    NowGameSeconds = day * 86400f + 300f,
                    MaterialCheck = task => CheckMaterials(businessId, task),
                    EquipmentKeysFor = task => EquipmentKeysFor(task),
                    IsShortageDriven = task => string.Equals(task.DefinitionId, AutonomousWorkGenerator.GeneratedWorkCatalog.ThreshGrainId, StringComparison.Ordinal),
                    HiringPort = rec => Diag.Add("LivingWorldSim: hiring recommendation for " + businessId + ": " + rec.Reason),
                    // The manager's own time is reserved once per day, not per pass.
                    ManagerTimeCostMinutes = pass == 0 ? 60 : 0,
                };
                ManagerWorkAllocator.AllocationReport report = allocator.Allocate(inputs, Diag);
                foreach (string reason in report.DeferralReasons)
                {
                    if (reason.Contains(ReservationKeyConventions.Asset(ThresherKey)))
                    {
                        ThresherContentionDeferrals++;
                    }
                }

                if (report.AssignedTaskIds.Count == 0) break;
            }
        }

        private string CheckMaterials(string businessId, WorkTask task)
        {
            string def = task.DefinitionId;
            if (string.Equals(def, AutonomousWorkGenerator.GeneratedWorkCatalog.ProductionWorkOrderId, StringComparison.Ordinal) && businessId == Bakery)
            {
                return BizGoods(Bakery, Flour) >= 10 ? null : "no flour on hand";
            }

            if (string.Equals(def, AutonomousWorkGenerator.GeneratedWorkCatalog.ThreshGrainId, StringComparison.Ordinal) ||
                string.Equals(def, AutonomousWorkGenerator.GeneratedWorkCatalog.MillGrainId, StringComparison.Ordinal))
            {
                return BizGoods(businessId, Grain) >= 10 ? null : "no grain on hand";
            }

            if (string.Equals(def, AutonomousWorkGenerator.GeneratedWorkCatalog.MaintenanceWorkOrderId, StringComparison.Ordinal) && businessId == Farm1)
            {
                return MillBeltInstalled ? null : "no mill belt on hand — procure from General Store";
            }

            return null;
        }

        private List<string> EquipmentKeysFor(WorkTask task)
        {
            var keys = new List<string>();
            string def = task.DefinitionId;
            string biz = OwnerBusinessId(task.OwnerId);
            if (string.Equals(def, AutonomousWorkGenerator.GeneratedWorkCatalog.ProductionWorkOrderId, StringComparison.Ordinal) && biz == Bakery)
            {
                keys.Add(ReservationKeyConventions.Asset(OvenKey));
            }
            else if (string.Equals(def, AutonomousWorkGenerator.GeneratedWorkCatalog.ThreshGrainId, StringComparison.Ordinal))
            {
                keys.Add(ReservationKeyConventions.Asset(ThresherKey));
            }
            else if (string.Equals(def, AutonomousWorkGenerator.GeneratedWorkCatalog.MillGrainId, StringComparison.Ordinal))
            {
                keys.Add(ReservationKeyConventions.Asset(MillKey));
            }

            return keys;
        }

        /// <summary>
        /// Owner-operated farm-2: Otto assigns Robin directly (inherent owner
        /// authority — no delegation needed). Same real checks as the
        /// allocator: employment, free window, budget, materials, equipment.
        /// </summary>
        private void OwnerAssignFarm2(int day)
        {
            for (int pass = 0; pass < 4; pass++)
            {
                bool assignedAny = false;
                List<WorkTask> open = Tasks.GetOpenTasks(BusinessOwnerEntity(Farm2));
                open.Sort((a, b) => a.CreatedDayIndex.CompareTo(b.CreatedDayIndex));
                foreach (WorkTask task in open)
                {
                    if (task == null || task.Status != TaskStatus.Queued) continue;

                    string materialProblem = CheckMaterials(Farm2, task);
                    if (materialProblem != null)
                    {
                        Diag.Add($"LivingWorldSim: F2 task {task.TaskId} deferred — materials: {materialProblem}");
                        continue;
                    }

                    // Equipment first, per candidate window (thresher contention
                    // is the point: whoever holds the window keeps the machine).
                    List<string> keys = EquipmentKeysFor(task);
                    List<ManagerWorkAllocator.WorkWindow> windows = ComputeFreeWindows(P9, day, 540, 780);
                    bool placed = false;
                    string equipmentRefusal = null;
                    foreach (var window in windows)
                    {
                        if (window.DurationMinutes < task.PlannedMinutes) continue;

                        string reservationId = null;
                        if (keys.Count > 0)
                        {
                            var request = new ReservationRequest("task:" + task.TaskId, "owner:farm-2");
                            float start = day * 86400f + window.StartMinuteOfDay * 60f;
                            foreach (string key in keys)
                            {
                                request.Items.Add(new ReservationItem(key, 1f, start, start + task.PlannedMinutes * 60f));
                            }

                            ReservationHandle handle;
                            string refusal = Reservations.TryReserveAll(request, out handle);
                            if (refusal != null)
                            {
                                equipmentRefusal = refusal;
                                continue; // try the next window
                            }

                            reservationId = handle != null ? handle.ReservationId : string.Empty;
                        }

                        EntityId workerId = EntityId.For(EntityKind.Person, P9);
                        if (Budgets.GetOrCreate(workerId).MinutesRemaining < task.PlannedMinutes)
                        {
                            if (!string.IsNullOrEmpty(reservationId)) Reservations.Release(reservationId);
                            continue;
                        }

                        string rejection;
                        if (!Tasks.AssignTask(task.TaskId, workerId, Budgets, out rejection))
                        {
                            if (!string.IsNullOrEmpty(reservationId)) Reservations.Release(reservationId);
                            continue;
                        }

                        string windowRefusal = Schedules.TryReserve(P9, PersonActivityKind.Work,
                            day, window.StartMinuteOfDay, task.PlannedMinutes, $"owner:farm-2 {task.TaskId}");
                        if (windowRefusal != null)
                        {
                            string ignore;
                            Tasks.InterruptTask(task.TaskId, Budgets, out ignore);
                            if (!string.IsNullOrEmpty(reservationId)) Reservations.Release(reservationId);
                            continue;
                        }

                        string dueId;
                        DueEvents.Schedule(DueEventKind.ActivityWindowDue,
                            day * 86400f + window.StartMinuteOfDay * 60f,
                            task.TaskId.ToString(), out dueId);
                        PersonLocations[P9] = "field-2";
                        Diag.Add($"LivingWorldSim: F2 {task.TaskId} → P{P9} [{window.StartMinuteOfDay}-{window.StartMinuteOfDay + task.PlannedMinutes}]" +
                            (string.IsNullOrEmpty(reservationId) ? "" : " + equipment " + reservationId));
                        placed = true;
                        assignedAny = true;
                        break;
                    }

                    if (!placed)
                    {
                        if (equipmentRefusal != null &&
                            equipmentRefusal.Contains(ReservationKeyConventions.Asset(ThresherKey)))
                        {
                            ThresherContentionDeferrals++;
                        }

                        Diag.Add($"LivingWorldSim: F2 task {task.TaskId} deferred — " +
                            (equipmentRefusal != null ? "equipment: " + equipmentRefusal : "no free window for P" + P9 + "."));
                    }
                }

                if (!assignedAny) break;
            }
        }
    }
}

namespace LandLedgers.Editor.Integration
{
    public sealed partial class LivingWorldSim
    {
        private void GenerateForBusinesses(int day)
        {

            // --- bakery: bake while daily policy unmet and stock below cap ---
            var bakeryInputs = new AutonomousWorkGenerator.BusinessGenerationInputs
            {
                BusinessInstanceId = Bakery,
                BusinessOwner = BusinessOwnerEntity(Bakery),
                DayIndex = day,
                StockUnits = item => BizGoods(Bakery, item),
                HasOpenTask = (def, cref) => HasOpenTask(BusinessOwnerEntity(Bakery), def, cref),
                ProductionPolicy = new AutonomousWorkGenerator.ManagerProductionPolicy
                {
                    ItemId = Bread,
                    TargetUnitsPerDay = 30,
                    ActualUnitsPerDay = breadProducedToday >= 30 || BizGoods(Bakery, Bread) >= 90 ? 999 : breadProducedToday,
                },
                CheckFeasibility = policy => new AutonomousWorkGenerator.WorkFeasibility
                {
                    Feasible = BizGoods(Bakery, Flour) >= 10,
                    Reason = BizGoods(Bakery, Flour) >= 10 ? "flour on hand, baker free" : "no flour on hand",
                    AvailableWorkerMinutes = 480,
                    MaterialsAvailable = BizGoods(Bakery, Flour) >= 10,
                },
            };
            Generator.GenerateForBusiness(bakeryInputs, Tasks, Diag);

            // --- farm-1: field work in the seasonal window; mill repair when broken ---
            var farm1Inputs = new AutonomousWorkGenerator.BusinessGenerationInputs
            {
                BusinessInstanceId = Farm1,
                BusinessOwner = BusinessOwnerEntity(Farm1),
                DayIndex = day,
                StockUnits = item => BizGoods(Farm1, item),
                HasOpenTask = (def, cref) => HasOpenTask(BusinessOwnerEntity(Farm1), def, cref),
                FarmWindow = new AutonomousWorkGenerator.FarmWorkWindow
                {
                    StartDayIndex = StartDay + 1,
                    EndDayIndex = StartDay + 12,
                    WorkKind = "spring field work",
                    FieldLocationId = "field-1",
                    WeatherOk = true,
                },
            };
            if (MillFailed)
            {
                farm1Inputs.Equipment.Add(new AutonomousWorkGenerator.EquipmentCondition
                {
                    EquipmentId = MillKey,
                    OwnerBusinessId = Farm1,
                    Condition01 = MillCondition01,
                    Failed = true,
                    LocationId = "mill",
                });
            }

            Generator.GenerateForBusiness(farm1Inputs, Tasks, Diag);

            // --- farm-2: field work in the seasonal window ---
            var farm2Inputs = new AutonomousWorkGenerator.BusinessGenerationInputs
            {
                BusinessInstanceId = Farm2,
                BusinessOwner = BusinessOwnerEntity(Farm2),
                DayIndex = day,
                StockUnits = item => BizGoods(Farm2, item),
                HasOpenTask = (def, cref) => HasOpenTask(BusinessOwnerEntity(Farm2), def, cref),
                FarmWindow = new AutonomousWorkGenerator.FarmWorkWindow
                {
                    StartDayIndex = StartDay + 1,
                    EndDayIndex = StartDay + 12,
                    WorkKind = "spring field work",
                    FieldLocationId = "field-2",
                    WeatherOk = true,
                },
            };
            Generator.GenerateForBusiness(farm2Inputs, Tasks, Diag);

            // --- store: shelf below target + backroom stock → restock task for the clerk ---
            var storeInputs = new AutonomousWorkGenerator.BusinessGenerationInputs
            {
                BusinessInstanceId = Store,
                BusinessOwner = BusinessOwnerEntity(Store),
                DayIndex = day,
                StockUnits = item =>
                {
                    int v;
                    Store1.Backroom.TryGetValue(item, out v);
                    return v;
                },
                HasOpenTask = (def, cref) => HasOpenTask(BusinessOwnerEntity(Store), def, cref),
            };
            storeInputs.Shelves.Add(new AutonomousWorkGenerator.ShelfLine
            {
                ItemId = Bread,
                ShelfUnits = Store1.ItemStockUnits(Bread),
                TargetUnits = StoreShelfTargetBread,
            });
            Generator.GenerateForBusiness(storeInputs, Tasks, Diag);
        }

        private bool HasOpenTask(EntityId owner, string definitionId, string customerRef)
        {
            foreach (WorkTask t in Tasks.GetOpenTasks(owner))
            {
                if (t == null) continue;
                if (!string.Equals(t.DefinitionId, definitionId, StringComparison.Ordinal)) continue;
                if (!string.IsNullOrEmpty(customerRef) &&
                    !string.Equals(t.CustomerRef, customerRef, StringComparison.Ordinal)) continue;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Farm processing: mill grain at the farm mill, or thresh at the
        /// SHARED thresher when the mill is down. Real need only: grain ≥ 10.
        /// </summary>
        private void CreateFarmProcessingTasks(int day)
        {

            // Farm-1: mill while it works; shared thresher while it doesn't.
            if (BizGoods(Farm1, Grain) >= 10)
            {
                if (!MillFailed && !HasOpenTask(BusinessOwnerEntity(Farm1), AutonomousWorkGenerator.GeneratedWorkCatalog.MillGrainId, null))
                {
                    CreateSimTask(AutonomousWorkGenerator.GeneratedWorkCatalog.MillGrainId, BusinessOwnerEntity(Farm1), day, 150, "mill:" + day);
                }
                else if (MillFailed && !HasOpenTask(BusinessOwnerEntity(Farm1), AutonomousWorkGenerator.GeneratedWorkCatalog.ThreshGrainId, null))
                {
                    CreateSimTask(AutonomousWorkGenerator.GeneratedWorkCatalog.ThreshGrainId, BusinessOwnerEntity(Farm1), day, 180, "thresh:" + day);
                }
            }

            // Farm-2: thresh at the shared thresher when it has grain.
            if (BizGoods(Farm2, Grain) >= 10 &&
                !HasOpenTask(BusinessOwnerEntity(Farm2), AutonomousWorkGenerator.GeneratedWorkCatalog.ThreshGrainId, null))
            {
                CreateSimTask(AutonomousWorkGenerator.GeneratedWorkCatalog.ThreshGrainId, BusinessOwnerEntity(Farm2), day, 180, "thresh:" + day);
            }
        }

        private void CreateSimTask(string definitionId, EntityId owner, int day, int plannedMinutes, string customerRef)
        {
            WorkTask task = Tasks.CreateTaskWithPlannedMinutes(definitionId, owner, day, plannedMinutes, customerRef: customerRef);
            if (task != null)
            {
                Diag.Add($"LivingWorldSim: day {day} — created {task.TaskId} ('{definitionId}') for {OwnerBusinessId(owner)}.");
            }
        }

        private void GenerateForHouseholds(int day)
        {
            foreach (int h in new[] { H1, H2, H3, H4, H6 })
            {
                int householdId = h;
                var inputs = new AutonomousWorkGenerator.HouseholdGenerationInputs
                {
                    HouseholdId = householdId,
                    HouseholdOwner = EntityId.For(EntityKind.Household, householdId),
                    Needs = Needs,
                    DayIndex = day,
                    LowDaysThreshold = 7f,
                    DaysOfSupplyByItem = new Dictionary<string, float>
                    {
                        { Bread, DaysOfSupply(householdId, Bread, DailyBreadUse(householdId)) },
                    },
                };
                AutonomousWorkGenerator.GenerationReport report = Generator.GenerateForHousehold(inputs, Diag);
                foreach (string needKey in report.CreatedNeedKeys)
                {
                    HouseholdPurchasingNeed need = Needs.GetOpenNeed(householdId, Bread);
                    if (need != null)
                    {
                        // Buy ~4 days of supply: bulk enough to be worth the
                        // trip, small enough to eat before it spoils.
                        need.UnitsNeeded = Math.Max(2, DailyBreadUse(householdId) * 4);
                        need.IsNecessity = true;
                    }
                }
            }
        }

        private int DailyBreadUse(int householdId)
        {
            switch (householdId)
            {
                case H1: return 5;
                case H2: return 4;
                case H3: return 2;
                case H4: return 2;
                case H6: return 2;
                default: return 2;
            }
        }

        private float DaysOfSupply(int householdId, string itemId, int dailyUse)
        {
            if (dailyUse <= 0) return 999f;
            return (float)Inventories.GetOrCreate(householdId).GetTotalUnits(itemId) / dailyUse;
        }

        /// <summary>The manager's wholesale round: real cash, real goods, real counterparties.</summary>
        private void RunProcurement(int day)
        {
            // 1. Mill belt for the broken mill (once).
            if (day == StartDay + 5 && !MillBeltInstalled && Store1.ItemStockUnits(MillBelt) > 0)
            {
                string problem = TransferBusinessCash(Bakery, Store, 800, day, "mill belt procurement");
                if (problem == null)
                {
                    Store1.Stock[MillBelt] = 0;
                    MillBeltInstalled = true;
                    GoodsFlows.Add(new GoodsFlow { DayIndex = day, Owner = Bakery, ItemId = MillBelt, DeltaUnits = 1, Reason = "procured from General Store" });
                    GoodsFlows.Add(new GoodsFlow { DayIndex = day, Owner = Store, ItemId = MillBelt, DeltaUnits = -1, Reason = "sold to bakery-1" });
                    Diag.Add($"LivingWorldSim: day {day} — bakery procured a mill belt from the General Store (800c).");
                }
                else
                {
                    Diag.Add($"LivingWorldSim: day {day} — mill belt procurement failed: {problem}");
                }
            }

            // 2. Flour for the bakery (farm-1 first, then farm-2).
            if (BizGoods(Bakery, Flour) < 20)
            {
                BuyFlour(Bakery, Farm1, 20, 60, day);
                if (BizGoods(Bakery, Flour) < 20)
                {
                    BuyFlour(Bakery, Farm2, 20, 60, day);
                }
            }

            // 3. Bread for the store's shelves (kept in the backroom; the clerk shelves it).
            int brNow;
            Store1.Backroom.TryGetValue(Bread, out brNow);
            if (Store1.ItemStockUnits(Bread) + brNow < 60 && BizGoods(Bakery, Bread) >= 60)
            {
                string problem = TransferBusinessCash(Store, Bakery, 60 * 80, day, "wholesale bread");
                if (problem == null)
                {
                    MoveBizGoods(Bakery, Bread, -60, day, "wholesale to General Store");
                    int br0;
                    Store1.Backroom.TryGetValue(Bread, out br0);
                    Store1.Backroom[Bread] = br0 + 60;
                    GoodsFlows.Add(new GoodsFlow { DayIndex = day, Owner = Store, ItemId = Bread, DeltaUnits = 60, Reason = "wholesale from bakery-1 (backroom)" });
                    Diag.Add($"LivingWorldSim: day {day} — General Store bought 60 bread wholesale (4800c) into the backroom.");
                }
            }

            // 4. Farm-2's one-off grain purchase (day 3).
            if (day == StartDay + 3 && BizGoods(Farm2, Grain) < 10 && BizGoods(Farm1, Grain) >= 20)
            {
                string problem = TransferBusinessCash(Farm2, Farm1, 600, day, "grain purchase");
                if (problem == null)
                {
                    MoveBizGoods(Farm1, Grain, -20, day, "sold to farm-2");
                    MoveBizGoods(Farm2, Grain, 20, day, "bought from farm-1");
                    Diag.Add($"LivingWorldSim: day {day} — farm-2 bought 20 grain from farm-1 (600c).");
                }
            }
        }

        private void BuyFlour(string buyer, string seller, int wantUnits, int pricePerUnit, int day)
        {
            int have = BizGoods(seller, Flour);
            int units = Math.Min(wantUnits, have);
            if (units <= 0) return;
            int cost = units * pricePerUnit;
            int cash = BusinessCash.TryGetValue(buyer, out int c) ? c : 0;
            if (cash < cost) return;
            string problem = TransferBusinessCash(buyer, seller, cost, day, "wholesale flour");
            if (problem != null) return;
            MoveBizGoods(seller, Flour, -units, day, "wholesale to " + buyer);
            MoveBizGoods(buyer, Flour, units, day, "wholesale from " + seller);
        }

        private void RunEveningMeals(int day)
        {
            Diag.Add($"LivingWorldSim: day {day} — evening meals.");
            foreach (int h in new[] { H1, H2, H3, H4, H6 })
            {
                int need = DailyBreadUse(h);
                var records = new List<HouseholdConsumptionRecord>();
                string rejection = Inventories.GetOrCreate(h).Consume(Bread, need, day, "evening meal", "H" + h, records);
                if (rejection == null)
                {
                    foreach (var r in records)
                    {
                        GoodsFlows.Add(new GoodsFlow
                        {
                            DayIndex = day, Owner = "H" + h, ItemId = Bread,
                            DeltaUnits = -r.QuantityUnits, Reason = "evening meal",
                        });
                    }
                }
                else
                {
                    // Fall back to preserved food, else a missed meal.
                    int members = Membership.GetActiveMembers(h).Count;
                    var fallback = new List<HouseholdConsumptionRecord>();
                    string fallbackRejection = Inventories.GetOrCreate(h).Consume(
                        PreservedFood, members, day, "evening meal (fallback)", "H" + h, fallback);
                    if (fallbackRejection == null)
                    {
                        foreach (var r in fallback)
                        {
                            GoodsFlows.Add(new GoodsFlow
                            {
                                DayIndex = day, Owner = "H" + h, ItemId = PreservedFood,
                                DeltaUnits = -r.QuantityUnits, Reason = "evening meal (fallback)",
                            });
                        }

                        Diag.Add($"LivingWorldSim: day {day} — H{h} ate preserved food (no bread).");
                    }
                    else
                    {
                        MissedMeals += members;
                        int mm;
                        MissedMealsByHousehold.TryGetValue(h, out mm);
                        MissedMealsByHousehold[h] = mm + members;
                        UnmetBreadNeeds++;
                        Diag.Add($"LivingWorldSim: day {day} — H{h} MISSED MEAL ({members} members, no bread: {rejection})");
                    }
                }
            }

            // Spoilage: bread lots older than 5 days are waste, honestly recorded.
            foreach (int h in new[] { H1, H2, H3, H4, H6 })
            {
                HouseholdInventory inv = Inventories.GetOrCreate(h);
                int spoiledUnits = 0;
                foreach (HouseholdItemLot lot in inv.Lots)
                {
                    if (lot != null && string.Equals(lot.ItemId, Bread, StringComparison.Ordinal) &&
                        lot.AcquiredDayIndex <= day - 5)
                    {
                        spoiledUnits += lot.AvailableUnits;
                    }
                }

                if (spoiledUnits > 0)
                {
                    var records = new List<HouseholdConsumptionRecord>();
                    string rejection = inv.Consume(Bread, spoiledUnits, day, "spoilage", "sim", records);
                    if (rejection == null)
                    {
                        WastedUnits += spoiledUnits;
                        GoodsFlows.Add(new GoodsFlow
                        {
                            DayIndex = day, Owner = "H" + h, ItemId = Bread,
                            DeltaUnits = -spoiledUnits, Reason = "spoilage",
                        });
                        Diag.Add($"LivingWorldSim: day {day} — H{h} wasted {spoiledUnits}u bread (spoilage).");
                    }
                }
            }
        }

        /// <summary>
        /// Weekly payroll: business cash → household ledgers via the ledger's
        /// wage path (employment id as provenance). Idempotent per week — a
        /// week is never posted twice, including across save/load. Shortfalls
        /// become arrears, never fake money.
        /// </summary>
        private void RunPayroll(int weekIndex, int day)
        {
            if (!SettledPayrollWeeks.Add(weekIndex))
            {
                Diag.Add($"LivingWorldSim: payroll week {weekIndex} already settled — skipped (idempotency).");
                return;
            }

            Diag.Add($"LivingWorldSim: day {day} — settling payroll week {weekIndex}.");

            // Old-age pension for the retiree household: a real, documented
            // income source (not manufactured — it represents the world's
            // pension system, recorded with source and reason like any inflow).
            {
                string pensionRejection = Ledgers.GetOrCreate(H3).RecordInflow(
                    day, 800, HouseholdIncomeSource.OtherDocumented,
                    "pension", "old-age pension week " + weekIndex, "pension fund");
                CashTransfers.Add(new CashTransfer
                {
                    DayIndex = day, From = "pension fund", To = "H" + H3,
                    AmountCents = 800, Reason = "old-age pension week " + weekIndex,
                });
                if (pensionRejection != null)
                {
                    Diag.Add("LivingWorldSim: pension rejected: " + pensionRejection);
                }
            }

            foreach (Employment emp in Employments)
            {
                int cash = BusinessCash.TryGetValue(emp.BusinessId, out int c) ? c : 0;
                if (cash >= emp.WeeklyWageCents)
                {
                    BusinessCash[emp.BusinessId] = cash - emp.WeeklyWageCents;
                    string rejection = Ledgers.GetOrCreate(emp.HouseholdId).RecordWagePayment(
                        day, emp.Id, emp.PersonId, emp.WeeklyWageCents, "week " + weekIndex);
                    CashTransfers.Add(new CashTransfer
                    {
                        DayIndex = day, From = emp.BusinessId, To = "H" + emp.HouseholdId,
                        AmountCents = emp.WeeklyWageCents, Reason = "wages week " + weekIndex,
                    });
                    if (rejection != null)
                    {
                        Diag.Add("LivingWorldSim: wage payment rejected: " + rejection);
                    }
                }
                else
                {
                    PayrollArrears.Add($"week {weekIndex}: {emp.Id} unpaid ({emp.WeeklyWageCents}c owed, {emp.BusinessId} holds {cash}c)");
                    Diag.Add($"LivingWorldSim: payroll arrears — {emp.Id} unpaid.");
                }
            }
        }
    }
}

namespace LandLedgers.Editor.Integration
{
    /// <summary>Full save snapshot of the living-world sim (test scaffold).</summary>
    [Serializable]
    public sealed class LivingWorldSimSaveDto
    {
        public SimpleDueEventSchedulerSaveDto Scheduler;
        public TaskAuthoritySaveDto Tasks;
        public List<EntityIdCursor> TaskIdCursors;
        public WorkTimeBudgetSaveDto Budgets;
        public int BudgetDay;
        public PersonScheduleTrackerSaveDto Schedules;
        public List<HouseholdLedgerState> Ledgers = new List<HouseholdLedgerState>();
        public List<HouseholdInventoryState> Inventories = new List<HouseholdInventoryState>();
        public List<HouseholdPurchasingNeed> Needs = new List<HouseholdPurchasingNeed>();
        public List<PlannedShoppingTripSaveDto> ShoppingTrips = new List<PlannedShoppingTripSaveDto>();
        public DomesticLaborDriverSaveDto DomesticDriver;
        public ManagerWorkAllocator.HiringPressureTracker BakeryPressure;
        public ManagerWorkAllocator.HiringPressureTracker Farm1Pressure;
        public ManagerWorkAllocator.HiringPressureTracker StorePressure;
        public Dictionary<string, int> StoreBackroom = new Dictionary<string, int>();
        // sim scalars
        public float NowSeconds;
        public int CurrentDay;
        public Dictionary<string, int> BusinessCash = new Dictionary<string, int>();
        public Dictionary<string, int> BusinessGoods = new Dictionary<string, int>();
        public Dictionary<string, int> StoreStock = new Dictionary<string, int>();
        public Dictionary<string, int> StorePrices = new Dictionary<string, int>();
        public int StoreCashReceived;
        public Dictionary<string, int> StoreUnitsSold = new Dictionary<string, int>();
        public List<LivingWorldSim.GoodsFlow> GoodsFlows = new List<LivingWorldSim.GoodsFlow>();
        public List<LivingWorldSim.CashTransfer> CashTransfers = new List<LivingWorldSim.CashTransfer>();
        public Dictionary<string, int> CompletedTasksByDefinition = new Dictionary<string, int>();
        public Dictionary<string, int> FailedTasksByDefinition = new Dictionary<string, int>();
        public int TotalWorkMinutes;
        public int ThresherOccupiedMinutes;
        public int OvenOccupiedMinutes;
        public int MillOccupiedMinutes;
        public int MissedMeals;
        public int WastedUnits;
        public int ThresherContentionDeferrals;
        public int BakeRefusalsForFlour;
        public int EmploymentConflictRefusals;
        public int UnmetBreadNeeds;
        public float MillCondition01;
        public bool MillFailed;
        public bool MillBeltInstalled;
        public List<int> SettledPayrollWeeks = new List<int>();
        public List<string> PayrollArrears = new List<string>();
        public Dictionary<int, string> PersonLocations = new Dictionary<int, string>();
        public int BreadProducedToday;
        public Dictionary<string, int> InitialStocks = new Dictionary<string, int>();
    }

    public sealed partial class LivingWorldSim
    {
        /// <summary>Captures the full save snapshot (clear-then-load, idempotent).</summary>
        public LivingWorldSimSaveDto CaptureSaveDto()
        {
            var dto = new LivingWorldSimSaveDto
            {
                Scheduler = DueEvents.CaptureSaveDto(),
                Tasks = Tasks.CaptureSaveDto(),
                Budgets = Budgets.CaptureSaveDto(),
                BudgetDay = Budgets.CurrentAbsoluteDayIndex,
                Schedules = Schedules.CaptureSaveDto(),
                DomesticDriver = DomesticDriver.CaptureSaveDto(),
                BakeryPressure = BakeryAllocator.CapturePressureDto(),
                Farm1Pressure = Farm1Allocator.CapturePressureDto(),
                StorePressure = StoreAllocator.CapturePressureDto(),
                StoreBackroom = new Dictionary<string, int>(Store1.Backroom),
                ShoppingTrips = ShoppingDriver.CaptureSaveDto(),
                NowSeconds = NowSeconds,
                CurrentDay = CurrentDay,
                BusinessCash = new Dictionary<string, int>(BusinessCash),
                BusinessGoods = new Dictionary<string, int>(BusinessGoods),
                StoreStock = new Dictionary<string, int>(Store1.Stock),
                StorePrices = new Dictionary<string, int>(Store1.Prices),
                StoreCashReceived = Store1.CashReceived,
                StoreUnitsSold = new Dictionary<string, int>(Store1.UnitsSold),
                GoodsFlows = new List<GoodsFlow>(GoodsFlows),
                CashTransfers = new List<CashTransfer>(CashTransfers),
                CompletedTasksByDefinition = new Dictionary<string, int>(CompletedTasksByDefinition),
                FailedTasksByDefinition = new Dictionary<string, int>(FailedTasksByDefinition),
                TotalWorkMinutes = TotalWorkMinutes,
                ThresherOccupiedMinutes = ThresherOccupiedMinutes,
                OvenOccupiedMinutes = OvenOccupiedMinutes,
                MillOccupiedMinutes = MillOccupiedMinutes,
                MissedMeals = MissedMeals,
                WastedUnits = WastedUnits,
                ThresherContentionDeferrals = ThresherContentionDeferrals,
                BakeRefusalsForFlour = BakeRefusalsForFlour,
                EmploymentConflictRefusals = EmploymentConflictRefusals,
                UnmetBreadNeeds = UnmetBreadNeeds,
                MillCondition01 = MillCondition01,
                MillFailed = MillFailed,
                MillBeltInstalled = MillBeltInstalled,
                SettledPayrollWeeks = new List<int>(SettledPayrollWeeks),
                PayrollArrears = new List<string>(PayrollArrears),
                PersonLocations = new Dictionary<int, string>(PersonLocations),
                BreadProducedToday = breadProducedToday,
                InitialStocks = new Dictionary<string, int>(initialStocks),
            };
            dto.TaskIdCursors = SnapshotTaskIdCursors();
            Ledgers.ExportState(dto.Ledgers);
            Inventories.ExportState(dto.Inventories);
            Needs.ExportState(dto.Needs);
            return dto;
        }

        /// <summary>
        /// Clear-then-load restore. Call after BuildStatic() on a fresh sim
        /// (registries empty, so ImportState is a clean load). Idempotent:
        /// loading the same DTO twice yields the same state.
        /// </summary>
        public void LoadFromSaveDto(LivingWorldSimSaveDto dto)
        {
            if (dto == null) return;

            DueEvents.LoadFromSaveDto(dto.Scheduler);
            Tasks.LoadFromSaveDto(dto.Tasks);
            RestoreTaskIdCursors(dto.TaskIdCursors);
            Budgets.LoadFromSaveDto(dto.Budgets, dto.BudgetDay);
            Schedules.LoadFromSaveDto(dto.Schedules);
            Ledgers.ImportState(dto.Ledgers);
            Inventories.ImportState(dto.Inventories);
            Needs.ImportState(dto.Needs);
            ShoppingDriver.LoadFromSaveDto(dto.ShoppingTrips);
            ShoppingDriver.RehydrateAfterLoad(Tasks, id => id == Store ? (IGoodsSupplier)Store1 : null,
                dto.CurrentDay, dto.NowSeconds, DueEvents, Diag);
            DomesticDriver.LoadFromSaveDto(dto.DomesticDriver);
            BakeryAllocator.LoadPressureDto(dto.BakeryPressure);
            Farm1Allocator.LoadPressureDto(dto.Farm1Pressure);
            StoreAllocator.LoadPressureDto(dto.StorePressure);
            Store1.Backroom.Clear();
            foreach (var kv in dto.StoreBackroom) Store1.Backroom[kv.Key] = kv.Value;

            NowSeconds = dto.NowSeconds;
            CurrentDay = dto.CurrentDay;
            BusinessCash.Clear();
            foreach (var kv in dto.BusinessCash) BusinessCash[kv.Key] = kv.Value;
            BusinessGoods.Clear();
            foreach (var kv in dto.BusinessGoods) BusinessGoods[kv.Key] = kv.Value;
            Store1.Stock = new Dictionary<string, int>(dto.StoreStock);
            Store1.Prices = new Dictionary<string, int>(dto.StorePrices);
            Store1.CashReceived = dto.StoreCashReceived;
            Store1.UnitsSold = new Dictionary<string, int>(dto.StoreUnitsSold);
            GoodsFlows.Clear();
            GoodsFlows.AddRange(dto.GoodsFlows);
            CashTransfers.Clear();
            CashTransfers.AddRange(dto.CashTransfers);
            CompletedTasksByDefinition.Clear();
            foreach (var kv in dto.CompletedTasksByDefinition) CompletedTasksByDefinition[kv.Key] = kv.Value;
            FailedTasksByDefinition.Clear();
            foreach (var kv in dto.FailedTasksByDefinition) FailedTasksByDefinition[kv.Key] = kv.Value;
            TotalWorkMinutes = dto.TotalWorkMinutes;
            ThresherOccupiedMinutes = dto.ThresherOccupiedMinutes;
            OvenOccupiedMinutes = dto.OvenOccupiedMinutes;
            MillOccupiedMinutes = dto.MillOccupiedMinutes;
            MissedMeals = dto.MissedMeals;
            WastedUnits = dto.WastedUnits;
            ThresherContentionDeferrals = dto.ThresherContentionDeferrals;
            BakeRefusalsForFlour = dto.BakeRefusalsForFlour;
            EmploymentConflictRefusals = dto.EmploymentConflictRefusals;
            UnmetBreadNeeds = dto.UnmetBreadNeeds;
            MillCondition01 = dto.MillCondition01;
            MillFailed = dto.MillFailed;
            MillBeltInstalled = dto.MillBeltInstalled;
            SettledPayrollWeeks.Clear();
            foreach (int w in dto.SettledPayrollWeeks) SettledPayrollWeeks.Add(w);
            PayrollArrears.Clear();
            PayrollArrears.AddRange(dto.PayrollArrears);
            PersonLocations.Clear();
            foreach (var kv in dto.PersonLocations) PersonLocations[kv.Key] = kv.Value;
            breadProducedToday = dto.BreadProducedToday;
            initialStocks.Clear();
            foreach (var kv in dto.InitialStocks) initialStocks[kv.Key] = kv.Value;

            if (loopOptions == null) BuildLoopOptions();
            Diag.Add($"LivingWorldSim: restored save at day {CurrentDay}, t={NowSeconds}s.");
        }

        private List<EntityIdCursor> SnapshotTaskIdCursors()
        {
            FieldInfo field = typeof(TaskAuthority).GetField("idRegistry",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var registry = (EntityIdRegistry)field.GetValue(Tasks);
            return registry.SnapshotCursors();
        }

        private void RestoreTaskIdCursors(List<EntityIdCursor> cursors)
        {
            if (cursors == null) return;
            FieldInfo field = typeof(TaskAuthority).GetField("idRegistry",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var registry = (EntityIdRegistry)field.GetValue(Tasks);
            registry.RestoreCursors(cursors);
        }

        // ---------- reconciliation ----------

        /// <summary>Deterministic hash of every consequential outcome.</summary>
        public string ComputeHash()
        {
            var parts = new List<string>
            {
                "day=" + CurrentDay,
                "cash=" + TotalCashCents(),
                "workMin=" + TotalWorkMinutes,
                "thresherMin=" + ThresherOccupiedMinutes,
                "ovenMin=" + OvenOccupiedMinutes,
                "millMin=" + MillOccupiedMinutes,
                "missedMeals=" + MissedMeals,
                "wasted=" + WastedUnits,
                "thresherDefer=" + ThresherContentionDeferrals,
                "bakeRefuse=" + BakeRefusalsForFlour,
                "conflictRefuse=" + EmploymentConflictRefusals,
                "unmetBread=" + UnmetBreadNeeds,
                "arrears=" + PayrollArrears.Count,
                "goods=" + GoodsHash(),
                "tasks=" + TasksHash(),
            };
            return string.Join("|", parts);
        }

        private int TotalCashCents()
        {
            int total = 0;
            foreach (int h in new[] { H1, H2, H3, H4, H5, H6 })
            {
                total += Ledgers.GetOrCreate(h).GetBalanceCents();
            }

            foreach (var kv in BusinessCash) total += kv.Value;
            total += Store1.CashReceived;
            return total;
        }

        private string GoodsHash()
        {
            var sums = new Dictionary<string, int>();
            foreach (GoodsFlow f in GoodsFlows)
            {
                int v;
                sums.TryGetValue(f.ItemId, out v);
                sums[f.ItemId] = v + f.DeltaUnits;
            }

            var keys = new List<string>(sums.Keys);
            keys.Sort(StringComparer.Ordinal);
            var parts = new List<string>();
            foreach (string k in keys) parts.Add(k + ":" + sums[k]);
            return string.Join(",", parts);
        }

        private string TasksHash()
        {
            var parts = new List<string>();
            var keys = new List<string>(CompletedTasksByDefinition.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (string k in keys) parts.Add(k + "=" + CompletedTasksByDefinition[k]);
            var fkeys = new List<string>(FailedTasksByDefinition.Keys);
            fkeys.Sort(StringComparer.Ordinal);
            foreach (string k in fkeys) parts.Add(k + "!f=" + FailedTasksByDefinition[k]);
            return string.Join(",", parts);
        }

        /// <summary>
        /// Full reconciliation: cash conserved, goods balanced per item,
        /// retail cross-check. Returns human-readable findings; empty = clean.
        /// </summary>
        public List<string> Reconcile()
        {
            var findings = new List<string>();

            // 1. Cash conservation: every cent is somewhere. (The old-age
            // pension is a documented external inflow, counted in the total.)
            int totalCash = TotalCashCents();
            int seededCash = 36000 + 110000 + 800 * SettledPayrollWeeks.Count;
            if (totalCash != seededCash)
            {
                findings.Add($"CASH: total {totalCash}c != expected {seededCash}c — money appeared or vanished.");
            }

            // 2. Goods balance per item: initial + produced = consumed + wasted + final.
            // (Transfers net to zero across owners; household retail purchases
            // are cross-checked against the store's real sales log below.)
            foreach (string item in new[] { Bread, Flour, Grain, PreservedFood })
            {
                int initial = InitialStock(item);
                int produced = 0, consumed = 0, wasted = 0, converted = 0;
                foreach (GoodsFlow f in GoodsFlows)
                {
                    if (!string.Equals(f.ItemId, item, StringComparison.Ordinal)) continue;
                    if (f.DeltaUnits > 0 && IsProductionReason(f.Reason)) produced += f.DeltaUnits;
                    if (f.DeltaUnits < 0 && IsProductionReason(f.Reason)) converted -= f.DeltaUnits;
                    if (f.Reason == "evening meal" || f.Reason == "evening meal (fallback)") consumed -= f.DeltaUnits;
                    if (f.Reason == "spoilage") wasted -= f.DeltaUnits;
                }

                int final = FinalStock(item);
                int balance = initial + produced - consumed - wasted - converted - final;
                if (balance != 0)
                {
                    findings.Add($"GOODS {item}: initial {initial} + produced {produced} - consumed {consumed} - wasted {wasted} - converted {converted} - final {final} = {balance} (want 0).");
                }

                // Retail cross-check: implied household purchases == store's sales log.
                int impliedPurchases = 0;
                foreach (int h in new[] { H1, H2, H3, H4, H6 })
                {
                    impliedPurchases += FinalStockOf("H" + h, item) - InitialStockOf("H" + h, item);
                }

                foreach (GoodsFlow f in GoodsFlows)
                {
                    if (!string.Equals(f.ItemId, item, StringComparison.Ordinal)) continue;
                    if (!f.Owner.StartsWith("H", StringComparison.Ordinal)) continue;
                    if (f.Reason == "evening meal" || f.Reason == "evening meal (fallback)" || f.Reason == "spoilage")
                    {
                        impliedPurchases -= f.DeltaUnits; // deltas are negative
                    }
                }

                int retailSold = Store1.UnitsSold.TryGetValue(item, out int s) ? s : 0;
                if (impliedPurchases != retailSold)
                {
                    findings.Add($"RETAIL {item}: implied household purchases {impliedPurchases} != store sales log {retailSold}.");
                }
            }

            // 3. Work-time: completed business tasks' minutes are all recorded.
            int completedMinutes = 0;
            foreach (var kv in CompletedTasksByDefinition)
            {
                // Planned minutes per definition are fixed in the sim.
                completedMinutes += kv.Value * PlannedMinutesFor(kv.Key);
            }

            if (completedMinutes != TotalWorkMinutes)
            {
                findings.Add($"WORK: completed-task minutes {completedMinutes} != recorded {TotalWorkMinutes}.");
            }

            return findings;
        }

        private static bool IsProductionReason(string reason)
        {
            return reason == "bake batch" || reason == "bake batch (poor oven)" ||
                reason == "threshing" || reason == "milling" || reason == "field work";
        }

        private int PlannedMinutesFor(string definitionId)
        {
            if (definitionId == AutonomousWorkGenerator.GeneratedWorkCatalog.ProductionWorkOrderId) return 120;
            if (definitionId == AutonomousWorkGenerator.GeneratedWorkCatalog.FieldWorkId) return 240;
            if (definitionId == AutonomousWorkGenerator.GeneratedWorkCatalog.ThreshGrainId) return 180;
            if (definitionId == AutonomousWorkGenerator.GeneratedWorkCatalog.MillGrainId) return 150;
            if (definitionId == AutonomousWorkGenerator.GeneratedWorkCatalog.MaintenanceWorkOrderId) return 90;
            if (definitionId == AutonomousWorkGenerator.GeneratedWorkCatalog.GoodsHandlingId) return 30;
            if (definitionId == AutonomousWorkGenerator.GeneratedWorkCatalog.RestockShelvesId) return 45;
            return 0;
        }

        private readonly Dictionary<string, int> initialStocks = new Dictionary<string, int>();

        /// <summary>Call after SeedDynamic: snapshots every stock for reconciliation.</summary>
        public void SnapshotInitialStocks()
        {
            initialStocks.Clear();
            foreach (string item in new[] { Bread, Flour, Grain, PreservedFood, MillBelt })
            {
                foreach (int h in new[] { H1, H2, H3, H4, H5, H6 })
                {
                    initialStocks["H" + h + "|" + item] = Inventories.GetOrCreate(h).GetTotalUnits(item);
                }

                foreach (var kv in BusinessGoods)
                {
                    if (kv.Key.EndsWith("|" + item, StringComparison.Ordinal))
                    {
                        initialStocks[kv.Key] = kv.Value;
                    }
                }

                if (Store1.Stock.TryGetValue(item, out int ss))
                {
                    initialStocks[Store + "|" + item] = ss;
                }
            }
        }

        private int InitialStockOf(string owner, string item)
        {
            int v;
            return initialStocks.TryGetValue(owner + "|" + item, out v) ? v : 0;
        }

        private int InitialStock(string item)
        {
            int total = 0;
            foreach (var kv in initialStocks)
            {
                if (kv.Key.EndsWith("|" + item, StringComparison.Ordinal)) total += kv.Value;
            }

            return total;
        }

        private int FinalStockOf(string owner, string item)
        {
            if (owner.StartsWith("H", StringComparison.Ordinal) && int.TryParse(owner.Substring(1), out int h))
            {
                return Inventories.GetOrCreate(h).GetTotalUnits(item);
            }

            int v;
            if (BusinessGoods.TryGetValue(owner + "|" + item, out v)) return v;
            if (string.Equals(owner, Store, StringComparison.Ordinal))
            {
                int total = 0;
                if (Store1.Stock.TryGetValue(item, out int ss)) total += ss;
                if (Store1.Backroom.TryGetValue(item, out int bb)) total += bb;
                return total;
            }

            return 0;
        }

        private int FinalStock(string item)
        {
            int total = 0;
            foreach (int h in new[] { H1, H2, H3, H4, H5, H6 })
            {
                total += Inventories.GetOrCreate(h).GetTotalUnits(item);
            }

            foreach (var kv in BusinessGoods)
            {
                if (kv.Key.EndsWith("|" + item, StringComparison.Ordinal)) total += kv.Value;
            }

            total += FinalStockOf(Store, item);
            return total;
        }
    }
}
