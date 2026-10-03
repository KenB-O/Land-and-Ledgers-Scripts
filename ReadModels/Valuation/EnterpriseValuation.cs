using System;
using System.Collections.Generic;
using UnityEngine;
using LandLedgers.Persistence;

namespace LandLedgers.ReadModels.Valuation
{
    /// <summary>
    /// BIZ-5: how much valuation evidence backs the numbers (Tech X §9.3 —
    /// "a range/confidence when evidence is weak, not false precision").
    /// </summary>
    public enum ValuationConfidence
    {
        Unspecified = 0,
        Low = 1,     // Fewer than 4 weeks of profit evidence.
        Medium = 2,  // 4–12 weeks.
        High = 3,    // More than 12 weeks.
    }

    /// <summary>
    /// BIZ-5: tuning for the valuation formula. Documented tuning, not canon —
    /// Kennedy adjusts these; the STRUCTURE (two values, cash exclusion, owner
    /// normalization) is the canon lock.
    /// </summary>
    [Serializable]
    public sealed class ValuationTuning
    {
        [SerializeField, Min(0.5f)]
        [Tooltip("Earnings multiple applied to maintainable annual operating profit.")]
        private float earningsMultiple = 3f;

        [SerializeField, Min(1)]
        [Tooltip("Weeks of profit history used for the maintainable-profit window.")]
        private int maintainableWindowWeeks = 12;

        public float EarningsMultiple => Mathf.Max(0.5f, earningsMultiple);
        public int MaintainableWindowWeeks => Mathf.Max(1, maintainableWindowWeeks);

        public static ValuationTuning Default() => new ValuationTuning();
    }

    /// <summary>
    /// BIZ-5: valuation evidence for one business (Tech X §9.3 inputs).
    /// NOTE what is deliberately absent: business cash and headcount. Cash contributes
    /// ZERO to objective value (Canon §11.4) and headcount adds nothing by itself
    /// (Canon §11.5) — they are not parameters, so they cannot affect the result
    /// by construction. This is enforcement, not documentation.
    /// </summary>
    [Serializable]
    public sealed class ValuationEvidence
    {
        [SerializeField]
        private string businessInstanceId = string.Empty;

        [SerializeField]
        private List<int> weeklyOperatingProfitCents = new List<int>();

        [SerializeField, Min(0)]
        private int ownerWeeklyHoursTenth;

        [SerializeField, Min(0)]
        private int ownerReplacementCostPerHourCents;

        [SerializeField, Min(0)]
        private int ownerActualWeeklyDrawCents;

        [SerializeField, Min(0)]
        private int businessSpecificLiabilitiesCents;

        [SerializeField, Min(0)]
        [Tooltip("Transferable productive-asset value: creates an asset FLOOR, never blindly added on top of earnings (Tech X §9.3).")]
        private int transferableAssetValueCents;

        public string BusinessInstanceId => businessInstanceId ?? string.Empty;
        public IReadOnlyList<int> WeeklyOperatingProfitCents => weeklyOperatingProfitCents;
        public float OwnerWeeklyHours => ownerWeeklyHoursTenth / 10f;
        public int OwnerReplacementCostPerHourCents => Mathf.Max(0, ownerReplacementCostPerHourCents);
        public int OwnerActualWeeklyDrawCents => Mathf.Max(0, ownerActualWeeklyDrawCents);
        public int BusinessSpecificLiabilitiesCents => Mathf.Max(0, businessSpecificLiabilitiesCents);
        public int TransferableAssetValueCents => Mathf.Max(0, transferableAssetValueCents);

        public ValuationEvidence(string businessInstanceId)
        {
            this.businessInstanceId = businessInstanceId ?? string.Empty;
        }

        public void RecordWeeklyProfit(int profitCents)
        {
            weeklyOperatingProfitCents ??= new List<int>();
            weeklyOperatingProfitCents.Add(profitCents);
        }

        public void SetOwnerLabor(float weeklyHours, int replacementCostPerHourCents, int actualWeeklyDrawCents)
        {
            ownerWeeklyHoursTenth = Mathf.Max(0, Mathf.RoundToInt(weeklyHours * 10f));
            ownerReplacementCostPerHourCents = Mathf.Max(0, replacementCostPerHourCents);
            ownerActualWeeklyDrawCents = Mathf.Max(0, actualWeeklyDrawCents);
        }

