// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Agent F, slice F3 (expanded campaign 2026-11-16): consolidated
// mixed-cargo freight — one shipment record owns a manifest of lines,
// one line per source lot, so a bed + table + four chairs ships as ONE
// shipment on ONE wagon in ONE trip instead of one trip per item.
//
// The manifest is the extension point the shipment service was missing
// (plan section 2.4): one shipment = one source lot today, and the
// exclusivity rule (one wagon, one live shipment) forbids booking the
// pieces onto one wagon by hand. The manifest fixes that inside the
// service — it extends FreightShipmentService, it never forks a second
// freight system. FreightCargoCapacity remains the single trip-math
// authority: aggregate mass/volume are validated ONCE against the wagon
// (sum of lines, not per-line) before load, exactly as CreateShipment
// validates a single cargo.
//
// Custody stays per line: each line keeps its own source lot, SKU and
// units, and the receiving authority debits/credits each source lot
// through the consolidated hooks. A failed line fails the whole receipt
// loudly — the cargo stays in wagon custody, nothing is silently
// dropped. Freight delivery is not installation: the buyer takes
// custody at delivery, and installation happens only through B0.
//
// Persistence: the manifest rides in FreightServiceSaveDto (the
// service's own save seam, the same place dispatch plans already ride),
// NOT in LogisticsShipmentSaveDto — that DTO lives in the
// harness-excluded SaveDtos.cs whose shim mirror is shared, and this
// slice must not touch the shared shim. Old saves carry no manifest
// and restore exactly as before (a consolidated shipment cannot exist
// in a save that predates the feature).

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Logistics;

namespace LandLedgers.Economy.Logistics
{
    /// <summary>
    /// One line of a consolidated manifest: one source lot of one SKU,
    /// with its own per-unit freight measures. Units are whole items;
    /// a line never splits an item.
    /// </summary>
    [Serializable]
    public sealed class FreightManifestLine
    {
        public string LotId = string.Empty;
        public string SkuId = string.Empty;
        public int Units;
        public float MassKgPerUnit;
        public float VolumeM3PerUnit;

        /// <summary>Validates one line. Returns null when the line is shippable, else a loud reason.</summary>
        public string Validate()
        {
            if (string.IsNullOrWhiteSpace(LotId))
            {
                return "FreightManifestLine: a manifest line needs a source lot id.";
            }

            if (string.IsNullOrWhiteSpace(SkuId))
            {
                return $"FreightManifestLine: lot '{LotId}' needs a SKU id.";
            }

            if (Units <= 0)
            {
                return $"FreightManifestLine: lot '{LotId}' ({SkuId}) needs a positive unit count, not {Units}.";
            }

            string specRefusal = FreightCargoCapacity.ValidateCargoSpec(new FreightCargoSpec
            {
                MassKgPerUnit = MassKgPerUnit,
                VolumeM3PerUnit = VolumeM3PerUnit,
            });
            if (specRefusal != null)
            {
                return $"FreightManifestLine: lot '{LotId}' ({SkuId}) has an invalid freight measure — {specRefusal}";
            }

            return null;
        }
    }

    /// <summary>
    /// Pure helpers over a consolidated manifest. No state, no Unity
    /// objects: the shipment service and the tests share these, so the
    /// aggregate is never re-derived two ways.
    /// </summary>
    public static class FreightConsolidatedManifest
    {
        /// <summary>Validates a whole manifest: non-empty, every line valid, no lot twice.</summary>
        public static string ValidateLines(IReadOnlyList<FreightManifestLine> lines)
        {
            if (lines == null || lines.Count == 0)
            {
                return "FreightConsolidatedManifest: a consolidated shipment needs at least one manifest line.";
            }

            var seenLots = new HashSet<string>(StringComparer.Ordinal);
            foreach (FreightManifestLine line in lines)
            {
                if (line == null)
                {
                    return "FreightConsolidatedManifest: a manifest line is null — refusing a phantom line.";
                }

                string lineRefusal = line.Validate();
                if (lineRefusal != null)
                {
                    return lineRefusal;
                }

                if (!seenLots.Add(line.LotId.Trim()))
                {
                    return $"FreightConsolidatedManifest: lot '{line.LotId}' appears twice in one manifest — one lot, one line.";
                }
            }

            return null;
        }

        public static int TotalUnits(IReadOnlyList<FreightManifestLine> lines)
        {
            int total = 0;
            if (lines == null) return total;
            foreach (FreightManifestLine line in lines)
            {
                if (line != null) total += Math.Max(0, line.Units);
            }

            return total;
        }

        public static float TotalMassKg(IReadOnlyList<FreightManifestLine> lines)
        {
            float total = 0f;
            if (lines == null) return total;
            foreach (FreightManifestLine line in lines)
            {
                if (line != null) total += Math.Max(0, line.Units) * line.MassKgPerUnit;
            }

            return total;
        }

        public static float TotalVolumeM3(IReadOnlyList<FreightManifestLine> lines)
        {
            float total = 0f;
            if (lines == null) return total;
            foreach (FreightManifestLine line in lines)
            {
                if (line != null) total += Math.Max(0, line.Units) * line.VolumeM3PerUnit;
            }

            return total;
        }

        /// <summary>
        /// Aggregate capacity check: the manifest's summed mass and volume
        /// against the wagon, in the service's own wording. Over capacity
        /// blocks pre-load with zero cargo owned, exactly as a single
        /// cargo does. A wagon that cannot carry even one unit of the
        /// manifest is a capacity refusal, not a zero-quantity shipment.
        /// </summary>
        public static string CapacityRefusal(IReadOnlyList<FreightManifestLine> lines, EquipmentAsset wagon)
        {
            if (wagon == null)
            {
                return "FreightShipmentService: registered wagon is unavailable; shipment cannot be loaded.";
            }

            float totalMassKg = TotalMassKg(lines);
            float totalVolumeM3 = TotalVolumeM3(lines);
            if (totalMassKg > wagon.PayloadMassCapacityKg + 0.001f)
            {
                return "cargo mass " + totalMassKg.ToString("0.0") + "kg exceeds wagon '" + wagon.AssetId
                    + "' capacity " + wagon.PayloadMassCapacityKg.ToString("0.0") + "kg.";
            }

            if (totalVolumeM3 > wagon.CargoVolumeCapacityM3 + 0.0001f)
            {
                return "cargo volume " + totalVolumeM3.ToString("0.00") + "m3 exceeds wagon '" + wagon.AssetId
                    + "' capacity " + wagon.CargoVolumeCapacityM3.ToString("0.00") + "m3.";
            }

            return null;
        }
    }
}
