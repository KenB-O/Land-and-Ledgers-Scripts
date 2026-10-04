using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Market.PriceHistory
{
    /// <summary>
    /// D4I: append-only price history per (settlement, commodity). DATA ONLY.
    ///
    /// - Records observed prices from REAL recorded transactions. Nothing
    ///   here invents, models, or predicts prices (the MarketTerritory
    ///   generator core is held for Kennedy's decision).
    /// - Corrections are superseding entries (<see
    ///   cref="PriceHistoryObservation.SupersedesObservationId"/>); existing
    ///   entries are never edited.
    /// - Queries always operate on effective (non-superseded) observations.
    /// - Read API for the D4J newspaper market-report package:
    ///   <see cref="BuildMarketRow"/> /
    ///   <see cref="BuildMarketRowsForNewspaper"/>.
    ///
    /// Save contract (W5A): the save DTO lives inside this owning runtime
    /// class — <see cref="PriceHistoryStoreSaveDto"/>,
    /// <see cref="CaptureSaveDto"/>, <see cref="RestoreFromSaveDto"/>.
    /// </summary>
    public sealed class PriceHistoryStore
    {
        private readonly List<PriceHistoryObservation> entries = new List<PriceHistoryObservation>();
        private readonly HashSet<string> supersededIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> knownObservationIds = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>W5A save contract: lives inside the owning runtime class.</summary>
        [Serializable]
        public sealed class PriceHistoryStoreSaveDto
        {
            public List<PriceHistoryObservation> Entries = new List<PriceHistoryObservation>();
        }

        /// <summary>W5A save contract: lives inside the owning runtime class.</summary>
        public PriceHistoryStoreSaveDto CaptureSaveDto()
        {
            var dto = new PriceHistoryStoreSaveDto();
            foreach (PriceHistoryObservation entry in entries)
            {
                if (entry == null) continue;
                dto.Entries.Add(CopyObservation(entry));
            }
            return dto;
        }

        /// <summary>
        /// W5A save contract: lives inside the owning runtime class. Rebuilds
        /// the append-only log and re-derives the superseded set from the
        /// SupersedesObservationId links (nothing is edited, only replayed).
        /// </summary>
        public void RestoreFromSaveDto(PriceHistoryStoreSaveDto dto, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            entries.Clear();
            supersededIds.Clear();
            knownObservationIds.Clear();
            if (dto == null || dto.Entries == null)
            {
                diagnostics.Add("PriceHistoryStore: null save DTO — restored empty.");
                return;
            }
            var pendingSupersedes = new List<string>();
            foreach (PriceHistoryObservation entry in dto.Entries)
            {
                if (entry == null) continue;
                entries.Add(CopyObservation(entry));
                knownObservationIds.Add(entry.ObservationId ?? string.Empty);
                if (!string.IsNullOrEmpty(entry.SupersedesObservationId))
                    pendingSupersedes.Add(entry.SupersedesObservationId);
            }
            foreach (string supersededId in pendingSupersedes)
            {
                if (knownObservationIds.Contains(supersededId))
                    supersededIds.Add(supersededId);
                else
                    diagnostics.Add($"PriceHistoryStore: correction references unknown observation '{supersededId}' — link kept, nothing superseded.");
            }
        }

        /// <summary>
        /// Appends one observation. Refuses (returns false + diagnostic) when
        /// the record is incomplete or invented: missing source transaction
        /// reference, non-positive price, missing settlement/commodity, or a
        /// duplicate ObservationId. Upstream-provenance rule enforced here.
        /// </summary>
        public bool RecordObservation(PriceHistoryObservation observation, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (observation == null)
            {
                diagnostics.Add("PriceHistoryStore: null observation refused — nothing invented.");
                return false;
            }
            if (string.IsNullOrWhiteSpace(observation.ObservationId))
            {
                diagnostics.Add("PriceHistoryStore: observation refused — ObservationId is required.");
                return false;
            }
            if (knownObservationIds.Contains(observation.ObservationId))
            {
                diagnostics.Add($"PriceHistoryStore: observation '{observation.ObservationId}' already recorded — append-only log never re-records.");
                return false;
            }
            if (string.IsNullOrWhiteSpace(observation.SettlementId))
            {
                diagnostics.Add($"PriceHistoryStore: observation '{observation.ObservationId}' refused — SettlementId is required.");
                return false;
            }
            if (string.IsNullOrWhiteSpace(observation.CommodityKind))
            {
                diagnostics.Add($"PriceHistoryStore: observation '{observation.ObservationId}' refused — CommodityKind is required.");
                return false;
            }
            if (observation.DayIndex < 0)
            {
                diagnostics.Add($"PriceHistoryStore: observation '{observation.ObservationId}' refused — negative DayIndex.");
                return false;
            }
            if (observation.UnitPriceCents <= 0)
            {
                diagnostics.Add($"PriceHistoryStore: observation '{observation.ObservationId}' refused — price must be positive cents.");
                return false;
            }
            if (string.IsNullOrWhiteSpace(observation.SourceTransactionReference))
            {
                diagnostics.Add($"PriceHistoryStore: observation '{observation.ObservationId}' refused — SourceTransactionReference is required (no invented prices).");
                return false;
            }

            bool isCorrection = !string.IsNullOrWhiteSpace(observation.SupersedesObservationId);
            if (isCorrection && !knownObservationIds.Contains(observation.SupersedesObservationId))
            {
                diagnostics.Add($"PriceHistoryStore: correction '{observation.ObservationId}' refused — supersedes unknown observation '{observation.SupersedesObservationId}'.");
                return false;
            }

            entries.Add(CopyObservation(observation));
            knownObservationIds.Add(observation.ObservationId);
            if (isCorrection)
                supersededIds.Add(observation.SupersedesObservationId);
            return true;
        }

        /// <summary>
        /// Latest effective observation for (settlement, commodity), by
        /// DayIndex with append-order tiebreak. Null when none recorded.
        /// </summary>
        public PriceHistoryObservation GetLatest(string settlementId, string commodityKind)
        {
            PriceHistoryObservation best = null;
            foreach (PriceHistoryObservation entry in entries)
            {
                if (!IsEffective(entry, settlementId, commodityKind)) continue;
                if (best == null || entry.DayIndex > best.DayIndex)
                    best = entry;
            }
            return best == null ? null : CopyObservation(best);
        }

        /// <summary>
        /// The effective price in force on a given day: the latest observation
        /// with DayIndex &lt;= dayIndex. Null when none recorded yet.
        /// </summary>
        public PriceHistoryObservation GetPriceOnDate(string settlementId, string commodityKind, int dayIndex)
        {
            PriceHistoryObservation best = null;
            foreach (PriceHistoryObservation entry in entries)
            {
                if (!IsEffective(entry, settlementId, commodityKind)) continue;
                if (entry.DayIndex > dayIndex) continue;
                if (best == null || entry.DayIndex > best.DayIndex)
                    best = entry;
            }
            return best == null ? null : CopyObservation(best);
        }

        /// <summary>
        /// Effective observations for (settlement, commodity) in
        /// [fromDayIndex, toDayIndex], ordered by DayIndex (append-order
        /// tiebreak). Empty list when none recorded.
        /// </summary>
        public List<PriceHistoryObservation> GetPriceSeries(string settlementId, string commodityKind, int fromDayIndex, int toDayIndex)
        {
            var series = new List<PriceHistoryObservation>();
            foreach (PriceHistoryObservation entry in entries)
            {
                if (!IsEffective(entry, settlementId, commodityKind)) continue;
                if (entry.DayIndex < fromDayIndex || entry.DayIndex > toDayIndex) continue;
                series.Add(CopyObservation(entry));
            }
            series.Sort((a, b) => a.DayIndex.CompareTo(b.DayIndex));
            return series;
        }

        /// <summary>
        /// All raw log entries (including superseded) in append order.
        /// For save/audit; queries use the effective views above.
        /// </summary>
        public IReadOnlyList<PriceHistoryObservation> AllEntries => entries;

        /// <summary>Number of effective (non-superseded) observations recorded.</summary>
        public int EffectiveEntryCount
        {
            get
            {
                int count = 0;
                foreach (PriceHistoryObservation entry in entries)
                    if (entry != null && !supersededIds.Contains(entry.ObservationId ?? string.Empty))
                        count++;
                return count;
            }
        }

        /// <summary>
        /// D4J read seam: one market-report row for a single commodity.
        /// latest vs (latest on or before asOfDayIndex - lookbackDays).
        /// No prediction — pure reads over recorded history.
        /// </summary>
        public PriceHistoryMarketRow BuildMarketRow(string settlementId, string commodityKind, int asOfDayIndex, int lookbackDays)
        {
            var row = new PriceHistoryMarketRow
            {
                SettlementId = settlementId ?? string.Empty,
                CommodityKind = commodityKind ?? string.Empty,
                AsOfDayIndex = asOfDayIndex,
            };
            PriceHistoryObservation latest = GetPriceOnDate(row.SettlementId, row.CommodityKind, asOfDayIndex);
            PriceHistoryObservation earlier = lookbackDays > 0
                ? GetPriceOnDate(row.SettlementId, row.CommodityKind, asOfDayIndex - lookbackDays)
                : null;
            if (latest == null) return row;

            row.HasData = true;
            row.LatestPriceCents = latest.UnitPriceCents;
            row.LatestDayIndex = latest.DayIndex;
            row.QuoteSide = latest.QuoteSide;
            row.UnitLabel = latest.UnitLabel ?? string.Empty;
            row.SourceTransactionReference = latest.SourceTransactionReference ?? string.Empty;
            if (earlier != null && earlier.ObservationId != latest.ObservationId)
            {
                row.EarlierPriceCents = earlier.UnitPriceCents;
                row.EarlierDayIndex = earlier.DayIndex;
                row.ChangeCents = latest.UnitPriceCents - earlier.UnitPriceCents;
                row.Direction = row.ChangeCents > 0
                    ? PriceHistoryTrendDirection.Up
                    : row.ChangeCents < 0 ? PriceHistoryTrendDirection.Down : PriceHistoryTrendDirection.Steady;
            }
            return row;
        }

        /// <summary>
        /// D4J read seam: market-report rows for a list of commodities at one
        /// settlement. Skips commodities with no history.
        /// </summary>
        public List<PriceHistoryMarketRow> BuildMarketRowsForNewspaper(
            string settlementId, List<string> commodityKinds, int asOfDayIndex, int lookbackDays)
        {
            var rows = new List<PriceHistoryMarketRow>();
            if (commodityKinds == null) return rows;
            foreach (string commodityKind in commodityKinds)
            {
                if (string.IsNullOrWhiteSpace(commodityKind)) continue;
                PriceHistoryMarketRow row = BuildMarketRow(settlementId, commodityKind, asOfDayIndex, lookbackDays);
                if (row.HasData)
                    rows.Add(row);
            }
            return rows;
        }

        private bool IsEffective(PriceHistoryObservation entry, string settlementId, string commodityKind)
        {
            if (entry == null) return false;
            if (supersededIds.Contains(entry.ObservationId ?? string.Empty)) return false;
            return string.Equals(entry.SettlementId, settlementId, StringComparison.Ordinal)
                && string.Equals(entry.CommodityKind, commodityKind, StringComparison.Ordinal);
        }

        private static PriceHistoryObservation CopyObservation(PriceHistoryObservation source)
        {
            return new PriceHistoryObservation
            {
                ObservationId = source.ObservationId,
                SettlementId = source.SettlementId,
                SettlementName = source.SettlementName,
                CommodityKind = source.CommodityKind,
                DayIndex = source.DayIndex,
                UnitPriceCents = source.UnitPriceCents,
                QuantityUnits = source.QuantityUnits,
                UnitLabel = source.UnitLabel,
                QuoteSide = source.QuoteSide,
                SourceTransactionReference = source.SourceTransactionReference,
                SourceBusinessId = source.SourceBusinessId,
                SupersedesObservationId = source.SupersedesObservationId,
                Note = source.Note,
            };
        }
    }
}
