using System;
using System.Collections.Generic;
using LandLedgers.World;
using LandLedgers.World.Journeys;
using LandLedgers.World.Regional;
using UnityEngine;
using NUnit.Framework;

namespace LandLedgers.EditorTests.World
{
    /// <summary>
    /// Plan 03 Slice 0 (P0-2): pin the pure journey contract the production
    /// registration path depends on — model, town-network builder, regional
    /// registry, and exit-point resolver, in isolation. These tests prove
    /// the model, builder, and registry are faithful authorities when fed a
    /// real grid; they do NOT prove the drivers ever feed them one — that is
    /// the Unity-owed part of Slice 0 (Codex PlayMode + scene check).
    /// </summary>
    [TestFixture]
    public sealed class JourneyLocationRegistrationTests
    {
        [Test]
        public void Synchronize_RegistersTownCenterAndBuildings_WithMeasuredEdges_AndRouteFound()
        {
            TownGrid grid = RoadGrid(8, 3, 2f, (x, z) => z == 1 && x >= 1 && x <= 6);
            PlacedBuilding west = Building(101, new GridCoord(1, 1));
            PlacedBuilding east = Building(102, new GridCoord(6, 1));
            var model = new JourneyModel();

            TownJourneyNetworkBuilder.BuildResult result = TownJourneyNetworkBuilder.Synchronize(
                model, grid, grid.CoordToWorldCenter(new GridCoord(4, 1)), new[] { west, east }, null);

            Assert.AreEqual(1, result.RoadComponents);
            Assert.AreEqual(2, result.RoutableBuildings);
            Assert.AreEqual(0, result.BuildingsWithoutRoadAccess);
            Assert.IsNotNull(model.GetLocation("town-center"));
            Assert.IsNotNull(model.GetLocation("building:101"));
            Assert.IsNotNull(model.GetLocation("building:102"));
            Assert.AreEqual(3, CountLocations(model));

            JourneyRoute route = model.FindRoute("building:101", "building:102", TravelMode.Foot);
            Assert.IsTrue(route.Found, route.Diagnostic);
            // Root is road cell x=4. Distances are 3 + 2 road cells from the
            // root, at 2 m per cell: 10 m of measured road.
            Assert.AreEqual(10f / 1609.344f, route.TotalMiles, 0.0001f);
        }

        [Test]
        public void Synchronize_ZeroRoadCells_RegistersNothing_WithDiagnostic()
        {
            TownGrid grid = RoadGrid(5, 3, 2f, (x, z) => false);
            var model = new JourneyModel();

            TownJourneyNetworkBuilder.BuildResult result = TownJourneyNetworkBuilder.Synchronize(
                model, grid, grid.CoordToWorldCenter(new GridCoord(2, 1)),
                new[] { Building(101, new GridCoord(2, 1)) }, null);

            Assert.AreEqual(0, result.RoadComponents);
            Assert.IsTrue(result.Diagnostic.Contains("no road cells"),
                "Unexpected diagnostic: " + result.Diagnostic);
            Assert.IsNull(model.GetLocation("town-center"));
            Assert.IsNull(model.GetLocation("building:101"));
            Assert.AreEqual(0, CountLocations(model));
        }

        [Test]
        public void Synchronize_NullGrid_RegistersNothing_WithDiagnostic()
        {
            var model = new JourneyModel();

            TownJourneyNetworkBuilder.BuildResult result = TownJourneyNetworkBuilder.Synchronize(
                model, null, Vector3.zero, new[] { Building(101, new GridCoord(2, 1)) }, null);

            Assert.AreEqual(0, result.RoadComponents);
            Assert.IsTrue(result.Diagnostic.Contains("No generated TownGrid"),
                "Unexpected diagnostic: " + result.Diagnostic);
            Assert.AreEqual(0, CountLocations(model));
        }

