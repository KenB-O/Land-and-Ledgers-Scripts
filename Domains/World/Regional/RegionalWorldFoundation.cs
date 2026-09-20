using System;
using System.Collections.Generic;
using System.Text;
using LandLedgers.Persistence;
using UnityEngine;

namespace LandLedgers.World
{
    public enum RegionalTerrainRecipeFamily
    {
        CreekCorridorSettlementLand = 0,
        RiverBendHigherShelf = 1,
        ValleyMouthTransition = 2,
        PrairieOpenPlains = 3,
        PlainsToHillsInterface = 4,
        MixedRoughFlatCountry = 5,
        WetLowlandPockets = 6
    }

    public enum RegionalWatercourseKind
    {
        River = 0,
        Creek = 1,
        DrainageLine = 2,
        WetLowland = 3
    }

    public enum RegionalRouteCorridorKind
    {
        OpeningFootholdSpine = 0,
        SettlementConnector = 1,
        FreightTrack = 2,
        RemoteWorksiteTrack = 3,
        CreekOrDrawTrack = 4
    }

    public enum RegionalRouteConstraintKind
    {
        None = 0,
        StableDryTrack = 1,
        WetGround = 2,
        FordOrLowCrossing = 3,
        BridgeLikely = 4,
        SteepGrade = 5,
        MudSeasonRisk = 6
    }



    public enum RegionalFoundationIssueSeverity
    {
        Info = 0,
        Warning = 1,
        Error = 2
    }

    public enum RegionalFoundationIssueCategory
    {
        General = 0,
        AnchorTown = 1,
        TerrainTiles = 2,
        Hydrology = 3,
        SurveyParcels = 4,
        SettlementNodes = 5,
        RouteCorridors = 6,
        SaveCompatibility = 7
    }

    public enum RegionalFoundationOpportunityKind
    {
        None = 0,
        BestNearTermExpansionParcel = 1,
        BestRemoteWorksiteCandidate = 2,
        StrongestFreightParcel = 3,
        WeakestRoute = 4,
        HighestBurdenSettlement = 5,
        CredibleSuccessorNode = 6
    }

    public enum RegionalParcelKind
    {
        RuralTract = 0,
        TownPlatCore = 1,
        TownPlatEdge = 2,
        EdgeExpansion = 3,
        RoughParcel = 4,
        FloodplainTract = 5
    }

    public enum RegionalBiomeKind
    {
        OpenPrairie = 0,
        CreekBottom = 1,
        WetLowland = 2,
        UplandGrass = 3,
        RoughHills = 4,
        TimberDraw = 5
    }

    public enum RegionalVegetationKind
    {
        Grassland = 0,
        RiparianTrees = 1,
        WetGround = 2,
        BrushAndScrub = 3,
        TimberStand = 4,
        SparseRoughCountry = 5
    }

    public enum RegionalParcelFrontageClass
    {
        None = 0,
        InteriorSurvey = 1,
        CreekFront = 2,
        TrailOrRoad = 3,
        Crossroads = 4,
        TownMainStreet = 5,
        RemoteWorksiteAccess = 6
    }

    public enum RegionalParcelAccessQuality
    {
        None = 0,
        Poor = 1,
        SeasonalTrack = 2,
        WagonReachable = 3,
        RoadFrontage = 4,
        Gateway = 5
    }

    public enum RegionalParcelProvenance
    {
        Unset = 0,
        PublicSurveyQuarter = 1,
        CreekFragment = 2,
        TownsitePlat = 3,
        FounderOrSpeculatorReserve = 4,
        EdgeExpansionClaim = 5,
        RemoteIndustrialClaim = 6,
        RanchOrFarmServiceTract = 7
    }

    public enum RegionalRemoteSuitability
    {
        None = 0,
        FarmService = 1,
        RanchRange = 2,
        TimberWorksite = 3,
        MineralProspect = 4,
        FreightOutpost = 5,
        MixedOpportunity = 6
    }

    public enum RegionalParcelCorridorRelation
    {
        None = 0,
        NearOpeningSpine = 1,
        NearSettlementConnector = 2,
        NearFreightTrack = 3,
        NearRemoteWorksiteTrack = 4,
        NearCreekOrDrawTrack = 5,
        OffRoute = 6
    }

    public enum RegionalSettlementCharacter
    {
        Unset = 0,
        OpeningFootholdGateway = 1,
        FarmServicePocket = 2,
        TimberWorksite = 3,
        MineralOrRoughCountryCamp = 4,
        FreightCrossing = 5,
        MixedOpportunityNode = 6
    }

    public enum RegionalSettlementPermanence
    {
        Unset = 0,
        FragileOccupation = 1,
        ThinButServiceable = 2,
        EmergingNode = 3,
        DurableSettlement = 4,
        RegionalCenterCandidate = 5
    }

    public enum RegionalSettlementSuccessionRole
    {
        Unset = 0,
        OpeningFoothold = 1,
        DependentSatellite = 2,
        RisingSecondaryNode = 3,
        PeerChallenger = 4,
        LikelyRegionalSuccessor = 5,
        StalledOrDecliningNode = 6
    }

    public enum RegionalPopulationBand
    {
        Unset = 0,
        FamilyCluster = 1,
        Camp = 2,
        Hamlet = 3,
        Town = 4,
        LargeTown = 5
    }

    public enum RegionalSettlementLabel
    {
        LocalCluster = 0,
        Camp = 1,
        Hamlet = 2,
        Town = 3,
        LargeTown = 4
    }

    [Serializable]
    public sealed class RegionalWorldGenerationSettings
    {
        [Min(2048f)] public float regionWidthMeters = 8192f;
        [Min(2048f)] public float regionDepthMeters = 8192f;
        [Min(0.1f)] public float surveyCellSizeMeters = 2f;
        [Min(128f)] public float terrainTileSizeMeters = 512f;
        [Min(256f)] public float sectionSizeMeters = 1609.344f;
        [Min(1)] public int initialHouseholdCount = 18;
        [Min(1)] public int initialBusinessCount = 6;
        [Min(32f)] public float anchorScanStepMeters = 256f;
        [Min(32f)] public float townPlatLotWidthMeters = 32f;
        [Min(32f)] public float townPlatLotDepthMeters = 48f;

        public RegionalWorldGenerationSettings Sanitized()
        {
            return new RegionalWorldGenerationSettings
            {
                regionWidthMeters = Mathf.Max(2048f, regionWidthMeters),
                regionDepthMeters = Mathf.Max(2048f, regionDepthMeters),
                surveyCellSizeMeters = Mathf.Max(0.1f, surveyCellSizeMeters),
                terrainTileSizeMeters = Mathf.Max(128f, terrainTileSizeMeters),
                sectionSizeMeters = Mathf.Max(256f, sectionSizeMeters),
                initialHouseholdCount = Mathf.Max(1, initialHouseholdCount),
                initialBusinessCount = Mathf.Max(1, initialBusinessCount),
                anchorScanStepMeters = Mathf.Max(32f, anchorScanStepMeters),
                townPlatLotWidthMeters = Mathf.Max(32f, townPlatLotWidthMeters),
                townPlatLotDepthMeters = Mathf.Max(32f, townPlatLotDepthMeters)
            };
        }
    }

    [Serializable]
    public sealed class RegionalTerrainTileRecord
    {
        public string TileId { get; private set; }
        public int TileX { get; private set; }
        public int TileZ { get; private set; }
        public Rect BoundsMeters { get; private set; }
        public float MeanElevationMeters { get; private set; }
        public float Ruggedness01 { get; private set; }
        public float Wetness01 { get; private set; }
        public float ActivationReadiness01 { get; private set; }
        public float SettlementInfluence01 { get; private set; }
        public RegionalBiomeKind DominantBiomeKind { get; private set; }
        public float RouteInfluence01 { get; private set; }
        public float RouteActivationReadiness01 { get; private set; }
        public float FreightBurdenInfluence01 { get; private set; }
        public string NearestRouteCorridorId { get; private set; }
        public RegionalRouteCorridorKind NearestRouteCorridorKind { get; private set; }
        public bool Active { get; private set; }
        public string DebugSummary { get; private set; }

        public RegionalTerrainTileRecord(
            string tileId,
            int tileX,
            int tileZ,
            Rect boundsMeters,
            float meanElevationMeters,
            float ruggedness01,
            float wetness01,
            bool active,
            float activationReadiness01 = 0f,
            float settlementInfluence01 = 0f,
            RegionalBiomeKind dominantBiomeKind = RegionalBiomeKind.OpenPrairie,
            string debugSummary = "",
            float routeInfluence01 = 0f,
            float routeActivationReadiness01 = 0f,
            float freightBurdenInfluence01 = 0f,
            string nearestRouteCorridorId = "",
            RegionalRouteCorridorKind nearestRouteCorridorKind = RegionalRouteCorridorKind.SettlementConnector)
        {
            TileId = tileId ?? string.Empty;
            TileX = tileX;
            TileZ = tileZ;
            BoundsMeters = boundsMeters;
            MeanElevationMeters = meanElevationMeters;
            Ruggedness01 = Mathf.Clamp01(ruggedness01);
            Wetness01 = Mathf.Clamp01(wetness01);
            ActivationReadiness01 = Mathf.Clamp01(activationReadiness01);
            SettlementInfluence01 = Mathf.Clamp01(settlementInfluence01);
            DominantBiomeKind = dominantBiomeKind;
            RouteInfluence01 = Mathf.Clamp01(routeInfluence01);
            RouteActivationReadiness01 = Mathf.Clamp01(routeActivationReadiness01);
            FreightBurdenInfluence01 = Mathf.Clamp01(freightBurdenInfluence01);
            NearestRouteCorridorId = nearestRouteCorridorId ?? string.Empty;
            NearestRouteCorridorKind = nearestRouteCorridorKind;
            Active = active;
            DebugSummary = debugSummary ?? string.Empty;
        }

        public void SetActive(bool active)
        {
            Active = active;
        }

        public string BuildReadinessSummary()
        {
            string readiness = ActivationReadiness01 >= 0.72f
                ? "active-region candidate"
                : ActivationReadiness01 >= 0.38f ? "near-region support" : "background regional tile";
            string wetness = Wetness01 >= 0.62f ? "wet" : Wetness01 >= 0.32f ? "water-influenced" : "dry";
            string slope = Ruggedness01 >= 0.58f ? "rough" : Ruggedness01 >= 0.28f ? "rolling" : "gentle";
            string route = RouteInfluence01 >= 0.58f
                ? $"strong route influence from {NearestRouteCorridorKind}"
                : RouteInfluence01 >= 0.24f ? $"some route influence from {NearestRouteCorridorKind}" : "no meaningful route influence";
            return $"{readiness}; route readiness {RouteActivationReadiness01:P0}; {DominantBiomeKind.ToString().ToLowerInvariant()}; {wetness}; {slope}; {route}.";
        }
    }

    [Serializable]
    public sealed class RegionalWatercourseRecord
    {
        public string WatercourseId { get; private set; }
        public RegionalWatercourseKind Kind { get; private set; }
        public float InfluenceWidthMeters { get; private set; }
        public float FloodplainWidthMeters { get; private set; }
        public float FlowStrength01 { get; private set; }
        public List<Vector2> Points { get; private set; } = new();

        public RegionalWatercourseRecord(
            string watercourseId,
            RegionalWatercourseKind kind,
            float influenceWidthMeters,
            float floodplainWidthMeters,
            float flowStrength01,
            IReadOnlyList<Vector2> points)
        {
            WatercourseId = watercourseId ?? string.Empty;
            Kind = kind;
            InfluenceWidthMeters = Mathf.Max(0f, influenceWidthMeters);
            FloodplainWidthMeters = Mathf.Max(InfluenceWidthMeters, floodplainWidthMeters);
            FlowStrength01 = Mathf.Clamp01(flowStrength01);
            if (points != null)
            {
                Points.AddRange(points);
            }
        }
    }

    [Serializable]
    public sealed class RegionalRouteCorridorRecord
    {
        public string CorridorId { get; private set; }
        public RegionalRouteCorridorKind Kind { get; private set; }
        public string SourceSettlementId { get; private set; }
        public string DestinationSettlementId { get; private set; }
        public string SourceParcelId { get; private set; }
        public string DestinationParcelId { get; private set; }
        public float LengthMeters { get; private set; }
        public float Practicality01 { get; private set; }
        public float SeasonalReliability01 { get; private set; }
        public float FreightBurden01 { get; private set; }
        public RegionalRouteConstraintKind PrimaryConstraintKind { get; private set; }
        public int WaterCrossingCount { get; private set; }
        public int WetGroundCrossingCount { get; private set; }
        public float BridgeOrFordNeed01 { get; private set; }
        public float MudSeasonRisk01 { get; private set; }
        public float GradeBurden01 { get; private set; }
        public string ConstraintSummary { get; private set; }
        public string DebugSummary { get; private set; }
        public List<Vector2> Points { get; private set; } = new();

        public RegionalRouteCorridorRecord(
            string corridorId,
            RegionalRouteCorridorKind kind,
            string sourceSettlementId,
            string destinationSettlementId,
            string sourceParcelId,
            string destinationParcelId,
            float lengthMeters,
            float practicality01,
            float seasonalReliability01,
            float freightBurden01,
            string debugSummary,
            IReadOnlyList<Vector2> points,
            RegionalRouteConstraintKind primaryConstraintKind = RegionalRouteConstraintKind.None,
            int waterCrossingCount = 0,
            int wetGroundCrossingCount = 0,
            float bridgeOrFordNeed01 = 0f,
            float mudSeasonRisk01 = 0f,
            float gradeBurden01 = 0f,
            string constraintSummary = "")
        {
            CorridorId = corridorId ?? string.Empty;
            Kind = kind;
            SourceSettlementId = sourceSettlementId ?? string.Empty;
            DestinationSettlementId = destinationSettlementId ?? string.Empty;
            SourceParcelId = sourceParcelId ?? string.Empty;
            DestinationParcelId = destinationParcelId ?? string.Empty;
            LengthMeters = Mathf.Max(0f, lengthMeters);
            Practicality01 = Mathf.Clamp01(practicality01);
            SeasonalReliability01 = Mathf.Clamp01(seasonalReliability01);
            FreightBurden01 = Mathf.Clamp01(freightBurden01);
            PrimaryConstraintKind = primaryConstraintKind;
            WaterCrossingCount = Mathf.Max(0, waterCrossingCount);
            WetGroundCrossingCount = Mathf.Max(0, wetGroundCrossingCount);
            BridgeOrFordNeed01 = Mathf.Clamp01(bridgeOrFordNeed01);
            MudSeasonRisk01 = Mathf.Clamp01(mudSeasonRisk01);
            GradeBurden01 = Mathf.Clamp01(gradeBurden01);
            ConstraintSummary = constraintSummary ?? string.Empty;
            DebugSummary = debugSummary ?? string.Empty;
            if (points != null)
            {
                Points.AddRange(points);
            }
        }

        public string BuildRoutePlanningReadout()
        {
            string reliability = SeasonalReliability01 >= 0.72f
                ? "reliable wagon passage"
                : SeasonalReliability01 >= 0.45f ? "seasonally workable passage" : "fragile seasonal passage";
            string burden = FreightBurden01 >= 0.68f
                ? "heavy freight burden"
                : FreightBurden01 >= 0.38f ? "moderate freight burden" : "light freight burden";
            string practicality = Practicality01 >= 0.70f
                ? "practical corridor"
                : Practicality01 >= 0.42f ? "workable corridor" : "awkward corridor";
            string constraint = PrimaryConstraintKind == RegionalRouteConstraintKind.None
                ? "route constraints pending"
                : PrimaryConstraintKind == RegionalRouteConstraintKind.StableDryTrack ? "stable dry track" : PrimaryConstraintKind.ToString();
            return $"{CorridorId}: {Kind}; {LengthMeters:0}m; {practicality}; {reliability}; {burden}; {constraint}.";
        }

        public string BuildDetailedRouteInspectionReadout()
        {
            StringBuilder builder = new();
            builder.AppendLine($"Route {CorridorId} | {Kind} | {LengthMeters:0}m");
            builder.AppendLine($"Connects: {FormatEndpoint(SourceSettlementId, SourceParcelId)} -> {FormatEndpoint(DestinationSettlementId, DestinationParcelId)}");
            builder.AppendLine($"Performance: practicality {Practicality01:P0}, seasonal reliability {SeasonalReliability01:P0}, freight burden {FreightBurden01:P0}");
            builder.AppendLine($"Constraints: {PrimaryConstraintKind}; water crossings {WaterCrossingCount}, wet-ground crossings {WetGroundCrossingCount}, bridge/ford need {BridgeOrFordNeed01:P0}, mud risk {MudSeasonRisk01:P0}, grade burden {GradeBurden01:P0}");
            builder.AppendLine("Owner read: " + BuildRouteOwnerReadout());

            if (!string.IsNullOrWhiteSpace(ConstraintSummary))
            {
                builder.AppendLine("Constraint read: " + ConstraintSummary);
            }

            if (!string.IsNullOrWhiteSpace(DebugSummary))
            {
                builder.AppendLine("Generator reason: " + DebugSummary);
            }

            return builder.ToString().Trim();
        }

        public string BuildRouteOwnerReadout()
        {
            if (Practicality01 >= 0.72f && SeasonalReliability01 >= 0.68f && FreightBurden01 <= 0.38f)
            {
                return "strong practical corridor; suitable as a future default freight or settlement connector.";
            }

            if (FreightBurden01 >= 0.68f)
            {
                return "freight-heavy corridor; future livery, wagon capacity, depot siting, or route work should care about this.";
            }

            if (BridgeOrFordNeed01 >= 0.55f || WaterCrossingCount > 0)
            {
                return "water crossing pressure; future bridge, ford, seasonal delay, or freight surcharge logic should use this route.";
            }

            if (MudSeasonRisk01 >= 0.55f || WetGroundCrossingCount > 0)
            {
                return "mud-season risk; do not let cargo teleport through this corridor without lead time or reliability cost.";
            }

            if (GradeBurden01 >= 0.55f || PrimaryConstraintKind == RegionalRouteConstraintKind.SteepGrade)
            {
                return "grade burden; heavy freight, rail corridors, and team capacity should treat this as expensive ground.";
            }

            if (Practicality01 <= 0.42f || SeasonalReliability01 <= 0.42f)
            {
                return "awkward corridor; usable as a rough track, but weak for dependable trade until improved.";
            }

            return "workable wagon-era connector; good enough for backend logistics but still worth monitoring.";
        }

        private static string FormatEndpoint(string settlementId, string parcelId)
        {
            if (!string.IsNullOrWhiteSpace(settlementId))
            {
                return settlementId;
            }

            if (!string.IsNullOrWhiteSpace(parcelId))
            {
                return parcelId;
            }

            return "regional point";
        }
    }

    [Serializable]
    public sealed class RegionalFoundationIssueRecord
    {
        public RegionalFoundationIssueSeverity Severity { get; private set; }
        public RegionalFoundationIssueCategory Category { get; private set; }
        public string SubjectId { get; private set; }
        public string Message { get; private set; }
        public string Recommendation { get; private set; }
        public bool HasLocation { get; private set; }
        public Vector2 LocationMeters { get; private set; }

        public RegionalFoundationIssueRecord(
            RegionalFoundationIssueSeverity severity,
            RegionalFoundationIssueCategory category,
            string subjectId,
            string message,
            string recommendation = "",
            bool hasLocation = false,
            Vector2 locationMeters = default)
        {
            Severity = severity;
            Category = category;
            SubjectId = subjectId ?? string.Empty;
            Message = message ?? string.Empty;
            Recommendation = recommendation ?? string.Empty;
            HasLocation = hasLocation;
            LocationMeters = locationMeters;
        }

        public string BuildReadout()
        {
            string subject = string.IsNullOrWhiteSpace(SubjectId) ? Category.ToString() : SubjectId;
            string recommendation = string.IsNullOrWhiteSpace(Recommendation) ? string.Empty : $" Recommended: {Recommendation}";
            return $"{Severity} / {Category} / {subject}: {Message}{recommendation}";
        }
    }

    [Serializable]
    public sealed class RegionalFoundationOpportunityRecord
    {
        public RegionalFoundationOpportunityKind Kind { get; private set; }
        public string SubjectId { get; private set; }
        public bool HasLocation { get; private set; }
        public Vector2 LocationMeters { get; private set; }
        public float Score01 { get; private set; }
        public string Title { get; private set; }
        public string Readout { get; private set; }
        public string Recommendation { get; private set; }

        public RegionalFoundationOpportunityRecord(
            RegionalFoundationOpportunityKind kind,
            string subjectId,
            float score01,
            string title,
            string readout,
            string recommendation = "",
            bool hasLocation = false,
            Vector2 locationMeters = default)
        {
            Kind = kind;
            SubjectId = subjectId ?? string.Empty;
            Score01 = Mathf.Clamp01(score01);
            Title = title ?? string.Empty;
            Readout = readout ?? string.Empty;
            Recommendation = recommendation ?? string.Empty;
            HasLocation = hasLocation;
            LocationMeters = locationMeters;
        }

        public string BuildReadout()
        {
            string subject = string.IsNullOrWhiteSpace(SubjectId) ? Kind.ToString() : SubjectId;
            string recommendation = string.IsNullOrWhiteSpace(Recommendation) ? string.Empty : $" Recommended: {Recommendation}";
            return $"{Title}: {subject}; score {Score01:P0}. {Readout}{recommendation}";
        }
    }

    [Serializable]
    public sealed class RegionalFoundationInspectionReport
    {
        public int Seed { get; private set; }
        public RegionalTerrainRecipeFamily RecipeFamily { get; private set; }
        public string Summary { get; private set; }
        public int InfoCount { get; private set; }
        public int WarningCount { get; private set; }
        public int ErrorCount { get; private set; }
        public List<RegionalFoundationIssueRecord> Issues { get; private set; } = new();
        public List<RegionalFoundationOpportunityRecord> OpportunityReadouts { get; private set; } = new();

        public int OpportunityCount => OpportunityReadouts.Count;
        public bool HasErrors => ErrorCount > 0;
        public bool HasWarnings => WarningCount > 0;
        public bool HasBlockingIssues => ErrorCount > 0;

        public RegionalFoundationInspectionReport(
            int seed,
            RegionalTerrainRecipeFamily recipeFamily,
            string summary,
            IReadOnlyList<RegionalFoundationIssueRecord> issues,
            IReadOnlyList<RegionalFoundationOpportunityRecord> opportunityReadouts = null)
        {
            Seed = Mathf.Max(0, seed);
            RecipeFamily = recipeFamily;
            Summary = summary ?? string.Empty;
            if (issues != null)
            {
                Issues.AddRange(issues);
            }

            if (opportunityReadouts != null)
            {
                OpportunityReadouts.AddRange(opportunityReadouts);
            }

            for (int i = 0; i < Issues.Count; i++)
            {
                RegionalFoundationIssueRecord issue = Issues[i];
                if (issue == null)
                {
                    continue;
                }

                switch (issue.Severity)
                {
                    case RegionalFoundationIssueSeverity.Error:
                        ErrorCount++;
                        break;
                    case RegionalFoundationIssueSeverity.Warning:
                        WarningCount++;
                        break;
                    default:
                        InfoCount++;
                        break;
                }
            }
        }

        public string BuildCompactReadout()
        {
            string status = ErrorCount > 0 ? "needs correction" : WarningCount > 0 ? "usable with warnings" : "healthy";
            return $"Regional foundation check: {status}; seed {Seed}; {RecipeFamily}; errors {ErrorCount}, warnings {WarningCount}, notes {InfoCount}, opportunity readouts {OpportunityReadouts.Count}. {Summary}";
        }

        public string BuildOpportunityDigest(int maxItems = 3)
        {
            if (OpportunityReadouts.Count == 0 || maxItems <= 0)
            {
                return string.Empty;
            }

            int count = Mathf.Min(maxItems, OpportunityReadouts.Count);
            List<string> parts = new();
            for (int i = 0; i < count; i++)
            {
                RegionalFoundationOpportunityRecord opportunity = OpportunityReadouts[i];
                if (opportunity == null)
                {
                    continue;
                }

                parts.Add(opportunity.BuildReadout());
            }

            return parts.Count == 0 ? string.Empty : "Regional opportunities: " + string.Join(" | ", parts);
        }

        public string BuildFirstIssueReadout()
        {
            for (int i = 0; i < Issues.Count; i++)
            {
                if (Issues[i] != null && Issues[i].Severity != RegionalFoundationIssueSeverity.Info)
                {
                    return Issues[i].BuildReadout();
                }
            }

            return string.Empty;
        }
    }

    [Serializable]
    public sealed class RegionalSurveyParcelRecord
    {
        public string ParcelId { get; private set; }
        public string SectionId { get; private set; }
        public string TractId { get; private set; }
        public RegionalParcelKind ParcelKind { get; private set; }
        public Rect BoundsMeters { get; private set; }
        public float Acreage { get; private set; }
        public float MeanSlope01 { get; private set; }
        public float Wetness01 { get; private set; }
        public float WaterAccess01 { get; private set; }
        public float FarmSuitability01 { get; private set; }
        public float BuildSuitability01 { get; private set; }
        public float ResourceSuitability01 { get; private set; }
        public float SupportBurden01 { get; private set; }
        public RegionalParcelFrontageClass FrontageClass { get; private set; }
        public RegionalParcelAccessQuality AccessQuality { get; private set; }
        public RegionalParcelProvenance Provenance { get; private set; }
        public RegionalRemoteSuitability RemoteSuitability { get; private set; }
        public float TerrainBurden01 { get; private set; }
        public float DevelopmentReadiness01 { get; private set; }
        public string DistrictContext { get; private set; }
        public string DebugReason { get; private set; }
        public RegionalParcelCorridorRelation CorridorRelation { get; private set; }
        public string NearestCorridorId { get; private set; }
        public RegionalRouteCorridorKind NearestCorridorKind { get; private set; }
        public float DistanceToCorridorMeters { get; private set; }
        public float CorridorInfluence01 { get; private set; }
        public float FreightAccess01 { get; private set; }
        public float CorridorBurden01 { get; private set; }
        public string CorridorContext { get; private set; }

        public Vector2 CenterMeters => BoundsMeters.center;

        public RegionalSurveyParcelRecord(
            string parcelId,
            string sectionId,
            string tractId,
            RegionalParcelKind parcelKind,
            Rect boundsMeters,
            float acreage,
            float meanSlope01,
            float wetness01,
            float waterAccess01,
            float farmSuitability01,
            float buildSuitability01,
            float resourceSuitability01,
            float supportBurden01,
            RegionalParcelFrontageClass frontageClass = RegionalParcelFrontageClass.None,
            RegionalParcelAccessQuality accessQuality = RegionalParcelAccessQuality.None,
            RegionalParcelProvenance provenance = RegionalParcelProvenance.Unset,
            RegionalRemoteSuitability remoteSuitability = RegionalRemoteSuitability.None,
            float terrainBurden01 = 0f,
            float developmentReadiness01 = 0f,
            string districtContext = "",
            string debugReason = "",
            RegionalParcelCorridorRelation corridorRelation = RegionalParcelCorridorRelation.None,
            string nearestCorridorId = "",
            RegionalRouteCorridorKind nearestCorridorKind = RegionalRouteCorridorKind.SettlementConnector,
            float distanceToCorridorMeters = 0f,
            float corridorInfluence01 = 0f,
            float freightAccess01 = 0f,
            float corridorBurden01 = 0f,
            string corridorContext = "")
        {
            ParcelId = parcelId ?? string.Empty;
            SectionId = sectionId ?? string.Empty;
            TractId = tractId ?? string.Empty;
            ParcelKind = parcelKind;
            BoundsMeters = boundsMeters;
            Acreage = Mathf.Max(0.01f, acreage);
            MeanSlope01 = Mathf.Clamp01(meanSlope01);
            Wetness01 = Mathf.Clamp01(wetness01);
            WaterAccess01 = Mathf.Clamp01(waterAccess01);
            FarmSuitability01 = Mathf.Clamp01(farmSuitability01);
            BuildSuitability01 = Mathf.Clamp01(buildSuitability01);
            ResourceSuitability01 = Mathf.Clamp01(resourceSuitability01);
            SupportBurden01 = Mathf.Clamp01(supportBurden01);
            FrontageClass = frontageClass;
            AccessQuality = accessQuality;
            Provenance = provenance;
            RemoteSuitability = remoteSuitability;
            TerrainBurden01 = Mathf.Clamp01(terrainBurden01);
            DevelopmentReadiness01 = Mathf.Clamp01(developmentReadiness01);
            DistrictContext = districtContext ?? string.Empty;
            DebugReason = debugReason ?? string.Empty;
            CorridorRelation = corridorRelation;
            NearestCorridorId = nearestCorridorId ?? string.Empty;
            NearestCorridorKind = nearestCorridorKind;
            DistanceToCorridorMeters = Mathf.Max(0f, distanceToCorridorMeters);
            CorridorInfluence01 = Mathf.Clamp01(corridorInfluence01);
            FreightAccess01 = Mathf.Clamp01(freightAccess01);
            CorridorBurden01 = Mathf.Clamp01(corridorBurden01);
            CorridorContext = corridorContext ?? string.Empty;
        }

        public string BuildParcelPlanningReadout()
        {
            string access = FormatAccessQuality(AccessQuality);
            string readiness = FormatDevelopmentReadiness(DevelopmentReadiness01);
            string remote = RemoteSuitability == RegionalRemoteSuitability.None
                ? "ordinary parcel"
                : FormatRemoteSuitability(RemoteSuitability);
            string corridor = CorridorRelation == RegionalParcelCorridorRelation.None
                ? "route context pending"
                : CorridorRelation == RegionalParcelCorridorRelation.OffRoute ? "off-route tract" : $"{CorridorRelation} influence {CorridorInfluence01:P0}";
            return $"{ParcelId}: {ParcelKind}, {Acreage:0.#} acres, {access}, {readiness}, {remote}; {corridor}; {ResolveRecommendedUseLabel()}. {BuildOwnerActionReadout()}";
        }

        public string BuildParcelInspectionReadout()
        {
            StringBuilder builder = new();
            builder.AppendLine($"Parcel {ParcelId} | {ParcelKind} | {Acreage:0.#} acres");
            builder.AppendLine($"Survey: {SectionId} / {TractId} | Provenance: {Provenance} | Frontage: {FrontageClass} | Access: {FormatAccessQuality(AccessQuality)}");
            builder.AppendLine($"Recommended use: {ResolveRecommendedUseLabel()} | Priority: {ResolveUsePriorityLabel()}");
            builder.AppendLine($"Suitability: build {BuildSuitability01:P0}, farm {FarmSuitability01:P0}, resource {ResourceSuitability01:P0}, timber hook {CalculateTimberSuitability01():P0}, mineral hook {CalculateMineralSuitability01():P0}, freight hook {CalculateFreightOutpostSuitability01():P0}");
            builder.AppendLine($"Burden: support {SupportBurden01:P0}, terrain {TerrainBurden01:P0}, wetness {Wetness01:P0}, slope {MeanSlope01:P0}, water access {WaterAccess01:P0}, development readiness {DevelopmentReadiness01:P0}");
            builder.AppendLine(BuildLogisticsSuitabilityReadout());
            builder.AppendLine(BuildResourceSuitabilityReadout());
            builder.AppendLine("Future hooks: " + BuildFutureHookReadout());
            builder.AppendLine("Owner read: " + BuildOwnerActionReadout());

            if (!string.IsNullOrWhiteSpace(DistrictContext))
            {
                builder.AppendLine("District context: " + DistrictContext);
            }

            if (!string.IsNullOrWhiteSpace(CorridorContext))
            {
                builder.AppendLine("Route context: " + CorridorContext);
            }

            if (!string.IsNullOrWhiteSpace(DebugReason))
            {
                builder.AppendLine("Generator reason: " + DebugReason);
            }

            return builder.ToString().Trim();
        }

