using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Economy.AnimalServices;
using LandLedgers.Primitives;
using LandLedgers.World.Journeys;
using NUnit.Framework;

namespace LandLedgers.Editor.Economy
{
    /// <summary>
    /// D3E: veterinarian depth — herd-health visit programs (Canon §6.2 "group
    /// visits can matter"), authored preventive-care schedules (Canon §6.1),
    /// and data-only treatment-outcome tracking (Tech X §18.1 research hold:
    /// counts, never efficacy rates).
    /// </summary>
    [TestFixture]
    public sealed class VetHerdHealthTests
    {
        private static JourneyModel NewJourney()
        {
            var journeys = new JourneyModel();
            journeys.RegisterLocation(new JourneyLocation("vet-office", JourneyLocationKind.TownBuilding, "Vet Office", 0f, 0f));
            journeys.RegisterLocation(new JourneyLocation("farm-1", JourneyLocationKind.Farmstead, "Morrow Farm", 6f, 0f));
            journeys.AddEdge("vet-office", "farm-1", 6.0f, "river road");
            return journeys;
        }

        private static VetPractitioner NewVet()
        {
            return new VetPractitioner
            {
                PractitionerId = "vet-1",
                DisplayName = "Dr. Hart",
                PersonId = 42,
                LocationId = "vet-office",
                CallOutFeeCents = 200,
                PerMileTravelCents = 10,
                SpeciesServiced = new List<string> { "cow", "horse" },
            };
        }

        private static AnimalRegistry RegistryWithCattle(out List<AnimalState> animals, int head = 3)
        {
            var registry = new AnimalRegistry(new EntityIdRegistry());
            animals = new List<AnimalState>();
            for (int i = 0; i < head; i++)
            {
                AnimalState cow = registry.RegisterAnimal(AnimalSpecies.Cattle, "Shorthorn",
                    AnimalSex.Female, AnimalOwnerKind.Business, "farm-1", 0, "test");
                Assert.IsNotNull(cow);
                animals.Add(cow);
            }
            return registry;
        }

        private static VetHerdVisitSchedule NewSchedule()
        {
            return new VetHerdVisitSchedule
            {
                ScheduleId = "herd-1",
                FarmOrBusinessId = "farm-1",
                PractitionerId = "vet-1",
                IntervalDays = 90,
                NextDueDayIndex = 100,
                ExamFeePerAnimalCents = 25,
            };
        }

        private static List<(EntityId, string, string, string, int, int, string)> TreatmentCaseFor(EntityId animalId)
        {
            return new List<(EntityId, string, string, string, int, int, string)>
            {
                (animalId, "cow", "lame hoof", "trim and poultice", 100, 0, ""),
            };
        }

        // ---------- Herd visit schedules ----------

        [Test]
        public void HerdVisitSchedule_IsDueOn_AdvancesByAuthoredCadence()
        {
            var schedule = NewSchedule();
            Assert.IsFalse(schedule.IsDueOn(99));
            Assert.IsTrue(schedule.IsDueOn(100));

            Assert.IsNull(schedule.AdvanceToNext());
            Assert.AreEqual(190, schedule.NextDueDayIndex, "Cadence is the authored IntervalDays — never guessed by the engine.");
        }

        [Test]
        public void HerdVisitSchedule_AdvanceToNext_RefusesNonPositiveCadence()
        {
            var schedule = NewSchedule();
            schedule.IntervalDays = 0;
            Assert.IsNotNull(schedule.AdvanceToNext());
            Assert.AreEqual(100, schedule.NextDueDayIndex, "Bad cadence leaves the schedule unadvanced, not re-guessed.");
        }

        // ---------- Group visits ----------

