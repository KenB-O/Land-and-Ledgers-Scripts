using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.World.Journeys
{
    /// <summary>Who is traveling off-map.</summary>
    public enum OffMapTravelerKind
    {
        Unspecified = 0,
        Person = 1,   // a real PersonState id; the record is untouched, identity stable
        Vehicle = 2,  // a wagon/team id string; the same vehicle id returns
    }

    /// <summary>Lifecycle of one off-map journey. No other states exist.</summary>
    public enum OffMapJourneyState
    {
        Unspecified = 0,
        Departed = 1,  // off-map now; completes only when the due day arrives
        Returned = 2,  // completed exactly once; cargo delivered exactly once
        Cancelled = 3, // called off; cargo handed back to the origin exactly once
    }

    /// <summary>
    /// One persistent off-map journey: identity, route, time, cargo, obligations.
    /// The traveler is the SAME entity on return (same person id / same vehicle
    /// id). Cargo is taken from the origin once at departure and delivered once
    /// at return or cancellation; the authority never duplicates or teleports it.
    /// </summary>
    [Serializable]
    public sealed class OffMapJourneyRecord
    {
        public string JourneyId = string.Empty;
        public OffMapTravelerKind TravelerKind;
        public int PersonId = -1;
        public string VehicleId = string.Empty;
        public string OriginLocationId = string.Empty;
        public string DestinationLabel = string.Empty; // off-map destination (authored)
        public string GatewayLocationId = string.Empty; // journey-model gateway used
        public List<string> RouteLegLocationIds = new List<string>();
        public float RouteMiles;
        public int GatewayTravelDays;   // routed days origin <-> gateway (each way)
        public int OffMapLegDays;       // authored days beyond the gateway (each way)
        public int DepartureDayIndex;
        public int DueReturnDayIndex = -1; // -1 = one-way (migration); no return expected
        public int ActualReturnDayIndex = -1;
        public Dictionary<string, int> CargoUnits = new Dictionary<string, int>();
        /// <summary>
        /// Goods bought off-map during the journey (the purchase manifest).
        /// Unlike taken cargo, these were never in the origin inventory: they
        /// are delivered on successful return, and DROPPED (not returned) on
        /// cancellation, since the cash already left the system off-map.
        /// </summary>
        public Dictionary<string, int> PurchaseManifestUnits = new Dictionary<string, int>();
        public List<string> ObligationIds = new List<string>();
        public string Purpose = string.Empty;
        public OffMapJourneyState State = OffMapJourneyState.Unspecified;
        public string CancelReason = string.Empty;

        public bool IsRoundTrip => DueReturnDayIndex >= 0;
        public bool IsActive => State == OffMapJourneyState.Departed;

        /// <summary>Authoritative location label while departed. Never a map coordinate.</summary>
        public string OffMapLocationLabel => "off-map:" + JourneyId;

        /// <summary>
        /// Pass VIII (Worker 9): deep snapshot. CaptureSaveDto must hand out a
        /// stable snapshot — aliasing the live record meant post-capture
        /// mutations (completion, cancellation) leaked into the saved DTO.
        /// </summary>
        public OffMapJourneyRecord Copy()
        {
            return new OffMapJourneyRecord
            {
                JourneyId = JourneyId,
                TravelerKind = TravelerKind,
                PersonId = PersonId,
                VehicleId = VehicleId,
                OriginLocationId = OriginLocationId,
                DestinationLabel = DestinationLabel,
                GatewayLocationId = GatewayLocationId,
                RouteLegLocationIds = RouteLegLocationIds != null
                    ? new List<string>(RouteLegLocationIds) : new List<string>(),
                RouteMiles = RouteMiles,
                GatewayTravelDays = GatewayTravelDays,
                OffMapLegDays = OffMapLegDays,
                DepartureDayIndex = DepartureDayIndex,
                DueReturnDayIndex = DueReturnDayIndex,
                ActualReturnDayIndex = ActualReturnDayIndex,
                CargoUnits = CargoUnits != null
                    ? new Dictionary<string, int>(CargoUnits, System.StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase),
                PurchaseManifestUnits = PurchaseManifestUnits != null
                    ? new Dictionary<string, int>(PurchaseManifestUnits, System.StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase),
                ObligationIds = ObligationIds != null
                    ? new List<string>(ObligationIds) : new List<string>(),
                Purpose = Purpose,
                State = State,
                CancelReason = CancelReason,
            };
        }
    }

    /// <summary>
    /// Pass V (Worker 4): the persistent journey authority for person and vehicle
    /// off-map travel (migration, external freight drivers, wagon off-map legs).
    ///
    /// This does NOT create a second movement authority. Route math comes from
    /// JourneyModel (real miles, real minutes to the off-map gateway); off-map
    /// legs carry authored travel time (Canon 13.1); time accounting uses
    /// JourneyTravel.TransitDaysForMinutes. Person travel time is booked through
    /// the caller's task authority; this class persists the journey itself.
    ///
    /// Hard rules:
    /// - Identity: the same person id / vehicle id departs and returns.
    /// - Cargo: verified sufficient BEFORE departure (all-or-nothing), taken once
    ///   via the caller's take-callback, delivered once via the caller's
    ///   deliver-callback on return (or back to origin on cancel).
    /// - Purchase manifest: goods bought off-map are recorded as a manifest
    ///   (never taken from origin stock), delivered once on return, dropped
    ///   on cancel.
    /// - Time: a journey completes only when its due return day arrives; early
    ///   completion is refused (no teleporting).
    /// - No double completion: CompleteDueJourneys flips Departed to Returned
    ///   exactly once; re-running a day is idempotent.
    /// - Save/load is clear-then-load (CLN-1 pattern).
    /// </summary>
    public sealed class OffMapJourneyAuthority
    {
        private readonly JourneyModel journeyModel;
        private readonly Dictionary<string, OffMapJourneyRecord> byId =
            new Dictionary<string, OffMapJourneyRecord>(StringComparer.Ordinal);
        private readonly List<string> diagnostics = new List<string>();

        public OffMapJourneyAuthority(JourneyModel journeyModel)
        {
            this.journeyModel = journeyModel;
        }

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public int ActiveJourneyCount => CountActive();
        public IEnumerable<OffMapJourneyRecord> AllJourneys() => byId.Values;

        private int CountActive()
        {
            int count = 0;
            foreach (var record in byId.Values)
            {
                if (record.IsActive) count++;
            }
            return count;
        }

        /// <summary>
        /// A person leaves the map (migration, long-haul driving, visiting kin).
        /// One-way when expectedReturnDays is negative; otherwise a round trip.
        /// </summary>
        public OffMapJourneyRecord DepartPerson(
            int personId,
            string originLocationId,
            string destinationLabel,
            int offMapLegDays,
            int expectedReturnDays,
            string purpose,
            Dictionary<string, int> cargoUnits,
            List<string> obligationIds,
            int dayIndex,
            Func<string, int> inventoryUnitsOf,
            Func<string, int, string> takeCargo,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            return Depart(OffMapTravelerKind.Person, personId, null,
                originLocationId, destinationLabel, offMapLegDays, expectedReturnDays,
                purpose, cargoUnits, obligationIds, dayIndex,
                inventoryUnitsOf, takeCargo, diag);
        }

        /// <summary>
        /// A vehicle (wagon/team) leaves on an external route and returns.
        /// Vehicles always travel round trips; one-way vehicle departure is refused.
        /// </summary>
        public OffMapJourneyRecord DepartVehicle(
            string vehicleId,
            string originLocationId,
            string destinationLabel,
            int offMapLegDays,
            string purpose,
            Dictionary<string, int> cargoUnits,
            List<string> obligationIds,
            int dayIndex,
            Func<string, int> inventoryUnitsOf,
            Func<string, int, string> takeCargo,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(vehicleId))
            {
                diag.Add("OffMapJourneyAuthority: vehicle departure needs a vehicle id.");
                return null;
            }
            if (IsOffMapVehicle(vehicleId))
            {
                diag.Add($"OffMapJourneyAuthority: vehicle '{vehicleId}' is already off-map; refusing a second departure.");
                return null;
            }
            // Round trip: out and back through the gateway.
            int roundTripDays = -1; // computed below from routed + authored legs
            var record = Depart(OffMapTravelerKind.Vehicle, -1, vehicleId,
                originLocationId, destinationLabel, offMapLegDays, roundTripDays,
                purpose, cargoUnits, obligationIds, dayIndex,
                inventoryUnitsOf, takeCargo, diag);
            return record;
        }

        private OffMapJourneyRecord Depart(
            OffMapTravelerKind kind,
            int personId,
            string vehicleId,
            string originLocationId,
            string destinationLabel,
            int offMapLegDays,
            int expectedReturnDays,
            string purpose,
            Dictionary<string, int> cargoUnits,
            List<string> obligationIds,
            int dayIndex,
            Func<string, int> inventoryUnitsOf,
            Func<string, int, string> takeCargo,
            List<string> diag)
        {
            if (journeyModel == null)
            {
                diag.Add("OffMapJourneyAuthority: a journey model is required for routing.");
                return null;
            }
            if (kind == OffMapTravelerKind.Person)
            {
                if (personId < 0)
                {
                    diag.Add("OffMapJourneyAuthority: person departure needs a person id.");
                    return null;
                }
                if (IsOffMapPerson(personId))
                {
                    diag.Add($"OffMapJourneyAuthority: P{personId} is already off-map; refusing a second departure.");
                    return null;
                }
            }
            if (string.IsNullOrWhiteSpace(originLocationId) || journeyModel.GetLocation(originLocationId) == null)
            {
                diag.Add($"OffMapJourneyAuthority: unknown origin location '{originLocationId}'.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(destinationLabel))
            {
                diag.Add("OffMapJourneyAuthority: an off-map destination label is required.");
                return null;
            }

            // Route to the nearest off-map gateway through the journey model.
            JourneyLocation gateway = FindNearestGateway(originLocationId);
            if (gateway == null)
            {
                diag.Add($"OffMapJourneyAuthority: no off-map gateway reachable from '{originLocationId}'.");
                return null;
            }
            JourneyRoute route = journeyModel.FindRoute(originLocationId, gateway.LocationId, TravelMode.Wagon);
            if (!route.Found)
            {
                diag.Add("OffMapJourneyAuthority: " + route.Diagnostic);
                return null;
            }

            int gatewayDays = JourneyTravel.TransitDaysForMinutes(route.TotalMinutes);
            int legDays = Math.Max(0, offMapLegDays);
            int dueReturn = -1;
            if (kind == OffMapTravelerKind.Vehicle)
            {
                // Vehicles always return: out and back.
                dueReturn = dayIndex + 2 * (gatewayDays + legDays);
            }
            else if (expectedReturnDays >= 0)
            {
                dueReturn = dayIndex + 2 * (gatewayDays + legDays);
                if (expectedReturnDays > 0)
                {
                    dueReturn = dayIndex + expectedReturnDays;
                }
            }

            string journeyId = $"offmap-{kind.ToString().ToLowerInvariant()}-" +
                (kind == OffMapTravelerKind.Person ? personId.ToString() : (vehicleId ?? "unknown")) +
                $"-{dayIndex}";
            if (byId.ContainsKey(journeyId))
            {
                diag.Add($"OffMapJourneyAuthority: journey '{journeyId}' already exists; ids are never reused.");
                return null;
            }

            // Cargo: verify ALL units exist before taking ANY (all-or-nothing).
            var cargo = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (cargoUnits != null)
            {
                foreach (var kv in cargoUnits)
                {
                    if (kv.Value <= 0) continue;
                    int have = inventoryUnitsOf != null ? inventoryUnitsOf(kv.Key) : 0;
                    if (have < kv.Value)
                    {
                        diag.Add($"OffMapJourneyAuthority: refusing departure; cargo needs {kv.Value}u {kv.Key} but only {have}u exist.");
                        return null;
                    }
                    cargo[kv.Key] = kv.Value;
                }
            }
            if (takeCargo != null)
            {
                foreach (var kv in cargo)
                {
                    string refusal = takeCargo(kv.Key, kv.Value);
                    if (refusal != null)
                    {
                        diag.Add($"OffMapJourneyAuthority: cargo take failed for {kv.Value}u {kv.Key}: {refusal}.");
                        return null;
                    }
                }
            }

            var record = new OffMapJourneyRecord
            {
                JourneyId = journeyId,
                TravelerKind = kind,
                PersonId = personId,
                VehicleId = vehicleId ?? string.Empty,
                OriginLocationId = originLocationId,
                DestinationLabel = destinationLabel,
                GatewayLocationId = gateway.LocationId,
                RouteLegLocationIds = new List<string>(route.LegLocationIds),
                RouteMiles = route.TotalMiles,
                GatewayTravelDays = gatewayDays,
                OffMapLegDays = legDays,
                DepartureDayIndex = dayIndex,
                DueReturnDayIndex = dueReturn,
                CargoUnits = cargo,
                ObligationIds = obligationIds != null ? new List<string>(obligationIds) : new List<string>(),
                Purpose = purpose ?? string.Empty,
                State = OffMapJourneyState.Departed,
            };
            byId[journeyId] = record;
            diag.Add($"OffMapJourneyAuthority: {DescribeTraveler(record)} departed {originLocationId} for " +
                $"'{destinationLabel}' via {gateway.LocationId} ({route.TotalMiles:F1} mi to gateway); " +
                (record.IsRoundTrip ? $"due back day {dueReturn}." : "one-way; no return expected."));
            return record;
        }

        private JourneyLocation FindNearestGateway(string originLocationId)
        {
            JourneyLocation best = null;
            float bestMiles = float.PositiveInfinity;
            foreach (var location in journeyModel.AllLocations())
            {
                if (location == null || location.Kind != JourneyLocationKind.OffMapGateway) continue;
                JourneyRoute route = journeyModel.FindRoute(originLocationId, location.LocationId, TravelMode.Wagon);
                if (!route.Found) continue;
                if (route.TotalMiles < bestMiles)
                {
                    bestMiles = route.TotalMiles;
                    best = location;
                }
            }
            return best;
        }

        private static string DescribeTraveler(OffMapJourneyRecord record)
        {
            return record.TravelerKind == OffMapTravelerKind.Person
                ? $"P{record.PersonId}"
                : $"vehicle '{record.VehicleId}'";
        }

        public bool IsOffMapPerson(int personId)
        {
            foreach (var record in byId.Values)
            {
                if (record.IsActive && record.TravelerKind == OffMapTravelerKind.Person && record.PersonId == personId)
                    return true;
            }
            return false;
        }

        public bool IsOffMapVehicle(string vehicleId)
        {
            if (string.IsNullOrEmpty(vehicleId)) return false;
            foreach (var record in byId.Values)
            {
                if (record.IsActive && record.TravelerKind == OffMapTravelerKind.Vehicle &&
                    string.Equals(record.VehicleId, vehicleId, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        public OffMapJourneyRecord FindActiveByPerson(int personId)
        {
            foreach (var record in byId.Values)
            {
                if (record.IsActive && record.TravelerKind == OffMapTravelerKind.Person && record.PersonId == personId)
                    return record;
            }
            return null;
        }

        public OffMapJourneyRecord FindActiveByVehicle(string vehicleId)
        {
            foreach (var record in byId.Values)
            {
                if (record.IsActive && record.TravelerKind == OffMapTravelerKind.Vehicle &&
                    string.Equals(record.VehicleId, vehicleId, StringComparison.Ordinal))
                    return record;
            }
            return null;
        }

        /// <summary>
        /// Authoritative location label for a traveler: the off-map label while
        /// departed, the origin while never departed. After return the caller
        /// owns the traveler's map location again.
        /// </summary>
        public string TravelerLocationLabel(OffMapTravelerKind kind, int personId, string vehicleId)
        {
            OffMapJourneyRecord active = kind == OffMapTravelerKind.Person
                ? FindActiveByPerson(personId)
                : FindActiveByVehicle(vehicleId);
            return active != null ? active.OffMapLocationLabel : null;
        }

        /// <summary>
        /// Advances the authority to a day index. Journeys whose due return day
        /// has arrived complete EXACTLY ONCE; early completion is impossible by
        /// construction (no teleporting). Cargo is delivered through the caller's
        /// callback once per completed journey. Re-running a day is idempotent.
        /// </summary>
        public List<OffMapJourneyRecord> CompleteDueJourneys(
            int dayIndex,
            Action<string, int> deliverCargo,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            var completed = new List<OffMapJourneyRecord>();
            foreach (var record in byId.Values)
            {
                if (!record.IsActive || !record.IsRoundTrip) continue;
                if (record.DueReturnDayIndex > dayIndex) continue; // not due: no teleporting

                record.State = OffMapJourneyState.Returned;
                record.ActualReturnDayIndex = dayIndex;
                if (deliverCargo != null)
                {
                    foreach (var kv in record.CargoUnits)
                    {
                        deliverCargo(kv.Key, kv.Value);
                    }
                    foreach (var kv in record.PurchaseManifestUnits)
                    {
                        deliverCargo(kv.Key, kv.Value);
                    }
                }
                completed.Add(record);
                diag.Add($"OffMapJourneyAuthority: {DescribeTraveler(record)} returned from " +
                    $"'{record.DestinationLabel}' on day {dayIndex} (due day {record.DueReturnDayIndex}); " +
                    $"cargo and purchase manifest delivered once, same entity.");
            }
            return completed;
        }

        /// <summary>
        /// Calls a journey off. Cargo goes back to the origin exactly once via
        /// the caller's callback. The traveler is no longer off-map afterwards.
        /// </summary>
        public string CancelJourney(string journeyId, string reason, Action<string, int> returnCargo, List<string> diag)
        {
            diag = diag ?? diagnostics;
            OffMapJourneyRecord record;
            if (string.IsNullOrEmpty(journeyId) || !byId.TryGetValue(journeyId, out record))
            {
                return $"OffMapJourneyAuthority: unknown journey '{journeyId}'.";
            }
            if (!record.IsActive)
            {
                return $"OffMapJourneyAuthority: journey '{journeyId}' is not active (state {record.State}).";
            }
            record.State = OffMapJourneyState.Cancelled;
            record.CancelReason = reason ?? string.Empty;
            if (returnCargo != null)
            {
                foreach (var kv in record.CargoUnits)
                {
                    returnCargo(kv.Key, kv.Value);
                }
            }
            int droppedManifest = 0;
            foreach (var kv in record.PurchaseManifestUnits)
            {
                droppedManifest += kv.Value;
            }
            record.PurchaseManifestUnits.Clear();
            diag.Add($"OffMapJourneyAuthority: journey '{journeyId}' cancelled ({record.CancelReason}); " +
                $"taken cargo returned to origin once, {droppedManifest}u of off-map purchase manifest dropped " +
                "(the cash already left the system off-map).");
            return null;
        }

        /// <summary>Save support (CLN-1 pattern): clear-then-load.</summary>
        public OffMapJourneyAuthoritySaveDto CaptureSaveDto()
        {
            var snapshot = new List<OffMapJourneyRecord>(byId.Count);
            foreach (OffMapJourneyRecord record in byId.Values)
            {
                // Pass VIII (Worker 9): deep snapshot — the DTO must not alias
                // live records, or post-capture mutations leak into the save.
                snapshot.Add(record != null ? record.Copy() : null);
            }

            return new OffMapJourneyAuthoritySaveDto { journeys = snapshot };
        }

        /// <summary>Save support (CLN-1 pattern): clear-then-load.</summary>
        public void LoadFromSaveDto(OffMapJourneyAuthoritySaveDto dto)
        {
            byId.Clear();
            if (dto == null || dto.journeys == null) return;
            foreach (var record in dto.journeys)
            {
                if (record == null || string.IsNullOrEmpty(record.JourneyId)) continue;
                // Pass VIII (Worker 9): copy on load as well — the DTO is a
                // snapshot; the live authority must not alias it, or later
                // mutations (completion, cancellation) would dirty the save.
                OffMapJourneyRecord live = record.Copy();
                live.CargoUnits = live.CargoUnits ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                live.PurchaseManifestUnits = live.PurchaseManifestUnits ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                live.ObligationIds = live.ObligationIds ?? new List<string>();
                live.RouteLegLocationIds = live.RouteLegLocationIds ?? new List<string>();
                byId[live.JourneyId] = live;
            }
        }
    }

    /// <summary>Save DTO for the off-map journey authority (CLN-1 pattern).</summary>
    [Serializable]
    public sealed class OffMapJourneyAuthoritySaveDto
    {
        public List<OffMapJourneyRecord> journeys = new List<OffMapJourneyRecord>();
    }
}
