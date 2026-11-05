// Slice 32 (Pass 6): the bakery -> store wholesale transfer, the
// bread supply path. Review2BakeryToStoreWholesaleDriver is
// harness-compiled but its only tests lived in the !-excluded
// Review2VerticalD1Tests.cs, so the harness had never executed a
// wholesale-transfer assertion. These tests mirror that coverage
// into the harness (Slice 29 pattern) and add the refusal branches
// no test anywhere pins. The fixture bakes one batch through the
// real driver with the in-stock fuel source — pre-baked stock,
// test-chosen quantities — and passes wholesale 2c / retail 5c as
// caller parameters (the store definition's evidence values, not
// driver defaults). Nothing here wires a daily transfer or decides
// any part of the daily bake policy (product, batch count, fuel
// source, off-map receipt); the driver stays as unwired as it is.
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
    public sealed class BakeryToStoreWholesaleTransferTests
    {
        // ---- production port over a real BusinessRuntimeState --------
        // The production GeneralStoreTradingPort wraps a MonoBehaviour
        // manager and cannot be constructed in Roslyn; this seam
        // implements the same production interface over the same
        // BusinessRuntimeState the wholesale cash port uses, so
        // wholesale cash and retail cash reconcile in ONE runtime.
        // It is a test seam, not a second cash authority: every cash
        // move lands in the shared runtime.
        private sealed class RuntimeStorePort : IGeneralStoreTradingPort
        {
            public BusinessRuntimeState Runtime;
            public Dictionary<string, int> Prices = new Dictionary<string, int>();
            public int Cap = int.MaxValue;
            public List<GeneralStoreCategoryOffer> OfferList = new List<GeneralStoreCategoryOffer>();

            public string StoreBusinessId => "general-store-1";
            public string StoreName => "General Store";
            public string LocationId => "general-store";
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
            public bool TryConsumeStoreUnits(string categoryId, int units, out int consumedUnits)
            {
                consumedUnits = 0;
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
                if (units > Cap) return $"capped port refuses {units}u (cap {Cap})";
                int accepted = Runtime.AddCategoryStockUnits(categoryId, units);
                if (accepted < units) return $"store accepted {accepted}/{units}u {categoryId}: category full.";
                return null;
            }
            public int CategoryReceivableUnits(string categoryId)
            {
                CategoryStockState stock = Runtime != null ? Runtime.GetCategoryStock(categoryId) : null;
                if (stock == null) return 0;
                if (Cap < int.MaxValue) return Math.Min(Cap, stock.TargetStockUnits > 0 ? Math.Max(0, stock.TargetStockUnits - stock.CurrentStockUnits) : Cap);
                return stock.TargetStockUnits > 0 ? Math.Max(0, stock.TargetStockUnits - stock.CurrentStockUnits) : int.MaxValue;
            }
            public int DeliveryFeeCents(string itemId, int units) => 0;
        }

        private sealed class RefusingCreditPort : IBusinessIdentityCashPort
        {
            public string BusinessId => "pierce-bakery-1";
            public int BalanceCents => 999999;
            public string TryDebit(int amountCents, string reason) => null;
            public string Credit(int amountCents, string reason) => "test port refuses credit";
        }

        private sealed class Slice
        {
            public List<string> Diag = new List<string>();
            public BakeryShopRuntime Bakery = new BakeryShopRuntime("pierce-bakery-1");
            public BusinessRuntimeState BakeryRuntime = new BusinessRuntimeState();
            public BusinessRuntimeState StoreRuntime = new BusinessRuntimeState();
            public RuntimeStorePort StorePort;
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

        private static Slice BakeAndFund(int storeCash, int bakeryCash, int bakeDay)
        {
            var s = new Slice();
            s.StoreRuntime.BindBusinessIdentity("general-store-1");
            s.StoreRuntime.AddCashCents(storeCash);
            s.StoreRuntime.EnsureCategoryStock("staple_food", 0, 1000);
            s.BakeryRuntime.BindBusinessIdentity("pierce-bakery-1");
            s.BakeryRuntime.AddCashCents(bakeryCash);
            s.StorePort = new RuntimeStorePort { Runtime = s.StoreRuntime };
            s.StorePort.Prices["staple_food"] = 5;
            s.StorePort.OfferList.Add(new GeneralStoreCategoryOffer
            {
                CategoryId = "staple_food",
                OfferedItemIds = new List<string> { "bread" },
                StoreUnitsPerItemUnit = 1,
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
            Assert.IsNotNull(driver.PlanBatches(BakeryBreadCatalog.BreadLoafId, 1, bakeDay, s.Diag));
            Assert.AreEqual(1, driver.RunScheduledDay(
                bakeDay, true, WorkstationCatalog.BakeOven, OvenFinder(), null, s.Diag, s.Ids),
                "one batch should bake; diag: " + string.Join("; ", s.Diag));
            Assert.AreEqual(24, s.Bakery.BreadShelf[0].UnitCount, "one batch yields 24 loaves");
            return s;
        }

        private static Review2WholesaleTransferOptions TransferOptions(
            Slice s, int units, int wholesale, int retail, int transferDay,
            IBusinessCashPort storePort, IBusinessCashPort bakeryPort)
        {
            return new Review2WholesaleTransferOptions
            {
                Bakery = s.Bakery,
                BakeryProductId = BakeryBreadCatalog.BreadLoafId,
                Units = units,
                WholesalePriceCents = wholesale,
                RetailPriceCents = retail,
                TransferDayIndex = transferDay,
                Intake = s.Intake,
                StoreCatalog = s.Catalog,
                StoreCashPort = storePort,
                BakeryCashPort = bakeryPort,
                StoreBusinessId = "general-store-1",
                BakeryBusinessId = "pierce-bakery-1",
            };
        }

        // Production settlement requires the adapter to prove which Business owns each balance.
        private static IBusinessIdentityCashPort ProductionPort(BusinessRuntimeState runtime)
        {
            return new Series2BusinessRuntimeCashPort(runtime);
        }

        [Test]
        public void TransferAndStock_ReconcilesExactly()
        {
            Slice s = BakeAndFund(5000, 2000, 2);
            Review2WholesaleTransferResult transfer = Review2BakeryToStoreWholesaleDriver.TransferToShelf(
                TransferOptions(s, 24, 2, 5, 2, ProductionPort(s.StoreRuntime), ProductionPort(s.BakeryRuntime)), s.Diag);
            Assert.IsTrue(transfer.Transferred, transfer.Refusal + "; diag: " + string.Join("; ", s.Diag));
            Assert.IsTrue(transfer.Settled);
            Assert.IsTrue(transfer.OnShelf);
            Assert.AreEqual("bread", transfer.StoreProductId, "the map translates bake.bread-loaf to store bread, loaf to loaf");
            Assert.AreEqual("Fresh", transfer.Condition, "baked day 2, transferred day 2");
            Assert.AreEqual(48, transfer.AmountCents, "24 loaves x 2c wholesale");
            Assert.AreEqual("gs.move-back-to-shelf", transfer.TaskActionId, "the shelf move must be named as the Pass 2 task");
            Assert.AreEqual(2, transfer.TaskMinutesPerLot);
            Assert.AreEqual(0, s.Bakery.BreadShelf[0].UnitCount, "the bakery shelf lot is fully taken by the wholesale");
            Assert.AreEqual(4952, s.StoreRuntime.CurrentCashCents, "store pays exactly the wholesale amount");
            Assert.AreEqual(2048, s.BakeryRuntime.CurrentCashCents, "bakery gains exactly the wholesale amount");
            Pass3ProductLine line = s.Catalog.Find("bread");
            Assert.IsNotNull(line);
            Assert.AreEqual(0, line.BackUnits, "the shelf move empties BACK");
            Assert.AreEqual(24, line.ShelfUnits, "all 24 loaves sit on the Pass 3 shelf");
            Assert.AreEqual(5, line.PriceCents, "the retail decision, not the wholesale cost, prices the shelf line");
            Assert.AreEqual(0, s.StorePort.CategoryStockUnits("staple_food"), "transfer alone does not touch the live pool");

            Review2StockingResult stocked = Review2BakeryToStoreWholesaleDriver.StockShelfToLive(
                s.Catalog, "bread", 24, "staple_food", s.StorePort, s.Diag);
            Assert.IsTrue(stocked.Stocked, stocked.Refusal);
            Assert.AreEqual(0, line.ShelfUnits, "stocking consumes the Pass 3 shelf exactly once");
            Assert.AreEqual(24, stocked.LivePoolUnitsAfter, "the live pool gains exactly the stocked units");
            Assert.AreEqual(24, s.StorePort.CategoryStockUnits("staple_food"));
            Assert.AreEqual(4952, s.StoreRuntime.CurrentCashCents, "stocking moves stock, never cash");
        }

        [Test]
        public void Transfer_StaleLot_Refuses_NothingMoves()
        {
            Slice s = BakeAndFund(5000, 2000, 2);
            Review2WholesaleTransferResult result = Review2BakeryToStoreWholesaleDriver.TransferToShelf(
                TransferOptions(s, 24, 2, 5, 4, ProductionPort(s.StoreRuntime), ProductionPort(s.BakeryRuntime)), s.Diag);
            Assert.IsTrue(!result.Transferred, "a lot 2 days past baked day is stale and must be refused");
            Assert.IsTrue(!result.IntakeCompleted);
            Assert.AreEqual(24, s.Bakery.BreadShelf[0].UnitCount, "stale refusal leaves the bakery shelf untouched");
            Assert.AreEqual(5000, s.StoreRuntime.CurrentCashCents);
            Assert.AreEqual(2000, s.BakeryRuntime.CurrentCashCents);
            Assert.IsNull(s.Catalog.Find("bread"), "no catalog line is created by a refused transfer");
        }

        [Test]
        public void Transfer_UnmappedProduct_Refuses_NothingMoves()
        {
            Slice s = BakeAndFund(5000, 2000, 2);
            var options = TransferOptions(s, 24, 2, 5, 2, ProductionPort(s.StoreRuntime), ProductionPort(s.BakeryRuntime));
            options.BakeryProductId = "bake.rolls";
            Review2WholesaleTransferResult result = Review2BakeryToStoreWholesaleDriver.TransferToShelf(options, s.Diag);
            Assert.IsTrue(!result.Transferred);
            Assert.IsTrue(result.Refusal.Contains("no store translation"), "rolls are deliberately unmapped; the refusal must say so: " + result.Refusal);
            Assert.AreEqual(24, s.Bakery.BreadShelf[0].UnitCount);
            Assert.AreEqual(5000, s.StoreRuntime.CurrentCashCents);
            Assert.IsNull(s.Catalog.Find("bread"));
        }

        [Test]
        public void Transfer_RetailEqualsWholesale_Refuses_BeforeIntake()
        {
            Slice s = BakeAndFund(5000, 2000, 2);
            Review2WholesaleTransferResult result = Review2BakeryToStoreWholesaleDriver.TransferToShelf(
                TransferOptions(s, 24, 2, 2, 2, ProductionPort(s.StoreRuntime), ProductionPort(s.BakeryRuntime)), s.Diag);
            Assert.IsTrue(!result.Transferred);
            Assert.IsTrue(!result.IntakeCompleted, "the retail guard must fire before intake, not after cash moves");
            Assert.AreEqual(24, s.Bakery.BreadShelf[0].UnitCount);
            Assert.AreEqual(5000, s.StoreRuntime.CurrentCashCents);
            Assert.AreEqual(2000, s.BakeryRuntime.CurrentCashCents);
        }

        [Test]
        public void Transfer_InsufficientPayerCash_Refuses_BeforeIntake()
        {
            Slice s = BakeAndFund(10, 2000, 2);
            Review2WholesaleTransferResult result = Review2BakeryToStoreWholesaleDriver.TransferToShelf(
                TransferOptions(s, 24, 2, 5, 2, ProductionPort(s.StoreRuntime), ProductionPort(s.BakeryRuntime)), s.Diag);
            Assert.IsTrue(!result.Transferred);
            Assert.IsTrue(!result.IntakeCompleted, "a payer that cannot cover wholesale must be refused before intake");
            Assert.AreEqual(24, s.Bakery.BreadShelf[0].UnitCount);
            Assert.AreEqual(10, s.StoreRuntime.CurrentCashCents);
            Assert.IsNull(s.Catalog.Find("bread"));
        }

        [Test]
        public void Transfer_SettlementFailure_StandsInBack_ThenRecovers()
        {
            Slice s = BakeAndFund(5000, 2000, 2);
            Review2WholesaleTransferResult failed = Review2BakeryToStoreWholesaleDriver.TransferToShelf(
                TransferOptions(s, 24, 2, 5, 2, ProductionPort(s.StoreRuntime), new RefusingCreditPort()), s.Diag);
            Assert.IsTrue(!failed.Transferred);
            Assert.IsTrue(failed.IntakeCompleted, "intake happened before settlement failed");
            Assert.IsTrue(!failed.Settled);
            Assert.IsTrue(!failed.OnShelf, "no shelf or live movement on a settlement failure");
            Assert.AreEqual(0, s.Bakery.BreadShelf[0].UnitCount, "the bakery shelf was taken, and that taking is not undone");
            Assert.AreEqual(5000, s.StoreRuntime.CurrentCashCents, "no cash moved on a failed settlement");
            Assert.AreEqual(2000, s.BakeryRuntime.CurrentCashCents);
            Pass3ProductLine line = s.Catalog.Find("bread");
            Assert.IsNotNull(line);
            Assert.AreEqual(24, line.BackUnits, "the bread stands in BACK, unsettled, not lost");
            Assert.AreEqual(0, line.ShelfUnits);
            Assert.IsNotNull(failed.Settlement);
            Assert.IsTrue(!failed.Settlement.Settled);

            // Recovery, without re-intake (the duplicate guard would
            // refuse it): settle the returned record, then move.
            Series2WholesaleSettlementResult settled = Series2WholesaleSettlement.SettleCash(
                failed.Settlement, ProductionPort(s.StoreRuntime), ProductionPort(s.BakeryRuntime), s.Diag);
            Assert.IsTrue(settled.Settled, settled.Refusal);
            Assert.AreEqual(4952, s.StoreRuntime.CurrentCashCents);
            Assert.AreEqual(2048, s.BakeryRuntime.CurrentCashCents);
            Review2WholesaleTransferResult moved = Review2BakeryToStoreWholesaleDriver.MoveSettledLotToShelf(
                s.Catalog, "bread", 24, 5, s.Diag);
            Assert.IsTrue(moved.OnShelf, moved.Refusal);
            Assert.AreEqual(0, line.BackUnits);
            Assert.AreEqual(24, line.ShelfUnits);
            Assert.AreEqual("gs.move-back-to-shelf", moved.TaskActionId);
        }

        [Test]
        public void StockShelfToLive_CappedPort_Refuses_ThenRetrySucceeds()
        {
            Slice s = BakeAndFund(5000, 2000, 2);
            Review2WholesaleTransferResult transfer = Review2BakeryToStoreWholesaleDriver.TransferToShelf(
                TransferOptions(s, 24, 2, 5, 2, ProductionPort(s.StoreRuntime), ProductionPort(s.BakeryRuntime)), s.Diag);
            Assert.IsTrue(transfer.Transferred, transfer.Refusal);
            s.StorePort.Cap = 10;
            Review2StockingResult refused = Review2BakeryToStoreWholesaleDriver.StockShelfToLive(
                s.Catalog, "bread", 24, "staple_food", s.StorePort, s.Diag);
            Assert.IsTrue(!refused.Stocked, "a live pool that can receive only 10u must refuse 24u, never partial");
            Assert.AreEqual(24, s.Catalog.Find("bread").ShelfUnits, "refused stocking leaves the Pass 3 shelf intact");
            Assert.AreEqual(0, s.StorePort.CategoryStockUnits("staple_food"));
            s.StorePort.Cap = int.MaxValue;
            Review2StockingResult stocked = Review2BakeryToStoreWholesaleDriver.StockShelfToLive(
                s.Catalog, "bread", 24, "staple_food", s.StorePort, s.Diag);
            Assert.IsTrue(stocked.Stocked, stocked.Refusal);
            Assert.AreEqual(0, s.Catalog.Find("bread").ShelfUnits);
            Assert.AreEqual(24, s.StorePort.CategoryStockUnits("staple_food"));
        }

        [Test]
        public void ConsumeShelfUnits_Refusals_DoNotMoveStock()
        {
            var line = new Pass3ProductLine { ProductId = "bread", ShelfUnits = 5, BackUnits = 0 };
            var diag = new List<string>();
            Assert.IsTrue(!line.ConsumeShelfUnits(6, diag), "short shelf must refuse");
            Assert.AreEqual(5, line.ShelfUnits);
            Assert.IsTrue(!line.ConsumeShelfUnits(0, diag), "zero must refuse");
            Assert.AreEqual(5, line.ShelfUnits);
            Assert.IsTrue(line.ConsumeShelfUnits(5, diag), "an exact consume must succeed");
            Assert.AreEqual(0, line.ShelfUnits);
        }

        [Test]
        public void Transfer_NoSellableLot_Refuses_NothingMoves()
        {
            Slice s = BakeAndFund(5000, 2000, 2);
            Review2WholesaleTransferResult first = Review2BakeryToStoreWholesaleDriver.TransferToShelf(
                TransferOptions(s, 24, 2, 5, 2, ProductionPort(s.StoreRuntime), ProductionPort(s.BakeryRuntime)), s.Diag);
            Assert.IsTrue(first.Transferred, first.Refusal);
            Assert.AreEqual(0, s.Bakery.BreadShelf[0].UnitCount, "the first transfer empties the lot");
            Review2WholesaleTransferResult second = Review2BakeryToStoreWholesaleDriver.TransferToShelf(
                TransferOptions(s, 24, 2, 5, 2, ProductionPort(s.StoreRuntime), ProductionPort(s.BakeryRuntime)), s.Diag);
            Assert.IsTrue(!second.Transferred);
            Assert.IsTrue(second.Refusal.Contains("no sellable shelf lot"), "an emptied lot cannot be transferred twice: " + second.Refusal);
            Assert.IsTrue(!second.IntakeCompleted);
            Assert.AreEqual(4952, s.StoreRuntime.CurrentCashCents, "the refused second transfer moves no cash");
            Assert.AreEqual(2048, s.BakeryRuntime.CurrentCashCents);
            Pass3ProductLine line = s.Catalog.Find("bread");
            Assert.IsNotNull(line);
            Assert.AreEqual(0, line.BackUnits, "the catalog line is unchanged by the refused second transfer");
            Assert.AreEqual(24, line.ShelfUnits);
        }

        [Test]
        public void Transfer_ShortLot_Refuses_NothingMoves()
        {
            Slice s = BakeAndFund(5000, 2000, 2);
            Review2WholesaleTransferResult result = Review2BakeryToStoreWholesaleDriver.TransferToShelf(
                TransferOptions(s, 25, 2, 5, 2, ProductionPort(s.StoreRuntime), ProductionPort(s.BakeryRuntime)), s.Diag);
            Assert.IsTrue(!result.Transferred);
            Assert.IsTrue(result.Refusal.Contains("holds 24u, asked 25u"), "the driver never partials a short lot: " + result.Refusal);
            Assert.IsTrue(!result.IntakeCompleted);
            Assert.AreEqual(24, s.Bakery.BreadShelf[0].UnitCount, "short-lot refusal leaves the bakery shelf untouched");
            Assert.AreEqual(5000, s.StoreRuntime.CurrentCashCents);
            Assert.AreEqual(2000, s.BakeryRuntime.CurrentCashCents);
            Assert.IsNull(s.Catalog.Find("bread"), "no catalog line is created by a refused transfer");
        }

        [Test]
        public void Transfer_BeforeBakedDay_Refuses_NothingMoves()
        {
            Slice s = BakeAndFund(5000, 2000, 2);
            Review2WholesaleTransferResult result = Review2BakeryToStoreWholesaleDriver.TransferToShelf(
                TransferOptions(s, 24, 2, 5, 1, ProductionPort(s.StoreRuntime), ProductionPort(s.BakeryRuntime)), s.Diag);
            Assert.IsTrue(!result.Transferred);
            Assert.IsTrue(result.Refusal.Contains("before baked day"), "transfer day 1 precedes baked day 2: " + result.Refusal);
            Assert.IsTrue(!result.IntakeCompleted);
            Assert.AreEqual(24, s.Bakery.BreadShelf[0].UnitCount, "the refusal leaves the bakery shelf untouched");
            Assert.AreEqual(5000, s.StoreRuntime.CurrentCashCents);
            Assert.AreEqual(2000, s.BakeryRuntime.CurrentCashCents);
            Assert.IsNull(s.Catalog.Find("bread"), "no catalog line is created by a refused transfer");
        }

        [Test]
        public void Transfer_RetailZero_Refuses_BeforeIntake()
        {
            Slice s = BakeAndFund(5000, 2000, 2);
            Review2WholesaleTransferResult result = Review2BakeryToStoreWholesaleDriver.TransferToShelf(
                TransferOptions(s, 24, 2, 0, 2, ProductionPort(s.StoreRuntime), ProductionPort(s.BakeryRuntime)), s.Diag);
            Assert.IsTrue(!result.Transferred);
            Assert.IsTrue(!result.IntakeCompleted, "the retail guard must fire before intake, not after cash moves");
            Assert.AreEqual(24, s.Bakery.BreadShelf[0].UnitCount);
            Assert.AreEqual(5000, s.StoreRuntime.CurrentCashCents);
            Assert.AreEqual(2000, s.BakeryRuntime.CurrentCashCents);
            Assert.IsNull(s.Catalog.Find("bread"), "no catalog line is created by a refused transfer");
        }

        [Test]
        public void Transfer_DuplicateIntake_RestoresBakeryShelf()
        {
            Slice s = BakeAndFund(5000, 2000, 2);
            Review2WholesaleTransferResult first = Review2BakeryToStoreWholesaleDriver.TransferToShelf(
                TransferOptions(s, 10, 2, 5, 2, ProductionPort(s.StoreRuntime), ProductionPort(s.BakeryRuntime)), s.Diag);
            Assert.IsTrue(first.Transferred, first.Refusal);
            Assert.AreEqual(4980, s.StoreRuntime.CurrentCashCents, "store pays 10 x 2c");
            Assert.AreEqual(2020, s.BakeryRuntime.CurrentCashCents, "bakery gains 10 x 2c");
            Assert.AreEqual(14, s.Bakery.BreadShelf[0].UnitCount, "14 loaves remain in the lot");
            Pass3ProductLine line = s.Catalog.Find("bread");
            Assert.IsNotNull(line);
            Assert.AreEqual(0, line.BackUnits);
            Assert.AreEqual(10, line.ShelfUnits);
            Review2WholesaleTransferResult second = Review2BakeryToStoreWholesaleDriver.TransferToShelf(
                TransferOptions(s, 14, 2, 5, 2, ProductionPort(s.StoreRuntime), ProductionPort(s.BakeryRuntime)), s.Diag);
            Assert.IsTrue(!second.Transferred);
            Assert.IsTrue(!second.IntakeCompleted, "the duplicate intake is refused before settlement");
            Assert.IsTrue(second.Refusal.Contains("wholesale intake refused"), "the duplicate-lot guard refuses the second intake: " + second.Refusal);
            Assert.AreEqual(14, s.Bakery.BreadShelf[0].UnitCount, "the intake refusal restores exactly the units taken");
            Assert.AreEqual(4980, s.StoreRuntime.CurrentCashCents, "the refused second transfer settles nothing");
            Assert.AreEqual(2020, s.BakeryRuntime.CurrentCashCents);
            Assert.AreEqual(0, line.BackUnits, "the catalog line is unchanged by the refused second transfer");
            Assert.AreEqual(10, line.ShelfUnits);
        }
    }
}
