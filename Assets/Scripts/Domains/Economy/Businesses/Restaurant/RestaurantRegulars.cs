using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Businesses.Restaurant
{
    /// <summary>
    /// D1E: one habitual diner's record. A regular is recognized by VISITS,
    /// not by promises: person id, first/last visit, visit and meal counts,
    /// preferred meal. Canon §2.5 grounds nutrition in "regularity"; Canon
    /// §7.3E notes recurring demand "improve[s] throughput visibility" — the
    /// house plans its pots against the diners it can count on.
    ///
    /// HARD FORK BOUNDARY (recorded, not built): regulars-buying-on-credit is
    /// a design fork reserved for Kennedy. Nothing here extends credit, runs
    /// a tab, or defers payment: every served meal settles AT SERVICE for its
    /// recorded PriceCents. If the fork ever resolves toward credit, it
    /// belongs in a receivables instrument (Part VI), not in this record.
    /// </summary>
    [Serializable]
    public sealed class RestaurantRegularRecord
    {
        public int PersonId;
        public int FirstVisitDayIndex;
        public int LastVisitDayIndex = -1;
        public int VisitCount;
        public int MealsServedTotal;
        public string PreferredMealId = string.Empty;

        /// <summary>Parallel to <see cref="MealIds"/>: meals served per menu item.</summary>
        public List<string> MealIds = new List<string>();

        /// <summary>Parallel to <see cref="MealIds"/>: meals served of that menu item.</summary>
        public List<int> MealCounts = new List<int>();

        public RestaurantRegularRecord() { }

        public void NoteMeal(string mealId)
        {
            MealsServedTotal++;
            if (string.IsNullOrWhiteSpace(mealId)) return;
            for (int i = 0; i < MealIds.Count; i++)
            {
                if (string.Equals(MealIds[i], mealId, StringComparison.Ordinal))
                {
                    MealCounts[i]++;
                    if (MealCounts[i] > (PreferredMealCount())) PreferredMealId = mealId;
                    return;
                }
            }

            MealIds.Add(mealId);
            MealCounts.Add(1);
            if (string.IsNullOrWhiteSpace(PreferredMealId)) PreferredMealId = mealId;
        }

        private int PreferredMealCount()
        {
            for (int i = 0; i < MealIds.Count; i++)
            {
                if (string.Equals(MealIds[i], PreferredMealId, StringComparison.Ordinal))
                    return MealCounts[i];
            }

            return 0;
        }
    }

    /// <summary>
    /// D1E: the house's regulars book — visit facts only, no tabs, no credit.
    /// Every <see cref="RestaurantShopRuntime.ServeMeal"/> notes the diner's
    /// visit automatically. The book answers three owner questions: who is a
    /// regular (visit threshold), who has lapsed (no visit within the lapse
    /// window — a real business signal), and how many covers the regulars
    /// promise today (throughput visibility for batch planning).
    /// </summary>
    public sealed class RestaurantRegulars
    {
        private readonly List<RestaurantRegularRecord> records = new List<RestaurantRegularRecord>();
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<RestaurantRegularRecord> Records => records;

        /// <summary>TUNING: visits at/above which a diner counts as a regular.</summary>
        public int RegularVisitThreshold = 3;

        /// <summary>TUNING: days without a visit after which a regular counts as lapsed.</summary>
        public int LapsedAfterDays = 14;

        public RestaurantRegulars() { }

        /// <summary>
        /// Notes one diner's visit (called per served meal). Anonymous person
        /// ids are refused loudly — regulars are real persons (Canon §2.11).
        /// </summary>
        public string NoteVisit(int personId, string mealId, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (personId <= 0)
                return "RestaurantRegulars: a visit needs a real person id — anonymous diners are not regulars.";
            if (dayIndex < 0)
                return "RestaurantRegulars: a visit needs a real day.";

            var record = Find(personId);
            if (record == null)
            {
                record = new RestaurantRegularRecord
                {
                    PersonId = personId,
                    FirstVisitDayIndex = dayIndex,
                };
                records.Add(record);
            }

            record.VisitCount++;
            record.LastVisitDayIndex = dayIndex;
            record.NoteMeal(mealId);
            return null;
        }

        public RestaurantRegularRecord Find(int personId)
        {
            foreach (var record in records)
            {
                if (record != null && record.PersonId == personId) return record;
            }

            return null;
        }

        public bool IsRegular(int personId)
        {
            var record = Find(personId);
            return record != null && record.VisitCount >= Math.Max(1, RegularVisitThreshold);
        }

        /// <summary>Regulars whose last visit is older than the lapse window — the house is losing them.</summary>
        public List<RestaurantRegularRecord> LapsedRegulars(int dayIndex)
        {
            var lapsed = new List<RestaurantRegularRecord>();
            foreach (var record in records)
            {
                if (record == null) continue;
                if (record.VisitCount < Math.Max(1, RegularVisitThreshold)) continue;
                if (dayIndex - record.LastVisitDayIndex > Math.Max(1, LapsedAfterDays))
                    lapsed.Add(record);
            }

            return lapsed;
        }

        /// <summary>
        /// Covers the house can plan against today: every non-lapsed regular
        /// is expected for one cover. A planning input, not a promise — the
        /// diners still have to walk in (Canon lock: no demand conjured).
        /// </summary>
        public int ExpectedCovers(int dayIndex)
        {
            int covers = 0;
            foreach (var record in records)
            {
                if (record == null) continue;
                if (record.VisitCount < Math.Max(1, RegularVisitThreshold)) continue;
                if (dayIndex - record.LastVisitDayIndex <= Math.Max(1, LapsedAfterDays)) covers++;
            }

            return covers;
        }

        #region Save / Load
        [Serializable]
        public sealed class RestaurantRegularsSaveDto
        {
            public List<RestaurantRegularRecord> Records = new List<RestaurantRegularRecord>();
            public int RegularVisitThreshold = 3;
            public int LapsedAfterDays = 14;
        }

        public RestaurantRegularsSaveDto CaptureSaveDto()
        {
            var dto = new RestaurantRegularsSaveDto
            {
                RegularVisitThreshold = RegularVisitThreshold,
                LapsedAfterDays = LapsedAfterDays,
            };
            foreach (var record in records)
            {
                if (record != null) dto.Records.Add(record);
            }

            return dto;
        }

        public void LoadFromSaveDto(RestaurantRegularsSaveDto dto)
        {
            records.Clear();
            if (dto == null) return;
            RegularVisitThreshold = Math.Max(1, dto.RegularVisitThreshold);
            LapsedAfterDays = Math.Max(1, dto.LapsedAfterDays);
            if (dto.Records == null) return;
            foreach (var record in dto.Records)
            {
                if (record != null) records.Add(record);
            }
        }
        #endregion
    }
}
