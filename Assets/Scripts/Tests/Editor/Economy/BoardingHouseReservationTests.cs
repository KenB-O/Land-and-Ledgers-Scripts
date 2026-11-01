using System.Collections.Generic;
using LandLedgers.Economy.Businesses.BoardingHouse;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// D1F: employer bed reservations (Canon §8.1D "reservation of rooms for
    /// expected contract or commercial customers"; §8.1F "employers ... can
    /// reserve or subsidize beds"). Holds are validated against real physical
    /// beds; reserved-but-unconsumed beds are withheld from walk-ins;
    /// reservation check-ins lock the reservation's rates.
    /// </summary>
    [TestFixture]
    public sealed class BoardingHouseReservationTests
    {
        private (BoardingHouseShopRuntime house, List<string> diag) NewHouse(int dayIndex)
        {
            var diag = new List<string>();
            var house = new BoardingHouseShopRuntime("bh-1", new EntityIdRegistry());
            house.ApplyOpeningPantryEndowment(dayIndex, diag);
            house.ApplyOpeningFuelEndowment(dayIndex, diag);
            Assert.Null(house.AddRoom(BoardingRoomType.SharedBed, 4, diag));
            Assert.Null(house.AddRoom(BoardingRoomType.PrivateRoom, 1, diag));
            return (house, diag);
        }

        private string AddMineHold(BoardingHouseShopRuntime house, List<string> diag)
        {
            return house.AddReservation("mine-1", "Black Hills Mine", BoardingRoomType.SharedBed,
                bedCount: 2, fromDayIndex: 100, toDayIndex: 130,
                reservedWeeklyRateCents: 200, reservedMonthlyRateCents: 750, reservedNightlyRateCents: 30,
                diag: diag);
        }

        [Test]
        public void Reserve_HoldsRealBeds_RefusesOverHold()
        {
            var (house, diag) = NewHouse(100);

            string reservationId = AddMineHold(house, diag);
            Assert.IsNotNull(reservationId);
            Assert.AreEqual(2, house.Reservations.ReservedBeds(BoardingRoomType.SharedBed, 110));
            Assert.AreEqual(0, house.Reservations.ReservedBeds(BoardingRoomType.SharedBed, 99), "before the window");
            Assert.AreEqual(0, house.Reservations.ReservedBeds(BoardingRoomType.SharedBed, 131), "after the window");
            Assert.AreEqual(0, house.Reservations.ReservedBeds(BoardingRoomType.PrivateRoom, 110), "other types untouched");

            Assert.IsNull(house.AddReservation("mine-2", "Other Mine", BoardingRoomType.SharedBed,
                bedCount: 3, fromDayIndex: 110, toDayIndex: 120, 200, 750, 30, diag),
                "2 held + 3 wanted > 4 physical shared beds — refused");
            Assert.IsNull(house.AddReservation("", "No Business", BoardingRoomType.SharedBed,
                1, 100, 130, 200, 750, 30, diag),
                "anonymous employer refused");
        }

        [Test]
        public void CheckIn_WalkInCannotEatReservedBeds()
        {
            var (house, diag) = NewHouse(100);
            AddMineHold(house, diag); // holds 2 of 4 shared beds

            Assert.AreEqual(2, house.WalkInOpenBeds(BoardingRoomType.SharedBed, 110));
            Assert.AreEqual(4, house.WalkInOpenBeds(BoardingRoomType.SharedBed, 99), "no hold before the window");

            Assert.Null(house.CheckInBoarder(11, "bh-room-1", 0, BoarderStayKind.Weekly, false, 100, 0, diag));
            Assert.Null(house.CheckInBoarder(12, "bh-room-1", 1, BoarderStayKind.Weekly, false, 100, 0, diag));
            Assert.NotNull(house.CheckInBoarder(13, "bh-room-1", 2, BoarderStayKind.Weekly, false, 100, 0, diag),
                "the remaining shared beds are held — walk-in refused");
        }

        [Test]
        public void CheckIn_UnderReservationLocksReservedRates()
        {
            var (house, diag) = NewHouse(100);
            string reservationId = AddMineHold(house, diag);

            Assert.Null(house.CheckInBoarder(21, "bh-room-1", 2, BoarderStayKind.Weekly, false, 105, 0, diag, reservationId));
            Assert.Null(house.CheckInBoarder(22, "bh-room-1", 3, BoarderStayKind.Monthly, false, 105, 0, diag, reservationId));

            BoarderRecord weekly = house.BoarderRegister.FindRecord(21);
            Assert.AreEqual(200, weekly.WeeklyRateCents, "the reserved (subsidized) weekly rate locks");
            Assert.AreEqual(reservationId, weekly.ReservationId);
            BoarderRecord monthly = house.BoarderRegister.FindRecord(22);
            Assert.AreEqual(750, monthly.MonthlyRateCents, "the reserved monthly rate locks");

            Assert.NotNull(house.CheckInBoarder(23, "bh-room-1", 0, BoarderStayKind.Weekly, false, 105, 0, diag, reservationId),
                "the 2-bed hold is consumed");

            // Walk-ins still get the unheld beds.
            Assert.AreEqual(2, house.WalkInOpenBeds(BoardingRoomType.SharedBed, 110));
            Assert.Null(house.CheckInBoarder(11, "bh-room-1", 0, BoarderStayKind.Weekly, false, 100, 0, diag));
            Assert.AreEqual(150, house.BoarderRegister.FindRecord(11).WeeklyRateCents,
                "walk-ins pay the schedule rate (150¢ shared room-only)");
        }

        [Test]
        public void CheckIn_ReservationRefusalsAreLoud()
        {
            var (house, diag) = NewHouse(100);
            string reservationId = AddMineHold(house, diag);

            Assert.NotNull(house.CheckInBoarder(21, "bh-room-1", 0, BoarderStayKind.Weekly, false, 105, 0, diag, "bh-res-nope"),
                "unknown reservation refused");
            Assert.NotNull(house.CheckInBoarder(21, "bh-room-1", 0, BoarderStayKind.Weekly, false, 99, 0, diag, reservationId),
                "check-in before the hold window refused");
            Assert.NotNull(house.CheckInBoarder(21, "bh-room-2", 0, BoarderStayKind.Weekly, false, 105, 0, diag, reservationId),
                "reservation holds shared beds, not the private room");
        }

        [Test]
        public void ReleaseReservation_FreesBeds_LodgedBoardersKeepRates()
        {
            var (house, diag) = NewHouse(100);
            string reservationId = AddMineHold(house, diag);
            Assert.Null(house.CheckInBoarder(21, "bh-room-1", 2, BoarderStayKind.Weekly, false, 105, 0, diag, reservationId));

            Assert.Null(house.ReleaseReservation(reservationId, 110, diag));
            Assert.AreEqual(3, house.WalkInOpenBeds(BoardingRoomType.SharedBed, 110), "held beds freed");
            Assert.AreEqual(200, house.BoarderRegister.FindRecord(21).WeeklyRateCents,
                "the lodged boarder keeps their locked reserved rate");
            Assert.NotNull(house.CheckInBoarder(22, "bh-room-1", 3, BoarderStayKind.Weekly, false, 110, 0, diag, reservationId),
                "released reservation covers no new check-ins");
        }

        [Test]
        public void Reservations_SaveLoad_RoundTrips()
        {
            var (house, diag) = NewHouse(100);
            string reservationId = AddMineHold(house, diag);
            Assert.Null(house.CheckInBoarder(21, "bh-room-1", 2, BoarderStayKind.Weekly, false, 105, 0, diag, reservationId));

            var restored = new BoardingHouseShopRuntime("bh-1", new EntityIdRegistry());
            restored.LoadFromSaveDto(house.CaptureSaveDto());

            Assert.AreEqual(2, restored.Reservations.ReservedBeds(BoardingRoomType.SharedBed, 110));
            Assert.AreEqual(reservationId, restored.BoarderRegister.FindRecord(21).ReservationId);
            Assert.AreEqual(200, restored.BoarderRegister.FindRecord(21).WeeklyRateCents);
        }
    }
}
