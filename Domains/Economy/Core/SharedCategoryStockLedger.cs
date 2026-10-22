using System;
using System.Collections.Generic;
using LandLedgers.Primitives;

namespace LandLedgers.Economy
{
    /// <summary>Pass IX WS6 (IDENT-001): how a boundary provenance record entered the world. Append-only.</summary>
    public enum StockProvenanceOriginKind
    {
        Unspecified = 0,
        /// <summary>Created at a production output seam, from named input lots.</summary>
        Production = 1,
        /// <summary>Entered from outside the town economy (imports, scenario seeding at the boundary).</summary>
        ExternalSupply = 2,
    }

    /// <summary>
    /// Pass IX WS6 (IDENT-001): one boundary lot-identity record for shared
    /// category stock. Interior category movements (the generic weekly
    /// resolver's bare counts) carry NO lot identity — records exist only
    /// where goods cross a boundary: production output and external arrival.
    /// </summary>
    [Serializable]
    public sealed class StockProvenanceRecord
    {
        public string LotId = string.Empty;
        public string CategoryId = string.Empty;
        public float Quantity;
        public StockProvenanceOriginKind OriginKind;
        /// <summary>Process id for production, shipment id for external supply.</summary>
        public string OriginRef = string.Empty;
        public string BusinessId = string.Empty;
        /// <summary>Real input lots consumed to produce this lot (production records only).</summary>
        public List<string> SourceLotIds = new List<string>();
        public int DayIndex;
        /// <summary>True once an arrival has consumed this record (external supply records).</summary>
        public bool Consumed;
        public int ConsumedDayIndex = -1;

        public StockProvenanceRecord Clone()
        {
            return new StockProvenanceRecord
            {
                LotId = LotId ?? string.Empty,
                CategoryId = CategoryId ?? string.Empty,
                Quantity = Quantity,
                OriginKind = OriginKind,
                OriginRef = OriginRef ?? string.Empty,
                BusinessId = BusinessId ?? string.Empty,
                SourceLotIds = new List<string>(SourceLotIds ?? new List<string>()),
                DayIndex = DayIndex,
                Consumed = Consumed,
                ConsumedDayIndex = ConsumedDayIndex,
            };
        }
    }

    /// <summary>
    /// Pass IX WS6 (IDENT-001): the shared category-stock ledger's
    /// provenance surface. (The brief described adding this surface to an
    /// existing SharedCategoryStockLedger; no such type exists in the real
    /// tree — CategoryStockState is the bare-count stock state — so this
    /// ledger is created new as the boundary record keeper, and
    /// CategoryStockState itself is untouched.)
    ///
    /// Boundary doctrine: production outputs are recorded with their real
    /// consumed input lots; external goods enter under an ExternalSupply
    /// record and the destination's arrival consumes that record, so every
    /// arrived lot traces to exactly one entry event. Lot ids come from
    /// the hub EntityIdRegistry allocator when one is provided; otherwise
    /// a documented NEGATIVE local sequence ("local-lot--1", "-2", ...)
    /// marks ids that no registry issued — they can never collide with a
    /// registry id, and the minus sign is the tell.
    ///
    /// Interior remainder: all interior CategoryStockState movements of
    /// the generic SharedBusinessRuntimeManager weekly resolver (inputs
    /// consumed, outputs credited as bare category counts) carry no lot
    /// identity between the production boundary and the arrival boundary;
    /// per-lot backing of every interior movement of the ~30 business
    /// archetypes is the phased remainder requiring per-archetype migration.
    /// </summary>
    public sealed class SharedCategoryStockLedger
    {
        [Serializable]
        public sealed class SharedCategoryStockLedgerSaveDto
        {
            public List<StockProvenanceRecord> provenance = new List<StockProvenanceRecord>();
            public int nextLocalLotSequence = 1;
        }

        private readonly List<StockProvenanceRecord> records = new List<StockProvenanceRecord>();
        private int nextLocalLotSequence = 1;

        public IReadOnlyList<StockProvenanceRecord> Provenance => records;

        public StockProvenanceRecord FindRecord(string lotId)
        {
            if (string.IsNullOrEmpty(lotId)) return null;
            for (int i = 0; i < records.Count; i++)
            {
                if (string.Equals(records[i].LotId, lotId, StringComparison.Ordinal))
                    return records[i];
            }
            return null;
        }

