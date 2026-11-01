using System;
using System.Collections.Generic;
using LandLedgers.Population;

namespace LandLedgers.Economy.Businesses.Restaurant
{
    /// <summary>
    /// W2B: the day-scoped ledger of meals actually served to real persons —
    /// the producer side of <see cref="IRestaurantMealDaySource"/>. Every
    /// served meal the shop runtime reports lands here keyed by (day, person
    /// id); <see cref="DailyNeedsService"/> consults it so restaurant meals
    /// genuinely satisfy NPC nutrition (Canon §2.5) instead of existing in a
    /// parallel food economy.
    ///
    /// The ledger is additive-only: it records served-meal facts; it never
    /// estimates demand and never invents diners. Old days prune explicitly
    /// (bounded memory) and the ledger round-trips through save/load so a
    /// mid-day save does not lose served meals.
    ///
    /// D1E: the ledger also implements <see cref="IRestaurantMealQualitySource"/> —
    /// each served meal carries its quality weight (Canon §8.1B, §13.X), so a
    /// consumer that opts in can weight the count link by quality. The count
    /// link above stays authoritative and unchanged.
    /// </summary>
    public sealed class RestaurantMealDayLedger : IRestaurantMealDaySource, IRestaurantMealQualitySource
    {
        // dayIndex → personId → meals served that day.
        private readonly Dictionary<int, Dictionary<int, int>> servedByDay =
            new Dictionary<int, Dictionary<int, int>>();

        // D1E: dayIndex → personId → summed quality weights of meals served that day.
        private readonly Dictionary<int, Dictionary<int, float>> qualitySumByDay =
            new Dictionary<int, Dictionary<int, float>>();

        private readonly List<string> diagnostics = new List<string>();
        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>Records one served meal for a real person on a real day. Person ids ≤ 0 are refused loudly.</summary>
        public string ReportServedMeal(int personId, int dayIndex, List<string> diag)
        {
            return ReportServedMeal(personId, dayIndex, 1.0f, diag);
        }

        /// <summary>
        /// D1E: records one served meal with its quality weight (see
        /// <see cref="RestaurantMealQualityPolicy.QualityWeight01"/>). Person
        /// ids ≤ 0 are refused loudly.
        /// </summary>
        public string ReportServedMeal(int personId, int dayIndex, float qualityWeight01, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (personId <= 0)
                return "RestaurantMealDayLedger: refused — a served meal needs a real person id; anonymous diners are not recorded.";
            if (dayIndex < 0)
                return "RestaurantMealDayLedger: refused — a served meal needs a real day index.";

            if (!servedByDay.TryGetValue(dayIndex, out Dictionary<int, int> byPerson))
            {
                byPerson = new Dictionary<int, int>();
                servedByDay[dayIndex] = byPerson;
            }

            byPerson.TryGetValue(personId, out int soFar);
            byPerson[personId] = soFar + 1;

            if (!qualitySumByDay.TryGetValue(dayIndex, out Dictionary<int, float> qualityByPerson))
            {
                qualityByPerson = new Dictionary<int, float>();
                qualitySumByDay[dayIndex] = qualityByPerson;
            }

            qualityByPerson.TryGetValue(personId, out float qualitySoFar);
            qualityByPerson[personId] = qualitySoFar + Math.Max(0f, qualityWeight01);
            return null;
        }

        /// <summary>IRestaurantMealDaySource: meals the person actually ate at a restaurant on the given day.</summary>
        public int MealsEatenAtRestaurant(int personId, int dayIndex)
        {
            if (personId <= 0 || dayIndex < 0) return 0;
            if (!servedByDay.TryGetValue(dayIndex, out Dictionary<int, int> byPerson)) return 0;
            return byPerson.TryGetValue(personId, out int count) ? Math.Max(0, count) : 0;
        }

        /// <summary>
        /// D1E IRestaurantMealQualitySource: the average quality weight of
        /// the meals the person ate at restaurants on the given day (1.0 =
        /// house standard). 1.0 when the person ate nothing recorded — the
        /// count link, not this, decides whether they ate.
        /// </summary>
        public float AverageMealQuality01(int personId, int dayIndex)
        {
            if (personId <= 0 || dayIndex < 0) return 1.0f;
            if (!servedByDay.TryGetValue(dayIndex, out Dictionary<int, int> byPerson)) return 1.0f;
            if (!byPerson.TryGetValue(personId, out int count) || count <= 0) return 1.0f;
            if (!qualitySumByDay.TryGetValue(dayIndex, out Dictionary<int, float> qualityByPerson)) return 1.0f;
            if (!qualityByPerson.TryGetValue(personId, out float sum)) return 1.0f;
            return sum / count;
        }

        /// <summary>Total served meals recorded for the day, across all diners.</summary>
        public int ServedMealsOnDay(int dayIndex)
        {
            if (!servedByDay.TryGetValue(dayIndex, out Dictionary<int, int> byPerson)) return 0;
            int total = 0;
            foreach (int count in byPerson.Values) total += Math.Max(0, count);
            return total;
        }

        /// <summary>Drops days strictly before the given day (bounded memory; yesterday's meals never feed tomorrow's nutrition).</summary>
        public void PruneBefore(int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var stale = new List<int>();
            foreach (int day in servedByDay.Keys)
            {
                if (day < dayIndex) stale.Add(day);
            }

            foreach (int day in stale) servedByDay.Remove(day);
            foreach (int day in stale) qualitySumByDay.Remove(day);
            if (stale.Count > 0)
            {
                diag.Add($"RestaurantMealDayLedger: pruned {stale.Count} day(s) before day {dayIndex} — served meals are day-scoped facts.");
            }
        }

