// Slice 10 (2026-11-05): retail unit conversion. The conversion table is
// pure and tested directly; the adapter's exact-item settlement is tested
// against a stub port that behaves like the FIXED production port (its
// item surface is in household units, its lots are in store units). The
// production port's factor math and the manager's offer wiring are
// source-reviewed only: the port needs a live GeneralStoreRuntimeManager
// and the manager file is harness-excluded.
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    public sealed class RetailUnitConversionTests
    {
        // ---- The conversion table ----

        [Test]
        public void LampOil_QuartsToGallons_IsFour()
        {
            Assert.IsTrue(RetailUnitConversions.TryGetStoreUnitsPerHouseholdUnit(
                "lamp_oil", "quart", "gal", out int factor, out string _));
            Assert.AreEqual(4, factor);
        }

        [Test]
        public void IdenticalUnits_ResolveToOne()
        {
            Assert.IsTrue(RetailUnitConversions.TryGetStoreUnitsPerHouseholdUnit(
                "bread", "loaf", "loaf", out int factor, out string _));
            Assert.AreEqual(1, factor);
        }

        [Test]
        public void Flour_PackageShape_IsRefusedWithTheMissingMechanismNamed()
        {
            Assert.IsFalse(RetailUnitConversions.TryGetStoreUnitsPerHouseholdUnit(
                "flour", "sack", "lb", out int _, out string reason));
            Assert.IsTrue(reason.Contains("whole-package purchase"),
                $"reason should name the missing mechanism, was: {reason}");
        }

        [Test]
        public void Potatoes_SackWeight_IsRefusedWithoutInventingOne()
        {
            Assert.IsFalse(RetailUnitConversions.TryGetStoreUnitsPerHouseholdUnit(
                "potatoes", "sack", "lb", out int _, out string reason));
            Assert.IsTrue(reason.Contains("D-5 sack weight"),
                $"reason should name the missing D-5 weight, was: {reason}");
        }

        [Test]
        public void Salt_SmallBagWeight_IsRefusedWithoutInventingOne()
        {
            Assert.IsFalse(RetailUnitConversions.TryGetStoreUnitsPerHouseholdUnit(
                "salt", "small bag", "lb", out int _, out string reason));
            Assert.IsTrue(reason.Contains("D-5 bag weight"),
                $"reason should name the missing D-5 weight, was: {reason}");
        }

        [Test]
        public void UnknownItem_IsRefusedWithTheMissingDefinitionNamed()
        {
            Assert.IsFalse(RetailUnitConversions.TryGetStoreUnitsPerHouseholdUnit(
                "nails", "box", "lb", out int _, out string reason));
            Assert.IsTrue(reason.Contains("no package definition"),
                $"reason should name the missing definition, was: {reason}");
        }

        // ---- W5: the package direction (whole-package purchase) ----

        [Test]
        public void Flour_PackagePurchase_IsTenPoundsPerSack()
        {
            Assert.IsTrue(RetailUnitConversions.TryGetHouseholdUnitsPerStoreUnit(
                "flour", "10 lb sack", "lb", out int packageSize, out string _));
            Assert.AreEqual(10, packageSize);
        }

        [Test]
        public void Flour_PackagePurchase_BareSackLabel_DoesNotMatchTheAuthoredUnit()
        {
            // The authored store unit is "10 lb sack"; a bare "sack" is a
            // different unit string, so the package conversion does not
            // silently apply to it.
            Assert.IsFalse(RetailUnitConversions.TryGetHouseholdUnitsPerStoreUnit(
                "flour", "sack", "lb", out int _, out string reason));
            Assert.IsTrue(reason.Contains("no package definition"),
                $"reason should name the missing definition, was: {reason}");
        }

        [Test]
        public void Potatoes_PackagePurchase_RefusesWithD5WeightNamed()
        {
            Assert.IsFalse(RetailUnitConversions.TryGetHouseholdUnitsPerStoreUnit(
                "potatoes", "sack", "lb", out int _, out string reason));
            Assert.IsTrue(reason.Contains("D-5 sack weight"),
                $"reason should name the missing D-5 weight, was: {reason}");
        }

        [Test]
        public void Salt_PackagePurchase_RefusesWithD5WeightNamed()
        {
            Assert.IsFalse(RetailUnitConversions.TryGetHouseholdUnitsPerStoreUnit(
                "salt", "small bag", "lb", out int _, out string reason));
            Assert.IsTrue(reason.Contains("D-5 bag weight"),
                $"reason should name the missing D-5 weight, was: {reason}");
        }

        [Test]
        public void IdenticalUnits_PackagePurchase_IsOne()
        {
            Assert.IsTrue(RetailUnitConversions.TryGetHouseholdUnitsPerStoreUnit(
                "bread", "loaf", "loaf", out int packageSize, out string _));
            Assert.AreEqual(1, packageSize);
        }

        // ---- The adapter over a converted offer ----

        private sealed class StubItemPort : IItemLevelGeneralStoreTradingPort
        {
            public string StoreBusinessId { get; set; } = "general-store-1";
            public string StoreName { get; set; } = "General Store";
            public string LocationId { get; set; } = "general-store";
            public List<GeneralStoreCategoryOffer> OfferList = new List<GeneralStoreCategoryOffer>();
            // Stock and prices are in STORE units, like the real lots.
            public Dictionary<string, int> StockStoreUnits = new Dictionary<string, int>();
            public Dictionary<string, int> PricePerStoreUnitCents = new Dictionary<string, int>();
            public int CashBalance;
            public int LastCashSettlementUnits = -1;
            public int LastCashSettlementRevenue = -1;
            public int LastCreditSettlementUnits = -1;

            public IReadOnlyList<GeneralStoreCategoryOffer> CategoryOffers => OfferList;
            public StoreTradingCapability TradingCapability => new StoreTradingCapability { Staffed = true };
            public int CurrentCashCents => CashBalance;
            public bool OffersDelivery => false;
            public bool OffersTradeCredit => false;
            public int TradeCreditLimitCents => 0;

            private GeneralStoreCategoryOffer FindOffer(string itemId)
            {
                foreach (GeneralStoreCategoryOffer offer in OfferList)
                {
                    if (offer?.OfferedItemIds == null) continue;
                    foreach (string id in offer.OfferedItemIds)
                        if (string.Equals(id, itemId, System.StringComparison.OrdinalIgnoreCase)) return offer;
                }
                return null;
            }

            private int Factor(string itemId)
            {
                GeneralStoreCategoryOffer offer = FindOffer(itemId);
                return offer != null ? System.Math.Max(1, offer.StoreUnitsPerItemUnit) : 1;
            }

            public int CategoryStockUnits(string categoryId) => 0;
            public int CategoryTargetStockUnits(string categoryId) => int.MaxValue;
            public int CategoryPricePerUnitCents(string categoryId) => 0;
            public bool TryConsumeStoreUnits(string categoryId, int units, out int consumedUnits)
            {
                consumedUnits = 0;
                return false;
            }
            public void RecordCashSettlement(string categoryId, int unitsSold, int revenueCents)
            {
                CashBalance += System.Math.Max(0, revenueCents);
            }
            public void RecordCreditSettlement(string categoryId, int unitsSold, int amountCents, string obligationId)
            {
                LastCreditSettlementUnits = unitsSold;
            }
            public string SpendCash(int amountCents, string purpose) => "stub does not spend";
            public string ReceiveStock(string categoryId, int units, string provenanceLabel) => "stub does not receive";
            public int CategoryReceivableUnits(string categoryId) => int.MaxValue;
            public int DeliveryFeeCents(string itemId, int units) => 0;

            public bool UsesItemLevelInventory(string itemId) => FindOffer(itemId)?.UsesItemLevelInventory == true;

            // Fixed-port surface: household units out, store units inside.
            public int ItemStockUnits(string itemId)
            {
                int stock = StockStoreUnits.TryGetValue(itemId, out int s) ? s : 0;
                return stock / Factor(itemId);
            }

            public int ItemPricePerUnitCents(string itemId)
            {
                int price = PricePerStoreUnitCents.TryGetValue(itemId, out int p) ? p : 0;
                return price * Factor(itemId);
            }

            public bool TryConsumeItemStockUnits(string itemId, int units, out int consumedUnits)
            {
                consumedUnits = 0;
                int needed = units * Factor(itemId);
                int have = StockStoreUnits.TryGetValue(itemId, out int s) ? s : 0;
                if (needed <= 0 || have < needed) return false;
                StockStoreUnits[itemId] = have - needed;
                consumedUnits = units;
                return true;
            }

            public void RecordItemCashSettlement(string categoryId, int unitsSold, int revenueCents)
            {
                LastCashSettlementUnits = unitsSold;
                LastCashSettlementRevenue = revenueCents;
                CashBalance += System.Math.Max(0, revenueCents);
            }
        }

        private static GeneralStoreSupplierAdapter LampOilAdapter(out StubItemPort port)
        {
            port = new StubItemPort();
            port.OfferList.Add(new GeneralStoreCategoryOffer
            {
                CategoryId = "household_goods",
                OfferedItemIds = new List<string> { "lamp_oil" },
                StoreUnitsPerItemUnit = 4,
                UsesItemLevelInventory = true,
            });
            port.StockStoreUnits["lamp_oil"] = 10;
            port.PricePerStoreUnitCents["lamp_oil"] = 25;
            return new GeneralStoreSupplierAdapter(port);
        }

        [Test]
        public void LampOil_StockAndPrice_AreQuotedInGallons()
        {
            GeneralStoreSupplierAdapter adapter = LampOilAdapter(out StubItemPort _);
            Assert.AreEqual(2, adapter.ItemStockUnits("lamp_oil"));
            Assert.AreEqual(100, adapter.ItemPricePerUnitCents("lamp_oil"));
        }

        [Test]
        public void LampOil_SaleOfOneGallon_ConsumesFourQuartsAndPostsFourStoreUnits()
        {
            GeneralStoreSupplierAdapter adapter = LampOilAdapter(out StubItemPort port);
            var diag = new List<string>();
            int sold = adapter.SellItem("lamp_oil", 1, 0, null, diag);
            Assert.AreEqual(1, sold);
            Assert.AreEqual(1, adapter.ItemStockUnits("lamp_oil"));
            Assert.AreEqual(100, port.CashBalance);
            Assert.AreEqual(4, port.LastCashSettlementUnits);
            Assert.AreEqual(100, port.LastCashSettlementRevenue);
        }

        [Test]
        public void LampOil_PartialStock_FloorsToWholeGallonsAndRefusesMore()
        {
            GeneralStoreSupplierAdapter adapter = LampOilAdapter(out StubItemPort port);
            port.StockStoreUnits["lamp_oil"] = 6;
            Assert.AreEqual(1, adapter.ItemStockUnits("lamp_oil"));
            var diag = new List<string>();
            Assert.AreEqual(0, adapter.SellItem("lamp_oil", 2, 0, null, diag));
            Assert.AreEqual(6, port.StockStoreUnits["lamp_oil"]);
            Assert.AreEqual(0, port.CashBalance);
        }

        [Test]
        public void LampOil_TradeCreditSettlement_PostsStoreUnitsAsReceivable()
        {
            GeneralStoreSupplierAdapter adapter = LampOilAdapter(out StubItemPort port);
            var settlement = new ShoppingSettlement
            {
                Mode = ShoppingSettlementMode.TradeCreditObligation,
                ObligationId = "obl-test-1",
            };
            var diag = new List<string>();
            Assert.AreEqual(1, adapter.SellItem("lamp_oil", 1, 0, settlement, diag));
            Assert.AreEqual(4, port.LastCreditSettlementUnits);
        }

        [Test]
        public void FactorOneItem_IsUnchanged()
        {
            var port = new StubItemPort();
            port.OfferList.Add(new GeneralStoreCategoryOffer
            {
                CategoryId = "staple_food",
                OfferedItemIds = new List<string> { "bread" },
                StoreUnitsPerItemUnit = 1,
                UsesItemLevelInventory = true,
            });
            port.StockStoreUnits["bread"] = 5;
            port.PricePerStoreUnitCents["bread"] = 10;
            var adapter = new GeneralStoreSupplierAdapter(port);
            Assert.AreEqual(5, adapter.ItemStockUnits("bread"));
            Assert.AreEqual(10, adapter.ItemPricePerUnitCents("bread"));
            Assert.AreEqual(2, adapter.SellItem("bread", 2, 0, null, new List<string>()));
            Assert.AreEqual(20, port.CashBalance);
            Assert.AreEqual(2, port.LastCashSettlementUnits);
        }

        // ---- W5: the adapter's package surface for flour ----

        private static GeneralStoreSupplierAdapter FlourSackAdapter(out StubItemPort port)
        {
            port = new StubItemPort();
            port.OfferList.Add(new GeneralStoreCategoryOffer
            {
                CategoryId = "staple_food",
                OfferedItemIds = new List<string> { "flour" },
                StoreUnitsPerItemUnit = 1,
                HouseholdUnitsPerStoreUnit = 10,
                UsesItemLevelInventory = true,
            });
            port.StockStoreUnits["flour"] = 48;
            port.PricePerStoreUnitCents["flour"] = 35;
            return new GeneralStoreSupplierAdapter(port);
        }

        [Test]
        public void Flour_PackageOffer_IsStockedAndPricedPerSack()
        {
            GeneralStoreSupplierAdapter adapter = FlourSackAdapter(out StubItemPort _);
            Assert.AreEqual(48, adapter.ItemStockUnits("flour"));
            Assert.AreEqual(35, adapter.ItemPricePerUnitCents("flour"));
            Assert.AreEqual(10, adapter.HouseholdUnitsPerStoreUnit("flour"));
            Assert.AreEqual(1, adapter.HouseholdUnitsPerStoreUnit("bread"),
                "An item with no package offer sells one household unit per store unit.");
        }

        [Test]
        public void Flour_SaleOfThreeSacks_ConsumesThreeSacksAndPostsSackRevenue()
        {
            GeneralStoreSupplierAdapter adapter = FlourSackAdapter(out StubItemPort port);
            var diag = new List<string>();
            Assert.AreEqual(3, adapter.SellItem("flour", 3, 0, null, diag));
            Assert.AreEqual(45, adapter.ItemStockUnits("flour"));
            Assert.AreEqual(105, port.CashBalance);
            Assert.AreEqual(3, port.LastCashSettlementUnits);
            Assert.AreEqual(105, port.LastCashSettlementRevenue);
        }
    }
}
