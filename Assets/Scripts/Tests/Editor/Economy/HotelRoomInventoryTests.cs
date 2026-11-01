using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Hotel;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W3A: hotel rooms are real rooms with real beds — class/bed
    /// invariants hold, beds are never double-assigned, a person holds
    /// at most one bed, occupancy counts from assigned beds.
    /// </summary>
    [TestFixture]
    public sealed class HotelRoomInventoryTests
    {
        private static HotelRoomInventory NewInventory(out List<string> diag)
        {
            diag = new List<string>();
            return new HotelRoomInventory();
        }

        [Test]
        public void AddRoom_SingleHoldsExactlyOneBed()
        {
            var inv = NewInventory(out var diag);
            Assert.IsNull(inv.AddRoom(HotelRoomClass.SingleRoom, 1, diag));
            Assert.IsNotNull(inv.AddRoom(HotelRoomClass.SingleRoom, 2, diag));
            Assert.IsNotNull(inv.AddRoom(HotelRoomClass.SingleRoom, 0, diag));
            Assert.AreEqual(1, inv.Rooms.Count);
        }

        [Test]
        public void AddRoom_DoubleAndSuiteHoldExactlyTwoBeds()
        {
            var inv = NewInventory(out var diag);
            Assert.IsNull(inv.AddRoom(HotelRoomClass.DoubleRoom, 2, diag));
            Assert.IsNotNull(inv.AddRoom(HotelRoomClass.DoubleRoom, 1, diag));
            Assert.IsNull(inv.AddRoom(HotelRoomClass.ParlorSuite, 2, diag));
            Assert.IsNotNull(inv.AddRoom(HotelRoomClass.ParlorSuite, 3, diag));
            Assert.AreEqual(2, inv.Rooms.Count);
        }

        [Test]
        public void AssignSpecificBed_NoDoubleBooking_OnePersonOneBed()
        {
            var inv = NewInventory(out var diag);
            inv.AddRoom(HotelRoomClass.DoubleRoom, 2, diag);
            string room = inv.Rooms[0].RoomNumber;

            Assert.IsNull(inv.AssignSpecificBed(11, room, 0, diag));
            Assert.IsNotNull(inv.AssignSpecificBed(12, room, 0, diag), "occupied bed must refuse");
            Assert.IsNotNull(inv.AssignSpecificBed(11, room, 1, diag), "one person holds at most one bed");
            Assert.IsNull(inv.AssignSpecificBed(12, room, 1, diag));
            Assert.AreEqual(2, inv.OccupiedBeds());
            Assert.AreEqual(0, inv.OpenBeds(HotelRoomClass.DoubleRoom));
        }

        [Test]
        public void AssignBed_FindsFirstOpenBedOfClass()
        {
            var inv = NewInventory(out var diag);
            inv.AddRoom(HotelRoomClass.SingleRoom, 1, diag);
            inv.AddRoom(HotelRoomClass.DoubleRoom, 2, diag);

            Assert.IsNull(inv.AssignBed(21, HotelRoomClass.DoubleRoom, out string roomNo, out int bed, diag));
            Assert.AreEqual(0, bed);
            Assert.AreEqual(inv.Rooms[1].RoomNumber, roomNo);
            Assert.AreEqual(1, inv.OpenBeds(HotelRoomClass.DoubleRoom), "one of two double beds now occupied");
            Assert.IsNotNull(inv.AssignBed(0, null, out _, out _, diag), "anonymous sleepers refused");
        }

        [Test]
        public void ReleaseBedByPerson_FreesTheBed()
        {
            var inv = NewInventory(out var diag);
            inv.AddRoom(HotelRoomClass.SingleRoom, 1, diag);
            string room = inv.Rooms[0].RoomNumber;
            inv.AssignSpecificBed(31, room, 0, diag);
            Assert.IsTrue(inv.ReleaseBedByPerson(31, diag));
            Assert.AreEqual(0, inv.OccupiedBeds());
            Assert.IsFalse(inv.ReleaseBedByPerson(31, diag));
        }

        [Test]
        public void TotalAndOccupiedBeds_CountRealBeds()
        {
            var inv = NewInventory(out var diag);
            inv.AddRoom(HotelRoomClass.SingleRoom, 1, diag);
            inv.AddRoom(HotelRoomClass.ParlorSuite, 2, diag);
            Assert.AreEqual(3, inv.TotalBeds());
            Assert.AreEqual(0, inv.OccupiedBeds());
            inv.AssignBed(41, null, out _, out _, diag);
            Assert.AreEqual(1, inv.OccupiedBeds());
        }

        [Test]
        public void SaveLoad_RoundTrip_PreservesAssignments()
        {
            var inv = NewInventory(out var diag);
            inv.AddRoom(HotelRoomClass.DoubleRoom, 2, diag);
            inv.AddRoom(HotelRoomClass.SingleRoom, 1, diag);
            inv.AssignBed(51, null, out string roomNo, out int bed, diag);

            var dto = inv.CaptureSaveDto();
            var reloaded = new HotelRoomInventory();
            reloaded.LoadFromSaveDto(dto);

            Assert.AreEqual(2, reloaded.Rooms.Count);
            Assert.IsTrue(reloaded.PersonHoldsAnyBed(51));
            Assert.AreEqual(51, reloaded.FindRoom(roomNo).OccupantOfBed(bed));

            // A second save from the reloaded inventory is stable.
            var dto2 = reloaded.CaptureSaveDto();
            Assert.AreEqual(dto.Rooms.Count, dto2.Rooms.Count);
        }

        [Test]
        public void LoadFromSaveDto_DropsCorruptAssignments()
        {
            var inv = NewInventory(out var diag);
            inv.AddRoom(HotelRoomClass.SingleRoom, 1, diag);
            var dto = inv.CaptureSaveDto();
            // Corrupt: person on a bed index that does not exist.
            dto.Rooms[0].OccupiedBeds.Add(new HotelRoomBedAssignment(7, 61));

            var reloaded = new HotelRoomInventory();
            reloaded.LoadFromSaveDto(dto);
            Assert.IsFalse(reloaded.PersonHoldsAnyBed(61));
        }
    }
}
