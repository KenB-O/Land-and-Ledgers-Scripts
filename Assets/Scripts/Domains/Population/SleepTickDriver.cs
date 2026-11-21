using System;
using System.Collections.Generic;
using System.Linq;
using LandLedgers.World;

namespace LandLedgers.Population
{
    /// <summary>
    /// W-G2 (Agent G, expanded campaign 2026-11-16): the sleep tick
    /// driver. B1 delivered the SleepingPlaceRegistry (bed to bedroll to
    /// floor resolution, one place one person one night, asset holds
    /// surviving save/load, planner hook stamping place id and rest
    /// quality) but no per-tick movement to bed existed — the planner
    /// reserved the place and SleepingPlaceArrival is the contract this
    /// driver calls. This driver closes that gap: at the sleep start
    /// (22:00) it validates the arrival path and moves the sleeper to
    /// their reserved place, holds them through the night, and at the
    /// wake (07:00) it releases the reservation so rest quality is
    /// recorded and the place can be re-reserved.
    ///
    /// The driver is Unity-free and owns no authorities: the home
    /// building authority, the arrival check and the movement are handed
    /// in per tick by the owner (SimulationDrivers), exactly as
    /// DomesticLaborDriver takes its movement callback. Null for any of
    /// them means unavailable: the driver logs loudly once per night and
    /// retries while the sleep window remains open. A reservation only
    /// counts as sleep after movement confirms arrival; an unreached
    /// reservation is cancelled at waking without rest or bed-use credit.
    ///
    /// Phase is transient and re-derived from the clock, the live
    /// reservation and the movement result, never persisted: after a
    /// Continue mid-night the driver re-validates arrival and re-requests
    /// movement, and the movement resolver reports Arrived when the
    /// sleeper is already at the place. A missed wake (the game was
    /// saved mid-night, or the driver was not running at 07:00) closes
    /// the stale night on the next tick so the bed can be re-reserved —
    /// the same missed-wake semantics B1's TryReserveSleepingPlace
    /// applies at the next reservation.
    ///
    /// No new scheduler state: the sleeper's PopulationScheduleState
    /// becomes AtHome on arrival at the place (they are home, asleep);
    /// the driver's own per-night phase (Travelling / Sleeping) lives
    /// only in this transient map and is pruned once the night is over.
    /// </summary>
    public sealed class SleepTickDriver
    {
        /// <summary>
        /// Move one sleeper toward their reserved place. Returns
        /// Travelling while the route is active, Arrived once the
        /// sleeper is at the place, Unsupported when no route exists.
        /// </summary>
        public delegate DomesticActivityMovementResult SleepMovement(
            int personId, string routeIdentity, SleepingPlace place, BuildingFootprintAuthority authority,
            out string failureReason);

        /// <summary>Releases only the route owned by one expired sleep reservation.</summary>
        public delegate void SleepRouteRelease(int personId, string routeIdentity, SleepingPlace place);

        /// <summary>
        /// Validate the arrival path to a reserved place. Returns null
        /// when the place is reachable, else a loud reason. Null as a
        /// delegate means the driver calls the B1 contract directly
        /// (SleepingPlaceArrival.TryValidateArrivalPath).
        /// </summary>
        public delegate string SleepArrivalCheck(
            SleepingPlace place, BuildingFootprintAuthority authority);

        private enum SleepNightPhase
        {
            Travelling,
            Sleeping,
        }

        private sealed class SleepNightState
        {
            public SleepNightPhase Phase = SleepNightPhase.Travelling;
            public string LastFailure;
        }

        private readonly Dictionary<string, SleepNightState> nights =
            new Dictionary<string, SleepNightState>(StringComparer.Ordinal);
        private readonly HashSet<string> loggedKeys =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> diagnostics = new List<string>();

