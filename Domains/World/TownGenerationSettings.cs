using LandLedgers.Civic;
using UnityEngine;

namespace LandLedgers.World
{
    [CreateAssetMenu(
        fileName = "TownGenerationSettings",
        menuName = "Land & Ledgers/World/Town Generation Settings",
        order = 110)]
    public sealed class TownGenerationSettings : ScriptableObject
    {
        [Header("Grid")]
        [Min(1)]
        public int gridWidthCells = 220;

        [Min(1)]
        public int gridDepthCells = 200;

        [Min(0.1f)]
        public float cellSizeMeters = 2f;

        [Tooltip("World-space center of the hidden simulation grid.")]
        public Vector3 worldCenter = Vector3.zero;

        [Min(0)]
        public int seed = 1886;

        [Header("Startup Seed Authority")]
        [Tooltip("Controls how fresh runtime startup chooses the authoritative world seed. Save/load still restores saved world data and saved seed.")]
        public WorldGenerationStartupMode startupMode = WorldGenerationStartupMode.RandomFreshRuntimeWorld;

        [Tooltip("Developer-facing profile name logged when Fixed Testing World mode is active.")]
        public string fixedTestingProfileName = "Default QA Town";

        [Min(0)]
        [Tooltip("Seed used by Fixed Testing World mode for repeatable runtime starts.")]
        public int fixedTestingWorldSeed = 1886;

        [Tooltip("When enabled, this seed overrides the selected fresh-start mode for deterministic debugging.")]
        public bool useManualSeedOverride = false;

        [Min(0)]
        [Tooltip("Manual debugging seed used when Manual Seed Override is enabled.")]
        public int manualSeedOverride = 1886;

        [HideInInspector]
        public bool randomizeSeedOnFreshRuntimeGeneration = true;

        [HideInInspector]
        public bool useFixedDebugSeed = false;

        [Min(0)]
        [HideInInspector]
        public int fixedDebugSeed = 1886;

        [HideInInspector]
        [SerializeField]
        private bool seedAuthorityLegacyMigrationApplied;

        [Header("Terrain Classification")]
        [Min(1f)]
        public float terrainRaycastHeight = 250f;

        [Min(1f)]
        public float terrainRaycastDistance = 600f;

        [Range(0f, 60f)]
        public float maxBuildableSlopeDegrees = 8f;

        public float minBuildableHeight = -25f;
        public float maxBuildableHeight = 25f;
        public LayerMask terrainLayers = ~0;

        [Header("Regional Foundation")]
        [Tooltip("When enabled, new town generation first builds the full seeded regional map, then centers the opening town shell on the selected anchor foothold.")]
        public bool generateRegionalFoundation = true;

        [Min(2048f)]
        public float regionWidthMeters = 8192f;

        [Min(2048f)]
        public float regionDepthMeters = 8192f;

        [Min(128f)]
        public float regionalTerrainTileSizeMeters = 512f;

        [Min(256f)]
        public float regionalSurveySectionSizeMeters = 1609.344f;

        [Min(1)]
        public int regionalInitialHouseholdCount = 18;

        [Min(1)]
        public int regionalInitialBusinessCount = 6;

        [Tooltip("When enabled, the local town grid origin follows the generated regional anchor town rather than assuming map-center placement.")]
        public bool centerTownGridOnRegionalAnchor = true;
        [Tooltip("Keep the opening town visually near the scene origin while regional-foundation data exists elsewhere, until camera/active-region recentering is intentionally wired.")]
        public bool keepOpeningTownNearSceneOrigin = true;
        [Tooltip("Scene-space center used for the opening town when regional data is translated into the local playable slice.")]
        public Vector3 openingTownSceneCenter = Vector3.zero;

        [Header("Regional Debug / Inspection")]
        [Tooltip("When enabled, the regional debug overlay copies its display toggles from this settings asset on startup/refresh.")]
        public bool syncRegionalDebugOverlaySettings = true;

        [Tooltip("Show generated regional terrain tile markers in the debug overlay.")]
        public bool showRegionalTerrainTiles = true;

