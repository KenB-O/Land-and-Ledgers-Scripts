using System.Collections.Generic;

namespace LandLedgers.Economy.Businesses.Logging
{
    /// <summary>
    /// D2D: the seasonal working-day limit for logging labor (Canon §8.5:
    /// "Logging labor is outdoor work whose practical day can be limited by
    /// usable light, travel, physical conditions and available timber rather
    /// than a universal shift").
    ///
    /// The table below is an ASTRONOMICAL APPROXIMATION of daylight hours at
    /// ~44°N (Black Hills latitude), minus a work-light margin — it is NOT a
    /// canon number and NOT a historical claim. It exists so the mechanic
    /// canon demands (light limits the day) has an explicit, visible input;
    /// Kennedy can override any month through OverrideMinutes and the camp
    /// treats the override as the authority. Weather and travel reduce the
    /// day further through LoggingCampParameters.
    ///
    /// DESIGN FORKS / UNBUILT (canon-silent, not invented here):
    /// - Log scaling rules (board-foot scales): the canon and the historical
    ///   research are both silent — no scale is invented. Recorded as unbuilt.
    /// - Log drives / river transport: the canon describes only "log landing
    ///   or pond" at the mill end (Canon §8.5A) — no river-drive mechanic is
    ///   invented. Recorded as unbuilt.
    /// - Sled-vs-wagon season gating: the canon lists "logging sled/wagon" as
    ///   equipment alternatives (Canon 4.1) but is silent on season rules —
    ///   hauling stays wagon-gated (LoggingTaskDefinitions). Recorded as a
    ///   design fork for Kennedy.
    /// - Steam donkey: the canon explicitly restricts it to later/regional
    ///   researched settings — NOT modeled at the 1880 opening.
    /// </summary>
    public static class LoggingSeasonalDaylight
    {
        // Approximate usable work-light minutes per month at ~44°N, indexed
        // 1-12 (index 0 unused). Astronomical approximation minus a work-light
        // margin; Kennedy-calibratable via OverrideMinutes.
        private static readonly int[] BaseUsableMinutes =
        {
            0,    // placeholder for index 0
            510,  // Jan — ~9.0h
            582,  // Feb — ~10.2h
            672,  // Mar — ~11.7h
            768,  // Apr — ~13.3h
            852,  // May — ~14.7h
            894,  // Jun — ~15.4h
            870,  // Jul — ~15.0h
            804,  // Aug — ~13.9h
            714,  // Sep — ~12.4h
            618,  // Oct — ~10.8h
            534,  // Nov — ~9.4h
            492,  // Dec — ~8.7h
        };

        private static readonly Dictionary<int, int> Overrides =
            new Dictionary<int, int>();

        /// <summary>
        /// Usable work-light minutes for a 1-based month. Returns -1 for an
        /// invalid month (the camp refuses the day rather than guessing).
        /// Overrides set by Kennedy win over the astronomical base table.
        /// </summary>
        public static int UsableWorkMinutes(int monthIndex)
        {
            if (monthIndex < 1 || monthIndex > 12) return -1;
            int overridden;
            if (Overrides.TryGetValue(monthIndex, out overridden))
                return MathMax0(overridden);
            return BaseUsableMinutes[monthIndex];
        }

        /// <summary>
        /// Kennedy's calibration hook: replaces the table value for one
        /// month. Non-positive values are refused (they would silently zero
        /// the working day); returns null on success, a refusal otherwise.
        /// </summary>
        public static string OverrideMinutes(int monthIndex, int minutes)
        {
            if (monthIndex < 1 || monthIndex > 12)
                return $"LoggingSeasonalDaylight: month {monthIndex} is not 1-12 — override refused.";
            if (minutes <= 0)
                return $"LoggingSeasonalDaylight: override of {minutes} minutes would silently zero the working day — refused.";
            Overrides[monthIndex] = minutes;
            return null;
        }

        /// <summary>Clears all month overrides, restoring the base table.</summary>
        public static void ClearOverrides()
        {
            Overrides.Clear();
        }

        private static int MathMax0(int value)
        {
            return value < 0 ? 0 : value;
        }
    }
}
