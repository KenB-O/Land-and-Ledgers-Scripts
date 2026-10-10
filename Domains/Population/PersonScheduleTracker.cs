using System;
using System.Collections.Generic;
using LandLedgers.Time;

namespace LandLedgers.Population
{
    /// <summary>
    /// Phase C (Real People): what a Person is doing, and when. Append-only.
    /// Person time is authoritative: a Person cannot simultaneously
    /// work/shop/travel/sleep/cook/build — the existing Time, Task, Activity
    /// and Journey authorities own the minutes; this tracker owns the
    /// simultaneity guard so double-booking is refused LOUDLY, never
    /// silently overlapped.
    /// </summary>
    public enum PersonActivityKind
    {
        Unspecified = 0,
        Work = 1,
        Shopping = 2,
        Travel = 3,
        Cooking = 4,
        Building = 5,
        Sleeping = 6,
        Eating = 7,
    }

    /// <summary>Phase C: one reserved activity window for a Person on one day.</summary>
    [Serializable]
    public sealed class PersonActivityReservation
    {
        public string ReservationId = string.Empty;
        public int PersonId = -1;
        public PersonActivityKind Activity;
        public int DayIndex;
        /// <summary>Minute of day the window starts (0-1439).</summary>
        public int StartMinuteOfDay;
        /// <summary>Whole minutes reserved (TTS-1 quantum, ≥1).</summary>
        public int DurationMinutes;
        public string Label = string.Empty;
        public bool Released;

        public int EndMinuteOfDay => StartMinuteOfDay + Math.Max(1, DurationMinutes);
    }

    /// <summary>
    /// Phase C: the simultaneity guard for Person time. Reservations are
    /// per-day minute windows; any overlap with an unreleased reservation for
    /// the same Person is refused with a loud reason. Complements (never
    /// replaces) the TTS-1 work-time budget: the budget caps total minutes,
    /// this tracker forbids two activities at the same minute.
    /// Save-persisted via the DTO methods on this class.
    /// </summary>
    public sealed class PersonScheduleTracker
    {
        private readonly List<PersonActivityReservation> reservations = new List<PersonActivityReservation>();
        private int nextReservationSequence;

        public IReadOnlyList<PersonActivityReservation> Reservations => reservations;

        /// <summary>
        /// Reserves an activity window. Returns null on success, or a loud
        /// refusal naming the conflicting activity. Double-booking a Person
        /// is refused — never silently overlapped.
        /// </summary>
        public string TryReserve(
            int personId,
            PersonActivityKind activity,
            int dayIndex,
            int startMinuteOfDay,
            int durationMinutes,
            string label)
        {
            if (personId < 0)
            {
                return "Refused: no real Person — only a real Person holds a schedule (Canon 13.4).";
            }

            int start = Math.Max(0, startMinuteOfDay);
            int duration = WorkTimeMath.ClampToWholeMinutes(durationMinutes);
            int end = start + duration;
            if (end > 1440)
            {
                return $"Refused: P{personId} {activity} [{start}-{end}] runs past midnight — split across days instead.";
            }

            for (int i = 0; i < reservations.Count; i++)
            {
                PersonActivityReservation existing = reservations[i];
                if (existing == null || existing.Released || existing.PersonId != personId || existing.DayIndex != dayIndex)
                {
                    continue;
                }

                if (start < existing.EndMinuteOfDay && existing.StartMinuteOfDay < end)
                {
                    return $"Refused: P{personId} is already {existing.Activity} " +
                        $"[{existing.StartMinuteOfDay}-{existing.EndMinuteOfDay}] on day {dayIndex} " +
                        $"({existing.Label}); cannot also do {activity} [{start}-{end}]. " +
                        "Person time is authoritative — one activity at a time.";
                }
            }

            reservations.Add(new PersonActivityReservation
            {
                ReservationId = $"sched-{dayIndex}-{nextReservationSequence++}",
                PersonId = personId,
                Activity = activity,
                DayIndex = dayIndex,
                StartMinuteOfDay = start,
                DurationMinutes = duration,
                Label = label ?? string.Empty,
            });
            return null;
        }

        /// <summary>Releases a reservation (activity finished or cancelled).</summary>
        public void Release(string reservationId)
        {
            if (string.IsNullOrWhiteSpace(reservationId))
            {
                return;
            }

            for (int i = 0; i < reservations.Count; i++)
            {
                if (reservations[i] != null &&
                    string.Equals(reservations[i].ReservationId, reservationId, StringComparison.Ordinal))
                {
                    reservations[i].Released = true;
                    return;
                }
            }
        }

        /// <summary>Unreleased reservations for a Person on a day.</summary>
        public List<PersonActivityReservation> ActiveFor(int personId, int dayIndex)
        {
            var result = new List<PersonActivityReservation>();
            for (int i = 0; i < reservations.Count; i++)
            {
                PersonActivityReservation reservation = reservations[i];
                if (reservation != null && !reservation.Released && reservation.PersonId == personId && reservation.DayIndex == dayIndex)
                {
                    result.Add(reservation);
                }
            }

            return result;
        }

