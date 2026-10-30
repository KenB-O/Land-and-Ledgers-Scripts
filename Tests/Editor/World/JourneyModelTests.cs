using System.Collections.Generic;
using LandLedgers.World;
using LandLedgers.World.Journeys;
using UnityEngine;
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
        public void FindRoute_EqualCostAlternativesAreStableAcrossRegistrationOrder()
        {
            JourneyModel first = BuildEqualCostRouteModel(false);
            JourneyModel reversed = BuildEqualCostRouteModel(true);

            JourneyRoute firstRoute = first.FindRoute("start", "destination", TravelMode.Foot);
            JourneyRoute reversedRoute = reversed.FindRoute("start", "destination", TravelMode.Foot);

            Assert.IsTrue(firstRoute.Found, firstRoute.Diagnostic);
            Assert.IsTrue(reversedRoute.Found, reversedRoute.Diagnostic);
            CollectionAssert.AreEqual(new[] { "start", "alpha", "destination" }, firstRoute.LegLocationIds);
            CollectionAssert.AreEqual(firstRoute.LegLocationIds, reversedRoute.LegLocationIds,
                "Equal-cost routing must not depend on location or edge registration order.");
            Assert.AreEqual(2f, firstRoute.TotalMiles, 0.0001f);
            Assert.AreEqual(firstRoute.TotalMinutes, reversedRoute.TotalMinutes);
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

        [Test]
        public void TownNetwork_UsesMeasuredRoadDistanceFromSelectedJunction()
        {
            TownGrid grid = RoadGrid(8, 3, 2f, (x, z) => z == 1 && x >= 1 && x <= 6);
            PlacedBuilding west = Building(101, new GridCoord(1, 1));
            PlacedBuilding east = Building(102, new GridCoord(6, 1));
            var model = new JourneyModel();

            TownJourneyNetworkBuilder.BuildResult result = TownJourneyNetworkBuilder.Synchronize(
                model, grid, grid.CoordToWorldCenter(new GridCoord(4, 1)), new[] { west, east }, null);

            Assert.AreEqual(1, result.RoadComponents);
            Assert.AreEqual(2, result.RoutableBuildings);
            JourneyRoute route = model.FindRoute("building:101", "building:102", TravelMode.Foot);
            Assert.IsTrue(route.Found, route.Diagnostic);
            // Root is road cell x=4. Distances are 3 + 2 road cells from the
            // root. Both anchors lie directly on road cells.
            Assert.AreEqual(10f / 1609.344f, route.TotalMiles, 0.0001f);
        }

        [Test]
        public void TownNetwork_DoesNotConnectDisconnectedRoadComponents()
        {
            TownGrid grid = RoadGrid(12, 3, 2f,
                (x, z) => z == 1 && (x == 1 || x == 2 || x == 9 || x == 10));
            var model = new JourneyModel();
            TownJourneyNetworkBuilder.BuildResult result = TownJourneyNetworkBuilder.Synchronize(
                model, grid, grid.CoordToWorldCenter(new GridCoord(1, 1)),
                new[] { Building(201, new GridCoord(1, 1)), Building(202, new GridCoord(10, 1)) }, null);

            Assert.AreEqual(2, result.RoadComponents);
            Assert.IsFalse(model.FindRoute("building:201", "building:202", TravelMode.Foot).Found);
        }

        [Test]
        public void TownNetwork_RebuildIsIdempotentAndPreservesAuthoredJourneyData()
        {
            TownGrid grid = RoadGrid(5, 3, 2f, (x, z) => z == 1 && x >= 1 && x <= 3);
            var model = new JourneyModel();
            model.RegisterLocation(new JourneyLocation("authored-gateway", JourneyLocationKind.OffMapGateway,
                "Regional Trail", 20f, 0f));
            model.RegisterLocation(new JourneyLocation("authored-town", JourneyLocationKind.TownBuilding,
                "Neighboring Town", 0f, 20f));
            model.AddEdge("authored-gateway", "authored-town", 20f, "authored route");
            model.RegisterLocation(new JourneyLocation("town-center", JourneyLocationKind.Waypoint,
                "Previous town junction", 0f, 0f));
            model.AddEdge("town-center", "authored-gateway", 3f, "regional connection");
            PlacedBuilding building = Building(301, new GridCoord(2, 1));

            TownJourneyNetworkBuilder.Synchronize(model, grid, grid.CoordToWorldCenter(new GridCoord(2, 1)),
                new[] { building }, null);
            TownJourneyNetworkBuilder.Synchronize(model, grid, grid.CoordToWorldCenter(new GridCoord(2, 1)),
                new[] { building }, null);

            Assert.IsNotNull(model.GetLocation("building:301"));
            Assert.IsNotNull(model.GetLocation("authored-gateway"));
            Assert.IsTrue(model.FindRoute("authored-gateway", "authored-town", TravelMode.Wagon).Found);
            Assert.IsTrue(model.FindRoute("building:301", "authored-gateway", TravelMode.Wagon).Found,
                "Rebuilding local roads must preserve the stable regional connection.");
            Assert.AreEqual(1, CountTownBuildingLocations(model));
        }

        [Test]
        public void TownNetwork_SkipsBuildingsWithoutActualRoadAccess()
        {
            TownGrid grid = RoadGrid(6, 6, 2f, (x, z) => z == 1 && x >= 1 && x <= 3);
            PlacedBuilding remote = Building(401, new GridCoord(5, 5));
            var model = new JourneyModel();

            TownJourneyNetworkBuilder.BuildResult result = TownJourneyNetworkBuilder.Synchronize(
                model, grid, grid.CoordToWorldCenter(new GridCoord(1, 1)), new[] { remote }, null);

            Assert.AreEqual(1, result.BuildingsWithoutRoadAccess);
            Assert.IsNull(model.GetLocation("building:401"));
        }

        [Test]
        public void TownNetwork_UsesOnlyTheBuildingsOwnAuthoritativePlotFrontage()
        {
            TownGrid grid = RoadGrid(8, 4, 2f, (x, z) => z == 1 && x >= 1 && x <= 5);
            var plot = new TownPlot { id = 7, buildingId = 501, roadAccessCell = new GridCoord(2, 1) };
            var building = new PlacedBuilding { id = 501, plotId = 7 };
            var model = new JourneyModel();

            TownJourneyNetworkBuilder.BuildResult result = TownJourneyNetworkBuilder.Synchronize(
                model, grid, grid.CoordToWorldCenter(new GridCoord(2, 1)), new[] { plot },
                new[] { building }, null);

            Assert.AreEqual(1, result.RoutableBuildings);
            Assert.IsNotNull(model.GetLocation("building:501"),
                "The actual TownPlot frontage is authoritative when a placed building has no anchor record.");

            building.anchors.Add(new BuildingAnchor { type = AnchorType.FrontDoor, coord = new GridCoord(7, 3) });
            var invalidAnchorModel = new JourneyModel();
            TownJourneyNetworkBuilder.BuildResult invalidAnchor = TownJourneyNetworkBuilder.Synchronize(
                invalidAnchorModel, grid, grid.CoordToWorldCenter(new GridCoord(2, 1)), new[] { plot },
                new[] { building }, null);
            Assert.AreEqual(1, invalidAnchor.RoutableBuildings,
                "An authored anchor disconnected from roads may fall back to its own validated plot frontage.");
            Assert.IsNotNull(invalidAnchorModel.GetLocation("building:501"));

            var mismatchedModel = new JourneyModel();
            building.plotId = 8;
            TownJourneyNetworkBuilder.BuildResult mismatch = TownJourneyNetworkBuilder.Synchronize(
                mismatchedModel, grid, grid.CoordToWorldCenter(new GridCoord(2, 1)), new[] { plot },
                new[] { building }, null);
            Assert.AreEqual(1, mismatch.BuildingsWithoutRoadAccess);
            Assert.IsNull(mismatchedModel.GetLocation("building:501"),
                "A frontage from another plot must never be borrowed to invent building access.");
        }

        private static TownGrid RoadGrid(int width, int depth, float cellSize,
            System.Func<int, int, bool> isRoad)
        {
            var grid = new TownGrid(width, depth, cellSize, Vector3.zero);
            for (int z = 0; z < depth; z++)
            for (int x = 0; x < width; x++)
            {
                if (!isRoad(x, z)) continue;
                TownCell cell = grid.GetCell(new GridCoord(x, z));
                cell.occupancy |= CellOccupancy.Road;
                cell.roadType = RoadType.MainStreet;
                grid.SetCell(new GridCoord(x, z), cell);
            }
            return grid;
        }

        private static JourneyModel BuildEqualCostRouteModel(bool reverseRegistration)
        {
            var model = new JourneyModel();
            JourneyLocation[] locations =
            {
                new JourneyLocation("start", JourneyLocationKind.TownBuilding, "Start", 0f, 0f),
                new JourneyLocation("alpha", JourneyLocationKind.Waypoint, "Alpha", 0f, 0f),
                new JourneyLocation("beta", JourneyLocationKind.Waypoint, "Beta", 0f, 0f),
                new JourneyLocation("destination", JourneyLocationKind.TownBuilding, "Destination", 0f, 0f)
            };
            JourneyEdge[] edges =
            {
                new JourneyEdge("start", "beta", 1f, "beta road"),
                new JourneyEdge("beta", "destination", 1f, "beta road"),
                new JourneyEdge("start", "alpha", 1f, "alpha road"),
                new JourneyEdge("alpha", "destination", 1f, "alpha road")
            };

            for (int i = 0; i < locations.Length; i++)
            {
                int index = reverseRegistration ? locations.Length - 1 - i : i;
                Assert.IsNull(model.RegisterLocation(locations[index]));
            }
            for (int i = 0; i < edges.Length; i++)
            {
                int index = reverseRegistration ? edges.Length - 1 - i : i;
                Assert.IsNull(model.AddEdge(edges[index].FromLocationId, edges[index].ToLocationId,
                    edges[index].Miles, edges[index].RoadName));
            }

            return model;
        }

        private static PlacedBuilding Building(int id, GridCoord roadAccess)
        {
            var building = new PlacedBuilding { id = id };
            building.anchors.Add(new BuildingAnchor { type = AnchorType.FrontDoor, coord = roadAccess });
            return building;
        }

        private static int CountTownBuildingLocations(JourneyModel model)
        {
            int count = 0;
            foreach (JourneyLocation location in model.AllLocations())
                if (location.LocationId.StartsWith("building:", System.StringComparison.OrdinalIgnoreCase)) count++;
            return count;
        }
    }
}
