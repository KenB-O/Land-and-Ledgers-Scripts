using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// D1D: dough scheduling — proofing as a real scheduled stage between
    /// prep and bake, with the proofing rack gating daily throughput
    /// (Canon Part III §3.1; canon equipment: the proofing rack, with
    /// "expanded racks" as researched expansion gear).
    /// </summary>
    [TestFixture]
    public sealed class BakeryDoughSchedulingTests
    {
        private static Func<string, WorkstationComponentView?> OvenFinder()
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

        private static BakeryShopRuntime StockedShop(List<string> diag, int ovenCount = 1, int day = 290)
        {
            var runtime = new BakeryShopRuntime("bakery-biz-1");
            var registry = new EntityIdRegistry();
            BakeryFlourBootstrap.ApplyBootstrapEndowment(runtime.FlourStock, registry, day, diag);
            BakeryFuelBootstrap.ApplyBootstrapEndowment(runtime.FuelStock, registry, day, diag);
            for (int i = 0; i < ovenCount; i++)
            {
                Assert.Null(runtime.AddOven("bakehouse", new List<string>
                {
                    $"oven-chamber-{i + 1}", $"kneading-table-{i + 1}",
                    $"proofing-rack-{i + 1}", $"bake-peels-{i + 1}",
                }, diag));
            }

            return runtime;
        }

        private static int WorkDay(BakeryShopRuntime runtime, int day, List<string> diag)
        {
            return runtime.WorkDay(day, bakerKitUsable: true,
                WorkstationCatalog.BakeOven, OvenFinder(), diag, new EntityIdRegistry());
        }

        [Test]
        public void Batch_ProofsBeforeBaking_ProofMinutesFromSpec()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);

            Assert.IsNotNull(runtime.PlanBatch(BakeryBreadCatalog.BreadLoafId, 1, 300, diag));
            Assert.AreEqual(1, WorkDay(runtime, 300, diag));

            var batch = runtime.Batches[0];
            Assert.AreEqual(BakeryBatchStage.Baked, batch.Stage);
            bool proofed = false;
            foreach (string entry in batch.StageLog)
            {
                if (entry.Contains("proofing rack")) proofed = true;
            }

            Assert.IsTrue(proofed, "the batch must pass through proofing on the rack");
        }

        [Test]
        public void SmallRack_BatchesQueueFifo_AcrossDays()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);
            Assert.Null(runtime.SetProofingRackSlots(1, diag)); // 120 proof-min/day

            Assert.IsNotNull(runtime.PlanBatch(BakeryBreadCatalog.BreadLoafId, 2, 300, diag));
            Assert.AreEqual(1, WorkDay(runtime, 300, diag),
                "one rack slot proofs one bread batch per day");

            var first = runtime.Batches[0];
            var second = runtime.Batches[1];
            Assert.AreEqual(BakeryBatchStage.Baked, first.Stage);
            Assert.AreEqual(BakeryBatchStage.Proofing, second.Stage);
            Assert.Greater(second.ProofMinutesRemaining, 0, "the second batch waits for rack time");
            StringAssert.Contains("proofing rack at capacity", string.Join("\n", diag).ToLower());

            // Next day the queued batch proofs and bakes.
            Assert.AreEqual(1, WorkDay(runtime, 301, diag));
            Assert.AreEqual(BakeryBatchStage.Baked, second.Stage);
            Assert.AreEqual(301, runtime.BreadShelf[1].BakedDayIndex);
        }

        [Test]
        public void ExpandedRack_RemovesProofBottleneck()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag, ovenCount: 2);
            // Canon expansion gear: expanded racks.
            Assert.Null(runtime.SetProofingRackSlots(8, diag)); // 960 proof-min/day

            Assert.IsNotNull(runtime.PlanBatch(BakeryBreadCatalog.BreadLoafId, 5, 300, diag));
            int baked = WorkDay(runtime, 300, diag);

            // All five proofed (5×120=600 ≤ 960); the bake is now labor-gated
            // (5×120 prep + 4×60 bake = 840 = the full day), not rack-gated.
            foreach (var batch in runtime.Batches)
            {
                Assert.AreEqual(0, batch.ProofMinutesRemaining,
                    "no batch should still be waiting on the expanded rack");
            }

            Assert.AreEqual(4, baked);
        }

        [Test]
        public void SetProofingRackSlots_RefusesNonPositive()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);

            string rejection = runtime.SetProofingRackSlots(0, diag);
            Assert.IsNotNull(rejection);
            Assert.AreEqual(BakeryBreadCatalog.DefaultProofingRackSlots, runtime.ProofingRackSlots,
                "the old capacity stands on refusal");
        }

        [Test]
        public void ProofOrder_IsFifoByEntry()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);
            Assert.Null(runtime.SetProofingRackSlots(1, diag));

            // Rolls (90m) then bread (120m): the rack proves the roll first.
            Assert.IsNotNull(runtime.PlanBatch(BakeryBreadCatalog.RollsId, 1, 300, diag));
            Assert.IsNotNull(runtime.PlanBatch(BakeryBreadCatalog.BreadLoafId, 1, 300, diag));
            WorkDay(runtime, 300, diag);

            Assert.AreEqual(BakeryBreadCatalog.RollsId, runtime.BreadShelf[0].ProductId,
                "FIFO proofing: the first-entered batch bakes first");
        }

        [Test]
        public void SaveLoad_RoundTrip_PreservesProofState()
        {
            var diag = new List<string>();
            var runtime = StockedShop(diag);
            Assert.Null(runtime.SetProofingRackSlots(1, diag));
            Assert.IsNotNull(runtime.PlanBatch(BakeryBreadCatalog.BreadLoafId, 2, 300, diag));
            WorkDay(runtime, 300, diag); // one baked, one waiting on the rack

            var dto = runtime.CaptureSaveDto();
            var revived = new BakeryShopRuntime("bakery-biz-1");
            revived.LoadFromSaveDto(dto);

            Assert.AreEqual(1, revived.ProofingRackSlots);
            var waiting = revived.Batches[1];
            Assert.AreEqual(BakeryBatchStage.Proofing, waiting.Stage);
            Assert.Greater(waiting.ProofMinutesRemaining, 0);

            // The revived shop's rack still schedules: the waiter proofs and bakes.
            Assert.AreEqual(1, WorkDay(revived, 301, new List<string>()));
            Assert.AreEqual(BakeryBatchStage.Baked, revived.Batches[1].Stage);
        }
    }
}
