using System.Collections.Generic;
using NUnit.Framework;
using LandLedgers.Economy;
using LandLedgers.Economy.BreadDrivers;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Primitives;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Plan 02 (2026-11-06) slice 2A: whole-sack B2B flour purchase from the
    /// General Store's identified retail lots at the live retail price.
    /// Fixture mirrors BakeryToStoreWholesaleTransferTests.BakeAndFund (real
    /// BakeryShopRuntime, real BusinessRuntimeStates, store seeded with an
    /// identified retail flour lot of 4 sacks via TryAddRetailItemLot the way
    /// ScenarioOpeningStockEndowment does it; bakery cash 2,000c).
    /// Unity compilation, Test Runner, PlayMode, and the forwarder call site
    /// are NOT claimed; SimulationDrivers is harness-excluded, so the
    /// forwarder is source-reviewed only.
    /// </summary>
    [TestFixture]
    public sealed class BakeryFlourLocalPurchaseTests
    {
        private const string BakeryId = "pierce-bakery-1";
        private const string StoreId = "general-store-1";
        private const string StoreFlourLotId = "opening-endowment:dev:general-store:seed:flour";

        private sealed class Slice
        {
            public List<string> Diag = new List<string>();
            public BakeryShopRuntime Bakery = new BakeryShopRuntime(BakeryId);
            public BusinessRuntimeState BakeryRuntime = new BusinessRuntimeState();
            public BusinessRuntimeState StoreRuntime = new BusinessRuntimeState();
            public EntityIdRegistry Ids = new EntityIdRegistry();
        }

        private static Slice NewSlice(int storeCash = 5000, int bakeryCash = 2000, int storeSacks = 4)
        {
            var s = new Slice();
            s.StoreRuntime.BindBusinessIdentity(StoreId);
            s.StoreRuntime.AddCashCents(storeCash);
            s.StoreRuntime.EnsureCategoryStock("staple_food", 0, 1000);
            s.BakeryRuntime.BindBusinessIdentity(BakeryId);
            s.BakeryRuntime.AddCashCents(bakeryCash);
            if (storeSacks > 0)
            {
                Assert.IsNull(s.StoreRuntime.TryAddRetailItemLot(
                    StoreFlourLotId, "flour", "staple_food", "10 lb sack",
                    storeSacks, StoreId, StoreId,
                    "DevelopmentTownConfiguration opening inventory, general-store flour", 0),
                    "store flour lot seeding should succeed");
            }
            return s;
        }

        private static BakeryFlourLocalPurchaseInputs InputsFor(Slice s, int sacks, int priceCents, string transactionId) =>
            new BakeryFlourLocalPurchaseInputs
            {
                StoreRuntime = s.StoreRuntime,
                BakeryRuntime = s.BakeryRuntime,
                Bakery = s.Bakery,
                BakeryCashPort = new Series2BusinessRuntimeCashPort(s.BakeryRuntime),
                StoreCashPort = new Series2BusinessRuntimeCashPort(s.StoreRuntime),
                IdRegistry = s.Ids,
                BakeryBusinessId = BakeryId,
                UnitPriceCents = priceCents,
                SacksRequested = sacks,
                DayIndex = 2,
                TransactionId = transactionId,
            };

        [Test]
        public void SuccessfulPurchase_MovesCashStockAndBinExactlyOnce()
        {
            Slice s = NewSlice();
            BakeryFlourLocalPurchaseResult result =
                BakeryFlourLocalPurchase.PurchaseSacks(InputsFor(s, 2, 29, "bakery-flour-purchase:pierce-bakery-1:2:1"), s.Diag);

            Assert.IsTrue(result.Succeeded, "purchase should succeed; refusal: " + result.Refusal);
            Assert.IsTrue(!result.Duplicate);
            Assert.AreEqual(2, result.SacksPurchased);
            Assert.AreEqual(20, result.PoundsReceived);
            Assert.AreEqual(58, result.TotalCostCents);
            Assert.AreEqual(1942, s.BakeryRuntime.CurrentCashCents, "bakery cash 2,000 -> 1,942c");
            Assert.AreEqual(5058, s.StoreRuntime.CurrentCashCents, "store cash +58c, settlement recorded once");
            Assert.AreEqual(2, s.StoreRuntime.GetRetailItemStockUnits("flour", "staple_food"), "store retail flour 4 -> 2 sacks");
            Assert.AreEqual(20, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId), "bin 0 -> 20 lb");
            Assert.AreEqual(1, s.Bakery.FlourStock.Lots.Count, "exactly one bin lot");
            BakeryFlourLot lot = s.Bakery.FlourStock.Lots[0];
            Assert.AreEqual("bakery-flour-purchase:pierce-bakery-1:2:1", lot.ImportOrderId);
            Assert.AreEqual("General Store general-store-1", lot.OriginName);
            Assert.AreEqual(result.BakeryFlourLotId, lot.LotId.ToString());
            Assert.IsTrue(lot.SupplierNote.Contains(StoreFlourLotId),
                "supplier note names the source lot; note: " + lot.SupplierNote);
            Assert.IsTrue(lot.ProvenanceChain().Contains(StoreFlourLotId),
                "provenance chain names the source lot: " + lot.ProvenanceChain());
        }

        [Test]
        public void StoreShortfallRefuses_NothingMoves()
        {
            Slice s = NewSlice();
            BakeryFlourLocalPurchaseResult result =
                BakeryFlourLocalPurchase.PurchaseSacks(InputsFor(s, 5, 29, "bakery-flour-purchase:pierce-bakery-1:2:1"), s.Diag);

            Assert.IsTrue(!result.Succeeded);
            Assert.IsTrue(result.Refusal.Contains("shortfall") || result.Refusal.Contains("prove"),
                "refusal names the shortfall; refusal: " + result.Refusal);
            Assert.AreEqual(2000, s.BakeryRuntime.CurrentCashCents, "bakery cash unchanged");
            Assert.AreEqual(5000, s.StoreRuntime.CurrentCashCents, "store cash unchanged");
            Assert.AreEqual(4, s.StoreRuntime.GetRetailItemStockUnits("flour", "staple_food"), "store lots untouched");
            Assert.AreEqual(0, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId), "bin empty");
            Assert.AreEqual(0, s.Bakery.FlourStock.Lots.Count);
        }

        [Test]
        public void BakeryCashShortfallRefuses_NothingMoves()
        {
            Slice s = NewSlice(bakeryCash: 10);
            BakeryFlourLocalPurchaseResult result =
                BakeryFlourLocalPurchase.PurchaseSacks(InputsFor(s, 2, 29, "bakery-flour-purchase:pierce-bakery-1:2:1"), s.Diag);

            Assert.IsTrue(!result.Succeeded);
            Assert.IsTrue(result.Refusal.Contains("payment refused") || result.Refusal.Contains("cash"),
                "refusal: " + result.Refusal);
            Assert.AreEqual(10, s.BakeryRuntime.CurrentCashCents, "bakery cash unchanged");
            Assert.AreEqual(5000, s.StoreRuntime.CurrentCashCents, "store cash unchanged");
            Assert.AreEqual(4, s.StoreRuntime.GetRetailItemStockUnits("flour", "staple_food"), "store lots untouched");
            Assert.AreEqual(0, s.Bakery.FlourStock.Lots.Count, "bin empty");
        }

        [Test]
        public void ZeroPriceRefuses_NoFreeFlour()
        {
            Slice s = NewSlice();
            BakeryFlourLocalPurchaseResult result =
                BakeryFlourLocalPurchase.PurchaseSacks(InputsFor(s, 2, 0, "bakery-flour-purchase:pierce-bakery-1:2:1"), s.Diag);

            Assert.IsTrue(!result.Succeeded);
            Assert.IsTrue(result.Refusal.Contains("positive unit price") || result.Refusal.Contains("free flour"),
                "refusal: " + result.Refusal);
            Assert.AreEqual(2000, s.BakeryRuntime.CurrentCashCents, "bakery cash unchanged");
            Assert.AreEqual(5000, s.StoreRuntime.CurrentCashCents, "store cash unchanged");
            Assert.AreEqual(4, s.StoreRuntime.GetRetailItemStockUnits("flour", "staple_food"), "store lots untouched");
            Assert.AreEqual(0, s.Bakery.FlourStock.Lots.Count, "bin empty");
        }

        [Test]
        public void DuplicateTransactionId_IsIdempotent()
        {
            Slice s = NewSlice();
            var inputs = InputsFor(s, 2, 29, "bakery-flour-purchase:pierce-bakery-1:2:1");
            BakeryFlourLocalPurchaseResult first = BakeryFlourLocalPurchase.PurchaseSacks(inputs, s.Diag);
            Assert.IsTrue(first.Succeeded);

            BakeryFlourLocalPurchaseResult second = BakeryFlourLocalPurchase.PurchaseSacks(inputs, s.Diag);
            Assert.IsTrue(second.Succeeded, "duplicate returns success; refusal: " + second.Refusal);
            Assert.IsTrue(second.Duplicate);
            Assert.AreEqual(0, second.SacksPurchased, "second call buys 0 new sacks");
            Assert.AreEqual(0, second.PoundsReceived);
            Assert.AreEqual(1, s.Bakery.FlourStock.Lots.Count, "still exactly one bin lot");
            Assert.AreEqual(1942, s.BakeryRuntime.CurrentCashCents, "cash moved once");
            Assert.AreEqual(5058, s.StoreRuntime.CurrentCashCents, "cash moved once");
            Assert.AreEqual(2, s.StoreRuntime.GetRetailItemStockUnits("flour", "staple_food"));
        }

        [Test]
        public void SackToPoundConversion_IsTenPounds()
        {
            Slice s = NewSlice();
            BakeryFlourLocalPurchaseResult result =
                BakeryFlourLocalPurchase.PurchaseSacks(InputsFor(s, 2, 29, "bakery-flour-purchase:pierce-bakery-1:2:1"), s.Diag);

            Assert.IsTrue(result.Succeeded, "refusal: " + result.Refusal);
            Assert.AreEqual(20, result.PoundsReceived, "2 sacks -> exactly 20 lb (never 2 lb, never 24 lb)");
            Assert.AreEqual(20, s.Bakery.FlourStock.Lots[0].Units);
            Assert.IsTrue(s.Bakery.FlourStock.Lots[0].SupplierNote.Contains("10 lb sack"),
                "provenance names the 10-lb sack basis; note: " + s.Bakery.FlourStock.Lots[0].SupplierNote);
        }
    }
}
