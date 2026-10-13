using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Equipment
{
    /// <summary>
    /// EQP-1: the unified reservation authority for equipment-side assets
    /// (Tech X §3.8) — significant assets, toolkits, and workstation instances.
    /// Generalizes the EQU-3 DraftPowerService pattern: one authority, no
    /// double-booking. Draft animals stay with DraftPowerService (identity lives
    /// in the HF-2 registry); everything equipment-side reserves here.
    ///
    /// The per-asset Reserve() methods remain for single-asset call sites; this
    /// service is the recommended path whenever more than one asset class is in
    /// play, because it refuses cross-class double-booking through one key space.
    ///
    /// Pass IV: this service IS the IUnifiedReservationAuthority implementation
    /// (ReservationContracts.cs). It keeps its original key-based API (used by
    /// RentalFleet) and adds time-windowed, quantity-aware reservations over the
    /// unified key space (asset:/kit:/workstation:/person:/lot:/workspace:/
    /// animal:/motive:). Legacy calls reserve an open-ended exclusive hold
    /// [0, +inf); windowed holds coexist in the same key space.
    ///
    /// Correctness: TryReserveAll validates every item before committing, so a
    /// refusal never leaves a partial hold (atomic, no deadlock from partial
    /// acquisition). First valid reservation wins; refusals name the conflicting
    /// holder. Capacity never goes negative (AvailableCapacity floors at 0).
    /// Holds by the same owner task never conflict with each other (staged
    /// reservation by one task is one logical holder); capacity still sums.
    /// Canon: XXXI Part III §§3.1-3.7. Technical: XIV Part II §§2.1-2.8.
    /// </summary>
    public sealed class EquipmentReservationService : IUnifiedReservationAuthority
    {
        /// <summary>One live reservation: an owner task's atomic item set.</summary>
        private sealed class ReservationRecord
        {
            public string ReservationId = string.Empty;
            public string OwnerTaskId = string.Empty;
            public string OwnerLabel = string.Empty;
            public List<ReservationItem> Items = new List<ReservationItem>();
        }

        private readonly List<ReservationRecord> liveReservations = new List<ReservationRecord>();

        /// <summary>
        /// Per-key capacity limits for partial-capacity resources. Default 1
        /// (exclusive semantics) when unregistered — ownership never implies
        /// unlimited capacity.
        /// </summary>
        private readonly Dictionary<string, float> resourceCapacities =
            new Dictionary<string, float>(StringComparer.Ordinal);

        private int reservationSequence;

        public static string AssetKey(string assetId) => "asset:" + assetId;
        public static string KitKey(string kitInstanceId) => "kit:" + kitInstanceId;
        public static string WorkstationKey(string workstationInstanceId) => "workstation:" + workstationInstanceId;

        // ---------- legacy key-based API (unchanged semantics) ----------

        /// <summary>Legacy: any live hold on the key (any window).</summary>
        public bool IsReserved(string key) =>
            !string.IsNullOrWhiteSpace(key) && FindAnyRecord(key) != null;

        /// <summary>
        /// Reserves one key. Returns null on success, refusal reason otherwise.
        /// Legacy semantics: an open-ended exclusive hold [0, +inf).
        /// </summary>
        public string Reserve(string key, string reserverId, string purpose)
        {
            if (string.IsNullOrWhiteSpace(key))
                return "EquipmentReservationService: reservation key is required.";
            if (string.IsNullOrWhiteSpace(reserverId))
                return "EquipmentReservationService: reserver id is required.";
            var request = new ReservationRequest(reserverId, purpose ?? string.Empty);
            request.Items.Add(new ReservationItem(key, 1f, 0f, float.MaxValue));
            string refusal = TryReserveAll(request, out _);
            if (refusal != null)
            {
                // Keep the legacy refusal shape (names the current holder).
                string holder = DescribeHold(key, 0f) ?? "unknown holder";
                return $"Reservation refused: {key} is already reserved ({holder}) — no double-booking (Tech X §3.8).";
            }
            return null;
        }

        /// <summary>
        /// Reserves several keys atomically: all succeed or none do. A plow team
        /// that can't get both the plow and the workstation holds neither.
        /// </summary>
        public string ReserveAll(List<string> keys, string reserverId, string purpose, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (keys == null || keys.Count == 0)
            {
                diagnostics.Add("EquipmentReservationService: nothing to reserve.");
                return "EquipmentReservationService: nothing to reserve.";
            }
            var request = new ReservationRequest(reserverId ?? string.Empty, purpose ?? string.Empty);
            foreach (string key in keys)
                request.Items.Add(new ReservationItem(key, 1f, 0f, float.MaxValue));
            string refusal = TryReserveAll(request, out _);
            if (refusal != null)
            {
                diagnostics.Add(refusal);
                return refusal;
            }
            return null;
        }

        /// <summary>Legacy: releases every live hold on the key. Idempotent.</summary>
        public void Release(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            liveReservations.RemoveAll(r => r != null && HoldsKey(r, key));
        }

        public void ReleaseAll(IEnumerable<string> keys)
        {
            if (keys == null) return;
            foreach (string key in keys) Release(key);
        }

        /// <summary>Legacy: number of distinct keys with a live hold.</summary>
        public int ReservedCount
        {
            get
            {
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var record in liveReservations)
                {
                    if (record == null) continue;
                    foreach (var item in record.Items)
                    {
                        if (item != null && !string.IsNullOrWhiteSpace(item.ResourceKey))
                            keys.Add(item.ResourceKey);
                    }
                }
                return keys.Count;
            }
        }

        // ---------- IUnifiedReservationAuthority ----------

        /// <summary>
        /// Registers (or replaces) the reservable capacity for a resource key.
        /// Exclusive resources stay at the default 1. Non-positive values are
        /// refused loudly — a capacity of zero would make the resource unusable.
        /// </summary>
        public string RegisterResourceCapacity(string resourceKey, float capacity)
        {
            if (string.IsNullOrWhiteSpace(resourceKey))
                return "EquipmentReservationService: resource key is required.";
            if (!(capacity > 0f) || float.IsNaN(capacity))
                return $"EquipmentReservationService: capacity for '{resourceKey}' must be positive.";
            resourceCapacities[resourceKey] = capacity;
            return null;
        }

        /// <inheritdoc />
        public string TryReserveAll(ReservationRequest request, out ReservationHandle handle)
        {
            handle = null;
            if (request == null)
                return "Reservation refused: no request supplied.";
            if (string.IsNullOrWhiteSpace(request.OwnerTaskId))
                return "Reservation refused: owner task id is required.";
            if (request.Items == null || request.Items.Count == 0)
                return "Reservation refused: the request has no items — nothing to reserve.";

            // Validate every item first: a malformed request reserves nothing.
            for (int i = 0; i < request.Items.Count; i++)
            {
                ReservationItem item = request.Items[i];
                if (item == null || string.IsNullOrWhiteSpace(item.ResourceKey))
                    return $"Reservation refused: item {i} has no resource key.";
                if (!(item.Quantity > 0f))
                    return $"Reservation refused: item {i} ('{item.ResourceKey}') needs a positive quantity.";
                if (!(item.EndGameSeconds > item.StartGameSeconds))
                    return $"Reservation refused: item {i} ('{item.ResourceKey}') ends before it starts.";
            }

            // Deterministic first-wins check, in request order. Holds by the
            // same owner never conflict with each other (one logical holder);
            // capacity still sums across all overlapping holds.
            for (int i = 0; i < request.Items.Count; i++)
            {
                ReservationItem item = request.Items[i];
                float limit = CapacityLimit(item.ResourceKey);
                float overlapping = OverlappingQuantity(item.ResourceKey, item.StartGameSeconds, item.EndGameSeconds, excludeOwnerTaskId: request.OwnerTaskId);
                if (overlapping + item.Quantity > limit + 1e-6f)
                {
                    string holder = DescribeHold(item.ResourceKey, item.StartGameSeconds)
                        ?? "another reservation";
                    return $"Reservation refused: '{item.ResourceKey}' cannot take {item.Quantity} " +
                           $"[{item.StartGameSeconds}-{item.EndGameSeconds}] for '{request.OwnerLabel}' " +
                           $"(task {request.OwnerTaskId}) — held by {holder} (capacity {limit}, " +
                           $"overlapping {overlapping}) — no double-booking, no negative capacity.";
                }
            }

            // All clear: commit. Nothing before this point mutated state.
            reservationSequence++;
            string reservationId = $"res-{reservationSequence}-{Guid.NewGuid():N}".Substring(0, 24);
            var record = new ReservationRecord
            {
                ReservationId = reservationId,
                OwnerTaskId = request.OwnerTaskId,
                OwnerLabel = request.OwnerLabel ?? string.Empty,
                Items = new List<ReservationItem>(),
            };
            foreach (ReservationItem item in request.Items)
                record.Items.Add(new ReservationItem(item.ResourceKey, item.Quantity, item.StartGameSeconds, item.EndGameSeconds));
            liveReservations.Add(record);
            handle = new ReservationHandle(reservationId, request.OwnerTaskId, record.Items);
            return null;
        }

        /// <summary>Releases a reservation by id. No-op when unknown (idempotent).</summary>
        void IUnifiedReservationAuthority.Release(string reservationId)
        {
            ReleaseReservation(reservationId);
        }

        /// <summary>Releases one reservation by id. Public for non-interface callers. No-op when unknown.</summary>
        public void ReleaseReservation(string reservationId)
        {
            if (string.IsNullOrWhiteSpace(reservationId)) return;
            liveReservations.RemoveAll(r => r != null && string.Equals(r.ReservationId, reservationId, StringComparison.Ordinal));
        }

        /// <inheritdoc />
        public void ReleaseAllForTask(string ownerTaskId)
        {
            if (string.IsNullOrWhiteSpace(ownerTaskId)) return;
            liveReservations.RemoveAll(r => r != null && string.Equals(r.OwnerTaskId, ownerTaskId, StringComparison.Ordinal));
        }

        /// <inheritdoc />
        public void ReleaseAllForResource(string resourceKey)
        {
            if (string.IsNullOrWhiteSpace(resourceKey)) return;
            liveReservations.RemoveAll(r => r != null && HoldsKey(r, resourceKey));
        }

        /// <inheritdoc />
        public bool IsReserved(string resourceKey, float atGameSeconds)
        {
            if (string.IsNullOrWhiteSpace(resourceKey)) return false;
            return OverlappingQuantity(resourceKey, atGameSeconds, atGameSeconds, excludeOwnerTaskId: null) > 0f;
        }

        /// <inheritdoc />
        public float AvailableCapacity(string resourceKey, float atGameSeconds)
        {
            if (string.IsNullOrWhiteSpace(resourceKey)) return 0f;
            float limit = CapacityLimit(resourceKey);
            float used = OverlappingQuantity(resourceKey, atGameSeconds, atGameSeconds, excludeOwnerTaskId: null);
            // Never negative: float error or over-registration cannot create capacity debt.
            return Math.Max(0f, limit - used);
        }

        /// <inheritdoc />
        public List<ReservationHandle> ReservationsForTask(string ownerTaskId)
        {
            var result = new List<ReservationHandle>();
            if (string.IsNullOrWhiteSpace(ownerTaskId)) return result;
            foreach (var record in liveReservations)
            {
                if (record != null && string.Equals(record.OwnerTaskId, ownerTaskId, StringComparison.Ordinal))
                    result.Add(new ReservationHandle(record.ReservationId, record.OwnerTaskId, new List<ReservationItem>(record.Items)));
            }
            return result;
        }

        /// <inheritdoc />
        public int LiveReservationCount => liveReservations.Count;

        /// <summary>
        /// Names the holder of a conflicting hold for diagnostics ("label (task id)").
        /// Null when the key is free at that time.
        /// </summary>
        public string DescribeHold(string resourceKey, float atGameSeconds)
        {
            var record = FindAnyRecordAt(resourceKey, atGameSeconds);
            if (record == null) return null;
            string label = string.IsNullOrWhiteSpace(record.OwnerLabel) ? "unlabeled" : record.OwnerLabel;
            return $"{label} (task {record.OwnerTaskId})";
        }

        // ---------- save/load (DTO type lives in SystemsSaveDtos.cs) ----------

        /// <summary>CLN-1/Pass IV: captures live reservations + capacity limits for the save pipeline.</summary>
        public LandLedgers.Persistence.EquipmentReservationAuthoritySaveDto CaptureSaveDto()
        {
            var dto = new LandLedgers.Persistence.EquipmentReservationAuthoritySaveDto();
            foreach (var record in liveReservations)
            {
                if (record == null) continue;
                dto.handles.Add(new ReservationHandle(record.ReservationId, record.OwnerTaskId, new List<ReservationItem>(record.Items)));
            }
            foreach (var kv in resourceCapacities)
                dto.capacities.Add(new LandLedgers.Persistence.ResourceCapacityEntry { resourceKey = kv.Key, capacity = kv.Value });
            return dto;
        }

        /// <summary>
        /// CLN-1/Pass IV: restores reservations + capacity limits. Clear-then-load
        /// and idempotent: restoring the same DTO twice yields the same state, and
        /// a restored game never double-books (conflicting/expired holds from a
        /// stale DTO are refused, never force-applied).
        /// </summary>
        public void LoadFromSaveDto(LandLedgers.Persistence.EquipmentReservationAuthoritySaveDto dto)
        {
            liveReservations.Clear();
            resourceCapacities.Clear();
            if (dto == null) return;
            if (dto.capacities != null)
            {
                foreach (var entry in dto.capacities)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.resourceKey)) continue;
                    if (entry.capacity > 0f && !float.IsNaN(entry.capacity))
                        resourceCapacities[entry.resourceKey] = entry.capacity;
                }
            }
            if (dto.handles != null)
            {
                foreach (var handle in dto.handles)
                {
                    if (handle == null || string.IsNullOrWhiteSpace(handle.ReservationId)) continue;
                    if (string.IsNullOrWhiteSpace(handle.OwnerTaskId)) continue;
                    var items = new List<ReservationItem>();
                    if (handle.Items != null)
                    {
                        foreach (var item in handle.Items)
                        {
                            if (item == null || string.IsNullOrWhiteSpace(item.ResourceKey)) continue;
                            if (!(item.Quantity > 0f)) continue;
                            if (!(item.EndGameSeconds > item.StartGameSeconds)) continue;
                            items.Add(new ReservationItem(item.ResourceKey, item.Quantity, item.StartGameSeconds, item.EndGameSeconds));
                        }
                    }
                    if (items.Count == 0) continue;
                    // Skip duplicates (idempotent restore) and anything that
                    // would double-book what an earlier entry already holds.
                    bool duplicate = false;
                    foreach (var existing in liveReservations)
                    {
                        if (existing != null && string.Equals(existing.ReservationId, handle.ReservationId, StringComparison.Ordinal))
                        { duplicate = true; break; }
                    }
                    if (duplicate) continue;
                    bool conflicts = false;
                    foreach (var item in items)
                    {
                        float limit = CapacityLimit(item.ResourceKey);
                        if (OverlappingQuantity(item.ResourceKey, item.StartGameSeconds, item.EndGameSeconds, excludeOwnerTaskId: handle.OwnerTaskId) + item.Quantity > limit + 1e-6f)
                        { conflicts = true; break; }
                    }
                    if (conflicts) continue;
                    liveReservations.Add(new ReservationRecord
                    {
                        ReservationId = handle.ReservationId,
                        OwnerTaskId = handle.OwnerTaskId,
                        OwnerLabel = string.Empty,
                        Items = items,
                    });
                }
            }
        }

        // ---------- internals ----------

        private float CapacityLimit(string resourceKey)
        {
            float limit;
            if (resourceCapacities.TryGetValue(resourceKey, out limit) && limit > 0f)
                return limit;
            return 1f; // exclusive default
        }

        private static bool Overlaps(float aStart, float aEnd, float bStart, float bEnd) =>
            aStart < bEnd && bStart < aEnd;

        /// <summary>
        /// Total held quantity on a key overlapping a window (point query when
        /// start == end). Holds by excludeOwnerTaskId are skipped (staged
        /// reservation by one task never conflicts with itself).
        /// </summary>
        private float OverlappingQuantity(string resourceKey, float windowStart, float windowEnd, string excludeOwnerTaskId)
        {
            float total = 0f;
            foreach (var record in liveReservations)
            {
                if (record == null) continue;
                if (excludeOwnerTaskId != null &&
                    string.Equals(record.OwnerTaskId, excludeOwnerTaskId, StringComparison.Ordinal))
                    continue;
                foreach (var item in record.Items)
                {
                    if (item == null) continue;
                    if (!string.Equals(item.ResourceKey, resourceKey, StringComparison.Ordinal)) continue;
                    if (windowStart == windowEnd)
                    {
                        if (item.StartGameSeconds <= windowStart && windowStart < item.EndGameSeconds)
                            total += item.Quantity;
                    }
                    else if (Overlaps(item.StartGameSeconds, item.EndGameSeconds, windowStart, windowEnd))
                    {
                        total += item.Quantity;
                    }
                }
            }
            return total;
        }

        private ReservationRecord FindAnyRecord(string resourceKey)
        {
            foreach (var record in liveReservations)
            {
                if (record != null && HoldsKey(record, resourceKey)) return record;
            }
            return null;
        }

        private ReservationRecord FindAnyRecordAt(string resourceKey, float atGameSeconds)
        {
            foreach (var record in liveReservations)
            {
                if (record == null) continue;
                foreach (var item in record.Items)
                {
                    if (item == null) continue;
                    if (string.Equals(item.ResourceKey, resourceKey, StringComparison.Ordinal) &&
                        item.StartGameSeconds <= atGameSeconds && atGameSeconds < item.EndGameSeconds)
                        return record;
                }
            }
            return null;
        }

        private static bool HoldsKey(ReservationRecord record, string resourceKey)
        {
            foreach (var item in record.Items)
            {
                if (item != null && string.Equals(item.ResourceKey, resourceKey, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }
    }
}
