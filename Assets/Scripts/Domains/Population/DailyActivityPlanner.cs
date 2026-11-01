using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using LandLedgers.Time;

namespace LandLedgers.Population
{
    /// <summary>
    /// Pass IV (Worker 4): the daily activity planner. Reserves REAL person-time
    /// for sleep, meals, travel, rest, job-seeking and management — the activities
    /// that previously had zero reservation call sites. A person cannot work while
    /// sleeping or shop while at work: the PersonScheduleTracker refuses overlaps
    /// loudly, and this planner surfaces those refusals as plan conflicts instead
    /// of silently overlapping.
    ///
    /// Policy honoured (PersonActivityPolicy):
    /// - Sleep is reserved first and is NOT interruptible (except emergencies,
    ///   which go through TryInterrupt with an explicit emergency flag).
    /// - Rest/Socializing/Eating are interruptible; Work/Building/Cooking are not.
    /// - Sleep, meals, rest and social time do NOT consume the work-time budget;
    ///   job-seeking, managing, travel and shopping DO (committed here, loudly
    ///   refused when the budget cannot cover them).
    ///
    /// Household coordination is emergent, not scripted: members are planned in
    /// a deterministic order, every reservation competes for the same exclusive
    /// person-time, and drivers pick whoever is actually free
    /// (FindFreeMemberForWindow) — so one member shops while another works
    /// because the schedule says so, not because a script assigned roles.
    /// </summary>
    public sealed class DailyActivityPlanner
    {
        /// <summary>Default sleep window: 22:00 → 07:00 (540 min across midnight).</summary>
        public const int DefaultSleepStartMinute = 1320;
        public const int DefaultSleepEndMinute = 420;

        /// <summary>Default meals: 07:30 and 18:30, 30 min each.</summary>
        public static readonly int[] DefaultMealMinutes = { 450, 1110 };
        public const int DefaultMealDurationMinutes = 30;

        /// <summary>Per-person inputs for one day's plan. All minutes are minute-of-day.</summary>
        [Serializable]
        public sealed class PersonDayPlanInputs
        {
            public int PersonId = -1;
            public int HouseholdId = -1;
            public AgeBand AgeBand = AgeBand.Adult18Plus;
            public int SleepStartMinute = DefaultSleepStartMinute;
            public int SleepEndMinute = DefaultSleepEndMinute;
            /// <summary>Meal start minutes; null/empty = defaults.</summary>
            public List<int> MealStartMinutes;
            public int MealDurationMinutes = DefaultMealDurationMinutes;
            /// <summary>Desired rest minutes to sprinkle into free gaps (interruptible).</summary>
            public int DesiredRestMinutes = 60;
            /// <summary>Unemployed adult: plan real job-seeking blocks (budget-consuming).</summary>
            public bool IsJobSeeker;
            public int JobSeekingBlockMinutes = 180;
            /// <summary>Delegated manager/owner: plan a management block (budget-consuming).</summary>
            public bool IsManager;
            public int ManagerBlockMinutes = 60;
            /// <summary>Earliest minute productive blocks may start (default 09:00).</summary>
            public int ProductiveDayStartMinute = 540;
        }

        /// <summary>One planned activity: what was (or will be) reserved.</summary>
        [Serializable]
        public sealed class PlannedActivity
        {
            public PersonActivityKind Kind;
            public int StartMinuteOfDay;
            public int DurationMinutes;
            public string Label = string.Empty;
            public string ReservationId = string.Empty;
            public bool Reserved;
            public bool ConsumesBudget;
        }

        /// <summary>The day's plan for one person, with loud conflicts.</summary>
        [Serializable]
        public sealed class PersonDayPlan
        {
            public int PersonId = -1;
            public int HouseholdId = -1;
            public int DayIndex;
            public List<PlannedActivity> Activities = new List<PlannedActivity>();
            /// <summary>Loud refusals encountered while planning (never silent).</summary>
            public List<string> Conflicts = new List<string>();
        }

