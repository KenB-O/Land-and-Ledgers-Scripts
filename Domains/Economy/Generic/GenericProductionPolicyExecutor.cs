using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Linq;

namespace LandLedgers.Economy
{
    /// <summary>
    /// Production-policy entry point for normal runtime callers. Planning remains
    /// separate from execution: this adapter evaluates the persisted policy,
    /// checks physical equipment/workspace state, consumes the shared generic
    /// Inventory authority, and schedules ordinary Person Tasks through the
    /// shared GenericProductionRuntime.
    /// </summary>
    public static class GenericProductionPolicyExecutor
    {
        public static bool TryStart(
            BusinessInstanceState business,
            GenericProductionRuntime runtime,
            string methodId,
            EntityId personId,
            GenericProductDefinition outputDefinition,
            int dayIndex,
            out ProductionProcessState process,
            out string reason,
            Func<string, bool> hasEquipment = null,
            Func<string, bool> hasWorkspace = null,
            Func<string, bool> personAvailable = null)
        {
            process = null;
            reason = string.Empty;
            if (business == null || runtime == null)
            {
                reason = "Generic production requires a Business and shared runtime.";
                return false;
            }

            ProductionMethodDefinition method = business.GenericConfiguration.ProductionMethods
                .FirstOrDefault(candidate => candidate != null
                    && string.Equals(candidate.MethodId, methodId ?? string.Empty, StringComparison.OrdinalIgnoreCase));
            if (method == null)
            {
                reason = "No generic ProductionMethod is registered for this Business.";
                return false;
            }

            GenericProductionPolicy policy;
            if (!business.GenericConfiguration.TryGetProductionPolicy(method.MethodId, out policy))
            {
                reason = "No generic ProductionPolicy is registered for this method.";
                return false;
            }

            float currentStock = business.GenericConfiguration.GetInventoryQuantity(method.OutputProductId);
            GenericProductionPlan plan = GenericProductionPlanner.Plan(
                policy,
                method,
                currentStock,
                0f,
                input => business.GenericConfiguration.GetAvailableInventoryQuantity(input.ProductId),
                _ => 0f);
            if (!plan.IsEligible)
            {
                reason = plan.BlockedReason;
                return false;
            }

            hasEquipment ??= business.GenericConfiguration.IsEquipmentAvailable;
            hasWorkspace ??= business.GenericConfiguration.IsWorkspaceAvailable;
            personAvailable ??= _ => true;

            bool Consume(ProductionInputRequirement input) =>
                business.GenericConfiguration.TryConsumeInventory(input.ProductId, input.Quantity, out _);
            void Rollback(ProductionInputRequirement input) =>
                business.GenericConfiguration.AddInventory(input.ProductId, input.Quantity,
                    business.GenericConfiguration.GetInventoryAverageUnitCostCents(input.ProductId),
                    null, "generic-production-rollback", dayIndex);

            return business.TryStartGenericProduction(
                runtime,
                plan,
                personId,
                hasEquipment,
                hasWorkspace,
                input => business.GenericConfiguration.GetAvailableInventoryQuantity(input) > 0f,
                personAvailable,
                Consume,
                Rollback,
                out process,
                out reason);
        }

        public static bool Advance(
            BusinessInstanceState business,
            GenericProductionRuntime runtime,
            ProductionProcessState process,
            EntityId personId,
            int minutes,
            GenericProductDefinition outputDefinition,
            int unitCostCents,
            string provenance,
            int dayIndex,
            out string reason)
        {
            reason = string.Empty;
            if (business == null || runtime == null || process == null)
            {
                reason = "Generic production process is unavailable.";
                return false;
            }
            return business.AdvanceGenericProduction(
                runtime,
                process.ProcessId,
                personId,
                minutes,
                () => GenericProductionOutputAuthority.TryCreateInventoryOutputOnce(
                    process,
                    process.MethodId == null ? null : business.GenericConfiguration.ProductionMethods
                        .FirstOrDefault(method => method != null
                            && string.Equals(method.MethodId, process.MethodId, StringComparison.OrdinalIgnoreCase)),
                    business.GenericConfiguration,
                    outputDefinition,
                    unitCostCents,
                    provenance,
                    dayIndex,
                    out _),
                out reason);
        }
    }
}
