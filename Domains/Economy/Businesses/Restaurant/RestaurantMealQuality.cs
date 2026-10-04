using System;

namespace LandLedgers.Economy.Businesses.Restaurant
{
    /// <summary>
    /// D1E: meal quality bands — Canon §8.1B abstracts the menu to "meal
    /// quality, food cost, supply reliability and labor burden rather than
    /// becoming a cooking game". Quality is a property of the COOKED LOT
    /// (whose hands made it, on the day), not of the recipe: the same beef
    /// stew can be rough, house-standard, or fine. Bands move the served
    /// price through the owner's price schedule and ride the served-meal
    /// record into the nutrition link (Canon §13.X: nutrition has a quality
    /// dimension alongside quantity and regularity). The day-old condition
    /// discount applies on top of the quality price, never instead of it.
    /// </summary>
    public enum RestaurantMealQualityBand
    {
        Unspecified = 0,
        Rough = 1, // plain fare: filling, unrefined
        House = 2, // the house standard
        Fine = 3,  // the cook's best work
    }

    /// <summary>
    /// D1E: the house's quality policy as data — the Cooking skill thresholds
    /// that separate the bands. Settable per shop. All values are calibration
    /// (Canon Part XV), never canon claims.
    /// </summary>
    [Serializable]
    public sealed class RestaurantMealQualityPolicy
    {
        /// <summary>TUNING: Cooking skill level (1-10 PersonSkillState scale) at/above which the cook's work rates Fine.</summary>
        public int FineMinCookSkillLevel = 6;

        /// <summary>TUNING: Cooking skill level (1-10) at/above which the cook's work rates House. Below this (but known) rates Rough.</summary>
        public int HouseMinCookSkillLevel = 3;

        public RestaurantMealQualityPolicy() { }

        /// <summary>
        /// Derives the quality band from the leading cook's skill level.
        /// Skill ≤ 0 (unknown, or the implicit proprietor cook of a house with
        /// no named staff) rates House — the plain house standard, never
        /// punished for being unnamed.
        /// </summary>
        public RestaurantMealQualityBand DeriveBand(int cookSkillLevel)
        {
            if (cookSkillLevel >= FineMinCookSkillLevel) return RestaurantMealQualityBand.Fine;
            if (cookSkillLevel >= HouseMinCookSkillLevel) return RestaurantMealQualityBand.House;
            if (cookSkillLevel > 0) return RestaurantMealQualityBand.Rough;
            return RestaurantMealQualityBand.House;
        }

        /// <summary>
        /// TUNING: nutrition weight of one served meal of the band, for the
        /// quality-aware nutrition link. The count link stays authoritative —
        /// this only weights it where a consumer opts in. A Fine meal
        /// nourishes a touch beyond its count; a Rough meal a touch less.
        /// </summary>
        public static float QualityWeight01(RestaurantMealQualityBand band)
        {
            switch (band)
            {
                case RestaurantMealQualityBand.Fine: return 1.15f;
                case RestaurantMealQualityBand.Rough: return 0.70f;
                default: return 1.00f; // House and Unspecified
            }
        }
    }
}
