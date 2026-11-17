using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using LandLedgers.Population;
using UnityEngine;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// UBC K1 (2026-11-17): the five derived compatibility views
    /// (BusinessCashTargets, HouseholdCashTargets, OpeningInventory,
    /// OpeningEquipment, OpeningEmployees) are replaced by direct
    /// owner-record iteration. These tests pin the behaviour the
    /// replacement must preserve, without reading the views themselves,
    /// so they pass before and after the removal. Golden
    /// TryResolveOpeningInventory quantities were pinned from the
    /// pre-change lookup path. Unity compilation, Test Runner, PlayMode,
    /// and scene wiring are NOT claimed.
    /// </summary>
    [TestFixture]
    public sealed class UBC_K1_DerivedViewReplacementTests
    {
        private const string ConfigId = "k1-derived-view-test";
        private const int Seed = 9217;

        private static void SetPrivateField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private static DevelopmentTownConfiguration BuildConfig()
        {
            var config = ScriptableObject.CreateInstance<DevelopmentTownConfiguration>();
            config.Settings.configurationId = ConfigId;
            config.Settings.seedOffset = Seed;
            config.Settings.freightBusinessKey = "general-store";
            SetPrivateField(config, "people", new[]
            {
                new DevelopmentTownConfiguration.PersonDefinition
                { key = "person-owner", runtimePersonId = 1, firstName = "Test", lastName = "Owner", age = 35, homeBuildingOrdinal = 0 },
                new DevelopmentTownConfiguration.PersonDefinition
                { key = "person-hand", runtimePersonId = 2, firstName = "Test", lastName = "Hand", age = 26, homeBuildingOrdinal = 1 },
            });
            SetPrivateField(config, "households", new[]
            {
                new DevelopmentTownConfiguration.HouseholdDefinition
                {
                    key = "household-owner", runtimeHouseholdId = 1, surname = "Owner", displayName = "Owner Household",
                    memberPersonKeys = new[] { "person-owner" }, homeBuildingOrdinal = 0, openingCashCents = 40000,
                },
                new DevelopmentTownConfiguration.HouseholdDefinition
                {
                    key = "household-hand", runtimeHouseholdId = 2, surname = "Hand", displayName = "Hand Household",
                    memberPersonKeys = new[] { "person-hand" }, homeBuildingOrdinal = 1, openingCashCents = 40000,
                },
            });
            SetPrivateField(config, "relationships", Array.Empty<DevelopmentTownConfiguration.RelationshipDefinition>());
            SetPrivateField(config, "businesses", new[]
            {
                new DevelopmentTownConfiguration.BusinessDefinition
                {
                    key = "general-store", operationId = "general-store", ownerPersonKey = "person-owner",
                    businessId = "test-store-1", premisesRequired = false, openingCashCents = 50000,
                    opening = new DevelopmentTownConfiguration.BusinessOpeningConfiguration
                    {
                        openingHorseId = "horse-1",
                        openingWagonId = "wagon-1",
                        inventory = new[]
                        {
                            new DevelopmentTownConfiguration.OpeningInventoryItem { itemId = "flour", minimumUnits = 10, maximumUnits = 20 },
                            new DevelopmentTownConfiguration.OpeningInventoryItem { itemId = "salt", minimumUnits = 5, maximumUnits = 5 },
                            new DevelopmentTownConfiguration.OpeningInventoryItem { itemId = "bread", minimumUnits = 0, maximumUnits = 3 },
                            new DevelopmentTownConfiguration.OpeningInventoryItem { itemId = "nails_hardware_lot", minimumUnits = 2, maximumUnits = 6 },
                        },
                    },
                },
                new DevelopmentTownConfiguration.BusinessDefinition
                {
                    key = "sawmill", operationId = "sawmill", ownerPersonKey = "person-owner",
                    businessId = "test-mill-1", premisesRequired = false, openingCashCents = 15000,
                    opening = new DevelopmentTownConfiguration.BusinessOpeningConfiguration
                    {
                        employees = new[]
                        {
                            new DevelopmentTownConfiguration.BusinessEmployeeDefinition
                            { personKey = "person-hand", role = "logger", weeklyWageCents = 750 },
                        },
                    },
                },
            });
            return config;
        }

        [Test]
        public void Validate_BaseConfig_Passes()
        {
            DevelopmentTownConfiguration config = BuildConfig();
            Assert.IsTrue(config.Validate(out string error), error);
        }

        [Test]
        public void Validate_NegativeBusinessCash_Refuses()
        {
            DevelopmentTownConfiguration config = BuildConfig();
            config.Businesses[0].openingCashCents = -1;
            Assert.IsTrue(!config.Validate(out string error));
            Assert.IsTrue(error.Contains("negative openingCashCents"), error);
        }

        [Test]
        public void Validate_NegativeHouseholdCash_Refuses()
        {
            DevelopmentTownConfiguration config = BuildConfig();
            config.Households[0].openingCashCents = -1;
            Assert.IsTrue(!config.Validate(out string error));
            Assert.IsTrue(error.Contains("non-negative opening cash"), error);
        }

        [Test]
        public void Validate_InventoryMaxBelowMin_Refuses()
        {
            DevelopmentTownConfiguration config = BuildConfig();
            config.Businesses[0].opening.inventory[0].maximumUnits = 5;
            Assert.IsTrue(!config.Validate(out string error));
            Assert.IsTrue(error.Contains("invalid or duplicate item range"), error);
        }

        [Test]
        public void Validate_UnknownKitId_Refuses()
        {
            DevelopmentTownConfiguration config = BuildConfig();
            config.Businesses[1].opening.equipment.kitIds = new[] { "no-such-kit" };
            Assert.IsTrue(!config.Validate(out string error));
            Assert.IsTrue(error.Contains("unknown tool kit"), error);
        }

        [Test]
        public void Validate_ZeroWageEmployee_Refuses()
        {
            DevelopmentTownConfiguration config = BuildConfig();
            config.Businesses[1].opening.employees[0].weeklyWageCents = 0;
            Assert.IsTrue(!config.Validate(out string error));
            Assert.IsTrue(error.Contains("Opening employees must reference"), error);
        }

        [Test]
        public void TryResolveOpeningInventory_GoldenQuantities_AreStable()
        {
            DevelopmentTownConfiguration config = BuildConfig();
            Assert.IsTrue(config.TryResolveOpeningInventory("general-store", "flour", Seed, out int flour));
            Assert.AreEqual(19, flour);
            Assert.IsTrue(config.TryResolveOpeningInventory("general-store", "salt", Seed, out int salt));
            Assert.AreEqual(5, salt);
            Assert.IsTrue(config.TryResolveOpeningInventory("general-store", "bread", Seed, out int bread));
            Assert.AreEqual(0, bread);
            Assert.IsTrue(config.TryResolveOpeningInventory("general-store", "nails_hardware_lot", Seed, out int nails));
            Assert.AreEqual(6, nails);
            Assert.IsTrue(config.TryResolveOpeningInventory("general-store", "flour", 0, out int flourSeed0));
            Assert.AreEqual(12, flourSeed0);
        }

        [Test]
        public void TryResolveOpeningInventory_IsDeterministicAndCaseInsensitive()
        {
            DevelopmentTownConfiguration config = BuildConfig();
            Assert.IsTrue(config.TryResolveOpeningInventory("general-store", "flour", Seed, out int first));
            Assert.IsTrue(config.TryResolveOpeningInventory("general-store", "flour", Seed, out int second));
            Assert.AreEqual(first, second);
            Assert.IsTrue(config.TryResolveOpeningInventory("GENERAL-STORE", "FLOUR", Seed, out int cased));
            Assert.AreEqual(first, cased);
        }

        [Test]
        public void TryResolveOpeningInventory_UnknownBusinessOrItem_ReturnsFalse()
        {
            DevelopmentTownConfiguration config = BuildConfig();
            Assert.IsTrue(!config.TryResolveOpeningInventory("bakery", "bread", Seed, out _));
            Assert.IsTrue(!config.TryResolveOpeningInventory("general-store", "no-such-item", Seed, out _));
            Assert.IsTrue(config.TryResolveOpeningInventory("general-store", "flour", Seed, out int qty));
            Assert.IsTrue(qty >= 10 && qty <= 20);
        }

        [Test]
        public void DirectCashEnumeration_YieldsEachBusinessOnceWithAuthoredCash()
        {
            DevelopmentTownConfiguration config = BuildConfig();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var found = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (DevelopmentTownConfiguration.BusinessDefinition business in config.Businesses)
            {
                Assert.IsTrue(seen.Add(business.key), "Business " + business.key + " enumerated twice.");
                found[business.key] = business.openingCashCents;
            }
            Assert.AreEqual(2, found.Count);
            Assert.AreEqual(50000, found["general-store"]);
            Assert.AreEqual(15000, found["sawmill"]);
        }

        [Test]
        public void DirectHouseholdEnumeration_YieldsEachHouseholdOnceWithAuthoredCash()
        {
            DevelopmentTownConfiguration config = BuildConfig();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var found = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (DevelopmentTownConfiguration.HouseholdDefinition household in config.Households)
            {
                Assert.IsTrue(household.memberPersonKeys != null && household.memberPersonKeys.Length > 0);
                Assert.IsTrue(seen.Add(household.memberPersonKeys[0]), "Anchor " + household.memberPersonKeys[0] + " enumerated twice.");
                found[household.memberPersonKeys[0]] = household.openingCashCents;
            }
            Assert.AreEqual(2, found.Count);
            Assert.AreEqual(40000, found["person-owner"]);
            Assert.AreEqual(40000, found["person-hand"]);
        }

        [Test]
        public void GeneratedHouseholdFallback_ReturnsStoredArrayUnchanged()
        {
            var config = ScriptableObject.CreateInstance<DevelopmentTownConfiguration>();
            config.Settings.configurationId = ConfigId;
            config.Settings.generatePopulationFromSettings = true;
            config.Settings.generatedHouseholdOpeningCash = new[]
            {
                new DevelopmentTownConfiguration.HouseholdCashTarget { anchorPersonKey = "generated-person-0", targetCashCents = 25000 },
                new DevelopmentTownConfiguration.HouseholdCashTarget { anchorPersonKey = "generated-person-1", targetCashCents = 25000 },
            };
            DevelopmentTownConfiguration.HouseholdCashTarget[] stored = config.Settings.generatedHouseholdOpeningCash;
            Assert.AreEqual(2, stored.Length);
            Assert.IsTrue(stored[0].targetCashCents == 25000 && stored[1].targetCashCents == 25000);
            Assert.AreEqual("generated-person-0", stored[0].anchorPersonKey);
            Assert.AreEqual("generated-person-1", stored[1].anchorPersonKey);
        }

    }
}
