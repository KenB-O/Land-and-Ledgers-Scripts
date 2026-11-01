using System;
using System.Collections.Generic;
using System.Text;
using LandLedgers.Persistence;
using UnityEngine;

namespace LandLedgers.World
{
    public enum MineralResourceKind
    {
        Coal = 0,
        Iron = 1,
        Gold = 2,
        Silver = 3
    }

    [Serializable]
    public sealed class MineralDistrictRecord
    {
        [SerializeField] private string id = string.Empty;
        [SerializeField] private MineralResourceKind kind;
        [SerializeField] private Vector3 center;
        [SerializeField] private GridRect bounds;
        [SerializeField, Min(0f)] private float radiusMeters;
        [SerializeField, Range(0f, 1f)] private float strength01;
        [SerializeField, Range(0f, 1f)] private float suitability01;
        [SerializeField, Range(0f, 1f)] private float freightPressure01;
        [SerializeField, Range(0f, 1f)] private float settlementPressure01;
        [SerializeField, Range(0f, 1f)] private float industrialBackboneSignificance01;
        [SerializeField, Range(0f, 1f)] private float exportSpeculationSignificance01;
        [SerializeField] private string notes = string.Empty;

        public string Id => id ?? string.Empty;
        public MineralResourceKind Kind => kind;
        public Vector3 Center => center;
        public GridRect Bounds => bounds;
        public float RadiusMeters => Mathf.Max(0f, radiusMeters);
        public float Strength01 => Mathf.Clamp01(strength01);
        public float Suitability01 => Mathf.Clamp01(suitability01);
        public float FreightPressure01 => Mathf.Clamp01(freightPressure01);
        public float SettlementPressure01 => Mathf.Clamp01(settlementPressure01);
        public float IndustrialBackboneSignificance01 => Mathf.Clamp01(industrialBackboneSignificance01);
        public float ExportSpeculationSignificance01 => Mathf.Clamp01(exportSpeculationSignificance01);
        public string Notes => notes ?? string.Empty;

        public static MineralDistrictRecord Create(
            string id,
            MineralResourceKind kind,
            Vector3 center,
            GridRect bounds,
            float radiusMeters,
            float strength01,
            float suitability01,
            float freightPressure01,
            float settlementPressure01,
            float industrialBackboneSignificance01,
            float exportSpeculationSignificance01,
            string notes)
        {
            return new MineralDistrictRecord
            {
                id = id ?? string.Empty,
                kind = kind,
                center = center,
                bounds = bounds,
                radiusMeters = Mathf.Max(0f, radiusMeters),
                strength01 = Mathf.Clamp01(strength01),
                suitability01 = Mathf.Clamp01(suitability01),
                freightPressure01 = Mathf.Clamp01(freightPressure01),
                settlementPressure01 = Mathf.Clamp01(settlementPressure01),
                industrialBackboneSignificance01 = Mathf.Clamp01(industrialBackboneSignificance01),
                exportSpeculationSignificance01 = Mathf.Clamp01(exportSpeculationSignificance01),
                notes = notes ?? string.Empty
            };
        }
    }

    [Serializable]
    public sealed class RemoteIndustrySiteRecord
    {
        [SerializeField] private string id = string.Empty;
        [SerializeField] private MineralResourceKind dominantResource;
        [SerializeField] private Vector3 anchorPosition;
        [SerializeField] private GridRect anchorArea;
        [SerializeField] private List<string> linkedDistrictIds = new();
        [SerializeField, Range(0f, 1f)] private float siteStrength01;
        [SerializeField, Range(0f, 1f)] private float parcelRelevance01;
        [SerializeField, Range(0f, 1f)] private float futureCampRelevance01;
        [SerializeField, Range(0f, 1f)] private float futureBoomtownRelevance01;
        [SerializeField, Range(0f, 1f)] private float freightPressure01;
        [SerializeField, Range(0f, 1f)] private float settlementPressure01;
        [SerializeField] private string debugLabel = string.Empty;

        public string Id => id ?? string.Empty;
        public MineralResourceKind DominantResource => dominantResource;
        public Vector3 AnchorPosition => anchorPosition;
        public GridRect AnchorArea => anchorArea;
        public IReadOnlyList<string> LinkedDistrictIds => linkedDistrictIds != null ? linkedDistrictIds : Array.Empty<string>();
        public float SiteStrength01 => Mathf.Clamp01(siteStrength01);
        public float ParcelRelevance01 => Mathf.Clamp01(parcelRelevance01);
        public float FutureCampRelevance01 => Mathf.Clamp01(futureCampRelevance01);
        public float FutureBoomtownRelevance01 => Mathf.Clamp01(futureBoomtownRelevance01);
        public float FreightPressure01 => Mathf.Clamp01(freightPressure01);
        public float SettlementPressure01 => Mathf.Clamp01(settlementPressure01);
        public string DebugLabel => debugLabel ?? string.Empty;

