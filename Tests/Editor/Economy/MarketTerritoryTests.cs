using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Market;
using LandLedgers.Population;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// T2E: market territory without the generator. Catchment is derived from
    /// real routes (never a fixed radius), the three population views are read
    /// models over actual persons, drivers score opportunities (never counts),
    /// and labor demand is worker-time by task and season.
    /// </summary>
    public sealed class MarketTerritoryTests
    {
        private PopulationState population;
        private EmploymentRelationshipRegistry employment;

        private static PersonState Person(int id, int householdId, string professionId = null)
        {
            return new PersonState
            {
                id = id, firstName = "P" + id, lastName = "Test", age = 30,
                ageBand = AgeBand.Adult18Plus, laborAccessLevel = LaborAccessLevel.FullLaborMarket,
                householdId = householdId, professionId = professionId,
            };
        }

        private static JourneyModel ThreeCenterJourney()
        {
            var journeys = new JourneyModel();
            journeys.RegisterLocation(new JourneyLocation("town", JourneyLocationKind.TownBuilding, "Town", 0f, 0f));
            journeys.RegisterLocation(new JourneyLocation("rival", JourneyLocationKind.TownBuilding, "Rival Town", 20f, 0f));
            journeys.RegisterLocation(new JourneyLocation("farm-near", JourneyLocationKind.Farmstead, "Near Farm", 5f, 0f));
            journeys.RegisterLocation(new JourneyLocation("farm-far", JourneyLocationKind.Farmstead, "Far Farm", 60f, 0f));
            journeys.RegisterLocation(new JourneyLocation("farm-contested", JourneyLocationKind.Farmstead, "Contested Farm", 8f, 0f));
            journeys.AddEdge("town", "farm-near", 5.0f, "road");
            journeys.AddEdge("town", "farm-far", 60.0f, "long road");
            journeys.AddEdge("town", "farm-contested", 8.0f, "road");
            journeys.AddEdge("rival", "farm-contested", 3.0f, "short road");
            return journeys;
        }

        [SetUp]
        public void SetUp()
        {
            population = new PopulationState();
            population.people.Add(Person(1, 1, "merchant")); // town household 1
            population.people.Add(Person(2, 1));
            population.people.Add(Person(3, 2, "farmer"));   // rural household 2
            population.people.Add(Person(4, 2));
            population.people.Add(Person(5, 2));
            employment = new EmploymentRelationshipRegistry();
        }

        [Test]
        public void PopulationViewsAreReadModelsOverActualPersons()
        {
            var views = new PopulationViews(population, employment);

            Assert.AreEqual(2, views.BuiltUpPopulation(new[] { 1 }));
            Assert.AreEqual(3, views.RuralServicePopulation(new[] { "2" }));
            Assert.AreEqual(2 + 3 + 10, views.EffectiveMarketPopulation(2, 3, 10),
                "Effective market = built-up + rural-service + transient.");

            var participation = views.MeasureParticipation(new[] { 1, 2 });
            Assert.AreEqual(5, participation.Residents);
            Assert.AreEqual(5, participation.EconomicallyActive);
            Assert.AreEqual(2, participation.WithPrimaryOccupation, "Only two have a profession recorded.");
            Assert.AreEqual(0, participation.WageEmployees, "Nobody is employed — separate measure, not inferred.");
        }

        [Test]
        public void CatchmentIsDerivedFromTravelTimeNotRadius()
        {
            var journeys = ThreeCenterJourney();
            var query = new CatchmentQuery();
            var units = new List<RuralUnit>
            {
                new RuralUnit { UnitId = "near", LocationId = "farm-near", HouseholdId = "2", IsFarm = true, ResidentCount = 3 },
                new RuralUnit { UnitId = "far", LocationId = "farm-far", HouseholdId = "3", IsFarm = true, ResidentCount = 4 },
                new RuralUnit { UnitId = "contested", LocationId = "farm-contested", HouseholdId = "4", IsFarm = true, ResidentCount = 2 },
            };

            var result = query.Compute("town", units, new List<string> { "rival" }, journeys, new List<string>());

            var ids = new List<string>();
            foreach (var u in result.UnitsInCatchment) ids.Add(u.UnitId);
            CollectionAssert.Contains(ids, "near", "5 mi by wagon (75 min) is a day's trip.");
            CollectionAssert.DoesNotContain(ids, "far", "60 mi by wagon (900 min) exceeds a day's trip.");
            CollectionAssert.DoesNotContain(ids, "contested", "Strictly closer to the rival center — competitive territory.");
        }

        [Test]
        public void DriversScoreOpportunitiesNeverCounts()
        {
            var census = new TerritoryCensus
            {
                EffectiveMarketPopulation = 900,
                TownHouseholdCount = 120,
                CatchmentFarmCount = 15,
                AvgCatchmentMiles = 6f,
                WagonsInTerritory = 20,
                DraftTeamsInTerritory = 12,
                ImplementsInTerritory = 25,
                LivestockFarmsInCatchment = 8,
                TransientVisitorsPerDay = 15,
                Role = SettlementRole.AgriculturalServiceTown,
            };

            var opportunities = new DemandDriverSet().EvaluateAll(census);

            Assert.AreEqual(6, opportunities.Count);
            foreach (BusinessOpportunity o in opportunities)
            {
                Assert.GreaterOrEqual(o.Score01, 0f);
                Assert.LessOrEqual(o.Score01, 1f);
                Assert.Greater(o.Reasons.Count, 0, "Every score cites its real-world reasons.");
            }
            // The blacksmith has real repair demand behind it here.
            BusinessOpportunity smith = opportunities.Find(o => o.BusinessType == BusinessType.Blacksmith);
            Assert.Greater(smith.Score01, 0.5f);
        }

        [Test]
        public void EmptyTerritoryScoresNothing()
        {
            var opportunities = new DemandDriverSet().EvaluateAll(new TerritoryCensus());
            foreach (BusinessOpportunity o in opportunities)
                Assert.AreEqual(0f, o.Score01, "No real demand → no opportunity. Nothing is invented.");
        }

        [Test]
        public void LaborDemandIsWorkerTimeByTaskAndSeason()
        {
            var plan = new LaborDemandPlan { BusinessInstanceId = "biz-farm", WorkdayMinutes = 600 };
            plan.Workloads.Add(new TaskWorkload { TaskId = "milk", WorkerMinutesPerDay = 120, SimultaneousWorkers = 1, Season = "year-round" });
            plan.Workloads.Add(new TaskWorkload { TaskId = "harvest", WorkerMinutesPerDay = 1200, SimultaneousWorkers = 6, Season = "autumn" });

            Assert.AreEqual(1320, plan.TotalWorkerMinutesPerDay());
            Assert.AreEqual(1320 / 600f, plan.FullTimeEquivalents(), 0.001f);
            Assert.AreEqual(6, plan.PeakSimultaneousHeadcount(), "Peak is the largest single crew — distinct from FTE.");
            Assert.AreEqual(120, plan.TotalWorkerMinutesPerDay("spring"), "Harvest is autumn-only.");
        }

        [Test]
        public void GeneratorSeamIsExplicitlyHeld()
        {
            Assert.IsNotEmpty(GeneratorSeam.HeldReason);
            StringAssert.Contains("HELD", GeneratorSeam.HeldReason);
        }
    }
}
