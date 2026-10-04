using System.Collections.Generic;
using LandLedgers.Economy.Businesses.BoardingHouse;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W2C: the per-instance runtime — rooms, rates, agreements, the
    /// kitchen, and nightly states run together; a day serves board meals
    /// and settles transient nights, a week settles weekly rent, and the
    /// whole instance round-trips through save/load.
    /// </summary>
    [TestFixture]
    public sealed class BoardingHouseShopRuntimeTests
    {
        private BoardingHouseShopRuntime NewHouse(int dayIndex, List<string> diag)
        {
            var registry = new EntityIdRegistry();
            var house = new BoardingHouseShopRuntime("bh-1", registry);
            house.ApplyOpeningPantryEndowment(dayIndex, diag);
            Assert.Null(house.AddRoom(BoardingRoomType.SharedBed, 2, diag));
            Assert.Null(house.AddRoom(BoardingRoomType.PrivateRoom, 1, diag));
            Assert.AreEqual("bh-1", house.BusinessInstanceId);
            return house;
        }

        [Test]
        public void CheckIn_LocksScheduleRateAtAgreement()
        {
            var diag = new List<string>();
            var house = NewHouse(100, diag);

            Assert.Null(house.CheckInBoarder(11, "bh-room-1", 0, BoarderStayKind.Weekly,
                boardIncluded: true, startDayIndex: 100, transientNights: 0, diag: diag));
            Assert.Null(house.CheckInBoarder(12, "bh-room-1", 1, BoarderStayKind.Weekly,
                boardIncluded: false, startDayIndex: 100, transientNights: 0, diag: diag));
            Assert.Null(house.CheckInBoarder(13, "bh-room-2", 0, BoarderStayKind.Transient,
                boardIncluded: true, startDayIndex: 100, transientNights: 2, diag: diag));

            Assert.AreEqual(275, house.BoarderRegister.FindRecord(11).WeeklyRateCents,
                "shared bed + board at default schedule");
            Assert.AreEqual(150, house.BoarderRegister.FindRecord(12).WeeklyRateCents,
                "shared bed room-only at default schedule");
            Assert.AreEqual(45, house.BoarderRegister.FindRecord(13).TransientNightlyRateCents,
                "transient + board at default schedule");

            Assert.AreEqual(3, house.RoomInventory.OccupiedBeds());
            StringAssert.Contains("3/3", house.BuildOccupancyLine());
        }

        [Test]
        public void SetRateSchedule_ChangesFutureAgreementsOnly()
        {
            var diag = new List<string>();
            var house = NewHouse(100, diag);
            house.CheckInBoarder(11, "bh-room-1", 0, BoarderStayKind.Weekly,
                boardIncluded: true, startDayIndex: 100, transientNights: 0, diag: diag);

            house.SetRateSchedule(new BoardingRateSchedule(200, 300, 400, 100, 30, 50), diag);
            house.CheckInBoarder(12, "bh-room-1", 1, BoarderStayKind.Weekly,
                boardIncluded: true, startDayIndex: 100, transientNights: 0, diag: diag);

            Assert.AreEqual(275, house.BoarderRegister.FindRecord(11).WeeklyRateCents,
                "existing agreement keeps its locked rate");
            Assert.AreEqual(300, house.BoarderRegister.FindRecord(12).WeeklyRateCents,
                "new agreement takes the new schedule");
        }

        [Test]
        public void ExecuteDay_ServesBoardAndSettlesTransient()
        {
            var diag = new List<string>();
            var house = NewHouse(100, diag);
            house.CheckInBoarder(11, "bh-room-1", 0, BoarderStayKind.Weekly,
                boardIncluded: true, startDayIndex: 100, transientNights: 0, diag: diag);
            house.CheckInBoarder(13, "bh-room-2", 0, BoarderStayKind.Transient,
                boardIncluded: true, startDayIndex: 100, transientNights: 1, diag: diag);

            List<BoarderRentDue> due = house.ExecuteDay(100, kitchenLaborMinutes: 480, diag: diag);

            Assert.AreEqual(2, house.MealDaySource.MealsEatenAtBoardingHouse(11, 100));
            Assert.AreEqual(2, house.MealDaySource.MealsEatenAtBoardingHouse(13, 100));
            Assert.AreEqual(1, due.Count, "the transient night is due tonight");
            Assert.AreEqual(45, due[0].CentsDue);
            Assert.IsNull(house.BoarderRegister.FindRecord(13), "the 1-night stay expired");
            Assert.IsNotNull(house.BoarderRegister.FindRecord(11), "the weekly boarder stays");
        }

        [Test]
        public void ExecuteWeek_SettlesWeeklyRent()
        {
            var diag = new List<string>();
            var house = NewHouse(100, diag);
            house.CheckInBoarder(11, "bh-room-1", 0, BoarderStayKind.Weekly,
                boardIncluded: true, startDayIndex: 100, transientNights: 0, diag: diag);

            List<BoarderRentDue> due = house.ExecuteWeek(106, diag: diag);

            Assert.AreEqual(1, due.Count);
            Assert.AreEqual(275, due[0].CentsDue);
            Assert.AreEqual(BoarderStayKind.Weekly, due[0].StayKind);
        }

        [Test]
        public void NightlyPlacement_ResolvesBoardersOnly()
        {
            var diag = new List<string>();
            var house = NewHouse(100, diag);
            house.CheckInBoarder(11, "bh-room-1", 0, BoarderStayKind.Weekly,
                boardIncluded: true, startDayIndex: 100, transientNights: 0, diag: diag);

            Assert.IsTrue(house.TryGetNightlyPlacement(11, out BoardingNightlyPlacement placement));
            Assert.AreEqual(BoardingNightlyState.BoardingBed, placement.NightlyState);
            Assert.AreEqual("bh-room-1", placement.RoomId);
            Assert.AreEqual("bh-1", placement.BusinessInstanceId);
            Assert.IsFalse(house.TryGetNightlyPlacement(99, out _));
        }

        [Test]
        public void Runtime_SaveLoad_RoundTripsEverything()
        {
            var diag = new List<string>();
            var house = NewHouse(100, diag);
            house.CheckInBoarder(11, "bh-room-1", 0, BoarderStayKind.Weekly,
                boardIncluded: true, startDayIndex: 100, transientNights: 0, diag: diag);
            house.ExecuteDay(100, 480, diag);
            house.SetRateSchedule(new BoardingRateSchedule(200, 300, 400, 100, 30, 50), diag);

            var restored = new BoardingHouseShopRuntime("bh-1", new EntityIdRegistry());
            restored.LoadFromSaveDto(house.CaptureSaveDto());

            Assert.AreEqual(1, restored.BoarderRegister.BoarderCount);
            Assert.AreEqual(275, restored.BoarderRegister.FindRecord(11).WeeklyRateCents);
            Assert.AreEqual(1, restored.RoomInventory.OccupiedBeds(), "bed assignments survive");
            Assert.AreEqual(200, restored.RateSchedule.SharedBedRoomOnlyWeeklyCents);
            Assert.AreEqual(2, restored.MealDaySource.MealsEatenAtBoardingHouse(11, 100));
            Assert.AreEqual(house.Kitchen.FoodStock.UnitsOnHand(BoardingHouseFoodSupply.MeatMaterialId),
                restored.Kitchen.FoodStock.UnitsOnHand(BoardingHouseFoodSupply.MeatMaterialId));
            Assert.IsTrue(restored.TryGetNightlyPlacement(11, out _));
        }
    }
}
