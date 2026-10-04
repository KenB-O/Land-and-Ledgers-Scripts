using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W2A: the bakery shop runtime — oven firing capacity gating the daily
    /// bake, dough-batch custody of flour, loud shortfall refusal, staling
    /// across days (day-old discounts, stale waste), and save/load.
    /// </summary>
    [TestFixture]
    public sealed class BakeryShopRuntimeTests
    {
        private static Func<string, WorkstationComponentView?> OvenFinder(params string[] goodAssetIds)
        {
            var kinds = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "oven-chamber-1", "oven-chamber" },
                { "kneading-table-1", "kneading-table" },
                { "proofing-rack-1", "proofing-rack" },
                { "bake-peels-1", "bake-peels" },
                { "oven-chamber-2", "oven-chamber" },
                { "kneading-table-2", "kneading-table" },
                { "proofing-rack-2", "proofing-rack" },
                { "bake-peels-2", "bake-peels" },
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

        private static BakeryShopRuntime StockedShop(
            List<string> diag, int ovenCount = 1, int day = 290)
        {
            var runtime = new BakeryShopRuntime("bakery-biz-1");
            var registry = new EntityIdRegistry();
            BakeryFlourBootstrap.ApplyBootstrapEndowment(runtime.FlourStock, registry, day, diag);
            // D1D: every firing burns cordwood — the shop opens with a fuel pile.
            BakeryFuelBootstrap.ApplyBootstrapEndowment(runtime.FuelStock, registry, day, diag);
            for (int i = 0; i < ovenCount; i++)
            {
                Assert.Null(runtime.AddOven("bakehouse", new List<string>
                {
                    $"oven-chamber-{i + 1}", $"kneading-table-{i + 1}",
                    $"proofing-rack-{i + 1}", $"bake-peels-{i + 1}",
                }, diag), "oven installation should succeed");
            }

            return runtime;
        }

        private static int WorkDay(BakeryShopRuntime runtime, int day, List<string> diag)
        {
            var registry = new EntityIdRegistry();
            return runtime.WorkDay(day, bakerKitUsable: true,
                WorkstationCatalog.BakeOven, OvenFinder(), diag, registry);
        }

        [Test]
        public void PlanBatch_RefusesUnknownProduct_Loudly()
        {
            var runtime = new BakeryShopRuntime("bakery-biz-1");
            var diag = new List<string>();

            var ids = runtime.PlanBatch("bake.croissant-1870", 1, 300, diag);
            Assert.IsNull(ids, "unknown product must refuse (not schedule)");
            StringAssert.Contains("BakeryShopRuntime:", string.Join("\n", diag));
            Assert.AreEqual(0, runtime.Batches.Count);
        }

        [Test]
        public void WorkDay_BakesPlannedBatches_OvenAndFlourGated()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);

            var ids = runtime.PlanBatch(BakeryBreadCatalog.BreadLoafId, 2, 300, diag);
            Assert.IsNotNull(ids);
            Assert.AreEqual(2, ids.Count);

            int baked = WorkDay(runtime, 300, diag);
            Assert.AreEqual(2, baked);
            Assert.AreEqual(2, runtime.BreadShelf.Count);

            var lot = runtime.BreadShelf[0];
            Assert.AreEqual(BakeryBreadCatalog.BreadLoafId, lot.ProductId);
            Assert.AreEqual(BakeryBreadCatalog.BreadYieldPerBatch, lot.UnitCount);
            Assert.AreEqual(BakeryGoodsCondition.Fresh, lot.Condition);
            Assert.AreEqual(300, lot.BakedDayIndex);
            // Real inputs → real bread: the lot carries flour provenance.
            Assert.Greater(lot.InputProvenance.Count, 0);
            StringAssert.Contains("bakery-flour", lot.InputProvenance[0]);
            Assert.IsTrue(lot.LotId.IsValid, "baked lots take real lot ids from the registry");

            // 2 batches × 12 lb flour consumed from the 120 lb endowment.
            Assert.AreEqual(120 - 2 * BakeryBreadCatalog.BreadFlourPerBatch,
                runtime.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId));
        }

        [Test]
        public void WorkDay_OvenCapacityGatesBake_OneOvenFourFirings()
        {
            var diag = new List<string>();
            // D1D: rolls (90m proof each) fit 5-across on the rack (5×90=450 ≤ 480),
            // so the OVEN is the binding gate here, not proofing.
            var runtime = StockedShop(diag); // one oven, 4 firings/day

            Assert.IsNotNull(runtime.PlanBatch(BakeryBreadCatalog.RollsId, 5, 300, diag));
            int baked = WorkDay(runtime, 300, diag);
            Assert.AreEqual(BakeryBreadCatalog.FiringsPerOvenPerDay, baked,
                "one oven bakes at most its daily firing budget");
            Assert.AreEqual(BakeryBreadCatalog.FiringsPerOvenPerDay, runtime.BreadShelf.Count);

            // The 5th batch proofed but found no firing — parked proofed, not dropped.
            int proofedWaiting = 0;
            foreach (var batch in runtime.Batches)
            {
                if (batch.Stage == BakeryBatchStage.Proofing && batch.ProofMinutesRemaining <= 0) proofedWaiting++;
            }

            Assert.AreEqual(1, proofedWaiting);
            StringAssert.Contains("fully booked", string.Join("\n", diag).ToLower());
        }

        [Test]
        public void WorkDay_ProofingRackGatesBake_FifthBatchWaits()
        {
            var diag = new List<string>();
            // D1D: two ovens (8 firings) but one 4-slot rack (4×120=480 proof-min/day):
            // bread needs 120m proof each, so the RACK gates at 4 batches.
            var runtime = StockedShop(diag, ovenCount: 2);

            Assert.IsNotNull(runtime.PlanBatch(BakeryBreadCatalog.BreadLoafId, 5, 300, diag));
            int baked = WorkDay(runtime, 300, diag);
            Assert.AreEqual(4, baked, "the proofing rack — not the ovens — binds here");

            int waitingOnRack = 0;
            foreach (var batch in runtime.Batches)
            {
                if (batch.Stage == BakeryBatchStage.Proofing && batch.ProofMinutesRemaining > 0) waitingOnRack++;
            }

            Assert.AreEqual(1, waitingOnRack, "the 5th batch waits for rack time, loudly");
            StringAssert.Contains("proofing rack at capacity", string.Join("\n", diag).ToLower());
        }

        [Test]
        public void WorkDay_NoReadyOven_NothingBakes_TechXGate()
        {
            var diag = new List<string>();
            var runtime = new BakeryShopRuntime("bakery-biz-1");
            var registry = new EntityIdRegistry();
            BakeryFlourBootstrap.ApplyBootstrapEndowment(runtime.FlourStock, registry, 290, diag);
            BakeryFuelBootstrap.ApplyBootstrapEndowment(runtime.FuelStock, registry, 290, diag);
            // An oven with no components: not ready.
            Assert.Null(runtime.AddOven("bakehouse", new List<string>(), diag));

            Assert.IsNotNull(runtime.PlanBatch(BakeryBreadCatalog.BreadLoafId, 1, 300, diag));
            int baked = runtime.WorkDay(300, bakerKitUsable: true,
                WorkstationCatalog.BakeOven, OvenFinder(), diag, registry);
            Assert.AreEqual(0, baked, "no ready oven — Tech X §3.5: the room is not enough");
            StringAssert.Contains("no ready bake oven", string.Join("\n", diag).ToLower());
        }

        [Test]
        public void WorkDay_FlourShortfall_StaysPlanned_Loud()
        {
            var diag = new List<string>();
            // No flour at all: nothing dispensed, nothing conjured.
            var runtime = new BakeryShopRuntime("bakery-biz-1");
            var fuelRegistry = new EntityIdRegistry();
            BakeryFuelBootstrap.ApplyBootstrapEndowment(runtime.FuelStock, fuelRegistry, 290, diag);
            Assert.Null(runtime.AddOven("bakehouse", new List<string>
            {
                "oven-chamber-1", "kneading-table-1", "proofing-rack-1", "bake-peels-1",
            }, diag));

            Assert.IsNotNull(runtime.PlanBatch(BakeryBreadCatalog.BreadLoafId, 1, 300, diag));
            int baked = runtime.WorkDay(300, bakerKitUsable: true,
                WorkstationCatalog.BakeOven, OvenFinder(), diag, new EntityIdRegistry());
            Assert.AreEqual(0, baked);
            Assert.AreEqual(BakeryBatchStage.Planned, runtime.Batches[0].Stage,
                "flour shortfall keeps the batch planned — never worked on conjured stock");
            StringAssert.Contains("no flour on hand", string.Join("\n", diag).ToLower());
            Assert.AreEqual(0, runtime.BreadShelf.Count);
        }

        [Test]
        public void BreadStales_AcrossDays_DayOldDiscount_ThenWaste()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);
            runtime.SetPriceSchedule(new BakeryPriceSchedule(8, 2, 25, 50));

            Assert.IsNotNull(runtime.PlanBatch(BakeryBreadCatalog.BreadLoafId, 1, 300, diag));
            Assert.AreEqual(1, WorkDay(runtime, 300, diag));

            // Day 300: fresh at full price.
            var freshSale = runtime.SellBread(BakeryBreadCatalog.BreadLoafId, 4, 300, diag);
            Assert.AreEqual(1, freshSale.Count);
            Assert.AreEqual(BakeryGoodsCondition.Fresh, freshSale[0].ConditionSoldAt);
            Assert.AreEqual(8, freshSale[0].PricePerUnitCents);
            Assert.AreEqual(4 * 8, freshSale[0].TotalCents);

            // Day 301: day-old at the schedule discount.
            var dayOldSale = runtime.SellBread(BakeryBreadCatalog.BreadLoafId, 4, 301, diag);
            Assert.AreEqual(1, dayOldSale.Count);
            Assert.AreEqual(BakeryGoodsCondition.DayOld, dayOldSale[0].ConditionSoldAt);
            Assert.AreEqual(4, dayOldSale[0].PricePerUnitCents);

            // Day 302: stale — never sold, written off as waste with provenance.
            var staleSale = runtime.SellBread(BakeryBreadCatalog.BreadLoafId, 100, 302, diag);
            Assert.AreEqual(0, staleSale.Count, "stale bread can never be sold");
            Assert.AreEqual(1, runtime.Waste.Count);
            Assert.AreEqual(16, runtime.Waste[0].Units);
            StringAssert.Contains("stale", string.Join("\n", diag).ToLower());
            Assert.AreEqual(BakeryGoodsCondition.Stale, runtime.BreadShelf[0].Condition);

            // The waste write-off is noted exactly once, even across repeated aging.
            runtime.AgeShelf(303, diag);
            Assert.AreEqual(1, runtime.Waste.Count, "no double write-off after save/load-safe dedupe");
        }

        [Test]
        public void SellBread_SellsOldestFirst_DayOldBeforeFresh()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);
            runtime.SetPriceSchedule(new BakeryPriceSchedule(8, 2, 25, 50));

            Assert.IsNotNull(runtime.PlanBatch(BakeryBreadCatalog.BreadLoafId, 1, 300, diag));
            Assert.AreEqual(1, WorkDay(runtime, 300, diag));
            Assert.IsNotNull(runtime.PlanBatch(BakeryBreadCatalog.BreadLoafId, 1, 301, diag));
            Assert.AreEqual(1, WorkDay(runtime, 301, diag));

            // Day 301: the day-old lot (baked 300) sells before the fresh lot (baked 301).
            var sale = runtime.SellBread(BakeryBreadCatalog.BreadLoafId, 24, 301, diag);
            Assert.AreEqual(1, sale.Count);
            Assert.AreEqual(BakeryGoodsCondition.DayOld, sale[0].ConditionSoldAt);
            Assert.IsTrue(sale[0].LotId.Equals(runtime.BreadShelf[0].LotId),
                "the sale must come from the older lot (shelf is bake-ordered)");
            Assert.AreEqual(24, sale[0].Units);
        }

        [Test]
        public void PopulateWorkstations_AliasesFirstReadyOven()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag, ovenCount: 2);
            var registry = new BusinessWorkstations("bakery-biz-1");

            runtime.PopulateWorkstations(registry, WorkstationCatalog.BakeOven, OvenFinder(), diag);

            WorkstationInstance alias = registry.Get(BakeryBreadCatalog.BakeOvenStationId);
            Assert.IsNotNull(alias, "the bake-oven alias must resolve when an oven is ready");
            StringAssert.Contains("bake-oven-0", alias.InstanceId);
            StringAssert.Contains("first ready", string.Join("\n", diag).ToLower());
        }

        [Test]
        public void SaveLoad_RoundTrip_PreservesShelfBatchesAndWasteDedupe()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);
            runtime.SetPriceSchedule(new BakeryPriceSchedule(9, 3, 30, 50));

            Assert.IsNotNull(runtime.PlanBatch(BakeryBreadCatalog.BreadLoafId, 1, 300, diag));
            Assert.AreEqual(1, WorkDay(runtime, 300, diag));
            // Sell some fresh on day 300, then age the remainder to stale.
            runtime.SellBread(BakeryBreadCatalog.BreadLoafId, 4, 300, diag);
            Assert.AreEqual(1, runtime.Sales.Count);
            runtime.AgeShelf(302, diag);
            Assert.AreEqual(1, runtime.Waste.Count);

            var dto = runtime.CaptureSaveDto();

            var revived = new BakeryShopRuntime("bakery-biz-1");
            revived.LoadFromSaveDto(dto);

            Assert.AreEqual(1, revived.BreadShelf.Count);
            Assert.AreEqual(BakeryGoodsCondition.Stale, revived.BreadShelf[0].Condition);
            Assert.AreEqual(1, revived.Sales.Count);
            Assert.AreEqual(1, revived.Waste.Count);
            Assert.AreEqual(9, revived.Prices.BreadLoafFreshCents);
            Assert.AreEqual(108, revived.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId));
            Assert.AreEqual(1, revived.Ovens.Count);

            // The waste dedupe list survives — aging again does not double-write.
            revived.AgeShelf(303, new List<string>());
            Assert.AreEqual(1, revived.Waste.Count);
        }
    }
}