        public string BuildResourceSuitabilityReadout()
        {
            float timber = CalculateTimberSuitability01();
            float mineral = CalculateMineralSuitability01();
            float freight = CalculateFreightOutpostSuitability01();
            float industrial = CalculateRemoteIndustrySuitability01();
            string primary = ResolvePrimaryResourceHookLabel(timber, mineral, freight, industrial);
            return $"Resource hooks: {primary}; timber {timber:P0}, mineral {mineral:P0}, freight/outpost {freight:P0}, remote industry {industrial:P0}.";
        }

        public string BuildLogisticsSuitabilityReadout()
        {
            string corridor = string.IsNullOrWhiteSpace(NearestCorridorId)
                ? "no named corridor yet"
                : $"nearest corridor {NearestCorridorId} ({NearestCorridorKind}) at {DistanceToCorridorMeters:0}m";
            string freight = FreightAccess01 >= 0.70f
                ? "strong freight access"
                : FreightAccess01 >= 0.42f ? "workable freight access" : "weak freight access";
            string burden = CorridorBurden01 >= 0.66f
                ? "heavy corridor burden"
                : CorridorBurden01 >= 0.36f ? "moderate corridor burden" : "light corridor burden";
            return $"Logistics: {freight}; {burden}; {corridor}; corridor influence {CorridorInfluence01:P0}.";
        }

        public string BuildFutureHookReadout()
        {
            List<string> hooks = new();
            if (DevelopmentReadiness01 >= 0.58f && BuildSuitability01 >= 0.50f)
            {
                hooks.Add("near-term acquisition/development candidate");
            }

            if (FarmSuitability01 >= 0.58f || RemoteSuitability is RegionalRemoteSuitability.FarmService or RegionalRemoteSuitability.RanchRange)
            {
                hooks.Add("farm/ranch service land");
            }

            if (CalculateTimberSuitability01() >= 0.52f || RemoteSuitability == RegionalRemoteSuitability.TimberWorksite)
            {
                hooks.Add("logging/timber stand hook");
            }

            if (CalculateMineralSuitability01() >= 0.54f || RemoteSuitability == RegionalRemoteSuitability.MineralProspect)
            {
                hooks.Add("claim/prospect/mineral district hook");
            }

            if (FreightAccess01 >= 0.48f || RemoteSuitability == RegionalRemoteSuitability.FreightOutpost)
            {
                hooks.Add("freight/livery route support hook");
            }

            if (SupportBurden01 >= 0.56f && (BuildSuitability01 >= 0.42f || FreightAccess01 >= 0.42f))
            {
                hooks.Add("remote support or settlement-node pressure");
            }

            return hooks.Count == 0 ? "ordinary flexible parcel; keep available for future acquisition, valuation, or settlement pressure" : string.Join(", ", hooks);
        }

        public string BuildOwnerActionReadout()
        {
            if (DevelopmentReadiness01 >= 0.72f && SupportBurden01 <= 0.40f)
            {
                return "good near-term parcel; low support burden means it can enter play without major regional systems.";
            }

            if (RemoteSuitability == RegionalRemoteSuitability.MineralProspect || CalculateMineralSuitability01() >= 0.62f)
            {
                return FreightAccess01 >= 0.42f
                    ? "hold for prospecting/claim logic; access is plausible enough for later mining diligence."
                    : "do not treat as an instant mine; mineral promise exists but freight/access must be solved first.";
            }

            if (RemoteSuitability == RegionalRemoteSuitability.TimberWorksite || CalculateTimberSuitability01() >= 0.58f)
            {
                return FreightAccess01 >= 0.38f
                    ? "strong future timber/logging candidate; pair with hauling, camp, or sawmill support later."
                    : "timber promise exists, but it needs route or hauling support before it becomes useful.";
            }

            if (RemoteSuitability == RegionalRemoteSuitability.FreightOutpost || FreightAccess01 >= 0.62f)
            {
                return "watch as a freight support parcel; useful for livery, staging, route work, or later corridor value.";
            }

            if (SupportBurden01 >= 0.66f)
            {
                return "high support burden; avoid early development unless a specific resource or route reason justifies it.";
            }

            if (BuildSuitability01 >= 0.52f || FarmSuitability01 >= 0.52f)
            {
                return "practical flexible land; best value depends on local demand, ownership, and future settlement pressure.";
            }

            return "speculative or background parcel; keep readable in acquisition/valuation, but do not force early gameplay here.";
        }

        public float CalculateTimberSuitability01()
        {
            float remoteBias = RemoteSuitability == RegionalRemoteSuitability.TimberWorksite ? 0.30f : 0f;
            float wetDraw = Mathf.Clamp01(WaterAccess01 * 0.36f + Wetness01 * 0.18f + (1f - MeanSlope01) * 0.16f);
            float corridor = Mathf.Clamp01(FreightAccess01 * 0.16f + CorridorInfluence01 * 0.10f);
            float kindBias = ParcelKind == RegionalParcelKind.FloodplainTract ? 0.08f : ParcelKind == RegionalParcelKind.RoughParcel ? 0.05f : 0f;
            return Mathf.Clamp01(wetDraw + corridor + kindBias + remoteBias);
        }

        public float CalculateMineralSuitability01()
        {
            float remoteBias = RemoteSuitability == RegionalRemoteSuitability.MineralProspect ? 0.26f : 0f;
            float roughness = Mathf.Clamp01(MeanSlope01 * 0.26f + TerrainBurden01 * 0.16f);
            float resource = ResourceSuitability01 * 0.42f;
            float kindBias = ParcelKind == RegionalParcelKind.RoughParcel ? 0.14f : 0f;
            float accessPenalty = AccessQuality <= RegionalParcelAccessQuality.Poor ? -0.06f : 0f;
            return Mathf.Clamp01(resource + roughness + kindBias + remoteBias + accessPenalty);
        }

        public float CalculateFreightOutpostSuitability01()
        {
            float access = ResolveAccessScore01(AccessQuality) * 0.32f;
            float corridor = FreightAccess01 * 0.32f + CorridorInfluence01 * 0.18f;
            float burden = Mathf.Clamp01(SupportBurden01 * 0.14f + CorridorBurden01 * 0.14f);
            float remoteBias = RemoteSuitability == RegionalRemoteSuitability.FreightOutpost ? 0.18f : 0f;
            return Mathf.Clamp01(access + corridor + burden + remoteBias - Wetness01 * 0.08f);
        }

        public float CalculateRemoteIndustrySuitability01()
        {
            float resource = Mathf.Max(CalculateTimberSuitability01(), CalculateMineralSuitability01());
            float logistics = Mathf.Clamp01(FreightAccess01 * 0.34f + ResolveAccessScore01(AccessQuality) * 0.26f + (1f - SupportBurden01) * 0.18f);
            float development = DevelopmentReadiness01 * 0.18f;
            return Mathf.Clamp01(resource * 0.50f + logistics + development);
        }

        public string ResolveRecommendedUseLabel()
        {
            if (ParcelKind == RegionalParcelKind.TownPlatCore)
            {
                return "town-core frontage / civic-commercial anchor";
            }

            if (ParcelKind == RegionalParcelKind.TownPlatEdge || ParcelKind == RegionalParcelKind.EdgeExpansion)
            {
                return DevelopmentReadiness01 >= 0.55f ? "edge expansion / settlement support" : "future edge expansion with site work";
            }

            if (RemoteSuitability == RegionalRemoteSuitability.MineralProspect || CalculateMineralSuitability01() >= 0.62f)
            {
                return "mineral prospect / claim district hook";
            }

            if (RemoteSuitability == RegionalRemoteSuitability.TimberWorksite || CalculateTimberSuitability01() >= 0.58f)
            {
                return "timber worksite / logging support hook";
            }

            if (RemoteSuitability == RegionalRemoteSuitability.FreightOutpost || CalculateFreightOutpostSuitability01() >= 0.62f)
            {
                return "freight outpost / route service hook";
            }

            if (RemoteSuitability == RegionalRemoteSuitability.RanchRange || (FarmSuitability01 >= 0.48f && MeanSlope01 >= 0.34f))
            {
                return "ranch range / livestock support tract";
            }

            if (RemoteSuitability == RegionalRemoteSuitability.FarmService || FarmSuitability01 >= 0.58f)
            {
                return "farm-service / crop-support tract";
            }

            if (ParcelKind == RegionalParcelKind.FloodplainTract)
            {
                return "water-influenced tract with seasonal risk";
            }

            if (BuildSuitability01 >= 0.56f)
            {
                return "flexible rural development tract";
            }

            return "speculative surveyed rural tract";
        }

        private string ResolveUsePriorityLabel()
        {
            float score = Mathf.Max(DevelopmentReadiness01, Mathf.Max(CalculateRemoteIndustrySuitability01(), CalculateFreightOutpostSuitability01()));
            if (score >= 0.72f)
            {
                return "high";
            }

            if (score >= 0.52f)
            {
                return "medium";
            }

            return "low / future";
        }

        private static string ResolvePrimaryResourceHookLabel(float timber, float mineral, float freight, float industrial)
        {
            float best = Mathf.Max(Mathf.Max(timber, mineral), Mathf.Max(freight, industrial));
            if (best < 0.36f)
            {
                return "no strong resource hook yet";
            }

            if (mineral >= timber && mineral >= freight && mineral >= industrial)
            {
                return "mineral/claim diligence";
            }

            if (timber >= freight && timber >= industrial)
            {
                return "timber/logging diligence";
            }

            if (freight >= industrial)
            {
                return "freight staging / route support";
            }

            return "remote industrial support";
        }

        private static string FormatAccessQuality(RegionalParcelAccessQuality access)
        {
            return access switch
            {
                RegionalParcelAccessQuality.Gateway => "gateway access",
                RegionalParcelAccessQuality.RoadFrontage => "road frontage",
                RegionalParcelAccessQuality.WagonReachable => "wagon reachable",
                RegionalParcelAccessQuality.SeasonalTrack => "seasonal track",
                RegionalParcelAccessQuality.Poor => "poor access",
                _ => "unspecified access"
            };
        }

        private static string FormatDevelopmentReadiness(float readiness01)
        {
            return readiness01 >= 0.72f
                ? "ready for near-term use"
                : readiness01 >= 0.45f ? "usable with preparation" : "needs meaningful site work";
        }

        private static string FormatRemoteSuitability(RegionalRemoteSuitability suitability)
        {
            return suitability switch
            {
                RegionalRemoteSuitability.FarmService => "farm-service pocket",
                RegionalRemoteSuitability.RanchRange => "ranch-range tract",
                RegionalRemoteSuitability.TimberWorksite => "timber worksite",
                RegionalRemoteSuitability.MineralProspect => "mineral prospect",
                RegionalRemoteSuitability.FreightOutpost => "freight outpost",
                RegionalRemoteSuitability.MixedOpportunity => "mixed remote opportunity",
                _ => "ordinary parcel"
            };
        }

        private static float ResolveAccessScore01(RegionalParcelAccessQuality access)
        {
            return access switch
            {
                RegionalParcelAccessQuality.Gateway => 1f,
                RegionalParcelAccessQuality.RoadFrontage => 0.82f,
                RegionalParcelAccessQuality.WagonReachable => 0.62f,
                RegionalParcelAccessQuality.SeasonalTrack => 0.36f,
                RegionalParcelAccessQuality.Poor => 0.12f,
                _ => 0f
            };
        }
    }

    [Serializable]
    public sealed class RegionalAnchorTownRecord
    {
        public string AnchorId { get; private set; } = string.Empty;
        public Vector2 CenterMeters { get; private set; }
        public float SuitabilityScore01 { get; private set; }
        public float NearestWaterDistanceMeters { get; private set; }
        public float BuildableLand01 { get; private set; }
        public float GatewayScore01 { get; private set; }
        public float ExpansionRoom01 { get; private set; }
        public string DebugReason { get; private set; } = string.Empty;

        public static RegionalAnchorTownRecord Create(
            string anchorId,
            Vector2 centerMeters,
            float suitabilityScore01,
            float nearestWaterDistanceMeters,
            float buildableLand01,
            float gatewayScore01,
            float expansionRoom01,
            string debugReason)
        {
            return new RegionalAnchorTownRecord
            {
                AnchorId = anchorId ?? string.Empty,
                CenterMeters = centerMeters,
                SuitabilityScore01 = Mathf.Clamp01(suitabilityScore01),
                NearestWaterDistanceMeters = Mathf.Max(0f, nearestWaterDistanceMeters),
                BuildableLand01 = Mathf.Clamp01(buildableLand01),
                GatewayScore01 = Mathf.Clamp01(gatewayScore01),
                ExpansionRoom01 = Mathf.Clamp01(expansionRoom01),
                DebugReason = debugReason ?? string.Empty
            };
        }
    }

    [Serializable]
    public sealed class RegionalVegetationZoneRecord
    {
        public string ZoneId { get; private set; }
        public RegionalBiomeKind BiomeKind { get; private set; }
        public RegionalVegetationKind VegetationKind { get; private set; }
        public Rect BoundsMeters { get; private set; }
        public float Density01 { get; private set; }
        public float Wetness01 { get; private set; }
        public float Slope01 { get; private set; }
        public string Notes { get; private set; }

        public RegionalVegetationZoneRecord(
            string zoneId,
            RegionalBiomeKind biomeKind,
            RegionalVegetationKind vegetationKind,
            Rect boundsMeters,
            float density01,
            float wetness01,
            float slope01,
            string notes)
        {
            ZoneId = zoneId ?? string.Empty;
            BiomeKind = biomeKind;
            VegetationKind = vegetationKind;
            BoundsMeters = boundsMeters;
            Density01 = Mathf.Clamp01(density01);
            Wetness01 = Mathf.Clamp01(wetness01);
            Slope01 = Mathf.Clamp01(slope01);
            Notes = notes ?? string.Empty;
        }
    }

    [Serializable]
    public sealed class RegionalSettlementClusterRecord
    {
        public string ClusterId { get; private set; }
        public Vector2 CenterMeters { get; private set; }
        public int HouseholdCount { get; private set; }
        public int BusinessCount { get; private set; }
        public int Population { get; private set; }
        public float ServiceGravity01 { get; private set; }
        public float FreightSupport01 { get; private set; }
        public float SupportBurden01 { get; private set; }
        public RegionalSettlementCharacter Character { get; private set; }
        public float Permanence01 { get; private set; }
        public float LocalConfidence01 { get; private set; }
        public float ServiceDeficit01 { get; private set; }
        public float DeclineRisk01 { get; private set; }
        public string DebugReason { get; private set; }

        public RegionalSettlementClusterRecord(
            string clusterId,
            Vector2 centerMeters,
            int householdCount,
            int businessCount,
            int population,
            float serviceGravity01,
            float freightSupport01,
            float supportBurden01,
            RegionalSettlementCharacter character = RegionalSettlementCharacter.Unset,
            float permanence01 = 0f,
            float localConfidence01 = 0f,
            float serviceDeficit01 = 0f,
            float declineRisk01 = 0f,
            string debugReason = "")
        {
            ClusterId = clusterId ?? string.Empty;
            CenterMeters = centerMeters;
            HouseholdCount = Mathf.Max(0, householdCount);
            BusinessCount = Mathf.Max(0, businessCount);
            Population = Mathf.Max(0, population);
            ServiceGravity01 = Mathf.Clamp01(serviceGravity01);
            FreightSupport01 = Mathf.Clamp01(freightSupport01);
            SupportBurden01 = Mathf.Clamp01(supportBurden01);
            Character = character;
            Permanence01 = Mathf.Clamp01(permanence01);
            LocalConfidence01 = Mathf.Clamp01(localConfidence01);
            ServiceDeficit01 = Mathf.Clamp01(serviceDeficit01);
            DeclineRisk01 = Mathf.Clamp01(declineRisk01);
            DebugReason = debugReason ?? string.Empty;
        }
    }

    [Serializable]
    public sealed class RegionalSettlementRecord
    {
        public string SettlementId { get; private set; }
        public string SourceClusterId { get; private set; }
        public Vector2 CenterMeters { get; private set; }
        public int HouseholdCount { get; private set; }
        public int BusinessCount { get; private set; }
        public int Population { get; private set; }
        public RegionalPopulationBand PopulationBand { get; private set; }
        public int PopulationBandMin { get; private set; }
        public int PopulationBandMax { get; private set; }
        public int NextTriggerWindowMinPopulation { get; private set; }
        public int NextTriggerWindowMaxPopulation { get; private set; }
        public RegionalSettlementLabel Label { get; private set; }
        public float ServiceGravity01 { get; private set; }
        public float FreightSupport01 { get; private set; }
        public float SupportBurden01 { get; private set; }
        public RegionalSettlementCharacter Character { get; private set; }
        public RegionalSettlementPermanence Permanence { get; private set; }
        public RegionalSettlementSuccessionRole SuccessionRole { get; private set; }
        public int RegionalHierarchyRank { get; private set; }
        public float RegionalGravity01 { get; private set; }
        public float FootholdChallenge01 { get; private set; }
        public float SuccessionReadiness01 { get; private set; }
        public string SuccessionContext { get; private set; }
        public float Permanence01 { get; private set; }
        public float LocalConfidence01 { get; private set; }
        public float ServiceDeficit01 { get; private set; }
        public float DeclineRisk01 { get; private set; }
        public string DebugSummary { get; private set; }

        public RegionalSettlementRecord(
            string settlementId,
            string sourceClusterId,
            Vector2 centerMeters,
            int householdCount,
            int businessCount,
            int population,
            RegionalPopulationBand populationBand,
            int populationBandMin,
            int populationBandMax,
            int nextTriggerWindowMinPopulation,
            int nextTriggerWindowMaxPopulation,
            RegionalSettlementLabel label,
            float serviceGravity01,
            float freightSupport01,
            float supportBurden01,
            string debugSummary)
            : this(
                settlementId,
                sourceClusterId,
                centerMeters,
                householdCount,
                businessCount,
                population,
                populationBand,
                populationBandMin,
                populationBandMax,
                nextTriggerWindowMinPopulation,
                nextTriggerWindowMaxPopulation,
                label,
                serviceGravity01,
                freightSupport01,
                supportBurden01,
                RegionalSettlementCharacter.Unset,
                RegionalSettlementPermanence.Unset,
                0f,
                0f,
                0f,
                0f,
                debugSummary)
        {
        }

        public RegionalSettlementRecord(
            string settlementId,
            string sourceClusterId,
            Vector2 centerMeters,
            int householdCount,
            int businessCount,
            int population,
            RegionalPopulationBand populationBand,
            int populationBandMin,
            int populationBandMax,
            int nextTriggerWindowMinPopulation,
            int nextTriggerWindowMaxPopulation,
            RegionalSettlementLabel label,
            float serviceGravity01,
            float freightSupport01,
            float supportBurden01,
            RegionalSettlementCharacter character,
            RegionalSettlementPermanence permanence,
            float permanence01,
            float localConfidence01,
            float serviceDeficit01,
            float declineRisk01,
            string debugSummary,
            RegionalSettlementSuccessionRole successionRole = RegionalSettlementSuccessionRole.Unset,
            int regionalHierarchyRank = 0,
            float regionalGravity01 = 0f,
            float footholdChallenge01 = 0f,
            float successionReadiness01 = 0f,
            string successionContext = "")
        {
            SettlementId = settlementId ?? string.Empty;
            SourceClusterId = sourceClusterId ?? string.Empty;
            CenterMeters = centerMeters;
            HouseholdCount = Mathf.Max(0, householdCount);
            BusinessCount = Mathf.Max(0, businessCount);
            Population = Mathf.Max(0, population);
            PopulationBand = populationBand;
            PopulationBandMin = Mathf.Max(0, populationBandMin);
            PopulationBandMax = Mathf.Max(PopulationBandMin, populationBandMax);
            NextTriggerWindowMinPopulation = Mathf.Max(PopulationBandMin, nextTriggerWindowMinPopulation);
            NextTriggerWindowMaxPopulation = Mathf.Max(NextTriggerWindowMinPopulation, nextTriggerWindowMaxPopulation);
            Label = label;
            ServiceGravity01 = Mathf.Clamp01(serviceGravity01);
            FreightSupport01 = Mathf.Clamp01(freightSupport01);
            SupportBurden01 = Mathf.Clamp01(supportBurden01);
            Character = character;
            Permanence = permanence;
            SuccessionRole = successionRole;
            RegionalHierarchyRank = Mathf.Max(0, regionalHierarchyRank);
            RegionalGravity01 = Mathf.Clamp01(regionalGravity01);
            FootholdChallenge01 = Mathf.Clamp01(footholdChallenge01);
            SuccessionReadiness01 = Mathf.Clamp01(successionReadiness01);
            SuccessionContext = successionContext ?? string.Empty;
            Permanence01 = Mathf.Clamp01(permanence01);
            LocalConfidence01 = Mathf.Clamp01(localConfidence01);
            ServiceDeficit01 = Mathf.Clamp01(serviceDeficit01);
            DeclineRisk01 = Mathf.Clamp01(declineRisk01);
            DebugSummary = debugSummary ?? string.Empty;
        }

        public string BuildVisiblePlanningReadout()
        {
            string identity = Label switch
            {
                RegionalSettlementLabel.Camp => "camp",
                RegionalSettlementLabel.Hamlet => "hamlet",
                RegionalSettlementLabel.Town => "town",
                RegionalSettlementLabel.LargeTown => "large town",
                _ => "local cluster"
            };
            string burden = SupportBurden01 >= 0.68f
                ? "support is stretched"
                : SupportBurden01 >= 0.38f ? "support is workable" : "support is close at hand";
            string permanence = Permanence switch
            {
                RegionalSettlementPermanence.RegionalCenterCandidate => "regional-center gravity is possible",
                RegionalSettlementPermanence.DurableSettlement => "durable local life is forming",
                RegionalSettlementPermanence.EmergingNode => "an emerging node is taking shape",
                RegionalSettlementPermanence.ThinButServiceable => "thin but serviceable occupation",
                RegionalSettlementPermanence.FragileOccupation => "fragile occupation",
                _ => "settlement footing is uncertain"
            };
            string risk = DeclineRisk01 >= 0.65f
                ? "decline risk is visible"
                : DeclineRisk01 >= 0.36f ? "stability still needs support" : "stability is not the main blocker";
            string succession = SuccessionRole switch
            {
                RegionalSettlementSuccessionRole.OpeningFoothold => "opening foothold, not a permanent destiny",
                RegionalSettlementSuccessionRole.LikelyRegionalSuccessor => "strong enough to challenge the regional hierarchy",
                RegionalSettlementSuccessionRole.PeerChallenger => "credible peer pressure on the opening town",
                RegionalSettlementSuccessionRole.RisingSecondaryNode => "rising secondary node",
                RegionalSettlementSuccessionRole.StalledOrDecliningNode => "likely false start without intervention",
                RegionalSettlementSuccessionRole.DependentSatellite => "dependent satellite",
                _ => "regional succession status uncertain"
            };
            return $"{Population} people around a {identity}; {burden}; {permanence}; {risk}; {succession}.";
        }

        public string BuildSuccessionPlanningReadout()
        {
            string context = string.IsNullOrWhiteSpace(SuccessionContext) ? "No succession context recorded." : SuccessionContext;
            return $"{SettlementId}: {SuccessionRole} / rank {RegionalHierarchyRank}; gravity {RegionalGravity01:P0}; foothold challenge {FootholdChallenge01:P0}; readiness {SuccessionReadiness01:P0}. {context}";
        }

        public string BuildDetailedSettlementInspectionReadout()
        {
            StringBuilder builder = new();
            builder.AppendLine($"Settlement {SettlementId} | {Label} | {Character} | {Permanence} | {SuccessionRole}");
            builder.AppendLine($"People: population {Population}, households {HouseholdCount}, businesses {BusinessCount} | Band: {PopulationBand}");
            builder.AppendLine($"Gravity: regional {RegionalGravity01:P0}, service {ServiceGravity01:P0}, freight support {FreightSupport01:P0}, foothold challenge {FootholdChallenge01:P0}, succession readiness {SuccessionReadiness01:P0}");
            builder.AppendLine($"Stability: permanence {Permanence01:P0}, confidence {LocalConfidence01:P0}, service deficit {ServiceDeficit01:P0}, support burden {SupportBurden01:P0}, decline risk {DeclineRisk01:P0}");
            builder.AppendLine("Planning read: " + BuildVisiblePlanningReadout());
            builder.AppendLine("Owner response: " + BuildSettlementOwnerResponseReadout());

            if (!string.IsNullOrWhiteSpace(SuccessionContext))
            {
                builder.AppendLine("Succession context: " + SuccessionContext);
            }

            if (!string.IsNullOrWhiteSpace(DebugSummary))
            {
                builder.AppendLine("Generator reason: " + DebugSummary);
            }

            return builder.ToString().Trim();
        }

        public string BuildSettlementOwnerResponseReadout()
        {
            if (SuccessionRole == RegionalSettlementSuccessionRole.OpeningFoothold)
            {
                return "starting foothold; keep it useful, but do not assume it must permanently dominate the region.";
            }

            if (SuccessionRole == RegionalSettlementSuccessionRole.LikelyRegionalSuccessor || SuccessionReadiness01 >= 0.72f)
            {
                return "serious successor pressure; future services, freight, land deals, and capital should be allowed to gather here.";
            }

            if (SuccessionRole == RegionalSettlementSuccessionRole.PeerChallenger || FootholdChallenge01 >= 0.55f)
            {
                return "credible peer node; watch business mix, housing, and route support before choosing whether to invest or contain it.";
            }

            if (SuccessionRole == RegionalSettlementSuccessionRole.RisingSecondaryNode || RegionalGravity01 >= 0.48f)
            {
                return "rising secondary node; useful target for boarding, freight, basic services, or remote supply links.";
            }

            if (SuccessionRole == RegionalSettlementSuccessionRole.StalledOrDecliningNode || DeclineRisk01 >= 0.58f)
            {
                return "fragile or declining node; it needs service coverage, housing, work, or freight support before expansion makes sense.";
            }

            if (SupportBurden01 >= 0.62f)
            {
                return "support is stretched; future logistics and settlement pressure should treat this node as expensive to sustain.";
            }

            if (ServiceDeficit01 >= 0.52f)
            {
                return "service-light node; good future pressure source for general store, blacksmith, doctor, boarding, freight, or basic civic hooks.";
            }

            return "dependent regional node; keep it readable and let actual activity decide whether it grows or fades.";
        }
    }

    [Serializable]
    public sealed class RegionalWorldState
    {
        private const float SquareMetersPerAcre = 4046.8564224f;

        public int Seed { get; private set; }
        public RegionalTerrainRecipeFamily RecipeFamily { get; private set; }
        public Vector2 RegionSizeMeters { get; private set; }
        public float SurveyCellSizeMeters { get; private set; }
        public float TerrainTileSizeMeters { get; private set; }
        public RegionalAnchorTownRecord AnchorTown { get; private set; } = RegionalAnchorTownRecord.Create(string.Empty, Vector2.zero, 0f, 0f, 0f, 0f, 0f, string.Empty);
        public List<RegionalTerrainTileRecord> TerrainTiles { get; private set; } = new();
        public List<RegionalWatercourseRecord> Watercourses { get; private set; } = new();
        public List<RegionalRouteCorridorRecord> RouteCorridors { get; private set; } = new();
        public List<RegionalSurveyParcelRecord> SurveyParcels { get; private set; } = new();
        public List<RegionalVegetationZoneRecord> VegetationZones { get; private set; } = new();
        public List<RegionalSettlementClusterRecord> SettlementClusters { get; private set; } = new();
        public List<RegionalSettlementRecord> Settlements { get; private set; } = new();

        public Vector2 RegionCenterMeters => RegionSizeMeters * 0.5f;
        public Vector2 OpeningTownLocalOriginMeters => AnchorTown != null ? AnchorTown.CenterMeters : Vector2.zero;

        public Vector2 RegionalToLocalMeters(Vector2 regionalPointMeters)
        {
            return regionalPointMeters - OpeningTownLocalOriginMeters;
        }

        public Vector2 LocalToRegionalMeters(Vector2 localPointMeters)
        {
            return localPointMeters + OpeningTownLocalOriginMeters;
        }

        public Rect RegionalToLocalRect(Rect regionalRectMeters)
        {
            Vector2 origin = OpeningTownLocalOriginMeters;
            return new Rect(
                regionalRectMeters.x - origin.x,
                regionalRectMeters.y - origin.y,
                regionalRectMeters.width,
                regionalRectMeters.height);
        }

        public Rect LocalToRegionalRect(Rect localRectMeters)
        {
            Vector2 origin = OpeningTownLocalOriginMeters;
            return new Rect(
                localRectMeters.x + origin.x,
                localRectMeters.y + origin.y,
                localRectMeters.width,
                localRectMeters.height);
        }

        public static RegionalWorldState Create(
            int seed,
            RegionalTerrainRecipeFamily recipeFamily,
            Vector2 regionSizeMeters,
            float surveyCellSizeMeters,
            float terrainTileSizeMeters,
            RegionalAnchorTownRecord anchorTown,
            List<RegionalTerrainTileRecord> terrainTiles,
            List<RegionalWatercourseRecord> watercourses,
            List<RegionalRouteCorridorRecord> routeCorridors,
            List<RegionalSurveyParcelRecord> surveyParcels,
            List<RegionalVegetationZoneRecord> vegetationZones,
            List<RegionalSettlementClusterRecord> settlementClusters,
            List<RegionalSettlementRecord> settlements)
        {
            return new RegionalWorldState
            {
                Seed = Mathf.Max(0, seed),
                RecipeFamily = recipeFamily,
                RegionSizeMeters = new Vector2(Mathf.Max(1f, regionSizeMeters.x), Mathf.Max(1f, regionSizeMeters.y)),
                SurveyCellSizeMeters = Mathf.Max(0.1f, surveyCellSizeMeters),
                TerrainTileSizeMeters = Mathf.Max(1f, terrainTileSizeMeters),
                AnchorTown = anchorTown ?? RegionalAnchorTownRecord.Create(string.Empty, Vector2.zero, 0f, 0f, 0f, 0f, 0f, string.Empty),
                TerrainTiles = terrainTiles ?? new List<RegionalTerrainTileRecord>(),
                Watercourses = watercourses ?? new List<RegionalWatercourseRecord>(),
                RouteCorridors = routeCorridors ?? new List<RegionalRouteCorridorRecord>(),
                SurveyParcels = surveyParcels ?? new List<RegionalSurveyParcelRecord>(),
                VegetationZones = vegetationZones ?? new List<RegionalVegetationZoneRecord>(),
                SettlementClusters = settlementClusters ?? new List<RegionalSettlementClusterRecord>(),
                Settlements = settlements ?? new List<RegionalSettlementRecord>()
            };
        }

