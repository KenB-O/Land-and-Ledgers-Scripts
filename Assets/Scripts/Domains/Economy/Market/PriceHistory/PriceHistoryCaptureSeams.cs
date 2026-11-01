using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Market.PriceHistory
{
    /// <summary>
    /// D4I: capture bridge — the ONLY sanctioned entry point from business
    /// sale paths into the price history store.
    ///
    /// DESIGN DECISION (documented, not hacked in): existing sale paths were
    /// NOT modified by this package. Each business runtime (grist mill, bakery,
    /// lumber yard, general store) already records its own sale records, but
    /// none of the existing public seams carry BOTH a settlement context and
    /// the store reference needed here; threading them through would change
    /// constructor/Record* signatures on classes under active development
    /// with ambiguous behaviors routed around, not decided by this package.
    /// The runtime owners wire calls to this bridge when the sale-record ->
    /// settlement mapping exists in their context.
    ///
    /// RECOMMENDED CALL SITES (the D4J newspaper reads whatever is recorded):
    /// - Grain mill: after GristMillMerchantTrade.SellProductLot returns a
    ///   GristMillMerchantSaleResult — the sale lot id (SaleLotId) is the
    ///   SourceTransactionReference, ProductKind the commodity, the mill's
    ///   settlement the town. Canon R6: grain dealing, feed retail, and
    ///   milling are separable capabilities — record the price the actual
    ///   capability booked.
    /// - Grain mill purchases: after GristMillMerchantTrade.BuyMerchantLot —
    ///   ObservedBuyPrice side with the purchase lot id as the reference.
    /// - Bakery: after SellBread books BakerySaleRecord entries (ObservedSellPrice).
    /// - Lumber yard: after TrySellToConstructionProject / sale recording
    ///   (LumberYardSaleRecord).
    /// - General store: after retail checkout books a sale; the grain dealer
    ///   farmgate path is buy_grain_farmgate (ObservedBuyPrice).
    /// - RegionalTrade: existing PriceObservation merchant quotes can be
    ///   bridged here only when they cite a real transaction — quote lists
    ///   without a transaction reference are REFUSED, not recorded.
    ///
    /// All methods validate through PriceHistoryStore.RecordObservation:
    /// invented prices and sourceless records are refused, never stored.
    /// </summary>
    public static class PriceHistoryCaptureSeams
    {
        /// <summary>
        /// Captures one observed price from a real sale transaction. Returns
        /// true when the observation was appended. The ObservationId is
        /// derived from the source transaction reference so re-processing the
        /// same transaction is refused as a duplicate (append-only).
        /// </summary>
        public static bool TryCaptureSalePrice(
            PriceHistoryStore store,
            string settlementId,
            string settlementName,
            string commodityKind,
            int dayIndex,
            int unitPriceCents,
            int quantityUnits,
            string unitLabel,
            PriceHistoryQuoteSide quoteSide,
            string sourceTransactionReference,
            string sourceBusinessId,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (store == null)
            {
                diagnostics.Add("PriceHistoryCaptureSeams: no store — observation dropped, transaction unchanged.");
                return false;
            }
            var observation = new PriceHistoryObservation
            {
                ObservationId = "obs:" + (sourceTransactionReference ?? string.Empty),
                SettlementId = settlementId ?? string.Empty,
                SettlementName = settlementName ?? string.Empty,
                CommodityKind = commodityKind ?? string.Empty,
                DayIndex = dayIndex,
                UnitPriceCents = unitPriceCents,
                QuantityUnits = quantityUnits,
                UnitLabel = unitLabel ?? string.Empty,
                QuoteSide = quoteSide,
                SourceTransactionReference = sourceTransactionReference ?? string.Empty,
                SourceBusinessId = sourceBusinessId ?? string.Empty,
            };
            return store.RecordObservation(observation, diagnostics);
        }

        /// <summary>
        /// Appends a correction for a previously recorded observation.
        /// The original stays in the log; the new entry supersedes it.
        /// </summary>
        public static bool TryCaptureCorrection(
            PriceHistoryStore store,
            string supersededObservationId,
            string commodityKind,
            int correctedUnitPriceCents,
            string correctedUnitLabel,
            int correctedQuantityUnits,
            string correctionReason,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (store == null)
            {
                diagnostics.Add("PriceHistoryCaptureSeams: no store — correction dropped.");
                return false;
            }
            // Read the superseded entry's context back so the correction is a
            // faithful re-statement of the SAME transaction, not new data.
            PriceHistoryObservation prior = null;
            foreach (PriceHistoryObservation entry in store.AllEntries)
            {
                if (entry != null && string.Equals(entry.ObservationId, supersededObservationId, StringComparison.Ordinal))
                {
                    prior = entry;
                    break;
                }
            }
            if (prior == null)
            {
                diagnostics.Add($"PriceHistoryCaptureSeams: correction refused — unknown observation '{supersededObservationId}'.");
                return false;
            }
            var correction = new PriceHistoryObservation
            {
                ObservationId = "obs:" + prior.SourceTransactionReference + ":correction",
                SettlementId = prior.SettlementId,
                SettlementName = prior.SettlementName,
                CommodityKind = commodityKind ?? prior.CommodityKind,
                DayIndex = prior.DayIndex,
                UnitPriceCents = correctedUnitPriceCents,
                QuantityUnits = correctedQuantityUnits,
                UnitLabel = correctedUnitLabel ?? prior.UnitLabel,
                QuoteSide = prior.QuoteSide,
                SourceTransactionReference = prior.SourceTransactionReference,
                SourceBusinessId = prior.SourceBusinessId,
                SupersedesObservationId = supersededObservationId,
                Note = correctionReason ?? string.Empty,
            };
            return store.RecordObservation(correction, diagnostics);
        }
    }
}
