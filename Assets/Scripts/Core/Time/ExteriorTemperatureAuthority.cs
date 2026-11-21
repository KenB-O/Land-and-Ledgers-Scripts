using System;

namespace LandLedgers.Time
{
    /// <summary>
    /// Heating lane (Nov 20 2026): the region's exterior temperature by day.
    /// A pure static authority — no state, no Unity dependency — so any day's
    /// temperature is a deterministic function of (seed, absolute day) and
    /// Save/Continue reproduces it exactly from the already-persisted seed
    /// (hub.WeatherSeed). Keyed by day-of-year, not by year: the scenario is
    /// the 1870 dev town (Canon 1.1 spring 1870) and the baseline is a
    /// 30-year average centred on the scenario year (1855-1885).
    ///
    /// Baseline provenance: NOAA 1991-2020 U.S. Climate Normals for Bismarck
    /// Municipal Airport, North Dakota (station USW00024011) — the Dakota-region
    /// reference station, records back to 1874. No systematic 30-year normals
    /// exist for 1855-1885, so the 1991-2020 normals are CALIBRATION per
    /// Owner Decisions 1-15 (starting values, not permanent rules); the 1870s
    /// were likely somewhat cooler, but no offset is invented here. The
    /// per-month standard deviations are calibration ESTIMATES, not NOAA
    /// measurements — the published normals do not include them.
    /// </summary>
    public static class ExteriorTemperatureAuthority
    {
        /// <summary>
        /// The acceptance-run seed (RegressionComparator). A seed of 0 means
        /// "unset" in this codebase (hub.WeatherSeed defaults to 0), so 0
        /// resolves to this value — see <see cref="ResolveSeed"/>.
        /// </summary>
        public const int DefaultScenarioSeed = 1886;

        /// <summary>Bismarck record envelope (NOAA extremes 1874-present).</summary>
        public const float RecordLowC = -45f;
        public const float RecordHighC = 46f;

        /// <summary>Anomaly clamp, in standard deviations either side of baseline.</summary>
        public const float AnomalyClampSigma = 3f;

        /// <summary>
        /// Monthly mean daily maximum, °C (Bismarck 1991-2020 normals).
        /// Jan -4.9, Feb -2.3, Mar 5.0, Apr 13.3, May 20.2, Jun 25.5,
        /// Jul 29.3, Aug 28.6, Sep 23.0, Oct 13.9, Nov 4.8, Dec -2.4.
        /// </summary>
        private static readonly float[] MonthlyMeanHighC =
        {
            -4.9f, -2.3f, 5.0f, 13.3f, 20.2f, 25.5f,
            29.3f, 28.6f, 23.0f, 13.9f, 4.8f, -2.4f
        };

        /// <summary>
        /// Monthly mean daily minimum, °C (Bismarck 1991-2020 normals).
        /// Jan -16.4, Feb -13.8, Mar -7.2, Apr -0.9, May 5.8, Jun 11.6,
        /// Jul 14.4, Aug 13.2, Sep 7.7, Oct 0.3, Nov -7.2, Dec -13.3.
        /// </summary>
        private static readonly float[] MonthlyMeanLowC =
        {
            -16.4f, -13.8f, -7.2f, -0.9f, 5.8f, 11.6f,
            14.4f, 13.2f, 7.7f, 0.3f, -7.2f, -13.3f
        };

        /// <summary>
        /// Per-month standard deviation of the daily mean temperature, °C.
        /// CALIBRATION ESTIMATES, not NOAA measurements (see class doc):
        /// a continental-interior station swings widely in winter (Arctic
        /// intrusions vs. chinook warm spells) and less in summer.
        /// Jan 5.0, Feb 4.8, Mar 4.5, Apr 3.8, May 3.2, Jun 2.9,
        /// Jul 2.7, Aug 2.8, Sep 3.1, Oct 3.6, Nov 4.2, Dec 4.8.
        /// </summary>
        private static readonly float[] MonthlySigmaC =
        {
            5.0f, 4.8f, 4.5f, 3.8f, 3.2f, 2.9f,
            2.7f, 2.8f, 3.1f, 3.6f, 4.2f, 4.8f
        };

        /// <summary>One day's exterior temperature: baseline plus seeded anomaly.</summary>
        public struct DailyExteriorTemperature
        {
            public float HighC;
            public float LowC;
            public float MeanC;
            /// <summary>The day's seeded anomaly, °C, applied to high, low and mean alike.</summary>
            public float AnomalyC;
            public int DayOfYear;
            public int Month;
            public int Day;
        }

        /// <summary>Seed 0 means unset in this codebase; it resolves to the scenario seed.</summary>
        public static int ResolveSeed(int seed)
        {
            return seed == 0 ? DefaultScenarioSeed : seed;
        }

        /// <summary>
        /// Baseline (30-year mean) high/low for a day-of-year, by linear
        /// interpolation between the mid-month (day-15) anchors, wrapping
        /// Dec 15 -&gt; Jan 15. No fluctuation: the same day-of-year returns
        /// the same baseline every year.
        /// </summary>
        public static void GetBaseline(int dayOfYear, bool isLeapYear, out float highC, out float lowC)
        {
            GetBaselineCurve(dayOfYear, isLeapYear, out highC, out lowC, out _);
        }

