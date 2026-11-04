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
                int packageUnits = string.IsNullOrEmpty(feedItemId)
                    ? storeRuntime.GetCategoryStock(feedCategoryId)?.CurrentStockUnits ?? 0
                    : storeRuntime.GetRetailItemStockUnits(feedItemId, feedCategoryId);
                return Math.Max(0, registration?.UnusedFeedUnits ?? 0)
                    + (packageUnits * feedUnitsNumerator / storeUnitsDenominator);
            }
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
                bool consumed = string.IsNullOrEmpty(feedItemId)
                    ? storeRuntime.TryConsumeCategoryStockUnits(feedCategoryId, packageUnitsNeeded, out int categoryUnits)
                        && categoryUnits == packageUnitsNeeded
                    : storeRuntime.TryConsumeRetailItemStockUnits(feedItemId, feedCategoryId, packageUnitsNeeded, out int itemUnits)
                        && itemUnits == packageUnitsNeeded;
                if (!consumed) return 0;
            }

            if (registration != null)
                registration.UnusedFeedUnits = Math.Max(0, carried + deliveredByPackages - requestedUnits);

            return requestedUnits;
        }
    }
}
