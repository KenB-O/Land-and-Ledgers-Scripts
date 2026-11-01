using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Primitives;
using LandLedgers.World.Journeys;
using UnityEngine;

namespace LandLedgers.Economy.AnimalServices
{
    /// <summary>
    /// D3E: a scheduled herd-health visit program. Canon XXVII §6.2 names "group
    /// visits" explicitly and a standing service agreement covers terms — but the
    /// agreement "does not magically service every animal", so the program is only
    /// a cadence the caller executes: each visit still examines every animal
    /// individually (or records cohort-level work) and invoices through
    /// VetService.CallOut.
    ///
    /// DESIGN FORK (canon silent — parameterized, never guessed): the visit
    /// cadence (IntervalDays) and the per-animal exam fee are scenario/player
    /// parameters. The engine schedules and records; it does not invent a
    /// "correct" herd-health cadence.
    /// </summary>
    [Serializable]
    public sealed class VetHerdVisitSchedule
    {
        public string ScheduleId = string.Empty;
        public string FarmOrBusinessId = string.Empty;
        public string PractitionerId = string.Empty;

        /// <summary>Days between scheduled visits. DESIGN FORK: authored, not canon-derived.</summary>
        public int IntervalDays = 90;

        public int NextDueDayIndex;
        public int ExamFeePerAnimalCents;
        public string Notes = string.Empty;
        public bool Active = true;

        public VetHerdVisitSchedule() { }

        public bool IsDueOn(int dayIndex)
        {
            return Active && dayIndex >= NextDueDayIndex;
        }

        /// <summary>
        /// Moves the schedule to the next visit after a conducted visit.
        /// Returns a refusal string when the cadence is not a positive authored value.
        /// </summary>
        public string AdvanceToNext()
        {
            if (IntervalDays < 1)
            {
                return $"VetHerdVisitSchedule: schedule '{ScheduleId}' has non-positive IntervalDays ({IntervalDays}) — cadence must be authored, not guessed.";
            }

            NextDueDayIndex += IntervalDays;
            return null;
        }
    }

    /// <summary>
    /// D3E: one animal's examination inside a group visit. The exam is always
    /// recorded per animal; when the exam led to treatment, TreatmentSequence
    /// points at the VetService treatment record (Sequence on AnimalTreatment).
    /// An exam without treatment is honest preventive observation (Canon §6.1).
    /// </summary>
    [Serializable]
    public sealed class VetAnimalExamination
    {
        public EntityId AnimalId = EntityId.Invalid; // EntityKind.Animal — must be a real registered animal
        public string Species = string.Empty;
        public int DayIndex;
        public string ExaminationNotes = string.Empty;
        public int TreatmentSequence = -1; // -1 = examined, not treated
        public int ExamFeeCents;

        public VetAnimalExamination() { }
    }

    /// <summary>
    /// D3E: cohort-level vet work inside a group visit. Canon XXVII §6.2 says
    /// "actual animal-level or cohort-level work still occurs" — a vet can walk
    /// a cohort (a LivestockCohort headcount) and record cohort-level findings
    /// without pretending to have handled every head individually. Where animals
    /// ARE handled individually they also get VetAnimalExamination records.
    /// </summary>
    [Serializable]
    public sealed class VetCohortExamination
    {
        public EntityId CohortId = EntityId.Invalid; // EntityKind.AnimalCohort
        public int HeadExamined;
        public string CohortNotes = string.Empty;

        public VetCohortExamination() { }
    }

    /// <summary>
    /// D3E: one executed group herd-health visit. The call-out, travel (both
    /// ways), and any treatments invoice through VetService exactly like an
    /// individual call-out — one trip, many animals. Examinations and
    /// cohort-level notes are the visit's evidence; the invoice is the money.
    /// </summary>
    [Serializable]
    public sealed class VetHerdVisit
    {
        public string VisitId = string.Empty;
        public string ScheduleId = string.Empty;
        public string FarmOrBusinessId = string.Empty;
        public string PractitionerId = string.Empty;
        public int DayIndex;
        public List<VetAnimalExamination> Examinations = new List<VetAnimalExamination>();
        public List<VetCohortExamination> CohortExaminations = new List<VetCohortExamination>();
        public VetInvoice Invoice;
        public string Notes = string.Empty;

        public VetHerdVisit() { }
    }

