using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Restaurant;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Population;
using EntityId = LandLedgers.Primitives.EntityId;
using LandLedgers.Primitives;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Population
{
    /// <summary>
    /// P2 household &amp; daily needs journey: the live daily driver must (a)
    /// count real prepared meals served by eating-house runtimes (Canon §2.5 —
    /// no double-feeding a diner), (b) plan procurement against REAL reserve
    /// category ids, (c) refuse ledger outflows the household cannot cover,
    /// and (d) gate embodied shopping trips on the acting person's work-time
    /// budget. Before this pass the driver passed no meal sources, planned
    /// phantom categories ("heating_fuel", "general_goods", "medicine"),
    /// allowed negative household balances, and never committed travel time.
    /// </summary>
    [TestFixture]
    public sealed class P2HouseholdNeedsJourneyTests
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
        private HouseholdLedgerRegistry ledgers;
        private SupplierDirectory suppliers;
        private JourneyModel journeys;
        private EmbodiedPurchaseExecutor executor;
        private HouseholdConsumptionPlanner planner;
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
                currentUnits = 100, lowThresholdUnits = 20, targetUnits = 30,
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
        public void PlannerEmitsRealReserveCategoryIds()
        {
            var household = new HouseholdState
            {
                id = 9,
                demandSnapshot = new HouseholdDemandSnapshot
                {
                    foodNeed = 2,
                    generalGoodsNeed = 3,
                    medicineNeed = 4,
                    heatingNeed = 5,
                },
            };

            List<ProcurementNeed> needs = planner.BuildPlan(household, requestingPersonId: 1, dayIndex: 0);

            var ids = new HashSet<string>();
            foreach (ProcurementNeed need in needs) ids.Add(need.CategoryId);
            Assert.IsTrue(ids.Contains("staple_food"));
            Assert.IsTrue(ids.Contains("household_goods"), "Phantom 'general_goods' must map to the real reserve category.");
            Assert.IsTrue(ids.Contains("medicine_remedies"), "Phantom 'medicine' must map to the real reserve category.");
            Assert.IsTrue(ids.Contains("fuel_wood"), "Phantom 'heating_fuel' must map to the real reserve category.");
            Assert.IsFalse(ids.Contains("general_goods"));
            Assert.IsFalse(ids.Contains("medicine"));
            Assert.IsFalse(ids.Contains("heating_fuel"));

            // Every planned category must resolve to a catalogued reserve so the
            // executor can credit what it buys.
            foreach (ProcurementNeed need in needs)
            {
                Assert.IsNotNull(HouseholdReserveCatalog.Get(need.CategoryId),
                    $"Planned category '{need.CategoryId}' has no reserve definition.");
            }
        }

        [Test]
        public void LedgerOutflowBeyondBalanceIsRejected()
        {
            var ledger = new HouseholdLedger(3);
            Assert.IsNull(ledger.RecordInflow(0, 5000, HouseholdIncomeSource.Remittance,
                "kin", "test funds", "aunt"));

            string rejection = ledger.RecordOutflow(1, 5001, "staple food purchase", "general store");

            Assert.IsNotNull(rejection, "Households cannot spend money they do not have.");
            Assert.AreEqual(5000, ledger.GetBalanceCents());
            Assert.AreEqual(1, ledger.Entries.Count, "Rejected outflow books nothing.");
        }

        [Test]
        public void LedgerOutflowAtExactBalanceIsAccepted()
        {
            var ledger = new HouseholdLedger(4);
            Assert.IsNull(ledger.RecordInflow(0, 5000, HouseholdIncomeSource.Remittance,
                "kin", "test funds", "aunt"));

            Assert.IsNull(ledger.RecordOutflow(1, 5000, "staple food purchase", "general store"));
            Assert.AreEqual(0, ledger.GetBalanceCents());
        }

        [Test]
        public void HubMealSourceRegistryComposesAndDedupes()
        {
            var hubObject = new GameObject("Meal Source Hub");
            try
            {
                var hub = hubObject.AddComponent<SimulationSystemsHub>();
                var ledger = new RestaurantMealDayLedger();
                hub.RegisterRestaurantMealSource(ledger);
                hub.RegisterRestaurantMealSource(ledger); // duplicate registration refused
                hub.RegisterRestaurantMealSource(null);

                Assert.AreEqual(1, hub.RestaurantMealSources.Count);
                Assert.AreEqual(0, hub.BoardingHouseMealSources.Count);
                Assert.AreEqual(0, hub.HotelMealSources.Count);

                SimulationSystemsHub.BuildMealSourceComposites(
                    hub.RestaurantMealSources,
                    hub.BoardingHouseMealSources,
                    hub.HotelMealSources,
                    out CompositeRestaurantMealSource restaurants,
                    out var boardingHouses,
                    out var hotels);

                Assert.IsNotNull(restaurants);
                Assert.IsNotNull(boardingHouses);
                Assert.IsNotNull(hotels);

                string refusal = ledger.ReportServedMeal(1, 0, new List<string>());
                Assert.IsNull(refusal);
                Assert.AreEqual(1, restaurants.MealsEatenAtRestaurant(1, 0));
                Assert.AreEqual(0, boardingHouses.MealsEatenAtBoardingHouse(1, 0));
            }
            finally
            {
                Object.DestroyImmediate(hubObject);
            }
        }

        [Test]
        public void RestaurantMealsReduceHouseholdReserveDrawWithoutDoubleFeeding()
        {
            // P1 ate one real meal at the eating house today (Canon §2.5):
            // the household draws only their remaining need from stores.
            var hubObject = new GameObject("Meal Wiring Hub");
            try
            {
                var hub = hubObject.AddComponent<SimulationSystemsHub>();
                var ledger = new RestaurantMealDayLedger();
                hub.RegisterRestaurantMealSource(ledger);
                ledger.ReportServedMeal(1, 0, new List<string>());

                SimulationSystemsHub.BuildMealSourceComposites(
                    hub.RestaurantMealSources,
                    hub.BoardingHouseMealSources,
                    hub.HotelMealSources,
                    out CompositeRestaurantMealSource restaurants,
                    out var boardingHouses,
                    out var hotels);

                DailyNeedsService.DayReport report = service.ExecuteDay(
                    population, planner, executor, 0, diag,
                    budgetStore: null,
                    restaurantMeals: restaurants,
                    boardingMeals: boardingHouses,
                    hotelMeals: hotels);

                Assert.AreEqual(4, report.MealsEaten, "2 members x 2 meals — the diner is never double-fed.");
                Assert.AreEqual(1, report.MealsFromRestaurants);
                Assert.AreEqual(97, population.households[0].reserves[0].currentUnits,
                    "Reserves drew 3 (4 needed minus 1 eaten out), not 4.");
                Assert.AreEqual(0, report.PurchasesMade, "Stores covered the remainder — no shopping.");
            }
            finally
            {
                Object.DestroyImmediate(hubObject);
            }
        }

        [Test]
        public void EmbodiedPurchaseCommitsTravelTimeAgainstPersonBudget()
        {
            // The live driver now maps legacy person ids to HF-1 EntityIds so
            // the shopping trip's round-trip minutes come out of the acting
            // person's TTS-1 budget. A person with no time left cannot shop —
            // the need goes unmet, never faked.
            var budgets = new WorkTimeBudgetStore();
            var timedExecutor = new EmbodiedPurchaseExecutor(
                population, ledgers, suppliers, journeys, pid => "home", budgets,
                pid => EntityId.For(EntityKind.Person, pid));

            // Burn almost the whole day: 600-minute budget, 590 committed.
            Assert.IsTrue(budgets.TryCommitTask(EntityId.For(EntityKind.Person, 1), 590, out _));

            var need = new ProcurementNeed
            {
                HouseholdId = 7,
                CategoryId = "staple_food",
                UnitsNeeded = 4,
                Priority01 = 1f,
                RequestingPersonId = 1,
                DayIndex = 0,
            };

            PurchaseExecutionResult result = timedExecutor.Execute(need, 1, 0);
            Assert.IsFalse(result.Success, "No travel time left — the trip cannot happen.");
            StringAssert.Contains("no time left", result.Notes);
        }
        [Test]
        public void HouseholdInspectionShowsNutritionAndWorkCapacity()
        {
            // The NX-1B teeth are only real if the player can SEE them: the
            // household inspection must surface undernourishment and the
            // resulting work-capacity hit (Canon §25: aggregate, not a minigame).
            var managerObject = new GameObject("Nutrition Inspection");
            try
            {
                var manager = managerObject.AddComponent<PopulationManager>();
                var person = Adult(21, "Hal", 31);
                person.nutrition = new PersonNutritionState { nutrition01 = 0.30f };
                manager.State.people.Add(person);
                var household = new HouseholdState { id = 31 };
                household.memberIds.Add(21);
                manager.State.households.Add(household);

                string summary = manager.BuildHouseholdInspectionSummary(31);

                StringAssert.Contains("Nutrition 1/1 undernourished", summary);
                StringAssert.Contains("work capacity 50%", summary);
            }
            finally
            {
                Object.DestroyImmediate(managerObject);
            }
        }

        [Test]
        public void HouseholdInspectionOmitsNutritionWhenAllNourished()
        {
            var managerObject = new GameObject("Nutrition Inspection Clean");
            try
            {
                var manager = managerObject.AddComponent<PopulationManager>();
                manager.State.people.Add(Adult(22, "Ivy", 32));
                var household = new HouseholdState { id = 32 };
                household.memberIds.Add(22);
                manager.State.households.Add(household);

                string summary = manager.BuildHouseholdInspectionSummary(32);

                StringAssert.DoesNotContain("Nutrition", summary);
            }
            finally
            {
                Object.DestroyImmediate(managerObject);
            }
        }
    }
}