        public static RemoteIndustrySiteRecord Create(
            string id,
            MineralResourceKind dominantResource,
            Vector3 anchorPosition,
            GridRect anchorArea,
            IReadOnlyList<string> linkedDistrictIds,
            float siteStrength01,
            float parcelRelevance01,
            float futureCampRelevance01,
            float futureBoomtownRelevance01,
            float freightPressure01,
            float settlementPressure01,
            string debugLabel)
        {
            RemoteIndustrySiteRecord record = new()
            {
                id = id ?? string.Empty,
                dominantResource = dominantResource,
                anchorPosition = anchorPosition,
                anchorArea = anchorArea,
                siteStrength01 = Mathf.Clamp01(siteStrength01),
                parcelRelevance01 = Mathf.Clamp01(parcelRelevance01),
                futureCampRelevance01 = Mathf.Clamp01(futureCampRelevance01),
                futureBoomtownRelevance01 = Mathf.Clamp01(futureBoomtownRelevance01),
                freightPressure01 = Mathf.Clamp01(freightPressure01),
                settlementPressure01 = Mathf.Clamp01(settlementPressure01),
                debugLabel = debugLabel ?? string.Empty
            };

            if (linkedDistrictIds != null)
            {
                for (int i = 0; i < linkedDistrictIds.Count; i++)
                {
                    string candidate = linkedDistrictIds[i];
                    if (!string.IsNullOrWhiteSpace(candidate))
                    {
                        record.linkedDistrictIds.Add(candidate);
                    }
                }
            }

            return record;
        }
    }

    [Serializable]
    public sealed class RegionalResourceSnapshot
    {
        [SerializeField] private List<MineralDistrictRecord> districts = new();
        [SerializeField] private List<RemoteIndustrySiteRecord> remoteSites = new();
        [SerializeField, Range(0f, 1f)] private float totalFreightPressure01;
        [SerializeField, Range(0f, 1f)] private float totalSettlementPressure01;
        [SerializeField, Range(0f, 1f)] private float totalRemoteDevelopmentPressure01;

        public static RegionalResourceSnapshot Empty => new();
        public IReadOnlyList<MineralDistrictRecord> Districts => districts != null ? districts : Array.Empty<MineralDistrictRecord>();
        public IReadOnlyList<RemoteIndustrySiteRecord> RemoteSites => remoteSites != null ? remoteSites : Array.Empty<RemoteIndustrySiteRecord>();
        public float TotalFreightPressure01 => Mathf.Clamp01(totalFreightPressure01);
        public float TotalSettlementPressure01 => Mathf.Clamp01(totalSettlementPressure01);
        public float TotalRemoteDevelopmentPressure01 => Mathf.Clamp01(totalRemoteDevelopmentPressure01);
        public bool HasMeaningfulMineralOpportunity => Districts.Count > 0 || RemoteSites.Count > 0;

        public void SetTotals(float freight, float settlement, float remoteDevelopment)
        {
            totalFreightPressure01 = Mathf.Clamp01(freight);
            totalSettlementPressure01 = Mathf.Clamp01(settlement);
            totalRemoteDevelopmentPressure01 = Mathf.Clamp01(remoteDevelopment);
        }

        public void SetRecords(List<MineralDistrictRecord> newDistricts, List<RemoteIndustrySiteRecord> newSites)
        {
            districts = newDistricts ?? new List<MineralDistrictRecord>();
            remoteSites = newSites ?? new List<RemoteIndustrySiteRecord>();
        }

        public RegionalResourceSaveDto CaptureSaveDto()
        {
            RegionalResourceSaveDto dto = new()
            {
                totalFreightPressure01 = TotalFreightPressure01,
                totalSettlementPressure01 = TotalSettlementPressure01,
                totalRemoteDevelopmentPressure01 = TotalRemoteDevelopmentPressure01
            };

            for (int i = 0; i < Districts.Count; i++)
            {
                MineralDistrictRecord district = Districts[i];
                if (district == null)
                {
                    continue;
                }

                dto.districts.Add(new MineralDistrictSaveDto
                {
                    id = district.Id,
                    kind = district.Kind,
                    centerX = district.Center.x,
                    centerY = district.Center.y,
                    centerZ = district.Center.z,
                    bounds = ToSaveDto(district.Bounds),
                    radiusMeters = district.RadiusMeters,
                    strength01 = district.Strength01,
                    suitability01 = district.Suitability01,
                    freightPressure01 = district.FreightPressure01,
                    settlementPressure01 = district.SettlementPressure01,
                    industrialBackboneSignificance01 = district.IndustrialBackboneSignificance01,
                    exportSpeculationSignificance01 = district.ExportSpeculationSignificance01,
                    notes = district.Notes
                });
            }

            for (int i = 0; i < RemoteSites.Count; i++)
            {
                RemoteIndustrySiteRecord site = RemoteSites[i];
                if (site == null)
                {
                    continue;
                }

                dto.remoteSites.Add(new RemoteIndustrySiteSaveDto
                {
                    id = site.Id,
                    dominantResource = site.DominantResource,
                    anchorX = site.AnchorPosition.x,
                    anchorY = site.AnchorPosition.y,
                    anchorZ = site.AnchorPosition.z,
                    anchorArea = ToSaveDto(site.AnchorArea),
                    linkedDistrictIds = new List<string>(site.LinkedDistrictIds),
                    siteStrength01 = site.SiteStrength01,
                    parcelRelevance01 = site.ParcelRelevance01,
                    futureCampRelevance01 = site.FutureCampRelevance01,
                    futureBoomtownRelevance01 = site.FutureBoomtownRelevance01,
                    freightPressure01 = site.FreightPressure01,
                    settlementPressure01 = site.SettlementPressure01,
                    debugLabel = site.DebugLabel
                });
            }

            return dto;
        }

