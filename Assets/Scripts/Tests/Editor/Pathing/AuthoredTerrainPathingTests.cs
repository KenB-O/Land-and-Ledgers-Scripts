using System.Collections.Generic;
using System.Reflection;
using LandLedgers.FirstLedger;
using LandLedgers.Pathing;
using LandLedgers.World;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LandLedgers.EditorTests.Pathing
{
    public sealed class AuthoredTerrainPathingTests
    {
        [Test]
        public void InvalidBlockedWaterAndSteepCellsRemainNonTraversableEvenWhenMarkedAsRoads()
        {
            List<Object> cleanup = new();
            try
            {
                CreateHarness(4, 1, cleanup, out TownGrid grid, out PathingManager pathing, out _);

                TownCell unknownRoad = BuildableCell(0f);
                unknownRoad.terrainZone = TerrainZone.Unknown;
                unknownRoad.occupancy |= CellOccupancy.Road;
                grid.SetCell(new GridCoord(0, 0), unknownRoad);

                TownCell waterRoad = BuildableCell(0f);
                waterRoad.terrainZone = TerrainZone.Blocked;
                waterRoad.blocked = true;
                waterRoad.occupancy |= CellOccupancy.Road;
                grid.SetCell(new GridCoord(1, 0), waterRoad);

                TownCell blockedRoad = BuildableCell(0f);
                blockedRoad.terrainZone = TerrainZone.Blocked;
                blockedRoad.occupancy |= CellOccupancy.Road | CellOccupancy.Blocked;
                grid.SetCell(new GridCoord(2, 0), blockedRoad);

                TownCell steep = BuildableCell(0f);
                steep.terrainZone = TerrainZone.Steep;
                grid.SetCell(new GridCoord(3, 0), steep);

                Assert.IsFalse(pathing.TryGetMovementCost(new GridCoord(0, 0), out _), "Failed samples must remain non-traversable.");
                Assert.IsFalse(pathing.TryGetMovementCost(new GridCoord(1, 0), out _), "Water/blocked terrain must remain non-traversable.");
                Assert.IsFalse(pathing.TryGetMovementCost(new GridCoord(2, 0), out _), "Blocked masks must override stale road occupancy.");
                Assert.IsFalse(pathing.TryGetMovementCost(new GridCoord(3, 0), out _), "Steep terrain must be rejected when rough traversal is disabled.");
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void PathingPrefersValidRoadCellsAndRejectsUnsafeHeightSteps()
        {
            List<Object> cleanup = new();
            try
            {
                CreateHarness(5, 3, cleanup, out TownGrid grid, out PathingManager pathing, out PathingSettings settings);
                settings.roadCost = 1;
                settings.generalWalkableCost = 20;
                FillBuildable(grid, 0f);
                for (int x = 0; x < grid.Width; x++)
                {
                    TownCell road = grid.GetCell(new GridCoord(x, 0));
                    road.occupancy |= CellOccupancy.Road;
                    road.roadType = RoadType.MainStreet;
                    grid.SetCell(new GridCoord(x, 0), road);
                }

                Assert.IsTrue(pathing.TryFindPath(new GridCoord(0, 1), new GridCoord(4, 1), out PathingResult preferred), preferred.FailureReason);
                Assert.That(preferred.Path.Exists(coord => coord.z == 0), Is.True, "The lower-cost valid road should be used.");

                CreateHarness(3, 1, cleanup, out TownGrid stepGrid, out PathingManager stepPathing, out PathingSettings stepSettings);
                stepSettings.maximumTraversableStepHeightMeters = 1.25f;
                stepGrid.SetCell(new GridCoord(0, 0), BuildableCell(0f));
                stepGrid.SetCell(new GridCoord(1, 0), BuildableCell(3f));
                stepGrid.SetCell(new GridCoord(2, 0), BuildableCell(0f));

                Assert.IsFalse(stepPathing.TryFindPath(
                    new PathingRequest(new GridCoord(0, 0), new GridCoord(2, 0), suppressFailureLog: true),
                    out PathingResult rejected));
                Assert.That(rejected.FailureCategory, Is.EqualTo(PathingFailureCategory.NoRoute));
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void PedestrianRouteGoesAroundBuildingFootprintInsteadOfCrossingIt()
        {
            List<Object> cleanup = new();
            try
            {
                CreateHarness(7, 5, cleanup, out TownGrid grid, out PathingManager pathing, out _);
                FillBuildable(grid, 0f);
                for (int z = 1; z <= 3; z++)
                {
                    TownCell wall = BuildableCell(0f);
                    wall.occupancy = CellOccupancy.Building | CellOccupancy.Blocked;
                    wall.buildingId = 41;
                    wall.blocked = true;
                    grid.SetCell(new GridCoord(3, z), wall);
                }

                Assert.IsTrue(pathing.TryFindPath(new GridCoord(0, 2), new GridCoord(6, 2), out PathingResult route), route.FailureReason);
                Assert.That(route.Path.Count, Is.GreaterThan(7), "A legal route must go around the solid footprint.");
                foreach (GridCoord point in route.Path)
                    Assert.IsFalse(grid.GetCell(point).HasBuilding, $"Route crossed building cell {point}.");
            }
            finally { DestroyAll(cleanup); }
        }

        [Test]
        public void AuthoredEntranceCanReconcileWhenCoarseDoorCellIsNotTraversable()
        {
            List<Object> cleanup = new();
            try
            {
                CreateHarness(7, 5, cleanup, out TownGrid grid, out PathingManager pathing, out _);
                FillBuildable(grid, 0f);

                GridCoord authoredDoorCell = new(3, 2);
                TownCell footprint = BuildableCell(0f);
                footprint.occupancy = CellOccupancy.Building | CellOccupancy.Blocked;
                footprint.buildingId = 0;
                footprint.blocked = true;
                grid.SetCell(authoredDoorCell, footprint);

                GameObject buildingObject = new("Blocked coarse storefront door");
                cleanup.Add(buildingObject);
                buildingObject.transform.position = grid.CoordToWorldCenter(authoredDoorCell);
                BuildingFootprintAuthority authority = buildingObject.AddComponent<BuildingFootprintAuthority>();
                typeof(BuildingFootprintAuthority)
                    .GetField("entrancePairs", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(authority, new List<BuildingEntrancePairAuthoring>
                    {
                        new()
                        {
                            id = "main-entrance",
                            indoorPositionLocal = Vector3.zero,
                            outdoorApproachLocal = new Vector3(0f, 0f, 2f),
                            clearWidthMeters = 1.1f
                        }
                    });

                PlacedBuilding placed = new()
                {
                    id = 0,
                    runtimeFootprintAuthority = authority
                };
                placed.anchors.Add(new BuildingAnchor { type = AnchorType.FrontDoor, coord = authoredDoorCell });
                List<PlacedBuilding> buildings = (List<PlacedBuilding>)typeof(TownWorldController)
                    .GetField("buildings", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(pathing.WorldController);
                Assert.IsNotNull(buildings);
                buildings.Add(placed);

                Assert.IsFalse(pathing.TryFindAnchorByBuildingId(0, AnchorType.FrontDoor, out _, out _),
                    "Normal route lookup must reject the non-traversable authored marker cell.");

                GameObject directorObject = new("Entrance route test director");
                cleanup.Add(directorObject);
                PopulationPathingDirector director = directorObject.AddComponent<PopulationPathingDirector>();
                typeof(PopulationPathingDirector)
                    .GetField("pathingManager", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(director, pathing);
                MethodInfo resolveDoor = typeof(PopulationPathingDirector).GetMethod("TryGetDoorAnchor",
                    BindingFlags.Instance | BindingFlags.NonPublic, null,
                    new[] { typeof(int), typeof(BuildingAnchor).MakeByRefType(), typeof(PlacedBuilding).MakeByRefType() }, null);
                Assert.IsNotNull(resolveDoor);

                object[] args = { 0, default(BuildingAnchor), null };
                bool resolved = (bool)resolveDoor.Invoke(director, args);
                BuildingAnchor routeAnchor = (BuildingAnchor)args[1];

                Assert.IsTrue(resolved, "The authored entrance should be projected to a physically walkable exterior cell.");
                Assert.IsTrue(routeAnchor.wasReconciledToExteriorAccess);
                Assert.AreNotEqual(authoredDoorCell, routeAnchor.coord);
                Assert.IsFalse(grid.GetCell(routeAnchor.coord).HasBuilding);
                Assert.IsTrue(pathing.TryGetMovementCost(routeAnchor.coord, out _),
                    "Reconciliation must return a real traversable path cell, not waive movement validation.");
            }
            finally { DestroyAll(cleanup); }
        }

        [Test]
        public void PrivatePlotIsNotUsedAsTransitShortcutButEndpointParcelRemainsAccessible()
        {
            List<Object> cleanup = new();
            try
            {
                CreateHarness(7, 5, cleanup, out TownGrid grid, out PathingManager pathing, out _);
                FillBuildable(grid, 0f);
                for (int z = 1; z <= 3; z++)
                for (int x = 2; x <= 4; x++)
                {
                    TownCell parcel = grid.GetCell(new GridCoord(x, z));
                    parcel.occupancy |= CellOccupancy.Plot;
                    parcel.plotId = 8;
                    grid.SetCell(new GridCoord(x, z), parcel);
                }

                Assert.IsTrue(pathing.TryFindPath(new GridCoord(0, 2), new GridCoord(6, 2), out PathingResult around), around.FailureReason);
                foreach (GridCoord point in around.Path)
                    Assert.AreNotEqual(8, grid.GetCell(point).plotId, $"Transit route crossed private parcel cell {point}.");

                Assert.IsTrue(pathing.TryFindPath(new GridCoord(0, 2), new GridCoord(3, 2), out PathingResult toParcel), toParcel.FailureReason);
                Assert.That(toParcel.Path.Exists(point => point.x >= 2 && point.x <= 4 && point.z >= 1 && point.z <= 3),
                    Is.True, "A destination parcel remains reachable from public ground.");
            }
            finally { DestroyAll(cleanup); }
        }

        [Test]
        public void PathingCrossesAdjacentTerrainTilesAtProviderGroundedWaypointHeights()
        {
            List<Object> cleanup = new();
            try
            {
                Terrain left = CreateFlatTerrain("Path Left", new Vector3(-4f, 3f, -1f), new Vector3(4f, 10f, 2f), 0.2f, cleanup);
                Terrain right = CreateFlatTerrain("Path Right", new Vector3(0f, 4f, -1f), new Vector3(4f, 10f, 2f), 0.1f, cleanup);
                PreAuthoredTerrainSurfaceProvider provider = new(
                    new[] { right, left }, null, null, false, 0f, 8f, 6f, 10f, 28f);
                CreateHarness(4, 1, cleanup, out TownGrid grid, out PathingManager pathing, out PathingSettings settings, new Vector3(-4f, 0f, -1f));
                settings.agentGroundOffsetMeters = 0.08f;

                for (int x = 0; x < grid.Width; x++)
                {
                    GridCoord coord = new(x, 0);
                    Vector3 center = grid.CoordToWorldCenter(coord);
                    Assert.IsTrue(provider.TrySampleHeight(center, out float height));
                    grid.SetCell(coord, BuildableCell(height));
                }

                Assert.IsTrue(pathing.TryFindPath(new GridCoord(0, 0), new GridCoord(3, 0), out PathingResult result), result.FailureReason);
                Assert.That(result.Path.Count, Is.EqualTo(4));
                for (int i = 0; i < result.Path.Count; i++)
                {
                    Assert.That(pathing.CoordToPathWorld(result.Path[i]).y, Is.EqualTo(5.08f).Within(0.001f));
                }
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        private static void CreateHarness(
            int width,
            int depth,
            List<Object> cleanup,
            out TownGrid grid,
            out PathingManager pathing,
            out PathingSettings settings,
            Vector3? origin = null)
        {
            GameObject worldObject = new("Pathing Test World");
            cleanup.Add(worldObject);
            TownWorldController world = worldObject.AddComponent<TownWorldController>();
            grid = new TownGrid(width, depth, 2f, origin ?? Vector3.zero);
            typeof(TownWorldController)
                .GetField("grid", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(world, grid);

            GameObject pathingObject = new("Pathing Test Manager");
            cleanup.Add(pathingObject);
            pathing = pathingObject.AddComponent<PathingManager>();
            settings = ScriptableObject.CreateInstance<PathingSettings>();
            settings.logFailedRequests = false;
            settings.allowRoughTerrain = false;
            cleanup.Add(settings);
            pathing.Configure(world, settings);
        }

        private static void FillBuildable(TownGrid grid, float height)
        {
            for (int z = 0; z < grid.Depth; z++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    grid.SetCell(new GridCoord(x, z), BuildableCell(height));
                }
            }
        }

        private static TownCell BuildableCell(float height)
        {
            TownCell cell = TownCell.CreateDefault();
            cell.terrainZone = TerrainZone.Buildable;
            cell.height = height;
            return cell;
        }

        private static Terrain CreateFlatTerrain(
            string name,
            Vector3 position,
            Vector3 size,
            float normalizedHeight,
            List<Object> cleanup)
        {
            TerrainData data = new() { heightmapResolution = 33, size = size };
            float[,] heights = new float[33, 33];
            for (int z = 0; z < 33; z++)
            {
                for (int x = 0; x < 33; x++)
                {
                    heights[z, x] = normalizedHeight;
                }
            }

            data.SetHeights(0, 0, heights);
            GameObject terrainObject = Terrain.CreateTerrainGameObject(data);
            terrainObject.name = name;
            terrainObject.transform.position = position;
            cleanup.Add(data);
            cleanup.Add(terrainObject);
            return terrainObject.GetComponent<Terrain>();
        }

        private static void DestroyAll(List<Object> cleanup)
        {
            for (int i = cleanup.Count - 1; i >= 0; i--)
            {
                if (cleanup[i] != null)
                {
                    Object.DestroyImmediate(cleanup[i]);
                }
            }
        }
    }
}
