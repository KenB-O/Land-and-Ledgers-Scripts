// Priority 5 Slice 9-1 — StoreHorseFeedStockAdapter regression coverage.
//
// The adapter must read a store horse's feed from the General Store's
// own runtime category stock — the unallocated pool AND identified
// retail lots of the feed item — counting each physical unit exactly
// once, and must consume pool-first, all-or-nothing, with exactly-once
// stock movement. The D2/D3 fixture shape is reused throughout:
// AddFeedItem then stock; every id, quantity, and conversion below is
// a caller-supplied test parameter, not canon. No Unity compilation,
// Test Runner, PlayMode, or scene wiring is claimed.
using System.Collections.Generic;
using NUnit.Framework;
using LandLedgers.Economy;
using LandLedgers.Economy.Farming.Integration;

namespace LandLedgers.Tests.Economy
{
    public sealed class StoreHorseFeedStockAdapterTests
    {
        private const string FeedItemId = "d3-test-horse-feed";
        private const string FeedCategoryId = "d3-feed-cat";
        private const string StoreBusinessId = "general-store-1";
        private const int StoreUnitsPerFeedUnit = 2;

        private static BusinessRuntimeState NewStore(Review2StoreFeedRegistry registry, Review2StoreFeedItem item)
        {
            var store = new BusinessRuntimeState();
            store.BindBusinessIdentity(StoreBusinessId);
            var diag = new List<string>();
            Assert.IsNull(Review2StoreHorseFeed.AddFeedItem(store, registry, item, diag),
                "owner add must succeed; diag: " + string.Join("; ", diag));
            return store;
        }

        private static Review2StoreFeedItem DefaultFeedItem() => new Review2StoreFeedItem
        {
            BusinessInstanceId = StoreBusinessId,
            FeedCategoryId = FeedCategoryId,
            FeedItemId = FeedItemId,
            DisplayName = "Test Horse Feed",
            UnitLabel = "sack",
            StoreUnitsPerFeedUnit = StoreUnitsPerFeedUnit,
            RetailPriceCents = 47,
        };

        private static StoreHorseFeedStockAdapter NewAdapter(BusinessRuntimeState store, Review2StoreFeedRegistry registry)
        {
            Review2StoreFeedItem registered = registry.Find(StoreBusinessId);
            Assert.IsNotNull(registered, "the registry must resolve the added feed item");
            return new StoreHorseFeedStockAdapter(store, registered);
        }

        private static int CategoryTotal(BusinessRuntimeState store) =>
            store.GetCategoryStock(FeedCategoryId).CurrentStockUnits;

        private static void AddLot(BusinessRuntimeState store, string lotId, string itemId, int units)
        {
            Assert.IsNull(store.TryAddRetailItemLot(lotId, itemId, FeedCategoryId, "sack",
                    units, StoreBusinessId, StoreBusinessId, "test", 0),
                "lot receipt must succeed for lot '" + lotId + "'");
        }

        private static int LotAvailable(BusinessRuntimeState store, string lotId)
        {
            BusinessRetailItemLotState lot = store.FindRetailItemLot(lotId);
            Assert.IsNotNull(lot, "lot '" + lotId + "' must exist");
            return lot.AvailableUnits;
        }

        [Test]
        public void PoolOnlyStock_ReadsCategoryPool_TenFeedUnits()
        {
            var registry = new Review2StoreFeedRegistry();
            BusinessRuntimeState store = NewStore(registry, DefaultFeedItem());
            Assert.AreEqual(20, store.AddCategoryStockUnits(FeedCategoryId, 20),
                "stocking after the add must land exactly; the add itself creates no stock");
            StoreHorseFeedStockAdapter adapter = NewAdapter(store, registry);

            Assert.AreEqual(10, adapter.FeedStockUnits,
                "20 store units at 2 per feed unit = 10 feed units");
        }

        [Test]
        public void LotOnlyStock_ReadsIdentifiedLots_TenFeedUnits()
        {
            var registry = new Review2StoreFeedRegistry();
            BusinessRuntimeState store = NewStore(registry, DefaultFeedItem());
            AddLot(store, "lot-1", FeedItemId, 20);
            StoreHorseFeedStockAdapter adapter = NewAdapter(store, registry);

            Assert.AreEqual(20, CategoryTotal(store),
                "the category total already includes lot units (inclusion invariant)");
            Assert.AreEqual(10, adapter.FeedStockUnits,
                "20 lot units at 2 per feed unit = 10 feed units");
        }

        [Test]
        public void MixedPoolAndLots_CountsEachUnitOnce()
        {
            var registry = new Review2StoreFeedRegistry();
            BusinessRuntimeState store = NewStore(registry, DefaultFeedItem());
            Assert.AreEqual(20, store.AddCategoryStockUnits(FeedCategoryId, 20));
            AddLot(store, "lot-1", FeedItemId, 10);
            StoreHorseFeedStockAdapter adapter = NewAdapter(store, registry);

            Assert.AreEqual(30, CategoryTotal(store),
                "pool 20 + lot 10 = category total 30");
            Assert.AreEqual(15, adapter.FeedStockUnits,
                "30 store units at 2 per feed unit = 15 feed units, not 20 (double-count) and not 5 (lots-only)");
        }

