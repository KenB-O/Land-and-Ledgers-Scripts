using System.Collections.Generic;
using LandLedgers.Economy.Businesses.BoardingHouse;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// D1F: standing pantry supply agreements (Canon §8.1B: "Supply agreements
    /// with butchers, stores, farms or owned businesses can stabilize service
    /// through real procurement") plus threshold resupply signals. Due dates
    /// are evaluated, never auto-fulfilled; signals are facts, never orders.
    /// </summary>
    [TestFixture]
    public sealed class BoardingHouseStandingOrderTests
    {
        private (BoardingHouseShopRuntime house, List<string> diag) NewHouse(int dayIndex)
        {
            var diag = new List<string>();
            var house = new BoardingHouseShopRuntime("bh-1", new EntityIdRegistry());
            house.ApplyOpeningPantryEndowment(dayIndex, diag);
            house.ApplyOpeningFuelEndowment(dayIndex, diag);
            return (house, diag);
        }

        [Test]
        public void AddStandingOrder_RecordsAgreement_RefusesBadOrders()
        {
            var (house, diag) = NewHouse(100);

            Assert.IsNull(house.AddStandingOrder(BoardingHouseFoodSupply.BreadMaterialId, "", "bakery",
                "bakery-acct-3", 24, 2, 102, 3, diag),
                "anonymous supplier refused");
            Assert.IsNull(house.AddStandingOrder(BoardingHouseFoodSupply.BreadMaterialId, "bakery-1", "bakery",
                "bakery-acct-3", 0, 2, 102, 3, diag),
                "empty delivery refused");

            string orderId = house.AddStandingOrder(BoardingHouseFoodSupply.BreadMaterialId, "bakery-1", "bakery",
                "bakery-acct-3", 24, 2, 102, 3, diag);
            Assert.IsNotNull(orderId);

            BoardingHouseStandingOrder order = house.StandingOrders.Orders[0];
            Assert.AreEqual("bakery-1", order.SupplierBusinessId);
            Assert.AreEqual("bakery-acct-3", order.SupplierAccountId, "names the seller-side wholesale account (D1D)");
            Assert.IsFalse(order.IsDue(101), "not due before the first due day");
            Assert.IsTrue(order.IsDue(102));
        }

        [Test]
        public void EvaluateDueStandingOrders_ReportsDue_NeverFulfills()
        {
            var (house, diag) = NewHouse(100);
            string orderId = house.AddStandingOrder(BoardingHouseFoodSupply.MeatMaterialId, "butcher-1", "butcher",
                "", 30, 7, 100, 8, diag);
            Assert.IsNotNull(orderId);

            List<BoardingHouseStandingOrder> due = house.EvaluateDueStandingOrders(100, diag);
            Assert.AreEqual(1, due.Count);
            Assert.AreEqual(107, due[0].NextDueDayIndex, "the book advances honestly even when nobody acts");
            Assert.AreEqual(0, house.EvaluateDueStandingOrders(100, diag).Count, "not due twice on the same day");

            Assert.Null(house.CancelStandingOrder(orderId, 101, diag));
            Assert.AreEqual(0, house.EvaluateDueStandingOrders(107, diag).Count, "cancelled orders stay quiet");
            Assert.NotNull(house.CancelStandingOrder("bh-order-nope", 101, diag));
        }

        [Test]
        public void EvaluateResupplySignals_FiresAtThreshold_SignalOnly()
        {
            var (house, diag) = NewHouse(100);

            // Drain the bootstrap bread below its threshold (10 loaves).
            int breadOnHand = house.Kitchen.FoodStock.UnitsOnHand(BoardingHouseFoodSupply.BreadMaterialId);
            var lines = house.Kitchen.FoodStock.TryDispenseUnits(BoardingHouseFoodSupply.BreadMaterialId,
                breadOnHand, 100, diag);
            Assert.IsNotNull(lines);

            List<BoardingHouseResupplySignal> signals = house.EvaluateResupplySignals(100, diag);
            bool breadSignal = false;
            foreach (BoardingHouseResupplySignal signal in signals)
            {
                if (signal.FoodName == BoardingHouseFoodSupply.BreadMaterialId)
                {
                    breadSignal = true;
                    Assert.AreEqual(0, signal.UnitsOnHand);
                    Assert.Greater(signal.SuggestedOrderUnits, 0);
                }
            }
            Assert.IsTrue(breadSignal, "bread at zero fires its threshold signal");
            Assert.AreEqual(0, house.Kitchen.FoodStock.UnitsOnHand(BoardingHouseFoodSupply.BreadMaterialId),
                "the signal ordered nothing — stock untouched by the signal itself");
        }

        [Test]
        public void StandingOrders_SaveLoad_RoundTrips()
        {
            var (house, diag) = NewHouse(100);
            string orderId = house.AddStandingOrder(BoardingHouseFoodSupply.BreadMaterialId, "bakery-1", "bakery",
                "bakery-acct-3", 24, 2, 102, 3, diag);
            house.StandingOrders.ResupplyPolicy.BreadThresholdUnits = 5;

            var restored = new BoardingHouseShopRuntime("bh-1", new EntityIdRegistry());
            restored.LoadFromSaveDto(house.CaptureSaveDto());

            Assert.AreEqual(1, restored.StandingOrders.Orders.Count);
            Assert.AreEqual(orderId, restored.StandingOrders.Orders[0].OrderId);
            Assert.AreEqual("bakery-acct-3", restored.StandingOrders.Orders[0].SupplierAccountId);
            Assert.AreEqual(5, restored.StandingOrders.ResupplyPolicy.BreadThresholdUnits);
            Assert.AreEqual(1, restored.EvaluateDueStandingOrders(102, new List<string>()).Count);
        }
    }
}
