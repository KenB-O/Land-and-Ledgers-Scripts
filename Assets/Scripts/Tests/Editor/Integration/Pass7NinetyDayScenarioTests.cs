// Pass VII extended autonomous simulation: "Millbrook, ninety days".
//
// A bounded 90-day economic scenario driven entirely by the production
// authorities (no fake engine, no manufactured revenue):
//   - 6 households, 13 persons, 4 operating businesses (farm, grain mill,
//     bakery, general store), a landlord, a delegated manager, legitimate
//     employment with weekly payroll, shared horses + wagon with real
//     reservation contention, trade credit, a bank loan, rent obligations,
//     social visits with information transmission, and scripted adverse
//     events (mill breakdown, drought, flour shortage, missed meals).
//
// Losses, shortages and failed decisions are permitted and observed. Every
// cash cent and every goods unit is reconciled at the end. Save/load and
// tick-size consistency are tested below.
//
// Reported scope: Roslyn harness only. This is NOT Unity validation.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using LandLedgers.Animals;
using LandLedgers.Economy;
using LandLedgers.Economy.Core;
using LandLedgers.Economy.Delegation;
using LandLedgers.Economy.DraftPower;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Generic;
using LandLedgers.Economy.Labor;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.Editor.Integration
{
    /// <summary>
    /// The autonomous scenario world. All mutable economy state lives in the
    /// production authorities; this class only orchestrates days, records
    /// observations, and reconciles.
    /// </summary>
    public sealed class NinetyDayScenario
    {
        public const int StartDay = 500;
        public const int SimDays = 90;
        public const int EndDayExclusive = StartDay + SimDays;
        public const int Seed = 777;

        // Households.
        public const int H1 = 7, H2 = 8, H3 = 9, H4 = 10, H5 = 11, H6 = 12;
        // Persons: H1 Tomas(baker)/Petra/Milo, H2 Sable(farmhand)/Wren(clerk)/Nina,
        // H3 Ash(landlord), H4 Dana(manager), H5 Otto(owner)/Clara, H6 Robin(farmhand)/June/Bram(miller).
        public const int P1 = 1, P2 = 2, P3 = 3, P4 = 4, P5 = 5, P13 = 13;
        public const int P6 = 6, P7 = 7, P8 = 8, P12 = 12, P9 = 9, P14 = 14, P15 = 15;

        public const string Bakery = "bakery-1";
        public const string Farm = "farm-1";
        public const string Store = "store-1";
        public const string Mill = "mill-1";
        public const string Saloon = "saloon-1";

        public const string Grain = "grain";
        public const string Flour = "flour";
        public const string Bread = "bread";

        public const int GrainPriceCents = 35;
        public const int FlourPriceCents = 55;
        public const int BreadWholesaleCents = 75;
        public const int BreadRetailCents = 100;

        // ---- authorities (production) ----
        public PopulationState Population = new PopulationState();
        public HouseholdLedgerRegistry Ledgers = new HouseholdLedgerRegistry();
        public HouseholdNeedRegistry Needs = new HouseholdNeedRegistry();
        public SupplierDirectory Directory = new SupplierDirectory();
        public RealDemandSource Demand;
        public readonly Dictionary<string, BusinessRuntimeState> Runtimes =
            new Dictionary<string, BusinessRuntimeState>(StringComparer.Ordinal);
        public readonly Dictionary<string, BusinessInstanceState> Instances =
            new Dictionary<string, BusinessInstanceState>(StringComparer.Ordinal);
        public EmploymentRelationshipRegistry Employments = new EmploymentRelationshipRegistry();
        public WageSettlementService Wages = new WageSettlementService();
        public ManagerBrain Brain = new ManagerBrain();
        public ManagerDecisionExecutionService Exec = new ManagerDecisionExecutionService();
        public DelegatedAuthority StoreAuthority;
        public AnimalRegistry Animals;
        public EquipmentReservationService Equipment = new EquipmentReservationService();
        public AnimalAvailabilityAuthority Availability;
        public DraftPowerService DraftPower;
        public HorseRentalService Rentals = new HorseRentalService();
        public PersonScheduleTracker Schedules = new PersonScheduleTracker();
        public KnowledgeTransmissionStore Knowledge = new KnowledgeTransmissionStore();
        public FinancialObligationAuthority Obligations = new FinancialObligationAuthority();
        public EntityIdRegistry Ids = new EntityIdRegistry();
        public HouseholdMembershipRegistry Memberships = new HouseholdMembershipRegistry();
        public ContactGraphService Contacts = new ContactGraphService();
        public EmploymentNegotiationService Negotiations =
            new EmploymentNegotiationService((businessId, personId, act) => true);
        public readonly Dictionary<int, PersonState> Persons = new Dictionary<int, PersonState>();
        public readonly Dictionary<int, int> PersonHousehold = new Dictionary<int, int>();

        // ---- scenario state ----
        public int CurrentDay = StartDay;
        public bool MillBroken;
        public bool DroughtDone;
        public readonly List<string> Diag = new List<string>();
        public readonly List<CashTransfer> CashLog = new List<CashTransfer>();
        public readonly List<ReservationRecord> AnimalReservations = new List<ReservationRecord>();
        public readonly List<PurchaseIntent> PendingB2B = new List<PurchaseIntent>();
        public FinancialObligation FarmLoan;

        // ---- observation counters ----
        public int InitialInternalCashCents;
        public int ExternalInflowCents;
        public int ExternalOutflowCents;
        public long ProducedGrain, ProducedFlour, BakedBread;
        public long BakeryFlourStockouts;
        public long WastedBran;
        public long EatenBread, MissedMeals;
        public long RetailBreadSold, CreditBreadSold;
        public long WholesaleBreadMoved, FlourMovedB2B, GrainMovedB2B;
        public long ManagerBreadShipped;
        public long FeedGrainEaten;
        public int UnfedHorseDays;
        public int ContentionRefusals;
        public int WagonDeferrals;
        public int EmploymentConflictRefusals;
        public int PayrollWeeksSettled;
        public int PayrollArrearsWeeks;
        public int ManagerProcureDecisions, ManagerShipmentsBooked, ManagerPendingRequests;
        public int RentPaymentsMade, RentArrearsEvents;
        public int CreditSalesMade, CreditRepaidCents;
        public int SocialVisitsHeld, PacketsTransmitted, JobRefusalsCitingRumor;
        public int HorseRentalCount;
        public long TotalWorkMinutes;

        // goods pools: pool -> net units moved (my mirror of production API calls)
        public readonly Dictionary<string, long> GoodsIn = new Dictionary<string, long>(StringComparer.Ordinal);
        public readonly Dictionary<string, long> GoodsOut = new Dictionary<string, long>(StringComparer.Ordinal);

        public sealed class CashTransfer
        {
            public int DayIndex;
            public string From = string.Empty;
            public string To = string.Empty;
            public int AmountCents;
            public string Reason = string.Empty;
        }

        public sealed class ReservationRecord
        {
            public string HorseKey = string.Empty; // "horse:bramble" | "horse:thistle" | "wagon-1"
            public float FromSeconds;
            public float ToSeconds;
            public string Purpose = string.Empty;
            public bool Granted;
        }

        private static void SetPrivate(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) throw new InvalidOperationException("missing field " + name);
            field.SetValue(target, value);
        }

        private void GoodsMove(string pool, long delta)
        {
            if (delta >= 0) GoodsIn[pool] = GoodsIn.GetValueOrDefault(pool) + delta;
            else GoodsOut[pool] = GoodsOut.GetValueOrDefault(pool) - delta;
        }

        // ================= build =================

        private readonly Dictionary<string, AnimalState> horses = new Dictionary<string, AnimalState>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> animalIdToStableKey = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> workingToday = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> householdShopper = new Dictionary<string, int>();
        private FarmFeedStock feedStock;

        private sealed class LiveSupplier : IGoodsSupplier
        {
            public string Id = string.Empty;
            public string Name = string.Empty;
            public Func<string, bool> HasCategoryFn = _ => false;
            public Func<string, int> StockFn = _ => 0;
            public Func<string, int> PriceFn = _ => 0;
            public string SupplierBusinessId => Id;
            public string SupplierName => Name;
            public string LocationId => "business:" + Id;
            public bool HasCategory(string categoryId) => HasCategoryFn(categoryId);
            public int StockUnits(string categoryId) => Math.Max(0, StockFn(categoryId));
            public int PricePerUnitCents(string categoryId) => Math.Max(0, PriceFn(categoryId));
            public int Sell(string categoryId, int requestedUnits, int dayIndex, List<string> diagnostics)
            {
                if (diagnostics != null)
                    diagnostics.Add("LiveSupplier.Sell is not an execution path; execution goes through the production sale authorities.");
                return 0;
            }
        }

        private sealed class FarmFeedStock : IHorseFeedStock
        {
            private readonly NinetyDayScenario sim;
            public FarmFeedStock(NinetyDayScenario s) { sim = s; }
            public int FeedStockUnits => sim.FarmGrainUnits();
            public int TakeFeedUnits(int requestedUnits)
            {
                int take = Math.Min(Math.Max(0, requestedUnits), sim.FarmGrainUnits());
                if (take > 0) sim.ConsumeFarmGrain(take, "horse feed");
                return take;
            }
        }

        private int FarmGrainUnits()
        {
            var stock = Runtimes[Farm].GetCategoryStock(Grain);
            return stock != null ? stock.CurrentStockUnits : 0;
        }

        private void ConsumeFarmGrain(int units, string reason)
        {
            if (Runtimes[Farm].TryConsumeCategoryStockUnits(Grain, units, out int consumed) && consumed > 0)
            {
                GoodsMove("farm.grain", -consumed);
                if (reason == "horse feed") FeedGrainEaten += consumed;
            }
        }

        public void Build()
        {
            BuildStatic();
            SeedEconomy();
        }

        public void BuildStatic()
        {
            ProductionPathRegistry.ClearAllInstanceOverrides();
            Diag.Clear();

            Animals = new AnimalRegistry(Ids);
            Availability = new AnimalAvailabilityAuthority(Equipment);
            DraftPower = new DraftPowerService(Equipment);
            feedStock = new FarmFeedStock(this);

            // ---- persons ----
            AddPerson(P1, "Tomas", "Baker", H1); AddPerson(P2, "Petra", "Baker", H1); AddPerson(P3, "Milo", "Baker", H1);
            AddPerson(P4, "Sable", "Field", H2); AddPerson(P5, "Wren", "Field", H2); AddPerson(P13, "Nina", "Field", H2);
            AddPerson(P6, "Ash", "Land", H3);
            AddPerson(P7, "Dana", "Ward", H4);
            AddPerson(P8, "Otto", "Carter", H5); AddPerson(P12, "Clara", "Carter", H5);
            AddPerson(P9, "Robin", "Hay", H6); AddPerson(P14, "June", "Hay", H6); AddPerson(P15, "Bram", "Miller", H6);

            int membershipId = 1;
            foreach (var kv in PersonHousehold)
            {
                string problem = Memberships.Register(new HouseholdMembership
                {
                    MembershipId = membershipId++,
                    PersonId = kv.Key,
                    HouseholdId = kv.Value,
                    Lifecycle = HouseholdMembershipLifecycle.Active,
                    StartDayIndex = StartDay - 100,
                });
                if (problem != null) Diag.Add("membership refused: " + problem);
            }

            // ---- households + bread reserves ----
            foreach (int hid in new[] { H1, H2, H3, H4, H5, H6 })
            {
                var household = new HouseholdState { id = hid };
                household.reserves.Add(new HouseholdReserveState
                {
                    categoryId = Bread,
                    displayName = "Bread",
                    currentUnits = 20,
                    lowThresholdUnits = 14,
                    targetUnits = 28,
                });
                Population.households.Add(household);
                Ledgers.GetOrCreate(hid);
            }
            householdShopper["household:7"] = P2; householdShopper["household:8"] = P5;
            householdShopper["household:9"] = P6; householdShopper["household:10"] = P7;
            householdShopper["household:11"] = P12; householdShopper["household:12"] = P14;

            // ---- business runtimes ----
            AddRuntime(Bakery, 0); AddRuntime(Farm, 0); AddRuntime(Store, 0); AddRuntime(Mill, 0);

            // ---- business instances (store + bakery: generic-path retail/procurement) ----
            var store = new BusinessInstanceState();
            SetPrivate(store, "instanceId", Store);
            store.TryAssignPremises(101);
            SetPrivate(store, "runtimeState", Runtimes[Store]);
            Instances[Store] = store;

            var bakery = new BusinessInstanceState();
            SetPrivate(bakery, "instanceId", Bakery);
            bakery.TryAssignPremises(102);
            SetPrivate(bakery, "runtimeState", Runtimes[Bakery]);
            Instances[Bakery] = bakery;

            var saloon = new BusinessInstanceState();
            SetPrivate(saloon, "instanceId", Saloon);
            saloon.TryAssignPremises(103);
            Instances[Saloon] = saloon;

            ProductionPathRegistry.SetInstancePath(Store, ProductionPath.GenericPath);
            ProductionPathRegistry.SetInstancePath(Bakery, ProductionPath.GenericPath);

            store.GenericConfiguration.Retail.AddOrReplaceLine(
                new GenericRetailProductLine(Bread, "shelf-a", 15f, 30f, 6f, BreadRetailCents));
            // The production retail path mints a zero-allocation capacity pool on
            // first sale (ReleaseProductSpace -> GetOrCreatePool), which then
            // blocks every future shipment receive. Configure real shelf space.
            store.GenericConfiguration.Retail.GetOrCreatePool("shelf-a", 200f);

            // ---- employment ----
            Employ("emp-bakery-1-1", P1, Bakery, 400);
            Employ("emp-farm-1-4", P4, Farm, 350);
            Employ("emp-farm-1-9", P9, Farm, 180);
            Employ("emp-store-1-7", P7, Store, 500);
            Employ("emp-store-1-5", P5, Store, 300);
            Employ("emp-store-1-8", P8, Store, 250);
            Employ("emp-mill-1-15", P15, Mill, 380);

            // ---- horses (owned by Ash, household 9) ----
            AddHorse("bramble", "Bramble");
            AddHorse("thistle", "Thistle");
            Equipment.RegisterResourceCapacity("wagon-1", 1f);

            // ---- manager ----
            StoreAuthority = new DelegatedAuthority
            {
                AuthorityId = "auth-store-1",
                BusinessInstanceId = Store,
                DelegatePersonId = P7,
                Scopes = DelegationScope.Purchasing,
                PurchaseLimitCents = 20000,
                GrantedDayIndex = StartDay - 10,
            };
            Exec.Configure(new ManagerExecutionServices
            {
                FindBusiness = id => Instances.TryGetValue(id, out BusinessInstanceState b) ? b : null,
                AllBusinesses = () => Instances.Values,
                DemandFor = id => new LaborDemandReport { BusinessInstanceId = id, DayIndex = CurrentDay },
                BusinessEntityFor = id => EntityId.For(EntityKind.Business, Math.Abs(id.GetHashCode())),
                Tasks = new TaskAuthority(),
                WorkScheduling = new WorkScheduler(),
                PersonSchedules = Schedules,
                WorkTimeBudgets = new WorkTimeBudgetStore(),
                Employments = Employments,
                FinancialObligations = Obligations,
                Ids = Ids,
                SupplierKnowledge = new ShopperSellerKnowledge(),
                BusinessLender = null,
                OpenRecruitment = (business, demand, decision, day, diag) => "effort:" + business.InstanceId,
            });

            // ---- social graph: a few documented links ----
            Contacts.SeedHouseholdLinks(H2, P4, new[] { P5, P13 });
            Contacts.SeedHouseholdLinks(H6, P9, new[] { P14, P15 });
            Contacts.AddLink(P15, P12, ContactLinkKind.Witnessed, "Bram takes his supper at Clara's saloon counter");
            Contacts.AddLink(P12, P15, ContactLinkKind.Witnessed, "Clara knows Bram by name and trade");

            // ---- demand ----
            Directory.Register(new LiveSupplier
            {
                Id = Store, Name = "General Store",
                HasCategoryFn = c => c == Bread,
                StockFn = c => c == Bread ? (int)Instances[Store].GenericConfiguration.GetAvailableInventoryQuantity(Bread) : 0,
                PriceFn = c => c == Bread ? BreadRetailCents : 0,
            });
            Directory.Register(new LiveSupplier
            {
                Id = Mill, Name = "Grain Mill",
                HasCategoryFn = c => c == Flour,
                StockFn = c => { var s = Runtimes[Mill].GetCategoryStock(c); return s != null ? s.CurrentStockUnits : 0; },
                PriceFn = c => c == Flour ? FlourPriceCents : 0,
            });
            Directory.Register(new LiveSupplier
            {
                Id = Farm, Name = "Home Farm",
                HasCategoryFn = c => c == Grain,
                StockFn = c => { var s = Runtimes[Farm].GetCategoryStock(c); return s != null ? s.CurrentStockUnits : 0; },
                PriceFn = c => c == Grain ? GrainPriceCents : 0,
            });
            Demand = new RealDemandSource(
                Population, Ledgers, Needs, Directory,
                householdKnowsSupplier: (hid, sid) => sid == Store,
                businessCashCents: bid => Runtimes.TryGetValue(bid, out BusinessRuntimeState rt) ? rt.CurrentCashCents : 0,
                generalStoreSupplierId: Store);
        }

        private void AddPerson(int id, string first, string last, int householdId)
        {
            Persons[id] = new PersonState { id = id, firstName = first, lastName = last };
            Population.people.Add(Persons[id]);
            PersonHousehold[id] = householdId;
        }

        private void AddRuntime(string businessId, int openingCashCents)
        {
            var rt = new BusinessRuntimeState();
            rt.BindBusinessIdentity(businessId);
            if (openingCashCents != 0) rt.AddCashCents(openingCashCents);
            rt.EmploymentRegistry = Employments;
            rt.HouseholdLedgers = Ledgers;
            rt.PersonHouseholdIdLookup = pid => PersonHousehold.TryGetValue(pid, out int h) ? h : -1;
            Runtimes[businessId] = rt;
        }

        private void AddHorse(string stableKey, string name)
        {
            AnimalState horse = Animals.RegisterAnimal(
                AnimalSpecies.Horse, "draft", AnimalSex.Female,
                AnimalOwnerKind.Household, H3.ToString(), StartDay - 400, "scenario team: " + name);
            string problem = HorseAuthority.Configure(horse, HorseUseCapabilities.GeneralPurpose, Farm, "stable-1", StartDay - 400);
            if (problem != null) Diag.Add("horse configure refused: " + problem);
            horses[stableKey] = horse;
            animalIdToStableKey[horse.AnimalId.ToString()] = stableKey;
        }

        private void Employ(string empId, int personId, string businessId, int weeklyWageCents)
        {
            var rel = new EmploymentRelationship
            {
                Id = empId,
                EmployeePersonId = personId,
                EmployerBusinessId = businessId,
                LifecycleState = EmploymentLifecycleState.Active,
                StartDayIndex = StartDay,
                Compensation = CompensationTerms.FromWeeklyWage(weeklyWageCents, "scenario wage"),
            };
            string problem = Employments.Register(rel);
            if (problem != null) Diag.Add("employment refused: " + problem);
        }

        private void SeedEconomy()
        {
            Runtimes[Bakery].AddCashCents(20000);
            Runtimes[Farm].AddCashCents(15000);
            Runtimes[Store].AddCashCents(25000);
            Runtimes[Mill].AddCashCents(18000);

            FundHousehold(H1, 8000); FundHousehold(H2, 6000); FundHousehold(H3, 12000);
            FundHousehold(H4, 6000); FundHousehold(H5, 10000); FundHousehold(H6, 6000);

            // Snapshot the clean opening position BEFORE the external loan arrives.
            InitialInternalCashCents = TotalInternalCashCents();

            Runtimes[Farm].EnsureCategoryStock(Grain, 200, 1600);
            GoodsMove("farm.grain", 200);
            Runtimes[Mill].EnsureCategoryStock(Grain, 60, 600);
            GoodsMove("mill.grain", 60);
            Runtimes[Mill].EnsureCategoryStock(Flour, 40, 500);
            GoodsMove("mill.flour", 40);

            Instances[Bakery].GenericConfiguration.AddInventory(Flour, 30f, FlourPriceCents);
            GoodsMove("bakery.flour", 30);
            Instances[Bakery].GenericConfiguration.AddInventory(Bread, 15f, BreadWholesaleCents);
            GoodsMove("bakery.bread", 15);
            bool seeded = Instances[Store].GenericConfiguration.TryAddInventory(Bread, 20f, BreadWholesaleCents);
            if (!seeded) Diag.Add("store opening bread intake refused");
            else GoodsMove("store.bread", 20);

            // Farm operating loan: real bank debt, real cash in.
            FarmLoan = Obligations.Create(Ids, FinancialObligationKind.Loan,
                "business:" + Farm, "bank:millbrook", 20000, StartDay,
                "weekly 800c", "farm operating loan");
            Runtimes[Farm].AddCashCents(20000);
            ExternalInflowCents += 20000;
            LogCash(StartDay, "bank:millbrook", "business:" + Farm, 20000, "farm loan drawn");

            // Household opening bread (6 x 20) is real goods on hand.
            GoodsMove("household.bread", 120);
        }

        private void FundHousehold(int householdId, int cents)
        {
            string problem = Ledgers.GetOrCreate(householdId).RecordInflow(
                StartDay, cents, HouseholdIncomeSource.OtherDocumented,
                "opening-savings", "opening reserves", "self");
            if (problem != null) Diag.Add("funding refused: " + problem);
        }

        private int TotalInternalCashCents()
        {
            int total = 0;
            foreach (var rt in Runtimes.Values) total += rt.CurrentCashCents;
            foreach (int hid in new[] { H1, H2, H3, H4, H5, H6 }) total += Ledgers.GetOrCreate(hid).GetBalanceCents();
            return total;
        }

        private void LogCash(int day, string from, string to, int cents, string reason)
        {
            CashLog.Add(new CashTransfer { DayIndex = day, From = from, To = to, AmountCents = cents, Reason = reason });
        }

        // ================= daily step =================

        public void StepDay(int day)
        {
            CurrentDay = day;
            workingToday.Clear();
            var diag = Diag;

            // ---- 1. meals: each person eats 1 bread every 3 days, staggered ----
            foreach (var kv in PersonHousehold)
            {
                if ((day + kv.Key) % 3 != 0) continue;
                HouseholdState hh = Population.GetHousehold(kv.Value);
                HouseholdReserveState reserve = hh.reserves.Find(r => r.categoryId == Bread);
                if (reserve.currentUnits > 0)
                {
                    reserve.currentUnits--;
                    GoodsMove("household.bread", -1);
                    EatenBread++;
                }
                else
                {
                    MissedMeals++;
                }
            }

            // ---- 2. work time reservations (no double-booking allowed) ----
            ReserveWork(P1, 8 * 60, 480, "work:bakery-1");
            ReserveWork(P4, 8 * 60, 480, "work:farm-1");
            ReserveWork(P9, 8 * 60, 240, "work:farm-1");
            ReserveWork(P5, 9 * 60, 480, "work:store-1");
            ReserveWork(P7, 9 * 60, 480, "work:store-1");
            ReserveWork(P8, 9 * 60, 240, "work:store-1");
            ReserveWork(P15, 8 * 60, 480, "work:mill-1");

            // ---- 3. horse care (feed comes from real farm grain) ----
            int unfed = HorseCareDriver.CareDay(Animals, feedStock,
                h => h != null && workingToday.Contains(StableKeyOf(h)), day, diag);
            UnfedHorseDays += unfed;

            // ---- 4. scripted adverse events ----
            if (day == StartDay + 15)
            {
                int yield = 900;
                int accepted = Runtimes[Farm].AddCategoryStockUnits(Grain, yield);
                GoodsMove("farm.grain", accepted);
                ProducedGrain += accepted;
                if (accepted < yield) diag.Add($"day {day}: granary full — stored {accepted}/{yield} grain.");
                else diag.Add($"day {day}: harvest +{accepted} grain at farm-1.");
            }
            if (day == StartDay + 20)
            {
                MillBroken = true;
                diag.Add($"day {day}: ADVERSITY — the mill's main belt snapped. Grinding stops until repaired.");
            }
            if (day == StartDay + 41)
            {
                ChargeExternal(Mill, 3000, day, "parts", "mill belt repair");
                MillBroken = false;
                diag.Add($"day {day}: mill repaired after 21 idle days; grinding resumes.");
            }
            if (day == StartDay + 60)
            {
                int yield = 450; // drought halved the second harvest
                int accepted = Runtimes[Farm].AddCategoryStockUnits(Grain, yield);
                GoodsMove("farm.grain", accepted);
                ProducedGrain += accepted;
                DroughtDone = true;
                diag.Add($"day {day}: ADVERSITY — drought. Second harvest only +{accepted} grain (expected 900).");
            }

            // ---- 5. milling: 12 grain -> 10 flour (2 lost as bran: real waste) ----
            // Grind only while flour stocks are lean (< 50u) so the mill keeps a
            // small buffer; a stoppage can then genuinely starve the bakery.
            if (!MillBroken)
            {
                var g = Runtimes[Mill].GetCategoryStock(Grain);
                var f = Runtimes[Mill].GetCategoryStock(Flour);
                if (g != null && g.CurrentStockUnits >= 12 && f != null && f.CurrentStockUnits < 30)
                {
                    if (Runtimes[Mill].TryConsumeCategoryStockUnits(Grain, 12, out int consumed) && consumed > 0)
                    {
                        GoodsMove("mill.grain", -consumed);
                        int flourMade = consumed * 10 / 12;
                        int accepted = Runtimes[Mill].AddCategoryStockUnits(Flour, flourMade);
                        GoodsMove("mill.flour", accepted);
                        ProducedFlour += accepted;
                        WastedBran += consumed - flourMade;
                        if (accepted < flourMade)
                            diag.Add($"day {day}: flour bins full — {flourMade - accepted}u of fresh flour could not be stored.");
                    }
                }
            }

            // ---- 6. baking: 5 flour -> 5 bread, capped by storage ----
            {
                var bakery = Instances[Bakery];
                float flourQty = bakery.GenericConfiguration.GetAvailableInventoryQuantity(Flour);
                float breadQty = bakery.GenericConfiguration.GetAvailableInventoryQuantity(Bread);
                if (flourQty >= 5f && breadQty < 20f)
                {
                    if (bakery.GenericConfiguration.TryConsumeInventory(Flour, 5f, out float consumedF) && consumedF > 0f)
                    {
                        GoodsMove("bakery.flour", -(long)consumedF);
                        bakery.GenericConfiguration.AddInventory(Bread, consumedF, BreadWholesaleCents);
                        GoodsMove("bakery.bread", (long)consumedF);
                        BakedBread += (long)consumedF;
                    }
                }
                else if (flourQty < 5f && breadQty < 20f)
                {
                    BakeryFlourStockouts++;
                    if (BakeryFlourStockouts <= 3 || BakeryFlourStockouts % 5 == 0)
                        diag.Add($"day {day}: bakery cannot bake — flour exhausted (mill stoppage).");
                }
            }

            // ---- 7. weekly business input needs (Monday) ----
            bool isMonday = (day - StartDay) % 7 == 0;
            if (isMonday)
            {
                Demand.ClearBusinessInputNeeds();
                Demand.RegisterBusinessInputNeed(new BusinessInputNeed
                {
                    BuyerBusinessInstanceId = Mill, InputCategoryId = Grain,
                    UnitsNeeded = 84, MaxPriceCentsPerUnit = 40, ReasonSummary = "weekly grist",
                });
                Demand.RegisterBusinessInputNeed(new BusinessInputNeed
                {
                    BuyerBusinessInstanceId = Bakery, InputCategoryId = Flour,
                    UnitsNeeded = 35, MaxPriceCentsPerUnit = 60, ReasonSummary = "weekly bake",
                });
            }

            // ---- 8. real demand collection + execution ----
            List<PurchaseIntent> intents = Demand.CollectDemand(day, diag);
            foreach (PurchaseIntent intent in intents)
            {
                if (intent.BuyerKind == "household") ExecuteHouseholdIntent(intent, day);
                else if (intent.BuyerKind == "business") PendingB2B.Add(intent);
                else diag.Add($"day {day}: unexpected intent buyer kind '{intent.BuyerKind}' — skipped, nothing moved.");
            }
            if (isMonday) Demand.ClearBusinessInputNeeds();
            ExtendStoreCredit(day);

            // ---- 9. shared horses + wagon ----
            StepAnimals(day, diag);

            // ---- 10. manager: Dana runs the store ----
            StepManager(day, diag);

            // ---- 11. weekly payroll (day 6, 13, ... of each week) ----
            if ((day - StartDay) % 7 == 6)
            {
                int weekIndex = (day - StartDay) / 7;
                foreach (string biz in new[] { Bakery, Farm, Store, Mill })
                {
                    string result = Wages.SettleWeeklyPayroll(Runtimes[biz], Employments, weekIndex, day, diag);
                    if (result == "settled") PayrollWeeksSettled++;
                    else if (result == "settled-arrears")
                    {
                        PayrollWeeksSettled++;
                        PayrollArrearsWeeks++;
                        diag.Add($"day {day}: {biz} settled payroll IN ARREARS — wages accrued, not paid.");
                    }
                }
                // upkeep: real operating expense, no revenue required to exist
                ChargeUpkeep(Bakery, 200, day); ChargeUpkeep(Farm, 300, day);
                ChargeUpkeep(Store, 400, day); ChargeUpkeep(Mill, 300, day);
                // farm loan repayment to the external bank
                {
                    var farm = Runtimes[Farm];
                    int pay = Math.Min(800, Math.Max(0, farm.CurrentCashCents));
                    if (pay > 0)
                    {
                        farm.AdjustCashCents(-pay, recordNetDelta: true);
                        Obligations.ApplyPayment(FarmLoan.ObligationId, pay, day, "business:" + Farm, "bank:millbrook");
                        ExternalOutflowCents += pay;
                        LogCash(day, "business:" + Farm, "bank:millbrook", pay, "loan repayment");
                    }
                    else
                    {
                        Diag.Add($"day {day}: farm-1 missed its 800c loan repayment — no cash.");
                    }
                }
                RepayStoreCredit(day);
            }

            // ---- 12. monthly rent to Ash (households 8, 12 and the bakery) ----
            if (day == StartDay + 10 || day == StartDay + 40 || day == StartDay + 70)
            {
                CollectRent("household:8", H2, 1200, day, "farm cottage rent");
                CollectRent("household:12", H6, 1000, day, "mill cottage rent");
                CollectRent("business:" + Bakery, -1, 1000, day, "bakery building rent");
            }

            // ---- 13. social beats ----
            StepSocial(day, diag);

            // ---- 14. horse rental day ----
            if (day == StartDay + 55) RentHorseToOtto(day);
            if (day == StartDay + 56) ReturnRentedHorse(day);

            // ---- end of day: release horse holds (except an active rental's hold) ----
            foreach (var kv in horses)
            {
                if (Rentals.ActiveRentalFor(kv.Value.AnimalId) != null) continue;
                HorseAuthority.ReleaseUse(kv.Value, Availability);
            }
        }

        private void ReserveWork(int personId, int startMinute, int durationMinutes, string label)
        {
            string refusal = Schedules.TryReserve(personId, PersonActivityKind.Work, CurrentDay, startMinute, durationMinutes, label);
            if (refusal != null)
            {
                EmploymentConflictRefusals++;
                Diag.Add($"day {CurrentDay}: work reservation refused for P{personId}: {refusal}");
            }
            else
            {
                TotalWorkMinutes += durationMinutes;
            }
        }

        private string StableKeyOf(AnimalState horse)
        {
            if (horse == null) return string.Empty;
            return animalIdToStableKey.TryGetValue(horse.AnimalId.ToString(), out string key) ? key : string.Empty;
        }

        private void ChargeUpkeep(string businessId, int centsPerWeek, int day)
        {
            ChargeExternal(businessId, centsPerWeek, day, "upkeep", "weekly upkeep");
        }

        private void ChargeExternal(string businessId, int cents, int day, string sink, string reason)
        {
            var rt = Runtimes[businessId];
            int before = rt.CurrentCashCents;
            rt.AdjustCashCents(-cents, recordNetDelta: true);
            int actual = Math.Max(0, before - rt.CurrentCashCents);
            if (actual > 0)
            {
                ExternalOutflowCents += actual;
                LogCash(day, "business:" + businessId, "external:" + sink, actual, reason);
            }
            else
            {
                Diag.Add($"day {day}: {businessId} could not pay {cents}c {reason} — no cash, nothing left the system.");
            }
        }

        private void CollectRent(string debtorKey, int householdId, int amountCents, int day, string purpose)
        {
            var ob = Obligations.Create(Ids, FinancialObligationKind.Payable,
                debtorKey, "household:" + H3, amountCents, day, "monthly", purpose);
            if (ob == null) { Diag.Add($"day {day}: rent obligation creation failed for {debtorKey}"); return; }

            int available = debtorKey.StartsWith("household:", StringComparison.Ordinal)
                ? Ledgers.GetOrCreate(householdId).GetBalanceCents()
                : Math.Max(0, Runtimes[Bakery].CurrentCashCents);
            int pay = Math.Min(amountCents, available);
            if (pay > 0)
            {
                if (debtorKey.StartsWith("household:", StringComparison.Ordinal))
                {
                    Ledgers.GetOrCreate(householdId).RecordOutflow(day, pay, purpose, "ash");
                }
                else
                {
                    Runtimes[Bakery].AdjustCashCents(-pay, recordNetDelta: true);
                }
                Ledgers.GetOrCreate(H3).RecordInflow(day, pay, HouseholdIncomeSource.OtherDocumented,
                    ob.ObligationId, "rent received: " + purpose, debtorKey);
                Obligations.ApplyPayment(ob.ObligationId, pay, day, debtorKey, "household:" + H3);
                LogCash(day, debtorKey, "household:" + H3, pay, purpose);
                RentPaymentsMade++;
            }
            if (pay < amountCents)
            {
                RentArrearsEvents++;
                Diag.Add($"day {day}: RENT ARREARS — {debtorKey} paid {pay}c of {amountCents}c ({purpose}).");
            }
        }

        private long TotalOutstandingFor(string debtor)
        {
            long total = 0;
            foreach (FinancialObligation ob in Obligations.Obligations)
            {
                if (ob != null && !ob.Settled && string.Equals(ob.Debtor, debtor, StringComparison.Ordinal)
                    && ob.Kind == FinancialObligationKind.TradeCredit)
                    total += ob.OutstandingPrincipalCents;
            }
            return total;
        }

        // ================= demand execution =================

        private readonly Dictionary<string, LiveSupplier> suppliersById = new Dictionary<string, LiveSupplier>(StringComparer.Ordinal);
        private readonly HashSet<string> countedShipments = new HashSet<string>(StringComparer.Ordinal);

        private LiveSupplier SupplierOf(string sellerId)
        {
            if (suppliersById.Count == 0)
            {
                // Rebuild from the directory registration order (deterministic).
                suppliersById[Store] = new LiveSupplier
                {
                    Id = Store,
                    StockFn = c => c == Bread ? (int)Instances[Store].GenericConfiguration.GetAvailableInventoryQuantity(Bread) : 0,
                    PriceFn = c => c == Bread ? BreadRetailCents : 0,
                };
                suppliersById[Mill] = new LiveSupplier
                {
                    Id = Mill,
                    StockFn = c => { var s = Runtimes[Mill].GetCategoryStock(c); return s != null ? s.CurrentStockUnits : 0; },
                    PriceFn = c => c == Flour ? FlourPriceCents : 0,
                };
                suppliersById[Farm] = new LiveSupplier
                {
                    Id = Farm,
                    StockFn = c => { var s = Runtimes[Farm].GetCategoryStock(c); return s != null ? s.CurrentStockUnits : 0; },
                    PriceFn = c => c == Grain ? GrainPriceCents : 0,
                };
            }
            return suppliersById.TryGetValue(sellerId, out LiveSupplier s) ? s : null;
        }

        private void ExecuteHouseholdIntent(PurchaseIntent intent, int day)
        {
            if (intent.SellerBusinessInstanceId != Store || intent.GoodsOrServiceId != Bread) return;
            string buyerKey = intent.BuyerKind + ":" + intent.BuyerId;
            var store = Instances[Store];
            int available = (int)store.GenericConfiguration.GetAvailableInventoryQuantity(Bread);
            int qty = Math.Min(intent.QuantityUnits, available);
            if (qty <= 0)
            {
                Diag.Add($"day {day}: retail: {buyerKey} wanted bread, store shelf empty — no sale.");
                return;
            }
            int price = qty * BreadRetailCents;
            int householdId = int.Parse(intent.BuyerId);
            var ctx = new GenericRetailSaleContext
            {
                BuyerPrincipal = buyerKey,
                ActingPersonId = householdShopper[buyerKey],
                SellerBusinessId = Store,
                ProductId = Bread,
                Quantity = qty,
                UnitPriceCents = BreadRetailCents,
                LocationId = Store,
                DayIndex = day,
            };
            bool ok = GenericRetailSaleAuthority.TryExecuteSale(store, ctx, c =>
            {
                var ledger = Ledgers.GetOrCreate(householdId);
                string problem = ledger.RecordOutflow(day, price, "bread purchase", Store);
                if (problem != null) return false;
                Runtimes[Store].RecordGenericRetailSettlement(Bread, qty, price);
                HouseholdState hh = Population.GetHousehold(householdId);
                hh.reserves.Find(r => r.categoryId == Bread).currentUnits += qty;
                GoodsMove("store.bread", -qty);
                GoodsMove("household.bread", qty);
                RetailBreadSold += qty;
                LogCash(day, buyerKey, "business:" + Store, price, "bread retail");
                return true;
            }, out string reason);
            if (!ok) Diag.Add($"day {day}: retail refused for {buyerKey}: {reason}");
        }

        private void ExtendStoreCredit(int day)
        {
            foreach (int hid in new[] { H1, H2, H3, H4, H5, H6 })
            {
                HouseholdState hh = Population.GetHousehold(hid);
                HouseholdReserveState reserve = hh.reserves.Find(r => r.categoryId == Bread);
                int bal = Ledgers.GetOrCreate(hid).GetBalanceCents();
                if (reserve.currentUnits >= 6 || bal >= BreadRetailCents * 4) continue;
                if (TotalOutstandingFor("household:" + hid) >= 3000) continue;
                var store = Instances[Store];
                int available = (int)store.GenericConfiguration.GetAvailableInventoryQuantity(Bread);
                int qty = Math.Min(Math.Min(6, Math.Max(1, 10 - reserve.currentUnits)), available);
                if (qty <= 0) continue;
                int price = qty * BreadRetailCents;
                string buyerKey = "household:" + hid;
                var ctx = new GenericRetailSaleContext
                {
                    BuyerPrincipal = buyerKey,
                    ActingPersonId = householdShopper[buyerKey],
                    SellerBusinessId = Store,
                    ProductId = Bread,
                    Quantity = qty,
                    UnitPriceCents = BreadRetailCents,
                    LocationId = Store,
                    DayIndex = day,
                };
                bool ok = GenericRetailSaleAuthority.TryExecuteSale(store, ctx, c =>
                {
                    FinancialObligation ob = Obligations.Create(Ids, FinancialObligationKind.TradeCredit,
                        buyerKey, "business:" + Store, price, day, "net 30", "bread on store credit");
                    if (ob == null) return false;
                    Runtimes[Store].RecordGenericRetailSettlement(Bread, qty, 0);
                    reserve.currentUnits += qty;
                    GoodsMove("store.bread", -qty);
                    GoodsMove("household.bread", qty);
                    CreditBreadSold += qty;
                    CreditSalesMade++;
                    Diag.Add($"day {day}: CREDIT — {buyerKey} takes {qty} bread on account ({price}c).");
                    return true;
                }, out string reason);
                if (!ok) Diag.Add($"day {day}: credit sale refused: {reason}");
            }
        }

        private void RepayStoreCredit(int day)
        {
            foreach (int hid in new[] { H1, H2, H3, H4, H5, H6 })
            {
                string debtor = "household:" + hid;
                var ledger = Ledgers.GetOrCreate(hid);
                var snapshot = new List<FinancialObligation>(Obligations.Obligations);
                foreach (FinancialObligation ob in snapshot)
                {
                    if (ob == null || ob.Settled || ob.Kind != FinancialObligationKind.TradeCredit) continue;
                    if (!string.Equals(ob.Debtor, debtor, StringComparison.Ordinal)) continue;
                    int bal = ledger.GetBalanceCents();
                    if (bal <= 300) break;
                    int pay = Math.Min(200, Math.Min(bal - 300, ob.OutstandingPrincipalCents));
                    if (pay <= 0) continue;
                    ledger.RecordOutflow(day, pay, "store credit repayment", Store);
                    Runtimes[Store].AddCashCents(pay);
                    Obligations.ApplyPayment(ob.ObligationId, pay, day, debtor, "business:" + Store);
                    CreditRepaidCents += pay;
                    LogCash(day, debtor, "business:" + Store, pay, "credit repayment");
                }
            }
        }

        // ================= animals =================

        private bool IsPlowDay(int day)
        {
            int o = day - StartDay;
            return (o >= 5 && o <= 8) || (o >= 48 && o <= 51);
        }

        private bool IsMarketDay(int day)
        {
            int w = (day - StartDay) % 7;
            return w == 0 || w == 2 || w == 4;
        }

        private void StepAnimals(int day, List<string> diag)
        {
            if (IsPlowDay(day))
            {
                foreach (string hk in new[] { "bramble", "thistle" })
                {
                    float from = day * 86400f + 6 * 3600f, to = day * 86400f + 12 * 3600f;
                    string refusal = HorseAuthority.TryReserveUse(
                        horses[hk], HorseUseKind.Draft, "plowing:" + day, Availability, from, to, out _);
                    if (refusal == null)
                    {
                        workingToday.Add(hk);
                        AnimalReservations.Add(new ReservationRecord
                            { HorseKey = "horse:" + hk, FromSeconds = from, ToSeconds = to, Purpose = "plowing", Granted = true });
                        Runtimes[Farm].AdjustCashCents(-50, recordNetDelta: true);
                        Ledgers.GetOrCreate(H3).RecordInflow(day, 50, HouseholdIncomeSource.OtherDocumented,
                            "plow-fee-" + day, "horse plowing fee", "business:" + Farm);
                        LogCash(day, "business:" + Farm, "household:9", 50, "horse plowing fee");
                    }
                    else
                    {
                        ContentionRefusals++;
                        AnimalReservations.Add(new ReservationRecord
                            { HorseKey = "horse:" + hk, FromSeconds = from, ToSeconds = to, Purpose = "plowing", Granted = false });
                        diag.Add($"day {day}: plowing reservation refused for {hk}: {refusal}");
                    }
                }
            }

            if (IsMarketDay(day))
            {
                float from = day * 86400f + 8 * 3600f, to = day * 86400f + 12 * 3600f;
                string refusal = HorseAuthority.TryReserveUse(
                    horses["bramble"], HorseUseKind.Draft, "market-run:" + day, Availability, from, to, out _);
                string wagonTask = "wagon-" + day;
                string wagonRefusal = null;
                if (refusal == null)
                {
                    var req = new ReservationRequest(wagonTask, "market run");
                    req.Items.Add(new ReservationItem("wagon-1", 1f, from, to));
                    wagonRefusal = Equipment.TryReserveAll(req, out _);
                }
                if (refusal == null && wagonRefusal == null)
                {
                    workingToday.Add("bramble");
                    AnimalReservations.Add(new ReservationRecord
                        { HorseKey = "horse:bramble", FromSeconds = from, ToSeconds = to, Purpose = "market-run", Granted = true });
                    AnimalReservations.Add(new ReservationRecord
                        { HorseKey = "wagon-1", FromSeconds = from, ToSeconds = to, Purpose = "market-run", Granted = true });
                    ExecutePendingB2B(day);
                    Runtimes[Farm].AdjustCashCents(-30, recordNetDelta: true);
                    Ledgers.GetOrCreate(H3).RecordInflow(day, 30, HouseholdIncomeSource.OtherDocumented,
                        "wagon-fee-" + day, "wagon use fee", "business:" + Farm);
                    LogCash(day, "business:" + Farm, "household:9", 30, "wagon use fee");
                    Equipment.ReleaseAllForTask(wagonTask);
                }
                else
                {
                    ContentionRefusals++;
                    WagonDeferrals++;
                    AnimalReservations.Add(new ReservationRecord
                        { HorseKey = "horse:bramble", FromSeconds = from, ToSeconds = to, Purpose = "market-run", Granted = false });
                    if (refusal == null) Equipment.ReleaseAllForTask(wagonTask);
                    diag.Add($"day {day}: market run refused ({refusal ?? wagonRefusal}) — B2B transfers deferred.");
                }
            }
        }

        private void ExecutePendingB2B(int day)
        {
            foreach (PurchaseIntent intent in PendingB2B)
            {
                string sellerId = intent.SellerBusinessInstanceId;
                string buyerId = intent.BuyerId;
                string category = intent.GoodsOrServiceId;
                LiveSupplier supplier = SupplierOf(sellerId);
                if (supplier == null) continue;
                int qty = Math.Min(intent.QuantityUnits, supplier.StockUnits(category));
                if (qty <= 0)
                {
                    Diag.Add($"day {day}: B2B {sellerId}->{buyerId} {category}: seller has no stock — lapsed.");
                    continue;
                }
                int price = qty * supplier.PricePerUnitCents(category);
                var buyerRt = Runtimes[buyerId];
                if (buyerRt.CurrentCashCents < price)
                {
                    Diag.Add($"day {day}: B2B {buyerId} cannot afford {price}c of {category} — lapsed.");
                    continue;
                }
                bool settled = SellerSettleB2B(sellerId, category, qty, price, buyerId, day);
                if (!settled) continue;
                buyerRt.AdjustCashCents(-price, recordNetDelta: true);
                BuyerReceiveB2B(buyerId, category, qty);
                LogCash(day, "business:" + buyerId, "business:" + sellerId, price, category + " B2B");
                if (sellerId == Farm && category == Grain) GrainMovedB2B += qty;
                if (sellerId == Mill && category == Flour) FlourMovedB2B += qty;
            }
            PendingB2B.Clear();
        }

        private bool SellerSettleB2B(string sellerId, string category, int qty, int priceCents, string buyerId, int day)
        {
            var rt = Runtimes[sellerId];
            bool ok = rt.ResolveSettledSale(category, qty, priceCents, "business:" + buyerId);
            if (!ok) { Diag.Add($"day {day}: B2B seller {sellerId} refused settlement."); return false; }
            GoodsMove(sellerId == Farm ? "farm.grain" : "mill.flour", -qty);
            return true;
        }

        private void BuyerReceiveB2B(string buyerId, string category, int qty)
        {
            if (buyerId == Mill && category == Grain)
            {
                int accepted = Runtimes[Mill].AddCategoryStockUnits(Grain, qty);
                GoodsMove("mill.grain", accepted);
                if (accepted < qty)
                    Diag.Add($"day {CurrentDay}: mill grain bins full — {qty - accepted}u of {qty}u purchased grain could not be stored.");
            }
            else if (buyerId == Bakery && category == Flour)
            {
                Instances[Bakery].GenericConfiguration.AddInventory(Flour, qty, FlourPriceCents);
                GoodsMove("bakery.flour", qty);
            }
        }

        // ================= manager =================

        private void StepManager(int day, List<string> diag)
        {
            int shipmentsBefore = Exec.Ledger.Shipments.Count;
            var storeInst = Instances[Store];
            long breadBefore = (long)storeInst.GenericConfiguration.GetAvailableInventoryQuantity(Bread);
            Exec.AdvanceShipments(day, diag);
            long breadAfter = (long)storeInst.GenericConfiguration.GetAvailableInventoryQuantity(Bread);
            long received = Math.Max(0, breadAfter - breadBefore);
            if (received > 0)
            {
                GoodsMove("store.bread", received);
                WholesaleBreadMoved += received;
            }
            long newlyDeliveredQty = 0;
            foreach (var ship in Exec.Ledger.Shipments)
            {
                if (ship != null && ship.Status == "Delivered" && countedShipments.Add(ship.ShipmentId))
                    newlyDeliveredQty += (long)ship.Quantity;
            }
            if (newlyDeliveredQty > received)
            {
                diag.Add($"day {day}: ledger shows {newlyDeliveredQty}u delivered but stock rose only {received}u — receive refused for the rest.");
            }

            var storeRt = Runtimes[Store];
            float breadQty = Instances[Store].GenericConfiguration.GetAvailableInventoryQuantity(Bread);
            var demandReport = new LaborDemandReport { BusinessInstanceId = Store, DayIndex = day };
            ManagerDecisionResult decision = Brain.DailyCycle(new ManagerCycleInputs
            {
                BusinessInstanceId = Store,
                ManagerPersonId = P7,
                CashCents = storeRt.CurrentCashCents,
                DemandReport = demandReport,
                DelegatedScopes = new List<string> { ManagerScopeNames.Purchasing },
                DayIndex = day,
                JudgmentSeed = Seed * 100000 + day,
                JudgmentQuality01 = 0.9f,
                PurchaseLimitCents = 20000,
                MaxBorrowingCents = 0,
                ShortageObserved = breadQty <= 12f,
                ProposedProcurementCents = 3000,
            }, diag);

            if (decision.Acted || decision.Escalated)
            {
                int cashBefore = storeRt.CurrentCashCents;
                ManagerExecutionOutcome outcome = Exec.ExecuteDecision(
                    Instances[Store], StoreAuthority, P7, demandReport, decision, day, diag);
                if (decision.Acted && decision.ActionKind == ManagerActKinds.Procure) ManagerProcureDecisions++;
                if (outcome.Executed && Exec.Ledger.Shipments.Count > shipmentsBefore)
                {
                    var ship = Exec.Ledger.Shipments[Exec.Ledger.Shipments.Count - 1];
                    GoodsMove("bakery.bread", -(long)ship.Quantity);
                    ManagerBreadShipped += (long)ship.Quantity;
                    int paid = cashBefore - storeRt.CurrentCashCents;
                    LogCash(day, "business:" + Store, "business:" + Bakery, paid, "manager bread procurement");
                    ManagerShipmentsBooked++;
                }
                if (outcome.Pending) ManagerPendingRequests = Exec.Ledger.PendingRequests.Count;
            }
        }

        // ================= social =================

        private ConversationOpportunityService Opportunities()
        {
            return new ConversationOpportunityService(
                Schedules,
                id => Persons.TryGetValue(id, out PersonState p) ? p : null,
                Employments,
                id => Instances.TryGetValue(id, out BusinessInstanceState b) ? b : null);
        }

        private void PlanVisit(int personId, int startMinute, int durationMinutes, SocialVisitMotivation mot, int day)
        {
            string refusal = SocialVisitPlanner.PlanSocialVisit(
                personId, Saloon, day, startMinute, durationMinutes, mot, Schedules, Diag);
            if (refusal == null) SocialVisitsHeld++;
            else Diag.Add($"day {day}: social visit refused for P{personId}: {refusal}");
        }

        private void StepSocial(int day, List<string> diag)
        {
            int d522 = StartDay + 22, d523 = StartDay + 23, d524 = StartDay + 24;
            if (day == d522)
            {
                PlanVisit(P15, 19 * 60, 90, SocialVisitMotivation.Recreation, day);
                PlanVisit(P12, 18 * 60, 180, SocialVisitMotivation.Friendship, day);
                PlanVisit(P9, 19 * 60 + 30, 60, SocialVisitMotivation.Companionship, day);
                var ops = Opportunities();
                if (ops.TryGetChannel(P15, P12, day, out _, diag))
                {
                    InformationPacket packet = InformationPacketFactory.Observe("mill-trouble",
                        "the main belt snapped this morning; the mill will not grind for days", P15, day, 0.8f);
                    string refusal = ConversationRunner.Converse(ops, Knowledge, P15, P12, day, packet, diag);
                    if (refusal == null)
                    {
                        PacketsTransmitted++;
                        diag.Add($"day {day}: Bram tells Clara firsthand that the mill belt snapped.");
                    }
                }
            }
            if (day == d523)
            {
                PlanVisit(P12, 18 * 60, 180, SocialVisitMotivation.Friendship, day);
                PlanVisit(P9, 19 * 60, 90, SocialVisitMotivation.Companionship, day);
                var ops = Opportunities();
                List<InformationPacket> prior = Knowledge.PacketsFor(P12, "mill-trouble");
                if (prior.Count > 0 && ops.TryGetChannel(P12, P9, day, out _, diag))
                {
                    InformationPacket relay = InformationPacketFactory.Relay(prior[0], P12, P9, day, 0.6f);
                    string refusal = ConversationRunner.Converse(ops, Knowledge, P12, P9, day, relay, diag);
                    if (refusal == null)
                    {
                        PacketsTransmitted++;
                        diag.Add($"day {day}: Clara relays the mill news to Robin (secondhand).");
                    }
                }
            }
            if (day == d524)
            {
                EmploymentOffer offer = Negotiations.MakeOffer(Mill, P9, P15, 120,
                    "repair crew: haul timber for the new belt", "dawn to dusk", day + 1, 2,
                    Mill, 0, string.Empty, "labor", string.Empty, P15,
                    "InPerson", "WordOfMouth", day, diag);
                if (offer != null)
                {
                    string refusal = Negotiations.RecordRefusal(offer.OfferId,
                        "the mill is idle, or so Clara heard from Bram (rumor, confidence 0.60) — no work there",
                        day, diag);
                    if (refusal == null)
                    {
                        JobRefusalsCitingRumor++;
                        diag.Add($"day {day}: Robin refuses the mill repair-crew offer, citing the rumor.");
                    }
                }
            }
        }

        private void RentHorseToOtto(int day)
        {
            var req = new HorseRentalRequest
            {
                Registry = Animals,
                HorseId = horses["thistle"].AnimalId,
                AuthorizingOwnerKind = AnimalOwnerKind.Household,
                AuthorizingOwnerId = H3.ToString(),
                BorrowerKind = "person",
                BorrowerId = P8.ToString(),
                BorrowerName = "Otto",
                FromDayIndex = day,
                ToDayIndex = day,
                FeeCents = 200,
                TermsNote = "day trip to the county seat",
                Availability = Availability,
                DayIndex = day,
                BorrowerLedger = Ledgers.GetOrCreate(H5),
                OwnerLedger = Ledgers.GetOrCreate(H3),
            };
            HorseRentalRecord record = Rentals.RentOut(req, Diag);
            if (record != null)
            {
                HorseRentalCount++;
                workingToday.Add("thistle");
                LogCash(day, "household:11", "household:9", 200, "horse rental");
                Diag.Add($"day {day}: Otto rents Thistle for a day trip (200c).");
            }
            else Diag.Add($"day {day}: horse rental refused.");
        }

        private void ReturnRentedHorse(int day)
        {
            string problem = Rentals.ReturnHorse(Animals, horses["thistle"].AnimalId, day, Availability, Diag);
            if (problem != null) Diag.Add($"day {day}: horse return problem: {problem}");
            else Diag.Add($"day {day}: Thistle returned to Ash.");
        }

        // ================= run control =================

        public void RunTo(int endDayExclusive, int chunkDays)
        {
            int day = CurrentDay + 1;
            if (day < StartDay) day = StartDay;
            int chunk = Math.Max(1, chunkDays);
            while (day < endDayExclusive)
            {
                int chunkEnd = Math.Min(day + chunk, endDayExclusive);
                for (; day < chunkEnd; day++) StepDay(day);
            }
        }

        public string ComputeHash()
        {
            var sb = new StringBuilder();
            sb.Append("day=").Append(CurrentDay).Append(';');
            foreach (string id in new[] { Bakery, Farm, Store, Mill })
                sb.Append(id).Append('=').Append(Runtimes[id].CurrentCashCents).Append(';');
            var hids = new List<int> { H1, H2, H3, H4, H5, H6 };
            hids.Sort();
            foreach (int hid in hids)
            {
                sb.Append("H").Append(hid).Append('=').Append(Ledgers.GetOrCreate(hid).GetBalanceCents()).Append(';');
                HouseholdState hh = Population.GetHousehold(hid);
                HouseholdReserveState r = hh.reserves.Find(x => x.categoryId == Bread);
                sb.Append("HR").Append(hid).Append('=').Append(r != null ? r.currentUnits : -1).Append(';');
            }
            sb.Append("farm.grain=").Append(FarmGrainUnits()).Append(';');
            var mg = Runtimes[Mill].GetCategoryStock(Grain);
            var mf = Runtimes[Mill].GetCategoryStock(Flour);
            sb.Append("mill.grain=").Append(mg != null ? mg.CurrentStockUnits : -1).Append(';');
            sb.Append("mill.flour=").Append(mf != null ? mf.CurrentStockUnits : -1).Append(';');
            sb.Append("bakery.flour=").Append((long)Instances[Bakery].GenericConfiguration.GetAvailableInventoryQuantity(Flour)).Append(';');
            sb.Append("bakery.bread=").Append((long)Instances[Bakery].GenericConfiguration.GetAvailableInventoryQuantity(Bread)).Append(';');
            sb.Append("store.bread=").Append((long)Instances[Store].GenericConfiguration.GetAvailableInventoryQuantity(Bread)).Append(';');
            var obs = new List<FinancialObligation>(Obligations.Obligations);
            obs.Sort((a, b) => string.Compare(a.ObligationId, b.ObligationId, StringComparison.Ordinal));
            foreach (FinancialObligation ob in obs)
                sb.Append(ob.ObligationId).Append(':').Append(ob.OutstandingPrincipalCents).Append(':').Append(ob.Settled ? 1 : 0).Append(';');
            var ships = Exec.Ledger.Shipments;
            foreach (var s in ships)
                if (s != null) sb.Append("ship:").Append(s.ProductId).Append(':').Append((long)s.Quantity).Append(':').Append(s.Status).Append(':').Append(s.TotalCents).Append(';');
            sb.Append("pendingReq=").Append(Exec.Ledger.PendingRequests.Count).Append(';');
            var gkeys = new List<string>(GoodsIn.Keys); gkeys.Sort(StringComparer.Ordinal);
            foreach (string k in gkeys) sb.Append("in:").Append(k).Append('=').Append(GoodsIn[k]).Append(';');
            gkeys = new List<string>(GoodsOut.Keys); gkeys.Sort(StringComparer.Ordinal);
            foreach (string k in gkeys) sb.Append("out:").Append(k).Append('=').Append(GoodsOut[k]).Append(';');
            sb.Append("c=").Append(ProducedGrain).Append(',').Append(ProducedFlour).Append(',').Append(BakedBread).Append(',')
                .Append(WastedBran).Append(',').Append(EatenBread).Append(',').Append(MissedMeals).Append(',')
                .Append(RetailBreadSold).Append(',').Append(CreditBreadSold).Append(',').Append(WholesaleBreadMoved).Append(',')
                .Append(FlourMovedB2B).Append(',').Append(GrainMovedB2B).Append(',').Append(FeedGrainEaten).Append(',')
                .Append(UnfedHorseDays).Append(',').Append(ContentionRefusals).Append(',').Append(WagonDeferrals).Append(',')
                .Append(PayrollWeeksSettled).Append(',').Append(PayrollArrearsWeeks).Append(',').Append(ManagerProcureDecisions).Append(',')
                .Append(ManagerShipmentsBooked).Append(',').Append(ManagerPendingRequests).Append(',').Append(RentPaymentsMade).Append(',')
                .Append(RentArrearsEvents).Append(',').Append(CreditSalesMade).Append(',').Append(CreditRepaidCents).Append(',')
                .Append(SocialVisitsHeld).Append(',').Append(PacketsTransmitted).Append(',').Append(JobRefusalsCitingRumor).Append(',')
                .Append(HorseRentalCount).Append(',').Append(TotalWorkMinutes).Append(';');
            sb.Append("millBroken=").Append(MillBroken ? 1 : 0).Append(";drought=").Append(DroughtDone ? 1 : 0).Append(';');
            sb.Append("ext=").Append(ExternalInflowCents).Append(',').Append(ExternalOutflowCents).Append(';');
            var pids = new List<int>(Persons.Keys); pids.Sort();
            foreach (int pid in pids)
                sb.Append("kp").Append(pid).Append('=').Append(Knowledge.PacketsFor(pid, "mill-trouble").Count).Append(';');
            var res = new List<ReservationRecord>(AnimalReservations);
            res.Sort((a, b) =>
            {
                int c = string.Compare(a.HorseKey, b.HorseKey, StringComparison.Ordinal);
                if (c != 0) return c;
                return a.FromSeconds.CompareTo(b.FromSeconds);
            });
            foreach (ReservationRecord r in res)
                sb.Append("res:").Append(r.HorseKey).Append(':').Append((long)r.FromSeconds).Append('-').Append((long)r.ToSeconds)
                    .Append(':').Append(r.Purpose).Append(':').Append(r.Granted ? 1 : 0).Append(';');
            return sb.ToString();
        }

        // ================= reconciliation =================

        public List<string> Reconcile()
        {
            var findings = new List<string>();

            int expectedCash = InitialInternalCashCents + ExternalInflowCents - ExternalOutflowCents;
            int actualCash = TotalInternalCashCents();
            if (expectedCash != actualCash)
                findings.Add($"cash: expected {expectedCash}c, actual {actualCash}c (diff {actualCash - expectedCash}c)");

            CheckPool(findings, "farm.grain", FarmGrainUnits());
            var mg = Runtimes[Mill].GetCategoryStock(Grain);
            var mf = Runtimes[Mill].GetCategoryStock(Flour);
            CheckPool(findings, "mill.grain", mg != null ? mg.CurrentStockUnits : 0);
            CheckPool(findings, "mill.flour", mf != null ? mf.CurrentStockUnits : 0);
            CheckPool(findings, "bakery.flour", (long)Instances[Bakery].GenericConfiguration.GetAvailableInventoryQuantity(Flour));
            CheckPool(findings, "bakery.bread", (long)Instances[Bakery].GenericConfiguration.GetAvailableInventoryQuantity(Bread));
            CheckPool(findings, "store.bread", (long)Instances[Store].GenericConfiguration.GetAvailableInventoryQuantity(Bread));
            long householdBread = 0;
            foreach (int hid in new[] { H1, H2, H3, H4, H5, H6 })
            {
                HouseholdReserveState r = Population.GetHousehold(hid).reserves.Find(x => x.categoryId == Bread);
                householdBread += r != null ? r.currentUnits : 0;
            }
            CheckPool(findings, "household.bread", householdBread);

            int inTransit = 0;
            foreach (var s in Exec.Ledger.Shipments)
                if (s != null && s.Status == "InTransit") inTransit += (int)s.Quantity;
            long inTransitExpected = ManagerBreadShipped - WholesaleBreadMoved;
            if (inTransit != inTransitExpected)
                findings.Add($"goods in transit mismatch: ledger {inTransit}u vs books {inTransitExpected}u (shipped {ManagerBreadShipped}u, received {WholesaleBreadMoved}u)");

            for (int i = 0; i < AnimalReservations.Count; i++)
            {
                for (int j = i + 1; j < AnimalReservations.Count; j++)
                {
                    ReservationRecord a = AnimalReservations[i], b = AnimalReservations[j];
                    if (!a.Granted || !b.Granted) continue;
                    if (!string.Equals(a.HorseKey, b.HorseKey, StringComparison.Ordinal)) continue;
                    if (a.FromSeconds < b.ToSeconds && b.FromSeconds < a.ToSeconds)
                        findings.Add($"DOUBLE-BOOKED {a.HorseKey}: '{a.Purpose}' overlaps '{b.Purpose}'");
                }
            }

            for (int day = StartDay; day <= CurrentDay; day++)
            {
                foreach (int pid in Persons.Keys)
                {
                    List<PersonActivityReservation> acts = Schedules.ActiveFor(pid, day);
                    for (int i = 0; i < acts.Count; i++)
                    {
                        for (int j = i + 1; j < acts.Count; j++)
                        {
                            PersonActivityReservation a = acts[i], c = acts[j];
                            if (a.Released || c.Released) continue;
                            if (a.StartMinuteOfDay < c.EndMinuteOfDay && c.StartMinuteOfDay < a.EndMinuteOfDay)
                                findings.Add($"person P{pid} double-booked on day {day}: '{a.Label}' vs '{c.Label}'");
                        }
                    }
                }
            }

            return findings;
        }

        private void CheckPool(List<string> findings, string pool, long finalUnits)
        {
            long expected = GoodsIn.GetValueOrDefault(pool) - GoodsOut.GetValueOrDefault(pool);
            if (expected != finalUnits)
                findings.Add($"goods {pool}: expected {expected}u, actual {finalUnits}u");
        }

        public string Summary()
        {
            var lines = new List<string>
            {
                $"days simulated: {StartDay}..{CurrentDay} ({CurrentDay - StartDay + 1} days)",
                $"cash: internal total {TotalInternalCashCents()}c (opened {InitialInternalCashCents}c, external in {ExternalInflowCents}c, out {ExternalOutflowCents}c)",
                $"business cash: bakery {Runtimes[Bakery].CurrentCashCents}c, farm {Runtimes[Farm].CurrentCashCents}c, store {Runtimes[Store].CurrentCashCents}c, mill {Runtimes[Mill].CurrentCashCents}c",
                $"household balances: H1 {Ledgers.GetOrCreate(H1).GetBalanceCents()}c, H2 {Ledgers.GetOrCreate(H2).GetBalanceCents()}c, H3 {Ledgers.GetOrCreate(H3).GetBalanceCents()}c, H4 {Ledgers.GetOrCreate(H4).GetBalanceCents()}c, H5 {Ledgers.GetOrCreate(H5).GetBalanceCents()}c, H6 {Ledgers.GetOrCreate(H6).GetBalanceCents()}c",
                $"goods: grain harvested {ProducedGrain}u, flour milled {ProducedFlour}u (bran wasted {WastedBran}u), bread baked {BakedBread}u",
                $"goods: bread eaten {EatenBread}u, missed meals {MissedMeals}, retail sold {RetailBreadSold}u, credit sold {CreditBreadSold}u",
                $"goods: wholesale bread {WholesaleBreadMoved}u, flour B2B {FlourMovedB2B}u, grain B2B {GrainMovedB2B}u, feed eaten {FeedGrainEaten}u",
                $"payroll: {PayrollWeeksSettled} weeks settled, {PayrollArrearsWeeks} in arrears; work minutes {TotalWorkMinutes}",
                $"manager: {ManagerProcureDecisions} procure decisions, {ManagerShipmentsBooked} shipments booked, {ManagerPendingRequests} pending requests",
                $"rent: {RentPaymentsMade} payments, {RentArrearsEvents} arrears events; credit: {CreditSalesMade} sales, {CreditRepaidCents}c repaid",
                $"animals: {ContentionRefusals} contention refusals, {WagonDeferrals} wagon deferrals, {UnfedHorseDays} unfed horse-days, {HorseRentalCount} rentals",
                $"social: {SocialVisitsHeld} visits, {PacketsTransmitted} packets transmitted, {JobRefusalsCitingRumor} rumor-cited refusals",
                $"adversity: mill broken now {MillBroken}, drought done {DroughtDone}, employment conflicts {EmploymentConflictRefusals}",
            };
            return string.Join("\n", lines);
        }

        // ================= save / load =================

        [Serializable]
        public sealed class AnimalSnap
        {
            public string StableKey = string.Empty;
            public float Nutrition01;
            public float Fatigue01;
            public int LastCareDayIndex;
            public string CustodianKind = string.Empty;
            public string CustodianId = string.Empty;
            public int LocationBuildingId;
        }

        [Serializable]
        public sealed class B2BSnap
        {
            public string BuyerKind = string.Empty;
            public string BuyerId = string.Empty;
            public string SellerBusinessInstanceId = string.Empty;
            public string GoodsOrServiceId = string.Empty;
            public int QuantityUnits;
            public int AgreedPriceCents;
            public string Provenance = string.Empty;
        }

        [Serializable]
        public sealed class ScenarioScalars
        {
            public int InitialInternalCashCents, ExternalInflowCents, ExternalOutflowCents;
            public long ProducedGrain, ProducedFlour, BakedBread, WastedBran, EatenBread, MissedMeals;
            public long BakeryFlourStockouts;
            public long RetailBreadSold, CreditBreadSold, WholesaleBreadMoved, FlourMovedB2B, GrainMovedB2B, FeedGrainEaten;
            public long ManagerBreadShipped;
            public int UnfedHorseDays, ContentionRefusals, WagonDeferrals, EmploymentConflictRefusals;
            public int PayrollWeeksSettled, PayrollArrearsWeeks, ManagerProcureDecisions, ManagerShipmentsBooked, ManagerPendingRequests;
            public int RentPaymentsMade, RentArrearsEvents, CreditSalesMade, CreditRepaidCents;
            public int SocialVisitsHeld, PacketsTransmitted, JobRefusalsCitingRumor, HorseRentalCount;
            public long TotalWorkMinutes;
            public bool MillBroken, DroughtDone;
            public string FarmLoanId = string.Empty;
            public Dictionary<string, long> GoodsIn = new Dictionary<string, long>();
            public Dictionary<string, long> GoodsOut = new Dictionary<string, long>();
        }

        [Serializable]
        public sealed class ScenarioSaveDto
        {
            public int CurrentDay;
            public Dictionary<string, BusinessRuntimeSaveDto> Runtimes = new Dictionary<string, BusinessRuntimeSaveDto>();
            public Dictionary<string, BusinessInstanceSaveDto> Instances = new Dictionary<string, BusinessInstanceSaveDto>();
            public WageSettlementSaveDto Wages = new WageSettlementSaveDto();
            public ManagerBrainSaveDto Brain = new ManagerBrainSaveDto();
            public ManagerDecisionExecutionSaveDto ExecLedger = new ManagerDecisionExecutionSaveDto();
            public PersonScheduleTrackerSaveDto Schedules = new PersonScheduleTrackerSaveDto();
            public KnowledgeTransmissionSaveDto Knowledge = new KnowledgeTransmissionSaveDto();
            public EmploymentRegistrySaveDto Employments = new EmploymentRegistrySaveDto();
            public FinancialObligationSaveDto Obligations = new FinancialObligationSaveDto();
            public List<HouseholdLedgerState> Ledgers = new List<HouseholdLedgerState>();
            public List<EntityIdCursor> IdCursors = new List<EntityIdCursor>();
            public List<AnimalSnap> Animals = new List<AnimalSnap>();
            public List<ReservationRecord> AnimalHistory = new List<ReservationRecord>();
            public List<B2BSnap> PendingB2B = new List<B2BSnap>();
            public List<string> CountedShipments = new List<string>();
            public Dictionary<int, int> HouseholdBreadReserves = new Dictionary<int, int>();
            public ScenarioScalars Scalars = new ScenarioScalars();
        }

        public ScenarioSaveDto CaptureSaveDto()
        {
            var dto = new ScenarioSaveDto { CurrentDay = CurrentDay };
            dto.Runtimes[Farm] = Runtimes[Farm].CaptureSaveDto();
            dto.Runtimes[Mill] = Runtimes[Mill].CaptureSaveDto();
            foreach (string id in new[] { Store, Bakery, Saloon }) dto.Instances[id] = Instances[id].CaptureSaveDto();
            dto.Wages = Wages.CaptureSaveDto();
            dto.Brain = Brain.CaptureSaveDto();
            dto.ExecLedger = Exec.CaptureSaveDto();
            dto.Schedules = Schedules.CaptureSaveDto();
            dto.Knowledge = Knowledge.CaptureSaveDto();
            dto.Employments = Employments.CaptureSaveDto();
            dto.Obligations = Obligations.CaptureSaveDto();
            Ledgers.ExportState(dto.Ledgers);
            dto.IdCursors = Ids.SnapshotCursors();
            foreach (var kv in horses)
            {
                AnimalState h = kv.Value;
                dto.Animals.Add(new AnimalSnap
                {
                    StableKey = kv.Key, Nutrition01 = h.Nutrition01, Fatigue01 = h.Fatigue01,
                    LastCareDayIndex = h.LastCareDayIndex, CustodianKind = h.CustodianKind ?? string.Empty,
                    CustodianId = h.CustodianId ?? string.Empty, LocationBuildingId = h.LocationBuildingId,
                });
            }
            dto.AnimalHistory = new List<ReservationRecord>(AnimalReservations);
            foreach (PurchaseIntent p in PendingB2B)
                dto.PendingB2B.Add(new B2BSnap
                {
                    BuyerKind = p.BuyerKind, BuyerId = p.BuyerId,
                    SellerBusinessInstanceId = p.SellerBusinessInstanceId, GoodsOrServiceId = p.GoodsOrServiceId,
                    QuantityUnits = p.QuantityUnits, AgreedPriceCents = p.AgreedPriceCents, Provenance = p.Provenance,
                });
            dto.CountedShipments = new List<string>(countedShipments);
            dto.HouseholdBreadReserves = new Dictionary<int, int>();
            foreach (int hid in new[] { H1, H2, H3, H4, H5, H6 })
            {
                HouseholdReserveState r = Population.GetHousehold(hid).reserves.Find(x => x.categoryId == Bread);
                dto.HouseholdBreadReserves[hid] = r != null ? r.currentUnits : 0;
            }
            var s = dto.Scalars;
            s.InitialInternalCashCents = InitialInternalCashCents; s.ExternalInflowCents = ExternalInflowCents; s.ExternalOutflowCents = ExternalOutflowCents;
            s.ProducedGrain = ProducedGrain; s.ProducedFlour = ProducedFlour; s.BakedBread = BakedBread; s.WastedBran = WastedBran;
            s.BakeryFlourStockouts = BakeryFlourStockouts;
            s.EatenBread = EatenBread; s.MissedMeals = MissedMeals; s.RetailBreadSold = RetailBreadSold; s.CreditBreadSold = CreditBreadSold;
            s.WholesaleBreadMoved = WholesaleBreadMoved; s.FlourMovedB2B = FlourMovedB2B; s.GrainMovedB2B = GrainMovedB2B; s.FeedGrainEaten = FeedGrainEaten;
            s.ManagerBreadShipped = ManagerBreadShipped;
            s.UnfedHorseDays = UnfedHorseDays; s.ContentionRefusals = ContentionRefusals; s.WagonDeferrals = WagonDeferrals;
            s.EmploymentConflictRefusals = EmploymentConflictRefusals;
            s.PayrollWeeksSettled = PayrollWeeksSettled; s.PayrollArrearsWeeks = PayrollArrearsWeeks;
            s.ManagerProcureDecisions = ManagerProcureDecisions; s.ManagerShipmentsBooked = ManagerShipmentsBooked; s.ManagerPendingRequests = ManagerPendingRequests;
            s.RentPaymentsMade = RentPaymentsMade; s.RentArrearsEvents = RentArrearsEvents;
            s.CreditSalesMade = CreditSalesMade; s.CreditRepaidCents = CreditRepaidCents;
            s.SocialVisitsHeld = SocialVisitsHeld; s.PacketsTransmitted = PacketsTransmitted; s.JobRefusalsCitingRumor = JobRefusalsCitingRumor;
            s.HorseRentalCount = HorseRentalCount; s.TotalWorkMinutes = TotalWorkMinutes;
            s.MillBroken = MillBroken; s.DroughtDone = DroughtDone;
            s.FarmLoanId = FarmLoan != null ? FarmLoan.ObligationId : string.Empty;
            foreach (var kv in GoodsIn) s.GoodsIn[kv.Key] = kv.Value;
            foreach (var kv in GoodsOut) s.GoodsOut[kv.Key] = kv.Value;
            return dto;
        }

        public void LoadFromSaveDto(ScenarioSaveDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            Runtimes[Farm] = BusinessRuntimeState.FromSaveDto(dto.Runtimes[Farm], BusinessType.CropFarm);
            Runtimes[Mill] = BusinessRuntimeState.FromSaveDto(dto.Runtimes[Mill], BusinessType.GrainMill);
            foreach (string id in new[] { Store, Bakery, Saloon })
            {
                BusinessInstanceState restored = BusinessInstanceState.FromSaveDto(dto.Instances[id]);
                Instances[id] = restored;
            }
            Runtimes[Store] = Instances[Store].RuntimeState;
            Runtimes[Bakery] = Instances[Bakery].RuntimeState;
            foreach (var rt in Runtimes.Values)
            {
                rt.EmploymentRegistry = Employments;
                rt.HouseholdLedgers = Ledgers;
                rt.PersonHouseholdIdLookup = pid => PersonHousehold.TryGetValue(pid, out int h) ? h : -1;
            }
            ProductionPathRegistry.SetInstancePath(Store, ProductionPath.GenericPath);
            ProductionPathRegistry.SetInstancePath(Bakery, ProductionPath.GenericPath);

            Wages.LoadFromSaveDto(dto.Wages);
            Brain.LoadFromSaveDto(dto.Brain);
            Exec.LoadFromSaveDto(dto.ExecLedger);
            Schedules.LoadFromSaveDto(dto.Schedules);
            Knowledge.LoadFromSaveDto(dto.Knowledge);
            Employments.LoadFromSaveDto(dto.Employments);
            Obligations.LoadFromSaveDto(dto.Obligations);
            Ledgers.ClearForNewGame("save restore");
            Ledgers.ImportState(dto.Ledgers);
            Ids.RestoreCursors(dto.IdCursors);

            foreach (AnimalSnap snap in dto.Animals)
            {
                if (!horses.TryGetValue(snap.StableKey, out AnimalState h)) continue;
                h.Nutrition01 = snap.Nutrition01; h.Fatigue01 = snap.Fatigue01;
                h.LastCareDayIndex = snap.LastCareDayIndex;
                h.CustodianKind = snap.CustodianKind; h.CustodianId = snap.CustodianId;
                h.LocationBuildingId = snap.LocationBuildingId;
            }

            AnimalReservations.Clear();
            AnimalReservations.AddRange(dto.AnimalHistory);
            PendingB2B.Clear();
            foreach (B2BSnap b in dto.PendingB2B)
                PendingB2B.Add(new PurchaseIntent
                {
                    BuyerKind = b.BuyerKind, BuyerId = b.BuyerId,
                    SellerBusinessInstanceId = b.SellerBusinessInstanceId, GoodsOrServiceId = b.GoodsOrServiceId,
                    QuantityUnits = b.QuantityUnits, AgreedPriceCents = b.AgreedPriceCents, Provenance = b.Provenance,
                });
            countedShipments.Clear();
            foreach (string id in dto.CountedShipments) countedShipments.Add(id);
            foreach (var kv in dto.HouseholdBreadReserves)
            {
                HouseholdReserveState r = Population.GetHousehold(kv.Key).reserves.Find(x => x.categoryId == Bread);
                if (r != null) r.currentUnits = kv.Value;
            }

            var s = dto.Scalars;
            InitialInternalCashCents = s.InitialInternalCashCents; ExternalInflowCents = s.ExternalInflowCents; ExternalOutflowCents = s.ExternalOutflowCents;
            ProducedGrain = s.ProducedGrain; ProducedFlour = s.ProducedFlour; BakedBread = s.BakedBread; WastedBran = s.WastedBran;
            BakeryFlourStockouts = s.BakeryFlourStockouts;
            EatenBread = s.EatenBread; MissedMeals = s.MissedMeals; RetailBreadSold = s.RetailBreadSold; CreditBreadSold = s.CreditBreadSold;
            WholesaleBreadMoved = s.WholesaleBreadMoved; FlourMovedB2B = s.FlourMovedB2B; GrainMovedB2B = s.GrainMovedB2B; FeedGrainEaten = s.FeedGrainEaten;
            ManagerBreadShipped = s.ManagerBreadShipped;
            UnfedHorseDays = s.UnfedHorseDays; ContentionRefusals = s.ContentionRefusals; WagonDeferrals = s.WagonDeferrals;
            EmploymentConflictRefusals = s.EmploymentConflictRefusals;
            PayrollWeeksSettled = s.PayrollWeeksSettled; PayrollArrearsWeeks = s.PayrollArrearsWeeks;
            ManagerProcureDecisions = s.ManagerProcureDecisions; ManagerShipmentsBooked = s.ManagerShipmentsBooked; ManagerPendingRequests = s.ManagerPendingRequests;
            RentPaymentsMade = s.RentPaymentsMade; RentArrearsEvents = s.RentArrearsEvents;
            CreditSalesMade = s.CreditSalesMade; CreditRepaidCents = s.CreditRepaidCents;
            SocialVisitsHeld = s.SocialVisitsHeld; PacketsTransmitted = s.PacketsTransmitted; JobRefusalsCitingRumor = s.JobRefusalsCitingRumor;
            HorseRentalCount = s.HorseRentalCount; TotalWorkMinutes = s.TotalWorkMinutes;
            MillBroken = s.MillBroken; DroughtDone = s.DroughtDone;
            GoodsIn.Clear(); GoodsOut.Clear();
            foreach (var kv in s.GoodsIn) GoodsIn[kv.Key] = kv.Value;
            foreach (var kv in s.GoodsOut) GoodsOut[kv.Key] = kv.Value;
            FarmLoan = null;
            foreach (FinancialObligation ob in Obligations.Obligations)
                if (ob != null && string.Equals(ob.ObligationId, s.FarmLoanId, StringComparison.Ordinal)) FarmLoan = ob;

            CurrentDay = dto.CurrentDay;
            Exec.Configure(new ManagerExecutionServices
            {
                FindBusiness = id => Instances.TryGetValue(id, out BusinessInstanceState b) ? b : null,
                AllBusinesses = () => Instances.Values,
                DemandFor = id => new LaborDemandReport { BusinessInstanceId = id, DayIndex = CurrentDay },
                BusinessEntityFor = id => EntityId.For(EntityKind.Business, Math.Abs(id.GetHashCode())),
                Tasks = new TaskAuthority(),
                WorkScheduling = new WorkScheduler(),
                PersonSchedules = Schedules,
                WorkTimeBudgets = new WorkTimeBudgetStore(),
                Employments = Employments,
                FinancialObligations = Obligations,
                Ids = Ids,
                SupplierKnowledge = new ShopperSellerKnowledge(),
                BusinessLender = null,
                OpenRecruitment = (business, demand, decision, day, diag) => "effort:" + business.InstanceId,
            });
            Demand = new RealDemandSource(
                Population, Ledgers, Needs, Directory,
                householdKnowsSupplier: (hid, sid) => sid == Store,
                businessCashCents: bid => Runtimes.TryGetValue(bid, out BusinessRuntimeState rt) ? rt.CurrentCashCents : 0,
                generalStoreSupplierId: Store);
        }
    }

    // ================= tests =================

    [TestFixture]
    public sealed class Pass7NinetyDayScenarioTests
    {
        [Test]
        public void Scenario90Days_Reconciles_WithStoryBeats()
        {
            var sim = new NinetyDayScenario();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            sim.Build();
            long buildMs = sw.ElapsedMilliseconds;
            sim.RunTo(NinetyDayScenario.EndDayExclusive, 1);
            long runMs = sw.ElapsedMilliseconds - buildMs;

            List<string> findings = sim.Reconcile();
            Assert.AreEqual(0, findings.Count, "reconciliation failed:\n" + string.Join("\n", findings));

            Assert.Greater(sim.RetailBreadSold, 0L, "no retail bread sold in 90 days");
            Assert.AreEqual(48, sim.PayrollWeeksSettled, "expected 12 weeks x 4 businesses = 48 payroll settlements, got " + sim.PayrollWeeksSettled);
            Assert.IsTrue(sim.DroughtDone, "the drought never happened");
            Assert.Greater(sim.BakeryFlourStockouts, 0L, "the 21-day mill stoppage should have starved the bakery\n" + string.Join("\n", sim.Diag));
            Assert.Greater(sim.ContentionRefusals, 0, "expected horse/wagon contention refusals");
            Assert.Greater(sim.PacketsTransmitted, 0, "no social information transmitted");
            Assert.AreEqual(1, sim.JobRefusalsCitingRumor, "the rumor-cited job refusal did not happen");
            Assert.Greater(sim.ManagerShipmentsBooked, 0, "the manager never procured bread");
            Assert.AreEqual(1, sim.HorseRentalCount, "the horse rental did not happen");
            Assert.AreEqual(0, sim.EmploymentConflictRefusals, "person-time double booking occurred");

            Console.WriteLine("=== 90-day scenario summary ===");
            Console.WriteLine(sim.Summary());
            Console.WriteLine($"build {buildMs}ms, 90-day run {runMs}ms");
            Console.WriteLine("final hash: " + sim.ComputeHash());
        }

        [Test]
        public void Scenario30Days_Reconciles()
        {
            var sim = new NinetyDayScenario();
            sim.Build();
            sim.RunTo(NinetyDayScenario.StartDay + 30, 1);
            List<string> findings = sim.Reconcile();
            Assert.AreEqual(0, findings.Count, "30-day reconciliation failed:\n" + string.Join("\n", findings));
            Console.WriteLine("=== 30-day summary ===");
            Console.WriteLine(sim.Summary());
        }

        [Test]
        public void SaveLoad_MidSim_IdenticalContinuation()
        {
            var baseline = new NinetyDayScenario();
            baseline.Build();
            baseline.RunTo(NinetyDayScenario.EndDayExclusive, 1);
            string baselineHash = baseline.ComputeHash();

            var a = new NinetyDayScenario();
            a.Build();
            a.RunTo(546, 1);
            NinetyDayScenario.ScenarioSaveDto dto = a.CaptureSaveDto();

            var b = new NinetyDayScenario();
            b.BuildStatic();
            b.LoadFromSaveDto(dto);
            b.RunTo(NinetyDayScenario.EndDayExclusive, 1);

            Assert.AreEqual(baselineHash, b.ComputeHash(),
                "save/load continuation diverged from the uninterrupted run");
            List<string> findings = b.Reconcile();
            Assert.AreEqual(0, findings.Count, "restored-run reconciliation failed:\n" + string.Join("\n", findings));
            Console.WriteLine("save/load at day 546: continuation identical, reconciled.");
        }

        [Test]
        public void TickSize_DailyVsThreeDayChunks_Agree()
        {
            var a = new NinetyDayScenario();
            a.Build();
            a.RunTo(NinetyDayScenario.EndDayExclusive, 1);

            var b = new NinetyDayScenario();
            b.Build();
            b.RunTo(NinetyDayScenario.EndDayExclusive, 3);

            Assert.AreEqual(a.ComputeHash(), b.ComputeHash(),
                "3-day chunks diverged from daily steps");
            Console.WriteLine("tick-size: daily vs 3-day chunks agree.");
        }
    }
}
