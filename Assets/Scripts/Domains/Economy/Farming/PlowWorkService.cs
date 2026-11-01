// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Pass IV (Worker 3) — Slice C: farm plowing.
//
// FarmPlowService runs plowing work orders through real TaskAuthority WorkTasks.
// Every order needs ALL of: field area (CropFieldState.Acres), a suitable plow
// (EquipmentAsset, Kind="plow", EXCLUSIVE reservation — no double-booking), an
// operator (driver Person), a draft team (2 horses via DraftPowerService), and
// usable condition (plow + harness). Two competing orders + one plow: the
// second WAITS (stays queued), it is not failed and no phantom plow is conjured.
// A broken plow BLOCKS the order or routes to a legitimate alternative plow —
// never a phantom.

using System;
using System.Collections.Generic;
using UnityEngine;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.Animals;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.DraftPower;
using LandLedgers.Economy.Farming.Crops;
using EntityId = LandLedgers.Primitives.EntityId;

namespace LandLedgers.Economy.Farming
{
    public enum PlowOrderStatus
    {
        Queued = 0,
        InProgress = 1,
        Done = 2,
        Blocked = 3,
    }

    /// <summary>One plowing work order for one field.</summary>
    [Serializable]
    public sealed class PlowWorkOrder
    {
        public string OrderId = string.Empty;
        public string FieldId = string.Empty;
        public float Acres;
        public PlowOrderStatus Status = PlowOrderStatus.Queued;
        public string PreferredPlowAssetId = string.Empty;
        public string AssignedPlowAssetId = string.Empty;
        public string UnitId = string.Empty;
        public List<EntityId> TeamAnimalIds = new List<EntityId>();
        public EntityId DriverPersonId = EntityId.Invalid;
        public EntityId TaskId = EntityId.Invalid;
        public float AcresPlowed;
        public string WaitReason = string.Empty;
        public string BlockedReason = string.Empty;
    }

    /// <summary>Runtime hooks the plow service needs. Supplied by the caller.</summary>
    public sealed class PlowWorkHooks
    {
        public TaskAuthority Tasks;
        public WorkTimeBudgetStore Budgets;
        public DraftPowerService DraftPower;
        public AnimalRegistry AnimalRegistry;
        /// <summary>Candidate horses (real animals) the service may team up.</summary>
        public List<EntityId> AvailableHorses;
        /// <summary>Candidate drivers (real persons) the service may assign.</summary>
        public List<EntityId> AvailableDrivers;
        /// <summary>Plow implements to choose from (real assets, never phantoms).</summary>
        public List<EquipmentAsset> Plows;
        public EquipmentAsset Harness;
        public int DayIndex;
        public Func<float> NowGameSeconds;
        public List<string> Diagnostics;
    }

    /// <summary>
    /// Runs plowing work orders. The plow, team, and driver are all reserved
    /// exclusively; the work itself is a real WorkTask scaled by field acres.
    /// </summary>
    public sealed class FarmPlowService
    {
        /// <summary>Minutes of plowing per acre with a standard 2-horse team.</summary>
        public float MinutesPerAcre = 30f;
        /// <summary>Implement wear per acre plowed (0..1).</summary>
        public float WearPerAcre01 = 0.02f;

        private readonly List<PlowWorkOrder> orders = new List<PlowWorkOrder>();
        private readonly Dictionary<string, CropFieldState> fields =
            new Dictionary<string, CropFieldState>(StringComparer.Ordinal);
        private readonly Dictionary<string, DraftWorkUnit> activeUnits =
            new Dictionary<string, DraftWorkUnit>(StringComparer.Ordinal);
        private int sequence;

        public IReadOnlyList<PlowWorkOrder> Orders => orders;

        public void RegisterField(CropFieldState field)
        {
            if (field == null || string.IsNullOrWhiteSpace(field.FieldId)) return;
            fields[field.FieldId] = field;
        }

