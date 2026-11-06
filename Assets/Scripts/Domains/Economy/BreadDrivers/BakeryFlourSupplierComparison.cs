using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Bakery;

namespace LandLedgers.Economy.BreadDrivers
{
    /// <summary>
    /// One ranked flour quote. CostPerBatchCents is goods only (the sacks
    /// a batch needs at the goods price); DeliveryCostPerShipmentCents is
    /// quoted per shipment and never amortised into a fake per-sack figure
    /// (wagon sack capacity is unauthored). TotalCostForPurchaseCents is
    /// goods for the sacks actually needed this purchase plus delivery.
    /// A missing price is a refusal, never a zero: PricePerSackCents is
    /// null when no price was supplied.
    /// </summary>
    public sealed class FlourSupplierQuote
    {
        public string SupplierId = string.Empty;
        public string DisplayName = string.Empty;
        public int? PricePerSackCents;
        public int SacksAvailable;
        public float DistanceMiles;
        public int? DeliveryCostPerShipmentCents;
        public int TransitDays;
        public bool Executable;
        public string Reason = string.Empty;
        public int? CostPerBatchCents;
        public int? TotalCostForPurchaseCents;
    }

    /// <summary>All inputs to the flour supplier comparison, explicit.</summary>
    public sealed class BakeryFlourSupplierInputs
    {
        public int SacksNeeded;
        public int StoreSacksAvailable;
        public int? StorePricePerSackCents;
        public int MillbrookSacksAvailable;
        public int? MillbrookPricePerSackCents;
        public float MillbrookOneWayMiles;
        public int? MillbrookFreightQuoteCents;
        public bool MillbrookFreightAvailable;
        public string MillbrookFreightUnavailableReason = string.Empty;
        public int? ImportPricePerLbCents;
        public int ImportTransitDays;
        public bool ImportOrderPathExists;
    }

    /// <summary>Ranked quotes plus the recommended executable source, if any.</summary>
    public sealed class BakeryFlourSupplierComparisonResult
    {
        public List<FlourSupplierQuote> RankedQuotes = new List<FlourSupplierQuote>();
        public FlourSupplierQuote Recommended;
        public string Refusal = string.Empty;
    }

    /// <summary>
    /// Plan 02 (2026-11-06, slice 2A): flour supplier comparison on actual
    /// price, quantity, distance, delivery cost and executability. Plain C#,
    /// no Unity dependency. The caller passes the freight state it can
    /// actually observe; this class never guesses whether freight can run.
    /// Rank: executable sources by total cost for this purchase (goods for
    /// the sacks needed plus delivery per shipment); non-executable sources
    /// follow in fixed order with the reason named. The 18
    /// BakeryUnitsPerOrderUnit figure is never used as a quantity anywhere
    /// here — it is preserved in provenance by the custody adapter only.
    /// </summary>
    public static class BakeryFlourSupplierComparison
    {
        /// <summary>Pounds in one store/Millbrook flour sack (authored unit "10 lb sack").</summary>
        public const int LbPerFlourSack = 10;

        /// <summary>Whole sacks a 12-lb batch needs (ceil, never a partial sack).</summary>
        public static int SacksPerBatch =>
            (BakeryBreadCatalog.BreadFlourPerBatch + LbPerFlourSack - 1) / LbPerFlourSack;

