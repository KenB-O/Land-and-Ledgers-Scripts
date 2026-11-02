using System;
using System.Linq;
using System.Reflection;
using LandLedgers.Population;
using LandLedgers.Economy;
using LandLedgers.Economy.Creation;
using LandLedgers.FirstLedger;
using LandLedgers.World;
using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

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
            Assert.IsTrue(result.IsReadyForRuntime, string.Join("; ", result.Unresolved));
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
        public void DefaultModel_UsesSuppliedRosterAndMarksOnlyProvisionalAgesAsCalibration()
        {
            ExpandedSliceFixture.PersonSlot ethan = expandedFixture.AdditionalPeople.Single(person => person.key == "ethan");
            ExpandedSliceFixture.PersonSlot tia = expandedFixture.AdditionalPeople.Single(person => person.key == "tia");
            ExpandedSliceFixture.PersonSlot shane = expandedFixture.AdditionalPeople.Single(person => person.key == "shane");
            ExpandedSliceFixture.HouseholdSlot ethanTia = expandedFixture.AdditionalHouseholds.Single(household => household.key == "ethan-tia");

            Assert.AreEqual("Ethan Boniecki", ethan.knownDisplayName);
            Assert.AreEqual("Tia Boniecki", tia.knownDisplayName);
            Assert.AreEqual("Shane Bryan", shane.knownDisplayName);
            Assert.AreEqual("Ethan Boniecki", ethan.fullLegalName);
            Assert.AreEqual("Tia Boniecki", tia.fullLegalName);
            Assert.IsTrue(ethan.ageResolved && ethan.ageIsProvisionalCalibration);
            Assert.IsTrue(tia.ageResolved && tia.ageIsProvisionalCalibration);
            Assert.IsTrue(shane.ageResolved && shane.ageIsProvisionalCalibration);
            Assert.IsTrue(ethanTia.relationshipBetweenMembersResolved);
            Assert.AreEqual("Spouses", ethanTia.relationshipBetweenMembers);
            Assert.AreEqual(3, ethanTia.homeBuildingOrdinal);
            Assert.AreEqual(4, expandedFixture.AdditionalHouseholds.Single(household => household.key == "shane").homeBuildingOrdinal);
            Assert.IsFalse(expandedFixture.AdditionalRelationships.Any(relation =>
                relation.firstPersonKey.Contains("parent", StringComparison.OrdinalIgnoreCase)
                || relation.secondPersonKey.Contains("parent", StringComparison.OrdinalIgnoreCase)),
                "The fixture must not fabricate unknown ancestors.");
        }

        [Test]
        public void DefaultModel_MapsFourBusinessesToCorrectOperatorsAndSeparateFixtureCash()
        {
            Assert.AreEqual("base:0", expandedFixture.Businesses.Single(b => b.operation == ExpandedSliceFixture.Operation.GeneralStore).ownerPersonKey);
            Assert.AreEqual("base:2", expandedFixture.Businesses.Single(b => b.operation == ExpandedSliceFixture.Operation.Bakery).ownerPersonKey);
            Assert.AreEqual("base:4", expandedFixture.Businesses.Single(b => b.operation == ExpandedSliceFixture.Operation.FuelDealer).ownerPersonKey);
            Assert.AreEqual("ethan", expandedFixture.Businesses.Single(b => b.operation == ExpandedSliceFixture.Operation.FreightCompany).ownerPersonKey);
            Assert.AreEqual(25000, expandedFixture.Businesses.Single(b => b.operation == ExpandedSliceFixture.Operation.GeneralStore).openingCashCents);
            Assert.AreEqual(17500, expandedFixture.Businesses.Single(b => b.operation == ExpandedSliceFixture.Operation.Bakery).openingCashCents);
            Assert.AreEqual(20000, expandedFixture.Businesses.Single(b => b.operation == ExpandedSliceFixture.Operation.FuelDealer).openingCashCents);
            ExpandedSliceFixture.BusinessSlot freight = expandedFixture.Businesses.Single(b => b.operation == ExpandedSliceFixture.Operation.FreightCompany);
            Assert.AreEqual(10000, freight.openingCashCents);
            Assert.IsFalse(freight.premisesRequired, "Freight entity establishment does not require a depot.");
        }

        [Test]
        public void DefaultModel_UsesSpecifiedNpcHouseholdCashWithoutDuplicatingPlayerHousehold()
        {
            CollectionAssert.AreEquivalent(new[] { 6500, 7500 }, expandedFixture.BaseHouseholdEndowments.Select(x => x.openingCashCents));
            Assert.AreEqual(6000, expandedFixture.AdditionalHouseholds.Single(h => h.key == "ethan-tia").openingCashCents);
            Assert.AreEqual(12500, expandedFixture.AdditionalHouseholds.Single(h => h.key == "shane").openingCashCents);
            Assert.IsFalse(expandedFixture.BaseHouseholdEndowments.Any(x => x.householdIndex == 0),
                "The existing player household balance must be preserved, not duplicated by this profile.");
        }

        [Test]
        public void DefaultModel_RecordsKnownJakeEthanSiblingWithoutInventingParents()
        {
            ExpandedSliceFixture.PersonRelationshipSlot relation = expandedFixture.AdditionalRelationships.Single();
            Assert.AreEqual("base:4", relation.firstPersonKey);
            Assert.AreEqual("ethan", relation.secondPersonKey);
            Assert.AreEqual(ExpandedSliceFixture.Relationship.Sibling, relation.relationship);
            Assert.AreEqual("Tannis Pierce", expandedFixture.FindPersonDisplayName("base:3"));
        }

        [Test]
        public void FixtureCashGrant_IsOnceOnlyAndTargetsBalanceRatherThanAddingDuplicateCapital()
        {
            var ledgers = new HouseholdLedgerRegistry();
            MethodInfo method = typeof(PopulationGenerator).GetMethod(
                "SeedFixtureCash", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            method.Invoke(null, new object[] { ledgers, 4, 6000, 0, "expanded:household:ethan-tia" });
            method.Invoke(null, new object[] { ledgers, 4, 6000, 1, "expanded:household:ethan-tia" });
            Assert.AreEqual(6000, ledgers.Get(4).GetBalanceCents());
            Assert.AreEqual(1, ledgers.Get(4).Entries.Count);
        }

        [Test]
        public void AuthoredCoreAsset_LoadsTheExistingSixPersonBaseAndValidExpandedRoster()
        {
            ExpandedSliceFixture asset = AssetDatabase.LoadAssetAtPath<ExpandedSliceFixture>(
                "Assets/Core/Population/ExpandedSliceFixture.asset");
            Assert.IsNotNull(asset);
            Assert.IsNotNull(asset.SixPersonFixture);
            Assert.AreEqual("expanded-nine-person-development", asset.ProfileId);
            ExpandedSliceFixtureValidation result = asset.Validate();
            Assert.IsTrue(result.IsReadyForRuntime, string.Join("; ", result.Errors) + " / " + string.Join("; ", result.Unresolved));
        }

        [Test]
        public void ExpandedOpeningRoster_UsesExistingFormationAuthorityInputsAndNoFreightPremises()
        {
            MethodInfo method = typeof(FirstLedgerSliceBootstrapper).GetMethod(
                "BuildExpandedOpeningRoster", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            var roster = (List<OpeningRosterEntry>)method.Invoke(null, new object[] { expandedFixture });
            Assert.AreEqual(4, roster.Count);
            CollectionAssert.AreEquivalent(new[] { BusinessType.GeneralStore, BusinessType.Bakery,
                BusinessType.FuelDealer, BusinessType.LiveryFreight }, roster.ConvertAll(entry => entry.BusinessType));
            OpeningRosterEntry freight = roster.Find(entry => entry.BusinessType == BusinessType.LiveryFreight);
            Assert.AreEqual(7, freight.OwnerPersonId);
            Assert.IsTrue(freight.PremisesRequirement.IsSiteFree);
            Assert.AreEqual(10000, freight.StartingCapitalCents);
        }

        [Test]
        public void RuntimeAppend_CreatesNinePeopleFiveSeparateHomesAndExpectedKinship()
        {
            GameObject worldObject = new GameObject("ExpandedSliceFixtureWorld");
            GameObject managerObject = new GameObject("ExpandedSliceFixturePopulationManager");
            PopulationGenerationSettings settings = ScriptableObject.CreateInstance<PopulationGenerationSettings>();
            TownWorldController world = worldObject.AddComponent<TownWorldController>();
            PopulationManager manager = managerObject.AddComponent<PopulationManager>();
            var definitions = new List<BuildingDefinition>();
            try
            {
                SetPrivateField(world, "settings", new TownGenerationSettings { seed = 1886 });
                SetPrivateField(world, "grid", new TownGrid(60, 60, 2f, Vector3.zero));
                var plots = (List<TownPlot>)typeof(TownWorldController)
                    .GetField("plots", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(world);
                var buildings = (List<PlacedBuilding>)typeof(TownWorldController)
                    .GetField("buildings", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(world);
                for (int i = 0; i < 5; i++)
                {
                    plots.Add(new TownPlot { id = 20 + i, zone = PlotZone.Residential });
                    BuildingDefinition definition = ScriptableObject.CreateInstance<BuildingDefinition>();
                    definition.ConfigureRuntimeFallback($"expanded_house_{i}", $"Expanded House {i}",
                        PlotZone.Residential, new Vector2Int(4, 4), Color.white, 4f);
                    definitions.Add(definition);
                    buildings.Add(new PlacedBuilding { id = 201 + i, plotId = 20 + i, definition = definition });
                }

                manager.Configure(world, settings, false);
                manager.HouseholdLedgers = new HouseholdLedgerRegistry();
                manager.GeneratePopulationSnapshot(sixPersonFixture);
                int playerHouseholdId = manager.State.GetPerson(1).householdId;
                HouseholdLedger playerHouseholdLedger = manager.HouseholdLedgers.Get(playerHouseholdId);
                int playerHouseholdBalanceBeforeAppend = playerHouseholdLedger != null
                    ? playerHouseholdLedger.GetBalanceCents() : 0;
                int playerHouseholdEntryCountBeforeAppend = playerHouseholdLedger != null
                    ? playerHouseholdLedger.Entries.Count : 0;
                PopulationGenerationResult result = PopulationGenerator.AppendExpandedDevelopmentFixture(
                    world, settings, manager.State, expandedFixture, manager.Lifecycle, manager.Kinship,
                    manager.HouseholdLedgers, 0, 1870, 1, 1);

                Assert.IsFalse(result.HasErrors, result.Summary);
                Assert.AreEqual(9, manager.State.people.Count);
                Assert.AreEqual(5, manager.State.households.Count);
                CollectionAssert.AreEqual(new[] { "Kennedy Baldwin-Ooms", "Emma Baldwin-Ooms", "Glen Pierce",
                    "Tannis Pierce", "Jacob Boniecki", "Grace Boniecki", "Ethan Boniecki", "Tia Boniecki", "Shane Bryan" },
                    manager.State.people.OrderBy(person => person.id).Select(person => person.DisplayName).ToArray());
                Assert.AreEqual(5, manager.State.households.Select(household => household.homeBuildingId).Distinct().Count());
                Assert.AreEqual(9, manager.Memberships.ActiveCount);
                CollectionAssert.Contains(manager.Kinship.GetSpouses(3), 4);
                CollectionAssert.Contains(manager.Kinship.GetSpouses(7), 8);
                CollectionAssert.Contains(manager.Kinship.GetRelated(5, KinshipRelation.Sibling), 7);
                Assert.AreEqual(6500, manager.HouseholdLedgers.Get(manager.State.GetPerson(3).householdId).GetBalanceCents());
                Assert.AreEqual(7500, manager.HouseholdLedgers.Get(manager.State.GetPerson(5).householdId).GetBalanceCents());
                Assert.AreEqual(6000, manager.HouseholdLedgers.Get(manager.State.GetPerson(7).householdId).GetBalanceCents());
                Assert.AreEqual(12500, manager.HouseholdLedgers.Get(manager.State.GetPerson(9).householdId).GetBalanceCents());
                Assert.IsTrue(manager.State.GetPerson(1).birthDate.IsValid);
                Assert.AreEqual(playerHouseholdBalanceBeforeAppend,
                    playerHouseholdLedger != null ? playerHouseholdLedger.GetBalanceCents() : 0,
                    "The expanded append must preserve the player household's pre-existing ledger balance.");
                Assert.AreEqual(playerHouseholdEntryCountBeforeAppend,
                    playerHouseholdLedger != null ? playerHouseholdLedger.Entries.Count : 0,
                    "The expanded append must not add a cash entry to the player household ledger.");
                Assert.IsFalse(playerHouseholdLedger != null && playerHouseholdLedger.Entries.Any(entry =>
                    string.Equals(entry.SourceReference,
                        "expanded-nine-person-development:household:base-0", StringComparison.Ordinal)),
                    "The player household must not receive a duplicate fixture endowment.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(managerObject);
                UnityEngine.Object.DestroyImmediate(worldObject);
                foreach (BuildingDefinition definition in definitions) UnityEngine.Object.DestroyImmediate(definition);
                UnityEngine.Object.DestroyImmediate(settings);
            }
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
