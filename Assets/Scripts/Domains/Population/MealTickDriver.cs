using System;
using System.Collections.Generic;
using System.Linq;
using LandLedgers.World;

namespace LandLedgers.Population
{
    /// <summary>
    /// G-4 (Agent G, expanded campaign 2026-11-16): the meal tick
    /// driver. B4 delivered the DiningPlaceRegistry (chair-at-a-table
    /// resolution, one seat one diner one meal window, the Fallback
    /// that never blocks a meal, planner hook stamping seat id and
    /// mode) but no per-tick movement to the seat existed — the planner
    /// reserved the seat and DiningArrival is the contract this driver
    /// calls. This driver closes that gap: at the meal window it
    /// validates the arrival path and moves the diner to their reserved
    /// seat, holds them through the meal, and releases the reservation
    /// at the window end so the seat can be re-reserved for the next
    /// meal. If a seated arrival cannot be made, it changes the live
    /// reservation to the existing no-chair fallback and releases the
    /// unused seat claim; it never records that diner as seated. A
    /// Fallback meal validates standing room and eats in place.
    ///
    /// Meal preparation is NOT this driver: DomesticLaborDriver
    /// already plans meal-prep tasks (MealsAtHomeCount trigger,
    /// HouseholdMealService) and DriveDomesticLaborDay runs daily.
    /// This driver is the eating half only.
    ///
    /// The driver is Unity-free and owns no authorities: the home
    /// building authority, the arrival checks and the movement are
    /// handed in per tick by the owner (SimulationDrivers), exactly as
    /// SleepTickDriver takes its movement callback. Null for any of
    /// them means unavailable: the driver logs loudly once per meal
    /// and the diner eats in place — a meal is time-reserved, never
    /// blocked by a missing authority, and the reservation still
    /// releases at the window end.
    ///
    /// Phase is transient and re-derived from the clock, the live
    /// reservation and the movement result, never persisted: after a
    /// Continue mid-meal the driver re-validates arrival and
    /// re-requests movement, and the movement resolver reports Arrived
    /// when the diner is already at the seat. A missed window end (the
    /// game was saved mid-meal, or the driver was not running) closes
    /// the stale meal on the next tick so the seat can be re-reserved —
    /// the same missed-release semantics the sleep driver applies at
    /// waking. Reservations are windowed, so a past window never
    /// blocks a later one even before the driver releases it.
    ///
    /// No new scheduler state: the diner's PopulationScheduleState is
    /// untouched by this driver (the Eating activity already holds
    /// their time); the driver's own per-meal phase (Travelling /
    /// Eating) lives only in this transient map and is pruned once the
    /// meal is over.
    /// </summary>
    public sealed class MealTickDriver
    {
        /// <summary>
        /// Move one diner toward their reserved seat. Returns
        /// Travelling while the route is active, Arrived once the
        /// diner is at the seat, Unsupported when no route exists.
        /// Never called for a Fallback meal (no seat to move to).
        /// </summary>
        public delegate DomesticActivityMovementResult MealMovement(
            int personId, DiningSeat seat, BuildingFootprintAuthority authority);

        /// <summary>
        /// Validate the arrival path to a reserved seat. Returns null
        /// when the seat is reachable, else a loud reason. Null as a
        /// delegate means the driver calls the B4 contract directly
        /// (DiningArrival.TryValidateArrivalPath).
        /// </summary>
        public delegate string MealArrivalCheck(
            DiningSeat seat, BuildingFootprintAuthority authority);

        /// <summary>
        /// Validate standing room for a Fallback meal. Returns null
        /// when the home admits a fallback meal, else a loud reason.
        /// Null as a delegate means the driver calls the B4 contract
        /// directly (DiningArrival.TryValidateFallbackArrivalPath).
        /// </summary>
        public delegate string MealFallbackCheck(
            BuildingFootprintAuthority authority);

        private enum MealPhase
        {
            Travelling,
            Eating,
        }

        private sealed class MealState
        {
            public MealPhase Phase = MealPhase.Travelling;
            public bool ArrivalChecked;
            public bool FallbackChecked;
        }

