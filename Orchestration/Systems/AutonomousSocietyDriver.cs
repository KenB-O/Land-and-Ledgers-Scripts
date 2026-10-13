using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Estates;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Labor;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using LandLedgers.World.Property;
using UnityEngine;

using EntityId = LandLedgers.Primitives.EntityId;

namespace LandLedgers.Orchestration.Systems
{
    /// <summary>
    /// Pass V (Worker 4, brief section 40): a bounded, deterministic, fully
    /// reconcilable autonomous society simulation. 5 households, 13 persons,
    /// 4 businesses (grist mill, bakery, general store, blacksmith forge),
    /// housing, employment, credit, real inventories, multiple task types,
    /// shared equipment (wagon-1, communal oven), a delegated store manager,
    /// and constrained supply/demand.
    ///
    /// Every economic movement goes through the real authorities: HouseholdLedger
    /// (the ONE household cash writer), WageSettlementService + EmploymentRelationship
    /// (the wage path), TaskAuthority + WorkTimeBudgetStore (person-time),
    /// GenericProductionRuntime (production), HouseholdInventory lots and
    /// GenericInventoryPosition (real goods), FinancialObligationAuthority (credit),
    /// EquipmentReservationService (shared equipment), OffMapJourneyAuthority
    /// (off-map continuity), and the hub DueEventScheduler (birthdays).
    /// This driver OWNS no money, goods, time, or identity: it only sequences
    /// the authorities. It is fully deterministic: no RNG in the daily step.
    ///
    /// The clock is tick-size agnostic: AdvanceMinutes accumulates minutes and a
    /// day step runs each time a day boundary is crossed, so 15-minute ticks,
    /// hourly ticks, daily ticks, and weekly ticks produce identical outcomes.
    /// </summary>
    public sealed class AutonomousSocietyDriver
    {
        // Gregorian anchor: absoluteDayIndex 0 = 1880-03-01 (matches the P0-1 plan default).
        public const int AnchorYear = 1880;
        public const int AnchorMonth = 3;
        public const int AnchorDay = 1;
        public const int MinutesPerDay = 1440;
        public const int WorkShiftMinutes = 480;
        public const int MaxEventLog = 200;

        // Business ids.
        public const string MillId = "biz-mill";
        public const string BakeryId = "biz-bakery";
        public const string StoreId = "biz-store";
        public const string ForgeId = "biz-forge";

        // Shared equipment keys.
        public const string WagonAssetKey = "asset:wagon-1";
        public const string OvenAssetKey = "asset:communal-oven";

        // Person ids (stable for the standard scenario).
        public const int P1Miller = 1;
        public const int P2Anna = 2;
        public const int P3Jonas = 3;
        public const int P4Marta = 4;
        public const int P5Baker = 5;
        public const int P6Clerk = 6;
        public const int P7Tessa = 7;
        public const int P8Teamster = 8;
        public const int P9Smith = 9;
        public const int P10Ingrid = 10;
        public const int P11Widow = 11;
        public const int P12Lena = 12;
        public const int P13Elder = 13;

        // Household ids.
        public const int H1Miller = 1;
        public const int H2Baker = 2;
        public const int H3Teamster = 3;
        public const int H4Smith = 4;
        public const int H5Widow = 5;

        // --- clock ---
        private int absoluteDayIndex;
        private int tickMinuteAccumulator;

        // --- population authorities ---
        private PopulationState population;
        private HouseholdMembershipRegistry memberships;
        private KinshipRegistry kinship;
        private EntityIdRegistry ids;
        private HouseholdLifecycleManager lifecycle;
        private readonly Dictionary<int, BirthDate> birthDates = new Dictionary<int, BirthDate>();

        // --- economy authorities ---
        private readonly Dictionary<int, HouseholdLedger> ledgers = new Dictionary<int, HouseholdLedger>();
        private readonly Dictionary<int, HouseholdInventory> inventories = new Dictionary<int, HouseholdInventory>();
        private readonly Dictionary<string, BusinessRuntimeState> businesses = new Dictionary<string, BusinessRuntimeState>();
        private readonly Dictionary<string, Dictionary<string, GenericInventoryPosition>> businessStock =
            new Dictionary<string, Dictionary<string, GenericInventoryPosition>>();
        private readonly Dictionary<string, GenericProductionRuntime> productionRuntimes =
            new Dictionary<string, GenericProductionRuntime>();
        private readonly Dictionary<string, ProductionMethodDefinition> methods =
            new Dictionary<string, ProductionMethodDefinition>();
        private readonly Dictionary<string, GenericProductionPolicy> policies =
            new Dictionary<string, GenericProductionPolicy>();
        private readonly Dictionary<string, ProductionProcessState> activeProcesses =
            new Dictionary<string, ProductionProcessState>();
        private EmploymentRelationshipRegistry employments;
        private HouseholdLedgerRegistry ledgerRegistry;
        private WageSettlementService wageService;
        private TaskAuthority tasks;
        private WorkTimeBudgetStore budgets;
        private FinancialObligationAuthority obligations;
        private EquipmentReservationService equipment;
        private JourneyModel journeyModel;
        private OffMapJourneyAuthority offMap;
        private DueEventScheduler dueEvents;
        private EstateService estates;
        private TitleAuthority titles;

        // --- scenario config ---
        private bool demographicEvents;
        private bool p11Hired;
        private bool birthDone;
        private bool migrationDone;
        private bool deathDone;
        // Multi-year arc (section 41): years 2-3 of the same continuous run.
        private bool birth2Done;
        private bool p6Promoted;
        private bool h6Formed;
        private int h6Id = -1;
        private bool birth3Done;
        private bool p11DeathDone;
        private bool birth4Done;
        private bool p3ForgeHired;
        private bool p8LongHaulDone;
        private bool p6BakeryHired;
        private int eligibilityReviewsDone;
        private string h5CreditObligationId = string.Empty;
        private int nextMembershipId = 1000;

        // --- reconciliation counters ---
        private long initialCashCents;
        private readonly Dictionary<string, long> initialGoods = new Dictionary<string, long>();
        private long externalCashOutflowsCents;
        private long externalCashInflowsCents;
        private readonly Dictionary<string, long> producedUnits = new Dictionary<string, long>();
        private readonly Dictionary<string, long> productionInputsConsumed = new Dictionary<string, long>();
        private readonly Dictionary<string, long> consumedUnits = new Dictionary<string, long>();
        private readonly Dictionary<string, long> boughtOffMapUnits = new Dictionary<string, long>();
        private readonly Dictionary<string, long> soldOffMapUnits = new Dictionary<string, long>();
        private readonly Dictionary<int, int> personMinutesWorked = new Dictionary<int, int>();
        private int unmetNeeds;
        private int shortages;
        private int hungerEvents;
        private int payrollArrearsWeeks;
        private int creditDelinquencies;
        private int rentArrearsCents;
        private int equipmentContentions;
        private int wagesPaidCents;
        private int birthsRecorded;
        private int deathsRecorded;
        private int birthdaysProcessed;

        private readonly List<string> eventLog = new List<string>();
        private readonly List<string> diag = new List<string>();

        public int AbsoluteDayIndex => absoluteDayIndex;
        public IReadOnlyList<string> EventLog => eventLog;
        public IReadOnlyList<string> Diagnostics => diag;
        public int UnmetNeeds => unmetNeeds;
        public int Shortages => shortages;
        public int HungerEvents => hungerEvents;
        public int PayrollArrearsWeeks => payrollArrearsWeeks;
        public int CreditDelinquencies => creditDelinquencies;
        public int EquipmentContentions => equipmentContentions;
        public int WagesPaidCents => wagesPaidCents;
        public int PersonCount => population != null ? population.people.Count : 0;
        public int BirthsRecorded => birthsRecorded;
        public int DeathsRecorded => deathsRecorded;
        public int BirthdaysProcessed => birthdaysProcessed;
        public int EligibilityReviewsDone => eligibilityReviewsDone;
        public int H6HouseholdId => h6Id;
        public int GetActiveHouseholdId(int personId) => memberships.GetActiveHouseholdId(personId);
        public EmploymentLifecycleState GetEmploymentState(string employmentId)
        {
            EmploymentRelationship rel;
            return employments.TryGetById(employmentId, out rel) && rel != null
                ? rel.LifecycleState
                : EmploymentLifecycleState.Ended;
        }
        public int GetBizCash(string bizId) => businesses.ContainsKey(bizId) ? businesses[bizId].CurrentCashCents : -999;
        public int GetHouseholdLedgerEntryCount(int householdId)
            => ledgers.ContainsKey(householdId) ? ledgers[householdId].Entries.Count : -1;

        /// <summary>
        /// P3-1: builds the developer-facing causal profile for a person from
        /// the authorities. Age is derived from the simulation date and the
        /// birth-date authority; nothing is cached.
        /// </summary>
        public LandLedgers.Reporting.PersonCausalProfile BuildCausalProfile(int personId)
        {
            PersonState person = FindPerson(personId);
            if (person == null)
            {
                return new LandLedgers.Reporting.PersonCausalProfile();
            }
            BirthDate? birth = null;
            BirthDate bd;
            if (birthDates.TryGetValue(personId, out bd))
            {
                birth = bd;
            }
            CurrentDate(out int y, out int m, out int d);
            string origin = birth.HasValue && birth.Value.ProvenanceNote == "simulated birth"
                ? "simulated birth"
                : "authored scenario";
            return LandLedgers.Reporting.PersonCausalProfileBuilder.Build(
                person, birth, y, m, d, absoluteDayIndex, origin,
                memberships, population, employments, obligations);
        }
        public int HouseholdCount => population != null ? population.households.Count : 0;
        public bool P11Hired => p11Hired;
        public bool IsPersonOffMap(int personId) => offMap.IsOffMapPerson(personId);
        public PersonState GetPerson(int personId) => FindPerson(personId);
        public int ActiveOffMapJourneyCount => offMap.ActiveJourneyCount;

        private AutonomousSocietyDriver() { }

        private void Log(string message)
        {
            eventLog.Add($"day {absoluteDayIndex}: {message}");
            if (eventLog.Count > MaxEventLog)
            {
                eventLog.RemoveAt(0);
            }
        }

        private void Bump(Dictionary<string, long> counter, string item, long units)
        {
            long have;
            counter.TryGetValue(item, out have);
            counter[item] = have + units;
        }

        // =====================================================================
        // Scenario construction
        // =====================================================================

        /// <summary>
        /// Builds the standard section-40 society: 5 households, 13 persons,
        /// 4 businesses. When demographicEvents is true, the run also scripts a
        /// hiring, a birth, a migration, and a death with estate settlement.
        /// </summary>
        public static AutonomousSocietyDriver BuildStandard(bool demographicEvents)
        {
            var driver = new AutonomousSocietyDriver { demographicEvents = demographicEvents };
            driver.Construct();
            return driver;
        }

        private void Construct()
        {
            population = new PopulationState();
            memberships = new HouseholdMembershipRegistry();
            kinship = new KinshipRegistry();
            ids = new EntityIdRegistry();
            lifecycle = new HouseholdLifecycleManager(population, memberships, kinship, ids);
            employments = new EmploymentRelationshipRegistry();
            ledgerRegistry = new HouseholdLedgerRegistry();
            wageService = new WageSettlementService();
            tasks = new TaskAuthority();
            budgets = new WorkTimeBudgetStore();
            obligations = new FinancialObligationAuthority();
            equipment = new EquipmentReservationService();
            // The shared assets must be registered before anyone reserves them:
            // an unregistered asset reads as "missing usable equipment".
            equipment.RegisterResourceCapacity(OvenAssetKey, 1f);
            equipment.RegisterResourceCapacity(WagonAssetKey, 1f);
            dueEvents = new DueEventScheduler();
            estates = new EstateService();
            titles = new TitleAuthority();
            titles.RegisterParcel("parcel-h4-forge", "Forge homestead parcel", 40f,
                "Old Tomas Forge", TitleBasis.HomesteadClaim, 0, diag);
            journeyModel = BuildJourneyModel();
            offMap = new OffMapJourneyAuthority(journeyModel);

            tasks.RegisterDefinition(new TaskDefinition("work-shift", "Work shift", WorkShiftMinutes), out _);
            tasks.RegisterDefinition(new TaskDefinition("domestic-work", "Domestic work", 240), out _);
            tasks.RegisterDefinition(new TaskDefinition("domestic-help", "Domestic help", 120), out _);
            JourneyTravel.RegisterTaskDefinitions(tasks);

            BuildHouseholdsAndPersons();
            // The authored persons/households use ids 1-13 / 1-5 directly, so
            // the population allocators must start past them: otherwise
            // RecordBirth/FormHousehold reuses live ids (duplicate persons).
            population.RestorePersonIdAllocator(14);
            population.RestoreHouseholdIdAllocator(6);
            BuildBusinesses();
            BuildEmployments();
            BuildCredit();
            ScheduleAllBirthdays(0);

            // Snapshot the opening position for reconciliation.
            initialCashCents = TotalCashCents();
            foreach (string item in AllGoods())
            {
                initialGoods[item] = TotalGoodsUnits(item);
            }
            Log($"society opened: {population.people.Count} persons, {population.households.Count} households, " +
                $"{businesses.Count} businesses, opening cash {initialCashCents}c.");
        }

