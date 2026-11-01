using System;

namespace LandLedgers.Economy
{
    /// <summary>
    /// D4H: which draw a retainage slice is computed against.
    /// </summary>
    public enum ConstructionBillingDrawKind
    {
        Unspecified = 0,
        Deposit = 1,
        Milestone = 2,
        CompletionBalance = 3,
    }

    /// <summary>
    /// D4H: operator-policy calibration for progress billing. The canon is
    /// SILENT on retainage (Canon §7.3 names deposits, milestone/progress
    /// payments, and completion balances as legitimate terms and reserves
    /// exact percentages to contract data) — so every number here is
    /// research-grounded CALIBRATION, not canon, and fully parameterized.
    ///
    /// Research (Oct 2026): retainage (holdback) originates in the 1840s
    /// railway mania — English railroads held up to 20% against contractor
    /// default — and spread to the US, where it has been a fixture in
    /// construction for 175+ years, i.e. established practice by the 1870s.
    /// US practice through the period withheld ~10% of PROGRESS payments
    /// until completion/acceptance (later federal/state practice formalized
    /// 10% of progress payments); English practice was 5% (JCT default),
    /// half at practical completion and half after the defects period.
    /// This game is a US-frontier setting, so the default is the US figure:
    /// 10% withheld from milestone (progress) draws, released only on final
    /// acceptance. Final payment itself follows the contract terms after the
    /// owner's acceptance of the work.
    ///
    /// Retainage is applied to milestone/progress draws only — not to the
    /// deposit (due at signing, before any work) nor to the completion
    /// balance (the release payment itself) — unless a contract's operator
    /// policy says otherwise. The retained money is never moved until final
    /// acceptance; it stays the owner's money until then.
    /// </summary>
    [Serializable]
    public sealed class ConstructionProgressBillingPolicy
    {
        /// <summary>
        /// CALIBRATION: master switch for holdback. True by default because
        /// research shows retainage was the 1870s US norm; set false for
        /// contracts paid straight through (the canon reserves exact terms to
        /// contract data, so this is operator policy, not doctrine).
        /// </summary>
        public bool RetainageEnabled = true;

        /// <summary>
        /// CALIBRATION: whole-percent holdback on each eligible draw.
        /// Default 10 (US 1870s norm, per research). 0..100, clamped.
        /// </summary>
        public int RetainagePercentWhole = 10;

        /// <summary>
        /// CALIBRATION: holdback applies to milestone/progress draws.
        /// True by default (research: withheld from interim certificates).
        /// </summary>
        public bool RetainageAppliesToMilestones = true;

        /// <summary>
        /// CALIBRATION: holdback applies to the deposit. False by default —
        /// the deposit is due at signing, before any work exists to hold
        /// against, and period practice deducted retainage from progress
        /// payments, not from signing deposits.
        /// </summary>
        public bool RetainageAppliesToDeposit = false;

        /// <summary>
        /// CALIBRATION: holdback applies to the completion balance. False by
        /// default — the completion balance IS the release payment at final
        /// acceptance; holding back from it would be double retainage.
        /// </summary>
        public bool RetainageAppliesToCompletionBalance = false;

        public ConstructionProgressBillingPolicy() { }

        /// <summary>
        /// D4H: the whole-percent holdback in force, clamped to 0..100.
        /// </summary>
        public int EffectiveRetainagePercentWhole => Math.Max(0, Math.Min(100, RetainagePercentWhole));

        /// <summary>
        /// D4H: splits one draw into (retained, moved). Deterministic
        /// integer math: retained = floor(amount * percent / 100), moved =
        /// amount - retained. When the kind is not eligible under this
        /// policy, retained is 0 and the full amount moves.
        /// </summary>
        public void SplitDraw(ConstructionBillingDrawKind kind, int amountCents,
            out int retainedCents, out int movedCents)
        {
            amountCents = Math.Max(0, amountCents);
            bool eligible = RetainageEnabled && EffectiveRetainagePercentWhole > 0
                && ((kind == ConstructionBillingDrawKind.Milestone && RetainageAppliesToMilestones)
                    || (kind == ConstructionBillingDrawKind.Deposit && RetainageAppliesToDeposit)
                    || (kind == ConstructionBillingDrawKind.CompletionBalance && RetainageAppliesToCompletionBalance));
            if (!eligible)
            {
                retainedCents = 0;
                movedCents = amountCents;
                return;
            }
            retainedCents = (int)((long)amountCents * EffectiveRetainagePercentWhole / 100L);
            movedCents = Math.Max(0, amountCents - retainedCents);
        }

        /// <summary>D4H: human-readable policy summary for diagnostics.</summary>
        public string Describe()
        {
            if (!RetainageEnabled || EffectiveRetainagePercentWhole <= 0)
                return "no retainage (policy)";
            return $"{EffectiveRetainagePercentWhole}% retainage on "
                + (RetainageAppliesToMilestones ? "milestones" : "no draws")
                + (RetainageAppliesToDeposit ? " + deposit" : "")
                + (RetainageAppliesToCompletionBalance ? " + completion balance" : "")
                + ", released on final acceptance";
        }
    }
}