        public void SetLiabilities(int liabilitiesCents)
        {
            businessSpecificLiabilitiesCents = Mathf.Max(0, liabilitiesCents);
        }

        public void SetTransferableAssetValue(int assetValueCents)
        {
            transferableAssetValueCents = Mathf.Max(0, assetValueCents);
        }
    }

    /// <summary>
    /// BIZ-5: the valuation result — ALWAYS two values (Canon §11.3 CANON LOCK,
    /// Tech X §9.3 TECH LOCK): gross going-concern value and owner equity after
    /// business-specific liabilities. Scenario objectives measuring what the player
    /// built use <see cref="OwnerEquityValueCents"/>.
    /// </summary>
    [Serializable]
    public sealed class EnterpriseValuationResult
    {
        [SerializeField]
        private string businessInstanceId = string.Empty;

        [SerializeField]
        private int grossGoingConcernValueCents;

        [SerializeField]
        private int ownerEquityValueCents;

        [SerializeField]
        private int lowEstimateCents;

        [SerializeField]
        private int highEstimateCents;

        [SerializeField]
        private ValuationConfidence confidence;

        [SerializeField]
        private string derivationNotes = string.Empty;

        public string BusinessInstanceId => businessInstanceId ?? string.Empty;

        /// <summary>Estimated sale value of the business as a going concern.</summary>
        public int GrossGoingConcernValueCents => grossGoingConcernValueCents;

        /// <summary>Transferable value to the owner after business-specific liabilities.</summary>
        public int OwnerEquityValueCents => ownerEquityValueCents;

        public int LowEstimateCents => lowEstimateCents;
        public int HighEstimateCents => highEstimateCents;
        public ValuationConfidence Confidence => confidence;
        public string DerivationNotes => derivationNotes ?? string.Empty;

        public EnterpriseValuationResult(string businessInstanceId)
        {
            this.businessInstanceId = businessInstanceId ?? string.Empty;
        }

        internal void SetValues(int gross, int equity, int low, int high,
            ValuationConfidence confidence, string notes)
        {
            grossGoingConcernValueCents = gross;
            ownerEquityValueCents = equity;
            lowEstimateCents = low;
            highEstimateCents = high;
            this.confidence = confidence;
            derivationNotes = notes ?? string.Empty;
        }
    }