        public string BuildRegionalGameplayReadinessSummary(int maxOpportunityItems = 4)
        {
            int nearTermParcels = 0;
            int timberCandidates = 0;
            int mineralCandidates = 0;
            int freightCandidates = 0;
            int highBurdenParcels = 0;
            RegionalSurveyParcelRecord bestParcel = null;
            RegionalSurveyParcelRecord bestRemote = null;
            float bestParcelScore = float.MinValue;
            float bestRemoteScore = float.MinValue;

            for (int i = 0; i < SurveyParcels.Count; i++)
            {
                RegionalSurveyParcelRecord parcel = SurveyParcels[i];
                if (parcel == null)
                {
                    continue;
                }

                if (parcel.DevelopmentReadiness01 >= 0.56f && parcel.BuildSuitability01 >= 0.46f && parcel.SupportBurden01 <= 0.58f)
                {
                    nearTermParcels++;
                }

                if (parcel.CalculateTimberSuitability01() >= 0.52f || parcel.RemoteSuitability == RegionalRemoteSuitability.TimberWorksite)
                {
                    timberCandidates++;
                }

                if (parcel.CalculateMineralSuitability01() >= 0.54f || parcel.RemoteSuitability == RegionalRemoteSuitability.MineralProspect)
                {
                    mineralCandidates++;
                }

                if (parcel.CalculateFreightOutpostSuitability01() >= 0.52f || parcel.RemoteSuitability == RegionalRemoteSuitability.FreightOutpost)
                {
                    freightCandidates++;
                }

                if (parcel.SupportBurden01 >= 0.66f)
                {
                    highBurdenParcels++;
                }

                float parcelScore = parcel.DevelopmentReadiness01 * 0.42f + parcel.BuildSuitability01 * 0.24f + parcel.FarmSuitability01 * 0.14f + parcel.FreightAccess01 * 0.12f + (1f - parcel.SupportBurden01) * 0.08f;
                if (parcelScore > bestParcelScore)
                {
                    bestParcelScore = parcelScore;
                    bestParcel = parcel;
                }

                float remoteScore = parcel.CalculateRemoteIndustrySuitability01();
                if (remoteScore > bestRemoteScore)
                {
                    bestRemoteScore = remoteScore;
                    bestRemote = parcel;
                }
            }

            int constrainedRoutes = 0;
            int highBurdenRoutes = 0;
            int weakRoutes = 0;
            for (int i = 0; i < RouteCorridors.Count; i++)
            {
                RegionalRouteCorridorRecord route = RouteCorridors[i];
                if (route == null)
                {
                    continue;
                }

                if (route.PrimaryConstraintKind != RegionalRouteConstraintKind.None && route.PrimaryConstraintKind != RegionalRouteConstraintKind.StableDryTrack)
                {
                    constrainedRoutes++;
                }

                if (route.FreightBurden01 >= 0.58f)
                {
                    highBurdenRoutes++;
                }

                if (route.Practicality01 <= 0.42f || route.SeasonalReliability01 <= 0.42f)
                {
                    weakRoutes++;
                }
            }

            int successorCandidates = 0;
            int stalledOrDeclining = 0;
            for (int i = 0; i < Settlements.Count; i++)
            {
                RegionalSettlementRecord settlement = Settlements[i];
                if (settlement == null)
                {
                    continue;
                }

                if (settlement.SuccessionRole is RegionalSettlementSuccessionRole.RisingSecondaryNode or RegionalSettlementSuccessionRole.PeerChallenger or RegionalSettlementSuccessionRole.LikelyRegionalSuccessor)
                {
                    successorCandidates++;
                }

                if (settlement.SuccessionRole == RegionalSettlementSuccessionRole.StalledOrDecliningNode || settlement.DeclineRisk01 >= 0.55f)
                {
                    stalledOrDeclining++;
                }
            }

            string bestParcelRead = bestParcel != null ? $"best parcel {bestParcel.ParcelId} ({bestParcel.ResolveRecommendedUseLabel()}, score {bestParcelScore:P0})" : "no best parcel";
            string bestRemoteRead = bestRemote != null ? $"best remote hook {bestRemote.ParcelId} ({bestRemote.ResolveRecommendedUseLabel()}, score {bestRemoteScore:P0})" : "no remote hook";
            RegionalFoundationInspectionReport report = BuildInspectionReport();
            string opportunityDigest = report != null ? report.BuildOpportunityDigest(Mathf.Max(0, maxOpportunityItems)) : string.Empty;
            string opportunityRead = string.IsNullOrWhiteSpace(opportunityDigest) ? string.Empty : " " + opportunityDigest;

            return $"Regional gameplay read: parcels {SurveyParcels.Count}, near-term {nearTermParcels}, timber {timberCandidates}, mineral {mineralCandidates}, freight {freightCandidates}, high-support-burden {highBurdenParcels}; routes {RouteCorridors.Count}, constrained {constrainedRoutes}, high-burden {highBurdenRoutes}, weak {weakRoutes}; settlements {Settlements.Count}, successor candidates {successorCandidates}, stalled/declining {stalledOrDeclining}; {bestParcelRead}; {bestRemoteRead}.{opportunityRead}";
        }

        public string BuildRegionalFoundationInspectionSummary(int maxOpportunityItems = 4, string authoringDebugSummary = null)
        {
            int remoteSuitableParcels = 0;
            int corridorInfluencedParcels = 0;
            int highFreightAccessParcels = 0;
            int routeReadyTiles = 0;
            int corridorInfluencedTiles = 0;
            int highBurdenRoutes = 0;
            int constrainedRoutes = 0;
            int waterCrossings = 0;
            int highMudRiskRoutes = 0;
            int successionChallengers = 0;
            int stalledSettlementNodes = 0;
            float strongestFootholdChallenge = 0f;
            float readinessSum = 0f;
            float burdenSum = 0f;
            float routePracticalitySum = 0f;
            float routeBurdenSum = 0f;

            for (int i = 0; i < TerrainTiles.Count; i++)
            {
                RegionalTerrainTileRecord tile = TerrainTiles[i];
                if (tile == null)
                {
                    continue;
                }

                if (tile.RouteActivationReadiness01 >= 0.42f) routeReadyTiles++;
                if (tile.RouteInfluence01 > 0.05f) corridorInfluencedTiles++;
            }

            for (int i = 0; i < SurveyParcels.Count; i++)
            {
                RegionalSurveyParcelRecord parcel = SurveyParcels[i];
                if (parcel == null)
                {
                    continue;
                }

                readinessSum += parcel.DevelopmentReadiness01;
                burdenSum += parcel.SupportBurden01;
                if (parcel.RemoteSuitability != RegionalRemoteSuitability.None) remoteSuitableParcels++;
                if (parcel.CorridorRelation != RegionalParcelCorridorRelation.None && parcel.CorridorRelation != RegionalParcelCorridorRelation.OffRoute) corridorInfluencedParcels++;
                if (parcel.FreightAccess01 >= 0.58f) highFreightAccessParcels++;
            }

            for (int i = 0; i < RouteCorridors.Count; i++)
            {
                RegionalRouteCorridorRecord route = RouteCorridors[i];
                if (route == null)
                {
                    continue;
                }

                routePracticalitySum += route.Practicality01;
                routeBurdenSum += route.FreightBurden01;
                if (route.FreightBurden01 >= 0.52f) highBurdenRoutes++;
                if (route.PrimaryConstraintKind != RegionalRouteConstraintKind.None && route.PrimaryConstraintKind != RegionalRouteConstraintKind.StableDryTrack) constrainedRoutes++;
                waterCrossings += Mathf.Max(0, route.WaterCrossingCount);
                if (route.MudSeasonRisk01 >= 0.55f) highMudRiskRoutes++;
            }

            RegionalSettlementRecord leadSettlement = null;
            for (int i = 0; i < Settlements.Count; i++)
            {
                RegionalSettlementRecord settlement = Settlements[i];
                if (settlement == null)
                {
                    continue;
                }

                if (leadSettlement == null)
                {
                    leadSettlement = settlement;
                }

                if (settlement.SuccessionRole is RegionalSettlementSuccessionRole.PeerChallenger or RegionalSettlementSuccessionRole.LikelyRegionalSuccessor)
                {
                    successionChallengers++;
                    strongestFootholdChallenge = Mathf.Max(strongestFootholdChallenge, settlement.FootholdChallenge01);
                }

                if (settlement.SuccessionRole == RegionalSettlementSuccessionRole.StalledOrDecliningNode)
                {
                    stalledSettlementNodes++;
                }
            }

            float parcelCount = Mathf.Max(1, SurveyParcels.Count);
            float routeCount = Mathf.Max(1, RouteCorridors.Count);
            string leadSettlementRead = "none";
            if (leadSettlement != null)
            {
                leadSettlementRead = $"{leadSettlement.SettlementId} ({leadSettlement.SuccessionRole}, pop {leadSettlement.PopulationBandMin:0}-{leadSettlement.PopulationBandMax:0}, {leadSettlement.PopulationBand})";
            }

            RegionalFoundationInspectionReport inspectionReport = BuildInspectionReport();
            string issue = inspectionReport.BuildFirstIssueReadout();
            string issueSuffix = string.IsNullOrWhiteSpace(issue) ? string.Empty : $" First issue: {issue}";
            string opportunityDigest = inspectionReport.BuildOpportunityDigest(Mathf.Max(0, maxOpportunityItems));
            string opportunitySuffix = string.IsNullOrWhiteSpace(opportunityDigest) ? string.Empty : $" {opportunityDigest}";
            string authoringDebugSuffix = string.IsNullOrWhiteSpace(authoringDebugSummary) ? string.Empty : $" {authoringDebugSummary}";

            return $"Regional foundation: {RecipeFamily}, anchor {AnchorTown.CenterMeters.x:0}m/{AnchorTown.CenterMeters.y:0}m, tiles {TerrainTiles.Count}, route-ready tiles {routeReadyTiles}, corridor-influenced tiles {corridorInfluencedTiles}, parcels {SurveyParcels.Count}, remote-suitable {remoteSuitableParcels}, corridor-linked {corridorInfluencedParcels}, high-freight-access {highFreightAccessParcels}, avg readiness {readinessSum / parcelCount:P0}, avg burden {burdenSum / parcelCount:P0}, routes {RouteCorridors.Count}, avg route practicality {routePracticalitySum / routeCount:P0}, avg route burden {routeBurdenSum / routeCount:P0}, high-burden routes {highBurdenRoutes}, constrained routes {constrainedRoutes}, water crossings {waterCrossings}, mud-risk routes {highMudRiskRoutes}, succession challengers {successionChallengers}, strongest foothold challenge {strongestFootholdChallenge:P0}, stalled nodes {stalledSettlementNodes}, lead node {leadSettlementRead}. {inspectionReport.BuildCompactReadout()}{issueSuffix}{opportunitySuffix}{authoringDebugSuffix}";
        }


        public string BuildRegionalFoundationDeveloperReport(int maxOpportunityItems = 5)
        {
            StringBuilder builder = new();
            builder.AppendLine("Regional Foundation Report");
            builder.AppendLine($"Seed: {Seed} | Recipe: {RecipeFamily} | Size: {RegionSizeMeters.x:0}m x {RegionSizeMeters.y:0}m | Tiles: {TerrainTiles.Count} | Parcels: {SurveyParcels.Count} | Routes: {RouteCorridors.Count} | Settlements: {Settlements.Count}");
            builder.AppendLine($"Anchor: {AnchorTown.AnchorId} at {AnchorTown.CenterMeters.x:0}m/{AnchorTown.CenterMeters.y:0}m | suitability {AnchorTown.SuitabilityScore01:P0} | water {AnchorTown.NearestWaterDistanceMeters:0}m | buildable {AnchorTown.BuildableLand01:P0} | gateway {AnchorTown.GatewayScore01:P0} | expansion {AnchorTown.ExpansionRoom01:P0}");
            builder.AppendLine(BuildRegionalFoundationInspectionSummary(maxOpportunityItems));
            builder.AppendLine(BuildRegionalGameplayReadinessSummary(maxOpportunityItems));

            if (TryFindBestNearTermParcel(out RegionalSurveyParcelRecord nearTerm))
            {
                builder.AppendLine("Best near-term parcel:");
                builder.AppendLine(RegionalParcelAuthority.BuildInspectionReadout(this, nearTerm));
            }

            if (TryFindBestRemoteIndustryParcel(out RegionalSurveyParcelRecord remote))
            {
                builder.AppendLine("Best remote/resource parcel:");
                builder.AppendLine(RegionalParcelAuthority.BuildInspectionReadout(this, remote));
            }

            if (TryFindWeakestRoute(out RegionalRouteCorridorRecord route))
            {
                builder.AppendLine("Weakest/most important route constraint:");
                builder.AppendLine(route.BuildDetailedRouteInspectionReadout());
            }

            if (Settlements.Count > 0 && Settlements[0] != null)
            {
                builder.AppendLine("Lead settlement:");
                builder.AppendLine(Settlements[0].BuildDetailedSettlementInspectionReadout());
            }

            RegionalFoundationInspectionReport report = BuildInspectionReport();
            builder.AppendLine(report.BuildCompactReadout());
            string issue = report.BuildFirstIssueReadout();
            if (!string.IsNullOrWhiteSpace(issue))
            {
                builder.AppendLine("First issue: " + issue);
            }

            string opportunities = report.BuildOpportunityDigest(maxOpportunityItems);
            if (!string.IsNullOrWhiteSpace(opportunities))
            {
                builder.AppendLine(opportunities);
            }

            return builder.ToString().Trim();
        }

        public bool TryFindParcelById(string parcelId, out RegionalSurveyParcelRecord parcel)
        {
            parcel = null;
            if (string.IsNullOrWhiteSpace(parcelId))
            {
                return false;
            }

            for (int i = 0; i < SurveyParcels.Count; i++)
            {
                RegionalSurveyParcelRecord current = SurveyParcels[i];
                if (current != null && string.Equals(current.ParcelId, parcelId, StringComparison.OrdinalIgnoreCase))
                {
                    parcel = current;
                    return true;
                }
            }

            return false;
        }

        public bool TryFindNearestParcel(Vector2 regionalPointMeters, float maxDistanceMeters, out RegionalSurveyParcelRecord parcel, out float distanceMeters)
        {
            parcel = null;
            distanceMeters = float.MaxValue;
            float maxDistance = Mathf.Max(0f, maxDistanceMeters);
            for (int i = 0; i < SurveyParcels.Count; i++)
            {
                RegionalSurveyParcelRecord current = SurveyParcels[i];
                if (current == null)
                {
                    continue;
                }

                // Parcel inspection uses legal bounds so large rural/remote parcels select correctly when the inspected point is inside the parcel.
                float distance = DistanceToRect(regionalPointMeters, current.BoundsMeters);
                if (distance <= maxDistance && distance < distanceMeters)
                {
                    distanceMeters = distance;
                    parcel = current;
                }
            }

            return parcel != null;
        }

        public string BuildNearestParcelInspectionReadout(Vector2 regionalPointMeters, float maxDistanceMeters = 640f)
        {
            if (!TryFindNearestParcel(regionalPointMeters, maxDistanceMeters, out RegionalSurveyParcelRecord parcel, out float distanceMeters))
            {
                return $"Regional parcel inspection: no parcel found within {Mathf.Max(0f, maxDistanceMeters):0}m of {regionalPointMeters.x:0}m/{regionalPointMeters.y:0}m.";
            }

            return RegionalParcelAuthority.BuildInspectionReadout(this, parcel)
                + Environment.NewLine
                + $"Selection distance: {distanceMeters:0}m from {regionalPointMeters.x:0}m/{regionalPointMeters.y:0}m.";
        }


        public bool TryFindRouteById(string corridorId, out RegionalRouteCorridorRecord route)
        {
            route = null;
            if (string.IsNullOrWhiteSpace(corridorId))
            {
                return false;
            }

            for (int i = 0; i < RouteCorridors.Count; i++)
            {
                RegionalRouteCorridorRecord current = RouteCorridors[i];
                if (current != null && string.Equals(current.CorridorId, corridorId, StringComparison.OrdinalIgnoreCase))
                {
                    route = current;
                    return true;
                }
            }

            return false;
        }

        public bool TryFindSettlementById(string settlementId, out RegionalSettlementRecord settlement)
        {
            settlement = null;
            if (string.IsNullOrWhiteSpace(settlementId))
            {
                return false;
            }

            for (int i = 0; i < Settlements.Count; i++)
            {
                RegionalSettlementRecord current = Settlements[i];
                if (current != null && string.Equals(current.SettlementId, settlementId, StringComparison.OrdinalIgnoreCase))
                {
                    settlement = current;
                    return true;
                }
            }

            return false;
        }

        public bool TryFindBestNearTermParcel(out RegionalSurveyParcelRecord parcel)
        {
            return TryFindBestParcel(p => p.DevelopmentReadiness01 * 0.44f + p.BuildSuitability01 * 0.26f + p.FarmSuitability01 * 0.12f + p.FreightAccess01 * 0.10f + (1f - p.SupportBurden01) * 0.08f, out parcel);
        }

        public bool TryFindBestRemoteIndustryParcel(out RegionalSurveyParcelRecord parcel)
        {
            return TryFindBestParcel(p => p.CalculateRemoteIndustrySuitability01(), out parcel);
        }

        public bool TryFindWeakestRoute(out RegionalRouteCorridorRecord route)
        {
            route = null;
            float bestScore = float.MinValue;
            for (int i = 0; i < RouteCorridors.Count; i++)
            {
                RegionalRouteCorridorRecord current = RouteCorridors[i];
                if (current == null)
                {
                    continue;
                }

                float score = current.FreightBurden01 * 0.26f + (1f - current.Practicality01) * 0.22f + (1f - current.SeasonalReliability01) * 0.18f + current.BridgeOrFordNeed01 * 0.14f + current.MudSeasonRisk01 * 0.12f + current.GradeBurden01 * 0.08f;
                if (score > bestScore)
                {
                    bestScore = score;
                    route = current;
                }
            }

            return route != null;
        }

        private bool TryFindBestParcel(Func<RegionalSurveyParcelRecord, float> scoreFunc, out RegionalSurveyParcelRecord parcel)
        {
            parcel = null;
            if (scoreFunc == null)
            {
                return false;
            }

            float bestScore = float.MinValue;
            for (int i = 0; i < SurveyParcels.Count; i++)
            {
                RegionalSurveyParcelRecord current = SurveyParcels[i];
                if (current == null)
                {
                    continue;
                }

                float score = scoreFunc(current);
                if (score > bestScore)
                {
                    bestScore = score;
                    parcel = current;
                }
            }

            return parcel != null;
        }

        public RegionalWorldSaveDto CaptureSaveDto()
        {
            RegionalWorldSaveDto dto = new()
            {
                seed = Seed,
                recipeFamily = RecipeFamily,
                regionWidthMeters = RegionSizeMeters.x,
                regionDepthMeters = RegionSizeMeters.y,
                surveyCellSizeMeters = SurveyCellSizeMeters,
                terrainTileSizeMeters = TerrainTileSizeMeters,
                anchorTown = new RegionalAnchorTownSaveDto
                {
                    anchorId = AnchorTown.AnchorId,
                    centerMeters = ToSaveDto(AnchorTown.CenterMeters),
                    suitabilityScore01 = AnchorTown.SuitabilityScore01,
                    nearestWaterDistanceMeters = AnchorTown.NearestWaterDistanceMeters,
                    buildableLand01 = AnchorTown.BuildableLand01,
                    gatewayScore01 = AnchorTown.GatewayScore01,
                    expansionRoom01 = AnchorTown.ExpansionRoom01,
                    debugReason = AnchorTown.DebugReason
                }
            };

            for (int i = 0; i < TerrainTiles.Count; i++)
            {
                RegionalTerrainTileRecord tile = TerrainTiles[i];
                dto.terrainTiles.Add(new RegionalTerrainTileSaveDto
                {
                    tileId = tile.TileId,
                    tileX = tile.TileX,
                    tileZ = tile.TileZ,
                    boundsMeters = ToSaveDto(tile.BoundsMeters),
                    meanElevationMeters = tile.MeanElevationMeters,
                    ruggedness01 = tile.Ruggedness01,
                    wetness01 = tile.Wetness01,
                    activationReadiness01 = tile.ActivationReadiness01,
                    settlementInfluence01 = tile.SettlementInfluence01,
                    dominantBiomeKind = tile.DominantBiomeKind,
                    routeInfluence01 = tile.RouteInfluence01,
                    routeActivationReadiness01 = tile.RouteActivationReadiness01,
                    freightBurdenInfluence01 = tile.FreightBurdenInfluence01,
                    nearestRouteCorridorId = tile.NearestRouteCorridorId,
                    nearestRouteCorridorKind = tile.NearestRouteCorridorKind,
                    active = tile.Active,
                    debugSummary = tile.DebugSummary
                });
            }

            for (int i = 0; i < Watercourses.Count; i++)
            {
                RegionalWatercourseRecord water = Watercourses[i];
                RegionalWatercourseSaveDto waterDto = new()
                {
                    watercourseId = water.WatercourseId,
                    kind = water.Kind,
                    influenceWidthMeters = water.InfluenceWidthMeters,
                    floodplainWidthMeters = water.FloodplainWidthMeters,
                    flowStrength01 = water.FlowStrength01
                };
                for (int pointIndex = 0; pointIndex < water.Points.Count; pointIndex++)
                {
                    waterDto.points.Add(ToSaveDto(water.Points[pointIndex]));
                }

                dto.watercourses.Add(waterDto);
            }

            for (int i = 0; i < RouteCorridors.Count; i++)
            {
                RegionalRouteCorridorRecord route = RouteCorridors[i];
                if (route == null)
                {
                    continue;
                }

                RegionalRouteCorridorSaveDto routeDto = new()
                {
                    corridorId = route.CorridorId,
                    kind = route.Kind,
                    sourceSettlementId = route.SourceSettlementId,
                    destinationSettlementId = route.DestinationSettlementId,
                    sourceParcelId = route.SourceParcelId,
                    destinationParcelId = route.DestinationParcelId,
                    lengthMeters = route.LengthMeters,
                    practicality01 = route.Practicality01,
                    seasonalReliability01 = route.SeasonalReliability01,
                    freightBurden01 = route.FreightBurden01,
                    primaryConstraintKind = route.PrimaryConstraintKind,
                    waterCrossingCount = route.WaterCrossingCount,
                    wetGroundCrossingCount = route.WetGroundCrossingCount,
                    bridgeOrFordNeed01 = route.BridgeOrFordNeed01,
                    mudSeasonRisk01 = route.MudSeasonRisk01,
                    gradeBurden01 = route.GradeBurden01,
                    constraintSummary = route.ConstraintSummary,
                    debugSummary = route.DebugSummary
                };
                for (int pointIndex = 0; pointIndex < route.Points.Count; pointIndex++)
                {
                    routeDto.points.Add(ToSaveDto(route.Points[pointIndex]));
                }

                dto.routeCorridors.Add(routeDto);
            }

            for (int i = 0; i < SurveyParcels.Count; i++)
            {
                RegionalSurveyParcelRecord parcel = SurveyParcels[i];
                dto.surveyParcels.Add(new RegionalSurveyParcelSaveDto
                {
                    parcelId = parcel.ParcelId,
                    sectionId = parcel.SectionId,
                    tractId = parcel.TractId,
                    parcelKind = parcel.ParcelKind,
                    boundsMeters = ToSaveDto(parcel.BoundsMeters),
                    acreage = parcel.Acreage,
                    meanSlope01 = parcel.MeanSlope01,
                    wetness01 = parcel.Wetness01,
                    waterAccess01 = parcel.WaterAccess01,
                    farmSuitability01 = parcel.FarmSuitability01,
                    buildSuitability01 = parcel.BuildSuitability01,
                    resourceSuitability01 = parcel.ResourceSuitability01,
                    supportBurden01 = parcel.SupportBurden01,
                    frontageClass = parcel.FrontageClass,
                    accessQuality = parcel.AccessQuality,
                    provenance = parcel.Provenance,
                    remoteSuitability = parcel.RemoteSuitability,
                    terrainBurden01 = parcel.TerrainBurden01,
                    developmentReadiness01 = parcel.DevelopmentReadiness01,
                    districtContext = parcel.DistrictContext,
                    debugReason = parcel.DebugReason,
                    corridorRelation = parcel.CorridorRelation,
                    nearestCorridorId = parcel.NearestCorridorId,
                    nearestCorridorKind = parcel.NearestCorridorKind,
                    distanceToCorridorMeters = parcel.DistanceToCorridorMeters,
                    corridorInfluence01 = parcel.CorridorInfluence01,
                    freightAccess01 = parcel.FreightAccess01,
                    corridorBurden01 = parcel.CorridorBurden01,
                    corridorContext = parcel.CorridorContext
                });
            }

            for (int i = 0; i < VegetationZones.Count; i++)
            {
                RegionalVegetationZoneRecord zone = VegetationZones[i];
                dto.vegetationZones.Add(new RegionalVegetationZoneSaveDto
                {
                    zoneId = zone.ZoneId,
                    biomeKind = zone.BiomeKind,
                    vegetationKind = zone.VegetationKind,
                    boundsMeters = ToSaveDto(zone.BoundsMeters),
                    density01 = zone.Density01,
                    wetness01 = zone.Wetness01,
                    slope01 = zone.Slope01,
                    notes = zone.Notes
                });
            }

            for (int i = 0; i < SettlementClusters.Count; i++)
            {
                RegionalSettlementClusterRecord cluster = SettlementClusters[i];
                dto.settlementClusters.Add(new RegionalSettlementClusterSaveDto
                {
                    clusterId = cluster.ClusterId,
                    centerMeters = ToSaveDto(cluster.CenterMeters),
                    householdCount = cluster.HouseholdCount,
                    businessCount = cluster.BusinessCount,
                    population = cluster.Population,
                    serviceGravity01 = cluster.ServiceGravity01,
                    freightSupport01 = cluster.FreightSupport01,
                    supportBurden01 = cluster.SupportBurden01,
                    character = cluster.Character,
                    permanence01 = cluster.Permanence01,
                    localConfidence01 = cluster.LocalConfidence01,
                    serviceDeficit01 = cluster.ServiceDeficit01,
                    declineRisk01 = cluster.DeclineRisk01,
                    debugReason = cluster.DebugReason
                });
            }

            for (int i = 0; i < Settlements.Count; i++)
            {
                RegionalSettlementRecord settlement = Settlements[i];
                dto.settlements.Add(new RegionalSettlementSaveDto
                {
                    settlementId = settlement.SettlementId,
                    sourceClusterId = settlement.SourceClusterId,
                    centerMeters = ToSaveDto(settlement.CenterMeters),
                    householdCount = settlement.HouseholdCount,
                    businessCount = settlement.BusinessCount,
                    population = settlement.Population,
                    populationBand = settlement.PopulationBand,
                    populationBandMin = settlement.PopulationBandMin,
                    populationBandMax = settlement.PopulationBandMax,
                    nextTriggerWindowMinPopulation = settlement.NextTriggerWindowMinPopulation,
                    nextTriggerWindowMaxPopulation = settlement.NextTriggerWindowMaxPopulation,
                    label = settlement.Label,
                    serviceGravity01 = settlement.ServiceGravity01,
                    freightSupport01 = settlement.FreightSupport01,
                    supportBurden01 = settlement.SupportBurden01,
                    character = settlement.Character,
                    permanence = settlement.Permanence,
                    successionRole = settlement.SuccessionRole,
                    regionalHierarchyRank = settlement.RegionalHierarchyRank,
                    regionalGravity01 = settlement.RegionalGravity01,
                    footholdChallenge01 = settlement.FootholdChallenge01,
                    successionReadiness01 = settlement.SuccessionReadiness01,
                    successionContext = settlement.SuccessionContext,
                    permanence01 = settlement.Permanence01,
                    localConfidence01 = settlement.LocalConfidence01,
                    serviceDeficit01 = settlement.ServiceDeficit01,
                    declineRisk01 = settlement.DeclineRisk01,
                    debugSummary = settlement.DebugSummary
                });
            }

            return dto;
        }