        private void AddPerson(int id, string first, string last, int householdId,
            BirthDate birth, string occupation, int homeBuildingId)
        {
            var person = new PersonState
            {
                id = id,
                firstName = first,
                lastName = last,
                age = AgeAuthority.CompletedYears(birth, AnchorYear, AnchorMonth, AnchorDay),
                ageBand = HouseholdLifecycleManager.AgeBandForAge(
                    AgeAuthority.CompletedYears(birth, AnchorYear, AnchorMonth, AnchorDay)),
                professionName = occupation ?? string.Empty,
                homeBuildingId = homeBuildingId,
                deathDayIndex = -1,
            };
            population.people.Add(person);
            ids.SeedKind(EntityKind.Person, id + 1);
            birthDates[id] = birth;

            var membership = new HouseholdMembership
            {
                MembershipId = nextMembershipId++,
                PersonId = id,
                HouseholdId = householdId,
                Lifecycle = HouseholdMembershipLifecycle.Active,
                StartDayIndex = 0,
                EndDayIndex = -1,
                Source = HouseholdMembershipSource.Authored,
            };
            memberships.Register(membership);

            // Person-time budget exists from day 0.
            budgets.EnsureDay(0);
            budgets.GetOrCreate(EntityId.For(EntityKind.Person, id));
            personMinutesWorked[id] = 0;
        }

        private void AddHousehold(int id, string name, string surname, int openingCashCents, int homeBuildingId)
        {
            var household = new HouseholdState
            {
                id = id,
                householdName = name,
                surname = surname,
            };
            population.households.Add(household);
            ids.SeedKind(EntityKind.Household, id + 1);

            HouseholdLedger ledger = ledgerRegistry.GetOrCreate(id);
            ledger.RecordInflow(0, openingCashCents, HouseholdIncomeSource.OtherDocumented,
                "opening", "opening household cash", "scenario");
            ledgers[id] = ledger;
            inventories[id] = new HouseholdInventory(id);
        }

        private static BirthDate Born(int y, int m, int d)
        {
            return new BirthDate(y, m, d, BirthDateProvenance.GeneratedExact, "section-40 scenario");
        }

        private void BuildHouseholdsAndPersons()
        {
            AddHousehold(H1Miller, "Miller household", "Miller", 3000, 101);
            AddHousehold(H2Baker, "Baker household", "Baker", 2500, 102);
            AddHousehold(H3Teamster, "Reyes household", "Reyes", 1200, 103);
            AddHousehold(H4Smith, "Forge household", "Forge", 2800, 104);
            AddHousehold(H5Widow, "Voss household", "Voss", 400, 105);

            AddPerson(P1Miller, "Tomas", "Miller", H1Miller, Born(1846, 2, 10), "miller", 101);
            AddPerson(P2Anna, "Anna", "Miller", H1Miller, Born(1849, 6, 22), "homemaker", 101);
            AddPerson(P3Jonas, "Jonas", "Miller", H1Miller, Born(1866, 1, 15), "mill help", 101);
            AddPerson(P4Marta, "Marta", "Miller", H1Miller, Born(1872, 9, 3), "child", 101);
            AddPerson(P5Baker, "Piotr", "Baker", H2Baker, Born(1851, 4, 18), "baker", 102);
            AddPerson(P6Clerk, "Elena", "Baker", H2Baker, Born(1853, 11, 30), "store clerk", 102);
            AddPerson(P7Tessa, "Tessa", "Baker", H2Baker, Born(1875, 5, 12), "child", 102);
            AddPerson(P8Teamster, "Samuel", "Reyes", H3Teamster, Born(1839, 8, 8), "teamster", 103);
            AddPerson(P9Smith, "Henrik", "Forge", H4Smith, Born(1835, 3, 25), "blacksmith", 104);
            AddPerson(P10Ingrid, "Ingrid", "Forge", H4Smith, Born(1837, 12, 1), "homemaker", 104);
            AddPerson(P13Elder, "Old Tomas", "Forge", H4Smith, Born(1812, 7, 4), "retired", 104);
            AddPerson(P11Widow, "Mara", "Voss", H5Widow, Born(1828, 10, 19), "seamstress", 105);
            AddPerson(P12Lena, "Lena", "Voss", H5Widow, Born(1870, 2, 28), "child", 105);

            // Kinship: the scenario's real family edges.
            kinship.AddSpouses(P1Miller, P2Anna, "scenario");
            kinship.AddParentChild(P1Miller, P3Jonas, "scenario");
            kinship.AddParentChild(P2Anna, P3Jonas, "scenario");
            kinship.AddParentChild(P1Miller, P4Marta, "scenario");
            kinship.AddParentChild(P2Anna, P4Marta, "scenario");
            kinship.AddSpouses(P5Baker, P6Clerk, "scenario");
            kinship.AddParentChild(P5Baker, P7Tessa, "scenario");
            kinship.AddParentChild(P6Clerk, P7Tessa, "scenario");
            kinship.AddSpouses(P9Smith, P10Ingrid, "scenario");
            kinship.AddParentChild(P13Elder, P9Smith, "scenario");
            kinship.AddParentChild(P11Widow, P12Lena, "scenario");
        }

        private static JourneyModel BuildJourneyModel()
        {
            var model = new JourneyModel();
            model.RegisterLocation(new JourneyLocation("mill-site", JourneyLocationKind.Mill, "Grist Mill", 0.5f, 3f));
            model.RegisterLocation(new JourneyLocation("town-center", JourneyLocationKind.Waypoint, "Town Center", 0f, 0f));
            model.RegisterLocation(new JourneyLocation("bakery-town", JourneyLocationKind.TownBuilding, "Bakery", 0.2f, 0.3f));
            model.RegisterLocation(new JourneyLocation("store-town", JourneyLocationKind.Store, "General Store", -0.2f, 0.2f));
            model.RegisterLocation(new JourneyLocation("forge-town", JourneyLocationKind.TownBuilding, "Forge", 0.3f, -0.2f));
            model.RegisterLocation(new JourneyLocation("town-depot", JourneyLocationKind.OffMapGateway, "Rail Depot", 0f, -1f));
            model.AddEdge("mill-site", "town-center", 3f, "mill road");
            model.AddEdge("town-center", "bakery-town", 0.4f, "main street");
            model.AddEdge("town-center", "store-town", 0.4f, "main street");
            model.AddEdge("town-center", "forge-town", 0.5f, "forge lane");
            model.AddEdge("town-center", "town-depot", 1f, "depot road");
            return model;
        }

        private void BuildBusinesses()
        {
            AddBusiness(MillId, 20000);
            AddBusiness(BakeryId, 15000);
            AddBusiness(StoreId, 25000);
            AddBusiness(ForgeId, 20000);

            Stock(MillId, "wheat", 600);
            Stock(BakeryId, "flour", 200);
            Stock(StoreId, "salt", 150);
            Stock(StoreId, "soap", 150);
            Stock(ForgeId, "iron", 200);

            // Mill: 100u wheat -> 80u flour. Grind: 360 min with the miller.
            methods[MillId] = new ProductionMethodDefinition
            {
                MethodId = "mill-wheat-flour",
                OutputProductId = "flour",
                OutputQuantity = 90f,
                InputRequirements = new List<ProductionInputRequirement>
                {
                    new ProductionInputRequirement { ProductId = "wheat", Quantity = 100f },
                },
                Phases = new List<ProductionPhaseDefinition>
                {
                    new ProductionPhaseDefinition { PhaseId = "grind", ElapsedMinutes = 360, RequiresPerson = true },
                },
            };
            policies[MillId] = new GenericProductionPolicy
            {
                MethodId = "mill-wheat-flour",
                Mode = GenericProductionPolicyMode.MakeToStock,
                TargetStock = 400f,
                BatchSize = 1f,
                Priority = 2,
            };

            // Bakery: 50u flour -> 60u bread. Bake 240 min (person, communal oven) + cool 120 min.
            methods[BakeryId] = new ProductionMethodDefinition
            {
                MethodId = "bake-flour-bread",
                OutputProductId = "bread",
                OutputQuantity = 60f,
                InputRequirements = new List<ProductionInputRequirement>
                {
                    new ProductionInputRequirement { ProductId = "flour", Quantity = 50f },
                },
                RequiredEquipmentIds = new List<string> { OvenAssetKey },
                Phases = new List<ProductionPhaseDefinition>
                {
                    new ProductionPhaseDefinition
                    {
                        PhaseId = "bake", ElapsedMinutes = 240, RequiresPerson = true,
                        ReservedEquipmentIds = new List<string> { OvenAssetKey },
                    },
                    new ProductionPhaseDefinition { PhaseId = "cool", ElapsedMinutes = 120, RequiresPerson = false },
                },
            };
            policies[BakeryId] = new GenericProductionPolicy
            {
                MethodId = "bake-flour-bread",
                Mode = GenericProductionPolicyMode.MakeToStock,
                TargetStock = 300f,
                BatchSize = 1f,
                Priority = 3,
            };

            // Forge: 20u iron -> 10u tools. Forge work 420 min with the smith.
            methods[ForgeId] = new ProductionMethodDefinition
            {
                MethodId = "forge-iron-tools",
                OutputProductId = "tools",
                OutputQuantity = 10f,
                InputRequirements = new List<ProductionInputRequirement>
                {
                    new ProductionInputRequirement { ProductId = "iron", Quantity = 20f },
                },
                Phases = new List<ProductionPhaseDefinition>
                {
                    new ProductionPhaseDefinition { PhaseId = "forge", ElapsedMinutes = 420, RequiresPerson = true },
                },
            };
            policies[ForgeId] = new GenericProductionPolicy
            {
                MethodId = "forge-iron-tools",
                Mode = GenericProductionPolicyMode.MakeToStock,
                TargetStock = 40f,
                BatchSize = 1f,
                Priority = 1,
            };

            foreach (string bizId in businesses.Keys)
            {
                var runtime = new GenericProductionRuntime(
                    tasks, budgets, EntityId.For(EntityKind.Business, BusinessIndex(bizId)), 0);
                productionRuntimes[bizId] = runtime;
            }
        }

        private static int BusinessIndex(string bizId)
        {
            if (bizId == MillId) return 1;
            if (bizId == BakeryId) return 2;
            if (bizId == StoreId) return 3;
            return 4;
        }

        private void AddBusiness(string bizId, int openingCashCents)
        {
            var state = new BusinessRuntimeState();
            state.BindBusinessIdentity(bizId);
            state.AdjustCashCents(openingCashCents, recordNetDelta: false);
            state.EmploymentRegistry = employments;
            state.HouseholdLedgers = ledgerRegistry;
            state.PersonHouseholdIdLookup = memberships.GetActiveHouseholdId;
            businesses[bizId] = state;
            businessStock[bizId] = new Dictionary<string, GenericInventoryPosition>(StringComparer.OrdinalIgnoreCase);
        }

        private GenericInventoryPosition Position(string bizId, string productId)
        {
            Dictionary<string, GenericInventoryPosition> stocks = businessStock[bizId];
            GenericInventoryPosition position;
            if (!stocks.TryGetValue(productId, out position))
            {
                position = new GenericInventoryPosition(productId);
                stocks[productId] = position;
            }
            return position;
        }

        private void Stock(string bizId, string productId, float units)
        {
            Position(bizId, productId).Add(units, 0, null, "opening stock", 0);
        }

        private void AddEmployment(string employmentId, int personId, string bizId, string role, int weeklyWageCents)
        {
            employments.Register(new EmploymentRelationship
            {
                Id = employmentId,
                EmployeePersonId = personId,
                EmployerBusinessId = bizId,
                RoleDisplayName = role,
                LifecycleState = EmploymentLifecycleState.Active,
                Compensation = CompensationTerms.FromWeeklyWage(weeklyWageCents, "section-40 scenario"),
                StartDayIndex = 0,
            });
        }

        private void BuildEmployments()
        {
            AddEmployment("emp-p1-mill", P1Miller, MillId, "miller", 750);
            AddEmployment("emp-p8-mill", P8Teamster, MillId, "teamster", 500);
            AddEmployment("emp-p5-bakery", P5Baker, BakeryId, "baker", 700);
            AddEmployment("emp-p6-store", P6Clerk, StoreId, "clerk and delegated manager", 600);
            AddEmployment("emp-p9-forge", P9Smith, ForgeId, "blacksmith", 800);
            // P11 (widow) starts unemployed: the bad outcome. Hired on day 15.
        }

        private void BuildCredit()
        {
            var terms = new FinancialPaymentTerms
            {
                Structure = FinancialPaymentStructure.PrincipalInstallments,
                PaymentIntervalDays = 7,
                InstallmentPrincipalCents = 100,
            };
            FinancialObligation obligation = obligations.CreateWithTerms(
                ids, FinancialObligationKind.TradeCredit, "household:5", "biz-store",
                3000, 0, "store credit, 100c weekly", "winter provisions", terms, "store-credit-h5");
            if (obligation != null)
            {
                h5CreditObligationId = obligation.ObligationId;
                // The credit already bought provisions before day 0: the cash moved
                // then (opening stock), the obligation records the debt now.
                Log("H5 owes the general store 3000c trade credit (100c weekly).");
            }
        }

