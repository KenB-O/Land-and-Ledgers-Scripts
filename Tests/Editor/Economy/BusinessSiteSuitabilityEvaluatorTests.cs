using LandLedgers.Economy;
using LandLedgers.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LandLedgers.Editor.Economy
{
    public sealed class BusinessSiteSuitabilityEvaluatorTests
    {
        [Test]
        public void SuitableShellFailsWhenSiteIsTooSmall()
        {
            BuildingDefinition shell = CreateShell(
                "store_shell",
                "Store Shell",
                PlotZone.Business,
                new Vector2Int(5, 5),
                (BusinessType.GeneralStore, true));
            BusinessProfileDefinition profile = CreateProfile(BusinessType.GeneralStore, new Vector2Int(8, 8), 6, 64);
            TownPlot plot = CreatePlot(0, PlotZone.Business, 5, 5, 5);
            PlacedBuilding building = CreateBuilding(shell, plot);

            Assert.IsFalse(BusinessSiteSuitabilityEvaluator.CanOperate(profile, building, plot));
        }

        [Test]
        public void LargeEnoughSiteFailsWhenShellIsUnsuitable()
        {
            BuildingDefinition shell = CreateShell(
                "house_shell",
                "House Shell",
                PlotZone.Residential,
                new Vector2Int(4, 4),
                (BusinessType.GeneralStore, false));
            BusinessProfileDefinition profile = CreateProfile(BusinessType.GeneralStore, new Vector2Int(5, 5), 5, 25);
            TownPlot plot = CreatePlot(1, PlotZone.Business, 10, 10, 10);
            PlacedBuilding building = CreateBuilding(shell, plot);

            Assert.IsFalse(BusinessSiteSuitabilityEvaluator.CanOperate(profile, building, plot));
        }

        [Test]
        public void PlayerOwnedFitAllowsUnsuitableResidentialShellWithWarnings()
        {
            BuildingDefinition shell = CreateShell(
                "house_shell",
                "House Shell",
                PlotZone.Residential,
                new Vector2Int(4, 4),
                (BusinessType.GeneralStore, false));
            BusinessProfileDefinition profile = CreateProfile(BusinessType.GeneralStore, new Vector2Int(5, 5), 5, 25);
            TownPlot plot = CreatePlot(10, PlotZone.Residential, 4, 4, 3);
            PlacedBuilding building = CreateBuilding(shell, plot);

            Assert.IsFalse(BusinessSiteSuitabilityEvaluator.CanOperate(profile, building, plot));
            Assert.IsTrue(BusinessSiteSuitabilityEvaluator.TryEvaluatePlayerOwnedDevelopmentFit(profile, building, plot, out PlayerDevelopmentFitResult fit));
            Assert.IsTrue(fit.canProceed);
            Assert.Less(fit.fitScore01, 1f);
            Assert.Greater(fit.costMultiplier, 1f);
            StringAssert.Contains("residential/non-workplace", fit.WarningSummary);
        }

        [Test]
        public void GeneralStorePassesOnModestSuitableCommercialShell()
        {
            BuildingDefinition shell = CreateShell(
                "store_shell",
                "Store Shell",
                PlotZone.Business,
                new Vector2Int(5, 5),
                (BusinessType.GeneralStore, true));
            BusinessProfileDefinition profile = CreateProfile(BusinessType.GeneralStore, new Vector2Int(5, 5), 5, 25);
            TownPlot plot = CreatePlot(2, PlotZone.Business, 8, 8, 8);
            PlacedBuilding building = CreateBuilding(shell, plot);

            Assert.IsTrue(BusinessSiteSuitabilityEvaluator.CanOperate(profile, building, plot));
        }

        [Test]
        public void RanchAndCropFarmRequireLargerSiteThanStorefronts()
        {
            BuildingDefinition shell = CreateShell(
                "regular_business_shell",
                "Regular Business Shell",
                PlotZone.Business,
                new Vector2Int(5, 5),
                (BusinessType.Ranch, false),
                (BusinessType.CropFarm, false));
            BusinessProfileDefinition ranch = CreateProfile(BusinessType.Ranch, new Vector2Int(8, 10), 8, 80, PlotZone.Agricultural);
            BusinessProfileDefinition cropFarm = CreateProfile(BusinessType.CropFarm, new Vector2Int(8, 10), 8, 80, PlotZone.Agricultural);
            TownPlot smallCropPlot = CreatePlot(3, PlotZone.Agricultural, 8, 8, 8, AgriculturalSiteRole.CropProductionYard);
            TownPlot largeCropPlot = CreatePlot(4, PlotZone.Agricultural, 8, 10, 10, AgriculturalSiteRole.CropProductionYard);
            TownPlot smallRanchPlot = CreatePlot(5, PlotZone.Agricultural, 8, 8, 8, AgriculturalSiteRole.LivestockYard);
            TownPlot largeRanchPlot = CreatePlot(6, PlotZone.Agricultural, 8, 10, 10, AgriculturalSiteRole.LivestockYard);

            Assert.IsFalse(BusinessSiteSuitabilityEvaluator.CanOperate(ranch, CreateBuilding(shell, smallRanchPlot), smallRanchPlot));
            Assert.IsFalse(BusinessSiteSuitabilityEvaluator.CanOperate(cropFarm, CreateBuilding(shell, smallCropPlot), smallCropPlot));
            Assert.IsTrue(BusinessSiteSuitabilityEvaluator.CanOperate(ranch, CreateBuilding(shell, largeRanchPlot), largeRanchPlot));
            Assert.IsTrue(BusinessSiteSuitabilityEvaluator.CanOperate(cropFarm, CreateBuilding(shell, largeCropPlot), largeCropPlot));
        }

        [Test]
        public void SawmillRequiresMatchingSawmillYard()
        {
            BuildingDefinition shell = CreateShell(
                "remote_sawmill_shell",
                "Remote Sawmill Shell",
                PlotZone.Agricultural,
                new Vector2Int(8, 7),
                (BusinessType.Sawmill, true));
            BusinessProfileDefinition sawmill = CreateProfile(BusinessType.Sawmill, new Vector2Int(32, 28), 8, 120, PlotZone.Agricultural);
            TownPlot sawmillPlot = CreatePlot(13, PlotZone.Agricultural, 32, 28, 12, AgriculturalSiteRole.SawmillYard);
            TownPlot cropPlot = CreatePlot(14, PlotZone.Agricultural, 32, 28, 12, AgriculturalSiteRole.CropProductionYard);
            TownPlot ranchPlot = CreatePlot(15, PlotZone.Agricultural, 32, 28, 12, AgriculturalSiteRole.LivestockYard);

            Assert.IsTrue(BusinessSiteSuitabilityEvaluator.CanOperate(sawmill, CreateBuilding(shell, sawmillPlot), sawmillPlot));
            Assert.IsFalse(BusinessSiteSuitabilityEvaluator.CanOperate(sawmill, CreateBuilding(shell, cropPlot), cropPlot));
            Assert.IsFalse(BusinessSiteSuitabilityEvaluator.CanOperate(sawmill, CreateBuilding(shell, ranchPlot), ranchPlot));
        }

        [Test]
        public void LumberYardRequiresTownFacingBusinessOrMixedUseSite()
        {
            BuildingDefinition shell = CreateShell(
                "lumber_yard_shell",
                "Lumber Yard Shell",
                PlotZone.Business,
                new Vector2Int(6, 6),
                (BusinessType.LumberYard, true),
                (BusinessType.Sawmill, false));
            BusinessProfileDefinition lumberYard = CreateProfile(BusinessType.LumberYard, new Vector2Int(8, 8), 6, 45, PlotZone.Business, PlotZone.MixedUse);
            TownPlot businessPlot = CreatePlot(16, PlotZone.Business, 8, 8, 6);
            TownPlot mixedUsePlot = CreatePlot(17, PlotZone.MixedUse, 8, 8, 6);
            TownPlot sawmillPlot = CreatePlot(18, PlotZone.Agricultural, 40, 34, 12, AgriculturalSiteRole.SawmillYard);

            Assert.IsTrue(BusinessSiteSuitabilityEvaluator.CanOperate(lumberYard, CreateBuilding(shell, businessPlot), businessPlot));
            Assert.IsTrue(BusinessSiteSuitabilityEvaluator.CanOperate(lumberYard, CreateBuilding(shell, mixedUsePlot), mixedUsePlot));
            Assert.IsFalse(BusinessSiteSuitabilityEvaluator.CanOperate(lumberYard, CreateBuilding(shell, sawmillPlot), sawmillPlot));
        }

        [Test]
        public void MixedUseTownPlotsDoNotSatisfyAgricultureOnlyRequirements()
        {
            BuildingDefinition shell = CreateShell(
                "regular_business_shell",
                "Regular Business Shell",
                PlotZone.Business,
                new Vector2Int(5, 5),
                (BusinessType.Ranch, false),
                (BusinessType.CropFarm, false));
            BusinessProfileDefinition ranch = CreateProfile(BusinessType.Ranch, new Vector2Int(8, 10), 8, 80, PlotZone.Agricultural);
            BusinessProfileDefinition cropFarm = CreateProfile(BusinessType.CropFarm, new Vector2Int(8, 10), 8, 80, PlotZone.Agricultural);
            TownPlot mixedUsePlot = CreatePlot(5, PlotZone.MixedUse, 10, 10, 10);
            TownPlot ranchPlot = CreatePlot(6, PlotZone.Agricultural, 10, 10, 10, AgriculturalSiteRole.LivestockYard);
            TownPlot cropPlot = CreatePlot(7, PlotZone.Agricultural, 10, 10, 10, AgriculturalSiteRole.CropProductionYard);

            Assert.IsFalse(ranch.SiteRequirements.Meets(mixedUsePlot));
            Assert.IsFalse(BusinessSiteSuitabilityEvaluator.CanOperate(ranch, CreateBuilding(shell, mixedUsePlot), mixedUsePlot));
            Assert.IsFalse(BusinessSiteSuitabilityEvaluator.CanOperate(cropFarm, CreateBuilding(shell, mixedUsePlot), mixedUsePlot));
            Assert.IsTrue(BusinessSiteSuitabilityEvaluator.CanOperate(ranch, CreateBuilding(shell, ranchPlot), ranchPlot));
            Assert.IsTrue(BusinessSiteSuitabilityEvaluator.CanOperate(cropFarm, CreateBuilding(shell, cropPlot), cropPlot));
        }

        [Test]
        public void PlayerOwnedFitBlocksAgricultureOnMixedUse()
        {
            BuildingDefinition shell = CreateShell(
                "regular_business_shell",
                "Regular Business Shell",
                PlotZone.Business,
                new Vector2Int(5, 5),
                (BusinessType.Ranch, false));
            BusinessProfileDefinition ranch = CreateProfile(BusinessType.Ranch, new Vector2Int(8, 10), 8, 80, PlotZone.Agricultural);
            TownPlot mixedUsePlot = CreatePlot(11, PlotZone.MixedUse, 10, 10, 6);
            PlacedBuilding building = CreateBuilding(shell, mixedUsePlot);

            Assert.IsFalse(BusinessSiteSuitabilityEvaluator.CanOperate(ranch, building, mixedUsePlot));
            Assert.IsFalse(BusinessSiteSuitabilityEvaluator.TryEvaluatePlayerOwnedDevelopmentFit(ranch, building, mixedUsePlot, out PlayerDevelopmentFitResult fit));
            Assert.IsFalse(fit.canProceed);
            StringAssert.Contains("requires a matching agricultural parcel", fit.reason);
        }

        [Test]
        public void LegacyAgriculturalDefinitionRoleStillAllowsMatchingBusiness()
        {
            BuildingDefinition shell = CreateShell(
                "legacy_ranch_shell",
                "Legacy Ranch Shell",
                PlotZone.Agricultural,
                new Vector2Int(5, 5),
                (BusinessType.Ranch, false));
            shell.ConfigureAgriculturalSiteRole(AgriculturalSiteRole.LivestockYard);
            BusinessProfileDefinition ranch = CreateProfile(BusinessType.Ranch, new Vector2Int(8, 10), 8, 80, PlotZone.Agricultural);
            TownPlot agriculturalPlot = CreatePlot(12, PlotZone.Agricultural, 10, 10, 10);

            Assert.IsTrue(BusinessSiteSuitabilityEvaluator.CanOperate(ranch, CreateBuilding(shell, agriculturalPlot), agriculturalPlot));
        }

        [Test]
        public void ResidentialHouseShellRejectsCurrentBusinessTypes()
        {
            BuildingDefinition shell = CreateShell(
                "house_shell",
                "House Shell",
                PlotZone.Residential,
                new Vector2Int(4, 4),
                (BusinessType.GeneralStore, false),
                (BusinessType.Blacksmith, false),
                (BusinessType.Butcher, false),
                (BusinessType.Ranch, false),
                (BusinessType.CropFarm, false),
                (BusinessType.Doctor, false),
                (BusinessType.Sawmill, false),
                (BusinessType.LumberYard, false));

            Assert.IsFalse(shell.IsSuitableForBusiness(BusinessType.GeneralStore));
            Assert.IsFalse(shell.IsSuitableForBusiness(BusinessType.Blacksmith));
            Assert.IsFalse(shell.IsSuitableForBusiness(BusinessType.Butcher));
            Assert.IsFalse(shell.IsSuitableForBusiness(BusinessType.Ranch));
            Assert.IsFalse(shell.IsSuitableForBusiness(BusinessType.CropFarm));
            Assert.IsFalse(shell.IsSuitableForBusiness(BusinessType.Doctor));
            Assert.IsFalse(shell.IsSuitableForBusiness(BusinessType.Sawmill));
            Assert.IsFalse(shell.IsSuitableForBusiness(BusinessType.LumberYard));
        }

        [Test]
        public void HouseNaturalWoodAssetIsResidentialOnly()
        {
            BuildingDefinition shell = AssetDatabase.LoadAssetAtPath<BuildingDefinition>("Assets/Core/World/Buildings/HouseNaturalWood.asset");

            Assert.NotNull(shell);
            Assert.AreEqual(PlotZone.Residential, shell.AllowedPlotZone);
            Assert.AreEqual(BuildingUseType.Residential, shell.PrimaryUse);
            Assert.IsFalse(shell.CanHostWorkplace);
            Assert.AreEqual(BuildingResidentialMode.StandaloneHousehold, shell.ResidentialMode);
            Assert.AreEqual(1, shell.ResidentHouseholdCapacity);
            Assert.IsFalse(shell.IsSuitableForBusiness(BusinessType.GeneralStore));
            Assert.IsFalse(shell.IsSuitableForBusiness(BusinessType.Blacksmith));
            Assert.IsFalse(shell.IsSuitableForBusiness(BusinessType.Butcher));
            Assert.IsFalse(shell.IsSuitableForBusiness(BusinessType.Ranch));
            Assert.IsFalse(shell.IsSuitableForBusiness(BusinessType.CropFarm));
            Assert.IsFalse(shell.IsSuitableForBusiness(BusinessType.Doctor));
            Assert.IsFalse(shell.IsSuitableForBusiness(BusinessType.Sawmill));
            Assert.IsFalse(shell.IsSuitableForBusiness(BusinessType.LumberYard));
        }

        private static BuildingDefinition CreateShell(
            string id,
            string label,
            PlotZone zone,
            Vector2Int footprint,
            params (BusinessType type, bool suitable)[] suitability)
        {
            BuildingDefinition shell = ScriptableObject.CreateInstance<BuildingDefinition>();
            shell.ConfigureRuntimeFallback(id, label, zone, footprint, Color.white, 5f);

            SerializedObject serialized = new(shell);
            SerializedProperty entries = serialized.FindProperty("businessSuitability");
            entries.arraySize = suitability.Length;
            for (int i = 0; i < suitability.Length; i++)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("businessType").enumValueIndex = (int)suitability[i].type;
                entry.FindPropertyRelative("suitable").boolValue = suitability[i].suitable;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            return shell;
        }

        private static BusinessProfileDefinition CreateProfile(
            BusinessType businessType,
            Vector2Int minimumSiteSize,
            int frontage,
            int buildableArea,
            params PlotZone[] allowedZones)
        {
            BusinessProfileDefinition profile = ScriptableObject.CreateInstance<BusinessProfileDefinition>();
            SerializedObject serialized = new(profile);
            serialized.FindProperty("business.businessType").enumValueIndex = (int)businessType;
            serialized.FindProperty("siteRequirements.minimumSiteSizeCells").vector2IntValue = minimumSiteSize;
            serialized.FindProperty("siteRequirements.minimumFrontageCells").intValue = frontage;
            serialized.FindProperty("siteRequirements.minimumBuildableAreaCells").intValue = buildableArea;
            serialized.FindProperty("siteRequirements.requiresRoadFrontage").boolValue = true;

            SerializedProperty zones = serialized.FindProperty("siteRequirements.allowedPlotZones");
            if (allowedZones == null || allowedZones.Length == 0)
            {
                allowedZones = new[] { PlotZone.Business, PlotZone.MixedUse };
            }

            zones.arraySize = allowedZones.Length;
            for (int i = 0; i < allowedZones.Length; i++)
            {
                zones.GetArrayElementAtIndex(i).enumValueIndex = (int)allowedZones[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            return profile;
        }

        private static TownPlot CreatePlot(
            int id,
            PlotZone zone,
            int width,
            int depth,
            int frontage,
            AgriculturalSiteRole agriculturalSiteRole = AgriculturalSiteRole.None)
        {
            GridRect bounds = new(0, 0, width, depth);
            return new TownPlot
            {
                id = id,
                zone = zone,
                bounds = bounds,
                candidateFootprint = bounds,
                siteSizeCells = new Vector2Int(width, depth),
                agriculturalSiteRole = agriculturalSiteRole,
                frontageCells = frontage,
                depthCells = width,
                roadFrontageDirection = GridDirection.East,
                roadAccessCell = new GridCoord(width, depth / 2)
            };
        }

        private static PlacedBuilding CreateBuilding(BuildingDefinition shell, TownPlot plot)
        {
            return new PlacedBuilding
            {
                id = plot.id,
                plotId = plot.id,
                definition = shell,
                footprint = new GridRect(0, 0, shell.FootprintSizeCells.x, shell.FootprintSizeCells.y),
                siteSizeCells = plot.siteSizeCells,
                intendedFootprintSizeCells = shell.FootprintSizeCells,
                frontageDirection = plot.roadFrontageDirection
            };
        }
    }
}
