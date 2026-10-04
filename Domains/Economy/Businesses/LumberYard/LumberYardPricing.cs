using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.LumberYard
{
    /// <summary>D2C: one grade's price modifier inside the grade price policy.</summary>
    [Serializable]
    public sealed class LumberYardGradePriceEntry
    {
        public string GradeId = string.Empty;
        /// <summary>Multiplier over the grade-neutral retail price. Calibration data (Canon Part XV).</summary>
        public float Multiplier = 1f;

        public LumberYardGradePriceEntry() { }

        public LumberYardGradePriceEntry(string gradeId, float multiplier)
        {
            GradeId = gradeId ?? string.Empty;
            Multiplier = multiplier;
        }
    }

    /// <summary>
    /// D2C: the yard's retail pricing policy — DATA, not canon. Canon §5.3:
    /// "Retail pricing follows landed cost... UI may then show margin, cash
    /// burden and demand consequences of candidate prices; it should not
    /// reverse-engineer a fictional supplier price from the desired retail
    /// price." So the yard quotes FROM its real lots: FIFO landed unit cost
    /// (acquisition cost plus recorded freight/handling), marked up by the
    /// policy margin, adjusted by the yard grade sort (clear commands more,
    /// cull sells cheap — broad 1870s yard sorts, W4B grade catalog) and by an
    /// optional seasonal factor. Every number is calibration data, set by the
    /// scenario/economy authority — the canon fixes no prices.
    ///
    /// Genuine design fork (recorded, not guessed): Canon §2.2 notes a good
    /// lumber buyer understands "grades, suppliers, seasonality or freight",
    /// and the historical research annex discusses seasonal logging
    /// (winter cut) — but no canon value or research figure fixes a seasonal
    /// lumber-price factor for an 1870s Dakota yard. SeasonalMultiplier is
    /// therefore a parameter defaulting to 1.0 (no seasonal effect) until a
    /// calibrated regime is supplied.
    /// </summary>
    [Serializable]
    public sealed class LumberYardGradePricePolicy
    {
        public List<LumberYardGradePriceEntry> GradeMultipliers = new List<LumberYardGradePriceEntry>();
        /// <summary>Margin over landed unit cost, as a ratio (0.25 = 25%). Calibration.</summary>
        public float MarkupRatio = 0.25f;
        /// <summary>Seasonal price factor; 1.0 = none. Calibration; see design fork note above.</summary>
        public float SeasonalMultiplier = 1f;

        public LumberYardGradePricePolicy() { }

        public float MultiplierForGrade(string gradeId)
        {
            if (string.IsNullOrWhiteSpace(gradeId)) return 1f;
            string key = gradeId.Trim();
            foreach (var entry in GradeMultipliers)
            {
                if (entry != null && string.Equals(entry.GradeId, key, StringComparison.OrdinalIgnoreCase))
                    return entry.Multiplier;
            }
            return 1f;
        }
    }

    /// <summary>
    /// D2C: landed-cost retail quoting. Quotes only what the yard actually
    /// holds — a quote with no matching stock is refused, never priced from
    /// thin air (Canon §5.1: supplier-first; no anonymous product-price
    /// pairs). Earmarked project stock is excluded: it is not for normal sale
    /// (Canon §6.4).
    /// </summary>
    public static class LumberYardPricing
    {
        /// <summary>
        /// Quotes a per-unit retail price in cents for lumber matching the
        /// filters, derived from the FIFO landed unit cost of the yard's
        /// actual sale-available lots. Returns -1 when nothing matches (no
        /// stock, no quote). diag carries the landed-cost breakdown.
        /// </summary>
        public static int QuoteRetailUnitPriceCents(
            LumberYardLumberStock stock,
            string speciesFilter,
            string gradeFilter,
            LumberYardGradePricePolicy policy,
            List<string> diag)
        {
            if (stock == null || policy == null) return -1;

            long totalLandedCents = 0;
            long totalUnits = 0;
            foreach (var lot in stock.Lots)
            {
                if (lot == null || lot.LumberUnits <= 0) continue;
                if (!LumberYardPricingFilters.Matches(lot, speciesFilter, gradeFilter)) continue;
                int saleUnits = Math.Max(0, lot.LumberUnits - Math.Max(0, lot.EarmarkedUnits));
                if (saleUnits <= 0) continue;
                long landedPerUnit = Math.Max(0, lot.UnitCostCents)
                    + (lot.LumberUnits > 0
                        ? (Math.Max(0, lot.LandedFreightCents) + Math.Max(0, lot.LandedHandlingCents)) / lot.LumberUnits
                        : 0);
                totalLandedCents += landedPerUnit * saleUnits;
                totalUnits += saleUnits;
            }

            if (totalUnits <= 0) return -1;

            double landedUnitCost = (double)totalLandedCents / totalUnits;
            float gradeMultiplier = policy.MultiplierForGrade(gradeFilter);
            float seasonal = policy.SeasonalMultiplier <= 0f ? 1f : policy.SeasonalMultiplier;
            double price = landedUnitCost * (1.0 + Math.Max(0f, policy.MarkupRatio)) * gradeMultiplier * seasonal;
            int quoted = Math.Max(1, (int)Math.Round(price));

            if (diag != null)
            {
                diag.Add($"LumberYardPricing: quote {quoted}c/unit over {totalUnits} sale-available units "
                    + $"(landed {landedUnitCost:F1}c/unit, markup {policy.MarkupRatio:P0}, "
                    + $"grade '{(string.IsNullOrWhiteSpace(gradeFilter) ? "any" : gradeFilter.Trim())}' x{gradeMultiplier:F2}, "
                    + $"seasonal x{seasonal:F2}). Quoted from real lots, never from thin air.");
            }
            return quoted;
        }
    }

    /// <summary>D2C: shared species/grade filter matching for the pricing pass.</summary>
    internal static class LumberYardPricingFilters
    {
        internal static bool Matches(LumberYardLumberLot lot, string speciesFilter, string gradeFilter)
        {
            if (!string.IsNullOrWhiteSpace(speciesFilter)
                && !string.Equals(lot.Species, speciesFilter.Trim(), StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrWhiteSpace(gradeFilter)
                && !string.Equals(lot.YardGradeId, gradeFilter.Trim(), StringComparison.OrdinalIgnoreCase))
                return false;
            return true;
        }
    }
}
