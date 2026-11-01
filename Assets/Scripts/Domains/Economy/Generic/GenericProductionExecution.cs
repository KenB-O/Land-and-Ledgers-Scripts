using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;

namespace LandLedgers.Economy
{
    public enum GenericProductionPolicyMode
    {
        MakeToStock = 0,
        MakeToOrder = 1,
    }

    /// <summary>
    /// Management policy only. It decides when ordinary production work may be
    /// requested; it never creates output and it never grants physical ability.
    /// </summary>
    [Serializable]
    public sealed class GenericProductionPolicy
    {
        public string MethodId = string.Empty;
        public bool Enabled = true;
        public GenericProductionPolicyMode Mode = GenericProductionPolicyMode.MakeToStock;
        public float TargetStock;
        public float MaximumStock;
        public int Priority;
        public bool ExternalSaleAllowed = true;
        public bool InternalOrdersAllowed = true;
        public bool CustomerOrdersOverrideStock;
        public bool ConstructionOrdersAllowed = true;
        public float MinimumInputReserve;
        public float BatchSize = 1f;

        public bool IsEligible(float currentStock, float requestedQuantity = 0f)
        {
            if (!Enabled || string.IsNullOrWhiteSpace(MethodId)) return false;
            if (Mode == GenericProductionPolicyMode.MakeToOrder)
                return requestedQuantity > 0f;
            if (currentStock >= TargetStock) return false;
            if (MaximumStock > 0f && currentStock >= MaximumStock) return false;
            return true;
        }

        public float PlannedQuantity(float currentStock, float requestedQuantity = 0f)
        {
            if (!IsEligible(currentStock, requestedQuantity)) return 0f;
            float desired = Mode == GenericProductionPolicyMode.MakeToOrder
                ? requestedQuantity
                : Math.Max(0f, TargetStock - currentStock);
            if (MaximumStock > 0f) desired = Math.Min(desired, Math.Max(0f, MaximumStock - currentStock));
            if (BatchSize > 0f) desired = Math.Min(desired, BatchSize);
            return Math.Max(0f, desired);
        }
    }

    public sealed class GenericProductionPlan
    {
        public GenericProductionPolicy Policy;
        public ProductionMethodDefinition Method;
        public float Quantity;
        public string BlockedReason = string.Empty;
        public bool IsEligible => string.IsNullOrWhiteSpace(BlockedReason) && Quantity > 0f;
    }

    /// <summary>
    /// Converts policy into a schedulable production request. The returned plan
    /// is deliberately not an output mutation; callers must pass it through the
    /// normal physical evaluator and TaskAuthority.
    /// </summary>
    public static class GenericProductionPlanner
    {
        public static GenericProductionPlan Plan(
            GenericProductionPolicy policy,
            ProductionMethodDefinition method,
            float currentStock,
            float requestedQuantity,
            Func<ProductionInputRequirement, float> availableInput,
            Func<ProductionInputRequirement, float> reservedInput)
        {
            var plan = new GenericProductionPlan { Policy = policy, Method = method };
            if (policy == null || method == null)
            {
                plan.BlockedReason = "No production policy or method is configured.";
                return plan;
            }
            if (!policy.IsEligible(currentStock, requestedQuantity))
            {
                plan.BlockedReason = policy.Mode == GenericProductionPolicyMode.MakeToStock
                    ? "Target stock reached or policy is disabled."
                    : "No customer/internal order is waiting.";
                return plan;
            }

            float quantity = policy.PlannedQuantity(currentStock, requestedQuantity);
            foreach (ProductionInputRequirement input in method.InputRequirements ?? new List<ProductionInputRequirement>())
            {
                if (input == null || input.Quantity <= 0f) continue;
                float available = availableInput?.Invoke(input) ?? 0f;
                float reserved = reservedInput?.Invoke(input) ?? 0f;
                float free = Math.Max(0f, available - reserved - Math.Max(0f, policy.MinimumInputReserve));
                if (free < input.Quantity * quantity - 0.001f)
                {
                    plan.BlockedReason = $"Minimum input reserve or available stock blocks '{input.ProductId}'.";
                    return plan;
                }
            }
            plan.Quantity = quantity;
            return plan;
        }

        public static GenericProductionPlan ChooseHighestPriority(IEnumerable<GenericProductionPlan> plans)
        {
            GenericProductionPlan selected = null;
            foreach (GenericProductionPlan plan in plans ?? new List<GenericProductionPlan>())
            {
                if (plan == null || !plan.IsEligible) continue;
                if (selected == null || plan.Policy.Priority > selected.Policy.Priority)
                    selected = plan;
            }
            return selected;
        }
    }

    /// <summary>
    /// Adapter to the shared Person task authority. Active phases become ordinary
    /// WorkTasks; passive phases retain equipment/workspace reservations while no
    /// Person is committed. This class owns no second scheduler.
    /// </summary>
    public sealed class GenericProductionTaskBridge
    {
        private readonly TaskAuthority taskAuthority;
        private readonly WorkTimeBudgetStore budgetStore;
        private readonly EntityId businessId;
        private int dayIndex;
        private readonly Dictionary<string, EntityId> phaseTasks = new(StringComparer.Ordinal);

        public GenericProductionTaskBridge(TaskAuthority taskAuthority, WorkTimeBudgetStore budgetStore,
            EntityId businessId, int dayIndex)
        {
            this.taskAuthority = taskAuthority ?? throw new ArgumentNullException(nameof(taskAuthority));
            this.budgetStore = budgetStore;
            this.businessId = businessId;
            this.dayIndex = dayIndex;
        }

