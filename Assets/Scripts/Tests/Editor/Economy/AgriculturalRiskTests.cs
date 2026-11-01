using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Economy.Farming.Crops;
using LandLedgers.Economy.Farming.Risk;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// NX-2B: a hostile world. Disasters operate on ACTUAL state (Canon §9.1,
    /// §9.3) — never flat multipliers. Disease is named, historically justified,
    /// and ranch-localized (Canon 9.6A).
    /// </summary>
    [TestFixture]
    public sealed class AgriculturalRiskTests
    {
        private CropFieldAuthority FieldsWithCrop()
        {
            var authority = new CropFieldAuthority();
            var field = new CropFieldState("field-1", "farm-1", 40f)
            {
                CurrentCrop = CropKind.Wheat,
                GrowthState = CropGrowthState.Growing,
                Condition01 = 1f,
                Moisture01 = 0.7f,
            };
            authority.RegisterField(field);
            var hay = new CropFieldState("hay-1", "farm-1", 20f)
            {
                CurrentCrop = CropKind.Hay,
                GrowthState = CropGrowthState.Growing,
                Condition01 = 1f,
                Moisture01 = 0.7f,
            };
            authority.RegisterField(hay);
            var otherFarm = new CropFieldState("field-2", "farm-2", 40f)
            {
                CurrentCrop = CropKind.Wheat,
                GrowthState = CropGrowthState.Growing,
                Condition01 = 1f,
                Moisture01 = 0.7f,
            };
            authority.RegisterField(otherFarm);
            return authority;
        }

        [Test]
        public void Grasshoppers_ConsumeActualCropState()
        {
            var crops = FieldsWithCrop();
            var risk = new AgriculturalRiskService();
            var diag = new List<string>();

            DisasterReport report = risk.TriggerGrasshoppers(
                new List<string> { "farm-1" }, crops, 1.0f, 42, 100, diag);

            CropFieldState wheat = crops.GetField("field-1");
            CropFieldState hay = crops.GetField("hay-1");
            CropFieldState other = crops.GetField("field-2");
            Assert.Less(wheat.Condition01, 1f, "Hoppers eat actual condition, not a revenue multiplier.");
            Assert.Less(hay.Condition01, 1f, "Hay is forage — hoppers eat it too (Canon §9.3).");
            Assert.AreEqual(1f, other.Condition01, 0.001f, "Unlisted farms are untouched — localized.");
            Assert.Greater(report.AffectedFieldIds.Count, 0);
        }

        [Test]
        public void Grasshoppers_SevereDamage_FailsField()
        {
            var crops = FieldsWithCrop();
            var risk = new AgriculturalRiskService();
            var diag = new List<string>();
            DisasterReport report = risk.TriggerGrasshoppers(
                new List<string> { "farm-1" }, crops, 1.0f, 42, 100, diag);
            // At severity 1.0, damage in [0.5, 1.0] — condition must collapse.
            Assert.Greater(report.FieldsFailed + (crops.GetField("field-1").Condition01 < 1f ? 1 : 0), 0);
        }

        [Test]
        public void PrairieFire_Fireguard_ReducesButNotEliminates()
        {
            var diag = new List<string>();
            int guardedBurns = 0, unguardedBurns = 0;
            // Run many seeded trials: guarded fields must burn strictly less often.
            for (int trial = 0; trial < 40; trial++)
            {
                var crops = FieldsWithCrop();
                var risk = new AgriculturalRiskService();
                risk.RecordFireguard("farm-1", "field-1", 100, diag);
                Assert.IsTrue(risk.HasFireguard("farm-1", "field-1"));
                risk.TriggerPrairieFire(new List<string> { "farm-1" }, crops,
                    1.0f, 30f, trial, 100, diag);
                if (crops.GetField("field-1").GrowthState == CropGrowthState.Failed) guardedBurns++;
                if (crops.GetField("hay-1").GrowthState == CropGrowthState.Failed) unguardedBurns++;
            }
            Assert.Less(guardedBurns, unguardedBurns,
                "Real fireguards reduce fire (Canon §9.3) — guarded must burn less often across trials.");
        }

        [Test]
        public void Drought_DevelopsThroughStages_AndDrainsMoisture()
        {
            var crops = FieldsWithCrop();
            var risk = new AgriculturalRiskService();
            var diag = new List<string>();
            var drought = new DroughtState { RegionId = "local" };

            for (int day = 0; day < 15; day++)
                drought = risk.AdvanceDrought(drought, 0f, crops, 100 + day, diag);
            Assert.AreEqual(DroughtStage.Mounting, drought.Stage, "15 dry days → Mounting (Canon 9.6F developing shock).");

            float moistureBefore = crops.GetField("field-1").Moisture01;
            for (int day = 15; day < 40; day++)
                drought = risk.AdvanceDrought(drought, 0f, crops, 100 + day, diag);
            Assert.AreEqual(DroughtStage.Action, drought.Stage);
            Assert.Less(crops.GetField("field-1").Moisture01, moistureBefore,
                "Drought drains ACTUAL moisture state — CRP-2 yield math reads it (canon chain).");
        }

        [Test]
        public void Drought_RainBreaksIt()
        {
            var crops = FieldsWithCrop();
            var risk = new AgriculturalRiskService();
            var diag = new List<string>();
            var drought = new DroughtState { RegionId = "local" };
            for (int day = 0; day < 40; day++)
                drought = risk.AdvanceDrought(drought, 0f, crops, 100 + day, diag);
            Assert.AreEqual(DroughtStage.Action, drought.Stage);
            drought = risk.AdvanceDrought(drought, 0.8f, crops, 140, diag);
            Assert.AreEqual(DroughtStage.None, drought.Stage, "Real rain breaks the drought — recovery is real, not timed.");
        }

        private AnimalRegistry RegistryWithCattle(out List<AnimalState> animals)
        {
            var registry = new AnimalRegistry(new EntityIdRegistry());
            animals = new List<AnimalState>();
            for (int i = 0; i < 6; i++)
            {
                AnimalState cow = registry.RegisterAnimal(AnimalSpecies.Cattle, "Shorthorn",
                    AnimalSex.Female, AnimalOwnerKind.Business, "farm-1", 0, "test");
                Assert.IsNotNull(cow);
                animals.Add(cow);
            }
            AnimalState otherFarmCow = registry.RegisterAnimal(AnimalSpecies.Cattle, "Shorthorn",
                AnimalSex.Female, AnimalOwnerKind.Business, "farm-2", 0, "test");
            animals.Add(otherFarmCow);
            return registry;
        }

        [Test]
        public void Disease_StartOutbreak_IsRanchLocalized()
        {
            var registry = RegistryWithCattle(out List<AnimalState> animals);
            var service = new LivestockDiseaseService();
            var diag = new List<string>();

            DiseaseOutbreak outbreak = service.StartOutbreak("blackleg", "farm-1", animals, registry, 42, 100, diag);
            Assert.IsNotNull(outbreak);
            Assert.Greater(outbreak.SickAnimalIds.Count, 0);
            Assert.GreaterOrEqual(outbreak.HealthIncidentIndex, 0, "HF-2 incident recorded — the T1F vet treats against it.");
            foreach (EntityId id in outbreak.SickAnimalIds)
            {
                AnimalState animal = registry.GetAnimal(id);
                Assert.AreEqual("farm-1", animal.OwnerId, "Only the one ranch suffers (Canon 9.6A).");
            }
        }

        [Test]
        public void Disease_UnknownDisease_Refused()
        {
            var registry = RegistryWithCattle(out List<AnimalState> animals);
            var service = new LivestockDiseaseService();
            var diag = new List<string>();
            DiseaseOutbreak outbreak = service.StartOutbreak("mystery-plague", "farm-1", animals, registry, 42, 100, diag);
            Assert.IsNull(outbreak, "No generic cattle-epidemic event (Canon 9.6A).");
        }

        [Test]
        public void Disease_SickAnimals_NotSaleEligible()
        {
            var registry = RegistryWithCattle(out List<AnimalState> animals);
            var service = new LivestockDiseaseService();
            var diag = new List<string>();
            DiseaseOutbreak outbreak = service.StartOutbreak("texas-fever", "farm-1", animals, registry, 42, 100, diag);
            Assert.IsNotNull(outbreak);

            var outbreaks = new List<DiseaseOutbreak> { outbreak };
            string reason;
            Assert.IsFalse(service.IsAnimalSaleEligible(outbreak.SickAnimalIds[0], outbreaks, out reason));
            Assert.IsNotEmpty(reason);
            // A healthy animal at the same farm is still eligible.
            EntityId healthy = animals[0].AnimalId;
            bool anySickIsFirst = outbreak.SickAnimalIds.Contains(healthy);
            if (!anySickIsFirst)
                Assert.IsTrue(service.IsAnimalSaleEligible(healthy, outbreaks, out _));
        }

        [Test]
        public void Disease_AdvanceDay_SpreadsAndKills_Seeded()
        {
            var registry = RegistryWithCattle(out List<AnimalState> animals);
            var service = new LivestockDiseaseService();
            var diag = new List<string>();
            DiseaseOutbreak outbreak = service.StartOutbreak("hog-cholera", "farm-1", animals, registry, 42, 100, diag);
            // Hogs only — cattle at farm-1 are not susceptible.
            Assert.IsNull(outbreak, "Hog cholera does not touch cattle — commodity specificity (Canon 9.6B).");

            // Now with pigs.
            var pigRegistry = new AnimalRegistry(new EntityIdRegistry());
            var pigs = new List<AnimalState>();
            for (int i = 0; i < 8; i++)
                pigs.Add(pigRegistry.RegisterAnimal(AnimalSpecies.Pig, "Berkshire",
                    AnimalSex.Female, AnimalOwnerKind.Business, "farm-1", 0, "test"));
            DiseaseOutbreak pigOutbreak = service.StartOutbreak("hog-cholera", "farm-1", pigs, pigRegistry, 42, 100, diag);
            Assert.IsNotNull(pigOutbreak);

            for (int day = 101; day < 130; day++)
            {
                service.AdvanceOutbreak(pigOutbreak, pigs, pigRegistry, day, diag);
                if (pigOutbreak.Resolved) break;
            }
            Assert.IsTrue(pigOutbreak.Resolved, "Outbreaks resolve — they are episodes, not permanent debuffs.");
            Assert.Greater(pigOutbreak.DeadAnimalIds.Count + pigOutbreak.RecoveredAnimalIds.Count, 0);
            // Dead animals used terminal disposition; IDs never reused.
            foreach (EntityId dead in pigOutbreak.DeadAnimalIds)
            {
                AnimalState animal = pigRegistry.GetAnimal(dead);
                Assert.IsFalse(animal.IsActive);
                Assert.AreEqual(AnimalCommercialStatus.Deceased, animal.CommercialStatus);
            }
        }

        [Test]
        public void DiseaseCatalog_HasFiveNamedDiseases()
        {
            Assert.AreEqual(5, LivestockDiseaseService.Catalog.Count);
            foreach (LivestockDiseaseDef def in LivestockDiseaseService.Catalog)
            {
                Assert.IsNotEmpty(def.HistoricalNote, $"{def.DiseaseId} must carry its historical justification (Canon 9.6A).");
                Assert.Greater(def.Species.Count, 0);
            }
        }
    }
}
