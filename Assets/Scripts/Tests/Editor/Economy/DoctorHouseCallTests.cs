using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Doctor;
using LandLedgers.Primitives;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W1A: house calls travel real JRN routes (both ways), treat patients
    /// individually, and invoice every line.
    /// </summary>
    [TestFixture]
    public sealed class DoctorHouseCallTests
    {
        private static JourneyModel NewJourney()
        {
            var journeys = new JourneyModel();
            journeys.RegisterLocation(new JourneyLocation("doctor-office", JourneyLocationKind.TownBuilding, "Doctor's Office", 0f, 0f));
            journeys.RegisterLocation(new JourneyLocation("household-7", JourneyLocationKind.TownBuilding, "Miller Household", 6f, 0f));
            journeys.AddEdge("doctor-office", "household-7", 6.0f, "mill road");
            return journeys;
        }

        private static HouseCallRequest SevereRequest(EntityIdRegistry registry)
        {
            return new HouseCallRequest
            {
                RequestId = registry.Allocate(EntityKind.Contract),
                DoctorBusinessId = "doc-biz-1",
                PatientHouseholdId = "household-7",
                PatientLocationId = "household-7",
                RequestDayIndex = 300,
                Patients = new List<HouseCallPatient>
                {
                    new HouseCallPatient
                    {
                        PatientPersonId = EntityId.For(EntityKind.Person, 701),
                        Ailment = "severe fever",
                        Severity = HouseCallSeverity.Severe,
                        MedicineName = DoctorTreatmentCatalog.MedicineDoseItemId,
                        DosesNeeded = 2,
                    },
                },
            };
        }

        [Test]
        public void QueueHouseCall_TriageRefusesMinorAndModerate()
        {
            var service = new DoctorHouseCallService();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            var minor = SevereRequest(registry);
            minor.Patients[0].Severity = HouseCallSeverity.Minor;
            Assert.NotNull(service.QueueHouseCall(minor, diag),
                "Minor cases route to office visits — no house call.");
            Assert.AreEqual(0, service.Queue.Count);

            var moderate = SevereRequest(registry);
            moderate.Patients[0].Severity = HouseCallSeverity.Moderate;
            Assert.NotNull(service.QueueHouseCall(moderate, diag),
                "Moderate cases route to office visits — no house call.");
            Assert.AreEqual(0, service.Queue.Count);

            var severe = SevereRequest(registry);
            Assert.Null(service.QueueHouseCall(severe, diag), "Severe cases earn a house call.");
            Assert.AreEqual(1, service.Queue.Count);

            var immobile = SevereRequest(registry);
            immobile.Patients[0].Severity = HouseCallSeverity.Immobile;
            Assert.Null(service.QueueHouseCall(immobile, diag), "Immobile cases earn a house call.");
            Assert.AreEqual(2, service.Queue.Count);
        }

        [Test]
        public void QueueHouseCall_RefusesAnonymousPatientsAndMissingLocation()
        {
            var service = new DoctorHouseCallService();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            var anonymous = SevereRequest(registry);
            anonymous.Patients[0].PatientPersonId = EntityId.Invalid;
            Assert.NotNull(service.QueueHouseCall(anonymous, diag), "No anonymous patients.");

            var nowhere = SevereRequest(registry);
            nowhere.PatientLocationId = string.Empty;
            Assert.NotNull(service.QueueHouseCall(nowhere, diag), "The doctor travels somewhere real.");
        }

        [Test]
        public void ExecuteHouseCall_TravelsBothWays_InvoicesEveryLine()
        {
            var service = new DoctorHouseCallService();
            var registry = new EntityIdRegistry();
            var stock = new DoctorMedicineStock();
            var diag = new List<string>();
            DoctorMedicineBootstrap.ApplyBootstrapEndowment(stock, registry, 290, diag);

            var fees = new DoctorFeeSchedule();
            var request = SevereRequest(registry);

            HouseCallInvoice invoice = service.ExecuteHouseCall(
                request, "doctor-office", fees, stock, 300, NewJourney(), diag);

            Assert.NotNull(invoice, string.Join("; ", diag));
            Assert.AreEqual(250, invoice.CallOutFeeCents);
            Assert.AreEqual(6f, invoice.TravelMiles);
            Assert.AreEqual(120, invoice.TravelFeeCents, "6 miles x 2 ways x 10c (traveling-practitioner economics).");
            Assert.AreEqual(103, invoice.TravelMinutes, "12 miles at 7 mph horseback, rounded.");
            Assert.AreEqual(1, invoice.Treatments.Count, "Per-patient treatment — never a batch.");
            Assert.AreEqual(100, invoice.Treatments[0].WorkFeeCents);
            Assert.AreEqual(50, invoice.Treatments[0].MedicineChargeCents, "2 doses x 25c.");
            Assert.AreEqual(1, invoice.Treatments[0].MedicineLotsUsed.Count, "Dose provenance recorded.");
            StringAssert.Contains("BOOTSTRAP", invoice.Treatments[0].MedicineLotsUsed[0].ProvenanceChain);
            Assert.AreEqual(520, invoice.TotalCents, "250 + 120 + 100 + 50.");
            Assert.AreEqual("treated (recovery not guaranteed)", invoice.Treatments[0].Outcome,
                "Canon §13.3B: treatment never guarantees recovery.");
            Assert.AreEqual(0, service.Queue.Count, "Executed calls leave the queue.");
        }

        [Test]
        public void ExecuteHouseCall_NoRoute_RefusesLoudly()
        {
            var service = new DoctorHouseCallService();
            var registry = new EntityIdRegistry();
            var stock = new DoctorMedicineStock();
            var diag = new List<string>();
            DoctorMedicineBootstrap.ApplyBootstrapEndowment(stock, registry, 290, diag);

            var unreachable = SevereRequest(registry);
            unreachable.PatientLocationId = "far-side-of-nowhere";

            HouseCallInvoice invoice = service.ExecuteHouseCall(
                unreachable, "doctor-office", new DoctorFeeSchedule(), stock, 300, NewJourney(), diag);

            Assert.Null(invoice, "No route, no call.");
            Assert.IsTrue(diag.Count > 0, "The refusal must be loud.");
        }

        [Test]
        public void ExecuteHouseCall_MedicineShortfall_PatientUntreated_NotPaperedOver()
        {
            var service = new DoctorHouseCallService();
            var registry = new EntityIdRegistry();
            var emptyStock = new DoctorMedicineStock(); // no lots at all
            var diag = new List<string>();

            HouseCallInvoice invoice = service.ExecuteHouseCall(
                SevereRequest(registry), "doctor-office", new DoctorFeeSchedule(),
                emptyStock, 300, NewJourney(), diag);

            Assert.NotNull(invoice, "The call still happens; the patient is honestly recorded.");
            Assert.AreEqual("untreated", invoice.Treatments[0].Outcome);
            Assert.AreEqual(0, invoice.Treatments[0].WorkFeeCents, "No work billed for an untreated patient.");
            Assert.AreEqual(0, invoice.Treatments[0].MedicineChargeCents);
            Assert.IsTrue(diag.Count > 0, "The shortfall must be loud.");
        }
    }
}
