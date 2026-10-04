using System;
using System.Collections.Generic;
using LandLedgers.Population;

namespace LandLedgers.Economy.Businesses.BoardingHouse
{
    /// <summary>
    /// W2C: the per-instance boarding-house runtime — rented rooms, weekly
    /// agreements, board meals, nightly states. One instance per
    /// BoardingHouse business instance (BusinessType.BoardingHouse = 8).
    ///
    /// What this owns (the detailed layer):
    /// - rooms as rentable inventory (beds, not percentages);
    /// - the weekly rate schedule as proprietor policy data;
    /// - boarder agreements: weekly or transient, room-only or room-and-board;
    /// - the boarding-house kitchen: board meals from real pantry lots;
    /// - the nightly-state register: every boarder resolves to a boarding
    ///   bed in a named room (the W3 hook for the settlement-wide
    ///   nightly-state audit).
    ///
    /// Relationship to the shared layer: SharedBusinessRuntimeManager
    /// remains the authority for weekly cash flow (revenue at
    /// BoardingHouseWeeklyBoardCents per service bed, supply upkeep). This
    /// runtime's default rate schedule matches that constant exactly so the
    /// two layers agree on price policy; its rent-due reports are settled
    /// by ledger authorities, never collected here. Money moves only
    /// through ledger authorities.
    /// </summary>
    public sealed class BoardingHouseShopRuntime
    {
        private readonly List<string> diagnostics = new List<string>();
        private readonly string businessInstanceId;
        private readonly BoardingRoomInventory roomInventory = new BoardingRoomInventory();
        private readonly BoardingRateSchedule rateSchedule = new BoardingRateSchedule();
        private readonly BoardingHouseBoarderRegister boarderRegister = new BoardingHouseBoarderRegister();
        private readonly BoardingHouseKitchen kitchen;
        private readonly EntityIdRegistry idRegistry;

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public string BusinessInstanceId => businessInstanceId;
        public BoardingRoomInventory RoomInventory => roomInventory;
        public BoardingRateSchedule RateSchedule => rateSchedule;
        public BoardingHouseBoarderRegister BoarderRegister => boarderRegister;
        public BoardingHouseKitchen Kitchen => kitchen;

        /// <summary>The nutrition-link producer: board meals this house served, by (day, person).</summary>
        public IBoardingHouseMealDaySource MealDaySource => kitchen != null ? kitchen.MealDaySource : null;

        public BoardingHouseShopRuntime(string businessInstanceId, EntityIdRegistry idRegistry)
        {
            this.businessInstanceId = businessInstanceId ?? string.Empty;
            this.idRegistry = idRegistry;
            this.kitchen = new BoardingHouseKitchen(idRegistry);
        }

        /// <summary>One-time opening pantry endowment — explicit, flagged, never auto-replenished.</summary>
        public void ApplyOpeningPantryEndowment(int dayIndex, List<string> diag)
        {
            BoardingHouseFoodBootstrap.ApplyBootstrapEndowment(kitchen.FoodStock, idRegistry, dayIndex, diag ?? diagnostics);
        }

        /// <summary>Adds a physical room. Returns the refusal, or null.</summary>
        public string AddRoom(BoardingRoomType roomType, int bedCount, List<string> diag)
        {
            return roomInventory.AddRoom(roomType, bedCount, diag ?? diagnostics);
        }

        /// <summary>Proprietor policy: replaces the rate schedule.</summary>
        public void SetRateSchedule(BoardingRateSchedule schedule, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (schedule == null)
            {
                diag.Add("BoardingHouseShopRuntime: no rate schedule offered — the current schedule stands.");
                return;
            }
            rateSchedule.LoadFromSaveDto(schedule.CaptureSaveDto());
            diag.Add("BoardingHouseShopRuntime: rate schedule updated (weekly and transient policy as data).");
        }

        /// <summary>
        /// Checks a person in. Weekly agreements lock the schedule's current
        /// weekly rate; transient stays lock the nightly rate and take
        /// nights-paid up front. Returns the refusal, or null.
        /// </summary>
        public string CheckInBoarder(int personId, string roomId, int bedIndex, BoarderStayKind stayKind,
            bool boardIncluded, int startDayIndex, int transientNights, List<string> diag)
        {
            diag = diag ?? diagnostics;
            BoardingRoom room = roomInventory.FindRoom(roomId);
            if (room == null)
                return $"BoardingHouseShopRuntime.CheckInBoarder: no room '{roomId}'.";
            int weeklyRate = rateSchedule.WeeklyRateCents(room.RoomType, boardIncluded);
            int nightlyRate = rateSchedule.TransientNightlyRateCents(boardIncluded);
            return boarderRegister.CheckIn(personId, roomId, bedIndex, stayKind, boardIncluded,
                weeklyRate, nightlyRate, startDayIndex, transientNights, roomInventory, diag);
        }

        /// <summary>Checks a person out, vacating their bed. Returns the refusal, or null.</summary>
        public string CheckOutBoarder(int personId, List<string> diag)
        {
            return boarderRegister.CheckOut(personId, roomInventory, diag ?? diagnostics);
        }

