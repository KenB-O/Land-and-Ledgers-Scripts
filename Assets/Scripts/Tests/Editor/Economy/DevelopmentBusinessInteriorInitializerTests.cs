using System;
using System.Reflection;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.FirstLedger;
using LandLedgers.Orchestration;
using LandLedgers.Population;
using LandLedgers.World;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

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

        [Test]
        public void WorldVisualRecoverySubscription_WaitsUntilBootstrapHasInitialized()
        {
            GameObject worldObject = new GameObject("world");
            worldObject.SetActive(false);
            TownWorldController world = worldObject.AddComponent<TownWorldController>();

            GameObject bootstrapObject = new GameObject("bootstrapper");
            bootstrapObject.SetActive(false);
            FirstLedgerSliceBootstrapper bootstrapper = bootstrapObject.AddComponent<FirstLedgerSliceBootstrapper>();
            SetPrivateField(bootstrapper, "townWorld", world);
            MethodInfo onEnable = typeof(FirstLedgerSliceBootstrapper).GetMethod(
                "OnEnable", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo initialized = typeof(FirstLedgerSliceBootstrapper).GetField(
                "initialized", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo listeners = typeof(TownWorldController).GetField(
                "WorldVisualsRebuilt", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(onEnable);
            Assert.IsNotNull(initialized);
            Assert.IsNotNull(listeners);
            try
            {
                onEnable.Invoke(bootstrapper, null);
                Assert.IsNull(listeners.GetValue(world),
                    "the initial world build may fire before the development business roster exists");

                initialized.SetValue(bootstrapper, true);
                onEnable.Invoke(bootstrapper, null);
                Delegate subscribed = listeners.GetValue(world) as Delegate;
                Assert.IsNotNull(subscribed,
                    "re-enabling after startup must restore the world-visual recovery listener");
                Assert.IsTrue(System.Array.Exists(subscribed.GetInvocationList(),
                    handler => handler.Target == bootstrapper
                        && handler.Method.Name == "RestoreRuntimePresentationsAfterWorldRebuild"));
            }
            finally
            {
                MethodInfo onDisable = typeof(FirstLedgerSliceBootstrapper).GetMethod(
                    "OnDisable", BindingFlags.Instance | BindingFlags.NonPublic);
                onDisable?.Invoke(bootstrapper, null);
                Object.DestroyImmediate(bootstrapObject);
                Object.DestroyImmediate(worldObject);
            }
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Expected private field '" + fieldName + "'.");
            field.SetValue(target, value);
        }
    }
}