        [Test]
        public void HerdVisit_ConductsOneCallOut_ExaminesEachAnimal_InvoicesLines()
        {
            AnimalRegistry registry = RegistryWithCattle(out List<AnimalState> cows);
            var service = new VetService();
            service.RegisterPractitioner(NewVet());
            var visitService = new VetHerdVisitService();
            var schedule = NewSchedule();

            var exams = new List<VetAnimalExamination>();
            foreach (AnimalState cow in cows)
            {
                exams.Add(new VetAnimalExamination { AnimalId = cow.AnimalId, Species = "cow", ExaminationNotes = "sound" });
            }

            var diag = new List<string>();
            VetHerdVisit visit = visitService.ConductVisit(service, schedule, "farm-1", "farm-1", exams,
                new List<VetCohortExamination>(), TreatmentCaseFor(cows[0].AnimalId),
                registry, 100, NewJourney(), new EntityIdRegistry(), diag);

            Assert.NotNull(visit, string.Join("; ", diag));
            Assert.AreEqual(3, visit.Examinations.Count, "Each animal examined individually — no herd-wide magic.");
            Assert.AreEqual(1, visit.Invoice.Treatments.Count, "Only the treated animal gets a treatment record.");
            Assert.AreEqual(visit.Invoice.Treatments[0].Sequence, visit.Examinations[0].TreatmentSequence,
                "The examination links to its treatment by sequence.");
            Assert.AreEqual(-1, visit.Examinations[1].TreatmentSequence, "Exam-only animals keep no treatment link.");
            Assert.AreEqual(200, visit.Invoice.CallOutFeeCents, "One call-out fee for the whole group visit.");
            Assert.AreEqual(120, visit.Invoice.TravelFeeCents, "One travel charge (both ways) for the group visit.");
            Assert.AreEqual(25, visit.Examinations[0].ExamFeeCents, "Authored per-animal exam fee stamped on each exam.");
            Assert.AreEqual(190, schedule.NextDueDayIndex, "The program advances after a conducted visit.");
        }

        [Test]
        public void HerdVisit_HonorsStandingAgreementFee()
        {
            AnimalRegistry registry = RegistryWithCattle(out List<AnimalState> cows);
            var service = new VetService();
            service.RegisterPractitioner(NewVet());
            service.RegisterAgreement(new VetServiceAgreement
            {
                AgreementId = EntityId.For(EntityKind.Contract, 1),
                FarmOrBusinessId = "farm-1",
                PractitionerId = "vet-1",
                StartDayIndex = 0,
                AgreedCallOutFeeCents = 100,
            });
            var visitService = new VetHerdVisitService();
            var schedule = NewSchedule();

            var diag = new List<string>();
            VetHerdVisit visit = visitService.ConductVisit(service, schedule, "farm-1", "farm-1",
                new List<VetAnimalExamination> { new VetAnimalExamination { AnimalId = cows[0].AnimalId, Species = "cow" } },
                null, new List<(EntityId, string, string, string, int, int, string)>(),
                registry, 100, NewJourney(), new EntityIdRegistry(), diag);

            Assert.NotNull(visit, string.Join("; ", diag));
            Assert.AreEqual(100, visit.Invoice.CallOutFeeCents, "The standing agreement's fee applies to group visits too.");
        }

        [Test]
        public void HerdVisit_RecordsCohortLevelWork()
        {
            AnimalRegistry registry = RegistryWithCattle(out List<AnimalState> cows);
            var service = new VetService();
            service.RegisterPractitioner(NewVet());
            var visitService = new VetHerdVisitService();
            var schedule = NewSchedule();

            var diag = new List<string>();
            VetHerdVisit visit = visitService.ConductVisit(service, schedule, "farm-1", "farm-1",
                new List<VetAnimalExamination> { new VetAnimalExamination { AnimalId = cows[0].AnimalId, Species = "cow" } },
                new List<VetCohortExamination>
                {
                    new VetCohortExamination
                    {
                        CohortId = EntityId.For(EntityKind.AnimalCohort, 7),
                        HeadExamined = 40,
                        CohortNotes = "walked the yearling bunch — generally thrifty, three pulled for foot rot",
                    },
                },
                new List<(EntityId, string, string, string, int, int, string)>(),
                registry, 100, NewJourney(), new EntityIdRegistry(), diag);

            Assert.NotNull(visit, string.Join("; ", diag));
            Assert.AreEqual(1, visit.CohortExaminations.Count);
            Assert.AreEqual(40, visit.CohortExaminations[0].HeadExamined);
            Assert.AreEqual(1, visit.Examinations.Count, "Individually handled animals still get per-animal records alongside the cohort note.");
        }

