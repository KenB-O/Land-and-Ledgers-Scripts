using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using LandLedgers.Tasks;

namespace LandLedgers.Population
{
    /// <summary>
    /// Pass IV (Worker 4, Phase O): autonomous work generation from legitimate
    /// needs — never from arbitrary task-count quotas. Every rule below fires
    /// only when its trigger condition holds on real state:
    ///
    ///   Business:
    ///   - goods received with no handling task → goods-handling task;
    ///   - shelf below target AND real stock exists → restocking task (empty
    ///     shelf with no stock creates NO task — that is a procurement signal);
    ///   - open repair request → maintenance work order at the requested priority;
    ///   - equipment failed (or condition critical) → maintenance DECISION
    ///     (repair/replace/defer with a stated reason) + work order;
    ///   - farm work window active (right days, weather ok) → field task;
    ///   - manager production policy unmet AND feasible (worker time and
    ///     materials check out) → work order; infeasible → loud refusal, no
    ///     phantom task.
    ///
    ///   Household:
    ///   - days-of-supply below threshold → a REAL purchasing need (consumed by
    ///     the shopping driver) when none is open; never a second need for the
    ///     same item.
    ///
    /// Created tasks are Queued and unassigned — allocation (who does it, with
    /// what equipment, in which window) belongs to the ManagerWorkAllocator /
    /// household drivers, never to generation. Deterministic: inputs in, tasks
    /// out, no randomness, no quotas.
    /// </summary>
    public sealed class AutonomousWorkGenerator
    {
        /// <summary>Task definitions for generated work (registered by the integrator).</summary>
        public static class GeneratedWorkCatalog
        {
            public const string GoodsHandlingId = "work.goods-handling";
            public const string RestockShelvesId = "work.restock-shelves";
            public const string MaintenanceWorkOrderId = "work.maintenance";
            public const string FieldWorkId = "work.field-work";
            public const string ProductionWorkOrderId = "work.production-order";
            /// <summary>Thresh grain at shared equipment (10 grain → 8 flour).</summary>
            public const string ThreshGrainId = "work.thresh-grain";
            /// <summary>Mill grain at the farm mill (10 grain → 9 flour).</summary>
            public const string MillGrainId = "work.mill-grain";

            public static void RegisterAll(TaskAuthority tasks, List<string> diagnostics)
            {
                diagnostics = diagnostics ?? new List<string>();
                if (tasks == null)
                {
                    diagnostics.Add("AutonomousWorkGenerator: no TaskAuthority — generated-work definitions not registered.");
                    return;
                }

                Register(tasks, diagnostics, GoodsHandlingId, "Handle received goods", 30);
                Register(tasks, diagnostics, RestockShelvesId, "Restock shelves from stock", 45);
                Register(tasks, diagnostics, MaintenanceWorkOrderId, "Maintenance work order", 90);
                Register(tasks, diagnostics, FieldWorkId, "Field work", 240);
                Register(tasks, diagnostics, ProductionWorkOrderId, "Production work order", 120);
                Register(tasks, diagnostics, ThreshGrainId, "Thresh grain (shared thresher)", 180);
                Register(tasks, diagnostics, MillGrainId, "Mill grain", 150);
            }

            private static void Register(TaskAuthority tasks, List<string> diagnostics,
                string id, string displayName, int baseMinutes)
            {
                string rejection;
                if (!tasks.RegisterDefinition(new TaskDefinition(id, displayName, baseMinutes), out rejection))
                {
                    diagnostics.Add("AutonomousWorkGenerator: " + id + " not registered — " + rejection);
                }
            }
        }

        /// <summary>Goods physically received and needing handling.</summary>
        [Serializable]
        public sealed class ReceivedGoods
        {
            public string ItemId = string.Empty;
            public int Units;
            public string LocationId = string.Empty;
            public string Provenance = string.Empty;
        }

