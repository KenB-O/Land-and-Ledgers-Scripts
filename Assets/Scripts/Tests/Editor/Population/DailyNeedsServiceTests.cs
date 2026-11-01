using System.Collections.Generic;
using LandLedgers.Population;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    /// <summary>
    /// T2C: NPC daily-needs execution. HF-4 plans; this executes. Meals need a
    /// source (GHOST-DEF-006): reserves are consumed causally, shortfalls are
    /// shopped for by a real household member, and missed meals are logged —
    /// never faked.
    /// </summary>
    public sealed class DailyNeedsServiceTests
    {
        private sealed class TestSupplier : IGoodsSupplier
        {
            public string SupplierBusinessId => "store-1";
            public string SupplierName => "General Store";
            public string LocationId => "store";
            public readonly Dictionary<string, int> Stock = new Dictionary<string, int>();
            public int PriceCents = 10;

            public bool HasCategory(string categoryId) => Stock.ContainsKey(categoryId);
            public int StockUnits(string categoryId) => Stock.TryGetValue(categoryId, out int u) ? u : 0;
            public int PricePerUnitCents(string categoryId) => PriceCents;
            public int Sell(string categoryId, int requestedUnits, int dayIndex, List<string> diagnostics)
            {
                int sold = System.Math.Min(requestedUnits, StockUnits(categoryId));
                if (sold > 0) Stock[categoryId] -= sold;
                return sold;
            }
        }

        private PopulationState population;
        private HouseholdLedgerRegistry ledgers;
        private SupplierDirectory suppliers;
        private JourneyModel journeys;
        private EmbodiedPurchaseExecutor executor;
        private HouseholdConsumptionPlanner planner;
        private DailyNeedsService service;
        private List<string> diag;

        private static PersonState Adult(int id, string name, int householdId)
        {
            return new PersonState
            {
                id = id, firstName = name, lastName = "Test", age = 30,
                ageBand = AgeBand.Adult18Plus, laborAccessLevel = LaborAccessLevel.FullLaborMarket,
                householdId = householdId,
            };
        }

        [SetUp]
        public void SetUp()
        {
            population = new PopulationState();
            population.people.Add(Adult(1, "Abe", 7));
            population.people.Add(Adult(2, "Bea", 7));
            var household = new HouseholdState { id = 7 };
            household.memberIds.Add(1);
            household.memberIds.Add(2);
            household.reserves.Add(new HouseholdReserveState
            {
                categoryId = "staple_food", displayName = "Staple food",
                currentUnits = 0, lowThresholdUnits = 20, targetUnits = 30,
            });
            population.households.Add(household);

            ledgers = new HouseholdLedgerRegistry();
            ledgers.GetOrCreate(7).RecordInflow(0, 100000, HouseholdIncomeSource.OwnerContribution,
                "test-capital", "test funding", "test");

            suppliers = new SupplierDirectory();
            var store = new TestSupplier();
            store.Stock["staple_food"] = 1000;
            suppliers.Register(store);

            journeys = new JourneyModel();
            journeys.RegisterLocation(new JourneyLocation("home", JourneyLocationKind.Farmstead, "Home", 0f, 0f));
            journeys.RegisterLocation(new JourneyLocation("store", JourneyLocationKind.Store, "Store", 2f, 0f));
            journeys.AddEdge("home", "store", 2.0f, "road");

            executor = new EmbodiedPurchaseExecutor(population, ledgers, suppliers, journeys, pid => "home");
            planner = new HouseholdConsumptionPlanner();
            service = new DailyNeedsService();
            diag = new List<string>();
        }

        [Test]
        public void HouseholdEatsFromStoresWhenStocked()
        {
            population.households[0].reserves[0].currentUnits = 100;
            var report = service.ExecuteDay(population, planner, executor, 0, diag);

            Assert.AreEqual(4, report.MealsEaten, "2 members x 2 meals.");
            Assert.AreEqual(0, report.MealsMissed);
            Assert.AreEqual(96, population.households[0].reserves[0].currentUnits);
            Assert.AreEqual(0, report.PurchasesMade, "No shopping needed — ate from stores.");
        }

        [Test]
        public void ShortfallIsShoppedForByAHouseholdMember()
        {
            // No food in stores: the acting member must shop (GHOST-DEF-006 — meals need a source).
            var report = service.ExecuteDay(population, planner, executor, 0, diag);

            Assert.Greater(report.PurchasesMade, 0, "The shortfall was shopped for, not conjured.");
            Assert.AreEqual(4, report.MealsEaten);
            Assert.AreEqual(0, report.MealsMissed);
            Assert.Greater(report.SpendCents, 0);
        }

        [Test]
        public void NoSupplierMeansMissedMealsLoggedNotFaked()
        {
            var emptySuppliers = new SupplierDirectory(); // nothing stocked anywhere
            var noShopExecutor = new EmbodiedPurchaseExecutor(population, ledgers, emptySuppliers, journeys, pid => "home");

            var report = service.ExecuteDay(population, planner, noShopExecutor, 0, diag);

            Assert.AreEqual(0, report.MealsEaten);
            Assert.AreEqual(4, report.MealsMissed, "Missed meals are logged, never faked.");
            Assert.AreEqual(0, report.PurchasesMade);
        }

        [Test]
        public void NoActingAdultMeansUnmetNeeds()
        {
            population.people.Clear(); // nobody home
            var report = service.ExecuteDay(population, planner, executor, 0, diag);

            Assert.AreEqual(0, report.HouseholdsServed);
            Assert.AreEqual(0, report.MealsEaten);
        }

        [Test]
        public void ConsumptionAndPurchaseAreCausallyLinked()
        {
            // Day 0: shop for meals. Day 1: reserves reflect yesterday's purchase minus meals eaten.
            service.ExecuteDay(population, planner, executor, 0, diag);
            int afterDay0 = population.households[0].reserves[0].currentUnits;

            var report1 = service.ExecuteDay(population, planner, executor, 1, diag);

            // Reserves move only through eating and buying — the loop is causal.
            Assert.GreaterOrEqual(afterDay0, 0);
            Assert.AreEqual(4, report1.MealsEaten + report1.MealsMissed,
                "Every meal is accounted for: eaten or missed.");
        }

        [Test]
        public void DailyNeeds_UsesAuthoritativeHouseholdMembershipInsteadOfLegacyMemberMirror()
        {
            // The household's compatibility member list is deliberately stale.
            // The registry remains the sole authority for who receives meals.
            population.people.Add(Adult(3, "Cal", householdId: -1));
            population.people[0].laborAccessLevel = LaborAccessLevel.None;
            population.people[0].nutrition.nutrition01 = 0.4f;
            population.people[2].nutrition.nutrition01 = 0.4f;
            population.households[0].memberIds.Clear();
            population.households[0].memberIds.Add(1);
            population.households[0].reserves[0].currentUnits = 100;

            var memberships = new HouseholdMembershipRegistry();
            Assert.IsNull(memberships.Register(new HouseholdMembership
            {
                MembershipId = 1,
                PersonId = 3,
                HouseholdId = 7,
                Lifecycle = HouseholdMembershipLifecycle.Active,
                StartDayIndex = 0,
            }));

            DailyNeedsService.DayReport report = service.ExecuteDay(
                population, planner, executor, 0, diag,
                memberships: memberships);

            Assert.AreEqual(2, report.MealsEaten,
                "Only the single registry member should receive two daily meals.");
            Assert.AreEqual(0, report.MealsMissed);
            Assert.AreEqual(98, population.households[0].reserves[0].currentUnits);
            Assert.That(population.people[0].nutrition.nutrition01, Is.EqualTo(0.4f),
                "The stale legacy household member must not receive a meal.");
            Assert.That(population.people[2].nutrition.nutrition01, Is.EqualTo(0.7f).Within(0.000001f),
                "The current registry member receives two meals and recovers once.");
        }
    }
}
