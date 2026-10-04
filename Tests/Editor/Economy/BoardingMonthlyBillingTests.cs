using System.Collections.Generic;
using LandLedgers.Economy.Businesses.BoardingHouse;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// D1F: longer-stay monthly agreements (Canon §8.1D "weekly or longer-stay
    /// terms"; §8.1C boarders who stay "for weeks or months"). The monthly
    /// rate locks at check-in like the weekly rate; the monthly settlement
    /// reports who owes what and the caller settles through ledger
    /// authorities — no credit, no tabs (design fork boundary, D1E
    /// precedent).
    /// </summary>
    [TestFixture]
    public sealed class BoardingMonthlyBillingTests
    {
        private (BoardingHouseShopRuntime house, List<string> diag) NewHouse(int dayIndex)
        {
            var diag = new List<string>();
            var house = new BoardingHouseShopRuntime("bh-1", new EntityIdRegistry());
            house.ApplyOpeningPantryEndowment(dayIndex, diag);
            house.ApplyOpeningFuelEndowment(dayIndex, diag);
            Assert.Null(house.AddRoom(BoardingRoomType.SharedBed, 2, diag));
            Assert.Null(house.AddRoom(BoardingRoomType.PrivateRoom, 1, diag));
            return (house, diag);
        }

        [Test]
        public void CheckIn_LocksMonthlyRateAtAgreement()
        {
            var (house, diag) = NewHouse(100);

            Assert.Null(house.CheckInBoarder(11, "bh-room-1", 0, BoarderStayKind.Monthly,
                boardIncluded: true, startDayIndex: 100, transientNights: 0, diag: diag));

            BoarderRecord record = house.BoarderRegister.FindRecord(11);
            Assert.IsNotNull(record);
            Assert.AreEqual(BoarderStayKind.Monthly, record.StayKind);
            // Default schedule: 600¢ shared-bed room-only + 500¢ monthly board add-on.
            Assert.AreEqual(1100, record.MonthlyRateCents);
            Assert.AreEqual(0, record.WeeklyRateCents, "the weekly rate is not the agreement");
        }

        [Test]
        public void CheckIn_MonthlyRateFollowsScheduleChange()
        {
            var (house, diag) = NewHouse(100);
            house.SetRateSchedule(new BoardingRateSchedule(
                150, 250, 400, 125, 25, 45,
                sharedBedRoomOnlyMonthlyCents: 550, privateRoomOnlyMonthlyCents: 900,
                familyRoomOnlyMonthlyCents: 1400, monthlyBoardAddOnCents: 450), diag);

            Assert.Null(house.CheckInBoarder(11, "bh-room-1", 0, BoarderStayKind.Monthly, true, 100, 0, diag));
            Assert.AreEqual(1000, house.BoarderRegister.FindRecord(11).MonthlyRateCents,
                "proprietor-set monthly policy prices the agreement");

            house.SetRateSchedule(new BoardingRateSchedule(150, 250, 400, 125, 25, 45), diag);
            Assert.Null(house.CheckInBoarder(12, "bh-room-1", 1, BoarderStayKind.Monthly, true, 100, 0, diag));
            Assert.AreEqual(1000, house.BoarderRegister.FindRecord(11).MonthlyRateCents,
                "the first agreement keeps its locked rate after the schedule changes");
        }

        [Test]
        public void ExecuteMonth_SettlesMonthlyRentOnly()
        {
            var (house, diag) = NewHouse(100);
            Assert.Null(house.CheckInBoarder(11, "bh-room-1", 0, BoarderStayKind.Monthly, true, 100, 0, diag));
            Assert.Null(house.CheckInBoarder(12, "bh-room-1", 1, BoarderStayKind.Weekly, true, 100, 0, diag));
            Assert.Null(house.CheckInBoarder(13, "bh-room-2", 0, BoarderStayKind.Transient, true, 100, 2, diag));

            List<BoarderRentDue> due = house.ExecuteMonth(130, diag);

            Assert.AreEqual(1, due.Count, "only the monthly boarder is in the monthly settlement");
            Assert.AreEqual(11, due[0].PersonId);
            Assert.AreEqual(1100, due[0].CentsDue);
            Assert.AreEqual(BoarderStayKind.Monthly, due[0].StayKind);
            Assert.AreEqual(BoardingHouseBilling.MonthlyBillingDays, due[0].NightsCovered);
            Assert.IsTrue(due[0].BoardIncluded);

            List<BoarderRentDue> weekly = house.ExecuteWeek(130, diag);
            Assert.AreEqual(1, weekly.Count, "the monthly boarder is not in the weekly settlement");
            Assert.AreEqual(12, weekly[0].PersonId);
        }

        [Test]
        public void MonthlyBillingDays_IsThirtyDayCycle()
        {
            Assert.AreEqual(30, BoardingHouseBilling.MonthlyBillingDays);
        }

        [Test]
        public void Monthly_SaveLoad_RoundTrips()
        {
            var (house, diag) = NewHouse(100);
            Assert.Null(house.CheckInBoarder(11, "bh-room-1", 0, BoarderStayKind.Monthly, true, 100, 0, diag));

            var restored = new BoardingHouseShopRuntime("bh-1", new EntityIdRegistry());
            restored.LoadFromSaveDto(house.CaptureSaveDto());

            BoarderRecord record = restored.BoarderRegister.FindRecord(11);
            Assert.IsNotNull(record);
            Assert.AreEqual(BoarderStayKind.Monthly, record.StayKind);
            Assert.AreEqual(1100, record.MonthlyRateCents);

            List<BoarderRentDue> due = restored.ExecuteMonth(130, new List<string>());
            Assert.AreEqual(1, due.Count);
            Assert.AreEqual(1100, due[0].CentsDue);
        }
    }
}
