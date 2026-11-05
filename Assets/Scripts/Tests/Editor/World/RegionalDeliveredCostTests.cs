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
            smallDriver.DeliveredCostPerUnitCents = (supplier, category, itemId, units) =>
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
        public void CategoryPath_DeliveredCostAtOrAbovePrice_WithholdsOrder_GoodsOnlyWouldPlace()
        {
            // The category path's delivered-cost no-loss check: goods 20c/18c
            // both pass the 30c selling price, but delivered 33c (near,
            // cheapest) / 41c (far) do not, so the solo-freight check
            // withholds the order instead of placing it at a loss.
            var near = new FixtureSupplier { SupplierId = "near", SupplierName = "Near Supplier" };
            near.Stock["staple_food"] = 500;
            near.Quotes["staple_food"] = 20;
            var far = new FixtureSupplier { SupplierId = "far", SupplierName = "Far Supplier" };
            far.Stock["staple_food"] = 500;
            far.Quotes["staple_food"] = 18;
            var card = RegionalFreightRateCard.OpeningCompany;

            var port = new StubStorePort();
            port.OfferList.Add(new GeneralStoreCategoryOffer { CategoryId = "staple_food", OfferedItemIds = new List<string> { "flour" }, StoreUnitsPerItemUnit = 1 });
            port.Stock["staple_food"] = 10;
            port.Targets["staple_food"] = 20;
            port.Prices["staple_food"] = 30;
            port.CashBalance = 100000;
            var driver = new StoreRestockDriver();
            driver.RegisterSupplier(near);
            driver.RegisterSupplier(far);
            driver.DeliveredCostPerUnitCents = (supplier, category, itemId, units) =>
            {
                int goods = supplier.QuoteUnitCostCents(category);
                float miles = supplier.SupplierId == "near" ? 50f : 100f;
                int freight = RegionalDeliveredCost.FreightCentsForSoloShipment(miles, card);
                return (goods * units + freight + units - 1) / units;
            };
            Assert.IsTrue(driver.DriveDaily(port, 0, 0.5f));
            Assert.AreEqual(0, driver.Restock.Orders.Count,
                "Goods 20c/18c pass the 30c price, but delivered 33c (near, cheapest) does not, " +
                "so the solo-freight no-loss check withholds the order.");
            Assert.IsTrue(string.Join("\n", driver.Restock.Diagnostics).Contains(
                "cheapest delivered cost 33c meets or exceeds the selling price 30c"),
                "Withholding diagnostic: " + string.Join(" | ", driver.Restock.Diagnostics));

            // Goods-only control: the identical setup with no delivered-cost
            // delegate places exactly one order from the far supplier (18c
            // beats 20c on goods alone) — the contrast proves the delivered
            // check, not the goods check, is what withholds above.
            var controlNear = new FixtureSupplier { SupplierId = "near", SupplierName = "Near Supplier" };
            controlNear.Stock["staple_food"] = 500;
            controlNear.Quotes["staple_food"] = 20;
            var controlFar = new FixtureSupplier { SupplierId = "far", SupplierName = "Far Supplier" };
            controlFar.Stock["staple_food"] = 500;
            controlFar.Quotes["staple_food"] = 18;
            var controlPort = new StubStorePort();
            controlPort.OfferList.Add(new GeneralStoreCategoryOffer { CategoryId = "staple_food", OfferedItemIds = new List<string> { "flour" }, StoreUnitsPerItemUnit = 1 });
            controlPort.Stock["staple_food"] = 10;
            controlPort.Targets["staple_food"] = 20;
            controlPort.Prices["staple_food"] = 30;
            controlPort.CashBalance = 100000;
            var controlDriver = new StoreRestockDriver();
            controlDriver.RegisterSupplier(controlNear);
            controlDriver.RegisterSupplier(controlFar);
            Assert.IsTrue(controlDriver.DriveDaily(controlPort, 0, 0.5f));
            Assert.AreEqual(1, controlDriver.Restock.Orders.Count);
            Assert.AreEqual("far", controlDriver.Restock.Orders[0].UpstreamSupplierId);
            Assert.AreEqual(18, controlDriver.Restock.Orders[0].UnitCostCents);
            Assert.AreEqual(0, controlDriver.Restock.Orders[0].DeliveredCostCentsPerUnit,
                "No delegate supplied, so no delivered cost is recorded.");
        }

        [Test]
        public void CategoryPath_MillbrookSmallOrderWithheld_LargeOrderPlaced()
        {
            // Same branch with real regional adapters and authored production
            // numbers: Millbrook flour is 20c goods, delivered 33c at 10u
            // and 22c at 90u, against a 24c staple_food selling price — so
            // the solo-freight check withholds the small order and places
            // the large one. Millbrook starts with 96u, so availability is
            // not the reason for the withholding.
            var card = RegionalFreightRateCard.OpeningCompany;

            // Setup A (withheld): 10u missing.
            RegionalSupplierAdapter millbrookA = RegionalSupplierSet.CreateAll()
                .Find(a => a.SupplierId == "millbrook_grain_co");
            var smallPort = new StubStorePort();
            smallPort.OfferList.Add(new GeneralStoreCategoryOffer { CategoryId = "staple_food", OfferedItemIds = new List<string> { "flour" }, StoreUnitsPerItemUnit = 1 });
            smallPort.Stock["staple_food"] = 10;
            smallPort.Targets["staple_food"] = 20;
            smallPort.Prices["staple_food"] = 24;
            smallPort.CashBalance = 100000;
            var smallDriver = new StoreRestockDriver();
            smallDriver.RegisterSupplier(millbrookA);
            smallDriver.DeliveredCostPerUnitCents = (supplier, category, itemId, units) =>
                RegionalDeliveredCost.DeliveredCentsPerUnit(supplier, category, units, card);
            Assert.IsTrue(smallDriver.DriveDaily(smallPort, 0, 0.5f));
            Assert.AreEqual(0, smallDriver.Restock.Orders.Count,
                "Millbrook delivered 33c at 10u meets or exceeds the 24c price, so the order is withheld.");
            Assert.IsTrue(string.Join("\n", smallDriver.Restock.Diagnostics).Contains(
                "cheapest delivered cost 33c meets or exceeds the selling price 24c"),
                "Withholding diagnostic: " + string.Join(" | ", smallDriver.Restock.Diagnostics));

            // Setup B (placed): 90u missing, fresh adapter so the scenarios
            // stay hermetic.
            RegionalSupplierAdapter millbrookB = RegionalSupplierSet.CreateAll()
                .Find(a => a.SupplierId == "millbrook_grain_co");
            var largePort = new StubStorePort();
            largePort.OfferList.Add(new GeneralStoreCategoryOffer { CategoryId = "staple_food", OfferedItemIds = new List<string> { "flour" }, StoreUnitsPerItemUnit = 1 });
            largePort.Stock["staple_food"] = 10;
            largePort.Targets["staple_food"] = 100;
            largePort.Prices["staple_food"] = 24;
            largePort.CashBalance = 100000;
            var largeDriver = new StoreRestockDriver();
            largeDriver.RegisterSupplier(millbrookB);
            largeDriver.DeliveredCostPerUnitCents = smallDriver.DeliveredCostPerUnitCents;
            Assert.IsTrue(largeDriver.DriveDaily(largePort, 0, 0.5f));
            Assert.AreEqual(1, largeDriver.Restock.Orders.Count);
            Assert.AreEqual("millbrook_grain_co", largeDriver.Restock.Orders[0].UpstreamSupplierId);
            Assert.AreEqual(string.Empty, largeDriver.Restock.Orders[0].ItemId,
                "A category order: StubStorePort is not item-level, so the item path never runs.");
            Assert.AreEqual(20, largeDriver.Restock.Orders[0].UnitCostCents);
            Assert.AreEqual(90, largeDriver.Restock.Orders[0].UnitsOrdered);
            Assert.AreEqual(22, largeDriver.Restock.Orders[0].DeliveredCostCentsPerUnit);
        }

        [Test]
        public void CategoryPath_MercantileQuoteRisesAsStockDrains_FlipsSupplierToRedfield()
        {
            // The category quote is the cheapest IN-STOCK item, so as
            // Ashford Mercantile's sugar (5c) and salt (7c) drain, its
            // delivered staple cost at 90u climbs 7c -> 9c (salt) -> 15c
            // (potatoes) — and at 15c Redfield Grain's potatoes (14c)
            // win a category order the mercantile used to win. Only the
            // two contenders are registered: with all twelve suppliers,
            // Redfield General Store's sugar (delivered 9c at 90u:
            // (6x90+225)/90 = 8.5, ceiling 9c) would win the drained
            // scenario instead — a true fact, but a different flip.
            var card = RegionalFreightRateCard.OpeningCompany;

            // Control half (fresh stock): the mercantile wins at 7c.
            List<RegionalSupplierAdapter> adapters = RegionalSupplierSet.CreateAll();
            RegionalSupplierAdapter mercantile = adapters.Find(a => a.SupplierId == "ashford_general_mercantile");
            RegionalSupplierAdapter redfieldGrain = adapters.Find(a => a.SupplierId == "redfield_grain_feed");
            Assert.AreEqual(7, RegionalDeliveredCost.DeliveredCentsPerUnit(mercantile, "staple_food", 90, card));
            var port = new StubStorePort();
            port.OfferList.Add(new GeneralStoreCategoryOffer { CategoryId = "staple_food", OfferedItemIds = new List<string> { "flour" }, StoreUnitsPerItemUnit = 1 });
            port.Stock["staple_food"] = 10;
            port.Targets["staple_food"] = 100;
            port.Prices["staple_food"] = 24;
            port.CashBalance = 100000;
            var driver = new StoreRestockDriver();
            driver.RegisterSupplier(mercantile);
            driver.RegisterSupplier(redfieldGrain);
            driver.DeliveredCostPerUnitCents = (supplier, category, itemId, units) =>
                RegionalDeliveredCost.DeliveredCentsPerUnit(supplier, category, units, card);
            Assert.IsTrue(driver.DriveDaily(port, 0, 0.5f));
            Assert.AreEqual(1, driver.Restock.Orders.Count);
            StoreProcurementOrder order = driver.Restock.Orders[0];
            Assert.AreEqual("ashford_general_mercantile", order.UpstreamSupplierId);
            Assert.AreEqual(string.Empty, order.ItemId,
                "A category order: StubStorePort is not item-level, so the item path never runs.");
            Assert.AreEqual(5, order.UnitCostCents, "Sugar is the cheapest in-stock staple.");
            Assert.AreEqual(7, order.DeliveredCostCentsPerUnit);
            Assert.AreEqual(90, order.UnitsOrdered);

            // Drained half (fresh adapters, fresh port/driver, same
            // price): sugar and salt are gone, so the mercantile's
            // cheapest in-stock staple is potatoes at 13c goods.
            List<RegionalSupplierAdapter> drainedAdapters = RegionalSupplierSet.CreateAll();
            RegionalSupplierAdapter drainedMercantile = drainedAdapters.Find(a => a.SupplierId == "ashford_general_mercantile");
            RegionalSupplierAdapter drainedRedfieldGrain = drainedAdapters.Find(a => a.SupplierId == "redfield_grain_feed");
            var diag = new List<string>();
            drainedMercantile.ShipItem("sugar", int.MaxValue, 0, diag);
            drainedMercantile.ShipItem("salt", int.MaxValue, 0, diag);
            Assert.AreEqual(15, RegionalDeliveredCost.DeliveredCentsPerUnit(drainedMercantile, "staple_food", 90, card),
                "Potatoes are now the cheapest in-stock staple: (13x90+125+89)/90 = 15c.");
            Assert.AreEqual(14, RegionalDeliveredCost.DeliveredCentsPerUnit(drainedRedfieldGrain, "staple_food", 90, card),
                "Redfield Grain potatoes: (11x90+225+89)/90 = 14c.");
            var drainedPort = new StubStorePort();
            drainedPort.OfferList.Add(new GeneralStoreCategoryOffer { CategoryId = "staple_food", OfferedItemIds = new List<string> { "flour" }, StoreUnitsPerItemUnit = 1 });
            drainedPort.Stock["staple_food"] = 10;
            drainedPort.Targets["staple_food"] = 100;
            drainedPort.Prices["staple_food"] = 24;
            drainedPort.CashBalance = 100000;
            var drainedDriver = new StoreRestockDriver();
            drainedDriver.RegisterSupplier(drainedMercantile);
            drainedDriver.RegisterSupplier(drainedRedfieldGrain);
            drainedDriver.DeliveredCostPerUnitCents = driver.DeliveredCostPerUnitCents;
            Assert.IsTrue(drainedDriver.DriveDaily(drainedPort, 0, 0.5f));
            Assert.AreEqual(1, drainedDriver.Restock.Orders.Count);
            StoreProcurementOrder drainedOrder = drainedDriver.Restock.Orders[0];
            Assert.AreEqual("redfield_grain_feed", drainedOrder.UpstreamSupplierId,
                "The quote rise flips the supplier: 15c loses to 14c.");
            Assert.AreEqual(string.Empty, drainedOrder.ItemId);
            Assert.AreEqual(11, drainedOrder.UnitCostCents, "Potatoes are Redfield Grain's cheapest in-stock staple.");
            Assert.AreEqual(14, drainedOrder.DeliveredCostCentsPerUnit);
            Assert.AreEqual(90, drainedOrder.UnitsOrdered);
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

        [Test]
        public void MultiStop_TwoOrdersSameSupplier_GroupIntoOneStop_SummedUnits()
        {
            JourneyModel model = ModelWithTowns();
            var orders = new List<RegionalCollectionOrder>
            {
                new RegionalCollectionOrder { OrderId = "po-1", SupplierId = "ashford_general_mercantile", CategoryId = "staple_food", Units = 40 },
                new RegionalCollectionOrder { OrderId = "po-2", SupplierId = "ashford_general_mercantile", CategoryId = "staple_food", Units = 60 },
                new RegionalCollectionOrder { OrderId = "po-3", SupplierId = "ashford_blacksmith", CategoryId = "tools_hardware", Units = 20 },
            };
            var plans = RegionalMultiStopPlanner.Plan(model, orders,
                RegionalMultiStopPlanner.DefaultWagonCapacityUnits,
                RegionalFreightRateCard.OpeningCompany, out List<RegionalCollectionOrder> unplanned);
            Assert.AreEqual(0, unplanned.Count);
            Assert.AreEqual(1, plans.Count);
            RegionalMultiStopPlan plan = plans[0];
            Assert.AreEqual(2, plan.Stops.Count, "Two orders for one supplier make ONE stop.");
            // Mercantile first (nearest): summed units, both order ids in input order.
            Assert.AreEqual("ashford_general_mercantile", plan.Stops[0].SupplierId);
            Assert.AreEqual(100, plan.Stops[0].Units, "40u + 60u summed into one stop.");
            Assert.AreEqual(2, plan.Stops[0].OrderIds.Count);
            Assert.AreEqual("po-1", plan.Stops[0].OrderIds[0]);
            Assert.AreEqual("po-2", plan.Stops[0].OrderIds[1]);
            Assert.AreEqual("Ashford General Mercantile", plan.Stops[0].SupplierName);
            Assert.AreEqual("ashford-ashford_general_mercantile", plan.Stops[0].SiteLocationId);
            Assert.AreEqual("ashford_blacksmith", plan.Stops[1].SupplierId);
            Assert.AreEqual(20, plan.Stops[1].Units);
            Assert.AreEqual("po-3", plan.Stops[1].OrderIds[0]);
            // Same legs as the mercantile + blacksmith journey: 100.26 miles.
            Assert.AreEqual(100.26f, plan.TotalMiles, 0.02f);
            Assert.AreEqual(126, plan.FreightCents);
            Assert.AreEqual(1504, plan.TotalMinutes);
        }

        [Test]
        public void MultiStop_EqualDistanceStops_TieBreakPicksSmallerSupplierId()
        {
            JourneyModel model = ModelWithTowns();
            // Both sites sit sqrt(0.0029) = 0.0539 mi from the centre, so the
            // distances tie exactly; the dry-goods order is registered FIRST
            // so the stop order pins the ordinal SupplierId tie-break, not
            // registration order.
            var orders = new List<RegionalCollectionOrder>
            {
                new RegionalCollectionOrder { OrderId = "po-d", SupplierId = "ashford_dry_goods", CategoryId = "dry_goods", Units = 10 },
                new RegionalCollectionOrder { OrderId = "po-a", SupplierId = "ashford_apothecary", CategoryId = "medicine", Units = 10 },
            };
            var plans = RegionalMultiStopPlanner.Plan(model, orders,
                RegionalMultiStopPlanner.DefaultWagonCapacityUnits,
                RegionalFreightRateCard.OpeningCompany, out List<RegionalCollectionOrder> unplanned);
            Assert.AreEqual(0, unplanned.Count);
            Assert.AreEqual(1, plans.Count);
            Assert.AreEqual(2, plans[0].Stops.Count);
            Assert.AreEqual("ashford_apothecary", plans[0].Stops[0].SupplierId,
                "Apothecary first despite the dry-goods order being registered first: ordinal tie-break on equal distance.");
            Assert.AreEqual("ashford_dry_goods", plans[0].Stops[1].SupplierId);
            Assert.AreEqual(100.22f, plans[0].TotalMiles, 0.02f);
            Assert.AreEqual(126, plans[0].FreightCents);
            Assert.AreEqual(1503, plans[0].TotalMinutes);
        }

        [Test]
        public void MultiStop_StopFillingWagonExactly_Fits()
        {
            JourneyModel model = ModelWithTowns();
            var orders = new List<RegionalCollectionOrder>
            {
                new RegionalCollectionOrder { OrderId = "po-1", SupplierId = "ashford_general_mercantile", CategoryId = "staple_food", Units = 60 },
                new RegionalCollectionOrder { OrderId = "po-2", SupplierId = "ashford_blacksmith", CategoryId = "tools_hardware", Units = 40 },
            };
            var plans = RegionalMultiStopPlanner.Plan(model, orders, 100,
                RegionalFreightRateCard.OpeningCompany, out List<RegionalCollectionOrder> unplanned);
            // 60 + 40 == 100 == capacity: the fit check is strict >
            // (RegionalDeliveredCost.cs:265), so a stop that fills the wagon
            // exactly fits; a >= would leave the blacksmith unplanned.
            Assert.AreEqual(0, unplanned.Count);
            Assert.AreEqual(1, plans.Count);
            Assert.AreEqual(2, plans[0].Stops.Count);
            Assert.AreEqual(100.26f, plans[0].TotalMiles, 0.02f);
            Assert.AreEqual(126, plans[0].FreightCents);
            Assert.AreEqual(1504, plans[0].TotalMinutes);
        }

        [Test]
        public void MultiStop_NothingFits_NoPlanReturned()
        {
            JourneyModel model = ModelWithTowns();
            var orders = new List<RegionalCollectionOrder>
            {
                new RegionalCollectionOrder { OrderId = "po-1", SupplierId = "ashford_general_mercantile", CategoryId = "staple_food", Units = 10 },
            };
            var plans = RegionalMultiStopPlanner.Plan(model, orders, 1,
                RegionalFreightRateCard.OpeningCompany, out List<RegionalCollectionOrder> unplanned);
            // The town yields NO plan, not a plan with zero stops.
            Assert.AreEqual(0, plans.Count);
            Assert.AreEqual(1, unplanned.Count);
            Assert.AreEqual("po-1", unplanned[0].OrderId, "The order is named, not silently dropped.");
        }

        [Test]
        public void JourneyFreight_OneWayCard_HalvesTotalMiles()
        {
            Assert.AreEqual(126,
                RegionalMultiStopPlanner.FreightCentsForJourney(100.89f, RegionalFreightRateCard.OpeningCompany),
                "Round-trip control: 25c base + 101c for 100.89 miles.");
            var oneWay = new RegionalFreightRateCard { BaseFeeCents = 25, PerMileCents = 1, ChargeRoundTrip = false };
            Assert.AreEqual(76,
                RegionalMultiStopPlanner.FreightCentsForJourney(100.89f, oneWay),
                "Journey miles already include the return leg, so a one-way card is charged on half: 25 + ceil(50.445).");
            Assert.AreEqual(50,
                RegionalMultiStopPlanner.FreightCentsForJourney(50f, oneWay),
                "Exact-half case: 25 + 25.");
        }
    }
}
