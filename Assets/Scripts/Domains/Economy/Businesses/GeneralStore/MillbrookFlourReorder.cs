using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Businesses.GeneralStore
{
    /// <summary>
    /// Plan 02 (2026-11-06, slice 2B / Plan 10 slice E1): the repeatable
    /// Millbrook flour reorder side. The opening order (request
    /// "opening-flour-b1", 1 sack, first day only) is placed once by
    /// DriveMillbrookAidTable; this class is the only other order path, so
    /// a second (and later) paid flour lot can exist and enter the existing
    /// freight pipeline untouched. Order authority stays
    /// OffMapAidBusinessRuntime.TryPlaceOrder — the same one-cash-writer,
    /// exactly-once, finite-stock authority the opening order uses — so a
    /// reorder debits supplier stock immediately into a uniquely identified
    /// pickup lot and debits buyer cash exactly once. No staging, hitching,
    /// dispatch, departure, or custody behaviour changes here.
    /// </summary>
    public static class MillbrookFlourReorder
    {
        /// <summary>Millbrook flour SKU on the supplier runtime.</summary>
        public const string FlourSkuId = "bakery_flour_sack";

        /// <summary>Pounds in one Millbrook flour sack (authored unit "10 lb sack";
        /// the opening lot credits InventoryUnits = 10 for 1 package).</summary>
        public const int LbPerSack = 10;

        /// <summary>
        /// Places one repeatable Millbrook flour reorder. Returns null on
        /// success (lot set), or a refusal naming the blocker with nothing
        /// moved. Request id is deterministic and unique per order
        /// ("flour-reorder-d&lt;day&gt;-n&lt;k&gt;", k = count of existing lots
        /// for this buyer + 1, derived from supplier.Lots, never random).
        /// Guards: sacks &gt; 0; sacks &lt;= live available sacks
        /// (supplier stock in pounds / 10); single-outstanding-order rule —
        /// if uncommitted, unreceived lots for this buyer already cover the
        /// requested sacks, refuse naming the oldest outstanding lot instead
        /// of double-ordering (Obj 47's hazard, same shape as the notions
        /// one-order-per-day guard).
        /// </summary>
        public static string PlaceReorder(
            OffMapAidBusinessRuntime supplier,
            IBusinessCashPort buyerCash,
            string buyerBusinessId,
            string buyerName,
            int sacks,
            int dayIndex,
            List<string> diag,
            out OffMapAidCargoLot lot)
        {
            lot = null;
            diag = diag ?? new List<string>();
            if (supplier == null)
                return Refuse(diag, "Millbrook flour reorder refused: no Millbrook supplier runtime is attached.");
            if (buyerCash == null)
                return Refuse(diag, "Millbrook flour reorder refused: no buyer cash port — the order cannot be paid.");
            if (string.IsNullOrWhiteSpace(buyerBusinessId))
                return Refuse(diag, "Millbrook flour reorder refused: no buyer business id.");
            if (sacks <= 0)
                return Refuse(diag, "Millbrook flour reorder refused: sacks must be positive.");

            int availableSacks = Math.Max(0, supplier.GetInventoryUnits(FlourSkuId) / LbPerSack);
            if (sacks > availableSacks)
                return Refuse(diag, $"Millbrook flour reorder refused: Millbrook holds {availableSacks} sack(s) of flour; order requires {sacks}.");

            OffMapAidCargoLot outstanding = FindShippableLot(supplier, buyerBusinessId);
            if (outstanding != null)
            {
                int outstandingSacks = 0;
                foreach (var candidate in supplier.Lots)
                {
                    if (candidate != null
                        && string.Equals(candidate.BuyerBusinessId, buyerBusinessId, StringComparison.Ordinal)
                        && !candidate.SourceCommitted && !candidate.Received)
                        outstandingSacks += Math.Max(0, candidate.PackageUnits);
                }
                if (outstandingSacks >= sacks)
                    return Refuse(diag, $"Millbrook flour reorder refused: outstanding lot '{outstanding.LotId}' already covers {outstandingSacks} sack(s) for {buyerBusinessId}; no duplicate order placed.");
            }

            int existingLotCount = 0;
            foreach (var candidate in supplier.Lots)
            {
                if (candidate != null
                    && string.Equals(candidate.BuyerBusinessId, buyerBusinessId, StringComparison.Ordinal))
                    existingLotCount++;
            }
            string requestId = $"flour-reorder-d{Math.Max(0, dayIndex)}-n{existingLotCount + 1}";
            string refusal = supplier.TryPlaceOrder(
                requestId, buyerBusinessId, buyerName, sacks, buyerCash, dayIndex, out lot);
            if (refusal != null)
                return Refuse(diag, "Millbrook flour reorder refused: " + refusal);
            diag.Add($"MillbrookFlourReorder: placed reorder '{requestId}' for {sacks} sack(s) " +
                $"(lot '{lot.LotId}', {lot.TotalCostCents}c) for {buyerBusinessId}; enters the existing freight pipeline.");
            return null;
        }

        /// <summary>
        /// The oldest uncommitted, unreceived Millbrook lot for this buyer —
        /// the next lot the freight negotiation may ship — or null when no
        /// such lot exists. Lots are returned in placement order, so the
        /// first match is the oldest. The opening lot is found by the same
        /// predicate while it is still uncommitted.
        /// </summary>
        public static OffMapAidCargoLot FindShippableLot(OffMapAidBusinessRuntime supplier, string buyerBusinessId)
        {
            if (supplier == null || string.IsNullOrWhiteSpace(buyerBusinessId)) return null;
            foreach (var candidate in supplier.Lots)
            {
                if (candidate != null
                    && string.Equals(candidate.BuyerBusinessId, buyerBusinessId, StringComparison.Ordinal)
                    && !candidate.SourceCommitted && !candidate.Received)
                    return candidate;
            }
            return null;
        }

        private static string Refuse(List<string> diag, string message)
        {
            diag.Add(message);
            return message;
        }
    }
}