        public static RegionalResourceSnapshot FromSaveDto(RegionalResourceSaveDto dto)
        {
            RegionalResourceSnapshot snapshot = new();
            if (dto == null)
            {
                return snapshot;
            }

            List<MineralDistrictRecord> restoredDistricts = new();
            if (dto.districts != null)
            {
                for (int i = 0; i < dto.districts.Count; i++)
                {
                    MineralDistrictSaveDto district = dto.districts[i];
                    if (district == null)
                    {
                        continue;
                    }

                    restoredDistricts.Add(MineralDistrictRecord.Create(
                        district.id,
                        district.kind,
                        new Vector3(district.centerX, district.centerY, district.centerZ),
                        FromSaveDto(district.bounds),
                        district.radiusMeters,
                        district.strength01,
                        district.suitability01,
                        district.freightPressure01,
                        district.settlementPressure01,
                        district.industrialBackboneSignificance01,
                        district.exportSpeculationSignificance01,
                        district.notes));
                }
            }

            List<RemoteIndustrySiteRecord> restoredSites = new();
            if (dto.remoteSites != null)
            {
                for (int i = 0; i < dto.remoteSites.Count; i++)
                {
                    RemoteIndustrySiteSaveDto site = dto.remoteSites[i];
                    if (site == null)
                    {
                        continue;
                    }

                    restoredSites.Add(RemoteIndustrySiteRecord.Create(
                        site.id,
                        site.dominantResource,
                        new Vector3(site.anchorX, site.anchorY, site.anchorZ),
                        FromSaveDto(site.anchorArea),
                        site.linkedDistrictIds,
                        site.siteStrength01,
                        site.parcelRelevance01,
                        site.futureCampRelevance01,
                        site.futureBoomtownRelevance01,
                        site.freightPressure01,
                        site.settlementPressure01,
                        site.debugLabel));
                }
            }

            snapshot.SetRecords(restoredDistricts, restoredSites);
            snapshot.SetTotals(
                dto.totalFreightPressure01,
                dto.totalSettlementPressure01,
                dto.totalRemoteDevelopmentPressure01);
            return snapshot;
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

        private static GridRect FromSaveDto(GridRectSaveDto dto)
        {
            return dto == null ? default : new GridRect(dto.xMin, dto.zMin, dto.width, dto.depth);
        }

        public int GetDistrictCount(MineralResourceKind kind)
        {
            int count = 0;
            for (int i = 0; i < Districts.Count; i++)
            {
                if (Districts[i] != null && Districts[i].Kind == kind)
                {
                    count++;
                }
            }

            return count;
        }

        public MineralDistrictRecord GetStrongestDistrict(MineralResourceKind kind)
        {
            MineralDistrictRecord best = null;
            float bestStrength = float.MinValue;
            for (int i = 0; i < Districts.Count; i++)
            {
                MineralDistrictRecord candidate = Districts[i];
                if (candidate == null || candidate.Kind != kind)
                {
                    continue;
                }

                if (best == null || candidate.Strength01 > bestStrength)
                {
                    best = candidate;
                    bestStrength = candidate.Strength01;
                }
            }

            return best;
        }

        public RemoteIndustrySiteRecord GetStrongestRemoteSite(MineralResourceKind kind)
        {
            RemoteIndustrySiteRecord best = null;
            float bestStrength = float.MinValue;
            for (int i = 0; i < RemoteSites.Count; i++)
            {
                RemoteIndustrySiteRecord candidate = RemoteSites[i];
                if (candidate == null || candidate.DominantResource != kind)
                {
                    continue;
                }

                if (best == null || candidate.SiteStrength01 > bestStrength)
                {
                    best = candidate;
                    bestStrength = candidate.SiteStrength01;
                }
            }

            return best;
        }

        public bool TryGetStrongestDistrictAt(Vector3 worldPosition, out MineralDistrictRecord district)
        {
            district = null;
            float best = float.MinValue;
            for (int i = 0; i < Districts.Count; i++)
            {
                MineralDistrictRecord candidate = Districts[i];
                if (candidate == null)
                {
                    continue;
                }

                float distance = Vector2.Distance(
                    new Vector2(worldPosition.x, worldPosition.z),
                    new Vector2(candidate.Center.x, candidate.Center.z));
                if (distance > candidate.RadiusMeters)
                {
                    continue;
                }

                float score = candidate.Strength01 - distance / Mathf.Max(1f, candidate.RadiusMeters);
                if (district == null || score > best)
                {
                    district = candidate;
                    best = score;
                }
            }

            return district != null;
        }

        public bool TryGetNearestRemoteSite(Vector3 worldPosition, float maxDistanceMeters, out RemoteIndustrySiteRecord site)
        {
            site = null;
            float bestDistance = Mathf.Max(0f, maxDistanceMeters);
            for (int i = 0; i < RemoteSites.Count; i++)
            {
                RemoteIndustrySiteRecord candidate = RemoteSites[i];
                if (candidate == null)
                {
                    continue;
                }

                float distance = Vector2.Distance(
                    new Vector2(worldPosition.x, worldPosition.z),
                    new Vector2(candidate.AnchorPosition.x, candidate.AnchorPosition.z));
                if (distance > bestDistance)
                {
                    continue;
                }

                site = candidate;
                bestDistance = distance;
            }

            return site != null;
        }

        public string BuildRegionalLinkageSummary(RegionalWorldState regionalWorld, int maxRemoteSites = 3)
        {
            StringBuilder builder = new();
            builder.AppendLine(BuildSummaryText());

            MineralDistrictRecord strongest = GetStrongestOverallDistrict();
            if (strongest != null)
            {
                builder.AppendLine(BuildDistrictRegionalContextReadout(strongest, regionalWorld));
            }

            int limit = Mathf.Clamp(maxRemoteSites, 0, RemoteSites.Count);
            for (int i = 0; i < limit; i++)
            {
                RemoteIndustrySiteRecord site = RemoteSites[i];
                if (site != null)
                {
                    builder.AppendLine(BuildRemoteSiteRegionalContextReadout(site, regionalWorld));
                }
            }

            return builder.ToString().Trim();
        }

        public string BuildDistrictRegionalContextReadout(MineralDistrictRecord district, RegionalWorldState regionalWorld)
        {
            if (district == null)
            {
                return "Mineral district linkage: no district supplied.";
            }

            Vector2 point = new(district.Center.x, district.Center.z);
            string parcelRead = BuildNearestParcelContext(regionalWorld, point, Mathf.Max(640f, district.RadiusMeters * 1.5f));
            string settlementRead = BuildNearestSettlementContext(regionalWorld, point);
            string districtKind = district.Kind is MineralResourceKind.Coal or MineralResourceKind.Iron
                ? "industrial backbone candidate"
                : "speculation and claim-diligence candidate";

            return $"Mineral district linkage: {district.Id} ({district.Kind}, {districtKind}, strength {district.Strength01:P0}, suitability {district.Suitability01:P0}); {parcelRead}; {settlementRead}; freight pressure {district.FreightPressure01:P0}; settlement pressure {district.SettlementPressure01:P0}.";
        }

        public string BuildRemoteSiteRegionalContextReadout(RemoteIndustrySiteRecord site, RegionalWorldState regionalWorld)
        {
            if (site == null)
            {
                return "Remote proto-site linkage: no site supplied.";
            }

            Vector2 point = new(site.AnchorPosition.x, site.AnchorPosition.z);
            string parcelRead = BuildNearestParcelContext(regionalWorld, point, 960f);
            string settlementRead = BuildNearestSettlementContext(regionalWorld, point);
            return $"Remote proto-site linkage: {site.Id} ({site.DominantResource}, strength {site.SiteStrength01:P0}); {parcelRead}; {settlementRead}; freight pressure {site.FreightPressure01:P0}; camp pressure {site.FutureCampRelevance01:P0}; boom/service-node pressure {site.FutureBoomtownRelevance01:P0}.";
        }

        private MineralDistrictRecord GetStrongestOverallDistrict()
        {
            MineralDistrictRecord best = null;
            float bestScore = float.MinValue;
            for (int i = 0; i < Districts.Count; i++)
            {
                MineralDistrictRecord candidate = Districts[i];
                if (candidate == null)
                {
                    continue;
                }

                float score = candidate.Strength01 + candidate.Suitability01 + candidate.FreightPressure01 * 0.35f + candidate.SettlementPressure01 * 0.25f;
                if (best == null || score > bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }

            return best;
        }

        private static string BuildNearestParcelContext(RegionalWorldState regionalWorld, Vector2 point, float maxDistanceMeters)
        {
            if (regionalWorld == null)
            {
                return "nearest parcel unavailable: no regional foundation";
            }

            if (!regionalWorld.TryFindNearestParcel(point, maxDistanceMeters, out RegionalSurveyParcelRecord parcel, out float distanceMeters) || parcel == null)
            {
                return $"nearest parcel unavailable within {Mathf.Max(0f, maxDistanceMeters):0}m";
            }

            RegionalParcelAuthorityReadout authority = RegionalParcelAuthority.Evaluate(regionalWorld, parcel);
            string hooks = authority.FutureSystemHooks.Count > 0 ? string.Join(", ", authority.FutureSystemHooks) : "general acquisition";
            return $"nearest parcel {parcel.ParcelId} at {distanceMeters:0}m; parcel authority {authority.RecommendedUseLabel}; action {authority.OwnerActionPostureLabel}; constraints {authority.ConstraintReadout}; hooks {hooks}; {authority.FlexibleUseReadout}";
        }

        private static string BuildNearestSettlementContext(RegionalWorldState regionalWorld, Vector2 point)
        {
            if (regionalWorld == null || regionalWorld.Settlements == null || regionalWorld.Settlements.Count == 0)
            {
                return "nearest node unavailable";
            }

            RegionalSettlementRecord best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < regionalWorld.Settlements.Count; i++)
            {
                RegionalSettlementRecord settlement = regionalWorld.Settlements[i];
                if (settlement == null)
                {
                    continue;
                }

                float distance = Vector2.Distance(point, settlement.CenterMeters);
                if (best == null || distance < bestDistance)
                {
                    best = settlement;
                    bestDistance = distance;
                }
            }

            return best != null
                ? $"nearest node {best.SettlementId} at {bestDistance:0}m ({best.SuccessionRole}, service pressure {best.ServiceDeficit01:P0})"
                : "nearest node unavailable";
        }

        public string BuildSummaryText()
        {
            if (!HasMeaningfulMineralOpportunity)
            {
                return "Regional resources: no meaningful mineral opportunity generated.";
            }

            StringBuilder builder = new();
            builder.Append($"Regional resources: freight {TotalFreightPressure01:P0}, settlement {TotalSettlementPressure01:P0}, remote development {TotalRemoteDevelopmentPressure01:P0}.");
            for (int i = 0; i < 4; i++)
            {
                MineralResourceKind kind = (MineralResourceKind)i;
                MineralDistrictRecord strongest = GetStrongestDistrict(kind);
                if (strongest != null)
                {
                    builder.Append($" {kind}: {GetDistrictCount(kind)} district(s), strongest {strongest.Strength01:P0}.");
                }
            }

            return builder.ToString();
        }
    }

