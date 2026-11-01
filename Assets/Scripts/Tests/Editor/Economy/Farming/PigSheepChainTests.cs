using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Economy.Butcher;
using LandLedgers.Economy.Farming;
using LandLedgers.Economy.Farming.Integration;
using LandLedgers.Economy.Farming.Livestock;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy.Farming
{
    /// <summary>
    /// SWN-1: pigs and sheep. Pigs grow to market weight on real feed, sell to
    /// the butcher, and slaughter with pig-specific carcass fractions. Sheep
    /// give annual wool clips (live-animal product) plus mutton at cull.
    /// </summary>
    [TestFixture]
    public sealed class PigSheepChainTests
    {
        private AnimalRegistry Registry() => new AnimalRegistry(new EntityIdRegistry());

        private AnimalState RegisterFarmAnimal(AnimalRegistry registry, AnimalSpecies species, string farmId)
        {
            return registry.RegisterAnimal(species, "test-breed", AnimalSex.Female,
                AnimalOwnerKind.Business, farmId, 50, "raised on farm");
        }

        [Test]
        public void Pig_MarketReadyThenSoldToButcher()
        {
            var registry = Registry();
            var chain = new PigSheepChain();
            var diagnostics = new List<string>();

            AnimalState pig = RegisterFarmAnimal(registry, AnimalSpecies.Pig, "farm-1");
            Assert.IsNull(chain.RegisterPig(registry, pig.AnimalId, 50));
            Assert.IsFalse(chain.IsPigMarketReady(pig.AnimalId, 100), "Pigs are not market-ready at 50 days.");
            Assert.IsTrue(chain.IsPigMarketReady(pig.AnimalId, 50 + PigSheepChain.PigMarketReadyDays));

            var butcher = new ButcherRuntime("butcher-1", "yard", "counter");
            var ledger = new HouseholdLedger(7);
            FarmLivestockSale sale = FarmLivestockMarket.ExecuteSale(
                registry, pig.AnimalId, "farm-1", "Home Farm",
                butcher, "butcher-1", 1200, 230, ledger, diagnostics);
            Assert.IsNotNull(sale, "Pig sale failed: " + string.Join(" | ", diagnostics));

            CarcassYield yield = butcher.Slaughter(registry, pig.AnimalId, 250, 231, diagnostics);
            Assert.IsNotNull(yield, "Pig slaughter failed: " + string.Join(" | ", diagnostics));
            Assert.AreEqual(AnimalSpecies.Pig, yield.Species);
            Assert.AreEqual(1f, yield.TotalFraction, 0.001f, "Carcass balance is structural for every species (GHOST-DES-010).");
            // Pigs dress high: more retail cuts than beef's 0.42.
            Assert.Greater(yield.WeightFor(CarcassProduct.RetailCuts), 250 * 0.42f);
        }

        [Test]
        public void Sheep_ShearedAnnuallyThenCulledForMutton()
        {
            var registry = Registry();
            var ids = new EntityIdRegistry();
            var chain = new PigSheepChain();
            var diagnostics = new List<string>();

            AnimalState sheep = RegisterFarmAnimal(registry, AnimalSpecies.Sheep, "farm-1");
            Assert.IsNull(chain.RegisterSheep(registry, sheep.AnimalId, 50));

            WoolLot wool = chain.ShearSheep(registry, ids, sheep.AnimalId,
                EntityId.For(EntityKind.Person, 3), "farm-1", 100, diagnostics);
            Assert.IsNotNull(wool, "Shearing failed: " + string.Join(" | ", diagnostics));
            Assert.Greater(wool.Lbs, 0, "Wool comes off the live sheep.");

            // No second clip before the wool regrows.
            WoolLot second = chain.ShearSheep(registry, ids, sheep.AnimalId,
                EntityId.For(EntityKind.Person, 3), "farm-1", 200, diagnostics);
            Assert.IsNull(second, "Sheep cannot be shorn twice in one wool cycle.");

            // Wool sells exactly once to a real buyer.
            var ledger = new HouseholdLedger(7);
            Assert.IsNull(chain.SellWool(wool, "tailor-1", "Tailor Shop", 15, 101, ledger, diagnostics));
            Assert.IsTrue(wool.Sold);
            Assert.IsNotNull(chain.SellWool(wool, "tailor-1", "Tailor Shop", 15, 102, ledger, diagnostics),
                "Wool lots sell exactly once.");

            // Cull to the butcher for mutton.
            var butcher = new ButcherRuntime("butcher-1", "yard", "counter");
            FarmLivestockSale sale = FarmLivestockMarket.ExecuteSale(
                registry, sheep.AnimalId, "farm-1", "Home Farm",
                butcher, "butcher-1", 800, 400, ledger, diagnostics);
            Assert.IsNotNull(sale, "Sheep cull sale failed: " + string.Join(" | ", diagnostics));
            CarcassYield yield = butcher.Slaughter(registry, sheep.AnimalId, 140, 401, diagnostics);
            Assert.IsNotNull(yield);
            Assert.AreEqual(1f, yield.TotalFraction, 0.001f);
        }

        [Test]
        public void FeedLoop_FeedsPigsAndSheep()
        {
            var feed = new FeedLoop(1000);
            var swine = new PigSheepChain();
            var diagnostics = new List<string>();
            var registry = Registry();
            AnimalState pig = RegisterFarmAnimal(registry, AnimalSpecies.Pig, "farm-1");
            Assert.IsNull(swine.RegisterPig(registry, pig.AnimalId, 50));

            int shortfall = feed.ConsumeDay(
                0, 0, 2, 4, FarmSeason.Summer,
                null, null, null, swine, diagnostics);
            Assert.AreEqual(0, shortfall, "1000 units must cover 2 pigs + 4 sheep in summer.");
            Assert.Less(feed.FeedStockUnits, 1000, "Pigs and sheep eat real feed.");

            // Starve them: condition drops.
            var starved = new FeedLoop(0);
            starved.ConsumeDay(0, 0, 2, 0, FarmSeason.Winter, null, null, null, swine, diagnostics);
            Assert.Less(swine.GetPig(pig.AnimalId).Condition01, 1f,
                "Underfed pigs lose condition.");
        }

        [Test]
        public void CarcassFractions_SumToOneForEverySpecies()
        {
            foreach (AnimalSpecies species in new[] { AnimalSpecies.Cattle, AnimalSpecies.Pig, AnimalSpecies.Sheep })
            {
                float total = 0f;
                foreach (var (_, fraction) in CarcassYieldFractions.ForSpecies(species))
                {
                    total += fraction;
                }
                Assert.AreEqual(1f, total, 0.001f, $"Fractions must sum to 1.0 for {species}.");
            }
        }

        [Test]
        public void RegisterTaskDefinitions_RegistersSwineAndShearing()
        {
            var tasks = new TaskAuthority();
            PigSheepChain.RegisterTaskDefinitions(tasks);
            Assert.IsNotNull(tasks.GetDefinition(PigSheepChain.TendSwineTaskId));
            Assert.IsNotNull(tasks.GetDefinition(PigSheepChain.ShearSheepTaskId));
        }
    }
}