    /// <summary>
    /// D3E: the herd-visit authority. A group visit is still one call-out
    /// (one travel charge, one call-out fee) covering many examined animals —
    /// that is what Canon §6.2's "group visits can matter" means. Every animal
    /// is validated against the real AnimalRegistry: unregistered or invalid
    /// animals are refused LOUDLY, never silently examined.
    /// </summary>
    public sealed class VetHerdVisitService
    {
        private readonly List<VetHerdVisit> visitHistory = new List<VetHerdVisit>();
        private int nextVisitSequence;

        public IReadOnlyList<VetHerdVisit> VisitHistory => visitHistory;

        /// <summary>
        /// Conducts a scheduled group visit. Treatment cases invoice through
        /// VetService.CallOut (practitioner validation, real JRN route, standing
        /// agreement fee honoring all apply). Returns null with diagnostics when
        /// the visit cannot happen — no practitioner, no route, no visit.
        /// </summary>
        public VetHerdVisit ConductVisit(
            VetService service,
            VetHerdVisitSchedule schedule,
            string farmOrBusinessId,
            string farmLocationId,
            List<VetAnimalExamination> examinations,
            List<VetCohortExamination> cohortExaminations,
            List<(EntityId animalId, string species, string ailment, string treatment, int workCostCents, int medicineCostCents, string medicineSource)> treatmentCases,
            AnimalRegistry registry,
            int dayIndex,
            JourneyModel journeys,
            EntityIdRegistry idRegistry,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (service == null)
            {
                diagnostics.Add("VetHerdVisitService: no vet service — the visit is unconducted, not free.");
                return null;
            }

            if (schedule == null)
            {
                diagnostics.Add("VetHerdVisitService: no visit schedule — a visit needs its scheduled program.");
                return null;
            }

            if (string.IsNullOrWhiteSpace(farmOrBusinessId))
            {
                diagnostics.Add("VetHerdVisitService: the farm/business must be named.");
                return null;
            }

            if (examinations == null || examinations.Count == 0)
            {
                diagnostics.Add("VetHerdVisitService: no animals presented for examination — the practitioner does not travel for nothing.");
                return null;
            }

            // Validate every animal against the real registry — upstream-provenance
            // doctrine: every examined animal is a real registered animal.
            var validExaminations = new List<VetAnimalExamination>();
            foreach (VetAnimalExamination exam in examinations)
            {
                if (exam == null) continue;
                if (!exam.AnimalId.IsValid || exam.AnimalId.Kind != EntityKind.Animal)
                {
                    diagnostics.Add($"VetHerdVisitService: examination refused — not a valid animal id. No herd-wide magic.");
                    continue;
                }

                if (registry == null || registry.GetAnimal(exam.AnimalId) == null)
                {
                    diagnostics.Add($"VetHerdVisitService: examination refused — animal {exam.AnimalId} is not a registered animal. No phantom herd members.");
                    continue;
                }

                exam.DayIndex = dayIndex;
                exam.ExamFeeCents = Math.Max(0, schedule.ExamFeePerAnimalCents);
                validExaminations.Add(exam);
            }

            if (validExaminations.Count == 0)
            {
                diagnostics.Add("VetHerdVisitService: no valid registered animals presented — visit refused.");
                return null;
            }

            // One call-out, one travel charge, per-animal treatments inside it.
            VetInvoice invoice = service.CallOut(
                schedule.PractitionerId, farmOrBusinessId, farmLocationId,
                treatmentCases, dayIndex, journeys, idRegistry, diagnostics);

            if (invoice == null)
            {
                diagnostics.Add("VetHerdVisitService: the call-out behind the visit failed — the visit is unconducted.");
                return null;
            }

            var visit = new VetHerdVisit
            {
                VisitId = $"herd-visit-{schedule.ScheduleId}-{dayIndex}-{nextVisitSequence++}",
                ScheduleId = schedule.ScheduleId,
                FarmOrBusinessId = farmOrBusinessId,
                PractitionerId = schedule.PractitionerId,
                DayIndex = dayIndex,
                Invoice = invoice,
            };
            visit.Examinations.AddRange(validExaminations);
            if (cohortExaminations != null) visit.CohortExaminations.AddRange(cohortExaminations);
            visitHistory.Add(visit);

            // Link examinations to their treatment records by animal id.
            foreach (AnimalTreatment treatment in invoice.Treatments)
            {
                foreach (VetAnimalExamination exam in visit.Examinations)
                {
                    if (exam.AnimalId.Equals(treatment.AnimalId))
                    {
                        exam.TreatmentSequence = treatment.Sequence;
                        break;
                    }
                }
            }

            string advanceProblem = schedule.AdvanceToNext();
            if (advanceProblem != null)
            {
                diagnostics.Add($"VetHerdVisitService: {advanceProblem} — visit conducted but the schedule did not advance.");
            }
            else
            {
                diagnostics.Add($"VetHerdVisitService: visit {visit.VisitId} conducted — " +
                    $"{visit.Examinations.Count} animal(s) examined, {invoice.Treatments.Count} treated, " +
                    $"{visit.CohortExaminations.Count} cohort-level note(s); invoice {invoice.TotalCents}c.");
            }

            return visit;
        }

