using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Hotel;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// D2A: standing supply agreements for the dining-room pantry (Canon
    /// §8.1B — "Supply agreements with butchers, stores, farms or owned
    /// businesses can stabilize service through real procurement"). Orders
    /// report as due data and reorder signals fire at thresholds; neither
    /// auto-orders — the caller buys real lots with provenance. The
    /// runtime surfaces due orders as data in the daily summary.
    /// </summary>
    [TestFixture]
    public sealed class HotelStandingOrdersTests
    {
        private static (HotelShopRuntime, List<string>) NewHotel()
        {
            var diag = new List<string>();
            var hotel = new HotelShopRuntime("hotel-inst-so", new EntityIdRegistry());
            hotel.AddRoom(HotelRoomClass.SingleRoom, 1, diag);
            return (hotel, diag);
        }

        [Test]
        public void PlaceOrder_ReportsDue_And_MarksDelivered()
        {
            var (hotel, diag) = NewHotel();
            string id = hotel.StandingOrders.PlaceOrder(HotelFoodSupply.MeatMaterialId, "butcher-1", "butcher",
                "wholesale-9", 48, 7, 20, 6, diag);
            Assert.IsNotNull(id);

            var due = hotel.StandingOrders.OrdersDue(21);
            Assert.AreEqual(1, due.Count);
            Assert.AreEqual("butcher-1", due[0].SupplierBusinessId);

            Assert.IsNull(hotel.StandingOrders.MarkDelivered(id, 21, diag));
            Assert.AreEqual(0, hotel.StandingOrders.OrdersDue(21).Count, "the delivered order advances past today");
            Assert.AreEqual(29, hotel.StandingOrders.Orders[0].NextDueDayIndex, "21 + 7-day cadence");
        }

        [Test]
        public void PlaceOrder_Refuses_NamelessSupplier_And_Cancel_KeepsHistory()
        {
            var (hotel, diag) = NewHotel();
            Assert.IsNotNull(hotel.StandingOrders.PlaceOrder(HotelFoodSupply.BreadMaterialId, "", "bakery", "", 40, 7, 20, 3, diag));
            Assert.IsNotNull(hotel.StandingOrders.PlaceOrder(HotelFoodSupply.BreadMaterialId, "bakery-1", "bakery", "", 0, 7, 20, 3, diag));

            string id = hotel.StandingOrders.PlaceOrder(HotelFoodSupply.BreadMaterialId, "bakery-1", "bakery",
                "acct-3", 40, 7, 20, 3, diag);
            Assert.IsNull(hotel.StandingOrders.CancelOrder(id, 22, diag));
            Assert.AreEqual(0, hotel.StandingOrders.OrdersDue(30).Count, "cancelled orders never report due");
            Assert.AreEqual(1, hotel.StandingOrders.Orders.Count, "history stays on the books");
        }

        [Test]
        public void EvaluateSignals_FiresAtThreshold_DataNotOrders()
        {
            var (hotel, diag) = NewHotel();
            hotel.ApplyOpeningPantryEndowment(1, diag);

            // Drain the pantry below the meat threshold through the food stock directly.
            HotelFoodStock pantry = hotel.Kitchen.FoodStock;
            pantry.TryDispenseUnits(HotelFoodSupply.MeatMaterialId,
                pantry.UnitsOnHand(HotelFoodSupply.MeatMaterialId), 2, diag);

            var signals = hotel.StandingOrders.EvaluateSignals(pantry, 2, diag);
            bool meatSignal = false;
            foreach (HotelReorderSignal signal in signals)
                if (signal.FoodName == HotelFoodSupply.MeatMaterialId) meatSignal = true;
            Assert.IsTrue(meatSignal, "the drained pantry fires a reorder signal");
            Assert.AreEqual(pantry.UnitsOnHand(HotelFoodSupply.MeatMaterialId), 0,
                "signals never fulfill themselves — the pantry stays empty until the caller buys real food");
        }

        [Test]
        public void SaveLoad_RoundTripsOrdersAndPolicy()
        {
            var (hotel, diag) = NewHotel();
            hotel.StandingOrders.PlaceOrder(HotelFoodSupply.DairyMaterialId, "dairy-1", "dairy", "", 28, 7, 20, 4, diag);
            hotel.StandingOrders.ResupplyPolicy.MeatThresholdUnits = 99;

            var reloaded = new HotelShopRuntime("hotel-inst-so", new EntityIdRegistry());
            reloaded.LoadFromSaveDto(hotel.CaptureSaveDto());
            Assert.AreEqual(1, reloaded.StandingOrders.Orders.Count);
            Assert.AreEqual(99, reloaded.StandingOrders.ResupplyPolicy.MeatThresholdUnits);
        }
    }
}
