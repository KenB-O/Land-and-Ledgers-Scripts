using System;
using System.Collections.Generic;
using LandLedgers.Time;

namespace LandLedgers.Population
{
    /// <summary>
    /// Pass V P0-2: additive save migration for the birth-date authority.
    ///
    /// Saves written before the birth-date authority have an `age` counter and
    /// no birth date. On load, every person without a valid birth date gets an
    /// ESTIMATED one: birth year = current year minus saved age, January 1.
    /// January 1 is chosen deliberately — CompletedYears(birthYear, 1, 1) equals
    /// the saved age on every date of that year, so the migration never changes
    /// anyone's age; it only anchors the birthday to January 1 going forward.
    ///
    /// The provenance is Estimated with an explicit note. Estimates are never
    /// presented as documentary fact (see BirthDateProvenance).
    /// </summary>
    public static class PopulationSaveMigration
    {
        /// <summary>
        /// Assigns estimated birth dates to every person lacking a valid one and
        /// refreshes all derived ages. Returns the number of estimates assigned.
        /// Idempotent: persons that already have valid birth dates are untouched.
        /// </summary>
        public static int EnsureBirthDates(
            PopulationState state,
            CalendarAnchor anchor,
            int currentAbsoluteDayIndex,
            List<string> diagnostics)
        {
            if (state == null || state.people == null)
            {
                return 0;
            }

            anchor.FromAbsoluteDay(currentAbsoluteDayIndex, out int year, out int month, out int day);
            int estimated = 0;
            for (int i = 0; i < state.people.Count; i++)
            {
                PersonState person = state.people[i];
                if (person == null)
                {
                    continue;
                }

                if (!person.birthDate.IsValid)
                {
                    int savedAge = Math.Max(0, person.age);
                    person.birthDate = new BirthDate(
                        year - savedAge,
                        1,
                        1,
                        BirthDateProvenance.Estimated,
                        $"Estimated from saved age {savedAge} during the Pass V birth-date migration; documented estimate, not documentary fact.");
                    estimated++;
                }

                person.RefreshDerivedAge(year, month, day);
            }

            if (estimated > 0)
            {
                diagnostics?.Add(
                    $"PopulationSaveMigration: assigned {estimated} estimated birth date(s) consistent with saved ages " +
                    $"(anchor {anchor}, load date {year:D4}-{month:D2}-{day:D2}).");
            }

            return estimated;
        }
    }
}
