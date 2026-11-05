// Slice 15 (2026-11-05): delivered cost decides, and one journey collects
// from several suppliers in a town. Proves the freight math, the
// goods-vs-delivered crossover, the chain's delivered-cost choice, and the
// multi-stop manifest (stops, miles, capacity, per-stop custody).
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Population;
using LandLedgers.World.Journeys;
using LandLedgers.World.Regional;
using NUnit.Framework;

namespace LandLedgers.Tests.World
{
    public sealed class RegionalDeliveredCostTests
    {
        private sealed class StubStorePort : IGeneralStoreTradingPort
        {
            public string StoreBusinessId { get; set; } = "general-store-1";
            public string StoreName { get; set; } = "General Store";
            public string LocationId { get; set; } = "general-store";
            public Dictionary<string, int> Stock = new Dictionary<string, int>();
            public Dictionary<string, int> Prices = new Dictionary<string, int>();
            public Dictionary<string, int> Targets = new Dictionary<string, int>();
            public List<GeneralStoreCategoryOffer> OfferList = new List<GeneralStoreCategoryOffer>();
            public int CashBalance;

            public IReadOnlyList<GeneralStoreCategoryOffer> CategoryOffers => OfferList;
            public StoreTradingCapability TradingCapability => new StoreTradingCapability { Staffed = true };
            public int CurrentCashCents => CashBalance;
            public bool OffersDelivery => false;
            public bool OffersTradeCredit => false;
            public int TradeCreditLimitCents => 0;
            public int CategoryStockUnits(string categoryId) => Stock.TryGetValue(categoryId, out int s) ? s : 0;
            public int CategoryTargetStockUnits(string categoryId) => Targets.TryGetValue(categoryId, out int t) ? t : 0;
            public int CategoryPricePerUnitCents(string categoryId) => Prices.TryGetValue(categoryId, out int p) ? p : 0;
            public bool TryConsumeStoreUnits(string categoryId, int units, out int consumedUnits)
            {
                consumedUnits = 0;
                return false;
            }
            public void RecordCashSettlement(string categoryId, int unitsSold, int revenueCents)
            {
                CashBalance += System.Math.Max(0, revenueCents);
            }
            public void RecordCreditSettlement(string categoryId, int unitsSold, int amountCents, string obligationId) { }
            public string SpendCash(int amountCents, string purpose)
            {
                if (amountCents < 0) return "negative spend";
                if (CashBalance < amountCents) return "cannot afford";
                CashBalance -= amountCents;
                return null;
            }
            public string ReceiveStock(string categoryId, int units, string provenanceLabel)
            {
                Stock[categoryId] = CategoryStockUnits(categoryId) + System.Math.Max(0, units);
                return null;
            }
            public int CategoryReceivableUnits(string categoryId) => int.MaxValue;
            public int DeliveryFeeCents(string itemId, int units) => 0;
        }

        private sealed class FixtureSupplier : IUpstreamGoodsSupplier
        {
            public string SupplierId { get; set; } = "fixture";
            public string SupplierName { get; set; } = "Fixture";
            public Dictionary<string, int> Stock = new Dictionary<string, int>();
            public Dictionary<string, int> Quotes = new Dictionary<string, int>();
            public int QuoteUnitCostCents(string categoryId) => Quotes.TryGetValue(categoryId, out int q) ? q : 0;
            public int LeadTimeDays(string categoryId) => 1;
            public int AvailableUnits(string categoryId) => Stock.TryGetValue(categoryId, out int s) ? s : 0;
            public int Ship(string categoryId, int units, int dayIndex, List<string> diagnostics)
            {
                int have = AvailableUnits(categoryId);
                int shipped = System.Math.Min(System.Math.Max(0, units), have);
                Stock[categoryId] = have - shipped;
                return shipped;
            }
        }

        private static JourneyModel ModelWithTowns()
        {
            var model = new JourneyModel();
            model.RegisterLocation(new JourneyLocation("town-center", JourneyLocationKind.Waypoint, "Town Centre", 0f, 0f));
            RegionalTownRegistry.ApplyAll(model, new List<string>());
            return model;
        }

        [Test]
        public void SoloFreight_Ashford125c_Redfield225c()
        {
            var card = RegionalFreightRateCard.OpeningCompany;
            Assert.AreEqual(125, RegionalDeliveredCost.FreightCentsForSoloShipment(50f, card));
            Assert.AreEqual(225, RegionalDeliveredCost.FreightCentsForSoloShipment(100f, card));
            var oneWay = new RegionalFreightRateCard { BaseFeeCents = 25, PerMileCents = 1, ChargeRoundTrip = false };
            Assert.AreEqual(75, RegionalDeliveredCost.FreightCentsForSoloShipment(50f, oneWay));
        }

