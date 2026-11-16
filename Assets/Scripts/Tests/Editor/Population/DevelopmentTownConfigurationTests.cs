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
            Assert.AreEqual(15, configuration.People.Count);
            Assert.AreEqual(8, configuration.Households.Count);
            Assert.AreEqual(7, configuration.Relationships.Count);
            Assert.AreEqual(7, configuration.Businesses.Count);
            Assert.IsTrue(configuration.TryGetPerson("ethan", out DevelopmentTownConfiguration.PersonDefinition ethan));
            Assert.AreEqual("Ethan", ethan.firstName);
            Assert.AreEqual(7, configuration.ResolveRuntimePersonId("ethan"));
            Assert.AreEqual(9, configuration.ResolveRuntimePersonId("shane"));
            // C-1 (expanded campaign, Phase 1 forestry): the mill hand.
            Assert.IsTrue(configuration.TryGetPerson("silas", out DevelopmentTownConfiguration.PersonDefinition silas));
            Assert.AreEqual("Silas", silas.firstName);
            Assert.AreEqual("Ward", silas.lastName);
            Assert.AreEqual(24, silas.age);
            Assert.AreEqual("Sawmill hand", silas.occupation);
            Assert.AreEqual(5, silas.homeBuildingOrdinal);
            Assert.AreEqual(10, configuration.ResolveRuntimePersonId("silas"));
            DevelopmentTownConfiguration.HouseholdDefinition silasHousehold =
                configuration.Households.Single(item => item.key == "silas");
            Assert.AreEqual(1, silasHousehold.memberPersonKeys.Length);
            Assert.AreEqual("silas", silasHousehold.memberPersonKeys[0]);
            Assert.AreEqual(5, silasHousehold.homeBuildingOrdinal);
            // C-8 (expanded campaign, Phase 1 forestry): Enzo Kwint runs
            // the sawmill with his wife Emma; Josh Saunders labours for
            // Enzo and boards with the Kwints (D-10/D-11 owns his permanent
            // residence). Owner-directed names (Kennedy 2026-11-16); ages
            // are provisional calibrations. Emma Kwint is a distinct person
            // from Emma Baldwin-Ooms (base:1).
            Assert.IsTrue(configuration.TryGetPerson("enzo", out DevelopmentTownConfiguration.PersonDefinition enzo));
            Assert.AreEqual("Enzo", enzo.firstName);
            Assert.AreEqual("Kwint", enzo.lastName);
            Assert.AreEqual(40, enzo.age);
            Assert.AreEqual("Sawmill owner/operator", enzo.occupation);
            Assert.AreEqual(6, enzo.homeBuildingOrdinal);
            Assert.AreEqual(11, configuration.ResolveRuntimePersonId("enzo"));
            Assert.IsTrue(configuration.TryGetPerson("emma-kwint", out DevelopmentTownConfiguration.PersonDefinition emmaKwint));
            Assert.AreEqual("Emma", emmaKwint.firstName);
            Assert.AreEqual("Kwint", emmaKwint.lastName);
            Assert.AreEqual(34, emmaKwint.age);
            Assert.AreEqual("Unspecified", emmaKwint.occupation);
            Assert.AreEqual(6, emmaKwint.homeBuildingOrdinal);
            Assert.AreEqual(12, configuration.ResolveRuntimePersonId("emma-kwint"));
            Assert.IsTrue(configuration.TryGetPerson("josh", out DevelopmentTownConfiguration.PersonDefinition josh));
            Assert.AreEqual("Josh", josh.firstName);
            Assert.AreEqual("Saunders", josh.lastName);
            Assert.AreEqual(26, josh.age);
            Assert.AreEqual("Sawmill labourer", josh.occupation);
            Assert.AreEqual(6, josh.homeBuildingOrdinal);
            Assert.AreEqual(13, configuration.ResolveRuntimePersonId("josh"));
            DevelopmentTownConfiguration.HouseholdDefinition kwintHousehold =
                configuration.Households.Single(item => item.key == "enzo-emma");
            Assert.AreEqual(3, kwintHousehold.memberPersonKeys.Length);
            Assert.AreEqual("enzo", kwintHousehold.memberPersonKeys[0]);
            Assert.AreEqual("emma-kwint", kwintHousehold.memberPersonKeys[1]);
            Assert.AreEqual("josh", kwintHousehold.memberPersonKeys[2]);
            Assert.AreEqual(6, kwintHousehold.homeBuildingOrdinal);
            // F1 part 2 (O-F3): Don Millward is the carpentry workshop
            // owner/worker and Grace Millward his wife, in the same
            // household. Renumbered at integration merge to home ordinal
            // 7 and person ids 14/15: the F branch used ordinal 5 and ids
            // 10/11, which the integration tree already gives to Silas
            // Ward, and 6 is the Kwints' from C-8 (the F branch's "bump
            // to 6" note was wrong).
            Assert.IsTrue(configuration.TryGetPerson("don-millward", out DevelopmentTownConfiguration.PersonDefinition don));
            Assert.AreEqual("Don", don.firstName);
            Assert.AreEqual("Millward", don.lastName);
            Assert.AreEqual(7, don.homeBuildingOrdinal);
            Assert.IsTrue(configuration.TryGetPerson("grace-millward", out DevelopmentTownConfiguration.PersonDefinition grace));
            Assert.AreEqual("Grace", grace.firstName);
            Assert.AreEqual("Millward", grace.lastName);
            Assert.AreEqual(7, grace.homeBuildingOrdinal);
            Assert.AreEqual(14, configuration.ResolveRuntimePersonId("don-millward"));
            Assert.AreEqual(15, configuration.ResolveRuntimePersonId("grace-millward"));
            DevelopmentTownConfiguration.HouseholdDefinition millwardHousehold =
                configuration.Households.Single(item => item.key == "millward");
            Assert.AreEqual(2, millwardHousehold.memberPersonKeys.Length);
            Assert.AreEqual("don-millward", millwardHousehold.memberPersonKeys[0]);
            Assert.AreEqual("grace-millward", millwardHousehold.memberPersonKeys[1]);
            Assert.AreEqual(7, millwardHousehold.homeBuildingOrdinal);
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
            // C-8: Enzo Kwint runs the sawmill (Kennedy 2026-11-16); Shane
            // stays on as a sawmill hand under him, never a second owner.
            Assert.AreEqual("enzo", configuration.Businesses.Single(item => item.key == "sawmill").ownerPersonKey);
            Assert.AreEqual("base:5", configuration.Businesses.Single(item => item.key == "lumber-yard").ownerPersonKey);
            Assert.AreEqual("don-millward", configuration.Businesses.Single(item => item.key == "carpentry-workshop").ownerPersonKey);
            Assert.AreEqual("carpentry-workshop", configuration.Businesses.Single(item => item.key == "carpentry-workshop").operationId);
            Assert.AreEqual(7, configuration.BusinessCashTargets.Count);
            Assert.AreEqual(15000, configuration.BusinessCashTargets.Single(item => item.businessKey == "sawmill").targetCashCents);
            Assert.AreEqual(12000, configuration.BusinessCashTargets.Single(item => item.businessKey == "lumber-yard").targetCashCents);
            Assert.AreEqual(10000, configuration.BusinessCashTargets.Single(item => item.businessKey == "carpentry-workshop").targetCashCents);
            Assert.AreEqual(8, configuration.HouseholdCashTargets.Count);
            Assert.AreEqual(40000, configuration.HouseholdCashTargets.Single(item => item.anchorPersonKey == "silas").targetCashCents);
            Assert.AreEqual(40000, configuration.HouseholdCashTargets.Single(item => item.anchorPersonKey == "enzo").targetCashCents);
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
        public void Configuration_MillOpeningCrewToolsAndProvisions_AreAuthored()
        {
            DevelopmentTownConfiguration configuration = AssetDatabase.LoadAssetAtPath<DevelopmentTownConfiguration>(AssetPath);
            Assert.IsNotNull(configuration);
            // C-1 (expanded campaign, Phase 1 forestry): two felling axes
            // for the two-person crew, seven cover days of camp provisions,
            // and the mill hand's opening employment. C-2b: the log wagon
            // (1200 kg two-horse logging wagon, OD-C4) the mill teamster
            // hauls with, beside the axes. C-3: the saw-line components
            // (saw-line, log-carriage, drive-system) the bootstrapper wires
            // into the mill's saw-line workstation. C-5: the one spare saw
            // blade the filer fits when the fitted blade retires.
            DevelopmentTownConfiguration.BusinessOpeningEquipment millEquipment =
                configuration.OpeningEquipment.Single(item => item.businessKey == "sawmill");
            Assert.AreEqual(6, millEquipment.assetEntries.Length);
            Assert.AreEqual("felling-axe-1", millEquipment.assetEntries[0].assetId);
            Assert.AreEqual("felling-axe", millEquipment.assetEntries[0].kind);
            Assert.AreEqual("felling-axe-2", millEquipment.assetEntries[1].assetId);
            Assert.AreEqual("felling-axe", millEquipment.assetEntries[1].kind);
            Assert.AreEqual("log-wagon-1", millEquipment.assetEntries[2].assetId);
            Assert.AreEqual("log-wagon", millEquipment.assetEntries[2].kind);
            Assert.AreEqual("saw-line-1", millEquipment.assetEntries[3].assetId);
            Assert.AreEqual("saw-line", millEquipment.assetEntries[3].kind);
            Assert.AreEqual("log-carriage-1", millEquipment.assetEntries[4].assetId);
            Assert.AreEqual("log-carriage", millEquipment.assetEntries[4].kind);
            Assert.AreEqual("drive-system-1", millEquipment.assetEntries[5].assetId);
            Assert.AreEqual("drive-system", millEquipment.assetEntries[5].kind);
            Assert.AreEqual(7, configuration.OpeningLoggingCampProvisionCoverDays);
            Assert.AreEqual(3, configuration.OpeningEmployees.Count);
            DevelopmentTownConfiguration.BusinessEmployeeDefinition millHand = configuration.OpeningEmployees[0];
            Assert.AreEqual("sawmill", millHand.businessKey);
            Assert.AreEqual("silas", millHand.personKey);
            Assert.AreEqual("logger", millHand.role);
            Assert.AreEqual(750, millHand.weeklyWageCents);
            // C-8: Shane stays on as a logger under the new owner Enzo, and
            // Josh labours for Enzo — both at the miller precedent wage.
            // The owner is never his own opening employee.
            DevelopmentTownConfiguration.BusinessEmployeeDefinition shaneHand = configuration.OpeningEmployees[1];
            Assert.AreEqual("sawmill", shaneHand.businessKey);
            Assert.AreEqual("shane", shaneHand.personKey);
            Assert.AreEqual("logger", shaneHand.role);
            Assert.AreEqual(750, shaneHand.weeklyWageCents);
            DevelopmentTownConfiguration.BusinessEmployeeDefinition joshHand = configuration.OpeningEmployees[2];
            Assert.AreEqual("sawmill", joshHand.businessKey);
            Assert.AreEqual("josh", joshHand.personKey);
            Assert.AreEqual("labourer", joshHand.role);
            Assert.AreEqual(750, joshHand.weeklyWageCents);
            Assert.AreEqual(1, configuration.OpeningSpareBlades.Count);
            DevelopmentTownConfiguration.BusinessSpareBladeDefinition spareBlade = configuration.OpeningSpareBlades[0];
            Assert.AreEqual("sawmill", spareBlade.businessKey);
            Assert.AreEqual("sawmill-spare-blade-1", spareBlade.bladeId);
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
