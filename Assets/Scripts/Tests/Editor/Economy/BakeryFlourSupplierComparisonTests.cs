using System.Collections.Generic;
using NUnit.Framework;
using LandLedgers.Economy;
using LandLedgers.Economy.BreadDrivers;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Plan 02 (2026-11-06) slice 2A: flour supplier comparison — ranked on
    /// actual price, quantity, distance, delivery cost and executability.
    /// Pure tests, no Unity objects. The store price is pinned by derivation
    /// in test 1: the authored definition values (wholesale 18c + import 2c
    /// = 20c landed, item markup 1, staple_food markup 1.45, band 28–45)
    /// through RetailPricingDefinition.GetCostPlusPriceCents give 29c — the
    /// same computation GeneralStoreRuntimeManager.GetItemSellingPriceCents
    /// performs (the manager itself is harness-excluded, so the live read is
    /// verified by this derivation, not assumed).
    /// Unity compilation, Test Runner, PlayMode, and the forwarder call site
    /// are NOT claimed; SimulationDrivers is harness-excluded, so the
    /// forwarder is source-reviewed only.
    /// </summary>
    [TestFixture]
    public sealed class BakeryFlourSupplierComparisonTests
    {
        private static BakeryFlourSupplierInputs BaseInputs() => new BakeryFlourSupplierInputs
        {
            SacksNeeded = 2,
            StoreSacksAvailable = 4,
            StorePricePerSackCents = 29,
            MillbrookSacksAvailable = 23,
            MillbrookPricePerSackCents = 48,
            MillbrookOneWayMiles = 50f,
            MillbrookFreightQuoteCents = 125,
            MillbrookFreightAvailable = false,
            MillbrookFreightUnavailableReason = "freight blocked: no Millbrook shipment has departed (staging/T49 — Obj 16–18)",
            ImportPricePerLbCents = 3,
            ImportTransitDays = 14,
            ImportOrderPathExists = false,
        };

        [Test]
        public void StoreCheapestExecutable_RecommendsStore_CostPerBatch58C()
        {
            // Pin the derivation first: 20c landed x 1 x 1.45 = 29c, inside [28,45].
            var pricing = new RetailPricingDefinition
            {
                baselineCents = 35,
                baseWholesaleCostCents = 18,
                importCostCents = 2,
                localMarketBand = new PriceBand { minCents = 28, maxCents = 45 },
                markupMultiplier = 1f,
                supplyDemandSensitivity = 0.35f,
            };
            Assert.AreEqual(20, pricing.GetLandedCostCents());
            Assert.AreEqual(29, pricing.GetCostPlusPriceCents(1.45f, 0f));

            var diag = new List<string>();
            BakeryFlourSupplierComparisonResult result =
                BakeryFlourSupplierComparison.Compare(BaseInputs(), diag);

            Assert.IsNotNull(result.Recommended, "a source must be recommended; diag: " + string.Join(" | ", diag));
            Assert.AreEqual("general-store", result.Recommended.SupplierId);
            Assert.AreEqual(58, result.Recommended.CostPerBatchCents, "2 sacks x 29c per 12-lb batch");
            Assert.AreEqual(58, result.Recommended.TotalCostForPurchaseCents, "2 sacks x 29c, no delivery");
            Assert.AreEqual(0, result.Recommended.DeliveryCostPerShipmentCents);
            Assert.AreEqual(3, result.RankedQuotes.Count);
            Assert.AreEqual("general-store", result.RankedQuotes[0].SupplierId, "executable sources rank first");
            Assert.IsTrue(string.IsNullOrEmpty(result.Refusal), "a recommendation means no refusal");
        }

        [Test]
        public void StoreOutOfStock_StoreUnavailable_MillbrookListedBlocked()
        {
            BakeryFlourSupplierInputs inputs = BaseInputs();
            inputs.StoreSacksAvailable = 0;

            BakeryFlourSupplierComparisonResult result =
                BakeryFlourSupplierComparison.Compare(inputs, new List<string>());

            Assert.IsNull(result.Recommended, "no executable source: store empty, Millbrook freight-blocked, import has no order path");
            Assert.IsTrue(!string.IsNullOrWhiteSpace(result.Refusal), "the refusal is named");
            FlourSupplierQuote store = result.RankedQuotes.Find(q => q.SupplierId == "general-store");
            Assert.IsNotNull(store);
            Assert.IsTrue(!store.Executable);
            Assert.IsTrue(store.Reason.Contains("0 sacks"), "reason names the stockout: " + store.Reason);
            FlourSupplierQuote millbrook = result.RankedQuotes.Find(q => q.SupplierId == "millbrook_grain_co");
            Assert.IsNotNull(millbrook);
            Assert.IsTrue(!millbrook.Executable);
            Assert.AreEqual(96, millbrook.CostPerBatchCents, "goods 2 sacks x 48c per batch, quoted separately from freight");
            Assert.AreEqual(125, millbrook.DeliveryCostPerShipmentCents, "freight 125c/shipment, not amortised into a per-sack figure");
            Assert.IsTrue(millbrook.Reason.Contains("freight blocked"), "freight-blocked status recorded: " + millbrook.Reason);
        }

        [Test]
        public void MillbrookCheaperOnGoods_BlockedOnFreight_NotRecommended()
        {
            // Millbrook goods (48c/sack) vs store goods (29c/sack): Millbrook is
            // NOT cheaper on goods here, so make it so — and it still must not
            // be recommended while freight is unavailable.
            BakeryFlourSupplierInputs inputs = BaseInputs();
            inputs.StorePricePerSackCents = 60;
            inputs.MillbrookPricePerSackCents = 48;
            inputs.MillbrookFreightAvailable = false;

            BakeryFlourSupplierComparisonResult result =
                BakeryFlourSupplierComparison.Compare(inputs, new List<string>());

            Assert.IsNotNull(result.Recommended);
            Assert.AreEqual("general-store", result.Recommended.SupplierId,
                "Millbrook's 48c goods price is listed but it is not recommended while freight is unavailable");
            FlourSupplierQuote millbrook = result.RankedQuotes.Find(q => q.SupplierId == "millbrook_grain_co");
            Assert.IsNotNull(millbrook);
            Assert.IsTrue(!millbrook.Executable);
            Assert.AreEqual(48, millbrook.PricePerSackCents);
            Assert.AreEqual(96, millbrook.CostPerBatchCents);
        }

        [Test]
        public void ImportNeverExecutable_ListsPriceAndTransit_NeverRecommended()
        {
            BakeryFlourSupplierComparisonResult result =
                BakeryFlourSupplierComparison.Compare(BaseInputs(), new List<string>());

            FlourSupplierQuote import = result.RankedQuotes.Find(q => q.SupplierId == "import-railhead");
            Assert.IsNotNull(import);
            Assert.IsTrue(!import.Executable);
            Assert.AreEqual(14, import.TransitDays);
            Assert.AreEqual(36, import.CostPerBatchCents, "12 lb x 3c/lb goods per batch — listed for honesty, never acted on");
            Assert.IsTrue(import.Reason.Contains("no live order path"), "reason: " + import.Reason);
            Assert.IsTrue(result.Recommended == null || result.Recommended.SupplierId != "import-railhead",
                "import is never recommended in this slice");
        }

        [Test]
        public void MissingPriceRefuses_NeverPricedAtZero()
        {
            BakeryFlourSupplierInputs inputs = BaseInputs();
            inputs.StorePricePerSackCents = null;

            BakeryFlourSupplierComparisonResult result =
                BakeryFlourSupplierComparison.Compare(inputs, new List<string>());

            FlourSupplierQuote store = result.RankedQuotes.Find(q => q.SupplierId == "general-store");
            Assert.IsNotNull(store);
            Assert.IsTrue(!store.Executable, "a source with no price is unavailable, not free");
            Assert.IsTrue(!store.CostPerBatchCents.HasValue, "no per-batch cost is computed from a missing price");
            Assert.IsTrue(!store.TotalCostForPurchaseCents.HasValue, "no purchase total is computed from a missing price");
            Assert.IsTrue(store.Reason.Contains("missing price"), "reason: " + store.Reason);
            Assert.IsTrue(result.Recommended == null || result.Recommended.SupplierId != "general-store",
                "the store must not be recommended without a price");
        }
    }
}
