using System;
using System.Collections.Generic;
using LandLedgers.Economy.Blacksmith;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.Population
{
    /// <summary>
    /// B4 (Agent B, expanded campaign 2026-11-16): how a meal is eaten
    /// at home. Seated means a reserved chair at a table in the same
    /// building; Fallback means the meal still happens at home with no
    /// chair reserved (floor/standing). Chairs are comfort/capacity,
    /// never a gate on eating (plan §6.2): no table, no chair, or no
    /// free chair all resolve to Fallback, the meal is counted, and
    /// nothing fails. There is no benefit difference today — no morale
    /// system exists — so seating is a record and a B6 furnishing-need
    /// signal, not a gate.
    /// </summary>
    public enum DiningMode
    {
        Unspecified = 0,
        Seated = 1,
        Fallback = 2,
    }

    /// <summary>
    /// B4: one dining seat — an installed chair at a table. Derived
    /// from the household furnishing register (B0): a usable asset of
    /// kind furniture.chair, installed with the DiningSeat capability,
    /// in a building that also holds at least one installed usable
    /// table (kind furniture.table, DiningTable capability). A chair
    /// in a building with no table is placed furniture, not a dining
    /// seat (plan §6.2: no table means the fallback). The seat id is
    /// stable: "chair:&lt;assetId&gt;". The activity location id is the
    /// binding's capability reference — one authored location per
    /// chair ("dining-chair-1", ...), confirmed as the Codex pattern
    /// in the B4 handoff (plan §9.2 instruction 5); empty until Codex
    /// authors it, and a seat with no authored location cannot be
    /// reached (see DiningArrival).
    /// </summary>
    [Serializable]
    public sealed class DiningSeat
    {
        public string SeatId = string.Empty;
        public string AssetId = string.Empty;
        public string TableAssetId = string.Empty;
        public int BuildingInstanceId;
        /// <summary>Authored per-chair activity location id ("dining-chair-N"). Empty until authored.</summary>
        public string ActivityLocationId = string.Empty;
    }

    /// <summary>
    /// B4: one dining table — an installed usable asset of kind
    /// furniture.table bound with the DiningTable capability. A table
    /// installed without the capability is placed but does not make
    /// the building's chairs dining seats, the same rule B1 applies
    /// to beds. The table id is stable: "table:&lt;assetId&gt;".
    /// </summary>
    [Serializable]
    public sealed class DiningTable
    {
        public string TableId = string.Empty;
        public string AssetId = string.Empty;
        public int BuildingInstanceId;
        /// <summary>Authored dining approach point ("dining"). Empty until authored.</summary>
        public string ActivityLocationId = string.Empty;
    }

    /// <summary>
    /// B4: one meal's dining reservation — one seat, one diner, one
    /// meal window. Unlike sleep (B1), this record is NOT backed by
    /// an EquipmentAsset hold: EquipmentAsset.Reserve holds exactly
    /// one hold per asset (it refuses when already reserved), and a
    /// household plans two meal windows per day at plan time, so one
    /// chair would need two concurrent holds, which the asset cannot
    /// express. Dining reservations are therefore held by the registry
    /// itself, keyed by seat + day + window with overlap checks. The
    /// registry is the only reserver (chairs are household-scoped,
    /// one registry per household), so no cross-driver double-booking
    /// path exists. A mid-day Continue clears the day's meal
    /// reservations — they are intraday working state, re-derived by
    /// the next plan — which can only make a chair appear free, never
    /// double-booked within a session. A Fallback reservation is a
    /// real resolution too (the diner eats at home, no seat), recorded
    /// so the planner stamps the mode and UncoveredDiners reads it.
    /// </summary>
    [Serializable]
    public sealed class DiningSeatReservation
    {
        public string ReservationId = string.Empty;
        public string SeatId = string.Empty;
        public string AssetId = string.Empty;
        public int PersonId = -1;
        public int HouseholdId = -1;
        public int DayIndex;
        public int MealStartMinute;
        public int MealDurationMinutes;
        public DiningMode Mode = DiningMode.Unspecified;
        public bool Released;
    }

    /// <summary>
    /// B4: the per-household dining registry (plan §6). Seats are
    /// derived from the household furnishing register (B0) — never
    /// invented, never a second ownership record. Resolution prefers
    /// a free chair at a table in the home building; no table, no
    /// chair, or no free chair all resolve to the Fallback, so no
    /// furniture state ever prevents eating.
    ///
    /// Assignment is deterministic first-come-first-served in
    /// person-id order per meal window (the planner plans household
    /// members in sorted order). There is NO rotation state like B1's
    /// sleep debt: seating carries no benefit today (plan §6.2 — no
    /// morale system exists), so which member sits is arbitrary-but-
    /// deterministic; if a seating benefit ever lands, rotation
    /// becomes a follow-up. The same chair serves both daily meals —
    /// reservations are per window, so the morning and evening meals
    /// never contend.
    ///
    /// Missed releases are harmless by construction: reservations
    /// are windowed, so a reservation for a past window or day never
    /// blocks a later one (every check is a same-day window-overlap
    /// check). The meal driver releases each seat after the meal
    /// (Agent G's wiring); until it exists, past reservations simply
    /// sit in the live list as inert history — two small records per
    /// person per day. There is deliberately no missed-release
    /// cleanup: at plan time BOTH of the day's meals are still in the
    /// future, so closing an "earlier" window would release a seat
    /// for a meal that has not happened yet. The registry cannot tell
    /// plan time from meal time, so it never closes a reservation
    /// except on an explicit release.
    /// </summary>
    public sealed class DiningPlaceRegistry
    {
        public const string ChairAssetKind = "furniture.chair";
        public const string TableAssetKind = "furniture.table";
        /// <summary>Authored dining approach point id on the home authority.</summary>
        public const string DiningLocationId = "dining";
        /// <summary>Authored per-chair activity location id prefix ("dining-chair-1", ...).</summary>
        public const string DiningChairLocationPrefix = "dining-chair-";

        public int HouseholdId;
        /// <summary>The household's B0 furnishing register (chairs and tables are derived from it). Null means fallback only.</summary>
        public HouseholdFurnishingRegister Furnishings;
        /// <summary>The household's home building instance id.</summary>
        public int HomeBuildingInstanceId = -1;

        private readonly List<DiningSeatReservation> liveReservations = new List<DiningSeatReservation>();

        /// <summary>Live (unreleased) dining reservations for the household, seated and fallback alike.</summary>
        public IReadOnlyList<DiningSeatReservation> LiveReservations => liveReservations;

        /// <summary>
        /// Installed tables that make a building's chairs dining seats:
        /// registered asset of kind furniture.table, installed, usable,
        /// and bound with the DiningTable capability (B4's install path
        /// sets it). A table installed without the capability is placed
        /// but does not seat anyone — see Validate.
        /// </summary>
        public List<DiningTable> DiningTables()
        {
            var tables = new List<DiningTable>();
            if (Furnishings == null || Furnishings.Bindings == null) return tables;
            foreach (FurnishingPlacementBinding binding in Furnishings.Bindings)
            {
                if (binding == null || binding.CapabilityKind != FurnishingCapabilityKind.DiningTable) continue;
                EquipmentAsset asset = Furnishings.FindAsset(binding.AssetId);
                if (asset == null || asset.Kind != TableAssetKind || !asset.IsUsable) continue;
                tables.Add(new DiningTable
                {
                    TableId = "table:" + asset.AssetId,
                    AssetId = asset.AssetId,
                    BuildingInstanceId = binding.BuildingInstanceId,
                    ActivityLocationId = binding.CapabilityReferenceId ?? string.Empty,
                });
            }
            tables.Sort((a, b) => string.CompareOrdinal(a.AssetId, b.AssetId));
            return tables;
        }

        /// <summary>
        /// Dining seats: installed usable chairs bound with the
        /// DiningSeat capability, in a building that also holds at
        /// least one installed usable table. A chair in a building
        /// with no table is not a seat. The table recorded on the seat
        /// is the first table in that building by asset id
        /// (deterministic).
        /// </summary>
        public List<DiningSeat> DiningSeats()
        {
            var seats = new List<DiningSeat>();
            if (Furnishings == null || Furnishings.Bindings == null) return seats;
            List<DiningTable> tables = DiningTables();
            foreach (FurnishingPlacementBinding binding in Furnishings.Bindings)
            {
                if (binding == null || binding.CapabilityKind != FurnishingCapabilityKind.DiningSeat) continue;
                EquipmentAsset asset = Furnishings.FindAsset(binding.AssetId);
                if (asset == null || asset.Kind != ChairAssetKind || !asset.IsUsable) continue;
                DiningTable table = null;
                foreach (DiningTable candidate in tables)
                {
                    if (candidate.BuildingInstanceId == binding.BuildingInstanceId)
                    {
                        table = candidate;
                        break;
                    }
                }
                if (table == null) continue;
                seats.Add(new DiningSeat
                {
                    SeatId = "chair:" + asset.AssetId,
                    AssetId = asset.AssetId,
                    TableAssetId = table.AssetId,
                    BuildingInstanceId = binding.BuildingInstanceId,
                    ActivityLocationId = binding.CapabilityReferenceId ?? string.Empty,
                });
            }
            seats.Sort((a, b) => string.CompareOrdinal(a.AssetId, b.AssetId));
            return seats;
        }

        /// <summary>Installed usable tables with the DiningTable capability.</summary>
        public int InstalledTableCount()
        {
            return DiningTables().Count;
        }

        /// <summary>Chairs that are dining seats (table present in the same building).</summary>
        public int InstalledChairSeatCount()
        {
            return DiningSeats().Count;
        }

        /// <summary>The person's live (unreleased) reservation for a meal window, or null.</summary>
        public DiningSeatReservation LiveReservationFor(int personId, int dayIndex, int mealStartMinute)
        {
            foreach (DiningSeatReservation reservation in liveReservations)
            {
                if (reservation == null || reservation.Released) continue;
                if (reservation.PersonId == personId && reservation.DayIndex == dayIndex
                    && reservation.MealStartMinute == mealStartMinute)
                    return reservation;
            }
            return null;
        }

        /// <summary>
        /// Resolves and reserves a dining seat for one person for one
        /// meal window: the first free chair at a table in the home
        /// building, else the Fallback (no table, no chair, or no free
        /// chair — the meal still happens, at home, with no seat). The
        /// Fallback always resolves, so no furniture state ever
        /// prevents eating. A person who already holds a reservation
        /// for the window keeps it (re-planning never double-reserves).
        /// Returns null on success, else a loud refusal for invalid
        /// inputs only.
        /// </summary>
        public string TryReserveDiningSeat(
            int personId, int dayIndex, int mealStartMinute, int mealDurationMinutes,
            out DiningSeatReservation reservation)
        {
            reservation = null;
            if (personId < 0)
                return "DiningPlaceRegistry.TryReserveDiningSeat: person id is required.";
            if (dayIndex < 0)
                return "DiningPlaceRegistry.TryReserveDiningSeat: day index is required.";
            if (mealStartMinute < 0 || mealStartMinute >= 1440 || mealDurationMinutes <= 0)
                return "DiningPlaceRegistry.TryReserveDiningSeat: a valid meal window is required.";
            DiningSeatReservation live = LiveReservationFor(personId, dayIndex, mealStartMinute);
            if (live != null)
            {
                reservation = live;
                return null;
            }

            DiningSeat seat = ResolveFor(personId, dayIndex, mealStartMinute, mealDurationMinutes);
            reservation = new DiningSeatReservation
            {
                ReservationId = Guid.NewGuid().ToString("N"),
                SeatId = seat != null ? seat.SeatId : string.Empty,
                AssetId = seat != null ? seat.AssetId : string.Empty,
                PersonId = personId,
                HouseholdId = HouseholdId,
                DayIndex = dayIndex,
                MealStartMinute = mealStartMinute,
                MealDurationMinutes = mealDurationMinutes,
                Mode = seat != null ? DiningMode.Seated : DiningMode.Fallback,
            };
            liveReservations.Add(reservation);
            return null;
        }

        /// <summary>
        /// Assigns dining seats for a whole household for one meal
        /// window, in person-id order (deterministic first-come-first-
        /// served — no rotation state, see the class doc). Members who
        /// already hold a reservation for the window keep it. Returns
        /// the first refusal, or null. The Fallback makes refusal
        /// near-impossible except for invalid inputs.
        /// </summary>
        public string AssignHouseholdDining(
            IReadOnlyList<int> memberIds, int dayIndex, int mealStartMinute, int mealDurationMinutes)
        {
            if (memberIds == null || memberIds.Count == 0)
                return "DiningPlaceRegistry.AssignHouseholdDining: no household members to assign.";
            var ordered = new List<int>(memberIds);
            ordered.Sort();
            foreach (int personId in ordered)
            {
                if (LiveReservationFor(personId, dayIndex, mealStartMinute) != null) continue;
                string error = TryReserveDiningSeat(personId, dayIndex, mealStartMinute, mealDurationMinutes, out _);
                if (error != null) return error;
            }
            return null;
        }

        /// <summary>
        /// Releases the person's seat for a meal window (the meal
        /// driver calls this after the meal). A Fallback reservation
        /// releases the same way. Returns true when a live
        /// reservation was released; false when the person held none
        /// for that window.
        /// </summary>
        public bool ReleaseDiningSeat(int personId, int dayIndex, int mealStartMinute)
        {
            DiningSeatReservation live = LiveReservationFor(personId, dayIndex, mealStartMinute);
            if (live == null) return false;
            live.Released = true;
            liveReservations.Remove(live);
            return true;
        }

        /// <summary>
        /// Releases every live dining reservation held by a person (any
        /// day or window) — departure/death cleanup so a stale seat
        /// never blocks a chair forever. Returns the number released.
        /// </summary>
        public int ReleaseAllForPerson(int personId)
        {
            int released = 0;
            var held = new List<DiningSeatReservation>();
            foreach (DiningSeatReservation reservation in liveReservations)
            {
                if (reservation != null && !reservation.Released && reservation.PersonId == personId)
                    held.Add(reservation);
            }
            foreach (DiningSeatReservation reservation in held)
            {
                reservation.Released = true;
                liveReservations.Remove(reservation);
                released++;
            }
            return released;
        }

        /// <summary>
        /// Members whose meal for the window resolves to the Fallback —
        /// the uncovered diners whose furnishing need (B6) is a chair
        /// (or a table, when the household has chairs but no table).
        /// Simulates the meal's assignment in person-id order (the same
        /// order AssignHouseholdDining uses), tracking tentatively-
        /// taken seats, so the answer reflects the household's real
        /// scarcity instead of resolving every member against the same
        /// free chair. Members who already hold a live reservation are
        /// judged by it. Peek only: resolves without reserving.
        /// </summary>
        public List<int> UncoveredDiners(
            IReadOnlyList<int> memberIds, int dayIndex, int mealStartMinute, int mealDurationMinutes)
        {
            var uncovered = new List<int>();
            if (memberIds == null) return uncovered;
            var ordered = new List<int>(memberIds);
            ordered.Sort();
            var tentativelyTaken = new HashSet<string>(StringComparer.Ordinal);
            foreach (int personId in ordered)
            {
                DiningSeatReservation live = LiveReservationFor(personId, dayIndex, mealStartMinute);
                if (live != null)
                {
                    if (live.Mode == DiningMode.Fallback) uncovered.Add(personId);
                    continue;
                }
                DiningSeat seat = ResolveFor(personId, dayIndex, mealStartMinute, mealDurationMinutes, tentativelyTaken);
                if (seat == null) uncovered.Add(personId);
                else tentativelyTaken.Add(seat.SeatId);
            }
            return uncovered;
        }

        /// <summary>
        /// Structural diagnostics: no furnishing register; chairs
        /// installed with the DiningSeat capability in a building with
        /// no table (placed but not seats); tables installed without
        /// the DiningTable capability; live reservations referencing
        /// seats that no longer derive (chair sold, removed, unusable,
        /// or its table gone); duplicate live reservations for one
        /// person+window; two persons holding one seat for overlapping
        /// windows. Empty means the registry is internally consistent.
        /// </summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            if (Furnishings == null)
            {
                problems.Add($"DiningPlaceRegistry {HouseholdId}: no furnishing register — no chairs or tables can be derived; fallback only.");
                return problems;
            }
            if (Furnishings.Bindings != null)
            {
                List<DiningTable> tables = DiningTables();
                foreach (FurnishingPlacementBinding binding in Furnishings.Bindings)
                {
                    if (binding == null) continue;
                    EquipmentAsset asset = Furnishings.FindAsset(binding.AssetId);
                    if (asset == null) continue;
                    if (asset.Kind == TableAssetKind && asset.IsUsable
                        && binding.CapabilityKind != FurnishingCapabilityKind.DiningTable)
                        problems.Add($"DiningPlaceRegistry {HouseholdId}: installed table {asset.AssetId} has no DiningTable capability — it is placed but seats nobody.");
                    if (asset.Kind == ChairAssetKind && asset.IsUsable
                        && binding.CapabilityKind == FurnishingCapabilityKind.DiningSeat)
                    {
                        bool hasTable = false;
                        foreach (DiningTable table in tables)
                        {
                            if (table.BuildingInstanceId == binding.BuildingInstanceId) { hasTable = true; break; }
                        }
                        if (!hasTable)
                            problems.Add($"DiningPlaceRegistry {HouseholdId}: installed chair {asset.AssetId} has the DiningSeat capability but its building holds no table — it is placed but is not a dining seat yet.");
                    }
                }
            }
            var seenPersonWindow = new HashSet<string>(StringComparer.Ordinal);
            var seenSeatWindow = new HashSet<string>(StringComparer.Ordinal);
            var seated = new List<DiningSeatReservation>();
            foreach (DiningSeatReservation reservation in liveReservations)
            {
                if (reservation == null) { problems.Add($"DiningPlaceRegistry {HouseholdId}: null live reservation entry."); continue; }
                if (reservation.Released)
                {
                    problems.Add($"DiningPlaceRegistry {HouseholdId}: released reservation {reservation.ReservationId} is still in the live list.");
                    continue;
                }
                string personWindow = $"{reservation.PersonId}:{reservation.DayIndex}:{reservation.MealStartMinute}";
                if (!seenPersonWindow.Add(personWindow))
                    problems.Add($"DiningPlaceRegistry {HouseholdId}: P{reservation.PersonId} holds more than one dining reservation for day {reservation.DayIndex} meal {reservation.MealStartMinute}.");
                if (reservation.Mode == DiningMode.Seated)
                {
                    seated.Add(reservation);
                    string seatWindow = $"{reservation.SeatId}:{reservation.DayIndex}:{reservation.MealStartMinute}";
                    if (!seenSeatWindow.Add(seatWindow))
                        problems.Add($"DiningPlaceRegistry {HouseholdId}: {reservation.SeatId} is held by more than one person for day {reservation.DayIndex} meal {reservation.MealStartMinute} — no double-booking.");
                    DiningSeat seat = FindSeat(reservation.SeatId);
                    if (seat == null)
                        problems.Add($"DiningPlaceRegistry {HouseholdId}: reservation {reservation.ReservationId} references seat {reservation.SeatId}, which no longer derives (chair sold, removed, unusable, or its table gone).");
                }
            }
            for (int i = 0; i < seated.Count; i++)
            {
                for (int j = i + 1; j < seated.Count; j++)
                {
                    DiningSeatReservation a = seated[i];
                    DiningSeatReservation b = seated[j];
                    if (a.SeatId != b.SeatId || a.DayIndex != b.DayIndex) continue;
                    if (WindowsOverlap(a.MealStartMinute, a.MealDurationMinutes, b.MealStartMinute, b.MealDurationMinutes))
                        problems.Add($"DiningPlaceRegistry {HouseholdId}: {a.SeatId} is held by P{a.PersonId} and P{b.PersonId} for overlapping windows on day {a.DayIndex} — no double-booking.");
                }
            }
            return problems;
        }

        // ---------- internals ----------

        private DiningSeat FindSeat(string seatId)
        {
            foreach (DiningSeat seat in DiningSeats())
            {
                if (seat.SeatId == seatId) return seat;
            }
            return null;
        }

        /// <summary>
        /// Pure resolution (no reservation): the first free chair at a
        /// table for one person for one meal window, or null (the
        /// Fallback). The tentative set (used by the uncovered
        /// simulation) marks seats already spoken for in the
        /// simulation, so one chair is not promised to two people.
        /// </summary>
        private DiningSeat ResolveFor(
            int personId, int dayIndex, int mealStartMinute, int mealDurationMinutes,
            HashSet<string> tentativelyTaken = null)
        {
            foreach (DiningSeat seat in DiningSeats())
            {
                if (IsSeatFreeInWindow(seat, personId, dayIndex, mealStartMinute, mealDurationMinutes, tentativelyTaken))
                    return seat;
            }
            return null;
        }

        private bool IsSeatFreeInWindow(
            DiningSeat seat, int personId, int dayIndex, int mealStartMinute, int mealDurationMinutes,
            HashSet<string> tentativelyTaken = null)
        {
            if (seat == null) return false;
            if (tentativelyTaken != null && tentativelyTaken.Contains(seat.SeatId)) return false;
            foreach (DiningSeatReservation reservation in liveReservations)
            {
                if (reservation == null || reservation.Released) continue;
                if (reservation.Mode != DiningMode.Seated || reservation.SeatId != seat.SeatId) continue;
                if (reservation.DayIndex != dayIndex) continue;
                if (reservation.PersonId == personId) continue;
                if (WindowsOverlap(reservation.MealStartMinute, reservation.MealDurationMinutes, mealStartMinute, mealDurationMinutes))
                    return false;
            }
            return true;
        }

        private static bool WindowsOverlap(int startA, int durationA, int startB, int durationB)
        {
            return startA < startB + durationB && startB < startA + durationA;
        }
    }

    /// <summary>
    /// B4: the physical-arrival contract for a dining seat (plan §6.1).
    /// Proves a seat is reachable through the existing interior route
    /// engine — the same engine PopulationPathingDirector uses for
    /// domestic activities — without duplicating the mover. A seat with
    /// an authored activity location must resolve and have an interior
    /// route from "main-entrance"; a seat with no authored location is
    /// refused loudly (Codex authors one dining-chair-N location per
    /// chair, plan §9.2 instruction 5). A fallback meal validates as
    /// standing room inside the home (the main-entrance indoor point
    /// must be walkable). This is the check the runtime dining driver
    /// calls before moving the diner; the per-tick movement itself is
    /// Agent G's wiring (no dining driver exists today — dining is a
    /// planner reservation only). Codex owns the visual pose anchor on
    /// arrival (a seated wagon/riding animation may serve as a
    /// temporary substitute, plan §9.2 instruction 7).
    /// </summary>
    public static class DiningArrival
    {
        public const string MainEntranceId = "main-entrance";

        /// <summary>Returns null when the seat is reachable, else a loud reason naming the failure.</summary>
        public static string TryValidateArrivalPath(DiningSeat seat, BuildingFootprintAuthority authority)
        {
            if (seat == null)
                return "DiningArrival: no dining seat to validate.";
            if (authority == null)
                return "DiningArrival: no building authority — the arrival path cannot be validated.";
            if (!string.IsNullOrWhiteSpace(seat.ActivityLocationId))
            {
                if (!authority.TryGetActivityLocation(seat.ActivityLocationId, seat.BuildingInstanceId,
                        out ResolvedBuildingActivityLocation activity, out string reason))
                    return $"DiningArrival: {seat.SeatId} activity location '{seat.ActivityLocationId}' did not resolve — {reason}";
                if (activity.RequiresWalkableInterior
                    && !authority.TryBuildInteriorActivityRoute(MainEntranceId, seat.ActivityLocationId, out _, out reason))
                    return $"DiningArrival: no interior route from {MainEntranceId} to {seat.SeatId} — {reason}";
                return null;
            }
            return $"DiningArrival: {seat.SeatId} has no authored dining-chair activity location — author one dining-chair-N location per chair on the home authority (plan §9.2 instruction 5) before the diner can reach it.";
        }

        /// <summary>
        /// Returns null when a fallback meal is reachable (standing room
        /// inside the home: the main-entrance indoor point must be
        /// walkable), else a loud reason naming the failure.
        /// </summary>
        public static string TryValidateFallbackArrivalPath(BuildingFootprintAuthority authority)
        {
            if (authority == null)
                return "DiningArrival: no building authority — the fallback arrival path cannot be validated.";
            if (!authority.TryGetEntrancePair(MainEntranceId, out BuildingEntrancePairAuthoring entrance, out string entranceReason))
                return $"DiningArrival: fallback meal has no {MainEntranceId} to enter through — {entranceReason}";
            if (!authority.TryEvaluateInteriorLocalPoint(entrance.indoorPositionLocal, entrance.floorIndex, entrance.functionalSpaceId, out string pointReason))
                return $"DiningArrival: fallback meal entrance indoor point is not walkable — {pointReason}";
            return null;
        }
    }
}