        [Tooltip("Show markers for terrain tiles that are suitable for route handoff / route readiness.")]
        public bool showRegionalRouteReadyTileMarkers = true;

        [Tooltip("Show generated regional watercourses in the debug overlay.")]
        public bool showRegionalWatercourses = true;

        [Tooltip("Show generated regional route corridors in the debug overlay.")]
        public bool showRegionalRouteCorridors = true;

        [Tooltip("Show route constraint markers such as bridge, ford, mud, grade, or wet-ground pressure.")]
        public bool showRegionalRouteConstraintMarkers = true;

        [Tooltip("Show generated regional vegetation zones in the debug overlay.")]
        public bool showRegionalVegetationZones = true;

        [Tooltip("Show regional survey parcels in the debug overlay.")]
        public bool showRegionalSurveyParcels = true;

        [Tooltip("Show parcel readiness markers for acquisition, valuation, remote industry, and freight handoff debugging.")]
        public bool showRegionalParcelReadinessMarkers = true;

        [Tooltip("Show remote industry suitability markers for mines, timber, freight staging, and other later regional opportunities.")]
        public bool showRegionalRemoteSuitabilityMarkers = true;

        [Tooltip("Show the opening regional anchor / foothold marker.")]
        public bool showRegionalAnchor = true;

        [Tooltip("Show regional settlement node markers.")]
        public bool showRegionalSettlements = true;

        [Tooltip("Show markers for settlement decline, support burden, and stability risk.")]
        public bool showRegionalSettlementRiskMarkers = true;

        [Tooltip("Show markers for succession pressure, peer challengers, and likely regional successor nodes.")]
        public bool showRegionalSettlementSuccessionMarkers = true;

        [Tooltip("Show validation issue markers emitted by the regional foundation inspection report.")]
        public bool showRegionalFoundationValidationMarkers = true;

        [Tooltip("Show opportunity markers emitted by the regional foundation inspection report.")]
        public bool showRegionalOpportunityMarkers = true;

        [Min(1)]
        [Tooltip("Maximum number of regional opportunity notes included in compact inspection summaries.")]
        public int regionalInspectionOpportunityDigestLimit = 3;

        [Header("Roads")]
        [Min(1)]
        public int roadWidthCells = 3;

        [Min(1)]
        public int mainStreetLengthCells = 150;

        [Min(0)]
        public int crossStreetCount = 3;

        [Min(1)]
        public int crossStreetSpacingCells = 36;

        [Min(1)]
        public int crossStreetLengthCells = 116;

        [Header("Plots")]
        [Min(1)]
        public int plotDepthCells = 8;

        [Min(1)]
        public int minPlotFrontageCells = 6;

        [Min(1)]
        public int maxPlotFrontageCells = 10;

        [Min(0)]
        public int plotGapCells = 1;

        [Min(0)]
        public int buildingSetbackCells = 1;

        [Min(0)]
        [Tooltip("Number of generated residential house lots placed near the rough town edge.")]
        public int edgeHouseCount = 2;

        [Min(0)]
        [Tooltip("Minimum number of new vacant parcels surfaced after the player buys vacant land.")]
        public int landPurchaseExpansionMinPlots = 1;

        [Min(0)]
        [Tooltip("Maximum number of new vacant parcels surfaced after the player buys vacant land.")]
        public int landPurchaseExpansionMaxPlots = 2;

        [Header("Agriculture")]
        public bool generateAgriculturalParcels = true;

        [Min(1)]
        public int agricultureSpurRoadLengthCells = 40;

        [Min(1)]
        public int agricultureDistanceFromCenterCells = 68;

        public Vector2Int cropFarmParcelSizeCells = new(16, 18);

        public Vector2Int ranchParcelSizeCells = new(20, 24);

        [Header("Remote Sawmill")]
        public bool generateSawmillProperty = true;

        [Min(1)]
        public int sawmillSpurRoadLengthCells = 82;

        [Min(1)]
        public int sawmillDistanceFromCenterCells = 62;

        public Vector2Int sawmillParcelSizeCells = new(40, 34);

        [Header("Mineral Districts")]
        [Tooltip("When enabled, the world generator builds deterministic mineral opportunity districts and remote proto-sites.")]
        public bool generateMineralDistricts = true;

