using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Population;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Population
{
    /// <summary>
    /// Plan 02 Slice 3 — prove the store's sellable stock is visible.
    /// Investigation first, repair only on a positive finding.
    ///
    /// Harness-reachability note: the REAL port builder
    /// (GeneralStoreRuntimeManager.BuildHouseholdTradingPort) is NOT
    /// harness-reachable — GeneralStoreTradingPort.cs is excluded from the
    /// harness sources and AssetDatabase.LoadAssetAtPath returns null in the
    /// Roslyn harness. So these tests drive the REAL adapter
    /// (GeneralStoreSupplierAdapter) and the REAL BusinessRuntimeState, over
    /// a test port whose stock delegation mirrors the real port's one line
    /// (GeneralStoreTradingPort.ItemStockUnits reads
    /// ActiveState.GetRetailItemStockUnits live on every call). That
    /// delegation, the authored bread/flour offers (factor 1, prices at or
    /// above the 5c floor), and the live-read post-build visibility are
    /// source-reviewed at GeneralStoreTradingPort.cs and
    /// GeneralStoreRuntimeManager.cs (BuildHouseholdTradingPort,
    /// GetItemSellingPriceCents); the harness half below proves the adapter
    /// and the runtime state behave as the source says.
    ///
    /// Probe outcome: NO defect found. At opening the store holds zero
    /// identified lots (by design — the opening-stock rule); one identified
    /// lot through the wholesale receipt seam makes the item viable; lots
    /// added after the port is built and the adapter registered are visible
    /// to the same adapter without any rebuild or re-registration. So this
    /// slice makes NO offer/stock repair — only the once-per-day empty-store
    /// diagnostic in the shopping drive (SimulationDrivers).
    /// </summary>
    [TestFixture]
    public sealed class StoreOfferVisibilityTests
    {
        /// <summary>Test port over a real BusinessRuntimeState. Stock reads
        /// delegate to the live state (exactly as the real port does); prices
        /// are the authored baselines (bread 5c/loaf, flour 35c/sack).</summary>
        private sealed class LiveStatePort : IItemLevelGeneralStoreTradingPort, IIdentifiedRetailStockReceivingPort
        {
            public BusinessRuntimeState State;
            public string StoreBusinessId { get; set; } = "general_store_060";
            public string StoreName { get; set; } = "General Store";
            public string LocationId { get; set; } = "building:60";
            public List<GeneralStoreCategoryOffer> OfferList = new List<GeneralStoreCategoryOffer>();
            public StoreTradingCapability Capability { get; set; } = new StoreTradingCapability();
            public int CashBalance;

            public LiveStatePort(BusinessRuntimeState state)
            {
                State = state;
                OfferList.Add(new GeneralStoreCategoryOffer
                {
                    CategoryId = "staple_food",
                    OfferedItemIds = new List<string> { HouseholdItemCatalog.BreadId },
                    StoreUnitsPerItemUnit = 1,
                    HouseholdUnitsPerStoreUnit = 1,
                    UsesItemLevelInventory = true,
                });
                OfferList.Add(new GeneralStoreCategoryOffer
                {
                    CategoryId = "staple_food",
                    OfferedItemIds = new List<string> { HouseholdItemCatalog.FlourId },
                    StoreUnitsPerItemUnit = 1,
                    HouseholdUnitsPerStoreUnit = 10,
                    UsesItemLevelInventory = true,
                });
            }

            public IReadOnlyList<GeneralStoreCategoryOffer> CategoryOffers => OfferList;
            public StoreTradingCapability TradingCapability => Capability;
            public int CurrentCashCents => CashBalance;
            public bool OffersDelivery => false;
            public bool OffersTradeCredit => false;
            public int TradeCreditLimitCents => 0;

            private GeneralStoreCategoryOffer FindOffer(string itemId)
            {
                if (string.IsNullOrWhiteSpace(itemId)) return null;
                foreach (GeneralStoreCategoryOffer offer in OfferList)
                {
                    if (offer?.OfferedItemIds == null) continue;
                    foreach (string id in offer.OfferedItemIds)
                    {
                        if (string.Equals(id, itemId, System.StringComparison.OrdinalIgnoreCase)) return offer;
                    }
                }

                return null;
            }

            public int CategoryStockUnits(string categoryId)
            {
                CategoryStockState stock = State != null ? State.GetCategoryStock(categoryId) : null;
                return stock != null ? stock.CurrentStockUnits : 0;
            }

            public int CategoryTargetStockUnits(string categoryId)
            {
                CategoryStockState stock = State != null ? State.GetCategoryStock(categoryId) : null;
                return stock != null ? stock.TargetStockUnits : 0;
            }

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

            public void RecordCreditSettlement(string categoryId, int unitsSold, int amountCents, string obligationId) { }

            public string SpendCash(int amountCents, string purpose) => null;

            public string ReceiveStock(string categoryId, int units, string provenanceLabel) => null;

            public int CategoryReceivableUnits(string categoryId) => int.MaxValue;

            public int DeliveryFeeCents(string itemId, int units) => 0;

            public bool UsesItemLevelInventory(string itemId) => FindOffer(itemId)?.UsesItemLevelInventory == true;

            public int ItemStockUnits(string itemId)
            {
                GeneralStoreCategoryOffer offer = FindOffer(itemId);
                if (offer == null) return 0;
                // Mirrors GeneralStoreTradingPort.ItemStockUnits: the
                // item-level path reads identified lots live from the state.
                return offer.UsesItemLevelInventory
                    ? (State?.GetRetailItemStockUnits(itemId, offer.CategoryId) ?? 0) / System.Math.Max(1, offer.StoreUnitsPerItemUnit)
                    : CategoryStockUnits(offer.CategoryId) / System.Math.Max(1, offer.StoreUnitsPerItemUnit);
            }

            public int ItemPricePerUnitCents(string itemId)
            {
                GeneralStoreCategoryOffer offer = FindOffer(itemId);
                if (offer == null) return 0;
                // Authored baselines: bread 5c/loaf, flour 35c/sack.
                int storeUnitPrice = string.Equals(itemId, HouseholdItemCatalog.BreadId, System.StringComparison.OrdinalIgnoreCase) ? 5
                    : string.Equals(itemId, HouseholdItemCatalog.FlourId, System.StringComparison.OrdinalIgnoreCase) ? 35 : 0;
                return storeUnitPrice * System.Math.Max(1, offer.StoreUnitsPerItemUnit);
            }

            public bool TryConsumeItemStockUnits(string itemId, int units, out int consumedUnits)
            {
                consumedUnits = 0;
                GeneralStoreCategoryOffer offer = FindOffer(itemId);
                if (offer == null || State == null) return false;
                return State.TryConsumeRetailItemStockUnits(itemId, offer.CategoryId, units, out consumedUnits);
            }

            public void RecordItemCashSettlement(string categoryId, int unitsSold, int revenueCents)
            {
                CashBalance += System.Math.Max(0, revenueCents);
            }

            /// <summary>The wholesale receipt seam: mirrors
            /// GeneralStoreTradingPort.ReceiveRetailItemLot, which delegates
            /// to BusinessRuntimeState.TryAddRetailItemLot.</summary>
            public string ReceiveRetailItemLot(string lotId, string itemId, string categoryId, string unitLabel,
                int units, string provenance, int dayIndex)
            {
                if (State == null) return "Store runtime unavailable.";
                return State.TryAddRetailItemLot(lotId, itemId, categoryId, unitLabel, units,
                    StoreBusinessId, StoreBusinessId, provenance, dayIndex);
            }
        }

        private static BusinessRuntimeState OpeningState()
        {
            var state = new BusinessRuntimeState();
            state.BindBusinessIdentity("general_store_060");
            state.EnsureCategoryStock("staple_food", 0, 100);
            return state;
        }

        [Test]
        public void OpeningStore_HoldsNoSellableMealStock_Regression()
        {
            // No conjuring: the category starts at zero and no identified
            // lot exists before a receipt (BusinessRuntimeState.cs:1990-1993).
            BusinessRuntimeState state = OpeningState();
            var port = new LiveStatePort(state);
            var adapter = new GeneralStoreSupplierAdapter(port);

            Assert.AreEqual(0, state.GetCategoryStock("staple_food").CurrentStockUnits,
                "opening category stock must be zero");
            Assert.AreEqual(0, state.RetailItemLots.Count, "no identified lots exist before a receipt");
            Assert.AreEqual(0, state.GetRetailItemStockUnits("bread"));
            Assert.AreEqual(0, state.GetRetailItemStockUnits("flour"));
            Assert.AreEqual(0, adapter.ItemStockUnits("bread"), "the adapter must see zero sellable bread at opening");
            Assert.AreEqual(0, adapter.ItemStockUnits("flour"), "the adapter must see zero sellable flour at opening");
            Assert.IsFalse(adapter.OffersItem("bread"), "bread is unviable at opening: no stock to sell");
            Assert.IsFalse(adapter.OffersItem("flour"), "flour is unviable at opening: no stock to sell");
            // Recognition is not stock: bread IS an authored store item.
            Assert.IsTrue(adapter.RecognizesItem("bread"));
            Assert.IsTrue(adapter.RecognizesItem("flour"));
        }

        [Test]
        public void IdentifiedBreadLot_MakesBreadViable()
        {
            BusinessRuntimeState state = OpeningState();
            var port = new LiveStatePort(state);
            var adapter = new GeneralStoreSupplierAdapter(port);

            Assert.IsFalse(adapter.OffersItem("bread"), "no stock yet, so no viable bread offer");

            Assert.IsNull(port.ReceiveRetailItemLot(
                "lot:test:bread:1", "bread", "staple_food", "loaf", 24,
                "wholesale-receipt:test", 0));

            Assert.IsTrue(adapter.RecognizesItem("bread"));
            Assert.AreEqual(24, adapter.ItemStockUnits("bread"));
            Assert.AreEqual(5, adapter.ItemPricePerUnitCents("bread"),
                "a stocked loaf carries the authored 5c baseline price");
            Assert.Greater(adapter.ItemPricePerUnitCents("bread"), 0);
            Assert.IsTrue(adapter.OffersItem("bread"), "stocked bread at a positive price is a viable offer");
            Assert.IsNull(adapter.CheckTradingCapability(0, 520),
                "the store is open and staffed on a weekday at the arrival minute");
        }

        [Test]
        public void PostBuildLot_IsVisibleWithoutRebuild()
        {
            BusinessRuntimeState state = OpeningState();
            var port = new LiveStatePort(state);
            var adapter = new GeneralStoreSupplierAdapter(port);
            var directory = new SupplierDirectory();
            directory.Register(adapter);
            Assert.AreEqual(1, directory.All.Count, "one registered adapter before the delivery");

            // The lot lands AFTER the port was built and the adapter
            // registered — the same adapter instance must see it, with no
            // rebuild and no re-registration (the real port reads the live
            // runtime state on every ItemStockUnits call).
            Assert.AreEqual(0, adapter.ItemStockUnits("bread"));
            Assert.IsNull(port.ReceiveRetailItemLot(
                "lot:test:bread:2", "bread", "staple_food", "loaf", 24,
                "wholesale-receipt:test", 0));
            Assert.AreEqual(24, adapter.ItemStockUnits("bread"),
                "the registered adapter must see the post-build lot without a rebuild");
            Assert.IsTrue(adapter.OffersItem("bread"));
            Assert.AreEqual(1, directory.All.Count, "no re-registration happened");
        }

        [Test]
        public void IdentifiedFlourSack_MakesFlourViable_WholePackage()
        {
            BusinessRuntimeState state = OpeningState();
            var port = new LiveStatePort(state);
            var adapter = new GeneralStoreSupplierAdapter(port);

            Assert.IsFalse(adapter.OffersItem("flour"), "no stock yet, so no viable flour offer");

            // One whole sack received (1 store unit = 10 lb household units).
            Assert.IsNull(port.ReceiveRetailItemLot(
                "lot:test:flour:1", "flour", "staple_food", "10 lb sack", 1,
                "wholesale-receipt:test", 0));

            Assert.IsTrue(adapter.RecognizesItem("flour"));
            Assert.AreEqual(1, adapter.ItemStockUnits("flour"), "one sack is one store unit of flour stock");
            Assert.AreEqual(10, adapter.HouseholdUnitsPerStoreUnit("flour"),
                "one flour sack carries 10 household lb");
            Assert.AreEqual(35, adapter.ItemPricePerUnitCents("flour"),
                "a stocked sack carries the authored 35c baseline price");
            Assert.IsTrue(adapter.OffersItem("flour"), "stocked flour at a positive price is a viable offer");
        }

        [Test]
        public void SecondFlourLot_IsVisibleWithoutRebuild()
        {
            // Plan 05 Slice 2 step 3: the NPC-port shape pin extended to a
            // second lot. A second flour sack landing later must be visible
            // to the same registered adapter with no rebuild and no
            // re-registration — the live-read path Plan 02 Slice 3 proved
            // for bread, now pinned for flour as cumulative stock (2 sacks).
            BusinessRuntimeState state = OpeningState();
            var port = new LiveStatePort(state);
            var adapter = new GeneralStoreSupplierAdapter(port);
            var directory = new SupplierDirectory();
            directory.Register(adapter);

            Assert.IsNull(port.ReceiveRetailItemLot(
                "lot:test:flour:1", "flour", "staple_food", "10 lb sack", 1,
                "wholesale-receipt:test", 0));
            Assert.AreEqual(1, adapter.ItemStockUnits("flour"), "the first sack must be visible");
            Assert.IsNull(port.ReceiveRetailItemLot(
                "lot:test:flour:2", "flour", "staple_food", "10 lb sack", 1,
                "wholesale-receipt:test", 0));
            Assert.AreEqual(2, adapter.ItemStockUnits("flour"),
                "the second sack must be visible too, with no rebuild");
            Assert.AreEqual(20, adapter.ItemStockUnits("flour") * adapter.HouseholdUnitsPerStoreUnit("flour"),
                "two sacks carry 20 household lb");
            Assert.IsTrue(adapter.OffersItem("flour"), "stocked flour at a positive price stays a viable offer");
            Assert.AreEqual(1, directory.All.Count, "no re-registration happened");
        }
    }
}