        #region Save / Load
        [Serializable]
        public sealed class VetHerdVisitServiceSaveDto
        {
            public List<VetHerdVisit> Visits = new List<VetHerdVisit>();
            public int NextVisitSequence;
        }

        public VetHerdVisitServiceSaveDto CaptureSaveDto()
        {
            return new VetHerdVisitServiceSaveDto
            {
                Visits = new List<VetHerdVisit>(visitHistory),
                NextVisitSequence = nextVisitSequence,
            };
        }

        public void LoadFromSaveDto(VetHerdVisitServiceSaveDto dto)
        {
            visitHistory.Clear();
            if (dto == null) return;
            if (dto.Visits != null) visitHistory.AddRange(dto.Visits);
            nextVisitSequence = Math.Max(nextVisitSequence, dto.NextVisitSequence);
        }
        #endregion
    }

    /// <summary>
    /// D3E: an authored preventive-care schedule. Canon XXVII §6.1 requires
    /// recurring preventive work for working animals (grooming, hoof observation,
    /// harness checks, condition observation, ordinary observation) but gives no
    /// vaccination/dosing calendar and no species mapping — and the 1870s are
    /// too early for period-vaccination doctrine to invent. So the schedule is
    /// AUTHORED (scenario or player writes the regimen); the engine schedules
    /// and records performance. Never auto-generates a medical regimen.
    /// </summary>
    [Serializable]
    public sealed class VetPreventiveSchedule
    {
        public string ScheduleId = string.Empty;
        public string FarmOrBusinessId = string.Empty;
        public AnimalSpecies Species = AnimalSpecies.Unspecified;

        /// <summary>
        /// The authored preventive action, e.g. "hoof observation", "harness check",
        /// "condition observation", "grooming". Canon §6.1's named items — nothing invented.
        /// </summary>
        public string PreventiveAction = string.Empty;

        /// <summary>Days between due dates. DESIGN FORK: authored, not canon-derived.</summary>
        public int IntervalDays = 30;

        public int NextDueDayIndex;
        public bool Active = true;
        public string Notes = string.Empty;

        public VetPreventiveSchedule() { }

        public bool IsDueOn(int dayIndex)
        {
            return Active && dayIndex >= NextDueDayIndex;
        }

        public string AdvanceToNext()
        {
            if (IntervalDays < 1)
            {
                return $"VetPreventiveSchedule: schedule '{ScheduleId}' has non-positive IntervalDays ({IntervalDays}) — cadence must be authored, not guessed.";
            }

            NextDueDayIndex += IntervalDays;
            return null;
        }
    }

    /// <summary>
    /// D3E: one performed preventive-care event. Animals are listed individually
    /// (no magic cohort coverage); an optional cohort record captures cohort-level
    /// observation. When medicine is used it comes from a real VetMedicineStock
    /// with dose-by-dose provenance — shortfalls are recorded LOUDLY as
    /// unmedicated, never auto-ordered (upstream-provenance doctrine).
    /// </summary>
    [Serializable]
    public sealed class VetPreventiveEvent
    {
        public int Sequence;
        public string ScheduleId = string.Empty;
        public string FarmOrBusinessId = string.Empty;
        public AnimalSpecies Species = AnimalSpecies.Unspecified;
        public string PreventiveAction = string.Empty;
        public int DayIndex;
        public int PerformedByPersonId = -1;
        public List<EntityId> AnimalIds = new List<EntityId>(); // each animal individually identified
        public EntityId CohortId = EntityId.Invalid; // EntityKind.AnimalCohort, when cohort-level
        public int CohortHeadCount;
        public string MedicineName = string.Empty;
        public int DosesPerAnimal;
        public string MedicineProvenance = string.Empty; // dose-by-dose chains, or "unmedicated"/"unmedicated — stock shortfall"
        public string Notes = string.Empty;

