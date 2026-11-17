using NUnit.Framework;
using LandLedgers.Economy;
using LandLedgers.Primitives;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// C1 (UBC 2026-11-17): the multi-capability audit found that a player-formed
    /// Generic business carried NO record of what it was set up to do. The setup
    /// template installed methods, policies and retail lines but left the
    /// business's canonical capability list empty, and the formation intent
    /// dropped the player's capability choices entirely (see the C1 report).
    /// These tests pin the template half of the repair: applying a setup template
    /// that installs a real vertical records that vertical on the business via
    /// AddCapability, using the template's own id; plans that install no
    /// operation record nothing; reapplying stays idempotent. The formation
    /// half (CreateBusinessIntent / TryCreate / ToIntent) lives in
    /// harness-excluded files and is verified by expanded build only, per the
    /// C1 report. Unity compilation, Test Runner and PlayMode are NOT claimed.
    /// </summary>
    [TestFixture]
    public sealed class C1TemplateCapabilityTests
    {
        private static BusinessInstanceState CreateGenericBusiness(string instanceId, string displayName)
        {
            BusinessProfileDefinition profile =
                BusinessProfileDefinition.CreateFallback(BusinessType.Generic, displayName);
            return BusinessInstanceState.Create(
                instanceId, profile, -1, BusinessOwnerIdentity.Npc(911, "Test Owner", "Owner"));
        }

        [Test]
        public void BakeryTemplate_RecordsBakeryCapabilityOnTheBusiness()
        {
            BusinessInstanceState business = CreateGenericBusiness("c1-test-bakery", "C1 Test Bakery");

            Assert.IsEmpty(business.CapabilityIds, "A freshly formed Generic business carries no capabilities yet.");
            Assert.IsTrue(
                GenericBusinessSetupTemplateCatalog.TryApply(
                    business, GenericBusinessSetupTemplateCatalog.BakeryId, out string message),
                message);

            Assert.IsTrue(business.HasCapability(GenericBusinessSetupTemplateCatalog.BakeryId),
                "Applying the bakery setup must record the installed vertical on the business's capability list.");
            Assert.AreEqual(1, business.CapabilityIds.Count,
                "One installed vertical records exactly one capability id.");
            // The install itself is unchanged: the bread method and retail line are still installed.
            Assert.IsTrue(business.GenericConfiguration.ProductionMethods.Count > 0,
                "The bakery template must still install its production method.");
            Assert.IsTrue(business.GenericConfiguration.Retail.TryGetLine("bread", out _),
                "The bakery template must still install its bread retail line.");
        }

        [Test]
        public void CarpentryTemplate_RecordsCarpentryCapabilityAndReapplyStaysIdempotent()
        {
            BusinessInstanceState business = CreateGenericBusiness("c1-test-carpentry", "C1 Test Carpentry");

            Assert.IsTrue(
                GenericBusinessSetupTemplateCatalog.TryApply(
                    business, GenericBusinessSetupTemplateCatalog.CarpentryId, out string message),
                message);
            Assert.IsTrue(business.HasCapability(GenericBusinessSetupTemplateCatalog.CarpentryId));
            Assert.AreEqual(1, business.CapabilityIds.Count);

            Assert.IsTrue(
                GenericBusinessSetupTemplateCatalog.TryApply(
                    business, GenericBusinessSetupTemplateCatalog.CarpentryId, out _),
                "Reapplying a preset should update the same business, not duplicate it.");
            Assert.AreEqual(1, business.CapabilityIds.Count,
                "Reapplying a template must not duplicate the recorded capability.");
        }

        [Test]
        public void SmithyTemplate_RecordsSmithyCapabilityOnTheBusiness()
        {
            BusinessInstanceState business = CreateGenericBusiness("c1-test-smithy", "C1 Test Smithy");

            Assert.IsTrue(
                GenericBusinessSetupTemplateCatalog.TryApply(
                    business, GenericBusinessSetupTemplateCatalog.SmithyId, out string message),
                message);

            Assert.IsTrue(business.HasCapability(GenericBusinessSetupTemplateCatalog.SmithyId),
                "Applying the smithy setup must record the installed vertical on the business's capability list.");
            Assert.AreEqual(1, business.CapabilityIds.Count);
        }

        [Test]
        public void GeneralMerchandisePreset_RecordsNoCapability()
        {
            BusinessInstanceState business = CreateGenericBusiness("c1-test-merchandise", "C1 Test Merchandise");

            Assert.IsTrue(
                GenericBusinessSetupTemplateCatalog.TryApply(
                    business, GenericBusinessSetupTemplateCatalog.GeneralMerchandiseId, out string message),
                message);

            Assert.IsEmpty(business.CapabilityIds,
                "The merchandise preset is a disabled-catalog plan, not an installed operation, so it records no capability.");
            Assert.AreEqual(27, business.GenericConfiguration.Retail.ProductLines.Count,
                "The preset must still install its authored catalog assortment.");
        }

        [Test]
        public void BlankTemplate_RecordsNoCapability()
        {
            BusinessInstanceState business = CreateGenericBusiness("c1-test-blank", "C1 Test Blank");

            Assert.IsTrue(
                GenericBusinessSetupTemplateCatalog.TryApply(
                    business, GenericBusinessSetupTemplateCatalog.BlankId, out string message),
                message);

            Assert.IsEmpty(business.CapabilityIds,
                "A blank setup installs no activity, so it records no capability.");
        }

        [Test]
        public void UnknownTemplate_RecordsNoCapability()
        {
            BusinessInstanceState business = CreateGenericBusiness("c1-test-unknown", "C1 Test Unknown");

            Assert.IsFalse(GenericBusinessSetupTemplateCatalog.TryApply(business, "missing-preset", out _));
            Assert.IsEmpty(business.CapabilityIds,
                "A refused template must not record a capability.");
        }

        [Test]
        public void TwoVerticals_AccumulateOnTheSameBusinessIdentity()
        {
            BusinessInstanceState business = CreateGenericBusiness("c1-test-two-verticals", "C1 Test Two Verticals");

            Assert.IsTrue(GenericBusinessSetupTemplateCatalog.TryApply(
                business, GenericBusinessSetupTemplateCatalog.BakeryId, out _));
            Assert.IsTrue(GenericBusinessSetupTemplateCatalog.TryApply(
                business, GenericBusinessSetupTemplateCatalog.CarpentryId, out _));

            Assert.AreEqual(2, business.CapabilityIds.Count,
                "Two installed verticals on one business record two capabilities on the one identity — no second business is created.");
            Assert.IsTrue(business.HasCapability(GenericBusinessSetupTemplateCatalog.BakeryId));
            Assert.IsTrue(business.HasCapability(GenericBusinessSetupTemplateCatalog.CarpentryId));
        }
    }
}