        private readonly Dictionary<string, MealState> meals =
            new Dictionary<string, MealState>(StringComparer.Ordinal);
        private readonly HashSet<string> loggedKeys =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> diagnostics = new List<string>();

        /// <summary>Loud lines recorded by this driver (failures and stale-meal recoveries), for diagnostics.</summary>
        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>
        /// Advance one tick. dayIndex and minuteOfDay are the current
        /// clock; registries are the hub's per-household dining-place
        /// registries. A reservation releases at its window end
        /// (MealStartMinute + MealDurationMinutes) on its day, or on
        /// the first tick after that when the window end was missed; a
        /// past-day reservation releases on the next tick. The window
        /// itself is read from the reservation, never assumed — the
        /// planner's 07:30 / 18:30 defaults are not hardcoded here.
        /// </summary>
        public void AdvanceTick(
            int dayIndex,
            int minuteOfDay,
            IReadOnlyList<DiningPlaceRegistry> registries,
            Func<int, BuildingFootprintAuthority> authorityForPerson,
            MealArrivalCheck arrivalCheck,
            MealFallbackCheck fallbackCheck,
            MealMovement moveToSeat,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (registries == null) return;

            PruneStaleMeals(dayIndex);
            foreach (DiningPlaceRegistry registry in registries)
            {
                if (registry == null) continue;
                // LiveReservations is the live list, not a snapshot:
                // copy before releasing any reservation below.
                DiningSeatReservation[] live = registry.LiveReservations.ToArray();
                foreach (DiningSeatReservation reservation in live)
                {
                    if (reservation == null) continue;
                    if (reservation.DayIndex < dayIndex)
                    {
                        ReleaseStale(registry, reservation, dayIndex, diag);
                    }
                    else if (reservation.DayIndex == dayIndex
                        && minuteOfDay >= reservation.MealStartMinute + reservation.MealDurationMinutes)
                    {
                        ReleaseAtWindowEnd(registry, reservation);
                    }
                    else if (reservation.DayIndex == dayIndex
                        && minuteOfDay >= reservation.MealStartMinute)
                    {
                        StartOrContinueMeal(registry, reservation,
                            authorityForPerson, arrivalCheck, fallbackCheck, moveToSeat, diag);
                    }
                    // DayIndex == dayIndex and before the meal window:
                    // the meal has not started yet; the hold simply stands.
                }
            }
        }

        private void ReleaseStale(
            DiningPlaceRegistry registry,
            DiningSeatReservation reservation,
            int dayIndex,
            List<string> diag)
        {
            bool released = registry.ReleaseDiningSeat(
                reservation.PersonId, reservation.DayIndex, reservation.MealStartMinute);
            meals.Remove(MealKey(registry.HouseholdId, reservation.PersonId, reservation.DayIndex, reservation.MealStartMinute));
            if (!released) return;

            LogOnce(diag, "stale-meal:" + MealKey(registry.HouseholdId, reservation.PersonId, reservation.DayIndex, reservation.MealStartMinute),
                $"[Meal] Stale meal closed for P{reservation.PersonId} (household {registry.HouseholdId}, meal day {reservation.DayIndex} at {reservation.MealStartMinute}): " +
                $"the window end was missed, the seat '{reservation.SeatId}' is released and can be re-reserved.");
        }

        private void ReleaseAtWindowEnd(
            DiningPlaceRegistry registry,
            DiningSeatReservation reservation)
        {
            // A routine release is a regular event — no log line.
            if (!registry.ReleaseDiningSeat(
                reservation.PersonId, reservation.DayIndex, reservation.MealStartMinute))
                return;
            meals.Remove(MealKey(registry.HouseholdId, reservation.PersonId, reservation.DayIndex, reservation.MealStartMinute));
        }

