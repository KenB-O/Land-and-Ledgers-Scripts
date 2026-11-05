// Slice 18 (2026-11-05): item-level ordering inside a category need.
// Proves the chain now chooses the cheapest deliverable (supplier, item)
// pair against the ITEM's own selling price: the flour crossover flips
// by order size, tea and coffee become orderable, the cheapest item wins
// when several pass, category-only ports fall back to the category path,
// ShipItem debits only the named item, and the save round trip keeps
// the ItemId.
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Population;
using LandLedgers.World.Regional;
using NUnit.Framework;

namespace LandLedgers.Tests.World
{
    public sealed class RegionalItemOrderingTests
    {
        private sealed class ItemStorePort : IItemLevelGeneralStoreTradingPort
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
            public int CategoryPricePerUnitCents(string categoryId)
            {
                // The real port averages the category's offered items.
                GeneralStoreCategoryOffer offer = null;
                foreach (GeneralStoreCategoryOffer o in OfferList)
                    if (string.Equals(o.CategoryId, categoryId, System.StringComparison.OrdinalIgnoreCase)) offer = o;
                if (offer == null || offer.OfferedItemIds == null || offer.OfferedItemIds.Count == 0) return 0;
                int sum = 0;
                int count = 0;
                foreach (string itemId in offer.OfferedItemIds)
                {
                    if (Prices.TryGetValue(itemId, out int p)) { sum += p; count++; }
                }
                return count == 0 ? 0 : sum / count;
            }
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

            // IItemLevelGeneralStoreTradingPort: the store's own offers are
            // pooled (UsesItemLevelInventory false), so item stock reads the
            // pooled category stock, exactly like the real household port.
            public bool UsesItemLevelInventory(string itemId) => false;
            public int ItemStockUnits(string itemId) => 0;
            public int ItemPricePerUnitCents(string itemId) => Prices.TryGetValue(itemId, out int p) ? p : 0;
            public bool TryConsumeItemStockUnits(string itemId, int units, out int consumedUnits)
            {
                consumedUnits = 0;
                return false;
            }
            public void RecordItemCashSettlement(string categoryId, int unitsSold, int revenueCents) { }
        }

        private static readonly Dictionary<string, int> StaplePrices = new Dictionary<string, int>
        {
            { "flour", 35 }, { "potatoes", 25 }, { "salt", 10 }, { "sugar", 8 },
            { "coffee", 25 }, { "tea", 60 },
        };

        private static ItemStorePort StaplePort(int stock, int target, params string[] offeredItems)
        {
            var port = new ItemStorePort();
            foreach (KeyValuePair<string, int> price in StaplePrices)
                port.Prices[price.Key] = price.Value;
            port.OfferList.Add(new GeneralStoreCategoryOffer
            {
                CategoryId = "staple_food",
                OfferedItemIds = new List<string>(offeredItems),
                StoreUnitsPerItemUnit = 1,
            });
            port.Stock["staple_food"] = stock;
            port.Targets["staple_food"] = target;
            port.CashBalance = 100000;
            return port;
        }

        private static StoreRestockDriver DriverWith(params RegionalSupplierAdapter[] adapters)
        {
            var driver = new StoreRestockDriver();
            foreach (RegionalSupplierAdapter adapter in adapters)
                driver.RegisterSupplier(adapter);
            driver.DeliveredCostPerUnitCents = (supplier, category, itemId, units) =>
                string.IsNullOrWhiteSpace(itemId)
                    ? RegionalDeliveredCost.DeliveredCentsPerUnit(supplier, category, units,
                        RegionalFreightRateCard.OpeningCompany)
                    : RegionalDeliveredCost.DeliveredCentsPerUnitForItem(supplier, itemId, units,
                        RegionalFreightRateCard.OpeningCompany);
            return driver;
        }

        private static RegionalSupplierAdapter Adapter(string supplierId)
        {
            foreach (RegionalSupplierAdapter adapter in RegionalSupplierSet.CreateAll())
                if (string.Equals(adapter.SupplierId, supplierId, System.StringComparison.OrdinalIgnoreCase))
                    return adapter;
            return null;
        }

        private static void Drain(RegionalSupplierAdapter adapter, params string[] itemIds)
        {
            var diag = new List<string>();
            foreach (string itemId in itemIds)
                adapter.ShipItem(itemId, int.MaxValue, 0, diag);
        }