        public void SetDayIndex(int value) => dayIndex = value;

        public WorkTask EnsureCurrentPhaseTask(ProductionProcessState process, ProductionMethodDefinition method,
            EntityId personId, out string reason)
        {
            reason = string.Empty;
            if (process == null || method == null || process.Completed)
            {
                reason = "No open production phase exists.";
                return null;
            }
            if (!process.PersonRequiredNow(method)) return null;
            string taskId = $"generic-production:{process.ProcessId}:phase:{process.CurrentPhaseIndex}";
            if (!phaseTasks.TryGetValue(taskId, out EntityId existingId))
            {
                string definitionId = taskId + ":definition";
                WorkTask persisted = taskAuthority.FindOpenTask(definitionId, businessId, personId);
                if (persisted != null)
                {
                    existingId = persisted.TaskId;
                    phaseTasks[taskId] = existingId;
                }
                else
                {
                if (taskAuthority.GetDefinition(definitionId) == null)
                {
                    var definition = new TaskDefinition(definitionId,
                        method.Phases[process.CurrentPhaseIndex].PhaseId, 
                        method.Phases[process.CurrentPhaseIndex].ElapsedMinutes);
                    // Physical feasibility is checked by the production authority;
                    // no occupation or skill is a permission requirement here.
                    if (!string.IsNullOrWhiteSpace(method.PerformanceSkillId))
                        definition.SetRequiredSkill(method.PerformanceSkillId, new[] { method.PerformanceSkillId });
                    if (!taskAuthority.RegisterDefinition(definition, out reason)) return null;
                }
                WorkTask created = taskAuthority.CreateTask(definitionId, businessId, dayIndex);
                phaseTasks[taskId] = created.TaskId;
                existingId = created.TaskId;
                }
            }

            WorkTask task = taskAuthority.FindTask(existingId);
            if (task == null) { reason = "Production phase task disappeared."; return null; }
            if (task.Status == TaskStatus.Queued && !taskAuthority.AssignTask(task.TaskId, personId, budgetStore, out reason)) return null;
            return task;
        }

        public bool StartCurrentPhase(ProductionProcessState process, ProductionMethodDefinition method,
            EntityId personId, out string reason)
        {
            reason = string.Empty;
            WorkTask task = EnsureCurrentPhaseTask(process, method, personId, out reason);
            if (task == null) return false;
            if (task.Status == TaskStatus.Assigned)
                return taskAuthority.StartTask(task.TaskId, dayIndex, out reason);
            return task.Status == TaskStatus.InProgress || task.Status == TaskStatus.Complete;
        }

        public bool CompleteCurrentPhase(ProductionProcessState process, ProductionMethodDefinition method,
            EntityId personId, out string reason)
        {
            reason = string.Empty;
            string taskId = $"generic-production:{process.ProcessId}:phase:{process.CurrentPhaseIndex}";
            if (!phaseTasks.TryGetValue(taskId, out EntityId id))
            {
                reason = "Current active phase has no task.";
                return false;
            }
            WorkTask task = taskAuthority.FindTask(id);
            if (task == null) { reason = "Current active phase task disappeared."; return false; }
            if (task.Status == TaskStatus.InProgress)
            {
                if (!taskAuthority.RecordWork(id, task.MinutesRemaining, budgetStore, dayIndex, out reason)) return false;
                process.Advance(method, method.Phases[process.CurrentPhaseIndex].ElapsedMinutes);
                return true;
            }
            if (task.Status == TaskStatus.Complete)
            {
                process.Advance(method, method.Phases[process.CurrentPhaseIndex].ElapsedMinutes);
                return true;
            }
            reason = "The active phase task has not completed.";
            return false;
        }

        public bool WorkCurrentPhase(ProductionProcessState process, ProductionMethodDefinition method,
            EntityId personId, int minutes, out string reason)
        {
            reason = string.Empty;
            WorkTask task = EnsureCurrentPhaseTask(process, method, personId, out reason);
            if (task == null) return false;
            if (task.Status == TaskStatus.Assigned && !taskAuthority.StartTask(task.TaskId, dayIndex, out reason)) return false;
            task = taskAuthority.FindTask(task.TaskId);
            if (task == null || task.Status != TaskStatus.InProgress)
            {
                reason = "The active production task is not in progress.";
                return false;
            }
            int work = Math.Max(1, Math.Min(minutes, task.MinutesRemaining));
            if (!taskAuthority.RecordWork(task.TaskId, work, budgetStore, dayIndex, out reason)) return false;
            if (taskAuthority.FindTask(task.TaskId).Status == TaskStatus.Complete)
                process.Advance(method, method.Phases[process.CurrentPhaseIndex].ElapsedMinutes);
            return true;
        }

        public bool AdvancePassiveTime(ProductionProcessState process, ProductionMethodDefinition method, int minutes)
        {
            if (process == null || method == null || process.Completed || process.PersonRequiredNow(method)) return false;
            if (minutes <= 0) return false;
            // Report completion of the current passive phase, rather than
            // completion of the entire process. The process may advance into a
            // later active attention phase without producing its final output.
            int phaseBefore = process.CurrentPhaseIndex;
            int phaseMinutes = Math.Min(minutes, Math.Max(1, process.RemainingPhaseMinutes));
            process.Advance(method, phaseMinutes);
            return process.Completed || process.CurrentPhaseIndex != phaseBefore;
        }
    }
}
