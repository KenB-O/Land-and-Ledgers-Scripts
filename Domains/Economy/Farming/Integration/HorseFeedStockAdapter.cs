using LandLedgers.Animals;

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
}
