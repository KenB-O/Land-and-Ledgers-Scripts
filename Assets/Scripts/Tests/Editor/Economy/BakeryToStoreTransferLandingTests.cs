// Priority 1 Slice 6 — Bakery transfer landing pin (feeds H7 /
// Priority 3). Diagnostic pin only: whether a Bakery wholesale
// delivery can land as sellable retail stock visible to the
// already-registered adapter. This decides whether the post-day-1
// economy has any stock source at all.
//
// Both tests drive the real production chain (bake one batch through
// the real production driver, TransferToShelf, then StockShelfToLive)
// over a real BusinessRuntimeState, with the adapter registered in a
// real SupplierDirectory exactly as the bootstrapper registers it —
// no fake port, no new behaviour asserted. The only test-local seam
// is the trading port, which mirrors the production
// GeneralStoreTradingPort read/write rules over the same real
// BusinessRuntimeState (the production port wraps a MonoBehaviour
// manager and cannot be constructed in Roslyn), exactly as the
// existing BakeryToStoreWholesaleTransferTests.RuntimeStorePort does.
// Every cash/stock move lands in the shared runtime; this is a test
// seam, not a second cash authority.
//
// Narrow correction: NONE expected. If either test fails, the defect
// is in the transfer seam — file it with the failing test and stop
// per the Plan 02 Slice 4 rule; the repair belongs to Priority 3.
using System;
using System.Collections.Generic;
using NUnit.Framework;
using LandLedgers.Economy;
using LandLedgers.Economy.BreadDrivers;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Core;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Ordering;
using LandLedgers.Population;
using LandLedgers.Primitives;

namespace LandLedgers.EditorTests.Economy
{
    [TestFixture]
    public sealed class BakeryToStoreTransferLandingTests
    {
        // ---- test port over a real BusinessRuntimeState --------
        // Mirrors the production GeneralStoreTradingPort read/write
        // rules (item-level offers read identified retail lots, pooled
        // offers read the category pool; ReceiveRetailItemLot lands an
        // identified lot, ReceiveStock lands in the category pool) over
        // the same real BusinessRuntimeState the wholesale cash port
        // uses, so wholesale cash and retail cash reconcile in ONE
        // runtime. The UsesItemLevelInventory flag on the bread offer
        // is what makes this port item-level (NPC-shaped) or
        // fallback-shaped; StockShelfToLive branches on exactly that.
        private sealed class LandingStorePort : IGeneralStoreTradingPort, IItemLevelGeneralStoreTradingPort, IIdentifiedRetailStockReceivingPort
        {
            public BusinessRuntimeState Runtime;
            public Dictionary<string, int> Prices = new Dictionary<string, int>();
            public List<GeneralStoreCategoryOffer> OfferList = new List<GeneralStoreCategoryOffer>();

            public string StoreBusinessId => "general-store-1";
            public string StoreName => "General Store";
            public string LocationId => "building:7";
            public IReadOnlyList<GeneralStoreCategoryOffer> CategoryOffers => OfferList;
            public StoreTradingCapability TradingCapability => new StoreTradingCapability { Staffed = true };
            public int CurrentCashCents => Runtime != null ? Runtime.CurrentCashCents : 0;
            public bool OffersDelivery => false;
            public bool OffersTradeCredit => false;
            public int TradeCreditLimitCents => 0;

