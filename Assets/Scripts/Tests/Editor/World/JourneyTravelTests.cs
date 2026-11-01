using System.Collections.Generic;
using LandLedgers.Economy.Farming.Dairy;
using LandLedgers.Economy.Farming.Delivery;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.EditorTests.World
{
    /// <summary>
    /// JRN-2: travel integration. Deliveries plan on journey-model miles,
    /// perishable aging uses real travel time, and work travel becomes a real
    /// task against the worker's time budget.
    /// </summary>
    [TestFixture]
    public sealed class JourneyTravelTests
    {
        private JourneyModel ModelWithStore()
        {
            var model = new JourneyModel();
            var diagnostics = new List<string>();
            var locations = new List<JourneyLocation>
            {
                new JourneyLocation("town-center", JourneyLocationKind.TownBuilding, "Town Center", 0f, 0f),
                new JourneyLocation("town-store", JourneyLocationKind.Store, "General Store", 0.2f, 0.1f)
                {
                    BusinessId = "store-1",
                },
                WorldLayoutBuilder.FarmsteadNode("farm-1", "Home Farm", 3f),
            };
            var edges = new List<JourneyEdge>
            {
                new JourneyEdge("town-center", "town-store", 0.3f, "main street"),
                new JourneyEdge("town-center", "farmstead-farm-1", 3f, "river road"),
            };
            JourneyModel built = WorldLayoutBuilder.Build(locations, edges, 7, diagnostics);
            Assert.IsTrue(diagnostics.Count == 0, string.Join(" | ", diagnostics));
            return built;
        }

        private List<ITransitLot> MilkLots(EntityIdRegistry ids)
        {
            return new List<ITransitLot>
            {
                new MilkLot
                {
                    LotId = ids.Allocate(EntityKind.Lot),
                    QuantityUnits = 20,
                    ProducedDayIndex = 100,
                    FarmId = "farm-1",
                },
            };
        }

        [Test]
        public void PlanDeliveryWithJourney_UsesModelMiles()
        {
            JourneyModel model = ModelWithStore();
            var ids = new EntityIdRegistry();
            var diagnostics = new List<string>();

            // Legacy call with a wrong abstract distance (50 miles).
            DeliveryJob legacy = DeliveryService.PlanDelivery(
                "sale-1", "milk", MilkLots(ids), "farm-1", "farm-biz-1",
                "store-1", "General Store", DeliveryHaulerOption.FarmOwnTeam,
                50f, 100, "", "", diagnostics);
            Assert.IsNotNull(legacy);
            Assert.AreEqual(50f, legacy.DistanceMilesOneWay, 0.01f);

            // Journey-aware call: model routes farmstead → store = 3.3 miles.
            diagnostics.Clear();
            DeliveryJob routed = DeliveryService.PlanDeliveryWithJourney(
                "sale-2", "milk", MilkLots(ids), "farm-1", "farm-biz-1",
                "store-1", "General Store", DeliveryHaulerOption.FarmOwnTeam,
                50f, 100, "", "", model, diagnostics);
            Assert.IsNotNull(routed);
            Assert.AreEqual(3.3f, routed.DistanceMilesOneWay, 0.01f,
                "Journey-model miles replace the abstract leg.");
            Assert.IsNotNull(routed.InternalCost, "Internal haul cost derives from real miles.");
        }

        [Test]
        public void PlanDeliveryWithJourney_FallsBackHonestly()
        {
            var model = new JourneyModel(); // empty: nothing routable
            var ids = new EntityIdRegistry();
            var diagnostics = new List<string>();

            DeliveryJob job = DeliveryService.PlanDeliveryWithJourney(
                "sale-3", "milk", MilkLots(ids), "farm-9", "farm-biz-9",
                "store-9", "Nowhere Store", DeliveryHaulerOption.FarmOwnTeam,
                12f, 100, "", "", model, diagnostics);
            Assert.IsNotNull(job, "Unroutable delivery still plans — on the legacy distance.");
            Assert.AreEqual(12f, job.DistanceMilesOneWay, 0.01f);
            StringAssert.Contains("legacy distance", string.Join(" ", diagnostics).ToLowerInvariant(),
                "The fallback must be disclosed, not silent.");
        }

        [Test]
        public void TransitDays_UsesRealTravelTime()
        {
            // 50-minute haul: same-day, ~0 days of aging. Honest, not inflated.
            Assert.AreEqual(0, JourneyTravel.TransitDaysForMinutes(50));
            // Multi-day wagon haul: ages honestly.
            Assert.AreEqual(2, JourneyTravel.TransitDaysForMinutes(3000));
        }

        [Test]
        public void PlanWorkTravel_CreatesRealTask()
        {
            JourneyModel model = ModelWithStore();
            var tasks = new TaskAuthority();
            JourneyTravel.RegisterTaskDefinitions(tasks);
            var diagnostics = new List<string>();
            EntityId worker = EntityId.For(EntityKind.Person, 11);

            WorkTask task = JourneyTravel.PlanWorkTravel(
                tasks, model, worker,
                "farmstead-farm-1", "town-store", TravelMode.Foot, 100, diagnostics);

            Assert.IsNotNull(task, "Routable work travel becomes a task: " + string.Join(" | ", diagnostics));
            Assert.AreEqual(JourneyTravel.TravelTaskId, task.DefinitionId);
            Assert.Greater(task.PlannedMinutes, 0, "Travel consumes real budgeted minutes.");

            // Unroutable travel: no task, honest diagnostic.
            diagnostics.Clear();
            WorkTask none = JourneyTravel.PlanWorkTravel(
                tasks, model, worker, "farmstead-farm-1", "mill-1", TravelMode.Foot, 100, diagnostics);
            Assert.IsNull(none, "Unroutable travel must not invent a task.");
        }
    }
}
