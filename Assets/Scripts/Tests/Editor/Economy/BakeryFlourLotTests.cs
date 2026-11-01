using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Farming.Crops;
using LandLedgers.Economy.Trade;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W2A: flour/notions lots carry upstream provenance; dispense is FIFO;
    /// shortfalls refuse loudly and never conjure stock; miller intake
    /// preserves the farm ← dealer ← mill chain.
    /// </summary>
    [TestFixture]
    public sealed class BakeryFlourLotTests
    {
        private static BakeryFlourLot ImportLot(EntityIdRegistry registry, string name, int units, int day)
        {
            return new BakeryFlourLot
            {
                LotId = registry.Allocate(EntityKind.Lot),
                FlourName = name,
                Units = units,
                AcquiredDayIndex = day,
                ImportOrderId = "IMP-1870-042",
                OriginName = "Off-map flour mill, via railhead",
                SupplierNote = "Great Plains flour mill",
            };
        }

        [Test]
        public void ReceiveLot_RefusesOrphanLot_NamesChainOrBootstrap()
        {
            var stock = new BakeryFlourStock();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            var orphan = new BakeryFlourLot
            {
                LotId = registry.Allocate(EntityKind.Lot),
                FlourName = BakeryBreadCatalog.FlourItemId,
                Units = 50,
                AcquiredDayIndex = 300,
            };
            string rejection = stock.ReceiveLot(orphan, diag);
            Assert.IsNotNull(rejection, "an orphan lot must be refused loudly");
            Assert.AreEqual(0, stock.UnitsOnHand(BakeryBreadCatalog.FlourItemId));
        }

        [Test]
        public void ReceiveLot_AcceptsMillChain_WithoutImportOrder()
        {
            var stock = new BakeryFlourStock();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            string rejection = stock.ReceiveLot(new BakeryFlourLot
            {
                LotId = registry.Allocate(EntityKind.Lot),
                FlourName = BakeryBreadCatalog.FlourItemId,
                Units = 70,
                AcquiredDayIndex = 300,
                MillerBusinessId = "miller-1",
                SourceGrainLotId = "LOT-1870-511",
                OriginName = "Miller miller-1 (CRP-3 grain chain)",
            }, diag);
            Assert.IsNull(rejection);
            Assert.AreEqual(70, stock.UnitsOnHand(BakeryBreadCatalog.FlourItemId));

            string chain = stock.Lots[0].ProvenanceChain();
            StringAssert.Contains("miller-1", chain);
            StringAssert.Contains("LOT-1870-511", chain);
        }

        [Test]
        public void ReceiveMillLot_PreservesMillerUpstreamChain()
        {
            var stock = new BakeryFlourStock();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            var millLot = new MillLot
            {
                LotId = registry.Allocate(EntityKind.Lot),
                ProductKind = "flour",
                QuantityUnits = 42,
                SourceGrainLotId = "LOT-1870-511",
                MillerBusinessId = "miller-1",
                MilledDayIndex = 298,
                MilledBy = EntityId.For(EntityKind.Person, 77),
            };

            string rejection = BakeryFlourSupply.ReceiveMillLot(stock, millLot, 299, registry, diag);
            Assert.IsNull(rejection, $"mill lot intake should succeed, got: {rejection}");
            Assert.AreEqual(42, stock.UnitsOnHand(BakeryBreadCatalog.FlourItemId));

            string chain = stock.Lots[0].ProvenanceChain();
            StringAssert.Contains("miller-1", chain, "miller named");
            StringAssert.Contains("LOT-1870-511", chain, "upstream grain lot named");
            StringAssert.DoesNotContain("BOOTSTRAP", chain, "a real mill lot is not a bootstrap endowment");
        }

        [Test]
        public void ReceiveMillLot_RefusesNonFlourProducts()
        {
            var stock = new BakeryFlourStock();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            var feedLot = new MillLot
            {
                LotId = registry.Allocate(EntityKind.Lot),
                ProductKind = "feed",
                QuantityUnits = 42,
                MillerBusinessId = "miller-1",
            };

            string rejection = BakeryFlourSupply.ReceiveMillLot(stock, feedLot, 299, registry, diag);
            Assert.IsNotNull(rejection, "mill feed is not flour — refused loudly");
            Assert.AreEqual(0, stock.UnitsOnHand(BakeryBreadCatalog.FlourItemId));
        }

        [Test]
        public void ReceiveImportArrival_RequiresNamedOrder()
        {
            var stock = new BakeryFlourStock();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            string rejection = BakeryFlourSupply.ReceiveImportArrival(
                stock, BakeryBreadCatalog.FlourItemId, 60, string.Empty,
                "Off-map flour mill, via railhead", 300, registry, diag);
            Assert.IsNotNull(rejection, "unnamed import order must be refused — no orphan stock");
            Assert.AreEqual(0, stock.UnitsOnHand(BakeryBreadCatalog.FlourItemId));

            rejection = BakeryFlourSupply.ReceiveImportArrival(
                stock, BakeryBreadCatalog.FlourItemId, 60, "IMP-1870-042",
                "Off-map flour mill, via railhead", 300, registry, diag);
            Assert.IsNull(rejection);
            Assert.AreEqual(60, stock.UnitsOnHand(BakeryBreadCatalog.FlourItemId));
        }

        [Test]
        public void TryDispenseUnits_FIFO_OldestFirst()
        {
            var stock = new BakeryFlourStock();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            stock.ReceiveLot(ImportLot(registry, BakeryBreadCatalog.FlourItemId, 30, 300), diag);
            stock.ReceiveLot(ImportLot(registry, BakeryBreadCatalog.FlourItemId, 30, 290), diag);

            var lines = stock.TryDispenseUnits(BakeryBreadCatalog.FlourItemId, 40, 301, diag);
            Assert.IsNotNull(lines);
            // Oldest lot (day 290) empties first; the day-300 lot supplies the rest.
            Assert.AreEqual(2, lines.Count);
            Assert.AreEqual(30, lines[0].UnitsTaken);
            Assert.AreEqual(10, lines[1].UnitsTaken);
            Assert.AreEqual(20, stock.UnitsOnHand(BakeryBreadCatalog.FlourItemId));
        }

        [Test]
        public void TryDispenseUnits_Shortfall_RefusesLoudly_NeverConjures()
        {
            var stock = new BakeryFlourStock();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            stock.ReceiveLot(ImportLot(registry, BakeryBreadCatalog.FlourItemId, 10, 300), diag);
            var lines = stock.TryDispenseUnits(BakeryBreadCatalog.FlourItemId, 25, 301, diag);
            Assert.IsNull(lines, "shortfall must return null — no partial conjuring");
            Assert.AreEqual(10, stock.UnitsOnHand(BakeryBreadCatalog.FlourItemId), "stock untouched on refusal");
            StringAssert.Contains("shortfall", string.Join("\n", diag).ToLower());
        }

        [Test]
        public void Bootstrap_MarksEndowment_Explicitly_NeverSilent()
        {
            var stock = new BakeryFlourStock();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            BakeryFlourBootstrap.ApplyBootstrapEndowment(stock, registry, 290, diag);
            Assert.AreEqual(BakeryFlourBootstrap.BootstrapFlourUnits,
                stock.UnitsOnHand(BakeryBreadCatalog.FlourItemId));
            Assert.AreEqual(BakeryFlourBootstrap.BootstrapNotionsUnits,
                stock.UnitsOnHand(BakeryBreadCatalog.NotionsItemId));
            Assert.IsTrue(stock.Lots[0].IsBootstrapEndowment);
            StringAssert.Contains("BOOTSTRAP", stock.Lots[0].ProvenanceChain());
            StringAssert.Contains("BOOTSTRAP", string.Join("\n", diag));
        }

        [Test]
        public void EnsureBakeryImportables_RegistersFlourAndNotions()
        {
            var diag = new List<string>();
            BakeryFlourSupply.EnsureBakeryImportables(diag);
            Assert.IsNotNull(ImportCatalog.Get(BakeryFlourSupply.FlourMaterialId),
                "flour must be a declared importable (EQU-1 path)");
            Assert.IsNotNull(ImportCatalog.Get(BakeryFlourSupply.NotionsMaterialId),
                "notions must be a declared importable (EQU-1 path)");
        }
    }
}
