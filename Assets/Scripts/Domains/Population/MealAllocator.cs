using System.Collections.Generic;

namespace LandLedgers.Population
{
    /// <summary>
    /// NX-1B: household-internal scarcity allocation — when meals are short,
    /// who eats first. Priority: children (youngest first — growth
    /// vulnerability), then working adults (calories protect the household's
    /// labor income), then other adults. Within a tier, round-robin by person
    /// id for determinism.
    ///
    /// Historical basis (calibration, not canon): 19th-century studies document
    /// unequal intra-household allocation with breadwinners commonly privileged
    /// ("men and boys were commonly privileged, consuming more and better
    /// foodstuffs" — PMC7687131; Humphries et al. on bare-bones living and
    /// labor capacity). The game orders by vulnerability + labor-need rather
    /// than encoding the historical gender bias as optimal play.
    /// </summary>
    public static class MealAllocator
    {
        /// <summary>
        /// Allocates up to mealsAvailable meals across members, each capped at
        /// mealsPerPerson. Returns person id → meals allocated.
        /// </summary>
        public static Dictionary<int, int> Allocate(
            List<PersonState> members, int mealsAvailable, int mealsPerPerson)
        {
            var result = new Dictionary<int, int>();
            if (members == null || mealsAvailable <= 0 || mealsPerPerson <= 0)
            {
                if (members != null)
                    foreach (var m in members)
                        if (m != null && !result.ContainsKey(m.id)) result[m.id] = 0;
                return result;
            }

            var ordered = new List<PersonState>(members);
            ordered.Sort((a, b) =>
            {
                int pa = PriorityTier(a);
                int pb = PriorityTier(b);
                if (pa != pb) return pa.CompareTo(pb);
                if (pa == 0) return a.age.CompareTo(b.age); // youngest children first
                return a.id.CompareTo(b.id); // deterministic
            });

            int remaining = mealsAvailable;
            foreach (var m in ordered)
            {
                if (m == null) continue;
                int give = System.Math.Min(mealsPerPerson, remaining);
                result[m.id] = give;
                remaining -= give;
                if (remaining <= 0) break;
            }
            // Anyone not reached gets zero.
            foreach (var m in ordered)
                if (m != null && !result.ContainsKey(m.id)) result[m.id] = 0;
            return result;
        }

        /// <summary>
        /// W2B: allocates up to mealsAvailable meals across members with
        /// PER-PERSON caps — a person who already ate elsewhere (e.g. at a
        /// restaurant, Canon §2.5) draws only their remaining need from the
        /// household. Caps keyed by person id; a missing entry means
        /// mealsPerPerson. Same priority order as the uniform-cap overload.
        /// </summary>
        public static Dictionary<int, int> Allocate(
            List<PersonState> members, int mealsAvailable,
            IReadOnlyDictionary<int, int> perPersonCaps, int mealsPerPerson)
        {
            var result = new Dictionary<int, int>();
            if (members == null || mealsAvailable <= 0 || mealsPerPerson <= 0)
            {
                if (members != null)
                    foreach (var m in members)
                        if (m != null && !result.ContainsKey(m.id)) result[m.id] = 0;
                return result;
            }

            var ordered = new List<PersonState>(members);
            ordered.Sort((a, b) =>
            {
                int pa = PriorityTier(a);
                int pb = PriorityTier(b);
                if (pa != pb) return pa.CompareTo(pb);
                if (pa == 0) return a.age.CompareTo(b.age); // youngest children first
                return a.id.CompareTo(b.id); // deterministic
            });

            int remaining = mealsAvailable;
            foreach (var m in ordered)
            {
                if (m == null) continue;
                int cap = mealsPerPerson;
                if (perPersonCaps != null && perPersonCaps.TryGetValue(m.id, out int perCap))
                    cap = System.Math.Max(0, System.Math.Min(mealsPerPerson, perCap));
                int give = System.Math.Min(cap, remaining);
                result[m.id] = give;
                remaining -= give;
                if (remaining <= 0) break;
            }
            // Anyone not reached gets zero.
            foreach (var m in ordered)
                if (m != null && !result.ContainsKey(m.id)) result[m.id] = 0;
            return result;
        }

        private static int PriorityTier(PersonState p)
        {
            if (p == null) return 3;
            if (p.ageBand != AgeBand.Adult18Plus) return 0; // children first
            if (p.laborAccessLevel != LaborAccessLevel.None) return 1; // workers second
            return 2; // other adults last
        }
    }
}
