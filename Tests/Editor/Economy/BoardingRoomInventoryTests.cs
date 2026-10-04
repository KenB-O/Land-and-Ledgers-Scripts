using System.Collections.Generic;
using LandLedgers.Economy.Businesses.BoardingHouse;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W2C: rooms as rentable inventory — beds are real, counted from
    /// assignments, never estimated. One person holds one bed; beds are
    /// never double-assigned; the inventory round-trips through save/load.
    /// </summary>
    [TestFixture]
    public sealed class BoardingRoomInventoryTests
    {
        [Test]
        public void AddRoom_ValidatesPhysicalShapes()
        {
            var inventory = new BoardingRoomInventory();
            var diag = new List<string>();

            Assert.Null(inventory.AddRoom(BoardingRoomType.SharedBed, 4, diag));
            Assert.Null(inventory.AddRoom(BoardingRoomType.PrivateRoom, 1, diag));
            Assert.Null(inventory.AddRoom(BoardingRoomType.FamilyRoom, 3, diag));

            Assert.NotNull(inventory.AddRoom(BoardingRoomType.PrivateRoom, 2, diag), "private room holds exactly one bed");
            Assert.NotNull(inventory.AddRoom(BoardingRoomType.SharedBed, 0, diag), "a room needs beds");
            Assert.NotNull(inventory.AddRoom(BoardingRoomType.FamilyRoom, 1, diag), "a family room needs at least two beds");

            Assert.AreEqual(4 + 1 + 3, inventory.TotalBeds());
        }

        [Test]
        public void AssignBed_RefusesAnonymousAndDoubleAssignment()
        {
            var inventory = new BoardingRoomInventory();
            var diag = new List<string>();
            inventory.AddRoom(BoardingRoomType.SharedBed, 2, diag);

            Assert.NotNull(inventory.AssignBed(0, null, out _, out _, diag), "anonymous sleepers are refused");
            Assert.Null(inventory.AssignBed(11, null, out string roomId, out int bedIndex, diag));
            Assert.AreNotEqual(string.Empty, roomId);
            Assert.GreaterOrEqual(bedIndex, 0);

            Assert.NotNull(inventory.AssignBed(11, null, out _, out _, diag), "one person, one bed");
            Assert.IsTrue(inventory.PersonHoldsAnyBed(11));
            Assert.IsFalse(inventory.PersonHoldsAnyBed(12));
        }

        [Test]
        public void AssignBed_FillsThenRefusesHonestly()
        {
            var inventory = new BoardingRoomInventory();
            var diag = new List<string>();
            inventory.AddRoom(BoardingRoomType.SharedBed, 2, diag);

            Assert.Null(inventory.AssignBed(11, null, out _, out _, diag));
            Assert.Null(inventory.AssignBed(12, null, out _, out _, diag));
            Assert.NotNull(inventory.AssignBed(13, null, out _, out _, diag), "no open bed — refused, never invented");

            Assert.AreEqual(2, inventory.OccupiedBeds());
            Assert.AreEqual(0, inventory.OpenBeds(BoardingRoomType.SharedBed));
            Assert.AreEqual(0, inventory.OpenBeds(BoardingRoomType.PrivateRoom));
        }

        [Test]
        public void AssignBed_HonorsRoomTypeFilter()
        {
            var inventory = new BoardingRoomInventory();
            var diag = new List<string>();
            inventory.AddRoom(BoardingRoomType.PrivateRoom, 1, diag);
            inventory.AddRoom(BoardingRoomType.SharedBed, 2, diag);

            Assert.Null(inventory.AssignBed(11, BoardingRoomType.SharedBed, out string roomId, out _, diag));
            BoardingRoom room = inventory.FindRoom(roomId);
            Assert.AreEqual(BoardingRoomType.SharedBed, room.RoomType);
        }

        [Test]
        public void AssignSpecificBed_RefusesOccupiedAndMissing()
        {
            var inventory = new BoardingRoomInventory();
            var diag = new List<string>();
            inventory.AddRoom(BoardingRoomType.SharedBed, 2, diag);

            Assert.Null(inventory.AssignSpecificBed(11, "bh-room-1", 0, diag));
            Assert.NotNull(inventory.AssignSpecificBed(12, "bh-room-1", 0, diag), "no double-booking");
            Assert.NotNull(inventory.AssignSpecificBed(12, "bh-room-1", 7, diag), "bed 7 does not exist");
            Assert.NotNull(inventory.AssignSpecificBed(12, "bh-room-9", 0, diag), "room does not exist");

            Assert.IsTrue(inventory.ReleaseBedByPerson(11, diag));
            Assert.IsFalse(inventory.PersonHoldsAnyBed(11));
            Assert.AreEqual(0, inventory.OccupiedBeds());
        }

        [Test]
        public void Inventory_SaveLoad_RoundTripsBedAssignments()
        {
            var inventory = new BoardingRoomInventory();
            var diag = new List<string>();
            inventory.AddRoom(BoardingRoomType.SharedBed, 2, diag);
            inventory.AddRoom(BoardingRoomType.PrivateRoom, 1, diag);
            inventory.AssignSpecificBed(11, "bh-room-1", 0, diag);
            inventory.AssignSpecificBed(12, "bh-room-2", 0, diag);

            var restored = new BoardingRoomInventory();
            restored.LoadFromSaveDto(inventory.CaptureSaveDto());

            Assert.AreEqual(3, restored.TotalBeds());
            Assert.AreEqual(2, restored.OccupiedBeds());
            Assert.IsTrue(restored.PersonHoldsAnyBed(11));
            Assert.IsTrue(restored.PersonHoldsAnyBed(12));
            BoardingRoom room = restored.FindRoom("bh-room-1");
            Assert.AreEqual(11, room.OccupantOfBed(0));
            Assert.AreEqual(0, room.OccupantOfBed(1));

            // Room ids keep sequencing after load (no collisions).
            Assert.Null(restored.AddRoom(BoardingRoomType.SharedBed, 1, diag));
            Assert.IsNotNull(restored.FindRoom("bh-room-3"));
        }
    }
}