        public static BakeryFlourSupplierComparisonResult Compare(
            BakeryFlourSupplierInputs inputs, List<string> diag)
        {
            diag = diag ?? new List<string>();
            var result = new BakeryFlourSupplierComparisonResult();
            if (inputs == null)
            {
                result.Refusal = "BakeryFlourSupplierComparison: no supplier inputs — no source can be ranked.";
                diag.Add(result.Refusal);
                return result;
            }

            var store = new FlourSupplierQuote
            {
                SupplierId = "general-store",
                DisplayName = "General Store (local)",
                PricePerSackCents = inputs.StorePricePerSackCents,
                SacksAvailable = Math.Max(0, inputs.StoreSacksAvailable),
                DistanceMiles = 0f,
                DeliveryCostPerShipmentCents = 0,
                TransitDays = 0,
            };
            if (!inputs.StorePricePerSackCents.HasValue || inputs.StorePricePerSackCents.Value <= 0)
            {
                store.Executable = false;
                store.Reason = "no live store flour price — a missing price is a refusal, never a zero";
            }
            else if (store.SacksAvailable <= 0)
            {
                store.Executable = false;
                store.Reason = "store holds 0 sacks of flour";
                store.CostPerBatchCents = SacksPerBatch * inputs.StorePricePerSackCents.Value;
            }
            else
            {
                store.Executable = true;
                store.Reason = "executable — in-town counter purchase, same day, no freight";
                store.CostPerBatchCents = SacksPerBatch * inputs.StorePricePerSackCents.Value;
                store.TotalCostForPurchaseCents = Math.Max(0, inputs.SacksNeeded) * inputs.StorePricePerSackCents.Value;
            }

            var millbrook = new FlourSupplierQuote
            {
                SupplierId = "millbrook_grain_co",
                DisplayName = "Millbrook Grain Co. (off-map)",
                PricePerSackCents = inputs.MillbrookPricePerSackCents,
                SacksAvailable = Math.Max(0, inputs.MillbrookSacksAvailable),
                DistanceMiles = Math.Max(0f, inputs.MillbrookOneWayMiles),
                DeliveryCostPerShipmentCents = inputs.MillbrookFreightQuoteCents,
                TransitDays = 1,
            };
            if (!inputs.MillbrookPricePerSackCents.HasValue || inputs.MillbrookPricePerSackCents.Value <= 0)
            {
                millbrook.Executable = false;
                millbrook.Reason = "no Millbrook flour price — a missing price is a refusal, never a zero";
            }
            else if (millbrook.SacksAvailable <= 0)
            {
                millbrook.Executable = false;
                millbrook.Reason = "Millbrook holds 0 sacks of flour";
                millbrook.CostPerBatchCents = SacksPerBatch * inputs.MillbrookPricePerSackCents.Value;
            }
            else if (!inputs.MillbrookFreightAvailable)
            {
                millbrook.Executable = false;
                millbrook.Reason = string.IsNullOrWhiteSpace(inputs.MillbrookFreightUnavailableReason)
                    ? "freight unavailable — no delivery path for a Millbrook lot today"
                    : inputs.MillbrookFreightUnavailableReason;
                millbrook.CostPerBatchCents = SacksPerBatch * inputs.MillbrookPricePerSackCents.Value;
            }
            else if (!inputs.MillbrookFreightQuoteCents.HasValue)
            {
                millbrook.Executable = false;
                millbrook.Reason = "no freight quote for the Millbrook route — delivery cost unknown, never guessed";
                millbrook.CostPerBatchCents = SacksPerBatch * inputs.MillbrookPricePerSackCents.Value;
            }
            else
            {
                millbrook.Executable = true;
                millbrook.Reason = "executable — freight available, D+1 pickup";
                millbrook.CostPerBatchCents = SacksPerBatch * inputs.MillbrookPricePerSackCents.Value;
                millbrook.TotalCostForPurchaseCents = Math.Max(0, inputs.SacksNeeded) * inputs.MillbrookPricePerSackCents.Value
                    + inputs.MillbrookFreightQuoteCents.Value;
            }

            var import = new FlourSupplierQuote
            {
                SupplierId = "import-railhead",
                DisplayName = "Off-map import (railhead)",
                PricePerSackCents = null,
                SacksAvailable = 0,
                DistanceMiles = 250f,
                DeliveryCostPerShipmentCents = null,
                TransitDays = Math.Max(1, inputs.ImportTransitDays),
            };
            if (inputs.ImportPricePerLbCents.HasValue && inputs.ImportPricePerLbCents.Value > 0)
            {
                import.CostPerBatchCents =
                    BakeryBreadCatalog.BreadFlourPerBatch * inputs.ImportPricePerLbCents.Value;
            }
            if (!inputs.ImportOrderPathExists)
            {
                import.Executable = false;
                import.Reason = "catalog-registered, no live order path";
            }
            else if (!inputs.ImportPricePerLbCents.HasValue || inputs.ImportPricePerLbCents.Value <= 0)
            {
                import.Executable = false;
                import.Reason = "no import flour price — a missing price is a refusal, never a zero";
            }
            else
            {
                import.Executable = true;
                import.Reason = "executable — import order path exists";
                import.TotalCostForPurchaseCents =
                    Math.Max(0, inputs.SacksNeeded) * LbPerFlourSack * inputs.ImportPricePerLbCents.Value;
            }

            var executable = new List<FlourSupplierQuote>();
            var unavailable = new List<FlourSupplierQuote>();
            foreach (var quote in new[] { store, millbrook, import })
            {
                if (quote.Executable) executable.Add(quote);
                else unavailable.Add(quote);
            }
            executable.Sort((a, b) =>
                (a.TotalCostForPurchaseCents ?? int.MaxValue)
                    .CompareTo(b.TotalCostForPurchaseCents ?? int.MaxValue));
            result.RankedQuotes.AddRange(executable);
            result.RankedQuotes.AddRange(unavailable);

            if (inputs.SacksNeeded <= 0)
            {
                result.Refusal = "BakeryFlourSupplierComparison: no flour needed today (0 sacks) — nothing to recommend.";
            }
            else if (executable.Count > 0)
            {
                result.Recommended = executable[0];
            }
            else
            {
                result.Refusal = "BakeryFlourSupplierComparison: no executable flour source today — store, Millbrook and import are all unavailable (see ranked reasons).";
            }

            foreach (var quote in result.RankedQuotes)
            {
                diag.Add($"BakeryFlourSupplierComparison: {quote.DisplayName} — " +
                    (quote.Executable ? "executable" : "unavailable") +
                    $", price {(quote.PricePerSackCents.HasValue ? quote.PricePerSackCents.Value + "c/sack" : "none")}" +
                    $", {quote.SacksAvailable} sack(s) available" +
                    (quote.DeliveryCostPerShipmentCents.HasValue
                        ? $", delivery {quote.DeliveryCostPerShipmentCents.Value}c/shipment"
                        : ", delivery unquoted") +
                    (quote.CostPerBatchCents.HasValue ? $", {quote.CostPerBatchCents.Value}c/batch goods" : "") +
                    $"; {quote.Reason}.");
            }
            if (result.Recommended != null)
                diag.Add($"BakeryFlourSupplierComparison: recommended {result.Recommended.DisplayName}.");
            else
                diag.Add(result.Refusal);
            return result;
        }
    }
}