        private void ScheduleAllBirthdays(int fromDay)
        {
            foreach (PersonState person in population.people)
            {
                if (person.deathDayIndex >= 0)
                {
                    continue;
                }
                BirthDate birth;
                if (!birthDates.TryGetValue(person.id, out birth))
                {
                    continue;
                }
                BirthdayScheduling.ScheduleNextBirthday(
                    dueEvents, person.id, birth, fromDay,
                    AnchorYear, AnchorMonth, AnchorDay, diag);
            }
        }

        // =====================================================================
        // Clock: tick-size agnostic day stepping
        // =====================================================================

        /// <summary>
        /// Advances the clock by minutes. A full day step runs each time a day
        /// boundary is crossed, so any tick size summing to the same total
        /// produces identical outcomes (1x/10x/100x consistency).
        /// </summary>
        public void AdvanceMinutes(int minutes)
        {
            if (minutes <= 0)
            {
                return;
            }
            tickMinuteAccumulator += minutes;
            while (tickMinuteAccumulator >= MinutesPerDay)
            {
                tickMinuteAccumulator -= MinutesPerDay;
                RunDay();
            }
        }

        public void AdvanceDays(int days)
        {
            for (int i = 0; i < days; i++)
            {
                AdvanceMinutes(MinutesPerDay);
            }
        }

        private void RunDay()
        {
            absoluteDayIndex++;
            int day = absoluteDayIndex;
            budgets.EnsureDay(day);
            foreach (var runtime in productionRuntimes.Values)
            {
                runtime.SetDayIndex(day);
            }

            ProcessBirthdays(day);
            ProcessJourneyReturns(day);
            ScheduleWagonDepartures(day);
            RunWorkTasks(day);
            RunProduction(day);
            if (day % 7 == 6)
            {
                SettlePayroll(day);
            }
            RunTrade(day);
            RunConsumption(day);
            ProcessRent(day);
            ProcessCredit(day);
            RunDelegatedManagement(day);
            RunScriptedEvents(day);
            ReleaseDayReservations(day);
            // Presentation is told AFTER the day's outcomes are committed; the
            // sink cannot change anything (section 25A/B).
            SimulationPresentationBridge.Notify(ObservationMode.FullyRepresented,
                "society-day-completed", "day " + day);
        }

        private void ProcessBirthdays(int day)
        {
            List<DueEvent> due = dueEvents.PopDue(day * BirthdayScheduling.SecondsPerDay);
            foreach (DueEvent evt in due)
            {
                if (evt == null || evt.Kind != DueEventKind.Birthday)
                {
                    continue;
                }
                int personId = BirthdayScheduling.PersonIdFromPayload(evt.PayloadRef);
                PersonState person = FindPerson(personId);
                if (person == null || person.deathDayIndex >= 0)
                {
                    continue; // the dead keep no birthdays
                }
                BirthDate birth;
                if (!birthDates.TryGetValue(personId, out birth))
                {
                    continue;
                }
                int newAge = AgeAuthority.CompletedYears(birth,
                    CurrentYear(), CurrentMonth(), CurrentDay());
                person.age = newAge; // mirror of the birth-date authority until P0-2 lands the derived read
                person.ageBand = HouseholdLifecycleManager.AgeBandForAge(newAge);
                birthdaysProcessed++;
                Log($"P{personId} ({person.DisplayName}) turned {newAge}.");
                // Chain the next birthday from the day after the one that just fired:
                // exactly one pending event per living person, never a same-day echo.
                BirthdayScheduling.ScheduleNextBirthday(dueEvents, personId, birth, day + 1,
                    AnchorYear, AnchorMonth, AnchorDay, diag);
            }
        }

        private void CurrentDate(out int year, out int month, out int day)
        {
            GregorianDateMath.FromAbsoluteDay(absoluteDayIndex, AnchorYear, AnchorMonth, AnchorDay,
                out year, out month, out day);
        }

        private int CurrentYear() { CurrentDate(out int y, out _, out _); return y; }
        private int CurrentMonth() { CurrentDate(out _, out int m, out _); return m; }
        private int CurrentDay() { CurrentDate(out _, out _, out int d); return d; }

        private PersonState FindPerson(int personId)
        {
            foreach (PersonState person in population.people)
            {
                if (person.id == personId)
                {
                    return person;
                }
            }
            return null;
        }

        private bool IsDead(int personId)
        {
            PersonState person = FindPerson(personId);
            return person != null && person.deathDayIndex >= 0;
        }

        private bool IsOffMap(int personId)
        {
            return offMap.IsOffMapPerson(personId);
        }

        // =====================================================================
        // Daily step: journeys and shared equipment
        // =====================================================================

        private void ProcessJourneyReturns(int day)
        {
            List<OffMapJourneyRecord> returned = offMap.CompleteDueJourneys(day,
                (item, units) =>
                {
                    // Off-map purchases arrive: wheat to the mill, salt/soap to
                    // the store, iron to the forge.
                    string bizId = item == "wheat" ? MillId
                        : item == "salt" || item == "soap" ? StoreId : ForgeId;
                    Position(bizId, item).Add(units, 0, null, "off-map purchase", day);
                    Bump(boughtOffMapUnits, item, units);
                }, diag);
            foreach (OffMapJourneyRecord record in returned)
            {
                equipment.Release(WagonAssetKey);
                Log($"{DescribeJourney(record)} returned; wagon-1 released.");
                if (record.TravelerKind == OffMapTravelerKind.Person && record.PersonId == P3Jonas)
                {
                    if (memberships.GetActiveHouseholdId(P3Jonas) < 0)
                    {
                        string refusal = lifecycle.AddMember(H1Miller, P3Jonas, day,
                            HouseholdMembershipSource.Manual, "returned from off-map work");
                        Log(refusal == null
                            ? $"P{P3Jonas} (Jonas Miller) returned home to H{H1Miller}: the same person, membership restored."
                            : $"P{P3Jonas} return: membership re-registration refused: {refusal}");
                    }
                }
            }
        }

        private static string DescribeJourney(OffMapJourneyRecord record)
        {
            return record.TravelerKind == OffMapTravelerKind.Person
                ? $"P{record.PersonId}"
                : $"wagon '{record.VehicleId}'";
        }

        /// <summary>
        /// The shared wagon: the mill runs wheat-buying trips every 14 days, the
        /// store runs staples-buying trips every 21 days (offset). Both reserve
        /// the wagon through the equipment authority; the loser waits (real
        /// contention, logged). The teamster (P8) rides along as a person journey.
        /// </summary>
        private bool pendingStoreRun;
        private bool pendingForgeRun;

        private void ScheduleWagonDepartures(int day)
        {
            bool millDue = day % 14 == 0;
            bool storeDue = day % 21 == 7 || pendingStoreRun;
            bool forgeDue = day % 28 == 21 || pendingForgeRun;
            if (!millDue && !storeDue && !forgeDue)
            {
                return;
            }
            if (offMap.IsOffMapVehicle("wagon-1"))
            {
                // The wagon is out: whoever is due waits, loudly, and retries
                // tomorrow. Shared equipment means queuing, not duplicating.
                if (storeDue && !pendingStoreRun)
                {
                    pendingStoreRun = true;
                    equipmentContentions++;
                    Log("store's wagon run delayed: wagon-1 is out with another run (contention).");
                }
                if (forgeDue && !pendingForgeRun)
                {
                    pendingForgeRun = true;
                    equipmentContentions++;
                    Log("forge's wagon run delayed: wagon-1 is out with another run (contention).");
                }
                return;
            }

            // Priority: mill wheat first, then store, then forge iron.
            string requester = millDue ? MillId : storeDue ? StoreId : ForgeId;
            string refusal = equipment.Reserve(WagonAssetKey, requester, "off-map buying trip");
            if (refusal != null)
            {
                equipmentContentions++;
                Log($"{requester} wanted wagon-1 for an off-map run but it is reserved; run delayed (contention).");
                return;
            }

            if (millDue)
            {
                DepartMillWheatRun(day);
            }
            else if (storeDue)
            {
                pendingStoreRun = false;
                DepartStoreStaplesRun(day);
            }
            else
            {
                pendingForgeRun = false;
                DepartForgeIronRun(day);
            }
        }

        private void DepartForgeIronRun(int day)
        {
            const int units = 100;
            const int priceCents = 10;
            int cost = units * priceCents;
            BusinessRuntimeState forge = businesses[ForgeId];
            if (forge.CurrentCashCents < cost)
            {
                equipment.Release(WagonAssetKey);
                shortages++;
                Log("forge cannot fund its iron run (cash short); run cancelled, reservation released.");
                return;
            }
            forge.AdjustCashCents(-cost, recordNetDelta: true);
            externalCashOutflowsCents += cost;

            OffMapJourneyRecord wagon = offMap.DepartVehicle(
                "wagon-1", "forge-town", "ironworks up the line", 1,
                "buy iron", null, new List<string> { "po-iron" }, day,
                item => (int)Position(ForgeId, item).AvailableQuantity,
                (item, takeUnits) =>
                {
                    float consumed;
                    return Position(ForgeId, item).TryConsume(takeUnits, out consumed)
                        ? null : "insufficient";
                }, diag);
            if (wagon == null)
            {
                forge.AdjustCashCents(cost, recordNetDelta: true);
                externalCashOutflowsCents -= cost;
                equipment.Release(WagonAssetKey);
                return;
            }
            wagon.PurchaseManifestUnits["iron"] = units;
            Log($"wagon-1 departed for the ironworks for the forge; 100u iron bought for {cost}c (due day {wagon.DueReturnDayIndex}).");
        }

        private void DepartMillWheatRun(int day)
        {
            const int units = 400;
            const int priceCents = 2;
            int cost = units * priceCents;
            BusinessRuntimeState mill = businesses[MillId];
            if (mill.CurrentCashCents < cost)
            {
                equipment.Release(WagonAssetKey);
                shortages++;
                Log("mill cannot fund its wheat run (cash short); run cancelled, reservation released.");
                return;
            }
            mill.AdjustCashCents(-cost, recordNetDelta: true);
            externalCashOutflowsCents += cost;

            OffMapJourneyRecord wagon = offMap.DepartVehicle(
                "wagon-1", "mill-site", "Kingston grain markets", 2,
                "buy wheat", null, new List<string> { "po-wheat" }, day,
                item => (int)Position(MillId, item).AvailableQuantity,
                (item, takeUnits) =>
                {
                    float consumed;
                    return Position(MillId, item).TryConsume(takeUnits, out consumed)
                        ? null : "insufficient";
                }, diag);
            if (wagon == null)
            {
                mill.AdjustCashCents(cost, recordNetDelta: true); // refund: nothing moved
                externalCashOutflowsCents -= cost;
                equipment.Release(WagonAssetKey);
                return;
            }
            // The wheat is bought off-map and rides home: a purchase manifest,
            // not taken from origin stock.
            wagon.PurchaseManifestUnits["wheat"] = units;
            // The teamster rides along: same due day, no double-booked person-time.
            int tripDays = Math.Max(1, wagon.DueReturnDayIndex - day);
            offMap.DepartPerson(P8Teamster, "mill-site", "Kingston grain markets", 2, tripDays,
                "drive the wheat wagon",
                null, null, day, item => 0, null, diag);
            Log($"wagon-1 departed for Kingston with P{P8Teamster} driving; 400u wheat bought for {cost}c (due day {wagon.DueReturnDayIndex}).");
        }

        private void DepartStoreStaplesRun(int day)
        {
            const int unitsPerItem = 100;
            const int priceCents = 3;
            int cost = unitsPerItem * 2 * priceCents;
            BusinessRuntimeState store = businesses[StoreId];
            if (store.CurrentCashCents < cost)
            {
                equipment.Release(WagonAssetKey);
                shortages++;
                Log("store cannot fund its staples run (cash short); run cancelled, reservation released.");
                return;
            }
            store.AdjustCashCents(-cost, recordNetDelta: true);
            externalCashOutflowsCents += cost;

            OffMapJourneyRecord wagon = offMap.DepartVehicle(
                "wagon-1", "store-town", "county wholesalers", 1,
                "buy salt and soap", null, new List<string> { "po-salt-soap" }, day,
                item => (int)Position(StoreId, item).AvailableQuantity,
                (item, takeUnits) =>
                {
                    float consumed;
                    return Position(StoreId, item).TryConsume(takeUnits, out consumed)
                        ? null : "insufficient";
                }, diag);
            if (wagon == null)
            {
                store.AdjustCashCents(cost, recordNetDelta: true);
                externalCashOutflowsCents -= cost;
                equipment.Release(WagonAssetKey);
                return;
            }
            wagon.PurchaseManifestUnits["salt"] = unitsPerItem;
            wagon.PurchaseManifestUnits["soap"] = unitsPerItem;
            Log($"wagon-1 departed for the county wholesalers for the store; 100u salt + 100u soap bought for {cost}c (due day {wagon.DueReturnDayIndex}).");
        }

