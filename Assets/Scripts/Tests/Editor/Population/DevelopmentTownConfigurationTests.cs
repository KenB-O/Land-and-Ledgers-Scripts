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
        public void AuthoredConfiguration_DirectlyDefinesTheCompleteDevelopmentTown()
        {
            DevelopmentTownConfiguration configuration = AssetDatabase.LoadAssetAtPath<DevelopmentTownConfiguration>(AssetPath);
            Assert.IsNotNull(configuration);
            Assert.IsTrue(configuration.Validate(out string error), error);
            Assert.AreEqual(9, configuration.People.Count);
            Assert.AreEqual(5, configuration.Households.Count);
            Assert.AreEqual(5, configuration.Relationships.Count);
            Assert.AreEqual(4, configuration.Businesses.Count);
            Assert.IsTrue(configuration.TryGetPerson("ethan", out DevelopmentTownConfiguration.PersonDefinition ethan));
            Assert.AreEqual("Ethan", ethan.firstName);
            Assert.AreEqual(7, configuration.ResolveRuntimePersonId("ethan"));
            Assert.AreEqual(9, configuration.ResolveRuntimePersonId("shane"));
            Assert.AreEqual("A0", configuration.OpeningHorseId);
            Assert.AreEqual("opening-freight-wagon", configuration.OpeningWagonId);
            Assert.AreEqual("freight-company", configuration.FreightBusinessKey);
        }

        [Test]
        public void Configuration_PreservesOwnersCashAndGenericOpeningInventory()
        {
            DevelopmentTownConfiguration configuration = AssetDatabase.LoadAssetAtPath<DevelopmentTownConfiguration>(AssetPath);
            Assert.IsNotNull(configuration);
            Assert.AreEqual("base:0", configuration.Businesses.Single(item => item.key == "general-store").ownerPersonKey);
            Assert.AreEqual("base:2", configuration.Businesses.Single(item => item.key == "bakery").ownerPersonKey);
            Assert.AreEqual("base:4", configuration.Businesses.Single(item => item.key == "fuel-dealer").ownerPersonKey);
            Assert.AreEqual("ethan", configuration.Businesses.Single(item => item.key == "freight-company").ownerPersonKey);
            Assert.AreEqual(4, configuration.BusinessCashTargets.Count);
            Assert.AreEqual(5, configuration.HouseholdCashTargets.Count);
            Assert.IsTrue(configuration.OpeningInventory.Any(item => item.businessKey == "general-store"));
            Assert.IsTrue(configuration.OpeningInventory.All(item => configuration.TryGetBusiness(item.businessKey, out _)));
            Assert.AreEqual(0, configuration.Households.Sum(item => item.openingCashCents),
                "Development household liquidity has one scenario allocation authority, not a base seed plus top-up.");
            Assert.AreEqual(50000, configuration.BusinessCashTargets.Single(item => item.businessKey == "general-store").targetCashCents);
            Assert.AreEqual(35000, configuration.BusinessCashTargets.Single(item => item.businessKey == "bakery").targetCashCents);
            Assert.AreEqual(120, configuration.OpeningBakeryFlourPounds);
            Assert.AreEqual(30, configuration.OpeningBakeryNotionsUnits);
        }

        [Test]
        public void Configuration_AllowsMoreBusinessRecordsWithoutASecondFixtureType()
        {
            DevelopmentTownConfiguration configuration = AssetDatabase.LoadAssetAtPath<DevelopmentTownConfiguration>(AssetPath);
            Assert.IsNotNull(configuration);
            Assert.AreEqual("general-store", configuration.Businesses[0].operationId);
            Assert.IsTrue(configuration.Businesses.All(business => !string.IsNullOrWhiteSpace(business.operationId)));
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
