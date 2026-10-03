using System.Collections.Generic;
using LandLedgers.Economy.Butcher;
using LandLedgers.Economy.Farming;
using LandLedgers.Population;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// FRM-2: farm → butcher livestock sales. Ownership transfers through the HF-2
    /// registry (append-only history, pedigree intact, IDs never reused); per-head
    /// pricing records SaleProceeds on the farm household ledger (Canon XIII 13.2).
    /// </summary>
    [TestFixture]
    public sealed class FarmLivestockSaleTests
    {
        private AnimalRegistry NewRegistry()
        {
            return new AnimalRegistry(new EntityIdRegistry());
        }

        private ButcherRuntime NewButcher()
        {
            return new ButcherRuntime("B1", "yard", "counter");
        }

        private AnimalState NewFarmAnimal(AnimalRegistry registry, string farmId)
        {
            return registry.RegisterAnimal(
                AnimalSpecies.Cattle, "Angus", AnimalSex.Female,
                AnimalOwnerKind.Business, farmId, 50, "raised on farm");
        }

        [Test]
        public void Sale_AppendsOwnershipHistory_KeepsPedigree()
        {
            AnimalRegistry registry = NewRegistry();
            AnimalState animal = NewFarmAnimal(registry, "F1");
            animal.Sire = EntityId.For(EntityKind.Animal, 111);
            animal.Dam = EntityId.For(EntityKind.Animal, 222);

            int recordsBefore = animal.OwnershipHistory.Count;

            var diagnostics = new List<string>();
            FarmLivestockSale sale = FarmLivestockMarket.ExecuteSale(
                registry, animal.AnimalId, "F1", "Test Farm",
                NewButcher(), "B1", 8500, 100,
                new HouseholdLedger(7), diagnostics);

            Assert.IsNotNull(sale);
            Assert.AreEqual(8500, sale.PriceCents);

            // History appended, never rewritten.
            Assert.Greater(animal.OwnershipHistory.Count, recordsBefore);
            AnimalOwnershipRecord last = animal.OwnershipHistory[animal.OwnershipHistory.Count - 1];
            Assert.AreEqual(AnimalOwnerKind.Business, last.OwnerKind);
            Assert.AreEqual("B1", last.OwnerId);
            Assert.IsTrue(last.IsCurrent);

            // The farm's earlier record closed cleanly.
            AnimalOwnershipRecord first = animal.OwnershipHistory[0];
            Assert.AreEqual(100, first.EndDayIndex);

            // Current owner + pedigree intact.
            Assert.AreEqual(AnimalOwnerKind.Business, animal.OwnerKind);
            Assert.AreEqual("B1", animal.OwnerId);
            Assert.AreEqual(EntityId.For(EntityKind.Animal, 111), animal.Sire);
            Assert.AreEqual(EntityId.For(EntityKind.Animal, 222), animal.Dam);
        }

        [Test]
        public void Sale_RecordsPerHeadTermsAndLedgerProvenance()
        {
            AnimalRegistry registry = NewRegistry();
            AnimalState animal = NewFarmAnimal(registry, "F1");
            var ledger = new HouseholdLedger(7);
            var diagnostics = new List<string>();

            FarmLivestockSale sale = FarmLivestockMarket.ExecuteSale(
                registry, animal.AnimalId, "F1", "Test Farm",
                NewButcher(), "B1", 9200, 100, ledger, diagnostics);

            Assert.IsNotNull(sale);
            Assert.AreEqual(9200, sale.PriceCents);

            Assert.AreEqual(1, ledger.Entries.Count);
            Assert.AreEqual(HouseholdIncomeSource.SaleProceeds, ledger.Entries[0].Source);
            Assert.AreEqual(animal.AnimalId.ToString(), ledger.Entries[0].SourceReference);
            Assert.AreEqual(9200, ledger.GetBalanceCents());
        }

        [Test]
        public void Sale_RejectedWhenAnimalNotFarmOwned()
        {
            AnimalRegistry registry = NewRegistry();
            // Registered to a household, not the farm.
            AnimalState animal = registry.RegisterAnimal(
                AnimalSpecies.Cattle, "Angus", AnimalSex.Female,
                AnimalOwnerKind.Household, "H1", 50, "household cow");

            var diagnostics = new List<string>();
            FarmLivestockSale sale = FarmLivestockMarket.ExecuteSale(
                registry, animal.AnimalId, "F1", "Test Farm",
                NewButcher(), "B1", 8500, 100,
                new HouseholdLedger(7), diagnostics);

            Assert.IsNull(sale);
            Assert.AreEqual(AnimalOwnerKind.Household, animal.OwnerKind);
            Assert.AreEqual("H1", animal.OwnerId);
        }

        [Test]
        public void Sale_RejectedForInactiveAnimal()
        {
            AnimalRegistry registry = NewRegistry();
            AnimalState animal = NewFarmAnimal(registry, "F1");
            animal.IsActive = false;

            var diagnostics = new List<string>();
            FarmLivestockSale sale = FarmLivestockMarket.ExecuteSale(
                registry, animal.AnimalId, "F1", "Test Farm",
                NewButcher(), "B1", 8500, 100,
                new HouseholdLedger(7), diagnostics);

            Assert.IsNull(sale);
        }
    }
}
