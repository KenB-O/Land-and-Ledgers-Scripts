using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Primitives;
using LandLedgers.Reporting;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Core
{
    /// <summary>
    /// PKG-2 (PL-04 + SD-05): TECH §4.1 work-relationship taxonomy, family-labor no-payroll
    /// guard, legacy slot classification bridge, and the derived staffing read model.
    /// </summary>
    [TestFixture]
    public sealed class WorkRelationshipTaxonomyTests
    {
        [Test]
        public void Taxonomy_HasAllFiveCanonKinds()
        {
            Assert.AreEqual(1, (int)WorkRelationshipKind.FarmOrBusinessOperator);
            Assert.AreEqual(2, (int)WorkRelationshipKind.FamilyEnterpriseLabor);
            Assert.AreEqual(3, (int)WorkRelationshipKind.PermanentEmployment);
            Assert.AreEqual(4, (int)WorkRelationshipKind.SeasonalOrCasualEmployment);
            Assert.AreEqual(5, (int)WorkRelationshipKind.ContractOrServiceWork);
        }

        [Test]
        public void OnlyWageKinds_CarryPayrollObligation()
        {
            Assert.IsTrue(new WorkRelationship { Kind = WorkRelationshipKind.PermanentEmployment }.CarriesPayrollObligation);
            Assert.IsTrue(new WorkRelationship { Kind = WorkRelationshipKind.SeasonalOrCasualEmployment }.CarriesPayrollObligation);
            Assert.IsFalse(new WorkRelationship { Kind = WorkRelationshipKind.FarmOrBusinessOperator }.CarriesPayrollObligation);
            Assert.IsFalse(new WorkRelationship { Kind = WorkRelationshipKind.FamilyEnterpriseLabor }.CarriesPayrollObligation);
            Assert.IsFalse(new WorkRelationship { Kind = WorkRelationshipKind.ContractOrServiceWork }.CarriesPayrollObligation);
            Assert.IsFalse(new WorkRelationship { Kind = WorkRelationshipKind.None }.CarriesPayrollObligation);
        }

        [Test]
        public void Guard_FlagsWageTermsOnFamilyLabor_Operator_AndContract()
        {
            Assert.IsNotNull(WorkRelationshipGuard.ValidateNoWageForNonWageKind(WorkRelationshipKind.FamilyEnterpriseLabor, 1200));
            Assert.IsNotNull(WorkRelationshipGuard.ValidateNoWageForNonWageKind(WorkRelationshipKind.FarmOrBusinessOperator, 1200));
            Assert.IsNotNull(WorkRelationshipGuard.ValidateNoWageForNonWageKind(WorkRelationshipKind.ContractOrServiceWork, 1200));
        }

        [Test]
        public void Guard_PassesWageEmploymentAndZeroWage()
        {
            Assert.IsNull(WorkRelationshipGuard.ValidateNoWageForNonWageKind(WorkRelationshipKind.PermanentEmployment, 1200));
            Assert.IsNull(WorkRelationshipGuard.ValidateNoWageForNonWageKind(WorkRelationshipKind.SeasonalOrCasualEmployment, 1200));
            Assert.IsNull(WorkRelationshipGuard.ValidateNoWageForNonWageKind(WorkRelationshipKind.FamilyEnterpriseLabor, 0));
        }

        [Test]
        public void Projection_FilledSlot_ClassifiesAsWageEmployment()
        {
            var slot = new WorkerSlotState();
            slot.Assign("7", "Ada Example", 1200);

            WorkRelationship relationship = WorkRelationshipProjection.FromWorkerSlotAssignment("biz-1", slot);

            Assert.AreEqual(WorkRelationshipKind.PermanentEmployment, relationship.Kind);
            Assert.AreEqual(7, relationship.PersonId);
            Assert.AreEqual(WorkRelationshipCounterpartyKind.Business, relationship.Counterparty.Kind);
            Assert.AreEqual("biz-1", relationship.Counterparty.StableId);
            Assert.IsTrue(relationship.IsWageEmployment);
            Assert.IsNotNull(relationship.Notes);
        }

        [Test]
        public void Projection_UnfilledSlot_ClassifiesAsNone()
        {
            var slot = new WorkerSlotState("clerk", "Clerk", 1000, false);

            WorkRelationship relationship = WorkRelationshipProjection.FromWorkerSlotAssignment("biz-1", slot);

            Assert.AreEqual(WorkRelationshipKind.None, relationship.Kind);
            Assert.IsFalse(relationship.IsWageEmployment);
        }

        [Test]
        public void Projection_FamilyLabor_CarriesNoPayroll()
        {
            WorkRelationship relationship = WorkRelationshipProjection.ForFamilyEnterpriseLabor(
                personId: 7, householdId: 3, businessId: "biz-1", roleDisplayName: "Harvest help", startDayIndex: 10);

            Assert.AreEqual(WorkRelationshipKind.FamilyEnterpriseLabor, relationship.Kind);
            Assert.IsFalse(relationship.CarriesPayrollObligation);
            Assert.IsNull(WorkRelationshipGuard.ValidateNoWageForNonWageKind(relationship.Kind, 0));
        }

        [Test]
        public void ReadModel_DerivesHeadcountFromAssignments_NotSlotDefinitions()
        {
            var filledPaid = new WorkerSlotState();
            filledPaid.Assign("7", "Ada", 1200);

            var filledSuspended = new WorkerSlotState();
            filledSuspended.Assign("8", "Bob", 1100);
            filledSuspended.SuspendForMissedPayroll();

            var vacant = new WorkerSlotState("clerk", "Clerk", 1000, false);

            var slots = new List<WorkerSlotState> { filledPaid, filledSuspended, vacant };
            WorkforceStaffingReadModel model = WorkforceStaffingReadModel.FromAssignments("biz-1", slots);

            Assert.AreEqual(2, model.FilledHeadcount);
            Assert.AreEqual(1, model.PaidActiveHeadcount);
            Assert.AreEqual(1, model.SuspendedHeadcount);
            Assert.AreEqual(1, model.VacantPositions);
            Assert.AreEqual(2, model.WageEmploymentHeadcount);
        }

        [Test]
        public void ReadModel_VacantSlots_NeverFabricateWorkers()
        {
            var slots = new List<WorkerSlotState>
            {
                new WorkerSlotState("a", "A", 1000, true),
                new WorkerSlotState("b", "B", 1000, false),
            };

            WorkforceStaffingReadModel model = WorkforceStaffingReadModel.FromAssignments("biz-1", slots);

            Assert.AreEqual(0, model.FilledHeadcount);
            Assert.AreEqual(0, model.WageEmploymentHeadcount);
            Assert.AreEqual(2, model.VacantPositions);
        }

        [Test]
        public void ReadModel_SupplementaryFamilyLabor_CountsAsWorker_NotPayroll()
        {
            var slots = new List<WorkerSlotState>();
            var supplementary = new List<WorkRelationship>
            {
                WorkRelationshipProjection.ForFamilyEnterpriseLabor(7, 3, "biz-1", "Harvest help", 10),
            };

            WorkforceStaffingReadModel model = WorkforceStaffingReadModel.FromAssignments("biz-1", slots, supplementary);

            Assert.AreEqual(1, model.FilledHeadcount);
            Assert.AreEqual(0, model.WageEmploymentHeadcount);
        }
    }
}