        [Min(0)] public int coalDistrictCount = 2;
        [Min(0)] public int ironDistrictCount = 2;
        [Min(0)] public int goldDistrictCount = 2;
        [Min(0)] public int silverDistrictCount = 2;
        public Vector2Int coalDistrictRadiusCells = new(10, 18);
        public Vector2Int ironDistrictRadiusCells = new(8, 15);
        public Vector2Int goldDistrictRadiusCells = new(6, 11);
        public Vector2Int silverDistrictRadiusCells = new(6, 11);
        [Min(0.1f)] public float coalSuitabilityWeight = 1f;
        [Min(0.1f)] public float ironSuitabilityWeight = 1f;
        [Min(0.1f)] public float goldSuitabilityWeight = 1f;
        [Min(0.1f)] public float silverSuitabilityWeight = 1f;
        [Range(0f, 1f)] public float minimumProtoSiteStrength01 = 0.48f;
        [Min(1)] public int maximumRemoteProtoSites = 6;

        [Header("Construction Trades")]
        [Tooltip("When enabled, the town should guarantee at least one local carpenter/builder path for projects and repairs.")]
        public bool guaranteeLocalCarpenter = true;

        [Min(0)]
        [Tooltip("Minimum number of local carpenter/builder providers the town should support.")]
        public int minimumLocalCarpenterCount = 1;

        [Tooltip("Typical weeks required for a straightforward house build.")]
        public Vector2Int houseBuildDurationWeeks = new(1, 2);

        [Tooltip("Typical weeks required for a straightforward business build.")]
        public Vector2Int businessBuildDurationWeeks = new(2, 4);

        [Tooltip("Typical weeks required for a practical repair or refit job.")]
        public Vector2Int repairDurationWeeks = new(1, 2);

        [Tooltip("Food crop prefabs used as non-interactive visual dressing on generated Crop Farm parcels.")]
        public GameObject[] cropFarmFoodPrefabs;

        [Tooltip("Produce pile/crate/read-zone prefabs used as non-interactive visual dressing on generated Crop Farm parcels.")]
        public GameObject[] cropFarmProduceStackPrefabs;

        [Tooltip("Animal prefabs used as non-interactive visual dressing on generated Ranch parcels.")]
        public GameObject[] ranchAnimalPrefabs;

        [Header("Agricultural Yard Detail Props")]
        [Tooltip("When enabled, generated agricultural yards receive non-interactive site-level support prop dressing. Ordinary building exterior props are authored on building visual prefabs with BuildingExteriorPropAuthoring.")]
        public bool generateDetailProps = true;

        [Tooltip("Barrel prefabs used as visual-only agricultural yard support dressing.")]
        public GameObject[] barrelPropPrefabs;

        [Tooltip("Crate prefabs used as visual-only agricultural yard support dressing.")]
        public GameObject[] cratePropPrefabs;

        [Tooltip("Small goods prefabs such as buckets, sacks, packages, and milk-jug-style assets for agricultural yard support dressing.")]
        public GameObject[] smallGoodsPropPrefabs;

        [Tooltip("Farm support props used around crop production parcels.")]
        public GameObject[] farmSupportPropPrefabs;

        [Tooltip("Ranch support props used around livestock parcels.")]
        public GameObject[] ranchSupportPropPrefabs;

        [Tooltip("Sawmill support props used around remote sawmill yards.")]
        public GameObject[] sawmillSupportPropPrefabs;

        [Header("Forest Environment Dressing")]
        [Tooltip("When enabled, generated towns receive deterministic, visual-only forest environment dressing.")]
        public bool generateForestEnvironmentDressing = true;

        [Min(0)]
        [Tooltip("Maximum visual-only forest environment instances generated per town rebuild.")]
        public int forestDressingMaxInstances = 320;

        [Min(0)]
        [Tooltip("Width in cells used for the outer map-edge forest framing band.")]
        public int mapEdgeForestBandWidthCells = 10;

        [Min(0)]
        [Tooltip("Radius in cells around the remote sawmill used for forest district dressing.")]
        public int sawmillForestRadiusCells = 12;

