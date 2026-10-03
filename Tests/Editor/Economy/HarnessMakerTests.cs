using System.Collections.Generic;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Tannery;
using LandLedgers.Primitives;
using LandLedgers.Skills;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// NX-1B drive-by: harness crafting recipe — tannery leather + buckles +
    /// thread becomes a work harness (EQU-3 draft unit input), with
    /// provenance and the equipment gate.
    /// </summary>
    public sealed class HarnessMakerTests
    {
        private static TanneryRuntime.LeatherLot LeatherLot(int lbs)
        {
            return new TanneryRuntime.LeatherLot
            {
                LotId = "LOT-1", Lbs = lbs,
                HideSourceNote = "butcher lot B-7",
                TanneryBusinessId = "tan-1",
            };
        }

        private static SkillService SkilledSaddler(EntityId saddler)
        {
            var skills = new SkillService();
            HarnessMaker.RegisterSkills(skills, new List<string>());
            skills.GrantPractice(saddler, HarnessMaker.LeatherworkingSkillId, 100); // level 2
            return skills;
        }

        [Test]
        public void Craft_ProducesHarness_WithProvenance()
        {
            var maker = new HarnessMaker("tan-1", "Tannery");
            EntityId saddler = EntityId.For(EntityKind.Person, 9);
            var skills = SkilledSaddler(saddler);
            int buckles = 10, thread = 10;
            var diag = new List<string>();

            var asset = maker.CraftHarness(
                LeatherLot(20), ref buckles, ref thread,
                9, 40, skills, null, diag);

            Assert.IsNotNull(asset);
            Assert.AreEqual("harness", asset.Kind);
            Assert.AreEqual(1f, asset.Condition01, 0.001f);
            Assert.Contains("LOT-1", asset.MaterialLotIds);
            Assert.AreEqual(20 - HarnessMaker.LeatherLbsPerHarness, 8); // sanity on recipe
            Assert.AreEqual(10 - HarnessMaker.BucklesPerHarness, buckles);
            Assert.AreEqual(10 - HarnessMaker.ThreadUnitsPerHarness, thread);
        }

        [Test]
        public void Craft_RefusesWithoutLeather()
        {
            var maker = new HarnessMaker("tan-1", "Tannery");
            EntityId saddler = EntityId.For(EntityKind.Person, 9);
            var skills = SkilledSaddler(saddler);
            int buckles = 10, thread = 10;
            var diag = new List<string>();

            var asset = maker.CraftHarness(
                LeatherLot(2), ref buckles, ref thread,
                9, 40, skills, null, diag);

            Assert.IsNull(asset);
            Assert.AreEqual(10, buckles); // nothing consumed on refusal
            Assert.AreEqual(10, thread);
        }

        [Test]
        public void Craft_GateRefusesWithoutKit()
        {
            var maker = new HarnessMaker("tan-1", "Tannery");
            EntityId saddler = EntityId.For(EntityKind.Person, 9);
            var skills = SkilledSaddler(saddler);
            var gate = new EquipmentTaskGate(); // no suppliers: nothing usable
            int buckles = 10, thread = 10;
            var diag = new List<string>();

            var asset = maker.CraftHarness(
                LeatherLot(20), ref buckles, ref thread,
                9, 40, skills, gate, diag);

            Assert.IsNull(asset);
            Assert.IsTrue(diag.Exists(d => d.Contains("wheelwright-kit")));
        }
    }
}
