using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    [TestFixture]
    public sealed class PersonScheduleWindowFinderTests
    {
        [Test]
        public void OpenCurrentWindowIsNotBlockedByLaterAssignedActivity()
        {
            var schedules = new PersonScheduleTracker();
            Assert.IsNull(schedules.TryReserve(7, PersonActivityKind.Work, 3,
                600, 60, "future-work"));

            Assert.IsTrue(PersonScheduleWindowFinder.IsAvailableAtMinute(
                schedules, 7, 3, 540));
            Assert.AreEqual(540, PersonScheduleWindowFinder.FindNextAvailableStartMinute(
                schedules, 7, 3, 540, 15));
        }

        [Test]
        public void CommonWindowUsesReservationsInsteadOfAnyOpenTaskAssumption()
        {
            var schedules = new PersonScheduleTracker();
            Assert.IsNull(schedules.TryReserve(3, PersonActivityKind.Work, 1,
                480, 60, "glen-work"));
            Assert.IsNull(schedules.TryReserve(7, PersonActivityKind.Resting, 1,
                480, 30, "ethan-rest"));

            Assert.AreEqual(540, PersonScheduleWindowFinder.FindCommonAvailableStartMinute(
                schedules, 3, 7, 1, 480, 15));
        }

        [Test]
        public void ReturnsNoWindowWhenTheRemainingDayCannotFitTheActivity()
        {
            var schedules = new PersonScheduleTracker();
            Assert.IsNull(schedules.TryReserve(3, PersonActivityKind.Work, 1,
                480, 960, "full-day-work"));

            Assert.AreEqual(-1, PersonScheduleWindowFinder.FindNextAvailableStartMinute(
                schedules, 3, 1, 480, 1));
        }
    }
}
