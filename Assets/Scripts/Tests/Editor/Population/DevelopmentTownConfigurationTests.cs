using System.Linq;
using LandLedgers.Economy;
using LandLedgers.Population;
using NUnit.Framework;
using UnityEditor;

namespace LandLedgers.EditorTests.Population
{
    [TestFixture]
    public sealed class DevelopmentTownConfigurationTests
    {
        private const string AssetPath = "Assets/Core/Population/DevelopmentTownConfiguration.asset";

        [Test]
        public void OwnerAuthoredConfiguration_ReadsBackCompleteStableRosterAndOwnership()
        {
            DevelopmentTownConfiguration configuration = AssetDatabase.LoadAssetAtPath<DevelopmentTownConfiguration>(AssetPath);
            Assert.IsNotNull(configuration);
            Assert.IsTrue(configuration.Validate(out string error), error);
            Assert.AreEqual(2, configuration.SchemaVersion);
            Assert.AreEqual(14, configuration.People.Count);
            Assert.AreEqual(8, configuration.Households.Count);
            Assert.AreEqual(7, configuration.Businesses.Count);
            Assert.AreEqual(configuration.People.Count, configuration.People.Select(p => p.key).Distinct().Count());
            Assert.AreEqual(configuration.People.Count, configuration.People.Select(p => p.runtimePersonId).Distinct().Count());
            Assert.AreEqual(configuration.Households.Count, configuration.Households.Select(h => h.runtimeHouseholdId).Distinct().Count());

            Assert.IsTrue(configuration.TryGetPerson("richard-sithole", out var richard));
            Assert.AreEqual("Richard Sithole", richard.firstName + " " + richard.lastName);
            Assert.AreEqual(10, configuration.ResolveRuntimePersonId("richard-sithole"));
            Assert.IsTrue(configuration.TryGetPerson("enzo-kwindt", out var enzo));
            Assert.AreEqual("Enzo Kwindt", enzo.firstName + " " + enzo.lastName);
            Assert.IsTrue(configuration.TryGetPerson("josh-saunders", out var josh));
            Assert.AreEqual("Josh Saunders", josh.firstName + " " + josh.lastName);
            Assert.IsTrue(configuration.TryGetPerson("shane-bryan", out var shane));
            Assert.AreEqual("Unemployed", shane.occupation, "He is not one of the two authored sawmill workers.");

            DevelopmentTownConfiguration.HouseholdDefinition millward = configuration.Households.Single(h => h.key == "millward");
            Assert.AreEqual("Millward Household", millward.displayName);
            Assert.AreEqual("Millward", millward.surname);
            CollectionAssert.AreEqual(new[] { "don-millward", "grace-millward" }, millward.memberPersonKeys);
            Assert.AreEqual(7, millward.homeBuildingOrdinal);
            Assert.IsTrue(configuration.TryGetPerson("grace-millward", out var grace));
            Assert.IsTrue(grace.isFemale);

            DevelopmentTownConfiguration.HouseholdDefinition kwindt = configuration.Households.Single(h => h.key == "kwindt");
            CollectionAssert.AreEqual(new[] { "enzo-kwindt", "josh-saunders" }, kwindt.memberPersonKeys);
            Assert.AreEqual(6, kwindt.homeBuildingOrdinal);
            Assert.AreEqual("enzo-kwindt", configuration.FindBusiness("sawmill").ownerPersonKey);
            Assert.AreEqual("don-millward", configuration.FindBusiness("carpentry-workshop").ownerPersonKey);
        }

        [Test]
        public void OpeningCashAndResourcesHaveSingleOwnerOrganizedSource()
        {
            DevelopmentTownConfiguration configuration = AssetDatabase.LoadAssetAtPath<DevelopmentTownConfiguration>(AssetPath);
            Assert.IsNotNull(configuration);
            Assert.AreEqual(320000, configuration.Households.Sum(h => h.openingCashCents));
            Assert.IsTrue(configuration.Households.All(h => h.openingCashCents == 40000));
            CollectionAssert.AreEqual(new[] { 50000, 35000, 20000, 20000, 15000, 12000, 10000 },
                configuration.Businesses.Select(b => b.openingCashCents).ToArray());

            var store = configuration.FindBusiness("general-store");
            Assert.AreEqual(15, store.opening.inventory.Length);
            Assert.IsTrue(store.opening.inventory.Any(i => i.itemId == "bread" && i.minimumUnits == 24 && i.maximumUnits == 48));
            Assert.AreEqual(1, configuration.FindBusiness("bakery").opening.equipment.ovens.Length);
            Assert.AreEqual(40, configuration.FindBusiness("bakery").opening.bakeryFuelUnits);
            Assert.AreEqual(120, configuration.FindBusiness("bakery").opening.bakeryFlourPounds);
            Assert.AreEqual(30, configuration.FindBusiness("bakery").opening.bakeryNotionsUnits);
            Assert.AreEqual(15, configuration.FindBusiness("carpentry-workshop").opening.carpentryLumberUnits);
            Assert.AreEqual("A0", configuration.FindBusiness("freight-company").opening.openingHorseId);
            Assert.AreEqual("opening-freight-wagon", configuration.FindBusiness("freight-company").opening.openingWagonId);

            var serialized = new SerializedObject(configuration);
            Assert.IsNull(serialized.FindProperty("businessCashTargets"));
            Assert.IsNull(serialized.FindProperty("householdCashTargets"));
            Assert.IsNull(serialized.FindProperty("openingInventory"));
            Assert.IsNull(serialized.FindProperty("openingEquipment"));
            Assert.IsNull(serialized.FindProperty("openingEmployees"));
        }

