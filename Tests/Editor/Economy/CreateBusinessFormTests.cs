using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Creation;
using LandLedgers.Primitives;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// BIZ-1 (addendum): the Create Business form model + presenter —
    /// GHOST-DES-029 UI flow, GHOST-DES-031 premises options, GHOST-DES-035 ownership,
    /// and the canon premises validation rules (Tech X §3.2).
    /// </summary>
    [TestFixture]
    public sealed class CreateBusinessFormTests
    {
        private sealed class FakeView : ICreateBusinessView
        {
            public CreateBusinessFormModel Form { get; } = new CreateBusinessFormModel();
            public IReadOnlyList<BusinessTypeOption> BusinessTypeOptions =>
                new List<BusinessTypeOption>();
            public IReadOnlyList<PropertyOption> OwnedPropertyOptions =>
                new List<PropertyOption>();
            public IReadOnlyList<CapabilityOption> CapabilityOptions =>
                new List<CapabilityOption>();

            public event Action Submitted;
            public event Action Cancelled;

            public List<string> ShownErrors;
            public BusinessCreationResult ShownSuccess;
            public List<string> ShownFailure;

            public void ShowErrors(IReadOnlyList<string> errors)
            {
                ShownErrors = new List<string>(errors);
            }

            public void ShowSuccess(BusinessCreationResult result)
            {
                ShownSuccess = result;
            }

            public void ShowWorkflowFailure(IReadOnlyList<string> diagnostics)
            {
                ShownFailure = new List<string>(diagnostics);
            }

            public void Close()
            {
            }

            public void RaiseSubmitted() => Submitted?.Invoke();
            public void RaiseCancelled() => Cancelled?.Invoke();
        }

        private sealed class TestContext : IBusinessCreationContext
        {
            public EntityIdRegistry IdRegistry { get; } = new EntityIdRegistry();

            public bool TryGetProfile(BusinessType type, out BusinessProfileDefinition profile)
            {
                profile = null;
                return false;
            }

            public BusinessProfileDefinition BuildFallbackProfile(BusinessType type, string displayName)
            {
                return ScriptableObject.CreateInstance<BusinessProfileDefinition>();
            }

            public bool TryAssignPremises(PremisesKind kind, out int buildingId)
            {
                buildingId = 7;
                return true;
            }

            public void LogDiagnostic(string message)
            {
            }
        }

        private static void FillValidForm(FakeView view)
        {
            view.Form.SetBusinessType(BusinessType.Butcher);
            view.Form.SetDisplayName("Test Butcher");
            view.Form.AddOwnerRow(new CreateBusinessOwnerRow());
            view.Form.AddCapability("butchery");
            view.Form.Premises.SetMode(PremisesMode.UseOwnedProperty);
            view.Form.Premises.SetSelectedPropertyId("plot-1");
            view.Form.SetWorkingCapitalCents(8000);
        }

        [Test]
        public void Validate_EmptyForm_ReportsAllProblems()
        {
            var form = new CreateBusinessFormModel();

            List<string> errors = form.Validate();

            Assert.Greater(errors.Count, 0);
        }

        [Test]
        public void Validate_SlaughterWithNoPremises_ExplainsUnmetNeeds()
        {
            var form = new CreateBusinessFormModel();
            form.SetDisplayName("Slaughterhouse");
            form.AddOwnerRow(new CreateBusinessOwnerRow());
            form.AddCapability("slaughter");
            form.Premises.SetMode(PremisesMode.NoPremisesRequired);

            List<string> errors = form.Validate();

            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains("yard space", errors[0].ToLowerInvariant());
        }

        [Test]
        public void Validate_OccupiedPropertyWithoutSpaceAssignment_Rejected()
        {
            var form = new CreateBusinessFormModel();
            form.SetDisplayName("Second Shop");
            form.AddOwnerRow(new CreateBusinessOwnerRow());
            form.AddCapability("retail");
            form.Premises.SetMode(PremisesMode.UseOwnedProperty);
            form.Premises.SetSelectedPropertyId("plot-9");
            form.Premises.SetPropertyAlreadyOccupied(true);
            // No functional space assignment.

            List<string> errors = form.Validate();

            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains("functional space", errors[0].ToLowerInvariant());
        }

        [Test]
        public void Validate_OccupiedPropertyWithSpaceAssignment_Accepted()
        {
            var form = new CreateBusinessFormModel();
            form.SetDisplayName("Second Shop");
            form.AddOwnerRow(new CreateBusinessOwnerRow());
            form.AddCapability("retail");
            form.Premises.SetMode(PremisesMode.UseOwnedProperty);
            form.Premises.SetSelectedPropertyId("plot-9");
            form.Premises.SetPropertyAlreadyOccupied(true);
            form.Premises.SetFunctionalSpaceAssignment("North rooms, 2nd floor");

            List<string> errors = form.Validate();

            Assert.AreEqual(0, errors.Count, string.Join("; ", errors));
        }

        [Test]
        public void Validate_AnimalHousingInHouseRoom_Rejected()
        {
            var form = new CreateBusinessFormModel();
            form.SetDisplayName("Livery");
            form.AddOwnerRow(new CreateBusinessOwnerRow());
            form.AddCapability("livery");
            form.Premises.SetMode(PremisesMode.UseExistingCompatibleSpace);
            form.Premises.SetCompatibleSpaceKind(CompatibleSpaceKind.HouseRoom);

            List<string> errors = form.Validate();

            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains("barn", errors[0].ToLowerInvariant());
        }

        [Test]
        public void Validate_UnknownCapability_Rejected()
        {
            var form = new CreateBusinessFormModel();
            form.SetDisplayName("Mystery Shop");
            form.AddOwnerRow(new CreateBusinessOwnerRow());
            form.AddCapability("teleportation");
            form.Premises.SetMode(PremisesMode.NoPremisesRequired);

            List<string> errors = form.Validate();

            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains("teleportation", errors[0]);
        }

        [Test]
        public void Presenter_InvalidForm_ShowsErrorsAndCreatesNothing()
        {
            var view = new FakeView();
            var context = new TestContext();
            using (new CreateBusinessPresenter(view, context))
            {
                view.RaiseSubmitted();

                Assert.IsNotNull(view.ShownErrors);
                Assert.Greater(view.ShownErrors.Count, 0);
                Assert.IsNull(view.ShownSuccess);
            }
        }

        [Test]
        public void Presenter_ValidForm_RunsWorkflowAndShowsSuccess()
        {
            var view = new FakeView();
            FillValidForm(view);
            var context = new TestContext();
            BusinessCreationResult created = null;
            using (var presenter = new CreateBusinessPresenter(view, context))
            {
                presenter.BusinessCreated += r => created = r;
                view.RaiseSubmitted();

                Assert.IsNull(view.ShownErrors);
                Assert.IsNotNull(view.ShownSuccess);
                Assert.IsTrue(view.ShownSuccess.Success);
                Assert.AreEqual(EntityKind.Business, view.ShownSuccess.BusinessEntityId.Kind);
                Assert.AreSame(created, view.ShownSuccess);
            }
        }

        [Test]
        public void FormModel_ToIntent_CarriesCapabilitiesAndCapital()
        {
            var view = new FakeView();
            FillValidForm(view);

            CreateBusinessIntent intent = view.Form.ToIntent();

            Assert.AreEqual(BusinessType.Butcher, intent.BusinessType);
            Assert.AreEqual("Test Butcher", intent.DisplayName);
            // 8000 working capital + 0 owner capital.
            Assert.AreEqual(8000, intent.StartingCapitalCents);
        }
    }
}
