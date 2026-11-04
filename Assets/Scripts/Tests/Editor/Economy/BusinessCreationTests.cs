using System.Collections.Generic;
using LandLedgers.Economy;
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
        private sealed class TestContext : IBusinessCreationContext, IBusinessTypedPremisesContext
        {
            public EntityIdRegistry IdRegistry { get; } = new EntityIdRegistry();
            public int PremisesAssignments;
            public BusinessType LastPremisesBusinessType;
            public PremisesKind LastPremisesKind;
            public bool PremisesAvailable = true;

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
                if (!PremisesAvailable)
                {
                    buildingId = -1;
                    return false;
                }

                buildingId = 100 + PremisesAssignments;
                PremisesAssignments++;
                return true;
            }

            public bool TryAssignPremises(BusinessType businessType, PremisesKind kind, out int buildingId)
            {
                LastPremisesBusinessType = businessType;
                LastPremisesKind = kind;
                return TryAssignPremises(kind, out buildingId);
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
            Assert.AreEqual(0, result.Business.RuntimeState.CurrentCashCents,
                "A positive capital term cannot turn into cash without a settled owner debit.");
            StringAssert.Contains("remains unsettled", string.Join(" ", result.Diagnostics));
        }

        [Test]
        public void TryCreate_UsesBusinessTypeAwarePremisesContext()
        {
            var authority = new BusinessCreationAuthority();
            var context = new TestContext();
            var intent = ValidIntent(
                BusinessType.CropFarm,
                new PremisesRequirement(yardStorage: true));

            bool created = authority.TryCreate(intent, context, out BusinessCreationResult result);

            Assert.IsTrue(created, string.Join("; ", result.Diagnostics));
            Assert.AreEqual(BusinessType.CropFarm, context.LastPremisesBusinessType);
            Assert.AreEqual(PremisesKind.YardShopArea, context.LastPremisesKind);
        }

        [Test]
        public void TryCreate_SiteUnavailable_EstablishesEntityWithoutGrantingPremisesOrAssets()
        {
            var authority = new BusinessCreationAuthority();
            var context = new TestContext { PremisesAvailable = false };
            var intent = ValidIntent(
                BusinessType.Bakery,
                new PremisesRequirement(foodHandling: true, minAreaSqFt: 400));

            bool created = authority.TryCreate(intent, context, out BusinessCreationResult result);

            Assert.IsTrue(created, string.Join("; ", result.Diagnostics));
            Assert.IsTrue(result.Success);
            Assert.IsNotNull(result.Business);
            Assert.AreEqual(-1, result.Premises.BuildingId);
            Assert.AreEqual(0, result.Business.RuntimeState.CurrentCashCents);
            Assert.AreEqual(0, result.Business.RuntimeState.RetailItemLots.Count,
                "Establishment must not grant inventory lots.");
            foreach (CategoryStockState category in result.Business.RuntimeState.CategoryStock)
            {
                Assert.AreEqual(0, category.CurrentStockUnits,
                    $"Establishment must not grant stock in {category.CategoryId}.");
            }
            foreach (WorkerSlotState slot in result.Business.RuntimeState.WorkerSlots)
            {
                Assert.IsFalse(slot.IsFilled, "Establishment must not grant workers.");
            }
            Assert.AreEqual(0, context.PremisesAssignments);
            StringAssert.Contains("Established without a", string.Join(" ", result.Diagnostics));
            Assert.IsFalse(new BusinessOperatingLedger().IsOperating(result.BusinessEntityId));
        }

        [Test]
        public void TryCreate_ExplicitDeferredPremises_DoesNotAttemptToClaimASite()
        {
            var authority = new BusinessCreationAuthority();
            var context = new TestContext();
            var intent = ValidIntent(
                BusinessType.LiveryFreight,
                new PremisesRequirement(animalHousing: true, yardStorage: true),
                PremisesPreference.NoneRequired);

            bool created = authority.TryCreate(intent, context, out BusinessCreationResult result);

            Assert.IsTrue(created, string.Join("; ", result.Diagnostics));
            Assert.AreEqual(PremisesKind.NoDedicatedPremises, result.Premises.Kind);
            Assert.AreEqual(-1, result.Premises.BuildingId);
            Assert.AreEqual(0, context.PremisesAssignments);
            Assert.AreEqual(0, result.Business.RuntimeState.CurrentCashCents);
        }

        [Test]
        public void NpcAdapter_UsesConfiguredTypedPremisesSelectorWithoutUntypedFallback()
        {
            var adapter = new NpcBusinessCreationAdapter(new EntityIdRegistry());
            bool untypedCalled = false;
            BusinessType selectedType = BusinessType.Generic;
            PremisesKind selectedKind = PremisesKind.Unspecified;
            adapter.PremisesAssigner = kind =>
            {
                untypedCalled = true;
                return 11;
            };
            adapter.TypedPremisesAssigner = (businessType, kind) =>
            {
                selectedType = businessType;
                selectedKind = kind;
                return 22;
            };

            bool assigned = ((IBusinessTypedPremisesContext)adapter).TryAssignPremises(
                BusinessType.Ranch,
                PremisesKind.BarnSpace,
                out int buildingId);

            Assert.IsTrue(assigned);
            Assert.AreEqual(22, buildingId);
            Assert.AreEqual(BusinessType.Ranch, selectedType);
            Assert.AreEqual(PremisesKind.BarnSpace, selectedKind);
            Assert.IsFalse(untypedCalled, "The generic fallback must not override a configured typed site authority.");
        }

        [Test]
        public void NpcAdapter_RefusesWhenOnlyLegacyUntypedPremisesSelectorExists()
        {
            var adapter = new NpcBusinessCreationAdapter(new EntityIdRegistry());
            bool untypedCalled = false;
            adapter.PremisesAssigner = kind =>
            {
                untypedCalled = true;
                return 11;
            };

            bool assigned = ((IBusinessTypedPremisesContext)adapter).TryAssignPremises(
                BusinessType.CropFarm,
                PremisesKind.YardShopArea,
                out int buildingId);

            Assert.IsFalse(assigned,
                "A typed formation must refuse when only a business-unaware selector is wired.");
            Assert.AreEqual(-1, buildingId);
            Assert.IsFalse(untypedCalled);
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
        public void NewBusinessProfile_DoesNotGrantCatalogCashOrInventory()
        {
            BusinessProfileDefinition profile = Resources.Load<BusinessProfileDefinition>(
                "Core/Economy/BusinessProfiles/GeneralStoreProfile");
            Assert.NotNull(profile, "The authored General Store profile is the production creation template.");
            Assert.Greater(profile.Business.Economy.StartingCashCents, 0,
                "This fixture protects against accidentally treating authored profile defaults as transactions.");

            BusinessRuntimeState runtime = profile.CreateRuntimeState();

            Assert.AreEqual(0, runtime.CurrentCashCents,
                "A profile is descriptive data and must not create business cash without a settled source.");
            Assert.Greater(runtime.CategoryStock.Count, 0,
                "The store keeps its authored category targets even when it has no physical opening stock.");
            foreach (CategoryStockState stock in runtime.CategoryStock)
            {
                Assert.AreEqual(0, stock.CurrentStockUnits,
                    $"Catalog category {stock.CategoryId} cannot become inventory without a receipt or production event.");
            }
        }

        [Test]
        public void OpeningRoster_Default_HasTenAuthoredEntries()
        {
            List<OpeningRosterEntry> roster = OpeningBusinessRoster.Default();

            Assert.AreEqual(10, roster.Count);
        }

        [Test]
        public void OpeningRoster_CompactPlaytest_IsThreeDistinctBusinesses()
        {
            List<OpeningRosterEntry> roster = OpeningBusinessRoster.CompactPlaytest();

            Assert.AreEqual(3, roster.Count);
            CollectionAssert.AreEquivalent(
                new[] { BusinessType.GeneralStore, BusinessType.Blacksmith, BusinessType.LiveryFreight },
                new[] { roster[0].BusinessType, roster[1].BusinessType, roster[2].BusinessType });
            Assert.Greater(roster[0].StartingCapitalCents, 0);
            Assert.Greater(roster[1].StartingCapitalCents, 0);
            Assert.Greater(roster[2].StartingCapitalCents, 0);
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