        /// <summary>Queues a plowing order for a field. Refuses loudly on bad input.</summary>
        public PlowWorkOrder RequestPlowing(CropFieldState field, out string reason, string preferredPlowAssetId = null)
        {
            reason = string.Empty;
            if (field == null || string.IsNullOrWhiteSpace(field.FieldId))
            {
                reason = "FarmPlowService: plowing needs a real field.";
                return null;
            }
            if (field.Acres <= 0f)
            {
                reason = "FarmPlowService: field '" + field.FieldId + "' has no area (" + field.Acres + " acres) — nothing to plow.";
                return null;
            }
            foreach (PlowWorkOrder existing in orders)
            {
                if ((existing.Status == PlowOrderStatus.Queued || existing.Status == PlowOrderStatus.InProgress)
                    && string.Equals(existing.FieldId, field.FieldId, StringComparison.Ordinal))
                {
                    reason = "FarmPlowService: field '" + field.FieldId + "' already has order " + existing.OrderId + " (" + existing.Status + ") — no duplicate order.";
                    return null;
                }
            }
            RegisterField(field);
            var order = new PlowWorkOrder
            {
                OrderId = "plow-" + field.FieldId + "-" + (sequence++),
                FieldId = field.FieldId,
                Acres = field.Acres,
                PreferredPlowAssetId = preferredPlowAssetId ?? string.Empty,
            };
            orders.Add(order);
            return order;
        }

        /// <summary>Tries to start queued orders (FIFO). Orders whose plow/team is
        /// busy WAIT; orders with no usable plow anywhere BLOCK.</summary>
        public void Tick(PlowWorkHooks hooks)
        {
            if (hooks == null) return;
            foreach (PlowWorkOrder order in orders)
            {
                if (order.Status != PlowOrderStatus.Queued) continue;
                TryStartOrder(order, hooks);
            }
        }

        /// <summary>
        /// Works an in-progress order's task through TaskAuthority. Updates the
        /// field's WorkProgress01 as acres are plowed. On completion the unit is
        /// worked (wear) and released, and the field is marked plowed.
        /// </summary>
        public bool WorkOnPlowing(PlowWorkOrder order, int minutes, PlowWorkHooks hooks, out string reason)
        {
            reason = string.Empty;
            if (order == null) { reason = "FarmPlowService: no order."; return false; }
            if (order.Status != PlowOrderStatus.InProgress) { reason = "FarmPlowService: order " + order.OrderId + " is " + order.Status + ", not in progress."; return false; }
            if (hooks == null || hooks.Tasks == null) { reason = "FarmPlowService: no TaskAuthority."; return false; }
            WorkTask task = hooks.Tasks.FindTask(order.TaskId);
            if (task == null) { reason = "FarmPlowService: task " + order.TaskId + " vanished."; return false; }

            if (!hooks.Tasks.RecordWorkProgress(order.TaskId, minutes, hooks.Budgets, hooks.DayIndex,
                out bool reachedCompletion, out string rejection))
            {
                reason = rejection;
                return false;
            }

            UpdateFieldProgress(order, task);
            if (!reachedCompletion) return true;

            hooks.Tasks.CompleteTask(order.TaskId, TaskOutcome.Success, hooks.DayIndex, hooks.Budgets);
            return FinishOrder(order, hooks, out reason);
        }

        // ---------- save / load (DTO methods inside the owning runtime class) ----------

        [Serializable]
        public sealed class FarmPlowServiceSaveDto
        {
            public List<PlowWorkOrder> Orders = new List<PlowWorkOrder>();
            public int Sequence;
        }

        public FarmPlowServiceSaveDto CaptureSaveDto()
        {
            return new FarmPlowServiceSaveDto
            {
                Orders = new List<PlowWorkOrder>(orders),
                Sequence = sequence,
            };
        }