        public VetPreventiveEvent() { }
    }

    /// <summary>
    /// D3E: the preventive-care authority. Records performance of authored
    /// schedules; validates every animal; dispenses real medicine only.
    /// </summary>
    public sealed class VetPreventiveService
    {
        private readonly List<VetPreventiveEvent> eventHistory = new List<VetPreventiveEvent>();
        private int nextEventSequence;

        public IReadOnlyList<VetPreventiveEvent> EventHistory => eventHistory;

        /// <summary>
        /// Performs the scheduled preventive act. Returns the event record, or
        /// null with diagnostics when nothing valid can be performed (no
        /// registered animals, no schedule). Medicine comes from the real stock;
        /// a shortfall is recorded as unmedicated — never auto-ordered.
        /// </summary>
        public VetPreventiveEvent PerformPreventive(
            VetPreventiveSchedule schedule,
            List<EntityId> animalIds,
            EntityId cohortId,
            int cohortHeadCount,
            string medicineName,
            int dosesPerAnimal,
            int performedByPersonId,
            VetMedicineStock stock,
            AnimalRegistry registry,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (schedule == null)
            {
                diagnostics.Add("VetPreventiveService: no schedule — preventive work needs its authored regimen.");
                return null;
            }

            if (string.IsNullOrWhiteSpace(schedule.PreventiveAction))
            {
                diagnostics.Add($"VetPreventiveService: schedule '{schedule.ScheduleId}' names no preventive action — no invented regimen.");
                return null;
            }

            var validAnimals = new List<EntityId>();
            if (animalIds != null)
            {
                foreach (EntityId animalId in animalIds)
                {
                    if (!animalId.IsValid || animalId.Kind != EntityKind.Animal)
                    {
                        diagnostics.Add("VetPreventiveService: animal refused — not a valid animal id.");
                        continue;
                    }

                    if (registry == null || registry.GetAnimal(animalId) == null)
                    {
                        diagnostics.Add($"VetPreventiveService: animal {animalId} is not a registered animal — refused, no phantom treatments.");
                        continue;
                    }

                    validAnimals.Add(animalId);
                }
            }

            if (validAnimals.Count == 0 && (!cohortId.IsValid || cohortHeadCount <= 0))
            {
                diagnostics.Add("VetPreventiveService: no registered animals and no cohort presented — nothing performed.");
                return null;
            }

            var evt = new VetPreventiveEvent
            {
                Sequence = nextEventSequence++,
                ScheduleId = schedule.ScheduleId,
                FarmOrBusinessId = schedule.FarmOrBusinessId,
                Species = schedule.Species,
                PreventiveAction = schedule.PreventiveAction,
                DayIndex = dayIndex,
                PerformedByPersonId = performedByPersonId,
                CohortId = cohortId,
                CohortHeadCount = Math.Max(0, cohortHeadCount),
                MedicineName = medicineName ?? string.Empty,
                DosesPerAnimal = Math.Max(0, dosesPerAnimal),
                Notes = schedule.Notes ?? string.Empty,
            };
            evt.AnimalIds.AddRange(validAnimals);

            // Medicine: real stock only, FIFO dispense, loud shortfalls.
            // Dosing is per individually-identified animal (upstream-provenance
            // doctrine: every treated animal is a real registered animal). A
            // cohort-level observation alone can never take medicine — that
            // would be unidentifiable mass dosing, which canon does not model.
            if (!string.IsNullOrWhiteSpace(evt.MedicineName) && evt.DosesPerAnimal > 0)
            {
                if (validAnimals.Count == 0)
                {
                    evt.MedicineProvenance = "unmedicated — cohort observation cannot take per-animal medicine";
                    diagnostics.Add("VetPreventiveService: event recorded unmedicated — per-animal medicine needs " +
                        "individually identified animals; a cohort headcount alone is not doseable. No mass dosing invented.");
                }
                else if (stock == null)
                {
                    evt.MedicineProvenance = "unmedicated — no medicine stock";
                    diagnostics.Add("VetPreventiveService: no medicine stock — the event is recorded unmedicated, not auto-ordered.");
                }
                else
                {
                    int totalDoses = evt.DosesPerAnimal * validAnimals.Count;
                    List<VetMedicineDispenseLine> lines = stock.TryDispenseDoses(evt.MedicineName, totalDoses, dayIndex, diagnostics);
                    if (lines == null)
                    {
                        evt.MedicineProvenance = "unmedicated — stock shortfall";
                        diagnostics.Add($"VetPreventiveService: preventive event {evt.Sequence} recorded unmedicated — " +
                            $"chest cannot cover {totalDoses} dose(s) of '{evt.MedicineName}'. No doses conjured, none auto-ordered.");
                    }
                    else
                    {
                        var chains = new List<string>();
                        foreach (VetMedicineDispenseLine line in lines)
                        {
                            chains.Add($"{line.DosesTaken}x {line.MedicineName} ({line.ProvenanceChain})");
                        }

                        evt.MedicineProvenance = string.Join("; ", chains.ToArray());
                    }
                }
            }
            else
            {
                evt.MedicineProvenance = "unmedicated";
            }

            eventHistory.Add(evt);

            string advanceProblem = schedule.AdvanceToNext();
            if (advanceProblem != null)
            {
                diagnostics.Add($"VetPreventiveService: {advanceProblem} — event recorded but the schedule did not advance.");
            }
            else
            {
                diagnostics.Add($"VetPreventiveService: preventive event {evt.Sequence} — '{evt.PreventiveAction}' on " +
                    $"{evt.AnimalIds.Count} animal(s)" +
                    (evt.CohortHeadCount > 0 ? $" + cohort {evt.CohortId} ({evt.CohortHeadCount} head)" : "") +
                    $", day {dayIndex}.");
            }

            return evt;
        }

