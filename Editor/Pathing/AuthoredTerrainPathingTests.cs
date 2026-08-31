using System.Collections.Generic;
using System.Reflection;
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
