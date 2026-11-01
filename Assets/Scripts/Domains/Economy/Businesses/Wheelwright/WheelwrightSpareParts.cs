using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy.Wheelwright
{
    /// <summary>
    /// D3A: the spare-parts shelf. Canon ground: the wagon-repairer occupation
    /// row lists "spare hardware/components" as CORE tools and "stocked
    /// standardized replacement parts" as optional equipment (Canon XXIII
    /// occupation table); §7.2F says repair shops hold real wagon components
    /// and may carry expensive slow-moving parts; §6.5G says spare parts are
    /// real inventory that ties up cash but turns a multi-week outside-order
    /// delay into an immediate repair.
    ///
    /// The shelf holds FINISHED components the wheelwright fabricates
    /// (wheels, axles — wooden parts; iron tires stay smith-supplied, Canon
    /// §7.2B overlap). Each batch carries its unit cost basis (lumber +
    /// ironwork at fabrication) and provenance, so cash tied up is real and
    /// dead inventory is detectable.
    /// </summary>
    [Serializable]
    public sealed class SparePartBatch
    {
        public string PartKind = string.Empty;      // "wagon-wheel", "wagon-axle"
        public string DisplayName = string.Empty;
        public int Units;
        public int UnitCostCents;                   // shop's cost basis per unit
        public int ReceivedDayIndex;
        public string ProvenanceNote = string.Empty; // lots / salvaged-from asset

        public SparePartBatch() { }

        public SparePartBatch(string partKind, string displayName, int units,
            int unitCostCents, int receivedDayIndex, string provenanceNote)
        {
            PartKind = partKind ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Units = Math.Max(0, units);
            UnitCostCents = Math.Max(0, unitCostCents);
            ReceivedDayIndex = receivedDayIndex;
            ProvenanceNote = provenanceNote ?? string.Empty;
        }
    }

    /// <summary>D3A: the parts shelf itself — FIFO consumption, slow-moving and dead-inventory detection.</summary>
    public sealed class WheelwrightSparePartsShelf
    {
        /// <summary>Component kinds the wheelwright fabricates for the shelf (wooden parts only).</summary>
        public static readonly string[] SparePartKinds = { "wagon-wheel", "wagon-axle" };

        /// <summary>Calibration: a part unsold past this age is slow-moving. Tuning, not canon.</summary>
        public const int SlowMovingDays = 60;

        /// <summary>Calibration: a part unsold past this age is dead inventory (Canon §7.2F). Tuning, not canon.</summary>
        public const int DeadInventoryDays = 180;

        private readonly List<SparePartBatch> batches = new List<SparePartBatch>();

        public IReadOnlyList<SparePartBatch> Batches => batches;

        public static bool IsSparePartKind(string partKind)
        {
            foreach (string k in SparePartKinds)
                if (string.Equals(k, partKind, StringComparison.Ordinal))
                    return true;
            return false;
        }

        public void ReceiveBatch(SparePartBatch batch, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (batch == null || batch.Units <= 0)
            {
                diagnostics.Add("WheelwrightSparePartsShelf: cannot receive an empty batch.");
                return;
            }
            if (!IsSparePartKind(batch.PartKind))
            {
                diagnostics.Add($"WheelwrightSparePartsShelf: '{batch.PartKind}' is not a stocked part kind — refused.");
                return;
            }
            batches.Add(batch);
            diagnostics.Add(
                $"WheelwrightSparePartsShelf: received {batch.Units}x {batch.DisplayName} " +
                $"at {batch.UnitCostCents}c/unit ({batch.ProvenanceNote}).");
        }

        public int UnitsOnHand(string partKind)
        {
            int total = 0;
            foreach (SparePartBatch b in batches)
                if (string.Equals(b.PartKind, partKind, StringComparison.Ordinal))
                    total += b.Units;
            return total;
        }

        /// <summary>Cash tied up in the shelf right now (Canon §6.5G: parts tie up cash).</summary>
        public int CashTiedUpCents()
        {
            int total = 0;
            foreach (SparePartBatch b in batches)
                total += b.Units * b.UnitCostCents;
            return total;
        }

        /// <summary>
        /// Consumes one unit FIFO (oldest batch first). Returns the batch it
        /// came from (for cost-basis / provenance), or null when the shelf is bare.
        /// </summary>
        public SparePartBatch ConsumeOne(string partKind)
        {
            SparePartBatch oldest = null;
            foreach (SparePartBatch b in batches)
            {
                if (!string.Equals(b.PartKind, partKind, StringComparison.Ordinal)) continue;
                if (b.Units <= 0) continue;
                if (oldest == null || b.ReceivedDayIndex < oldest.ReceivedDayIndex)
                    oldest = b;
            }
            if (oldest == null) return null;
            oldest.Units--;
            if (oldest.Units <= 0)
                batches.Remove(oldest);
            return oldest;
        }

        /// <summary>Part kinds with units older than the slow-moving window (Canon §7.2F).</summary>
        public List<string> SlowMovingKinds(int asOfDayIndex, int slowDays = SlowMovingDays)
        {
            var kinds = new List<string>();
            foreach (string kind in SparePartKinds)
            {
                foreach (SparePartBatch b in batches)
                {
                    if (!string.Equals(b.PartKind, kind, StringComparison.Ordinal)) continue;
                    if (b.Units > 0 && asOfDayIndex - b.ReceivedDayIndex >= slowDays)
                    {
                        kinds.Add(kind);
                        break;
                    }
                }
            }
            return kinds;
        }

        /// <summary>Part kinds with units older than the dead-inventory window (Canon §7.2F).</summary>
        public List<string> DeadInventoryKinds(int asOfDayIndex, int deadDays = DeadInventoryDays)
        {
            var kinds = new List<string>();
            foreach (string kind in SparePartKinds)
            {
                foreach (SparePartBatch b in batches)
                {
                    if (!string.Equals(b.PartKind, kind, StringComparison.Ordinal)) continue;
                    if (b.Units > 0 && asOfDayIndex - b.ReceivedDayIndex >= deadDays)
                    {
                        kinds.Add(kind);
                        break;
                    }
                }
            }
            return kinds;
        }

        // ---------- save DTO (inside the owning shelf class) ----------

        [Serializable]
        public sealed class SparePartsShelfDto
        {
            public List<SparePartBatch> Batches = new List<SparePartBatch>();
        }

        public SparePartsShelfDto ToSaveDto()
        {
            var dto = new SparePartsShelfDto();
            dto.Batches.AddRange(batches);
            return dto;
        }

        public void LoadFromSaveDto(SparePartsShelfDto dto)
        {
            batches.Clear();
            if (dto == null) return;
            foreach (SparePartBatch b in dto.Batches)
                if (b != null && b.Units > 0 && IsSparePartKind(b.PartKind))
                    batches.Add(b);
        }
    }
}
