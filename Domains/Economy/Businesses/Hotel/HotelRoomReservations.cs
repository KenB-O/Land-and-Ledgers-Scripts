using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Businesses.Hotel
{
    /// <summary>
    /// D2A: one contract or commercial customer's held bed capacity. Canon
    /// §8.1D: the owner can reserve "rooms for expected contract or
    /// commercial customers" — the parlor suite the era kept for
    /// commercial travelers (§8.1C) and for mines, rail projects or major
    /// merchants sending men to town (§8.1F). A reservation holds a COUNT
    /// of beds of one room class over a day window at locked reserved
    /// rates — real held capacity, never a percentage. Reserved-but-
    /// unconsumed beds are withheld from walk-in arrivals; travelers with
    /// no contract take what is left.
    /// </summary>
    [Serializable]
    public sealed class HotelRoomReservation
    {
        public string ReservationId = string.Empty;

        /// <summary>The contract customer holding the beds (a real business instance id).</summary>
        public string CustomerBusinessId = string.Empty;

        /// <summary>Named customer (mine, rail contractor, merchant house) for readouts.</summary>
        public string CustomerName = string.Empty;

        public HotelRoomClass RoomClass = HotelRoomClass.SingleRoom;

        /// <summary>How many beds of the room class this reservation holds.</summary>
        public int BedCount;

        /// <summary>First day the hold applies (inclusive).</summary>
        public int FromDayIndex;

        /// <summary>Last day the hold applies (inclusive).</summary>
        public int ToDayIndex;

        /// <summary>Locked nightly rate for check-ins under this reservation (Canon "rent agreement memory").</summary>
        public int ReservedNightlyRateCents;

        /// <summary>Locked weekly rate for check-ins under this reservation.</summary>
        public int ReservedWeeklyRateCents;

        /// <summary>Locked monthly rate for check-ins under this reservation.</summary>
        public int ReservedMonthlyRateCents;

        /// <summary>Beds of this hold already taken by guests checked in under it (consumed, not just held).</summary>
        public int ConsumedBeds;

        public bool IsActive = true;

        public HotelRoomReservation() { }

        public bool CoversDay(int dayIndex)
        {
            return IsActive && dayIndex >= FromDayIndex && dayIndex <= ToDayIndex;
        }

        public bool OverlapsWindow(int fromDayIndex, int toDayIndex)
        {
            return fromDayIndex <= ToDayIndex && toDayIndex >= FromDayIndex;
        }

        /// <summary>Beds still held but unconsumed — withheld from walk-ins.</summary>
        public int UnconsumedBeds => Math.Max(0, BedCount - Math.Max(0, ConsumedBeds));
    }

    /// <summary>
    /// D2A: the hotel's reservation book. Reservations are validated
    /// against REAL physical beds: a hold can never exceed the hotel's
    /// beds of that class minus already-held beds over the same window
    /// (proprietor-occupied rooms are not beds — Canon §8.1A). Released
    /// (or expired) reservations free their beds; the hold history stays
    /// on the books. Refusals are loud — capacity is never conjured.
    /// </summary>
    public sealed class HotelRoomReservations
    {
        private readonly List<string> diagnostics = new List<string>();
        private readonly List<HotelRoomReservation> reservations = new List<HotelRoomReservation>();
        private int reservationSequence = 1;

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<HotelRoomReservation> Reservations => reservations;

        /// <summary>
        /// Holds beds for a contract/commercial customer. Rates lock at
        /// reservation (the Canon §8.1D policy side: contract rates below
        /// the walk-in schedule). Returns the reservation id, or null plus
        /// a loud refusal.
        /// </summary>
        public string Reserve(string customerBusinessId, string customerName, HotelRoomClass roomClass,
            int bedCount, int fromDayIndex, int toDayIndex,
            int reservedNightlyRateCents, int reservedWeeklyRateCents, int reservedMonthlyRateCents,
            HotelRoomInventory inventory, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(customerBusinessId))
                return "HotelRoomReservations.Reserve: a reservation needs a real customer business id.";
            if (bedCount <= 0)
                return "HotelRoomReservations.Reserve: a reservation must hold at least one bed.";
            if (toDayIndex < fromDayIndex)
                return "HotelRoomReservations.Reserve: the hold window ends before it starts.";
            if (fromDayIndex < 0)
                return "HotelRoomReservations.Reserve: a hold needs a real start day.";
            if (reservedNightlyRateCents < 0 || reservedWeeklyRateCents < 0 || reservedMonthlyRateCents < 0)
                return "HotelRoomReservations.Reserve: reserved rates cannot be negative.";
            if (inventory == null)
                return "HotelRoomReservations.Reserve: no room inventory — beds cannot be held against a ledger.";

            int physical = 0;
            foreach (HotelRoom room in inventory.Rooms)
                if (room != null && room.RoomClass == roomClass && !room.ProprietorOccupied)
                    physical += Math.Max(0, room.BedCount);

            int alreadyHeld = 0;
            foreach (HotelRoomReservation existing in reservations)
            {
                if (existing == null || !existing.IsActive) continue;
                if (existing.RoomClass != roomClass) continue;
                if (!existing.OverlapsWindow(fromDayIndex, toDayIndex)) continue;
                alreadyHeld += Math.Max(0, existing.BedCount);
            }

            if (alreadyHeld + bedCount > physical)
                return $"HotelRoomReservations.Reserve: cannot hold {bedCount} {roomClass} bed(s) for '{customerBusinessId}' " +
                    $"over days {fromDayIndex}-{toDayIndex} — the hotel has {physical} sellable such bed(s), {alreadyHeld} already held.";

            var reservation = new HotelRoomReservation
            {
                ReservationId = $"htl-res-{reservationSequence++}",
                CustomerBusinessId = customerBusinessId,
                CustomerName = customerName ?? string.Empty,
                RoomClass = roomClass,
                BedCount = bedCount,
                FromDayIndex = fromDayIndex,
                ToDayIndex = toDayIndex,
                ReservedNightlyRateCents = reservedNightlyRateCents,
                ReservedWeeklyRateCents = reservedWeeklyRateCents,
                ReservedMonthlyRateCents = reservedMonthlyRateCents,
                ConsumedBeds = 0,
                IsActive = true,
            };
            reservations.Add(reservation);
            diag.Add($"HotelRoomReservations: held {bedCount} {roomClass} bed(s) for '{customerBusinessId}' " +
                $"days {fromDayIndex}-{toDayIndex} ({reservation.ReservationId}) at {reservedNightlyRateCents}¢/night, " +
                $"{reservedWeeklyRateCents}¢/wk, {reservedMonthlyRateCents}¢/mo.");
            return reservation.ReservationId;
        }

        /// <summary>
        /// A guest checks in UNDER a reservation: consumes one held bed and
        /// returns the locked rates. Refuses loudly when the reservation
        /// has no unconsumed beds or does not cover the check-in day.
        /// </summary>
        public string ConsumeBed(string reservationId, int dayIndex, out int lockedNightlyRateCents,
            out int lockedWeeklyRateCents, out int lockedMonthlyRateCents, List<string> diag)
        {
            diag = diag ?? diagnostics;
            lockedNightlyRateCents = 0;
            lockedWeeklyRateCents = 0;
            lockedMonthlyRateCents = 0;
            HotelRoomReservation reservation = Find(reservationId);
            if (reservation == null)
                return $"HotelRoomReservations.ConsumeBed: no reservation '{reservationId}'.";
            if (!reservation.IsActive)
                return $"HotelRoomReservations.ConsumeBed: reservation '{reservationId}' is released.";
            if (!reservation.CoversDay(dayIndex))
                return $"HotelRoomReservations.ConsumeBed: reservation '{reservationId}' does not cover day {dayIndex}.";
            if (reservation.UnconsumedBeds <= 0)
                return $"HotelRoomReservations.ConsumeBed: reservation '{reservationId}' is fully consumed — no held bed remains for this guest.";

            reservation.ConsumedBeds++;
            lockedNightlyRateCents = reservation.ReservedNightlyRateCents;
            lockedWeeklyRateCents = reservation.ReservedWeeklyRateCents;
            lockedMonthlyRateCents = reservation.ReservedMonthlyRateCents;
            diag.Add($"HotelRoomReservations: bed consumed under '{reservationId}' (day {dayIndex}) — " +
                $"{reservation.UnconsumedBeds} held bed(s) remain.");
            return null;
        }

        /// <summary>
        /// A reservation guest checks out (or their stay expires): the held
        /// bed returns to the hold for the next contract traveler.
        /// </summary>
        public void ReleaseConsumedBed(string reservationId, List<string> diag)
        {
            diag = diag ?? diagnostics;
            HotelRoomReservation reservation = Find(reservationId);
            if (reservation == null) return;
            reservation.ConsumedBeds = Math.Max(0, reservation.ConsumedBeds - 1);
        }

        /// <summary>
        /// Releases a hold early. History stays on the books; the beds free
        /// immediately. Guests already checked in under the reservation
        /// keep their locked rates (rent agreement memory) — the hold only
        /// stops covering NEW check-ins.
        /// </summary>
        public string ReleaseReservation(string reservationId, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            HotelRoomReservation reservation = Find(reservationId);
            if (reservation == null)
                return $"HotelRoomReservations.ReleaseReservation: no reservation '{reservationId}'.";
            if (!reservation.IsActive)
                return $"HotelRoomReservations.ReleaseReservation: reservation '{reservationId}' is already released.";
            reservation.IsActive = false;
            diag.Add($"HotelRoomReservations: reservation '{reservationId}' released (day {dayIndex}) — held beds freed; lodged guests keep their locked rates.");
            return null;
        }

        public HotelRoomReservation Find(string reservationId)
        {
            if (string.IsNullOrWhiteSpace(reservationId)) return null;
            for (int i = 0; i < reservations.Count; i++)
                if (reservations[i] != null && string.Equals(reservations[i].ReservationId, reservationId, StringComparison.OrdinalIgnoreCase))
                    return reservations[i];
            return null;
        }

        /// <summary>
        /// Beds of one class withheld from walk-in check-ins on the given
        /// day: held-but-unconsumed across all active reservations.
        /// </summary>
        public int WithheldFromWalkIns(HotelRoomClass roomClass, int dayIndex)
        {
            int withheld = 0;
            for (int i = 0; i < reservations.Count; i++)
            {
                HotelRoomReservation reservation = reservations[i];
                if (reservation == null) continue;
                if (reservation.RoomClass != roomClass) continue;
                if (!reservation.CoversDay(dayIndex)) continue;
                withheld += reservation.UnconsumedBeds;
            }
            return withheld;
        }

        #region Save / Load
        [Serializable]
        public sealed class HotelRoomReservationsSaveDto
        {
            public int ReservationSequence = 1;
            public List<HotelRoomReservation> Reservations = new List<HotelRoomReservation>();
        }

        public HotelRoomReservationsSaveDto CaptureSaveDto()
        {
            var dto = new HotelRoomReservationsSaveDto { ReservationSequence = Math.Max(1, reservationSequence) };
            foreach (HotelRoomReservation reservation in reservations)
            {
                if (reservation != null) dto.Reservations.Add(reservation);
            }
            return dto;
        }

        public void LoadFromSaveDto(HotelRoomReservationsSaveDto dto)
        {
            reservations.Clear();
            reservationSequence = 1;
            if (dto == null) return;
            reservationSequence = Math.Max(1, dto.ReservationSequence);
            if (dto.Reservations == null) return;
            foreach (HotelRoomReservation reservation in dto.Reservations)
            {
                if (reservation == null || string.IsNullOrWhiteSpace(reservation.ReservationId)) continue;
                reservations.Add(reservation);
            }
        }
        #endregion
    }
}