        [Test]
        public void DeliveredCentsPerUnit_PricesTheCategoryCheapestItem()
        {
            // The calculator prices the category's cheapest in-stock item
            // (the same quote the chain sees). Ashford Mercantile's staple
            // quote is sugar at 5c; Millbrook's only staple offer is flour
            // at 20c; Redfield Grain's cheapest staple is potatoes at 11c.
            var adapters = RegionalSupplierSet.CreateAll();
            RegionalSupplierAdapter mercantile = adapters.Find(a => a.SupplierId == "ashford_general_mercantile");
            RegionalSupplierAdapter millbrook = adapters.Find(a => a.SupplierId == "millbrook_grain_co");
            RegionalSupplierAdapter redfieldGrain = adapters.Find(a => a.SupplierId == "redfield_grain_feed");
            var card = RegionalFreightRateCard.OpeningCompany;
            Assert.AreEqual(18, RegionalDeliveredCost.DeliveredCentsPerUnit(mercantile, "staple_food", 10, card));
            Assert.AreEqual(7, RegionalDeliveredCost.DeliveredCentsPerUnit(mercantile, "staple_food", 90, card));
            Assert.AreEqual(33, RegionalDeliveredCost.DeliveredCentsPerUnit(millbrook, "staple_food", 10, card));
            Assert.AreEqual(22, RegionalDeliveredCost.DeliveredCentsPerUnit(millbrook, "staple_food", 90, card));
            Assert.AreEqual(34, RegionalDeliveredCost.DeliveredCentsPerUnit(redfieldGrain, "staple_food", 10, card));
            Assert.AreEqual(14, RegionalDeliveredCost.DeliveredCentsPerUnit(redfieldGrain, "staple_food", 90, card));

            // The ITEM-level flour crossover (Millbrook 20c vs Redfield 18c
            // goods; delivered 33c vs 41c at 10u, 22c vs 21c at 90u) is
            // real arithmetic — (20*10+125)/10 = 33, (18*10+225)/10 = 41,
            // (20*90+125)/90 = 22, (18*90+225)/90 = 21 — but the chain
            // orders by category, so it cannot see it yet: Redfield
            // Grain's category quote is its potatoes, not its flour.
            // Item-level ordering is a later slice; this test pins the
            // numbers so that slice can verify against them.
            Assert.AreEqual(33, (20 * 10 + 125 + 9) / 10);
            Assert.AreEqual(41, (18 * 10 + 225 + 9) / 10);
            Assert.AreEqual(22, (20 * 90 + 125 + 89) / 90);
            Assert.AreEqual(21, (18 * 90 + 225 + 89) / 90);
        }

        [Test]
        public void Chain_OnDeliveredCost_FlipsSupplierWithOrderSize()
        {
            // Two fixture suppliers, same category, goods 20c (near) vs 18c
            // (far). Goods-only choice always picks the far one; delivered
            // choice flips with order size. The delivered function prices
            // each supplier's solo freight from its known distance.
            var near = new FixtureSupplier { SupplierId = "near", SupplierName = "Near Supplier" };
            near.Stock["staple_food"] = 500;
            near.Quotes["staple_food"] = 20;
            var far = new FixtureSupplier { SupplierId = "far", SupplierName = "Far Supplier" };
            far.Stock["staple_food"] = 500;
            far.Quotes["staple_food"] = 18;
            var card = RegionalFreightRateCard.OpeningCompany;

            // Small order (10u): near wins delivered (33c vs 41c).
            var smallPort = new StubStorePort();
            smallPort.OfferList.Add(new GeneralStoreCategoryOffer { CategoryId = "staple_food", OfferedItemIds = new List<string> { "flour" }, StoreUnitsPerItemUnit = 1 });
            smallPort.Stock["staple_food"] = 10;
            smallPort.Targets["staple_food"] = 20;
            smallPort.Prices["staple_food"] = 50;
            smallPort.CashBalance = 100000;
            var smallDriver = new StoreRestockDriver();
            smallDriver.RegisterSupplier(near);
            smallDriver.RegisterSupplier(far);
            smallDriver.DeliveredCostPerUnitCents = (supplier, category, units) =>
            {
                int goods = supplier.QuoteUnitCostCents(category);
                float miles = supplier.SupplierId == "near" ? 50f : 100f;
                int freight = RegionalDeliveredCost.FreightCentsForSoloShipment(miles, card);
                return (goods * units + freight + units - 1) / units;
            };
            Assert.IsTrue(smallDriver.DriveDaily(smallPort, 0, 0.5f));
            Assert.AreEqual(1, smallDriver.Restock.Orders.Count);
            Assert.AreEqual("near", smallDriver.Restock.Orders[0].UpstreamSupplierId);
            Assert.AreEqual(20, smallDriver.Restock.Orders[0].UnitCostCents, "The supplier is still paid its goods price.");
            Assert.AreEqual(33, smallDriver.Restock.Orders[0].DeliveredCostCentsPerUnit);

            // Large order (90u): far wins delivered (21c vs 22c).
            var largePort = new StubStorePort();
            largePort.OfferList.Add(new GeneralStoreCategoryOffer { CategoryId = "staple_food", OfferedItemIds = new List<string> { "flour" }, StoreUnitsPerItemUnit = 1 });
            largePort.Stock["staple_food"] = 10;
            largePort.Targets["staple_food"] = 100;
            largePort.Prices["staple_food"] = 50;
            largePort.CashBalance = 100000;
            var largeDriver = new StoreRestockDriver();
            largeDriver.RegisterSupplier(near);
            largeDriver.RegisterSupplier(far);
            largeDriver.DeliveredCostPerUnitCents = smallDriver.DeliveredCostPerUnitCents;
            Assert.IsTrue(largeDriver.DriveDaily(largePort, 0, 0.5f));
            Assert.AreEqual(1, largeDriver.Restock.Orders.Count);
            Assert.AreEqual("far", largeDriver.Restock.Orders[0].UpstreamSupplierId);
            Assert.AreEqual(21, largeDriver.Restock.Orders[0].DeliveredCostCentsPerUnit);
        }