        /// <summary>
        /// Plans one person's day: reserves sleep, meals, job-seeking/management
        /// blocks and rest in priority order. Returns the plan; conflicts name
        /// whatever already held the person's time.
        /// </summary>
        public PersonDayPlan PlanPersonDay(
            PersonDayPlanInputs inputs,
            int dayIndex,
            PersonScheduleTracker schedules,
            WorkTimeBudgetStore budgets,
            List<string> diag)
        {
            var plan = new PersonDayPlan
            {
                PersonId = inputs != null ? inputs.PersonId : -1,
                HouseholdId = inputs != null ? inputs.HouseholdId : -1,
                DayIndex = dayIndex,
            };
            if (inputs == null || inputs.PersonId < 0 || schedules == null)
            {
                plan.Conflicts.Add("DailyActivityPlanner: no person or no schedule tracker — nothing planned.");
                return plan;
            }

            // 1. Sleep first: non-interruptible, wraps midnight → two windows.
            ReserveSleep(inputs, dayIndex, schedules, plan, diag);

            // 2. Meals: interruptible, no budget.
            List<int> meals = inputs.MealStartMinutes != null && inputs.MealStartMinutes.Count > 0
                ? inputs.MealStartMinutes
                : new List<int>(DefaultMealMinutes);
            foreach (int mealStart in meals)
            {
                ReserveActivity(inputs.PersonId, PersonActivityKind.Eating, dayIndex,
                    mealStart, Math.Max(1, inputs.MealDurationMinutes), "meal",
                    schedules, budgets, plan, diag, commitBudget: false);
            }

            // 3. Productive blocks (budget-consuming): job-seeking, managing.
            if (inputs.IsJobSeeker && inputs.JobSeekingBlockMinutes > 0)
            {
                ReserveInFreeGap(inputs, PersonActivityKind.JobSeeking, dayIndex,
                    inputs.ProductiveDayStartMinute, inputs.JobSeekingBlockMinutes,
                    "job seeking", schedules, budgets, plan, diag, commitBudget: true);
            }

            if (inputs.IsManager && inputs.ManagerBlockMinutes > 0)
            {
                ReserveInFreeGap(inputs, PersonActivityKind.Managing, dayIndex,
                    inputs.ProductiveDayStartMinute, inputs.ManagerBlockMinutes,
                    "management duties", schedules, budgets, plan, diag, commitBudget: true);
            }

            // 4. Rest: interruptible, no budget — fills free gaps up to the desire.
            SprinkleRest(inputs, dayIndex, schedules, plan, diag);

            return plan;
        }

        /// <summary>
        /// Plans every member of a household for the day, in deterministic
        /// (person-id) order so reservations compete fairly for shared nothing —
        /// each person has their own exclusive time.
        /// </summary>
        public List<PersonDayPlan> PlanHouseholdDay(
            List<int> memberIds,
            Func<int, PersonDayPlanInputs> inputsFor,
            int dayIndex,
            PersonScheduleTracker schedules,
            WorkTimeBudgetStore budgets,
            List<string> diag)
        {
            var plans = new List<PersonDayPlan>();
            if (memberIds == null)
            {
                return plans;
            }

            var ordered = new List<int>(memberIds);
            ordered.Sort();
            foreach (int personId in ordered)
            {
                PersonDayPlanInputs inputs = inputsFor != null ? inputsFor(personId) : null;
                if (inputs == null)
                {
                    inputs = new PersonDayPlanInputs { PersonId = personId };
                }

                inputs.PersonId = personId;
                plans.Add(PlanPersonDay(inputs, dayIndex, schedules, budgets, diag));
            }

            return plans;
        }

        /// <summary>
        /// Coordination helper: finds the best member to take a time-boxed duty
        /// (shopping, errand, childcare) — an adult (fallback: young worker) who
        /// is free for the whole window with budget to cover it. No scripting:
        /// whoever the schedule says is free.
        /// </summary>
        public int FindFreeMemberForWindow(
            List<int> memberIds,
            Func<int, AgeBand> ageBandFor,
            int dayIndex,
            int startMinuteOfDay,
            int durationMinutes,
            PersonScheduleTracker schedules,
            WorkTimeBudgetStore budgets,
            out string reason)
        {
            reason = null;
            if (memberIds == null || memberIds.Count == 0)
            {
                reason = "No household members to choose from.";
                return -1;
            }

            var ordered = new List<int>(memberIds);
            ordered.Sort();
            int fallback = -1;
            var rejections = new List<string>();
            foreach (int personId in ordered)
            {
                AgeBand band = ageBandFor != null ? ageBandFor(personId) : AgeBand.Adult18Plus;
                if (band < AgeBand.YoungWorker16To17)
                {
                    rejections.Add("P" + personId + ": too young.");
                    continue;
                }

                if (schedules != null && !schedules.IsFree(personId, dayIndex, startMinuteOfDay, durationMinutes))
                {
                    rejections.Add("P" + personId + ": not free in the window.");
                    continue;
                }

                if (budgets != null)
                {
                    WorkTimeBudget budget = budgets.GetOrCreate(EntityId.For(EntityKind.Person, personId));
                    if (budget.MinutesRemaining < durationMinutes)
                    {
                        rejections.Add("P" + personId + ": budget short.");
                        continue;
                    }
                }

                if (band >= AgeBand.Adult18Plus)
                {
                    return personId;
                }

                if (fallback < 0)
                {
                    fallback = personId;
                }
            }

            if (fallback >= 0)
            {
                return fallback;
            }

            reason = "Nobody free: " + string.Join(" ", rejections);
            return -1;
        }

