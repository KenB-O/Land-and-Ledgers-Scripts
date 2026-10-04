using System;
using System.Collections.Generic;
using LandLedgers.World.Journeys;

namespace LandLedgers.Economy.Businesses.Doctor
{
    /// <summary>
    /// W1A: the per-instance doctor practice runtime. Holds the medicine cabinet
    /// (lots with provenance), the fee schedule, the house-call queue, and the
    /// professional-time accounting that makes the queue-and-coverage problem
    /// visible (Canon §13.3A). This runtime owns the house-call layer; the
    /// existing daily in-office treatment resolution is untouched.
    /// </summary>
    public sealed class DoctorPracticeRuntime
    {
        /// <summary>TUNING: professional minutes one practitioner can staff per day (calibration, Canon Part XV).</summary>
        public const int ProfessionalMinutesPerDay = 600;

        private readonly string businessInstanceId;
        private readonly DoctorMedicineStock medicineStock = new DoctorMedicineStock();
        private readonly DoctorHouseCallService houseCallService = new DoctorHouseCallService();
        private readonly DoctorReputationLedger reputation = new DoctorReputationLedger();
        private DoctorFeeSchedule feeSchedule = new DoctorFeeSchedule();
        private DoctorPractitioner practitioner;
        private DoctorMedicineReorderPolicy reorderPolicy = new DoctorMedicineReorderPolicy();

        /// <summary>
        /// D1A: whether transport is ready for house calls today. Canon §13.3A:
        /// horse availability (and roads, weather) can change how efficiently
        /// calls are arranged. The transport owner reports this; default true
        /// preserves prior behavior until someone reports otherwise.
        /// </summary>
        public bool TransportReadyForHouseCalls = true;

        public string TransportNotes = string.Empty;

        private int officeMinutesToday;
        private int houseCallMinutesToday;
        private int officeVisitsToday;
        private int houseCallsToday;

        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public DoctorMedicineStock MedicineStock => medicineStock;
        public DoctorHouseCallService HouseCallService => houseCallService;
        public DoctorFeeSchedule FeeSchedule => feeSchedule;
        public DoctorReputationLedger Reputation => reputation;
        public DoctorPractitioner Practitioner => practitioner;
        public DoctorMedicineReorderPolicy ReorderPolicy => reorderPolicy;

        /// <summary>
        /// D1A: effective professional minutes today. Canon §13.3D: with no
        /// available practitioner the office keeps premises, records and
        /// goodwill but loses its core professional capacity. This gates only
        /// the D1A house-call layer; the staffed daily office resolution keeps
        /// its own authority.
        /// </summary>
        public int EffectiveProfessionalMinutesPerDay =>
            practitioner == null || practitioner.IsAvailable ? ProfessionalMinutesPerDay : 0;

        /// <summary>D1A: seats (or replaces) the practitioner behind the practice.</summary>
        public void SetPractitioner(DoctorPractitioner next, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            practitioner = next;
            if (next == null)
            {
                diag.Add($"Doctor practice {businessInstanceId}: practitioner seat vacated (day {dayIndex}) — premises and records remain, professional capacity is gone.");
            }
            else
            {
                diag.Add($"Doctor practice {businessInstanceId}: practitioner '{next.DisplayName}' seated — skill {next.Skill}, status {next.Status} (day {dayIndex}).");
            }
        }

        public void SetReorderPolicy(DoctorMedicineReorderPolicy policy)
        {
            reorderPolicy = policy ?? new DoctorMedicineReorderPolicy();
        }

        public DoctorPracticeRuntime(string businessInstanceId)
        {
            this.businessInstanceId = businessInstanceId ?? string.Empty;
        }

        public void SetFeeSchedule(DoctorFeeSchedule schedule)
        {
            feeSchedule = schedule ?? new DoctorFeeSchedule();
        }

        /// <summary>Records staffed office work (from the existing daily resolution) for coverage accounting.</summary>
        public void RecordOfficeMinutes(int visits, int minutes, List<string> diag)
        {
            diag = diag ?? diagnostics;
            officeVisitsToday += Math.Max(0, visits);
            officeMinutesToday += Math.Max(0, minutes);
            diag.Add($"Doctor practice {businessInstanceId}: {visits} office visits, {minutes} min (day total {officeMinutesToday} min).");
        }

        /// <summary>Queues a house call (triage applies). Returns a rejection string, or null on success.</summary>
        public string QueueHouseCall(HouseCallRequest request, List<string> diag)
        {
            return houseCallService.QueueHouseCall(request, diag ?? diagnostics);
        }

