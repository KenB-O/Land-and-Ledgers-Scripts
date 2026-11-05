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
    /// Overnight 2026-11-04 slice 9: pins the live bakery-driver fuel
    /// contract. The production bootstrapper constructs the driver
    /// with a null fuel source ("a production fuel source is attached
    /// only by the daily operations wiring"), and no production code
    /// anywhere replaces it with the dealer source, plans batches, or
    /// runs a scheduled day — the driver path is implemented and
    /// tested but unwired, and the only production bake is the
    /// ThreeBusinessSlice demo path. Until Kennedy decides the daily
    /// baking policy (what product, how many batches, which FuelDealer
    /// supplies, off-map receipt yes or no), the driver must keep
    /// refusing loudly and baking nothing, never guessing a source.
    /// These tests pin exactly that: planned batches stay planned,
    /// no bread appears, and no stock moves.
    /// </summary>
    [TestFixture]
    public sealed class BakeryDriverFuelWiringTests
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

        [Test]
        public void NullFuelSource_PrecheckRefusesAndRunBakesNothing()
        {
            var diag = new List<string>();
            BakeryShopRuntime bakery = NewBakeryWithOven(diag);
            var driver = new Series2BakeryProductionDriver(bakery, null);

            List<string> planned = driver.PlanBatches(BakeryBreadCatalog.BreadLoafId, 2, 10, diag);
            Assert.IsNotNull(planned);
            Assert.AreEqual(2, planned.Count);

            string refusal = driver.Precheck(10, true, WorkstationCatalog.BakeOven, OvenFinder(), null, diag);
            Assert.IsNotNull(refusal, "A driver with no fuel source must refuse the pre-check.");
            StringAssert.Contains("no fuel source supplied", refusal);

            int baked = driver.RunScheduledDay(10, true, WorkstationCatalog.BakeOven, OvenFinder(), null, diag);
            Assert.AreEqual(-1, baked, "Nothing may bake from a sourceless driver.");
            Assert.AreEqual(2, driver.PlannedBatchIds.Count, "Refused batches stay planned, never dropped.");
            Assert.AreEqual(0, bakery.BreadShelf.Count, "No bread appears from a refused day.");
            Assert.AreEqual(0, bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId),
                "No fuel moves on a refused day.");
        }

        [Test]
        public void NullFuelSource_RefusalPrecedesFlourAndNotionShortfalls()
        {
            // The fuel-source check sits before the flour/notion checks
            // in Precheck, so a sourceless driver must report the
            // missing source even with empty input stocks — it must
            // never misreport the problem as a flour shortage.
            var diag = new List<string>();
            BakeryShopRuntime bakery = NewBakeryWithOven(diag);
            var driver = new Series2BakeryProductionDriver(bakery, null);
            Assert.IsNotNull(driver.PlanBatches(BakeryBreadCatalog.BreadLoafId, 1, 10, diag));

            string refusal = driver.Precheck(10, true, WorkstationCatalog.BakeOven, OvenFinder(), null, diag);
            Assert.IsNotNull(refusal);
            StringAssert.Contains("no fuel source supplied", refusal);
            Assert.AreEqual(0, bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId));
        }

        [Test]
        public void InStockSource_WithCoveringInputs_PassesPrecheck()
        {
            // Contrast control: the same driver with the honest
            // in-stock source (fuel already in the shop's store, moving
            // nothing) and covering flour and notions from declared
            // import arrivals must pass the pre-check outright. This
            // is the stand-in the production wiring may use until a
            // dealer source exists — it conjures nothing, and it
            // proves the null-source refusal above is about the
            // missing source, not about the rest of the setup.
            var diag = new List<string>();
            BakeryShopRuntime bakery = NewBakeryWithOven(diag);
            var ids = new EntityIdRegistry();
            BakeryFuelBootstrap.ApplyBootstrapEndowment(bakery.FuelStock, ids, 10, diag);
            Assert.AreEqual(BakeryFuelBootstrap.BootstrapFuelUnits,
                bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId));
            Assert.IsNull(BakeryFlourSupply.ReceiveImportArrival(
                bakery.FlourStock, BakeryBreadCatalog.FlourItemId, 100,
                "test-flour-import", "Test flour import", 10, ids, diag));
            Assert.IsNull(BakeryFlourSupply.ReceiveImportArrival(
                bakery.FlourStock, BakeryBreadCatalog.NotionsItemId, 100,
                "test-notions-import", "Test notions import", 10, ids, diag));

            var driver = new Series2BakeryProductionDriver(
                bakery, new Series2InStockBakeryFuelSource(bakery));
            Assert.IsNotNull(driver.PlanBatches(BakeryBreadCatalog.BreadLoafId, 1, 10, diag));

            string refusal = driver.Precheck(10, true, WorkstationCatalog.BakeOven, OvenFinder(), null, diag);
            Assert.IsNull(refusal,
                "Covering inputs must pass the pre-check; any refusal here is a different gate: " + refusal);
        }
    }
}
