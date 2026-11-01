using System;
using System.Collections.Generic;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Businesses.Bakery
{
    /// <summary>
    /// Pass 2 (task system): the single staged-to-sellable transfer
    /// bridge, closing F16. The staged chain's shelf and the shop
    /// runtime's shelf are different lot types (string "bread-&lt;batch&gt;"
    /// staged lots vs EntityId shop lots with condition aging), and
    /// before this pass no converter, caller, delivered-mark set, or
    /// scene owner connected them. This bridge is the caller:
    ///
    ///   1. Find the staged lot on the staged chain's shelf.
    ///   2. Convert it via BakeryShopRuntime.ReceiveTransferredBreadLot
    ///      (baked-day aging preserved, staged id recorded).
    ///   3. ONLY on success, remove the staged lot from the staged
    ///      shelf. Removal IS the delivered mark: a removed lot cannot
    ///      transfer again, and the runtime's transferred-id set guards
    ///      the other side even if a shelf is restored from a save.
    ///
    /// Stale staged lots are never transferred: a lot that is already
    /// stale in shop terms (older than one day past its baked day) is
    /// refused here, not converted into sellable stock and not silently
    /// dropped — the refusal names the lot so a driver can waste it
    /// through the staged chain's own waste path.
    /// </summary>
    public static class Pass2BakeryStagedTransfer
    {
        public static BakeryBreadLot Transfer(
            BakeryStagedProductionChain chain, BakeryShopRuntime shop,
            string stagedLotId, int dayIndex, EntityIdRegistry idRegistry, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (chain == null)
            {
                diag.Add("Pass2BakeryStagedTransfer: transfer refused — no staged chain supplied.");
                return null;
            }
            if (shop == null)
            {
                diag.Add("Pass2BakeryStagedTransfer: transfer refused — no shop runtime supplied.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(stagedLotId))
            {
                diag.Add("Pass2BakeryStagedTransfer: transfer refused — no staged lot id supplied.");
                return null;
            }

            BakeryStagedBreadLot staged = null;
            foreach (var lot in chain.BreadShelf)
            {
                if (lot != null && string.Equals(lot.LotId, stagedLotId, StringComparison.Ordinal))
                {
                    staged = lot;
                    break;
                }
            }
            if (staged == null)
            {
                diag.Add($"Pass2BakeryStagedTransfer: transfer refused — staged lot '{stagedLotId}' is not on the staged shelf (already transferred, or never stored).");
                return null;
            }
            if (dayIndex - staged.BakedDayIndex > 1)
            {
                diag.Add($"Pass2BakeryStagedTransfer: transfer refused — staged lot '{stagedLotId}' baked day {staged.BakedDayIndex} is stale by day {dayIndex}; it must go through waste, not to the sellable shelf.");
                return null;
            }

            BakeryBreadLot converted = shop.ReceiveTransferredBreadLot(staged, dayIndex, idRegistry, diag);
            if (converted == null)
            {
                // Shop refused (duplicate, unknown product, no units):
                // staged stock stays intact. Never remove on failure.
                return null;
            }

            chain.BreadShelf.Remove(staged);
            diag.Add($"Pass2BakeryStagedTransfer: staged lot '{stagedLotId}' moved to the sellable shelf as shop lot {converted.LotId}.");
            return converted;
        }
    }
}