        [Min(0)]
        [Tooltip("Half-width in cells for the generated visual wet-ground corridor.")]
        public int wetGroundCorridorWidthCells = 5;

        [Tooltip("Tree canopy prefabs used by visual-only forest dressing.")]
        public GameObject[] forestCanopyTreePrefabs;

        [Tooltip("Understory and bush prefabs used by visual-only forest dressing.")]
        public GameObject[] forestUnderstoryPrefabs;

        [Tooltip("Grass, fern, and low plant prefabs used by visual-only forest dressing.")]
        public GameObject[] forestGroundCoverPrefabs;

        [Tooltip("Fern, moss, lily, and other wet-ground prefabs used by visual-only wet corridor dressing.")]
        public GameObject[] forestWetGroundPrefabs;

        [Tooltip("Stumps, logs, roots, and branch prefabs used by visual-only forest dressing.")]
        public GameObject[] forestDeadfallPrefabs;

        [Tooltip("Meadow-friendly grass and plant prefabs used near agricultural transitions.")]
        public GameObject[] forestMeadowPrefabs;

        [Tooltip("Rock and stone prefabs used by visual-only forest and wet-ground dressing.")]
        public GameObject[] forestRockPrefabs;

        [Header("Acquisition Market")]
        [Tooltip("How many generated plots should remain vacant so land-for-sale offers can come from real town plots.")]
        [Min(0)]
        public int vacantLandPlotsToReserve = 4;

        [Header("Civic")]
        public bool generateTownHall = true;

        [Tooltip("Single slice civic holding placed near the town center. Its referenced physical shell is placed in the world.")]
        public TownHallDefinition townHallDefinition;

        [Tooltip("Physical civic shell used when a later Schoolhouse is established. Falls back to the Town Hall civic shell when unset.")]
        public BuildingDefinition schoolhousePhysicalDefinition;

        [Header("Building Catalog")]
        public BuildingDefinition[] buildingCatalog;

        [Header("Debug Visuals")]
        public bool showTerrainOverlay = false;
        public bool showRoads = true;
        public bool showPlots = true;
        public bool showBuildings = true;
        [Tooltip("Editor-only debug overlay for generated anchor points. Ignored during Play Mode and in player builds.")]
        public bool showAnchors = false;
        public Color buildableTerrainColor = new(0.18f, 0.38f, 0.19f, 0.22f);
        public Color blockedTerrainColor = new(0.45f, 0.12f, 0.08f, 0.45f);
        public Color roadColor = new(0.38f, 0.31f, 0.23f, 1f);
        public Material roadMaterial;
        public Color plotColor = new(0.95f, 0.78f, 0.33f, 0.22f);
        public Color footprintColor = new(0.55f, 0.38f, 0.24f, 1f);
        public Color doorAnchorColor = new(0.2f, 0.85f, 1f, 1f);
        public Color serviceAnchorColor = new(0.5f, 0.9f, 0.35f, 1f);
        public Color dropOffAnchorColor = new(1f, 0.75f, 0.2f, 1f);
        public bool showResourceSuitabilityOverlay = false;
        public bool showResourceDistricts = true;
        public bool showResourceProtoSites = true;
        public bool showResourcePressureLabels = false;
        public Color coalDistrictColor = new(0.18f, 0.18f, 0.18f, 0.42f);
        public Color ironDistrictColor = new(0.50f, 0.32f, 0.24f, 0.42f);
        public Color goldDistrictColor = new(0.78f, 0.63f, 0.14f, 0.42f);
        public Color silverDistrictColor = new(0.64f, 0.68f, 0.74f, 0.42f);
        public Color remoteProtoSiteColor = new(0.88f, 0.86f, 0.76f, 1f);

        public Vector3 GridOrigin
        {
            get
            {
                return new Vector3(
                    worldCenter.x - gridWidthCells * cellSizeMeters * 0.5f,
                    worldCenter.y,
                    worldCenter.z - gridDepthCells * cellSizeMeters * 0.5f);
            }
        }

