using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Animals
{
    /// <summary>
    /// HF-2: animal identity stability, never-reuse, append-only ownership history,
    /// historical compaction, cohort promotion, and the poultry lifecycle hooks
    /// (Tech X Part III, §2.3).
    /// </summary>
    [TestFixture]
    public sealed class AnimalRegistryTests
    {
        private AnimalRegistry CreateRegistry()
        {
            return new AnimalRegistry(new EntityIdRegistry());
        }

        [Test]
        public void RegisterAnimal_AllocatesStableTypedId()
        {
            AnimalRegistry registry = CreateRegistry();

            AnimalState hen = registry.RegisterAnimal(
                AnimalSpecies.Chicken, "Rhode Island Red", AnimalSex.Female,
                AnimalOwnerKind.Household, "H1", 100, "test");

            Assert.IsNotNull(hen);
            Assert.AreEqual(EntityKind.Animal, hen.AnimalId.Kind);
            Assert.AreEqual(0, hen.AnimalId.Id);
            Assert.AreEqual("A0", hen.AnimalId.ToString());
            Assert.AreSame(hen, registry.GetAnimal(hen.AnimalId));
        }

        [Test]
        public void Disposition_NeverReusesId()
        {
            AnimalRegistry registry = CreateRegistry();
            AnimalState cow = registry.RegisterAnimal(
                AnimalSpecies.Cattle, "Shorthorn", AnimalSex.Female,
                AnimalOwnerKind.Person, "P3", 100, "test");

            Assert.IsNull(registry.RecordDisposition(cow.AnimalId, AnimalCommercialStatus.Deceased, 200, "cold snap"));

            AnimalState replacement = registry.RegisterAnimal(
                AnimalSpecies.Cattle, "Shorthorn", AnimalSex.Female,
                AnimalOwnerKind.Person, "P3", 201, "replacement");

            Assert.AreNotEqual(cow.AnimalId, replacement.AnimalId);
            Assert.Greater(replacement.AnimalId.Id, cow.AnimalId.Id);
        }

        [Test]
        public void TransferOwnership_AppendsHistory_NeverRewrites()
        {
            AnimalRegistry registry = CreateRegistry();
            AnimalState pig = registry.RegisterAnimal(
                AnimalSpecies.Pig, "Berkshire", AnimalSex.Male,
                AnimalOwnerKind.Household, "H1", 100, "initial");

            Assert.IsNull(registry.TransferOwnership(pig.AnimalId, AnimalOwnerKind.Business, "B7", 150, "sold"));
            Assert.IsNull(registry.TransferOwnership(pig.AnimalId, AnimalOwnerKind.Household, "H2", 180, "returned"));

            Assert.AreEqual(3, pig.OwnershipHistory.Count);
            Assert.AreEqual(100, pig.OwnershipHistory[0].StartDayIndex);
            Assert.AreEqual(150, pig.OwnershipHistory[0].EndDayIndex); // closed, not rewritten
            Assert.AreEqual(-1, pig.OwnershipHistory[2].EndDayIndex); // current tenure open
            Assert.AreEqual("H2", pig.OwnerId);
        }

        [Test]
        public void CompactInactive_RetainsIdentifiers_And_Pedigree()
        {
            AnimalRegistry registry = CreateRegistry();
            AnimalState dam = registry.RegisterAnimal(
                AnimalSpecies.Cattle, "Shorthorn", AnimalSex.Female,
                AnimalOwnerKind.Household, "H1", 50, "test");
            AnimalState calf = registry.RegisterAnimal(
                AnimalSpecies.Cattle, "Shorthorn", AnimalSex.Male,
                AnimalOwnerKind.Household, "H1", 400, "test");
            calf.Dam = dam.AnimalId;
            registry.RecordDisposition(calf.AnimalId, AnimalCommercialStatus.Sold, 700, "sold at market");

            int compacted = registry.CompactInactive(701);

            Assert.AreEqual(1, compacted);
            Assert.IsNull(registry.GetAnimal(calf.AnimalId));
            HistoricalAnimalRecord historical = registry.GetHistorical(calf.AnimalId);
            Assert.IsNotNull(historical);
            Assert.AreEqual(calf.AnimalId, historical.AnimalId);
            Assert.AreEqual(dam.AnimalId, historical.Dam); // pedigree retained
            Assert.AreEqual(AnimalCommercialStatus.Sold, historical.FinalStatus);
        }

        [Test]
        public void CohortPromotion_MovesHeadToIndividual_WithoutDuplicateOwnership()
        {
            AnimalRegistry registry = CreateRegistry();
            LivestockCohort cohort = registry.RegisterCohort(
                AnimalSpecies.Sheep, "Merino", 10, AnimalOwnerKind.Household, "H1", 100);

            AnimalState individual = registry.PromoteCohortHead(cohort.CohortId, AnimalSex.Female, 200, "breeding prospect");

            Assert.IsNotNull(individual);
            Assert.AreEqual(9, cohort.HeadCount);
            Assert.AreEqual(cohort.CohortId, individual.CohortId);
            Assert.AreEqual(cohort.OwnerId, individual.OwnerId); // ownership moved, not duplicated
            Assert.AreSame(individual, registry.GetAnimal(individual.AnimalId));
        }

        [Test]
        public void PoultryHatch_RecordsMaleChicks_WithParentage()
        {
            AnimalRegistry registry = CreateRegistry();
            AnimalState hen = registry.RegisterAnimal(
                AnimalSpecies.Chicken, "Leghorn", AnimalSex.Female,
                AnimalOwnerKind.Household, "H1", 100, "test");
            AnimalState rooster = registry.RegisterAnimal(
                AnimalSpecies.Chicken, "Leghorn", AnimalSex.Male,
                AnimalOwnerKind.Household, "H1", 100, "test");

            EggBatchState batch = PoultryLifecycle.LayClutch(
                registry, hen.AnimalId, rooster.AnimalId, 12, 120, 5);
            Assert.IsNotNull(batch);
            Assert.AreEqual(EntityKind.Lot, batch.BatchId.Kind);

            List<AnimalState> chicks = PoultryLifecycle.RecordHatch(registry, batch.BatchId, 141, 3, 2, 1);

            Assert.AreEqual(6, chicks.Count);
            int males = 0;
            foreach (AnimalState chick in chicks)
            {
                Assert.AreEqual(hen.AnimalId, chick.Dam);
                Assert.AreEqual(rooster.AnimalId, chick.Sire);
                Assert.AreEqual(141, chick.BirthDayIndex);
                if (chick.Sex == AnimalSex.Male)
                {
                    males++;
                }
            }

            Assert.AreEqual(3, males, "Male chicks must remain present in records (Tech X §2.3).");
            Assert.AreEqual(6, batch.HatchedChickCount);
        }

        [Test]
        public void DuplicateRegistration_Rejected()
        {
            AnimalRegistry registry = CreateRegistry();
            AnimalState first = registry.RegisterAnimal(
                AnimalSpecies.Horse, "Morgan", AnimalSex.Female,
                AnimalOwnerKind.Person, "P1", 100, "test");

            var duplicate = new AnimalState { AnimalId = first.AnimalId, Species = AnimalSpecies.Horse };
            string rejection = registry.ValidateAndAdd(duplicate);

            Assert.IsNotNull(rejection);
            Assert.AreSame(first, registry.GetAnimal(first.AnimalId));
        }

        [Test]
        public void HealthIncident_SharesCausalExposure_AcrossAnimals()
        {
            AnimalRegistry registry = CreateRegistry();
            AnimalState a1 = registry.RegisterAnimal(AnimalSpecies.Cattle, "", AnimalSex.Female, AnimalOwnerKind.Household, "H1", 100, "t");
            AnimalState a2 = registry.RegisterAnimal(AnimalSpecies.Cattle, "", AnimalSex.Female, AnimalOwnerKind.Household, "H1", 100, "t");

            AnimalHealthIncident incident = registry.RecordHealthIncident(
                300, "cold snap", new[] { a1.AnimalId, a2.AnimalId }, "shared exposure");

            Assert.AreEqual(1, registry.HealthIncidents.Count);
            Assert.AreEqual(2, incident.AffectedAnimalIds.Count);
        }
    }
}
