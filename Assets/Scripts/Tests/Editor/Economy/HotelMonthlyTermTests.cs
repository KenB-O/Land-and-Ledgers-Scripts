using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Hotel;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// D2A: the monthly longer-stay agreement (Canon §8.1D "weekly or
    /// longer-stay terms"; §8.1C "board for weeks or months before
    /// renting"). Monthly rates lock at check-in (4× weekly default),
    /// nightly settlement emits 0-charge sales, and ExecuteMonth reports
    /// the 30-day dues. Old saves without monthly rates fall back to the
    /// 4× weekly calibration rather than a free room.
    /// </summary>
    [TestFixture]
    public sealed class HotelMonthlyTermTests
    {
        private static (HotelShopRuntime, List<string>) NewHotel()
        {
            var diag = new List<string>();
            var hotel = new HotelShopRuntime("hotel-inst-monthly", new EntityIdRegistry());
            hotel.AddRoom(HotelRoomClass.SingleRoom, 1, diag);
            hotel.AddRoom(HotelRoomClass.DoubleRoom, 2, diag);
            hotel.ApplyOpeningLinenEndowment(1, diag);
            return (hotel, diag);
        }

        [Test]
        public void CheckIn_Monthly_LocksMonthlyRate_SettlesMonthly()
        {
            var (hotel, diag) = NewHotel();
            string dbl = hotel.RoomInventory.Rooms[1].RoomNumber;
            Assert.IsNull(hotel.CheckInGuest(1101, dbl, 0, HotelRoomClass.DoubleRoom, HotelStayKind.Monthly,
                20, 0, diag));

            HotelGuestRecord record = hotel.GuestRegister.FindRecord(1101);
            Assert.IsNotNull(record);
            Assert.AreEqual(1320, record.LockedMonthlyRateCents, "4× the 330¢ weekly rate");

            var sales = hotel.GuestRegister.SettleNight(20, hotel.RoomInventory, diag);
            Assert.AreEqual(1, sales.Count);
            Assert.AreEqual(0, sales[0].CentsCharged, "monthly guests settle monthly, not nightly");
            Assert.AreEqual(HotelStayKind.Monthly, sales[0].StayKind);
            Assert.AreEqual(1, hotel.GuestRegister.GuestCount, "monthly stays persist — no paid-night expiry");

            var due = hotel.ExecuteMonth(49, diag);
            Assert.AreEqual(1, due.Count);
            Assert.AreEqual(1320, due[0].CentsDue);
            Assert.AreEqual(30, due[0].NightsCovered);
            Assert.AreEqual(HotelStayKind.Monthly, due[0].StayKind);
        }

        [Test]
        public void MonthlyTerm_AccountGuest_ChargesPostToFolio()
        {
            var (hotel, diag) = NewHotel();
            string single = hotel.RoomInventory.Rooms[0].RoomNumber;
            hotel.CheckInGuest(1102, single, 0, HotelRoomClass.SingleRoom, HotelStayKind.Monthly,
                20, 0, diag, 0, null, 2000);
            hotel.ExecuteMonth(49, diag);
            HotelGuestFolio folio = hotel.GuestFolios.FindFolio(1102);
            Assert.AreEqual(880, folio.BalanceCents, "the monthly dues posted to the folio");
        }

        [Test]
        public void MonthlyRate_Defaults_ToFourTimesWeekly_And_LocksAtCheckIn()
        {
            var schedule = new HotelRateSchedule();
            Assert.AreEqual(880, schedule.MonthlyRateCents(HotelRoomClass.SingleRoom));
            Assert.AreEqual(1320, schedule.MonthlyRateCents(HotelRoomClass.DoubleRoom));
            Assert.AreEqual(2200, schedule.MonthlyRateCents(HotelRoomClass.ParlorSuite));

            var (hotel, diag) = NewHotel();
            string single = hotel.RoomInventory.Rooms[0].RoomNumber;
            hotel.CheckInGuest(1103, single, 0, HotelRoomClass.SingleRoom, HotelStayKind.Monthly,
                20, 0, diag, 700);
            Assert.AreEqual(700, hotel.GuestRegister.FindRecord(1103).LockedMonthlyRateCents,
                "an explicit monthly rate locks over the schedule default");
        }

        [Test]
        public void SaveLoad_MigratesOldRates_ToCalibrationDefault()
        {
            var schedule = new HotelRateSchedule();
            var dto = schedule.CaptureSaveDto();
            // Simulate a pre-D2A save: monthly rates were never written.
            dto.SingleMonthlyCents = 0;
            dto.DoubleMonthlyCents = 0;
            dto.ParlorSuiteMonthlyCents = 0;

            var reloaded = new HotelRateSchedule();
            reloaded.LoadFromSaveDto(dto);
            Assert.AreEqual(880, reloaded.SingleMonthlyCents, "unset monthly falls back to 4× weekly, never a free room");
            Assert.AreEqual(1320, reloaded.DoubleMonthlyCents);
        }

        [Test]
        public void WeeklyTerm_Unaffected_ByMonthly_Addition()
        {
            var (hotel, diag) = NewHotel();
            string single = hotel.RoomInventory.Rooms[0].RoomNumber;
            hotel.CheckInGuest(1104, single, 0, HotelRoomClass.SingleRoom, HotelStayKind.Weekly, 20, 0, diag);
            var monthly = hotel.ExecuteMonth(49, diag);
            Assert.AreEqual(0, monthly.Count, "weekly guests settle weekly only");
            var weekly = hotel.ExecuteWeek(26, diag);
            Assert.AreEqual(1, weekly.Count);
        }
    }
}
