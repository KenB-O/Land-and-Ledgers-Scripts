using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Market.PriceHistory
{
    /// <summary>
    /// D4I: commodity-kind catalog. NOT a parallel taxonomy — every key here
    /// is a kind string already in use by existing repo code:
    ///
    /// - GristMill merchant products: "flour", "meal", "feed", "bran"
    ///   (GristMillMerchantTrade.GristMillSaleRecord.ProductKind)
    /// - Crop kinds: "wheat", "corn", "oats", "hay"
    ///   (CropKind — Wheat cash grain, Corn feed/food, Oats feed, Hay forage;
    ///   Canon §7.4F calls hay a first-class strategic commodity)
    /// - Regional trade goods: "nails"
    ///   (LandLedgers.Economy.Market.RegionalTrade price quotes)
    ///
    /// The store accepts any non-empty kind string from a real transaction
    /// (businesses may trade kinds not yet listed here). This catalog is a
    /// convenience for callers that need a bounded list (e.g. the D4J
    /// newspaper market-report commodity loop). Add a key here only when it
    /// is actually used as a kind string in repo code — never pre-invented.
    /// </summary>
    public static class TradeCommodityKindCatalog
    {
        public const string Flour = "flour";
        public const string Meal = "meal";
        public const string Feed = "feed";
        public const string Bran = "bran";
        public const string Wheat = "wheat";
        public const string Corn = "corn";
        public const string Oats = "oats";
        public const string Hay = "hay";
        public const string Nails = "nails";

        private static readonly List<string> KnownKinds = new List<string>
        {
            Flour, Meal, Feed, Bran, Wheat, Corn, Oats, Hay, Nails,
        };

        /// <summary>The known commodity-kind keys (snapshot copy).</summary>
        public static List<string> KnownCommodityKinds() => new List<string>(KnownKinds);

        /// <summary>True when the kind key appears in this catalog.</summary>
        public static bool IsKnownCommodityKind(string commodityKind)
            => !string.IsNullOrWhiteSpace(commodityKind) && KnownKinds.Contains(commodityKind);
    }
}