    /// <summary>
    /// BIZ-5: the valuation formula (Canon §11.3–11.5, Tech X §9.3).
    /// 1. Maintainable weekly operating profit = trailing-window average — never the
    ///    best short period annualized (Canon §11.5).
    /// 2. Owner labor normalized at replacement cost: transferable profit is what
    ///    remains after paying a competent replacement for the owner's actual hours.
    /// 3. Gross = annualized normalized profit × earnings multiple, with the
    ///    transferable-asset value as a FLOOR (never blindly added on top).
    /// 4. Owner equity = gross − business-specific liabilities.
    /// 5. Cash is not an input — it contributes zero by construction (Canon §11.4).
    ///    Headcount is not an input — it adds nothing by itself (Canon §11.5).
    /// </summary>
    public static class EnterpriseValuation
    {
        public static EnterpriseValuationResult Evaluate(
            ValuationEvidence evidence,
            ValuationTuning tuning)
        {
            var result = new EnterpriseValuationResult(
                evidence != null ? evidence.BusinessInstanceId : string.Empty);
            tuning ??= ValuationTuning.Default();

            if (evidence == null)
            {
                result.SetValues(0, 0, 0, 0, ValuationConfidence.Low, "No evidence.");
                return result;
            }

            // Maintainable weekly profit: trailing window average (Canon §11.5).
            var profits = evidence.WeeklyOperatingProfitCents;
            int window = Math.Min(tuning.MaintainableWindowWeeks,
                profits != null ? profits.Count : 0);
            long sum = 0;
            for (int i = 0; i < window; i++)
            {
                sum += profits[profits.Count - window + i];
            }

            double maintainableWeekly = window > 0 ? (double)sum / window : 0.0;

            // Owner-labor normalization (Canon §11.5): the reported profit may include
            // unpaid owner hours. Transferable profit pays a competent replacement.
            double ownerReplacementWeekly =
                evidence.OwnerWeeklyHours * evidence.OwnerReplacementCostPerHourCents;
            double normalizedWeekly = maintainableWeekly
                + evidence.OwnerActualWeeklyDrawCents
                - ownerReplacementWeekly;

            double annualized = normalizedWeekly * 52.0;
            double earningsValue = Math.Max(0.0, annualized) * tuning.EarningsMultiple;

            // Asset floor, not asset addition (Tech X §9.3).
            double gross = Math.Max(earningsValue, evidence.TransferableAssetValueCents);

            double equity = gross - evidence.BusinessSpecificLiabilitiesCents;

            ValuationConfidence confidence = window < 4 ? ValuationConfidence.Low
                : window <= 12 ? ValuationConfidence.Medium
                : ValuationConfidence.High;

            // Range widens as confidence falls (Tech X §9.3: no false precision).
            double spread = confidence == ValuationConfidence.Low ? 0.25
                : confidence == ValuationConfidence.Medium ? 0.10 : 0.05;
            int grossCents = (int)Math.Round(gross);
            int equityCents = (int)Math.Round(equity);

            string notes = $"Maintainable weekly {maintainableWeekly:F0}c over {window}w; " +
                $"owner-normalized {normalizedWeekly:F0}c/wk; ×{tuning.EarningsMultiple:F1} = " +
                $"{earningsValue:F0}c earnings value; asset floor {evidence.TransferableAssetValueCents}c; " +
                $"liabilities {evidence.BusinessSpecificLiabilitiesCents}c. " +
                "Cash excluded by construction (Canon §11.4).";

            result.SetValues(grossCents, equityCents,
                (int)Math.Round(equity * (1 - spread)),
                (int)Math.Round(equity * (1 + spread)),
                confidence, notes);
            return result;
        }
    }

    /// <summary>
    /// BIZ-5: the event-fed valuation read model (Tech X §9.3). Recomputes on economic
    /// events — weekly profit postings, liability changes, owner-labor updates — never
    /// per-frame. Scenario equity goals read <see cref="TotalOwnerEquityCents"/>.
    /// </summary>
    [Serializable]
    public sealed class EnterpriseValuationReadModel
    {
        [Serializable]
        private sealed class BusinessEntry
        {
            public string businessInstanceId = string.Empty;
            public string ownerKey = string.Empty;
            public bool playerOwned;
            public ValuationEvidence evidence;
            public EnterpriseValuationResult cached;
        }

        /// <summary>CLN-1: persisted valuation entry (save pipeline).</summary>
        [Serializable]
        public sealed class BusinessExport
        {
            public string businessInstanceId = string.Empty;
            public string ownerKey = string.Empty;
            public bool playerOwned;
            public ValuationEvidence evidence;
            public EnterpriseValuationResult cached;
        }

        [SerializeField]
        private List<BusinessEntry> entries = new List<BusinessEntry>();

        [SerializeField]
        private ValuationTuning tuning = ValuationTuning.Default();

        public ValuationTuning Tuning => tuning ??= ValuationTuning.Default();

        /// <summary>Registers a business for valuation tracking.</summary>
        public void RegisterBusiness(string businessInstanceId, string ownerKey, bool playerOwned)
        {
            entries ??= new List<BusinessEntry>();
            if (Find(businessInstanceId) != null || string.IsNullOrWhiteSpace(businessInstanceId))
            {
                return;
            }

            entries.Add(new BusinessEntry
            {
                businessInstanceId = businessInstanceId,
                ownerKey = ownerKey ?? string.Empty,
                playerOwned = playerOwned,
                evidence = new ValuationEvidence(businessInstanceId),
            });
        }

        /// <summary>Economic event: a week's operating profit posted.</summary>
        public void RecordWeeklyProfit(string businessInstanceId, int profitCents)
        {
            BusinessEntry entry = Find(businessInstanceId);
            if (entry == null)
            {
                return;
            }

            entry.evidence.RecordWeeklyProfit(profitCents);
            entry.cached = null; // Recompute on next read (event-fed).
        }

