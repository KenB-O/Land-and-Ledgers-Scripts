using System.Collections.Generic;
using LandLedgers.Persistence;
using LandLedgers.World;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LandLedgers.Editor.World
{
    public sealed class AuthoredTerrainDevelopmentTests
    {
        [Test]
        public void MultiTileSamplingIncludesTransformYAndUsesDeterministicSeamTile()
        {
            List<Object> cleanup = new();
            try
            {
                Terrain left = CreateFlatTerrain("Left Tile", new Vector3(-20f, 3f, -10f), new Vector3(20f, 10f, 20f), 0.2f, cleanup);
                Terrain right = CreateFlatTerrain("Right Tile", new Vector3(0f, 11f, -10f), new Vector3(20f, 10f, 20f), 0.4f, cleanup);
                PreAuthoredTerrainSurfaceProvider provider = new(
                    new[] { right, left },
                    null,
                    null,
                    false,
                    0f,
                    8f,
                    6f,
                    10f,
                    28f);

                Assert.IsTrue(provider.TrySampleHeight(new Vector3(-5f, 0f, 0f), out float leftHeight));
                Assert.That(leftHeight, Is.EqualTo(5f).Within(0.01f));
                Assert.IsTrue(provider.TrySampleHeight(new Vector3(5f, 0f, 0f), out float rightHeight));
                Assert.That(rightHeight, Is.EqualTo(15f).Within(0.01f));
                Assert.IsTrue(provider.TrySampleHeight(new Vector3(0f, 0f, 0f), out float seamHeight));
                Assert.That(seamHeight, Is.EqualTo(15f).Within(0.01f), "The positive-origin seam tile must win deterministically.");
                Assert.IsFalse(provider.TrySampleSurface(new Vector3(25f, 0f, 0f), out _));
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void MaskContainmentUsesColliderShapeInsteadOfOnlyBounds()
        {
            GameObject maskObject = new("Round Water Mask");
            try
            {
                SphereCollider sphere = maskObject.AddComponent<SphereCollider>();
                sphere.radius = 1f;
                WorldPlacementMaskVolume mask = maskObject.AddComponent<WorldPlacementMaskVolume>();

                Assert.IsTrue(mask.Contains(new Vector3(0.5f, 0f, 0f)));
                Assert.IsFalse(mask.Contains(new Vector3(0.9f, 0f, 0.9f)), "Point is inside the AABB but outside the sphere.");
            }
            finally
            {
                Object.DestroyImmediate(maskObject);
            }
        }

        [Test]
        public void FailedAuthoredSamplesAreNotBuildable()
        {
            FailingSurfaceProvider provider = new();
            Assert.IsFalse(provider.IsBuildable(Vector3.zero, new PlacementQuery { forBuilding = true, maximumSlopeDegrees = 8f }));
            Assert.IsFalse(provider.IsRoadCompatible(Vector3.zero, new RoadQuery { maximumSlopeDegrees = 10f }));
            Assert.IsFalse(provider.IsResourceCompatible(Vector3.zero, new ResourcePlacementQuery { maximumSlopeDegrees = 28f }));
        }

        [Test]
        public void ProceduralTestFallbackWithoutSurfaceSamplesKeepsAUsableFlatGrid()
        {
            List<Object> cleanup = new();
            try
            {
                TownGenerationSettings settings = CreateSettings();
                cleanup.Add(settings);
                settings.gridWidthCells = 40;
                settings.gridDepthCells = 50;
                settings.mainStreetLengthCells = 30;
                settings.crossStreetCount = 0;
                settings.plotDepthCells = 8;
                settings.minPlotFrontageCells = 8;
                settings.maxPlotFrontageCells = 8;
                settings.terrainLayers = 0;
                settings.Sanitize();

                GameObject townObject = new("Procedural Flat Fallback Test Town");
                cleanup.Add(townObject);
                TownWorldController controller = townObject.AddComponent<TownWorldController>();
                controller.Configure(settings, null, null);
                controller.ConfigureStartup(
                    WorldStartupMode.GenerateProceduralTerrain,
                    null,
                    "procedural-test-fallback",
                    false);

                controller.GenerateTownShell();

                Assert.NotNull(controller.Grid);
                int usableCellCount = 0;
                for (int z = 0; z < controller.Grid.Depth; z++)
                {
                    for (int x = 0; x < controller.Grid.Width; x++)
                    {
                        TownCell cell = controller.Grid.GetCell(new GridCoord(x, z));
                        if (!cell.blocked && cell.terrainZone == TerrainZone.Buildable)
                        {
                            usableCellCount++;
                        }
                    }
                }

                Assert.That(usableCellCount, Is.GreaterThan(0), "Legacy procedural/test fallback must remain buildable when no surface sample is available.");
                Assert.That(controller.Plots.Count, Is.GreaterThan(0), "Legacy procedural/test fallback must still produce usable town plots.");
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void FixedAnchorValidationRejectsOutsideWaterNoTownAndSlope()
        {
            TownSiteSelectionRequest request = new()
            {
                townSizeMeters = new Vector2(10f, 10f),
                maximumTownSlopeDegrees = 6f,
                maximumRoadSlopeDegrees = 10f,
                validationGridResolution = 3
            };
            RuleSurfaceProvider provider = new(new Bounds(Vector3.zero, new Vector3(40f, 20f, 40f)));

            Assert.IsFalse(TownSiteSelector.TryValidateFixed(provider, request, new Vector3(30f, 0f, 0f), out _, out string outside));
            StringAssert.Contains("bounds", outside);

            provider.Water = true;
            Assert.IsFalse(TownSiteSelector.TryValidateFixed(provider, request, Vector3.zero, out _, out string water));
            StringAssert.Contains("water", water);

            provider.Water = false;
            provider.Slope = 20f;
            Assert.IsFalse(TownSiteSelector.TryValidateFixed(provider, request, Vector3.zero, out _, out string slope));
            StringAssert.Contains("slope", slope);

            List<Object> cleanup = new();
            try
            {
                Terrain terrain = CreateFlatTerrain("NoTown Terrain", new Vector3(-20f, 0f, -20f), new Vector3(40f, 10f, 40f), 0f, cleanup);
                GameObject maskObject = new("NoTown Mask");
                cleanup.Add(maskObject);
                BoxCollider collider = maskObject.AddComponent<BoxCollider>();
                collider.size = new Vector3(4f, 4f, 4f);
                WorldPlacementMaskVolume mask = maskObject.AddComponent<WorldPlacementMaskVolume>();
                SetPrivateField(mask, "maskType", WorldPlacementMaskType.NoTown);
                PreAuthoredTerrainSurfaceProvider maskedProvider = new(
                    new[] { terrain },
                    new[] { mask },
                    null,
                    false,
                    0f,
                    8f,
                    6f,
                    10f,
                    28f);
                Assert.IsFalse(TownSiteSelector.TryValidateFixed(maskedProvider, request, Vector3.zero, out _, out string noTown));
                StringAssert.Contains("NoTown", noTown);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void GenerationAndLoadDoNotMutateTerrainDataOrDeleteAuthoredChildren()
        {
            List<Object> cleanup = new();
            try
            {
                Terrain terrain = CreateFlatTerrain("Immutable Authored Terrain", new Vector3(-40f, 7f, -40f), new Vector3(80f, 20f, 80f), 0.25f, cleanup);
                TerrainData terrainData = terrain.terrainData;
                Vector3 terrainPosition = terrain.transform.position;
                Vector3 terrainSize = terrainData.size;
                int heightmapResolution = terrainData.heightmapResolution;
                float[,] beforeHeights = terrainData.GetHeights(0, 0, heightmapResolution, heightmapResolution);
                TerrainLayer[] beforeLayers = terrainData.terrainLayers;
                int terrainCount = Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include).Length;

                TownGenerationSettings settings = CreateSettings();
                cleanup.Add(settings);
                GameObject townObject = new("Generated Root Safety Town");
                cleanup.Add(townObject);
                GameObject visualRoot = new("WorldVisualRoot");
                visualRoot.transform.SetParent(townObject.transform, false);
                GameObject authoredDressing = new("Hand Authored Dressing");
                authoredDressing.transform.SetParent(visualRoot.transform, false);

                TownWorldController controller = townObject.AddComponent<TownWorldController>();
                controller.Configure(settings, terrain.GetComponent<TerrainCollider>(), visualRoot.transform);
                controller.ConfigureStartup(
                    WorldStartupMode.UsePreAuthoredTerrain,
                    new PreAuthoredTerrainSurfaceProvider(new[] { terrain }, null, null, false, 0f, 8f, 6f, 10f, 28f),
                    "immutability-test",
                    false);
                controller.GenerateTownShell();
                Assert.NotNull(controller.Grid);
                Assert.AreSame(authoredDressing, visualRoot.transform.Find("Hand Authored Dressing")?.gameObject);

                WorldSaveDto saved = controller.CaptureSaveDto();
                List<string> roadsBefore = CaptureRoads(saved);
                Assert.IsTrue(controller.LoadFromSaveDto(saved, new SaveReferenceResolver(controller), out string loadMessage), loadMessage);
                CollectionAssert.AreEqual(roadsBefore, CaptureRoads(controller.CaptureSaveDto()));
                Assert.AreSame(authoredDressing, visualRoot.transform.Find("Hand Authored Dressing")?.gameObject);

                controller.ClearGeneratedTown();
                Assert.AreSame(authoredDressing, visualRoot.transform.Find("Hand Authored Dressing")?.gameObject);
                Assert.AreSame(terrainData, terrain.terrainData);
                Assert.That(terrain.transform.position, Is.EqualTo(terrainPosition));
                Assert.That(terrainData.size, Is.EqualTo(terrainSize));
                Assert.That(terrainData.heightmapResolution, Is.EqualTo(heightmapResolution));
                CollectionAssert.AreEqual(beforeLayers, terrainData.terrainLayers);
                CollectionAssert.AreEqual(beforeHeights, terrainData.GetHeights(0, 0, heightmapResolution, heightmapResolution));
                Assert.That(Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include).Length, Is.EqualTo(terrainCount));
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void TerrainProfileMismatchFailsBeforeExistingWorldIsCleared()
        {
            List<Object> cleanup = new();
            try
            {
                TownGenerationSettings sourceSettings = CreateSettings();
                TownGenerationSettings targetSettings = CreateSettings();
                cleanup.Add(sourceSettings);
                cleanup.Add(targetSettings);
                TownWorldController source = CreateFlatController("Source Terrain", sourceSettings, "profile-a", cleanup);
                TownWorldController target = CreateFlatController("Target Terrain", targetSettings, "profile-b", cleanup);
                source.GenerateTownShell();
                target.GenerateTownShell();
                TownGrid originalTargetGrid = target.Grid;

                Assert.IsFalse(target.LoadFromSaveDto(source.CaptureSaveDto(), new SaveReferenceResolver(source), out string message));
                StringAssert.Contains("does not match", message);
                Assert.AreSame(originalTargetGrid, target.Grid, "Compatibility failure must happen before destructive reconstruction.");
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        private static TownWorldController CreateFlatController(string name, TownGenerationSettings settings, string profileId, List<Object> cleanup)
        {
            GameObject root = new(name);
            cleanup.Add(root);
            TownWorldController controller = root.AddComponent<TownWorldController>();
            controller.Configure(settings, null, null);
            controller.ConfigureStartup(
                WorldStartupMode.UsePreAuthoredTerrain,
                new RuleSurfaceProvider(new Bounds(Vector3.zero, new Vector3(100f, 20f, 100f))),
                profileId,
                false);
            return controller;
        }

        private static TownGenerationSettings CreateSettings()
        {
            TownGenerationSettings settings = ScriptableObject.CreateInstance<TownGenerationSettings>();
            settings.seed = 1886;
            settings.gridWidthCells = 12;
            settings.gridDepthCells = 12;
            settings.cellSizeMeters = 2f;
            settings.roadWidthCells = 1;
            settings.mainStreetLengthCells = 8;
            settings.crossStreetCount = 1;
            settings.crossStreetLengthCells = 8;
            settings.generateRegionalFoundation = false;
            settings.generateAgriculturalParcels = false;
            settings.generateSawmillProperty = false;
            settings.generateMineralDistricts = false;
            settings.generateTownHall = false;
            settings.generateDetailProps = false;
            settings.generateForestEnvironmentDressing = false;
            settings.showTerrainOverlay = false;
            settings.showRoads = true;
            settings.showPlots = false;
            settings.showBuildings = false;
            settings.showAnchors = false;
            settings.Sanitize();
            return settings;
        }

        private static Terrain CreateFlatTerrain(
            string name,
            Vector3 position,
            Vector3 size,
            float normalizedHeight,
            List<Object> cleanup)
        {
            TerrainData data = new()
            {
                heightmapResolution = 33,
                size = size
            };
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
            cleanup.Add(terrainObject);
            cleanup.Add(data);
            return terrainObject.GetComponent<Terrain>();
        }

        private static List<string> CaptureRoads(WorldSaveDto dto)
        {
            List<string> roads = new();
            if (dto?.roadCells == null)
            {
                return roads;
            }

            for (int i = 0; i < dto.roadCells.Count; i++)
            {
                RoadCellSaveDto road = dto.roadCells[i];
                roads.Add($"{road.x},{road.z},{road.roadType}");
            }

            return roads;
        }

        private static void SetPrivateField<T>(Object target, string fieldName, T value)
        {
            target.GetType().GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(target, value);
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

        private sealed class FailingSurfaceProvider : IWorldSurfaceProvider
        {
            public Bounds WorldBounds => new(Vector3.zero, Vector3.one * 10f);
            public bool TrySampleHeight(Vector3 worldPosition, out float height) { height = 0f; return false; }
            public bool TrySampleSurface(Vector3 worldPosition, out WorldSurfaceSample sample) { sample = default; return false; }
            public bool IsInsideWorld(Vector3 worldPosition) => false;
            public bool IsBuildable(Vector3 worldPosition, PlacementQuery query) => TrySampleSurface(worldPosition, out _);
            public bool IsRoadCompatible(Vector3 worldPosition, RoadQuery query) => TrySampleSurface(worldPosition, out _);
            public bool IsResourceCompatible(Vector3 worldPosition, ResourcePlacementQuery query) => TrySampleSurface(worldPosition, out _);
        }

        private sealed class RuleSurfaceProvider : IWorldSurfaceProvider
        {
            public RuleSurfaceProvider(Bounds bounds) { WorldBounds = bounds; }
            public Bounds WorldBounds { get; }
            public bool Water { get; set; }
            public float Slope { get; set; }
            public bool TrySampleHeight(Vector3 worldPosition, out float height) { height = 0f; return IsInsideWorld(worldPosition); }
            public bool TrySampleSurface(Vector3 worldPosition, out WorldSurfaceSample sample)
            {
                bool inside = IsInsideWorld(worldPosition);
                sample = new WorldSurfaceSample
                {
                    position = new Vector3(worldPosition.x, 0f, worldPosition.z),
                    height = 0f,
                    normal = Vector3.up,
                    slopeDegrees = Slope,
                    isInsideWorld = inside,
                    isWater = Water,
                    isBlocked = Water,
                    isBuildable = inside && !Water && Slope <= 8f
                };
                return inside;
            }
            public bool IsInsideWorld(Vector3 p) => p.x >= WorldBounds.min.x && p.x <= WorldBounds.max.x && p.z >= WorldBounds.min.z && p.z <= WorldBounds.max.z;
            public bool IsBuildable(Vector3 p, PlacementQuery q) => TrySampleSurface(p, out WorldSurfaceSample s) && s.isBuildable && s.slopeDegrees <= q.maximumSlopeDegrees;
            public bool IsRoadCompatible(Vector3 p, RoadQuery q) => TrySampleSurface(p, out WorldSurfaceSample s) && !s.isWater && !s.isBlocked && s.slopeDegrees <= q.maximumSlopeDegrees;
            public bool IsResourceCompatible(Vector3 p, ResourcePlacementQuery q) => TrySampleSurface(p, out WorldSurfaceSample s) && !s.isWater && !s.isBlocked && s.slopeDegrees <= q.maximumSlopeDegrees;
        }
    }
}
