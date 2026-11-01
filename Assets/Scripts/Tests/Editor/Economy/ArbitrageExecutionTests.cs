using System.Collections.Generic;
using LandLedgers.Population;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.Economy.Market.RegionalTrade.Tests
{
    /// <summary>
    /// NX-3D: arbitrage execution. T3A derives opportunities; this acts on
    /// them — buy low, haul over real routes, sell high. No teleporting goods.
    /// </summary>
    public sealed class ArbitrageExecutionTests
    {
        private List<string> diag;
        private JourneyModel journeys;
        private RegionalTradeService trade;
        private PopulationState population;

        private sealed class FakeBuyMerchant : IGoodsSupplier
        {
            public int Stock;
            public int Sold;
            public string SupplierBusinessId => "buy-merchant";
            public string SupplierName => "Buy Merchant";
            public string LocationId => "town-a";
            public bool HasCategory(string categoryId) => categoryId == "flour";
            public int StockUnits(string categoryId) => Stock;
            public int PricePerUnitCents(string categoryId) => 100;
            public int Sell(string categoryId, int requestedUnits, int dayIndex, List<string> diagnostics)
            {
                int sold = System.Math.Min(requestedUnits, Stock);
                Stock -= sold;
                Sold += sold;
                return sold;
            }
        }

        private sealed class FakeSellMerchant : IArbitrageSellSide
        {
            public int Appetite = 10000;
            public int Taken;
            public string MerchantName => "Sell Merchant";
            public string LocationId => "town-b";
            public bool BuysCategory(string categoryId) => categoryId == "flour";
            public int BuyPricePerUnitCents(string categoryId) => 160;
            public int AcceptSale(string categoryId, int requestedUnits, int dayIndex, List<string> diagnostics)
            {
                int taken = System.Math.Min(requestedUnits, Appetite);
                Appetite -= taken;
                Taken += taken;
                return taken;
            }
        }

        private sealed class FakeFunds : IArbitrageFunds
        {
            public int Balance = 100000;
            public int Debited;
            public int Credited;
            public string OwnerName => "Trader";
            public string DebitBuyCost(int costCents, string reason, int dayIndex, List<string> diag)
            {
                if (costCents > Balance) return "insufficient funds";
                Balance -= costCents;
                Debited += costCents;
                return null;
            }
            public void CreditSaleProceeds(int amountCents, string reason, int dayIndex, List<string> diag)
            {
                Balance += amountCents;
                Credited += amountCents;
            }
        }

        [SetUp]
        public void SetUp()
        {
            diag = new List<string>();
            journeys = new JourneyModel();
            journeys.RegisterLocation(new JourneyLocation("town-a", JourneyLocationKind.TownBuilding, "Town A", 0f, 0f));
            journeys.RegisterLocation(new JourneyLocation("town-b", JourneyLocationKind.TownBuilding, "Town B", 10f, 0f));
            journeys.AddEdge("town-a", "town-b", 10.0f, "wagon road");

            trade = new RegionalTradeService();
            trade.RegisterSettlement(new Settlement
            {
                SettlementId = "a", Name = "Town A", LocationId = "town-a",
                Role = SettlementRole.AgriculturalServiceTown, ResidentCount = 500,
            }, diag);
            trade.RegisterSettlement(new Settlement
            {
                SettlementId = "b", Name = "Town B", LocationId = "town-b",
                Role = SettlementRole.Railhead, ResidentCount = 800, HasRailAccess = false,
            }, diag);

            population = new PopulationState();
            population.people.Add(new PersonState
            {
                id = 1, firstName = "Trader", lastName = "T", age = 30,
                ageBand = AgeBand.Adult18Plus, laborAccessLevel = LaborAccessLevel.FullLaborMarket,
                householdId = 1,
            });
        }

        private ArbitrageOpportunity Opportunity(int units = 100)
        {
            return new ArbitrageOpportunity
            {
                GoodKind = "flour",
                BuySettlementId = "a",
                SellSettlementId = "b",
                BuyMerchant = "Buy Merchant",
                SellMerchant = "Sell Merchant",
                Units = units,
            };
        }

        [Test]
        public void ProfitableTradeExecutesAllLegs()
        {
            var executor = new ArbitrageExecutor();
            var buy = new FakeBuyMerchant { Stock = 100 };
            var sell = new FakeSellMerchant();
            var funds = new FakeFunds();

            ArbitrageTrade tradeRecord = executor.Execute(
                Opportunity(), "Trader", 1, buy, 100, sell, 160, funds,
                ArbitrageHaulKind.HiredCarrier, "Freight Co.", 0,
                journeys, trade, population, 10, diag);

            Assert.IsNotNull(tradeRecord, "A profitable, routable, stocked trade executes.");
            Assert.IsTrue(tradeRecord.Completed);
            Assert.AreEqual(100, buy.Sold, "The merchant's finite stock moved.");
            Assert.AreEqual(100, sell.Taken);
            // Buy 100×100 = 10000; sell 100×160 = 16000; freight 10mi×2c×100u = 2000.
            Assert.AreEqual(10000, tradeRecord.BuyCostCents);
            Assert.AreEqual(2000, tradeRecord.FreightCents);
            Assert.AreEqual(16000, tradeRecord.SaleRevenueCents);
            Assert.AreEqual(4000, tradeRecord.NetCents);
            Assert.AreEqual(12000, funds.Debited, "Buy cost + hired freight debited.");
            Assert.AreEqual(16000, funds.Credited);
        }

        [Test]
        public void NoStockNoTrade()
        {
            var executor = new ArbitrageExecutor();
            var funds = new FakeFunds();
            ArbitrageTrade tradeRecord = executor.Execute(
                Opportunity(), "Trader", 1, new FakeBuyMerchant { Stock = 0 }, 100,
                new FakeSellMerchant(), 160, funds,
                ArbitrageHaulKind.HiredCarrier, "Freight Co.", 0,
                journeys, trade, population, 10, diag);
            Assert.IsNull(tradeRecord, "No stock — no trade faked.");
            Assert.AreEqual(0, funds.Debited, "The refunded debit leaves funds untouched.");
        }

        [Test]
        public void UnroutableHaulNoTrade()
        {
            var lonely = new JourneyModel();
            lonely.RegisterLocation(new JourneyLocation("town-a", JourneyLocationKind.TownBuilding, "Town A", 0f, 0f));
            lonely.RegisterLocation(new JourneyLocation("town-b", JourneyLocationKind.TownBuilding, "Town B", 10f, 0f));
            // No edge — the cargo cannot travel.
            var executor = new ArbitrageExecutor();
            ArbitrageTrade tradeRecord = executor.Execute(
                Opportunity(), "Trader", 1, new FakeBuyMerchant { Stock = 100 }, 100,
                new FakeSellMerchant(), 160, new FakeFunds(),
                ArbitrageHaulKind.HiredCarrier, "Freight Co.", 0,
                lonely, trade, population, 10, diag);
            Assert.IsNull(tradeRecord, "Unroutable — the cargo cannot travel, so no trade.");
        }

        [Test]
        public void NegativeNetRefused()
        {
            var executor = new ArbitrageExecutor();
            var funds = new FakeFunds();
            // Sell price 105 vs buy 100 + freight 20/unit → net negative.
            ArbitrageTrade tradeRecord = executor.Execute(
                Opportunity(), "Trader", 1, new FakeBuyMerchant { Stock = 100 }, 100,
                new FakeSellMerchant(), 105, funds,
                ArbitrageHaulKind.HiredCarrier, "Freight Co.", 0,
                journeys, trade, population, 10, diag);
            Assert.IsNull(tradeRecord, "Stale opportunities do not execute at a loss.");
            Assert.AreEqual(0, funds.Debited);
        }

        [Test]
        public void OwnHaulIsInternalCost()
        {
            var executor = new ArbitrageExecutor();
            var funds = new FakeFunds();
            ArbitrageTrade tradeRecord = executor.Execute(
                Opportunity(), "Freight Co.", 1, new FakeBuyMerchant { Stock = 100 }, 100,
                new FakeSellMerchant(), 160, funds,
                ArbitrageHaulKind.OwnTeam, "", 0,
                journeys, trade, population, 10, diag);
            Assert.IsNotNull(tradeRecord);
            Assert.AreEqual(ArbitrageHaulKind.OwnTeam, tradeRecord.HaulKind);
            Assert.AreEqual(10000, funds.Debited, "Own-team haul: only the buy cost leaves the purse — never revenue to itself (BIZ-3 rule).");
            Assert.AreEqual(2000, tradeRecord.FreightCents, "The internal cost is still honestly recorded.");
        }

        [Test]
        public void SpoilageComesOffTheTop()
        {
            var executor = new ArbitrageExecutor();
            var funds = new FakeFunds();
            // 20 of 100 units spoil in transit: revenue on 80 × 160 = 12800.
            ArbitrageTrade tradeRecord = executor.Execute(
                Opportunity(), "Trader", 1, new FakeBuyMerchant { Stock = 100 }, 100,
                new FakeSellMerchant(), 160, funds,
                ArbitrageHaulKind.HiredCarrier, "Freight Co.", 20,
                journeys, trade, population, 10, diag);
            Assert.IsNotNull(tradeRecord);
            Assert.AreEqual(20, tradeRecord.SpoiledUnits);
            Assert.AreEqual(12800, tradeRecord.SaleRevenueCents);
            Assert.AreEqual(800, tradeRecord.NetCents, "12800 − 10000 − 2000.");
        }
    }
}
