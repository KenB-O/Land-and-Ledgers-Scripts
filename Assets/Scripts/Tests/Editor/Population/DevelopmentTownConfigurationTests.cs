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
            Assert.AreEqual(11, configuration.People.Count);
            Assert.AreEqual(6, configuration.Households.Count);
            Assert.AreEqual(6, configuration.Relationships.Count);
            Assert.AreEqual(7, configuration.Businesses.Count);
            Assert.IsTrue(configuration.TryGetPerson("ethan", out DevelopmentTownConfiguration.PersonDefinition ethan));
            Assert.AreEqual("Ethan", ethan.firstName);
            Assert.AreEqual(7, configuration.ResolveRuntimePersonId("ethan"));
            Assert.AreEqual(9, configuration.ResolveRuntimePersonId("shane"));
            // F1 part 2 (O-F3): Don Millward is the carpentry workshop
            // owner/worker and Grace Millward his wife, in the same
            // household (home ordinal 5 — the integration tree already
            // has Silas Ward at ordinal 5, so the parent must bump the
            // Millwards to 6 at integration merge).
            Assert.IsTrue(configuration.TryGetPerson("don-millward", out DevelopmentTownConfiguration.PersonDefinition don));
            Assert.AreEqual("Don", don.firstName);
            Assert.AreEqual("Millward", don.lastName);
            Assert.AreEqual(5, don.homeBuildingOrdinal);
            Assert.IsTrue(configuration.TryGetPerson("grace-millward", out DevelopmentTownConfiguration.PersonDefinition grace));
            Assert.AreEqual("Grace", grace.firstName);
            Assert.AreEqual("Millward", grace.lastName);
            Assert.AreEqual(5, grace.homeBuildingOrdinal);
            Assert.AreEqual(10, configuration.ResolveRuntimePersonId("don-millward"));
            Assert.AreEqual(11, configuration.ResolveRuntimePersonId("grace-millward"));
            Assert.AreEqual("A0", configuration.OpeningHorseId);
            Assert.AreEqual("opening-freight-wagon", configuration.OpeningWagonId);
            Assert.AreEqual("freight-company", configuration.FreightBusinessKey);
            // DEF-004: the dev scenario carries a sawmill, a lumber yard, and two legally accessible timber stands.
            Assert.AreEqual(2, configuration.TimberStands.Count);
            Assert.IsTrue(configuration.TimberStands.All(stand =>
                !string.IsNullOrWhiteSpace(stand.standId)
                && stand.standingTimberUnits > 0
                && stand.kilogramsPerLog > 0
                && stand.milesToMill > 0));
            Assert.AreEqual("stand-pine-north", configuration.TimberStands[0].standId);
            Assert.AreEqual("stand-pine-east", configuration.TimberStands[1].standId);
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
            Assert.AreEqual("shane", configuration.Businesses.Single(item => item.key == "sawmill").ownerPersonKey);
            Assert.AreEqual("base:5", configuration.Businesses.Single(item => item.key == "lumber-yard").ownerPersonKey);
            Assert.AreEqual("don-millward", configuration.Businesses.Single(item => item.key == "carpentry-workshop").ownerPersonKey);
            Assert.AreEqual("carpentry-workshop", configuration.Businesses.Single(item => item.key == "carpentry-workshop").operationId);
            Assert.AreEqual(7, configuration.BusinessCashTargets.Count);
            Assert.AreEqual(15000, configuration.BusinessCashTargets.Single(item => item.businessKey == "sawmill").targetCashCents);
            Assert.AreEqual(12000, configuration.BusinessCashTargets.Single(item => item.businessKey == "lumber-yard").targetCashCents);
            Assert.AreEqual(10000, configuration.BusinessCashTargets.Single(item => item.businessKey == "carpentry-workshop").targetCashCents);
            Assert.AreEqual(6, configuration.HouseholdCashTargets.Count);
            Assert.AreEqual(40000, configuration.HouseholdCashTargets.Single(item => item.anchorPersonKey == "don-millward").targetCashCents);
            Assert.IsTrue(configuration.OpeningInventory.Any(item => item.businessKey == "general-store"));
            Assert.IsTrue(configuration.OpeningInventory.All(item => configuration.TryGetBusiness(item.businessKey, out _)));
            Assert.AreEqual(0, configuration.Households.Sum(item => item.openingCashCents),
                "Development household liquidity has one scenario allocation authority, not a base seed plus top-up.");
            Assert.AreEqual(50000, configuration.BusinessCashTargets.Single(item => item.businessKey == "general-store").targetCashCents);
            Assert.AreEqual(35000, configuration.BusinessCashTargets.Single(item => item.businessKey == "bakery").targetCashCents);
            // DEF-013 (Decision 9): a business's opening cash is one
            // scenario-authored figure, not a creation floor plus a top-up
            // that disagrees with it. (Households are deliberately
            // different: their openingCashCents is pinned to 0 above, with
            // the target top-up as the single authority.)
            foreach (DevelopmentTownConfiguration.BusinessDefinition business in configuration.Businesses)
            {
                DevelopmentTownConfiguration.BusinessCashTarget target =
                    configuration.BusinessCashTargets.Single(item => item.businessKey == business.key);
                Assert.AreEqual(target.targetCashCents, business.openingCashCents,
                    $"Business '{business.key}' opens with {business.openingCashCents}c but its cash target is {target.targetCashCents}c.");
            }
            Assert.AreEqual(120, configuration.OpeningBakeryFlourPounds);
            Assert.AreEqual(30, configuration.OpeningBakeryNotionsUnits);
            Assert.AreEqual(15, configuration.OpeningCarpentryLumberUnits);
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
