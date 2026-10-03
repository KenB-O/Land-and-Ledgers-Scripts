using System.Collections.Generic;
using LandLedgers.Economy.Creation;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// BIZ-2: one business, many capabilities (Tech X §3.1); Add Operation vs
    /// Add Separate Business (GHOST-DES-033, Canon §3.4).
    /// </summary>
    [TestFixture]
    public sealed class BusinessCapabilitiesTests
    {
        private static BusinessCapabilityRegistry BuildRegistry()
        {
            var registry = new BusinessCapabilityRegistry();
            var diagnostics = new List<string>();
            registry.Register(new BusinessCapability("retail", "Retail",
                new[] { "tend_customer", "stock_shelves" },
                new PremisesRequirement(customerFacing: true, minAreaSqFt: 400)), diagnostics);
            registry.Register(new BusinessCapability("slaughter", "Slaughter",
                new[] { "slaughter_livestock" },
                new PremisesRequirement(foodHandling: true, yardStorage: true, minAreaSqFt: 800)), diagnostics);
            registry.Register(new BusinessCapability("lodging", "Lodging",
                new[] { "tend_guest" },
                new PremisesRequirement(customerFacing: true, minAreaSqFt: 1200)), diagnostics);
            return registry;
        }

        private static BusinessInstanceState BuildBusiness()
        {
            var profile = ScriptableObject.CreateInstance<BusinessProfileDefinition>();
            var business = BusinessInstanceState.Create(
                "test_001", profile, -1, BusinessOwnerIdentity.Player());
            business.AddCapability("retail");
            return business;
        }

        [Test]
        public void Registry_RejectsDuplicateCapabilityIds()
        {
            var registry = new BusinessCapabilityRegistry();
            var diagnostics = new List<string>();

            bool first = registry.Register(new BusinessCapability("retail", "Retail"), diagnostics);
            bool second = registry.Register(new BusinessCapability("retail", "Retail Again"), diagnostics);

            Assert.IsTrue(first);
            Assert.IsFalse(second);
            Assert.AreEqual(1, registry.Count);
        }

        [Test]
        public void TryAddOperation_AddsCapabilitiesToSameLedger()
        {
            BusinessInstanceState business = BuildBusiness();
            var registry = BuildRegistry();
            var diagnostics = new List<string>();

            bool ok = BusinessExpansion.TryAddOperation(
                business, new[] { "lodging" }, registry,
                PremisesKind.DedicatedStorefront, diagnostics);

            Assert.IsTrue(ok, string.Join("; ", diagnostics));
            Assert.IsTrue(business.HasCapability("retail"));
            Assert.IsTrue(business.HasCapability("lodging"));
        }

        [Test]
        public void TryAddOperation_PremisesMismatch_FailsLoudly()
        {
            BusinessInstanceState business = BuildBusiness();
            var registry = BuildRegistry();
            var diagnostics = new List<string>();

            // A storefront cannot take slaughter (yard storage + food handling at scale).
            bool ok = BusinessExpansion.TryAddOperation(
                business, new[] { "slaughter" }, registry,
                PremisesKind.DedicatedStorefront, diagnostics);

            Assert.IsFalse(ok);
            Assert.IsFalse(business.HasCapability("slaughter"));
            Assert.Greater(diagnostics.Count, 0);
        }

        [Test]
        public void TryAddOperation_UnknownCapability_Fails()
        {
            BusinessInstanceState business = BuildBusiness();
            var registry = BuildRegistry();
            var diagnostics = new List<string>();

            bool ok = BusinessExpansion.TryAddOperation(
                business, new[] { "teleportation" }, registry,
                PremisesKind.DedicatedStorefront, diagnostics);

            Assert.IsFalse(ok);
        }

        [Test]
        public void MergeSpaceRequirements_UnionsCapabilityNeeds()
        {
            var registry = BuildRegistry();

            PremisesRequirement merged =
                registry.MergeSpaceRequirements(new[] { "retail", "slaughter" });

            Assert.IsTrue(merged.NeedsCustomerFacingSpace);
            Assert.IsTrue(merged.NeedsYardStorage);
            Assert.IsTrue(merged.NeedsFoodHandling);
            Assert.AreEqual(800, merged.MinimumAreaSqFt);
        }

        [Test]
        public void BusinessInstance_CapabilityList_IsAdditiveAndCaseInsensitive()
        {
            BusinessInstanceState business = BuildBusiness();

            business.AddCapability("RETAIL"); // already present (different case)

            Assert.AreEqual(1, business.CapabilityIds.Count);
        }
    }
}
