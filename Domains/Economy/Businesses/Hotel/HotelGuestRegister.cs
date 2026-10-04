using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.BoardingHouse;

namespace LandLedgers.Economy.Businesses.Hotel
{
    /// <summary>
    /// W3A: the two hotel stay terms (Canon §8.1A/§8.1C/§8.1D): a nightly
    /// stay (short-stay traveler, billed per night, expires when paid
    /// nights run out) versus a weekly agreement (weeks-or-months guest,
    /// billed per week, persists until checkout — the period hotel's
    /// longer-stay custom).
    /// </summary>
    public enum HotelStayKind
    {
        Nightly = 0,
        Weekly = 1,
    }

    /// <summary>
    /// W3A: one guest's lodging agreement — the continuing nightly-state
    /// fact. The rate is locked at check-in (rent is an occupancy
    /// agreement, not a daily spot-price reset — Canon "Rent agreement
    /// memory" lock). Nightly stays carry paid nights remaining; weekly
    /// stays carry the locked weekly rate.
    /// </summary>
    [Serializable]
    public sealed class HotelGuestRecord
    {
        public int PersonId;
        public string RoomNumber = string.Empty;
        public int BedIndex;
        public HotelRoomClass RoomClass = HotelRoomClass.SingleRoom;
        public HotelStayKind StayKind = HotelStayKind.Nightly;
        public int LockedNightlyRateCents;
        public int LockedWeeklyRateCents;
        public int StartDayIndex;
        /// <summary>Nightly stays: paid nights remaining (checked out when this reaches 0).</summary>
        public int NightsRemaining;
        public BoardingNightlyState NightlyState = BoardingNightlyState.HotelRoom;

        public HotelGuestRecord() { }
    }

    /// <summary>
    /// W3A: one room-night sale — a single guest occupying a single bed
    /// for a single night. This is the hotel's sales ledger line: who
    /// slept where, on which night, at what locked rate, and which linen
    /// sets the turnover consumed (with their provenance chains). Money
    /// moves only when a ledger authority settles these lines; this
    /// register only reports them.
    /// </summary>
    [Serializable]
    public sealed class HotelRoomNightSale
    {
        public int PersonId;
        public string RoomNumber = string.Empty;
        public int BedIndex;
        public HotelRoomClass RoomClass = HotelRoomClass.SingleRoom;
        public HotelStayKind StayKind = HotelStayKind.Nightly;
        /// <summary>The game day the guest sleeps (the night of this day index).</summary>
        public int DayIndex;
        /// <summary>Cents charged for the night. 0 for weekly guests — their rent settles weekly.</summary>
        public int CentsCharged;
        /// <summary>Clean linen sets the housekeeping turned over for this night (0 when the shelf failed).</summary>
        public int LinenSetsUsed;
        /// <summary>Provenance chains of the linen lots consumed (parallel to LinenSetsUsed, may be shorter on shortfall).</summary>
        public List<string> LinenProvenanceChains = new List<string>();
        public string Label = string.Empty;

        public HotelRoomNightSale() { }
    }

    /// <summary>
    /// W3A: one weekly guest's rent due for the week. The caller settles
    /// these through ledger authorities; the register only reports who
    /// owes what under which agreement.
    /// </summary>
    [Serializable]
    public sealed class HotelRoomRentDue
    {
        public int PersonId;
        public string RoomNumber = string.Empty;
        public int NightsCovered = 7;
        public int CentsDue;
        public string Label = string.Empty;

        public HotelRoomRentDue() { }
    }

    /// <summary>
    /// W3A: the per-hotel register of guest Persons assigned to beds.
    /// This is the nightly-state authority for the hotel: every guest's
    /// nightly state is <see cref="BoardingNightlyState.HotelRoom"/> with a
    /// named room number and bed — the settlement-wide answer to "Where
    /// does this Person sleep tonight?" Bed occupancy itself stays in
    /// <see cref="HotelRoomInventory"/>; the register holds the agreements
    /// and produces the room-night sales records.
    /// </summary>
    public sealed class HotelGuestRegister
    {
        private readonly List<string> diagnostics = new List<string>();
        private readonly List<HotelGuestRecord> guests = new List<HotelGuestRecord>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<HotelGuestRecord> Guests => guests;

        public int GuestCount => guests.Count;

