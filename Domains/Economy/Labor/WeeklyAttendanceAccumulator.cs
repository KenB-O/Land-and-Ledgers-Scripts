// Pass IX (A2-05): the production weekly attendance accumulator.
//
// Pass VIII built the WeeklyAttendanceProvider seam into
// BusinessRuntimeState.ResolveWeeklyPayroll (payroll pays only weeks
// actually worked), but the only installer was the test-only
// AutonomousSocietyDriver — production payroll ran with a null provider
// and paid a full wage for a mere employment record. This accumulator is
// the production counterpart: SimulationDrivers snapshots each person's
// real worked minutes (WorkTimeBudget.MinutesWorked, recorded only by
// TaskAuthority work) into per-week totals BEFORE the daily budget roll,
// and the provider built here answers payroll's (employmentId, weekIndex)
// question from those totals.
//
// Honesty rules:
// - Minutes enter ONLY from WorkTimeBudget.MinutesWorked — real recorded
//   work. Nothing here invents attendance from an employment record.
// - Unknown employments and persons with no recorded minutes count as
//   absent (fail-closed), exactly like the Pass VIII scenario driver.
// - Dead and off-map persons never count as attended, however many
//   minutes they recorded earlier in the week.
// - Weeks are PayrollDayIndex / 7, the same derivation ResolveWeeklyPayroll
//   uses, and WageSettlementService's replay consults the same provider,
//   so the read model and the cash path can never disagree.
using System;
using System.Collections.Generic;
using LandLedgers.Persistence;
using LandLedgers.Primitives;
using LandLedgers.Time;

namespace LandLedgers.Economy.Labor
{
    /// <summary>
    /// Accumulates real worked minutes per (person, week) for the payroll
    /// attendance seam. Owned by SimulationDrivers in production; plain C#
    /// so the harness exercises the exact production code.
    /// </summary>
    public sealed class WeeklyAttendanceAccumulator
    {
        private readonly Dictionary<long, int> minutesByPersonWeek = new Dictionary<long, int>();
        private int lastObservedDayIndex = -1;

        private static long Key(int personId, int weekIndex)
        {
            return ((long)weekIndex << 32) | (uint)personId;
        }

        /// <summary>The week index payroll uses for a day (PayrollDayIndex / 7).</summary>
        public static int WeekIndexForDay(int dayIndex)
        {
            return dayIndex >= 0 ? dayIndex / 7 : -1;
        }

        /// <summary>Minutes recorded for one person in one week (0 when none).</summary>
        public int MinutesWorkedInWeek(int personId, int weekIndex)
        {
            if (personId < 0 || weekIndex < 0)
            {
                return 0;
            }

            return minutesByPersonWeek.TryGetValue(Key(personId, weekIndex), out int minutes) ? minutes : 0;
        }

        /// <summary>Records worked minutes for a person on a day (idempotent per day via ObserveDay).</summary>
        public void RecordWorkedMinutes(int personId, int dayIndex, int minutes)
        {
            if (personId < 0 || dayIndex < 0 || minutes <= 0)
            {
                return;
            }

            long key = Key(personId, WeekIndexForDay(dayIndex));
            minutesByPersonWeek.TryGetValue(key, out int existing);
            minutesByPersonWeek[key] = existing + minutes;
        }

        /// <summary>
        /// Snapshots one day's worked minutes from the budget store. Must be
        /// called BEFORE WorkTimeBudgetStore.EnsureDay rolls the budgets
        /// (MinutesWorked resets at the day boundary). Calling twice for the
        /// same day is a no-op, so day-boundary replays cannot double-count.
        /// Returns the number of persons with minutes recorded.
        /// </summary>
        public int ObserveDay(WorkTimeBudgetStore budgets, int dayIndex)
        {
            if (budgets == null || dayIndex < 0 || dayIndex <= lastObservedDayIndex)
            {
                return 0;
            }

            lastObservedDayIndex = dayIndex;
            WorkTimeBudgetSaveDto snapshot = budgets.CaptureSaveDto();
            if (snapshot == null || snapshot.budgets == null)
            {
                return 0;
            }

            int recorded = 0;
            foreach (WorkTimeBudget budget in snapshot.budgets)
            {
                if (budget == null || budget.MinutesWorked <= 0)
                {
                    continue;
                }

                EntityId personId = budget.PersonId;
                if (personId.Kind != EntityKind.Person)
                {
                    continue;
                }

                RecordWorkedMinutes(personId.Id, dayIndex, budget.MinutesWorked);
                recorded++;
            }

            return recorded;
        }

