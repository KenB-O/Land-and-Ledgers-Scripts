using System;
using System.Collections.Generic;
using System.Linq;
using LandLedgers.Persistence;
using LandLedgers.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LandLedgers.Editor.World
{
    public sealed class TownWorldExpansionTests
    {
        [Test]
        public void DefaultTownShellUsesLargerFrontierFootprintAndMoreRoads()
        {
            using TestTown town = TestTown.CreateFromDefaultSettings();

            Assert.GreaterOrEqual(town.Settings.gridWidthCells, 220);
            Assert.GreaterOrEqual(town.Settings.gridDepthCells, 200);
            Assert.GreaterOrEqual(town.Settings.mainStreetLengthCells, 150);
            Assert.GreaterOrEqual(town.Settings.crossStreetCount, 3);
            Assert.GreaterOrEqual(town.Settings.crossStreetLengthCells, 116);
            Assert.Greater(CountRoadCells(town.Controller), 1200);
        }

        [Test]
        public void GeneratedTownHasRegionalExitRoadsOnMapBoundary()
        {
            using TestTown town = TestTown.CreateFromDefaultSettings();
            TownGrid grid = town.Controller.Grid;

            bool foundRegionalExitOnBoundary = false;
            for (int x = 0; x < grid.Width; x++)
            {
                foundRegionalExitOnBoundary |= IsRegionalExit(grid, new GridCoord(x, 0));
                foundRegionalExitOnBoundary |= IsRegionalExit(grid, new GridCoord(x, grid.Depth - 1));
            }

            for (int z = 0; z < grid.Depth; z++)
            {
                foundRegionalExitOnBoundary |= IsRegionalExit(grid, new GridCoord(0, z));
                foundRegionalExitOnBoundary |= IsRegionalExit(grid, new GridCoord(grid.Width - 1, z));
            }

            Assert.IsTrue(foundRegionalExitOnBoundary);
        }

        [Test]
        public void GeneratedTownPlacesRoadConnectedEdgeResidentialHouses()
        {
            using TestTown town = TestTown.CreateFromDefaultSettings();

            int edgeHouseCount = town.Controller.Buildings.Count(building =>
            {
                TownPlot plot = town.Controller.Plots[building.plotId];
                return building.definition != null
                    && building.definition.CanHostHouseholds
                    && plot.zone == PlotZone.Residential
                    && IsEdgeHousePlot(town.Controller.Grid, town.Settings, plot);
            });

            Assert.GreaterOrEqual(edgeHouseCount, 1);
            Assert.LessOrEqual(edgeHouseCount, Mathf.Max(2, town.Settings.edgeHouseCount));
        }

        [Test]
        public void GeneratedTownPlotsHaveRoadAccessAndDoNotOverlap()
        {
            using TestTown town = TestTown.CreateFromDefaultSettings();
            IReadOnlyList<TownPlot> plots = town.Controller.Plots;

            for (int i = 0; i < plots.Count; i++)
            {
                TownPlot plot = plots[i];
                Assert.IsTrue(town.Controller.Grid.IsInBounds(plot.roadAccessCell), $"Plot {plot.id:000} access is out of bounds.");
                Assert.IsTrue(town.Controller.Grid.GetCell(plot.roadAccessCell).IsRoad, $"Plot {plot.id:000} has no road access.");

                for (int j = i + 1; j < plots.Count; j++)
                {
                    Assert.IsFalse(plot.bounds.Overlaps(plots[j].bounds), $"Plot {plot.id:000} overlaps plot {plots[j].id:000}.");
                }
            }
        }

        [Test]
        public void LandPurchaseExpansionSurfacesOneOrTwoReservedEdgePlots()
        {
            using TestTown town = TestTown.CreateFromDefaultSettings();
            int purchasedPlotId = FindVacantLandPlot(town.Controller);
            int initialPlotCount = town.Controller.Plots.Count;

            town.Controller.Plots[purchasedPlotId].playerOwned = true;
            Assert.IsTrue(town.Controller.TrySurfaceExpansionPlotsAfterLandPurchase(purchasedPlotId, out int createdCount));

            Assert.That(createdCount, Is.InRange(1, 2));
            Assert.AreEqual(initialPlotCount + createdCount, town.Controller.Plots.Count);

            for (int i = initialPlotCount; i < town.Controller.Plots.Count; i++)
            {
                TownPlot plot = town.Controller.Plots[i];
                Assert.AreEqual(-1, plot.buildingId);
                Assert.IsTrue(plot.reservedForLandSale);
                Assert.IsFalse(plot.playerOwned);
                Assert.IsTrue(town.Controller.Grid.GetCell(plot.roadAccessCell).IsRoad);
                Assert.AreEqual(RoadType.RegionalExit, town.Controller.Grid.GetCell(plot.roadAccessCell).roadType);
                Assert.IsTrue(IsNearMapEdge(town.Controller.Grid, plot));
            }
        }

        [Test]
        public void LandPurchaseExpansionPlotsSurviveWorldSaveLoad()
        {
            using TestTown source = TestTown.CreateFromDefaultSettings();
            int purchasedPlotId = FindVacantLandPlot(source.Controller);
            int initialPlotCount = source.Controller.Plots.Count;

            source.Controller.Plots[purchasedPlotId].playerOwned = true;
            Assert.IsTrue(source.Controller.TrySurfaceExpansionPlotsAfterLandPurchase(purchasedPlotId, out int createdCount));

            WorldSaveDto save = source.Controller.CaptureSaveDto();
            using TestTown restored = TestTown.CreateEmptyFromSettings(source.Settings);
            SaveReferenceResolver resolver = new(restored.Controller);
            Assert.IsTrue(restored.Controller.LoadFromSaveDto(save, resolver, out string message), message);

            Assert.AreEqual(initialPlotCount + createdCount, restored.Controller.Plots.Count);
            for (int i = initialPlotCount; i < restored.Controller.Plots.Count; i++)
            {
                TownPlot plot = restored.Controller.Plots[i];
                Assert.IsTrue(plot.reservedForLandSale);
                Assert.AreEqual(-1, plot.buildingId);
                Assert.IsTrue(restored.Controller.Grid.GetCell(plot.roadAccessCell).IsRoad);
            }
        }

        [Test]
        public void RegionalWorldGeneratedSaveLoadPreservesLayersAndReadouts()
        {
            using TestTown town = TestTown.CreateFromDefaultSettings();
            RegionalWorldState source = town.Controller.RegionalWorld;
            Assert.NotNull(source, "Regional foundation should be generated for the default town settings.");
            Assert.Greater(source.TerrainTiles.Count, 0, "Regional terrain tiles should be present before save/load.");
            Assert.Greater(source.RouteCorridors.Count, 0, "Regional route corridors should be present before save/load.");
            Assert.Greater(source.SurveyParcels.Count, 0, "Regional survey parcels should be present before save/load.");
            Assert.Greater(source.Settlements.Count, 0, "Regional settlement nodes should be present before save/load.");

            string sourceFoundationSummary = source.BuildRegionalFoundationInspectionSummary(3, town.Settings.BuildRegionalDebugSurfaceSummary());
            string sourceGameplaySummary = source.BuildRegionalGameplayReadinessSummary(3);
            string sourceDeveloperReport = source.BuildRegionalFoundationDeveloperReport(3);
            string sourceNearestParcelReadout = source.BuildNearestParcelInspectionReadout(source.AnchorTown.CenterMeters, 20000f);

            RegionalWorldSaveDto save = source.CaptureSaveDto();
            RegionalWorldState restored = RegionalWorldState.FromSaveDto(save);

            AssertRegionalWorldCoreStateEqual(source, restored);
            Assert.AreEqual(sourceFoundationSummary, restored.BuildRegionalFoundationInspectionSummary(3, town.Settings.BuildRegionalDebugSurfaceSummary()));
            Assert.AreEqual(sourceGameplaySummary, restored.BuildRegionalGameplayReadinessSummary(3));
            Assert.AreEqual(sourceDeveloperReport, restored.BuildRegionalFoundationDeveloperReport(3));
            Assert.AreEqual(sourceNearestParcelReadout, restored.BuildNearestParcelInspectionReadout(restored.AnchorTown.CenterMeters, 20000f));

            AssertRepresentativeRegionalLayerReadoutsSurviveSaveLoad(source, restored);
        }

        [Test]
        public void RegionalParcelAuthoritySurfacesOwnerPostureFlexibleUseAndOpportunityContext()
        {
            RegionalSurveyParcelRecord townEdge = new(
                "edge-001",
                "section-a",
                "tract-edge",
                RegionalParcelKind.TownPlatEdge,
                new Rect(100f, 100f, 120f, 100f),
                3f,
                0.12f,
                0.08f,
                0.20f,
                0.32f,
                0.76f,
                0.20f,
                0.18f,
                RegionalParcelFrontageClass.TownMainStreet,
                RegionalParcelAccessQuality.RoadFrontage,
                RegionalParcelProvenance.FounderOrSpeculatorReserve,
                RegionalRemoteSuitability.None,
                0.12f,
                0.82f,
                "town edge",
                "good near-term edge parcel",
                RegionalParcelCorridorRelation.NearOpeningSpine,
                "route-main",
                RegionalRouteCorridorKind.OpeningFootholdSpine,
                20f,
                0.72f,
                0.70f,
                0.12f,
                "opening spine frontage");

            RegionalSurveyParcelRecord mineral = new(
                "claim-001",
                "section-b",
                "tract-claim",
                RegionalParcelKind.RoughParcel,
                new Rect(900f, 860f, 180f, 160f),
                7f,
                0.70f,
                0.20f,
                0.40f,
                0.18f,
                0.28f,
                0.90f,
                0.80f,
                RegionalParcelFrontageClass.RemoteWorksiteAccess,
                RegionalParcelAccessQuality.Poor,
                RegionalParcelProvenance.RemoteIndustrialClaim,
                RegionalRemoteSuitability.MineralProspect,
                0.75f,
                0.32f,
                "upland mineral belt",
                "claim hook with poor access",
                RegionalParcelCorridorRelation.NearRemoteWorksiteTrack,
                "route-claim",
                RegionalRouteCorridorKind.RemoteWorksiteTrack,
                420f,
                0.20f,
                0.22f,
                0.82f,
                "remote rough track");

            RegionalWorldState state = RegionalWorldState.Create(
                17,
                RegionalTerrainRecipeFamily.PlainsToHillsInterface,
                new Vector2(2000f, 2000f),
                160f,
                320f,
                RegionalAnchorTownRecord.Create("anchor", new Vector2(500f, 500f), 0.70f, 80f, 0.64f, 0.58f, 0.62f, "test anchor"),
                new List<RegionalTerrainTileRecord>(),
                new List<RegionalWatercourseRecord>(),
                new List<RegionalRouteCorridorRecord>(),
                new List<RegionalSurveyParcelRecord> { townEdge, mineral },
                new List<RegionalVegetationZoneRecord>(),
                new List<RegionalSettlementClusterRecord>(),
                new List<RegionalSettlementRecord>());

            RegionalParcelAuthorityReadout townReadout = RegionalParcelAuthority.Evaluate(state, townEdge);
            Assert.AreEqual(RegionalParcelOwnerActionPosture.NearTermInspect, townReadout.OwnerActionPosture);
            StringAssert.Contains("not hard-zoned", townReadout.FlexibleUseReadout);
            StringAssert.Contains("action near-term inspect", townReadout.BuildCompactReadout());

            RegionalParcelAuthorityReadout mineralReadout = RegionalParcelAuthority.Evaluate(state, mineral);
            Assert.AreEqual(RegionalParcelOwnerActionPosture.PrepareInfrastructure, mineralReadout.OwnerActionPosture);
            StringAssert.Contains("poor access", mineralReadout.ConstraintReadout);
            StringAssert.Contains("high route constraint burden", mineralReadout.ConstraintReadout);
            StringAssert.Contains("not a guaranteed mine", mineralReadout.FlexibleUseReadout);
            CollectionAssert.Contains(mineralReadout.FutureSystemHooks.ToList(), "mining/claims");

            string readiness = state.BuildRegionalGameplayReadinessSummary(3);
            StringAssert.Contains("action near-term inspect", readiness);
            StringAssert.Contains("not hard-zoned", readiness);
            StringAssert.Contains("prepare infrastructure first", readiness);

            string opportunities = state.BuildInspectionReport().BuildOpportunityDigest(3);
            StringAssert.Contains("Parcel authority:", opportunities);
            StringAssert.Contains("action near-term inspect", opportunities);
            StringAssert.Contains("prepare infrastructure first", opportunities);
        }

        private static int CountRoadCells(TownWorldController controller)
        {
            int count = 0;
            TownGrid grid = controller.Grid;
            for (int x = 0; x < grid.Width; x++)
            {
                for (int z = 0; z < grid.Depth; z++)
                {
                    if (grid.GetCell(new GridCoord(x, z)).IsRoad)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static void AssertRegionalWorldCoreStateEqual(RegionalWorldState expected, RegionalWorldState actual)
        {
            Assert.NotNull(actual);
            Assert.AreEqual(expected.Seed, actual.Seed);
            Assert.AreEqual(expected.RecipeFamily, actual.RecipeFamily);
            Assert.AreEqual(expected.RegionSizeMeters.x, actual.RegionSizeMeters.x, 0.001f);
            Assert.AreEqual(expected.RegionSizeMeters.y, actual.RegionSizeMeters.y, 0.001f);
            Assert.AreEqual(expected.SurveyCellSizeMeters, actual.SurveyCellSizeMeters, 0.001f);
            Assert.AreEqual(expected.TerrainTileSizeMeters, actual.TerrainTileSizeMeters, 0.001f);
            Assert.AreEqual(expected.AnchorTown.AnchorId, actual.AnchorTown.AnchorId);
            Assert.AreEqual(expected.AnchorTown.CenterMeters.x, actual.AnchorTown.CenterMeters.x, 0.001f);
            Assert.AreEqual(expected.AnchorTown.CenterMeters.y, actual.AnchorTown.CenterMeters.y, 0.001f);
            Assert.AreEqual(expected.TerrainTiles.Count, actual.TerrainTiles.Count);
            Assert.AreEqual(expected.Watercourses.Count, actual.Watercourses.Count);
            Assert.AreEqual(expected.RouteCorridors.Count, actual.RouteCorridors.Count);
            Assert.AreEqual(expected.SurveyParcels.Count, actual.SurveyParcels.Count);
            Assert.AreEqual(expected.VegetationZones.Count, actual.VegetationZones.Count);
            Assert.AreEqual(expected.SettlementClusters.Count, actual.SettlementClusters.Count);
            Assert.AreEqual(expected.Settlements.Count, actual.Settlements.Count);
        }

        private static void AssertRepresentativeRegionalLayerReadoutsSurviveSaveLoad(RegionalWorldState expected, RegionalWorldState actual)
        {
            if (expected.TerrainTiles.Count > 0)
            {
                Assert.AreEqual(expected.TerrainTiles[0].BuildReadinessSummary(), actual.TerrainTiles[0].BuildReadinessSummary());
            }

            if (expected.Watercourses.Count > 0)
            {
                Assert.AreEqual(expected.Watercourses[0].WatercourseId, actual.Watercourses[0].WatercourseId);
                Assert.AreEqual(expected.Watercourses[0].Points.Count, actual.Watercourses[0].Points.Count);
            }

            if (expected.RouteCorridors.Count > 0)
            {
                Assert.AreEqual(expected.RouteCorridors[0].BuildRoutePlanningReadout(), actual.RouteCorridors[0].BuildRoutePlanningReadout());
                Assert.AreEqual(expected.RouteCorridors[0].BuildDetailedRouteInspectionReadout(), actual.RouteCorridors[0].BuildDetailedRouteInspectionReadout());
            }

            if (expected.SurveyParcels.Count > 0)
            {
                Assert.AreEqual(expected.SurveyParcels[0].ParcelId, actual.SurveyParcels[0].ParcelId);
                Assert.AreEqual(expected.SurveyParcels[0].ParcelKind, actual.SurveyParcels[0].ParcelKind);
                Assert.AreEqual(expected.SurveyParcels[0].BuildParcelInspectionReadout(), actual.SurveyParcels[0].BuildParcelInspectionReadout());
            }

            if (expected.VegetationZones.Count > 0)
            {
                Assert.AreEqual(expected.VegetationZones[0].Notes, actual.VegetationZones[0].Notes);
                Assert.AreEqual(expected.VegetationZones[0].VegetationKind, actual.VegetationZones[0].VegetationKind);
            }

            if (expected.SettlementClusters.Count > 0)
            {
                Assert.AreEqual(expected.SettlementClusters[0].ClusterId, actual.SettlementClusters[0].ClusterId);
                Assert.AreEqual(expected.SettlementClusters[0].DebugReason, actual.SettlementClusters[0].DebugReason);
            }

            if (expected.Settlements.Count > 0)
            {
                Assert.AreEqual(expected.Settlements[0].BuildVisiblePlanningReadout(), actual.Settlements[0].BuildVisiblePlanningReadout());
                Assert.AreEqual(expected.Settlements[0].BuildDetailedSettlementInspectionReadout(), actual.Settlements[0].BuildDetailedSettlementInspectionReadout());
            }
        }

        private static bool IsRegionalExit(TownGrid grid, GridCoord coord)
        {
            TownCell cell = grid.GetCell(coord);
            return cell.IsRoad && cell.roadType == RoadType.RegionalExit;
        }

        private static bool IsEdgeHousePlot(TownGrid grid, TownGenerationSettings settings, TownPlot plot)
        {
            int edgeBand = Mathf.Max(settings.plotDepthCells + settings.maxPlotFrontageCells + 4, grid.Width / 5);
            bool nearEastWestEdge = plot.bounds.xMin <= edgeBand || plot.bounds.xMaxInclusive >= grid.Width - edgeBand - 1;
            bool centralRoadBand = Mathf.Abs(plot.roadAccessCell.z - grid.Depth / 2) <= settings.roadWidthCells;
            return nearEastWestEdge && centralRoadBand;
        }

        private static bool IsNearMapEdge(TownGrid grid, TownPlot plot)
        {
            int edgeDistance = Mathf.Min(
                Mathf.Min(plot.bounds.xMin, grid.Width - 1 - plot.bounds.xMaxInclusive),
                Mathf.Min(plot.bounds.zMin, grid.Depth - 1 - plot.bounds.zMaxInclusive));
            return edgeDistance <= Mathf.Max(12, Mathf.Min(grid.Width, grid.Depth) / 5);
        }

        private static int FindVacantLandPlot(TownWorldController controller)
        {
            for (int i = 0; i < controller.Plots.Count; i++)
            {
                TownPlot plot = controller.Plots[i];
                if (plot != null && !plot.playerOwned && plot.buildingId < 0 && plot.zone != PlotZone.Agricultural)
                {
                    return plot.id;
                }
            }

            Assert.Fail("No vacant non-agricultural plot found.");
            return -1;
        }

        private sealed class TestTown : IDisposable
        {
            private readonly GameObject root;
            private readonly TownGenerationSettings settings;

            private TestTown(GameObject root, TownGenerationSettings settings)
            {
                this.root = root;
                this.settings = settings;
                Controller = root.AddComponent<TownWorldController>();
                Controller.Configure(settings, null, null);
            }

            public TownWorldController Controller { get; }
            public TownGenerationSettings Settings => settings;

            public static TestTown CreateFromDefaultSettings()
            {
                TownGenerationSettings sourceSettings = AssetDatabase.LoadAssetAtPath<TownGenerationSettings>("Assets/Core/World/DefaultTownGenerationSettings.asset");
                Assert.NotNull(sourceSettings);
                TownGenerationSettings settings = UnityEngine.Object.Instantiate(sourceSettings);
                ConfigureForFastTests(settings);
                TestTown town = new(new GameObject("Town World Expansion Test"), settings);
                town.Controller.GenerateTownShell();
                return town;
            }

            public static TestTown CreateEmptyFromSettings(TownGenerationSettings sourceSettings)
            {
                TownGenerationSettings settings = UnityEngine.Object.Instantiate(sourceSettings);
                ConfigureForFastTests(settings);
                return new TestTown(new GameObject("Town World Expansion Load Test"), settings);
            }

            public void Dispose()
            {
                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }

                if (settings != null)
                {
                    UnityEngine.Object.DestroyImmediate(settings);
                }
            }

            private static void ConfigureForFastTests(TownGenerationSettings settings)
            {
                settings.showTerrainOverlay = false;
                settings.showRoads = false;
                settings.showPlots = false;
                settings.showBuildings = false;
                settings.generateForestEnvironmentDressing = false;
                settings.Sanitize();
            }
        }
    }
}
