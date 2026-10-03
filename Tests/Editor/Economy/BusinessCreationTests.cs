using System.Collections.Generic;
using LandLedgers.Economy.Creation;
using LandLedgers.Primitives;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// BIZ-1: the Canon §3.1 creation workflow — intent validation, capability-driven
    /// premises (Tech X §3.2), ownership (Canon §3.5), HF-1 identity, and the
    /// creation≠operating rule (Canon §3.2, GHOST-DES-009).
    /// </summary>
    [TestFixture]
    public sealed class BusinessCreationTests
    {
        private sealed class TestContext : IBusinessCreationContext
        {
            public EntityIdRegistry IdRegistry { get; } = new EntityIdRegistry();
            public int PremisesAssignments;

            public bool TryGetProfile(BusinessType type, out BusinessProfileDefinition profile)
            {
                profile = null;
                return false; // Exercise the fallback path for every type.
            }

            public BusinessProfileDefinition BuildFallbackProfile(BusinessType type, string displayName)
            {
                return ScriptableObject.CreateInstance<BusinessProfileDefinition>();
            }

            public bool TryAssignPremises(PremisesKind kind, out int buildingId)
            {
                buildingId = 100 + PremisesAssignments;
                PremisesAssignments++;
                return true;
            }

            public void LogDiagnostic(string message)
            {
            }
        }

        private static CreateBusinessIntent ValidIntent(
            BusinessType type = BusinessType.Tailor,
            PremisesRequirement requirement = null,
            PremisesPreference preference = PremisesPreference.Auto)
        {
            return new CreateBusinessIntent(
                type,
                "Test Tailor Shop",
                BusinessOwnership.Sole(BusinessOwnerIdentity.Player(), 5000),
                requirement ?? new PremisesRequirement(),
                preference,
                5000);
        }

        [Test]
        public void TryCreate_ValidIntent_AllocatesBusinessEntityId()
        {
            var authority = new BusinessCreationAuthority();
            var context = new TestContext();

            bool ok = authority.TryCreate(ValidIntent(), context, out BusinessCreationResult result);

            Assert.IsTrue(ok, string.Join("; ", result.Diagnostics));
            Assert.IsTrue(result.Success);
            Assert.AreEqual(EntityKind.Business, result.BusinessEntityId.Kind);
            Assert.IsNotNull(result.Business);
        }

        [Test]
        public void TryCreate_AllocatesNeverReusedIds()
        {
            var authority = new BusinessCreationAuthority();
            var context = new TestContext();

            authority.TryCreate(ValidIntent(), context, out BusinessCreationResult first);
            authority.TryCreate(ValidIntent(BusinessType.Bakery), context, out BusinessCreationResult second);

            Assert.AreNotEqual(first.BusinessEntityId, second.BusinessEntityId);
        }

        [Test]
        public void TryCreate_InvalidIntent_FailsWithDiagnostics()
        {
            var authority = new BusinessCreationAuthority();
            var context = new TestContext();
            var bad = new CreateBusinessIntent(BusinessType.Saloon, "", null);

            bool ok = authority.TryCreate(bad, context, out BusinessCreationResult result);

            Assert.IsFalse(ok);
            Assert.IsFalse(result.Success);
            Assert.Greater(result.Diagnostics.Count, 0);
        }

        [Test]
        public void TryCreate_PartnershipMustTotal100Percent()
        {
            var authority = new BusinessCreationAuthority();
            var context = new TestContext();
            var ownership = new BusinessOwnership(new[]
            {
                new OwnershipShare(BusinessOwnerIdentity.Player(), 0.6f, 6000, 0.6f, true),
                new OwnershipShare(BusinessOwnerIdentity.Town(), 0.3f, 3000, 0.4f, false),
            });
            var intent = new CreateBusinessIntent(BusinessType.Saloon, "Test Saloon", ownership);

            bool ok = authority.TryCreate(intent, context, out BusinessCreationResult result);

            Assert.IsFalse(ok); // 90%, not 100% — Canon §3.5.
        }

        [Test]
        public void PremisesResolver_CustomerFacing_ResolvesStorefront()
        {
            var diagnostics = new List<string>();
            PremisesResolution resolution = PremisesResolver.Resolve(
                new PremisesRequirement(customerFacing: true),
                PremisesPreference.Auto, -1, diagnostics);

            Assert.AreEqual(PremisesKind.DedicatedStorefront, resolution.Kind);
        }

        [Test]
        public void PremisesResolver_AnimalHousing_OverrulesPreference()
        {
            var diagnostics = new List<string>();
            PremisesResolution resolution = PremisesResolver.Resolve(
                new PremisesRequirement(animalHousing: true),
                PremisesPreference.PreferHomeBased, -1, diagnostics);

            // Hard requirement wins over preference (Tech X §3.2).
            Assert.AreEqual(PremisesKind.BarnSpace, resolution.Kind);
        }

        [Test]
        public void PremisesResolver_NoRequirements_ResolvesNoDedicatedPremises()
        {
            var diagnostics = new List<string>();
            PremisesResolution resolution = PremisesResolver.Resolve(
                new PremisesRequirement(),
                PremisesPreference.Auto, -1, diagnostics);

            // Canon §3.1: no dedicated premises required is a valid outcome.
            Assert.AreEqual(PremisesKind.NoDedicatedPremises, resolution.Kind);
            Assert.AreEqual(-1, resolution.BuildingId);
        }

        [Test]
        public void PremisesResolver_MobilePreference_WithNoRequirements_ResolvesMobileRoute()
        {
            var diagnostics = new List<string>();
            PremisesResolution resolution = PremisesResolver.Resolve(
                new PremisesRequirement(),
                PremisesPreference.PreferMobile, -1, diagnostics);

            Assert.AreEqual(PremisesKind.MobileRoute, resolution.Kind);
        }

        [Test]
        public void Creation_DoesNotMarkOperating_OnlyCommerceDoes()
        {
            var authority = new BusinessCreationAuthority();
            var context = new TestContext();
            var ledger = new BusinessOperatingLedger();

            authority.TryCreate(ValidIntent(), context, out BusinessCreationResult result);

            // Canon §3.2: creation is not commerce.
            Assert.IsFalse(ledger.IsOperating(result.BusinessEntityId));

            ledger.RecordCommerce(result.BusinessEntityId, 12, "First sale to a real customer.");

            Assert.IsTrue(ledger.IsOperating(result.BusinessEntityId));
            Assert.AreEqual(1, ledger.OperatingBusinessCount);
        }

        [Test]
        public void OpeningRoster_Default_HasTenAuthoredEntries()
        {
            List<OpeningRosterEntry> roster = OpeningBusinessRoster.Default();

            Assert.AreEqual(10, roster.Count);
        }

        [Test]
        public void OpeningRoster_EnsureViaWorkflow_SkipsExistingTypes()
        {
            var authority = new BusinessCreationAuthority();
            var context = new TestContext();
            var diagnostics = new List<string>();
            var created = new List<BusinessCreationResult>();

            int count = OpeningBusinessRoster.EnsureViaFormationWorkflow(
                OpeningBusinessRoster.Default(), authority, context,
                type => type == BusinessType.GeneralStore, // already exists
                created.Add, diagnostics);

            Assert.AreEqual(9, count);
            Assert.AreEqual(9, created.Count);
        }
    }
}