            public int CategoryStockUnits(string categoryId)
            {
                CategoryStockState stock = Runtime != null ? Runtime.GetCategoryStock(categoryId) : null;
                return stock != null ? stock.CurrentStockUnits : 0;
            }
            public int CategoryTargetStockUnits(string categoryId)
            {
                CategoryStockState stock = Runtime != null ? Runtime.GetCategoryStock(categoryId) : null;
                return stock != null ? stock.TargetStockUnits : 0;
            }
            public int CategoryPricePerUnitCents(string categoryId)
            {
                return Prices.TryGetValue(categoryId, out int p) ? p : 0;
            }
            public bool UsesItemLevelInventory(string itemId)
            {
                foreach (GeneralStoreCategoryOffer offer in OfferList)
                    if (offer?.UsesItemLevelInventory == true && offer.OfferedItemIds.Contains(itemId)) return true;
                return false;
            }
            public int ItemStockUnits(string itemId) => Runtime != null ? Runtime.GetRetailItemStockUnits(itemId) : 0;
            public int ItemPricePerUnitCents(string itemId) => Prices.TryGetValue(itemId, out int p) ? p : 0;
            public bool TryConsumeItemStockUnits(string itemId, int units, out int consumedUnits)
            {
                consumedUnits = 0;
                // Production rule (GeneralStoreTradingPort.cs:147): the
                // consume is by item AND category — the offer's category.
                // Passing null matches no lot (BusinessRuntimeState
                // matches lot.CategoryId against the argument), so a
                // null category reads as "exhausted" even with stock.
                GeneralStoreCategoryOffer offer = null;
                foreach (GeneralStoreCategoryOffer candidate in OfferList)
                {
                    if (candidate?.OfferedItemIds != null && candidate.OfferedItemIds.Contains(itemId))
                    {
                        offer = candidate;
                        break;
                    }
                }
                if (offer == null || !offer.UsesItemLevelInventory || Runtime == null) return false;
                int factor = Math.Max(1, offer.StoreUnitsPerItemUnit);
                if (!Runtime.TryConsumeRetailItemStockUnits(itemId, offer.CategoryId, units * factor, out int consumedStoreUnits))
                    return false;
                consumedUnits = consumedStoreUnits / factor;
                return consumedUnits > 0;
            }
            public void RecordItemCashSettlement(string categoryId, int unitsSold, int revenueCents)
            {
                Runtime?.RecordRetailItemSettlement(categoryId, unitsSold, revenueCents);
            }
            public string ReceiveRetailItemLot(string lotId, string itemId, string categoryId, string unitLabel,
                int units, string provenance, int dayIndex)
            {
                return Runtime == null ? "no runtime" : Runtime.TryAddRetailItemLot(lotId, itemId, categoryId,
                    unitLabel, units, StoreBusinessId, StoreBusinessId, provenance, dayIndex);
            }
            public bool TryConsumeStoreUnits(string categoryId, int units, out int consumedUnits)
            {
                consumedUnits = 0;
                foreach (GeneralStoreCategoryOffer offer in OfferList)
                    if (offer != null && offer.UsesItemLevelInventory
                        && string.Equals(offer.CategoryId, categoryId, StringComparison.OrdinalIgnoreCase))
                        return false;
                return Runtime != null && Runtime.TryConsumeCategoryStockUnits(categoryId, units, out consumedUnits);
            }
            public void RecordCashSettlement(string categoryId, int unitsSold, int revenueCents)
            {
                if (Runtime != null) Runtime.RecordGenericRetailSettlement(categoryId, unitsSold, revenueCents);
            }
            public void RecordCreditSettlement(string categoryId, int unitsSold, int amountCents, string obligationId) { }
            public string SpendCash(int amountCents, string purpose)
            {
                if (Runtime == null) return "no runtime";
                if (Runtime.CurrentCashCents < amountCents) return "cannot afford";
                Runtime.SpendCents(amountCents);
                return null;
            }
            public string ReceiveStock(string categoryId, int units, string provenanceLabel)
            {
                if (Runtime == null) return "no runtime";
                int accepted = Runtime.AddCategoryStockUnits(categoryId, units);
                if (accepted < units) return $"store accepted {accepted}/{units}u {categoryId}: category full.";
                return null;
            }
            public int CategoryReceivableUnits(string categoryId)
            {
                CategoryStockState stock = Runtime != null ? Runtime.GetCategoryStock(categoryId) : null;
                if (stock == null) return 0;
                return stock.TargetStockUnits > 0 ? Math.Max(0, stock.TargetStockUnits - stock.CurrentStockUnits) : int.MaxValue;
            }
            public int DeliveryFeeCents(string itemId, int units) => 0;
        }

