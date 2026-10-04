using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using LandLedgers.Civic;
using LandLedgers.FirstLedger;
using LandLedgers.Economy;
using LandLedgers.Persistence;
using LandLedgers.Population;
using LandLedgers.Primitives;
using UnityEngine;
using UnityEngine.Rendering;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace LandLedgers.World
{
    [DisallowMultipleComponent]
    public sealed class TownWorldController : MonoBehaviour
    {
        private const string VisualRootName = "WorldVisualRoot";
        private const string AuthoredContentRootName = "AuthoredWorldContent";
        private const string FrontDoorMarkerName = "Anchor_FrontDoor";
        private const string ServicePointMarkerName = "Anchor_ServicePoint";
        private const string DropOffPointMarkerName = "Anchor_DropOffPoint";
        private const string AgricultureActivityCuePrefix = "Agriculture Activity Cue";
        private const string AgricultureFoodVisualPrefix = "Agriculture Crop Food Asset";
        private const string AgricultureProduceVisualPrefix = "Agriculture Crop Produce Asset";
        private const string AgricultureAnimalVisualPrefix = "Agriculture Ranch Animal Asset";
        private const string SawmillVisualPrefix = "Remote Sawmill";
        private const string DetailPropVisualPrefix = "Agricultural Detail Prop";
        private const string ForestEnvironmentVisualPrefix = "Forest Environment";
        private const string LegacyCropFarmShellBuildingId = "crop_farm_shell";
        private const string LegacyRanchShellBuildingId = "ranch_shell";
        private const string LegacyTownHallBuildingId = TownHallState.TownHallBuildingId;
        private const string TownHallPhysicalBuildingId = "single_story_civic_natural_wood";
        private const float RoadVisualYOffset = 0.018f;
        private const float RoadVisualHeight = 0.026f;
        private const float RoadTextureTileMeters = 2f;
        private const float PlotBoundaryYOffset = 0.046f;
        private const float PlotBoundaryHeight = 0.038f;
        private const float AgricultureVisualYOffset = 0.074f;
        private const float AgricultureRowHeight = 0.055f;
        private const float AgricultureFenceHeight = 0.34f;
        private const float AgriculturePropHeight = 0.22f;
        private const float AgricultureFoodPrefabScale = 1.1f;
        private const float AgricultureProducePrefabScale = 1.3f;
        private const float AgricultureAnimalPrefabScale = 0.78f;
        private const float SawmillPropHeight = 0.36f;
        private const float SawmillTreeHeight = 1.25f;
        private const int CropFarmFoodPrefabBudget = 12;
        private const int CropFarmProducePrefabBudget = 3;
        private const int RanchAnimalPrefabBudget = 5;
        private const int DetailCropFarmSalt = 1100;
        private const int DetailRanchSalt = 1200;
        private const int DetailSawmillSalt = 1300;
        private const int ForestSawmillSalt = 2000;
        private const int ForestMapEdgeSalt = 3000;
        private const int ForestWetGroundSalt = 4000;
        private const int ForestAgricultureSalt = 5000;
        private const float DetailPropScaleMin = 0.88f;
        private const float DetailPropScaleMax = 1.14f;
        private const float ForestTreeScaleMin = 0.78f;
        private const float ForestTreeScaleMax = 1.16f;
        private const float ForestUnderstoryScaleMin = 0.82f;
        private const float ForestUnderstoryScaleMax = 1.18f;
        private const float ForestGroundScaleMin = 0.78f;
        private const float ForestGroundScaleMax = 1.12f;
        private const float ForestDeadfallScaleMin = 0.84f;
        private const float ForestDeadfallScaleMax = 1.22f;
        private static readonly Color CropFieldVisualColor = new(0.26f, 0.55f, 0.22f, 1f);
        private static readonly Color CropStackVisualColor = new(0.88f, 0.68f, 0.25f, 1f);
        private static readonly Color RanchFenceVisualColor = new(0.50f, 0.36f, 0.18f, 1f);
        private static readonly Color RanchFeedVisualColor = new(0.78f, 0.61f, 0.24f, 1f);
        private static readonly Color SawmillLogVisualColor = new(0.48f, 0.30f, 0.14f, 1f);
        private static readonly Color SawmillBoardVisualColor = new(0.72f, 0.56f, 0.34f, 1f);
        private static readonly Color SawmillTreeVisualColor = new(0.36f, 0.22f, 0.10f, 1f);
        private static readonly Color SawmillCabinVisualColor = new(0.52f, 0.40f, 0.28f, 1f);
        private static readonly Color AgricultureActiveCueColor = new(0.36f, 0.78f, 0.26f, 1f);
        private static readonly Color AgricultureBlockedCueColor = new(0.92f, 0.43f, 0.15f, 1f);
        private static readonly int BaseColorMapScaleOffsetId = Shader.PropertyToID("_BaseColorMap_ST");
        private static readonly int MainTexScaleOffsetId = Shader.PropertyToID("_MainTex_ST");
        private static readonly int MaskMapScaleOffsetId = Shader.PropertyToID("_MaskMap_ST");
        private static readonly int NormalMapScaleOffsetId = Shader.PropertyToID("_NormalMap_ST");
        private static readonly int BumpMapScaleOffsetId = Shader.PropertyToID("_BumpMap_ST");
        private static readonly int HeightMapScaleOffsetId = Shader.PropertyToID("_HeightMap_ST");
        private static readonly int OcclusionMapScaleOffsetId = Shader.PropertyToID("_OcclusionMap_ST");
        private static readonly int ParallaxMapScaleOffsetId = Shader.PropertyToID("_ParallaxMap_ST");

        private enum AnchorResolutionStatus
        {
            Missing = 0,
            FoundValid = 1,
            FoundInvalid = 2
        }

        private readonly struct ExpansionPlotCandidate
        {
            public ExpansionPlotCandidate(GridRect bounds, GridDirection frontageDirection, GridCoord roadAccess, PlotZone zone, int score)
            {
                this.bounds = bounds;
                this.frontageDirection = frontageDirection;
                this.roadAccess = roadAccess;
                this.zone = zone;
                this.score = score;
            }

            public readonly GridRect bounds;
            public readonly GridDirection frontageDirection;
            public readonly GridCoord roadAccess;
            public readonly PlotZone zone;
            public readonly int score;
        }

        private enum LandOfferStrength
        {
            Weak = 0,
            Speculative = 1,
            Practical = 2,
            Strong = 3,
            Prime = 4
        }

        private enum VacantInventoryTriage
        {
            WeakLeftover = 0,
            SpeculativeHold = 1,
            FutureReserve = 2,
            DomesticReserve = 3,
            FrontageReserve = 4,
            WorkingReserve = 5,
            ActiveMarketOffer = 6,
            PlayerHold = 7
        }

        private enum ImprovedSiteTriage
        {
            NaturalFit = 0,
            MixedUseUpgrade = 1,
            FrontageRefit = 2,
            YardRecovery = 3,
            AgriculturalRoleRecovery = 4,
            StretchedAdaptation = 5,
            CrampedHold = 6,
            NarrowLockedFit = 7
        }

        private readonly struct PlacementFootprintResolution
        {
            public PlacementFootprintResolution(
                Vector2Int definitionSizeCells,
                Vector2Int authorityMinimumSizeCells,
                Vector2Int resolvedSizeCells,
                bool hasPrefabAuthority,
                bool expandedByAuthority,
                string source,
                string note)
            {
                this.definitionSizeCells = SanitizeFootprintSize(definitionSizeCells);
                this.authorityMinimumSizeCells = SanitizeFootprintSize(authorityMinimumSizeCells);
                this.resolvedSizeCells = SanitizeFootprintSize(resolvedSizeCells);
                this.hasPrefabAuthority = hasPrefabAuthority;
                this.expandedByAuthority = expandedByAuthority;
                this.source = source;
                this.note = note;
            }

            public readonly Vector2Int definitionSizeCells;
            public readonly Vector2Int authorityMinimumSizeCells;
            public readonly Vector2Int resolvedSizeCells;
            public readonly bool hasPrefabAuthority;
            public readonly bool expandedByAuthority;
            public readonly string source;
            public readonly string note;
        }

        private readonly struct LandOfferCandidate
        {
            public LandOfferCandidate(
                TownPlot plot,
                int score,
                LandOfferStrength strength,
                bool frontagePriority,
                bool domesticPriority,
                int totalFits,
                int workplaceFits,
                int householdFits,
                int mixedUseFits,
                int dedicatedAgriculturalFits,
                int fallbackAgriculturalFits)
            {
                this.plot = plot;
                this.score = score;
                this.strength = strength;
                this.frontagePriority = frontagePriority;
                this.domesticPriority = domesticPriority;
                this.totalFits = totalFits;
                this.workplaceFits = workplaceFits;
                this.householdFits = householdFits;
                this.mixedUseFits = mixedUseFits;
                this.dedicatedAgriculturalFits = dedicatedAgriculturalFits;
                this.fallbackAgriculturalFits = fallbackAgriculturalFits;
            }

            public readonly TownPlot plot;
            public readonly int score;
            public readonly LandOfferStrength strength;
            public readonly bool frontagePriority;
            public readonly bool domesticPriority;
            public readonly int totalFits;
            public readonly int workplaceFits;
            public readonly int householdFits;
            public readonly int mixedUseFits;
            public readonly int dedicatedAgriculturalFits;
            public readonly int fallbackAgriculturalFits;
        }

        [Header("Generation")]
        [SerializeField]
        private TownGenerationSettings settings;

        [SerializeField]
        private bool generateOnStart = false;

        [SerializeField]
        private bool clearBeforeGenerate = true;

        [Header("Scene References")]
        [SerializeField]
        private Transform visualRoot;

        [SerializeField]
        private Collider terrainCollider;

        [SerializeField]
        private bool createRuntimeFallbackTerrainCollider = true;

        [SerializeField]
        private bool logRuntimeFallbackTerrainCollider;

        [Header("Runtime Terrain Visual Conformance")]
        [Tooltip("Keeps generated visual evidence aligned to Unity Terrain after RegionalTerrainTileView registers a real TerrainCollider. This is visual-only; the hidden grid remains authoritative.")]
        [SerializeField]
        private bool conformGeneratedVisualsToRuntimeTerrain = true;

        [SerializeField]
        private bool logRuntimeTerrainVisualConformance;

        [SerializeField, Min(0f)]
        private float runtimeTerrainVisualSurfaceOffsetMeters = 0.015f;

        [Tooltip("Optional per-pass vertical movement cap for generated visuals. Leave at 0 for a full one-shot conform after terrain builds.")]
        [SerializeField, Range(0f, 8f)]
        private float runtimeTerrainConformMaxSingleMoveMeters;

        [Header("Runtime Validation")]
        [Tooltip("Groups repeated authoring warnings so true generated-world errors are not buried under dozens of similar catalog/prefab notices.")]
        [SerializeField]
        private bool compactWorldValidationReport = true;

        [SerializeField, Min(0)]
        private int worldValidationDetailedWarningLimit = 36;

        [SerializeField, Min(0)]
        private int worldValidationCategorySampleLimit = 3;

        [SerializeField]
        private bool includeWorldValidationCategoryBreakdown = true;

        [SerializeField]
        private bool logRuntimeGenerationTiming = true;

        [Tooltip("When enabled, fresh Play Mode generation asks any RegionalTerrainTileView in the scene to rebuild its visual terrain tiles. This can be expensive; disable in the main scene when startup responsiveness matters more than immediate regional terrain evidence.")]
        [SerializeField]
        private bool refreshRegionalTerrainTilesDuringRuntimeStartup = true;

        [Tooltip("Keeps expensive regional terrain renderer rebuilds out of the pathing/world-ready call stack during Play Mode. The regional data still generates synchronously; only visual terrain evidence is delayed.")]
        [SerializeField]
        private bool deferRegionalTerrainRefreshDuringRuntimeStartup = true;

        [SerializeField, Min(0f)]
        private float deferredRegionalTerrainRefreshDelaySeconds = 0.15f;

        private Coroutine deferredRegionalTerrainRefreshCoroutine;

        [Header("Property Access Evidence")]
        [Tooltip("Draws lightweight visual path evidence from the public frontage/lot approach cell to authored door, service, and drop-off anchors. Visual-only: it does not change grid/pathing authority.")]
        [SerializeField]
        private bool buildPropertyAccessPathEvidence = true;

        [SerializeField]
        private bool logPropertyAccessPathEvidence;

        [SerializeField, Min(0.05f)]
        private float propertyAccessPathWidthMeters = 0.55f;

        [SerializeField, Min(0f)]
        private float propertyAccessPathSurfaceOffsetMeters = 0.08f;

        [SerializeField]
        private Color propertyAccessPathColor = new(0.48f, 0.31f, 0.16f, 0.72f);

        private Collider runtimeFallbackTerrainCollider;
        private int lastRuntimeTerrainConformCandidateCount;
        private int lastRuntimeTerrainConformedVisualCount;
        private int lastRuntimeTerrainAlreadyAlignedVisualCount;
        private int lastRuntimeTerrainOutsideBoundsVisualCount;
        private int lastRuntimeTerrainMissingRenderableVisualCount;
        private int lastRuntimeTerrainCappedMoveCount;
        private float lastRuntimeTerrainConformAverageDeltaMeters;
        private string lastRuntimeTerrainConformStatus = "Not checked";

        [Header("Runtime Summary")]
        [SerializeField]
        private int generatedCellCount;

        [SerializeField]
        private int generatedRoadCellCount;

        [SerializeField]
        private int generatedPlotCount;

        [SerializeField]
        private int generatedBuildingCount;

        [SerializeField]
        private int generatedAnchorCount;

        [SerializeField]
        private string generatedRegionalFoundationSummary = string.Empty;

        private readonly List<TownPlot> plots = new();
        private readonly List<PlacedBuilding> buildings = new();
        private readonly SequentialIdAllocator buildingIdAllocator = new();
        private TownGrid grid;
        private readonly List<BuildingDefinition> fallbackBuildingDefinitions = new();
        private TownHallDefinition fallbackTownHallDefinition;
        private BuildingDefinition fallbackTownHallPhysicalDefinition;
        private PopulationManager cachedPopulationManager;
        private SharedBusinessRuntimeManager cachedSharedBusinessRuntime;
        private GeneralStoreRuntimeManager cachedGeneralStoreRuntime;
        private AcquisitionMarketManager cachedAcquisitionMarket;
        [SerializeField] private RegionalWorldState regionalWorld;
        [SerializeField] private RegionalResourceSnapshot regionalResources = new();
        private Material runtimeBuildingFallbackMaterial;
        private WorldSeedResolution lastSeedResolution;
        private IWorldSurfaceProvider worldSurfaceProvider;
        private PreAuthoredTerrainWorldProfile preAuthoredTerrainProfile;
        private WorldStartupMode currentStartupMode = WorldStartupMode.GenerateProceduralTerrain;
        private string terrainProfileId = string.Empty;
        private int terrainContentRevision;
        private float minimumDepositDistanceFromTownCore;
        private float maximumResourceSlopeDegrees = 28f;
        private float maximumRoadSlopeDegrees = 10f;
        private float maximumBuildingSlopeDegrees = 8f;
        private float maximumBuildingFootprintHeightDifference = 1.25f;
        private float maximumRoadConnectionHeightDifference = 1.5f;
        private bool verboseStartupLogging;
        private bool startupConfigured;
        private Vector3 openingTownAnchor;
        private int generationPassCount;

        public TownGenerationSettings Settings => settings;
        public TownGrid Grid => grid;
        public IReadOnlyList<TownPlot> Plots => plots;
        public IReadOnlyList<PlacedBuilding> Buildings => buildings;
        public int NextBuildingId => buildingIdAllocator.NextId;
        public int AllocateNextBuildingId() => buildingIdAllocator.AllocateNext();
        public void RestoreBuildingIdAllocator(int nextBuildingId)
        {
            buildingIdAllocator.RestoreExact(nextBuildingId);
        }
        public Transform VisualRoot => visualRoot;
        public RegionalWorldState RegionalWorld => regionalWorld;
        public RegionalResourceSnapshot RegionalResources => regionalResources ??= new RegionalResourceSnapshot();
        public string RegionalFoundationSummary => generatedRegionalFoundationSummary;
        public WorldSeedResolution LastSeedResolution => lastSeedResolution;
        public WorldStartupMode CurrentStartupMode => currentStartupMode;
        public string TerrainProfileId => terrainProfileId ?? string.Empty;
        public int TerrainContentRevision => Mathf.Max(0, terrainContentRevision);
        public Vector3 OpeningTownAnchor => openingTownAnchor;
        public int GenerationPassCount => generationPassCount;
        public bool IsStartupConfigured => startupConfigured;
        public bool IsOpeningTownUsingRegionalLocalSpace => settings != null
            && settings.generateRegionalFoundation
            && settings.centerTownGridOnRegionalAnchor
            && settings.keepOpeningTownNearSceneOrigin;

        public Vector2 RegionalToSceneOffsetMeters
        {
            get
            {
                if (!IsOpeningTownUsingRegionalLocalSpace || regionalWorld == null)
                {
                    return Vector2.zero;
                }

                return new Vector2(settings.openingTownSceneCenter.x, settings.openingTownSceneCenter.z)
                    - regionalWorld.AnchorTown.CenterMeters;
            }
        }

        public Vector3 RegionalPointToSceneWorld(Vector2 regionalPoint, float y = 0f)
        {
            Vector2 shifted = regionalPoint + RegionalToSceneOffsetMeters;
            return new Vector3(shifted.x, y, shifted.y);
        }

        public Vector2 SceneWorldToRegionalPoint(Vector3 sceneWorld)
        {
            Vector2 scenePoint = new(sceneWorld.x, sceneWorld.z);
            return scenePoint - RegionalToSceneOffsetMeters;
        }

        public bool TryGetPlotById(int plotId, out TownPlot plot)
        {
            return TryGetPlot(plotId, out plot);
        }

        public bool TryGetBuildingById(int buildingId, out PlacedBuilding building)
        {
            return TryGetBuilding(buildingId, out building);
        }

        public Vector3 GetWorldCenterForRect(GridRect rect, float yOffset = 0f)
        {
            return GetRectWorldCenter(rect, settings != null ? settings.worldCenter.y + yOffset : yOffset);
        }

        public string BuildPlotInspectionSummary(int plotId)
        {
            if (!TryGetPlot(plotId, out TownPlot plot))
            {
                return string.Empty;
            }

            string zone = FormatPlotZone(plot.zone);
            string holding = BuildPlotHoldingRead(plot);
            string use = plot.buildingId >= 0 ? "Improved" : "Vacant";
            string role = FormatPublicSiteRole(plot.publicSiteRole);
            string agRole = FormatAgriculturalSiteRole(plot.agriculturalSiteRole);
            string size = $"{plot.frontageCells}f x {plot.depthCells}d cells";
            string footprint = plot.intendedBuildingFootprintCells.x > 0 && plot.intendedBuildingFootprintCells.y > 0
                ? $"Target build pad {plot.intendedBuildingFootprintCells.x}x{plot.intendedBuildingFootprintCells.y}"
                : "No target build pad";
            string scorecard = BuildPlotScorecard(plot);
            string roleLine = string.Empty;
            if (!string.IsNullOrWhiteSpace(role))
            {
                roleLine = $" | {role}";
            }
            else if (!string.IsNullOrWhiteSpace(agRole))
            {
                roleLine = $" | {agRole}";
            }

            StringBuilder builder = new();
            builder.AppendLine($"Plot {plot.id:000} | {holding} | {zone}{roleLine}");
            builder.AppendLine($"State: {use} | Size: {size}");
            builder.AppendLine(footprint);
            builder.AppendLine(scorecard);

            string marketState = BuildPlotMarketStateSummary(plot);
            if (!string.IsNullOrWhiteSpace(marketState))
            {
                builder.AppendLine(marketState);
            }

            string linkageRead = BuildPlotBuildingLinkInspectionSummary(plot);
            if (!string.IsNullOrWhiteSpace(linkageRead))
            {
                builder.AppendLine(linkageRead);
            }

            string developmentRead = BuildPlotDevelopmentReadSummary(plot);
            if (!string.IsNullOrWhiteSpace(developmentRead))
            {
                builder.AppendLine(developmentRead);
            }

            string lotOpportunity = BuildVacantPlotOpportunitySummary(plot);
            if (!string.IsNullOrWhiteSpace(lotOpportunity))
            {
                builder.AppendLine(lotOpportunity);
            }

            string agriculturalRead = BuildAgriculturalPlotInspectionSummary(plot);
            if (!string.IsNullOrWhiteSpace(agriculturalRead))
            {
                builder.AppendLine(agriculturalRead);
            }

            string resourceRead = BuildRegionalResourceInspectionSummary(GetRectWorldCenter(plot.bounds, settings != null ? settings.worldCenter.y : 0f));
            if (!string.IsNullOrWhiteSpace(resourceRead))
            {
                builder.AppendLine(resourceRead);
            }

            if (plot.buildingId >= 0 && TryGetBuilding(plot.buildingId, out PlacedBuilding building))
            {
                string attachedImprovement = BuildPlotAttachedImprovementSummary(plot, building);
                if (!string.IsNullOrWhiteSpace(attachedImprovement))
                {
                    builder.AppendLine(attachedImprovement);
                }
            }
            else if (TryResolvePopulationManager(out PopulationManager populationManager))
            {
                string nearbySettlement = populationManager.BuildTownSettlementHeadline();
                if (!string.IsNullOrWhiteSpace(nearbySettlement))
                {
                    builder.AppendLine(nearbySettlement);
                }
            }

            if (TryResolveAcquisitionMarket(out AcquisitionMarketManager acquisitionMarket))
            {
                string acquisitionLead = acquisitionMarket.BuildListingInspectionSummaryByPlotId(plotId);
                if (!string.IsNullOrWhiteSpace(acquisitionLead))
                {
                    builder.AppendLine(acquisitionLead);
                }

                string acquisitionReadiness = acquisitionMarket.BuildAcquisitionReadinessHeadline();
                if (!string.IsNullOrWhiteSpace(acquisitionReadiness))
                {
                    builder.AppendLine(acquisitionReadiness);
                }

                string processLedger = acquisitionMarket.BuildAcquisitionProcessLedgerSummary();
                if (!string.IsNullOrWhiteSpace(processLedger))
                {
                    builder.AppendLine(processLedger);
                }

                string decisionClimate = acquisitionMarket.BuildAcquisitionDecisionClimateSummary();
                if (!string.IsNullOrWhiteSpace(decisionClimate))
                {
                    builder.AppendLine(decisionClimate);
                }

                string actionChecklist = acquisitionMarket.BuildSelectedLeadActionChecklist(AcquisitionMarketSection.Land);
                if (string.IsNullOrWhiteSpace(actionChecklist))
                {
                    actionChecklist = acquisitionMarket.BuildSelectedLeadActionChecklist(AcquisitionMarketSection.Businesses);
                }
                if (!string.IsNullOrWhiteSpace(actionChecklist))
                {
                    builder.AppendLine(actionChecklist);
                }
            }

            if (TryResolveSharedBusinessRuntime(out SharedBusinessRuntimeManager townSharedRuntime))
            {
                string commerce = townSharedRuntime.BuildTownCommerceHeadline();
                if (!string.IsNullOrWhiteSpace(commerce))
                {
                    builder.AppendLine(commerce);
                }

                string commerceLedger = townSharedRuntime.BuildTownCommerceLedgerSummary();
                if (!string.IsNullOrWhiteSpace(commerceLedger))
                {
                    builder.AppendLine(commerceLedger);
                }

                string servicePressure = townSharedRuntime.BuildTownServicePressureHeadline();
                if (!string.IsNullOrWhiteSpace(servicePressure))
                {
                    builder.AppendLine(servicePressure);
                }

                string decisionClimate = townSharedRuntime.BuildTownDecisionClimateHeadline();
                if (!string.IsNullOrWhiteSpace(decisionClimate))
                {
                    builder.AppendLine(decisionClimate);
                }

                string processClimate = townSharedRuntime.BuildTownProcessClimateSummary();
                if (!string.IsNullOrWhiteSpace(processClimate))
                {
                    builder.AppendLine(processClimate);
                }
            }

            return builder.ToString().Trim();
        }

        // Plot inspections intentionally carry only a compact improvement preview so the property surface stays scannable.
        // The deeper shell, anchor, and runtime diagnostics remain on direct building inspection.
        private string BuildPlotAttachedImprovementSummary(TownPlot plot, PlacedBuilding building)
        {
            if (plot == null || building == null)
            {
                return string.Empty;
            }

            StringBuilder builder = new();
            string displayName = GetBuildingDisplayName(building);
            string site = building.siteSizeCells.x > 0 && building.siteSizeCells.y > 0
                ? $"Site {building.siteSizeCells.x}x{building.siteSizeCells.y}"
                : $"Footprint {building.footprint.width}x{building.footprint.depth}";
            string frontage = $"Frontage {FormatDirection(building.frontageDirection)}";
            builder.AppendLine($"Attached improvement: {displayName} | {site} | {frontage}");

            AppendInspectionLine(builder, BuildBuildingSiteStateSummary(building, plot));

            if (building.definition != null)
            {
                AppendInspectionLine(builder, $"Shell: {building.definition.BuildUseSummary()}");
                AppendInspectionLine(builder, BuildBuildingSiteFitSummary(plot, building));
            }

            if (TryResolvePopulationManager(out PopulationManager populationManager))
            {
                string householdLead = ExtractFirstInspectionLine(populationManager.BuildHouseholdInspectionSummaryByHomeBuildingId(building.id));
                AppendInspectionLine(builder, householdLead);
            }

            string operationsLead = string.Empty;
            if (TryResolveGeneralStoreRuntime(out GeneralStoreRuntimeManager generalStoreRuntime))
            {
                operationsLead = ExtractFirstInspectionLine(generalStoreRuntime.BuildStoreInspectionSummaryByBuildingId(building.id));
            }

            if (string.IsNullOrWhiteSpace(operationsLead)
                && TryResolveSharedBusinessRuntime(out SharedBusinessRuntimeManager sharedRuntime))
            {
                operationsLead = ExtractFirstInspectionLine(sharedRuntime.BuildBusinessInspectionSummaryByBuildingId(building.id));
            }

            AppendInspectionLine(builder, operationsLead);
            return builder.ToString().TrimEnd();
        }

        private static void AppendInspectionLine(StringBuilder builder, string line)
        {
            if (builder == null || string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            builder.AppendLine(line);
        }

        private static string ExtractFirstInspectionLine(string summary)
        {
            if (string.IsNullOrWhiteSpace(summary))
            {
                return string.Empty;
            }

            string[] lines = summary.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return lines.Length > 0 ? lines[0].Trim() : string.Empty;
        }


        public string BuildBuildingInspectionSummary(int buildingId)
        {
            if (!TryGetBuilding(buildingId, out PlacedBuilding building))
            {
                return string.Empty;
            }

            string displayName = GetBuildingDisplayName(building);
            TownPlot plot = null;
            TryGetPlot(building.plotId, out plot);
            string holding = BuildBuildingHoldingRead(building, plot);
            string footprint = $"Footprint {building.footprint.width}x{building.footprint.depth}";
            string site = building.siteSizeCells.x > 0 && building.siteSizeCells.y > 0
                ? $"Site {building.siteSizeCells.x}x{building.siteSizeCells.y}"
                : footprint;
            string frontage = $"Frontage {FormatDirection(building.frontageDirection)}";
            string role = FormatPublicSiteRole(building.publicSiteRole);
            string roleLine = string.IsNullOrWhiteSpace(role) ? string.Empty : $" | {role}";

            StringBuilder builder = new();
            builder.AppendLine($"{displayName} | {holding}{roleLine}");
            builder.AppendLine($"{site} | {frontage}");

            string linkageRead = BuildBuildingPlotLinkInspectionSummary(building, out plot);
            if (!string.IsNullOrWhiteSpace(linkageRead))
            {
                builder.AppendLine(linkageRead);
            }

            string siteState = BuildBuildingSiteStateSummary(building, plot);
            if (!string.IsNullOrWhiteSpace(siteState))
            {
                builder.AppendLine(siteState);
            }

            string footprintAuthority = BuildFootprintAuthorityInspectionSummary(building);
            if (!string.IsNullOrWhiteSpace(footprintAuthority))
            {
                builder.AppendLine(footprintAuthority);
            }

            string prefabPlacement = BuildPrefabPlacementInspectionSummary(building);
            if (!string.IsNullOrWhiteSpace(prefabPlacement))
            {
                builder.AppendLine(prefabPlacement);
            }

            if (building.plotId >= 0 && TryGetPlot(building.plotId, out plot))
            {
                builder.AppendLine(BuildPlotScorecard(plot));
                string agriculturalRead = BuildAgriculturalBuildingSummary(plot, building);
                if (!string.IsNullOrWhiteSpace(agriculturalRead))
                {
                    builder.AppendLine(agriculturalRead);
                }
            }

            string resourceRead = BuildRegionalResourceInspectionSummary(GetRectWorldCenter(building.footprint, settings != null ? settings.worldCenter.y : 0f));
            if (!string.IsNullOrWhiteSpace(resourceRead))
            {
                builder.AppendLine(resourceRead);
            }

            if (building.definition != null)
            {
                string definitionSummary = building.definition.BuildInspectionSummary();
                if (!string.IsNullOrWhiteSpace(definitionSummary))
                {
                    builder.AppendLine(definitionSummary);
                }

                string siteIntent = building.definition.BuildSiteIntentSummary();
                if (!string.IsNullOrWhiteSpace(siteIntent))
                {
                    builder.AppendLine(siteIntent);
                }

                string siteFit = BuildBuildingSiteFitSummary(plot, building);
                if (!string.IsNullOrWhiteSpace(siteFit))
                {
                    builder.AppendLine(siteFit);
                }

                string improvementRead = BuildImprovementPotentialSummary(plot, building);
                if (!string.IsNullOrWhiteSpace(improvementRead))
                {
                    builder.AppendLine(improvementRead);
                }

                string anchorProfile = building.definition.BuildAnchorExpectationSummary();
                if (!string.IsNullOrWhiteSpace(anchorProfile))
                {
                    builder.AppendLine(anchorProfile);
                }

                string exteriorSummary = BuildExteriorPropInspectionSummary(building);
                if (!string.IsNullOrWhiteSpace(exteriorSummary))
                {
                    builder.AppendLine(exteriorSummary);
                }

                string anchorSummary = BuildAnchorInspectionSummary(building);
                if (!string.IsNullOrWhiteSpace(anchorSummary))
                {
                    builder.AppendLine(anchorSummary);
                }
            }

            if (settings != null)
            {
                builder.AppendLine(BuildConstructionReadinessSummary(building));
            }

            if (TryResolvePopulationManager(out PopulationManager populationManager))
            {
                string householdSummary = populationManager.BuildHouseholdInspectionSummaryByHomeBuildingId(buildingId);
                if (!string.IsNullOrWhiteSpace(householdSummary))
                {
                    builder.AppendLine(householdSummary);
                }
            }

            if (TryResolveGeneralStoreRuntime(out GeneralStoreRuntimeManager generalStoreRuntime))
            {
                string storeSummary = generalStoreRuntime.BuildStoreInspectionSummaryByBuildingId(buildingId);
                if (!string.IsNullOrWhiteSpace(storeSummary))
                {
                    builder.AppendLine(storeSummary);
                }
            }

            if (TryResolveSharedBusinessRuntime(out SharedBusinessRuntimeManager sharedRuntime))
            {
                string businessSummary = sharedRuntime.BuildBusinessInspectionSummaryByBuildingId(buildingId);
                if (!string.IsNullOrWhiteSpace(businessSummary))
                {
                    builder.AppendLine(businessSummary);
                }
            }

            if (TryResolveAcquisitionMarket(out AcquisitionMarketManager acquisitionMarket))
            {
                string acquisitionLead = acquisitionMarket.BuildListingInspectionSummaryByBuildingId(buildingId);
                if (!string.IsNullOrWhiteSpace(acquisitionLead))
                {
                    builder.AppendLine(acquisitionLead);
                }

                string acquisitionReadiness = acquisitionMarket.BuildAcquisitionReadinessHeadline();
                if (!string.IsNullOrWhiteSpace(acquisitionReadiness))
                {
                    builder.AppendLine(acquisitionReadiness);
                }

                string processLedger = acquisitionMarket.BuildAcquisitionProcessLedgerSummary();
                if (!string.IsNullOrWhiteSpace(processLedger))
                {
                    builder.AppendLine(processLedger);
                }

                string decisionClimate = acquisitionMarket.BuildAcquisitionDecisionClimateSummary();
                if (!string.IsNullOrWhiteSpace(decisionClimate))
                {
                    builder.AppendLine(decisionClimate);
                }

                string actionChecklist = acquisitionMarket.BuildSelectedLeadActionChecklist(AcquisitionMarketSection.Businesses);
                if (string.IsNullOrWhiteSpace(actionChecklist))
                {
                    actionChecklist = acquisitionMarket.BuildSelectedLeadActionChecklist(AcquisitionMarketSection.Land);
                }
                if (!string.IsNullOrWhiteSpace(actionChecklist))
                {
                    builder.AppendLine(actionChecklist);
                }
            }

            return builder.ToString().Trim();
        }


        // Plot/building IDs are persistent world identifiers, not guaranteed list positions. These helpers keep
        // inspection and validation honest even when save/load order or future deletion paths stop matching list order.
        private string BuildPlotBuildingLinkInspectionSummary(TownPlot plot)
        {
            if (plot == null || plot.buildingId < 0)
            {
                return string.Empty;
            }

            if (!TryGetBuilding(plot.buildingId, out PlacedBuilding building) || building == null)
            {
                return $"Linkage: Plot points to missing {FormatBuildingLinkTarget(plot.buildingId)}.";
            }

            if (building.plotId != plot.id)
            {
                return $"Linkage: Plot points to Building {building.id:000}, but that building claims {FormatPlotLinkTarget(building.plotId)}.";
            }

            return $"Linkage: Building {building.id:000} attached and reciprocal.";
        }

        private string BuildBuildingPlotLinkInspectionSummary(PlacedBuilding building, out TownPlot plot)
        {
            plot = null;
            if (building == null)
            {
                return string.Empty;
            }

            if (building.plotId < 0)
            {
                return "Linkage: Building is not assigned to any plot.";
            }

            if (!TryGetPlot(building.plotId, out plot) || plot == null)
            {
                return $"Linkage: Building points to missing {FormatPlotLinkTarget(building.plotId)}.";
            }

            if (plot.buildingId != building.id)
            {
                string plotRead = plot.buildingId >= 0
                    ? $"Building {plot.buildingId:000}"
                    : "vacant";
                return $"Linkage: Building points to Plot {plot.id:000}, but that plot currently reads as {plotRead}.";
            }

            return $"Linkage: Plot {plot.id:000} attached and reciprocal.";
        }

        private void ValidatePlotBuildingLinkage(TownPlot plot, StringBuilder report, ref int errors, ref int warnings)
        {
            if (plot == null || plot.buildingId < 0)
            {
                return;
            }

            if (!TryGetBuilding(plot.buildingId, out PlacedBuilding building) || building == null)
            {
                AppendError(report, ref errors, $"Plot {plot.id} points to missing {FormatBuildingLinkTarget(plot.buildingId)}.");
                return;
            }

            if (building.plotId != plot.id)
            {
                AppendError(report, ref errors, $"Plot {plot.id} points to Building {building.id}, but that building points to {FormatPlotLinkTarget(building.plotId)}.");
            }

            if (plot.playerOwned != building.playerOwned)
            {
                AppendWarning(report, ref warnings, $"Plot {plot.id} and Building {building.id} disagree on player ownership.");
            }
        }

        private void ValidateBuildingPlotLinkage(PlacedBuilding building, TownPlot plot, StringBuilder report, ref int errors, ref int warnings)
        {
            if (building == null)
            {
                return;
            }

            if (building.plotId < 0)
            {
                AppendError(report, ref errors, $"Building {building.id} is not assigned to any plot.");
                return;
            }

            if (plot == null)
            {
                AppendError(report, ref errors, $"Building {building.id} points to missing {FormatPlotLinkTarget(building.plotId)}.");
                return;
            }

            if (plot.buildingId != building.id)
            {
                string plotRead = plot.buildingId >= 0
                    ? $"Building {plot.buildingId}"
                    : "vacant";
                AppendError(report, ref errors, $"Building {building.id} points to Plot {plot.id}, but that plot currently reads as {plotRead}.");
            }

            if (plot.playerOwned != building.playerOwned)
            {
                AppendWarning(report, ref warnings, $"Building {building.id} and Plot {plot.id} disagree on player ownership.");
            }
        }

        private static string FormatBuildingLinkTarget(int buildingId)
        {
            return buildingId >= 0 ? $"Building {buildingId:000}" : "no building";
        }

        private static string FormatPlotLinkTarget(int plotId)
        {
            return plotId >= 0 ? $"Plot {plotId:000}" : "no plot";
        }

        private string BuildTownConstructionClimateSummary()
        {
            if (settings == null)
            {
                return string.Empty;
            }

            bool hasLocalMill = TryFindAgriculturalPlot(AgriculturalSiteRole.SawmillYard, out _, out _);
            return settings.BuildConstructionClimateHeadline(hasLocalMill, 0);
        }

        private string BuildConstructionReadinessSummary(PlacedBuilding building)
        {
            if (building == null || settings == null)
            {
                return string.Empty;
            }

            bool hasLocalMill = TryFindAgriculturalPlot(AgriculturalSiteRole.SawmillYard, out _, out _);
            int requiredLumber = settings.EstimateTypicalLumberUnitsForDefinition(building.definition);
            int queueProjectsAhead = 0;
            bool materialTight = !hasLocalMill && building.definition != null && building.definition.CanHostWorkplace;

            if (TryResolveAcquisitionMarket(out AcquisitionMarketManager acquisitionMarket))
            {
                IReadOnlyList<ConstructionSupportNodeState> nodes = acquisitionMarket.ConstructionSupportNodes;
                if (nodes != null)
                {
                    for (int i = 0; i < nodes.Count; i++)
                    {
                        ConstructionSupportNodeState node = nodes[i];
                        if (node != null && node.ResourceKind == ConstructionResourceKind.Lumber)
                        {
                            materialTight = requiredLumber > 0 && node.CurrentStockUnits < requiredLumber;
                            string authority = node.BuildProjectStartAuthoritySummary(settings, building.definition, queueProjectsAhead, requiredLumber);
                            string queueRead = settings.BuildProjectQueueSummary(building.definition, queueProjectsAhead, materialTight);
                            string inputs = settings.BuildProjectInputEstimateSummary(building.definition);
                            return settings.BuildConstructionClimateHeadline(hasLocalMill, queueProjectsAhead) + " " + queueRead + " " + inputs + " " + authority;
                        }
                    }
                }
            }

            string climate = settings.BuildConstructionClimateHeadline(hasLocalMill, queueProjectsAhead);
            string projectRead = settings.BuildProjectQueueSummary(building.definition, queueProjectsAhead, materialTight);
            string inputRead = settings.BuildProjectInputEstimateSummary(building.definition);
            return climate + " " + projectRead + " " + inputRead;
        }


        public void Configure(TownGenerationSettings newSettings, Collider newTerrainCollider, Transform newVisualRoot)
        {
            settings = newSettings;
            terrainCollider = newTerrainCollider;
            visualRoot = newVisualRoot;
        }

        public void ConfigureStartup(
            WorldStartupMode startupMode,
            IWorldSurfaceProvider surfaceProvider,
            string profileId,
            bool verboseLogging,
            float minimumDepositDistance = 0f,
            float maximumResourceSlope = 28f,
            PreAuthoredTerrainWorldProfile authoredProfile = null)
        {
            currentStartupMode = startupMode;
            worldSurfaceProvider = surfaceProvider;
            terrainProfileId = profileId ?? string.Empty;
            preAuthoredTerrainProfile = authoredProfile;
            terrainContentRevision = authoredProfile != null ? authoredProfile.TerrainContentRevision : 0;
            verboseStartupLogging = verboseLogging;
            minimumDepositDistanceFromTownCore = Mathf.Max(0f, minimumDepositDistance);
            maximumResourceSlopeDegrees = Mathf.Clamp(maximumResourceSlope, 0f, 90f);
            maximumRoadSlopeDegrees = authoredProfile != null
                ? authoredProfile.DefaultMaxRoadSlope
                : Mathf.Max(settings != null ? settings.maxBuildableSlopeDegrees : 0f, 10f);
            maximumBuildingSlopeDegrees = authoredProfile != null
                ? authoredProfile.DefaultMaxBuildingSlope
                : settings != null ? settings.maxBuildableSlopeDegrees : 8f;
            maximumBuildingFootprintHeightDifference = authoredProfile != null
                ? authoredProfile.MaximumBuildingFootprintHeightDifference
                : 1.25f;
            maximumRoadConnectionHeightDifference = authoredProfile != null
                ? authoredProfile.MaximumRoadConnectionHeightDifference
                : 1.5f;
            startupConfigured = true;
        }

        public bool TrySampleWorldSurface(Vector3 worldPosition, out WorldSurfaceSample sample)
        {
            sample = default;
            return worldSurfaceProvider != null
                && worldSurfaceProvider.TrySampleSurface(worldPosition, out sample)
                && sample.isInsideWorld
                && IsFinite(sample.position)
                && IsFinite(sample.height)
                && IsFinite(sample.slopeDegrees);
        }

        public Collider RuntimeTerrainCollider => terrainCollider;

        public void SetRuntimeTerrainCollider(Collider newTerrainCollider, bool replaceExisting = false)
        {
            if (newTerrainCollider == null)
            {
                return;
            }

            if (!replaceExisting
                && terrainCollider != null
                && terrainCollider.gameObject != null
                && terrainCollider.gameObject.activeInHierarchy)
            {
                return;
            }

            terrainCollider = newTerrainCollider;
            if (Application.isPlaying && conformGeneratedVisualsToRuntimeTerrain)
            {
                ConformGeneratedVisualsToRuntimeTerrain(logRuntimeTerrainVisualConformance);
            }
        }

        public bool TryResolveRuntimeTerrainCollider(out Collider resolvedCollider)
        {
            if (terrainCollider != null && terrainCollider.gameObject != null && terrainCollider.gameObject.activeInHierarchy)
            {
                resolvedCollider = terrainCollider;
                return true;
            }

            TerrainCollider terrain = FindAnyObjectByType<TerrainCollider>();
            if (terrain != null && terrain.gameObject.activeInHierarchy)
            {
                terrainCollider = terrain;
                resolvedCollider = terrainCollider;
                return true;
            }

            GameObject ground = GameObject.Find("Ground");
            if (ground != null && ground.TryGetComponent(out Collider groundCollider))
            {
                terrainCollider = groundCollider;
                resolvedCollider = terrainCollider;
                return true;
            }

            if (currentStartupMode != WorldStartupMode.UsePreAuthoredTerrain
                && TryCreateRuntimeFallbackTerrainCollider(out Collider fallbackCollider))
            {
                terrainCollider = fallbackCollider;
                resolvedCollider = terrainCollider;
                return true;
            }

            resolvedCollider = null;
            return false;
        }

        private bool TryCreateRuntimeFallbackTerrainCollider(out Collider fallbackCollider)
        {
            fallbackCollider = null;
            if (!createRuntimeFallbackTerrainCollider || !Application.isPlaying)
            {
                return false;
            }

            if (runtimeFallbackTerrainCollider != null
                && runtimeFallbackTerrainCollider.gameObject != null
                && runtimeFallbackTerrainCollider.gameObject.activeInHierarchy)
            {
                fallbackCollider = runtimeFallbackTerrainCollider;
                return true;
            }

            float width = settings != null
                ? Mathf.Max(16f, settings.gridWidthCells * settings.cellSizeMeters)
                : 512f;
            float depth = settings != null
                ? Mathf.Max(16f, settings.gridDepthCells * settings.cellSizeMeters)
                : 512f;
            Vector3 center = settings != null ? settings.worldCenter : transform.position;

            GameObject fallback = new("Runtime Fallback Terrain Collider");
            fallback.transform.SetParent(transform, false);
            fallback.transform.position = new Vector3(center.x, center.y, center.z);
            fallback.transform.rotation = Quaternion.identity;
            fallback.transform.localScale = Vector3.one;
            fallback.layer = gameObject.layer;

            BoxCollider box = fallback.AddComponent<BoxCollider>();
            box.size = new Vector3(width, 0.5f, depth);
            box.center = new Vector3(0f, -0.25f, 0f);
            box.isTrigger = false;

            runtimeFallbackTerrainCollider = box;
            fallbackCollider = box;
            if (logRuntimeFallbackTerrainCollider)
            {
                Debug.Log($"[TownWorld] Created runtime fallback terrain collider {width:0.#}m x {depth:0.#}m at {fallback.transform.position}. Generated TerrainCollider will replace it when RegionalTerrainTileView registers one.", this);
            }

            return true;
        }

        [ContextMenu("Generate Town Shell")]
        public void GenerateTownShell()
        {
            if (Application.isPlaying && FindAnyObjectByType<FirstLedgerSliceBootstrapper>() != null)
            {
                if (!startupConfigured)
                {
                    if (verboseStartupLogging)
                    {
                        Debug.Log("[TownWorld] Fresh generation request ignored until FirstLedgerSliceBootstrapper configures the terrain mode.", this);
                    }

                    return;
                }

                if (generationPassCount > 0 || grid != null)
                {
                    Debug.LogWarning("[TownWorld] Duplicate fresh generation request ignored; FirstLedgerSliceBootstrapper already created the opening town.", this);
                    return;
                }
            }

            Stopwatch totalTimer = ShouldLogRuntimeGenerationTiming() ? Stopwatch.StartNew() : null;
            Stopwatch stepTimer = totalTimer != null ? Stopwatch.StartNew() : null;
            StringBuilder timing = totalTimer != null ? new StringBuilder() : null;

            if (settings == null)
            {
                Debug.LogWarning("TownWorldController cannot generate without TownGenerationSettings.", this);
                return;
            }

            settings.Sanitize();
            ResolveSeedForFreshGeneration(Application.isPlaying, CreateFreshRuntimeSeed());
            AppendGenerationTiming(timing, stepTimer, "settings");

            if (currentStartupMode == WorldStartupMode.UsePreAuthoredTerrain
                && !TryPreparePreAuthoredTownSite(out string sitingError))
            {
                Debug.LogError($"[TownWorld] Fresh world generation stopped: {sitingError}", this);
                return;
            }
            AppendGenerationTiming(timing, stepTimer, "town siting");

            if (currentStartupMode == WorldStartupMode.UsePreAuthoredTerrain
                && !TryValidateRequiredRoadLayout(out string roadValidationError))
            {
                Debug.LogError($"[TownWorld] Fresh world generation stopped before committing data: {roadValidationError}", this);
                return;
            }
            AppendGenerationTiming(timing, stepTimer, "initial road validation");

            if (clearBeforeGenerate)
            {
                ClearGeneratedTown();
            }
            AppendGenerationTiming(timing, stepTimer, "clear");

            generationPassCount++;
            Debug.Log($"[TownWorld] Town generation pass count: {generationPassCount}.", this);

            GenerateRegionalWorldFoundation();
            openingTownAnchor = settings.worldCenter;
            AppendGenerationTiming(timing, stepTimer, "regional foundation");
            grid = new TownGrid(settings.gridWidthCells, settings.gridDepthCells, settings.cellSizeMeters, settings.GridOrigin);
            plots.Clear();
            buildings.Clear();
            generatedRoadCellCount = 0;
            AppendGenerationTiming(timing, stepTimer, "grid init");

            ClassifyTerrain();
            AppendGenerationTiming(timing, stepTimer, "terrain classify");
            StampRoads();
            AppendGenerationTiming(timing, stepTimer, "roads");
            GenerateRegionalResources();
            AppendGenerationTiming(timing, stepTimer, "regional resources");
            GeneratePlots();
            AppendGenerationTiming(timing, stepTimer, "plots");
            PlaceBuildings();
            AppendGenerationTiming(timing, stepTimer, "buildings");
            UpdateSummary();
            AppendGenerationTiming(timing, stepTimer, "summary");
            BuildVisuals();
            AppendGenerationTiming(timing, stepTimer, "visuals");
            if (currentStartupMode == WorldStartupMode.UsePreAuthoredTerrain && conformGeneratedVisualsToRuntimeTerrain)
            {
                ConformGeneratedVisualsToRuntimeTerrain(logRuntimeTerrainVisualConformance);
            }
            AppendGenerationTiming(timing, stepTimer, "terrain conform");
            ValidateWorld();
            AppendGenerationTiming(timing, stepTimer, "validation");
            RefreshRegionalTerrainTilesIfPresent();
            AppendGenerationTiming(timing, stepTimer, "regional terrain refresh request");

            if (totalTimer != null)
            {
                totalTimer.Stop();
                Debug.Log($"[TownWorld] GenerateTownShell timing total {totalTimer.ElapsedMilliseconds} ms ({timing}).", this);
            }
        }

        public bool TryConstructBuildingShell(int plotId, BuildingDefinition definition, bool playerOwned, out PlacedBuilding building, out string message)
        {
            building = null;
            if (definition == null)
            {
                message = "No building definition selected.";
                return false;
            }

            if (settings == null)
            {
                message = "Town generation settings are missing.";
                return false;
            }

            if (grid == null)
            {
                GenerateTownShell();
            }

            if (grid == null)
            {
                message = "Town grid is not generated.";
                return false;
            }

            if (!TryGetPlot(plotId, out TownPlot plot))
            {
                message = $"Plot {plotId:000} is not part of the generated town.";
                return false;
            }

            if (plot.buildingId >= 0)
            {
                message = $"Plot {plot.id:000} already has a building.";
                return false;
            }

            if (!plot.bounds.IsValid || plot.frontageCells <= 0)
            {
                message = $"Plot {plot.id:000} has no valid buildable footprint or road frontage.";
                return false;
            }

            if (!playerOwned && !definition.CanUsePlot(plot.zone))
            {
                message = $"{definition.DisplayName} does not fit {plot.zone} zoning.";
                return false;
            }

            if (!TryBuildFootprint(plot, definition, out GridRect footprint))
            {
                message = $"{definition.DisplayName} does not fit the remaining buildable area on Plot {plot.id:000}.";
                return false;
            }

            building = new PlacedBuilding
            {
                id = AllocateNextBuildingId(),
                plotId = plot.id,
                definition = definition,
                footprint = footprint,
                siteSizeCells = plot.siteSizeCells,
                intendedFootprintSizeCells = ResolvePlacementFootprintSizeCells(definition),
                frontageDirection = plot.roadFrontageDirection,
                playerOwned = playerOwned
            };

            StampFootprintAuthorityDiagnostics(building, true);
            ClaimBuildingFootprint(building);
            CreateAnchors(building);
            buildings.Add(building);
            plot.buildingId = building.id;
            plot.intendedBuildingFootprintCells = ResolvePlacementFootprintSizeCells(definition);
            plot.reservedForLandSale = false;
            if (playerOwned)
            {
                plot.playerOwned = true;
            }

            UpdateSummary();
            BuildVisuals();
            message = $"{definition.DisplayName} building shell completed on Plot {plot.id:000}. Operator assignment and business activation remain pending.";
            return true;
        }

        public bool TryEstablishCivicSite(PublicSiteRole role, BuildingDefinition definition, out PlacedBuilding building, out string message)
        {
            building = null;
            if (role == PublicSiteRole.None)
            {
                message = "No civic site role selected.";
                return false;
            }

            if (definition == null)
            {
                message = "No civic building definition selected.";
                return false;
            }

            if (HasPublicSiteRole(role))
            {
                message = $"{FormatPublicSiteRole(role)} already exists.";
                return false;
            }

            if (grid == null)
            {
                GenerateTownShell();
            }

            TownPlot plot = FindTownHallPlot(definition);
            if (plot == null)
            {
                message = $"No sensible civic parcel can fit {definition.DisplayName}.";
                return false;
            }

            if (!TryConstructBuildingShell(plot.id, definition, false, out building, out message))
            {
                return false;
            }

            building.publicSiteRole = role;
            building.playerOwned = false;
            plot.publicSiteRole = role;
            plot.playerOwned = false;
            plot.reservedForLandSale = false;
            BuildVisuals();
            ValidateWorld();
            message = $"{FormatPublicSiteRole(role)} established on Plot {plot.id:000}.";
            return true;
        }

        public bool TrySurfaceExpansionPlotsAfterLandPurchase(int purchasedPlotId, out int createdCount)
        {
            createdCount = 0;
            if (settings == null || grid == null || purchasedPlotId < 0)
            {
                return false;
            }

            settings.Sanitize();
            int minPlots = Mathf.Max(0, settings.landPurchaseExpansionMinPlots);
            int maxPlots = Mathf.Max(minPlots, settings.landPurchaseExpansionMaxPlots);
            if (maxPlots <= 0)
            {
                return false;
            }

            int requested = CalculateLandPurchaseExpansionPlotCount(purchasedPlotId, minPlots, maxPlots);
            TryStampGrowthRoadExtension(purchasedPlotId);

            List<ExpansionPlotCandidate> candidates = BuildExpansionPlotCandidates(purchasedPlotId);
            for (int i = 0; i < candidates.Count && createdCount < requested; i++)
            {
                ExpansionPlotCandidate candidate = candidates[i];
                if (TryCreatePlot(candidate.bounds, candidate.frontageDirection, candidate.roadAccess, candidate.zone, true))
                {
                    createdCount++;
                }
            }

            if (createdCount <= 0)
            {
                return false;
            }

            UpdateSummary();
            BuildVisuals();
            ValidateWorld();
            return true;
        }

        private int CalculateLandPurchaseExpansionPlotCount(int purchasedPlotId, int minPlots, int maxPlots)
        {
            int requested = Mathf.Clamp(minPlots, 0, maxPlots);
            if (requested < maxPlots)
            {
                int roll = PositiveHash(settings.seed * 37 + purchasedPlotId * 101 + plots.Count * 17) % 10;
                if (roll == 0)
                {
                    requested++;
                }
            }

            return Mathf.Clamp(requested, minPlots, maxPlots);
        }

        private bool TryStampGrowthRoadExtension(int purchasedPlotId)
        {
            if (grid == null || settings == null || plots.Count < 8)
            {
                return false;
            }

            int purchasePressure = Mathf.Max(0, CountPlayerOwnedVacantOrImprovedPlots());
            int roll = PositiveHash(settings.seed * 113 + purchasedPlotId * 41 + purchasePressure * 19) % 4;
            if (purchasePressure < 3 || roll != 0)
            {
                return false;
            }

            int centerX = grid.Width / 2;
            int centerZ = grid.Depth / 2;
            int halfRoad = settings.roadWidthCells / 2;
            int z = Mathf.Clamp(centerZ + (purchasedPlotId % 2 == 0 ? settings.crossStreetSpacingCells : -settings.crossStreetSpacingCells), halfRoad + 2, grid.Depth - halfRoad - 3);
            int startX = purchasedPlotId % 2 == 0 ? centerX + halfRoad + 1 : Mathf.Max(0, centerX - halfRoad - settings.plotDepthCells - 6);
            int endX = purchasedPlotId % 2 == 0 ? Mathf.Min(grid.Width - 1, startX + settings.plotDepthCells + 5) : centerX - halfRoad - 1;
            int created = StampGrowthRoadBand(Mathf.Min(startX, endX), Mathf.Max(startX, endX), z, halfRoad);
            if (created <= 0)
            {
                return false;
            }

            UpdateSummary();
            return true;
        }

        private int CountPlayerOwnedVacantOrImprovedPlots()
        {
            int count = 0;
            for (int i = 0; i < plots.Count; i++)
            {
                if (plots[i] != null && plots[i].playerOwned)
                {
                    count++;
                }
            }

            return count;
        }

        private int StampGrowthRoadBand(int xStart, int xEnd, int centerZ, int halfRoad)
        {
            int created = 0;
            for (int x = Mathf.Max(0, xStart); x <= Mathf.Min(grid.Width - 1, xEnd); x++)
            {
                for (int z = centerZ - halfRoad; z <= centerZ + halfRoad; z++)
                {
                    GridCoord coord = new(x, z);
                    if (!grid.IsInBounds(coord))
                    {
                        continue;
                    }

                    ref TownCell cell = ref grid.GetCellRef(coord);
                    if (cell.HasBuilding || (cell.occupancy & CellOccupancy.Plot) != 0)
                    {
                        continue;
                    }

                    bool wasRoad = cell.IsRoad;
                    MarkRoad(coord, RoadType.Spur);
                    if (!wasRoad && cell.IsRoad)
                    {
                        created++;
                    }
                }
            }

            return created;
        }

        public WorldSaveDto CaptureSaveDto()
        {
            WorldSaveDto dto = new()
            {
                settingsName = settings != null ? settings.name : string.Empty,
                seed = settings != null ? settings.seed : 0,
                hasTerrainStartupMetadata = true,
                startupMode = currentStartupMode,
                terrainProfileId = terrainProfileId ?? string.Empty,
                terrainContentRevision = terrainContentRevision,
                townAnchorX = openingTownAnchor.x,
                townAnchorY = openingTownAnchor.y,
                townAnchorZ = openingTownAnchor.z,
                gridWidthCells = grid != null ? grid.Width : (settings != null ? settings.gridWidthCells : 0),
                gridDepthCells = grid != null ? grid.Depth : (settings != null ? settings.gridDepthCells : 0),
                cellSizeMeters = grid != null ? grid.CellSizeMeters : (settings != null ? settings.cellSizeMeters : 0f),
                gridOriginX = grid != null ? grid.Origin.x : (settings != null ? settings.GridOrigin.x : 0f),
                gridOriginY = grid != null ? grid.Origin.y : (settings != null ? settings.GridOrigin.y : 0f),
                gridOriginZ = grid != null ? grid.Origin.z : (settings != null ? settings.GridOrigin.z : 0f)
            };
            dto.hasExplicitRoadState = grid != null;
            if (grid != null)
            {
                for (int z = 0; z < grid.Depth; z++)
                {
                    for (int x = 0; x < grid.Width; x++)
                    {
                        TownCell cell = grid.GetCell(new GridCoord(x, z));
                        if (!cell.IsRoad)
                        {
                            continue;
                        }

                        dto.roadCells.Add(new RoadCellSaveDto
                        {
                            x = x,
                            z = z,
                            roadType = cell.roadType
                        });
                    }
                }
            }
            if (regionalWorld != null)
            {
                dto.regionalWorld = regionalWorld.CaptureSaveDto();
            }
            dto.hasExplicitResourceState = true;
            dto.regionalResources = RegionalResources.CaptureSaveDto();

            for (int i = 0; i < plots.Count; i++)
            {
                TownPlot plot = plots[i];
                if (plot == null)
                {
                    continue;
                }

                dto.plots.Add(new PlotSaveDto
                {
                    id = plot.id,
                    zone = plot.zone,
                    bounds = ToSaveDto(plot.bounds),
                    candidateFootprint = ToSaveDto(plot.candidateFootprint),
                    siteSizeX = plot.siteSizeCells.x,
                    siteSizeY = plot.siteSizeCells.y,
                    intendedFootprintX = plot.intendedBuildingFootprintCells.x,
                    intendedFootprintY = plot.intendedBuildingFootprintCells.y,
                    agriculturalSiteRole = plot.agriculturalSiteRole,
                    publicSiteRole = plot.publicSiteRole,
                    frontageCells = plot.frontageCells,
                    depthCells = plot.depthCells,
                    roadFrontageDirection = plot.roadFrontageDirection,
                    roadAccessCell = ToSaveDto(plot.roadAccessCell),
                    buildingId = plot.buildingId,
                    reservedForLandSale = plot.reservedForLandSale,
                    playerOwned = plot.playerOwned
                });
            }

            for (int i = 0; i < buildings.Count; i++)
            {
                PlacedBuilding building = buildings[i];
                if (building == null)
                {
                    continue;
                }

                BuildingSaveDto buildingDto = new()
                {
                    id = building.id,
                    plotId = building.plotId,
                    buildingDefinitionId = building.definition != null ? building.definition.BuildingId : string.Empty,
                    footprint = ToSaveDto(building.footprint),
                    siteSizeX = building.siteSizeCells.x,
                    siteSizeY = building.siteSizeCells.y,
                    intendedFootprintX = building.intendedFootprintSizeCells.x,
                    intendedFootprintY = building.intendedFootprintSizeCells.y,
                    frontageDirection = building.frontageDirection,
                    publicSiteRole = building.publicSiteRole,
                    playerOwned = building.playerOwned
                };

                for (int anchorIndex = 0; anchorIndex < building.anchors.Count; anchorIndex++)
                {
                    BuildingAnchor anchor = building.anchors[anchorIndex];
                    buildingDto.anchors.Add(new BuildingAnchorSaveDto
                    {
                        type = anchor.type,
                        coord = ToSaveDto(anchor.coord),
                        fromAuthoredMarker = anchor.fromAuthoredMarker,
                        fromFallbackRule = anchor.fromFallbackRule,
                        source = anchor.source
                    });
                }

                dto.buildings.Add(buildingDto);
            }

            dto.nextBuildingId = NextBuildingId;

            return dto;
        }

        public bool TryValidateSaveTerrainCompatibility(WorldSaveDto dto, out string message)
        {
            message = string.Empty;
            if (dto == null)
            {
                message = "World save data is missing.";
                return false;
            }

            if (currentStartupMode != WorldStartupMode.UsePreAuthoredTerrain)
            {
                return true;
            }

            if (!startupConfigured || worldSurfaceProvider == null)
            {
                message = "Authored terrain startup must be configured before save compatibility can be validated.";
                return false;
            }

            if (!dto.hasTerrainStartupMetadata)
            {
                message = "Legacy save has no terrain profile identity; compatibility cannot be proven.";
                return false;
            }

            if (dto.startupMode != WorldStartupMode.UsePreAuthoredTerrain)
            {
                message = $"Save uses {dto.startupMode}, but the scene is configured for {WorldStartupMode.UsePreAuthoredTerrain}.";
                return false;
            }

            string savedProfileId = dto.terrainProfileId != null ? dto.terrainProfileId.Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(terrainProfileId))
            {
                message = "The current authored terrain profile ID is missing.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(savedProfileId))
            {
                message = "The save does not identify its authored terrain profile.";
                return false;
            }

            if (!string.Equals(terrainProfileId, savedProfileId, StringComparison.Ordinal))
            {
                message = $"Save terrain profile '{savedProfileId}' does not match current profile '{terrainProfileId}'.";
                return false;
            }

            int savedRevision = Mathf.Max(0, dto.terrainContentRevision);
            if (savedRevision > terrainContentRevision)
            {
                message = $"Save terrain revision {savedRevision} is newer than current revision {terrainContentRevision}.";
                return false;
            }

            Vector3 savedAnchor = new(dto.townAnchorX, dto.townAnchorY, dto.townAnchorZ);
            if (!IsFinite(savedAnchor))
            {
                message = "Saved town anchor contains NaN or infinite coordinates.";
                return false;
            }

            TownSiteSelectionRequest townRequest = new()
            {
                townSizeMeters = new Vector2(
                    Mathf.Max(1, dto.gridWidthCells) * Mathf.Max(0.1f, dto.cellSizeMeters),
                    Mathf.Max(1, dto.gridDepthCells) * Mathf.Max(0.1f, dto.cellSizeMeters)),
                minimumEdgeDistanceMeters = preAuthoredTerrainProfile != null
                    ? preAuthoredTerrainProfile.MinimumTownDistanceFromMapEdge
                    : 0f,
                maximumTownSlopeDegrees = preAuthoredTerrainProfile != null
                    ? preAuthoredTerrainProfile.DefaultMaxTownSlope
                    : settings != null ? settings.maxBuildableSlopeDegrees : 8f,
                maximumRoadSlopeDegrees = maximumRoadSlopeDegrees,
                candidateCount = 1,
                validationGridResolution = preAuthoredTerrainProfile != null
                    ? preAuthoredTerrainProfile.TownValidationGridResolution
                    : 5
            };
            if (!TownSiteSelector.TryValidateFixed(
                    worldSurfaceProvider,
                    townRequest,
                    savedAnchor,
                    out TownSiteSelectionResult validatedAnchor,
                    out string anchorError))
            {
                message = $"Saved town anchor is incompatible: {anchorError}.";
                return false;
            }

            if (Mathf.Abs(validatedAnchor.Position.y - savedAnchor.y) > 0.25f)
            {
                message = $"Saved town anchor height {savedAnchor.y:0.###}m no longer matches terrain height {validatedAnchor.Position.y:0.###}m.";
                return false;
            }

            if (dto.plots != null)
            {
                for (int i = 0; i < dto.plots.Count; i++)
                {
                    PlotSaveDto plot = dto.plots[i];
                    string plotError = "saved plot record is missing";
                    if (plot == null
                        || !TryValidateSavedRectSurface(dto, plot.bounds, false, out plotError))
                    {
                        message = $"Saved plot {plot?.id ?? i} is incompatible: {plotError}.";
                        return false;
                    }
                }
            }

            if (dto.buildings != null)
            {
                for (int i = 0; i < dto.buildings.Count; i++)
                {
                    BuildingSaveDto building = dto.buildings[i];
                    string buildingError = "saved building record is missing";
                    if (building == null
                        || !TryValidateSavedRectSurface(dto, building.footprint, true, out buildingError))
                    {
                        message = $"Saved building {building?.id ?? i} is incompatible: {buildingError}.";
                        return false;
                    }
                }
            }

            if (!TryValidateSavedRoadState(dto, out message)
                || !TryValidateSavedResourceState(dto, out message))
            {
                return false;
            }

            if (savedRevision < terrainContentRevision)
            {
                Debug.LogWarning($"[TownWorld] Save terrain revision {savedRevision} validated against current revision {terrainContentRevision}; all saved anchor, road, plot, building, and resource positions remain compatible.", this);
            }

            return true;
        }

        private bool TryValidateSavedRectSurface(
            WorldSaveDto world,
            GridRectSaveDto rect,
            bool forBuilding,
            out string error)
        {
            error = string.Empty;
            if (rect == null || rect.width <= 0 || rect.depth <= 0)
            {
                error = "saved footprint is empty";
                return false;
            }

            int width = Mathf.Max(1, world.gridWidthCells);
            int depth = Mathf.Max(1, world.gridDepthCells);
            if (rect.xMin < 0
                || rect.zMin < 0
                || rect.xMin + rect.width > width
                || rect.zMin + rect.depth > depth)
            {
                error = "saved footprint lies outside the saved grid";
                return false;
            }

            float cellSize = Mathf.Max(0.1f, world.cellSizeMeters);
            float minX = world.gridOriginX + rect.xMin * cellSize;
            float maxX = world.gridOriginX + (rect.xMin + rect.width) * cellSize;
            float minZ = world.gridOriginZ + rect.zMin * cellSize;
            float maxZ = world.gridOriginZ + (rect.zMin + rect.depth) * cellSize;
            float inset = Mathf.Min(cellSize * 0.1f, 0.15f);
            Vector3[] points =
            {
                new((minX + maxX) * 0.5f, world.gridOriginY, (minZ + maxZ) * 0.5f),
                new(minX + inset, world.gridOriginY, minZ + inset),
                new(maxX - inset, world.gridOriginY, minZ + inset),
                new(minX + inset, world.gridOriginY, maxZ - inset),
                new(maxX - inset, world.gridOriginY, maxZ - inset)
            };
            float minHeight = float.MaxValue;
            float maxHeight = float.MinValue;
            for (int i = 0; i < points.Length; i++)
            {
                if (!worldSurfaceProvider.TrySampleSurface(points[i], out WorldSurfaceSample sample)
                    || sample.isWater
                    || sample.isBlocked
                    || !IsFinite(sample.height))
                {
                    error = $"surface sample {i + 1} is outside terrain, water, blocked, or invalid";
                    return false;
                }

                if (forBuilding
                    && !worldSurfaceProvider.IsBuildable(
                        points[i],
                        new PlacementQuery
                        {
                            maximumSlopeDegrees = maximumBuildingSlopeDegrees,
                            forBuilding = true
                        }))
                {
                    error = $"surface sample {i + 1} is NoBuilding or exceeds the slope limit";
                    return false;
                }

                minHeight = Mathf.Min(minHeight, sample.height);
                maxHeight = Mathf.Max(maxHeight, sample.height);
            }

            if (forBuilding && maxHeight - minHeight > maximumBuildingFootprintHeightDifference)
            {
                error = $"footprint height difference {maxHeight - minHeight:0.###}m exceeds {maximumBuildingFootprintHeightDifference:0.###}m";
                return false;
            }

            return true;
        }

        private bool TryValidateSavedRoadState(WorldSaveDto dto, out string error)
        {
            error = string.Empty;
            IEnumerable<RoadCellSaveDto> roads;
            if (dto.hasExplicitRoadState)
            {
                roads = dto.roadCells != null
                    ? dto.roadCells
                    : (IEnumerable<RoadCellSaveDto>)Array.Empty<RoadCellSaveDto>();
            }
            else
            {
                if (settings == null
                    || settings.gridWidthCells != dto.gridWidthCells
                    || settings.gridDepthCells != dto.gridDepthCells
                    || Mathf.Abs(settings.cellSizeMeters - dto.cellSizeMeters) > 0.001f)
                {
                    error = "Legacy save has no explicit road state and current road generation dimensions differ; compatibility cannot be proven.";
                    return false;
                }

                Dictionary<GridCoord, RoadType> legacyLayout = BuildRequiredRoadLayout();
                List<RoadCellSaveDto> legacyRoads = new(legacyLayout.Count);
                foreach (KeyValuePair<GridCoord, RoadType> pair in legacyLayout)
                {
                    legacyRoads.Add(new RoadCellSaveDto { x = pair.Key.x, z = pair.Key.z, roadType = pair.Value });
                }

                roads = legacyRoads;
            }

            Dictionary<GridCoord, float> heights = new();
            foreach (RoadCellSaveDto road in roads)
            {
                if (road == null
                    || road.x < 0
                    || road.x >= dto.gridWidthCells
                    || road.z < 0
                    || road.z >= dto.gridDepthCells)
                {
                    error = "Saved road state contains a missing or out-of-bounds cell.";
                    return false;
                }

                GridCoord coord = new(road.x, road.z);
                if (heights.ContainsKey(coord))
                {
                    error = $"Saved road state contains duplicate cell {coord}.";
                    return false;
                }

                Vector3 point = new(
                    dto.gridOriginX + (road.x + 0.5f) * dto.cellSizeMeters,
                    dto.gridOriginY,
                    dto.gridOriginZ + (road.z + 0.5f) * dto.cellSizeMeters);
                if (!worldSurfaceProvider.TrySampleSurface(point, out WorldSurfaceSample sample)
                    || !worldSurfaceProvider.IsRoadCompatible(
                        point,
                        new RoadQuery { maximumSlopeDegrees = maximumRoadSlopeDegrees }))
                {
                    error = $"Saved road cell {coord} is now outside terrain, in water, Blocked/NoRoad, or too steep.";
                    return false;
                }

                heights.Add(coord, sample.height);
            }

            foreach (KeyValuePair<GridCoord, float> pair in heights)
            {
                GridCoord east = pair.Key + new GridCoord(1, 0);
                GridCoord north = pair.Key + new GridCoord(0, 1);
                if (heights.TryGetValue(east, out float eastHeight)
                    && Mathf.Abs(pair.Value - eastHeight) > maximumRoadConnectionHeightDifference
                    || heights.TryGetValue(north, out float northHeight)
                    && Mathf.Abs(pair.Value - northHeight) > maximumRoadConnectionHeightDifference)
                {
                    error = $"Saved road state has an unsafe height connection at {pair.Key}.";
                    return false;
                }
            }

            return true;
        }

        private bool TryValidateSavedResourceState(WorldSaveDto dto, out string error)
        {
            error = string.Empty;
            RegionalResourceSaveDto resources = dto.regionalResources;
            if (resources == null)
            {
                return !dto.hasExplicitResourceState;
            }

            if (resources.districts != null)
            {
                for (int i = 0; i < resources.districts.Count; i++)
                {
                    MineralDistrictSaveDto district = resources.districts[i];
                    Vector3 position = district != null
                        ? new Vector3(district.centerX, district.centerY, district.centerZ)
                        : new Vector3(float.NaN, 0f, 0f);
                    if (district == null
                        || !IsFinite(position)
                        || !worldSurfaceProvider.IsResourceCompatible(
                            position,
                            new ResourcePlacementQuery
                            {
                                maximumSlopeDegrees = maximumResourceSlopeDegrees,
                                resourceKind = district.kind
                            })
                        || !worldSurfaceProvider.TrySampleHeight(position, out float height)
                        || Mathf.Abs(height - position.y) > 0.25f)
                    {
                        error = $"Saved resource district '{district?.id ?? i.ToString()}' is invalid on the current terrain revision.";
                        return false;
                    }
                }
            }

            if (resources.remoteSites != null)
            {
                for (int i = 0; i < resources.remoteSites.Count; i++)
                {
                    RemoteIndustrySiteSaveDto site = resources.remoteSites[i];
                    Vector3 position = site != null
                        ? new Vector3(site.anchorX, site.anchorY, site.anchorZ)
                        : new Vector3(float.NaN, 0f, 0f);
                    if (site == null
                        || !IsFinite(position)
                        || !worldSurfaceProvider.IsResourceCompatible(
                            position,
                            new ResourcePlacementQuery
                            {
                                maximumSlopeDegrees = maximumResourceSlopeDegrees,
                                resourceKind = site.dominantResource
                            })
                        || !worldSurfaceProvider.TrySampleHeight(position, out float height)
                        || Mathf.Abs(height - position.y) > 0.25f)
                    {
                        error = $"Saved remote resource site '{site?.id ?? i.ToString()}' is invalid on the current terrain revision.";
                        return false;
                    }
                }
            }

            return true;
        }

        private void RestoreSavedRoads(IReadOnlyList<RoadCellSaveDto> roads)
        {
            if (roads == null)
            {
                return;
            }

            for (int i = 0; i < roads.Count; i++)
            {
                RoadCellSaveDto road = roads[i];
                if (road != null)
                {
                    MarkRoad(new GridCoord(road.x, road.z), road.roadType);
                }
            }
        }

        public bool LoadFromSaveDto(WorldSaveDto dto, SaveReferenceResolver resolver, out string message)
        {
            if (dto == null)
            {
                message = "World save data is missing.";
                return false;
            }

            if (!TryValidateSaveTerrainCompatibility(dto, out message))
            {
                return false;
            }

            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<TownGenerationSettings>();
                settings.name = string.IsNullOrWhiteSpace(dto.settingsName) ? "Runtime Loaded Town Settings" : dto.settingsName;
            }

            if (dto.hasTerrainStartupMetadata)
            {
                currentStartupMode = dto.startupMode;
            }

            ApplyGenerationSnapshot(dto);
            openingTownAnchor = dto.hasTerrainStartupMetadata
                ? new Vector3(dto.townAnchorX, dto.townAnchorY, dto.townAnchorZ)
                : settings.worldCenter;
            lastSeedResolution = WorldSeedAuthority.ResolveSaveDataSeed(settings.seed);
            Debug.Log($"[TownWorld]\n{lastSeedResolution.BuildLog()}", this);
            LoadRegionalWorldFoundation(dto);
            ClearGeneratedTown();
            grid = new TownGrid(settings.gridWidthCells, settings.gridDepthCells, settings.cellSizeMeters, settings.GridOrigin);
            generatedRoadCellCount = 0;
            ClassifyTerrain();
            if (dto.hasExplicitRoadState)
            {
                RestoreSavedRoads(dto.roadCells);
            }
            else
            {
                StampRoads();
            }
            regionalResources = dto.hasExplicitResourceState
                ? RegionalResourceSnapshot.FromSaveDto(dto.regionalResources)
                : dto.regionalResources != null && dto.regionalResources.HasRecords
                    ? RegionalResourceSnapshot.FromSaveDto(dto.regionalResources)
                    : RegionalResourceGenerator.Generate(
                    grid,
                    settings,
                    worldSurfaceProvider,
                    openingTownAnchor,
                    minimumDepositDistanceFromTownCore,
                    maximumResourceSlopeDegrees);

            plots.Clear();
            buildings.Clear();
            if (dto.nextBuildingId < 0)
            {
                message = $"World save validation error: persisted nextBuildingId ({dto.nextBuildingId}) is negative.";
                return false;
            }

            RestoreBuildingIdAllocator(dto.nextBuildingId);

            if (dto.plots != null)
            {
                for (int i = 0; i < dto.plots.Count; i++)
                {
                    TownPlot plot = FromSaveDto(dto.plots[i]);
                    plots.Add(plot);
                    StampPlot(plot);
                }
            }

            if (dto.buildings != null)
            {
                for (int i = 0; i < dto.buildings.Count; i++)
                {
                    BuildingSaveDto buildingDto = dto.buildings[i];
                    if (buildingDto == null)
                    {
                        continue;
                    }

                    if (resolver == null || !resolver.TryResolveBuildingDefinition(buildingDto.buildingDefinitionId, out BuildingDefinition definition))
                    {
                        message = $"Missing building definition '{buildingDto.buildingDefinitionId}' for saved building {buildingDto.id:000}.";
                        return false;
                    }

                    PlacedBuilding building = FromSaveDto(buildingDto, definition);
                    StampFootprintAuthorityDiagnostics(building, false);
                    bool rebuiltFootprint = TryRebuildLoadedFootprintFromCurrentPlacementAuthority(buildingDto, building, out GridRect rebuiltFootprintRect);
                    if (rebuiltFootprint)
                    {
                        building.footprint = rebuiltFootprintRect;
                        StampFootprintAuthorityDiagnostics(building, true);
                        building.anchors.Clear();
                        building.anchorIssues.Clear();
                    }

                    buildings.Add(building);
                    ClaimBuildingFootprint(building);
                    if (rebuiltFootprint)
                    {
                        CreateAnchors(building);
                    }
                    else
                    {
                        StampSavedAnchors(building);
                    }

                    InferLegacyAgriculturalPlotRole(buildingDto.buildingDefinitionId, building);
                    InferLegacyPublicSiteRole(buildingDto.buildingDefinitionId, building);
                    SyncPublicSiteRoleToPlot(building);
                }
            }

            for (int i = 0; i < buildings.Count; i++)
            {
                PlacedBuilding building = buildings[i];
                TownPlot plot = GetPlotById(building.plotId);
                if (plot != null)
                {
                    plot.buildingId = building.id;
                    plot.intendedBuildingFootprintCells = building.intendedFootprintSizeCells;
                    if (building.playerOwned)
                    {
                        plot.playerOwned = true;
                    }
                }
            }

            UpdateSummary();
            BuildVisuals();
            if (currentStartupMode == WorldStartupMode.UsePreAuthoredTerrain && conformGeneratedVisualsToRuntimeTerrain)
            {
                ConformGeneratedVisualsToRuntimeTerrain(logRuntimeTerrainVisualConformance);
            }
            ValidateWorld();
            message = $"Loaded town world. Plots={plots.Count}, Buildings={buildings.Count}.";
            return true;
        }

        [ContextMenu("Clear Generated Town")]
        public void ClearGeneratedTown()
        {
            plots.Clear();
            buildings.Clear();
            buildingIdAllocator.RestoreExact(0);
            grid = null;
            generatedCellCount = 0;
            generatedRoadCellCount = 0;
            generatedPlotCount = 0;
            generatedBuildingCount = 0;
            generatedAnchorCount = 0;

            Transform root = EnsureVisualRoot();
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Transform child = root.GetChild(i);
                if (child != null && child.GetComponent<GeneratedWorldContentMarker>() != null)
                {
                    DestroyUnityObject(child.gameObject);
                }
            }
        }

        public bool SetAgriculturalActivityCue(int buildingId, bool active, string blockedReason)
        {
            if (grid == null || settings == null || !TryGetBuilding(buildingId, out PlacedBuilding building) || building == null)
            {
                return false;
            }

            TownPlot plot = GetPlotById(building.plotId);
            if (!IsAgriculturalWorkingSite(plot, building))
            {
                return false;
            }

            Transform root = EnsureVisualRoot();
            ClearAgriculturalActivityCue(root, buildingId);

            Color color = active ? AgricultureActiveCueColor : AgricultureBlockedCueColor;
            Material material = CreateDebugMaterial(color);
            string state = active ? "Active" : "Blocked";
            Vector3 center = GetRectWorldCenter(building.footprint, settings.worldCenter.y + 0.72f);
            float cueSize = Mathf.Clamp(settings.cellSizeMeters * 1.1f, 0.85f, 1.8f);
            CreateWorldBoxVisual(
                root,
                $"{AgricultureActivityCuePrefix} {buildingId:000} {state}",
                center,
                new Vector3(cueSize, 0.36f, cueSize),
                color,
                material);

            return true;
        }

        [ContextMenu("Validate World")]
        public void ValidateWorld()
        {
            if (grid == null)
            {
                Debug.Log("Town world has not been generated yet.", this);
                return;
            }

            int errors = 0;
            int warnings = 0;
            StringBuilder report = new();

            ValidatePlots(report, ref errors, ref warnings);
            ValidateBuildings(report, ref errors, ref warnings);

            if (errors == 0 && warnings == 0)
            {
                Debug.Log($"Town world validation passed. Cells={generatedCellCount}, Roads={generatedRoadCellCount}, Plots={generatedPlotCount}, Buildings={generatedBuildingCount}, Anchors={generatedAnchorCount}.", this);
                return;
            }

            string validationReport = BuildWorldValidationReport(report, errors, warnings);
            Debug.LogWarning($"Town world validation finished with {errors} error(s), {warnings} warning(s):\n{validationReport}", this);
        }

        private void Reset()
        {
            FindDefaultReferences();
        }

        private void Awake()
        {
            FindDefaultReferences();
        }

        private void Start()
        {
            if (generateOnStart
                && Application.isPlaying
                && FindAnyObjectByType<FirstLedgerSliceBootstrapper>() == null)
            {
                GenerateTownShell();
            }
            else if (generateOnStart && Application.isPlaying && verboseStartupLogging)
            {
                Debug.Log("[TownWorld] Automatic Start generation skipped because FirstLedgerSliceBootstrapper owns fresh-world startup.", this);
            }
        }

        private void OnValidate()
        {
            settings?.Sanitize();
            if (visualRoot == null)
            {
                visualRoot = transform.Find(VisualRootName);
            }
        }

        private void FindDefaultReferences()
        {
            if (visualRoot == null)
            {
                visualRoot = transform.Find(VisualRootName);
            }

            if (terrainCollider == null)
            {
                TryResolveRuntimeTerrainCollider(out _);
            }
        }

        private void ClassifyTerrain()
        {
            for (int z = 0; z < grid.Depth; z++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    GridCoord coord = new(x, z);
                    Vector3 center = grid.CoordToWorldCenter(coord, settings.worldCenter.y);
                    TownCell cell = grid.GetCell(coord);

                    bool sampled = TrySampleTerrain(center, out float height, out float slope, out bool traversable);
                    if (sampled || currentStartupMode != WorldStartupMode.UsePreAuthoredTerrain)
                    {
                        cell.height = height;
                        cell.slopeDegrees = slope;
                        cell.terrainZone = height >= settings.minBuildableHeight
                            && height <= settings.maxBuildableHeight
                            && slope <= settings.maxBuildableSlopeDegrees
                            && traversable
                                ? TerrainZone.Buildable
                                : TerrainZone.Steep;
                        cell.blocked = cell.terrainZone != TerrainZone.Buildable;
                    }
                    else
                    {
                        cell.height = settings.worldCenter.y;
                        cell.slopeDegrees = 90f;
                        cell.terrainZone = TerrainZone.Steep;
                        cell.blocked = true;
                    }

                    grid.SetCell(coord, cell);
                }
            }
        }

        private void GenerateRegionalResources()
        {
            regionalResources = RegionalResourceGenerator.Generate(
                grid,
                settings,
                worldSurfaceProvider,
                openingTownAnchor,
                minimumDepositDistanceFromTownCore,
                maximumResourceSlopeDegrees);
        }

        public string BuildRegionalFoundationInspectionSummary()
        {
            if (regionalWorld == null)
            {
                return "Regional foundation: disabled or not generated.";
            }

            int opportunityDigestLimit = settings != null ? settings.regionalInspectionOpportunityDigestLimit : 4;
            string authoringDebugSummary = settings != null ? settings.BuildRegionalDebugSurfaceSummary() : string.Empty;
            return regionalWorld.BuildRegionalFoundationInspectionSummary(opportunityDigestLimit, authoringDebugSummary);
        }

        public string BuildRegionalGameplayReadinessSummary(int maxOpportunityItems = 4)
        {
            return regionalWorld != null
                ? regionalWorld.BuildRegionalGameplayReadinessSummary(maxOpportunityItems)
                : "Regional gameplay read: no regional foundation is available.";
        }

        public string BuildRegionalFoundationDeveloperReport(int maxOpportunityItems = 5)
        {
            if (regionalWorld == null)
            {
                return "Regional Foundation Report\nNo regional foundation is available.";
            }

            string report = regionalWorld.BuildRegionalFoundationDeveloperReport(maxOpportunityItems);
            if (RegionalResources == null || !RegionalResources.HasMeaningfulMineralOpportunity)
            {
                return report;
            }

            return report
                + Environment.NewLine
                + Environment.NewLine
                + "Regional Resource Linkage:"
                + Environment.NewLine
                + RegionalResources.BuildRegionalLinkageSummary(regionalWorld, maxOpportunityItems);
        }

        public string BuildRegionalParcelInspectionSummary(string parcelId)
        {
            if (regionalWorld == null)
            {
                return "Regional parcel inspection: no regional foundation is available.";
            }

            if (!regionalWorld.TryFindParcelById(parcelId, out RegionalSurveyParcelRecord parcel))
            {
                return string.IsNullOrWhiteSpace(parcelId)
                    ? "Regional parcel inspection: no parcel id supplied."
                    : $"Regional parcel inspection: parcel '{parcelId}' was not found.";
            }

            return RegionalParcelAuthority.BuildInspectionReadout(regionalWorld, parcel);
        }

        public string BuildRegionalParcelInspectionSummaryAtWorldPosition(Vector3 worldPosition, float maxDistanceMeters = 640f)
        {
            if (regionalWorld == null)
            {
                return "Regional parcel inspection: no regional foundation is available.";
            }

            Vector2 regionalPoint = new(worldPosition.x, worldPosition.z);
            return regionalWorld.BuildNearestParcelInspectionReadout(regionalPoint, maxDistanceMeters);
        }

        public string BuildRegionalRouteInspectionSummary(string corridorId)
        {
            if (regionalWorld == null)
            {
                return "Regional route inspection: no regional foundation is available.";
            }

            if (!regionalWorld.TryFindRouteById(corridorId, out RegionalRouteCorridorRecord route))
            {
                return string.IsNullOrWhiteSpace(corridorId)
                    ? "Regional route inspection: no corridor id supplied."
                    : $"Regional route inspection: route '{corridorId}' was not found.";
            }

            return route.BuildDetailedRouteInspectionReadout();
        }

        public string BuildRegionalSettlementInspectionSummary(string settlementId)
        {
            if (regionalWorld == null)
            {
                return "Regional settlement inspection: no regional foundation is available.";
            }

            if (!regionalWorld.TryFindSettlementById(settlementId, out RegionalSettlementRecord settlement))
            {
                return string.IsNullOrWhiteSpace(settlementId)
                    ? "Regional settlement inspection: no settlement id supplied."
                    : $"Regional settlement inspection: settlement '{settlementId}' was not found.";
            }

            return settlement.BuildDetailedSettlementInspectionReadout();
        }

        [ContextMenu("Log Regional Foundation Developer Report")]
        private void LogRegionalFoundationDeveloperReport()
        {
            Debug.Log(BuildRegionalFoundationDeveloperReport(), this);
        }

        private string BuildRegionalResourceInspectionSummary(Vector3 worldPosition)
        {
            if (RegionalResources == null || !RegionalResources.HasMeaningfulMineralOpportunity)
            {
                return string.Empty;
            }

            StringBuilder builder = new();
            if (RegionalResources.TryGetStrongestDistrictAt(worldPosition, out MineralDistrictRecord district))
            {
                builder.Append($"Resources: inside {district.Kind.ToString().ToLowerInvariant()} district ({district.Strength01:P0} strength, freight {district.FreightPressure01:P0}, settlement {district.SettlementPressure01:P0})");
            }
            else
            {
                builder.Append("Resources: no district directly under this site");
            }

            if (RegionalResources.TryGetNearestRemoteSite(worldPosition, 140f, out RemoteIndustrySiteRecord site))
            {
                builder.Append($"; nearest proto-site {site.DominantResource.ToString().ToLowerInvariant()} at {Vector3.Distance(worldPosition, site.AnchorPosition):0}m");
            }

            return builder.ToString();
        }

        private void ApplyGenerationSnapshot(WorldSaveDto dto)
        {
            int width = Mathf.Max(1, dto.gridWidthCells);
            int depth = Mathf.Max(1, dto.gridDepthCells);
            float cellSize = Mathf.Max(0.1f, dto.cellSizeMeters);
            settings.gridWidthCells = width;
            settings.gridDepthCells = depth;
            settings.cellSizeMeters = cellSize;
            settings.seed = Mathf.Max(0, dto.seed);
            settings.worldCenter = new Vector3(
                dto.gridOriginX + width * cellSize * 0.5f,
                dto.gridOriginY,
                dto.gridOriginZ + depth * cellSize * 0.5f);
            settings.Sanitize();
        }

        private void ResolveSeedForFreshGeneration(bool runtimeFreshGeneration, int randomCandidateSeed)
        {
            if (settings == null)
            {
                return;
            }

            lastSeedResolution = WorldSeedAuthority.ResolveFreshGenerationSeed(settings, runtimeFreshGeneration, randomCandidateSeed);
            settings.seed = lastSeedResolution.Seed;
            settings.Sanitize();
            if (!string.IsNullOrWhiteSpace(lastSeedResolution.Warning))
            {
                Debug.LogWarning($"[TownWorld] {lastSeedResolution.Warning}", this);
            }

            Debug.Log($"[TownWorld]\n{lastSeedResolution.BuildLog()}", this);
        }

        private static int CreateFreshRuntimeSeed()
        {
            int seed = Guid.NewGuid().GetHashCode() & int.MaxValue;
            return seed > 0 ? seed : 1;
        }

        private void GenerateRegionalWorldFoundation()
        {
            if (settings == null || !settings.generateRegionalFoundation)
            {
                regionalWorld = null;
                return;
            }

            regionalWorld = RegionalWorldGenerator.Generate(settings.seed, settings.BuildRegionalWorldGenerationSettings());
            ApplyRegionalAnchorToTownSettings();
        }

        private void LoadRegionalWorldFoundation(WorldSaveDto dto)
        {
            if (settings == null || !settings.generateRegionalFoundation)
            {
                regionalWorld = null;
                return;
            }

            if (dto != null && dto.regionalWorld != null && dto.regionalWorld.surveyParcels != null && dto.regionalWorld.surveyParcels.Count > 0)
            {
                regionalWorld = RegionalWorldState.FromSaveDto(dto.regionalWorld);
                ApplyRegionalAnchorToTownSettings();
                return;
            }

            regionalWorld = RegionalWorldGenerator.Generate(settings.seed, settings.BuildRegionalWorldGenerationSettings());
            ApplyRegionalAnchorToTownSettings();
        }

        private void ApplyRegionalAnchorToTownSettings()
        {
            if (settings == null || regionalWorld == null || !settings.centerTownGridOnRegionalAnchor)
            {
                return;
            }

            if (currentStartupMode == WorldStartupMode.UsePreAuthoredTerrain)
            {
                return;
            }

            if (settings.keepOpeningTownNearSceneOrigin)
            {
                settings.worldCenter = new Vector3(
                    settings.openingTownSceneCenter.x,
                    settings.openingTownSceneCenter.y,
                    settings.openingTownSceneCenter.z);
                settings.Sanitize();
                return;
            }

            Vector2 anchor = regionalWorld.AnchorTown.CenterMeters;
            settings.worldCenter = new Vector3(anchor.x, settings.worldCenter.y, anchor.y);
            settings.Sanitize();
        }

        private bool TryPreparePreAuthoredTownSite(out string error)
        {
            if (worldSurfaceProvider == null)
            {
                error = "UsePreAuthoredTerrain requires a valid PreAuthoredTerrainWorldProfile/provider. Procedural terrain will not be used as a fallback.";
                return false;
            }

            TownSiteSelectionRequest request = new()
            {
                townSizeMeters = new Vector2(
                    settings.gridWidthCells * settings.cellSizeMeters,
                    settings.gridDepthCells * settings.cellSizeMeters),
                minimumEdgeDistanceMeters = 0f,
                maximumTownSlopeDegrees = settings.maxBuildableSlopeDegrees,
                maximumRoadSlopeDegrees = Mathf.Max(settings.maxBuildableSlopeDegrees, 10f),
                candidateCount = 96,
                validationGridResolution = 5
            };

            PreAuthoredTerrainWorldProfile profile = preAuthoredTerrainProfile != null
                ? preAuthoredTerrainProfile
                : FindAnyObjectByType<PreAuthoredTerrainWorldProfile>();
            if (profile != null)
            {
                request.minimumEdgeDistanceMeters = profile.MinimumTownDistanceFromMapEdge;
                request.maximumTownSlopeDegrees = profile.DefaultMaxTownSlope;
                request.maximumRoadSlopeDegrees = profile.DefaultMaxRoadSlope;
                request.candidateCount = profile.TownCandidateCount;
                request.validationGridResolution = profile.TownValidationGridResolution;
            }

            TownSiteSelectionResult selected;
            if (profile != null && profile.UseFixedTownAnchor)
            {
                if (profile.FixedTownAnchor == null)
                {
                    error = "Fixed authored town anchor mode is enabled, but the profile has no assigned anchor Transform.";
                    return false;
                }

                if (!TownSiteSelector.TryValidateFixed(
                        worldSurfaceProvider,
                        request,
                        profile.FixedTownAnchor.position,
                        out selected,
                        out string fixedAnchorFailure))
                {
                    error = $"Fixed authored town anchor '{profile.FixedTownAnchor.name}' is invalid: {fixedAnchorFailure}.";
                    return false;
                }

                Debug.Log($"[TownSiting] Fixed authored town anchor '{profile.FixedTownAnchor.name}' validated at {selected.Position}.", this);
            }
            else if (!TownSiteSelector.TrySelect(worldSurfaceProvider, request, settings.seed, verboseStartupLogging, out selected))
            {
                error = "No authored-terrain town candidate met the bounds, slope, water, mask, footprint, and road-spine constraints.";
                return false;
            }

            openingTownAnchor = selected.Position;
            settings.worldCenter = selected.Position;
            settings.openingTownSceneCenter = selected.Position;
            settings.Sanitize();
            if (verboseStartupLogging)
            {
                Debug.Log($"[TownSiting] Selected {selected.Position} from {selected.ValidCandidates}/{selected.EvaluatedCandidates} valid candidates (score {selected.Score:0.000}, seed {settings.seed}).", this);
            }

            error = string.Empty;
            return true;
        }

        private void RefreshRegionalTerrainTilesIfPresent()
        {
            if (regionalWorld == null)
            {
                return;
            }

            if (currentStartupMode == WorldStartupMode.UsePreAuthoredTerrain)
            {
                Debug.Log("[TownWorld] Runtime regional terrain generation: SKIPPED (pre-authored terrain mode).", this);
                return;
            }

            if (Application.isPlaying && !refreshRegionalTerrainTilesDuringRuntimeStartup)
            {
                if (ShouldLogRuntimeGenerationTiming())
                {
                    Debug.Log("[TownWorld] Regional terrain visual refresh skipped during runtime startup by scene setting. Regional data remains generated; rebuild RegionalTerrainTileView manually when visual terrain evidence is needed.", this);
                }

                return;
            }

            if (Application.isPlaying && deferRegionalTerrainRefreshDuringRuntimeStartup)
            {
                if (deferredRegionalTerrainRefreshCoroutine != null)
                {
                    StopCoroutine(deferredRegionalTerrainRefreshCoroutine);
                }

                deferredRegionalTerrainRefreshCoroutine = StartCoroutine(RefreshRegionalTerrainTilesDeferred());
                return;
            }

            RebuildRegionalTerrainTileViewsNow();
        }

        private IEnumerator RefreshRegionalTerrainTilesDeferred()
        {
            if (deferredRegionalTerrainRefreshDelaySeconds > 0f)
            {
                yield return new WaitForSecondsRealtime(deferredRegionalTerrainRefreshDelaySeconds);
            }
            else
            {
                yield return null;
            }

            deferredRegionalTerrainRefreshCoroutine = null;
            RebuildRegionalTerrainTileViewsNow();
        }

        private void RebuildRegionalTerrainTileViewsNow()
        {
            Stopwatch timer = ShouldLogRuntimeGenerationTiming() ? Stopwatch.StartNew() : null;
            RegionalTerrainTileView[] terrainViews = FindObjectsByType<RegionalTerrainTileView>(FindObjectsInactive.Exclude);
            int rebuilt = 0;
            for (int i = 0; i < terrainViews.Length; i++)
            {
                if (terrainViews[i] != null)
                {
                    terrainViews[i].RebuildTiles();
                    rebuilt++;
                }
            }

            if (timer != null)
            {
                timer.Stop();
                Debug.Log($"[TownWorld] Regional terrain refresh dispatched to {rebuilt} view(s) in {timer.ElapsedMilliseconds} ms.", this);
            }
        }

        private bool ShouldLogRuntimeGenerationTiming()
        {
            return logRuntimeGenerationTiming && Application.isPlaying;
        }

        private static void AppendGenerationTiming(StringBuilder builder, Stopwatch timer, string label)
        {
            if (builder == null || timer == null)
            {
                return;
            }

            timer.Stop();
            if (builder.Length > 0)
            {
                builder.Append(", ");
            }

            builder.Append(label);
            builder.Append(' ');
            builder.Append(timer.ElapsedMilliseconds);
            builder.Append(" ms");
            timer.Restart();
        }

        private void StampPlot(TownPlot plot)
        {
            if (plot == null || grid == null || !plot.bounds.IsValid)
            {
                return;
            }

            foreach (GridCoord coord in TownGrid.EachCoord(plot.bounds))
            {
                if (!grid.IsInBounds(coord))
                {
                    continue;
                }

                ref TownCell cell = ref grid.GetCellRef(coord);
                cell.occupancy |= CellOccupancy.Plot;
                cell.plotId = plot.id;
            }
        }

        private void StampSavedAnchors(PlacedBuilding building)
        {
            if (building == null || grid == null)
            {
                return;
            }

            for (int i = 0; i < building.anchors.Count; i++)
            {
                BuildingAnchor anchor = building.anchors[i];
                if (!grid.IsInBounds(anchor.coord))
                {
                    continue;
                }

                ref TownCell cell = ref grid.GetCellRef(anchor.coord);
                cell.occupancy |= CellOccupancy.Anchor;
            }
        }

        private bool TryRebuildLoadedFootprintFromCurrentPlacementAuthority(BuildingSaveDto dto, PlacedBuilding building, out GridRect rebuilt)
        {
            rebuilt = default;
            if (dto == null || building == null || building.definition == null)
            {
                return false;
            }

            Vector2Int currentIntentSize = ResolvePlacementFootprintSizeCells(building.definition);
            Vector2Int currentWorldSize = GetPlacementWorldFootprintSize(building.definition, building.frontageDirection);
            GridRect savedFootprint = FromSaveDto(dto.footprint);
            bool savedFootprintMatchesCurrentAuthority = savedFootprint.IsValid
                && savedFootprint.width == currentWorldSize.x
                && savedFootprint.depth == currentWorldSize.y;
            bool savedIntentMatchesCurrentAuthority = dto.intendedFootprintX == currentIntentSize.x
                && dto.intendedFootprintY == currentIntentSize.y;

            building.loadedFromSave = true;
            building.savedFootprintBeforeReconciliation = savedFootprint;
            building.savedIntendedFootprintSizeCells = new Vector2Int(dto.intendedFootprintX, dto.intendedFootprintY);
            building.savedAnchorCount = dto.anchors != null ? dto.anchors.Count : 0;

            if (savedFootprintMatchesCurrentAuthority && savedIntentMatchesCurrentAuthority)
            {
                building.footprintMatchedCurrentAuthorityOnLoad = true;
                building.savedAnchorsPreservedOnLoad = building.savedAnchorCount > 0;
                building.footprintReconciliationNote = $"Loaded footprint already matches current placement authority {currentIntentSize.x}x{currentIntentSize.y}.";
                return false;
            }

            TownPlot plot = GetPlotById(building.plotId);
            if (plot == null || !TryBuildFootprint(plot, building.definition, out rebuilt))
            {
                building.savedFootprintPreservedAfterAuthorityMismatch = true;
                building.savedAnchorsPreservedOnLoad = building.savedAnchorCount > 0;
                building.footprintReconciliationNote = $"Saved footprint {savedFootprint.width}x{savedFootprint.depth} with intended {dto.intendedFootprintX}x{dto.intendedFootprintY} differs from current placement authority {currentIntentSize.x}x{currentIntentSize.y}, but the current authority does not fit the saved plot. Preserved saved grid claim; do not fix by scaling the prefab.";
                Debug.LogWarning(
                    $"Saved building '{building.definition.BuildingId}' on Plot {building.plotId:000} has footprint {savedFootprint.width}x{savedFootprint.depth} / intended {dto.intendedFootprintX}x{dto.intendedFootprintY}, while current placement authority is {currentIntentSize.x}x{currentIntentSize.y}. The current authority does not fit the saved plot, so the saved footprint was preserved. Do not solve this by scaling the prefab.",
                    this);
                return false;
            }

            building.footprintRebuiltFromCurrentAuthorityOnLoad = true;
            building.savedAnchorsRegeneratedOnLoad = true;
            building.footprintReconciliationNote = $"Updated saved footprint {savedFootprint.width}x{savedFootprint.depth} to current placement authority {currentIntentSize.x}x{currentIntentSize.y}; anchors were regenerated from current authoring.";
            Debug.Log(
                $"Updated saved building '{building.definition.BuildingId}' on Plot {building.plotId:000} from footprint {savedFootprint.width}x{savedFootprint.depth} / intended {dto.intendedFootprintX}x{dto.intendedFootprintY} to current placement authority {currentIntentSize.x}x{currentIntentSize.y}.",
                this);
            return true;
        }

        private TownPlot GetPlotById(int plotId)
        {
            for (int i = 0; i < plots.Count; i++)
            {
                if (plots[i] != null && plots[i].id == plotId)
                {
                    return plots[i];
                }
            }

            return null;
        }

        private static bool IsAgriculturalWorkingSite(TownPlot plot, PlacedBuilding building)
        {
            if (plot == null
                || plot.zone != PlotZone.Agricultural
                || building == null
                || building.definition == null)
            {
                return false;
            }

            return ResolveAgriculturalSiteRole(plot, building) != AgriculturalSiteRole.None;
        }

        private static AgriculturalSiteRole ResolveAgriculturalSiteRole(TownPlot plot, PlacedBuilding building)
        {
            if (plot != null && plot.agriculturalSiteRole != AgriculturalSiteRole.None)
            {
                return plot.agriculturalSiteRole;
            }

            return building != null && building.definition != null
                ? building.definition.AgriculturalSiteRole
                : AgriculturalSiteRole.None;
        }

        private void InferLegacyAgriculturalPlotRole(string buildingDefinitionId, PlacedBuilding building)
        {
            AgriculturalSiteRole role = GetLegacyAgriculturalSiteRole(buildingDefinitionId);
            if (role == AgriculturalSiteRole.None && building != null && building.definition != null)
            {
                role = building.definition.AgriculturalSiteRole;
            }

            if (role == AgriculturalSiteRole.None || building == null)
            {
                return;
            }

            TownPlot plot = GetPlotById(building.plotId);
            if (plot != null && plot.agriculturalSiteRole == AgriculturalSiteRole.None)
            {
                plot.agriculturalSiteRole = role;
            }
        }

        private static AgriculturalSiteRole GetLegacyAgriculturalSiteRole(string buildingDefinitionId)
        {
            if (string.Equals(buildingDefinitionId, LegacyCropFarmShellBuildingId, StringComparison.OrdinalIgnoreCase))
            {
                return AgriculturalSiteRole.CropProductionYard;
            }

            if (string.Equals(buildingDefinitionId, LegacyRanchShellBuildingId, StringComparison.OrdinalIgnoreCase))
            {
                return AgriculturalSiteRole.LivestockYard;
            }

            return AgriculturalSiteRole.None;
        }

        private void InferLegacyPublicSiteRole(string buildingDefinitionId, PlacedBuilding building)
        {
            if (building == null
                || !string.Equals(buildingDefinitionId, LegacyTownHallBuildingId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            building.publicSiteRole = PublicSiteRole.TownHall;
        }

        private void SyncPublicSiteRoleToPlot(PlacedBuilding building)
        {
            if (building == null || building.publicSiteRole == PublicSiteRole.None)
            {
                return;
            }

            TownPlot plot = GetPlotById(building.plotId);
            if (plot != null && plot.publicSiteRole == PublicSiteRole.None)
            {
                plot.publicSiteRole = building.publicSiteRole;
            }
        }

        private static void ClearAgriculturalActivityCue(Transform root, int buildingId)
        {
            if (root == null)
            {
                return;
            }

            string prefix = $"{AgricultureActivityCuePrefix} {buildingId:000}";
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Transform child = root.GetChild(i);
                if (child != null && child.name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    DestroyUnityObject(child.gameObject);
                }
            }
        }

        private bool TrySampleTerrain(Vector3 cellCenter, out float height, out float slopeDegrees, out bool buildable)
        {
            if (worldSurfaceProvider != null
                && worldSurfaceProvider.TrySampleSurface(cellCenter, out WorldSurfaceSample surfaceSample))
            {
                if (!surfaceSample.isInsideWorld
                    || !IsFinite(surfaceSample.position)
                    || !IsFinite(surfaceSample.height)
                    || !IsFinite(surfaceSample.slopeDegrees))
                {
                    height = settings.worldCenter.y;
                    slopeDegrees = 90f;
                    buildable = false;
                    return false;
                }

                height = surfaceSample.height;
                slopeDegrees = surfaceSample.slopeDegrees;
                buildable = !surfaceSample.isWater && !surfaceSample.isBlocked;
                return true;
            }

            if (currentStartupMode == WorldStartupMode.UsePreAuthoredTerrain)
            {
                height = settings.worldCenter.y;
                slopeDegrees = 90f;
                buildable = false;
                return false;
            }

            Vector3 origin = new(cellCenter.x, settings.worldCenter.y + settings.terrainRaycastHeight, cellCenter.z);
            Ray ray = new(origin, Vector3.down);

            if (terrainCollider != null && terrainCollider.Raycast(ray, out RaycastHit terrainHit, settings.terrainRaycastDistance))
            {
                height = terrainHit.point.y;
                slopeDegrees = Vector3.Angle(Vector3.up, terrainHit.normal);
                buildable = true;
                return true;
            }

            if (Physics.Raycast(
                    ray,
                    out RaycastHit hit,
                    settings.terrainRaycastDistance,
                    settings.terrainLayers,
                    QueryTriggerInteraction.Ignore))
            {
                height = hit.point.y;
                slopeDegrees = Vector3.Angle(Vector3.up, hit.normal);
                buildable = true;
                return true;
            }

            height = settings.worldCenter.y;
            slopeDegrees = 0f;
            buildable = true;
            return false;
        }

        private void StampRoads()
        {
            Dictionary<GridCoord, RoadType> layout = BuildRequiredRoadLayout();
            List<KeyValuePair<GridCoord, RoadType>> ordered = new(layout);
            ordered.Sort((left, right) =>
            {
                int z = left.Key.z.CompareTo(right.Key.z);
                return z != 0 ? z : left.Key.x.CompareTo(right.Key.x);
            });

            for (int i = 0; i < ordered.Count; i++)
            {
                MarkRoad(ordered[i].Key, ordered[i].Value);
            }
        }

        private bool TryValidateRequiredRoadLayout(out string error)
        {
            error = string.Empty;
            if (worldSurfaceProvider == null)
            {
                error = "the authored surface provider is missing during required-road validation";
                return false;
            }

            Dictionary<GridCoord, RoadType> layout = BuildRequiredRoadLayout();
            if (layout.Count == 0)
            {
                error = "the configured starting-road layout contains no cells";
                return false;
            }

            TownGrid validationGrid = new(
                settings.gridWidthCells,
                settings.gridDepthCells,
                settings.cellSizeMeters,
                settings.GridOrigin);
            Dictionary<GridCoord, float> heights = new();
            foreach (KeyValuePair<GridCoord, RoadType> pair in layout)
            {
                if (!validationGrid.IsInBounds(pair.Key))
                {
                    error = $"required {pair.Value} cell {pair.Key} lies outside the town grid";
                    return false;
                }

                Vector3 point = validationGrid.CoordToWorldCenter(pair.Key, settings.worldCenter.y);
                if (!worldSurfaceProvider.TrySampleSurface(point, out WorldSurfaceSample sample))
                {
                    error = $"required {pair.Value} cell {pair.Key} at {point} could not be sampled";
                    return false;
                }

                if (!IsFinite(sample.height)
                    || !worldSurfaceProvider.IsRoadCompatible(
                        point,
                        new RoadQuery { maximumSlopeDegrees = maximumRoadSlopeDegrees }))
                {
                    error = $"required {pair.Value} cell {pair.Key} crosses water, Blocked/NoRoad masks, invalid terrain, or excessive slope";
                    return false;
                }

                heights[pair.Key] = sample.height;
            }

            GridCoord[] forwardNeighbors = { new(1, 0), new(0, 1) };
            foreach (KeyValuePair<GridCoord, float> pair in heights)
            {
                for (int i = 0; i < forwardNeighbors.Length; i++)
                {
                    GridCoord neighbor = pair.Key + forwardNeighbors[i];
                    if (!heights.TryGetValue(neighbor, out float neighborHeight))
                    {
                        continue;
                    }

                    float difference = Mathf.Abs(pair.Value - neighborHeight);
                    if (difference > maximumRoadConnectionHeightDifference)
                    {
                        error = $"required road connection {pair.Key}->{neighbor} changes height by {difference:0.###}m, above the {maximumRoadConnectionHeightDifference:0.###}m safe threshold";
                        return false;
                    }
                }
            }

            return true;
        }

        private Dictionary<GridCoord, RoadType> BuildRequiredRoadLayout()
        {
            int width = Mathf.Max(1, settings.gridWidthCells);
            int depth = Mathf.Max(1, settings.gridDepthCells);
            int centerX = width / 2;
            int centerZ = depth / 2;
            int halfRoad = settings.roadWidthCells / 2;
            int mainLength = Mathf.Min(settings.mainStreetLengthCells, depth);
            int mainStart = Mathf.Max(0, centerZ - mainLength / 2);
            int mainEnd = Mathf.Min(depth - 1, mainStart + mainLength - 1);
            Dictionary<GridCoord, RoadType> result = new();

            AddPlannedVerticalBand(result, centerX, mainStart, mainEnd, halfRoad, width, depth, RoadType.MainStreet);
            for (int i = 0; i < settings.crossStreetCount; i++)
            {
                float indexOffset = i - (settings.crossStreetCount - 1) * 0.5f;
                int z = centerZ + Mathf.RoundToInt(indexOffset * settings.crossStreetSpacingCells);
                int halfLength = settings.crossStreetLengthCells / 2;
                AddPlannedHorizontalBand(result, centerX - halfLength, centerX + halfLength, z, halfRoad, width, depth, RoadType.CrossStreet);
            }

            AddPlannedVerticalBand(result, centerX, 0, mainStart - 1, halfRoad, width, depth, RoadType.RegionalExit);
            AddPlannedVerticalBand(result, centerX, mainEnd + 1, depth - 1, halfRoad, width, depth, RoadType.RegionalExit);
            AddPlannedHorizontalBand(result, 0, width - 1, centerZ, halfRoad, width, depth, RoadType.RegionalExit);

            Vector2Int cropSize = new(
                Mathf.Clamp(Mathf.Max(1, settings.cropFarmParcelSizeCells.x), 1, Mathf.Max(1, width / 3)),
                Mathf.Clamp(Mathf.Max(1, settings.cropFarmParcelSizeCells.y), 1, Mathf.Max(1, depth / 3)));
            Vector2Int ranchSize = new(
                Mathf.Clamp(Mathf.Max(cropSize.x, settings.ranchParcelSizeCells.x), 1, Mathf.Max(1, width / 3)),
                Mathf.Clamp(Mathf.Max(cropSize.y, settings.ranchParcelSizeCells.y), 1, Mathf.Max(1, depth / 3)));

            if (settings.generateAgriculturalParcels && width >= 12 && depth >= 24)
            {
                int lengthMinimum = Mathf.Max(cropSize.x, ranchSize.x) + 4;
                int lengthMaximum = Mathf.Max(lengthMinimum, width / 2 - settings.roadWidthCells - 2);
                int length = Mathf.Clamp(Mathf.Max(settings.agricultureSpurRoadLengthCells, lengthMinimum), lengthMinimum, lengthMaximum);
                int westEnd = centerX - halfRoad - 1;
                int westStart = Mathf.Max(0, westEnd - length + 1);
                int cropRoadZ = Mathf.Clamp(
                    centerZ + settings.agricultureDistanceFromCenterCells,
                    halfRoad + 1,
                    Mathf.Max(halfRoad + 1, depth - halfRoad - cropSize.y - 2));
                AddPlannedHorizontalBand(result, westStart, westEnd, cropRoadZ, halfRoad, width, depth, RoadType.Spur);

                int eastStart = centerX + halfRoad + 1;
                int eastEnd = Mathf.Min(width - 1, eastStart + length - 1);
                int ranchRoadZ = Mathf.Clamp(
                    centerZ - settings.agricultureDistanceFromCenterCells,
                    halfRoad + ranchSize.y + 1,
                    Mathf.Max(halfRoad + ranchSize.y + 1, depth - halfRoad - 2));
                AddPlannedHorizontalBand(result, eastStart, eastEnd, ranchRoadZ, halfRoad, width, depth, RoadType.Spur);
            }

            if (settings.generateSawmillProperty && width >= 48 && depth >= 56)
            {
                Vector2Int sawmillSize = new(
                    Mathf.Clamp(Mathf.Max(ranchSize.x, settings.sawmillParcelSizeCells.x), 1, Mathf.Max(1, width / 3)),
                    Mathf.Clamp(Mathf.Max(ranchSize.y, settings.sawmillParcelSizeCells.y), 1, Mathf.Max(1, depth / 3)));
                int roadZ = Mathf.Clamp(
                    centerZ + settings.sawmillDistanceFromCenterCells,
                    halfRoad + 1,
                    Mathf.Max(halfRoad + 1, depth - halfRoad - sawmillSize.y - 2));
                if (roadZ > mainEnd)
                {
                    AddPlannedVerticalBand(result, centerX, mainEnd + 1, roadZ, halfRoad, width, depth, RoadType.Spur);
                }

                int lengthMinimum = Mathf.Max(1, sawmillSize.x) + 6;
                int lengthMaximum = Mathf.Max(lengthMinimum, width / 2 - settings.roadWidthCells - 2);
                int length = Mathf.Clamp(Mathf.Max(settings.sawmillSpurRoadLengthCells, lengthMinimum), lengthMinimum, lengthMaximum);
                int westEnd = centerX - halfRoad - 1;
                AddPlannedHorizontalBand(result, Mathf.Max(0, westEnd - length + 1), westEnd, roadZ, halfRoad, width, depth, RoadType.Spur);
            }

            return result;
        }

        private static void AddPlannedHorizontalBand(
            Dictionary<GridCoord, RoadType> layout,
            int xStart,
            int xEnd,
            int centerZ,
            int halfRoad,
            int width,
            int depth,
            RoadType roadType)
        {
            for (int x = Mathf.Min(xStart, xEnd); x <= Mathf.Max(xStart, xEnd); x++)
            {
                for (int z = centerZ - halfRoad; z <= centerZ + halfRoad; z++)
                {
                    AddPlannedRoadCell(layout, new GridCoord(x, z), width, depth, roadType);
                }
            }
        }

        private static void AddPlannedVerticalBand(
            Dictionary<GridCoord, RoadType> layout,
            int centerX,
            int zStart,
            int zEnd,
            int halfRoad,
            int width,
            int depth,
            RoadType roadType)
        {
            for (int z = Mathf.Min(zStart, zEnd); z <= Mathf.Max(zStart, zEnd); z++)
            {
                for (int x = centerX - halfRoad; x <= centerX + halfRoad; x++)
                {
                    AddPlannedRoadCell(layout, new GridCoord(x, z), width, depth, roadType);
                }
            }
        }

        private static void AddPlannedRoadCell(
            Dictionary<GridCoord, RoadType> layout,
            GridCoord coord,
            int width,
            int depth,
            RoadType roadType)
        {
            if (coord.x < 0 || coord.x >= width || coord.z < 0 || coord.z >= depth)
            {
                return;
            }

            if (!layout.TryGetValue(coord, out RoadType existing)
                || GetRoadTypePriority(roadType) >= GetRoadTypePriority(existing))
            {
                layout[coord] = roadType;
            }
        }

        private void StampOffMapRoadExits(int centerX, int centerZ, int halfRoad, int mainStart, int mainEnd)
        {
            StampVerticalRoadBand(centerX, 0, mainStart - 1, halfRoad, RoadType.RegionalExit);
            StampVerticalRoadBand(centerX, mainEnd + 1, grid.Depth - 1, halfRoad, RoadType.RegionalExit);
            StampHorizontalRoadBand(0, grid.Width - 1, centerZ, halfRoad, RoadType.RegionalExit);
        }

        private void StampHorizontalRoadBand(int xStart, int xEnd, int centerZ, int halfRoad, RoadType roadType)
        {
            int minX = Mathf.Min(xStart, xEnd);
            int maxX = Mathf.Max(xStart, xEnd);
            for (int x = minX; x <= maxX; x++)
            {
                for (int z = centerZ - halfRoad; z <= centerZ + halfRoad; z++)
                {
                    MarkRoad(new GridCoord(x, z), roadType);
                }
            }
        }

        private void StampVerticalRoadBand(int centerX, int zStart, int zEnd, int halfRoad, RoadType roadType)
        {
            int minZ = Mathf.Min(zStart, zEnd);
            int maxZ = Mathf.Max(zStart, zEnd);
            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = centerX - halfRoad; x <= centerX + halfRoad; x++)
                {
                    MarkRoad(new GridCoord(x, z), roadType);
                }
            }
        }

        private void StampAgriculturalSpurRoads(int centerX, int centerZ, int halfRoad)
        {
            if (!ShouldGenerateAgriculturalParcels())
            {
                return;
            }

            Vector2Int cropSize = GetCropFarmParcelSize();
            Vector2Int ranchSize = GetRanchParcelSize();
            int cropRoadZ = GetNorthernAgricultureRoadZ(centerZ, halfRoad, cropSize.y);
            int ranchRoadZ = GetSouthernAgricultureRoadZ(centerZ, halfRoad, ranchSize.y);
            int length = GetAgricultureSpurRoadLength(cropSize.x, ranchSize.x);

            int westRoadEndX = centerX - halfRoad - 1;
            int westRoadStartX = Mathf.Max(0, westRoadEndX - length + 1);
            StampHorizontalRoadBand(westRoadStartX, westRoadEndX, cropRoadZ, halfRoad, RoadType.Spur);

            int eastRoadStartX = centerX + halfRoad + 1;
            int eastRoadEndX = Mathf.Min(grid.Width - 1, eastRoadStartX + length - 1);
            StampHorizontalRoadBand(eastRoadStartX, eastRoadEndX, ranchRoadZ, halfRoad, RoadType.Spur);
        }

        private void StampSawmillSpurRoad(int centerX, int centerZ, int halfRoad, int mainEnd)
        {
            if (!ShouldGenerateSawmillProperty())
            {
                return;
            }

            Vector2Int sawmillSize = GetSawmillParcelSize();
            int roadZ = GetNorthernSawmillRoadZ(centerZ, halfRoad, sawmillSize.y);
            if (roadZ > mainEnd)
            {
                StampVerticalRoadBand(centerX, mainEnd + 1, roadZ, halfRoad, RoadType.Spur);
            }

            int length = GetSawmillSpurRoadLength(sawmillSize.x);
            int westRoadEndX = centerX - halfRoad - 1;
            int westRoadStartX = Mathf.Max(0, westRoadEndX - length + 1);
            StampHorizontalRoadBand(westRoadStartX, westRoadEndX, roadZ, halfRoad, RoadType.Spur);
        }

        private void MarkRoad(GridCoord coord, RoadType roadType)
        {
            if (!grid.IsInBounds(coord))
            {
                return;
            }

            ref TownCell cell = ref grid.GetCellRef(coord);
            if (!IsFinite(cell.height)
                || cell.slopeDegrees >= 90f
                || worldSurfaceProvider != null
                && !worldSurfaceProvider.IsRoadCompatible(
                    grid.CoordToWorldCenter(coord, cell.height),
                    new RoadQuery { maximumSlopeDegrees = maximumRoadSlopeDegrees }))
            {
                return;
            }

            GridCoord[] neighbors = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };
            for (int i = 0; i < neighbors.Length; i++)
            {
                GridCoord neighbor = coord + neighbors[i];
                if (!grid.IsInBounds(neighbor))
                {
                    continue;
                }

                TownCell neighborCell = grid.GetCell(neighbor);
                if (neighborCell.IsRoad
                    && Mathf.Abs(neighborCell.height - cell.height) > maximumRoadConnectionHeightDifference)
                {
                    return;
                }
            }

            if (!cell.IsRoad)
            {
                generatedRoadCellCount++;
            }

            cell.occupancy |= CellOccupancy.Road | CellOccupancy.Blocked;
            if (GetRoadTypePriority(roadType) >= GetRoadTypePriority(cell.roadType))
            {
                cell.roadType = roadType;
            }
            cell.blocked = true;
        }

        private static int GetRoadTypePriority(RoadType roadType)
        {
            return roadType switch
            {
                RoadType.MainStreet => 5,
                RoadType.CrossStreet => 4,
                RoadType.Spur => 3,
                RoadType.RegionalExit => 2,
                _ => 0
            };
        }

        private void GeneratePlots()
        {
            int centerX = grid.Width / 2;
            int centerZ = grid.Depth / 2;
            int halfRoad = settings.roadWidthCells / 2;
            int mainLength = Mathf.Min(settings.mainStreetLengthCells, grid.Depth);
            int mainStart = Mathf.Max(0, centerZ - mainLength / 2);
            int mainEnd = Mathf.Min(grid.Depth - 1, mainStart + mainLength - 1);
            System.Random random = new(settings.seed);

            GenerateAgriculturalPlots(centerX, centerZ, halfRoad);
            GenerateSawmillPlot(centerX, centerZ, halfRoad);

            GeneratePlotsOnRoadSide(
                random,
                mainStart,
                mainEnd,
                centerX - halfRoad - 1,
                -1,
                GridDirection.East);

            GeneratePlotsOnRoadSide(
                random,
                mainStart,
                mainEnd,
                centerX + halfRoad + 1,
                1,
                GridDirection.West);

            GenerateCrossStreetPlots(random, centerX, centerZ, halfRoad);
            GenerateEdgeResidentialPlots(centerX, centerZ, halfRoad);
        }

        private void GenerateCrossStreetPlots(System.Random random, int centerX, int centerZ, int halfRoad)
        {
            if (settings.crossStreetCount <= 0)
            {
                return;
            }

            int halfLength = settings.crossStreetLengthCells / 2;
            int roadStartX = Mathf.Max(0, centerX - halfLength);
            int roadEndX = Mathf.Min(grid.Width - 1, centerX + halfLength);
            for (int i = 0; i < settings.crossStreetCount; i++)
            {
                float indexOffset = i - (settings.crossStreetCount - 1) * 0.5f;
                int roadZ = centerZ + Mathf.RoundToInt(indexOffset * settings.crossStreetSpacingCells);
                if (roadZ == centerZ)
                {
                    continue;
                }

                GeneratePlotsAlongHorizontalRoadSide(
                    random,
                    roadStartX,
                    roadEndX,
                    roadZ + halfRoad + 1,
                    roadZ + halfRoad,
                    1,
                    GridDirection.South);

                GeneratePlotsAlongHorizontalRoadSide(
                    random,
                    roadStartX,
                    roadEndX,
                    roadZ - halfRoad - 1,
                    roadZ - halfRoad,
                    -1,
                    GridDirection.North);
            }
        }

        private void GeneratePlotsAlongHorizontalRoadSide(
            System.Random random,
            int startX,
            int endX,
            int plotEdgeZ,
            int roadAccessZ,
            int sideSign,
            GridDirection frontageDirection)
        {
            int x = startX;
            while (x <= endX)
            {
                GridCoord preliminaryRoadAccess = new(x + settings.minPlotFrontageCells / 2, roadAccessZ);
                PlotZone zone = EstimatePlotZoneForRoadAccess(frontageDirection, preliminaryRoadAccess);
                int frontage = ChooseGeneratedPlotFrontageCells(random, zone, frontageDirection, preliminaryRoadAccess);
                int depth = ChooseGeneratedPlotDepthCells(zone, preliminaryRoadAccess);
                int zMin = sideSign > 0 ? plotEdgeZ : plotEdgeZ - depth + 1;
                GridRect bounds = new(x, zMin, frontage, depth);
                GridCoord roadAccess = new(x + frontage / 2, roadAccessZ);

                if (TryCreatePlot(bounds, frontageDirection, roadAccess, zone))
                {
                    x += frontage + settings.plotGapCells;
                }
                else
                {
                    x += Mathf.Max(1, settings.plotGapCells + 1);
                }
            }
        }

        private void GenerateEdgeResidentialPlots(int centerX, int centerZ, int halfRoad)
        {
            int target = settings != null ? Mathf.Max(0, settings.edgeHouseCount) : 0;
            if (target <= 0)
            {
                return;
            }

            int frontage = Mathf.Clamp(8, settings.minPlotFrontageCells, settings.maxPlotFrontageCells);
            int depth = settings.plotDepthCells;
            int northZ = centerZ + halfRoad + 1;
            int southZ = centerZ - halfRoad - depth;
            int westX = Mathf.Max(1, depth + settings.plotGapCells + 1);
            int eastX = Mathf.Min(grid.Width - frontage - 1, grid.Width - depth - frontage - settings.plotGapCells - 1);

            ExpansionPlotCandidate[] candidates =
            {
                new(
                    new GridRect(westX, northZ, frontage, depth),
                    GridDirection.South,
                    new GridCoord(westX + frontage / 2, centerZ + halfRoad),
                    PlotZone.Residential,
                    0),
                new(
                    new GridRect(eastX, southZ, frontage, depth),
                    GridDirection.North,
                    new GridCoord(eastX + frontage / 2, centerZ - halfRoad),
                    PlotZone.Residential,
                    1)
            };

            int created = 0;
            for (int i = 0; i < candidates.Length && created < target; i++)
            {
                ExpansionPlotCandidate candidate = candidates[i];
                if (TryCreatePlot(candidate.bounds, candidate.frontageDirection, candidate.roadAccess, candidate.zone, false))
                {
                    created++;
                }
            }
        }

        private void GenerateAgriculturalPlots(int centerX, int centerZ, int halfRoad)
        {
            if (!ShouldGenerateAgriculturalParcels())
            {
                return;
            }

            Vector2Int cropSize = GetCropFarmParcelSize();
            Vector2Int ranchSize = GetRanchParcelSize();
            int length = GetAgricultureSpurRoadLength(cropSize.x, ranchSize.x);

            int westRoadEndX = centerX - halfRoad - 1;
            int westRoadStartX = Mathf.Max(0, westRoadEndX - length + 1);
            int cropX = Mathf.Clamp(westRoadStartX, 0, Mathf.Max(0, grid.Width - cropSize.x));
            int cropRoadZ = GetNorthernAgricultureRoadZ(centerZ, halfRoad, cropSize.y);
            GridRect cropBounds = new(cropX, cropRoadZ + halfRoad + 1, cropSize.x, cropSize.y);
            GridCoord cropAccess = new(cropX + cropSize.x / 2, cropRoadZ + halfRoad);
            TryCreateAgriculturalPlot(cropBounds, GridDirection.South, cropAccess, AgriculturalSiteRole.CropProductionYard);

            int eastRoadStartX = centerX + halfRoad + 1;
            int eastRoadEndX = Mathf.Min(grid.Width - 1, eastRoadStartX + length - 1);
            int ranchX = Mathf.Clamp(eastRoadEndX - ranchSize.x + 1, 0, Mathf.Max(0, grid.Width - ranchSize.x));
            int ranchRoadZ = GetSouthernAgricultureRoadZ(centerZ, halfRoad, ranchSize.y);
            GridRect ranchBounds = new(ranchX, ranchRoadZ - halfRoad - ranchSize.y, ranchSize.x, ranchSize.y);
            GridCoord ranchAccess = new(ranchX + ranchSize.x / 2, ranchRoadZ - halfRoad);
            TryCreateAgriculturalPlot(ranchBounds, GridDirection.North, ranchAccess, AgriculturalSiteRole.LivestockYard);
        }

        private void GenerateSawmillPlot(int centerX, int centerZ, int halfRoad)
        {
            if (!ShouldGenerateSawmillProperty())
            {
                return;
            }

            Vector2Int sawmillSize = GetSawmillParcelSize();
            int length = GetSawmillSpurRoadLength(sawmillSize.x);
            int westRoadEndX = centerX - halfRoad - 1;
            int westRoadStartX = Mathf.Max(0, westRoadEndX - length + 1);
            int sawmillX = Mathf.Clamp(westRoadStartX, 0, Mathf.Max(0, grid.Width - sawmillSize.x));
            int sawmillRoadZ = GetNorthernSawmillRoadZ(centerZ, halfRoad, sawmillSize.y);
            GridRect sawmillBounds = new(sawmillX, sawmillRoadZ + halfRoad + 1, sawmillSize.x, sawmillSize.y);
            GridCoord sawmillAccess = new(sawmillX + sawmillSize.x / 2, sawmillRoadZ + halfRoad);
            TryCreateAgriculturalPlot(sawmillBounds, GridDirection.South, sawmillAccess, AgriculturalSiteRole.SawmillYard);
        }

        private void GeneratePlotsOnRoadSide(System.Random random, int startZ, int endZ, int roadEdgeX, int sideSign, GridDirection frontageDirection)
        {
            int z = startZ;
            while (z <= endZ)
            {
                GridCoord preliminaryRoadAccess = new(roadEdgeX, z + settings.minPlotFrontageCells / 2);
                PlotZone zone = EstimatePlotZoneForRoadAccess(frontageDirection, preliminaryRoadAccess);
                int frontage = ChooseGeneratedPlotFrontageCells(random, zone, frontageDirection, preliminaryRoadAccess);
                int depth = ChooseGeneratedPlotDepthCells(zone, preliminaryRoadAccess);
                int xMin = sideSign < 0 ? roadEdgeX - depth + 1 : roadEdgeX;
                GridRect bounds = new(xMin, z, depth, frontage);

                if (TryCreatePlot(bounds, frontageDirection, roadEdgeX, z + frontage / 2, zone))
                {
                    z += frontage + settings.plotGapCells;
                }
                else
                {
                    z += Mathf.Max(1, settings.plotGapCells + 1);
                }
            }
        }

        private bool TryCreatePlot(GridRect bounds, GridDirection frontageDirection, int roadEdgeX, int roadAccessZ, PlotZone? forcedZone = null)
        {
            GridCoord roadAccess = new GridCoord(
                frontageDirection == GridDirection.East ? roadEdgeX + 1 : roadEdgeX - 1,
                roadAccessZ);
            return TryCreatePlot(bounds, frontageDirection, roadAccess, forcedZone);
        }

        private bool TryCreatePlot(GridRect bounds, GridDirection frontageDirection, GridCoord roadAccess, PlotZone? forcedZone = null, bool reservedForLandSale = false)
        {
            if (!grid.ContainsRect(bounds) || !grid.RectCellsAreBuildable(bounds, false))
            {
                return false;
            }

            if (!grid.IsInBounds(roadAccess) || !grid.GetCell(roadAccess).IsRoad)
            {
                return false;
            }

            TownPlot plot = new()
            {
                id = plots.Count,
                zone = forcedZone ?? EstimatePlotZoneForRoadAccess(frontageDirection, roadAccess),
                bounds = bounds,
                candidateFootprint = bounds,
                siteSizeCells = new Vector2Int(bounds.width, bounds.depth),
                frontageCells = frontageDirection == GridDirection.East || frontageDirection == GridDirection.West ? bounds.depth : bounds.width,
                depthCells = frontageDirection == GridDirection.East || frontageDirection == GridDirection.West ? bounds.width : bounds.depth,
                roadFrontageDirection = frontageDirection,
                roadAccessCell = roadAccess,
                reservedForLandSale = reservedForLandSale
            };

            plots.Add(plot);

            foreach (GridCoord coord in TownGrid.EachCoord(bounds))
            {
                ref TownCell cell = ref grid.GetCellRef(coord);
                cell.occupancy |= CellOccupancy.Plot;
                cell.plotId = plot.id;
            }

            return true;
        }

        private bool TryCreateAgriculturalPlot(
            GridRect bounds,
            GridDirection frontageDirection,
            GridCoord roadAccess,
            AgriculturalSiteRole agriculturalSiteRole)
        {
            if (!grid.ContainsRect(bounds) || !grid.RectCellsAreBuildable(bounds, false))
            {
                return false;
            }

            if (!grid.IsInBounds(roadAccess) || !grid.GetCell(roadAccess).IsRoad)
            {
                return false;
            }

            TownPlot plot = new()
            {
                id = plots.Count,
                zone = PlotZone.Agricultural,
                bounds = bounds,
                candidateFootprint = bounds,
                siteSizeCells = new Vector2Int(bounds.width, bounds.depth),
                agriculturalSiteRole = agriculturalSiteRole,
                frontageCells = frontageDirection == GridDirection.East || frontageDirection == GridDirection.West ? bounds.depth : bounds.width,
                depthCells = frontageDirection == GridDirection.East || frontageDirection == GridDirection.West ? bounds.width : bounds.depth,
                roadFrontageDirection = frontageDirection,
                roadAccessCell = roadAccess
            };

            plots.Add(plot);

            foreach (GridCoord coord in TownGrid.EachCoord(bounds))
            {
                ref TownCell cell = ref grid.GetCellRef(coord);
                cell.occupancy |= CellOccupancy.Plot;
                cell.plotId = plot.id;
            }

            return true;
        }

        private PlotZone EstimatePlotZoneForRoadAccess(GridDirection frontageDirection, GridCoord roadAccess)
        {
            int centerX = grid.Width / 2;
            int centerZ = grid.Depth / 2;
            int halfRoad = settings.roadWidthCells / 2;
            int mainStreetReach = Mathf.Max(settings.crossStreetSpacingCells, Mathf.RoundToInt(settings.mainStreetLengthCells * 0.45f));
            int distanceAlongMain = Mathf.Abs(roadAccess.z - centerZ);
            bool frontsMainStreet = (frontageDirection == GridDirection.East || frontageDirection == GridDirection.West)
                && Mathf.Abs(roadAccess.x - centerX) <= halfRoad + 1;
            bool nearMainStreetCorner = (frontageDirection == GridDirection.North || frontageDirection == GridDirection.South)
                && Mathf.Abs(roadAccess.x - centerX) <= Mathf.Max(settings.maxPlotFrontageCells + halfRoad + 1, 12);
            bool nearCrossRoad = settings.crossStreetCount > 0
                && Mathf.Abs(roadAccess.z - centerZ) <= Mathf.Max(settings.crossStreetSpacingCells * settings.crossStreetCount / 2, settings.crossStreetSpacingCells);

            if ((frontsMainStreet && distanceAlongMain <= mainStreetReach) || (nearMainStreetCorner && nearCrossRoad))
            {
                return PlotZone.Business;
            }

            if (frontsMainStreet && distanceAlongMain <= Mathf.RoundToInt(settings.mainStreetLengthCells * 0.5f))
            {
                return PlotZone.MixedUse;
            }

            return PlotZone.Residential;
        }

        private int ChooseGeneratedPlotFrontageCells(System.Random random, PlotZone zone, GridDirection frontageDirection, GridCoord roadAccess)
        {
            int minimum = Mathf.Max(1, settings.minPlotFrontageCells);
            int maximum = Mathf.Max(minimum, settings.maxPlotFrontageCells);

            if (zone == PlotZone.Business)
            {
                int catalogMinimum = GetCatalogMinimumFrontageCells(PlotZone.Business);
                int tightMinimum = Mathf.Max(minimum, catalogMinimum);
                int tightMaximum = Mathf.Max(tightMinimum, Mathf.Min(maximum, tightMinimum + 1));
                return random.Next(tightMinimum, tightMaximum + 1);
            }

            if (zone == PlotZone.MixedUse)
            {
                int catalogMinimum = Mathf.Max(GetCatalogMinimumFrontageCells(PlotZone.Business), GetCatalogMinimumFrontageCells(PlotZone.Residential));
                int mixedMinimum = Mathf.Max(minimum, catalogMinimum);
                int mixedMaximum = Mathf.Max(mixedMinimum, Mathf.Min(maximum + 1, mixedMinimum + 2));
                return random.Next(mixedMinimum, mixedMaximum + 1);
            }

            int distance = GetDistanceFromTownCoreCells(roadAccess);
            int extra = distance >= settings.crossStreetSpacingCells * 2 ? 3 : distance >= settings.crossStreetSpacingCells ? 2 : 1;
            int residentialMinimum = Mathf.Max(minimum, GetCatalogMinimumFrontageCells(PlotZone.Residential) + extra);
            int residentialMaximum = Mathf.Max(residentialMinimum, maximum + extra);
            return random.Next(residentialMinimum, residentialMaximum + 1);
        }

        private int ChooseGeneratedPlotDepthCells(PlotZone zone, GridCoord roadAccess)
        {
            int baseDepth = Mathf.Max(1, settings.plotDepthCells);
            if (zone == PlotZone.Business)
            {
                return Mathf.Max(baseDepth, GetCatalogMinimumBuildingDepthCells(PlotZone.Business) + settings.buildingSetbackCells);
            }

            if (zone == PlotZone.MixedUse)
            {
                return Mathf.Max(baseDepth + 1, GetCatalogMinimumBuildingDepthCells(PlotZone.Business) + settings.buildingSetbackCells + 1);
            }

            int distance = GetDistanceFromTownCoreCells(roadAccess);
            int extraDepth = distance >= settings.crossStreetSpacingCells * 2 ? 6 : distance >= settings.crossStreetSpacingCells ? 4 : 2;
            return Mathf.Max(baseDepth + extraDepth, GetCatalogMinimumBuildingDepthCells(PlotZone.Residential) + settings.buildingSetbackCells + extraDepth);
        }

        private int GetDistanceFromTownCoreCells(GridCoord coord)
        {
            int centerX = grid.Width / 2;
            int centerZ = grid.Depth / 2;
            return Mathf.Abs(coord.x - centerX) + Mathf.Abs(coord.z - centerZ);
        }

        private int GetCatalogMinimumFrontageCells(PlotZone zone)
        {
            IReadOnlyList<BuildingDefinition> catalog = GetBuildingCatalog();
            int best = 0;
            for (int i = 0; i < catalog.Count; i++)
            {
                BuildingDefinition definition = catalog[i];
                if (!DefinitionContributesToGeneratedLotSizing(definition, zone))
                {
                    continue;
                }

                int frontage = ResolvePlacementFootprintSizeCells(definition).x;
                best = best == 0 ? frontage : Mathf.Min(best, frontage);
            }

            return Mathf.Max(0, best);
        }

        private int GetCatalogMinimumBuildingDepthCells(PlotZone zone)
        {
            IReadOnlyList<BuildingDefinition> catalog = GetBuildingCatalog();
            int best = 0;
            for (int i = 0; i < catalog.Count; i++)
            {
                BuildingDefinition definition = catalog[i];
                if (!DefinitionContributesToGeneratedLotSizing(definition, zone))
                {
                    continue;
                }

                int depth = ResolvePlacementFootprintSizeCells(definition).y;
                best = best == 0 ? depth : Mathf.Min(best, depth);
            }

            return Mathf.Max(0, best);
        }

        private static bool DefinitionContributesToGeneratedLotSizing(BuildingDefinition definition, PlotZone zone)
        {
            if (definition == null || definition.PrimaryUse == BuildingUseType.Civic || !definition.CanUsePlot(zone))
            {
                return false;
            }

            return zone == PlotZone.Residential
                ? definition.CanHostHouseholds && !definition.CanHostWorkplace
                : definition.CanHostWorkplace;
        }

        private List<ExpansionPlotCandidate> BuildExpansionPlotCandidates(int purchasedPlotId)
        {
            List<ExpansionPlotCandidate> candidates = new();
            if (grid == null || settings == null)
            {
                return candidates;
            }

            int centerX = grid.Width / 2;
            int centerZ = grid.Depth / 2;
            int halfRoad = settings.roadWidthCells / 2;
            int mainLength = Mathf.Min(settings.mainStreetLengthCells, grid.Depth);
            int mainStart = Mathf.Max(0, centerZ - mainLength / 2);
            int mainEnd = Mathf.Min(grid.Depth - 1, mainStart + mainLength - 1);
            int frontage = Mathf.Clamp(8, settings.minPlotFrontageCells, settings.maxPlotFrontageCells);
            int depth = settings.plotDepthCells;
            int spacing = Mathf.Max(1, frontage + settings.plotGapCells);

            AddVerticalExpansionCandidates(candidates, centerX, 1, mainStart - 1, halfRoad, frontage, depth, purchasedPlotId);
            AddVerticalExpansionCandidates(candidates, centerX, mainEnd + 1, grid.Depth - frontage - 1, halfRoad, frontage, depth, purchasedPlotId);
            AddHorizontalExpansionCandidates(candidates, 1, centerX - halfRoad - depth - 1, centerZ, halfRoad, frontage, depth, purchasedPlotId);
            AddHorizontalExpansionCandidates(candidates, centerX + halfRoad + depth + 1, grid.Width - frontage - 1, centerZ, halfRoad, frontage, depth, purchasedPlotId);

            candidates.Sort((left, right) =>
            {
                int scoreCompare = left.score.CompareTo(right.score);
                if (scoreCompare != 0)
                {
                    return scoreCompare;
                }

                return left.bounds.xMin != right.bounds.xMin
                    ? left.bounds.xMin.CompareTo(right.bounds.xMin)
                    : left.bounds.zMin.CompareTo(right.bounds.zMin);
            });
            return candidates;

            void AddVerticalExpansionCandidates(
                List<ExpansionPlotCandidate> target,
                int roadCenterX,
                int zStart,
                int zEnd,
                int roadHalfWidth,
                int plotFrontage,
                int plotDepth,
                int purchasedId)
            {
                if (zStart > zEnd)
                {
                    return;
                }

                for (int z = zStart; z <= zEnd; z += spacing)
                {
                    int accessZ = Mathf.Min(zEnd, z + plotFrontage / 2);
                    AddCandidate(
                        target,
                        new GridRect(roadCenterX - roadHalfWidth - plotDepth, z, plotDepth, plotFrontage),
                        GridDirection.East,
                        new GridCoord(roadCenterX - roadHalfWidth, accessZ),
                        purchasedId);
                    AddCandidate(
                        target,
                        new GridRect(roadCenterX + roadHalfWidth + 1, z, plotDepth, plotFrontage),
                        GridDirection.West,
                        new GridCoord(roadCenterX + roadHalfWidth, accessZ),
                        purchasedId);
                }
            }

            void AddHorizontalExpansionCandidates(
                List<ExpansionPlotCandidate> target,
                int xStart,
                int xEnd,
                int roadCenterZ,
                int roadHalfWidth,
                int plotFrontage,
                int plotDepth,
                int purchasedId)
            {
                if (xStart > xEnd)
                {
                    return;
                }

                for (int x = xStart; x <= xEnd; x += spacing)
                {
                    int accessX = Mathf.Min(xEnd, x + plotFrontage / 2);
                    AddCandidate(
                        target,
                        new GridRect(x, roadCenterZ + roadHalfWidth + 1, plotFrontage, plotDepth),
                        GridDirection.South,
                        new GridCoord(accessX, roadCenterZ + roadHalfWidth),
                        purchasedId);
                    AddCandidate(
                        target,
                        new GridRect(x, roadCenterZ - roadHalfWidth - plotDepth, plotFrontage, plotDepth),
                        GridDirection.North,
                        new GridCoord(accessX, roadCenterZ - roadHalfWidth),
                        purchasedId);
                }
            }

            void AddCandidate(
                List<ExpansionPlotCandidate> target,
                GridRect bounds,
                GridDirection frontageDirection,
                GridCoord roadAccess,
                int purchasedId)
            {
                if (!grid.ContainsRect(bounds)
                    || !grid.IsInBounds(roadAccess)
                    || !grid.GetCell(roadAccess).IsRoad
                    || grid.GetCell(roadAccess).roadType != RoadType.RegionalExit
                    || !grid.RectCellsAreBuildable(bounds, false))
                {
                    return;
                }

                PlotZone zone = EstimateExpansionPlotZone(bounds);
                int edgeDistance = Mathf.Min(
                    Mathf.Min(bounds.xMin, grid.Width - 1 - bounds.xMaxInclusive),
                    Mathf.Min(bounds.zMin, grid.Depth - 1 - bounds.zMaxInclusive));
                int hash = PositiveHash(settings.seed * 97 + purchasedId * 53 + bounds.xMin * 7 + bounds.zMin * 11);
                int score = edgeDistance * 1000 + hash % 997;
                target.Add(new ExpansionPlotCandidate(bounds, frontageDirection, roadAccess, zone, score));
            }
        }

        private PlotZone EstimateExpansionPlotZone(GridRect bounds)
        {
            int centerX = grid.Width / 2;
            int centerZ = grid.Depth / 2;
            int distanceFromCenter = Mathf.Abs(bounds.Center.x - centerX) + Mathf.Abs(bounds.Center.z - centerZ);
            int residentialThreshold = Mathf.Max(settings.crossStreetSpacingCells, settings.mainStreetLengthCells / 3);
            return distanceFromCenter >= residentialThreshold ? PlotZone.Residential : PlotZone.MixedUse;
        }

        private static int PositiveHash(int value)
        {
            unchecked
            {
                uint hash = (uint)value;
                hash ^= hash >> 16;
                hash *= 0x7feb352d;
                hash ^= hash >> 15;
                hash *= 0x846ca68b;
                hash ^= hash >> 16;
                return (int)(hash & 0x7fffffff);
            }
        }

        private bool ShouldGenerateAgriculturalParcels()
        {
            return settings != null && settings.generateAgriculturalParcels && grid != null && grid.Width >= 12 && grid.Depth >= 24;
        }

        private bool ShouldGenerateSawmillProperty()
        {
            return settings != null
                && settings.generateSawmillProperty
                && grid != null
                && grid.Width >= 48
                && grid.Depth >= 56;
        }

        private Vector2Int GetCropFarmParcelSize()
        {
            Vector2Int size = settings != null ? settings.cropFarmParcelSizeCells : new Vector2Int(16, 18);
            return new Vector2Int(
                Mathf.Clamp(Mathf.Max(1, size.x), 1, Mathf.Max(1, grid.Width / 3)),
                Mathf.Clamp(Mathf.Max(1, size.y), 1, Mathf.Max(1, grid.Depth / 3)));
        }

        private Vector2Int GetRanchParcelSize()
        {
            Vector2Int cropSize = GetCropFarmParcelSize();
            Vector2Int size = settings != null ? settings.ranchParcelSizeCells : new Vector2Int(20, 24);
            return new Vector2Int(
                Mathf.Clamp(Mathf.Max(cropSize.x, size.x), 1, Mathf.Max(1, grid.Width / 3)),
                Mathf.Clamp(Mathf.Max(cropSize.y, size.y), 1, Mathf.Max(1, grid.Depth / 3)));
        }

        private Vector2Int GetSawmillParcelSize()
        {
            Vector2Int ranchSize = GetRanchParcelSize();
            Vector2Int size = settings != null ? settings.sawmillParcelSizeCells : new Vector2Int(40, 34);
            return new Vector2Int(
                Mathf.Clamp(Mathf.Max(ranchSize.x, size.x), 1, Mathf.Max(1, grid.Width / 3)),
                Mathf.Clamp(Mathf.Max(ranchSize.y, size.y), 1, Mathf.Max(1, grid.Depth / 3)));
        }

        private int GetAgricultureSpurRoadLength(int cropWidth, int ranchWidth)
        {
            int requested = settings != null ? settings.agricultureSpurRoadLengthCells : 28;
            int minimum = Mathf.Max(cropWidth, ranchWidth) + 4;
            int maximum = Mathf.Max(minimum, grid.Width / 2 - settings.roadWidthCells - 2);
            return Mathf.Clamp(Mathf.Max(requested, minimum), minimum, maximum);
        }

        private int GetSawmillSpurRoadLength(int sawmillWidth)
        {
            int requested = settings != null ? settings.sawmillSpurRoadLengthCells : 68;
            int minimum = Mathf.Max(1, sawmillWidth) + 6;
            int maximum = Mathf.Max(minimum, grid.Width / 2 - settings.roadWidthCells - 2);
            return Mathf.Clamp(Mathf.Max(requested, minimum), minimum, maximum);
        }

        private int GetNorthernAgricultureRoadZ(int centerZ, int halfRoad, int parcelDepth)
        {
            int requested = centerZ + (settings != null ? settings.agricultureDistanceFromCenterCells : 42);
            int min = halfRoad + 1;
            int max = Mathf.Max(min, grid.Depth - halfRoad - parcelDepth - 2);
            return Mathf.Clamp(requested, min, max);
        }

        private int GetNorthernSawmillRoadZ(int centerZ, int halfRoad, int parcelDepth)
        {
            int requested = centerZ + (settings != null ? settings.sawmillDistanceFromCenterCells : 62);
            int min = halfRoad + 1;
            int max = Mathf.Max(min, grid.Depth - halfRoad - parcelDepth - 2);
            return Mathf.Clamp(requested, min, max);
        }

        private int GetSouthernAgricultureRoadZ(int centerZ, int halfRoad, int parcelDepth)
        {
            int requested = centerZ - (settings != null ? settings.agricultureDistanceFromCenterCells : 42);
            int min = halfRoad + parcelDepth + 1;
            int max = Mathf.Max(min, grid.Depth - halfRoad - 2);
            return Mathf.Clamp(requested, min, max);
        }

        private void PlaceBuildings()
        {
            IReadOnlyList<BuildingDefinition> catalog = GetBuildingCatalog();
            PlaceTownHall();

            HashSet<int> reservedLandPlotIds = DetermineLandMarketReservations();
            for (int i = 0; i < plots.Count; i++)
            {
                TownPlot plot = plots[i];
                if (plot == null || plot.buildingId >= 0)
                {
                    continue;
                }

                plot.reservedForLandSale = reservedLandPlotIds.Contains(plot.id);
                if (plot.reservedForLandSale)
                {
                    continue;
                }

                BuildingDefinition definition = PickDefinitionForPlot(catalog, plot, i);
                if (definition == null)
                {
                    continue;
                }

                if (!TryBuildFootprint(plot, definition, out GridRect footprint))
                {
                    continue;
                }

                PlacedBuilding building = new()
                {
                    id = AllocateNextBuildingId(),
                    plotId = plot.id,
                    definition = definition,
                    footprint = footprint,
                    siteSizeCells = plot.siteSizeCells,
                    intendedFootprintSizeCells = ResolvePlacementFootprintSizeCells(definition),
                    frontageDirection = plot.roadFrontageDirection
                };

                StampFootprintAuthorityDiagnostics(building, true);
                ClaimBuildingFootprint(building);
                CreateAnchors(building);
                buildings.Add(building);
                plot.buildingId = building.id;
                plot.intendedBuildingFootprintCells = ResolvePlacementFootprintSizeCells(definition);
            }
        }

        private void PlaceTownHall()
        {
            if (settings == null || !settings.generateTownHall || TownHallAlreadyPlaced())
            {
                return;
            }

            TownHallDefinition townHallDefinition = ResolveTownHallDefinition();
            BuildingDefinition definition = townHallDefinition != null
                ? townHallDefinition.PhysicalBuildingDefinition
                : null;
            TownPlot plot = FindTownHallPlot(definition);
            if (definition == null || plot == null || !TryBuildFootprint(plot, definition, out GridRect footprint))
            {
                return;
            }

            PlacedBuilding building = new()
            {
                id = AllocateNextBuildingId(),
                plotId = plot.id,
                definition = definition,
                footprint = footprint,
                siteSizeCells = plot.siteSizeCells,
                intendedFootprintSizeCells = ResolvePlacementFootprintSizeCells(definition),
                frontageDirection = plot.roadFrontageDirection,
                publicSiteRole = PublicSiteRole.TownHall,
                playerOwned = false
            };

            StampFootprintAuthorityDiagnostics(building, true);
            ClaimBuildingFootprint(building);
            CreateAnchors(building);
            buildings.Add(building);
            plot.buildingId = building.id;
            plot.intendedBuildingFootprintCells = ResolvePlacementFootprintSizeCells(definition);
            plot.publicSiteRole = PublicSiteRole.TownHall;
            plot.reservedForLandSale = false;
        }

        private bool TownHallAlreadyPlaced()
        {
            return HasPublicSiteRole(PublicSiteRole.TownHall);
        }

        private bool HasPublicSiteRole(PublicSiteRole role)
        {
            if (role == PublicSiteRole.None)
            {
                return false;
            }

            for (int i = 0; i < buildings.Count; i++)
            {
                PlacedBuilding building = buildings[i];
                if (building != null && building.publicSiteRole == role)
                {
                    return true;
                }
            }

            return false;
        }

        private TownHallDefinition ResolveTownHallDefinition()
        {
            if (settings != null && settings.townHallDefinition != null)
            {
                return settings.townHallDefinition;
            }

            fallbackTownHallDefinition ??= CreateFallbackTownHallDefinition();
            return fallbackTownHallDefinition;
        }

        private TownPlot FindTownHallPlot(BuildingDefinition definition)
        {
            if (definition == null || grid == null)
            {
                return null;
            }

            TownPlot best = null;
            float bestScore = float.MaxValue;
            int centerX = grid.Width / 2;
            int centerZ = grid.Depth / 2;

            for (int i = 0; i < plots.Count; i++)
            {
                TownPlot plot = plots[i];
                if (plot == null
                    || plot.zone == PlotZone.Agricultural
                    || plot.buildingId >= 0
                    || plot.publicSiteRole != PublicSiteRole.None
                    || !definition.CanUsePlot(plot.zone)
                    || !TryBuildFootprint(plot, definition, out _))
                {
                    continue;
                }

                float centerDistance = Mathf.Abs(plot.bounds.Center.x - centerX) + Mathf.Abs(plot.bounds.Center.z - centerZ);
                float zonePenalty = plot.zone == PlotZone.Business ? 0f : 12f;
                float score = centerDistance + zonePenalty + i * 0.001f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = plot;
                }
            }

            return best;
        }

        private HashSet<int> DetermineLandMarketReservations()
        {
            HashSet<int> reserved = new();

            int target = settings != null ? Mathf.Min(settings.vacantLandPlotsToReserve, plots.Count) : 0;
            if (target <= 0 || plots.Count == 0)
            {
                return reserved;
            }

            List<LandOfferCandidate> candidates = new();
            for (int i = 0; i < plots.Count; i++)
            {
                TownPlot plot = plots[i];
                if (!IsPlotEligibleForLandMarket(plot))
                {
                    continue;
                }

                LandOfferCandidate candidate = EvaluateLandOfferCandidate(plot);
                if (candidate.totalFits <= 0)
                {
                    continue;
                }

                candidates.Add(candidate);
            }

            if (candidates.Count == 0)
            {
                return reserved;
            }

            candidates.Sort(CompareLandOfferCandidates);

            if (target > 0)
            {
                TrySelectLandOfferCandidate(
                    candidates,
                    reserved,
                    candidate => candidate.frontagePriority && candidate.strength >= LandOfferStrength.Strong,
                    respectSpacing: true);
            }

            if (target > 1)
            {
                TrySelectLandOfferCandidate(
                    candidates,
                    reserved,
                    candidate => candidate.domesticPriority && candidate.strength >= LandOfferStrength.Practical,
                    respectSpacing: true);
            }

            for (int pass = 0; pass < 2 && reserved.Count < target; pass++)
            {
                bool respectSpacing = pass == 0;
                for (int i = 0; i < candidates.Count && reserved.Count < target; i++)
                {
                    LandOfferCandidate candidate = candidates[i];
                    if (reserved.Contains(candidate.plot.id))
                    {
                        continue;
                    }

                    if (respectSpacing && !HasHealthyLandOfferSpacing(candidate.plot, reserved))
                    {
                        continue;
                    }

                    reserved.Add(candidate.plot.id);
                }
            }

            return reserved;
        }

        private bool TrySelectLandOfferCandidate(
            List<LandOfferCandidate> candidates,
            HashSet<int> reserved,
            Predicate<LandOfferCandidate> predicate,
            bool respectSpacing)
        {
            if (candidates == null || reserved == null || predicate == null)
            {
                return false;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                LandOfferCandidate candidate = candidates[i];
                if (reserved.Contains(candidate.plot.id) || !predicate(candidate))
                {
                    continue;
                }

                if (respectSpacing && !HasHealthyLandOfferSpacing(candidate.plot, reserved))
                {
                    continue;
                }

                reserved.Add(candidate.plot.id);
                return true;
            }

            return false;
        }

        private static int CompareLandOfferCandidates(LandOfferCandidate a, LandOfferCandidate b)
        {
            int strengthCompare = b.strength.CompareTo(a.strength);
            if (strengthCompare != 0)
            {
                return strengthCompare;
            }

            int scoreCompare = b.score.CompareTo(a.score);
            if (scoreCompare != 0)
            {
                return scoreCompare;
            }

            return a.plot.id.CompareTo(b.plot.id);
        }

        private bool HasHealthyLandOfferSpacing(TownPlot plot, HashSet<int> reserved)
        {
            if (plot == null || reserved == null || reserved.Count == 0)
            {
                return true;
            }

            foreach (int plotId in reserved)
            {
                if (!TryGetPlot(plotId, out TownPlot other) || other == null)
                {
                    continue;
                }

                int manhattan = Mathf.Abs(plot.roadAccessCell.x - other.roadAccessCell.x)
                    + Mathf.Abs(plot.roadAccessCell.z - other.roadAccessCell.z);
                if (manhattan < 12)
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsPlotEligibleForLandMarket(TownPlot plot)
        {
            if (plot == null || plot.buildingId >= 0 || plot.playerOwned || plot.zone == PlotZone.Agricultural)
            {
                return false;
            }

            if (IsEdgeResidentialHousePlot(plot))
            {
                return false;
            }

            return true;
        }

        private IReadOnlyList<BuildingDefinition> GetBuildingCatalog()
        {
            if (settings.buildingCatalog != null && settings.buildingCatalog.Length > 0)
            {
                return settings.buildingCatalog;
            }

            if (fallbackBuildingDefinitions.Count == 0)
            {
                fallbackBuildingDefinitions.Add(CreateFallbackBuilding("single_story_business_natural_wood", "Single Story Business Natural Wood", PlotZone.Business, new Vector2Int(5, 5), new Color(0.65f, 0.43f, 0.24f, 1f)));
                fallbackBuildingDefinitions.Add(CreateFallbackBuilding("house_natural_wood", "House Natural Wood", PlotZone.Residential, new Vector2Int(4, 4), new Color(0.58f, 0.48f, 0.35f, 1f)));
                fallbackBuildingDefinitions.Add(CreateFallbackBuilding("single_story_business_green_wood", "Single Story Business Green Wood", PlotZone.Business, new Vector2Int(4, 5), new Color(0.56f, 0.38f, 0.23f, 1f)));
                fallbackBuildingDefinitions.Add(CreateFallbackBuilding("single_story_business_white_wood", "Single Story Business White Wood", PlotZone.Business, new Vector2Int(4, 5), new Color(0.68f, 0.61f, 0.49f, 1f)));
                fallbackBuildingDefinitions.Add(CreateFallbackBuilding("single_story_business_white_plaster", "Single Story Business White Plaster", PlotZone.Business, new Vector2Int(4, 5), new Color(0.73f, 0.68f, 0.58f, 1f)));
                fallbackBuildingDefinitions.Add(CreateFallbackBuilding("single_story_business_grey_plaster", "Single Story Business Grey Plaster", PlotZone.Business, new Vector2Int(4, 5), new Color(0.52f, 0.52f, 0.48f, 1f)));
                fallbackBuildingDefinitions.Add(CreateFallbackBuilding("double_story_saloon_corner_natural_wood", "Double Story Saloon Corner Natural Wood", PlotZone.Business, new Vector2Int(6, 6), new Color(0.55f, 0.29f, 0.22f, 1f)));
                fallbackBuildingDefinitions.Add(CreateFallbackBuilding("double_story_business_green_wood", "Double Story Business Green Wood", PlotZone.Business, new Vector2Int(5, 6), new Color(0.35f, 0.48f, 0.32f, 1f)));
                fallbackBuildingDefinitions.Add(CreateFallbackBuilding("double_story_business_natural_wood", "Double Story Business Natural Wood", PlotZone.Business, new Vector2Int(5, 6), new Color(0.54f, 0.39f, 0.23f, 1f)));
                fallbackBuildingDefinitions.Add(CreateFallbackBuilding("double_story_business_red_wood", "Double Story Business Red Wood", PlotZone.Business, new Vector2Int(5, 6), new Color(0.58f, 0.24f, 0.18f, 1f)));
            }

            return fallbackBuildingDefinitions;
        }

        private static BuildingDefinition CreateFallbackBuilding(string id, string displayName, PlotZone zone, Vector2Int footprint, Color color)
        {
            BuildingDefinition definition = ScriptableObject.CreateInstance<BuildingDefinition>();
            definition.name = displayName;
            definition.ConfigureRuntimeFallback(id, displayName, zone, footprint, color, zone == PlotZone.Residential ? 4.5f : 6f);
            return definition;
        }

        private TownHallDefinition CreateFallbackTownHallDefinition()
        {
            fallbackTownHallPhysicalDefinition ??= CreateFallbackTownHallPhysicalDefinition();
            TownHallDefinition definition = ScriptableObject.CreateInstance<TownHallDefinition>();
            definition.name = "Town Hall";
            definition.ConfigureRuntimeFallback(fallbackTownHallPhysicalDefinition);
            return definition;
        }

        private static BuildingDefinition CreateFallbackTownHallPhysicalDefinition()
        {
            BuildingDefinition definition = ScriptableObject.CreateInstance<BuildingDefinition>();
            definition.name = "Single Story Civic Natural Wood";
            definition.ConfigureRuntimeCivicFallback(
                TownHallPhysicalBuildingId,
                "Single Story Civic Natural Wood",
                PlotZone.Business,
                new Vector2Int(6, 5),
                new Color(0.72f, 0.66f, 0.54f, 1f),
                7f);
            return definition;
        }

        private BuildingDefinition PickDefinitionForPlot(IReadOnlyList<BuildingDefinition> catalog, TownPlot plot, int plotIndex)
        {
            if (plot != null && plot.zone == PlotZone.Agricultural)
            {
                return PickAgriculturalDefinitionForPlot(catalog, plot, plotIndex);
            }

            if (IsEdgeResidentialHousePlot(plot))
            {
                BuildingDefinition residential = PickResidentialDefinitionForPlot(catalog, plot);
                if (residential != null)
                {
                    return residential;
                }
            }

            BuildingDefinition best = null;
            int bestScore = int.MinValue;
            for (int i = 0; i < catalog.Count; i++)
            {
                BuildingDefinition definition = catalog[(plotIndex + i) % catalog.Count];
                if (definition == null || definition.PrimaryUse == BuildingUseType.Civic)
                {
                    continue;
                }

                int score = ScoreDefinitionForGeneratedPlot(definition, plot);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = definition;
                }
            }

            return best;
        }

        private int ScoreDefinitionForGeneratedPlot(BuildingDefinition definition, TownPlot plot)
        {
            if (definition == null || plot == null || !definition.CanUsePlot(plot.zone) || !DefinitionFitsPlot(plot, definition))
            {
                return int.MinValue;
            }

            GetPlacementFrontageDepthSpan(definition, out int frontageSpan, out int depthSpan);
            int footprintArea = Mathf.Max(1, frontageSpan * depthSpan);
            int plotArea = Mathf.Max(1, plot.frontageCells * plot.depthCells);
            int openArea = Mathf.Max(0, plotArea - footprintArea);

            int score = 1000;
            switch (plot.zone)
            {
                case PlotZone.Business:
                    score += definition.CanHostWorkplace ? 600 : -500;
                    score += definition.IsMixedUse || definition.UsesUpperFloorResidential ? 120 : 0;
                    score += definition.ToleratesTightPad ? 90 : 0;
                    score -= Mathf.Clamp(openArea, 0, 200);
                    break;
                case PlotZone.MixedUse:
                    score += definition.IsMixedUse || definition.UsesUpperFloorResidential ? 420 : 0;
                    score += definition.CanHostWorkplace ? 180 : 0;
                    score += definition.CanHostHouseholds ? 140 : 0;
                    score -= Mathf.Clamp(openArea / 2, 0, 120);
                    break;
                case PlotZone.Residential:
                    score += definition.CanHostHouseholds ? 520 : -360;
                    score += definition.CanHostWorkplace ? -180 : 120;
                    score += definition.PrefersDeepYard ? 90 : 0;
                    score += Mathf.Clamp(openArea, 0, 180);
                    break;
            }

            score += Mathf.Clamp(plot.frontageCells - frontageSpan, -20, 20);
            score += Mathf.Clamp(plot.depthCells - depthSpan, -20, 30);
            return score;
        }

        private BuildingDefinition PickResidentialDefinitionForPlot(IReadOnlyList<BuildingDefinition> catalog, TownPlot plot)
        {
            if (catalog == null || plot == null)
            {
                return null;
            }

            for (int i = 0; i < catalog.Count; i++)
            {
                BuildingDefinition definition = catalog[i];
                if (definition != null
                    && definition.PrimaryUse == BuildingUseType.Residential
                    && definition.CanHostHouseholds
                    && definition.CanUsePlot(plot.zone)
                    && DefinitionFitsPlot(plot, definition))
                {
                    return definition;
                }
            }

            return null;
        }

        private bool IsEdgeResidentialHousePlot(TownPlot plot)
        {
            if (plot == null || plot.zone != PlotZone.Residential || grid == null || settings == null)
            {
                return false;
            }

            int edgeBand = Mathf.Max(settings.plotDepthCells + settings.maxPlotFrontageCells + 4, grid.Width / 5);
            bool nearEastWestEdge = plot.bounds.xMin <= edgeBand || plot.bounds.xMaxInclusive >= grid.Width - edgeBand - 1;
            bool centralRoadBand = Mathf.Abs(plot.roadAccessCell.z - grid.Depth / 2) <= settings.roadWidthCells;
            return nearEastWestEdge && centralRoadBand;
        }

        private BuildingDefinition PickAgriculturalDefinitionForPlot(IReadOnlyList<BuildingDefinition> catalog, TownPlot plot, int plotIndex)
        {
            if (catalog == null || catalog.Count == 0 || plot == null)
            {
                return null;
            }

            BuildingDefinition best = null;
            int bestScore = int.MinValue;

            for (int i = 0; i < catalog.Count; i++)
            {
                BuildingDefinition definition = catalog[(plotIndex + i) % catalog.Count];
                int score = ScoreAgriculturalDefinitionForPlot(definition, plot);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = definition;
                }
            }

            return best;
        }

        private int ScoreAgriculturalDefinitionForPlot(BuildingDefinition definition, TownPlot plot)
        {
            if (definition == null
                || plot == null
                || definition.PrimaryUse == BuildingUseType.Civic
                || !definition.CanHostWorkplace
                || !DefinitionFitsPlot(plot, definition))
            {
                return int.MinValue;
            }

            AgriculturalSiteRole plotRole = plot.agriculturalSiteRole;
            if (plotRole == AgriculturalSiteRole.None)
            {
                return definition.AgriculturalSiteRole == AgriculturalSiteRole.None ? 1000 : 1200;
            }

            int score = 0;
            if (definition.MatchesAgriculturalSiteRole(plotRole))
            {
                score += 5000;
            }
            else if (definition.HasDedicatedAgriculturalRole)
            {
                return int.MinValue;
            }
            else if (definition.IsFallbackAgriculturalFit(plotRole))
            {
                score += 3200;
            }
            else
            {
                score += 900;
            }

            if (definition.AllowedPlotZone == PlotZone.Agricultural)
            {
                score += 250;
            }
            else if (definition.AllowedPlotZone == PlotZone.MixedUse)
            {
                score += 100;
            }

            if (definition.CanHostHouseholds)
            {
                score -= 120;
            }

            if (plotRole == AgriculturalSiteRole.SawmillYard)
            {
                if (definition.IsSuitableForBusiness(BusinessType.Sawmill))
                {
                    score += 180;
                }
                else if (definition.IsSuitableForBusiness(BusinessType.LumberYard))
                {
                    score += 60;
                }
            }

            Vector2Int placementSize = ResolvePlacementFootprintSizeCells(definition);
            score += Mathf.Clamp(24 - placementSize.x - placementSize.y, -60, 60);
            return score;
        }

        private PlacementFootprintResolution ResolvePlacementFootprint(BuildingDefinition definition)
        {
            Vector2Int definitionSize = SanitizeFootprintSize(definition != null ? definition.FootprintSizeCells : Vector2Int.one);
            BuildingFootprintAuthority authority = ResolveFootprintAuthority(definition);
            if (authority == null)
            {
                return new PlacementFootprintResolution(
                    definitionSize,
                    Vector2Int.zero,
                    definitionSize,
                    false,
                    false,
                    "BuildingDefinition",
                    $"Placement footprint comes from definition {definitionSize.x}x{definitionSize.y}; no prefab footprint authority is assigned.");
            }

            Vector2Int resolved = authority.ReconcileWithDefinitionFootprint(
                definitionSize,
                out Vector2Int authorityMinimum,
                out bool expandedByAuthority,
                out string note);

            return new PlacementFootprintResolution(
                definitionSize,
                authorityMinimum,
                resolved,
                true,
                expandedByAuthority,
                "BuildingDefinition + BuildingFootprintAuthority",
                note);
        }

        private Vector2Int ResolvePlacementFootprintSizeCells(BuildingDefinition definition)
        {
            return ResolvePlacementFootprint(definition).resolvedSizeCells;
        }

        private BuildingFootprintAuthority ResolveFootprintAuthority(BuildingDefinition definition)
        {
            GameObject prefab = definition != null ? definition.VisualPrefab : null;
            if (prefab == null)
            {
                return null;
            }

            BuildingFootprintAuthority authority = prefab.GetComponent<BuildingFootprintAuthority>();
            if (authority != null)
            {
                return authority;
            }

            return prefab.GetComponentInChildren<BuildingFootprintAuthority>(true);
        }

        private void GetPlacementFrontageDepthSpan(BuildingDefinition definition, out int frontageSpan, out int depthSpan)
        {
            Vector2Int size = ResolvePlacementFootprintSizeCells(definition);
            frontageSpan = Mathf.Max(1, size.x);
            depthSpan = Mathf.Max(1, size.y);
        }

        private Vector2Int GetPlacementWorldFootprintSize(BuildingDefinition definition, GridDirection frontageDirection)
        {
            GetPlacementFrontageDepthSpan(definition, out int frontageSpan, out int depthSpan);
            return frontageDirection == GridDirection.East || frontageDirection == GridDirection.West
                ? new Vector2Int(depthSpan, frontageSpan)
                : new Vector2Int(frontageSpan, depthSpan);
        }

        private static Vector2Int SanitizeFootprintSize(Vector2Int size)
        {
            return new Vector2Int(Mathf.Max(1, size.x), Mathf.Max(1, size.y));
        }

        private void StampFootprintAuthorityDiagnostics(PlacedBuilding building, bool updateIntendedFootprint)
        {
            if (building == null || building.definition == null)
            {
                return;
            }

            PlacementFootprintResolution resolution = ResolvePlacementFootprint(building.definition);
            building.definitionFootprintSizeCells = resolution.definitionSizeCells;
            building.footprintAuthorityMinimumSizeCells = resolution.authorityMinimumSizeCells;
            building.resolvedPlacementFootprintSizeCells = resolution.resolvedSizeCells;
            building.hasPrefabFootprintAuthority = resolution.hasPrefabAuthority;
            building.footprintExpandedByAuthority = resolution.expandedByAuthority;
            building.footprintReconciliationSource = resolution.source;
            if (!building.loadedFromSave || string.IsNullOrWhiteSpace(building.footprintReconciliationNote))
            {
                building.footprintReconciliationNote = resolution.note;
            }

            if (updateIntendedFootprint)
            {
                building.intendedFootprintSizeCells = resolution.resolvedSizeCells;
            }
        }

        private string BuildFootprintAuthorityInspectionSummary(PlacedBuilding building)
        {
            if (building == null || building.definition == null)
            {
                return string.Empty;
            }

            PlacementFootprintResolution resolution = ResolvePlacementFootprint(building.definition);
            string authorityRead = resolution.hasPrefabAuthority
                ? $"prefab minimum {resolution.authorityMinimumSizeCells.x}x{resolution.authorityMinimumSizeCells.y}"
                : "no prefab authority";
            string expansionRead = resolution.expandedByAuthority ? "expanded placement claim" : "definition-compatible claim";
            string savedRead = string.Empty;

            if (building.loadedFromSave)
            {
                if (building.footprintRebuiltFromCurrentAuthorityOnLoad)
                {
                    savedRead = " | loaded save rebuilt to current authority";
                }
                else if (building.savedFootprintPreservedAfterAuthorityMismatch)
                {
                    savedRead = " | loaded save preserved because current authority could not safely fit";
                }
                else if (building.footprintMatchedCurrentAuthorityOnLoad)
                {
                    savedRead = " | loaded save matches current authority";
                }
            }

            return $"Footprint authority: definition {resolution.definitionSizeCells.x}x{resolution.definitionSizeCells.y}; {authorityRead}; resolved {resolution.resolvedSizeCells.x}x{resolution.resolvedSizeCells.y}; {expansionRead}; visual scale preserved{savedRead}.";
        }

        private string BuildPrefabPlacementInspectionSummary(PlacedBuilding building)
        {
            if (building == null || building.definition == null || building.definition.VisualPrefab == null)
            {
                return string.Empty;
            }

            if (!TryResolvePrefabPlacementPose(building, building.definition.VisualPrefab, out PrefabPlacementPose pose))
            {
                return "Prefab placement: unavailable; missing placement authority data.";
            }

            if (pose.roadAccessAnchored)
            {
                string fallbackRead = pose.usedFallback
                    ? $" | fallback {pose.fallbackReason}"
                    : string.Empty;
                return $"Prefab placement: road-access anchored via {pose.source}; target road access {FormatWorldPoint(pose.roadAccessTargetWorldPosition)}; resolved root {FormatWorldPoint(pose.rootWorldPosition)}; authored road-access point {FormatWorldPoint(pose.resolvedRoadAccessWorldPosition)}; centered root would be {FormatWorldPoint(pose.centeredRootWorldPosition)}{fallbackRead}.";
            }

            string reason = string.IsNullOrWhiteSpace(pose.fallbackReason) ? pose.source : pose.fallbackReason;
            return $"Prefab placement: centered ({reason}); resolved root {FormatWorldPoint(pose.rootWorldPosition)}.";
        }

        private bool TryResolvePrefabPlacementPose(PlacedBuilding building, GameObject visualRoot, out PrefabPlacementPose pose)
        {
            pose = default;
            if (building == null || building.definition == null || settings == null || !building.footprint.IsValid)
            {
                return false;
            }

            BuildingDefinition definition = building.definition;
            Quaternion rotation = Quaternion.Euler(0f, GetFrontageYaw(building.frontageDirection) + definition.VisualYawOffsetDegrees, 0f);
            float groundHeight = settings.worldCenter.y;
            if (!TryResolveFootprintGroundHeight(building.footprint, true, out groundHeight, out _))
            {
                return false;
            }

            Vector3 centeredRoot = GetRectWorldCenter(building.footprint, groundHeight) + rotation * definition.VisualPositionOffset;
            Vector3 effectiveScale = ResolveEffectiveVisualRootScale(definition, building.footprint, visualRoot, rotation);

            pose = new PrefabPlacementPose
            {
                rootWorldPosition = centeredRoot,
                rootWorldRotation = rotation,
                rootWorldScale = effectiveScale,
                centeredRootWorldPosition = centeredRoot,
                source = "legacy centered placement"
            };

            if (!IsFinite(effectiveScale))
            {
                pose.fallbackReason = "fallback to centered placement because the visual root scale is invalid";
                return true;
            }

            if (!TryGetRoadAccessPlacementAuthority(visualRoot, definition, out BuildingFootprintAuthority authority))
            {
                pose.source = "no BuildingFootprintAuthority road-access anchor";
                return true;
            }

            if (!authority.UsesFrontPointAsRoadAccessAnchor)
            {
                pose.source = "front-point road-access anchoring disabled";
                return true;
            }

            if (!authority.TryGetRoadAccessLocalPose(out Vector3 localPoint, out _)
                || !IsFinite(localPoint))
            {
                pose.usedFallback = true;
                pose.fallbackReason = "fallback to centered placement because the authored road-access point is malformed";
                return true;
            }

            if (!TryResolveRoadAccessPlacementTarget(building, out Vector3 roadAccessTarget, out string targetFailure))
            {
                pose.usedFallback = true;
                pose.fallbackReason = $"fallback to centered placement because {targetFailure}";
                return true;
            }

            Vector3 scaledLocalPoint = Vector3.Scale(localPoint, effectiveScale);
            pose.roadAccessAnchored = true;
            pose.source = "BuildingFootprintAuthority front point";
            pose.roadAccessTargetWorldPosition = roadAccessTarget;
            pose.rootWorldPosition = roadAccessTarget - rotation * scaledLocalPoint;
            pose.resolvedRoadAccessWorldPosition = pose.rootWorldPosition + rotation * scaledLocalPoint;
            return true;
        }

        private bool TryResolveRoadAccessPlacementTarget(PlacedBuilding building, out Vector3 target, out string failureReason)
        {
            target = default;
            failureReason = string.Empty;

            if (grid == null)
            {
                failureReason = "the town grid is missing";
                return false;
            }

            if (building == null || !building.footprint.IsValid)
            {
                failureReason = "the building footprint is missing";
                return false;
            }

            if (!TryGetPlot(building.plotId, out TownPlot plot) || plot == null)
            {
                failureReason = "the source plot is missing";
                return false;
            }

            if (!grid.IsInBounds(plot.roadAccessCell))
            {
                failureReason = $"road access cell {plot.roadAccessCell} is outside the town grid";
                return false;
            }

            if (!grid.GetCell(plot.roadAccessCell).IsRoad)
            {
                failureReason = $"road access cell {plot.roadAccessCell} is not marked as road";
                return false;
            }

            float y = settings != null ? settings.worldCenter.y : 0f;
            Vector3 roadAccessCenter = grid.CoordToWorldCenter(plot.roadAccessCell, y);
            float cellSize = grid.CellSizeMeters;
            float minX = grid.Origin.x + building.footprint.xMin * cellSize;
            float maxX = grid.Origin.x + (building.footprint.xMaxInclusive + 1) * cellSize;
            float minZ = grid.Origin.z + building.footprint.zMin * cellSize;
            float maxZ = grid.Origin.z + (building.footprint.zMaxInclusive + 1) * cellSize;

            target = building.frontageDirection switch
            {
                GridDirection.North => new Vector3(Mathf.Clamp(roadAccessCenter.x, minX, maxX), y, maxZ),
                GridDirection.East => new Vector3(maxX, y, Mathf.Clamp(roadAccessCenter.z, minZ, maxZ)),
                GridDirection.South => new Vector3(Mathf.Clamp(roadAccessCenter.x, minX, maxX), y, minZ),
                GridDirection.West => new Vector3(minX, y, Mathf.Clamp(roadAccessCenter.z, minZ, maxZ)),
                _ => grid.CoordToWorldCenter(building.footprint.Center, y)
            };
            if (worldSurfaceProvider != null
                && worldSurfaceProvider.TrySampleSurface(target, out WorldSurfaceSample sample)
                && IsFinite(sample.height))
            {
                target.y = sample.height;
            }
            return true;
        }

        private static bool TryGetRoadAccessPlacementAuthority(GameObject visualRoot, BuildingDefinition definition, out BuildingFootprintAuthority authority)
        {
            authority = visualRoot != null ? visualRoot.GetComponent<BuildingFootprintAuthority>() : null;
            if (authority != null)
            {
                return true;
            }

            GameObject prefab = definition != null ? definition.VisualPrefab : null;
            authority = prefab != null ? prefab.GetComponent<BuildingFootprintAuthority>() : null;
            return authority != null;
        }

        private Vector3 ResolveEffectiveVisualRootScale(BuildingDefinition definition, GridRect footprint, GameObject visualRoot, Quaternion rotation)
        {
            float multiplier = ResolveVisualScaleMultiplier(definition, footprint, visualRoot, rotation);
            Vector3 authoredScale = visualRoot != null ? visualRoot.transform.localScale : Vector3.one;
            return new Vector3(
                authoredScale.x * multiplier,
                authoredScale.y * multiplier,
                authoredScale.z * multiplier);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static string FormatWorldPoint(Vector3 point)
        {
            return $"{point.x:0.##}/{point.y:0.##}/{point.z:0.##}m";
        }

        private struct PrefabPlacementPose
        {
            public Vector3 rootWorldPosition;
            public Quaternion rootWorldRotation;
            public Vector3 rootWorldScale;
            public Vector3 centeredRootWorldPosition;
            public bool roadAccessAnchored;
            public bool usedFallback;
            public string source;
            public string fallbackReason;
            public Vector3 roadAccessTargetWorldPosition;
            public Vector3 resolvedRoadAccessWorldPosition;
        }

        private bool TryBuildFootprint(TownPlot plot, BuildingDefinition definition, out GridRect footprint)
        {
            if (!DefinitionFitsPlot(plot, definition))
            {
                footprint = default;
                return false;
            }

            Vector2Int size = GetPlacementWorldFootprintSize(definition, plot.roadFrontageDirection);
            int width = size.x;
            int depth = size.y;

            int xMin;
            int zMin;
            switch (plot.roadFrontageDirection)
            {
                case GridDirection.East:
                    {
                        int xMax = plot.bounds.xMaxInclusive - settings.buildingSetbackCells;
                        xMin = xMax - width + 1;
                        zMin = plot.bounds.zMin + Mathf.Max(0, (plot.bounds.depth - depth) / 2);
                        break;
                    }
                case GridDirection.West:
                    xMin = plot.bounds.xMin + settings.buildingSetbackCells;
                    zMin = plot.bounds.zMin + Mathf.Max(0, (plot.bounds.depth - depth) / 2);
                    break;
                case GridDirection.North:
                    {
                        int zMax = plot.bounds.zMaxInclusive - settings.buildingSetbackCells;
                        zMin = zMax - depth + 1;
                        xMin = plot.bounds.xMin + Mathf.Max(0, (plot.bounds.width - width) / 2);
                        break;
                    }
                case GridDirection.South:
                    zMin = plot.bounds.zMin + settings.buildingSetbackCells;
                    xMin = plot.bounds.xMin + Mathf.Max(0, (plot.bounds.width - width) / 2);
                    break;
                default:
                    xMin = plot.bounds.xMin + settings.buildingSetbackCells;
                    zMin = plot.bounds.zMin + Mathf.Max(0, (plot.bounds.depth - depth) / 2);
                    break;
            }

            footprint = new GridRect(xMin, zMin, width, depth);
            return grid.RectCellsAreBuildable(footprint, true)
                && TryResolveFootprintGroundHeight(footprint, true, out _, out _);
        }

        private bool TryResolveFootprintGroundHeight(
            GridRect footprint,
            bool requireBuildingCompatibility,
            out float stableGroundHeight,
            out string error)
        {
            stableGroundHeight = settings != null ? settings.worldCenter.y : 0f;
            error = string.Empty;
            if (grid == null || !grid.ContainsRect(footprint))
            {
                error = "footprint is outside the town grid";
                return false;
            }

            if (currentStartupMode != WorldStartupMode.UsePreAuthoredTerrain)
            {
                float fallbackTotal = 0f;
                int fallbackCount = 0;
                foreach (GridCoord coord in TownGrid.EachCoord(footprint))
                {
                    fallbackTotal += grid.GetCell(coord).height;
                    fallbackCount++;
                }

                stableGroundHeight = fallbackCount > 0 ? fallbackTotal / fallbackCount : stableGroundHeight;
                return true;
            }

            if (worldSurfaceProvider == null)
            {
                error = "authored surface provider is missing";
                return false;
            }

            float cellSize = grid.CellSizeMeters;
            float minX = grid.Origin.x + footprint.xMin * cellSize;
            float maxX = grid.Origin.x + (footprint.xMaxInclusive + 1) * cellSize;
            float minZ = grid.Origin.z + footprint.zMin * cellSize;
            float maxZ = grid.Origin.z + (footprint.zMaxInclusive + 1) * cellSize;
            float inset = Mathf.Min(cellSize * 0.1f, 0.15f);
            float centerX = (minX + maxX) * 0.5f;
            float centerZ = (minZ + maxZ) * 0.5f;
            Vector3[] points =
            {
                new(centerX, stableGroundHeight, centerZ),
                new(minX + inset, stableGroundHeight, minZ + inset),
                new(maxX - inset, stableGroundHeight, minZ + inset),
                new(minX + inset, stableGroundHeight, maxZ - inset),
                new(maxX - inset, stableGroundHeight, maxZ - inset),
                new(centerX, stableGroundHeight, minZ + inset),
                new(centerX, stableGroundHeight, maxZ - inset),
                new(minX + inset, stableGroundHeight, centerZ),
                new(maxX - inset, stableGroundHeight, centerZ)
            };

            float minimumHeight = float.MaxValue;
            float maximumHeight = float.MinValue;
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 point = points[i];
                if (!worldSurfaceProvider.TrySampleSurface(point, out WorldSurfaceSample sample)
                    || !sample.isInsideWorld
                    || sample.isWater
                    || sample.isBlocked
                    || !IsFinite(sample.height)
                    || !IsFinite(sample.slopeDegrees))
                {
                    error = $"footprint sample {i + 1} is outside terrain, water, blocked, or invalid";
                    return false;
                }

                if (requireBuildingCompatibility
                    && !worldSurfaceProvider.IsBuildable(
                        point,
                        new PlacementQuery
                        {
                            maximumSlopeDegrees = maximumBuildingSlopeDegrees,
                            forBuilding = true
                        }))
                {
                    error = $"footprint sample {i + 1} is inside NoBuilding or exceeds the building slope limit";
                    return false;
                }

                minimumHeight = Mathf.Min(minimumHeight, sample.height);
                maximumHeight = Mathf.Max(maximumHeight, sample.height);
            }

            float heightDifference = maximumHeight - minimumHeight;
            if (heightDifference > maximumBuildingFootprintHeightDifference)
            {
                error = $"footprint height difference {heightDifference:0.###}m exceeds {maximumBuildingFootprintHeightDifference:0.###}m";
                return false;
            }

            stableGroundHeight = maximumHeight;
            return true;
        }

        private bool DefinitionFitsPlot(TownPlot plot, BuildingDefinition definition)
        {
            if (plot == null || definition == null || settings == null || !plot.bounds.IsValid)
            {
                return false;
            }

            GetPlacementFrontageDepthSpan(definition, out int frontageSpan, out int depthSpan);
            int usableDepth = Mathf.Max(0, plot.depthCells - settings.buildingSetbackCells);
            return frontageSpan <= plot.frontageCells && depthSpan <= usableDepth;
        }

        private void ClaimBuildingFootprint(PlacedBuilding building)
        {
            foreach (GridCoord coord in TownGrid.EachCoord(building.footprint))
            {
                ref TownCell cell = ref grid.GetCellRef(coord);
                cell.occupancy |= CellOccupancy.Building | CellOccupancy.Blocked;
                cell.buildingId = building.id;
                cell.blocked = true;
            }
        }

        private void CreateAnchors(PlacedBuilding building)
        {
            Dictionary<AnchorType, AnchorResolutionStatus> authoredStatuses = new();
            bool hasPrefabMarkers = TryCreatePrefabMarkerAnchors(building, authoredStatuses);

            foreach (BuildingAnchorRule rule in building.definition.AnchorRules)
            {
                if (HasAnchor(building, rule.type))
                {
                    continue;
                }

                AnchorResolutionStatus authoredStatus = GetAuthoredAnchorStatus(authoredStatuses, rule.type);
                if (authoredStatus == AnchorResolutionStatus.FoundInvalid)
                {
                    AddAnchorIssue(
                        building,
                        rule.type,
                        $"Building '{building.definition.DisplayName}' has an authored {GetAnchorAuthoringName(rule.type)} marker, but it did not resolve to a valid anchor. Legacy fallback is intentionally disabled for this anchor so authored data is not masked.",
                        true);
                    continue;
                }

                if (hasPrefabMarkers)
                {
                    AddAnchorIssue(
                        building,
                        rule.type,
                        $"Building '{building.definition.DisplayName}' prefab is missing a {GetAnchorAuthoringName(rule.type)} marker. Falling back to its legacy BuildingDefinition anchor rule for this anchor.",
                        false);
                }
                else if (building.definition.VisualPrefab != null)
                {
                    AddAnchorIssue(
                        building,
                        rule.type,
                        $"Building '{building.definition.DisplayName}' prefab has no anchor markers. Falling back to legacy BuildingDefinition anchor rules. Add {FrontDoorMarkerName}, {ServicePointMarkerName}, and {DropOffPointMarkerName} children or BuildingAnchorMarker components to the prefab.",
                        false);
                }

                GridDirection anchorDirection = GetAnchorSideDirection(building.frontageDirection, rule.side);
                GridCoord sideCenter = GetFootprintEdgeCenter(building.footprint, anchorDirection);
                GridCoord coord = TownGrid.RotateLocalFrontOffset(sideCenter, anchorDirection, rule.offsetFromSideCenter);
                if (!TryAddAnchor(building, rule.type, coord, "legacy BuildingDefinition anchor rule", false, true))
                {
                    continue;
                }
            }
        }

        private bool TryCreatePrefabMarkerAnchors(PlacedBuilding building, Dictionary<AnchorType, AnchorResolutionStatus> authoredStatuses)
        {
            BuildingDefinition definition = building.definition;
            GameObject prefab = definition != null ? definition.VisualPrefab : null;
            if (prefab == null)
            {
                return false;
            }

            bool foundAnyMarker = false;
            HashSet<AnchorType> discoveredMarkerTypes = new();
            BuildingAnchorMarker[] componentMarkers = prefab.GetComponentsInChildren<BuildingAnchorMarker>(true);
            foreach (BuildingAnchorMarker marker in componentMarkers)
            {
                if (marker == null)
                {
                    continue;
                }

                foundAnyMarker = true;
                discoveredMarkerTypes.Add(marker.Type);
                RecordAuthoredAnchorStatus(
                    authoredStatuses,
                    marker.Type,
                    AddPrefabMarkerAnchor(building, prefab.transform, marker.transform, marker.Type, $"BuildingAnchorMarker on '{marker.name}'"));
            }

            foundAnyMarker |= TryAddNamedPrefabMarkerAnchor(building, prefab.transform, FrontDoorMarkerName, AnchorType.FrontDoor, discoveredMarkerTypes, authoredStatuses);
            foundAnyMarker |= TryAddNamedPrefabMarkerAnchor(building, prefab.transform, ServicePointMarkerName, AnchorType.Service, discoveredMarkerTypes, authoredStatuses);
            foundAnyMarker |= TryAddNamedPrefabMarkerAnchor(building, prefab.transform, DropOffPointMarkerName, AnchorType.DropOff, discoveredMarkerTypes, authoredStatuses);
            foundAnyMarker |= TryAddFootprintAuthorityFrontDoorAnchor(building, prefab.transform, discoveredMarkerTypes, authoredStatuses);

            return foundAnyMarker;
        }

        private bool TryAddNamedPrefabMarkerAnchor(
            PlacedBuilding building,
            Transform prefabRoot,
            string markerName,
            AnchorType anchorType,
            HashSet<AnchorType> discoveredMarkerTypes,
            Dictionary<AnchorType, AnchorResolutionStatus> authoredStatuses)
        {
            if (discoveredMarkerTypes.Contains(anchorType))
            {
                return false;
            }

            if (HasAnchor(building, anchorType))
            {
                return false;
            }

            Transform marker = FindChildByExactName(prefabRoot, markerName);
            if (marker == null)
            {
                return false;
            }

            discoveredMarkerTypes.Add(anchorType);
            RecordAuthoredAnchorStatus(
                authoredStatuses,
                anchorType,
                AddPrefabMarkerAnchor(building, prefabRoot, marker, anchorType, $"prefab marker '{markerName}'"));
            return true;
        }

        private bool TryAddFootprintAuthorityFrontDoorAnchor(
            PlacedBuilding building,
            Transform prefabRoot,
            HashSet<AnchorType> discoveredMarkerTypes,
            Dictionary<AnchorType, AnchorResolutionStatus> authoredStatuses)
        {
            if (discoveredMarkerTypes.Contains(AnchorType.FrontDoor) || HasAnchor(building, AnchorType.FrontDoor))
            {
                return false;
            }

            BuildingFootprintAuthority authority = prefabRoot != null ? prefabRoot.GetComponent<BuildingFootprintAuthority>() : null;
            if (authority == null || !authority.UsesCornerAuthoring)
            {
                return false;
            }

            discoveredMarkerTypes.Add(AnchorType.FrontDoor);
            RecordAuthoredAnchorStatus(
                authoredStatuses,
                AnchorType.FrontDoor,
                AddPrefabLocalPointAnchor(
                    building,
                    prefabRoot,
                    authority.FrontDoorLocalPoint,
                    AnchorType.FrontDoor,
                    "BuildingFootprintAuthority front point"));
            return true;
        }

        private AnchorResolutionStatus AddPrefabMarkerAnchor(PlacedBuilding building, Transform prefabRoot, Transform marker, AnchorType anchorType, string source)
        {
            if (HasAnchor(building, anchorType))
            {
                AddAnchorIssue(
                    building,
                    anchorType,
                    $"Building '{building.definition.DisplayName}' has more than one {GetAnchorAuthoringName(anchorType)} marker. Using the first valid marker and ignoring '{marker.name}'.",
                    false);
                return AnchorResolutionStatus.FoundValid;
            }

            Vector3 markerWorld = PrefabMarkerToWorldPosition(building, prefabRoot, marker);
            GridCoord coord = grid.WorldToCoord(markerWorld);
            return TryAddAnchor(building, anchorType, coord, source, true, false, true, markerWorld)
                ? AnchorResolutionStatus.FoundValid
                : AnchorResolutionStatus.FoundInvalid;
        }

        private AnchorResolutionStatus AddPrefabLocalPointAnchor(PlacedBuilding building, Transform prefabRoot, Vector3 localPoint, AnchorType anchorType, string source)
        {
            if (HasAnchor(building, anchorType))
            {
                return AnchorResolutionStatus.FoundValid;
            }

            Vector3 markerWorld = PrefabLocalPointToWorldPosition(building, prefabRoot, localPoint);
            GridCoord coord = grid.WorldToCoord(markerWorld);
            return TryAddAnchor(building, anchorType, coord, source, true, false, true, markerWorld)
                ? AnchorResolutionStatus.FoundValid
                : AnchorResolutionStatus.FoundInvalid;
        }

        private static bool ShouldReconcileAuthoredAnchorToExteriorAccess(PlacedBuilding building, AnchorType anchorType, GridCoord authoredCoord)
        {
            // Authored prefab anchors are intentional building/property destinations: porch, door, service point,
            // delivery point, or similar lot-level target. The road/frontage connection remains the plot road access
            // cell; it should not replace the authored destination unless we later add an explicit "force exterior"
            // authoring flag for a specific prefab.
            return false;
        }

        private bool TryResolveAuthoredAnchorExteriorAccess(
            PlacedBuilding building,
            AnchorType anchorType,
            GridCoord authoredCoord,
            string source,
            out GridCoord exteriorCoord,
            out string reconciliationNote)
        {
            exteriorCoord = authoredCoord;
            reconciliationNote = string.Empty;

            if (building == null || grid == null || !grid.IsInBounds(authoredCoord) || !building.footprint.IsValid)
            {
                return false;
            }

            if (!building.footprint.Contains(authoredCoord))
            {
                return false;
            }

            GridDirection primaryDirection = anchorType == AnchorType.FrontDoor
                ? building.frontageDirection
                : GetNearestFootprintEdgeDirection(building.footprint, authoredCoord);

            if (!TryFindExteriorAnchorAccessCell(building.footprint, primaryDirection, authoredCoord, anchorType, out exteriorCoord))
            {
                return false;
            }

            if (exteriorCoord.Equals(authoredCoord))
            {
                return false;
            }

            string label = GetAnchorDisplayLabel(anchorType);
            string displayName = building.definition != null ? building.definition.DisplayName : "Unknown";
            string directionRead = FormatAnchorSideRead(building.frontageDirection, GetNearestFootprintEdgeDirection(building.footprint, exteriorCoord));
            reconciliationNote = $"Building '{displayName}' {label} from {source} resolved inside the claimed building footprint at {authoredCoord}. Runtime anchor was reconciled to exterior access cell {exteriorCoord} on the {directionRead}; visual prefab and authored marker were not changed.";
            return true;
        }

        private bool TryFindExteriorAnchorAccessCell(
            GridRect footprint,
            GridDirection primaryDirection,
            GridCoord authoredCoord,
            AnchorType anchorType,
            out GridCoord bestCoord)
        {
            bestCoord = default;
            bool found = false;
            int bestScore = int.MinValue;

            GridDirection[] directions =
            {
                primaryDirection,
                TurnLeft(primaryDirection),
                TurnRight(primaryDirection),
                Opposite(primaryDirection)
            };

            HashSet<GridDirection> visited = new();
            for (int i = 0; i < directions.Length; i++)
            {
                GridDirection direction = directions[i];
                if (!visited.Add(direction))
                {
                    continue;
                }

                int span = direction == GridDirection.North || direction == GridDirection.South
                    ? footprint.width
                    : footprint.depth;

                for (int offset = 0; offset < span; offset++)
                {
                    GridCoord candidate = GetExteriorCellAlongFootprintEdge(footprint, direction, offset);
                    if (!ExteriorAnchorCellIsUsable(candidate))
                    {
                        continue;
                    }

                    int score = ScoreExteriorAnchorCandidate(candidate, authoredCoord, direction, primaryDirection, anchorType);
                    if (!found || score > bestScore)
                    {
                        found = true;
                        bestScore = score;
                        bestCoord = candidate;
                    }
                }
            }

            return found;
        }

        private static GridCoord GetExteriorCellAlongFootprintEdge(GridRect footprint, GridDirection direction, int offset)
        {
            return direction switch
            {
                GridDirection.North => new GridCoord(footprint.xMin + offset, footprint.zMaxInclusive + 1),
                GridDirection.East => new GridCoord(footprint.xMaxInclusive + 1, footprint.zMin + offset),
                GridDirection.South => new GridCoord(footprint.xMin + offset, footprint.zMin - 1),
                GridDirection.West => new GridCoord(footprint.xMin - 1, footprint.zMin + offset),
                _ => footprint.Center
            };
        }

        private bool ExteriorAnchorCellIsUsable(GridCoord coord)
        {
            if (grid == null || !grid.IsInBounds(coord))
            {
                return false;
            }

            TownCell cell = grid.GetCell(coord);
            if (cell.IsRoad)
            {
                return true;
            }

            if (cell.HasBuilding || (cell.occupancy & CellOccupancy.Blocked) != 0 || cell.blocked)
            {
                return false;
            }

            if ((cell.occupancy & CellOccupancy.Plot) != 0)
            {
                return true;
            }

            return cell.terrainZone == TerrainZone.Buildable;
        }

        private int ScoreExteriorAnchorCandidate(
            GridCoord candidate,
            GridCoord authoredCoord,
            GridDirection candidateDirection,
            GridDirection primaryDirection,
            AnchorType anchorType)
        {
            int distance = Mathf.Abs(candidate.x - authoredCoord.x) + Mathf.Abs(candidate.z - authoredCoord.z);
            int score = -distance * 20;

            if (candidateDirection == primaryDirection)
            {
                score += 2000;
            }

            bool roadAccess = AnchorTouchesRoad(candidate);
            bool yardAccess = AnchorTouchesOpenPlot(candidate);

            if (anchorType == AnchorType.FrontDoor)
            {
                score += roadAccess ? 1500 : yardAccess ? 250 : 0;
            }
            else
            {
                score += yardAccess ? 900 : roadAccess ? 300 : 0;
            }

            return score;
        }

        private Vector3 PrefabMarkerToWorldPosition(PlacedBuilding building, Transform prefabRoot, Transform marker)
        {
            return PrefabLocalPointToWorldPosition(building, prefabRoot, prefabRoot.InverseTransformPoint(marker.position));
        }

        private Vector3 PrefabLocalPointToWorldPosition(PlacedBuilding building, Transform prefabRoot, Vector3 localPoint)
        {
            GameObject visualRoot = prefabRoot != null ? prefabRoot.gameObject : null;
            if (!TryResolvePrefabPlacementPose(building, visualRoot, out PrefabPlacementPose pose))
            {
                return GetRectWorldCenter(building.footprint, settings != null ? settings.worldCenter.y : 0f);
            }

            return pose.rootWorldPosition + pose.rootWorldRotation * Vector3.Scale(localPoint, pose.rootWorldScale);
        }

        private bool TryAddAnchor(
            PlacedBuilding building,
            AnchorType anchorType,
            GridCoord coord,
            string source,
            bool fromAuthoredMarker,
            bool fromFallbackRule,
            bool hasAuthoredWorldPosition = false,
            Vector3 authoredWorldPosition = default)
        {
            GridCoord authoredCoordBeforeReconciliation = coord;
            bool wasReconciledToExteriorAccess = false;
            string reconciliationNote = string.Empty;

            if (fromAuthoredMarker
                && ShouldReconcileAuthoredAnchorToExteriorAccess(building, anchorType, coord)
                && TryResolveAuthoredAnchorExteriorAccess(building, anchorType, coord, source, out GridCoord exteriorCoord, out reconciliationNote))
            {
                coord = exteriorCoord;
                wasReconciledToExteriorAccess = true;
                AddAnchorIssue(building, anchorType, reconciliationNote, false);
            }

            if (!grid.IsInBounds(coord))
            {
                AddAnchorIssue(
                    building,
                    anchorType,
                    $"Building '{building.definition.DisplayName}' {GetAnchorAuthoringName(anchorType)} from {source} resolved outside the town grid at {coord}.",
                    fromAuthoredMarker);
                return false;
            }

            ref TownCell cell = ref grid.GetCellRef(coord);
            cell.occupancy |= CellOccupancy.Anchor;
            building.anchors.Add(new BuildingAnchor
            {
                type = anchorType,
                coord = coord,
                fromAuthoredMarker = fromAuthoredMarker,
                fromFallbackRule = fromFallbackRule,
                source = source,
                hasAuthoredWorldPosition = hasAuthoredWorldPosition,
                authoredWorldPosition = authoredWorldPosition,
                wasReconciledToExteriorAccess = wasReconciledToExteriorAccess,
                authoredCoordBeforeReconciliation = authoredCoordBeforeReconciliation,
                reconciliationNote = reconciliationNote
            });
            return true;
        }

        private static AnchorResolutionStatus GetAuthoredAnchorStatus(Dictionary<AnchorType, AnchorResolutionStatus> statuses, AnchorType anchorType)
        {
            return statuses.TryGetValue(anchorType, out AnchorResolutionStatus status)
                ? status
                : AnchorResolutionStatus.Missing;
        }

        private static void RecordAuthoredAnchorStatus(Dictionary<AnchorType, AnchorResolutionStatus> statuses, AnchorType anchorType, AnchorResolutionStatus status)
        {
            if (!statuses.TryGetValue(anchorType, out AnchorResolutionStatus existing))
            {
                statuses[anchorType] = status;
                return;
            }

            if (existing == AnchorResolutionStatus.FoundValid)
            {
                return;
            }

            if (status == AnchorResolutionStatus.FoundValid)
            {
                statuses[anchorType] = status;
            }
        }

        private void AddAnchorIssue(PlacedBuilding building, AnchorType anchorType, string message, bool isError)
        {
            building.anchorIssues.Add(new BuildingAnchorIssue
            {
                type = anchorType,
                message = message,
                isError = isError
            });

            if (isError)
            {
                Debug.LogError(message, building.definition != null ? building.definition.VisualPrefab : this);
            }
            else
            {
                Debug.LogWarning(message, building.definition != null ? building.definition.VisualPrefab : this);
            }
        }

        private static Transform FindChildByExactName(Transform root, string childName)
        {
            if (root == null)
            {
                return null;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name == childName)
                {
                    return child;
                }

                Transform nested = FindChildByExactName(child, childName);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static string GetAnchorAuthoringName(AnchorType anchorType)
        {
            return anchorType switch
            {
                AnchorType.FrontDoor => "FrontDoor",
                AnchorType.Service => "ServicePoint",
                AnchorType.DropOff => "DropOffPoint",
                _ => anchorType.ToString()
            };
        }

        private static GridCoord GetFootprintEdgeCenter(GridRect footprint, GridDirection edgeDirection)
        {
            return edgeDirection switch
            {
                GridDirection.North => new GridCoord(footprint.xMin + footprint.width / 2, footprint.zMaxInclusive),
                GridDirection.East => new GridCoord(footprint.xMaxInclusive, footprint.zMin + footprint.depth / 2),
                GridDirection.South => new GridCoord(footprint.xMin + footprint.width / 2, footprint.zMin),
                GridDirection.West => new GridCoord(footprint.xMin, footprint.zMin + footprint.depth / 2),
                _ => footprint.Center
            };
        }

        private static GridDirection GetAnchorSideDirection(GridDirection frontageDirection, BuildingAnchorSide side)
        {
            return side switch
            {
                BuildingAnchorSide.Front => frontageDirection,
                BuildingAnchorSide.Back => Opposite(frontageDirection),
                BuildingAnchorSide.Left => TurnLeft(frontageDirection),
                BuildingAnchorSide.Right => TurnRight(frontageDirection),
                _ => frontageDirection
            };
        }

        private static GridDirection Opposite(GridDirection direction)
        {
            return direction switch
            {
                GridDirection.North => GridDirection.South,
                GridDirection.East => GridDirection.West,
                GridDirection.South => GridDirection.North,
                GridDirection.West => GridDirection.East,
                _ => direction
            };
        }

        private static GridDirection TurnLeft(GridDirection direction)
        {
            return direction switch
            {
                GridDirection.North => GridDirection.West,
                GridDirection.East => GridDirection.North,
                GridDirection.South => GridDirection.East,
                GridDirection.West => GridDirection.South,
                _ => direction
            };
        }

        private static GridDirection TurnRight(GridDirection direction)
        {
            return direction switch
            {
                GridDirection.North => GridDirection.East,
                GridDirection.East => GridDirection.South,
                GridDirection.South => GridDirection.West,
                GridDirection.West => GridDirection.North,
                _ => direction
            };
        }

        private void BuildVisuals()
        {
            Stopwatch totalTimer = ShouldLogRuntimeGenerationTiming() ? Stopwatch.StartNew() : null;
            Stopwatch stepTimer = totalTimer != null ? Stopwatch.StartNew() : null;
            StringBuilder timing = totalTimer != null ? new StringBuilder() : null;

            Transform root = EnsureVisualRoot();
            AppendGenerationTiming(timing, stepTimer, "ensure visual root");

            ClearVisualChildren(root);
            HashSet<int> authoredChildIds = CaptureRootChildIds(root);
            AppendGenerationTiming(timing, stepTimer, "clear visuals");

            if (settings.showTerrainOverlay)
            {
                BuildTerrainOverlay(root);
                AppendGenerationTiming(timing, stepTimer, "terrain overlay");
            }
            else
            {
                AppendGenerationTiming(timing, stepTimer, "terrain overlay skipped");
            }

            if (settings.showPlots)
            {
                BuildPlotVisuals(root);
                AppendGenerationTiming(timing, stepTimer, "plot visuals");
            }
            else
            {
                AppendGenerationTiming(timing, stepTimer, "plot visuals skipped");
            }

            BuildAgriculturalWorkingSiteVisuals(root);
            AppendGenerationTiming(timing, stepTimer, "agricultural visuals");

            BuildForestEnvironmentDressing(root);
            AppendGenerationTiming(timing, stepTimer, "forest dressing");

            if (settings.showRoads)
            {
                BuildRoadVisuals(root);
                AppendGenerationTiming(timing, stepTimer, "road visuals");
            }
            else
            {
                AppendGenerationTiming(timing, stepTimer, "road visuals skipped");
            }

            if (settings.showBuildings)
            {
                foreach (PlacedBuilding building in buildings)
                {
                    CreateBuildingVisual(root, building);
                }
                AppendGenerationTiming(timing, stepTimer, "building visuals");

                BuildDetailPropVisuals(root);
                AppendGenerationTiming(timing, stepTimer, "detail props");

                BuildPropertyAccessPathEvidence(root);
                AppendGenerationTiming(timing, stepTimer, "property access evidence");

                if (ShouldRunAutomaticBuildingVisualRepair())
                {
                    RepairSpawnedBuildingVisualMeshes(root, logSummary: false);
                    AppendGenerationTiming(timing, stepTimer, "building visual repair");
                }
                else
                {
                    AppendGenerationTiming(timing, stepTimer, "building visual repair skipped");
                }
            }
            else
            {
                AppendGenerationTiming(timing, stepTimer, "building visuals skipped");
            }

            BuildRegionalResourceVisuals(root);
            AppendGenerationTiming(timing, stepTimer, "regional resource visuals");

            if (ShouldShowAnchorVisuals())
            {
                foreach (PlacedBuilding building in buildings)
                {
                    foreach (BuildingAnchor anchor in building.anchors)
                    {
                        CreateAnchorVisual(root, building.id, anchor);
                    }
                }
                AppendGenerationTiming(timing, stepTimer, "anchor visuals");
            }
            else
            {
                AppendGenerationTiming(timing, stepTimer, "anchor visuals skipped");
            }

            BuildInteractionProxies(root);
            AppendGenerationTiming(timing, stepTimer, "interaction proxies");

            MarkGeneratedChildren(root, authoredChildIds);

            if (Application.isPlaying && conformGeneratedVisualsToRuntimeTerrain)
            {
                ConformGeneratedVisualsToRuntimeTerrain(logRuntimeTerrainVisualConformance);
                AppendGenerationTiming(timing, stepTimer, "terrain conformance");
            }
            else
            {
                AppendGenerationTiming(timing, stepTimer, "terrain conformance skipped");
            }

            if (totalTimer != null)
            {
                totalTimer.Stop();
                Debug.Log($"[TownWorld] BuildVisuals timing total {totalTimer.ElapsedMilliseconds} ms ({timing}).", this);
            }
        }

        [ContextMenu("Conform Generated Visuals To Runtime Terrain")]
        public void ConformGeneratedVisualsToRuntimeTerrainFromContextMenu()
        {
            if (!Application.isPlaying)
            {
                Debug.Log("[TownWorld] Runtime terrain visual conformance is Play Mode only. Enter Play Mode after RegionalTerrainTileView builds generated terrain, then run this command.", this);
                return;
            }

            int conformed = ConformGeneratedVisualsToRuntimeTerrain(logSummary: true);
            Debug.Log($"[TownWorld] Runtime terrain visual conformance checked generated visual roots and adjusted {conformed} object(s).", this);
        }

        public string BuildRuntimeTerrainVisualConformanceSummary()
        {
            string terrainRead = worldSurfaceProvider != null
                ? $"Surface provider bounds {worldSurfaceProvider.WorldBounds}"
                : "No world surface provider is available for visual conformance";

            return $"[TownWorld] Terrain visual conformance: enabled {conformGeneratedVisualsToRuntimeTerrain}, status {lastRuntimeTerrainConformStatus}, "
                + $"candidates {lastRuntimeTerrainConformCandidateCount}, adjusted {lastRuntimeTerrainConformedVisualCount}, "
                + $"already aligned {lastRuntimeTerrainAlreadyAlignedVisualCount}, outside terrain {lastRuntimeTerrainOutsideBoundsVisualCount}, "
                + $"missing renderable bounds {lastRuntimeTerrainMissingRenderableVisualCount}, capped moves {lastRuntimeTerrainCappedMoveCount}, "
                + $"average vertical move {lastRuntimeTerrainConformAverageDeltaMeters:0.###}m. {terrainRead}.";
        }

        private int ConformGeneratedVisualsToRuntimeTerrain(bool logSummary)
        {
            ResetRuntimeTerrainVisualConformanceDiagnostics("Checking");

            if (!conformGeneratedVisualsToRuntimeTerrain)
            {
                lastRuntimeTerrainConformStatus = "Disabled";
                if (logSummary)
                {
                    Debug.Log(BuildRuntimeTerrainVisualConformanceSummary(), this);
                }

                return 0;
            }

            if (worldSurfaceProvider == null)
            {
                lastRuntimeTerrainConformStatus = "World surface provider missing";
                if (logSummary)
                {
                    Debug.Log(BuildRuntimeTerrainVisualConformanceSummary(), this);
                }

                return 0;
            }

            Transform root = visualRoot != null ? visualRoot : transform.Find(VisualRootName);
            if (root == null)
            {
                lastRuntimeTerrainConformStatus = "Generated visual root missing";
                if (logSummary)
                {
                    Debug.Log(BuildRuntimeTerrainVisualConformanceSummary(), this);
                }

                return 0;
            }

            float totalDelta = 0f;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (!ShouldConformGeneratedVisualToTerrain(child))
                {
                    continue;
                }

                lastRuntimeTerrainConformCandidateCount++;
                TerrainVisualConformanceResult result = TryConformVisualRootToTerrain(child, out float delta, out bool wasCapped);
                switch (result)
                {
                    case TerrainVisualConformanceResult.Adjusted:
                        lastRuntimeTerrainConformedVisualCount++;
                        totalDelta += Mathf.Abs(delta);
                        if (wasCapped)
                        {
                            lastRuntimeTerrainCappedMoveCount++;
                        }
                        break;
                    case TerrainVisualConformanceResult.AlreadyAligned:
                        lastRuntimeTerrainAlreadyAlignedVisualCount++;
                        break;
                    case TerrainVisualConformanceResult.OutsideTerrain:
                        lastRuntimeTerrainOutsideBoundsVisualCount++;
                        break;
                    case TerrainVisualConformanceResult.MissingRenderable:
                        lastRuntimeTerrainMissingRenderableVisualCount++;
                        break;
                }
            }

            lastRuntimeTerrainConformAverageDeltaMeters = lastRuntimeTerrainConformedVisualCount > 0
                ? totalDelta / lastRuntimeTerrainConformedVisualCount
                : 0f;
            lastRuntimeTerrainConformStatus = lastRuntimeTerrainConformCandidateCount > 0
                ? "Completed"
                : "Completed with no generated visual candidates";

            if (logSummary)
            {
                Debug.Log(BuildRuntimeTerrainVisualConformanceSummary(), this);
            }

            return lastRuntimeTerrainConformedVisualCount;
        }

        private void ResetRuntimeTerrainVisualConformanceDiagnostics(string status)
        {
            lastRuntimeTerrainConformStatus = string.IsNullOrWhiteSpace(status) ? "Checking" : status;
            lastRuntimeTerrainConformCandidateCount = 0;
            lastRuntimeTerrainConformedVisualCount = 0;
            lastRuntimeTerrainAlreadyAlignedVisualCount = 0;
            lastRuntimeTerrainOutsideBoundsVisualCount = 0;
            lastRuntimeTerrainMissingRenderableVisualCount = 0;
            lastRuntimeTerrainCappedMoveCount = 0;
            lastRuntimeTerrainConformAverageDeltaMeters = 0f;
        }

        private enum TerrainVisualConformanceResult
        {
            Invalid,
            MissingRenderable,
            OutsideTerrain,
            AlreadyAligned,
            Adjusted
        }

        private TerrainVisualConformanceResult TryConformVisualRootToTerrain(Transform visual, out float deltaMeters, out bool wasCapped)
        {
            deltaMeters = 0f;
            wasCapped = false;
            if (visual == null || worldSurfaceProvider == null)
            {
                return TerrainVisualConformanceResult.Invalid;
            }

            Vector3 position = visual.position;
            if (!worldSurfaceProvider.TrySampleSurface(position, out WorldSurfaceSample surface)
                || !IsFinite(surface.height))
            {
                return TerrainVisualConformanceResult.OutsideTerrain;
            }

            float terrainY = surface.height;

            if (!TryResolveVisualBottomOffsetFromRoot(visual, out float bottomOffset))
            {
                return TerrainVisualConformanceResult.MissingRenderable;
            }

            float targetY = terrainY - bottomOffset + Mathf.Max(0f, runtimeTerrainVisualSurfaceOffsetMeters);
            float maxMove = Mathf.Max(0f, runtimeTerrainConformMaxSingleMoveMeters);
            if (maxMove > 0f && Mathf.Abs(targetY - position.y) > maxMove)
            {
                targetY = position.y + Mathf.Sign(targetY - position.y) * maxMove;
                wasCapped = true;
            }

            deltaMeters = targetY - position.y;
            if (Mathf.Abs(deltaMeters) <= 0.001f)
            {
                return TerrainVisualConformanceResult.AlreadyAligned;
            }

            visual.position = new Vector3(position.x, targetY, position.z);
            return TerrainVisualConformanceResult.Adjusted;
        }

        private static bool TrySampleRuntimeTerrainY(Terrain terrain, Vector3 worldPosition, out float y)
        {
            y = 0f;
            if (terrain == null || terrain.terrainData == null)
            {
                return false;
            }

            Vector3 terrainPosition = terrain.transform.position;
            Vector3 size = terrain.terrainData.size;
            if (worldPosition.x < terrainPosition.x
                || worldPosition.z < terrainPosition.z
                || worldPosition.x > terrainPosition.x + size.x
                || worldPosition.z > terrainPosition.z + size.z)
            {
                return false;
            }

            y = terrainPosition.y + terrain.SampleHeight(worldPosition);
            return true;
        }

        private bool TryGetTerrainForVisualConformance(out Terrain terrain)
        {
            terrain = null;
            if (terrainCollider is TerrainCollider terrainColliderComponent && terrainColliderComponent.gameObject.activeInHierarchy)
            {
                terrain = terrainColliderComponent.GetComponent<Terrain>();
                if (terrain != null && terrain.terrainData != null)
                {
                    return true;
                }
            }

            TerrainCollider foundCollider = FindAnyObjectByType<TerrainCollider>();
            if (foundCollider != null && foundCollider.gameObject.activeInHierarchy)
            {
                Terrain foundTerrain = foundCollider.GetComponent<Terrain>();
                if (foundTerrain != null && foundTerrain.terrainData != null)
                {
                    terrainCollider = foundCollider;
                    terrain = foundTerrain;
                    return true;
                }
            }

            return false;
        }

        private static bool TryResolveVisualBottomOffsetFromRoot(Transform visual, out float bottomOffset)
        {
            bottomOffset = 0f;
            if (visual == null)
            {
                return false;
            }

            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
            bool initialized = false;
            float minY = 0f;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || renderer is ParticleSystemRenderer)
                {
                    continue;
                }

                Bounds bounds = renderer.bounds;
                if (bounds.size.sqrMagnitude <= 0.000001f)
                {
                    continue;
                }

                minY = initialized ? Mathf.Min(minY, bounds.min.y) : bounds.min.y;
                initialized = true;
            }

            if (!initialized)
            {
                return false;
            }

            bottomOffset = minY - visual.position.y;
            return true;
        }

        private static bool ShouldConformGeneratedVisualToTerrain(Transform visual)
        {
            if (visual == null
                || !visual.gameObject.activeInHierarchy
                || visual.GetComponent<GeneratedWorldContentMarker>() == null)
            {
                return false;
            }

            string name = visual.name ?? string.Empty;
            if (name.StartsWith("Runtime Fallback Terrain Collider", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Regional Terrain", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Regional Water", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Regional Route", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Regional Scenic", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Regional Settlement", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Regional Parcel", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }


        [ContextMenu("Repair Spawned Building Visual Meshes")]
        private void RepairSpawnedBuildingVisualMeshesFromContextMenu()
        {
            if (!Application.isPlaying)
            {
                Debug.Log("[TownWorld] Building visual mesh repair is runtime-only. Enter Play Mode, then use this command if generated prefab meshes still appear hidden.", this);
                return;
            }

            Transform root = visualRoot != null ? visualRoot : transform.Find(VisualRootName);
            int checkedRoots = RepairSpawnedBuildingVisualMeshes(root, logSummary: true);
            Debug.Log($"[TownWorld] Building visual mesh repair checked {checkedRoots} generated building visual root(s).", this);
        }

        private static bool ShouldRunAutomaticBuildingVisualRepair()
        {
            return Application.isPlaying;
        }

        private int RepairSpawnedBuildingVisualMeshes(Transform root, bool logSummary)
        {
            if (root == null)
            {
                return 0;
            }

            int checkedRoots = 0;
            int repairedRoots = 0;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child == null
                    || string.IsNullOrWhiteSpace(child.name)
                    || !child.name.StartsWith("Building ", StringComparison.Ordinal))
                {
                    continue;
                }

                checkedRoots++;
                if (EnsurePrefabBuildingVisualMeshesVisible(child.gameObject, null))
                {
                    repairedRoots++;
                }
            }

            if (logSummary)
            {
                Debug.Log($"[TownWorld] Runtime building mesh repair scanned {checkedRoots} generated building visual root(s); {repairedRoots} have usable main mesh renderers after repair.", this);
            }

            return checkedRoots;
        }

        private void BuildPropertyAccessPathEvidence(Transform root)
        {
            if (!buildPropertyAccessPathEvidence || root == null || grid == null || buildings.Count == 0)
            {
                return;
            }

            int built = 0;
            for (int i = 0; i < buildings.Count; i++)
            {
                PlacedBuilding building = buildings[i];
                if (building == null || !building.footprint.IsValid || building.anchors.Count == 0)
                {
                    continue;
                }

                for (int a = 0; a < building.anchors.Count; a++)
                {
                    BuildingAnchor anchor = building.anchors[a];
                    if (!AnchorShouldReceivePropertyPath(building, anchor))
                    {
                        continue;
                    }

                    GridDirection approachDirection = anchor.type == AnchorType.FrontDoor
                        ? building.frontageDirection
                        : GetNearestFootprintEdgeDirection(building.footprint, anchor.coord);

                    if (!TryFindExteriorAnchorAccessCell(building.footprint, approachDirection, anchor.coord, anchor.type, out GridCoord approachCoord))
                    {
                        continue;
                    }

                    if (approachCoord.Equals(anchor.coord))
                    {
                        continue;
                    }

                    Vector3 start = grid.CoordToWorldCenter(approachCoord, settings.worldCenter.y + propertyAccessPathSurfaceOffsetMeters);
                    Vector3 end = anchor.hasAuthoredWorldPosition
                        ? anchor.authoredWorldPosition
                        : grid.CoordToWorldCenter(anchor.coord, settings.worldCenter.y + propertyAccessPathSurfaceOffsetMeters);
                    end.y = settings.worldCenter.y + propertyAccessPathSurfaceOffsetMeters;

                    if (CreatePropertyAccessPathSegment(root, building.id, anchor.type, start, end))
                    {
                        built++;
                    }
                }
            }

            if (logPropertyAccessPathEvidence && built > 0)
            {
                Debug.Log($"[TownWorld] Built {built} property access path evidence segment(s) from frontage approach cells to authored building anchors.", this);
            }
        }

        private static bool AnchorShouldReceivePropertyPath(PlacedBuilding building, BuildingAnchor anchor)
        {
            if (building == null || !building.footprint.IsValid || !anchor.fromAuthoredMarker)
            {
                return false;
            }

            if (anchor.type != AnchorType.FrontDoor && anchor.type != AnchorType.Service && anchor.type != AnchorType.DropOff)
            {
                return false;
            }

            return building.footprint.Contains(anchor.coord);
        }

        private bool CreatePropertyAccessPathSegment(Transform root, int buildingId, AnchorType anchorType, Vector3 start, Vector3 end)
        {
            Vector3 delta = end - start;
            delta.y = 0f;
            float length = delta.magnitude;
            if (length < 0.25f)
            {
                return false;
            }

            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = $"Property Path {buildingId:000} {anchorType}";
            visual.transform.SetParent(root, true);
            visual.transform.position = (start + end) * 0.5f;
            visual.transform.rotation = Quaternion.LookRotation(delta.normalized, Vector3.up);
            visual.transform.localScale = new Vector3(
                Mathf.Max(0.05f, propertyAccessPathWidthMeters),
                0.045f,
                length);

            Collider collider = visual.GetComponent<Collider>();
            if (collider != null)
            {
                DestroyUnityObject(collider);
            }

            Renderer renderer = visual.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = CreateDebugMaterial(propertyAccessPathColor);
            }

            return true;
        }

        private void BuildRegionalResourceVisuals(Transform root)
        {
            if (root == null || grid == null || settings == null || RegionalResources == null)
            {
                return;
            }

            if (settings.showResourceSuitabilityOverlay || settings.showResourceDistricts)
            {
                for (int i = 0; i < RegionalResources.Districts.Count; i++)
                {
                    MineralDistrictRecord district = RegionalResources.Districts[i];
                    if (district == null)
                    {
                        continue;
                    }

                    string label = settings.showResourcePressureLabels
                        ? $"{district.Kind} District {district.Strength01:P0} freight {district.FreightPressure01:P0} settlement {district.SettlementPressure01:P0}"
                        : $"{district.Kind} District";
                    Color color = GetMineralColor(district.Kind);
                    CreateRectVisual(root, label, district.Bounds, 0.05f, 0.08f, color);
                }
            }

            if (settings.showResourceProtoSites)
            {
                for (int i = 0; i < RegionalResources.RemoteSites.Count; i++)
                {
                    RemoteIndustrySiteRecord site = RegionalResources.RemoteSites[i];
                    if (site == null)
                    {
                        continue;
                    }

                    string label = settings.showResourcePressureLabels
                        ? $"{site.DebugLabel} | site {site.SiteStrength01:P0}"
                        : site.DebugLabel;
                    CreatePointVisual(root, label, site.AnchorPosition + Vector3.up * 0.3f, 0.55f, settings.remoteProtoSiteColor);
                }
            }
        }

        private Color GetMineralColor(MineralResourceKind kind)
        {
            return kind switch
            {
                MineralResourceKind.Coal => settings.coalDistrictColor,
                MineralResourceKind.Iron => settings.ironDistrictColor,
                MineralResourceKind.Gold => settings.goldDistrictColor,
                MineralResourceKind.Silver => settings.silverDistrictColor,
                _ => settings.plotColor
            };
        }

        private void BuildInteractionProxies(Transform root)
        {
            if (root == null || grid == null || settings == null)
            {
                return;
            }

            for (int i = 0; i < plots.Count; i++)
            {
                TownPlot plot = plots[i];
                if (plot == null || !plot.bounds.IsValid)
                {
                    continue;
                }

                CreateInteractionProxy(
                    root,
                    $"Inspect Plot {plot.id:000}",
                    plot.bounds,
                    0.08f,
                    0.16f,
                    ResolvePlotInspectableKind(plot),
                    plot.id,
                    plot.buildingId);
            }

            for (int i = 0; i < buildings.Count; i++)
            {
                PlacedBuilding building = buildings[i];
                if (building == null || !building.footprint.IsValid)
                {
                    continue;
                }

                CreateInteractionProxy(
                    root,
                    $"Inspect Building {building.id:000}",
                    building.footprint,
                    1.6f,
                    3.2f,
                    ResolveBuildingInspectableKind(building),
                    building.plotId,
                    building.id);
            }
        }

        private void CreateInteractionProxy(
            Transform root,
            string objectName,
            GridRect rect,
            float yOffset,
            float height,
            WorldInspectableTargetKind kind,
            int plotId,
            int buildingId)
        {
            GameObject proxy = new(objectName);
            proxy.transform.SetParent(root, false);
            proxy.transform.position = GetRectWorldCenter(rect, settings.worldCenter.y + yOffset);
            proxy.transform.localScale = Vector3.one;

            BoxCollider collider = proxy.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(
                Mathf.Max(settings.cellSizeMeters, rect.width * settings.cellSizeMeters),
                Mathf.Max(0.1f, height),
                Mathf.Max(settings.cellSizeMeters, rect.depth * settings.cellSizeMeters));

            WorldInspectableTarget target = proxy.AddComponent<WorldInspectableTarget>();
            target.Configure(kind, plotId, buildingId);
        }

        private WorldInspectableTargetKind ResolvePlotInspectableKind(TownPlot plot)
        {
            if (plot == null)
            {
                return WorldInspectableTargetKind.Unknown;
            }

            if (plot.publicSiteRole != PublicSiteRole.None)
            {
                return WorldInspectableTargetKind.Civic;
            }

            return WorldInspectableTargetKind.Plot;
        }

        private WorldInspectableTargetKind ResolveBuildingInspectableKind(PlacedBuilding building)
        {
            if (building == null)
            {
                return WorldInspectableTargetKind.Unknown;
            }

            if (building.publicSiteRole != PublicSiteRole.None)
            {
                return WorldInspectableTargetKind.Civic;
            }

            if (building.definition != null && building.definition.CanHostWorkplace)
            {
                return WorldInspectableTargetKind.Business;
            }

            if (building.definition != null && building.definition.CanHostHouseholds)
            {
                return WorldInspectableTargetKind.Residence;
            }

            return WorldInspectableTargetKind.Building;
        }

        private bool ShouldShowAnchorVisuals()
        {
#if UNITY_EDITOR
            return settings != null && settings.showAnchors && !Application.isPlaying;
#else
            return false;
#endif
        }

        private void BuildTerrainOverlay(Transform root)
        {
            for (int z = 0; z < grid.Depth; z++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    GridCoord coord = new(x, z);
                    TownCell cell = grid.GetCell(coord);
                    Color color = cell.terrainZone == TerrainZone.Buildable ? settings.buildableTerrainColor : settings.blockedTerrainColor;
                    CreateCellVisual(root, $"Terrain {x}_{z}", coord, 0.01f, 0.02f, color);
                }
            }
        }

        private void BuildPlotVisuals(Transform root)
        {
            Material plotMaterial = CreateDebugMaterial(settings.plotColor);
            foreach (TownPlot plot in plots)
            {
                CreatePlotPerimeterVisual(root, plot, plotMaterial);
            }
        }

        private void BuildAgriculturalWorkingSiteVisuals(Transform root)
        {
            if (root == null || settings == null || grid == null)
            {
                return;
            }

            Material cropMaterial = CreateDebugMaterial(CropFieldVisualColor);
            Material cropStackMaterial = CreateDebugMaterial(CropStackVisualColor);
            Material ranchFenceMaterial = CreateDebugMaterial(RanchFenceVisualColor);
            Material ranchFeedMaterial = CreateDebugMaterial(RanchFeedVisualColor);
            Material sawmillLogMaterial = CreateDebugMaterial(SawmillLogVisualColor);
            Material sawmillBoardMaterial = CreateDebugMaterial(SawmillBoardVisualColor);
            Material sawmillTreeMaterial = CreateDebugMaterial(SawmillTreeVisualColor);
            Material sawmillCabinMaterial = CreateDebugMaterial(SawmillCabinVisualColor);

            for (int i = 0; i < plots.Count; i++)
            {
                TownPlot plot = plots[i];
                if (plot == null || plot.zone != PlotZone.Agricultural)
                {
                    continue;
                }

                if (!TryGetBuildingForPlot(plot, out PlacedBuilding building) || building.definition == null)
                {
                    continue;
                }

                switch (ResolveAgriculturalSiteRole(plot, building))
                {
                    case AgriculturalSiteRole.CropProductionYard:
                        CreateCropFarmDressing(root, plot, building, cropMaterial, cropStackMaterial);
                        break;
                    case AgriculturalSiteRole.LivestockYard:
                        CreateRanchDressing(root, plot, building, ranchFenceMaterial, ranchFeedMaterial);
                        break;
                    case AgriculturalSiteRole.SawmillYard:
                        CreateSawmillDressing(root, plot, building, sawmillLogMaterial, sawmillBoardMaterial, sawmillTreeMaterial, sawmillCabinMaterial);
                        break;
                }
            }
        }

        private void BuildForestEnvironmentDressing(Transform root)
        {
            if (!ShouldGenerateForestEnvironmentDressing() || !HasAnyForestEnvironmentPrefabs())
            {
                return;
            }

            int maxInstances = Mathf.Max(0, settings.forestDressingMaxInstances);
            if (maxInstances <= 0)
            {
                return;
            }

            HashSet<GridCoord> reservedCells = new();
            int createdCount = 0;

            CreateSawmillForestDistrictDressing(root, reservedCells, ref createdCount, maxInstances);
            CreateWetGroundCorridorDressing(root, reservedCells, ref createdCount, maxInstances);
            CreateAgriculturalEdgeTransitionDressing(root, reservedCells, ref createdCount, maxInstances);
            CreateMapEdgeForestBandDressing(root, reservedCells, ref createdCount, maxInstances);
        }

        private bool ShouldGenerateForestEnvironmentDressing()
        {
            return settings != null
                && grid != null
                && settings.generateForestEnvironmentDressing
                && settings.forestDressingMaxInstances > 0;
        }

        private bool HasAnyForestEnvironmentPrefabs()
        {
            return HasAnyPrefab(settings.forestCanopyTreePrefabs)
                || HasAnyPrefab(settings.forestUnderstoryPrefabs)
                || HasAnyPrefab(settings.forestGroundCoverPrefabs)
                || HasAnyPrefab(settings.forestWetGroundPrefabs)
                || HasAnyPrefab(settings.forestDeadfallPrefabs)
                || HasAnyPrefab(settings.forestMeadowPrefabs)
                || HasAnyPrefab(settings.forestRockPrefabs);
        }

        private void CreateSawmillForestDistrictDressing(
            Transform root,
            HashSet<GridCoord> reservedCells,
            ref int createdCount,
            int maxInstances)
        {
            if (!TryFindAgriculturalPlot(AgriculturalSiteRole.SawmillYard, out TownPlot plot, out PlacedBuilding building))
            {
                return;
            }

            int domainStart = createdCount;
            int domainLimit = Mathf.Min(maxInstances, domainStart + Mathf.Clamp(settings.sawmillForestRadiusCells * 8, 56, 128));
            CreateSawmillCutoverDressing(root, plot, building, reservedCells, ref createdCount, domainLimit);
            CreateSawmillSurroundingForestDressing(root, plot, reservedCells, ref createdCount, domainLimit);
            CreateSawmillApproachRoadDressing(root, plot, reservedCells, ref createdCount, domainLimit);
        }

        private void CreateSawmillCutoverDressing(
            Transform root,
            TownPlot plot,
            PlacedBuilding building,
            HashSet<GridCoord> reservedCells,
            ref int createdCount,
            int domainLimit)
        {
            int deadfallCount = Mathf.Clamp(plot.bounds.Area / 95, 6, 16);
            for (int i = 0; i < deadfallCount && createdCount < domainLimit; i++)
            {
                if (!TryGetDressingCell(plot, building, ForestSawmillSalt + i * 17, true, out GridCoord coord)
                    || !IsRearInteriorCell(plot, coord)
                    || !IsValidForestPlotDressingCell(plot, building, coord, reservedCells, 1))
                {
                    continue;
                }

                TryCreateForestEnvironmentAssetAtCell(
                    root,
                    coord,
                    reservedCells,
                    "Sawmill Cutover Deadfall",
                    i,
                    ForestSawmillSalt + i * 31,
                    ForestDeadfallScaleMin,
                    ForestDeadfallScaleMax,
                    1,
                    ref createdCount,
                    domainLimit,
                    settings.forestDeadfallPrefabs,
                    settings.forestRockPrefabs,
                    settings.forestUnderstoryPrefabs);
            }

            int standCount = Mathf.Clamp(plot.bounds.Area / 72, 12, 28);
            for (int i = 0; i < standCount && createdCount < domainLimit; i++)
            {
                if (!TryGetDressingCell(plot, building, ForestSawmillSalt + 300 + i * 19, true, out GridCoord coord)
                    || !IsRearInteriorCell(plot, coord)
                    || !IsValidForestPlotDressingCell(plot, building, coord, reservedCells, 2))
                {
                    continue;
                }

                bool placeTree = DeterministicUnit01(coord, ForestSawmillSalt + 360 + i) < 0.68f;
                TryCreateForestEnvironmentAssetAtCell(
                    root,
                    coord,
                    reservedCells,
                    placeTree ? "Sawmill Timber Stand" : "Sawmill Understory",
                    i,
                    ForestSawmillSalt + 380 + i * 31,
                    placeTree ? ForestTreeScaleMin : ForestUnderstoryScaleMin,
                    placeTree ? ForestTreeScaleMax : ForestUnderstoryScaleMax,
                    placeTree ? 2 : 1,
                    ref createdCount,
                    domainLimit,
                    placeTree ? settings.forestCanopyTreePrefabs : settings.forestUnderstoryPrefabs,
                    settings.forestUnderstoryPrefabs,
                    settings.forestGroundCoverPrefabs);
            }
        }

        private void CreateSawmillSurroundingForestDressing(
            Transform root,
            TownPlot plot,
            HashSet<GridCoord> reservedCells,
            ref int createdCount,
            int domainLimit)
        {
            int radius = Mathf.Clamp(settings.sawmillForestRadiusCells, 0, Mathf.Max(grid.Width, grid.Depth));
            if (radius <= 0)
            {
                return;
            }

            GridRect area = GetClampedExpandedRect(plot.bounds, radius);
            for (int z = area.zMin; z <= area.zMaxInclusive && createdCount < domainLimit; z++)
            {
                for (int x = area.xMin; x <= area.xMaxInclusive && createdCount < domainLimit; x++)
                {
                    GridCoord coord = new(x, z);
                    if (plot.bounds.Contains(coord))
                    {
                        continue;
                    }

                    int distance = ChebyshevDistanceToRect(coord, plot.bounds);
                    if (distance <= 0 || distance > radius || !IsRearOrSideExteriorCell(plot, coord, radius))
                    {
                        continue;
                    }

                    bool rear = IsCoordOnSideOfRect(coord, plot.bounds, Opposite(plot.roadFrontageDirection), radius);
                    float distanceT = Mathf.Clamp01(distance / (float)Mathf.Max(1, radius));
                    float density = rear
                        ? Mathf.Lerp(0.46f, 0.16f, distanceT)
                        : Mathf.Lerp(0.28f, 0.08f, distanceT);

                    if (DeterministicUnit01(coord, ForestSawmillSalt + 700) > density
                        || !IsOpenForestEnvironmentCell(coord, reservedCells, 2, 0, 4, rear ? 2 : 3))
                    {
                        continue;
                    }

                    float roll = DeterministicUnit01(coord, ForestSawmillSalt + 713);
                    bool nearCutover = distance <= 2 && roll < 0.46f;
                    bool canopy = !nearCutover && roll < 0.78f;
                    TryCreateForestEnvironmentAssetAtCell(
                        root,
                        coord,
                        reservedCells,
                        canopy ? "Sawmill Surrounding Forest" : "Sawmill Forest Edge",
                        createdCount,
                        ForestSawmillSalt + 730,
                        canopy ? ForestTreeScaleMin : ForestUnderstoryScaleMin,
                        canopy ? ForestTreeScaleMax : ForestUnderstoryScaleMax,
                        canopy ? 2 : 1,
                        ref createdCount,
                        domainLimit,
                        nearCutover ? settings.forestDeadfallPrefabs : canopy ? settings.forestCanopyTreePrefabs : settings.forestUnderstoryPrefabs,
                        settings.forestUnderstoryPrefabs,
                        settings.forestGroundCoverPrefabs);
                }
            }
        }

        private void CreateSawmillApproachRoadDressing(
            Transform root,
            TownPlot plot,
            HashSet<GridCoord> reservedCells,
            ref int createdCount,
            int domainLimit)
        {
            int approachLimit = Mathf.Min(domainLimit, createdCount + 28);
            int halfRoad = Mathf.Max(0, settings.roadWidthCells / 2);
            for (int z = 0; z < grid.Depth && createdCount < approachLimit; z++)
            {
                for (int x = 0; x < grid.Width && createdCount < approachLimit; x++)
                {
                    GridCoord roadCoord = new(x, z);
                    TownCell roadCell = grid.GetCell(roadCoord);
                    if (!roadCell.IsRoad
                        || roadCell.roadType != RoadType.Spur
                        || !IsLikelySawmillApproachRoadCell(plot, roadCoord)
                        || DeterministicUnit01(roadCoord, ForestSawmillSalt + 1000) > 0.18f)
                    {
                        continue;
                    }

                    int offsetDistance = halfRoad + 2 + RandomInclusive(0, 1, DeterministicUnit01(roadCoord, ForestSawmillSalt + 1011));
                    GridCoord[] offsets =
                    {
                        new(offsetDistance, 0),
                        new(-offsetDistance, 0),
                        new(0, offsetDistance),
                        new(0, -offsetDistance)
                    };

                    int start = RandomInclusive(0, offsets.Length - 1, DeterministicUnit01(roadCoord, ForestSawmillSalt + 1023));
                    for (int i = 0; i < offsets.Length && createdCount < approachLimit; i++)
                    {
                        GridCoord coord = roadCoord + offsets[(start + i) % offsets.Length];
                        if (!IsOpenForestEnvironmentCell(coord, reservedCells, 1, 0, 5, 2))
                        {
                            continue;
                        }

                        bool canopy = DeterministicUnit01(coord, ForestSawmillSalt + 1041) < 0.28f;
                        TryCreateForestEnvironmentAssetAtCell(
                            root,
                            coord,
                            reservedCells,
                            canopy ? "Sawmill Approach Tree" : "Sawmill Approach Brush",
                            createdCount,
                            ForestSawmillSalt + 1050,
                            canopy ? ForestTreeScaleMin : ForestUnderstoryScaleMin,
                            canopy ? ForestTreeScaleMax : ForestUnderstoryScaleMax,
                            canopy ? 2 : 1,
                            ref createdCount,
                            approachLimit,
                            canopy ? settings.forestCanopyTreePrefabs : settings.forestUnderstoryPrefabs,
                            settings.forestGroundCoverPrefabs,
                            settings.forestDeadfallPrefabs);
                        break;
                    }
                }
            }
        }

        private void CreateWetGroundCorridorDressing(
            Transform root,
            HashSet<GridCoord> reservedCells,
            ref int createdCount,
            int maxInstances)
        {
            if (!HasAnyPrefab(settings.forestWetGroundPrefabs)
                && !HasAnyPrefab(settings.forestRockPrefabs)
                && !HasAnyPrefab(settings.forestGroundCoverPrefabs)
                && !HasAnyPrefab(settings.forestDeadfallPrefabs))
            {
                return;
            }

            int width = Mathf.Clamp(settings.wetGroundCorridorWidthCells, 0, Mathf.Max(grid.Width, grid.Depth));
            if (width <= 0)
            {
                return;
            }

            int domainLimit = Mathf.Min(maxInstances, createdCount + Mathf.Clamp(maxInstances / 7, 24, 54));
            bool useLowTerrain = TryGetTerrainHeightRange(out float minHeight, out float maxHeight) && maxHeight - minHeight > 0.5f;

            for (int z = 1; z < grid.Depth - 1 && createdCount < domainLimit; z++)
            {
                int centerX = GetWetGroundCorridorCenterX(z, useLowTerrain);
                for (int dx = -width; dx <= width && createdCount < domainLimit; dx++)
                {
                    GridCoord coord = new(centerX + dx, z);
                    float offsetT = Mathf.Clamp01(Mathf.Abs(dx) / (float)Mathf.Max(1, width));
                    float density = Mathf.Lerp(0.32f, 0.07f, offsetT);
                    if (DeterministicUnit01(coord, ForestWetGroundSalt + 17) > density
                        || !IsOpenForestEnvironmentCell(coord, reservedCells, 2, 1, 4, 1))
                    {
                        continue;
                    }

                    float roll = DeterministicUnit01(coord, ForestWetGroundSalt + 29);
                    GameObject[] primary = roll < 0.58f
                        ? settings.forestWetGroundPrefabs
                        : roll < 0.76f
                            ? settings.forestGroundCoverPrefabs
                            : roll < 0.91f
                                ? settings.forestRockPrefabs
                                : settings.forestDeadfallPrefabs;
                    TryCreateForestEnvironmentAssetAtCell(
                        root,
                        coord,
                        reservedCells,
                        "Wet Ground Corridor",
                        createdCount,
                        ForestWetGroundSalt + 41,
                        ForestGroundScaleMin,
                        ForestGroundScaleMax,
                        1,
                        ref createdCount,
                        domainLimit,
                        primary,
                        settings.forestWetGroundPrefabs,
                        settings.forestRockPrefabs,
                        settings.forestDeadfallPrefabs);
                }
            }
        }

        private void CreateAgriculturalEdgeTransitionDressing(
            Transform root,
            HashSet<GridCoord> reservedCells,
            ref int createdCount,
            int maxInstances)
        {
            int domainLimit = Mathf.Min(maxInstances, createdCount + Mathf.Clamp(maxInstances / 7, 24, 54));
            for (int i = 0; i < plots.Count && createdCount < domainLimit; i++)
            {
                TownPlot plot = plots[i];
                if (plot == null || plot.zone != PlotZone.Agricultural)
                {
                    continue;
                }

                AgriculturalSiteRole role = ResolveAgriculturalSiteRole(plot, TryGetBuildingForPlot(plot, out PlacedBuilding building) ? building : null);
                if (role != AgriculturalSiteRole.CropProductionYard && role != AgriculturalSiteRole.LivestockYard)
                {
                    continue;
                }

                int plotLimit = Mathf.Min(domainLimit, createdCount + (role == AgriculturalSiteRole.LivestockYard ? 22 : 18));
                int transitionWidth = role == AgriculturalSiteRole.LivestockYard ? 5 : 4;
                GridRect area = GetClampedExpandedRect(plot.bounds, transitionWidth);
                for (int z = area.zMin; z <= area.zMaxInclusive && createdCount < plotLimit; z++)
                {
                    for (int x = area.xMin; x <= area.xMaxInclusive && createdCount < plotLimit; x++)
                    {
                        GridCoord coord = new(x, z);
                        int distance = ChebyshevDistanceToRect(coord, plot.bounds);
                        if (distance <= 0
                            || distance > transitionWidth
                            || !IsAgriculturalTransitionCell(plot, coord, transitionWidth))
                        {
                            continue;
                        }

                        bool rear = IsCoordOnSideOfRect(coord, plot.bounds, Opposite(plot.roadFrontageDirection), transitionWidth);
                        float density = role == AgriculturalSiteRole.LivestockYard ? 0.18f : 0.14f;
                        if (rear)
                        {
                            density += role == AgriculturalSiteRole.LivestockYard ? 0.09f : 0.06f;
                        }

                        if (DeterministicUnit01(coord, ForestAgricultureSalt + plot.id) > density
                            || !IsOpenForestEnvironmentCell(coord, reservedCells, 2, 0, 5, 2))
                        {
                            continue;
                        }

                        float roll = DeterministicUnit01(coord, ForestAgricultureSalt + 31 + plot.id);
                        bool canopy = role == AgriculturalSiteRole.LivestockYard && rear && roll < 0.20f;
                        GameObject[] primary = canopy
                            ? settings.forestCanopyTreePrefabs
                            : roll < 0.68f
                                ? settings.forestMeadowPrefabs
                                : settings.forestUnderstoryPrefabs;
                        TryCreateForestEnvironmentAssetAtCell(
                            root,
                            coord,
                            reservedCells,
                            role == AgriculturalSiteRole.LivestockYard ? "Ranch Edge Transition" : "Farm Edge Transition",
                            createdCount,
                            ForestAgricultureSalt + 53 + plot.id,
                            canopy ? ForestTreeScaleMin : ForestUnderstoryScaleMin,
                            canopy ? ForestTreeScaleMax : ForestUnderstoryScaleMax,
                            canopy ? 2 : 1,
                            ref createdCount,
                            plotLimit,
                            primary,
                            settings.forestGroundCoverPrefabs,
                            settings.forestUnderstoryPrefabs);
                    }
                }
            }
        }

        private void CreateMapEdgeForestBandDressing(
            Transform root,
            HashSet<GridCoord> reservedCells,
            ref int createdCount,
            int maxInstances)
        {
            int bandWidth = Mathf.Clamp(settings.mapEdgeForestBandWidthCells, 0, Mathf.Max(grid.Width, grid.Depth));
            if (bandWidth <= 0)
            {
                return;
            }

            int domainLimit = Mathf.Min(maxInstances, createdCount + Mathf.Clamp(maxInstances / 3, 70, 132));
            for (int z = 0; z < grid.Depth && createdCount < domainLimit; z++)
            {
                for (int x = 0; x < grid.Width && createdCount < domainLimit; x++)
                {
                    GridCoord coord = new(x, z);
                    int edgeDistance = Mathf.Min(Mathf.Min(x, grid.Width - 1 - x), Mathf.Min(z, grid.Depth - 1 - z));
                    if (edgeDistance >= bandWidth)
                    {
                        continue;
                    }

                    bool northOrWest = z >= grid.Depth - bandWidth || x < bandWidth;
                    float edgeT = 1f - edgeDistance / (float)Mathf.Max(1, bandWidth);
                    float density = northOrWest
                        ? Mathf.Lerp(0.08f, 0.26f, edgeT)
                        : Mathf.Lerp(0.04f, 0.15f, edgeT);

                    if (DeterministicUnit01(coord, ForestMapEdgeSalt + 19) > density
                        || !IsOpenForestEnvironmentCell(coord, reservedCells, 2, 2, 4, 2))
                    {
                        continue;
                    }

                    float roll = DeterministicUnit01(coord, ForestMapEdgeSalt + 37);
                    bool canopy = roll < (northOrWest ? 0.72f : 0.52f);
                    GameObject[] primary = canopy
                        ? settings.forestCanopyTreePrefabs
                        : roll < 0.86f
                            ? settings.forestUnderstoryPrefabs
                            : settings.forestGroundCoverPrefabs;
                    TryCreateForestEnvironmentAssetAtCell(
                        root,
                        coord,
                        reservedCells,
                        canopy ? "Map Edge Forest Band" : "Map Edge Forest Understory",
                        createdCount,
                        ForestMapEdgeSalt + 61,
                        canopy ? ForestTreeScaleMin : ForestUnderstoryScaleMin,
                        canopy ? ForestTreeScaleMax : ForestUnderstoryScaleMax,
                        canopy ? 2 : 1,
                        ref createdCount,
                        domainLimit,
                        primary,
                        settings.forestUnderstoryPrefabs,
                        settings.forestGroundCoverPrefabs);
                }
            }
        }

        private bool TryCreateForestEnvironmentAssetAtCell(
            Transform root,
            GridCoord coord,
            HashSet<GridCoord> reservedCells,
            string label,
            int itemIndex,
            int salt,
            float minScale,
            float maxScale,
            int minReservedDistance,
            ref int createdCount,
            int maxInstances,
            params GameObject[][] prefabGroups)
        {
            if (createdCount >= maxInstances
                || root == null
                || !grid.IsInBounds(coord)
                || HasReservedForestCellWithin(coord, reservedCells, minReservedDistance)
                || !TryGetForestEnvironmentPrefab(coord, salt + itemIndex, out GameObject prefab, prefabGroups))
            {
                return false;
            }

            Vector3 position = GetCellGroundPosition(coord);
            float yaw = DeterministicUnit01(coord, salt + 211 + itemIndex) * 360f;
            float scale = Mathf.Lerp(
                Mathf.Max(0.01f, minScale),
                Mathf.Max(minScale, maxScale),
                DeterministicUnit01(coord, salt + 227 + itemIndex));
            GameObject visual = CreateGroundAlignedPrefabVisual(
                root,
                prefab,
                $"{ForestEnvironmentVisualPrefix} {label} {itemIndex:000} {coord.x}_{coord.z} {prefab.name}",
                position,
                yaw,
                scale);

            if (visual == null)
            {
                return false;
            }

            SetStaticRecursively(visual);
            reservedCells?.Add(coord);
            createdCount++;
            return true;
        }

        private bool TryGetForestEnvironmentPrefab(GridCoord coord, int salt, out GameObject prefab, params GameObject[][] prefabGroups)
        {
            prefab = null;
            if (prefabGroups == null || prefabGroups.Length == 0)
            {
                return false;
            }

            int start = RandomInclusive(0, prefabGroups.Length - 1, DeterministicUnit01(coord, salt + 17));
            for (int i = 0; i < prefabGroups.Length; i++)
            {
                GameObject[] group = prefabGroups[(start + i) % prefabGroups.Length];
                if (TryGetPrefab(group, DeterministicIndex(coord, salt + i * 23), out prefab))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsValidForestPlotDressingCell(
            TownPlot plot,
            PlacedBuilding building,
            GridCoord coord,
            HashSet<GridCoord> reservedCells,
            int minReservedDistance)
        {
            if (!IsValidDressingCell(plot, building, coord)
                || HasReservedForestCellWithin(coord, reservedCells, minReservedDistance)
                || IsNearAnyPlotAccess(coord, 4))
            {
                return false;
            }

            TownCell cell = grid.GetCell(coord);
            return (cell.occupancy & CellOccupancy.Anchor) == 0;
        }

        private bool IsOpenForestEnvironmentCell(
            GridCoord coord,
            HashSet<GridCoord> reservedCells,
            int roadClearanceCells,
            int plotClearanceCells,
            int accessClearanceCells,
            int minReservedDistance)
        {
            if (grid == null
                || !grid.IsInBounds(coord)
                || HasReservedForestCellWithin(coord, reservedCells, minReservedDistance))
            {
                return false;
            }

            TownCell cell = grid.GetCell(coord);
            if (cell.terrainZone != TerrainZone.Buildable
                || cell.blocked
                || cell.IsRoad
                || cell.HasBuilding
                || (cell.occupancy & (CellOccupancy.Plot | CellOccupancy.Anchor)) != 0)
            {
                return false;
            }

            if (roadClearanceCells > 0 && HasOccupancyWithin(coord, roadClearanceCells, CellOccupancy.Road))
            {
                return false;
            }

            if (plotClearanceCells > 0 && HasOccupancyWithin(coord, plotClearanceCells, CellOccupancy.Plot | CellOccupancy.Building | CellOccupancy.Anchor))
            {
                return false;
            }

            return accessClearanceCells <= 0 || !IsNearAnyPlotAccess(coord, accessClearanceCells);
        }

        private bool TryFindAgriculturalPlot(AgriculturalSiteRole role, out TownPlot plot, out PlacedBuilding building)
        {
            plot = null;
            building = null;
            for (int i = 0; i < plots.Count; i++)
            {
                TownPlot candidatePlot = plots[i];
                if (candidatePlot == null || candidatePlot.zone != PlotZone.Agricultural)
                {
                    continue;
                }

                PlacedBuilding candidateBuilding = TryGetBuildingForPlot(candidatePlot, out PlacedBuilding foundBuilding)
                    ? foundBuilding
                    : null;
                if (ResolveAgriculturalSiteRole(candidatePlot, candidateBuilding) != role)
                {
                    continue;
                }

                plot = candidatePlot;
                building = candidateBuilding;
                return true;
            }

            return false;
        }

        // Plot.buildingId is a persistent world identifier, not a guaranteed list index. Always resolve through
        // TryGetBuilding so save/load changes and future deletions do not silently break runtime linkage reads.
        private bool TryGetBuildingForPlot(TownPlot plot, out PlacedBuilding building)
        {
            building = null;
            return plot != null
                && plot.buildingId >= 0
                && TryGetBuilding(plot.buildingId, out building)
                && building != null;
        }

        private bool IsLikelySawmillApproachRoadCell(TownPlot sawmillPlot, GridCoord roadCoord)
        {
            if (sawmillPlot == null)
            {
                return false;
            }

            int halfRoad = Mathf.Max(0, settings.roadWidthCells / 2);
            int zTolerance = halfRoad + 3;
            if (Mathf.Abs(roadCoord.z - sawmillPlot.roadAccessCell.z) <= zTolerance
                && roadCoord.x <= sawmillPlot.roadAccessCell.x + settings.roadWidthCells)
            {
                return true;
            }

            int centerX = grid.Width / 2;
            return Mathf.Abs(roadCoord.x - centerX) <= halfRoad + 1
                && roadCoord.z >= grid.Depth / 2
                && roadCoord.z <= sawmillPlot.roadAccessCell.z + zTolerance;
        }

        private bool IsRearInteriorCell(TownPlot plot, GridCoord coord)
        {
            if (plot == null || !plot.bounds.Contains(coord))
            {
                return false;
            }

            switch (plot.roadFrontageDirection)
            {
                case GridDirection.North:
                    return coord.z <= plot.bounds.zMin + Mathf.FloorToInt(plot.bounds.depth * 0.48f);
                case GridDirection.East:
                    return coord.x <= plot.bounds.xMin + Mathf.FloorToInt(plot.bounds.width * 0.48f);
                case GridDirection.South:
                    return coord.z >= plot.bounds.zMin + Mathf.CeilToInt(plot.bounds.depth * 0.52f);
                case GridDirection.West:
                    return coord.x >= plot.bounds.xMin + Mathf.CeilToInt(plot.bounds.width * 0.52f);
                default:
                    return false;
            }
        }

        private bool IsRearOrSideExteriorCell(TownPlot plot, GridCoord coord, int padding)
        {
            if (plot == null || plot.bounds.Contains(coord))
            {
                return false;
            }

            return IsCoordOnSideOfRect(coord, plot.bounds, Opposite(plot.roadFrontageDirection), padding)
                || IsCoordOnSideOfRect(coord, plot.bounds, TurnLeft(plot.roadFrontageDirection), padding)
                || IsCoordOnSideOfRect(coord, plot.bounds, TurnRight(plot.roadFrontageDirection), padding);
        }

        private bool IsAgriculturalTransitionCell(TownPlot plot, GridCoord coord, int padding)
        {
            if (plot == null || plot.bounds.Contains(coord))
            {
                return false;
            }

            if (IsCoordOnSideOfRect(coord, plot.bounds, plot.roadFrontageDirection, padding))
            {
                return false;
            }

            return IsRearOrSideExteriorCell(plot, coord, padding);
        }

        private static bool IsCoordOnSideOfRect(GridCoord coord, GridRect rect, GridDirection side, int padding)
        {
            int safePadding = Mathf.Max(0, padding);
            return side switch
            {
                GridDirection.North => coord.z > rect.zMaxInclusive
                    && coord.x >= rect.xMin - safePadding
                    && coord.x <= rect.xMaxInclusive + safePadding,
                GridDirection.East => coord.x > rect.xMaxInclusive
                    && coord.z >= rect.zMin - safePadding
                    && coord.z <= rect.zMaxInclusive + safePadding,
                GridDirection.South => coord.z < rect.zMin
                    && coord.x >= rect.xMin - safePadding
                    && coord.x <= rect.xMaxInclusive + safePadding,
                GridDirection.West => coord.x < rect.xMin
                    && coord.z >= rect.zMin - safePadding
                    && coord.z <= rect.zMaxInclusive + safePadding,
                _ => false
            };
        }

        private GridRect GetClampedExpandedRect(GridRect rect, int expandCells)
        {
            int expand = Mathf.Max(0, expandCells);
            int xMin = Mathf.Max(0, rect.xMin - expand);
            int zMin = Mathf.Max(0, rect.zMin - expand);
            int xMax = Mathf.Min(grid.Width - 1, rect.xMaxInclusive + expand);
            int zMax = Mathf.Min(grid.Depth - 1, rect.zMaxInclusive + expand);
            return new GridRect(xMin, zMin, xMax - xMin + 1, zMax - zMin + 1);
        }

        private static int ChebyshevDistanceToRect(GridCoord coord, GridRect rect)
        {
            int dx = coord.x < rect.xMin
                ? rect.xMin - coord.x
                : coord.x > rect.xMaxInclusive
                    ? coord.x - rect.xMaxInclusive
                    : 0;
            int dz = coord.z < rect.zMin
                ? rect.zMin - coord.z
                : coord.z > rect.zMaxInclusive
                    ? coord.z - rect.zMaxInclusive
                    : 0;
            return Mathf.Max(dx, dz);
        }

        private bool HasOccupancyWithin(GridCoord coord, int radius, CellOccupancy occupancy)
        {
            int safeRadius = Mathf.Max(0, radius);
            for (int z = coord.z - safeRadius; z <= coord.z + safeRadius; z++)
            {
                for (int x = coord.x - safeRadius; x <= coord.x + safeRadius; x++)
                {
                    GridCoord candidate = new(x, z);
                    if (!grid.IsInBounds(candidate) || ManhattanDistance(coord, candidate) > safeRadius)
                    {
                        continue;
                    }

                    if ((grid.GetCell(candidate).occupancy & occupancy) != 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool IsNearAnyPlotAccess(GridCoord coord, int clearanceCells)
        {
            int clearance = Mathf.Max(0, clearanceCells);
            for (int i = 0; i < plots.Count; i++)
            {
                TownPlot plot = plots[i];
                if (plot != null && ManhattanDistance(coord, plot.roadAccessCell) <= clearance)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasReservedForestCellWithin(GridCoord coord, HashSet<GridCoord> reservedCells, int radius)
        {
            if (reservedCells == null || reservedCells.Count == 0)
            {
                return false;
            }

            int safeRadius = Mathf.Max(0, radius);
            foreach (GridCoord reserved in reservedCells)
            {
                if (Mathf.Abs(coord.x - reserved.x) <= safeRadius
                    && Mathf.Abs(coord.z - reserved.z) <= safeRadius)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryGetTerrainHeightRange(out float minHeight, out float maxHeight)
        {
            minHeight = float.MaxValue;
            maxHeight = float.MinValue;
            if (grid == null)
            {
                return false;
            }

            bool initialized = false;
            for (int z = 0; z < grid.Depth; z++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    float height = grid.GetCell(new GridCoord(x, z)).height;
                    if (float.IsNaN(height) || float.IsInfinity(height))
                    {
                        continue;
                    }

                    minHeight = Mathf.Min(minHeight, height);
                    maxHeight = Mathf.Max(maxHeight, height);
                    initialized = true;
                }
            }

            return initialized;
        }

        private int GetWetGroundCorridorCenterX(int z, bool useLowTerrain)
        {
            float seedOffset = (settings != null ? settings.seed : 0) * 0.013f;
            int fallback = Mathf.RoundToInt(
                grid.Width * 0.68f
                + Mathf.Sin(z * 0.17f + seedOffset) * grid.Width * 0.055f);
            fallback = Mathf.Clamp(fallback, 1, Mathf.Max(1, grid.Width - 2));
            if (!useLowTerrain)
            {
                return fallback;
            }

            int minX = Mathf.Clamp(Mathf.RoundToInt(grid.Width * 0.48f), 1, Mathf.Max(1, grid.Width - 2));
            int maxX = Mathf.Clamp(grid.Width - 2, minX, grid.Width - 2);
            int bestX = fallback;
            float bestScore = float.MaxValue;
            for (int x = minX; x <= maxX; x++)
            {
                GridCoord coord = new(x, z);
                if (!grid.IsInBounds(coord))
                {
                    continue;
                }

                TownCell cell = grid.GetCell(coord);
                float score = cell.height + Mathf.Abs(x - fallback) * 0.035f;
                if (cell.terrainZone != TerrainZone.Buildable || cell.blocked || cell.IsRoad || (cell.occupancy & CellOccupancy.Plot) != 0)
                {
                    score += 1000f;
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    bestX = x;
                }
            }

            return Mathf.Clamp(bestX, 1, Mathf.Max(1, grid.Width - 2));
        }

        private Vector3 GetCellGroundPosition(GridCoord coord)
        {
            TownCell cell = grid.GetCell(coord);
            float y = float.IsNaN(cell.height) || float.IsInfinity(cell.height)
                ? settings.worldCenter.y
                : cell.height;
            return grid.CoordToWorldCenter(coord, y);
        }

        private int DeterministicIndex(GridCoord coord, int salt)
        {
            return (int)(DeterministicHash(coord.x, coord.z, salt) & 0x7FFFFFFFu);
        }

        private float DeterministicUnit01(GridCoord coord, int salt)
        {
            return (DeterministicHash(coord.x, coord.z, salt) & 0xFFFFu) / 65535f;
        }

        private uint DeterministicHash(int first, int second, int salt)
        {
            unchecked
            {
                int seed = settings != null ? settings.seed : 0;
                uint hash = (uint)(seed * 73856093)
                    ^ (uint)(first * 19349663)
                    ^ (uint)(second * 83492791)
                    ^ ((uint)salt * 2654435761u);
                hash ^= hash >> 13;
                hash *= 1274126177u;
                hash ^= hash >> 16;
                return hash;
            }
        }

        private static void SetStaticRecursively(GameObject visual)
        {
            if (visual == null)
            {
                return;
            }

            visual.isStatic = true;
            Transform visualTransform = visual.transform;
            for (int i = 0; i < visualTransform.childCount; i++)
            {
                SetStaticRecursively(visualTransform.GetChild(i).gameObject);
            }
        }

        private void CreateCropFarmDressing(
            Transform root,
            TownPlot plot,
            PlacedBuilding building,
            Material cropMaterial,
            Material stackMaterial)
        {
            float cellSize = settings.cellSizeMeters;
            float widthMeters = plot.bounds.width * cellSize;
            float depthMeters = plot.bounds.depth * cellSize;
            float margin = Mathf.Clamp(cellSize * 0.75f, 0.6f, 1.6f);
            float rowWidth = Mathf.Clamp(cellSize * 0.22f, 0.16f, 0.42f);
            int rowCount = Mathf.Clamp(plot.bounds.depth - 2, 3, 7);
            float usableDepth = Mathf.Max(rowWidth, depthMeters - margin * 2f);
            float spacing = rowCount > 1 ? usableDepth / (rowCount - 1) : 0f;
            float xMin = grid.Origin.x + plot.bounds.xMin * cellSize;
            float zMin = grid.Origin.z + plot.bounds.zMin * cellSize;
            float centerX = xMin + widthMeters * 0.5f;
            float startZ = zMin + margin;
            float rowLength = Mathf.Max(cellSize, widthMeters - margin * 2f);
            float jitter = (DeterministicUnit01(plot.id, 11) - 0.5f) * cellSize * 0.25f;
            float y = settings.worldCenter.y + AgricultureVisualYOffset;

            for (int row = 0; row < rowCount; row++)
            {
                float z = startZ + row * spacing;
                CreateWorldBoxVisual(
                    root,
                    $"Agriculture Crop Field Row Plot {plot.id:000} {row:00}",
                    new Vector3(centerX + jitter, y, z),
                    new Vector3(rowLength, AgricultureRowHeight, rowWidth),
                    CropFieldVisualColor,
                    cropMaterial);
            }

            Vector3 stackCenter = new(
                xMin + margin + DeterministicUnit01(plot.id, 21) * Mathf.Max(cellSize, widthMeters - margin * 2f),
                settings.worldCenter.y + AgricultureVisualYOffset + AgriculturePropHeight * 0.5f,
                zMin + depthMeters - margin);
            CreateWorldBoxVisual(
                root,
                $"Agriculture Crop Stack Plot {plot.id:000}",
                stackCenter,
                new Vector3(cellSize * 0.75f, AgriculturePropHeight, cellSize * 0.55f),
                CropStackVisualColor,
                stackMaterial);

            CreateCropFarmFoodAssets(root, plot, building, rowCount);
            CreateCropFarmProduceAssets(root, plot, building);
        }

        private void CreateRanchDressing(
            Transform root,
            TownPlot plot,
            PlacedBuilding building,
            Material fenceMaterial,
            Material feedMaterial)
        {
            float cellSize = settings.cellSizeMeters;
            float widthMeters = plot.bounds.width * cellSize;
            float depthMeters = plot.bounds.depth * cellSize;
            float railWidth = Mathf.Clamp(cellSize * 0.10f, 0.11f, 0.24f);
            float xMin = grid.Origin.x + plot.bounds.xMin * cellSize;
            float zMin = grid.Origin.z + plot.bounds.zMin * cellSize;
            float centerX = xMin + widthMeters * 0.5f;
            float centerZ = zMin + depthMeters * 0.5f;
            float y = settings.worldCenter.y + AgricultureVisualYOffset + AgricultureFenceHeight * 0.5f;
            float sideDepth = Mathf.Max(railWidth, depthMeters - railWidth * 2f);

            CreateWorldBoxVisual(
                root,
                $"Agriculture Ranch Fence Plot {plot.id:000} Front",
                new Vector3(centerX, y, zMin + railWidth * 0.5f),
                new Vector3(widthMeters, AgricultureFenceHeight, railWidth),
                RanchFenceVisualColor,
                fenceMaterial);
            CreateWorldBoxVisual(
                root,
                $"Agriculture Ranch Fence Plot {plot.id:000} Back",
                new Vector3(centerX, y, zMin + depthMeters - railWidth * 0.5f),
                new Vector3(widthMeters, AgricultureFenceHeight, railWidth),
                RanchFenceVisualColor,
                fenceMaterial);
            CreateWorldBoxVisual(
                root,
                $"Agriculture Ranch Fence Plot {plot.id:000} Left",
                new Vector3(xMin + railWidth * 0.5f, y, centerZ),
                new Vector3(railWidth, AgricultureFenceHeight, sideDepth),
                RanchFenceVisualColor,
                fenceMaterial);
            CreateWorldBoxVisual(
                root,
                $"Agriculture Ranch Fence Plot {plot.id:000} Right",
                new Vector3(xMin + widthMeters - railWidth * 0.5f, y, centerZ),
                new Vector3(railWidth, AgricultureFenceHeight, sideDepth),
                RanchFenceVisualColor,
                fenceMaterial);
            CreateWorldBoxVisual(
                root,
                $"Agriculture Ranch Pen Divider Plot {plot.id:000}",
                new Vector3(centerX, y, centerZ),
                new Vector3(widthMeters - railWidth * 2f, AgricultureFenceHeight * 0.8f, railWidth),
                RanchFenceVisualColor,
                fenceMaterial);

            float propY = settings.worldCenter.y + AgricultureVisualYOffset + AgriculturePropHeight * 0.5f;
            float troughX = xMin + widthMeters * (0.28f + DeterministicUnit01(plot.id, 31) * 0.12f);
            float troughZ = zMin + depthMeters * 0.72f;
            CreateWorldBoxVisual(
                root,
                $"Agriculture Ranch Trough Plot {plot.id:000}",
                new Vector3(troughX, propY, troughZ),
                new Vector3(cellSize * 1.1f, AgriculturePropHeight, cellSize * 0.32f),
                RanchFeedVisualColor,
                feedMaterial);
            CreateWorldBoxVisual(
                root,
                $"Agriculture Ranch Hay Stack Plot {plot.id:000}",
                new Vector3(xMin + widthMeters * 0.76f, propY, zMin + depthMeters * 0.27f),
                new Vector3(cellSize * 0.78f, AgriculturePropHeight, cellSize * 0.62f),
                RanchFeedVisualColor,
                feedMaterial);

            CreateRanchAnimalAssets(root, plot, building);
        }

        private void CreateSawmillDressing(
            Transform root,
            TownPlot plot,
            PlacedBuilding building,
            Material logMaterial,
            Material boardMaterial,
            Material treeMaterial,
            Material cabinMaterial)
        {
            float cellSize = settings.cellSizeMeters;
            float widthMeters = plot.bounds.width * cellSize;
            float depthMeters = plot.bounds.depth * cellSize;
            float xMin = grid.Origin.x + plot.bounds.xMin * cellSize;
            float zMin = grid.Origin.z + plot.bounds.zMin * cellSize;
            float propY = settings.worldCenter.y + AgricultureVisualYOffset + SawmillPropHeight * 0.5f;

            CreateWorldBoxVisual(
                root,
                $"{SawmillVisualPrefix} Log Deck Plot {plot.id:000}",
                new Vector3(xMin + widthMeters * 0.28f, propY, zMin + depthMeters * 0.24f),
                new Vector3(cellSize * 4.4f, SawmillPropHeight, cellSize * 0.7f),
                SawmillLogVisualColor,
                logMaterial);
            CreateWorldBoxVisual(
                root,
                $"{SawmillVisualPrefix} Board Stack Plot {plot.id:000}",
                new Vector3(xMin + widthMeters * 0.62f, propY, zMin + depthMeters * 0.24f),
                new Vector3(cellSize * 3.2f, SawmillPropHeight * 0.85f, cellSize * 1.05f),
                SawmillBoardVisualColor,
                boardMaterial);
            CreateWorldBoxVisual(
                root,
                $"{SawmillVisualPrefix} Small Sawmill Main Mill Plot {plot.id:000}",
                new Vector3(xMin + widthMeters * 0.48f, propY + SawmillPropHeight * 0.35f, zMin + depthMeters * 0.34f),
                new Vector3(cellSize * 3.8f, SawmillPropHeight * 1.7f, cellSize * 2.1f),
                SawmillCabinVisualColor,
                cabinMaterial);
            Vector2[] workerHouseFractions =
            {
                new(0.80f, 0.40f),
                new(0.80f, 0.53f),
                new(0.67f, 0.47f)
            };
            for (int i = 0; i < workerHouseFractions.Length; i++)
            {
                Vector2 fraction = workerHouseFractions[i];
                CreateWorldBoxVisual(
                    root,
                    $"{SawmillVisualPrefix} Worker House {i + 1} Plot {plot.id:000}",
                    new Vector3(xMin + widthMeters * fraction.x, propY + SawmillPropHeight * 0.18f, zMin + depthMeters * fraction.y),
                    new Vector3(cellSize * 2.3f, SawmillPropHeight * 1.35f, cellSize * 2.0f),
                    SawmillCabinVisualColor,
                    cabinMaterial);
            }

            if (ShouldGenerateForestEnvironmentDressing())
            {
                return;
            }

            int stumpCount = Mathf.Clamp(plot.bounds.Area / 130, 4, 8);
            for (int i = 0; i < stumpCount; i++)
            {
                if (!TryGetDressingCell(plot, building, 520 + i, false, out GridCoord coord))
                {
                    continue;
                }

                Vector3 center = grid.CoordToWorldCenter(coord, settings.worldCenter.y + AgricultureVisualYOffset + SawmillPropHeight * 0.18f);
                CreateWorldBoxVisual(
                    root,
                    $"{SawmillVisualPrefix} Stump Plot {plot.id:000} {i:00}",
                    center,
                    new Vector3(cellSize * 0.34f, SawmillPropHeight * 0.36f, cellSize * 0.34f),
                    SawmillLogVisualColor,
                    logMaterial);
            }

            int treeCount = Mathf.Clamp(plot.bounds.Area / 70, 16, 24);
            for (int i = 0; i < treeCount; i++)
            {
                if (!TryGetDressingCell(plot, building, 620 + i, true, out GridCoord coord))
                {
                    continue;
                }

                Vector3 center = grid.CoordToWorldCenter(coord, settings.worldCenter.y + AgricultureVisualYOffset + SawmillTreeHeight * 0.5f);
                float scale = Mathf.Lerp(0.82f, 1.18f, DeterministicUnit01(plot.id, 650 + i));
                CreateWorldBoxVisual(
                    root,
                    $"{SawmillVisualPrefix} Placeholder Tree Block Plot {plot.id:000} {i:00}",
                    center,
                    new Vector3(cellSize * 0.52f * scale, SawmillTreeHeight * scale, cellSize * 0.52f * scale),
                    SawmillTreeVisualColor,
                    treeMaterial);
            }
        }

        private void CreateCropFarmFoodAssets(Transform root, TownPlot plot, PlacedBuilding building, int rowCount)
        {
            if (!TryGetPrefab(settings.cropFarmFoodPrefabs, 0, out _))
            {
                return;
            }

            int instanceCount = Mathf.Clamp(plot.bounds.Area / 28, 6, CropFarmFoodPrefabBudget);
            for (int i = 0; i < instanceCount; i++)
            {
                if (!TryGetPrefab(settings.cropFarmFoodPrefabs, i, out GameObject prefab))
                {
                    return;
                }

                int row = rowCount > 0 ? i % rowCount : 0;
                if (!TryGetCropRowDressingCell(plot, building, row, rowCount, 100 + i, out GridCoord coord))
                {
                    continue;
                }

                Vector3 position = grid.CoordToWorldCenter(coord, settings.worldCenter.y);
                float yaw = DeterministicUnit01(plot.id, 210 + i) * 360f;
                float scale = AgricultureFoodPrefabScale * Mathf.Lerp(0.86f, 1.14f, DeterministicUnit01(plot.id, 230 + i));
                CreateAgriculturalPrefabVisual(
                    root,
                    prefab,
                    $"{AgricultureFoodVisualPrefix} Plot {plot.id:000} {i:00} {prefab.name}",
                    position,
                    yaw,
                    scale);
            }
        }

        private void CreateCropFarmProduceAssets(Transform root, TownPlot plot, PlacedBuilding building)
        {
            if (!TryGetPrefab(settings.cropFarmProduceStackPrefabs, 0, out _))
            {
                return;
            }

            int instanceCount = Mathf.Clamp(plot.bounds.Area / 120, 2, CropFarmProducePrefabBudget);
            for (int i = 0; i < instanceCount; i++)
            {
                if (!TryGetPrefab(settings.cropFarmProduceStackPrefabs, i, out GameObject prefab))
                {
                    return;
                }

                if (!TryGetDressingCell(plot, building, 300 + i, true, out GridCoord coord))
                {
                    continue;
                }

                Vector3 position = grid.CoordToWorldCenter(coord, settings.worldCenter.y);
                float yaw = DeterministicUnit01(plot.id, 320 + i) * 360f;
                float scale = AgricultureProducePrefabScale * Mathf.Lerp(0.92f, 1.25f, DeterministicUnit01(plot.id, 330 + i));
                CreateAgriculturalPrefabVisual(
                    root,
                    prefab,
                    $"{AgricultureProduceVisualPrefix} Plot {plot.id:000} {i:00} {prefab.name}",
                    position,
                    yaw,
                    scale);
            }
        }

        private void CreateRanchAnimalAssets(Transform root, TownPlot plot, PlacedBuilding building)
        {
            if (!TryGetPrefab(settings.ranchAnimalPrefabs, 0, out _))
            {
                return;
            }

            int instanceCount = Mathf.Clamp(plot.bounds.Area / 110, 3, RanchAnimalPrefabBudget);
            for (int i = 0; i < instanceCount; i++)
            {
                if (!TryGetPrefab(settings.ranchAnimalPrefabs, i, out GameObject prefab))
                {
                    return;
                }

                if (!TryGetDressingCell(plot, building, 400 + i, false, out GridCoord coord))
                {
                    continue;
                }

                Vector3 position = grid.CoordToWorldCenter(coord, settings.worldCenter.y);
                float yaw = DeterministicUnit01(plot.id, 420 + i) * 360f;
                float scale = AgricultureAnimalPrefabScale * Mathf.Lerp(0.86f, 1.08f, DeterministicUnit01(plot.id, 430 + i));
                CreateAgriculturalPrefabVisual(
                    root,
                    prefab,
                    $"{AgricultureAnimalVisualPrefix} Plot {plot.id:000} {i:00} {prefab.name}",
                    position,
                    yaw,
                    scale);
            }
        }

        private void BuildDetailPropVisuals(Transform root)
        {
            if (root == null
                || settings == null
                || grid == null
                || !settings.generateDetailProps
                || !HasAnyDetailPropPrefabs())
            {
                return;
            }

            HashSet<GridCoord> reservedDetailCells = new();
            for (int i = 0; i < buildings.Count; i++)
            {
                PlacedBuilding building = buildings[i];
                if (building == null || building.definition == null)
                {
                    continue;
                }

                TownPlot plot = GetPlotById(building.plotId);
                if (plot == null)
                {
                    continue;
                }

                if (plot.zone == PlotZone.Agricultural)
                {
                    CreateAgriculturalDetailProps(root, plot, building, reservedDetailCells);
                }
            }
        }

        private void CreateAgriculturalDetailProps(Transform root, TownPlot plot, PlacedBuilding building, HashSet<GridCoord> reservedDetailCells)
        {
            switch (ResolveAgriculturalSiteRole(plot, building))
            {
                case AgriculturalSiteRole.CropProductionYard:
                    {
                        int count = 2 + (DeterministicUnit01(plot.id, DetailCropFarmSalt) < 0.5f ? 1 : 0);
                        for (int i = 0; i < count; i++)
                        {
                            int salt = DetailCropFarmSalt + i * 13;
                            if (!TryGetDetailDressingCell(plot, building, salt, true, reservedDetailCells, out GridCoord coord))
                            {
                                continue;
                            }

                            TryCreateDetailPropAtCell(
                                root,
                                plot,
                                $"Crop Farm Support Plot {plot.id:000}",
                                i,
                                salt,
                                0.95f,
                                reservedDetailCells,
                                coord,
                                settings.farmSupportPropPrefabs,
                                settings.cratePropPrefabs,
                                settings.barrelPropPrefabs,
                                settings.smallGoodsPropPrefabs);
                        }

                        break;
                    }
                case AgriculturalSiteRole.LivestockYard:
                    {
                        int count = 2 + (DeterministicUnit01(plot.id, DetailRanchSalt) < 0.5f ? 1 : 0);
                        for (int i = 0; i < count; i++)
                        {
                            int salt = DetailRanchSalt + i * 13;
                            if (!TryGetDetailDressingCell(plot, building, salt, i % 2 == 0, reservedDetailCells, out GridCoord coord))
                            {
                                continue;
                            }

                            TryCreateDetailPropAtCell(
                                root,
                                plot,
                                $"Ranch Support Plot {plot.id:000}",
                                i,
                                salt,
                                0.96f,
                                reservedDetailCells,
                                coord,
                                settings.ranchSupportPropPrefabs,
                                settings.smallGoodsPropPrefabs,
                                settings.barrelPropPrefabs,
                                settings.cratePropPrefabs);
                        }

                        break;
                    }
                case AgriculturalSiteRole.SawmillYard:
                    {
                        int count = 3 + RandomInclusive(0, 2, DeterministicUnit01(plot.id, DetailSawmillSalt));
                        for (int i = 0; i < count; i++)
                        {
                            int salt = DetailSawmillSalt + i * 17;
                            if (!TryGetDetailDressingCell(plot, building, salt, i % 3 == 0, reservedDetailCells, out GridCoord coord))
                            {
                                continue;
                            }

                            TryCreateDetailPropAtCell(
                                root,
                                plot,
                                $"Sawmill Support Plot {plot.id:000}",
                                i,
                                salt,
                                0.98f,
                                reservedDetailCells,
                                coord,
                                settings.sawmillSupportPropPrefabs,
                                settings.cratePropPrefabs,
                                settings.barrelPropPrefabs,
                                settings.smallGoodsPropPrefabs);
                        }

                        break;
                    }
            }
        }

        private bool TryGetAnchorAdjacentDressingCell(
            TownPlot plot,
            PlacedBuilding building,
            AnchorType anchorType,
            int salt,
            HashSet<GridCoord> reservedDetailCells,
            out GridCoord coord)
        {
            coord = default;
            if (plot == null || building == null)
            {
                return false;
            }

            if (building.TryGetAnchor(anchorType, out BuildingAnchor anchor))
            {
                for (int attempt = 0; attempt < 24; attempt++)
                {
                    int xOffset = RandomInclusive(-2, 2, DeterministicUnit01(plot.id, salt + attempt * 17));
                    int zOffset = RandomInclusive(-2, 2, DeterministicUnit01(plot.id, salt + attempt * 19 + 5));
                    if (xOffset == 0 && zOffset == 0)
                    {
                        continue;
                    }

                    coord = new GridCoord(anchor.coord.x + xOffset, anchor.coord.z + zOffset);
                    if (IsValidDetailPropCell(plot, building, coord, reservedDetailCells))
                    {
                        return true;
                    }
                }

                GridCoord[] fallbackOffsets =
                {
                    new(1, 0),
                    new(-1, 0),
                    new(0, 1),
                    new(0, -1),
                    new(1, 1),
                    new(-1, 1),
                    new(1, -1),
                    new(-1, -1)
                };

                int start = RandomInclusive(0, fallbackOffsets.Length - 1, DeterministicUnit01(plot.id, salt + 101));
                for (int i = 0; i < fallbackOffsets.Length; i++)
                {
                    GridCoord offset = fallbackOffsets[(start + i) % fallbackOffsets.Length];
                    coord = anchor.coord + offset;
                    if (IsValidDetailPropCell(plot, building, coord, reservedDetailCells))
                    {
                        return true;
                    }
                }
            }

            return TryGetDetailDressingCell(plot, building, salt + 131, anchorType == AnchorType.Service, reservedDetailCells, out coord);
        }

        private bool TryGetDetailDressingCell(
            TownPlot plot,
            PlacedBuilding building,
            int salt,
            bool preferRear,
            HashSet<GridCoord> reservedDetailCells,
            out GridCoord coord)
        {
            coord = default;
            for (int attempt = 0; attempt < 10; attempt++)
            {
                if (TryGetDressingCell(plot, building, salt + attempt * 41, preferRear, out GridCoord candidate)
                    && IsValidDetailPropCell(plot, building, candidate, reservedDetailCells))
                {
                    coord = candidate;
                    return true;
                }
            }

            if (!TryGetInteriorBounds(plot, out int xMin, out int xMax, out int zMin, out int zMax))
            {
                return false;
            }

            for (int z = zMin; z <= zMax; z++)
            {
                for (int x = xMin; x <= xMax; x++)
                {
                    GridCoord candidate = new(x, z);
                    if (IsValidDetailPropCell(plot, building, candidate, reservedDetailCells))
                    {
                        coord = candidate;
                        return true;
                    }
                }
            }

            return false;
        }

        private bool IsValidDetailPropCell(TownPlot plot, PlacedBuilding building, GridCoord coord, HashSet<GridCoord> reservedDetailCells)
        {
            if (!IsValidDressingCell(plot, building, coord))
            {
                return false;
            }

            if (reservedDetailCells != null && reservedDetailCells.Contains(coord))
            {
                return false;
            }

            TownCell cell = grid.GetCell(coord);
            return (cell.occupancy & CellOccupancy.Anchor) == 0;
        }

        private void TryCreateDetailPropAtCell(
            Transform root,
            TownPlot plot,
            string label,
            int itemIndex,
            int salt,
            float baseScale,
            HashSet<GridCoord> reservedDetailCells,
            GridCoord coord,
            params GameObject[][] prefabGroups)
        {
            if (plot == null
                || !TryGetDetailPropPrefab(plot.id, salt + itemIndex, out GameObject prefab, prefabGroups))
            {
                return;
            }

            Vector3 position = grid.CoordToWorldCenter(coord, settings.worldCenter.y);
            float yaw = DeterministicUnit01(plot.id, salt + 211 + itemIndex) * 360f;
            float scale = Mathf.Max(0.01f, baseScale) * Mathf.Lerp(
                DetailPropScaleMin,
                DetailPropScaleMax,
                DeterministicUnit01(plot.id, salt + 227 + itemIndex));
            GameObject visual = CreateDetailPropVisual(
                root,
                prefab,
                $"{DetailPropVisualPrefix} {label} {itemIndex:00} {prefab.name}",
                position,
                yaw,
                scale);

            if (visual != null && reservedDetailCells != null)
            {
                reservedDetailCells.Add(coord);
            }
        }

        private GameObject CreateDetailPropVisual(
            Transform root,
            GameObject prefab,
            string objectName,
            Vector3 position,
            float yawDegrees,
            float scale)
        {
            return CreateGroundAlignedPrefabVisual(root, prefab, objectName, position, yawDegrees, scale);
        }

        private bool TryGetDetailPropPrefab(int plotId, int salt, out GameObject prefab, params GameObject[][] prefabGroups)
        {
            prefab = null;
            if (prefabGroups == null || prefabGroups.Length == 0)
            {
                return false;
            }

            int start = RandomInclusive(0, prefabGroups.Length - 1, DeterministicUnit01(plotId, salt + 17));
            for (int i = 0; i < prefabGroups.Length; i++)
            {
                GameObject[] group = prefabGroups[(start + i) % prefabGroups.Length];
                if (TryGetPrefab(group, salt + i, out prefab))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasAnyDetailPropPrefabs()
        {
            return HasAnyPrefab(settings.barrelPropPrefabs)
                || HasAnyPrefab(settings.cratePropPrefabs)
                || HasAnyPrefab(settings.smallGoodsPropPrefabs)
                || HasAnyPrefab(settings.farmSupportPropPrefabs)
                || HasAnyPrefab(settings.ranchSupportPropPrefabs)
                || HasAnyPrefab(settings.sawmillSupportPropPrefabs);
        }

        private static bool HasAnyPrefab(GameObject[] prefabs)
        {
            if (prefabs == null)
            {
                return false;
            }

            for (int i = 0; i < prefabs.Length; i++)
            {
                if (prefabs[i] != null)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryGetCropRowDressingCell(
            TownPlot plot,
            PlacedBuilding building,
            int row,
            int rowCount,
            int salt,
            out GridCoord coord)
        {
            coord = default;
            if (!TryGetInteriorBounds(plot, out int xMin, out int xMax, out int zMin, out int zMax))
            {
                return false;
            }

            float rowT = rowCount > 1 ? Mathf.Clamp01(row / (float)(rowCount - 1)) : 0.5f;
            int rowZ = Mathf.RoundToInt(Mathf.Lerp(zMin, zMax, rowT));
            for (int attempt = 0; attempt < 18; attempt++)
            {
                int x = RandomInclusive(xMin, xMax, DeterministicUnit01(plot.id, salt + attempt * 17));
                int zJitter = Mathf.RoundToInt((DeterministicUnit01(plot.id, salt + attempt * 19 + 3) - 0.5f) * 2f);
                coord = new GridCoord(x, Mathf.Clamp(rowZ + zJitter, zMin, zMax));
                if (IsValidDressingCell(plot, building, coord))
                {
                    return true;
                }
            }

            return TryGetDressingCell(plot, building, salt + 71, false, out coord);
        }

        private bool TryGetDressingCell(
            TownPlot plot,
            PlacedBuilding building,
            int salt,
            bool preferRear,
            out GridCoord coord)
        {
            coord = default;
            if (!TryGetInteriorBounds(plot, out int xMin, out int xMax, out int zMin, out int zMax))
            {
                return false;
            }

            for (int attempt = 0; attempt < 32; attempt++)
            {
                float xSample = DeterministicUnit01(plot.id, salt + attempt * 23);
                float zSample = DeterministicUnit01(plot.id, salt + attempt * 29 + 5);
                coord = preferRear
                    ? GetRearBiasedCell(plot, xMin, xMax, zMin, zMax, xSample, zSample)
                    : new GridCoord(RandomInclusive(xMin, xMax, xSample), RandomInclusive(zMin, zMax, zSample));

                if (IsValidDressingCell(plot, building, coord))
                {
                    return true;
                }
            }

            for (int z = zMin; z <= zMax; z++)
            {
                for (int x = xMin; x <= xMax; x++)
                {
                    coord = new GridCoord(x, z);
                    if (IsValidDressingCell(plot, building, coord))
                    {
                        return true;
                    }
                }
            }

            coord = default;
            return false;
        }

        private bool TryGetInteriorBounds(TownPlot plot, out int xMin, out int xMax, out int zMin, out int zMax)
        {
            xMin = xMax = zMin = zMax = 0;
            if (plot == null || !plot.bounds.IsValid)
            {
                return false;
            }

            xMin = plot.bounds.xMin;
            xMax = plot.bounds.xMaxInclusive;
            zMin = plot.bounds.zMin;
            zMax = plot.bounds.zMaxInclusive;

            if (plot.bounds.width > 2)
            {
                xMin++;
                xMax--;
            }

            if (plot.bounds.depth > 2)
            {
                zMin++;
                zMax--;
            }

            return xMin <= xMax && zMin <= zMax;
        }

        private GridCoord GetRearBiasedCell(
            TownPlot plot,
            int xMin,
            int xMax,
            int zMin,
            int zMax,
            float xSample,
            float zSample)
        {
            int x = RandomInclusive(xMin, xMax, xSample);
            int z = RandomInclusive(zMin, zMax, zSample);
            int rearWidth = Mathf.Max(1, Mathf.CeilToInt((xMax - xMin + 1) * 0.38f));
            int rearDepth = Mathf.Max(1, Mathf.CeilToInt((zMax - zMin + 1) * 0.38f));

            switch (plot.roadFrontageDirection)
            {
                case GridDirection.East:
                    x = RandomInclusive(xMin, xMin + rearWidth - 1, xSample);
                    break;
                case GridDirection.West:
                    x = RandomInclusive(xMax - rearWidth + 1, xMax, xSample);
                    break;
                case GridDirection.North:
                    z = RandomInclusive(zMin, zMin + rearDepth - 1, zSample);
                    break;
                case GridDirection.South:
                    z = RandomInclusive(zMax - rearDepth + 1, zMax, zSample);
                    break;
            }

            return new GridCoord(x, z);
        }

        private bool IsValidDressingCell(TownPlot plot, PlacedBuilding building, GridCoord coord)
        {
            if (grid == null || plot == null || !plot.bounds.Contains(coord) || !grid.IsInBounds(coord))
            {
                return false;
            }

            if (building != null && building.footprint.Contains(coord))
            {
                return false;
            }

            if (ManhattanDistance(coord, plot.roadAccessCell) <= 2)
            {
                return false;
            }

            TownCell cell = grid.GetCell(coord);
            return !cell.IsRoad
                && !cell.HasBuilding
                && !cell.blocked
                && (cell.occupancy & CellOccupancy.Plot) != 0;
        }

        private GameObject CreateAgriculturalPrefabVisual(
            Transform root,
            GameObject prefab,
            string objectName,
            Vector3 position,
            float yawDegrees,
            float scale)
        {
            return CreateGroundAlignedPrefabVisual(root, prefab, objectName, position, yawDegrees, scale);
        }

        private GameObject CreateGroundAlignedPrefabVisual(
            Transform root,
            GameObject prefab,
            string objectName,
            Vector3 position,
            float yawDegrees,
            float scale)
        {
            GameObject visual = TryInstantiateGameObject(prefab, root, objectName);
            if (visual == null)
            {
                return null;
            }

            visual.name = objectName;
            visual.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yawDegrees, 0f));
            Vector3 baseScale = visual.transform.localScale;
            visual.transform.localScale = new Vector3(baseScale.x * scale, baseScale.y * scale, baseScale.z * scale);
            DisablePrefabColliders(visual);
            AlignPrefabToGroundCell(visual, position);
            return visual;
        }

        private static bool TryGetPrefab(GameObject[] prefabs, int index, out GameObject prefab)
        {
            prefab = null;
            if (prefabs == null || prefabs.Length == 0)
            {
                return false;
            }

            int start = Mathf.Abs(index) % prefabs.Length;
            for (int i = 0; i < prefabs.Length; i++)
            {
                GameObject candidate = prefabs[(start + i) % prefabs.Length];
                if (candidate != null)
                {
                    prefab = candidate;
                    return true;
                }
            }

            return false;
        }

        private static void DisablePrefabColliders(GameObject visual)
        {
            Collider[] colliders = visual.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] == null)
                {
                    continue;
                }

                if (colliders[i] is BoxCollider)
                {
                    DestroyUnityObject(colliders[i]);
                    continue;
                }

                colliders[i].enabled = false;
            }
        }

        private static void AlignPrefabToGroundCell(GameObject visual, Vector3 targetPosition)
        {
            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                return;
            }

            bool initialized = false;
            Bounds bounds = default;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null)
                {
                    continue;
                }

                if (!initialized)
                {
                    bounds = renderers[i].bounds;
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
            }

            if (!initialized)
            {
                return;
            }

            visual.transform.position += new Vector3(
                targetPosition.x - bounds.center.x,
                targetPosition.y - bounds.min.y,
                targetPosition.z - bounds.center.z);
        }

        private static int RandomInclusive(int min, int max, float sample)
        {
            if (min >= max)
            {
                return min;
            }

            return Mathf.Clamp(min + Mathf.FloorToInt(Mathf.Clamp01(sample) * (max - min + 1)), min, max);
        }

        private static int ManhattanDistance(GridCoord left, GridCoord right)
        {
            return Mathf.Abs(left.x - right.x) + Mathf.Abs(left.z - right.z);
        }

        private float DeterministicUnit01(int plotId, int salt)
        {
            unchecked
            {
                int seed = settings != null ? settings.seed : 0;
                uint hash = (uint)(seed * 73856093) ^ (uint)(plotId * 19349663) ^ (uint)(salt * 83492791);
                hash ^= hash >> 13;
                hash *= 1274126177u;
                hash ^= hash >> 16;
                return (hash & 0xFFFFu) / 65535f;
            }
        }

        private void CreatePlotPerimeterVisual(Transform root, TownPlot plot, Material material)
        {
            if (plot == null || !plot.bounds.IsValid || grid == null)
            {
                return;
            }

            float cellSize = settings.cellSizeMeters;
            float lineWidth = Mathf.Clamp(cellSize * 0.12f, 0.14f, 0.32f);
            float widthMeters = plot.bounds.width * cellSize;
            float depthMeters = plot.bounds.depth * cellSize;
            float y = settings.worldCenter.y + PlotBoundaryYOffset;
            float xMin = grid.Origin.x + plot.bounds.xMin * cellSize;
            float zMin = grid.Origin.z + plot.bounds.zMin * cellSize;
            float centerX = xMin + widthMeters * 0.5f;
            float centerZ = zMin + depthMeters * 0.5f;
            float sideDepth = Mathf.Max(lineWidth, depthMeters - lineWidth * 2f);

            CreateWorldBoxVisual(
                root,
                $"Plot {plot.id:000} Front Boundary",
                new Vector3(centerX, y, zMin + lineWidth * 0.5f),
                new Vector3(widthMeters, PlotBoundaryHeight, lineWidth),
                settings.plotColor,
                material);

            CreateWorldBoxVisual(
                root,
                $"Plot {plot.id:000} Back Boundary",
                new Vector3(centerX, y, zMin + depthMeters - lineWidth * 0.5f),
                new Vector3(widthMeters, PlotBoundaryHeight, lineWidth),
                settings.plotColor,
                material);

            CreateWorldBoxVisual(
                root,
                $"Plot {plot.id:000} Left Boundary",
                new Vector3(xMin + lineWidth * 0.5f, y, centerZ),
                new Vector3(lineWidth, PlotBoundaryHeight, sideDepth),
                settings.plotColor,
                material);

            CreateWorldBoxVisual(
                root,
                $"Plot {plot.id:000} Right Boundary",
                new Vector3(xMin + widthMeters - lineWidth * 0.5f, y, centerZ),
                new Vector3(lineWidth, PlotBoundaryHeight, sideDepth),
                settings.plotColor,
                material);
        }

        private void BuildRoadVisuals(Transform root)
        {
            Material roadMaterial = GetRoadVisualMaterial();
            for (int z = 0; z < grid.Depth; z++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    GridCoord coord = new(x, z);
                    if (!grid.GetCell(coord).IsRoad)
                    {
                        continue;
                    }

                    GridRect roadRect = new(x, z, 1, 1);
                    GameObject roadVisual = CreateRectVisual(
                        root,
                        $"Road Cell {x}_{z}",
                        roadRect,
                        RoadVisualYOffset,
                        RoadVisualHeight,
                        settings.roadColor,
                        roadMaterial);
                    ApplyRoadTextureTiling(roadVisual, roadRect);
                }
            }
        }

        private GridRect FindUnconsumedRoadRect(int startX, int startZ, bool[,] consumed)
        {
            int width = 0;
            while (startX + width < grid.Width
                && !consumed[startX + width, startZ]
                && grid.GetCell(new GridCoord(startX + width, startZ)).IsRoad)
            {
                width++;
            }

            int depth = 1;
            while (startZ + depth < grid.Depth && RoadRowCanExtend(startX, startZ + depth, width, consumed))
            {
                depth++;
            }

            return new GridRect(startX, startZ, width, depth);
        }

        private bool RoadRowCanExtend(int startX, int z, int width, bool[,] consumed)
        {
            for (int xOffset = 0; xOffset < width; xOffset++)
            {
                int x = startX + xOffset;
                if (consumed[x, z] || !grid.GetCell(new GridCoord(x, z)).IsRoad)
                {
                    return false;
                }
            }

            return true;
        }

        private static void MarkConsumed(GridRect rect, bool[,] consumed)
        {
            for (int z = rect.zMin; z <= rect.zMaxInclusive; z++)
            {
                for (int x = rect.xMin; x <= rect.xMaxInclusive; x++)
                {
                    consumed[x, z] = true;
                }
            }
        }

        private void ApplyRoadTextureTiling(GameObject roadVisual, GridRect rect)
        {
            if (roadVisual == null || settings == null)
            {
                return;
            }

            Renderer renderer = roadVisual.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            float tileMeters = Mathf.Max(0.25f, RoadTextureTileMeters);
            Vector4 scaleOffset = new(
                Mathf.Max(1f, rect.width * settings.cellSizeMeters / tileMeters),
                Mathf.Max(1f, rect.depth * settings.cellSizeMeters / tileMeters),
                0f,
                0f);

            MaterialPropertyBlock block = new();
            renderer.GetPropertyBlock(block);
            block.SetVector(BaseColorMapScaleOffsetId, scaleOffset);
            block.SetVector(MainTexScaleOffsetId, scaleOffset);
            block.SetVector(MaskMapScaleOffsetId, scaleOffset);
            block.SetVector(NormalMapScaleOffsetId, scaleOffset);
            block.SetVector(BumpMapScaleOffsetId, scaleOffset);
            block.SetVector(HeightMapScaleOffsetId, scaleOffset);
            block.SetVector(OcclusionMapScaleOffsetId, scaleOffset);
            block.SetVector(ParallaxMapScaleOffsetId, scaleOffset);
            renderer.SetPropertyBlock(block);
        }

        private Material GetRoadVisualMaterial()
        {
            if (IsUsableMaterial(settings.roadMaterial))
            {
                return settings.roadMaterial;
            }

            if (settings.roadMaterial != null)
            {
                Debug.LogWarning($"Road material '{settings.roadMaterial.name}' is missing a supported shader. Falling back to generated road debug material.", settings.roadMaterial);
            }
            else
            {
                Debug.LogWarning("Town road material is not assigned. Falling back to generated road debug material.", this);
            }

            return null;
        }

        private void CreateBuildingVisual(Transform root, PlacedBuilding building)
        {
            if (building.definition != null && building.definition.VisualPrefab != null)
            {
                if (CreatePrefabBuildingVisual(root, building))
                {
                    return;
                }
            }

            CreateGreyboxBuildingVisual(root, building);
        }

        private void CreateGreyboxBuildingVisual(Transform root, PlacedBuilding building)
        {
            float height = building.definition != null ? building.definition.GreyboxHeightMeters : 4f;
            Color color = building.definition != null ? building.definition.GreyboxColor : settings.footprintColor;
            string displayName = building.definition != null ? building.definition.DisplayName : "Unknown";
            CreateRectVisual(root, $"Building {building.id:000} {displayName}", building.footprint, height * 0.5f, height, color);
        }

        private bool CreatePrefabBuildingVisual(Transform root, PlacedBuilding building)
        {
            BuildingDefinition definition = building.definition;
            GameObject visual = TryInstantiateGameObject(definition.VisualPrefab, root, $"building {building.id:000} {definition.DisplayName}");
            if (visual == null)
            {
                return false;
            }

            // Generated building interaction is handled by authored anchors and proxies;
            // disabling prefab colliders prevents nested/negative-scale art colliders from
            // fighting the generated terrain collider or spamming BoxCollider warnings.
            DisablePrefabColliders(visual);
            visual.name = $"Building {building.id:000} {definition.DisplayName}";

            if (!TryResolvePrefabPlacementPose(building, visual, out PrefabPlacementPose pose))
            {
                DestroyUnityObject(visual);
                return false;
            }

            visual.transform.SetPositionAndRotation(pose.rootWorldPosition, pose.rootWorldRotation);
            visual.transform.localScale = pose.rootWorldScale;

            HideRuntimeAnchorAuthoringHelpers(visual);
            if (ShouldRunAutomaticBuildingVisualRepair() && !EnsurePrefabBuildingVisualMeshesVisible(visual, building))
            {
                DestroyUnityObject(visual);
                return false;
            }

            building.doorAnimator = EnsureBuildingDoorAnimator(visual);
            ActivateAuthoredExteriorProps(visual, building);
            if (ShouldRunAutomaticBuildingVisualRepair())
            {
                EnsurePrefabBuildingVisualMeshesVisible(visual, building);
            }

            AlignBuildingVisualToFootprintGround(visual, building);

            return true;
        }

        private void AlignBuildingVisualToFootprintGround(GameObject visual, PlacedBuilding building)
        {
            if (visual == null
                || building == null
                || !TryResolveFootprintGroundHeight(building.footprint, true, out float groundHeight, out _)
                || !TryResolveVisualBottomOffsetFromRoot(visual.transform, out float bottomOffset))
            {
                return;
            }

            float authoredVerticalOffset = building.definition != null
                ? (visual.transform.rotation * building.definition.VisualPositionOffset).y
                : 0f;
            Vector3 position = visual.transform.position;
            visual.transform.position = new Vector3(
                position.x,
                groundHeight - bottomOffset + authoredVerticalOffset + Mathf.Max(0f, runtimeTerrainVisualSurfaceOffsetMeters),
                position.z);
        }

        private bool EnsurePrefabBuildingVisualMeshesVisible(GameObject visual, PlacedBuilding building)
        {
            if (visual == null)
            {
                return false;
            }

            visual.SetActive(true);
            NormalizeRuntimeLodGroups(visual);
            ForceRuntimeLodZero(visual);

            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
            List<Renderer> candidates = new();
            bool hasLodZeroCandidate = false;
            bool hasVisibleRenderer = false;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (!IsRuntimeBuildingMeshRenderer(renderer))
                {
                    continue;
                }

                candidates.Add(renderer);
                hasLodZeroCandidate |= IsLodZeroRenderer(renderer.transform);
                if (IsRendererCurrentlyVisible(renderer))
                {
                    hasVisibleRenderer = true;
                }
            }

            if (hasVisibleRenderer)
            {
                ForceRuntimeLodZero(visual);
                for (int i = 0; i < candidates.Count; i++)
                {
                    Renderer renderer = candidates[i];
                    if (renderer == null)
                    {
                        continue;
                    }

                    renderer.enabled = true;
                    renderer.forceRenderingOff = false;
                    if (renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly)
                    {
                        renderer.shadowCastingMode = ShadowCastingMode.On;
                    }

                    renderer.allowOcclusionWhenDynamic = false;
                    EnsureRendererHasVisibleMaterial(renderer);
                }

                return true;
            }

            int repairedCount = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                Renderer renderer = candidates[i];
                if (renderer == null)
                {
                    continue;
                }

                if (hasLodZeroCandidate && !IsLodZeroRenderer(renderer.transform))
                {
                    continue;
                }

                ActivateTransformChain(visual.transform, renderer.transform);
                renderer.enabled = true;
                renderer.forceRenderingOff = false;
                if (renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly)
                {
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                }

                renderer.allowOcclusionWhenDynamic = false;
                EnsureRendererHasVisibleMaterial(renderer);
                repairedCount++;
            }

            if (repairedCount > 0)
            {
                ForceRuntimeLodZero(visual);
                return true;
            }

            string label = building != null && building.definition != null
                ? building.definition.DisplayName
                : visual.name;
            Debug.LogWarning($"Building visual '{label}' was instantiated but no usable mesh renderer was found. Falling back to greybox so the generated town remains visible.", visual);
            return false;
        }

        private static void NormalizeRuntimeLodGroups(GameObject visual)
        {
            LODGroup[] lodGroups = visual.GetComponentsInChildren<LODGroup>(true);
            for (int i = 0; i < lodGroups.Length; i++)
            {
                LODGroup lodGroup = lodGroups[i];
                if (lodGroup == null)
                {
                    continue;
                }

                lodGroup.enabled = true;
                lodGroup.animateCrossFading = false;
                if (lodGroup.size <= 0.01f)
                {
                    lodGroup.size = EstimateLodGroupSize(lodGroup.gameObject);
                }

                lodGroup.RecalculateBounds();
            }
        }

        private static float EstimateLodGroupSize(GameObject root)
        {
            if (root == null)
            {
                return 1f;
            }

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = default;
            bool initialized = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (!IsRuntimeBuildingMeshRenderer(renderer))
                {
                    continue;
                }

                if (!initialized)
                {
                    bounds = renderer.bounds;
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (!initialized)
            {
                return 1f;
            }

            return Mathf.Max(1f, Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z));
        }

        private static void ForceRuntimeLodZero(GameObject visual)
        {
            if (visual == null)
            {
                return;
            }

            LODGroup[] lodGroups = visual.GetComponentsInChildren<LODGroup>(true);
            for (int i = 0; i < lodGroups.Length; i++)
            {
                LODGroup lodGroup = lodGroups[i];
                if (lodGroup == null)
                {
                    continue;
                }

                // Unity warns if ForceLOD is called on a disabled LODGroup. Disabled
                // groups can exist in authored prefabs while the visible child mesh is still
                // valid, so the runtime mesh repair should not mutate or force those groups.
                if (!lodGroup.enabled || !lodGroup.gameObject.activeInHierarchy)
                {
                    continue;
                }

                lodGroup.ForceLOD(0);
            }
        }

        private static bool IsRendererCurrentlyVisible(Renderer renderer)
        {
            return renderer != null
                && renderer.gameObject.activeInHierarchy
                && renderer.enabled
                && !renderer.forceRenderingOff
                && RendererHasVisibleMaterial(renderer);
        }

        private static bool IsRuntimeBuildingMeshRenderer(Renderer renderer)
        {
            if (renderer == null || IsAnchorAuthoringHelperRenderer(renderer))
            {
                return false;
            }

            if (!RendererHasMesh(renderer))
            {
                return false;
            }

            Transform transform = renderer.transform;
            if (TransformPathContains(transform, "collider")
                || TransformPathContains(transform, "anchor_")
                || TransformPathContains(transform, "servicepoint")
                || TransformPathContains(transform, "dropoff")
                || TransformPathContains(transform, "residential props")
                || TransformPathContains(transform, "commercial props"))
            {
                return false;
            }

            return true;
        }

        private static bool RendererHasMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned)
            {
                return skinned.sharedMesh != null;
            }

            MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
            return meshFilter != null && meshFilter.sharedMesh != null;
        }

        private static bool IsLodZeroRenderer(Transform transform)
        {
            while (transform != null)
            {
                string lowered = (transform.name ?? string.Empty).ToLowerInvariant();
                if (lowered.Contains("lod0") || lowered.Contains("lod_0") || lowered.Contains("lod 0"))
                {
                    return true;
                }

                transform = transform.parent;
            }

            return false;
        }

        private static bool TransformPathContains(Transform transform, string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            string loweredToken = token.ToLowerInvariant();
            while (transform != null)
            {
                string name = transform.name ?? string.Empty;
                if (name.ToLowerInvariant().Contains(loweredToken))
                {
                    return true;
                }

                transform = transform.parent;
            }

            return false;
        }

        private static void ActivateTransformChain(Transform root, Transform target)
        {
            if (root == null || target == null)
            {
                return;
            }

            List<Transform> chain = new();
            Transform current = target;
            while (current != null)
            {
                chain.Add(current);
                if (current == root)
                {
                    break;
                }

                current = current.parent;
            }

            for (int i = chain.Count - 1; i >= 0; i--)
            {
                if (chain[i] != null)
                {
                    chain[i].gameObject.SetActive(true);
                }
            }
        }

        private void EnsureRendererHasVisibleMaterial(Renderer renderer)
        {
            if (renderer == null || RendererHasVisibleMaterial(renderer))
            {
                return;
            }

            Material fallback = GetRuntimeBuildingFallbackMaterial();
            if (fallback == null)
            {
                return;
            }

            Material[] materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0)
            {
                renderer.sharedMaterial = fallback;
                return;
            }

            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                if (!MaterialLooksVisible(materials[i]))
                {
                    materials[i] = fallback;
                    changed = true;
                }
            }

            if (changed)
            {
                renderer.sharedMaterials = materials;
            }
        }

        private Material GetRuntimeBuildingFallbackMaterial()
        {
            if (runtimeBuildingFallbackMaterial == null || runtimeBuildingFallbackMaterial.shader == null)
            {
                runtimeBuildingFallbackMaterial = CreateDebugMaterial(new Color(0.62f, 0.50f, 0.36f, 1f));
                runtimeBuildingFallbackMaterial.name = "Generated Runtime Building Fallback Material";
            }

            return runtimeBuildingFallbackMaterial;
        }

        private static bool RendererHasVisibleMaterial(Renderer renderer)
        {
            if (renderer == null)
            {
                return false;
            }

            Material[] materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < materials.Length; i++)
            {
                if (MaterialLooksVisible(materials[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool MaterialLooksVisible(Material material)
        {
            if (!IsUsableMaterial(material))
            {
                return false;
            }

            if (material.HasProperty("_BaseColor") && material.GetColor("_BaseColor").a <= 0.02f)
            {
                return false;
            }

            if (material.HasProperty("_Color") && material.GetColor("_Color").a <= 0.02f)
            {
                return false;
            }

            return true;
        }

        private void ActivateAuthoredExteriorProps(GameObject visual, PlacedBuilding building)
        {
            if (visual == null || building == null)
            {
                return;
            }

            BuildingExteriorPropAuthoring authoring = visual.GetComponent<BuildingExteriorPropAuthoring>();
            if (authoring == null)
            {
                return;
            }

            TryResolveAssignedBusinessType(building.id, out BusinessType? assignedBusinessType);
            TownPlot plot = GetPlotById(building.plotId);
            int seed = settings != null ? settings.seed : 0;
            authoring.ActivateForRuntime(assignedBusinessType, building.definition, plot, seed, building.id);
        }

        private float ResolveVisualScaleMultiplier(BuildingDefinition definition, GridRect footprint, GameObject visualRoot, Quaternion rotation)
        {
            if (definition == null)
            {
                return 1f;
            }

            // Runtime building visuals preserve the authored prefab scale and only apply the
            // explicit authored scale multiplier from the definition. Plot/cell footprint stays
            // authoritative for claimed ground area and fit validation, but visuals are no longer
            // silently stretched or shrunk to fill the footprint.
            return definition.VisualScaleMultiplier;
        }

        private static bool TryGetRotatedHorizontalRendererSize(GameObject visualRoot, Quaternion rotation, out Vector2 size)
        {
            size = default;
            if (visualRoot == null)
            {
                return false;
            }

            Renderer[] renderers = visualRoot.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                return false;
            }

            bool initialized = false;
            Bounds bounds = default;
            Transform root = visualRoot.transform;
            Matrix4x4 rootWorldToLocal = root.worldToLocalMatrix;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled || IsAnchorAuthoringHelperRenderer(renderer))
                {
                    continue;
                }

                Bounds localBounds = renderer.localBounds;
                if (localBounds.size == Vector3.zero)
                {
                    continue;
                }

                Matrix4x4 rendererToRoot = rootWorldToLocal * renderer.transform.localToWorldMatrix;
                EncapsulateRotatedRendererBounds(localBounds, rendererToRoot, rotation, ref bounds, ref initialized);
            }

            if (!initialized || !IsFinitePositive(bounds.size.x) || !IsFinitePositive(bounds.size.z))
            {
                return false;
            }

            size = new Vector2(bounds.size.x, bounds.size.z);
            return true;
        }

        private static void EncapsulateRotatedRendererBounds(
            Bounds localBounds,
            Matrix4x4 rendererToRoot,
            Quaternion rotation,
            ref Bounds bounds,
            ref bool initialized)
        {
            Vector3 min = localBounds.min;
            Vector3 max = localBounds.max;

            for (int x = 0; x < 2; x++)
            {
                for (int y = 0; y < 2; y++)
                {
                    for (int z = 0; z < 2; z++)
                    {
                        Vector3 localCorner = new(
                            x == 0 ? min.x : max.x,
                            y == 0 ? min.y : max.y,
                            z == 0 ? min.z : max.z);
                        Vector3 rotatedCorner = rotation * rendererToRoot.MultiplyPoint3x4(localCorner);

                        if (!initialized)
                        {
                            bounds = new Bounds(rotatedCorner, Vector3.zero);
                            initialized = true;
                        }
                        else
                        {
                            bounds.Encapsulate(rotatedCorner);
                        }
                    }
                }
            }
        }

        private static bool IsAnchorAuthoringHelperRenderer(Renderer renderer)
        {
            Transform current = renderer != null ? renderer.transform : null;
            while (current != null)
            {
                if (current.GetComponent<BuildingAnchorMarker>() != null
                    || string.Equals(current.name, FrontDoorMarkerName, StringComparison.Ordinal)
                    || string.Equals(current.name, ServicePointMarkerName, StringComparison.Ordinal)
                    || string.Equals(current.name, DropOffPointMarkerName, StringComparison.Ordinal))
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        private static bool IsFinitePositive(float value)
        {
            return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static void HideRuntimeAnchorAuthoringHelpers(GameObject visual)
        {
            if (visual == null || !Application.isPlaying)
            {
                return;
            }

            HideNamedAnchorAuthoringHelper(visual.transform, FrontDoorMarkerName);
            HideNamedAnchorAuthoringHelper(visual.transform, ServicePointMarkerName);
            HideNamedAnchorAuthoringHelper(visual.transform, DropOffPointMarkerName);

            BuildingAnchorMarker[] markers = visual.GetComponentsInChildren<BuildingAnchorMarker>(true);
            for (int i = 0; i < markers.Length; i++)
            {
                if (markers[i] != null)
                {
                    HideRenderersAndColliders(markers[i].transform);
                }
            }
        }

        private static void HideNamedAnchorAuthoringHelper(Transform root, string markerName)
        {
            Transform marker = FindChildByExactName(root, markerName);
            if (marker != null)
            {
                HideRenderersAndColliders(marker);
            }
        }

        private static void HideRenderersAndColliders(Transform root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].enabled = false;
            }

            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }
        }

        private static BuildingDoorAnimator EnsureBuildingDoorAnimator(GameObject visual)
        {
            if (visual == null)
            {
                return null;
            }

            BuildingDoorAnimator doorAnimator = visual.GetComponentInChildren<BuildingDoorAnimator>(true);
            if (doorAnimator != null)
            {
                return doorAnimator;
            }

            return visual.AddComponent<BuildingDoorAnimator>();
        }

        private static GameObject TryInstantiateGameObject(GameObject prefab, Transform parent, string label)
        {
            if (prefab == null)
            {
                return null;
            }

            try
            {
                UnityEngine.Object instance = parent != null
                    ? Instantiate((UnityEngine.Object)prefab, parent)
                    : Instantiate((UnityEngine.Object)prefab);

                if (instance is GameObject gameObject)
                {
                    return gameObject;
                }

                if (instance is Component component)
                {
                    return component.gameObject;
                }
            }
            catch (System.Exception exception)
            {
                Debug.LogError($"Failed to instantiate {label} prefab '{prefab.name}': {exception.Message}", prefab);
            }

            return null;
        }

        private Vector3 GetRectWorldCenter(GridRect rect, float y)
        {
            Vector3 min = grid.CoordToWorldCenter(new GridCoord(rect.xMin, rect.zMin));
            Vector3 max = grid.CoordToWorldCenter(new GridCoord(rect.xMaxInclusive, rect.zMaxInclusive));
            Vector3 center = (min + max) * 0.5f;
            center.y = y;
            return center;
        }

        private static float GetFrontageYaw(GridDirection frontageDirection)
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

        private void CreateAnchorVisual(Transform root, int buildingId, BuildingAnchor anchor)
        {
            Color color = anchor.type switch
            {
                AnchorType.FrontDoor => settings.doorAnchorColor,
                AnchorType.Service => settings.serviceAnchorColor,
                AnchorType.DropOff => settings.dropOffAnchorColor,
                _ => Color.white
            };

            if (anchor.hasAuthoredWorldPosition)
            {
                CreatePointVisual(root, $"Anchor {buildingId:000} {anchor.type}", anchor.authoredWorldPosition, 0.45f, color);
                return;
            }

            CreateCellVisual(root, $"Anchor {buildingId:000} {anchor.type}", anchor.coord, 1.2f, 0.45f, color);
        }

        private void CreatePointVisual(Transform root, string objectName, Vector3 worldPosition, float size, Color color)
        {
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visual.name = objectName;
            visual.transform.SetParent(root, false);
            visual.transform.position = worldPosition;
            visual.transform.localScale = Vector3.one * size;

            Collider collider = visual.GetComponent<Collider>();
            if (collider != null)
            {
                DestroyUnityObject(collider);
            }

            Renderer renderer = visual.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = CreateDebugMaterial(color);
            }
        }

        private GameObject CreateCellVisual(Transform root, string objectName, GridCoord coord, float yOffset, float height, Color color)
        {
            GridRect rect = new(coord.x, coord.z, 1, 1);
            return CreateRectVisual(root, objectName, rect, yOffset, height, color);
        }

        private GameObject CreateCellVisual(Transform root, string objectName, GridCoord coord, float yOffset, float height, Color color, Material material)
        {
            GridRect rect = new(coord.x, coord.z, 1, 1);
            return CreateRectVisual(root, objectName, rect, yOffset, height, color, material);
        }

        private GameObject CreateRectVisual(Transform root, string objectName, GridRect rect, float yOffset, float height, Color color)
        {
            return CreateRectVisual(root, objectName, rect, yOffset, height, color, null);
        }

        private GameObject CreateRectVisual(Transform root, string objectName, GridRect rect, float yOffset, float height, Color color, Material material)
        {
            if (!rect.IsValid)
            {
                return null;
            }

            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = objectName;
            visual.transform.SetParent(root, false);
            EnsureGeneratedMarker(visual);

            float groundHeight = settings.worldCenter.y;
            TryResolveFootprintGroundHeight(rect, false, out groundHeight, out _);
            Vector3 center = GetRectWorldCenter(rect, groundHeight + yOffset);

            visual.transform.position = center;
            visual.transform.localScale = new Vector3(
                rect.width * settings.cellSizeMeters,
                height,
                rect.depth * settings.cellSizeMeters);

            Collider collider = visual.GetComponent<Collider>();
            if (collider != null)
            {
                DestroyUnityObject(collider);
            }

            Renderer renderer = visual.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material != null ? material : CreateDebugMaterial(color);
            }

            return visual;
        }

        private void CreateWorldBoxVisual(Transform root, string objectName, Vector3 center, Vector3 size, Color color, Material material)
        {
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = objectName;
            visual.transform.SetParent(root, false);
            EnsureGeneratedMarker(visual);
            visual.transform.position = center;
            visual.transform.localScale = size;

            Collider collider = visual.GetComponent<Collider>();
            if (collider != null)
            {
                DestroyUnityObject(collider);
            }

            Renderer renderer = visual.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material != null ? material : CreateDebugMaterial(color);
            }
        }

        private static bool IsUsableMaterial(Material material)
        {
            return material != null && material.shader != null && material.shader.isSupported;
        }

        private static Material CreateDebugMaterial(Color color)
        {
            Shader shader = Shader.Find("HDRP/Lit") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material material = new(shader);
            material.name = "Generated Town Debug Material";

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
            else if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            return material;
        }

        private Transform EnsureVisualRoot()
        {
            if (visualRoot != null)
            {
                EnsureGeneratedMarker(visualRoot.gameObject);
                EnsureAuthoredContentRoot();
                return visualRoot;
            }

            Transform existing = transform.Find(VisualRootName);
            if (existing != null)
            {
                visualRoot = existing;
                EnsureGeneratedMarker(visualRoot.gameObject);
                EnsureAuthoredContentRoot();
                return visualRoot;
            }

            GameObject root = new(VisualRootName);
            root.transform.SetParent(transform, false);
            visualRoot = root.transform;
            EnsureGeneratedMarker(root);
            EnsureAuthoredContentRoot();
            return visualRoot;
        }

        private static HashSet<int> CaptureRootChildIds(Transform root)
        {
            HashSet<int> result = new();
            if (root == null)
            {
                return result;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child != null)
                {
                    result.Add(child.gameObject.GetInstanceID());
                }
            }

            return result;
        }

        private static void MarkGeneratedChildren(Transform root, HashSet<int> authoredChildIds)
        {
            if (root == null)
            {
                return;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child != null && (authoredChildIds == null || !authoredChildIds.Contains(child.gameObject.GetInstanceID())))
                {
                    EnsureGeneratedMarker(child.gameObject);
                }
            }
        }

        private static void EnsureGeneratedMarker(GameObject target)
        {
            if (target != null && target.GetComponent<GeneratedWorldContentMarker>() == null)
            {
                target.AddComponent<GeneratedWorldContentMarker>();
            }
        }

        private Transform EnsureAuthoredContentRoot()
        {
            Transform existing = transform.Find(AuthoredContentRootName);
            if (existing == null)
            {
                GameObject sceneRoot = GameObject.Find(AuthoredContentRootName);
                existing = sceneRoot != null ? sceneRoot.transform : null;
            }

            if (existing != null)
            {
                return existing;
            }

            GameObject root = new(AuthoredContentRootName);
            root.transform.SetParent(transform, false);
            return root.transform;
        }

        private static void ClearVisualChildren(Transform root)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Transform child = root.GetChild(i);
                if (child != null && child.GetComponent<GeneratedWorldContentMarker>() != null)
                {
                    DestroyUnityObject(child.gameObject);
                }
            }
        }

        private void ValidatePlots(StringBuilder report, ref int errors, ref int warnings)
        {
            HashSet<int> seenPlotIds = new();
            int reservedVacantPlots = 0;
            int targetReservedVacantPlots = settings != null ? Mathf.Min(settings.vacantLandPlotsToReserve, plots.Count) : 0;
            int reservedUsableOffers = 0;
            int reservedFrontageOffers = 0;
            int eligibleFrontageOffers = 0;
            int weakReservedOffers = 0;
            int offMarketPracticalPlots = 0;
            int offMarketStrongFrontagePlots = 0;
            int weakFrontageLeftovers = 0;
            int speculativeOffMarketPlots = 0;

            for (int i = 0; i < plots.Count; i++)
            {
                TownPlot plot = plots[i];
                if (plot == null)
                {
                    AppendError(report, ref errors, $"Plot entry {i} is null.");
                    continue;
                }

                if (!seenPlotIds.Add(plot.id))
                {
                    AppendError(report, ref errors, $"Duplicate Plot id {plot.id} detected.");
                }

                if (!grid.ContainsRect(plot.bounds))
                {
                    AppendError(report, ref errors, $"Plot {plot.id} is outside grid bounds.");
                }

                if (plot.zone == PlotZone.Agricultural && plot.agriculturalSiteRole == AgriculturalSiteRole.None)
                {
                    AppendWarning(report, ref warnings, $"Plot {plot.id} is agricultural but has no agricultural site role.");
                }

                if (!grid.IsInBounds(plot.roadAccessCell) || !grid.GetCell(plot.roadAccessCell).IsRoad)
                {
                    AppendError(report, ref errors, $"Plot {plot.id} has no valid road frontage.");
                }

                ValidatePlotBuildingLinkage(plot, report, ref errors, ref warnings);
                ValidatePlotHoldingStateConsistency(plot, report, ref warnings);

                LandOfferCandidate offerCandidate = default;
                bool hasOfferCandidate = IsPlotEligibleForLandMarket(plot) || plot.zone == PlotZone.Agricultural && plot.buildingId < 0;
                if (hasOfferCandidate)
                {
                    offerCandidate = EvaluateLandOfferCandidate(plot);
                    if (offerCandidate.frontagePriority && offerCandidate.strength >= LandOfferStrength.Strong)
                    {
                        eligibleFrontageOffers++;
                    }
                }

                if (plot.reservedForLandSale)
                {
                    if (plot.buildingId >= 0)
                    {
                        AppendWarning(report, ref warnings, $"Plot {plot.id} is still marked for vacant land sale even though it is already improved.");
                    }
                    else
                    {
                        reservedVacantPlots++;
                    }

                    if (plot.zone == PlotZone.Agricultural)
                    {
                        AppendWarning(report, ref warnings, $"Plot {plot.id} is agricultural but is flagged as an ordinary vacant land offer.");
                    }

                    if (hasOfferCandidate)
                    {
                        if (offerCandidate.frontagePriority && offerCandidate.strength >= LandOfferStrength.Strong)
                        {
                            reservedFrontageOffers++;
                        }

                        if (offerCandidate.strength >= LandOfferStrength.Practical)
                        {
                            reservedUsableOffers++;
                        }

                        if (offerCandidate.strength == LandOfferStrength.Weak)
                        {
                            weakReservedOffers++;
                        }
                    }
                }
                else if (hasOfferCandidate)
                {
                    if (offerCandidate.strength >= LandOfferStrength.Practical)
                    {
                        offMarketPracticalPlots++;
                    }

                    if (offerCandidate.frontagePriority && offerCandidate.strength >= LandOfferStrength.Strong)
                    {
                        offMarketStrongFrontagePlots++;
                    }

                    if (offerCandidate.strength == LandOfferStrength.Speculative)
                    {
                        speculativeOffMarketPlots++;
                    }

                    if (IsWeakFrontageLeftover(plot, offerCandidate))
                    {
                        weakFrontageLeftovers++;
                    }
                }

                ValidateVacantPlotOpportunity(plot, report, ref errors, ref warnings);

                for (int j = i + 1; j < plots.Count; j++)
                {
                    TownPlot otherPlot = plots[j];
                    if (otherPlot == null)
                    {
                        continue;
                    }

                    if (plot.bounds.Overlaps(otherPlot.bounds))
                    {
                        AppendError(report, ref errors, $"Plot {plot.id} overlaps plot {otherPlot.id}.");
                    }
                }
            }

            if (targetReservedVacantPlots > 0 && reservedVacantPlots < targetReservedVacantPlots)
            {
                AppendWarning(report, ref warnings, $"Town only has {reservedVacantPlots} vacant land offer plot(s) reserved, below the target of {targetReservedVacantPlots}.");
            }

            if (eligibleFrontageOffers > 0 && reservedFrontageOffers <= 0)
            {
                AppendWarning(report, ref warnings, "Land-offer reservation currently exposes no strong frontage opportunity even though the current town seed has frontage-ready candidates.");
            }

            if (reservedVacantPlots > 0 && reservedUsableOffers <= 0)
            {
                AppendWarning(report, ref warnings, "Reserved land offers currently read like weak leftover inventory rather than practical opportunities.");
            }
            else if (reservedVacantPlots > 0 && weakReservedOffers >= reservedVacantPlots)
            {
                AppendWarning(report, ref warnings, "Every reserved land offer currently reads as weak leftover inventory.");
            }

            if (reservedVacantPlots < targetReservedVacantPlots && offMarketPracticalPlots > 0)
            {
                AppendWarning(report, ref warnings, $"Town is below its reserved land-offer target while {offMarketPracticalPlots} practical vacant plot(s) remain off-market.");
            }

            if (reservedFrontageOffers <= 0 && offMarketStrongFrontagePlots > 0)
            {
                AppendWarning(report, ref warnings, $"Town currently holds back {offMarketStrongFrontagePlots} strong frontage-ready vacant plot(s) even though no reserved land offer exposes that quality yet.");
            }

            if (weakFrontageLeftovers >= Mathf.Max(3, reservedVacantPlots))
            {
                AppendWarning(report, ref warnings, $"Town has {weakFrontageLeftovers} weak frontage-side vacant plot(s) reading more like leftover inventory than credible near-term opportunities.");
            }

            if (speculativeOffMarketPlots >= Mathf.Max(3, targetReservedVacantPlots + 1))
            {
                AppendWarning(report, ref warnings, $"Most off-market vacant stock is currently speculative ({speculativeOffMarketPlots} plot(s)), which may make the town's remaining parcel inventory feel thin.");
            }
        }

        private void ValidateBuildings(StringBuilder report, ref int errors, ref int warnings)
        {
            HashSet<int> seenBuildingIds = new();
            foreach (PlacedBuilding building in buildings)
            {
                if (building == null)
                {
                    AppendError(report, ref errors, "Building entry is null.");
                    continue;
                }

                if (!seenBuildingIds.Add(building.id))
                {
                    AppendError(report, ref errors, $"Duplicate Building id {building.id} detected.");
                }

                foreach (BuildingAnchorIssue issue in building.anchorIssues)
                {
                    if (issue.isError)
                    {
                        AppendError(report, ref errors, issue.message);
                    }
                    else
                    {
                        AppendWarning(report, ref warnings, issue.message);
                    }
                }

                if (!grid.ContainsRect(building.footprint))
                {
                    AppendError(report, ref errors, $"Building {building.id} footprint is outside grid bounds.");
                }

                TownPlot plot = null;
                bool hasLinkedPlot = TryGetPlot(building.plotId, out plot);
                ValidateBuildingPlotLinkage(building, plot, report, ref errors, ref warnings);

                if (hasLinkedPlot)
                {
                    ValidateAgriculturalBuildingFit(plot, building, report, ref errors, ref warnings);
                    ValidateExteriorPropAuthoring(plot, building, report, ref errors, ref warnings);
                    ValidateBuildingSiteFit(plot, building, report, ref warnings);
                    ValidateFootprintReconciliation(plot, building, report, ref warnings);
                    ValidateImprovedSitePressure(plot, building, report, ref warnings);
                    ValidateBuildingHoldingStateConsistency(building, plot, report, ref warnings);
                }
                else
                {
                    ValidateExteriorPropAuthoring(null, building, report, ref errors, ref warnings);
                }

                if (building.anchors.Count == 0)
                {
                    AppendWarning(report, ref warnings, $"Building {building.id} has no anchors.");
                }

                ValidateAnchorPresence(building, report, ref errors, ref warnings);
                ValidateAnchorLayout(plot, building, report, ref errors, ref warnings);

                foreach (BuildingAnchor anchor in building.anchors)
                {
                    if (!grid.IsInBounds(anchor.coord))
                    {
                        AppendError(report, ref errors, $"Building {building.id} {anchor.type} anchor is outside grid bounds.");
                    }
                    else if (building.footprint.Contains(anchor.coord))
                    {
                        if (!anchor.fromAuthoredMarker)
                        {
                            AppendWarning(report, ref warnings, $"Building {building.id} {anchor.type} fallback/runtime anchor at {anchor.coord} sits inside the claimed building footprint. Prefer an authored prefab marker for interior door/property destinations.");
                        }
                    }
                    else if (!AnchorHasAccess(anchor.coord))
                    {
                        AppendWarning(report, ref warnings, $"Building {building.id} {anchor.type} anchor at {anchor.coord} is not adjacent to road or plot access.");
                    }

                    if (grid.IsInBounds(anchor.coord) && !AnchorIsWalkableForBuilding(building, anchor))
                    {
                        AppendWarning(report, ref warnings, $"Building {building.id} {anchor.type} anchor at {anchor.coord} is not walkable by pathing traversal rules.");
                    }

                    if (anchor.wasReconciledToExteriorAccess && !string.IsNullOrWhiteSpace(anchor.reconciliationNote))
                    {
                        AppendWarning(report, ref warnings, $"Building {building.id} {anchor.type} anchor was exterior-reconciled: {anchor.reconciliationNote}");
                    }

                    if (anchor.fromFallbackRule)
                    {
                        AppendWarning(report, ref warnings, $"Building {building.id} {anchor.type} anchor at {anchor.coord} came from legacy fallback rule '{anchor.source}'. Prefer an authored prefab marker.");
                    }
                }
            }
        }

        private string BuildAnchorInspectionSummary(PlacedBuilding building)
        {
            if (building == null || building.definition == null)
            {
                return string.Empty;
            }

            StringBuilder builder = new("Anchors: ");
            AppendAnchorInspectionSegment(builder, building, AnchorType.FrontDoor);
            builder.Append(" | ");
            AppendAnchorInspectionSegment(builder, building, AnchorType.Service);
            builder.Append(" | ");
            AppendAnchorInspectionSegment(builder, building, AnchorType.DropOff);
            return builder.ToString();
        }

        private void AppendAnchorInspectionSegment(StringBuilder builder, PlacedBuilding building, AnchorType anchorType)
        {
            if (builder == null || building == null || building.definition == null)
            {
                return;
            }

            string label = GetAnchorDisplayLabel(anchorType);
            BuildingAnchorExpectation expectation = building.definition.GetAnchorExpectation(anchorType);
            if (!building.TryGetAnchor(anchorType, out BuildingAnchor anchor))
            {
                builder.Append($"{label} missing ({FormatAnchorExpectation(expectation)})");
                return;
            }

            GridDirection sideDirection = GetNearestFootprintEdgeDirection(building.footprint, anchor.coord);
            string sideRead = FormatAnchorSideRead(building.frontageDirection, sideDirection);
            string accessRead = BuildAnchorAccessRead(anchor.coord);
            string sourceRead = anchor.fromAuthoredMarker ? "authored" : anchor.fromFallbackRule ? "fallback" : "runtime";
            if (anchor.wasReconciledToExteriorAccess)
            {
                sourceRead += $", exterior-reconciled from {anchor.authoredCoordBeforeReconciliation}";
            }

            builder.Append($"{label} {sideRead}, {accessRead}, {sourceRead}");
        }

        private void ValidateAnchorPresence(PlacedBuilding building, StringBuilder report, ref int errors, ref int warnings)
        {
            if (building == null || building.definition == null)
            {
                return;
            }

            ValidateAnchorPresence(building, AnchorType.FrontDoor, report, ref warnings);
            ValidateAnchorPresence(building, AnchorType.Service, report, ref warnings);
            ValidateAnchorPresence(building, AnchorType.DropOff, report, ref warnings);
        }

        private void ValidateAnchorPresence(PlacedBuilding building, AnchorType anchorType, StringBuilder report, ref int warnings)
        {
            if (building == null || building.definition == null)
            {
                return;
            }

            if (building.TryGetAnchor(anchorType, out _))
            {
                return;
            }

            BuildingAnchorExpectation expectation = building.definition.GetAnchorExpectation(anchorType);
            if (expectation == BuildingAnchorExpectation.Optional)
            {
                return;
            }

            string requirementRead = expectation == BuildingAnchorExpectation.Required ? "required" : "preferred";
            AppendWarning(report, ref warnings, $"Building {building.id} is missing its {requirementRead} {GetAnchorDisplayLabel(anchorType).ToLowerInvariant()} anchor.");
        }

        private void ValidateAnchorLayout(TownPlot plot, PlacedBuilding building, StringBuilder report, ref int errors, ref int warnings)
        {
            if (building == null || building.definition == null)
            {
                return;
            }

            if (building.TryGetAnchor(AnchorType.FrontDoor, out BuildingAnchor frontDoor))
            {
                bool authoredInteriorFrontDoor = frontDoor.fromAuthoredMarker && building.footprint.Contains(frontDoor.coord);
                GridDirection doorSide = GetNearestFootprintEdgeDirection(building.footprint, frontDoor.coord);
                if (building.definition.PrefersRoadFacingFrontDoor && !authoredInteriorFrontDoor && doorSide != building.frontageDirection)
                {
                    AppendWarning(report, ref warnings, $"Building {building.id} front door anchor reads as {FormatAnchorSideRead(building.frontageDirection, doorSide)} instead of frontage-side.");
                }

                if (building.definition.PrefersRoadFacingFrontDoor && !authoredInteriorFrontDoor && !AnchorTouchesRoad(frontDoor.coord))
                {
                    AppendWarning(report, ref warnings, $"Building {building.id} front door anchor is not road-adjacent, which weakens frontage readability.");
                }
            }

            ValidateOperationalAnchorPlacement(plot, building, AnchorType.Service, ref warnings, report);
            ValidateOperationalAnchorPlacement(plot, building, AnchorType.DropOff, ref warnings, report);
            ValidateDuplicateAnchorCoords(building, report, ref warnings);
        }

        private void ValidateOperationalAnchorPlacement(TownPlot plot, PlacedBuilding building, AnchorType anchorType, ref int warnings, StringBuilder report)
        {
            if (building == null || building.definition == null)
            {
                return;
            }

            if (!building.TryGetAnchor(anchorType, out BuildingAnchor anchor))
            {
                return;
            }

            GridDirection sideDirection = GetNearestFootprintEdgeDirection(building.footprint, anchor.coord);
            bool agriculturalContext = (plot != null && plot.zone == PlotZone.Agricultural) || building.definition.HasDedicatedAgriculturalRole;
            if (agriculturalContext && sideDirection == building.frontageDirection)
            {
                AppendWarning(report, ref warnings, $"Building {building.id} {GetAnchorDisplayLabel(anchorType).ToLowerInvariant()} anchor sits on public frontage; a rear or side yard placement would read better for a work yard.");
            }

            if (building.TryGetAnchor(AnchorType.FrontDoor, out BuildingAnchor frontDoor) && frontDoor.coord.Equals(anchor.coord))
            {
                AppendWarning(report, ref warnings, $"Building {building.id} {GetAnchorDisplayLabel(anchorType).ToLowerInvariant()} anchor shares a cell with the front door anchor.");
            }
        }

        private static void ValidateDuplicateAnchorCoords(PlacedBuilding building, StringBuilder report, ref int warnings)
        {
            if (building == null)
            {
                return;
            }

            for (int i = 0; i < building.anchors.Count; i++)
            {
                for (int j = i + 1; j < building.anchors.Count; j++)
                {
                    if (building.anchors[i].coord.Equals(building.anchors[j].coord))
                    {
                        AppendWarning(report, ref warnings, $"Building {building.id} has {GetAnchorDisplayLabel(building.anchors[i].type)} and {GetAnchorDisplayLabel(building.anchors[j].type)} anchors on the same cell {building.anchors[i].coord}.");
                    }
                }
            }
        }

        private string BuildAnchorAccessRead(GridCoord coord)
        {
            bool roadAdjacent = AnchorTouchesRoad(coord);
            bool plotAdjacent = AnchorTouchesOpenPlot(coord);
            if (roadAdjacent && plotAdjacent)
            {
                return "road and yard access";
            }

            if (roadAdjacent)
            {
                return "road-adjacent";
            }

            if (plotAdjacent)
            {
                return "yard-access";
            }

            return "weak access";
        }

        private bool AnchorTouchesRoad(GridCoord coord)
        {
            if (grid == null)
            {
                return false;
            }

            if (grid.IsInBounds(coord) && grid.GetCell(coord).IsRoad)
            {
                return true;
            }

            GridCoord[] neighbors =
            {
                new(coord.x + 1, coord.z),
                new(coord.x - 1, coord.z),
                new(coord.x, coord.z + 1),
                new(coord.x, coord.z - 1)
            };

            foreach (GridCoord neighbor in neighbors)
            {
                if (grid.IsInBounds(neighbor) && grid.GetCell(neighbor).IsRoad)
                {
                    return true;
                }
            }

            return false;
        }

        private bool AnchorTouchesOpenPlot(GridCoord coord)
        {
            if (grid == null)
            {
                return false;
            }

            if (grid.IsInBounds(coord))
            {
                TownCell ownCell = grid.GetCell(coord);
                if ((ownCell.occupancy & CellOccupancy.Plot) != 0
                    && !ownCell.HasBuilding
                    && (ownCell.occupancy & CellOccupancy.Blocked) == 0
                    && !ownCell.blocked)
                {
                    return true;
                }
            }

            GridCoord[] neighbors =
            {
                new(coord.x + 1, coord.z),
                new(coord.x - 1, coord.z),
                new(coord.x, coord.z + 1),
                new(coord.x, coord.z - 1)
            };

            foreach (GridCoord neighbor in neighbors)
            {
                if (!grid.IsInBounds(neighbor))
                {
                    continue;
                }

                TownCell cell = grid.GetCell(neighbor);
                if ((cell.occupancy & CellOccupancy.Plot) != 0
                    && !cell.HasBuilding
                    && (cell.occupancy & CellOccupancy.Blocked) == 0
                    && !cell.blocked)
                {
                    return true;
                }
            }

            return false;
        }

        private static GridDirection GetNearestFootprintEdgeDirection(GridRect footprint, GridCoord coord)
        {
            GridDirection bestDirection = GridDirection.North;
            int bestDistance = int.MaxValue;

            GridDirection[] directions =
            {
                GridDirection.North,
                GridDirection.East,
                GridDirection.South,
                GridDirection.West
            };

            for (int i = 0; i < directions.Length; i++)
            {
                GridDirection direction = directions[i];
                GridCoord edgeCenter = GetFootprintEdgeCenter(footprint, direction);
                int distance = Mathf.Abs(coord.x - edgeCenter.x) + Mathf.Abs(coord.z - edgeCenter.z);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestDirection = direction;
                }
            }

            return bestDirection;
        }

        private static string FormatAnchorSideRead(GridDirection frontageDirection, GridDirection sideDirection)
        {
            if (sideDirection == frontageDirection)
            {
                return "frontage-side";
            }

            if (sideDirection == Opposite(frontageDirection))
            {
                return "rear-yard side";
            }

            if (sideDirection == TurnLeft(frontageDirection))
            {
                return "left flank";
            }

            if (sideDirection == TurnRight(frontageDirection))
            {
                return "right flank";
            }

            return FormatDirection(sideDirection);
        }

        private static string GetAnchorDisplayLabel(AnchorType anchorType)
        {
            return anchorType switch
            {
                AnchorType.FrontDoor => "Front door",
                AnchorType.Service => "Service",
                AnchorType.DropOff => "Drop-off",
                _ => anchorType.ToString()
            };
        }

        private static string FormatAnchorExpectation(BuildingAnchorExpectation expectation)
        {
            return expectation switch
            {
                BuildingAnchorExpectation.Required => "required",
                BuildingAnchorExpectation.Preferred => "preferred",
                _ => "optional"
            };
        }

        private static bool HasAnchor(PlacedBuilding building, AnchorType anchorType)
        {
            for (int i = 0; i < building.anchors.Count; i++)
            {
                if (building.anchors[i].type == anchorType)
                {
                    return true;
                }
            }

            return false;
        }

        private bool AnchorHasAccess(GridCoord coord)
        {
            return AnchorTouchesRoad(coord) || AnchorTouchesOpenPlot(coord);
        }

        private bool AnchorIsWalkableForBuilding(PlacedBuilding building, BuildingAnchor anchor)
        {
            if (building != null
                && anchor.fromAuthoredMarker
                && building.footprint.Contains(anchor.coord)
                && grid != null
                && grid.IsInBounds(anchor.coord))
            {
                return true;
            }

            return AnchorIsWalkable(anchor.coord);
        }

        private bool AnchorIsWalkable(GridCoord coord)
        {
            if (grid == null || !grid.IsInBounds(coord))
            {
                return false;
            }

            TownCell cell = grid.GetCell(coord);
            if ((cell.occupancy & CellOccupancy.Anchor) != 0)
            {
                return true;
            }

            if (cell.HasBuilding || (cell.occupancy & CellOccupancy.Blocked) != 0 || cell.blocked)
            {
                return false;
            }

            if (cell.IsRoad)
            {
                return true;
            }

            if ((cell.occupancy & CellOccupancy.Plot) != 0)
            {
                return true;
            }

            return cell.terrainZone == TerrainZone.Buildable;
        }

        private static void AppendError(StringBuilder report, ref int errors, string message)
        {
            errors++;
            report.AppendLine($"ERROR: {message}");
        }

        private static void AppendWarning(StringBuilder report, ref int warnings, string message)
        {
            warnings++;
            report.AppendLine($"WARN: {message}");
        }

        private string BuildWorldValidationReport(StringBuilder rawReport, int errors, int warnings)
        {
            if (!compactWorldValidationReport || rawReport == null)
            {
                return rawReport?.ToString().TrimEnd() ?? string.Empty;
            }

            string[] lines = rawReport.ToString().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            List<string> errorLines = new();
            List<string> warningLines = new();
            Dictionary<string, int> warningCategoryCounts = new(StringComparer.Ordinal);
            Dictionary<string, List<string>> warningCategorySamples = new(StringComparer.Ordinal);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (line.StartsWith("ERROR:", StringComparison.Ordinal))
                {
                    errorLines.Add(line);
                    continue;
                }

                if (line.StartsWith("WARN:", StringComparison.Ordinal))
                {
                    warningLines.Add(line);
                    string category = ResolveWorldValidationWarningCategory(line);
                    warningCategoryCounts.TryGetValue(category, out int categoryCount);
                    warningCategoryCounts[category] = categoryCount + 1;

                    if (!warningCategorySamples.TryGetValue(category, out List<string> samples))
                    {
                        samples = new List<string>();
                        warningCategorySamples[category] = samples;
                    }

                    if (samples.Count < worldValidationCategorySampleLimit)
                    {
                        samples.Add(StripValidationPrefix(line));
                    }
                }
            }

            StringBuilder builder = new();
            if (errors > 0)
            {
                builder.AppendLine("ERRORS:");
                for (int i = 0; i < errorLines.Count; i++)
                {
                    builder.AppendLine(errorLines[i]);
                }
            }

            if (warnings > 0 && includeWorldValidationCategoryBreakdown)
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.AppendLine("WARNING SUMMARY:");
                foreach (KeyValuePair<string, int> pair in warningCategoryCounts)
                {
                    builder.AppendLine($"- {pair.Key}: {pair.Value}");
                    if (worldValidationCategorySampleLimit <= 0 || !warningCategorySamples.TryGetValue(pair.Key, out List<string> samples))
                    {
                        continue;
                    }

                    for (int i = 0; i < samples.Count; i++)
                    {
                        builder.AppendLine($"  - {samples[i]}");
                    }
                }
            }

            int detailLimit = Mathf.Max(0, worldValidationDetailedWarningLimit);
            if (warningLines.Count > 0 && detailLimit > 0)
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                int shown = Mathf.Min(detailLimit, warningLines.Count);
                builder.AppendLine($"WARNING DETAIL: showing {shown}/{warningLines.Count} warning(s). Increase World Validation Detailed Warning Limit for the full raw list.");
                for (int i = 0; i < shown; i++)
                {
                    builder.AppendLine(warningLines[i]);
                }

                if (warningLines.Count > shown)
                {
                    builder.AppendLine($"WARN: {warningLines.Count - shown} additional warning(s) hidden by compact validation reporting.");
                }
            }

            return builder.ToString().TrimEnd();
        }

        private static string ResolveWorldValidationWarningCategory(string warningLine)
        {
            string lower = warningLine.ToLowerInvariant();
            if (lower.Contains("prefab footprint authority"))
            {
                return "Prefab footprint authority / visual scale";
            }

            if (lower.Contains("exterior prop authoring") || lower.Contains("exterior dressing"))
            {
                return "Exterior prop authoring";
            }

            if (lower.Contains("anchor"))
            {
                return "Building anchor/access authoring";
            }

            if (lower.Contains("weak generated fit") || lower.Contains("narrow fit") || lower.Contains("frontage fit lane") || lower.Contains("agricultural fit"))
            {
                return "Building/plot fit quality";
            }

            if (lower.Contains("land for sale") || lower.Contains("land-offer") || lower.Contains("vacant"))
            {
                return "Land-offer inventory";
            }

            if (lower.Contains("ownership") || lower.Contains("holding"))
            {
                return "Ownership/holding state";
            }

            return "Other generated-world warning";
        }

        private static string StripValidationPrefix(string line)
        {
            if (line.StartsWith("WARN: ", StringComparison.Ordinal))
            {
                return line.Substring(6);
            }

            if (line.StartsWith("ERROR: ", StringComparison.Ordinal))
            {
                return line.Substring(7);
            }

            return line;
        }

        private void UpdateSummary()
        {
            generatedCellCount = grid.CellCount;
            generatedPlotCount = plots.Count;
            generatedBuildingCount = buildings.Count;
            generatedAnchorCount = 0;

            foreach (PlacedBuilding building in buildings)
            {
                generatedAnchorCount += building.anchors.Count;
            }

            generatedRegionalFoundationSummary = BuildRegionalFoundationInspectionSummary();
        }

        private static GridCoordSaveDto ToSaveDto(GridCoord coord)
        {
            return new GridCoordSaveDto
            {
                x = coord.x,
                z = coord.z
            };
        }

        private static GridRectSaveDto ToSaveDto(GridRect rect)
        {
            return new GridRectSaveDto
            {
                xMin = rect.xMin,
                zMin = rect.zMin,
                width = rect.width,
                depth = rect.depth
            };
        }

        private static GridCoord FromSaveDto(GridCoordSaveDto dto)
        {
            return dto != null ? new GridCoord(dto.x, dto.z) : new GridCoord();
        }

        private static GridRect FromSaveDto(GridRectSaveDto dto)
        {
            return dto != null ? new GridRect(dto.xMin, dto.zMin, dto.width, dto.depth) : new GridRect();
        }

        private static TownPlot FromSaveDto(PlotSaveDto dto)
        {
            if (dto == null)
            {
                return new TownPlot();
            }

            return new TownPlot
            {
                id = dto.id,
                zone = dto.zone,
                bounds = FromSaveDto(dto.bounds),
                candidateFootprint = FromSaveDto(dto.candidateFootprint),
                siteSizeCells = new Vector2Int(dto.siteSizeX, dto.siteSizeY),
                intendedBuildingFootprintCells = new Vector2Int(dto.intendedFootprintX, dto.intendedFootprintY),
                agriculturalSiteRole = dto.agriculturalSiteRole,
                publicSiteRole = dto.publicSiteRole,
                frontageCells = dto.frontageCells,
                depthCells = dto.depthCells,
                roadFrontageDirection = dto.roadFrontageDirection,
                roadAccessCell = FromSaveDto(dto.roadAccessCell),
                buildingId = dto.buildingId,
                reservedForLandSale = dto.reservedForLandSale,
                playerOwned = dto.playerOwned
            };
        }

        private static PlacedBuilding FromSaveDto(BuildingSaveDto dto, BuildingDefinition definition)
        {
            PlacedBuilding building = new()
            {
                id = dto.id,
                plotId = dto.plotId,
                definition = definition,
                footprint = FromSaveDto(dto.footprint),
                siteSizeCells = new Vector2Int(dto.siteSizeX, dto.siteSizeY),
                intendedFootprintSizeCells = new Vector2Int(dto.intendedFootprintX, dto.intendedFootprintY),
                frontageDirection = dto.frontageDirection,
                publicSiteRole = dto.publicSiteRole,
                playerOwned = dto.playerOwned,
                loadedFromSave = true,
                savedFootprintBeforeReconciliation = FromSaveDto(dto.footprint),
                savedIntendedFootprintSizeCells = new Vector2Int(dto.intendedFootprintX, dto.intendedFootprintY),
                savedAnchorCount = dto.anchors != null ? dto.anchors.Count : 0
            };

            if (dto.anchors != null)
            {
                for (int i = 0; i < dto.anchors.Count; i++)
                {
                    BuildingAnchorSaveDto anchorDto = dto.anchors[i];
                    if (anchorDto == null)
                    {
                        continue;
                    }

                    building.anchors.Add(new BuildingAnchor
                    {
                        type = anchorDto.type,
                        coord = FromSaveDto(anchorDto.coord),
                        fromAuthoredMarker = anchorDto.fromAuthoredMarker,
                        fromFallbackRule = anchorDto.fromFallbackRule,
                        source = anchorDto.source ?? string.Empty
                    });
                }
            }

            return building;
        }

        private bool TryGetPlot(int plotId, out TownPlot plot)
        {
            plot = null;
            for (int i = 0; i < plots.Count; i++)
            {
                TownPlot candidate = plots[i];
                if (candidate != null && candidate.id == plotId)
                {
                    plot = candidate;
                    return true;
                }
            }

            return false;
        }

        private bool TryGetBuilding(int buildingId, out PlacedBuilding building)
        {
            building = null;
            for (int i = 0; i < buildings.Count; i++)
            {
                PlacedBuilding candidate = buildings[i];
                if (candidate != null && candidate.id == buildingId)
                {
                    building = candidate;
                    return true;
                }
            }

            return false;
        }

        private static string GetBuildingDisplayName(PlacedBuilding building)
        {
            if (building == null)
            {
                return "Unknown Building";
            }

            if (building.definition != null && !string.IsNullOrWhiteSpace(building.definition.DisplayName))
            {
                return building.definition.DisplayName;
            }

            return $"Building {building.id:000}";
        }

        private string BuildVacantPlotOpportunitySummary(TownPlot plot)
        {
            if (plot == null || plot.buildingId >= 0)
            {
                return string.Empty;
            }

            LandOfferCandidate candidate = EvaluateLandOfferCandidate(plot);

            string marketRead = plot.playerOwned
                ? "player-held land"
                : plot.reservedForLandSale
                    ? "surfaced as a land offer"
                    : "held off-market";
            string qualityRead = BuildLandOfferStrengthRead(plot, candidate);
            string frontageUseRead = BuildVacantPlotFrontageUseRead(plot, candidate);
            string triageRead = BuildVacantPlotTriageRead(plot, candidate);
            string fitDepth = candidate.totalFits >= 6
                ? "deep shell choice"
                : candidate.totalFits >= 3
                    ? "usable shell choice"
                    : candidate.totalFits >= 1
                        ? "narrow shell choice"
                        : "no current shell fit";
            string opportunityLane = BuildPlotOpportunityLaneRead(
                plot,
                candidate.workplaceFits,
                candidate.householdFits,
                candidate.mixedUseFits,
                candidate.dedicatedAgriculturalFits,
                candidate.fallbackAgriculturalFits);
            BuildingDefinition starterDefinition = GetBestOpportunityDefinitionForPlot(plot);
            string starterRead = starterDefinition != null
                ? $"starter shell {starterDefinition.DisplayName}"
                : "no starter shell in current catalog";
            return $"Opportunity: {marketRead} | {qualityRead} | {frontageUseRead} | {triageRead} | {fitDepth} | {opportunityLane} | {starterRead}";
        }

        private static bool IsCivicParcel(TownPlot plot)
        {
            return plot != null && plot.publicSiteRole != PublicSiteRole.None;
        }

        private static bool IsPlayerHeldSite(PlacedBuilding building, TownPlot plot)
        {
            return (building != null && building.playerOwned) || (plot != null && plot.playerOwned);
        }

        private static bool IsCivicBuildingSite(PlacedBuilding building, TownPlot plot)
        {
            return (building != null && building.publicSiteRole != PublicSiteRole.None) || IsCivicParcel(plot);
        }

        // Land-sale flags only belong on town-held, vacant, non-civic, non-agricultural parcels.
        // Keep this helper aligned with validation and inspection wording so stale flags surface consistently.
        private bool HasStaleLandSaleFlag(TownPlot plot)
        {
            if (plot == null || !plot.reservedForLandSale)
            {
                return false;
            }

            return !IsPlotEligibleForLandMarket(plot);
        }

        // Inspection wording and validation warnings share this note on purpose so stale-flag messaging does not drift.
        private static string BuildStaleLandSaleFlagReviewNote(bool staleSaleFlag)
        {
            return staleSaleFlag ? " | reserved-for-sale flag should be cleared" : string.Empty;
        }

        private void AppendStaleLandSaleFlagWarning(StringBuilder report, ref int warnings, string subject)
        {
            AppendWarning(report, ref warnings, $"{subject} carries a land-sale flag outside the supported town-held vacant parcel lane.");
        }

        private bool IsActiveLandOffer(TownPlot plot)
        {
            return plot != null && plot.reservedForLandSale && !HasStaleLandSaleFlag(plot);
        }

        private string BuildPlotHoldingRead(TownPlot plot)
        {
            if (plot == null)
            {
                return "Plot state unclear";
            }

            if (IsCivicParcel(plot))
            {
                return plot.playerOwned ? "Player-held civic parcel" : "Civic parcel";
            }

            bool staleSaleFlag = HasStaleLandSaleFlag(plot);
            if (plot.buildingId >= 0)
            {
                if (staleSaleFlag)
                {
                    return plot.playerOwned ? "Player-held improved site with stale sale flag" : "Improved site with stale sale flag";
                }

                if (plot.playerOwned)
                {
                    return plot.zone == PlotZone.Agricultural ? "Player-held working parcel" : "Player-held improved site";
                }

                return plot.zone == PlotZone.Agricultural ? "Town-held working parcel" : "Town-held improved site";
            }

            if (plot.playerOwned)
            {
                return plot.zone == PlotZone.Agricultural ? "Player-held work reserve" : "Player-held reserve";
            }

            if (IsActiveLandOffer(plot))
            {
                return "Town land offer";
            }

            if (staleSaleFlag)
            {
                return "Town-held parcel with stale sale flag";
            }

            return plot.zone == PlotZone.Agricultural ? "Town-held work reserve" : "Town-held reserve";
        }

        private string BuildPlotMarketStateSummary(TownPlot plot)
        {
            if (plot == null)
            {
                return string.Empty;
            }

            bool staleSaleFlag = HasStaleLandSaleFlag(plot);
            if (IsCivicParcel(plot))
            {
                string civicRead = plot.buildingId >= 0 ? "civic site stays off-market by role" : "civic reserve stays off-market by role";
                if (staleSaleFlag)
                {
                    civicRead += BuildStaleLandSaleFlagReviewNote(true);
                }

                if (plot.playerOwned)
                {
                    civicRead += " | player ownership should be reviewed";
                }

                return $"Market state: {civicRead}";
            }

            if (plot.buildingId >= 0)
            {
                string improvedRead = plot.zone == PlotZone.Agricultural ? "improved working parcel" : "improved parcel";
                string holderRead = plot.playerOwned ? "player-held" : "off-market by current use";
                return staleSaleFlag
                    ? $"Market state: {holderRead} {improvedRead}{BuildStaleLandSaleFlagReviewNote(true)}"
                    : $"Market state: {holderRead} {improvedRead}";
            }

            LandOfferCandidate candidate = EvaluateLandOfferCandidate(plot);
            if (plot.playerOwned)
            {
                string reserveRead = plot.zone == PlotZone.Agricultural ? "player-held work reserve" : "player-held reserve";
                return $"Market state: {reserveRead} | not surfaced publicly";
            }

            if (plot.zone == PlotZone.Agricultural)
            {
                string workRead = candidate.dedicatedAgriculturalFits > 0
                    ? "role-specific working reserve"
                    : candidate.fallbackAgriculturalFits > 0
                        ? "generic working reserve"
                        : "weak working reserve";
                return staleSaleFlag
                    ? $"Market state: {workRead}{BuildStaleLandSaleFlagReviewNote(true)}"
                    : $"Market state: {workRead} | off-market by yard role";
            }

            if (IsActiveLandOffer(plot))
            {
                return $"Market state: surfaced as land offer | {BuildLandOfferStrengthRead(plot, candidate)}";
            }

            return staleSaleFlag
                ? $"Market state: held off-market{BuildStaleLandSaleFlagReviewNote(true)} | {BuildVacantPlotTriageRead(plot, candidate)}"
                : $"Market state: held off-market | {BuildVacantPlotTriageRead(plot, candidate)}";
        }

        private string BuildBuildingHoldingRead(PlacedBuilding building, TownPlot plot)
        {
            if (building == null)
            {
                return "Building state unclear";
            }

            bool playerHeld = IsPlayerHeldSite(building, plot);
            if (IsCivicBuildingSite(building, plot))
            {
                return playerHeld ? "Player-held civic building" : "Civic building";
            }

            if (HasStaleLandSaleFlag(plot))
            {
                return playerHeld ? "Player-held improved site with stale sale flag" : "Improved site with stale sale flag";
            }

            return playerHeld ? "Player-held building" : "Town/NPC-owned building";
        }

        private string BuildBuildingSiteStateSummary(PlacedBuilding building, TownPlot plot)
        {
            if (building == null)
            {
                return string.Empty;
            }

            if (plot == null)
            {
                return "Site state: building record is detached from plot state";
            }

            bool playerHeld = IsPlayerHeldSite(building, plot);
            bool staleSaleFlag = HasStaleLandSaleFlag(plot);
            if (IsCivicBuildingSite(building, plot))
            {
                string civicRead = playerHeld ? "player-held civic site should be reviewed" : "civic holding";
                return staleSaleFlag
                    ? $"Site state: {civicRead}{BuildStaleLandSaleFlagReviewNote(true)}"
                    : $"Site state: {civicRead}";
            }

            string baseRead;
            if (plot.zone == PlotZone.Agricultural)
            {
                baseRead = building.definition != null && building.definition.HasDedicatedAgriculturalRole
                    ? "working-yard shell on agricultural parcel"
                    : "adapted shell on agricultural parcel";
            }
            else if (building.definition != null && building.definition.IsMixedUse)
            {
                baseRead = "mixed-use improved site";
            }
            else if (building.definition != null && building.definition.CanHostWorkplace)
            {
                baseRead = plot.zone == PlotZone.Residential ? "trade conversion on domestic parcel" : "trade shell in active use";
            }
            else
            {
                baseRead = plot.zone == PlotZone.Business ? "domestic hold on trade frontage" : "domestic improved site";
            }

            string holderRead = playerHeld ? "player-held" : "town-held";
            return staleSaleFlag
                ? $"Site state: {holderRead} {baseRead}{BuildStaleLandSaleFlagReviewNote(true)}"
                : $"Site state: {holderRead} {baseRead}";
        }

        private void ValidatePlotHoldingStateConsistency(TownPlot plot, StringBuilder report, ref int warnings)
        {
            if (plot == null)
            {
                return;
            }

            if (plot.playerOwned && plot.reservedForLandSale)
            {
                AppendWarning(report, ref warnings, $"Plot {plot.id} is player-owned but still flagged as reserved for public land sale.");
            }

            if (IsCivicParcel(plot) && plot.reservedForLandSale)
            {
                AppendWarning(report, ref warnings, $"Plot {plot.id} is a {FormatPublicSiteRole(plot.publicSiteRole)} but is still flagged for land sale.");
            }

            if (IsCivicParcel(plot) && plot.playerOwned)
            {
                AppendWarning(report, ref warnings, $"Plot {plot.id} is a {FormatPublicSiteRole(plot.publicSiteRole)} but is marked player-owned.");
            }

            if (plot.reservedForLandSale && HasStaleLandSaleFlag(plot))
            {
                AppendStaleLandSaleFlagWarning(report, ref warnings, $"Plot {plot.id}");
            }
        }

        private void ValidateBuildingHoldingStateConsistency(PlacedBuilding building, TownPlot plot, StringBuilder report, ref int warnings)
        {
            if (building == null || plot == null)
            {
                return;
            }

            if (building.publicSiteRole != plot.publicSiteRole)
            {
                string buildingRole = building.publicSiteRole == PublicSiteRole.None ? "no public-site role" : FormatPublicSiteRole(building.publicSiteRole);
                string plotRole = plot.publicSiteRole == PublicSiteRole.None ? "no public-site role" : FormatPublicSiteRole(plot.publicSiteRole);
                AppendWarning(report, ref warnings, $"Building {building.id} and Plot {plot.id} disagree on public-site role ({buildingRole} vs {plotRole}).");
            }

            if (building.playerOwned && plot.reservedForLandSale)
            {
                AppendWarning(report, ref warnings, $"Building {building.id} is player-owned but Plot {plot.id} still carries a public land-sale flag.");
            }

            if (building.publicSiteRole != PublicSiteRole.None && building.playerOwned)
            {
                AppendWarning(report, ref warnings, $"Building {building.id} is a {FormatPublicSiteRole(building.publicSiteRole)} but is marked player-owned.");
            }

            if (HasStaleLandSaleFlag(plot))
            {
                AppendStaleLandSaleFlagWarning(report, ref warnings, $"Building {building.id} on Plot {plot.id}");
            }
        }

        private string BuildImprovementPotentialSummary(TownPlot plot, PlacedBuilding building)
        {
            if (plot == null || building == null || building.definition == null)
            {
                return string.Empty;
            }

            CountCompatibleDefinitionsForPlot(
                plot,
                building.definition,
                out int totalAlternatives,
                out int workplaceAlternatives,
                out int householdAlternatives,
                out int mixedUseAlternatives,
                out int dedicatedAgriculturalAlternatives,
                out int fallbackAgriculturalAlternatives,
                out int largerAlternatives);

            ComputePlotFitMetrics(plot, building, out _, out _, out int frontageHeadroom, out int depthHeadroom, out float openRatio);
            string spaceRead = openRatio >= 0.40f
                ? "support ground remains generous"
                : openRatio >= 0.22f
                    ? "support ground remains workable"
                    : "site is already mostly spoken for";

            string changeRead;
            if (plot.zone == PlotZone.Agricultural)
            {
                if (building.definition.HasDedicatedAgriculturalRole)
                {
                    changeRead = largerAlternatives > 0 && openRatio >= 0.22f
                        ? "a larger work-shell path remains possible"
                        : dedicatedAgriculturalAlternatives > 0 || fallbackAgriculturalAlternatives > 0
                            ? "yard-side refit paths remain open"
                            : "best gains now come from yard use, not shell swaps";
                }
                else
                {
                    changeRead = dedicatedAgriculturalAlternatives > 0
                        ? "the parcel could still be pulled back toward a role-specific work shell"
                        : totalAlternatives > 0
                            ? "some work-shell refit paths remain open"
                            : "this parcel is already in a narrow adaptation lane";
                }
            }
            else if (plot.zone == PlotZone.Business || plot.zone == PlotZone.MixedUse)
            {
                changeRead = mixedUseAlternatives > 0 && !building.definition.IsMixedUse && !building.definition.UsesUpperFloorResidential
                    ? "mixed-use upgrade paths remain open"
                    : largerAlternatives > 0 && openRatio >= 0.18f
                        ? "a stronger frontage shell remains possible"
                        : totalAlternatives > 0
                            ? "refit and conversion paths remain open"
                            : "current shell already fills most practical frontage options";
            }
            else
            {
                changeRead = largerAlternatives > 0 && openRatio >= 0.18f
                    ? "a larger household shell remains possible"
                    : workplaceAlternatives > 0
                        ? "light conversion paths remain available"
                        : totalAlternatives > 0
                            ? "some house-lot refit paths remain open"
                            : "current shell already sits near the practical fit lane";
            }

            string pressureRead = BuildImprovedSitePressureRead(
                plot,
                building,
                totalAlternatives,
                workplaceAlternatives,
                mixedUseAlternatives,
                dedicatedAgriculturalAlternatives,
                largerAlternatives,
                frontageHeadroom,
                depthHeadroom,
                openRatio);

            string alternativesRead = totalAlternatives > 0
                ? $"{totalAlternatives} alternate shell fit{(totalAlternatives == 1 ? string.Empty : "s")}"
                : "no alternate shell fits";
            return $"Improvement read: {spaceRead} | {changeRead} | {pressureRead} | {alternativesRead}";
        }

        private string BuildImprovedSitePressureRead(
            TownPlot plot,
            PlacedBuilding building,
            int totalAlternatives,
            int workplaceAlternatives,
            int mixedUseAlternatives,
            int dedicatedAgriculturalAlternatives,
            int largerAlternatives,
            int frontageHeadroom,
            int depthHeadroom,
            float openRatio)
        {
            ImprovedSiteTriage triage = ClassifyImprovedSiteTriage(
                plot,
                building,
                totalAlternatives,
                workplaceAlternatives,
                mixedUseAlternatives,
                dedicatedAgriculturalAlternatives,
                largerAlternatives,
                frontageHeadroom,
                depthHeadroom,
                openRatio);

            return triage switch
            {
                ImprovedSiteTriage.MixedUseUpgrade => "pressure points toward a mixed-use upgrade",
                ImprovedSiteTriage.FrontageRefit => "frontage wants a stronger refit lane",
                ImprovedSiteTriage.YardRecovery => "parcel wants support ground recovered",
                ImprovedSiteTriage.AgriculturalRoleRecovery => "working parcel wants role-specific shell recovery",
                ImprovedSiteTriage.StretchedAdaptation => "current shell reads like a stretched adaptation",
                ImprovedSiteTriage.CrampedHold => "site feels cramped and under fit pressure",
                ImprovedSiteTriage.NarrowLockedFit => "site is drifting into a narrow fit lane",
                _ => "pressure stays low; current siting reads natural"
            };
        }

        // This triage is diagnostic, not prescriptive. It helps inspection and validation explain why a site feels settled, stretched, or ready for later refit.
        private ImprovedSiteTriage ClassifyImprovedSiteTriage(
            TownPlot plot,
            PlacedBuilding building,
            int totalAlternatives,
            int workplaceAlternatives,
            int mixedUseAlternatives,
            int dedicatedAgriculturalAlternatives,
            int largerAlternatives,
            int frontageHeadroom,
            int depthHeadroom,
            float openRatio)
        {
            if (plot == null || building == null || building.definition == null)
            {
                return ImprovedSiteTriage.NaturalFit;
            }

            bool tightYard = building.definition.PrefersDeepYard && depthHeadroom < 2 && openRatio < 0.30f;
            bool crampedFrontage = building.definition.PrefersBroadFrontage && frontageHeadroom < 0;

            if (plot.zone == PlotZone.Agricultural)
            {
                if (!building.definition.HasDedicatedAgriculturalRole && dedicatedAgriculturalAlternatives > 0)
                {
                    return ImprovedSiteTriage.AgriculturalRoleRecovery;
                }

                if (tightYard || (!building.definition.HasDedicatedAgriculturalRole && openRatio < 0.22f))
                {
                    return ImprovedSiteTriage.YardRecovery;
                }

                if (totalAlternatives <= 1 && openRatio < 0.18f)
                {
                    return ImprovedSiteTriage.NarrowLockedFit;
                }

                return ImprovedSiteTriage.NaturalFit;
            }

            if (crampedFrontage)
            {
                return ImprovedSiteTriage.CrampedHold;
            }

            if (plot.zone == PlotZone.Business || plot.zone == PlotZone.MixedUse)
            {
                if (mixedUseAlternatives > 0 && !building.definition.IsMixedUse && !building.definition.UsesUpperFloorResidential)
                {
                    return ImprovedSiteTriage.MixedUseUpgrade;
                }

                if ((!building.definition.CanHostWorkplace && workplaceAlternatives > 0) || (largerAlternatives > 0 && openRatio >= 0.18f))
                {
                    return ImprovedSiteTriage.FrontageRefit;
                }

                if (building.playerOwned && !building.definition.CanHostWorkplace)
                {
                    return ImprovedSiteTriage.StretchedAdaptation;
                }

                if (totalAlternatives <= 1 || openRatio < 0.12f)
                {
                    return ImprovedSiteTriage.NarrowLockedFit;
                }

                return ImprovedSiteTriage.NaturalFit;
            }

            if (plot.zone == PlotZone.Residential)
            {
                if (tightYard)
                {
                    return ImprovedSiteTriage.YardRecovery;
                }

                if (building.definition.CanHostWorkplace && !building.definition.CanHostHouseholds)
                {
                    return building.playerOwned
                        ? ImprovedSiteTriage.StretchedAdaptation
                        : ImprovedSiteTriage.FrontageRefit;
                }

                if (totalAlternatives <= 1 && openRatio < 0.18f)
                {
                    return ImprovedSiteTriage.NarrowLockedFit;
                }
            }

            return ImprovedSiteTriage.NaturalFit;
        }

        private string BuildPlotOpportunityLaneRead(
            TownPlot plot,
            int workplaceFits,
            int householdFits,
            int mixedUseFits,
            int dedicatedAgriculturalFits,
            int fallbackAgriculturalFits)
        {
            if (plot == null)
            {
                return "opportunity lane unclear";
            }

            return plot.zone switch
            {
                PlotZone.Agricultural => dedicatedAgriculturalFits > 0
                    ? $"strongest as a {FormatAgriculturalSiteRole(plot.agriculturalSiteRole).ToLowerInvariant()}"
                    : fallbackAgriculturalFits > 0
                        ? "leans on a generic work-shell fallback"
                        : "would need a weak conversion-style work shell",
                PlotZone.Business => mixedUseFits > 0
                    ? "strongest as trade frontage with mixed-use upside"
                    : workplaceFits > 0
                        ? "strongest as compact trade infill"
                        : householdFits > 0
                            ? "leans toward a cautious domestic hold"
                            : "current catalog does not want this trade frontage",
                PlotZone.MixedUse => mixedUseFits > 0
                    ? "strongest as mixed-use frontage"
                    : workplaceFits > householdFits
                        ? "leans trade-first on a flexible lot"
                        : householdFits > 0
                            ? "leans domestic with conversion potential"
                            : "current catalog fit stays thin",
                PlotZone.Residential => householdFits > 0
                    ? "strongest as a practical household lot"
                    : workplaceFits > 0
                        ? "only supports light conversion-style use"
                        : "current catalog does not want this domestic lot",
                _ => "opportunity lane unclear"
            };
        }

        private string BuildLandOfferStrengthRead(TownPlot plot, LandOfferCandidate candidate)
        {
            if (plot == null)
            {
                return "offer strength unclear";
            }

            if (plot.zone == PlotZone.Agricultural)
            {
                return candidate.dedicatedAgriculturalFits > 0
                    ? "role-specific work parcel"
                    : candidate.fallbackAgriculturalFits > 0
                        ? "generic work parcel"
                        : "weak work parcel";
            }

            return candidate.strength switch
            {
                LandOfferStrength.Prime => candidate.frontagePriority ? "prime frontage offer" : "prime lot offer",
                LandOfferStrength.Strong => candidate.frontagePriority
                    ? "strong frontage offer"
                    : candidate.domesticPriority
                        ? "strong domestic lot"
                        : "strong infill offer",
                LandOfferStrength.Practical => candidate.domesticPriority
                    ? "practical household lot"
                    : "practical infill offer",
                LandOfferStrength.Speculative => "speculative hold with some upside",
                _ => "weak leftover inventory"
            };
        }

        private string BuildVacantPlotFrontageUseRead(TownPlot plot, LandOfferCandidate candidate)
        {
            if (plot == null)
            {
                return "frontage use unclear";
            }

            return plot.zone switch
            {
                PlotZone.Agricultural => candidate.dedicatedAgriculturalFits > 0
                    ? plot.frontageCells >= 8
                        ? "frontage reads as roomy working access"
                        : plot.frontageCells >= 6
                            ? "frontage reads as practical working access"
                            : "frontage keeps working access tight"
                    : candidate.fallbackAgriculturalFits > 0
                        ? "frontage supports only a generic work-yard face"
                        : "frontage does not read as a convincing work yard",
                PlotZone.Business => candidate.frontagePriority && candidate.strength >= LandOfferStrength.Strong
                    ? "frontage can carry a credible main-street trade face"
                    : candidate.workplaceFits > 0
                        ? plot.frontageCells >= 6
                            ? "frontage can still support secondary trade infill"
                            : "frontage is thin for a convincing trade face"
                        : candidate.householdFits > 0
                            ? "frontage reads more like a cautious domestic hold"
                            : "frontage use stays weak",
                PlotZone.MixedUse => candidate.mixedUseFits > 0
                    ? plot.frontageCells >= 7
                        ? "frontage can carry shop-below mixed-use"
                        : "frontage supports only compact mixed-use"
                    : candidate.workplaceFits > candidate.householdFits
                        ? "frontage leans trade-first but stays modest"
                        : candidate.householdFits > 0
                            ? "frontage reads more domestic than mercantile"
                            : "frontage use stays awkward",
                PlotZone.Residential => candidate.householdFits > 0
                    ? plot.frontageCells >= 5
                        ? "frontage reads as a practical house-lot address"
                        : "frontage leaves a tight house-lot address"
                    : candidate.workplaceFits > 0
                        ? "frontage only works through light conversion"
                        : "frontage use stays weak",
                _ => "frontage use unclear"
            };
        }

        private string BuildVacantPlotTriageRead(TownPlot plot, LandOfferCandidate candidate)
        {
            VacantInventoryTriage triage = ClassifyVacantInventoryTriage(plot, candidate);
            return triage switch
            {
                VacantInventoryTriage.PlayerHold => "player hold for later use",
                VacantInventoryTriage.ActiveMarketOffer => candidate.strength >= LandOfferStrength.Strong
                    ? "surfaced now as a near-term anchor offer"
                    : candidate.strength >= LandOfferStrength.Practical
                        ? "surfaced now as practical market stock"
                        : candidate.strength == LandOfferStrength.Speculative
                            ? "surfaced now as speculative upside"
                            : "surfaced now despite thin fit",
                VacantInventoryTriage.WorkingReserve => candidate.dedicatedAgriculturalFits > 0
                    ? "held as a role-specific work reserve"
                    : "held as a generic work reserve",
                VacantInventoryTriage.FrontageReserve => "held as strategic frontage reserve",
                VacantInventoryTriage.DomesticReserve => "held as a practical domestic reserve",
                VacantInventoryTriage.FutureReserve => "held as future reserve with usable upside",
                VacantInventoryTriage.SpeculativeHold => "held as speculative future stock",
                _ => "reads as weak leftover stock"
            };
        }

        private VacantInventoryTriage ClassifyVacantInventoryTriage(TownPlot plot, LandOfferCandidate candidate)
        {
            if (plot == null)
            {
                return VacantInventoryTriage.WeakLeftover;
            }

            if (plot.playerOwned)
            {
                return VacantInventoryTriage.PlayerHold;
            }

            if (plot.reservedForLandSale)
            {
                return VacantInventoryTriage.ActiveMarketOffer;
            }

            if (plot.zone == PlotZone.Agricultural)
            {
                return candidate.dedicatedAgriculturalFits > 0 || candidate.fallbackAgriculturalFits > 0
                    ? VacantInventoryTriage.WorkingReserve
                    : VacantInventoryTriage.WeakLeftover;
            }

            if (candidate.frontagePriority && candidate.strength >= LandOfferStrength.Strong)
            {
                return VacantInventoryTriage.FrontageReserve;
            }

            if (candidate.domesticPriority && candidate.strength >= LandOfferStrength.Practical)
            {
                return VacantInventoryTriage.DomesticReserve;
            }

            if (candidate.strength >= LandOfferStrength.Practical)
            {
                return VacantInventoryTriage.FutureReserve;
            }

            return candidate.strength == LandOfferStrength.Speculative
                ? VacantInventoryTriage.SpeculativeHold
                : VacantInventoryTriage.WeakLeftover;
        }

        private static bool IsWeakFrontageLeftover(TownPlot plot, LandOfferCandidate candidate)
        {
            if (plot == null || plot.buildingId >= 0)
            {
                return false;
            }

            if (plot.zone != PlotZone.Business && plot.zone != PlotZone.MixedUse)
            {
                return false;
            }

            if (candidate.strength != LandOfferStrength.Weak)
            {
                return false;
            }

            return plot.frontageCells >= 5 || candidate.householdFits <= 0;
        }

        // This stays intentionally heuristic. It ranks near-term land-offer usefulness for inspection and reservation without pretending to be the full acquisition economy.
        private LandOfferCandidate EvaluateLandOfferCandidate(TownPlot plot)
        {
            CountCompatibleDefinitionsForPlot(
                plot,
                null,
                out int totalFits,
                out int workplaceFits,
                out int householdFits,
                out int mixedUseFits,
                out int dedicatedAgriculturalFits,
                out int fallbackAgriculturalFits,
                out _);

            float openRatio = GetPlotOpenRatio(plot, null);
            bool frontagePriority = (plot != null)
                && (plot.zone == PlotZone.Business || plot.zone == PlotZone.MixedUse)
                && plot.frontageCells >= 6
                && (workplaceFits > 0 || mixedUseFits > 0);
            bool domesticPriority = plot != null
                && plot.zone == PlotZone.Residential
                && householdFits > 0;

            int score = plot == null ? int.MinValue : plot.zone switch
            {
                PlotZone.Business => 34,
                PlotZone.MixedUse => 31,
                PlotZone.Residential => 18,
                PlotZone.Agricultural => 8,
                _ => 0
            };

            score += plot != null ? Mathf.Clamp(plot.frontageCells, 0, 12) * (frontagePriority ? 4 : 2) : 0;
            score += plot != null ? Mathf.Clamp(plot.depthCells, 0, 14) : 0;
            score += openRatio >= 0.35f ? 12 : openRatio >= 0.18f ? 5 : -10;
            score += Mathf.Min(6, totalFits) * 4;
            score += Mathf.Min(4, workplaceFits) * 3;
            score += Mathf.Min(4, householdFits) * 2;
            score += Mathf.Min(3, mixedUseFits) * 4;
            score += Mathf.Min(2, dedicatedAgriculturalFits) * 6;
            score += frontagePriority ? (plot.frontageCells >= 8 ? 16 : 9) : 0;
            score += domesticPriority ? (plot.frontageCells >= 5 ? 7 : 3) : 0;

            if (plot != null && plot.zone == PlotZone.Residential && workplaceFits > 0 && householdFits <= 0)
            {
                score -= 4;
            }

            if (totalFits == 1)
            {
                score -= 8;
            }
            else if (totalFits <= 0)
            {
                score -= 1000;
            }

            LandOfferStrength strength = totalFits <= 0
                ? LandOfferStrength.Weak
                : plot != null && plot.zone == PlotZone.Agricultural
                    ? dedicatedAgriculturalFits > 0
                        ? LandOfferStrength.Strong
                        : fallbackAgriculturalFits > 0
                            ? LandOfferStrength.Practical
                            : LandOfferStrength.Weak
                    : frontagePriority && plot.frontageCells >= 8 && openRatio >= 0.18f && (mixedUseFits > 0 || workplaceFits >= 2)
                        ? LandOfferStrength.Prime
                        : frontagePriority && totalFits >= 3
                            ? LandOfferStrength.Strong
                            : domesticPriority || totalFits >= 3 || (plot != null && plot.zone == PlotZone.MixedUse && mixedUseFits > 0)
                                ? LandOfferStrength.Practical
                                : totalFits > 1
                                    ? LandOfferStrength.Speculative
                                    : LandOfferStrength.Weak;

            return new LandOfferCandidate(
                plot,
                score,
                strength,
                frontagePriority,
                domesticPriority,
                totalFits,
                workplaceFits,
                householdFits,
                mixedUseFits,
                dedicatedAgriculturalFits,
                fallbackAgriculturalFits);
        }

        private BuildingDefinition GetBestOpportunityDefinitionForPlot(TownPlot plot)
        {
            IReadOnlyList<BuildingDefinition> catalog = GetBuildingCatalog();
            if (plot == null || catalog == null || catalog.Count == 0)
            {
                return null;
            }

            return PickDefinitionForPlot(catalog, plot, plot.id);
        }

        private void CountCompatibleDefinitionsForPlot(
            TownPlot plot,
            BuildingDefinition excludeDefinition,
            out int totalFits,
            out int workplaceFits,
            out int householdFits,
            out int mixedUseFits,
            out int dedicatedAgriculturalFits,
            out int fallbackAgriculturalFits,
            out int largerFits)
        {
            totalFits = 0;
            workplaceFits = 0;
            householdFits = 0;
            mixedUseFits = 0;
            dedicatedAgriculturalFits = 0;
            fallbackAgriculturalFits = 0;
            largerFits = 0;

            IReadOnlyList<BuildingDefinition> catalog = GetBuildingCatalog();
            if (plot == null || catalog == null || catalog.Count == 0)
            {
                return;
            }

            Vector2Int currentPlacementSize = excludeDefinition != null ? ResolvePlacementFootprintSizeCells(excludeDefinition) : Vector2Int.zero;
            int currentArea = excludeDefinition != null
                ? Mathf.Max(1, currentPlacementSize.x * currentPlacementSize.y)
                : 0;

            for (int i = 0; i < catalog.Count; i++)
            {
                BuildingDefinition definition = catalog[i];
                if (definition == null || definition == excludeDefinition || definition.PrimaryUse == BuildingUseType.Civic)
                {
                    continue;
                }

                bool fits = plot.zone == PlotZone.Agricultural
                    ? ScoreAgriculturalDefinitionForPlot(definition, plot) > int.MinValue
                    : definition.CanUsePlot(plot.zone) && DefinitionFitsPlot(plot, definition);
                if (!fits)
                {
                    continue;
                }

                totalFits++;
                if (definition.CanHostWorkplace)
                {
                    workplaceFits++;
                }

                if (definition.CanHostHouseholds)
                {
                    householdFits++;
                }

                if (definition.IsMixedUse || definition.UsesUpperFloorResidential)
                {
                    mixedUseFits++;
                }

                if (plot.zone == PlotZone.Agricultural)
                {
                    if (definition.MatchesAgriculturalSiteRole(plot.agriculturalSiteRole))
                    {
                        dedicatedAgriculturalFits++;
                    }
                    else if (definition.IsFallbackAgriculturalFit(plot.agriculturalSiteRole))
                    {
                        fallbackAgriculturalFits++;
                    }
                }

                if (currentArea > 0)
                {
                    Vector2Int candidatePlacementSize = ResolvePlacementFootprintSizeCells(definition);
                    int area = Mathf.Max(1, candidatePlacementSize.x * candidatePlacementSize.y);
                    if (area > currentArea)
                    {
                        largerFits++;
                    }
                }
            }
        }

        private void ValidateVacantPlotOpportunity(TownPlot plot, StringBuilder report, ref int errors, ref int warnings)
        {
            if (plot == null)
            {
                return;
            }

            if (plot.buildingId >= 0)
            {
                return;
            }

            LandOfferCandidate candidate = EvaluateLandOfferCandidate(plot);
            if (candidate.totalFits <= 0)
            {
                string lane = BuildPlotOpportunityLaneRead(
                    plot,
                    candidate.workplaceFits,
                    candidate.householdFits,
                    candidate.mixedUseFits,
                    candidate.dedicatedAgriculturalFits,
                    candidate.fallbackAgriculturalFits);
                if (plot.reservedForLandSale)
                {
                    AppendWarning(report, ref warnings, $"Plot {plot.id} is surfaced as a vacant land offer but has no current shell fit: {lane}.");
                }
                else if (!plot.playerOwned)
                {
                    AppendWarning(report, ref warnings, $"Plot {plot.id} is vacant but has no current shell fit: {lane}.");
                }

                return;
            }

            if (plot.reservedForLandSale && candidate.totalFits == 1)
            {
                AppendWarning(report, ref warnings, $"Plot {plot.id} is surfaced as land for sale but only has one practical shell fit in the current catalog.");
            }

            if (plot.reservedForLandSale && candidate.strength == LandOfferStrength.Weak)
            {
                AppendWarning(report, ref warnings, $"Plot {plot.id} is surfaced as land for sale but currently reads as weak leftover inventory.");
            }
        }

        private string BuildPlotDevelopmentReadSummary(TownPlot plot)
        {
            if (plot == null)
            {
                return string.Empty;
            }

            float openRatio = GetPlotOpenRatio(plot, null);
            string frontageRead = plot.zone switch
            {
                PlotZone.Agricultural => plot.frontageCells >= 8 ? "working frontage with wagon room" : plot.frontageCells >= 6 ? "compact working frontage" : "tight working frontage",
                PlotZone.Business => plot.frontageCells >= 8 ? "trade frontage ready" : plot.frontageCells >= 6 ? "compact trade frontage" : "thin trade frontage",
                PlotZone.MixedUse => plot.frontageCells >= 8 ? "mixed-use frontage ready" : plot.frontageCells >= 6 ? "compact mixed-use frontage" : "thin mixed-use frontage",
                PlotZone.Residential => plot.frontageCells >= 7 ? "domestic frontage with room to read" : plot.frontageCells >= 5 ? "compact domestic frontage" : "tight domestic frontage",
                _ => "frontage read uncertain"
            };
            string groundRead = plot.zone == PlotZone.Agricultural
                ? openRatio >= 0.45f ? "working ground generous" : openRatio >= 0.30f ? "working ground usable" : "working ground tight"
                : openRatio >= 0.35f ? "reserve ground available" : openRatio >= 0.18f ? "site coverage balanced" : "little reserve ground";
            string conversionRead = plot.zone switch
            {
                PlotZone.Business => "best for frontage-first trade or service use",
                PlotZone.MixedUse => "supports sensible mixed-use or conversion work",
                PlotZone.Residential => "best for domestic use with light conversion potential",
                PlotZone.Agricultural => plot.agriculturalSiteRole == AgriculturalSiteRole.None
                    ? "best for a practical working yard"
                    : $"best when the parcel stays a {FormatAgriculturalSiteRole(plot.agriculturalSiteRole).ToLowerInvariant()}",
                _ => "siting logic still looks loose"
            };
            return $"Development read: {frontageRead} | {groundRead} | {conversionRead}";
        }

        private string BuildBuildingSiteFitSummary(TownPlot plot, PlacedBuilding building)
        {
            if (plot == null || building == null || building.definition == null)
            {
                return string.Empty;
            }

            ComputePlotFitMetrics(plot, building, out int frontageSpan, out int depthSpan, out int frontageHeadroom, out int depthHeadroom, out float openRatio);
            string frontageRead;
            if (building.definition.PrefersBroadFrontage)
            {
                frontageRead = frontageHeadroom >= 2
                    ? "frontage fit comfortable"
                    : frontageHeadroom >= 0
                        ? "frontage fit workable"
                        : "frontage fit cramped";
            }
            else if (building.definition.HasDedicatedAgriculturalRole)
            {
                frontageRead = frontageHeadroom >= 0
                    ? "frontage stays secondary to yard use"
                    : "frontage pinched for a work yard";
            }
            else
            {
                frontageRead = frontageHeadroom >= 0
                    ? "frontage fit balanced"
                    : "frontage fit tight";
            }

            string groundRead;
            if (building.definition.PrefersDeepYard)
            {
                groundRead = depthHeadroom >= 4 || openRatio >= 0.45f
                    ? "yard room strong"
                    : depthHeadroom >= 2 || openRatio >= 0.30f
                        ? "yard room workable"
                        : "yard room tight";
            }
            else if (building.definition.ToleratesTightPad)
            {
                groundRead = openRatio <= 0.18f
                    ? "pad reads efficient"
                    : openRatio <= 0.40f
                        ? "pad reads balanced"
                        : "site leaves support room";
            }
            else
            {
                groundRead = openRatio >= 0.35f
                    ? "site leaves support ground"
                    : openRatio >= 0.20f
                        ? "site reads balanced"
                        : "site reads built-out";
            }

            string contextRead = BuildBuildingContextRead(plot, building);
            return $"Site fit: {frontageRead} | {groundRead} | {contextRead}";
        }

        private string BuildBuildingContextRead(TownPlot plot, PlacedBuilding building)
        {
            if (plot == null || building == null || building.definition == null)
            {
                return string.Empty;
            }

            if (plot.zone == PlotZone.Agricultural)
            {
                if (building.definition.HasDedicatedAgriculturalRole)
                {
                    return "working-yard shell matches parcel intent";
                }

                return building.playerOwned
                    ? "adapted conversion on a working parcel"
                    : "generic work shell adapted onto a yard parcel";
            }

            if (plot.zone == PlotZone.MixedUse)
            {
                return building.definition.IsMixedUse
                    ? "mixed-use read fits the lot"
                    : building.definition.CanHostWorkplace
                        ? "trade use fits a flexible frontage"
                        : "domestic hold fits a flexible frontage";
            }

            if (plot.zone == PlotZone.Business)
            {
                if (building.definition.UsesUpperFloorResidential)
                {
                    return "upper-floor household keeps the trade frontage grounded";
                }

                return building.definition.CanHostWorkplace
                    ? "trade frontage fit reads natural"
                    : building.playerOwned
                        ? "conversion-style domestic hold on trade frontage"
                        : "domestic shell reads quiet for trade frontage";
            }

            if (plot.zone == PlotZone.Residential)
            {
                return building.definition.CanHostWorkplace
                    ? building.playerOwned
                        ? "small conversion read on domestic frontage"
                        : "trade shell reads assertive on domestic frontage"
                    : "domestic siting reads natural";
            }

            return "site context still reads loose";
        }

        private void ValidateFootprintReconciliation(TownPlot plot, PlacedBuilding building, StringBuilder report, ref int warnings)
        {
            if (building == null || building.definition == null)
            {
                return;
            }

            PlacementFootprintResolution resolution = ResolvePlacementFootprint(building.definition);
            Vector2Int expectedWorldSize = GetPlacementWorldFootprintSize(building.definition, building.frontageDirection);
            bool preservedLegacyFootprint = building.loadedFromSave && building.savedFootprintPreservedAfterAuthorityMismatch;

            if (building.intendedFootprintSizeCells != resolution.resolvedSizeCells)
            {
                string reason = preservedLegacyFootprint
                    ? "saved legacy footprint is intentionally preserved because the current authority did not fit this plot"
                    : "intended footprint is stale against current placement authority";
                AppendWarning(report, ref warnings, $"Building {building.id} intended footprint {building.intendedFootprintSizeCells.x}x{building.intendedFootprintSizeCells.y} differs from current placement authority {resolution.resolvedSizeCells.x}x{resolution.resolvedSizeCells.y}; {reason}.");
            }

            if (building.footprint.IsValid
                && (building.footprint.width != expectedWorldSize.x || building.footprint.depth != expectedWorldSize.y)
                && !preservedLegacyFootprint)
            {
                AppendWarning(report, ref warnings, $"Building {building.id} world footprint {building.footprint.width}x{building.footprint.depth} does not match rotated current placement authority {expectedWorldSize.x}x{expectedWorldSize.y}.");
            }

            if (building.definitionFootprintSizeCells != resolution.definitionSizeCells
                || building.footprintAuthorityMinimumSizeCells != resolution.authorityMinimumSizeCells
                || building.resolvedPlacementFootprintSizeCells != resolution.resolvedSizeCells
                || building.hasPrefabFootprintAuthority != resolution.hasPrefabAuthority
                || building.footprintExpandedByAuthority != resolution.expandedByAuthority)
            {
                AppendWarning(report, ref warnings, $"Building {building.id} footprint authority diagnostics are stale; regenerate or reload the world to refresh placement-authority reads.");
            }

            BuildingFootprintAuthority authority = ResolveFootprintAuthority(building.definition);
            if (authority != null && authority.TryBuildAuthoringWarningSummary(out string warningSummary))
            {
                AppendWarning(report, ref warnings, $"Building {building.id} prefab footprint authority warning: {warningSummary}.");
            }

            if (building.loadedFromSave && building.footprintRebuiltFromCurrentAuthorityOnLoad && building.savedAnchorCount > 0)
            {
                AppendWarning(report, ref warnings, $"Building {building.id} was rebuilt to current placement authority on load; saved anchors were regenerated from current authoring.");
            }

            if (building.loadedFromSave && building.savedFootprintPreservedAfterAuthorityMismatch && building.savedAnchorCount > 0)
            {
                AppendWarning(report, ref warnings, $"Building {building.id} preserved saved footprint and anchors after a placement-authority mismatch: {building.footprintReconciliationNote}");
            }
        }

        private void ValidateBuildingSiteFit(TownPlot plot, PlacedBuilding building, StringBuilder report, ref int warnings)
        {
            if (plot == null || building == null || building.definition == null)
            {
                return;
            }

            ComputePlotFitMetrics(plot, building, out int frontageSpan, out int depthSpan, out int frontageHeadroom, out int depthHeadroom, out float openRatio);

            if (building.definition.PrefersBroadFrontage && frontageHeadroom < 0)
            {
                string severity = building.playerOwned ? "reads like a cramped conversion" : "looks cramped for generated frontage use";
                AppendWarning(report, ref warnings, $"Building {building.id} frontage span {frontageSpan} exceeds Plot {plot.id}'s frontage {plot.frontageCells}; it {severity}.");
            }

            bool tightYard = depthHeadroom < 2 && openRatio < 0.30f;
            if (building.definition.PrefersDeepYard && tightYard)
            {
                string severity = building.playerOwned ? "reads like a squeezed yard conversion" : "looks tight for its yard demand";
                AppendWarning(report, ref warnings, $"Building {building.id} leaves little support ground on Plot {plot.id}; it {severity}.");
            }

            if (plot.zone == PlotZone.Agricultural && !building.definition.HasDedicatedAgriculturalRole && openRatio < 0.20f)
            {
                AppendWarning(report, ref warnings, $"Building {building.id} uses most of agricultural Plot {plot.id}; the parcel no longer reads like a generous working yard.");
            }
        }

        private void ValidateImprovedSitePressure(TownPlot plot, PlacedBuilding building, StringBuilder report, ref int warnings)
        {
            if (plot == null || building == null || building.definition == null)
            {
                return;
            }

            CountCompatibleDefinitionsForPlot(
                plot,
                building.definition,
                out int totalAlternatives,
                out int workplaceAlternatives,
                out _,
                out int mixedUseAlternatives,
                out int dedicatedAgriculturalAlternatives,
                out _,
                out int largerAlternatives);

            ComputePlotFitMetrics(plot, building, out _, out _, out int frontageHeadroom, out int depthHeadroom, out float openRatio);
            ImprovedSiteTriage triage = ClassifyImprovedSiteTriage(
                plot,
                building,
                totalAlternatives,
                workplaceAlternatives,
                mixedUseAlternatives,
                dedicatedAgriculturalAlternatives,
                largerAlternatives,
                frontageHeadroom,
                depthHeadroom,
                openRatio);

            bool playerOwned = building.playerOwned || plot.playerOwned;
            switch (triage)
            {
                case ImprovedSiteTriage.MixedUseUpgrade:
                    AppendWarning(report, ref warnings, $"Building {building.id} on Plot {plot.id} has mixed-use upgrade headroom in the current catalog; the frontage may be underusing a flexible site.");
                    break;
                case ImprovedSiteTriage.FrontageRefit:
                    AppendWarning(report, ref warnings, $"Building {building.id} on Plot {plot.id} reads below the stronger frontage fit lane available in the current catalog.");
                    break;
                case ImprovedSiteTriage.YardRecovery:
                    AppendWarning(report, ref warnings, $"Building {building.id} on Plot {plot.id} leaves too little support ground for how the parcel wants to read.");
                    break;
                case ImprovedSiteTriage.AgriculturalRoleRecovery:
                    AppendWarning(report, ref warnings, $"Building {building.id} on agricultural Plot {plot.id} has role-specific shell recovery available but still reads as a generic adaptation.");
                    break;
                case ImprovedSiteTriage.StretchedAdaptation:
                    if (!playerOwned)
                    {
                        AppendWarning(report, ref warnings, $"Building {building.id} on Plot {plot.id} reads like a stretched adaptation rather than a settled natural fit.");
                    }
                    break;
                case ImprovedSiteTriage.CrampedHold:
                    AppendWarning(report, ref warnings, $"Building {building.id} on Plot {plot.id} feels cramped for its frontage-facing shell intent.");
                    break;
                case ImprovedSiteTriage.NarrowLockedFit:
                    if (!playerOwned)
                    {
                        AppendWarning(report, ref warnings, $"Building {building.id} on Plot {plot.id} is slipping into a narrow fit lane with little practical refit headroom.");
                    }
                    break;
            }
        }

        private void ComputePlotFitMetrics(TownPlot plot, PlacedBuilding building, out int frontageSpan, out int depthSpan, out int frontageHeadroom, out int depthHeadroom, out float openRatio)
        {
            frontageSpan = 0;
            depthSpan = 0;
            frontageHeadroom = 0;
            depthHeadroom = 0;
            openRatio = 0f;

            if (plot == null)
            {
                return;
            }

            frontageSpan = building != null ? GetRectSpanAlongDirection(building.footprint, building.frontageDirection) : 0;
            depthSpan = building != null ? GetRectSpanAlongDirection(building.footprint, Opposite(building.frontageDirection)) : 0;
            frontageHeadroom = plot.frontageCells - frontageSpan;
            depthHeadroom = plot.depthCells - depthSpan;
            openRatio = GetPlotOpenRatio(plot, building);
        }

        private static int GetRectSpanAlongDirection(GridRect rect, GridDirection direction)
        {
            return direction == GridDirection.North || direction == GridDirection.South
                ? rect.width
                : rect.depth;
        }

        private float GetPlotOpenRatio(TownPlot plot, PlacedBuilding building)
        {
            if (plot == null)
            {
                return 0f;
            }

            int occupiedArea = 0;
            if (building != null)
            {
                occupiedArea = building.footprint.Area;
            }
            else if (plot.buildingId >= 0 && TryGetBuilding(plot.buildingId, out PlacedBuilding placed) && placed != null)
            {
                occupiedArea = placed.footprint.Area;
            }
            else if (plot.intendedBuildingFootprintCells.x > 0 && plot.intendedBuildingFootprintCells.y > 0)
            {
                occupiedArea = plot.intendedBuildingFootprintCells.x * plot.intendedBuildingFootprintCells.y;
            }
            else if (plot.candidateFootprint.IsValid)
            {
                occupiedArea = plot.candidateFootprint.Area;
            }

            int siteArea = Mathf.Max(1, plot.bounds.Area);
            int openArea = Mathf.Clamp(siteArea - Mathf.Max(0, occupiedArea), 0, siteArea);
            return openArea / (float)siteArea;
        }

        private string BuildAgriculturalPlotInspectionSummary(TownPlot plot)
        {
            if (plot == null || plot.zone != PlotZone.Agricultural)
            {
                return string.Empty;
            }

            string siteRole = plot.agriculturalSiteRole == AgriculturalSiteRole.None
                ? "unassigned working yard"
                : FormatAgriculturalSiteRole(plot.agriculturalSiteRole);
            string bias = plot.agriculturalSiteRole switch
            {
                AgriculturalSiteRole.CropProductionYard => "prefers crop-farm shells and open working land",
                AgriculturalSiteRole.LivestockYard => "prefers ranch shells and wider yard support",
                AgriculturalSiteRole.SawmillYard => "prefers sawmill-capable shells and lumber throughput access",
                _ => "requires a sensible frontier work shell"
            };
            return $"Site role: {siteRole} | {bias}";
        }

        private string BuildAgriculturalBuildingSummary(TownPlot plot, PlacedBuilding building)
        {
            if (plot == null || building == null || building.definition == null)
            {
                return string.Empty;
            }

            if (plot.zone != PlotZone.Agricultural && !building.definition.HasDedicatedAgriculturalRole)
            {
                return string.Empty;
            }

            return building.definition.BuildAgriculturalFitSummary(plot.agriculturalSiteRole);
        }

        private string BuildExteriorPropInspectionSummary(PlacedBuilding building)
        {
            if (building == null || building.definition == null || building.definition.VisualPrefab == null)
            {
                return string.Empty;
            }

            BuildingExteriorPropAuthoring authoring = building.definition.VisualPrefab.GetComponent<BuildingExteriorPropAuthoring>();
            if (authoring == null)
            {
                return string.Empty;
            }

            TryResolveAssignedBusinessType(building.id, out BusinessType? assignedBusinessType);
            TownPlot plot = GetPlotById(building.plotId);
            ExteriorPropRuntimeDiagnostics diagnostics = authoring.BuildRuntimeDiagnostics(assignedBusinessType, building.definition, plot);
            List<BuildingExteriorIssue> authoringIssues = new();
            authoring.GetAuthoringIssues(authoringIssues);

            string summary = authoring.BuildSelectionSummary(assignedBusinessType, building.definition, plot);
            List<string> supplementalParts = BuildExteriorPropSupplementalInspectionParts(authoringIssues, diagnostics);
            if (supplementalParts.Count <= 0)
            {
                return summary;
            }

            return string.IsNullOrWhiteSpace(summary)
                ? $"Exterior dressing: {string.Join(" | ", supplementalParts)}"
                : $"{summary} | {string.Join(" | ", supplementalParts)}";
        }

        private static List<string> BuildExteriorPropSupplementalInspectionParts(
            IReadOnlyList<BuildingExteriorIssue> issues,
            ExteriorPropRuntimeDiagnostics diagnostics)
        {
            // Keep non-selection, overlap, and authoring-side reads composed in one place so inspection text stays
            // consistent even as runtime selection and validation rules evolve.
            List<string> parts = new();
            AddExteriorPropInspectionPart(parts, BuildExteriorPropHierarchyOverlapInspectionSummary(issues));
            AddExteriorPropInspectionPart(parts, BuildExteriorPropIssueInspectionSummary(issues, diagnostics));
            return parts;
        }

        private static void AddExteriorPropInspectionPart(List<string> parts, string value)
        {
            if (parts != null && !string.IsNullOrWhiteSpace(value))
            {
                parts.Add(value);
            }
        }

        private static string BuildExteriorPropHierarchyOverlapInspectionSummary(IReadOnlyList<BuildingExteriorIssue> issues)
        {
            if (issues == null || issues.Count == 0)
            {
                return string.Empty;
            }

            ExteriorPropHierarchyOverlapCounts counts = CollectExteriorPropHierarchyOverlapCounts(issues);
            return BuildExteriorPropHierarchyOverlapSummaryText(
                counts.DuplicateRootReuseCount,
                counts.NestedUnderManagedRootCount,
                counts.ContainsNestedManagedRootsCount);
        }

        private static string BuildExteriorPropIssueInspectionSummary(
            IReadOnlyList<BuildingExteriorIssue> issues,
            ExteriorPropRuntimeDiagnostics diagnostics)
        {
            if (issues == null || issues.Count == 0)
            {
                return string.Empty;
            }

            int issueCount = 0;
            int errorCount = 0;
            string firstMessage = string.Empty;
            HashSet<string> seenMessages = new();
            for (int i = 0; i < issues.Count; i++)
            {
                BuildingExteriorIssue issue = issues[i];
                if (string.IsNullOrWhiteSpace(issue.message)
                    || IsExteriorPropHierarchyOverlapCategory(issue.category)
                    || issue.message == diagnostics?.SelectionCompetitionDetail
                    || issue.message == diagnostics?.NoSelectionDetail
                    || !seenMessages.Add(issue.message))
                {
                    continue;
                }

                issueCount++;
                if (issue.isError)
                {
                    errorCount++;
                }

                if (string.IsNullOrWhiteSpace(firstMessage))
                {
                    firstMessage = issue.message;
                }
            }

            if (issueCount <= 0)
            {
                return string.Empty;
            }

            if (issueCount == 1)
            {
                return $"authoring issue: {firstMessage}";
            }

            return errorCount > 0
                ? $"{issueCount} exterior authoring issues ({errorCount} blocking) | first: {firstMessage}"
                : $"{issueCount} exterior authoring issues | first: {firstMessage}";
        }

        private static bool IsExteriorPropHierarchyOverlapCategory(BuildingExteriorIssueCategory category)
        {
            return category == BuildingExteriorIssueCategory.DuplicateRootReuse
                || category == BuildingExteriorIssueCategory.NestedUnderManagedRoot
                || category == BuildingExteriorIssueCategory.ContainsNestedManagedRoots;
        }

        private static string BuildExteriorPropHierarchyOverlapSummaryText(
            int duplicateRootReuseCount,
            int nestedUnderManagedRootCount,
            int containsNestedManagedRootsCount)
        {
            List<string> parts = new();
            if (duplicateRootReuseCount > 0)
            {
                parts.Add(duplicateRootReuseCount == 1
                    ? "duplicate root 1"
                    : $"duplicate roots {duplicateRootReuseCount}");
            }

            if (nestedUnderManagedRootCount > 0)
            {
                parts.Add(nestedUnderManagedRootCount == 1
                    ? "nested under managed root 1"
                    : $"nested under managed roots {nestedUnderManagedRootCount}");
            }

            if (containsNestedManagedRootsCount > 0)
            {
                parts.Add(containsNestedManagedRootsCount == 1
                    ? "contains nested managed root 1"
                    : $"contains nested managed roots {containsNestedManagedRootsCount}");
            }

            return parts.Count == 0
                ? string.Empty
                : $"hierarchy overlap: {string.Join(", ", parts)}";
        }

        private static string FormatExteriorBlockedMatchSummary(ExteriorPropRuntimeDiagnostics diagnostics)
        {
            if (diagnostics == null)
            {
                return "no blocked matching groups";
            }

            string reasonSummary = BuildExteriorPropBlockedMatchReasonSummary(
                diagnostics.BlockedMissingRootCount,
                diagnostics.BlockedPrefabRootCount,
                diagnostics.BlockedOutsideHierarchyRootCount);
            return FormatExteriorBlockedMatchSummaryText(
                diagnostics.BlockedMatchingGroupCount,
                "blocked matching exterior group",
                "blocked matching exterior groups",
                reasonSummary,
                fallbackText: "no blocked matching groups");
        }

        private static string BuildExteriorPropBlockedMatchReasonSummary(
            int missingRootCount,
            int prefabRootCount,
            int outsideHierarchyRootCount)
        {
            List<string> reasonParts = new();
            if (missingRootCount > 0)
            {
                reasonParts.Add(missingRootCount == 1
                    ? "missing root 1"
                    : $"missing root {missingRootCount}");
            }

            if (prefabRootCount > 0)
            {
                reasonParts.Add(prefabRootCount == 1
                    ? "prefab root 1"
                    : $"prefab root {prefabRootCount}");
            }

            if (outsideHierarchyRootCount > 0)
            {
                reasonParts.Add(outsideHierarchyRootCount == 1
                    ? "outside hierarchy 1"
                    : $"outside hierarchy {outsideHierarchyRootCount}");
            }

            return reasonParts.Count > 0
                ? $" ({string.Join(", ", reasonParts)})"
                : string.Empty;
        }

        private static string FormatExteriorBlockedMatchSummaryText(
            int blockedMatchCount,
            string singularLabel,
            string pluralLabel,
            string reasonSummary,
            string fallbackText = "")
        {
            if (blockedMatchCount <= 0)
            {
                return fallbackText;
            }

            return blockedMatchCount == 1
                ? $"{singularLabel}: 1{reasonSummary}"
                : $"{pluralLabel}: {blockedMatchCount}{reasonSummary}";
        }

        private static ExteriorPropHierarchyOverlapCounts CollectExteriorPropHierarchyOverlapCounts(IReadOnlyList<BuildingExteriorIssue> issues)
        {
            if (issues == null || issues.Count == 0)
            {
                return default;
            }

            int duplicateRootReuseCount = 0;
            int nestedUnderManagedRootCount = 0;
            int containsNestedManagedRootsCount = 0;
            HashSet<string> seenOverlapKeys = new();
            for (int i = 0; i < issues.Count; i++)
            {
                BuildingExteriorIssue issue = issues[i];
                string overlapKey = BuildExteriorPropHierarchyOverlapDedupKey(issue);
                if (string.IsNullOrWhiteSpace(overlapKey) || !seenOverlapKeys.Add(overlapKey))
                {
                    continue;
                }

                switch (issue.category)
                {
                    case BuildingExteriorIssueCategory.DuplicateRootReuse:
                        duplicateRootReuseCount++;
                        break;
                    case BuildingExteriorIssueCategory.NestedUnderManagedRoot:
                        nestedUnderManagedRootCount++;
                        break;
                    case BuildingExteriorIssueCategory.ContainsNestedManagedRoots:
                        containsNestedManagedRootsCount++;
                        break;
                }
            }

            return new ExteriorPropHierarchyOverlapCounts(
                duplicateRootReuseCount,
                nestedUnderManagedRootCount,
                containsNestedManagedRootsCount);
        }

        private static string BuildExteriorPropHierarchyOverlapDedupKey(BuildingExteriorIssue issue)
        {
            if (!IsExteriorPropHierarchyOverlapCategory(issue.category))
            {
                return string.Empty;
            }

            string detailKey = !string.IsNullOrWhiteSpace(issue.detailKey)
                ? issue.detailKey
                : issue.groupName;
            return string.IsNullOrWhiteSpace(detailKey)
                ? issue.category.ToString()
                : $"{issue.category}:{detailKey}";
        }

        private readonly struct ExteriorPropHierarchyOverlapCounts
        {
            public ExteriorPropHierarchyOverlapCounts(
                int duplicateRootReuseCount,
                int nestedUnderManagedRootCount,
                int containsNestedManagedRootsCount)
            {
                DuplicateRootReuseCount = duplicateRootReuseCount;
                NestedUnderManagedRootCount = nestedUnderManagedRootCount;
                ContainsNestedManagedRootsCount = containsNestedManagedRootsCount;
            }

            public int DuplicateRootReuseCount { get; }
            public int NestedUnderManagedRootCount { get; }
            public int ContainsNestedManagedRootsCount { get; }
        }

        private void ValidateAgriculturalBuildingFit(TownPlot plot, PlacedBuilding building, StringBuilder report, ref int errors, ref int warnings)
        {
            if (plot == null || building == null || building.definition == null || plot.zone != PlotZone.Agricultural)
            {
                return;
            }

            AgriculturalSiteRole plotRole = plot.agriculturalSiteRole;
            if (plotRole == AgriculturalSiteRole.None)
            {
                return;
            }

            if (building.definition.MatchesAgriculturalSiteRole(plotRole))
            {
                return;
            }

            string fitSummary = building.definition.BuildAgriculturalFitSummary(plotRole);
            if (building.definition.HasDedicatedAgriculturalRole)
            {
                if (building.playerOwned)
                {
                    AppendWarning(report, ref warnings, $"Building {building.id} is an intentional-looking conversion case on Plot {plot.id}: {fitSummary}.");
                }
                else
                {
                    AppendError(report, ref errors, $"Building {building.id} does not match Plot {plot.id}'s agricultural role: {fitSummary}.");
                }

                return;
            }

            if (building.definition.IsFallbackAgriculturalFit(plotRole))
            {
                AppendWarning(report, ref warnings, $"Building {building.id} is using a generic agricultural fallback on Plot {plot.id}: {fitSummary}.");
                return;
            }

            if (building.playerOwned)
            {
                AppendWarning(report, ref warnings, $"Building {building.id} is a weak agricultural fit on Plot {plot.id}: {fitSummary}.");
            }
            else
            {
                AppendError(report, ref errors, $"Building {building.id} is a weak generated fit on Plot {plot.id}: {fitSummary}.");
            }
        }

        private void ValidateExteriorPropAuthoring(TownPlot plot, PlacedBuilding building, StringBuilder report, ref int errors, ref int warnings)
        {
            if (building == null || building.definition == null || building.definition.VisualPrefab == null)
            {
                return;
            }

            BuildingExteriorPropAuthoring authoring = building.definition.VisualPrefab.GetComponent<BuildingExteriorPropAuthoring>();
            if (authoring == null)
            {
                return;
            }

            if (authoring.GroupCount <= 0)
            {
                AppendWarning(report, ref warnings, $"Building {building.id} has exterior prop authoring but no authored groups.");
                return;
            }

            List<BuildingExteriorIssue> authoringIssues = new();
            authoring.GetAuthoringIssues(authoringIssues);
            ExteriorPropHierarchyOverlapCounts hierarchyOverlapCounts = CollectExteriorPropHierarchyOverlapCounts(authoringIssues);
            HashSet<string> seenIssueMessages = new();
            for (int i = 0; i < authoringIssues.Count; i++)
            {
                BuildingExteriorIssue issue = authoringIssues[i];
                if (string.IsNullOrWhiteSpace(issue.message) || !seenIssueMessages.Add(issue.message))
                {
                    continue;
                }

                if (issue.isError)
                {
                    AppendError(report, ref errors, $"Building {building.id} exterior authoring: {issue.message}");
                    continue;
                }

                if (IsExteriorPropHierarchyOverlapCategory(issue.category))
                {
                    continue;
                }

                AppendWarning(report, ref warnings, $"Building {building.id} exterior authoring: {issue.message}");
            }

            string hierarchyOverlapSummary = BuildExteriorPropHierarchyOverlapSummaryText(
                hierarchyOverlapCounts.DuplicateRootReuseCount,
                hierarchyOverlapCounts.NestedUnderManagedRootCount,
                hierarchyOverlapCounts.ContainsNestedManagedRootsCount);
            if (!string.IsNullOrWhiteSpace(hierarchyOverlapSummary))
            {
                AppendWarning(report, ref warnings, $"Building {building.id} exterior authoring has {hierarchyOverlapSummary}.");
            }

            TryResolveAssignedBusinessType(building.id, out BusinessType? assignedBusinessType);
            ExteriorPropRuntimeDiagnostics diagnostics = authoring.BuildRuntimeDiagnostics(assignedBusinessType, building.definition, plot);
            if (diagnostics.BlockedMatchingGroupCount > 0)
            {
                AppendWarning(report, ref warnings, $"Building {building.id} has {FormatExteriorBlockedMatchSummary(diagnostics)}.");
            }

            if (diagnostics.CollapsedSameRootMatchingGroupCount > 0)
            {
                string sameRootVariantCount = diagnostics.CollapsedSameRootMatchingGroupCount == 1
                    ? "1 same-root matching exterior variant"
                    : $"{diagnostics.CollapsedSameRootMatchingGroupCount} same-root matching exterior variants";
                AppendWarning(report, ref warnings, diagnostics.CollapsedSameRootMatchingGroupCount == 1
                    ? $"Building {building.id} has {sameRootVariantCount}; only the strongest route for that root will compete."
                    : $"Building {building.id} has {sameRootVariantCount}; only the strongest route for each root will compete.");
            }

            if (!authoring.TryBuildSelectionPreview(assignedBusinessType, building.definition, plot, out ExteriorPropSelectionPreview preview))
            {
                string summary = authoring.BuildSelectionSummary(assignedBusinessType, building.definition, plot);
                AppendWarning(report, ref warnings, $"Building {building.id} has exterior prop authoring but nothing will activate: {summary}.");
                return;
            }

            if (diagnostics.ManagedPropCount == 0)
            {
                AppendWarning(report, ref warnings, $"Building {building.id} selects exterior group '{diagnostics.SelectedGroupName}' but that group does not manage any props.");
            }

            if (diagnostics.UsedArrayOrderTieBreak)
            {
                AppendWarning(report, ref warnings, $"Building {building.id} exterior selection is tied and depends on group array order: {diagnostics.TopScoreTieGroupSummary}.");
            }

            if (plot != null && plot.zone == PlotZone.Agricultural)
            {
                if (preview.MatchKind == ExteriorPropMatchKind.CommercialGeneric)
                {
                    AppendWarning(report, ref warnings, $"Building {building.id} is using a generic commercial exterior set on agricultural Plot {plot.id}: {preview.GroupName}.");
                }
                else if (preview.MatchKind == ExteriorPropMatchKind.AgriculturalGeneric)
                {
                    AppendWarning(report, ref warnings, $"Building {building.id} is using a generic agricultural exterior fallback on Plot {plot.id}: {preview.GroupName}.");
                }
            }

            if (building.definition.UsesUpperFloorResidential
                && preview.MatchKind == ExteriorPropMatchKind.CommercialGeneric
                && !preview.UsesUpperFloorResidentialBoost)
            {
                AppendWarning(report, ref warnings, $"Building {building.id} has upper-floor household space but exterior dressing stays purely commercial: {preview.GroupName}.");
            }
        }

        private string BuildPlotScorecard(TownPlot plot)
        {
            if (plot == null)
            {
                return string.Empty;
            }

            string frontage = plot.frontageCells >= 8 ? "Strong frontage" : plot.frontageCells >= 6 ? "Fair frontage" : "Thin frontage";
            string depth = plot.depthCells >= 10 ? "Deep site" : plot.depthCells >= 7 ? "Usable depth" : "Tight depth";
            bool hasRoadAccess = grid != null && grid.IsInBounds(plot.roadAccessCell) && grid.GetCell(plot.roadAccessCell).IsRoad;
            string access = hasRoadAccess ? "Road access" : "Weak access";

            int occupiedArea = 0;
            if (plot.buildingId >= 0 && TryGetBuilding(plot.buildingId, out PlacedBuilding building) && building != null)
            {
                occupiedArea = building.footprint.Area;
            }
            else if (plot.intendedBuildingFootprintCells.x > 0 && plot.intendedBuildingFootprintCells.y > 0)
            {
                occupiedArea = plot.intendedBuildingFootprintCells.x * plot.intendedBuildingFootprintCells.y;
            }
            else if (plot.candidateFootprint.IsValid)
            {
                occupiedArea = plot.candidateFootprint.Area;
            }

            int siteArea = Mathf.Max(1, plot.bounds.Area);
            float occupancyRatio = occupiedArea > 0 ? occupiedArea / (float)siteArea : 0f;
            string pad = occupancyRatio <= 0.33f ? "Loose build pad" : occupancyRatio <= 0.66f ? "Balanced build pad" : "Tight build pad";
            string context = plot.zone switch
            {
                PlotZone.Business => "Trade frontage",
                PlotZone.MixedUse => "Flexible frontage",
                PlotZone.Residential => "Domestic frontage",
                PlotZone.Agricultural => plot.agriculturalSiteRole == AgriculturalSiteRole.None
                    ? "Working land"
                    : FormatAgriculturalSiteRole(plot.agriculturalSiteRole),
                _ => "Undeclared use"
            };
            return $"Scorecard: {frontage} | {depth} | {access} | {pad} | {context}";
        }

        private static string FormatPlotZone(PlotZone zone)
        {
            return zone switch
            {
                PlotZone.Business => "Business",
                PlotZone.Residential => "Residential",
                PlotZone.MixedUse => "Mixed-use",
                PlotZone.Agricultural => "Agricultural",
                _ => zone.ToString()
            };
        }

        private static string FormatDirection(GridDirection direction)
        {
            return direction switch
            {
                GridDirection.North => "North",
                GridDirection.South => "South",
                GridDirection.East => "East",
                GridDirection.West => "West",
                _ => direction.ToString()
            };
        }

        private static string FormatPublicSiteRole(PublicSiteRole role)
        {
            if (role == PublicSiteRole.None)
            {
                return string.Empty;
            }

            if (role == PublicSiteRole.TownHall)
            {
                return "Town Hall site";
            }

            if (role == PublicSiteRole.Schoolhouse)
            {
                return "Schoolhouse site";
            }

            return HumanizeIdentifier(role.ToString()) + " site";
        }

        private static string FormatAgriculturalSiteRole(AgriculturalSiteRole role)
        {
            if (role == AgriculturalSiteRole.None)
            {
                return string.Empty;
            }

            return HumanizeIdentifier(role.ToString());
        }

        private static string HumanizeIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            System.Text.StringBuilder builder = new();
            builder.Append(value[0]);
            for (int i = 1; i < value.Length; i++)
            {
                char current = value[i];
                char previous = value[i - 1];
                if (char.IsUpper(current) && !char.IsWhiteSpace(previous) && previous != '-')
                {
                    builder.Append(' ');
                }

                builder.Append(current);
            }

            return builder.ToString().Replace(" Yard", " yard").Replace(" Production", " production").Replace(" Livestock", " livestock");
        }


        private bool TryResolvePopulationManager(out PopulationManager populationManager)
        {
            if (cachedPopulationManager == null)
            {
                cachedPopulationManager = UnityEngine.Object.FindAnyObjectByType<PopulationManager>();
            }

            populationManager = cachedPopulationManager;
            return populationManager != null;
        }

        private bool TryResolveSharedBusinessRuntime(out SharedBusinessRuntimeManager sharedBusinessRuntime)
        {
            if (cachedSharedBusinessRuntime == null)
            {
                cachedSharedBusinessRuntime = UnityEngine.Object.FindAnyObjectByType<SharedBusinessRuntimeManager>();
            }

            sharedBusinessRuntime = cachedSharedBusinessRuntime;
            return sharedBusinessRuntime != null;
        }

        private bool TryResolveAssignedBusinessType(int buildingId, out BusinessType? businessType)
        {
            businessType = null;
            if (buildingId < 0)
            {
                return false;
            }

            if (TryResolveSharedBusinessRuntime(out SharedBusinessRuntimeManager sharedRuntime)
                && sharedRuntime.TryFindByBuildingId(buildingId, out BusinessInstanceState sharedBusiness))
            {
                businessType = sharedBusiness.BusinessType;
                return true;
            }

            if (TryResolveGeneralStoreRuntime(out GeneralStoreRuntimeManager generalStoreRuntime)
                && generalStoreRuntime.StoreBuildingId == buildingId
                && generalStoreRuntime.CurrentBusiness != null)
            {
                businessType = generalStoreRuntime.CurrentBusiness.BusinessType;
                return true;
            }

            return false;
        }

        private bool TryResolveAcquisitionMarket(out AcquisitionMarketManager acquisitionMarket)
        {
            if (cachedAcquisitionMarket == null)
            {
                cachedAcquisitionMarket = UnityEngine.Object.FindAnyObjectByType<AcquisitionMarketManager>();
            }

            acquisitionMarket = cachedAcquisitionMarket;
            return acquisitionMarket != null;
        }

        private bool TryResolveGeneralStoreRuntime(out GeneralStoreRuntimeManager generalStoreRuntime)
        {
            if (cachedGeneralStoreRuntime == null)
            {
                cachedGeneralStoreRuntime = UnityEngine.Object.FindAnyObjectByType<GeneralStoreRuntimeManager>();
            }

            generalStoreRuntime = cachedGeneralStoreRuntime;
            return generalStoreRuntime != null;
        }

        private static void DestroyUnityObject(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(obj);
            }
            else
            {
                DestroyImmediate(obj);
            }
        }

}
}
