// Review 2 vertical slice Pass D2 (owner-decided horse feed):
// a finite feed stock drawn from a General Store's own runtime
// category stock. This is the store-owned-horse counterpart of
// BusinessHorseFeedStockAdapter (which draws livery_feed_upkeep
// for LiveryFreight businesses). The store category is NOT a
// hard-coded default: it is the category of an item the store
// owner added through Review2StoreHorseFeed.AddFeedItem, and the
// conversion is a caller parameter, never a silent 1:1 canon.
// Category consumption is all-or-nothing at store-unit level
// (BusinessRuntimeState.TryConsumeCategoryStockUnits), so a
// horse whose need, converted, exceeds the store units on hand
// takes 0 feed units and the store units stay put for the next
// day — they are never rounded up into a ration that does not
// exist. Feed is never free: TakeFeedUnits returns only whole
// feed units actually backed by store units.
using System;
using LandLedgers.Animals;
using LandLedgers.Economy;

namespace LandLedgers.Economy.Farming.Integration
{
    public sealed class StoreHorseFeedStockAdapter : IHorseFeedStock
    {
        private readonly BusinessRuntimeState storeRuntime;
        private readonly string feedCategoryId;
        private readonly string feedItemId;
        private readonly int feedUnitsNumerator;
        private readonly int storeUnitsDenominator;
        private readonly Review2StoreFeedItem registration;

        public StoreHorseFeedStockAdapter(
            BusinessRuntimeState storeRuntime,
            string feedCategoryId,
            int storeUnitsPerFeedUnit)
        {
            this.storeRuntime = storeRuntime;
            this.feedCategoryId = string.IsNullOrWhiteSpace(feedCategoryId) ? string.Empty : feedCategoryId.Trim();
            this.storeUnitsDenominator = storeUnitsPerFeedUnit;
            feedUnitsNumerator = 1;
            feedItemId = string.Empty;
        }

        public StoreHorseFeedStockAdapter(BusinessRuntimeState storeRuntime, Review2StoreFeedItem registration)
        {
            this.storeRuntime = storeRuntime;
            this.registration = registration;
            feedCategoryId = registration?.FeedCategoryId?.Trim() ?? string.Empty;
            feedItemId = registration?.FeedItemId?.Trim() ?? string.Empty;
            feedUnitsNumerator = Review2StoreHorseFeed.EffectiveNumerator(registration);
            storeUnitsDenominator = Review2StoreHorseFeed.EffectiveDenominator(registration);
        }

        public string FeedCategoryId => feedCategoryId;

        /// <summary>
        /// Caller-supplied conversion: store units backing one horse
        /// feed unit. Zero or negative means "not supplied" — the
        /// adapter then holds 0 feed units and takes 0, never a
        /// silent 1:1. This follows the Pass C map rule.
        /// </summary>
        public int StoreUnitsPerFeedUnit => feedUnitsNumerator > 0
            ? Math.Max(1, (storeUnitsDenominator + feedUnitsNumerator - 1) / feedUnitsNumerator)
            : 0;

        public int FeedStockUnits
        {
            get
            {
                if (storeRuntime == null || storeUnitsDenominator <= 0 || feedUnitsNumerator <= 0) return 0;
                int availableStoreUnits = AvailableStoreUnits(out _, out _);
                return Math.Max(0, registration?.UnusedFeedUnits ?? 0)
                    + (availableStoreUnits * feedUnitsNumerator / storeUnitsDenominator);
            }
        }