        private sealed class Slice
        {
            public List<string> Diag = new List<string>();
            public BakeryShopRuntime Bakery = new BakeryShopRuntime("pierce-bakery-1");
            public BusinessRuntimeState BakeryRuntime = new BusinessRuntimeState();
            public BusinessRuntimeState StoreRuntime = new BusinessRuntimeState();
            public LandingStorePort StorePort;
            public Pass3StoreCatalog Catalog = new Pass3StoreCatalog();
            public Pass3WholesaleIntake Intake = new Pass3WholesaleIntake();
            public EntityIdRegistry Ids = new EntityIdRegistry();
        }

        private static Func<string, WorkstationComponentView?> OvenFinder()
        {
            var kinds = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "oven-chamber-1", "oven-chamber" },
                { "kneading-table-1", "kneading-table" },
                { "proofing-rack-1", "proofing-rack" },
                { "bake-peels-1", "bake-peels" },
            };
            return assetId => kinds.TryGetValue(assetId, out string kind)
                ? (WorkstationComponentView?)new WorkstationComponentView
                {
                    AssetId = assetId,
                    Kind = kind,
                    Condition01 = 0.9f,
                    IsUsable = true,
                }
                : null;
        }

        // Bake one 24-loaf batch on day 2 through the real production
        // driver, fund both businesses, and transfer the whole batch to
        // the Pass 3 shelf at wholesale 2c / retail 5c (the store
        // definition's evidence values, passed as caller parameters,
        // not driver defaults). Returns the slice with the shelf
        // holding exactly 24u and the live store still empty.
        private static Slice BakeTransferToShelf(bool itemLevelPort)
        {
            var s = new Slice();
            s.StoreRuntime.BindBusinessIdentity("general-store-1");
            s.StoreRuntime.AddCashCents(5000);
            s.StoreRuntime.EnsureCategoryStock("staple_food", 0, 1000);
            s.BakeryRuntime.BindBusinessIdentity("pierce-bakery-1");
            s.BakeryRuntime.AddCashCents(2000);
            s.StorePort = new LandingStorePort { Runtime = s.StoreRuntime };
            s.StorePort.Prices["staple_food"] = 5;
            s.StorePort.Prices["bread"] = 5;
            s.StorePort.OfferList.Add(new GeneralStoreCategoryOffer
            {
                CategoryId = "staple_food",
                OfferedItemIds = new List<string> { "bread" },
                StoreUnitsPerItemUnit = 1,
                HouseholdUnitsPerStoreUnit = 1,
                UsesItemLevelInventory = itemLevelPort,
                ItemPriceOverrideCents = 5,
            });
            Assert.IsNull(s.Bakery.AddOven("bakehouse", new List<string>
            {
                "oven-chamber-1", "kneading-table-1", "proofing-rack-1", "bake-peels-1",
            }, s.Diag), "oven installation should succeed");
            Assert.IsNull(s.Bakery.FlourStock.ReceiveLot(new BakeryFlourLot
            {
                LotId = s.Ids.Allocate(EntityKind.Lot),
                FlourName = BakeryBreadCatalog.FlourItemId,
                Units = 24,
                AcquiredDayIndex = 0,
                ImportOrderId = "test-flour-order",
                OriginName = "Test mill",
            }, s.Diag));
            Assert.IsNull(s.Bakery.FlourStock.ReceiveLot(new BakeryFlourLot
            {
                LotId = s.Ids.Allocate(EntityKind.Lot),
                FlourName = BakeryBreadCatalog.NotionsItemId,
                Units = 2,
                AcquiredDayIndex = 0,
                ImportOrderId = "test-notions-order",
                OriginName = "Test notions",
            }, s.Diag));
            Assert.IsNull(s.Bakery.FuelStock.ReceiveLot(new BakeryFuelLot
            {
                LotId = s.Ids.Allocate(EntityKind.Lot),
                FuelName = BakeryBreadCatalog.FuelItemId,
                Units = 2,
                AcquiredDayIndex = 0,
                ImportOrderId = "test-fuel-order",
                OriginName = "Test fuel",
                IsBootstrapEndowment = false,
            }, s.Diag));
            var driver = new Series2BakeryProductionDriver(
                s.Bakery, new Series2InStockBakeryFuelSource(s.Bakery));
            Assert.IsNotNull(driver.PlanBatches(BakeryBreadCatalog.BreadLoafId, 1, 2, s.Diag));
            Assert.AreEqual(1, driver.RunScheduledDay(
                2, true, WorkstationCatalog.BakeOven, OvenFinder(), null, s.Diag, s.Ids),
                "one batch should bake; diag: " + string.Join("; ", s.Diag));
            Assert.AreEqual(24, s.Bakery.BreadShelf[0].UnitCount, "one batch yields 24 loaves");

            var options = new Review2WholesaleTransferOptions
            {
                Bakery = s.Bakery,
                BakeryProductId = BakeryBreadCatalog.BreadLoafId,
                Units = 24,
                WholesalePriceCents = 2,
                RetailPriceCents = 5,
                TransferDayIndex = 2,
                Intake = s.Intake,
                StoreCatalog = s.Catalog,
                StoreCashPort = new Series2BusinessRuntimeCashPort(s.StoreRuntime),
                BakeryCashPort = new Series2BusinessRuntimeCashPort(s.BakeryRuntime),
                StoreBusinessId = "general-store-1",
                BakeryBusinessId = "pierce-bakery-1",
            };
            Review2WholesaleTransferResult transfer = Review2BakeryToStoreWholesaleDriver.TransferToShelf(options, s.Diag);
            Assert.IsTrue(transfer.Transferred, transfer.Refusal + "; diag: " + string.Join("; ", s.Diag));
            Assert.AreEqual(24, s.Catalog.Find("bread").ShelfUnits, "all 24 loaves sit on the Pass 3 shelf");
            Assert.AreEqual(0, s.StorePort.CategoryStockUnits("staple_food"), "transfer alone does not touch the live store");
            Assert.AreEqual(4952, s.StoreRuntime.CurrentCashCents, "store pays exactly 24 x 2c wholesale");
            return s;
        }

        // The adapter exactly as the bootstrapper registers it: the
        // real GeneralStoreSupplierAdapter over the port, registered in
        // a real SupplierDirectory, then read back from the directory —
        // so the assertions below run against the registered adapter,
        // not a separately constructed one.
        private static GeneralStoreSupplierAdapter RegisteredAdapter(Slice s)
        {
            var directory = new SupplierDirectory();
            GeneralStoreSupplierAdapter.RegisterIn(directory, s.StorePort);
            Assert.AreEqual(1, directory.All.Count, "exactly one supplier is registered");
            Assert.IsTrue(directory.All[0] is GeneralStoreSupplierAdapter, "the registered supplier is the store adapter");
            return (GeneralStoreSupplierAdapter)directory.All[0];
        }

        [Test]
        public void WholesaleTransfer_LandsAsIdentifiedLot_VisibleWithoutRebuild()
        {
            Slice s = BakeTransferToShelf(itemLevelPort: true);
            GeneralStoreSupplierAdapter adapter = RegisteredAdapter(s);
            Assert.AreEqual(0, adapter.ItemStockUnits("bread"), "live store opens empty before stocking");

            Review2StockingResult stocked = Review2BakeryToStoreWholesaleDriver.StockShelfToLive(
                s.Catalog, "bread", 24, "staple_food", s.StorePort, s.Diag, "loaf", "test-bakery-lot", 2);
            Assert.IsTrue(stocked.Stocked, stocked.Refusal);
            Assert.AreEqual(0, s.Catalog.Find("bread").ShelfUnits, "stocking consumes the Pass 3 shelf exactly once");

            Assert.AreEqual(1, s.StoreRuntime.RetailItemLots.Count,
                "one bakery lot creates one identified retail lot, not a second pooled copy");
            Assert.AreEqual(24, s.StoreRuntime.GetRetailItemStockUnits("bread", "staple_food"),
                "the identified lot holds exactly the transferred 24 loaves");
            Assert.AreEqual(24, adapter.ItemStockUnits("bread"),
                "the already-registered adapter sees the transferred lot with no rebuild or re-registration");
            Assert.IsTrue(adapter.OffersItem("bread"), "the adapter offers the transferred bread for sale");

            // The same adapter sells it: 6 loaves at the authored 5c.
            int cashBefore = s.StoreRuntime.CurrentCashCents;
            var diag = new List<string>();
            int sold = adapter.SellItem("bread", 6, 2, null, diag);
            Assert.AreEqual(6, sold, "the adapter sells 6 loaves from the landed lot; diag: " + string.Join("; ", diag));
            Assert.AreEqual(18, adapter.ItemStockUnits("bread"), "the identified lot falls by exactly the units sold");
            Assert.AreEqual(18, s.StoreRuntime.GetRetailItemStockUnits("bread", "staple_food"));
            Assert.AreEqual(cashBefore + 30, s.StoreRuntime.CurrentCashCents,
                "the store gains exactly 6 x 5c retail cash; stocking and selling move stock and cash once each");
        }

        [Test]
        public void WholesaleTransfer_FallbackPort_LandsInCategoryPool()
        {
            Slice s = BakeTransferToShelf(itemLevelPort: false);
            GeneralStoreSupplierAdapter adapter = RegisteredAdapter(s);
            Assert.IsTrue(!s.StorePort.UsesItemLevelInventory("bread"),
                "fallback-shaped port: UsesItemLevelInventory is false for bread");
            Assert.AreEqual(0, adapter.ItemStockUnits("bread"), "live store opens empty before stocking");

            Review2StockingResult stocked = Review2BakeryToStoreWholesaleDriver.StockShelfToLive(
                s.Catalog, "bread", 24, "staple_food", s.StorePort, s.Diag, "loaf", "test-bakery-lot", 2);
            Assert.IsTrue(stocked.Stocked, stocked.Refusal);
            Assert.AreEqual(0, s.Catalog.Find("bread").ShelfUnits, "stocking consumes the Pass 3 shelf exactly once");
            Assert.AreEqual(24, stocked.LivePoolUnitsAfter, "the live pool gains exactly the stocked units");

            Assert.AreEqual(0, s.StoreRuntime.RetailItemLots.Count,
                "the fallback port lands no identified lot — the transfer is pool-only");
            Assert.AreEqual(24, s.StorePort.CategoryStockUnits("staple_food"),
                "the category pool rises by exactly the transferred 24 units");
            Assert.AreEqual(24, adapter.ItemStockUnits("bread"),
                "the already-registered adapter sees the pooled bread with no rebuild or re-registration");
            Assert.IsTrue(adapter.OffersItem("bread"), "the adapter offers the pooled bread for sale");

            // The same adapter sells from the pool: 6 loaves at 5c.
            int cashBefore = s.StoreRuntime.CurrentCashCents;
            var diag = new List<string>();
            int sold = adapter.SellItem("bread", 6, 2, null, diag);
            Assert.AreEqual(6, sold, "the adapter sells 6 loaves from the category pool; diag: " + string.Join("; ", diag));
            Assert.AreEqual(18, adapter.ItemStockUnits("bread"), "the pool falls by exactly the units sold");
            Assert.AreEqual(18, s.StorePort.CategoryStockUnits("staple_food"));
            Assert.AreEqual(cashBefore + 30, s.StoreRuntime.CurrentCashCents,
                "the store gains exactly 6 x 5c retail cash");
        }
    }
}
