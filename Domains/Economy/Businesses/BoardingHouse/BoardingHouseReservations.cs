using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Businesses.BoardingHouse
{
    /// <summary>
    /// D1F: one employer's (or contract customer's) held bed capacity. Canon
    /// §8.1D: the owner can reserve "rooms for expected contract or
    /// commercial customers"; §8.1F: "Employers, mines or major projects can
    /// reserve or subsidize beds." A reservation holds a COUNT of beds of one
    /// room type over a day window at locked reserved rates — real held
    /// capacity, never a percentage. Reserved-but-unconsumed beds are
    /// withheld from walk-in check-ins; the runtime enforces that.
    /// </summary>
    [Serializable]
    public sealed class BoardingBedReservation
    {
        public string ReservationId = string.Empty;

        /// <summary>The employer / contract customer holding the beds (a real business instance id).</summary>
        public string EmployerBusinessId = string.Empty;

        public string EmployerName = string.Empty;
        public BoardingRoomType RoomType = BoardingRoomType.SharedBed;

        /// <summary>How many beds of the room type this reservation holds.</summary>
        public int BedCount;

        /// <summary>First day the hold applies (inclusive).</summary>
        public int FromDayIndex;

        /// <summary>Last day the hold applies (inclusive).</summary>
        public int ToDayIndex;

        /// <summary>Locked weekly rate for check-ins under this reservation (Canon "rent agreement memory").</summary>
        public int ReservedWeeklyRateCents;

        /// <summary>Locked monthly rate for check-ins under this reservation.</summary>
        public int ReservedMonthlyRateCents;

        /// <summary>Locked nightly rate for transient check-ins under this reservation.</summary>
        public int ReservedNightlyRateCents;

        public bool IsActive = true;

        public BoardingBedReservation() { }

        public bool CoversDay(int dayIndex)
        {
            return IsActive && dayIndex >= FromDayIndex && dayIndex <= ToDayIndex;
        }

        public bool OverlapsWindow(int fromDayIndex, int toDayIndex)
        {
            return fromDayIndex <= ToDayIndex && toDayIndex >= FromDayIndex;
        }
    }

    /// <summary>
    /// D1F: the house's reservation book. Reservations are validated against
    /// REAL physical beds: a hold can never exceed the house's beds of that
    /// type minus already-held beds over the same window. Released (or
    /// expired) reservations free their beds; the hold history stays on the
    /// books. Refusals are loud — capacity is never conjured.
    /// </summary>
    public sealed class BoardingHouseReservations
    {
        private readonly List<string> diagnostics = new List<string>();
        private readonly List<BoardingBedReservation> reservations = new List<BoardingBedReservation>();
        private int reservationSequence = 1;

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<BoardingBedReservation> Reservations => reservations;

        /// <summary>
        /// Holds beds for an employer/contract customer. Rates lock at
        /// reservation (the "subsidize" side of Canon §8.1F is a reserved
        /// rate below the walk-in schedule). Returns the reservation id, or
        /// null plus a loud refusal.
        /// </summary>
        public string Reserve(string employerBusinessId, string employerName, BoardingRoomType roomType,
            int bedCount, int fromDayIndex, int toDayIndex,
            int reservedWeeklyRateCents, int reservedMonthlyRateCents, int reservedNightlyRateCents,
            BoardingRoomInventory inventory, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(employerBusinessId))
                return "BoardingHouseReservations.Reserve: a reservation needs a real employer business id.";
            if (bedCount <= 0)
                return "BoardingHouseReservations.Reserve: a reservation must hold at least one bed.";
            if (toDayIndex < fromDayIndex)
                return "BoardingHouseReservations.Reserve: the hold window ends before it starts.";
            if (fromDayIndex < 0)
                return "BoardingHouseReservations.Reserve: a hold needs a real start day.";
            if (reservedWeeklyRateCents < 0 || reservedMonthlyRateCents < 0 || reservedNightlyRateCents < 0)
                return "BoardingHouseReservations.Reserve: reserved rates cannot be negative.";
            if (inventory == null)
                return "BoardingHouseReservations.Reserve: no room inventory — beds cannot be held against a ledger.";

            int physical = 0;
            foreach (BoardingRoom room in inventory.Rooms)
                if (room != null && room.RoomType == roomType)
                    physical += Math.Max(0, room.BedCount);

            int alreadyHeld = 0;
            foreach (BoardingBedReservation existing in reservations)
            {
                if (existing == null || !existing.IsActive) continue;
                if (existing.RoomType != roomType) continue;
                if (!existing.OverlapsWindow(fromDayIndex, toDayIndex)) continue;
                alreadyHeld += Math.Max(0, existing.BedCount);
            }

            if (alreadyHeld + bedCount > physical)
                return $"BoardingHouseReservations.Reserve: cannot hold {bedCount} {roomType} bed(s) for '{employerBusinessId}' " +
                    $"over days {fromDayIndex}-{toDayIndex} — the house has {physical} such bed(s), {alreadyHeld} already held.";

            var reservation = new BoardingBedReservation
            {
                ReservationId = $"bh-res-{reservationSequence++}",
                EmployerBusinessId = employerBusinessId,
                EmployerName = employerName ?? string.Empty,
                RoomType = roomType,
                BedCount = bedCount,
                FromDayIndex = fromDayIndex,
                ToDayIndex = toDayIndex,
                ReservedWeeklyRateCents = reservedWeeklyRateCents,
                ReservedMonthlyRateCents = reservedMonthlyRateCents,
                ReservedNightlyRateCents = reservedNightlyRateCents,
                IsActive = true,
            };
            reservations.Add(reservation);
            diag.Add($"BoardingHouseReservations: held {bedCount} {roomType} bed(s) for '{employerBusinessId}' " +
                $"days {fromDayIndex}-{toDayIndex} ({reservation.ReservationId}) at {reservedWeeklyRateCents}¢/wk, " +
                $"{reservedMonthlyRateCents}¢/mo, {reservedNightlyRateCents}¢/night.");
            return reservation.ReservationId;
        }

        /// <summary>
        /// Releases a hold early. History stays on the books; the beds free
        /// immediately. Boarders already checked in under the reservation
        /// keep their locked rates (rent agreement memory) — the hold only
        /// stops covering NEW check-ins.
        /// </summary>
        public string ReleaseReservation(string reservationId, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            BoardingBedReservation reservation = Find(reservationId);
            if (reservation == null)
                return $"BoardingHouseReservations.ReleaseReservation: no reservation '{reservationId}'.";
            if (!reservation.IsActive)
                return $"BoardingHouseReservations.ReleaseReservation: reservation '{reservationId}' is already released.";
            reservation.IsActive = false;
            diag.Add($"BoardingHouseReservations: reservation '{reservationId}' released (day {dayIndex}) — held beds freed; lodged boarders keep their locked rates.");
            return null;
        }

        public BoardingBedReservation Find(string reservationId)
        {
            if (string.IsNullOrWhiteSpace(reservationId)) return null;
            for (int i = 0; i < reservations.Count; i++)
                if (reservations[i] != null && string.Equals(reservations[i].ReservationId, reservationId, StringComparison.OrdinalIgnoreCase))
                    return reservations[i];
            return null;
        }

        /// <summary>Beds held by active reservations of one room type covering the given day.</summary>
        public int ReservedBeds(BoardingRoomType roomType, int dayIndex)
        {
            int held = 0;
            for (int i = 0; i < reservations.Count; i++)
            {
                BoardingBedReservation reservation = reservations[i];
                if (reservation == null) continue;
                if (reservation.RoomType != roomType) continue;
                if (!reservation.CoversDay(dayIndex)) continue;
                held += Math.Max(0, reservation.BedCount);
            }
            return held;
        }

        #region Save / Load
        [Serializable]
        public sealed class BoardingHouseReservationsSaveDto
        {
            public int ReservationSequence = 1;
            public List<BoardingBedReservation> Reservations = new List<BoardingBedReservation>();
        }

        public BoardingHouseReservationsSaveDto CaptureSaveDto()
        {
            var dto = new BoardingHouseReservationsSaveDto { ReservationSequence = Math.Max(1, reservationSequence) };
            foreach (BoardingBedReservation reservation in reservations)
            {
                if (reservation != null) dto.Reservations.Add(reservation);
            }
            return dto;
        }

        public void LoadFromSaveDto(BoardingHouseReservationsSaveDto dto)
        {
            reservations.Clear();
            reservationSequence = 1;
            if (dto == null) return;
            reservationSequence = Math.Max(1, dto.ReservationSequence);
            if (dto.Reservations == null) return;
            foreach (BoardingBedReservation reservation in dto.Reservations)
            {
                if (reservation == null || string.IsNullOrWhiteSpace(reservation.ReservationId)) continue;
                reservations.Add(reservation);
            }
        }
        #endregion
    }
}
