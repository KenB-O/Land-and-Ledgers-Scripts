using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Mine;
using LandLedgers.Primitives;
using LandLedgers.World;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// Pass XI WS3 (PASS9-005): one assay certifies one lot. Splitting
    /// an assayed lot leaves the certificate on the parent only; the
    /// child starts unassayed at estimate confidence, tonnage is
    /// conserved, and assayed lots still never re-merge.
    /// </summary>
    [TestFixture]
    public sealed class Pass11Ws3OreAssaySplitTests
    {
        [Test]
        public void Split_AssayedLot_ChildStartsUnassayed_ParentKeepsCertificate()
        {
            var registry = new EntityIdRegistry();
            var stock = new MineOreStock();
            MineOreLot parent = MineOreLot.Create(registry, MineralResourceKind.Gold, 10, 2.0f,
                "shaft-1", "level-1", "vein-1", 3, "ore-gold");
            Assert.IsNull(stock.ReceiveLot(parent, new List<string>()));
            Assert.IsNull(parent.RecordAssay("assay-1", EntityId.For(EntityKind.Person, 9), 6, 2.0f));
            Assert.IsTrue(parent.Assayed);
            Assert.AreEqual(1.0f, parent.GradeConfidence01);

            MineOreLot child = stock.SplitLot(registry, parent.LotId, 4, new List<string>());
            Assert.IsNotNull(child);
            Assert.AreEqual(6, parent.Tons);
            Assert.AreEqual(4, child.Tons);
            Assert.AreEqual(10, stock.TotalTons, "split conserves tons");

            Assert.IsTrue(parent.Assayed);
            Assert.AreEqual("assay-1", parent.AssayId);
            Assert.AreEqual(1.0f, parent.GradeConfidence01);
            Assert.IsFalse(child.Assayed, "one assay must not certify two lots");
            Assert.AreEqual(string.Empty, child.AssayId);
            Assert.AreEqual(0.3f, child.GradeConfidence01, "the child is an estimate until it earns its own assay");
            Assert.AreEqual(2.0f, child.GradeValue, "the grade value carries as an estimate of the same material");

            // The child can earn its own assay afterwards, with its own id.
            Assert.IsNull(child.RecordAssay("assay-2", EntityId.For(EntityKind.Person, 9), 7, 2.0f));
            Assert.IsTrue(child.Assayed);
            Assert.AreEqual("assay-2", child.AssayId);
            Assert.AreNotEqual(parent.AssayId, child.AssayId);
        }

        [Test]
        public void Split_UnassayedLot_ChildStaysUnassayed_TonsConserved()
        {
            var registry = new EntityIdRegistry();
            var stock = new MineOreStock();
            MineOreLot parent = MineOreLot.Create(registry, MineralResourceKind.Gold, 10, 2.0f,
                "shaft-1", "level-1", "vein-1", 3, "ore-gold");
            Assert.IsNull(stock.ReceiveLot(parent, new List<string>()));
            MineOreLot child = stock.SplitLot(registry, parent.LotId, 4, new List<string>());
            Assert.IsNotNull(child);
            Assert.IsFalse(child.Assayed);
            Assert.AreEqual(10, stock.TotalTons);
        }

        [Test]
        public void Load_OldStockWithDuplicatedAssay_FirstKeepsCertificate_RestCleared()
        {
            var registry = new EntityIdRegistry();
            var stock = new MineOreStock();
            MineOreLot parent = MineOreLot.Create(registry, MineralResourceKind.Gold, 6, 2.0f,
                "shaft-1", "level-1", "vein-1", 3, "ore-gold");
            Assert.IsNull(stock.ReceiveLot(parent, new List<string>()));
            Assert.IsNull(parent.RecordAssay("assay-1", EntityId.For(EntityKind.Person, 9), 6, 2.0f));

            // Simulate a pre-fix save: a second lot carrying the same assay id.
            MineOreStockSaveDto dto = stock.CaptureSaveDto();
            var copy = new MineOreLotSaveDto
            {
                lotKind = dto.lots[0].lotKind,
                lotSeq = 999,
                mineralKind = dto.lots[0].mineralKind,
                tons = 4,
                gradeValue = dto.lots[0].gradeValue,
                gradeUnit = dto.lots[0].gradeUnit,
                assayed = true,
                assayId = "assay-1",
                assayerKind = dto.lots[0].assayerKind,
                assayerSeq = dto.lots[0].assayerSeq,
                assayDayIndex = dto.lots[0].assayDayIndex,
                gradeConfidence01 = 1.0f,
                shaftId = dto.lots[0].shaftId,
                levelId = dto.lots[0].levelId,
                veinId = dto.lots[0].veinId,
                minedDayIndex = dto.lots[0].minedDayIndex,
                stockpileCategoryId = dto.lots[0].stockpileCategoryId,
                ancestryLotSeqs = new List<int>(dto.lots[0].ancestryLotSeqs),
            };
            dto.lots.Add(copy);
            MineOreStock restored = MineOreStock.FromSaveDto(dto);
            Assert.AreEqual(2, restored.Lots.Count);
            Assert.IsTrue(restored.Lots[0].Assayed);
            Assert.AreEqual("assay-1", restored.Lots[0].AssayId);
            Assert.IsFalse(restored.Lots[1].Assayed, "the duplicated certificate is removed, not re-certified");
            Assert.AreEqual(string.Empty, restored.Lots[1].AssayId);
            Assert.AreEqual(0.3f, restored.Lots[1].GradeConfidence01);
            Assert.AreEqual(1, restored.Diagnostics.Count, "the migration is diagnosed, not silent");
            int assayedCount = 0;
            foreach (MineOreLot lot in restored.Lots)
                if (lot.Assayed) assayedCount++;
            Assert.AreEqual(1, assayedCount, "exactly one lot carries the assay after load");
        }

        [Test]
        public void Merge_AssayedParentStillNeverMerges()
        {
            var registry = new EntityIdRegistry();
            var stock = new MineOreStock();
            MineOreLot parent = MineOreLot.Create(registry, MineralResourceKind.Gold, 10, 2.0f,
                "shaft-1", "level-1", "vein-1", 3, "ore-gold");
            Assert.IsNull(stock.ReceiveLot(parent, new List<string>()));
            Assert.IsNull(parent.RecordAssay("assay-1", EntityId.For(EntityKind.Person, 9), 6, 2.0f));
            MineOreLot child = stock.SplitLot(registry, parent.LotId, 4, new List<string>());
            Assert.IsNotNull(child);
            Assert.AreEqual(0, stock.MergeCompatibleLots(), "the assayed parent never merges; the unassayed child has no compatible partner here");
            Assert.AreEqual(2, stock.Lots.Count);
        }
    }
}
