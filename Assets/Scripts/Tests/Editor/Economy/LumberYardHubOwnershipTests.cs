using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.LumberYard;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Persistence;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// Plan C2 (Priority C hub ownership): the Pass 9 lumber-yard slot
    /// contract — capture carries the business id, restore loads in
    /// place, a mismatched DTO diagnoses instead of overwriting the
    /// wrong yard, a missing holder drops with a diagnostic, and an old
    /// save restores empty. Only test 2 is red-first (it fails before
    /// the step-3 identity guard and passes after); tests 1, 3, 4, 5, 6
    /// pin the slot contract C2 relies on. The hub accessors and the
    /// bootstrapper wiring have no harness tests (MonoBehaviour hub)
    /// and are covered by source review plus the Unity-owed check.
    /// Stocking pattern copied from LumberYardDepthTests.StockedYard
    /// (that file is harness-excluded); this file does not reference it.
    /// </summary>
    [TestFixture]
    public sealed class LumberYardHubOwnershipTests
    {
        private static SawmillLumberLot DocumentedMillLot(int lotNumber, int units, string millId, string standId)
        {
            return new SawmillLumberLot
            {
                LotId = EntityId.For(EntityKind.Lot, lotNumber),
                LumberUnits = units,
                SourceLogLotId = "log-" + lotNumber,
                StandId = standId,
                Species = "white pine",
                MillBusinessId = millId,
                SawedBy = EntityId.For(EntityKind.Person, 5002),
                SawedDayIndex = 210,
                ConversionProfileId = "softwood-standard",
            };
        }

        private static LumberYardShopRuntime StockedYard(string yardId, int units)
        {
            var yard = new LumberYardShopRuntime(yardId, new EntityIdRegistry());
            var diag = new List<string>();
            Assert.IsNull(yard.ReceiveLumberFromSawmill(
                DocumentedMillLot(9401, units, "mill-1", "stand-1"), "merchantable", 220, 85, diag));
            return yard;
        }

        private static bool DiagContains(List<string> diag, string fragment)
        {
            if (diag == null || string.IsNullOrEmpty(fragment)) return false;
            foreach (string line in diag)
                if (!string.IsNullOrEmpty(line) && line.Contains(fragment)) return true;
            return false;
        }

        [Test]
        public void RoundTrip_RestoresStockAndOpeningFlag()
        {
            var yard = StockedYard("yard-1", 40);
            var state = new Series2Pass9SaveState { LumberYard = yard };
            var captureDiag = new List<string>();
            Series2Pass9SaveDto dto = state.Capture(captureDiag);
            Assert.IsNotNull(dto);
            Assert.IsNotNull(dto.LumberYard);
            Assert.AreEqual("yard-1", dto.LumberYard.BusinessInstanceId);

            var freshYard = new LumberYardShopRuntime("yard-1", new EntityIdRegistry());
            var freshState = new Series2Pass9SaveState { LumberYard = freshYard };
            var restoreDiag = new List<string>();
            freshState.Restore(dto, null, restoreDiag);
            Assert.AreEqual(40, freshYard.LumberStock.AvailableUnitsForSale(string.Empty, string.Empty));
            Assert.IsTrue(!freshYard.OpeningStockApplied);
            // The yard-specific drop diagnostic only: the restore-complete
            // line at Series2Pass9Save.cs:1311 itself contains "NOT
            // restored" ("Adapter DayIndex is NOT restored by design"), so
            // a bare "NOT restored" match would false-positive on every
            // restore. (Found in the red-first run.)
            Assert.IsTrue(!DiagContains(restoreDiag, "yard stock NOT restored"));
            Assert.IsTrue(!DiagContains(restoreDiag, "identity mismatch"));
        }

        [Test]
        public void IdentityMismatch_RefusesAndDiagnoses()
        {
            var holderYard = StockedYard("yard-A", 40);
            var otherYard = StockedYard("yard-B", 25);
            var otherState = new Series2Pass9SaveState { LumberYard = otherYard };
            Series2Pass9SaveDto dto = otherState.Capture(new List<string>());
            Assert.AreEqual("yard-B", dto.LumberYard.BusinessInstanceId);

            var state = new Series2Pass9SaveState { LumberYard = holderYard };
            var restoreDiag = new List<string>();
            state.Restore(dto, null, restoreDiag);
            Assert.AreEqual(40, holderYard.LumberStock.AvailableUnitsForSale(string.Empty, string.Empty));
            Assert.IsTrue(DiagContains(restoreDiag, "identity mismatch"));
        }

        [Test]
        public void NoHolder_DropsWithDiagnostic()
        {
            var yard = StockedYard("yard-1", 40);
            var stockedState = new Series2Pass9SaveState { LumberYard = yard };
            Series2Pass9SaveDto dto = stockedState.Capture(new List<string>());

            var emptyState = new Series2Pass9SaveState();
            var restoreDiag = new List<string>();
            emptyState.Restore(dto, null, restoreDiag);
            Assert.IsTrue(DiagContains(restoreDiag, "yard stock NOT restored"));
        }

        [Test]
        public void OldSave_NoSection_RestoresEmpty()
        {
            var yard = new LumberYardShopRuntime("yard-1", new EntityIdRegistry());
            var state = new Series2Pass9SaveState { LumberYard = yard };
            var restoreDiag = new List<string>();
            state.Restore(null, null, restoreDiag);
            Assert.IsTrue(DiagContains(restoreDiag, "no Pass 9 section in save"));
            Assert.IsTrue(!yard.OpeningStockApplied);
        }

        [Test]
        public void CaptureEmpty_NoHolder()
        {
            var state = new Series2Pass9SaveState();
            var captureDiag = new List<string>();
            Series2Pass9SaveDto dto = state.Capture(captureDiag);
            Assert.IsNotNull(dto);
            Assert.IsNull(dto.LumberYard);
            Assert.IsTrue(DiagContains(captureDiag, "no lumber yard supplied"));
        }

        [Test]
        public void CaptureCarriesBusinessId()
        {
            var yard = StockedYard("yard-1", 40);
            var state = new Series2Pass9SaveState { LumberYard = yard };
            Series2Pass9SaveDto dto = state.Capture(new List<string>());
            Assert.IsNotNull(dto);
            Assert.IsNotNull(dto.LumberYard);
            Assert.AreEqual("yard-1", dto.LumberYard.BusinessInstanceId);
        }
    }
}
