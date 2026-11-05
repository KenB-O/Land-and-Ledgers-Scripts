// Slice 19 (2026-11-05): item-level sustainability over 30 simulated
// days, with a save/restore in the middle. Proves the Slice 18 item
// path sustains the store over time (the cheap staples are pre-drained,
// so the chain must climb the price ladder to coffee, flour, and tea),
// that a mid-run capture/restore loses nothing, that money conserves,
// and that no order is ever placed at a delivered loss.
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Population;
using LandLedgers.World.Regional;
using NUnit.Framework;

namespace LandLedgers.Tests.World
{
    public sealed class RegionalItemSustainabilityTests
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
                GeneralStoreCategoryOffer offer = null;
                foreach (GeneralStoreCategoryOffer o in OfferList)
                    if (string.Equals(o.CategoryId, categoryId, System.StringComparison.OrdinalIgnoreCase)) offer = o;
                if (offer == null || offer.OfferedItemIds == null || offer.OfferedItemIds.Count == 0) return 0;
                int sum = 0;
                int count = 0;
                foreach (string itemId in offer.OfferedItemIds)
                    if (Prices.TryGetValue(itemId, out int p)) { sum += p; count++; }
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
            public bool UsesItemLevelInventory(string itemId) => false;
            public int ItemStockUnits(string itemId) => 0;
            public int ItemPricePerUnitCents(string itemId) => Prices.TryGetValue(itemId, out int p) ? p : 0;
            public bool TryConsumeItemStockUnits(string itemId, int units, out int consumedUnits)
            {
                consumedUnits = 0;
                return false;
            }
            public void RecordItemCashSettlement(string categoryId, int unitsSold, int revenueCents) { }