        #region Save / Load
        [Serializable]
        public sealed class RestaurantMealDayLedgerSaveDto
        {
            public List<RestaurantMealDayEntry> Entries = new List<RestaurantMealDayEntry>();
        }

        [Serializable]
        public sealed class RestaurantMealDayEntry
        {
            public int DayIndex;
            public int PersonId;
            public int MealsServed;

            /// <summary>D1E: summed quality weights of the served meals (parallel to MealsServed).</summary>
            public float QualitySum;
        }

        public RestaurantMealDayLedgerSaveDto CaptureSaveDto()
        {
            var dto = new RestaurantMealDayLedgerSaveDto();
            foreach (var dayKvp in servedByDay)
            {
                foreach (var personKvp in dayKvp.Value)
                {
                    float qualitySum = personKvp.Value; // default: every meal weighed 1.0 (pre-D1E entries)
                    if (qualitySumByDay.TryGetValue(dayKvp.Key, out Dictionary<int, float> qualityByPerson)
                        && qualityByPerson.TryGetValue(personKvp.Key, out float recorded))
                    {
                        qualitySum = recorded;
                    }

                    dto.Entries.Add(new RestaurantMealDayEntry
                    {
                        DayIndex = dayKvp.Key,
                        PersonId = personKvp.Key,
                        MealsServed = personKvp.Value,
                        QualitySum = qualitySum,
                    });
                }
            }

            return dto;
        }

        public void LoadFromSaveDto(RestaurantMealDayLedgerSaveDto dto)
        {
            servedByDay.Clear();
            qualitySumByDay.Clear();
            if (dto?.Entries == null) return;
            foreach (var entry in dto.Entries)
            {
                if (entry == null || entry.PersonId <= 0 || entry.DayIndex < 0 || entry.MealsServed <= 0) continue;
                if (!servedByDay.TryGetValue(entry.DayIndex, out Dictionary<int, int> byPerson))
                {
                    byPerson = new Dictionary<int, int>();
                    servedByDay[entry.DayIndex] = byPerson;
                }

                byPerson[entry.PersonId] = entry.MealsServed;

                if (!qualitySumByDay.TryGetValue(entry.DayIndex, out Dictionary<int, float> qualityByPerson))
                {
                    qualityByPerson = new Dictionary<int, float>();
                    qualitySumByDay[entry.DayIndex] = qualityByPerson;
                }

                qualityByPerson[entry.PersonId] = entry.QualitySum > 0f ? entry.QualitySum : entry.MealsServed;
            }
        }
        #endregion
    }

    /// <summary>
    /// D1E: the quality-aware nutrition-link seam. Reports the average quality
    /// weight of the meals a person actually ate at restaurants on a day
    /// (1.0 = house standard). The count contract
    /// (<see cref="IRestaurantMealDaySource"/>) stays authoritative for
    /// WHETHER the person ate; this only weights it for consumers that opt
    /// in. Consumer wiring into DailyNeedsService/PersonNutritionState is a
    /// later pass — this interface is the seam.
    /// </summary>
    public interface IRestaurantMealQualitySource
    {
        /// <summary>
        /// Average quality weight of the person's restaurant meals on the
        /// given day. 1.0 when nothing is recorded.
        /// </summary>
        float AverageMealQuality01(int personId, int dayIndex);
    }

    /// <summary>
    /// W2B: sums served meals across any number of restaurant ledgers (a town
    /// may hold several eating houses). Each source reports its own facts;
    /// the composite never double-counts within one source and simply adds
    /// across sources.
    /// D1E: also composites the quality seam — the count-weighted average
    /// quality across sources.
    /// </summary>
    public sealed class CompositeRestaurantMealSource : IRestaurantMealDaySource, IRestaurantMealQualitySource
    {
        private readonly List<IRestaurantMealDaySource> sources = new List<IRestaurantMealDaySource>();

        public CompositeRestaurantMealSource() { }

        public CompositeRestaurantMealSource(IEnumerable<IRestaurantMealDaySource> sources)
        {
            if (sources == null) return;
            foreach (var source in sources)
            {
                if (source != null) this.sources.Add(source);
            }
        }

        public void Add(IRestaurantMealDaySource source)
        {
            if (source != null) sources.Add(source);
        }

        public int MealsEatenAtRestaurant(int personId, int dayIndex)
        {
            int total = 0;
            foreach (var source in sources)
            {
                if (source == null) continue;
                total += Math.Max(0, source.MealsEatenAtRestaurant(personId, dayIndex));
            }

            return total;
        }

        /// <summary>
        /// D1E: the count-weighted average meal quality across sources (a
        /// house the diner ate twice at weighs twice). 1.0 when nothing is
        /// recorded.
        /// </summary>
        public float AverageMealQuality01(int personId, int dayIndex)
        {
            double weightedSum = 0.0;
            int totalMeals = 0;
            foreach (var source in sources)
            {
                if (source == null) continue;
                int meals = Math.Max(0, source.MealsEatenAtRestaurant(personId, dayIndex));
                if (meals <= 0) continue;
                var qualitySource = source as IRestaurantMealQualitySource;
                float quality = qualitySource != null ? qualitySource.AverageMealQuality01(personId, dayIndex) : 1.0f;
                weightedSum += quality * meals;
                totalMeals += meals;
            }

            return totalMeals > 0 ? (float)(weightedSum / totalMeals) : 1.0f;
        }
    }
}
