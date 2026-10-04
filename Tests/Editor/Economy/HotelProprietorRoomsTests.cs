using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Hotel;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// D2A: the proprietor's own household may occupy part of the
    /// building without becoming identical to the boarders (Canon §8.1A).
    /// Proprietor-occupied rooms are real physical beds but not sellable:
    /// no check-ins, no reservation holds, never counted as open beds.
    /// Occupied rooms cannot be re-flagged while guests hold beds.
    /// </summary>
    [TestFixture]
    public sealed class HotelProprietorRoomsTests
    {
        [Test]
        public void ProprietorRoom_IsNotSellable_And_NotHoldable()
        {
            var diag = new List<string>();
            var hotel = new HotelShopRuntime("hotel-inst-prop", new EntityIdRegistry());
            hotel.AddRoom(HotelRoomClass.DoubleRoom, 2, diag);
            string room = hotel.RoomInventory.Rooms[0].RoomNumber;

            Assert.IsNull(hotel.SetRoomProprietorUse(room, true, diag));
            Assert.AreEqual(0, hotel.RoomInventory.OpenBeds(HotelRoomClass.DoubleRoom),
                "proprietor rooms are never counted as open");
            Assert.AreEqual(2, hotel.RoomInventory.TotalBeds(), "they are still physical beds");

            Assert.IsNotNull(hotel.CheckInGuest(1401, room, 0, HotelRoomClass.DoubleRoom, HotelStayKind.Nightly,
                10, 1, diag), "guests never check into proprietor-household space");

            string hold = hotel.RoomReservations.Reserve("mine-9", "Copper Mine", HotelRoomClass.DoubleRoom,
                1, 10, 20, 60, 330, 1320, hotel.RoomInventory, diag);
            Assert.IsTrue(hold.StartsWith("HotelRoomReservations.Reserve:"), "holds validate against sellable beds only");
        }

        [Test]
        public void ProprietorRoom_CannotDisplaceGuests_And_CanBeReturned()
        {
            var diag = new List<string>();
            var hotel = new HotelShopRuntime("hotel-inst-prop2", new EntityIdRegistry());
            hotel.AddRoom(HotelRoomClass.SingleRoom, 1, diag);
            string room = hotel.RoomInventory.Rooms[0].RoomNumber;
            hotel.CheckInGuest(1402, room, 0, HotelRoomClass.SingleRoom, HotelStayKind.Nightly, 10, 1, diag);

            Assert.IsNotNull(hotel.SetRoomProprietorUse(room, true, diag), "a guest's room is never re-flagged under them");

            hotel.CheckOutGuest(1402, diag, 10);
            Assert.IsNull(hotel.SetRoomProprietorUse(room, true, diag), "once vacant, the flag takes");
            Assert.IsNull(hotel.SetRoomProprietorUse(room, false, diag), "and the room returns to the sellable stock");
            Assert.AreEqual(1, hotel.RoomInventory.OpenBeds(HotelRoomClass.SingleRoom));
        }

        [Test]
        public void TravelerDemand_SkipsProprietorRooms()
        {
            var diag = new List<string>();
            var hotel = new HotelShopRuntime("hotel-inst-prop3", new EntityIdRegistry());
            hotel.AddRoom(HotelRoomClass.SingleRoom, 1, diag);
            hotel.AddRoom(HotelRoomClass.DoubleRoom, 2, diag);
            string dbl = hotel.RoomInventory.Rooms[1].RoomNumber;
            hotel.SetRoomProprietorUse(dbl, true, diag);

            var arrival = new TravelerArrival
            {
                PersonId = 1403,
                ArrivalDayIndex = 10,
                PlannedStayNights = 1,
                PreferredRoomClass = HotelRoomClass.DoubleRoom,
            };
            HotelTravelerDemandResult result = HotelTravelerDemand.ProcessArrival(hotel, arrival, diag);
            Assert.AreEqual(HotelTravelerDemandOutcome.CheckedIn, result.Outcome);
            Assert.AreEqual(HotelRoomClass.SingleRoom, hotel.GuestRegister.FindRecord(1403).RoomClass,
                "the proprietor's double is not walked-in on; the traveler takes the sellable single");
        }

        [Test]
        public void OccupancyLine_NamesProprietorRooms()
        {
            var diag = new List<string>();
            var hotel = new HotelShopRuntime("hotel-inst-prop4", new EntityIdRegistry());
            hotel.AddRoom(HotelRoomClass.SingleRoom, 1, diag);
            hotel.SetRoomProprietorUse(hotel.RoomInventory.Rooms[0].RoomNumber, true, diag);
            string line = hotel.BuildOccupancyLine();
            Assert.IsTrue(line.Contains("1 proprietor-household room"), line);
        }

        [Test]
        public void SaveLoad_RoundTripsProprietorFlag()
        {
            var diag = new List<string>();
            var hotel = new HotelShopRuntime("hotel-inst-prop5", new EntityIdRegistry());
            hotel.AddRoom(HotelRoomClass.SingleRoom, 1, diag);
            hotel.SetRoomProprietorUse(hotel.RoomInventory.Rooms[0].RoomNumber, true, diag);

            var reloaded = new HotelShopRuntime("hotel-inst-prop5", new EntityIdRegistry());
            reloaded.LoadFromSaveDto(hotel.CaptureSaveDto());
            Assert.IsTrue(reloaded.RoomInventory.Rooms[0].ProprietorOccupied);
            Assert.AreEqual(0, reloaded.RoomInventory.OpenBeds(HotelRoomClass.SingleRoom));
        }
    }
}