        /// <summary>Economic event: liabilities changed.</summary>
        public void RecordLiabilities(string businessInstanceId, int liabilitiesCents)
        {
            BusinessEntry entry = Find(businessInstanceId);
            if (entry == null)
            {
                return;
            }

            entry.evidence.SetLiabilities(liabilitiesCents);
            entry.cached = null;
        }

        /// <summary>Economic event: owner labor facts changed.</summary>
        public void RecordOwnerLabor(string businessInstanceId, float weeklyHours,
            int replacementCostPerHourCents, int actualWeeklyDrawCents)
        {
            BusinessEntry entry = Find(businessInstanceId);
            if (entry == null)
            {
                return;
            }

            entry.evidence.SetOwnerLabor(weeklyHours, replacementCostPerHourCents, actualWeeklyDrawCents);
            entry.cached = null;
        }

        public void RecordTransferableAssets(string businessInstanceId, int assetValueCents)
        {
            BusinessEntry entry = Find(businessInstanceId);
            if (entry == null)
            {
                return;
            }

            entry.evidence.SetTransferableAssetValue(assetValueCents);
            entry.cached = null;
        }

        /// <summary>Current valuation for a business (recomputed if events arrived).</summary>
        public EnterpriseValuationResult GetValuation(string businessInstanceId)
        {
            BusinessEntry entry = Find(businessInstanceId);
            if (entry == null)
            {
                return new EnterpriseValuationResult(businessInstanceId);
            }

            entry.cached ??= EnterpriseValuation.Evaluate(entry.evidence, Tuning);
            return entry.cached;
        }

        /// <summary>
        /// Sum of OwnerEquityValue across the player's businesses — the number scenario
        /// equity goals measure (Canon §11.3: objectives use owner equity).
        /// </summary>
        public int TotalPlayerOwnerEquityCents()
        {
            int total = 0;
            if (entries != null)
            {
                foreach (BusinessEntry entry in entries)
                {
                    if (entry.playerOwned)
                    {
                        total += GetValuation(entry.businessInstanceId).OwnerEquityValueCents;
                    }
                }
            }

            return total;
        }

        public int TrackedBusinessCount => entries != null ? entries.Count : 0;

        private BusinessEntry Find(string businessInstanceId)
        {
            if (entries == null || string.IsNullOrWhiteSpace(businessInstanceId))
            {
                return null;
            }

            foreach (BusinessEntry entry in entries)
            {
                if (entry.businessInstanceId == businessInstanceId)
                {
                    return entry;
                }
            }

            return null;
        }

        /// <summary>
        /// CLN-1: captures valuation state for the save pipeline. Evidence and cached
        /// results round-trip; the read model never recomputes from scratch on load.
        /// </summary>
        public ValuationReadModelSaveDto CaptureSaveDto()
        {
            var dto = new ValuationReadModelSaveDto();
            if (entries != null)
            {
                foreach (BusinessEntry entry in entries)
                {
                    if (entry == null)
                    {
                        continue;
                    }

                    dto.entries.Add(new BusinessExport
                    {
                        businessInstanceId = entry.businessInstanceId,
                        ownerKey = entry.ownerKey,
                        playerOwned = entry.playerOwned,
                        evidence = entry.evidence,
                        cached = entry.cached,
                    });
                }
            }

            return dto;
        }

        /// <summary>CLN-1: restores valuation state from the save pipeline.</summary>
        public void LoadFromSaveDto(ValuationReadModelSaveDto dto)
        {
            entries ??= new List<BusinessEntry>();
            entries.Clear();
            if (dto == null || dto.entries == null)
            {
                return;
            }

            foreach (BusinessExport exported in dto.entries)
            {
                if (exported == null || string.IsNullOrWhiteSpace(exported.businessInstanceId))
                {
                    continue;
                }

                entries.Add(new BusinessEntry
                {
                    businessInstanceId = exported.businessInstanceId,
                    ownerKey = exported.ownerKey ?? string.Empty,
                    playerOwned = exported.playerOwned,
                    evidence = exported.evidence ?? new ValuationEvidence(exported.businessInstanceId),
                    cached = exported.cached,
                });
            }
        }
    }
}
