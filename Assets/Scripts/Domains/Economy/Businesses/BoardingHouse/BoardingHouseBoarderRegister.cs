using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Businesses.BoardingHouse
{
    /// <summary>
    /// W2C: the two boarding stay terms (Canon §8.1A/§8.1C): a weekly
    /// agreement (weeks-or-months boarder, billed per week, persists until
    /// checkout) versus a transient night-to-night stay (short-stay
    /// traveler, billed per night, expires when paid nights run out).
    /// D1F adds Monthly (Canon §8.1D "weekly or longer-stay terms"; §8.1C
    /// boarders who stay "for weeks or months before renting"): a
    /// longer-stay agreement billed per monthly cycle, persisting until
    /// checkout like the weekly term.
    /// </summary>
    public enum BoarderStayKind
    {
        Weekly = 0,
        Transient = 1,
        /// <summary>D1F: longer-stay monthly agreement (Canon §8.1D).</summary>
        Monthly = 2,
    }

    /// <summary>
    /// D1F: boarding billing-cycle calibration. Canon §8.1D names "weekly or
    /// longer-stay terms" without fixing the month's length; the cycle below
    /// is TUNING (Canon Part XV), not a canon claim.
    /// </summary>
    public static class BoardingHouseBilling
    {
        /// <summary>TUNING: days per monthly billing cycle.</summary>
        public const int MonthlyBillingDays = 30;
    }

    /// <summary>
    /// W2C: one boarder's lodging agreement — the continuing nightly-state
    /// fact. The rate is locked at check-in (rent is an occupancy agreement,
    /// not a daily spot-price reset — Canon "Rent agreement memory" lock).
    /// BoardIncluded marks board-included meals: the kitchen feeds this
    /// person while the agreement stands.
    /// </summary>
    [Serializable]
    public sealed class BoarderRecord
    {
        public int PersonId;
        public string RoomId = string.Empty;
        public int BedIndex;
        public BoarderStayKind StayKind = BoarderStayKind.Weekly;
        public bool BoardIncluded;
        public int WeeklyRateCents;
        /// <summary>D1F: locked monthly rate (Canon §8.1D longer-stay terms). 0 for non-monthly agreements.</summary>
        public int MonthlyRateCents;
        public int TransientNightlyRateCents;
        public int StartDayIndex;
        /// <summary>Transient stays: paid nights remaining (checked out when this reaches 0).</summary>
        public int TransientNightsRemaining;
        /// <summary>D1F: the reservation this boarder checked in under (empty = walk-in).</summary>
        public string ReservationId = string.Empty;
        public BoardingNightlyState NightlyState = BoardingNightlyState.BoardingBed;

        public BoarderRecord() { }
    }

    /// <summary>
    /// W2C: one boarder's rent due for the week (weekly) or the day
    /// (transient). The caller settles these through ledger authorities;
    /// this register only reports who owes what under which agreement.
    /// </summary>
    [Serializable]
    public sealed class BoarderRentDue
    {
        public int PersonId;
        public string RoomId = string.Empty;
        public BoarderStayKind StayKind;
        public bool BoardIncluded;
        public int NightsCovered;
        public int CentsDue;
        public string Label = string.Empty;

        public BoarderRentDue() { }
    }

    /// <summary>
    /// W2C: the per-house register of boarder Persons assigned to beds.
    /// This is the nightly-state authority for the house: every boarder's
    /// nightly state is <see cref="BoardingNightlyState.BoardingBed"/> with
    /// a named room and bed — the settlement-wide answer to "Where does
    /// this Person sleep tonight?" that the W3 hotel package extends with
    /// hotel-room placements. Bed occupancy itself stays in
    /// <see cref="BoardingRoomInventory"/>; the register holds the
    /// agreements.
    /// </summary>
    public sealed class BoardingHouseBoarderRegister
    {
        private readonly List<string> diagnostics = new List<string>();
        private readonly List<BoarderRecord> boarders = new List<BoarderRecord>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<BoarderRecord> Boarders => boarders;

        public int BoarderCount => boarders.Count;

        /// <summary>
        /// Checks a real person into a specific open bed. The rate is locked
        /// at agreement. Transient stays need at least one paid night.
        /// D1F: monthly agreements lock the monthly rate; a reservation id
        /// tags the boarder to their employer's held beds (empty = walk-in).
        /// Returns the refusal, or null on success.
        /// </summary>
        public string CheckIn(int personId, string roomId, int bedIndex, BoarderStayKind stayKind,
            bool boardIncluded, int weeklyRateCents, int transientNightlyRateCents,
            int startDayIndex, int transientNights, BoardingRoomInventory inventory, List<string> diag,
            int monthlyRateCents = 0, string reservationId = null)
        {
            diag = diag ?? diagnostics;
            if (personId <= 0)
                return "BoardingHouseBoarderRegister.CheckIn: a lodging agreement needs a real person id.";
            if (FindRecord(personId) != null)
                return $"BoardingHouseBoarderRegister.CheckIn: person {personId} already holds a boarding agreement here.";
            if (inventory == null)
                return "BoardingHouseBoarderRegister.CheckIn: no room inventory — a person cannot sleep in a ledger.";
            if (stayKind == BoarderStayKind.Transient && transientNights <= 0)
                return "BoardingHouseBoarderRegister.CheckIn: a transient stay needs at least one paid night.";
            if (weeklyRateCents < 0 || transientNightlyRateCents < 0 || monthlyRateCents < 0)
                return "BoardingHouseBoarderRegister.CheckIn: rates cannot be negative.";

            string refusal = inventory.AssignSpecificBed(personId, roomId, bedIndex, diag);
            if (refusal != null) return refusal;

            boarders.Add(new BoarderRecord
            {
                PersonId = personId,
                RoomId = roomId,
                BedIndex = bedIndex,
                StayKind = stayKind,
                BoardIncluded = boardIncluded,
                WeeklyRateCents = weeklyRateCents,
                MonthlyRateCents = monthlyRateCents,
                TransientNightlyRateCents = transientNightlyRateCents,
                StartDayIndex = startDayIndex,
                TransientNightsRemaining = stayKind == BoarderStayKind.Transient ? transientNights : 0,
                ReservationId = reservationId ?? string.Empty,
                NightlyState = BoardingNightlyState.BoardingBed,
            });
            string package = boardIncluded ? "room and board" : "room only";
            string reservationNote = string.IsNullOrWhiteSpace(reservationId) ? string.Empty : $" under reservation '{reservationId}'";
            diag.Add($"BoardingHouseBoarderRegister: person {personId} checked in ({stayKind}, " +
                $"{package}) at bed {bedIndex} in '{roomId}', day {startDayIndex}{reservationNote}.");
            return null;
        }

        /// <summary>
        /// Checks the person out and vacates their bed. The vacated bed
        /// soils its room (D1F: Canon §8.1E bed turnover) — chamber work must
        /// turn the room before the bed is re-let. The load-integrity path
        /// passes soilBed: false for beds whose assignment never survived.
        /// </summary>
        public string CheckOut(int personId, BoardingRoomInventory inventory, List<string> diag, bool soilBed = true)
        {
            diag = diag ?? diagnostics;
            if (personId <= 0)
                return "BoardingHouseBoarderRegister.CheckOut: needs a real person id.";
            BoarderRecord record = FindRecord(personId);
            if (record == null)
                return $"BoardingHouseBoarderRegister.CheckOut: person {personId} holds no boarding agreement here.";
            boarders.Remove(record);
            if (inventory != null)
            {
                inventory.ReleaseBedByPerson(personId, diag);
                if (soilBed)
                    inventory.MarkBedSoiled(record.RoomId, record.BedIndex, diag);
            }
            diag.Add($"BoardingHouseBoarderRegister: person {personId} checked out (day of last stay kept as history in the ledger).");
            return null;
        }

        public BoarderRecord FindRecord(int personId)
        {
            if (personId <= 0) return null;
            for (int i = 0; i < boarders.Count; i++)
                if (boarders[i] != null && boarders[i].PersonId == personId)
                    return boarders[i];
            return null;
        }

        /// <summary>
        /// W2C: resolves one person's nightly placement for the house —
        /// the W3 hook. A boarder always resolves to a boarding bed in a
        /// named room; non-boarders are not this house's to place.
        /// </summary>
        public bool TryGetNightlyPlacement(int personId, string businessInstanceId, out BoardingNightlyPlacement placement)
        {
            placement = null;
            BoarderRecord record = FindRecord(personId);
            if (record == null) return false;
            placement = new BoardingNightlyPlacement(personId, BoardingNightlyState.BoardingBed,
                record.RoomId, record.BedIndex, businessInstanceId ?? string.Empty);
            return true;
        }

        /// <summary>Persons entitled to board-included meals today (Canon §2.5).</summary>
        public List<int> BoarderPersonIdsWithBoardIncluded()
        {
            var ids = new List<int>();
            for (int i = 0; i < boarders.Count; i++)
            {
                BoarderRecord record = boarders[i];
                if (record != null && record.BoardIncluded && record.PersonId > 0)
                    ids.Add(record.PersonId);
            }
            return ids;
        }

        /// <summary>
        /// Weekly agreements' rent due for one week (called by the runtime's
        /// weekly settlement). Rates locked at check-in.
        /// </summary>
        public List<BoarderRentDue> RentDueWeekly(int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var due = new List<BoarderRentDue>();
            for (int i = 0; i < boarders.Count; i++)
            {
                BoarderRecord record = boarders[i];
                if (record == null || record.StayKind != BoarderStayKind.Weekly) continue;
                if (record.WeeklyRateCents <= 0) continue;
                string package = record.BoardIncluded ? "room and board" : "room only";
                due.Add(new BoarderRentDue
                {
                    PersonId = record.PersonId,
                    RoomId = record.RoomId,
                    StayKind = BoarderStayKind.Weekly,
                    BoardIncluded = record.BoardIncluded,
                    NightsCovered = 7,
                    CentsDue = record.WeeklyRateCents,
                    Label = $"weekly {package} — person {record.PersonId}, bed {record.BedIndex} '{record.RoomId}', day {dayIndex}",
                });
            }
            diag.Add($"BoardingHouseBoarderRegister: weekly settlement day {dayIndex} — {due.Count} weekly boarder(s) owe rent.");
            return due;
        }

        /// <summary>
        /// D1F: monthly agreements' rent due for one monthly cycle (Canon
        /// §8.1D "weekly or longer-stay terms"). Rates locked at check-in;
        /// the cycle length is <see cref="BoardingHouseBilling.MonthlyBillingDays"/>
        /// (TUNING). The caller settles these through ledger authorities.
        /// </summary>
        public List<BoarderRentDue> RentDueMonthly(int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var due = new List<BoarderRentDue>();
            for (int i = 0; i < boarders.Count; i++)
            {
                BoarderRecord record = boarders[i];
                if (record == null || record.StayKind != BoarderStayKind.Monthly) continue;
                if (record.MonthlyRateCents <= 0) continue;
                string package = record.BoardIncluded ? "room and board" : "room only";
                due.Add(new BoarderRentDue
                {
                    PersonId = record.PersonId,
                    RoomId = record.RoomId,
                    StayKind = BoarderStayKind.Monthly,
                    BoardIncluded = record.BoardIncluded,
                    NightsCovered = BoardingHouseBilling.MonthlyBillingDays,
                    CentsDue = record.MonthlyRateCents,
                    Label = $"monthly {package} — person {record.PersonId}, bed {record.BedIndex} '{record.RoomId}', day {dayIndex}",
                });
            }
            diag.Add($"BoardingHouseBoarderRegister: monthly settlement day {dayIndex} — {due.Count} monthly boarder(s) owe rent.");
            return due;
        }

        /// <summary>
        /// Transient nights: decrements paid nights and returns tonight's
        /// due list. Expired stays check out after their last paid night.
        /// </summary>
        public List<BoarderRentDue> SettleTransientNight(int dayIndex, BoardingRoomInventory inventory, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var due = new List<BoarderRentDue>();
            var expired = new List<int>();
            for (int i = 0; i < boarders.Count; i++)
            {
                BoarderRecord record = boarders[i];
                if (record == null || record.StayKind != BoarderStayKind.Transient) continue;
                if (record.TransientNightsRemaining <= 0) { expired.Add(record.PersonId); continue; }
                if (record.TransientNightlyRateCents > 0)
                {
                    due.Add(new BoarderRentDue
                    {
                        PersonId = record.PersonId,
                        RoomId = record.RoomId,
                        StayKind = BoarderStayKind.Transient,
                        BoardIncluded = record.BoardIncluded,
                        NightsCovered = 1,
                        CentsDue = record.TransientNightlyRateCents,
                        Label = $"transient night — person {record.PersonId}, bed {record.BedIndex} '{record.RoomId}', day {dayIndex}",
                    });
                }
                record.TransientNightsRemaining--;
                if (record.TransientNightsRemaining <= 0) expired.Add(record.PersonId);
            }

            foreach (int personId in expired)
            {
                diag.Add($"BoardingHouseBoarderRegister: person {personId}'s transient stay ended — paid nights exhausted, bed released.");
                CheckOut(personId, inventory, diag);
            }
            return due;
        }

        #region Save / Load
        [Serializable]
        public sealed class BoardingHouseBoarderRegisterSaveDto
        {
            public List<BoarderRecord> Boarders = new List<BoarderRecord>();
        }

        public BoardingHouseBoarderRegisterSaveDto CaptureSaveDto()
        {
            var dto = new BoardingHouseBoarderRegisterSaveDto();
            foreach (BoarderRecord record in boarders)
            {
                if (record == null || record.PersonId <= 0) continue;
                dto.Boarders.Add(new BoarderRecord
                {
                    PersonId = record.PersonId,
                    RoomId = record.RoomId ?? string.Empty,
                    BedIndex = record.BedIndex,
                    StayKind = record.StayKind,
                    BoardIncluded = record.BoardIncluded,
                    WeeklyRateCents = Math.Max(0, record.WeeklyRateCents),
                    MonthlyRateCents = Math.Max(0, record.MonthlyRateCents),
                    TransientNightlyRateCents = Math.Max(0, record.TransientNightlyRateCents),
                    StartDayIndex = record.StartDayIndex,
                    TransientNightsRemaining = Math.Max(0, record.TransientNightsRemaining),
                    ReservationId = record.ReservationId ?? string.Empty,
                    NightlyState = BoardingNightlyState.BoardingBed,
                });
            }
            return dto;
        }

        public void LoadFromSaveDto(BoardingHouseBoarderRegisterSaveDto dto)
        {
            boarders.Clear();
            if (dto?.Boarders == null) return;
            foreach (BoarderRecord record in dto.Boarders)
            {
                if (record == null || record.PersonId <= 0) continue;
                record.NightlyState = BoardingNightlyState.BoardingBed;
                boarders.Add(record);
            }
        }
        #endregion
    }
}