            public void Consume(string categoryId, int units)
            {
                Stock[categoryId] = System.Math.Max(0, CategoryStockUnits(categoryId) - units);
            }
        }

        private static System.Func<IUpstreamGoodsSupplier, string, string, int, int> DeliveredCostFunc =>
            (supplier, category, itemId, units) =>
                string.IsNullOrWhiteSpace(itemId)
                    ? RegionalDeliveredCost.DeliveredCentsPerUnit(supplier, category, units,
                        RegionalFreightRateCard.OpeningCompany)
                    : RegionalDeliveredCost.DeliveredCentsPerUnitForItem(supplier, itemId, units,
                        RegionalFreightRateCard.OpeningCompany);

        private static void DrainTo(RegionalSupplierAdapter adapter, string itemId, int leave)
        {
            var diag = new List<string>();
            int have = adapter.OnHandUnits(itemId);
            if (have > leave)
                adapter.ShipItem(itemId, have - leave, 0, diag);
        }

        private static void AssertNoNegativeStock(List<RegionalSupplierAdapter> adapters)
        {
            foreach (RegionalSupplierAdapter adapter in adapters)
            {
                foreach (RegionalBusinessRegistry.RegionalBusinessDefinition business in RegionalBusinessRegistry.All)
                {
                    if (business.BusinessId != adapter.SupplierId) continue;
                    foreach (RegionalBusinessRegistry.RegionalOffer offer in business.Offers)
                        Assert.GreaterOrEqual(adapter.OnHandUnits(offer.ItemId), 0,
                            $"{adapter.SupplierId} went negative on {offer.ItemId}.");
                }
            }
        }

        [Test]
        public void ThirtyDays_ItemLevel_ClimbsLadder_SurvivesMidRunRestore()
        {
            var port = new ItemStorePort();
            port.OfferList.Add(new GeneralStoreCategoryOffer { CategoryId = "staple_food", OfferedItemIds = new List<string> { "flour", "potatoes", "salt", "sugar", "coffee", "tea" }, StoreUnitsPerItemUnit = 1 });
            port.OfferList.Add(new GeneralStoreCategoryOffer { CategoryId = "medicine_remedies", OfferedItemIds = new List<string> { "patent_remedy", "medicine_bottle" }, StoreUnitsPerItemUnit = 1 });
            port.Stock["staple_food"] = 10;
            port.Targets["staple_food"] = 100;
            port.Stock["medicine_remedies"] = 0;
            port.Targets["medicine_remedies"] = 40;
            port.Prices["flour"] = 35;
            port.Prices["potatoes"] = 25;
            port.Prices["salt"] = 10;
            port.Prices["sugar"] = 8;
            port.Prices["coffee"] = 25;
            port.Prices["tea"] = 60;
            port.Prices["patent_remedy"] = 50;
            port.Prices["medicine_bottle"] = 75;
            port.CashBalance = 1000000;
            int cashAtStart = port.CashBalance;

            var adapters = RegionalSupplierSet.CreateAll();
            // Stress scenario: the cheap staples are nearly out, so the
            // chain must climb the ladder (potatoes, coffee, flour, tea)
            // instead of living on sugar and salt all month.
            foreach (RegionalSupplierAdapter adapter in adapters)
            {
                DrainTo(adapter, "sugar", 10);
                DrainTo(adapter, "salt", 10);
                DrainTo(adapter, "potatoes", 10);
            }

            var service = new StoreRestockService();
            List<RegionalSupplierAdapter> liveAdapters = adapters;

            for (int day = 0; day < 30; day++)
            {
                if (day == 15)
                {
                    // Mid-run save/restore: capture everything, rebuild
                    // fresh instances, and continue on the restored state.
                    var dto = new RegionalSupplySaveDto();
                    service.CaptureInto(dto);
                    foreach (RegionalSupplierAdapter adapter in liveAdapters)
                        adapter.CaptureInto(dto);
                    int ordersBefore = service.Orders.Count;
                    int mercantileSugarBefore = liveAdapters.Find(a => a.SupplierId == "ashford_general_mercantile").OnHandUnits("sugar");
                    int millbrookFlourBefore = liveAdapters.Find(a => a.SupplierId == "millbrook_grain_co").OnHandUnits("flour");

                    var restoredService = new StoreRestockService();
                    var diag = new List<string>();
                    restoredService.RestoreFrom(dto, diag);
                    Assert.AreEqual(0, diag.Count, "A capture of live state must restore cleanly.");
                    Assert.AreEqual(ordersBefore, restoredService.Orders.Count,
                        "The restore must not lose or duplicate orders.");
                    var restoredAdapters = RegionalSupplierSet.CreateAll();
                    foreach (RegionalSupplierAdapter adapter in restoredAdapters)
                        adapter.RestoreFrom(dto, diag);
                    Assert.AreEqual(mercantileSugarBefore,
                        restoredAdapters.Find(a => a.SupplierId == "ashford_general_mercantile").OnHandUnits("sugar"));
                    Assert.AreEqual(millbrookFlourBefore,
                        restoredAdapters.Find(a => a.SupplierId == "millbrook_grain_co").OnHandUnits("flour"));

                    service = restoredService;
                    liveAdapters = restoredAdapters;
                }

                RegionalSupplierSet.DriveWeeklyProduction(liveAdapters, day);
                service.EvaluateRestock(port, ToSuppliers(liveAdapters), day, 0.5f, DeliveredCostFunc);
                service.AdvanceDay(port, ToSuppliers(liveAdapters), day, new List<string>());
                AssertNoNegativeStock(liveAdapters);
                port.Consume("staple_food", 10);
                port.Consume("medicine_remedies", 6);
            }

            Assert.Greater(port.CategoryStockUnits("staple_food"), 0,
                "After 30 days the store must still hold staple stock.");
            // Money conservation across the restore boundary: cash paid
            // equals the goods cost of every order that reached Received.
            int paid = 0;
            int stapleReceived = 0;
            int medicineReceived = 0;
            bool sawCoffee = false;
            bool sawFlour = false;
            foreach (StoreProcurementOrder order in service.Orders)
            {
                if (order.Stage == StoreProcurementStage.Received)
                {
                    paid += order.UnitsOrdered * order.UnitCostCents;
                    if (string.Equals(order.CategoryId, "staple_food", System.StringComparison.OrdinalIgnoreCase))
                        stapleReceived += order.UnitsOrdered;
                    else if (string.Equals(order.CategoryId, "medicine_remedies", System.StringComparison.OrdinalIgnoreCase))
                        medicineReceived += order.UnitsOrdered;
                }
                if (string.Equals(order.ItemId, "coffee", System.StringComparison.OrdinalIgnoreCase)) sawCoffee = true;
                if (string.Equals(order.ItemId, "flour", System.StringComparison.OrdinalIgnoreCase)) sawFlour = true;
                // No order was ever placed at a delivered loss: an item
                // order is judged against its own price, a category order
                // against the category average.
                int sellingPrice = string.IsNullOrWhiteSpace(order.ItemId)
                    ? port.CategoryPricePerUnitCents(order.CategoryId)
                    : port.ItemPricePerUnitCents(order.ItemId);
                Assert.Greater(sellingPrice, order.DeliveredCostCentsPerUnit,
                    $"Order {order.OrderId} ({order.ItemId}) was placed at delivered cost {order.DeliveredCostCentsPerUnit}c, at or above its {sellingPrice}c selling price.");
            }
            Assert.AreEqual(cashAtStart - paid, port.CashBalance);
            Assert.Greater(stapleReceived, 0, "The store must have received real staple goods over 30 days.");
            Assert.Greater(medicineReceived, 0, "The store must have received real medicine over 30 days.");
            Assert.IsTrue(sawCoffee, "With the cheap staples drained, the chain must have ordered coffee at least once.");
            Assert.IsTrue(sawFlour, "With the cheap staples drained, the chain must have ordered flour at least once.");
        }

        private static List<IUpstreamGoodsSupplier> ToSuppliers(List<RegionalSupplierAdapter> adapters)
        {
            var suppliers = new List<IUpstreamGoodsSupplier>();
            foreach (RegionalSupplierAdapter adapter in adapters)
                suppliers.Add(adapter);
            return suppliers;
        }
    }
}
