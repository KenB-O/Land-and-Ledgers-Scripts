// Copyright (c) 2026 Land & Ledgers. All rights reserved.
// Pass V P0-1: the calendar epoch is an explicit Gregorian anchor; derived
// dates are true Gregorian math (real month lengths, leap years, weekday
// alignment to real history). These tests pin the anchor facts so the
// 336-day fiction can never silently return.
using LandLedgers.Population;
using LandLedgers.Time;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Time
{
    [TestFixture]
    public sealed class GregorianCalendarMigrationTests
    {
        [Test]
        public void Anchor_DayZero_Is1880_03_01_Monday()
        {
            CalendarAnchor anchor = CalendarAnchor.Default;
            anchor.FromAbsoluteDay(0, out int y, out int m, out int d);

            Assert.AreEqual(1880, y);
            Assert.AreEqual(3, m);
            Assert.AreEqual(1, d);
            // 1880-03-01 was a Monday in real history (Monday-zero index 0).
            Assert.AreEqual(0, GregorianDateMath.DayOfWeekMondayZero(y, m, d));
        }

        [Test]
        public void Anchor_LeapRules_AreTrueGregorian()
        {
            Assert.IsTrue(GregorianDateMath.IsLeapYear(1880), "1880 is a leap year.");
            Assert.IsFalse(GregorianDateMath.IsLeapYear(1900), "1900 is not a leap year.");
            Assert.IsTrue(GregorianDateMath.IsLeapYear(1904), "1904 is a leap year.");
            Assert.AreEqual(29, GregorianDateMath.DaysInMonth(1880, 2));
            Assert.AreEqual(28, GregorianDateMath.DaysInMonth(1900, 2));
            Assert.AreEqual(28, GregorianDateMath.DaysInMonth(1881, 2));
        }

        [Test]
        public void Anchor_MonthLengths_AreReal()
        {
            Assert.AreEqual(31, GregorianDateMath.DaysInMonth(1892, 1));
            Assert.AreEqual(30, GregorianDateMath.DaysInMonth(1892, 4));
            Assert.AreEqual(30, GregorianDateMath.DaysInMonth(1892, 6));
            Assert.AreEqual(31, GregorianDateMath.DaysInMonth(1892, 7));
            Assert.AreEqual(30, GregorianDateMath.DaysInMonth(1892, 11));
            Assert.AreEqual(31, GregorianDateMath.DaysInMonth(1892, 12));
        }

        [Test]
        public void Anchor_WeekdayAlignment_MatchesRealHistory()
        {
            CalendarAnchor anchor = CalendarAnchor.Default;
            // 1892-03-01 was a Tuesday (Monday-zero index 1).
            int abs = anchor.ToAbsoluteDay(1892, 3, 1);
            anchor.FromAbsoluteDay(abs, out int y, out int m, out int d);
            Assert.AreEqual(1, GregorianDateMath.DayOfWeekMondayZero(y, m, d));
        }

        [Test]
        public void ScenarioStart_Is1892_03_01_Tuesday()
        {
            var settings = ScriptableObject.CreateInstance<SimulationTimeSettings>();
            int startDay = settings.GetScenarioStartAbsoluteDayIndex();

            Assert.AreEqual(4383, startDay, "Days between 1880-03-01 and 1892-03-01.");

            SimulationDate date = settings.BuildDateFromAbsoluteDay(startDay);
            Assert.AreEqual(1892, date.Year);
            Assert.AreEqual(3, date.Month);
            Assert.AreEqual(1, date.DayOfMonth);
            Assert.AreEqual(1, date.DayOfWeekIndex, "1892-03-01 was a Tuesday.");
        }

        [Test]
        public void BuildDateFromAbsoluteDay_IsReversibleAcrossLeapBoundaries()
        {
            var settings = ScriptableObject.CreateInstance<SimulationTimeSettings>();
            CalendarAnchor anchor = settings.Anchor;

            int[][] samples = new int[][]
            {
                new[] { 1880, 3, 1 },
                new[] { 1899, 12, 31 },
                new[] { 1900, 2, 28 },
                new[] { 1904, 2, 29 },
                new[] { 1905, 6, 15 },
            };

            foreach (int[] sample in samples)
            {
                int abs = anchor.ToAbsoluteDay(sample[0], sample[1], sample[2]);
                SimulationDate date = settings.BuildDateFromAbsoluteDay(abs);
                Assert.AreEqual(sample[0], date.Year);
                Assert.AreEqual(sample[1], date.Month);
                Assert.AreEqual(sample[2], date.DayOfMonth);
            }
        }

        [Test]
        public void BuildDateFromAbsoluteDay_ClampsPreAnchorDays()
        {
            var settings = ScriptableObject.CreateInstance<SimulationTimeSettings>();
            // 1880-02-29 is one day before the anchor: clamped to the anchor day.
            SimulationDate date = settings.BuildDateFromAbsoluteDay(-1);
            Assert.AreEqual(0, date.AbsoluteDayIndex);
            Assert.AreEqual(1880, date.Year);
            Assert.AreEqual(3, date.Month);
            Assert.AreEqual(1, date.DayOfMonth);
        }

        [Test]
        public void CustomAnchor_IsHonored()
        {
            var settings = ScriptableObject.CreateInstance<SimulationTimeSettings>();
            settings.anchorYear = 1900;
            settings.anchorMonth = 1;
            settings.anchorDay = 1;

            SimulationDate date = settings.BuildDateFromAbsoluteDay(0);
            Assert.AreEqual(1900, date.Year);
            Assert.AreEqual(1, date.Month);
            Assert.AreEqual(1, date.DayOfMonth);
        }

        [Test]
        public void Feb29Birth_ObservesFeb28_InNonLeapYears()
        {
            var birth = new BirthDate(1880, 2, 29, BirthDateProvenance.Exact);
            CalendarAnchor anchor = CalendarAnchor.Default;

            // Day before the observed anniversary: still 0 completed years.
            int dayBefore = anchor.ToAbsoluteDay(1881, 2, 27);
            anchor.FromAbsoluteDay(dayBefore, out int y1, out int m1, out int d1);
            Assert.AreEqual(0, AgeAuthority.CompletedYears(birth, y1, m1, d1));

            // The observed anniversary: 1 completed year.
            int anniversary = anchor.ToAbsoluteDay(1881, 2, 28);
            anchor.FromAbsoluteDay(anniversary, out int y2, out int m2, out int d2);
            Assert.AreEqual(1, AgeAuthority.CompletedYears(birth, y2, m2, d2));

            // Next birthday from before the anniversary lands on Feb 28, not Mar 1.
            int next = AgeAuthority.NextBirthdayAbsoluteDay(
                birth, anchor.ToAbsoluteDay(1881, 1, 5),
                anchor.Year, anchor.Month, anchor.Day);
            anchor.FromAbsoluteDay(next, out int ny, out int nm, out int nd);
            Assert.AreEqual(1881, ny);
            Assert.AreEqual(2, nm);
            Assert.AreEqual(28, nd);
        }

        [Test]
        public void CompletedYears_CrossesYearBoundary()
        {
            var birth = new BirthDate(1860, 5, 10, BirthDateProvenance.GeneratedExact);
            CalendarAnchor anchor = CalendarAnchor.Default;

            int day = anchor.ToAbsoluteDay(1892, 3, 1);
            anchor.FromAbsoluteDay(day, out int y, out int m, out int d);
            Assert.AreEqual(31, AgeAuthority.CompletedYears(birth, y, m, d));
        }
    }
}
