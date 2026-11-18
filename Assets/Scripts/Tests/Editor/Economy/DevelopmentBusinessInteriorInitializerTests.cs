using LandLedgers.Economy.Blacksmith;
using LandLedgers.Orchestration;
using LandLedgers.Population;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Economy
{
    public sealed class DevelopmentBusinessInteriorInitializerTests
    {
        [Test]
        public void FixtureValidation_RequiresRealVisualAndStableEquipmentIdentity()
        {
            var fixture = new DevelopmentTownConfiguration.BusinessInteriorFixture
            {
                assetId = "store-counter-01",
                kind = "counter",
                displayName = "Store counter",
                prefab = new GameObject("counter-prefab"),
                visualScaleMultiplier = Vector3.one,
                activityForwardLocal = Vector3.forward,
            };
            GameObject child = GameObject.CreatePrimitive(PrimitiveType.Cube);
            child.transform.SetParent(fixture.prefab.transform, false);
            try
            {
                Assert.IsNull(DevelopmentBusinessInteriorInitializer.ValidateFixture(fixture));
                fixture.assetId = string.Empty;
                StringAssert.Contains("asset ID", DevelopmentBusinessInteriorInitializer.ValidateFixture(fixture));
                fixture.assetId = "store-counter-01";
                fixture.visualScaleMultiplier = Vector3.zero;
                StringAssert.Contains("scale", DevelopmentBusinessInteriorInitializer.ValidateFixture(fixture));
            }
            finally
            {
                Object.DestroyImmediate(child);
                Object.DestroyImmediate(fixture.prefab);
            }
        }

        [Test]
        public void ExistingEquipmentMustBelongToTheConfiguredBusinessAndKind()
        {
            var asset = new EquipmentAsset
            {
                AssetId = "bakery-oven-01",
                Kind = "bake-oven",
                OwnerKind = "business",
                OwnerId = "bakery-instance",
            };

            Assert.IsNull(DevelopmentBusinessInteriorInitializer.ValidateRegisteredOwner(asset, "bakery-instance", "bake-oven"));
            StringAssert.Contains("owner", DevelopmentBusinessInteriorInitializer.ValidateRegisteredOwner(asset, "store-instance", "bake-oven"));
            StringAssert.Contains("kind", DevelopmentBusinessInteriorInitializer.ValidateRegisteredOwner(asset, "bakery-instance", "shelf"));
            Assert.IsNotNull(DevelopmentBusinessInteriorInitializer.ValidateRegisteredOwner(null, "bakery-instance", "bake-oven"));
        }

        [TestCase("general-store", "general_store")]
        [TestCase("fuel-dealer", "fuel_dealer")]
        [TestCase("lumber-yard", "lumber_yard")]
        [TestCase("bakery", "bakery")]
        public void ConfiguredOperationIdResolvesFormattingVariantOfRuntimeProfile(string operation, string profile)
        {
            Assert.IsTrue(DevelopmentBusinessInteriorInitializer.OperationMatchesRuntimeProfile(operation, profile));
        }

        [Test]
        public void ConfiguredOperationIdDoesNotResolveDifferentRuntimeProfile()
        {
            Assert.IsFalse(DevelopmentBusinessInteriorInitializer.OperationMatchesRuntimeProfile("general-store", "bakery"));
        }
    }
}
