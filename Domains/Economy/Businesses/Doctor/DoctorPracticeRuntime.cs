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
        private DoctorFeeSchedule feeSchedule = new DoctorFeeSchedule();

        private int officeMinutesToday;
        private int houseCallMinutesToday;
        private int officeVisitsToday;
        private int houseCallsToday;

        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public DoctorMedicineStock MedicineStock => medicineStock;
        public DoctorHouseCallService HouseCallService => houseCallService;
        public DoctorFeeSchedule FeeSchedule => feeSchedule;

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
        /// Returns the number of completed calls.
        /// </summary>
        public int ExecuteQueuedHouseCalls(
            string doctorLocationId,
            int dayIndex,
            JourneyModel journeys,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            int completed = 0;

            while (houseCallService.Queue.Count > 0)
            {
                HouseCallRequest request = houseCallService.Queue[0];
                var invoice = houseCallService.ExecuteHouseCall(
                    request, doctorLocationId, feeSchedule, medicineStock, dayIndex, journeys, diag);
                if (invoice == null)
                {
                    // Loud refusal (no route, no stock path) — stays queued for tomorrow.
                    diag.Add($"Doctor practice {businessInstanceId}: house call for {request.PatientHouseholdId} could not run today — stays queued.");
                    break;
                }

                houseCallMinutesToday += invoice.TotalMinutes;
                houseCallsToday++;
                completed++;

                if (officeMinutesToday + houseCallMinutesToday >= ProfessionalMinutesPerDay)
                {
                    diag.Add($"Doctor practice {businessInstanceId}: professional day exhausted ({officeMinutesToday + houseCallMinutesToday} min) — {houseCallService.Queue.Count} call(s) still queued.");
                    break;
                }
            }

            return completed;
        }

        /// <summary>Coverage readout: the queue-and-coverage problem, plainly stated.</summary>
        public string CoverageSummary()
        {
            int used = officeMinutesToday + houseCallMinutesToday;
            return $"Doctor practice {businessInstanceId}: {officeVisitsToday} office visits, {houseCallsToday} house calls, " +
                $"{used}/{ProfessionalMinutesPerDay} professional minutes, {houseCallService.Queue.Count} call(s) still queued.";
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
        }

        public DoctorPracticeRuntimeSaveDto CaptureSaveDto()
        {
            var dto = new DoctorPracticeRuntimeSaveDto
            {
                BusinessInstanceId = businessInstanceId,
                MedicineStock = medicineStock.CaptureSaveDto(),
                FeeSchedule = feeSchedule,
            };
            dto.HouseCallQueue.AddRange(houseCallService.Queue);
            return dto;
        }

        public void LoadFromSaveDto(DoctorPracticeRuntimeSaveDto dto)
        {
            if (dto == null) return;
            medicineStock.LoadFromSaveDto(dto.MedicineStock);
            if (dto.FeeSchedule != null) feeSchedule = dto.FeeSchedule;
            houseCallService.RestoreQueue(dto.HouseCallQueue);
        }
        #endregion
    }
}
