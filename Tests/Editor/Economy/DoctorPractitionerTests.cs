using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Doctor;
using LandLedgers.Primitives;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// D1A: the practitioner is the practice's professional capacity —
    /// Canon §13.3D: premises and records survive the doctor, capacity does not.
    /// </summary>
    [TestFixture]
    public sealed class DoctorPractitionerTests
    {
        private static DoctorPractitioner Practitioner(DoctorPractitionerStatus status, DoctorPractitionerSkill skill)
        {
            return new DoctorPractitioner
            {
                PractitionerId = EntityId.For(EntityKind.Person, 9001),
                DisplayName = "Dr. Hart",
                Skill = skill,
                Status = status,
                StatusDayIndex = 300,
            };
        }

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
                    },
                },
            };
        }

        [Test]
        public void EffectiveMinutes_ActivePractitioner_FullDay()
        {
            var runtime = new DoctorPracticeRuntime("doc-biz-1");
            runtime.SetPractitioner(Practitioner(DoctorPractitionerStatus.Active, DoctorPractitionerSkill.Experienced), 300, new List<string>());

            Assert.AreEqual(DoctorPracticeRuntime.ProfessionalMinutesPerDay, runtime.EffectiveProfessionalMinutesPerDay);
        }

        [Test]
        public void EffectiveMinutes_NoPractitionerSet_PreservesPriorBehavior()
        {
            var runtime = new DoctorPracticeRuntime("doc-biz-1");

            Assert.AreEqual(DoctorPracticeRuntime.ProfessionalMinutesPerDay, runtime.EffectiveProfessionalMinutesPerDay,
                "Practices created before the practitioner model keep their capacity until someone seats one.");
        }

        [Test]
        public void EffectiveMinutes_IncapacitatedOrGone_NoProfessionalCapacity()
        {
            var runtime = new DoctorPracticeRuntime("doc-biz-1");
            var diag = new List<string>();

            foreach (var status in new[]
            {
                DoctorPractitionerStatus.Incapacitated,
                DoctorPractitionerStatus.Departed,
                DoctorPractitionerStatus.Deceased,
            })
            {
                runtime.SetPractitioner(Practitioner(status, DoctorPractitionerSkill.Competent), 301, diag);
                Assert.AreEqual(0, runtime.EffectiveProfessionalMinutesPerDay,
                    $"Status {status}: premises and records remain, professional capacity is gone (Canon §13.3D).");
            }
        }

        [Test]
        public void ExecuteQueuedHouseCalls_NoAvailablePractitioner_CallsStayQueued()
        {
            var runtime = new DoctorPracticeRuntime("doc-biz-1");
            var registry = new EntityIdRegistry();
            var diag = new List<string>();
            runtime.SetPractitioner(Practitioner(DoctorPractitionerStatus.Incapacitated, DoctorPractitionerSkill.Competent), 300, diag);
            runtime.QueueHouseCall(SevereRequest(registry), diag);

            int completed = runtime.ExecuteQueuedHouseCalls("doctor-office", 300, NewJourney(), diag);

            Assert.AreEqual(0, completed);
            Assert.AreEqual(1, runtime.HouseCallService.Queue.Count, "The queue stays visible, never silently dropped.");
            Assert.IsTrue(diag.Count > 0, "The refusal must be loud.");
        }

        [Test]
        public void PractitionerSkill_FlowsIntoHouseCallOutcomes()
        {
            var runtime = new DoctorPracticeRuntime("doc-biz-1");
            var registry = new EntityIdRegistry();
            var diag = new List<string>();
            // Stock the runtime's own cabinet via the bootstrap endowment.
            DoctorMedicineBootstrap.ApplyBootstrapEndowment(runtime.MedicineStock, registry, 290, diag);

            runtime.SetPractitioner(Practitioner(DoctorPractitionerStatus.Active, DoctorPractitionerSkill.Experienced), 300, diag);
            runtime.QueueHouseCall(SevereRequest(registry), diag);

            int completed = runtime.ExecuteQueuedHouseCalls("doctor-office", 300, NewJourney(), diag);

            Assert.AreEqual(1, completed);
            var invoice = runtime.HouseCallService.CompletedInvoices[0];
            Assert.AreEqual(1, invoice.Treatments.Count);
            Assert.AreNotEqual(DoctorOutcomeTier.Untreated, invoice.Treatments[0].OutcomeTier,
                "A treated patient gets a resolved outcome, not a placeholder.");
            Assert.IsNotEmpty(invoice.Treatments[0].OutcomeSummary);
            Assert.AreNotEqual(50, runtime.Reputation.Trust, "Outcomes move trust (Canon §13.3E).");
        }

        [Test]
        public void PractitionerSearch_BeforeMaxDays_StillSearching()
        {
            var request = new PractitionerSearchRequest
            {
                RequestId = new EntityIdRegistry().Allocate(EntityKind.Contract),
                BusinessInstanceId = "doc-biz-1",
                RequiredSkill = DoctorPractitionerSkill.Competent,
                DayStarted = 300,
                MaxSearchDays = 30,
            };

            var result = DoctorPractitionerSearch.ResolveSearch(
                request, 310, new Random(5), out DoctorPractitionerSkill found, new List<string>());

            Assert.AreEqual(PractitionerSearchResult.StillSearching, result);
        }

        [Test]
        public void PractitionerSearch_AfterMaxDays_ResolvesDeterministically()
        {
            var registry = new EntityIdRegistry();
            var request = new PractitionerSearchRequest
            {
                RequestId = registry.Allocate(EntityKind.Contract),
                BusinessInstanceId = "doc-biz-1",
                RequiredSkill = DoctorPractitionerSkill.Experienced,
                DayStarted = 300,
                MaxSearchDays = 30,
            };

            var first = DoctorPractitionerSearch.ResolveSearch(request, 331, new Random(9), out var skillA, new List<string>());
            var second = DoctorPractitionerSearch.ResolveSearch(request, 331, new Random(9), out var skillB, new List<string>());

            Assert.AreEqual(first, second, "Deterministic given the seed.");
            Assert.AreNotEqual(PractitionerSearchResult.StillSearching, first);
            if (first == PractitionerSearchResult.FoundBelowSkill)
            {
                Assert.GreaterOrEqual((int)skillA, (int)DoctorPractitionerSkill.Basic,
                    "A below-skill candidate is never below Basic.");
                Assert.Less((int)skillA, (int)DoctorPractitionerSkill.Experienced,
                    "Below-skill means below the required Experienced tier.");
            }
        }

        [Test]
        public void SaveLoad_PreservesPractitioner()
        {
            var runtime = new DoctorPracticeRuntime("doc-biz-1");
            runtime.SetPractitioner(Practitioner(DoctorPractitionerStatus.Active, DoctorPractitionerSkill.Experienced), 300, new List<string>());

            var dto = runtime.CaptureSaveDto();
            var restored = new DoctorPracticeRuntime("doc-biz-1");
            restored.LoadFromSaveDto(dto);

            Assert.NotNull(restored.Practitioner);
            Assert.AreEqual(DoctorPractitionerSkill.Experienced, restored.Practitioner.Skill);
            Assert.AreEqual(DoctorPracticeRuntime.ProfessionalMinutesPerDay, restored.EffectiveProfessionalMinutesPerDay);
        }
    }
}
