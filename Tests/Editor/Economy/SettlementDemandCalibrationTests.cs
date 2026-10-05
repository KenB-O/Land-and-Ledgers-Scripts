using System.Linq;
using LandLedgers.Economy;
using LandLedgers.Economy.Market;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    public sealed class SettlementDemandCalibrationTests
    {
        [Test]
        public void PopulationChangesDemandPressureWithoutSpawningBusinesses()
        {
            var small = new TerritoryCensus
            {
                EffectiveMarketPopulation = 15,
                TownHouseholdCount = 6,
                CatchmentFarmCount = 2,
                AvgCatchmentMiles = 4f,
                Role = SettlementRole.AgriculturalServiceTown
            };
            var large = new TerritoryCensus
            {
                EffectiveMarketPopulation = 150,
                TownHouseholdCount = 45,
                CatchmentFarmCount = 12,
                AvgCatchmentMiles = 8f,
                Role = SettlementRole.AgriculturalServiceTown
            };

            var drivers = new DemandDriverSet();
            var smallOpportunities = drivers.EvaluateAll(small);
            var largeOpportunities = drivers.EvaluateAll(large);

            float smallStore = smallOpportunities.Single(x => x.BusinessType == BusinessType.GeneralStore).Score01;
            float largeStore = largeOpportunities.Single(x => x.BusinessType == BusinessType.GeneralStore).Score01;
            float smallFreight = smallOpportunities.Single(x => x.BusinessType == BusinessType.LiveryFreight).Score01;
            float largeFreight = largeOpportunities.Single(x => x.BusinessType == BusinessType.LiveryFreight).Score01;
            Assert.Greater(largeStore, smallStore);
            Assert.Greater(largeFreight, smallFreight);
            Assert.That(smallOpportunities, Is.Not.Null, "Demand produces opportunities, not businesses.");
        }

        [Test]
        public void RoleAndCatchmentCanOutweighBuiltUpPopulation()
        {
            var crossroads = new TerritoryCensus
            {
                EffectiveMarketPopulation = 15,
                CatchmentFarmCount = 20,
                AvgCatchmentMiles = 12f,
                Role = SettlementRole.Railhead
            };
            var isolated = new TerritoryCensus
            {
                EffectiveMarketPopulation = 150,
                CatchmentFarmCount = 1,
                AvgCatchmentMiles = 2f,
                Role = SettlementRole.Unspecified
            };

            var driver = new LiveryFreightDemandDriver();
            Assert.Greater(driver.Evaluate(crossroads).Score01, driver.Evaluate(isolated).Score01);
        }
    }
}
