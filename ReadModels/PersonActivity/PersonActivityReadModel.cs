using System;
using System.Collections.Generic;

namespace LandLedgers.Reporting
{
    /// <summary>
    /// Pass IV (Worker 4, Phase Q): one person's live activity, production-
    /// backed. Every field derives from the schedule tracker / task authority;
    /// nothing is invented. The bounded history keeps the last 16 activity
    /// transitions — never a tick log.
    ///
    /// Observer-visibility: call <see cref="RedactForObserver"/> before showing
    /// to anyone but the subject. Strangers at a shared/public location see the
    /// activity but not the purpose, employer, task, or blockers; the full
    /// record is visible only to the subject and their household.
    /// </summary>
    [Serializable]
    public sealed class PersonActivityReadModel
    {
        public const int MaxHistory = 16;

        public int PersonId = -1;
        public string DisplayName = string.Empty;
        public int HouseholdId = -1;
        public string CurrentActivity = string.Empty;
        /// <summary>Why they are doing it — household-visible only.</summary>
        public string Purpose = string.Empty;
        public string LocationId = string.Empty;
        public string DestinationId = string.Empty;
        /// <summary>Task they are working — household-visible only.</summary>
        public string TaskId = string.Empty;
        public string TaskLabel = string.Empty;
        /// <summary>Employer — household-visible only.</summary>
        public string EmployerBusinessId = string.Empty;
        public int ActivityStartMinuteOfDay = -1;
        public int ElapsedMinutes;
        public int RemainingMinutes = -1; // -1 = open-ended
        /// <summary>What is blocking them — household-visible only.</summary>
        public string Blocker = string.Empty;
        public string Outcome = string.Empty;
        public List<ActivityTransition> RecentHistory = new List<ActivityTransition>();
        /// <summary>True when any field was blanked for the observer.</summary>
        public bool Redacted;

        /// <summary>Returns a copy filtered by the observer-visibility rule.</summary>
        public PersonActivityReadModel RedactForObserver(ObserverContext observer)
        {
            var copy = new PersonActivityReadModel
            {
                PersonId = PersonId,
                DisplayName = DisplayName,
                HouseholdId = HouseholdId,
                CurrentActivity = CurrentActivity,
                Purpose = Purpose,
                LocationId = LocationId,
                DestinationId = DestinationId,
                TaskId = TaskId,
                TaskLabel = TaskLabel,
                EmployerBusinessId = EmployerBusinessId,
                ActivityStartMinuteOfDay = ActivityStartMinuteOfDay,
                ElapsedMinutes = ElapsedMinutes,
                RemainingMinutes = RemainingMinutes,
                Blocker = Blocker,
                Outcome = Outcome,
                RecentHistory = new List<ActivityTransition>(RecentHistory),
            };

            if (observer == null)
            {
                copy.BlankSensitive();
                return copy;
            }

            bool seesActivity = ObserverVisibility.CanSeePersonActivity(
                observer.ObserverPersonId, PersonId,
                observer.ObserverHouseholdId, HouseholdId,
                observer.ObserverLocationId, LocationId,
                observer.IsPublicLocation);
            if (!seesActivity)
            {
                copy.CurrentActivity = "unknown";
                copy.LocationId = string.Empty;
                copy.DestinationId = string.Empty;
                copy.ElapsedMinutes = 0;
                copy.RemainingMinutes = -1;
                copy.RecentHistory.Clear();
            }

            bool seesPurpose = ObserverVisibility.CanSeePersonPurpose(
                observer.ObserverPersonId, PersonId,
                observer.ObserverHouseholdId, HouseholdId);
            if (!seesPurpose)
            {
                copy.BlankSensitive();
            }

            return copy;
        }

        private void BlankSensitive()
        {
            Purpose = string.Empty;
            TaskId = string.Empty;
            TaskLabel = string.Empty;
            EmployerBusinessId = string.Empty;
            Blocker = string.Empty;
            Outcome = string.Empty;
            Redacted = true;
        }
    }

    /// <summary>One bounded activity transition — who, what, when, why.</summary>
    [Serializable]
    public sealed class ActivityTransition
    {
        public int DayIndex;
        public int MinuteOfDay;
        public string FromActivity = string.Empty;
        public string ToActivity = string.Empty;
        public string Reason = string.Empty;
    }

    /// <summary>Who is asking, and what they can legitimately observe.</summary>
    [Serializable]
    public sealed class ObserverContext
    {
        public int ObserverPersonId = -1;
        public int ObserverHouseholdId = -1;
        public string ObserverLocationId = string.Empty;
        public Func<string, bool> IsPublicLocation;
        public Func<string, int> OwnerPersonIdOfBusiness;
        public Func<string, int, bool> IsDelegatedManagerOfBusiness;
    }

    /// <summary>Builds <see cref="PersonActivityReadModel"/> from live authorities.</summary>
    public sealed class PersonActivityReadModelBuilder
    {
        /// <summary>Providers, all backed by real state (never fabricated).</summary>
        public Func<int, string> DisplayNameOf;
        public Func<int, int> HouseholdOf;
        /// <summary>Returns the person's current reservation or null.</summary>
        public Func<int, int, PersonActivitySnapshot> CurrentActivityOf;
        public Func<int, List<ActivityTransition>> HistoryOf;

        public PersonActivityReadModel Build(int personId, int dayIndex, int minuteOfDay)
        {
            var model = new PersonActivityReadModel { PersonId = personId };
            model.DisplayName = DisplayNameOf != null ? DisplayNameOf(personId) ?? string.Empty : string.Empty;
            model.HouseholdId = HouseholdOf != null ? HouseholdOf(personId) : -1;

            PersonActivitySnapshot snap = CurrentActivityOf != null ? CurrentActivityOf(personId, dayIndex) : null;
            if (snap != null)
            {
                model.CurrentActivity = snap.ActivityLabel;
                model.Purpose = snap.Purpose;
                model.LocationId = snap.LocationId;
                model.DestinationId = snap.DestinationId;
                model.TaskId = snap.TaskId;
                model.TaskLabel = snap.TaskLabel;
                model.EmployerBusinessId = snap.EmployerBusinessId;
                model.ActivityStartMinuteOfDay = snap.StartMinuteOfDay;
                model.ElapsedMinutes = Math.Max(0, minuteOfDay - snap.StartMinuteOfDay);
                model.RemainingMinutes = snap.RemainingMinutes;
                model.Blocker = snap.Blocker;
                model.Outcome = snap.Outcome;
            }
            else
            {
                model.CurrentActivity = "unscheduled";
            }

            List<ActivityTransition> history = HistoryOf != null ? HistoryOf(personId) : null;
            if (history != null)
            {
                int start = Math.Max(0, history.Count - PersonActivityReadModel.MaxHistory);
                for (int i = start; i < history.Count; i++)
                {
                    if (history[i] != null)
                    {
                        model.RecentHistory.Add(history[i]);
                    }
                }
            }

            return model;
        }
    }

    /// <summary>One person's current activity, sourced from the schedule tracker.</summary>
    [Serializable]
    public sealed class PersonActivitySnapshot
    {
        public string ActivityLabel = string.Empty;
        public string Purpose = string.Empty;
        public string LocationId = string.Empty;
        public string DestinationId = string.Empty;
        public string TaskId = string.Empty;
        public string TaskLabel = string.Empty;
        public string EmployerBusinessId = string.Empty;
        public int StartMinuteOfDay;
        public int RemainingMinutes = -1;
        public string Blocker = string.Empty;
        public string Outcome = string.Empty;
    }
}
