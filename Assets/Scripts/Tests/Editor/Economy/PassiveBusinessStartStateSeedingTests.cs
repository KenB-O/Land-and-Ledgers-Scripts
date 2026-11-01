using System;
using System.Collections.Generic;
using System.Text;
using LandLedgers.Economy;
using LandLedgers.Pathing;
using LandLedgers.Persistence;
using LandLedgers.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LandLedgers.Editor.Economy
{
    public sealed class PassiveBusinessStartStateSeedingTests
    {
        [Test]
        public void StartStateSeedsApprovedLaunchBusinessesWithoutPassiveGeneralStore()
        {
            List<UnityEngine.Object> cleanup = new();
            try
            {
                TownWorldController townWorld = CreateGeneratedTown(1886, cleanup);
                Dictionary<BusinessType, BusinessProfileDefinition> profiles = LoadProfilesByType();
                int storeBuildingId = FindEligibleBuilding(townWorld, profiles[BusinessType.GeneralStore]);
                BusinessInstanceState playerStore = CreatePlayerStore(profiles[BusinessType.GeneralStore], townWorld, storeBuildingId);
                SharedBusinessRuntimeManager runtime = CreateRuntime(townWorld, profiles.Values, cleanup);

                runtime.InitializeIfNeeded(playerStore);

                Assert.AreEqual(1, CountBusinesses(runtime.Businesses, BusinessType.GeneralStore));
                Assert.AreEqual(1, CountBusinesses(runtime.Businesses, BusinessType.Blacksmith));
                Assert.AreEqual(1, CountBusinesses(runtime.Businesses, BusinessType.Butcher));
                Assert.AreEqual(1, CountBusinesses(runtime.Businesses, BusinessType.Ranch));
                Assert.AreEqual(1, CountBusinesses(runtime.Businesses, BusinessType.CropFarm));
                Assert.AreEqual(1, CountBusinesses(runtime.Businesses, BusinessType.Sawmill));
                Assert.AreEqual(1, CountBusinesses(runtime.Businesses, BusinessType.LumberYard));
                Assert.AreEqual(1, CountBusinesses(runtime.Businesses, BusinessType.Doctor));
                Assert.AreEqual(7, CountPassiveLaunchBusinesses(runtime.Businesses));
                AssertNoDuplicatePassiveTypes(runtime.Businesses);
                AssertPassiveBusinessesDoNotUsePlayerStore(runtime.Businesses, storeBuildingId);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void SameSeedProducesSamePassiveBusinessAssignments()
        {
            string first = BuildPassiveBusinessSignature(1886);
            string second = BuildPassiveBusinessSignature(1886);

            Assert.AreEqual(first, second);
        }

        [Test]
        public void GeneratedTownCreatesRoadConnectedAgricultureParcels()
        {
            List<UnityEngine.Object> cleanup = new();
            try
            {
                TownWorldController townWorld = CreateGeneratedTown(1886, cleanup);
                List<TownPlot> agriculturalPlots = new();
                for (int i = 0; i < townWorld.Plots.Count; i++)
                {
                    if (townWorld.Plots[i] != null && townWorld.Plots[i].zone == PlotZone.Agricultural)
                    {
                        agriculturalPlots.Add(townWorld.Plots[i]);
                    }
                }

                Assert.AreEqual(3, agriculturalPlots.Count);

                GameObject pathingObject = new("Agriculture Pathing Test");
                cleanup.Add(pathingObject);
                PathingSettings pathingSettings = ScriptableObject.CreateInstance<PathingSettings>();
                cleanup.Add(pathingSettings);
                pathingSettings.maxVisitedCells = 20000;
                PathingManager pathing = pathingObject.AddComponent<PathingManager>();
                pathing.Configure(townWorld, pathingSettings);

                int livestockYards = 0;
                int cropProductionYards = 0;
                int sawmillYards = 0;
                for (int i = 0; i < agriculturalPlots.Count; i++)
                {
                    TownPlot plot = agriculturalPlots[i];
                    Assert.IsTrue(townWorld.Grid.GetCell(plot.roadAccessCell).IsRoad);
                    Assert.AreEqual(RoadType.Spur, townWorld.Grid.GetCell(plot.roadAccessCell).roadType);
                    Assert.GreaterOrEqual(Mathf.Abs(plot.bounds.Center.z - townWorld.Grid.Depth / 2), 18);
                    Assert.GreaterOrEqual(plot.frontageCells, 8);
                    Assert.GreaterOrEqual(plot.buildingId, 0);

                    PlacedBuilding building = townWorld.Buildings[plot.buildingId];
                    Assert.NotNull(building.definition);
                    Assert.AreEqual(AgriculturalSiteRole.None, building.definition.AgriculturalSiteRole);
                    if (plot.agriculturalSiteRole == AgriculturalSiteRole.LivestockYard)
                    {
                        livestockYards++;
                    }

                    if (plot.agriculturalSiteRole == AgriculturalSiteRole.CropProductionYard)
                    {
                        cropProductionYards++;
                    }

                    if (plot.agriculturalSiteRole == AgriculturalSiteRole.SawmillYard)
                    {
                        sawmillYards++;
                    }

                    Assert.IsTrue(building.TryGetAnchor(AnchorType.FrontDoor, out BuildingAnchor anchor));
                    Assert.IsTrue(pathing.TryFindPath(plot.roadAccessCell, anchor.coord, out PathingResult result), result.FailureReason);
                }

                Assert.AreEqual(1, livestockYards);
                Assert.AreEqual(1, cropProductionYards);
                Assert.AreEqual(1, sawmillYards);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void DefaultAgriculturalParcelsUseRegularBuildingDefinitionsAndSpawnDressing()
        {
            TownGenerationSettings settings = AssetDatabase.LoadAssetAtPath<TownGenerationSettings>("Assets/Core/World/DefaultTownGenerationSettings.asset");
            Assert.NotNull(settings, "Default town generation settings should be loadable.");

            Assert.IsNull(FindCatalogShell(settings, "crop_farm_shell"), "Default town building catalog should no longer include CropFarmShell.");
            Assert.IsNull(FindCatalogShell(settings, "ranch_shell"), "Default town building catalog should no longer include RanchShell.");
            BuildingDefinition cropReplacement = FindCatalogShell(settings, "single_story_business_natural_wood");
            BuildingDefinition ranchReplacement = FindCatalogShell(settings, "single_story_business_green_wood");
            Assert.NotNull(cropReplacement, "Default town building catalog should include the regular Crop Farm physical building.");
            Assert.NotNull(ranchReplacement, "Default town building catalog should include the regular Ranch physical building.");
            Assert.AreEqual(AgriculturalSiteRole.None, cropReplacement.AgriculturalSiteRole);
            Assert.AreEqual(AgriculturalSiteRole.None, ranchReplacement.AgriculturalSiteRole);
            Assert.NotNull(cropReplacement.VisualPrefab, "Crop Farm physical building should use a regular visual prefab.");
            Assert.NotNull(ranchReplacement.VisualPrefab, "Ranch physical building should use a regular visual prefab.");
            AssertNotHangerVisual(cropReplacement, "Crop Farm physical building");
            AssertNotHangerVisual(ranchReplacement, "Ranch physical building");
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<BuildingDefinition>("Assets/Core/World/Buildings/CropFarmShell.asset"));
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<BuildingDefinition>("Assets/Core/World/Buildings/RanchShell.asset"));
            AssertConfiguredPrefabs(settings.cropFarmFoodPrefabs, "Crop farm food prefabs");
            AssertConfiguredPrefabs(settings.cropFarmProduceStackPrefabs, "Crop farm produce stack prefabs");
            AssertConfiguredPrefabs(settings.ranchAnimalPrefabs, "Ranch animal prefabs");

            List<UnityEngine.Object> cleanup = new();
            try
            {
                GameObject townObject = new("Default Agriculture Shell Test Town");
                cleanup.Add(townObject);
                TownWorldController townWorld = townObject.AddComponent<TownWorldController>();
                townWorld.Configure(settings, null, null);
                townWorld.GenerateTownShell();

                GameObject pathingObject = new("Default Agriculture Pathing Test");
                cleanup.Add(pathingObject);
                PathingSettings pathingSettings = ScriptableObject.CreateInstance<PathingSettings>();
                cleanup.Add(pathingSettings);
                pathingSettings.maxVisitedCells = 30000;
                PathingManager pathing = pathingObject.AddComponent<PathingManager>();
                pathing.Configure(townWorld, pathingSettings);

                int agriculturalPlots = 0;
                int regularAgriculturalBuildings = 0;
                int cropProductionYards = 0;
                int livestockYards = 0;
                int sawmillYards = 0;
                for (int i = 0; i < townWorld.Plots.Count; i++)
                {
                    TownPlot plot = townWorld.Plots[i];
                    if (plot == null || plot.zone != PlotZone.Agricultural)
                    {
                        continue;
                    }

                    agriculturalPlots++;
                    Assert.IsTrue(townWorld.Grid.GetCell(plot.roadAccessCell).IsRoad);
                    Assert.AreEqual(RoadType.Spur, townWorld.Grid.GetCell(plot.roadAccessCell).roadType);
                    Assert.GreaterOrEqual(plot.buildingId, 0);

                    PlacedBuilding building = townWorld.Buildings[plot.buildingId];
                    Assert.NotNull(building.definition);
                    Assert.NotNull(building.definition.VisualPrefab, $"{building.definition.DisplayName} should not fall back to a greybox visual.");
                    AssertNotHangerVisual(building.definition, $"{building.definition.DisplayName} agricultural building");
                    Assert.AreEqual(PlotZone.Agricultural, plot.zone);
                    Assert.AreEqual(AgriculturalSiteRole.None, building.definition.AgriculturalSiteRole);
                    Assert.IsTrue(building.definition.CanHostWorkplace);
                    Assert.AreNotEqual("crop_farm_shell", building.definition.BuildingId);
                    Assert.AreNotEqual("ranch_shell", building.definition.BuildingId);
                    regularAgriculturalBuildings++;

                    if (plot.agriculturalSiteRole == AgriculturalSiteRole.CropProductionYard)
                    {
                        cropProductionYards++;
                    }
                    else if (plot.agriculturalSiteRole == AgriculturalSiteRole.LivestockYard)
                    {
                        livestockYards++;
                    }
                    else if (plot.agriculturalSiteRole == AgriculturalSiteRole.SawmillYard)
                    {
                        sawmillYards++;
                    }

                    Assert.IsTrue(building.TryGetAnchor(AnchorType.FrontDoor, out BuildingAnchor anchor));
                    Assert.IsTrue(pathing.TryFindPath(plot.roadAccessCell, anchor.coord, out PathingResult result), result.FailureReason);

                }

                Assert.AreEqual(3, agriculturalPlots);
                Assert.AreEqual(3, regularAgriculturalBuildings);
                Assert.AreEqual(1, cropProductionYards);
                Assert.AreEqual(1, livestockYards);
                Assert.AreEqual(1, sawmillYards);
                Assert.GreaterOrEqual(
                    CountVisualsWithPrefix(townWorld.VisualRoot, "Agriculture Crop Food Asset"),
                    6,
                    "Crop Farm parcel should instantiate food asset dressing.");
                Assert.GreaterOrEqual(
                    CountVisualsWithPrefix(townWorld.VisualRoot, "Agriculture Crop Produce Asset"),
                    2,
                    "Crop Farm parcel should instantiate produce stack asset dressing.");
                Assert.GreaterOrEqual(
                    CountVisualsWithPrefix(townWorld.VisualRoot, "Agriculture Ranch Animal Asset"),
                    3,
                    "Ranch parcel should instantiate animal asset dressing.");
                Assert.GreaterOrEqual(
                    CountVisualsWithPrefix(townWorld.VisualRoot, "Remote Sawmill Tree Stand"),
                    10,
                    "Sawmill parcel should create remote timber dressing.");
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void CoreWorldBuildingsFolderContainsOnlyPhysicalBuildingDefinitions()
        {
            HashSet<string> forbiddenIds = new(StringComparer.OrdinalIgnoreCase)
            {
                "crop_farm_shell",
                "ranch_shell",
                "town_hall"
            };

            Assert.IsNull(AssetDatabase.LoadAssetAtPath<BuildingDefinition>("Assets/Core/World/Buildings/CropFarmShell.asset"));
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<BuildingDefinition>("Assets/Core/World/Buildings/RanchShell.asset"));
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<BuildingDefinition>("Assets/Core/World/Buildings/TownHall.asset"));

            string[] guids = AssetDatabase.FindAssets("t:BuildingDefinition", new[] { "Assets/Core/World/Buildings" });
            Assert.IsNotEmpty(guids, "Core world building folder should contain physical BuildingDefinition assets.");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                BuildingDefinition definition = AssetDatabase.LoadAssetAtPath<BuildingDefinition>(path);
                Assert.NotNull(definition, path);
                Assert.IsFalse(string.IsNullOrWhiteSpace(definition.BuildingId), $"{path} should have a physical shell id.");
                Assert.IsFalse(forbiddenIds.Contains(definition.BuildingId), $"{path} is using an identity-layer id.");
                Assert.AreEqual(AgriculturalSiteRole.None, definition.AgriculturalSiteRole, $"{path} should not carry agricultural identity.");
            }
        }

        [Test]
        public void LegacyAgriculturalShellIdsLoadAsRegularBuildingsAndRestorePlotRoles()
        {
            BuildingDefinition cropReplacement = AssetDatabase.LoadAssetAtPath<BuildingDefinition>("Assets/Core/World/Buildings/SingleStoryBusinessNaturalWood.asset");
            BuildingDefinition ranchReplacement = AssetDatabase.LoadAssetAtPath<BuildingDefinition>("Assets/Core/World/Buildings/SingleStoryBusinessGreenWood.asset");
            Assert.NotNull(cropReplacement);
            Assert.NotNull(ranchReplacement);

            List<UnityEngine.Object> cleanup = new();
            try
            {
                TownGenerationSettings settings = ScriptableObject.CreateInstance<TownGenerationSettings>();
                cleanup.Add(settings);
                settings.gridWidthCells = 80;
                settings.gridDepthCells = 120;
                settings.cellSizeMeters = 2f;
                settings.seed = 1886;
                settings.generateAgriculturalParcels = true;
                settings.generateSawmillProperty = false;
                settings.generateTownHall = false;
                settings.buildingCatalog = new[] { cropReplacement, ranchReplacement };

                GameObject townObject = new("Legacy Agriculture Load Test Town");
                cleanup.Add(townObject);
                TownWorldController townWorld = townObject.AddComponent<TownWorldController>();
                townWorld.Configure(settings, null, null);

                SaveReferenceResolver resolver = new(townWorld);
                WorldSaveDto dto = CreateLegacyAgriculturalWorldDto();

                Assert.IsTrue(townWorld.LoadFromSaveDto(dto, resolver, out string message), message);
                Assert.AreEqual(2, townWorld.Buildings.Count);
                AssertLegacyAgriculturalLoad(
                    townWorld,
                    0,
                    AgriculturalSiteRole.CropProductionYard,
                    cropReplacement.BuildingId);
                AssertLegacyAgriculturalLoad(
                    townWorld,
                    1,
                    AgriculturalSiteRole.LivestockYard,
                    ranchReplacement.BuildingId);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void GeneratedAgricultureUsesPlotSiteRoleWithRegularBusinessShells()
        {
            List<UnityEngine.Object> cleanup = new();
            try
            {
                GameObject townObject = new("Agriculture Site Role Selection Test Town");
                cleanup.Add(townObject);

                TownGenerationSettings settings = ScriptableObject.CreateInstance<TownGenerationSettings>();
                cleanup.Add(settings);
                settings.gridWidthCells = 80;
                settings.gridDepthCells = 120;
                settings.cellSizeMeters = 2f;
                settings.seed = 1886;
                settings.roadWidthCells = 3;
                settings.mainStreetLengthCells = 96;
                settings.crossStreetCount = 0;
                settings.crossStreetSpacingCells = 96;
                settings.plotDepthCells = 8;
                settings.minPlotFrontageCells = 10;
                settings.maxPlotFrontageCells = 10;
                settings.plotGapCells = 1;
                settings.buildingSetbackCells = 1;
                settings.generateAgriculturalParcels = true;
                settings.generateSawmillProperty = false;
                settings.agricultureSpurRoadLengthCells = 28;
                settings.agricultureDistanceFromCenterCells = 36;
                settings.cropFarmParcelSizeCells = new Vector2Int(16, 18);
                settings.ranchParcelSizeCells = new Vector2Int(20, 24);
                settings.vacantLandPlotsToReserve = 0;

                BuildingDefinition townShell = CreateAllBusinessSuitableShell(cleanup);
                BuildingDefinition livestockShell = CreateAgriculturalRoleOnlyShell(
                    cleanup,
                    AgriculturalSiteRole.LivestockYard,
                    "role_test_livestock_yard_shell",
                    "Role Test Livestock Yard Shell",
                    new Vector2Int(7, 6));
                BuildingDefinition cropShell = CreateAgriculturalRoleOnlyShell(
                    cleanup,
                    AgriculturalSiteRole.CropProductionYard,
                    "role_test_crop_production_yard_shell",
                    "Role Test Crop Production Yard Shell",
                    new Vector2Int(6, 5));
                settings.buildingCatalog = new[] { townShell, livestockShell, cropShell };

                Assert.IsFalse(townShell.IsSuitableForBusiness(BusinessType.CropFarm));
                Assert.IsFalse(townShell.IsSuitableForBusiness(BusinessType.Ranch));
                Assert.IsFalse(cropShell.IsSuitableForBusiness(BusinessType.CropFarm));
                Assert.IsFalse(livestockShell.IsSuitableForBusiness(BusinessType.Ranch));

                TownWorldController townWorld = townObject.AddComponent<TownWorldController>();
                townWorld.Configure(settings, null, null);
                townWorld.GenerateTownShell();

                int cropProductionYards = 0;
                int livestockYards = 0;
                for (int i = 0; i < townWorld.Plots.Count; i++)
                {
                    TownPlot plot = townWorld.Plots[i];
                    if (plot == null || plot.zone != PlotZone.Agricultural)
                    {
                        continue;
                    }

                    Assert.GreaterOrEqual(plot.buildingId, 0);
                    PlacedBuilding building = townWorld.Buildings[plot.buildingId];
                    Assert.NotNull(building.definition);

                    Assert.AreEqual("seed_test_town_core_shell", building.definition.BuildingId);
                    Assert.AreEqual(AgriculturalSiteRole.None, building.definition.AgriculturalSiteRole);
                    Assert.IsTrue(building.definition.CanHostWorkplace);

                    if (plot.agriculturalSiteRole == AgriculturalSiteRole.CropProductionYard)
                    {
                        cropProductionYards++;
                        Assert.AreEqual(settings.cropFarmParcelSizeCells, plot.siteSizeCells);
                    }
                    else if (plot.agriculturalSiteRole == AgriculturalSiteRole.LivestockYard)
                    {
                        livestockYards++;
                        Assert.AreEqual(settings.ranchParcelSizeCells, plot.siteSizeCells);
                    }
                    else
                    {
                        Assert.Fail($"Agricultural plot {plot.id:000} received a shell without an agricultural site role.");
                    }
                }

                Assert.AreEqual(1, cropProductionYards);
                Assert.AreEqual(1, livestockYards);
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        [Test]
        public void DifferentSeedsCanProduceDifferentPassiveBusinessAssignments()
        {
            HashSet<string> signatures = new();
            for (int seed = 1886; seed <= 1892; seed++)
            {
                signatures.Add(BuildPassiveBusinessSignature(seed));
            }

            Assert.Greater(signatures.Count, 1, "Seeded passive business assignment should vary across different town seeds.");
        }

        private static string BuildPassiveBusinessSignature(int seed)
        {
            List<UnityEngine.Object> cleanup = new();
            try
            {
                TownWorldController townWorld = CreateGeneratedTown(seed, cleanup);
                Dictionary<BusinessType, BusinessProfileDefinition> profiles = LoadProfilesByType();
                int storeBuildingId = FindEligibleBuilding(townWorld, profiles[BusinessType.GeneralStore]);
                BusinessInstanceState playerStore = CreatePlayerStore(profiles[BusinessType.GeneralStore], townWorld, storeBuildingId);
                SharedBusinessRuntimeManager runtime = CreateRuntime(townWorld, profiles.Values, cleanup);

                runtime.InitializeIfNeeded(playerStore);

                List<BusinessInstanceState> passiveBusinesses = new();
                for (int i = 0; i < runtime.Businesses.Count; i++)
                {
                    BusinessInstanceState business = runtime.Businesses[i];
                    if (business != null && business.BusinessType != BusinessType.GeneralStore)
                    {
                        passiveBusinesses.Add(business);
                    }
                }

                passiveBusinesses.Sort((left, right) => left.BusinessType.CompareTo(right.BusinessType));

                StringBuilder builder = new();
                for (int i = 0; i < passiveBusinesses.Count; i++)
                {
                    BusinessInstanceState business = passiveBusinesses[i];
                    builder.Append(business.BusinessType);
                    builder.Append(':');
                    builder.Append(business.AssignedBuildingId.ToString("000"));
                    builder.Append('|');
                }

                return builder.ToString();
            }
            finally
            {
                DestroyAll(cleanup);
            }
        }

        private static TownWorldController CreateGeneratedTown(int seed, List<UnityEngine.Object> cleanup)
        {
            GameObject townObject = new($"Passive Business Seed Test Town {seed}");
            cleanup.Add(townObject);

            TownGenerationSettings settings = ScriptableObject.CreateInstance<TownGenerationSettings>();
            cleanup.Add(settings);
            settings.gridWidthCells = 80;
            settings.gridDepthCells = 120;
            settings.cellSizeMeters = 2f;
            settings.seed = seed;
            settings.roadWidthCells = 3;
            settings.mainStreetLengthCells = 96;
            settings.crossStreetCount = 0;
            settings.crossStreetSpacingCells = 96;
            settings.plotDepthCells = 8;
            settings.minPlotFrontageCells = 10;
            settings.maxPlotFrontageCells = 10;
            settings.plotGapCells = 1;
            settings.buildingSetbackCells = 1;
            settings.generateAgriculturalParcels = true;
            settings.generateSawmillProperty = true;
            settings.agricultureSpurRoadLengthCells = 28;
            settings.agricultureDistanceFromCenterCells = 36;
            settings.sawmillSpurRoadLengthCells = 48;
            settings.sawmillDistanceFromCenterCells = 50;
            settings.cropFarmParcelSizeCells = new Vector2Int(16, 18);
            settings.ranchParcelSizeCells = new Vector2Int(20, 24);
            settings.sawmillParcelSizeCells = new Vector2Int(32, 28);
            settings.vacantLandPlotsToReserve = 0;

            BuildingDefinition shell = CreateAllBusinessSuitableShell(cleanup);
            BuildingDefinition cropShell = CreateAgriculturalShell(cleanup, BusinessType.CropFarm);
            BuildingDefinition ranchShell = CreateAgriculturalShell(cleanup, BusinessType.Ranch);
            BuildingDefinition sawmillShell = CreateAgriculturalShell(cleanup, BusinessType.Sawmill);
            settings.buildingCatalog = new[] { shell, cropShell, ranchShell, sawmillShell };

            TownWorldController townWorld = townObject.AddComponent<TownWorldController>();
            townWorld.Configure(settings, null, null);
            townWorld.GenerateTownShell();
            Assert.GreaterOrEqual(townWorld.Buildings.Count, 6);
            return townWorld;
        }

        private static BuildingDefinition CreateAllBusinessSuitableShell(List<UnityEngine.Object> cleanup)
        {
            BuildingDefinition shell = ScriptableObject.CreateInstance<BuildingDefinition>();
            cleanup.Add(shell);
            shell.ConfigureRuntimeFallback(
                "seed_test_town_core_shell",
                "Seed Test Town Core Shell",
                PlotZone.Business,
                new Vector2Int(5, 5),
                Color.white,
                5f);

            SerializedObject serialized = new(shell);
            SerializedProperty entries = serialized.FindProperty("businessSuitability");
            entries.arraySize = 8;
            SetSuitability(entries, 0, BusinessType.GeneralStore, true);
            SetSuitability(entries, 1, BusinessType.Blacksmith, true);
            SetSuitability(entries, 2, BusinessType.Butcher, true);
            SetSuitability(entries, 3, BusinessType.Ranch, false);
            SetSuitability(entries, 4, BusinessType.CropFarm, false);
            SetSuitability(entries, 5, BusinessType.Doctor, true);
            SetSuitability(entries, 6, BusinessType.Sawmill, false);
            SetSuitability(entries, 7, BusinessType.LumberYard, true);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return shell;
        }

        private static BuildingDefinition CreateAgriculturalShell(List<UnityEngine.Object> cleanup, BusinessType businessType)
        {
            BuildingDefinition shell = ScriptableObject.CreateInstance<BuildingDefinition>();
            cleanup.Add(shell);
            string id = businessType switch
            {
                BusinessType.Ranch => "seed_test_ranch_shell",
                BusinessType.Sawmill => "seed_test_sawmill_shell",
                _ => "seed_test_crop_farm_shell"
            };
            string label = businessType switch
            {
                BusinessType.Ranch => "Seed Test Ranch Shell",
                BusinessType.Sawmill => "Seed Test Sawmill Shell",
                _ => "Seed Test Crop Farm Shell"
            };
            Vector2Int footprint = businessType switch
            {
                BusinessType.Ranch => new Vector2Int(7, 6),
                BusinessType.Sawmill => new Vector2Int(8, 7),
                _ => new Vector2Int(6, 5)
            };
            shell.ConfigureRuntimeFallback(
                id,
                label,
                PlotZone.Agricultural,
                footprint,
                Color.white,
                4.5f);
            shell.ConfigureAgriculturalSiteRole(businessType switch
            {
                BusinessType.Ranch => AgriculturalSiteRole.LivestockYard,
                BusinessType.Sawmill => AgriculturalSiteRole.SawmillYard,
                _ => AgriculturalSiteRole.CropProductionYard
            });

            SerializedObject serialized = new(shell);
            SerializedProperty entries = serialized.FindProperty("businessSuitability");
            entries.arraySize = 8;
            SetSuitability(entries, 0, BusinessType.GeneralStore, false);
            SetSuitability(entries, 1, BusinessType.Blacksmith, false);
            SetSuitability(entries, 2, BusinessType.Butcher, false);
            SetSuitability(entries, 3, BusinessType.Ranch, businessType == BusinessType.Ranch);
            SetSuitability(entries, 4, BusinessType.CropFarm, businessType == BusinessType.CropFarm);
            SetSuitability(entries, 5, BusinessType.Doctor, false);
            SetSuitability(entries, 6, BusinessType.Sawmill, businessType == BusinessType.Sawmill);
            SetSuitability(entries, 7, BusinessType.LumberYard, false);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return shell;
        }

        private static BuildingDefinition CreateAgriculturalRoleOnlyShell(
            List<UnityEngine.Object> cleanup,
            AgriculturalSiteRole role,
            string id,
            string label,
            Vector2Int footprint)
        {
            BuildingDefinition shell = ScriptableObject.CreateInstance<BuildingDefinition>();
            cleanup.Add(shell);
            shell.ConfigureRuntimeFallback(
                id,
                label,
                PlotZone.Agricultural,
                footprint,
                Color.white,
                4.5f);
            shell.ConfigureAgriculturalSiteRole(role);

            SerializedObject serialized = new(shell);
            SerializedProperty entries = serialized.FindProperty("businessSuitability");
            entries.arraySize = 8;
            SetSuitability(entries, 0, BusinessType.GeneralStore, false);
            SetSuitability(entries, 1, BusinessType.Blacksmith, false);
            SetSuitability(entries, 2, BusinessType.Butcher, false);
            SetSuitability(entries, 3, BusinessType.Ranch, false);
            SetSuitability(entries, 4, BusinessType.CropFarm, false);
            SetSuitability(entries, 5, BusinessType.Doctor, false);
            SetSuitability(entries, 6, BusinessType.Sawmill, false);
            SetSuitability(entries, 7, BusinessType.LumberYard, false);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return shell;
        }

        private static BuildingDefinition FindCatalogShell(TownGenerationSettings settings, string buildingId)
        {
            if (settings == null || settings.buildingCatalog == null)
            {
                return null;
            }

            for (int i = 0; i < settings.buildingCatalog.Length; i++)
            {
                BuildingDefinition definition = settings.buildingCatalog[i];
                if (definition != null && string.Equals(definition.BuildingId, buildingId, StringComparison.OrdinalIgnoreCase))
                {
                    return definition;
                }
            }

            return null;
        }

        private static WorldSaveDto CreateLegacyAgriculturalWorldDto()
        {
            WorldSaveDto dto = new()
            {
                settingsName = "Legacy Agriculture Test Settings",
                seed = 1886,
                gridWidthCells = 80,
                gridDepthCells = 120,
                cellSizeMeters = 2f,
                gridOriginX = -80f,
                gridOriginY = 0f,
                gridOriginZ = -120f
            };

            dto.plots.Add(CreateLegacyAgriculturalPlotDto(0, 0, "crop_farm_shell", 4, 78, 16, 18, GridDirection.South));
            dto.plots.Add(CreateLegacyAgriculturalPlotDto(1, 1, "ranch_shell", 56, 18, 20, 24, GridDirection.North));
            dto.buildings.Add(CreateLegacyAgriculturalBuildingDto(0, 0, "crop_farm_shell", 9, 79, 5, 5, GridDirection.South, 16, 18));
            dto.buildings.Add(CreateLegacyAgriculturalBuildingDto(1, 1, "ranch_shell", 63, 19, 4, 5, GridDirection.North, 20, 24));
            return dto;
        }

        private static PlotSaveDto CreateLegacyAgriculturalPlotDto(
            int id,
            int buildingId,
            string legacyBuildingId,
            int xMin,
            int zMin,
            int width,
            int depth,
            GridDirection frontageDirection)
        {
            return new PlotSaveDto
            {
                id = id,
                zone = PlotZone.Agricultural,
                bounds = CreateRectDto(xMin, zMin, width, depth),
                candidateFootprint = CreateRectDto(xMin, zMin, width, depth),
                siteSizeX = width,
                siteSizeY = depth,
                intendedFootprintX = legacyBuildingId == "ranch_shell" ? 7 : 6,
                intendedFootprintY = legacyBuildingId == "ranch_shell" ? 6 : 5,
                frontageCells = width,
                depthCells = depth,
                roadFrontageDirection = frontageDirection,
                roadAccessCell = CreateCoordDto(xMin + width / 2, frontageDirection == GridDirection.South ? zMin - 1 : zMin + depth),
                buildingId = buildingId
            };
        }

        private static BuildingSaveDto CreateLegacyAgriculturalBuildingDto(
            int id,
            int plotId,
            string legacyBuildingId,
            int xMin,
            int zMin,
            int width,
            int depth,
            GridDirection frontageDirection,
            int siteWidth,
            int siteDepth)
        {
            return new BuildingSaveDto
            {
                id = id,
                plotId = plotId,
                buildingDefinitionId = legacyBuildingId,
                footprint = CreateRectDto(xMin, zMin, width, depth),
                siteSizeX = siteWidth,
                siteSizeY = siteDepth,
                intendedFootprintX = legacyBuildingId == "ranch_shell" ? 7 : 6,
                intendedFootprintY = legacyBuildingId == "ranch_shell" ? 6 : 5,
                frontageDirection = frontageDirection
            };
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

        private static void AssertLegacyAgriculturalLoad(
            TownWorldController townWorld,
            int plotId,
            AgriculturalSiteRole expectedRole,
            string expectedBuildingDefinitionId)
        {
            TownPlot plot = townWorld.Plots[plotId];
            Assert.AreEqual(expectedRole, plot.agriculturalSiteRole);
            Assert.GreaterOrEqual(plot.buildingId, 0);

            PlacedBuilding building = townWorld.Buildings[plot.buildingId];
            Assert.NotNull(building.definition);
            Assert.AreEqual(expectedBuildingDefinitionId, building.definition.BuildingId);
            Assert.AreEqual(AgriculturalSiteRole.None, building.definition.AgriculturalSiteRole);
            Assert.IsTrue(building.definition.CanHostWorkplace);
            AssertNotHangerVisual(building.definition, building.definition.DisplayName);
        }

        private static void AssertNotHangerVisual(BuildingDefinition definition, string label)
        {
            Assert.NotNull(definition, $"{label} should have a building definition.");
            Assert.NotNull(definition.VisualPrefab, $"{label} should have a visual prefab.");
            string assetPath = AssetDatabase.GetAssetPath(definition.VisualPrefab).Replace('\\', '/');
            Assert.AreNotEqual("Assets/Asset Packs/Western/Prefabs/Buildings/Hanger.prefab", assetPath);
        }

        private static void AssertConfiguredPrefabs(GameObject[] prefabs, string label)
        {
            Assert.NotNull(prefabs, $"{label} should be configured on default town settings.");
            Assert.Greater(prefabs.Length, 0, $"{label} should have at least one default prefab.");
            for (int i = 0; i < prefabs.Length; i++)
            {
                Assert.NotNull(prefabs[i], $"{label} entry {i} should not be null.");
            }
        }

        private static int CountVisualsWithPrefix(Transform root, string prefix)
        {
            if (root == null)
            {
                return 0;
            }

            int count = 0;
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform child = transforms[i];
                if (child != root && child != null && child.name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        private static void AssertOnlySuitableFor(BuildingDefinition shell, BusinessType expectedBusinessType)
        {
            foreach (BusinessType businessType in (BusinessType[])Enum.GetValues(typeof(BusinessType)))
            {
                Assert.AreEqual(
                    businessType == expectedBusinessType,
                    shell.IsSuitableForBusiness(businessType),
                    $"{shell.DisplayName} suitability for {businessType} should match its agricultural role.");
            }
        }

        private static void SetSuitability(SerializedProperty entries, int index, BusinessType businessType, bool suitable)
        {
            SerializedProperty entry = entries.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("businessType").enumValueIndex = (int)businessType;
            entry.FindPropertyRelative("suitable").boolValue = suitable;
        }

        private static Dictionary<BusinessType, BusinessProfileDefinition> LoadProfilesByType()
        {
            BusinessProfileDefinition[] profiles = Resources.LoadAll<BusinessProfileDefinition>("Core/Economy/BusinessProfiles");
            Dictionary<BusinessType, BusinessProfileDefinition> byType = new();
            for (int i = 0; i < profiles.Length; i++)
            {
                if (profiles[i] != null)
                {
                    byType[profiles[i].Business.BusinessType] = profiles[i];
                }
            }

            AssertProfileLoaded(byType, BusinessType.GeneralStore);
            AssertProfileLoaded(byType, BusinessType.Blacksmith);
            AssertProfileLoaded(byType, BusinessType.Butcher);
            AssertProfileLoaded(byType, BusinessType.CropFarm);
            AssertProfileLoaded(byType, BusinessType.Ranch);
            AssertProfileLoaded(byType, BusinessType.Doctor);
            AssertProfileLoaded(byType, BusinessType.Sawmill);
            AssertProfileLoaded(byType, BusinessType.LumberYard);
            return byType;
        }

        private static int FindEligibleBuilding(TownWorldController townWorld, BusinessProfileDefinition profile)
        {
            for (int i = 0; i < townWorld.Buildings.Count; i++)
            {
                PlacedBuilding building = townWorld.Buildings[i];
                TownPlot plot = townWorld.Plots[building.plotId];
                if (BusinessSiteSuitabilityEvaluator.CanOperate(profile, building, plot))
                {
                    return building.id;
                }
            }

            Assert.Fail($"No eligible building found for {profile.Business.DisplayName}.");
            return -1;
        }

        private static BusinessInstanceState CreatePlayerStore(
            BusinessProfileDefinition generalStoreProfile,
            TownWorldController townWorld,
            int buildingId)
        {
            townWorld.Buildings[buildingId].playerOwned = true;
            return BusinessInstanceState.Create(
                "player_general_store_test",
                generalStoreProfile,
                buildingId,
                BusinessOwnerIdentity.Player());
        }

        private static SharedBusinessRuntimeManager CreateRuntime(
            TownWorldController townWorld,
            IEnumerable<BusinessProfileDefinition> profiles,
            List<UnityEngine.Object> cleanup)
        {
            GameObject runtimeObject = new("Passive Business Runtime Test");
            cleanup.Add(runtimeObject);
            SharedBusinessRuntimeManager runtime = runtimeObject.AddComponent<SharedBusinessRuntimeManager>();
            List<BusinessProfileDefinition> profileList = new(profiles);
            runtime.Configure(townWorld, profileList, null);
            return runtime;
        }

        private static int CountBusinesses(IReadOnlyList<BusinessInstanceState> businesses, BusinessType businessType)
        {
            int count = 0;
            for (int i = 0; i < businesses.Count; i++)
            {
                if (businesses[i] != null && businesses[i].BusinessType == businessType)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountPassiveLaunchBusinesses(IReadOnlyList<BusinessInstanceState> businesses)
        {
            return CountBusinesses(businesses, BusinessType.Blacksmith)
                + CountBusinesses(businesses, BusinessType.Butcher)
                + CountBusinesses(businesses, BusinessType.Ranch)
                + CountBusinesses(businesses, BusinessType.CropFarm)
                + CountBusinesses(businesses, BusinessType.Doctor)
                + CountBusinesses(businesses, BusinessType.Sawmill)
                + CountBusinesses(businesses, BusinessType.LumberYard);
        }

        private static void AssertNoDuplicatePassiveTypes(IReadOnlyList<BusinessInstanceState> businesses)
        {
            HashSet<BusinessType> seen = new();
            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState business = businesses[i];
                if (business == null || business.BusinessType == BusinessType.GeneralStore)
                {
                    continue;
                }

                Assert.IsTrue(seen.Add(business.BusinessType), $"{business.BusinessType} should only be seeded once.");
            }
        }

        private static void AssertPassiveBusinessesDoNotUsePlayerStore(IReadOnlyList<BusinessInstanceState> businesses, int storeBuildingId)
        {
            for (int i = 0; i < businesses.Count; i++)
            {
                BusinessInstanceState business = businesses[i];
                if (business == null || business.BusinessType == BusinessType.GeneralStore)
                {
                    continue;
                }

                Assert.AreNotEqual(storeBuildingId, business.AssignedBuildingId);
            }
        }

        private static void AssertProfileLoaded(
            IReadOnlyDictionary<BusinessType, BusinessProfileDefinition> profiles,
            BusinessType businessType)
        {
            Assert.IsTrue(profiles.ContainsKey(businessType), $"{businessType} profile should be present in Resources.");
        }

        private static void DestroyAll(List<UnityEngine.Object> cleanup)
        {
            for (int i = cleanup.Count - 1; i >= 0; i--)
            {
                UnityEngine.Object obj = cleanup[i];
                if (obj != null)
                {
                    UnityEngine.Object.DestroyImmediate(obj);
                }
            }
        }
    }
}
