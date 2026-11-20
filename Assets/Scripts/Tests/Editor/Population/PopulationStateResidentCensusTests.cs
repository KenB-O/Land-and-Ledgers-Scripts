using LandLedgers.Population;
using LandLedgers.Orchestration.Systems;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    public sealed class PopulationStateResidentCensusTests
    {
        [Test]
        public void LivingResidentCount_ExcludesOffMapKinAndDeceasedIdentities()
        {
            PopulationState state = new PopulationState();
            for (int i = 0; i < 14; i++)
            {
                state.people.Add(new PersonState
                {
                    id = i,
                    firstName = "Resident",
                    lastName = i.ToString(),
                    isOffMap = false,
                    deathDayIndex = -1
                });
            }
            for (int i = 14; i < 98; i++)
            {
                state.people.Add(new PersonState
                {
                    id = i,
                    firstName = "Relative",
                    lastName = i.ToString(),
                    isOffMap = true,
                    deathDayIndex = -1
                });
            }
            state.people.Add(new PersonState { id = 98, isOffMap = false, deathDayIndex = 12 });
            state.people.Add(null);

            Assert.AreEqual(100, state.people.Count, "all identity records remain stored");
            Assert.AreEqual(14, state.LivingResidentCount);
            Assert.IsTrue(state.HasExactLivingResidentCount(14));
            Assert.IsFalse(state.HasExactLivingResidentCount(98));
            Assert.IsFalse(state.HasExactLivingResidentCount(-1));
        }

        [Test]
        public void EmptyOrMissingPeopleCannotPassAnExpectedRosterCount()
        {
            PopulationState state = new PopulationState();

            Assert.AreEqual(0, state.LivingResidentCount);
            Assert.IsFalse(state.HasExactLivingResidentCount(14));

            state.people = null;
            Assert.AreEqual(0, state.LivingResidentCount);
            Assert.IsFalse(state.HasExactLivingResidentCount(14));
        }

        [Test]
        public void MealDaySummary_UsesLivingResidentsRatherThanStoredIdentityRecords()
        {
            PopulationState state = new PopulationState();
            for (int i = 0; i < 14; i++)
                state.people.Add(new PersonState { id = i, isOffMap = false, deathDayIndex = -1 });
            for (int i = 14; i < 98; i++)
                state.people.Add(new PersonState { id = i, isOffMap = true, deathDayIndex = -1 });

            string summary = SimulationDrivers.FormatCompletedMealDaySummary(0, 42, 0, state, 3);

            StringAssert.Contains("out of 14 residents × 3 slots", summary);
            StringAssert.DoesNotContain("out of 98 residents", summary);
        }
    }
}
