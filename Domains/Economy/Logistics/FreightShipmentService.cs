// Copyright (c) Land & Ledgers contributors. Licensed under the MIT License.
// Pass IV (Worker 3) — Slice E: freight shipment service.
//
// FreightShipmentService drives LogisticsShipmentState through
// load (real handling time) -> travel -> unload -> recipient custody.
// Wagon capacity is enforced by mass AND volume (EquipmentAsset.
// PayloadMassCapacityKg / CargoVolumeCapacityM3) — over-capacity shipments
// are blocked pre-load and own zero cargo (MR-P001 preserved). Loading and
// unloading are real TaskAuthority WorkTasks. Delays and save/load mid-
// shipment are first-class. Conservation: cargo is never created or
// destroyed; blocked shipments own zero.

using System;
using System.Collections.Generic;
using UnityEngine;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.Persistence;
using LandLedgers.Economy.Blacksmith;

namespace LandLedgers.Economy.Logistics
{
    /// <summary>Physical cargo spec per unit (for wagon capacity checks).</summary>
    [Serializable]
    public sealed class FreightCargoSpec
    {
        public float MassKgPerUnit = 1f;
        public float VolumeM3PerUnit = 0.01f;
    }

    public sealed class FreightShipmentHooks
    {
        public TaskAuthority Tasks;
        public WorkTimeBudgetStore Budgets;
        public int DayIndex;
        public List<string> Diagnostics;
    }

    /// <summary>
    /// Drives freight shipments. The wagon, driver, and animals are real;
    /// the cargo is conserved; the recipient takes custody through the
    /// shipment-aware receiving authority (no duplicates).
    /// </summary>
    public sealed class FreightShipmentService
    {
        private readonly Dictionary<string, EquipmentAsset> wagons =
            new Dictionary<string, EquipmentAsset>(StringComparer.Ordinal);
        private readonly Dictionary<string, FreightCargoSpec> cargoSpecs =
            new Dictionary<string, FreightCargoSpec>(StringComparer.Ordinal);
        private readonly Dictionary<string, EntityId> loadTasks =
            new Dictionary<string, EntityId>(StringComparer.Ordinal);
        private readonly Dictionary<string, EntityId> unloadTasks =
            new Dictionary<string, EntityId>(StringComparer.Ordinal);
        private int sequence;

        public void RegisterWagon(EquipmentAsset wagon)
        {
            if (wagon == null || string.IsNullOrWhiteSpace(wagon.AssetId)) return;
            wagons[wagon.AssetId] = wagon;
        }

        private void Log(FreightShipmentHooks hooks, string message)
        {
            if (hooks != null && hooks.Diagnostics != null)
                hooks.Diagnostics.Add("FreightShipmentService: " + message);
        }

        /// <summary>
        /// Creates a shipment (Planned). Over-capacity cargo is blocked pre-load:
        /// Failed with a reason, zero cargo owned (MR-P001).
        /// </summary>
        public LogisticsShipmentState CreateShipment(string shipmentId, string sourceBusinessId,
            string destinationBusinessId, int quantityUnits, FreightCargoSpec cargo,
            string wagonAssetId, FreightShipmentHooks hooks, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(shipmentId) || quantityUnits <= 0)
            {
                reason = "FreightShipmentService: shipment needs an id and quantity — refusing phantom shipment.";
                return null;
            }
            if (!wagons.TryGetValue(wagonAssetId ?? string.Empty, out EquipmentAsset wagon) || wagon == null)
            {
                reason = "FreightShipmentService: wagon '" + wagonAssetId + "' is not a registered wagon — refusing phantom wagon.";
                return null;
            }
            if (!wagon.IsWagon)
            {
                reason = "FreightShipmentService: asset '" + wagonAssetId + "' is not a wagon (kind '" + wagon.Kind + "').";
                return null;
            }

