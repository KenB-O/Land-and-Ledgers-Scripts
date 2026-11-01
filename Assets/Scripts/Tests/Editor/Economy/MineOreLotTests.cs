using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.Mine;
using LandLedgers.Primitives;
using LandLedgers.World;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W8B: mined ore becomes real lots with grade/assayer provenance.
    /// Orphan lots (no shaft/level) are refused loudly; dispense is FIFO and
    /// refuses shortfalls without conjuring stock.
    /// </summary>
    [TestFixture]
    public sealed class MineOreLotTests
    {
        private static EntityIdRegistry NewRegistry() => new EntityIdRegistry();

        private static MineOreLot NewLot(EntityIdRegistry registry, int tons, float grade,
            string shaftId = "shaft-1", string levelId = "level-1")
        {
            return MineOreLot.Create(registry, MineralResourceKind.Silver, tons, grade,
                shaftId, levelId, "vein-1", 400, MineRuntimeState.GetStockpileCategoryId(MineralResourceKind.Silver));
        }

        [Test]
        public void ReceiveLot_AcceptsWorkingProvenance()
        {
            var registry = NewRegistry();
            var stock = new MineOreStock();
            var diag = new List<string>();

            Assert.IsNull(stock.ReceiveLot(NewLot(registry, 40, 2.5f), diag));
            Assert.AreEqual(40, stock.TotalTons);
        }

        [Test]
        public void ReceiveLot_RefusesOrphanLot()
        {
            var registry = NewRegistry();
            var stock = new MineOreStock();
            var diag = new List<string>();

            var orphan = MineOreLot.Create(registry, MineralResourceKind.Gold, 10, 1.2f,
                string.Empty, string.Empty, string.Empty, 400, "gold_ore");
            string rejection = stock.ReceiveLot(orphan, diag);
            Assert.IsNotNull(rejection, "ore with no shaft/level provenance must be refused");
            Assert.AreEqual(0, stock.TotalTons);
        }

        [Test]
        public void ReceiveLot_RefusesZeroTonsAndDuplicates()
        {
            var registry = NewRegistry();
            var stock = new MineOreStock();
            var diag = new List<string>();

            var empty = NewLot(registry, 0, 1.0f);
            Assert.IsNotNull(stock.ReceiveLot(empty, diag), "zero-ton lot refused");

            var lot = NewLot(registry, 25, 1.0f);
            Assert.IsNull(stock.ReceiveLot(lot, diag));
            Assert.IsNotNull(stock.ReceiveLot(lot, diag), "duplicate lot refused");
            Assert.AreEqual(25, stock.TotalTons);
        }

        [Test]
        public void DispenseTons_IsFifoAndRefusesShortfall()
        {
            var registry = NewRegistry();
            var stock = new MineOreStock();
            var diag = new List<string>();
            MineOreLot first = NewLot(registry, 30, 2.0f);
            MineOreLot second = NewLot(registry, 30, 3.0f);
            stock.ReceiveLot(first, diag);
            stock.ReceiveLot(second, diag);

            var shortDiag = new List<string>();
            var shortLines = stock.DispenseTons(100, "smelter", shortDiag);
            Assert.AreEqual(0, shortLines.Count, "shortfall refuses the whole request");
            Assert.AreEqual(60, stock.TotalTons, "nothing moved on refusal");

            var lines = stock.DispenseTons(45, "smelter", diag);
            Assert.AreEqual(2, lines.Count, "FIFO spans lots");
            Assert.AreEqual(first.LotId, lines[0].LotId);
            Assert.AreEqual(30, lines[0].TonsTaken);
            Assert.AreEqual(15, lines[1].TonsTaken);
            Assert.AreEqual(15, stock.TotalTons);
            Assert.AreEqual("smelter", lines[0].Destination);
        }

        [Test]
        public void GradeUnit_DefaultsByMineralKind()
        {
            Assert.AreEqual("oz/ton", MineOreLot.DefaultGradeUnit(MineralResourceKind.Gold));
            Assert.AreEqual("oz/ton", MineOreLot.DefaultGradeUnit(MineralResourceKind.Silver));
            Assert.AreEqual("pct-fe", MineOreLot.DefaultGradeUnit(MineralResourceKind.Iron));
            Assert.AreEqual("btu-per-lb", MineOreLot.DefaultGradeUnit(MineralResourceKind.Coal));
        }

        [Test]
        public void SaveRoundTrip_PreservesOreStock()
        {
            var registry = NewRegistry();
            var state = MineRuntimeState.CreateDefault(MineralResourceKind.Silver);
            var diag = new List<string>();
            MineOreLot lot = NewLot(registry, 40, 2.5f);
            state.OreStock.ReceiveLot(lot, diag);

            var dto = state.CaptureSaveDto();
            MineRuntimeState restored = MineRuntimeState.FromSaveDto(dto);

            Assert.AreEqual(40, restored.OreStock.TotalTons);
            Assert.AreEqual(1, restored.OreStock.Lots.Count);
            Assert.AreEqual(2.5f, restored.OreStock.Lots[0].GradeValue);
            Assert.AreEqual("shaft-1", restored.OreStock.Lots[0].ShaftId);
        }
    }
}