        [Test]
        public void FlourCrossover_SmallOrder_ChoosesNearMillbrook()
        {
            // Missing 10u: Millbrook flour delivered 33c, Redfield 41c.
            RegionalSupplierAdapter millbrook = Adapter("millbrook_grain_co");
            RegionalSupplierAdapter redfield = Adapter("redfield_grain_feed");
            Drain(redfield, "potatoes"); // only flour in play
            ItemStorePort port = StaplePort(10, 20, "flour"); // missing 10u at the 0.5 threshold
            StoreRestockDriver driver = DriverWith(millbrook, redfield);
            Assert.IsTrue(driver.DriveDaily(port, 0, 0.5f));
            Assert.AreEqual(1, driver.Restock.Orders.Count);
            StoreProcurementOrder order = driver.Restock.Orders[0];
            Assert.AreEqual("flour", order.ItemId);
            Assert.AreEqual("millbrook_grain_co", order.UpstreamSupplierId);
            Assert.AreEqual(20, order.UnitCostCents, "The supplier is paid its goods price.");
            Assert.AreEqual(33, order.DeliveredCostCentsPerUnit);
            Assert.AreEqual(10, order.UnitsOrdered);
        }

        [Test]
        public void FlourCrossover_LargeOrder_ChoosesFarRedfield()
        {
            // Missing 90u: Millbrook flour delivered 22c, Redfield 21c.
            RegionalSupplierAdapter millbrook = Adapter("millbrook_grain_co");
            RegionalSupplierAdapter redfield = Adapter("redfield_grain_feed");
            Drain(redfield, "potatoes");
            ItemStorePort port = StaplePort(10, 100, "flour");
            StoreRestockDriver driver = DriverWith(millbrook, redfield);
            Assert.IsTrue(driver.DriveDaily(port, 0, 0.5f));
            Assert.AreEqual(1, driver.Restock.Orders.Count);
            StoreProcurementOrder order = driver.Restock.Orders[0];
            Assert.AreEqual("flour", order.ItemId);
            Assert.AreEqual("redfield_grain_feed", order.UpstreamSupplierId);
            Assert.AreEqual(18, order.UnitCostCents);
            Assert.AreEqual(21, order.DeliveredCostCentsPerUnit);
            Assert.AreEqual(90, order.UnitsOrdered);
        }

        [Test]
        public void CheapestPassingItem_Wins_WhenSeveralPass()
        {
            // Missing 90u with the whole region on offer: sugar from
            // Ashford Mercantile is delivered at 7c against its own 8c
            // price — cheaper than flour (21c), potatoes, salt, coffee.
            List<RegionalSupplierAdapter> adapters = RegionalSupplierSet.CreateAll();
            ItemStorePort port = StaplePort(10, 100, "flour", "potatoes", "salt", "sugar", "coffee", "tea");
            StoreRestockDriver driver = DriverWith(adapters.ToArray());
            Assert.IsTrue(driver.DriveDaily(port, 0, 0.5f));
            Assert.AreEqual(1, driver.Restock.Orders.Count);
            StoreProcurementOrder order = driver.Restock.Orders[0];
            Assert.AreEqual("sugar", order.ItemId);
            Assert.AreEqual("ashford_general_mercantile", order.UpstreamSupplierId);
            Assert.AreEqual(5, order.UnitCostCents);
            Assert.AreEqual(7, order.DeliveredCostCentsPerUnit);
        }

        [Test]
        public void Tea_Orderable_AtItsOwnPrice_WhenOrderIsBigEnough()
        {
            // Tea at 42c goods: a 16u order is delivered at 57c, below
            // tea's own 60c price (the category average of 24c would
            // have refused it). Only tea is left on this supplier.
            RegionalSupplierAdapter redfield = Adapter("redfield_general_store");
            Drain(redfield, "salt", "sugar", "soap", "candles", "lamp_oil", "lantern");
            ItemStorePort port = StaplePort(16, 32, "tea"); // missing 16u at the 0.5 threshold
            StoreRestockDriver driver = DriverWith(redfield);
            Assert.IsTrue(driver.DriveDaily(port, 0, 0.5f));
            Assert.AreEqual(1, driver.Restock.Orders.Count);
            StoreProcurementOrder order = driver.Restock.Orders[0];
            Assert.AreEqual("tea", order.ItemId);
            Assert.AreEqual("redfield_general_store", order.UpstreamSupplierId);
            Assert.AreEqual(42, order.UnitCostCents);
            Assert.AreEqual(57, order.DeliveredCostCentsPerUnit);
            Assert.AreEqual(16, order.UnitsOrdered);
        }

        [Test]
        public void Tea_Withheld_WhenTooSmallToProfit()
        {
            // A 12u tea order is delivered at 61c, above tea's own 60c
            // price: no order is placed, honestly, and nothing else is
            // on offer to fall back to.
            RegionalSupplierAdapter redfield = Adapter("redfield_general_store");
            Drain(redfield, "salt", "sugar", "soap", "candles", "lamp_oil", "lantern");
            ItemStorePort port = StaplePort(12, 24, "tea"); // missing 12u at the 0.5 threshold
            StoreRestockDriver driver = DriverWith(redfield);
            Assert.IsTrue(driver.DriveDaily(port, 0, 0.5f));
            Assert.AreEqual(0, driver.Restock.Orders.Count);
        }

