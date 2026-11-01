using System.Collections.Generic;
using LandLedgers.Economy.Farming.Dairy;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Skills;
using LandLedgers.Tasks;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy.Farming
{
    /// <summary>
    /// SWN-2: cheese. Making requires skill, equipment, and named rennet;
    /// wheels cure before they can sell; green cheese is never saleable.
    /// </summary>
    [TestFixture]
    public sealed class CheeseChainTests
    {
        private MilkLot FreshMilk(EntityIdRegistry ids, int units, int day)
        {
            return new MilkLot
            {
                LotId = ids.Allocate(EntityKind.Lot),
                QuantityUnits = units,
                ProducedDayIndex = day,
                FarmId = "farm-1",
            };
        }

        [Test]
        public void MakeCheese_RequiresEquipmentAndRennet()
        {
            var ids = new EntityIdRegistry();
            var chain = new CheeseChain();
            var diagnostics = new List<string>();
            var milk = new List<MilkLot> { FreshMilk(ids, 100, 100) };

            Assert.IsNull(chain.MakeCheese(ids, milk, EntityId.Invalid, "farm-1", 100, "", "butcher-1 rennet", diagnostics),
                "Cheesemaking without vats/press must be refused (Canon §9.6).");
            Assert.IsNull(chain.MakeCheese(ids, milk, EntityId.Invalid, "farm-1", 100, "vat + press", "", diagnostics),
                "Rennet needs a named source — no orphan inputs.");

            List<CheeseWheel> wheels = chain.MakeCheese(ids, milk, EntityId.Invalid, "farm-1", 100,
                "vat + press", "butcher-1 rennet", diagnostics);
            Assert.IsNotNull(wheels, "Cheese make failed: " + string.Join(" | ", diagnostics));
            Assert.AreEqual(1, wheels.Count, "100 milk units → 10 lbs → 1 wheel.");
            Assert.AreEqual(CheeseAgeState.Green, wheels[0].AgeState);
            Assert.AreEqual("butcher-1 rennet", wheels[0].RennetSource);
        }

        [Test]
        public void GreenCheese_IsNeverSaleable()
        {
            var ids = new EntityIdRegistry();
            var chain = new CheeseChain();
            var diagnostics = new List<string>();
            var milk = new List<MilkLot> { FreshMilk(ids, 100, 100) };
            List<CheeseWheel> wheels = chain.MakeCheese(ids, milk, EntityId.Invalid, "farm-1", 100,
                "vat + press", "butcher-1 rennet", diagnostics);
            Assert.IsNotNull(wheels);

            var ledger = new HouseholdLedger(7);
            string problem = chain.SellCheese(wheels[0], "store-1", "General Store", 40, 101, ledger, diagnostics);
            Assert.IsNotNull(problem, "Green cheese must never sell — delayed sale is the rule (Canon §9.6).");

            // Cure it, then it sells.
            chain.AgeWheels(100 + CheeseChain.CureDays, 1f, diagnostics);
            Assert.AreEqual(CheeseAgeState.Aged, wheels[0].AgeState);
            Assert.IsNull(chain.SellCheese(wheels[0], "store-1", "General Store", 40, 100 + CheeseChain.CureDays, ledger, diagnostics));
            Assert.IsTrue(wheels[0].Sold);
        }

        [Test]
        public void SpoiledMilk_CanNeverBecomeCheese()
        {
            var ids = new EntityIdRegistry();
            var chain = new CheeseChain();
            var diagnostics = new List<string>();
            // Milk produced 10 days ago: long past FreshDays=2.
            var milk = new List<MilkLot> { FreshMilk(ids, 100, 90) };

            Assert.IsNull(chain.MakeCheese(ids, milk, EntityId.Invalid, "farm-1", 100,
                "vat + press", "butcher-1 rennet", diagnostics),
                "Spoiled milk can never become cheese (Canon §9.1).");
        }

        [Test]
        public void PoorStorage_DegradesQuality()
        {
            var ids = new EntityIdRegistry();
            var chain = new CheeseChain();
            var diagnostics = new List<string>();
            var milk = new List<MilkLot> { FreshMilk(ids, 100, 100) };
            List<CheeseWheel> wheels = chain.MakeCheese(ids, milk, EntityId.Invalid, "farm-1", 100,
                "vat + press", "butcher-1 rennet", diagnostics);
            Assert.IsNotNull(wheels);

            for (int d = 101; d < 100 + CheeseChain.CureDays; d++)
            {
                chain.AgeWheels(d, 0.2f, diagnostics); // poor storage
            }
            Assert.Less(wheels[0].Quality01, 1f, "Poor storage must degrade curing cheese.");
        }

        [Test]
        public void CheeseChain_RegistersSkillAndTask()
        {
            var skills = new SkillService();
            var diagnostics = new List<string>();
            CheeseChain.RegisterSkills(skills, diagnostics);
            Assert.IsNotNull(skills.GetSkill(CheeseChain.CheeseMakingSkillId),
                "Cheese making registers through the TTS-3 extension path.");

            var tasks = new TaskAuthority();
            CheeseChain.RegisterTaskDefinitions(tasks);
            Assert.IsNotNull(tasks.GetDefinition(CheeseChain.MakeCheeseTaskId));
        }
    }
}
