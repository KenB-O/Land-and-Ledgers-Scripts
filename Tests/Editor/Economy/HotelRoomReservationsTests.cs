using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Hotel;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// D2A: contract/commercial bed holds (Canon §8.1D) — validated
    /// against real physical beds, withheld from walk-in arrivals,
    /// consumed by reservation check-ins at locked rates, released on
    /// checkout, freed on early release. Proprietor-household rooms never
    /// count as holdable.
    /// </summary>
    [TestFixture]
    public sealed class HotelRoomReservationsTests
    {
        private static (HotelShopRuntime, HotelRoomReservations, List<string>) NewHotel()
        {
            var diag = new List<string>();
            var hotel = new HotelShopRuntime("hotel-inst-res", new LandLedgers.Primitives.EntityIdRegistry());
            hotel.AddRoom(HotelRoomClass.SingleRoom, 1, diag);
            hotel.AddRoom(HotelRoomClass.ParlorSuite, 2, diag);
            return (hotel, hotel.RoomReservations, diag);
        }

        [Test]
        public void Reserve_HoldsBeds_AgainstPhysicalCapacity()
        {
            var (hotel, reservations, diag) = NewHotel();
            string id = reservations.Reserve("mine-1", "Copper Mine", HotelRoomClass.ParlorSuite,
                2, 10, 20, 80, 440, 1760, hotel.RoomInventory, diag);
            Assert.IsNotNull(id, "one parlor suite holds exactly 2 beds");
            Assert.AreEqual(2, reservations.WithheldFromWalkIns(HotelRoomClass.ParlorSuite, 15));
            Assert.AreEqual(0, reservations.WithheldFromWalkIns(HotelRoomClass.ParlorSuite, 9), "hold not yet started");
            Assert.AreEqual(0, reservations.WithheldFromWalkIns(HotelRoomClass.ParlorSuite, 21), "hold expired");
        }

        [Test]
        public void Reserve_Refuses_OverPhysical_AndAnonymousCustomer()
        {
            var (hotel, reservations, diag) = NewHotel();
            string anonymous = reservations.Reserve("", "Nobody", HotelRoomClass.SingleRoom, 1, 10, 20, 40, 220, 880,
                hotel.RoomInventory, diag);
            Assert.IsTrue(anonymous.StartsWith("HotelRoomReservations.Reserve:"), "anonymous customers cannot hold beds");
            string overCapacity = reservations.Reserve("mine-2", "Silver Mine", HotelRoomClass.SingleRoom, 2, 10, 20, 40, 220, 880,
                hotel.RoomInventory, diag);
            Assert.IsTrue(overCapacity.StartsWith("HotelRoomReservations.Reserve:"), "one single holds one bed, not two");
        }

        [Test]
        public void Reserve_ExcludesProprietorHouseholdRooms()
        {
            var (hotel, reservations, diag) = NewHotel();
            string suite = hotel.RoomInventory.Rooms[1].RoomNumber;
            hotel.SetRoomProprietorUse(suite, true, diag);
            string proprietor = reservations.Reserve("mine-3", "Lead Mine", HotelRoomClass.ParlorSuite, 1, 10, 20, 80, 440, 1760,
                hotel.RoomInventory, diag);
            Assert.IsTrue(proprietor.StartsWith("HotelRoomReservations.Reserve:"), "proprietor-household rooms are not holdable (Canon §8.1A)");
        }

        [Test]
        public void ConsumeBed_LocksReservedRates_AndReleasesOnCheckout()
        {
            var (hotel, reservations, diag) = NewHotel();
            string suite = hotel.RoomInventory.Rooms[1].RoomNumber;
            string id = reservations.Reserve("mine-4", "Iron Mine", HotelRoomClass.ParlorSuite,
                2, 10, 20, 80, 440, 1760, hotel.RoomInventory, diag);

            Assert.IsNull(hotel.CheckInGuest(701, suite, 0, HotelRoomClass.ParlorSuite, HotelStayKind.Nightly,
                10, 2, diag, 0, id));
            HotelGuestRecord record = hotel.GuestRegister.FindRecord(701);
            Assert.IsNotNull(record);
            Assert.AreEqual(80, record.LockedNightlyRateCents, "the reservation's rate, not the walk-in schedule");
            Assert.AreEqual(id, record.ReservationId);
            Assert.AreEqual(1, reservations.WithheldFromWalkIns(HotelRoomClass.ParlorSuite, 15), "one held bed consumed");

            Assert.IsNull(hotel.CheckOutGuest(701, diag, 12));
            Assert.AreEqual(2, reservations.WithheldFromWalkIns(HotelRoomClass.ParlorSuite, 15), "checkout returns the held bed");
        }

        [Test]
        public void ConsumeBed_Refuses_WhenHoldConsumedOrWrongDay()
        {
            var (hotel, reservations, diag) = NewHotel();
            string id = reservations.Reserve("mine-5", "Gold Mine", HotelRoomClass.SingleRoom,
                1, 10, 20, 30, 180, 720, hotel.RoomInventory, diag);

            string single = hotel.RoomInventory.Rooms[0].RoomNumber;
            // No bed consumed directly — go through the guest register path via runtime check-in on the wrong day.
            Assert.IsNotNull(hotel.CheckInGuest(702, single, 0, HotelRoomClass.SingleRoom, HotelStayKind.Nightly,
                25, 2, diag, 0, id), "the hold starts day 10, not day 25");
            Assert.IsNotNull(hotel.CheckInGuest(702, single, 0, HotelRoomClass.SingleRoom, HotelStayKind.Nightly,
                10, 2, diag, 0, id), "a consumed bed cannot be consumed twice over");
            Assert.IsNotNull(hotel.CheckInGuest(703, single, 0, HotelRoomClass.SingleRoom, HotelStayKind.Nightly,
                10, 2, diag, 0, id), "the hold is fully consumed");
        }

        [Test]
        public void WalkIn_WithholdsHeldBeds_FromTravelerArrivals()
        {
            var (hotel, reservations, diag) = NewHotel();
            string id = reservations.Reserve("rail-1", "Rail Survey", HotelRoomClass.SingleRoom,
                1, 10, 20, 30, 180, 720, hotel.RoomInventory, diag);
            Assert.IsNotNull(id);

            var arrival = new TravelerArrival
            {
                PersonId = 801,
                ArrivalDayIndex = 12,
                PlannedStayNights = 1,
                PreferredRoomClass = HotelRoomClass.SingleRoom,
            };
            HotelTravelerDemandResult result = HotelTravelerDemand.ProcessArrival(hotel, arrival, diag);
            Assert.AreEqual(HotelTravelerDemandOutcome.CheckedIn, result.Outcome);
            // The only single is held — the walk-in takes the parlor suite as class fallback, never the held bed.
            Assert.AreEqual(HotelRoomClass.ParlorSuite, hotel.GuestRegister.FindRecord(801).RoomClass);
            HotelRoomReservation reservation = reservations.Find(id);
            Assert.AreEqual(1, reservation.UnconsumedBeds, "the hold is untouched by the walk-in");
        }

        [Test]
        public void ReleaseReservation_FreesBeds_ButLodgedGuestsKeepRates()
        {
            var (hotel, reservations, diag) = NewHotel();
            string suite = hotel.RoomInventory.Rooms[1].RoomNumber;
            string id = reservations.Reserve("mine-6", "Zinc Mine", HotelRoomClass.ParlorSuite,
                2, 10, 20, 80, 440, 1760, hotel.RoomInventory, diag);
            hotel.CheckInGuest(901, suite, 0, HotelRoomClass.ParlorSuite, HotelStayKind.Weekly, 10, 0, diag, 0, id);

            Assert.IsNull(reservations.ReleaseReservation(id, 12, diag));
            Assert.AreEqual(0, reservations.WithheldFromWalkIns(HotelRoomClass.ParlorSuite, 15), "hold freed");
            HotelGuestRecord record = hotel.GuestRegister.FindRecord(901);
            Assert.AreEqual(440, record.LockedWeeklyRateCents, "rent agreement memory: the lodged guest keeps the reserved rate");
        }
    }
}
