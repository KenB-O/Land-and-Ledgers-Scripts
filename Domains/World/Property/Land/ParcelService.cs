using System;
using System.Collections.Generic;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.World.Property
{
    /// <summary>
    /// Pass III land core (Worker A): the legal-parcel registry.
    ///
    /// Authority: this service owns ParcelRecord/HoldingRecord identity, geometry
    /// references and lineage. Legal OWNERSHIP truth stays in TitleAuthority's
    /// chain; non-title rights stay in PropertyInterestStore. This service never
    /// answers "who owns it" — it answers "what/where is the legal parcel".
    ///
    /// Canon: XXXI Part VI §§6.1-6.8 (property/settlement); preserved Rev XIV
    /// §3.1A (survey frame, parcels, legal geography, provenance); preserved
    /// Rev XVIII System Audit 04 §5 — "Parcel = legal/property geography.
    /// Building/Improvement = physical structure." Do not collapse these layers.
    /// Rev XXIII §1.1 (one authority per consequential fact).
    ///
    /// Spatial substrate: the hidden two-metre grid already exists
    /// (TownGrid/TownCell/GridCoord; Tech XIV preserved Rev I §16.1, Rev XIV
    /// §3.1B). Cell→parcel resolves via the existing TownCell.plotId, indexed
    /// here against ParcelRecord.PlotId. No per-cell objects are serialized.
    /// </summary>
    public sealed class ParcelService
    {
        /// <summary>Cell edge length (m) used when deriving area from footprints.</summary>
        public const float DefaultCellSizeMeters = 2f;

        private readonly Dictionary<string, ParcelRecord> parcels =
            new Dictionary<string, ParcelRecord>(StringComparer.Ordinal);
        private readonly Dictionary<string, HoldingRecord> holdings =
            new Dictionary<string, HoldingRecord>(StringComparer.Ordinal);
        // Physical linkage index: town plot id (as written on ParcelRecord.PlotId) -> parcel id.
        private readonly Dictionary<string, string> parcelByPlotKey =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>
        /// Optional spatial substrate. When set, ParcelAtLocation resolves a grid
        /// coordinate to its parcel through TownCell.plotId. Null = no spatial queries.
        /// </summary>
        public TownGrid Grid { get; set; }

        public static string NewParcelId() => "parcel-" + Guid.NewGuid().ToString("N");
        public static string NewHoldingId() => "holding-" + Guid.NewGuid().ToString("N");

        public int ParcelCount => parcels.Count;
        public int HoldingCount => holdings.Count;

        /// <summary>
        /// Creates a legal parcel. A parcel has persistent identity independent of
        /// its owner (owner changes never create a new parcel identity).
        /// Area derives from footprint cells when not supplied (2 m grid).
        /// </summary>
        public ParcelRecord CreateParcel(
            string worldId,
            List<GridRect> footprint,
            string surveyDescription,
            string parcelUse,
            string jurisdiction,
            LandStatus status,
            string plotId,
            string accessNotes,
            double areaSquareMeters = 0,
            float cellSizeMeters = DefaultCellSizeMeters)
        {
            var record = new ParcelRecord
            {
                Id = NewParcelId(),
                WorldId = worldId ?? string.Empty,
                Footprint = footprint != null ? new List<GridRect>(footprint) : new List<GridRect>(),
                SurveyDescription = surveyDescription ?? string.Empty,
                ParcelUse = parcelUse ?? string.Empty,
                Jurisdiction = jurisdiction ?? string.Empty,
                Status = status,
                PlotId = plotId ?? string.Empty,
                AccessNotes = accessNotes ?? string.Empty,
            };
            record.AreaSquareMeters = areaSquareMeters > 0
                ? areaSquareMeters
                : FootprintCellCount(record.Footprint) * cellSizeMeters * cellSizeMeters;
            RegisterParcelRecord(record);
            return record;
        }

        /// <summary>
        /// Registers an already-constructed record (subdivision children, save/load,
        /// migration). First-wins on id: re-registering an existing id is a no-op
        /// so save/load replay cannot duplicate parcels.
        /// </summary>
        public void RegisterParcelRecord(ParcelRecord record)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.Id))
            {
                diagnostics.Add("ParcelService: refused to register a parcel with no id.");
                return;
            }
            if (parcels.ContainsKey(record.Id)) return;
            parcels[record.Id] = record;
            IndexPlotLink(record);
        }

        /// <summary>Removes a parcel (rollback path only — history is otherwise never deleted).</summary>
        public bool RemoveParcel(string parcelId)
        {
            if (!parcels.TryGetValue(parcelId, out ParcelRecord record)) return false;
            parcels.Remove(parcelId);
            if (!string.IsNullOrWhiteSpace(record.PlotId) &&
                parcelByPlotKey.TryGetValue(record.PlotId, out string mapped) &&
                string.Equals(mapped, parcelId, StringComparison.Ordinal))
            {
                parcelByPlotKey.Remove(record.PlotId);
            }
            return true;
        }

        public ParcelRecord GetParcel(string parcelId)
        {
            return !string.IsNullOrWhiteSpace(parcelId) && parcels.TryGetValue(parcelId, out ParcelRecord r) ? r : null;
        }

        /// <summary>Parcel linked to a physical town plot (TownPlot.id as written on PlotId).</summary>
        public ParcelRecord ParcelForPlot(string plotKey)
        {
            if (string.IsNullOrWhiteSpace(plotKey)) return null;
            return parcelByPlotKey.TryGetValue(plotKey, out string parcelId) ? GetParcel(parcelId) : null;
        }

        public ParcelRecord ParcelForPlot(int plotId) => ParcelForPlot(plotId.ToString());

        /// <summary>
        /// Resolves a grid coordinate to its legal parcel via the existing
        /// TownCell.plotId (O(1); the cell array IS the spatial index).
        /// Returns null when no grid is attached, the coordinate is out of
        /// bounds, the cell has no plot, or no parcel links that plot.
        /// </summary>
        public ParcelRecord ParcelAtLocation(GridCoord coord)
        {
            TownGrid grid = Grid;
            if (grid == null || !grid.IsInBounds(coord)) return null;
            int plotId = grid.GetCell(coord).plotId;
            if (plotId < 0) return null;
            return ParcelForPlot(plotId);
        }

        public IReadOnlyList<ParcelRecord> ParcelsWithStatus(LandStatus status)
        {
            var result = new List<ParcelRecord>();
            foreach (ParcelRecord record in parcels.Values)
                if (record.Status == status && !record.IsRetired) result.Add(record);
            return result;
        }

        public HoldingRecord CreateHolding(string name, string notes)
        {
            var holding = new HoldingRecord
            {
                Id = NewHoldingId(),
                Name = name ?? string.Empty,
                Notes = notes ?? string.Empty,
            };
            if (!holdings.ContainsKey(holding.Id)) holdings[holding.Id] = holding;
            return holding;
        }

        public HoldingRecord GetHolding(string holdingId)
        {
            return !string.IsNullOrWhiteSpace(holdingId) && holdings.TryGetValue(holdingId, out HoldingRecord h) ? h : null;
        }

        /// <summary>
        /// Groups a parcel under a holding. Grouping only — the holding never
        /// merges legal titles and ownership may differ per parcel.
        /// </summary>
        public bool AddParcelToHolding(string holdingId, string parcelId)
        {
            HoldingRecord holding = GetHolding(holdingId);
            ParcelRecord parcel = GetParcel(parcelId);
            if (holding == null || parcel == null) return false;
            if (!holding.ParcelIds.Contains(parcelId)) holding.ParcelIds.Add(parcelId);
            return true;
        }

        public bool RemoveParcelFromHolding(string holdingId, string parcelId)
        {
            HoldingRecord holding = GetHolding(holdingId);
            if (holding == null) return false;
            return holding.ParcelIds.Remove(parcelId);
        }

        public IReadOnlyList<ParcelRecord> ParcelsInHolding(string holdingId)
        {
            var result = new List<ParcelRecord>();
            HoldingRecord holding = GetHolding(holdingId);
            if (holding == null) return result;
            foreach (string parcelId in holding.ParcelIds)
            {
                ParcelRecord parcel = GetParcel(parcelId);
                if (parcel != null) result.Add(parcel);
            }
            return result;
        }

        public static long FootprintCellCount(List<GridRect> footprint)
        {
            long count = 0;
            if (footprint == null) return 0;
            foreach (GridRect rect in footprint)
                if (rect.IsValid) count += (long)rect.Area;
            return count;
        }

        private void IndexPlotLink(ParcelRecord record)
        {
            if (string.IsNullOrWhiteSpace(record.PlotId)) return;
            if (parcelByPlotKey.TryGetValue(record.PlotId, out string existing) &&
                !string.Equals(existing, record.Id, StringComparison.Ordinal))
            {
                diagnostics.Add($"ParcelService: plot link '{record.PlotId}' already maps to parcel '{existing}' — " +
                    $"parcel '{record.Id}' not indexed (first-wins; resolve the duplicate linkage).");
                return;
            }
            parcelByPlotKey[record.PlotId] = record.Id;
        }

        #region Save / Load
        public ParcelServiceSaveDto CaptureSaveDto()
        {
            return new ParcelServiceSaveDto
            {
                parcels = new List<ParcelRecord>(parcels.Values),
                holdings = new List<HoldingRecord>(holdings.Values),
            };
        }

        /// <summary>Clear-then-load: reloading never duplicates parcels or holdings.</summary>
        public void LoadFromSaveDto(ParcelServiceSaveDto dto)
        {
            parcels.Clear();
            holdings.Clear();
            parcelByPlotKey.Clear();
            if (dto == null) return;
            if (dto.parcels != null)
                foreach (ParcelRecord record in dto.parcels)
                    RegisterParcelRecord(record);
            if (dto.holdings != null)
                foreach (HoldingRecord holding in dto.holdings)
                    if (holding != null && !string.IsNullOrWhiteSpace(holding.Id) && !holdings.ContainsKey(holding.Id))
                        holdings[holding.Id] = holding;
        }
        #endregion
    }

    /// <summary>Save data for the legal-parcel registry (hub-registered).</summary>
    [Serializable]
    public sealed class ParcelServiceSaveDto
    {
        public List<ParcelRecord> parcels = new List<ParcelRecord>();
        public List<HoldingRecord> holdings = new List<HoldingRecord>();
    }
}
