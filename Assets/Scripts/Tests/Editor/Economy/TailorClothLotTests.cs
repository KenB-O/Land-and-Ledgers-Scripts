using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Tailor;
using LandLedgers.Economy.Trade;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W1C: cloth/notions lots carry upstream provenance; dispense is FIFO;
    /// shortfalls refuse loudly and never conjure stock.
    /// </summary>
    [TestFixture]
    public sealed class TailorClothLotTests
    {
        private static TailorClothLot ImportLot(EntityIdRegistry registry, string name, int units, int day)
        {
            return new TailorClothLot
            {
                LotId = registry.Allocate(EntityKind.Lot),
                ClothName = name,
                Units = units,
                AcquiredDayIndex = day,
                ImportOrderId = "IMP-1870-077",
                OriginName = "Off-map dry-goods wholesaler, via railhead",
                SupplierNote = "Eastern dry-goods house",
            };
        }

        [Test]
        public void ReceiveLot_RefusesOrphanLot_NamesChainOrBootstrap()
        {
            var stock = new TailorClothStock();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            var orphan = new TailorClothLot
            {
                LotId = registry.Allocate(EntityKind.Lot),
                ClothName = TailorGarmentCatalog.ClothItemId,
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
            var stock = new TailorClothStock();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            TailorClothLot lot = ImportLot(registry, TailorGarmentCatalog.ClothItemId, 20, 300);
            Assert.Null(stock.ReceiveLot(lot, diag));
            Assert.AreEqual(1, stock.Lots.Count);

            string rejection = stock.ReceiveLot(lot, diag);
            Assert.NotNull(rejection, "The same lot twice is a duplicate — refused.");
            Assert.AreEqual(1, stock.Lots.Count);
        }

        [Test]
        public void ReceiveLot_RefusesNonPositiveUnits_AndNullLot()
        {
            var stock = new TailorClothStock();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            Assert.NotNull(stock.ReceiveLot(null, diag), "Null lot refused.");

            var zero = ImportLot(registry, TailorGarmentCatalog.ClothItemId, 0, 300);
            Assert.NotNull(stock.ReceiveLot(zero, diag), "Zero-unit lot refused.");
            Assert.AreEqual(0, stock.Lots.Count);
        }

        [Test]
        public void TryDispenseUnits_DispensesFifoOldestFirst_WithProvenanceLines()
        {
            var stock = new TailorClothStock();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            // Receive out of order: the newer lot arrives first.
            Assert.Null(stock.ReceiveLot(ImportLot(registry, TailorGarmentCatalog.ClothItemId, 10, 310), diag));
            Assert.Null(stock.ReceiveLot(ImportLot(registry, TailorGarmentCatalog.ClothItemId, 10, 300), diag));

            List<TailorClothDispenseLine> lines =
                stock.TryDispenseUnits(TailorGarmentCatalog.ClothItemId, 15, 320, diag);

            Assert.NotNull(lines, "Covered dispense must succeed.");
            Assert.AreEqual(2, lines.Count, "Dispense spans two lots.");
            Assert.AreEqual(10, lines[0].UnitsTaken, "Oldest lot (day 300) dispenses first, fully.");
            Assert.AreEqual(5, lines[1].UnitsTaken, "Newer lot (day 310) covers the remainder.");
            Assert.IsTrue(lines[0].ProvenanceChain.Contains("IMP-1870-077"),
                "Every line carries its upstream chain.");
            Assert.AreEqual(5, stock.UnitsOnHand(TailorGarmentCatalog.ClothItemId),
                "5 yards remain on the newer lot.");
        }

        [Test]
        public void TryDispenseUnits_ShortfallRefusesLoudly_ConjuresNothing()
        {
            var stock = new TailorClothStock();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            Assert.Null(stock.ReceiveLot(ImportLot(registry, TailorGarmentCatalog.ClothItemId, 2, 300), diag));

            List<TailorClothDispenseLine> lines =
                stock.TryDispenseUnits(TailorGarmentCatalog.ClothItemId, 6, 320, diag);

            Assert.Null(lines, "Shortfall dispenses nothing — no units conjured.");
            Assert.AreEqual(2, stock.UnitsOnHand(TailorGarmentCatalog.ClothItemId),
                "The shelf is untouched by a refused dispense.");
            Assert.IsTrue(diag.Count > 0, "The refusal is loud.");
        }

        [Test]
        public void TryDispenseUnits_DropsEmptiedLots_NoGhostStock()
        {
            var stock = new TailorClothStock();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            Assert.Null(stock.ReceiveLot(ImportLot(registry, TailorGarmentCatalog.ClothItemId, 5, 300), diag));
            Assert.NotNull(stock.TryDispenseUnits(TailorGarmentCatalog.ClothItemId, 5, 320, diag));

            Assert.AreEqual(0, stock.Lots.Count, "Emptied lots leave the shelf.");
            Assert.AreEqual(0, stock.UnitsOnHand(TailorGarmentCatalog.ClothItemId));
        }

        [Test]
        public void BootstrapEndowment_AppliesMarkedLots_OneTimeOpeningStock()
        {
            var stock = new TailorClothStock();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            TailorClothBootstrap.ApplyBootstrapEndowment(stock, registry, 290, diag);

            Assert.AreEqual(2, stock.Lots.Count);
            Assert.AreEqual(TailorClothBootstrap.BootstrapClothYards,
                stock.UnitsOnHand(TailorGarmentCatalog.ClothItemId));
            Assert.AreEqual(TailorClothBootstrap.BootstrapNotionsUnits,
                stock.UnitsOnHand(TailorGarmentCatalog.NotionsItemId));
            foreach (var lot in stock.Lots)
            {
                Assert.IsTrue(lot.IsBootstrapEndowment, "Bootstrap lots are explicitly marked.");
                Assert.IsTrue(lot.ProvenanceChain().StartsWith("BOOTSTRAP"),
                    "The chain names the endowment, never a fake supplier.");
            }
        }

        [Test]
        public void ProvenanceChain_NamesOrderOriginAndNote()
        {
            var registry = new EntityIdRegistry();
            TailorClothLot lot = ImportLot(registry, TailorGarmentCatalog.ClothItemId, 10, 300);

            string chain = lot.ProvenanceChain();
            Assert.IsTrue(chain.Contains("IMP-1870-077"), "Chain names the import order.");
            Assert.IsTrue(chain.Contains("Off-map dry-goods wholesaler, via railhead"), "Chain names the origin.");
            Assert.IsTrue(chain.Contains("Eastern dry-goods house"), "Chain names the supplier note.");
        }

        [Test]
        public void EnsureTailorImportables_RegistersClothAndNotions()
        {
            var diag = new List<string>();
            TailorClothSupply.EnsureTailorImportables(diag);

            ImportMaterial cloth = ImportCatalog.Get(TailorClothSupply.ClothMaterialId);
            ImportMaterial notions = ImportCatalog.Get(TailorClothSupply.NotionsMaterialId);

            Assert.NotNull(cloth, "Cloth registers as an importable material.");
            Assert.NotNull(notions, "Notions register as an importable material.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(cloth.OriginName), "Cloth names its origin.");
            Assert.Greater(cloth.TransitDays, 1, "Transit is real days, not instant.");
        }

        [Test]
        public void ReceiveImportArrival_RecordsCustodyWithProvenance()
        {
            var stock = new TailorClothStock();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            string rejection = TailorClothSupply.ReceiveImportArrival(
                stock, TailorGarmentCatalog.ClothItemId, 12,
                "IMP-1870-078", "Off-map dry-goods wholesaler, via railhead",
                330, registry, diag);

            Assert.Null(rejection);
            Assert.AreEqual(12, stock.UnitsOnHand(TailorGarmentCatalog.ClothItemId));
            Assert.IsTrue(stock.Lots[0].ProvenanceChain().Contains("IMP-1870-078"));

            string noOrder = TailorClothSupply.ReceiveImportArrival(
                stock, TailorGarmentCatalog.ClothItemId, 12,
                string.Empty, "Off-map dry-goods wholesaler, via railhead",
                330, registry, diag);
            Assert.NotNull(noOrder, "An arrival naming no order is refused — no orphan stock.");
        }

        [Test]
        public void SaveLoad_RoundTripsLots()
        {
            var stock = new TailorClothStock();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();
            TailorClothBootstrap.ApplyBootstrapEndowment(stock, registry, 290, diag);

            var dto = stock.CaptureSaveDto();
            var restored = new TailorClothStock();
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(stock.UnitsOnHand(TailorGarmentCatalog.ClothItemId),
                restored.UnitsOnHand(TailorGarmentCatalog.ClothItemId));
            Assert.AreEqual(stock.UnitsOnHand(TailorGarmentCatalog.NotionsItemId),
                restored.UnitsOnHand(TailorGarmentCatalog.NotionsItemId));
            Assert.IsTrue(restored.Lots[0].IsBootstrapEndowment, "Bootstrap flags survive save/load.");
        }
    }
}