        private void StartOrContinueMeal(
            DiningPlaceRegistry registry,
            DiningSeatReservation reservation,
            Func<int, BuildingFootprintAuthority> authorityForPerson,
            MealArrivalCheck arrivalCheck,
            MealFallbackCheck fallbackCheck,
            MealMovement moveToSeat,
            List<string> diag)
        {
            string key = MealKey(registry.HouseholdId, reservation.PersonId, reservation.DayIndex, reservation.MealStartMinute);
            if (!meals.TryGetValue(key, out MealState state) || state == null)
            {
                state = new MealState();
                meals[key] = state;
            }

            if (state.Phase == MealPhase.Eating) return;

            BuildingFootprintAuthority authority =
                authorityForPerson != null ? authorityForPerson(reservation.PersonId) : null;

            // A seat that no longer derives (chair sold, removed,
            // unusable, or its table gone between planning and the
            // meal) falls back: no furniture state ever prevents
            // eating, so the diner eats at home and the seat frees at
            // the window end like any Fallback meal.
            DiningSeat seat = null;
            bool seated = reservation.Mode == DiningMode.Seated;
            if (seated)
            {
                seat = FindSeat(registry, reservation.SeatId);
                if (seat == null)
                {
                    LogOnce(diag, "seat-gone:" + key,
                        $"[Meal] Seat '{reservation.SeatId}' no longer derives for P{reservation.PersonId} (household {registry.HouseholdId}, meal day {reservation.DayIndex} at {reservation.MealStartMinute}): " +
                        $"the chair was sold, removed, made unusable, or its table is gone — the diner uses the Fallback at home and the unavailable seat is released now.");
                    DemoteSeatReservationToFallback(reservation);
                    seated = false;
                }
            }

            if (!seated)
            {
                EatFallback(reservation, registry.HouseholdId, key, authority, fallbackCheck, state, diag);
                return;
            }

            if (!state.ArrivalChecked)
            {
                state.ArrivalChecked = true;
                if (authority == null)
                {
                    LogOnce(diag, "no-authority:" + key,
                        $"[Meal] No building authority for P{reservation.PersonId} (household {registry.HouseholdId}, meal day {reservation.DayIndex} at {reservation.MealStartMinute}): " +
                        $"the arrival path to '{seat.SeatId}' cannot be validated and no movement can be requested — the diner eats in place without a seated claim.");
                    DemoteSeatReservationToFallback(reservation);
                    state.Phase = MealPhase.Eating;
                    return;
                }

                string failure = arrivalCheck != null
                    ? arrivalCheck(seat, authority)
                    : DiningArrival.TryValidateArrivalPath(seat, authority);
                if (failure != null)
                {
                    LogOnce(diag, "arrival-failed:" + key,
                        $"[Meal] Arrival to '{seat.SeatId}' failed for P{reservation.PersonId} (household {registry.HouseholdId}, meal day {reservation.DayIndex} at {reservation.MealStartMinute}): {failure} — the diner uses the standing fallback and the unused seat is released now.");
                    DemoteSeatReservationToFallback(reservation);
                    EatFallback(reservation, registry.HouseholdId, key, authority, fallbackCheck, state, diag);
                    return;
                }
            }

            if (moveToSeat == null)
            {
                LogOnce(diag, "no-movement:" + key,
                    $"[Meal] No movement resolver for P{reservation.PersonId} (household {registry.HouseholdId}, meal day {reservation.DayIndex} at {reservation.MealStartMinute}): " +
                    $"the diner cannot be moved to '{seat.SeatId}' — the diner eats in place and the unused seat is released now.");
                DemoteSeatReservationToFallback(reservation);
                EatFallback(reservation, registry.HouseholdId, key, authority, fallbackCheck, state, diag);
                return;
            }

            DomesticActivityMovementResult result = moveToSeat(reservation.PersonId, seat, authority);
            switch (result)
            {
                case DomesticActivityMovementResult.Arrived:
                    state.Phase = MealPhase.Eating;
                    break;
                case DomesticActivityMovementResult.Unsupported:
                    LogOnce(diag, "movement-unsupported:" + key,
                        $"[Meal] Movement to '{seat.SeatId}' is unsupported for P{reservation.PersonId} (household {registry.HouseholdId}, meal day {reservation.DayIndex} at {reservation.MealStartMinute}): " +
                        $"no route reaches the seat — the diner eats in place and the unused seat is released now.");
                    DemoteSeatReservationToFallback(reservation);
                    EatFallback(reservation, registry.HouseholdId, key, authority, fallbackCheck, state, diag);
                    break;
                default:
                    // Travelling: keep requesting each tick until the
                    // resolver reports Arrived (or Unsupported).
                    break;
            }
        }