        /// <summary>Loud lines recorded by this driver (failures and stale-night recoveries), for diagnostics.</summary>
        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>
        /// Advance one tick. dayIndex and minuteOfDay are the current
        /// clock; registries are the hub's per-household sleeping-place
        /// registries. A reservation for tonight starts at its
        /// SleepStartMinute (22:00 by default); a reservation for last
        /// night releases at its SleepEndMinute (07:00 by default) on
        /// the following day, or on the first tick after that when the
        /// wake was missed.
        /// </summary>
        public void AdvanceTick(
            int dayIndex,
            int minuteOfDay,
            IReadOnlyList<SleepingPlaceRegistry> registries,
            Func<int, BuildingFootprintAuthority> authorityForPerson,
            SleepArrivalCheck arrivalCheck,
            SleepMovement moveToPlace,
            List<string> diag,
            SleepRouteRelease releaseRoute = null)
        {
            diag = diag ?? diagnostics;
            if (registries == null) return;

            PruneStaleNights(dayIndex);
            foreach (SleepingPlaceRegistry registry in registries)
            {
                if (registry == null) continue;
                // LiveReservations is the live list, not a snapshot:
                // copy before releasing any reservation below.
                SleepingPlaceReservation[] live = registry.LiveReservations.ToArray();
                foreach (SleepingPlaceReservation reservation in live)
                {
                    if (reservation == null) continue;
                    if (reservation.DayIndex < dayIndex)
                    {
                        WakeIfDue(registry, reservation, dayIndex, minuteOfDay, diag, releaseRoute);
                    }
                    else if (reservation.DayIndex == dayIndex
                        && minuteOfDay >= reservation.SleepStartMinute)
                    {
                        StartOrContinueNight(registry, reservation,
                            authorityForPerson, arrivalCheck, moveToPlace, diag);
                    }
                    // DayIndex == dayIndex and before the sleep start:
                    // tonight has not started yet; the hold simply stands.
                }
            }
        }

        private void WakeIfDue(
            SleepingPlaceRegistry registry,
            SleepingPlaceReservation reservation,
            int dayIndex,
            int minuteOfDay,
            List<string> diag,
            SleepRouteRelease releaseRoute)
        {
            bool stale = dayIndex > reservation.DayIndex + 1;
            if (!stale && minuteOfDay < reservation.SleepEndMinute) return;

            string key = NightKey(registry.HouseholdId, reservation.PersonId, reservation.DayIndex);
            bool reachedPlace = nights.TryGetValue(key, out SleepNightState state)
                && state != null && state.Phase == SleepNightPhase.Sleeping;
            float restQuality01 = 0f;
            bool released = reachedPlace
                ? registry.ReleaseSleepingPlace(reservation.PersonId, reservation.DayIndex, out restQuality01)
                : registry.CancelSleepingPlace(reservation.PersonId, reservation.DayIndex);
            if (!reachedPlace && releaseRoute != null)
                releaseRoute(reservation.PersonId, RouteIdentity(registry.HouseholdId, reservation.PersonId,
                    reservation.DayIndex), ResolvePlace(registry, reservation));
            nights.Remove(key);
            if (!released) return;

            if (!reachedPlace)
            {
                string reason = state?.LastFailure;
                string detail = string.IsNullOrWhiteSpace(reason)
                    ? "no confirmed arrival at the reserved sleeping place"
                    : reason;
                LogOnce(diag, "unreached-wake:" + key,
                    $"[Sleep] Reserved sleep was not completed for P{reservation.PersonId} (household {registry.HouseholdId}, night day {reservation.DayIndex}, woken day {dayIndex}): " +
                    $"the place '{reservation.PlaceId}' could not be reached ({detail}); the reservation was cancelled without bed-use or rest-quality credit.");
                return;
            }

            // A routine wake is a regular event, recorded in the
            // registry's rest-quality history — no log line. A stale
            // night (the wake was missed, the bed was held past its
            // morning) is a recovery worth one loud line, so the
            // re-reservation is visible.
            if (stale)
            {
                LogOnce(diag, "stale-wake:" + NightKey(registry.HouseholdId, reservation.PersonId, reservation.DayIndex),
                    $"[Sleep] Stale night closed for P{reservation.PersonId} (household {registry.HouseholdId}, night day {reservation.DayIndex}, woken day {dayIndex}): " +
                    $"the 07:00 wake was missed, the place '{reservation.PlaceId}' is released with rest quality {restQuality01:0.00} and can be re-reserved.");
            }
        }

