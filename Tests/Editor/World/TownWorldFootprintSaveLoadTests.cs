using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Pathing;
using LandLedgers.Persistence;
using LandLedgers.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LandLedgers.Editor.World
{
    public sealed class TownWorldFootprintSaveLoadTests
    {
        [Test]
        public void BuildingDefinitionUsesPrefabFootprintAuthorityWhenPresent()
        {
            List<Object> cleanup = new();
            try
            {
                GameObject prefab = new("Footprint Authority Prefab");
                cleanup.Add(prefab);
                BuildingFootprintAuthority authority = prefab.AddComponent<BuildingFootprintAuthority>();
                SerializedObject authoritySerialized = new(authority);
                authoritySerialized.FindProperty("minimumFootprintWidthCells").intValue = 9;
                authoritySerialized.FindProperty("minimumFootprintDepthCells").intValue = 7;
                authoritySerialized.ApplyModifiedPropertiesWithoutUndo();

                BuildingDefinition definition = ScriptableObject.CreateInstance<BuildingDefinition>();
                cleanup.Add(definition);
                definition.ConfigureRuntimeFallback(
                    "prefab_authority_test_building",
                    "Prefab Authority Test Building",
                    PlotZone.Business,
                    new Vector2Int(3, 2),
                    Color.white,
                    5f);
                definition.ConfigureVisual(prefab, 1f, 0f, Vector3.zero, false, 1f);

                Assert.AreEqual(new Vector2Int(9, 7), definition.FootprintSizeCells);
                Assert.AreEqual(new Vector2Int(9, 7), definition.MinimumFootprintSizeCells);
                Assert.AreEqual(new Vector2Int(3, 2), definition.LegacyFootprintSizeCells);
                Assert.IsTrue(definition.HasPrefabFootprintAuthority);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void BuildingDefinitionUsesPrefabRendererBoundsWhenAuthorityMinimumIsUndersized()
        {
            List<Object> cleanup = new();
            try
            {
                GameObject prefab = CreateSizedPrefab("Renderer Sized Footprint Authority Prefab", new Vector3(9f, 1f, 7f));
                cleanup.Add(prefab);
                prefab.AddComponent<BuildingFootprintAuthority>();

                BuildingDefinition definition = ScriptableObject.CreateInstance<BuildingDefinition>();
                cleanup.Add(definition);
                definition.ConfigureRuntimeFallback(
                    "renderer_authority_test_building",
                    "Renderer Authority Test Building",
                    PlotZone.Business,
                    new Vector2Int(2, 2),
                    Color.white,
                    5f);
                definition.ConfigureVisual(prefab, 1f, 0f, Vector3.zero, false, 1f);

                Assert.AreEqual(new Vector2Int(5, 4), definition.FootprintSizeCells);
                Assert.AreEqual(new Vector2Int(2, 2), definition.LegacyFootprintSizeCells);
                Assert.IsTrue(definition.HasPrefabFootprintAuthority);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void BuildingDefinitionFallsBackToLegacyFootprintWithoutPrefabAuthority()
        {
            List<Object> cleanup = new();
            try
            {
                GameObject prefab = new("No Footprint Authority Prefab");
                cleanup.Add(prefab);

                BuildingDefinition definition = ScriptableObject.CreateInstance<BuildingDefinition>();
                cleanup.Add(definition);
                definition.ConfigureRuntimeFallback(
                    "legacy_footprint_test_building",
                    "Legacy Footprint Test Building",
                    PlotZone.Business,
                    new Vector2Int(5, 4),
                    Color.white,
                    5f);
                definition.ConfigureVisual(prefab, 1f, 0f, Vector3.zero, false, 1f);

                Assert.AreEqual(new Vector2Int(5, 4), definition.FootprintSizeCells);
                Assert.AreEqual(new Vector2Int(5, 4), definition.MinimumFootprintSizeCells);
                Assert.AreEqual(new Vector2Int(5, 4), definition.LegacyFootprintSizeCells);
                Assert.IsFalse(definition.HasPrefabFootprintAuthority);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void EastWestFrontageUsesPrefabWidthAsFrontageSpan()
        {
            List<Object> cleanup = new();
            try
            {
                GameObject prefab = new("Oriented Footprint Authority Prefab");
                cleanup.Add(prefab);
                BuildingFootprintAuthority authority = prefab.AddComponent<BuildingFootprintAuthority>();
                SerializedObject authoritySerialized = new(authority);
                authoritySerialized.FindProperty("minimumFootprintWidthCells").intValue = 7;
                authoritySerialized.FindProperty("minimumFootprintDepthCells").intValue = 4;
                authoritySerialized.ApplyModifiedPropertiesWithoutUndo();

                BuildingDefinition definition = ScriptableObject.CreateInstance<BuildingDefinition>();
                cleanup.Add(definition);
                definition.ConfigureRuntimeFallback(
                    "oriented_authority_test_building",
                    "Oriented Authority Test Building",
                    PlotZone.Business,
                    new Vector2Int(2, 2),
                    Color.white,
                    5f);
                definition.ConfigureVisual(prefab, 1f, 0f, Vector3.zero, false, 1f);

                TownGenerationSettings settings = CreateSettings(definition);
                cleanup.Add(settings);
                settings.gridWidthCells = 40;
                settings.gridDepthCells = 40;
                settings.mainStreetLengthCells = 16;
                settings.minPlotFrontageCells = 8;
                settings.maxPlotFrontageCells = 8;
                settings.plotDepthCells = 8;
                settings.Sanitize();

                GameObject townObject = new("Oriented Footprint Test Town");
                cleanup.Add(townObject);
                TownWorldController townWorld = townObject.AddComponent<TownWorldController>();
                townWorld.Configure(settings, null, null);

                townWorld.GenerateTownShell();

                PlacedBuilding eastFacing = null;
                for (int i = 0; i < townWorld.Buildings.Count; i++)
                {
                    if (townWorld.Buildings[i].frontageDirection == GridDirection.East)
                    {
                        eastFacing = townWorld.Buildings[i];
                        break;
                    }
                }

                Assert.IsNotNull(eastFacing);
                Assert.AreEqual(new Vector2Int(7, 4), eastFacing.intendedFootprintSizeCells);
                Assert.AreEqual(4, eastFacing.footprint.width);
                Assert.AreEqual(7, eastFacing.footprint.depth);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void AuthoredBuildingAnchorsInsideFootprintRemainValidPathingTargets()
        {
            List<Object> cleanup = new();
            try
            {
                GameObject prefab = new("Interior Anchor Test Prefab");
                cleanup.Add(prefab);
                GameObject frontDoorMarker = new("Anchor_FrontDoor");
                cleanup.Add(frontDoorMarker);
                frontDoorMarker.transform.SetParent(prefab.transform, false);
                frontDoorMarker.transform.localPosition = Vector3.zero;
                frontDoorMarker.AddComponent<BuildingAnchorMarker>();

                BuildingDefinition definition = ScriptableObject.CreateInstance<BuildingDefinition>();
                cleanup.Add(definition);
                definition.ConfigureRuntimeFallback(
                    "interior_anchor_test_building",
                    "Interior Anchor Test Building",
                    PlotZone.Business,
                    new Vector2Int(4, 4),
                    Color.white,
                    5f);
                definition.ConfigureVisual(prefab, 1f, 0f, Vector3.zero, false, 1f);

                TownGenerationSettings settings = CreateSettings(definition);
                cleanup.Add(settings);

                GameObject townObject = new("Interior Anchor Test Town");
                cleanup.Add(townObject);
                TownWorldController townWorld = townObject.AddComponent<TownWorldController>();
                townWorld.Configure(settings, null, null);
                townWorld.GenerateTownShell();

                Assert.AreEqual(1, townWorld.Buildings.Count);
                PlacedBuilding building = townWorld.Buildings[0];
                Assert.IsTrue(building.TryGetAnchor(AnchorType.FrontDoor, out BuildingAnchor frontDoor));
                Assert.IsTrue(building.footprint.Contains(frontDoor.coord), $"Expected front-door anchor {frontDoor.coord} inside footprint {building.footprint}.");

                for (int i = 0; i < building.anchorIssues.Count; i++)
                {
                    Assert.IsFalse(building.anchorIssues[i].isError, building.anchorIssues[i].message);
                    StringAssert.DoesNotContain("inside the building footprint", building.anchorIssues[i].message);
                }

                TownCell anchorCell = townWorld.Grid.GetCell(frontDoor.coord);
                Assert.IsTrue(anchorCell.HasBuilding);
                Assert.AreEqual(building.id, anchorCell.buildingId);
                Assert.AreNotEqual(0, anchorCell.occupancy & CellOccupancy.Anchor);

                GameObject pathingObject = new("Interior Anchor Pathing Test");
                cleanup.Add(pathingObject);
                PathingSettings pathingSettings = ScriptableObject.CreateInstance<PathingSettings>();
                cleanup.Add(pathingSettings);
                pathingSettings.maxVisitedCells = 20000;
                PathingManager pathing = pathingObject.AddComponent<PathingManager>();
                pathing.Configure(townWorld, pathingSettings);

                TownPlot plot = townWorld.Plots[building.plotId];
                Assert.IsTrue(pathing.TryFindPath(plot.roadAccessCell, frontDoor.coord, out PathingResult result), result.FailureReason);
                Assert.AreEqual(frontDoor.coord, result.Path[^1]);
                Assert.IsTrue(result.Path.Exists(coord => building.footprint.Contains(coord)));
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void PrefabRoadAccessAuthorityPinsVisualAndAuthoredAnchorToFrontageTarget()
        {
            List<Object> cleanup = new();
            try
            {
                GameObject prefab = CreateSizedPrefab("Road Access Anchor Test Prefab", new Vector3(3f, 1f, 3f));
                cleanup.Add(prefab);
                BuildingFootprintAuthority authority = prefab.AddComponent<BuildingFootprintAuthority>();
                SerializedObject authoritySerialized = new(authority);
                authoritySerialized.FindProperty("frontDoorLocalPoint").vector3Value = new Vector3(0f, 0f, 2.35f);
                authoritySerialized.FindProperty("useFrontPointAsRoadAccessAnchor").boolValue = true;
                authoritySerialized.ApplyModifiedPropertiesWithoutUndo();

                BuildingDefinition definition = ScriptableObject.CreateInstance<BuildingDefinition>();
                cleanup.Add(definition);
                definition.ConfigureRuntimeFallback(
                    "road_access_anchor_test_building",
                    "Road Access Anchor Test Building",
                    PlotZone.Business,
                    new Vector2Int(4, 4),
                    Color.white,
                    5f);
                definition.ConfigureVisual(prefab, 1f, 0f, new Vector3(3f, 0f, 0.75f), false, 1f);

                TownGenerationSettings settings = CreateSettings(definition);
                cleanup.Add(settings);

                GameObject townObject = new("Road Access Anchor Test Town");
                cleanup.Add(townObject);
                TownWorldController townWorld = townObject.AddComponent<TownWorldController>();
                townWorld.Configure(settings, null, null);
                townWorld.GenerateTownShell();

                Assert.AreEqual(1, townWorld.Buildings.Count);
                PlacedBuilding building = townWorld.Buildings[0];
                Assert.IsTrue(townWorld.TryGetPlotById(building.plotId, out TownPlot plot));
                Assert.IsTrue(building.TryGetAnchor(AnchorType.FrontDoor, out BuildingAnchor frontDoor));
                Assert.IsTrue(frontDoor.hasAuthoredWorldPosition);

                Vector3 expectedTarget = ResolveExpectedFrontageTarget(
                    townWorld.Grid,
                    building.footprint,
                    building.frontageDirection,
                    plot.roadAccessCell,
                    settings.worldCenter.y);

                Transform visual = townObject.transform.Find("WorldVisualRoot/Building 000 Road Access Anchor Test Building");
                Assert.IsNotNull(visual);
                BuildingFootprintAuthority spawnedAuthority = visual.GetComponent<BuildingFootprintAuthority>();
                Assert.IsNotNull(spawnedAuthority);

                Vector3 actualRoadAccess = spawnedAuthority.ResolveRoadAccessWorldPoint(
                    visual.position,
                    visual.rotation,
                    visual.localScale);

                Assert.AreEqual(expectedTarget.x, actualRoadAccess.x, 0.01f);
                Assert.AreEqual(expectedTarget.z, actualRoadAccess.z, 0.01f);
                Assert.AreEqual(expectedTarget.x, frontDoor.authoredWorldPosition.x, 0.01f);
                Assert.AreEqual(expectedTarget.z, frontDoor.authoredWorldPosition.z, 0.01f);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void BuildingInspectionReportsRoadAccessAnchoredPrefabPlacement()
        {
            List<Object> cleanup = new();
            try
            {
                GameObject prefab = CreateSizedPrefab("Road Access Inspection Test Prefab", new Vector3(3f, 1f, 3f));
                cleanup.Add(prefab);
                BuildingFootprintAuthority authority = prefab.AddComponent<BuildingFootprintAuthority>();
                SerializedObject authoritySerialized = new(authority);
                authoritySerialized.FindProperty("frontDoorLocalPoint").vector3Value = new Vector3(0f, 0f, 2.35f);
                authoritySerialized.FindProperty("useFrontPointAsRoadAccessAnchor").boolValue = true;
                authoritySerialized.ApplyModifiedPropertiesWithoutUndo();

                BuildingDefinition definition = ScriptableObject.CreateInstance<BuildingDefinition>();
                cleanup.Add(definition);
                definition.ConfigureRuntimeFallback(
                    "road_access_inspection_test_building",
                    "Road Access Inspection Test Building",
                    PlotZone.Business,
                    new Vector2Int(4, 4),
                    Color.white,
                    5f);
                definition.ConfigureVisual(prefab, 1f, 0f, new Vector3(3f, 0f, 0.75f), false, 1f);

                TownGenerationSettings settings = CreateSettings(definition);
                cleanup.Add(settings);

                GameObject townObject = new("Road Access Inspection Test Town");
                cleanup.Add(townObject);
                TownWorldController townWorld = townObject.AddComponent<TownWorldController>();
                townWorld.Configure(settings, null, null);
                townWorld.GenerateTownShell();

                string inspection = townWorld.BuildBuildingInspectionSummary(0);
                StringAssert.Contains("Prefab placement: road-access anchored", inspection);
                StringAssert.Contains("target road access", inspection);
                StringAssert.Contains("resolved root", inspection);
                StringAssert.Contains("authored road-access point", inspection);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void LegacyPrefabPlacementKeepsCenteredVisualOffsetWhenRoadAccessAnchorIsDisabled()
        {
            List<Object> cleanup = new();
            try
            {
                GameObject prefab = CreateSizedPrefab("Centered Placement Test Prefab", new Vector3(3f, 1f, 3f));
                cleanup.Add(prefab);
                BuildingFootprintAuthority authority = prefab.AddComponent<BuildingFootprintAuthority>();
                SerializedObject authoritySerialized = new(authority);
                authoritySerialized.FindProperty("frontDoorLocalPoint").vector3Value = new Vector3(0f, 0f, 2.35f);
                authoritySerialized.FindProperty("useFrontPointAsRoadAccessAnchor").boolValue = false;
                authoritySerialized.ApplyModifiedPropertiesWithoutUndo();

                BuildingDefinition definition = ScriptableObject.CreateInstance<BuildingDefinition>();
                cleanup.Add(definition);
                definition.ConfigureRuntimeFallback(
                    "centered_placement_test_building",
                    "Centered Placement Test Building",
                    PlotZone.Business,
                    new Vector2Int(4, 4),
                    Color.white,
                    5f);
                Vector3 visualOffset = new(3f, 0f, 0.75f);
                definition.ConfigureVisual(prefab, 1f, 0f, visualOffset, false, 1f);

                TownGenerationSettings settings = CreateSettings(definition);
                cleanup.Add(settings);

                GameObject townObject = new("Centered Placement Test Town");
                cleanup.Add(townObject);
                TownWorldController townWorld = townObject.AddComponent<TownWorldController>();
                townWorld.Configure(settings, null, null);
                townWorld.GenerateTownShell();

                Assert.AreEqual(1, townWorld.Buildings.Count);
                PlacedBuilding building = townWorld.Buildings[0];
                Transform visual = townObject.transform.Find("WorldVisualRoot/Building 000 Centered Placement Test Building");
                Assert.IsNotNull(visual);

                Quaternion rotation = Quaternion.Euler(0f, ResolveExpectedFrontageYaw(building.frontageDirection), 0f);
                Vector3 expectedRoot = ResolveExpectedFootprintCenter(townWorld.Grid, building.footprint, settings.worldCenter.y)
                    + rotation * visualOffset;

                Assert.AreEqual(expectedRoot.x, visual.position.x, 0.01f);
                Assert.AreEqual(expectedRoot.z, visual.position.z, 0.01f);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void LoadFromSaveRebuildsStaleBuildingFootprintFromCurrentDefinition()
        {
            List<Object> cleanup = new();
            try
            {
                BuildingDefinition definition = ScriptableObject.CreateInstance<BuildingDefinition>();
                cleanup.Add(definition);
                definition.ConfigureRuntimeFallback(
                    "resize_test_building",
                    "Resize Test Building",
                    PlotZone.Business,
                    new Vector2Int(6, 4),
                    Color.white,
                    5f);

                TownGenerationSettings settings = CreateSettings(definition);
                cleanup.Add(settings);

                GameObject townObject = new("Footprint Save Load Test Town");
                cleanup.Add(townObject);
                TownWorldController townWorld = townObject.AddComponent<TownWorldController>();
                townWorld.Configure(settings, null, null);

                WorldSaveDto dto = CreateStaleFootprintSaveDto(definition.BuildingId);
                SaveReferenceResolver resolver = new(townWorld);

                Assert.IsTrue(townWorld.LoadFromSaveDto(dto, resolver, out string message), message);
                Assert.AreEqual(1, townWorld.Buildings.Count);

                PlacedBuilding building = townWorld.Buildings[0];
                Assert.AreEqual(6, building.footprint.width);
                Assert.AreEqual(4, building.footprint.depth);
                Assert.AreEqual(new Vector2Int(6, 4), building.intendedFootprintSizeCells);
                Assert.AreEqual(12, building.footprint.xMin);
                Assert.AreEqual(22, building.footprint.zMin);

                TownPlot plot = townWorld.Plots[0];
                Assert.AreEqual(new Vector2Int(6, 4), plot.intendedBuildingFootprintCells);
                Assert.IsTrue(building.TryGetAnchor(AnchorType.FrontDoor, out BuildingAnchor frontDoor));
                Assert.IsFalse(building.footprint.Contains(frontDoor.coord));
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void LoadFromSavePreservesFitEnabledPrefabVisualScaleOnRebuiltFootprint()
        {
            List<Object> cleanup = new();
            try
            {
                GameObject prefab = CreateSizedPrefab("Fit Test Building Prefab", new Vector3(4f, 1f, 6f));
                cleanup.Add(prefab);

                BuildingDefinition definition = ScriptableObject.CreateInstance<BuildingDefinition>();
                cleanup.Add(definition);
                definition.ConfigureRuntimeFallback(
                    "fit_visual_test_building",
                    "Fit Visual Test Building",
                    PlotZone.Business,
                    new Vector2Int(6, 4),
                    Color.white,
                    5f);
                definition.ConfigureVisual(prefab, 1f, 0f, Vector3.zero, true, 1f);

                TownGenerationSettings settings = CreateSettings(definition);
                cleanup.Add(settings);

                GameObject townObject = new("Footprint Visual Fit Test Town");
                cleanup.Add(townObject);
                TownWorldController townWorld = townObject.AddComponent<TownWorldController>();
                townWorld.Configure(settings, null, null);

                WorldSaveDto dto = CreateStaleFootprintSaveDto(definition.BuildingId);
                SaveReferenceResolver resolver = new(townWorld);

                Assert.IsTrue(townWorld.LoadFromSaveDto(dto, resolver, out string message), message);

                Transform visual = townObject.transform.Find("WorldVisualRoot/Building 000 Fit Visual Test Building");
                Assert.IsNotNull(visual);

                Bounds bounds = GetRendererBounds(visual.gameObject);
                Assert.AreEqual(4f, bounds.size.x, 0.01f);
                Assert.AreEqual(6f, bounds.size.z, 0.01f);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void GenerateTownShellActivatesAuthoredExteriorPropsForAssignedBusiness()
        {
            List<Object> cleanup = new();
            try
            {
                GameObject prefab = CreateSizedPrefab("Authored Exterior Prop Building Prefab", new Vector3(2f, 1f, 2f));
                cleanup.Add(prefab);
                BuildingExteriorPropAuthoring authoring = prefab.AddComponent<BuildingExteriorPropAuthoring>();
                Transform butcherRoot = CreatePropGroup(prefab.transform, "Butcher Exterior Props", 4);
                Transform generalStoreRoot = CreatePropGroup(prefab.transform, "General Store Exterior Props", 3);
                Transform genericRoot = CreatePropGroup(prefab.transform, "Generic Commercial Exterior Props", 2);
                authoring.ConfigureGroups(
                    new BuildingExteriorPropGroup
                    {
                        businessTypeToggles = CreateBusinessToggles(BusinessType.Butcher),
                        groupRoot = butcherRoot,
                        activeRatio = 0.5f
                    },
                    new BuildingExteriorPropGroup
                    {
                        businessTypeToggles = CreateBusinessToggles(BusinessType.GeneralStore),
                        groupRoot = generalStoreRoot,
                        activeRatio = 1f
                    },
                    new BuildingExteriorPropGroup
                    {
                        matchesAnyCommercialBusiness = true,
                        groupRoot = genericRoot,
                        activeRatio = 1f
                    });

                BuildingDefinition definition = ScriptableObject.CreateInstance<BuildingDefinition>();
                cleanup.Add(definition);
                definition.ConfigureRuntimeFallback(
                    "authored_exterior_prop_test_building",
                    "Authored Exterior Prop Test Building",
                    PlotZone.Business,
                    new Vector2Int(4, 4),
                    Color.white,
                    5f);
                definition.ConfigureVisual(prefab, 1f, 0f, Vector3.zero, false, 1f);

                TownGenerationSettings settings = CreateSettings(definition);
                cleanup.Add(settings);
                settings.generateDetailProps = true;

                GameObject townObject = new("Authored Exterior Prop Test Town");
                cleanup.Add(townObject);
                TownWorldController townWorld = townObject.AddComponent<TownWorldController>();
                townWorld.Configure(settings, null, null);
                SharedBusinessRuntimeManager sharedBusinessRuntime = townObject.AddComponent<SharedBusinessRuntimeManager>();
                sharedBusinessRuntime.Configure(townWorld, null);
                sharedBusinessRuntime.LoadFromSaveDtos(new[]
                {
                    new BusinessInstanceSaveDto
                    {
                        instanceId = "test_butcher_000",
                        profileId = "test_butcher",
                        businessType = BusinessType.Butcher,
                        assignedBuildingId = 0,
                        runtimeDisplayName = "Test Butcher"
                    }
                });

                townWorld.GenerateTownShell();

                Transform root = townObject.transform.Find("WorldVisualRoot");
                Transform buildingVisual = root.Find("Building 000 Authored Exterior Prop Test Building");
                Assert.IsNotNull(buildingVisual);

                Transform activeButcherRoot = buildingVisual.Find("Butcher Exterior Props");
                Transform inactiveGeneralStoreRoot = buildingVisual.Find("General Store Exterior Props");
                Transform inactiveGenericRoot = buildingVisual.Find("Generic Commercial Exterior Props");
                Assert.IsTrue(activeButcherRoot.gameObject.activeSelf);
                Assert.IsFalse(inactiveGeneralStoreRoot.gameObject.activeSelf);
                Assert.IsFalse(inactiveGenericRoot.gameObject.activeSelf);
                Assert.AreEqual(2, CountActiveChildren(activeButcherRoot));

                Collider[] activeColliders = activeButcherRoot.GetComponentsInChildren<Collider>(false);
                for (int i = 0; i < activeColliders.Length; i++)
                {
                    Assert.IsFalse(activeColliders[i].enabled);
                }

                Assert.AreEqual(0, FindChildrenStartingWith(root, "Detail Prop").Count);
                Assert.AreEqual(0, FindChildrenStartingWith(root, "Agricultural Detail Prop").Count);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }


        [Test]
        public void GenerateTownShellIgnoresPrefabRootExteriorGroupAndSurfacesAuthoringIssue()
        {
            List<Object> cleanup = new();
            try
            {
                GameObject prefab = CreateSizedPrefab("Invalid Exterior Root Building Prefab", new Vector3(2f, 1f, 2f));
                cleanup.Add(prefab);
                BuildingExteriorPropAuthoring authoring = prefab.AddComponent<BuildingExteriorPropAuthoring>();
                Transform fallbackRoot = CreatePropGroup(prefab.transform, "Generic Commercial Exterior Props", 2);
                authoring.ConfigureGroups(
                    new BuildingExteriorPropGroup
                    {
                        businessTypeToggles = CreateBusinessToggles(BusinessType.Butcher),
                        groupRoot = prefab.transform,
                        activeRatio = 1f
                    },
                    new BuildingExteriorPropGroup
                    {
                        matchesAnyCommercialBusiness = true,
                        groupRoot = fallbackRoot,
                        activeRatio = 1f
                    });

                BuildingDefinition definition = ScriptableObject.CreateInstance<BuildingDefinition>();
                cleanup.Add(definition);
                definition.ConfigureRuntimeFallback(
                    "invalid_exterior_root_test_building",
                    "Invalid Exterior Root Test Building",
                    PlotZone.Business,
                    new Vector2Int(4, 4),
                    Color.white,
                    5f);
                definition.ConfigureVisual(prefab, 1f, 0f, Vector3.zero, false, 1f);

                TownGenerationSettings settings = CreateSettings(definition);
                cleanup.Add(settings);
                settings.generateDetailProps = true;

                GameObject townObject = new("Invalid Exterior Root Test Town");
                cleanup.Add(townObject);
                TownWorldController townWorld = townObject.AddComponent<TownWorldController>();
                townWorld.Configure(settings, null, null);
                SharedBusinessRuntimeManager sharedBusinessRuntime = townObject.AddComponent<SharedBusinessRuntimeManager>();
                sharedBusinessRuntime.Configure(townWorld, null);
                sharedBusinessRuntime.LoadFromSaveDtos(new[]
                {
                    new BusinessInstanceSaveDto
                    {
                        instanceId = "invalid_root_butcher_000",
                        profileId = "invalid_root_butcher",
                        businessType = BusinessType.Butcher,
                        assignedBuildingId = 0,
                        runtimeDisplayName = "Invalid Root Butcher"
                    }
                });

                townWorld.GenerateTownShell();

                Transform root = townObject.transform.Find("WorldVisualRoot");
                Transform buildingVisual = root.Find("Building 000 Invalid Exterior Root Test Building");
                Assert.IsNotNull(buildingVisual);
                Assert.IsTrue(buildingVisual.gameObject.activeSelf);

                Transform fallbackVisualRoot = buildingVisual.Find("Generic Commercial Exterior Props");
                Assert.IsNotNull(fallbackVisualRoot);
                Assert.IsTrue(fallbackVisualRoot.gameObject.activeSelf);
                Assert.AreEqual(2, CountActiveChildren(fallbackVisualRoot));

                string inspection = townWorld.BuildBuildingInspectionSummary(0);
                StringAssert.Contains("fallback route", inspection);
                StringAssert.Contains("blocked matching group: 1 (prefab root 1)", inspection);
                StringAssert.Contains("uses the prefab root as its group root", inspection);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void GenerateTownShellReportsNoUsableExteriorMatchWhenOnlyMatchingGroupHasInvalidRoot()
        {
            List<Object> cleanup = new();
            try
            {
                GameObject prefab = CreateSizedPrefab("Blocked Exterior Match Building Prefab", new Vector3(2f, 1f, 2f));
                cleanup.Add(prefab);
                BuildingExteriorPropAuthoring authoring = prefab.AddComponent<BuildingExteriorPropAuthoring>();
                authoring.ConfigureGroups(
                    new BuildingExteriorPropGroup
                    {
                        businessTypeToggles = CreateBusinessToggles(BusinessType.Butcher),
                        groupRoot = prefab.transform,
                        activeRatio = 1f
                    });

                BuildingDefinition definition = ScriptableObject.CreateInstance<BuildingDefinition>();
                cleanup.Add(definition);
                definition.ConfigureRuntimeFallback(
                    "blocked_exterior_match_test_building",
                    "Blocked Exterior Match Test Building",
                    PlotZone.Business,
                    new Vector2Int(4, 4),
                    Color.white,
                    5f);
                definition.ConfigureVisual(prefab, 1f, 0f, Vector3.zero, false, 1f);

                TownGenerationSettings settings = CreateSettings(definition);
                cleanup.Add(settings);
                settings.generateDetailProps = true;

                GameObject townObject = new("Blocked Exterior Match Test Town");
                cleanup.Add(townObject);
                TownWorldController townWorld = townObject.AddComponent<TownWorldController>();
                townWorld.Configure(settings, null, null);
                SharedBusinessRuntimeManager sharedBusinessRuntime = townObject.AddComponent<SharedBusinessRuntimeManager>();
                sharedBusinessRuntime.Configure(townWorld, null);
                sharedBusinessRuntime.LoadFromSaveDtos(new[]
                {
                    new BusinessInstanceSaveDto
                    {
                        instanceId = "blocked_root_butcher_000",
                        profileId = "blocked_root_butcher",
                        businessType = BusinessType.Butcher,
                        assignedBuildingId = 0,
                        runtimeDisplayName = "Blocked Root Butcher"
                    }
                });

                townWorld.GenerateTownShell();

                Transform root = townObject.transform.Find("WorldVisualRoot");
                Transform buildingVisual = root.Find("Building 000 Blocked Exterior Match Test Building");
                Assert.IsNotNull(buildingVisual);
                Assert.IsTrue(buildingVisual.gameObject.activeSelf);

                string inspection = townWorld.BuildBuildingInspectionSummary(0);
                StringAssert.Contains("no usable match remains", inspection);
                StringAssert.Contains("blocked matching group: 1 (prefab root 1)", inspection);
                StringAssert.Contains("uses the prefab root", inspection);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }


        [Test]
        public void GenerateTownShellCollapsesSameRootMatchingVariantsBeforeTieReporting()
        {
            List<Object> cleanup = new();
            try
            {
                GameObject prefab = CreateSizedPrefab("Same Root Variant Building Prefab", new Vector3(2f, 1f, 2f));
                cleanup.Add(prefab);
                BuildingExteriorPropAuthoring authoring = prefab.AddComponent<BuildingExteriorPropAuthoring>();
                Transform sharedRoot = CreatePropGroup(prefab.transform, "Shared Root Exterior Props", 3);
                Transform otherGenericRoot = CreatePropGroup(prefab.transform, "Other Generic Exterior Props", 2);
                authoring.ConfigureGroups(
                    new BuildingExteriorPropGroup
                    {
                        debugName = "Butcher Exact Shared Root",
                        businessTypeToggles = CreateBusinessToggles(BusinessType.Butcher),
                        groupRoot = sharedRoot,
                        activeRatio = 1f
                    },
                    new BuildingExteriorPropGroup
                    {
                        debugName = "Shared Root Commercial Variant",
                        matchesAnyCommercialBusiness = true,
                        groupRoot = sharedRoot,
                        activeRatio = 1f
                    },
                    new BuildingExteriorPropGroup
                    {
                        debugName = "Shared Root Mixed Use Variant",
                        matchesMixedUse = true,
                        groupRoot = sharedRoot,
                        activeRatio = 1f
                    },
                    new BuildingExteriorPropGroup
                    {
                        debugName = "Other Generic Exterior Root",
                        matchesAnyCommercialBusiness = true,
                        groupRoot = otherGenericRoot,
                        activeRatio = 1f
                    });

                BuildingDefinition definition = ScriptableObject.CreateInstance<BuildingDefinition>();
                cleanup.Add(definition);
                definition.ConfigureRuntimeFallback(
                    "same_root_variant_test_building",
                    "Same Root Variant Test Building",
                    PlotZone.Business,
                    new Vector2Int(4, 4),
                    Color.white,
                    5f);
                definition.ConfigureVisual(prefab, 1f, 0f, Vector3.zero, false, 1f);

                TownGenerationSettings settings = CreateSettings(definition);
                cleanup.Add(settings);
                settings.generateDetailProps = true;

                GameObject townObject = new("Same Root Variant Test Town");
                cleanup.Add(townObject);
                TownWorldController townWorld = townObject.AddComponent<TownWorldController>();
                townWorld.Configure(settings, null, null);
                SharedBusinessRuntimeManager sharedBusinessRuntime = townObject.AddComponent<SharedBusinessRuntimeManager>();
                sharedBusinessRuntime.Configure(townWorld, null);
                sharedBusinessRuntime.LoadFromSaveDtos(new[]
                {
                    new BusinessInstanceSaveDto
                    {
                        instanceId = "same_root_variant_butcher_000",
                        profileId = "same_root_variant_butcher",
                        businessType = BusinessType.Butcher,
                        assignedBuildingId = 0,
                        runtimeDisplayName = "Same Root Variant Butcher"
                    }
                });

                townWorld.GenerateTownShell();

                Transform root = townObject.transform.Find("WorldVisualRoot");
                Transform buildingVisual = root.Find("Building 000 Same Root Variant Test Building");
                Assert.IsNotNull(buildingVisual);

                Transform sharedRootVisual = buildingVisual.Find("Shared Root Exterior Props");
                Transform otherGenericVisual = buildingVisual.Find("Other Generic Exterior Props");
                Assert.IsNotNull(sharedRootVisual);
                Assert.IsTrue(sharedRootVisual.gameObject.activeSelf);
                Assert.IsNotNull(otherGenericVisual);
                Assert.IsFalse(otherGenericVisual.gameObject.activeSelf);

                string inspection = townWorld.BuildBuildingInspectionSummary(0);
                StringAssert.Contains("collapsed same-root variants: 2", inspection);
                StringAssert.Contains("hierarchy overlap: duplicate root 1", inspection);
                StringAssert.DoesNotContain("array-order tie break", inspection);
                StringAssert.DoesNotContain("won by authored array order", inspection);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void GenerateTownShellSummarizesNestedManagedRootOverlapCompactly()
        {
            List<Object> cleanup = new();
            try
            {
                GameObject prefab = CreateSizedPrefab("Nested Exterior Overlap Building Prefab", new Vector3(2f, 1f, 2f));
                cleanup.Add(prefab);
                BuildingExteriorPropAuthoring authoring = prefab.AddComponent<BuildingExteriorPropAuthoring>();
                Transform parentRoot = new GameObject("Parent Exterior Props").transform;
                parentRoot.SetParent(prefab.transform, false);
                GameObject parentProp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                parentProp.name = "Parent Prop 00";
                parentProp.transform.SetParent(parentRoot, false);
                parentProp.SetActive(false);

                Transform childRoot = CreatePropGroup(parentRoot, "Child Exterior Props", 2);
                parentRoot.gameObject.SetActive(false);
                authoring.ConfigureGroups(
                    new BuildingExteriorPropGroup
                    {
                        debugName = "Parent Generic Group",
                        matchesAnyCommercialBusiness = true,
                        groupRoot = parentRoot,
                        includeNestedChildren = true,
                        activeRatio = 1f
                    },
                    new BuildingExteriorPropGroup
                    {
                        debugName = "Child Butcher Group",
                        businessTypeToggles = CreateBusinessToggles(BusinessType.Butcher),
                        groupRoot = childRoot,
                        activeRatio = 1f
                    });

                BuildingDefinition definition = ScriptableObject.CreateInstance<BuildingDefinition>();
                cleanup.Add(definition);
                definition.ConfigureRuntimeFallback(
                    "nested_exterior_overlap_test_building",
                    "Nested Exterior Overlap Test Building",
                    PlotZone.Business,
                    new Vector2Int(4, 4),
                    Color.white,
                    5f);
                definition.ConfigureVisual(prefab, 1f, 0f, Vector3.zero, false, 1f);

                TownGenerationSettings settings = CreateSettings(definition);
                cleanup.Add(settings);
                settings.generateDetailProps = true;

                GameObject townObject = new("Nested Exterior Overlap Test Town");
                cleanup.Add(townObject);
                TownWorldController townWorld = townObject.AddComponent<TownWorldController>();
                townWorld.Configure(settings, null, null);
                SharedBusinessRuntimeManager sharedBusinessRuntime = townObject.AddComponent<SharedBusinessRuntimeManager>();
                sharedBusinessRuntime.Configure(townWorld, null);
                sharedBusinessRuntime.LoadFromSaveDtos(new[]
                {
                    new BusinessInstanceSaveDto
                    {
                        instanceId = "nested_overlap_butcher_000",
                        profileId = "nested_overlap_butcher",
                        businessType = BusinessType.Butcher,
                        assignedBuildingId = 0,
                        runtimeDisplayName = "Nested Overlap Butcher"
                    }
                });

                townWorld.GenerateTownShell();

                Transform root = townObject.transform.Find("WorldVisualRoot");
                Transform buildingVisual = root.Find("Building 000 Nested Exterior Overlap Test Building");
                Assert.IsNotNull(buildingVisual);

                string inspection = townWorld.BuildBuildingInspectionSummary(0);
                StringAssert.Contains("hierarchy overlap: nested under managed root 1, contains nested managed root 1", inspection);
                StringAssert.Contains("activated 1 ancestor group root", inspection);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void GenerateTownShellDoesNotSpawnLooseDetailPropsForStandardBuildings()
        {
            List<Object> cleanup = new();
            try
            {
                GameObject propPrefab = CreateSizedPrefab("Loose Detail Prop Test Crate Prefab", new Vector3(0.5f, 0.5f, 0.5f));
                cleanup.Add(propPrefab);

                BuildingDefinition definition = ScriptableObject.CreateInstance<BuildingDefinition>();
                cleanup.Add(definition);
                definition.ConfigureRuntimeFallback(
                    "loose_detail_prop_bypass_test_building",
                    "Loose Detail Prop Bypass Test Building",
                    PlotZone.Business,
                    new Vector2Int(4, 4),
                    Color.white,
                    5f);

                TownGenerationSettings settings = CreateSettings(definition);
                cleanup.Add(settings);
                settings.generateDetailProps = true;
                settings.cratePropPrefabs = new[] { propPrefab };

                GameObject townObject = new("Loose Detail Prop Bypass Test Town");
                cleanup.Add(townObject);
                TownWorldController townWorld = townObject.AddComponent<TownWorldController>();
                townWorld.Configure(settings, null, null);

                townWorld.GenerateTownShell();

                Transform root = townObject.transform.Find("WorldVisualRoot");
                List<Transform> detailProps = FindChildrenStartingWith(root, "Detail Prop");
                Assert.AreEqual(0, detailProps.Count);
                List<Transform> agriculturalDetailProps = FindChildrenStartingWith(root, "Agricultural Detail Prop");
                Assert.AreEqual(0, agriculturalDetailProps.Count);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        private static TownGenerationSettings CreateSettings(BuildingDefinition definition)
        {
            TownGenerationSettings settings = ScriptableObject.CreateInstance<TownGenerationSettings>();
            settings.gridWidthCells = 40;
            settings.gridDepthCells = 50;
            settings.cellSizeMeters = 2f;
            settings.seed = 1886;
            settings.roadWidthCells = 3;
            settings.mainStreetLengthCells = 30;
            settings.crossStreetCount = 0;
            settings.plotDepthCells = 8;
            settings.minPlotFrontageCells = 8;
            settings.maxPlotFrontageCells = 8;
            settings.buildingSetbackCells = 1;
            settings.generateAgriculturalParcels = false;
            settings.generateSawmillProperty = false;
            settings.generateTownHall = false;
            settings.showTerrainOverlay = false;
            settings.showRoads = false;
            settings.showPlots = false;
            settings.showBuildings = true;
            settings.buildingCatalog = new[] { definition };
            settings.Sanitize();
            return settings;
        }

        private static WorldSaveDto CreateStaleFootprintSaveDto(string buildingId)
        {
            WorldSaveDto dto = new()
            {
                settingsName = "Footprint Save Load Test Settings",
                seed = 1886,
                gridWidthCells = 40,
                gridDepthCells = 50,
                cellSizeMeters = 2f,
                gridOriginX = -40f,
                gridOriginY = 0f,
                gridOriginZ = -50f
            };

            dto.plots.Add(new PlotSaveDto
            {
                id = 0,
                zone = PlotZone.Business,
                bounds = CreateRectDto(11, 20, 8, 8),
                candidateFootprint = CreateRectDto(11, 20, 8, 8),
                siteSizeX = 8,
                siteSizeY = 8,
                intendedFootprintX = 4,
                intendedFootprintY = 4,
                frontageCells = 8,
                depthCells = 8,
                roadFrontageDirection = GridDirection.East,
                roadAccessCell = CreateCoordDto(19, 24),
                buildingId = 0
            });

            dto.buildings.Add(new BuildingSaveDto
            {
                id = 0,
                plotId = 0,
                buildingDefinitionId = buildingId,
                footprint = CreateRectDto(14, 22, 4, 4),
                siteSizeX = 8,
                siteSizeY = 8,
                intendedFootprintX = 4,
                intendedFootprintY = 4,
                frontageDirection = GridDirection.East
            });

            return dto;
        }

        private static GridRectSaveDto CreateRectDto(int xMin, int zMin, int width, int depth)
        {
            return new GridRectSaveDto
            {
                xMin = xMin,
                zMin = zMin,
                width = width,
                depth = depth
            };
        }

        private static GridCoordSaveDto CreateCoordDto(int x, int z)
        {
            return new GridCoordSaveDto
            {
                x = x,
                z = z
            };
        }

        private static Vector3 ResolveExpectedFrontageTarget(TownGrid grid, GridRect footprint, GridDirection frontageDirection, GridCoord roadAccessCell, float y)
        {
            Vector3 roadAccessCenter = grid.CoordToWorldCenter(roadAccessCell, y);
            float cellSize = grid.CellSizeMeters;
            float minX = grid.Origin.x + footprint.xMin * cellSize;
            float maxX = grid.Origin.x + (footprint.xMaxInclusive + 1) * cellSize;
            float minZ = grid.Origin.z + footprint.zMin * cellSize;
            float maxZ = grid.Origin.z + (footprint.zMaxInclusive + 1) * cellSize;

            return frontageDirection switch
            {
                GridDirection.North => new Vector3(Mathf.Clamp(roadAccessCenter.x, minX, maxX), y, maxZ),
                GridDirection.East => new Vector3(maxX, y, Mathf.Clamp(roadAccessCenter.z, minZ, maxZ)),
                GridDirection.South => new Vector3(Mathf.Clamp(roadAccessCenter.x, minX, maxX), y, minZ),
                GridDirection.West => new Vector3(minX, y, Mathf.Clamp(roadAccessCenter.z, minZ, maxZ)),
                _ => grid.CoordToWorldCenter(footprint.Center, y)
            };
        }

        private static Vector3 ResolveExpectedFootprintCenter(TownGrid grid, GridRect footprint, float y)
        {
            Vector3 min = grid.CoordToWorldCenter(new GridCoord(footprint.xMin, footprint.zMin));
            Vector3 max = grid.CoordToWorldCenter(new GridCoord(footprint.xMaxInclusive, footprint.zMaxInclusive));
            Vector3 center = (min + max) * 0.5f;
            center.y = y;
            return center;
        }

        private static float ResolveExpectedFrontageYaw(GridDirection frontageDirection)
        {
            return frontageDirection switch
            {
                GridDirection.North => 0f,
                GridDirection.East => 90f,
                GridDirection.South => 180f,
                GridDirection.West => 270f,
                _ => 0f
            };
        }

        private static GameObject CreateSizedPrefab(string name, Vector3 meshSize)
        {
            GameObject root = new(name);
            GameObject mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mesh.name = "Fit Test Mesh";
            mesh.transform.SetParent(root.transform, false);
            mesh.transform.localScale = meshSize;
            return root;
        }

        private static Transform CreatePropGroup(Transform parent, string name, int count)
        {
            GameObject group = new(name);
            group.transform.SetParent(parent, false);
            for (int i = 0; i < count; i++)
            {
                GameObject prop = GameObject.CreatePrimitive(PrimitiveType.Cube);
                prop.name = $"{name} Prop {i:00}";
                prop.transform.SetParent(group.transform, false);
                prop.transform.localPosition = new Vector3(i, 0f, 0f);
                prop.SetActive(false);
            }

            group.SetActive(false);
            return group.transform;
        }

        private static BuildingExteriorBusinessToggle[] CreateBusinessToggles(params BusinessType[] enabledTypes)
        {
            BuildingExteriorBusinessToggle[] toggles = new BuildingExteriorBusinessToggle[enabledTypes.Length];
            for (int i = 0; i < enabledTypes.Length; i++)
            {
                toggles[i] = new BuildingExteriorBusinessToggle
                {
                    businessType = enabledTypes[i],
                    enabled = true
                };
            }

            return toggles;
        }

        private static int CountActiveChildren(Transform root)
        {
            int count = 0;
            for (int i = 0; i < root.childCount; i++)
            {
                if (root.GetChild(i).gameObject.activeSelf)
                {
                    count++;
                }
            }

            return count;
        }

        private static Bounds GetRendererBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
            Assert.Greater(renderers.Length, 0);

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        private static List<Transform> FindChildrenStartingWith(Transform root, string prefix)
        {
            List<Transform> results = new();
            AddChildrenStartingWith(root, prefix, results);
            return results;
        }

        private static void AddChildrenStartingWith(Transform root, string prefix, List<Transform> results)
        {
            if (root == null)
            {
                return;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name.StartsWith(prefix, System.StringComparison.Ordinal))
                {
                    results.Add(child);
                }

                AddChildrenStartingWith(child, prefix, results);
            }
        }

        private static void DestroyAll(List<Object> objects)
        {
            for (int i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i] != null)
                {
                    Object.DestroyImmediate(objects[i]);
                }
            }
        }
    }
}
