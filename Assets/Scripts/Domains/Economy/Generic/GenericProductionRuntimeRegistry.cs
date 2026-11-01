using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EntityId = LandLedgers.Primitives.EntityId;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;

namespace LandLedgers.Economy
{
    /// <summary>
    /// Shared runtime access point for generic production. It owns no parallel
    /// schedule: all active Person phases are delegated to the hub's TaskAuthority
    /// and persisted process state remains on the Business.
    /// </summary>
    public sealed class GenericProductionRuntimeRegistry
    {
        private readonly TaskAuthority tasks;
        private readonly WorkTimeBudgetStore budgets;
        private readonly Dictionary<string, GenericProductionRuntime> runtimes = new(StringComparer.OrdinalIgnoreCase);

        public GenericProductionRuntimeRegistry(TaskAuthority tasks, WorkTimeBudgetStore budgets)
        {
            this.tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
            this.budgets = budgets;
        }

        /// <summary>
        /// Pass IX WS6 (IDENT-001): optional boundary provenance ledger (the
        /// hub's SharedCategoryStockLedger in production). When set, each
        /// completed process's output is recorded with the real input lots
        /// the process consumed. Null = no provenance recording (legacy
        /// callers unchanged).
        /// </summary>
        public SharedCategoryStockLedger ProvenanceLedger { get; set; }

        /// <summary>Pass IX WS6 (IDENT-001): optional id allocator for provenance lot ids; null = documented negative local sequence on the ledger.</summary>
        public EntityIdRegistry ProvenanceIds { get; set; }

        public GenericProductionRuntime ForBusiness(BusinessInstanceState business, EntityId businessId, int dayIndex)
        {
            if (business == null) throw new ArgumentNullException(nameof(business));
            string key = string.IsNullOrWhiteSpace(business.InstanceId) ? businessId.ToString() : business.InstanceId;
            if (!runtimes.TryGetValue(key, out GenericProductionRuntime runtime))
            {
                runtime = new GenericProductionRuntime(tasks, budgets, businessId, dayIndex);
                foreach (ProductionMethodDefinition method in business.GenericConfiguration.ProductionMethods)
                    runtime.RegisterMethod(method);
                runtimes[key] = runtime;
            }
            else
            {
                runtime.SetDayIndex(dayIndex);
                foreach (ProductionMethodDefinition method in business.GenericConfiguration.ProductionMethods)
                    runtime.RegisterMethod(method);
            }
            return runtime;
        }

        public void ClearRuntime(string businessInstanceId)
        {
            if (!string.IsNullOrWhiteSpace(businessInstanceId)) runtimes.Remove(businessInstanceId);
        }

        public void AdvanceBusinessProcesses(BusinessInstanceState business, EntityId businessId, int dayIndex, int elapsedMinutes)
        {
            if (business == null || elapsedMinutes <= 0) return;
            // Pass V P1-4: generic production advances ONLY businesses routed to
            // the generic path. Legacy businesses produce through their archetype
            // runtimes; advancing generic processes here would double-produce.
            if (ProductionPathRegistry.GetPath(business) != ProductionPath.GenericPath) return;
            GenericProductionRuntime runtime = ForBusiness(business, businessId, dayIndex);
            foreach (ProductionProcessState process in new List<ProductionProcessState>(business.GenericConfiguration.ActiveProcesses))
            {
                if (process == null || process.Completed || !TryParsePerson(process.AssignedPersonId, out EntityId personId)) continue;
                GenericProductDefinition output = new GenericProductDefinition(
                    process.MethodId ?? string.Empty,
                    process.MethodId ?? string.Empty,
                    ProductQuantityUnit.Each);
                runtime.Advance(process, personId, elapsedMinutes, () =>
                {
                    ProductionMethodDefinition method = business.GenericConfiguration.ProductionMethods
                        .FirstOrDefault(candidate => candidate != null
                            && string.Equals(candidate.MethodId, process.MethodId, StringComparison.OrdinalIgnoreCase));
                    if (method == null || string.IsNullOrWhiteSpace(method.OutputProductId)) return true;
                    output = new GenericProductDefinition(method.OutputProductId, method.OutputProductId, ProductQuantityUnit.Each);
                    bool added = business.GenericConfiguration.TryAddInventory(
                        method.OutputProductId,
                        method.OutputQuantity,
                        0,
                        output,
                        $"generic-production:{process.ProcessId}",
                        dayIndex);
                    if (added && ProvenanceLedger != null)
                    {
                        // Pass IX WS6 (IDENT-001): hub vector-slice output
                        // seam — provenance with the process's real input lots.
                        ProvenanceLedger.RecordProductionOutput(
                            method.OutputProductId, method.OutputQuantity,
                            process.ProcessId, business.InstanceId,
                            process.ConsumedInputLotIds, dayIndex, ProvenanceIds);
                    }
                    return added;
                }, out _);
            }
        }

