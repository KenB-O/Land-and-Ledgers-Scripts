// Review 2 second ScriptableObjects pass — diagnostic only, no behaviour change.
//
// The headline asset/runtime disagreement this pass traced, with evidence:
//
//   - D2's owner operation (Review2StoreHorseFeed.AddFeedItem) creates only a
//     runtime category entry on BusinessRuntimeState and a registry entry.
//     It adds NOTHING to any .asset: no definition category, no definition
//     item, no profile entry, and no price. That is correct per Kennedy's
//     decision (feed is player-addable, never a default catalog item).
//   - The production household offer list, however, is built ONLY from the
//     authored store definition: GeneralStoreRuntimeManager
//     .BuildHouseholdTradingPort iterates storeDefinition.Categories and,
//     per category, the definition's authored items (falling back to the
//     category's own item list only when the definition's item table is
//     empty for that category). It never iterates runtime category stock.
//   - Consequence, proved by reading both sides: a store-owned horse CAN
//     draw feed from a runtime-added category (the D2 adapter reads runtime
//     stock directly), but a person-owned horse CANNOT buy that same feed
//     through the production port unless the feed item is already an
//     authored definition item in an authored definition category — and in
//     that case feed draws and household sales share that category's single
//     aggregate pool. D2's person-leg test used a test-seam port that
//     offered the feed item directly, so it does not prove the production
//     port offers it.
//
// This helper makes that disagreement observable instead of silent. It
// changes no offer, no stock, no cash, and no price. It does NOT choose
// between the profile and the definition (they are currently identical
// after stripping script/name/class — see the review doc), it does NOT
// add a feed item to any asset, and it does NOT set a price: a runtime
// category with no authored items would price at the manager's 5c floor
// (minimumCategoryUnitPriceCents), and selling feed at that floor by
// default would silently canonise a price, so this helper never suggests
// it. The production fix — a runtime-offer path plus an owner-set price —
// is a design decision for Kennedy/Codex, recorded in the review doc.
//
// Caller contract for definitionItemsByCategory: pass the EFFECTIVE offer
// item list per category, exactly as BuildHouseholdTradingPort would emit
// it — the definition's authored items for the category, or the category's
// own item list when the definition's item table is empty for it.
// Plain C#, no Unity dependency, Roslyn-testable.

using System;
using System.Collections.Generic;

namespace LandLedgers.Economy
{
    public static class Review2StoreOfferGapCheck
    {
        /// <summary>
        /// Returns diagnostics for one registered store feed item, or an empty
        /// list when nothing about its offerability is in doubt. A "gap"
        /// diagnostic means the production port emits no offer for the item;
        /// a "shared pool" diagnostic means it is offered, but feed draws and
        /// household sales consume the same aggregate category pool.
        /// </summary>
        public static List<string> CheckFeedOffer(
            Review2StoreFeedItem registration,
            IReadOnlyCollection<string> definitionCategoryIds,
            IReadOnlyDictionary<string, IReadOnlyCollection<string>> definitionItemsByCategory)
        {
            var diagnostics = new List<string>();
            if (registration == null)
            {
                return diagnostics;
            }

            if (string.IsNullOrWhiteSpace(registration.FeedCategoryId))
            {
                diagnostics.Add("Store feed offer: registration for store '" + registration.BusinessInstanceId + "' has no category id — no offer can be checked.");
                return diagnostics;
            }

            if (string.IsNullOrWhiteSpace(registration.FeedItemId))
            {
                diagnostics.Add("Store feed offer: registration for store '" + registration.BusinessInstanceId + "' in category '" + registration.FeedCategoryId + "' has no item id — no offer can be checked.");
                return diagnostics;
            }

            bool categoryInDefinition = false;
            if (definitionCategoryIds != null)
            {
                foreach (string categoryId in definitionCategoryIds)
                {
                    if (string.Equals(categoryId, registration.FeedCategoryId, StringComparison.OrdinalIgnoreCase))
                    {
                        categoryInDefinition = true;
                        break;
                    }
                }
            }

            if (!categoryInDefinition)
            {
                diagnostics.Add("Store feed offer GAP: registered feed item '" + registration.FeedItemId + "' in category '" + registration.FeedCategoryId + "' has no definition category, so BuildHouseholdTradingPort emits no offer for it — a person-owned horse cannot buy it through the production port, while a store-owned horse can still draw runtime category stock. No price is set here; do not sell it at the 5c floor by default.");
                return diagnostics;
            }

            IReadOnlyCollection<string> offeredItems = null;
            if (definitionItemsByCategory != null)
            {
                foreach (KeyValuePair<string, IReadOnlyCollection<string>> pair in definitionItemsByCategory)
                {
                    if (string.Equals(pair.Key, registration.FeedCategoryId, StringComparison.OrdinalIgnoreCase))
                    {
                        offeredItems = pair.Value;
                        break;
                    }
                }
            }

            bool itemOffered = false;
            if (offeredItems != null)
            {
                foreach (string itemId in offeredItems)
                {
                    if (string.Equals(itemId, registration.FeedItemId, StringComparison.OrdinalIgnoreCase))
                    {
                        itemOffered = true;
                        break;
                    }
                }
            }

            if (!itemOffered)
            {
                diagnostics.Add("Store feed offer GAP: registered feed item '" + registration.FeedItemId + "' is not an offered item of definition category '" + registration.FeedCategoryId + "', so BuildHouseholdTradingPort emits no offer for it — a person-owned horse cannot buy it through the production port, while a store-owned horse can still draw runtime category stock. No price is set here; do not sell it at the 5c floor by default.");
                return diagnostics;
            }

            diagnostics.Add("Store feed offer SHARED POOL: feed item '" + registration.FeedItemId + "' is an authored definition item in category '" + registration.FeedCategoryId + "', so it is offered — but feed draws and household sales share that category's single aggregate pool in BusinessRuntimeState, and a household buying the item and a store horse drawing feed consume the same stock.");
            return diagnostics;
        }
    }
}