    internal static class RegionalResourceGenerator
    {
        private struct TerrainStats
        {
            public float minHeight;
            public float maxHeight;
            public float maxSlope;
        }

        public static RegionalResourceSnapshot Generate(
            TownGrid grid,
            TownGenerationSettings settings,
            IWorldSurfaceProvider surfaceProvider = null,
            Vector3 townAnchor = default,
            float minimumTownDistanceMeters = 0f,
            float maximumResourceSlopeDegrees = 28f)
        {
            RegionalResourceSnapshot snapshot = new();
            if (grid == null || settings == null || !settings.generateMineralDistricts)
            {
                return snapshot;
            }

            TerrainStats stats = MeasureTerrain(grid);
            List<MineralDistrictRecord> districts = new();
            List<RemoteIndustrySiteRecord> sites = new();

            GenerateResourceDistricts(grid, settings, stats, MineralResourceKind.Coal, settings.coalDistrictCount, settings.coalDistrictRadiusCells, districts, surfaceProvider, townAnchor, minimumTownDistanceMeters, maximumResourceSlopeDegrees);
            GenerateResourceDistricts(grid, settings, stats, MineralResourceKind.Iron, settings.ironDistrictCount, settings.ironDistrictRadiusCells, districts, surfaceProvider, townAnchor, minimumTownDistanceMeters, maximumResourceSlopeDegrees);
            GenerateResourceDistricts(grid, settings, stats, MineralResourceKind.Gold, settings.goldDistrictCount, settings.goldDistrictRadiusCells, districts, surfaceProvider, townAnchor, minimumTownDistanceMeters, maximumResourceSlopeDegrees);
            GenerateResourceDistricts(grid, settings, stats, MineralResourceKind.Silver, settings.silverDistrictCount, settings.silverDistrictRadiusCells, districts, surfaceProvider, townAnchor, minimumTownDistanceMeters, maximumResourceSlopeDegrees);

            BuildRemoteSites(grid, settings, districts, sites, surfaceProvider, maximumResourceSlopeDegrees);

            snapshot.SetRecords(districts, sites);
            snapshot.SetTotals(
                AverageDistrictValue(districts, d => d.FreightPressure01),
                AverageDistrictValue(districts, d => d.SettlementPressure01),
                Mathf.Clamp01(AverageSiteValue(sites, s => Mathf.Max(s.ParcelRelevance01, s.SiteStrength01))));
            return snapshot;
        }