        [Test]
        public void HerdVisit_RefusesUnregisteredAnimals_Loudly()
        {
            AnimalRegistry registry = RegistryWithCattle(out List<AnimalState> cows);
            var service = new VetService();
            service.RegisterPractitioner(NewVet());
            var visitService = new VetHerdVisitService();
            var schedule = NewSchedule();

            var phantom = EntityId.For(EntityKind.Animal, 99999);
            var diag = new List<string>();
            VetHerdVisit visit = visitService.ConductVisit(service, schedule, "farm-1", "farm-1",
                new List<VetAnimalExamination>
                {
                    new VetAnimalExamination { AnimalId = cows[0].AnimalId, Species = "cow" },
                    new VetAnimalExamination { AnimalId = phantom, Species = "cow" },
                },
                null, new List<(EntityId, string, string, string, int, int, string)>(),
                registry, 100, NewJourney(), new EntityIdRegistry(), diag);

            Assert.NotNull(visit, string.Join("; ", diag));
            Assert.AreEqual(1, visit.Examinations.Count, "The phantom animal is not examined.");
            Assert.IsTrue(string.Join("; ", diag).Contains("not a registered animal"), "The refusal is loud.");
        }

        [Test]
        public void HerdVisit_RefusesVisit_WhenNoValidAnimals()
        {
            AnimalRegistry registry = RegistryWithCattle(out List<AnimalState> cows);
            var service = new VetService();
            service.RegisterPractitioner(NewVet());
            var visitService = new VetHerdVisitService();
            var schedule = NewSchedule();

            var diag = new List<string>();
            VetHerdVisit visit = visitService.ConductVisit(service, schedule, "farm-1", "farm-1",
                new List<VetAnimalExamination>
                {
                    new VetAnimalExamination { AnimalId = EntityId.For(EntityKind.Animal, 99999), Species = "cow" },
                },
                null, new List<(EntityId, string, string, string, int, int, string)>(),
                registry, 100, NewJourney(), new EntityIdRegistry(), diag);

            Assert.IsNull(visit, "No registered animals, no visit — the practitioner does not travel.");
        }

        [Test]
        public void HerdVisit_RefusesWhenNoPractitioner()
        {
            AnimalRegistry registry = RegistryWithCattle(out List<AnimalState> cows);
            var service = new VetService(); // no practitioner registered
            var visitService = new VetHerdVisitService();
            var schedule = NewSchedule();

            var diag = new List<string>();
            VetHerdVisit visit = visitService.ConductVisit(service, schedule, "farm-1", "farm-1",
                new List<VetAnimalExamination> { new VetAnimalExamination { AnimalId = cows[0].AnimalId, Species = "cow" } },
                null, new List<(EntityId, string, string, string, int, int, string)>(),
                registry, 100, NewJourney(), new EntityIdRegistry(), diag);

            Assert.IsNull(visit, "No practitioner, no route, no visit.");
        }

        // ---------- Preventive schedules ----------

        [Test]
        public void PreventiveSchedule_IsDueOn_AdvancesByAuthoredCadence()
        {
            var schedule = new VetPreventiveSchedule
            {
                ScheduleId = "prev-1",
                FarmOrBusinessId = "farm-1",
                Species = AnimalSpecies.Horse,
                PreventiveAction = "harness check",
                IntervalDays = 30,
                NextDueDayIndex = 50,
            };

            Assert.IsFalse(schedule.IsDueOn(49));
            Assert.IsTrue(schedule.IsDueOn(50));
            Assert.IsNull(schedule.AdvanceToNext());
            Assert.AreEqual(80, schedule.NextDueDayIndex);
        }

        [Test]
        public void PerformPreventive_RecordsEachAnimal_DispensesRealMedicine()
        {
            AnimalRegistry registry = RegistryWithCattle(out List<AnimalState> cows);
            var schedule = new VetPreventiveSchedule
            {
                ScheduleId = "prev-1",
                FarmOrBusinessId = "farm-1",
                Species = AnimalSpecies.Cattle,
                PreventiveAction = "condition observation",
                IntervalDays = 30,
                NextDueDayIndex = 50,
            };
            var ids = new EntityIdRegistry();
            var stock = new VetMedicineStock();
            var diag0 = new List<string>();
            Assert.IsNull(stock.ReceiveLot(new VetMedicineLot
            {
                LotId = ids.Allocate(EntityKind.Lot),
                MedicineName = "veterinary-dressings",
                Doses = 10,
                AcquiredDayIndex = 40,
                ImportOrderId = "import-7",
                OriginName = "eastern wholesale drug house",
            }, diag0));

            var service = new VetPreventiveService();
            var animalIds = new List<EntityId>();
            foreach (AnimalState cow in cows) animalIds.Add(cow.AnimalId);
            var diag = new List<string>();
            VetPreventiveEvent evt = service.PerformPreventive(schedule, animalIds,
                EntityId.Invalid, 0, "veterinary-dressings", 2, 42,
                stock, registry, 50, diag);

            Assert.NotNull(evt, string.Join("; ", diag));
            Assert.AreEqual(3, evt.AnimalIds.Count, "Every animal identified individually.");
            Assert.AreEqual(4, stock.DosesOnHand("veterinary-dressings"), "6 doses consumed from real stock (2 x 3 animals).");
            StringAssert.Contains("import order import-7", evt.MedicineProvenance, "Dose-by-dose provenance rides along.");
            Assert.AreEqual(80, schedule.NextDueDayIndex, "The schedule advances after performance.");
            Assert.AreEqual(1, service.EventHistory.Count);
        }

