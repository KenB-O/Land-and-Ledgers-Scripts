// Copyright (c) 2026 Land & Ledgers. All rights reserved.
// Pass V P1-3: end-to-end generation through the Canon 12.6G order (persons
// first, demographics, kinship/marriage, households FROM family units,
// accommodation, economic roles, validation). Pins the 55-95 calibration,
// birth-date authority, and kinship coherence of the generated town.
using System.Collections.Generic;
using System.Linq;
using LandLedgers.Population;
using LandLedgers.World;
using NUnit.Framework;
using UnityEngine;

namespace LandLedgers.EditorTests.Population
{
    [TestFixture]
    public sealed class PopulationGeneratorIntegrationTests
    {
        private TownWorldController townWorld;
        private PopulationGenerationSettings settings;
        private KinshipRegistry kinship;

        [SetUp]
        public void SetUp()
        {
            var plots = new List<TownPlot>();
            var buildings = new List<PlacedBuilding>();
            for (int i = 0; i < 24; i++)
            {
                plots.Add(new TownPlot { id = i, zone = PlotZone.Residential });
                var definition = ScriptableObject.CreateInstance<BuildingDefinition>();
                definition.ConfigureRuntimeFallback(
                    "house_" + i, "House " + i, PlotZone.Residential,
                    new Vector2Int(3, 3), Color.white, 3f);
                buildings.Add(new PlacedBuilding
                {
                    id = i,
                    plotId = i,
                    definition = definition,
                });
            }

            townWorld = new TownWorldController
            {
                Settings = new TownGenerationSettings { seed = 1886 },
                Grid = new TownGrid(60, 60, 2f, Vector3.zero),
                Plots = plots,
                Buildings = buildings,
            };

            settings = ScriptableObject.CreateInstance<PopulationGenerationSettings>();
            settings.seedOffset = 42;
            settings.targetStartingResidents = 78;
            kinship = new KinshipRegistry();
        }

        [Test]
        public void Generate_ProducesCalibratedCohort_WithBirthDates_AndKinship()
        {
            PopulationGenerationResult result = PopulationGenerator.Generate(
                townWorld, settings, null, kinship, 1892, 3, 1);

            Assert.IsFalse(result.HasErrors, string.Join("; ", result.State.validationMessages));
            PopulationState state = result.State;

            // 55-95 calibration is untouched.
            Assert.GreaterOrEqual(state.people.Count, 55);
            Assert.LessOrEqual(state.people.Count, 95);

            // Household envelope from settings.
            Assert.GreaterOrEqual(state.households.Count, 14);
            Assert.LessOrEqual(state.households.Count, 22);

            // No duplicate person ids.
            var seen = new HashSet<int>();
            foreach (PersonState p in state.people)
            {
                Assert.IsTrue(seen.Add(p.id), $"Duplicate person id {p.id}.");
            }

            // Birth-date authority: every person has a valid generated-exact
            // date and the derived age agrees with it.
            foreach (PersonState p in state.people)
            {
                Assert.IsTrue(p.birthDate.IsValid, $"P{p.id} has no valid birth date.");
                Assert.AreEqual(BirthDateProvenance.GeneratedExact, p.birthDate.Provenance);
                Assert.AreEqual(
                    AgeAuthority.CompletedYears(p.birthDate, 1892, 3, 1), p.age,
                    $"P{p.id} age cache disagrees with the birth date.");
            }

            // Kinship: generation creates real spouse and parent/child edges.
            Assert.Greater(kinship.AllEdges.Count, 0, "No kinship edges were created.");
            int spousePairs = state.people.Sum(p => kinship.GetSpouses(p.id).Count) / 2;
            Assert.Greater(spousePairs, 0, "No married couples were created.");
        }

        [Test]
        public void Generate_KinshipIsTemporallyCoherent()
        {
            PopulationGenerationResult result = PopulationGenerator.Generate(
                townWorld, settings, null, kinship, 1892, 3, 1);
            Assert.IsFalse(result.HasErrors);

            foreach (PersonState p in result.State.people)
            {
                foreach (int parentId in kinship.GetParents(p.id))
                {
                    PersonState parent = result.State.GetPerson(parentId);
                    Assert.IsNotNull(parent);
                    int gap = parent.age - p.age;
                    Assert.GreaterOrEqual(gap, 17, $"Parent/child gap {gap} is incoherent.");
                    Assert.LessOrEqual(gap, 60, $"Parent/child gap {gap} is implausible.");
                }

                foreach (int spouseId in kinship.GetSpouses(p.id))
                {
                    PersonState spouse = result.State.GetPerson(spouseId);
                    Assert.IsNotNull(spouse);
                    CollectionAssert.Contains(kinship.GetSpouses(spouseId), p.id,
                        "Spouse edge is not symmetric.");
                }
            }
        }

        [Test]
        public void Generate_IsDeterministicInSeedOffset()
        {
            PopulationGenerationResult a = PopulationGenerator.Generate(
                townWorld, settings, null, new KinshipRegistry(), 1892, 3, 1);
            PopulationGenerationResult b = PopulationGenerator.Generate(
                townWorld, settings, null, new KinshipRegistry(), 1892, 3, 1);

            Assert.AreEqual(a.State.people.Count, b.State.people.Count);
            Assert.AreEqual(a.State.people.Sum(p => p.age), b.State.people.Sum(p => p.age));
        }
    }
}
