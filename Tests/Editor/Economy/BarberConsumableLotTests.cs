using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Barber;
using LandLedgers.Economy.Trade;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W1B: soap/linen lots carry upstream provenance; dispense is FIFO; shortfalls
    /// refuse loudly and never conjure stock.
    /// </summary>
    [TestFixture]
    public sealed class BarberConsumableLotTests
    {
        private static BarberConsumableLot ImportLot(EntityIdRegistry registry, string name, int units, int day)
        {
            return new BarberConsumableLot
            {
                LotId = registry.Allocate(EntityKind.Lot),
                ConsumableName = name,
                Units = units,
                AcquiredDayIndex = day,
                ImportOrderId = "IMP-1870-042",
                OriginName = "Off-map wholesale drug and toiletries house, via railhead",
                SupplierNote = "Eastern wholesale house",
            };
        }

        [Test]
        public void ReceiveLot_RefusesOrphanLot_NamesChainOrBootstrap()
        {
            var stock = new BarberConsumableStock();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            var orphan = new BarberConsumableLot
            {
                LotId = registry.Allocate(EntityKind.Lot),
                ConsumableName = BarberServiceCatalog.SoapItemId,
                Units = 10,
                AcquiredDayIndex = 300,
                IsBootstrapEndowment = false,
            };

            string rejection = stock.ReceiveLot(orphan, diag);
            Assert.NotNull(rejection, "A lot naming no import order or origin is an orphan — refused.");
            Assert.AreEqual(0, stock.Lots.Count);
        }

        [Test]
        public void ReceiveLot_AcceptsImportLot_AndRefusesDuplicates()
        {
            var stock = new BarberConsumableStock();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            Assert.Null(stock.ReceiveLot(ImportLot(registry, BarberServiceCatalog.SoapItemId, 20, 300), diag));
            Assert.AreEqual(20, stock.UnitsOnHand(BarberServiceCatalog.SoapItemId));

            var duplicate = ImportLot(registry, BarberServiceCatalog.SoapItemId, 5, 301);
            duplicate.LotId = stock.Lots[0].LotId;
            Assert.NotNull(stock.ReceiveLot(duplicate, diag), "Duplicate lot ids are refused.");
            Assert.AreEqual(20, stock.UnitsOnHand(BarberServiceCatalog.SoapItemId));
        }

        [Test]
        public void TryDispenseUnits_FifoOldestFirst_WithProvenanceLines()
        {
            var stock = new BarberConsumableStock();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            // Newer lot received first — FIFO must still take the older lot first.
            Assert.Null(stock.ReceiveLot(ImportLot(registry, BarberServiceCatalog.SoapItemId, 5, 310), diag));
            EntityId newerId = stock.Lots[0].LotId;
            Assert.Null(stock.ReceiveLot(ImportLot(registry, BarberServiceCatalog.SoapItemId, 5, 300), diag));
            EntityId olderId = stock.Lots[1].LotId;

            var lines = stock.TryDispenseUnits(BarberServiceCatalog.SoapItemId, 7, 320, diag);

            Assert.NotNull(lines);
            Assert.AreEqual(2, lines.Count);
            Assert.AreEqual(olderId, lines[0].LotId, "Oldest lot dispenses first.");
            Assert.AreEqual(5, lines[0].UnitsTaken);
            Assert.AreEqual(newerId, lines[1].LotId);
            Assert.AreEqual(2, lines[1].UnitsTaken);
            StringAssert.Contains("IMP-1870-042", lines[0].ProvenanceChain);
            Assert.AreEqual(3, stock.UnitsOnHand(BarberServiceCatalog.SoapItemId));
        }

        [Test]
        public void TryDispenseUnits_ShortfallReturnsNullLoudly_NoConjuring()
        {
            var stock = new BarberConsumableStock();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            Assert.Null(stock.ReceiveLot(ImportLot(registry, BarberServiceCatalog.LinenItemId, 2, 300), diag));

            var lines = stock.TryDispenseUnits(BarberServiceCatalog.LinenItemId, 5, 301, diag);

            Assert.Null(lines, "Shortfall dispenses nothing — no units conjured.");
            Assert.AreEqual(2, stock.UnitsOnHand(BarberServiceCatalog.LinenItemId),
                "Failed dispenses leave stock untouched.");
            Assert.Greater(diag.Count, 0, "The refusal is loud.");
        }

        [Test]
        public void TryDispenseUnits_DropsEmptiedLots()
        {
            var stock = new BarberConsumableStock();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            Assert.Null(stock.ReceiveLot(ImportLot(registry, BarberServiceCatalog.SoapItemId, 4, 300), diag));
            Assert.NotNull(stock.TryDispenseUnits(BarberServiceCatalog.SoapItemId, 4, 301, diag));

            Assert.AreEqual(0, stock.Lots.Count, "Emptied lots are dropped — no ghost stock.");
        }

        [Test]
        public void Bootstrap_AppliesMarkedOneTimeEndowment()
        {
            var stock = new BarberConsumableStock();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            BarberConsumableBootstrap.ApplyBootstrapEndowment(stock, registry, 290, diag);

            Assert.AreEqual(BarberConsumableBootstrap.BootstrapSoapUnits,
                stock.UnitsOnHand(BarberServiceCatalog.SoapItemId));
            Assert.AreEqual(BarberConsumableBootstrap.BootstrapLinenUnits,
                stock.UnitsOnHand(BarberServiceCatalog.LinenItemId));
            foreach (var lot in stock.Lots)
            {
                Assert.True(lot.IsBootstrapEndowment, "Every bootstrap lot is explicitly marked.");
                StringAssert.Contains("BOOTSTRAP", lot.ProvenanceChain());
            }
        }

        [Test]
        public void Supply_RegistersSoapAndLinenImportables_WithNamedOrigins()
        {
            var diag = new List<string>();
            BarberConsumableSupply.EnsureBarberImportables(diag);

            ImportMaterial soap = ImportCatalog.Get(BarberConsumableSupply.SoapMaterialId);
            ImportMaterial linen = ImportCatalog.Get(BarberConsumableSupply.LinenMaterialId);

            Assert.NotNull(soap, "Shaving soap must be importable.");
            Assert.NotNull(linen, "Barber linen must be importable.");
            Assert.False(string.IsNullOrWhiteSpace(soap.OriginName), "No anonymous sources.");
            Assert.False(string.IsNullOrWhiteSpace(linen.OriginName), "No anonymous sources.");
            Assert.Greater(soap.TransitDays, 1, "Transit is real days, not instant.");
        }

        [Test]
        public void Supply_ReceiveImportArrival_NamesOrderOrRefused()
        {
            var stock = new BarberConsumableStock();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            string rejection = BarberConsumableSupply.ReceiveImportArrival(
                stock, BarberServiceCatalog.SoapItemId, 12, "",
                "Off-map wholesale drug and toiletries house, via railhead",
                320, registry, diag);
            Assert.NotNull(rejection, "An unnamed import order is an orphan — refused.");
            Assert.AreEqual(0, stock.UnitsOnHand(BarberServiceCatalog.SoapItemId));

            Assert.Null(BarberConsumableSupply.ReceiveImportArrival(
                stock, BarberServiceCatalog.SoapItemId, 12, "IMP-1870-043",
                "Off-map wholesale drug and toiletries house, via railhead",
                320, registry, diag));
            Assert.AreEqual(12, stock.UnitsOnHand(BarberServiceCatalog.SoapItemId));
            Assert.False(stock.Lots[0].IsBootstrapEndowment);
            StringAssert.Contains("IMP-1870-043", stock.Lots[0].ProvenanceChain());
        }
    }
}
