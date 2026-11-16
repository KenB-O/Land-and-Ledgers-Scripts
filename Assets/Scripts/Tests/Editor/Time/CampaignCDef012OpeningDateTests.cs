// Campaign C (DEF-012) — the opening date is no longer five-way split:
// the ordinary dev scenario begins in 1870 (owner Decision 8), the math
// epoch (CalendarAnchor, 1880-03-01) is preserved as a calendar origin, the
// legal epoch is aligned to it, and the scenario start — not the epoch —
// determines the clock (the old Mathf.Max(0, ...) clamp in
// GetScenarioStartAbsoluteDayIndex floored every pre-anchor start to the
// anchor date). SimulationTimeSettings is a plain field bag; `new` is used
// instead of ScriptableObject.CreateInstance so the same tests run in the
// Roslyn harness and in Unity's Test Runner. The TimeManager wiring
// (InitializeState routes the start through calendarDayOffset exactly as
// StartNewScenarioAtDate does) is pinned structurally by reading the
// production files, because TimeManager is too large a closure for the
// focused harness — the same precedent as the package-3 hotkey tests.
using LandLedgers.Economy.Legal;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.Editor.Time
{
    [TestFixture]
    public sealed class CampaignCDef012OpeningDateTests
    {
        [Test]
        public void ScenarioStart_Default_Is1870_01_01()
        {
            var settings = new SimulationTimeSettings();
            Assert.AreEqual(1870, settings.startingYear, "The ordinary dev scenario begins in 1870 (Decision 8).");
            Assert.AreEqual(1, settings.startingMonth);
            Assert.AreEqual(1, settings.startingDayOfMonth);
        }

        [Test]
        public void ScenarioStart_NotFloored_PreAnchorStartIsNegative()
        {
            var settings = new SimulationTimeSettings();
            int startDay = settings.GetScenarioStartAbsoluteDayIndex();

            // The old clamp floored this to 0 (the anchor date, 1880-03-01).
            Assert.AreEqual(-3712, startDay, "1870-01-01 is 3712 days before the 1880-03-01 anchor.");
        }

        [Test]
        public void ScenarioStart_OffsetMechanism_ResolvesTo1870_01_01()
        {
            var settings = new SimulationTimeSettings();
            int startDay = settings.GetScenarioStartAbsoluteDayIndex();

            // The value TimeManager carries as calendarDayOffset; resolved
            // through the anchor it is the ordinary opening, not the anchor.
            CalendarAnchor anchor = settings.Anchor;
            anchor.FromAbsoluteDay(startDay, out int y, out int m, out int d);
            Assert.AreEqual(1870, y);
            Assert.AreEqual(1, m);
            Assert.AreEqual(1, d);
            Assert.AreEqual(5, GregorianDateMath.DayOfWeekMondayZero(y, m, d), "1870-01-01 was a Saturday.");
        }

        [Test]
        public void ScenarioStart_PostAnchorStart_Unchanged()
        {
            var settings = new SimulationTimeSettings();
            settings.startingYear = 1892;
            settings.startingMonth = 3;
            settings.startingDayOfMonth = 1;

            Assert.AreEqual(4383, settings.GetScenarioStartAbsoluteDayIndex(),
                "Days between 1880-03-01 and 1892-03-01; the clamp removal only affects pre-anchor starts.");
        }

        [Test]
        public void ScenarioStart_CustomPreAnchorStart_NotFloored()
        {
            var settings = new SimulationTimeSettings();
            settings.startingYear = 1875;
            settings.startingMonth = 6;
            settings.startingDayOfMonth = 15;

            int startDay = settings.GetScenarioStartAbsoluteDayIndex();
            Assert.AreEqual(-1721, startDay, "1875-06-15 is 1721 days before the 1880-03-01 anchor.");

            CalendarAnchor anchor = settings.Anchor;
            anchor.FromAbsoluteDay(startDay, out int y, out int m, out int d);
            Assert.AreEqual(1875, y);
            Assert.AreEqual(6, m);
            Assert.AreEqual(15, d);
            Assert.AreEqual(1, GregorianDateMath.DayOfWeekMondayZero(y, m, d), "1875-06-15 was a Tuesday.");
        }

        [Test]
        public void LegalEpoch_DayZero_IsCalendarAnchor()
        {
            var regime = new LegalRegime();
            Assert.AreEqual(1880, regime.EpochYear);
            Assert.AreEqual(3, regime.EpochMonth, "Aligned to the calendar anchor (1880-03-01), not April 1.");
            Assert.AreEqual(1, regime.EpochDay);

            LegalRegimeDate d0 = regime.DateForDayIndex(0);
            Assert.AreEqual(1880, d0.Year);
            Assert.AreEqual(3, d0.Month);
            Assert.AreEqual(1, d0.Day);

            Assert.AreEqual(0, regime.DayIndexForDate(1880, 3, 1));
            Assert.AreEqual(31, regime.DayIndexForDate(1880, 4, 1), "April 1 is 31 days after the aligned epoch.");
        }

        [Test]
        public void LegalEpoch_MatchesClockFrame()
        {
            var regime = new LegalRegime();

            // A clock-frame day index (anchor-relative) must read the same
            // date through the regime: the old 31-day skew is gone, so
            // future wiring needs no conversion between the frames.
            int statehoodDay = CalendarAnchor.Default.ToAbsoluteDay(1889, 11, 2);
            Assert.AreEqual(3533, statehoodDay);
            Assert.AreEqual(statehoodDay, regime.DayIndexForDate(1889, 11, 2));
            Assert.AreEqual(LegalJurisdiction.SouthDakota, regime.JurisdictionForDay(statehoodDay));
            Assert.AreEqual(LegalJurisdiction.DakotaTerritory, regime.JurisdictionForDay(statehoodDay - 1));
        }

        [Test]
        public void LegalRules_UnmovedByEpochAlignment()
        {
            var regime = new LegalRegime();

            // Every dated rule is absolute-dated, so aligning the epoch
            // moves no rule's effective date. Each boundary is checked
            // through DayIndexForDate (dates, not stored indices).
            Assert.AreEqual(LegalJurisdiction.DakotaTerritory,
                regime.JurisdictionForDay(regime.DayIndexForDate(1889, 11, 1)));
            Assert.AreEqual(LegalJurisdiction.SouthDakota,
                regime.JurisdictionForDay(regime.DayIndexForDate(1889, 11, 2)));
            Assert.IsNull(regime.MunicipalDebtCeilingPercentOfAssessedValue(regime.DayIndexForDate(1886, 7, 29)));
            Assert.AreEqual(4.0, regime.MunicipalDebtCeilingPercentOfAssessedValue(regime.DayIndexForDate(1886, 7, 30)));
            Assert.AreEqual(5.0, regime.MunicipalDebtCeilingPercentOfAssessedValue(regime.DayIndexForDate(1889, 11, 2)));
            Assert.AreEqual(PublicAidPosture.Unrestricted,
                regime.PublicAidPostureForDay(regime.DayIndexForDate(1886, 7, 29)));
            Assert.AreEqual(PublicAidPosture.SharplyRestricted,
                regime.PublicAidPostureForDay(regime.DayIndexForDate(1886, 7, 30)));
            Assert.AreEqual(FederalBankruptcyPosture.GapNoFederalRoute,
                regime.BankruptcyPostureForDay(regime.DayIndexForDate(1898, 6, 30)));
            Assert.AreEqual(FederalBankruptcyPosture.BankruptcyAct1898,
                regime.BankruptcyPostureForDay(regime.DayIndexForDate(1898, 7, 1)));
            Assert.AreEqual(CreditorRulesEra.Territorial,
                regime.CreditorRulesEraForDay(regime.DayIndexForDate(1891, 12, 31)));
            Assert.AreEqual(CreditorRulesEra.SouthDakota1892Anchored,
                regime.CreditorRulesEraForDay(regime.DayIndexForDate(1892, 1, 1)));
        }

        [Test]
        public void LegalRate_SevenPercent_Unchanged()
        {
            var regime = new LegalRegime();

            // No 1870 rate is invented: Canon states 7% "at the spring 1880
            // opening" and no dated research gives a 1870 rate, so the
            // constant stands at every date (open research question, §9.1).
            Assert.AreEqual(7, regime.DefaultLegalRatePercent);
            Assert.AreEqual(12, regime.MaxWrittenContractRatePercent(0, false));
        }

        [Test]
        public void Anchor_Preserved_1880_03_01()
        {
            CalendarAnchor anchor = CalendarAnchor.Default;
            Assert.AreEqual(1880, anchor.Year);
            Assert.AreEqual(3, anchor.Month);
            Assert.AreEqual(1, anchor.Day);
            Assert.AreEqual(60, anchor.AnchorDayIndex, "1880-03-01 is day 60 of the 1880 frame (leap year).");
        }
    }
}
