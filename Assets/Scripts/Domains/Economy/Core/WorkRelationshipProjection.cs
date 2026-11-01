using LandLedgers.Primitives;

namespace LandLedgers.Economy
{
    /// <summary>
    /// Projects legacy WorkerSlot assignments onto the TECH §4.1 WorkRelationship taxonomy.
    /// This is a read-side classification bridge (PKG-2): it introduces the taxonomy alongside
    /// the existing WorkerSlot system without changing hiring, payroll or assignment behavior.
    /// Full decomposition of WorkerSlot into RoleDefinition + PositionState + EmploymentRelationship
    /// is PKG-7 (3A-D19); EmploymentRelationship authority is PKG-6 (3A-D18/D21).
    /// </summary>
    public static class WorkRelationshipProjection
    {
        /// <summary>
        /// Classifies one legacy WorkerSlot assignment. Legacy slots cannot distinguish permanent
        /// from seasonal/casual wage employment, so filled slots are classified as
        /// <see cref="WorkRelationshipKind.PermanentEmployment"/> with an explicit ambiguity note
        /// recorded in <see cref="WorkRelationship.Notes"/>. Unfilled slots project to
        /// <see cref="WorkRelationshipKind.None"/> (no relationship exists).
        /// Per SD-04 this projection never classifies family or operator work as employment.
        /// </summary>
        public static WorkRelationship FromWorkerSlotAssignment(string businessId, WorkerSlotState slot)
        {
            if (slot == null || !slot.IsFilled)
            {
                return new WorkRelationship
                {
                    Kind = WorkRelationshipKind.None,
                    PersonId = -1,
                    Counterparty = WorkRelationshipCounterparty.ForBusiness(businessId),
                    RoleDisplayName = slot != null ? slot.SlotDisplayName : string.Empty,
                    StartDayIndex = -1,
                    EndDayIndex = -1,
                    Notes = "PKG-2 projection: unfilled legacy WorkerSlot carries no work relationship.",
                };
            }

            int personId = TryParsePersonId(slot.AssignedWorkerId);

            return new WorkRelationship
            {
                Kind = WorkRelationshipKind.PermanentEmployment,
                PersonId = personId,
                Counterparty = WorkRelationshipCounterparty.ForBusiness(businessId),
                RoleDisplayName = slot.SlotDisplayName,
                StartDayIndex = -1,
                EndDayIndex = -1,
                Notes = "PKG-2 projection: legacy WorkerSlot assignments are wage-employment by construction " +
                        "(slot wage drives payroll); permanent vs seasonal/casual is not distinguishable from " +
                        "legacy data and is recorded here as PermanentEmployment pending PKG-6 classification.",
            };
        }

        /// <summary>
        /// Records family enterprise labor. TECH §4.5: economically material family work is recorded
        /// through task/activity history and the household/business relationship - this projection
        /// creates NO wage terms and NO payroll obligation (PL-04; "FAMILY LABOR != EMPLOYEE").
        /// </summary>
        public static WorkRelationship ForFamilyEnterpriseLabor(
            int personId,
            int householdId,
            string businessId,
            string roleDisplayName,
            int startDayIndex)
        {
            return new WorkRelationship
            {
                Kind = WorkRelationshipKind.FamilyEnterpriseLabor,
                PersonId = personId,
                Counterparty = WorkRelationshipCounterparty.ForBusiness(businessId),
                RoleDisplayName = roleDisplayName ?? string.Empty,
                StartDayIndex = startDayIndex,
                EndDayIndex = -1,
                Notes = $"Family enterprise labor from household {householdId}: updates task time/capability/output; no payroll (TECH §4.5).",
            };
        }

        /// <summary>
        /// Records a proprietor/operator relationship. Compensation is profit/equity draw, never payroll.
        /// (3A-D29: synthetic owner workers must not become fake Persons.)
        /// </summary>
        public static WorkRelationship ForOperator(
            int personId,
            string businessId,
            string roleDisplayName,
            int startDayIndex)
        {
            return new WorkRelationship
            {
                Kind = WorkRelationshipKind.FarmOrBusinessOperator,
                PersonId = personId,
                Counterparty = WorkRelationshipCounterparty.ForBusiness(businessId),
                RoleDisplayName = roleDisplayName ?? string.Empty,
                StartDayIndex = startDayIndex,
                EndDayIndex = -1,
                Notes = "Operator relationship: profit/equity draw, not wage payroll (TECH §4.1).",
            };
        }

        /// <summary>
        /// Records contract/service work. Compensation lives on the agreement terms, not payroll.
        /// </summary>
        public static WorkRelationship ForContractWork(
            int personId,
            string businessId,
            string roleDisplayName,
            int startDayIndex,
            string agreementTerms)
        {
            return new WorkRelationship
            {
                Kind = WorkRelationshipKind.ContractOrServiceWork,
                PersonId = personId,
                Counterparty = WorkRelationshipCounterparty.ForBusiness(businessId),
                RoleDisplayName = roleDisplayName ?? string.Empty,
                StartDayIndex = startDayIndex,
                EndDayIndex = -1,
                Notes = $"Contract/service work; compensation per agreement: {agreementTerms ?? string.Empty}",
            };
        }

        private static int TryParsePersonId(string assignedWorkerId)
        {
            if (int.TryParse(assignedWorkerId, out int personId))
            {
                return personId;
            }

            return -1;
        }
    }
}