        /// <summary>One shelf line: what is on the shelf vs its target.</summary>
        [Serializable]
        public sealed class ShelfLine
        {
            public string ItemId = string.Empty;
            public int ShelfUnits;
            public int TargetUnits;
        }

        /// <summary>An explicit repair request (someone asked for the fix).</summary>
        [Serializable]
        public sealed class RepairRequest
        {
            public string RequestId = string.Empty;
            public string EquipmentId = string.Empty;
            public string Description = string.Empty;
            public TaskPriority Priority = TaskPriority.Normal;
            public int DayIndex = -1;
            public bool Fulfilled;
        }

        /// <summary>Observed equipment condition (caller-supplied, read-only).</summary>
        [Serializable]
        public sealed class EquipmentCondition
        {
            public string EquipmentId = string.Empty;
            public string OwnerBusinessId = string.Empty;
            public float Condition01 = 1f;
            public bool Failed;
            public string LocationId = string.Empty;
        }

        /// <summary>Condition at or below which equipment counts as failed.</summary>
        public const float EquipmentFailureConditionThreshold = 0.15f;

        /// <summary>Condition below which preventive maintenance is due.</summary>
        public const float EquipmentMaintenanceConditionThreshold = 0.35f;

        /// <summary>A seasonal/contractual window for field work.</summary>
        [Serializable]
        public sealed class FarmWorkWindow
        {
            public int StartDayIndex;
            public int EndDayIndex;
            public string WorkKind = string.Empty;
            public string FieldLocationId = string.Empty;
            public bool WeatherOk = true;
        }

        /// <summary>A manager's production policy vs observed output.</summary>
        [Serializable]
        public sealed class ManagerProductionPolicy
        {
            public string ItemId = string.Empty;
            public int TargetUnitsPerDay;
            public int ActualUnitsPerDay;
            public string WorkDefinitionId = GeneratedWorkCatalog.ProductionWorkOrderId;
        }

        /// <summary>Feasibility evidence for a policy-driven work order.</summary>
        [Serializable]
        public sealed class WorkFeasibility
        {
            public bool Feasible = true;
            public string Reason = string.Empty;
            public int AvailableWorkerMinutes;
            public bool MaterialsAvailable = true;
        }

        /// <summary>A maintenance decision with a stated reason (repair vs replace vs defer).</summary>
        [Serializable]
        public sealed class MaintenanceDecision
        {
            public string EquipmentId = string.Empty;
            public string Decision = string.Empty; // Repair | Replace | Defer
            public string Reason = string.Empty;
            public int DayIndex = -1;
            public string WorkOrderTaskId = string.Empty;
        }

        public sealed class BusinessGenerationInputs
        {
            public string BusinessInstanceId = string.Empty;
            public EntityId BusinessOwner;
            public List<ReceivedGoods> ReceivedGoods = new List<ReceivedGoods>();
            public List<ShelfLine> Shelves = new List<ShelfLine>();
            /// <summary>Real stock units by item id (back-room, not shelf).</summary>
            public Func<string, int> StockUnits;
            public List<RepairRequest> RepairRequests = new List<RepairRequest>();
            public List<EquipmentCondition> Equipment = new List<EquipmentCondition>();
            public FarmWorkWindow FarmWindow;
            public ManagerProductionPolicy ProductionPolicy;
            /// <summary>Feasibility check for policy-driven work orders.</summary>
            public Func<ManagerProductionPolicy, WorkFeasibility> CheckFeasibility;
            /// <summary>True when an open task already covers (definitionId, customerRef).</summary>
            public Func<string, string, bool> HasOpenTask;
            public int DayIndex;
        }

        public sealed class HouseholdGenerationInputs
        {
            public int HouseholdId = -1;
            public EntityId HouseholdOwner;
            /// <summary>Item ids the household consumes, with days of supply left.</summary>
            public Dictionary<string, float> DaysOfSupplyByItem = new Dictionary<string, float>();
            public float LowDaysThreshold = 7f;
            public HouseholdNeedRegistry Needs;
            public int DayIndex;
        }

