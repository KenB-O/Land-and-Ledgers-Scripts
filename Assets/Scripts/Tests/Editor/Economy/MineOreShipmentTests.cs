using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.Mine;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W8D: ore goes to the smelter via a DECLARED off-map trade link —
    /// named destination, priced per ton, real transit days. Shipments
    /// dispense real tons from the stockpile (shortfalls refused); the
    /// smelter pays on delivery, never early, never twice, never fiat.
    /// </summary>
    [TestFixture]
    public sealed class MineOreShipmentTests
    {
        private static MineOreStock StockWithTons(EntityIdRegistry registry, int tons)
        {
            var stock = new MineOreStock();
            var diag = new List<string>();
            var lot = MineOreLot.Create(registry, MineralResourceKind.Silver, tons, 2.0f,
                "shaft-1", "level-1", "vein-1", 400, "silver_ore");
            stock.ReceiveLot(lot, diag);
            return stock;
        }

        [Test]
        public void PlaceShipment_DispensesRealTonsOnDeclaredLink()
        {
            var registry = new EntityIdRegistry();
            MineOreStock stock = StockWithTons(registry, 60);
            var service = new MineOreShipmentService();

            MineOreShipmentOrder order = service.PlaceShipment(stock, "biz-mine-1", "Silver King Mine",
                "smelter-omaha-ag", 40, "shaft-1", 400);
            Assert.IsNotNull(order);
            Assert.AreEqual(40, order.Tons);
            Assert.AreEqual("Omaha Smelting Works", order.SmelterName);
            Assert.AreEqual(MineOreShipmentStatus.InTransit, order.Status);
            Assert.AreEqual(400 + 21, order.ExpectedArrivalDayIndex);
            Assert.AreEqual(40 * 1900, order.TotalCents);
            Assert.AreEqual(20, stock.TotalTons, "shipped tons left the stockpile");
        }

        [Test]
        public void PlaceShipment_RefusesUndeclaredLinkAndShortfall()
        {
            var registry = new EntityIdRegistry();
            MineOreStock stock = StockWithTons(registry, 60);
            var service = new MineOreShipmentService();

            Assert.IsNull(service.PlaceShipment(stock, "biz-mine-1", "Silver King Mine",
                "smelter-nowhere", 10, "shaft-1", 400), "undeclared destination refused");
            Assert.IsNull(service.PlaceShipment(stock, "biz-mine-1", "Silver King Mine",
                "smelter-omaha-ag", 100, "shaft-1", 400), "shortfall refused");
            Assert.AreEqual(60, stock.TotalTons, "nothing moved on refusal");
        }

        [Test]
        public void SettleDelivery_PaysOnArrivalOnly()
        {
            var registry = new EntityIdRegistry();
            MineOreStock stock = StockWithTons(registry, 60);
            var service = new MineOreShipmentService();
            MineOreShipmentOrder order = service.PlaceShipment(stock, "biz-mine-1", "Silver King Mine",
                "smelter-omaha-ag", 40, "shaft-1", 400);
            var businessCash = new HouseholdLedger(60);

            string early = service.SettleDelivery(order.OrderId, businessCash, 410);
            Assert.IsNotNull(early, "no payment before arrival");
            Assert.IsFalse(order.IsDelivered);

            string ok = service.SettleDelivery(order.OrderId, businessCash, 421);
            Assert.IsNull(ok);
            Assert.IsTrue(order.IsDelivered);
            Assert.AreEqual(421, order.DeliveredDayIndex);
            Assert.AreEqual(40 * 1900, businessCash.GetBalanceCents(), "smelter paid in full");

            string twice = service.SettleDelivery(order.OrderId, businessCash, 430);
            Assert.IsNotNull(twice, "duplicate settlement refused");
            Assert.AreEqual(40 * 1900, businessCash.GetBalanceCents(), "no double payment");
        }

        [Test]
        public void SmelterCatalog_DeclaresNamedDestinations()
        {
            Assert.IsNotNull(MineSmelterCatalog.FindLink("smelter-omaha-au"));
            Assert.IsNull(MineSmelterCatalog.FindLink("smelter-moon"));
            Assert.IsNotEmpty(MineSmelterCatalog.Links);
            foreach (MineSmelterLink link in MineSmelterCatalog.Links)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(link.SmelterName), "every link names its destination");
                Assert.Greater(link.PricePerTonCents, 0);
                Assert.Greater(link.TransitDays, 0);
            }
        }

        [Test]
        public void SaveRoundTrip_PreservesShipmentOrders()
        {
            var registry = new EntityIdRegistry();
            var state = MineRuntimeState.CreateDefault(MineralResourceKind.Silver);
            var diag = new List<string>();
            var lot = MineOreLot.Create(registry, MineralResourceKind.Silver, 60, 2.0f,
                "shaft-1", "level-1", "vein-1", 400, "silver_ore");
            state.OreStock.ReceiveLot(lot, diag);

            MineOreShipmentService service = state.CreateShipmentService();
            MineOreShipmentOrder order = service.PlaceShipment(state.OreStock, "biz-mine-1",
                "Silver King Mine", "smelter-omaha-ag", 40, "shaft-1", 400);
            Assert.IsNotNull(order);

            var dto = state.CaptureSaveDto();
            MineRuntimeState restored = MineRuntimeState.FromSaveDto(dto);

            Assert.AreEqual(1, restored.ShipmentOrders.Count);
            MineOreShipmentOrder restoredOrder = restored.ShipmentOrders[0];
            Assert.AreEqual(order.OrderId, restoredOrder.OrderId);
            Assert.AreEqual(40, restoredOrder.Tons);
            Assert.AreEqual("Omaha Smelting Works", restoredOrder.SmelterName);
            Assert.AreEqual(MineOreShipmentStatus.InTransit, restoredOrder.Status);
            Assert.AreEqual(20, restored.OreStock.TotalTons);

            // The rebuilt service continues the order numbering from the restored list.
            MineOreShipmentService restoredService = restored.CreateShipmentService();
            Assert.AreEqual(1, restoredService.Orders.Count);
        }
    }
}
