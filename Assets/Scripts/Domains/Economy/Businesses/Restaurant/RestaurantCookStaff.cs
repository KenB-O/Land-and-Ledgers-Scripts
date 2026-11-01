using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Businesses.Restaurant
{
    /// <summary>
    /// D1E: one cook on the roster — a REAL PERSON (person id) with a Cooking
    /// skill level on the 1-10 PersonSkillState scale and a daily minute
    /// budget. Canon §8.1E: boarding/food-service houses "may need cooks,
    /// waiters, cleaners, clerks"; "loss of a strong cook ... can damage the
    /// establishment even when the building is undamaged."
    /// </summary>
    [Serializable]
    public sealed class RestaurantCookAssignment
    {
        /// <summary>The cook — a real person id, never anonymous.</summary>
        public int PersonId;

        /// <summary>Cooking skill level, 1-10 (1 = untrained beginner). Drives quality band and prep efficiency.</summary>
        public int CookingSkillLevel = 1;

        /// <summary>Hands-on minutes this cook gives the kitchen per day. Defaults to the house calibration.</summary>
        public int MinutesPerDay = RestaurantShopRuntime.CookMinutesPerDay;

        public int AssignedFromDayIndex;
        public bool IsActive = true;

        public RestaurantCookAssignment() { }

        public bool IsOnDuty(int dayIndex)
        {
            return IsActive && PersonId > 0 && dayIndex >= AssignedFromDayIndex && MinutesPerDay > 0;
        }
    }

    /// <summary>
    /// D1E: the kitchen's cook roster. Capacity is the SUM of on-duty cooks'
    /// minutes (Canon §8.1E scale transition: more stoves without more cooks
    /// buys nothing); the served quality follows the LEADING (most skilled)
    /// on-duty cook; prep runs faster under a skilled hand and slower under
    /// a green one. An empty roster means the proprietor cooks (Canon §8.1E:
    /// "small houses can rely heavily on proprietor and family labor") — the
    /// W2B fallback, behavior unchanged. A roster whose cooks have ALL left
    /// is different: nobody is at the stove, and the house says so loudly.
    /// </summary>
    public sealed class RestaurantCookStaff
    {
        private readonly List<RestaurantCookAssignment> cooks = new List<RestaurantCookAssignment>();
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<RestaurantCookAssignment> Cooks => cooks;

        /// <summary>True once the house has ever named a cook — separates "proprietor cooks" from "the cooks all left".</summary>
        public bool HasRoster => cooks.Count > 0;

        /// <summary>
        /// Names a cook to the roster. Re-naming an active cook updates their
        /// skill/minutes rather than duplicating them. Returns a rejection
        /// string, or null on success.
        /// </summary>
        public string AssignCook(int personId, int skillLevel, int dayIndex, List<string> diag, int minutesPerDay = 0)
        {
            diag = diag ?? diagnostics;
            if (personId <= 0)
                return "RestaurantCookStaff: a cook needs a real person id — anonymous cooks are not rostered.";
            if (dayIndex < 0)
                return "RestaurantCookStaff: a cook needs a real assignment day.";

            int clampedSkill = Math.Max(1, Math.Min(10, skillLevel));
            int minutes = minutesPerDay > 0 ? minutesPerDay : RestaurantShopRuntime.CookMinutesPerDay;

            foreach (var existing in cooks)
            {
                if (existing != null && existing.PersonId == personId && existing.IsActive)
                {
                    existing.CookingSkillLevel = clampedSkill;
                    existing.MinutesPerDay = minutes;
                    existing.AssignedFromDayIndex = Math.Min(existing.AssignedFromDayIndex, dayIndex);
                    diag.Add($"RestaurantCookStaff: cook {personId} re-rostered (skill {clampedSkill}, {minutes}m/day).");
                    return null;
                }
            }

            cooks.Add(new RestaurantCookAssignment
            {
                PersonId = personId,
                CookingSkillLevel = clampedSkill,
                MinutesPerDay = minutes,
                AssignedFromDayIndex = dayIndex,
                IsActive = true,
            });
            diag.Add($"RestaurantCookStaff: cook {personId} joined the roster (skill {clampedSkill}, {minutes}m/day from day {dayIndex}).");
            return null;
        }

        /// <summary>
        /// A cook leaves (or is let go). The assignment deactivates but stays
        /// on the books as history — the house remembers it had a cook.
        /// Returns a rejection string, or null on success.
        /// </summary>
        public string RemoveCook(int personId, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            foreach (var existing in cooks)
            {
                if (existing != null && existing.PersonId == personId && existing.IsActive)
                {
                    existing.IsActive = false;
                    diag.Add($"RestaurantCookStaff: cook {personId} left the roster (day {dayIndex}) — " +
                        (HasActiveCook(dayIndex)
                            ? "the kitchen still has hands."
                            : "NO COOK remains: the stove stays cold until someone is named."));
                    return null;
                }
            }

            return $"RestaurantCookStaff: no active cook {personId} to remove — the roster is unchanged.";
        }

        public bool HasActiveCook(int dayIndex)
        {
            foreach (var cook in cooks)
            {
                if (cook != null && cook.IsOnDuty(dayIndex)) return true;
            }

            return false;
        }

        /// <summary>Total hands-on cook minutes available today (the kitchen's labor budget).</summary>
        public int TotalMinutesToday(int dayIndex)
        {
            int total = 0;
            foreach (var cook in cooks)
            {
                if (cook != null && cook.IsOnDuty(dayIndex)) total += Math.Max(0, cook.MinutesPerDay);
            }

            return total;
        }

        /// <summary>The most skilled on-duty cook's level today; 0 when nobody is on duty.</summary>
        public int LeadingCookSkill(int dayIndex)
        {
            int best = 0;
            foreach (var cook in cooks)
            {
                if (cook != null && cook.IsOnDuty(dayIndex))
                    best = Math.Max(best, Math.Max(1, Math.Min(10, cook.CookingSkillLevel)));
            }

            return best;
        }

        /// <summary>
        /// TUNING: prep efficiency by skill — a strong cook works faster, a
        /// green cook slower. Applied to the catalog prep minutes.
        /// </summary>
        public static int EffectivePrepMinutes(int basePrepMinutes, int cookSkillLevel)
        {
            if (basePrepMinutes <= 0) return 0;
            double factor = 1.0;
            if (cookSkillLevel >= 6) factor = 0.8;
            else if (cookSkillLevel >= 3) factor = 1.0;
            else if (cookSkillLevel > 0) factor = 1.25;
            return Math.Max(1, (int)Math.Ceiling(basePrepMinutes * factor));
        }

        #region Save / Load
        [Serializable]
        public sealed class RestaurantCookStaffSaveDto
        {
            public List<RestaurantCookAssignment> Cooks = new List<RestaurantCookAssignment>();
        }

        public RestaurantCookStaffSaveDto CaptureSaveDto()
        {
            var dto = new RestaurantCookStaffSaveDto();
            foreach (var cook in cooks)
            {
                if (cook != null) dto.Cooks.Add(cook);
            }

            return dto;
        }

        public void LoadFromSaveDto(RestaurantCookStaffSaveDto dto)
        {
            cooks.Clear();
            if (dto?.Cooks == null) return;
            foreach (var cook in dto.Cooks)
            {
                if (cook != null) cooks.Add(cook);
            }
        }
        #endregion
    }
}
