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

        public string ProfileId => profileId ?? string.Empty;
        public IReadOnlyList<PersonDefinition> People => people ?? Array.Empty<PersonDefinition>();
        public IReadOnlyList<HouseholdDefinition> Households => households ?? Array.Empty<HouseholdDefinition>();

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

            return true;
        }
    }
}