        /// <summary>
        /// Checks a real guest into a specific open bed. Rates are locked at
        /// agreement (from the schedule passed in). Nightly stays need at
        /// least one paid night. Returns the refusal, or null on success.
        /// </summary>
        public string CheckIn(int personId, string roomNumber, int bedIndex, HotelRoomClass roomClass,
            HotelStayKind stayKind, int nightlyRateCents, int weeklyRateCents,
            int startDayIndex, int nightsPaid, HotelRoomInventory inventory, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (personId <= 0)
                return "HotelGuestRegister.CheckIn: a lodging agreement needs a real person id.";
            if (FindRecord(personId) != null)
                return $"HotelGuestRegister.CheckIn: person {personId} already holds a hotel agreement here.";
            if (inventory == null)
                return "HotelGuestRegister.CheckIn: no room inventory — a guest cannot sleep in a ledger.";
            if (stayKind == HotelStayKind.Nightly && nightsPaid <= 0)
                return "HotelGuestRegister.CheckIn: a nightly stay needs at least one paid night.";
            if (nightlyRateCents < 0 || weeklyRateCents < 0)
                return "HotelGuestRegister.CheckIn: rates cannot be negative.";

            string refusal = inventory.AssignSpecificBed(personId, roomNumber, bedIndex, diag);
            if (refusal != null) return refusal;

            guests.Add(new HotelGuestRecord
            {
                PersonId = personId,
                RoomNumber = roomNumber ?? string.Empty,
                BedIndex = bedIndex,
                RoomClass = roomClass,
                StayKind = stayKind,
                LockedNightlyRateCents = Math.Max(0, nightlyRateCents),
                LockedWeeklyRateCents = Math.Max(0, weeklyRateCents),
                StartDayIndex = startDayIndex,
                NightsRemaining = stayKind == HotelStayKind.Nightly ? nightsPaid : 0,
                NightlyState = BoardingNightlyState.HotelRoom,
            });
            diag.Add($"HotelGuestRegister: person {personId} checked in ({stayKind}, {roomClass}) at bed {bedIndex} in '{roomNumber}', day {startDayIndex}.");
            return null;
        }

        /// <summary>Checks the guest out and vacates their bed.</summary>
        public string CheckOut(int personId, HotelRoomInventory inventory, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (personId <= 0)
                return "HotelGuestRegister.CheckOut: needs a real person id.";
            HotelGuestRecord record = FindRecord(personId);
            if (record == null)
                return $"HotelGuestRegister.CheckOut: person {personId} holds no hotel agreement here.";
            guests.Remove(record);
            if (inventory != null) inventory.ReleaseBedByPerson(personId, diag);
            diag.Add($"HotelGuestRegister: person {personId} checked out (nightly-state history stays in the ledger).");
            return null;
        }

        public HotelGuestRecord FindRecord(int personId)
        {
            if (personId <= 0) return null;
            for (int i = 0; i < guests.Count; i++)
                if (guests[i] != null && guests[i].PersonId == personId)
                    return guests[i];
            return null;
        }

        /// <summary>
        /// W3A: resolves one guest's nightly placement — the W2C hook made
        /// real. A guest always resolves to a hotel room (never to
        /// another member of the enum); non-guests are not this hotel's
        /// to place.
        /// </summary>
        public bool TryGetNightlyPlacement(int personId, string businessInstanceId, out HotelNightlyPlacement placement)
        {
            placement = null;
            HotelGuestRecord record = FindRecord(personId);
            if (record == null) return false;
            placement = new HotelNightlyPlacement(personId, record.RoomNumber, record.BedIndex,
                businessInstanceId ?? string.Empty, record.RoomClass, record.StayKind, record.LockedNightlyRateCents);
            return true;
        }

