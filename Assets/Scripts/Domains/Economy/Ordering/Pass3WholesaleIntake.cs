// Pass 3 (economy ordering): bakery bread into General Store stock,
// and a per-item offer model for multi-item categories. Plain C#,
// no UnityEngine references, Roslyn-testable.
//
// Wholesale intake, stated plainly:
//   - A bakery bread lot is received into the store's BACK storage
//     only, never straight onto the shelf. It reaches the shelf only
//     through a Pass 2 back-to-shelf move (a task), preserving the
//     shelf/back distinction this pass exists to enforce.
//   - Aging follows Pass 2's staged-transfer rule: condition is
//     derived from BAKED day vs TRANSFER day. A loaf baked yesterday
//     is day-old when transferred today, never fresh again. A lot
//     more than one day past its baked day is STALE and is refused,
//     not converted and not silently dropped.
//   - Duplicate intake is refused: the same bakery lot id can be
//     received once per intake instance. A second intake for that
//     lot moves nothing, so bread can never be double-counted by
//     re-running an intake.
//   - Cash is NOT moved here. Intake produces a settlement record
//     (store pays bakery, units x wholesale price, Settled = false)
//     that a caller must settle through the business cash ports
//     (BusinessRuntimeState). Until that settlement is confirmed,
//     the stock record and the cash record are both visible and
//     unsettled, which is honest; claiming cash moved here would
//     be inventing a payment.
//   - This is a MODEL, not a driver and not a scene wiring. No
//     production code calls it yet; a driver / adapter must call it,
//     settle it, and then move stock to shelf. Until then, bakery
//     bread reaching store counters through this path remains
//     unwired, and that fact is recorded in the review doc, not
//     hidden.
//
// Per-item offers, stated plainly:
//   - The production General Store trading port today only offers a
//     category when exactly one item resolves in it, so staple_food
//     (flour, bread, potatoes, salt, sugar, coffee, tea) is skipped
//     entirely. Pass3PerItemOffer is the TESTED MODEL a production
//     adapter must use instead: offer one named item at a time,
//     from that item's own line, never the aggregate labelled as
//     flour or as bread.
//   - This file does NOT edit GeneralStoreRuntimeManager or its
//     trading port. Editing that legacy path without migrating its
//     callers would break existing tests and callers, so the fix
//     here is a model plus a documented adapter task for Codex,
//     not a silent runtime change. The production shopping-loop
//     block for staple_food therefore REMAINS until that adapter
//     is wired; this file does not claim otherwise.
using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Ordering
{
    /// <summary>Settlement owed on a wholesale intake: store pays bakery. Settled is false until a caller confirms payment through the cash ports.</summary>
    public sealed class Pass3WholesaleSettlement
    {
        public string PayerBusinessId = string.Empty;
        public string PayeeBusinessId = string.Empty;
        public string BakeryLotId = string.Empty;
        public string ProductId = string.Empty;
        public int Units;
        public int UnitWholesalePriceCents;
        public int AmountCents;
        public bool Settled;
    }

    /// <summary>Result of a wholesale intake: the back-storage stock change and the unsettled cash record, together, so neither can be claimed without the other.</summary>
    public sealed class Pass3WholesaleIntakeResult
    {
        public string BakeryLotId = string.Empty;
        public string ProductId = string.Empty;
        public int UnitsReceived;
        public string Condition = string.Empty; // Fresh or DayOld
        public Pass3WholesaleSettlement Settlement;
    }

    /// <summary>
    /// Wholesale intake instance: holds the delivered-lot guard, so
    /// duplicate intake is refused per instance. A production driver
    /// must persist delivered lot ids across saves (like the Pass XL
    /// delivered-sale set) before this model is production-safe;
    /// that persistence wiring is a Codex task, listed in the review.
    /// </summary>
    public sealed class Pass3WholesaleIntake
    {
        private readonly HashSet<string> deliveredLotIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Receive bakery bread into store back storage. Refuses bad
        /// lots, non-positive units, negative prices, stale lots, and
        /// duplicate lot ids, moving nothing on any refusal. On
        /// success the line is created if absent (cost = wholesale
        /// price, price initially = wholesale price, owner must set
        /// a retail price/margin via an instant decision before any
        /// shelf sale), back units grow, shelf units do not change,
        /// and a settlement record is returned unsettled.
        /// </summary>
        public Pass3WholesaleIntakeResult Receive(
            string bakeryLotId, string productId, int units, int wholesalePriceCents,
            int bakedDayIndex, int transferDayIndex, Pass3StoreCatalog storeCatalog,
            string payerBusinessId, string payeeBusinessId, List<string> diag)
        {
            if (string.IsNullOrWhiteSpace(bakeryLotId) || string.IsNullOrWhiteSpace(productId) || storeCatalog == null)
            {
                if (diag != null) diag.Add("Pass3Ordering: wholesale intake needs a bakery lot id, a product id, and a store catalog.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(payerBusinessId) || string.IsNullOrWhiteSpace(payeeBusinessId)
                || string.Equals(payerBusinessId.Trim(), payeeBusinessId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                if (diag != null) diag.Add("Pass3Ordering: wholesale intake needs distinct, real payer and payee Business ids.");
                return null;
            }
            if (units <= 0)
            {
                if (diag != null) diag.Add("Pass3Ordering: wholesale intake for lot '" + bakeryLotId + "' needs a positive quantity.");
                return null;
            }
            if (wholesalePriceCents < 0)
            {
                if (diag != null) diag.Add("Pass3Ordering: wholesale intake for lot '" + bakeryLotId + "' cannot use a negative price.");
                return null;
            }
            if (deliveredLotIds.Contains(bakeryLotId.Trim()))
            {
                if (diag != null) diag.Add("Pass3Ordering: wholesale lot '" + bakeryLotId + "' was already received; duplicate intake refused.");
                return null;
            }
            int ageDays = transferDayIndex - bakedDayIndex;
            if (ageDays < 0)
            {
                if (diag != null) diag.Add("Pass3Ordering: wholesale lot '" + bakeryLotId + "' has a transfer day before its baked day.");
                return null;
            }
            if (ageDays > 1)
            {
                if (diag != null) diag.Add("Pass3Ordering: wholesale lot '" + bakeryLotId + "' is stale (" + ageDays + " days past baked day); refused, not converted.");
                return null;
            }
            string condition = ageDays == 0 ? "Fresh" : "DayOld";
            Pass3ProductLine line = storeCatalog.Find(productId);
            if (line == null)
            {
                line = new Pass3ProductLine
                {
                    ProductId = productId.Trim(),
                    DisplayName = productId.Trim(),
                    CostCents = wholesalePriceCents,
                    PriceCents = wholesalePriceCents,
                };
                if (!storeCatalog.AddProduct(line, diag)) return null;
            }
            else
            {
                line.CostCents = wholesalePriceCents;
            }
            line.BackUnits += units;
            deliveredLotIds.Add(bakeryLotId.Trim());
            return new Pass3WholesaleIntakeResult
            {
                BakeryLotId = bakeryLotId.Trim(),
                ProductId = productId.Trim(),
                UnitsReceived = units,
                Condition = condition,
                Settlement = new Pass3WholesaleSettlement
                {
                    PayerBusinessId = payerBusinessId.Trim(),
                    PayeeBusinessId = payeeBusinessId.Trim(),
                    BakeryLotId = bakeryLotId.Trim(),
                    ProductId = productId.Trim(),
                    Units = units,
                    UnitWholesalePriceCents = wholesalePriceCents,
                    AmountCents = units * wholesalePriceCents,
                    Settled = false,
                },
            };
        }

        /// <summary>
        /// Series 2 Pass 9, additive only (Receive is untouched):
        /// snapshot of the delivered-lot guard, for save adoption.
        /// A reload that rebuilt this instance without its delivered
        /// set could re-intake a lot and double-count bread, so the
        /// Pass 9 save bundle captures this set and restores it
        /// before this path runs in production.
        /// </summary>
        public List<string> DeliveredLotIds()
        {
            return new List<string>(deliveredLotIds);
        }

        /// <summary>
        /// Series 2 Pass 9, additive only: clear-then-load restore
        /// of the delivered-lot guard. Restoring the same set twice
        /// yields the same guard (idempotent); empty, duplicate, and
        /// blank ids are skipped with a diagnostic, never stored.
        /// </summary>
        public void RestoreDeliveredLotIds(IEnumerable<string> lotIds, List<string> diag)
        {
            deliveredLotIds.Clear();
            if (lotIds == null) return;
            foreach (string lotId in lotIds)
            {
                if (string.IsNullOrWhiteSpace(lotId)) continue;
                string trimmed = lotId.Trim();
                if (!deliveredLotIds.Add(trimmed) && diag != null)
                    diag.Add($"Pass3Ordering: duplicate delivered wholesale lot id '{trimmed}' in save; kept once.");
            }
        }
    }

    /// <summary>
    /// Per-item offer model for multi-item categories. Given a
    /// category's item list and a store catalog, OfferForItem returns
    /// exactly that item's line, or a refusal when the item is not
    /// in the category or not in the catalog. It never returns an
    /// aggregate quantity labelled as a single item, which is the
    /// exact error the single-item guard was built to prevent.
    /// </summary>
    public static class Pass3PerItemOffer
    {
        public static Pass3ProductLine OfferForItem(
            List<string> categoryItemIds, string itemId, Pass3StoreCatalog catalog, List<string> diag)
        {
            if (categoryItemIds == null || string.IsNullOrWhiteSpace(itemId) || catalog == null)
            {
                if (diag != null) diag.Add("Pass3Ordering: per-item offer needs a category item list, an item id, and a catalog.");
                return null;
            }
            bool inCategory = false;
            foreach (string candidate in categoryItemIds)
            {
                if (string.Equals(candidate, itemId.Trim(), StringComparison.OrdinalIgnoreCase)) { inCategory = true; break; }
            }
            if (!inCategory)
            {
                if (diag != null) diag.Add("Pass3Ordering: item '" + itemId + "' is not in this category; it cannot be offered as a member of it.");
                return null;
            }
            Pass3ProductLine line = catalog.Find(itemId);
            if (line == null)
            {
                if (diag != null) diag.Add("Pass3Ordering: item '" + itemId + "' is in the category but has no catalog line.");
                return null;
            }
            return line;
        }
    }
}
