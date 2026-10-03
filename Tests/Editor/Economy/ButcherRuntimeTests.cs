using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Economy.Butcher;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// BIZ-4: the butcher as a GoodsTransformer (Canon §3.3) — carcass balance
    /// (GHOST-DES-010), lot provenance (Tech X Part IV), perishability.
    /// </summary>
    [TestFixture]
    public sealed class ButcherRuntimeTests
    {
        private static (AnimalRegistry registry, EntityId animal) BuildAnimal()
        {
            var ids = new EntityIdRegistry();
            var registry = new AnimalRegistry(ids);
            AnimalState animal = registry.RegisterAnimal(
                AnimalSpecies.Cattle, "Shorthorn", AnimalSex.Male,
                AnimalOwnerKind.Person, "P0", 10, "test purchase");
            return (registry, animal.AnimalId);
        }

        [Test]
        public void Slaughter_CarcassBalances_NoFreeProduct()
        {
            var (registry, animalId) = BuildAnimal();
            var butcher = new ButcherRuntime("biz_butcher_001", "site-prod", "site-retail");
            var diagnostics = new List<string>();

            butcher.TryBuyLivestock(registry, animalId, 2000, 11, diagnostics);
            CarcassYield yield = butcher.Slaughter(registry, animalId, 1000, 12, diagnostics);

            Assert.IsNotNull(yield);
            // GHOST-DES-010: fractions sum to 1, weights sum to live weight.
            Assert.AreEqual(1f, yield.TotalFraction, 0.001f);
            Assert.AreEqual(1000, yield.TotalOutputWeightLbs);
            Assert.Greater(yield.WeightFor(CarcassProduct.RetailCuts), 0);
            Assert.Greater(yield.WeightFor(CarcassProduct.Hide), 0);
            Assert.Greater(yield.WeightFor(CarcassProduct.Tallow), 0);
            Assert.Greater(yield.WeightFor(CarcassProduct.Waste), 0);
        }

        [Test]
        public void Slaughter_MarksAnimalTerminal_IdNeverReused()
        {
            var (registry, animalId) = BuildAnimal();
            var butcher = new ButcherRuntime("biz_butcher_001", "site-prod", "site-retail");
            var diagnostics = new List<string>();

            butcher.TryBuyLivestock(registry, animalId, 2000, 11, diagnostics);
            butcher.Slaughter(registry, animalId, 1000, 12, diagnostics);

            AnimalState animal = registry.GetAnimal(animalId);
            Assert.AreEqual(AnimalCommercialStatus.Slaughtered, animal.CommercialStatus);
            Assert.IsFalse(animal.IsActive);

            // Cannot buy/slaughter the same animal twice.
            Assert.IsFalse(butcher.TryBuyLivestock(registry, animalId, 2000, 13, diagnostics));
        }

        [Test]
        public void Slaughter_RequiresOwnership()
        {
            var (registry, animalId) = BuildAnimal();
            var butcher = new ButcherRuntime("biz_butcher_001", "site-prod", "site-retail");
            var diagnostics = new List<string>();

            // Never bought — still owned by P0.
            CarcassYield yield = butcher.Slaughter(registry, animalId, 1000, 12, diagnostics);

            Assert.IsNull(yield);
        }

        [Test]
        public void Lots_CarrySourceAnimalProvenance()
        {
            var (registry, animalId) = BuildAnimal();
            var butcher = new ButcherRuntime("biz_butcher_001", "site-prod", "site-retail");
            var diagnostics = new List<string>();

            butcher.TryBuyLivestock(registry, animalId, 2000, 11, diagnostics);
            butcher.Slaughter(registry, animalId, 1000, 12, diagnostics);

            Assert.Greater(butcher.Lots.Count, 0);
            foreach (ButcherLot lot in butcher.Lots)
            {
                Assert.AreEqual(animalId.ToString(), lot.SourceAnimalKey);
                Assert.AreEqual(12, lot.ProducedDayIndex);
            }
        }

        [Test]
        public void SpoiledLots_CannotBeSold()
        {
            var (registry, animalId) = BuildAnimal();
            var butcher = new ButcherRuntime("biz_butcher_001", "site-prod", "site-retail");
            var diagnostics = new List<string>();

            butcher.TryBuyLivestock(registry, animalId, 2000, 11, diagnostics);
            butcher.Slaughter(registry, animalId, 1000, 12, diagnostics);
            butcher.AgeLotsToDay(30); // Long past fresh.

            string lotId = butcher.Lots[0].LotId;
            Assert.AreEqual(LotCondition.Spoiled, butcher.Lots[0].Condition);
            Assert.AreEqual(0, butcher.SellRetail(lotId, 10, diagnostics));
            Assert.AreEqual(0, butcher.SellWholesale(lotId, 10, "biz_store_001", diagnostics));
            Assert.AreEqual(0, butcher.RetailRevenueCents);
        }

        [Test]
        public void SellRetail_And_Wholesale_TrackRevenueSeparately()
        {
            var (registry, animalId) = BuildAnimal();
            var butcher = new ButcherRuntime("biz_butcher_001", "site-prod", "site-retail");
            var diagnostics = new List<string>();

            butcher.TryBuyLivestock(registry, animalId, 2000, 11, diagnostics);
            butcher.Slaughter(registry, animalId, 1000, 12, diagnostics);

            string cutsLot = null;
            foreach (ButcherLot lot in butcher.Lots)
            {
                if (lot.Product == CarcassProduct.RetailCuts)
                {
                    cutsLot = lot.LotId;
                }
            }

            Assert.IsNotNull(cutsLot);
            int retail = butcher.SellRetail(cutsLot, 10, diagnostics);
            int wholesale = butcher.SellWholesale(cutsLot, 10, "biz_store_001", diagnostics);

            Assert.Greater(retail, 0);
            Assert.Greater(wholesale, 0);
            Assert.Greater(retail, wholesale); // Retail beats wholesale per-lb.
            Assert.AreEqual(retail, butcher.RetailRevenueCents);
            Assert.AreEqual(wholesale, butcher.WholesaleRevenueCents);
        }
    }
}