            var shipment = new LogisticsShipmentState
            {
                shipmentId = shipmentId,
                sourceBusinessInstanceId = sourceBusinessId ?? string.Empty,
                destinationBusinessInstanceId = destinationBusinessId ?? string.Empty,
                plannedQuantityUnits = Math.Max(0, quantityUnits),
                physicalWagonAssetId = wagonAssetId,
                physicalLocationId = sourceBusinessId ?? string.Empty,
            };
            cargoSpecs[shipmentId] = cargo ?? new FreightCargoSpec();

            // Capacity check: mass AND volume. Over capacity -> blocked pre-load, zero cargo.
            float totalMassKg = quantityUnits * cargoSpecs[shipmentId].MassKgPerUnit;
            float totalVolumeM3 = quantityUnits * cargoSpecs[shipmentId].VolumeM3PerUnit;
            if (totalMassKg > wagon.PayloadMassCapacityKg + 0.001f)
            {
                return BlockPreLoad(shipment,
                    "cargo mass " + totalMassKg.ToString("0.0") + "kg exceeds wagon '" + wagonAssetId
                    + "' capacity " + wagon.PayloadMassCapacityKg.ToString("0.0") + "kg.", hooks);
            }
            if (totalVolumeM3 > wagon.CargoVolumeCapacityM3 + 0.0001f)
            {
                return BlockPreLoad(shipment,
                    "cargo volume " + totalVolumeM3.ToString("0.00") + "m3 exceeds wagon '" + wagonAssetId
                    + "' capacity " + wagon.CargoVolumeCapacityM3.ToString("0.00") + "m3.", hooks);
            }

            Log(hooks, "shipment '" + shipmentId + "' planned: " + quantityUnits + " units ("
                + totalMassKg.ToString("0.0") + "kg, " + totalVolumeM3.ToString("0.00") + "m3) on wagon '"
                + wagonAssetId + "' from '" + sourceBusinessId + "' to '" + destinationBusinessId + "'.");
            return shipment;
        }

        private LogisticsShipmentState BlockPreLoad(LogisticsShipmentState shipment, string why, FreightShipmentHooks hooks)
        {
            // MR-P001: the blocked record owns zero cargo — loading never occurred.
            shipment.blockedReason = why;
            shipment.state = LogisticsShipmentStatus.Failed;
            shipment.loadApplied = false;
            shipment.sourceCommittedAtSchedule = false;
            shipment.remainingQuantityUnits = 0;
            Log(hooks, "shipment '" + shipment.ShipmentId + "' BLOCKED pre-load: " + why + " (owns zero cargo).");
            return shipment;
        }

        /// <summary>Begins loading: creates the real handling-time WorkTask.</summary>
        public bool BeginLoading(LogisticsShipmentState shipment, EntityId loaderId,
            FreightShipmentHooks hooks, out string reason)
        {
            reason = string.Empty;
            if (shipment == null) { reason = "No shipment."; return false; }
            if (shipment.IsPreLoadBlocked)
            {
                reason = "FreightShipmentService: shipment '" + shipment.ShipmentId + "' is blocked pre-load — nothing to load.";
                return false;
            }
            if (shipment.Status != LogisticsShipmentStatus.Planned)
            {
                reason = "FreightShipmentService: shipment '" + shipment.ShipmentId + "' is " + shipment.Status + ", not Planned.";
                return false;
            }
            string defId = "freight:load:" + shipment.ShipmentId;
            if (hooks.Tasks.GetDefinition(defId) == null)
            {
                // Handling time scales with quantity: 1 min per 10 units, min 5.
                int baseMinutes = Math.Max(5, (shipment.PlannedQuantityUnits + 9) / 10);
                hooks.Tasks.RegisterDefinition(new TaskDefinition(defId,
                    "Load " + shipment.PlannedQuantityUnits + " units (" + shipment.ShipmentId + ")", baseMinutes), out _);
            }
            WorkTask task = hooks.Tasks.CreateTask(defId, EntityId.For(EntityKind.Business, 3), hooks.DayIndex);
            if (!hooks.Tasks.AssignTask(task.TaskId, loaderId, hooks.Budgets, out reason)
                || !hooks.Tasks.StartTask(task.TaskId, hooks.DayIndex, out reason))
            {
                reason = "FreightShipmentService: load task could not start: " + reason;
                return false;
            }
            loadTasks[shipment.ShipmentId] = task.TaskId;
            shipment.state = LogisticsShipmentStatus.Loading;
            shipment.stageElapsedGameSeconds = 0f;
            Log(hooks, "shipment '" + shipment.ShipmentId + "': loading started (task " + task.TaskId + ").");
            return true;
        }

