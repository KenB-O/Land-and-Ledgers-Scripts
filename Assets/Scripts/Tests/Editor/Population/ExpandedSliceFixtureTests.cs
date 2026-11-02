using System;
using System.Linq;
using System.Reflection;
using LandLedgers.Population;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Population
{
    [TestFixture]
    public sealed class ExpandedSliceFixtureTests
    {
        private SixPersonDevelopmentFixture sixPersonFixture;
        private ExpandedSliceFixture expandedFixture;

        [SetUp]
        public void SetUp()
        {
            sixPersonFixture = ScriptableObject.CreateInstance<SixPersonDevelopmentFixture>();
            expandedFixture = ScriptableObject.CreateInstance<ExpandedSliceFixture>();
            SetPrivateField(expandedFixture, "sixPersonFixture", sixPersonFixture);
        }

        [TearDown]
        public void TearDown()
        {
            if (expandedFixture != null) UnityEngine.Object.DestroyImmediate(expandedFixture);
            if (sixPersonFixture != null) UnityEngine.Object.DestroyImmediate(sixPersonFixture);
        }

        [Test]
        public void DefaultModel_ValidatesNinePeopleFiveHouseholdsAndFourDistinctBusinesses()
        {
            ExpandedSliceFixtureValidation result = expandedFixture.Validate();

            Assert.IsTrue(result.IsStructurallyValid, string.Join("; ", result.Errors));
            Assert.IsFalse(result.IsReadyForRuntime, "Unprovided identity, age, home, ownership, premises and cash data must block runtime readiness.");
            Assert.AreEqual(ExpandedSliceFixture.RequiredPersonCount,
                sixPersonFixture.People.Count + expandedFixture.AdditionalPeople.Count);
            Assert.AreEqual(ExpandedSliceFixture.RequiredHouseholdCount,
                sixPersonFixture.Households.Count + expandedFixture.AdditionalHouseholds.Count);
            Assert.AreEqual(ExpandedSliceFixture.RequiredBusinessCount, expandedFixture.Businesses.Count);
            CollectionAssert.AreEquivalent(
                Enum.GetValues(typeof(ExpandedSliceFixture.Operation)).Cast<ExpandedSliceFixture.Operation>(),
                expandedFixture.Businesses.Select(business => business.operation));
        }

        [Test]
        public void DefaultModel_LeavesUnprovidedPersonalAndHouseholdFactsExplicitlyUnresolved()
        {
            ExpandedSliceFixture.PersonSlot ethan = expandedFixture.AdditionalPeople.Single(person => person.key == "ethan");
            ExpandedSliceFixture.PersonSlot tia = expandedFixture.AdditionalPeople.Single(person => person.key == "tia");
            ExpandedSliceFixture.PersonSlot shane = expandedFixture.AdditionalPeople.Single(person => person.key == "shane");
            ExpandedSliceFixture.HouseholdSlot ethanTia = expandedFixture.AdditionalHouseholds.Single(household => household.key == "ethan-tia");

            Assert.AreEqual("Ethan B.", ethan.knownDisplayName);
            Assert.AreEqual("Tia B.", tia.knownDisplayName);
            Assert.AreEqual("Shane Bryan", shane.knownDisplayName);
            Assert.IsFalse(ethan.fullLegalNameResolved);
            Assert.IsFalse(tia.ageResolved);
            Assert.IsFalse(shane.ageResolved);
            Assert.IsFalse(shane.startingCashResolved);
            Assert.IsFalse(ethanTia.relationshipBetweenMembersResolved,
                "Co-residence must not imply marriage or another specific relationship.");

            CollectionAssert.Contains(expandedFixture.Validate().Unresolved, "ethan.age");
            CollectionAssert.Contains(expandedFixture.Validate().Unresolved, "ethan-tia.relationship between household members");
            CollectionAssert.Contains(expandedFixture.Validate().Unresolved, "shane.starting cash");
        }

        [Test]
        public void DuplicateBusinessOperation_MakesShapeInvalid()
        {
            var businesses = expandedFixture.Businesses.ToArray();
            businesses[3].operation = ExpandedSliceFixture.Operation.Bakery;
            SetPrivateField(expandedFixture, "businesses", businesses);

            ExpandedSliceFixtureValidation result = expandedFixture.Validate();
            Assert.IsFalse(result.IsStructurallyValid);
            Assert.IsTrue(result.Errors.Any(error => error.Contains("unique keys and one slot for each distinct operation")));
        }

        [Test]
        public void HouseholdMemberMismatch_MakesShapeInvalidWithoutChangingBaseFixture()
        {
            string baseProfile = sixPersonFixture.ProfileId;
            int basePersonCount = sixPersonFixture.People.Count;
            var households = expandedFixture.AdditionalHouseholds.ToArray();
            households[0].memberPersonKeys = new[] { "ethan", "not-a-person" };
            SetPrivateField(expandedFixture, "additionalHouseholds", households);

            ExpandedSliceFixtureValidation result = expandedFixture.Validate();
            Assert.IsFalse(result.IsStructurallyValid);
            Assert.IsTrue(result.Errors.Any(error => error.Contains("unknown or repeated Person slot")));
            Assert.AreEqual(baseProfile, sixPersonFixture.ProfileId);
            Assert.AreEqual(basePersonCount, sixPersonFixture.People.Count);
            Assert.IsTrue(sixPersonFixture.Validate(out string baseError), baseError);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Could not find serialized field " + fieldName);
            field.SetValue(target, value);
        }
    }
}