        // =====================================================================
        // Daily step: work and person-time
        // =====================================================================

        private void DoTask(int personId, string definitionId, int minutes, int day, string label)
        {
            EntityId worker = EntityId.For(EntityKind.Person, personId);
            WorkTask task = tasks.CreateTaskWithPlannedMinutes(definitionId, worker, day, minutes,
                customerRef: "society-day");
            string rejection;
            if (!tasks.AssignTask(task.TaskId, worker, budgets, day, out rejection))
            {
                diag.Add($"society: {label} for P{personId} refused at assign: {rejection}");
                tasks.CancelTask(task.TaskId, budgets);
                return;
            }
            if (!tasks.StartTask(task.TaskId, day, out rejection))
            {
                diag.Add($"society: {label} for P{personId} refused at start: {rejection}");
                tasks.CancelTask(task.TaskId, budgets);
                return;
            }
            if (!tasks.RecordWork(task.TaskId, minutes, budgets, day, out rejection))
            {
                diag.Add($"society: {label} for P{personId} refused at work: {rejection}");
                return;
            }
            int worked;
            personMinutesWorked.TryGetValue(personId, out worked);
            personMinutesWorked[personId] = worked + minutes;
        }

        private void RunWorkTasks(int day)
        {
            // Employed persons work their shift unless dead or legitimately off-map.
            // Production workers (miller, baker, smith) do their work through
            // RunBusinessProduction instead: their person-time is exclusive, so
            // they must not also consume a separate work-shift task.
            var productionWorkers = new HashSet<int> { P1Miller, P5Baker, P9Smith };
            var seenWorkers = new HashSet<int>();
            foreach (string bizId in businesses.Keys)
            {
                foreach (EmploymentRelationship employment in employments.GetActiveByEmployer(bizId))
                {
                    int personId = employment.EmployeePersonId;
                    if (!seenWorkers.Add(personId))
                    {
                        continue;
                    }
                    if (IsDead(personId) || IsOffMap(personId))
                    {
                        continue;
                    }
                    if (productionWorkers.Contains(personId))
                    {
                        continue; // time is spent in production, not a duplicate shift
                    }
                    DoTask(personId, "work-shift", WorkShiftMinutes, day,
                        $"{employment.RoleDisplayName} at {employment.EmployerBusinessId}");
                }
            }

            // Domestic labor: homemakers and children keep the households running.
            int[] domesticAdults = { P2Anna, P10Ingrid };
            foreach (int personId in domesticAdults)
            {
                if (!IsDead(personId) && !IsOffMap(personId))
                {
                    DoTask(personId, "domestic-work", 240, day, "domestic work");
                }
            }
            int[] children = { P3Jonas, P4Marta, P7Tessa, P12Lena };
            foreach (int personId in children)
            {
                if (!IsDead(personId) && !IsOffMap(personId))
                {
                    DoTask(personId, "domestic-help", 120, day, "domestic help");
                }
            }
        }

        // =====================================================================
        // Daily step: production through the generic production runtime
        // =====================================================================

        private void RunProduction(int day)
        {
            // Bakery buys flour from the mill first: the real supply chain.
            TransferFlourToBakery(day);

            string[] producers = { MillId, BakeryId, ForgeId };
            int[] workers = { P1Miller, P5Baker, P9Smith };
            for (int i = 0; i < producers.Length; i++)
            {
                RunBusinessProduction(producers[i], workers[i], day);
            }
        }

        private void TransferFlourToBakery(int day)
        {
            GenericInventoryPosition bakeryFlour = Position(BakeryId, "flour");
            if (bakeryFlour.AvailableQuantity >= 100f)
            {
                return;
            }
            GenericInventoryPosition millFlour = Position(MillId, "flour");
            float need = Math.Min(100f - bakeryFlour.AvailableQuantity, millFlour.AvailableQuantity);
            if (need < 10f)
            {
                shortages++;
                Log("bakery cannot buy flour: the mill is short (supply-chain shortage).");
                return;
            }
            const int priceCents = 9;
            int cost = Mathf.RoundToInt(need * priceCents);
            BusinessRuntimeState bakery = businesses[BakeryId];
            if (bakery.CurrentCashCents < cost)
            {
                shortages++;
                Log("bakery cannot buy flour: cash short.");
                return;
            }
            float consumed;
            if (!millFlour.TryConsume(need, out consumed))
            {
                shortages++;
                return;
            }
            bakery.AdjustCashCents(-cost, recordNetDelta: true);
            businesses[MillId].AdjustCashCents(cost, recordNetDelta: true);
            bakeryFlour.Add(consumed, priceCents, null, "bought from mill", day);
            Log($"bakery bought {consumed:F0}u flour from the mill for {cost}c.");
        }

        private void RunBusinessProduction(string bizId, int workerPersonId, int day)
        {
            if (IsDead(workerPersonId) || IsOffMap(workerPersonId))
            {
                return; // no worker, no production: never faked
            }
            ProductionMethodDefinition method = methods[bizId];
            GenericProductionPolicy policy = policies[bizId];
            GenericInventoryPosition output = Position(bizId, method.OutputProductId);

            GenericProductionPlan plan = GenericProductionPlanner.Plan(
                policy, method, output.Quantity, 0f,
                input => Position(bizId, input.ProductId).AvailableQuantity,
                input => Position(bizId, input.ProductId).ReservedQuantity);
            if (!plan.IsEligible)
            {
                return; // target stock reached or inputs short: idle, honestly
            }

            // The communal oven is genuinely shared: the bakery must hold the
            // reservation before the bake begins.
            bool ovenNeeded = method.RequiredEquipmentIds.Contains(OvenAssetKey);
            bool ovenHeld = false;
            if (ovenNeeded)
            {
                if (equipment.IsReserved(OvenAssetKey))
                {
                    shortages++;
                    Log("bakery bake delayed: the communal oven is already reserved.");
                    return;
                }
                string ovenRefusal = equipment.Reserve(OvenAssetKey, bizId, "bake");
                if (ovenRefusal != null)
                {
                    shortages++;
                    Log("bakery bake delayed: " + ovenRefusal);
                    return;
                }
                ovenHeld = true;
            }

            EntityId worker = EntityId.For(EntityKind.Person, workerPersonId);
            ProductionProcessState process;
            string reason;
            bool begun = productionRuntimes[bizId].TryBegin(plan, worker,
                // Usable if free, or if it is the oven WE just reserved above.
                equipmentId => !equipment.IsReserved(equipmentId) || (ovenHeld && equipmentId == OvenAssetKey),
                workspaceId => true,
                inputProductId =>
                {
                    ProductionInputRequirement requirement = null;
                    foreach (ProductionInputRequirement req in method.InputRequirements)
                    {
                        if (string.Equals(req.ProductId, inputProductId, StringComparison.OrdinalIgnoreCase))
                        {
                            requirement = req;
                            break;
                        }
                    }
                    float need = requirement != null ? requirement.Quantity * plan.Quantity : 0f;
                    return Position(bizId, inputProductId).AvailableQuantity >= need;
                },
                personId => !IsDead(workerPersonId) && !IsOffMap(workerPersonId),
                input =>
                {
                    float need = input.Quantity * plan.Quantity;
                    float consumed;
                    if (!Position(bizId, input.ProductId).TryConsume(need, out consumed))
                    {
                        return false;
                    }
                    Bump(productionInputsConsumed, input.ProductId, (long)Math.Round(consumed));
                    return true;
                },
                out process, out reason);
            if (!begun)
            {
                if (ovenHeld)
                {
                    equipment.Release(OvenAssetKey);
                }
                shortages++;
                Log($"{bizId} production blocked: {reason}");
                return;
            }

            activeProcesses[bizId] = process;
            string advanceReason;
            bool advanced = productionRuntimes[bizId].Advance(process, worker, WorkShiftMinutes,
                () =>
                {
                    float qty = method.OutputQuantity * plan.Quantity;
                    output.Add(qty, 0, null, "production", day);
                    Bump(producedUnits, method.OutputProductId, (long)Math.Round(qty));
                    return true;
                }, out advanceReason);
            if (!advanced)
            {
                diag.Add($"society: {bizId} production advance failed: {advanceReason}");
            }
            if (process.Completed)
            {
                activeProcesses.Remove(bizId);
            }
            if (ovenHeld)
            {
                equipment.Release(OvenAssetKey);
            }
        }

        // =====================================================================
        // Daily step: payroll, trade, consumption, rent, credit
        // =====================================================================

        private void SettlePayroll(int day)
        {
            int weekIndex = day / 7;
            foreach (string bizId in businesses.Keys)
            {
                BusinessRuntimeState business = businesses[bizId];
                business.PayrollDayIndex = day;
                // The business is fully wired (employment registry, household
                // ledgers, household lookup): ResolveWeeklyPayroll moves business
                // cash AND credits worker household ledgers through the one
                // PKG-6 path. Crediting here as well would invent cash.
                string result = wageService.SettleWeeklyPayroll(business, employments, weekIndex, day, diag);
                PayrollEvent evt = wageService.Events[wageService.Events.Count - 1];
                foreach (PayrollEntry entry in evt.Entries)
                {
                    if (entry.Paid)
                    {
                        wagesPaidCents += entry.AmountCents;
                    }
                }
                if (result == "settled-arrears")
                {
                    payrollArrearsWeeks++;
                    Log($"{bizId} could not meet payroll in week {weekIndex}: arrears accrued (business distress).");
                }
            }
        }

        private void BuyFromBusiness(int householdId, string bizId, string productId,
            float unitsWanted, int priceCentsPerUnit, int day, string purpose)
        {
            if (unitsWanted <= 0f)
            {
                return;
            }
            GenericInventoryPosition position = Position(bizId, productId);
            float available = position.AvailableQuantity;
            if (available <= 0f)
            {
                shortages++;
                Log($"H{householdId} wanted {productId} but {bizId} is out of stock (shortage).");
                return;
            }
            if (HouseholdItemCatalog.Get(productId) == null)
            {
                diag.Add($"society: {bizId} cannot sell '{productId}' to a household: not a household-storable item.");
                return;
            }
            HouseholdLedger ledger = ledgers[householdId];
            float affordable = ledger.GetBalanceCents() / (float)Math.Max(1, priceCentsPerUnit);
            float units = Math.Min(unitsWanted, Math.Min(available, affordable));
            if (units < 1f)
            {
                unmetNeeds++;
                Log($"H{householdId} cannot afford {productId} (need {unitsWanted:F0}u, balance {ledger.GetBalanceCents()}c): need unmet.");
                return;
            }
            int cost = Mathf.RoundToInt(units * priceCentsPerUnit);
            string rejection = ledger.RecordOutflow(day, cost, purpose, bizId);
            if (rejection != null)
            {
                unmetNeeds++;
                Log($"H{householdId} purchase of {productId} refused: {rejection}");
                return;
            }
            float consumed;
            if (!position.TryConsume(units, out consumed))
            {
                diag.Add($"society: {bizId} stock vanished mid-sale.");
                return;
            }
            businesses[bizId].AdjustCashCents(cost, recordNetDelta: true);
            string lotRejection = inventories[householdId].AddLot(productId, Mathf.RoundToInt(consumed), day, $"bought from {bizId}");
            if (lotRejection != null)
            {
                // Roll back: the sale did not happen.
                position.Add(consumed, 0, null, "sale rolled back", day);
                businesses[bizId].AdjustCashCents(-cost, recordNetDelta: true);
                ledger.RecordInflow(day, cost, HouseholdIncomeSource.OtherDocumented,
                    "sale-rollback", "rolled back purchase of " + productId, bizId);
                diag.Add($"society: household purchase of {productId} rolled back: {lotRejection}");
                return;
            }
        }

        private void RunTrade(int day)
        {
            foreach (int householdId in ledgers.Keys)
            {
                List<int> members = memberships.GetActiveMembers(householdId);
                int mouths = 0;
                foreach (int personId in members)
                {
                    if (!IsDead(personId))
                    {
                        mouths++;
                    }
                }
                if (mouths == 0)
                {
                    continue;
                }
                // Daily bread: 2u per mouth, bought from the bakery at 12c.
                float breadWant = mouths * 2f - inventories[householdId].GetAvailableUnits("bread");
                BuyFromBusiness(householdId, BakeryId, "bread", breadWant, 12, day, "daily bread");
                // Salt and soap every 3 days at 5c.
                if (day % 3 == 0)
                {
                    BuyFromBusiness(householdId, StoreId, "salt", 1f, 5, day, "household salt");
                    BuyFromBusiness(householdId, StoreId, "soap", 1f, 5, day, "household soap");
                }
            }
            SellForgeToolsOffMap(day);
        }

