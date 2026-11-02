using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Population
{
    /// <summary>
    /// Explicit, development-only population seed for the Pass XL six-person
    /// vertical slice. It is opt-in through a bootstrapper reference and is
    /// never selected by ordinary population generation.
    /// </summary>
    [CreateAssetMenu(
        fileName = "PassXLSixPersonDevelopmentFixture",
        menuName = "Land & Ledgers/Development/Six Person Population Fixture",
        order = 990)]
    public sealed class SixPersonDevelopmentFixture : ScriptableObject
    {
        [Serializable]
        public sealed class OpeningRetailStockRange
        {
            public string itemId = string.Empty;
            [Min(0)] public int minimumUnits;
            [Min(0)] public int maximumUnits;
        }

        [Serializable]
        public sealed class PersonDefinition
        {
            public string firstName;
            public string lastName;
            [Min(18)] public int age = 30;
            public bool isFemale;
        }

        [Serializable]
        public sealed class HouseholdDefinition
        {
            public string surname;
            [Tooltip("Indexes into the fixture's Persons list. Each person must belong to exactly one household.")]
            public int[] personIndexes;
            [Tooltip("Ordinal among distinct, real residential buildings sorted by TownWorld building ID.")]
            public int homeBuildingOrdinal;
        }

        [Serializable]
        public sealed class SpousePairDefinition
        {
            [Tooltip("Indexes into the fixture's Persons list. Spouse relations are explicit and are not inferred from shared household membership.")]
            public int firstPersonIndex;
            public int secondPersonIndex;
        }

        [SerializeField] private string profileId = "pass-xl-six-person-development";
        [SerializeField] private PersonDefinition[] people =
        {
            new PersonDefinition { firstName = "Kennedy", lastName = "Baldwin-Ooms", age = 35, isFemale = false },
            new PersonDefinition { firstName = "Emma", lastName = "Baldwin-Ooms", age = 32, isFemale = true },
            new PersonDefinition { firstName = "Glen", lastName = "Pierce", age = 31, isFemale = false },
            new PersonDefinition { firstName = "Cassidy", lastName = "Pierce", age = 29, isFemale = true },
            new PersonDefinition { firstName = "Jacob", lastName = "Boniecki", age = 34, isFemale = false },
            new PersonDefinition { firstName = "Grace", lastName = "Boniecki", age = 30, isFemale = true },
        };
        [SerializeField] private HouseholdDefinition[] households =
        {
            new HouseholdDefinition { surname = "Baldwin-Ooms", personIndexes = new[] { 0, 1 }, homeBuildingOrdinal = 0 },
            new HouseholdDefinition { surname = "Pierce", personIndexes = new[] { 2, 3 }, homeBuildingOrdinal = 1 },
            new HouseholdDefinition { surname = "Boniecki", personIndexes = new[] { 4, 5 }, homeBuildingOrdinal = 2 },
        };
        [SerializeField, Tooltip("Explicit kinship records for this fixture. Household membership alone does not imply marriage.")]
        private SpousePairDefinition[] spousePairs =
        {
            new SpousePairDefinition { firstPersonIndex = 0, secondPersonIndex = 1 },
            new SpousePairDefinition { firstPersonIndex = 2, secondPersonIndex = 3 },
            new SpousePairDefinition { firstPersonIndex = 4, secondPersonIndex = 5 },
        };
        [SerializeField, Tooltip("Finite, scenario-seeded opening endowment for the NPC General Store in this development fixture only. These ranges are test calibration, not Canon.")]
        private OpeningRetailStockRange[] openingStoreStock =
        {
            new OpeningRetailStockRange { itemId = "bread", minimumUnits = 12, maximumUnits = 24 },
            new OpeningRetailStockRange { itemId = "flour", minimumUnits = 4, maximumUnits = 8 },
            new OpeningRetailStockRange { itemId = "potatoes", minimumUnits = 4, maximumUnits = 8 },
            new OpeningRetailStockRange { itemId = "sugar", minimumUnits = 5, maximumUnits = 10 },
            new OpeningRetailStockRange { itemId = "coffee", minimumUnits = 3, maximumUnits = 6 },
            new OpeningRetailStockRange { itemId = "tea", minimumUnits = 2, maximumUnits = 4 },
            new OpeningRetailStockRange { itemId = "salt", minimumUnits = 4, maximumUnits = 8 },
        };

        public string ProfileId => profileId ?? string.Empty;
        public IReadOnlyList<PersonDefinition> People => people ?? Array.Empty<PersonDefinition>();
        public IReadOnlyList<HouseholdDefinition> Households => households ?? Array.Empty<HouseholdDefinition>();
        public IReadOnlyList<SpousePairDefinition> SpousePairs => spousePairs ?? Array.Empty<SpousePairDefinition>();
        public IReadOnlyList<OpeningRetailStockRange> OpeningStoreStock => openingStoreStock ?? Array.Empty<OpeningRetailStockRange>();

        public bool TryResolveOpeningStoreStock(string itemId, int scenarioSeed, out int quantity)
        {
            quantity = 0;
            if (string.IsNullOrWhiteSpace(itemId) || openingStoreStock == null) return false;
            for (int i = 0; i < openingStoreStock.Length; i++)
            {
                OpeningRetailStockRange range = openingStoreStock[i];
                if (range == null || !string.Equals(range.itemId, itemId, StringComparison.OrdinalIgnoreCase)) continue;
                int min = Mathf.Max(0, range.minimumUnits);
                int max = Mathf.Max(min, range.maximumUnits);
                uint hash = 2166136261u ^ unchecked((uint)scenarioSeed);
                string key = ProfileId + ":" + itemId.Trim().ToLowerInvariant();
                for (int c = 0; c < key.Length; c++)
                {
                    hash ^= key[c];
                    hash *= 16777619u;
                }
                hash ^= hash >> 16;
                hash *= 0x7feb352du;
                hash ^= hash >> 15;
                quantity = min + (int)(hash % (uint)(max - min + 1));
                return true;
            }
            return false;
        }

        public bool Validate(out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(profileId))
            {
                error = "The development population fixture needs a profile ID.";
                return false;
            }

            if (people == null || people.Length != 6 || households == null || households.Length != 3)
            {
                error = "The Pass XL fixture requires exactly six Persons and three Households.";
                return false;
            }

            if (spousePairs == null || spousePairs.Length != 3)
            {
                error = "The Pass XL fixture requires three explicit spouse pairs; household membership alone does not establish marriage.";
                return false;
            }

            var assigned = new bool[people.Length];
            var homeOrdinals = new HashSet<int>();
            foreach (HouseholdDefinition household in households)
            {
                if (household == null || string.IsNullOrWhiteSpace(household.surname)
                    || household.personIndexes == null || household.personIndexes.Length != 2
                    || household.homeBuildingOrdinal < 0 || !homeOrdinals.Add(household.homeBuildingOrdinal))
                {
                    error = "Each fixture Household must have a surname, exactly two members, and a unique non-negative home ordinal.";
                    return false;
                }

                foreach (int personIndex in household.personIndexes)
                {
                    if (personIndex < 0 || personIndex >= people.Length || assigned[personIndex])
                    {
                        error = "Fixture Person indexes must be valid and belong to exactly one Household.";
                        return false;
                    }

                    PersonDefinition person = people[personIndex];
                    if (person == null || string.IsNullOrWhiteSpace(person.firstName)
                        || !string.Equals(person.lastName, household.surname, StringComparison.Ordinal)
                        || person.age < 18)
                    {
                        error = "Fixture Persons must be named adults whose surname matches their Household.";
                        return false;
                    }

                    assigned[personIndex] = true;
                }
            }

            for (int i = 0; i < assigned.Length; i++)
            {
                if (!assigned[i])
                {
                    error = $"Fixture Person at index {i} has no Household.";
                    return false;
                }
            }

            var spousePersons = new HashSet<int>();
            foreach (SpousePairDefinition pair in SpousePairs)
            {
                if (pair == null || pair.firstPersonIndex < 0 || pair.firstPersonIndex >= people.Length
                    || pair.secondPersonIndex < 0 || pair.secondPersonIndex >= people.Length
                    || pair.firstPersonIndex == pair.secondPersonIndex
                    || !spousePersons.Add(pair.firstPersonIndex) || !spousePersons.Add(pair.secondPersonIndex))
                {
                    error = "Fixture spouse pairs must use distinct, valid Persons with at most one configured spouse per Person.";
                    return false;
                }

                bool shareHousehold = false;
                foreach (HouseholdDefinition household in households)
                {
                    bool containsFirst = Array.IndexOf(household.personIndexes, pair.firstPersonIndex) >= 0;
                    bool containsSecond = Array.IndexOf(household.personIndexes, pair.secondPersonIndex) >= 0;
                    if (containsFirst && containsSecond)
                    {
                        shareHousehold = true;
                        break;
                    }
                }

                if (!shareHousehold)
                {
                    error = "Fixture spouse pairs must be explicitly assigned to the same Household.";
                    return false;
                }
            }

            var stockIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (OpeningRetailStockRange range in OpeningStoreStock)
            {
                if (range == null || string.IsNullOrWhiteSpace(range.itemId)
                    || !stockIds.Add(range.itemId.Trim()) || range.minimumUnits < 0
                    || range.maximumUnits < range.minimumUnits)
                {
                    error = "Opening-store stock ranges must have unique SKU IDs and valid non-negative bounds.";
                    return false;
                }
            }

            return true;
        }
    }
}
