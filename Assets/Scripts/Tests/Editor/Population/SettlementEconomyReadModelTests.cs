using System.Collections.Generic;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    public sealed class SettlementEconomyReadModelTests
    {
        [Test]
        public void PopulationViewsRemainSeparateAndDeriveFromRealEntities()
        {
            PopulationState state = new PopulationState();
            state.people.Add(new PersonState
            {
                id = 1, ageBand = AgeBand.Adult18Plus,
                laborAccessLevel = LaborAccessLevel.FullLaborMarket,
                homeBuildingId = 10
            });
            state.people.Add(new PersonState
            {
                id = 2, ageBand = AgeBand.Child0To9,
                laborAccessLevel = LaborAccessLevel.None,
                homeBuildingId = 10
            });
            state.people.Add(new PersonState
            {
                id = 3, ageBand = AgeBand.Adult18Plus,
                laborAccessLevel = LaborAccessLevel.FullLaborMarket,
                homeBuildingId = -1
            });
            state.households.Add(new HouseholdState
            {
                id = 1, homeBuildingId = 10, memberIds = new List<int> { 1, 2 }
            });

            SettlementEconomyReadModel report = SettlementEconomyReadModel.Build(
                "Crossroads", state,
                new[] { 3 },
                new[] { 1, 2, 3 },
                new[] { 3 },
                null,
                null);

            Assert.AreEqual(3, report.PersonCount);
            Assert.AreEqual(2, report.BuiltUpResidents);
            Assert.AreEqual(1, report.RuralServicePopulation);
            Assert.AreEqual(3, report.EffectiveMarketPopulation);
            Assert.AreEqual(1, report.TemporaryPresentPopulation);
            Assert.AreEqual(1, report.HouseholdCount);
            Assert.AreEqual(1, report.OccupiedAccommodationCount);
            Assert.AreEqual(2, report.EconomicallyActivePersons);
        }

        [Test]
        public void ReadModelDoesNotTurnPopulationIntoBusinessCount()
        {
            PopulationState small = new PopulationState();
            for (int i = 0; i < 15; i++)
                small.people.Add(new PersonState { id = i, homeBuildingId = 1 });

            PopulationState large = new PopulationState();
            for (int i = 0; i < 150; i++)
                large.people.Add(new PersonState { id = i, homeBuildingId = 1 });

            SettlementEconomyReadModel smallReport = SettlementEconomyReadModel.Build(
                "Small", small, null, null, null, null, null);
            SettlementEconomyReadModel largeReport = SettlementEconomyReadModel.Build(
                "Large", large, null, null, null, null, null);

            Assert.AreEqual(0, smallReport.BusinessCount);
            Assert.AreEqual(0, largeReport.BusinessCount);
            Assert.Greater(largeReport.BuiltUpResidents, smallReport.BuiltUpResidents);
        }
    }
}