        [Test]
        public void MixedStock_TakeConsumesPoolFirst_ThenLots()
        {
            var registry = new Review2StoreFeedRegistry();
            BusinessRuntimeState store = NewStore(registry, DefaultFeedItem());
            Assert.AreEqual(20, store.AddCategoryStockUnits(FeedCategoryId, 20));
            AddLot(store, "lot-1", FeedItemId, 20);
            StoreHorseFeedStockAdapter adapter = NewAdapter(store, registry);
            Assert.AreEqual(20, adapter.FeedStockUnits, "category 40 at 2 per feed unit = 20 feed units");

            Assert.AreEqual(12, adapter.TakeFeedUnits(12), "the take must deliver the full request");
            Assert.AreEqual(16, CategoryTotal(store),
                "24 store units consumed: category 40 -> 16");
            Assert.AreEqual(16, LotAvailable(store, "lot-1"),
                "pool exhausted first (20), then 4 from lots: lot 20 -> 16");
        }

        [Test]
        public void LotOnlyStock_TakeConsumesLotsAndCategoryTogether()
        {
            var registry = new Review2StoreFeedRegistry();
            BusinessRuntimeState store = NewStore(registry, DefaultFeedItem());
            AddLot(store, "lot-1", FeedItemId, 20);
            StoreHorseFeedStockAdapter adapter = NewAdapter(store, registry);

            Assert.AreEqual(2, adapter.TakeFeedUnits(2), "the take must deliver the full request");
            Assert.AreEqual(16, LotAvailable(store, "lot-1"), "2 feed units x 2 store units = 4 lot units consumed");
            Assert.AreEqual(16, CategoryTotal(store), "the category total falls by exactly what the lots lost");
        }

        [Test]
        public void InsufficientTotalStock_TakesNothing_NothingMoves()
        {
            var registry = new Review2StoreFeedRegistry();
            BusinessRuntimeState store = NewStore(registry, DefaultFeedItem());
            Assert.AreEqual(2, store.AddCategoryStockUnits(FeedCategoryId, 2));
            AddLot(store, "lot-1", FeedItemId, 2);
            StoreHorseFeedStockAdapter adapter = NewAdapter(store, registry);
            Assert.AreEqual(2, adapter.FeedStockUnits, "category 4 at 2 per feed unit = 2 feed units");

            Assert.AreEqual(0, adapter.TakeFeedUnits(3), "a request above availability takes nothing");
            Assert.AreEqual(4, CategoryTotal(store), "all-or-nothing: the category must not move");
            Assert.AreEqual(2, LotAvailable(store, "lot-1"), "all-or-nothing: the lot must not move");
        }

        [Test]
        public void OtherItemLotsInSameCategory_AreNotFeed()
        {
            var registry = new Review2StoreFeedRegistry();
            BusinessRuntimeState store = NewStore(registry, DefaultFeedItem());
            Assert.AreEqual(10, store.AddCategoryStockUnits(FeedCategoryId, 10));
            AddLot(store, "lot-other", "d3-other-item", 10);
            StoreHorseFeedStockAdapter adapter = NewAdapter(store, registry);

            Assert.AreEqual(5, adapter.FeedStockUnits,
                "only the 10 pool units are feed; another item's 10 lot units are not");
            Assert.AreEqual(2, adapter.TakeFeedUnits(2), "the take must deliver the full request");
            Assert.AreEqual(16, CategoryTotal(store), "4 store units consumed from the pool: category 20 -> 16");
            Assert.AreEqual(10, LotAvailable(store, "lot-other"), "the other item's lot is untouched");
        }

        [Test]
        public void RationalConversion_CarrySurvivesSplitTake()
        {
            var registry = new Review2StoreFeedRegistry();
            var item = DefaultFeedItem();
            item.FeedUnitsPerStoreUnitNumerator = 3;
            item.FeedUnitsPerStoreUnitDenominator = 2;
            item.StoreUnitsPerFeedUnit = 0;
            BusinessRuntimeState store = NewStore(registry, item);
            Assert.AreEqual(5, store.AddCategoryStockUnits(FeedCategoryId, 5));
            StoreHorseFeedStockAdapter adapter = NewAdapter(store, registry);

            Assert.AreEqual(7, adapter.FeedStockUnits, "5 store units at 3 feed units per 2 store units = 7 feed units");
            Assert.AreEqual(2, adapter.TakeFeedUnits(2), "the take must deliver the full request");
            Assert.AreEqual(3, CategoryTotal(store), "2 store units consumed: pool 5 -> 3");
            Review2StoreFeedItem registered = registry.Find(StoreBusinessId);
            Assert.AreEqual(1, registered.UnusedFeedUnits,
                "delivered 3 feed units for a request of 2, so 1 carries over");
            Assert.AreEqual(5, adapter.FeedStockUnits,
                "carry 1 + 3 store units at 3/2 = 1 + 4 = 5 feed units after the take");
        }

        [Test]
        public void EmptyFeedItemId_ReadsWholeCategory()
        {
            var registry = new Review2StoreFeedRegistry();
            BusinessRuntimeState store = NewStore(registry, DefaultFeedItem());
            Assert.AreEqual(20, store.AddCategoryStockUnits(FeedCategoryId, 20));
            var adapter = new StoreHorseFeedStockAdapter(store, FeedCategoryId, StoreUnitsPerFeedUnit);

            Assert.AreEqual(10, adapter.FeedStockUnits,
                "the legacy constructor reads the whole category: 20 store units at 2 per feed unit = 10");
            Assert.AreEqual(2, adapter.TakeFeedUnits(2), "the take must deliver the full request");
            Assert.AreEqual(16, CategoryTotal(store), "2 feed units x 2 store units = 4 store units consumed");
        }
    }
}