        private static void GenerateResourceDistricts(
            TownGrid grid,
            TownGenerationSettings settings,
            TerrainStats stats,
            MineralResourceKind kind,
            int districtCount,
            Vector2Int radiusRangeCells,
            List<MineralDistrictRecord> districts,
            IWorldSurfaceProvider surfaceProvider,
            Vector3 townAnchor,
            float minimumTownDistanceMeters,
            float maximumResourceSlopeDegrees)
        {
            int count = Mathf.Max(0, districtCount);
            if (count <= 0)
            {
                return;
            }

            System.Random random = new(settings.seed * 97 + (int)kind * 761);
            List<GridCoord> chosen = new();
            IReadOnlyList<Bounds> authoredAllowedZones = surfaceProvider is IWorldSurfaceResourceZones resourceZones
                ? resourceZones.GetResourceAllowedZones(kind)
                : null;
            for (int districtIndex = 0; districtIndex < count; districtIndex++)
            {
                GridCoord bestCoord = new(grid.Width / 2, grid.Depth / 2);
                float bestScore = float.MinValue;
                bool foundCandidate = false;
                int attempts = authoredAllowedZones != null && authoredAllowedZones.Count > 0
                    ? Mathf.Max(64, grid.CellCount / 160)
                    : Mathf.Max(18, grid.CellCount / 220);
                for (int attempt = 0; attempt < attempts; attempt++)
                {
                    GridCoord candidate;
                    if (authoredAllowedZones != null && authoredAllowedZones.Count > 0)
                    {
                        Bounds zone = authoredAllowedZones[random.Next(0, authoredAllowedZones.Count)];
                        Vector3 sampled = new(
                            Mathf.Lerp(zone.min.x, zone.max.x, (float)random.NextDouble()),
                            zone.center.y,
                            Mathf.Lerp(zone.min.z, zone.max.z, (float)random.NextDouble()));
                        candidate = grid.WorldToCoord(sampled);
                        if (!grid.IsInBounds(candidate))
                        {
                            continue;
                        }
                    }
                    else
                    {
                        candidate = new GridCoord(
                            random.Next(2, Mathf.Max(3, grid.Width - 2)),
                            random.Next(2, Mathf.Max(3, grid.Depth - 2)));
                    }
                    if (IsTooClose(candidate, chosen, Mathf.Max(8, radiusRangeCells.y)))
                    {
                        continue;
                    }

                    TownCell candidateCell = grid.GetCell(candidate);
                    Vector3 candidateWorld = grid.CoordToWorldCenter(candidate, candidateCell.height);
                    if ((surfaceProvider != null && candidateCell.blocked)
                        || (minimumTownDistanceMeters > 0f
                            && HorizontalDistance(candidateWorld, townAnchor) < minimumTownDistanceMeters)
                        || (surfaceProvider != null
                            && !surfaceProvider.IsResourceCompatible(
                                candidateWorld,
                                new ResourcePlacementQuery
                                {
                                    maximumSlopeDegrees = maximumResourceSlopeDegrees,
                                    resourceKind = kind
                                })))
                    {
                        continue;
                    }

                    float score = EvaluateSuitability(grid, settings, stats, kind, candidate, districtIndex);
                    if (score > bestScore)
                    {
                        bestCoord = candidate;
                        bestScore = score;
                        foundCandidate = true;
                    }
                }

                if (!foundCandidate)
                {
                    continue;
                }

                chosen.Add(bestCoord);

                int radiusCells = Mathf.Clamp(
                    Mathf.RoundToInt(Mathf.Lerp(radiusRangeCells.x, radiusRangeCells.y, Mathf.Clamp01(bestScore))),
                    Mathf.Max(2, radiusRangeCells.x),
                    Mathf.Max(radiusRangeCells.x, radiusRangeCells.y));
                float radiusMeters = radiusCells * grid.CellSizeMeters;
                TownCell cell = grid.GetCell(bestCoord);
                Vector3 center = grid.CoordToWorldCenter(bestCoord, cell.height);
                GridRect bounds = new(
                    Mathf.Clamp(bestCoord.x - radiusCells, 0, grid.Width - 1),
                    Mathf.Clamp(bestCoord.z - radiusCells, 0, grid.Depth - 1),
                    Mathf.Clamp(radiusCells * 2 + 1, 1, grid.Width),
                    Mathf.Clamp(radiusCells * 2 + 1, 1, grid.Depth));

                float strength = Mathf.Clamp01(bestScore);
                ResolveSignificance(kind, strength, out float industrial, out float export, out float freight, out float settlement);
                districts.Add(MineralDistrictRecord.Create(
                    $"{kind.ToString().ToLowerInvariant()}_{districtIndex + 1:00}",
                    kind,
                    center,
                    bounds,
                    radiusMeters,
                    strength,
                    strength,
                    freight,
                    settlement,
                    industrial,
                    export,
                    BuildDistrictNote(kind, cell, strength)));
            }
        }

