using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using UnityEngine;
using LandLedgers.Persistence;
using LandLedgers.Primitives;

namespace LandLedgers.Time
{
    /// <summary>
    /// TTS-1: the minute is the base unit of work in Land &amp; Ledgers.
    ///
    /// Every job/task duration is expressed in whole minutes with a minimum of one
    /// minute (Kennedy's rule). Skill moves durations down toward the floor but never
    /// below it. The 24-hour clock (<see cref="TimeManager"/>) is the shared reference;
    /// these helpers convert between clock seconds and whole work minutes.
    /// </summary>
    public static class WorkTimeMath
    {
        /// <summary>Game seconds in one work minute. The clock runs in seconds; work is booked in minutes.</summary>
        public const int SecondsPerMinute = 60;

        /// <summary>
        /// Enforces the one-minute floor: any non-positive minute request becomes 1 minute.
        /// A zero-minute job is meaningless — if it takes no time it is not a task.
        /// </summary>
        public static int ClampToWholeMinutes(int minutes)
        {
            return Math.Max(1, minutes);
        }

        /// <summary>
        /// Converts a game-seconds span to whole work minutes, rounding UP so partial
        /// minutes still consume a full minute of budget. Floored at 1.
        /// </summary>
        public static int WholeMinutesUp(float gameSeconds)
        {
            if (gameSeconds <= 0f)
            {
                return 1;
            }

            return Math.Max(1, Mathf.CeilToInt(gameSeconds / SecondsPerMinute));
        }

        /// <summary>Converts whole work minutes to game seconds for clock comparison.</summary>
        public static float MinutesToGameSeconds(int minutes)
        {
            return ClampToWholeMinutes(minutes) * SecondsPerMinute;
        }
    }

    /// <summary>
    /// TTS-1: one Person's daily work-time budget.
    ///
    /// Labor is a real constraint: a Person has a finite number of working minutes per
    /// day, and performing tasks consumes it. When the budget is exhausted, new task
    /// assignment is blocked with a clear reason (never silently). Players and NPCs
    /// share this accounting — there is no separate player logic.
    ///
    /// The budget window follows the 24-hour clock's absolute day index; the owning
    /// <see cref="WorkTimeBudgetStore"/> resets budgets when the day rolls over.
    /// </summary>
    [Serializable]
    public sealed class WorkTimeBudget
    {
        /// <summary>
        /// Default daily working minutes. TUNING VALUE: 10 hours. A starting point, not
        /// a historical claim — Kennedy tunes this without touching logic.
        /// </summary>
        public const int DefaultDailyWorkMinutes = 600;

        [SerializeField]
        private EntityId personId;

        [SerializeField]
        private int dailyBudgetMinutes = DefaultDailyWorkMinutes;

        [SerializeField]
        private int minutesCommitted;

        /// <summary>
        /// NX-1B: work capacity multiplier from nutrition (0..1, default 1).
        /// Malnourished persons have fewer usable minutes — the missed-meal
        /// consequence bites here.
        /// P2: serialized so a save/load keeps today's capacity (it is
        /// re-derived by DailyNeedsService every game-day anyway).
        /// </summary>
        [SerializeField]
        private float capacityMultiplier01 = 1f;

        /// <summary>NX-1B: sets the capacity multiplier (clamped 0..1).</summary>
        public void SetCapacityMultiplier(float multiplier)
        {
            capacityMultiplier01 = Mathf.Clamp01(multiplier);
        }

        public float CapacityMultiplier01 => capacityMultiplier01;

        /// <summary>
        /// Effective usable budget after the capacity multiplier.
        /// </summary>
        public int EffectiveBudgetMinutes => Mathf.FloorToInt(dailyBudgetMinutes * capacityMultiplier01);

        [SerializeField]
        private int minutesWorked;

        public EntityId PersonId => personId;
        public int DailyBudgetMinutes => dailyBudgetMinutes;

        /// <summary>Minutes reserved by assigned but unfinished tasks.</summary>
        public int MinutesCommitted => minutesCommitted;

        /// <summary>Minutes actually worked today.</summary>
        public int MinutesWorked => minutesWorked;

        /// <summary>Minutes still available for new task commitments today.</summary>
        public int MinutesRemaining => Math.Max(0, EffectiveBudgetMinutes - minutesCommitted);

        public WorkTimeBudget()
        {
        }

        public WorkTimeBudget(EntityId personId, int dailyBudgetMinutes)
        {
            if (personId.Kind != EntityKind.Person)
            {
                throw new ArgumentException("WorkTimeBudget is per-Person; got kind " + personId.Kind, nameof(personId));
            }

            this.personId = personId;
            this.dailyBudgetMinutes = Math.Max(1, dailyBudgetMinutes);
        }