        [Test]
        public void PerformPreventive_Shortfall_RecordedUnmedicated_Loudly()
        {
            AnimalRegistry registry = RegistryWithCattle(out List<AnimalState> cows);
            var schedule = new VetPreventiveSchedule
            {
                ScheduleId = "prev-1",
                FarmOrBusinessId = "farm-1",
                Species = AnimalSpecies.Cattle,
                PreventiveAction = "condition observation",
                IntervalDays = 30,
                NextDueDayIndex = 50,
            };
            var stock = new VetMedicineStock(); // empty chest

            var service = new VetPreventiveService();
            var animalIds = new List<EntityId>();
            foreach (AnimalState cow in cows) animalIds.Add(cow.AnimalId);
            var diag = new List<string>();
            VetPreventiveEvent evt = service.PerformPreventive(schedule, animalIds,
                EntityId.Invalid, 0, "veterinary-dressings", 2, 42,
                stock, registry, 50, diag);

            Assert.NotNull(evt, string.Join("; ", diag), "The event is recorded — but honest about being unmedicated.");
            Assert.AreEqual("unmedicated — stock shortfall", evt.MedicineProvenance);
            Assert.IsTrue(string.Join("; ", diag).Contains("shortfall"), "The shortfall is loud; nothing is auto-ordered.");
        }

        [Test]
        public void PerformPreventive_SupportsCohortLevelObservation()
        {
            var registry = new AnimalRegistry(new EntityIdRegistry());
            var schedule = new VetPreventiveSchedule
            {
                ScheduleId = "prev-1",
                FarmOrBusinessId = "farm-1",
                Species = AnimalSpecies.Cattle,
                PreventiveAction = "hoof observation",
                IntervalDays = 30,
                NextDueDayIndex = 50,
            };

            var service = new VetPreventiveService();
            var diag = new List<string>();
            VetPreventiveEvent evt = service.PerformPreventive(schedule, null,
                EntityId.For(EntityKind.AnimalCohort, 7), 40, "", 0, 42,
                null, registry, 50, diag);

            Assert.NotNull(evt, string.Join("; ", diag));
            Assert.AreEqual(40, evt.CohortHeadCount, "Cohort-level preventive work is recorded at cohort scope.");
            Assert.AreEqual("unmedicated", evt.MedicineProvenance);
        }

        [Test]
        public void PerformPreventive_RefusesWhenScheduleNamesNoAction()
        {
            var registry = new AnimalRegistry(new EntityIdRegistry());
            var schedule = new VetPreventiveSchedule
            {
                ScheduleId = "prev-1",
                FarmOrBusinessId = "farm-1",
                PreventiveAction = "", // no authored regimen
            };

            var service = new VetPreventiveService();
            var diag = new List<string>();
            VetPreventiveEvent evt = service.PerformPreventive(schedule,
                new List<EntityId> { EntityId.For(EntityKind.Animal, 5) },
                EntityId.Invalid, 0, "", 0, 42, null, registry, 50, diag);

            Assert.IsNull(evt, "The engine never invents a medical regimen — the schedule must name the action.");
        }

        [Test]
        public void PerformPreventive_RefusesPhantomAnimals()
        {
            AnimalRegistry registry = RegistryWithCattle(out List<AnimalState> cows);
            var schedule = new VetPreventiveSchedule
            {
                ScheduleId = "prev-1",
                FarmOrBusinessId = "farm-1",
                Species = AnimalSpecies.Cattle,
                PreventiveAction = "grooming",
                IntervalDays = 30,
                NextDueDayIndex = 50,
            };

            var service = new VetPreventiveService();
            var diag = new List<string>();
            VetPreventiveEvent evt = service.PerformPreventive(schedule,
                new List<EntityId> { EntityId.For(EntityKind.Animal, 99999) },
                EntityId.Invalid, 0, "", 0, 42, null, registry, 50, diag);

            Assert.IsNull(evt, "No registered animals, nothing performed.");
        }