        /// <summary>
        /// Selects the highest-priority eligible policy and starts one ordinary
        /// production process for it. This is scheduling only: the policy never
        /// creates output, and physical state, inventory, and Person Tasks are
        /// still checked by GenericProductionPolicyExecutor.
        /// </summary>
        public bool TryStartEligiblePolicy(BusinessInstanceState business, EntityId businessId,
            int dayIndex, IEnumerable<EntityId> workerIds, out ProductionProcessState process, out string reason)
        {
            process = null;
            reason = string.Empty;
            if (business == null || workerIds == null) return false;
            // Pass V P1-4: policy scheduling is a generic-path operation only.
            if (ProductionPathRegistry.GetPath(business) != ProductionPath.GenericPath)
            {
                reason = $"Policy scheduling refused: '{business.InstanceId}' is on the legacy production path.";
                return false;
            }

            GenericProductionRuntime runtime = ForBusiness(business, businessId, dayIndex);
            var plans = new List<GenericProductionPlan>();
            foreach (GenericProductionPolicy policy in business.GenericConfiguration.ProductionPolicies)
            {
                if (policy == null) continue;
                ProductionMethodDefinition method = business.GenericConfiguration.ProductionMethods
                    .FirstOrDefault(candidate => candidate != null
                        && string.Equals(candidate.MethodId, policy.MethodId, StringComparison.OrdinalIgnoreCase));
                if (method == null) continue;
                plans.Add(GenericProductionPlanner.Plan(
                    policy,
                    method,
                    business.GenericConfiguration.GetInventoryQuantity(method.OutputProductId),
                    0f,
                    input => business.GenericConfiguration.GetAvailableInventoryQuantity(input.ProductId),
                    _ => 0f));
            }

            GenericProductionPlan selected = GenericProductionPlanner.ChooseHighestPriority(plans);
            if (selected == null) return false;

            foreach (EntityId workerId in workerIds)
            {
                if (!workerId.IsValid || business.GenericConfiguration.ActiveProcesses.Any(active =>
                    active != null && !active.Completed
                    && string.Equals(active.AssignedPersonId, workerId.ToString(), StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                GenericProductDefinition output = new GenericProductDefinition(
                    selected.Method.OutputProductId,
                    selected.Method.OutputProductId,
                    ProductQuantityUnit.Each);
                if (GenericProductionPolicyExecutor.TryStart(
                    business, runtime, selected.Method.MethodId, workerId, output, dayIndex,
                    out process, out reason)) return true;
            }

            return false;
        }

        private static bool TryParsePerson(string value, out EntityId personId)
        {
            personId = EntityId.Invalid;
            if (string.IsNullOrWhiteSpace(value) || !value.StartsWith("P", StringComparison.OrdinalIgnoreCase)) return false;
            return int.TryParse(value.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
                && id >= 0
                && (personId = EntityId.For(EntityKind.Person, id)).IsValid;
        }
    }
}
