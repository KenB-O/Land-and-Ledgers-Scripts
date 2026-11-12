using System.Collections.Generic;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    /// <summary>
    /// Seller recognition controls shopping feasibility, not whether a real
    /// household shortage exists. Unservable needs stay visible and are
    /// updated in place rather than duplicated each day.
    /// </summary>
    [TestFixture]
    public sealed class HouseholdShoppingUnservableNeedsTests
    {
        /// <summary>Minimal item-resolving supplier: recognition is a fixed
        /// set, independent of stock (a recognized item may hold zero units).</summary>
        private sealed class FakeRecognizingSupplier : IItemResolvingSupplier, IItemCatalogSupplier
        {
            public string SupplierBusinessId { get; set; } = "store-1";
            public string SupplierName { get; set; } = "General Store";
            public string LocationId { get; set; } = "store";
            public HashSet<string> Recognized = new HashSet<string>();
            public Dictionary<string, int> Stock = new Dictionary<string, int>();
            public Dictionary<string, int> Prices = new Dictionary<string, int>();

            public bool HasCategory(string categoryId) => true;
            public int StockUnits(string categoryId) => 0;
            public int PricePerUnitCents(string categoryId) => 0;
            public int Sell(string categoryId, int requestedUnits, int dayIndex, List<string> diagnostics) => 0;

            public bool RecognizesItem(string itemId) => Recognized.Contains(itemId);
            public bool OffersItem(string itemId) => Recognized.Contains(itemId)
                && ItemStockUnits(itemId) > 0 && ItemPricePerUnitCents(itemId) > 0;
            public string CategoryForItem(string itemId) => Recognized.Contains(itemId) ? "staple_food" : string.Empty;
            public int ItemStockUnits(string itemId) => Stock.TryGetValue(itemId, out int s) ? s : 0;
            public int ItemPricePerUnitCents(string itemId) => Prices.TryGetValue(itemId, out int p) ? p : 0;
            public string CheckTradingCapability(int absoluteDayIndex, int minuteOfDay) => null;
            public string LastReceiptId => string.Empty;
            public int SellItem(string itemId, int requestedUnits, int dayIndex, ShoppingSettlement settlement, List<string> diagnostics) => 0;
        }

        private sealed class Fixture
        {
            public HouseholdState Household = new HouseholdState { id = 7 };
            public HouseholdInventoryRegistry Inventories = new HouseholdInventoryRegistry();
            public HouseholdNeedRegistry Needs = new HouseholdNeedRegistry();
            public SupplierDirectory Directory = new SupplierDirectory();
            public ShopperSellerKnowledge Knowledge = new ShopperSellerKnowledge();
            public List<int> MemberIds = new List<int> { 1, 2 };

            public HouseholdShortageMonitor Monitor()
            {
                return new HouseholdShortageMonitor(Needs) { LowDaysThreshold = 1f };
            }

            public Dictionary<string, int> ExpectedDailyUse()
            {
                // Mirrors MealSchedulingPolicy.MealItemPriority (all food).
                return new Dictionary<string, int>
                {
                    { HouseholdItemCatalog.BreadId, 2 },
                    { HouseholdItemCatalog.PreservedMeatId, 2 },
                    { HouseholdItemCatalog.UnspecifiedMeatId, 2 },
                    { HouseholdItemCatalog.PreservedFoodId, 2 },
                    { HouseholdItemCatalog.PotatoesId, 2 },
                    { HouseholdItemCatalog.FlourId, 2 },
                    { HouseholdItemCatalog.UnspecifiedStapleFoodId, 2 },
                };
            }

            public void Evaluate(HouseholdShortageMonitor monitor, int dayIndex)
            {
                monitor.Evaluate(
                    Household,
                    Inventories.GetOrCreate(7),
                    ExpectedDailyUse(),
                    isColdSeason: false,
                    supplyAccess: new HouseholdSupplyAccess(),
                    upcomingObligationCents: 0,
                    expectedIncomeCents: 0,
                    dayIndex: dayIndex,
                    isRecognizedByKnownSeller: HouseholdShortageMonitor.KnownSellerRecognitionPredicate(
                        Directory, Knowledge, MemberIds));
            }

            public int SuppressionCount(HouseholdShortageMonitor monitor, string itemId)
            {
                int count = 0;
                string needle = $"has an open '{itemId}' shortage";
                foreach (string line in monitor.Diagnostics)
                {
                    if (line != null && line.Contains(needle))
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        private static FakeRecognizingSupplier BreadFlourPotatoStore()
        {
            var store = new FakeRecognizingSupplier();
            store.Recognized.Add(HouseholdItemCatalog.BreadId);
            store.Recognized.Add(HouseholdItemCatalog.FlourId);
            store.Recognized.Add(HouseholdItemCatalog.PotatoesId);
            return store;
        }

        [Test]
        public void PreservedMeat_NeedRemainsVisible_WithBreadOnlyStore()
        {
            Fixture f = new Fixture();
            f.Directory.Register(BreadFlourPotatoStore());
            f.Knowledge.LearnSupplier(1, "store-1", "lives in the same town", 0);
            f.Knowledge.LearnSupplier(2, "store-1", "lives in the same town", 0);
            HouseholdShortageMonitor monitor = f.Monitor();

            // Empty inventory: every meal item is at zero days of supply.
            f.Evaluate(monitor, 0);

            Assert.IsNotNull(f.Needs.GetOpenNeed(7, HouseholdItemCatalog.PreservedMeatId),
                "a genuine shortage remains visible when no seller recognizes it");
            Assert.IsNotNull(f.Needs.GetOpenNeed(7, HouseholdItemCatalog.UnspecifiedMeatId));
            Assert.IsNotNull(f.Needs.GetOpenNeed(7, HouseholdItemCatalog.PreservedFoodId));
            Assert.IsNotNull(f.Needs.GetOpenNeed(7, HouseholdItemCatalog.UnspecifiedStapleFoodId));
            Assert.IsNotNull(f.Needs.GetOpenNeed(7, HouseholdItemCatalog.BreadId),
                "bread is recognized and short, so its need must open normally");
            Assert.IsNotNull(f.Needs.GetOpenNeed(7, HouseholdItemCatalog.FlourId));
            Assert.IsNotNull(f.Needs.GetOpenNeed(7, HouseholdItemCatalog.PotatoesId));
            Assert.Greater(f.Needs.GetOpenNeed(7, HouseholdItemCatalog.BreadId).UnitsNeeded, 0);
        }

        [Test]
        public void RecognizedButOutOfStock_NeedStillOpens()
        {
            Fixture f = new Fixture();
            FakeRecognizingSupplier store = BreadFlourPotatoStore();
            // Recognized, but holding zero stock and no price: a stockout is
            // real demand, not an unservable item.
            Assert.AreEqual(0, store.ItemStockUnits(HouseholdItemCatalog.BreadId));
            f.Directory.Register(store);
            f.Knowledge.LearnSupplier(1, "store-1", "lives in the same town", 0);
            HouseholdShortageMonitor monitor = f.Monitor();

            f.Evaluate(monitor, 0);

            Assert.IsNotNull(f.Needs.GetOpenNeed(7, HouseholdItemCatalog.BreadId),
                "a recognized-but-out-of-stock item must still raise a need");
            Assert.AreEqual(0, f.SuppressionCount(monitor, HouseholdItemCatalog.BreadId),
                "bread must not be suppressed when a known seller recognizes it");
        }

        [Test]
        public void SellerLearnedLater_ExistingNeedUpdatesWithoutDuplication()
        {
            Fixture f = new Fixture();
            HouseholdShortageMonitor monitor = f.Monitor();

            // Day 0: the actual shortages remain visible even though no
            // seller is known yet.
            f.Evaluate(monitor, 0);
            HouseholdPurchasingNeed originalMeatNeed = f.Needs.GetOpenNeed(7, HouseholdItemCatalog.PreservedMeatId);
            Assert.IsNotNull(originalMeatNeed,
                "with no known seller, the meat shortage still remains visible");
            Assert.IsNotNull(f.Needs.GetOpenNeed(7, HouseholdItemCatalog.BreadId),
                "with no known seller, the bread shortage also remains visible");

            // A supplier recognizing preserved_meat is registered and learned.
            var butcher = new FakeRecognizingSupplier
            {
                SupplierBusinessId = "butcher-1",
                SupplierName = "Butcher",
                LocationId = "butcher",
            };
            butcher.Recognized.Add(HouseholdItemCatalog.PreservedMeatId);
            f.Directory.Register(butcher);
            f.Knowledge.LearnSupplier(1, "butcher-1", "lives in the same town", 1);

            // Day 1: the existing need stays open and its seller blocker
            // disappears from the explanation.
            f.Evaluate(monitor, 1);
            Assert.IsNotNull(f.Needs.GetOpenNeed(7, HouseholdItemCatalog.PreservedMeatId),
                "the shortage remains open after seller knowledge changes");
            Assert.AreSame(originalMeatNeed, f.Needs.GetOpenNeed(7, HouseholdItemCatalog.PreservedMeatId),
                "daily reevaluation must update the existing need rather than duplicate it");
            Assert.IsFalse(originalMeatNeed.ReasonSummary.Contains("no known seller"));
        }

        [Test]
        public void UnservableShortage_IsLoggedAndRemainsOpenWithoutDuplicates()
        {
            Fixture f = new Fixture();
            f.Directory.Register(BreadFlourPotatoStore());
            f.Knowledge.LearnSupplier(1, "store-1", "lives in the same town", 0);
            HouseholdShortageMonitor monitor = f.Monitor();

            f.Evaluate(monitor, 0);

            bool found = false;
            foreach (string line in monitor.Diagnostics)
            {
                if (line != null && line.Contains("H7 has an open 'preserved_meat' shortage, but no known seller currently offers it"))
                {
                    found = true;
                }
            }

            Assert.IsTrue(found, "the shortage diagnostic must name the household and the item; diagnostics: "
                + string.Join(" | ", monitor.Diagnostics));
            Assert.AreEqual(1, f.SuppressionCount(monitor, HouseholdItemCatalog.PreservedMeatId),
                "the shortage is logged once per household per day");
            HouseholdPurchasingNeed need = f.Needs.GetOpenNeed(7, HouseholdItemCatalog.PreservedMeatId);
            Assert.IsNotNull(need);
            Assert.IsTrue(need.ReasonSummary.Contains("no known seller"));

            // The next day logs it once more — the signal is not hidden.
            f.Evaluate(monitor, 1);
            Assert.AreEqual(2, f.SuppressionCount(monitor, HouseholdItemCatalog.PreservedMeatId));
            Assert.AreSame(need, f.Needs.GetOpenNeed(7, HouseholdItemCatalog.PreservedMeatId));
        }
    }
}