        public static RegionalWorldState FromSaveDto(RegionalWorldSaveDto dto)
        {
            if (dto == null)
            {
                return Create(0, RegionalTerrainRecipeFamily.CreekCorridorSettlementLand, Vector2.one, 2f, 512f, null, null, null, null, null, null, null, null);
            }

            List<RegionalTerrainTileRecord> tiles = new();
            if (dto.terrainTiles != null)
            {
                for (int i = 0; i < dto.terrainTiles.Count; i++)
                {
                    RegionalTerrainTileSaveDto tile = dto.terrainTiles[i];
                    if (tile == null)
                    {
                        continue;
                    }

                    tiles.Add(new RegionalTerrainTileRecord(
                        tile.tileId,
                        tile.tileX,
                        tile.tileZ,
                        FromSaveDto(tile.boundsMeters),
                        tile.meanElevationMeters,
                        tile.ruggedness01,
                        tile.wetness01,
                        tile.active,
                        tile.activationReadiness01,
                        tile.settlementInfluence01,
                        tile.dominantBiomeKind,
                        tile.debugSummary,
                        tile.routeInfluence01,
                        tile.routeActivationReadiness01,
                        tile.freightBurdenInfluence01,
                        tile.nearestRouteCorridorId,
                        tile.nearestRouteCorridorKind));
                }
            }

            List<RegionalWatercourseRecord> waters = new();
            if (dto.watercourses != null)
            {
                for (int i = 0; i < dto.watercourses.Count; i++)
                {
                    RegionalWatercourseSaveDto water = dto.watercourses[i];
                    if (water == null)
                    {
                        continue;
                    }

                    List<Vector2> points = new();
                    if (water.points != null)
                    {
                        for (int pointIndex = 0; pointIndex < water.points.Count; pointIndex++)
                        {
                            points.Add(FromSaveDto(water.points[pointIndex]));
                        }
                    }

                    waters.Add(new RegionalWatercourseRecord(
                        water.watercourseId,
                        water.kind,
                        water.influenceWidthMeters,
                        water.floodplainWidthMeters,
                        water.flowStrength01,
                        points));
                }
            }

            List<RegionalRouteCorridorRecord> routes = new();
            if (dto.routeCorridors != null)
            {
                for (int i = 0; i < dto.routeCorridors.Count; i++)
                {
                    RegionalRouteCorridorSaveDto route = dto.routeCorridors[i];
                    if (route == null)
                    {
                        continue;
                    }

                    List<Vector2> points = new();
                    if (route.points != null)
                    {
                        for (int pointIndex = 0; pointIndex < route.points.Count; pointIndex++)
                        {
                            points.Add(FromSaveDto(route.points[pointIndex]));
                        }
                    }

                    routes.Add(new RegionalRouteCorridorRecord(
                        route.corridorId,
                        route.kind,
                        route.sourceSettlementId,
                        route.destinationSettlementId,
                        route.sourceParcelId,
                        route.destinationParcelId,
                        route.lengthMeters,
                        route.practicality01,
                        route.seasonalReliability01,
                        route.freightBurden01,
                        route.debugSummary,
                        points,
                        route.primaryConstraintKind,
                        route.waterCrossingCount,
                        route.wetGroundCrossingCount,
                        route.bridgeOrFordNeed01,
                        route.mudSeasonRisk01,
                        route.gradeBurden01,
                        route.constraintSummary));
                }
            }

            List<RegionalSurveyParcelRecord> parcels = new();
            if (dto.surveyParcels != null)
            {
                for (int i = 0; i < dto.surveyParcels.Count; i++)
                {
                    RegionalSurveyParcelSaveDto parcel = dto.surveyParcels[i];
                    if (parcel == null)
                    {
                        continue;
                    }

                    parcels.Add(new RegionalSurveyParcelRecord(
                        parcel.parcelId,
                        parcel.sectionId,
                        parcel.tractId,
                        parcel.parcelKind,
                        FromSaveDto(parcel.boundsMeters),
                        parcel.acreage,
                        parcel.meanSlope01,
                        parcel.wetness01,
                        parcel.waterAccess01,
                        parcel.farmSuitability01,
                        parcel.buildSuitability01,
                        parcel.resourceSuitability01,
                        parcel.supportBurden01,
                        parcel.frontageClass,
                        parcel.accessQuality,
                        parcel.provenance,
                        parcel.remoteSuitability,
                        parcel.terrainBurden01,
                        parcel.developmentReadiness01,
                        parcel.districtContext,
                        parcel.debugReason,
                        parcel.corridorRelation,
                        parcel.nearestCorridorId,
                        parcel.nearestCorridorKind,
                        parcel.distanceToCorridorMeters,
                        parcel.corridorInfluence01,
                        parcel.freightAccess01,
                        parcel.corridorBurden01,
                        parcel.corridorContext));
                }
            }

            List<RegionalVegetationZoneRecord> vegetation = new();
            if (dto.vegetationZones != null)
            {
                for (int i = 0; i < dto.vegetationZones.Count; i++)
                {
                    RegionalVegetationZoneSaveDto zone = dto.vegetationZones[i];
                    if (zone == null)
                    {
                        continue;
                    }

                    vegetation.Add(new RegionalVegetationZoneRecord(
                        zone.zoneId,
                        zone.biomeKind,
                        zone.vegetationKind,
                        FromSaveDto(zone.boundsMeters),
                        zone.density01,
                        zone.wetness01,
                        zone.slope01,
                        zone.notes));
                }
            }

            List<RegionalSettlementClusterRecord> clusters = new();
            if (dto.settlementClusters != null)
            {
                for (int i = 0; i < dto.settlementClusters.Count; i++)
                {
                    RegionalSettlementClusterSaveDto cluster = dto.settlementClusters[i];
                    if (cluster == null)
                    {
                        continue;
                    }

                    clusters.Add(new RegionalSettlementClusterRecord(
                        cluster.clusterId,
                        FromSaveDto(cluster.centerMeters),
                        cluster.householdCount,
                        cluster.businessCount,
                        cluster.population,
                        cluster.serviceGravity01,
                        cluster.freightSupport01,
                        cluster.supportBurden01,
                        cluster.character,
                        cluster.permanence01,
                        cluster.localConfidence01,
                        cluster.serviceDeficit01,
                        cluster.declineRisk01,
                        cluster.debugReason));
                }
            }

            List<RegionalSettlementRecord> settlements = new();
            if (dto.settlements != null)
            {
                for (int i = 0; i < dto.settlements.Count; i++)
                {
                    RegionalSettlementSaveDto settlement = dto.settlements[i];
                    if (settlement == null)
                    {
                        continue;
                    }

                    settlements.Add(new RegionalSettlementRecord(
                        settlement.settlementId,
                        settlement.sourceClusterId,
                        FromSaveDto(settlement.centerMeters),
                        settlement.householdCount,
                        settlement.businessCount,
                        settlement.population,
                        settlement.populationBand,
                        settlement.populationBandMin,
                        settlement.populationBandMax,
                        settlement.nextTriggerWindowMinPopulation,
                        settlement.nextTriggerWindowMaxPopulation,
                        settlement.label,
                        settlement.serviceGravity01,
                        settlement.freightSupport01,
                        settlement.supportBurden01,
                        settlement.character,
                        settlement.permanence,
                        settlement.permanence01,
                        settlement.localConfidence01,
                        settlement.serviceDeficit01,
                        settlement.declineRisk01,
                        settlement.debugSummary,
                        settlement.successionRole,
                        settlement.regionalHierarchyRank,
                        settlement.regionalGravity01,
                        settlement.footholdChallenge01,
                        settlement.successionReadiness01,
                        settlement.successionContext));
                }
            }

            if (RegionalWorldGenerator.NeedsSettlementHierarchyRebuild(settlements))
            {
                settlements = RegionalWorldGenerator.ApplySettlementHierarchyMetadata(settlements);
            }

            RegionalAnchorTownSaveDto anchorDto = dto.anchorTown ?? new RegionalAnchorTownSaveDto();
            RegionalAnchorTownRecord anchor = RegionalAnchorTownRecord.Create(
                anchorDto.anchorId,
                FromSaveDto(anchorDto.centerMeters),
                anchorDto.suitabilityScore01,
                anchorDto.nearestWaterDistanceMeters,
                anchorDto.buildableLand01,
                anchorDto.gatewayScore01,
                anchorDto.expansionRoom01,
                anchorDto.debugReason);

            if (routes.Count == 0 && settlements.Count > 0)
            {
                routes = RegionalWorldGenerator.GenerateRouteCorridors(dto.seed, anchor, parcels, settlements, waters);
            }

            if (RegionalWorldGenerator.NeedsRouteConstraintRebuild(routes))
            {
                routes = RegionalWorldGenerator.ApplyRouteConstraintMetadata(routes, waters, parcels);
            }

            if (RegionalWorldGenerator.NeedsParcelCorridorInfluenceRebuild(parcels, routes))
            {
                parcels = RegionalWorldGenerator.ApplyRouteCorridorInfluence(parcels, routes);
            }

            if (RegionalWorldGenerator.NeedsTileRouteReadinessRebuild(tiles, routes))
            {
                tiles = RegionalWorldGenerator.ApplyRouteCorridorTileReadiness(tiles, routes, settlements);
            }

            return Create(
                dto.seed,
                dto.recipeFamily,
                new Vector2(dto.regionWidthMeters, dto.regionDepthMeters),
                dto.surveyCellSizeMeters,
                dto.terrainTileSizeMeters,
                anchor,
                tiles,
                waters,
                routes,
                parcels,
                vegetation,
                clusters,
                settlements);
        }

        public RegionalFoundationInspectionReport BuildInspectionReport()
        {
            List<RegionalFoundationIssueRecord> issues = new();
            HashSet<string> parcelIds = new();
            HashSet<string> settlementIds = new();
            HashSet<string> routeIds = new();

            if (RegionSizeMeters.x < 2048f || RegionSizeMeters.y < 2048f)
            {
                AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.General, "region_size", $"Region is small for long-term regional play ({RegionSizeMeters.x:0}m x {RegionSizeMeters.y:0}m).", "Keep the regional foundation large enough for later settlement, freight, and remote industry spacing.");
            }

            if (AnchorTown == null || string.IsNullOrWhiteSpace(AnchorTown.AnchorId))
            {
                AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Error, RegionalFoundationIssueCategory.AnchorTown, "anchor", "No opening foothold anchor is present.", "Regenerate or rebuild the regional foundation before local town handoff.");
            }
            else
            {
                if (AnchorTown.SuitabilityScore01 < 0.45f)
                {
                    AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.AnchorTown, AnchorTown.AnchorId, $"Anchor suitability is low ({AnchorTown.SuitabilityScore01:P0}).", "Review seed or recipe weighting; the starting foothold should feel useful, not arbitrary.", true, AnchorTown.CenterMeters);
                }

