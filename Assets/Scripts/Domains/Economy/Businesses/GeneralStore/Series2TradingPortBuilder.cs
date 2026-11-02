// Series 2 Pass 7 (selling): per-item trading-port offers for the
// General Store. Plain C#, no UnityEngine references, Roslyn-testable.
//
// The problem this replaces, stated plainly: the production trading
// port used to offer a category only when exactly one authored item
// resolved in it (GeneralStoreRuntimeManager.TryResolveSingleItemForCategory).
// The authored staple_food category holds seven items (flour, bread,
// potatoes, salt, sugar, coffee, tea), so the whole category was skipped
// and households could buy none of it through the shopping loop —
// even though every one of those seven items IS a real household item.
//
// The fix is per-item offers, not an aggregate offer: one offer per
// real item, validated through Pass3PerItemOffer against a transient
// validation catalog. That catalog is validation-only — it is built,
// checked, and discarded inside this builder. It creates no stock,
// sets no price, and is never the store's stock authority. The store's
// stock authority stays BusinessRuntimeState category stock, reached
// through the production GeneralStoreTradingPort.
//
// SHARED-POOL CAVEAT, stated loudly because it is the one place this
// fix is weaker than item-level stock: BusinessRuntimeState holds ONE
// aggregate count per category, not one count per item. Every per-item
// offer in the same category therefore draws from the same pool.
// ItemStockUnits for flour and for bread each report the pool size,
// which overstates each item taken alone — but total units actually
// sold across all items in the category can never exceed the pool,
// because every sale consumes pool units through TryConsumeStoreUnits.
// A future item-level stock migration (a save-shape change, Codex /
// Kennedy decision, NOT claimed here) is what would make per-item
// stock counts exact. Until then, no offer here may be read as
// item-level stock, and no sale may be recorded against an item the
// category pool did not actually supply.
//
// Items that are NOT household items are never offered, and never
// offered under a representative id: beef/bacon/cheese are NOT offered
// as "preserved_meat", and basic_cloth/flannel/etc. are NOT offered as
// "clothing_textiles". Those categories stay un-offered through this
// path until an item-level mapping for them is decided. The old
// single-item path is preserved exactly for categories with exactly
// one authored item (see GeneralStoreRuntimeManager, which still owns
// that branch); this builder only handles multi-item categories.
using System;
using System.Collections.Generic;
using LandLedgers.Economy.Ordering;
using LandLedgers.Population;

namespace LandLedgers.Economy.Businesses.GeneralStore
{
    /// <summary>
    /// Series 2 Pass 7: per-item offers for one multi-item store category.
    /// One offer per validated household item, each drawing from the
    /// shared category pool (see the shared-pool caveat above).
    /// </summary>
    public static class Series2TradingPortBuilder
    {
        /// <summary>
        /// Builds per-item offers for a multi-item category. Returns an
        /// empty list (never null) when nothing in the category is a
        /// real household item. Every skipped item is diagnosed with
        /// its reason, so a missing offer is visible, never silent.
        /// </summary>
        public static List<GeneralStoreCategoryOffer> BuildPerItemOffers(
            string categoryId, IReadOnlyList<string> categoryItemIds, List<string> diag)
        {
            var offers = new List<GeneralStoreCategoryOffer>();
            if (string.IsNullOrWhiteSpace(categoryId) || categoryItemIds == null)
            {
                if (diag != null) diag.Add("Series2TradingPort: per-item offers need a category id and an item list.");
                return offers;
            }

            // Validation-only catalog: Pass3PerItemOffer requires a catalog
            // line to exist for the item. These lines carry no stock and
            // no price; they exist only so the per-item check is the same
            // check Pass 3 tested, not a second hand-rolled check.
            var validationCatalog = new Pass3StoreCatalog();
            var validationDiag = new List<string>();
            foreach (string itemId in categoryItemIds)
            {
                if (string.IsNullOrWhiteSpace(itemId)) continue;
                validationCatalog.AddProduct(new Pass3ProductLine
                {
                    ProductId = itemId.Trim(),
                    DisplayName = itemId.Trim(),
                }, validationDiag);
            }

            var itemIds = new List<string>(categoryItemIds);
            foreach (string itemId in categoryItemIds)
            {
                if (string.IsNullOrWhiteSpace(itemId)) continue;
                string trimmed = itemId.Trim();
                Pass3ProductLine line = Pass3PerItemOffer.OfferForItem(itemIds, trimmed, validationCatalog, diag);
                if (line == null)
                {
                    if (diag != null) diag.Add($"Series2TradingPort: '{trimmed}' is not offered from '{categoryId}': per-item validation refused it.");
                    continue;
                }
                if (HouseholdItemCatalog.Get(trimmed) == null)
                {
                    if (diag != null) diag.Add($"Series2TradingPort: '{trimmed}' in '{categoryId}' is not a household item, so it is not offered — it is never sold under a representative item id.");
                    continue;
                }
                offers.Add(new GeneralStoreCategoryOffer
                {
                    CategoryId = categoryId,
                    OfferedItemIds = new List<string> { trimmed },
                    StoreUnitsPerItemUnit = 1,
                });
            }

            return offers;
        }
    }
}
