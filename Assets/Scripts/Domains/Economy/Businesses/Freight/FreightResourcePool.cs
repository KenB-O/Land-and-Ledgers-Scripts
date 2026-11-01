using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.DraftPower;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.Economy.Transport;
using LandLedgers.Persistence;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Freight
{
    /// <summary>
    /// BIZ-3: a wagon in the freight company's physical pool. One physical asset —
    /// it cannot be in two states at once (Canon §3.6, Tech X §3.4).
    /// </summary>
    public enum FreightWagonStatus
    {
        Unspecified = 0,
        Available = 1,
        OnRoute = 2,
        InRepair = 3,
        Retired = 4,
    }

    [Serializable]
    public sealed class FreightWagon
    {
        [SerializeField]
        private string wagonId = string.Empty;

        [SerializeField]
        private string displayName = string.Empty;

        [SerializeField, Min(1)]
        private int capacityUnits = 600;

        [SerializeField, Range(0f, 1f)]
        private float condition01 = 1f;

        [SerializeField]
        private FreightWagonStatus status = FreightWagonStatus.Available;

        public string WagonId => wagonId ?? string.Empty;
        public string DisplayName => displayName ?? string.Empty;

        /// <summary>
        /// Pass IV: when attached to a registry asset (wagon reconciliation),
        /// capacity reads the asset's payload — one truth, not two.
        /// </summary>
        public int CapacityUnits => attachedAsset != null
            ? Math.Max(1, (int)attachedAsset.PayloadMassCapacityKg)
            : Mathf.Max(1, capacityUnits);

        /// <summary>
        /// Pass IV: condition truth lives on the attached registry asset after
        /// wagon reconciliation. The serialized field below is the migration
        /// carrier for pre-reconciliation saves.
        /// </summary>
        public float Condition01 => attachedAsset != null
            ? attachedAsset.Condition01
            : Mathf.Clamp01(condition01);
        public FreightWagonStatus Status => status;

        /// <summary>Pass IV: single-truth link to the registry asset. Not serialized.</summary>
        private EquipmentAsset attachedAsset;

        /// <summary>
        /// Pass IV: attaches the registry asset that is the condition truth for
        /// this wagon. After attachment, condition reads/writes go through the
        /// asset; the legacy field is kept in sync for old save readers.
        /// </summary>
        public void AttachAsset(EquipmentAsset asset)
        {
            attachedAsset = asset;
            if (asset != null) condition01 = asset.Condition01;
        }

        /// <summary>Pass IV: the attached registry asset, or null before reconciliation.</summary>
        public EquipmentAsset AttachedAsset => attachedAsset;

        public FreightWagon(string wagonId, string displayName, int capacityUnits)
        {
            this.wagonId = wagonId ?? string.Empty;
            this.displayName = displayName ?? string.Empty;
            this.capacityUnits = Mathf.Max(1, capacityUnits);
        }

        internal void SetStatus(FreightWagonStatus value) => status = value;

        /// <summary>Pass IV: damage applies to the asset when attached (single condition truth).</summary>
        public void Damage(float amount01)
        {
            if (attachedAsset != null) attachedAsset.ApplyWear(Mathf.Abs(amount01));
            else condition01 = Mathf.Clamp01(condition01 - Mathf.Abs(amount01));
        }

        /// <summary>Pass IV: repair applies to the asset when attached (single condition truth).</summary>
        internal void Repair(float amount01)
        {
            if (attachedAsset != null) attachedAsset.RepairTo(attachedAsset.Condition01 + Mathf.Abs(amount01));
            else condition01 = Mathf.Clamp01(condition01 + Mathf.Abs(amount01));
        }
    }

    /// <summary>
    /// BIZ-3: a draft-team reservation — one wagon + its draft animals + its driver,
    /// reserved as a unit. While reserved, none of the three can be used elsewhere
    /// (Canon §3.6: a wagon in repair cannot haul; a driver on a task isn't elsewhere).
    /// </summary>
    public sealed class DraftTeamReservation
    {
        public string ReservationId { get; }
        public string WagonId { get; }
        public IReadOnlyList<EntityId> DraftAnimalIds { get; }
        public EntityId DriverPersonId { get; }
        public string Purpose { get; }

        public DraftTeamReservation(string reservationId, string wagonId,
            List<EntityId> draftAnimalIds, EntityId driverPersonId, string purpose)
        {
            ReservationId = reservationId ?? string.Empty;
            WagonId = wagonId ?? string.Empty;
            DraftAnimalIds = draftAnimalIds ?? new List<EntityId>();
            DriverPersonId = driverPersonId;
            Purpose = purpose ?? string.Empty;
        }
    }

    /// <summary>
    /// BIZ-3: the ONE physical pool shared by the freight company's hauling and repair
    /// units (Canon §3.6; Tech X §3.4 TECH LOCK — a shared reservation cannot satisfy
    /// two simultaneous tasks). Wagons, draft animals (HF-2 registry), drivers, and
    /// yard space live here; both units reserve from it, never duplicate it.
    /// </summary>
    [Serializable]
    public sealed class FreightResourcePool
    {
        [SerializeField]
        private List<FreightWagon> wagons = new List<FreightWagon>();

        [SerializeField]
        private List<string> reservedWagonIds = new List<string>();

        /// <summary>
        /// EQU-3: animal + driver reservations now go through the ONE draft-power
        /// authority (Tech X §3.8) — the same service the plow work unit uses.
        /// Pass IV: the draft service reserves through the unified reservation
        /// authority, and the hub persists both — draft reservations survive a
        /// save/load round-trip.
        /// </summary>
        private DraftPowerService draftPower;

        /// <summary>
        /// Pass IV: the asset registry that is the wagon truth after
        /// reconciliation (see FreightWagonAssetAdapter). Attached by the hub;
        /// null in standalone/test use, where the legacy wagon list decides.
        /// </summary>
        private TransportAssetRegistry wagonRegistry;

        /// <summary>Pass IV: absolute game seconds for reservation windows. Defaults to 0.</summary>
        public Func<float> NowGameSeconds { get; set; }

        /// <summary>
        /// EQU-3: the shared draft-power authority (Tech X §3.8) — the same
        /// service the plow work unit reserves through. Public so the plow unit
        /// and the freight pool provably share one authority.
        /// </summary>
        public DraftPowerService DraftPower
        {
            get
            {
                if (draftPower == null)
                    draftPower = new DraftPowerService();
                return draftPower;
            }
        }

        /// <summary>
        /// Pass IV: attaches the hub-owned draft-power service (which reserves
        /// through the persisted unified authority). The hub calls this on boot
        /// and after every load.
        /// </summary>
        public void AttachDraftPower(DraftPowerService service)
        {
            if (service != null) draftPower = service;
        }

        /// <summary>
        /// Pass IV: attaches the wagon asset registry — the single truth for
        /// wagon condition and reservation after reconciliation.
        /// </summary>
        public void AttachWagonRegistry(TransportAssetRegistry registry)
        {
            wagonRegistry = registry;
        }

        /// <summary>Pass IV: the attached wagon registry, or null before reconciliation.</summary>
        public TransportAssetRegistry WagonRegistry => wagonRegistry;

        /// <summary>
        /// Pass IV: migrates legacy freight wagons onto the equipment-asset
        /// registry (additive, idempotent). Legacy wagons missing from the
        /// registry are imported once via <see cref="FreightWagonAssetAdapter"/>;
        /// registry assets are never overwritten. After migration each legacy
        /// wagon attaches to its asset so condition reads/writes share one
        /// truth. Legacy reserved ids become authority holds when no live hold
        /// exists (migration carrier for pre-reconciliation saves).
        /// </summary>
        public void MigrateWagonsToRegistry(List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            if (wagonRegistry == null)
            {
                diagnostics.Add("FreightResourcePool: no wagon registry attached — migration skipped.");
                return;
            }
            int migrated = 0;
            if (wagons != null)
            {
                foreach (FreightWagon wagon in wagons)
                {
                    if (wagon == null || string.IsNullOrWhiteSpace(wagon.WagonId)) continue;
                    EquipmentAsset asset = wagonRegistry.Get(wagon.WagonId);
                    if (asset == null)
                    {
                        asset = FreightWagonAssetAdapter.ToEquipmentAsset(wagon);
                        string problem = wagonRegistry.Register(asset);
                        if (problem != null)
                        {
                            diagnostics.Add(problem);
                            continue;
                        }
                        migrated++;
                    }
                    wagon.AttachAsset(asset);
                }
            }
            var authority = DraftPower.Authority as EquipmentReservationService;
            if (authority != null && reservedWagonIds != null)
            {
                foreach (string wagonId in reservedWagonIds)
                {
                    if (string.IsNullOrWhiteSpace(wagonId)) continue;
                    string key = ReservationKeyConventions.Asset(wagonId);
                    EquipmentAsset asset = wagonRegistry.Get(wagonId);
                    bool held = (asset != null && asset.IsReserved) || authority.IsReserved(key, 0f);
                    if (held) continue;
                    var request = new ReservationRequest(
                        "freight-team:legacy:" + wagonId, "legacy freight reservation (migrated)");
                    request.Items.Add(new ReservationItem(key, 1f, 0f, float.MaxValue));
                    authority.TryReserveAll(request, out _);
                }
            }
            diagnostics.Add(
                $"FreightResourcePool: wagon reconciliation migrated {migrated} legacy wagons " +
                "to the asset registry — one condition truth, one reservation truth.");
        }

        private DraftPowerService DraftAnimals => DraftPower;

        [SerializeField, Min(0)]
        private int yardCapacityWagons = 6;

        public IReadOnlyList<FreightWagon> Wagons => wagons;
        public int YardCapacityWagons => Mathf.Max(0, yardCapacityWagons);

        public void AddWagon(FreightWagon wagon)
        {
            wagons ??= new List<FreightWagon>();
            if (wagon != null && !string.IsNullOrWhiteSpace(wagon.WagonId) && GetWagon(wagon.WagonId) == null)
            {
                wagons.Add(wagon);
                // Pass IV: keep the registry in step — a wagon added to the pool
                // exists as an asset too (single truth from birth).
                if (wagonRegistry != null)
                {
                    EquipmentAsset asset = wagonRegistry.Get(wagon.WagonId)
                        ?? FreightWagonAssetAdapter.ToEquipmentAsset(wagon);
                    if (wagonRegistry.Register(asset) == null)
                        wagon.AttachAsset(asset);
                }
            }
        }

        public FreightWagon GetWagon(string wagonId)
        {
            if (wagons == null || string.IsNullOrWhiteSpace(wagonId))
            {
                return null;
            }

            foreach (FreightWagon wagon in wagons)
            {
                if (wagon.WagonId == wagonId)
                {
                    return wagon;
                }
            }

            return null;
        }

        /// <summary>
        /// Reserves a roadworthy wagon + draft animals + driver as one team.
        /// Fails loudly when anything is unavailable — never double-books.
        /// Pass IV: the wagon instance is reserved through the unified
        /// reservation authority (key "asset:{wagonId}") AND the asset-level
        /// flag, atomically with the draft team — a wagon on a freight team
        /// cannot satisfy a second task.
        /// </summary>
        public bool TryReserveTeam(
            int requiredCapacityUnits,
            List<EntityId> availableDraftAnimals,
            List<EntityId> availableDrivers,
            string purpose,
            out DraftTeamReservation reservation,
            List<string> diagnostics)
        {
            reservation = null;
            diagnostics ??= new List<string>();
            reservedWagonIds ??= new List<string>();
            DraftPower.NowGameSeconds = NowGameSeconds ?? (() => 0f);

            FreightWagon wagon = SelectAvailableWagon(requiredCapacityUnits);

            if (wagon == null)
            {
                diagnostics.Add($"No available wagon with {requiredCapacityUnits} capacity " +
                    "(in repair / on route wagons cannot haul — Canon §3.6).");
                return false;
            }

            // EQU-3: animals + driver reserve through the ONE draft-power authority
            // (Tech X §3.8) — the same service the plow work unit uses. The pool's
            // legacy behavior is preserved: any draft animal qualifies here.
            if (!DraftAnimals.TryReserveAnimals(availableDraftAnimals, 2, null,
                purpose, out List<EntityId> animals, diagnostics))
            {
                diagnostics.Add("Fewer than 2 unreserved draft animals available for the team.");
                return false;
            }

            if (!DraftAnimals.TryReserveDriver(availableDrivers, out EntityId driver, diagnostics))
            {
                DraftAnimals.ReleaseAnimals(animals);
                diagnostics.Add("No unreserved driver available for the team.");
                return false;
            }

            // Pass IV: reserve the wagon instance itself through the unified
            // authority, atomically with the team — rollback the team when the
            // wagon cannot be held (no partial acquisition).
            string teamId = Guid.NewGuid().ToString("N");
            string ownerTaskId = "freight-team:" + teamId;
            float now = DraftPower.NowGameSeconds != null ? DraftPower.NowGameSeconds() : 0f;
            string wagonKey = ReservationKeyConventions.Asset(wagon.WagonId);
            var wagonRequest = new ReservationRequest(ownerTaskId, purpose ?? "freight haul");
            wagonRequest.Items.Add(new ReservationItem(wagonKey, 1f, now, float.MaxValue));
            string wagonRefusal = DraftPower.Authority.TryReserveAll(wagonRequest, out _);
            EquipmentAsset asset = wagon.AttachedAsset;
            string assetProblem = null;
            if (wagonRefusal == null && asset != null)
                assetProblem = asset.Reserve(ownerTaskId, purpose ?? "freight haul");
            if (wagonRefusal != null || assetProblem != null)
            {
                DraftPower.Authority.ReleaseAllForTask(ownerTaskId);
                DraftAnimals.ReleaseAnimals(animals);
                DraftAnimals.ReleaseDriver(driver);
                diagnostics.Add(wagonRefusal ?? assetProblem);
                return false;
            }

            reservedWagonIds.Add(wagon.WagonId);
            wagon.SetStatus(FreightWagonStatus.OnRoute);

            reservation = new DraftTeamReservation(
                teamId, wagon.WagonId, animals, driver, purpose);
            diagnostics.Add($"Reserved team {reservation.ReservationId}: wagon {wagon.WagonId}, " +
                $"{animals.Count} draft animals, driver {driver} — for '{purpose}'.");
            return true;
        }

        /// <summary>
        /// Pass IV: selects an available wagon honoring the single reservation
        /// truth — pool status, the legacy reserved list, the asset-level flag,
        /// and the unified authority must ALL agree the wagon is free.
        /// </summary>
        private FreightWagon SelectAvailableWagon(int requiredCapacityUnits)
        {
            if (wagons == null) return null;
            foreach (FreightWagon candidate in wagons)
            {
                if (candidate == null) continue;
                if (candidate.Status != FreightWagonStatus.Available) continue;
                if (candidate.CapacityUnits < requiredCapacityUnits) continue;
                if (reservedWagonIds != null && reservedWagonIds.Contains(candidate.WagonId)) continue;
                if (IsWagonHeld(candidate)) continue;
                return candidate;
            }
            return null;
        }

        /// <summary>Pass IV: wagon held = asset flag or unified-authority hold.</summary>
        private bool IsWagonHeld(FreightWagon wagon)
        {
            if (wagon == null) return true;
            if (wagon.AttachedAsset != null && wagon.AttachedAsset.IsReserved) return true;
            var authority = DraftPower.Authority;
            if (authority != null)
            {
                float now = NowGameSeconds != null ? NowGameSeconds() : 0f;
                if (authority.IsReserved(ReservationKeyConventions.Asset(wagon.WagonId), now)) return true;
            }
            return false;
        }

        /// <summary>Releases a team back to the pool.</summary>
        public void ReleaseTeam(DraftTeamReservation reservation, float wagonWear01 = 0.02f)
        {
            if (reservation == null)
            {
                return;
            }

            // Pass IV: release the wagon instance through the same truths it
            // was reserved through — authority hold, asset flag, legacy list.
            DraftPower.Authority.ReleaseAllForTask("freight-team:" + reservation.ReservationId);
            FreightWagon wagon = GetWagon(reservation.WagonId);
            if (wagon?.AttachedAsset != null) wagon.AttachedAsset.Release();
            reservedWagonIds?.Remove(reservation.WagonId);
            // EQU-3: released through the shared draft-power authority.
            DraftAnimals.ReleaseAnimals(reservation.DraftAnimalIds);
            DraftAnimals.ReleaseDriver(reservation.DriverPersonId);

            if (wagon != null && wagon.Status == FreightWagonStatus.OnRoute)
            {
                wagon.SetStatus(FreightWagonStatus.Available);
                wagon.Damage(wagonWear01); // Hauling wears wagons; repair restores them.
            }
        }

        /// <summary>
        /// Pass IV: releases a team after a trip, wearing the wagon from its
        /// condition profile (per-mile rate) plus the trip load term — no flat
        /// universal wear. Falls back to the legacy explicit wear amount when
        /// no profile exists for the wagon kind.
        /// </summary>
        public void ReleaseTeamAfterTrip(DraftTeamReservation reservation, float miles, float loadRatio01)
        {
            float wear = 0.02f; // legacy explicit default when no profile is authored
            if (reservation != null)
            {
                FreightWagon wagon = GetWagon(reservation.WagonId);
                string kind = wagon?.AttachedAsset?.Kind ?? FreightWagonAssetAdapter.MigratedWagonKind;
                var profile = EquipmentConditionProfiles.GetProfile(kind);
                if (profile != null)
                    wear = profile.WearPerMile01 * Math.Max(0f, miles)
                        + Math.Max(0f, Math.Min(1f, loadRatio01)) * 0.01f;
            }
            ReleaseTeam(reservation, wear);
        }

        /// <summary>Sends a wagon to the repair unit. A wagon in repair cannot haul.</summary>
        public bool TrySendToRepair(string wagonId, List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            FreightWagon wagon = GetWagon(wagonId);
            if (wagon == null)
            {
                diagnostics.Add($"Unknown wagon '{wagonId}'.");
                return false;
            }

            if (wagon.Status != FreightWagonStatus.Available)
            {
                diagnostics.Add($"Wagon '{wagonId}' is {wagon.Status}; only available wagons enter repair.");
                return false;
            }

            if (reservedWagonIds != null && reservedWagonIds.Contains(wagonId))
            {
                diagnostics.Add($"Wagon '{wagonId}' is reserved and cannot enter repair.");
                return false;
            }

            if (IsWagonHeld(wagon))
            {
                diagnostics.Add($"Wagon '{wagonId}' is reserved (authority hold) and cannot enter repair.");
                return false;
            }

            wagon.SetStatus(FreightWagonStatus.InRepair);
            diagnostics.Add($"Wagon '{wagonId}' entered repair (unavailable for hauling — Canon §3.6).");
            return true;
        }

        /// <summary>Returns a wagon from repair to available.</summary>
        public bool TryCompleteRepair(string wagonId, float restoredCondition01, List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            FreightWagon wagon = GetWagon(wagonId);
            if (wagon == null || wagon.Status != FreightWagonStatus.InRepair)
            {
                diagnostics.Add($"Wagon '{wagonId}' is not in repair.");
                return false;
            }

            wagon.Repair(restoredCondition01);
            wagon.SetStatus(FreightWagonStatus.Available);
            diagnostics.Add($"Wagon '{wagonId}' repaired to {wagon.Condition01:P0} condition; available.");
            return true;
        }

        public int AvailableWagonCount
        {
            get
            {
                int count = 0;
                if (wagons != null)
                {
                    foreach (FreightWagon wagon in wagons)
                    {
                        if (wagon.Status == FreightWagonStatus.Available
                            && (reservedWagonIds == null || !reservedWagonIds.Contains(wagon.WagonId))
                            && !IsWagonHeld(wagon))
                        {
                            count++;
                        }
                    }
                }

                return count;
            }
        }

        /// <summary>
        /// CLN-1: captures freight pool state for the save pipeline. Wagons and
        /// reservations round-trip; yard capacity is configuration. Pass IV: the
        /// legacy reservedWagonIds are re-derived from the unified authority so
        /// the DTO never contradicts the single reservation truth.
        /// </summary>
        public FreightResourcePoolSaveDto CaptureSaveDto()
        {
            reservedWagonIds ??= new List<string>();
            var authority = DraftPower.Authority;
            if (authority != null && wagons != null)
            {
                reservedWagonIds.Clear();
                float now = NowGameSeconds != null ? NowGameSeconds() : 0f;
                foreach (FreightWagon wagon in wagons)
                {
                    if (wagon == null || string.IsNullOrWhiteSpace(wagon.WagonId)) continue;
                    if (wagon.AttachedAsset != null && wagon.AttachedAsset.IsReserved)
                        reservedWagonIds.Add(wagon.WagonId);
                    else if (authority.IsReserved(ReservationKeyConventions.Asset(wagon.WagonId), now))
                        reservedWagonIds.Add(wagon.WagonId);
                }
            }
            return new FreightResourcePoolSaveDto
            {
                wagons = new List<FreightWagon>(wagons ?? new List<FreightWagon>()),
                reservedWagonIds = new List<string>(reservedWagonIds ?? new List<string>()),
            };
        }

        /// <summary>
        /// CLN-1: restores freight pool state from the save pipeline. Pass IV:
        /// the attached draft-power service is never reset here — draft/animal
        /// holds persist through the hub-owned unified authority, and a
        /// restored game never silently drops a live reservation. Re-attach the
        /// registry and call MigrateWagonsToRegistry after loading.
        /// </summary>
        public void LoadFromSaveDto(FreightResourcePoolSaveDto dto)
        {
            wagons.Clear();
            reservedWagonIds.Clear();
            if (dto == null)
            {
                return;
            }

            if (dto.wagons != null)
            {
                wagons.AddRange(dto.wagons);
            }

            if (dto.reservedWagonIds != null)
            {
                reservedWagonIds.AddRange(dto.reservedWagonIds);
            }
        }
    }

    /// <summary>
    /// BIZ-3: a named freight route (origin → destination with a time estimate).
    /// </summary>
    [Serializable]
    public sealed class FreightRoute
    {
        [SerializeField]
        private string routeId = string.Empty;

        [SerializeField]
        private string displayName = string.Empty;

        [SerializeField]
        private string originLabel = string.Empty;

        [SerializeField]
        private string destinationLabel = string.Empty;

        [SerializeField, Min(1)]
        private int transitMinutesOneWay = 120;

        public string RouteId => routeId ?? string.Empty;
        public string DisplayName => displayName ?? string.Empty;
        public string OriginLabel => originLabel ?? string.Empty;
        public string DestinationLabel => destinationLabel ?? string.Empty;
        public int TransitMinutesOneWay => Mathf.Max(1, transitMinutesOneWay);

        public FreightRoute(string routeId, string displayName, string origin, string destination, int transitMinutesOneWay)
        {
            this.routeId = routeId ?? string.Empty;
            this.displayName = displayName ?? string.Empty;
            originLabel = origin ?? string.Empty;
            destinationLabel = destination ?? string.Empty;
            this.transitMinutesOneWay = Mathf.Max(1, transitMinutesOneWay);
        }
    }
}