                if (AnchorTown.BuildableLand01 < 0.42f)
                {
                    AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.AnchorTown, AnchorTown.AnchorId, $"Anchor buildable shelf is weak ({AnchorTown.BuildableLand01:P0}).", "Preserve relief, but keep enough practical grading room for the first town.", true, AnchorTown.CenterMeters);
                }

                if (AnchorTown.NearestWaterDistanceMeters < 70f)
                {
                    AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.AnchorTown, AnchorTown.AnchorId, $"Anchor is very close to water ({AnchorTown.NearestWaterDistanceMeters:0}m).", "Check floodplain pressure before treating the site as safe prime frontage.", true, AnchorTown.CenterMeters);
                }
                else if (AnchorTown.NearestWaterDistanceMeters > 1100f)
                {
                    AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.AnchorTown, AnchorTown.AnchorId, $"Anchor is far from reliable water ({AnchorTown.NearestWaterDistanceMeters:0}m).", "Gateway settlements need water relation, hauling explanation, or a strong alternate reason to exist.", true, AnchorTown.CenterMeters);
                }
            }

            InspectTerrainTiles(issues);
            InspectWatercourses(issues);
            InspectSurveyParcels(issues, parcelIds);
            InspectSettlements(issues, settlementIds);
            InspectRouteCorridors(issues, parcelIds, settlementIds, routeIds);

            int remoteSuitable = 0;
            int corridorInfluencedParcels = 0;
            int highFreightAccessParcels = 0;
            int highBurdenRoutes = 0;
            int constrainedRoutes = 0;
            int routeWaterCrossings = 0;
            int highMudRiskRoutes = 0;
            int endpointRiskRoutes = 0;
            int routeReadyTiles = 0;
            int corridorInfluencedTiles = 0;
            int successionChallengers = 0;
            int stalledSettlementNodes = 0;
            float strongestNonFootholdChallenge = 0f;
            for (int i = 0; i < Settlements.Count; i++)
            {
                RegionalSettlementRecord settlement = Settlements[i];
                if (settlement == null)
                {
                    continue;
                }

                if (settlement.SuccessionRole is RegionalSettlementSuccessionRole.PeerChallenger or RegionalSettlementSuccessionRole.LikelyRegionalSuccessor)
                {
                    successionChallengers++;
                    strongestNonFootholdChallenge = Mathf.Max(strongestNonFootholdChallenge, settlement.FootholdChallenge01);
                }

                if (settlement.SuccessionRole == RegionalSettlementSuccessionRole.StalledOrDecliningNode)
                {
                    stalledSettlementNodes++;
                }
            }

            for (int i = 0; i < TerrainTiles.Count; i++)
            {
                RegionalTerrainTileRecord tile = TerrainTiles[i];
                if (tile == null)
                {
                    continue;
                }

                if (tile.RouteActivationReadiness01 >= 0.42f)
                {
                    routeReadyTiles++;
                }

                if (tile.RouteInfluence01 > 0.05f)
                {
                    corridorInfluencedTiles++;
                }
            }

            for (int i = 0; i < SurveyParcels.Count; i++)
            {
                RegionalSurveyParcelRecord parcel = SurveyParcels[i];
                if (parcel == null)
                {
                    continue;
                }

                if (parcel.RemoteSuitability != RegionalRemoteSuitability.None)
                {
                    remoteSuitable++;
                }

                if (parcel.CorridorRelation != RegionalParcelCorridorRelation.None && parcel.CorridorRelation != RegionalParcelCorridorRelation.OffRoute)
                {
                    corridorInfluencedParcels++;
                }

                if (parcel.FreightAccess01 >= 0.58f)
                {
                    highFreightAccessParcels++;
                }
            }

            for (int i = 0; i < RouteCorridors.Count; i++)
            {
                RegionalRouteCorridorRecord route = RouteCorridors[i];
                if (route != null && route.FreightBurden01 >= 0.62f)
                {
                    highBurdenRoutes++;
                }

                if (route != null && route.PrimaryConstraintKind != RegionalRouteConstraintKind.None && route.PrimaryConstraintKind != RegionalRouteConstraintKind.StableDryTrack)
                {
                    constrainedRoutes++;
                }

                if (route != null)
                {
                    routeWaterCrossings += Mathf.Max(0, route.WaterCrossingCount);
                    if (route.MudSeasonRisk01 >= 0.55f)
                    {
                        highMudRiskRoutes++;
                    }

                    if (RouteHasEndpointGeometryRisk(route))
                    {
                        endpointRiskRoutes++;
                    }
                }
            }

            List<RegionalFoundationOpportunityRecord> opportunityReadouts = BuildOpportunityReadouts();
            string summary = $"tiles {TerrainTiles.Count}, route-ready tiles {routeReadyTiles}, corridor-influenced tiles {corridorInfluencedTiles}, watercourses {Watercourses.Count}, parcels {SurveyParcels.Count}, remote-suitable parcels {remoteSuitable}, corridor-linked parcels {corridorInfluencedParcels}, high-freight-access parcels {highFreightAccessParcels}, settlements {Settlements.Count}, succession challengers {successionChallengers}, stalled nodes {stalledSettlementNodes}, strongest foothold challenge {strongestNonFootholdChallenge:P0}, routes {RouteCorridors.Count}, high-burden routes {highBurdenRoutes}, constrained routes {constrainedRoutes}, water crossings {routeWaterCrossings}, mud-risk routes {highMudRiskRoutes}, endpoint-risk routes {endpointRiskRoutes}, opportunity readouts {opportunityReadouts.Count}.";
            return new RegionalFoundationInspectionReport(Seed, RecipeFamily, summary, issues, opportunityReadouts);
        }

        private void InspectTerrainTiles(List<RegionalFoundationIssueRecord> issues)
        {
            if (TerrainTiles.Count == 0)
            {
                AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Error, RegionalFoundationIssueCategory.TerrainTiles, "terrain_tiles", "No terrain tile records exist.", "The visual terrain layer needs tiles backed by authoritative regional data.");
                return;
            }

            HashSet<string> ids = new();
            int activeCount = 0;
            int routeInfluencedTiles = 0;
            int routeReadyTiles = 0;
            int highBurdenTileCount = 0;
            float readinessSum = 0f;
            for (int i = 0; i < TerrainTiles.Count; i++)
            {
                RegionalTerrainTileRecord tile = TerrainTiles[i];
                if (tile == null)
                {
                    AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.TerrainTiles, $"tile_{i}", "Null terrain tile entry found.", "Remove or rebuild the broken tile record.");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(tile.TileId) && !ids.Add(tile.TileId))
                {
                    AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.TerrainTiles, tile.TileId, "Duplicate terrain tile ID.", "Tile IDs should remain stable for activation and save/debug inspection.", true, tile.BoundsMeters.center);
                }

                if (tile.Active)
                {
                    activeCount++;
                }

                readinessSum += Mathf.Max(tile.ActivationReadiness01, tile.RouteActivationReadiness01);
                if (tile.RouteInfluence01 > 0.05f)
                {
                    routeInfluencedTiles++;
                }

                if (tile.RouteActivationReadiness01 >= 0.42f)
                {
                    routeReadyTiles++;
                }

                if (tile.FreightBurdenInfluence01 >= 0.52f)
                {
                    highBurdenTileCount++;
                }

                if (tile.BoundsMeters.width <= 0f || tile.BoundsMeters.height <= 0f)
                {
                    AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Error, RegionalFoundationIssueCategory.TerrainTiles, tile.TileId, "Terrain tile bounds are invalid.", "Regenerate terrain tile layout before rendering.", true, tile.BoundsMeters.center);
                }
            }

            if (activeCount == 0)
            {
                AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Error, RegionalFoundationIssueCategory.TerrainTiles, "terrain_tiles", "No active terrain tiles are available.", "Keep at least the opening region active so the player has world evidence.");
            }

            float averageReadiness = readinessSum / Mathf.Max(1, TerrainTiles.Count);
            if (averageReadiness < 0.22f)
            {
                AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.TerrainTiles, "terrain_readiness", $"Average terrain activation readiness is low ({averageReadiness:P0}).", "Check anchor influence, hydrology, route corridors, or tile readiness weighting.");
            }

            if (RouteCorridors.Count > 0 && routeInfluencedTiles == 0)
            {
                AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.TerrainTiles, "tile_route_readiness", "Route corridors exist but no terrain tiles carry route influence.", "Rebuild route-aware tile readiness so active-region streaming can prioritize corridors and emerging nodes.");
            }

            if (RouteCorridors.Count > 0 && routeReadyTiles == 0)
            {
                AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.TerrainTiles, "tile_route_readiness", "No terrain tiles qualify as route-ready despite generated corridors.", "Corridors should create load/readiness pressure even before visible wagon agents exist.");
            }

            if (highBurdenTileCount > 0)
            {
                AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Info, RegionalFoundationIssueCategory.TerrainTiles, "tile_freight_burden", $"{highBurdenTileCount} terrain tile(s) touch high-burden freight corridors.", "Use these tiles as future active-region candidates for visible wagon strain, mud pressure, or route work.");
            }
        }

        private void InspectWatercourses(List<RegionalFoundationIssueRecord> issues)
        {
            if (Watercourses.Count == 0)
            {
                AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Error, RegionalFoundationIssueCategory.Hydrology, "watercourses", "No watercourse records exist.", "Hydrology should influence siting, parcels, vegetation, and later freight or mining logic.");
                return;
            }

            for (int i = 0; i < Watercourses.Count; i++)
            {
                RegionalWatercourseRecord water = Watercourses[i];
                if (water == null)
                {
                    AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.Hydrology, $"water_{i}", "Null watercourse entry found.", "Remove or rebuild the broken watercourse record.");
                    continue;
                }

                if (water.Points.Count < 2)
                {
                    AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Error, RegionalFoundationIssueCategory.Hydrology, water.WatercourseId, "Watercourse has fewer than two points.", "Water influence and floodplain checks need at least one segment.");
                }

                if (water.FloodplainWidthMeters < water.InfluenceWidthMeters)
                {
                    AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.Hydrology, water.WatercourseId, "Floodplain width is narrower than influence width.", "Keep wet-ground and floodplain treatment coherent for parcel valuation.");
                }
            }
        }

        private void InspectSurveyParcels(List<RegionalFoundationIssueRecord> issues, HashSet<string> parcelIds)
        {
            if (SurveyParcels.Count == 0)
            {
                AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Error, RegionalFoundationIssueCategory.SurveyParcels, "survey_parcels", "No survey parcel records exist.", "The legal parcel skeleton is required before acquisition, valuation, and settlement logic can safely build on the region.");
                return;
            }

            bool hasTownCore = false;
            bool hasRural = false;
            bool hasRemoteSuitability = false;
            int missingAccess = 0;
            int missingProvenance = 0;
            int highTerrainBurden = 0;
            int corridorLinked = 0;
            int highFreightAccess = 0;
            int corridorBurdenFlags = 0;
            float readinessSum = 0f;
            for (int i = 0; i < SurveyParcels.Count; i++)
            {
                RegionalSurveyParcelRecord parcel = SurveyParcels[i];
                if (parcel == null)
                {
                    AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.SurveyParcels, $"parcel_{i}", "Null survey parcel entry found.", "Remove or rebuild the broken parcel record.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(parcel.ParcelId))
                {
                    AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.SurveyParcels, $"parcel_{i}", "Survey parcel has an empty ID.", "Parcel IDs must be stable for save/load, acquisition, and valuation handoff.", true, parcel.CenterMeters);
                }
                else if (!parcelIds.Add(parcel.ParcelId))
                {
                    AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.SurveyParcels, parcel.ParcelId, "Duplicate survey parcel ID.", "Parcel identity must remain unique for legal-ownership and save/load handoff.", true, parcel.CenterMeters);
                }

                hasTownCore |= parcel.ParcelKind == RegionalParcelKind.TownPlatCore;
                hasRural |= parcel.ParcelKind == RegionalParcelKind.RuralTract || parcel.ParcelKind == RegionalParcelKind.EdgeExpansion;
                hasRemoteSuitability |= parcel.RemoteSuitability != RegionalRemoteSuitability.None;
                readinessSum += parcel.DevelopmentReadiness01;
                if (parcel.AccessQuality == RegionalParcelAccessQuality.None)
                {
                    missingAccess++;
                }

                if (parcel.Provenance == RegionalParcelProvenance.Unset)
                {
                    missingProvenance++;
                }

                if (parcel.TerrainBurden01 >= 0.72f)
                {
                    highTerrainBurden++;
                }

                if (parcel.CorridorRelation != RegionalParcelCorridorRelation.None && parcel.CorridorRelation != RegionalParcelCorridorRelation.OffRoute)
                {
                    corridorLinked++;
                }

                if (parcel.FreightAccess01 >= 0.58f)
                {
                    highFreightAccess++;
                }

                if (parcel.CorridorBurden01 >= 0.68f)
                {
                    corridorBurdenFlags++;
                }

                if (!string.IsNullOrWhiteSpace(parcel.NearestCorridorId) && RouteCorridors.Count > 0 && parcel.CorridorRelation == RegionalParcelCorridorRelation.None)
                {
                    AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.SurveyParcels, parcel.ParcelId, "Parcel has a nearest corridor ID but no corridor relation.", "Rebuild parcel corridor influence so acquisition and freight handoffs read a coherent parcel context.", true, parcel.CenterMeters);
                }

                if (parcel.BoundsMeters.width <= 0f || parcel.BoundsMeters.height <= 0f)
                {
                    AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Error, RegionalFoundationIssueCategory.SurveyParcels, parcel.ParcelId, "Parcel bounds are invalid.", "Regenerate the survey grid before acquisition or valuation systems read this parcel.", true, parcel.CenterMeters);
                }
            }

            if (RouteCorridors.Count > 0 && corridorLinked == 0)
            {
                AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.SurveyParcels, "parcel_corridor_influence", "Route corridors exist but no parcels carry corridor influence metadata.", "Rebuild parcel-to-corridor influence before valuation, acquisition, or freight systems use this foundation.");
            }

            if (highFreightAccess == 0 && RouteCorridors.Count > 0)
            {
                AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Info, RegionalFoundationIssueCategory.SurveyParcels, "freight_access_parcels", "No parcel currently has strong freight access from a route corridor.", "Acceptable for some rough seeds, but acquisition and logistics reads may be thin until route proximity improves.");
            }

            if (corridorBurdenFlags > 0)
            {
                AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Info, RegionalFoundationIssueCategory.SurveyParcels, "corridor_burden_parcels", $"{corridorBurdenFlags} parcel(s) sit under heavy corridor burden influence.", "Treat these as future freight pressure or support-burden candidates, not silent penalties.");
            }

            if (!hasTownCore)
            {
                AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.SurveyParcels, "town_plat", "No town-plat core parcel exists.", "The opening foothold should have a legal townsite core inside the wider survey frame.");
            }

            if (!hasRural)
            {
                AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.SurveyParcels, "rural_parcels", "No rural or edge expansion parcels exist.", "The region needs land beyond the first town for farms, ranches, remote work, and later settlement attempts.");
            }

            if (!hasRemoteSuitability)
            {
                AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Info, RegionalFoundationIssueCategory.SurveyParcels, "remote_suitability", "No parcels currently expose remote suitability hooks.", "Later mining, timber, freight, and secondary settlement logic will need at least a few candidate tracts.");
            }

            float averageReadiness = readinessSum / Mathf.Max(1, SurveyParcels.Count);
            if (averageReadiness < 0.30f)
            {
                AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.SurveyParcels, "parcel_readiness", $"Average parcel development readiness is low ({averageReadiness:P0}).", "Check terrain burden, access, water relation, and anchor-weighted parcel scoring.");
            }

            if (missingAccess > 0)
            {
                AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.SurveyParcels, "parcel_access", $"{missingAccess} parcels have no access quality.", "Every parcel should carry at least rough access information for valuation and acquisition readouts.");
            }

            if (missingProvenance > 0)
            {
                AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.SurveyParcels, "parcel_provenance", $"{missingProvenance} parcels have unset provenance.", "Legal history and ownership handoff need parcel provenance even when the player sees only a summary.");
            }

            if (highTerrainBurden > SurveyParcels.Count * 0.55f)
            {
                AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.SurveyParcels, "parcel_terrain_burden", $"A majority of parcels have high terrain burden ({highTerrainBurden}/{SurveyParcels.Count}).", "Keep natural roughness, but avoid seeds where practical development becomes mostly implausible.");
            }
        }

        private void InspectSettlements(List<RegionalFoundationIssueRecord> issues, HashSet<string> settlementIds)
        {
            if (Settlements.Count == 0)
            {
                AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Error, RegionalFoundationIssueCategory.SettlementNodes, "settlements", "No settlement records exist.", "The opening foothold and later regional nodes must be explicit data records.");
                return;
            }

            bool hasOpeningFoothold = false;
            bool hasNonAnchorPotential = false;
            bool hasOpeningSuccessionRole = false;
            int challengerCount = 0;
            int stalledCount = 0;
            float strongestChallenge = 0f;
            for (int i = 0; i < Settlements.Count; i++)
            {
                RegionalSettlementRecord settlement = Settlements[i];
                if (settlement == null)
                {
                    AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.SettlementNodes, $"settlement_{i}", "Null settlement entry found.", "Remove or rebuild the broken settlement record.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(settlement.SettlementId))
                {
                    AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.SettlementNodes, $"settlement_{i}", "Settlement has an empty ID.", "Settlement IDs must be stable for node UI, logistics, and save/load handoff.", true, settlement.CenterMeters);
                }
                else if (!settlementIds.Add(settlement.SettlementId))
                {
                    AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.SettlementNodes, settlement.SettlementId, "Duplicate settlement ID.", "Settlement identity must remain unique for regional hierarchy and route handoff.", true, settlement.CenterMeters);
                }

                hasOpeningFoothold |= settlement.Character == RegionalSettlementCharacter.OpeningFootholdGateway;
                hasNonAnchorPotential |= settlement.Character != RegionalSettlementCharacter.OpeningFootholdGateway;
                hasOpeningSuccessionRole |= settlement.SuccessionRole == RegionalSettlementSuccessionRole.OpeningFoothold;
                if (settlement.SuccessionRole is RegionalSettlementSuccessionRole.PeerChallenger or RegionalSettlementSuccessionRole.LikelyRegionalSuccessor)
                {
                    challengerCount++;
                    strongestChallenge = Mathf.Max(strongestChallenge, settlement.FootholdChallenge01);
                }

                if (settlement.SuccessionRole == RegionalSettlementSuccessionRole.StalledOrDecliningNode)
                {
                    stalledCount++;
                }

                if (settlement.SuccessionRole == RegionalSettlementSuccessionRole.Unset)
                {
                    AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.SettlementNodes, settlement.SettlementId, "Settlement succession role is unset.", "Rebuild hierarchy metadata so later nodes can challenge the opening foothold without hard-coding dominance.", true, settlement.CenterMeters);
                }

                if (settlement.RegionalGravity01 <= 0f)
                {
                    AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.SettlementNodes, settlement.SettlementId, "Settlement regional gravity is empty.", "Regional hierarchy needs a gravity score derived from population, service, freight, permanence, and stability.", true, settlement.CenterMeters);
                }
                if (settlement.PopulationBand == RegionalPopulationBand.Unset)
                {
                    AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.SettlementNodes, settlement.SettlementId, "Settlement population band is unset.", "Node growth should use broad population bands and hidden trigger windows.", true, settlement.CenterMeters);
                }

                if (settlement.NextTriggerWindowMaxPopulation < settlement.NextTriggerWindowMinPopulation)
                {
                    AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.SettlementNodes, settlement.SettlementId, "Settlement trigger window is inverted.", "Soft growth windows should be stable and hidden from player-facing readouts.", true, settlement.CenterMeters);
                }

                if (settlement.Permanence == RegionalSettlementPermanence.Unset)
                {
                    AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.SettlementNodes, settlement.SettlementId, "Settlement permanence is unset.", "Persistence, decline, and false-start logic need permanence metadata.", true, settlement.CenterMeters);
                }

                if (settlement.DeclineRisk01 >= 0.72f)
                {
                    AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Info, RegionalFoundationIssueCategory.SettlementNodes, settlement.SettlementId, $"Settlement has visible decline risk ({settlement.DeclineRisk01:P0}).", "This is acceptable if it represents a fragile camp or service gap rather than a broken generation output.", true, settlement.CenterMeters);
                }
            }

            if (!hasOpeningFoothold)
            {
                AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.SettlementNodes, "opening_foothold", "No settlement is marked as the opening foothold gateway.", "The first town should remain the tutorial anchor even though it is not permanently dominant.");
            }

            if (!hasOpeningSuccessionRole)
            {
                AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.SettlementNodes, "opening_foothold_succession", "No settlement carries the opening-foothold succession role.", "The opening town should be explicit as a starting foothold, not assumed as permanent regional destiny.");
            }

            if (challengerCount > 0)
            {
                AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Info, RegionalFoundationIssueCategory.SettlementNodes, "regional_succession", $"{challengerCount} node(s) can meaningfully challenge the opening foothold; strongest challenge {strongestChallenge:P0}.", "Use this as a future Opportunity & Pressure and regional hierarchy hook, not as an automatic town replacement.");
            }

            if (stalledCount > 0)
            {
                AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Info, RegionalFoundationIssueCategory.SettlementNodes, "stalled_nodes", $"{stalledCount} node(s) read as stalled or decline-prone.", "These are useful false-start hooks if caused by support burden, weak services, or unstable freight rather than broken generation.");
            }

            if (!hasNonAnchorPotential)
            {
                AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Info, RegionalFoundationIssueCategory.SettlementNodes, "regional_plurality", "No non-anchor settlement potential is present in this seed.", "Acceptable for some seeds, but later gameplay needs room for remote nodes, service pockets, or false starts.");
            }
        }

        private void InspectRouteCorridors(List<RegionalFoundationIssueRecord> issues, HashSet<string> parcelIds, HashSet<string> settlementIds, HashSet<string> routeIds)
        {
            if (RouteCorridors.Count == 0)
            {
                AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Error, RegionalFoundationIssueCategory.RouteCorridors, "route_corridors", "No regional route corridors exist.", "Trip-based logistics, freight burden, and regional node support need route handoff data.");
                return;
            }

            bool hasOpeningSpine = false;
            int connectorCount = 0;
            Dictionary<string, int> destinationRouteCounts = BuildDestinationRouteCounts();
            for (int i = 0; i < RouteCorridors.Count; i++)
            {
                RegionalRouteCorridorRecord route = RouteCorridors[i];
                if (route == null)
                {
                    AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.RouteCorridors, $"route_{i}", "Null route corridor entry found.", "Remove or rebuild the broken route record.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(route.CorridorId))
                {
                    AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.RouteCorridors, $"route_{i}", "Route corridor has an empty ID.", "Corridor IDs should be stable for shipment, debug, and save/load handoff.");
                }
                else if (!routeIds.Add(route.CorridorId))
                {
                    AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.RouteCorridors, route.CorridorId, "Duplicate route corridor ID.", "Route identity must remain unique for future shipment and cart agent handoff.");
                }

                hasOpeningSpine |= route.Kind == RegionalRouteCorridorKind.OpeningFootholdSpine;
                if (route.Kind != RegionalRouteCorridorKind.OpeningFootholdSpine)
                {
                    connectorCount++;
                }

                if (route.Points.Count < 2 || route.LengthMeters <= 0f)
                {
                    AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Error, RegionalFoundationIssueCategory.RouteCorridors, route.CorridorId, "Route corridor has no usable polyline.", "Regenerate route points before visible carts, freight timing, or terrain grading reads this corridor.");
                }
                else
                {
                    InspectRouteEndpointGeometry(issues, route, destinationRouteCounts);
                }

                if (!string.IsNullOrWhiteSpace(route.SourceSettlementId) && settlementIds.Count > 0 && !settlementIds.Contains(route.SourceSettlementId))
                {
                    AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.RouteCorridors, route.CorridorId, $"Route source settlement '{route.SourceSettlementId}' is not present.", "Keep route endpoints aligned with settlement records for future regional movement.");
                }

                if (!string.IsNullOrWhiteSpace(route.DestinationSettlementId) && settlementIds.Count > 0 && !settlementIds.Contains(route.DestinationSettlementId))
                {
                    AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.RouteCorridors, route.CorridorId, $"Route destination settlement '{route.DestinationSettlementId}' is not present.", "Keep route endpoints aligned with settlement records for future regional movement.");
                }

                if (!string.IsNullOrWhiteSpace(route.DestinationParcelId) && parcelIds.Count > 0 && !parcelIds.Contains(route.DestinationParcelId))
                {
                    AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.RouteCorridors, route.CorridorId, $"Route destination parcel '{route.DestinationParcelId}' is not present.", "Keep route endpoints aligned with survey parcels for acquisition and logistics handoff.");
                }

                if (route.FreightBurden01 >= 0.74f)
                {
                    AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Info, RegionalFoundationIssueCategory.RouteCorridors, route.CorridorId, $"Route has heavy freight burden ({route.FreightBurden01:P0}).", "This should become an Opportunity & Pressure source rather than a silent penalty.");
                }

                if (route.SeasonalReliability01 < 0.28f)
                {
                    AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.RouteCorridors, route.CorridorId, $"Route seasonal reliability is poor ({route.SeasonalReliability01:P0}).", "Flag as mud, wet-ground, or bridge/grade pressure before logistics depends on it.");
                }

                if (route.PrimaryConstraintKind == RegionalRouteConstraintKind.None)
                {
                    AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.RouteCorridors, route.CorridorId, "Route has no crossing or seasonal constraint assessment.", "Rebuild route constraint metadata before freight or active-region transport uses this route.");
                }

                if (route.BridgeOrFordNeed01 >= 0.58f)
                {
                    AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Info, RegionalFoundationIssueCategory.RouteCorridors, route.CorridorId, $"Route likely needs a ford, bridge, or low-water crossing plan ({route.BridgeOrFordNeed01:P0}).", "Surface this later as infrastructure pressure rather than hiding it in reliability math.");
                }

                if (route.MudSeasonRisk01 >= 0.62f)
                {
                    AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Info, RegionalFoundationIssueCategory.RouteCorridors, route.CorridorId, $"Route has strong mud-season risk ({route.MudSeasonRisk01:P0}).", "Use this as a future freight delay and road-improvement hook.");
                }
            }

            if (!hasOpeningSpine)
            {
                AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Error, RegionalFoundationIssueCategory.RouteCorridors, "opening_route_spine", "Missing opening foothold route spine.", "The starting settlement needs a readable commercial spine before routes to other nodes are evaluated.");
            }

            if (Settlements.Count > 1 && connectorCount == 0)
            {
                AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.RouteCorridors, "regional_connectors", "Multiple settlements exist but no connector corridors exist.", "Secondary settlements should have freight/travel handoff back to the opening foothold or a stronger route network.");
            }
        }

        private Dictionary<string, int> BuildDestinationRouteCounts()
        {
            Dictionary<string, int> counts = new();
            for (int i = 0; i < RouteCorridors.Count; i++)
            {
                RegionalRouteCorridorRecord route = RouteCorridors[i];
                if (route == null || string.IsNullOrWhiteSpace(route.DestinationSettlementId))
                {
                    continue;
                }

                counts.TryGetValue(route.DestinationSettlementId, out int current);
                counts[route.DestinationSettlementId] = current + 1;
            }

            return counts;
        }

        private void InspectRouteEndpointGeometry(List<RegionalFoundationIssueRecord> issues, RegionalRouteCorridorRecord route, IReadOnlyDictionary<string, int> destinationRouteCounts)
        {
            if (route == null || route.Points.Count < 2)
            {
                return;
            }

            Vector2 start = route.Points[0];
            Vector2 end = route.Points[route.Points.Count - 1];
            for (int i = 0; i < route.Points.Count; i++)
            {
                Vector2 point = route.Points[i];
                if (!IsPointInsideRegion(point, 96f))
                {
                    AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.RouteCorridors, route.CorridorId, "Route corridor leaves the regional map bounds.", "Keep generated route endpoints and bends inside the regional foundation before visible carts or terrain grading rely on them.", true, point);
                    break;
                }
            }

            RegionalSettlementRecord sourceSettlement = FindSettlementById(route.SourceSettlementId);
            if (sourceSettlement != null)
            {
                float sourceDistance = Vector2.Distance(start, sourceSettlement.CenterMeters);
                if (sourceDistance > ResolveRouteEndpointToleranceMeters(route.Kind))
                {
                    AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.RouteCorridors, route.CorridorId, $"Route source endpoint sits {sourceDistance:0}m from its source settlement.", "Route handoff should begin near the settlement it claims to serve.", true, start);
                }
            }

            RegionalSettlementRecord destinationSettlement = FindSettlementById(route.DestinationSettlementId);
            if (destinationSettlement != null)
            {
                float destinationDistance = Vector2.Distance(end, destinationSettlement.CenterMeters);
                if (destinationDistance > ResolveRouteEndpointToleranceMeters(route.Kind))
                {
                    AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.RouteCorridors, route.CorridorId, $"Route destination endpoint sits {destinationDistance:0}m from its destination settlement.", "Route handoff should end near the settlement it claims to support.", true, end);
                }
            }

            RegionalSurveyParcelRecord destinationParcel = FindParcelById(SurveyParcels, route.DestinationParcelId);
            if (destinationParcel != null)
            {
                float parcelDistance = DistanceToRect(end, destinationParcel.BoundsMeters);
                if (parcelDistance > 320f)
                {
                    AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Warning, RegionalFoundationIssueCategory.RouteCorridors, route.CorridorId, $"Route endpoint sits {parcelDistance:0}m from destination parcel {destinationParcel.ParcelId}.", "Keep route parcel endpoints close enough for acquisition, valuation, and freight handoff to agree.", true, end);
                }
            }

            int destinationRouteCount = 0;
            if (!string.IsNullOrWhiteSpace(route.DestinationSettlementId) && destinationRouteCounts != null)
            {
                destinationRouteCounts.TryGetValue(route.DestinationSettlementId, out destinationRouteCount);
            }

            bool singleConstrainedConnector = route.Kind != RegionalRouteCorridorKind.OpeningFootholdSpine
                && destinationRouteCount <= 1
                && (route.Practicality01 < 0.36f || route.SeasonalReliability01 < 0.36f || route.FreightBurden01 >= 0.72f || ResolveRouteConstraintSeverity01(route) >= 0.58f);
            if (singleConstrainedConnector)
            {
                AddFoundationIssue(issues, RegionalFoundationIssueSeverity.Info, RegionalFoundationIssueCategory.RouteCorridors, route.CorridorId, "Settlement depends on a single constrained corridor.", "Later logistics should surface this as support burden, road work, ferry/bridge need, or alternate-route pressure rather than hiding it in a score.", true, GetRouteMidpoint(route));
            }
        }

        private bool RouteHasEndpointGeometryRisk(RegionalRouteCorridorRecord route)
        {
            if (route == null || route.Points.Count < 2)
            {
                return true;
            }

            for (int i = 0; i < route.Points.Count; i++)
            {
                if (!IsPointInsideRegion(route.Points[i], 96f))
                {
                    return true;
                }
            }

            Vector2 start = route.Points[0];
            Vector2 end = route.Points[route.Points.Count - 1];
            RegionalSettlementRecord sourceSettlement = FindSettlementById(route.SourceSettlementId);
            if (sourceSettlement != null && Vector2.Distance(start, sourceSettlement.CenterMeters) > ResolveRouteEndpointToleranceMeters(route.Kind))
            {
                return true;
            }

            RegionalSettlementRecord destinationSettlement = FindSettlementById(route.DestinationSettlementId);
            if (destinationSettlement != null && Vector2.Distance(end, destinationSettlement.CenterMeters) > ResolveRouteEndpointToleranceMeters(route.Kind))
            {
                return true;
            }

            RegionalSurveyParcelRecord destinationParcel = FindParcelById(SurveyParcels, route.DestinationParcelId);
            return destinationParcel != null && DistanceToRect(end, destinationParcel.BoundsMeters) > 320f;
        }

        private static RegionalSurveyParcelRecord FindParcelById(IReadOnlyList<RegionalSurveyParcelRecord> parcels, string parcelId)
        {
            if (parcels == null || string.IsNullOrWhiteSpace(parcelId))
            {
                return null;
            }

            for (int i = 0; i < parcels.Count; i++)
            {
                RegionalSurveyParcelRecord parcel = parcels[i];
                if (parcel != null && string.Equals(parcel.ParcelId, parcelId, StringComparison.OrdinalIgnoreCase))
                {
                    return parcel;
                }
            }

            return null;
        }

        private static float ResolveRouteConstraintSeverity01(RegionalRouteCorridorRecord route)
        {
            if (route == null)
            {
                return 0f;
            }

            float kindSeverity = route.PrimaryConstraintKind switch
            {
                RegionalRouteConstraintKind.BridgeLikely => 0.72f,
                RegionalRouteConstraintKind.MudSeasonRisk => 0.62f,
                RegionalRouteConstraintKind.FordOrLowCrossing => 0.56f,
                RegionalRouteConstraintKind.SteepGrade => 0.48f,
                RegionalRouteConstraintKind.WetGround => 0.42f,
                RegionalRouteConstraintKind.StableDryTrack => 0.08f,
                RegionalRouteConstraintKind.None => 0.25f,
                _ => 0.20f
            };

            float measuredSeverity = Mathf.Clamp01(
                route.BridgeOrFordNeed01 * 0.34f
                + route.MudSeasonRisk01 * 0.30f
                + route.GradeBurden01 * 0.22f
                + Mathf.Clamp01(route.WetGroundCrossingCount / 3f) * 0.14f);

            return Mathf.Clamp01(Mathf.Max(kindSeverity, measuredSeverity));
        }

        private RegionalSettlementRecord FindSettlementById(string settlementId)
        {
            if (string.IsNullOrWhiteSpace(settlementId))
            {
                return null;
            }

            for (int i = 0; i < Settlements.Count; i++)
            {
                RegionalSettlementRecord settlement = Settlements[i];
                if (settlement != null && settlement.SettlementId == settlementId)
                {
                    return settlement;
                }
            }

            return null;
        }

        private bool IsPointInsideRegion(Vector2 point, float toleranceMeters)
        {
            return point.x >= -toleranceMeters
                && point.y >= -toleranceMeters
                && point.x <= RegionSizeMeters.x + toleranceMeters
                && point.y <= RegionSizeMeters.y + toleranceMeters;
        }

        private static float ResolveRouteEndpointToleranceMeters(RegionalRouteCorridorKind kind)
        {
            return kind switch
            {
                RegionalRouteCorridorKind.FreightTrack => 780f,
                RegionalRouteCorridorKind.RemoteWorksiteTrack => 760f,
                RegionalRouteCorridorKind.CreekOrDrawTrack => 700f,
                RegionalRouteCorridorKind.OpeningFootholdSpine => 860f,
                _ => 640f
            };
        }

        private static float DistanceToRect(Vector2 point, Rect rect)
        {
            float x = Mathf.Clamp(point.x, rect.xMin, rect.xMax);
            float y = Mathf.Clamp(point.y, rect.yMin, rect.yMax);
            return Vector2.Distance(point, new Vector2(x, y));
        }

        private List<RegionalFoundationOpportunityRecord> BuildOpportunityReadouts()
        {
            List<RegionalFoundationOpportunityRecord> opportunities = new();
            AddBestNearTermExpansionParcel(opportunities);
            AddBestRemoteWorksiteCandidate(opportunities);
            AddStrongestFreightParcel(opportunities);
            AddWeakestRouteCandidate(opportunities);
            AddHighestBurdenSettlement(opportunities);
            AddCredibleSuccessorNode(opportunities);
            opportunities.Sort((a, b) => b.Score01.CompareTo(a.Score01));
            return opportunities;
        }

        private void AddBestNearTermExpansionParcel(List<RegionalFoundationOpportunityRecord> opportunities)
        {
            RegionalSurveyParcelRecord best = null;
            float bestScore = 0f;
            for (int i = 0; i < SurveyParcels.Count; i++)
            {
                RegionalSurveyParcelRecord parcel = SurveyParcels[i];
                if (parcel == null)
                {
                    continue;
                }

                bool sensibleNearTerm = parcel.ParcelKind is RegionalParcelKind.TownPlatCore or RegionalParcelKind.TownPlatEdge or RegionalParcelKind.EdgeExpansion
                    || parcel.SupportBurden01 <= 0.34f
                    || parcel.CorridorRelation == RegionalParcelCorridorRelation.NearOpeningSpine;
                if (!sensibleNearTerm)
                {
                    continue;
                }

                float score = Mathf.Clamp01(
                    parcel.DevelopmentReadiness01 * 0.42f
                    + parcel.BuildSuitability01 * 0.22f
                    + parcel.FreightAccess01 * 0.16f
                    + (1f - parcel.TerrainBurden01) * 0.12f
                    + (1f - parcel.Wetness01) * 0.08f);
                if (score > bestScore)
                {
                    best = parcel;
                    bestScore = score;
                }
            }

            if (best != null)
            {
                AddOpportunity(opportunities, RegionalFoundationOpportunityKind.BestNearTermExpansionParcel, best.ParcelId, best.CenterMeters, bestScore,
                    "Best near-term expansion parcel",
                    $"{best.ParcelKind} with {best.AccessQuality}, readiness {best.DevelopmentReadiness01:P0}, freight access {best.FreightAccess01:P0}, and terrain burden {best.TerrainBurden01:P0}.",
                    "Use as the first parcel to inspect when acquisition, valuation, or tutorial expansion needs a grounded candidate.");
            }
        }

        private void AddBestRemoteWorksiteCandidate(List<RegionalFoundationOpportunityRecord> opportunities)
        {
            RegionalSurveyParcelRecord best = null;
            float bestScore = 0f;
            for (int i = 0; i < SurveyParcels.Count; i++)
            {
                RegionalSurveyParcelRecord parcel = SurveyParcels[i];
                if (parcel == null || parcel.RemoteSuitability == RegionalRemoteSuitability.None)
                {
                    continue;
                }

                float remoteBias = parcel.RemoteSuitability switch
                {
                    RegionalRemoteSuitability.MineralProspect => 0.30f,
                    RegionalRemoteSuitability.TimberWorksite => 0.26f,
                    RegionalRemoteSuitability.FreightOutpost => 0.24f,
                    RegionalRemoteSuitability.MixedOpportunity => 0.20f,
                    RegionalRemoteSuitability.FarmService => 0.14f,
                    RegionalRemoteSuitability.RanchRange => 0.14f,
                    _ => 0f
                };
                float score = Mathf.Clamp01(
                    remoteBias
                    + parcel.ResourceSuitability01 * 0.24f
                    + parcel.FreightAccess01 * 0.20f
                    + parcel.DevelopmentReadiness01 * 0.16f
                    + (1f - parcel.SupportBurden01) * 0.12f
                    - parcel.CorridorBurden01 * 0.06f);
                if (score > bestScore)
                {
                    best = parcel;
                    bestScore = score;
                }
            }

            if (best != null)
            {
                AddOpportunity(opportunities, RegionalFoundationOpportunityKind.BestRemoteWorksiteCandidate, best.ParcelId, best.CenterMeters, bestScore,
                    "Best remote worksite candidate",
                    $"{best.RemoteSuitability} tract with resource score {best.ResourceSuitability01:P0}, corridor relation {best.CorridorRelation}, and support burden {best.SupportBurden01:P0}.",
                    "Use as a later mining, timber, freight, camp, or remote-service handoff candidate; do not treat it as a finished industry system yet.");
            }
        }

        private void AddStrongestFreightParcel(List<RegionalFoundationOpportunityRecord> opportunities)
        {
            RegionalSurveyParcelRecord best = null;
            float bestScore = 0f;
            for (int i = 0; i < SurveyParcels.Count; i++)
            {
                RegionalSurveyParcelRecord parcel = SurveyParcels[i];
                if (parcel == null || parcel.CorridorRelation == RegionalParcelCorridorRelation.None || parcel.CorridorRelation == RegionalParcelCorridorRelation.OffRoute)
                {
                    continue;
                }

                float score = Mathf.Clamp01(
                    parcel.FreightAccess01 * 0.48f
                    + parcel.CorridorInfluence01 * 0.24f
                    + parcel.DevelopmentReadiness01 * 0.14f
                    + (1f - parcel.CorridorBurden01) * 0.10f
                    + (parcel.AccessQuality is RegionalParcelAccessQuality.Gateway or RegionalParcelAccessQuality.RoadFrontage ? 0.04f : 0f));
                if (score > bestScore)
                {
                    best = parcel;
                    bestScore = score;
                }
            }

            if (best != null)
            {
                AddOpportunity(opportunities, RegionalFoundationOpportunityKind.StrongestFreightParcel, best.ParcelId, best.CenterMeters, bestScore,
                    "Strongest freight parcel",
                    $"{best.CorridorRelation} via {best.NearestCorridorKind}; freight access {best.FreightAccess01:P0}; corridor burden {best.CorridorBurden01:P0}.",
                    "Use as a candidate for livery/freight, storage, lumber yard, recurring-order staging, or route-facing valuation tests.");
            }
        }

        private void AddWeakestRouteCandidate(List<RegionalFoundationOpportunityRecord> opportunities)
        {
            RegionalRouteCorridorRecord weakest = null;
            float weakestRisk = 0f;
            for (int i = 0; i < RouteCorridors.Count; i++)
            {
                RegionalRouteCorridorRecord route = RouteCorridors[i];
                if (route == null)
                {
                    continue;
                }

                float risk = Mathf.Clamp01(
                    (1f - route.Practicality01) * 0.26f
                    + (1f - route.SeasonalReliability01) * 0.24f
                    + route.FreightBurden01 * 0.18f
                    + route.MudSeasonRisk01 * 0.14f
                    + route.BridgeOrFordNeed01 * 0.10f
                    + route.GradeBurden01 * 0.08f);
                if (risk > weakestRisk)
                {
                    weakest = route;
                    weakestRisk = risk;
                }
            }

            if (weakest != null)
            {
                AddOpportunity(opportunities, RegionalFoundationOpportunityKind.WeakestRoute, weakest.CorridorId, GetRouteMidpoint(weakest), weakestRisk,
                    "Weakest route corridor",
                    $"{weakest.Kind} with practicality {weakest.Practicality01:P0}, reliability {weakest.SeasonalReliability01:P0}, burden {weakest.FreightBurden01:P0}, and constraint {weakest.PrimaryConstraintKind}.",
                    "Use as a future road-work, bridge/ford, mud-season, or freight-delay pressure hook.");
            }
        }

        private void AddHighestBurdenSettlement(List<RegionalFoundationOpportunityRecord> opportunities)
        {
            RegionalSettlementRecord highest = null;
            float highestScore = 0f;
            for (int i = 0; i < Settlements.Count; i++)
            {
                RegionalSettlementRecord settlement = Settlements[i];
                if (settlement == null)
                {
                    continue;
                }

                float score = Mathf.Clamp01(
                    settlement.SupportBurden01 * 0.34f
                    + settlement.ServiceDeficit01 * 0.28f
                    + settlement.DeclineRisk01 * 0.22f
                    + (1f - settlement.FreightSupport01) * 0.16f);
                if (score > highestScore)
                {
                    highest = settlement;
                    highestScore = score;
                }
            }

            if (highest != null)
            {
                AddOpportunity(opportunities, RegionalFoundationOpportunityKind.HighestBurdenSettlement, highest.SettlementId, highest.CenterMeters, highestScore,
                    "Highest-burden settlement node",
                    $"{highest.Label} / {highest.Character}; support burden {highest.SupportBurden01:P0}; service deficit {highest.ServiceDeficit01:P0}; decline risk {highest.DeclineRisk01:P0}.",
                    "Use as an Opportunity & Pressure hook for boarding, local services, freight support, doctor/schoolhouse pressure, or false-start recovery.");
            }
        }

        private void AddCredibleSuccessorNode(List<RegionalFoundationOpportunityRecord> opportunities)
        {
            RegionalSettlementRecord best = null;
            float bestScore = 0f;
            for (int i = 0; i < Settlements.Count; i++)
            {
                RegionalSettlementRecord settlement = Settlements[i];
                if (settlement == null || settlement.SuccessionRole == RegionalSettlementSuccessionRole.OpeningFoothold)
                {
                    continue;
                }

                float score = Mathf.Clamp01(settlement.SuccessionReadiness01 * 0.46f + settlement.FootholdChallenge01 * 0.34f + settlement.RegionalGravity01 * 0.20f);
                if (score > bestScore)
                {
                    best = settlement;
                    bestScore = score;
                }
            }

            if (best != null)
            {
                AddOpportunity(opportunities, RegionalFoundationOpportunityKind.CredibleSuccessorNode, best.SettlementId, best.CenterMeters, bestScore,
                    "Most credible successor node",
                    $"{best.SuccessionRole}; gravity {best.RegionalGravity01:P0}; foothold challenge {best.FootholdChallenge01:P0}; readiness {best.SuccessionReadiness01:P0}.",
                    "Use to test the rule that the opening town is a foothold, not a permanent dominant-town lock.");
            }
        }

        private static void AddOpportunity(
            List<RegionalFoundationOpportunityRecord> opportunities,
            RegionalFoundationOpportunityKind kind,
            string subjectId,
            Vector2 locationMeters,
            float score01,
            string title,
            string readout,
            string recommendation)
        {
            if (opportunities == null || kind == RegionalFoundationOpportunityKind.None)
            {
                return;
            }

            opportunities.Add(new RegionalFoundationOpportunityRecord(kind, subjectId, score01, title, readout, recommendation, true, locationMeters));
        }

        private static Vector2 GetRouteMidpoint(RegionalRouteCorridorRecord route)
        {
            if (route == null || route.Points.Count == 0)
            {
                return Vector2.zero;
            }

            return route.Points[Mathf.Clamp(route.Points.Count / 2, 0, route.Points.Count - 1)];
        }

        private static void AddFoundationIssue(
            List<RegionalFoundationIssueRecord> issues,
            RegionalFoundationIssueSeverity severity,
            RegionalFoundationIssueCategory category,
            string subjectId,
            string message,
            string recommendation = "",
            bool hasLocation = false,
            Vector2 locationMeters = default)
        {
            issues.Add(new RegionalFoundationIssueRecord(severity, category, subjectId, message, recommendation, hasLocation, locationMeters));
        }

        public static float CalculateAcreage(Rect boundsMeters)
        {
            return Mathf.Max(0.01f, boundsMeters.width * boundsMeters.height / SquareMetersPerAcre);
        }

        private static RegionalVector2SaveDto ToSaveDto(Vector2 value)
        {
            return new RegionalVector2SaveDto { x = value.x, y = value.y };
        }

        private static RegionalRectSaveDto ToSaveDto(Rect value)
        {
            return new RegionalRectSaveDto { x = value.x, y = value.y, width = value.width, height = value.height };
        }

        private static Vector2 FromSaveDto(RegionalVector2SaveDto dto)
        {
            return dto == null ? Vector2.zero : new Vector2(dto.x, dto.y);
        }

        private static Rect FromSaveDto(RegionalRectSaveDto dto)
        {
            return dto == null ? new Rect() : new Rect(dto.x, dto.y, dto.width, dto.height);
        }
    }

    public static class RegionalWorldGenerator
    {
        private const float OpeningTownRouteHandoffMeters = 160f;

        public static RegionalWorldState Generate(int seed, RegionalWorldGenerationSettings settings)
        {
            RegionalWorldGenerationSettings sanitized = (settings ?? new RegionalWorldGenerationSettings()).Sanitized();
            RegionalTerrainRecipeFamily recipe = SelectRecipe(seed);
            Vector2 size = new(sanitized.regionWidthMeters, sanitized.regionDepthMeters);
            List<RegionalWatercourseRecord> watercourses = GenerateWatercourses(seed, recipe, size);
            RegionalAnchorTownRecord anchor = ChooseAnchorTown(seed, recipe, size, watercourses, sanitized);
            List<RegionalTerrainTileRecord> tiles = GenerateTerrainTiles(seed, recipe, size, sanitized, watercourses, anchor);
            List<RegionalSurveyParcelRecord> parcels = GenerateSurveyParcels(seed, recipe, size, sanitized, watercourses, anchor);
            List<RegionalVegetationZoneRecord> vegetation = GenerateVegetationZones(recipe, tiles, watercourses);
            List<RegionalSettlementClusterRecord> clusters = GenerateSettlementClusters(seed, sanitized, anchor, parcels);
            List<RegionalSettlementRecord> settlements = DeriveSettlements(seed, clusters);
            List<RegionalRouteCorridorRecord> routeCorridors = GenerateRouteCorridors(seed, anchor, parcels, settlements, watercourses);
            routeCorridors = ApplyRouteConstraintMetadata(routeCorridors, watercourses, parcels);
            parcels = ApplyRouteCorridorInfluence(parcels, routeCorridors);
            tiles = ApplyRouteCorridorTileReadiness(tiles, routeCorridors, settlements);

            return RegionalWorldState.Create(
                seed,
                recipe,
                size,
                sanitized.surveyCellSizeMeters,
                sanitized.terrainTileSizeMeters,
                anchor,
                tiles,
                watercourses,
                routeCorridors,
                parcels,
                vegetation,
                clusters,
                settlements);
        }

        private static RegionalTerrainRecipeFamily SelectRecipe(int seed)
        {
            int index = PositiveHash(seed * 397 + 41) % 7;
            return (RegionalTerrainRecipeFamily)index;
        }

        private static List<RegionalTerrainTileRecord> GenerateTerrainTiles(
            int seed,
            RegionalTerrainRecipeFamily recipe,
            Vector2 size,
            RegionalWorldGenerationSettings settings,
            IReadOnlyList<RegionalWatercourseRecord> watercourses,
            RegionalAnchorTownRecord anchor)
        {
            List<RegionalTerrainTileRecord> tiles = new();
            int countX = Mathf.CeilToInt(size.x / settings.terrainTileSizeMeters);
            int countZ = Mathf.CeilToInt(size.y / settings.terrainTileSizeMeters);
            for (int z = 0; z < countZ; z++)
            {
                for (int x = 0; x < countX; x++)
                {
                    Rect bounds = new(
                        x * settings.terrainTileSizeMeters,
                        z * settings.terrainTileSizeMeters,
                        Mathf.Min(settings.terrainTileSizeMeters, size.x - x * settings.terrainTileSizeMeters),
                        Mathf.Min(settings.terrainTileSizeMeters, size.y - z * settings.terrainTileSizeMeters));
                    Vector2 center = bounds.center;
                    float elevation = SampleElevationMeters(seed, recipe, size, center);
                    float ruggedness = SampleSlope01(seed, recipe, size, center);
                    float wetness = EvaluateWaterAccess01(center, watercourses, 900f);
                    float settlementInfluence = anchor != null
                        ? Mathf.Clamp01(1f - Vector2.Distance(center, anchor.CenterMeters) / 2600f)
                        : 0f;
                    ResolveBiome(recipe, wetness, ruggedness, out RegionalBiomeKind biome, out _);
                    float activationReadiness = Mathf.Clamp01(settlementInfluence * 0.56f + wetness * 0.20f + (1f - ruggedness) * 0.16f + (elevation <= 32f ? 0.08f : 0f));
                    string debugSummary = $"Tile readiness {activationReadiness:P0}; settlement influence {settlementInfluence:P0}; {biome}; wet {wetness:P0}; rugged {ruggedness:P0}.";
                    tiles.Add(new RegionalTerrainTileRecord(
                        $"tile_{x:00}_{z:00}",
                        x,
                        z,
                        bounds,
                        elevation,
                        ruggedness,
                        wetness,
                        true,
                        activationReadiness,
                        settlementInfluence,
                        biome,
                        debugSummary));
                }
            }

            return tiles;
        }

        private static List<RegionalWatercourseRecord> GenerateWatercourses(int seed, RegionalTerrainRecipeFamily recipe, Vector2 size)
        {
            List<RegionalWatercourseRecord> watercourses = new();
            System.Random random = new(PositiveHash(seed + (int)recipe * 97));
            bool mostlyNorthSouth = recipe is RegionalTerrainRecipeFamily.CreekCorridorSettlementLand
                or RegionalTerrainRecipeFamily.ValleyMouthTransition
                or RegionalTerrainRecipeFamily.PlainsToHillsInterface;
            watercourses.Add(CreateSinuousWatercourse(
                "river_primary",
                recipe == RegionalTerrainRecipeFamily.PrairieOpenPlains ? RegionalWatercourseKind.Creek : RegionalWatercourseKind.River,
                size,
                mostlyNorthSouth,
                9,
                120f + random.Next(0, 80),
                260f + random.Next(0, 120),
                0.82f,
                random));

            watercourses.Add(CreateSinuousWatercourse(
                "creek_feeder_01",
                RegionalWatercourseKind.Creek,
                size,
                !mostlyNorthSouth,
                6,
                55f + random.Next(0, 55),
                110f + random.Next(0, 70),
                0.46f,
                random));

            if (recipe is RegionalTerrainRecipeFamily.WetLowlandPockets or RegionalTerrainRecipeFamily.RiverBendHigherShelf)
            {
                watercourses.Add(CreateSinuousWatercourse(
                    "lowland_drainage_01",
                    RegionalWatercourseKind.DrainageLine,
                    size,
                    mostlyNorthSouth,
                    5,
                    40f,
                    180f,
                    0.28f,
                    random));
            }

            return watercourses;
        }

        private static RegionalWatercourseRecord CreateSinuousWatercourse(
            string id,
            RegionalWatercourseKind kind,
            Vector2 size,
            bool northSouth,
            int pointCount,
            float influenceWidth,
            float floodplainWidth,
            float flowStrength,
            System.Random random)
        {
            List<Vector2> points = new();
            float crossBase = northSouth
                ? Mathf.Lerp(size.x * 0.28f, size.x * 0.72f, (float)random.NextDouble())
                : Mathf.Lerp(size.y * 0.28f, size.y * 0.72f, (float)random.NextDouble());
            float amplitude = northSouth ? size.x * 0.10f : size.y * 0.10f;
            float phase = (float)random.NextDouble() * Mathf.PI * 2f;
            for (int i = 0; i < pointCount; i++)
            {
                float t = pointCount <= 1 ? 0f : i / (float)(pointCount - 1);
                float along = northSouth ? t * size.y : t * size.x;
                float cross = crossBase + Mathf.Sin(t * Mathf.PI * 2.4f + phase) * amplitude + Mathf.Lerp(-amplitude * 0.35f, amplitude * 0.35f, (float)random.NextDouble());
                points.Add(northSouth
                    ? new Vector2(Mathf.Clamp(cross, 0f, size.x), along)
                    : new Vector2(along, Mathf.Clamp(cross, 0f, size.y)));
            }

            return new RegionalWatercourseRecord(id, kind, influenceWidth, floodplainWidth, flowStrength, points);
        }

        private static RegionalAnchorTownRecord ChooseAnchorTown(
            int seed,
            RegionalTerrainRecipeFamily recipe,
            Vector2 size,
            IReadOnlyList<RegionalWatercourseRecord> watercourses,
            RegionalWorldGenerationSettings settings)
        {
            Vector2 best = size * 0.5f;
            float bestScore = float.MinValue;
            float bestWaterDistance = 0f;
            float bestBuildable = 0f;
            float bestGateway = 0f;
            float bestExpansion = 0f;
            float margin = Mathf.Max(512f, settings.anchorScanStepMeters * 2f);
            int ordinal = 0;
            for (float y = margin; y <= size.y - margin; y += settings.anchorScanStepMeters)
            {
                for (float x = margin; x <= size.x - margin; x += settings.anchorScanStepMeters)
                {
                    Vector2 candidate = new(x, y);
                    float waterDistance = DistanceToNearestWater(candidate, watercourses);
                    float waterScore = Mathf.Clamp01(1f - Mathf.Abs(waterDistance - 260f) / 760f);
                    float slope = SampleSlope01(seed, recipe, size, candidate);
                    float buildable = 1f - slope;
                    float edgeDistance = Mathf.Min(Mathf.Min(x, size.x - x), Mathf.Min(y, size.y - y));
                    float expansion = Mathf.Clamp01(edgeDistance / 1800f);
                    float eastWestGateway = Mathf.Clamp01(Mathf.Abs(x - size.x * 0.5f) / Mathf.Max(1f, size.x * 0.5f));
                    float northSouthGateway = Mathf.Clamp01(1f - Mathf.Abs(y - size.y * 0.52f) / Mathf.Max(1f, size.y * 0.52f));
                    float gateway = Mathf.Clamp01(eastWestGateway * 0.55f + northSouthGateway * 0.45f);
                    float noise = (PositiveHash(seed * 31 + ordinal * 17) % 1000) / 1000f * 0.05f;
                    float score = waterScore * 0.34f + buildable * 0.27f + expansion * 0.20f + gateway * 0.14f + noise;
                    if (score > bestScore)
                    {
                        best = candidate;
                        bestScore = score;
                        bestWaterDistance = waterDistance;
                        bestBuildable = buildable;
                        bestGateway = gateway;
                        bestExpansion = expansion;
                    }

                    ordinal++;
                }
            }

            return RegionalAnchorTownRecord.Create(
                "anchor_starting_foothold",
                best,
                bestScore,
                bestWaterDistance,
                bestBuildable,
                bestGateway,
                bestExpansion,
                $"Selected for water distance {bestWaterDistance:0}m, buildable shelf {bestBuildable:P0}, gateway {bestGateway:P0}, expansion room {bestExpansion:P0}.");
        }

        private static List<RegionalSurveyParcelRecord> GenerateSurveyParcels(
            int seed,
            RegionalTerrainRecipeFamily recipe,
            Vector2 size,
            RegionalWorldGenerationSettings settings,
            IReadOnlyList<RegionalWatercourseRecord> watercourses,
            RegionalAnchorTownRecord anchor)
        {
            List<RegionalSurveyParcelRecord> parcels = new();
            int sectionsX = Mathf.CeilToInt(size.x / settings.sectionSizeMeters);
            int sectionsZ = Mathf.CeilToInt(size.y / settings.sectionSizeMeters);
            for (int z = 0; z < sectionsZ; z++)
            {
                for (int x = 0; x < sectionsX; x++)
                {
                    Rect section = new(
                        x * settings.sectionSizeMeters,
                        z * settings.sectionSizeMeters,
                        Mathf.Min(settings.sectionSizeMeters, size.x - x * settings.sectionSizeMeters),
                        Mathf.Min(settings.sectionSizeMeters, size.y - z * settings.sectionSizeMeters));
                    string sectionId = $"S{x + 1:00}-{z + 1:00}";
                    AddQuarterSectionParcels(parcels, seed, recipe, size, section, sectionId, watercourses, anchor);
                }
            }

            PromoteStrongestRoughParcelIfMissing(parcels);
            PromoteMissingRemoteSuitabilityHooks(parcels);
            AddTownPlatParcels(parcels, settings, watercourses, anchor);
            return parcels;
        }

        private static void PromoteMissingRemoteSuitabilityHooks(List<RegionalSurveyParcelRecord> parcels)
        {
            if (parcels == null || parcels.Count == 0)
            {
                return;
            }

            PromoteRemoteSuitabilityIfMissing(parcels, RegionalRemoteSuitability.MineralProspect, parcel => parcel.CalculateMineralSuitability01() + parcel.ResourceSuitability01 * 0.22f + parcel.MeanSlope01 * 0.12f);
            PromoteRemoteSuitabilityIfMissing(parcels, RegionalRemoteSuitability.TimberWorksite, parcel => parcel.CalculateTimberSuitability01() + parcel.WaterAccess01 * 0.18f + (1f - parcel.MeanSlope01) * 0.10f);
            PromoteRemoteSuitabilityIfMissing(parcels, RegionalRemoteSuitability.FreightOutpost, parcel => parcel.CalculateFreightOutpostSuitability01() + parcel.SupportBurden01 * 0.12f + ResolveAccessScore01(parcel.AccessQuality) * 0.10f);
        }

        private static void PromoteRemoteSuitabilityIfMissing(
            List<RegionalSurveyParcelRecord> parcels,
            RegionalRemoteSuitability targetSuitability,
            Func<RegionalSurveyParcelRecord, float> scoreFunc)
        {
            if (parcels.Exists(parcel => parcel != null && parcel.RemoteSuitability == targetSuitability))
            {
                return;
            }

            int bestIndex = -1;
            float bestScore = float.MinValue;
            for (int i = 0; i < parcels.Count; i++)
            {
                RegionalSurveyParcelRecord parcel = parcels[i];
                if (parcel == null || parcel.ParcelKind is RegionalParcelKind.TownPlatCore or RegionalParcelKind.TownPlatEdge)
                {
                    continue;
                }

                float score = scoreFunc != null ? scoreFunc(parcel) : 0f;
                score += parcel.ParcelKind == RegionalParcelKind.RoughParcel ? 0.10f : 0f;
                score += parcel.RemoteSuitability == RegionalRemoteSuitability.None ? 0.08f : -0.08f;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0)
            {
                return;
            }

            RegionalSurveyParcelRecord source = parcels[bestIndex];
            RegionalParcelProvenance provenance = targetSuitability is RegionalRemoteSuitability.MineralProspect or RegionalRemoteSuitability.TimberWorksite
                ? RegionalParcelProvenance.RemoteIndustrialClaim
                : source.Provenance;
            string context = targetSuitability switch
            {
                RegionalRemoteSuitability.MineralProspect => "rough-country prospect: guaranteed regional claim/mineral hook for later diligence",
                RegionalRemoteSuitability.TimberWorksite => "timber/draw pocket: guaranteed regional logging and sawmill-support hook",
                RegionalRemoteSuitability.FreightOutpost => "freight-support location: guaranteed regional route/livery support hook",
                _ => source.DistrictContext
            };
            string reason = string.IsNullOrWhiteSpace(source.DebugReason)
                ? $"Promoted to {targetSuitability} to preserve regional future-system coverage."
                : source.DebugReason + $" Promoted to {targetSuitability} to preserve regional future-system coverage.";

            parcels[bestIndex] = new RegionalSurveyParcelRecord(
                source.ParcelId,
                source.SectionId,
                source.TractId,
                source.ParcelKind == RegionalParcelKind.RuralTract && targetSuitability == RegionalRemoteSuitability.MineralProspect ? RegionalParcelKind.RoughParcel : source.ParcelKind,
                source.BoundsMeters,
                source.Acreage,
                source.MeanSlope01,
                source.Wetness01,
                source.WaterAccess01,
                source.FarmSuitability01,
                source.BuildSuitability01,
                source.ResourceSuitability01,
                source.SupportBurden01,
                source.FrontageClass,
                source.AccessQuality,
                provenance,
                targetSuitability,
                source.TerrainBurden01,
                source.DevelopmentReadiness01,
                context,
                reason,
                source.CorridorRelation,
                source.NearestCorridorId,
                source.NearestCorridorKind,
                source.DistanceToCorridorMeters,
                source.CorridorInfluence01,
                source.FreightAccess01,
                source.CorridorBurden01,
                source.CorridorContext);
        }

        private static void PromoteStrongestRoughParcelIfMissing(List<RegionalSurveyParcelRecord> parcels)
        {
            if (parcels == null || parcels.Exists(parcel => parcel != null && parcel.ParcelKind == RegionalParcelKind.RoughParcel))
            {
                return;
            }

            int bestIndex = -1;
            float bestScore = float.MinValue;
            for (int i = 0; i < parcels.Count; i++)
            {
                RegionalSurveyParcelRecord parcel = parcels[i];
                if (parcel == null || parcel.ParcelKind != RegionalParcelKind.RuralTract)
                {
                    continue;
                }

                float score = parcel.ResourceSuitability01 * 0.55f + parcel.MeanSlope01 * 0.45f;
                if (score > bestScore)
                {
                    bestIndex = i;
                    bestScore = score;
                }
            }

            if (bestIndex < 0)
            {
                return;
            }

            RegionalSurveyParcelRecord source = parcels[bestIndex];
            parcels[bestIndex] = new RegionalSurveyParcelRecord(
                source.ParcelId,
                source.SectionId,
                source.TractId,
                RegionalParcelKind.RoughParcel,
                source.BoundsMeters,
                source.Acreage,
                source.MeanSlope01,
                source.Wetness01,
                source.WaterAccess01,
                source.FarmSuitability01,
                source.BuildSuitability01,
                source.ResourceSuitability01,
                source.SupportBurden01,
                source.FrontageClass,
                source.AccessQuality,
                RegionalParcelProvenance.RemoteIndustrialClaim,
                source.RemoteSuitability == RegionalRemoteSuitability.None ? RegionalRemoteSuitability.MineralProspect : source.RemoteSuitability,
                source.TerrainBurden01,
                source.DevelopmentReadiness01,
                source.DistrictContext,
                $"{source.DebugReason} Promoted to rough-country industrial placeholder because this seed otherwise lacked a rough parcel.");
        }

        private static void AddQuarterSectionParcels(
            List<RegionalSurveyParcelRecord> parcels,
            int seed,
            RegionalTerrainRecipeFamily recipe,
            Vector2 size,
            Rect section,
            string sectionId,
            IReadOnlyList<RegionalWatercourseRecord> watercourses,
            RegionalAnchorTownRecord anchor)
        {
            float halfWidth = section.width * 0.5f;
            float halfHeight = section.height * 0.5f;
            for (int dz = 0; dz < 2; dz++)
            {
                for (int dx = 0; dx < 2; dx++)
                {
                    Rect bounds = new(section.x + dx * halfWidth, section.y + dz * halfHeight, halfWidth, halfHeight);
                    Vector2 center = bounds.center;
                    float waterAccess = EvaluateWaterAccess01(center, watercourses, 1200f);
                    float wetness = EvaluateWaterAccess01(center, watercourses, 650f);
                    float slope = SampleSlope01(seed, recipe, size, center);
                    float resource = SampleResourceSuitability01(seed, recipe, size, center);
                    float supportBurden = EvaluateSupportBurden01(center, anchor.CenterMeters, waterAccess);
                    RegionalParcelKind kind = ResolveRuralParcelKind(bounds, size, center, waterAccess, slope, resource, anchor.CenterMeters);
                    float farm = Mathf.Clamp01((1f - slope) * 0.52f + waterAccess * 0.26f + (1f - resource * 0.35f) * 0.22f);
                    float build = Mathf.Clamp01((1f - slope) * 0.62f + Mathf.Clamp01(1f - wetness * 0.45f) * 0.24f + (1f - supportBurden) * 0.14f);
                    float distanceToAnchor = Vector2.Distance(center, anchor.CenterMeters);
                    RegionalParcelFrontageClass frontage = ResolveFrontageClass(kind, bounds, size, center, waterAccess, supportBurden, distanceToAnchor);
                    RegionalParcelAccessQuality access = ResolveAccessQuality(frontage, supportBurden, distanceToAnchor, waterAccess);
                    RegionalParcelProvenance provenance = ResolveProvenance(kind, waterAccess, resource, supportBurden);
                    RegionalRemoteSuitability remoteSuitability = ResolveRemoteSuitability(kind, recipe, waterAccess, slope, resource, supportBurden, distanceToAnchor);
                    float terrainBurden = ResolveTerrainBurden(slope, wetness, supportBurden);
                    float readiness = ResolveDevelopmentReadiness(build, access, terrainBurden, supportBurden, kind);
                    string districtContext = BuildDistrictContext(kind, remoteSuitability, waterAccess, slope, resource, supportBurden);
                    string debugReason = BuildParcelDebugReason(frontage, access, provenance, remoteSuitability, readiness, terrainBurden);
                    parcels.Add(new RegionalSurveyParcelRecord(
                        $"{sectionId}-{(dz * 2 + dx + 1):00}",
                        sectionId,
                        $"Q{dz * 2 + dx + 1}",
                        kind,
                        bounds,
                        RegionalWorldState.CalculateAcreage(bounds),
                        slope,
                        wetness,
                        waterAccess,
                        farm,
                        build,
                        resource,
                        supportBurden,
                        frontage,
                        access,
                        provenance,
                        remoteSuitability,
                        terrainBurden,
                        readiness,
                        districtContext,
                        debugReason));
                }
            }
        }

        private static void AddTownPlatParcels(
            List<RegionalSurveyParcelRecord> parcels,
            RegionalWorldGenerationSettings settings,
            IReadOnlyList<RegionalWatercourseRecord> watercourses,
            RegionalAnchorTownRecord anchor)
        {
            int id = 1;
            int coreColumns = 4;
            int coreRows = 3;
            float startX = anchor.CenterMeters.x - coreColumns * settings.townPlatLotWidthMeters * 0.5f;
            float startY = anchor.CenterMeters.y - coreRows * settings.townPlatLotDepthMeters * 0.5f;
            for (int row = 0; row < coreRows; row++)
            {
                for (int column = 0; column < coreColumns; column++)
                {
                    Rect bounds = new(
                        startX + column * settings.townPlatLotWidthMeters,
                        startY + row * settings.townPlatLotDepthMeters,
                        settings.townPlatLotWidthMeters,
                        settings.townPlatLotDepthMeters);
                    parcels.Add(CreateTownParcel($"TOWN-CORE-{id:00}", RegionalParcelKind.TownPlatCore, bounds, watercourses, anchor));
                    id++;
                }
            }

            float edgeWidth = settings.townPlatLotWidthMeters * 2f;
            float edgeDepth = settings.townPlatLotDepthMeters * 2f;
            Rect[] edgeParcels =
            {
                new(startX - edgeWidth, startY, edgeWidth, edgeDepth),
                new(startX + coreColumns * settings.townPlatLotWidthMeters, startY, edgeWidth, edgeDepth),
                new(startX, startY + coreRows * settings.townPlatLotDepthMeters, edgeWidth, edgeDepth),
                new(startX + edgeWidth, startY - edgeDepth, edgeWidth, edgeDepth)
            };
            for (int i = 0; i < edgeParcels.Length; i++)
            {
                RegionalParcelKind kind = i < 2 ? RegionalParcelKind.TownPlatEdge : RegionalParcelKind.EdgeExpansion;
                parcels.Add(CreateTownParcel($"TOWN-EDGE-{i + 1:00}", kind, edgeParcels[i], watercourses, anchor));
            }
        }

        private static RegionalSurveyParcelRecord CreateTownParcel(
            string id,
            RegionalParcelKind kind,
            Rect bounds,
            IReadOnlyList<RegionalWatercourseRecord> watercourses,
            RegionalAnchorTownRecord anchor)
        {
            Vector2 center = bounds.center;
            float water = EvaluateWaterAccess01(center, watercourses, 900f);
            float support = EvaluateSupportBurden01(center, anchor.CenterMeters, water);
            float distanceToAnchor = Vector2.Distance(center, anchor.CenterMeters);
            RegionalParcelFrontageClass frontage = kind == RegionalParcelKind.TownPlatCore
                ? RegionalParcelFrontageClass.TownMainStreet
                : RegionalParcelFrontageClass.TrailOrRoad;
            RegionalParcelAccessQuality access = kind == RegionalParcelKind.TownPlatCore
                ? RegionalParcelAccessQuality.Gateway
                : RegionalParcelAccessQuality.RoadFrontage;
            RegionalParcelProvenance provenance = kind == RegionalParcelKind.TownPlatCore
                ? RegionalParcelProvenance.TownsitePlat
                : RegionalParcelProvenance.FounderOrSpeculatorReserve;
            RegionalRemoteSuitability remoteSuitability = distanceToAnchor > 260f ? RegionalRemoteSuitability.FreightOutpost : RegionalRemoteSuitability.None;
            float terrainBurden = ResolveTerrainBurden(0.08f, Mathf.Clamp01(water * 0.45f), support);
            float readiness = ResolveDevelopmentReadiness(Mathf.Clamp01(0.76f - support * 0.18f), access, terrainBurden, support, kind);
            string districtContext = kind == RegionalParcelKind.TownPlatCore
                ? "opening townsite plat: prime early frontage and service concentration"
                : "founder/speculator edge reserve: expansion parcel tied to the opening foothold";
            string debugReason = BuildParcelDebugReason(frontage, access, provenance, remoteSuitability, readiness, terrainBurden);
            return new RegionalSurveyParcelRecord(
                id,
                "TOWN-PLAT",
                id,
                kind,
                bounds,
                RegionalWorldState.CalculateAcreage(bounds),
                0.08f,
                Mathf.Clamp01(water * 0.45f),
                water,
                Mathf.Clamp01(0.44f + water * 0.22f),
                Mathf.Clamp01(0.76f - support * 0.18f),
                0.12f,
                support,
                frontage,
                access,
                provenance,
                remoteSuitability,
                terrainBurden,
                readiness,
                districtContext,
                debugReason);
        }

        private static List<RegionalVegetationZoneRecord> GenerateVegetationZones(
            RegionalTerrainRecipeFamily recipe,
            IReadOnlyList<RegionalTerrainTileRecord> tiles,
            IReadOnlyList<RegionalWatercourseRecord> watercourses)
        {
            List<RegionalVegetationZoneRecord> zones = new();
            for (int i = 0; i < tiles.Count; i++)
            {
                RegionalTerrainTileRecord tile = tiles[i];
                ResolveBiome(recipe, tile.Wetness01, tile.Ruggedness01, out RegionalBiomeKind biome, out RegionalVegetationKind vegetation);
                zones.Add(new RegionalVegetationZoneRecord(
                    $"veg_{tile.TileId}",
                    biome,
                    vegetation,
                    tile.BoundsMeters,
                    Mathf.Clamp01(0.18f + tile.Wetness01 * 0.46f + tile.Ruggedness01 * 0.16f + tile.SettlementInfluence01 * 0.04f),
                    tile.Wetness01,
                    tile.Ruggedness01,
                    $"Tile-scale deterministic vegetation mask. {tile.BuildReadinessSummary()}"));
            }

            for (int i = 0; i < watercourses.Count; i++)
            {
                RegionalWatercourseRecord water = watercourses[i];
                Rect bounds = BoundsForPoints(water.Points, water.FloodplainWidthMeters);
                zones.Add(new RegionalVegetationZoneRecord(
                    $"veg_{water.WatercourseId}",
                    water.Kind == RegionalWatercourseKind.DrainageLine ? RegionalBiomeKind.WetLowland : RegionalBiomeKind.CreekBottom,
                    RegionalVegetationKind.RiparianTrees,
                    bounds,
                    Mathf.Clamp01(0.58f + water.FlowStrength01 * 0.22f),
                    1f,
                    0.08f,
                    "Water-adjacent tree and wet-ground influence zone."));
            }

            return zones;
        }

        private static List<RegionalSettlementClusterRecord> GenerateSettlementClusters(
            int seed,
            RegionalWorldGenerationSettings settings,
            RegionalAnchorTownRecord anchor,
            IReadOnlyList<RegionalSurveyParcelRecord> parcels)
        {
            List<RegionalSettlementClusterRecord> clusters = new();
            int households = Mathf.Max(1, settings.initialHouseholdCount);
            int businesses = Mathf.Max(1, settings.initialBusinessCount);
            int population = households * 3 + Mathf.Max(2, households / 3);
            float anchorServiceGravity = Mathf.Clamp01(0.42f + businesses * 0.055f);
            float anchorFreight = Mathf.Clamp01(0.38f + anchor.GatewayScore01 * 0.38f);
            float anchorPermanence = Mathf.Clamp01(0.38f + households / 40f + businesses / 16f + anchor.BuildableLand01 * 0.18f);
            float anchorConfidence = Mathf.Clamp01(anchor.SuitabilityScore01 * 0.46f + anchorServiceGravity * 0.30f + anchorFreight * 0.24f);
            float anchorServiceDeficit = ResolveServiceDeficit01(population, businesses, anchorServiceGravity);
            float anchorDecline = ResolveDeclineRisk01(anchorPermanence, anchorConfidence, anchorServiceDeficit, 0.12f);
            clusters.Add(new RegionalSettlementClusterRecord(
                "cluster_anchor_households_businesses",
                anchor.CenterMeters,
                households,
                businesses,
                population,
                anchorServiceGravity,
                anchorFreight,
                0.12f,
                RegionalSettlementCharacter.OpeningFootholdGateway,
                anchorPermanence,
                anchorConfidence,
                anchorServiceDeficit,
                anchorDecline,
                $"Opening foothold anchored by {households} household(s), {businesses} business(es), gateway {anchor.GatewayScore01:P0}, buildable shelf {anchor.BuildableLand01:P0}."));

            RegionalSurveyParcelRecord remote = FindRemoteClusterParcel(parcels, anchor.CenterMeters);
            if (remote != null)
            {
                int remoteHouseholds = 3 + PositiveHash(seed * 13 + 5) % 4;
                int remoteBusinesses = remote.ResourceSuitability01 > 0.58f ? 2 : 1;
                int remotePopulation = remoteHouseholds * 3;
                RegionalSettlementCharacter character = ResolveSettlementCharacter(remote, anchor.CenterMeters);
                float remoteServiceGravity = Mathf.Clamp01(0.12f + remoteBusinesses * 0.16f + remote.DevelopmentReadiness01 * 0.12f);
                float remoteFreight = Mathf.Clamp01(0.18f + remote.WaterAccess01 * 0.22f + ResolveAccessScore01(remote.AccessQuality) * 0.24f);
                float permanence = Mathf.Clamp01(remote.DevelopmentReadiness01 * 0.34f + (1f - remote.SupportBurden01) * 0.28f + remote.WaterAccess01 * 0.16f + remoteBusinesses * 0.08f + remoteHouseholds * 0.03f);
                float confidence = Mathf.Clamp01(remote.ResourceSuitability01 * 0.28f + remote.BuildSuitability01 * 0.24f + remoteFreight * 0.22f + permanence * 0.26f);
                float serviceDeficit = ResolveServiceDeficit01(remotePopulation, remoteBusinesses, remoteServiceGravity);
                float decline = ResolveDeclineRisk01(permanence, confidence, serviceDeficit, remote.SupportBurden01);
                clusters.Add(new RegionalSettlementClusterRecord(
                    "cluster_remote_working_families",
                    remote.CenterMeters,
                    remoteHouseholds,
                    remoteBusinesses,
                    remotePopulation,
                    remoteServiceGravity,
                    remoteFreight,
                    remote.SupportBurden01,
                    character,
                    permanence,
                    confidence,
                    serviceDeficit,
                    decline,
                    $"Remote cluster from parcel {remote.ParcelId}; {remote.RemoteSuitability}; readiness {remote.DevelopmentReadiness01:P0}; access {remote.AccessQuality}; burden {remote.SupportBurden01:P0}."));
            }

            RegionalSurveyParcelRecord freight = FindFreightSupportParcel(parcels, anchor.CenterMeters, remote != null ? remote.ParcelId : string.Empty);
            if (freight != null)
            {
                int freightHouseholds = 2 + PositiveHash(seed * 19 + 11) % 3;
                int freightBusinesses = (int)freight.AccessQuality >= (int)RegionalParcelAccessQuality.WagonReachable ? 1 : 0;
                int freightPopulation = freightHouseholds * 3 - 1;
                float freightServiceGravity = Mathf.Clamp01(0.08f + freightBusinesses * 0.16f + ResolveAccessScore01(freight.AccessQuality) * 0.10f);
                float freightSupport = Mathf.Clamp01(0.24f + ResolveAccessScore01(freight.AccessQuality) * 0.32f + freight.WaterAccess01 * 0.16f);
                float permanence = Mathf.Clamp01(freight.DevelopmentReadiness01 * 0.30f + freightSupport * 0.26f + (1f - freight.SupportBurden01) * 0.18f + freightHouseholds * 0.04f);
                float confidence = Mathf.Clamp01(permanence * 0.44f + freightSupport * 0.32f + freight.ResourceSuitability01 * 0.12f + freight.WaterAccess01 * 0.12f);
                float serviceDeficit = ResolveServiceDeficit01(freightPopulation, freightBusinesses, freightServiceGravity);
                float decline = ResolveDeclineRisk01(permanence, confidence, serviceDeficit, freight.SupportBurden01);
                clusters.Add(new RegionalSettlementClusterRecord(
                    "cluster_freight_service_pressure",
                    freight.CenterMeters,
                    freightHouseholds,
                    freightBusinesses,
                    Mathf.Max(5, freightPopulation),
                    freightServiceGravity,
                    freightSupport,
                    freight.SupportBurden01,
                    RegionalSettlementCharacter.FreightCrossing,
                    permanence,
                    confidence,
                    serviceDeficit,
                    decline,
                    $"Freight/service pressure from parcel {freight.ParcelId}; access {freight.AccessQuality}; support burden {freight.SupportBurden01:P0}."));
            }

            return clusters;
        }

        private static List<RegionalSettlementRecord> DeriveSettlements(int seed, IReadOnlyList<RegionalSettlementClusterRecord> clusters)
        {
            List<RegionalSettlementRecord> settlements = new();
            if (clusters == null)
            {
                return settlements;
            }

            for (int i = 0; i < clusters.Count; i++)
            {
                RegionalSettlementClusterRecord cluster = clusters[i];
                if (cluster == null || cluster.Population < 5 || cluster.HouseholdCount <= 0)
                {
                    continue;
                }

                ResolveBand(cluster.Population, out RegionalPopulationBand band, out int bandMin, out int bandMax);
                RegionalSettlementLabel label = ResolveLabel(band, cluster.BusinessCount, cluster.ServiceGravity01, cluster.Permanence01);
                int windowSpan = Mathf.Max(4, (bandMax - bandMin + 1) / 4);
                int maxStart = Mathf.Max(bandMin, bandMax - windowSpan);
                int offsetRange = Mathf.Max(1, maxStart - bandMin + 1);
                int windowMin = bandMin + PositiveHash(seed * 101 + i * 43 + Mathf.RoundToInt(cluster.LocalConfidence01 * 100f)) % offsetRange;
                int windowMax = Mathf.Min(bandMax, windowMin + windowSpan);
                if (windowMin == cluster.Population)
                {
                    windowMin = Mathf.Max(bandMin, windowMin - 1);
                }

                if (windowMax == cluster.Population)
                {
                    windowMax = Mathf.Min(bandMax, windowMax + 1);
                }

                RegionalSettlementPermanence permanence = ResolvePermanence(cluster.Permanence01, cluster.LocalConfidence01, cluster.DeclineRisk01, cluster.Population);
                string debugSummary = $"Derived from {cluster.HouseholdCount} household(s), {cluster.BusinessCount} business(es), character {cluster.Character}, permanence {permanence}, service deficit {cluster.ServiceDeficit01:P0}, decline risk {cluster.DeclineRisk01:P0}. {cluster.DebugReason}";
                settlements.Add(new RegionalSettlementRecord(
                    $"settlement_{i + 1:00}",
                    cluster.ClusterId,
                    cluster.CenterMeters,
                    cluster.HouseholdCount,
                    cluster.BusinessCount,
                    cluster.Population,
                    band,
                    bandMin,
                    bandMax,
                    windowMin,
                    windowMax,
                    label,
                    cluster.ServiceGravity01,
                    cluster.FreightSupport01,
                    cluster.SupportBurden01,
                    cluster.Character,
                    permanence,
                    cluster.Permanence01,
                    cluster.LocalConfidence01,
                    cluster.ServiceDeficit01,
                    cluster.DeclineRisk01,
                    debugSummary));
            }

            settlements.Sort((left, right) => right.Population.CompareTo(left.Population));
            return ApplySettlementHierarchyMetadata(settlements);
        }

        internal static bool NeedsSettlementHierarchyRebuild(IReadOnlyList<RegionalSettlementRecord> settlements)
        {
            if (settlements == null || settlements.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < settlements.Count; i++)
            {
                RegionalSettlementRecord settlement = settlements[i];
                if (settlement != null && (settlement.SuccessionRole == RegionalSettlementSuccessionRole.Unset || settlement.RegionalGravity01 <= 0f || settlement.RegionalHierarchyRank <= 0 || string.IsNullOrWhiteSpace(settlement.SuccessionContext)))
                {
                    return true;
                }
            }

            return false;
        }

        internal static List<RegionalSettlementRecord> ApplySettlementHierarchyMetadata(IReadOnlyList<RegionalSettlementRecord> settlements)
        {
            List<RegionalSettlementRecord> enriched = new();
            if (settlements == null || settlements.Count == 0)
            {
                return enriched;
            }

            RegionalSettlementRecord openingFoothold = FindOpeningFootholdSettlement(settlements, Vector2.zero);
            float footholdGravity = Mathf.Max(0.12f, CalculateSettlementRegionalGravity01(openingFoothold, true));

            List<(RegionalSettlementRecord settlement, float gravity, float challenge, float readiness, RegionalSettlementSuccessionRole role, string context)> ranked = new();
            for (int i = 0; i < settlements.Count; i++)
            {
                RegionalSettlementRecord settlement = settlements[i];
                if (settlement == null)
                {
                    continue;
                }

                bool isOpeningFoothold = settlement.Character == RegionalSettlementCharacter.OpeningFootholdGateway || ReferenceEquals(settlement, openingFoothold);
                float gravity = CalculateSettlementRegionalGravity01(settlement, isOpeningFoothold);
                float challenge = isOpeningFoothold ? 0f : Mathf.Clamp01(gravity / Mathf.Max(0.12f, footholdGravity));
                float readiness = CalculateSuccessionReadiness01(settlement, gravity, challenge, isOpeningFoothold);
                RegionalSettlementSuccessionRole role = ResolveSuccessionRole(settlement, challenge, readiness, isOpeningFoothold);
                string context = BuildSuccessionContext(settlement, gravity, challenge, readiness, role, isOpeningFoothold);
                ranked.Add((settlement, gravity, challenge, readiness, role, context));
            }

            ranked.Sort((left, right) => right.gravity.CompareTo(left.gravity));
            Dictionary<string, int> rankBySettlementId = new();
            for (int i = 0; i < ranked.Count; i++)
            {
                string key = ranked[i].settlement.SettlementId ?? string.Empty;
                if (!rankBySettlementId.ContainsKey(key))
                {
                    rankBySettlementId[key] = i + 1;
                }
            }

            for (int i = 0; i < settlements.Count; i++)
            {
                RegionalSettlementRecord settlement = settlements[i];
                if (settlement == null)
                {
                    continue;
                }

                (RegionalSettlementRecord settlement, float gravity, float challenge, float readiness, RegionalSettlementSuccessionRole role, string context) entry = ranked.Find(r => ReferenceEquals(r.settlement, settlement));
                int rank = rankBySettlementId.TryGetValue(settlement.SettlementId ?? string.Empty, out int foundRank) ? foundRank : i + 1;
                enriched.Add(new RegionalSettlementRecord(
                    settlement.SettlementId,
                    settlement.SourceClusterId,
                    settlement.CenterMeters,
                    settlement.HouseholdCount,
                    settlement.BusinessCount,
                    settlement.Population,
                    settlement.PopulationBand,
                    settlement.PopulationBandMin,
                    settlement.PopulationBandMax,
                    settlement.NextTriggerWindowMinPopulation,
                    settlement.NextTriggerWindowMaxPopulation,
                    settlement.Label,
                    settlement.ServiceGravity01,
                    settlement.FreightSupport01,
                    settlement.SupportBurden01,
                    settlement.Character,
                    settlement.Permanence,
                    settlement.Permanence01,
                    settlement.LocalConfidence01,
                    settlement.ServiceDeficit01,
                    settlement.DeclineRisk01,
                    settlement.DebugSummary,
                    entry.role,
                    rank,
                    entry.gravity,
                    entry.challenge,
                    entry.readiness,
                    entry.context));
            }

            return enriched;
        }

        private static float CalculateSettlementRegionalGravity01(RegionalSettlementRecord settlement, bool openingFoothold)
        {
            if (settlement == null)
            {
                return 0f;
            }

            float populationWeight = Mathf.Clamp01(settlement.Population / 220f);
            float serviceWeight = settlement.ServiceGravity01;
            float freightWeight = settlement.FreightSupport01;
            float permanenceWeight = settlement.Permanence01;
            float confidenceWeight = settlement.LocalConfidence01;
            float supportRelief = 1f - settlement.SupportBurden01;
            float stabilityWeight = 1f - settlement.DeclineRisk01;
            float labelWeight = settlement.Label switch
            {
                RegionalSettlementLabel.LargeTown => 1f,
                RegionalSettlementLabel.Town => 0.82f,
                RegionalSettlementLabel.Hamlet => 0.56f,
                RegionalSettlementLabel.Camp => 0.32f,
                _ => 0.18f
            };
            float characterWeight = settlement.Character switch
            {
                RegionalSettlementCharacter.FreightCrossing => 0.72f,
                RegionalSettlementCharacter.MixedOpportunityNode => 0.66f,
                RegionalSettlementCharacter.MineralOrRoughCountryCamp => 0.58f,
                RegionalSettlementCharacter.FarmServicePocket => 0.54f,
                RegionalSettlementCharacter.TimberWorksite => 0.48f,
                RegionalSettlementCharacter.OpeningFootholdGateway => 0.70f,
                _ => 0.25f
            };

            float gravity =
                populationWeight * 0.22f +
                serviceWeight * 0.18f +
                freightWeight * 0.17f +
                permanenceWeight * 0.15f +
                confidenceWeight * 0.11f +
                supportRelief * 0.06f +
                stabilityWeight * 0.05f +
                labelWeight * 0.04f +
                characterWeight * 0.02f;

            if (openingFoothold)
            {
                gravity = Mathf.Clamp01(gravity + 0.08f);
            }

            return Mathf.Clamp01(gravity);
        }

        private static float CalculateSuccessionReadiness01(RegionalSettlementRecord settlement, float gravity, float challenge, bool openingFoothold)
        {
            if (settlement == null)
            {
                return 0f;
            }

            if (openingFoothold)
            {
                return Mathf.Clamp01(gravity);
            }

            float populationReadiness = Mathf.Clamp01((settlement.Population - 18f) / 120f);
            float serviceReadiness = Mathf.Clamp01(settlement.ServiceGravity01 * 0.55f + settlement.FreightSupport01 * 0.45f);
            float permanenceReadiness = Mathf.Clamp01(settlement.Permanence01 * 0.72f + settlement.LocalConfidence01 * 0.28f);
            float burdenPenalty = settlement.SupportBurden01 * 0.18f + settlement.DeclineRisk01 * 0.22f + settlement.ServiceDeficit01 * 0.12f;
            return Mathf.Clamp01(challenge * 0.34f + gravity * 0.24f + populationReadiness * 0.14f + serviceReadiness * 0.14f + permanenceReadiness * 0.14f - burdenPenalty);
        }

        private static RegionalSettlementSuccessionRole ResolveSuccessionRole(RegionalSettlementRecord settlement, float challenge, float readiness, bool openingFoothold)
        {
            if (settlement == null)
            {
                return RegionalSettlementSuccessionRole.Unset;
            }

            if (openingFoothold)
            {
                return RegionalSettlementSuccessionRole.OpeningFoothold;
            }

            bool fragile = settlement.DeclineRisk01 >= 0.68f || (settlement.Permanence <= RegionalSettlementPermanence.ThinButServiceable && settlement.SupportBurden01 >= 0.62f);
            if (fragile)
            {
                return RegionalSettlementSuccessionRole.StalledOrDecliningNode;
            }

            if (readiness >= 0.66f && challenge >= 0.78f && settlement.Population >= 50)
            {
                return RegionalSettlementSuccessionRole.LikelyRegionalSuccessor;
            }

            if (readiness >= 0.48f && challenge >= 0.58f)
            {
                return RegionalSettlementSuccessionRole.PeerChallenger;
            }

            if (readiness >= 0.30f || settlement.Permanence >= RegionalSettlementPermanence.EmergingNode)
            {
                return RegionalSettlementSuccessionRole.RisingSecondaryNode;
            }

            return RegionalSettlementSuccessionRole.DependentSatellite;
        }

        private static string BuildSuccessionContext(RegionalSettlementRecord settlement, float gravity, float challenge, float readiness, RegionalSettlementSuccessionRole role, bool openingFoothold)
        {
            if (settlement == null)
            {
                return string.Empty;
            }

            if (openingFoothold)
            {
                return $"Opening foothold gravity {gravity:P0}; it begins ahead but can be challenged by stronger regional service, freight, and household concentration elsewhere.";
            }

            string cause = role switch
            {
                RegionalSettlementSuccessionRole.LikelyRegionalSuccessor => "local population, service gravity, and permanence are strong enough to pressure the opening town",
                RegionalSettlementSuccessionRole.PeerChallenger => "regional gravity is approaching the foothold but still needs durable support",
                RegionalSettlementSuccessionRole.RisingSecondaryNode => "activity is becoming more durable but has not yet become a true rival",
                RegionalSettlementSuccessionRole.StalledOrDecliningNode => "support burden or decline risk makes this a false-start risk",
                RegionalSettlementSuccessionRole.DependentSatellite => "the node still reads as dependent on stronger regional centers",
                _ => "succession role is not yet clear"
            };

            return $"{cause}; challenge {challenge:P0}, readiness {readiness:P0}, gravity {gravity:P0}.";
        }

        internal static bool NeedsRouteConstraintRebuild(IReadOnlyList<RegionalRouteCorridorRecord> routes)
        {
            if (routes == null || routes.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < routes.Count; i++)
            {
                RegionalRouteCorridorRecord route = routes[i];
                if (route == null)
                {
                    continue;
                }

                if (route.PrimaryConstraintKind == RegionalRouteConstraintKind.None || string.IsNullOrWhiteSpace(route.ConstraintSummary))
                {
                    return true;
                }
            }

            return false;
        }

        internal static List<RegionalRouteCorridorRecord> ApplyRouteConstraintMetadata(
            IReadOnlyList<RegionalRouteCorridorRecord> routes,
            IReadOnlyList<RegionalWatercourseRecord> watercourses,
            IReadOnlyList<RegionalSurveyParcelRecord> parcels)
        {
            List<RegionalRouteCorridorRecord> enriched = new();
            if (routes == null)
            {
                return enriched;
            }

            for (int i = 0; i < routes.Count; i++)
            {
                RegionalRouteCorridorRecord route = routes[i];
                if (route == null)
                {
                    continue;
                }

                RegionalSurveyParcelRecord destinationParcel = FindParcelById(parcels, route.DestinationParcelId);
                RouteConstraintAssessment assessment = EvaluateRouteConstraints(route.Points, watercourses, destinationParcel, route.Kind);
                float practicality = Mathf.Clamp01(route.Practicality01 - assessment.BridgeOrFordNeed01 * 0.08f - assessment.GradeBurden01 * 0.06f);
                float seasonalReliability = Mathf.Clamp01(route.SeasonalReliability01 - assessment.MudSeasonRisk01 * 0.18f - assessment.WetGroundCrossingCount * 0.025f);
                float freightBurden = Mathf.Clamp01(route.FreightBurden01 + assessment.MudSeasonRisk01 * 0.12f + assessment.BridgeOrFordNeed01 * 0.10f + assessment.GradeBurden01 * 0.06f);
                string debug = string.IsNullOrWhiteSpace(route.DebugSummary)
                    ? assessment.Summary
                    : $"{route.DebugSummary} Constraint: {assessment.Summary}";

                enriched.Add(new RegionalRouteCorridorRecord(
                    route.CorridorId,
                    route.Kind,
                    route.SourceSettlementId,
                    route.DestinationSettlementId,
                    route.SourceParcelId,
                    route.DestinationParcelId,
                    route.LengthMeters,
                    practicality,
                    seasonalReliability,
                    freightBurden,
                    debug,
                    route.Points,
                    assessment.PrimaryConstraintKind,
                    assessment.WaterCrossingCount,
                    assessment.WetGroundCrossingCount,
                    assessment.BridgeOrFordNeed01,
                    assessment.MudSeasonRisk01,
                    assessment.GradeBurden01,
                    assessment.Summary));
            }

            return enriched;
        }

        internal static bool NeedsTileRouteReadinessRebuild(IReadOnlyList<RegionalTerrainTileRecord> tiles, IReadOnlyList<RegionalRouteCorridorRecord> routes)
        {
            if (tiles == null || tiles.Count == 0 || routes == null || routes.Count == 0)
            {
                return false;
            }

            bool anyRouteAwareTile = false;
            for (int i = 0; i < tiles.Count; i++)
            {
                RegionalTerrainTileRecord tile = tiles[i];
                if (tile == null)
                {
                    continue;
                }

                bool hasRouteMetadata = tile.RouteInfluence01 > 0f || tile.RouteActivationReadiness01 > 0f || !string.IsNullOrWhiteSpace(tile.NearestRouteCorridorId);
                anyRouteAwareTile |= hasRouteMetadata;

                // Older or partially migrated saves can contain a mix of route-aware and untouched tiles.
                // Rebuild if an otherwise useful tile has not received even fallback route-readiness metadata.
                if (!hasRouteMetadata && tile.ActivationReadiness01 > 0.05f)
                {
                    return true;
                }
            }

            return !anyRouteAwareTile;
        }

        internal static List<RegionalTerrainTileRecord> ApplyRouteCorridorTileReadiness(
            IReadOnlyList<RegionalTerrainTileRecord> tiles,
            IReadOnlyList<RegionalRouteCorridorRecord> routes,
            IReadOnlyList<RegionalSettlementRecord> settlements)
        {
            List<RegionalTerrainTileRecord> enriched = new();
            if (tiles == null)
            {
                return enriched;
            }

            for (int i = 0; i < tiles.Count; i++)
            {
                RegionalTerrainTileRecord tile = tiles[i];
                if (tile == null)
                {
                    continue;
                }

                RegionalRouteCorridorRecord nearestRoute = null;
                float bestInfluence = 0f;
                float bestDistance = float.MaxValue;
                float freightBurdenInfluence = 0f;
                if (routes != null)
                {
                    for (int routeIndex = 0; routeIndex < routes.Count; routeIndex++)
                    {
                        RegionalRouteCorridorRecord route = routes[routeIndex];
                        if (route == null || route.Points.Count < 2)
                        {
                            continue;
                        }

                        float distance = DistanceToPolyline(tile.BoundsMeters.center, route.Points);
                        float influenceRadius = ResolveTileRouteInfluenceRadiusMeters(tile, route.Kind);
                        float influence = Mathf.Clamp01(1f - distance / Mathf.Max(1f, influenceRadius));
                        if (influence > bestInfluence || (Mathf.Approximately(influence, bestInfluence) && distance < bestDistance))
                        {
                            nearestRoute = route;
                            bestInfluence = influence;
                            bestDistance = distance;
                            freightBurdenInfluence = Mathf.Clamp01(influence * route.FreightBurden01);
                        }
                    }
                }

                float settlementSupport = ResolveTileSettlementSupport01(tile, settlements);
                float routeReadiness = tile.ActivationReadiness01;
                string nearestRouteId = string.Empty;
                RegionalRouteCorridorKind nearestRouteKind = RegionalRouteCorridorKind.SettlementConnector;
                if (nearestRoute != null && bestInfluence > 0f)
                {
                    nearestRouteId = nearestRoute.CorridorId;
                    nearestRouteKind = nearestRoute.Kind;
                    routeReadiness = Mathf.Clamp01(
                        tile.ActivationReadiness01 * 0.42f
                        + bestInfluence * 0.32f
                        + nearestRoute.Practicality01 * 0.10f
                        + nearestRoute.SeasonalReliability01 * 0.08f
                        + settlementSupport * 0.08f);
                }
                else
                {
                    routeReadiness = Mathf.Clamp01(tile.ActivationReadiness01 * 0.72f + settlementSupport * 0.12f);
                }

                string routeDebug = nearestRoute != null && bestInfluence > 0f
                    ? $" Route readiness {routeReadiness:P0}; route influence {bestInfluence:P0} from {nearestRoute.Kind} {nearestRoute.CorridorId}; tile freight burden {freightBurdenInfluence:P0}."
                    : $" Route readiness {routeReadiness:P0}; no corridor within tile activation radius.";

                enriched.Add(new RegionalTerrainTileRecord(
                    tile.TileId,
                    tile.TileX,
                    tile.TileZ,
                    tile.BoundsMeters,
                    tile.MeanElevationMeters,
                    tile.Ruggedness01,
                    tile.Wetness01,
                    tile.Active,
                    tile.ActivationReadiness01,
                    tile.SettlementInfluence01,
                    tile.DominantBiomeKind,
                    string.IsNullOrWhiteSpace(tile.DebugSummary) ? routeDebug.Trim() : $"{tile.DebugSummary}{routeDebug}",
                    bestInfluence,
                    routeReadiness,
                    freightBurdenInfluence,
                    nearestRouteId,
                    nearestRouteKind));
            }

            return enriched;
        }

        private static float ResolveTileRouteInfluenceRadiusMeters(RegionalTerrainTileRecord tile, RegionalRouteCorridorKind kind)
        {
            float tileReach = tile != null ? Mathf.Max(tile.BoundsMeters.width, tile.BoundsMeters.height) * 0.58f : 0f;
            return ResolveCorridorInfluenceRadiusMeters(kind) + tileReach;
        }

        private static float ResolveTileSettlementSupport01(RegionalTerrainTileRecord tile, IReadOnlyList<RegionalSettlementRecord> settlements)
        {
            if (tile == null || settlements == null || settlements.Count == 0)
            {
                return 0f;
            }

            float support = 0f;
            Vector2 center = tile.BoundsMeters.center;
            for (int i = 0; i < settlements.Count; i++)
            {
                RegionalSettlementRecord settlement = settlements[i];
                if (settlement == null)
                {
                    continue;
                }

                float distanceInfluence = Mathf.Clamp01(1f - Vector2.Distance(center, settlement.CenterMeters) / 1800f);
                support = Mathf.Max(support, distanceInfluence * Mathf.Lerp(0.30f, 1f, settlement.Permanence01));
            }

            return Mathf.Clamp01(support);
        }

        internal static bool NeedsParcelCorridorInfluenceRebuild(IReadOnlyList<RegionalSurveyParcelRecord> parcels, IReadOnlyList<RegionalRouteCorridorRecord> routes)
        {
            if (parcels == null || parcels.Count == 0 || routes == null || routes.Count == 0)
            {
                return false;
            }

            bool anyCorridorRelation = false;
            for (int i = 0; i < parcels.Count; i++)
            {
                RegionalSurveyParcelRecord parcel = parcels[i];
                if (parcel == null)
                {
                    continue;
                }

                if (parcel.CorridorRelation == RegionalParcelCorridorRelation.None || string.IsNullOrWhiteSpace(parcel.CorridorContext))
                {
                    return true;
                }

                anyCorridorRelation = true;
                if (ParcelNeedsCorridorConstraintRefresh(parcel, routes))
                {
                    return true;
                }
            }

            return !anyCorridorRelation;
        }

        internal static List<RegionalSurveyParcelRecord> ApplyRouteCorridorInfluence(
            IReadOnlyList<RegionalSurveyParcelRecord> parcels,
            IReadOnlyList<RegionalRouteCorridorRecord> routes)
        {
            List<RegionalSurveyParcelRecord> enriched = new();
            if (parcels == null)
            {
                return enriched;
            }

            for (int i = 0; i < parcels.Count; i++)
            {
                RegionalSurveyParcelRecord parcel = parcels[i];
                if (parcel == null)
                {
                    continue;
                }

                RegionalRouteCorridorRecord nearestRoute = null;
                float nearestDistance = float.MaxValue;
                if (routes != null)
                {
                    for (int routeIndex = 0; routeIndex < routes.Count; routeIndex++)
                    {
                        RegionalRouteCorridorRecord route = routes[routeIndex];
                        if (route == null || route.Points.Count < 2)
                        {
                            continue;
                        }

                        float distance = DistanceToPolyline(parcel.CenterMeters, route.Points);
                        if (distance < nearestDistance)
                        {
                            nearestRoute = route;
                            nearestDistance = distance;
                        }
                    }
                }

                RegionalParcelCorridorRelation relation = RegionalParcelCorridorRelation.None;
                string nearestId = string.Empty;
                RegionalRouteCorridorKind nearestKind = RegionalRouteCorridorKind.SettlementConnector;
                float corridorInfluence = 0f;
                float freightAccess = 0f;
                float corridorBurden = 0f;
                string corridorContext = "No generated route corridor currently influences this parcel.";

                if (nearestRoute != null && nearestDistance < float.MaxValue)
                {
                    nearestId = nearestRoute.CorridorId;
                    nearestKind = nearestRoute.Kind;
                    float radius = ResolveCorridorInfluenceRadiusMeters(nearestRoute.Kind);
                    corridorInfluence = Mathf.Clamp01(1f - nearestDistance / Mathf.Max(1f, radius));
                    relation = corridorInfluence > 0f ? ResolveParcelCorridorRelation(nearestRoute.Kind) : RegionalParcelCorridorRelation.OffRoute;
                    float constraintSeverity = ResolveRouteConstraintSeverity01(nearestRoute);
                    freightAccess = Mathf.Clamp01(corridorInfluence * 0.50f + nearestRoute.Practicality01 * 0.22f + nearestRoute.SeasonalReliability01 * 0.20f + (1f - constraintSeverity) * 0.08f);
                    freightAccess = Mathf.Clamp01(freightAccess - corridorInfluence * constraintSeverity * 0.18f);
                    corridorBurden = Mathf.Clamp01(corridorInfluence * nearestRoute.FreightBurden01 + parcel.SupportBurden01 * 0.16f + corridorInfluence * constraintSeverity * 0.22f);
                    string proximity = corridorInfluence >= 0.62f ? "strong" : corridorInfluence >= 0.30f ? "workable" : "thin";
                    string constraintContext = BuildParcelCorridorConstraintContext(nearestRoute);
                    corridorContext = $"{proximity} relation to {nearestRoute.Kind} {nearestRoute.CorridorId}; {nearestDistance:0}m from corridor; freight access {freightAccess:P0}; corridor burden {corridorBurden:P0}; {constraintContext}";
                }

                enriched.Add(new RegionalSurveyParcelRecord(
                    parcel.ParcelId,
                    parcel.SectionId,
                    parcel.TractId,
                    parcel.ParcelKind,
                    parcel.BoundsMeters,
                    parcel.Acreage,
                    parcel.MeanSlope01,
                    parcel.Wetness01,
                    parcel.WaterAccess01,
                    parcel.FarmSuitability01,
                    parcel.BuildSuitability01,
                    parcel.ResourceSuitability01,
                    parcel.SupportBurden01,
                    parcel.FrontageClass,
                    parcel.AccessQuality,
                    parcel.Provenance,
                    parcel.RemoteSuitability,
                    parcel.TerrainBurden01,
                    parcel.DevelopmentReadiness01,
                    parcel.DistrictContext,
                    parcel.DebugReason,
                    relation,
                    nearestId,
                    nearestKind,
                    nearestRoute != null ? nearestDistance : 0f,
                    corridorInfluence,
                    freightAccess,
                    corridorBurden,
                    corridorContext));
            }

            return enriched;
        }

        private static RegionalParcelCorridorRelation ResolveParcelCorridorRelation(RegionalRouteCorridorKind kind)
        {
            return kind switch
            {
                RegionalRouteCorridorKind.OpeningFootholdSpine => RegionalParcelCorridorRelation.NearOpeningSpine,
                RegionalRouteCorridorKind.FreightTrack => RegionalParcelCorridorRelation.NearFreightTrack,
                RegionalRouteCorridorKind.RemoteWorksiteTrack => RegionalParcelCorridorRelation.NearRemoteWorksiteTrack,
                RegionalRouteCorridorKind.CreekOrDrawTrack => RegionalParcelCorridorRelation.NearCreekOrDrawTrack,
                _ => RegionalParcelCorridorRelation.NearSettlementConnector
            };
        }

        private static float ResolveCorridorInfluenceRadiusMeters(RegionalRouteCorridorKind kind)
        {
            return kind switch
            {
                RegionalRouteCorridorKind.OpeningFootholdSpine => 620f,
                RegionalRouteCorridorKind.FreightTrack => 780f,
                RegionalRouteCorridorKind.RemoteWorksiteTrack => 720f,
                RegionalRouteCorridorKind.CreekOrDrawTrack => 640f,
                _ => 680f
            };
        }

        private static bool ParcelNeedsCorridorConstraintRefresh(RegionalSurveyParcelRecord parcel, IReadOnlyList<RegionalRouteCorridorRecord> routes)
        {
            if (parcel == null || string.IsNullOrWhiteSpace(parcel.NearestCorridorId))
            {
                return false;
            }

            RegionalRouteCorridorRecord route = FindRouteById(routes, parcel.NearestCorridorId);
            if (route == null || route.PrimaryConstraintKind == RegionalRouteConstraintKind.None)
            {
                return false;
            }

            string context = parcel.CorridorContext ?? string.Empty;
            return context.IndexOf("constraint", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static RegionalRouteCorridorRecord FindRouteById(IReadOnlyList<RegionalRouteCorridorRecord> routes, string corridorId)
        {
            if (routes == null || string.IsNullOrWhiteSpace(corridorId))
            {
                return null;
            }

            for (int i = 0; i < routes.Count; i++)
            {
                RegionalRouteCorridorRecord route = routes[i];
                if (route != null && route.CorridorId == corridorId)
                {
                    return route;
                }
            }

            return null;
        }

        private static float ResolveRouteConstraintSeverity01(RegionalRouteCorridorRecord route)
        {
            if (route == null)
            {
                return 0f;
            }

            float kindSeverity = route.PrimaryConstraintKind switch
            {
                RegionalRouteConstraintKind.BridgeLikely => 0.72f,
                RegionalRouteConstraintKind.MudSeasonRisk => 0.62f,
                RegionalRouteConstraintKind.FordOrLowCrossing => 0.56f,
                RegionalRouteConstraintKind.SteepGrade => 0.48f,
                RegionalRouteConstraintKind.WetGround => 0.42f,
                RegionalRouteConstraintKind.StableDryTrack => 0.08f,
                RegionalRouteConstraintKind.None => 0.25f,
                _ => 0.20f
            };

            float measuredSeverity = Mathf.Clamp01(route.BridgeOrFordNeed01 * 0.34f + route.MudSeasonRisk01 * 0.30f + route.GradeBurden01 * 0.22f + Mathf.Clamp01(route.WetGroundCrossingCount / 3f) * 0.14f);
            return Mathf.Clamp01(Mathf.Max(kindSeverity, measuredSeverity));
        }

        private static string BuildParcelCorridorConstraintContext(RegionalRouteCorridorRecord route)
        {
            if (route == null)
            {
                return "constraint context unavailable.";
            }

            if (route.PrimaryConstraintKind == RegionalRouteConstraintKind.None)
            {
                return "constraint assessment pending.";
            }

            List<string> parts = new();
            if (route.WaterCrossingCount > 0)
            {
                parts.Add($"{route.WaterCrossingCount} water crossing(s)");
            }

            if (route.WetGroundCrossingCount > 0)
            {
                parts.Add($"{route.WetGroundCrossingCount} wet-ground segment(s)");
            }

            if (route.BridgeOrFordNeed01 >= 0.35f)
            {
                parts.Add($"bridge/ford pressure {route.BridgeOrFordNeed01:P0}");
            }

            if (route.MudSeasonRisk01 >= 0.35f)
            {
                parts.Add($"mud-season risk {route.MudSeasonRisk01:P0}");
            }

            if (route.GradeBurden01 >= 0.35f)
            {
                parts.Add($"grade burden {route.GradeBurden01:P0}");
            }

            if (parts.Count == 0)
            {
                parts.Add("no major crossing pressure");
            }

            return $"constraint {route.PrimaryConstraintKind}: {string.Join(", ", parts)}.";
        }

        private sealed class RouteConstraintAssessment
        {
            public RegionalRouteConstraintKind PrimaryConstraintKind;
            public int WaterCrossingCount;
            public int WetGroundCrossingCount;
            public float BridgeOrFordNeed01;
            public float MudSeasonRisk01;
            public float GradeBurden01;
            public string Summary = string.Empty;
        }

        private static RouteConstraintAssessment EvaluateRouteConstraints(
            IReadOnlyList<Vector2> points,
            IReadOnlyList<RegionalWatercourseRecord> watercourses,
            RegionalSurveyParcelRecord destinationParcel,
            RegionalRouteCorridorKind kind)
        {
            RouteConstraintAssessment assessment = new();
            float strongestFlowCrossed = 0f;
            if (points != null && points.Count >= 2 && watercourses != null)
            {
                HashSet<string> crossedWaters = new();
                HashSet<string> wetWaters = new();
                for (int routeIndex = 1; routeIndex < points.Count; routeIndex++)
                {
                    Vector2 routeA = points[routeIndex - 1];
                    Vector2 routeB = points[routeIndex];
                    for (int waterIndex = 0; waterIndex < watercourses.Count; waterIndex++)
                    {
                        RegionalWatercourseRecord water = watercourses[waterIndex];
                        if (water == null || water.Points.Count < 2)
                        {
                            continue;
                        }

                        float bestDistance = DistanceToPolylineSegmentPair(routeA, routeB, water.Points);
                        float crossingReach = Mathf.Max(24f, water.InfluenceWidthMeters * 0.42f);
                        float wetReach = Mathf.Max(crossingReach, water.FloodplainWidthMeters * 0.50f);
                        if (bestDistance <= crossingReach)
                        {
                            crossedWaters.Add(water.WatercourseId);
                            strongestFlowCrossed = Mathf.Max(strongestFlowCrossed, water.FlowStrength01);
                        }
                        else if (bestDistance <= wetReach)
                        {
                            wetWaters.Add(water.WatercourseId);
                        }
                    }
                }

                assessment.WaterCrossingCount = crossedWaters.Count;
                assessment.WetGroundCrossingCount = wetWaters.Count;
            }

            float destinationWetness = destinationParcel != null ? destinationParcel.Wetness01 : 0f;
            float destinationTerrainBurden = destinationParcel != null ? destinationParcel.TerrainBurden01 : 0f;
            float wetGroundPressure = Mathf.Clamp01(destinationWetness * 0.42f + assessment.WetGroundCrossingCount * 0.18f + assessment.WaterCrossingCount * 0.10f);
            assessment.BridgeOrFordNeed01 = Mathf.Clamp01(assessment.WaterCrossingCount * 0.24f + strongestFlowCrossed * 0.38f + (kind == RegionalRouteCorridorKind.FreightTrack ? 0.08f : 0f));
            assessment.MudSeasonRisk01 = Mathf.Clamp01(wetGroundPressure + destinationWetness * 0.22f + (kind == RegionalRouteCorridorKind.CreekOrDrawTrack ? 0.10f : 0f));
            assessment.GradeBurden01 = Mathf.Clamp01(destinationTerrainBurden * 0.60f + (destinationParcel != null ? destinationParcel.MeanSlope01 * 0.34f : 0f));
            assessment.PrimaryConstraintKind = ResolvePrimaryRouteConstraint(assessment);
            assessment.Summary = BuildRouteConstraintSummary(assessment);
            return assessment;
        }

        private static RegionalRouteConstraintKind ResolvePrimaryRouteConstraint(RouteConstraintAssessment assessment)
        {
            if (assessment == null)
            {
                return RegionalRouteConstraintKind.None;
            }

            if (assessment.BridgeOrFordNeed01 >= 0.62f || assessment.WaterCrossingCount >= 2)
            {
                return RegionalRouteConstraintKind.BridgeLikely;
            }

            if (assessment.WaterCrossingCount > 0)
            {
                return RegionalRouteConstraintKind.FordOrLowCrossing;
            }

            if (assessment.MudSeasonRisk01 >= 0.58f)
            {
                return RegionalRouteConstraintKind.MudSeasonRisk;
            }

            if (assessment.GradeBurden01 >= 0.58f)
            {
                return RegionalRouteConstraintKind.SteepGrade;
            }

            if (assessment.WetGroundCrossingCount > 0 || assessment.MudSeasonRisk01 >= 0.34f)
            {
                return RegionalRouteConstraintKind.WetGround;
            }

            return RegionalRouteConstraintKind.StableDryTrack;
        }

        private static string BuildRouteConstraintSummary(RouteConstraintAssessment assessment)
        {
            if (assessment == null)
            {
                return "Route constraint assessment unavailable.";
            }

            return $"{assessment.PrimaryConstraintKind}; crossings {assessment.WaterCrossingCount}; wet stretches {assessment.WetGroundCrossingCount}; bridge/ford need {assessment.BridgeOrFordNeed01:P0}; mud risk {assessment.MudSeasonRisk01:P0}; grade burden {assessment.GradeBurden01:P0}.";
        }

        private static float DistanceToPolylineSegmentPair(Vector2 a, Vector2 b, IReadOnlyList<Vector2> polyline)
        {
            float best = float.MaxValue;
            for (int i = 1; i < polyline.Count; i++)
            {
                Vector2 c = polyline[i - 1];
                Vector2 d = polyline[i];
                best = Mathf.Min(best, DistanceBetweenSegments(a, b, c, d));
            }

            return best;
        }

        private static float DistanceBetweenSegments(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            // Regional routes are coarse planning lines. Endpoint-to-segment checks are stable enough here and avoid importing heavier geometry code into this lane.
            return Mathf.Min(
                Mathf.Min(DistanceToSegment(a, c, d), DistanceToSegment(b, c, d)),
                Mathf.Min(DistanceToSegment(c, a, b), DistanceToSegment(d, a, b)));
        }

        private static RegionalSurveyParcelRecord FindParcelById(IReadOnlyList<RegionalSurveyParcelRecord> parcels, string parcelId)
        {
            if (parcels == null || string.IsNullOrWhiteSpace(parcelId))
            {
                return null;
            }

            for (int i = 0; i < parcels.Count; i++)
            {
                RegionalSurveyParcelRecord parcel = parcels[i];
                if (parcel != null && parcel.ParcelId == parcelId)
                {
                    return parcel;
                }
            }

            return null;
        }

        private static float DistanceToPolyline(Vector2 point, IReadOnlyList<Vector2> polyline)
        {
            if (polyline == null || polyline.Count == 0)
            {
                return float.MaxValue;
            }

            if (polyline.Count == 1)
            {
                return Vector2.Distance(point, polyline[0]);
            }

            float best = float.MaxValue;
            for (int i = 1; i < polyline.Count; i++)
            {
                best = Mathf.Min(best, DistanceToSegment(point, polyline[i - 1], polyline[i]));
            }

            return best;
        }

        private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSq = ab.sqrMagnitude;
            if (lengthSq <= 0.001f)
            {
                return Vector2.Distance(point, a);
            }

            float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSq);
            return Vector2.Distance(point, a + ab * t);
        }

        internal static List<RegionalRouteCorridorRecord> GenerateRouteCorridors(
            int seed,
            RegionalAnchorTownRecord anchor,
            IReadOnlyList<RegionalSurveyParcelRecord> parcels,
            IReadOnlyList<RegionalSettlementRecord> settlements,
            IReadOnlyList<RegionalWatercourseRecord> watercourses)
        {
            List<RegionalRouteCorridorRecord> routes = new();
            if (anchor == null)
            {
                return routes;
            }

            RegionalSettlementRecord anchorSettlement = FindOpeningFootholdSettlement(settlements, anchor.CenterMeters);
            Vector2 spineStart = new(Mathf.Max(0f, anchor.CenterMeters.x - 720f), anchor.CenterMeters.y);
            Vector2 spineEnd = new(anchor.CenterMeters.x + 720f, anchor.CenterMeters.y);
            List<Vector2> spinePoints = BuildCorridorPoints(seed, spineStart, spineEnd, 0, 55f);
            routes.Add(new RegionalRouteCorridorRecord(
                "route_opening_foothold_spine",
                RegionalRouteCorridorKind.OpeningFootholdSpine,
                anchorSettlement?.SettlementId ?? string.Empty,
                anchorSettlement?.SettlementId ?? string.Empty,
                string.Empty,
                string.Empty,
                CalculatePolylineLength(spinePoints),
                Mathf.Clamp01(anchor.GatewayScore01 * 0.50f + anchor.BuildableLand01 * 0.34f + anchor.ExpansionRoom01 * 0.16f),
                Mathf.Clamp01(0.68f + anchor.BuildableLand01 * 0.18f - Mathf.Clamp01(anchor.NearestWaterDistanceMeters / 1200f) * 0.10f),
                Mathf.Clamp01(0.20f + (1f - anchor.GatewayScore01) * 0.22f),
                $"Opening foothold commercial spine; gateway {anchor.GatewayScore01:P0}, buildable shelf {anchor.BuildableLand01:P0}, expansion room {anchor.ExpansionRoom01:P0}.",
                spinePoints));

            if (settlements == null || anchorSettlement == null)
            {
                return routes;
            }

            int connectorIndex = 1;
            for (int i = 0; i < settlements.Count; i++)
            {
                RegionalSettlementRecord settlement = settlements[i];
                if (settlement == null || settlement.SettlementId == anchorSettlement.SettlementId)
                {
                    continue;
                }

                RegionalSurveyParcelRecord destinationParcel = FindNearestParcel(parcels, settlement.CenterMeters);
                RegionalRouteCorridorKind kind = ResolveRouteCorridorKind(settlement, destinationParcel);
                Vector2 routeHandoff = ResolveOpeningTownRouteHandoff(anchor.CenterMeters, settlement.CenterMeters);
                List<Vector2> points = BuildCorridorPoints(seed, routeHandoff, settlement.CenterMeters, connectorIndex, Mathf.Lerp(90f, 260f, settlement.FreightSupport01));
                float length = CalculatePolylineLength(points);
                float waterInfluence = EvaluateWaterAccess01(settlement.CenterMeters, watercourses, 900f);
                float accessScore = destinationParcel != null ? ResolveAccessScore01(destinationParcel.AccessQuality) : 0.35f;
                float destinationWetness = destinationParcel != null ? destinationParcel.Wetness01 : waterInfluence;
                float practicality = Mathf.Clamp01(accessScore * 0.30f + settlement.FreightSupport01 * 0.26f + (1f - settlement.SupportBurden01) * 0.22f + settlement.Permanence01 * 0.14f + anchor.GatewayScore01 * 0.08f);
                float seasonalReliability = Mathf.Clamp01(accessScore * 0.34f + (1f - destinationWetness) * 0.24f + settlement.Permanence01 * 0.18f + settlement.FreightSupport01 * 0.14f + waterInfluence * 0.10f);
                float freightBurden = Mathf.Clamp01(settlement.SupportBurden01 * 0.34f + (1f - practicality) * 0.30f + (1f - seasonalReliability) * 0.22f + Mathf.Clamp01(length / 5200f) * 0.14f);
                routes.Add(new RegionalRouteCorridorRecord(
                    $"route_anchor_to_node_{connectorIndex:00}",
                    kind,
                    anchorSettlement.SettlementId,
                    settlement.SettlementId,
                    string.Empty,
                    destinationParcel?.ParcelId ?? string.Empty,
                    length,
                    practicality,
                    seasonalReliability,
                    freightBurden,
                    $"Connector from opening foothold to {settlement.Label}/{settlement.Character}; {length:0}m, access {destinationParcel?.AccessQuality.ToString() ?? "unknown"}, support burden {settlement.SupportBurden01:P0}.",
                    points));
                connectorIndex++;
            }

            return routes;
        }

        private static Vector2 ResolveOpeningTownRouteHandoff(Vector2 anchor, Vector2 destination)
        {
            Vector2 delta = destination - anchor;
            if (delta.sqrMagnitude <= 0.001f)
            {
                return anchor + Vector2.right * OpeningTownRouteHandoffMeters;
            }

            if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y))
            {
                return anchor + new Vector2(Mathf.Sign(delta.x), 0f) * OpeningTownRouteHandoffMeters;
            }

            return anchor + new Vector2(0f, Mathf.Sign(delta.y)) * OpeningTownRouteHandoffMeters;
        }

        private static RegionalSettlementRecord FindOpeningFootholdSettlement(IReadOnlyList<RegionalSettlementRecord> settlements, Vector2 anchor)
        {
            if (settlements == null)
            {
                return null;
            }

            RegionalSettlementRecord best = null;
            float bestScore = float.MinValue;
            for (int i = 0; i < settlements.Count; i++)
            {
                RegionalSettlementRecord settlement = settlements[i];
                if (settlement == null)
                {
                    continue;
                }

                float distanceScore = Mathf.Clamp01(1f - Vector2.Distance(settlement.CenterMeters, anchor) / 600f);
                float identityScore = settlement.Character == RegionalSettlementCharacter.OpeningFootholdGateway ? 0.65f : 0f;
                float score = identityScore + distanceScore * 0.35f + Mathf.Clamp01(settlement.Population / 120f) * 0.12f;
                if (score > bestScore)
                {
                    best = settlement;
                    bestScore = score;
                }
            }

            return best;
        }

        private static RegionalSurveyParcelRecord FindNearestParcel(IReadOnlyList<RegionalSurveyParcelRecord> parcels, Vector2 point)
        {
            if (parcels == null)
            {
                return null;
            }

            RegionalSurveyParcelRecord best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < parcels.Count; i++)
            {
                RegionalSurveyParcelRecord parcel = parcels[i];
                if (parcel == null)
                {
                    continue;
                }

                float distance = Vector2.Distance(parcel.CenterMeters, point);
                if (distance < bestDistance)
                {
                    best = parcel;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private static RegionalRouteCorridorKind ResolveRouteCorridorKind(RegionalSettlementRecord settlement, RegionalSurveyParcelRecord parcel)
        {
            if (settlement == null)
            {
                return RegionalRouteCorridorKind.SettlementConnector;
            }

            bool parcelIsFreightOutpost = parcel != null && parcel.RemoteSuitability == RegionalRemoteSuitability.FreightOutpost;
            bool parcelIsWorksite = parcel != null
                && (parcel.RemoteSuitability == RegionalRemoteSuitability.MineralProspect || parcel.RemoteSuitability == RegionalRemoteSuitability.TimberWorksite);
            if (settlement.Character == RegionalSettlementCharacter.FreightCrossing || parcelIsFreightOutpost)
            {
                return RegionalRouteCorridorKind.FreightTrack;
            }

            if (settlement.Character is RegionalSettlementCharacter.MineralOrRoughCountryCamp or RegionalSettlementCharacter.TimberWorksite || parcelIsWorksite)
            {
                return RegionalRouteCorridorKind.RemoteWorksiteTrack;
            }

            if (parcel != null && parcel.FrontageClass == RegionalParcelFrontageClass.CreekFront)
            {
                return RegionalRouteCorridorKind.CreekOrDrawTrack;
            }

            return RegionalRouteCorridorKind.SettlementConnector;
        }

        private static List<Vector2> BuildCorridorPoints(int seed, Vector2 source, Vector2 destination, int ordinal, float maxBendMeters)
        {
            Vector2 delta = destination - source;
            Vector2 normal = delta.sqrMagnitude <= 0.001f ? Vector2.right : new Vector2(-delta.y, delta.x).normalized;
            float bendSign = PositiveHash(seed * 149 + ordinal * 67) % 2 == 0 ? -1f : 1f;
            float bendScale = ((PositiveHash(seed * 173 + ordinal * 59) % 1000) / 1000f - 0.5f) * 2f;
            Vector2 mid = Vector2.Lerp(source, destination, 0.52f) + normal * bendSign * Mathf.Abs(bendScale) * Mathf.Max(0f, maxBendMeters);
            return new List<Vector2> { source, mid, destination };
        }

        private static float CalculatePolylineLength(IReadOnlyList<Vector2> points)
        {
            if (points == null || points.Count < 2)
            {
                return 0f;
            }

            float length = 0f;
            for (int i = 1; i < points.Count; i++)
            {
                length += Vector2.Distance(points[i - 1], points[i]);
            }

            return length;
        }

        private static void ResolveBand(int population, out RegionalPopulationBand band, out int min, out int max)
        {
            if (population < 10)
            {
                band = RegionalPopulationBand.FamilyCluster;
                min = 1;
                max = 9;
                return;
            }

            if (population < 25)
            {
                band = RegionalPopulationBand.Camp;
                min = 10;
                max = 24;
                return;
            }

            if (population < 50)
            {
                band = RegionalPopulationBand.Hamlet;
                min = 25;
                max = 49;
                return;
            }

            if (population < 200)
            {
                band = RegionalPopulationBand.Town;
                min = 50;
                max = 199;
                return;
            }

            band = RegionalPopulationBand.LargeTown;
            min = 200;
            max = 5000;
        }

        private static RegionalSettlementLabel ResolveLabel(RegionalPopulationBand band, int businessCount, float serviceGravity, float permanence)
        {
            return band switch
            {
                RegionalPopulationBand.Camp => businessCount >= 1 && permanence >= 0.30f ? RegionalSettlementLabel.Camp : RegionalSettlementLabel.LocalCluster,
                RegionalPopulationBand.Hamlet => serviceGravity >= 0.24f ? RegionalSettlementLabel.Hamlet : RegionalSettlementLabel.Camp,
                RegionalPopulationBand.Town => RegionalSettlementLabel.Town,
                RegionalPopulationBand.LargeTown => RegionalSettlementLabel.LargeTown,
                _ => serviceGravity >= 0.35f && permanence >= 0.38f ? RegionalSettlementLabel.Camp : RegionalSettlementLabel.LocalCluster
            };
        }

        private static RegionalSettlementPermanence ResolvePermanence(float permanence01, float confidence01, float declineRisk01, int population)
        {
            float score = Mathf.Clamp01(permanence01 * 0.46f + confidence01 * 0.34f + Mathf.Clamp01(population / 120f) * 0.20f - declineRisk01 * 0.22f);
            if (score >= 0.78f && population >= 80)
            {
                return RegionalSettlementPermanence.RegionalCenterCandidate;
            }

            if (score >= 0.62f)
            {
                return RegionalSettlementPermanence.DurableSettlement;
            }

            if (score >= 0.42f)
            {
                return RegionalSettlementPermanence.EmergingNode;
            }

            if (score >= 0.24f)
            {
                return RegionalSettlementPermanence.ThinButServiceable;
            }

            return RegionalSettlementPermanence.FragileOccupation;
        }

        private static float ResolveServiceDeficit01(int population, int businessCount, float serviceGravity)
        {
            float expectedBusinesses = population switch
            {
                < 10 => 0.25f,
                < 25 => 0.75f,
                < 50 => 1.5f,
                < 100 => 3.25f,
                < 200 => 5.5f,
                _ => 8f
            };
            float serviceCoverage = Mathf.Clamp01((businessCount + serviceGravity * 2.0f) / Mathf.Max(0.5f, expectedBusinesses));
            return Mathf.Clamp01(1f - serviceCoverage);
        }

        private static float ResolveDeclineRisk01(float permanence01, float localConfidence01, float serviceDeficit01, float supportBurden01)
        {
            return Mathf.Clamp01(serviceDeficit01 * 0.34f + supportBurden01 * 0.30f + (1f - permanence01) * 0.22f + (1f - localConfidence01) * 0.14f);
        }

        private static RegionalSettlementCharacter ResolveSettlementCharacter(RegionalSurveyParcelRecord parcel, Vector2 anchor)
        {
            if (parcel == null)
            {
                return RegionalSettlementCharacter.Unset;
            }

            if (parcel.RemoteSuitability == RegionalRemoteSuitability.MineralProspect)
            {
                return RegionalSettlementCharacter.MineralOrRoughCountryCamp;
            }

            if (parcel.RemoteSuitability == RegionalRemoteSuitability.TimberWorksite)
            {
                return RegionalSettlementCharacter.TimberWorksite;
            }

            if (parcel.RemoteSuitability == RegionalRemoteSuitability.FarmService || parcel.RemoteSuitability == RegionalRemoteSuitability.RanchRange)
            {
                return RegionalSettlementCharacter.FarmServicePocket;
            }

            if (parcel.RemoteSuitability == RegionalRemoteSuitability.FreightOutpost || (int)parcel.AccessQuality >= (int)RegionalParcelAccessQuality.WagonReachable && parcel.SupportBurden01 >= 0.38f)
            {
                return RegionalSettlementCharacter.FreightCrossing;
            }

            return Vector2.Distance(parcel.CenterMeters, anchor) > 2200f
                ? RegionalSettlementCharacter.MixedOpportunityNode
                : RegionalSettlementCharacter.OpeningFootholdGateway;
        }

        private static RegionalSurveyParcelRecord FindRemoteClusterParcel(IReadOnlyList<RegionalSurveyParcelRecord> parcels, Vector2 anchor)
        {
            RegionalSurveyParcelRecord best = null;
            float bestScore = float.MinValue;
            if (parcels == null)
            {
                return null;
            }

            for (int i = 0; i < parcels.Count; i++)
            {
                RegionalSurveyParcelRecord parcel = parcels[i];
                if (parcel == null || parcel.ParcelKind is RegionalParcelKind.TownPlatCore or RegionalParcelKind.TownPlatEdge)
                {
                    continue;
                }

                float distance = Vector2.Distance(parcel.CenterMeters, anchor);
                if (distance < 1800f)
                {
                    continue;
                }

                float remoteBias = parcel.RemoteSuitability switch
                {
                    RegionalRemoteSuitability.MineralProspect => 0.24f,
                    RegionalRemoteSuitability.TimberWorksite => 0.18f,
                    RegionalRemoteSuitability.FarmService => 0.14f,
                    RegionalRemoteSuitability.RanchRange => 0.12f,
                    RegionalRemoteSuitability.FreightOutpost => 0.10f,
                    RegionalRemoteSuitability.MixedOpportunity => 0.08f,
                    _ => 0f
                };
                float score = parcel.ResourceSuitability01 * 0.28f
                    + parcel.WaterAccess01 * 0.16f
                    + parcel.DevelopmentReadiness01 * 0.20f
                    + ResolveAccessScore01(parcel.AccessQuality) * 0.16f
                    + (1f - parcel.SupportBurden01) * 0.12f
                    + Mathf.Clamp01(distance / 5000f) * 0.08f
                    + remoteBias;
                if (score > bestScore)
                {
                    best = parcel;
                    bestScore = score;
                }
            }

            return best;
        }

        private static RegionalSurveyParcelRecord FindFreightSupportParcel(IReadOnlyList<RegionalSurveyParcelRecord> parcels, Vector2 anchor, string excludedParcelId)
        {
            RegionalSurveyParcelRecord best = null;
            float bestScore = float.MinValue;
            if (parcels == null)
            {
                return null;
            }

            for (int i = 0; i < parcels.Count; i++)
            {
                RegionalSurveyParcelRecord parcel = parcels[i];
                if (parcel == null
                    || parcel.ParcelId == excludedParcelId
                    || parcel.ParcelKind is RegionalParcelKind.TownPlatCore or RegionalParcelKind.TownPlatEdge)
                {
                    continue;
                }

                float distance = Vector2.Distance(parcel.CenterMeters, anchor);
                if (distance < 1100f || distance > 5200f)
                {
                    continue;
                }

                float score = ResolveAccessScore01(parcel.AccessQuality) * 0.32f
                    + parcel.DevelopmentReadiness01 * 0.20f
                    + parcel.WaterAccess01 * 0.16f
                    + (1f - Mathf.Abs(distance - 2800f) / 2800f) * 0.16f
                    + (parcel.RemoteSuitability == RegionalRemoteSuitability.FreightOutpost ? 0.16f : 0f);
                if (score > bestScore)
                {
                    best = parcel;
                    bestScore = score;
                }
            }

            return best;
        }

        private static RegionalParcelFrontageClass ResolveFrontageClass(
            RegionalParcelKind kind,
            Rect bounds,
            Vector2 size,
            Vector2 center,
            float waterAccess,
            float supportBurden,
            float distanceToAnchor)
        {
            if (kind == RegionalParcelKind.TownPlatCore)
            {
                return RegionalParcelFrontageClass.TownMainStreet;
            }

            if (waterAccess >= 0.72f)
            {
                return RegionalParcelFrontageClass.CreekFront;
            }

            float edgeDistance = Mathf.Min(Mathf.Min(bounds.xMin, size.x - bounds.xMax), Mathf.Min(bounds.yMin, size.y - bounds.yMax));
            if (kind == RegionalParcelKind.RoughParcel && distanceToAnchor > 2200f)
            {
                return RegionalParcelFrontageClass.RemoteWorksiteAccess;
            }

            if (supportBurden <= 0.26f)
            {
                return RegionalParcelFrontageClass.TrailOrRoad;
            }

            if (edgeDistance < 260f)
            {
                return RegionalParcelFrontageClass.RemoteWorksiteAccess;
            }

            return RegionalParcelFrontageClass.InteriorSurvey;
        }

        private static RegionalParcelAccessQuality ResolveAccessQuality(
            RegionalParcelFrontageClass frontage,
            float supportBurden,
            float distanceToAnchor,
            float waterAccess)
        {
            if (frontage == RegionalParcelFrontageClass.TownMainStreet || frontage == RegionalParcelFrontageClass.Crossroads)
            {
                return RegionalParcelAccessQuality.Gateway;
            }

            if (frontage == RegionalParcelFrontageClass.TrailOrRoad)
            {
                return supportBurden <= 0.32f ? RegionalParcelAccessQuality.RoadFrontage : RegionalParcelAccessQuality.WagonReachable;
            }

            if (frontage == RegionalParcelFrontageClass.CreekFront)
            {
                return waterAccess >= 0.82f && supportBurden <= 0.48f ? RegionalParcelAccessQuality.WagonReachable : RegionalParcelAccessQuality.SeasonalTrack;
            }

            if (frontage == RegionalParcelFrontageClass.RemoteWorksiteAccess)
            {
                return distanceToAnchor > 4200f ? RegionalParcelAccessQuality.SeasonalTrack : RegionalParcelAccessQuality.WagonReachable;
            }

            return supportBurden >= 0.72f ? RegionalParcelAccessQuality.Poor : RegionalParcelAccessQuality.SeasonalTrack;
        }

        private static RegionalParcelProvenance ResolveProvenance(
            RegionalParcelKind kind,
            float waterAccess,
            float resource,
            float supportBurden)
        {
            if (kind == RegionalParcelKind.FloodplainTract || waterAccess >= 0.78f)
            {
                return RegionalParcelProvenance.CreekFragment;
            }

            if (kind == RegionalParcelKind.EdgeExpansion)
            {
                return supportBurden <= 0.36f ? RegionalParcelProvenance.EdgeExpansionClaim : RegionalParcelProvenance.RanchOrFarmServiceTract;
            }

            if (kind == RegionalParcelKind.RoughParcel && resource >= 0.52f)
            {
                return RegionalParcelProvenance.RemoteIndustrialClaim;
            }

            if (kind == RegionalParcelKind.RuralTract && supportBurden >= 0.55f)
            {
                return RegionalParcelProvenance.RanchOrFarmServiceTract;
            }

            return RegionalParcelProvenance.PublicSurveyQuarter;
        }

        private static RegionalRemoteSuitability ResolveRemoteSuitability(
            RegionalParcelKind kind,
            RegionalTerrainRecipeFamily recipe,
            float waterAccess,
            float slope,
            float resource,
            float supportBurden,
            float distanceToAnchor)
        {
            if (distanceToAnchor < 900f && kind is RegionalParcelKind.TownPlatCore or RegionalParcelKind.TownPlatEdge)
            {
                return RegionalRemoteSuitability.None;
            }

            if (resource >= 0.68f || (kind == RegionalParcelKind.RoughParcel && resource >= 0.50f))
            {
                return RegionalRemoteSuitability.MineralProspect;
            }

            if (waterAccess >= 0.72f && slope <= 0.34f && supportBurden <= 0.62f)
            {
                return RegionalRemoteSuitability.FarmService;
            }

            if (recipe is RegionalTerrainRecipeFamily.PlainsToHillsInterface or RegionalTerrainRecipeFamily.MixedRoughFlatCountry && slope >= 0.38f)
            {
                return RegionalRemoteSuitability.RanchRange;
            }

            if (recipe is RegionalTerrainRecipeFamily.ValleyMouthTransition or RegionalTerrainRecipeFamily.CreekCorridorSettlementLand && waterAccess >= 0.48f)
            {
                return RegionalRemoteSuitability.TimberWorksite;
            }

            if (supportBurden >= 0.48f && waterAccess >= 0.36f)
            {
                return RegionalRemoteSuitability.FreightOutpost;
            }

            return distanceToAnchor > 2200f ? RegionalRemoteSuitability.MixedOpportunity : RegionalRemoteSuitability.None;
        }

        private static float ResolveTerrainBurden(float slope, float wetness, float supportBurden)
        {
            return Mathf.Clamp01(slope * 0.52f + wetness * 0.26f + supportBurden * 0.22f);
        }

        private static float ResolveAccessScore01(RegionalParcelAccessQuality access)
        {
            return access switch
            {
                RegionalParcelAccessQuality.Gateway => 1f,
                RegionalParcelAccessQuality.RoadFrontage => 0.82f,
                RegionalParcelAccessQuality.WagonReachable => 0.62f,
                RegionalParcelAccessQuality.SeasonalTrack => 0.36f,
                RegionalParcelAccessQuality.Poor => 0.12f,
                _ => 0f
            };
        }

        private static float ResolveDevelopmentReadiness(
            float buildSuitability,
            RegionalParcelAccessQuality access,
            float terrainBurden,
            float supportBurden,
            RegionalParcelKind kind)
        {
            float parcelBias = kind switch
            {
                RegionalParcelKind.TownPlatCore => 0.18f,
                RegionalParcelKind.TownPlatEdge => 0.10f,
                RegionalParcelKind.EdgeExpansion => 0.04f,
                RegionalParcelKind.FloodplainTract => -0.16f,
                RegionalParcelKind.RoughParcel => -0.10f,
                _ => 0f
            };
            return Mathf.Clamp01(buildSuitability * 0.46f + ResolveAccessScore01(access) * 0.24f + (1f - terrainBurden) * 0.18f + (1f - supportBurden) * 0.12f + parcelBias);
        }

        private static string BuildDistrictContext(
            RegionalParcelKind kind,
            RegionalRemoteSuitability remoteSuitability,
            float waterAccess,
            float slope,
            float resource,
            float supportBurden)
        {
            if (kind == RegionalParcelKind.FloodplainTract)
            {
                return "creek/floodplain tract: useful water relation but wet-ground and seasonal risk must be priced";
            }

            if (remoteSuitability == RegionalRemoteSuitability.MineralProspect)
            {
                return "rough-country prospect: useful for later claim, mine, or camp logic rather than ordinary frontage";
            }

            if (remoteSuitability == RegionalRemoteSuitability.TimberWorksite)
            {
                return "timber/draw pocket: future forestry, sawmill, or worksite hook";
            }

            if (remoteSuitability == RegionalRemoteSuitability.FarmService)
            {
                return "farm-service pocket: water and gentler land can support household and crop pressure";
            }

            if (remoteSuitability == RegionalRemoteSuitability.RanchRange)
            {
                return "range-support tract: rougher land favors grazing, corrals, and later ranch service";
            }

            if (remoteSuitability == RegionalRemoteSuitability.FreightOutpost)
            {
                return "freight-support location: distance and access make it a candidate for outpost or hauling pressure";
            }

            if (supportBurden <= 0.25f && slope <= 0.25f)
            {
                return "near-foothold expansion land: low burden and workable grade";
            }

            if (resource >= 0.5f)
            {
                return "resource-leaning rural tract: preserve for later industrial diligence";
            }

            return "ordinary surveyed rural tract: keep flexible for acquisition, farming, or later settlement pressure";
        }

        private static string BuildParcelDebugReason(
            RegionalParcelFrontageClass frontage,
            RegionalParcelAccessQuality access,
            RegionalParcelProvenance provenance,
            RegionalRemoteSuitability remoteSuitability,
            float readiness,
            float terrainBurden)
        {
            return $"Frontage {frontage}; access {access}; provenance {provenance}; remote {remoteSuitability}; readiness {readiness:P0}; terrain burden {terrainBurden:P0}.";
        }

        private static RegionalParcelKind ResolveRuralParcelKind(
            Rect bounds,
            Vector2 size,
            Vector2 center,
            float waterAccess,
            float slope,
            float resource,
            Vector2 anchor)
        {
            if (Vector2.Distance(center, anchor) < 700f)
            {
                return RegionalParcelKind.EdgeExpansion;
            }

            float edgeDistance = Mathf.Min(Mathf.Min(bounds.xMin, size.x - bounds.xMax), Mathf.Min(bounds.yMin, size.y - bounds.yMax));
            if (edgeDistance < 180f)
            {
                return RegionalParcelKind.EdgeExpansion;
            }

            if (waterAccess > 0.72f && slope < 0.42f)
            {
                return RegionalParcelKind.FloodplainTract;
            }

            if (slope > 0.48f || resource > 0.54f)
            {
                return RegionalParcelKind.RoughParcel;
            }

            return RegionalParcelKind.RuralTract;
        }

        private static void ResolveBiome(
            RegionalTerrainRecipeFamily recipe,
            float wetness,
            float slope,
            out RegionalBiomeKind biome,
            out RegionalVegetationKind vegetation)
        {
            if (wetness > 0.70f)
            {
                biome = RegionalBiomeKind.CreekBottom;
                vegetation = RegionalVegetationKind.RiparianTrees;
                return;
            }

            if (wetness > 0.48f)
            {
                biome = RegionalBiomeKind.WetLowland;
                vegetation = RegionalVegetationKind.WetGround;
                return;
            }

            if (slope > 0.62f)
            {
                biome = RegionalBiomeKind.RoughHills;
                vegetation = RegionalVegetationKind.SparseRoughCountry;
                return;
            }

            if (slope > 0.42f || recipe is RegionalTerrainRecipeFamily.PlainsToHillsInterface or RegionalTerrainRecipeFamily.MixedRoughFlatCountry)
            {
                biome = RegionalBiomeKind.UplandGrass;
                vegetation = RegionalVegetationKind.BrushAndScrub;
                return;
            }

            biome = RegionalBiomeKind.OpenPrairie;
            vegetation = RegionalVegetationKind.Grassland;
        }

        private static float DistanceToNearestWater(Vector2 point, IReadOnlyList<RegionalWatercourseRecord> watercourses)
        {
            float best = float.MaxValue;
            if (watercourses == null)
            {
                return best;
            }

            for (int i = 0; i < watercourses.Count; i++)
            {
                RegionalWatercourseRecord water = watercourses[i];
                if (water == null)
                {
                    continue;
                }

                for (int p = 1; p < water.Points.Count; p++)
                {
                    best = Mathf.Min(best, DistancePointToSegment(point, water.Points[p - 1], water.Points[p]));
                }
            }

            return best == float.MaxValue ? 99999f : best;
        }

        private static float EvaluateWaterAccess01(Vector2 point, IReadOnlyList<RegionalWatercourseRecord> watercourses, float maxDistance)
        {
            return Mathf.Clamp01(1f - DistanceToNearestWater(point, watercourses) / Mathf.Max(1f, maxDistance));
        }

        private static float EvaluateSupportBurden01(Vector2 point, Vector2 anchor, float waterAccess)
        {
            float distanceBurden = Mathf.Clamp01(Vector2.Distance(point, anchor) / 5200f);
            float waterRelief = waterAccess * 0.18f;
            return Mathf.Clamp01(distanceBurden - waterRelief);
        }

        private static float SampleElevationMeters(int seed, RegionalTerrainRecipeFamily recipe, Vector2 size, Vector2 point)
        {
            float x01 = point.x / Mathf.Max(1f, size.x);
            float y01 = point.y / Mathf.Max(1f, size.y);
            float longNoise = Mathf.PerlinNoise(seed * 0.013f + x01 * 2.2f, seed * 0.017f + y01 * 2.2f);
            float detail = Mathf.PerlinNoise(seed * 0.071f + x01 * 8.5f, seed * 0.053f + y01 * 8.5f);
            float eastRise = x01 * 18f;
            float northRise = y01 * 8f;
            float recipeLift = recipe switch
            {
                RegionalTerrainRecipeFamily.ValleyMouthTransition => Mathf.Abs(x01 - 0.5f) * 26f,
                RegionalTerrainRecipeFamily.PlainsToHillsInterface => x01 * 42f,
                RegionalTerrainRecipeFamily.MixedRoughFlatCountry => detail * 26f,
                RegionalTerrainRecipeFamily.RiverBendHigherShelf => y01 * 18f + longNoise * 12f,
                RegionalTerrainRecipeFamily.WetLowlandPockets => -8f + detail * 12f,
                _ => longNoise * 10f
            };
            return eastRise + northRise + recipeLift + detail * 5f;
        }

        private static float SampleSlope01(int seed, RegionalTerrainRecipeFamily recipe, Vector2 size, Vector2 point)
        {
            float sample = 90f;
            float center = SampleElevationMeters(seed, recipe, size, point);
            float east = SampleElevationMeters(seed, recipe, size, new Vector2(Mathf.Min(size.x, point.x + sample), point.y));
            float north = SampleElevationMeters(seed, recipe, size, new Vector2(point.x, Mathf.Min(size.y, point.y + sample)));
            float rise = Mathf.Max(Mathf.Abs(east - center), Mathf.Abs(north - center));
            return Mathf.Clamp01(rise / 16f);
        }

        private static float SampleResourceSuitability01(int seed, RegionalTerrainRecipeFamily recipe, Vector2 size, Vector2 point)
        {
            float slope = SampleSlope01(seed, recipe, size, point);
            float noise = Mathf.PerlinNoise(seed * 0.041f + point.x * 0.0017f, seed * 0.029f + point.y * 0.0013f);
            float edgeRidge = Mathf.Clamp01(Mathf.Abs(point.x - size.x * 0.5f) / Mathf.Max(1f, size.x * 0.5f));
            return Mathf.Clamp01(noise * 0.46f + slope * 0.32f + edgeRidge * 0.22f);
        }

        private static float DistancePointToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float denominator = Mathf.Max(0.0001f, Vector2.Dot(ab, ab));
            float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / denominator);
            return Vector2.Distance(point, a + ab * t);
        }

        private static Rect BoundsForPoints(IReadOnlyList<Vector2> points, float padding)
        {
            if (points == null || points.Count == 0)
            {
                return new Rect();
            }

            float minX = points[0].x;
            float maxX = points[0].x;
            float minY = points[0].y;
            float maxY = points[0].y;
            for (int i = 1; i < points.Count; i++)
            {
                minX = Mathf.Min(minX, points[i].x);
                maxX = Mathf.Max(maxX, points[i].x);
                minY = Mathf.Min(minY, points[i].y);
                maxY = Mathf.Max(maxY, points[i].y);
            }

            return Rect.MinMaxRect(minX - padding, minY - padding, maxX + padding, maxY + padding);
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
    }
}