        /// <summary>
        /// The forge sells tools to traveling buyers off-map: real external
        /// revenue (cash enters the system, goods leave it), both tracked.
        /// </summary>
        private void SellForgeToolsOffMap(int day)
        {
            if (day % 7 != 0)
            {
                return;
            }
            GenericInventoryPosition position = Position(ForgeId, "tools");
            if (position.AvailableQuantity < 20f)
            {
                return;
            }
            const int units = 10;
            const int priceCents = 150;
            float consumed;
            if (!position.TryConsume(units, out consumed))
            {
                return;
            }
            int revenue = Mathf.RoundToInt(consumed * priceCents);
            businesses[ForgeId].AdjustCashCents(revenue, recordNetDelta: true);
            externalCashInflowsCents += revenue;
            Bump(soldOffMapUnits, "tools", (long)Math.Round(consumed));
            Log($"forge sold {consumed:F0}u tools to traveling buyers for {revenue}c.");
        }

        private void RunConsumption(int day)
        {
            foreach (int householdId in ledgers.Keys)
            {
                List<int> members = memberships.GetActiveMembers(householdId);
                int mouths = 0;
                foreach (int personId in members)
                {
                    if (!IsDead(personId))
                    {
                        mouths++;
                    }
                }
                if (mouths == 0)
                {
                    continue;
                }
                HouseholdInventory inventory = inventories[householdId];
                int need = mouths * 2;
                int have = inventory.GetAvailableUnits("bread");
                int toEat = Math.Min(need, have);
                if (toEat > 0)
                {
                    var records = new List<HouseholdConsumptionRecord>();
                    string rejection = inventory.Consume("bread", toEat, day, "daily meals",
                        $"H{householdId}", records);
                    if (rejection == null)
                    {
                        Bump(consumedUnits, "bread", toEat);
                    }
                }
                if (toEat < need)
                {
                    hungerEvents++;
                    Log($"H{householdId} went short on bread ({toEat}/{need}u): hunger (hardship).");
                }
            }
        }

        private void ProcessRent(int day)
        {
            // Rent is due on the first of each Gregorian month.
            if (CurrentDay() != 1)
            {
                return;
            }
            ChargeRent(H5Widow, 400, day, "monthly rent");
            ChargeRent(H3Teamster, 150, day, "room rent");
        }

        private void ChargeRent(int householdId, int amountCents, int day, string purpose)
        {
            string rejection = ledgers[householdId].RecordOutflow(day, amountCents, purpose, "landlord");
            if (rejection != null)
            {
                rentArrearsCents += amountCents;
                Log($"H{householdId} could not pay rent ({amountCents}c): arrears now {rentArrearsCents}c.");
                return;
            }
            // The landlord is not a modeled household: rent leaves the system.
            externalCashOutflowsCents += amountCents;
        }

        private void ProcessCredit(int day)
        {
            if (string.IsNullOrEmpty(h5CreditObligationId))
            {
                return;
            }
            // Weekly payment due mid-week.
            if (day % 7 != 3)
            {
                return;
            }
            FinancialObligation obligation = obligations.Find(h5CreditObligationId);
            if (obligation == null || obligation.Settled)
            {
                return;
            }
            int due = obligations.CalculateDueAmountCents(h5CreditObligationId, day);
            if (due <= 0)
            {
                return;
            }
            HouseholdLedger ledger = ledgers[H5Widow];
            if (ledger.GetBalanceCents() < due)
            {
                obligations.MarkDelinquent(h5CreditObligationId);
                creditDelinquencies++;
                Log($"H5 missed its 100c store-credit payment (balance {ledger.GetBalanceCents()}c): delinquent.");
                return;
            }
            string rejection = ledger.RecordOutflow(day, due, "store credit installment", "biz-store");
            if (rejection != null)
            {
                obligations.MarkDelinquent(h5CreditObligationId);
                creditDelinquencies++;
                Log($"H5 credit payment refused: {rejection}");
                return;
            }
            businesses[StoreId].AdjustCashCents(due, recordNetDelta: true);
            obligations.ApplyPayment(h5CreditObligationId, due, day, "household:5", "biz-store", "weekly installment");
            Log($"H5 paid {due}c toward its store credit (outstanding {obligations.Find(h5CreditObligationId).OutstandingPrincipalCents}c).");
        }

        private void RunDelegatedManagement(int day)
        {
            // The general store is manager-run by P6 (delegated). Weekly, the
            // manager sets the reorder point from the delegation policy and
            // orders staples when stock falls below it.
            if (day % 7 != 1)
            {
                return;
            }
            float threshold = ManagerPolicyEffects.CalculateReorderThreshold01(
                0.3f, BusinessControlState.ManagerRun, ManagerPolicyPreset.StabilityFirst);
            float target = 300f;
            float onHand = Position(StoreId, "salt").AvailableQuantity + Position(StoreId, "soap").AvailableQuantity;
            if (onHand < threshold * target)
            {
                Log($"manager P{P6Clerk} (delegated, stability-first) set the salt/soap reorder point at " +
                    $"{threshold:P0} of target and ordered a wagon run.");
            }
        }

        // =====================================================================
        // Daily step: scripted demographic events (deterministic)
        // =====================================================================

        private void RunScriptedEvents(int day)
        {
            if (!demographicEvents)
            {
                return;
            }
            if (day == 15 && !p11Hired)
            {
                p11Hired = true;
                AddEmployment("emp-p11-store", P11Widow, StoreId, "seamstress (mending)", 450);
                Log($"P{P11Widow} (widow) hired by the general store at 900c/week: unemployment ends.");
            }
            if (day == 40 && !birthDone)
            {
                birthDone = true;
                RecordScriptedBirth(H1Miller, P2Anna, P1Miller, "Ruth", "Miller", day);
            }
            if (day == 45 && !migrationDone)
            {
                migrationDone = true;
                // A long round trip, not a one-way migration: Jonas returns on
                // day 450. The same entity comes home; membership is restored.
                OffMapJourneyRecord journey = offMap.DepartPerson(
                    P3Jonas, "mill-site", "uncle's farm two counties over", 5, 405,
                    "seeking mill work", null, null, day, item => 0, null, diag);
                if (journey != null)
                {
                    memberships.EndMembership(P3Jonas, day, "migration off-map");
                    Log($"P{P3Jonas} (Jonas Miller) left H{H1Miller} for work off-map: same person, membership ended, kinship kept; due back day 450.");
                }
            }
            // ---- Year 2: the households grow and change -------------------
            if (day == 200 && !birth2Done)
            {
                birth2Done = true;
                RecordScriptedBirth(H2Baker, P5Baker, P6Clerk, "Jozef", "Baker", day);
            }
            if (day == 100 && !p6Promoted)
            {
                p6Promoted = true;
                // The general store cannot carry staff on staples alone: Mara
                // retires and Elena is laid off. The shop continues staffless
                // on its capital (a marginal business, an honest bad outcome).
                EndEmployment("emp-p11-store", day, EmploymentEndReason.EndedByAgreement,
                    "retirement at 52");
                EndEmployment("emp-p6-store", day, EmploymentEndReason.Dismissed,
                    "store cannot afford staff");
                Log($"P{P11Widow} (Mara Voss) retired; P{P6Clerk} (Elena Baker) laid off: the store goes staffless.");
            }
            if (day == 110 && !p6BakeryHired)
            {
                p6BakeryHired = true;
                // The profitable forge hires Elena as its clerk: the employment
                // change is recorded, and her wages follow her to H6 for years.
                AddEmployment("emp-p6-forge", P6Clerk, ForgeId, "forge clerk", 300);
                Log($"P{P6Clerk} (Elena Baker) hired by the forge as clerk at 300c/week.");
            }
            if (day == 365 && eligibilityReviewsDone < 1)
            {
                eligibilityReviewsDone = 1;
                RunAnnualEligibilityReview(day);
            }
            if (day == 180 && !h6Formed)
            {
                h6Formed = true;
                HouseholdState h6 = lifecycle.FormHousehold(
                    "Elena's household", "Baker", P6Clerk, day, HouseholdMembershipSource.Manual);
                if (h6 != null)
                {
                    h6Id = h6.id;
                    EnsureHouseholdEconomics(h6Id);
                    string refusal = lifecycle.TransferMember(P6Clerk, h6Id, day);
                    Log(refusal == null
                        ? $"H{h6Id} formed: P{P6Clerk} (Elena) leaves H{H2Baker}; her forge wages now credit H{h6Id}."
                        : $"H{h6Id} formed but transfer refused: {refusal}");
                }
            }
            if (day == 550 && !birth3Done)
            {
                birth3Done = true;
                RecordScriptedBirth(H1Miller, P2Anna, P1Miller, "Pavel", "Miller", day);
            }
            if (day == 650 && !p11DeathDone)
            {
                p11DeathDone = true;
                if (lifecycle.RecordDeath(P11Widow, day, "illness"))
                {
                    deathsRecorded++;
                    BirthdayScheduling.CancelBirthday(dueEvents, P11Widow);
                    OpenAndSettleEstate(P11Widow, day);
                    Log($"P{P11Widow} (Mara Voss) died: record kept, birthday cancelled, estate opened.");
                }
            }
            // ---- Year 3 ----------------------------------------------------
            if (day == 730 && eligibilityReviewsDone < 2)
            {
                eligibilityReviewsDone = 2;
                RunAnnualEligibilityReview(day);
            }
            if (day == 800 && !birth4Done)
            {
                birth4Done = true;
                RecordScriptedBirth(H4Smith, P9Smith, P10Ingrid, "Sofia", "Forge", day);
            }
            if (day == 900 && !p3ForgeHired)
            {
                p3ForgeHired = true;
                AddEmployment("emp-p3-forge", P3Jonas, ForgeId, "forge help", 400);
                Log($"P{P3Jonas} (Jonas Miller) hired by the forge at 800c/week after his return.");
            }
            if (day == 1000 && !p8LongHaulDone)
            {
                p8LongHaulDone = true;
                OffMapJourneyRecord journey = offMap.DepartPerson(
                    P8Teamster, "mill-site", "distant grain markets", 10, 80,
                    "long freight contract", null, null, day, item => 0, null, diag);
                Log(journey != null
                    ? $"P{P8Teamster} (Samuel Reyes) left on a long freight contract; due back day 1080."
                    : $"P{P8Teamster} long freight contract refused.");
            }
            if (day == 1095 && eligibilityReviewsDone < 3)
            {
                eligibilityReviewsDone = 3;
                RunAnnualEligibilityReview(day);
            }
            if (day == 60 && !deathDone)
            {
                deathDone = true;
                if (lifecycle.RecordDeath(P13Elder, day, "old age"))
                {
                    deathsRecorded++;
                    BirthdayScheduling.CancelBirthday(dueEvents, P13Elder);
                    OpenAndSettleEstate(P13Elder, day);
                    Log($"P{P13Elder} (Old Tomas Forge) died: record kept, birthday cancelled, estate opened.");
                }
            }
        }

        private void RecordScriptedBirth(int householdId, int motherId, int fatherId,
            string firstName, string lastName, int day)
        {
            CurrentDate(out int y, out int m, out int d);
            var birthDate = new BirthDate(y, m, d, BirthDateProvenance.GeneratedExact, "simulated birth");
            PersonState child = lifecycle.RecordBirth(householdId, motherId, fatherId, firstName, lastName, day, birthDate);
            if (child == null)
            {
                diag.Add($"society: scripted birth refused on day {day}.");
                return;
            }
            birthDates[child.id] = birthDate;
            personMinutesWorked[child.id] = 0;
            BirthdayScheduling.ScheduleNextBirthday(dueEvents, child.id, birthDates[child.id], day,
                AnchorYear, AnchorMonth, AnchorDay, diag);
            birthsRecorded++;
            Log($"P{child.id} ({firstName} {lastName}) born to H{householdId}: the same-person record will age from this birth date.");
        }

        private void EndEmployment(string employmentId, int day, EmploymentEndReason reason, string note)
        {
            if (employments.TryGetById(employmentId, out EmploymentRelationship rel) && rel != null)
            {
                rel.LifecycleState = EmploymentLifecycleState.Ended;
                rel.EndDayIndex = day;
                rel.EndReason = reason;
                rel.LegacyNotes = note ?? string.Empty;
                Log($"employment {employmentId} ended on day {day}: {note} (kept as history).");
            }
        }

        /// <summary>
        /// Creates the ledger and inventory for a dynamically formed household
        /// (H6). Opening cash is zero: formation moves no cash, so conservation
        /// holds by construction.
        /// </summary>
        private void EnsureHouseholdEconomics(int householdId)
        {
            if (!ledgers.ContainsKey(householdId))
            {
                HouseholdLedger ledger = ledgerRegistry.GetOrCreate(householdId);
                ledgers[householdId] = ledger;
            }
            if (!inventories.ContainsKey(householdId))
            {
                inventories[householdId] = new HouseholdInventory(householdId);
            }
        }

        /// <summary>
        /// Annual eligibility review (section 41): every active employment is
        /// checked against the birth-date authority (working age 14+). A review
        /// records findings; it changes nothing.
        /// </summary>
        private void RunAnnualEligibilityReview(int day)
        {
            int reviewed = 0;
            int flagged = 0;
            CurrentDate(out int y, out int m, out int d);
            foreach (string bizId in businesses.Keys)
            {
                foreach (EmploymentRelationship emp in employments.GetActiveByEmployer(bizId))
                {
                    reviewed++;
                    PersonState worker = FindPerson(emp.EmployeePersonId);
                    if (worker == null || worker.deathDayIndex >= 0)
                    {
                        flagged++;
                        continue;
                    }
                    if (birthDates.TryGetValue(worker.id, out BirthDate birth))
                    {
                        if (AgeAuthority.CompletedYears(birth, y, m, d) < 14)
                        {
                            flagged++;
                        }
                    }
                }
            }
            Log($"annual eligibility review (day {day}): {reviewed} employments reviewed, {flagged} flagged.");
        }