        public sealed class GenerationReport
        {
            public List<string> CreatedTaskIds = new List<string>();
            public List<string> CreatedNeedKeys = new List<string>();
            public List<string> Refusals = new List<string>();
            public List<MaintenanceDecision> MaintenanceDecisions = new List<MaintenanceDecision>();
        }

        /// <summary>Generates business work from real conditions. Never invents demand.</summary>
        public GenerationReport GenerateForBusiness(BusinessGenerationInputs inputs, TaskAuthority tasks, List<string> diag)
        {
            var report = new GenerationReport();
            diag = diag ?? new List<string>();
            if (inputs == null || tasks == null)
            {
                diag.Add("AutonomousWorkGenerator: no inputs or task authority — nothing generated.");
                return report;
            }

            int day = Math.Max(0, inputs.DayIndex);

            // 1. Received goods → handling tasks.
            foreach (ReceivedGoods goods in inputs.ReceivedGoods)
            {
                if (goods == null || goods.Units <= 0 || string.IsNullOrWhiteSpace(goods.ItemId))
                {
                    continue;
                }

                string customerRef = $"goods:{goods.ItemId}:{goods.LocationId}";
                if (HasOpen(inputs, GeneratedWorkCatalog.GoodsHandlingId, customerRef))
                {
                    continue;
                }

                CreateTask(tasks, inputs.BusinessOwner, GeneratedWorkCatalog.GoodsHandlingId,
                    customerRef, $"handle {goods.Units}u {goods.ItemId} ({goods.Provenance})",
                    TaskPriority.Normal, day, report, diag);
            }

            // 2. Shelves below target with real stock → restocking tasks.
            foreach (ShelfLine shelf in inputs.Shelves)
            {
                if (shelf == null || string.IsNullOrWhiteSpace(shelf.ItemId))
                {
                    continue;
                }

                int deficit = shelf.TargetUnits - shelf.ShelfUnits;
                if (deficit <= 0)
                {
                    continue;
                }

                int stock = inputs.StockUnits != null ? Math.Max(0, inputs.StockUnits(shelf.ItemId)) : 0;
                if (stock <= 0)
                {
                    // Empty shelf AND no stock: no task — this is a procurement
                    // signal for the manager, not phantom restocking work.
                    string note = $"restock refused for '{shelf.ItemId}': shelf {shelf.ShelfUnits}/{shelf.TargetUnits} but no real stock — procurement signal, not a task";
                    report.Refusals.Add(note);
                    diag.Add("AutonomousWorkGenerator: " + note);
                    continue;
                }

                string customerRef = $"restock:{shelf.ItemId}";
                if (HasOpen(inputs, GeneratedWorkCatalog.RestockShelvesId, customerRef))
                {
                    continue;
                }

                int units = Math.Min(deficit, stock);
                CreateTask(tasks, inputs.BusinessOwner, GeneratedWorkCatalog.RestockShelvesId,
                    customerRef, $"restock {units}u {shelf.ItemId} (shelf {shelf.ShelfUnits}/{shelf.TargetUnits})",
                    TaskPriority.Normal, day, report, diag);
            }

            // 3. Repair requests → work orders at the requested priority.
            foreach (RepairRequest request in inputs.RepairRequests)
            {
                if (request == null || request.Fulfilled || string.IsNullOrWhiteSpace(request.RequestId))
                {
                    continue;
                }

                string customerRef = $"repair:{request.RequestId}";
                if (HasOpen(inputs, GeneratedWorkCatalog.MaintenanceWorkOrderId, customerRef))
                {
                    continue;
                }

                WorkTask task = CreateTask(tasks, inputs.BusinessOwner, GeneratedWorkCatalog.MaintenanceWorkOrderId,
                    customerRef, $"repair {request.EquipmentId}: {request.Description}",
                    request.Priority, day, report, diag);
                if (task != null)
                {
                    request.Fulfilled = true;
                    diag.Add($"AutonomousWorkGenerator: repair request '{request.RequestId}' → work order {task.TaskId}.");
                }
            }

            // 4. Equipment condition → maintenance decisions + work orders.
            foreach (EquipmentCondition equipment in inputs.Equipment)
            {
                if (equipment == null || string.IsNullOrWhiteSpace(equipment.EquipmentId))
                {
                    continue;
                }

                bool failed = equipment.Failed || equipment.Condition01 <= EquipmentFailureConditionThreshold;
                bool worn = equipment.Condition01 < EquipmentMaintenanceConditionThreshold;
                if (!failed && !worn)
                {
                    continue;
                }

                string customerRef = $"equip-maint:{equipment.EquipmentId}:{day}";
                if (HasOpen(inputs, GeneratedWorkCatalog.MaintenanceWorkOrderId, customerRef))
                {
                    continue;
                }

                var decision = new MaintenanceDecision
                {
                    EquipmentId = equipment.EquipmentId,
                    DayIndex = day,
                };
                TaskPriority priority;
                if (failed)
                {
                    // Failed equipment: repair if it has life left, replace if
                    // destroyed, defer never — a failed asset blocks work.
                    if (equipment.Condition01 <= 0.02f)
                    {
                        decision.Decision = "Replace";
                        decision.Reason = $"condition {equipment.Condition01:F2}: destroyed beyond repair — replacement, not a work order, is the honest path";
                        report.MaintenanceDecisions.Add(decision);
                        diag.Add("AutonomousWorkGenerator: " + decision.EquipmentId + " destroyed — replace, no work order.");
                        continue;
                    }

                    decision.Decision = "Repair";
                    decision.Reason = $"failed at condition {equipment.Condition01:F2} — repair work order, urgent";
                    priority = TaskPriority.Urgent;
                }
                else
                {
                    decision.Decision = "Repair";
                    decision.Reason = $"condition {equipment.Condition01:F2} below maintenance threshold — preventive work order";
                    priority = TaskPriority.Low;
                }

                WorkTask task = CreateTask(tasks, inputs.BusinessOwner, GeneratedWorkCatalog.MaintenanceWorkOrderId,
                    customerRef, $"{decision.Decision.ToLowerInvariant()} {equipment.EquipmentId} ({decision.Reason})",
                    priority, day, report, diag);
                if (task != null)
                {
                    decision.WorkOrderTaskId = task.TaskId.ToString();
                }

                report.MaintenanceDecisions.Add(decision);
            }

            // 5. Farm work window → field tasks.
            FarmWorkWindow window = inputs.FarmWindow;
            if (window != null && !string.IsNullOrWhiteSpace(window.WorkKind) &&
                day >= window.StartDayIndex && day <= window.EndDayIndex && window.WeatherOk)
            {
                string customerRef = $"field:{window.WorkKind}:{window.StartDayIndex}-{window.EndDayIndex}";
                if (!HasOpen(inputs, GeneratedWorkCatalog.FieldWorkId, customerRef))
                {
                    CreateTask(tasks, inputs.BusinessOwner, GeneratedWorkCatalog.FieldWorkId,
                        customerRef, $"field work: {window.WorkKind} at {window.FieldLocationId} (window days {window.StartDayIndex}-{window.EndDayIndex})",
                        TaskPriority.High, day, report, diag);
                }
            }
            else if (window != null && day >= window.StartDayIndex && day <= window.EndDayIndex && !window.WeatherOk)
            {
                diag.Add($"AutonomousWorkGenerator: field work '{window.WorkKind}' window is open but weather is bad — no task today.");
            }

            // 6. Manager production policy unmet → FEASIBLE work order only.
            ManagerProductionPolicy policy = inputs.ProductionPolicy;
            if (policy != null && !string.IsNullOrWhiteSpace(policy.ItemId) &&
                policy.ActualUnitsPerDay < policy.TargetUnitsPerDay)
            {
                string customerRef = $"policy:{policy.ItemId}:{day}";
                if (!HasOpen(inputs, policy.WorkDefinitionId, customerRef))
                {
                    WorkFeasibility feasibility = inputs.CheckFeasibility != null
                        ? inputs.CheckFeasibility(policy)
                        : new WorkFeasibility { Feasible = true, Reason = "no feasibility check wired — assumed feasible", AvailableWorkerMinutes = int.MaxValue, MaterialsAvailable = true };
                    if (feasibility != null && feasibility.Feasible)
                    {
                        CreateTask(tasks, inputs.BusinessOwner, policy.WorkDefinitionId,
                            customerRef, $"policy: make {policy.ItemId} ({policy.ActualUnitsPerDay}/{policy.TargetUnitsPerDay}/day) — {feasibility.Reason}",
                            TaskPriority.High, day, report, diag);
                    }
                    else
                    {
                        string note = $"policy work order refused for '{policy.ItemId}': {(feasibility != null ? feasibility.Reason : "infeasible")} — no phantom task";
                        report.Refusals.Add(note);
                        diag.Add("AutonomousWorkGenerator: " + note);
                    }
                }
            }

            return report;
        }

