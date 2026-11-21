using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Tasks;
using LandLedgers.Time;

namespace LandLedgers.Economy
{
    /// <summary>
    /// Runtime coordinator for generic production. It turns an eligible policy
    /// into one persistent process, consumes inputs once, routes active attention
    /// through TaskAuthority, leaves Persons free during passive phases, and emits
    /// output only through the caller's authoritative Inventory/Asset callback.
    /// </summary>
    public sealed class GenericProductionRuntime
    {
        private readonly TaskAuthority taskAuthority;
        private readonly WorkTimeBudgetStore budgetStore;
        private readonly GenericProductionTaskBridge taskBridge;
        private readonly Dictionary<string, ProductionMethodDefinition> methods = new(StringComparer.OrdinalIgnoreCase);

        public GenericProductionRuntime(TaskAuthority taskAuthority, WorkTimeBudgetStore budgetStore,
            EntityId businessId, int dayIndex)
        {
            this.taskAuthority = taskAuthority ?? throw new ArgumentNullException(nameof(taskAuthority));
            this.budgetStore = budgetStore;
            taskBridge = new GenericProductionTaskBridge(taskAuthority, budgetStore, businessId, dayIndex);
        }

        public void RegisterMethod(ProductionMethodDefinition method)
        {
            if (method != null && !string.IsNullOrWhiteSpace(method.MethodId)) methods[method.MethodId] = method;
        }

        public void SetDayIndex(int value) => taskBridge.SetDayIndex(value);

        /// <summary>
        /// Lane 9f (F R-2): rebinds one restored process's current phase task
        /// after a Continue, without accruing work, consuming inputs, or
        /// creating output. A process whose method is no longer registered is
        /// refused LOUDLY with the reason — the process is never silently
        /// dropped, and the caller records the refusal. A passive phase needs
        /// no task and restores trivially; a completed process is already
        /// restored. Returns false only when the process cannot be bound
        /// (missing process, missing method, or an unbindable phase).
        /// </summary>
        public bool RestoreProcess(ProductionProcessState process, EntityId personId, out string reason)
        {
            reason = string.Empty;
            if (process == null)
            {
                reason = "Generic production restore needs a process — refusing to restore nothing.";
                return false;
            }
            if (process.Completed) return true;
            if (!methods.TryGetValue(process.MethodId, out ProductionMethodDefinition method))
            {
                reason = $"Generic production process '{process.ProcessId}' runs '{process.MethodId}', which is not registered on this runtime — the process is kept, not dropped, and must be rebound after its method is registered.";
                return false;
            }
            if (!process.PersonRequiredNow(method)) return true;
            return taskBridge.RestorePhaseTask(process, method, personId, out reason) != null;
        }

        public bool TryBegin(GenericProductionPlan plan, EntityId personId,
            Func<string, bool> hasEquipment, Func<string, bool> hasWorkspace,
            Func<string, bool> hasInput, Func<string, bool> personAvailable,
            Func<ProductionInputRequirement, bool> consumeInput,
            out ProductionProcessState process, out string reason)
        {
            return TryBegin(plan, personId, hasEquipment, hasWorkspace, hasInput, personAvailable,
                consumeInput, null, out process, out reason);
        }

        public bool TryBegin(GenericProductionPlan plan, EntityId personId,
            Func<string, bool> hasEquipment, Func<string, bool> hasWorkspace,
            Func<string, bool> hasInput, Func<string, bool> personAvailable,
            Func<ProductionInputRequirement, bool> consumeInput,
            Action<ProductionInputRequirement> rollbackInput,
            out ProductionProcessState process, out string reason)
        {
            process = null;
            reason = string.Empty;
            if (plan == null || !plan.IsEligible || plan.Method == null)
            {
                reason = plan?.BlockedReason ?? "No eligible production plan.";
                return false;
            }
            if (!GenericProductionAuthority.TryBegin(plan.Method, Guid.NewGuid().ToString("N"), personId.ToString(),
                hasEquipment, hasWorkspace, hasInput, personAvailable, out process, out reason)) return false;
            if (!process.ConsumeInputsOnce(plan.Method, consumeInput, rollbackInput))
            {
                reason = "Production inputs could not be reserved/consumed exactly once.";
                process = null;
                return false;
            }
            RegisterMethod(plan.Method);
            return true;
        }

        public bool Advance(ProductionProcessState process, EntityId personId, int minutes,
            Func<bool> createOutput, out string reason)
        {
            reason = string.Empty;
            if (process == null || !methods.TryGetValue(process.MethodId, out ProductionMethodDefinition method))
            {
                reason = "Production process has no registered method.";
                return false;
            }
            if (process.Completed) return false;
            if (process.PersonRequiredNow(method))
            {
                if (!taskBridge.WorkCurrentPhase(process, method, personId, minutes, out reason)) return false;
            }
            else
            {
                taskBridge.AdvancePassiveTime(process, method, minutes);
            }
            if (process.Completed)
                return GenericProductionAuthority.TryCreateOutputOnce(process, createOutput);
            return true;
        }
    }
}
