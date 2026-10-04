using LandLedgers.Economy.Businesses.Hotel;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W3A: nightly and weekly rates are proprietor policy data — the
    /// defaults calibrate above the boarding house (Canon §8.1C
    /// higher-paying transient guests), every class has a nightly and a
    /// weekly rate, and the schedule round-trips through save/load.
    /// </summary>
    [TestFixture]
    public sealed class HotelRateScheduleTests
    {
        [Test]
        public void Defaults_PriceAboveBoardingHouse_PerClass()
        {
            var schedule = new HotelRateSchedule();
            Assert.AreEqual(40, schedule.NightlyRateCents(HotelRoomClass.SingleRoom));
            Assert.AreEqual(60, schedule.NightlyRateCents(HotelRoomClass.DoubleRoom));
            Assert.AreEqual(100, schedule.NightlyRateCents(HotelRoomClass.ParlorSuite));
            Assert.AreEqual(220, schedule.WeeklyRateCents(HotelRoomClass.SingleRoom));
            Assert.AreEqual(330, schedule.WeeklyRateCents(HotelRoomClass.DoubleRoom));
            Assert.AreEqual(550, schedule.WeeklyRateCents(HotelRoomClass.ParlorSuite));
        }

        [Test]
        public void Proprietor_CanSetCustomRates()
        {
            var schedule = new HotelRateSchedule(50, 80, 120, 275, 440, 660);
            Assert.AreEqual(50, schedule.NightlyRateCents(HotelRoomClass.SingleRoom));
            Assert.AreEqual(120, schedule.NightlyRateCents(HotelRoomClass.ParlorSuite));
            Assert.AreEqual(660, schedule.WeeklyRateCents(HotelRoomClass.ParlorSuite));
        }

        [Test]
        public void Rates_NeverNegative()
        {
            var schedule = new HotelRateSchedule(-5, -5, -5, -5, -5, -5);
            Assert.AreEqual(0, schedule.NightlyRateCents(HotelRoomClass.SingleRoom));
            Assert.AreEqual(0, schedule.WeeklyRateCents(HotelRoomClass.DoubleRoom));
        }

        [Test]
        public void SaveLoad_RoundTrip_PreservesPolicy()
        {
            var schedule = new HotelRateSchedule(45, 70, 110, 240, 385, 605);
            var dto = schedule.CaptureSaveDto();
            var reloaded = new HotelRateSchedule();
            reloaded.LoadFromSaveDto(dto);
            Assert.AreEqual(45, reloaded.NightlyRateCents(HotelRoomClass.SingleRoom));
            Assert.AreEqual(110, reloaded.NightlyRateCents(HotelRoomClass.ParlorSuite));
            Assert.AreEqual(385, reloaded.WeeklyRateCents(HotelRoomClass.DoubleRoom));
        }
    }
}
