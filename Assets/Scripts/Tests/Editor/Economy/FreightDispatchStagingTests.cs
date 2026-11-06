// Slice S5 (2026-11-05): dispatch staging travel + arrival gate — the
// pure helper seam (FreightDispatchStaging). Pins the travel-ref
// round-trip, cross-talk rejection between the dispatch / hitch / plain
// dispatch task kinds, the needs-travel decision table, the home-building
// precondition, and save stability of the ref.
//
// SCOPE NOTE — read before citing these tests as coverage of the production
// wiring: SimulationDrivers.cs is harness-EXCLUDED, so the Ensure /
// starter / gate / handler wiring that calls this helper is
// source-reviewed only; no test in this file invokes it. What IS
// harness-tested here is the exact ref + decision seam that wiring uses.
using LandLedgers.Economy.Freight;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    [TestFixture]
    public sealed class FreightDispatchStagingTests
    {
        private const string AgreementId = "freight-agreement:request-1";
        private const string ShipmentId = "freight-shipment:request-1";

        [Test]
        public void TravelRef_RoundTrips_AgreementAndShipment()
        {
            // Production form: both ids carry a namespace colon.
            string travelRef = FreightDispatchStaging.TravelCustomerRef(AgreementId, ShipmentId);
            Assert.AreEqual("freight-dispatch:travel:" + AgreementId + ":" + ShipmentId, travelRef);
            Assert.IsTrue(FreightDispatchStaging.TryParseTravelRef(travelRef, out string agreementId, out string shipmentId));
            Assert.AreEqual(AgreementId, agreementId);
            Assert.AreEqual(ShipmentId, shipmentId);
            Assert.IsTrue(FreightDispatchStaging.IsDispatchTravelRef(travelRef));

            // Simple form: ids without colons round-trip identically.
            string simpleRef = FreightDispatchStaging.TravelCustomerRef("agreement-1", "shipment-1");
            Assert.IsTrue(FreightDispatchStaging.TryParseTravelRef(simpleRef, out string simpleAgreement, out string simpleShipment));
            Assert.AreEqual("agreement-1", simpleAgreement);
            Assert.AreEqual("shipment-1", simpleShipment);
        }

        [Test]
        public void TravelRef_RejectsPlainDispatchRef_AndHitchTravelRef()
        {
            // The plain dispatch ref is the 1-minute Work task, not travel.
            Assert.IsFalse(FreightDispatchStaging.TryParseTravelRef(
                "freight-dispatch:" + AgreementId + ":" + ShipmentId, out _, out _));
            Assert.IsFalse(FreightDispatchStaging.IsDispatchTravelRef(
                "freight-dispatch:" + AgreementId + ":" + ShipmentId));

            // The hitch travel ref is a different leg's task (real code
            // value: DevelopmentHitchTaskPrefix + "travel:" + shipment).
            Assert.IsFalse(FreightDispatchStaging.TryParseTravelRef(
                "development-hitch:travel:" + ShipmentId, out _, out _));
            Assert.IsFalse(FreightDispatchStaging.IsDispatchTravelRef(
                "development-hitch:travel:" + ShipmentId));

            // Rejects everything else: null, empty, bare prefix, wrong
            // prefix, and a travel ref with a missing shipment half.
            Assert.IsFalse(FreightDispatchStaging.TryParseTravelRef(null, out _, out _));
            Assert.IsFalse(FreightDispatchStaging.TryParseTravelRef(string.Empty, out _, out _));
            Assert.IsFalse(FreightDispatchStaging.TryParseTravelRef("freight-dispatch:travel:", out _, out _));
            Assert.IsFalse(FreightDispatchStaging.TryParseTravelRef(
                "freight-dispatch:travel:" + AgreementId, out _, out _));
            Assert.IsFalse(FreightDispatchStaging.TryParseTravelRef(
                "development-preparation:travel:" + ShipmentId, out _, out _));
            Assert.IsFalse(FreightDispatchStaging.TryParseTravelRef(
                "freight-dispatch:travel:agreement-1:shipment-1:extra", out _, out _));
        }

        [Test]
        public void NeedsStagingTravel_DecisionTable()
        {
            // (at staging, any) -> false: already there, no travel.
            Assert.IsFalse(FreightDispatchStaging.NeedsStagingTravel(true, false));
            Assert.IsFalse(FreightDispatchStaging.NeedsStagingTravel(true, true));
            // (away, open travel) -> false: the duplicate guard holds.
            Assert.IsFalse(FreightDispatchStaging.NeedsStagingTravel(false, true));
            // (away, no open travel) -> true: the only case that creates one.
            Assert.IsTrue(FreightDispatchStaging.NeedsStagingTravel(false, false));
        }

        /// <summary>
        /// W3 REPRODUCTION (H1/Note-4), now asserting the FIXED behaviour:
        /// Ethan's journey location reads as staging ("building:7") while
        /// his visual agent is idle elsewhere — no travel task is open. The
        /// fixed decision is TRUE: a travel task must still be created
        /// (TryStartExplicitRoute is a no-op success when already arrived,
        /// per the S5 precedent), and the duplicate guard (open travel
        /// task) is the only thing that may suppress it. Fail-first history:
        /// this test FAILED against the pre-W3 transcription (origin ==
        /// staging returned false) and passes once the early return is
        /// removed — the reproduction confirmed the trap before the fix.
        /// </summary>
        [Test]
        public void HitchTravel_OriginEqualsStaging_StillNeedsTravelTask()
        {
            Assert.IsTrue(
                FreightDispatchStaging.NeedsHitchStagingTravel(false, false, "building:7", "building:7"),
                "W3: journey location == staging with the visual agent away and no open travel task must still create a travel task — otherwise the hitch never starts.");
            Assert.IsTrue(
                FreightDispatchStaging.NeedsHitchStagingTravel(false, false, "farmstead-f1", "building:7"),
                "the ordinary away case still needs travel");
        }

        /// <summary>
        /// W3: after the fix the hitch decision is the dispatch decision —
        /// the origin is not an input to it. All four (visual, open-travel)
        /// combinations agree with NeedsStagingTravel whether the origin
        /// equals staging or not.
        /// </summary>
        [Test]
        public void HitchTravel_DecisionMatchesDispatchDecision_RegardlessOfOrigin()
        {
            foreach (string origin in new[] { "building:7", "farmstead-f1" })
            {
                Assert.AreEqual(FreightDispatchStaging.NeedsStagingTravel(true, false),
                    FreightDispatchStaging.NeedsHitchStagingTravel(true, false, origin, "building:7"));
                Assert.AreEqual(FreightDispatchStaging.NeedsStagingTravel(true, true),
                    FreightDispatchStaging.NeedsHitchStagingTravel(true, true, origin, "building:7"));
                Assert.AreEqual(FreightDispatchStaging.NeedsStagingTravel(false, true),
                    FreightDispatchStaging.NeedsHitchStagingTravel(false, true, origin, "building:7"));
                Assert.AreEqual(FreightDispatchStaging.NeedsStagingTravel(false, false),
                    FreightDispatchStaging.NeedsHitchStagingTravel(false, false, origin, "building:7"));
            }
        }

        [Test]
        public void CanCreateTravel_RequiresHomeBuilding()
        {
            Assert.IsFalse(FreightDispatchStaging.CanCreateTravel(-1));
            Assert.IsTrue(FreightDispatchStaging.CanCreateTravel(0));
            Assert.IsTrue(FreightDispatchStaging.CanCreateTravel(5));
        }

        [Test]
        public void TravelRef_IsStableAcrossSaveBoundary()
        {
            // The ref contains only the agreement + shipment ids: no task
            // id, no timestamp, nothing generated per run. A save/restore
            // re-run builds the identical ref from the same ids, so the
            // duplicate guard matches the same open task after a load.
            string beforeSave = FreightDispatchStaging.TravelCustomerRef(AgreementId, ShipmentId);
            string afterRestore = FreightDispatchStaging.TravelCustomerRef(AgreementId, ShipmentId);
            Assert.AreEqual(beforeSave, afterRestore);
            Assert.AreEqual("freight-dispatch:travel:freight-agreement:request-1:freight-shipment:request-1", afterRestore);
            Assert.IsTrue(FreightDispatchStaging.TryParseTravelRef(afterRestore, out string agreementId, out string shipmentId));
            Assert.AreEqual(AgreementId, agreementId);
            Assert.AreEqual(ShipmentId, shipmentId);
        }
    }
}
