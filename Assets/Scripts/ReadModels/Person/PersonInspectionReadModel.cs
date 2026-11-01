using System;
using System.Collections.Generic;

namespace LandLedgers.Reporting
{
    /// <summary>
    /// Pass XII Worker D (brief §6.4): the source-side Person/Household
    /// inspection read model — what the player sees when they click on
    /// a person. It composes the existing truthful sources (identity +
    /// household + employment from PersonCausalProfile-style inputs,
    /// current activity from PersonActivityReadModel-style inputs) into
    /// ONE inspection surface, and applies the observer-visibility rule
    /// per field instead of leaking private knowledge:
    ///
    /// Access rule per field (enforced in <see cref="Build"/>):
    /// - Identity (name, alive/deceased, derived age): everyone.
    /// - Household membership: self + household members only. A stranger
    ///   sees no household id or name (the causal profile's
    ///   RedactForObserver rule).
    /// - Current activity + location + destination: self, household
    ///   members, or an observer at the same/public location
    ///   (ObserverVisibility.CanSeePersonActivity). Anyone else sees
    ///   "unknown" activity and no location.
    /// - Purpose, current task, employer, blockers: self + household
    ///   members only (ObserverVisibility.CanSeePersonPurpose).
    /// - Finances where legitimately known: self + household members
    ///   only. Business financials are NOT included here at all — they
    ///   remain under ObserverVisibility.CanSeeBusinessFinancials
    ///   (owner + delegated managers) on the business surface.
    /// - Needs: only the caller-supplied summary is shown; when the
    ///   caller supplies none, the field says "not recorded" rather
    ///   than inventing needs. (Feed from HouseholdNeedRegistry.)
    /// - Recent events: self + household members see demographic and
    ///   employment history; strangers see none.
    /// - Every blanked field is explained in <see cref="AccessNotes"/>.
    ///
    /// Clicking, selection, camera, and HUD presentation are Codex
    /// work. This model is the data contract the click handler binds.
    /// </summary>
    [Serializable]
    public sealed class PersonInspectionReadModel
    {
        [Serializable]
        public sealed class InspectionEvent
        {
            public int DayIndex = -1;
            public string Kind = string.Empty;
            public string Detail = string.Empty;
        }

        public int PersonId = -1;
        public string DisplayName = string.Empty;
        public bool IsDeceased;
        public int DerivedAgeYears = -1;

        public int HouseholdId = -1;
        public string HouseholdName = string.Empty;
        public bool HouseholdVisible;

        public string WorkDescription = string.Empty;
        public string EmploymentState = string.Empty;
        public bool WorkVisible;

        public string CurrentActivity = string.Empty;
        public string CurrentTaskLabel = string.Empty;
        public string LocationId = string.Empty;
        public string DestinationId = string.Empty;
        public int ActivityStartMinuteOfDay = -1;
        public int RemainingMinutes = -1;
        public bool ActivityVisible;
        public bool PurposeVisible;

        public string HousingSummary = string.Empty;
        public bool HousingVisible;

        public string FinancesSummary = string.Empty;
        public bool FinancesVisible;

        public string NeedsSummary = string.Empty;

        public List<InspectionEvent> RecentEvents = new List<InspectionEvent>();
        public bool EventsVisible;

        public List<string> AccessNotes = new List<string>();

        /// <summary>
        /// Builds the inspection model. Visibility is computed through
        /// ObserverVisibility from the observer context; callers that
        /// cannot supply an observer pass null and get the minimal view
        /// (identity only).
        /// </summary>
        public static PersonInspectionReadModel Build(
            int personId,
            string displayName,
            bool isDeceased,
            int derivedAgeYears,
            int householdId,
            string householdName,
            string workDescription,
            string employmentState,
            string currentActivity,
            string currentTaskLabel,
            string locationId,
            string destinationId,
            int activityStartMinuteOfDay,
            int remainingMinutes,
            string housingSummary,
            string financesSummary,
            string needsSummary,
            IReadOnlyList<InspectionEvent> recentEvents,
            ObserverContext observer)
        {
            var model = new PersonInspectionReadModel
            {
                PersonId = personId,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? ("P" + personId) : displayName,
                IsDeceased = isDeceased,
                DerivedAgeYears = derivedAgeYears,
            };

            bool isSelf = observer != null && observer.ObserverPersonId == personId;
            bool isHousehold = observer != null && observer.ObserverHouseholdId >= 0
                && householdId >= 0 && observer.ObserverHouseholdId == householdId;
            bool seesActivity = observer != null && ObserverVisibility.CanSeePersonActivity(
                observer.ObserverPersonId, personId,
                observer.ObserverHouseholdId, householdId,
                observer.ObserverLocationId, locationId ?? string.Empty,
                observer.IsPublicLocation);
            bool seesPurpose = observer != null && ObserverVisibility.CanSeePersonPurpose(
                observer.ObserverPersonId, personId,
                observer.ObserverHouseholdId, householdId);

            if (isSelf || isHousehold)
            {
                model.HouseholdId = householdId;
                model.HouseholdName = householdName ?? string.Empty;
                model.HouseholdVisible = true;
                model.WorkDescription = workDescription ?? string.Empty;
                model.EmploymentState = employmentState ?? string.Empty;
                model.WorkVisible = true;
                model.HousingSummary = housingSummary ?? string.Empty;
                model.HousingVisible = true;
                model.FinancesSummary = financesSummary ?? string.Empty;
                model.FinancesVisible = true;
                model.EventsVisible = true;
                if (recentEvents != null)
                {
                    foreach (InspectionEvent e in recentEvents)
                    {
                        if (e != null) model.RecentEvents.Add(e);
                    }
                }
            }
            else
            {
                model.AccessNotes.Add("Household, work, housing, finances, and history hidden: observer is not the subject or their household.");
            }

            if (seesActivity)
            {
                model.CurrentActivity = currentActivity ?? string.Empty;
                model.LocationId = locationId ?? string.Empty;
                model.DestinationId = destinationId ?? string.Empty;
                model.ActivityStartMinuteOfDay = activityStartMinuteOfDay;
                model.RemainingMinutes = remainingMinutes;
                model.ActivityVisible = true;
            }
            else
            {
                model.CurrentActivity = "unknown";
                model.AccessNotes.Add("Current activity hidden: observer is not at the subject's location and it is not public.");
            }

            if (seesPurpose && seesActivity)
            {
                model.CurrentTaskLabel = currentTaskLabel ?? string.Empty;
                model.PurposeVisible = true;
            }
            else if (seesActivity)
            {
                model.AccessNotes.Add("Task and purpose hidden: observer is not the subject or their household.");
            }

            model.NeedsSummary = string.IsNullOrWhiteSpace(needsSummary)
                ? "Needs: not recorded in this view."
                : needsSummary;
            return model;
        }
    }
}
