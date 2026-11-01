using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Hotel;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W3B: the hotel pantry — lots carry provenance (bootstrap, supplier,
    /// or declared import), orphan stock is refused, dispensing is FIFO and
    /// honest about shortfalls, and the hotel's material ids never mix with
    /// the boarding-house or eating-house pantries.
    /// </summary>
    [TestFixture]
    public sealed class HotelFoodLotTests
    {
        private static (HotelFoodStock, EntityIdRegistry, List<string>) NewPantry()
        {
            return (new HotelFoodStock(), new EntityIdRegistry(), new List<string>());
        }

        private static HotelFoodLot NamedLot(EntityIdRegistry registry, string foodName, int units, int day)
        {
            return new HotelFoodLot
            {
                LotId = registry.Allocate(EntityKind.Lot),
                FoodName = foodName,
                Units = units,
                AcquiredDayIndex = day,
                SupplierBusinessId = "butcher-1",
                SourceLotId = "butcher-lot-7",
                SupplierNote = "test custody chain",
            };
        }

        [Test]
        public void ReceiveLot_Refuses_OrphanAndAnonymousStock()
        {
            var (stock, registry, diag) = NewPantry();

            Assert.IsNotNull(stock.ReceiveLot(null, diag), "no lot offered");
            var noId = NamedLot(registry, HotelFoodSupply.MeatMaterialId, 5, 1);
            noId.LotId = EntityId.Invalid;
            Assert.IsNotNull(stock.ReceiveLot(noId, diag), "anonymous stock is refused");
            var orphan = NamedLot(registry, HotelFoodSupply.MeatMaterialId, 5, 1);
            orphan.SupplierBusinessId = string.Empty;
            orphan.SourceLotId = string.Empty;
            orphan.SupplierNote = string.Empty;
            Assert.IsNotNull(stock.ReceiveLot(orphan, diag), "orphan stock is refused");
            var zero = NamedLot(registry, HotelFoodSupply.MeatMaterialId, 0, 1);
            Assert.IsNotNull(stock.ReceiveLot(zero, diag), "zero units refused");

            Assert.AreEqual(0, stock.UnitsOnHand(HotelFoodSupply.MeatMaterialId));
        }

        [Test]
        public void Bootstrap_Endowment_IsFlagged_NeverAutoReplenished()
        {
            var (stock, registry, diag) = NewPantry();
            HotelFoodBootstrap.ApplyBootstrapEndowment(stock, registry, 1, diag);

            Assert.AreEqual(HotelFoodBootstrap.BootstrapMeatUnits, stock.UnitsOnHand(HotelFoodSupply.MeatMaterialId));
            Assert.AreEqual(HotelFoodBootstrap.BootstrapBreadUnits, stock.UnitsOnHand(HotelFoodSupply.BreadMaterialId));
            Assert.AreEqual(HotelFoodBootstrap.BootstrapProduceUnits, stock.UnitsOnHand(HotelFoodSupply.ProduceMaterialId));
            Assert.AreEqual(HotelFoodBootstrap.BootstrapDairyUnits, stock.UnitsOnHand(HotelFoodSupply.DairyMaterialId));

            foreach (HotelFoodLot lot in stock.Lots)
                Assert.IsTrue(lot.IsBootstrapEndowment, "every opening lot is flagged");
            Assert.IsTrue(diag.Exists(d => d.Contains("BOOTSTRAP")));
        }

        [Test]
        public void Dispense_IsFifo_HonestAboutShortfalls()
        {
            var (stock, registry, diag) = NewPantry();
            Assert.IsNull(stock.ReceiveLot(NamedLot(registry, HotelFoodSupply.BreadMaterialId, 3, 5), diag));
            Assert.IsNull(stock.ReceiveLot(NamedLot(registry, HotelFoodSupply.BreadMaterialId, 4, 2), diag));

            List<HotelFoodDispenseLine> lines = stock.TryDispenseUnits(HotelFoodSupply.BreadMaterialId, 5, 6, diag);

            Assert.AreEqual(5, TotalTaken(lines));
            Assert.AreEqual(4, lines[0].UnitsTaken, "oldest lot (day 2) dispensed first");
            Assert.AreEqual(1, lines[1].UnitsTaken, "then the day-5 lot");
            Assert.IsTrue(lines[0].ProvenanceChain.Contains("butcher-1"), "provenance rides along");
            Assert.AreEqual(2, stock.UnitsOnHand(HotelFoodSupply.BreadMaterialId));

            List<HotelFoodDispenseLine> shortfall = stock.TryDispenseUnits(HotelFoodSupply.BreadMaterialId, 10, 6, diag);
            Assert.AreEqual(2, TotalTaken(shortfall), "shortfall returns fewer lines, never invented units");
            Assert.AreEqual(0, stock.UnitsOnHand(HotelFoodSupply.BreadMaterialId));
            Assert.IsTrue(diag.Exists(d => d.Contains("shortfall")));
        }

        private static int TotalTaken(List<HotelFoodDispenseLine> lines)
        {
            int total = 0;
            foreach (HotelFoodDispenseLine line in lines) total += line.UnitsTaken;
            return total;
        }

        [Test]
        public void HotelMaterialIds_AreSeparate_FromOtherPantries()
        {
            Assert.AreNotEqual(HotelFoodSupply.MeatMaterialId, "boardinghouse-meat");
            Assert.AreNotEqual(HotelFoodSupply.BreadMaterialId, "boardinghouse-bread");
            Assert.AreNotEqual(HotelFoodSupply.ProduceMaterialId, "boardinghouse-produce");
            Assert.AreNotEqual(HotelFoodSupply.DairyMaterialId, "boardinghouse-dairy");
            Assert.AreNotEqual(HotelFoodSupply.MeatMaterialId, "restaurant-meat");
        }

        [Test]
        public void ImportArrival_Needs_NamedOrderAndOrigin()
        {
            var (stock, registry, diag) = NewPantry();

            Assert.IsNotNull(HotelFoodSupply.ReceiveImportArrival(stock, HotelFoodSupply.MeatMaterialId, 10,
                string.Empty, "origin", 3, registry, diag), "the import order must be named");
            Assert.IsNotNull(HotelFoodSupply.ReceiveImportArrival(stock, HotelFoodSupply.MeatMaterialId, 10,
                "order-9", string.Empty, 3, registry, diag), "the off-map origin must be named");
            Assert.IsNull(HotelFoodSupply.ReceiveImportArrival(stock, HotelFoodSupply.MeatMaterialId, 10,
                "order-9", "Off-map provision wholesaler, via railhead", 3, registry, diag));

            Assert.AreEqual(10, stock.UnitsOnHand(HotelFoodSupply.MeatMaterialId));
            Assert.IsTrue(stock.Lots[0].ProvenanceChain().Contains("order-9"));
        }

        [Test]
        public void ProvenanceChain_Flags_Bootstrap_And_Names_Supplier()
        {
            var (stock, registry, diag) = NewPantry();
            HotelFoodBootstrap.ApplyBootstrapEndowment(stock, registry, 1, diag);
            Assert.IsTrue(stock.Lots[0].ProvenanceChain().Contains("BOOTSTRAP"));

            var supplierLot = NamedLot(registry, HotelFoodSupply.MeatMaterialId, 5, 2);
            Assert.IsTrue(supplierLot.ProvenanceChain().Contains("butcher-1"));
            Assert.IsTrue(supplierLot.ProvenanceChain().Contains("butcher-lot-7"));

            var bare = new HotelFoodLot { LotId = registry.Allocate(EntityKind.Lot), FoodName = "x", Units = 1 };
            Assert.AreEqual("NO PROVENANCE", bare.ProvenanceChain());
        }

        [Test]
        public void SaveLoad_RoundTrips_Pantry()
        {
            var (stock, registry, diag) = NewPantry();
            HotelFoodBootstrap.ApplyBootstrapEndowment(stock, registry, 1, diag);
            stock.TryDispenseUnits(HotelFoodSupply.MeatMaterialId, 5, 2, diag);

            HotelFoodStock.HotelFoodStockSaveDto dto = stock.CaptureSaveDto();
            var restored = new HotelFoodStock();
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(HotelFoodBootstrap.BootstrapMeatUnits - 5, restored.UnitsOnHand(HotelFoodSupply.MeatMaterialId));
            Assert.AreEqual(HotelFoodBootstrap.BootstrapBreadUnits, restored.UnitsOnHand(HotelFoodSupply.BreadMaterialId));
            Assert.IsTrue(restored.Lots[0].IsBootstrapEndowment, "the flag survives the round trip");
        }
    }
}