        public void Sanitize()
        {
            gridWidthCells = Mathf.Max(1, gridWidthCells);
            gridDepthCells = Mathf.Max(1, gridDepthCells);
            cellSizeMeters = Mathf.Max(0.1f, cellSizeMeters);
            seed = Mathf.Max(0, seed);
            if (!seedAuthorityLegacyMigrationApplied)
            {
                if (useFixedDebugSeed)
                {
                    startupMode = WorldGenerationStartupMode.FixedTestingWorld;
                    fixedTestingWorldSeed = fixedDebugSeed > 0 ? fixedDebugSeed : fixedTestingWorldSeed;
                    if (string.IsNullOrWhiteSpace(fixedTestingProfileName))
                    {
                        fixedTestingProfileName = "Legacy Fixed Debug Seed";
                    }
                }
                else if (!randomizeSeedOnFreshRuntimeGeneration
                    && startupMode == WorldGenerationStartupMode.RandomFreshRuntimeWorld
                    && !useManualSeedOverride)
                {
                    useManualSeedOverride = true;
                    manualSeedOverride = seed;
                }

                seedAuthorityLegacyMigrationApplied = true;
            }

            fixedTestingProfileName = string.IsNullOrWhiteSpace(fixedTestingProfileName)
                ? "Default QA Town"
                : fixedTestingProfileName.Trim();
            fixedTestingWorldSeed = Mathf.Max(0, fixedTestingWorldSeed);
            manualSeedOverride = Mathf.Max(0, manualSeedOverride);
            fixedDebugSeed = Mathf.Max(0, fixedDebugSeed);
            terrainRaycastHeight = Mathf.Max(1f, terrainRaycastHeight);
            terrainRaycastDistance = Mathf.Max(1f, terrainRaycastDistance);
            if (minBuildableHeight > maxBuildableHeight)
            {
                (minBuildableHeight, maxBuildableHeight) = (maxBuildableHeight, minBuildableHeight);
            }

            regionWidthMeters = Mathf.Max(2048f, regionWidthMeters);
            regionDepthMeters = Mathf.Max(2048f, regionDepthMeters);
            regionalTerrainTileSizeMeters = Mathf.Max(128f, regionalTerrainTileSizeMeters);
            regionalSurveySectionSizeMeters = Mathf.Max(256f, regionalSurveySectionSizeMeters);
            regionalInitialHouseholdCount = Mathf.Max(1, regionalInitialHouseholdCount);
            regionalInitialBusinessCount = Mathf.Max(1, regionalInitialBusinessCount);
            roadWidthCells = Mathf.Max(1, roadWidthCells);
            mainStreetLengthCells = Mathf.Clamp(mainStreetLengthCells, 1, gridDepthCells);
            crossStreetCount = Mathf.Max(0, crossStreetCount);
            crossStreetSpacingCells = Mathf.Max(1, crossStreetSpacingCells);
            crossStreetLengthCells = Mathf.Clamp(crossStreetLengthCells, 1, gridWidthCells);
            plotDepthCells = Mathf.Max(1, plotDepthCells);
            minPlotFrontageCells = Mathf.Max(1, minPlotFrontageCells);
            maxPlotFrontageCells = Mathf.Max(minPlotFrontageCells, maxPlotFrontageCells);
            plotGapCells = Mathf.Max(0, plotGapCells);
            buildingSetbackCells = Mathf.Max(0, buildingSetbackCells);
            edgeHouseCount = Mathf.Max(0, edgeHouseCount);
            landPurchaseExpansionMinPlots = Mathf.Max(0, landPurchaseExpansionMinPlots);
            landPurchaseExpansionMaxPlots = Mathf.Max(landPurchaseExpansionMinPlots, landPurchaseExpansionMaxPlots);
            agricultureSpurRoadLengthCells = Mathf.Max(1, agricultureSpurRoadLengthCells);
            agricultureDistanceFromCenterCells = Mathf.Max(1, agricultureDistanceFromCenterCells);
            cropFarmParcelSizeCells = new Vector2Int(
                Mathf.Max(1, cropFarmParcelSizeCells.x),
                Mathf.Max(1, cropFarmParcelSizeCells.y));
            ranchParcelSizeCells = new Vector2Int(
                Mathf.Max(cropFarmParcelSizeCells.x, ranchParcelSizeCells.x),
                Mathf.Max(cropFarmParcelSizeCells.y, ranchParcelSizeCells.y));
            sawmillSpurRoadLengthCells = Mathf.Max(1, sawmillSpurRoadLengthCells);
            sawmillDistanceFromCenterCells = Mathf.Max(1, sawmillDistanceFromCenterCells);
            sawmillParcelSizeCells = new Vector2Int(
                Mathf.Max(ranchParcelSizeCells.x, sawmillParcelSizeCells.x),
                Mathf.Max(ranchParcelSizeCells.y, sawmillParcelSizeCells.y));
            coalDistrictCount = Mathf.Max(0, coalDistrictCount);
            ironDistrictCount = Mathf.Max(0, ironDistrictCount);
            goldDistrictCount = Mathf.Max(0, goldDistrictCount);
            silverDistrictCount = Mathf.Max(0, silverDistrictCount);
            coalDistrictRadiusCells = SanitizeRadiusRange(coalDistrictRadiusCells, 10, 18);
            ironDistrictRadiusCells = SanitizeRadiusRange(ironDistrictRadiusCells, 8, 15);
            goldDistrictRadiusCells = SanitizeRadiusRange(goldDistrictRadiusCells, 6, 11);
            silverDistrictRadiusCells = SanitizeRadiusRange(silverDistrictRadiusCells, 6, 11);
            coalSuitabilityWeight = Mathf.Max(0.1f, coalSuitabilityWeight);
            ironSuitabilityWeight = Mathf.Max(0.1f, ironSuitabilityWeight);
            goldSuitabilityWeight = Mathf.Max(0.1f, goldSuitabilityWeight);
            silverSuitabilityWeight = Mathf.Max(0.1f, silverSuitabilityWeight);
            minimumProtoSiteStrength01 = Mathf.Clamp01(minimumProtoSiteStrength01);
            maximumRemoteProtoSites = Mathf.Max(1, maximumRemoteProtoSites);
            minimumLocalCarpenterCount = Mathf.Max(0, minimumLocalCarpenterCount);
            houseBuildDurationWeeks = SanitizeWeekRange(houseBuildDurationWeeks, 1, 2);
            businessBuildDurationWeeks = SanitizeWeekRange(businessBuildDurationWeeks, 2, 4);
            repairDurationWeeks = SanitizeWeekRange(repairDurationWeeks, 1, 2);
            forestDressingMaxInstances = Mathf.Max(0, forestDressingMaxInstances);
            mapEdgeForestBandWidthCells = Mathf.Max(0, mapEdgeForestBandWidthCells);
            sawmillForestRadiusCells = Mathf.Max(0, sawmillForestRadiusCells);
            wetGroundCorridorWidthCells = Mathf.Max(0, wetGroundCorridorWidthCells);
            vacantLandPlotsToReserve = Mathf.Max(0, vacantLandPlotsToReserve);
            regionalInspectionOpportunityDigestLimit = Mathf.Clamp(regionalInspectionOpportunityDigestLimit, 1, 8);
        }