        public void RestoreSaveDto(FarmPlowServiceSaveDto dto)
        {
            if (dto == null) return;
            orders.Clear();
            orders.AddRange(dto.Orders);
            activeUnits.Clear();
            sequence = dto.Sequence;
        }

        // ---------- internals ----------

        private void Log(PlowWorkHooks hooks, string message)
        {
            if (hooks != null && hooks.Diagnostics != null) hooks.Diagnostics.Add("FarmPlowService: " + message);
        }

        private void TryStartOrder(PlowWorkOrder order, PlowWorkHooks hooks)
        {
            order.WaitReason = string.Empty;
            if (hooks.DraftPower == null || hooks.AnimalRegistry == null)
            {
                order.WaitReason = "no draft-power authority — waiting.";
                return;
            }
            if (hooks.Plows == null || hooks.Plows.Count == 0)
            {
                BlockOrder(order, "no plow implements available at all — acquire or repair a plow first.", hooks);
                return;
            }

            // 1. Choose a plow: preferred first, else any usable unreserved one.
            //    A broken preferred plow routes to a legitimate alternative — never a phantom.
            EquipmentAsset chosen = null;
            EquipmentAsset preferred = null;
            foreach (EquipmentAsset plow in hooks.Plows)
            {
                if (plow == null || !string.Equals(plow.Kind, "plow", StringComparison.Ordinal)) continue;
                if (!string.IsNullOrWhiteSpace(order.PreferredPlowAssetId)
                    && string.Equals(plow.AssetId, order.PreferredPlowAssetId, StringComparison.Ordinal))
                    preferred = plow;
                if (chosen == null && plow.IsUsable && !plow.IsReserved) chosen = plow;
            }
            if (preferred != null && !preferred.IsUsable && chosen != null && chosen != preferred)
            {
                Log(hooks, order.OrderId + ": preferred plow '" + preferred.AssetId + "' is wrecked (condition "
                    + preferred.Condition01.ToString("0.00") + ") — using legitimate alternative '" + chosen.AssetId + "'.");
            }
            if (chosen == null)
            {
                bool anyUsable = false;
                bool anyBusy = false;
                foreach (EquipmentAsset plow in hooks.Plows)
                {
                    if (plow == null || !string.Equals(plow.Kind, "plow", StringComparison.Ordinal)) continue;
                    if (plow.IsUsable) { anyUsable = true; if (plow.IsReserved) anyBusy = true; }
                }
                if (anyUsable && anyBusy)
                {
                    order.WaitReason = "every usable plow is in use — waiting for one to free up.";
                    Log(hooks, order.OrderId + ": " + order.WaitReason);
                    return;
                }
                BlockOrder(order, "no usable plow — every plow is wrecked (condition <= 0.05) or missing. Repair a plow first; no phantom plow will be conjured.", hooks);
                return;
            }

            // 2. Assemble the draft unit (team + driver + harness + implement).
            //    Fails loudly (null) when the team, driver, or harness is missing —
            //    the order waits, it is not failed.
            var diagnostics = new List<string>();
            DraftWorkUnit unit = DraftWorkUnitService.AssemblePlowUnit(
                hooks.DraftPower, hooks.AnimalRegistry,
                hooks.AvailableHorses, hooks.AvailableDrivers,
                chosen, hooks.Harness, "plowing " + order.FieldId, diagnostics);
            foreach (string d in diagnostics) Log(hooks, d);
            if (unit == null)
            {
                order.WaitReason = "no draft team/driver/harness available — waiting.";
                Log(hooks, order.OrderId + ": " + order.WaitReason);
                return;
            }

            // 3. Real work task for the plowing, assigned to the driver.
            string defId = "farm:plow:" + order.FieldId + ":" + order.OrderId;
            int baseMinutes = Mathf.Max(1, Mathf.CeilToInt(order.Acres * MinutesPerAcre));
            TaskDefinition def = hooks.Tasks.GetDefinition(defId);
            if (def == null)
            {
                def = new TaskDefinition(defId, "Plow " + order.Acres + " acres (" + order.FieldId + ")", baseMinutes);
                hooks.Tasks.RegisterDefinition(def, out _);
            }
            WorkTask task = hooks.Tasks.CreateTask(defId, EntityId.For(EntityKind.Business, 1), hooks.DayIndex);
            if (!hooks.Tasks.AssignTask(task.TaskId, unit.DriverPersonId, hooks.Budgets, out string assignReason))
            {
                DraftWorkUnitService.ReleaseUnit(unit, hooks.DraftPower);
                order.WaitReason = "driver " + unit.DriverPersonId + " could not take the task (" + assignReason + ") — waiting.";
                Log(hooks, order.OrderId + ": " + order.WaitReason);
                return;
            }
            if (!hooks.Tasks.StartTask(task.TaskId, hooks.DayIndex, out string startReason))
            {
                hooks.Tasks.CancelTask(task.TaskId, hooks.Budgets);
                DraftWorkUnitService.ReleaseUnit(unit, hooks.DraftPower);
                order.WaitReason = "task could not start (" + startReason + ") — waiting.";
                return;
            }

            order.Status = PlowOrderStatus.InProgress;
            order.AssignedPlowAssetId = chosen.AssetId;
            order.UnitId = unit.UnitId;
            order.TeamAnimalIds = new List<EntityId>(unit.TeamAnimalIds);
            order.DriverPersonId = unit.DriverPersonId;
            order.TaskId = task.TaskId;
            activeUnits[order.OrderId] = unit;
            if (fields.TryGetValue(order.FieldId, out CropFieldState field))
            {
                field.CurrentOperation = "plowing";
                field.WorkProgress01 = 0f;
            }
            Log(hooks, order.OrderId + ": started — plow '" + chosen.AssetId + "', driver " + unit.DriverPersonId
                + ", team of " + unit.TeamAnimalIds.Count + " horses, " + baseMinutes + " min for " + order.Acres + " acres.");
        }

