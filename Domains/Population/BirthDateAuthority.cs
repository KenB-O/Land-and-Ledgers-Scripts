using System;
using UnityEngine;

namespace LandLedgers.Population
{
    /// <summary>
    /// Pass V P0-2 contract: how confidently a person's birth date is known.
    /// APPEND-ONLY. The simulation's underlying date is always exact; the provenance
    /// records how much of that exactness is documentary fact vs generated truth.
    /// Never present a generated or estimated date as documentary fact.
    /// </summary>
    public enum BirthDateProvenance
    {
        Exact = 0,          // Authored documentary fact (scenario-authored person).
        GeneratedExact = 1, // Internally determined at generation; scenario provenance recorded.
        Estimated = 2,      // Estimated from an age or partial record (e.g. save migration).
        Partial = 3,        // Partial historical information (year-only, etc.).
    }

    /// <summary>
    /// Pass V P0-2 contract: authoritative birth-date representation for a Person.
    /// This is the single age authority. PersonState.age must be derived from this
    /// (completed calendar years), never an independently incremented counter.
    /// </summary>
    [Serializable]
    public struct BirthDate
    {
        [SerializeField] private int year;
        [SerializeField] private int month;
        [SerializeField] private int day;
        [SerializeField] private BirthDateProvenance provenance;
        [SerializeField] private string provenanceNote;

        public int Year => year;
        public int Month => month;
        public int Day => day;
        public BirthDateProvenance Provenance => provenance;
        public string ProvenanceNote => provenanceNote ?? string.Empty;

        public BirthDate(int year, int month, int day,
            BirthDateProvenance provenance = BirthDateProvenance.GeneratedExact,
            string provenanceNote = "")
        {
            this.year = year;
            this.month = month;
            this.day = day;
            this.provenance = provenance;
            this.provenanceNote = provenanceNote ?? string.Empty;
        }

        public bool IsValid =>
            year >= 1700 && year <= 2100 &&
            month >= 1 && month <= 12 &&
            day >= 1 && day <= 31;
    }

    /// <summary>
    /// Pass V P0-2 contract: the single place where a person's age is derived.
    /// Age = completed calendar years between birth date and current Gregorian date.
    /// Birthday due-event id convention: "birthday:{personId}" — stable, idempotent,
    /// one event per person per year, scheduled on the hub DueEventScheduler.
    /// </summary>
    public static class AgeAuthority
    {
        public const string BirthdayEventIdPrefix = "birthday:";

        public static string BirthdayEventId(int personId)
        {
            return BirthdayEventIdPrefix + personId;
        }

        public static int CompletedYears(BirthDate birth, int currentYear, int currentMonth, int currentDay)
        {
            return LandLedgers.Time.GregorianDateMath.CompletedYears(
                birth.Year, birth.Month, birth.Day, currentYear, currentMonth, currentDay);
        }

        /// <summary>
        /// Absolute day index of the person's next birthday on or after fromAbsoluteDay,
        /// using the project leap-day convention. Returns -1 for an invalid birth date.
        /// </summary>
        public static int NextBirthdayAbsoluteDay(
            BirthDate birth, int fromAbsoluteDay,
            int anchorYear, int anchorMonth, int anchorDay)
        {
            if (!birth.IsValid)
            {
                return -1;
            }
            LandLedgers.Time.GregorianDateMath.FromAbsoluteDay(
                fromAbsoluteDay, anchorYear, anchorMonth, anchorDay,
                out int curY, out int curM, out int curD);

            for (int year = curY; year <= curY + 150; year++)
            {
                int annDay = LandLedgers.Time.GregorianDateMath.AnniversaryDay(birth.Month, birth.Day, year);
                int candidate = LandLedgers.Time.GregorianDateMath.ToAbsoluteDay(
                    year, birth.Month, annDay, anchorYear, anchorMonth, anchorDay);
                if (candidate >= fromAbsoluteDay)
                {
                    return candidate;
                }
            }
            return -1;
        }
    }
}