        private void StartOrContinueNight(
            SleepingPlaceRegistry registry,
            SleepingPlaceReservation reservation,
            Func<int, BuildingFootprintAuthority> authorityForPerson,
            SleepArrivalCheck arrivalCheck,
            SleepMovement moveToPlace,
            List<string> diag)
        {
            string key = NightKey(registry.HouseholdId, reservation.PersonId, reservation.DayIndex);
            if (!nights.TryGetValue(key, out SleepNightState state) || state == null)
            {
                state = new SleepNightState();
                nights[key] = state;
            }

            if (state.Phase == SleepNightPhase.Sleeping) return;

            SleepingPlace place = ResolvePlace(registry, reservation);
            BuildingFootprintAuthority authority =
                authorityForPerson != null ? authorityForPerson(reservation.PersonId) : null;

            if (authority == null)
            {
                state.LastFailure = "building authority unavailable";
                LogOnce(diag, "no-authority:" + key,
                    $"[Sleep] No building authority for P{reservation.PersonId} (household {registry.HouseholdId}, night day {reservation.DayIndex}): " +
                    $"the arrival path to '{place.PlaceId}' cannot be validated; the reservation will not count unless arrival is later confirmed.");
                return;
            }

            string arrivalFailure = arrivalCheck != null
                ? arrivalCheck(place, authority)
                : SleepingPlaceArrival.TryValidateArrivalPath(place, authority);
            if (arrivalFailure != null)
            {
                state.LastFailure = arrivalFailure;
                LogOnce(diag, "arrival-failed:" + key,
                    $"[Sleep] Arrival to '{place.PlaceId}' failed for P{reservation.PersonId} (household {registry.HouseholdId}, night day {reservation.DayIndex}): {arrivalFailure}; route may be retried during this sleep window.");
                return;
            }

            if (moveToPlace == null)
            {
                state.LastFailure = "movement resolver unavailable";
                LogOnce(diag, "no-movement:" + key,
                    $"[Sleep] No movement resolver for P{reservation.PersonId} (household {registry.HouseholdId}, night day {reservation.DayIndex}): " +
                    $"the sleeper cannot be moved to '{place.PlaceId}'; the reservation will not count unless arrival is later confirmed.");
                return;
            }

            DomesticActivityMovementResult result = moveToPlace(
                reservation.PersonId,
                RouteIdentity(registry.HouseholdId, reservation.PersonId, reservation.DayIndex),
                place, authority, out string movementFailure);
            switch (result)
            {
                case DomesticActivityMovementResult.Arrived:
                    state.Phase = SleepNightPhase.Sleeping;
                    break;
                case DomesticActivityMovementResult.Unsupported:
                    state.LastFailure = string.IsNullOrWhiteSpace(movementFailure)
                        ? "movement route unsupported"
                        : movementFailure;
                    LogOnce(diag, "movement-unsupported:" + key,
                        $"[Sleep] Movement to '{place.PlaceId}' is unsupported for P{reservation.PersonId} (household {registry.HouseholdId}, night day {reservation.DayIndex}): " +
                        $"{state.LastFailure}; route may be retried during this sleep window.");
                    break;
                default:
                    // Travelling: keep requesting each tick until the
                    // resolver reports Arrived (or Unsupported).
                    state.LastFailure = null;
                    break;
            }
        }

        /// <summary>
        /// The reserved place, resolved from the registry's current
        /// places. When the furnishing register changed since planning
        /// (the bed was sold or unbound between planning and 22:00)
        /// the place no longer resolves: synthesize a floor-equivalent
        /// place from the reservation itself so the sleeper is still
        /// moved home instead of stalling the night.
        /// </summary>
        private static SleepingPlace ResolvePlace(
            SleepingPlaceRegistry registry, SleepingPlaceReservation reservation)
        {
            List<SleepingPlace> places = registry.PlacesFor(reservation.PersonId);
            foreach (SleepingPlace place in places)
            {
                if (place != null && string.Equals(place.PlaceId, reservation.PlaceId, StringComparison.Ordinal))
                    return place;
            }

            return new SleepingPlace
            {
                PlaceId = reservation.PlaceId,
                Kind = reservation.Kind,
                AssetId = reservation.AssetId,
                BuildingInstanceId = registry.HomeBuildingInstanceId,
                ActivityLocationId = string.Empty,
                BaseRestQuality01 = reservation.RestQuality01,
                CapacityPersons = 1,
            };
        }

        private void PruneStaleNights(int dayIndex)
        {
            if (nights.Count == 0) return;
            var stale = new List<string>();
            foreach (KeyValuePair<string, SleepNightState> entry in nights)
            {
                // Night keys end in ":D<dayIndex>".
                int marker = entry.Key.LastIndexOf(":D", StringComparison.Ordinal);
                if (marker < 0) continue;
                if (int.TryParse(entry.Key.Substring(marker + 2), out int nightDay)
                    && nightDay < dayIndex - 2)
                    stale.Add(entry.Key);
            }
            foreach (string key in stale) nights.Remove(key);
        }

        private static string NightKey(int householdId, int personId, int dayIndex)
        {
            return "H" + householdId + ":P" + personId + ":D" + dayIndex;
        }

        private static string RouteIdentity(int householdId, int personId, int dayIndex) =>
            "sleep:" + NightKey(householdId, personId, dayIndex);

        private void LogOnce(List<string> diag, string logKey, string line)
        {
            if (!loggedKeys.Add(logKey)) return;
            if (diag != null) diag.Add(line);
            // The caller may pass null, in which case AdvanceTick hands
            // this list in as diag — do not record the line twice.
            if (!ReferenceEquals(diag, diagnostics)) diagnostics.Add(line);
        }
    }
}