        [Test]
        public void Synchronize_Twice_IsIdempotent_AndPreservesAuthoredLocations()
        {
            TownGrid grid = RoadGrid(5, 3, 2f, (x, z) => z == 1 && x >= 1 && x <= 3);
            var model = new JourneyModel();
            Assert.IsNull(model.RegisterLocation(new JourneyLocation("authored-gateway", JourneyLocationKind.OffMapGateway,
                "Regional Trail", 20f, 0f)));
            Assert.IsNull(model.RegisterLocation(new JourneyLocation("authored-town", JourneyLocationKind.TownBuilding,
                "Neighboring Town", 0f, 20f)));
            Assert.IsNull(model.AddEdge("authored-gateway", "authored-town", 20f, "authored route"));
            Assert.IsNull(model.RegisterLocation(new JourneyLocation("town-center", JourneyLocationKind.Waypoint,
                "Previous town junction", 0f, 0f)));
            Assert.IsNull(model.AddEdge("town-center", "authored-gateway", 3f, "regional connection"));
            PlacedBuilding building = Building(301, new GridCoord(2, 1));

            TownJourneyNetworkBuilder.Synchronize(model, grid, grid.CoordToWorldCenter(new GridCoord(2, 1)),
                new[] { building }, null);
            int afterFirst = CountLocations(model);
            TownJourneyNetworkBuilder.Synchronize(model, grid, grid.CoordToWorldCenter(new GridCoord(2, 1)),
                new[] { building }, null);
            int afterSecond = CountLocations(model);

            Assert.AreEqual(afterFirst, afterSecond, "A second sync must not duplicate locations.");
            Assert.AreEqual(4, afterSecond, "town-center + building:301 + 2 authored locations.");
            Assert.IsNotNull(model.GetLocation("building:301"));
            Assert.IsNotNull(model.GetLocation("authored-gateway"));
            Assert.IsNotNull(model.GetLocation("authored-town"));
            Assert.IsTrue(model.FindRoute("authored-gateway", "authored-town", TravelMode.Wagon).Found);
            Assert.IsTrue(model.FindRoute("building:301", "authored-gateway", TravelMode.Wagon).Found,
                "Rebuilding local roads must preserve the stable regional connection.");
        }

        [Test]
        public void Synchronize_DifferentBuildingSet_RemovesStaleBuildingLocations()
        {
            TownGrid grid = RoadGrid(8, 3, 2f, (x, z) => z == 1 && x >= 1 && x <= 6);
            var model = new JourneyModel();
            Vector3 center = grid.CoordToWorldCenter(new GridCoord(4, 1));

            TownJourneyNetworkBuilder.Synchronize(model, grid, center,
                new[] { Building(101, new GridCoord(1, 1)), Building(102, new GridCoord(6, 1)) }, null);
            Assert.IsNotNull(model.GetLocation("building:101"));
            Assert.IsNotNull(model.GetLocation("building:102"));

            // A new town with a different building set: the old building
            // locations must go, the new one registers, town-center stays.
            TownJourneyNetworkBuilder.Synchronize(model, grid, center,
                new[] { Building(103, new GridCoord(6, 1)) }, null);

            Assert.IsNull(model.GetLocation("building:101"));
            Assert.IsNull(model.GetLocation("building:102"));
            Assert.IsNotNull(model.GetLocation("building:103"));
            Assert.IsNotNull(model.GetLocation("town-center"));
        }

        [Test]
        public void ApplyAll_WithoutTownCenter_Refused_WithDiagnostic()
        {
            var model = new JourneyModel();
            var diag = new List<string>();

            int registered = RegionalTownRegistry.ApplyAll(model, diag);

            Assert.AreEqual(-1, registered);
            Assert.IsTrue(string.Join(" ", diag).Contains("town-center"),
                "Refusal must name the missing town-center anchor: " + string.Join(" | ", diag));
            Assert.IsNull(model.GetLocation("ashford"));
            Assert.IsNull(model.GetLocation("ashford-millbrook_grain_co"));
            Assert.IsNull(model.GetLocation("redfield"));
        }

        [Test]
        public void ApplyAll_AfterTownSync_RegistersTwenty_AndSecondApplyRegistersZero()
        {
            JourneyModel model = SyncedTownModel();
            var diag = new List<string>();

            int registered = RegionalTownRegistry.ApplyAll(model, diag);

            // Ashford: centre + gateway + 8 sites = 10; Redfield: same = 10.
            Assert.AreEqual(20, registered);
            Assert.IsNotNull(model.GetLocation("ashford-millbrook_grain_co"));
            Assert.IsNotNull(model.GetLocation("redfield-redfield_grain_feed"));
            int afterFirst = CountLocations(model);

            diag.Clear();
            int second = RegionalTownRegistry.ApplyAll(model, diag);
            Assert.AreEqual(0, second, "A second ApplyAll must skip both fully-registered towns.");
            Assert.AreEqual(afterFirst, CountLocations(model), "No duplicates on re-apply.");
        }

