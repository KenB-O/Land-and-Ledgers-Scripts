using System;
using UnityEngine;

namespace LandLedgers.Population
{
    /// <summary>
    /// Pass IV (Living World Foundation): person activity conventions.
    ///
    /// The serialized enum remains PersonActivityKind (PersonScheduleTracker.cs) — its
    /// values are NEVER renumbered. Pass IV appends the everyday activities the brief
    /// requires (rest, job search, social, management, unpaid family work) so the single
    /// PersonScheduleTracker can reserve them like any other activity.
    ///
    /// Previously Sleeping, Eating and Travel existed as enum values with ZERO call
    /// sites. Pass IV wires real reservations for them: a person cannot work while
    /// sleeping, travel while smithing, or shop while at work. Some activities are
    /// interruptible (rest, social); others are not safely interruptible (sleep is
    /// interruptible only by emergency; active forging is not).
    ///
    /// Compatible overlap (e.g. passive supervision while resting nearby) is allowed
    /// only where a method explicitly supports it; ordinary conflicting active work
    /// must not overlap. The tracker remains the simultaneity guard.
    ///
    /// Canon: XXXI Part I §§1.2-1.4 (occupation describes; agency determines feasibility).
    /// </summary>
    public static class PersonActivityPolicy
    {
        /// <summary>
        /// Whether an activity may be interrupted mid-window for higher-priority work.
        /// Non-interruptible activities refuse preemption loudly.
        /// </summary>
        public static bool IsInterruptible(PersonActivityKind kind)
        {
            switch (kind)
            {
                case PersonActivityKind.Sleeping:
                case PersonActivityKind.Work:
                case PersonActivityKind.Building:
                case PersonActivityKind.Cooking:
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>
        /// Whether the activity counts against the daily work-time budget (WorkTimeBudgetStore).
        /// Sleep, meals, rest and social time do not consume work minutes; everything
        /// productive does.
        /// </summary>
        public static bool ConsumesWorkBudget(PersonActivityKind kind)
        {
            switch (kind)
            {
                case PersonActivityKind.Work:
                case PersonActivityKind.Shopping:
                case PersonActivityKind.Travel:
                case PersonActivityKind.Cooking:
                case PersonActivityKind.Building:
                case PersonActivityKind.Investigation:
                case PersonActivityKind.JobSeeking:
                case PersonActivityKind.Managing:
                case PersonActivityKind.FamilyWork:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Human-readable description for read models and diagnostics.</summary>
        public static string Describe(PersonActivityKind kind)
        {
            switch (kind)
            {
                case PersonActivityKind.Work: return "Paid or assigned work";
                case PersonActivityKind.Shopping: return "Shopping trip";
                case PersonActivityKind.Travel: return "Travel between locations";
                case PersonActivityKind.Cooking: return "Meal preparation";
                case PersonActivityKind.Building: return "Construction work";
                case PersonActivityKind.Sleeping: return "Sleep";
                case PersonActivityKind.Eating: return "Meal";
                case PersonActivityKind.Investigation: return "Business investigation";
                case PersonActivityKind.Resting: return "Rest";
                case PersonActivityKind.JobSeeking: return "Seeking work";
                case PersonActivityKind.Socializing: return "Social activity";
                case PersonActivityKind.Managing: return "Management responsibilities";
                case PersonActivityKind.FamilyWork: return "Unpaid family work and chores";
                default: return kind.ToString();
            }
        }
    }
}