        private void OpenAndSettleEstate(int decedentId, int day)
        {
            PersonState decedent = FindPerson(decedentId);
            Estate estate = estates.OpenEstate(
                ids, decedentId,
                decedent != null ? decedent.DisplayName : $"P{decedentId}",
                kinship, day, diag);
            if (estate == null)
            {
                diag.Add("society: estate opening failed.");
                return;
            }
            // Intestate: the son inherits.
            estates.ComputeIntestateShares(estate, new List<int>(), new List<int> { P9Smith }, diag);
            estates.AppointExecutor(estate, P9Smith, diag);
            PersonState heir = FindPerson(P9Smith);
            string refusal = estates.DistributeParcel(estate, "parcel-h4-forge", P9Smith,
                heir != null ? heir.DisplayName : "P9", ids, titles, day, diag);
            if (refusal != null)
            {
                diag.Add($"society: parcel distribution: {refusal}");
            }
            estates.CloseEstate(estate, diag);
            Log($"estate {estate.EstateId} settled: parcel-h4-forge to P{P9Smith} (sole heir), estate closed.");
        }

        private void ReleaseDayReservations(int day)
        {
            // Day-scoped reservations must never leak overnight.
            if (equipment.IsReserved(OvenAssetKey))
            {
                equipment.Release(OvenAssetKey);
                diag.Add($"society: oven reservation leaked past day {day}; released defensively.");
            }
        }

        // =====================================================================
        // Reconciliation: cash, goods, time, equipment, identity
        // =====================================================================

        private static readonly string[] GoodsItems =
            { "wheat", "flour", "bread", "iron", "tools", "salt", "soap" };

        private static string[] AllGoods() => GoodsItems;

        private long TotalCashCents()
        {
            long total = 0;
            foreach (HouseholdLedger ledger in ledgers.Values)
            {
                total += ledger.GetBalanceCents();
            }
            foreach (BusinessRuntimeState business in businesses.Values)
            {
                total += business.CurrentCashCents;
            }
            return total;
        }

        private long TotalGoodsUnits(string item)
        {
            long total = 0;
            foreach (HouseholdInventory inventory in inventories.Values)
            {
                total += inventory.GetTotalUnits(item);
            }
            foreach (var stocks in businessStock.Values)
            {
                GenericInventoryPosition position;
                if (stocks.TryGetValue(item, out position))
                {
                    total += (long)Math.Round(position.Quantity);
                }
            }
            return total;
        }

        private static long Counter(Dictionary<string, long> counter, string item)
        {
            long value;
            return counter.TryGetValue(item, out value) ? value : 0;
        }

        private int ActiveEmploymentCount()
        {
            var seen = new HashSet<int>();
            foreach (string bizId in businesses.Keys)
            {
                foreach (EmploymentRelationship employment in employments.GetActiveByEmployer(bizId))
                {
                    seen.Add(employment.EmployeePersonId);
                }
            }
            return seen.Count;
        }

        /// <summary>
        /// Full conservation audit. Cash: every internal movement conserves, so
        /// the total equals opening cash plus external flows. Goods: per item,
        /// current stock plus everything consumed/converted/sold equals opening
        /// stock plus everything produced/bought. Time, equipment, and identity
        /// are checked structurally.
        /// </summary>
        public SocietyReconciliation Reconcile()
        {
            var report = new SocietyReconciliation { Clean = true };

            long cashNow = TotalCashCents();
            long cashExpected = initialCashCents + externalCashInflowsCents - externalCashOutflowsCents;
            if (cashNow != cashExpected)
            {
                report.Clean = false;
                report.Add($"cash: now {cashNow}c but expected {cashExpected}c " +
                    $"(opened {initialCashCents}c, external in {externalCashInflowsCents}c, external out {externalCashOutflowsCents}c)");
            }

            foreach (string item in GoodsItems)
            {
                long have = TotalGoodsUnits(item);
                long accounted = have + Counter(consumedUnits, item)
                    + Counter(productionInputsConsumed, item) + Counter(soldOffMapUnits, item);
                long expected = Counter(initialGoods, item) + Counter(producedUnits, item)
                    + Counter(boughtOffMapUnits, item);
                if (accounted != expected)
                {
                    report.Clean = false;
                    report.Add($"goods {item}: stock {have}u + consumed {Counter(consumedUnits, item)}u + " +
                        $"inputs {Counter(productionInputsConsumed, item)}u = {accounted}u, " +
                        $"expected {expected}u (opened {Counter(initialGoods, item)}u + " +
                        $"produced {Counter(producedUnits, item)}u + bought off-map {Counter(boughtOffMapUnits, item)}u)");
                }
            }

            foreach (var kv in personMinutesWorked)
            {
                int cap = absoluteDayIndex * WorkTimeBudget.DefaultDailyWorkMinutes;
                if (kv.Value > cap)
                {
                    report.Clean = false;
                    report.Add($"time: P{kv.Key} worked {kv.Value} min over {absoluteDayIndex} days " +
                        $"(budget cap {cap} min): manufactured labor time");
                }
            }

            foreach (PersonState person in population.people)
            {
                List<WorkTask> open = tasks.GetOpenTasks(EntityId.For(EntityKind.Person, person.id));
                if (open != null && open.Count > 0)
                {
                    report.Clean = false;
                    report.Add($"time: P{person.id} has {open.Count} open tasks at day end");
                }
            }

            int expectedReservations = offMap.IsOffMapVehicle("wagon-1") ? 1 : 0;
            if (equipment.ReservedCount != expectedReservations)
            {
                report.Clean = false;
                report.Add($"equipment: {equipment.ReservedCount} reservations held, " +
                    $"expected {expectedReservations} (wagon-1 while off-map)");
            }

            var seenIds = new HashSet<int>();
            foreach (PersonState person in population.people)
            {
                if (!seenIds.Add(person.id))
                {
                    report.Clean = false;
                    report.Add($"identity: duplicate person id {person.id}");
                }
            }

            foreach (HouseholdLedger ledger in ledgers.Values)
            {
                if (ledger.State.NextEntrySequence != ledger.Entries.Count)
                {
                    report.Clean = false;
                    report.Add($"identity: H{ledger.HouseholdId} ledger sequence " +
                        $"{ledger.State.NextEntrySequence} != entries {ledger.Entries.Count} + 1");
                }
            }

            int livingPersons = 0;
            foreach (PersonState person in population.people)
            {
                if (person.deathDayIndex < 0)
                {
                    livingPersons++;
                }
            }
            if (dueEvents.ScheduledCount != livingPersons)
            {
                report.Clean = false;
                report.Add($"birthdays: {dueEvents.ScheduledCount} scheduled, {livingPersons} living persons " +
                    "(exactly one birthday event per living person)");
            }

            report.CashCents = cashNow;
            report.DayIndex = absoluteDayIndex;
            return report;
        }

        /// <summary>Debug helper: per-holder cash breakdown.</summary>
        public string CashDetail()
        {
            var parts = new List<string>();
            var bizIds = new List<string>(businesses.Keys);
            bizIds.Sort(StringComparer.Ordinal);
            foreach (string bizId in bizIds)
            {
                parts.Add(bizId + "=" + businesses[bizId].CurrentCashCents);
            }
            var hhIds = new List<int>(ledgers.Keys);
            hhIds.Sort();
            foreach (int hhId in hhIds)
            {
                parts.Add("H" + hhId + "=" + ledgers[hhId].GetBalanceCents());
            }
            parts.Add("extIn=" + externalCashInflowsCents);
            parts.Add("extOut=" + externalCashOutflowsCents);
            return string.Join(",", parts.ToArray());
        }

        /// <summary>
        /// Deterministic fingerprint of the full simulation state, for tick-size
        /// and save/load consistency comparisons.
        /// </summary>
        public string Fingerprint()
        {
            var parts = new List<string>
            {
                "day=" + absoluteDayIndex,
                "cash=" + TotalCashCents(),
                "extOut=" + externalCashOutflowsCents,
                "wages=" + wagesPaidCents,
                "employments=" + ActiveEmploymentCount(),
                "journeys=" + offMap.ActiveJourneyCount,
                "birthdays=" + dueEvents.ScheduledCount,
                "persons=" + population.people.Count,
                "unmet=" + unmetNeeds,
                "shortages=" + shortages,
                "hunger=" + hungerEvents,
                "arrears=" + payrollArrearsWeeks,
                "delinq=" + creditDelinquencies,
                "events=" + eventLog.Count,
            };
            foreach (string item in GoodsItems)
            {
                parts.Add($"{item}=stock:{TotalGoodsUnits(item)}+prod:{Counter(producedUnits, item)}" +
                    $"+cons:{Counter(consumedUnits, item)}+in:{Counter(productionInputsConsumed, item)}" +
                    $"+offmap:{Counter(boughtOffMapUnits, item)}");
            }
            var worked = new List<string>();
            var personIds = new List<int>(personMinutesWorked.Keys);
            personIds.Sort();
            foreach (int personId in personIds)
            {
                worked.Add(personId + ":" + personMinutesWorked[personId]);
            }
            parts.Add("worked=" + string.Join(",", worked.ToArray()));
            return string.Join("|", parts.ToArray());
        }

        // =====================================================================
        // Save / load (clear-then-load, composed from authority DTOs)
        // =====================================================================

        public AutonomousSocietySaveDto CaptureSaveDto()
        {
            var dto = new AutonomousSocietySaveDto
            {
                dayIndex = absoluteDayIndex,
                tickMinuteAccumulator = tickMinuteAccumulator,
                demographicEvents = demographicEvents,
                p11Hired = p11Hired,
                birthDone = birthDone,
                migrationDone = migrationDone,
                deathDone = deathDone,
                h5CreditObligationId = h5CreditObligationId,
                nextMembershipId = nextMembershipId,
                initialCashCents = initialCashCents,
                externalCashOutflowsCents = externalCashOutflowsCents,
                externalCashInflowsCents = externalCashInflowsCents,
                pendingStoreRun = pendingStoreRun,
                pendingForgeRun = pendingForgeRun,
                unmetNeeds = unmetNeeds,
                shortages = shortages,
                hungerEvents = hungerEvents,
                payrollArrearsWeeks = payrollArrearsWeeks,
                creditDelinquencies = creditDelinquencies,
                rentArrearsCents = rentArrearsCents,
                equipmentContentions = equipmentContentions,
                wagesPaidCents = wagesPaidCents,
                birthsRecorded = birthsRecorded,
                deathsRecorded = deathsRecorded,
                birthdaysProcessed = birthdaysProcessed,
                people = new List<PersonState>(population.people),
                households = new List<HouseholdState>(population.households),
                nextPersonId = population.NextPersonId,
                nextHouseholdId = population.NextHouseholdId,
                memberships = memberships.CaptureSaveDto(),
                kinship = kinship.CaptureSaveDto(),
                employments = employments.CaptureSaveDto(),
                wages = wageService.CaptureSaveDto(),
                taskAuthority = tasks.CaptureSaveDto(),
                budgets = budgets.CaptureSaveDto(),
                obligations = obligations.CaptureSaveDto(),
                equipment = equipment.CaptureSaveDto(),
                journeys = offMap.CaptureSaveDto(),
                journeyModel = journeyModel.CaptureSaveDto(),
                dueEvents = dueEvents.CaptureSaveDto(),
                titles = titles.CaptureSaveDto(),
                idCursors = ids.SnapshotCursors(),
                eventLog = new List<string>(eventLog),
            };
            foreach (var kv in birthDates)
            {
                dto.birthDates.Add(new PersonBirthDateRecord { personId = kv.Key, birth = kv.Value });
            }
            foreach (var kv in ledgers)
            {
                dto.ledgers.Add(kv.Value.State);
            }
            foreach (var kv in inventories)
            {
                dto.inventories.Add(kv.Value.State);
            }
            var bizIds = new List<string>(businesses.Keys);
            bizIds.Sort(StringComparer.Ordinal);
            foreach (string bizId in bizIds)
            {
                dto.businesses.Add(new BusinessSaveRecord
                {
                    businessId = bizId,
                    state = businesses[bizId].CaptureSaveDto(),
                });
                foreach (var stock in businessStock[bizId])
                {
                    dto.businessStock.Add(new BusinessStockRecord
                    {
                        businessId = bizId,
                        productId = stock.Key,
                        position = stock.Value,
                    });
                }
                ProductionProcessState process;
                if (activeProcesses.TryGetValue(bizId, out process))
                {
                    dto.activeProcesses.Add(new ActiveProcessRecord { businessId = bizId, process = process });
                }
            }
            CopyCounter(dto.initialGoods, initialGoods);
            CopyCounter(dto.producedUnits, producedUnits);
            CopyCounter(dto.productionInputsConsumed, productionInputsConsumed);
            CopyCounter(dto.consumedUnits, consumedUnits);
            CopyCounter(dto.boughtOffMapUnits, boughtOffMapUnits);
            CopyCounter(dto.soldOffMapUnits, soldOffMapUnits);
            foreach (var kv in personMinutesWorked)
            {
                dto.personMinutesWorked.Add(new PersonMinutesRecord { personId = kv.Key, minutes = kv.Value });
            }
            // The snapshot must own its data: deep-copy so the live driver and
            // the DTO never alias the same mutable objects.
            return SnapshotDeepCopier.Copy(dto);
        }

