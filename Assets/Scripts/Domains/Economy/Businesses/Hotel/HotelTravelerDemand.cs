using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using LandLedgers.World.Journeys;

namespace LandLedgers.Economy.Businesses.Hotel
{
    /// <summary>
    /// W3B: one traveler's arrival in town — the JRN journey leg that ended
    /// here. Canon §2.10: a traveler is a persistent Person with origin,
    /// destination/purpose, means, transport, lodging/food needs and timing;
    /// hotel, livery and meal transactions attach to that Person.
    ///
    /// Arrivals are SUPPLIED by the caller (the scenario/event director that
    /// produced the traveler); W3B never spawns travelers (Canon §3.2: a
    /// hotel being open creates availability, never customers). A traveler
    /// with no stay left to buy is a passer-through, not an arrival.
    /// </summary>
    [Serializable]
    public sealed class TravelerArrival
    {
        /// <summary>The arriving traveler — a real person id (Canon §2.10).</summary>
        public int PersonId;
        /// <summary>The game day the traveler reaches town.</summary>
        public int ArrivalDayIndex;
        /// <summary>JRN leg origin: the journey location id the traveler came from.</summary>
        public string FromLocationId = string.Empty;
        /// <summary>JRN travel mode for the arriving leg (Foot/Horseback/Wagon).</summary>
        public TravelMode Mode = TravelMode.Unspecified;
        /// <summary>Why they came: "commercial", "visiting", "passing", ...</summary>
        public string Purpose = string.Empty;
        /// <summary>Room-nights the traveler asks for (paid up front — nightly terms).</summary>
        public int PlannedStayNights;
        /// <summary>The room class the traveler prefers; fallback is any open bed.</summary>
        public HotelRoomClass PreferredRoomClass = HotelRoomClass.SingleRoom;
        /// <summary>
        /// Horses/teams traveling with them — real animal EntityIds from the
        /// HF-2 registry (Canon §2.10: transport is a real fact about the
        /// traveler). Empty for foot travelers.
        /// </summary>
        public List<EntityId> AnimalIds = new List<EntityId>();

        public TravelerArrival() { }
    }

    /// <summary>W3B: what happened when one arrival met the hotel's real beds.</summary>
    public enum HotelTravelerDemandOutcome
    {
        /// <summary>The traveler checked in as a nightly guest.</summary>
        CheckedIn = 0,
        /// <summary>No open bed of any class — refused loudly, never invented a room.</summary>
        RefusedNoBed = 1,
        /// <summary>The arrival itself was invalid (anonymous person, no paid nights, ...).</summary>
        RefusedInvalid = 2,
    }

    /// <summary>W3B: the per-arrival record — who arrived, what they got, and the loud refusal when they got nothing.</summary>
    [Serializable]
    public sealed class HotelTravelerDemandResult
    {
        public int PersonId;
        public HotelTravelerDemandOutcome Outcome = HotelTravelerDemandOutcome.RefusedInvalid;
        public string Refusal = string.Empty;
        public string RoomNumber = string.Empty;
        public int BedIndex = -1;
        public int NightsBooked;
        /// <summary>The nightly rate locked at check-in — the ledger authority settles it.</summary>
        public int LockedNightlyRateCents;
        /// <summary>
        /// When the traveler's animals could not be stabled: the livery
        /// refusal. Empty when stabled or when the traveler had no animals.
        /// </summary>
        public string StableBookingRefusal = string.Empty;

        public HotelTravelerDemandResult() { }
    }