        public List<StockProvenanceRecord> FindByCategory(string categoryId)
        {
            var found = new List<StockProvenanceRecord>();
            for (int i = 0; i < records.Count; i++)
            {
                if (string.Equals(records[i].CategoryId, categoryId ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    found.Add(records[i]);
            }
            return found;
        }

        /// <summary>
        /// Records a production output at its output seam. Source lot ids
        /// are the REAL input lots the process consumed (FIFO-captured at
        /// the consumption seam); an output with no lot-tracked inputs
        /// records an empty source list — honest, never invented ancestry.
        /// </summary>
        public StockProvenanceRecord RecordProductionOutput(
            string categoryId, float quantity, string originRef, string businessId,
            IEnumerable<string> sourceLotIds, int dayIndex, EntityIdRegistry ids = null)
        {
            if (string.IsNullOrWhiteSpace(categoryId) || quantity <= 0f) return null;
            var record = new StockProvenanceRecord
            {
                LotId = AllocateLotId(ids),
                CategoryId = categoryId,
                Quantity = quantity,
                OriginKind = StockProvenanceOriginKind.Production,
                OriginRef = originRef ?? string.Empty,
                BusinessId = businessId ?? string.Empty,
                DayIndex = dayIndex,
            };
            if (sourceLotIds != null)
            {
                foreach (string sourceLotId in sourceLotIds)
                {
                    if (!string.IsNullOrWhiteSpace(sourceLotId) && !record.SourceLotIds.Contains(sourceLotId))
                        record.SourceLotIds.Add(sourceLotId);
                }
            }
            records.Add(record);
            return record;
        }

        /// <summary>
        /// Records external goods entering the economy (at dispatch/origin).
        /// The record stays unconsumed until the destination's arrival
        /// consumes it via <see cref="RecordArrival"/>.
        /// </summary>
        public StockProvenanceRecord RecordExternalSupply(
            string originRef, string categoryId, float quantity, int dayIndex, EntityIdRegistry ids = null)
        {
            if (string.IsNullOrWhiteSpace(originRef) || string.IsNullOrWhiteSpace(categoryId) || quantity <= 0f)
                return null;
            var record = new StockProvenanceRecord
            {
                LotId = AllocateLotId(ids),
                CategoryId = categoryId,
                Quantity = quantity,
                OriginKind = StockProvenanceOriginKind.ExternalSupply,
                OriginRef = originRef,
                DayIndex = dayIndex,
            };
            records.Add(record);
            return record;
        }

        /// <summary>
        /// Consumes the ExternalSupply record for an arriving shipment. When
        /// no supply record was opened at dispatch (arrival is the first
        /// sight of the goods), the goods enter under a fresh ExternalSupply
        /// record created and consumed by this call — so arrival ALWAYS
        /// consumes exactly one record, and no lot materializes recordless.
        /// </summary>
        public StockProvenanceRecord RecordArrival(
            string originRef, string categoryId, float quantity, string businessId,
            int dayIndex, EntityIdRegistry ids = null)
        {
            if (string.IsNullOrWhiteSpace(originRef)) return null;
            for (int i = 0; i < records.Count; i++)
            {
                StockProvenanceRecord candidate = records[i];
                if (candidate.OriginKind == StockProvenanceOriginKind.ExternalSupply
                    && !candidate.Consumed
                    && string.Equals(candidate.OriginRef, originRef, StringComparison.Ordinal))
                {
                    candidate.Consumed = true;
                    candidate.ConsumedDayIndex = dayIndex;
                    if (string.IsNullOrEmpty(candidate.BusinessId))
                        candidate.BusinessId = businessId ?? string.Empty;
                    return candidate;
                }
            }
            StockProvenanceRecord entered = RecordExternalSupply(originRef, categoryId, quantity, dayIndex, ids);
            if (entered == null) return null;
            entered.BusinessId = businessId ?? string.Empty;
            entered.Consumed = true;
            entered.ConsumedDayIndex = dayIndex;
            return entered;
        }

        public SharedCategoryStockLedgerSaveDto CaptureSaveDto()
        {
            var dto = new SharedCategoryStockLedgerSaveDto { nextLocalLotSequence = nextLocalLotSequence };
            for (int i = 0; i < records.Count; i++)
                dto.provenance.Add(records[i].Clone());
            return dto;
        }

        /// <summary>Clear-then-load, matching the other hub sections.</summary>
        public void LoadFromSaveDto(SharedCategoryStockLedgerSaveDto dto)
        {
            records.Clear();
            nextLocalLotSequence = 1;
            if (dto == null) return;
            if (dto.provenance != null)
            {
                for (int i = 0; i < dto.provenance.Count; i++)
                {
                    if (dto.provenance[i] != null)
                        records.Add(dto.provenance[i].Clone());
                }
            }
            nextLocalLotSequence = Math.Max(1, dto.nextLocalLotSequence);
        }

        private string AllocateLotId(EntityIdRegistry ids)
        {
            if (ids != null)
            {
                EntityId allocated = ids.Allocate(EntityKind.Lot);
                if (allocated.IsValid) return allocated.ToString();
            }
            // Documented negative local sequence: never a registry id.
            return "local-lot-" + (-(nextLocalLotSequence++));
        }
    }
}
