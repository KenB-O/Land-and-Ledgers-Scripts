using System;
using System.Collections.Generic;
using LandLedgers.World;

namespace LandLedgers.Persistence
{
    [Serializable]
    public sealed class RegionalWorldSaveDto
    {
        public int seed;
        public RegionalTerrainRecipeFamily recipeFamily;
        public float regionWidthMeters;
        public float regionDepthMeters;
        public float surveyCellSizeMeters;
        public float terrainTileSizeMeters;
        public RegionalAnchorTownSaveDto anchorTown = new();
        public List<RegionalTerrainTileSaveDto> terrainTiles = new();
        public List<RegionalWatercourseSaveDto> watercourses = new();
        public List<RegionalRouteCorridorSaveDto> routeCorridors = new();
        public List<RegionalSurveyParcelSaveDto> surveyParcels = new();
        public List<RegionalVegetationZoneSaveDto> vegetationZones = new();
        public List<RegionalSettlementClusterSaveDto> settlementClusters = new();
        public List<RegionalSettlementSaveDto> settlements = new();
    }

    [Serializable]
    public sealed class RegionalVector2SaveDto
    {
        public float x;
        public float y;
    }

    [Serializable]
    public sealed class RegionalRectSaveDto
    {
        public float x;
        public float y;
        public float width;
        public float height;
    }

    [Serializable]
    public sealed class RegionalAnchorTownSaveDto
    {
        public string anchorId = string.Empty;
        public RegionalVector2SaveDto centerMeters = new();
        public float suitabilityScore01;
        public float nearestWaterDistanceMeters;
        public float buildableLand01;
        public float gatewayScore01;
        public float expansionRoom01;
        public string debugReason = string.Empty;
    }

    [Serializable]
    public sealed class RegionalTerrainTileSaveDto
    {
        public string tileId = string.Empty;
        public int tileX;
        public int tileZ;
        public RegionalRectSaveDto boundsMeters = new();
        public float meanElevationMeters;
        public float ruggedness01;
        public float wetness01;
        public float activationReadiness01;
        public float settlementInfluence01;
        public RegionalBiomeKind dominantBiomeKind;
        public float routeInfluence01;
        public float routeActivationReadiness01;
        public float freightBurdenInfluence01;
        public string nearestRouteCorridorId = string.Empty;
        public RegionalRouteCorridorKind nearestRouteCorridorKind;
        public bool active = true;
        public string debugSummary = string.Empty;
    }

    [Serializable]
    public sealed class RegionalWatercourseSaveDto
    {
        public string watercourseId = string.Empty;
        public RegionalWatercourseKind kind;
        public float influenceWidthMeters;
        public float floodplainWidthMeters;
        public float flowStrength01;
        public List<RegionalVector2SaveDto> points = new();
    }

    [Serializable]
    public sealed class RegionalRouteCorridorSaveDto
    {
        public string corridorId = string.Empty;
        public RegionalRouteCorridorKind kind;
        public string sourceSettlementId = string.Empty;
        public string destinationSettlementId = string.Empty;
        public string sourceParcelId = string.Empty;
        public string destinationParcelId = string.Empty;
        public float lengthMeters;
        public float practicality01;
        public float seasonalReliability01;
        public float freightBurden01;
        public RegionalRouteConstraintKind primaryConstraintKind;
        public int waterCrossingCount;
        public int wetGroundCrossingCount;
        public float bridgeOrFordNeed01;
        public float mudSeasonRisk01;
        public float gradeBurden01;
        public string constraintSummary = string.Empty;
        public string debugSummary = string.Empty;
        public List<RegionalVector2SaveDto> points = new();
    }

    [Serializable]
    public sealed class RegionalSurveyParcelSaveDto
    {
        public string parcelId = string.Empty;
        public string sectionId = string.Empty;
        public string tractId = string.Empty;
        public RegionalParcelKind parcelKind;
        public RegionalRectSaveDto boundsMeters = new();
        public float acreage;
        public float meanSlope01;
        public float wetness01;
        public float waterAccess01;
        public float farmSuitability01;
        public float buildSuitability01;
        public float resourceSuitability01;
        public float supportBurden01;
        public RegionalParcelFrontageClass frontageClass;
        public RegionalParcelAccessQuality accessQuality;
        public RegionalParcelProvenance provenance;
        public RegionalRemoteSuitability remoteSuitability;
        public float terrainBurden01;
        public float developmentReadiness01;
        public string districtContext = string.Empty;
        public string debugReason = string.Empty;
        public RegionalParcelCorridorRelation corridorRelation;
        public string nearestCorridorId = string.Empty;
        public RegionalRouteCorridorKind nearestCorridorKind;
        public float distanceToCorridorMeters;
        public float corridorInfluence01;
        public float freightAccess01;
        public float corridorBurden01;
        public string corridorContext = string.Empty;
    }

    [Serializable]
    public sealed class RegionalVegetationZoneSaveDto
    {
        public string zoneId = string.Empty;
        public RegionalBiomeKind biomeKind;
        public RegionalVegetationKind vegetationKind;
        public RegionalRectSaveDto boundsMeters = new();
        public float density01;
        public float wetness01;
        public float slope01;
        public string notes = string.Empty;
    }

    [Serializable]
    public sealed class RegionalSettlementClusterSaveDto
    {
        public string clusterId = string.Empty;
        public RegionalVector2SaveDto centerMeters = new();
        public int householdCount;
        public int businessCount;
        public int population;
        public float serviceGravity01;
        public float freightSupport01;
        public float supportBurden01;
        public RegionalSettlementCharacter character;
        public float permanence01;
        public float localConfidence01;
        public float serviceDeficit01;
        public float declineRisk01;
        public string debugReason = string.Empty;
    }

    [Serializable]
    public sealed class RegionalSettlementSaveDto
    {
        public string settlementId = string.Empty;
        public string sourceClusterId = string.Empty;
        public RegionalVector2SaveDto centerMeters = new();
        public int householdCount;
        public int businessCount;
        public int population;
        public RegionalPopulationBand populationBand;
        public int populationBandMin;
        public int populationBandMax;
        public int nextTriggerWindowMinPopulation;
        public int nextTriggerWindowMaxPopulation;
        public RegionalSettlementLabel label;
        public float serviceGravity01;
        public float freightSupport01;
        public float supportBurden01;
        public RegionalSettlementCharacter character;
        public RegionalSettlementPermanence permanence;
        public RegionalSettlementSuccessionRole successionRole;
        public int regionalHierarchyRank;
        public float regionalGravity01;
        public float footholdChallenge01;
        public float successionReadiness01;
        public string successionContext = string.Empty;
        public float permanence01;
        public float localConfidence01;
        public float serviceDeficit01;
        public float declineRisk01;
        public string debugSummary = string.Empty;
    }
}
