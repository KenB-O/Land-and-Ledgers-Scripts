using System.Collections.Generic;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.EditorTests.World
{
    /// <summary>
    /// JRN-1: the journey model. Real miles on a 1:1 scale (Canon §13.1),
    /// mode-derived travel minutes, shortest-path routing, and honest
    /// unreachable diagnostics.
    /// </summary>
    [TestFixture]
    public sealed class JourneyModelTests
    {
        private JourneyModel TownWithFarm()
        {
            var model = new JourneyModel();
            var diagnostics = new List<string>();
            var locations = new List<JourneyLocation>
            {
                new JourneyLocation("town-center", JourneyLocationKind.TownBuilding, "Town Center", 0f, 0f),
                new JourneyLocation("town-store", JourneyLocationKind.Store, "General Store", 0.2f, 0.1f),
                WorldLayoutBuilder.FarmsteadNode("farm-1", "Home Farm", 3f),
                new JourneyLocation("mill-1", JourneyLocationKind.Mill, "Granger Mill", 1f, 4f),
            };
            var edges = new List<JourneyEdge>
            {
                new JourneyEdge("town-center", "town-store", 0.3f, "main street"),
                new JourneyEdge("town-center", "farmstead-farm-1", 3f, "river road"),
                new JourneyEdge("farmstead-farm-1", "mill-1", 2f, "mill road"),
            };
            var built = WorldLayoutBuilder.Build(locations, edges, 42, diagnostics);
            Assert.IsTrue(diagnostics.Count == 0, "Layout build problems: " + string.Join(" | ", diagnostics));
            return built;
        }

        [Test]
        public void FindRoute_ReturnsShortestPathInMinutes()
        {
            JourneyModel model = TownWithFarm();
            JourneyRoute route = model.FindRoute("farmstead-farm-1", "town-store", TravelMode.Wagon);

            Assert.IsTrue(route.Found);
            // farmstead → town-center (3 mi) → town-store (0.3 mi) = 3.3 mi at 4 mph ≈ 50 min.
            Assert.AreEqual(3.3f, route.TotalMiles, 0.01f);
            Assert.AreEqual(JourneyModel.MinutesForMiles(3.3f, TravelMode.Wagon), route.TotalMinutes);
            Assert.Greater(route.TotalMinutes, 0);
        }

        [Test]
        public void FindRoute_UnreachableIsHonest()
        {
            var model = new JourneyModel();
            Assert.IsNull(model.RegisterLocation(new JourneyLocation("a", JourneyLocationKind.Farmstead, "A", 0f, 0f)));
            Assert.IsNull(model.RegisterLocation(new JourneyLocation("b", JourneyLocationKind.Mill, "B", 5f, 0f)));

            JourneyRoute route = model.FindRoute("a", "b", TravelMode.Wagon);
            Assert.IsFalse(route.Found, "Unreachable destinations must not assume free travel.");
            Assert.IsNotEmpty(route.Diagnostic);
        }

        [Test]
        public void TravelModes_DifferBySpeed()
        {
            int foot = JourneyModel.MinutesForMiles(6f, TravelMode.Foot);
            int horse = JourneyModel.MinutesForMiles(6f, TravelMode.Horseback);
            int wagon = JourneyModel.MinutesForMiles(6f, TravelMode.Wagon);
            Assert.Greater(foot, wagon, "A wagon beats walking.");
            Assert.Greater(wagon, horse, "Horseback beats a loaded wagon.");
            Assert.GreaterOrEqual(foot, 1, "TTS-1 minute quantum: floor of 1.");
        }

        [Test]
        public void DuplicateLocationIds_Refused()
        {
            var model = new JourneyModel();
            Assert.IsNull(model.RegisterLocation(new JourneyLocation("x", JourneyLocationKind.Store, "X", 0f, 0f)));
            Assert.IsNotNull(model.RegisterLocation(new JourneyLocation("x", JourneyLocationKind.Store, "X2", 1f, 0f)),
                "Location ids are never reused.");
        }

        [Test]
        public void SaveRoundTrip_PreservesWorld()
        {
            JourneyModel model = TownWithFarm();
            JourneyModelSaveDto dto = model.CaptureSaveDto();

            var loaded = new JourneyModel();
            loaded.LoadFromSaveDto(dto);

            JourneyRoute route = loaded.FindRoute("farmstead-farm-1", "mill-1", TravelMode.Wagon);
            Assert.IsTrue(route.Found);
            Assert.AreEqual(2f, route.TotalMiles, 0.01f);
        }
    }
}
