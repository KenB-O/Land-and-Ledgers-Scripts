using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Businesses.Doctor
{
    /// <summary>
    /// D1A: the practice's medicine reorder policy. The bootstrap endowment is
    /// one-time and lots never auto-replenish (upstream-provenance doctrine) —
    /// so the practice needs a visible reorder signal before the cabinet runs
    /// dry. This produces DATA: the caller places real import orders through
    /// ImportService (DoctorMedicineSupply), which arrive as named lots.
    /// Thresholds are calibration (Canon Part XV), settable per practice.
    /// </summary>
    [Serializable]
    public sealed class DoctorMedicineReorderPolicy
    {
        public int RemedyThresholdDoses = 20;
        public int DressingThresholdDoses = 10;
        public int RemedyOrderDoses = 60;
        public int DressingOrderDoses = 30;

        public DoctorMedicineReorderPolicy() { }
    }

    /// <summary>D1A: one reorder signal — a medicine below its threshold.</summary>
    [Serializable]
    public sealed class DoctorMedicineReorderSignal
    {
        public string MedicineName = string.Empty;
        public int DosesOnHand;
        public int ThresholdDoses;
        public int SuggestedOrderDoses;
        public int DayIndex;
        public string Reason = string.Empty;

        public DoctorMedicineReorderSignal() { }
    }

    /// <summary>D1A: evaluates reorder signals against the medicine cabinet.</summary>
    public static class DoctorMedicinePolicy
    {
        /// <summary>
        /// Returns a signal for each tracked medicine at or below its
        /// threshold. Empty list = stock is fine. Signals never order —
        /// ordering is the caller's real import order.
        /// </summary>
        public static List<DoctorMedicineReorderSignal> EvaluateReorderSignals(
            DoctorMedicineStock stock,
            DoctorMedicineReorderPolicy policy,
            int dayIndex,
            List<string> diagnostics)
        {
            var signals = new List<DoctorMedicineReorderSignal>();
            diagnostics = diagnostics ?? new List<string>();
            policy = policy ?? new DoctorMedicineReorderPolicy();
            if (stock == null)
            {
                diagnostics.Add("DoctorMedicinePolicy: no medicine stock — reorder not evaluated.");
                return signals;
            }

            CheckMedicine(stock, DoctorTreatmentCatalog.MedicineDoseItemId,
                policy.RemedyThresholdDoses, policy.RemedyOrderDoses, dayIndex, signals, diagnostics);
            CheckMedicine(stock, DoctorTreatmentCatalog.DressingItemId,
                policy.DressingThresholdDoses, policy.DressingOrderDoses, dayIndex, signals, diagnostics);

            if (signals.Count == 0)
            {
                diagnostics.Add("DoctorMedicinePolicy: cabinet above reorder thresholds — no signal.");
            }

            return signals;
        }

        private static void CheckMedicine(
            DoctorMedicineStock stock,
            string medicineName,
            int threshold,
            int orderQty,
            int dayIndex,
            List<DoctorMedicineReorderSignal> signals,
            List<string> diagnostics)
        {
            int onHand = stock.DosesOnHand(medicineName);
            if (onHand > Math.Max(0, threshold)) return;

            var signal = new DoctorMedicineReorderSignal
            {
                MedicineName = medicineName,
                DosesOnHand = onHand,
                ThresholdDoses = Math.Max(0, threshold),
                SuggestedOrderDoses = Math.Max(1, orderQty),
                DayIndex = dayIndex,
                Reason = $"'{medicineName}': {onHand} doses on hand at/below threshold {threshold} — " +
                    "place a real import order (DoctorMedicineSupply.MedicineStockMaterialId); lots never auto-replenish.",
            };
            signals.Add(signal);
            diagnostics.Add($"DoctorMedicinePolicy: REORDER SIGNAL — {signal.Reason}");
        }
    }
}
