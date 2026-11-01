using LandLedgers.Economy.Businesses.BoardingHouse;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W2C: the weekly rate schedule as data — room-only versus
    /// room-and-board pricing, weekly and transient terms (Canon §8.1D).
    /// The default shared-bed-plus-board weekly rate equals the shared
    /// layer's BoardingHouseWeeklyBoardCents so the two layers agree.
    /// </summary>
    [TestFixture]
    public sealed class BoardingRateScheduleTests
    {
        [Test]
        public void DefaultRates_BoardedSharedBedMatchesSharedLayerConstant()
        {
            var schedule = new BoardingRateSchedule();

            Assert.AreEqual(275, schedule.WeeklyRateCents(BoardingRoomType.SharedBed, boardIncluded: true),
                "default must equal SharedBusinessRuntimeManager.BoardingHouseWeeklyBoardCents");
            Assert.AreEqual(150, schedule.WeeklyRateCents(BoardingRoomType.SharedBed, boardIncluded: false));
            Assert.AreEqual(125, schedule.BoardAddOnWeeklyCents);
        }

        [Test]
        public void WeeklyRate_RanksRoomTypesAndAddsBoard()
        {
            var schedule = new BoardingRateSchedule();

            int shared = schedule.WeeklyRateCents(BoardingRoomType.SharedBed, false);
            int privateRoom = schedule.WeeklyRateCents(BoardingRoomType.PrivateRoom, false);
            int family = schedule.WeeklyRateCents(BoardingRoomType.FamilyRoom, false);

            Assert.Less(shared, privateRoom);
            Assert.Less(privateRoom, family);

            Assert.AreEqual(privateRoom + schedule.BoardAddOnWeeklyCents,
                schedule.WeeklyRateCents(BoardingRoomType.PrivateRoom, true));
            Assert.AreEqual(family + schedule.BoardAddOnWeeklyCents,
                schedule.WeeklyRateCents(BoardingRoomType.FamilyRoom, true));
        }

        [Test]
        public void TransientNightly_BoardCostsMore()
        {
            var schedule = new BoardingRateSchedule();

            Assert.Less(schedule.TransientNightlyRoomOnlyCents, schedule.TransientNightlyWithBoardCents);
            Assert.AreEqual(schedule.TransientNightlyRoomOnlyCents, schedule.TransientNightlyRateCents(false));
            Assert.AreEqual(schedule.TransientNightlyWithBoardCents, schedule.TransientNightlyRateCents(true));
        }

        [Test]
        public void CustomSchedule_RoundsTripsThroughSaveLoad()
        {
            var schedule = new BoardingRateSchedule(100, 200, 300, 50, 20, 35);
            var restored = new BoardingRateSchedule();
            restored.LoadFromSaveDto(schedule.CaptureSaveDto());

            Assert.AreEqual(150, restored.WeeklyRateCents(BoardingRoomType.SharedBed, true));
            Assert.AreEqual(250, restored.WeeklyRateCents(BoardingRoomType.PrivateRoom, true));
            Assert.AreEqual(35, restored.TransientNightlyRateCents(true));
            Assert.AreEqual(20, restored.TransientNightlyRateCents(false));
        }

        [Test]
        public void Rates_NeverNegative()
        {
            var schedule = new BoardingRateSchedule(-5, -5, -5, -5, -5, -5);

            Assert.AreEqual(0, schedule.WeeklyRateCents(BoardingRoomType.SharedBed, true));
            Assert.AreEqual(0, schedule.TransientNightlyRateCents(true));
        }
    }
}
