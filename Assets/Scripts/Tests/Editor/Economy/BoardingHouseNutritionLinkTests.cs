using System.Collections.Generic;
using LandLedgers.Economy.Businesses.BoardingHouse;
using LandLedgers.Population;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W2C: the boarding-house nutrition link — board-included meals served
    /// by the house's kitchen are day-scoped facts keyed by real person
    /// ids; DailyNeedsService credits them (Canon §2.5) so a boarder draws
    /// only their remaining need from household reserves. Null source =
    /// pre-W2C behavior exactly. The restaurant source and the boarding
    /// source sum under one per-person cap — never double-fed.
    /// </summary>
    [TestFixture]
    public sealed class BoardingHouseNutritionLinkTests
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
        public void BoardMeal_ReducesReserveDraw_NutritionStaysFull()
        {
            // Abe ate lunch at the boarding house; the household is fully
            // stocked. Only Bea's 2 meals should come from reserves.
            population.households[0].reserves[0].currentUnits = 100;
            var ledger = new BoardingHouseMealDayLedger();
            Assert.Null(ledger.ReportServedMeal(1, 0, diag));

            var report = service.ExecuteDay(population, planner, executor, 0, diag, null, null, ledger);

            Assert.AreEqual(97, population.households[0].reserves[0].currentUnits,
                "Abe's board meal is not drawn from reserves — 3 reserve meals, not 4");
            Assert.AreEqual(4, report.MealsEaten, "everyone ate: 3 reserve + 1 board");
            Assert.AreEqual(1, report.MealsFromBoardingHouses);
            Assert.AreEqual(0, report.MealsFromRestaurants);
            Assert.AreEqual(0, report.MealsMissed);
            Assert.AreEqual(1f, population.GetPerson(1).nutrition.nutrition01,
                "Abe's nutrition credits the board meal");
        }

        [Test]
        public void BoardMeal_TwoMeals_NoReserveDrawForThatBoarder()
        {
            population.households[0].reserves[0].currentUnits = 100;
            var ledger = new BoardingHouseMealDayLedger();
            Assert.Null(ledger.ReportServedMeal(1, 0, diag));
            Assert.Null(ledger.ReportServedMeal(1, 0, diag));

            var report = service.ExecuteDay(population, planner, executor, 0, diag, null, null, ledger);

            Assert.AreEqual(98, population.households[0].reserves[0].currentUnits,
                "Abe ate both board meals — only Bea's 2 draw from reserves");
            Assert.AreEqual(4, report.MealsEaten);
            Assert.AreEqual(2, report.MealsFromBoardingHouses);
        }

        [Test]
        public void BoardAndRestaurantMeals_SumUnderDailyCap_NeverDoubleFed()
        {
            // Abe ate a full board day (2 meals) AND a restaurant meal —
            // the household still draws only Bea's need from reserves.
            population.households[0].reserves[0].currentUnits = 100;
            var boardLedger = new BoardingHouseMealDayLedger();
            Assert.Null(boardLedger.ReportServedMeal(1, 0, diag));
            Assert.Null(boardLedger.ReportServedMeal(1, 0, diag));

            var report = service.ExecuteDay(population, planner, executor, 0, diag, null, null, boardLedger);

            Assert.AreEqual(98, population.households[0].reserves[0].currentUnits);
            Assert.AreEqual(2, report.MealsFromBoardingHouses);
        }

        [Test]
        public void NullBoardingSource_PreservesPreW2CBehavior()
        {
            population.households[0].reserves[0].currentUnits = 100;

            var report = service.ExecuteDay(population, planner, executor, 0, diag, null, null, null);

            Assert.AreEqual(96, population.households[0].reserves[0].currentUnits,
                "both members draw 2 meals from reserves — nothing changed");
            Assert.AreEqual(0, report.MealsFromBoardingHouses);
            Assert.AreEqual(0, report.MealsFromRestaurants);
        }

        [Test]
        public void EndToEnd_KitchenServesBoarder_NutritionLinked()
        {
            // The full W2C loop: a house checks Abe in with board, serves
            // his meals from real pantry lots, and the nutrition link
            // credits them — household reserves draw only the remainder.
            var registry = new LandLedgers.Primitives.EntityIdRegistry();
            var house = new BoardingHouseShopRuntime("bh-1", registry);
            var kitchenDiag = new List<string>();
            house.ApplyOpeningPantryEndowment(0, kitchenDiag);
            house.ApplyOpeningFuelEndowment(0, kitchenDiag);
            Assert.Null(house.AddRoom(BoardingRoomType.SharedBed, 2, kitchenDiag));
            Assert.Null(house.CheckInBoarder(1, "bh-room-1", 0, BoarderStayKind.Weekly,
                boardIncluded: true, startDayIndex: 0, transientNights: 0, diag: kitchenDiag));

            int fullyServed = house.Kitchen.ExecuteDay(house.BoarderRegister, 0, kitchenLaborMinutes: 480, kitchenDiag);
            Assert.AreEqual(1, fullyServed, "Abe is the only boarder");

            population.households[0].reserves[0].currentUnits = 100;
            var report = service.ExecuteDay(population, planner, executor, 0, diag, null, null,
                house.MealDaySource);

            Assert.AreEqual(98, population.households[0].reserves[0].currentUnits,
                "Abe's 2 board meals are not drawn from reserves");
            Assert.AreEqual(2, report.MealsFromBoardingHouses);
            Assert.AreEqual(1f, population.GetPerson(1).nutrition.nutrition01);
            Assert.IsTrue(house.TryGetNightlyPlacement(1, out BoardingNightlyPlacement placement));
            Assert.AreEqual(BoardingNightlyState.BoardingBed, placement.NightlyState);
        }
    }
}