        /// <summary>
        /// The day's exterior temperature: baseline for the day-of-year plus
        /// one seeded normal anomaly (clamped to +/-3 sigma). Same seed and
        /// same day always return the same temperature; different seeds give
        /// different weather. The fluctuation uses a splitmix integer hash,
        /// never System.Random, whose algorithm is not guaranteed stable
        /// across runtimes.
        /// </summary>
        public static DailyExteriorTemperature GetDailyTemperature(int seed, int absoluteDayIndex)
        {
            return GetDailyTemperature(seed, absoluteDayIndex, CalendarAnchor.Default);
        }

        public static DailyExteriorTemperature GetDailyTemperature(int seed, int absoluteDayIndex, CalendarAnchor anchor)
        {
            CalendarAnchor safeAnchor = anchor.IsValid ? anchor : CalendarAnchor.Default;
            safeAnchor.FromAbsoluteDay(Math.Max(0, absoluteDayIndex), out int year, out int month, out int day);
            bool isLeapYear = GregorianDateMath.IsLeapYear(year);
            int dayOfYear = DayOfYear(year, month, day);
            GetBaselineCurve(dayOfYear, isLeapYear, out float baseHighC, out float baseLowC, out float sigmaC);

            float anomalyC = SeededAnomalyC(ResolveSeed(seed), absoluteDayIndex, sigmaC);
            float highC = Clamp(baseHighC + anomalyC, RecordLowC, RecordHighC);
            float lowC = Clamp(baseLowC + anomalyC, RecordLowC, RecordHighC);
            return new DailyExteriorTemperature
            {
                HighC = highC,
                LowC = lowC,
                MeanC = (highC + lowC) * 0.5f,
                AnomalyC = anomalyC,
                DayOfYear = dayOfYear,
                Month = month,
                Day = day
            };
        }

        /// <summary>Day-of-year (1-365/366) for a Gregorian date.</summary>
        public static int DayOfYear(int year, int month, int day)
        {
            int safeMonth = Math.Max(1, Math.Min(12, month));
            int doy = Math.Max(1, Math.Min(GregorianDateMath.DaysInMonth(year, safeMonth), day));
            for (int m = 1; m < safeMonth; m++)
            {
                doy += GregorianDateMath.DaysInMonth(year, m);
            }

            return doy;
        }

        private static void GetBaselineCurve(int dayOfYear, bool isLeapYear, out float highC, out float lowC, out float sigmaC)
        {
            int daysInYear = isLeapYear ? 366 : 365;
            int safeDoy = Math.Max(1, Math.Min(daysInYear, dayOfYear));

            // Mid-month anchor day-of-year for each month.
            int[] anchorDoy = new int[12];
            for (int m = 1; m <= 12; m++)
            {
                anchorDoy[m - 1] = DayOfYear(isLeapYear ? 2000 : 2001, m, 15);
            }

            int loMonth;
            int hiMonth;
            float t;
            if (safeDoy < anchorDoy[0])
            {
                // Before Jan 15: interpolate Dec 15 (previous year) -> Jan 15.
                loMonth = 11;
                hiMonth = 0;
                int span = anchorDoy[0] - (anchorDoy[11] - daysInYear);
                t = (safeDoy - (anchorDoy[11] - daysInYear)) / (float)span;
            }
            else if (safeDoy >= anchorDoy[11])
            {
                // On/after Dec 15: interpolate Dec 15 -> Jan 15 (next year).
                loMonth = 11;
                hiMonth = 0;
                int span = (anchorDoy[0] + daysInYear) - anchorDoy[11];
                t = (safeDoy - anchorDoy[11]) / (float)span;
            }
            else
            {
                loMonth = 0;
                while (loMonth < 10 && safeDoy >= anchorDoy[loMonth + 1])
                {
                    loMonth++;
                }

                hiMonth = loMonth + 1;
                t = (safeDoy - anchorDoy[loMonth]) / (float)(anchorDoy[hiMonth] - anchorDoy[loMonth]);
            }

            t = Clamp(t, 0f, 1f);
            highC = Lerp(MonthlyMeanHighC[loMonth], MonthlyMeanHighC[hiMonth], t);
            lowC = Lerp(MonthlyMeanLowC[loMonth], MonthlyMeanLowC[hiMonth], t);
            sigmaC = Lerp(MonthlySigmaC[loMonth], MonthlySigmaC[hiMonth], t);
        }

        private static float SeededAnomalyC(int seed, int absoluteDayIndex, float sigmaC)
        {
            // Two independent uniform draws from a splitmix32 hash of
            // (seed, day, stream); Box-Muller turns them into a standard
            // normal. One anomaly shifts the whole day's curve, as a real
            // warm or cold day does.
            float u1 = Uniform01(seed, absoluteDayIndex, 0);
            float u2 = Uniform01(seed, absoluteDayIndex, 1);
            u1 = Math.Max(u1, 1e-7f);
            double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            float anomaly = (float)z * sigmaC;
            float clamp = AnomalyClampSigma * sigmaC;
            return Clamp(anomaly, -clamp, clamp);
        }

        private static float Uniform01(int seed, int absoluteDayIndex, int stream)
        {
            uint key = (uint)seed * 0x9E3779B9u
                ^ (uint)(absoluteDayIndex + 1) * 0x85EBCA6Bu
                ^ (uint)(stream + 1) * 0xC2B2AE35u;
            uint hash = SplitMix32(key);
            return (hash >> 8) * (1f / 16777216f);
        }

        private static uint SplitMix32(uint x)
        {
            x += 0x9E3779B9u;
            x = (x ^ (x >> 16)) * 0x85EBCA6Bu;
            x = (x ^ (x >> 13)) * 0xC2B2AE35u;
            return x ^ (x >> 16);
        }

        private static float Clamp(float value, float min, float max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }
    }
}
