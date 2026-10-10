using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Financing
{
    /// <summary>Phase E (E2): the seller's decision on a financing offer.</summary>
    public enum SellerFinanceDecisionOutcome
    {
        Accept = 0,
        Counter = 1,
        Refuse = 2,
    }

    /// <summary>
    /// Phase E: a seller's standing financing policy. Defaults are TUNING
    /// (canon sets none); individual sellers vary these, not the code.
    /// </summary>
    [Serializable]
    public sealed class SellerFinancePolicy
    {
        public float MinimumDownPaymentShare01 = 0.20f;
        public int MinimumRateBps = 500;
        public int MaximumTermDays = 1825;
        public float MaximumExposureShareOfSellerCash01 = 0.75f;
        public float HighLiquidityNeedThreshold01 = 0.80f;

        public SellerFinancePolicy() { }
    }

    /// <summary>
    /// Phase E: what the seller decides FROM. The buyer's reliability is
    /// derived ONLY from evidence the seller could legitimately know —
    /// disclosed records plus the authority's own obligations naming this
    /// seller as creditor. There is no universal credit score.
    /// </summary>
    [Serializable]
    public sealed class SellerFinanceDecisionInput
    {
        public string SellerName = string.Empty;
        public string BuyerName = string.Empty;
        public string AssetDescription = string.Empty;
        public string AssetInstanceId = string.Empty;
        public int AskingPriceCents;
        public int OfferedDownPaymentCents;
        public int OfferedRateBps;
        public int OfferedTermDays;
        /// <summary>Evidence the buyer disclosed to this seller (and only this).</summary>
        public List<CreditEvidenceRecord> BuyerDisclosedEvidence = new List<CreditEvidenceRecord>();
        /// <summary>A competing cash offer, 0 when none exists.</summary>
        public int AlternativeCashOfferCents;
        public string CollateralDescription = string.Empty;
        public string GuarantorName = string.Empty;
        public int SellerCashCents;
        /// <summary>0 = patient, 1 = needs cash now.</summary>
        public float SellerLiquidityNeed01;

        public SellerFinanceDecisionInput() { }
    }

    [Serializable]
    public sealed class SellerFinanceDecision
    {
        public SellerFinanceDecisionOutcome Outcome = SellerFinanceDecisionOutcome.Refuse;
        public int CounterDownPaymentCents;
        public int CounterRateBps;
        public int CounterTermDays;
        public string CounterGuarantorRequired = string.Empty;
        public List<string> Reasons = new List<string>();

        public SellerFinanceDecision() { }
    }

    /// <summary>
    /// Phase E (E2): the real seller decision — price, cash down, buyer
    /// reliability from legitimate evidence, available collateral, guaranties,
    /// alternative offers, maturity, rate, the seller's own need for
    /// liquidity and risk — yielding accept, counter, or refuse with reasons.
    /// Deterministic: the same inputs always decide the same way.
    /// </summary>
    public sealed class SellerFinanceDecisionService
    {
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;

        public SellerFinanceDecision Decide(SellerFinancePolicy policy,
            SellerFinanceDecisionInput input, FinancialObligationAuthority authority,
            int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var decision = new SellerFinanceDecision();
            if (policy == null) policy = new SellerFinancePolicy();
            if (input == null)
            {
                decision.Outcome = SellerFinanceDecisionOutcome.Refuse;
                decision.Reasons.Add("No offer was made.");
                return decision;
            }

            string seller = string.IsNullOrWhiteSpace(input.SellerName) ? "seller" : input.SellerName;
            string buyer = string.IsNullOrWhiteSpace(input.BuyerName) ? "buyer" : input.BuyerName;

            if (input.AskingPriceCents <= 0
                || input.OfferedDownPaymentCents < 0
                || input.OfferedDownPaymentCents > input.AskingPriceCents)
            {
                decision.Outcome = SellerFinanceDecisionOutcome.Refuse;
                decision.Reasons.Add("Price or down payment is inconsistent — no decision can be made on broken terms.");
                diag.Add($"SellerFinanceDecision: '{seller}' refused '{buyer}' — inconsistent price/down terms.");
                return decision;
            }

            int financedCents = input.AskingPriceCents - input.OfferedDownPaymentCents;

            // Buyer reliability from legitimate evidence only: disclosed
            // obligation records, summed from the shared authority.
            int knownDebtCents = 0;
            int disclosedCount = 0;
            if (authority != null && input.BuyerDisclosedEvidence != null)
            {
                foreach (CreditEvidenceRecord record in input.BuyerDisclosedEvidence)
                {
                    if (record == null || !record.DisclosedToLender) continue;
                    if (!string.Equals(record.Borrower, buyer, StringComparison.Ordinal)) continue;
                    disclosedCount++;
                    if (!string.IsNullOrWhiteSpace(record.ObligationId))
                        knownDebtCents += authority.Find(record.ObligationId)?.TotalOutstandingCents ?? 0;
                }
            }
            // Plus direct history with THIS seller (legitimate knowledge).
            int priorSatisfiedWithSeller = 0;
            int priorOwedToSeller = 0;
            if (authority != null)
            {
                foreach (FinancialObligation obligation in authority.Obligations)
                {
                    if (obligation == null) continue;
                    if (!string.Equals(obligation.Debtor, buyer, StringComparison.Ordinal)) continue;
                    if (!string.Equals(obligation.Creditor, seller, StringComparison.Ordinal)) continue;
                    if (obligation.Settled) priorSatisfiedWithSeller++;
                    else priorOwedToSeller += obligation.TotalOutstandingCents;
                }
            }

            // 1. A better cash alternative wins outright.
            if (input.AlternativeCashOfferCents >= input.AskingPriceCents)
            {
                decision.Outcome = SellerFinanceDecisionOutcome.Refuse;
                decision.Reasons.Add($"A cash offer of {input.AlternativeCashOfferCents}c meets the asking price — no financing needed.");
                diag.Add($"SellerFinanceDecision: '{seller}' refused '{buyer}' — cash alternative of {input.AlternativeCashOfferCents}c.");
                return decision;
            }

            // 2. Concentration: the financed claim must not swallow the seller's own cash.
            int maxExposure = (int)(Math.Max(0, input.SellerCashCents) * Math.Clamp(policy.MaximumExposureShareOfSellerCash01, 0f, 1f));
            if (financedCents > maxExposure && input.SellerCashCents > 0)
            {
                decision.Outcome = SellerFinanceDecisionOutcome.Refuse;
                decision.Reasons.Add($"The financed {financedCents}c exceeds the seller's concentration limit ({maxExposure}c) — too much risk on one buyer.");
                diag.Add($"SellerFinanceDecision: '{seller}' refused '{buyer}' — concentration limit.");
                return decision;
            }

            // 3. Liquidity need: a seller who needs cash now will not carry paper for a small down payment.
            if (input.SellerLiquidityNeed01 >= policy.HighLiquidityNeedThreshold01
                && input.OfferedDownPaymentCents < input.AskingPriceCents / 2)
            {
                decision.Outcome = SellerFinanceDecisionOutcome.Refuse;
                decision.Reasons.Add("The seller needs cash now and the down payment is under half the price.");
                diag.Add($"SellerFinanceDecision: '{seller}' refused '{buyer}' — liquidity need {input.SellerLiquidityNeed01:0.00}.");
                return decision;
            }

            // 4. Known disclosed debt: heavy existing obligations push the seller to demand more down or walk.
            bool counterDown = false, counterRate = false, counterTerm = false;
            string counterGuarantor = string.Empty;
            if (knownDebtCents > input.AskingPriceCents)
            {
                counterDown = true; counterRate = true;
                decision.Reasons.Add($"Buyer discloses {knownDebtCents}c of existing obligations against a {input.AskingPriceCents}c price — terms must harden.");
            }

            int minDown = (int)(input.AskingPriceCents * Math.Clamp(policy.MinimumDownPaymentShare01, 0f, 1f));
            if (input.OfferedDownPaymentCents < minDown)
            {
                counterDown = true;
                decision.Reasons.Add($"Down payment {input.OfferedDownPaymentCents}c is under the seller's {minDown}c minimum.");
            }
            if (input.OfferedRateBps < policy.MinimumRateBps)
            {
                counterRate = true;
                decision.Reasons.Add($"Rate {input.OfferedRateBps}bps is under the seller's {policy.MinimumRateBps}bps minimum.");
            }
            if (input.OfferedTermDays > policy.MaximumTermDays)
            {
                counterTerm = true;
                decision.Reasons.Add($"Term {input.OfferedTermDays} days exceeds the seller's {policy.MaximumTermDays}-day maximum.");
            }
            bool hasCollateral = !string.IsNullOrWhiteSpace(input.CollateralDescription)
                || !string.IsNullOrWhiteSpace(input.AssetInstanceId);
            bool hasGuarantor = !string.IsNullOrWhiteSpace(input.GuarantorName);
            if (!hasCollateral && !hasGuarantor && input.OfferedDownPaymentCents < input.AskingPriceCents / 4)
            {
                counterGuarantor = "required";
                decision.Reasons.Add("No collateral and no guarantor with a small down payment — a guarantor is required.");
            }

            if (counterDown || counterRate || counterTerm || !string.IsNullOrWhiteSpace(counterGuarantor))
            {
                decision.Outcome = SellerFinanceDecisionOutcome.Counter;
                decision.CounterDownPaymentCents = counterDown
                    ? Math.Max(minDown, input.OfferedDownPaymentCents)
                    : input.OfferedDownPaymentCents;
                if (knownDebtCents > input.AskingPriceCents)
                    decision.CounterDownPaymentCents = Math.Max(decision.CounterDownPaymentCents, (int)(input.AskingPriceCents * 0.40f));
                decision.CounterRateBps = counterRate
                    ? Math.Max(policy.MinimumRateBps, input.OfferedRateBps)
                    : input.OfferedRateBps;
                if (knownDebtCents > input.AskingPriceCents)
                    decision.CounterRateBps += 200;
                decision.CounterTermDays = counterTerm
                    ? Math.Min(policy.MaximumTermDays, Math.Max(1, input.OfferedTermDays))
                    : Math.Max(1, input.OfferedTermDays);
                decision.CounterGuarantorRequired = counterGuarantor;
                diag.Add($"SellerFinanceDecision: '{seller}' COUNTERED '{buyer}' — down {decision.CounterDownPaymentCents}c, " +
                    $"rate {decision.CounterRateBps}bps, term {decision.CounterTermDays}d, guarantor '{counterGuarantor}'. " +
                    $"Evidence considered: {disclosedCount} disclosed record(s), known debt {knownDebtCents}c, prior satisfied with seller {priorSatisfiedWithSeller}.");
                return decision;
            }

            decision.Outcome = SellerFinanceDecisionOutcome.Accept;
            decision.Reasons.Add($"Terms accepted: {input.OfferedDownPaymentCents}c down of {input.AskingPriceCents}c, " +
                $"{input.OfferedRateBps}bps, {input.OfferedTermDays} days. " +
                $"Evidence: {disclosedCount} disclosed record(s), known debt {knownDebtCents}c, prior satisfied with seller {priorSatisfiedWithSeller}.");
            diag.Add($"SellerFinanceDecision: '{seller}' ACCEPTED '{buyer}' — {input.OfferedDownPaymentCents}c down, {input.OfferedRateBps}bps, {input.OfferedTermDays}d.");
            return decision;
        }
    }
}