        private void BlockOrder(PlowWorkOrder order, string why, PlowWorkHooks hooks)
        {
            order.Status = PlowOrderStatus.Blocked;
            order.BlockedReason = why;
            Log(hooks, order.OrderId + " BLOCKED: " + why);
        }

        private void UpdateFieldProgress(PlowWorkOrder order, WorkTask task)
        {
            float frac = task.PlannedMinutes > 0 ? Mathf.Clamp01((float)task.MinutesWorked / task.PlannedMinutes) : 0f;
            order.AcresPlowed = order.Acres * frac;
            if (fields.TryGetValue(order.FieldId, out CropFieldState field))
            {
                field.WorkProgress01 = frac;
            }
        }

        private bool FinishOrder(PlowWorkOrder order, PlowWorkHooks hooks, out string reason)
        {
            reason = string.Empty;
            if (activeUnits.TryGetValue(order.OrderId, out DraftWorkUnit unit))
            {
                var diagnostics = new List<string>();
                DraftWorkUnitService.WorkPlowSession(
                    unit, hooks.AnimalRegistry, order.Acres, order.Acres * WearPerAcre01, diagnostics);
                DraftWorkUnitService.ReleaseUnit(unit, hooks.DraftPower);
                activeUnits.Remove(order.OrderId);
                foreach (string d in diagnostics) Log(hooks, d);
            }
            order.Status = PlowOrderStatus.Done;
            order.AcresPlowed = order.Acres;
            if (fields.TryGetValue(order.FieldId, out CropFieldState field))
            {
                field.CurrentOperation = string.Empty; // plowed — ready for planting
                field.WorkProgress01 = 1f;
            }
            Log(hooks, order.OrderId + ": done — " + order.Acres + " acres plowed with '" + order.AssignedPlowAssetId + "'.");
            return true;
        }
    }
}