        public string BuildConstructionTradeSummary()
        {
            string carpenterLine = guaranteeLocalCarpenter
                ? $"Local carpenter trade active ({Mathf.Max(1, minimumLocalCarpenterCount)} available)"
                : "No guaranteed local carpenter trade";
            string houseLine = FormatWeekRange(houseBuildDurationWeeks, "House builds");
            string businessLine = FormatWeekRange(businessBuildDurationWeeks, "Business builds");
            string repairLine = FormatWeekRange(repairDurationWeeks, "Repairs");
            return carpenterLine + " | " + houseLine + " | " + businessLine + " | " + repairLine;
        }

        public string BuildCarpenterAvailabilitySummary()
        {
            if (!guaranteeLocalCarpenter)
            {
                return "Builder availability uncertain. Outside help or slower starts may be required.";
            }

            int count = Mathf.Max(1, minimumLocalCarpenterCount);
            return count == 1
                ? "One local carpenter/builder should be available for ordinary projects."
                : $"Local carpenter trade active with about {count} builders available.";
        }

        public int GetTypicalHouseBuildWeeks()
        {
            return GetTypicalWeeks(houseBuildDurationWeeks);
        }

        public int GetTypicalBusinessBuildWeeks()
        {
            return GetTypicalWeeks(businessBuildDurationWeeks);
        }