        private static void CopyCounter(List<GoodsCounterRecord> target, Dictionary<string, long> source)
        {
            foreach (var kv in source)
            {
                target.Add(new GoodsCounterRecord { item = kv.Key, units = kv.Value });
            }
        }

        private static Dictionary<string, long> ToCounter(List<GoodsCounterRecord> records)
        {
            var dict = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            if (records != null)
            {
                foreach (GoodsCounterRecord record in records)
                {
                    dict[record.item] = record.units;
                }
            }
            return dict;
        }

        /// <summary>Clear-then-load: every authority is rebuilt from the DTO.</summary>
        public void LoadFromSaveDto(AutonomousSocietySaveDto dto)
        {
            if (dto == null)
            {
                return;
            }
            var fresh = new AutonomousSocietyDriver();
            fresh.demographicEvents = dto.demographicEvents;
            fresh.absoluteDayIndex = dto.dayIndex;
            fresh.tickMinuteAccumulator = dto.tickMinuteAccumulator;
            fresh.p11Hired = dto.p11Hired;
            fresh.birthDone = dto.birthDone;
            fresh.migrationDone = dto.migrationDone;
            fresh.deathDone = dto.deathDone;
            fresh.h5CreditObligationId = dto.h5CreditObligationId ?? string.Empty;
            fresh.nextMembershipId = dto.nextMembershipId;

            fresh.population = new PopulationState();
            fresh.population.people = dto.people ?? new List<PersonState>();
            fresh.population.households = dto.households ?? new List<HouseholdState>();
            fresh.population.RestorePersonIdAllocator(dto.nextPersonId);
            fresh.population.RestoreHouseholdIdAllocator(dto.nextHouseholdId);
            fresh.memberships = new HouseholdMembershipRegistry();
            fresh.memberships.LoadFromSaveDto(dto.memberships);
            fresh.kinship = new KinshipRegistry();
            fresh.kinship.LoadFromSaveDto(dto.kinship);
            fresh.ids = new EntityIdRegistry();
            fresh.ids.RestoreCursors(dto.idCursors);
            fresh.lifecycle = new HouseholdLifecycleManager(fresh.population, fresh.memberships, fresh.kinship, fresh.ids);
            fresh.birthDates.Clear();
            if (dto.birthDates != null)
            {
                foreach (PersonBirthDateRecord record in dto.birthDates)
                {
                    fresh.birthDates[record.personId] = record.birth;
                }
            }

            fresh.tasks = new TaskAuthority();
            fresh.tasks.LoadFromSaveDto(dto.taskAuthority);
            fresh.budgets = new WorkTimeBudgetStore();
            fresh.budgets.LoadFromSaveDto(dto.budgets, dto.dayIndex);
            fresh.employments = new EmploymentRelationshipRegistry();
            fresh.employments.LoadFromSaveDto(dto.employments);
            fresh.wageService = new WageSettlementService();
            fresh.wageService.LoadFromSaveDto(dto.wages);
            fresh.obligations = new FinancialObligationAuthority();
            fresh.obligations.LoadFromSaveDto(dto.obligations);
            fresh.equipment = new EquipmentReservationService();
            fresh.equipment.RegisterResourceCapacity(OvenAssetKey, 1f);
            fresh.equipment.RegisterResourceCapacity(WagonAssetKey, 1f);
            fresh.equipment.LoadFromSaveDto(dto.equipment);
            fresh.dueEvents = new DueEventScheduler();
            fresh.dueEvents.LoadFromSaveDto(dto.dueEvents);
            fresh.estates = new EstateService();
            fresh.titles = new TitleAuthority();
            fresh.titles.LoadFromSaveDto(dto.titles);
            fresh.journeyModel = new JourneyModel();
            fresh.journeyModel.LoadFromSaveDto(dto.journeyModel);
            fresh.offMap = new OffMapJourneyAuthority(fresh.journeyModel);
            fresh.offMap.LoadFromSaveDto(dto.journeys);

            fresh.ledgerRegistry = new HouseholdLedgerRegistry();
            fresh.ledgerRegistry.ImportState(dto.ledgers);
            fresh.ledgers.Clear();
            if (dto.ledgers != null)
            {
                foreach (HouseholdLedgerState state in dto.ledgers)
                {
                    if (state != null)
                    {
                        fresh.ledgers[state.HouseholdId] = fresh.ledgerRegistry.Get(state.HouseholdId);
                    }
                }
            }
            fresh.inventories.Clear();
            if (dto.inventories != null)
            {
                foreach (HouseholdInventoryState state in dto.inventories)
                {
                    if (state != null)
                    {
                        fresh.inventories[state.HouseholdId] = new HouseholdInventory(state);
                    }
                }
            }

            fresh.tasks.RegisterDefinition(new TaskDefinition("work-shift", "Work shift", WorkShiftMinutes), out _);
            fresh.tasks.RegisterDefinition(new TaskDefinition("domestic-work", "Domestic work", 240), out _);
            fresh.tasks.RegisterDefinition(new TaskDefinition("domestic-help", "Domestic help", 120), out _);
            JourneyTravel.RegisterTaskDefinitions(fresh.tasks);

            fresh.BuildMethodsAndPolicies();
            if (dto.businesses != null)
            {
                foreach (BusinessSaveRecord record in dto.businesses)
                {
                    if (record == null || string.IsNullOrEmpty(record.businessId))
                    {
                        continue;
                    }
                    BusinessRuntimeState state = BusinessRuntimeState.FromSaveDto(
                        record.state, FallbackBusinessType(record.businessId));
                    fresh.businesses[record.businessId] = state;
                    fresh.businessStock[record.businessId] =
                        new Dictionary<string, GenericInventoryPosition>(StringComparer.OrdinalIgnoreCase);
                    var runtime = new GenericProductionRuntime(
                        fresh.tasks, fresh.budgets,
                        EntityId.For(EntityKind.Business, BusinessIndex(record.businessId)), dto.dayIndex);
                    fresh.productionRuntimes[record.businessId] = runtime;
                }
            }
            if (dto.businessStock != null)
            {
                foreach (BusinessStockRecord record in dto.businessStock)
                {
                    if (record == null || record.position == null)
                    {
                        continue;
                    }
                    Dictionary<string, GenericInventoryPosition> stocks;
                    if (!fresh.businessStock.TryGetValue(record.businessId, out stocks))
                    {
                        stocks = new Dictionary<string, GenericInventoryPosition>(StringComparer.OrdinalIgnoreCase);
                        fresh.businessStock[record.businessId] = stocks;
                    }
                    stocks[record.productId] = record.position;
                }
            }
            if (dto.activeProcesses != null)
            {
                foreach (ActiveProcessRecord record in dto.activeProcesses)
                {
                    if (record == null || record.process == null)
                    {
                        continue;
                    }
                    fresh.activeProcesses[record.businessId] = record.process;
                    fresh.productionRuntimes[record.businessId].RegisterMethod(fresh.methods[record.businessId]);
                }
            }

            fresh.initialCashCents = dto.initialCashCents;
            fresh.externalCashOutflowsCents = dto.externalCashOutflowsCents;
            fresh.externalCashInflowsCents = dto.externalCashInflowsCents;
            fresh.pendingStoreRun = dto.pendingStoreRun;
            fresh.pendingForgeRun = dto.pendingForgeRun;
            fresh.initialGoods.Clear();
            foreach (var kv in ToCounter(dto.initialGoods)) fresh.initialGoods[kv.Key] = kv.Value;
            fresh.producedUnits.Clear();
            foreach (var kv in ToCounter(dto.producedUnits)) fresh.producedUnits[kv.Key] = kv.Value;
            fresh.productionInputsConsumed.Clear();
            foreach (var kv in ToCounter(dto.productionInputsConsumed)) fresh.productionInputsConsumed[kv.Key] = kv.Value;
            fresh.consumedUnits.Clear();
            foreach (var kv in ToCounter(dto.consumedUnits)) fresh.consumedUnits[kv.Key] = kv.Value;
            fresh.boughtOffMapUnits.Clear();
            foreach (var kv in ToCounter(dto.boughtOffMapUnits)) fresh.boughtOffMapUnits[kv.Key] = kv.Value;
            fresh.soldOffMapUnits.Clear();
            foreach (var kv in ToCounter(dto.soldOffMapUnits)) fresh.soldOffMapUnits[kv.Key] = kv.Value;
            fresh.personMinutesWorked.Clear();
            if (dto.personMinutesWorked != null)
            {
                foreach (PersonMinutesRecord record in dto.personMinutesWorked)
                {
                    fresh.personMinutesWorked[record.personId] = record.minutes;
                }
            }
            fresh.unmetNeeds = dto.unmetNeeds;
            fresh.shortages = dto.shortages;
            fresh.hungerEvents = dto.hungerEvents;
            fresh.payrollArrearsWeeks = dto.payrollArrearsWeeks;
            fresh.creditDelinquencies = dto.creditDelinquencies;
            fresh.rentArrearsCents = dto.rentArrearsCents;
            fresh.equipmentContentions = dto.equipmentContentions;
            fresh.wagesPaidCents = dto.wagesPaidCents;
            fresh.birthsRecorded = dto.birthsRecorded;
            fresh.deathsRecorded = dto.deathsRecorded;
            fresh.birthdaysProcessed = dto.birthdaysProcessed;
            fresh.eventLog.Clear();
            if (dto.eventLog != null)
            {
                fresh.eventLog.AddRange(dto.eventLog);
            }

            CopyInto(fresh);
        }

        private void BuildMethodsAndPolicies()
        {
            // Rebuilds the static production methods/policies (configuration, not state).
            var seed = new AutonomousSocietyDriver();
            seed.BuildBusinessConfigurationsOnly();
            foreach (var kv in seed.methods) methods[kv.Key] = kv.Value;
            foreach (var kv in seed.policies) policies[kv.Key] = kv.Value;
        }

        private void BuildBusinessConfigurationsOnly()
        {
            methods[MillId] = new ProductionMethodDefinition
            {
                MethodId = "mill-wheat-flour",
                OutputProductId = "flour",
                OutputQuantity = 90f,
                InputRequirements = new List<ProductionInputRequirement>
                {
                    new ProductionInputRequirement { ProductId = "wheat", Quantity = 100f },
                },
                Phases = new List<ProductionPhaseDefinition>
                {
                    new ProductionPhaseDefinition { PhaseId = "grind", ElapsedMinutes = 360, RequiresPerson = true },
                },
            };
            policies[MillId] = new GenericProductionPolicy
            {
                MethodId = "mill-wheat-flour",
                Mode = GenericProductionPolicyMode.MakeToStock,
                TargetStock = 400f,
                BatchSize = 1f,
                Priority = 2,
            };
            methods[BakeryId] = new ProductionMethodDefinition
            {
                MethodId = "bake-flour-bread",
                OutputProductId = "bread",
                OutputQuantity = 60f,
                InputRequirements = new List<ProductionInputRequirement>
                {
                    new ProductionInputRequirement { ProductId = "flour", Quantity = 50f },
                },
                RequiredEquipmentIds = new List<string> { OvenAssetKey },
                Phases = new List<ProductionPhaseDefinition>
                {
                    new ProductionPhaseDefinition
                    {
                        PhaseId = "bake", ElapsedMinutes = 240, RequiresPerson = true,
                        ReservedEquipmentIds = new List<string> { OvenAssetKey },
                    },
                    new ProductionPhaseDefinition { PhaseId = "cool", ElapsedMinutes = 120, RequiresPerson = false },
                },
            };
            policies[BakeryId] = new GenericProductionPolicy
            {
                MethodId = "bake-flour-bread",
                Mode = GenericProductionPolicyMode.MakeToStock,
                TargetStock = 300f,
                BatchSize = 1f,
                Priority = 3,
            };
            methods[ForgeId] = new ProductionMethodDefinition
            {
                MethodId = "forge-iron-tools",
                OutputProductId = "tools",
                OutputQuantity = 10f,
                InputRequirements = new List<ProductionInputRequirement>
                {
                    new ProductionInputRequirement { ProductId = "iron", Quantity = 20f },
                },
                Phases = new List<ProductionPhaseDefinition>
                {
                    new ProductionPhaseDefinition { PhaseId = "forge", ElapsedMinutes = 420, RequiresPerson = true },
                },
            };
            policies[ForgeId] = new GenericProductionPolicy
            {
                MethodId = "forge-iron-tools",
                Mode = GenericProductionPolicyMode.MakeToStock,
                TargetStock = 40f,
                BatchSize = 1f,
                Priority = 1,
            };
        }

