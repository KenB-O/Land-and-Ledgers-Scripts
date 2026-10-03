using System;
using System.Collections.Generic;
using LandLedgers.Economy.Creation;
using LandLedgers.Economy.DraftPower;
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
        public int CapacityUnits => Mathf.Max(1, capacityUnits);
        public float Condition01 => Mathf.Clamp01(condition01);
        public FreightWagonStatus Status => status;

        public FreightWagon(string wagonId, string displayName, int capacityUnits)
        {
            this.wagonId = wagonId ?? string.Empty;
            this.displayName = displayName ?? string.Empty;
            this.capacityUnits = Mathf.Max(1, capacityUnits);
        }

        internal void SetStatus(FreightWagonStatus value) => status = value;
        internal void Damage(float amount01) => condition01 = Mathf.Clamp01(condition01 - Mathf.Abs(amount01));
        internal void Repair(float amount01) => condition01 = Mathf.Clamp01(condition01 + Mathf.Abs(amount01));
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
        /// Transient runtime state only; never persisted (see LoadFromSaveDto).
        /// </summary>
        private DraftPowerService draftPower;

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

            FreightWagon wagon = null;
            if (wagons != null)
            {
                foreach (FreightWagon candidate in wagons)
                {
                    if (candidate.Status == FreightWagonStatus.Available
                        && candidate.CapacityUnits >= requiredCapacityUnits
                        && !reservedWagonIds.Contains(candidate.WagonId))
                    {
                        wagon = candidate;
                        break;
                    }
                }
            }

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

            reservedWagonIds.Add(wagon.WagonId);
            wagon.SetStatus(FreightWagonStatus.OnRoute);

            reservation = new DraftTeamReservation(
                Guid.NewGuid().ToString("N"), wagon.WagonId, animals, driver, purpose);
            diagnostics.Add($"Reserved team {reservation.ReservationId}: wagon {wagon.WagonId}, " +
                $"{animals.Count} draft animals, driver {driver} — for '{purpose}'.");
            return true;
        }

        /// <summary>Releases a team back to the pool.</summary>
        public void ReleaseTeam(DraftTeamReservation reservation, float wagonWear01 = 0.02f)
        {
            if (reservation == null)
            {
                return;
            }

            reservedWagonIds?.Remove(reservation.WagonId);
            // EQU-3: released through the shared draft-power authority.
            DraftAnimals.ReleaseAnimals(reservation.DraftAnimalIds);
            DraftAnimals.ReleaseDriver(reservation.DriverPersonId);

            FreightWagon wagon = GetWagon(reservation.WagonId);
            if (wagon != null && wagon.Status == FreightWagonStatus.OnRoute)
            {
                wagon.SetStatus(FreightWagonStatus.Available);
                wagon.Damage(wagonWear01); // Hauling wears wagons; repair restores them.
            }
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
                            && (reservedWagonIds == null || !reservedWagonIds.Contains(wagon.WagonId)))
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
        /// reservations round-trip; yard capacity is configuration.
        /// </summary>
        public FreightResourcePoolSaveDto CaptureSaveDto()
        {
            return new FreightResourcePoolSaveDto
            {
                wagons = new List<FreightWagon>(wagons ?? new List<FreightWagon>()),
                reservedWagonIds = new List<string>(reservedWagonIds ?? new List<string>()),
            };
        }

        /// <summary>CLN-1: restores freight pool state from the save pipeline.</summary>
        public void LoadFromSaveDto(FreightResourcePoolSaveDto dto)
        {
            wagons.Clear();
            reservedWagonIds.Clear();
            // EQU-3: draft reservations are transient runtime state — a fresh
            // authority on load means nothing stays reserved across a save.
            draftPower = new DraftPowerService();
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
