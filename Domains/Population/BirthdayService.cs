using System;
using System.Collections.Generic;
using LandLedgers.Time;

namespace LandLedgers.Population
{
    /// <summary>
    /// Pass V P0-2: birthday scheduling on the hub <see cref="IDueEventScheduler"/>.
    ///
    /// One event per person per year with the stable id "birthday:{personId}"
    /// (see <see cref="AgeAuthority.BirthdayEventId"/>). Scheduling the same id
    /// twice REPLACES the event, so EnsureScheduled is idempotent across
    /// save/load and repeated calls — never duplicates, never double-ages.
    ///
    /// The birthday handler never increments an age counter: it re-derives the
    /// completed-years age from the authoritative birth date
    /// (PersonState.RefreshDerivedAge) and reschedules next year's event. A
    /// double-fired event is therefore harmless.
    ///
    /// Deceased persons are excluded (their event is cancelled at death);
    /// off-map persons are included (migration never stops aging).
    /// Plain C# — fully deterministic and testable.
    /// </summary>
    public static class BirthdayService
    {
        /// <summary>
        /// Schedules (or reschedules) the next birthday due-event for one person.
        /// No-op for null persons, invalid birth dates, and the deceased.
        /// Returns null on success, or a refusal reason.
        /// </summary>
        public static string EnsureScheduledForPerson(
            PersonState person,
            IDueEventScheduler scheduler,
            CalendarAnchor anchor,
            int currentAbsoluteDayIndex,
            float gameSecondsPerDay)
        {
            if (person == null || scheduler == null)
            {
                return "BirthdayService: person and scheduler are required.";
            }

            if (person.deathDayIndex >= 0)
            {
                return "BirthdayService: deceased persons are excluded from birthday scheduling.";
            }

            if (!person.birthDate.IsValid)
            {
                return $"BirthdayService: P{person.id} has no valid birth date; cannot schedule.";
            }

            int nextBirthdayDay = AgeAuthority.NextBirthdayAbsoluteDay(
                person.birthDate, currentAbsoluteDayIndex, anchor.Year, anchor.Month, anchor.Day);
            if (nextBirthdayDay < 0)
            {
                return $"BirthdayService: P{person.id} has no computable next birthday.";
            }

            float dueGameSeconds = (float)((double)nextBirthdayDay * Math.Max(1f, gameSecondsPerDay));
            return scheduler.ScheduleWithEventId(
                AgeAuthority.BirthdayEventId(person.id),
                DueEventKind.Birthday,
                dueGameSeconds,
                person.id.ToString());
        }

        /// <summary>
        /// Idempotent pass over the whole population: every living person with a
        /// valid birth date ends up with exactly one scheduled next-birthday
        /// event. Safe to call daily, after generation, and after load.
        /// </summary>
        public static void EnsureScheduledForPopulation(
            PopulationState population,
            IDueEventScheduler scheduler,
            CalendarAnchor anchor,
            int currentAbsoluteDayIndex,
            float gameSecondsPerDay,
            List<string> diagnostics)
        {
            if (population == null || population.people == null || scheduler == null)
            {
                return;
            }

            for (int i = 0; i < population.people.Count; i++)
            {
                PersonState person = population.people[i];
                if (person == null || person.deathDayIndex >= 0 || !person.birthDate.IsValid)
                {
                    continue;
                }

                string refusal = EnsureScheduledForPerson(person, scheduler, anchor, currentAbsoluteDayIndex, gameSecondsPerDay);
                if (!string.IsNullOrEmpty(refusal))
                {
                    diagnostics?.Add(refusal);
                }
            }
        }

        /// <summary>
        /// Handles a fired birthday due-event: re-derives the person's age from
        /// the birth date (never increments) and schedules next year's birthday.
        /// Returns true when the event was consumed for a living person.
        /// </summary>
        public static bool HandleBirthdayDueEvent(
            DueEvent dueEvent,
            PopulationState population,
            IDueEventScheduler scheduler,
            CalendarAnchor anchor,
            int currentAbsoluteDayIndex,
            float gameSecondsPerDay,
            List<string> diagnostics)
        {
            if (dueEvent == null || population == null || scheduler == null)
            {
                return false;
            }

            int personId = ParsePersonId(dueEvent);
            if (personId < 0)
            {
                diagnostics?.Add($"BirthdayService: cannot parse person id from event '{dueEvent.EventId}'.");
                return false;
            }

            PersonState person = population.GetPerson(personId);
            if (person == null)
            {
                // Person is gone (should not happen — identity is never rewritten,
                // but a defensive consume keeps the schedule clean).
                diagnostics?.Add($"BirthdayService: birthday event for unknown P{personId}; consumed without effect.");
                return false;
            }

            if (person.deathDayIndex >= 0)
            {
                diagnostics?.Add($"BirthdayService: P{personId} is deceased; birthday consumed without reschedule.");
                return false;
            }

            anchor.FromAbsoluteDay(currentAbsoluteDayIndex, out int year, out int month, out int day);
            int ageBefore = person.age;
            person.RefreshDerivedAge(year, month, day);

            // Reschedule from tomorrow so the just-fired birthday is never
            // re-selected, even under accelerated multi-day jumps.
            int nextBirthdayDay = AgeAuthority.NextBirthdayAbsoluteDay(
                person.birthDate, currentAbsoluteDayIndex + 1, anchor.Year, anchor.Month, anchor.Day);
            if (nextBirthdayDay < 0)
            {
                diagnostics?.Add($"BirthdayService: P{personId} has no computable next birthday.");
                return true;
            }

            float dueGameSeconds = (float)((double)nextBirthdayDay * Math.Max(1f, gameSecondsPerDay));
            string refusal = scheduler.ScheduleWithEventId(
                AgeAuthority.BirthdayEventId(person.id),
                DueEventKind.Birthday,
                dueGameSeconds,
                person.id.ToString());
            if (!string.IsNullOrEmpty(refusal))
            {
                diagnostics?.Add(refusal);
            }
            else
            {
                diagnostics?.Add($"BirthdayService: P{personId} turned {person.age} (was {ageBefore}) on {year:D4}-{month:D2}-{day:D2}.");
            }

            return true;
        }

        /// <summary>Cancels a person's birthday event. No-op when none is scheduled.</summary>
        public static void CancelForPerson(int personId, IDueEventScheduler scheduler)
        {
            scheduler?.Cancel(AgeAuthority.BirthdayEventId(personId));
        }

        private static int ParsePersonId(DueEvent dueEvent)
        {
            string eventId = dueEvent.EventId ?? string.Empty;
            if (eventId.StartsWith(AgeAuthority.BirthdayEventIdPrefix, StringComparison.Ordinal)
                && int.TryParse(eventId.Substring(AgeAuthority.BirthdayEventIdPrefix.Length), out int fromId))
            {
                return fromId;
            }

            if (int.TryParse(dueEvent.PayloadRef, out int fromPayload))
            {
                return fromPayload;
            }

            return -1;
        }
    }
}