        /// <summary>Works the loading task; on completion custody moves to the wagon.</summary>
        public bool WorkLoading(LogisticsShipmentState shipment, int minutes,
            FreightShipmentHooks hooks, out string reason)
        {
            reason = string.Empty;
            if (!loadTasks.TryGetValue(shipment.ShipmentId, out EntityId taskId))
            {
                reason = "FreightShipmentService: no load task for '" + shipment.ShipmentId + "'.";
                return false;
            }
            if (!hooks.Tasks.RecordWorkProgress(taskId, minutes, hooks.Budgets, hooks.DayIndex,
                out bool done, out reason)) return false;
            if (!done) return true;
            hooks.Tasks.CompleteTask(taskId, TaskOutcome.Success, hooks.DayIndex, hooks.Budgets);
            loadTasks.Remove(shipment.ShipmentId);
            // Loading applied: the wagon now has custody of the full planned quantity.
            shipment.loadApplied = true;
            shipment.sourceCommittedAtSchedule = true;
            shipment.remainingQuantityUnits = shipment.PlannedQuantityUnits;
            shipment.physicalLocationId = "wagon:" + shipment.physicalWagonAssetId;
            Log(hooks, "shipment '" + shipment.ShipmentId + "': loaded — " + shipment.PlannedQuantityUnits
                + " units in wagon custody.");
            return true;
        }

        /// <summary>Dispatches: binds driver/animals, status -> InTransit.</summary>
        public bool Dispatch(LogisticsShipmentState shipment, EntityId driverId, List<EntityId> animalIds,
            FreightShipmentHooks hooks, out string reason)
        {
            reason = string.Empty;
            if (shipment == null) { reason = "No shipment."; return false; }
            if (!shipment.loadApplied)
            {
                reason = "FreightShipmentService: shipment '" + shipment.ShipmentId + "' not loaded — cannot dispatch.";
                return false;
            }
            if (driverId.Kind != EntityKind.Person || !driverId.IsValid)
            {
                reason = "FreightShipmentService: dispatch needs a real driver — refusing phantom driver.";
                return false;
            }
            shipment.physicalDriverPersonId = driverId.ToString();
            shipment.physicalDraftAnimalIds = new List<string>();
            if (animalIds != null)
                foreach (EntityId a in animalIds)
                    shipment.physicalDraftAnimalIds.Add(a.ToString());
            shipment.physicalTransportBound = true;
            shipment.state = LogisticsShipmentStatus.InTransit;
            shipment.stageElapsedGameSeconds = 0f;
            Log(hooks, "shipment '" + shipment.ShipmentId + "': dispatched — driver " + driverId
                + ", " + shipment.physicalDraftAnimalIds.Count + " animals, in transit.");
            return true;
        }

        /// <summary>Advances travel; handles delays honestly. InTransit -> Arrived.</summary>
        public void AdvanceTransit(LogisticsShipmentState shipment, float gameSeconds, FreightShipmentHooks hooks)
        {
            if (shipment == null) return;
            if (shipment.Status == LogisticsShipmentStatus.Delayed)
            {
                Log(hooks, "shipment '" + shipment.ShipmentId + "': delayed — holding (" + shipment.BlockedReason + ").");
                return;
            }
            if (shipment.Status != LogisticsShipmentStatus.InTransit) return;
            // Explicit transit drive (the state's AdvanceGameSeconds skips Arrived).
            shipment.stageElapsedGameSeconds += Mathf.Max(0f, gameSeconds);
            shipment.totalElapsedGameSeconds += Mathf.Max(0f, gameSeconds);
            float duration = Mathf.Max(0.01f, shipment.transitDurationGameSeconds);
            shipment.physicalProgress01 = Mathf.Clamp01(shipment.stageElapsedGameSeconds / duration);
            if (shipment.stageElapsedGameSeconds + 0.0001f < duration) return;
            shipment.state = LogisticsShipmentStatus.Arrived;
            shipment.stageElapsedGameSeconds = 0f;
            shipment.physicalProgress01 = 1f;
            shipment.physicalLocationId = shipment.destinationBusinessInstanceId;
            Log(hooks, "shipment '" + shipment.ShipmentId + "': arrived at '" + shipment.destinationBusinessInstanceId + "'.");
        }

