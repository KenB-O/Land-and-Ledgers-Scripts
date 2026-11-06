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
