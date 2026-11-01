using System;
using System.Collections.Generic;
using LandLedgers.World.Journeys;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.EditorTests.World
{
    /// <summary>
    /// Pass VIII Worker 7: production-authority proofs for the lenient side of
    /// the HousingAbsenceSweep boundary.
    /// - A person on a legitimate multi-day trip (an active off-map journey
    ///   WITH an expected return: freight run, buying trip, family travel)
    ///   keeps their bed under the lenient policy; the strict sweep ends it.
    /// - A one-way migration (no return expected) is not a valid arrangement.
    /// - Once the journey completes, the lenient exemption lapses.
    /// - An overdue return is not a held arrangement.
    /// - Death is never a valid arrangement, even mid-journey.
    /// - A standing agreement still holds the bed (the pre-existing helper,
    ///   exercised through the composed policy).
    /// </summary>
    [TestFixture]
    public sealed class Pass8LenientAbsenceArrangementTests
    {
        private sealed class Fixture
        {
            public HousingAuthority Housing = new HousingAuthority();
            public List<string> Diag = new List<string>();
            public JourneyModel Model;
            public OffMapJourneyAuthority Journeys;

            public Fixture()
            {
                Model = new JourneyModel();
                Model.RegisterLocation(new JourneyLocation("mill-site", JourneyLocationKind.Mill, "Grist Mill", 0.5f, 3f));
                Model.RegisterLocation(new JourneyLocation("town-depot", JourneyLocationKind.OffMapGateway, "Rail Depot", 0f, 0f));
                Model.AddEdge("mill-site", "town-depot", 3f, "depot road");
                Journeys = new OffMapJourneyAuthority(Model);
            }

            public string Space(int capacity = 2)
            {
                Housing.RegisterBuilding("b-1", "p-1", "house", 0, "test", Diag);
                AccommodationSpace space = Housing.DefineSpace("b-1", capacity, Diag);
                Assert.IsNotNull(space);
                return space.SpaceId;
            }

            public void Occupy(int personId, int householdId, string spaceId, string agreementId = "")
            {
                string refusal = Housing.TryOccupyUnderAgreement(personId, householdId, spaceId,
                    AccommodationArrangement.Rental, 0, agreementId, Diag, out _);
                Assert.IsNull(refusal, "occupancy should succeed: " + string.Join("; ", Diag));
            }

            public OffMapJourneyRecord DepartPerson(int personId, int day, int returnInDays)
            {
                // expectedReturnDays < 0 = one-way migration (no return expected).
                OffMapJourneyRecord record = Journeys.DepartPerson(
                    personId, "mill-site", "off-map destination", 2, returnInDays,
                    "test trip", null, null, day, item => 0, null, Diag);
                Assert.IsNotNull(record, "departure should succeed: " + string.Join("; ", Diag));
                return record;
            }
        }

        [Test]
        public void LegitimateReturnJourney_KeepsTheBedUnderLenientPolicy()
        {
            var sweep = new HousingAbsenceSweep();

            // Strict: the traveler's bed is lost.
            var strictFixture = new Fixture();
            string strictSpace = strictFixture.Space();
            strictFixture.Occupy(1, 10, strictSpace);
            strictFixture.DepartPerson(1, 5, 10); // absent, on a 10-day freight run
            HousingAbsenceSweepResult strict = sweep.Sweep(
                strictFixture.Housing, personId => personId == 1, 6, strictFixture.Diag);
            Assert.AreEqual(1, strict.OccupanciesEnded, "strict policy ends the occupancy");

            // Lenient: the same absence keeps the bed.
            var f = new Fixture();
            string spaceId = f.Space();
            f.Occupy(1, 10, spaceId);
            f.DepartPerson(1, 5, 10); // absent, on a 10-day freight run
            HousingAbsenceSweepResult lenient = sweep.Sweep(
                f.Housing, personId => personId == 1, 6, f.Diag,
                hasValidAbsenceArrangement: AbsenceArrangementPolicies.LenientPolicy(
                    f.Housing, f.Journeys, 6));
            Assert.AreEqual(0, lenient.OccupanciesEnded);
            Assert.AreEqual(1, lenient.ExemptedPersonIds.Count);
            Assert.AreEqual(1, f.Housing.OccupantCount(spaceId), "the traveler's bed is held");
        }

        [Test]
        public void OneWayMigration_IsNotAValidArrangement()
        {
            var f = new Fixture();
            string spaceId = f.Space();
            f.Occupy(1, 10, spaceId);
            // One-way: expectedReturnDays negative = no return expected.
            f.DepartPerson(1, 5, -1);

            var sweep = new HousingAbsenceSweep();
            HousingAbsenceSweepResult result = sweep.Sweep(
                f.Housing, personId => personId == 1, 6, f.Diag,
                hasValidAbsenceArrangement: AbsenceArrangementPolicies.LenientPolicy(
                    f.Housing, f.Journeys, 6));
            Assert.AreEqual(1, result.OccupanciesEnded, "a permanent departure frees the bed");
            Assert.AreEqual(0, f.Housing.OccupantCount(spaceId));
        }

        [Test]
        public void CompletedJourney_LenientExemptionLapses()
        {
            var f = new Fixture();
            string spaceId = f.Space();
            f.Occupy(1, 10, spaceId);
            OffMapJourneyRecord record = f.DepartPerson(1, 5, 10);
            int dueDay = record.DueReturnDayIndex;
            Assert.Greater(dueDay, 5);

            // Complete the journey (the traveler returned).
            var done = f.Journeys.CompleteDueJourneys(dueDay, (item, units) => { }, f.Diag);
            Assert.AreEqual(1, done.Count);

            // The traveler is still "absent" per the caller, but no journey is
            // active anymore: leniency no longer holds the bed.
            var sweep = new HousingAbsenceSweep();
            HousingAbsenceSweepResult result = sweep.Sweep(
                f.Housing, personId => personId == 1, dueDay + 1, f.Diag,
                hasValidAbsenceArrangement: AbsenceArrangementPolicies.LenientPolicy(
                    f.Housing, f.Journeys, dueDay + 1));
            Assert.AreEqual(1, result.OccupanciesEnded, "a finished trip holds no bed");
        }

        [Test]
        public void OverdueReturn_IsNotAHeldArrangement()
        {
            var f = new Fixture();
            string spaceId = f.Space();
            f.Occupy(1, 10, spaceId);
            OffMapJourneyRecord record = f.DepartPerson(1, 5, 10);
            int dueDay = record.DueReturnDayIndex;

            // Sweep well after the due return with the journey still active:
            // an overdue trip is not a valid arrangement.
            var sweep = new HousingAbsenceSweep();
            HousingAbsenceSweepResult result = sweep.Sweep(
                f.Housing, personId => personId == 1, dueDay + 30, f.Diag,
                hasValidAbsenceArrangement: AbsenceArrangementPolicies.LenientPolicy(
                    f.Housing, f.Journeys, dueDay + 30));
            Assert.AreEqual(1, result.OccupanciesEnded, "an overdue trip holds no bed");
        }

        [Test]
        public void DeathIsNeverAValidArrangement_EvenMidJourney()
        {
            var f = new Fixture();
            string spaceId = f.Space();
            f.Occupy(1, 10, spaceId);
            f.DepartPerson(1, 5, 10);

            var sweep = new HousingAbsenceSweep();
            HousingAbsenceSweepResult result = sweep.Sweep(
                f.Housing, personId => personId == 1, 6, f.Diag,
                hasValidAbsenceArrangement: AbsenceArrangementPolicies.LenientPolicy(
                    f.Housing, f.Journeys, 6, isDeceased: personId => personId == 1));
            Assert.AreEqual(1, result.OccupanciesEnded, "the dead keep no bed, journey or not");
        }

        [Test]
        public void StandingAgreement_HoldsTheBedThroughTheComposedPolicy()
        {
            var f = new Fixture();
            string spaceId = f.Space();
            PropertyAgreement agreement = f.Housing.RecordAgreement("boarding", "House", "P1",
                spaceId, "prepaid weekly", 0, -1, string.Empty, f.Diag);
            f.Occupy(1, 10, spaceId, agreement.AgreementId);

            var sweep = new HousingAbsenceSweep();
            HousingAbsenceSweepResult result = sweep.Sweep(
                f.Housing, personId => personId == 1, 10, f.Diag,
                hasValidAbsenceArrangement: AbsenceArrangementPolicies.LenientPolicy(
                    f.Housing, f.Journeys, 10));
            Assert.AreEqual(0, result.OccupanciesEnded);
            Assert.AreEqual(1, f.Housing.OccupantCount(spaceId), "a standing agreement holds the bed");
        }
    }
}