        public int GetTypicalRepairWeeks()
        {
            return GetTypicalWeeks(repairDurationWeeks);
        }

        public int GetTypicalBuildWeeksForDefinition(BuildingDefinition definition)
        {
            if (definition == null)
            {
                return GetTypicalBusinessBuildWeeks();
            }

            bool residential = definition.CanHostHouseholds && !definition.CanHostWorkplace;
            return residential ? GetTypicalHouseBuildWeeks() : GetTypicalBusinessBuildWeeks();
        }

        public int GetBuilderCapacityPerWeek()
        {
            return guaranteeLocalCarpenter ? Mathf.Max(1, minimumLocalCarpenterCount) : 1;
        }


        public int EstimateTypicalLumberUnitsForDefinition(BuildingDefinition definition)
        {
            if (definition == null)
            {
                return 12;
            }

            int width = Mathf.Max(1, definition.FootprintSizeCells.x);
            int depth = Mathf.Max(1, definition.FootprintSizeCells.y);
            int area = Mathf.Max(1, width * depth);
            bool residential = definition.CanHostHouseholds && !definition.CanHostWorkplace;
            int baseUnits = residential ? 8 : 12;
            int areaUnits = Mathf.CeilToInt(area * 0.9f);
            return Mathf.Max(6, baseUnits + areaUnits);
        }

        public int EstimateTypicalLaborUnitsForDefinition(BuildingDefinition definition)
        {
            if (definition == null)
            {
                return 4;
            }

            int width = Mathf.Max(1, definition.FootprintSizeCells.x);
            int depth = Mathf.Max(1, definition.FootprintSizeCells.y);
            int area = Mathf.Max(1, width * depth);
            bool residential = definition.CanHostHouseholds && !definition.CanHostWorkplace;
            int baseUnits = residential ? 3 : 5;
            return Mathf.Max(2, baseUnits + Mathf.CeilToInt(area * 0.35f));
        }

        public string BuildProjectInputEstimateSummary(BuildingDefinition definition)
        {
            int lumber = EstimateTypicalLumberUnitsForDefinition(definition);
            int labor = EstimateTypicalLaborUnitsForDefinition(definition);
            bool residential = definition != null && definition.CanHostHouseholds && !definition.CanHostWorkplace;
            string label = residential ? "House" : "Business";
            return $"{label} input read: about {lumber} lumber and {labor} labor.";
        }

        public int EstimateQueuedBuildWeeks(BuildingDefinition definition, int queueProjectsAhead, bool materialTight)
        {
            int weeks = GetTypicalBuildWeeksForDefinition(definition);
            int capacity = Mathf.Max(1, GetBuilderCapacityPerWeek());
            int queue = Mathf.Max(0, queueProjectsAhead);
            if (queue > 0)
            {
                weeks += queue / capacity;
                if (queue % capacity != 0)
                {
                    weeks += 1;
                }
            }

            if (materialTight)
            {
                weeks += 1;
            }

            return Mathf.Max(1, weeks);
        }

        public string BuildProjectQueueSummary(BuildingDefinition definition, int queueProjectsAhead, bool materialTight)
        {
            int weeks = EstimateQueuedBuildWeeks(definition, queueProjectsAhead, materialTight);
            bool residential = definition != null && definition.CanHostHouseholds && !definition.CanHostWorkplace;
            string label = residential ? "House" : "Business";
            string queue = queueProjectsAhead > 0 ? $" Queue ahead: {queueProjectsAhead}." : " No queue ahead.";
            string material = materialTight ? " Material flow looks tight." : " Material flow looks steady.";
            return $"{label} start read: about {weeks} week(s).{queue}{material}";
        }

