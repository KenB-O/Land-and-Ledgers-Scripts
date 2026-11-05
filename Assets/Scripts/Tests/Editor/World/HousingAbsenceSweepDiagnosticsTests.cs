using System;
using System.Collections.Generic;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.World
{
    /// <summary>
    /// Overnight 2026-11-04 slice 5 (console cleanup): the absence sweep
    /// used to hand the daily driver a non-empty diagnostic list on every
    /// single sweep — an unconditional summary line, plus one line per
    /// exempted person — so the driver warned every day even when nobody
    /// was absent and nothing had changed. Routine no-change sweeps must
    /// now stay silent, while real changes and real failures still speak.
    /// </summary>
    [TestFixture]
    public sealed class HousingAbsenceSweepDiagnosticsTests
    {
        private sealed class Fixture
        {
            public HousingAuthority Housing = new HousingAuthority();
            public List<string> Diag = new List<string>();

            public string BuildingWithSpace(string buildingId, string parcelId, int capacity)
            {
                Housing.RegisterBuilding(buildingId, parcelId, "house", 0, "test", Diag);
                AccommodationSpace space = Housing.DefineSpace(buildingId, capacity, Diag);
                Assert.IsNotNull(space);
                return space.SpaceId;
            }
        }

        [Test]
        public void NoChange_SweepEmitsNoDiagnostics()
        {
            var f = new Fixture();
            string spaceId = f.BuildingWithSpace("b-1", "p-1", 2);
            Assert.IsNull(f.Housing.TryOccupyUnderAgreement(2, 10, spaceId, AccommodationArrangement.Rental,
                0, string.Empty, f.Diag, out _));

            var sweepDiag = new List<string>();
            HousingAbsenceSweepResult result = new HousingAbsenceSweep().Sweep(
                f.Housing, personId => false, 10, sweepDiag);

            Assert.AreEqual(0, result.PersonsResolved);
            Assert.AreEqual(0, result.OccupanciesEnded);
            Assert.AreEqual(0, sweepDiag.Count, "A sweep that changes nothing must not hand the driver a warning.");
        }

        [Test]
        public void ExemptedAbsence_EmitsNoDiagnostics()
        {
            var f = new Fixture();
            string spaceId = f.BuildingWithSpace("b-1", "p-1", 2);
            PropertyAgreement agreement = f.Housing.RecordAgreement("boarding", "House", "P1",
                spaceId, "prepaid weekly", 0, -1, string.Empty, f.Diag);
            Assert.IsNull(f.Housing.TryOccupyUnderAgreement(1, 10, spaceId, AccommodationArrangement.BoardingOrLodging,
                0, agreement.AgreementId, f.Diag, out _));

            var sweepDiag = new List<string>();
            HousingAbsenceSweepResult result = new HousingAbsenceSweep().Sweep(
                f.Housing, personId => personId == 1, 10, sweepDiag,
                hasValidAbsenceArrangement: occ => HousingAbsenceSweep.AgreementStillStands(f.Housing, occ, 10));

            Assert.AreEqual(0, result.PersonsResolved);
            Assert.AreEqual(1, result.ExemptedPersonIds.Count);
            Assert.AreEqual(0, sweepDiag.Count,
                "A steady-state exemption is not a new event and must not warn every day.");
        }

        [Test]
        public void ResolvedAbsence_StillEmitsChangeDiagnostics()
        {
            var f = new Fixture();
            string spaceId = f.BuildingWithSpace("b-1", "p-1", 2);
            Assert.IsNull(f.Housing.TryOccupyUnderAgreement(1, 10, spaceId, AccommodationArrangement.Rental,
                0, string.Empty, f.Diag, out _));

            var sweepDiag = new List<string>();
            HousingAbsenceSweepResult result = new HousingAbsenceSweep().Sweep(
                f.Housing, personId => personId == 1, 10, sweepDiag);

            Assert.AreEqual(1, result.PersonsResolved);
            Assert.AreEqual(1, result.OccupanciesEnded);
            Assert.Greater(sweepDiag.Count, 0, "A real change must still be diagnosable.");
        }

        [Test]
        public void PredicateThrow_StillSurfacedAsDiagnostic()
        {
            var f = new Fixture();
            string spaceId = f.BuildingWithSpace("b-1", "p-1", 2);
            Assert.IsNull(f.Housing.TryOccupyUnderAgreement(1, 10, spaceId, AccommodationArrangement.Rental,
                0, string.Empty, f.Diag, out _));

            var sweepDiag = new List<string>();
            HousingAbsenceSweepResult result = new HousingAbsenceSweep().Sweep(
                f.Housing, personId => throw new InvalidOperationException("boom"), 10, sweepDiag);

            Assert.AreEqual(0, result.PersonsResolved, "A throwing predicate must never resolve by assumption.");
            Assert.Greater(sweepDiag.Count, 0, "A real failure must still surface.");
        }
    }
}
