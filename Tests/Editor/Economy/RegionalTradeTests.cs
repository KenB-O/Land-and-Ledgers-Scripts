using System.Collections.Generic;
using LandLedgers.Economy.Market;
using LandLedgers.Economy.Market.RegionalTrade;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// T3A: multi-town / regional trade. Canon master doctrine — settlement
    /// role can outweigh size. No Unity here: pure logic tests.
    /// </summary>
    [TestFixture]
    public sealed class RegionalTradeTests
    {
        private static JourneyModel TwoTownModel()
        {
            var model = new JourneyModel();
            model.RegisterLocation(new JourneyLocation("ash-creek", JourneyLocationKind.TownBuilding, "Ash Creek", 0f, 0f));
            model.RegisterLocation(new JourneyLocation("granite-jct", JourneyLocationKind.TownBuilding, "Granite Junction", 30f, 0f));
            model.RegisterLocation(new JourneyLocation("farm-north", JourneyLocationKind.Farmstead, "North Farm", 5f, 0f));
            model.RegisterLocation(new JourneyLocation("farm-east", JourneyLocationKind.Farmstead, "East Farm", 25f, 0f));
            model.AddEdge("ash-creek", "farm-north", 5f, "north road");
            model.AddEdge("granite-jct", "farm-east", 5f, "east road");
            model.AddEdge("ash-creek", "granite-jct", 30f, "county road");
            return model;
        }

        private static RegionalTradeService TwoTownService()
        {
            var service = new RegionalTradeService();
            var diag = new List<string>();
            service.RegisterSettlement(new Settlement
            {
                SettlementId = "ash", Name = "Ash Creek", LocationId = "ash-creek",
                Role = SettlementRole.AgriculturalServiceTown, ResidentCount = 400,
            }, diag);
            service.RegisterSettlement(new Settlement
            {
                SettlementId = "gj", Name = "Granite Junction", LocationId = "granite-jct",
                Role = SettlementRole.Railhead, ResidentCount = 350, HasRailAccess = true,
            }, diag);
            return service;
        }

        [Test]
        public void Catchments_ExcludeCompetitorCloserUnits()
        {
            var service = TwoTownService();
            var journeys = TwoTownModel();
            var units = new List<RuralUnit>
            {
                new RuralUnit { UnitId = "u1", LocationId = "farm-north", IsFarm = true, ResidentCount = 6 },
                new RuralUnit { UnitId = "u2", LocationId = "farm-east", IsFarm = true, ResidentCount = 4 },
            };
            var results = service.ComputeCatchments(units, journeys, new List<string>());
            Assert.IsTrue(results["ash"].UnitsInCatchment.Exists(u => u.UnitId == "u1"),
                "North farm belongs to Ash Creek.");
            Assert.IsFalse(results["ash"].UnitsInCatchment.Exists(u => u.UnitId == "u2"),
                "East farm is strictly closer to Granite Junction — competitive exclusion.");
            Assert.IsTrue(results["gj"].UnitsInCatchment.Exists(u => u.UnitId == "u2"));
        }

        [Test]
        public void FindArbitrage_PositiveNetOnly()
        {
            var service = TwoTownService();
            var journeys = TwoTownModel();
            var observations = new List<PriceObservation>
            {
                // Ash Creek merchant sells flour at 10c, buys at 6c.
                new PriceObservation { SettlementId = "ash", MerchantName = "Ash General", GoodKind = "flour", BuyCents = 6, SellCents = 10, DayIndex = 1 },
                // Granite Junction merchant buys flour at 20c, sells at 24c.
                new PriceObservation { SettlementId = "gj", MerchantName = "GJ Mercantile", GoodKind = "flour", BuyCents = 20, SellCents = 24, DayIndex = 1 },
            };
            var opportunities = service.FindArbitrage(observations, 100, journeys, new List<string>());
            Assert.AreEqual(1, opportunities.Count, "Exactly one direction is profitable.");
            ArbitrageOpportunity opp = opportunities[0];
            Assert.AreEqual("ash", opp.BuySettlementId);
            Assert.AreEqual("gj", opp.SellSettlementId);
            Assert.AreEqual((20 - 10) * 100, opp.GrossMarginCents);
            Assert.Greater(opp.FreightCents, 0, "Freight is honestly priced, never zero.");
            Assert.AreEqual(opp.GrossMarginCents - opp.FreightCents, opp.NetCents);
            Assert.Greater(opp.NetCents, 0);
        }

        [Test]
        public void FindArbitrage_NoObservations_NoOpportunities()
        {
            var service = TwoTownService();
            var opportunities = service.FindArbitrage(new List<PriceObservation>(), 100, TwoTownModel(), new List<string>());
            Assert.AreEqual(0, opportunities.Count, "No observations → no opportunities, none invented.");
        }

        [Test]
        public void FindArbitrage_LossAfterFreight_Excluded()
        {
            var service = TwoTownService();
            var journeys = TwoTownModel();
            var observations = new List<PriceObservation>
            {
                new PriceObservation { SettlementId = "ash", MerchantName = "Ash General", GoodKind = "nails", BuyCents = 5, SellCents = 8, DayIndex = 1 },
                // Tiny margin: 9c buy vs 8c sell = 1c/unit gross; freight for 100 units over 30 mi exceeds it.
                new PriceObservation { SettlementId = "gj", MerchantName = "GJ Mercantile", GoodKind = "nails", BuyCents = 9, SellCents = 12, DayIndex = 1 },
            };
            var opportunities = service.FindArbitrage(observations, 100, journeys, new List<string>());
            Assert.AreEqual(0, opportunities.Count, "Margin eaten by freight — honestly excluded.");
        }

        [Test]
        public void FreightCost_RailCheaperThanWagon_WhenBothRailServed()
        {
            var service = TwoTownService();
            var diag = new List<string>();
            // Make Ash Creek rail-served too, with a rail connection.
            service.Settlements[0].HasRailAccess = true;
            service.AddRailConnection(new RailConnection
            {
                FromLocationId = "ash-creek", ToLocationId = "granite-jct",
                Miles = 30f, CentsPerMile = 1, // rail cheaper than wagon's 2c/unit-mi
            });
            var journeys = TwoTownModel();
            int railCost = service.FreightCostCents("ash-creek", "granite-jct", 100, journeys, diag, out bool usesRail, out int railMinutes);
            Assert.IsTrue(usesRail, "Both rail-served with a connection → rail used.");
            Assert.AreEqual(30 * 1 * 100, railCost);

            var wagonOnly = TwoTownService(); // Ash Creek not rail-served here
            int wagonCost = wagonOnly.FreightCostCents("ash-creek", "granite-jct", 100, journeys, diag, out bool wagonRail, out int wagonMinutes);
            Assert.IsFalse(wagonRail);
            Assert.Greater(wagonCost, railCost, "Rail undercuts wagon on the same corridor.");
        }

        [Test]
        public void FreightCost_Unroutable_ReturnsNegative()
        {
            var service = TwoTownService();
            var journeys = TwoTownModel();
            int cost = service.FreightCostCents("ash-creek", "nowhere", 100, journeys, new List<string>(), out _, out _);
            Assert.AreEqual(-1, cost, "Unroutable legs are honestly reported, not zero-priced.");
        }
    }
}
