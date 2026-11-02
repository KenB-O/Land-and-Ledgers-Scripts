using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Population
{
    /// <summary>
    /// Authorable, opt-in runtime profile for the expanded development slice.
    /// The six-person fixture remains the base and ordinary scenarios do not
    /// use this profile unless the bootstrap explicitly enables it.
    /// </summary>
    [CreateAssetMenu(
        fileName = "ExpandedSliceFixture",
        menuName = "Land & Ledgers/Development/Expanded Slice Fixture",
        order = 991)]
    public sealed class ExpandedSliceFixture : ScriptableObject
    {
        public enum Operation
        {
            GeneralStore,
            Bakery,
            FuelDealer,
            FreightCompany
        }

        [Serializable]
        public sealed class PersonSlot
        {
            public string key = string.Empty;
            [Tooltip("Only the name information supplied so far. For example, 'Ethan B.' is not a resolved legal full name.")]
            public string knownDisplayName = string.Empty;
            public string householdKey = string.Empty;

            [Header("Unresolved or supplied personal data")]
            public bool fullLegalNameResolved;
            public string fullLegalName = string.Empty;
            public bool ageResolved;
            [Min(-1)] public int age = -1;
            [Tooltip("True when age is provisional development calibration, not biographical Canon.")]
            public bool ageIsProvisionalCalibration;
            public bool isFemale;
            public bool homeResolved;
            public string homeBuildingId = string.Empty;
            public bool occupationResolved;
            public string occupation = string.Empty;
            public bool startingCashResolved;
            [Min(-1)] public int startingCashCents = -1;
            public bool relationshipsResolved;
            [Tooltip("Do not infer a relationship from shared household membership.")]
            public string relationships = string.Empty;
            [Tooltip("Ordinal into real residential buildings sorted by TownWorld building ID.")]
            public int homeBuildingOrdinal = -1;
        }

        [Serializable]
        public sealed class HouseholdSlot
        {
            public string key = string.Empty;
            public string[] memberPersonKeys = Array.Empty<string>();
            public bool homeResolved;
            public string homeBuildingId = string.Empty;
            public bool relationshipBetweenMembersResolved;
            [Tooltip("For example, Ethan and Tia are assigned to one household, but their relationship is unspecified.")]
            public string relationshipBetweenMembers = string.Empty;
            [Tooltip("Ordinal into real residential buildings sorted by TownWorld building ID.")]
            public int homeBuildingOrdinal = -1;
            [Min(0), Tooltip("One-time scenario endowment in cents; not a general household money rule.")]
            public int openingCashCents;
        }

        [Serializable]
        public sealed class BusinessSlot
        {
            public string key = string.Empty;
            public Operation operation;
            [Tooltip("Use base:0 through base:5 for the existing six-person fixture, or an added Person slot key.")]
            public string ownerPersonKey = string.Empty;
            public bool ownerResolved;
            public bool businessIdResolved;
            public string businessId = string.Empty;
            public bool premisesResolved;
            public string premisesId = string.Empty;
            public bool openingCashResolved;
            [Min(-1)] public int openingCashCents = -1;
            [Tooltip("Freight entity may exist before it has premises.")]
            public bool premisesRequired = true;
        }

        public enum Relationship { Sibling }

        [Serializable]
        public sealed class PersonRelationshipSlot
        {
            public string firstPersonKey = string.Empty;
            public string secondPersonKey = string.Empty;
            public Relationship relationship;
        }

        [Serializable]
        public sealed class BaseHouseholdEndowment
        {
            [Tooltip("Index into the referenced six-person fixture household array.")]
            public int householdIndex;
            [Min(0)] public int openingCashCents;
        }

        [SerializeField] private string profileId = "expanded-nine-person-development";
        [SerializeField, Tooltip("Development-only deterministic generation seed offset; scenario calibration, not Canon.")]
        private int seedOffset = 9217;
        [SerializeField, Tooltip("Expanded-profile replacement for base fixture Person index 3. The six-person fixture asset itself is not modified.")]
        private string expandedPierceSpouseName = "Tannis";
        [SerializeField, Min(18), Tooltip("Provisional age calibration for Tannis in this development profile; does not alter the six-person fixture.")]
        private int expandedPierceSpouseAge = 29;
        [SerializeField] private SixPersonDevelopmentFixture sixPersonFixture;
        [SerializeField] private PersonSlot[] additionalPeople =
        {
            new PersonSlot
            {
                key = "ethan", knownDisplayName = "Ethan Boniecki", householdKey = "ethan-tia",
                fullLegalNameResolved = true, fullLegalName = "Ethan Boniecki",
                ageResolved = true, age = 34, ageIsProvisionalCalibration = true,
                isFemale = false, homeBuildingOrdinal = 3,
                occupationResolved = true, occupation = "Freight Company owner/operator",
                startingCashResolved = true, startingCashCents = 0,
                relationshipsResolved = true, relationships = "Sibling of base:4; spouse of tia"
            },
            new PersonSlot
            {
                key = "tia", knownDisplayName = "Tia Boniecki", householdKey = "ethan-tia",
                fullLegalNameResolved = true, fullLegalName = "Tia Boniecki",
                ageResolved = true, age = 30, ageIsProvisionalCalibration = true,
                isFemale = true, homeBuildingOrdinal = 3,
                occupationResolved = true, occupation = "Unspecified",
                startingCashResolved = true, startingCashCents = 0,
                relationshipsResolved = true, relationships = "Spouse of ethan"
            },
            new PersonSlot
            {
                key = "shane", knownDisplayName = "Shane Bryan", householdKey = "shane",
                fullLegalNameResolved = true, fullLegalName = "Shane Bryan",
                ageResolved = true, age = 27, ageIsProvisionalCalibration = true,
                isFemale = false, homeBuildingOrdinal = 4,
                occupationResolved = true, occupation = "Unemployed; seeking work",
                startingCashResolved = true, startingCashCents = 0,
                relationshipsResolved = true, relationships = "No additional relationships authored"
            }
        };
        [SerializeField] private HouseholdSlot[] additionalHouseholds =
        {
            new HouseholdSlot
            {
                key = "ethan-tia", memberPersonKeys = new[] { "ethan", "tia" },
                homeResolved = true, homeBuildingOrdinal = 3,
                relationshipBetweenMembersResolved = true, relationshipBetweenMembers = "Spouses",
                openingCashCents = 6000
            },
            new HouseholdSlot
            {
                key = "shane", memberPersonKeys = new[] { "shane" },
                homeResolved = true, homeBuildingOrdinal = 4,
                openingCashCents = 12500
            }
        };
        [SerializeField] private BusinessSlot[] businesses =
        {
            new BusinessSlot
            {
                key = "general-store", operation = Operation.GeneralStore,
                ownerPersonKey = "base:0", ownerResolved = true,
                openingCashResolved = true, openingCashCents = 25000
            },
            new BusinessSlot { key = "bakery", operation = Operation.Bakery,
                ownerPersonKey = "base:2", ownerResolved = true, openingCashResolved = true, openingCashCents = 17500 },
            new BusinessSlot { key = "fuel-dealer", operation = Operation.FuelDealer,
                ownerPersonKey = "base:4", ownerResolved = true, openingCashResolved = true, openingCashCents = 20000 },
            new BusinessSlot
            {
                key = "freight-company", operation = Operation.FreightCompany,
                ownerPersonKey = "ethan", ownerResolved = true,
                openingCashResolved = true, openingCashCents = 10000, premisesRequired = false
            }
        };
        [SerializeField] private PersonRelationshipSlot[] additionalRelationships =
        {
            new PersonRelationshipSlot { firstPersonKey = "base:4", secondPersonKey = "ethan", relationship = Relationship.Sibling }
        };
        [SerializeField] private BaseHouseholdEndowment[] baseHouseholdEndowments =
        {
            new BaseHouseholdEndowment { householdIndex = 1, openingCashCents = 6500 },
            new BaseHouseholdEndowment { householdIndex = 2, openingCashCents = 7500 }
        };

        public const int RequiredPersonCount = 9;
        public const int RequiredHouseholdCount = 5;
        public const int RequiredBusinessCount = 4;

        public string ProfileId => profileId ?? string.Empty;
        public int SeedOffset => seedOffset;
        public string ExpandedPierceSpouseName => expandedPierceSpouseName ?? "Tannis";
        public int ExpandedPierceSpouseAge => Mathf.Max(18, expandedPierceSpouseAge);
        public SixPersonDevelopmentFixture SixPersonFixture => sixPersonFixture;
        public IReadOnlyList<PersonSlot> AdditionalPeople => additionalPeople ?? Array.Empty<PersonSlot>();
        public IReadOnlyList<HouseholdSlot> AdditionalHouseholds => additionalHouseholds ?? Array.Empty<HouseholdSlot>();
        public IReadOnlyList<BusinessSlot> Businesses => businesses ?? Array.Empty<BusinessSlot>();
        public IReadOnlyList<PersonRelationshipSlot> AdditionalRelationships => additionalRelationships ?? Array.Empty<PersonRelationshipSlot>();
        public IReadOnlyList<BaseHouseholdEndowment> BaseHouseholdEndowments => baseHouseholdEndowments ?? Array.Empty<BaseHouseholdEndowment>();

        public string FindPersonDisplayName(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return string.Empty;
            if (key.StartsWith("base:", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(key.Substring(5), out int index)
                && sixPersonFixture != null && index >= 0 && index < sixPersonFixture.People.Count)
            {
                SixPersonDevelopmentFixture.PersonDefinition person = sixPersonFixture.People[index];
                if (index == 3 && string.Equals(person.lastName, "Pierce", StringComparison.Ordinal))
                    return ExpandedPierceSpouseName + " " + person.lastName;
                return person.firstName + " " + person.lastName;
            }
            foreach (PersonSlot person in AdditionalPeople)
                if (person != null && string.Equals(person.key, key, StringComparison.OrdinalIgnoreCase))
                    return person.fullLegalNameResolved ? person.fullLegalName : person.knownDisplayName;
            return string.Empty;
        }

        /// <summary>
        /// Checks the configured nine/five/four shape without pretending that
        /// unresolved owner data is known. A structurally valid fixture may
        /// still be not ready for runtime integration.
        /// </summary>
        public ExpandedSliceFixtureValidation Validate()
        {
            var result = new ExpandedSliceFixtureValidation();
            if (string.IsNullOrWhiteSpace(profileId))
                result.Errors.Add("The expanded fixture needs a profile ID.");
            if (sixPersonFixture == null)
                result.Errors.Add("The existing SixPersonDevelopmentFixture reference is unresolved.");
            else if (!sixPersonFixture.Validate(out string baseError))
                result.Errors.Add("The referenced six-person fixture is invalid: " + baseError);

            if (sixPersonFixture != null && sixPersonFixture.People.Count + AdditionalPeople.Count != RequiredPersonCount)
                result.Errors.Add($"The expanded slice requires exactly {RequiredPersonCount} Persons including the six-person base fixture.");
            if (sixPersonFixture != null && sixPersonFixture.Households.Count + AdditionalHouseholds.Count != RequiredHouseholdCount)
                result.Errors.Add($"The expanded slice requires exactly {RequiredHouseholdCount} Households including the six-person base fixture.");
            if (Businesses.Count != RequiredBusinessCount)
                result.Errors.Add($"The expanded slice requires exactly {RequiredBusinessCount} Business slots.");

            ValidateRequiredPersonSlots(result);

            var peopleByKey = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < (sixPersonFixture != null ? sixPersonFixture.People.Count : 0); i++)
                peopleByKey.Add("base:" + i);

            foreach (PersonSlot person in AdditionalPeople)
            {
                if (person == null || string.IsNullOrWhiteSpace(person.key)
                    || string.IsNullOrWhiteSpace(person.knownDisplayName)
                    || !peopleByKey.Add(person.key))
                {
                    result.Errors.Add("Additional Person slots need unique keys and known display labels.");
                    continue;
                }

                if (!person.fullLegalNameResolved) result.Unresolved.Add(person.key + ".full legal name");
                else if (string.IsNullOrWhiteSpace(person.fullLegalName)) result.Errors.Add(person.key + " marks full legal name resolved but supplies no value.");
                if (!person.ageResolved) result.Unresolved.Add(person.key + ".age");
                else if (person.age < 0) result.Errors.Add(person.key + " marks age resolved but supplies no non-negative value.");
                if (person.homeBuildingOrdinal < 0) result.Errors.Add(person.key + " requires a non-negative configured home ordinal.");
                if (!person.occupationResolved) result.Unresolved.Add(person.key + ".occupation");
                else if (string.IsNullOrWhiteSpace(person.occupation)) result.Errors.Add(person.key + " marks occupation resolved but supplies no value.");
                if (!person.startingCashResolved) result.Unresolved.Add(person.key + ".starting cash");
                else if (person.startingCashCents < 0) result.Errors.Add(person.key + " marks starting cash resolved but supplies no non-negative amount.");
                if (!person.relationshipsResolved) result.Unresolved.Add(person.key + ".personal relationships");
                else if (string.IsNullOrWhiteSpace(person.relationships)) result.Errors.Add(person.key + " marks relationships resolved but supplies no description.");
            }

            ValidateHouseholds(result, peopleByKey);
            ValidateBusinesses(result, peopleByKey);
            return result;
        }

        private void ValidateRequiredPersonSlots(ExpandedSliceFixtureValidation result)
        {
            RequireKnownPerson(result, "ethan", "Ethan Boniecki");
            RequireKnownPerson(result, "tia", "Tia Boniecki");
            RequireKnownPerson(result, "shane", "Shane Bryan");

            HouseholdSlot ethanTia = FindHousehold("ethan-tia");
            HouseholdSlot shane = FindHousehold("shane");
            if (ethanTia == null || ethanTia.memberPersonKeys == null
                || ethanTia.memberPersonKeys.Length != 2
                || !ContainsMember(ethanTia, "ethan") || !ContainsMember(ethanTia, "tia"))
                result.Errors.Add("The expanded shape requires Ethan and Tia in their own two-member Household slot; their relationship remains separately unresolved.");
            if (shane == null || shane.memberPersonKeys == null
                || shane.memberPersonKeys.Length != 1 || !ContainsMember(shane, "shane"))
                result.Errors.Add("The expanded shape requires Shane in his own one-member Household slot.");
        }

        private void RequireKnownPerson(ExpandedSliceFixtureValidation result, string key, string knownDisplayName)
        {
            PersonSlot person = FindPerson(key);
            if (person == null || !string.Equals(person.knownDisplayName, knownDisplayName, StringComparison.Ordinal))
                result.Errors.Add($"The expanded shape requires the supplied Person slot '{key}' with known display label '{knownDisplayName}'.");
        }

        private HouseholdSlot FindHousehold(string key)
        {
            foreach (HouseholdSlot household in AdditionalHouseholds)
                if (household != null && string.Equals(household.key, key, StringComparison.OrdinalIgnoreCase)) return household;
            return null;
        }

        private static bool ContainsMember(HouseholdSlot household, string personKey)
        {
            foreach (string member in household.memberPersonKeys)
                if (string.Equals(member, personKey, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private void ValidateHouseholds(ExpandedSliceFixtureValidation result, HashSet<string> peopleByKey)
        {
            var householdKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var homeOrdinals = new HashSet<int>();
            if (sixPersonFixture != null)
                foreach (SixPersonDevelopmentFixture.HouseholdDefinition baseHousehold in sixPersonFixture.Households)
                    if (baseHousehold != null) homeOrdinals.Add(baseHousehold.homeBuildingOrdinal);
            foreach (HouseholdSlot household in AdditionalHouseholds)
            {
                if (household == null || string.IsNullOrWhiteSpace(household.key)
                    || !householdKeys.Add(household.key) || household.memberPersonKeys == null
                    || household.memberPersonKeys.Length == 0)
                {
                    result.Errors.Add("Additional Households need unique keys and at least one member slot.");
                    continue;
                }

                var memberSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string memberKey in household.memberPersonKeys)
                {
                    if (!peopleByKey.Contains(memberKey) || !memberSet.Add(memberKey))
                    {
                        result.Errors.Add($"Household '{household.key}' contains an unknown or repeated Person slot '{memberKey}'.");
                        continue;
                    }

                    PersonSlot person = FindPerson(memberKey);
                    if (person != null && !string.Equals(person.householdKey, household.key, StringComparison.OrdinalIgnoreCase))
                        result.Errors.Add($"Person slot '{memberKey}' and Household '{household.key}' do not agree on membership.");
                    if (person != null && person.homeBuildingOrdinal != household.homeBuildingOrdinal)
                        result.Errors.Add($"Person slot '{memberKey}' and Household '{household.key}' do not agree on their home ordinal.");
                }

                if (!household.homeResolved || household.homeBuildingOrdinal < 0) result.Errors.Add(household.key + " requires a configured home ordinal.");
                else if (!homeOrdinals.Add(household.homeBuildingOrdinal)) result.Errors.Add("Each expanded fixture Household must have its own configured home ordinal.");
                if (household.openingCashCents < 0) result.Errors.Add("Household opening cash cannot be negative.");

                if (household.memberPersonKeys.Length > 1 && !household.relationshipBetweenMembersResolved)
                    result.Unresolved.Add(household.key + ".relationship between household members");
            }

            foreach (PersonSlot person in AdditionalPeople)
            {
                if (person != null && !householdKeys.Contains(person.householdKey))
                    result.Errors.Add($"Additional Person slot '{person.key}' has no matching additional Household slot.");
            }
        }

        private void ValidateBusinesses(ExpandedSliceFixtureValidation result, HashSet<string> peopleByKey)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var operations = new HashSet<Operation>();
            foreach (BusinessSlot business in Businesses)
            {
                if (business == null || string.IsNullOrWhiteSpace(business.key)
                    || !keys.Add(business.key) || !operations.Add(business.operation))
                {
                    result.Errors.Add("Business slots need unique keys and one slot for each distinct operation.");
                    continue;
                }

                if (!business.ownerResolved) result.Unresolved.Add(business.key + ".owner/operator");
                else if (string.IsNullOrWhiteSpace(business.ownerPersonKey) || !peopleByKey.Contains(business.ownerPersonKey))
                    result.Errors.Add(business.key + " marks its owner resolved but references no known Person slot.");
                if (!business.openingCashResolved) result.Unresolved.Add(business.key + ".opening cash");
                else if (business.openingCashCents < 0) result.Errors.Add(business.key + " marks opening cash resolved but supplies no non-negative amount.");
            }

            foreach (Operation expected in Enum.GetValues(typeof(Operation)))
                if (!operations.Contains(expected)) result.Errors.Add("Missing Business operation slot: " + expected + ".");

            foreach (PersonRelationshipSlot relation in AdditionalRelationships)
            {
                if (relation == null || string.IsNullOrWhiteSpace(relation.firstPersonKey)
                    || string.IsNullOrWhiteSpace(relation.secondPersonKey)
                    || !peopleByKey.Contains(relation.firstPersonKey) || !peopleByKey.Contains(relation.secondPersonKey)
                    || string.Equals(relation.firstPersonKey, relation.secondPersonKey, StringComparison.OrdinalIgnoreCase))
                    result.Errors.Add("Additional relationship records must connect two distinct known Person slots.");
            }

            ValidateBusinessOwner("general-store", "base:0", Operation.GeneralStore, result);
            ValidateBusinessOwner("bakery", "base:2", Operation.Bakery, result);
            ValidateBusinessOwner("fuel-dealer", "base:4", Operation.FuelDealer, result);
            ValidateBusinessOwner("freight-company", "ethan", Operation.FreightCompany, result);
        }

        private void ValidateBusinessOwner(string key, string personKey, Operation operation, ExpandedSliceFixtureValidation result)
        {
            BusinessSlot found = null;
            foreach (BusinessSlot business in Businesses)
                if (business != null && string.Equals(business.key, key, StringComparison.OrdinalIgnoreCase)) found = business;
            if (found == null || found.operation != operation || !found.ownerResolved
                || !string.Equals(found.ownerPersonKey, personKey, StringComparison.OrdinalIgnoreCase))
                result.Errors.Add($"Business slot '{key}' must map {operation} to owner '{personKey}'.");
        }

        private PersonSlot FindPerson(string key)
        {
            foreach (PersonSlot person in AdditionalPeople)
                if (person != null && string.Equals(person.key, key, StringComparison.OrdinalIgnoreCase)) return person;
            return null;
        }
    }

    public sealed class ExpandedSliceFixtureValidation
    {
        public List<string> Errors { get; } = new List<string>();
        public List<string> Unresolved { get; } = new List<string>();
        public bool IsStructurallyValid => Errors.Count == 0;
        public bool IsReadyForRuntime => IsStructurallyValid && Unresolved.Count == 0;
    }
}
