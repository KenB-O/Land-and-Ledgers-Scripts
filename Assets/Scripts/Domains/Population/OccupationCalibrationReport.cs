using System;
using System.Collections.Generic;

namespace LandLedgers.Population
{
    /// <summary>Diagnostic projection over real Persons; never an occupation authority.</summary>
    public sealed class OccupationCalibrationReport
    {
        public int PersonCount { get; }
        public int EconomicallyActivePersonCount { get; }
        public int ResolvedOccupationCount { get; }
        public int UnresolvedOccupationCount { get; }
        public IReadOnlyDictionary<string, int> PeopleByOccupation { get; }
        public IReadOnlyList<string> TierOneProfilesWithoutPeople { get; }

        private OccupationCalibrationReport(int people, int active, int resolved, int unresolved,
            IReadOnlyDictionary<string, int> counts, IReadOnlyList<string> missing)
        {
            PersonCount = people;
            EconomicallyActivePersonCount = active;
            ResolvedOccupationCount = resolved;
            UnresolvedOccupationCount = unresolved;
            PeopleByOccupation = counts;
            TierOneProfilesWithoutPeople = missing;
        }

        public static OccupationCalibrationReport Build(PopulationState population)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int people = 0, active = 0, resolved = 0, unresolved = 0;
            foreach (PersonState person in population?.people ?? new List<PersonState>())
            {
                if (person == null || person.deathDayIndex >= 0) continue;
                people++;
                if (person.laborAccessLevel != LaborAccessLevel.None) active++;
                OccupationProfile profile = OccupationProfileCatalog.Resolve(person);
                if (profile == null)
                {
                    unresolved++;
                    continue;
                }
                resolved++;
                counts[profile.Id] = counts.TryGetValue(profile.Id, out int current) ? current + 1 : 1;
            }

            var missing = new List<string>();
            for (int i = 0; i < OccupationProfileCatalog.TierOne.Count; i++)
            {
                OccupationProfile profile = OccupationProfileCatalog.TierOne[i];
                if (!counts.ContainsKey(profile.Id)) missing.Add(profile.Id);
            }
            return new OccupationCalibrationReport(people, active, resolved, unresolved, counts, missing);
        }
    }
}
