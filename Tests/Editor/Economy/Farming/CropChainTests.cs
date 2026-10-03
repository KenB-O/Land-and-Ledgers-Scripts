using System.Collections.Generic;
using LandLedgers.Economy.Farming.Crops;
using LandLedgers.Economy.Farming.Integration;
using LandLedgers.Primitives;
using LandLedgers.Skills;
using LandLedgers.Tasks;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy.Farming
{
    /// <summary>
    /// CRP-2: the field task chain. Planting is spring-gated and refuses without
    /// named seed; tending raises realized yield; harvest realizes biological
    /// potential × work completion; hay moves through its §7.4F stages.
    /// </summary>
    [TestFixture]
    public sealed class CropChainTests
    {
        private (CropFieldAuthority, CropChain) PreparedWheatField()
        {
            var authority = new CropFieldAuthority();
            var field = new CropFieldState("wheat-40", "farm-1", 40f)
            {
                LinkedPlotName = "north crop field",
                Suitability01 = 0.8f,
                Moisture01 = 0.7f,
                GrowthState = CropGrowthState.Prepared,
            };
            Assert.IsNull(authority.RegisterField(field));
            return (authority, new CropChain(authority));
        }

        [Test]
        public void PlantField_RefusesOutsideSpringAndWithoutSeedSource()
        {
            var (authority, chain) = PreparedWheatField();
            var diagnostics = new List<string>();

            // Day 100 = summer: planting refused loudly.
            string problem = chain.PlantField("wheat-40", CropKind.Wheat, 80, "general store", EntityId.Invalid, 100, diagnostics);
            Assert.IsNotNull(problem, "Planting outside spring must be refused (Canon §7.4D).");

            // Day 10 = spring, but no seed source: refused (no orphan inputs).
            problem = chain.PlantField("wheat-40", CropKind.Wheat, 80, "", EntityId.Invalid, 10, diagnostics);
            Assert.IsNotNull(problem, "Seed must name its source.");

            // Valid planting.
            Assert.IsNull(chain.PlantField("wheat-40", CropKind.Wheat, 80, "general store", EntityId.Invalid, 10, diagnostics));
            Assert.AreEqual(CropGrowthState.Planted, authority.GetField("wheat-40").GrowthState);
        }

        [Test]
        public void Harvest_RealizesPotentialTimesWork()
        {
            var (authority, chain) = PreparedWheatField();
            var ids = new EntityIdRegistry();
            var diagnostics = new List<string>();

            Assert.IsNull(chain.PlantField("wheat-40", CropKind.Wheat, 80, "general store", EntityId.Invalid, 10, diagnostics));

            // Grow to ready without any tending.
            CropFieldState field = authority.GetField("wheat-40");
            field.GrowthState = CropGrowthState.Ready;
            CropLot untended = chain.HarvestField(ids, "wheat-40", EntityId.Invalid, 120, 1f, diagnostics);
            Assert.IsNotNull(untended);
            Assert.AreEqual("sheaves", untended.ProductKind, "Wheat comes off as sheaves needing threshing.");

            // Second field, tended twice: yields more.
            var field2 = new CropFieldState("wheat-40b", "farm-1", 40f)
            {
                Suitability01 = 0.8f,
                Moisture01 = 0.7f,
                GrowthState = CropGrowthState.Prepared,
            };
            Assert.IsNull(authority.RegisterField(field2));
            Assert.IsNull(chain.PlantField("wheat-40b", CropKind.Wheat, 80, "general store", EntityId.Invalid, 10, diagnostics));
            Assert.IsNull(chain.TendField("wheat-40b", EntityId.Invalid, 40, diagnostics));
            Assert.IsNull(chain.TendField("wheat-40b", EntityId.Invalid, 70, diagnostics));
            authority.GetField("wheat-40b").GrowthState = CropGrowthState.Ready;
            CropLot tended = chain.HarvestField(ids, "wheat-40b", EntityId.Invalid, 120, 1f, diagnostics);

            Assert.IsNotNull(tended);
            Assert.Greater(tended.QuantityUnits, untended.QuantityUnits,
                "Tended fields must out-yield untended ones — missed work reduces through field state (Canon §7.4D).");
        }

        [Test]
        public void ThreshGrain_ConvertsSheavesWithLoss()
        {
            var (authority, chain) = PreparedWheatField();
            var ids = new EntityIdRegistry();
            var diagnostics = new List<string>();

            Assert.IsNull(chain.PlantField("wheat-40", CropKind.Wheat, 80, "general store", EntityId.Invalid, 10, diagnostics));
            authority.GetField("wheat-40").GrowthState = CropGrowthState.Ready;
            CropLot sheaves = chain.HarvestField(ids, "wheat-40", EntityId.Invalid, 120, 1f, diagnostics);
            Assert.IsNotNull(sheaves);

            CropLot grain = chain.ThreshGrain(ids, sheaves, EntityId.Invalid, 121, diagnostics);
            Assert.IsNotNull(grain);
            Assert.AreEqual("grain", grain.ProductKind);
            Assert.AreEqual(0, sheaves.QuantityUnits, "Sheaves are consumed by threshing.");
            Assert.Less(grain.QuantityUnits, 1000000, "Sanity: grain units are finite.");
            Assert.Greater(grain.QuantityUnits, 0);
            StringAssert.Contains("granary", grain.StorageNote, "Grain must note its storage need (Canon §7.4L).");
        }

        [Test]
        public void HayCycle_CutCureStack()
        {
            var authority = new CropFieldAuthority();
            var field = new CropFieldState("hay-20", "farm-1", 20f)
            {
                Suitability01 = 0.7f,
                Moisture01 = 0.6f,
                GrowthState = CropGrowthState.Prepared,
            };
            Assert.IsNull(authority.RegisterField(field));
            var chain = new CropChain(authority);
            var ids = new EntityIdRegistry();
            var diagnostics = new List<string>();

            Assert.IsNull(chain.PlantField("hay-20", CropKind.Hay, 20, "general store", EntityId.Invalid, 10, diagnostics));
            Assert.AreEqual(HayStage.Standing, authority.GetField("hay-20").HayStage);

            Assert.IsNull(chain.CutHay("hay-20", EntityId.Invalid, 80, diagnostics));

            // Rushing the cure is refused.
            CropLot rushed = chain.StackHay(ids, "hay-20", EntityId.Invalid, 81, 1f, diagnostics);
            Assert.IsNull(rushed, "Hay stacked before curing must be refused (Canon §7.4F).");

            CropLot hay = chain.StackHay(ids, "hay-20", EntityId.Invalid, 84, 1f, diagnostics);
            Assert.IsNotNull(hay);
            Assert.AreEqual("hay", hay.ProductKind);
            Assert.Greater(hay.QuantityUnits, 0);
        }

        [Test]
        public void RegisterTaskDefinitions_RegistersFieldTasks()
        {
            var tasks = new TaskAuthority();
            CropChain.RegisterTaskDefinitions(tasks);
            Assert.IsNotNull(tasks.GetDefinition(CropChain.PlowFieldTaskId));
            Assert.IsNotNull(tasks.GetDefinition(CropChain.PlantFieldTaskId));
            Assert.IsNotNull(tasks.GetDefinition(CropChain.HarvestFieldTaskId));
            Assert.IsNotNull(tasks.GetDefinition(CropChain.ThreshGrainTaskId));
            Assert.AreEqual(SkillIds.CropTending,
                tasks.GetDefinition(CropChain.PlowFieldTaskId).RequiredSkillId,
                "Field work is skilled crop-tending labor.");
        }
    }
}
