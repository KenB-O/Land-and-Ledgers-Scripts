using System.Collections.Generic;
using LandLedgers.Economy.Businesses.BoardingHouse;
using LandLedgers.Economy.Businesses.Hotel;
using LandLedgers.Primitives;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W3B: hotel demand from traveler arrivals — arrivals check in as
    /// nightly guests on real beds (rate locked, nights paid up front), a
    /// full hotel refuses LOUDLY (never invented rooms), invalid arrivals
    /// are refused, and travelers' animals book livery stalls through the
    /// stable link.
    /// </summary>
    [TestFixture]
    public sealed class HotelTravelerDemandTests
    {
        private static (HotelShopRuntime, List<string>) NewHotel()
        {
            var diag = new List<string>();
            var hotel = new HotelShopRuntime("hotel-inst-1", new EntityIdRegistry());
            hotel.AddRoom(HotelRoomClass.SingleRoom, 1, diag);
            hotel.AddRoom(HotelRoomClass.DoubleRoom, 2, diag);
            hotel.ApplyOpeningLinenEndowment(1, diag);
            return (hotel, diag);
        }

        private static TravelerArrival Arrival(int personId, int nights, HotelRoomClass preferred = HotelRoomClass.SingleRoom)
        {
            return new TravelerArrival
            {
                PersonId = personId,
                ArrivalDayIndex = 10,
                FromLocationId = "rail-depot",
                Mode = TravelMode.Horseback,
                Purpose = "commercial",
                PlannedStayNights = nights,
                PreferredRoomClass = preferred,
            };
        }

        [Test]
        public void Arrival_ChecksIn_AsNightlyGuest_RateLocked()
        {
            var (hotel, diag) = NewHotel();
            HotelTravelerDemandResult result = HotelTravelerDemand.ProcessArrival(hotel, Arrival(501, 2), diag);

            Assert.AreEqual(HotelTravelerDemandOutcome.CheckedIn, result.Outcome);
            Assert.AreEqual(501, result.PersonId);
            Assert.AreEqual(2, result.NightsBooked);
            Assert.AreEqual(40, result.LockedNightlyRateCents, "single room default nightly rate");
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.RoomNumber));
            Assert.AreEqual(0, result.BedIndex);

            Assert.IsTrue(hotel.TryGetNightlyPlacement(501, out HotelNightlyPlacement placement));
            Assert.AreEqual(BoardingNightlyState.HotelRoom, placement.NightlyState);
            Assert.AreEqual(HotelStayKind.Nightly, placement.StayKind);
        }

        [Test]
        public void FullHotel_RefusesLoudly_NeverInventsRooms()
        {
            var (hotel, diag) = NewHotel();
            // 3 beds total: fill them all.
            Assert.AreEqual(HotelTravelerDemandOutcome.CheckedIn,
                HotelTravelerDemand.ProcessArrival(hotel, Arrival(601, 1), diag).Outcome);
            Assert.AreEqual(HotelTravelerDemandOutcome.CheckedIn,
                HotelTravelerDemand.ProcessArrival(hotel, Arrival(602, 1), diag).Outcome);
            Assert.AreEqual(HotelTravelerDemandOutcome.CheckedIn,
                HotelTravelerDemand.ProcessArrival(hotel, Arrival(603, 1), diag).Outcome);
            Assert.AreEqual(3, hotel.RoomInventory.OccupiedBeds());

            HotelTravelerDemandResult refused = HotelTravelerDemand.ProcessArrival(hotel, Arrival(604, 1), diag);

            Assert.AreEqual(HotelTravelerDemandOutcome.RefusedNoBed, refused.Outcome);
            Assert.IsFalse(string.IsNullOrWhiteSpace(refused.Refusal), "the refusal is loud");
            Assert.AreEqual(3, hotel.RoomInventory.OccupiedBeds(), "no room invented for the refused traveler");
            Assert.AreEqual(3, hotel.GuestRegister.GuestCount);
            Assert.IsTrue(diag.Exists(d => d.Contains("604") && d.Contains("refused")));
        }

        [Test]
        public void Arrival_PrefersClass_ThenFallsBackToAnyOpenBed()
        {
            var (hotel, diag) = NewHotel();
            // Fill the single room with a direct check-in.
            string single = hotel.RoomInventory.Rooms[0].RoomNumber;
            hotel.CheckInGuest(701, single, 0, HotelRoomClass.SingleRoom, HotelStayKind.Nightly, 10, 1, diag);

            // The arrival prefers a single; only double beds are open.
            HotelTravelerDemandResult result = HotelTravelerDemand.ProcessArrival(
                hotel, Arrival(702, 1, HotelRoomClass.SingleRoom), diag);

            Assert.AreEqual(HotelTravelerDemandOutcome.CheckedIn, result.Outcome);
            Assert.IsTrue(hotel.TryGetNightlyPlacement(702, out HotelNightlyPlacement placement));
            Assert.AreEqual(HotelRoomClass.DoubleRoom, placement.RoomClass, "fell back to the open double");
            Assert.IsTrue(diag.Exists(d => d.Contains("class fallback")), "the class change is never silent");
        }

        [Test]
        public void InvalidArrivals_Refused()
        {
            var (hotel, diag) = NewHotel();

            var anonymous = Arrival(0, 1);
            Assert.AreEqual(HotelTravelerDemandOutcome.RefusedInvalid,
                HotelTravelerDemand.ProcessArrival(hotel, anonymous, diag).Outcome, "anonymous travelers are not placed");

            var noNights = Arrival(801, 0);
            Assert.AreEqual(HotelTravelerDemandOutcome.RefusedInvalid,
                HotelTravelerDemand.ProcessArrival(hotel, noNights, diag).Outcome, "a nightly stay needs at least one paid night");

            var fakeTeam = Arrival(802, 1);
            fakeTeam.AnimalIds.Add(new EntityId()); // default = unspecified/invalid
            HotelTravelerDemandResult fakeResult = HotelTravelerDemand.ProcessArrival(hotel, fakeTeam, diag);
            Assert.AreEqual(HotelTravelerDemandOutcome.RefusedInvalid, fakeResult.Outcome, "teams are never invented");
            Assert.AreEqual(0, hotel.GuestRegister.GuestCount, "nothing checked in on an invalid arrival");

            Assert.AreEqual(HotelTravelerDemandOutcome.RefusedInvalid,
                HotelTravelerDemand.ProcessArrival(null, Arrival(803, 1), diag).Outcome, "no runtime, no placement");
        }

        [Test]
        public void Arrival_WithAnimals_BooksLiveryStalls()
        {
            var (hotel, diag) = NewHotel();
            var stable = new LiveryStableBookings();
            stable.AddStalls(4, diag);
            hotel.AttachLiveryStable(stable, diag);

            var arrival = Arrival(901, 2);
            arrival.AnimalIds.Add(EntityId.For(EntityKind.Animal, 11));
            arrival.AnimalIds.Add(EntityId.For(EntityKind.Animal, 12));

            HotelTravelerDemandResult result = HotelTravelerDemand.ProcessArrival(hotel, arrival, diag);

            Assert.AreEqual(HotelTravelerDemandOutcome.CheckedIn, result.Outcome);
            Assert.IsTrue(string.IsNullOrEmpty(result.StableBookingRefusal), "team stabled");
            Assert.IsTrue(stable.HasBooking(901));
            Assert.AreEqual(2, stable.StabledAnimalsOnDay(10), "two animal-nights recorded day 10");
            Assert.AreEqual(2, stable.StabledAnimalsOnDay(11), "two animal-nights recorded day 11");
            Assert.AreEqual(0, stable.StabledAnimalsOnDay(12), "booking ends after 2 nights");

            // Checkout releases the stalls.
            Assert.IsNull(hotel.CheckOutGuest(901, diag));
            Assert.IsFalse(stable.HasBooking(901));
        }

        [Test]
        public void Arrival_WithAnimals_FullStable_RefusesStableNotGuest()
        {
            var (hotel, diag) = NewHotel();
            var stable = new LiveryStableBookings();
            stable.AddStalls(1, diag);
            hotel.AttachLiveryStable(stable, diag);

            var first = Arrival(911, 2);
            first.AnimalIds.Add(EntityId.For(EntityKind.Animal, 21));
            Assert.AreEqual(HotelTravelerDemandOutcome.CheckedIn,
                HotelTravelerDemand.ProcessArrival(hotel, first, diag).Outcome);

            var second = Arrival(912, 2);
            second.AnimalIds.Add(EntityId.For(EntityKind.Animal, 22));
            HotelTravelerDemandResult result = HotelTravelerDemand.ProcessArrival(hotel, second, diag);

            Assert.AreEqual(HotelTravelerDemandOutcome.CheckedIn, result.Outcome, "the traveler still gets the bed");
            Assert.IsFalse(string.IsNullOrEmpty(result.StableBookingRefusal), "the stable refusal is loud");
            Assert.IsFalse(stable.HasBooking(912), "no invented stalls");
        }

        [Test]
        public void Arrivals_AreDayScoped_BatchProcessesInOrder()
        {
            var (hotel, diag) = NewHotel();
            var arrivals = new List<TravelerArrival>
            {
                Arrival(921, 1),
                new TravelerArrival { PersonId = 922, ArrivalDayIndex = 11, PlannedStayNights = 1, FromLocationId = "x" },
                Arrival(923, 1),
            };

            List<HotelTravelerDemandResult> results = HotelTravelerDemand.ProcessArrivals(hotel, arrivals, 10, diag);

            Assert.AreEqual(2, results.Count, "the day-11 arrival is not processed on day 10");
            Assert.AreEqual(921, results[0].PersonId);
            Assert.AreEqual(923, results[1].PersonId);
            Assert.AreEqual(2, hotel.GuestRegister.GuestCount);
        }

        [Test]
        public void Result_RecordsLockedCharge_ForLedgerSettlement()
        {
            var (hotel, diag) = NewHotel();
            var arrival = Arrival(931, 3, HotelRoomClass.DoubleRoom);
            HotelTravelerDemandResult result = HotelTravelerDemand.ProcessArrival(hotel, arrival, diag);

            Assert.AreEqual(HotelTravelerDemandOutcome.CheckedIn, result.Outcome);
            Assert.AreEqual(60, result.LockedNightlyRateCents, "double room default nightly rate");
            Assert.AreEqual(180, result.LockedNightlyRateCents * result.NightsBooked, "the charge the ledger authority settles");
        }
    }
}
