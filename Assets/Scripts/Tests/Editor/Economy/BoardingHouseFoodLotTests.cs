using System.Collections.Generic;
using LandLedgers.Economy.Businesses.BoardingHouse;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W2C: the boarding-house pantry — every lot carries upstream
    /// provenance (or the explicit bootstrap flag), dispenses FIFO, and
    /// shortfalls stay shortfalls. Separate material ids from the
    /// restaurant's so the two pantries can never be confused.
    /// </summary>
    [TestFixture]
    public sealed class BoardingHouseFoodLotTests
    {
        private BoardingHouseFoodStock StockWithBootstrap(List<string> diag, out EntityIdRegistry registry)
        {
            registry = new EntityIdRegistry();
            var stock = new BoardingHouseFoodStock();
            BoardingHouseFoodBootstrap.ApplyBootstrapEndowment(stock, registry, 100, diag);
            return stock;
        }

        [Test]
        public void MaterialIds_AreDistinctFromRestaurant()
        {
            Assert.AreNotEqual("restaurant-meat", BoardingHouseFoodSupply.MeatMaterialId);
            Assert.AreNotEqual("restaurant-bread", BoardingHouseFoodSupply.BreadMaterialId);
            Assert.AreEqual("boardinghouse-meat", BoardingHouseFoodSupply.MeatMaterialId);
            Assert.AreEqual("boardinghouse-bread", BoardingHouseFoodSupply.BreadMaterialId);
            Assert.AreEqual("boardinghouse-produce", BoardingHouseFoodSupply.ProduceMaterialId);
            Assert.AreEqual("boardinghouse-dairy", BoardingHouseFoodSupply.DairyMaterialId);
        }

        [Test]
        public void ReceiveLot_RefusesOrphansAndMarksBootstrap()
        {
            var diag = new List<string>();
            var registry = new EntityIdRegistry();
            var stock = new BoardingHouseFoodStock();

            Assert.NotNull(stock.ReceiveLot(null, diag), "no conjured lots");
            Assert.NotNull(stock.ReceiveLot(new BoardingHouseFoodLot
            {
                LotId = registry.Allocate(EntityKind.Lot),
                FoodName = BoardingHouseFoodSupply.MeatMaterialId,
                Units = 10,
                AcquiredDayIndex = 100,
            }, diag), "orphan stock is refused");

            BoardingHouseFoodBootstrap.ApplyBootstrapEndowment(stock, registry, 100, diag);
            Assert.Greater(stock.UnitsOnHand(BoardingHouseFoodSupply.MeatMaterialId), 0);
            foreach (BoardingHouseFoodLot lot in stock.Lots)
            {
                Assert.IsTrue(lot.IsBootstrapEndowment);
                StringAssert.Contains("BOOTSTRAP", lot.ProvenanceChain());
            }
        }

        [Test]
        public void TryDispenseUnits_FifoAndShortfallHonest()
        {
            var diag = new List<string>();
            BoardingHouseFoodStock stock = StockWithBootstrap(diag, out _);
            int meatOnHand = stock.UnitsOnHand(BoardingHouseFoodSupply.MeatMaterialId);

            List<BoardingHouseFoodDispenseLine> lines =
                stock.TryDispenseUnits(BoardingHouseFoodSupply.MeatMaterialId, 10, 100, diag);
            int got = 0;
            foreach (BoardingHouseFoodDispenseLine line in lines) got += line.UnitsTaken;
            Assert.AreEqual(10, got);
            Assert.AreEqual(meatOnHand - 10, stock.UnitsOnHand(BoardingHouseFoodSupply.MeatMaterialId));
            StringAssert.Contains("BOOTSTRAP", lines[0].ProvenanceChain);

            // Ask for more than the pantry holds: shortfall, never invented units.
            List<BoardingHouseFoodDispenseLine> over =
                stock.TryDispenseUnits(BoardingHouseFoodSupply.MeatMaterialId, 100000, 100, diag);
            int overGot = 0;
            foreach (BoardingHouseFoodDispenseLine line in over) overGot += line.UnitsTaken;
            Assert.AreEqual(meatOnHand - 10, overGot);
            Assert.AreEqual(0, stock.UnitsOnHand(BoardingHouseFoodSupply.MeatMaterialId));
        }

        [Test]
        public void Stock_SaveLoad_RoundTripsProvenance()
        {
            var diag = new List<string>();
            BoardingHouseFoodStock stock = StockWithBootstrap(diag, out _);

            var restored = new BoardingHouseFoodStock();
            restored.LoadFromSaveDto(stock.CaptureSaveDto());

            Assert.AreEqual(stock.UnitsOnHand(BoardingHouseFoodSupply.BreadMaterialId),
                restored.UnitsOnHand(BoardingHouseFoodSupply.BreadMaterialId));
            Assert.IsTrue(restored.Lots[0].IsBootstrapEndowment);
        }
    }
}