        /// <summary>
        /// Executes queued house calls while professional minutes remain.
        /// Canon §13.3A: a doctor on a long call cannot serve office patients —
        /// the unrun queue stays queued and visible, never silently dropped.
        /// D1A: calls run worst-severity first (Canon §13.3B "immediate
        /// attention"), outcomes resolve through the outcome engine with a
        /// per-day deterministic seed, and trust follows the outcomes
        /// (Canon §13.3E). Returns the number of completed calls.
        /// </summary>
        public int ExecuteQueuedHouseCalls(
            string doctorLocationId,
            int dayIndex,
            JourneyModel journeys,
            List<string> diag)
        {
            diag = diag ?? diagnostics;

            if (EffectiveProfessionalMinutesPerDay <= 0)
            {
                diag.Add($"Doctor practice {businessInstanceId}: no professional capacity today — no available practitioner (Canon §13.3D). {houseCallService.Queue.Count} call(s) stay queued.");
                return 0;
            }

            if (!TransportReadyForHouseCalls)
            {
                diag.Add($"Doctor practice {businessInstanceId}: house calls held — no transport ready. {TransportNotes} {houseCallService.Queue.Count} call(s) stay queued (Canon §13.3A).");
                return 0;
            }

            // Deterministic per-day, per-practice seed (FNV-1a over the
            // instance id — stable across runs, unlike string.GetHashCode).
            var rng = new System.Random((int)(dayIndex * 2654435761u ^ StableHash32(businessInstanceId)));
            DoctorPractitionerSkill skill = practitioner != null
                ? practitioner.Skill
                : DoctorPractitionerSkill.Competent;

            int completed = 0;

            while (true)
            {
                HouseCallRequest request = houseCallService.DequeueNextPrioritized();
                if (request == null) break;

                var invoice = houseCallService.ExecuteHouseCall(
                    request, doctorLocationId, feeSchedule, medicineStock, dayIndex, journeys, diag, rng, skill);
                if (invoice == null)
                {
                    // Loud refusal (no route, no stock path) — stays queued for tomorrow.
                    houseCallService.RestoreQueuePrepend(request);
                    diag.Add($"Doctor practice {businessInstanceId}: house call for {request.PatientHouseholdId} could not run today — stays queued.");
                    break;
                }

                foreach (var treatment in invoice.Treatments)
                {
                    reputation.RecordTreatmentOutcome(treatment.OutcomeTier, dayIndex, diag);
                }

                houseCallMinutesToday += invoice.TotalMinutes;
                houseCallsToday++;
                completed++;

                if (officeMinutesToday + houseCallMinutesToday >= EffectiveProfessionalMinutesPerDay)
                {
                    diag.Add($"Doctor practice {businessInstanceId}: professional day exhausted ({officeMinutesToday + houseCallMinutesToday} min) — {houseCallService.Queue.Count} call(s) still queued.");
                    break;
                }
            }

            return completed;
        }

        /// <summary>
        /// D1A: ages the house-call queue — patients waiting past maxWaitDays
        /// are resolved as sought-off-map-care or untreated (Canon §13.3B),
        /// with the trust cost recorded. Returns the number aged out.
        /// </summary>
        public int AgeHouseCallQueue(int dayIndex, int maxWaitDays, bool offMapCareReachable, List<string> diag)
        {
            diag = diag ?? diagnostics;
            int before = houseCallService.DeferredOutcomes.Count;
            int agedOut = houseCallService.AgeQueue(dayIndex, maxWaitDays, offMapCareReachable, diag);
            for (int i = before; i < houseCallService.DeferredOutcomes.Count; i++)
            {
                var deferred = houseCallService.DeferredOutcomes[i];
                if (deferred.Resolution == DeferredPatientResolution.WentUntreated)
                {
                    reputation.RecordDeferredUntreated(dayIndex, diag);
                }
            }

            return agedOut;
        }

        /// <summary>
        /// D1A: evaluates what the practice can do today from its inputs.
        /// The caller reports equipment truth (bag, surgical set); stock,
        /// practitioner and transport come from this runtime.
        /// </summary>
        public DoctorPracticeCapability EvaluateCapability(bool hasDoctorBag, bool hasSurgicalSet, List<string> diag)
        {
            var input = new DoctorPracticeCapabilityInput
            {
                PractitionerAvailable = practitioner == null || practitioner.IsAvailable,
                PractitionerSkill = practitioner != null ? practitioner.Skill : DoctorPractitionerSkill.Competent,
                HasDoctorBag = hasDoctorBag,
                HasSurgicalSet = hasSurgicalSet,
                DressingsOnHand = medicineStock.DosesOnHand(DoctorTreatmentCatalog.DressingItemId),
                MedicinesOnHand = medicineStock.DosesOnHand(DoctorTreatmentCatalog.MedicineDoseItemId),
                TransportReady = TransportReadyForHouseCalls,
            };
            return DoctorPracticeCapability.Evaluate(input, diag ?? diagnostics);
        }

