using System;
using System.Collections.Generic;
using LandLedgers.Population;
using LandLedgers.Primitives;

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
    /// - boarder agreements: weekly, monthly (D1F) or transient,
    ///   room-only or room-and-board;
    /// - the boarding-house kitchen: board meals from real pantry lots,
    ///   fired with real fuel (D1F);
    /// - the nightly-state register: every boarder resolves to a boarding
    ///   bed in a named room (the W3 hook for the settlement-wide
    ///   nightly-state audit);
    /// - chamber staff and room turnover (D1F, Canon §8.1E);
    /// - bed reservations for employers/contract customers (D1F, Canon
    ///   §8.1D/§8.1F);
    /// - conduct incidents as facts (D1F, Canon §8.1G);
    /// - standing supply agreements + resupply signals (D1F, Canon §8.1B).
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
        /// <summary>D1F TUNING: chamber-work minutes one housekeeper gives per day (Canon Part XV).</summary>
        public const int HousekeeperMinutesPerDay = 240;

        private readonly List<string> diagnostics = new List<string>();
        private readonly string businessInstanceId;
        private readonly BoardingRoomInventory roomInventory = new BoardingRoomInventory();
        private readonly BoardingRateSchedule rateSchedule = new BoardingRateSchedule();
        private readonly BoardingHouseBoarderRegister boarderRegister = new BoardingHouseBoarderRegister();
        private readonly BoardingHouseKitchen kitchen;
        private readonly BoardingHousekeepingStaff housekeepingStaff = new BoardingHousekeepingStaff();
        private readonly BoardingHouseReservations reservations = new BoardingHouseReservations();
        private readonly BoardingHouseConductLog conductLog = new BoardingHouseConductLog();
        private readonly BoardingHouseConductPolicy conductPolicy = new BoardingHouseConductPolicy();
        private readonly BoardingHouseFuelStock fuelStock = new BoardingHouseFuelStock();
        private readonly BoardingHouseStandingOrders standingOrders = new BoardingHouseStandingOrders();
        private readonly EntityIdRegistry idRegistry;

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public string BusinessInstanceId => businessInstanceId;
        public BoardingRoomInventory RoomInventory => roomInventory;
        public BoardingRateSchedule RateSchedule => rateSchedule;
        public BoardingHouseBoarderRegister BoarderRegister => boarderRegister;
        public BoardingHouseKitchen Kitchen => kitchen;
        /// <summary>D1F: the chamber-staff roster.</summary>
        public BoardingHousekeepingStaff HousekeepingStaff => housekeepingStaff;
        /// <summary>D1F: the employer's reservation book.</summary>
        public BoardingHouseReservations Reservations => reservations;
        /// <summary>D1F: conduct incidents as facts.</summary>
        public BoardingHouseConductLog ConductLog => conductLog;
        /// <summary>D1F: the proprietor's conduct policy as data.</summary>
        public BoardingHouseConductPolicy ConductPolicy => conductPolicy;
        /// <summary>D1F: the house's stove-fuel store.</summary>
        public BoardingHouseFuelStock FuelStock => fuelStock;
        /// <summary>D1F: standing supply agreements + resupply signals.</summary>
        public BoardingHouseStandingOrders StandingOrders => standingOrders;

        /// <summary>
        /// D1F: when true (default), the kitchen needs fuel to cook — the
        /// stove stays cold without it. Routes around unmodeled contexts
        /// when false (D1E precedent).
        /// </summary>
        public bool RequireKitchenFuel
        {
            get => kitchen != null && kitchen.RequireFuel;
            set { if (kitchen != null) kitchen.RequireFuel = value; }
        }

        /// <summary>The nutrition-link producer: board meals this house served, by (day, person).</summary>
        public IBoardingHouseMealDaySource MealDaySource => kitchen != null ? kitchen.MealDaySource : null;

        public BoardingHouseShopRuntime(string businessInstanceId, EntityIdRegistry idRegistry)
        {
            this.businessInstanceId = businessInstanceId ?? string.Empty;
            this.idRegistry = idRegistry;
            this.kitchen = new BoardingHouseKitchen(idRegistry);
            this.kitchen.FuelStock = this.fuelStock;
        }

        /// <summary>One-time opening pantry endowment — explicit, flagged, never auto-replenished.</summary>
        public void ApplyOpeningPantryEndowment(int dayIndex, List<string> diag)
        {
            BoardingHouseFoodBootstrap.ApplyBootstrapEndowment(kitchen.FoodStock, idRegistry, dayIndex, diag ?? diagnostics);
        }

        /// <summary>D1F: one-time opening fuel endowment — explicit, flagged, never auto-replenished.</summary>
        public void ApplyOpeningFuelEndowment(int dayIndex, List<string> diag)
        {
            BoardingHouseFuelBootstrap.ApplyBootstrapEndowment(fuelStock, idRegistry, dayIndex, diag ?? diagnostics);
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
            diag.Add("BoardingHouseShopRuntime: rate schedule updated (weekly, monthly and transient policy as data).");
        }

        /// <summary>
        /// Checks a person in. Weekly/monthly agreements lock the schedule's
        /// current rate; transient stays lock the nightly rate and take
        /// nights-paid up front. D1F: a reservation id checks the boarder in
        /// under an employer's held beds at the reservation's locked rates;
        /// walk-ins are refused when only reserved-but-unconsumed capacity
        /// remains. Returns the refusal, or null.
        /// </summary>
        public string CheckInBoarder(int personId, string roomId, int bedIndex, BoarderStayKind stayKind,
            bool boardIncluded, int startDayIndex, int transientNights, List<string> diag,
            string reservationId = null)
        {
            diag = diag ?? diagnostics;
            BoardingRoom room = roomInventory.FindRoom(roomId);
            if (room == null)
                return $"BoardingHouseShopRuntime.CheckInBoarder: no room '{roomId}'.";

            int weeklyRate;
            int monthlyRate;
            int nightlyRate;
            string effectiveReservationId = string.Empty;

            if (!string.IsNullOrWhiteSpace(reservationId))
            {
                BoardingBedReservation reservation = reservations.Find(reservationId);
                if (reservation == null)
                    return $"BoardingHouseShopRuntime.CheckInBoarder: no reservation '{reservationId}'.";
                if (!reservation.CoversDay(startDayIndex))
                    return $"BoardingHouseShopRuntime.CheckInBoarder: reservation '{reservationId}' does not cover day {startDayIndex}.";
                if (reservation.RoomType != room.RoomType)
                    return $"BoardingHouseShopRuntime.CheckInBoarder: reservation '{reservationId}' holds {reservation.RoomType} beds, not {room.RoomType}.";
                if (CountReservationOccupied(reservationId) >= Math.Max(0, reservation.BedCount))
                    return $"BoardingHouseShopRuntime.CheckInBoarder: reservation '{reservationId}' is full ({reservation.BedCount} bed(s) held).";
                weeklyRate = reservation.ReservedWeeklyRateCents;
                monthlyRate = reservation.ReservedMonthlyRateCents;
                nightlyRate = reservation.ReservedNightlyRateCents;
                effectiveReservationId = reservation.ReservationId;
            }
            else
            {
                int walkInOpen = WalkInOpenBeds(room.RoomType, startDayIndex);
                if (walkInOpen <= 0)
                    return $"BoardingHouseShopRuntime.CheckInBoarder: no walk-in {room.RoomType} bed free on day {startDayIndex} — " +
                        "open beds are held under employer reservations (Canon §8.1D).";
                weeklyRate = rateSchedule.WeeklyRateCents(room.RoomType, boardIncluded);
                monthlyRate = rateSchedule.MonthlyRateCents(room.RoomType, boardIncluded);
                nightlyRate = rateSchedule.TransientNightlyRateCents(boardIncluded);
            }

            return boarderRegister.CheckIn(personId, roomId, bedIndex, stayKind, boardIncluded,
                weeklyRate, nightlyRate, startDayIndex, transientNights, roomInventory, diag,
                monthlyRateCents: monthlyRate, reservationId: effectiveReservationId);
        }

        /// <summary>D1F: walk-in beds free of one room type on a day — open beds minus reserved-but-unconsumed holds.</summary>
        public int WalkInOpenBeds(BoardingRoomType roomType, int dayIndex)
        {
            int open = roomInventory.OpenBeds(roomType);
            int reserved = reservations.ReservedBeds(roomType, dayIndex);
            int reservedOccupied = 0;
            foreach (BoarderRecord record in boarderRegister.Boarders)
            {
                if (record == null || string.IsNullOrWhiteSpace(record.ReservationId)) continue;
                BoardingBedReservation reservation = reservations.Find(record.ReservationId);
                if (reservation == null || reservation.RoomType != roomType) continue;
                if (!reservation.CoversDay(dayIndex)) continue;
                BoardingRoom occupiedRoom = roomInventory.FindRoom(record.RoomId);
                if (occupiedRoom != null && occupiedRoom.RoomType == roomType)
                    reservedOccupied++;
            }
            return Math.Max(0, open - Math.Max(0, reserved - reservedOccupied));
        }

        private int CountReservationOccupied(string reservationId)
        {
            int count = 0;
            foreach (BoarderRecord record in boarderRegister.Boarders)
                if (record != null && string.Equals(record.ReservationId, reservationId, StringComparison.OrdinalIgnoreCase))
                    count++;
            return count;
        }

        /// <summary>D1F: names a chambermaid to the housekeeping roster. Returns the refusal, or null.</summary>
        public string AssignChambermaid(int personId, int cleaningSkillLevel, int dayIndex, List<string> diag, int minutesPerDay = 0)
        {
            return housekeepingStaff.AssignChambermaid(personId, cleaningSkillLevel, dayIndex, diag ?? diagnostics, minutesPerDay);
        }

        /// <summary>D1F: a chamber hand leaves. Returns the refusal, or null.</summary>
        public string ReleaseChambermaid(int personId, int dayIndex, List<string> diag)
        {
            return housekeepingStaff.ReleaseChambermaid(personId, dayIndex, diag ?? diagnostics);
        }

        /// <summary>
        /// D1F: holds beds for an employer/contract customer (Canon §8.1D/§8.1F).
        /// Returns the reservation id, or null plus a loud refusal.
        /// </summary>
        public string AddReservation(string employerBusinessId, string employerName, BoardingRoomType roomType,
            int bedCount, int fromDayIndex, int toDayIndex,
            int reservedWeeklyRateCents, int reservedMonthlyRateCents, int reservedNightlyRateCents,
            List<string> diag)
        {
            return reservations.Reserve(employerBusinessId, employerName, roomType, bedCount,
                fromDayIndex, toDayIndex, reservedWeeklyRateCents, reservedMonthlyRateCents,
                reservedNightlyRateCents, roomInventory, diag ?? diagnostics);
        }

        /// <summary>D1F: releases an employer's hold early. Returns the refusal, or null.</summary>
        public string ReleaseReservation(string reservationId, int dayIndex, List<string> diag)
        {
            return reservations.ReleaseReservation(reservationId, dayIndex, diag ?? diagnostics);
        }

        /// <summary>
        /// D1F: records a conduct incident fact against a real person (Canon
        /// §8.1G). When the person's strikes reach the proprietor's ejection
        /// threshold, the boarder is checked out (ejected) loudly. Incidents
        /// stay on the books as the fact input for the later reputation pass.
        /// </summary>
        public void RecordConductIncident(int personId, BoardingConductIncidentKind kind, int dayIndex, string note, List<string> diag)
        {
            diag = diag ?? diagnostics;
            string incidentId = conductLog.RecordIncident(personId, kind, dayIndex, note, diag);
            if (incidentId == null) return;
            if (conductLog.ShouldEject(personId, conductPolicy))
            {
                int strikes = conductLog.StrikesFor(personId);
                diag.Add($"BoardingHouseShopRuntime: person {personId} EJECTED after {strikes} strike(s) " +
                    $"(proprietor policy: eject after {Math.Max(1, conductPolicy.EjectAfterStrikes)}).");
                string refusal = CheckOutBoarder(personId, diag);
                if (refusal != null)
                    diag.Add($"BoardingHouseShopRuntime: ejection checkout noted — {refusal}");
            }
        }

        /// <summary>D1F: records a standing pantry supply agreement. Returns the order id, or null plus a loud refusal.</summary>
        public string AddStandingOrder(string foodName, string supplierBusinessId, string supplierKind,
            string supplierAccountId, int unitsPerDelivery, int cadenceDays, int nextDueDayIndex,
            int pricePerUnitCents, List<string> diag)
        {
            return standingOrders.AddStandingOrder(foodName, supplierBusinessId, supplierKind, supplierAccountId,
                unitsPerDelivery, cadenceDays, nextDueDayIndex, pricePerUnitCents, diag ?? diagnostics);
        }

        /// <summary>D1F: cancels a standing order (history kept). Returns the refusal, or null.</summary>
        public string CancelStandingOrder(string orderId, int dayIndex, List<string> diag)
        {
            return standingOrders.CancelStandingOrder(orderId, dayIndex, diag ?? diagnostics);
        }

        /// <summary>
        /// D1F: standing orders due today. DUE IS NOT FULFILLED — the caller
        /// places the real order and receives real lots with provenance.
        /// </summary>
        public List<BoardingHouseStandingOrder> EvaluateDueStandingOrders(int dayIndex, List<string> diag)
        {
            return standingOrders.EvaluateDueOrders(dayIndex, diag ?? diagnostics);
        }

        /// <summary>D1F: pantry threshold signals — facts, never orders.</summary>
        public List<BoardingHouseResupplySignal> EvaluateResupplySignals(int dayIndex, List<string> diag)
        {
            return standingOrders.EvaluateResupplySignals(kitchen.FoodStock, dayIndex, diag ?? diagnostics);
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
        /// board-included boarders, transient nights settle, chamber work
        /// turns vacated rooms (D1F), and the served meal ledger prunes.
        /// Returns tonight's transient rent due.
        /// </summary>
        public List<BoarderRentDue> ExecuteDay(int dayIndex, int kitchenLaborMinutes, List<string> diag)
        {
            return ExecuteDay(dayIndex, kitchenLaborMinutes, 0, diag);
        }

        /// <summary>
        /// D1F: runs one day with an explicit housekeeping labor budget
        /// (proprietor/family pitching in beyond the chamber roster).
        /// </summary>
        public List<BoarderRentDue> ExecuteDay(int dayIndex, int kitchenLaborMinutes, int housekeepingLaborMinutes, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (dayIndex < 0)
            {
                diag.Add("BoardingHouseShopRuntime: refused — a house day needs a real day index.");
                return new List<BoarderRentDue>();
            }

            int fullyServed = kitchen.ExecuteDay(boarderRegister, dayIndex, kitchenLaborMinutes, diag);
            List<BoarderRentDue> transientDue = boarderRegister.SettleTransientNight(dayIndex, roomInventory, diag);
            int roomsTurned = RunTurnoverCleaning(dayIndex, housekeepingLaborMinutes, diag);
            kitchen.MealLedger.PruneBefore(dayIndex, diag);

            int transientCents = 0;
            foreach (BoarderRentDue due in transientDue) transientCents += Math.Max(0, due.CentsDue);
            diag.Add($"BoardingHouseShopRuntime: day {dayIndex} — {boarderRegister.BoarderCount} boarder(s) lodged, " +
                $"{fullyServed} fully board-served, {roomsTurned} room(s) turned, transient rent due {transientCents}¢.");
            return transientDue;
        }

        /// <summary>
        /// D1F: chamber work turns vacated rooms (Canon §8.1E). The labor
        /// budget is the on-duty chamber roster's minutes plus any explicit
        /// housekeeping minutes; with no roster ever named, the proprietor
        /// and family do the work (Canon §8.1E small-house fallback). A
        /// roster whose hands have ALL left turns nothing — loudly (the
        /// housekeeper-loss case). Returns rooms turned.
        /// </summary>
        public int RunTurnoverCleaning(int dayIndex, int housekeepingLaborMinutes, List<string> diag)
        {
            diag = diag ?? diagnostics;
            int budget = Math.Max(0, housekeepingLaborMinutes);
            if (!housekeepingStaff.HasRoster)
            {
                budget += BoardingHouseTurnoverPolicy.ProprietorTurnoverMinutesPerDay;
            }
            else if (!housekeepingStaff.HasActiveStaff(dayIndex))
            {
                diag.Add("BoardingHouseShopRuntime: no chamber staff on duty — vacated rooms wait unturned (Canon §8.1E: the housekeeper is gone).");
            }
            else
            {
                int rosterMinutes = housekeepingStaff.TotalMinutesToday(dayIndex);
                budget += rosterMinutes;
                int leadingSkill = housekeepingStaff.LeadingStaffSkill(dayIndex);
                diag.Add($"BoardingHouseShopRuntime: {rosterMinutes} chamber-minute(s) on duty today (leading skill {leadingSkill}).");
            }

            int roomsTurned = 0;
            int leadingSkillLevel = housekeepingStaff.HasActiveStaff(dayIndex)
                ? housekeepingStaff.LeadingStaffSkill(dayIndex)
                : 0;
            foreach (BoardingRoom room in roomInventory.RoomsNeedingTurnover())
            {
                if (room == null) continue;
                int minutesNeeded = housekeepingStaff.HasRoster
                    ? BoardingHousekeepingStaff.EffectiveTurnoverMinutes(
                        BoardingHouseTurnoverPolicy.MinutesPerRoomTurnover, leadingSkillLevel)
                    : BoardingHouseTurnoverPolicy.MinutesPerRoomTurnover;
                if (budget < minutesNeeded)
                {
                    diag.Add($"BoardingHouseShopRuntime: chamber labor short — '{room.RoomId}' waits unturned ({room.SoiledBedCount} bed(s) soiled).");
                    break;
                }
                budget -= minutesNeeded;
                roomInventory.ClearRoomTurnover(room.RoomId, diag);
                roomsTurned++;
            }

            if (roomsTurned > 0 || roomInventory.SoiledBedCount() > 0)
                diag.Add($"BoardingHouseShopRuntime: turnover day {dayIndex} — {roomsTurned} room(s) turned, {roomInventory.SoiledBedCount()} bed(s) still awaiting turnover.");
            return roomsTurned;
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

        /// <summary>
        /// D1F: runs monthly settlement (Canon §8.1D longer-stay terms):
        /// monthly agreements' rent due for one monthly cycle. The caller
        /// settles these through ledger authorities. NOTE (design fork
        /// boundary): nothing here extends credit or runs a tab — every due
        /// list is settled by the caller through ledger authorities (D1E
        /// precedent: regulars-buying-on-credit is Kennedy's recorded fork).
        /// </summary>
        public List<BoarderRentDue> ExecuteMonth(int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            List<BoarderRentDue> monthlyDue = boarderRegister.RentDueMonthly(dayIndex, diag);
            int cents = 0;
            foreach (BoarderRentDue due in monthlyDue) cents += Math.Max(0, due.CentsDue);
            diag.Add($"BoardingHouseShopRuntime: month ending day {dayIndex} — {monthlyDue.Count} monthly boarder(s) owe {cents}¢ total.");
            return monthlyDue;
        }

        /// <summary>Occupancy headline for readouts: occupied/total beds plus board-included boarders and turnover backlog.</summary>
        public string BuildOccupancyLine()
        {
            int total = roomInventory.TotalBeds();
            int occupied = roomInventory.OccupiedBeds();
            int boardIncluded = boarderRegister.BoarderPersonIdsWithBoardIncluded().Count;
            int soiled = roomInventory.SoiledBedCount();
            return $"Boarding House beds: {occupied}/{total} occupied | {Math.Max(0, total - occupied)} open | {boardIncluded} on board | {soiled} bed(s) awaiting turnover";
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
            public BoardingHousekeepingStaff.BoardingHousekeepingStaffSaveDto HousekeepingStaff = new BoardingHousekeepingStaff.BoardingHousekeepingStaffSaveDto();
            public BoardingHouseReservations.BoardingHouseReservationsSaveDto Reservations = new BoardingHouseReservations.BoardingHouseReservationsSaveDto();
            public BoardingHouseConductLog.BoardingHouseConductLogSaveDto ConductLog = new BoardingHouseConductLog.BoardingHouseConductLogSaveDto();
            public BoardingHouseConductPolicy ConductPolicy = new BoardingHouseConductPolicy();
            public BoardingHouseFuelStock.BoardingHouseFuelStockSaveDto FuelStock = new BoardingHouseFuelStock.BoardingHouseFuelStockSaveDto();
            public BoardingHouseStandingOrders.BoardingHouseStandingOrdersSaveDto StandingOrders = new BoardingHouseStandingOrders.BoardingHouseStandingOrdersSaveDto();
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
                HousekeepingStaff = housekeepingStaff.CaptureSaveDto(),
                Reservations = reservations.CaptureSaveDto(),
                ConductLog = conductLog.CaptureSaveDto(),
                ConductPolicy = conductPolicy,
                FuelStock = fuelStock.CaptureSaveDto(),
                StandingOrders = standingOrders.CaptureSaveDto(),
            };
        }

        public void LoadFromSaveDto(BoardingHouseShopRuntimeSaveDto dto)
        {
            if (dto == null) return;
            roomInventory.LoadFromSaveDto(dto.RoomInventory);
            rateSchedule.LoadFromSaveDto(dto.RateSchedule);
            kitchen.LoadFromSaveDto(dto.Kitchen);
            boarderRegister.LoadFromSaveDto(dto.BoarderRegister);
            housekeepingStaff.LoadFromSaveDto(dto.HousekeepingStaff);
            reservations.LoadFromSaveDto(dto.Reservations);
            conductLog.LoadFromSaveDto(dto.ConductLog);
            if (dto.ConductPolicy != null)
                conductPolicy.EjectAfterStrikes = Math.Max(1, dto.ConductPolicy.EjectAfterStrikes);
            fuelStock.LoadFromSaveDto(dto.FuelStock);
            standingOrders.LoadFromSaveDto(dto.StandingOrders);

            // Post-load integrity: every boarder's bed must actually be
            // theirs in the inventory — a corrupted pair is dropped loudly,
            // never silently absorbed. Dropped beds are NOT re-soiled here:
            // the assignment never survived, so no guest slept in them.
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
                boarderRegister.CheckOut(personId, roomInventory, diagnostics, soilBed: false);
                diagnostics.Add($"BoardingHouseShopRuntime: boarder {personId} dropped on load — bed assignment did not survive the round trip.");
            }
        }
        #endregion
    }
}
