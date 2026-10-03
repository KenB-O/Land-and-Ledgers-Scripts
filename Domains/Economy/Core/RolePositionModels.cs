using System;
using System.Collections.Generic;
using LandLedgers.Persistence;

namespace LandLedgers.Economy
{
    /// <summary>
    /// PKG-7 (PL-21 / 3A-D19): WorkerSlot decomposed into RoleDefinition + PositionState +
    /// EmploymentRelationship.
    ///
    /// - <see cref="RoleDefinition"/>: authored staffing template (what the business needs).
    ///   Adapted from the existing WorkerSlotDefinition, which is left untouched for
    ///   serialization stability.
    /// - <see cref="PositionState"/>: runtime staffing need / vacancy (a position exists whether
    ///   or not a worker fills it). Carries role/display identity, required-for-opening and
    ///   business association. It does NOT receive assigned worker as authority, and carries
    ///   no wage (3A-D28 §19.1).
    /// - <see cref="EmploymentRelationship"/> (PKG-6): the worker, the compensation, the lifecycle.
    ///
    /// <see cref="WorkerSlotCompatibility"/> keeps a WorkerSlotState-shaped projection for
    /// existing consumers (payroll, UI, hiring) until they migrate. New code must use the
    /// decomposed types directly.
    /// </summary>

    /// <summary>
    /// Authored role template: the staffing need independent of any worker. Canonical form of
    /// WorkerSlotDefinition for the decomposed architecture (the definition class itself is
    /// not renamed - serialized type stability per AGENTS.md).
    /// </summary>
    [Serializable]
    public sealed class RoleDefinition
    {
        public string RoleId = string.Empty;
        public string DisplayName = string.Empty;
        public string RequiredCapability = string.Empty;
        public int BaselineWeeklyWageCents;
        public bool RequiredForOpening;

        public static RoleDefinition FromWorkerSlotDefinition(WorkerSlotDefinition definition)
        {
            if (definition == null)
            {
                return new RoleDefinition();
            }

            return new RoleDefinition
            {
                RoleId = definition.SlotId,
                DisplayName = definition.DisplayName,
                RequiredCapability = string.Empty,
                BaselineWeeklyWageCents = definition.BaselineWeeklyWageCents,
                RequiredForOpening = definition.RequiredForOpening,
            };
        }
    }

    /// <summary>
    /// Runtime staffing position: a vacancy/business need independent of worker truth in slots
    /// (3A-D19: "Keep vacancy/business need independent of worker"). PositionId is
    /// business-scoped and deterministic: BusinessId + legacy slotId (3A-D28 §19.1).
    /// A position never stores who fills it or at what wage - those belong to
    /// EmploymentRelationship.
    /// </summary>
    [Serializable]
    public sealed class PositionState
    {
        public string PositionId = string.Empty;
        public string BusinessId = string.Empty;
        public string RoleId = string.Empty;
        public string DisplayName = string.Empty;
        public bool RequiredForOpening;
        public string CoverageNotes = string.Empty;

        public static string BuildPositionId(string businessId, string slotId)
        {
            return $"{businessId ?? string.Empty}:{slotId ?? string.Empty}";
        }

        /// <summary>
        /// Creates the runtime position from a legacy slot. Receives role/display identity,
        /// required-for-opening and business association only. Assigned worker and wage are
        /// deliberately NOT carried over (3A-D28 §19.1).
        /// </summary>
        public static PositionState FromWorkerSlot(string businessId, WorkerSlotState slot)
        {
            if (slot == null)
            {
                return new PositionState { BusinessId = businessId ?? string.Empty };
            }

            return new PositionState
            {
                PositionId = BuildPositionId(businessId, slot.SlotId),
                BusinessId = businessId ?? string.Empty,
                RoleId = slot.SlotId,
                DisplayName = slot.SlotDisplayName,
                RequiredForOpening = slot.RequiredForOpening,
                CoverageNotes = "PKG-7: decomposed from legacy WorkerSlot; worker identity and wage live on EmploymentRelationship.",
            };
        }
    }

    /// <summary>
    /// Compatibility projection: renders PositionState + EmploymentRelationship back into the
    /// legacy WorkerSlotState shape so existing consumers (payroll, hiring UI, reports) keep
    /// working while they migrate to the decomposed types. New code must NOT build on this;
    /// it is a migration shim, not an authority.
    /// </summary>
    public static class WorkerSlotCompatibility
    {
        /// <summary>
        /// Builds a WorkerSlotState-shaped view. Staffing need comes from the position, worker
        /// identity and wage come from the employment (null employment = vacant position).
        /// </summary>
        public static WorkerSlotState ToLegacyProjection(PositionState position, EmploymentRelationship employment)
        {
            string roleId = position != null ? position.RoleId : string.Empty;
            string displayName = position != null ? position.DisplayName : string.Empty;
            bool requiredForOpening = position != null && position.RequiredForOpening;

            if (employment == null)
            {
                return new WorkerSlotState(roleId, displayName, 0, requiredForOpening);
            }

            int wageCents = Math.Max(0, employment.Compensation.AgreedWeeklyWageCents);
            bool paidActive = employment.LifecycleState == EmploymentLifecycleState.Active;
            bool suspendedForMissedPayroll =
                employment.LifecycleState == EmploymentLifecycleState.Suspended
                && employment.SuspensionReason == EmploymentSuspensionReason.EmployerPaymentDefault;

            return WorkerSlotState.FromSaveDto(new WorkerSlotSaveDto
            {
                slotId = roleId,
                slotDisplayName = displayName,
                assignedWorkerId = employment.EmployeePersonId.ToString(),
                assignedWorkerDisplayName = employment.EmployeePersonId.ToString(),
                weeklyWageCents = wageCents,
                requiredForOpening = requiredForOpening,
                paidActive = paidActive,
                suspendedForMissedPayroll = suspendedForMissedPayroll,
            });
        }
    }

    /// <summary>
    /// Documents which decomposed type owns each legacy WorkerSlotState field, for consumers
    /// migrating off the compatibility projection (3A-D19).
    /// </summary>
    public static class WorkerSlotFieldOwnership
    {
        private static readonly Dictionary<string, string> Owners = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "slotId", "RoleDefinition.RoleId (authored) / PositionState.PositionId (runtime)" },
            { "slotDisplayName", "RoleDefinition.DisplayName" },
            { "assignedWorkerId", "EmploymentRelationship.EmployeePersonId" },
            { "assignedWorkerDisplayName", "Derived from PersonState.DisplayName (never stored)" },
            { "weeklyWageCents", "EmploymentRelationship.Compensation.AgreedWeeklyWageCents (3A-D21)" },
            { "requiredForOpening", "RoleDefinition.RequiredForOpening / PositionState.RequiredForOpening" },
            { "paidActive", "EmploymentRelationship.LifecycleState (Active vs Suspended)" },
            { "suspendedForMissedPayroll", "EmploymentRelationship.SuspensionReason" },
        };

        /// <summary>Returns the owning decomposed type for a legacy WorkerSlotState field name.</summary>
        public static string GetOwner(string legacyFieldName)
        {
            if (string.IsNullOrWhiteSpace(legacyFieldName))
            {
                return "Unknown field";
            }

            return Owners.TryGetValue(legacyFieldName.Trim(), out string owner)
                ? owner
                : "Unknown field";
        }

        public static IReadOnlyDictionary<string, string> All => Owners;
    }
}