        private static void BuildRemoteSites(
            TownGrid grid,
            TownGenerationSettings settings,
            IReadOnlyList<MineralDistrictRecord> districts,
            List<RemoteIndustrySiteRecord> sites,
            IWorldSurfaceProvider surfaceProvider,
            float maximumResourceSlopeDegrees)
        {
            if (districts == null || districts.Count == 0)
            {
                return;
            }

            int maxSites = Mathf.Max(1, settings.maximumRemoteProtoSites);
            List<MineralDistrictRecord> ordered = new(districts);
            ordered.Sort((left, right) => right.Strength01.CompareTo(left.Strength01));

            for (int i = 0; i < ordered.Count && sites.Count < maxSites; i++)
            {
                MineralDistrictRecord district = ordered[i];
                if (district == null || district.Strength01 < settings.minimumProtoSiteStrength01)
                {
                    continue;
                }

                GridCoord centerCoord = grid.WorldToCoord(district.Center);
                GridCoord anchorCoord = FindRoadEdgeOrFallback(grid, centerCoord, Mathf.Max(4, Mathf.RoundToInt(district.RadiusMeters / Mathf.Max(1f, grid.CellSizeMeters))));
                if (!TryResolveCompatibleRemoteSiteAnchor(
                        grid,
                        district,
                        anchorCoord,
                        surfaceProvider,
                        maximumResourceSlopeDegrees,
                        out anchorCoord))
                {
                    continue;
                }

                TownCell anchorCell = grid.GetCell(anchorCoord);
                Vector3 anchor = grid.CoordToWorldCenter(anchorCoord, anchorCell.height);
                float siteStrength = Mathf.Clamp01(district.Strength01 * 0.92f);
                sites.Add(RemoteIndustrySiteRecord.Create(
                    $"{district.Id}_site",
                    district.Kind,
                    anchor,
                    new GridRect(Mathf.Max(0, anchorCoord.x - 1), Mathf.Max(0, anchorCoord.z - 1), 3, 3),
                    new[] { district.Id },
                    siteStrength,
                    Mathf.Clamp01(siteStrength * 0.82f),
                    Mathf.Clamp01(siteStrength * (district.Kind is MineralResourceKind.Gold or MineralResourceKind.Silver ? 0.72f : 0.58f)),
                    Mathf.Clamp01(siteStrength * (district.Kind is MineralResourceKind.Gold or MineralResourceKind.Silver ? 0.84f : 0.48f)),
                    Mathf.Clamp01(district.FreightPressure01 * 0.94f),
                    Mathf.Clamp01(district.SettlementPressure01 * 0.94f),
                    BuildSiteLabel(district)));
            }
        }