        /// <summary>
        /// Nightly settlement: every guest present tonight produces a
        /// room-night sale (nightly guests at their locked nightly rate;
        /// weekly guests at 0 — their rent settles weekly). Nightly
        /// stays decrement paid nights and check out after their last
        /// paid night. Linen turnover is applied by the caller
        /// (housekeeping), which fills in the sale's linen fields.
        /// </summary>
        public List<HotelRoomNightSale> SettleNight(int dayIndex, HotelRoomInventory inventory, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var sales = new List<HotelRoomNightSale>();
            var expired = new List<int>();
            for (int i = 0; i < guests.Count; i++)
            {
                HotelGuestRecord record = guests[i];
                if (record == null) continue;

                if (record.StayKind == HotelStayKind.Nightly)
                {
                    if (record.NightsRemaining <= 0) { expired.Add(record.PersonId); continue; }
                    sales.Add(new HotelRoomNightSale
                    {
                        PersonId = record.PersonId,
                        RoomNumber = record.RoomNumber,
                        BedIndex = record.BedIndex,
                        RoomClass = record.RoomClass,
                        StayKind = HotelStayKind.Nightly,
                        DayIndex = dayIndex,
                        CentsCharged = Math.Max(0, record.LockedNightlyRateCents),
                        Label = $"room night — person {record.PersonId}, {record.RoomClass} '{record.RoomNumber}' bed {record.BedIndex}, night of day {dayIndex}",
                    });
                    record.NightsRemaining--;
                    if (record.NightsRemaining <= 0) expired.Add(record.PersonId);
                }
                else
                {
                    sales.Add(new HotelRoomNightSale
                    {
                        PersonId = record.PersonId,
                        RoomNumber = record.RoomNumber,
                        BedIndex = record.BedIndex,
                        RoomClass = record.RoomClass,
                        StayKind = HotelStayKind.Weekly,
                        DayIndex = dayIndex,
                        CentsCharged = 0,
                        Label = $"room night (weekly term) — person {record.PersonId}, {record.RoomClass} '{record.RoomNumber}' bed {record.BedIndex}, night of day {dayIndex}",
                    });
                }
            }

            foreach (int personId in expired)
            {
                diag.Add($"HotelGuestRegister: person {personId}'s nightly stay ended — paid nights exhausted, bed released.");
                CheckOut(personId, inventory, diag);
            }
            diag.Add($"HotelGuestRegister: night of day {dayIndex} settled — {sales.Count} room-night sale(s).");
            return sales;
        }

        /// <summary>
        /// Weekly agreements' rent due for one week (called by the
        /// runtime's weekly settlement). Rates locked at check-in.
        /// </summary>
        public List<HotelRoomRentDue> RentDueWeekly(int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var due = new List<HotelRoomRentDue>();
            for (int i = 0; i < guests.Count; i++)
            {
                HotelGuestRecord record = guests[i];
                if (record == null || record.StayKind != HotelStayKind.Weekly) continue;
                if (record.LockedWeeklyRateCents <= 0) continue;
                due.Add(new HotelRoomRentDue
                {
                    PersonId = record.PersonId,
                    RoomNumber = record.RoomNumber,
                    NightsCovered = 7,
                    CentsDue = record.LockedWeeklyRateCents,
                    Label = $"weekly room rent — person {record.PersonId}, {record.RoomClass} '{record.RoomNumber}' bed {record.BedIndex}, week ending day {dayIndex}",
                });
            }
            diag.Add($"HotelGuestRegister: weekly settlement day {dayIndex} — {due.Count} weekly guest(s) owe rent.");
            return due;
        }

        #region Save / Load
        [Serializable]
        public sealed class HotelGuestRegisterSaveDto
        {
            public List<HotelGuestRecord> Guests = new List<HotelGuestRecord>();
        }

        public HotelGuestRegisterSaveDto CaptureSaveDto()
        {
            var dto = new HotelGuestRegisterSaveDto();
            foreach (HotelGuestRecord record in guests)
            {
                if (record == null || record.PersonId <= 0) continue;
                dto.Guests.Add(new HotelGuestRecord
                {
                    PersonId = record.PersonId,
                    RoomNumber = record.RoomNumber ?? string.Empty,
                    BedIndex = record.BedIndex,
                    RoomClass = record.RoomClass,
                    StayKind = record.StayKind,
                    LockedNightlyRateCents = Math.Max(0, record.LockedNightlyRateCents),
                    LockedWeeklyRateCents = Math.Max(0, record.LockedWeeklyRateCents),
                    StartDayIndex = record.StartDayIndex,
                    NightsRemaining = Math.Max(0, record.NightsRemaining),
                    NightlyState = BoardingNightlyState.HotelRoom,
                });
            }
            return dto;
        }

        public void LoadFromSaveDto(HotelGuestRegisterSaveDto dto)
        {
            guests.Clear();
            if (dto?.Guests == null) return;
            foreach (HotelGuestRecord record in dto.Guests)
            {
                if (record == null || record.PersonId <= 0) continue;
                guests.Add(new HotelGuestRecord
                {
                    PersonId = record.PersonId,
                    RoomNumber = record.RoomNumber ?? string.Empty,
                    BedIndex = record.BedIndex,
                    RoomClass = record.RoomClass,
                    StayKind = record.StayKind,
                    LockedNightlyRateCents = Math.Max(0, record.LockedNightlyRateCents),
                    LockedWeeklyRateCents = Math.Max(0, record.LockedWeeklyRateCents),
                    StartDayIndex = record.StartDayIndex,
                    NightsRemaining = Math.Max(0, record.NightsRemaining),
                    NightlyState = BoardingNightlyState.HotelRoom,
                });
            }
        }
        #endregion
    }
}
