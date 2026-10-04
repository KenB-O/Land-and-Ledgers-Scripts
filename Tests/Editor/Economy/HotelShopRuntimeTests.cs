using System.Collections.Generic;
using LandLedgers.Economy.Businesses.BoardingHouse;
using LandLedgers.Economy.Businesses.Hotel;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W3A: the per-instance hotel runtime end to end — rooms, rates,
    /// guests, nightly sales with linen provenance, laundry, weekly
    /// settlement, save/load integrity, occupancy readouts.
    /// </summary>
    [TestFixture]
    public sealed class HotelShopRuntimeTests
    {
        private static (HotelShopRuntime, List<string>) NewHotel()
        {
            var diag = new List<string>();
            var hotel = new HotelShopRuntime("hotel-inst-1", new EntityIdRegistry());
            hotel.AddRoom(HotelRoomClass.SingleRoom, 1, diag);
            hotel.AddRoom(HotelRoomClass.DoubleRoom, 2, diag);
            hotel.AddRoom(HotelRoomClass.ParlorSuite, 2, diag);
            hotel.ApplyOpeningLinenEndowment(1, diag);
            return (hotel, diag);
        }

        [Test]
        public void ExecuteDay_SettlesNightlySales_TurnsOverLinen()
        {
            var (hotel, diag) = NewHotel();
            string single = hotel.RoomInventory.Rooms[0].RoomNumber;
            string suite = hotel.RoomInventory.Rooms[2].RoomNumber;
            hotel.CheckInGuest(201, single, 0, HotelRoomClass.SingleRoom, HotelStayKind.Nightly, 5, 2, diag);
            hotel.CheckInGuest(202, suite, 1, HotelRoomClass.ParlorSuite, HotelStayKind.Weekly, 5, 0, diag);

            var sales = hotel.ExecuteDay(5, 60, diag);
            Assert.AreEqual(2, sales.Count);
            Assert.AreEqual(40, sales[0].CentsCharged, "nightly guest pays the locked nightly rate");
            Assert.AreEqual(0, sales[1].CentsCharged, "weekly guest settles weekly");
            Assert.AreEqual(1, sales[0].LinenSetsUsed);
            Assert.AreEqual(1, sales[0].LinenProvenanceChains.Count);
            Assert.IsTrue(sales[0].LinenProvenanceChains[0].Contains("BOOTSTRAP"));
            Assert.AreEqual(1, sales[1].LinenSetsUsed, "weekly guests still turn over linen nightly");

            Assert.IsTrue(hotel.TryGetNightlyPlacement(201, out HotelNightlyPlacement placement));
            Assert.AreEqual(BoardingNightlyState.HotelRoom, placement.NightlyState);
        }

        [Test]
        public void ExecuteDay_LaundersDirtyLinen_WithSuppliedLabor()
        {
            var (hotel, diag) = NewHotel();
            string single = hotel.RoomInventory.Rooms[0].RoomNumber;
            hotel.CheckInGuest(211, single, 0, HotelRoomClass.SingleRoom, HotelStayKind.Nightly, 5, 1, diag);
            hotel.ExecuteDay(5, 60, diag);

            Assert.AreEqual(0, hotel.Housekeeping.DirtySetsCount, "one dirty set washed with 60 minutes");
        }

        [Test]
        public void ExecuteDay_LinenShortfall_RecordsLoudly_NightStillSettles()
        {
            var diag = new List<string>();
            var hotel = new HotelShopRuntime("hotel-inst-2", new EntityIdRegistry());
            hotel.AddRoom(HotelRoomClass.SingleRoom, 1, diag);
            // No endowment: the shelf is empty.
            string single = hotel.RoomInventory.Rooms[0].RoomNumber;
            hotel.CheckInGuest(221, single, 0, HotelRoomClass.SingleRoom, HotelStayKind.Nightly, 5, 1, diag);

            var sales = hotel.ExecuteDay(5, 0, diag);
            Assert.AreEqual(1, sales.Count);
            Assert.AreEqual(40, sales[0].CentsCharged, "revenue is real even when housekeeping fails");
            Assert.AreEqual(0, sales[0].LinenSetsUsed, "linenless night recorded honestly");
            Assert.AreEqual(0, sales[0].LinenProvenanceChains.Count);
        }

        [Test]
        public void ExecuteWeek_ReportsWeeklyRentDue()
        {
            var (hotel, diag) = NewHotel();
            string dbl = hotel.RoomInventory.Rooms[1].RoomNumber;
            hotel.CheckInGuest(231, dbl, 0, HotelRoomClass.DoubleRoom, HotelStayKind.Weekly, 5, 0, diag);

            var due = hotel.ExecuteWeek(11, diag);
            Assert.AreEqual(1, due.Count);
            Assert.AreEqual(330, due[0].CentsDue);
        }

        [Test]
        public void CheckInGuest_UnknownRoom_Refused()
        {
            var (hotel, diag) = NewHotel();
            Assert.IsNotNull(hotel.CheckInGuest(241, "no-such-room", 0, HotelRoomClass.SingleRoom, HotelStayKind.Nightly, 5, 1, diag));
        }

        [Test]
        public void BuildOccupancyLine_CountsBedsByClass()
        {
            var (hotel, diag) = NewHotel();
            string single = hotel.RoomInventory.Rooms[0].RoomNumber;
            hotel.CheckInGuest(251, single, 0, HotelRoomClass.SingleRoom, HotelStayKind.Nightly, 5, 1, diag);
            string line = hotel.BuildOccupancyLine();
            Assert.IsTrue(line.Contains("1/5"), line);
            Assert.IsTrue(line.Contains("single 0"), line);
            Assert.IsTrue(line.Contains("double 2"), line);
            Assert.IsTrue(line.Contains("suite 2"), line);
        }

        [Test]
        public void SaveLoad_RoundTrip_DropsOrphanedGuestLoudly()
        {
            var (hotel, diag) = NewHotel();
            string single = hotel.RoomInventory.Rooms[0].RoomNumber;
            hotel.CheckInGuest(261, single, 0, HotelRoomClass.SingleRoom, HotelStayKind.Nightly, 5, 2, diag);

            var dto = hotel.CaptureSaveDto();
            // Corrupt the save: erase the bed assignment but keep the guest record.
            dto.RoomInventory.Rooms[0].OccupiedBeds.Clear();

            var reloaded = new HotelShopRuntime("hotel-inst-1", new EntityIdRegistry());
            reloaded.LoadFromSaveDto(dto);
            Assert.AreEqual(0, reloaded.GuestRegister.GuestCount, "orphaned guest dropped on load");
            Assert.IsFalse(reloaded.TryGetNightlyPlacement(261, out _));

            // A clean round trip keeps everyone.
            var clean = new HotelShopRuntime("hotel-inst-1", new EntityIdRegistry());
            clean.LoadFromSaveDto(hotel.CaptureSaveDto());
            Assert.AreEqual(1, clean.GuestRegister.GuestCount);
            Assert.AreEqual(1, clean.RoomInventory.OccupiedBeds());
            Assert.AreEqual(hotel.Housekeeping.DirtySetsCount, clean.Housekeeping.DirtySetsCount);
        }

        [Test]
        public void SetRateSchedule_NullSchedule_KeepsCurrent()
        {
            var (hotel, diag) = NewHotel();
            hotel.SetRateSchedule(null, diag);
            Assert.AreEqual(40, hotel.RateSchedule.NightlyRateCents(HotelRoomClass.SingleRoom));
            hotel.SetRateSchedule(new HotelRateSchedule(50, 70, 120, 275, 385, 660), diag);
            Assert.AreEqual(50, hotel.RateSchedule.NightlyRateCents(HotelRoomClass.SingleRoom));
        }
    }
}