        /// <summary>
        /// Attempts to interrupt a person's current activity for urgent work.
        /// Only interruptible activities yield; sleep/work/cooking refuse loudly
        /// (PersonActivityPolicy). Returns null on success, else the refusal.
        /// </summary>
        public string TryInterrupt(
            int personId,
            int dayIndex,
            int minuteOfDay,
            string urgentLabel,
            PersonScheduleTracker schedules,
            List<string> diag)
        {
            if (schedules == null || personId < 0)
            {
                return "DailyActivityPlanner: no schedule tracker or person — cannot interrupt.";
            }

            List<PersonActivityReservation> active = schedules.ActiveFor(personId, dayIndex);
            foreach (PersonActivityReservation reservation in active)
            {
                if (reservation == null || reservation.Released)
                {
                    continue;
                }

                if (minuteOfDay < reservation.StartMinuteOfDay || minuteOfDay >= reservation.EndMinuteOfDay)
                {
                    continue;
                }

                if (!PersonActivityPolicy.IsInterruptible(reservation.Activity))
                {
                    string refusal = "Refused: P" + personId + " is " + reservation.Activity +
                        " [" + reservation.StartMinuteOfDay + "-" + reservation.EndMinuteOfDay + "]" +
                        " (" + reservation.Label + ") — not interruptible; urgent work '" +
                        urgentLabel + "' must wait.";
                    if (diag != null)
                    {
                        diag.Add("DailyActivityPlanner: " + refusal);
                    }

                    return refusal;
                }

                schedules.Release(reservation.ReservationId);
                if (diag != null)
                {
                    diag.Add("DailyActivityPlanner: interrupted P" + personId + " " + reservation.Activity +
                        " for urgent work '" + urgentLabel + "'.");
                }

                return null;
            }

            return null; // nothing to interrupt
        }

        // ---------- internals ----------

        private void ReserveSleep(
            PersonDayPlanInputs inputs, int dayIndex, PersonScheduleTracker schedules,
            PersonDayPlan plan, List<string> diag)
        {
            int start = inputs.SleepStartMinute;
            int end = inputs.SleepEndMinute;
            if (start < 0 || start >= 1440 || end < 0 || end > 1440 || start == end)
            {
                plan.Conflicts.Add("DailyActivityPlanner: invalid sleep window — sleep not planned.");
                return;
            }

            if (start < end)
            {
                ReserveActivity(inputs.PersonId, PersonActivityKind.Sleeping, dayIndex,
                    start, end - start, "sleep", schedules, null, plan, diag, commitBudget: false);
            }
            else
            {
                // Wraps midnight: evening part on this day, morning part on this day.
                ReserveActivity(inputs.PersonId, PersonActivityKind.Sleeping, dayIndex,
                    start, 1440 - start, "sleep (evening)", schedules, null, plan, diag, commitBudget: false);
                ReserveActivity(inputs.PersonId, PersonActivityKind.Sleeping, dayIndex,
                    0, end, "sleep (morning)", schedules, null, plan, diag, commitBudget: false);
            }
        }

        private void ReserveInFreeGap(
            PersonDayPlanInputs inputs, PersonActivityKind kind, int dayIndex,
            int earliestStart, int durationMinutes, string label,
            PersonScheduleTracker schedules, WorkTimeBudgetStore budgets,
            PersonDayPlan plan, List<string> diag, bool commitBudget)
        {
            int duration = WorkTimeMath.ClampToWholeMinutes(durationMinutes);
            // Scan forward from the productive start in 15-minute steps for the
            // first free gap that fits. Deterministic.
            for (int start = Math.Max(0, earliestStart); start + duration <= 1440; start += 15)
            {
                if (!schedules.IsFree(inputs.PersonId, dayIndex, start, duration))
                {
                    continue;
                }

                ReserveActivity(inputs.PersonId, kind, dayIndex, start, duration, label,
                    schedules, budgets, plan, diag, commitBudget);
                return;
            }

            plan.Conflicts.Add("DailyActivityPlanner: no free " + duration + "-min gap for " +
                PersonActivityPolicy.Describe(kind) + " (P" + inputs.PersonId + ") — day is full.");
        }

