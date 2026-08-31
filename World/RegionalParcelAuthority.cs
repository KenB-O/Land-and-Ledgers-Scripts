using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace LandLedgers.World
{
    public enum RegionalParcelAuthorityUse
    {
        BackgroundHolding = 0,
        TownCoreFrontage = 1,
        TownEdgeExpansion = 2,
        FarmServiceTract = 3,
        RanchRangeTract = 4,
        TimberStandWorksite = 5,
        CoalDistrictCandidate = 6,
        IronDistrictCandidate = 7,
        GoldProspectCandidate = 8,
        SilverProspectCandidate = 9,
        FreightOutpostCandidate = 10,
        DepotRailCorridorCandidate = 11,
        RemoteIndustrialStaging = 12,
        WaterRiskHolding = 13
    }

    public enum RegionalParcelOwnerActionPosture
    {
        BackgroundWatch = 0,
        NearTermInspect = 1,
        StrategicHold = 2,
        PrepareInfrastructure = 3,
        DeferUntilPressure = 4,
        AvoidForNow = 5
    }

    public sealed class RegionalParcelAuthorityReadout
    {
        private readonly List<string> futureSystemHooks;

        public RegionalParcelAuthorityReadout(
            string parcelId,
            RegionalParcelAuthorityUse recommendedUse,
            string recommendedUseLabel,
            float acquisitionReadiness01,
            float timberSuitability01,
            float mineralSuitability01,
            float freightOutpostSuitability01,
            float remoteIndustrialSuitability01,
            float coalDistrictSuitability01,
            float ironDistrictSuitability01,
            float goldProspectSuitability01,
            float silverProspectSuitability01,
            float depotRailCorridorSuitability01,
            string developmentReadinessSummary,
            string acquisitionReadinessHint,
            string supportBurdenSummary,
            string terrainBurdenSummary,
            string frontageAccessSummary,
            string resourceHookReadout,
            string logisticsHookReadout,
            string ownerActionReadout,
            IReadOnlyList<string> futureSystemHooks,
            RegionalParcelOwnerActionPosture ownerActionPosture = RegionalParcelOwnerActionPosture.BackgroundWatch,
            string ownerActionPostureLabel = null,
            string flexibleUseReadout = null,
            string constraintReadout = null)
        {
            ParcelId = parcelId ?? string.Empty;
            RecommendedUse = recommendedUse;
            RecommendedUseLabel = recommendedUseLabel ?? "background holding";
            AcquisitionReadiness01 = Mathf.Clamp01(acquisitionReadiness01);
            TimberSuitability01 = Mathf.Clamp01(timberSuitability01);
            MineralSuitability01 = Mathf.Clamp01(mineralSuitability01);
            FreightOutpostSuitability01 = Mathf.Clamp01(freightOutpostSuitability01);
            RemoteIndustrialSuitability01 = Mathf.Clamp01(remoteIndustrialSuitability01);
            CoalDistrictSuitability01 = Mathf.Clamp01(coalDistrictSuitability01);
            IronDistrictSuitability01 = Mathf.Clamp01(ironDistrictSuitability01);
            GoldProspectSuitability01 = Mathf.Clamp01(goldProspectSuitability01);
            SilverProspectSuitability01 = Mathf.Clamp01(silverProspectSuitability01);
            DepotRailCorridorSuitability01 = Mathf.Clamp01(depotRailCorridorSuitability01);
            DevelopmentReadinessSummary = developmentReadinessSummary ?? string.Empty;
            AcquisitionReadinessHint = acquisitionReadinessHint ?? string.Empty;
            SupportBurdenSummary = supportBurdenSummary ?? string.Empty;
            TerrainBurdenSummary = terrainBurdenSummary ?? string.Empty;
            FrontageAccessSummary = frontageAccessSummary ?? string.Empty;
            ResourceHookReadout = resourceHookReadout ?? string.Empty;
            LogisticsHookReadout = logisticsHookReadout ?? string.Empty;
            OwnerActionReadout = ownerActionReadout ?? string.Empty;
            OwnerActionPosture = ownerActionPosture;
            OwnerActionPostureLabel = ownerActionPostureLabel ?? FormatOwnerActionPosture(ownerActionPosture);
            FlexibleUseReadout = flexibleUseReadout ?? "Flexible use follows frontage, access, demand, support burden, and conversion cost; this parcel is not hard-zoned.";
            ConstraintReadout = constraintReadout ?? BuildFallbackConstraintReadout(frontageAccessSummary, supportBurdenSummary, terrainBurdenSummary);
            this.futureSystemHooks = futureSystemHooks != null ? new List<string>(futureSystemHooks) : new List<string>();
        }

        public string ParcelId { get; }
        public RegionalParcelAuthorityUse RecommendedUse { get; }
        public string RecommendedUseLabel { get; }
        public float AcquisitionReadiness01 { get; }
        public float TimberSuitability01 { get; }
        public float MineralSuitability01 { get; }
        public float FreightOutpostSuitability01 { get; }
        public float RemoteIndustrialSuitability01 { get; }
        public float CoalDistrictSuitability01 { get; }
        public float IronDistrictSuitability01 { get; }
        public float GoldProspectSuitability01 { get; }
        public float SilverProspectSuitability01 { get; }
        public float DepotRailCorridorSuitability01 { get; }
        public string DevelopmentReadinessSummary { get; }
        public string AcquisitionReadinessHint { get; }
        public string SupportBurdenSummary { get; }
        public string TerrainBurdenSummary { get; }
        public string FrontageAccessSummary { get; }
        public string ResourceHookReadout { get; }
        public string LogisticsHookReadout { get; }
        public string OwnerActionReadout { get; }
        public RegionalParcelOwnerActionPosture OwnerActionPosture { get; }
        public string OwnerActionPostureLabel { get; }
        public string FlexibleUseReadout { get; }
        public string ConstraintReadout { get; }
        public IReadOnlyList<string> FutureSystemHooks => futureSystemHooks;

        public string BuildCompactReadout()
        {
            string hooks = futureSystemHooks.Count == 0 ? "general parcel tracking" : string.Join(", ", futureSystemHooks);
            return $"{ParcelId}: {RecommendedUseLabel}; action {OwnerActionPostureLabel}; {AcquisitionReadinessHint}; {ConstraintReadout}; hooks: {hooks}.";
        }

        public string BuildInspectionReadout()
        {
            StringBuilder builder = new();
            builder.AppendLine($"Parcel authority: {ParcelId} | {RecommendedUseLabel} | readiness {AcquisitionReadiness01:P0}");
            builder.AppendLine($"Development: {DevelopmentReadinessSummary} | Acquisition: {AcquisitionReadinessHint} | Action: {OwnerActionPostureLabel}");
            builder.AppendLine($"Access: {FrontageAccessSummary}");
            builder.AppendLine($"Burden: {SupportBurdenSummary}; {TerrainBurdenSummary}");
            builder.AppendLine($"Constraints: {ConstraintReadout}");
            builder.AppendLine($"Flexible use: {FlexibleUseReadout}");
            builder.AppendLine($"Resources: {ResourceHookReadout}");
            builder.AppendLine($"Logistics: {LogisticsHookReadout}");
            builder.AppendLine($"Future hooks: {(futureSystemHooks.Count == 0 ? "general acquisition and valuation" : string.Join(", ", futureSystemHooks))}");
            builder.AppendLine("Owner read: " + OwnerActionReadout);
            return builder.ToString().Trim();
        }

        private static string FormatOwnerActionPosture(RegionalParcelOwnerActionPosture posture)
        {
            return posture switch
            {
                RegionalParcelOwnerActionPosture.NearTermInspect => "near-term inspect",
                RegionalParcelOwnerActionPosture.StrategicHold => "strategic hold",
                RegionalParcelOwnerActionPosture.PrepareInfrastructure => "prepare infrastructure first",
                RegionalParcelOwnerActionPosture.DeferUntilPressure => "defer until pressure",
                RegionalParcelOwnerActionPosture.AvoidForNow => "avoid for now",
                _ => "background watch"
            };
        }

        private static string BuildFallbackConstraintReadout(string frontage, string support, string terrain)
        {
            return $"{frontage ?? "access unknown"}; {support ?? "support unknown"}; {terrain ?? "terrain unknown"}";
        }

    }

    public static class RegionalParcelAuthority
    {
        public static RegionalParcelAuthorityReadout Evaluate(RegionalWorldState state, RegionalSurveyParcelRecord parcel)
        {
            if (parcel == null)
            {
                return new RegionalParcelAuthorityReadout(
                    string.Empty,
                    RegionalParcelAuthorityUse.BackgroundHolding,
                    "missing parcel",
                    0f,
                    0f,
                    0f,
                    0f,
                    0f,
                    0f,
                    0f,
                    0f,
                    0f,
                    0f,
                    "no development read",
                    "no acquisition read",
                    "no support read",
                    "no terrain read",
                    "no frontage read",
                    "no resource read",
                    "no logistics read",
                    "no owner action available",
                    Array.Empty<string>());
            }

            RegionalRouteCorridorRecord nearestRoute = FindNearestRoute(state, parcel);
            float accessScore = ResolveAccessScore01(parcel.AccessQuality);
            float routePracticality = nearestRoute != null ? nearestRoute.Practicality01 : parcel.CorridorInfluence01;
            float routeReliability = nearestRoute != null ? nearestRoute.SeasonalReliability01 : Mathf.Clamp01(1f - parcel.CorridorBurden01);
            float routeConstraintBurden = nearestRoute != null
                ? Mathf.Max(nearestRoute.FreightBurden01, Mathf.Max(nearestRoute.MudSeasonRisk01, Mathf.Max(nearestRoute.BridgeOrFordNeed01, nearestRoute.GradeBurden01)))
                : parcel.CorridorBurden01;

            float acquisition = Mathf.Clamp01(
                parcel.DevelopmentReadiness01 * 0.42f
                + accessScore * 0.18f
                + parcel.FreightAccess01 * 0.14f
                + (1f - parcel.SupportBurden01) * 0.14f
                + (1f - parcel.TerrainBurden01) * 0.08f
                + parcel.WaterAccess01 * 0.04f);
            float timber = parcel.CalculateTimberSuitability01();
            float mineral = parcel.CalculateMineralSuitability01();
            float freight = parcel.CalculateFreightOutpostSuitability01();
            float remoteIndustrial = parcel.CalculateRemoteIndustrySuitability01();
            float coal = Mathf.Clamp01(parcel.ResourceSuitability01 * 0.34f + parcel.FreightAccess01 * 0.22f + (1f - parcel.MeanSlope01) * 0.16f + routePracticality * 0.12f + (1f - parcel.Wetness01) * 0.08f + accessScore * 0.08f);
            float iron = Mathf.Clamp01(parcel.ResourceSuitability01 * 0.36f + parcel.MeanSlope01 * 0.18f + parcel.TerrainBurden01 * 0.12f + parcel.FreightAccess01 * 0.16f + routeReliability * 0.10f + accessScore * 0.08f);
            float gold = Mathf.Clamp01(parcel.ResourceSuitability01 * 0.32f + parcel.MeanSlope01 * 0.16f + parcel.WaterAccess01 * 0.14f + parcel.TerrainBurden01 * 0.12f + parcel.SupportBurden01 * 0.10f + (1f - routeConstraintBurden) * 0.08f + RemoteSuitabilityScoreBonus(parcel, RegionalRemoteSuitability.MineralProspect, 0.08f));
            float silver = Mathf.Clamp01(parcel.ResourceSuitability01 * 0.34f + parcel.MeanSlope01 * 0.20f + parcel.TerrainBurden01 * 0.12f + parcel.FreightAccess01 * 0.12f + routePracticality * 0.10f + RemoteSuitabilityScoreBonus(parcel, RegionalRemoteSuitability.MineralProspect, 0.06f));
            float depotRail = Mathf.Clamp01(parcel.FreightAccess01 * 0.30f + parcel.CorridorInfluence01 * 0.24f + routePracticality * 0.16f + routeReliability * 0.12f + (1f - parcel.TerrainBurden01) * 0.10f + accessScore * 0.08f);

            RegionalParcelAuthorityUse use = ResolveRecommendedUse(parcel, timber, mineral, freight, remoteIndustrial, coal, iron, gold, silver, depotRail);
            List<string> hooks = BuildFutureHooks(parcel, use, timber, mineral, freight, remoteIndustrial, depotRail);
            RegionalParcelOwnerActionPosture posture = ResolveOwnerActionPosture(parcel, use, acquisition, remoteIndustrial, routePracticality, routeReliability, routeConstraintBurden);
            string postureLabel = FormatOwnerActionPosture(posture);
            string flexibleUse = BuildFlexibleUseReadout(parcel, use);
            string constraints = BuildConstraintReadout(parcel, routePracticality, routeReliability, routeConstraintBurden);

            return new RegionalParcelAuthorityReadout(
                parcel.ParcelId,
                use,
                FormatUse(use),
                acquisition,
                timber,
                mineral,
                freight,
                remoteIndustrial,
                coal,
                iron,
                gold,
                silver,
                depotRail,
                BuildDevelopmentReadinessSummary(parcel),
                BuildAcquisitionReadinessHint(parcel, acquisition),
                BuildSupportBurdenSummary(parcel),
                BuildTerrainBurdenSummary(parcel),
                BuildFrontageAccessSummary(parcel),
                BuildResourceHookReadout(use, timber, mineral, coal, iron, gold, silver),
                BuildLogisticsHookReadout(parcel, nearestRoute, freight, depotRail),
                BuildOwnerActionReadout(parcel, use, acquisition, remoteIndustrial),
                hooks,
                posture,
                postureLabel,
                flexibleUse,
                constraints);
        }

        public static string BuildInspectionReadout(RegionalWorldState state, RegionalSurveyParcelRecord parcel)
        {
            return Evaluate(state, parcel).BuildInspectionReadout();
        }

        private static RegionalParcelOwnerActionPosture ResolveOwnerActionPosture(RegionalSurveyParcelRecord parcel, RegionalParcelAuthorityUse use, float acquisition01, float remoteIndustrial01, float routePracticality01, float routeReliability01, float routeConstraintBurden01)
        {
            bool weakAccess = parcel.AccessQuality < RegionalParcelAccessQuality.WagonReachable || routePracticality01 < 0.36f || routeReliability01 < 0.36f;
            bool highBurden = parcel.SupportBurden01 >= 0.68f || parcel.TerrainBurden01 >= 0.70f || routeConstraintBurden01 >= 0.64f;
            bool remoteHook = use is RegionalParcelAuthorityUse.TimberStandWorksite or RegionalParcelAuthorityUse.CoalDistrictCandidate or RegionalParcelAuthorityUse.IronDistrictCandidate or RegionalParcelAuthorityUse.GoldProspectCandidate or RegionalParcelAuthorityUse.SilverProspectCandidate or RegionalParcelAuthorityUse.RemoteIndustrialStaging;

            if ((remoteHook || remoteIndustrial01 >= 0.58f) && (weakAccess || highBurden)) return RegionalParcelOwnerActionPosture.PrepareInfrastructure;
            if (acquisition01 >= 0.70f && !weakAccess && !highBurden) return RegionalParcelOwnerActionPosture.NearTermInspect;
            if (use is RegionalParcelAuthorityUse.DepotRailCorridorCandidate or RegionalParcelAuthorityUse.FreightOutpostCandidate || (remoteHook && !weakAccess)) return RegionalParcelOwnerActionPosture.StrategicHold;
            if (highBurden && acquisition01 < 0.34f) return RegionalParcelOwnerActionPosture.AvoidForNow;
            if (acquisition01 >= 0.46f) return RegionalParcelOwnerActionPosture.DeferUntilPressure;
            return RegionalParcelOwnerActionPosture.BackgroundWatch;
        }

        private static string FormatOwnerActionPosture(RegionalParcelOwnerActionPosture posture)
        {
            return posture switch
            {
                RegionalParcelOwnerActionPosture.NearTermInspect => "near-term inspect",
                RegionalParcelOwnerActionPosture.StrategicHold => "strategic hold",
                RegionalParcelOwnerActionPosture.PrepareInfrastructure => "prepare infrastructure first",
                RegionalParcelOwnerActionPosture.DeferUntilPressure => "defer until pressure",
                RegionalParcelOwnerActionPosture.AvoidForNow => "avoid for now",
                _ => "background watch"
            };
        }

        private static string BuildFlexibleUseReadout(RegionalSurveyParcelRecord parcel, RegionalParcelAuthorityUse use)
        {
            return use switch
            {
                RegionalParcelAuthorityUse.TownCoreFrontage or RegionalParcelAuthorityUse.TownEdgeExpansion => "Not hard-zoned: practical use follows frontage, access, demand, support burden, lot depth, and conversion cost.",
                RegionalParcelAuthorityUse.GoldProspectCandidate or RegionalParcelAuthorityUse.SilverProspectCandidate or RegionalParcelAuthorityUse.CoalDistrictCandidate or RegionalParcelAuthorityUse.IronDistrictCandidate => "Mineral context is a staged claim/prospect hook, not a guaranteed mine; diligence, freight, labor, support, and capital still decide use.",
                RegionalParcelAuthorityUse.DepotRailCorridorCandidate => "Rail/depot value is corridor optionality, not proof that rail exists now.",
                RegionalParcelAuthorityUse.TimberStandWorksite or RegionalParcelAuthorityUse.RemoteIndustrialStaging => "Remote worksite value depends on road access, hauling, storage, labor, boarding, and settlement support.",
                _ => "Flexible use follows frontage, access, demand, support burden, and conversion cost; this parcel is not hard-zoned."
            };
        }

        private static string BuildConstraintReadout(RegionalSurveyParcelRecord parcel, float routePracticality01, float routeReliability01, float routeConstraintBurden01)
        {
            List<string> constraints = new();
            if (parcel.AccessQuality < RegionalParcelAccessQuality.WagonReachable) constraints.Add("poor access");
            if (parcel.SupportBurden01 >= 0.66f) constraints.Add("high support burden");
            else if (parcel.SupportBurden01 >= 0.46f) constraints.Add("moderate support burden");
            if (parcel.TerrainBurden01 >= 0.66f) constraints.Add("heavy site preparation");
            if (parcel.Wetness01 >= 0.58f) constraints.Add("wet/floodplain timing");
            if (routePracticality01 < 0.38f) constraints.Add("weak route practicality");
            if (routeReliability01 < 0.38f) constraints.Add("seasonal route reliability risk");
            if (routeConstraintBurden01 >= 0.60f) constraints.Add("high route constraint burden");
            return constraints.Count == 0 ? "no major regional constraint identified" : string.Join(", ", constraints);
        }

        private static RegionalRouteCorridorRecord FindNearestRoute(RegionalWorldState state, RegionalSurveyParcelRecord parcel)
        {
            if (state == null || parcel == null || state.RouteCorridors == null || string.IsNullOrWhiteSpace(parcel.NearestCorridorId))
            {
                return null;
            }

            for (int i = 0; i < state.RouteCorridors.Count; i++)
            {
                RegionalRouteCorridorRecord route = state.RouteCorridors[i];
                if (route != null && string.Equals(route.CorridorId, parcel.NearestCorridorId, StringComparison.OrdinalIgnoreCase))
                {
                    return route;
                }
            }

            return null;
        }

        private static RegionalParcelAuthorityUse ResolveRecommendedUse(
            RegionalSurveyParcelRecord parcel,
            float timber,
            float mineral,
            float freight,
            float remoteIndustrial,
            float coal,
            float iron,
            float gold,
            float silver,
            float depotRail)
        {
            if (parcel.ParcelKind == RegionalParcelKind.TownPlatCore)
            {
                return RegionalParcelAuthorityUse.TownCoreFrontage;
            }

            if (parcel.ParcelKind is RegionalParcelKind.TownPlatEdge or RegionalParcelKind.EdgeExpansion)
            {
                return RegionalParcelAuthorityUse.TownEdgeExpansion;
            }

            if (parcel.ParcelKind == RegionalParcelKind.FloodplainTract && parcel.Wetness01 >= 0.58f)
            {
                return RegionalParcelAuthorityUse.WaterRiskHolding;
            }

            if (depotRail >= 0.68f && freight >= 0.58f)
            {
                return RegionalParcelAuthorityUse.DepotRailCorridorCandidate;
            }

            float bestMineralKind = Mathf.Max(Mathf.Max(coal, iron), Mathf.Max(gold, silver));
            if (mineral >= 0.58f || bestMineralKind >= 0.62f || parcel.RemoteSuitability == RegionalRemoteSuitability.MineralProspect)
            {
                if (coal >= iron && coal >= gold && coal >= silver)
                {
                    return RegionalParcelAuthorityUse.CoalDistrictCandidate;
                }

                if (iron >= gold && iron >= silver)
                {
                    return RegionalParcelAuthorityUse.IronDistrictCandidate;
                }

                return gold >= silver ? RegionalParcelAuthorityUse.GoldProspectCandidate : RegionalParcelAuthorityUse.SilverProspectCandidate;
            }

            if (timber >= 0.58f || parcel.RemoteSuitability == RegionalRemoteSuitability.TimberWorksite)
            {
                return RegionalParcelAuthorityUse.TimberStandWorksite;
            }

            if (freight >= 0.60f || parcel.RemoteSuitability == RegionalRemoteSuitability.FreightOutpost)
            {
                return RegionalParcelAuthorityUse.FreightOutpostCandidate;
            }

            if (remoteIndustrial >= 0.58f || parcel.RemoteSuitability == RegionalRemoteSuitability.MixedOpportunity)
            {
                return RegionalParcelAuthorityUse.RemoteIndustrialStaging;
            }

            if (parcel.RemoteSuitability == RegionalRemoteSuitability.RanchRange || (parcel.FarmSuitability01 >= 0.46f && parcel.MeanSlope01 >= 0.34f))
            {
                return RegionalParcelAuthorityUse.RanchRangeTract;
            }

            if (parcel.RemoteSuitability == RegionalRemoteSuitability.FarmService || parcel.FarmSuitability01 >= 0.56f)
            {
                return RegionalParcelAuthorityUse.FarmServiceTract;
            }

            return RegionalParcelAuthorityUse.BackgroundHolding;
        }

        private static List<string> BuildFutureHooks(
            RegionalSurveyParcelRecord parcel,
            RegionalParcelAuthorityUse use,
            float timber,
            float mineral,
            float freight,
            float remoteIndustrial,
            float depotRail)
        {
            List<string> hooks = new();
            if (parcel.ParcelKind is RegionalParcelKind.TownPlatCore or RegionalParcelKind.TownPlatEdge or RegionalParcelKind.EdgeExpansion)
            {
                hooks.Add("acquisition/valuation");
                hooks.Add("settlement expansion");
            }

            if (timber >= 0.52f || use == RegionalParcelAuthorityUse.TimberStandWorksite)
            {
                hooks.Add("logging/forestry");
            }

            if (mineral >= 0.54f
                || use is RegionalParcelAuthorityUse.CoalDistrictCandidate
                    or RegionalParcelAuthorityUse.IronDistrictCandidate
                    or RegionalParcelAuthorityUse.GoldProspectCandidate
                    or RegionalParcelAuthorityUse.SilverProspectCandidate)
            {
                hooks.Add("mining/claims");
            }

            if (freight >= 0.50f || use is RegionalParcelAuthorityUse.FreightOutpostCandidate or RegionalParcelAuthorityUse.DepotRailCorridorCandidate)
            {
                hooks.Add("freight/livery");
            }

            if (depotRail >= 0.58f || use == RegionalParcelAuthorityUse.DepotRailCorridorCandidate)
            {
                hooks.Add("rail/depot groundwork");
            }

            if (remoteIndustrial >= 0.54f || parcel.SupportBurden01 >= 0.55f)
            {
                hooks.Add("remote support burden");
            }

            if (parcel.FarmSuitability01 >= 0.52f || use is RegionalParcelAuthorityUse.FarmServiceTract or RegionalParcelAuthorityUse.RanchRangeTract)
            {
                hooks.Add("farm/ranch service");
            }

            if (hooks.Count == 0)
            {
                hooks.Add("background ownership");
            }

            return hooks;
        }

        private static string BuildDevelopmentReadinessSummary(RegionalSurveyParcelRecord parcel)
        {
            if (parcel.DevelopmentReadiness01 >= 0.72f)
            {
                return "near-term ready land with few site-work blockers";
            }

            if (parcel.DevelopmentReadiness01 >= 0.48f)
            {
                return "usable land with preparation and local support";
            }

            if (parcel.BuildSuitability01 >= 0.40f || parcel.FarmSuitability01 >= 0.45f)
            {
                return "plausible land, but development needs roads, drainage, labor, or capital";
            }

            return "longer-term land; keep it readable, but do not force early active play";
        }

        private static string BuildAcquisitionReadinessHint(RegionalSurveyParcelRecord parcel, float acquisition01)
        {
            if (acquisition01 >= 0.72f && parcel.SupportBurden01 <= 0.48f)
            {
                return "inspect soon for purchase, platting, or near-term improvement";
            }

            if (acquisition01 >= 0.52f)
            {
                return "watchlist parcel; decision depends on route work and local demand";
            }

            if (parcel.SupportBurden01 >= 0.66f)
            {
                return "defer unless a specific resource, route, or strategic land reason justifies the burden";
            }

            return "background holding; useful for valuation and later regional pressure";
        }

        private static string BuildSupportBurdenSummary(RegionalSurveyParcelRecord parcel)
        {
            if (parcel.SupportBurden01 >= 0.68f)
            {
                return "high support burden from current settlement/service coverage";
            }

            if (parcel.SupportBurden01 >= 0.38f)
            {
                return "moderate support burden; workable with freight or service investment";
            }

            return "low support burden; existing settlement gravity can plausibly support it";
        }

        private static string BuildTerrainBurdenSummary(RegionalSurveyParcelRecord parcel)
        {
            if (parcel.TerrainBurden01 >= 0.66f)
            {
                return "rough grade, slope, or ground conditions will slow development and hauling";
            }

            if (parcel.Wetness01 >= 0.60f)
            {
                return "wet or floodplain influence needs drainage, timing, or cautious use";
            }

            if (parcel.TerrainBurden01 >= 0.34f)
            {
                return "some terrain burden, but it is not a fatal blocker";
            }

            return "terrain burden is light for current regional planning";
        }

        private static string BuildFrontageAccessSummary(RegionalSurveyParcelRecord parcel)
        {
            string access = parcel.AccessQuality switch
            {
                RegionalParcelAccessQuality.Gateway => "gateway access",
                RegionalParcelAccessQuality.RoadFrontage => "road frontage",
                RegionalParcelAccessQuality.WagonReachable => "wagon-reachable access",
                RegionalParcelAccessQuality.SeasonalTrack => "seasonal track access",
                RegionalParcelAccessQuality.Poor => "poor access",
                _ => "no reliable access recorded"
            };
            return $"{access} via {parcel.FrontageClass}; freight access {parcel.FreightAccess01:P0}; corridor influence {parcel.CorridorInfluence01:P0}";
        }

        private static string BuildResourceHookReadout(RegionalParcelAuthorityUse use, float timber, float mineral, float coal, float iron, float gold, float silver)
        {
            string leading = use switch
            {
                RegionalParcelAuthorityUse.CoalDistrictCandidate => "coal district candidate",
                RegionalParcelAuthorityUse.IronDistrictCandidate => "iron district candidate",
                RegionalParcelAuthorityUse.GoldProspectCandidate => "gold prospect candidate",
                RegionalParcelAuthorityUse.SilverProspectCandidate => "silver prospect candidate",
                RegionalParcelAuthorityUse.TimberStandWorksite => "timber stand/worksite candidate",
                _ => mineral >= timber ? "general mineral/resource diligence" : "general timber or land-use diligence"
            };

            return $"{leading}; timber {timber:P0}, mineral {mineral:P0}, coal {coal:P0}, iron {iron:P0}, gold {gold:P0}, silver {silver:P0}";
        }

        private static string BuildLogisticsHookReadout(RegionalSurveyParcelRecord parcel, RegionalRouteCorridorRecord route, float freight, float depotRail)
        {
            string routeRead = route != null
                ? $"{route.CorridorId} {route.Kind}, practicality {route.Practicality01:P0}, reliability {route.SeasonalReliability01:P0}, constraint {route.PrimaryConstraintKind}"
                : string.IsNullOrWhiteSpace(parcel.NearestCorridorId) ? "no named route yet" : $"{parcel.NearestCorridorId} ({parcel.NearestCorridorKind})";
            return $"freight/outpost {freight:P0}; depot/rail-corridor groundwork {depotRail:P0}; route {routeRead}; parcel corridor burden {parcel.CorridorBurden01:P0}";
        }

        private static string BuildOwnerActionReadout(RegionalSurveyParcelRecord parcel, RegionalParcelAuthorityUse use, float acquisition01, float remoteIndustrial01)
        {
            if (use == RegionalParcelAuthorityUse.TownCoreFrontage || use == RegionalParcelAuthorityUse.TownEdgeExpansion)
            {
                return "use for town platting, frontage control, civic-commercial pressure, or clean tutorial expansion.";
            }

            if (use is RegionalParcelAuthorityUse.CoalDistrictCandidate or RegionalParcelAuthorityUse.IronDistrictCandidate)
            {
                return "treat as industrial-resource groundwork; route capacity and labor support should matter before extraction.";
            }

            if (use is RegionalParcelAuthorityUse.GoldProspectCandidate or RegionalParcelAuthorityUse.SilverProspectCandidate)
            {
                return "treat as a claim/prospect hook with boom speculation, not as a guaranteed mine.";
            }

            if (use == RegionalParcelAuthorityUse.TimberStandWorksite)
            {
                return "hold for logging, camp, or sawmill support after hauling and labor constraints are modeled.";
            }

            if (use == RegionalParcelAuthorityUse.DepotRailCorridorCandidate)
            {
                return "keep available for future depot, freight yard, or rail-corridor valuation without implying rail exists now.";
            }

            if (use == RegionalParcelAuthorityUse.FreightOutpostCandidate)
            {
                return "watch for livery, storage, route work, and recurring freight pressure.";
            }

            if (remoteIndustrial01 >= 0.58f || parcel.SupportBurden01 >= 0.62f)
            {
                return "possible remote node, but support burden must be solved before normal settlement growth can hold.";
            }

            if (acquisition01 >= 0.58f)
            {
                return "reasonable acquisition candidate if local demand, capital, and road access line up.";
            }

            return "keep as surveyed background land until ownership, route, resource, or settlement pressure gives it a reason.";
        }

        private static string FormatUse(RegionalParcelAuthorityUse use)
        {
            return use switch
            {
                RegionalParcelAuthorityUse.TownCoreFrontage => "town-core frontage / civic-commercial land",
                RegionalParcelAuthorityUse.TownEdgeExpansion => "town-edge expansion land",
                RegionalParcelAuthorityUse.FarmServiceTract => "farm-service tract",
                RegionalParcelAuthorityUse.RanchRangeTract => "ranch/range tract",
                RegionalParcelAuthorityUse.TimberStandWorksite => "timber stand / logging worksite candidate",
                RegionalParcelAuthorityUse.CoalDistrictCandidate => "coal district candidate",
                RegionalParcelAuthorityUse.IronDistrictCandidate => "iron district candidate",
                RegionalParcelAuthorityUse.GoldProspectCandidate => "gold prospect candidate",
                RegionalParcelAuthorityUse.SilverProspectCandidate => "silver prospect candidate",
                RegionalParcelAuthorityUse.FreightOutpostCandidate => "freight/livery outpost candidate",
                RegionalParcelAuthorityUse.DepotRailCorridorCandidate => "depot / rail-corridor groundwork",
                RegionalParcelAuthorityUse.RemoteIndustrialStaging => "remote industrial staging candidate",
                RegionalParcelAuthorityUse.WaterRiskHolding => "water-influenced holding with seasonal risk",
                _ => "background ownership parcel"
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

        private static float RemoteSuitabilityScoreBonus(RegionalSurveyParcelRecord parcel, RegionalRemoteSuitability suitability, float bonus)
        {
            return parcel != null && parcel.RemoteSuitability == suitability ? Mathf.Max(0f, bonus) : 0f;
        }
    }
}
