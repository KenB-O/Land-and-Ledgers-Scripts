using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Primitives;

namespace LandLedgers.Reporting
{
    /// <summary>
    /// TECH §4.3 / SD-05: staffing counts are a DERIVED READ MODEL, never a causal authority.
    /// Headcount is computed from actual worker assignments (who is really attached to the
    /// business), never from slot definitions or template counts. This read model must not
    /// fabricate Persons, and must not be used to suppress seasonal hiring: vacant positions
    /// are reported as vacancies, not as a cap on who may be engaged.
    /// Read-only: derives from domain state, never mutates it (per DEPENDENCY_RULES.md).
    /// Longer term this derives from executed/planned worker-time (LaborDemandPlan, P1);
    /// until then it derives from assignment records, which is still actual-worker truth
    /// rather than slot-definition truth.
    /// </summary>
    public sealed class WorkforceStaffingReadModel
    {
        public string BusinessId { get; private set; } = string.Empty;

        /// <summary>Workers actually assigned (filled slots). Never includes vacant positions.</summary>
        public int FilledHeadcount { get; private set; }

        /// <summary>Assigned workers currently paid-active.</summary>
        public int PaidActiveHeadcount { get; private set; }

        /// <summary>Assigned workers suspended for missed payroll (still attached, not active labor).</summary>
        public int SuspendedHeadcount { get; private set; }

        /// <summary>
        /// Open positions. A vacancy count only - it does not fabricate workers and does not
        /// cap hiring (TECH §4.3: slots must not suppress seasonal hiring).
        /// </summary>
        public int VacantPositions { get; private set; }

        /// <summary>Headcount classified as wage employment via the WorkRelationship taxonomy.</summary>
        public int WageEmploymentHeadcount { get; private set; }

        /// <summary>
        /// Builds the read model from actual slot assignments. Pass supplementary classified
        /// relationships (operator, family labor, contracts) to include non-slot work in the
        /// totals; slot assignments are classified via <see cref="WorkRelationshipProjection"/>.
        /// </summary>
        public static WorkforceStaffingReadModel FromAssignments(
            string businessId,
            IReadOnlyList<WorkerSlotState> slots,
            IReadOnlyList<WorkRelationship> supplementaryRelationships = null)
        {
            var model = new WorkforceStaffingReadModel
            {
                BusinessId = businessId ?? string.Empty,
            };

            if (slots != null)
            {
                for (int i = 0; i < slots.Count; i++)
                {
                    WorkerSlotState slot = slots[i];
                    if (slot == null || !slot.IsFilled)
                    {
                        model.VacantPositions++;
                        continue;
                    }

                    model.FilledHeadcount++;
                    if (slot.SuspendedForMissedPayroll)
                    {
                        model.SuspendedHeadcount++;
                    }
                    else if (slot.IsPaidActive)
                    {
                        model.PaidActiveHeadcount++;
                    }

                    WorkRelationship projected = WorkRelationshipProjection.FromWorkerSlotAssignment(businessId, slot);
                    if (projected.IsWageEmployment)
                    {
                        model.WageEmploymentHeadcount++;
                    }
                }
            }

            if (supplementaryRelationships != null)
            {
                for (int i = 0; i < supplementaryRelationships.Count; i++)
                {
                    WorkRelationship relationship = supplementaryRelationships[i];
                    if (relationship.Kind == WorkRelationshipKind.None || !relationship.IsOpen)
                    {
                        continue;
                    }

                    model.FilledHeadcount++;
                    if (relationship.IsWageEmployment)
                    {
                        model.WageEmploymentHeadcount++;
                    }
                }
            }

            return model;
        }
    }
}