        /// <summary>
        /// A failed physical arrival may use the existing no-chair meal
        /// fallback, but it must not keep a dining seat reserved for a diner
        /// who never reached it. Preserve the meal reservation while
        /// releasing the physical seat claim so another household member can
        /// use that chair in the same window.
        /// </summary>
        private static void DemoteSeatReservationToFallback(DiningSeatReservation reservation)
        {
            if (reservation == null || reservation.Mode != DiningMode.Seated) return;
            reservation.Mode = DiningMode.Fallback;
            reservation.SeatId = string.Empty;
            reservation.AssetId = string.Empty;
        }

        /// <summary>
        /// A Fallback meal (or a Seated meal whose seat is gone):
        /// validate standing room, then eat in place. There is no seat
        /// to move to — standing room is the home itself — so no
        /// movement is ever requested. A failed fallback check still
        /// eats in place: the meal is never blocked.
        /// </summary>
        private void EatFallback(
            DiningSeatReservation reservation,
            int householdId,
            string key,
            BuildingFootprintAuthority authority,
            MealFallbackCheck fallbackCheck,
            MealState state,
            List<string> diag)
        {
            if (state.FallbackChecked)
            {
                state.Phase = MealPhase.Eating;
                return;
            }
            state.FallbackChecked = true;

            if (authority == null)
            {
                LogOnce(diag, "fallback-no-authority:" + key,
                    $"[Meal] No building authority for P{reservation.PersonId} (household {householdId}, meal day {reservation.DayIndex} at {reservation.MealStartMinute}): " +
                    $"standing room cannot be validated — the diner eats the Fallback in place and the reservation still releases at the window end.");
                state.Phase = MealPhase.Eating;
                return;
            }

            string failure = fallbackCheck != null
                ? fallbackCheck(authority)
                : DiningArrival.TryValidateFallbackArrivalPath(authority);
            if (failure != null)
            {
                LogOnce(diag, "fallback-failed:" + key,
                    $"[Meal] Fallback arrival failed for P{reservation.PersonId} (household {householdId}, meal day {reservation.DayIndex} at {reservation.MealStartMinute}): {failure} — the diner eats in place and the reservation still releases at the window end.");
            }
            state.Phase = MealPhase.Eating;
        }

        private static DiningSeat FindSeat(DiningPlaceRegistry registry, string seatId)
        {
            if (string.IsNullOrEmpty(seatId)) return null;
            foreach (DiningSeat seat in registry.DiningSeats())
            {
                if (seat != null && string.Equals(seat.SeatId, seatId, StringComparison.Ordinal))
                    return seat;
            }
            return null;
        }

        private void PruneStaleMeals(int dayIndex)
        {
            if (meals.Count == 0) return;
            var stale = new List<string>();
            foreach (KeyValuePair<string, MealState> entry in meals)
            {
                // Meal keys end in ":D<dayIndex>:M<startMinute>".
                int marker = entry.Key.LastIndexOf(":D", StringComparison.Ordinal);
                if (marker < 0) continue;
                int end = entry.Key.IndexOf(":M", marker, StringComparison.Ordinal);
                if (end < 0) continue;
                if (int.TryParse(entry.Key.Substring(marker + 2, end - (marker + 2)), out int mealDay)
                    && mealDay < dayIndex - 1)
                    stale.Add(entry.Key);
            }
            foreach (string key in stale) meals.Remove(key);
        }

        private static string MealKey(int householdId, int personId, int dayIndex, int mealStartMinute)
        {
            return "H" + householdId + ":P" + personId + ":D" + dayIndex + ":M" + mealStartMinute;
        }

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
