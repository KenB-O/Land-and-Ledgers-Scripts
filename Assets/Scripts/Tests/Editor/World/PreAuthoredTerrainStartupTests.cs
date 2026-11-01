using System.Collections;
using System.Collections.Generic;
using LandLedgers.FirstLedger;
using LandLedgers.Persistence;
using LandLedgers.World;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace LandLedgers.Editor.World
{
    public sealed class PreAuthoredTerrainStartupTests
    {
        [Test]
        public void StartupRoutingOnlyRunsProceduralTerrainForProceduralMode()
        {
            Assert.IsFalse(WorldStartupRouting.ShouldGenerateProceduralTerrain(WorldStartupMode.UsePreAuthoredTerrain));
            Assert.IsTrue(WorldStartupRouting.ShouldGenerateProceduralTerrain(WorldStartupMode.GenerateProceduralTerrain));
        }

        [Test]
        public void TownSitingIsStableForSameSeedAndCanMoveForDifferentSeed()
        {
            FlatSurfaceProvider provider = new(new Bounds(Vector3.zero, new Vector3(900f, 40f, 900f)));
            TownSiteSelectionRequest request = new()
            {
                townSizeMeters = new Vector2(300f, 280f),
                minimumEdgeDistanceMeters = 24f,
                maximumTownSlopeDegrees = 8f,
                candidateCount = 96,
                validationGridResolution = 5
            };

            Assert.IsTrue(TownSiteSelector.TrySelect(provider, request, 1886, false, out TownSiteSelectionResult first));
            Assert.IsTrue(TownSiteSelector.TrySelect(provider, request, 1886, false, out TownSiteSelectionResult second));
            Assert.IsTrue(TownSiteSelector.TrySelect(provider, request, 1901, false, out TownSiteSelectionResult different));

            Assert.That(second.Position, Is.EqualTo(first.Position));
            Assert.That(different.Position, Is.Not.EqualTo(first.Position));
            Assert.IsTrue(provider.IsInsideWorld(first.Position));
            Assert.IsTrue(provider.IsInsideWorld(different.Position));
        }

        [Test]
        public void SavedTerrainMetadataAndResourcesRestoreWithoutReroll()
        {
            List<Object> cleanup = new();
            try
            {
                TownGenerationSettings settings = CreateSettings(1886);
                cleanup.Add(settings);
                TownWorldController source = CreateController("Pre-authored Save Source", settings, cleanup);
                source.ConfigureStartup(
                    WorldStartupMode.UsePreAuthoredTerrain,
                    new FlatSurfaceProvider(new Bounds(Vector3.zero, new Vector3(600f, 20f, 600f))),
                    "main-authored-terrain-v1",
                    false);
                source.GenerateTownShell();

                WorldSaveDto saved = source.CaptureSaveDto();
                List<string> expectedResources = CaptureResources(source.RegionalResources);
                Vector3 expectedTownAnchor = source.OpeningTownAnchor;

                TownGenerationSettings loadedSettings = CreateSettings(9999);
                cleanup.Add(loadedSettings);
                TownWorldController target = CreateController("Pre-authored Save Target", loadedSettings, cleanup);
                target.ConfigureStartup(
                    WorldStartupMode.UsePreAuthoredTerrain,
                    new FlatSurfaceProvider(new Bounds(Vector3.zero, new Vector3(600f, 20f, 600f))),
                    "main-authored-terrain-v1",
                    false);

                Assert.IsTrue(target.LoadFromSaveDto(saved, new SaveReferenceResolver(source), out string message), message);
                Assert.That(target.OpeningTownAnchor, Is.EqualTo(expectedTownAnchor));
                Assert.That(target.CurrentStartupMode, Is.EqualTo(WorldStartupMode.UsePreAuthoredTerrain));
                Assert.That(target.TerrainProfileId, Is.EqualTo("main-authored-terrain-v1"));
                CollectionAssert.AreEqual(expectedResources, CaptureResources(target.RegionalResources));
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void MainSceneDefaultsToAssignedPreAuthoredTerrain()
        {
            Scene scene = EditorSceneManager.OpenScene("Assets/Main Scene.unity", OpenSceneMode.Single);
            Assert.IsTrue(scene.IsValid());

            FirstLedgerSliceBootstrapper bootstrapper = Object.FindAnyObjectByType<FirstLedgerSliceBootstrapper>();
            PreAuthoredTerrainWorldProfile profile = Object.FindAnyObjectByType<PreAuthoredTerrainWorldProfile>();
            Terrain terrain = Object.FindAnyObjectByType<Terrain>();
            SerializedObject serializedBootstrapper = new(bootstrapper);
            SerializedObject serializedProfile = new(profile);
            TownWorldController townWorld = Object.FindAnyObjectByType<TownWorldController>();
            RegionalTerrainTileView terrainView = Object.FindAnyObjectByType<RegionalTerrainTileView>(FindObjectsInactive.Include);

            Assert.NotNull(bootstrapper);
            Assert.NotNull(profile);
            Assert.NotNull(terrain);
            Assert.That(
                (WorldStartupMode)serializedBootstrapper.FindProperty("startupMode").enumValueIndex,
                Is.EqualTo(WorldStartupMode.UsePreAuthoredTerrain));
            Assert.That(
                (WorldStartupIntent)serializedBootstrapper.FindProperty("startupIntent").enumValueIndex,
                Is.EqualTo(WorldStartupIntent.NewGame));
            Assert.AreSame(profile, serializedBootstrapper.FindProperty("preAuthoredTerrainProfile").objectReferenceValue);
            Assert.IsTrue(serializedProfile.FindProperty("useFixedTownAnchor").boolValue);
            Transform fixedAnchor = serializedProfile.FindProperty("fixedTownAnchor").objectReferenceValue as Transform;
            Assert.NotNull(fixedAnchor);
            Assert.That(fixedAnchor.name, Is.EqualTo("Authored Opening Town Anchor"));
            Assert.That(profile.TerrainProfileId, Is.EqualTo("main-authored-terrain-v1"));
            Assert.That(profile.TerrainContentRevision, Is.GreaterThanOrEqualTo(1));
            Assert.IsTrue(profile.TryCreateProvider(out IWorldSurfaceProvider provider, out string error), error);
            Assert.IsTrue(provider.IsInsideWorld(terrain.transform.position + terrain.terrainData.size * 0.5f));
            Assert.NotNull(terrain.GetComponent<TerrainCollider>());
            Assert.NotNull(townWorld);
            Assert.NotNull(townWorld.transform.Find("WorldVisualRoot"));
            Assert.NotNull(GameObject.Find("AuthoredWorldContent"));
            Assert.NotNull(GameObject.Find("Authored Placement Masks"));
            Assert.IsFalse(new SerializedObject(townWorld).FindProperty("generateOnStart").boolValue);
            Assert.IsFalse(new SerializedObject(townWorld).FindProperty("refreshRegionalTerrainTilesDuringRuntimeStartup").boolValue);
            Assert.IsTrue(terrainView == null || !new SerializedObject(terrainView).FindProperty("buildOnStart").boolValue);
        }

        [UnityTest]
        public IEnumerator MainScenePlayModeUsesAuthoredTerrainAndGeneratesOneValidTown()
        {
            EditorSceneManager.OpenScene("Assets/Main Scene.unity", OpenSceneMode.Single);
            Terrain[] editorTerrains = Object.FindObjectsByType<Terrain>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            Assert.That(editorTerrains, Has.Length.EqualTo(1), "Main Scene should contain exactly one authored Terrain.");
            TerrainData expectedTerrainData = editorTerrains[0].terrainData;
            Assert.NotNull(expectedTerrainData);
            SessionState.SetInt("LandLedgers.PreAuthoredProbe.TerrainCount", editorTerrains.Length);
            SessionState.SetString(
                "LandLedgers.PreAuthoredProbe.TerrainGuid",
                AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(expectedTerrainData)));
            SessionState.SetFloat("LandLedgers.PreAuthoredProbe.SizeX", expectedTerrainData.size.x);
            SessionState.SetFloat("LandLedgers.PreAuthoredProbe.SizeY", expectedTerrainData.size.y);
            SessionState.SetFloat("LandLedgers.PreAuthoredProbe.SizeZ", expectedTerrainData.size.z);
            SessionState.SetInt("LandLedgers.PreAuthoredProbe.HeightmapResolution", expectedTerrainData.heightmapResolution);
            SessionState.SetInt("LandLedgers.PreAuthoredProbe.TerrainLayerCount", expectedTerrainData.terrainLayers.Length);
            SessionState.SetString("LandLedgers.PreAuthoredProbe.TerrainLayerGuids", CaptureTerrainLayerGuids(expectedTerrainData));
            SessionState.SetFloat("LandLedgers.PreAuthoredProbe.Height00", expectedTerrainData.GetInterpolatedHeight(0f, 0f));
            SessionState.SetFloat("LandLedgers.PreAuthoredProbe.Height25", expectedTerrainData.GetInterpolatedHeight(0.25f, 0.25f));
            SessionState.SetFloat("LandLedgers.PreAuthoredProbe.Height50", expectedTerrainData.GetInterpolatedHeight(0.5f, 0.5f));
            SessionState.SetFloat("LandLedgers.PreAuthoredProbe.Height75", expectedTerrainData.GetInterpolatedHeight(0.75f, 0.75f));
            SessionState.SetFloat("LandLedgers.PreAuthoredProbe.Height100", expectedTerrainData.GetInterpolatedHeight(1f, 1f));
            double startedAt = EditorApplication.timeSinceStartup;

            yield return new EnterPlayMode();

            TownWorldController townWorld = null;
            for (int frame = 0; frame < 600; frame++)
            {
                townWorld = Object.FindAnyObjectByType<TownWorldController>();
                if (townWorld != null && townWorld.Grid != null)
                {
                    break;
                }

                yield return null;
            }

            long elapsedMilliseconds = (long)((EditorApplication.timeSinceStartup - startedAt) * 1000d);
            Assert.NotNull(townWorld, "Main Scene has no TownWorldController in Play Mode.");
            Assert.NotNull(townWorld.Grid, "Main Scene did not reach a usable generated world.");
            Assert.That(townWorld.CurrentStartupMode, Is.EqualTo(WorldStartupMode.UsePreAuthoredTerrain));
            Assert.That(townWorld.GenerationPassCount, Is.EqualTo(1), "Fresh startup generated the opening town more than once.");

            Terrain[] runtimeTerrains = Object.FindObjectsByType<Terrain>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            int expectedTerrainCount = SessionState.GetInt("LandLedgers.PreAuthoredProbe.TerrainCount", -1);
            string expectedTerrainGuid = SessionState.GetString("LandLedgers.PreAuthoredProbe.TerrainGuid", string.Empty);
            Vector3 expectedTerrainSize = new(
                SessionState.GetFloat("LandLedgers.PreAuthoredProbe.SizeX", float.NaN),
                SessionState.GetFloat("LandLedgers.PreAuthoredProbe.SizeY", float.NaN),
                SessionState.GetFloat("LandLedgers.PreAuthoredProbe.SizeZ", float.NaN));
            int expectedHeightmapResolution = SessionState.GetInt(
                "LandLedgers.PreAuthoredProbe.HeightmapResolution",
                -1);
            int expectedTerrainLayerCount = SessionState.GetInt("LandLedgers.PreAuthoredProbe.TerrainLayerCount", -1);
            string expectedTerrainLayerGuids = SessionState.GetString(
                "LandLedgers.PreAuthoredProbe.TerrainLayerGuids",
                string.Empty);
            Assert.That(runtimeTerrains, Has.Length.EqualTo(expectedTerrainCount),
                "Pre-authored startup created or deleted Terrain objects.");
            TerrainData actualTerrainData = runtimeTerrains[0].terrainData;
            Assert.That(
                AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(actualTerrainData)),
                Is.EqualTo(expectedTerrainGuid));
            Assert.That(actualTerrainData.size, Is.EqualTo(expectedTerrainSize));
            Assert.That(actualTerrainData.heightmapResolution, Is.EqualTo(expectedHeightmapResolution));
            Assert.That(actualTerrainData.terrainLayers, Has.Length.EqualTo(expectedTerrainLayerCount));
            Assert.That(CaptureTerrainLayerGuids(actualTerrainData), Is.EqualTo(expectedTerrainLayerGuids));
            Assert.That(actualTerrainData.GetInterpolatedHeight(0f, 0f),
                Is.EqualTo(SessionState.GetFloat("LandLedgers.PreAuthoredProbe.Height00", float.NaN)));
            Assert.That(actualTerrainData.GetInterpolatedHeight(0.25f, 0.25f),
                Is.EqualTo(SessionState.GetFloat("LandLedgers.PreAuthoredProbe.Height25", float.NaN)));
            Assert.That(actualTerrainData.GetInterpolatedHeight(0.5f, 0.5f),
                Is.EqualTo(SessionState.GetFloat("LandLedgers.PreAuthoredProbe.Height50", float.NaN)));
            Assert.That(actualTerrainData.GetInterpolatedHeight(0.75f, 0.75f),
                Is.EqualTo(SessionState.GetFloat("LandLedgers.PreAuthoredProbe.Height75", float.NaN)));
            Assert.That(actualTerrainData.GetInterpolatedHeight(1f, 1f),
                Is.EqualTo(SessionState.GetFloat("LandLedgers.PreAuthoredProbe.Height100", float.NaN)));
            float[] actualHeightSamples = CaptureRepresentativeHeights(actualTerrainData);

            RegionalTerrainTileView[] terrainViews = Object.FindObjectsByType<RegionalTerrainTileView>(
                FindObjectsInactive.Include);
            for (int i = 0; i < terrainViews.Length; i++)
            {
                Assert.That(terrainViews[i].LastBuiltTileCount, Is.EqualTo(0),
                    $"Regional terrain view '{terrainViews[i].name}' rebuilt terrain in pre-authored mode.");
            }

            PreAuthoredTerrainWorldProfile profile = Object.FindAnyObjectByType<PreAuthoredTerrainWorldProfile>();
            Assert.NotNull(profile);
            Assert.IsTrue(profile.TryCreateProvider(out IWorldSurfaceProvider provider, out string error), error);
            Assert.IsTrue(provider.IsBuildable(
                townWorld.OpeningTownAnchor,
                new PlacementQuery
                {
                    maximumSlopeDegrees = profile.DefaultMaxTownSlope,
                    forTown = true
                }));

            for (int i = 0; i < townWorld.Buildings.Count; i++)
            {
                Vector3 center = townWorld.GetWorldCenterForRect(townWorld.Buildings[i].footprint);
                Assert.IsTrue(provider.IsBuildable(
                    center,
                    new PlacementQuery
                    {
                        maximumSlopeDegrees = profile.DefaultMaxBuildingSlope,
                        forBuilding = true
                    }),
                    $"Building {townWorld.Buildings[i].id} is invalid at {center}.");
            }

            RegionalResourceSnapshot resources = townWorld.RegionalResources;
            for (int i = 0; i < resources.Districts.Count; i++)
            {
                Vector3 center = resources.Districts[i].Center;
                Assert.IsTrue(provider.IsResourceCompatible(
                    center,
                    new ResourcePlacementQuery
                    {
                        maximumSlopeDegrees = profile.DefaultMaxResourceSlope,
                        resourceKind = resources.Districts[i].Kind
                    }),
                    $"Resource district '{resources.Districts[i].Id}' is invalid at {center}.");
            }

            for (int i = 0; i < resources.RemoteSites.Count; i++)
            {
                Vector3 anchor = resources.RemoteSites[i].AnchorPosition;
                Assert.IsTrue(provider.IsResourceCompatible(
                    anchor,
                    new ResourcePlacementQuery
                    {
                        maximumSlopeDegrees = profile.DefaultMaxResourceSlope,
                        resourceKind = resources.RemoteSites[i].DominantResource
                    }),
                    $"Remote resource site '{resources.RemoteSites[i].Id}' is invalid at {anchor}.");
            }

            FirstLedgerSliceBootstrapper bootstrapper = Object.FindAnyObjectByType<FirstLedgerSliceBootstrapper>();
            Debug.Log($"[PreAuthoredStartupProbe] Main Scene Play Mode transition and startup completed in {elapsedMilliseconds} ms; "
                + $"town passes={townWorld.GenerationPassCount}, regional terrain tiles built=0, "
                + $"buildings={townWorld.Buildings.Count}, resource districts={resources.Districts.Count}, "
                + $"remote resource sites={resources.RemoteSites.Count}. "
                + $"Authored TerrainData: size={expectedTerrainSize}, heightmap resolution={expectedHeightmapResolution}, "
                + $"layers={expectedTerrainLayerCount}, representative local heights="
                + $"[{string.Join(", ", actualHeightSamples)}]. "
                + $"Bootstrap timing: {bootstrapper?.LastStartupTimingSummary}");

            // Existing HUD shutdown callbacks can log MissingReferenceException while the test runner destroys the scene.
            LogAssert.ignoreFailingMessages = true;
            yield return new ExitPlayMode();
            LogAssert.ignoreFailingMessages = false;
        }

        private static float[] CaptureRepresentativeHeights(TerrainData terrainData)
        {
            return new[]
            {
                terrainData.GetInterpolatedHeight(0f, 0f),
                terrainData.GetInterpolatedHeight(0.25f, 0.25f),
                terrainData.GetInterpolatedHeight(0.5f, 0.5f),
                terrainData.GetInterpolatedHeight(0.75f, 0.75f),
                terrainData.GetInterpolatedHeight(1f, 1f)
            };
        }

        private static string CaptureTerrainLayerGuids(TerrainData terrainData)
        {
            TerrainLayer[] layers = terrainData.terrainLayers;
            string[] guids = new string[layers.Length];
            for (int i = 0; i < layers.Length; i++)
            {
                guids[i] = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(layers[i]));
            }

            return string.Join(";", guids);
        }

        private static TownGenerationSettings CreateSettings(int seed)
        {
            TownGenerationSettings settings = ScriptableObject.CreateInstance<TownGenerationSettings>();
            settings.seed = seed;
            settings.gridWidthCells = 80;
            settings.gridDepthCells = 72;
            settings.cellSizeMeters = 2f;
            settings.mainStreetLengthCells = 48;
            settings.crossStreetCount = 1;
            settings.crossStreetLengthCells = 42;
            settings.generateRegionalFoundation = false;
            settings.generateAgriculturalParcels = false;
            settings.generateSawmillProperty = false;
            settings.generateTownHall = false;
            settings.generateDetailProps = false;
            settings.generateForestEnvironmentDressing = false;
            settings.showTerrainOverlay = false;
            settings.showRoads = false;
            settings.showPlots = false;
            settings.showBuildings = false;
            settings.Sanitize();
            return settings;
        }

        private static TownWorldController CreateController(string name, TownGenerationSettings settings, List<Object> cleanup)
        {
            GameObject root = new(name);
            cleanup.Add(root);
            TownWorldController controller = root.AddComponent<TownWorldController>();
            controller.Configure(settings, null, null);
            return controller;
        }

        private static List<string> CaptureResources(RegionalResourceSnapshot snapshot)
        {
            List<string> result = new();
            if (snapshot == null)
            {
                return result;
            }

            for (int i = 0; i < snapshot.Districts.Count; i++)
            {
                MineralDistrictRecord district = snapshot.Districts[i];
                result.Add($"D|{district.Id}|{district.Kind}|{district.Center.x:F3}|{district.Center.y:F3}|{district.Center.z:F3}");
            }

            for (int i = 0; i < snapshot.RemoteSites.Count; i++)
            {
                RemoteIndustrySiteRecord site = snapshot.RemoteSites[i];
                result.Add($"S|{site.Id}|{site.DominantResource}|{site.AnchorPosition.x:F3}|{site.AnchorPosition.y:F3}|{site.AnchorPosition.z:F3}");
            }

            return result;
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

        private sealed class FlatSurfaceProvider : IWorldSurfaceProvider
        {
            public FlatSurfaceProvider(Bounds bounds)
            {
                WorldBounds = bounds;
            }

            public Bounds WorldBounds { get; }

            public bool TrySampleHeight(Vector3 worldPosition, out float height)
            {
                height = 0f;
                return IsInsideWorld(worldPosition);
            }

            public bool TrySampleSurface(Vector3 worldPosition, out WorldSurfaceSample sample)
            {
                bool inside = IsInsideWorld(worldPosition);
                sample = new WorldSurfaceSample
                {
                    position = new Vector3(worldPosition.x, 0f, worldPosition.z),
                    height = 0f,
                    normal = Vector3.up,
                    slopeDegrees = 0f,
                    isInsideWorld = inside,
                    isBuildable = inside
                };
                return inside;
            }

            public bool IsInsideWorld(Vector3 worldPosition)
            {
                return worldPosition.x >= WorldBounds.min.x
                    && worldPosition.x <= WorldBounds.max.x
                    && worldPosition.z >= WorldBounds.min.z
                    && worldPosition.z <= WorldBounds.max.z;
            }

            public bool IsBuildable(Vector3 worldPosition, PlacementQuery query)
            {
                return IsInsideWorld(worldPosition);
            }

            public bool IsRoadCompatible(Vector3 worldPosition, RoadQuery query)
            {
                return IsInsideWorld(worldPosition);
            }

            public bool IsResourceCompatible(Vector3 worldPosition, ResourcePlacementQuery query)
            {
                return IsInsideWorld(worldPosition);
            }
        }
    }
}
