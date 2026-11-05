using System;
using System.Collections.Generic;
using NUnit.Framework;
using LandLedgers.Economy.BreadDrivers;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Primitives;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Slice S1 (2026-11-05, food-economy trace §4.2): the bakery
    /// driver's daily execution wiring. Adopts runtime-planned batches
    /// without re-planning, proves the execution path reaches Precheck
    /// instead of the "nothing planned" guard, and pins that a refused
    /// run consumes nothing, mutates no stock, and creates no batch
    /// state. Unity compilation, Test Runner, PlayMode, and scene
    /// wiring are NOT claimed.
    /// </summary>
    [TestFixture]
    public sealed class BakeryDriverDailyExecutionTests
    {
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

        private static BakeryShopRuntime NewBakeryWithOven(List<string> diag)
        {
            var bakery = new BakeryShopRuntime("pierce-bakery-1");
            Assert.IsNull(bakery.AddOven("bakehouse", new List<string>
            {
                "oven-chamber-1", "kneading-table-1", "proofing-rack-1", "bake-peels-1",
            }, diag), "oven installation should succeed");
            return bakery;
        }

        private static void SeedFlourAndNotions(BakeryShopRuntime bakery, EntityIdRegistry ids, List<string> diag)
        {
            Assert.IsNull(BakeryFlourSupply.ReceiveImportArrival(
                bakery.FlourStock, BakeryBreadCatalog.FlourItemId, 100,
                "test-flour-import", "Test flour import", 0, ids, diag), "flour seeding should succeed");
            Assert.IsNull(BakeryFlourSupply.ReceiveImportArrival(
                bakery.FlourStock, BakeryBreadCatalog.NotionsItemId, 10,
                "test-notions-import", "Test notions import", 0, ids, diag), "notions seeding should succeed");
        }

        [Test]
        public void AdoptPlannedBatches_RecordsRuntimePlan_WithoutReplanning()
        {
            var diag = new List<string>();
            var bakery = new BakeryShopRuntime("pierce-bakery-1");
            var driver = new Series2BakeryProductionDriver(
                bakery, new Series2InStockBakeryFuelSource(bakery));

            List<string> planned = bakery.PlanBatch(BakeryBreadCatalog.BreadLoafId, 1, 3, diag);
            Assert.IsNotNull(planned);
            Assert.AreEqual(1, planned.Count);

            driver.AdoptPlannedBatches(3, planned, diag);

            Assert.AreEqual(3, driver.PlannedDayIndex, "adopting sets the planned day");
            Assert.AreEqual(1, driver.PlannedBatchIds.Count);
            Assert.AreEqual(planned[0], driver.PlannedBatchIds[0]);
            Assert.AreEqual(1, bakery.Batches.Count,
                "adopting must not re-plan: no duplicate batches in the runtime");
        }

        [Test]
        public void AdoptPlannedBatches_Twice_IsIdempotent()
        {
            var diag = new List<string>();
            var bakery = new BakeryShopRuntime("pierce-bakery-1");
            var driver = new Series2BakeryProductionDriver(
                bakery, new Series2InStockBakeryFuelSource(bakery));

            List<string> planned = bakery.PlanBatch(BakeryBreadCatalog.BreadLoafId, 2, 5, diag);
            Assert.IsNotNull(planned);
            Assert.AreEqual(2, planned.Count);

            driver.AdoptPlannedBatches(5, planned, diag);
            driver.AdoptPlannedBatches(5, planned, diag);

            Assert.AreEqual(5, driver.PlannedDayIndex);
            Assert.AreEqual(2, driver.PlannedBatchIds.Count, "adopting twice yields the same plan");
            Assert.AreEqual(planned[0], driver.PlannedBatchIds[0]);
            Assert.AreEqual(planned[1], driver.PlannedBatchIds[1]);
            Assert.AreEqual(2, bakery.Batches.Count, "adopting twice must not duplicate batches");
        }

        [Test]
        public void AdoptPlannedBatches_UnknownId_WarnsAndRefusesRun()
        {
            var diag = new List<string>();
            var bakery = new BakeryShopRuntime("pierce-bakery-1");
            var ids = new EntityIdRegistry();
            var driver = new Series2BakeryProductionDriver(
                bakery, new Series2InStockBakeryFuelSource(bakery));

            driver.AdoptPlannedBatches(4, new List<string> { "no-such-batch" }, diag);
            StringAssert.Contains("not in the bakery runtime's batches", string.Join(" | ", diag),
                "an adopted id the runtime no longer holds must be warned about; diag: " + string.Join(" | ", diag));

            int baked = driver.RunScheduledDay(
                4, true, WorkstationCatalog.BakeOven, OvenFinder(), null, diag, ids);
            Assert.AreEqual(-1, baked, "a plan naming no real batch must refuse, never bake blind");
            Assert.AreEqual(0, bakery.BreadShelf.Count, "no bread appears from a refused day");
            Assert.AreEqual(0, bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId), "refusal consumes no flour");
            Assert.AreEqual(0, bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId), "refusal consumes no fuel");
        }

        [Test]
        public void RunScheduledDay_AfterAdopt_WithEmptyFuelStore_RefusesOnFuel()
        {
            var diag = new List<string>();
            BakeryShopRuntime bakery = NewBakeryWithOven(diag);
            var ids = new EntityIdRegistry();
            SeedFlourAndNotions(bakery, ids, diag);
            var driver = new Series2BakeryProductionDriver(
                bakery, new Series2InStockBakeryFuelSource(bakery));

            List<string> planned = bakery.PlanBatch(BakeryBreadCatalog.BreadLoafId, 1, 2, diag);
            Assert.IsNotNull(planned);
            driver.AdoptPlannedBatches(2, planned, diag);

            int flourBefore = bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId);
            int notionsBefore = bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.NotionsItemId);
            int baked = driver.RunScheduledDay(
                2, true, WorkstationCatalog.BakeOven, OvenFinder(), null, diag, ids);

            Assert.AreEqual(-1, baked, "an empty fuel store refuses the run");
            StringAssert.Contains("fuel 0u on hand, need 1u", string.Join(" | ", diag),
                "the run must reach Precheck and refuse naming fuel, not the 'nothing planned' guard; diag: "
                + string.Join(" | ", diag));
            Assert.AreEqual(0, bakery.BreadShelf.Count, "no bread appears from a refused day");
            Assert.AreEqual(flourBefore, bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId),
                "a refused run consumes no flour");
            Assert.AreEqual(notionsBefore, bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.NotionsItemId),
                "a refused run consumes no notions");
            Assert.AreEqual(0, bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId),
                "a refused run consumes no fuel");
            Assert.AreEqual(BakeryBatchStage.Planned, bakery.Batches[0].Stage, "the refused batch stays planned");
        }

        [Test]
        public void RunScheduledDay_NoPlan_RefusesWithoutBaking()
        {
            var diag = new List<string>();
            BakeryShopRuntime bakery = NewBakeryWithOven(diag);
            var ids = new EntityIdRegistry();
            var driver = new Series2BakeryProductionDriver(
                bakery, new Series2InStockBakeryFuelSource(bakery));

            int baked = driver.RunScheduledDay(
                2, true, WorkstationCatalog.BakeOven, OvenFinder(), null, diag, ids);

            Assert.AreEqual(-1, baked, "an un-adopted driver still refuses the day");
            StringAssert.Contains("nothing planned", string.Join(" | ", diag),
                "the old guard is preserved; diag: " + string.Join(" | ", diag));
            Assert.AreEqual(0, bakery.BreadShelf.Count, "no bread appears from a refused day");
            Assert.AreEqual(0, bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId), "refusal consumes no flour");
            Assert.AreEqual(0, bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId), "refusal consumes no fuel");
        }
    }
}