        public string BuildConstructionClimateHeadline(bool hasLocalMill, int queueProjectsAhead)
        {
            string carpenter = BuildCarpenterAvailabilitySummary();
            string mill = hasLocalMill
                ? "Local mill support should help steady lumber flow."
                : "No clear local mill support; outside lumber pressure is more likely.";
            string queue = queueProjectsAhead > 0 ? $" Active build queue about {queueProjectsAhead} project(s)." : " No active build queue pressure.";
            return carpenter + " " + mill + queue;
        }


        private static int GetTypicalWeeks(Vector2Int weeks)
        {
            int minWeeks = Mathf.Max(1, weeks.x);
            int maxWeeks = Mathf.Max(minWeeks, weeks.y);
            return Mathf.RoundToInt((minWeeks + maxWeeks) * 0.5f);
        }

        private static string FormatWeekRange(Vector2Int weeks, string label)
        {
            int minWeeks = Mathf.Max(1, weeks.x);
            int maxWeeks = Mathf.Max(minWeeks, weeks.y);
            string range = minWeeks == maxWeeks
                ? $"{minWeeks} week{(minWeeks == 1 ? string.Empty : "s")}"
                : $"{minWeeks}-{maxWeeks} weeks";
            return label + " " + range;
        }

        private static Vector2Int SanitizeWeekRange(Vector2Int value, int defaultMin, int defaultMax)
        {
            int minWeeks = Mathf.Max(1, value.x == 0 ? defaultMin : value.x);
            int maxWeeks = Mathf.Max(minWeeks, value.y == 0 ? defaultMax : value.y);
            return new Vector2Int(minWeeks, maxWeeks);
        }

        private static Vector2Int SanitizeRadiusRange(Vector2Int value, int defaultMin, int defaultMax)
        {
            int min = Mathf.Max(1, value.x == 0 ? defaultMin : value.x);
            int max = Mathf.Max(min, value.y == 0 ? defaultMax : value.y);
            return new Vector2Int(min, max);
        }

        private void OnValidate()
        {
            Sanitize();
        }

        public string BuildRegionalDebugSurfaceSummary()
        {
            int enabledSurfaces = 0;
            if (showRegionalTerrainTiles) enabledSurfaces++;
            if (showRegionalRouteReadyTileMarkers) enabledSurfaces++;
            if (showRegionalWatercourses) enabledSurfaces++;
            if (showRegionalRouteCorridors) enabledSurfaces++;
            if (showRegionalRouteConstraintMarkers) enabledSurfaces++;
            if (showRegionalVegetationZones) enabledSurfaces++;
            if (showRegionalSurveyParcels) enabledSurfaces++;
            if (showRegionalParcelReadinessMarkers) enabledSurfaces++;
            if (showRegionalRemoteSuitabilityMarkers) enabledSurfaces++;
            if (showRegionalAnchor) enabledSurfaces++;
            if (showRegionalSettlements) enabledSurfaces++;
            if (showRegionalSettlementRiskMarkers) enabledSurfaces++;
            if (showRegionalSettlementSuccessionMarkers) enabledSurfaces++;
            if (showRegionalFoundationValidationMarkers) enabledSurfaces++;
            if (showRegionalOpportunityMarkers) enabledSurfaces++;

            string sync = syncRegionalDebugOverlaySettings ? "overlay sync on" : "overlay sync off";
            return $"Regional debug: {sync}, {enabledSurfaces}/15 surface(s) enabled, opportunity digest {Mathf.Clamp(regionalInspectionOpportunityDigestLimit, 1, 8)}.";
        }

        public RegionalWorldGenerationSettings BuildRegionalWorldGenerationSettings()
        {
            Sanitize();
            return new RegionalWorldGenerationSettings
            {
                regionWidthMeters = regionWidthMeters,
                regionDepthMeters = regionDepthMeters,
                terrainTileSizeMeters = regionalTerrainTileSizeMeters,
                surveyCellSizeMeters = cellSizeMeters,
                sectionSizeMeters = regionalSurveySectionSizeMeters,
                initialHouseholdCount = regionalInitialHouseholdCount,
                initialBusinessCount = regionalInitialBusinessCount
            }.Sanitized();
        }
    }
}