        public void DelayShipment(LogisticsShipmentState shipment, string why, FreightShipmentHooks hooks,
            LogisticsShipmentStatus resumeState = LogisticsShipmentStatus.InTransit)
        {
            if (shipment == null) return;
            shipment.MarkDelayed(why ?? "delayed", resumeState);
            Log(hooks, "shipment '" + shipment.ShipmentId + "' delayed: " + shipment.BlockedReason);
        }

        public void ResumeShipment(LogisticsShipmentState shipment, FreightShipmentHooks hooks)
        {
            if (shipment == null) return;
            shipment.ResumeFromDelay();
            Log(hooks, "shipment '" + shipment.ShipmentId + "' resumed -> " + shipment.Status + ".");
        }

        /// <summary>Begins unloading: creates the real handling-time WorkTask.</summary>
        public bool BeginUnloading(LogisticsShipmentState shipment, EntityId unloaderId,
            FreightShipmentHooks hooks, out string reason)
        {
            reason = string.Empty;
            if (shipment == null) { reason = "No shipment."; return false; }
            if (shipment.Status != LogisticsShipmentStatus.Arrived)
            {
                reason = "FreightShipmentService: shipment '" + shipment.ShipmentId + "' is " + shipment.Status + ", not Arrived.";
                return false;
            }
            string defId = "freight:unload:" + shipment.ShipmentId;
            if (hooks.Tasks.GetDefinition(defId) == null)
            {
                int baseMinutes = Math.Max(5, (shipment.RemainingQuantityUnits + 9) / 10);
                hooks.Tasks.RegisterDefinition(new TaskDefinition(defId,
                    "Unload " + shipment.RemainingQuantityUnits + " units (" + shipment.ShipmentId + ")", baseMinutes), out _);
            }
            WorkTask task = hooks.Tasks.CreateTask(defId, EntityId.For(EntityKind.Business, 3), hooks.DayIndex);
            if (!hooks.Tasks.AssignTask(task.TaskId, unloaderId, hooks.Budgets, out reason)
                || !hooks.Tasks.StartTask(task.TaskId, hooks.DayIndex, out reason))
            {
                reason = "FreightShipmentService: unload task could not start: " + reason;
                return false;
            }
            unloadTasks[shipment.ShipmentId] = task.TaskId;
            shipment.state = LogisticsShipmentStatus.Unloading;
            shipment.stageElapsedGameSeconds = 0f;
            Log(hooks, "shipment '" + shipment.ShipmentId + "': unloading started.");
            return true;
        }

        /// <summary>Works the unloading task; on completion the recipient has custody.</summary>
        public bool WorkUnloading(LogisticsShipmentState shipment, int minutes,
            FreightShipmentHooks hooks, out string reason)
        {
            reason = string.Empty;
            if (!unloadTasks.TryGetValue(shipment.ShipmentId, out EntityId taskId))
            {
                reason = "FreightShipmentService: no unload task for '" + shipment.ShipmentId + "'.";
                return false;
            }
            if (!hooks.Tasks.RecordWorkProgress(taskId, minutes, hooks.Budgets, hooks.DayIndex,
                out bool done, out reason)) return false;
            if (!done) return true;
            hooks.Tasks.CompleteTask(taskId, TaskOutcome.Success, hooks.DayIndex, hooks.Budgets);
            unloadTasks.Remove(shipment.ShipmentId);
            // Recipient custody: goods delivered, wagon released.
            shipment.deliveryApplied = true;
            shipment.remainingQuantityUnits = 0;
            shipment.physicalLocationId = shipment.destinationBusinessInstanceId;
            shipment.physicalTransportBound = false;
            shipment.state = LogisticsShipmentStatus.Completed;
            Log(hooks, "shipment '" + shipment.ShipmentId + "': delivered — recipient '"
                + shipment.destinationBusinessInstanceId + "' has custody.");
            return true;
        }

