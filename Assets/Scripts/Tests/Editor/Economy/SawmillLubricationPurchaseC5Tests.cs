using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Economy.Equipment;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// C-5 (expanded campaign, Phase 1 forestry): the mill buys lamp oil
    /// from the General Store's identified retail lots at the store's live
    /// selling price, for its lubrication store (1 quart = 1 unit). Fixture
    /// mirrors BakeryFlourLocalPurchaseTests (real BusinessRuntimeStates,
    /// store seeded with an identified retail lamp-oil lot via
    /// TryAddRetailItemLot the way ScenarioOpeningStockEndowment does it;
    /// mill cash 15,000c, the sawmill's authored opening cash). Unity
    /// compilation, Test Runner, PlayMode, and the driver call site are NOT
    /// claimed; SimulationDrivers is harness-excluded, so the restock
    /// trigger is source-reviewed only and exercised here through the same
    /// public API it calls.
    /// </summary>
    [TestFixture]
    public sealed class SawmillLubricationPurchaseC5Tests
    {
        private const string MillId = "test-sawmill-1";
        private const string StoreId = "general-store-1";
        private const string StoreLampOilLotId = "opening-endowment:dev:general-store:seed:lamp-oil";

        private sealed class Slice
        {
            public List<string> Diag = new List<string>();
            public SawmillShopRuntime Mill = new SawmillShopRuntime(MillId, new EntityIdRegistry());
            public SupportContext MillSupport = new SupportContext();
            public BusinessRuntimeState MillRuntime = new BusinessRuntimeState();
            public BusinessRuntimeState StoreRuntime = new BusinessRuntimeState();
        }

        private static Slice NewSlice(int storeCash = 5000, int millCash = 15000, int storeQuarts = 30, int millLubrication = 5)
        {
            var s = new Slice();
            s.MillRuntime.BindBusinessIdentity(MillId);
            s.MillRuntime.AddCashCents(millCash);
            s.StoreRuntime.BindBusinessIdentity(StoreId);
            s.StoreRuntime.AddCashCents(storeCash);
            s.StoreRuntime.EnsureCategoryStock("household_goods", 0, 1000);
            s.Mill.MillLubricationUnits = millLubrication;
            Assert.IsNull(SawmillLubrication.SyncSupportFromMill(s.Mill, s.MillSupport, s.Diag),
                "the test mill's two stores start in agreement");
            if (storeQuarts > 0)
            {
                Assert.IsNull(s.StoreRuntime.TryAddRetailItemLot(
                    StoreLampOilLotId, "lamp_oil", "household_goods", "quart",
                    storeQuarts, StoreId, StoreId,
                    "DevelopmentTownConfiguration opening inventory, general-store lamp oil", 0),
                    "store lamp oil lot seeding should succeed");
            }
            return s;
        }

        private static SawmillLubricationPurchaseInputs InputsFor(Slice s, int quarts, int priceCents, string transactionId) =>
            new SawmillLubricationPurchaseInputs
            {
                StoreRuntime = s.StoreRuntime,
                MillRuntime = s.Mill,
                MillSupport = s.MillSupport,
                MillCashPort = new Series2BusinessRuntimeCashPort(s.MillRuntime),
                StoreCashPort = new Series2BusinessRuntimeCashPort(s.StoreRuntime),
                MillBusinessId = MillId,
                StoreBusinessId = StoreId,
                UnitPriceCents = priceCents,
                QuartsRequested = quarts,
                DayIndex = 2,
                TransactionId = transactionId,
            };

        [Test]
        public void SuccessfulPurchase_MovesCashStockAndStoreExactlyOnce()
        {
            Slice s = NewSlice();
            SawmillLubricationPurchaseResult result =
                SawmillLubricationPurchase.PurchaseQuarts(InputsFor(s, 25, 20, "mill-lubrication-purchase:test-sawmill-1:2:1"), s.Diag);

            Assert.IsTrue(result.Succeeded, "purchase should succeed; refusal: " + result.Refusal);
            Assert.IsTrue(!result.Duplicate);
            Assert.AreEqual(25, result.QuartsPurchased);
            Assert.AreEqual(500, result.TotalCostCents, "25 quarts x 20c (the store's live lamp-oil price)");
            Assert.AreEqual(14500, s.MillRuntime.CurrentCashCents, "mill cash 15,000 -> 14,500c");
            Assert.AreEqual(5500, s.StoreRuntime.CurrentCashCents, "store cash +500c, settlement recorded once");
            Assert.AreEqual(5, s.StoreRuntime.GetRetailItemStockUnits("lamp_oil", "household_goods"), "store retail lamp oil 30 -> 5 quarts");
            Assert.AreEqual(30, s.Mill.MillLubricationUnits, "mill lubrication store 5 -> 30 (the reorder target)");
            Assert.AreEqual(30, s.MillSupport.ConsumableUnits[SawmillLubrication.LubricationKey], "the gate's key moves with the runtime field");
            Assert.AreEqual(1, result.SourceLotIds.Count);
            Assert.AreEqual(StoreLampOilLotId, result.SourceLotIds[0], "the source lot is named");
            Assert.AreEqual(1, s.Mill.LubricationPurchaseTransactionIds.Count, "the transaction id is recorded once");
            Assert.AreEqual("mill-lubrication-purchase:test-sawmill-1:2:1", s.Mill.LubricationPurchaseTransactionIds[0]);
        }

        [Test]
        public void StoreShortfallRefuses_NothingMoves()
        {
            Slice s = NewSlice();
            SawmillLubricationPurchaseResult result =
                SawmillLubricationPurchase.PurchaseQuarts(InputsFor(s, 31, 20, "mill-lubrication-purchase:test-sawmill-1:2:1"), s.Diag);

            Assert.IsTrue(!result.Succeeded);
            Assert.IsTrue(result.Refusal.Contains("shortfall") || result.Refusal.Contains("prove"),
                "refusal names the shortfall; refusal: " + result.Refusal);
            Assert.AreEqual(15000, s.MillRuntime.CurrentCashCents, "mill cash unchanged");
            Assert.AreEqual(5000, s.StoreRuntime.CurrentCashCents, "store cash unchanged");
            Assert.AreEqual(30, s.StoreRuntime.GetRetailItemStockUnits("lamp_oil", "household_goods"), "store lots untouched");
            Assert.AreEqual(5, s.Mill.MillLubricationUnits, "mill store unchanged");
            Assert.AreEqual(0, s.Mill.LubricationPurchaseTransactionIds.Count, "no transaction recorded");
        }

        [Test]
        public void MillCashShortfallRefuses_NothingMoves()
        {
            Slice s = NewSlice(millCash: 100);
            SawmillLubricationPurchaseResult result =
                SawmillLubricationPurchase.PurchaseQuarts(InputsFor(s, 25, 20, "mill-lubrication-purchase:test-sawmill-1:2:1"), s.Diag);

            Assert.IsTrue(!result.Succeeded);
            Assert.IsTrue(result.Refusal.Contains("payment refused") || result.Refusal.Contains("cash"),
                "refusal: " + result.Refusal);
            Assert.AreEqual(100, s.MillRuntime.CurrentCashCents, "mill cash unchanged");
            Assert.AreEqual(5000, s.StoreRuntime.CurrentCashCents, "store cash unchanged");
            Assert.AreEqual(30, s.StoreRuntime.GetRetailItemStockUnits("lamp_oil", "household_goods"), "store lots untouched");
            Assert.AreEqual(5, s.Mill.MillLubricationUnits, "mill store unchanged");
        }

        [Test]
        public void ZeroPriceRefuses_NoFreeLampOil()
        {
            Slice s = NewSlice();
            SawmillLubricationPurchaseResult result =
                SawmillLubricationPurchase.PurchaseQuarts(InputsFor(s, 25, 0, "mill-lubrication-purchase:test-sawmill-1:2:1"), s.Diag);

            Assert.IsTrue(!result.Succeeded);
            Assert.IsTrue(result.Refusal.Contains("positive unit price") || result.Refusal.Contains("free lamp oil"),
                "refusal: " + result.Refusal);
            Assert.AreEqual(15000, s.MillRuntime.CurrentCashCents, "mill cash unchanged");
            Assert.AreEqual(5000, s.StoreRuntime.CurrentCashCents, "store cash unchanged");
            Assert.AreEqual(30, s.StoreRuntime.GetRetailItemStockUnits("lamp_oil", "household_goods"), "store lots untouched");
            Assert.AreEqual(5, s.Mill.MillLubricationUnits, "mill store unchanged");
        }

        [Test]
        public void DuplicateTransactionId_IsIdempotent()
        {
            Slice s = NewSlice();
            const string transactionId = "mill-lubrication-purchase:test-sawmill-1:2:1";

            SawmillLubricationPurchaseResult first =
                SawmillLubricationPurchase.PurchaseQuarts(InputsFor(s, 25, 20, transactionId), s.Diag);
            Assert.IsTrue(first.Succeeded);
            Assert.IsTrue(!first.Duplicate);

            SawmillLubricationPurchaseResult second =
                SawmillLubricationPurchase.PurchaseQuarts(InputsFor(s, 25, 20, transactionId), s.Diag);
            Assert.IsTrue(second.Succeeded, "a duplicate is a success, not a failure");
            Assert.IsTrue(second.Duplicate, "the second call reports the duplicate");
            Assert.AreEqual(0, second.QuartsPurchased, "the duplicate buys nothing twice");
            Assert.AreEqual(14500, s.MillRuntime.CurrentCashCents, "mill cash moved once");
            Assert.AreEqual(5500, s.StoreRuntime.CurrentCashCents, "store cash moved once");
            Assert.AreEqual(5, s.StoreRuntime.GetRetailItemStockUnits("lamp_oil", "household_goods"), "store lots consumed once");
            Assert.AreEqual(30, s.Mill.MillLubricationUnits, "mill store filled once");
            Assert.AreEqual(1, s.Mill.LubricationPurchaseTransactionIds.Count, "one transaction record");
        }

        [Test]
        public void DisagreeingStoresRefuse_BeforeCashMoves()
        {
            Slice s = NewSlice();
            s.MillSupport.ConsumableUnits[SawmillLubrication.LubricationKey] = 12;

            SawmillLubricationPurchaseResult result =
                SawmillLubricationPurchase.PurchaseQuarts(InputsFor(s, 25, 20, "mill-lubrication-purchase:test-sawmill-1:2:1"), s.Diag);

            Assert.IsTrue(!result.Succeeded);
            Assert.IsTrue(result.Refusal.Contains("disagree"), "refusal: " + result.Refusal);
            Assert.AreEqual(15000, s.MillRuntime.CurrentCashCents, "mill cash unchanged");
            Assert.AreEqual(5000, s.StoreRuntime.CurrentCashCents, "store cash unchanged");
            Assert.AreEqual(30, s.StoreRuntime.GetRetailItemStockUnits("lamp_oil", "household_goods"), "store lots untouched");
            Assert.AreEqual(5, s.Mill.MillLubricationUnits, "mill store unchanged");
        }

        [Test]
        public void RestockThresholdAndTopUpMath()
        {
            var mill = new SawmillShopRuntime(MillId, new EntityIdRegistry());

            mill.MillLubricationUnits = 5;
            Assert.IsTrue(SawmillLubrication.ShouldRestock(mill), "at the 5-unit threshold the driver restocks");
            Assert.AreEqual(25, SawmillLubrication.QuartsToRestock(mill), "5 -> 30 is a 25-quart top-up");

            mill.MillLubricationUnits = 6;
            Assert.IsFalse(SawmillLubrication.ShouldRestock(mill), "6 units is above the threshold");
            Assert.AreEqual(24, SawmillLubrication.QuartsToRestock(mill), "the top-up math still holds above the threshold");

            mill.MillLubricationUnits = 0;
            Assert.IsTrue(SawmillLubrication.ShouldRestock(mill), "an empty store restocks");
            Assert.AreEqual(30, SawmillLubrication.QuartsToRestock(mill), "0 -> 30 is a full 30-quart buy");

            mill.MillLubricationUnits = 30;
            Assert.IsFalse(SawmillLubrication.ShouldRestock(mill), "a full store never restocks");
            Assert.AreEqual(0, SawmillLubrication.QuartsToRestock(mill), "no quarts at target");

            Assert.IsFalse(SawmillLubrication.ShouldRestock(null), "a null mill never restocks");
            Assert.AreEqual(0, SawmillLubrication.QuartsToRestock(null));
        }

        [Test]
        public void MismatchedCashPortIdentitiesRefuse()
        {
            Slice s = NewSlice();
            SawmillLubricationPurchaseInputs inputs = InputsFor(s, 25, 20, "mill-lubrication-purchase:test-sawmill-1:2:1");
            // The mill cash port is bound to the STORE's runtime state — the
            // port identities no longer match the named buyer and seller.
            inputs.MillCashPort = new Series2BusinessRuntimeCashPort(s.StoreRuntime);

            SawmillLubricationPurchaseResult result =
                SawmillLubricationPurchase.PurchaseQuarts(inputs, s.Diag);

            Assert.IsTrue(!result.Succeeded);
            Assert.IsTrue(result.Refusal.Contains("identities do not match"), "refusal: " + result.Refusal);
            Assert.AreEqual(15000, s.MillRuntime.CurrentCashCents, "mill cash unchanged");
            Assert.AreEqual(5000, s.StoreRuntime.CurrentCashCents, "store cash unchanged");
            Assert.AreEqual(30, s.StoreRuntime.GetRetailItemStockUnits("lamp_oil", "household_goods"), "store lots untouched");
        }
    }
}
