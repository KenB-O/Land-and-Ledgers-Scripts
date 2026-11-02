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

        public StoreHorseFeedStockAdapter(
            BusinessRuntimeState storeRuntime,
            string feedCategoryId,
            int storeUnitsPerFeedUnit)
        {
            this.storeRuntime = storeRuntime;
            this.feedCategoryId = string.IsNullOrWhiteSpace(feedCategoryId) ? string.Empty : feedCategoryId.Trim();
            StoreUnitsPerFeedUnit = storeUnitsPerFeedUnit;
        }

        public string FeedCategoryId => feedCategoryId;

        /// <summary>
        /// Caller-supplied conversion: store units backing one horse
        /// feed unit. Zero or negative means "not supplied" — the
        /// adapter then holds 0 feed units and takes 0, never a
        /// silent 1:1. This follows the Pass C map rule.
        /// </summary>
        public int StoreUnitsPerFeedUnit { get; }

        public int FeedStockUnits
        {
            get
            {
                if (storeRuntime == null || StoreUnitsPerFeedUnit <= 0) return 0;
                CategoryStockState stock = storeRuntime.GetCategoryStock(feedCategoryId);
                return stock != null ? stock.CurrentStockUnits / StoreUnitsPerFeedUnit : 0;
            }
        }

        public int TakeFeedUnits(int requestedUnits)
        {
            if (storeRuntime == null || StoreUnitsPerFeedUnit <= 0 || requestedUnits <= 0) return 0;
            int storeUnitsNeeded = requestedUnits * StoreUnitsPerFeedUnit;
            if (!storeRuntime.TryConsumeCategoryStockUnits(feedCategoryId, storeUnitsNeeded, out int consumedStoreUnits)
                || consumedStoreUnits <= 0)
            {
                return 0;
            }

            return consumedStoreUnits / StoreUnitsPerFeedUnit;
        }
    }
}
