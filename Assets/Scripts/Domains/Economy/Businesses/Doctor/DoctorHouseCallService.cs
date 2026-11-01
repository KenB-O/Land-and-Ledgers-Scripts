using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.World.Journeys;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Doctor
{
    /// <summary>
    /// W1A: house-call severity. Canon §13.3: "Office visits handle most cases.
    /// House calls are reserved for severe illness, severe injury, immobility,
    /// and later special cases." Minor and routine cases can wait or self-manage.
    /// This is W1A's triage policy for the house-call service; it does not alter
    /// the existing daily in-office doctor resolution.
    /// </summary>
    public enum HouseCallSeverity
    {
        Minor = 0,
        Moderate = 1,
        Severe = 2,
        Immobile = 3,
    }

    /// <summary>
    /// W1A: one patient at the house call. Per-patient work, never a batch —
    /// each patient is treated and billed individually.
    /// </summary>
    [Serializable]
    public sealed class HouseCallPatient
    {
        public EntityId PatientPersonId = EntityId.Invalid; // EntityKind.Person
        public string Ailment = string.Empty;
        public HouseCallSeverity Severity = HouseCallSeverity.Minor;

        /// <summary>Medicine to administer; empty = no medicine, labor only.</summary>
        public string MedicineName = string.Empty;

        public int DosesNeeded;

        /// <summary>
        /// D1A: the condition kind behind the ailment (drives outcome tables).
        /// Defaults to Illness; callers modeling injuries set Injury.
        /// </summary>
        public HealthConditionKind ConditionKind = HealthConditionKind.Illness;

        /// <summary>
        /// D1A: "later special cases" hook (Canon §13.3: house calls are
        /// reserved for severe illness, severe injury, immobility, "and later
        /// special cases"). A non-empty tag (e.g. a modeled childbirth
        /// complication, Canon §13.3B "where modeled") qualifies the patient
        /// for a call regardless of severity band. The tag alone models
        /// nothing — the modeled condition behind it lives elsewhere.
        /// </summary>
        public string SpecialCase = string.Empty;

        public HouseCallPatient() { }
    }

    /// <summary>W1A: a queued house-call request for one household.</summary>
    [Serializable]
    public sealed class HouseCallRequest
    {
        public EntityId RequestId = EntityId.Invalid; // EntityKind.Contract (SWN-3 precedent)
        public string DoctorBusinessId = string.Empty;
        public string PatientHouseholdId = string.Empty;
        public string PatientLocationId = string.Empty; // JRN location of the household
        public int RequestDayIndex;
        public List<HouseCallPatient> Patients = new List<HouseCallPatient>();

        public HouseCallRequest() { }
    }

    /// <summary>
    /// W1A: one patient's treatment record from a house call — labor, doses with
    /// lot provenance, and per Canon §13.3B an honest outcome (treatment never
    /// guarantees recovery).
    /// </summary>
    [Serializable]
    public sealed class HouseCallTreatment
    {
        public EntityId PatientPersonId = EntityId.Invalid;
        public string Ailment = string.Empty;
        public string TreatmentSummary = string.Empty;
        public int DayIndex;
        public int WorkFeeCents;
        public int MedicineChargeCents;
        public List<DoctorMedicineDispenseLine> MedicineLotsUsed = new List<DoctorMedicineDispenseLine>();
        public string Outcome = string.Empty;

        public HouseCallTreatment() { }

        public int TotalCostCents => Math.Max(0, WorkFeeCents) + Math.Max(0, MedicineChargeCents);

        /// <summary>D1A: structured outcome from the outcome engine (Canon §13.3B depth).</summary>
        public DoctorOutcomeTier OutcomeTier = DoctorOutcomeTier.Untreated;

        /// <summary>D1A: absence days the treatment eased (0 when unknown/not resolved).</summary>
        public int OutcomeDaysReduced;

        /// <summary>D1A: whether the outcome stepped severity down one band.</summary>
        public bool OutcomeSeverityDowngraded;

        /// <summary>D1A: the outcome engine's summary; the legacy Outcome string is kept for compatibility.</summary>
        public string OutcomeSummary = string.Empty;
    }

    /// <summary>
    /// W1A: one house call's itemized invoice — call-out, travel (both ways),
    /// and per-patient treatments. Line items, never a lump sum without basis.
    /// The caller settles it as an ordinary ledger outflow; money moves only
    /// through ledger authorities.
    /// </summary>
    [Serializable]
    public sealed class HouseCallInvoice
    {
        public int DayIndex;
        public string DoctorBusinessId = string.Empty;
        public string PatientHouseholdId = string.Empty;
        public string PatientLocationId = string.Empty;
        public int CallOutFeeCents;
        public float TravelMiles;
        public int TravelMinutes;
        public int TravelFeeCents;
        public List<HouseCallTreatment> Treatments = new List<HouseCallTreatment>();
        public string Notes = string.Empty;

        public int TreatmentTotalCents
        {
            get
            {
                int total = 0;
                foreach (var t in Treatments) total += t.TotalCostCents;
                return total;
            }
        }

        public int TotalCents => Math.Max(0, CallOutFeeCents) + Math.Max(0, TravelFeeCents) + TreatmentTotalCents;

        /// <summary>Total professional minutes consumed: travel plus bedside time.</summary>
        public int TotalMinutes => Math.Max(0, TravelMinutes)
            + Treatments.Count * DoctorTreatmentCatalog.HouseCallBedsideMinutes;
    }

    /// <summary>
    /// D1A: what happened to a queued patient who waited too long. Canon
    /// §13.3B: "When demand exceeds capacity, some patients wait, travel
    /// elsewhere or go untreated." The wait is the queue; the resolution is
    /// recorded here, loudly, with the trust cost it carries.
    /// </summary>
    public enum DeferredPatientResolution
    {
        SoughtOffMapCare = 0,
        WentUntreated = 1,
    }

    /// <summary>D1A: one queued request that aged out of the queue.</summary>
    [Serializable]
    public sealed class HouseCallDeferredOutcome
    {
        public EntityId RequestId = EntityId.Invalid; // EntityKind.Contract
        public string PatientHouseholdId = string.Empty;
        public int RequestDayIndex;
        public int ResolvedDayIndex;
        public DeferredPatientResolution Resolution = DeferredPatientResolution.WentUntreated;
        public string Notes = string.Empty;

        public HouseCallDeferredOutcome() { }
    }

    /// <summary>
    /// W1A: the doctor's house-call service. Historical model (1870s): the doctor
    /// TRAVELS to the patient — call-out fee plus travel charged both ways, then
    /// per-patient work on top. Canon §13.3A: a house call consumes travel time
    /// as well as treatment time, so the doctor who rides to a remote mine cannot
    /// simultaneously serve office patients. Travel runs through the JRN route
    /// model like any other journey; refusal is loud, never silent.
    ///
    /// Boundary: this service owns the house-call layer only. The existing daily
    /// in-office doctor treatment resolution (SharedBusinessRuntimeManager) is
    /// untouched — W1A routes around it.
    /// </summary>
    public sealed class DoctorHouseCallService
    {
        private readonly List<HouseCallRequest> queue = new List<HouseCallRequest>();
        private readonly List<HouseCallInvoice> completedInvoices = new List<HouseCallInvoice>();
        private readonly List<HouseCallDeferredOutcome> deferredOutcomes = new List<HouseCallDeferredOutcome>();

        public IReadOnlyList<HouseCallRequest> Queue => queue;
        public IReadOnlyList<HouseCallInvoice> CompletedInvoices => completedInvoices;
        public IReadOnlyList<HouseCallDeferredOutcome> DeferredOutcomes => deferredOutcomes;

        /// <summary>
        /// D1A: removes and returns the next request by urgency — worst
        /// severity first, then earliest request day (Canon §13.3B: severe
        /// cases "can justify urgent house calls or immediate attention").
        /// Stable within a band: FIFO is preserved, not replaced.
        /// Returns null when the queue is empty.
        /// </summary>
        public HouseCallRequest DequeueNextPrioritized()
        {
            if (queue.Count == 0) return null;

            int best = 0;
            for (int i = 1; i < queue.Count; i++)
            {
                if (CompareUrgency(queue[i], queue[best]) < 0) best = i;
            }

            HouseCallRequest request = queue[best];
            queue.RemoveAt(best);
            return request;
        }

        private static int CompareUrgency(HouseCallRequest a, HouseCallRequest b)
        {
            int severityCompare = WorstSeverity(b).CompareTo(WorstSeverity(a)); // worst first
            if (severityCompare != 0) return severityCompare;
            int dayCompare = a.RequestDayIndex.CompareTo(b.RequestDayIndex); // earliest first
            if (dayCompare != 0) return dayCompare;
            return 0;
        }

        private static HouseCallSeverity WorstSeverity(HouseCallRequest request)
        {
            HouseCallSeverity worst = HouseCallSeverity.Minor;
            if (request?.Patients != null)
            {
                foreach (var patient in request.Patients)
                {
                    if (patient != null && patient.Severity > worst) worst = patient.Severity;
                }
            }

            return worst;
        }

        /// <summary>
        /// D1A: ages the queue. Requests waiting longer than maxWaitDays are
        /// resolved, not silently dropped: when off-map care is reachable the
        /// patient is recorded as having sought it there, otherwise as having
        /// gone untreated (Canon §13.3B). Returns the number aged out.
        /// </summary>
        public int AgeQueue(int dayIndex, int maxWaitDays, bool offMapCareReachable, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            int agedOut = 0;
            for (int i = queue.Count - 1; i >= 0; i--)
            {
                HouseCallRequest request = queue[i];
                if (request == null) { queue.RemoveAt(i); continue; }
                if (dayIndex - request.RequestDayIndex <= Math.Max(1, maxWaitDays)) continue;

                var outcome = new HouseCallDeferredOutcome
                {
                    RequestId = request.RequestId,
                    PatientHouseholdId = request.PatientHouseholdId,
                    RequestDayIndex = request.RequestDayIndex,
                    ResolvedDayIndex = dayIndex,
                    Resolution = offMapCareReachable
                        ? DeferredPatientResolution.SoughtOffMapCare
                        : DeferredPatientResolution.WentUntreated,
                    Notes = offMapCareReachable
                        ? $"waited {dayIndex - request.RequestDayIndex} days, then sought care at the off-map center"
                        : $"waited {dayIndex - request.RequestDayIndex} days, then went untreated",
                };
                deferredOutcomes.Add(outcome);
                queue.RemoveAt(i);
                agedOut++;
                diagnostics.Add($"DoctorHouseCallService: queued call for {request.PatientHouseholdId} aged out after " +
                    $"{dayIndex - request.RequestDayIndex} days — {outcome.Notes} (Canon §13.3B).");
            }

            return agedOut;
        }

        /// <summary>Restores queued requests after a save/load (called by the owning practice runtime).</summary>
        public void RestoreQueue(IEnumerable<HouseCallRequest> requests)
        {
            queue.Clear();
            if (requests == null) return;
            foreach (var request in requests)
            {
                if (request != null) queue.Add(request);
            }
        }

        /// <summary>
        /// D1A: puts a dequeued-but-unrunnable request back at the front of
        /// the queue so a loud refusal (no route) keeps it queued for
        /// tomorrow instead of dropping it.
        /// </summary>
        public void RestoreQueuePrepend(HouseCallRequest request)
        {
            if (request == null) return;
            if (!queue.Contains(request)) queue.Insert(0, request);
        }

        /// <summary>D1A: restores deferred outcomes after a save/load.</summary>
        public void RestoreDeferredOutcomes(IEnumerable<HouseCallDeferredOutcome> outcomes)
        {
            deferredOutcomes.Clear();
            if (outcomes == null) return;
            foreach (var outcome in outcomes)
            {
                if (outcome != null) deferredOutcomes.Add(outcome);
            }
        }

        /// <summary>
        /// Queues a house call after triage. Canon §13.3B: minor cases can wait
        /// or self-manage; moderate cases are more likely to seek care but do not
        /// justify a call — they route to office visits. Only Severe and Immobile
        /// cases earn a house call. Returns a rejection string, or null on success.
        /// </summary>
        public string QueueHouseCall(HouseCallRequest request, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (request == null) return "DoctorHouseCallService: null request — no call queued.";
            if (request.Patients == null || request.Patients.Count == 0)
                return "DoctorHouseCallService: a house call needs at least one patient.";
            if (string.IsNullOrWhiteSpace(request.PatientLocationId))
                return "DoctorHouseCallService: the patient location is required — the doctor travels somewhere real.";

            foreach (var patient in request.Patients)
            {
                if (!patient.PatientPersonId.IsValid || patient.PatientPersonId.Kind != EntityKind.Person)
                    return "DoctorHouseCallService: every patient must be a valid person — no anonymous or batch treatment.";
            }

            HouseCallSeverity worst = HouseCallSeverity.Minor;
            bool hasSpecialCase = false;
            foreach (var patient in request.Patients)
            {
                if (patient.Severity > worst) worst = patient.Severity;
                if (!string.IsNullOrWhiteSpace(patient.SpecialCase)) hasSpecialCase = true;
            }

            // W1A triage policy (Canon §13.3): house calls are reserved for severe
            // illness, severe injury, immobility. This documents, not re-litigates,
            // the severity model — the daily office resolution keeps its own.
            // D1A: a named special case (Canon §13.3 "later special cases";
            // §13.3B "childbirth complications where modeled") qualifies too.
            if (worst <= HouseCallSeverity.Moderate && !hasSpecialCase)
            {
                string routing = worst == HouseCallSeverity.Minor
                    ? "minor case — self-manage or office visit"
                    : "moderate case — office visit";
                diagnostics.Add($"DoctorHouseCallService: house call refused for {request.PatientHouseholdId} ({routing}; Canon §13.3 reserves calls for severe illness/injury and immobility).");
                return $"DoctorHouseCallService: house call refused — {routing}.";
            }

            if (hasSpecialCase && worst <= HouseCallSeverity.Moderate)
            {
                diagnostics.Add($"DoctorHouseCallService: house call queued for {request.PatientHouseholdId} on a named special case (Canon §13.3 'later special cases').");
            }

            queue.Add(request);
            diagnostics.Add($"DoctorHouseCallService: house call queued for {request.PatientHouseholdId} ({request.Patients.Count} patient(s), worst severity {worst}, day {request.RequestDayIndex}).");
            return null;
        }

        /// <summary>
        /// Executes one queued house call: real JRN route out and back, per-patient
        /// treatment with medicine-lot provenance, itemized invoice. Returns null
        /// with diagnostics when the call cannot run.
        /// D1A: when the caller supplies an rng, each treated patient also gets a
        /// resolved outcome from the outcome engine (Canon §13.3B) recorded on
        /// the treatment's structured outcome fields. With no rng the legacy
        /// placeholder behavior is kept exactly.
        /// </summary>
        public HouseCallInvoice ExecuteHouseCall(
            HouseCallRequest request,
            string doctorLocationId,
            DoctorFeeSchedule fees,
            DoctorMedicineStock medicineStock,
            int dayIndex,
            JourneyModel journeys,
            List<string> diagnostics,
            System.Random rng = null,
            DoctorPractitionerSkill practitionerSkill = DoctorPractitionerSkill.Competent)
        {
            diagnostics = diagnostics ?? new List<string>();
            fees = fees ?? new DoctorFeeSchedule();
            if (request == null)
            {
                diagnostics.Add("DoctorHouseCallService: null request — no house call executed.");
                return null;
            }

            if (string.IsNullOrWhiteSpace(doctorLocationId))
            {
                diagnostics.Add("DoctorHouseCallService: doctor has no home location — no call executed.");
                return null;
            }

            if (journeys == null || string.IsNullOrWhiteSpace(request.PatientLocationId))
            {
                diagnostics.Add("DoctorHouseCallService: no journey model or patient location — call refused.");
                return null;
            }

            // Travel burden is real: route out and back, fee per mile each way.
            JourneyRoute route = journeys.FindRoute(doctorLocationId, request.PatientLocationId, TravelMode.Horseback);
            if (!route.Found)
            {
                diagnostics.Add($"DoctorHouseCallService: doctor cannot reach '{request.PatientLocationId}' — {route.Diagnostic}");
                return null;
            }

            float miles = route.TotalMiles;
            int travelMinutes = Mathf.RoundToInt(miles * 2f * 60f / 7f); // horseback, 7 mph (calibration, Canon Part XV)
            int travelFee = Mathf.RoundToInt(miles * 2f * Math.Max(0, fees.PerMileTravelCents));

            var invoice = new HouseCallInvoice
            {
                DayIndex = dayIndex,
                DoctorBusinessId = request.DoctorBusinessId,
                PatientHouseholdId = request.PatientHouseholdId,
                PatientLocationId = request.PatientLocationId,
                CallOutFeeCents = Math.Max(0, fees.HouseCallOutFeeCents),
                TravelMiles = miles,
                TravelMinutes = travelMinutes,
                TravelFeeCents = travelFee,
            };

            foreach (var patient in request.Patients)
            {
                var treatment = new HouseCallTreatment
                {
                    PatientPersonId = patient.PatientPersonId,
                    Ailment = patient.Ailment ?? string.Empty,
                    DayIndex = dayIndex,
                    WorkFeeCents = Math.Max(0, fees.HouseCallTreatmentFeeCents),
                };

                if (!string.IsNullOrWhiteSpace(patient.MedicineName) && patient.DosesNeeded > 0)
                {
                    var lines = medicineStock?.TryDispenseDoses(patient.MedicineName, patient.DosesNeeded, dayIndex, diagnostics);
                    if (lines == null)
                    {
                        diagnostics.Add($"DoctorHouseCallService: patient {patient.PatientPersonId} untreated — no '{patient.MedicineName}' on hand. Stock shortfall is loud, never papered over.");
                        treatment.TreatmentSummary = "untreated: medicine unavailable";
                        treatment.Outcome = "untreated";
                        treatment.WorkFeeCents = 0;
                        RecordHouseCallOutcome(treatment, patient, false, rng, practitionerSkill);
                    }
                    else
                    {
                        treatment.MedicineLotsUsed.AddRange(lines);
                        treatment.MedicineChargeCents = patient.DosesNeeded * Math.Max(0, fees.RemedyDoseChargeCents);
                        treatment.TreatmentSummary = $"treated: {patient.Ailment} ({patient.DosesNeeded} dose(s) {patient.MedicineName})";
                        treatment.Outcome = "treated (recovery not guaranteed)";
                        RecordHouseCallOutcome(treatment, patient, true, rng, practitionerSkill);
                    }
                }
                else
                {
                    treatment.TreatmentSummary = $"treated: {patient.Ailment} (labor only)";
                    treatment.Outcome = "treated (recovery not guaranteed)";
                    RecordHouseCallOutcome(treatment, patient, true, rng, practitionerSkill);
                }

                invoice.Treatments.Add(treatment);
            }

            completedInvoices.Add(invoice);
            queue.Remove(request);
            diagnostics.Add($"DoctorHouseCallService: house call to {request.PatientHouseholdId} done — {invoice.TotalCents}c total, {invoice.TotalMinutes} min professional time (day {dayIndex}).");
            return invoice;
        }

        /// <summary>
        /// D1A: records the structured outcome for one house-call treatment.
        /// Without an rng the legacy placeholder stands (OutcomeTier mirrors
        /// the legacy string); with one, the outcome engine resolves honestly.
        /// </summary>
        private static void RecordHouseCallOutcome(
            HouseCallTreatment treatment,
            HouseCallPatient patient,
            bool treated,
            System.Random rng,
            DoctorPractitionerSkill practitionerSkill)
        {
            if (treatment == null || patient == null) return;

            if (rng == null || !treated)
            {
                treatment.OutcomeTier = treated ? DoctorOutcomeTier.PartialBenefit : DoctorOutcomeTier.Untreated;
                treatment.OutcomeDaysReduced = 0;
                treatment.OutcomeSeverityDowngraded = false;
                treatment.OutcomeSummary = treated ? "treated (recovery not guaranteed)" : "untreated";
                return;
            }

            var input = new DoctorOutcomeInput
            {
                Procedure = DoctorProcedureKind.HouseCall,
                ConditionKind = patient.ConditionKind,
                ConditionSeverity = MapHouseCallSeverity(patient.Severity),
                SuppliesPresent = true,
            };
            DoctorTreatmentOutcome outcome = DoctorTreatmentOutcomes.ResolveOutcome(input, practitionerSkill, rng);

            treatment.OutcomeTier = outcome.Tier;
            treatment.OutcomeDaysReduced = outcome.DaysReduced;
            treatment.OutcomeSeverityDowngraded = outcome.SeverityDowngraded;
            treatment.OutcomeSummary = outcome.Summary;
            treatment.Outcome = outcome.Tier switch
            {
                DoctorOutcomeTier.FullBenefit => "treated: full benefit expected (recovery not guaranteed)",
                DoctorOutcomeTier.NoBenefit => "treated: no benefit observed",
                DoctorOutcomeTier.Untreated => "untreated",
                _ => "treated (recovery not guaranteed)",
            };
        }

        private static HealthConditionSeverity MapHouseCallSeverity(HouseCallSeverity severity)
        {
            return severity >= HouseCallSeverity.Severe
                ? HealthConditionSeverity.Serious
                : HealthConditionSeverity.Mild;
        }
    }
}
