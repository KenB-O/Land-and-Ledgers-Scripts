using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// PKG-4 (PL-51, Phase-0): TECH Part VII regression fixture "FAMILY LABOR != EMPLOYEE".
    /// Spouse/adult child performs farm work: task time/capability/output update without a
    /// wage EmploymentRelationship unless one actually exists. Locks the PL-04 doctrine
    /// against future tuning regressions.
    /// </summary>
    [TestFixture]
    public sealed class FamilyLaborNotEmployeeFixtureTests
    {
        [Test]
        public void FamilyLaborRelationship_CarriesNoPayrollObligation()
        {
            WorkRelationship relationship = WorkRelationshipProjection.ForFamilyEnterpriseLabor(
                personId: 7, householdId: 3, businessId: "farm-1", roleDisplayName: "Harvest help", startDayIndex: 42);

            Assert.AreEqual(WorkRelationshipKind.FamilyEnterpriseLabor, relationship.Kind);
            Assert.IsFalse(relationship.IsWageEmployment);
            Assert.IsFalse(relationship.CarriesPayrollObligation);
        }

        [Test]
        public void FamilyLabor_WithAnyWageTerms_IsRejectedByGuard()
        {
            // Even one cent of agreed wage on a family-labor relationship is a doctrine violation.
            Assert.IsNotNull(WorkRelationshipGuard.ValidateNoWageForNonWageKind(WorkRelationshipKind.FamilyEnterpriseLabor, 1));
            Assert.IsNotNull(WorkRelationshipGuard.ValidateNoWageForNonWageKind(WorkRelationshipKind.FamilyEnterpriseLabor, 1200));
        }

        [Test]
        public void FamilyLabor_IsNeverRecordedThroughWorkerSlotAssignment()
        {
            // WorkerSlot.Assign is wage employment by construction (slot wage drives payroll).
            // Family labor must use the taxonomy's non-wage kind, never a slot assignment.
            var slot = new WorkerSlotState();
            slot.Assign("7", "Ada Example", 1200);

            WorkRelationship projected = WorkRelationshipProjection.FromWorkerSlotAssignment("farm-1", slot);

            Assert.AreNotEqual(WorkRelationshipKind.FamilyEnterpriseLabor, projected.Kind);
            Assert.AreEqual(WorkRelationshipKind.PermanentEmployment, projected.Kind);
        }

        [Test]
        public void FamilyLabor_AlongsideWageEmployment_DoesNotCreatePayrollEntries()
        {
            var relationships = new List<WorkRelationship>
            {
                WorkRelationshipProjection.ForFamilyEnterpriseLabor(7, 3, "farm-1", "Harvest help", 42),
                new WorkRelationship
                {
                    Kind = WorkRelationshipKind.PermanentEmployment,
                    PersonId = 8,
                    Counterparty = WorkRelationshipCounterparty.ForBusiness("farm-1"),
                    RoleDisplayName = "Hired hand",
                    StartDayIndex = 40,
                    EndDayIndex = -1,
                },
            };

            int payrollObligations = 0;
            for (int i = 0; i < relationships.Count; i++)
            {
                if (relationships[i].CarriesPayrollObligation)
                {
                    payrollObligations++;
                }
            }

            // Only the genuinely hired hand carries a payroll obligation.
            Assert.AreEqual(1, payrollObligations);
        }
    }
}
