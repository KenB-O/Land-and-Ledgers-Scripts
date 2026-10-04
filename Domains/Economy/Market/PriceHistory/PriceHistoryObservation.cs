using System;

namespace LandLedgers.Economy.Market.PriceHistory
{
    /// <summary>
    /// D4I: which side of a transaction the observed price comes from.
    /// The store records what happened, never a modeled or predicted price.
    /// </summary>
    public enum PriceHistoryQuoteSide
    {
        Unspecified = 0,
        /// <summary>The price the merchant CHARGED a buyer (retail / selling price).</summary>
        ObservedSellPrice = 1,
        /// <summary>The price the merchant PAID a seller (farmgate / bid price).</summary>
        ObservedBuyPrice = 2,
    }

    /// <summary>
    /// D4I: one observed commodity price — a record of what actually happened.
    ///
    /// HARD CONSTRAINTS (Canon locks honored):
    /// - Every observation MUST cite a real recorded transaction via
    ///   <see cref="SourceTransactionReference"/> (e.g. a GristMillSaleRecord
    ///   SaleLotId, a bakery BakerySaleRecord id). No invented prices, ever.
    /// - Append-only: corrections are NEW observations with
    ///   <see cref="SupersedesObservationId"/> set. Existing records are never
    ///   edited. See <see cref="PriceHistoryStore"/>.
    /// - NO generator, demand model, or price-prediction logic lives here.
    ///   The MarketTerritory generator core is held for Kennedy's decision
    ///   (this class records, it never synthesizes).
    ///
    /// CommodityKind reuses the repo's existing string kind values
    /// ("flour", "meal", "feed", "bran", "wheat", "corn", "oats", "hay",
    /// "nails", ...). There is no parallel taxonomy: see
    /// <see cref="TradeCommodityKindCatalog"/>.
    /// Dates and prices use repo conventions: DayIndex (int, game calendar)
    /// and UnitPriceCents (int, cents per unit).
    /// </summary>
    [Serializable]
    public sealed class PriceHistoryObservation
    {
        /// <summary>Unique id for this observation (caller-assigned, e.g. "obs:&lt;txref&gt;").</summary>
        public string ObservationId = string.Empty;

        /// <summary>The town/settlement where the transaction happened (Settlement.SettlementId).</summary>
        public string SettlementId = string.Empty;

        /// <summary>Human-readable settlement name at record time (authored data, not a guess).</summary>
        public string SettlementName = string.Empty;

        /// <summary>
        /// Commodity kind key — one of the repo's existing goods kind strings.
        /// See <see cref="TradeCommodityKindCatalog"/>.
        /// </summary>
        public string CommodityKind = string.Empty;

        /// <summary>Game day the transaction occurred (DayIndex convention).</summary>
        public int DayIndex;

        /// <summary>Booked price, cents per unit.</summary>
        public int UnitPriceCents;

        /// <summary>Quantity that traded at this price (quantity context).</summary>
        public int QuantityUnits;

        /// <summary>Unit label from the transaction (e.g. "barrel", "bushel", "lb").</summary>
        public string UnitLabel = string.Empty;

        /// <summary>Which side of the transaction the price was observed from.</summary>
        public PriceHistoryQuoteSide QuoteSide = PriceHistoryQuoteSide.Unspecified;

        /// <summary>
        /// REQUIRED provenance: the real recorded transaction that produced
        /// this observation (e.g. "gristmill-sale:GS-0042"). Upstream-provenance
        /// rule: an observation without a real source transaction is refused.
        /// </summary>
        public string SourceTransactionReference = string.Empty;

        /// <summary>The business instance that recorded the source transaction.</summary>
        public string SourceBusinessId = string.Empty;

        /// <summary>
        /// Corrections only: the ObservationId this entry supersedes. Empty on
        /// original observations. Superseded entries remain in the log for
        /// audit but are excluded from all queries.
        /// </summary>
        public string SupersedesObservationId = string.Empty;

        /// <summary>Free-text note (e.g. correction reason). Optional.</summary>
        public string Note = string.Empty;

        public PriceHistoryObservation() { }
    }
}