        [Test]
        public void ApplyTown_PartlyRegistered_Refused_NeverHalfApplied()
        {
            var model = new JourneyModel();
            Assert.IsNull(model.RegisterLocation(new JourneyLocation("town-center", JourneyLocationKind.Waypoint,
                "Town Centre", 0f, 0f)));
            // One Ashford location pre-registered by hand: a partial town.
            Assert.IsNull(model.RegisterLocation(new JourneyLocation("ashford", JourneyLocationKind.Waypoint,
                "Ashford Town Centre", 35.35534f, 35.35534f)));
            var diag = new List<string>();

            int registered = RegionalTownRegistry.ApplyTown(model, RegionalTownRegistry.Ashford, diag);

            Assert.AreEqual(-1, registered);
            Assert.IsTrue(string.Join(" ", diag).Contains("partly registered"),
                "Refusal must name the partial apply: " + string.Join(" | ", diag));
            Assert.IsNull(model.GetLocation("ashford-gateway"));
            Assert.IsNull(model.GetLocation("ashford-millbrook_grain_co"));
        }

        [Test]
        public void FindRoute_TownCenterToMillbrook_IsEstablishedFiftyPointThreeTwoMileRoute()
        {
            JourneyModel model = SyncedTownModel();
            RegionalTownRegistry.ApplyAll(model, new List<string>());

            JourneyRoute route = model.FindRoute("town-center", "ashford-millbrook_grain_co", TravelMode.Wagon);

            Assert.IsTrue(route.Found, route.Diagnostic);
            // Established edge sum: home centre -> gateway 49.5 mi,
            // gateway -> centre 0.5 mi, centre -> Millbrook site
            // sqrt(0.30^2 + 0.10^2) = 0.316... mi; total 50.316... mi.
            Assert.AreEqual(50.32f, route.TotalMiles, 0.01f);
        }

        [Test]
        public void RegionalExitPointResolver_TownCenterToMillbrook_SucceedsViaAshfordGateway()
        {
            JourneyModel model = SyncedTownModel();
            RegionalTownRegistry.ApplyAll(model, new List<string>());

            bool resolved = RegionalExitPointResolver.TryResolve(
                model, "town-center", "ashford-millbrook_grain_co",
                out RegionalExitPoint exitPoint, out string reason);

            Assert.IsTrue(resolved, reason);
            Assert.IsNotNull(exitPoint);
            Assert.AreEqual("ashford-gateway", exitPoint.GatewayLocationId);
            Assert.AreEqual("Millbrook Grain Co.", exitPoint.DestinationLabel);
            Assert.AreEqual(50.32f, exitPoint.RouteMiles, 0.01f);
            Assert.AreEqual(49.5f, exitPoint.GatewayRouteMiles, 0.001f);
        }

        private static JourneyModel SyncedTownModel()
        {
            TownGrid grid = RoadGrid(8, 3, 2f, (x, z) => z == 1 && x >= 1 && x <= 6);
            var model = new JourneyModel();
            TownJourneyNetworkBuilder.Synchronize(model, grid, grid.CoordToWorldCenter(new GridCoord(4, 1)),
                new[] { Building(101, new GridCoord(1, 1)), Building(102, new GridCoord(6, 1)) }, null);
            Assert.IsNotNull(model.GetLocation("town-center"), "Test setup: town sync must register town-center.");
            return model;
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

        private static PlacedBuilding Building(int id, GridCoord roadAccess)
        {
            var building = new PlacedBuilding { id = id };
            building.anchors.Add(new BuildingAnchor { type = AnchorType.FrontDoor, coord = roadAccess });
            return building;
        }

        private static int CountLocations(JourneyModel model)
        {
            int count = 0;
            foreach (JourneyLocation location in model.AllLocations())
                if (location != null) count++;
            return count;
        }
    }
}
