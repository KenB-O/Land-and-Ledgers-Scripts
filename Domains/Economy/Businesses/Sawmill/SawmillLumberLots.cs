using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Sawmill
{
    /// <summary>
    /// W4A: one sawn-lumber lot with the FULL provenance chain — which stand,
    /// which log lot, which mill, which sawyer, which day, which conversion
    /// profile. Extends the T1E LumberLot idea with the stand and profile
    /// links the chain needs downstream (lumber yard retail, construction
    /// purchase) without touching T1E's authority.
    /// </summary>
    [Serializable]
    public sealed class SawmillLumberLot
    {
        public EntityId LotId = EntityId.Invalid; // EntityKind.Lot (HF-1)
        public int LumberUnits;
        public string SourceLogLotId = string.Empty; // the one log lot this was sawn from
        public string StandId = string.Empty;        // the timber stand the logs were felled from
        public string Species = string.Empty;
        public string MillBusinessId = string.Empty;
        public EntityId SawedBy = EntityId.Invalid;
        public int SawedDayIndex;
        public string ConversionProfileId = string.Empty; // which SawmillConversionProfile was applied

        public SawmillLumberLot() { }

        public string ProvenanceChain()
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(StandId)) parts.Add($"stand {StandId}");
            if (!string.IsNullOrWhiteSpace(SourceLogLotId)) parts.Add($"log lot {SourceLogLotId}");
            if (!string.IsNullOrWhiteSpace(Species)) parts.Add(Species);
            if (!string.IsNullOrWhiteSpace(MillBusinessId)) parts.Add($"mill {MillBusinessId}");
            if (!string.IsNullOrWhiteSpace(ConversionProfileId)) parts.Add($"profile {ConversionProfileId}");
            parts.Add($"day {SawedDayIndex}");
            return parts.Count == 0 ? "NO PROVENANCE" : string.Join(" | ", parts.ToArray());
        }
    }

    /// <summary>
    /// W4A: one withdrawal of lumber units into a sale — the audit line of
    /// what left the yard, preserving each source lot's provenance.
    /// </summary>
    [Serializable]
    public sealed class SawmillLumberDispenseLine
    {
        public EntityId LotId = EntityId.Invalid;
        public int UnitsTaken;
        public string ProvenanceChain = string.Empty;

        public SawmillLumberDispenseLine() { }
    }

    /// <summary>
    /// W4A: the mill's finished-lumber yard — lots in, withdrawn units out,
    /// FIFO so stock ages honestly. A mill's yard never accepts another
    /// mill's lumber as its own production (that is a purchase, a different
    /// path): MillBusinessId must match, loudly otherwise.
    /// </summary>
    public sealed class SawmillLumberStock
    {
        private readonly List<string> diagnostics = new List<string>();
        private readonly List<SawmillLumberLot> lots = new List<SawmillLumberLot>();
        private readonly string millBusinessId;

        public SawmillLumberStock(string millBusinessId)
        {
            this.millBusinessId = millBusinessId ?? string.Empty;
        }

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<SawmillLumberLot> Lots => lots;

        public int TotalLumberUnits
        {
            get
            {
                int total = 0;
                foreach (var lot in lots) total += Math.Max(0, lot.LumberUnits);
                return total;
            }
        }

        /// <summary>Receives a lumber lot with full provenance. Returns the refusal, or null.</summary>
        public string ReceiveLot(SawmillLumberLot lot, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (lot == null)
                return "SawmillLumberStock.ReceiveLot: no lot offered — lumber is not conjured.";
            if (lot.LotId == EntityId.Invalid)
                return "SawmillLumberStock.ReceiveLot: a lumber lot needs an EntityId — anonymous stock is refused.";
            if (lot.LumberUnits <= 0)
                return "SawmillLumberStock.ReceiveLot: a lumber lot needs positive units.";
            if (string.IsNullOrWhiteSpace(lot.SourceLogLotId))
                return "SawmillLumberStock.ReceiveLot: no source log lot — orphan lumber refused.";
            if (string.IsNullOrWhiteSpace(lot.StandId))
                return "SawmillLumberStock.ReceiveLot: no timber stand — the full provenance chain is required.";
            if (string.IsNullOrWhiteSpace(lot.MillBusinessId))
                return "SawmillLumberStock.ReceiveLot: no mill named — orphan production refused.";
            if (!string.Equals(lot.MillBusinessId, millBusinessId, StringComparison.Ordinal))
            {
                diag.Add($"SawmillLumberStock: REFUSED lot {lot.LotId} from mill '{lot.MillBusinessId}' — "
                    + $"this yard belongs to '{millBusinessId}'. Another mill's lumber arrives as a purchase, not production.");
                return "SawmillLumberStock.ReceiveLot: foreign-mill lumber refused.";
            }

            lots.Add(lot);
            diag.Add($"SawmillLumberStock: received {lot.LumberUnits} lumber units (lot {lot.LotId}) — {lot.ProvenanceChain()}.");
            return null;
        }

        /// <summary>
        /// Withdraws up to the requested units, oldest lots first, recording
        /// the dispense lines with provenance. A shortfall returns fewer
        /// lines — never invented units.
        /// </summary>
        public List<SawmillLumberDispenseLine> TryWithdrawUnits(int units, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var lines = new List<SawmillLumberDispenseLine>();
            if (units <= 0) return lines;

            lots.Sort((a, b) =>
            {
                int day = a.SawedDayIndex.CompareTo(b.SawedDayIndex);
                return day != 0 ? day : a.LotId.ToString().CompareTo(b.LotId.ToString());
            });

            int remaining = units;
            foreach (var lot in lots)
            {
                if (remaining <= 0) break;
                int available = Math.Max(0, lot.LumberUnits);
                if (available <= 0) continue;
                int take = Math.Min(remaining, available);
                lot.LumberUnits -= take;
                remaining -= take;
                lines.Add(new SawmillLumberDispenseLine
                {
                    LotId = lot.LotId,
                    UnitsTaken = take,
                    ProvenanceChain = lot.ProvenanceChain(),
                });
            }

            lots.RemoveAll(l => l.LumberUnits <= 0);

            if (remaining > 0)
            {
                diag.Add($"SawmillLumberStock: shortfall — requested {units}, withdrew {units - remaining}. "
                    + "Empty shelves stay empty; nothing invented.");
            }
            return lines;
        }

        /// <summary>W4A save contract: lives inside the owning stock class.</summary>
        [Serializable]
        public sealed class SawmillLumberStockSaveDto
        {
            public List<SawmillLumberLot> Lots = new List<SawmillLumberLot>();
        }

        public SawmillLumberStockSaveDto CaptureSaveDto()
        {
            var dto = new SawmillLumberStockSaveDto();
            foreach (var lot in lots)
            {
                dto.Lots.Add(new SawmillLumberLot
                {
                    LotId = lot.LotId,
                    LumberUnits = lot.LumberUnits,
                    SourceLogLotId = lot.SourceLogLotId,
                    StandId = lot.StandId,
                    Species = lot.Species,
                    MillBusinessId = lot.MillBusinessId,
                    SawedBy = lot.SawedBy,
                    SawedDayIndex = lot.SawedDayIndex,
                    ConversionProfileId = lot.ConversionProfileId,
                });
            }
            return dto;
        }

        public void LoadFromSaveDto(SawmillLumberStockSaveDto dto)
        {
            lots.Clear();
            if (dto == null) return;
            foreach (var lot in dto.Lots)
            {
                if (lot == null) continue;
                lots.Add(lot);
            }
        }
    }
}
