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
using System.Collections.Generic;
using LandLedgers.Animals;

namespace LandLedgers.Population
{
    public sealed class Review2HouseholdHorseFeed : IHorseFeedStock
    {
        private readonly HouseholdInventory inventory;
        private readonly string feedItemId;
        private readonly int dayIndex;

        public Review2HouseholdHorseFeed(
            HouseholdInventory inventory,
            string feedItemId,
            int householdUnitsPerFeedUnit,
            int dayIndex)
        {
            this.inventory = inventory;
            this.feedItemId = string.IsNullOrWhiteSpace(feedItemId) ? string.Empty : feedItemId.Trim();
            HouseholdUnitsPerFeedUnit = householdUnitsPerFeedUnit;
            this.dayIndex = dayIndex;
        }

        public string FeedItemId => feedItemId;

        /// <summary>
        /// Caller-supplied conversion: household item units backing
        /// one horse feed unit. Zero or negative means "not
        /// supplied" — the adapter holds/takes 0, never a silent 1.
        /// </summary>
        public int HouseholdUnitsPerFeedUnit { get; }

        public int FeedStockUnits
        {
            get
            {
                if (inventory == null || HouseholdUnitsPerFeedUnit <= 0) return 0;
                return inventory.GetAvailableUnits(feedItemId) / HouseholdUnitsPerFeedUnit;
            }
        }

        public int TakeFeedUnits(int requestedUnits)
        {
            if (inventory == null || HouseholdUnitsPerFeedUnit <= 0 || requestedUnits <= 0) return 0;
            int householdUnitsNeeded = requestedUnits * HouseholdUnitsPerFeedUnit;
            var records = new List<HouseholdConsumptionRecord>();
            string refusal = inventory.Consume(
                feedItemId, householdUnitsNeeded, dayIndex, "horse feed", "horse", records);
            if (refusal != null) return 0;
            return householdUnitsNeeded / HouseholdUnitsPerFeedUnit;
        }
    }
}