    /// <summary>
    /// W3B: hotel demand from traveler arrivals. Each arrival is routed to
    /// the hotel's real bed inventory as a nightly stay (nights paid up
    /// front — W3A nightly terms); travelers with animals also get their
    /// horses/teams booked into livery stalls through the runtime's stable
    /// link. When the hotel is full the refusal is LOUD — rooms are never
    /// invented, and a refused traveler is a recorded fact (Canon §3.1:
    /// needs can go unsatisfied), not a silently dropped customer.
    ///
    /// Money moves only through ledger authorities (W3A rule): this
    /// processor records the locked charge on the result; settling it is
    /// the caller's job.
    /// </summary>
    public static class HotelTravelerDemand
    {
        /// <summary>
        /// Routes one arrival to the hotel: bed first (preferred class, then
        /// any open class), then stable stalls for the traveler's animals.
        /// </summary>
        public static HotelTravelerDemandResult ProcessArrival(
            HotelShopRuntime runtime, TravelerArrival arrival, List<string> diag)
        {
            var result = new HotelTravelerDemandResult();
            var sink = new List<string>();
            List<string> d = diag ?? sink;

            if (runtime == null)
            {
                result.Refusal = "HotelTravelerDemand: no hotel runtime — an arrival cannot be placed without the hotel.";
                result.Outcome = HotelTravelerDemandOutcome.RefusedInvalid;
                d.Add(result.Refusal);
                return result;
            }

            string invalid = ValidateArrival(arrival);
            if (invalid != null)
            {
                result.Outcome = HotelTravelerDemandOutcome.RefusedInvalid;
                result.Refusal = invalid;
                d.Add(invalid);
                return result;
            }

            result.PersonId = arrival.PersonId;

            HotelRoomInventory inventory = runtime.RoomInventory;
            HotelRoom room = FindOpenRoom(runtime, arrival.PreferredRoomClass,
                out int bedIndex, out bool classFallback, arrival, d);
            if (room == null)
            {
                result.Outcome = HotelTravelerDemandOutcome.RefusedNoBed;
                result.Refusal = $"HotelTravelerDemand: traveler {arrival.PersonId} refused — no open bed " +
                    $"(preferred {arrival.PreferredRoomClass}, no other class open either). Capacity is real beds, never invented.";
                d.Add(result.Refusal);
                // D2A: a recorded refusal is a reputation fact (Canon §8.1G
                // "repeated overcrowding"), not a silently dropped customer.
                runtime.RecordReputationEvent(arrival.ArrivalDayIndex, HotelExperienceKind.OvercrowdingRefusal,
                    $"traveler {arrival.PersonId} turned away — the house was full", d);
                return result;
            }

            string refusal = runtime.CheckInGuest(arrival.PersonId, room.RoomNumber, bedIndex,
                room.RoomClass, HotelStayKind.Nightly, arrival.ArrivalDayIndex,
                arrival.PlannedStayNights, d);
            if (refusal != null)
            {
                result.Outcome = HotelTravelerDemandOutcome.RefusedNoBed;
                result.Refusal = $"HotelTravelerDemand: traveler {arrival.PersonId} refused at check-in — {refusal}";
                d.Add(result.Refusal);
                return result;
            }

            result.Outcome = HotelTravelerDemandOutcome.CheckedIn;
            result.RoomNumber = room.RoomNumber;
            result.BedIndex = bedIndex;
            result.NightsBooked = arrival.PlannedStayNights;
            result.LockedNightlyRateCents = runtime.RateSchedule.NightlyRateCents(room.RoomClass);

            if (arrival.AnimalIds != null && arrival.AnimalIds.Count > 0)
            {
                string stableRefusal = runtime.BookLiveryStallsForGuest(
                    arrival.PersonId, arrival.AnimalIds, arrival.PlannedStayNights, d);
                result.StableBookingRefusal = stableRefusal ?? string.Empty;
                if (stableRefusal != null)
                    d.Add($"HotelTravelerDemand: traveler {arrival.PersonId} checked in, but {stableRefusal} — " +
                        "the team must stable elsewhere; the traveler was NOT turned away over the stable.");
            }

            d.Add($"HotelTravelerDemand: traveler {arrival.PersonId} arrived from '{arrival.FromLocationId}' " +
                $"({arrival.Mode}, {arrival.Purpose}) — checked in {room.RoomClass} '{room.RoomNumber}' bed {bedIndex} " +
                $"for {arrival.PlannedStayNights} night(s){(classFallback ? " (class fallback — preferred class full)" : string.Empty)}.");
            return result;
        }

        /// <summary>Routes a batch of arrivals in list order (first come, first served on real beds).</summary>
        public static List<HotelTravelerDemandResult> ProcessArrivals(
            HotelShopRuntime runtime, List<TravelerArrival> arrivals, int dayIndex, List<string> diag)
        {
            var results = new List<HotelTravelerDemandResult>();
            diag = diag ?? new List<string>();
            if (arrivals == null)
            {
                diag.Add("HotelTravelerDemand: no arrivals offered — the hotel's beds stand empty.");
                return results;
            }
            foreach (TravelerArrival arrival in arrivals)
            {
                if (arrival == null) continue;
                if (arrival.ArrivalDayIndex != dayIndex)
                {
                    diag.Add($"HotelTravelerDemand: traveler {arrival.PersonId} arrives day {arrival.ArrivalDayIndex}, " +
                        $"not day {dayIndex} — arrivals are day-scoped facts, not processed early.");
                    continue;
                }
                results.Add(ProcessArrival(runtime, arrival, diag));
            }
            diag.Add($"HotelTravelerDemand: day {dayIndex} — {results.Count} arrival(s) processed.");
            return results;
        }

