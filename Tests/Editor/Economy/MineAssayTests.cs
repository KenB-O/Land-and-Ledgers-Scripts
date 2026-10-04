using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.Mine;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.World;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W8B: assay is a skilled task (assay-bench workstation + assaying
    /// capability); assayed lots carry assayer provenance and measured
    /// grade. Anonymous assays, phantom lots, and re-assays are refused.
    /// </summary>
    [TestFixture]
    public sealed class MineAssayTests
    {
        private static MineOreStock StockWithLot(EntityIdRegistry registry, out EntityId lotId)
        {
            var stock = new MineOreStock();
            var diag = new List<string>();
            var lot = MineOreLot.Create(registry, MineralResourceKind.Gold, 20, 0.8f,
                "shaft-1", "level-1", "vein-1", 410, "gold_ore");
            stock.ReceiveLot(lot, diag);
            lotId = lot.LotId;
            return stock;
        }

        [Test]
        public void AssayLot_RecordsGradeAndAssayerProvenance()
        {
            var registry = new EntityIdRegistry();
            MineOreStock stock = StockWithLot(registry, out EntityId lotId);
            EntityId assayer = registry.Allocate(EntityKind.Person);
            var service = new MineAssayService();

            MineAssayResult result = service.AssayLot(stock, lotId, assayer, 420, 1.35f, "fire assay");
            Assert.IsNotNull(result);
            Assert.AreEqual(assayer, result.AssayerPersonId);
            Assert.AreEqual(1.35f, result.AssayedGradeValue);
            Assert.AreEqual("fire assay", result.MethodNote);

            MineOreLot lot = stock.FindLot(lotId);
            Assert.IsTrue(lot.Assayed);
            Assert.AreEqual(1.35f, lot.GradeValue);
            Assert.AreEqual(1.0f, lot.GradeConfidence01);
            Assert.AreEqual(result.AssayId, lot.AssayId);
            Assert.AreEqual(1, stock.AssayResults.Count);
        }

        [Test]
        public void AssayLot_RefusesAnonymousAssayerAndPhantomLot()
        {
            var registry = new EntityIdRegistry();
            MineOreStock stock = StockWithLot(registry, out EntityId lotId);
            var service = new MineAssayService();

            Assert.IsNull(service.AssayLot(stock, lotId, EntityId.Invalid, 420, 1.0f, null),
                "anonymous assay refused");
            Assert.IsNotEmpty(service.Diagnostics);

            var phantom = registry.Allocate(EntityKind.Lot);
            EntityId assayer = registry.Allocate(EntityKind.Person);
            Assert.IsNull(service.AssayLot(stock, phantom, assayer, 420, 1.0f, null),
                "assay of ore that does not exist refused");
            Assert.IsFalse(stock.FindLot(lotId).Assayed);
        }

        [Test]
        public void AssayLot_RefusesReassay()
        {
            var registry = new EntityIdRegistry();
            MineOreStock stock = StockWithLot(registry, out EntityId lotId);
            EntityId assayer = registry.Allocate(EntityKind.Person);
            var service = new MineAssayService();

            Assert.IsNotNull(service.AssayLot(stock, lotId, assayer, 420, 1.35f, null));
            Assert.IsNull(service.AssayLot(stock, lotId, assayer, 421, 1.40f, null),
                "re-assay refused — record a new sample instead");
            Assert.AreEqual(1.35f, stock.FindLot(lotId).GradeValue, "original assay stands");
        }

        [Test]
        public void AssayTask_RequiresAssayBenchWorkstation()
        {
            var authority = new TaskAuthority();
            var diag = new List<string>();
            MineAssayTaskCatalog.Register(authority, diag);

            TaskDefinition assay = authority.GetDefinition(MineAssayTaskCatalog.AssaySampleTaskId);
            Assert.IsNotNull(assay);
            Assert.AreEqual(MineAssayTaskCatalog.AssayingSkillId, assay.RequiredSkillId);
            Assert.IsTrue(assay.EquipmentClasses.Contains(
                LandLedgers.Economy.Equipment.EquipmentRequirementCodes.Workstation(
                    MineAssayTaskCatalog.AssayBenchStationId)),
                "assaying requires the assay-bench workstation (Tech X §3.5)");
            Assert.IsTrue(assay.RequiredCapabilityTags.Contains(MineAssayTaskCatalog.CapabilityMineAssaying));
        }

        [Test]
        public void SaveRoundTrip_PreservesAssays()
        {
            var registry = new EntityIdRegistry();
            var state = MineRuntimeState.CreateDefault(MineralResourceKind.Gold);
            var diag = new List<string>();
            var lot = MineOreLot.Create(registry, MineralResourceKind.Gold, 20, 0.8f,
                "shaft-1", "level-1", "vein-1", 410, "gold_ore");
            state.OreStock.ReceiveLot(lot, diag);
            EntityId assayer = registry.Allocate(EntityKind.Person);
            new MineAssayService().AssayLot(state.OreStock, lot.LotId, assayer, 420, 1.35f, "fire assay");

            var dto = state.CaptureSaveDto();
            MineRuntimeState restored = MineRuntimeState.FromSaveDto(dto);

            Assert.AreEqual(1, restored.OreStock.AssayResults.Count);
            Assert.AreEqual("fire assay", restored.OreStock.AssayResults[0].MethodNote);
            Assert.IsTrue(restored.OreStock.Lots[0].Assayed);
            Assert.AreEqual(assayer, restored.OreStock.Lots[0].AssayerPersonId);
        }
    }
}
