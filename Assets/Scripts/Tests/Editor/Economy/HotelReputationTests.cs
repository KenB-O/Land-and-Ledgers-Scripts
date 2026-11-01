using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Hotel;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// D2A: reputation as an event record (Canon §8.1G — "reputation
    /// should emerge from actual experiences"). The daily cycle posts
    /// clean-room, complaint, reliable-meal, bad-service and
    /// overcrowding-refusal events; the standing reads over the trailing
    /// 30 days and degrades or recovers with real experience. The
    /// proprietor can also record out-of-cycle events (theft/security).
    /// </summary>
    [TestFixture]
    public sealed class HotelReputationTests
    {
        private static (HotelShopRuntime, List<string>) NewHotel()
        {
            var diag = new List<string>();
            var hotel = new HotelShopRuntime("hotel-inst-rep", new EntityIdRegistry());
            hotel.AddRoom(HotelRoomClass.SingleRoom, 1, diag);
            hotel.ApplyOpeningLinenEndowment(1, diag);
            hotel.ApplyOpeningFuelEndowment(1, diag);
            return (hotel, diag);
        }

        [Test]
        public void GoodNight_PostsCleanRooms_And_StandingReadsSolid()
        {
            var (hotel, diag) = NewHotel();
            string single = hotel.RoomInventory.Rooms[0].RoomNumber;
            hotel.CheckInGuest(1301, single, 0, HotelRoomClass.SingleRoom, HotelStayKind.Nightly, 10, 1, diag);
            hotel.ExecuteDay(10, 60, diag);

            bool cleanRoomEvent = false;
            foreach (HotelExperienceEvent evt in hotel.Reputation.Events)
                if (evt.Kind == HotelExperienceKind.CleanRooms) cleanRoomEvent = true;
            Assert.IsTrue(cleanRoomEvent, "the turned-over room is an experienced fact");
            Assert.IsTrue(hotel.Reputation.Standing(10).StartsWith("Solid"), hotel.Reputation.Standing(10));
        }

        [Test]
        public void BadNights_DegradeStanding_And_GoodNights_RecoverIt()
        {
            var (hotel, diag) = NewHotel();
            // Cold house, repeated overcrowding refusals, a theft.
            for (int day = 10; day < 15; day++)
            {
                hotel.RecordReputationEvent(day, HotelExperienceKind.OvercrowdingRefusal, "turned away, full", diag);
                hotel.RecordReputationEvent(day, HotelExperienceKind.GuestComplaint, "dirty room", diag);
            }
            hotel.RecordReputationEvent(14, HotelExperienceKind.TheftOrSecurity, "room theft", diag);

            string standing = hotel.Reputation.Standing(14);
            Assert.IsTrue(standing.StartsWith("Slipping") || standing.StartsWith("Failing"), standing);
            Assert.Less(hotel.Reputation.StandingScore(14), 0);

            // Run clean days: each good day adds clean-room experience.
            for (int day = 15; day < 40; day++)
                hotel.RecordReputationEvent(day, HotelExperienceKind.CleanRooms, "clean", diag);
            Assert.Greater(hotel.Reputation.StandingScore(39), hotel.Reputation.StandingScore(14),
                "reputation recovers with real good experience");
        }

        [Test]
        public void OvercrowdingRefusal_IsRecorded_ByTravelerDemand()
        {
            var diag = new List<string>();
            var hotel = new HotelShopRuntime("hotel-inst-rep2", new EntityIdRegistry());
            hotel.AddRoom(HotelRoomClass.SingleRoom, 1, diag);
            string single = hotel.RoomInventory.Rooms[0].RoomNumber;
            hotel.CheckInGuest(1311, single, 0, HotelRoomClass.SingleRoom, HotelStayKind.Nightly, 10, 3, diag);

            var arrival = new TravelerArrival { PersonId = 1312, ArrivalDayIndex = 10, PlannedStayNights = 1 };
            HotelTravelerDemandResult result = HotelTravelerDemand.ProcessArrival(hotel, arrival, diag);
            Assert.AreEqual(HotelTravelerDemandOutcome.RefusedNoBed, result.Outcome);

            bool refusalEvent = false;
            foreach (HotelExperienceEvent evt in hotel.Reputation.Events)
                if (evt.Kind == HotelExperienceKind.OvercrowdingRefusal) refusalEvent = true;
            Assert.IsTrue(refusalEvent, "a refusal is a recorded fact, not a silently dropped customer");
        }

        [Test]
        public void PruneBefore_DropsOldEvents_And_WindowCountsAreDayScoped()
        {
            var (hotel, diag) = NewHotel();
            hotel.RecordReputationEvent(1, HotelExperienceKind.CleanRooms, "old", diag);
            hotel.RecordReputationEvent(50, HotelExperienceKind.CleanRooms, "new", diag);

            var counts = hotel.Reputation.WindowCounts(50);
            Assert.AreEqual(1, counts[HotelExperienceKind.CleanRooms], "the day-1 event aged out of the 30-day window");

            hotel.Reputation.PruneBefore(50, diag);
            Assert.AreEqual(1, hotel.Reputation.Events.Count, "pruned events leave the ledger");
        }

        [Test]
        public void SaveLoad_RoundTripsExperienceEvents()
        {
            var (hotel, diag) = NewHotel();
            hotel.RecordReputationEvent(10, HotelExperienceKind.HonoredTerms, "reservation honored", diag);

            var reloaded = new HotelShopRuntime("hotel-inst-rep", new EntityIdRegistry());
            reloaded.LoadFromSaveDto(hotel.CaptureSaveDto());
            Assert.AreEqual(1, reloaded.Reputation.Events.Count);
            Assert.AreEqual(HotelExperienceKind.HonoredTerms, reloaded.Reputation.Events[0].Kind);
        }
    }
}