        /// <summary>
        /// W2C: the nightly-state answer for one person at this house — the
        /// W3 hook. A boarder resolves to a boarding bed in a named room.
        /// </summary>
        public bool TryGetNightlyPlacement(int personId, out BoardingNightlyPlacement placement)
        {
            return boarderRegister.TryGetNightlyPlacement(personId, businessInstanceId, out placement);
        }

        /// <summary>
        /// Runs one day: the kitchen serves today's board meals to
        /// board-included boarders, transient nights settle, and the served
        /// meal ledger prunes. Returns tonight's transient rent due.
        /// </summary>
        public List<BoarderRentDue> ExecuteDay(int dayIndex, int kitchenLaborMinutes, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (dayIndex < 0)
            {
                diag.Add("BoardingHouseShopRuntime: refused — a house day needs a real day index.");
                return new List<BoarderRentDue>();
            }

            int fullyServed = kitchen.ExecuteDay(boarderRegister, dayIndex, kitchenLaborMinutes, diag);
            List<BoarderRentDue> transientDue = boarderRegister.SettleTransientNight(dayIndex, roomInventory, diag);
            kitchen.MealLedger.PruneBefore(dayIndex, diag);

            int transientCents = 0;
            foreach (BoarderRentDue due in transientDue) transientCents += Math.Max(0, due.CentsDue);
            diag.Add($"BoardingHouseShopRuntime: day {dayIndex} — {boarderRegister.BoarderCount} boarder(s) lodged, " +
                $"{fullyServed} fully board-served, transient rent due {transientCents}¢.");
            return transientDue;
        }

        /// <summary>
        /// Runs weekly settlement: weekly agreements' rent due for one week.
        /// The caller settles these through ledger authorities.
        /// </summary>
        public List<BoarderRentDue> ExecuteWeek(int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            List<BoarderRentDue> weeklyDue = boarderRegister.RentDueWeekly(dayIndex, diag);
            int cents = 0;
            foreach (BoarderRentDue due in weeklyDue) cents += Math.Max(0, due.CentsDue);
            diag.Add($"BoardingHouseShopRuntime: week ending day {dayIndex} — {weeklyDue.Count} weekly boarder(s) owe {cents}¢ total.");
            return weeklyDue;
        }

        /// <summary>Occupancy headline for readouts: occupied/total beds plus board-included boarders.</summary>
        public string BuildOccupancyLine()
        {
            int total = roomInventory.TotalBeds();
            int occupied = roomInventory.OccupiedBeds();
            int boardIncluded = boarderRegister.BoarderPersonIdsWithBoardIncluded().Count;
            return $"Boarding House beds: {occupied}/{total} occupied | {Math.Max(0, total - occupied)} open | {boardIncluded} on board";
        }

        #region Save / Load
        [Serializable]
        public sealed class BoardingHouseShopRuntimeSaveDto
        {
            public string BusinessInstanceId = string.Empty;
            public BoardingRoomInventory.BoardingRoomInventorySaveDto RoomInventory = new BoardingRoomInventory.BoardingRoomInventorySaveDto();
            public BoardingRateSchedule.BoardingRateScheduleSaveDto RateSchedule = new BoardingRateSchedule.BoardingRateScheduleSaveDto();
            public BoardingHouseBoarderRegister.BoardingHouseBoarderRegisterSaveDto BoarderRegister = new BoardingHouseBoarderRegister.BoardingHouseBoarderRegisterSaveDto();
            public BoardingHouseKitchen.BoardingHouseKitchenSaveDto Kitchen = new BoardingHouseKitchen.BoardingHouseKitchenSaveDto();
        }

        public BoardingHouseShopRuntimeSaveDto CaptureSaveDto()
        {
            return new BoardingHouseShopRuntimeSaveDto
            {
                BusinessInstanceId = businessInstanceId,
                RoomInventory = roomInventory.CaptureSaveDto(),
                RateSchedule = rateSchedule.CaptureSaveDto(),
                BoarderRegister = boarderRegister.CaptureSaveDto(),
                Kitchen = kitchen.CaptureSaveDto(),
            };
        }

        public void LoadFromSaveDto(BoardingHouseShopRuntimeSaveDto dto)
        {
            if (dto == null) return;
            roomInventory.LoadFromSaveDto(dto.RoomInventory);
            rateSchedule.LoadFromSaveDto(dto.RateSchedule);
            kitchen.LoadFromSaveDto(dto.Kitchen);
            boarderRegister.LoadFromSaveDto(dto.BoarderRegister);

            // Post-load integrity: every boarder's bed must actually be
            // theirs in the inventory — a corrupted pair is dropped loudly,
            // never silently absorbed.
            var orphaned = new List<int>();
            foreach (BoarderRecord record in boarderRegister.Boarders)
            {
                if (record == null) continue;
                BoardingRoom room = roomInventory.FindRoom(record.RoomId);
                if (room == null || room.OccupantOfBed(record.BedIndex) != record.PersonId)
                    orphaned.Add(record.PersonId);
            }
            foreach (int personId in orphaned)
            {
                boarderRegister.CheckOut(personId, roomInventory, diagnostics);
                diagnostics.Add($"BoardingHouseShopRuntime: boarder {personId} dropped on load — bed assignment did not survive the round trip.");
            }
        }
        #endregion
    }
}
