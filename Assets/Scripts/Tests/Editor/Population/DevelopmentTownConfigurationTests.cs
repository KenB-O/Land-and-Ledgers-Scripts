using System.Linq;
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
            Assert.AreEqual(4, configuration.HouseholdCashTargets.Count);
            Assert.IsTrue(configuration.OpeningInventory.Any(item => item.businessKey == "general-store"));
            Assert.IsTrue(configuration.OpeningInventory.All(item => configuration.TryGetBusiness(item.businessKey, out _)));
        }

        [Test]
        public void Configuration_AllowsMoreBusinessRecordsWithoutASecondFixtureType()
        {
            DevelopmentTownConfiguration configuration = AssetDatabase.LoadAssetAtPath<DevelopmentTownConfiguration>(AssetPath);
            Assert.IsNotNull(configuration);
            Assert.AreEqual("general-store", configuration.Businesses[0].operationId);
            Assert.IsTrue(configuration.Businesses.All(business => !string.IsNullOrWhiteSpace(business.operationId)));
        }
    }
}
