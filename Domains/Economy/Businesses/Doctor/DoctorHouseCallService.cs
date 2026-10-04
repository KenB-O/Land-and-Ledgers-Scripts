using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
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

        public IReadOnlyList<HouseCallRequest> Queue => queue;
        public IReadOnlyList<HouseCallInvoice> CompletedInvoices => completedInvoices;

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
            foreach (var patient in request.Patients)
            {
                if (patient.Severity > worst) worst = patient.Severity;
            }

            // W1A triage policy (Canon §13.3): house calls are reserved for severe
            // illness, severe injury, immobility. This documents, not re-litigates,
            // the severity model — the daily office resolution keeps its own.
            if (worst <= HouseCallSeverity.Moderate)
            {
                string routing = worst == HouseCallSeverity.Minor
                    ? "minor case — self-manage or office visit"
                    : "moderate case — office visit";
                diagnostics.Add($"DoctorHouseCallService: house call refused for {request.PatientHouseholdId} ({routing}; Canon §13.3 reserves calls for severe illness/injury and immobility).");
                return $"DoctorHouseCallService: house call refused — {routing}.";
            }

            queue.Add(request);
            diagnostics.Add($"DoctorHouseCallService: house call queued for {request.PatientHouseholdId} ({request.Patients.Count} patient(s), worst severity {worst}, day {request.RequestDayIndex}).");
            return null;
        }

        /// <summary>
        /// Executes one queued house call: real JRN route out and back, per-patient
        /// treatment with medicine-lot provenance, itemized invoice. Returns null
        /// with diagnostics when the call cannot run.
        /// </summary>
        public HouseCallInvoice ExecuteHouseCall(
            HouseCallRequest request,
            string doctorLocationId,
            DoctorFeeSchedule fees,
            DoctorMedicineStock medicineStock,
            int dayIndex,
            JourneyModel journeys,
            List<string> diagnostics)
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
                    }
                    else
                    {
                        treatment.MedicineLotsUsed.AddRange(lines);
                        treatment.MedicineChargeCents = patient.DosesNeeded * Math.Max(0, fees.RemedyDoseChargeCents);
                        treatment.TreatmentSummary = $"treated: {patient.Ailment} ({patient.DosesNeeded} dose(s) {patient.MedicineName})";
                        treatment.Outcome = "treated (recovery not guaranteed)";
                    }
                }
                else
                {
                    treatment.TreatmentSummary = $"treated: {patient.Ailment} (labor only)";
                    treatment.Outcome = "treated (recovery not guaranteed)";
                }

                invoice.Treatments.Add(treatment);
            }

            completedInvoices.Add(invoice);
            queue.Remove(request);
            diagnostics.Add($"DoctorHouseCallService: house call to {request.PatientHouseholdId} done — {invoice.TotalCents}c total, {invoice.TotalMinutes} min professional time (day {dayIndex}).");
            return invoice;
        }
    }
}
