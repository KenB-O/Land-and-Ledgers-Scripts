using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Doctor;
using LandLedgers.Primitives;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// D1A: house-call coverage depth — worst-severity-first ordering, queue
    /// aging with honest deferred outcomes, the "later special cases" triage
    /// hook, and the transport gate (Canon §13.3A/B).
    /// </summary>
    [TestFixture]
    public sealed class DoctorHouseCallCoverageTests
    {
        private static JourneyModel NewJourney()
        {
            var journeys = new JourneyModel();
            journeys.RegisterLocation(new JourneyLocation("doctor-office", JourneyLocationKind.TownBuilding, "Doctor's Office", 0f, 0f));
            journeys.RegisterLocation(new JourneyLocation("household-7", JourneyLocationKind.TownBuilding, "Miller Household", 6f, 0f));
            journeys.AddEdge("doctor-office", "household-7", 6.0f, "mill road");
            return journeys;
        }

        private static HouseCallRequest Request(EntityIdRegistry registry, string household, HouseCallSeverity severity, int day)
        {
            return new HouseCallRequest
            {
                RequestId = registry.Allocate(EntityKind.Contract),
                DoctorBusinessId = "doc-biz-1",
                PatientHouseholdId = household,
                PatientLocationId = "household-7",
                RequestDayIndex = day,
                Patients = new List<HouseCallPatient>
                {
                    new HouseCallPatient
                    {
                        PatientPersonId = EntityId.For(EntityKind.Person, 700 + day),
                        Ailment = "test ailment",
                        Severity = severity,
                    },
                },
            };
        }

        [Test]
        public void DequeueNextPrioritized_WorstSeverityFirst_ThenEarliestDay()
        {
            var service = new DoctorHouseCallService();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            service.QueueHouseCall(Request(registry, "hh-severe-late", HouseCallSeverity.Severe, 305), diag);
            service.QueueHouseCall(Request(registry, "hh-immobile", HouseCallSeverity.Immobile, 306), diag);
            service.QueueHouseCall(Request(registry, "hh-severe-early", HouseCallSeverity.Severe, 301), diag);

            Assert.AreEqual("hh-immobile", service.DequeueNextPrioritized().PatientHouseholdId,
                "Immobile outranks severe (Canon §13.3B immediate attention).");
            Assert.AreEqual("hh-severe-early", service.DequeueNextPrioritized().PatientHouseholdId,
                "FIFO is preserved within a severity band.");
            Assert.AreEqual("hh-severe-late", service.DequeueNextPrioritized().PatientHouseholdId);
            Assert.Null(service.DequeueNextPrioritized(), "Empty queue returns null.");
        }

        [Test]
        public void QueueHouseCall_SpecialCase_QualifiesBelowSevere()
        {
            var service = new DoctorHouseCallService();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();

            var request = Request(registry, "hh-special", HouseCallSeverity.Moderate, 300);
            request.Patients[0].SpecialCase = "childbirth-complication"; // modeled elsewhere; the tag qualifies

            Assert.Null(service.QueueHouseCall(request, diag),
                "A named special case qualifies under Canon §13.3 'later special cases'.");
            Assert.AreEqual(1, service.Queue.Count);
        }

        [Test]
        public void AgeQueue_WaitedOut_WentUntreated_WhenNoOffMapCare()
        {
            var service = new DoctorHouseCallService();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();
            service.QueueHouseCall(Request(registry, "hh-waiting", HouseCallSeverity.Severe, 300), diag);

            int agedOut = service.AgeQueue(310, 5, offMapCareReachable: false, diag);

            Assert.AreEqual(1, agedOut);
            Assert.AreEqual(0, service.Queue.Count);
            Assert.AreEqual(1, service.DeferredOutcomes.Count);
            Assert.AreEqual(DeferredPatientResolution.WentUntreated, service.DeferredOutcomes[0].Resolution);
            Assert.AreEqual(300, service.DeferredOutcomes[0].RequestDayIndex);
            Assert.IsTrue(diag.Count > 0, "Aging out must be loud.");
        }

        [Test]
        public void AgeQueue_WaitedOut_SoughtOffMapCare_WhenReachable()
        {
            var service = new DoctorHouseCallService();
            var registry = new EntityIdRegistry();
            var diag = new List<string>();
            service.QueueHouseCall(Request(registry, "hh-waiting", HouseCallSeverity.Severe, 300), diag);
            service.QueueHouseCall(Request(registry, "hh-fresh", HouseCallSeverity.Severe, 309), diag);

            int agedOut = service.AgeQueue(310, 5, offMapCareReachable: true, diag);

            Assert.AreEqual(1, agedOut);
            Assert.AreEqual(1, service.Queue.Count, "The fresh request keeps waiting.");
            Assert.AreEqual(DeferredPatientResolution.SoughtOffMapCare, service.DeferredOutcomes[0].Resolution);
        }

        [Test]
        public void Runtime_AgeHouseCallQueue_UntreatedAging_CostsTrust()
        {
            var runtime = new DoctorPracticeRuntime("doc-biz-1");
            var registry = new EntityIdRegistry();
            var diag = new List<string>();
            runtime.QueueHouseCall(Request(registry, "hh-waiting", HouseCallSeverity.Severe, 300), diag);
            int trustBefore = runtime.Reputation.Trust;

            int agedOut = runtime.AgeHouseCallQueue(310, 5, offMapCareReachable: false, diag);

            Assert.AreEqual(1, agedOut);
            Assert.Less(runtime.Reputation.Trust, trustBefore,
                "Patients who wait out and go untreated weaken trust (Canon §13.3E).");
        }

        [Test]
        public void Runtime_TransportNotReady_HouseCallsHeld_QueueStays()
        {
            var runtime = new DoctorPracticeRuntime("doc-biz-1");
            var registry = new EntityIdRegistry();
            var diag = new List<string>();
            runtime.QueueHouseCall(Request(registry, "hh-waiting", HouseCallSeverity.Severe, 300), diag);
            runtime.TransportReadyForHouseCalls = false;
            runtime.TransportNotes = "horse lame";

            int completed = runtime.ExecuteQueuedHouseCalls("doctor-office", 300, NewJourney(), diag);

            Assert.AreEqual(0, completed);
            Assert.AreEqual(1, runtime.HouseCallService.Queue.Count, "Calls stay queued, never silently dropped.");
            Assert.IsTrue(diag.Count > 0, "The hold must be loud and name the cause.");
        }

        [Test]
        public void Runtime_ExecuteHouseCalls_DeterministicOutcomesPerDay()
        {
            HouseCallInvoice first = RunOneCall("doc-biz-1", 300);
            HouseCallInvoice second = RunOneCall("doc-biz-1", 300);

            Assert.AreEqual(first.Treatments[0].OutcomeTier, second.Treatments[0].OutcomeTier);
            Assert.AreEqual(first.Treatments[0].OutcomeDaysReduced, second.Treatments[0].OutcomeDaysReduced,
                "Same practice, same day → same outcomes (deterministic seed).");
        }

        private static HouseCallInvoice RunOneCall(string businessId, int dayIndex)
        {
            var runtime = new DoctorPracticeRuntime(businessId);
            var registry = new EntityIdRegistry();
            var diag = new List<string>();
            DoctorMedicineBootstrap.ApplyBootstrapEndowment(runtime.MedicineStock, registry, dayIndex - 10, diag);
            runtime.QueueHouseCall(Request(registry, "hh-7", HouseCallSeverity.Severe, dayIndex), diag);
            runtime.ExecuteQueuedHouseCalls("doctor-office", dayIndex, NewJourney(), diag);
            return runtime.HouseCallService.CompletedInvoices[0];
        }

        [Test]
        public void Runtime_ExecuteHouseCalls_SevereServedBeforeLaterSevere()
        {
            var runtime = new DoctorPracticeRuntime("doc-biz-1");
            var registry = new EntityIdRegistry();
            var diag = new List<string>();
            DoctorMedicineBootstrap.ApplyBootstrapEndowment(runtime.MedicineStock, registry, 290, diag);

            // Two calls; only minutes for one (each call: 103 travel min + 60 bedside = 163 min).
            // Burn most of the day first so only the highest-urgency call runs.
            runtime.RecordOfficeMinutes(0, 500, diag);
            runtime.QueueHouseCall(Request(registry, "hh-severe", HouseCallSeverity.Severe, 300), diag);
            runtime.QueueHouseCall(Request(registry, "hh-immobile", HouseCallSeverity.Immobile, 300), diag);

            int completed = runtime.ExecuteQueuedHouseCalls("doctor-office", 300, NewJourney(), diag);

            Assert.AreEqual(1, completed, "Only one call fits in the remaining minutes.");
            Assert.AreEqual("hh-immobile", runtime.HouseCallService.CompletedInvoices[0].PatientHouseholdId,
                "Under capacity pressure the most urgent call runs first (Canon §13.3B).");
            Assert.AreEqual(1, runtime.HouseCallService.Queue.Count);
        }
    }
}