        /// <summary>
        /// Store units genuinely available as feed for this adapter,
        /// counting each physical unit exactly once. The category total
        /// already includes every identified lot in the category, so
        /// the unallocated pool is the category total minus the
        /// quantities of ALL lots in the category (any item), and the
        /// matching lots contribute only their available units. A lot
        /// of a different item contributes through neither leg, and a
        /// save-carried reservation on a matching lot contributes
        /// through neither leg (AvailableUnits excludes it). When the
        /// adapter carries no feed item id (legacy constructor), the
        /// whole category total is unallocated pool, as before.
        /// </summary>
        private int AvailableStoreUnits(out int unallocatedPoolUnits, out int matchingLotAvailableUnits)
        {
            unallocatedPoolUnits = 0;
            matchingLotAvailableUnits = 0;
            CategoryStockState category = storeRuntime.GetCategoryStock(feedCategoryId);
            if (category == null) return 0;
            if (string.IsNullOrEmpty(feedItemId))
            {
                unallocatedPoolUnits = Math.Max(0, category.CurrentStockUnits);
                return unallocatedPoolUnits;
            }

            int identifiedPhysicalUnits = 0;
            for (int i = 0; i < storeRuntime.RetailItemLots.Count; i++)
            {
                BusinessRetailItemLotState lot = storeRuntime.RetailItemLots[i];
                if (lot == null) continue;
                if (string.Equals(lot.CategoryId, feedCategoryId, StringComparison.OrdinalIgnoreCase))
                    identifiedPhysicalUnits += lot.QuantityUnits;
                // Matching lots must be held by this business, exactly
                // as GetRetailItemStockUnits / TryConsumeRetailItemStockUnits
                // count them: a lot merely listed against another
                // business is neither readable nor consumable here.
                if (!string.IsNullOrWhiteSpace(storeRuntime.BusinessId)
                    && string.Equals(lot.OwnerBusinessId, storeRuntime.BusinessId, StringComparison.Ordinal)
                    && string.Equals(lot.CustodianBusinessId, storeRuntime.BusinessId, StringComparison.Ordinal)
                    && string.Equals(lot.ItemId, feedItemId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(lot.CategoryId, feedCategoryId, StringComparison.OrdinalIgnoreCase))
                    matchingLotAvailableUnits += lot.AvailableUnits;
            }

            unallocatedPoolUnits = Math.Max(0, category.CurrentStockUnits - identifiedPhysicalUnits);
            return unallocatedPoolUnits + matchingLotAvailableUnits;
        }

        public int TakeFeedUnits(int requestedUnits)
        {
            if (storeRuntime == null || storeUnitsDenominator <= 0 || feedUnitsNumerator <= 0 || requestedUnits <= 0) return 0;
            int carried = Math.Max(0, registration?.UnusedFeedUnits ?? 0);
            int available = FeedStockUnits;
            if (available < requestedUnits) return 0;

            int deficit = Math.Max(0, requestedUnits - carried);
            int packageUnitsNeeded = deficit == 0 ? 0
                : (deficit * storeUnitsDenominator + feedUnitsNumerator - 1) / feedUnitsNumerator;
            int deliveredByPackages = packageUnitsNeeded * feedUnitsNumerator / storeUnitsDenominator;
            while (packageUnitsNeeded > 0 && deliveredByPackages < deficit)
            {
                packageUnitsNeeded++;
                deliveredByPackages = packageUnitsNeeded * feedUnitsNumerator / storeUnitsDenominator;
            }

            if (packageUnitsNeeded > 0)
            {
                // Split the need pool-first: identified lots are the
                // household-saleable stock, so they are preserved while
                // unidentified pool stock lasts. Both legs are validated
                // against live reads before either consume runs, so a
                // failed second leg can never leave a half-consumed
                // ration behind (the two consume primitives have no
                // rollback). The empty-feedItemId shape keeps its
                // current behaviour: pool only, lot leg 0.
                int availableStoreUnits = AvailableStoreUnits(out int unallocatedPoolUnits, out int matchingLotAvailableUnits);
                if (packageUnitsNeeded > availableStoreUnits) return 0;
                int poolLeg = string.IsNullOrEmpty(feedItemId)
                    ? packageUnitsNeeded
                    : Math.Min(packageUnitsNeeded, unallocatedPoolUnits);
                int lotLeg = packageUnitsNeeded - poolLeg;
                if (poolLeg < 0 || lotLeg < 0 || poolLeg > unallocatedPoolUnits || lotLeg > matchingLotAvailableUnits) return 0;

                bool consumed = true;
                if (poolLeg > 0)
                    consumed = storeRuntime.TryConsumeCategoryStockUnits(feedCategoryId, poolLeg, out int categoryUnits)
                        && categoryUnits == poolLeg;
                if (consumed && lotLeg > 0)
                    consumed = storeRuntime.TryConsumeRetailItemStockUnits(feedItemId, feedCategoryId, lotLeg, out int itemUnits)
                        && itemUnits == lotLeg;
                if (!consumed) return 0;
            }

            if (registration != null)
                registration.UnusedFeedUnits = Math.Max(0, carried + deliveredByPackages - requestedUnits);

            return requestedUnits;
        }
    }
}
