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
using LandLedgers.Population;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Freight;
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
        /// Slice A2 (QA-05): the person-schedule calendar every other driver
        /// reserves through. When set, BeginLoading/BeginUnloading reserve the
        /// handler's window before creating the handling task (reserve-first);
        /// when null, the current behaviour is kept and one diagnostic names
        /// the missing calendar. Nullable-or-default so existing callers and
        /// tests keep compiling.
        /// </summary>
        public PersonScheduleTracker Schedules;

        /// <summary>
        /// Slice A2 (QA-05): the tick's current minute of day (0-1439), set by
        /// the hooks builder from the clock. The reservation start minute for
        /// the handler's window. No fixed handling hour exists in the driver;
        /// the contract is the clock's minute when the hooks are built
        /// (Agent 1 wiring: Floor(TimeOfDay01 * 1440), fallback 480).
        /// </summary>
        public int MinuteOfDay;

        /// <summary>
        /// Slice A3 (QA-12): the registered dispatch task id for the shipment's
        /// agreement, when one exists. Returns the TaskAuthority task id string
        /// (as stored on FreightServiceAgreement.DispatchTaskId), or null/empty
        /// when the shipment has no registered dispatch task. The service never
        /// references the agreement book directly; the hooks builder supplies
        /// the lookup from its own book. Null means no adoption is attempted
        /// and BeginLoading mints as before.
        /// </summary>
        public Func<LogisticsShipmentState, string> FindRegisteredDispatchTask;

        /// <summary>
        /// Slice A2 (QA-05): true when the hooks' caller already holds the
        /// handling crew's windows itself (ConstructionMaterialFreight
        /// reserves whole-day crew windows before driving the chain, so the
        /// service must not double-reserve sub-windows inside them and must
        /// not log the missing-calendar diagnostic). The service skips both
        /// the reserve and the diagnostic when this is set.
        /// </summary>
        public bool CrewWindowsReservedByCaller;

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
        /// Lot-custody path for cargo which already has a source inventory lot.
        /// The implementation must atomically debit shipment.CargoLotId (or return
        /// false without moving stock) and treat ShipmentId as an idempotency key.
        /// Legacy/construction shipments keep using ReceiveShipment unless they
        /// explicitly opt into RequiresSourceLotCustody.
        /// </summary>
        public CommitFreightSourceLotHandler CommitSourceLot;

        /// <summary>
        /// Atomically credits the same CargoLotId to the destination, or refuses
        /// without moving stock. The destination authority must persist receipt
        /// identity and refuse a second credit for this shipment/lot pair.
        /// </summary>
        public ReceiveFreightCargoLotHandler ReceiveCargoLot;

        /// <summary>
        /// Optional physical presentation gate for source-lot freight. When wired,
        /// the assigned handler must physically reach the destination receiving
        /// point before the receiving authority can transfer lot custody. A false
        /// result keeps the completed handling task open and the lot in wagon custody.
        /// </summary>
        public ConfirmFreightPhysicalHandlingHandler ConfirmPhysicalReceiving;
        public Action<LogisticsShipmentState, EntityId> OnPhysicalReceivingComplete;

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

        /// <summary>
        /// Slice S10: when set, AdvanceTick skips shipments whose wagon is
        /// off-map (the journey record is the hold — no new serialized field).
        /// Null means no hold: the pre-S10 behaviour.
        /// </summary>
        public Func<string, bool> IsVehicleOffMap;

        /// <summary>Absolute game seconds for reservation windows (TimeManager timeline).</summary>
        public float NowGameSeconds;
    }

    /// <summary>
    /// Recipient acceptance callback: (shipment, quantityUnits, reason) -> accepted units.
    /// </summary>
    public delegate int ReceiveFreightShipmentHandler(
        LogisticsShipmentState shipment, int quantityUnits, out string reason);

    public delegate bool CommitFreightSourceLotHandler(
        LogisticsShipmentState shipment, int quantityUnits, out string reason);

    public delegate bool ReceiveFreightCargoLotHandler(
        LogisticsShipmentState shipment, int quantityUnits, out string reason);

    public delegate bool ConfirmFreightPhysicalHandlingHandler(
        LogisticsShipmentState shipment, EntityId handlerId, out bool physicallyReceived, out string reason);

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
        // Slice A2 (QA-05): the schedule-window reservation id held per
        // shipment while its handling task is open, mirroring loadTasks /
        // unloadTasks. Transient: cleared on restore (the tracker's own
        // reservations persist via its save slot; release then falls back
        // to a same-day label lookup).
        private readonly Dictionary<string, string> loadReservations =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> unloadReservations =
            new Dictionary<string, string>(StringComparer.Ordinal);
        // Slice A1 (QA-04): consecutive daily ticks the physical-receiving
        // gate has held an unload task at zero minutes. A bound, not a
        // bypass: after PhysicalReceivingPinLimit pinned ticks the task is
        // closed with a failure outcome and the shipment is Delayed with the
        // gate's own reason, so the driver retry can re-attempt it. The gate
        // itself is never auto-passed. Service-side memory only: a save/load
        // resets the count (custody state is unaffected).
        public const int PhysicalReceivingPinLimit = 3;
        private readonly Dictionary<string, int> physicalReceivingPins =
            new Dictionary<string, int>(StringComparer.Ordinal);
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
        /// Slice S3: the owner is the shipment id itself, not a "freight:"
        /// prefix — the S3 draft-team bind (WagonAuthority.TryAssignDraftTeam,
        /// owner = the shipment id) and this dispatch hold are ONE logical
        /// holder for the same team, and the unified authority exempts
        /// same-owner holds from conflicting. A prefixed owner made Dispatch
        /// refuse its own team's animal key forever (capacity 1, overlapping
        /// 1) once the team was bound at dispatch completion. Releasing by
        /// shipment id also releases the bind's record at completion, when
        /// the haul is over; the horse/wagon mirror fields are still cleared
        /// by the terminal sweep (ReleaseDraftTeam).
        /// </summary>
        public void ReleaseShipmentReservations(LogisticsShipmentState shipment)
        {
            if (shipment == null) return;
            if (dispatchAuthorities.TryGetValue(shipment.ShipmentId, out IUnifiedReservationAuthority authority)
                && authority != null)
            {
                authority.ReleaseAllForTask(shipment.ShipmentId);
            }
            dispatchAuthorities.Remove(shipment.ShipmentId);
        }

        /// <summary>Slice A1 (QA-03): true while this shipment still holds a
        /// dispatch reservation through the unified authority.</summary>
        public bool HasDispatchReservation(LogisticsShipmentState shipment)
        {
            return shipment != null && dispatchAuthorities.ContainsKey(shipment.ShipmentId);
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

        /// <summary>Slice A2 (QA-05): reserve-first. Reserve the handler's
        /// window before the handling task is created. Returns false with a
        /// refusal when the calendar refuses; the caller then creates no task,
        /// commits no budget, and moves no cargo. When no calendar is wired
        /// the current behaviour is kept and one diagnostic names the missing
        /// calendar, unless the caller already holds the crew windows
        /// (CrewWindowsReservedByCaller). TryReserve generates the
        /// reservation id internally, so it is recovered from the tracker's
        /// active reservations for this person and day.</summary>
        private bool ReserveHandlerWindow(FreightShipmentHooks hooks, EntityId handlerId,
            string shipmentId, string label, int plannedMinutes,
            Dictionary<string, string> reservations, out string refusal)
        {
            refusal = string.Empty;
            if (hooks == null || hooks.Schedules == null || hooks.CrewWindowsReservedByCaller)
            {
                if (hooks != null && hooks.Schedules == null && !hooks.CrewWindowsReservedByCaller)
                    Log(hooks, $"shipment '{shipmentId}': no PersonScheduleTracker wired (Schedules is null) — handling proceeds without a reserved window.");
                return true;
            }
            refusal = hooks.Schedules.TryReserve(handlerId.Id, PersonActivityKind.Work,
                hooks.DayIndex, hooks.MinuteOfDay, plannedMinutes, label);
            if (refusal != null) return false;
            foreach (PersonActivityReservation reservation in hooks.Schedules.ActiveFor(handlerId.Id, hooks.DayIndex))
            {
                if (reservation.Label == label && reservation.StartMinuteOfDay == hooks.MinuteOfDay)
                {
                    reservations[shipmentId] = reservation.ReservationId;
                    break;
                }
            }
            return true;
        }

        /// <summary>Slice A2 (QA-05): release the handler's window for a
        /// shipment whose handling task just terminalized. Dictionary-first
        /// (the id stored at reserve time); falls back to a same-day label
        /// lookup, which is the post-restore path (the service-side maps are
        /// transient and cleared on restore). Never called while the task is
        /// still open. Release is idempotent on the tracker.</summary>
        private void ReleaseHandlerWindow(Dictionary<string, string> reservations,
            FreightShipmentHooks hooks, string shipmentId, EntityId handlerId, string label)
        {
            if (reservations != null && reservations.TryGetValue(shipmentId, out string reservationId))
            {
                hooks?.Schedules?.Release(reservationId);
                reservations.Remove(shipmentId);
                return;
            }
            if (hooks?.Schedules == null || !handlerId.IsValid) return;
            foreach (PersonActivityReservation reservation in hooks.Schedules.ActiveFor(handlerId.Id, hooks.DayIndex))
            {
                if (reservation.Label == label)
                    hooks.Schedules.Release(reservation.ReservationId);
            }
        }

        /// <summary>Slice A3 (QA-12): adopt the pre-registered dispatch
        /// task for the shipment's agreement instead of minting a second
        /// load task. Returns true when the task was adopted and started
        /// (the shipment is Loading). Returns false with a non-empty
        /// reason when adoption was attempted but blocked (reserve or
        /// start refused); the caller must return that refusal without
        /// minting. Returns false with an empty reason when there is
        /// nothing to adopt (no lookup, no registered id, task missing or
        /// not adoptable); the caller falls through to minting. Never
        /// calls AssignTask: the adopted task's budget was already
        /// committed when it was assigned. Never cancels the adopted
        /// task: it belongs to the agreement, not to BeginLoading.</summary>
        private bool TryAdoptDispatchTask(LogisticsShipmentState shipment, EntityId loaderId,
            FreightShipmentHooks hooks, out string reason)
        {
            reason = string.Empty;
            string registeredTaskId = hooks?.FindRegisteredDispatchTask?.Invoke(shipment);
            if (string.IsNullOrEmpty(registeredTaskId)) return false;
            WorkTask adopted = hooks.Tasks.FindTaskByIdString(registeredTaskId);
            if (adopted == null)
            {
                Log(hooks, $"shipment '{shipment.ShipmentId}': registered dispatch task '{registeredTaskId}' is not in the task authority; minting a fresh load task.");
                return false;
            }
            if ((adopted.Status != TaskStatus.Assigned && adopted.Status != TaskStatus.ResourcesReserved)
                || adopted.AssigneeId != loaderId)
            {
                Log(hooks, $"shipment '{shipment.ShipmentId}': registered dispatch task '{registeredTaskId}' is {adopted.Status} assigned to {adopted.AssigneeId}, not adoptable for loader {loaderId}; minting a fresh load task.");
                return false;
            }
            // Reserve-first (QA-05) applies to the adopted task too: its
            // window is booked from its own remaining minutes, before
            // StartTask. Its budget is already committed; the reservation
            // is the only new booking.
            if (!ReserveHandlerWindow(hooks, loaderId, shipment.ShipmentId,
                "freight:load:" + shipment.ShipmentId, adopted.MinutesRemaining, loadReservations, out reason))
            {
                reason = "FreightShipmentService: load window refused: " + reason;
                return false;
            }
            if (!hooks.Tasks.StartTask(adopted.TaskId, hooks.DayIndex, out reason))
            {
                reason = "FreightShipmentService: registered dispatch task could not start: " + reason;
                ReleaseHandlerWindow(loadReservations, hooks, shipment.ShipmentId, loaderId, "freight:load:" + shipment.ShipmentId);
                return false;
            }
            loadTasks[shipment.ShipmentId] = adopted.TaskId;
            shipment.state = LogisticsShipmentStatus.Loading;
            shipment.stageElapsedGameSeconds = 0f;
            Log(hooks, "shipment '" + shipment.ShipmentId + "': loading started (adopted dispatch task " + adopted.TaskId + ", " + adopted.DefinitionId + ").");
            return true;
        }

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
            foreach (LogisticsShipmentState existing in trackedShipments)
            {
                if (existing != null && string.Equals(existing.ShipmentId, shipmentId, StringComparison.Ordinal))
                {
                    reason = $"FreightShipmentService: shipment id '{shipmentId}' is already recorded; duplicate shipment creation refused.";
                    return null;
                }
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
                cargoLotId = "freight-cargo:" + shipmentId,
                sourceBusinessInstanceId = sourceBusinessId ?? string.Empty,
                destinationBusinessInstanceId = destinationBusinessId ?? string.Empty,
                plannedQuantityUnits = Math.Max(0, quantityUnits),
                physicalWagonAssetId = wagonAssetId,
                physicalLocationId = sourceBusinessId ?? string.Empty,
            };
            cargoSpecs[shipmentId] = cargo ?? new FreightCargoSpec();

            string cargoRefusal = ValidateCargoSpec(cargoSpecs[shipmentId]);
            if (cargoRefusal != null)
            {
                return BlockPreLoad(shipment, cargoRefusal, hooks);
            }

            // Capacity check: mass AND volume. Over capacity -> blocked pre-load, zero cargo.
            string capacityRefusal = CapacityRefusal(quantityUnits, cargoSpecs[shipmentId], wagon);
            if (capacityRefusal != null)
            {
                return BlockPreLoad(shipment, capacityRefusal, hooks);
            }

            float totalMassKg = quantityUnits * cargoSpecs[shipmentId].MassKgPerUnit;
            float totalVolumeM3 = quantityUnits * cargoSpecs[shipmentId].VolumeM3PerUnit;

            Log(hooks, "shipment '" + shipmentId + "' planned: " + quantityUnits + " units ("
                + totalMassKg.ToString("0.0") + "kg, " + totalVolumeM3.ToString("0.00") + "m3) on wagon '"
                + wagonAssetId + "' from '" + sourceBusinessId + "' to '" + destinationBusinessId + "'.");
            Track(shipment);
            return shipment;
        }

        /// <summary>
        /// Binds the authoritative source lot to a still-planned shipment.
        /// One whole lot can be assigned to only one shipment record; callers
        /// that need split loads must first represent those quantities as
        /// distinct source lots in their inventory authority.
        /// </summary>
        public bool TryBindSourceLot(LogisticsShipmentState shipment, string lotId, string skuId, out string reason)
        {
            reason = string.Empty;
            if (shipment == null || string.IsNullOrWhiteSpace(lotId) || string.IsNullOrWhiteSpace(skuId))
            {
                reason = "FreightShipmentService: a shipment, source lot id, and SKU are required; source lot was not bound.";
                return false;
            }
            if (shipment.Status != LogisticsShipmentStatus.Planned || shipment.loadApplied || shipment.sourceLotDebitApplied)
            {
                reason = $"FreightShipmentService: source lot '{lotId}' can only be bound before shipment '{shipment.ShipmentId}' loading begins.";
                return false;
            }
            foreach (LogisticsShipmentState existing in trackedShipments)
            {
                if (existing == null || ReferenceEquals(existing, shipment)) continue;
                if (string.Equals(existing.CargoLotId, lotId, StringComparison.Ordinal)
                    && existing.Status != LogisticsShipmentStatus.Failed)
                {
                    reason = $"FreightShipmentService: source lot '{lotId}' is already assigned to shipment '{existing.ShipmentId}' ({existing.Status}); duplicate lot assignment refused.";
                    return false;
                }
            }
            shipment.cargoLotId = lotId.Trim();
            shipment.cargoSkuId = skuId.Trim();
            shipment.requiresSourceLotCustody = true;
            return true;
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

        private static string ValidateCargoSpec(FreightCargoSpec cargo)
        {
            if (cargo == null) return "FreightShipmentService: cargo specification is missing.";
            if (float.IsNaN(cargo.MassKgPerUnit) || float.IsInfinity(cargo.MassKgPerUnit) || cargo.MassKgPerUnit < 0f)
                return "FreightShipmentService: cargo mass per unit must be a finite, non-negative value; shipment owns zero cargo.";
            if (float.IsNaN(cargo.VolumeM3PerUnit) || float.IsInfinity(cargo.VolumeM3PerUnit) || cargo.VolumeM3PerUnit < 0f)
                return "FreightShipmentService: cargo volume per unit must be a finite, non-negative value; shipment owns zero cargo.";
            return null;
        }

        private static string CapacityRefusal(int quantityUnits, FreightCargoSpec cargo, EquipmentAsset wagon)
        {
            if (wagon == null) return "FreightShipmentService: registered wagon is unavailable; shipment cannot be loaded.";
            float totalMassKg = quantityUnits * cargo.MassKgPerUnit;
            float totalVolumeM3 = quantityUnits * cargo.VolumeM3PerUnit;
            if (totalMassKg > wagon.PayloadMassCapacityKg + 0.001f)
            {
                return "cargo mass " + totalMassKg.ToString("0.0") + "kg exceeds wagon '" + wagon.AssetId
                    + "' capacity " + wagon.PayloadMassCapacityKg.ToString("0.0") + "kg.";
            }
            if (totalVolumeM3 > wagon.CargoVolumeCapacityM3 + 0.0001f)
            {
                return "cargo volume " + totalVolumeM3.ToString("0.00") + "m3 exceeds wagon '" + wagon.AssetId
                    + "' capacity " + wagon.CargoVolumeCapacityM3.ToString("0.00") + "m3.";
            }
            return null;
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
            // Capacity can change after planning (for example, a wagon can
            // become damaged or be reconciled from its current authority).
            // Recheck before creating the handling task or debiting a source lot.
            if (wagons.TryGetValue(shipment.physicalWagonAssetId ?? string.Empty, out EquipmentAsset currentWagon)
                && cargoSpecs.TryGetValue(shipment.ShipmentId, out FreightCargoSpec plannedCargo))
            {
                string invalidCargo = ValidateCargoSpec(plannedCargo);
                string capacityRefusal = invalidCargo ?? CapacityRefusal(
                    shipment.PlannedQuantityUnits, plannedCargo, currentWagon);
                if (capacityRefusal != null)
                {
                    BlockPreLoad(shipment, capacityRefusal, hooks);
                    reason = "FreightShipmentService: " + capacityRefusal + " Shipment failed before loading; source inventory remains untouched.";
                    return false;
                }
            }
            if (shipment.requiresSourceLotCustody
                && (string.IsNullOrWhiteSpace(shipment.cargoLotId)
                    || string.IsNullOrWhiteSpace(shipment.cargoSkuId)
                    || hooks?.CommitSourceLot == null))
            {
                reason = $"FreightShipmentService: shipment '{shipment.ShipmentId}' requires a source lot id, SKU, and wired source-debit authority before loading; no task or cargo movement started.";
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
            // Slice A3 (QA-12): adopt the pre-registered dispatch task
            // instead of minting a second load task. The overnight
            // recovery made StartTask refuse ResourcesReserved tasks, so
            // minting a second task for a handler who already holds the
            // registered dispatch task always refuses on occupancy.
            if (TryAdoptDispatchTask(shipment, loaderId, hooks, out reason))
                return true;
            if (!string.IsNullOrEmpty(reason))
            {
                // Adoption was attempted but blocked (reserve or start
                // refused). Do not fall through to minting: the registered
                // task exists and the refusal is about this handler, not
                // about a missing task.
                return false;
            }
            // No registered task, or it could not be adopted: fall through
            // to the minting path below.
            string defId = "freight:load:" + shipment.ShipmentId;
            if (hooks.Tasks.GetDefinition(defId) == null)
            {
                // Handling time scales with quantity: 1 min per 10 units, min 5.
                int baseMinutes = Math.Max(5, (shipment.PlannedQuantityUnits + 9) / 10);
                hooks.Tasks.RegisterDefinition(new TaskDefinition(defId,
                    "Load " + shipment.PlannedQuantityUnits + " units (" + shipment.ShipmentId + ")", baseMinutes), out _);
            }
            // Slice A2 (QA-05): reserve-first — the handler's calendar window
            // is booked before the task exists, so a double-booked handler
            // refuses here with no task, no budget, and no cargo moved.
            int plannedMinutes = hooks.Tasks.EstimateMinutesForWorker(defId, loaderId);
            if (!ReserveHandlerWindow(hooks, loaderId, shipment.ShipmentId,
                defId, plannedMinutes, loadReservations, out reason))
            {
                reason = "FreightShipmentService: load window refused: " + reason;
                return false;
            }
            WorkTask task = hooks.Tasks.CreateTask(defId, EntityId.For(EntityKind.Business, 3), hooks.DayIndex);
            if (!hooks.Tasks.AssignTask(task.TaskId, loaderId, hooks.Budgets, out reason)
                || !hooks.Tasks.StartTask(task.TaskId, hooks.DayIndex, out reason))
            {
                reason = "FreightShipmentService: load task could not start: " + reason;
                hooks.Tasks.CancelTask(task.TaskId, hooks.Budgets);
                ReleaseHandlerWindow(loadReservations, hooks, shipment.ShipmentId, loaderId, defId);
                return false;
            }
            loadTasks[shipment.ShipmentId] = task.TaskId;
            shipment.state = LogisticsShipmentStatus.Loading;
            shipment.stageElapsedGameSeconds = 0f;
            Log(hooks, "shipment '" + shipment.ShipmentId + "': loading started (task " + task.TaskId + ").");
            return true;
        }

        /// <summary>Slice A1 (QA-01): a load task whose work is done but whose
        /// source debit refused must not strand the loader. Complete the task
        /// with a failure outcome (the work was done; the objective failed),
        /// free the loader, and Delay the shipment back to Planned so the
        /// driver retry re-attempts the load through BeginLoading. No cargo
        /// moves: the debit refused, so nothing was debited. Returns false
        /// (the load did not complete); the return contract is unchanged.</summary>
        private bool FailLoadOnDebitRefusal(LogisticsShipmentState shipment, EntityId taskId,
            FreightShipmentHooks hooks, string reason)
        {
            WorkTask failedTask = hooks.Tasks.FindTask(taskId);
            EntityId loaderId = failedTask != null ? failedTask.AssigneeId : EntityId.Invalid;
            hooks.Tasks.CompleteTask(taskId, TaskOutcome.RecoverableFailure, hooks.DayIndex, hooks.Budgets);
            // Slice A2 (QA-05): the task terminalized, so its window releases.
            ReleaseHandlerWindow(loadReservations, hooks, shipment.ShipmentId, loaderId, "freight:load:" + shipment.ShipmentId);
            loadTasks.Remove(shipment.ShipmentId);
            shipment.MarkDelayed(reason, LogisticsShipmentStatus.Planned);
            Log(hooks, $"shipment '{shipment.ShipmentId}': load task closed as failed — {reason}");
            return false;
        }

        /// <summary>Works the loading task; on completion custody moves to the wagon.</summary>
        public bool WorkLoading(LogisticsShipmentState shipment, int minutes,
            FreightShipmentHooks hooks, out string reason)
        {
            reason = string.Empty;
            if (shipment == null) { reason = "FreightShipmentService: no shipment to load."; return false; }
            if (!loadTasks.TryGetValue(shipment.ShipmentId, out EntityId taskId))
            {
                reason = "FreightShipmentService: no load task for '" + shipment.ShipmentId + "'.";
                return false;
            }
            WorkTask loadTask = hooks?.Tasks?.FindTask(taskId);
            if (loadTask == null)
            {
                reason = $"FreightShipmentService: load task '{taskId}' is missing; shipment remains in source custody.";
                return false;
            }
            if (shipment.requiresSourceLotCustody
                && (!shipment.sourceLotDebitApplied)
                && (string.IsNullOrWhiteSpace(shipment.cargoLotId)
                    || string.IsNullOrWhiteSpace(shipment.cargoSkuId)
                    || hooks?.CommitSourceLot == null))
            {
                reason = $"FreightShipmentService: shipment '{shipment.ShipmentId}' has no complete source-lot debit binding; no loading work recorded.";
                // Slice A1 (QA-01, extended): the same strand as the
                // post-done refusal. In practice only the debit hook can
                // disappear between BeginLoading and WorkLoading (the lot
                // id and SKU are set at creation and never blanked), so
                // this fires for a hook unwired on today's daily drive.
                // Waiting would leave the loader bound to an unworkable
                // task; closing it and delaying back to Planned lets the
                // driver retry through BeginLoading when the hook returns.
                return FailLoadOnDebitRefusal(shipment, taskId, hooks, reason);
            }
            bool done = loadTask.MinutesRemaining <= 0;
            if (!done && !hooks.Tasks.RecordWorkProgress(taskId,
                Math.Min(WorkTimeMath.ClampToWholeMinutes(minutes), loadTask.MinutesRemaining),
                hooks.Budgets, hooks.DayIndex, out done, out reason)) return false;
            if (!done) return true;

            if (shipment.requiresSourceLotCustody && !shipment.sourceLotDebitApplied)
            {
                if (string.IsNullOrWhiteSpace(shipment.cargoLotId)
                    || string.IsNullOrWhiteSpace(shipment.cargoSkuId))
                {
                    reason = $"FreightShipmentService: shipment '{shipment.ShipmentId}' requires a source lot id and SKU before loading; source stock was not moved.";
                    return false;
                }
                if (hooks?.CommitSourceLot == null)
                {
                    reason = $"FreightShipmentService: shipment '{shipment.ShipmentId}' requires source-lot debit for '{shipment.cargoLotId}', but no debit authority is wired; source stock was not moved.";
                    return FailLoadOnDebitRefusal(shipment, taskId, hooks, reason);
                }
                if (!hooks.CommitSourceLot(shipment, shipment.PlannedQuantityUnits, out reason))
                {
                    if (string.IsNullOrWhiteSpace(reason)) reason = "source-lot debit refused; no cargo loaded.";
                    Log(hooks, $"shipment '{shipment.ShipmentId}': source debit refused for lot '{shipment.cargoLotId}': {reason}");
                    return FailLoadOnDebitRefusal(shipment, taskId, hooks, reason);
                }
                shipment.sourceLotDebitApplied = true;
                Log(hooks, $"shipment '{shipment.ShipmentId}': source lot '{shipment.cargoLotId}' ({shipment.cargoSkuId}) debited once; {shipment.PlannedQuantityUnits}u enters wagon custody.");
            }
            hooks.Tasks.CompleteTask(taskId, TaskOutcome.Success, hooks.DayIndex, hooks.Budgets);
            // Slice A2 (QA-05): the task terminalized, so its window releases.
            ReleaseHandlerWindow(loadReservations, hooks, shipment.ShipmentId, loadTask.AssigneeId, "freight:load:" + shipment.ShipmentId);
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
                // Slice S3: the reservation owner is the shipment id itself.
                // The draft-team bind made at dispatch completion
                // (WagonAuthority.TryAssignDraftTeam) already holds the same
                // animal key under this same owner; the unified authority
                // exempts same-owner holds ("one logical holder"), so this
                // dispatch hold extends the team's hold instead of refusing
                // against it. A different owner string here made Dispatch
                // refuse forever once the team was bound.
                var request = new ReservationRequest(
                    shipment.ShipmentId, "freight haul " + shipment.ShipmentId);
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
            if (shipment.deliveryApplied)
            {
                reason = $"FreightShipmentService: shipment '{shipment.ShipmentId}' already credited its cargo lot; duplicate receipt refused.";
                return false;
            }
            bool resumingPartial = shipment.Status == LogisticsShipmentStatus.Partial
                && !shipment.deliveryApplied
                && !unloadTasks.ContainsKey(shipment.ShipmentId);
            if (shipment.Status != LogisticsShipmentStatus.Arrived && !resumingPartial)
            {
                reason = "FreightShipmentService: shipment '" + shipment.ShipmentId + "' is " + shipment.Status + ", not Arrived.";
                return false;
            }
            if (shipment.requiresSourceLotCustody
                && (!shipment.sourceLotDebitApplied
                    || string.IsNullOrWhiteSpace(shipment.cargoLotId)
                    || hooks?.ReceiveCargoLot == null))
            {
                reason = $"FreightShipmentService: shipment '{shipment.ShipmentId}' requires a committed source lot and wired lot receiver before unloading; cargo remains in wagon custody.";
                return false;
            }
            string defId = "freight:unload:" + shipment.ShipmentId;
            if (hooks.Tasks.GetDefinition(defId) == null)
            {
                int baseMinutes = Math.Max(5, (shipment.RemainingQuantityUnits + 9) / 10);
                hooks.Tasks.RegisterDefinition(new TaskDefinition(defId,
                    "Unload " + shipment.RemainingQuantityUnits + " units (" + shipment.ShipmentId + ")", baseMinutes), out _);
            }
            // Slice A2 (QA-05): reserve-first, same as BeginLoading.
            int plannedMinutes = hooks.Tasks.EstimateMinutesForWorker(defId, unloaderId);
            if (!ReserveHandlerWindow(hooks, unloaderId, shipment.ShipmentId,
                defId, plannedMinutes, unloadReservations, out reason))
            {
                reason = "FreightShipmentService: unload window refused: " + reason;
                return false;
            }
            WorkTask task = hooks.Tasks.CreateTask(defId, EntityId.For(EntityKind.Business, 3), hooks.DayIndex);
            if (!hooks.Tasks.AssignTask(task.TaskId, unloaderId, hooks.Budgets, out reason)
                || !hooks.Tasks.StartTask(task.TaskId, hooks.DayIndex, out reason))
            {
                reason = "FreightShipmentService: unload task could not start: " + reason;
                hooks.Tasks.CancelTask(task.TaskId, hooks.Budgets);
                ReleaseHandlerWindow(unloadReservations, hooks, shipment.ShipmentId, unloaderId, defId);
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
            if (shipment == null) { reason = "FreightShipmentService: no shipment to unload."; return false; }
            if (!unloadTasks.TryGetValue(shipment.ShipmentId, out EntityId taskId))
            {
                reason = "FreightShipmentService: no unload task for '" + shipment.ShipmentId + "'.";
                return false;
            }
            WorkTask unloadTask = hooks?.Tasks?.FindTask(taskId);
            if (unloadTask == null)
            {
                reason = $"FreightShipmentService: unload task '{taskId}' is missing; cargo remains in wagon custody.";
                return false;
            }
            bool done = unloadTask.MinutesRemaining <= 0;
            if (!done && !hooks.Tasks.RecordWorkProgress(taskId,
                Math.Min(WorkTimeMath.ClampToWholeMinutes(minutes), unloadTask.MinutesRemaining),
                hooks.Budgets, hooks.DayIndex, out done, out reason)) return false;
            if (!done) return true;

            // Physical presentation may take longer than the simulated unload
            // work itself. Keep the same reserved task and wagon custody until
            // the assigned handler reaches the receiving point; never let a
            // day-sized simulation tick turn a planned route into receipt.
            if (shipment.requiresSourceLotCustody && hooks.ConfirmPhysicalReceiving != null)
            {
                bool routeAvailable = hooks.ConfirmPhysicalReceiving(shipment, unloadTask.AssigneeId,
                    out bool physicallyReceived, out reason);
                if (!routeAvailable || !physicallyReceived)
                {
                    if (routeAvailable && reason.StartsWith("Carrying:", StringComparison.Ordinal))
                        shipment.physicalLocationId = "person:" + unloadTask.AssigneeId.Id;
                    // Slice A1 (QA-04): the gate may legitimately lag the
                    // simulated work (the handler is still carrying), but it
                    // must not pin forever. Count consecutive pinned ticks;
                    // after the bound, close the unload task with a failure
                    // outcome (freeing the unloader) and Delay the shipment
                    // with the gate's own reason, so the driver retry can
                    // re-attempt it. The gate is never auto-passed.
                    int pins = 0;
                    physicalReceivingPins.TryGetValue(shipment.ShipmentId, out pins);
                    pins++;
                    if (pins >= PhysicalReceivingPinLimit)
                    {
                        physicalReceivingPins.Remove(shipment.ShipmentId);
                        hooks.Tasks.CompleteTask(taskId, TaskOutcome.RecoverableFailure, hooks.DayIndex, hooks.Budgets);
                        // Slice A2 (QA-05): the task terminalized, so its window releases.
                        ReleaseHandlerWindow(unloadReservations, hooks, shipment.ShipmentId, unloadTask.AssigneeId, "freight:unload:" + shipment.ShipmentId);
                        unloadTasks.Remove(shipment.ShipmentId);
                        string gateReason = string.IsNullOrWhiteSpace(reason)
                            ? "physical receiving did not confirm"
                            : reason.Trim();
                        shipment.MarkDelayed(
                            "physical receiving gate pinned for " + PhysicalReceivingPinLimit + " ticks: " + gateReason,
                            LogisticsShipmentStatus.Arrived);
                        Log(hooks, $"shipment '{shipment.ShipmentId}': unload task closed as failed — receiving gate pinned ({gateReason}); cargo remains in wagon custody.");
                        return true;
                    }
                    physicalReceivingPins[shipment.ShipmentId] = pins;
                    Log(hooks, $"shipment '{shipment.ShipmentId}': physical receiving not yet confirmed (pin {pins}/{PhysicalReceivingPinLimit}): {reason}");
                    return true;
                }
                physicalReceivingPins.Remove(shipment.ShipmentId);
            }
            hooks.Tasks.CompleteTask(taskId, TaskOutcome.Success, hooks.DayIndex, hooks.Budgets);
            // Slice A2 (QA-05): the task completes BEFORE the receive attempt
            // in this method, so releasing here covers every receive-attempt
            // exit (Completed / Partial / Delayed-on-refusal) with one
            // release point — the task is terminal from CompleteTask onward.
            ReleaseHandlerWindow(unloadReservations, hooks, shipment.ShipmentId, unloadTask.AssigneeId, "freight:unload:" + shipment.ShipmentId);
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

            if (shipment.requiresSourceLotCustody)
            {
                if (!shipment.sourceLotDebitApplied || string.IsNullOrWhiteSpace(shipment.cargoLotId))
                {
                    shipment.MarkDelayed("source lot was not debited or cargo lot identity is missing; recipient credit refused", LogisticsShipmentStatus.Arrived);
                    Log(hooks, $"shipment '{shipment.ShipmentId}': NOT delivered — no committed source lot identity; {remaining}u remain in wagon custody.");
                    return true;
                }
                if (hooks?.ReceiveCargoLot == null)
                {
                    shipment.MarkDelayed("no source-lot-aware receiving authority wired; cargo remains in wagon custody", LogisticsShipmentStatus.Arrived);
                    Log(hooks, $"shipment '{shipment.ShipmentId}': NOT delivered — no lot receiver for '{shipment.cargoLotId}'.");
                    return true;
                }
                if (!hooks.ReceiveCargoLot(shipment, remaining, out string lotReceiveReason))
                {
                    shipment.MarkDelayed(
                        string.IsNullOrWhiteSpace(lotReceiveReason) ? "recipient refused cargo lot" : lotReceiveReason.Trim(),
                        LogisticsShipmentStatus.Arrived);
                    Log(hooks, $"shipment '{shipment.ShipmentId}': NOT delivered — lot '{shipment.cargoLotId}' remains in wagon custody ({shipment.BlockedReason}).");
                    return true;
                }

                shipment.deliveredQuantityUnits += remaining;
                shipment.remainingQuantityUnits = 0;
                shipment.deliveryApplied = true;
                shipment.physicalLocationId = shipment.destinationBusinessInstanceId;
                shipment.physicalTransportBound = false;
                shipment.state = LogisticsShipmentStatus.Completed;
                hooks.OnPhysicalReceivingComplete?.Invoke(shipment, unloadTask.AssigneeId);
                ReleaseShipmentReservations(shipment);
                Log(hooks, $"shipment '{shipment.ShipmentId}': lot '{shipment.cargoLotId}' credited once to '{shipment.destinationBusinessInstanceId}' ({remaining}u).");
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
            public FreightServiceAgreementBookSaveDto ServiceAgreements = new FreightServiceAgreementBookSaveDto();
            public List<string> ShipmentIdsWithLoadTasks = new List<string>();
            public List<string> ShipmentIdsWithUnloadTasks = new List<string>();
            /// <summary>
            /// Pass XI WS4 save/load fix (Gap 2): ids of captured shipments
            /// that held a dispatch reservation through the reservation
            /// authority, so restore can rebind them. Additive; an old save
            /// leaves this empty and restore falls back to shipment state.
            /// </summary>
            public List<string> ShipmentIdsWithDispatchAuthorities = new List<string>();
            /// <summary>
            /// Priority 7 Slice 2 (D-F2): the dispatch plans (real driver +
            /// draft animals) recorded for the captured shipments, so a
            /// Planned shipment whose transport is not yet bound keeps its
            /// plan across save/load instead of waiting forever on "no
            /// dispatch plan". Additive; an old save leaves this null/empty
            /// and restore behaves exactly as before (no plan, same
            /// diagnostic, no crash). Format version stays 6.
            /// </summary>
            public List<FreightDispatchPlanSaveDto> DispatchPlans = new List<FreightDispatchPlanSaveDto>();
        }

        /// <summary>
        /// Priority 7 Slice 2 (D-F2): one persisted dispatch plan — the
        /// real driver and draft animals for a single shipment, as printed
        /// by EntityId.ToString ("P7", "A0"). New small serializable type;
        /// no existing field renamed, no enum touched.
        /// </summary>
        [Serializable]
        public sealed class FreightDispatchPlanSaveDto
        {
            public string ShipmentId = string.Empty;
            public string DriverPersonId = string.Empty;
            public List<string> AnimalIds = new List<string>();
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

        /// <summary>
        /// Slice A3b (QA-30): resolves the agreement's registered dispatch
        /// task for a restored shipment, when the hooks wire that lookup.
        /// Validated: the task must exist, be non-terminal, and carry the
        /// load definition for this shipment ("freight:load:&lt;id&gt;" or
        /// "fr.load-freight") — a miswired lookup returning the 1-minute
        /// dispatch token (definition "freight-dispatch-assignment") can
        /// never bind as a load task. Non-terminal is the full terminal
        /// set (TaskDurationDiagnostics: Complete / Failed / Cancelled /
        /// ReworkRequired) — ReworkRequired is terminal for the instance
        /// (TaskModels: "failed work that needs a corrective task"), so a
        /// rework task must never bind as a live load task. Returns null
        /// when the lookup is unwired, empty, or fails validation.
        /// </summary>
        private WorkTask FindRegisteredLoadTask(FreightShipmentHooks hooks,
            LogisticsShipmentState restored, string shipmentId)
        {
            if (hooks == null || hooks.Tasks == null || hooks.FindRegisteredDispatchTask == null || restored == null)
                return null;
            string registeredId = hooks.FindRegisteredDispatchTask(restored);
            if (string.IsNullOrEmpty(registeredId))
                return null;
            WorkTask candidate = hooks.Tasks.FindTaskByIdString(registeredId);
            if (candidate == null)
                return null;
            if (candidate.Status == TaskStatus.Complete
                || candidate.Status == TaskStatus.Failed
                || candidate.Status == TaskStatus.Cancelled
                || candidate.Status == TaskStatus.ReworkRequired)
                return null;
            if (candidate.DefinitionId != "freight:load:" + shipmentId
                && candidate.DefinitionId != "fr.load-freight")
                return null;
            return candidate;
        }

        /// <summary>Restores shipments; task bindings re-resolve through the registered-task lookup then by definition id (read-only). Shipments whose handling task cannot be re-bound are Delayed with a named reason instead of stalling (Slice A3b, QA-30).</summary>
        public List<LogisticsShipmentState> RestoreSaveDto(FreightServiceSaveDto dto,
            FreightShipmentHooks hooks)
        {
            var out_ = new List<LogisticsShipmentState>();
            // Q04 Slice 5: clear-then-load. A smaller second save must drop
            // shipments (and their load/unload-task and dispatch-authority
            // bindings) the save no longer carries; without this, stale
            // shipments stay tracked and stale bindings point at tasks and
            // authorities for shipments that no longer exist. A null DTO
            // clears too and returns empty, matching the existing null
            // contract. The driver rebuilds its per-company lists from the
            // returned shipments, so clearing first does not disturb it.
            trackedShipments.Clear();
            loadTasks.Clear();
            unloadTasks.Clear();
            physicalReceivingPins.Clear();
            // Slice A2 (QA-05): the reservation maps are transient; the
            // tracker's own reservations persist via its save slot, so a
            // post-restore release falls back to a same-day label lookup.
            loadReservations.Clear();
            unloadReservations.Clear();
            dispatchAuthorities.Clear();
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
            // Slice A3b (QA-30): rebind in-flight handling tasks. The load
            // task is found (1) through the agreement's registered dispatch
            // task when the hooks wire that lookup (fixture scope — no
            // production hooks builder wires it; the QA-12 identity
            // decision keeps adoption fixture-only), validated so a
            // miswired lookup returning the 1-minute dispatch token cannot
            // bind it, then (2) by the deterministic definition id
            // "freight:load:<shipmentId>" — the production minted task.
            // No bare "fr.load-freight" definition fallback: that id is
            // shared by the separate company-runtime delivery flow
            // (DeliveryService.cs:408 mints it too) and a first-match
            // lookup could bind another shipment's task. When neither
            // lookup finds a task and the restored shipment is still
            // Loading, the shipment is Delayed with a named reason and
            // resumeState Planned instead of stalling forever in Loading
            // (WorkLoading fails with "no load task" on a null rebind).
            // The unload loop has no registered-task lookup (the dispatch
            // task is a load task); its net resumes to Arrived so the
            // unload flow restarts from the pre-unload state.
            if (hooks != null && hooks.Tasks != null)
            {
                var restoredById = new Dictionary<string, LogisticsShipmentState>(StringComparer.Ordinal);
                foreach (LogisticsShipmentState restored in out_)
                {
                    if (restored != null && !string.IsNullOrEmpty(restored.ShipmentId))
                        restoredById[restored.ShipmentId] = restored;
                }
                foreach (string shipmentId in dto.ShipmentIdsWithLoadTasks)
                {
                    LogisticsShipmentState loadShipment;
                    restoredById.TryGetValue(shipmentId, out loadShipment);
                    WorkTask t = FindRegisteredLoadTask(hooks, loadShipment, shipmentId)
                        ?? hooks.Tasks.FindOpenTask("freight:load:" + shipmentId,
                            EntityId.For(EntityKind.Business, 3), EntityId.Invalid);
                    // A3b follow-up nit (b): bind only an OPEN task.
                    // FindOpenTask excludes Complete only, so without this
                    // guard it would re-bind the same dead task the
                    // registered lookup just rejected (Failed, Cancelled,
                    // ReworkRequired) and the shipment would stall in
                    // Loading with a non-null binding instead of hitting
                    // the Delayed net below. IsOpen is the authority's own
                    // predicate (Queued / Assigned / InProgress / Paused /
                    // WaitingOnPassiveProcess / WaitingOnDependency /
                    // ResourcesReserved).
                    if (t != null && t.IsOpen)
                    {
                        loadTasks[shipmentId] = t.TaskId;
                        continue;
                    }
                    if (loadShipment != null
                        && loadShipment.Status == LogisticsShipmentStatus.Loading)
                    {
                        loadShipment.MarkDelayed(
                            "restored shipment '" + shipmentId + "' is Loading but its load task could not be re-bound after restore (no open load task found); delaying for a fresh load attempt",
                            LogisticsShipmentStatus.Planned);
                        Log(hooks, "shipment '" + shipmentId + "' delayed: " + loadShipment.BlockedReason);
                    }
                }
                foreach (string shipmentId in dto.ShipmentIdsWithUnloadTasks)
                {
                    WorkTask t = hooks.Tasks.FindOpenTask("freight:unload:" + shipmentId,
                        EntityId.For(EntityKind.Business, 3), EntityId.Invalid);
                    // Same guard as the load loop, symmetric: a terminal
                    // unload task must fall through to the Delayed net
                    // below instead of binding a dead task.
                    if (t != null && t.IsOpen)
                    {
                        unloadTasks[shipmentId] = t.TaskId;
                        continue;
                    }
                    LogisticsShipmentState unloadShipment;
                    if (restoredById.TryGetValue(shipmentId, out unloadShipment)
                        && unloadShipment != null
                        && unloadShipment.Status == LogisticsShipmentStatus.Unloading)
                    {
                        unloadShipment.MarkDelayed(
                            "restored shipment '" + shipmentId + "' is Unloading but its unload task could not be re-bound after restore (no open unload task found); delaying for a fresh unload attempt",
                            LogisticsShipmentStatus.Arrived);
                        Log(hooks, "shipment '" + shipmentId + "' delayed: " + unloadShipment.BlockedReason);
                    }
                }
            }
            return out_;
        }
    }
}