        /// <summary>D1A: medicine reorder signals — data for the caller's real import orders.</summary>
        public List<DoctorMedicineReorderSignal> EvaluateMedicineReorder(int dayIndex, List<string> diag)
        {
            return DoctorMedicinePolicy.EvaluateReorderSignals(medicineStock, reorderPolicy, dayIndex, diag ?? diagnostics);
        }

        private static uint StableHash32(string value)
        {
            uint hash = 2166136261u;
            if (!string.IsNullOrEmpty(value))
            {
                foreach (char c in value)
                {
                    hash ^= c;
                    hash *= 16777619u;
                }
            }

            return hash;
        }

        /// <summary>Coverage readout: the queue-and-coverage problem, plainly stated.</summary>
        public string CoverageSummary()
        {
            int used = officeMinutesToday + houseCallMinutesToday;
            return $"Doctor practice {businessInstanceId}: {officeVisitsToday} office visits, {houseCallsToday} house calls, " +
                $"{used}/{EffectiveProfessionalMinutesPerDay} professional minutes, {houseCallService.Queue.Count} call(s) still queued, " +
                $"trust {reputation.Trust}.";
        }

        public void CloseDay(int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            diag.Add($"Doctor practice {businessInstanceId}: day {dayIndex} — {CoverageSummary()}");
            officeMinutesToday = 0;
            houseCallMinutesToday = 0;
            officeVisitsToday = 0;
            houseCallsToday = 0;
        }

        #region Save / Load
        [Serializable]
        public sealed class DoctorPracticeRuntimeSaveDto
        {
            public string BusinessInstanceId = string.Empty;
            public DoctorMedicineStock.DoctorMedicineStockSaveDto MedicineStock = new DoctorMedicineStock.DoctorMedicineStockSaveDto();
            public DoctorFeeSchedule FeeSchedule = new DoctorFeeSchedule();
            public List<HouseCallRequest> HouseCallQueue = new List<HouseCallRequest>();
            public DoctorPractitioner Practitioner;
            public bool TransportReadyForHouseCalls = true;
            public string TransportNotes = string.Empty;
            public DoctorReputationLedger.DoctorReputationSaveDto Reputation = new DoctorReputationLedger.DoctorReputationSaveDto();
            public DoctorMedicineReorderPolicy ReorderPolicy = new DoctorMedicineReorderPolicy();
            public List<HouseCallDeferredOutcome> DeferredOutcomes = new List<HouseCallDeferredOutcome>();
        }

        public DoctorPracticeRuntimeSaveDto CaptureSaveDto()
        {
            var dto = new DoctorPracticeRuntimeSaveDto
            {
                BusinessInstanceId = businessInstanceId,
                MedicineStock = medicineStock.CaptureSaveDto(),
                FeeSchedule = feeSchedule,
                Practitioner = practitioner,
                TransportReadyForHouseCalls = TransportReadyForHouseCalls,
                TransportNotes = TransportNotes ?? string.Empty,
                Reputation = reputation.CaptureSaveDto(),
                ReorderPolicy = reorderPolicy,
            };
            dto.HouseCallQueue.AddRange(houseCallService.Queue);
            dto.DeferredOutcomes.AddRange(houseCallService.DeferredOutcomes);
            return dto;
        }

        public void LoadFromSaveDto(DoctorPracticeRuntimeSaveDto dto)
        {
            if (dto == null) return;
            medicineStock.LoadFromSaveDto(dto.MedicineStock);
            if (dto.FeeSchedule != null) feeSchedule = dto.FeeSchedule;
            houseCallService.RestoreQueue(dto.HouseCallQueue);
            houseCallService.RestoreDeferredOutcomes(dto.DeferredOutcomes);
            practitioner = dto.Practitioner;
            TransportReadyForHouseCalls = dto.TransportReadyForHouseCalls;
            TransportNotes = dto.TransportNotes ?? string.Empty;
            reputation.LoadFromSaveDto(dto.Reputation);
            if (dto.ReorderPolicy != null) reorderPolicy = dto.ReorderPolicy;
        }
        #endregion
    }
}