        /// <summary>
        /// Builds the WeeklyAttendanceProvider for BusinessRuntimeState:
        /// (employmentId, weekIndex) -> the employment's worker recorded
        /// real minutes that week and is neither dead nor off-map.
        /// </summary>
        public Func<string, int, bool> BuildProvider(
            EmploymentRelationshipRegistry employments,
            Func<int, bool> personUnavailableForPayroll = null)
        {
            return (employmentId, weekIndex) =>
            {
                if (employments == null || string.IsNullOrWhiteSpace(employmentId) || weekIndex < 0)
                {
                    return false;
                }

                if (!employments.TryGetById(employmentId, out EmploymentRelationship employment) || employment == null)
                {
                    return false; // unknown employment: fail closed
                }

                if (personUnavailableForPayroll != null && personUnavailableForPayroll(employment.EmployeePersonId))
                {
                    return false; // dead or off-map: never attended (A2-10)
                }

                return MinutesWorkedInWeek(employment.EmployeePersonId, weekIndex) > 0;
            };
        }

        /// <summary>
        /// Pass IX (A2-05): clears every accumulated week — the save-load seam
        /// calls this (via LoadFromSaveDto) so a restored game never inherits
        /// attendance from a previous session.
        /// </summary>
        public void Clear()
        {
            minutesByPersonWeek.Clear();
            lastObservedDayIndex = -1;
        }

        /// <summary>Save support (CLN-1 pattern): capture.</summary>
        public WeeklyAttendanceSaveDto CaptureSaveDto()
        {
            var dto = new WeeklyAttendanceSaveDto { lastObservedDayIndex = lastObservedDayIndex };
            foreach (KeyValuePair<long, int> entry in minutesByPersonWeek)
            {
                dto.personIds.Add((int)(entry.Key & 0xFFFFFFFFL));
                dto.weekIndexes.Add((int)(entry.Key >> 32));
                dto.minutes.Add(entry.Value);
            }

            return dto;
        }

        /// <summary>Save support (CLN-1 pattern): clear-then-load.</summary>
        public void LoadFromSaveDto(WeeklyAttendanceSaveDto dto)
        {
            Clear();
            if (dto == null || dto.personIds == null || dto.weekIndexes == null || dto.minutes == null)
            {
                return;
            }

            lastObservedDayIndex = dto.lastObservedDayIndex;
            int count = Math.Min(dto.personIds.Count, Math.Min(dto.weekIndexes.Count, dto.minutes.Count));
            for (int i = 0; i < count; i++)
            {
                if (dto.personIds[i] < 0 || dto.weekIndexes[i] < 0 || dto.minutes[i] <= 0)
                {
                    continue;
                }

                minutesByPersonWeek[Key(dto.personIds[i], dto.weekIndexes[i])] = dto.minutes[i];
            }
        }

        /// <summary>Installs the provider on one business runtime state (null-safe).</summary>
        public void InstallOn(
            BusinessRuntimeState runtimeState,
            EmploymentRelationshipRegistry employments,
            Func<int, bool> personUnavailableForPayroll = null)
        {
            if (runtimeState == null)
            {
                return;
            }

            runtimeState.WeeklyAttendanceProvider = BuildProvider(employments, personUnavailableForPayroll);
        }
    }

    /// <summary>Save DTO for the weekly attendance accumulator (parallel lists, CLN-1 pattern).</summary>
    [Serializable]
    public sealed class WeeklyAttendanceSaveDto
    {
        public List<int> personIds = new List<int>();
        public List<int> weekIndexes = new List<int>();
        public List<int> minutes = new List<int>();
        public int lastObservedDayIndex = -1;
    }
}