        #region Save / Load
        [Serializable]
        public sealed class VetPreventiveServiceSaveDto
        {
            public List<VetPreventiveEvent> Events = new List<VetPreventiveEvent>();
            public int NextEventSequence;
        }

        public VetPreventiveServiceSaveDto CaptureSaveDto()
        {
            return new VetPreventiveServiceSaveDto
            {
                Events = new List<VetPreventiveEvent>(eventHistory),
                NextEventSequence = nextEventSequence,
            };
        }

        public void LoadFromSaveDto(VetPreventiveServiceSaveDto dto)
        {
            eventHistory.Clear();
            if (dto == null) return;
            if (dto.Events != null) eventHistory.AddRange(dto.Events);
            nextEventSequence = Math.Max(nextEventSequence, dto.NextEventSequence);
        }
        #endregion
    }

    /// <summary>
    /// D3E: outcome vocabulary for treatment observation. Only outcomes actually
    /// OBSERVED are recorded; "unknown" stays unknown — the ledger never
    /// resolves ambiguity into a guess (Tech X §5.3: poor records reduce
    /// information, not reality).
    /// </summary>
    public enum VetOutcomeCode
    {
        Unknown = 0,
        Recovered = 1,
        Died = 2,
        NoChange = 3,
    }

    /// <summary>
    /// D3E: one observed treatment outcome. Links a VetService treatment record
    /// (TreatmentSequence) to what actually happened afterward — as observed,
    /// with a named source. No efficacy math lives here: Tech X §18.1 holds
    /// veterinary-treatment rates as research, so the engine records data only.
    /// </summary>
    [Serializable]
    public sealed class VetTreatmentOutcome
    {
        public int TreatmentSequence = -1; // VetService AnimalTreatment.Sequence
        public EntityId AnimalId = EntityId.Invalid; // EntityKind.Animal — real registered animal
        public string MedicineName = string.Empty;
        public string Ailment = string.Empty;
        public VetOutcomeCode Outcome = VetOutcomeCode.Unknown;
        public string OutcomeNote = string.Empty;
        public int ObservedDayIndex = -1;
        public string Source = string.Empty; // who observed it: "veterinarian", "farmer", "outbreak record", ...

        public VetTreatmentOutcome() { }
    }

    /// <summary>
    /// D3E: counts of observed treatment outcomes, grouped by medicine and
    /// ailment. COUNTS ONLY — recorded data, never an efficacy rate or
    /// probability. Any reader that wants "how well does this work" gets raw
    /// counts plus the explicit no-efficacy-claim disclaimer (Tech X §18.1).
    /// </summary>
    [Serializable]
    public sealed class VetOutcomeRollup
    {
        public string MedicineName = string.Empty;
        public string Ailment = string.Empty;
        public int Treated;
        public int Recovered;
        public int Died;
        public int NoChange;
        public int Unknown;

