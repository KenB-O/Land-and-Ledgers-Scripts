using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Hotel;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W3B: the hotel dining room — board policy decides who eats (default:
    /// nightly transients), meals come from real pantry lots with
    /// provenance under a real labor budget, the shelf ages honestly
    /// (day-old meals are waste, never served), shortfalls are service
    /// failures logged loudly, and every served meal feeds the nutrition
    /// link for a real person.
    /// </summary>
    [TestFixture]
    public sealed class HotelKitchenTests
    {
        private static (HotelKitchen, HotelShopRuntime, List<string>) NewHotelWithKitchen()
        {
            var diag = new List<string>();
            var hotel = new HotelShopRuntime("hotel-inst-1", new EntityIdRegistry());
            hotel.AddRoom(HotelRoomClass.SingleRoom, 1, diag);
            hotel.AddRoom(HotelRoomClass.DoubleRoom, 2, diag);
            hotel.ApplyOpeningLinenEndowment(1, diag);
            hotel.ApplyOpeningPantryEndowment(1, diag);
            var kitchen = hotel.Kitchen;
            return (kitchen, hotel, diag);
        }

        private static void CheckInNightly(HotelShopRuntime hotel, int personId, int nights, List<string> diag)
        {
            string room = hotel.RoomInventory.Rooms[0].RoomNumber;
            Assert.IsNull(hotel.CheckInGuest(personId, room, 0, HotelRoomClass.SingleRoom,
                HotelStayKind.Nightly, 10, nights, diag));
        }

        [Test]
        public void ExecuteDay_ServesBoardEligibleGuests_TwoMeals()
        {
            var (kitchen, hotel, diag) = NewHotelWithKitchen();
            CheckInNightly(hotel, 1001, 2, diag);
            CheckInNightly(hotel, 1002, 2, diag);

            int fullyServed = kitchen.ExecuteDay(hotel.GuestRegister, 10, 200, diag);

            Assert.AreEqual(2, fullyServed);
            Assert.AreEqual(4, kitchen.MealLedger.ServedMealsOnDay(10), "2 guests x 2 meals");
            Assert.AreEqual(2, kitchen.MealLedger.MealsEatenAtHotel(1001, 10));
            Assert.AreEqual(2, kitchen.MealLedger.MealsEatenAtHotel(1002, 10));
            Assert.AreEqual(4, kitchen.ServedMeals.Count);
            Assert.IsTrue(kitchen.ServedMeals[0].ProvenanceChain.Contains("BOOTSTRAP"),
                "opening pantry provenance rides into the served meal");
        }

        [Test]
        public void BoardPolicy_NightlyGuestsOnly_ExcludesWeeklyGuests()
        {
            var (kitchen, hotel, diag) = NewHotelWithKitchen();
            CheckInNightly(hotel, 1101, 2, diag);
            string room = hotel.RoomInventory.Rooms[1].RoomNumber;
            Assert.IsNull(hotel.CheckInGuest(1102, room, 0, HotelRoomClass.DoubleRoom,
                HotelStayKind.Weekly, 10, 0, diag));

            kitchen.ExecuteDay(hotel.GuestRegister, 10, 200, diag);

            Assert.AreEqual(2, kitchen.MealLedger.MealsEatenAtHotel(1101, 10));
            Assert.AreEqual(0, kitchen.MealLedger.MealsEatenAtHotel(1102, 10),
                "the weekly long-stay guest keeps their own meal arrangements");
        }

        [Test]
        public void BoardPolicy_AllGuests_FeedsEveryone_NoBoard_FeedsNoOne()
        {
            var (kitchen, hotel, diag) = NewHotelWithKitchen();
            CheckInNightly(hotel, 1201, 2, diag);
            string room = hotel.RoomInventory.Rooms[1].RoomNumber;
            hotel.CheckInGuest(1202, room, 0, HotelRoomClass.DoubleRoom, HotelStayKind.Weekly, 10, 0, diag);

            kitchen.BoardPolicy = HotelBoardPolicy.AllGuests;
            kitchen.ExecuteDay(hotel.GuestRegister, 10, 200, diag);
            Assert.AreEqual(2, kitchen.MealLedger.MealsEatenAtHotel(1202, 10));

            var diag2 = new List<string>();
            var hotel2 = new HotelShopRuntime("hotel-inst-2", new EntityIdRegistry());
            hotel2.AddRoom(HotelRoomClass.SingleRoom, 1, diag2);
            hotel2.ApplyOpeningPantryEndowment(1, diag2);
            string room2 = hotel2.RoomInventory.Rooms[0].RoomNumber;
            hotel2.CheckInGuest(1203, room2, 0, HotelRoomClass.SingleRoom, HotelStayKind.Nightly, 10, 1, diag2);
            hotel2.Kitchen.BoardPolicy = HotelBoardPolicy.NoBoard;
            int served = hotel2.Kitchen.ExecuteDay(hotel2.GuestRegister, 10, 200, diag2);
            Assert.AreEqual(0, served, "room-only house: the dining room stays dark");
            Assert.AreEqual(0, hotel2.Kitchen.MealLedger.ServedMealsOnDay(10));
        }

        [Test]
        public void Shelf_AgesHonestly_DayOldMealsAreWaste()
        {
            // Unit behavior: a prepared meal is servable its own day, waste the next.
            var lot = new HotelMealLot { MealId = "hotel-breakfast", PreparedDayIndex = 10, MealsRemaining = 1 };
            lot.AgeToDay(10);
            Assert.IsFalse(lot.Spoiled);
            Assert.IsTrue(lot.CanServe);
            Assert.AreEqual(1, lot.TakeMeals(1));
            lot.AgeToDay(11);
            Assert.IsTrue(lot.Spoiled);
            Assert.IsFalse(lot.CanServe, "day-old meals are waste — never served");
            Assert.AreEqual(0, lot.TakeMeals(1));

            // Kitchen level: day 11 preps fresh for the continuing guest —
            // yesterday's shelf never feeds today.
            var (kitchen, hotel, diag) = NewHotelWithKitchen();
            CheckInNightly(hotel, 1301, 3, diag);
            kitchen.ExecuteDay(hotel.GuestRegister, 10, 200, diag);
            Assert.AreEqual(0, kitchen.MealShelf.Count, "a fully served day leaves no shelf");

            int servedDay11 = kitchen.ExecuteDay(hotel.GuestRegister, 11, 200, diag);
            Assert.AreEqual(1, servedDay11);
            Assert.AreEqual(2, kitchen.MealLedger.MealsEatenAtHotel(1301, 11));
        }

        [Test]
        public void Shortfalls_AreServiceFailures_LoggedLoudly()
        {
            var diag = new List<string>();
            var hotel = new HotelShopRuntime("hotel-inst-3", new EntityIdRegistry());
            hotel.AddRoom(HotelRoomClass.SingleRoom, 1, diag);
            // No pantry endowment: the shelf is empty.
            string room = hotel.RoomInventory.Rooms[0].RoomNumber;
            hotel.CheckInGuest(1401, room, 0, HotelRoomClass.SingleRoom, HotelStayKind.Nightly, 10, 1, diag);

            int served = hotel.Kitchen.ExecuteDay(hotel.GuestRegister, 10, 200, diag);

            Assert.AreEqual(0, served);
            Assert.AreEqual(0, hotel.Kitchen.MealLedger.ServedMealsOnDay(10));
            Assert.IsTrue(diag.Exists(d => d.Contains("shorted") || d.Contains("shortfall")),
                "board guests going hungry is a logged service failure, never papered over");
        }

        [Test]
        public void KitchenLabor_Budget_IsReal()
        {
            var (kitchen, hotel, diag) = NewHotelWithKitchen();
            CheckInNightly(hotel, 1501, 2, diag);

            // Breakfast needs 10 min/meal: 5 minutes cannot prep even one.
            int served = kitchen.ExecuteDay(hotel.GuestRegister, 10, 5, diag);
            Assert.AreEqual(0, served);
            Assert.IsTrue(diag.Exists(d => d.Contains("shorted by kitchen labor")));
        }

        [Test]
        public void ServedMealRecords_CarryGuest_Provenance_AndDay()
        {
            var (kitchen, hotel, diag) = NewHotelWithKitchen();
            CheckInNightly(hotel, 1601, 2, diag);
            kitchen.ExecuteDay(hotel.GuestRegister, 10, 200, diag);

            Assert.AreEqual(2, kitchen.ServedMeals.Count);
            foreach (HotelServedMealRecord record in kitchen.ServedMeals)
            {
                Assert.AreEqual(1601, record.PersonId, "nutrition credit goes to a real guest");
                Assert.AreEqual(10, record.DayIndex);
                Assert.IsFalse(string.IsNullOrWhiteSpace(record.ProvenanceChain));
            }
        }

        [Test]
        public void Runtime_ExecuteDay_RunsKitchen_WithLabor()
        {
            var (kitchenUnused, hotel, diag) = NewHotelWithKitchen();
            CheckInNightly(hotel, 1701, 2, diag);

            hotel.ExecuteDay(10, 60, diag, kitchenLaborMinutes: 200);

            Assert.AreEqual(2, hotel.Kitchen.MealLedger.MealsEatenAtHotel(1701, 10),
                "the runtime's day runs the dining room when kitchen labor is allocated");
        }

        [Test]
        public void SaveLoad_RoundTrips_Kitchen()
        {
            var (kitchen, hotel, diag) = NewHotelWithKitchen();
            CheckInNightly(hotel, 1801, 2, diag);
            kitchen.BoardPolicy = HotelBoardPolicy.AllGuests;
            kitchen.ExecuteDay(hotel.GuestRegister, 10, 200, diag);

            HotelKitchen.HotelKitchenSaveDto dto = kitchen.CaptureSaveDto();
            var restored = new HotelKitchen(new EntityIdRegistry());
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(HotelBoardPolicy.AllGuests, restored.BoardPolicy);
            Assert.AreEqual(2, restored.MealLedger.MealsEatenAtHotel(1801, 10));
            Assert.AreEqual(2, restored.ServedMeals.Count);
        }
    }
}