        /// <summary>
        /// Reserves budget minutes for a task assignment. Returns false with a human-
        /// readable reason when the budget cannot cover the request.
        /// </summary>
        public bool TryCommit(int minutes, out string rejectionReason)
        {
            int wholeMinutes = WorkTimeMath.ClampToWholeMinutes(minutes);

            if (wholeMinutes > MinutesRemaining)
            {
                rejectionReason = string.Format(
                    "{0} has {1} of {2} daily work minutes remaining (capacity {3:P0}); the task needs {4}.",
                    personId,
                    MinutesRemaining,
                    EffectiveBudgetMinutes,
                    capacityMultiplier01,
                    wholeMinutes);
                return false;
            }

            minutesCommitted += wholeMinutes;
            rejectionReason = null;
            return true;
        }

        /// <summary>Records minutes actually worked (task progress). Does not consume budget twice.</summary>
        public void RecordWorked(int minutes)
        {
            minutesWorked += WorkTimeMath.ClampToWholeMinutes(minutes);
        }

        /// <summary>
        /// Releases a commitment (task cancelled/interrupted before completion) so the
        /// minutes become available again. Never drives the commitment below zero.
        /// </summary>
        public void ReleaseCommitment(int minutes)
        {
            minutesCommitted = Math.Max(0, minutesCommitted - WorkTimeMath.ClampToWholeMinutes(minutes));
        }

        /// <summary>Called when the clock rolls to a new day: a fresh budget, worked history kept on the Person.</summary>
        public void ResetForNewDay()
        {
            minutesCommitted = 0;
            minutesWorked = 0;
        }
    }

    /// <summary>
    /// TTS-1: owns every Person's <see cref="WorkTimeBudget"/>, keyed by Person EntityId.
    /// Follows the 24-hour clock via <see cref="EnsureDay"/>: pass
    /// <see cref="TimeManager"/>'s absolute day index once per tick (or lazily before
    /// booking) and budgets reset on rollover. No Unity dependencies — fully testable.
    /// </summary>
    [Serializable]
    public sealed class WorkTimeBudgetStore
    {
        [SerializeField]
        private List<WorkTimeBudget> budgets = new List<WorkTimeBudget>();

        [SerializeField]
        private int currentAbsoluteDayIndex = -1;

        private readonly Dictionary<EntityId, WorkTimeBudget> lookup = new Dictionary<EntityId, WorkTimeBudget>();

        public int CurrentAbsoluteDayIndex => currentAbsoluteDayIndex;

        public WorkTimeBudget GetOrCreate(EntityId personId, int dailyBudgetMinutes = WorkTimeBudget.DefaultDailyWorkMinutes)
        {
            if (personId.Kind != EntityKind.Person)
            {
                throw new ArgumentException("WorkTimeBudgetStore is keyed by Person EntityId.", nameof(personId));
            }

            RebuildLookupIfNeeded();

            WorkTimeBudget budget;
            if (!lookup.TryGetValue(personId, out budget))
            {
                budget = new WorkTimeBudget(personId, dailyBudgetMinutes);
                budgets.Add(budget);
                lookup[personId] = budget;
            }

            return budget;
        }

        /// <summary>
        /// Convenience: commits task minutes for a Person, creating their budget on first use.
        /// </summary>
        public bool TryCommitTask(EntityId personId, int minutes, out string rejectionReason)
        {
            return GetOrCreate(personId).TryCommit(minutes, out rejectionReason);
        }

        /// <summary>
        /// Advances the store to the clock's absolute day index. Budgets reset when the
        /// day changes. Safe to call every tick; no-ops when the day is unchanged.
        /// </summary>
        public void EnsureDay(int absoluteDayIndex)
        {
            if (absoluteDayIndex == currentAbsoluteDayIndex)
            {
                return;
            }

            currentAbsoluteDayIndex = absoluteDayIndex;
            RebuildLookupIfNeeded();
            foreach (WorkTimeBudget budget in budgets)
            {
                budget.ResetForNewDay();
            }
        }

        private void RebuildLookupIfNeeded()
        {
            if (lookup.Count == budgets.Count)
            {
                return;
            }

            lookup.Clear();
            foreach (WorkTimeBudget budget in budgets)
            {
                lookup[budget.PersonId] = budget;
            }
        }

        /// <summary>
        /// CLN-1: captures work-time budget state for the save pipeline. Budgets and
        /// the current day; the lookup rebuilds on load.
        /// </summary>
        public WorkTimeBudgetSaveDto CaptureSaveDto()
        {
            return new WorkTimeBudgetSaveDto
            {
                budgets = new List<WorkTimeBudget>(budgets),
            };
        }

        /// <summary>CLN-1: restores work-time budget state from the save pipeline.</summary>
        public void LoadFromSaveDto(WorkTimeBudgetSaveDto dto, int absoluteDayIndex)
        {
            budgets.Clear();
            currentAbsoluteDayIndex = absoluteDayIndex;
            if (dto != null && dto.budgets != null)
            {
                budgets.AddRange(dto.budgets);
            }

            RebuildLookupIfNeeded();
        }
    }
}
