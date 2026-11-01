using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Businesses.Hotel
{
    /// <summary>W3B: one traveler's stable booking — which real animals hold which real stalls, for which nights.</summary>
    [Serializable]
    public sealed class LiveryStableBooking
    {
        /// <summary>The traveler whose animals these are (matches their hotel guest person id).</summary>
        public int PersonId;
        /// <summary>Real animal EntityIds from the HF-2 registry.</summary>
        public List<EntityId> AnimalIds = new List<EntityId>();
        /// <summary>Stall numbers held (parallel to AnimalIds).</summary>
        public List<string> StallNumbers = new List<string>();
        public int StartDayIndex;
        public int NightsBooked;

        public LiveryStableBooking() { }

        public bool CoversDay(int dayIndex)
        {
            return dayIndex >= StartDayIndex && dayIndex < StartDayIndex + Math.Max(0, NightsBooked);
        }
    }

    /// <summary>
    /// W3B: the livery-side stable booking authority — stalls as real
    /// inventory, bookings as night-scoped facts. Survey result: no livery
    /// stable runtime exists in the codebase (the EQU draft-power service
    /// reserves animals for work; it does not stable them), so this
    /// booking register IS the honest seam: a future Livery business type
    /// (Canon Rev XVIII lock: livery core = stabling, feeding, horse/team/
    /// rig hire) takes over this authority; until then it lives with the
    /// hotel-demand package that creates the demand.
    ///
    /// Stalls are authored (AddStalls), never invented; a full stable
    /// refuses loudly. Feeding is a documented seam: bookings record
    /// stabled animal-nights (<see cref="StabledAnimalsOnDay"/>); the
    /// livery business will settle real hay/oats/feed against those nights
    /// (Canon 26: transport animals consume real feed). W3B never invents
    /// feed and never serves it.
    /// </summary>
    public sealed class LiveryStableBookings
    {
        private readonly List<string> diagnostics = new List<string>();
        private readonly List<LiveryStableBooking> bookings = new List<LiveryStableBooking>();
        private int stallCount;

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<LiveryStableBooking> Bookings => bookings;
        public int StallCount => stallCount;

        /// <summary>Authors physical stalls. Stalls are the capacity — never invented later.</summary>
        public string AddStalls(int count, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (count <= 0)
                return "LiveryStableBookings.AddStalls: a stable needs a positive stall count — stalls are real, never conjured.";
            stallCount += count;
            diag.Add($"LiveryStableBookings: {count} stall(s) authored ({stallCount} total).");
            return null;
        }

        public bool HasBooking(int personId)
        {
            if (personId <= 0) return false;
            for (int i = 0; i < bookings.Count; i++)
                if (bookings[i] != null && bookings[i].PersonId == personId)
                    return true;
            return false;
        }

        /// <summary>
        /// Books one stall per animal for the night window, atomically: the
        /// whole team is stabled together or the booking is refused whole —
        /// a team is never split silently across "invented" overflow. A
        /// stall is free when no overlapping booking holds it.
        /// Returns the refusal, or null on success.
        /// </summary>
        public string BookStalls(int personId, List<EntityId> animalIds, int startDayIndex, int nights, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (personId <= 0)
                return "LiveryStableBookings.BookStalls: a booking needs a real person id.";
            if (HasBooking(personId))
                return $"LiveryStableBookings.BookStalls: person {personId} already holds a stable booking — one traveler, one booking.";
            if (animalIds == null || animalIds.Count == 0)
                return "LiveryStableBookings.BookStalls: no animals named — empty stalls are not booked.";
            if (nights <= 0)
                return $"LiveryStableBookings.BookStalls: person {personId} asks for no nights.";
            if (stallCount <= 0)
                return "LiveryStableBookings.BookStalls: the stable has no authored stalls — capacity is real, never invented.";

            var cleanAnimals = new List<EntityId>();
            foreach (EntityId animal in animalIds)
            {
                if (animal.Kind != EntityKind.Animal || !animal.IsValid)
                    return $"LiveryStableBookings.BookStalls: '{animal}' is not a real registered animal — teams are never invented.";
                if (cleanAnimals.Contains(animal))
                    return $"LiveryStableBookings.BookStalls: animal '{animal}' listed twice — one stall per animal.";
                cleanAnimals.Add(animal);
            }

            int endDay = startDayIndex + nights;
            var assigned = new List<string>();
            for (int s = 1; s <= stallCount && assigned.Count < cleanAnimals.Count; s++)
            {
                string stall = $"livery-stall-{s}";
                if (StallFreeForWindow(stall, startDayIndex, endDay))
                    assigned.Add(stall);
            }

            if (assigned.Count < cleanAnimals.Count)
            {
                diag.Add($"LiveryStableBookings: person {personId}'s team of {cleanAnimals.Count} refused — " +
                    $"only {assigned.Count} stall(s) free for nights {startDayIndex}–{endDay - 1} of {stallCount}. " +
                    "The team must stable elsewhere; no overflow stalls invented.");
                return $"LiveryStableBookings.BookStalls: only {assigned.Count} of {cleanAnimals.Count} stall(s) free — stable full.";
            }

            bookings.Add(new LiveryStableBooking
            {
                PersonId = personId,
                AnimalIds = cleanAnimals,
                StallNumbers = assigned,
                StartDayIndex = startDayIndex,
                NightsBooked = nights,
            });
            diag.Add($"LiveryStableBookings: person {personId}'s team of {cleanAnimals.Count} stabled " +
                $"({string.Join(", ", assigned.ToArray())}) for nights {startDayIndex}–{endDay - 1}.");
            return null;
        }

        /// <summary>Releases the traveler's booking (checkout, or stay ended). Returns true when one existed.</summary>
        public bool ReleaseBookings(int personId, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (personId <= 0) return false;
            for (int i = 0; i < bookings.Count; i++)
            {
                if (bookings[i] != null && bookings[i].PersonId == personId)
                {
                    bookings.RemoveAt(i);
                    diag.Add($"LiveryStableBookings: person {personId}'s stable booking released.");
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Feed-demand signal: stabled animal-nights on the given day. The
        /// future livery business settles real hay/oats/feed against this
        /// number; W3B records it, never serves it.
        /// </summary>
        public int StabledAnimalsOnDay(int dayIndex)
        {
            int total = 0;
            for (int i = 0; i < bookings.Count; i++)
            {
                LiveryStableBooking booking = bookings[i];
                if (booking == null || !booking.CoversDay(dayIndex)) continue;
                if (booking.AnimalIds != null) total += booking.AnimalIds.Count;
            }
            return total;
        }

        private bool StallFreeForWindow(string stallNumber, int startDayIndex, int endDayIndex)
        {
            for (int i = 0; i < bookings.Count; i++)
            {
                LiveryStableBooking booking = bookings[i];
                if (booking == null || booking.StallNumbers == null) continue;
                if (!booking.StallNumbers.Contains(stallNumber)) continue;
                int bookingEnd = booking.StartDayIndex + Math.Max(0, booking.NightsBooked);
                if (startDayIndex < bookingEnd && booking.StartDayIndex < endDayIndex)
                    return false;
            }
            return true;
        }

        #region Save / Load
        [Serializable]
        public sealed class LiveryStableBookingsSaveDto
        {
            public int StallCount;
            public List<LiveryStableBooking> Bookings = new List<LiveryStableBooking>();
        }

        public LiveryStableBookingsSaveDto CaptureSaveDto()
        {
            var dto = new LiveryStableBookingsSaveDto { StallCount = Math.Max(0, stallCount) };
            foreach (LiveryStableBooking booking in bookings)
            {
                if (booking == null || booking.PersonId <= 0) continue;
                dto.Bookings.Add(new LiveryStableBooking
                {
                    PersonId = booking.PersonId,
                    AnimalIds = new List<EntityId>(booking.AnimalIds ?? new List<EntityId>()),
                    StallNumbers = new List<string>(booking.StallNumbers ?? new List<string>()),
                    StartDayIndex = booking.StartDayIndex,
                    NightsBooked = Math.Max(0, booking.NightsBooked),
                });
            }
            return dto;
        }

        public void LoadFromSaveDto(LiveryStableBookingsSaveDto dto)
        {
            bookings.Clear();
            stallCount = 0;
            if (dto == null) return;
            stallCount = Math.Max(0, dto.StallCount);
            if (dto.Bookings == null) return;
            foreach (LiveryStableBooking booking in dto.Bookings)
            {
                if (booking == null || booking.PersonId <= 0 || booking.NightsBooked <= 0) continue;
                var cleanAnimals = new List<EntityId>();
                if (booking.AnimalIds != null)
                    foreach (EntityId animal in booking.AnimalIds)
                        if (animal.Kind == EntityKind.Animal && animal.IsValid && !cleanAnimals.Contains(animal))
                            cleanAnimals.Add(animal);
                if (cleanAnimals.Count == 0) continue;
                var cleanStalls = new List<string>();
                if (booking.StallNumbers != null)
                    foreach (string stall in booking.StallNumbers)
                        if (!string.IsNullOrWhiteSpace(stall) && !cleanStalls.Contains(stall))
                            cleanStalls.Add(stall);
                bookings.Add(new LiveryStableBooking
                {
                    PersonId = booking.PersonId,
                    AnimalIds = cleanAnimals,
                    StallNumbers = cleanStalls,
                    StartDayIndex = booking.StartDayIndex,
                    NightsBooked = booking.NightsBooked,
                });
            }
        }
        #endregion
    }
}