        private static bool TryResolveCompatibleRemoteSiteAnchor(
            TownGrid grid,
            MineralDistrictRecord district,
            GridCoord preferred,
            IWorldSurfaceProvider surfaceProvider,
            float maximumResourceSlopeDegrees,
            out GridCoord resolved)
        {
            resolved = preferred;
            if (IsCompatibleRemoteSiteCell(grid, district, preferred, surfaceProvider, maximumResourceSlopeDegrees))
            {
                return true;
            }

            int searchRadius = Mathf.Max(2, Mathf.RoundToInt(district.RadiusMeters / Mathf.Max(0.1f, grid.CellSizeMeters)));
            GridCoord center = grid.WorldToCoord(district.Center);
            for (int distance = 0; distance <= searchRadius; distance++)
            {
                for (int zOffset = -distance; zOffset <= distance; zOffset++)
                {
                    int xOffset = distance - Mathf.Abs(zOffset);
                    GridCoord left = new(center.x - xOffset, center.z + zOffset);
                    if (IsCompatibleRemoteSiteCell(grid, district, left, surfaceProvider, maximumResourceSlopeDegrees))
                    {
                        resolved = left;
                        return true;
                    }

                    if (xOffset == 0)
                    {
                        continue;
                    }

                    GridCoord right = new(center.x + xOffset, center.z + zOffset);
                    if (IsCompatibleRemoteSiteCell(grid, district, right, surfaceProvider, maximumResourceSlopeDegrees))
                    {
                        resolved = right;
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsCompatibleRemoteSiteCell(
            TownGrid grid,
            MineralDistrictRecord district,
            GridCoord coord,
            IWorldSurfaceProvider surfaceProvider,
            float maximumResourceSlopeDegrees)
        {
            if (grid == null || district == null || !grid.IsInBounds(coord))
            {
                return false;
            }

            TownCell cell = grid.GetCell(coord);
            if (cell.blocked)
            {
                return false;
            }

            if (surfaceProvider == null)
            {
                return true;
            }

            Vector3 position = grid.CoordToWorldCenter(coord, cell.height);
            return surfaceProvider.IsResourceCompatible(
                position,
                new ResourcePlacementQuery
                {
                    maximumSlopeDegrees = maximumResourceSlopeDegrees,
                    resourceKind = district.Kind
                });
        }

        private static TerrainStats MeasureTerrain(TownGrid grid)
        {
            TerrainStats stats = new()
            {
                minHeight = float.MaxValue,
                maxHeight = float.MinValue,
                maxSlope = 0.01f
            };

            for (int z = 0; z < grid.Depth; z++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    TownCell cell = grid.GetCell(new GridCoord(x, z));
                    stats.minHeight = Mathf.Min(stats.minHeight, cell.height);
                    stats.maxHeight = Mathf.Max(stats.maxHeight, cell.height);
                    stats.maxSlope = Mathf.Max(stats.maxSlope, cell.slopeDegrees);
                }
            }

            if (stats.minHeight == float.MaxValue)
            {
                stats.minHeight = 0f;
                stats.maxHeight = 0f;
            }

            return stats;
        }

        private static float EvaluateSuitability(
            TownGrid grid,
            TownGenerationSettings settings,
            TerrainStats stats,
            MineralResourceKind kind,
            GridCoord coord,
            int districtIndex)
        {
            TownCell cell = grid.GetCell(coord);
            float height01 = Mathf.InverseLerp(stats.minHeight, stats.maxHeight, cell.height);
            float slope01 = Mathf.Clamp01(cell.slopeDegrees / Mathf.Max(1f, stats.maxSlope));
            float rugged01 = Mathf.Clamp01(slope01 * 0.7f + Mathf.Abs(height01 - 0.5f) * 0.4f);
            float drainage01 = Mathf.Clamp01(1f - Mathf.Abs(coord.z - grid.Depth * 0.5f) / Mathf.Max(1f, grid.Depth * 0.5f));
            float edgeRidge01 = Mathf.Clamp01(Mathf.Abs(coord.x - grid.Width * 0.5f) / Mathf.Max(1f, grid.Width * 0.5f) * 0.65f + slope01 * 0.35f);
            float broadBand01 = Mathf.PerlinNoise((coord.x + settings.seed * 0.11f) * 0.031f, (coord.z + districtIndex * 9f) * 0.022f);
            float secondaryBand01 = Mathf.PerlinNoise((coord.x + (int)kind * 17f) * 0.017f, (coord.z + settings.seed * 0.07f) * 0.037f);
            float buildPenalty = cell.blocked ? 0f : 0.08f;

            float score = kind switch
            {
                MineralResourceKind.Gold => height01 * 0.34f + rugged01 * 0.28f + drainage01 * 0.22f + secondaryBand01 * 0.20f + edgeRidge01 * 0.16f,
                MineralResourceKind.Silver => height01 * 0.24f + rugged01 * 0.26f + edgeRidge01 * 0.28f + broadBand01 * 0.22f + drainage01 * 0.10f,
                MineralResourceKind.Coal => (1f - height01) * 0.28f + (1f - rugged01) * 0.28f + broadBand01 * 0.28f + (1f - drainage01) * 0.18f,
                MineralResourceKind.Iron => rugged01 * 0.32f + edgeRidge01 * 0.24f + broadBand01 * 0.16f + height01 * 0.18f + secondaryBand01 * 0.16f,
                _ => broadBand01
            };

            score -= buildPenalty;
            score *= GetWeight(settings, kind);
            return Mathf.Clamp01(score);
        }

        private static void ResolveSignificance(
            MineralResourceKind kind,
            float strength,
            out float industrial,
            out float export,
            out float freight,
            out float settlement)
        {
            strength = Mathf.Clamp01(strength);
            if (kind == MineralResourceKind.Coal || kind == MineralResourceKind.Iron)
            {
                industrial = Mathf.Clamp01(0.45f + strength * 0.5f);
                export = Mathf.Clamp01(0.10f + strength * 0.18f);
                freight = Mathf.Clamp01(0.26f + strength * 0.62f);
                settlement = Mathf.Clamp01(0.16f + strength * 0.48f);
                return;
            }

            industrial = Mathf.Clamp01(0.08f + strength * 0.18f);
            export = Mathf.Clamp01(0.46f + strength * 0.48f);
            freight = Mathf.Clamp01(0.18f + strength * 0.40f);
            settlement = Mathf.Clamp01(0.20f + strength * 0.56f);
        }

        private static GridCoord FindRoadEdgeOrFallback(TownGrid grid, GridCoord origin, int searchRadius)
        {
            GridCoord best = origin;
            float bestDistance = float.MaxValue;
            int radius = Mathf.Max(2, searchRadius);
            for (int dz = -radius; dz <= radius; dz++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    GridCoord candidate = new(origin.x + dx, origin.z + dz);
                    if (!grid.IsInBounds(candidate))
                    {
                        continue;
                    }

                    TownCell cell = grid.GetCell(candidate);
                    if (!cell.IsRoad)
                    {
                        continue;
                    }

                    float distance = dx * dx + dz * dz;
                    if (distance < bestDistance)
                    {
                        best = candidate;
                        bestDistance = distance;
                    }
                }
            }

            return best;
        }

        private static bool IsTooClose(GridCoord candidate, IReadOnlyList<GridCoord> chosen, int minDistanceCells)
        {
            float minDistanceSqr = minDistanceCells * minDistanceCells;
            for (int i = 0; i < chosen.Count; i++)
            {
                GridCoord existing = chosen[i];
                float dx = candidate.x - existing.x;
                float dz = candidate.z - existing.z;
                if (dx * dx + dz * dz < minDistanceSqr)
                {
                    return true;
                }
            }

            return false;
        }

        private static float HorizontalDistance(Vector3 left, Vector3 right)
        {
            float dx = left.x - right.x;
            float dz = left.z - right.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static float GetWeight(TownGenerationSettings settings, MineralResourceKind kind)
        {
            return kind switch
            {
                MineralResourceKind.Coal => Mathf.Max(0.1f, settings.coalSuitabilityWeight),
                MineralResourceKind.Iron => Mathf.Max(0.1f, settings.ironSuitabilityWeight),
                MineralResourceKind.Gold => Mathf.Max(0.1f, settings.goldSuitabilityWeight),
                MineralResourceKind.Silver => Mathf.Max(0.1f, settings.silverSuitabilityWeight),
                _ => 1f
            };
        }

        private static string BuildDistrictNote(MineralResourceKind kind, TownCell cell, float strength)
        {
            string terrainRead = cell.blocked
                ? "hard country"
                : cell.IsRoad ? "roadside cut" : "rough open ground";
            string pressureRead = strength >= 0.68f ? "strong" : strength >= 0.42f ? "workable" : "light";
            return $"{kind} district over {terrainRead}; remote development pressure {pressureRead}.";
        }

        private static string BuildSiteLabel(MineralDistrictRecord district)
        {
            return $"{district.Kind} proto-site | freight {district.FreightPressure01:P0} | settlement {district.SettlementPressure01:P0}";
        }

        private static float AverageDistrictValue(IReadOnlyList<MineralDistrictRecord> districts, Func<MineralDistrictRecord, float> selector)
        {
            if (districts == null || districts.Count == 0)
            {
                return 0f;
            }

            float total = 0f;
            int count = 0;
            for (int i = 0; i < districts.Count; i++)
            {
                MineralDistrictRecord district = districts[i];
                if (district == null)
                {
                    continue;
                }

                total += Mathf.Clamp01(selector(district));
                count++;
            }

            return count <= 0 ? 0f : Mathf.Clamp01(total / count);
        }

        private static float AverageSiteValue(IReadOnlyList<RemoteIndustrySiteRecord> sites, Func<RemoteIndustrySiteRecord, float> selector)
        {
            if (sites == null || sites.Count == 0)
            {
                return 0f;
            }

            float total = 0f;
            int count = 0;
            for (int i = 0; i < sites.Count; i++)
            {
                RemoteIndustrySiteRecord site = sites[i];
                if (site == null)
                {
                    continue;
                }

                total += Mathf.Clamp01(selector(site));
                count++;
            }

            return count <= 0 ? 0f : Mathf.Clamp01(total / count);
        }
    }
}
