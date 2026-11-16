using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Ordering;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>CAMPAIGN-B (DEF-005), package 8: the retail bread buyer exists.
    ///
    /// VERDICT — wired end to end, NOT a mechanical break. Check 1's two
    /// premises are both true and neither supports its conclusion:
    ///
    /// (a) "No bread-specific code in the shopping loop" is true and
    /// irrelevant. The loop is item-generic by design: the household
    /// shortage monitor builds one need per item in
    /// MealSchedulingPolicy.MealItemPriority, bread FIRST
    /// (DriveHouseholdShortageMonitor, SimulationDrivers.cs), and the
    /// loop serves a need by its item id through
    /// GeneralStoreSupplierAdapter.SellItem. No bread-specific buyer
    /// rule is needed for bread to be bought; the need IS the buyer
    /// signal, and the store's per-item offers ARE the bread handling.
    ///
    /// (b) "staple_food defaults to flour" is true and irrelevant to
    /// dev shopping. HouseholdItemForStoreCategory (flour for
    /// staple_food) has exactly ONE call site: the fallback port's
    /// single-item-category branch, which only fires when a category
    /// holds exactly one authored item. staple_food holds seven, so
    /// the branch never fires for it. The NPC port dev shopping uses
    /// builds per-item offers through Series2TradingPortBuilder and
    /// marks them UsesItemLevelInventory, and never consults the
    /// default. Households buy flour AND bread as separate items from
    /// separate identified lots; they do not buy flour instead of
    /// bread.
    ///
    /// The full chain, verified at source level: bread need (meal
    /// priority first) -&gt; shoppable need (adapter recognizes bread,
    /// KnownSellerRecognitionPredicate) -&gt; NPC port offers bread
    /// (authored item, household item, 1-loaf conversion, positive
    /// price; UsesItemLevelInventory) -&gt; wholesale driver stocks
    /// identified bread lots (StockShelfToLive -&gt;
    /// ReceiveCostedRetailItemLot) + opening inventory seeds
    /// identified bread lots -&gt; SellItem consumes the lots and posts
    /// cash -&gt; bought bread is eaten first (meal serving walks the
    /// same priority list). The Unity-only port test
    /// (GeneralStoreItemOfferIdentityTests) covers the live manager
    /// port's item-level path; these Roslyn tests pin the builder,
    /// the adapter, and the wiring the port feeds. No live run has
    /// yet shown a loaf actually bought — that proof is still owed
    /// (Codex simulation test), which is why DEF-005 stays CONFIRMED
    /// as "no proven retail bread buyer" rather than being closed.
    ///
    /// No price, buyer rule, or preference term is invented here.
    /// Test prices (5c bread, 7c flour) are test parameters only,
    /// not canon. No production file is changed by this package.</summary>
    [TestFixture]
    public sealed class CampaignBBreadBuyerWiringTests
    {
        private const string ManagerPath =
            "Assets/Scripts/Domains/Economy/Businesses/GeneralStore/GeneralStoreRuntimeManager.cs";
        private const string DriversPath =
            "Assets/Scripts/Orchestration/Systems/SimulationDrivers.cs";
        private const string WholesaleDriverPath =
            "Assets/Scripts/Domains/Economy/Ordering/Review2BakeryToStoreWholesaleDriver.cs";

        private static readonly string[] StapleFoodItems =
        {
            "flour", "bread", "potatoes", "salt", "sugar", "coffee", "tea",
        };

        // Test parameters only, not canon: any positive prices prove the
        // wiring; no retail bread price is asserted here.
        private const int TestBreadPriceCents = 5;
        private const int TestFlourPriceCents = 7;

        // ---------- (1) The need side names bread ----------

        [Test]
        public void MealPriority_NamesBreadFirst()
        {
            List<string> priority = MealSchedulingPolicy.Default.MealItemPriority;
            Assert.IsNotNull(priority);
            Assert.AreEqual(HouseholdItemCatalog.BreadId, priority[0],
                "Bread must be the first meal item: the shortage monitor builds a bread need before any other food need.");
            Assert.IsTrue(priority.IndexOf(HouseholdItemCatalog.BreadId) < priority.IndexOf(HouseholdItemCatalog.FlourId),
                "Bread must outrank flour in meal priority, so a bread need exists alongside (not behind) a flour need.");
        }

        [Test]
        public void BreadCatalogEntry_IsFoodLoafInStapleFood()
        {
            HouseholdItemDefinition bread = HouseholdItemCatalog.Get(HouseholdItemCatalog.BreadId);
            Assert.IsNotNull(bread, "Bread must be a real household item, or no need for it can ever be shoppable.");
            Assert.IsTrue(bread.IsFood);
            Assert.AreEqual("loaf", bread.UnitLabel);
            Assert.AreEqual("staple_food", bread.ReserveCategoryId);
            Assert.AreEqual(1, bread.MealServingUnits);
        }

        // ---------- (2) The offer side offers bread as its own item ----------

        [Test]
        public void Builder_OffersBreadAsItsOwnItem()
        {
            var diag = new List<string>();
            List<GeneralStoreCategoryOffer> offers = Series2TradingPortBuilder.BuildPerItemOffers(
                "staple_food", new List<string>(StapleFoodItems), diag);
            Assert.AreEqual(7, offers.Count,
                "All seven authored staple_food items must be offered, one per item — never collapsed to a single representative. Diag: "
                + string.Join("; ", diag));
            GeneralStoreCategoryOffer bread = FindOfferFor(offers, "bread");
            Assert.IsNotNull(bread, "Bread must have its own per-item offer.");
            Assert.AreEqual(1, bread.OfferedItemIds.Count);
            Assert.AreEqual("bread", bread.OfferedItemIds[0]);
            Assert.AreEqual(1, bread.StoreUnitsPerItemUnit);
            Assert.IsNotNull(FindOfferFor(offers, "flour"), "Flour must ALSO be offered — bread does not replace it.");
        }

        [Test]
        public void BreadConversion_IsOneLoafPerLoaf()
        {
            bool ok = RetailUnitConversions.TryGetStoreUnitsPerHouseholdUnit(
                "bread", "loaf", "loaf", out int factor, out string reason);
            Assert.IsTrue(ok, "Bread must convert 1 loaf to 1 loaf: " + reason);
            Assert.AreEqual(1, factor);
            bool packageOk = RetailUnitConversions.TryGetHouseholdUnitsPerStoreUnit(
                "bread", "loaf", "loaf", out int perStoreUnit, out string packageReason);
            Assert.IsTrue(packageOk, "Bread must resolve as one household unit per store unit: " + packageReason);
            Assert.AreEqual(1, perStoreUnit);
        }

        // ---------- (3) The adapter sells bread from item-level stock ----------

        [Test]
        public void Adapter_RecognizesAndOffersStockedBread()
        {
            var adapter = new GeneralStoreSupplierAdapter(NpcShapedPort());
            Assert.IsTrue(adapter.RecognizesItem("bread"), "Bread must be recognized: it has a per-item offer.");
            Assert.IsTrue(adapter.OffersItem("bread"), "Stocked bread at a positive price must be offered.");
            Assert.AreEqual(24, adapter.ItemStockUnits("bread"));
            Assert.AreEqual(TestBreadPriceCents, adapter.ItemPricePerUnitCents("bread"));
            Assert.IsTrue(adapter.RecognizesItem("flour"), "Flour must still be recognized alongside bread.");
            Assert.IsTrue(adapter.OffersItem("flour"), "Flour must still be offered alongside bread.");
        }

        [Test]
        public void Adapter_SellsBreadFromIdentifiedStock()
        {
            ItemLevelBreadStorePort port = NpcShapedPort();
            var adapter = new GeneralStoreSupplierAdapter(port);
            var diag = new List<string>();
            var settlement = new ShoppingSettlement { Mode = ShoppingSettlementMode.CashFromLedger };
            int sold = adapter.SellItem("bread", 6, 3, settlement, diag);
            Assert.AreEqual(6, sold, "Six loaves requested against 24 stocked must sell six. Diag: " + string.Join("; ", diag));
            Assert.AreEqual(18, port.ItemStockUnits("bread"));
            Assert.AreEqual(6 * TestBreadPriceCents, port.CashBalance, "The store must post exactly the bread revenue, no more.");
            Assert.IsTrue(adapter.Log.Count > 0 && adapter.Log[adapter.Log.Count - 1].IndexOf("identified lot stock", StringComparison.OrdinalIgnoreCase) >= 0,
                "The sale must be recorded as an identified-lot sale, not a pooled-category sale.");
        }

        [Test]
        public void Adapter_BreadSaleDoesNotTouchFlourStock()
        {
            ItemLevelBreadStorePort port = NpcShapedPort();
            var adapter = new GeneralStoreSupplierAdapter(port);
            var settlement = new ShoppingSettlement { Mode = ShoppingSettlementMode.CashFromLedger };
            Assert.AreEqual(6, adapter.SellItem("bread", 6, 3, settlement, new List<string>()));
            Assert.AreEqual(24, port.ItemStockUnits("flour"),
                "Bread and flour draw from separate item stocks: selling bread must not consume flour.");
            Assert.AreEqual(18, port.ItemStockUnits("bread"));
        }

        [Test]
        public void Adapter_SellsPartialFillWhenBreadShort()
        {
            ItemLevelBreadStorePort port = NpcShapedPort();
            var adapter = new GeneralStoreSupplierAdapter(port);
            var settlement = new ShoppingSettlement { Mode = ShoppingSettlementMode.CashFromLedger };
            int sold = adapter.SellItem("bread", 30, 3, settlement, new List<string>());
            Assert.AreEqual(24, sold, "Thirty loaves against 24 stocked must sell the 24 on hand, not refuse or fake the rest.");
            Assert.AreEqual(0, port.ItemStockUnits("bread"));
            Assert.AreEqual(24 * TestBreadPriceCents, port.CashBalance);
        }

        [Test]
        public void Adapter_BreadOutOfStock_SellsNothing()
        {
            ItemLevelBreadStorePort port = NpcShapedPort();
            port.ItemStock["bread"] = 0;
            var adapter = new GeneralStoreSupplierAdapter(port);
            Assert.IsTrue(adapter.RecognizesItem("bread"), "Bread stays recognized when out of stock — the offer is not erased.");
            Assert.IsFalse(adapter.OffersItem("bread"), "Bread with no stock must not be offered.");
            var settlement = new ShoppingSettlement { Mode = ShoppingSettlementMode.CashFromLedger };
            Assert.AreEqual(0, adapter.SellItem("bread", 6, 3, settlement, new List<string>()));
            Assert.AreEqual(0, port.CashBalance, "No sale, no cash posted.");
            Assert.AreEqual(24, port.ItemStockUnits("flour"), "Flour stock must be untouched by a failed bread sale.");
        }

        [Test]
        public void Adapter_PooledCategorySaleRefusedWhenItemLevel()
        {
            ItemLevelBreadStorePort port = NpcShapedPort();
            var adapter = new GeneralStoreSupplierAdapter(port);
            var diag = new List<string>();
            int sold = adapter.Sell("staple_food", 6, 3, diag);
            Assert.AreEqual(0, sold,
                "A pooled category sale must be refused when the offers are item-level: the loop must buy bread by item id, not by category.");
            Assert.IsTrue(diag.Count > 0 && diag[0].IndexOf("exact item ID", StringComparison.OrdinalIgnoreCase) >= 0,
                "The refusal must say why: an exact item id is required. Diag: " + string.Join("; ", diag));
            Assert.AreEqual(24, port.ItemStockUnits("bread"));
            Assert.AreEqual(0, port.CashBalance);
        }

        // ---------- (4) Structural pins: the production wiring behind the seam ----------

        [Test]
        public void NpcPort_MarksOffersItemLevel()
        {
            string manager = ReadRepoFile(ManagerPath);
            int npcOverload = manager.IndexOf("BuildHouseholdTradingPort(BusinessInstanceState npcBusiness)", StringComparison.Ordinal);
            Assert.IsTrue(npcOverload >= 0, "The NPC port overload must exist on the manager.");
            int occurrences = CountOccurrences(manager, "UsesItemLevelInventory = true");
            Assert.AreEqual(2, occurrences,
                "The NPC overload must mark its offers item-level (bread offer + runtime-feed offer); if this count changes, the item-level path changed with it.");
            Assert.IsTrue(manager.IndexOf("UsesItemLevelInventory = true", StringComparison.Ordinal) > npcOverload,
                "The item-level marking must sit inside the NPC overload, not the fallback port.");
        }

        [Test]
        public void FlourDefault_HasExactlyOneCallSite_InSingleItemBranch()
        {
            string manager = ReadRepoFile(ManagerPath);
            Assert.AreEqual(2, CountOccurrences(manager, "HouseholdItemForStoreCategory("),
                "The flour default must have exactly two mentions: its definition and ONE call site. A second call site would mean the default reaches another path.");
            int call = manager.IndexOf("HouseholdItemForStoreCategory(category.CategoryId)", StringComparison.Ordinal);
            Assert.IsTrue(call >= 0, "The single call site must still exist.");
            string before = manager.Substring(Math.Max(0, call - 800), Math.Min(800, call));
            Assert.IsTrue(before.IndexOf("TryResolveSingleItemForCategory", StringComparison.Ordinal) >= 0,
                "The single call site must sit inside the single-item-category branch: the flour default can only fire for a one-item category, and staple_food holds seven.");
        }

        [Test]
        public void WholesaleDriver_RoutesItemLevelStockToIdentifiedLots()
        {
            string driver = ReadRepoFile(WholesaleDriverPath);
            Assert.IsTrue(driver.IndexOf("StockShelfToLive", StringComparison.Ordinal) >= 0);
            Assert.IsTrue(driver.IndexOf("UsesItemLevelInventory", StringComparison.Ordinal) >= 0,
                "StockShelfToLive must check the item-level flag before stocking a wholesale lot.");
            Assert.IsTrue(driver.IndexOf("ReceiveCostedRetailItemLot", StringComparison.Ordinal) >= 0,
                "An item-level wholesale lot must land as an identified, costed retail lot — that is the bread stock the adapter sells from.");
        }

        [Test]
        public void ShortageMonitor_BuildsNeedsFromMealPriority()
        {
            string drivers = ReadRepoFile(DriversPath);
            int monitor = drivers.IndexOf("DriveHouseholdShortageMonitor(int absoluteDayIndex", StringComparison.Ordinal);
            Assert.IsTrue(monitor >= 0, "The household shortage monitor must exist.");
            string body = drivers.Substring(monitor, Math.Min(1500, drivers.Length - monitor));
            Assert.IsTrue(body.IndexOf("MealItemPriority", StringComparison.Ordinal) >= 0,
                "The shortage monitor must build expected daily use from MealItemPriority — that is where the bread need comes from.");
        }

        // ---------- seam: an NPC-shaped item-level port ----------

        /// <summary>
        /// Mirrors the production NPC overload's offer shape: one offer per
        /// item, UsesItemLevelInventory set, flour grouped separately at
        /// factor 10 / package 10 (sacks) and bread at factor 1 / package 1
        /// (loaves), flour's offer listed first (FindOffer returns it).
        /// Stock lives per item, as identified lots do in production; the
        /// pooled category methods exist only to satisfy the interface and
        /// are never reached by the item-level tests (Sell refuses first).
        /// </summary>
        private sealed class ItemLevelBreadStorePort : IItemLevelGeneralStoreTradingPort
        {
            public Dictionary<string, int> ItemStock = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, int> ItemPrices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public List<GeneralStoreCategoryOffer> OfferList = new List<GeneralStoreCategoryOffer>();
            public int CashBalance;

            public string StoreBusinessId => "general-store-1";
            public string StoreName => "General Store";
            public string LocationId => "general-store";
            public IReadOnlyList<GeneralStoreCategoryOffer> CategoryOffers => OfferList;
            public StoreTradingCapability TradingCapability => new StoreTradingCapability { Staffed = true };
            public int CurrentCashCents => CashBalance;
            public bool OffersDelivery => false;
            public bool OffersTradeCredit => false;
            public int TradeCreditLimitCents => 0;

            public int CategoryStockUnits(string categoryId)
            {
                int total = 0;
                foreach (GeneralStoreCategoryOffer offer in OfferList)
                {
                    if (offer == null || !string.Equals(offer.CategoryId, categoryId, StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (string itemId in offer.OfferedItemIds)
                        total += ItemStockUnits(itemId);
                }
                return total;
            }

            public int CategoryPricePerUnitCents(string categoryId) => 0;

            public bool TryConsumeStoreUnits(string categoryId, int units, out int consumedUnits)
            {
                consumedUnits = 0;
                return false;
            }

            public void RecordCashSettlement(string categoryId, int unitsSold, int revenueCents)
            {
                CashBalance += Math.Max(0, revenueCents);
            }

            public void RecordCreditSettlement(string categoryId, int unitsSold, int amountCents, string obligationId)
            {
            }

            public string SpendCash(int amountCents, string purpose) => "seam port holds no spendable cash authority";
            public string ReceiveStock(string categoryId, int units, string provenanceLabel) => "seam port takes no pooled stock";

            public int CategoryReceivableUnits(string categoryId) => int.MaxValue;
            public int CategoryTargetStockUnits(string categoryId) => int.MaxValue;
            public int DeliveryFeeCents(string itemId, int units) => 0;

            public bool UsesItemLevelInventory(string itemId)
            {
                foreach (GeneralStoreCategoryOffer offer in OfferList)
                {
                    if (offer == null || !offer.UsesItemLevelInventory || offer.OfferedItemIds == null) continue;
                    foreach (string offered in offer.OfferedItemIds)
                        if (string.Equals(offered, itemId, StringComparison.OrdinalIgnoreCase)) return true;
                }
                return false;
            }

            public int ItemStockUnits(string itemId) => ItemStock.TryGetValue(itemId, out int units) ? Math.Max(0, units) : 0;
            public int ItemPricePerUnitCents(string itemId) => ItemPrices.TryGetValue(itemId, out int price) ? Math.Max(0, price) : 0;

            public bool TryConsumeItemStockUnits(string itemId, int units, out int consumedUnits)
            {
                consumedUnits = 0;
                int have = ItemStockUnits(itemId);
                int take = Math.Min(Math.Max(0, units), have);
                if (take <= 0) return false;
                ItemStock[itemId] = have - take;
                consumedUnits = take;
                return true;
            }

            public void RecordItemCashSettlement(string categoryId, int unitsSold, int revenueCents)
            {
                CashBalance += Math.Max(0, revenueCents);
            }
        }

        private static ItemLevelBreadStorePort NpcShapedPort()
        {
            var port = new ItemLevelBreadStorePort();
            port.OfferList.Add(new GeneralStoreCategoryOffer
            {
                CategoryId = "staple_food",
                OfferedItemIds = new List<string> { "flour" },
                StoreUnitsPerItemUnit = 10,
                HouseholdUnitsPerStoreUnit = 10,
                UsesItemLevelInventory = true,
            });
            port.OfferList.Add(new GeneralStoreCategoryOffer
            {
                CategoryId = "staple_food",
                OfferedItemIds = new List<string> { "bread" },
                StoreUnitsPerItemUnit = 1,
                HouseholdUnitsPerStoreUnit = 1,
                UsesItemLevelInventory = true,
            });
            port.ItemStock["bread"] = 24;
            port.ItemPrices["bread"] = TestBreadPriceCents;
            port.ItemStock["flour"] = 24;
            port.ItemPrices["flour"] = TestFlourPriceCents;
            return port;
        }

        private static GeneralStoreCategoryOffer FindOfferFor(List<GeneralStoreCategoryOffer> offers, string itemId)
        {
            foreach (GeneralStoreCategoryOffer offer in offers)
            {
                if (offer?.OfferedItemIds == null) continue;
                foreach (string offered in offer.OfferedItemIds)
                    if (string.Equals(offered, itemId, StringComparison.OrdinalIgnoreCase)) return offer;
            }
            return null;
        }

        private static int CountOccurrences(string text, string needle)
        {
            int count = 0;
            int index = 0;
            while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += needle.Length;
            }
            return count;
        }

        private static string FindRepoRoot()
        {
            string fromEnv = Environment.GetEnvironmentVariable("LAND_LEDGERS_REPO");
            if (!string.IsNullOrWhiteSpace(fromEnv)
                && File.Exists(Path.Combine(fromEnv, ManagerPath)))
            {
                return fromEnv;
            }

            var starts = new[]
            {
                Directory.GetCurrentDirectory(),
                AppContext.BaseDirectory,
            };
            var tried = new StringBuilder();
            foreach (string start in starts)
            {
                string dir = start;
                while (!string.IsNullOrEmpty(dir))
                {
                    foreach (string candidate in new[]
                    {
                        dir,
                        Path.Combine(dir, "work-b"),
                    })
                    {
                        tried.AppendLine("  " + candidate);
                        if (File.Exists(Path.Combine(candidate, ManagerPath)))
                        {
                            return candidate;
                        }
                    }
                    DirectoryInfo parent = Directory.GetParent(dir);
                    dir = parent == null ? null : parent.FullName;
                }
            }

            Assert.IsTrue(false, "Campaign B (DEF-005): could not locate the Land & Ledgers repo. "
                + "Set LAND_LEDGERS_REPO, or run from the campaign tree. Searched:\n"
                + tried + "  LAND_LEDGERS_REPO=" + (fromEnv ?? "<unset>"));
            return null;
        }

        private static string ReadRepoFile(string relativePath)
        {
            string root = FindRepoRoot();
            string full = Path.Combine(root, relativePath);
            Assert.IsTrue(File.Exists(full), "Campaign B (DEF-005): expected repo file missing: " + full);
            return File.ReadAllText(full);
        }
    }
}
