using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy.Legal
{
    /// <summary>
    /// D3D: the dated legal jurisdiction. Canon §1.6C: the simulation queries
    /// a small set of dated legal authorities rather than exposing
    /// nineteenth-century filing work to the player.
    /// </summary>
    public enum LegalJurisdiction
    {
        Unspecified = 0,
        DakotaTerritory = 1, // through November 1, 1889
        SouthDakota = 2,     // from statehood, November 2, 1889
    }

    /// <summary>
    /// D3D: the federal bankruptcy posture. Canon §1.6B: the campaign begins
    /// inside the 1878-1898 federal bankruptcy gap; on July 1, 1898 the
    /// Bankruptcy Act of 1898 creates a new federal distress route for
    /// qualifying debtors. It is not a modern universal reset.
    /// </summary>
    public enum FederalBankruptcyPosture
    {
        Unspecified = 0,
        GapNoFederalRoute = 1, // 1878 through June 30, 1898
        BankruptcyAct1898 = 2, // from July 1, 1898; qualifying debtors only
    }

    /// <summary>
    /// D3D: which era of creditor/mortgage/chattel enforcement rules applies.
    /// Canon §1.6B: the previously validated 1892 South Dakota creditor rules
    /// remain later-campaign anchors. Their exact content is a research hold
    /// (Canon §9.1: "Exact court procedure, lien priority, redemption and
    /// filing law remain dated research") — this era marker is the dated
    /// structure; it does not invent the content.
    /// </summary>
    public enum CreditorRulesEra
    {
        Unspecified = 0,
        Territorial = 1,
        SouthDakota1892Anchored = 2,
    }

    /// <summary>
    /// D3D: how public credit/subscription aid to private corporations is
    /// treated. Canon §1.6B: the federal act of July 30, 1886 "sharply
    /// restricts" public credit or subscription aid to private corporations —
    /// restricted, not banned. Exact mechanism is dated research.
    /// </summary>
    public enum PublicAidPosture
    {
        Unspecified = 0,
        Unrestricted = 1,      // before the July 30, 1886 federal act
        SharplyRestricted = 2, // from July 30, 1886
    }

    /// <summary>D3D: a calendar date the regime resolved from a day index.</summary>
    [Serializable]
    public struct LegalRegimeDate
    {
        public int Year;
        public int Month;
        public int Day;

        public override string ToString()
        {
            return string.Format("{0:D4}-{1:D2}-{2:D2}", Year, Month, Day);
        }
    }

    /// <summary>
    /// D3D: the dated legal-regime authority (Canon §1.6C; Tech X §6.5
    /// "LegalRegime determines which procedures/remedies exist by
    /// date/jurisdiction"; Tech X §6.3 "Exact estate/legal procedure remains
    /// in dated LegalRegime data").
    ///
    /// Jurisdiction and regime rules live here as data and query methods —
    /// W9 carried a "territorial recording system" assumption inline in its
    /// deed service; that assumption is now a dated regime rule here, with
    /// behavior preserved. Nothing here moves title, decides disputes, or
    /// mints documents: the regime answers what the law permits; systems
    /// decide what happens.
    ///
    /// Dated canon facts encoded (Project Bible §1.6B):
    /// - Spring 1880 opening: Dakota's general framework uses a seven-percent
    ///   legal/default rate and permits written contractual interest up to
    ///   twelve percent.
    /// - From 1881 through June 30, 1887, covered Black Hills counties permit
    ///   written contracts at any mutually agreed interest rate; the general
    ///   ceiling returns July 1, 1887.
    /// - Statehood November 2, 1889 does not reset private law: existing
    ///   contracts, mortgages, corporations, lawsuits, bonds and rights
    ///   continue. South Dakota institutions/statutes refine the regime
    ///   during 1890-92.
    /// - The federal act of July 30, 1886 places a four-percent general debt
    ///   ceiling on territorial political subdivisions and sharply restricts
    ///   public credit or subscription aid to private corporations; the South
    ///   Dakota Constitution later uses a five-percent general local debt
    ///   ceiling (applied here from statehood — calibration, documented).
    /// - Federal bankruptcy gap 1878-1898; Bankruptcy Act route from
    ///   July 1, 1898 for qualifying debtors only.
    /// - Historical homestead and personal-property exemptions already exist
    ///   before statehood; separately owned spousal property is not reachable
    ///   as the business debtor's property; homestead transfer or encumbrance
    ///   can require spousal participation.
    ///
    /// The epoch (which calendar date day index 0 means) is configuration,
    /// not a constant: Canon §2.1B fixes the opening at "spring 1880"; the
    /// default anchors day 0 to April 1, 1880.
    /// </summary>
    [Serializable]
    public sealed class LegalRegime
    {
        // Epoch: day index 0 == this calendar date (Canon §2.1B: spring 1880 opening).
        public int EpochYear = 1880;
        public int EpochMonth = 4;
        public int EpochDay = 1;

        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;

        public LegalRegime() { }

        public LegalRegime(int epochYear, int epochMonth, int epochDay)
        {
            EpochYear = epochYear;
            EpochMonth = epochMonth;
            EpochDay = epochDay;
        }

        #region Calendar math (proleptic Gregorian; exact for the 1880-1905 campaign)

        private static bool IsLeapYear(int year)
        {
            return (year % 4 == 0 && year % 100 != 0) || year % 400 == 0;
        }

        private static int DaysInMonth(int year, int month)
        {
            switch (month)
            {
                case 2: return IsLeapYear(year) ? 29 : 28;
                case 4:
                case 6:
                case 9:
                case 11: return 30;
                default: return 31;
            }
        }

        /// <summary>Absolute day count from a fixed origin (0001-01-01).</summary>
        private static long AbsoluteDayNumber(int year, int month, int day)
        {
            long days = 0;
            for (int y = 1; y < year; y++)
                days += IsLeapYear(y) ? 366 : 365;
            for (int m = 1; m < month; m++)
                days += DaysInMonth(year, m);
            return days + day - 1;
        }

        private long AbsoluteDayForIndex(int dayIndex)
        {
            return AbsoluteDayNumber(EpochYear, EpochMonth, EpochDay) + dayIndex;
        }

        private bool OnOrAfter(int dayIndex, int year, int month, int day)
        {
            return AbsoluteDayForIndex(dayIndex) >= AbsoluteDayNumber(year, month, day);
        }

        private bool InRange(int dayIndex, int startYear, int startMonth, int startDay,
            int endYear, int endMonth, int endDay)
        {
            long d = AbsoluteDayForIndex(dayIndex);
            return d >= AbsoluteDayNumber(startYear, startMonth, startDay)
                && d <= AbsoluteDayNumber(endYear, endMonth, endDay);
        }

        /// <summary>
        /// Resolves a calendar date to the simulation day index (inverse of
        /// DateForDayIndex). Useful for tests and for callers that reason in
        /// calendar dates rather than day indexes.
        /// </summary>
        public int DayIndexForDate(int year, int month, int day)
        {
            return (int)(AbsoluteDayNumber(year, month, day) - AbsoluteDayNumber(EpochYear, EpochMonth, EpochDay));
        }

        /// <summary>Resolves a simulation day index to a calendar date (for diagnostics and display).</summary>
        public LegalRegimeDate DateForDayIndex(int dayIndex)
        {
            long remaining = AbsoluteDayNumber(EpochYear, EpochMonth, EpochDay) + dayIndex;
            int year = 1;
            while (true)
            {
                int yearDays = IsLeapYear(year) ? 366 : 365;
                if (remaining < yearDays) break;
                remaining -= yearDays;
                year++;
            }
            int month = 1;
            while (true)
            {
                int monthDays = DaysInMonth(year, month);
                if (remaining < monthDays) break;
                remaining -= monthDays;
                month++;
            }
            return new LegalRegimeDate { Year = year, Month = month, Day = (int)remaining + 1 };
        }

        #endregion

        #region Jurisdiction

        /// <summary>Dakota Territory through Nov 1, 1889; South Dakota from statehood Nov 2, 1889.</summary>
        public LegalJurisdiction JurisdictionForDay(int dayIndex)
        {
            return OnOrAfter(dayIndex, 1889, 11, 2)
                ? LegalJurisdiction.SouthDakota
                : LegalJurisdiction.DakotaTerritory;
        }

        /// <summary>
        /// Canon §1.6B: statehood does not reset private law — existing
        /// contracts, mortgages, corporations, lawsuits, bonds and rights
        /// continue. Always true; the regime never discontinuities private law.
        /// </summary>
        public bool PrivateLawContinuesAcrossStatehood => true;

        #endregion

        #region Interest / usury

        /// <summary>Canon §1.6B: seven-percent legal/default rate at the 1880 opening.</summary>
        public int DefaultLegalRatePercent => 7;

        /// <summary>
        /// The maximum written contractual interest rate in whole percent, or
        /// null when no ceiling applies. Canon §1.6B: twelve percent general
        /// ceiling; covered Black Hills counties permit any mutually agreed
        /// rate from 1881 through June 30, 1887; the general ceiling returns
        /// July 1, 1887. Non-covered counties keep the twelve-percent ceiling
        /// throughout (the canon lifts the ceiling only for covered counties).
        ///
        /// DESIGN FORK (documented, not guessed): the canon does not name the
        /// covered Black Hills counties, so coverage is a caller-supplied
        /// parameter — the regime does not guess which counties qualify.
        /// </summary>
        public int? MaxWrittenContractRatePercent(int dayIndex, bool coveredBlackHillsCounty)
        {
            if (coveredBlackHillsCounty && InRange(dayIndex, 1881, 1, 1, 1887, 6, 30))
                return null; // any mutually agreed rate
            return 12;
        }

        /// <summary>
        /// Whether a mutually agreed written contract rate is lawful on the
        /// given day. These are legal boundaries, not market rates (Canon
        /// §1.6B): actual pricing still comes from capital scarcity, borrower
        /// strength, collateral, purpose, term, lender appetite and competition.
        /// </summary>
        public bool IsAgreedRateLawful(double ratePercent, int dayIndex, bool coveredBlackHillsCounty,
            out string explanation)
        {
            int? ceiling = MaxWrittenContractRatePercent(dayIndex, coveredBlackHillsCounty);
            LegalRegimeDate date = DateForDayIndex(dayIndex);
            if (!ceiling.HasValue)
            {
                explanation = string.Format(
                    "Lawful: on {0} a covered Black Hills county permits any mutually agreed written rate (Canon §1.6B).",
                    date);
                return true;
            }
            if (ratePercent <= ceiling.Value)
            {
                explanation = string.Format(
                    "Lawful: {0}% is within the {1}% written-contract ceiling in force on {2} (Canon §1.6B).",
                    ratePercent, ceiling.Value, date);
                return true;
            }
            explanation = string.Format(
                "Unlawful: {0}% exceeds the {1}% written-contract ceiling in force on {2} (Canon §1.6B).",
                ratePercent, ceiling.Value, date);
            return false;
        }

        #endregion

        #region Municipal debt and public aid

        /// <summary>
        /// The general debt ceiling for political subdivisions as a percent of
        /// assessed valuation, or null where the canon states no ceiling.
        /// Canon §1.6B: four percent from the federal act of July 30, 1886;
        /// five percent under the South Dakota Constitution. The canon does
        /// not state a pre-July-1886 territorial ceiling, so none is invented.
        /// The five-percent figure is applied from statehood (the constitution
        /// becomes operative with the state) — calibration, documented.
        /// </summary>
        public double? MunicipalDebtCeilingPercentOfAssessedValue(int dayIndex)
        {
            if (OnOrAfter(dayIndex, 1889, 11, 2)) return 5.0;
            if (OnOrAfter(dayIndex, 1886, 7, 30)) return 4.0;
            return null; // no canon-stated ceiling before the July 30, 1886 act
        }

        /// <summary>Canon §1.6B: the July 30, 1886 federal act sharply restricts public credit or subscription aid to private corporations.</summary>
        public PublicAidPosture PublicAidPostureForDay(int dayIndex)
        {
            return OnOrAfter(dayIndex, 1886, 7, 30)
                ? PublicAidPosture.SharplyRestricted
                : PublicAidPosture.Unrestricted;
        }

        #endregion

        #region Bankruptcy, homestead, creditor rules

        /// <summary>Canon §1.6B: inside the 1878-1898 gap there is no federal bankruptcy route; from July 1, 1898 the Bankruptcy Act of 1898 route exists for qualifying debtors.</summary>
        public FederalBankruptcyPosture BankruptcyPostureForDay(int dayIndex)
        {
            return OnOrAfter(dayIndex, 1898, 7, 1)
                ? FederalBankruptcyPosture.BankruptcyAct1898
                : FederalBankruptcyPosture.GapNoFederalRoute;
        }

        /// <summary>Canon §1.6B: historical homestead and personal-property exemptions already exist before statehood.</summary>
        public bool HomesteadExemptionRecognized(int dayIndex)
        {
            return true;
        }

        /// <summary>
        /// Canon §1.6B: separately owned spousal property is not silently
        /// reachable as the business debtor's property; legally relevant
        /// homestead rights can require spousal participation in transfer or
        /// encumbrance. Recognized throughout the campaign.
        /// </summary>
        public bool SpousalParticipationRequiredForHomesteadTransferOrEncumbrance(int dayIndex)
        {
            return true;
        }

        /// <summary>
        /// Canon §1.6B: the previously validated 1892 South Dakota creditor
        /// rules remain later-campaign anchors. Era marker only — the exact
        /// content (lien priority, redemption, filing specifics) is a research
        /// hold per Canon §9.1 and is not invented here.
        /// </summary>
        public CreditorRulesEra CreditorRulesEraForDay(int dayIndex)
        {
            return OnOrAfter(dayIndex, 1892, 1, 1)
                ? CreditorRulesEra.SouthDakota1892Anchored
                : CreditorRulesEra.Territorial;
        }

        #endregion

        #region Recording practice (W9-carried, behavior preserved)

        /// <summary>
        /// D3D refactor: W9's deed service assumed inline that territorial
        /// recording systems record questionable instruments too (the defect
        /// becomes color of title for the dispute system rather than blocking
        /// recording). That assumption is now dated regime data — behavior is
        /// UNCHANGED (always true in this regime). Marked as W9-authored
        /// practice, not canon: Canon §1.6C's authority list does not cover
        /// recording practice.
        /// </summary>
        public bool RecordingAcceptsQuestionableInstruments(int dayIndex)
        {
            return true;
        }

        #endregion

        /// <summary>Human-readable regime summary for a day (diagnostics / UI read models).</summary>
        public string DescribeDay(int dayIndex)
        {
            LegalRegimeDate date = DateForDayIndex(dayIndex);
            double? debtCeiling = MunicipalDebtCeilingPercentOfAssessedValue(dayIndex);
            return string.Format(
                "{0}: {1}; default legal rate {2}%; municipal debt ceiling {3}; public aid {4}; federal bankruptcy {5}; creditor rules {6}.",
                date,
                JurisdictionForDay(dayIndex),
                DefaultLegalRatePercent,
                debtCeiling.HasValue ? debtCeiling.Value.ToString("0.#") + "%" : "(no canon-stated ceiling)",
                PublicAidPostureForDay(dayIndex),
                BankruptcyPostureForDay(dayIndex),
                CreditorRulesEraForDay(dayIndex));
        }
    }
}
