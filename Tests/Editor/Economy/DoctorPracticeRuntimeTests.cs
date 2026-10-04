using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Doctor;
using LandLedgers.Primitives;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// W1A: the doctor practice runtime — medicine cabinet, house-call queue,
    /// and professional-time coverage accounting.
    /// </summary>
    [TestFixture]
    public sealed class DoctorPracticeRuntimeTests
    {
        private static JourneyModel NewJourney(float miles)
        {
            var journeys = new JourneyModel();
            journeys.RegisterLocation(new JourneyLocation("doctor-office", JourneyLocationKind.TownBuilding, "Doctor's Office", 0f, 0f));
            journeys.RegisterLocation(new JourneyLocation("household-7", JourneyLocationKind.TownBuilding, "Miller Household", miles, 0f));
            journeys.AddEdge("doctor-office", "household-7", miles, "mill road");
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
                    },
                },
            };
        }

        [Test]
        public void QueueAndExecute_EndToEnd_WithCoverageAccounting()
        {
            var runtime = new DoctorPracticeRuntime("doc-biz-1");
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            DoctorMedicineBootstrap.ApplyBootstrapEndowment(runtime.MedicineStock, registry, 290, diag);

            Assert.Null(runtime.QueueHouseCall(SevereRequest(registry), diag));

            int completed = runtime.ExecuteQueuedHouseCalls("doctor-office", 300, NewJourney(6f), diag);

            Assert.AreEqual(1, completed);
            Assert.AreEqual(1, runtime.HouseCallService.CompletedInvoices.Count);
            HouseCallInvoice invoice = runtime.HouseCallService.CompletedInvoices[0];
            Assert.AreEqual(250 + 120 + 100, invoice.TotalCents, "No medicine requested: call-out + travel + treatment.");

            string summary = runtime.CoverageSummary();
            StringAssert.Contains("1 house calls", summary);
            StringAssert.Contains("0 call(s) still queued", summary);
        }

        [Test]
        public void ExecuteQueued_StopsWhenProfessionalDayExhausted_QueueStaysVisible()
        {
            var runtime = new DoctorPracticeRuntime("doc-biz-1");
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            // A full office day first, then two house calls (each 12 mi round
            // trip = 103 travel min + 60 bedside = 163 min). 450 + 163 = 613
            // exhausts the 600-minute professional day after one call.
            runtime.RecordOfficeMinutes(15, 15 * DoctorTreatmentCatalog.OfficeVisitMinutes, diag);
            Assert.Null(runtime.QueueHouseCall(SevereRequest(registry), diag));
            Assert.Null(runtime.QueueHouseCall(SevereRequest(registry), diag));

            int completed = runtime.ExecuteQueuedHouseCalls("doctor-office", 300, NewJourney(6f), diag);

            Assert.AreEqual(1, completed, "Only one call fits in the remaining minutes.");
            Assert.AreEqual(1, runtime.HouseCallService.Queue.Count,
                "The unrun call stays queued and visible — never silently dropped.");
        }

        [Test]
        public void SaveLoad_RoundTripsStockFeesAndQueue()
        {
            var runtime = new DoctorPracticeRuntime("doc-biz-1");
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            DoctorMedicineBootstrap.ApplyBootstrapEndowment(runtime.MedicineStock, registry, 290, diag);
            runtime.SetFeeSchedule(new DoctorFeeSchedule(150, 300, 120, 12, 30, 200));
            Assert.Null(runtime.QueueHouseCall(SevereRequest(registry), diag));

            var restored = new DoctorPracticeRuntime("doc-biz-1");
            restored.LoadFromSaveDto(runtime.CaptureSaveDto());

            Assert.AreEqual(150, restored.FeeSchedule.OfficeVisitFeeCents, "Custom fees must survive.");
            Assert.AreEqual(DoctorMedicineBootstrap.BootstrapRemedyDoses,
                restored.MedicineStock.DosesOnHand(DoctorTreatmentCatalog.MedicineDoseItemId),
                "Cabinet stock must survive.");
            Assert.AreEqual(1, restored.HouseCallService.Queue.Count, "The queue must survive.");
            Assert.AreEqual("household-7", restored.HouseCallService.Queue[0].PatientHouseholdId);
        }
    }
}
