using System.Collections.Generic;

namespace LandLedgers.Population
{
    /// <summary>
    /// Finds real openings in the authoritative per-person schedule. Open task
    /// records alone do not mean a person is occupied for the whole day; the
    /// reservation windows determine which minutes are unavailable.
    /// </summary>
    public static class PersonScheduleWindowFinder
    {
        public static int FindNextAvailableStartMinute(
            PersonScheduleTracker schedules,
            int personId,
            int dayIndex,
            int nowMinute,
            int durationMinutes)
        {
            if (schedules == null || personId < 0) return -1;

            int duration = System.Math.Max(1, durationMinutes);
            int candidate = System.Math.Max(480, nowMinute);
            List<PersonActivityReservation> existing = schedules.ActiveFor(personId, dayIndex);
            while (candidate + duration <= 1440)
            {
                PersonActivityReservation overlap = null;
                foreach (PersonActivityReservation reservation in existing)
                {
                    if (reservation == null || reservation.Released) continue;
                    if (candidate < reservation.EndMinuteOfDay
                        && reservation.StartMinuteOfDay < candidate + duration)
                    {
                        overlap = reservation;
                        break;
                    }
                }

                if (overlap == null) return candidate;
                candidate = System.Math.Max(candidate + 1, overlap.EndMinuteOfDay);
            }

            return -1;
        }

        public static int FindCommonAvailableStartMinute(
            PersonScheduleTracker schedules,
            int firstPersonId,
            int secondPersonId,
            int dayIndex,
            int nowMinute,
            int durationMinutes)
        {
            if (schedules == null || firstPersonId < 0 || secondPersonId < 0) return -1;

            int duration = System.Math.Max(1, durationMinutes);
            int candidate = System.Math.Max(480, nowMinute);
            for (int attempt = 0; attempt < 64; attempt++)
            {
                int firstStart = FindNextAvailableStartMinute(
                    schedules, firstPersonId, dayIndex, candidate, duration);
                int secondStart = FindNextAvailableStartMinute(
                    schedules, secondPersonId, dayIndex, candidate, duration);
                if (firstStart < 0 || secondStart < 0) return -1;

                int commonStart = System.Math.Max(firstStart, secondStart);
                if (commonStart == candidate && candidate + duration <= 1440) return candidate;
                candidate = commonStart;
            }

            return -1;
        }

        public static bool IsAvailableAtMinute(
            PersonScheduleTracker schedules,
            int personId,
            int dayIndex,
            int minuteOfDay)
        {
            int expected = System.Math.Max(480, minuteOfDay);
            return FindNextAvailableStartMinute(schedules, personId, dayIndex,
                minuteOfDay, 1) == expected;
        }
    }
}