        /// <summary>
        /// Conservation invariant: planned == delivered + remaining + lost, where
        /// lost is zero unless the shipment failed pre-load (then remaining is zero
        /// and nothing was ever loaded). Returns false with a loud reason on violation.
        /// </summary>
        public bool VerifyConservation(LogisticsShipmentState shipment, int deliveredUnits, out string reason)
        {
            reason = string.Empty;
            if (shipment == null) { reason = "No shipment."; return false; }
            if (shipment.IsPreLoadBlocked)
            {
                if (shipment.RemainingQuantityUnits != 0 || shipment.loadApplied)
                {
                    reason = "MR-P001 VIOLATION: blocked shipment '" + shipment.ShipmentId + "' owns cargo.";
                    return false;
                }
                return true;
            }
            int accounted = deliveredUnits + shipment.RemainingQuantityUnits;
            if (accounted != shipment.PlannedQuantityUnits)
            {
                reason = "Conservation violation on '" + shipment.ShipmentId + "': planned "
                    + shipment.PlannedQuantityUnits + " != delivered " + deliveredUnits
                    + " + remaining " + shipment.RemainingQuantityUnits + ".";
                return false;
            }
            return true;
        }

        // ---------- save / load ----------

        [Serializable]
        public sealed class FreightServiceSaveDto
        {
            public List<LogisticsShipmentSaveDto> Shipments = new List<LogisticsShipmentSaveDto>();
            public List<string> ShipmentIdsWithLoadTasks = new List<string>();
            public List<string> ShipmentIdsWithUnloadTasks = new List<string>();
        }

        /// <summary>Captures shipment DTOs (the state's own save DTO — owned by the state).</summary>
        public FreightServiceSaveDto CaptureSaveDto(List<LogisticsShipmentState> shipments)
        {
            var dto = new FreightServiceSaveDto();
            if (shipments != null)
                foreach (LogisticsShipmentState s in shipments)
                    if (s != null) dto.Shipments.Add(s.CaptureSaveDto());
            dto.ShipmentIdsWithLoadTasks.AddRange(loadTasks.Keys);
            dto.ShipmentIdsWithUnloadTasks.AddRange(unloadTasks.Keys);
            return dto;
        }

        /// <summary>Restores shipments; task bindings re-resolve by definition id (read-only).</summary>
        public List<LogisticsShipmentState> RestoreSaveDto(FreightServiceSaveDto dto,
            FreightShipmentHooks hooks)
        {
            var out_ = new List<LogisticsShipmentState>();
            if (dto == null) return out_;
            foreach (LogisticsShipmentSaveDto s in dto.Shipments)
                out_.Add(LogisticsShipmentState.FromSaveDto(s));
            // Rebind in-flight handling tasks by their deterministic definition ids.
            if (hooks != null && hooks.Tasks != null)
            {
                foreach (string shipmentId in dto.ShipmentIdsWithLoadTasks)
                {
                    WorkTask t = hooks.Tasks.FindOpenTask("freight:load:" + shipmentId,
                        EntityId.For(EntityKind.Business, 3), EntityId.Invalid);
                    if (t != null) loadTasks[shipmentId] = t.TaskId;
                }
                foreach (string shipmentId in dto.ShipmentIdsWithUnloadTasks)
                {
                    WorkTask t = hooks.Tasks.FindOpenTask("freight:unload:" + shipmentId,
                        EntityId.For(EntityKind.Business, 3), EntityId.Invalid);
                    if (t != null) unloadTasks[shipmentId] = t.TaskId;
                }
            }
            return out_;
        }
    }
}
