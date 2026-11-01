using System.Collections.Generic;
using System.Linq;
using LandLedgers.Animals;
using LandLedgers.Economy.Farming.Bootstrap;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy.Farming
{
    /// <summary>
    /// FVS-1: per-scenario farm bootstrap. Definitions validate loudly, the
    /// seeded-randomness policy reproduces, animals land in the HF-2 registry
    /// with correct lactation states, and the layout hands zones to the future
    /// journey model.
    /// </summary>
    [TestFixture]
    public sealed class FarmBootstrapTests
    {
        private FarmDefinition ValidDairyDefinition()
        {
            var def = new FarmDefinition
            {
                FarmId = "test-dairy-farm",
                DisplayName = "Test Dairy Farm",
                HouseholdId = 7,
                LocationDescription = "2 miles east of town",
                DistanceToTownMiles = 2f,
                RandomSeed = 42,
                StartingFeedUnits = 500,
            };
            def.LandInterests.Add(new FarmLandInterest(FarmLandInterestKind.FeeOwned, 160f, "home quarter"));
            def.Buildings.Add(new FarmBuilding(FarmBuildingKind.Farmhouse, "farmhouse", 0));
            def.Buildings.Add(new FarmBuilding(FarmBuildingKind.DairyBarn, "dairy barn", 20));
            def.Buildings.Add(new FarmBuilding(FarmBuildingKind.Coop, "hen coop", 40));
            def.Plots.Add(new FarmPlot(FarmPlotKind.Pasture, 60f, "north pasture"));
            def.Plots.Add(new FarmPlot(FarmPlotKind.HayField, 20f, "hay field", "timothy"));
            def.InitialHerd.Add(new AuthoredAnimal(AnimalSpecies.Cattle, AnimalSex.Female, 6,
                AnimalBiologicalState.EligibleOrCycling, lactatingAtStart: 4, breed: "Shorthorn"));
            def.InitialHerd.Add(new AuthoredAnimal(AnimalSpecies.Chicken, AnimalSex.Female, 12,
                AnimalBiologicalState.EligibleOrCycling, breed: "Plymouth Rock"));
            def.Equipment.Add("churn");
            def.Equipment.Add("milk pails x4");
            return def;
        }

        [Test]
        public void ValidDefinition_BootstrapsHerdLayoutAndPremises()
        {
            var def = ValidDairyDefinition();
            var registry = new AnimalRegistry(new EntityIdRegistry());
            var diagnostics = new List<string>();

            FarmInstance instance = FarmBootstrap.Bootstrap(def, registry, 100, diagnostics);

            Assert.IsNotNull(instance, "Valid definition was refused: " + string.Join(" | ", diagnostics));
            Assert.AreEqual(18, instance.AnimalIds.Count, "6 cattle + 12 chickens expected.");

            int lactating = 0;
            foreach (var id in instance.AnimalIds)
            {
                var animal = registry.GetAnimal(id);
                Assert.IsNotNull(animal);
                Assert.AreEqual(AnimalOwnerKind.Business, animal.OwnerKind);
                Assert.AreEqual("farm:test-dairy-farm", animal.OwnerId);
                if (animal.Species == AnimalSpecies.Cattle && animal.BiologicalState == AnimalBiologicalState.Lactating)
                {
                    lactating++;
                    Assert.GreaterOrEqual(animal.Parity, 1, "Lactating cow must have calved (Tech X §4.5).");
                }
            }
            Assert.AreEqual(4, lactating, "Authored lactating count must hold exactly.");

            Assert.IsNotNull(instance.Layout);
            Assert.IsTrue(instance.Layout.Zones.Count >= 5, "Expected zones for buildings + plots + yard.");
            Assert.IsNotNull(instance.DairyPremises, "Dairy premises must resolve via BIZ-1 logic.");
        }

        [Test]
        public void SameSeed_ReproducesSameLactatingSet()
        {
            var defA = ValidDairyDefinition();
            var defB = ValidDairyDefinition();

            var regA = new AnimalRegistry(new EntityIdRegistry());
            var regB = new AnimalRegistry(new EntityIdRegistry());
            var instA = FarmBootstrap.Bootstrap(defA, regA, 100, new List<string>());
            var instB = FarmBootstrap.Bootstrap(defB, regB, 100, new List<string>());

            Assert.IsNotNull(instA);
            Assert.IsNotNull(instB);

            var lactA = new HashSet<EntityId>(instA.AnimalIds.Where(id =>
                regA.GetAnimal(id).Species == AnimalSpecies.Cattle &&
                regA.GetAnimal(id).BiologicalState == AnimalBiologicalState.Lactating));
            var lactB = new HashSet<EntityId>(instB.AnimalIds.Where(id =>
                regB.GetAnimal(id).Species == AnimalSpecies.Cattle &&
                regB.GetAnimal(id).BiologicalState == AnimalBiologicalState.Lactating));

            Assert.IsTrue(lactA.SetEquals(lactB), "Same seed must pick the same lactating individuals.");
        }

        [Test]
        public void MilkingHerdWithoutDairyBarn_IsRefusedLoudly()
        {
            var def = ValidDairyDefinition();
            def.Buildings.RemoveAll(b => b.Kind == FarmBuildingKind.DairyBarn);
            var diagnostics = new List<string>();

            FarmInstance instance = FarmBootstrap.Bootstrap(def, new AnimalRegistry(new EntityIdRegistry()), 100, diagnostics);

            Assert.IsNull(instance, "Milking herd without a dairy barn must be refused.");
            Assert.IsTrue(diagnostics.Any(d => d.Contains("dairy barn")),
                "Diagnostics must name the missing dairy barn. Got: " + string.Join(" | ", diagnostics));
        }

        [Test]
        public void ChickensWithoutCoop_AreRefused()
        {
            var def = ValidDairyDefinition();
            def.Buildings.RemoveAll(b => b.Kind == FarmBuildingKind.Coop);
            var diagnostics = new List<string>();

            Assert.IsNull(FarmBootstrap.Bootstrap(def, new AnimalRegistry(new EntityIdRegistry()), 100, diagnostics));
            Assert.IsTrue(diagnostics.Any(d => d.Contains("coop")));
        }

        [Test]
        public void OpenRangeAcres_DoNotCountAsControlledLand()
        {
            var def = ValidDairyDefinition();
            def.LandInterests.Clear();
            def.LandInterests.Add(new FarmLandInterest(FarmLandInterestKind.OpenRangeUse, 1000f, "open range"));
            var diagnostics = new List<string>();

            Assert.IsNull(FarmBootstrap.Bootstrap(def, new AnimalRegistry(new EntityIdRegistry()), 100, diagnostics),
                "Open range alone must not satisfy the secure-land requirement (Canon §10.3).");
            Assert.IsTrue(diagnostics.Any(d => d.Contains("secure land")));
        }

        [Test]
        public void AcreageCategories_StaySeparate()
        {
            var def = ValidDairyDefinition();
            def.LandInterests.Add(new FarmLandInterest(FarmLandInterestKind.SeasonalGrazing, 40f, "summer range"));

            def.GetAcreageByCategory(out float owned, out float leased, out float grazing, out float open);

            Assert.AreEqual(160f, owned);
            Assert.AreEqual(0f, leased);
            Assert.AreEqual(40f, grazing);
            Assert.AreEqual(0f, open);
        }

        [Test]
        public void Layout_WiresBarnYardAdjacency()
        {
            var layout = FarmLayout.BuildFromDefinition(ValidDairyDefinition());

            Assert.IsTrue(layout.AreAdjacent("bldg-dairy-barn", "yard"), "Barn must touch the working yard.");
            Assert.IsTrue(layout.AreAdjacent("plot-north-pasture", "yard"), "Pasture must touch the yard.");
            Assert.IsNotNull(layout.GetZone("bldg-hen-coop"));
        }
    }
}
