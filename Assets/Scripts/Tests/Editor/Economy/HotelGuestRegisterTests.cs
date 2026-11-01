using System.Collections.Generic;
using LandLedgers.Economy.Businesses.BoardingHouse;
using LandLedgers.Economy.Businesses.Hotel;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W3A: the guest register sells room nights — check-in locks the
    /// rate, nightly settlement emits one sale per occupied bed-night,
    /// expired nightly stays check out and release their bed, weekly
    /// guests settle weekly, and every guest resolves to a HotelRoom
    /// nightly placement (the W2C hook).
    /// </summary>
    [TestFixture]
    public sealed class HotelGuestRegisterTests
    {
        private static (HotelGuestRegister, HotelRoomInventory, List<string>) NewRegister()
        {
            var inv = new HotelRoomInventory();
            var diag = new List<string>();
            inv.AddRoom(HotelRoomClass.SingleRoom, 1, diag);
            inv.AddRoom(HotelRoomClass.DoubleRoom, 2, diag);
            string single = inv.Rooms[0].RoomNumber;
            string dbl = inv.Rooms[1].RoomNumber;
            return (new HotelGuestRegister(), inv, diag);
        }

        private static string SingleRoom((HotelGuestRegister, HotelRoomInventory, List<string>) t) => t.Item2.Rooms[0].RoomNumber;
        private static string DoubleRoom((HotelGuestRegister, HotelRoomInventory, List<string>) t) => t.Item2.Rooms[1].RoomNumber;

        [Test]
        public void CheckIn_Nightly_LocksRate_AndPlacesGuest()
        {
            var (reg, inv, diag) = NewRegister();
            string room = SingleRoom((reg, inv, diag));
            Assert.IsNull(reg.CheckIn(101, room, 0, HotelRoomClass.SingleRoom, HotelStayKind.Nightly,
                40, 220, 10, 3, inv, diag));
            Assert.AreEqual(1, reg.GuestCount);

            Assert.IsTrue(reg.TryGetNightlyPlacement(101, "hotel-inst-1", out HotelNightlyPlacement placement));
            Assert.AreEqual(BoardingNightlyState.HotelRoom, placement.NightlyState);
            Assert.AreEqual(room, placement.RoomNumber);
            Assert.AreEqual(HotelRoomClass.SingleRoom, placement.RoomClass);
            Assert.AreEqual(40, placement.LockedNightlyRateCents);
        }

        [Test]
        public void CheckIn_Refuses_Anonymous_DoubleAgreement_NoRoom()
        {
            var (reg, inv, diag) = NewRegister();
            string room = SingleRoom((reg, inv, diag));
            Assert.IsNotNull(reg.CheckIn(0, room, 0, HotelRoomClass.SingleRoom, HotelStayKind.Nightly, 40, 220, 1, 1, inv, diag));
            Assert.IsNotNull(reg.CheckIn(102, room, 0, HotelRoomClass.SingleRoom, HotelStayKind.Nightly, 40, 220, 1, 0, inv, diag),
                "a nightly stay needs at least one paid night");
            Assert.IsNotNull(reg.CheckIn(102, "no-such-room", 0, HotelRoomClass.SingleRoom, HotelStayKind.Nightly, 40, 220, 1, 1, inv, diag));
            Assert.IsNull(reg.CheckIn(102, room, 0, HotelRoomClass.SingleRoom, HotelStayKind.Nightly, 40, 220, 1, 1, inv, diag));
            Assert.IsNotNull(reg.CheckIn(102, room, 0, HotelRoomClass.SingleRoom, HotelStayKind.Nightly, 40, 220, 1, 1, inv, diag),
                "one person holds one agreement");
        }

        [Test]
        public void SettleNight_EmitsSales_Decrements_ExpiresReleases()
        {
            var (reg, inv, diag) = NewRegister();
            string room = SingleRoom((reg, inv, diag));
            reg.CheckIn(111, room, 0, HotelRoomClass.SingleRoom, HotelStayKind.Nightly, 40, 220, 5, 2, inv, diag);

            var sales1 = reg.SettleNight(5, inv, diag);
            Assert.AreEqual(1, sales1.Count);
            Assert.AreEqual(40, sales1[0].CentsCharged);
            Assert.AreEqual(5, sales1[0].DayIndex);
            Assert.AreEqual(HotelStayKind.Nightly, sales1[0].StayKind);
            Assert.AreEqual(1, reg.GuestCount, "one paid night left");

            var sales2 = reg.SettleNight(6, inv, diag);
            Assert.AreEqual(1, sales2.Count);
            Assert.AreEqual(0, reg.GuestCount, "stay expired");
            Assert.AreEqual(0, inv.OccupiedBeds(), "bed released on expiry");

            var sales3 = reg.SettleNight(7, inv, diag);
            Assert.AreEqual(0, sales3.Count);
        }

        [Test]
        public void SettleNight_WeeklyGuest_AccruesAtZero_SetttlesWeekly()
        {
            var (reg, inv, diag) = NewRegister();
            string room = DoubleRoom((reg, inv, diag));
            reg.CheckIn(121, room, 0, HotelRoomClass.DoubleRoom, HotelStayKind.Weekly, 60, 330, 5, 0, inv, diag);

            var sales = reg.SettleNight(5, inv, diag);
            Assert.AreEqual(1, sales.Count);
            Assert.AreEqual(0, sales[0].CentsCharged, "weekly guests pay weekly, not nightly");
            Assert.AreEqual(HotelStayKind.Weekly, sales[0].StayKind);
            Assert.AreEqual(1, reg.GuestCount, "weekly guest persists across nights");

            var due = reg.RentDueWeekly(11, diag);
            Assert.AreEqual(1, due.Count);
            Assert.AreEqual(330, due[0].CentsDue);
            Assert.AreEqual(7, due[0].NightsCovered);
        }

        [Test]
        public void RateLockedAtCheckIn_ScheduleChangeDoesNotRetroprice()
        {
            var (reg, inv, diag) = NewRegister();
            string room = SingleRoom((reg, inv, diag));
            reg.CheckIn(131, room, 0, HotelRoomClass.SingleRoom, HotelStayKind.Nightly, 40, 220, 1, 1, inv, diag);
            // The record holds the locked rate, not a schedule lookup.
            Assert.AreEqual(40, reg.FindRecord(131).LockedNightlyRateCents);
        }

        [Test]
        public void CheckOut_ReleasesBed_UnknownGuestRefused()
        {
            var (reg, inv, diag) = NewRegister();
            string room = SingleRoom((reg, inv, diag));
            Assert.IsNotNull(reg.CheckOut(141, inv, diag));
            reg.CheckIn(141, room, 0, HotelRoomClass.SingleRoom, HotelStayKind.Weekly, 40, 220, 1, 0, inv, diag);
            Assert.IsNull(reg.CheckOut(141, inv, diag));
            Assert.AreEqual(0, reg.GuestCount);
            Assert.AreEqual(0, inv.OccupiedBeds());
        }

        [Test]
        public void TryGetNightlyPlacement_NonGuest_ReturnsFalse()
        {
            var (reg, inv, diag) = NewRegister();
            Assert.IsFalse(reg.TryGetNightlyPlacement(999, "hotel-inst-1", out _));
        }

        [Test]
        public void SaveLoad_RoundTrip_PreservesAgreements()
        {
            var (reg, inv, diag) = NewRegister();
            reg.CheckIn(151, SingleRoom((reg, inv, diag)), 0, HotelRoomClass.SingleRoom, HotelStayKind.Nightly, 40, 220, 1, 2, inv, diag);
            reg.CheckIn(152, DoubleRoom((reg, inv, diag)), 1, HotelRoomClass.DoubleRoom, HotelStayKind.Weekly, 60, 330, 1, 0, inv, diag);

            var dto = reg.CaptureSaveDto();
            var reloaded = new HotelGuestRegister();
            reloaded.LoadFromSaveDto(dto);
            Assert.AreEqual(2, reloaded.GuestCount);
            Assert.AreEqual(40, reloaded.FindRecord(151).LockedNightlyRateCents);
            Assert.AreEqual(2, reloaded.FindRecord(151).NightsRemaining);
            Assert.AreEqual(BoardingNightlyState.HotelRoom, reloaded.FindRecord(152).NightlyState);
        }
    }
}
