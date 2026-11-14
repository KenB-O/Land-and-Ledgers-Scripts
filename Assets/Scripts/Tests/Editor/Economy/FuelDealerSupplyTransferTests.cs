using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    /// <summary>
    /// Plan D1 Tier 1: sawmill-byproduct supply transfer seam — the
    /// custody transfer moves real lots with provenance from a mill
    /// runtime into a named FuelDealer's intake register. Pin-first:
    /// these tests PASS on arrival at 2bb224ce (no new harness-visible
    /// type exists in Tier 1); they pin the seam Tier 1 wires. The
    /// production wiring (hub, bootstrapper, daily transfer) is
    /// source-reviewed only — those files are harness-excluded.
    /// </summary>
    [TestFixture]
    public sealed class FuelDealerSupplyTransferTests
    {
        private static SlabOffcutFuelLot Lot(EntityIdRegistry ids, int fuelWoodUnits)
        {
            return new SlabOffcutFuelLot
            {
                LotId = ids.Allocate(EntityKind.Lot),
                FuelWoodUnits = fuelWoodUnits,
                OwnerId = "mill-1",
                SourceMillBusinessId = "mill-1",
                SourceLogLotId = "log-1",
                StandId = "stand-1",
                Species = "pine",
                ProducedDayIndex = 0,
            };
        }

        private static SawmillShopRuntime MillWithLots(EntityIdRegistry ids, params SlabOffcutFuelLot[] lots)
        {
            var mill = new SawmillShopRuntime("mill-1", ids);
            mill.LoadFromSaveDto(new SawmillShopRuntime.SawmillShopRuntimeSaveDto
            {
                ByproductStock = new List<SlabOffcutFuelLot>(lots),
            });
            return mill;
        }

        [Test]
        public void Transfer_MovesMillLotsToDealerIntake_ExactUnitsAndProvenance()
        {
            var ids = new EntityIdRegistry();
            SlabOffcutFuelLot lotA = Lot(ids, 20);
            SlabOffcutFuelLot lotB = Lot(ids, 15);
            var mill = MillWithLots(ids, lotA, lotB);
            var intake = new FuelYardByproductIntake();
            var diag = new List<string>();

            int transferred = mill.TransferByproductsToFuelDealer("fuel-dealer-1", intake, diag);

            Assert.AreEqual(2, transferred);
            Assert.AreEqual(35, intake.TotalFuelWoodUnitsFor("fuel-dealer-1"));
            Assert.AreEqual(0, mill.ByproductStock.Count, "Custody moved — the mill no longer holds the lots.");
            IReadOnlyList<SlabOffcutFuelLot> held = intake.LotsFor("fuel-dealer-1");
            Assert.AreEqual(2, held.Count);
            bool hasA = false;
            bool hasB = false;
            foreach (SlabOffcutFuelLot lot in held)
            {
                if (lot.LotId.Equals(lotA.LotId)) hasA = true;
                if (lot.LotId.Equals(lotB.LotId)) hasB = true;
                StringAssert.Contains("stand-1", lot.ProvenanceChain());
                StringAssert.Contains("mill mill-1", lot.ProvenanceChain());
            }
            Assert.IsTrue(hasA && hasB, "both lots A and B are in the intake with their LotIds");
        }

        [Test]
        public void Transfer_EmptyMill_TransfersZero_NoIntakeChange()
        {
            var ids = new EntityIdRegistry();
            var mill = new SawmillShopRuntime("mill-1", ids);
            var intake = new FuelYardByproductIntake();
            var diag = new List<string>();

            int transferred = mill.TransferByproductsToFuelDealer("fuel-dealer-1", intake, diag);

            Assert.AreEqual(0, transferred);
            Assert.AreEqual(0, intake.TotalFuelWoodUnitsFor("fuel-dealer-1"));
            StringAssert.Contains("no byproduct lots", string.Join("; ", diag));
        }

        [Test]
        public void Transfer_NullIntake_TransfersZero_MillStockIntact()
        {
            var ids = new EntityIdRegistry();
            SlabOffcutFuelLot lotA = Lot(ids, 20);
            SlabOffcutFuelLot lotB = Lot(ids, 15);
            var mill = MillWithLots(ids, lotA, lotB);
            var diag = new List<string>();

            int transferred = mill.TransferByproductsToFuelDealer("fuel-dealer-1", null, diag);

            Assert.AreEqual(0, transferred);
            Assert.AreEqual(2, mill.ByproductStock.Count);
            int total = 0;
            foreach (SlabOffcutFuelLot lot in mill.ByproductStock) total += lot.FuelWoodUnits;
            Assert.AreEqual(35, total);
            StringAssert.Contains("no FuelDealer intake", string.Join("; ", diag));
        }

        [Test]
        public void Transfer_DuplicateLotHeldBack_OtherLotsMove()
        {
            var ids = new EntityIdRegistry();
            SlabOffcutFuelLot lotA = Lot(ids, 20);
            SlabOffcutFuelLot lotB = Lot(ids, 15);
            var mill = MillWithLots(ids, lotA, lotB);
            var intake = new FuelYardByproductIntake();
            var diag = new List<string>();
            Assert.IsNull(intake.ReceiveByproductLot("fuel-dealer-1", lotA, diag));

            int transferred = mill.TransferByproductsToFuelDealer("fuel-dealer-1", intake, diag);

            Assert.AreEqual(1, transferred);
            Assert.AreEqual(2, intake.LotsFor("fuel-dealer-1").Count);
            Assert.AreEqual(35, intake.TotalFuelWoodUnitsFor("fuel-dealer-1"));
            Assert.AreEqual(1, mill.ByproductStock.Count);
            Assert.IsTrue(mill.ByproductStock[0].LotId.Equals(lotA.LotId), "the duplicate lot stays in the mill");
            StringAssert.Contains("held back", string.Join("; ", diag));
        }

        [Test]
        public void Transfer_BlankDealerId_RefusesWhole_NothingMoves()
        {
            var ids = new EntityIdRegistry();
            SlabOffcutFuelLot lotA = Lot(ids, 20);
            SlabOffcutFuelLot lotB = Lot(ids, 15);
            var mill = MillWithLots(ids, lotA, lotB);
            var intake = new FuelYardByproductIntake();
            var diag = new List<string>();

            int transferred = mill.TransferByproductsToFuelDealer("", intake, diag);

            Assert.AreEqual(0, transferred);
            Assert.AreEqual(2, mill.ByproductStock.Count);
            int total = 0;
            foreach (SlabOffcutFuelLot lot in mill.ByproductStock) total += lot.FuelWoodUnits;
            Assert.AreEqual(35, total);
            Assert.AreEqual(0, intake.LotsFor("fuel-dealer-1").Count);
            Assert.AreEqual(0, intake.TotalFuelWoodUnitsFor("fuel-dealer-1"));
        }
    }
}