        [Test]
        public void SawmillHasConfiguredOwnerAndOnlyRichardAndJoshAsOpeningWorkers()
        {
            DevelopmentTownConfiguration configuration = AssetDatabase.LoadAssetAtPath<DevelopmentTownConfiguration>(AssetPath);
            var sawmill = configuration.FindBusiness("sawmill");
            Assert.AreEqual("enzo-kwindt", sawmill.ownerPersonKey);
            Assert.AreEqual(6, sawmill.opening.equipment.assetEntries.Length);
            Assert.AreEqual(7, sawmill.opening.loggingCampProvisionCoverDays);
            Assert.AreEqual(1, sawmill.opening.spareBlades.Length);
            Assert.AreEqual(2, sawmill.opening.employees.Length);
            Assert.IsTrue(sawmill.opening.employees.Any(e => e.personKey == "richard-sithole" && e.weeklyWageCents == 750));
            Assert.IsTrue(sawmill.opening.employees.Any(e => e.personKey == "josh-saunders" && e.weeklyWageCents == 750));
            Assert.IsFalse(sawmill.opening.employees.Any(e => e.personKey == "shane-bryan"));
            Assert.AreEqual(2, configuration.TimberStands.Count);
        }

        [Test]
        public void SchemaMigration_IsIdempotentAndGeneratedConfigurationsPreserveOpeningAllocations()
        {
            DevelopmentTownConfiguration configuration = AssetDatabase.LoadAssetAtPath<DevelopmentTownConfiguration>(AssetPath);
            string before = EditorJsonUtility.ToJson(configuration, true);
            LandLedgers.EditorTools.Population.DevelopmentTownConfigurationMigration.MigrateAuthoritativeAsset();
            Assert.AreEqual(before, EditorJsonUtility.ToJson(configuration, true));

            foreach (string path in new[]
                     {
                         "Assets/Core/Population/MediumDevelopmentTownConfiguration.asset",
                         "Assets/Core/Population/LargeDevelopmentTownConfiguration.asset"
                     })
            {
                var generated = AssetDatabase.LoadAssetAtPath<DevelopmentTownConfiguration>(path);
                Assert.IsNotNull(generated);
                Assert.IsTrue(generated.Validate(out string error), error);
                Assert.IsTrue(generated.GeneratePopulationFromSettings);
                Assert.Greater(generated.HouseholdCashTargets.Count, 0);
                Assert.IsTrue(generated.HouseholdCashTargets.All(t => t.targetCashCents == 25000));
                Assert.IsFalse(string.IsNullOrWhiteSpace(generated.OpeningWagonId));
            }
        }

        [Test]
        public void GeneralStoreHouseholdSkus_AreRecognizedWithTheirAuthoredItems()
        {
            BusinessProfileDefinition store = AssetDatabase.LoadAssetAtPath<BusinessProfileDefinition>(
                "Assets/Core/Economy/GeneralStoreBusinessDefinition.asset");
            Assert.IsNotNull(store);
            string[] ids = { "bread", "flour", "potatoes", "salt", "beef", "bacon", "cheese",
                "basic_cloth", "flannel", "workwear", "boots", "blanket", "hat", "soap", "candles",
                "lamp_oil", "lantern", "thread", "buttons", "nails_hardware_lot", "hand_tool", "farm_tool",
                "patent_remedy", "medicine_bottle" };
            foreach (string id in ids)
            {
                Assert.IsNotNull(store.FindItem(id), $"General Store SKU '{id}' should be authored.");
                Assert.IsNotNull(HouseholdItemCatalog.Get(id), $"Household shopping should recognize exact SKU '{id}'.");
            }
        }
    }
}
