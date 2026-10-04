using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Hotel;
using LandLedgers.Population;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    /// <summary>
    /// W3B: the hotel-meals nutrition link — a guest who ate in the hotel's
    /// dining room genuinely ate (Canon §2.5): their household draws (and
    /// buys) only their remaining need. Hotel meals sum with restaurant and
    /// boarding-house meals under one per-person daily cap — never
    /// double-fed. Null source = pre-W3B behavior exactly.
    /// </summary>
    public sealed class DailyNeedsHotelMealsTests
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
        private HouseholdConsumptionPlanner planner;
        private EmbodiedPurchaseExecutor executor;
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
                currentUnits = 100, lowThresholdUnits = 20, targetUnits = 30,
            });
            population.households.Add(household);

            var ledgers = new HouseholdLedgerRegistry();
            ledgers.GetOrCreate(7).RecordInflow(0, 100000, HouseholdIncomeSource.OwnerContribution,
                "test-capital", "test funding", "test");

            var suppliers = new SupplierDirectory();
            var store = new TestSupplier();
            store.Stock["staple_food"] = 1000;
            suppliers.Register(store);

            var journeys = new JourneyModel();
            journeys.RegisterLocation(new JourneyLocation("home", JourneyLocationKind.Farmstead, "Home", 0f, 0f));
            journeys.RegisterLocation(new JourneyLocation("store", JourneyLocationKind.Store, "Store", 2f, 0f));
            journeys.AddEdge("home", "store", 2.0f, "road");

            executor = new EmbodiedPurchaseExecutor(population, ledgers, suppliers, journeys, pid => "home");
            planner = new HouseholdConsumptionPlanner();
            service = new DailyNeedsService();
            diag = new List<string>();
        }

        [Test]
        public void HotelGuest_Draws_Only_RemainingNeed_FromReserves()
        {
            var ledger = new HotelMealDayLedger();
            ledger.ReportServedMeal(1, 0, diag);
            ledger.ReportServedMeal(1, 0, diag);

            var report = service.ExecuteDay(population, planner, executor, 0, diag,
                hotelMeals: ledger);

            Assert.AreEqual(4, report.MealsEaten, "2 members x 2 meals");
            Assert.AreEqual(2, report.MealsFromHotels);
            Assert.AreEqual(0, report.MealsMissed);
            Assert.AreEqual(98, population.households[0].reserves[0].currentUnits,
                "the hotel guest drew no reserve meals; the other member drew 2");
            Assert.AreEqual(0, report.PurchasesMade, "no shortfall — nothing shopped");
        }

        [Test]
        public void HotelMeals_Sum_Under_OneCap_With_OtherSources()
        {
            var hotelLedger = new HotelMealDayLedger();
            hotelLedger.ReportServedMeal(1, 0, diag);
            hotelLedger.ReportServedMeal(1, 0, diag);
            var restaurantLedger = new RestaurantMealDayLedger();
            restaurantLedger.ReportServedMeal(1, 0, diag);

            var report = service.ExecuteDay(population, planner, executor, 0, diag,
                restaurantMeals: restaurantLedger, hotelMeals: hotelLedger);

            Assert.AreEqual(4, report.MealsEaten, "never double-fed: the cap holds at 2 per person");
            Assert.AreEqual(2, report.MealsFromHotels);
            Assert.AreEqual(1, report.MealsFromRestaurants);
            Assert.AreEqual(98, population.households[0].reserves[0].currentUnits,
                "member 1 drew 0 reserve meals; member 2 drew 2");
        }

        [Test]
        public void NullHotelSource_Is_PreW3BBehavior()
        {
            var report = service.ExecuteDay(population, planner, executor, 0, diag);

            Assert.AreEqual(4, report.MealsEaten);
            Assert.AreEqual(0, report.MealsFromHotels);
            Assert.AreEqual(96, population.households[0].reserves[0].currentUnits,
                "both members ate fully from reserves");
        }

        [Test]
        public void HotelGuest_Shortfall_Still_Shopped_For_RemainingNeed()
        {
            // Empty reserves: the hotel guest's remaining need is 0, the
            // other member's shortfall is shopped — never conjured.
            population.households[0].reserves[0].currentUnits = 0;
            var ledger = new HotelMealDayLedger();
            ledger.ReportServedMeal(1, 0, diag);
            ledger.ReportServedMeal(1, 0, diag);

            var report = service.ExecuteDay(population, planner, executor, 0, diag,
                hotelMeals: ledger);

            Assert.AreEqual(4, report.MealsEaten);
            Assert.AreEqual(2, report.MealsFromHotels);
            Assert.AreEqual(0, report.MealsMissed);
            Assert.Greater(report.PurchasesMade, 0, "the non-guest's shortfall was shopped for");
        }
    }
}