        [Test]
        public void PerformPreventive_CohortOnlyWithMedicine_RefusedUnmedicated()
        {
            var registry = new AnimalRegistry(new EntityIdRegistry());
            var schedule = new VetPreventiveSchedule
            {
                ScheduleId = "prev-1",
                FarmOrBusinessId = "farm-1",
                Species = AnimalSpecies.Cattle,
                PreventiveAction = "hoof observation",
                IntervalDays = 30,
                NextDueDayIndex = 50,
            };
            var ids = new EntityIdRegistry();
            var stock = new VetMedicineStock();
            var diag0 = new List<string>();
            Assert.IsNull(stock.ReceiveLot(new VetMedicineLot
            {
                LotId = ids.Allocate(EntityKind.Lot),
                MedicineName = "veterinary-dressings",
                Doses = 10,
                AcquiredDayIndex = 40,
                ImportOrderId = "import-7",
                OriginName = "eastern wholesale drug house",
            }, diag0));

            var service = new VetPreventiveService();
            var diag = new List<string>();
            VetPreventiveEvent evt = service.PerformPreventive(schedule, null,
                EntityId.For(EntityKind.AnimalCohort, 7), 40,
                "veterinary-dressings", 1, 42,
                stock, registry, 50, diag);

            Assert.NotNull(evt, string.Join("; ", diag));
            Assert.AreEqual("unmedicated — cohort observation cannot take per-animal medicine", evt.MedicineProvenance,
                "A cohort headcount alone is not doseable — no mass dosing invented.");
            Assert.AreEqual(10, stock.DosesOnHand("veterinary-dressings"), "No doses dispensed.");
        }

        [Test]
        public void HerdVisitService_SaveLoad_RoundTrip()
        {
            AnimalRegistry registry = RegistryWithCattle(out List<AnimalState> cows);
            var service = new VetService();
            service.RegisterPractitioner(NewVet());
            var visitService = new VetHerdVisitService();
            var schedule = NewSchedule();

            var diag = new List<string>();
            VetHerdVisit visit = visitService.ConductVisit(service, schedule, "farm-1", "farm-1",
                new List<VetAnimalExamination> { new VetAnimalExamination { AnimalId = cows[0].AnimalId, Species = "cow" } },
                null, new List<(EntityId, string, string, string, int, int, string)>(),
                registry, 100, NewJourney(), new EntityIdRegistry(), diag);

            Assert.NotNull(visit, string.Join("; ", diag));
            VetHerdVisitService.VetHerdVisitServiceSaveDto dto = visitService.CaptureSaveDto();
            var restored = new VetHerdVisitService();
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(1, restored.VisitHistory.Count);
            Assert.AreEqual(visit.VisitId, restored.VisitHistory[0].VisitId);
            Assert.AreEqual(1, restored.VisitHistory[0].Examinations.Count);
            Assert.AreEqual(visit.Invoice.TotalCents, restored.VisitHistory[0].Invoice.TotalCents);
        }

        [Test]
        public void PreventiveService_SaveLoad_RoundTrip()
        {
            AnimalRegistry registry = RegistryWithCattle(out List<AnimalState> cows);
            var schedule = new VetPreventiveSchedule
            {
                ScheduleId = "prev-1",
                FarmOrBusinessId = "farm-1",
                Species = AnimalSpecies.Cattle,
                PreventiveAction = "grooming",
                IntervalDays = 30,
                NextDueDayIndex = 50,
            };
            var service = new VetPreventiveService();
            var diag = new List<string>();
            var animalIds = new List<EntityId> { cows[0].AnimalId };
            service.PerformPreventive(schedule, animalIds, EntityId.Invalid, 0, "", 0, 42, null, registry, 50, diag);

            VetPreventiveService.VetPreventiveServiceSaveDto dto = service.CaptureSaveDto();
            var restored = new VetPreventiveService();
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(1, restored.EventHistory.Count);
            Assert.AreEqual("grooming", restored.EventHistory[0].PreventiveAction);
            Assert.AreEqual(cows[0].AnimalId, restored.EventHistory[0].AnimalIds[0]);
        }

        // ---------- Treatment outcomes (data only) ----------

