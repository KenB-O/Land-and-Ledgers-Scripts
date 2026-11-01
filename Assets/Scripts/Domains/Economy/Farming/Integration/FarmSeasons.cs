using System;

namespace LandLedgers.Economy.Farming.Integration
{
    /// <summary>FVS-4: the four seasons. Day-index mapping is calibration with a
    /// documented seam for the TimeManager calendar (Tech X §10.1).</summary>
    public enum FarmSeason
    {
        Spring = 0,
        Summer = 1,
        Fall = 2,
        Winter = 3,
    }

    /// <summary>
    /// FVS-4: seasonal effects on the farm loop (Canon Part IX dairy/egg
    /// seasonality; Canon §7.2 winter feed as a binding constraint).
    /// All constants are calibration, not canon — canon fixes that seasons
    /// MATTER, not the numbers.
    /// </summary>
    public static class FarmSeasons
    {
        /// <summary>Calibration: 90-day seasons; day 0 = first day of spring.</summary>
        public const int SeasonLengthDays = 90;

        public static FarmSeason SeasonForDayIndex(int dayIndex)
        {
            int d = Math.Max(0, dayIndex) % (SeasonLengthDays * 4);
            return (FarmSeason)(d / SeasonLengthDays);
        }

        /// <summary>Winter feed costs more: no grazing, higher intake (calibration).</summary>
        public const float WinterFeedMultiplier = 1.5f;

        public static float FeedMultiplierFor(FarmSeason season)
        {
            return season == FarmSeason.Winter ? WinterFeedMultiplier : 1f;
        }

        /// <summary>Share of cattle feed need met by pasture grazing (calibration).</summary>
        public static float PastureGrazingShare(FarmSeason season)
        {
            switch (season)
            {
                case FarmSeason.Spring: return 0.4f;
                case FarmSeason.Summer: return 0.6f;
                case FarmSeason.Fall: return 0.3f;
                default: return 0f; // winter: stored feed or purchased feed only
            }
        }

        /// <summary>Egg laying is biological and seasonal (Canon §9.8).</summary>
        public static float LayingFactorFor(FarmSeason season)
        {
            switch (season)
            {
                case FarmSeason.Spring: return 1f;
                case FarmSeason.Summer: return 0.9f;
                case FarmSeason.Fall: return 0.6f;
                default: return 0.35f;
            }
        }

        /// <summary>
        /// Lactation rest: cows should be dried off ~2 months before calving.
        /// Returns true when a cow this far into milk should be dried (calibration).
        /// </summary>
        public static bool ShouldDryOff(int daysInMilk)
        {
            return daysInMilk >= 305;
        }
    }
}
