// Review 2 vertical slice Pass D2 — household feed for a
// person-owned horse. A person does not eat from a store
// category directly: the household buys feed through the normal
// shopping loop (item runtime-registered as a household item,
// offered by the store port, paid through HouseholdLedger and
// the store cash port), the loop puts real lots into household
// inventory, and this adapter draws those lots for CareDay.
// Consumption is all-or-nothing at household-unit level
// (HouseholdInventory.Consume): if the household holds fewer
// units than the converted need, nothing is consumed and the
// adapter returns 0 feed units — the horse degrades and the
// units stay for the next day, never rounded up. The day index
// is a constructor parameter because Consume records it; the
// driver must construct this adapter per day, never reuse one
// across days. Conversion is a caller parameter, never a
// silent 1:1 canon, exactly as the store adapter does it.
using System;
using System.Collections.Generic;
using LandLedgers.Animals;

namespace LandLedgers.Population
{
    public sealed class Review2HouseholdHorseFeed : IHorseFeedStock
    {
        private readonly HouseholdInventory inventory;
        private readonly string feedItemId;
        private readonly int dayIndex;
        private readonly int feedUnitsNumerator;
        private readonly int householdUnitsDenominator;
        private readonly Action<int> persistUnusedFeedUnits;
        private int unusedFeedUnits;

        public Review2HouseholdHorseFeed(
            HouseholdInventory inventory,
            string feedItemId,
            int householdUnitsPerFeedUnit,
            int dayIndex)
            : this(inventory, feedItemId, 1, householdUnitsPerFeedUnit, dayIndex, 0, null)
        {
        }

        /// <summary>
        /// Explicit rational caller conversion: feed units delivered per
        /// household package, plus a carry callback so package rounding does
        /// not discard feed between daily CareDay adapters.
        /// </summary>
        public Review2HouseholdHorseFeed(
            HouseholdInventory inventory,
            string feedItemId,
            int feedUnitsNumerator,
            int householdUnitsDenominator,
            int dayIndex,
            int unusedFeedUnits,
            Action<int> persistUnusedFeedUnits)
        {
            this.inventory = inventory;
            this.feedItemId = string.IsNullOrWhiteSpace(feedItemId) ? string.Empty : feedItemId.Trim();
            this.feedUnitsNumerator = feedUnitsNumerator;
            this.householdUnitsDenominator = householdUnitsDenominator;
            this.dayIndex = dayIndex;
            this.unusedFeedUnits = System.Math.Max(0, unusedFeedUnits);
            this.persistUnusedFeedUnits = persistUnusedFeedUnits;
        }

        public string FeedItemId => feedItemId;

        /// <summary>
        /// Caller-supplied conversion: household item units backing
        /// one horse feed unit. Zero or negative means "not
        /// supplied" — the adapter holds/takes 0, never a silent 1.
        /// </summary>
        public int HouseholdUnitsPerFeedUnit => feedUnitsNumerator > 0
            ? System.Math.Max(1, (householdUnitsDenominator + feedUnitsNumerator - 1) / feedUnitsNumerator)
            : 0;

        public int FeedStockUnits
        {
            get
            {
                if (inventory == null || feedUnitsNumerator <= 0 || householdUnitsDenominator <= 0) return 0;
                return unusedFeedUnits + inventory.GetAvailableUnits(feedItemId) * feedUnitsNumerator / householdUnitsDenominator;
            }
        }

        public int TakeFeedUnits(int requestedUnits)
        {
            if (inventory == null || feedUnitsNumerator <= 0 || householdUnitsDenominator <= 0 || requestedUnits <= 0) return 0;
            if (FeedStockUnits < requestedUnits) return 0;
            int deficit = System.Math.Max(0, requestedUnits - unusedFeedUnits);
            int householdUnitsNeeded = deficit == 0 ? 0
                : (deficit * householdUnitsDenominator + feedUnitsNumerator - 1) / feedUnitsNumerator;
            int delivered = householdUnitsNeeded * feedUnitsNumerator / householdUnitsDenominator;
            while (householdUnitsNeeded > 0 && delivered < deficit)
            {
                householdUnitsNeeded++;
                delivered = householdUnitsNeeded * feedUnitsNumerator / householdUnitsDenominator;
            }
            var records = new List<HouseholdConsumptionRecord>();
            if (householdUnitsNeeded > 0)
            {
                string refusal = inventory.Consume(
                    feedItemId, householdUnitsNeeded, dayIndex, "horse feed", "horse", records);
                if (refusal != null) return 0;
            }
            unusedFeedUnits = System.Math.Max(0, unusedFeedUnits + delivered - requestedUnits);
            persistUnusedFeedUnits?.Invoke(unusedFeedUnits);
            return requestedUnits;
        }
    }
}
