using System;
using System.Reflection;
using LandLedgers.Population;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Population
{
    [TestFixture]
    public sealed class DevelopmentTownConfigurationValidationTests
    {
        [Test]
        public void ValidDirectConfigurationPassesBusinessAndRosterValidation()
        {
            DevelopmentTownConfiguration configuration = CreateConfiguration(
                Business("freight-company", "livery-freight", "kennedy"));
            try
            {
                Assert.IsTrue(configuration.Validate(out string error), error);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(configuration);
            }
        }

        [Test]
        public void InvalidBusinessOwnersIdentifyEveryBusinessAndMissingOwnerField()
        {
            DevelopmentTownConfiguration configuration = CreateConfiguration(
                Business("freight-company", "livery-freight", "kennedy"),
                Business("sawmill", "sawmill", "enzo-kwindt"),
                Business("carpentry-workshop", "carpentry-workshop", "don-millward"));
            try
            {
                Assert.IsFalse(configuration.Validate(out string error));
                StringAssert.Contains("business 'sawmill' ownerPersonKey 'enzo-kwindt'", error);
                StringAssert.Contains("business 'carpentry-workshop' ownerPersonKey 'don-millward'", error);
                StringAssert.Contains("does not match a configured person key", error);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(configuration);
            }
        }

        private static DevelopmentTownConfiguration CreateConfiguration(params DevelopmentTownConfiguration.BusinessDefinition[] businesses)
        {
            DevelopmentTownConfiguration configuration = ScriptableObject.CreateInstance<DevelopmentTownConfiguration>();
            typeof(DevelopmentTownConfiguration).GetField("general", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(configuration, new DevelopmentTownConfiguration.GeneralSettings
                {
                    configurationId = "business-validation-regression",
                    freightBusinessKey = "freight-company"
                });
            Set(configuration, "people", new[]
            {
                new DevelopmentTownConfiguration.PersonDefinition
                {
                    key = "kennedy", firstName = "Kennedy", lastName = "Baldwin-Ooms", age = 35,
                    occupation = "Unemployed", homeBuildingOrdinal = 0
                }
            });
            Set(configuration, "households", new[]
            {
                new DevelopmentTownConfiguration.HouseholdDefinition
                {
                    key = "baldwin-ooms", displayName = "Baldwin-Ooms Household", surname = "Baldwin-Ooms",
                    memberPersonKeys = new[] { "kennedy" }, homeBuildingOrdinal = 0
                }
            });
            Set(configuration, "relationships", Array.Empty<DevelopmentTownConfiguration.RelationshipDefinition>());
            Set(configuration, "businesses", businesses);
            return configuration;
        }

        private static DevelopmentTownConfiguration.BusinessDefinition Business(string key, string operation, string owner)
        {
            var business = new DevelopmentTownConfiguration.BusinessDefinition
            { key = key, operationId = operation, ownerPersonKey = owner, openingCashCents = 0 };
            if (operation == "livery-freight")
            {
                business.opening.openingHorseId = "test-horse";
                business.opening.openingWagonId = "test-wagon";
            }
            return business;
        }

        private static void Set(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Expected serialized field '{fieldName}'.");
            field.SetValue(target, value);
        }
    }
}
