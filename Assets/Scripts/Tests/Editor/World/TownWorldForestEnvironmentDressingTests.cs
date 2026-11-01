using System.Collections.Generic;
using LandLedgers.World;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LandLedgers.Editor.World
{
    public sealed class TownWorldForestEnvironmentDressingTests
    {
        [Test]
        public void ForestDressingIsDeterministicForSameSeed()
        {
            List<Object> cleanup = new();
            try
            {
                GameObject tree = CreateDummyPrefab("Test Forest Tree");
                GameObject brush = CreateDummyPrefab("Test Forest Brush");
                GameObject ground = CreateDummyPrefab("Test Forest Ground");
                GameObject deadfall = CreateDummyPrefab("Test Forest Deadfall");
                cleanup.Add(tree);
                cleanup.Add(brush);
                cleanup.Add(ground);
                cleanup.Add(deadfall);

                BuildingDefinition definition = CreateAgriculturalDefinition();
                cleanup.Add(definition);
                TownGenerationSettings settings = CreateSettings(definition);
                cleanup.Add(settings);
                AssignAllForestPrefabs(settings, tree, brush, ground, deadfall);

                TownWorldController controller = CreateController(settings, cleanup);
                controller.GenerateTownShell();
                List<string> firstSnapshot = CaptureForestVisualSnapshot(controller.VisualRoot);

                controller.GenerateTownShell();
                List<string> secondSnapshot = CaptureForestVisualSnapshot(controller.VisualRoot);

                Assert.That(firstSnapshot, Is.Not.Empty);
                CollectionAssert.AreEqual(firstSnapshot, secondSnapshot);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void SawmillForestDressingCreatesTimberAndDeadfallVisuals()
        {
            List<Object> cleanup = new();
            try
            {
                GameObject tree = CreateDummyPrefab("Test Sawmill Tree");
                GameObject brush = CreateDummyPrefab("Test Sawmill Brush");
                GameObject deadfall = CreateDummyPrefab("Test Sawmill Deadfall");
                cleanup.Add(tree);
                cleanup.Add(brush);
                cleanup.Add(deadfall);

                BuildingDefinition definition = CreateAgriculturalDefinition();
                cleanup.Add(definition);
                TownGenerationSettings settings = CreateSettings(definition);
                cleanup.Add(settings);
                settings.mapEdgeForestBandWidthCells = 0;
                settings.wetGroundCorridorWidthCells = 0;
                settings.forestCanopyTreePrefabs = new[] { tree };
                settings.forestUnderstoryPrefabs = new[] { brush };
                settings.forestDeadfallPrefabs = new[] { deadfall };
                settings.Sanitize();

                TownWorldController controller = CreateController(settings, cleanup);
                controller.GenerateTownShell();

                List<Transform> sawmillVisuals = FindForestVisuals(controller.VisualRoot, "Sawmill");
                Assert.That(sawmillVisuals.Count, Is.GreaterThan(0));
                Assert.That(sawmillVisuals.Exists(t => t.name.Contains("Deadfall") || t.name.Contains("Timber Stand")), Is.True);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void MapEdgeForestDressingSkipsClaimedAndBlockedCells()
        {
            List<Object> cleanup = new();
            try
            {
                GameObject tree = CreateDummyPrefab("Test Edge Tree");
                cleanup.Add(tree);

                BuildingDefinition definition = CreateAgriculturalDefinition();
                cleanup.Add(definition);
                TownGenerationSettings settings = CreateSettings(definition);
                cleanup.Add(settings);
                settings.generateAgriculturalParcels = false;
                settings.generateSawmillProperty = false;
                settings.mapEdgeForestBandWidthCells = 8;
                settings.wetGroundCorridorWidthCells = 0;
                settings.forestDressingMaxInstances = 80;
                settings.forestCanopyTreePrefabs = new[] { tree };
                settings.Sanitize();

                TownWorldController controller = CreateController(settings, cleanup);
                controller.GenerateTownShell();

                List<Transform> edgeVisuals = FindForestVisuals(controller.VisualRoot, "Map Edge");
                Assert.That(edgeVisuals.Count, Is.GreaterThan(0));
                foreach (Transform visual in edgeVisuals)
                {
                    GridCoord coord = controller.Grid.WorldToCoord(visual.position);
                    TownCell cell = controller.Grid.GetCell(coord);
                    int edgeDistance = Mathf.Min(
                        Mathf.Min(coord.x, controller.Grid.Width - 1 - coord.x),
                        Mathf.Min(coord.z, controller.Grid.Depth - 1 - coord.z));

                    Assert.That(edgeDistance, Is.LessThan(settings.mapEdgeForestBandWidthCells), visual.name);
                    Assert.That(cell.IsRoad, Is.False, visual.name);
                    Assert.That(cell.HasBuilding, Is.False, visual.name);
                    Assert.That(cell.blocked, Is.False, visual.name);
                    Assert.That((cell.occupancy & (CellOccupancy.Plot | CellOccupancy.Anchor)), Is.EqualTo(CellOccupancy.None), visual.name);
                }
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void WetGroundCorridorDressingDoesNotChangeGridOccupancy()
        {
            List<Object> cleanup = new();
            try
            {
                GameObject wet = CreateDummyPrefab("Test Wet Ground");
                cleanup.Add(wet);

                BuildingDefinition definition = CreateAgriculturalDefinition();
                cleanup.Add(definition);
                TownGenerationSettings settings = CreateSettings(definition);
                cleanup.Add(settings);
                settings.generateAgriculturalParcels = false;
                settings.generateSawmillProperty = false;
                settings.mapEdgeForestBandWidthCells = 0;
                settings.wetGroundCorridorWidthCells = 5;
                settings.forestWetGroundPrefabs = new[] { wet };
                settings.Sanitize();

                TownWorldController controller = CreateController(settings, cleanup);
                settings.generateForestEnvironmentDressing = false;
                controller.GenerateTownShell();
                CellOccupancy[,] before = CaptureOccupancy(controller.Grid);

                settings.generateForestEnvironmentDressing = true;
                controller.GenerateTownShell();
                CellOccupancy[,] after = CaptureOccupancy(controller.Grid);

                CollectionAssert.AreEqual(before, after);
                Assert.That(FindForestVisuals(controller.VisualRoot, "Wet Ground Corridor").Count, Is.GreaterThan(0));
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void EmptyForestPrefabArraysDoNotCreateVisualsOrThrow()
        {
            List<Object> cleanup = new();
            try
            {
                BuildingDefinition definition = CreateAgriculturalDefinition();
                cleanup.Add(definition);
                TownGenerationSettings settings = CreateSettings(definition);
                cleanup.Add(settings);

                TownWorldController controller = CreateController(settings, cleanup);
                Assert.DoesNotThrow(() => controller.GenerateTownShell());
                Assert.That(FindForestVisuals(controller.VisualRoot, string.Empty), Is.Empty);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        private static TownWorldController CreateController(TownGenerationSettings settings, List<Object> cleanup)
        {
            GameObject townObject = new("Forest Dressing Test Town");
            cleanup.Add(townObject);
            TownWorldController controller = townObject.AddComponent<TownWorldController>();
            controller.Configure(settings, null, null);
            return controller;
        }

        private static TownGenerationSettings CreateSettings(BuildingDefinition definition)
        {
            TownGenerationSettings settings = ScriptableObject.CreateInstance<TownGenerationSettings>();
            settings.gridWidthCells = 80;
            settings.gridDepthCells = 80;
            settings.cellSizeMeters = 2f;
            settings.seed = 1886;
            settings.roadWidthCells = 3;
            settings.mainStreetLengthCells = 56;
            settings.crossStreetCount = 0;
            settings.plotDepthCells = 8;
            settings.minPlotFrontageCells = 8;
            settings.maxPlotFrontageCells = 8;
            settings.generateAgriculturalParcels = true;
            settings.agricultureSpurRoadLengthCells = 24;
            settings.agricultureDistanceFromCenterCells = 20;
            settings.cropFarmParcelSizeCells = new Vector2Int(10, 10);
            settings.ranchParcelSizeCells = new Vector2Int(12, 12);
            settings.generateSawmillProperty = true;
            settings.sawmillSpurRoadLengthCells = 32;
            settings.sawmillDistanceFromCenterCells = 30;
            settings.sawmillParcelSizeCells = new Vector2Int(18, 16);
            settings.generateTownHall = false;
            settings.showTerrainOverlay = false;
            settings.showRoads = false;
            settings.showPlots = false;
            settings.showBuildings = false;
            settings.generateDetailProps = false;
            settings.forestDressingMaxInstances = 120;
            settings.mapEdgeForestBandWidthCells = 8;
            settings.sawmillForestRadiusCells = 8;
            settings.wetGroundCorridorWidthCells = 4;
            settings.buildingCatalog = new[] { definition };
            settings.Sanitize();
            return settings;
        }

        private static BuildingDefinition CreateAgriculturalDefinition()
        {
            BuildingDefinition definition = ScriptableObject.CreateInstance<BuildingDefinition>();
            definition.ConfigureRuntimeFallback(
                "test_agricultural_shell",
                "Test Agricultural Shell",
                PlotZone.Agricultural,
                new Vector2Int(4, 4),
                Color.white,
                4f);
            return definition;
        }

        private static GameObject CreateDummyPrefab(string name)
        {
            GameObject prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            prefab.name = name;
            return prefab;
        }

        private static void AssignAllForestPrefabs(TownGenerationSettings settings, GameObject tree, GameObject brush, GameObject ground, GameObject deadfall)
        {
            settings.forestCanopyTreePrefabs = new[] { tree };
            settings.forestUnderstoryPrefabs = new[] { brush };
            settings.forestGroundCoverPrefabs = new[] { ground };
            settings.forestWetGroundPrefabs = new[] { ground };
            settings.forestDeadfallPrefabs = new[] { deadfall };
            settings.forestMeadowPrefabs = new[] { ground };
            settings.forestRockPrefabs = new[] { deadfall };
            settings.Sanitize();
        }

        private static List<string> CaptureForestVisualSnapshot(Transform root)
        {
            List<string> snapshot = new();
            foreach (Transform visual in FindForestVisuals(root, string.Empty))
            {
                Vector3 position = visual.position;
                snapshot.Add($"{visual.name}|{position.x:F3}|{position.y:F3}|{position.z:F3}");
            }

            snapshot.Sort();
            return snapshot;
        }

        private static List<Transform> FindForestVisuals(Transform root, string nameContains)
        {
            List<Transform> visuals = new();
            if (root == null)
            {
                return visuals;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child != null
                    && child.name.StartsWith("Forest Environment", System.StringComparison.Ordinal)
                    && (string.IsNullOrEmpty(nameContains) || child.name.Contains(nameContains)))
                {
                    visuals.Add(child);
                }
            }

            return visuals;
        }

        private static CellOccupancy[,] CaptureOccupancy(TownGrid grid)
        {
            CellOccupancy[,] occupancy = new CellOccupancy[grid.Width, grid.Depth];
            for (int z = 0; z < grid.Depth; z++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    occupancy[x, z] = grid.GetCell(new GridCoord(x, z)).occupancy;
                }
            }

            return occupancy;
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
