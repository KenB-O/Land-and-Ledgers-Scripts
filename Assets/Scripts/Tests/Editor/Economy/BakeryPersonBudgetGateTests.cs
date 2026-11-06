using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.BreadDrivers;
using LandLedgers.Economy.Businesses.Bakery;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Equipment.Workstations;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Plan 10 slice E3 (Obj 9; W6 design): the Precheck person-budget
    /// refusal gate. Glen's person budget (600 x capacity, E2-verified)
    /// is now consulted: a batch that cannot fit is refused, never
    /// partially booked. No task instances are created here — booking is
    /// B2, blocked on the Obj 45 ruling. Proof minutes are not baker
    /// labour and are never counted. The driver-level gate test lives in
    /// BakeryWholesaleDailyDriverTests (its fixture owns the store seam).
    /// </summary>
    [TestFixture]
    public sealed class BakeryPersonBudgetGateTests
    {
        private static readonly string[] OvenComponentIds =
        {
            "oven-chamber-1", "kneading-table-1", "proofing-rack-1", "bake-peels-1",
        };

        private sealed class Slice
        {
            public List<string> Diag = new List<string>();
            public BakeryShopRuntime Bakery = new BakeryShopRuntime("pierce-bakery-1");
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public Series2BakeryProductionDriver ProductionDriver;
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

        private static Slice NewSlice()
        {
            var s = new Slice();
            Assert.IsNull(s.Bakery.AddOven("bakehouse", new List<string>(OvenComponentIds), s.Diag),
                "oven installation should succeed");
            s.ProductionDriver = new Series2BakeryProductionDriver(
                s.Bakery, new Series2InStockBakeryFuelSource(s.Bakery));
            return s;
        }

        private static void SeedInputs(Slice s, int flourLb, int notions, int fuel)
        {
            if (flourLb > 0)
                Assert.IsNull(s.Bakery.FlourStock.ReceiveLot(new BakeryFlourLot
                {
                    LotId = s.Ids.Allocate(EntityKind.Lot),
                    FlourName = BakeryBreadCatalog.FlourItemId,
                    Units = flourLb,
                    AcquiredDayIndex = 0,
                    ImportOrderId = "test-flour-order",
                    OriginName = "Test mill",
                }, s.Diag));
            if (notions > 0)
                Assert.IsNull(s.Bakery.FlourStock.ReceiveLot(new BakeryFlourLot
                {
                    LotId = s.Ids.Allocate(EntityKind.Lot),
                    FlourName = BakeryBreadCatalog.NotionsItemId,
                    Units = notions,
                    AcquiredDayIndex = 0,
                    ImportOrderId = "test-notions-order",
                    OriginName = "Test notions",
                }, s.Diag));
            if (fuel > 0)
                Assert.IsNull(s.Bakery.FuelStock.ReceiveLot(new BakeryFuelLot
                {
                    LotId = s.Ids.Allocate(EntityKind.Lot),
                    FuelName = BakeryBreadCatalog.FuelItemId,
                    Units = fuel,
                    AcquiredDayIndex = 0,
                    ImportOrderId = "test-fuel-order",
                    OriginName = "Test fuel",
                    IsBootstrapEndowment = false,
                }, s.Diag));
        }

        private static string Precheck(Slice s, int? personBudgetMinutes)
        {
            return s.ProductionDriver.Precheck(
                2, true, WorkstationCatalog.BakeOven, OvenFinder(), personBudgetMinutes, s.Diag);
        }

        [Test]
        public void PersonBudgetFits_PrecheckPasses_AndBakeProceeds()
        {
            Slice s = NewSlice();
            SeedInputs(s, 24, 2, 2);
            Assert.IsNotNull(s.ProductionDriver.PlanBatches(BakeryBreadCatalog.BreadLoafId, 1, 2, s.Diag));

            Assert.IsNull(Precheck(s, 600), "a fed Glen (600m) fits a 180m batch; diag: " + string.Join(" | ", s.Diag));
            int baked = s.ProductionDriver.RunScheduledDay(
                2, true, WorkstationCatalog.BakeOven, OvenFinder(), 600, s.Diag, s.Ids);
            Assert.AreEqual(1, baked);
            Assert.AreEqual(1, s.Bakery.BreadShelf.Count);
        }

        [Test]
        public void PersonBudgetShort_RefusalNamesPersonBudgetAndNumbers_NothingBakes()
        {
            Slice s = NewSlice();
            SeedInputs(s, 24, 2, 2);
            Assert.IsNotNull(s.ProductionDriver.PlanBatches(BakeryBreadCatalog.BreadLoafId, 1, 2, s.Diag));

            string refusal = Precheck(s, 150);
            Assert.IsNotNull(refusal, "150m cannot fit a 180m batch");
            Assert.IsTrue(refusal.Contains("person budget"), "refusal names the person budget; refusal: " + refusal);
            Assert.IsTrue(refusal.Contains("150m"), "refusal names the person minutes; refusal: " + refusal);
            Assert.IsTrue(refusal.Contains("capacity 0.25"), "refusal names the capacity; refusal: " + refusal);
            Assert.IsTrue(refusal.Contains("180m"), "refusal names the minutes needed; refusal: " + refusal);
            Assert.IsTrue(refusal.Contains("1 batch(es)"), "refusal names the batch count; refusal: " + refusal);

            int baked = s.ProductionDriver.RunScheduledDay(
                2, true, WorkstationCatalog.BakeOven, OvenFinder(), 150, s.Diag, s.Ids);
            Assert.AreEqual(-1, baked, "refusal bakes nothing");
            Assert.AreEqual(0, s.Bakery.BreadShelf.Count, "no bread on the shelf");
            Assert.AreEqual(24, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId),
                "nothing dispensed — flour stays in the bin");
            Assert.AreEqual(1, s.ProductionDriver.PlannedBatchIds.Count, "the batch stays planned");
        }

        [Test]
        public void CapacityQuarter_RefusesBreadLoaf_AllowsRolls()
        {
            // Per-product minutes matter at the same capacity: 180m bread
            // refuses at 150m, 135m rolls fits.
            Slice bread = NewSlice();
            SeedInputs(bread, 24, 2, 2);
            Assert.IsNotNull(bread.ProductionDriver.PlanBatches(BakeryBreadCatalog.BreadLoafId, 1, 2, bread.Diag));
            Assert.IsNotNull(Precheck(bread, 150), "bread-loaf refuses at capacity 0.25");

            Slice rolls = NewSlice();
            SeedInputs(rolls, 24, 2, 2);
            Assert.IsNotNull(rolls.ProductionDriver.PlanBatches(BakeryBreadCatalog.RollsId, 1, 2, rolls.Diag));
            Assert.IsNull(Precheck(rolls, 150), "rolls fits at capacity 0.25; diag: " + string.Join(" | ", rolls.Diag));
        }

        [Test]
        public void NoBudgetSupplied_GateInactive_BakeProceedsOnRuntimeRules()
        {
            Slice s = NewSlice();
            SeedInputs(s, 24, 2, 2);
            Assert.IsNotNull(s.ProductionDriver.PlanBatches(BakeryBreadCatalog.BreadLoafId, 1, 2, s.Diag));

            Assert.IsNull(Precheck(s, null), "no caller budget — the gate is inactive, never defaulted");
            int baked = s.ProductionDriver.RunScheduledDay(
                2, true, WorkstationCatalog.BakeOven, OvenFinder(), null, s.Diag, s.Ids);
            Assert.AreEqual(1, baked, "existing behaviour preserved when no budget is supplied");
        }

        [Test]
        public void RepeatedSameDayCall_SameRefusal_NoStateChange()
        {
            Slice s = NewSlice();
            SeedInputs(s, 24, 2, 2);
            Assert.IsNotNull(s.ProductionDriver.PlanBatches(BakeryBreadCatalog.BreadLoafId, 1, 2, s.Diag));

            string first = Precheck(s, 150);
            string second = Precheck(s, 150);
            Assert.IsNotNull(first);
            Assert.AreEqual(first, second, "the gate is pure — re-evaluated from current state, same refusal");
            Assert.AreEqual(24, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.FlourItemId));
            Assert.AreEqual(2, s.Bakery.FlourStock.UnitsOnHand(BakeryBreadCatalog.NotionsItemId));
        }
    }
}