        private static BusinessType FallbackBusinessType(string bizId)
        {
            if (bizId == BakeryId) return BusinessType.Bakery;
            if (bizId == ForgeId) return BusinessType.Blacksmith;
            if (bizId == StoreId) return BusinessType.GeneralStore;
            return BusinessType.GrainMill;
        }

        private void CopyInto(AutonomousSocietyDriver fresh)
        {
            absoluteDayIndex = fresh.absoluteDayIndex;
            tickMinuteAccumulator = fresh.tickMinuteAccumulator;
            demographicEvents = fresh.demographicEvents;
            p11Hired = fresh.p11Hired;
            birthDone = fresh.birthDone;
            migrationDone = fresh.migrationDone;
            deathDone = fresh.deathDone;
            h5CreditObligationId = fresh.h5CreditObligationId;
            nextMembershipId = fresh.nextMembershipId;
            population = fresh.population;
            memberships = fresh.memberships;
            kinship = fresh.kinship;
            ids = fresh.ids;
            lifecycle = fresh.lifecycle;
            birthDates.Clear();
            foreach (var kv in fresh.birthDates) birthDates[kv.Key] = kv.Value;
            ledgers.Clear();
            foreach (var kv in fresh.ledgers) ledgers[kv.Key] = kv.Value;
            ledgerRegistry = fresh.ledgerRegistry;
            inventories.Clear();
            foreach (var kv in fresh.inventories) inventories[kv.Key] = kv.Value;
            businesses.Clear();
            foreach (var kv in fresh.businesses) businesses[kv.Key] = kv.Value;
            businessStock.Clear();
            foreach (var kv in fresh.businessStock) businessStock[kv.Key] = kv.Value;
            productionRuntimes.Clear();
            foreach (var kv in fresh.productionRuntimes) productionRuntimes[kv.Key] = kv.Value;
            methods.Clear();
            foreach (var kv in fresh.methods) methods[kv.Key] = kv.Value;
            policies.Clear();
            foreach (var kv in fresh.policies) policies[kv.Key] = kv.Value;
            activeProcesses.Clear();
            foreach (var kv in fresh.activeProcesses) activeProcesses[kv.Key] = kv.Value;
            employments = fresh.employments;
            wageService = fresh.wageService;
            tasks = fresh.tasks;
            budgets = fresh.budgets;
            obligations = fresh.obligations;
            equipment = fresh.equipment;
            journeyModel = fresh.journeyModel;
            offMap = fresh.offMap;
            // Rewire business authorities AFTER the fresh registries are in
            // place: the rebuilt businesses must see the restored employment
            // registry (with day-15 hires), household ledgers, and lookup.
            foreach (var kv in businesses)
            {
                kv.Value.EmploymentRegistry = employments;
                kv.Value.HouseholdLedgers = ledgerRegistry;
                kv.Value.PersonHouseholdIdLookup = memberships.GetActiveHouseholdId;
            }
            dueEvents = fresh.dueEvents;
            estates = fresh.estates;
            titles = fresh.titles;
            initialCashCents = fresh.initialCashCents;
            externalCashOutflowsCents = fresh.externalCashOutflowsCents;
            externalCashInflowsCents = fresh.externalCashInflowsCents;
            pendingStoreRun = fresh.pendingStoreRun;
            pendingForgeRun = fresh.pendingForgeRun;
            initialGoods.Clear();
            foreach (var kv in fresh.initialGoods) initialGoods[kv.Key] = kv.Value;
            producedUnits.Clear();
            foreach (var kv in fresh.producedUnits) producedUnits[kv.Key] = kv.Value;
            productionInputsConsumed.Clear();
            foreach (var kv in fresh.productionInputsConsumed) productionInputsConsumed[kv.Key] = kv.Value;
            consumedUnits.Clear();
            foreach (var kv in fresh.consumedUnits) consumedUnits[kv.Key] = kv.Value;
            boughtOffMapUnits.Clear();
            foreach (var kv in fresh.boughtOffMapUnits) boughtOffMapUnits[kv.Key] = kv.Value;
            soldOffMapUnits.Clear();
            foreach (var kv in fresh.soldOffMapUnits) soldOffMapUnits[kv.Key] = kv.Value;
            personMinutesWorked.Clear();
            foreach (var kv in fresh.personMinutesWorked) personMinutesWorked[kv.Key] = kv.Value;
            unmetNeeds = fresh.unmetNeeds;
            shortages = fresh.shortages;
            hungerEvents = fresh.hungerEvents;
            payrollArrearsWeeks = fresh.payrollArrearsWeeks;
            creditDelinquencies = fresh.creditDelinquencies;
            rentArrearsCents = fresh.rentArrearsCents;
            equipmentContentions = fresh.equipmentContentions;
            wagesPaidCents = fresh.wagesPaidCents;
            birthsRecorded = fresh.birthsRecorded;
            deathsRecorded = fresh.deathsRecorded;
            birthdaysProcessed = fresh.birthdaysProcessed;
            eventLog.Clear();
            eventLog.AddRange(fresh.eventLog);
        }
    // Snapshot deep copy (save DTOs own their data)
    // =====================================================================

    /// <summary>
    /// Test-support deep copier: CaptureSaveDto snapshots must OWN their data.
    /// A save DTO that aliases live authority objects lets a later Load corrupt
    /// the running driver (two drivers writing the same ledger states). This
    /// copier clones the full object graph (including private serialized fields)
    /// so the DTO is independent of the live simulation. The graphs are trees;
    /// a reference guard protects against accidental cycles.
    /// </summary>
    private static class SnapshotDeepCopier
    {
        public static T Copy<T>(T source)
        {
            var seen = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
            return (T)CopyValue(source, typeof(T), seen);
        }

        private static object CopyValue(object source, Type type, Dictionary<object, object> seen)
        {
            if (source == null)
            {
                return null;
            }
            if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal))
            {
                return source;
            }
            object existing;
            if (!type.IsValueType && seen.TryGetValue(source, out existing))
            {
                return existing;
            }
            if (type.IsArray)
            {
                var array = (Array)source;
                Type elementType = type.GetElementType();
                Array copy = Array.CreateInstance(elementType, array.Length);
                if (!type.IsValueType)
                {
                    seen[source] = copy;
                }
                for (int i = 0; i < array.Length; i++)
                {
                    copy.SetValue(CopyValue(array.GetValue(i), elementType, seen), i);
                }
                return copy;
            }
            if (typeof(System.Collections.IDictionary).IsAssignableFrom(type))
            {
                object copy = Activator.CreateInstance(type);
                seen[source] = copy;
                var dict = (System.Collections.IDictionary)source;
                var copyDict = (System.Collections.IDictionary)copy;
                Type[] args = type.IsGenericType ? type.GetGenericArguments() : new[] { typeof(object), typeof(object) };
                foreach (System.Collections.DictionaryEntry entry in dict)
                {
                    copyDict[CopyValue(entry.Key, args[0], seen)] = CopyValue(entry.Value, args[1], seen);
                }
                return copy;
            }
            if (typeof(System.Collections.IList).IsAssignableFrom(type))
            {
                object copy = Activator.CreateInstance(type);
                seen[source] = copy;
                var list = (System.Collections.IList)source;
                var copyList = (System.Collections.IList)copy;
                Type elementType = type.IsGenericType ? type.GetGenericArguments()[0] : typeof(object);
                foreach (object item in list)
                {
                    copyList.Add(CopyValue(item, elementType, seen));
                }
                return copy;
            }
            if (type.IsValueType)
            {
                object copy = source; // box a copy of the struct
                CopyFields(source, copy, type, seen);
                return copy;
            }
            // Reference type: construct uninitialized, then copy every instance field.
            object instance = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type);
            seen[source] = instance;
            CopyFields(source, instance, type, seen);
            return instance;
        }

        private static void CopyFields(object source, object target, Type type, Dictionary<object, object> seen)
        {
            Type current = type;
            while (current != null && current != typeof(object))
            {
                var fields = current.GetFields(
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.DeclaredOnly);
                foreach (var field in fields)
                {
                    if (field.FieldType.IsPointer)
                    {
                        continue;
                    }
                    object value = field.GetValue(source);
                    field.SetValue(target, CopyValue(value, field.FieldType, seen));
                }
                current = current.BaseType;
            }
        }

        private sealed class ReferenceEqualityComparer : System.Collections.Generic.IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
    }

    // =====================================================================
    // Save DTOs and reconciliation report
    // =====================================================================

    /// <summary>Person id plus authoritative birth date (P0-2 contract).</summary>
    [Serializable]
    public sealed class PersonBirthDateRecord
    {
        public int personId = -1;
        public BirthDate birth;
    }

    [Serializable]
    public sealed class BusinessSaveRecord
    {
        public string businessId = string.Empty;
        public BusinessRuntimeSaveDto state;
    }

    [Serializable]
    public sealed class BusinessStockRecord
    {
        public string businessId = string.Empty;
        public string productId = string.Empty;
        public GenericInventoryPosition position;
    }

    [Serializable]
    public sealed class ActiveProcessRecord
    {
        public string businessId = string.Empty;
        public ProductionProcessState process;
    }

    [Serializable]
    public sealed class GoodsCounterRecord
    {
        public string item = string.Empty;
        public long units;
    }

    [Serializable]
    public sealed class PersonMinutesRecord
    {
        public int personId = -1;
        public int minutes;
    }

    /// <summary>
    /// Full save capture for the autonomous society driver, composed from the
    /// authority DTOs (clear-then-load throughout).
    /// </summary>
    [Serializable]
    public sealed class AutonomousSocietySaveDto
    {
        public int dayIndex;
        public int tickMinuteAccumulator;
        public bool demographicEvents;
        public bool p11Hired;
        public bool birthDone;
        public bool migrationDone;
        public bool deathDone;
        public bool birth2Done;
        public bool p6Promoted;
        public bool h6Formed;
        public int h6Id = -1;
        public bool birth3Done;
        public bool p11DeathDone;
        public bool birth4Done;
        public bool p3ForgeHired;
        public bool p8LongHaulDone;
        public bool p6BakeryHired;
        public int eligibilityReviewsDone;
        public int birthdaysProcessed;
        public string h5CreditObligationId = string.Empty;
        public int nextMembershipId;
        public long initialCashCents;
        public long externalCashOutflowsCents;
        public long externalCashInflowsCents;
        public bool pendingStoreRun;
        public bool pendingForgeRun;
        public int unmetNeeds;
        public int shortages;
        public int hungerEvents;
        public int payrollArrearsWeeks;
        public int creditDelinquencies;
        public int rentArrearsCents;
        public int equipmentContentions;
        public int wagesPaidCents;
        public int birthsRecorded;
        public int deathsRecorded;
        public List<PersonState> people = new List<PersonState>();
        public List<HouseholdState> households = new List<HouseholdState>();
        public int nextPersonId;
        public int nextHouseholdId;
        public List<PersonBirthDateRecord> birthDates = new List<PersonBirthDateRecord>();
        public HouseholdMembershipRegistrySaveDto memberships;
        public KinshipRegistrySaveDto kinship;
        public List<HouseholdLedgerState> ledgers = new List<HouseholdLedgerState>();
        public List<HouseholdInventoryState> inventories = new List<HouseholdInventoryState>();
        public List<BusinessSaveRecord> businesses = new List<BusinessSaveRecord>();
        public List<BusinessStockRecord> businessStock = new List<BusinessStockRecord>();
        public List<ActiveProcessRecord> activeProcesses = new List<ActiveProcessRecord>();
        public EmploymentRegistrySaveDto employments;
        public WageSettlementSaveDto wages;
        public TaskAuthoritySaveDto taskAuthority;
        public WorkTimeBudgetSaveDto budgets;
        public FinancialObligationSaveDto obligations;
        public EquipmentReservationAuthoritySaveDto equipment;
        public OffMapJourneyAuthoritySaveDto journeys;
        public JourneyModelSaveDto journeyModel;
        public DueEventSchedulerSaveDto dueEvents;
        public TitleAuthority.TitleSaveDto titles;
        public List<EntityIdCursor> idCursors = new List<EntityIdCursor>();
        public List<GoodsCounterRecord> initialGoods = new List<GoodsCounterRecord>();
        public List<GoodsCounterRecord> producedUnits = new List<GoodsCounterRecord>();
        public List<GoodsCounterRecord> productionInputsConsumed = new List<GoodsCounterRecord>();
        public List<GoodsCounterRecord> consumedUnits = new List<GoodsCounterRecord>();
        public List<GoodsCounterRecord> boughtOffMapUnits = new List<GoodsCounterRecord>();
        public List<GoodsCounterRecord> soldOffMapUnits = new List<GoodsCounterRecord>();
        public List<PersonMinutesRecord> personMinutesWorked = new List<PersonMinutesRecord>();
        public List<string> eventLog = new List<string>();
    }

    /// <summary>Conservation audit result for one society run.</summary>
    public sealed class SocietyReconciliation
    {
        public bool Clean = true;
        public int DayIndex;
        public long CashCents;
        public readonly List<string> Divergences = new List<string>();

        public void Add(string divergence)
        {
            Divergences.Add(divergence);
        }
    }
}
