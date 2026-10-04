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
        /// Checks a guest in. Nightly rates and weekly rates are locked
        /// from the schedule's current values; nightly stays take
        /// nights-paid up front. Returns the refusal, or null.
        /// </summary>
        public string CheckInGuest(int personId, string roomNumber, int bedIndex, HotelRoomClass roomClass,
            HotelStayKind stayKind, int startDayIndex, int nightsPaid, List<string> diag)
        {
            diag = diag ?? diagnostics;
            HotelRoom room = roomInventory.FindRoom(roomNumber);
            if (room == null)
                return $"HotelShopRuntime.CheckInGuest: no room '{roomNumber}'.";
            int nightlyRate = rateSchedule.NightlyRateCents(room.RoomClass);
            int weeklyRate = rateSchedule.WeeklyRateCents(room.RoomClass);
            return guestRegister.CheckIn(personId, roomNumber, bedIndex, room.RoomClass, stayKind,
                nightlyRate, weeklyRate, startDayIndex, nightsPaid, roomInventory, diag);
        }

        /// <summary>
        /// Checks a guest out, vacating their bed and releasing their livery
        /// stable booking. Returns the refusal, or null.
        /// </summary>
        public string CheckOutGuest(int personId, List<string> diag)
        {
            diag = diag ?? diagnostics;
            string refusal = guestRegister.CheckOut(personId, roomInventory, diag);
            if (liveryStable != null && refusal == null)
                liveryStable.ReleaseBookings(personId, diag);
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
        /// minutes supplied. Returns tonight's room-night sales.
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
            }

            int washed = housekeeping.Launder(dayIndex, Math.Max(0, housekeepingLaborMinutes),
                out int laborConsumed, diag);

            int fullyServed = kitchen.ExecuteDay(guestRegister, dayIndex, Math.Max(0, kitchenLaborMinutes), diag);

            int nightlyCents = 0;
            foreach (HotelRoomNightSale sale in sales)
                if (sale != null) nightlyCents += Math.Max(0, sale.CentsCharged);
            diag.Add($"HotelShopRuntime: day {dayIndex} — {sales.Count} room-night sale(s), {nightlyCents}¢ room revenue, " +
                $"{linenFailures} linen shortfall(s), {washed} set(s) laundered ({laborConsumed} labor minute(s)), " +
                $"{fullyServed} guest(s) fully served in the dining room.");
            return sales;
        }

        /// <summary>
        /// Runs weekly settlement: weekly guests' rent due for one week.
        /// The caller settles these through ledger authorities.
        /// </summary>
        public List<HotelRoomRentDue> ExecuteWeek(int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            List<HotelRoomRentDue> weeklyDue = guestRegister.RentDueWeekly(dayIndex, diag);
            int cents = 0;
            foreach (HotelRoomRentDue due in weeklyDue) cents += Math.Max(0, due.CentsDue);
            diag.Add($"HotelShopRuntime: week ending day {dayIndex} — {weeklyDue.Count} weekly guest(s) owe {cents}¢ total.");
            return weeklyDue;
        }

        /// <summary>Occupancy headline for readouts: occupied/total beds plus open beds by class.</summary>
        public string BuildOccupancyLine()
        {
            int total = roomInventory.TotalBeds();
            int occupied = roomInventory.OccupiedBeds();
            int singles = roomInventory.OpenBeds(HotelRoomClass.SingleRoom);
            int doubles = roomInventory.OpenBeds(HotelRoomClass.DoubleRoom);
            int suites = roomInventory.OpenBeds(HotelRoomClass.ParlorSuite);
            return $"Hotel beds: {occupied}/{total} occupied | {Math.Max(0, total - occupied)} open " +
                $"(single {singles}, double {doubles}, suite {suites})";
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
                guestRegister.CheckOut(personId, roomInventory, diagnostics);
                diagnostics.Add($"HotelShopRuntime: guest {personId} dropped on load — bed assignment did not survive the round trip.");
            }
        }
        #endregion
    }
}
