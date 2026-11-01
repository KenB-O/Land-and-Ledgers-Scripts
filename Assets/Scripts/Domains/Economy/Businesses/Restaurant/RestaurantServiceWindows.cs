using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Businesses.Restaurant
{
    /// <summary>
    /// D1E: one declared mealtime service — the eating house's day has
    /// structure (Canon §2.3 "The Day Belongs to the Person": meals are day
    /// events, not a continuous trickle; Tech X 8.5/8.6 treat meals as
    /// events). A frontier eating house serves set services — the midday
    /// dinner, the evening supper — and plans its pots against them. The
    /// window records the owner's plan (planned covers) and the actual
    /// served count, so under- and over-cooking show up plainly.
    /// Service ids are house vocabulary ("dinner", "supper"); the two names
    /// below are conveniences, not canon.
    /// </summary>
    [Serializable]
    public sealed class RestaurantServiceWindow
    {
        public const string DinnerServiceId = "dinner";
        public const string SupperServiceId = "supper";

        public string ServiceId = string.Empty;
        public string DisplayName = string.Empty;
        public int DayIndex;

        /// <summary>Covers the owner planned for this service (batch planning target).</summary>
        public int PlannedMeals;

        /// <summary>Covers actually served in this service.</summary>
        public int ServedMeals;

        public RestaurantServiceWindow() { }

        public int UnservedPlan => Math.Max(0, PlannedMeals - ServedMeals);
        public int OverPlan => Math.Max(0, ServedMeals - PlannedMeals);
    }

    /// <summary>
    /// D1E: the house's declared services. Windows are data: declaring one
    /// never cooks anything and never invents diners. Meal batches can name
    /// a target service (planning intent); served meals can name the service
    /// they were eaten at (the mealtime record). Meals served with no
    /// declared service are still real meals — they are simply noted as
    /// served outside a declared service, loudly.
    /// </summary>
    public sealed class RestaurantServiceSchedule
    {
        private readonly List<RestaurantServiceWindow> windows = new List<RestaurantServiceWindow>();
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<RestaurantServiceWindow> Windows => windows;

        /// <summary>
        /// Declares a service for a day. The same service id may repeat on
        /// different days, but not twice on one day. Returns a rejection
        /// string, or null on success.
        /// </summary>
        public string DeclareServiceWindow(
            string serviceId, string displayName, int dayIndex, int plannedMeals, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(serviceId))
                return "RestaurantServiceSchedule: a service needs an id ('dinner', 'supper', ...).";
            if (dayIndex < 0)
                return "RestaurantServiceSchedule: a service needs a real day.";

            foreach (var existing in windows)
            {
                if (existing != null
                    && existing.DayIndex == dayIndex
                    && string.Equals(existing.ServiceId, serviceId, StringComparison.OrdinalIgnoreCase))
                {
                    return $"RestaurantServiceSchedule: service '{serviceId}' is already declared for day {dayIndex} — not duplicated.";
                }
            }

            windows.Add(new RestaurantServiceWindow
            {
                ServiceId = serviceId.Trim(),
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? serviceId.Trim() : displayName.Trim(),
                DayIndex = dayIndex,
                PlannedMeals = Math.Max(0, plannedMeals),
            });
            diag.Add($"RestaurantServiceSchedule: service '{serviceId}' declared for day {dayIndex} ({Math.Max(0, plannedMeals)} planned cover(s)).");
            return null;
        }

        public RestaurantServiceWindow FindWindow(string serviceId, int dayIndex)
        {
            if (string.IsNullOrWhiteSpace(serviceId)) return null;
            foreach (var window in windows)
            {
                if (window != null
                    && window.DayIndex == dayIndex
                    && string.Equals(window.ServiceId, serviceId, StringComparison.OrdinalIgnoreCase))
                {
                    return window;
                }
            }

            return null;
        }

        /// <summary>
        /// Records one served cover against the named service. Returns false
        /// (with a loud diagnostic) when no such service was declared — the
        /// meal is still served; the schedule simply does not claim it.
        /// </summary>
        public bool NoteServedCover(string serviceId, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(serviceId)) return true; // no service named — nothing to record against
            var window = FindWindow(serviceId, dayIndex);
            if (window == null)
            {
                diag.Add($"RestaurantServiceSchedule: cover served as '{serviceId}' on day {dayIndex} with no declared service — " +
                    "the meal stands; the schedule does not claim it.");
                return false;
            }

            window.ServedMeals++;
            return true;
        }

        /// <summary>Planned covers across all declared services for the day.</summary>
        public int PlannedCoversOnDay(int dayIndex)
        {
            int total = 0;
            foreach (var window in windows)
            {
                if (window != null && window.DayIndex == dayIndex) total += Math.Max(0, window.PlannedMeals);
            }

            return total;
        }

        #region Save / Load
        [Serializable]
        public sealed class RestaurantServiceScheduleSaveDto
        {
            public List<RestaurantServiceWindow> Windows = new List<RestaurantServiceWindow>();
        }

        public RestaurantServiceScheduleSaveDto CaptureSaveDto()
        {
            var dto = new RestaurantServiceScheduleSaveDto();
            foreach (var window in windows)
            {
                if (window != null) dto.Windows.Add(window);
            }

            return dto;
        }

        public void LoadFromSaveDto(RestaurantServiceScheduleSaveDto dto)
        {
            windows.Clear();
            if (dto?.Windows == null) return;
            foreach (var window in dto.Windows)
            {
                if (window != null) windows.Add(window);
            }
        }
        #endregion
    }
}
