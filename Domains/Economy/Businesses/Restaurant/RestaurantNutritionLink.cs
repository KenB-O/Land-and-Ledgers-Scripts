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
    /// </summary>
    public sealed class RestaurantMealDayLedger : IRestaurantMealDaySource
    {
        // dayIndex → personId → meals served that day.
        private readonly Dictionary<int, Dictionary<int, int>> servedByDay =
            new Dictionary<int, Dictionary<int, int>>();

        private readonly List<string> diagnostics = new List<string>();
        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>Records one served meal for a real person on a real day. Person ids ≤ 0 are refused loudly.</summary>
        public string ReportServedMeal(int personId, int dayIndex, List<string> diag)
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
            return null;
        }

        /// <summary>IRestaurantMealDaySource: meals the person actually ate at a restaurant on the given day.</summary>
        public int MealsEatenAtRestaurant(int personId, int dayIndex)
        {
            if (personId <= 0 || dayIndex < 0) return 0;
            if (!servedByDay.TryGetValue(dayIndex, out Dictionary<int, int> byPerson)) return 0;
            return byPerson.TryGetValue(personId, out int count) ? Math.Max(0, count) : 0;
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
        }

        public RestaurantMealDayLedgerSaveDto CaptureSaveDto()
        {
            var dto = new RestaurantMealDayLedgerSaveDto();
            foreach (var dayKvp in servedByDay)
            {
                foreach (var personKvp in dayKvp.Value)
                {
                    dto.Entries.Add(new RestaurantMealDayEntry
                    {
                        DayIndex = dayKvp.Key,
                        PersonId = personKvp.Key,
                        MealsServed = personKvp.Value,
                    });
                }
            }

            return dto;
        }

        public void LoadFromSaveDto(RestaurantMealDayLedgerSaveDto dto)
        {
            servedByDay.Clear();
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
            }
        }
        #endregion
    }

    /// <summary>
    /// W2B: sums served meals across any number of restaurant ledgers (a town
    /// may hold several eating houses). Each source reports its own facts;
    /// the composite never double-counts within one source and simply adds
    /// across sources.
    /// </summary>
    public sealed class CompositeRestaurantMealSource : IRestaurantMealDaySource
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
    }
}