        /// <summary>Hard doctrine: counts are not efficacy. Tech X §18.1 research hold.</summary>
        public const string NoEfficacyClaimDisclaimer =
            "Recorded outcomes only — no efficacy rate, probability, or treatment recommendation " +
            "is implied or computed. Veterinary-treatment rates are a Canon/Tech X §18.1 research hold.";

        public VetOutcomeRollup() { }
    }

    /// <summary>
    /// D3E: the data-only medicine-efficacy tracker. Records what was observed
    /// for each treatment; rolls up into counts. It refuses to invent outcomes
    /// (unobserved stays Unknown) and refuses to compute efficacy (no rates,
    /// no probabilities, no "this treatment works" claims). That is the whole
    /// of the Tech X §18.1 research hold honored in code.
    /// </summary>
    public sealed class VetTreatmentOutcomeLedger
    {
        private readonly List<VetTreatmentOutcome> outcomes = new List<VetTreatmentOutcome>();

        public IReadOnlyList<VetTreatmentOutcome> Outcomes => outcomes;

        /// <summary>
        /// Records an observed outcome. Returns a refusal string when the record
        /// is unusable (unregistered animal, missing treatment reference).
        /// </summary>
        public string RecordOutcome(
            int treatmentSequence,
            EntityId animalId,
            string medicineName,
            string ailment,
            VetOutcomeCode outcome,
            string outcomeNote,
            int observedDayIndex,
            string source,
            AnimalRegistry registry,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (treatmentSequence < 0)
            {
                return "VetTreatmentOutcomeLedger: an outcome needs its treatment's sequence — no orphan observations.";
            }

            if (!animalId.IsValid || animalId.Kind != EntityKind.Animal)
            {
                return "VetTreatmentOutcomeLedger: outcome refused — not a valid animal id.";
            }

            if (registry == null || registry.GetAnimal(animalId) == null)
            {
                return $"VetTreatmentOutcomeLedger: outcome refused — animal {animalId} is not a registered animal.";
            }

            outcomes.Add(new VetTreatmentOutcome
            {
                TreatmentSequence = treatmentSequence,
                AnimalId = animalId,
                MedicineName = medicineName ?? string.Empty,
                Ailment = ailment ?? string.Empty,
                Outcome = outcome,
                OutcomeNote = outcomeNote ?? string.Empty,
                ObservedDayIndex = observedDayIndex,
                Source = string.IsNullOrWhiteSpace(source) ? "unattributed" : source,
            });
            diagnostics.Add($"VetTreatmentOutcomeLedger: outcome recorded — treatment #{treatmentSequence}, animal {animalId}, {outcome} (day {observedDayIndex}).");
            return null;
        }

        /// <summary>
        /// Counts observed outcomes for one medicine + ailment pair. Counts only;
        /// no rates, no probabilities — see VetOutcomeRollup.NoEfficacyClaimDisclaimer.
        /// </summary>
        public VetOutcomeRollup GetRollup(string medicineName, string ailment)
        {
            var rollup = new VetOutcomeRollup
            {
                MedicineName = medicineName ?? string.Empty,
                Ailment = ailment ?? string.Empty,
            };

            foreach (VetTreatmentOutcome o in outcomes)
            {
                if (!string.Equals(o.MedicineName, rollup.MedicineName, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(o.Ailment, rollup.Ailment, StringComparison.OrdinalIgnoreCase)) continue;

                rollup.Treated++;
                switch (o.Outcome)
                {
                    case VetOutcomeCode.Recovered: rollup.Recovered++; break;
                    case VetOutcomeCode.Died: rollup.Died++; break;
                    case VetOutcomeCode.NoChange: rollup.NoChange++; break;
                    default: rollup.Unknown++; break;
                }
            }

            return rollup;
        }

        #region Save / Load
        [Serializable]
        public sealed class VetTreatmentOutcomeLedgerSaveDto
        {
            public List<VetTreatmentOutcome> Outcomes = new List<VetTreatmentOutcome>();
        }

        public VetTreatmentOutcomeLedgerSaveDto CaptureSaveDto()
        {
            return new VetTreatmentOutcomeLedgerSaveDto { Outcomes = new List<VetTreatmentOutcome>(outcomes) };
        }

        public void LoadFromSaveDto(VetTreatmentOutcomeLedgerSaveDto dto)
        {
            outcomes.Clear();
            if (dto?.Outcomes == null) return;
            outcomes.AddRange(dto.Outcomes);
        }
        #endregion
    }
}
