using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Economy.Ordering;
using LandLedgers.Persistence;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Tests.Economy
{
    /// <summary>
    /// DEF-009 follow-on (Decision 5): the bootstrapper pre-creates the
    /// fuel-yard intake holder in EnsurePass9SaveOwnerForRestore, so a
    /// Continue load restores the saved lots in place instead of parking
    /// them as pending. These tests pin the Series2Pass9SaveState
    /// contract that pre-creation relies on. They PASS at base: the
    /// production change is one harness-excluded wiring line in
    /// FirstLedgerSliceBootstrapper.cs (source-reviewed only, like the
    /// FreightDriver/WholesaleIntake pre-creation it mirrors), and the
    /// state-level contract already works. They lock it so a future
    /// change to Restore/Capture cannot silently break the
    /// pre-creation's assumptions, and they document the loss shape
    /// pre-creation removes (T3): pending lots are NOT captured, so a
    /// re-save before any driver runs drops them.
    /// </summary>
    [TestFixture]
    public sealed class FuelIntakeSavePrecreationTests
    {
        private static Series2Pass9SaveState NewState()
        {
            var state = new Series2Pass9SaveState();
            state.Catalog = new Pass3StoreCatalog();
            return state;
        }

        private static FuelYardByproductIntake IntakeWithLots(List<string> diag)
        {
            var intake = new FuelYardByproductIntake();
            Assert.IsNull(intake.ReceiveByproductLot("dealer-1", new SlabOffcutFuelLot
            {
                LotId = EntityId.For(EntityKind.Lot, 21),
                FuelWoodUnits = 30,
                SourceMillBusinessId = "mill-1",
                OwnerId = "mill-1",
                SourceLogLotId = "log-1",
                StandId = "stand-1",
                ProducedDayIndex = 0,
            }, diag), "fuel lot 1 should be received; diag: " + string.Join("; ", diag));
            Assert.IsNull(intake.ReceiveByproductLot("dealer-1", new SlabOffcutFuelLot
            {
                LotId = EntityId.For(EntityKind.Lot, 22),
                FuelWoodUnits = 20,
                SourceMillBusinessId = "mill-1",
                OwnerId = "mill-1",
                SourceLogLotId = "log-1",
                StandId = "stand-1",
                ProducedDayIndex = 0,
            }, diag), "fuel lot 2 should be received; diag: " + string.Join("; ", diag));
            return intake;
        }

        [Test]
        public void PrecreatedHolder_RestoreAppliesInPlace_NeverParksPending()
        {
            var diag = new List<string>();
            var source = NewState();
            source.FuelIntake = IntakeWithLots(diag);
            Series2Pass9SaveDto dto = source.Capture(diag);

            // The pre-created holder: exactly what
            // EnsurePass9SaveOwnerForRestore now supplies before the
            // Pass XL restore consumes the state.
            var target = NewState();
            target.FuelIntake = new FuelYardByproductIntake();
            target.Restore(dto, null, diag);

            Assert.AreEqual(50, target.FuelIntake.TotalFuelWoodUnitsFor("dealer-1"),
                "saved lots must restore into the pre-created holder");
            Assert.IsNull(target.RestoredFuelIntake,
                "an in-place restore must never leave a pending DTO behind");
            Series2Pass9SaveDto recaptured = target.Capture(diag);
            Assert.IsNotNull(recaptured.FuelIntake, "the re-capture must carry the section");
            Assert.AreEqual(2, recaptured.FuelIntake.Lots.Count);
            Assert.AreEqual(50, TotalUnits(recaptured));
        }

        [Test]
        public void PendingPath_AppliesExactlyOnceWhenHolderAppears()
        {
            var diag = new List<string>();
            var source = NewState();
            source.FuelIntake = IntakeWithLots(diag);
            Series2Pass9SaveDto dto = source.Capture(diag);

            // No holder yet at restore time: the pre-fix shape.
            var target = NewState();
            target.Restore(dto, null, diag);
            Assert.IsNull(target.FuelIntake, "no holder existed at restore time");
            Assert.IsNotNull(target.RestoredFuelIntake,
                "the lots must park as pending, never drop");

            target.FuelIntake = new FuelYardByproductIntake();
            target.ApplyPendingFuelIntake();
            Assert.AreEqual(50, target.FuelIntake.TotalFuelWoodUnitsFor("dealer-1"),
                "applying the pending DTO must land the lots");
            Assert.IsNull(target.RestoredFuelIntake, "applying must clear the pending field");
            target.ApplyPendingFuelIntake();
            Assert.AreEqual(50, target.FuelIntake.TotalFuelWoodUnitsFor("dealer-1"),
                "a second apply must be a no-op, never a double load");
        }

        [Test]
        public void EarlyResaveAfterContinue_PreservesLots_WithPrecreation()
        {
            var diag = new List<string>();
            var source = NewState();
            source.FuelIntake = IntakeWithLots(diag);
            Series2Pass9SaveDto dto = source.Capture(diag);

            // Continue with pre-creation, then save again before any
            // driver runs: the chain the defect broke.
            var afterContinue = NewState();
            afterContinue.FuelIntake = new FuelYardByproductIntake();
            afterContinue.Restore(dto, null, diag);
            Series2Pass9SaveDto resaved = afterContinue.Capture(diag);
            Assert.IsNotNull(resaved.FuelIntake, "the re-save must carry the section");
            Assert.AreEqual(50, TotalUnits(resaved));

            var reloaded = NewState();
            reloaded.FuelIntake = new FuelYardByproductIntake();
            reloaded.Restore(resaved, null, diag);
            Assert.AreEqual(50, reloaded.FuelIntake.TotalFuelWoodUnitsFor("dealer-1"),
                "the lots must survive Continue, re-save, and reload");

            // The hazard pre-creation removes, pinned as the reason it
            // is required: pending lots are NOT captured, so without a
            // pre-created holder a re-save before any driver runs
            // writes an empty section and the lots are lost.
            var withoutHolder = NewState();
            withoutHolder.Restore(dto, null, diag);
            Assert.IsNotNull(withoutHolder.RestoredFuelIntake, "pending must be parked");
            Series2Pass9SaveDto lost = withoutHolder.Capture(diag);
            Assert.IsNull(lost.FuelIntake,
                "a pending-only state captures an empty fuel section: this is the loss pre-creation closes");
        }

        [Test]
        public void EmptyPrecreatedHolder_CapturesPresentEmptySection()
        {
            var diag = new List<string>();
            var state = NewState();
            state.FuelIntake = new FuelYardByproductIntake();
            Series2Pass9SaveDto dto = state.Capture(diag);

            Assert.IsNotNull(dto.FuelIntake,
                "a pre-created empty holder captures a present section, not a missing one");
            Assert.AreEqual(0, dto.FuelIntake.Lots.Count);
            foreach (string line in diag)
                Assert.IsFalse(line.Contains("no fuel-yard intake supplied"),
                    "no 'saved empty' fuel diagnostic may fire when the holder exists: " + line);

            var noHolder = NewState();
            var noHolderDiag = new List<string>();
            Series2Pass9SaveDto empty = noHolder.Capture(noHolderDiag);
            Assert.IsNull(empty.FuelIntake, "no holder captures no section");
            bool warned = false;
            foreach (string line in noHolderDiag)
                if (line.Contains("no fuel-yard intake supplied")) warned = true;
            Assert.IsTrue(warned, "the missing-holder diagnostic must still fire without a holder");
        }

        [Test]
        public void CoordinatorRoundTrip_FuelIntakeStaysInPass9Section()
        {
            var diag = new List<string>();
            var source = NewState();
            source.FuelIntake = IntakeWithLots(diag);
            PassXlSliceSaveDto dto = PassXlSliceSaveCoordinator.Capture(null, null, null, source);

            Assert.IsNotNull(dto.series2Pass9, "the Pass 9 section must be present");
            Assert.IsNotNull(dto.series2Pass9.FuelIntake,
                "the FuelIntake DTO lives in the Pass 9 section, not in SystemsSaveDto");
            Assert.AreEqual(50, TotalUnits(dto.series2Pass9));

            var target = NewState();
            target.FuelIntake = new FuelYardByproductIntake();
            List<string> restoreDiag = PassXlSliceSaveCoordinator.Restore(dto, null, null, null, target, null);
            Assert.AreEqual(50, target.FuelIntake.TotalFuelWoodUnitsFor("dealer-1"),
                "the lots must round-trip through the coordinator's Pass 9 section; diag: "
                + string.Join("; ", restoreDiag));
            Assert.IsNull(target.RestoredFuelIntake, "coordinator restore must not park pending");
        }

        private static int TotalUnits(Series2Pass9SaveDto dto)
        {
            int total = 0;
            if (dto?.FuelIntake?.Lots == null) return total;
            foreach (SlabOffcutFuelLot lot in dto.FuelIntake.Lots)
                if (lot != null) total += lot.FuelWoodUnits;
            return total;
        }
    }
}
