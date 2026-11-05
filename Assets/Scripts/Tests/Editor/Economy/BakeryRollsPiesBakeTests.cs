// Slice 33 (Pass 7): the rolls and pies bake conversions.
// The loaf conversion (12 lb flour + 1 notion + 1 fuel -> 24
// loaves) is harness-pinned by FlourToBakeToShelf_CashReconciles,
// but no harness-included test baked rolls (6 lb -> 48) or pies
// (4 lb -> 12): the only rolls bakes lived in the !-excluded
// BakeryShopRuntimeTests.cs and BakeryDoughSchedulingTests.cs.
// These tests bake one batch of each through the real driver
// with the in-stock fuel source, funding exactly one batch's
// authored inputs, and asserting exact consumption and yield.
// Nothing here wires a daily bake or decides any part of the
// daily bake policy (product, batch count, fuel source,
// off-map receipt); the driver stays as unwired as it is.
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
    [TestFixture]
    public sealed class BakeryRollsPiesBakeTests
    {
        private sealed class Slice
        {
            public List<string> Diag = new List<string>();
            public BakeryShopRuntime Bakery = new BakeryShopRuntime("pierce-bakery-1");
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

        private static Slice NewBakeryWithOven()
        {
            var s = new Slice();
            Assert.IsNull(s.Bakery.AddOven("bakehouse", new List<string>
            {
                "oven-chamber-1", "kneading-table-1", "proofing-rack-1", "bake-peels-1",
            }, s.Diag), "oven installation should succeed");
            return s;
        }

        // One lot each, day 0, test lot ids — copied from the
        // excluded Review2VerticalD1Tests.cs BakeAndFund, minus the
        // store runtime / cash ports / transfer options, which
        // baking does not touch.
        private static void SeedFlour(Slice s, int units)
        {
            Assert.IsNull(s.Bakery.FlourStock.ReceiveLot(new BakeryFlourLot
            {
                LotId = s.Ids.Allocate(EntityKind.Lot),
                FlourName = BakeryBreadCatalog.FlourItemId,
                Units = units,
                AcquiredDayIndex = 0,
                ImportOrderId = "test-flour-order",
                OriginName = "Test mill",
            }, s.Diag), "flour seeding should succeed");
        }

        private static void SeedNotions(Slice s, int units)
        {
            Assert.IsNull(s.Bakery.FlourStock.ReceiveLot(new BakeryFlourLot
            {
                LotId = s.Ids.Allocate(EntityKind.Lot),
                FlourName = BakeryBreadCatalog.NotionsItemId,
                Units = units,
                AcquiredDayIndex = 0,
                ImportOrderId = "test-notions-order",
                OriginName = "Test notions",
            }, s.Diag), "notions seeding should succeed");
        }

        private static void SeedFuel(Slice s, int units)
        {
            Assert.IsNull(s.Bakery.FuelStock.ReceiveLot(new BakeryFuelLot
            {
                LotId = s.Ids.Allocate(EntityKind.Lot),
                FuelName = BakeryBreadCatalog.FuelItemId,
                Units = units,
                AcquiredDayIndex = 0,
                ImportOrderId = "test-fuel-order",
                OriginName = "Test fuel",
                IsBootstrapEndowment = false,
            }, s.Diag), "fuel seeding should succeed");
        }

        // Plans 1 batch of the product for the given day and runs
        // the scheduled day through the real driver with the
        // in-stock fuel source; returns the baked count.
        private static int BakeOne(Slice s, string productId, int day)
        {
            var driver = new Series2BakeryProductionDriver(
                s.Bakery, new Series2InStockBakeryFuelSource(s.Bakery));
            List<string> planned = driver.PlanBatches(productId, 1, day, s.Diag);
            Assert.IsNotNull(planned, "planning should succeed; diag: " + string.Join("; ", s.Diag));
            Assert.AreEqual(1, planned.Count, "one batch should be planned");
            return driver.RunScheduledDay(
                day, true, WorkstationCatalog.BakeOven, OvenFinder(), null, s.Diag, s.Ids);
        }

        [Test]
        public void RollsBatch_BakesThroughDriver_ConsumesExactInputs()
        {
            var s = NewBakeryWithOven();
            SeedFlour(s, BakeryBreadCatalog.RollsFlourPerBatch);
            SeedNotions(s, BakeryBreadCatalog.StandardNotionsPerBatch);
            SeedFuel(s, BakeryBreadCatalog.FuelUnitsPerFiring);
            int baked = BakeOne(s, BakeryBreadCatalog.RollsId, 2);
            Assert.AreEqual(1, baked, "one batch should bake; diag: " + string.Join("; ", s.Diag));
            Assert.AreEqual(1, s.Bakery.BreadShelf.Count);
            Assert.AreEqual(BakeryBreadCatalog.RollsId, s.Bakery.BreadShelf[0].ProductId);
            Assert.AreEqual(BakeryBreadCatalog.RollsYieldPerBatch, s.Bakery.BreadShelf[0].UnitCount);
            Assert.AreEqual(BakeryGoodsCondition.Fresh, s.Bakery.BreadShelf[0].Condition);
            Assert.AreEqual(2, s.Bakery.BreadShelf[0].BakedDayIndex);
            Assert.AreEqual(0, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId), "6 lb fed minus 6 lb baked");
            Assert.AreEqual(0, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.NotionsItemId), "1 notion baked");
            Assert.AreEqual(0, s.Bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId), "1 fuel minus 1 firing");
        }

        [Test]
        public void PiesBatch_BakesThroughDriver_ConsumesExactInputs()
        {
            var s = NewBakeryWithOven();
            SeedFlour(s, BakeryBreadCatalog.PieFlourPerBatch);
            SeedNotions(s, BakeryBreadCatalog.StandardNotionsPerBatch);
            SeedFuel(s, BakeryBreadCatalog.FuelUnitsPerFiring);
            int baked = BakeOne(s, BakeryBreadCatalog.PieId, 2);
            Assert.AreEqual(1, baked, "one batch should bake; diag: " + string.Join("; ", s.Diag));
            Assert.AreEqual(1, s.Bakery.BreadShelf.Count);
            Assert.AreEqual(BakeryBreadCatalog.PieId, s.Bakery.BreadShelf[0].ProductId);
            Assert.AreEqual(BakeryBreadCatalog.PieYieldPerBatch, s.Bakery.BreadShelf[0].UnitCount);
            Assert.AreEqual(BakeryGoodsCondition.Fresh, s.Bakery.BreadShelf[0].Condition);
            Assert.AreEqual(2, s.Bakery.BreadShelf[0].BakedDayIndex);
            Assert.AreEqual(0, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId), "4 lb fed minus 4 lb baked");
            Assert.AreEqual(0, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.NotionsItemId), "1 notion baked");
            Assert.AreEqual(0, s.Bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId), "1 fuel minus 1 firing");
        }
    }
}