        private static string ValidateArrival(TravelerArrival arrival)
        {
            if (arrival == null)
                return "HotelTravelerDemand: null arrival — travelers are real persons, not nulls.";
            if (arrival.PersonId <= 0)
                return "HotelTravelerDemand: an arrival needs a real person id — anonymous travelers are not placed (Canon §2.10).";
            if (arrival.PlannedStayNights <= 0)
                return $"HotelTravelerDemand: traveler {arrival.PersonId} asks for no nights — a nightly stay needs at least one paid night.";
            if (arrival.ArrivalDayIndex < 0)
                return $"HotelTravelerDemand: traveler {arrival.PersonId} has no real arrival day.";
            if (arrival.AnimalIds != null)
                foreach (EntityId animal in arrival.AnimalIds)
                    if (animal.Kind != EntityKind.Animal || !animal.IsValid)
                        return $"HotelTravelerDemand: traveler {arrival.PersonId} names an animal '{animal}' that is not a real registered animal — teams are never invented.";
            return null;
        }

        /// <summary>
        /// Finds the first open bed the walk-in traveler may take. D2A:
        /// reservation-held beds are withheld — a walk-in never takes a
        /// bed the proprietor promised a contract/commercial customer
        /// (Canon §8.1D). Proprietor-household rooms are already excluded
        /// by the inventory itself (Canon §8.1A).
        /// </summary>
        private static HotelRoom FindOpenRoom(HotelShopRuntime runtime, HotelRoomClass preferred,
            out int bedIndex, out bool classFallback, TravelerArrival arrival, List<string> diag)
        {
            bedIndex = -1;
            classFallback = false;
            if (runtime == null) return null;
            HotelRoomInventory inventory = runtime.RoomInventory;
            if (inventory == null) return null;
            int dayIndex = arrival != null ? arrival.ArrivalDayIndex : 0;

            // Preferred class first — a commercial traveler who asked for
            // the parlor suite gets it when it is open.
            int skip = runtime.RoomReservations.WithheldFromWalkIns(preferred, dayIndex);
            foreach (HotelRoom room in inventory.Rooms)
            {
                if (room == null || room.RoomClass != preferred) continue;
                if (room.ProprietorOccupied) continue;
                if (room.TryFindOpenBed(out int open))
                {
                    // Withheld beds are skipped: the first N open beds of
                    // the class belong to the reservation book.
                    if (skip > 0)
                    {
                        int openBeds = room.OpenBedCount;
                        if (skip >= openBeds) { skip -= openBeds; continue; }
                        bedIndex = -1;
                        for (int b = 0; b < room.BedCount; b++)
                        {
                            if (room.IsBedOccupied(b)) continue;
                            if (skip > 0) { skip--; continue; }
                            bedIndex = b;
                            break;
                        }
                        if (bedIndex < 0) continue;
                        return room;
                    }
                    bedIndex = open;
                    return room;
                }
            }

            // Any open bed: a traveler sleeps where there is a bed rather
            // than on the street (hotel policy; logged so the class change
            // is never silent).
            foreach (HotelRoom room in inventory.Rooms)
            {
                if (room == null || room.RoomClass == preferred) continue;
                if (room.ProprietorOccupied) continue;
                int fallbackSkip = runtime.RoomReservations.WithheldFromWalkIns(room.RoomClass, dayIndex);
                if (room.TryFindOpenBed(out int open))
                {
                    if (fallbackSkip > 0)
                    {
                        int openBeds = room.OpenBedCount;
                        if (fallbackSkip >= openBeds) continue;
                        bedIndex = -1;
                        for (int b = 0; b < room.BedCount; b++)
                        {
                            if (room.IsBedOccupied(b)) continue;
                            if (fallbackSkip > 0) { fallbackSkip--; continue; }
                            bedIndex = b;
                            break;
                        }
                        if (bedIndex < 0) continue;
                    }
                    else
                    {
                        bedIndex = open;
                    }
                    classFallback = true;
                    diag.Add($"HotelTravelerDemand: traveler {arrival.PersonId} preferred {preferred} — full; " +
                        $"taking {room.RoomClass} '{room.RoomNumber}' instead (class fallback).");
                    return room;
                }
            }

            return null;
        }
    }
}