        [Test]
        public void MultiStop_OneAshfordJourney_ThreeStops_InNearestFirstOrder()
        {
            JourneyModel model = ModelWithTowns();
            var orders = new List<RegionalCollectionOrder>
            {
                new RegionalCollectionOrder { OrderId = "po-1", SupplierId = "millbrook_grain_co", CategoryId = "staple_food", Units = 40 },
                new RegionalCollectionOrder { OrderId = "po-2", SupplierId = "ashford_general_mercantile", CategoryId = "staple_food", Units = 60 },
                new RegionalCollectionOrder { OrderId = "po-3", SupplierId = "ashford_blacksmith", CategoryId = "tools_hardware", Units = 20 },
            };
            var plans = RegionalMultiStopPlanner.Plan(model, orders,
                RegionalMultiStopPlanner.DefaultWagonCapacityUnits,
                RegionalFreightRateCard.OpeningCompany, out List<RegionalCollectionOrder> unplanned);
            Assert.AreEqual(0, unplanned.Count);
            Assert.AreEqual(1, plans.Count);
            RegionalMultiStopPlan plan = plans[0];
            Assert.AreEqual("ashford", plan.TownId);
            Assert.AreEqual(3, plan.Stops.Count);
            // Nearest site first: mercantile (0.03 mi), blacksmith (0.10),
            // Millbrook (0.32).
            Assert.AreEqual("ashford_general_mercantile", plan.Stops[0].SupplierId);
            Assert.AreEqual("ashford_blacksmith", plan.Stops[1].SupplierId);
            Assert.AreEqual("millbrook_grain_co", plan.Stops[2].SupplierId);
            Assert.AreEqual(60, plan.Stops[0].Units);
            Assert.AreEqual("po-2", plan.Stops[0].OrderIds[0]);
            // Round trip: 49.5 + 0.5 out, local legs, 0.5 + 49.5 back.
            Assert.AreEqual(100.89f, plan.TotalMiles, 0.02f);
            Assert.AreEqual(126, plan.FreightCents, "25c base + 101c for 100.89 miles, round trip.");
            Assert.AreEqual(1513, plan.TotalMinutes, "100.89 miles at 4 mph.");
        }

        [Test]
        public void MultiStop_OverCapacity_LeavesLaterStopsUnplanned()
        {
            JourneyModel model = ModelWithTowns();
            var orders = new List<RegionalCollectionOrder>
            {
                new RegionalCollectionOrder { OrderId = "po-1", SupplierId = "millbrook_grain_co", CategoryId = "staple_food", Units = 40 },
                new RegionalCollectionOrder { OrderId = "po-2", SupplierId = "ashford_general_mercantile", CategoryId = "staple_food", Units = 60 },
                new RegionalCollectionOrder { OrderId = "po-3", SupplierId = "ashford_blacksmith", CategoryId = "tools_hardware", Units = 20 },
            };
            var plans = RegionalMultiStopPlanner.Plan(model, orders, 100,
                RegionalFreightRateCard.OpeningCompany, out List<RegionalCollectionOrder> unplanned);
            Assert.AreEqual(1, plans.Count);
            Assert.AreEqual(2, plans[0].Stops.Count, "Mercantile 60u + blacksmith 20u fit; Millbrook 40u does not.");
            Assert.AreEqual(1, unplanned.Count);
            Assert.AreEqual("po-1", unplanned[0].OrderId, "The unplanned order is named, not silently dropped.");
        }

        [Test]
        public void MultiStop_TwoTowns_TwoPlans_UnknownSupplierUnplanned()
        {
            JourneyModel model = ModelWithTowns();
            var orders = new List<RegionalCollectionOrder>
            {
                new RegionalCollectionOrder { OrderId = "po-a", SupplierId = "ashford_butcher", CategoryId = "meat", Units = 10 },
                new RegionalCollectionOrder { OrderId = "po-r", SupplierId = "redfield_butcher", CategoryId = "meat", Units = 10 },
                new RegionalCollectionOrder { OrderId = "po-x", SupplierId = "no_such_supplier", CategoryId = "meat", Units = 10 },
            };
            var plans = RegionalMultiStopPlanner.Plan(model, orders,
                RegionalMultiStopPlanner.DefaultWagonCapacityUnits,
                RegionalFreightRateCard.OpeningCompany, out List<RegionalCollectionOrder> unplanned);
            Assert.AreEqual(2, plans.Count);
            Assert.AreEqual(1, unplanned.Count);
            Assert.AreEqual("po-x", unplanned[0].OrderId);
        }
    }
}
