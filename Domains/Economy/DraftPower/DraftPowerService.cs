using System;
using System.Collections.Generic;
using LandLedgers.Economy.Equipment;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.DraftPower
{
    /// <summary>
    /// EQU-3: the ONE reservation authority for draft animals and drivers
    /// (Tech X §3.8). Before this, the BIZ-3 freight pool kept its own private
    /// reservation lists; now both the freight pool and the new plow work unit
    /// reserve through here — one model for animal draft power, not two.
    ///
    /// Draft animals remain persistent entities with feed, fatigue, health and
    /// assignment (Tech X §3.6) — this service tracks assignment; the HF-2
    /// registry remains the identity authority.
    ///
    /// Pass IV: animal/driver holds are reservations in the unified authority
    /// (keys "animal:{entityId}" / "person:{entityId}"), so a horse pulling a
    /// plow cannot simultaneously be claimed by a freight team — cross-domain
    /// conflicts are detected in the one key space. The service is hub-owned
    /// and its hold index persists (the authority persists the holds
    /// themselves); a restored game never double-books and never silently
    /// drops a live draft reservation.
    ///
    /// Economic integrity: reservations never create animals, drivers, or
    /// goods — they only mark real entities unavailable elsewhere.
    /// </summary>
    public sealed class DraftPowerService
    {
        private sealed class HoldRef
        {
            public string ReservationId = string.Empty;
            public string OwnerTaskId = string.Empty;
            public string OwnerLabel = string.Empty;
        }

        private readonly IUnifiedReservationAuthority authority;
        private readonly Dictionary<string, HoldRef> holdIndex =
            new Dictionary<string, HoldRef>(StringComparer.Ordinal);

        /// <summary>Absolute game seconds for reservation windows. Defaults to 0.</summary>
        public Func<float> NowGameSeconds = () => 0f;

        /// <summary>The unified authority this service reserves through (single key space).</summary>
        public IUnifiedReservationAuthority Authority => authority;

        /// <summary>Test/standalone ctor: private transient authority (not persisted).</summary>
        public DraftPowerService() : this(new EquipmentReservationService())
        {
        }

        /// <summary>Production ctor: hub-owned shared authority (persisted).</summary>
        public DraftPowerService(IUnifiedReservationAuthority authority)
        {
            this.authority = authority ?? throw new ArgumentNullException(nameof(authority));
        }

        private float Now() => NowGameSeconds != null ? NowGameSeconds() : 0f;

        private static string OwnerTaskIdFor(string purpose) => "draft:" + (purpose ?? "work");

        /// <summary>
        /// Reserves up to <paramref name="count"/> eligible, unreserved animals.
        /// Eligibility is caller-supplied: freight takes any draft animal, the plow
        /// unit requires horses. Never double-books.
        ///
        /// Each animal gets its own reservation so ReleaseAnimals can release
        /// exactly the animals passed (the long-standing API contract). The
        /// set is still all-or-none: every candidate is verified free up front,
        /// and a failure mid-way rolls back the ones just taken (atomic on
        /// Unity's single simulation thread — no interleaving reservation can
        /// slip between the check and the commit).
        /// </summary>
        public bool TryReserveAnimals(
            List<EntityId> candidates,
            int count,
            Func<EntityId, bool> isEligible,
            string purpose,
            out List<EntityId> reserved,
            List<string> diagnostics)
        {
            reserved = new List<EntityId>();
            diagnostics = diagnostics ?? new List<string>();
            if (count <= 0)
            {
                diagnostics.Add("DraftPowerService: reservation needs a positive animal count.");
                return false;
            }
            float now = Now();
            var chosen = new List<EntityId>();
            if (candidates != null)
            {
                foreach (EntityId animal in candidates)
                {
                    if (chosen.Count >= count) break;
                    if (isEligible != null && !isEligible(animal)) continue;
                    string key = ReservationKeyConventions.Animal(animal.ToString());
                    if (authority.IsReserved(key, now)) continue;
                    chosen.Add(animal);
                }
            }
            if (chosen.Count < count)
            {
                diagnostics.Add(
                    $"DraftPowerService: only {chosen.Count} eligible unreserved draft animals for '{purpose}' (need {count}) — no double-booking (Tech X §3.8).");
                return false;
            }
            string ownerTaskId = OwnerTaskIdFor(purpose);
            var takenKeys = new List<string>();
            foreach (EntityId animal in chosen)
            {
                string key = ReservationKeyConventions.Animal(animal.ToString());
                var request = new ReservationRequest(ownerTaskId, purpose ?? "draft work");
                request.Items.Add(new ReservationItem(key, 1f, now, float.MaxValue));
                string refusal = authority.TryReserveAll(request, out ReservationHandle handle);
                if (refusal != null || handle == null)
                {
                    // Roll back the ones just taken: no partial acquisition.
                    foreach (string takenKey in takenKeys)
                    {
                        HoldRef href;
                        if (holdIndex.TryGetValue(takenKey, out href) && href != null)
                            authority.Release(href.ReservationId);
                        holdIndex.Remove(takenKey);
                    }
                    diagnostics.Add(refusal ?? "DraftPowerService: draft animal reservation failed.");
                    return false;
                }
                holdIndex[key] = new HoldRef
                {
                    ReservationId = handle.ReservationId,
                    OwnerTaskId = ownerTaskId,
                    OwnerLabel = purpose ?? "draft work",
                };
                takenKeys.Add(key);
                reserved.Add(animal);
            }
            return true;
        }

        public bool TryReserveDriver(
            List<EntityId> candidates,
            out EntityId driver,
            List<string> diagnostics)
        {
            driver = default;
            diagnostics = diagnostics ?? new List<string>();
            float now = Now();
            EntityId chosen = default;
            bool found = false;
            if (candidates != null)
            {
                foreach (EntityId candidate in candidates)
                {
                    if (candidate.Kind != EntityKind.Person) continue;
                    string key = ReservationKeyConventions.Person(candidate.ToString());
                    if (authority.IsReserved(key, now)) continue;
                    chosen = candidate;
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                diagnostics.Add("DraftPowerService: no unreserved driver available.");
                return false;
            }
            string ownerTaskId = OwnerTaskIdFor("driving");
            var request = new ReservationRequest(ownerTaskId, "driving");
            string key2 = ReservationKeyConventions.Person(chosen.ToString());
            request.Items.Add(new ReservationItem(key2, 1f, now, float.MaxValue));
            string refusal = authority.TryReserveAll(request, out ReservationHandle handle);
            if (refusal != null || handle == null)
            {
                diagnostics.Add(refusal ?? "DraftPowerService: driver reservation failed.");
                return false;
            }
            holdIndex[key2] = new HoldRef
            {
                ReservationId = handle.ReservationId,
                OwnerTaskId = ownerTaskId,
                OwnerLabel = "driving",
            };
            driver = chosen;
            return true;
        }

        public void ReleaseAnimals(IEnumerable<EntityId> animals)
        {
            if (animals == null) return;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (EntityId animal in animals)
            {
                string key = ReservationKeyConventions.Animal(animal.ToString());
                HoldRef href;
                if (holdIndex.TryGetValue(key, out href) && href != null)
                    ids.Add(href.ReservationId);
                holdIndex.Remove(key);
            }
            // Release anything the authority still holds for these animals even
            // when our index missed it (e.g. reserved pre-load, index rebuilt).
            foreach (string id in ids)
                authority.Release(id);
        }

        public void ReleaseDriver(EntityId driver)
        {
            string key = ReservationKeyConventions.Person(driver.ToString());
            HoldRef href;
            if (holdIndex.TryGetValue(key, out href) && href != null)
                authority.Release(href.ReservationId);
            holdIndex.Remove(key);
        }

        public bool IsAnimalReserved(EntityId animal) =>
            authority.IsReserved(ReservationKeyConventions.Animal(animal.ToString()), Now());

        public bool IsDriverReserved(EntityId driver) =>
            authority.IsReserved(ReservationKeyConventions.Person(driver.ToString()), Now());

        /// <summary>Number of indexed holds (diagnostics).</summary>
        public int HoldCount => holdIndex.Count;

        // ---------- save/load (DTO type lives in SystemsSaveDtos.cs) ----------

        /// <summary>
        /// Pass IV: captures the hold index for the save pipeline. The holds
        /// themselves persist in the unified authority's section; this index
        /// lets release paths find them after a load.
        /// </summary>
        public LandLedgers.Persistence.DraftPowerServiceSaveDto CaptureSaveDto()
        {
            var dto = new LandLedgers.Persistence.DraftPowerServiceSaveDto();
            foreach (var kv in holdIndex)
            {
                if (kv.Value == null) continue;
                dto.holds.Add(new LandLedgers.Persistence.DraftPowerHoldEntry
                {
                    resourceKey = kv.Key,
                    reservationId = kv.Value.ReservationId,
                    ownerTaskId = kv.Value.OwnerTaskId,
                    ownerLabel = kv.Value.OwnerLabel,
                });
            }
            return dto;
        }

        /// <summary>
        /// Pass IV: restores the hold index. Clear-then-load; entries whose
        /// reservation no longer exists in the authority are dropped (the
        /// authority is the truth — the index never invents a hold).
        /// </summary>
        public void LoadFromSaveDto(LandLedgers.Persistence.DraftPowerServiceSaveDto dto)
        {
            holdIndex.Clear();
            if (dto?.holds == null) return;
            foreach (var entry in dto.holds)
            {
                if (entry == null) continue;
                if (string.IsNullOrWhiteSpace(entry.resourceKey)) continue;
                if (string.IsNullOrWhiteSpace(entry.reservationId)) continue;
                if (string.IsNullOrWhiteSpace(entry.ownerTaskId)) continue;
                if (holdIndex.ContainsKey(entry.resourceKey)) continue; // idempotent
                bool exists = false;
                foreach (var handle in authority.ReservationsForTask(entry.ownerTaskId))
                {
                    if (handle != null && string.Equals(handle.ReservationId, entry.reservationId, StringComparison.Ordinal))
                    { exists = true; break; }
                }
                if (!exists) continue;
                holdIndex[entry.resourceKey] = new HoldRef
                {
                    ReservationId = entry.reservationId,
                    OwnerTaskId = entry.ownerTaskId,
                    OwnerLabel = entry.ownerLabel ?? string.Empty,
                };
            }
        }
    }
}