        [Test]
        public void Coffee_Orderable_AtItsOwnPrice()
        {
            // Coffee at 17c goods: a 20u order is delivered at 24c,
            // below coffee's own 25c price (the category average of 24c
            // would have refused it at most sizes).
            RegionalSupplierAdapter mercantile = Adapter("ashford_general_mercantile");
            Drain(mercantile, "potatoes", "salt", "sugar", "soap", "candles", "lamp_oil", "thread");
            ItemStorePort port = StaplePort(20, 40, "coffee");
            StoreRestockDriver driver = DriverWith(mercantile);
            Assert.IsTrue(driver.DriveDaily(port, 0, 0.5f));
            Assert.AreEqual(1, driver.Restock.Orders.Count);
            StoreProcurementOrder order = driver.Restock.Orders[0];
            Assert.AreEqual("coffee", order.ItemId);
            Assert.AreEqual(17, order.UnitCostCents);
            Assert.AreEqual(24, order.DeliveredCostCentsPerUnit);
        }

        [Test]
        public void CategoryOnlyPort_FallsBackToCategoryOrder()
        {
            // A port that is not item-level keeps the exact old behavior:
            // a category order for the cheapest category quote, no ItemId.
            var port = new CategoryOnlyPort();
            port.OfferList.Add(new GeneralStoreCategoryOffer { CategoryId = "staple_food", OfferedItemIds = new List<string> { "flour" }, StoreUnitsPerItemUnit = 1 });
            port.Stock["staple_food"] = 10;
            port.Targets["staple_food"] = 100;
            port.Prices["staple_food"] = 24;
            port.CashBalance = 100000;
            List<RegionalSupplierAdapter> adapters = RegionalSupplierSet.CreateAll();
            StoreRestockDriver driver = DriverWith(adapters.ToArray());
            Assert.IsTrue(driver.DriveDaily(port, 0, 0.5f));
            Assert.AreEqual(1, driver.Restock.Orders.Count);
            StoreProcurementOrder order = driver.Restock.Orders[0];
            Assert.AreEqual(string.Empty, order.ItemId);
            Assert.AreEqual("ashford_general_mercantile", order.UpstreamSupplierId, "Cheapest staple quote is the mercantile's sugar at 5c.");
            Assert.AreEqual(5, order.UnitCostCents);
        }

        [Test]
        public void ItemOrder_Ship_DebitsOnlyTheNamedItem()
        {
            RegionalSupplierAdapter millbrook = Adapter("millbrook_grain_co");
            RegionalSupplierAdapter redfield = Adapter("redfield_grain_feed");
            Drain(redfield, "potatoes");
            ItemStorePort port = StaplePort(10, 20, "flour"); // missing 10u at the 0.5 threshold
            StoreRestockDriver driver = DriverWith(millbrook, redfield);
            Assert.IsTrue(driver.DriveDaily(port, 0, 0.5f));
            Assert.AreEqual(1, driver.Restock.Orders.Count);
            // Millbrook lead is 1 day: the order ships and arrives day 1.
            Assert.IsTrue(driver.DriveDaily(port, 1, 0.5f));
            Assert.AreEqual(96 - 10, millbrook.OnHandUnits("flour"), "Only the ordered flour left Millbrook.");
            Assert.AreEqual(96, redfield.OnHandUnits("flour"), "The losing supplier is untouched.");
            Assert.AreEqual(1, driver.Restock.Deliveries.Count);
            Assert.AreEqual("flour", driver.Restock.Deliveries[0].ItemId);
            Assert.AreEqual(20, port.CategoryStockUnits("staple_food"), "The flour landed in the pooled staple stock.");
        }

        [Test]
        public void ItemOrder_SaveRoundTrip_PreservesItemId()
        {
            RegionalSupplierAdapter millbrook = Adapter("millbrook_grain_co");
            RegionalSupplierAdapter redfield = Adapter("redfield_grain_feed");
            Drain(redfield, "potatoes");
            ItemStorePort port = StaplePort(10, 20, "flour"); // missing 10u at the 0.5 threshold
            StoreRestockDriver driver = DriverWith(millbrook, redfield);
            Assert.IsTrue(driver.DriveDaily(port, 0, 0.5f));

            var dto = new RegionalSupplySaveDto();
            driver.Restock.CaptureInto(dto);
            var restored = new StoreRestockService();
            restored.RestoreFrom(dto, new List<string>());
            Assert.AreEqual(1, restored.Orders.Count);
            Assert.AreEqual("flour", restored.Orders[0].ItemId);
            Assert.AreEqual("millbrook_grain_co", restored.Orders[0].UpstreamSupplierId);
            Assert.AreEqual(20, restored.Orders[0].UnitCostCents);
        }

        // A minimal category-only port (not item-level) for the fallback test.
        private sealed class CategoryOnlyPort : IGeneralStoreTradingPort
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
            public void RecordCashSettlement(string categoryId, int unitsSold, int revenueCents) { }
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
    }
}
