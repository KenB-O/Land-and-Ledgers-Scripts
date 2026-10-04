using System.Collections.Generic;
using LandLedgers.Economy.Businesses.BoardingHouse;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// D1F: room turnover — a vacated bed soils its room (Canon §8.1E bed
    /// turnover); the soiled bed cannot be re-let until chamber work turns
    /// the room. With no chamber roster the proprietor/family turns rooms
    /// (Canon §8.1E small-house fallback); a fully-departed roster turns
    /// nothing, loudly.
    /// </summary>
    [TestFixture]
    public sealed class BoardingRoomTurnoverTests
    {
        private BoardingHouseShopRuntime NewHouse(int dayIndex, List<string> diag)
        {
            var house = new BoardingHouseShopRuntime("bh-1", new EntityIdRegistry());
            house.ApplyOpeningPantryEndowment(dayIndex, diag);
            house.ApplyOpeningFuelEndowment(dayIndex, diag);
            Assert.Null(house.AddRoom(BoardingRoomType.SharedBed, 2, diag));
            return house;
        }

        [Test]
        public void Checkout_SoilsBed_ReletRefusedUntilTurned()
        {
            var diag = new List<string>();
            var house = NewHouse(100, diag);

            Assert.Null(house.CheckInBoarder(11, "bh-room-1", 0, BoarderStayKind.Weekly,
                boardIncluded: false, startDayIndex: 100, transientNights: 0, diag: diag));
            Assert.Null(house.CheckOutBoarder(11, diag));

            Assert.AreEqual(1, house.RoomInventory.SoiledBedCount());
            Assert.NotNull(house.CheckInBoarder(12, "bh-room-1", 0, BoarderStayKind.Weekly,
                boardIncluded: false, startDayIndex: 101, transientNights: 0, diag: diag),
                "soiled bed cannot be re-let before turnover");
            Assert.Null(house.CheckInBoarder(12, "bh-room-1", 1, BoarderStayKind.Weekly,
                boardIncluded: false, startDayIndex: 101, transientNights: 0, diag: diag),
                "the clean bed in the same room still lets");
        }

        [Test]
        public void RunTurnoverCleaning_ProprietorFallbackTurnsRooms()
        {
            var diag = new List<string>();
            var house = NewHouse(100, diag);

            Assert.Null(house.CheckInBoarder(11, "bh-room-1", 0, BoarderStayKind.Weekly,
                boardIncluded: false, startDayIndex: 100, transientNights: 0, diag: diag));
            Assert.Null(house.CheckOutBoarder(11, diag));
            Assert.AreEqual(1, house.RoomInventory.SoiledBedCount());

            int turned = house.RunTurnoverCleaning(101, 0, diag);
            Assert.AreEqual(1, turned, "proprietor/family turns the room with no roster named");
            Assert.AreEqual(0, house.RoomInventory.SoiledBedCount());

            Assert.Null(house.CheckInBoarder(12, "bh-room-1", 0, BoarderStayKind.Weekly,
                boardIncluded: false, startDayIndex: 101, transientNights: 0, diag: diag),
                "turned bed re-lets");
        }

        [Test]
        public void RunTurnoverCleaning_DepartedRosterTurnsNothing()
        {
            var diag = new List<string>();
            var house = NewHouse(100, diag);
            Assert.Null(house.AssignChambermaid(21, 5, 100, diag));
            Assert.Null(house.ReleaseChambermaid(21, 100, diag));

            Assert.Null(house.CheckInBoarder(11, "bh-room-1", 0, BoarderStayKind.Weekly,
                boardIncluded: false, startDayIndex: 100, transientNights: 0, diag: diag));
            Assert.Null(house.CheckOutBoarder(11, diag));

            int turned = house.RunTurnoverCleaning(101, 0, diag);
            Assert.AreEqual(0, turned, "the housekeeper-loss case: nobody turns rooms");
            Assert.AreEqual(1, house.RoomInventory.SoiledBedCount(), "soiled bed waits");
        }

        [Test]
        public void RunTurnoverCleaning_StaffMinutesTurnRooms_ShortfallStopsLoudly()
        {
            var diag = new List<string>();
            var house = NewHouse(100, diag);
            Assert.Null(house.AddRoom(BoardingRoomType.SharedBed, 2, diag));
            Assert.Null(house.AssignChambermaid(21, 5, 100, diag, minutesPerDay: 20));

            Assert.Null(house.CheckInBoarder(11, "bh-room-1", 0, BoarderStayKind.Weekly, false, 100, 0, diag));
            Assert.Null(house.CheckInBoarder(12, "bh-room-2", 0, BoarderStayKind.Weekly, false, 100, 0, diag));
            Assert.Null(house.CheckOutBoarder(11, diag));
            Assert.Null(house.CheckOutBoarder(12, diag));
            Assert.AreEqual(2, house.RoomInventory.SoiledBedCount());

            int turned = house.RunTurnoverCleaning(101, 0, diag);
            Assert.AreEqual(1, turned, "20 chamber-minutes turn exactly one room");
            Assert.AreEqual(1, house.RoomInventory.SoiledBedCount(), "the second room waits, loudly");
        }

        [Test]
        public void Turnover_SaveLoad_RoundTrips()
        {
            var diag = new List<string>();
            var house = NewHouse(100, diag);
            Assert.Null(house.CheckInBoarder(11, "bh-room-1", 0, BoarderStayKind.Weekly, false, 100, 0, diag));
            Assert.Null(house.CheckOutBoarder(11, diag));

            var restored = new BoardingHouseShopRuntime("bh-1", new EntityIdRegistry());
            restored.LoadFromSaveDto(house.CaptureSaveDto());

            Assert.AreEqual(1, restored.RoomInventory.SoiledBedCount(), "soiled beds survive the round trip");
            Assert.NotNull(restored.CheckInBoarder(12, "bh-room-1", 0, BoarderStayKind.Weekly, false, 101, 0, diag),
                "still refused after load");
            Assert.AreEqual(1, restored.RunTurnoverCleaning(101, 0, diag));
            Assert.AreEqual(0, restored.RoomInventory.SoiledBedCount());
        }
    }
}
