// Slice 31 (2026-11-05, Pass 5): the weekly off-map reorder path's state seam.
// The manager and logistics files that drive the weekly path are
// harness-excluded, so the harness can only reach the path through
// BusinessRuntimeState / CategoryStockState — the queue / allocate /
// receive / save mechanics the weekly cadence depends on. These eight
// tests pin that seam: queueing from stock alone, the threshold edge,
// allocation clamping, the failed-shipment re-queue restore, the
// receipt-clamps-pending interaction with the daily chain, and the
// pending save round-trip. No prices, freight, or supplier identity is
// asserted anywhere: the weekly path's landed cost is unauthored (see
// plan 31) and is not touched here. All stock/target numbers are
// test-chosen fixtures, not production rates. Zero production files
// are touched by this slice.
using LandLedgers.Economy;
using LandLedgers.Persistence;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    [TestFixture]
    public sealed class WeeklyOffMapReorderStateTests
    {
        private static BusinessRuntimeState StoreWith(
            int stapleCurrent, int stapleTarget, int meatCurrent, int meatTarget)
        {
            var state = new BusinessRuntimeState();
            state.EnsureCategoryStock("staple_food", stapleCurrent, stapleTarget);
            state.EnsureCategoryStock("meat", meatCurrent, meatTarget);
            return state;
        }

        private static CategoryStockState StockOf(BusinessRuntimeState state, string categoryId)
        {
            return state.GetCategoryStock(categoryId);
        }

        [Test]
        public void WeeklyCadence_QueuesPendingFromStockAlone_NoSupplierNeeded()
        {
            BusinessRuntimeState state = StoreWith(40, 100, 90, 100);

            Assert.IsTrue(state.HasReorderNeed(0.5f));

            // No supplier, driver, or catalog exists anywhere in this
            // fixture: the cadence queues from stock alone, which is what
            // makes the weekly path the durable fallback when the regional
            // list is empty.
            state.BeginWeeklySettlementCadence(null, 0.5f, false);

            Assert.AreEqual(60, StockOf(state, "staple_food").PendingReorderUnits);
            Assert.AreEqual(0, StockOf(state, "meat").PendingReorderUnits);
            Assert.AreEqual(60, state.TotalPendingReorderUnits);
        }

        [Test]
        public void WeeklyCadence_ExactThresholdHealth_Queues()
        {
            BusinessRuntimeState state = StoreWith(50, 100, 90, 100);

            state.BeginWeeklySettlementCadence(null, 0.5f, false);

            // Health == threshold queues: NeedsReorder is <=, not strict.
            Assert.AreEqual(50, StockOf(state, "staple_food").PendingReorderUnits);
            Assert.AreEqual(0, StockOf(state, "meat").PendingReorderUnits);
        }

        [Test]
        public void WeeklyCadence_HealthyCategory_ClearsStalePending()
        {
            BusinessRuntimeState state = StoreWith(80, 100, 90, 100);

            // QueueReorderToTarget is health-blind: it queues a stale
            // pending of 20 even though the category is healthy.
            StockOf(state, "staple_food").QueueReorderToTarget();
            Assert.AreEqual(20, StockOf(state, "staple_food").PendingReorderUnits);

            state.BeginWeeklySettlementCadence(null, 0.5f, false);

            // The weekly refresh OVERWRITES pending from stock health
            // rather than accumulating it.
            Assert.AreEqual(0, StockOf(state, "staple_food").PendingReorderUnits);
            Assert.AreEqual(80, StockOf(state, "staple_food").CurrentStockUnits);
        }

        [Test]
        public void Allocate_ClampsToPending_AndUnknownCategoryReturnsZero()
        {
            BusinessRuntimeState state = StoreWith(10, 100, 90, 100);
            StockOf(state, "staple_food").QueueReorderToTarget();
            Assert.AreEqual(90, StockOf(state, "staple_food").PendingReorderUnits);

            Assert.AreEqual(30, state.AllocatePendingReorderUnits("staple_food", 30));
            Assert.AreEqual(60, StockOf(state, "staple_food").PendingReorderUnits);

            // Over-allocation clamps to what is pending.
            Assert.AreEqual(60, state.AllocatePendingReorderUnits("staple_food", 99));
            Assert.AreEqual(0, StockOf(state, "staple_food").PendingReorderUnits);

            // Negative and unknown-category allocations are safe no-ops.
            Assert.AreEqual(0, state.AllocatePendingReorderUnits("staple_food", -5));
            Assert.AreEqual(0, StockOf(state, "staple_food").PendingReorderUnits);
            Assert.AreEqual(0, state.AllocatePendingReorderUnits("no_such_category", 10));
        }

        [Test]
        public void FailedShipment_RequeueRestoresFullPending()
        {
            BusinessRuntimeState state = StoreWith(5, 45, 90, 100);
            StockOf(state, "staple_food").QueueReorderToTarget();
            Assert.AreEqual(40, StockOf(state, "staple_food").PendingReorderUnits);

            Assert.AreEqual(10, state.AllocatePendingReorderUnits("staple_food", 10));
            Assert.AreEqual(30, StockOf(state, "staple_food").PendingReorderUnits);

            // The manager's failure rollback re-queues via
            // QueueReorderToTarget: the allocated units were never shipped,
            // so the FULL shortfall belongs back in pending, not just the
            // unallocated remainder.
            StockOf(state, "staple_food").QueueReorderToTarget();

            Assert.AreEqual(40, StockOf(state, "staple_food").PendingReorderUnits);
            Assert.AreEqual(5, StockOf(state, "staple_food").CurrentStockUnits);
        }

        [Test]
        public void DailyReceipt_ReducesWeeklyPending_NoDoubleCountOfReceivedUnits()
        {
            BusinessRuntimeState state = StoreWith(50, 100, 90, 100);
            StockOf(state, "staple_food").QueueReorderToTarget();
            Assert.AreEqual(50, StockOf(state, "staple_food").PendingReorderUnits);

            // The receipt path the daily chain's port and the weekly
            // delivery share: received units shrink the weekly pending,
            // so the next weekly refresh does not re-queue units already
            // on the shelf. (The received half of the double-order
            // question; the in-flight half is decision-blocked per plan 31
            // and is NOT asserted here.)
            Assert.AreEqual(30, state.AddCategoryStockUnits("staple_food", 30));
            Assert.AreEqual(80, StockOf(state, "staple_food").CurrentStockUnits);
            Assert.AreEqual(20, StockOf(state, "staple_food").PendingReorderUnits);
        }

        [Test]
        public void PartialReceipt_LeavesRemainderPending()
        {
            BusinessRuntimeState state = StoreWith(50, 100, 90, 100);
            StockOf(state, "staple_food").QueueReorderToTarget();
            Assert.AreEqual(50, StockOf(state, "staple_food").PendingReorderUnits);

            Assert.AreEqual(20, state.ReceivePendingReorderUnits("staple_food", 20));
            Assert.AreEqual(70, StockOf(state, "staple_food").CurrentStockUnits);
            Assert.AreEqual(30, StockOf(state, "staple_food").PendingReorderUnits);

            // Over-receipt clamps to what is pending.
            Assert.AreEqual(30, state.ReceivePendingReorderUnits("staple_food", 99));
            Assert.AreEqual(100, StockOf(state, "staple_food").CurrentStockUnits);
            Assert.AreEqual(0, StockOf(state, "staple_food").PendingReorderUnits);

            Assert.AreEqual(0, state.ReceivePendingReorderUnits("no_such_category", 10));
        }

        [Test]
        public void PendingReorder_SurvivesSaveLoadRoundTrip()
        {
            BusinessRuntimeState state = StoreWith(50, 100, 90, 100);
            StockOf(state, "staple_food").QueueReorderToTarget();
            Assert.AreEqual(20, state.AllocatePendingReorderUnits("staple_food", 20));
            Assert.AreEqual(30, StockOf(state, "staple_food").PendingReorderUnits);
            StockOf(state, "meat").QueueReorderToTarget();
            Assert.AreEqual(10, StockOf(state, "meat").PendingReorderUnits);

            BusinessRuntimeSaveDto dto = state.CaptureSaveDto();
            BusinessRuntimeState restored = BusinessRuntimeState.FromSaveDto(dto, BusinessType.GeneralStore);

            Assert.AreEqual(30, StockOf(restored, "staple_food").PendingReorderUnits);
            Assert.AreEqual(50, StockOf(restored, "staple_food").CurrentStockUnits);
            Assert.AreEqual(100, StockOf(restored, "staple_food").TargetStockUnits);
            Assert.AreEqual(10, StockOf(restored, "meat").PendingReorderUnits);
            Assert.AreEqual(40, restored.TotalPendingReorderUnits);
        }
    }
}