        private void SprinkleRest(
            PersonDayPlanInputs inputs, int dayIndex, PersonScheduleTracker schedules,
            PersonDayPlan plan, List<string> diag)
        {
            int remaining = Math.Max(0, inputs.DesiredRestMinutes);
            if (remaining <= 0)
            {
                return;
            }

            // Fill the largest free gaps first (deterministic scan), 30-min chunks.
            for (int start = 0; start + 30 <= 1440 && remaining > 0; start += 30)
            {
                if (!schedules.IsFree(inputs.PersonId, dayIndex, start, 30))
                {
                    continue;
                }

                int chunk = Math.Min(30, remaining);
                var activity = ReserveActivity(inputs.PersonId, PersonActivityKind.Resting, dayIndex,
                    start, chunk, "rest", schedules, null, plan, diag, commitBudget: false);
                if (activity != null && activity.Reserved)
                {
                    remaining -= chunk;
                }
            }
        }

        /// <summary>
        /// Reserves one activity window. Budget-consuming kinds commit work
        /// minutes first — a failed commit is a loud plan conflict, never a
        /// silent overdraw. Returns the planned activity (Reserved=false when
        /// the tracker refused).
        /// </summary>
        private PlannedActivity ReserveActivity(
            int personId, PersonActivityKind kind, int dayIndex,
            int startMinuteOfDay, int durationMinutes, string label,
            PersonScheduleTracker schedules, WorkTimeBudgetStore budgets,
            PersonDayPlan plan, List<string> diag, bool commitBudget)
        {
            var activity = new PlannedActivity
            {
                Kind = kind,
                StartMinuteOfDay = startMinuteOfDay,
                DurationMinutes = WorkTimeMath.ClampToWholeMinutes(durationMinutes),
                Label = label ?? string.Empty,
                ConsumesBudget = PersonActivityPolicy.ConsumesWorkBudget(kind),
            };

            bool wantsBudget = commitBudget && activity.ConsumesBudget;
            string commitRejection = null;
            if (wantsBudget && budgets != null)
            {
                EntityId person = EntityId.For(EntityKind.Person, personId);
                if (!budgets.GetOrCreate(person).TryCommit(activity.DurationMinutes, out commitRejection))
                {
                    plan.Conflicts.Add("DailyActivityPlanner: budget refused " + activity.DurationMinutes +
                        " min for " + PersonActivityPolicy.Describe(kind) + " (P" + personId + "): " + commitRejection);
                    if (diag != null)
                    {
                        diag.Add("DailyActivityPlanner: budget conflict for P" + personId + " " + kind + " — " + commitRejection);
                    }

                    plan.Activities.Add(activity);
                    return activity;
                }
            }

            string refusal = schedules.TryReserve(personId, kind, dayIndex,
                startMinuteOfDay, activity.DurationMinutes,
                "daily plan: " + label + " (P" + personId + ")");
            if (refusal != null)
            {
                // Roll back the budget commit — the time was never secured.
                if (wantsBudget && budgets != null)
                {
                    budgets.GetOrCreate(EntityId.For(EntityKind.Person, personId))
                        .ReleaseCommitment(activity.DurationMinutes);
                }

                plan.Conflicts.Add("DailyActivityPlanner: " + refusal);
                if (diag != null)
                {
                    diag.Add("DailyActivityPlanner: reservation conflict for P" + personId + " " + kind + " — " + refusal);
                }

                plan.Activities.Add(activity);
                return activity;
            }

            activity.Reserved = true;
            activity.ReservationId = FindReservationId(schedules, personId, dayIndex, kind, startMinuteOfDay);
            plan.Activities.Add(activity);
            if (diag != null)
            {
                diag.Add("DailyActivityPlanner: P" + personId + " " + kind +
                    " [" + startMinuteOfDay + "-" + (startMinuteOfDay + activity.DurationMinutes) + "] reserved.");
            }

            return activity;
        }

        private static string FindReservationId(
            PersonScheduleTracker schedules, int personId, int dayIndex,
            PersonActivityKind kind, int startMinuteOfDay)
        {
            List<PersonActivityReservation> active = schedules.ActiveFor(personId, dayIndex);
            for (int i = active.Count - 1; i >= 0; i--)
            {
                PersonActivityReservation r = active[i];
                if (r != null && r.Activity == kind && r.StartMinuteOfDay == startMinuteOfDay)
                {
                    return r.ReservationId;
                }
            }

            return string.Empty;
        }
    }
}
