using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Hotel;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W3B: the livery-side stable booking authority — stalls are authored
    /// capacity (never invented), bookings are atomic per team (never split
    /// silently), a full stable refuses loudly, checkout releases stalls,
    /// and the register round-trips through save/load.
    /// </summary>
    [TestFixture]
    public sealed class LiveryStableBookingsTests
    {
        private static LiveryStableBookings NewStable(int stalls, List<string> diag)
        {
            var stable = new LiveryStableBookings();
            Assert.IsNull(stable.AddStalls(stalls, diag));
            return stable;
        }

        private static List<EntityId> Team(params int[] animalIds)
        {
            var team = new List<EntityId>();
            foreach (int id in animalIds) team.Add(EntityId.For(EntityKind.Animal, id));
            return team;
        }

        [Test]
        public void BookStalls_AssignsRealStalls_PerNightWindow()
        {
            var diag = new List<string>();
            var stable = NewStable(4, diag);

            Assert.IsNull(stable.BookStalls(101, Team(11, 12), 10, 2, diag));
            Assert.IsTrue(stable.HasBooking(101));

            LiveryStableBooking booking = stable.Bookings[0];
            Assert.AreEqual(2, booking.AnimalIds.Count);
            Assert.AreEqual(2, booking.StallNumbers.Count);
            Assert.AreNotEqual(booking.StallNumbers[0], booking.StallNumbers[1], "one stall per animal");

            Assert.AreEqual(2, stable.StabledAnimalsOnDay(10));
            Assert.AreEqual(2, stable.StabledAnimalsOnDay(11));
            Assert.AreEqual(0, stable.StabledAnimalsOnDay(9), "before the window");
            Assert.AreEqual(0, stable.StabledAnimalsOnDay(12), "after the window");
        }

        [Test]
        public void FullStable_RefusesLoudly_NeverInventsStalls()
        {
            var diag = new List<string>();
            var stable = NewStable(2, diag);

            Assert.IsNull(stable.BookStalls(201, Team(31, 32), 10, 3, diag));
            string refusal = stable.BookStalls(202, Team(33), 11, 1, diag);

            Assert.IsNotNull(refusal, "one stall is taken nights 10-12; the second night overlaps");
            Assert.IsFalse(stable.HasBooking(202), "no invented overflow stalls");
            Assert.AreEqual(2, stable.StabledAnimalsOnDay(11), "only the real team is stabled");
            Assert.IsTrue(diag.Exists(d => d.Contains("202") && d.Contains("refused")));
        }

        [Test]
        public void Booking_IsAtomic_TeamNeverSplitSilently()
        {
            var diag = new List<string>();
            var stable = NewStable(2, diag);

            Assert.IsNull(stable.BookStalls(301, Team(41), 10, 1, diag));
            // Only 1 stall free for the window: the 2-animal team is refused whole.
            string refusal = stable.BookStalls(302, Team(42, 43), 10, 1, diag);

            Assert.IsNotNull(refusal);
            Assert.IsFalse(stable.HasBooking(302), "partial bookings are not created");
        }

        [Test]
        public void ReleaseBookings_FreesStalls()
        {
            var diag = new List<string>();
            var stable = NewStable(1, diag);

            Assert.IsNull(stable.BookStalls(401, Team(51), 10, 2, diag));
            Assert.IsNotNull(stable.BookStalls(402, Team(52), 10, 2, diag), "stable full while 401 holds the stall");

            Assert.IsTrue(stable.ReleaseBookings(401, diag));
            Assert.IsFalse(stable.HasBooking(401));
            Assert.IsNull(stable.BookStalls(402, Team(52), 10, 2, diag), "released stall is bookable again");
        }

        [Test]
        public void InvalidBookings_Refused()
        {
            var diag = new List<string>();
            var stable = NewStable(2, diag);

            Assert.IsNotNull(stable.BookStalls(0, Team(61), 10, 1, diag), "anonymous bookings are not made");
            Assert.IsNotNull(stable.BookStalls(501, new List<EntityId>(), 10, 1, diag), "empty teams do not book stalls");
            Assert.IsNotNull(stable.BookStalls(501, Team(61), 10, 0, diag), "zero-night bookings are not made");

            var fakeTeam = Team(61);
            fakeTeam.Add(new EntityId());
            Assert.IsNotNull(stable.BookStalls(501, fakeTeam, 10, 1, diag), "teams are never invented");

            Assert.IsNotNull(stable.BookStalls(502, Team(62), 10, 1, diag));
            Assert.IsNotNull(stable.BookStalls(502, Team(63), 10, 1, diag), "one traveler holds one booking");

            var noStalls = new LiveryStableBookings();
            Assert.IsNotNull(noStalls.BookStalls(503, Team(64), 10, 1, diag), "no authored stalls — capacity is real");

            Assert.IsNotNull(stable.AddStalls(0, diag), "stalls are not conjured");
            Assert.IsFalse(stable.ReleaseBookings(999, diag), "releasing a nonexistent booking returns false");
        }

        [Test]
        public void SaveLoad_RoundTrips_StallsAndBookings()
        {
            var diag = new List<string>();
            var stable = NewStable(3, diag);
            stable.BookStalls(601, Team(71, 72), 10, 2, diag);

            LiveryStableBookings.LiveryStableBookingsSaveDto dto = stable.CaptureSaveDto();
            var restored = new LiveryStableBookings();
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(3, restored.StallCount);
            Assert.IsTrue(restored.HasBooking(601));
            Assert.AreEqual(2, restored.StabledAnimalsOnDay(10));
            Assert.AreEqual(2, restored.StabledAnimalsOnDay(11));
            Assert.AreEqual(0, restored.StabledAnimalsOnDay(12));

            // A booking survives the round trip intact: the same window still blocks.
            string refusal = restored.BookStalls(602, Team(73, 74), 10, 2, diag);
            Assert.IsNotNull(refusal, "restored bookings still hold their stalls");
        }
    }
}
