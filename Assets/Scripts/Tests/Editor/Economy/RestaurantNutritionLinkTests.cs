using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Restaurant;
using LandLedgers.Population;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W2B: the nutrition link — served meals are day-scoped facts keyed by
    /// real person ids; DailyNeedsService credits them (Canon §2.5) so a
    /// restaurant diner draws only their remaining need from household
    /// reserves. Null source = pre-W2B behavior exactly.
    /// </summary>
    [TestFixture]
    public sealed class RestaurantNutritionLinkTests
    {
        #region Ledger unit tests

        [Test]
        public void Ledger_RecordsAndQueriesPerPersonPerDay()
        {
            var ledger = new RestaurantMealDayLedger();
            var diag = new List<string>();

            Assert.Null(ledger.ReportServedMeal(5, 100, diag));
            Assert.Null(ledger.ReportServedMeal(5, 100, diag));
            Assert.Null(ledger.ReportServedMeal(6, 100, diag));

            Assert.AreEqual(2, ledger.MealsEatenAtRestaurant(5, 100));
            Assert.AreEqual(1, ledger.MealsEatenAtRestaurant(6, 100));
            Assert.AreEqual(0, ledger.MealsEatenAtRestaurant(5, 101), "day-scoped — tomorrow sees nothing");
            Assert.AreEqual(0, ledger.MealsEatenAtRestaurant(7, 100), "other persons see nothing");
            Assert.AreEqual(3, ledger.ServedMealsOnDay(100));
        }

        [Test]
        public void Ledger_RefusesAnonymousDiners_Loudly()
        {
            var ledger = new RestaurantMealDayLedger();
            var diag = new List<string>();

            Assert.NotNull(ledger.ReportServedMeal(0, 100, diag), "anonymous diners are not recorded");
            Assert.NotNull(ledger.ReportServedMeal(-3, 100, diag));
            Assert.AreEqual(0, ledger.ServedMealsOnDay(100));
        }

        [Test]
        public void Ledger_PruneBefore_DropsOldDays()
        {
            var ledger = new RestaurantMealDayLedger();
            var diag = new List<string>();
            ledger.ReportServedMeal(5, 100, diag);
            ledger.ReportServedMeal(5, 101, diag);

            ledger.PruneBefore(101, diag);

            Assert.AreEqual(0, ledger.MealsEatenAtRestaurant(5, 100));
            Assert.AreEqual(1, ledger.MealsEatenAtRestaurant(5, 101));
        }

        [Test]
        public void Ledger_SaveLoad_RoundTrips()
        {
            var ledger = new RestaurantMealDayLedger();
            var diag = new List<string>();
            ledger.ReportServedMeal(5, 100, diag);
            ledger.ReportServedMeal(5, 100, diag);

            var restored = new RestaurantMealDayLedger();
            restored.LoadFromSaveDto(ledger.CaptureSaveDto());

            Assert.AreEqual(2, restored.MealsEatenAtRestaurant(5, 100));
        }

        [Test]
        public void CompositeSource_SumsAcrossEatingHouses()
        {
            var a = new RestaurantMealDayLedger();
            var b = new RestaurantMealDayLedger();
            var diag = new List<string>();
            a.ReportServedMeal(5, 100, diag);
            b.ReportServedMeal(5, 100, diag);
            b.ReportServedMeal(5, 100, diag);

            var composite = new CompositeRestaurantMealSource(new[] { a, b });

            Assert.AreEqual(3, composite.MealsEatenAtRestaurant(5, 100));
            Assert.AreEqual(0, composite.MealsEatenAtRestaurant(5, 101));
        }

        #endregion

        #region DailyNeedsService integration

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

        private static PersonState Adult(int id, string name, int householdId)
        {
            return new PersonState
            {
                id = id, firstName = name, lastName = "Test", age = 30,
                ageBand = AgeBand.Adult18Plus, laborAccessLevel = LaborAccessLevel.FullLaborMarket,
                householdId = householdId,
            };
        }

        private PopulationState population;
        private HouseholdConsumptionPlanner planner;
        private EmbodiedPurchaseExecutor executor;
        private DailyNeedsService service;
        private HouseholdLedgerRegistry ledgers;
        private JourneyModel journeys;
        private List<string> diag;

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

            var ledgers = new HouseholdLedgerRegistry();
            ledgers.GetOrCreate(7).RecordInflow(0, 100000, HouseholdIncomeSource.OwnerContribution,
                "test-capital", "test funding", "test");
            this.ledgers = ledgers;

            var suppliers = new SupplierDirectory();
            var store = new TestSupplier();
            store.Stock["staple_food"] = 1000;
            suppliers.Register(store);

            var journeys = new JourneyModel();
            journeys.RegisterLocation(new JourneyLocation("home", JourneyLocationKind.Farmstead, "Home", 0f, 0f));
            journeys.RegisterLocation(new JourneyLocation("store", JourneyLocationKind.Store, "Store", 2f, 0f));
            journeys.AddEdge("home", "store", 2.0f, "road");
            this.journeys = journeys;

            executor = new EmbodiedPurchaseExecutor(population, ledgers, suppliers, journeys, pid => "home");
            planner = new HouseholdConsumptionPlanner();
            service = new DailyNeedsService();
            diag = new List<string>();
        }

        [Test]
        public void RestaurantMeal_ReducesReserveDraw_NutritionStaysFull()
        {
            // Abe ate lunch at the eating house; the household is fully
            // stocked. Only Bea's 2 meals should come from reserves.
            population.households[0].reserves[0].currentUnits = 100;
            var ledger = new RestaurantMealDayLedger();
            Assert.Null(ledger.ReportServedMeal(1, 0, diag));

            var report = service.ExecuteDay(population, planner, executor, 0, diag, null, ledger);

            Assert.AreEqual(97, population.households[0].reserves[0].currentUnits,
                "Abe's restaurant meal is not drawn from reserves — 3 reserve meals, not 4");
            Assert.AreEqual(4, report.MealsEaten, "everyone ate: 3 reserve + 1 restaurant");
            Assert.AreEqual(1, report.MealsFromRestaurants);
            Assert.AreEqual(0, report.MealsMissed);
            Assert.AreEqual(0, report.PurchasesMade, "no shopping — reserves covered the remainder");
            Assert.AreEqual(1f, population.GetPerson(1).nutrition.nutrition01,
                "Abe's nutrition credits the restaurant meal");
            Assert.AreEqual(1f, population.GetPerson(2).nutrition.nutrition01,
                "Bea's nutrition credits her reserve meals");
        }

        [Test]
        public void RestaurantMeal_TwoMeals_NoReserveDrawForThatDiner()
        {
            population.households[0].reserves[0].currentUnits = 100;
            var ledger = new RestaurantMealDayLedger();
            Assert.Null(ledger.ReportServedMeal(1, 0, diag));
            Assert.Null(ledger.ReportServedMeal(1, 0, diag));

            var report = service.ExecuteDay(population, planner, executor, 0, diag, null, ledger);

            Assert.AreEqual(98, population.households[0].reserves[0].currentUnits,
                "Abe ate both meals out — only Bea's 2 draw from reserves");
            Assert.AreEqual(4, report.MealsEaten);
            Assert.AreEqual(2, report.MealsFromRestaurants);
        }

        [Test]
        public void RestaurantMeal_ReducesProcurementWhenReservesEmpty()
        {
            // Empty reserves: Abe's restaurant meal is not shopped for.
            var ledger = new RestaurantMealDayLedger();
            Assert.Null(ledger.ReportServedMeal(1, 0, diag));

            var report = service.ExecuteDay(population, planner, executor, 0, diag, null, ledger);

            Assert.AreEqual(4, report.MealsEaten, "3 shopped + 1 restaurant — everyone eats");
            Assert.AreEqual(1, report.MealsFromRestaurants);
            Assert.AreEqual(0, report.MealsMissed);
            Assert.Greater(report.PurchasesMade, 0, "the shortfall is still shopped for — just smaller");
        }

        [Test]
        public void RestaurantMeal_CountsAgainstMissedMeals()
        {
            // No supplier at all: Abe's restaurant meal still counts as eaten.
            var emptySuppliers = new SupplierDirectory();
            var noShopExecutor = new EmbodiedPurchaseExecutor(
                population, ledgers, emptySuppliers, journeys, pid => "home");
            var ledger = new RestaurantMealDayLedger();
            Assert.Null(ledger.ReportServedMeal(1, 0, diag));

            var report = service.ExecuteDay(population, planner, noShopExecutor, 0, diag, null, ledger);

            Assert.AreEqual(1, report.MealsEaten, "the restaurant meal is real food");
            Assert.AreEqual(3, report.MealsMissed, "3 missed — not 4; the restaurant meal is not missed");
        }

        [Test]
        public void NullSource_PreservesPreW2BBehaviorExactly()
        {
            population.households[0].reserves[0].currentUnits = 100;

            var report = service.ExecuteDay(population, planner, executor, 0, diag);

            Assert.AreEqual(4, report.MealsEaten, "2 members x 2 meals");
            Assert.AreEqual(0, report.MealsMissed);
            Assert.AreEqual(0, report.MealsFromRestaurants);
            Assert.AreEqual(96, population.households[0].reserves[0].currentUnits);
        }

        #endregion
    }
}