        [Test]
        public void OutcomeLedger_RecordsOutcomes_RollsUpCounts_NotRates()
        {
            AnimalRegistry registry = RegistryWithCattle(out List<AnimalState> cows);
            var ledger = new VetTreatmentOutcomeLedger();
            var diag = new List<string>();

            Assert.IsNull(ledger.RecordOutcome(0, cows[0].AnimalId, "veterinary-remedies", "milk fever",
                VetOutcomeCode.Recovered, "up and eating, day 102", 102, "veterinarian", registry, diag));
            Assert.IsNull(ledger.RecordOutcome(1, cows[1].AnimalId, "veterinary-remedies", "milk fever",
                VetOutcomeCode.Died, "found down, day 103", 103, "farmer", registry, diag));
            Assert.IsNull(ledger.RecordOutcome(2, cows[2].AnimalId, "veterinary-remedies", "milk fever",
                VetOutcomeCode.Unknown, "lost track after visit", 104, "unattributed", registry, diag));

            VetOutcomeRollup rollup = ledger.GetRollup("veterinary-remedies", "milk fever");
            Assert.AreEqual(3, rollup.Treated);
            Assert.AreEqual(1, rollup.Recovered);
            Assert.AreEqual(1, rollup.Died);
            Assert.AreEqual(1, rollup.Unknown, "Unobserved stays unknown — ambiguity is never resolved into a guess.");
            StringAssert.Contains("no efficacy rate", VetOutcomeRollup.NoEfficacyClaimDisclaimer,
                "The rollup carries the hard doctrine: counts, not efficacy.");
        }

        [Test]
        public void OutcomeLedger_RollupIsScopedByMedicineAndAilment()
        {
            AnimalRegistry registry = RegistryWithCattle(out List<AnimalState> cows);
            var ledger = new VetTreatmentOutcomeLedger();
            var diag = new List<string>();

            ledger.RecordOutcome(0, cows[0].AnimalId, "remedy-a", "ailment-x",
                VetOutcomeCode.Recovered, "", 102, "vet", registry, diag);
            ledger.RecordOutcome(1, cows[1].AnimalId, "remedy-b", "ailment-x",
                VetOutcomeCode.Recovered, "", 102, "vet", registry, diag);

            Assert.AreEqual(1, ledger.GetRollup("remedy-a", "ailment-x").Treated);
            Assert.AreEqual(1, ledger.GetRollup("remedy-b", "ailment-x").Treated);
            Assert.AreEqual(0, ledger.GetRollup("remedy-a", "other-ailment").Treated);
        }

        [Test]
        public void OutcomeLedger_RefusesUnregisteredAnimal_AndOrphanObservation()
        {
            AnimalRegistry registry = RegistryWithCattle(out List<AnimalState> cows);
            var ledger = new VetTreatmentOutcomeLedger();
            var diag = new List<string>();

            Assert.IsNotNull(ledger.RecordOutcome(0, EntityId.For(EntityKind.Animal, 99999),
                "remedy-a", "ailment-x", VetOutcomeCode.Recovered, "", 102, "vet", registry, diag),
                "No phantom outcomes.");
            Assert.IsNotNull(ledger.RecordOutcome(-1, cows[0].AnimalId,
                "remedy-a", "ailment-x", VetOutcomeCode.Recovered, "", 102, "vet", registry, diag),
                "No orphan observations without a treatment reference.");
            Assert.AreEqual(0, ledger.Outcomes.Count);
        }

        [Test]
        public void OutcomeLedger_SaveLoad_RoundTrip()
        {
            AnimalRegistry registry = RegistryWithCattle(out List<AnimalState> cows);
            var ledger = new VetTreatmentOutcomeLedger();
            var diag = new List<string>();
            ledger.RecordOutcome(0, cows[0].AnimalId, "remedy-a", "ailment-x",
                VetOutcomeCode.NoChange, "no improvement seen", 105, "vet", registry, diag);

            VetTreatmentOutcomeLedger.VetTreatmentOutcomeLedgerSaveDto dto = ledger.CaptureSaveDto();
            var restored = new VetTreatmentOutcomeLedger();
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(1, restored.Outcomes.Count);
            Assert.AreEqual(VetOutcomeCode.NoChange, restored.Outcomes[0].Outcome);
            Assert.AreEqual(1, restored.GetRollup("remedy-a", "ailment-x").NoChange);
        }
    }
}