        /// <summary>
        /// §26: what is this Person doing right now? Answers from the
        /// reservation windows — never guessed.
        /// </summary>
        public string DescribeActivity(int personId, int dayIndex, int minuteOfDay)
        {
            for (int i = 0; i < reservations.Count; i++)
            {
                PersonActivityReservation reservation = reservations[i];
                if (reservation == null || reservation.Released || reservation.PersonId != personId || reservation.DayIndex != dayIndex)
                {
                    continue;
                }

                if (minuteOfDay >= reservation.StartMinuteOfDay && minuteOfDay < reservation.EndMinuteOfDay)
                {
                    return $"P{personId} is {reservation.Activity} " +
                        $"[{reservation.StartMinuteOfDay}-{reservation.EndMinuteOfDay}] — {reservation.Label}";
                }
            }

            return $"P{personId} has no scheduled activity at minute {minuteOfDay} on day {dayIndex}.";
        }

        /// <summary>Whether the Person is free for the whole window (no overlap).</summary>
        public bool IsFree(int personId, int dayIndex, int startMinuteOfDay, int durationMinutes)
        {
            int start = Math.Max(0, startMinuteOfDay);
            int duration = WorkTimeMath.ClampToWholeMinutes(durationMinutes);
            int end = start + duration;

            for (int i = 0; i < reservations.Count; i++)
            {
                PersonActivityReservation existing = reservations[i];
                if (existing == null || existing.Released || existing.PersonId != personId || existing.DayIndex != dayIndex)
                {
                    continue;
                }

                if (start < existing.EndMinuteOfDay && existing.StartMinuteOfDay < end)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Save: captures reservation state (owning runtime class holds its DTO).</summary>
        public PersonScheduleTrackerSaveDto CaptureSaveDto()
        {
            return new PersonScheduleTrackerSaveDto
            {
                reservations = new List<PersonActivityReservation>(reservations),
                nextReservationSequence = nextReservationSequence,
            };
        }

        /// <summary>Save: restores reservation state.</summary>
        public void LoadFromSaveDto(PersonScheduleTrackerSaveDto dto)
        {
            reservations.Clear();
            nextReservationSequence = 0;
            if (dto == null)
            {
                return;
            }

            if (dto.reservations != null)
            {
                reservations.AddRange(dto.reservations);
            }

            nextReservationSequence = Math.Max(0, dto.nextReservationSequence);
        }
    }

    /// <summary>Phase C: save DTO for the person schedule tracker.</summary>
    [Serializable]
    public sealed class PersonScheduleTrackerSaveDto
    {
        public List<PersonActivityReservation> reservations = new List<PersonActivityReservation>();
        public int nextReservationSequence;
    }

    /// <summary>
    /// Phase C: shopper assignment policy. Picks a real household member to
    /// shop: adult (or young worker), living, with a free schedule window and
    /// remaining work-time budget for the trip. Shopping is real time away
    /// from other activities — the chosen shopper's other work waits.
    /// </summary>
    public static class ShopperAssignmentPolicy
    {
        /// <summary>
        /// Chooses the shopper. Returns the person id, or -1 with a reason
        /// when nobody can shop (C4: unavailable shopper is a real failure).
        /// </summary>
        public static int ChooseShopper(
            PopulationState population,
            List<int> memberIds,
            int dayIndex,
            int tripStartMinute,
            int tripDurationMinutes,
            PersonScheduleTracker scheduleTracker,
            WorkTimeBudgetStore budgets,
            Func<int, LandLedgers.Primitives.EntityId> personEntityId,
            out string reason)
        {
            reason = null;
            if (population == null || memberIds == null || memberIds.Count == 0)
            {
                reason = "No household members to choose a shopper from.";
                return -1;
            }

            var rejections = new List<string>();
            int fallback = -1;
            string fallbackReason = null;

            for (int i = 0; i < memberIds.Count; i++)
            {
                int personId = memberIds[i];
                PersonState person = population.GetPerson(personId);
                if (person == null)
                {
                    rejections.Add($"P{personId}: not a known person.");
                    continue;
                }

                if (person.deathDayIndex >= 0)
                {
                    rejections.Add($"P{personId}: deceased.");
                    continue;
                }

                if (person.ageBand < AgeBand.YoungWorker16To17)
                {
                    rejections.Add($"P{personId}: {person.ageBand} is too young to shop alone.");
                    continue;
                }

                if (scheduleTracker != null &&
                    !scheduleTracker.IsFree(personId, dayIndex, tripStartMinute, tripDurationMinutes))
                {
                    rejections.Add($"P{personId}: schedule conflict during the trip window.");
                    continue;
                }

                if (budgets != null && personEntityId != null)
                {
                    LandLedgers.Primitives.EntityId eid = personEntityId(personId);
                    if (eid.IsValid)
                    {
                        WorkTimeBudget budget = budgets.GetOrCreate(eid);
                        if (budget.MinutesRemaining < tripDurationMinutes)
                        {
                            rejections.Add($"P{personId}: only {budget.MinutesRemaining} work minutes left, trip needs {tripDurationMinutes}.");
                            continue;
                        }
                    }
                }

                // Prefer full adults; young workers are the fallback.
                if (person.ageBand >= AgeBand.Adult18Plus)
                {
                    reason = null;
                    return personId;
                }

                if (fallback < 0)
                {
                    fallback = personId;
                    fallbackReason = null;
                }
            }

            if (fallback >= 0)
            {
                reason = fallbackReason;
                return fallback;
            }

            reason = "No available shopper: " + string.Join(" ", rejections);
            return -1;
        }
    }
}