        /// <summary>
        /// Generates household work from real conditions: low days-of-supply →
        /// a real purchasing need (one per item; the shopping driver consumes it).
        /// </summary>
        public GenerationReport GenerateForHousehold(HouseholdGenerationInputs inputs, List<string> diag)
        {
            var report = new GenerationReport();
            diag = diag ?? new List<string>();
            if (inputs == null || inputs.Needs == null)
            {
                diag.Add("AutonomousWorkGenerator: no household inputs or need registry — nothing generated.");
                return report;
            }

            int day = Math.Max(0, inputs.DayIndex);
            foreach (KeyValuePair<string, float> entry in inputs.DaysOfSupplyByItem)
            {
                if (string.IsNullOrWhiteSpace(entry.Key) || entry.Value >= inputs.LowDaysThreshold)
                {
                    continue;
                }

                if (inputs.Needs.GetOpenNeed(inputs.HouseholdId, entry.Key) != null)
                {
                    continue; // one open need per item — never duplicates.
                }

                HouseholdPurchasingNeed need = inputs.Needs.CreateNeed(inputs.HouseholdId, entry.Key, day);
                need.IsNecessity = true;
                need.Urgency01 = 1f - Math.Max(0f, Math.Min(1f, entry.Value / Math.Max(0.01f, inputs.LowDaysThreshold)));
                need.ReasonSummary = $"only {entry.Value:F1} days of '{entry.Key}' left (threshold {inputs.LowDaysThreshold})";
                report.CreatedNeedKeys.Add(inputs.HouseholdId + ":" + entry.Key);
                diag.Add($"AutonomousWorkGenerator: household {inputs.HouseholdId} needs '{entry.Key}' — purchasing need {need.NeedSequence} opened ({need.ReasonSummary}).");
            }

            return report;
        }

        // ---------- internals ----------

        private static bool HasOpen(BusinessGenerationInputs inputs, string definitionId, string customerRef)
        {
            try
            {
                return inputs.HasOpenTask != null && inputs.HasOpenTask(definitionId, customerRef);
            }
            catch
            {
                return false;
            }
        }

        private static WorkTask CreateTask(
            TaskAuthority tasks, EntityId owner, string definitionId, string customerRef,
            string targetDescription, TaskPriority priority, int day,
            GenerationReport report, List<string> diag)
        {
            WorkTask task = tasks.CreateTask(definitionId, owner, day, customerRef);
            if (task == null)
            {
                string note = $"task creation refused for '{definitionId}' ({customerRef})";
                report.Refusals.Add(note);
                diag.Add("AutonomousWorkGenerator: " + note);
                return null;
            }

            task.SetPriority(priority);
            report.CreatedTaskIds.Add(task.TaskId.ToString());
            diag.Add($"AutonomousWorkGenerator: created {task.TaskId} '{definitionId}' — {targetDescription}.");
            return task;
        }
    }
}
