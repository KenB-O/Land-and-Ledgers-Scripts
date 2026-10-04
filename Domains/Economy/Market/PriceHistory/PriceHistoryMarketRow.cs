using System;

namespace LandLedgers.Economy.Market.PriceHistory
{
    /// <summary>
    /// D4I: direction of an observed price change — descriptive, never a forecast.
    /// </summary>
    public enum PriceHistoryTrendDirection
    {
        Unknown = 0,
        Up = 1,
        Down = 2,
        Steady = 3,
    }

    /// <summary>
    /// D4I: one newspaper market-report row, built by the store's read API
    /// (<see cref="PriceHistoryStore.BuildMarketRow"/>) for the D4J newspaper
    /// market-report package. Every price traces to a recorded observation
    /// and its real source transaction — no modeled values.
    /// </summary>
    [Serializable]
    public sealed class PriceHistoryMarketRow
    {
        public string SettlementId = string.Empty;
        public string CommodityKind = string.Empty;
        public int AsOfDayIndex;

        /// <summary>
        /// D4J: authored settlement name at record time (empty when the
        /// observation carried none). Lets the market-report column print a
        /// human town name without a second lookup.
        /// </summary>
        public string SettlementName = string.Empty;

        /// <summary>False when no history exists for (settlement, commodity).</summary>
        public bool HasData;

        public int LatestPriceCents;
        public int LatestDayIndex;
        public PriceHistoryQuoteSide QuoteSide = PriceHistoryQuoteSide.Unspecified;
        public string UnitLabel = string.Empty;
        public string SourceTransactionReference = string.Empty;

        /// <summary>Price in force lookbackDays earlier (null when none).</summary>
        public int? EarlierPriceCents;
        public int? EarlierDayIndex;

        /// <summary>Latest minus earlier, in cents (null when no earlier price).</summary>
        public int? ChangeCents;

        public PriceHistoryTrendDirection Direction = PriceHistoryTrendDirection.Unknown;

        public PriceHistoryMarketRow() { }
    }
}
