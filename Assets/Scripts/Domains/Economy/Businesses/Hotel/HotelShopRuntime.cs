using System;
using System.Collections.Generic;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Businesses.Hotel
{
    /// <summary>
    /// W3A: the per-instance hotel runtime — room nights as sold
    /// inventory. One instance per Hotel business instance
    /// (BusinessType.Hotel = 23).
    ///
    /// What this owns (the detailed layer):
    /// - rooms as rentable inventory by class (single / double / parlor
    ///   suite — real rooms, real beds, never percentages);
    /// - the nightly/weekly rate schedule as proprietor policy data
    ///   (Canon §8.1D);
    /// - the guest register: nightly and weekly agreements, rate locked
    ///   at check-in, room-night sale records per occupied bed-night;
    /// - housekeeping: bed-linen turnover with lot provenance and the
    ///   in-house laundry loop (Canon §8.1E);
    /// - the nightly-state register: every guest resolves to a hotel
    ///   room in a named room — the W2C hook, now live;
    /// - W3B: traveler demand (arrivals routed to real beds — full hotels
    ///   refuse loudly, never invent rooms), the livery stable link
    ///   (guests' horses/teams booked into real stalls), and the dining
    ///   room (room-and-board meals from real pantry lots with provenance,
    ///   following the W2C kitchen pattern with hotel-specific policy).
    /// - D2A: service staff (desk/night clerks, porters, chambermaids,
    ///   cooks, waiters — Canon §8.1E), contract/commercial room
    ///   reservations (Canon §8.1D), guest folios with credit tolerance
    ///   (Canon §8.1D), monthly terms (Canon §8.1D longer-stay), heating
    ///   fuel with upstream provenance (Canon §8.1A/§8.1E), standing
    ///   supply agreements (Canon §8.1B), and an experience-based
    ///   reputation log (Canon §8.1G).
    ///
    /// Money moves only through ledger authorities, never here: nightly
    /// sales, weekly rent-due, and the locked room charges on traveler
    /// demand results are settled by the caller. A hotel guest sleeps
    /// locally without becoming a resident (Tech §2.10). W3A's room rates
    /// remain room-only policy; board is the W3B kitchen's own package.
    /// </summary>
    public sealed class HotelShopRuntime
    {
        private readonly List<string> diagnostics = new List<string>();
        private readonly string businessInstanceId;
        private readonly HotelRoomInventory roomInventory = new HotelRoomInventory();
        private readonly HotelRateSchedule rateSchedule = new HotelRateSchedule();
        private readonly HotelGuestRegister guestRegister = new HotelGuestRegister();
        private readonly HotelHousekeeping housekeeping;
        private readonly HotelKitchen kitchen;
        private readonly EntityIdRegistry idRegistry;
        private readonly HotelStaff staff = new HotelStaff();
        private readonly HotelRoomReservations roomReservations = new HotelRoomReservations();
        private readonly HotelGuestFolios guestFolios = new HotelGuestFolios();
        private readonly HotelReputationLog reputationLog = new HotelReputationLog();
        private readonly HotelFuelStock fuelStock = new HotelFuelStock();
        private readonly HotelStandingOrders standingOrders = new HotelStandingOrders();
        /// <summary>
        /// W3B: the livery-side stable booking authority this hotel links
        /// to. Owned by the caller (the future Livery business), not by the
        /// hotel — the hotel only holds the link.
        /// </summary>
        private LiveryStableBookings liveryStable;

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public string BusinessInstanceId => businessInstanceId;
        public HotelRoomInventory RoomInventory => roomInventory;
        public HotelRateSchedule RateSchedule => rateSchedule;
        public HotelGuestRegister GuestRegister => guestRegister;
        public HotelHousekeeping Housekeeping => housekeeping;
        public HotelKitchen Kitchen => kitchen;
        /// <summary>D2A: the hotel's service-staff roster (clerks, porters, chambermaids, cooks, waiters).</summary>
        public HotelStaff Staff => staff;
        /// <summary>D2A: the reservation book for contract/commercial bed holds.</summary>
        public HotelRoomReservations RoomReservations => roomReservations;
        /// <summary>D2A: account guests' running folios (credit tolerance).</summary>
        public HotelGuestFolios GuestFolios => guestFolios;
        /// <summary>D2A: the experience-based reputation record (Canon §8.1G).</summary>
        public HotelReputationLog Reputation => reputationLog;
        /// <summary>D2A: the heating-fuel wood store (cordwood, provenance-tracked).</summary>
        public HotelFuelStock FuelStock => fuelStock;
        /// <summary>D2A: standing supply agreements for the dining-room pantry.</summary>
        public HotelStandingOrders StandingOrders => standingOrders;

        public HotelShopRuntime(string businessInstanceId, EntityIdRegistry idRegistry)
        {
            this.businessInstanceId = businessInstanceId ?? string.Empty;
            this.idRegistry = idRegistry;
            this.housekeeping = new HotelHousekeeping(idRegistry);
            this.kitchen = new HotelKitchen(idRegistry);
        }

        /// <summary>One-time opening linen/soap endowment — explicit, flagged, never auto-replenished.</summary>
        public void ApplyOpeningLinenEndowment(int dayIndex, List<string> diag)
        {
            housekeeping.ApplyOpeningLinenEndowment(dayIndex, diag ?? diagnostics);
        }

        /// <summary>Registers the hotel's importables (bed linen, soap) — EQU-1 declared trade link.</summary>
        public void EnsureImportables(List<string> diag)
        {
            HotelConsumableSupply.EnsureHotelImportables(diag ?? diagnostics);
        }

        /// <summary>W3B: registers the dining room's food ingredients as importables — EQU-1 declared trade link.</summary>
        public void EnsureFoodImportables(List<string> diag)
        {
            HotelFoodSupply.EnsureHotelFoodImportables(diag ?? diagnostics);
        }

        /// <summary>W3B: one-time opening pantry endowment — explicit, flagged, never auto-replenished.</summary>
        public void ApplyOpeningPantryEndowment(int dayIndex, List<string> diag)
        {
            HotelFoodBootstrap.ApplyBootstrapEndowment(kitchen.FoodStock, idRegistry, dayIndex, diag ?? diagnostics);
        }

        /// <summary>D2A: one-time opening heating-fuel endowment — explicit, flagged, never auto-replenished.</summary>
        public void ApplyOpeningFuelEndowment(int dayIndex, List<string> diag)
        {
            HotelFuelBootstrap.ApplyBootstrapEndowment(fuelStock, idRegistry, dayIndex, diag ?? diagnostics);
        }

        /// <summary>D2A: marks a room as proprietor-household space (Canon §8.1A). Returns the refusal, or null.</summary>
        public string SetRoomProprietorUse(string roomNumber, bool proprietorOccupied, List<string> diag)
        {
            return roomInventory.SetProprietorUse(roomNumber, proprietorOccupied, diag ?? diagnostics);
        }

        /// <summary>
        /// D2A: records one experienced reputation event (theft/security,
        /// guest complaints, bad service the runtime did not see). The
        /// reputation log is event-sourced; this is the caller's hook for
        /// events outside the daily cycle.
        /// </summary>
        public void RecordReputationEvent(int dayIndex, HotelExperienceKind kind, string note, List<string> diag)
        {
            reputationLog.RecordExperience(dayIndex, kind, note, diag ?? diagnostics);
        }

        /// <summary>
        /// W3B: attaches the livery-side stable booking authority this hotel
        /// links guests' teams to. The stable is NOT hotel-owned — the
        /// caller owns it (the future Livery business's authority); the
        /// hotel only holds the link so checkout releases the stalls.
        /// </summary>
        public void AttachLiveryStable(LiveryStableBookings stableBookings, List<string> diag)
        {
            diag = diag ?? diagnostics;
            liveryStable = stableBookings;
            diag.Add(liveryStable != null
                ? "HotelShopRuntime: livery stable linked — guests' horses/teams book real stalls."
                : "HotelShopRuntime: livery stable detached — teams must stable elsewhere.");
        }

        /// <summary>
        /// W3B: books livery stalls for one guest's animals. Returns the
        /// refusal (loud — the team must stable elsewhere), or null. A
        /// guest with no animals never calls this.
        /// </summary>
        public string BookLiveryStallsForGuest(int personId, List<EntityId> animalIds, int nights, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (liveryStable == null)
                return "HotelShopRuntime: no livery stable linked — the traveler's team cannot be stabled through this hotel.";
            HotelGuestRecord record = guestRegister.FindRecord(personId);
            if (record == null)
                return $"HotelShopRuntime: person {personId} holds no hotel agreement — stalls book only for real guests.";
            return liveryStable.BookStalls(personId, animalIds, record.StartDayIndex,
                Math.Max(1, nights), diag);
        }

        /// <summary>Adds a physical room. Returns the refusal, or null.</summary>
        public string AddRoom(HotelRoomClass roomClass, int bedCount, List<string> diag)
        {
            return roomInventory.AddRoom(roomClass, bedCount, diag ?? diagnostics);
        }

        /// <summary>Proprietor policy: replaces the rate schedule.</summary>
        public void SetRateSchedule(HotelRateSchedule schedule, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (schedule == null)
            {
                diag.Add("HotelShopRuntime: no rate schedule offered — the current schedule stands.");
                return;
            }
            rateSchedule.LoadFromSaveDto(schedule.CaptureSaveDto());
            diag.Add("HotelShopRuntime: rate schedule updated (nightly and weekly policy as data).");
        }

        /// <summary>
        /// Checks a guest in. Rates are locked from the schedule's current
        /// values (nightly and weekly, plus D2A monthly); nightly stays
        /// take nights-paid up front. D2A: when a reservation id is
        /// supplied, the guest checks in UNDER the contract/commercial hold
        /// and locks the reservation's rates instead of the walk-in
        /// schedule (Canon §8.1D). D2A: a positive folio tolerance opens
        /// an account folio for the guest (Canon §8.1D credit tolerance);
        /// zero tolerance means cash terms. Returns the refusal, or null.
        /// </summary>
        public string CheckInGuest(int personId, string roomNumber, int bedIndex, HotelRoomClass roomClass,
            HotelStayKind stayKind, int startDayIndex, int nightsPaid, List<string> diag,
            int monthlyRateCents = 0, string reservationId = null, int folioToleranceCents = 0)
        {
            diag = diag ?? diagnostics;
            HotelRoom room = roomInventory.FindRoom(roomNumber);
            if (room == null)
                return $"HotelShopRuntime.CheckInGuest: no room '{roomNumber}'.";

            string heldReservationId = null;
            int nightlyRate = rateSchedule.NightlyRateCents(room.RoomClass);
            int weeklyRate = rateSchedule.WeeklyRateCents(room.RoomClass);
            int monthlyRate = stayKind == HotelStayKind.Monthly
                ? (monthlyRateCents > 0 ? monthlyRateCents : rateSchedule.MonthlyRateCents(room.RoomClass))
                : Math.Max(0, monthlyRateCents);

            if (!string.IsNullOrWhiteSpace(reservationId))
            {
                string consumeRefusal = roomReservations.ConsumeBed(reservationId, startDayIndex,
                    out int resNightly, out int resWeekly, out int resMonthly, diag);
                if (consumeRefusal != null) return consumeRefusal;
                nightlyRate = resNightly;
                weeklyRate = resWeekly;
                monthlyRate = resMonthly;
                heldReservationId = reservationId;
            }

            string refusal = guestRegister.CheckIn(personId, roomNumber, bedIndex, room.RoomClass, stayKind,
                nightlyRate, weeklyRate, startDayIndex, nightsPaid, roomInventory, diag,
                monthlyRate, heldReservationId);
            if (refusal != null)
            {
                if (!string.IsNullOrWhiteSpace(heldReservationId))
                    roomReservations.ReleaseConsumedBed(heldReservationId, diag);
                return refusal;
            }

            if (!string.IsNullOrWhiteSpace(heldReservationId))
                reputationLog.RecordExperience(startDayIndex, HotelExperienceKind.HonoredTerms,
                    $"reservation '{heldReservationId}' honored — person {personId} checked in at the contracted rate.", diag);

            if (folioToleranceCents > 0)
            {
                string folioRefusal = guestFolios.OpenFolio(personId, folioToleranceCents, diag);
                if (folioRefusal != null) diag.Add(folioRefusal);
            }

            return null;
        }

        /// <summary>
        /// Checks a guest out, vacating their bed and releasing their livery
        /// stable booking. D2A: releases any reservation hold the guest
        /// consumed, and closes their folio — a balance over tolerance is
        /// recorded as debt, loudly. Returns the refusal, or null.
        /// </summary>
        public string CheckOutGuest(int personId, List<string> diag, int dayIndex = 0)
        {
            diag = diag ?? diagnostics;
            HotelGuestRecord record = guestRegister.FindRecord(personId);
            string refusal = guestRegister.CheckOut(personId, roomInventory, diag);
            if (liveryStable != null && refusal == null)
                liveryStable.ReleaseBookings(personId, diag);
            if (record != null && !string.IsNullOrWhiteSpace(record.ReservationId))
                roomReservations.ReleaseConsumedBed(record.ReservationId, diag);
            if (refusal == null)
                guestFolios.CloseFolio(personId, Math.Max(0, dayIndex), diag);
            return refusal;
        }

        /// <summary>
        /// W3A: the nightly-state answer for one guest — the W2C hook made
        /// real. A guest resolves to a hotel room in a named room; no
        /// other nightly state is ever produced here.
        /// </summary>
        public bool TryGetNightlyPlacement(int personId, out HotelNightlyPlacement placement)
        {
            return guestRegister.TryGetNightlyPlacement(personId, businessInstanceId, out placement);
        }

        /// <summary>
        /// Runs one day: settles tonight's room nights, turns over linen
        /// for every occupied bed-night (provenance recorded on the sale),
        /// launders dirty linen with the housekeeping labor minutes
        /// supplied, and runs the dining room (W3B) with the kitchen labor
        /// minutes supplied. D2A additions: nightly charges post to open
        /// guest folios; room stoves burn heating fuel (shortfalls logged
        /// loudly and recorded as bad-service experience); reputation
        /// events post from the night's real experiences; standing-order
        /// due lists report as data. Returns tonight's room-night sales.
        /// </summary>
        public List<HotelRoomNightSale> ExecuteDay(int dayIndex, int housekeepingLaborMinutes, List<string> diag, int kitchenLaborMinutes = 0)
        {
            diag = diag ?? diagnostics;
            if (dayIndex < 0)
            {
                diag.Add("HotelShopRuntime: refused — a hotel day needs a real day index.");
                return new List<HotelRoomNightSale>();
            }

            List<HotelRoomNightSale> sales = guestRegister.SettleNight(dayIndex, roomInventory, diag);

            int linenFailures = 0;
            int turnedOver = 0;
            foreach (HotelRoomNightSale sale in sales)
            {
                if (sale == null) continue;
                List<HotelConsumableDispenseLine> lines = housekeeping.TurnOverForGuestNight(dayIndex, diag);
                if (lines == null)
                {
                    sale.LinenSetsUsed = 0;
                    linenFailures++;
                    continue;
                }
                int sets = 0;
                foreach (HotelConsumableDispenseLine line in lines)
                {
                    sets += Math.Max(0, line.UnitsTaken);
                    sale.LinenProvenanceChains.Add(line.ProvenanceChain ?? string.Empty);
                }
                sale.LinenSetsUsed = sets;
                if (sets > 0) turnedOver++;

                // D2A: account guests' nightly charges post to their folios
                // (cash guests settle at the desk; their sales lines carry
                // the charge already). Money still moves only through ledger
                // authorities — the folio is the account record.
                if (sale.CentsCharged > 0 && guestFolios.FindFolio(sale.PersonId) != null)
                    guestFolios.PostCharge(sale.PersonId, dayIndex, HotelFolioLineKind.RoomNightCharge,
                        $"room night, {sale.RoomClass} '{sale.RoomNumber}' bed {sale.BedIndex}", sale.CentsCharged, diag);
            }

            int washed = housekeeping.Launder(dayIndex, Math.Max(0, housekeepingLaborMinutes),
                out int laborConsumed, diag);

            int fullyServed = kitchen.ExecuteDay(guestRegister, dayIndex, Math.Max(0, kitchenLaborMinutes), diag);

            // D2A: the heating fire. One cordwood unit per occupied
            // bed-night (TUNING). A cold house is a real bad-service
            // experience (Canon §8.1G) — logged loudly, never silent.
            bool houseCold = false;
            if (sales.Count > 0)
            {
                int heatUnits = sales.Count * HotelFuelSupply.FuelUnitsPerBedNight;
                List<HotelFuelDispenseLine> heatLines = fuelStock.TryBurnUnits(
                    HotelFuelSupply.FuelMaterialId, heatUnits, dayIndex, diag);
                if (heatLines == null)
                {
                    houseCold = true;
                    diag.Add($"HotelShopRuntime: the stoves went unlit (day {dayIndex}) — {sales.Count} guest(s) slept in a cold house. Fuel must be bought, not invented.");
                    reputationLog.RecordExperience(dayIndex, HotelExperienceKind.BadService,
                        $"cold rooms — no heating fuel for {sales.Count} occupied bed-night(s)", diag);
                }
            }

            // D2A: reputation from the night's real experiences (Canon §8.1G
            // — reputation emerges from experience, never a flat upgrade).
            if (turnedOver > 0)
                reputationLog.RecordExperience(dayIndex, HotelExperienceKind.CleanRooms,
                    $"{turnedOver} room(s) turned over with clean linen", diag);
            if (linenFailures > 0)
                reputationLog.RecordExperience(dayIndex, HotelExperienceKind.GuestComplaint,
                    $"{linenFailures} linen shortfall(s) — guests turned down on fresh linen", diag);
            if (fullyServed > 0)
                reputationLog.RecordExperience(dayIndex, HotelExperienceKind.ReliableMeals,
                    $"{fullyServed} guest(s) fully served in the dining room", diag);

            // D2A: standing-order due list — data for the caller, never an auto-order.
            List<HotelStandingOrder> dueOrders = standingOrders.OrdersDue(dayIndex);
            if (dueOrders.Count > 0)
                diag.Add($"HotelShopRuntime: {dueOrders.Count} standing pantry order(s) due (day {dayIndex}) — the caller places real orders.");

            int nightlyCents = 0;
            foreach (HotelRoomNightSale sale in sales)
                if (sale != null) nightlyCents += Math.Max(0, sale.CentsCharged);
            diag.Add($"HotelShopRuntime: day {dayIndex} — {sales.Count} room-night sale(s), {nightlyCents}¢ room revenue, " +
                $"{linenFailures} linen shortfall(s), {washed} set(s) laundered ({laborConsumed} labor minute(s)), " +
                $"{fullyServed} guest(s) fully served in the dining room, house cold: {houseCold}.");
            return sales;
        }

        /// <summary>
        /// Runs weekly settlement: weekly guests' rent due for one week.
        /// D2A: the dues also post to open guest folios (account guests);
        /// cash guests settle at the desk. The caller settles these
        /// through ledger authorities.
        /// </summary>
        public List<HotelRoomRentDue> ExecuteWeek(int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            List<HotelRoomRentDue> weeklyDue = guestRegister.RentDueWeekly(dayIndex, diag);
            int cents = 0;
            foreach (HotelRoomRentDue due in weeklyDue)
            {
                if (due == null) continue;
                cents += Math.Max(0, due.CentsDue);
                if (due.CentsDue > 0 && guestFolios.FindFolio(due.PersonId) != null)
                    guestFolios.PostCharge(due.PersonId, dayIndex, HotelFolioLineKind.WeeklyRentCharge,
                        $"weekly room rent, {due.RoomNumber} (7 nights)", due.CentsDue, diag);
            }
            diag.Add($"HotelShopRuntime: week ending day {dayIndex} — {weeklyDue.Count} weekly guest(s) owe {cents}¢ total.");
            return weeklyDue;
        }

        /// <summary>
        /// D2A: runs monthly settlement (the caller drives the 30-day
        /// cycle): monthly guests' rent due for one cycle. Dues post to
        /// open guest folios; the caller settles them through ledger
        /// authorities.
        /// </summary>
        public List<HotelRoomRentDue> ExecuteMonth(int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            List<HotelRoomRentDue> monthlyDue = guestRegister.RentDueMonthly(dayIndex, diag);
            int cents = 0;
            foreach (HotelRoomRentDue due in monthlyDue)
            {
                if (due == null) continue;
                cents += Math.Max(0, due.CentsDue);
                if (due.CentsDue > 0 && guestFolios.FindFolio(due.PersonId) != null)
                    guestFolios.PostCharge(due.PersonId, dayIndex, HotelFolioLineKind.MonthlyRentCharge,
                        $"monthly room rent, {due.RoomNumber} ({HotelBilling.MonthlyBillingDays} nights)", due.CentsDue, diag);
            }
            diag.Add($"HotelShopRuntime: month ending day {dayIndex} — {monthlyDue.Count} monthly guest(s) owe {cents}¢ total.");
            return monthlyDue;
        }

        /// <summary>Occupancy headline for readouts: occupied/total beds plus open beds by class.</summary>
        public string BuildOccupancyLine()
        {
            int total = roomInventory.TotalBeds();
            int occupied = roomInventory.OccupiedBeds();
            int singles = roomInventory.OpenBeds(HotelRoomClass.SingleRoom);
            int doubles = roomInventory.OpenBeds(HotelRoomClass.DoubleRoom);
            int suites = roomInventory.OpenBeds(HotelRoomClass.ParlorSuite);
            int proprietorRooms = 0;
            foreach (HotelRoom room in roomInventory.Rooms)
                if (room != null && room.ProprietorOccupied) proprietorRooms++;
            return $"Hotel beds: {occupied}/{total} occupied | {Math.Max(0, total - occupied)} open " +
                $"(single {singles}, double {doubles}, suite {suites})" +
                (proprietorRooms > 0 ? $" | {proprietorRooms} proprietor-household room(s) (Canon §8.1A)" : string.Empty);
        }

        #region Save / Load
        [Serializable]
        public sealed class HotelShopRuntimeSaveDto
        {
            public string BusinessInstanceId = string.Empty;
            public HotelRoomInventory.HotelRoomInventorySaveDto RoomInventory = new HotelRoomInventory.HotelRoomInventorySaveDto();
            public HotelRateSchedule.HotelRateScheduleSaveDto RateSchedule = new HotelRateSchedule.HotelRateScheduleSaveDto();
            public HotelGuestRegister.HotelGuestRegisterSaveDto GuestRegister = new HotelGuestRegister.HotelGuestRegisterSaveDto();
            public HotelHousekeeping.HotelHousekeepingSaveDto Housekeeping = new HotelHousekeeping.HotelHousekeepingSaveDto();
            public HotelKitchen.HotelKitchenSaveDto Kitchen = new HotelKitchen.HotelKitchenSaveDto();
            // D2A: the new subsystems, each save-safe on its own.
            public HotelStaff.HotelStaffSaveDto Staff = new HotelStaff.HotelStaffSaveDto();
            public HotelRoomReservations.HotelRoomReservationsSaveDto RoomReservations = new HotelRoomReservations.HotelRoomReservationsSaveDto();
            public HotelGuestFolios.HotelGuestFoliosSaveDto GuestFolios = new HotelGuestFolios.HotelGuestFoliosSaveDto();
            public HotelReputationLog.HotelReputationLogSaveDto Reputation = new HotelReputationLog.HotelReputationLogSaveDto();
            public HotelFuelStock.HotelFuelStockSaveDto FuelStock = new HotelFuelStock.HotelFuelStockSaveDto();
            public HotelStandingOrders.HotelStandingOrdersSaveDto StandingOrders = new HotelStandingOrders.HotelStandingOrdersSaveDto();
        }

        public HotelShopRuntimeSaveDto CaptureSaveDto()
        {
            return new HotelShopRuntimeSaveDto
            {
                BusinessInstanceId = businessInstanceId,
                RoomInventory = roomInventory.CaptureSaveDto(),
                RateSchedule = rateSchedule.CaptureSaveDto(),
                GuestRegister = guestRegister.CaptureSaveDto(),
                Housekeeping = housekeeping.CaptureSaveDto(),
                Kitchen = kitchen.CaptureSaveDto(),
                Staff = staff.CaptureSaveDto(),
                RoomReservations = roomReservations.CaptureSaveDto(),
                GuestFolios = guestFolios.CaptureSaveDto(),
                Reputation = reputationLog.CaptureSaveDto(),
                FuelStock = fuelStock.CaptureSaveDto(),
                StandingOrders = standingOrders.CaptureSaveDto(),
            };
        }

        public void LoadFromSaveDto(HotelShopRuntimeSaveDto dto)
        {
            if (dto == null) return;
            roomInventory.LoadFromSaveDto(dto.RoomInventory);
            rateSchedule.LoadFromSaveDto(dto.RateSchedule);
            housekeeping.LoadFromSaveDto(dto.Housekeeping);
            kitchen.LoadFromSaveDto(dto.Kitchen);
            guestRegister.LoadFromSaveDto(dto.GuestRegister);
            staff.LoadFromSaveDto(dto.Staff);
            roomReservations.LoadFromSaveDto(dto.RoomReservations);
            guestFolios.LoadFromSaveDto(dto.GuestFolios);
            reputationLog.LoadFromSaveDto(dto.Reputation);
            fuelStock.LoadFromSaveDto(dto.FuelStock);
            standingOrders.LoadFromSaveDto(dto.StandingOrders);

            // Post-load integrity: every guest's bed must actually be
            // theirs in the inventory — a corrupted pair is dropped loudly,
            // never silently absorbed.
            var orphaned = new List<int>();
            foreach (HotelGuestRecord record in guestRegister.Guests)
            {
                if (record == null) continue;
                HotelRoom room = roomInventory.FindRoom(record.RoomNumber);
                if (room == null || room.OccupantOfBed(record.BedIndex) != record.PersonId)
                    orphaned.Add(record.PersonId);
            }
            foreach (int personId in orphaned)
            {
                HotelGuestRecord record = guestRegister.FindRecord(personId);
                guestRegister.CheckOut(personId, roomInventory, diagnostics);
                if (record != null && !string.IsNullOrWhiteSpace(record.ReservationId))
                    roomReservations.ReleaseConsumedBed(record.ReservationId, diagnostics);
                guestFolios.CloseFolio(personId, 0, diagnostics);
                diagnostics.Add($"HotelShopRuntime: guest {personId} dropped on load — bed assignment did not survive the round trip.");
            }
        }
        #endregion
    }
}
