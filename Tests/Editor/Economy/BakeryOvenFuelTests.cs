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
    /// D1D: the oven-firebox fuel store — cordwood lots with upstream
    /// provenance, per-firing consumption, loud refusal without fuel, and
    /// save/load. Canon: "Oven/firebox" equipment; §8.1B fuel demand; the
    /// fuel_dealer_to_bakery supply link.
    /// </summary>
    [TestFixture]
    public sealed class BakeryOvenFuelTests
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

        private static BakeryShopRuntime FueledShop(List<string> diag, int day = 290)
        {
            var runtime = new BakeryShopRuntime("bakery-biz-1");
            var registry = new EntityIdRegistry();
            BakeryFlourBootstrap.ApplyBootstrapEndowment(runtime.FlourStock, registry, day, diag);
            BakeryFuelBootstrap.ApplyBootstrapEndowment(runtime.FuelStock, registry, day, diag);
            Assert.Null(runtime.AddOven("bakehouse", new List<string>
            {
                "oven-chamber-1", "kneading-table-1", "proofing-rack-1", "bake-peels-1",
            }, diag));
            return runtime;
        }

        private static int WorkDay(BakeryShopRuntime runtime, int day, List<string> diag)
        {
            return runtime.WorkDay(day, bakerKitUsable: true,
                WorkstationCatalog.BakeOven, OvenFinder(), diag, new EntityIdRegistry());
        }

        [Test]
        public void FuelBootstrap_EndowsMarkedCordwood_NeverSilent()
        {
            var diag = new List<string>();
            var runtime = new BakeryShopRuntime("bakery-biz-1");

            BakeryFuelBootstrap.ApplyBootstrapEndowment(
                runtime.FuelStock, new EntityIdRegistry(), 290, diag);

            Assert.AreEqual(BakeryFuelBootstrap.BootstrapFuelUnits,
                runtime.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId));
            var lot = runtime.FuelStock.Lots[0];
            Assert.IsTrue(lot.IsBootstrapEndowment, "opening fuel is explicitly marked");
            StringAssert.Contains("BOOTSTRAP", lot.ProvenanceChain());
        }

        [Test]
        public void ReceiveLot_RefusesOrphanFuel_Loudly()
        {
            var diag = new List<string>();
            var runtime = new BakeryShopRuntime("bakery-biz-1");
            var registry = new EntityIdRegistry();

            string rejection = runtime.FuelStock.ReceiveLot(new BakeryFuelLot
            {
                LotId = registry.Allocate(EntityKind.Lot),
                FuelName = BakeryBreadCatalog.FuelItemId,
                Units = 10,
                AcquiredDayIndex = 290,
                // No dealer, no import order/origin, not bootstrap: orphan.
            }, diag);

            Assert.IsNotNull(rejection, "orphan fuel must be refused");
            StringAssert.Contains("no fuel dealer", rejection.ToLower());
            Assert.AreEqual(0, runtime.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId));
        }

        [Test]
        public void ReceiveFuelDealerLot_NamesDealer_ChainPreserved()
        {
            var diag = new List<string>();
            var runtime = new BakeryShopRuntime("bakery-biz-1");

            string rejection = BakeryFuelSupply.ReceiveFuelDealerLot(
                runtime.FuelStock, "fueldealer-biz-1", "yard-lot-7", 25, 300,
                new EntityIdRegistry(), diag);

            Assert.IsNull(rejection);
            Assert.AreEqual(25, runtime.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId));
            StringAssert.Contains("fueldealer-biz-1",
                runtime.FuelStock.Lots[0].ProvenanceChain());
        }

        [Test]
        public void WorkDay_WithoutFuel_BatchStaysProofed_Loud()
        {
            var diag = new List<string>();
            // Flour but no fuel: the firebox cannot fire.
            var runtime = new BakeryShopRuntime("bakery-biz-1");
            var registry = new EntityIdRegistry();
            BakeryFlourBootstrap.ApplyBootstrapEndowment(runtime.FlourStock, registry, 290, diag);
            Assert.Null(runtime.AddOven("bakehouse", new List<string>
            {
                "oven-chamber-1", "kneading-table-1", "proofing-rack-1", "bake-peels-1",
            }, diag));

            Assert.IsNotNull(runtime.PlanBatch(BakeryBreadCatalog.BreadLoafId, 1, 300, diag));
            int baked = WorkDay(runtime, 300, diag);

            Assert.AreEqual(0, baked, "no fuel — no firing");
            Assert.AreEqual(BakeryBatchStage.Proofing, runtime.Batches[0].Stage,
                "the batch waits proofed, never worked without fuel");
            StringAssert.Contains("no oven fuel", string.Join("\n", diag).ToLower());
            Assert.AreEqual(0, runtime.BreadShelf.Count);
        }

        [Test]
        public void WorkDay_BurnsOneFuelUnitPerFiring()
        {
            var diag = new List<string>();
            var runtime = FueledShop(diag);

            Assert.IsNotNull(runtime.PlanBatch(BakeryBreadCatalog.BreadLoafId, 2, 300, diag));
            Assert.AreEqual(2, WorkDay(runtime, 300, diag));

            Assert.AreEqual(
                BakeryFuelBootstrap.BootstrapFuelUnits - 2 * BakeryBreadCatalog.FuelUnitsPerFiring,
                runtime.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId),
                "each firing burns its cordwood");
        }

        [Test]
        public void BakedLot_CarriesFuelProvenance()
        {
            var diag = new List<string>();
            var runtime = FueledShop(diag);

            Assert.IsNotNull(runtime.PlanBatch(BakeryBreadCatalog.BreadLoafId, 1, 300, diag));
            Assert.AreEqual(1, WorkDay(runtime, 300, diag));

            var lot = runtime.BreadShelf[0];
            bool fired = false;
            foreach (string line in lot.InputProvenance)
            {
                if (line.Contains("fired with")) fired = true;
            }

            Assert.IsTrue(fired, "the firing's fuel provenance rides on the bread lot");
        }

        [Test]
        public void SaveLoad_RoundTrip_PreservesFuelStore()
        {
            var diag = new List<string>();
            var runtime = FueledShop(diag);
            Assert.IsNotNull(runtime.PlanBatch(BakeryBreadCatalog.BreadLoafId, 1, 300, diag));
            Assert.AreEqual(1, WorkDay(runtime, 300, diag));

            var dto = runtime.CaptureSaveDto();
            var revived = new BakeryShopRuntime("bakery-biz-1");
            revived.LoadFromSaveDto(dto);

            Assert.AreEqual(
                BakeryFuelBootstrap.BootstrapFuelUnits - BakeryBreadCatalog.FuelUnitsPerFiring,
                revived.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId));
            Assert.IsTrue(revived.FuelStock.Lots[0].IsBootstrapEndowment);
        }
    }
}
