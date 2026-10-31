using LandLedgers.Animals;
using LandLedgers.Economy;

namespace LandLedgers.Economy.Farming.Integration
{
    /// <summary>
    /// Pass VII Workstream B (P1-5): production adapter letting the
    /// animal-domain HorseCareDriver draw rations from the farm's real
    /// FeedLoop stock. Feed is finite — the driver only ever receives what
    /// the stock actually holds, so horse upkeep is a real cost against the
    /// farm's feed supply.
    /// </summary>
    public sealed class HorseFeedStockAdapter : IHorseFeedStock
    {
        private readonly FeedLoop feedLoop;

        public HorseFeedStockAdapter(FeedLoop feedLoop)
        {
            this.feedLoop = feedLoop;
        }

        public int FeedStockUnits => feedLoop != null ? feedLoop.FeedStockUnits : 0;

        public int TakeFeedUnits(int requestedUnits)
        {
            if (feedLoop == null) return 0;
            return feedLoop.TakeFeedUnits(requestedUnits);
        }
    }

    /// <summary>Finite feed/upkeep stock adapter for a horse-owning livery Business.</summary>
    public sealed class BusinessHorseFeedStockAdapter : IHorseFeedStock
    {
        private const string FeedCategoryId = "livery_feed_upkeep";
        private readonly BusinessRuntimeState business;

        public BusinessHorseFeedStockAdapter(BusinessRuntimeState business)
        {
            this.business = business;
        }

        public int FeedStockUnits => business?.GetCategoryStock(FeedCategoryId)?.CurrentStockUnits ?? 0;

        public int TakeFeedUnits(int requestedUnits)
        {
            int requested = System.Math.Max(0, requestedUnits);
            return business != null && requested > 0
                && business.TryConsumeCategoryStockUnits(FeedCategoryId, requested, out int consumed)
                    ? consumed
                    : 0;
        }
    }
}
