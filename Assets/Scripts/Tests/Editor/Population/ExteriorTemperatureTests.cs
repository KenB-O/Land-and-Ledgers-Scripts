using System;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    /// <summary>
    /// Heating lane (Nov 20 2026): the exterior temperature authority.
    /// Baseline provenance: NOAA 1991-2020 U.S. Climate Normals, Bismarck
    /// Municipal Airport ND (station USW00024011) — calibration for the
    /// 1855-1885 window centred on the 1870 scenario year. No test claims
    /// Unity compilation or PlayMode.
    /// </summary>
    [TestFixture]
    public sealed class ExteriorTemperatureTests
    {
        private const int Seed = 1886;

        [Test]
        public void Baseline_June15_MatchesBismarckJuneMeans()
        {
            // The June monthly means are the mid-month anchor values.
            ExteriorTemperatureAuthority.GetBaseline(166, false, out float highC, out float lowC);
            Assert.AreEqual(25.5f, highC, 0.05f, "Bismarck June mean daily max is 25.5 C.");
            Assert.AreEqual(11.6f, lowC, 0.05f, "Bismarck June mean daily min is 11.6 C.");
        }

        [Test]
        public void Baseline_January15_MatchesBismarckJanuaryMeans()
        {
            ExteriorTemperatureAuthority.GetBaseline(15, false, out float highC, out float lowC);
            Assert.AreEqual(-4.9f, highC, 0.05f, "Bismarck January mean daily max is -4.9 C.");
            Assert.AreEqual(-16.4f, lowC, 0.05f, "Bismarck January mean daily min is -16.4 C.");
        }

        [Test]
        public void Baseline_June6_InterpolatesBetweenMayAndJuneAnchors()
        {
            // June 6 (day 157, non-leap) sits between the May 15 and June 15
            // anchors: t = 22/31. High = 20.2 + 0.71 * 5.3 = 23.96;
            // low = 5.8 + 0.71 * 5.8 = 9.92. The owner's "high 26, low 12"
            // example matches the June monthly means (mid-June), not the
            // June 6 interpolation.
            ExteriorTemperatureAuthority.GetBaseline(157, false, out float highC, out float lowC);
            Assert.AreEqual(23.96f, highC, 0.1f);
            Assert.AreEqual(9.92f, lowC, 0.1f);
        }

        [Test]
        public void Baseline_IsSmoothAcrossMonthBoundaries()
        {
            // No month steps: the largest monthly jump (Mar->Apr high, 8.3 C
            // over 31 days) is ~0.27 C per day, so consecutive days differ
            // by well under half a degree everywhere, including Dec->Jan.
            float previousHigh = float.NaN;
            for (int doy = 345; doy <= 375; doy++)
            {
                int wrapped = doy > 365 ? doy - 365 : doy;
                ExteriorTemperatureAuthority.GetBaseline(wrapped, false, out float highC, out _);
                if (!float.IsNaN(previousHigh))
                {
                    Assert.Less(Math.Abs(highC - previousHigh), 0.5f,
                        $"Baseline high must move smoothly across the year boundary (day {wrapped}).");
                }

                previousHigh = highC;
            }
        }

        [Test]
        public void DailyTemperature_IsSeededAndReproducible()
        {
            var first = ExteriorTemperatureAuthority.GetDailyTemperature(Seed, 306);
            var second = ExteriorTemperatureAuthority.GetDailyTemperature(Seed, 306);
            Assert.AreEqual(first.HighC, second.HighC, "Same seed and day must give the same high.");
            Assert.AreEqual(first.LowC, second.LowC, "Same seed and day must give the same low.");
            Assert.AreEqual(first.MeanC, second.MeanC, "Same seed and day must give the same mean.");
            Assert.AreEqual(first.AnomalyC, second.AnomalyC, "Same seed and day must give the same anomaly.");

            // Seed 0 means unset and resolves to the scenario seed.
            var unset = ExteriorTemperatureAuthority.GetDailyTemperature(0, 306);
            Assert.AreEqual(first.MeanC, unset.MeanC, "Seed 0 must resolve to the scenario seed 1886.");

            // A different seed gives different weather on the same day.
            var other = ExteriorTemperatureAuthority.GetDailyTemperature(1887, 306);
            Assert.AreNotEqual(first.MeanC, other.MeanC, "A different seed must give a different day.");
        }

        [Test]
        public void DailyTemperature_HighAboveMeanAboveLow_AndWithinRecordEnvelope()
        {
            // Every day of a year, on two seeds: ordering holds and the
            // record envelope ([-45, 46] C, Bismarck extremes) is respected.
            foreach (int seed in new[] { Seed, 1901 })
            {
                for (int day = 0; day < 365; day++)
                {
                    var temp = ExteriorTemperatureAuthority.GetDailyTemperature(seed, day);
                    Assert.GreaterOrEqual(temp.HighC, temp.MeanC, $"High must be >= mean (seed {seed}, day {day}).");
                    Assert.GreaterOrEqual(temp.MeanC, temp.LowC, $"Mean must be >= low (seed {seed}, day {day}).");
                    Assert.GreaterOrEqual(temp.HighC, ExteriorTemperatureAuthority.RecordLowC);
                    Assert.LessOrEqual(temp.HighC, ExteriorTemperatureAuthority.RecordHighC);
                    Assert.GreaterOrEqual(temp.LowC, ExteriorTemperatureAuthority.RecordLowC);
                    Assert.LessOrEqual(temp.LowC, ExteriorTemperatureAuthority.RecordHighC);
                }
            }
        }

        [Test]
        public void DailyTemperature_WinterHasVisibleSpread_MildAndBrutalDaysBothOccur()
        {
            // Dec 1880 - Feb 1881 (absolute days 275-364), seed 1886: the
            // seeded anomaly must actually vary the weather — a mild
            // January and a brutal January must not burn identically.
            // Baseline winter mean over the window is about -8.9 C.
            float sum = 0f;
            float sumSquares = 0f;
            float min = float.MaxValue;
            float max = float.MinValue;
            int count = 0;
            for (int day = 275; day <= 364; day++)
            {
                float mean = ExteriorTemperatureAuthority.GetDailyTemperature(Seed, day).MeanC;
                sum += mean;
                sumSquares += mean * mean;
                min = Math.Min(min, mean);
                max = Math.Max(max, mean);
                count++;
            }

            float sampleMean = sum / count;
            float variance = sumSquares / count - sampleMean * sampleMean;
            float stddev = (float)Math.Sqrt(Math.Max(0f, variance));
            Assert.AreEqual(-8.9f, sampleMean, 2.5f, "The winter sample mean must sit near the baseline winter mean.");
            Assert.Greater(stddev, 2.0f, "Winter days must actually vary — the standard deviation must be visible.");
            Assert.Greater(max - min, 10f, "The winter range must span more than 10 C.");
            Assert.Less(min, -15f, "A brutal winter day (mean below -15 C) must occur in the sample.");
            Assert.Greater(max, -5f, "A mild winter day (mean above -5 C) must occur in the sample.");
        }

        [Test]
        public void DayOfYear_MatchesGregorianCalendar()
        {
            Assert.AreEqual(1, ExteriorTemperatureAuthority.DayOfYear(1880, 1, 1));
            Assert.AreEqual(60, ExteriorTemperatureAuthority.DayOfYear(1880, 2, 29), "1880 is a leap year.");
            Assert.AreEqual(59, ExteriorTemperatureAuthority.DayOfYear(1881, 2, 28), "1881 is not a leap year.");
            Assert.AreEqual(365, ExteriorTemperatureAuthority.DayOfYear(1881, 12, 31));
            Assert.AreEqual(366, ExteriorTemperatureAuthority.DayOfYear(1880, 12, 31), "1880 has 366 days.");
        }
    }
}
