// Generic Business Reconstruction, Pass 1 (new series), section 13.
// UNITY-ONLY integration tests. Do NOT run these in the Roslyn
// harness: they reference UnityEngine (ScriptableObject) and the
// full generic-business economy types, which the small harness
// cannot compile. Run in Unity Test Runner (EditMode) via Codex /
// Unity AI. Until that run happens, these tests are AUTHORED, NOT
// PASSED, and must be reported that way.
// What they cover, once run:
// - a BusinessInstanceState keeps ONE identity/ledger when a second
//   capability is added (Add Operation, not a second business);
// - legacy General Store profile behaviour is unchanged by the
//   additive ProductionMethodDefinition fields (defaults are empty
//   / zero, so a legacy method behaves exactly as before);
// - the new additive recipe fields round-trip on a method instance;
// - the generic capability registry still rejects duplicate ids
//   after this pass.
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Creation;
using LandLedgers.Economy.GenericBusiness;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Economy
{
    [TestFixture]
    public sealed class GenericBusinessUnityIntegrationTests
    {
        private static BusinessInstanceState BuildBusiness()
        {
            var profile = ScriptableObject.CreateInstance<BusinessProfileDefinition>();
            var business = BusinessInstanceState.Create(
                "genbiz_001", profile, -1, BusinessOwnerIdentity.Player());
            business.AddCapability("retail");
            return business;
        }

        [Test]
        public void AddBarberCapabilityKeepsSingleBusinessIdentity()
        {
            BusinessInstanceState business = BuildBusiness();
            string idBefore = business.InstanceId;
            business.AddCapability("barber");
            Assert.AreEqual(idBefore, business.InstanceId);
        }

        [Test]
        public void LegacyProductionMethodDefaultsAreUnchanged()
        {
            var method = new ProductionMethodDefinition
            {
                MethodId = "legacy.bread",
                OutputProductId = "bread",
                OutputQuantity = 24f,
            };
            Assert.AreEqual(0, method.ByproductOutputs.Count);
            Assert.AreEqual(0, method.FuelOrEnergyInputs.Count);
            Assert.AreEqual(0, method.InputSubstitutions.Count);
            Assert.AreEqual(0f, method.RequiredFloorAreaSqM);
            Assert.AreEqual(0f, method.WasteFraction01);
            Assert.AreEqual(0, method.FeasibilityConditions.Count);
        }

        [Test]
        public void AdditiveRecipeFieldsRoundTripOnMethod()
        {
            var method = new ProductionMethodDefinition
            {
                MethodId = "bakery.bread-batch",
                OutputProductId = "bread",
                OutputQuantity = 24f,
                ByproductOutputs = new List<ProductionInputRequirement>
                {
                    new ProductionInputRequirement { ProductId = "crumbs", Quantity = 0.5f },
                },
                FuelOrEnergyInputs = new List<ProductionInputRequirement>
                {
                    new ProductionInputRequirement { ProductId = "fuel", Quantity = 1f },
                },
                RequiredFloorAreaSqM = 20f,
                WasteFraction01 = 0.05f,
                FeasibilityConditions = new List<string> { "oven workstation present" },
            };
            Assert.AreEqual(1, method.ByproductOutputs.Count);
            Assert.AreEqual("fuel", method.FuelOrEnergyInputs[0].ProductId);
            Assert.AreEqual(20f, method.RequiredFloorAreaSqM);
            Assert.AreEqual(0.05f, method.WasteFraction01);
        }

        [Test]
        public void GenericCapabilitySetMatchesRegistryCapabilities()
        {
            var set = new GenericBusinessCapabilitySet();
            set.TryExpandRetailCatalog(new[] { "meat", "clothing" }, out _);
            set.TryEnableService("barber-haircut", out _);
            Assert.IsTrue(set.HasKind(BusinessCapabilityKind.SellGoods));
            Assert.IsTrue(set.HasKind(BusinessCapabilityKind.OfferServices));
            var registry = new BusinessCapabilityRegistry();
            var diagnostics = new List<string>();
            Assert.IsTrue(registry.Register(new BusinessCapability("retail", "Retail"), diagnostics));
            Assert.IsFalse(registry.Register(new BusinessCapability("retail", "Retail Again"), diagnostics));
        }
    }
}
