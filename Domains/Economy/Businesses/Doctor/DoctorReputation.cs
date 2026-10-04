using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Doctor
{
    /// <summary>D1A: one trust event in the practice's reputation history.</summary>
    [Serializable]
    public sealed class DoctorTrustEvent
    {
        public int DayIndex;
        public string Description = string.Empty;
        public int Delta;

        public DoctorTrustEvent() { }
    }

    /// <summary>
    /// D1A: the practice's local reputation. Canon §13.3E: "Households should
    /// learn about the doctor through direct treatment, neighbors, employers
    /// and local reputation rather than omniscient quality scores" and "A
    /// reputable practice can draw patients from nearby nodes, while repeated
    /// absence, poor outcomes or inability to respond can weaken trust even if
    /// the doctor remains technically competent."
    ///
    /// This ledger is the practice-side record: event-sourced trust with a
    /// demand-pull readout. Household-by-household learning spread belongs to
    /// the population domain and is deliberately NOT modeled here (routed
    /// around, not re-litigated).
    /// </summary>
    [Serializable]
    public sealed class DoctorReputationLedger
    {
        /// <summary>TUNING: opening trust of a newly acquired practice (calibration, Canon Part XV).</summary>
        public const int OpeningTrust = 50;

        public int Trust = OpeningTrust;
        public List<DoctorTrustEvent> Events = new List<DoctorTrustEvent>();

        public DoctorReputationLedger() { }

        /// <summary>Records a trust event; trust stays in 0..100.</summary>
        public void RecordEvent(string description, int delta, int dayIndex, List<string> diagnostics)
        {
            Trust = Mathf.Clamp(Trust + delta, 0, 100);
            Events.Add(new DoctorTrustEvent
            {
                DayIndex = dayIndex,
                Description = description ?? string.Empty,
                Delta = delta,
            });
            diagnostics?.Add($"Doctor reputation: {(delta >= 0 ? "+" : "")}{delta} — {description} (trust now {Trust}).");
        }

        /// <summary>Trust effect of one resolved treatment outcome.</summary>
        public void RecordTreatmentOutcome(DoctorOutcomeTier tier, int dayIndex, List<string> diagnostics)
        {
            switch (tier)
            {
                case DoctorOutcomeTier.FullBenefit:
                    RecordEvent("treatment: full benefit", 2, dayIndex, diagnostics);
                    break;
                case DoctorOutcomeTier.PartialBenefit:
                    RecordEvent("treatment: partial benefit", 1, dayIndex, diagnostics);
                    break;
                case DoctorOutcomeTier.NoBenefit:
                    RecordEvent("treatment: no benefit observed", -1, dayIndex, diagnostics);
                    break;
                case DoctorOutcomeTier.Untreated:
                    RecordEvent("patient went untreated (no supplies / no response)", -2, dayIndex, diagnostics);
                    break;
            }
        }

        public void RecordUnansweredCall(int dayIndex, List<string> diagnostics) =>
            RecordEvent("house call requested but not answered", -1, dayIndex, diagnostics);

        public void RecordDeferredUntreated(int dayIndex, List<string> diagnostics) =>
            RecordEvent("queued patient waited out and went untreated", -2, dayIndex, diagnostics);

        public void RecordGenerousCredit(int dayIndex, List<string> diagnostics) =>
            RecordEvent("treatment carried on account (generous credit)", 1, dayIndex, diagnostics);

        public void RecordBadDebt(int dayIndex, List<string> diagnostics) =>
            RecordEvent("account written off as bad debt", -1, dayIndex, diagnostics);

        /// <summary>
        /// Demand pull: a reputable practice draws patients from nearby nodes
        /// (Canon §13.3E). 1.0 at trust ≤ 60, rising to 1.2 at trust 100.
        /// Calibration; consumed by whatever resolves patient demand.
        /// </summary>
        public float DemandPullMultiplier()
        {
            return 1f + Mathf.Max(0, Trust - 60) / 200f;
        }

        #region Save / Load
        [Serializable]
        public sealed class DoctorReputationSaveDto
        {
            public int Trust = OpeningTrust;
            public List<DoctorTrustEvent> Events = new List<DoctorTrustEvent>();
        }

        public DoctorReputationSaveDto CaptureSaveDto()
        {
            var dto = new DoctorReputationSaveDto { Trust = Trust };
            dto.Events.AddRange(Events);
            return dto;
        }

        public void LoadFromSaveDto(DoctorReputationSaveDto dto)
        {
            Trust = dto != null ? Mathf.Clamp(dto.Trust, 0, 100) : OpeningTrust;
            Events.Clear();
            if (dto?.Events != null) Events.AddRange(dto.Events);
        }
        #endregion
    }
}
