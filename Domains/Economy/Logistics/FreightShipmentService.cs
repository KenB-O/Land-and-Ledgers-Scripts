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
using LandLedgers.Economy.Equipment;
using EntityId = LandLedgers.Primitives.EntityId;

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

        /// <summary>
        /// PASS7-008: recipient acceptance for an unloaded shipment. Returns the
        /// accepted unit count (0..quantityUnits). The authority behind it must be
        /// the shipment-aware receiving authority (idempotent: a repeated callback
        /// for the same shipment id must not duplicate inventory). Null means no
        /// receiving authority is wired: the shipment is held as Delayed rather
        /// than falsely marked delivered.
        /// </summary>
        public ReceiveFreightShipmentHandler ReceiveShipment;

        /// <summary>
        /// Pass IX WS3 (A4-13): the existing unified reservation authority
        /// (EquipmentReservationService via the hub). When wired, Dispatch
        /// atomically reserves the wagon, driver and draft animals through
        /// it, and completion releases the hold. When null, exclusivity is
        /// still enforced across the service's own live shipments — the
        /// authority ADDS cross-system enforcement (a wagon held by a plow
        /// task cannot haul), it never replaces the live-shipment check.
        /// </summary>
        public IUnifiedReservationAuthority Reservations;

        /// <summary>Absolute game seconds for reservation windows (TimeManager timeline).</summary>
        public float NowGameSeconds;
    }

    /// <summary>
    /// Recipient acceptance callback: (shipment, quantityUnits, reason) -> accepted units.
    /// </summary>
    public delegate int ReceiveFreightShipmentHandler(
        LogisticsShipmentState shipment, int quantityUnits, out string reason);

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
        // Pass IX WS3 (A4-13): every shipment this service created or restored,
        // so exclusivity can be checked ACROSS live shipments. Terminal
        // shipments (Completed/Failed) hold nothing and never conflict.
        private readonly List<LogisticsShipmentState> trackedShipments =
            new List<LogisticsShipmentState>();
        // The reservation authority each dispatched shipment reserved through,
        // so completion releases the exact hold that was taken.
        private readonly Dictionary<string, IUnifiedReservationAuthority> dispatchAuthorities =
            new Dictionary<string, IUnifiedReservationAuthority>(StringComparer.Ordinal);
        private int sequence;

        /// <summary>All shipments known to this service (created or restored).</summary>
        public IReadOnlyList<LogisticsShipmentState> TrackedShipments => trackedShipments;

        /// <summary>Pass IX WS3 (A4-13): a shipment is live while it can still hold a wagon, driver or animal.</summary>
        public static bool IsLiveShipment(LogisticsShipmentState shipment)
        {
            if (shipment == null) return false;
            return shipment.Status != LogisticsShipmentStatus.Completed
                && shipment.Status != LogisticsShipmentStatus.Failed;
        }

        private void Track(LogisticsShipmentState shipment)
        {
            if (shipment == null) return;
            foreach (LogisticsShipmentState existing in trackedShipments)
            {
                if (ReferenceEquals(existing, shipment)
                    || string.Equals(existing.ShipmentId, shipment.ShipmentId, StringComparison.Ordinal))
                    return;
            }
            trackedShipments.Add(shipment);
        }

        /// <summary>
        /// Pass IX WS3 (A4-13): finds a live-shipment exclusivity conflict for
        /// the named wagon / driver / animals, excluding the candidate itself.
        /// Returns a loud reason, or null when everything is free. A wagon is
        /// bound from creation (Planned) until a terminal state; a driver or
        /// animal is bound once the shipment's transport is bound (dispatched)
        /// until a terminal state — cargo in wagon custody (including a
        /// refused delivery held as Delayed, and a Partial remainder) keeps
        /// its wagon bound.
        /// </summary>
        public string FindExclusivityConflict(
            LogisticsShipmentState candidate, string wagonAssetId,
            EntityId driverId, List<EntityId> animalIds)
        {
            string candidateId = candidate != null ? candidate.ShipmentId : string.Empty;
            foreach (LogisticsShipmentState other in trackedShipments)
            {
                if (other == null || !IsLiveShipment(other))
                    continue;
                if (string.Equals(other.ShipmentId, candidateId, StringComparison.Ordinal))
                    continue;
                if (!string.IsNullOrWhiteSpace(wagonAssetId)
                    && string.Equals(other.physicalWagonAssetId, wagonAssetId, StringComparison.Ordinal))
                {
                    return $"wagon '{wagonAssetId}' is already bound to live shipment " +
                        $"'{other.ShipmentId}' ({other.Status}) — no double-booking (A4-13).";
                }
                if (driverId.IsValid && other.physicalTransportBound
                    && string.Equals(other.physicalDriverPersonId, driverId.ToString(), StringComparison.Ordinal))
                {
                    return $"driver {driverId} is already bound to live shipment " +
                        $"'{other.ShipmentId}' ({other.Status}) — no double-booking (A4-13).";
                }
                if (animalIds != null && other.physicalTransportBound && other.physicalDraftAnimalIds != null)
                {
                    foreach (EntityId animal in animalIds)
                    {
                        if (!animal.IsValid) continue;
                        if (other.physicalDraftAnimalIds.Contains(animal.ToString()))
                        {
                            return $"draft animal {animal} is already bound to live shipment " +
                                $"'{other.ShipmentId}' ({other.Status}) — no double-booking (A4-13).";
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Pass IX WS3 (A4-13): releases the reservation-authority hold taken
        /// at Dispatch for a shipment that reached a terminal state (or was
        /// abandoned by its driver). Idempotent.
        /// </summary>
        public void ReleaseShipmentReservations(LogisticsShipmentState shipment)
        {
            if (shipment == null) return;
            if (dispatchAuthorities.TryGetValue(shipment.ShipmentId, out IUnifiedReservationAuthority authority)
                && authority != null)
            {
                authority.ReleaseAllForTask("freight:" + shipment.ShipmentId);
            }
            dispatchAuthorities.Remove(shipment.ShipmentId);
        }

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
            // Pass IX WS3 (A4-13): the wagon cannot already haul for another
            // live shipment, and cannot be held by another task through the
            // reservation authority when one is wired.
            string wagonConflict = FindExclusivityConflict(null, wagonAssetId, EntityId.Invalid, null);
            if (wagonConflict != null)
            {
                reason = "FreightShipmentService: " + wagonConflict;
                return null;
            }
            if (hooks != null && hooks.Reservations != null
                && hooks.Reservations.IsReserved(ReservationKeyConventions.Asset(wagonAssetId), hooks.NowGameSeconds))
            {
                reason = "FreightShipmentService: wagon '" + wagonAssetId + "' is reserved through the " +
                    "reservation authority by another holder — no double-booking (A4-13).";
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
            Track(shipment);
            return shipment;
        }

        private LogisticsShipmentState BlockPreLoad(LogisticsShipmentState shipment, string why, FreightShipmentHooks hooks)
        {
            Track(shipment);
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
            // Pass IX WS3 (A4-13): re-check the wagon at loading — a shipment
            // restored from a legacy save (or created before another claim)
            // must not start loading onto a wagon another live shipment holds.
            Track(shipment);
            string loadConflict = FindExclusivityConflict(shipment, shipment.physicalWagonAssetId, EntityId.Invalid, null);
            if (loadConflict != null)
            {
                reason = "FreightShipmentService: " + loadConflict;
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
            // Pass IX WS3 (A4-13): wagon, driver and animals are exclusive
            // across live shipments — checked against this service's tracked
            // shipments AND, when wired, atomically reserved through the
            // existing reservation authority so no other system can take
            // them mid-haul either.
            Track(shipment);
            string conflict = FindExclusivityConflict(
                shipment, shipment.physicalWagonAssetId, driverId, animalIds);
            if (conflict != null)
            {
                reason = "FreightShipmentService: " + conflict;
                return false;
            }
            if (hooks != null && hooks.Reservations != null)
            {
                var request = new ReservationRequest(
                    "freight:" + shipment.ShipmentId, "freight haul " + shipment.ShipmentId);
                request.Items.Add(new ReservationItem(
                    ReservationKeyConventions.Asset(shipment.physicalWagonAssetId),
                    1f, hooks.NowGameSeconds, float.MaxValue));
                request.Items.Add(new ReservationItem(
                    ReservationKeyConventions.Person(driverId.ToString()),
                    1f, hooks.NowGameSeconds, float.MaxValue));
                if (animalIds != null)
                {
                    foreach (EntityId animal in animalIds)
                    {
                        if (animal.IsValid)
                            request.Items.Add(new ReservationItem(
                                ReservationKeyConventions.Animal(animal.ToString()),
                                1f, hooks.NowGameSeconds, float.MaxValue));
                    }
                }
                string refusal = hooks.Reservations.TryReserveAll(request, out _);
                if (refusal != null)
                {
                    reason = "FreightShipmentService: reservation authority refused dispatch — " + refusal;
                    return false;
                }
                dispatchAuthorities[shipment.ShipmentId] = hooks.Reservations;
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

        /// <summary>Begins unloading: creates the real handling-time WorkTask.
        /// A partially received shipment (Partial, delivery not applied, no open
        /// unload task) may begin unloading again to retry the remainder.</summary>
        public bool BeginUnloading(LogisticsShipmentState shipment, EntityId unloaderId,
            FreightShipmentHooks hooks, out string reason)
        {
            reason = string.Empty;
            if (shipment == null) { reason = "No shipment."; return false; }
            bool resumingPartial = shipment.Status == LogisticsShipmentStatus.Partial
                && !shipment.deliveryApplied
                && !unloadTasks.ContainsKey(shipment.ShipmentId);
            if (shipment.Status != LogisticsShipmentStatus.Arrived && !resumingPartial)
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

        /// <summary>
        /// Works the unloading task; on completion the recipient is asked to accept
        /// the goods through the wired receiving authority (PASS7-008). The shipment
        /// completes ONLY when the recipient accepts the full remaining quantity:
        /// partial acceptance records exact accepted/remaining counts (Partial), a
        /// refusal keeps the goods in wagon custody (Delayed, retryable), and a
        /// missing receiving authority never produces a false delivery.
        /// </summary>
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

            int remaining = Math.Max(0, shipment.remainingQuantityUnits);
            if (remaining <= 0)
            {
                // Nothing to hand over: the transport completed with zero cargo.
                shipment.deliveryApplied = true;
                shipment.physicalLocationId = shipment.destinationBusinessInstanceId;
                shipment.physicalTransportBound = false;
                shipment.state = LogisticsShipmentStatus.Partial;
                Log(hooks, "shipment '" + shipment.ShipmentId + "': unload complete with zero cargo — nothing to deliver.");
                return true;
            }

            if (hooks == null || hooks.ReceiveShipment == null)
            {
                // No receiving authority wired: hold the goods in wagon custody.
                // Marking this delivered would be the PASS7-008 mask.
                shipment.deliveryApplied = false;
                shipment.MarkDelayed(
                    "no receiving authority wired — delivery cannot be verified; " + remaining + "u held in wagon custody",
                    LogisticsShipmentStatus.Arrived);
                Log(hooks, "shipment '" + shipment.ShipmentId + "': NOT delivered — " + shipment.BlockedReason + ".");
                return true;
            }

            int accepted = Math.Max(0, Math.Min(remaining, hooks.ReceiveShipment(shipment, remaining, out string receiveReason)));
            shipment.deliveredQuantityUnits += accepted;
            shipment.remainingQuantityUnits = remaining - accepted;
            if (accepted >= remaining)
            {
                // Recipient custody: goods accepted into inventory, wagon released.
                shipment.deliveryApplied = true;
                shipment.physicalLocationId = shipment.destinationBusinessInstanceId;
                shipment.physicalTransportBound = false;
                shipment.state = LogisticsShipmentStatus.Completed;
                ReleaseShipmentReservations(shipment);
                Log(hooks, "shipment '" + shipment.ShipmentId + "': delivered — recipient '"
                    + shipment.destinationBusinessInstanceId + "' accepted " + accepted + "u into inventory.");
                return true;
            }

            if (accepted > 0)
            {
                shipment.deliveryApplied = false;
                shipment.state = LogisticsShipmentStatus.Partial;
                shipment.blockedReason = "recipient accepted " + accepted + "/" + remaining + "u"
                    + (string.IsNullOrWhiteSpace(receiveReason) ? "" : " (" + receiveReason.Trim() + ")");
                Log(hooks, "shipment '" + shipment.ShipmentId + "': PARTIAL — " + shipment.blockedReason
                    + "; " + shipment.remainingQuantityUnits + "u remain in wagon custody (unload may resume).");
                return true;
            }

            shipment.deliveryApplied = false;
            shipment.MarkDelayed(
                string.IsNullOrWhiteSpace(receiveReason) ? "recipient refused shipment" : receiveReason.Trim(),
                LogisticsShipmentStatus.Arrived);
            Log(hooks, "shipment '" + shipment.ShipmentId + "': NOT delivered — " + shipment.BlockedReason
                + "; " + remaining + "u remain in wagon custody.");
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
            // PASS7-008: when the shipment tracks actual acceptance, the claimed
            // delivered count must equal it — completion cannot imply acceptance
            // the recipient never performed.
            if (shipment.DeliveredQuantityUnits > 0 && deliveredUnits != shipment.DeliveredQuantityUnits)
            {
                reason = "Acceptance mismatch on '" + shipment.ShipmentId + "': claimed delivered "
                    + deliveredUnits + " != accepted " + shipment.DeliveredQuantityUnits + ".";
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
            /// <summary>
            /// Pass XI WS4 save/load fix (Gap 2): ids of captured shipments
            /// that held a dispatch reservation through the reservation
            /// authority, so restore can rebind them. Additive; an old save
            /// leaves this empty and restore falls back to shipment state.
            /// </summary>
            public List<string> ShipmentIdsWithDispatchAuthorities = new List<string>();
        }

        /// <summary>Captures shipment DTOs (the state's own save DTO — owned by the state).</summary>
        public FreightServiceSaveDto CaptureSaveDto(List<LogisticsShipmentState> shipments)
        {
            var dto = new FreightServiceSaveDto();
            if (shipments != null)
                foreach (LogisticsShipmentState s in shipments)
                {
                    if (s == null) continue;
                    dto.Shipments.Add(s.CaptureSaveDto());
                    if (dispatchAuthorities.ContainsKey(s.ShipmentId))
                        dto.ShipmentIdsWithDispatchAuthorities.Add(s.ShipmentId);
                }
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
            {
                LogisticsShipmentState restored = LogisticsShipmentState.FromSaveDto(s);
                out_.Add(restored);
                // Pass IX WS3 (A4-13): restored shipments are tracked again so
                // exclusivity covers them exactly once — no duplicate holds,
                // no invisible live shipment after a mid-transport load.
                Track(restored);
            }
            // Pass XI WS4 save/load fix (Gap 2): rebind dispatch authorities
            // for restored shipments that were already dispatched and are not
            // yet terminal, so completion releases the wagon reservation
            // exactly once (ReleaseShipmentReservations is idempotent and
            // the map entry is removed on release, so a double restore can
            // only overwrite the same key with the same authority — never a
            // second hold). When the DTO carries the persisted list, only
            // listed shipments are bound (a shipment dispatched without an
            // authority holds nothing to release); an old save with an empty
            // list falls back to shipment state (transport bound + live),
            // which is exactly the dispatched-but-unfinished set.
            if (hooks != null && hooks.Reservations != null)
            {
                List<string> persisted = dto.ShipmentIdsWithDispatchAuthorities;
                bool hasPersisted = persisted != null && persisted.Count > 0;
                var persistedSet = hasPersisted
                    ? new HashSet<string>(persisted, StringComparer.Ordinal) : null;
                foreach (LogisticsShipmentState restored in out_)
                {
                    if (restored == null || !restored.physicalTransportBound || !IsLiveShipment(restored))
                        continue;
                    if (hasPersisted && !persistedSet.Contains(restored.ShipmentId))
                        continue;
                    dispatchAuthorities[restored.ShipmentId] = hooks.Reservations;
                }
            }
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
