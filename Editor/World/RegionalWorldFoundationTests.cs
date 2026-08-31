using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Persistence;
using LandLedgers.World;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LandLedgers.Editor.World
{
    public sealed class RegionalWorldFoundationTests
    {
        [Test]
        public void RegionalWorldGenerationIsDeterministicForSameSeed()
        {
            RegionalWorldGenerationSettings settings = CreateRegionalSettings(8192f, 8192f);

            RegionalWorldState first = RegionalWorldGenerator.Generate(1897, settings);
            RegionalWorldState second = RegionalWorldGenerator.Generate(1897, settings);

            CollectionAssert.AreEqual(CaptureSnapshot(first), CaptureSnapshot(second));
        }

        [Test]
        public void RegionalWorldGenerationChangesAcrossSeeds()
        {
            RegionalWorldGenerationSettings settings = CreateRegionalSettings(8192f, 8192f);

            RegionalWorldState first = RegionalWorldGenerator.Generate(1897, settings);
            RegionalWorldState second = RegionalWorldGenerator.Generate(1901, settings);

            CollectionAssert.AreNotEqual(CaptureSnapshot(first), CaptureSnapshot(second));
        }

        [Test]
        public void RegionalWorldBuildsLargeTiledSurveyedRegion()
        {
            RegionalWorldGenerationSettings settings = CreateRegionalSettings(8192f, 8192f);

            RegionalWorldState state = RegionalWorldGenerator.Generate(1886, settings);

            Assert.That(state.SurveyCellSizeMeters, Is.EqualTo(2f));
            Assert.That(state.RegionSizeMeters.x, Is.GreaterThanOrEqualTo(8000f));
            Assert.That(state.RegionSizeMeters.y, Is.GreaterThanOrEqualTo(8000f));
            Assert.That(state.TerrainTiles.Count, Is.GreaterThan(1));
            Assert.That(state.SurveyParcels.Count, Is.GreaterThan(20));
            Assert.That(state.SurveyParcels.Exists(p => p.ParcelKind == RegionalParcelKind.TownPlatCore), Is.True);
            Assert.That(state.SurveyParcels.Exists(p => p.ParcelKind == RegionalParcelKind.RuralTract), Is.True);
            Assert.That(state.SurveyParcels.Exists(p => p.ParcelKind == RegionalParcelKind.RoughParcel), Is.True);
            Assert.That(state.SurveyParcels.Exists(p => p.ParcelKind == RegionalParcelKind.EdgeExpansion), Is.True);
            Assert.That(state.RouteCorridors.Count, Is.GreaterThan(0));
            Assert.That(state.RouteCorridors.Exists(r => r.Kind == RegionalRouteCorridorKind.OpeningFootholdSpine), Is.True);

            foreach (RegionalSurveyParcelRecord parcel in state.SurveyParcels)
            {
                Assert.That(parcel.Acreage, Is.GreaterThan(0f), parcel.ParcelId);
                Assert.That(parcel.SectionId, Is.Not.Empty, parcel.ParcelId);
            }
        }

        [Test]
        public void SurveyParcelsCarryAccessReadinessProvenanceAndRemoteSuitability()
        {
            RegionalWorldGenerationSettings settings = CreateRegionalSettings(8192f, 8192f);

            RegionalWorldState state = RegionalWorldGenerator.Generate(1897, settings);

            Assert.That(state.SurveyParcels.Count, Is.GreaterThan(20));
            Assert.That(state.SurveyParcels.Exists(p => p.FrontageClass != RegionalParcelFrontageClass.None), Is.True);
            Assert.That(state.SurveyParcels.Exists(p => (int)p.AccessQuality >= (int)RegionalParcelAccessQuality.WagonReachable), Is.True);
            Assert.That(state.SurveyParcels.Exists(p => p.Provenance != RegionalParcelProvenance.Unset), Is.True);
            Assert.That(state.SurveyParcels.Exists(p => p.RemoteSuitability != RegionalRemoteSuitability.None), Is.True);
            Assert.That(state.SurveyParcels.Exists(p => p.DevelopmentReadiness01 > 0.45f), Is.True);

            foreach (RegionalSurveyParcelRecord parcel in state.SurveyParcels)
            {
                Assert.That(parcel.TerrainBurden01, Is.InRange(0f, 1f), parcel.ParcelId);
                Assert.That(parcel.DevelopmentReadiness01, Is.InRange(0f, 1f), parcel.ParcelId);
                Assert.That(parcel.DistrictContext, Is.Not.Empty, parcel.ParcelId);
                Assert.That(parcel.DebugReason, Is.Not.Empty, parcel.ParcelId);
            }
        }

        [Test]
        public void RegionalParcelAuthorityBuildsDecisionReadoutsAndResourceHooks()
        {
            RegionalWorldGenerationSettings settings = CreateRegionalSettings(8192f, 8192f);
            RegionalWorldState state = RegionalWorldGenerator.Generate(1897, settings);
            RegionalSurveyParcelRecord parcel = state.SurveyParcels.Find(p => p != null && p.RemoteSuitability != RegionalRemoteSuitability.None)
                ?? state.SurveyParcels[0];

            RegionalParcelAuthorityReadout readout = RegionalParcelAuthority.Evaluate(state, parcel);
            string compact = readout.BuildCompactReadout();
            string inspection = readout.BuildInspectionReadout();

            Assert.That(readout.RecommendedUseLabel, Is.Not.Empty);
            Assert.That(readout.AcquisitionReadiness01, Is.InRange(0f, 1f));
            Assert.That(readout.TimberSuitability01, Is.InRange(0f, 1f));
            Assert.That(readout.MineralSuitability01, Is.InRange(0f, 1f));
            Assert.That(readout.FreightOutpostSuitability01, Is.InRange(0f, 1f));
            Assert.That(readout.RemoteIndustrialSuitability01, Is.InRange(0f, 1f));
            Assert.That(readout.CoalDistrictSuitability01, Is.InRange(0f, 1f));
            Assert.That(readout.IronDistrictSuitability01, Is.InRange(0f, 1f));
            Assert.That(readout.GoldProspectSuitability01, Is.InRange(0f, 1f));
            Assert.That(readout.SilverProspectSuitability01, Is.InRange(0f, 1f));
            Assert.That(readout.DepotRailCorridorSuitability01, Is.InRange(0f, 1f));
            Assert.That(readout.FutureSystemHooks.Count, Is.GreaterThan(0));
            Assert.That(readout.ResourceHookReadout, Does.Contain("coal"));
            Assert.That(readout.ResourceHookReadout, Does.Contain("iron"));
            Assert.That(readout.ResourceHookReadout, Does.Contain("gold"));
            Assert.That(readout.ResourceHookReadout, Does.Contain("silver"));
            Assert.That(readout.LogisticsHookReadout, Does.Contain("freight"));
            Assert.That(compact, Is.Not.Empty);
            Assert.That(inspection, Does.Contain("Owner read"));
            Assert.That(inspection.ToLowerInvariant(), Does.Not.Contain("unlock"));
        }

        [Test]
        public void AnchorTownUsesSuitabilityAndHydrologyInsteadOfMapCenter()
        {
            RegionalWorldGenerationSettings settings = CreateRegionalSettings(8192f, 8192f);

            RegionalWorldState state = RegionalWorldGenerator.Generate(1886, settings);

            Assert.That(state.AnchorTown.AnchorId, Is.Not.Empty);
            Assert.That(state.AnchorTown.SuitabilityScore01, Is.GreaterThan(0.55f));
            Assert.That(state.AnchorTown.NearestWaterDistanceMeters, Is.LessThan(900f));
            Assert.That(state.AnchorTown.DebugReason, Does.Contain("water"));

            Vector2 center = state.RegionCenterMeters;
            float centerDistance = Vector2.Distance(center, state.AnchorTown.CenterMeters);
            Assert.That(centerDistance, Is.GreaterThan(120f));
        }

        [Test]
        public void RegionalToLocalConversionMapsOpeningAnchorToSceneOrigin()
        {
            RegionalWorldGenerationSettings regionalSettings = CreateRegionalSettings(8192f, 8192f);
            RegionalWorldState state = RegionalWorldGenerator.Generate(1886, regionalSettings);

            Vector2 localAnchor = state.RegionalToLocalMeters(state.AnchorTown.CenterMeters);
            Assert.That(localAnchor.x, Is.EqualTo(0f).Within(0.01f));
            Assert.That(localAnchor.y, Is.EqualTo(0f).Within(0.01f));

            Vector2 regionalAgain = state.LocalToRegionalMeters(localAnchor);
            Assert.That(regionalAgain.x, Is.EqualTo(state.AnchorTown.CenterMeters.x).Within(0.01f));
            Assert.That(regionalAgain.y, Is.EqualTo(state.AnchorTown.CenterMeters.y).Within(0.01f));

            Rect tileLocal = state.RegionalToLocalRect(state.TerrainTiles[0].BoundsMeters);
            Rect tileRegional = state.LocalToRegionalRect(tileLocal);
            Assert.That(tileRegional.x, Is.EqualTo(state.TerrainTiles[0].BoundsMeters.x).Within(0.01f));
            Assert.That(tileRegional.y, Is.EqualTo(state.TerrainTiles[0].BoundsMeters.y).Within(0.01f));
        }

        [Test]
        public void TownGenerationKeepsOpeningTownNearOriginWhileRegionalAnchorStaysLargeScale()
        {
            List<Object> cleanup = new();
            try
            {
                BuildingDefinition definition = CreateDefinition();
                cleanup.Add(definition);
                TownGenerationSettings settings = CreateTownSettings(definition, 1886);
                cleanup.Add(settings);

                GameObject townObject = new("Regional Local Space Town");
                cleanup.Add(townObject);
                TownWorldController townWorld = townObject.AddComponent<TownWorldController>();
                townWorld.Configure(settings, null, null);
                townWorld.GenerateTownShell();

                Assert.That(townWorld.RegionalWorld.AnchorTown.CenterMeters.x, Is.GreaterThan(500f));
                Assert.That(townWorld.RegionalWorld.AnchorTown.CenterMeters.y, Is.GreaterThan(500f));
                Assert.That(settings.worldCenter.x, Is.EqualTo(0f).Within(0.01f));
                Assert.That(settings.worldCenter.z, Is.EqualTo(0f).Within(0.01f));

                Vector3 sceneAnchor = townWorld.RegionalPointToSceneWorld(townWorld.RegionalWorld.AnchorTown.CenterMeters);
                Assert.That(sceneAnchor.x, Is.EqualTo(0f).Within(0.01f));
                Assert.That(sceneAnchor.z, Is.EqualTo(0f).Within(0.01f));

                Vector2 regionalAtOrigin = townWorld.SceneWorldToRegionalPoint(Vector3.zero);
                Assert.That(regionalAtOrigin.x, Is.EqualTo(townWorld.RegionalWorld.AnchorTown.CenterMeters.x).Within(0.01f));
                Assert.That(regionalAtOrigin.y, Is.EqualTo(townWorld.RegionalWorld.AnchorTown.CenterMeters.y).Within(0.01f));
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void TerrainTileViewAndDebugOverlayUseOpeningTownLocalSpaceWithoutEditModeTerrainCreation()
        {
            List<Object> cleanup = new();
            try
            {
                BuildingDefinition definition = CreateDefinition();
                cleanup.Add(definition);
                TownGenerationSettings settings = CreateTownSettings(definition, 1886);
                cleanup.Add(settings);

                GameObject townObject = new("Regional Tile Local Space Town");
                cleanup.Add(townObject);
                TownWorldController townWorld = townObject.AddComponent<TownWorldController>();
                townWorld.Configure(settings, null, null);
                townWorld.GenerateTownShell();

                GameObject terrainObject = new("Regional Terrain Tile View Test");
                cleanup.Add(terrainObject);
                RegionalTerrainTileView terrainView = terrainObject.AddComponent<RegionalTerrainTileView>();
                Assert.False(terrainView.AllowEditorTerrainRebuild);
                Assert.False(terrainView.AllowsTerrainRendererCreationNow);
                terrainView.RebuildTiles();
                Assert.That(terrainView.TerrainsByTileId.Count, Is.EqualTo(0));

                Vector3 terrainAnchor = terrainView.PreviewSceneWorldForRegionalPoint(townWorld.RegionalWorld.AnchorTown.CenterMeters);
                Assert.That(terrainAnchor.x, Is.EqualTo(0f).Within(0.01f));
                Assert.That(terrainAnchor.z, Is.EqualTo(0f).Within(0.01f));

                GameObject overlayObject = new("Regional Debug Overlay Test");
                cleanup.Add(overlayObject);
                RegionalWorldDebugOverlay overlay = overlayObject.AddComponent<RegionalWorldDebugOverlay>();
                List<string> before = CaptureSnapshot(townWorld.RegionalWorld);
                Vector3 overlayAnchor = overlay.PreviewSceneWorldForRegionalPoint(townWorld.RegionalWorld.AnchorTown.CenterMeters);
                List<string> after = CaptureSnapshot(townWorld.RegionalWorld);

                Assert.That(overlayAnchor.x, Is.EqualTo(0f).Within(0.01f));
                Assert.That(overlayAnchor.z, Is.EqualTo(0f).Within(0.01f));
                CollectionAssert.AreEqual(before, after);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void RegionalTerrainTileViewSuppressesRouteRibbonsAcrossOpeningTownFootprint()
        {
            List<Object> cleanup = new();
            try
            {
                BuildingDefinition definition = CreateDefinition();
                cleanup.Add(definition);
                TownGenerationSettings settings = CreateTownSettings(definition, 1886);
                cleanup.Add(settings);

                GameObject townObject = new("Route Ribbon Suppression Town");
                cleanup.Add(townObject);
                TownWorldController townWorld = townObject.AddComponent<TownWorldController>();
                townWorld.Configure(settings, null, null);

                Vector2 anchor = new(4096f, 4096f);
                RegionalWorldState state = RegionalWorldState.Create(
                    1886,
                    RegionalTerrainRecipeFamily.CreekCorridorSettlementLand,
                    new Vector2(8192f, 8192f),
                    settings.cellSizeMeters,
                    512f,
                    RegionalAnchorTownRecord.Create("anchor", anchor, 1f, 80f, 1f, 1f, 1f, "test anchor"),
                    new List<RegionalTerrainTileRecord>(),
                    new List<RegionalWatercourseRecord>(),
                    new List<RegionalRouteCorridorRecord>
                    {
                        new(
                            "test_crossing_route",
                            RegionalRouteCorridorKind.FreightTrack,
                            "source",
                            "destination",
                            string.Empty,
                            string.Empty,
                            900f,
                            0.8f,
                            0.8f,
                            0.2f,
                            "crosses opening town",
                            new List<Vector2>
                            {
                                anchor + new Vector2(-450f, 0f),
                                anchor + new Vector2(450f, 0f)
                            })
                    },
                    new List<RegionalSurveyParcelRecord>(),
                    new List<RegionalVegetationZoneRecord>(),
                    new List<RegionalSettlementClusterRecord>(),
                    new List<RegionalSettlementRecord>());

                typeof(TownWorldController)
                    .GetField("regionalWorld", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(townWorld, state);

                GameObject terrainObject = new("Route Ribbon Suppression Terrain");
                cleanup.Add(terrainObject);
                RegionalTerrainTileView terrainView = terrainObject.AddComponent<RegionalTerrainTileView>();
                GameObject rootObject = new("Route Evidence Root");
                cleanup.Add(rootObject);

                typeof(RegionalTerrainTileView)
                    .GetField("townWorld", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(terrainView, townWorld);

                MethodInfo buildRoutes = typeof(RegionalTerrainTileView).GetMethod("BuildRouteCorridorMeshes", BindingFlags.Instance | BindingFlags.NonPublic);
                int built = (int)buildRoutes.Invoke(terrainView, new object[] { state, rootObject.transform, anchor, 0.5f, 0f });

                Assert.That(built, Is.EqualTo(0));
                Assert.That(rootObject.GetComponentsInChildren<MeshRenderer>().Length, Is.EqualTo(0));
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void RegionalTerrainTileViewUsesAuthoredRiverPrefabWhenAssigned()
        {
            List<Object> cleanup = new();
            try
            {
                BuildingDefinition definition = CreateDefinition();
                cleanup.Add(definition);
                TownGenerationSettings settings = CreateTownSettings(definition, 1886);
                cleanup.Add(settings);

                GameObject townObject = new("Authored River Town");
                cleanup.Add(townObject);
                TownWorldController townWorld = townObject.AddComponent<TownWorldController>();
                townWorld.Configure(settings, null, null);
                townWorld.GenerateTownShell();

                GameObject riverPrefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cleanup.Add(riverPrefab);
                riverPrefab.name = "Authored River Test Prefab";

                GameObject terrainObject = new("Authored River Terrain View");
                cleanup.Add(terrainObject);
                RegionalTerrainTileView terrainView = terrainObject.AddComponent<RegionalTerrainTileView>();

                FieldInfo townWorldField = typeof(RegionalTerrainTileView).GetField("townWorld", BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo riverPrefabField = typeof(RegionalTerrainTileView).GetField("authoredRiverPrefab", BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo forceWorldIdentityField = typeof(RegionalTerrainTileView).GetField("forceTileRootWorldIdentity", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(riverPrefabField, "RegionalTerrainTileView should expose an authored River prefab slot.");
                townWorldField.SetValue(terrainView, townWorld);
                riverPrefabField.SetValue(terrainView, riverPrefab);
                forceWorldIdentityField.SetValue(terrainView, false);

                MethodInfo rebuildImmediate = typeof(RegionalTerrainTileView).GetMethod("RebuildTilesImmediate", BindingFlags.Instance | BindingFlags.NonPublic);
                rebuildImmediate.Invoke(terrainView, null);

                GameObject authoredRiver = GameObject.Find("Authored River Test Prefab (authored regional water)");
                Assert.NotNull(authoredRiver, "The authored river prefab should be instantiated under the generated watercourse evidence root.");
                Assert.That(authoredRiver.transform.parent.name, Does.StartWith("Authored River Segment"));
                Assert.That(terrainView.LastBuiltWatercourseCount, Is.GreaterThan(0));
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void RegionalTerrainTileViewSuppressesFallbackWaterWhenAuthoredRiverPrefabIsMissingByDefault()
        {
            List<Object> cleanup = new();
            try
            {
                BuildingDefinition definition = CreateDefinition();
                cleanup.Add(definition);
                TownGenerationSettings settings = CreateTownSettings(definition, 1886);
                cleanup.Add(settings);

                GameObject townObject = new("Missing River Prefab Town");
                cleanup.Add(townObject);
                TownWorldController townWorld = townObject.AddComponent<TownWorldController>();
                townWorld.Configure(settings, null, null);
                townWorld.GenerateTownShell();

                GameObject terrainObject = new("Missing River Prefab Terrain View");
                cleanup.Add(terrainObject);
                RegionalTerrainTileView terrainView = terrainObject.AddComponent<RegionalTerrainTileView>();
                GameObject rootObject = new("Water Evidence Root");
                cleanup.Add(rootObject);

                FieldInfo townWorldField = typeof(RegionalTerrainTileView).GetField("townWorld", BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo riverPrefabField = typeof(RegionalTerrainTileView).GetField("authoredRiverPrefab", BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo fallbackField = typeof(RegionalTerrainTileView).GetField("buildFallbackWaterMeshesWhenAuthoredRiverPrefabMissing", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(fallbackField, "Missing authored River prefab fallback must be controlled by an explicit safety flag.");
                townWorldField.SetValue(terrainView, townWorld);
                riverPrefabField.SetValue(terrainView, null);
                fallbackField.SetValue(terrainView, false);

                MethodInfo buildWatercourses = typeof(RegionalTerrainTileView).GetMethod("BuildWatercourseMeshes", BindingFlags.Instance | BindingFlags.NonPublic);
                int built = (int)buildWatercourses.Invoke(terrainView, new object[] { townWorld.RegionalWorld, rootObject.transform, townWorld.RegionalWorld.AnchorTown.CenterMeters, 0.5f, 0f });

                Assert.That(built, Is.EqualTo(0));
                Assert.That(terrainView.LastWatercourseVisualStatus, Does.Contain("disabled"));
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void AuthoredPrefabAnchorInsideFootprintRemainsDestinationWithSupplementalRoadAccess()
        {
            List<Object> cleanup = new();
            try
            {
                GameObject prefab = new("Regional Interior Anchor Prefab");
                cleanup.Add(prefab);
                GameObject marker = new("Anchor_FrontDoor");
                cleanup.Add(marker);
                marker.transform.SetParent(prefab.transform, false);
                marker.transform.localPosition = Vector3.zero;
                marker.AddComponent<BuildingAnchorMarker>();
                GameObject serviceMarker = new("Anchor_ServicePoint");
                cleanup.Add(serviceMarker);
                serviceMarker.transform.SetParent(prefab.transform, false);
                serviceMarker.transform.localPosition = new Vector3(0.25f, 0f, 0.25f);
                GameObject dropOffMarker = new("Anchor_DropOffPoint");
                cleanup.Add(dropOffMarker);
                dropOffMarker.transform.SetParent(prefab.transform, false);
                dropOffMarker.transform.localPosition = new Vector3(-0.25f, 0f, -0.25f);

                BuildingDefinition definition = CreateDefinition();
                cleanup.Add(definition);
                definition.ConfigureVisual(prefab, 1f, 0f, Vector3.zero, false, 1f);

                TownGenerationSettings settings = CreateTownSettings(definition, 1886);
                cleanup.Add(settings);

                GameObject townObject = new("Regional Interior Anchor Town");
                cleanup.Add(townObject);
                TownWorldController townWorld = townObject.AddComponent<TownWorldController>();
                townWorld.Configure(settings, null, null);
                townWorld.GenerateTownShell();

                PlacedBuilding building = null;
                for (int i = 0; i < townWorld.Buildings.Count; i++)
                {
                    if (townWorld.Buildings[i].definition == definition)
                    {
                        building = townWorld.Buildings[i];
                        break;
                    }
                }

                Assert.NotNull(building);
                Assert.IsTrue(building.TryGetAnchor(AnchorType.FrontDoor, out BuildingAnchor frontDoor));
                Assert.IsTrue(frontDoor.fromAuthoredMarker);
                Assert.IsTrue(building.footprint.Contains(frontDoor.coord), $"Expected authored destination {frontDoor.coord} inside footprint {building.footprint}.");
                Assert.IsFalse(frontDoor.wasReconciledToExteriorAccess);
                Assert.IsTrue(frontDoor.hasSupplementalRoadAccessCoord);
                Assert.IsFalse(building.footprint.Contains(frontDoor.supplementalRoadAccessCoord));
                Assert.IsTrue(building.TryGetRoadAccessCoord(out GridCoord roadAccessCoord));
                Assert.AreEqual(frontDoor.supplementalRoadAccessCoord, roadAccessCoord);

                for (int i = 0; i < building.anchorIssues.Count; i++)
                {
                    Assert.False(building.anchorIssues[i].isError, building.anchorIssues[i].message);
                    StringAssert.DoesNotContain("inside the building footprint", building.anchorIssues[i].message);
                    StringAssert.DoesNotContain("exterior-reconciled", building.anchorIssues[i].message);
                }
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void SettlementRecordsDeriveFromClustersAndHideExactGrowthMath()
        {
            RegionalWorldGenerationSettings settings = CreateRegionalSettings(8192f, 8192f);

            RegionalWorldState state = RegionalWorldGenerator.Generate(1897, settings);

            Assert.That(state.Settlements.Count, Is.GreaterThan(0));
            RegionalSettlementRecord anchor = state.Settlements[0];
            Assert.That(anchor.SourceClusterId, Is.Not.Empty);
            Assert.That(anchor.HouseholdCount, Is.GreaterThan(0));
            Assert.That(anchor.BusinessCount, Is.GreaterThan(0));
            Assert.That(anchor.Population, Is.GreaterThan(0));
            Assert.That(anchor.PopulationBand, Is.Not.EqualTo(RegionalPopulationBand.Unset));
            Assert.That(anchor.NextTriggerWindowMinPopulation, Is.GreaterThanOrEqualTo(anchor.PopulationBandMin));
            Assert.That(anchor.NextTriggerWindowMaxPopulation, Is.GreaterThanOrEqualTo(anchor.NextTriggerWindowMinPopulation));
            Assert.That(anchor.SupportBurden01, Is.InRange(0f, 1f));

            string visible = anchor.BuildVisiblePlanningReadout();
            Assert.That(visible, Does.Not.Contain("unlock"));
            Assert.That(visible, Does.Not.Contain(anchor.NextTriggerWindowMinPopulation.ToString()));
            Assert.That(visible, Does.Not.Contain(anchor.NextTriggerWindowMaxPopulation.ToString()));
        }

        [Test]
        public void SettlementRecordsCarryCharacterPermanenceAndDeclineRisk()
        {
            RegionalWorldGenerationSettings settings = CreateRegionalSettings(8192f, 8192f);

            RegionalWorldState state = RegionalWorldGenerator.Generate(1897, settings);

            Assert.That(state.Settlements.Count, Is.GreaterThan(0));
            Assert.That(state.Settlements.Exists(s => s.Character != RegionalSettlementCharacter.Unset), Is.True);
            Assert.That(state.Settlements.Exists(s => s.Permanence != RegionalSettlementPermanence.Unset), Is.True);
            Assert.That(state.Settlements.Exists(s => s.DeclineRisk01 >= 0f && s.DeclineRisk01 <= 1f), Is.True);

            RegionalSettlementRecord lead = state.Settlements[0];
            Assert.That(lead.LocalConfidence01, Is.InRange(0f, 1f));
            Assert.That(lead.ServiceDeficit01, Is.InRange(0f, 1f));
            Assert.That(lead.Permanence01, Is.InRange(0f, 1f));
            Assert.That(lead.DebugSummary, Does.Contain("permanence"));
            Assert.That(lead.BuildVisiblePlanningReadout(), Does.Not.Contain("trigger"));
        }

        [Test]
        public void SettlementRecordsCarryHierarchyAndSuccessionReadiness()
        {
            RegionalWorldGenerationSettings settings = CreateRegionalSettings(8192f, 8192f);

            RegionalWorldState state = RegionalWorldGenerator.Generate(1897, settings);

            Assert.That(state.Settlements.Count, Is.GreaterThan(0));
            Assert.That(state.Settlements.Exists(s => s.SuccessionRole == RegionalSettlementSuccessionRole.OpeningFoothold), Is.True);
            Assert.That(state.Settlements.Exists(s => s.SuccessionRole != RegionalSettlementSuccessionRole.Unset), Is.True);

            foreach (RegionalSettlementRecord settlement in state.Settlements)
            {
                Assert.That(settlement.RegionalHierarchyRank, Is.GreaterThan(0), settlement.SettlementId);
                Assert.That(settlement.RegionalGravity01, Is.InRange(0f, 1f), settlement.SettlementId);
                Assert.That(settlement.FootholdChallenge01, Is.InRange(0f, 1f), settlement.SettlementId);
                Assert.That(settlement.SuccessionReadiness01, Is.InRange(0f, 1f), settlement.SettlementId);
                Assert.That(settlement.SuccessionContext, Is.Not.Empty, settlement.SettlementId);
                Assert.That(settlement.BuildVisiblePlanningReadout(), Does.Not.Contain("unlock"), settlement.SettlementId);
            }

            RegionalFoundationInspectionReport report = state.BuildInspectionReport();
            Assert.That(report.Summary, Does.Contain("succession challengers"));
            Assert.That(report.Summary, Does.Contain("strongest foothold challenge"));
        }

        [Test]
        public void RouteCorridorsConnectOpeningFootholdToEmergingRegionalNodes()
        {
            RegionalWorldGenerationSettings settings = CreateRegionalSettings(8192f, 8192f);

            RegionalWorldState state = RegionalWorldGenerator.Generate(1897, settings);

            Assert.That(state.RouteCorridors.Count, Is.GreaterThan(0));
            RegionalRouteCorridorRecord spine = state.RouteCorridors.Find(r => r.Kind == RegionalRouteCorridorKind.OpeningFootholdSpine);
            Assert.NotNull(spine);
            Assert.That(spine.Points.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(spine.LengthMeters, Is.GreaterThan(0f));
            Assert.That(spine.Practicality01, Is.InRange(0f, 1f));
            Assert.That(spine.SeasonalReliability01, Is.InRange(0f, 1f));
            Assert.That(spine.FreightBurden01, Is.InRange(0f, 1f));
            Assert.That(spine.BuildRoutePlanningReadout(), Does.Not.Contain("teleport"));

            if (state.Settlements.Count > 1)
            {
                Assert.That(state.RouteCorridors.Exists(r => r.Kind != RegionalRouteCorridorKind.OpeningFootholdSpine && !string.IsNullOrWhiteSpace(r.DestinationSettlementId)), Is.True);
                Assert.That(state.RouteCorridors.Exists(r => r.LengthMeters > 900f), Is.True);
            }

            foreach (RegionalRouteCorridorRecord route in state.RouteCorridors)
            {
                Assert.That(route.CorridorId, Is.Not.Empty);
                Assert.That(route.Points.Count, Is.GreaterThanOrEqualTo(2), route.CorridorId);
                Assert.That(route.LengthMeters, Is.GreaterThan(0f), route.CorridorId);
                Assert.That(route.Practicality01, Is.InRange(0f, 1f), route.CorridorId);
                Assert.That(route.SeasonalReliability01, Is.InRange(0f, 1f), route.CorridorId);
                Assert.That(route.FreightBurden01, Is.InRange(0f, 1f), route.CorridorId);
                Assert.That(route.PrimaryConstraintKind, Is.Not.EqualTo(RegionalRouteConstraintKind.None), route.CorridorId);
                Assert.That(route.BridgeOrFordNeed01, Is.InRange(0f, 1f), route.CorridorId);
                Assert.That(route.MudSeasonRisk01, Is.InRange(0f, 1f), route.CorridorId);
                Assert.That(route.GradeBurden01, Is.InRange(0f, 1f), route.CorridorId);
                Assert.That(route.ConstraintSummary, Is.Not.Empty, route.CorridorId);
                Assert.That(route.DebugSummary, Is.Not.Empty, route.CorridorId);
            }
        }

        [Test]
        public void ConnectorRouteCorridorsBeginAtTownRoadHandoffInsteadOfTownCenter()
        {
            RegionalWorldGenerationSettings settings = CreateRegionalSettings(8192f, 8192f);

            RegionalWorldState state = RegionalWorldGenerator.Generate(1897, settings);
            Vector2 anchor = state.AnchorTown.CenterMeters;

            foreach (RegionalRouteCorridorRecord route in state.RouteCorridors)
            {
                if (route.Kind == RegionalRouteCorridorKind.OpeningFootholdSpine)
                {
                    continue;
                }

                Assert.That(route.Points.Count, Is.GreaterThanOrEqualTo(2), route.CorridorId);
                Vector2 start = route.Points[0];
                Assert.That(Vector2.Distance(start, anchor), Is.GreaterThanOrEqualTo(140f), route.CorridorId);
                Assert.That(Mathf.Approximately(start.x, anchor.x) || Mathf.Approximately(start.y, anchor.y), Is.True, route.CorridorId);
            }
        }

        [Test]
        public void RouteCorridorsCarryCrossingAndSeasonalConstraintMetadata()
        {
            RegionalWorldGenerationSettings settings = CreateRegionalSettings(8192f, 8192f);

            RegionalWorldState state = RegionalWorldGenerator.Generate(1897, settings);

            Assert.That(state.RouteCorridors.Count, Is.GreaterThan(0));
            Assert.That(state.RouteCorridors.Exists(r => r.PrimaryConstraintKind != RegionalRouteConstraintKind.None), Is.True);
            Assert.That(state.RouteCorridors.Exists(r => !string.IsNullOrWhiteSpace(r.ConstraintSummary)), Is.True);

            foreach (RegionalRouteCorridorRecord route in state.RouteCorridors)
            {
                Assert.That(route.WaterCrossingCount, Is.GreaterThanOrEqualTo(0), route.CorridorId);
                Assert.That(route.WetGroundCrossingCount, Is.GreaterThanOrEqualTo(0), route.CorridorId);
                Assert.That(route.BridgeOrFordNeed01, Is.InRange(0f, 1f), route.CorridorId);
                Assert.That(route.MudSeasonRisk01, Is.InRange(0f, 1f), route.CorridorId);
                Assert.That(route.GradeBurden01, Is.InRange(0f, 1f), route.CorridorId);
                Assert.That(route.BuildRoutePlanningReadout(), Does.Not.Contain("teleport"));
            }
        }

        [Test]
        public void RegionalFoundationInspectionReportFlagsRouteEndpointGeometryRisks()
        {
            RegionalWorldGenerationSettings settings = CreateRegionalSettings(8192f, 8192f);
            RegionalWorldState source = RegionalWorldGenerator.Generate(1903, settings);
            RegionalRouteCorridorRecord sourceRoute = source.RouteCorridors[0];
            List<RegionalRouteCorridorRecord> brokenRoutes = new(source.RouteCorridors);
            List<Vector2> brokenPoints = new()
            {
                new Vector2(-900f, -900f),
                new Vector2(source.RegionSizeMeters.x + 900f, source.RegionSizeMeters.y + 900f)
            };

            brokenRoutes[0] = new RegionalRouteCorridorRecord(
                sourceRoute.CorridorId,
                sourceRoute.Kind,
                sourceRoute.SourceSettlementId,
                sourceRoute.DestinationSettlementId,
                sourceRoute.SourceParcelId,
                sourceRoute.DestinationParcelId,
                sourceRoute.LengthMeters,
                sourceRoute.Practicality01,
                sourceRoute.SeasonalReliability01,
                sourceRoute.FreightBurden01,
                sourceRoute.DebugSummary,
                brokenPoints,
                sourceRoute.PrimaryConstraintKind,
                sourceRoute.WaterCrossingCount,
                sourceRoute.WetGroundCrossingCount,
                sourceRoute.BridgeOrFordNeed01,
                sourceRoute.MudSeasonRisk01,
                sourceRoute.GradeBurden01,
                sourceRoute.ConstraintSummary);

            RegionalWorldState broken = RegionalWorldState.Create(
                source.Seed,
                source.RecipeFamily,
                source.RegionSizeMeters,
                source.SurveyCellSizeMeters,
                source.TerrainTileSizeMeters,
                source.AnchorTown,
                source.TerrainTiles,
                source.Watercourses,
                brokenRoutes,
                source.SurveyParcels,
                source.VegetationZones,
                source.SettlementClusters,
                source.Settlements);

            RegionalFoundationInspectionReport report = broken.BuildInspectionReport();

            Assert.That(report.Summary, Does.Contain("endpoint-risk routes"));
            Assert.That(report.Issues.Exists(issue => issue != null && issue.Message.Contains("leaves the regional map bounds")), Is.True);
        }

        [Test]
        public void SurveyParcelsCarryCorridorInfluenceForValuationAndFreightHandoff()
        {
            RegionalWorldGenerationSettings settings = CreateRegionalSettings(8192f, 8192f);

            RegionalWorldState state = RegionalWorldGenerator.Generate(1897, settings);

            Assert.That(state.RouteCorridors.Count, Is.GreaterThan(0));
            Assert.That(state.SurveyParcels.Exists(p => p.CorridorRelation != RegionalParcelCorridorRelation.None && p.CorridorRelation != RegionalParcelCorridorRelation.OffRoute), Is.True);
            Assert.That(state.SurveyParcels.Exists(p => p.FreightAccess01 > 0.35f), Is.True);
            Assert.That(state.SurveyParcels.Exists(p => p.CorridorRelation != RegionalParcelCorridorRelation.None && p.CorridorContext.Contains("constraint")), Is.True);

            foreach (RegionalSurveyParcelRecord parcel in state.SurveyParcels)
            {
                Assert.That(parcel.CorridorInfluence01, Is.InRange(0f, 1f), parcel.ParcelId);
                Assert.That(parcel.FreightAccess01, Is.InRange(0f, 1f), parcel.ParcelId);
                Assert.That(parcel.CorridorBurden01, Is.InRange(0f, 1f), parcel.ParcelId);
                Assert.That(parcel.CorridorContext, Is.Not.Empty, parcel.ParcelId);
                Assert.That(parcel.BuildParcelPlanningReadout(), Does.Not.Contain("teleport"));
            }
        }

        [Test]
        public void TerrainTilesCarryRouteAwareReadinessForActiveRegionHandoff()
        {
            RegionalWorldGenerationSettings settings = CreateRegionalSettings(8192f, 8192f);

            RegionalWorldState state = RegionalWorldGenerator.Generate(1897, settings);

            Assert.That(state.RouteCorridors.Count, Is.GreaterThan(0));
            Assert.That(state.TerrainTiles.Exists(t => t.RouteInfluence01 > 0.05f), Is.True);
            Assert.That(state.TerrainTiles.Exists(t => t.RouteActivationReadiness01 >= 0.42f), Is.True);

            foreach (RegionalTerrainTileRecord tile in state.TerrainTiles)
            {
                Assert.That(tile.RouteInfluence01, Is.InRange(0f, 1f), tile.TileId);
                Assert.That(tile.RouteActivationReadiness01, Is.InRange(0f, 1f), tile.TileId);
                Assert.That(tile.FreightBurdenInfluence01, Is.InRange(0f, 1f), tile.TileId);
                Assert.That(tile.BuildReadinessSummary(), Does.Contain("route readiness"));
            }
        }

        [Test]
        public void RegionalFoundationInspectionReportHasNoBlockingErrorsForGeneratedFoundation()
        {
            RegionalWorldGenerationSettings settings = CreateRegionalSettings(8192f, 8192f);

            RegionalWorldState state = RegionalWorldGenerator.Generate(1897, settings);
            RegionalFoundationInspectionReport report = state.BuildInspectionReport();

            Assert.False(report.HasErrors, report.BuildCompactReadout());
            Assert.That(report.BuildCompactReadout(), Does.Contain("Regional foundation check"));
            Assert.That(report.Summary, Does.Contain("parcels"));
            Assert.That(report.Summary, Does.Contain("routes"));
            Assert.That(report.Summary, Does.Contain("succession"));
            Assert.That(report.WarningCount, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void RegionalFoundationInspectionReportBuildsOpportunityReadouts()
        {
            RegionalWorldGenerationSettings settings = CreateRegionalSettings(8192f, 8192f);

            RegionalWorldState state = RegionalWorldGenerator.Generate(1897, settings);
            RegionalFoundationInspectionReport report = state.BuildInspectionReport();

            Assert.That(report.OpportunityReadouts.Count, Is.GreaterThan(0));
            Assert.That(report.OpportunityCount, Is.EqualTo(report.OpportunityReadouts.Count));
            Assert.That(report.Summary, Does.Contain("opportunity readouts"));
            Assert.That(report.BuildCompactReadout(), Does.Contain("opportunity readouts"));
            Assert.That(report.BuildOpportunityDigest(), Does.Contain("Regional opportunities"));
            Assert.That(report.OpportunityReadouts.Exists(o => o != null && o.Kind != RegionalFoundationOpportunityKind.None && !string.IsNullOrWhiteSpace(o.BuildReadout())), Is.True);
        }

        [Test]
        public void TownGenerationSettingsExposeRegionalDebugAuthoringControls()
        {
            TownGenerationSettings settings = ScriptableObject.CreateInstance<TownGenerationSettings>();
            try
            {
                Assert.That(settings.regionalInitialHouseholdCount, Is.EqualTo(18));
                Assert.That(settings.regionalInitialBusinessCount, Is.EqualTo(6));

                settings.regionalInspectionOpportunityDigestLimit = 99;
                settings.showRegionalOpportunityMarkers = false;
                settings.showRegionalRouteConstraintMarkers = false;

                settings.Sanitize();

                Assert.That(settings.regionalInitialHouseholdCount, Is.EqualTo(18));
                Assert.That(settings.regionalInitialBusinessCount, Is.EqualTo(6));
                Assert.That(settings.regionalInspectionOpportunityDigestLimit, Is.EqualTo(8));
                string readout = settings.BuildRegionalDebugSurfaceSummary();
                Assert.That(readout, Does.Contain("Regional debug"));
                Assert.That(readout, Does.Contain("overlay sync"));
                Assert.That(readout, Does.Contain("13/15"));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void RegionalFoundationInspectionReportFlagsMissingRouteCorridorLayer()
        {
            RegionalWorldGenerationSettings settings = CreateRegionalSettings(8192f, 8192f);
            RegionalWorldState source = RegionalWorldGenerator.Generate(1903, settings);
            RegionalWorldState broken = RegionalWorldState.Create(
                source.Seed,
                source.RecipeFamily,
                source.RegionSizeMeters,
                source.SurveyCellSizeMeters,
                source.TerrainTileSizeMeters,
                source.AnchorTown,
                source.TerrainTiles,
                source.Watercourses,
                new List<RegionalRouteCorridorRecord>(),
                source.SurveyParcels,
                source.VegetationZones,
                source.SettlementClusters,
                source.Settlements);

            RegionalFoundationInspectionReport report = broken.BuildInspectionReport();

            Assert.True(report.HasErrors);
            Assert.That(report.Issues.Exists(issue =>
                issue != null
                && issue.Severity == RegionalFoundationIssueSeverity.Error
                && issue.Category == RegionalFoundationIssueCategory.RouteCorridors), Is.True);
            Assert.That(report.BuildCompactReadout(), Does.Contain("errors"));
        }

        [Test]
        public void RegionalWorldSaveDtoRebuildsRouteCorridorsForOlderRegionalSaves()
        {
            RegionalWorldGenerationSettings settings = CreateRegionalSettings(8192f, 8192f);
            RegionalWorldState source = RegionalWorldGenerator.Generate(1903, settings);
            RegionalWorldSaveDto dto = source.CaptureSaveDto();
            dto.routeCorridors.Clear();

            RegionalWorldState restored = RegionalWorldState.FromSaveDto(dto);

            Assert.That(restored.RouteCorridors.Count, Is.GreaterThan(0));
            Assert.That(restored.RouteCorridors.Exists(r => r.Kind == RegionalRouteCorridorKind.OpeningFootholdSpine), Is.True);
            Assert.That(restored.RouteCorridors.Exists(r => r.Points.Count >= 2 && r.LengthMeters > 0f), Is.True);
        }

        [Test]
        public void RegionalWorldSaveDtoRebuildsRouteConstraintMetadataForOlderRegionalSaves()
        {
            RegionalWorldGenerationSettings settings = CreateRegionalSettings(8192f, 8192f);
            RegionalWorldState source = RegionalWorldGenerator.Generate(1903, settings);
            RegionalWorldSaveDto dto = source.CaptureSaveDto();
            for (int i = 0; i < dto.routeCorridors.Count; i++)
            {
                dto.routeCorridors[i].primaryConstraintKind = RegionalRouteConstraintKind.None;
                dto.routeCorridors[i].waterCrossingCount = 0;
                dto.routeCorridors[i].wetGroundCrossingCount = 0;
                dto.routeCorridors[i].bridgeOrFordNeed01 = 0f;
                dto.routeCorridors[i].mudSeasonRisk01 = 0f;
                dto.routeCorridors[i].gradeBurden01 = 0f;
                dto.routeCorridors[i].constraintSummary = string.Empty;
            }

            RegionalWorldState restored = RegionalWorldState.FromSaveDto(dto);

            Assert.That(restored.RouteCorridors.Exists(r => r.PrimaryConstraintKind != RegionalRouteConstraintKind.None), Is.True);
            Assert.That(restored.RouteCorridors.Exists(r => !string.IsNullOrWhiteSpace(r.ConstraintSummary)), Is.True);
            Assert.That(restored.BuildInspectionReport().Issues.Exists(issue => issue != null && issue.Message.Contains("no crossing or seasonal constraint assessment")), Is.False);
        }

        [Test]
        public void RegionalWorldSaveDtoRebuildsPartiallyMissingRouteConstraintMetadata()
        {
            RegionalWorldGenerationSettings settings = CreateRegionalSettings(8192f, 8192f);
            RegionalWorldState source = RegionalWorldGenerator.Generate(1903, settings);
            RegionalWorldSaveDto dto = source.CaptureSaveDto();
            Assert.That(dto.routeCorridors.Count, Is.GreaterThan(1));

            int staleIndex = dto.routeCorridors.Count - 1;
            dto.routeCorridors[staleIndex].primaryConstraintKind = RegionalRouteConstraintKind.None;
            dto.routeCorridors[staleIndex].waterCrossingCount = 0;
            dto.routeCorridors[staleIndex].wetGroundCrossingCount = 0;
            dto.routeCorridors[staleIndex].bridgeOrFordNeed01 = 0f;
            dto.routeCorridors[staleIndex].mudSeasonRisk01 = 0f;
            dto.routeCorridors[staleIndex].gradeBurden01 = 0f;
            dto.routeCorridors[staleIndex].constraintSummary = string.Empty;

            RegionalWorldState restored = RegionalWorldState.FromSaveDto(dto);

            Assert.That(restored.RouteCorridors.Exists(r => r.PrimaryConstraintKind == RegionalRouteConstraintKind.None || string.IsNullOrWhiteSpace(r.ConstraintSummary)), Is.False);
            Assert.That(restored.BuildInspectionReport().Issues.Exists(issue => issue != null && issue.Message.Contains("no crossing or seasonal constraint assessment")), Is.False);
        }

        [Test]
        public void RegionalWorldSaveDtoRebuildsParcelCorridorInfluenceForOlderRegionalSaves()
        {
            RegionalWorldGenerationSettings settings = CreateRegionalSettings(8192f, 8192f);
            RegionalWorldState source = RegionalWorldGenerator.Generate(1903, settings);
            RegionalWorldSaveDto dto = source.CaptureSaveDto();
            for (int i = 0; i < dto.surveyParcels.Count; i++)
            {
                dto.surveyParcels[i].corridorRelation = RegionalParcelCorridorRelation.None;
                dto.surveyParcels[i].nearestCorridorId = string.Empty;
                dto.surveyParcels[i].distanceToCorridorMeters = 0f;
                dto.surveyParcels[i].corridorInfluence01 = 0f;
                dto.surveyParcels[i].freightAccess01 = 0f;
                dto.surveyParcels[i].corridorBurden01 = 0f;
                dto.surveyParcels[i].corridorContext = string.Empty;
            }

            RegionalWorldState restored = RegionalWorldState.FromSaveDto(dto);

            Assert.That(restored.SurveyParcels.Exists(p => p.CorridorRelation != RegionalParcelCorridorRelation.None && p.CorridorRelation != RegionalParcelCorridorRelation.OffRoute), Is.True);
            Assert.That(restored.SurveyParcels.Exists(p => !string.IsNullOrWhiteSpace(p.NearestCorridorId)), Is.True);
            Assert.That(restored.BuildInspectionReport().Issues.Exists(issue => issue != null && issue.Message.Contains("no parcels carry corridor influence")), Is.False);
        }

        [Test]
        public void RegionalWorldSaveDtoRefreshesParcelCorridorContextWhenRouteConstraintsAreNewer()
        {
            RegionalWorldGenerationSettings settings = CreateRegionalSettings(8192f, 8192f);
            RegionalWorldState source = RegionalWorldGenerator.Generate(1903, settings);
            RegionalWorldSaveDto dto = source.CaptureSaveDto();
            int staleLinkedParcels = 0;
            for (int i = 0; i < dto.surveyParcels.Count; i++)
            {
                if (dto.surveyParcels[i].corridorRelation != RegionalParcelCorridorRelation.None
                    && dto.surveyParcels[i].corridorRelation != RegionalParcelCorridorRelation.OffRoute)
                {
                    dto.surveyParcels[i].corridorContext = "old corridor context without seasonal detail";
                    staleLinkedParcels++;
                }
            }

            Assert.That(staleLinkedParcels, Is.GreaterThan(0));
            RegionalWorldState restored = RegionalWorldState.FromSaveDto(dto);

            Assert.That(restored.SurveyParcels.Exists(p =>
                p.CorridorRelation != RegionalParcelCorridorRelation.None
                && p.CorridorRelation != RegionalParcelCorridorRelation.OffRoute
                && p.CorridorContext.Contains("constraint")), Is.True);
        }

        [Test]
        public void RegionalWorldSaveDtoRebuildsTileRouteReadinessForOlderRegionalSaves()
        {
            RegionalWorldGenerationSettings settings = CreateRegionalSettings(8192f, 8192f);
            RegionalWorldState source = RegionalWorldGenerator.Generate(1903, settings);
            RegionalWorldSaveDto dto = source.CaptureSaveDto();
            for (int i = 0; i < dto.terrainTiles.Count; i++)
            {
                dto.terrainTiles[i].routeInfluence01 = 0f;
                dto.terrainTiles[i].routeActivationReadiness01 = 0f;
                dto.terrainTiles[i].freightBurdenInfluence01 = 0f;
                dto.terrainTiles[i].nearestRouteCorridorId = string.Empty;
            }

            RegionalWorldState restored = RegionalWorldState.FromSaveDto(dto);

            Assert.That(restored.TerrainTiles.Exists(t => t.RouteInfluence01 > 0.05f), Is.True);
            Assert.That(restored.TerrainTiles.Exists(t => !string.IsNullOrWhiteSpace(t.NearestRouteCorridorId)), Is.True);
            Assert.That(restored.BuildInspectionReport().Issues.Exists(issue => issue != null && issue.Message.Contains("no terrain tiles carry route influence")), Is.False);
        }

        [Test]
        public void RegionalWorldSaveDtoRebuildsPartiallyMissingTileRouteReadiness()
        {
            RegionalWorldGenerationSettings settings = CreateRegionalSettings(8192f, 8192f);
            RegionalWorldState source = RegionalWorldGenerator.Generate(1903, settings);
            RegionalWorldSaveDto dto = source.CaptureSaveDto();
            RegionalTerrainTileSaveDto staleTile = dto.terrainTiles.Find(tile => tile != null && tile.routeInfluence01 > 0.05f && tile.activationReadiness01 > 0.05f);
            Assert.That(staleTile, Is.Not.Null);
            string staleTileId = staleTile.tileId;
            staleTile.routeInfluence01 = 0f;
            staleTile.routeActivationReadiness01 = 0f;
            staleTile.freightBurdenInfluence01 = 0f;
            staleTile.nearestRouteCorridorId = string.Empty;

            RegionalWorldState restored = RegionalWorldState.FromSaveDto(dto);
            RegionalTerrainTileRecord restoredTile = restored.TerrainTiles.Find(tile => tile.TileId == staleTileId);

            Assert.That(restoredTile, Is.Not.Null);
            Assert.That(restoredTile.RouteInfluence01, Is.GreaterThan(0.05f));
            Assert.That(restoredTile.NearestRouteCorridorId, Is.Not.Empty);
        }

        [Test]
        public void RegionalWorldSaveDtoRebuildsSettlementHierarchyForOlderRegionalSaves()
        {
            RegionalWorldGenerationSettings settings = CreateRegionalSettings(8192f, 8192f);
            RegionalWorldState source = RegionalWorldGenerator.Generate(1903, settings);
            RegionalWorldSaveDto dto = source.CaptureSaveDto();
            for (int i = 0; i < dto.settlements.Count; i++)
            {
                dto.settlements[i].successionRole = RegionalSettlementSuccessionRole.Unset;
                dto.settlements[i].regionalHierarchyRank = 0;
                dto.settlements[i].regionalGravity01 = 0f;
                dto.settlements[i].footholdChallenge01 = 0f;
                dto.settlements[i].successionReadiness01 = 0f;
                dto.settlements[i].successionContext = string.Empty;
            }

            RegionalWorldState restored = RegionalWorldState.FromSaveDto(dto);

            Assert.That(restored.Settlements.Exists(s => s.SuccessionRole == RegionalSettlementSuccessionRole.OpeningFoothold), Is.True);
            Assert.That(restored.Settlements.Exists(s => s.RegionalHierarchyRank > 0), Is.True);
            Assert.That(restored.BuildInspectionReport().Issues.Exists(issue => issue != null && issue.Message.Contains("succession role is unset")), Is.False);
        }

        [Test]
        public void RegionalWorldSaveDtoRoundTripsGenerationIdentityAndDerivedRecords()
        {
            RegionalWorldGenerationSettings settings = CreateRegionalSettings(8192f, 8192f);
            RegionalWorldState source = RegionalWorldGenerator.Generate(1903, settings);

            RegionalWorldSaveDto dto = source.CaptureSaveDto();
            RegionalWorldState restored = RegionalWorldState.FromSaveDto(dto);

            CollectionAssert.AreEqual(CaptureSnapshot(source), CaptureSnapshot(restored));
        }

        [Test]
        public void TownWorldSaveLoadCarriesRegionalFoundationState()
        {
            List<Object> cleanup = new();
            try
            {
                BuildingDefinition definition = CreateDefinition();
                cleanup.Add(definition);
                TownGenerationSettings settings = CreateTownSettings(definition, 1886);
                cleanup.Add(settings);

                GameObject sourceObject = new("Regional Save Source");
                cleanup.Add(sourceObject);
                TownWorldController source = sourceObject.AddComponent<TownWorldController>();
                source.Configure(settings, null, null);
                source.GenerateTownShell();

                string inspectionSummary = source.BuildRegionalFoundationInspectionSummary();
                Assert.That(inspectionSummary, Does.Contain("Regional foundation check"));
                Assert.That(inspectionSummary, Does.Contain("route-ready tiles"));
                Assert.That(inspectionSummary, Does.Contain("Regional opportunities"));
                Assert.That(inspectionSummary, Does.Contain("Regional debug"));

                WorldSaveDto dto = source.CaptureSaveDto();
                Assert.NotNull(dto.regionalWorld);
                Assert.That(dto.regionalWorld.seed, Is.EqualTo(1886));
                Assert.That(dto.regionalWorld.surveyParcels.Count, Is.GreaterThan(0));

                GameObject targetObject = new("Regional Save Target");
                cleanup.Add(targetObject);
                TownWorldController target = targetObject.AddComponent<TownWorldController>();
                target.Configure(settings, null, null);

                Assert.IsTrue(target.LoadFromSaveDto(dto, new SaveReferenceResolver(source), out string message), message);
                CollectionAssert.AreEqual(
                    CaptureSnapshot(source.RegionalWorld),
                    CaptureSnapshot(target.RegionalWorld));
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        private static RegionalWorldGenerationSettings CreateRegionalSettings(float widthMeters, float depthMeters)
        {
            return new RegionalWorldGenerationSettings
            {
                regionWidthMeters = widthMeters,
                regionDepthMeters = depthMeters,
                terrainTileSizeMeters = 1024f,
                surveyCellSizeMeters = 2f,
                sectionSizeMeters = 1609.344f,
                initialHouseholdCount = 18,
                initialBusinessCount = 6
            };
        }

        private static TownGenerationSettings CreateTownSettings(BuildingDefinition definition, int seed)
        {
            TownGenerationSettings settings = ScriptableObject.CreateInstance<TownGenerationSettings>();
            settings.gridWidthCells = 96;
            settings.gridDepthCells = 96;
            settings.cellSizeMeters = 2f;
            settings.seed = seed;
            settings.roadWidthCells = 3;
            settings.mainStreetLengthCells = 60;
            settings.crossStreetCount = 0;
            settings.plotDepthCells = 8;
            settings.minPlotFrontageCells = 8;
            settings.maxPlotFrontageCells = 8;
            settings.generateRegionalFoundation = true;
            settings.regionWidthMeters = 8192f;
            settings.regionDepthMeters = 8192f;
            settings.regionalTerrainTileSizeMeters = 1024f;
            settings.generateAgriculturalParcels = false;
            settings.generateSawmillProperty = false;
            settings.generateTownHall = false;
            settings.generateDetailProps = false;
            settings.generateForestEnvironmentDressing = false;
            settings.showTerrainOverlay = false;
            settings.showRoads = false;
            settings.showPlots = false;
            settings.showBuildings = false;
            settings.buildingCatalog = new[] { definition };
            settings.Sanitize();
            return settings;
        }

        private static BuildingDefinition CreateDefinition()
        {
            BuildingDefinition definition = ScriptableObject.CreateInstance<BuildingDefinition>();
            definition.ConfigureRuntimeFallback(
                "test_regional_shell",
                "Test Regional Shell",
                PlotZone.Business,
                new Vector2Int(4, 4),
                Color.white,
                4f);
            return definition;
        }

        private static List<string> CaptureSnapshot(RegionalWorldState state)
        {
            List<string> lines = new();
            if (state == null)
            {
                return lines;
            }

            lines.Add($"R|{state.Seed}|{state.RecipeFamily}|{state.RegionSizeMeters.x:F0}|{state.RegionSizeMeters.y:F0}|{state.AnchorTown.CenterMeters.x:F1}|{state.AnchorTown.CenterMeters.y:F1}|{state.AnchorTown.SuitabilityScore01:F3}");
            for (int i = 0; i < state.TerrainTiles.Count; i++)
            {
                RegionalTerrainTileRecord tile = state.TerrainTiles[i];
                lines.Add($"T|{tile.TileId}|{tile.BoundsMeters.x:F0}|{tile.BoundsMeters.y:F0}|{tile.BoundsMeters.width:F0}|{tile.BoundsMeters.height:F0}|{tile.MeanElevationMeters:F2}|{tile.Active}|{tile.ActivationReadiness01:F2}|{tile.SettlementInfluence01:F2}|{tile.DominantBiomeKind}|{tile.RouteInfluence01:F2}|{tile.RouteActivationReadiness01:F2}|{tile.FreightBurdenInfluence01:F2}|{tile.NearestRouteCorridorId}");
            }

            for (int i = 0; i < state.Watercourses.Count; i++)
            {
                RegionalWatercourseRecord water = state.Watercourses[i];
                lines.Add($"W|{water.WatercourseId}|{water.Kind}|{water.InfluenceWidthMeters:F0}|{water.Points.Count}|{water.Points[0].x:F1}|{water.Points[0].y:F1}");
            }

            for (int i = 0; i < state.RouteCorridors.Count; i++)
            {
                RegionalRouteCorridorRecord route = state.RouteCorridors[i];
                lines.Add($"RC|{route.CorridorId}|{route.Kind}|{route.SourceSettlementId}|{route.DestinationSettlementId}|{route.DestinationParcelId}|{route.LengthMeters:F0}|{route.Practicality01:F2}|{route.SeasonalReliability01:F2}|{route.FreightBurden01:F2}|{route.PrimaryConstraintKind}|{route.WaterCrossingCount}|{route.WetGroundCrossingCount}|{route.BridgeOrFordNeed01:F2}|{route.MudSeasonRisk01:F2}|{route.GradeBurden01:F2}|{route.Points.Count}");
            }

            for (int i = 0; i < Mathf.Min(28, state.SurveyParcels.Count); i++)
            {
                RegionalSurveyParcelRecord parcel = state.SurveyParcels[i];
                lines.Add($"P|{parcel.ParcelId}|{parcel.SectionId}|{parcel.ParcelKind}|{parcel.Acreage:F1}|{parcel.WaterAccess01:F2}|{parcel.FarmSuitability01:F2}|{parcel.ResourceSuitability01:F2}|{parcel.FrontageClass}|{parcel.AccessQuality}|{parcel.Provenance}|{parcel.RemoteSuitability}|{parcel.DevelopmentReadiness01:F2}|{parcel.CorridorRelation}|{parcel.NearestCorridorId}|{parcel.DistanceToCorridorMeters:F0}|{parcel.FreightAccess01:F2}|{parcel.CorridorBurden01:F2}");
            }

            for (int i = 0; i < state.SettlementClusters.Count; i++)
            {
                RegionalSettlementClusterRecord cluster = state.SettlementClusters[i];
                lines.Add($"C|{cluster.ClusterId}|{cluster.Population}|{cluster.Character}|{cluster.Permanence01:F2}|{cluster.LocalConfidence01:F2}|{cluster.ServiceDeficit01:F2}|{cluster.DeclineRisk01:F2}");
            }

            for (int i = 0; i < state.Settlements.Count; i++)
            {
                RegionalSettlementRecord settlement = state.Settlements[i];
                lines.Add($"S|{settlement.SettlementId}|{settlement.SourceClusterId}|{settlement.Label}|{settlement.PopulationBand}|{settlement.Population}|{settlement.CenterMeters.x:F1}|{settlement.CenterMeters.y:F1}|{settlement.SupportBurden01:F2}|{settlement.Character}|{settlement.Permanence}|{settlement.SuccessionRole}|{settlement.RegionalHierarchyRank}|{settlement.RegionalGravity01:F2}|{settlement.FootholdChallenge01:F2}|{settlement.SuccessionReadiness01:F2}|{settlement.DeclineRisk01:F2}");
            }

            return lines;
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
