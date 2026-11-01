using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Restaurant;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// D1E: buyer-side procurement (Canon §8.1B supply agreements, §7.3E) —
    /// standing ingredient orders and pantry reorder signals. Signals never
    /// order; due orders never auto-fulfill: the caller places real orders
    /// and the sellers move real goods.
    /// </summary>
    [TestFixture]
    public sealed class RestaurantResupplyTests
    {
        private static RestaurantShopRuntime StockedShop(List<string> diag, int day = 300)
        {
            var runtime = new RestaurantShopRuntime("rest-biz-1");
            var registry = new EntityIdRegistry();
            RestaurantFoodBootstrap.ApplyBootstrapEndowment(runtime.FoodStock, registry, day, diag);
            RestaurantFuelBootstrap.ApplyBootstrapEndowment(runtime.FuelStock, registry, day, diag);
            return runtime;
        }

        private static RestaurantStandingOrder BreadOrder(int nextDue)
        {
            return new RestaurantStandingOrder
            {
                OrderId = "rest-bread-weekly",
                FoodName = RestaurantMealCatalog.BreadItemId,
                SupplierBusinessId = "bakery-biz-1",
                SupplierKind = "bakery",
                SupplierAccountId = "bakery-wholesale-7", // the bakery's wholesale account id (D1D)
                UnitsPerDelivery = 24,
                CadenceDays = 7,
                NextDueDayIndex = nextDue,
                PricePerUnitCents = 8,
                IsActive = true,
            };
        }

        [Test]
        public void FullPantry_NoSignals()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);

            var signals = runtime.EvaluateResupplySignals(300, diag);

            Assert.AreEqual(0, signals.Count, "the bootstrap pantry sits above every threshold");
        }

        [Test]
        public void DrainedPantry_SignalsWithSuggestedOrder()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);
            // Drain meat from 40 lb to 5 lb (threshold 10).
            Assert.NotNull(runtime.FoodStock.TryDispenseUnits(RestaurantMealCatalog.MeatItemId, 35, 300, diag));

            var signals = runtime.EvaluateResupplySignals(300, diag);

            Assert.AreEqual(1, signals.Count);
            Assert.AreEqual(RestaurantMealCatalog.MeatItemId, signals[0].FoodName);
            Assert.AreEqual(5, signals[0].UnitsOnHand);
            Assert.AreEqual(30, signals[0].SuggestedOrderUnits);
            Assert.AreEqual(RestaurantResupplyEvaluator.ImportLeadTimeDays, signals[0].LeadTimeDays);
            StringAssert.Contains("never self-replenishes", signals[0].Reason);
        }

        [Test]
        public void AddStandingOrder_ValidatesLoudly()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);

            Assert.NotNull(runtime.AddStandingOrder(null, diag), "null refused");
            Assert.NotNull(runtime.AddStandingOrder(new RestaurantStandingOrder
            {
                FoodName = RestaurantMealCatalog.BreadItemId,
                SupplierBusinessId = "bakery-biz-1",
                UnitsPerDelivery = 24,
                CadenceDays = 7,
            }, diag), "an order without an id is refused");
            Assert.NotNull(runtime.AddStandingOrder(new RestaurantStandingOrder
            {
                OrderId = "o-1",
                FoodName = RestaurantMealCatalog.BreadItemId,
                UnitsPerDelivery = 24,
                CadenceDays = 7,
            }, diag), "an order without a named supplier is refused — no orphan orders");

            Assert.Null(runtime.AddStandingOrder(BreadOrder(300), diag));
            Assert.AreEqual(1, runtime.StandingOrders.Count);
            Assert.NotNull(runtime.AddStandingOrder(BreadOrder(307), diag), "duplicate order ids are not duplicated");
            Assert.AreEqual(1, runtime.StandingOrders.Count);
        }

        [Test]
        public void DueStandingOrders_OnlyDue_PlacedAdvancesSchedule()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);
            Assert.Null(runtime.AddStandingOrder(BreadOrder(307), diag));

            Assert.AreEqual(0, runtime.DueStandingOrders(300, diag).Count, "not due yet");
            var due = runtime.DueStandingOrders(307, diag);
            Assert.AreEqual(1, due.Count);
            Assert.AreEqual("bakery-wholesale-7", due[0].SupplierAccountId,
                "the order names the seller-side agreement (D1D bakery wholesale account)");

            Assert.Null(runtime.MarkStandingOrderPlaced("rest-bread-weekly", 307, diag));
            Assert.AreEqual(314, runtime.StandingOrders[0].NextDueDayIndex, "next due advances by the cadence");
            Assert.AreEqual(0, runtime.DueStandingOrders(307, diag).Count, "placed orders are not due twice");

            Assert.NotNull(runtime.MarkStandingOrderPlaced("no-such-order", 307, diag),
                "advancing an unknown order is refused");
        }

        [Test]
        public void InactiveOrder_NeverDue()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);
            var order = BreadOrder(300);
            order.IsActive = false;
            Assert.Null(runtime.AddStandingOrder(order, diag));

            Assert.AreEqual(0, runtime.DueStandingOrders(300, diag).Count);
        }

        [Test]
        public void ResupplyPolicy_IsSettablePerShop()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);
            runtime.ResupplyPolicy.MeatThresholdUnits = 50; // nervous owner

            var signals = runtime.EvaluateResupplySignals(300, diag);

            Assert.AreEqual(1, signals.Count, "40 lb of meat trips a 50 lb threshold");
            Assert.AreEqual(RestaurantMealCatalog.MeatItemId, signals[0].FoodName);
        }

        [Test]
        public void StandingOrders_RoundTripSaveLoad()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);
            Assert.Null(runtime.AddStandingOrder(BreadOrder(307), diag));
            runtime.ResupplyPolicy.MeatThresholdUnits = 50; // customized by the owner

            var dto = runtime.CaptureSaveDto();
            var restored = new RestaurantShopRuntime("rest-biz-1");
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(1, restored.StandingOrders.Count, "standing orders survive save/load");
            Assert.AreEqual("bakery-biz-1", restored.StandingOrders[0].SupplierBusinessId);
            Assert.AreEqual(307, restored.StandingOrders[0].NextDueDayIndex);
            Assert.AreEqual(50, restored.ResupplyPolicy.MeatThresholdUnits,
                "a customized resupply policy survives save/load");
        }
    }
}
