using System.Collections.Generic;
using LandLedgers.Persistence;
using LandLedgers.World;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace LandLedgers.Editor.World
{
    public sealed class TownWorldMineralScaffoldTests
    {
        [Test]
        public void MineralScaffoldIsDeterministicForSameSeed()
        {
            List<Object> cleanup = new();
            try
            {
                BuildingDefinition definition = CreateDefinition();
                cleanup.Add(definition);

                TownGenerationSettings settings = CreateSettings(definition, 1897);
                cleanup.Add(settings);

                TownWorldController first = CreateController(settings, "Mineral Determinism A", cleanup);
                first.GenerateTownShell();
                List<string> firstSnapshot = CaptureSnapshot(first.RegionalResources);

                TownWorldController second = CreateController(settings, "Mineral Determinism B", cleanup);
                second.GenerateTownShell();
                List<string> secondSnapshot = CaptureSnapshot(second.RegionalResources);

                Assert.That(firstSnapshot, Is.Not.Empty);
                CollectionAssert.AreEqual(firstSnapshot, secondSnapshot);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void MineralScaffoldChangesAcrossDifferentSeeds()
        {
            List<Object> cleanup = new();
            try
            {
                BuildingDefinition definition = CreateDefinition();
                cleanup.Add(definition);

                TownGenerationSettings firstSettings = CreateSettings(definition, 1897);
                TownGenerationSettings secondSettings = CreateSettings(definition, 1901);
                cleanup.Add(firstSettings);
                cleanup.Add(secondSettings);

                TownWorldController first = CreateController(firstSettings, "Mineral Seed A", cleanup);
                first.GenerateTownShell();

                TownWorldController second = CreateController(secondSettings, "Mineral Seed B", cleanup);
                second.GenerateTownShell();

                CollectionAssert.AreNotEqual(
                    CaptureSnapshot(first.RegionalResources),
                    CaptureSnapshot(second.RegionalResources));
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void MineralScaffoldExposesAllStartingResourceFamiliesAndProtoSites()
        {
            List<Object> cleanup = new();
            try
            {
                BuildingDefinition definition = CreateDefinition();
                cleanup.Add(definition);

                TownGenerationSettings settings = CreateSettings(definition, 1886);
                cleanup.Add(settings);

                TownWorldController controller = CreateController(settings, "Mineral Coverage", cleanup);
                controller.GenerateTownShell();

                RegionalResourceSnapshot snapshot = controller.RegionalResources;
                Assert.NotNull(snapshot);
                Assert.That(snapshot.Districts.Count, Is.GreaterThan(0));
                Assert.That(snapshot.RemoteSites.Count, Is.GreaterThan(0));

                Assert.That(snapshot.GetDistrictCount(MineralResourceKind.Coal), Is.GreaterThan(0));
                Assert.That(snapshot.GetDistrictCount(MineralResourceKind.Iron), Is.GreaterThan(0));
                Assert.That(snapshot.GetDistrictCount(MineralResourceKind.Gold), Is.GreaterThan(0));
                Assert.That(snapshot.GetDistrictCount(MineralResourceKind.Silver), Is.GreaterThan(0));

                for (int i = 0; i < snapshot.RemoteSites.Count; i++)
                {
                    RemoteIndustrySiteRecord site = snapshot.RemoteSites[i];
                    Assert.That(site.LinkedDistrictIds.Count, Is.GreaterThan(0), site.DebugLabel);
                    Assert.That(site.SiteStrength01, Is.GreaterThan(0.01f), site.DebugLabel);
                }
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void MineralScaffoldSaveLoadRegeneratesDeterministicallyFromSeed()
        {
            List<Object> cleanup = new();
            try
            {
                BuildingDefinition definition = CreateDefinition();
                cleanup.Add(definition);

                TownGenerationSettings settings = CreateSettings(definition, 1886);
                cleanup.Add(settings);

                GameObject sourceObject = new("Mineral Save Source");
                cleanup.Add(sourceObject);
                TownWorldController source = sourceObject.AddComponent<TownWorldController>();
                source.Configure(settings, null, null);
                source.GenerateTownShell();

                WorldSaveDto dto = source.CaptureSaveDto();

                SaveReferenceResolver resolver = new(source);

                GameObject targetObject = new("Mineral Save Target");
                cleanup.Add(targetObject);
                TownWorldController target = targetObject.AddComponent<TownWorldController>();

                Assert.IsTrue(target.LoadFromSaveDto(dto, resolver, out string message), message);
                CollectionAssert.AreEqual(
                    CaptureSnapshot(source.RegionalResources),
                    CaptureSnapshot(target.RegionalResources));
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void MineralScaffoldQuerySummariesRemainClampedAndRoleWeighted()
        {
            List<Object> cleanup = new();
            try
            {
                BuildingDefinition definition = CreateDefinition();
                cleanup.Add(definition);

                TownGenerationSettings settings = CreateSettings(definition, 1886);
                cleanup.Add(settings);

                TownWorldController controller = CreateController(settings, "Mineral Query Summary", cleanup);
                controller.GenerateTownShell();

                RegionalResourceSnapshot snapshot = controller.RegionalResources;
                Assert.NotNull(snapshot);
                Assert.That(snapshot.TotalFreightPressure01, Is.InRange(0f, 1f));
                Assert.That(snapshot.TotalSettlementPressure01, Is.InRange(0f, 1f));
                Assert.That(snapshot.TotalRemoteDevelopmentPressure01, Is.InRange(0f, 1f));

                MineralDistrictRecord coal = snapshot.GetStrongestDistrict(MineralResourceKind.Coal);
                MineralDistrictRecord iron = snapshot.GetStrongestDistrict(MineralResourceKind.Iron);
                MineralDistrictRecord gold = snapshot.GetStrongestDistrict(MineralResourceKind.Gold);
                MineralDistrictRecord silver = snapshot.GetStrongestDistrict(MineralResourceKind.Silver);

                Assert.That(coal.IndustrialBackboneSignificance01, Is.GreaterThan(coal.ExportSpeculationSignificance01));
                Assert.That(iron.IndustrialBackboneSignificance01, Is.GreaterThan(iron.ExportSpeculationSignificance01));
                Assert.That(gold.ExportSpeculationSignificance01, Is.GreaterThan(gold.IndustrialBackboneSignificance01));
                Assert.That(silver.ExportSpeculationSignificance01, Is.GreaterThan(silver.IndustrialBackboneSignificance01));
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }


        [Test]
        public void MineralScaffoldLinksProtoSitesToRegionalParcelsRoutesAndSettlements()
        {
            List<Object> cleanup = new();
            try
            {
                BuildingDefinition definition = CreateDefinition();
                cleanup.Add(definition);

                TownGenerationSettings settings = CreateSettings(definition, 1886);
                cleanup.Add(settings);

                TownWorldController controller = CreateController(settings, "Mineral Regional Linkage", cleanup);
                controller.GenerateTownShell();

                RegionalResourceSnapshot snapshot = controller.RegionalResources;
                Assert.NotNull(snapshot);
                Assert.NotNull(controller.RegionalWorld);
                Assert.That(snapshot.HasMeaningfulMineralOpportunity, Is.True);

                string summary = snapshot.BuildRegionalLinkageSummary(controller.RegionalWorld, 2);
                StringAssert.Contains("Regional resources:", summary);
                StringAssert.Contains("Mineral district linkage:", summary);
                StringAssert.Contains("nearest parcel", summary);
                StringAssert.Contains("action", summary);
                StringAssert.Contains("constraints", summary);
                StringAssert.Contains("nearest node", summary);

                RemoteIndustrySiteRecord site = snapshot.RemoteSites.Count > 0 ? snapshot.RemoteSites[0] : null;
                Assert.NotNull(site);
                string siteReadout = snapshot.BuildRemoteSiteRegionalContextReadout(site, controller.RegionalWorld);
                StringAssert.Contains("Remote proto-site linkage:", siteReadout);
                StringAssert.Contains("nearest parcel", siteReadout);
                StringAssert.Contains("constraints", siteReadout);
                StringAssert.Contains("hooks", siteReadout);
                StringAssert.Contains("freight pressure", siteReadout);
                StringAssert.Contains("camp", siteReadout);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }


        [Test]
        public void RegionalDeveloperReportIncludesMineralLinkageWhenResourcesExist()
        {
            List<Object> cleanup = new();
            try
            {
                BuildingDefinition definition = CreateDefinition();
                cleanup.Add(definition);

                TownGenerationSettings settings = CreateSettings(definition, 1886);
                cleanup.Add(settings);

                TownWorldController controller = CreateController(settings, "Mineral Developer Report Linkage", cleanup);
                controller.GenerateTownShell();

                Assert.NotNull(controller.RegionalWorld);
                Assert.NotNull(controller.RegionalResources);
                Assert.That(controller.RegionalResources.HasMeaningfulMineralOpportunity, Is.True);

                string report = controller.BuildRegionalFoundationDeveloperReport(3);
                StringAssert.Contains("Regional Resource Linkage:", report);
                StringAssert.Contains("Mineral district linkage:", report);
                StringAssert.Contains("nearest parcel", report);
                StringAssert.Contains("action", report);
                StringAssert.Contains("constraints", report);
                StringAssert.Contains("nearest node", report);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }
        private static TownWorldController CreateController(TownGenerationSettings settings, string name, List<Object> cleanup)
        {
            GameObject root = new(name);
            cleanup.Add(root);
            TownWorldController controller = root.AddComponent<TownWorldController>();
            controller.Configure(settings, null, null);
            return controller;
        }

        private static TownGenerationSettings CreateSettings(BuildingDefinition definition, int seed)
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
            settings.generateAgriculturalParcels = true;
            settings.agricultureSpurRoadLengthCells = 24;
            settings.agricultureDistanceFromCenterCells = 20;
            settings.cropFarmParcelSizeCells = new Vector2Int(10, 10);
            settings.ranchParcelSizeCells = new Vector2Int(12, 12);
            settings.generateSawmillProperty = true;
            settings.sawmillSpurRoadLengthCells = 30;
            settings.sawmillDistanceFromCenterCells = 28;
            settings.sawmillParcelSizeCells = new Vector2Int(18, 16);
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
                "test_resource_shell",
                "Test Resource Shell",
                PlotZone.Business,
                new Vector2Int(4, 4),
                Color.white,
                4f);
            return definition;
        }

        private static List<string> CaptureSnapshot(RegionalResourceSnapshot snapshot)
        {
            List<string> lines = new();
            if (snapshot == null)
            {
                return lines;
            }

            for (int i = 0; i < snapshot.Districts.Count; i++)
            {
                MineralDistrictRecord district = snapshot.Districts[i];
                lines.Add(
                    $"D|{district.Id}|{district.Kind}|{district.Center.x:F2}|{district.Center.z:F2}|{district.RadiusMeters:F2}|{district.Strength01:F3}|{district.FreightPressure01:F3}|{district.SettlementPressure01:F3}");
            }

            for (int i = 0; i < snapshot.RemoteSites.Count; i++)
            {
                RemoteIndustrySiteRecord site = snapshot.RemoteSites[i];
                lines.Add(
                    $"S|{site.Id}|{site.DominantResource}|{site.AnchorPosition.x:F2}|{site.AnchorPosition.z:F2}|{site.SiteStrength01:F3}|{site.FutureCampRelevance01:F3}|{site.FutureBoomtownRelevance01:F3}|{string.Join(",", site.LinkedDistrictIds)}");
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
