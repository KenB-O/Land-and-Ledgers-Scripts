using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Economy.Farming.Dairy;
using LandLedgers.Primitives;
using LandLedgers.Skills;
using LandLedgers.Tasks;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy.Farming
{
    /// <summary>
    /// FVS-2: dairy production chain. Lactation gating, yield math from real
    /// animal state (Tech X §8.1), task-driven milking/churning with lot
    /// provenance, spoilage that blocks processing, and the TTS-3 extension
    /// path for the dairy-processing skill.
    /// </summary>
    [TestFixture]
    public sealed class DairyChainTests
    {
        private AnimalRegistry registry;
        private EntityIdRegistry ids;
        private DairyChain dairy;

        [SetUp]
        public void SetUp()
        {
            ids = new EntityIdRegistry();
            registry = new AnimalRegistry(ids);
            dairy = new DairyChain();
        }

        private AnimalState NewCow(AnimalBiologicalState state, int parity)
        {
            var cow = registry.RegisterAnimal(AnimalSpecies.Cattle, "Shorthorn",
                AnimalSex.Female, AnimalOwnerKind.Business, "farm:test", 100, "test");
            cow.BiologicalState = state;
            cow.Parity = parity;
            dairy.RegisterCow(new DairyCowState(cow.AnimalId, 90, 1f));
            return cow;
        }

        [Test]
        public void DryCow_YieldsNothing()
        {
            var dry = NewCow(AnimalBiologicalState.Dry, 3);
            Assert.AreEqual(0, dairy.DailyMilkYield(dry, 1),
                "Dry cows yield nothing — lactation gating (Canon §3.4).");
        }

        [Test]
        public void LactatingCow_YieldRespondsToRealState()
        {
            var peak = NewCow(AnimalBiologicalState.Lactating, 2);   // parity 2
            var heifer = NewCow(AnimalBiologicalState.Lactating, 1); // first-calf

            int peakYield = dairy.DailyMilkYield(peak, 3);
            int heiferYield = dairy.DailyMilkYield(heifer, 3);

            Assert.Greater(peakYield, 0, "Lactating cow must yield milk.");
            Assert.Greater(peakYield, heiferYield, "First-calf heifers yield less (Tech X §4.5).");
        }

        [Test]
        public void CalfNursing_ReducesMilkedYield()
        {
            var nursing = NewCow(AnimalBiologicalState.Lactating, 2);
            dairy.GetCowState(nursing.AnimalId).CalfNursing = true;
            var weaned = NewCow(AnimalBiologicalState.Lactating, 2);

            Assert.Less(dairy.DailyMilkYield(nursing, 3), dairy.DailyMilkYield(weaned, 3),
                "Nursing calf takes its share first (Canon §9.3).");
        }

        [Test]
        public void MilkCow_MintsProvenanceLot()
        {
            var cow = NewCow(AnimalBiologicalState.Lactating, 2);
            var worker = EntityId.For(EntityKind.Person, 5);
            var diagnostics = new List<string>();

            MilkLot lot = dairy.MilkCow(registry, ids, cow.AnimalId, worker, "test-farm", 100, 2, diagnostics);

            Assert.IsNotNull(lot, "Milking failed: " + string.Join(" | ", diagnostics));
            Assert.Greater(lot.QuantityUnits, 0);
            Assert.AreEqual(cow.AnimalId, lot.CowId, "Lot must name its source cow.");
            Assert.AreEqual(worker, lot.MilkedBy, "Lot must name the worker (labor provenance).");
            Assert.AreEqual(100, lot.ProducedDayIndex);
            Assert.IsTrue(lot.LotId.IsValid, "Lot needs an HF-1 id.");
        }

        [Test]
        public void MilkCow_RefusesDryCow()
        {
            var dry = NewCow(AnimalBiologicalState.Dry, 2);
            var diagnostics = new List<string>();

            MilkLot lot = dairy.MilkCow(registry, ids, dry.AnimalId,
                EntityId.For(EntityKind.Person, 5), "test-farm", 100, 2, diagnostics);

            Assert.IsNull(lot, "Dry cow must not produce a milk lot.");
            Assert.IsTrue(diagnostics.Count > 0, "Refusal must be loud.");
        }

        [Test]
        public void SpoiledMilk_CannotBecomeButter()
        {
            var cow = NewCow(AnimalBiologicalState.Lactating, 2);
            var diagnostics = new List<string>();
            MilkLot lot = dairy.MilkCow(registry, ids, cow.AnimalId,
                EntityId.For(EntityKind.Person, 5), "test-farm", 100, 2, diagnostics);
            Assert.IsNotNull(lot);

            // Age past freshness.
            DairyChain.AgeMilkLot(lot, 100 + MilkLot.FreshDays + 1);
            Assert.AreEqual(0, lot.QuantityUnits);
            Assert.Greater(lot.SpoiledUnits, 0, "Spoilage must be recorded, not dropped.");

            var churnDiagnostics = new List<string>();
            var batch = dairy.ChurnButter(ids, new List<MilkLot> { lot },
                EntityId.For(EntityKind.Person, 5), 104, "churn", churnDiagnostics);

            Assert.IsNull(batch, "Spoiled milk can never become butter (Canon §9.1).");
        }

        [Test]
        public void ChurnButter_YieldsButterAndByproduct()
        {
            var cow = NewCow(AnimalBiologicalState.Lactating, 2);
            var diagnostics = new List<string>();
            MilkLot lot = dairy.MilkCow(registry, ids, cow.AnimalId,
                EntityId.For(EntityKind.Person, 5), "test-farm", 100, 2, diagnostics);
            Assert.IsNotNull(lot);
            int milkIn = lot.QuantityUnits;

            var batch = dairy.ChurnButter(ids, new List<MilkLot> { lot },
                EntityId.For(EntityKind.Person, 6), 100, "churn", diagnostics);

            Assert.IsNotNull(batch, "Churn failed: " + string.Join(" | ", diagnostics));
            Assert.AreEqual((milkIn * DairyChain.ButterUnitsPer10Milk) / 10, batch.OutputUnits);
            Assert.Greater(batch.ByproductUnits, 0, "Buttermilk byproduct must exist (Canon §9.7).");
            Assert.AreEqual(0, lot.QuantityUnits, "Input lots are consumed into the batch.");
            Assert.AreEqual(EntityId.For(EntityKind.Person, 6), batch.WorkerId);
        }

        [Test]
        public void CalvingAndDryOff_MoveLactationState()
        {
            var cow = NewCow(AnimalBiologicalState.Dry, 1);

            Assert.IsNull(dairy.RecordCalving(registry, cow.AnimalId, 100, calfNursing: false));
            Assert.AreEqual(AnimalBiologicalState.Lactating, cow.BiologicalState);
            Assert.AreEqual(2, cow.Parity, "Calving increments parity (Tech X §4.5).");
            Assert.Greater(dairy.DailyMilkYield(cow, 2), 0);

            Assert.IsNull(dairy.DryOff(registry, cow.AnimalId));
            Assert.AreEqual(AnimalBiologicalState.Dry, cow.BiologicalState);
            Assert.AreEqual(0, dairy.DailyMilkYield(cow, 2), "Dried-off cow yields nothing.");
        }

        [Test]
        public void DairySkill_RegistersThroughExtensionPath()
        {
            var skills = new SkillService();
            var diagnostics = new List<string>();

            DairyChain.RegisterSkills(skills, diagnostics);

            Assert.IsNotNull(skills.GetSkill(DairyChain.DairyProcessingSkillId),
                "dairy-processing must register via the TTS-3 extension path.");
            Assert.IsNotNull(skills.GetSkill(SkillIds.AnimalHusbandry),
                "Milking uses the existing animal-husbandry skill.");
        }

        [Test]
        public void TaskDefinitions_RegisterForMilkingAndChurning()
        {
            var authority = new TaskAuthority();
            DairyChain.RegisterTaskDefinitions(authority);

            var milk = authority.GetDefinition(DairyChain.MilkCowTaskId);
            var churn = authority.GetDefinition(DairyChain.ChurnButterTaskId);

            Assert.IsNotNull(milk);
            Assert.IsNotNull(churn);
            Assert.AreEqual(SkillIds.AnimalHusbandry, milk.RequiredSkillId);
            Assert.AreEqual(DairyChain.DairyProcessingSkillId, churn.RequiredSkillId);
            Assert.GreaterOrEqual(milk.BaseMinutes, 1, "TTS-1 minute quantum floor.");
        }
    }
}
