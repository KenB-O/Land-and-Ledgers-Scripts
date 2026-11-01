using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy.Financing
{
    public sealed class FinancingFailureEvaluator
    {
        public FinancingFailureResult EvaluateFailure(FinancingFailureRequest request)
        {
            List<FinancingReasonCode> reasons = new List<FinancingReasonCode>();
            int earnestMoneyCents = Mathf.Max(0, request.earnestMoneyCents);
            float sellerSensitivity = Mathf.Clamp01(request.sellerRelationshipSensitivity01);
            float sellerPressure = Mathf.Clamp01(request.sellerPressure01);
            float reputationFragility = Mathf.Lerp(1.2f, 0.85f, Mathf.Clamp01(request.applicantReputation01));
            float lenderTrustFragility = Mathf.Lerp(1.2f, 0.85f, Mathf.Clamp01(request.lenderTrust01));
            bool deadlineMissed = request.closingDeadlineMissed || request.daysUntilClosingDeadline < 0;

            if (!request.hasTentativeAgreement)
            {
                reasons.Add(FinancingReasonCode.PreAgreementDenial);
                return new FinancingFailureResult(
                    request.tentativeAgreementId,
                    0f,
                    -0.015f,
                    0f,
                    0,
                    7,
                    1,
                    0.14f,
                    BuildPreAgreementSummary(request.failureReason),
                    reasons);
            }

            reasons.Add(FinancingReasonCode.TentativeAgreementAtRisk);
            if (request.financingContingencyPresent)
            {
                reasons.Add(FinancingReasonCode.FinancingContingencyProtected);
            }
            else
            {
                reasons.Add(FinancingReasonCode.FinancingContingencyMissing);
            }

            if (deadlineMissed)
            {
                reasons.Add(FinancingReasonCode.DeadlineMissed);
            }

            // Protected failures still cool the bank for a while; they simply do not burn trust as hard as an unprotected collapse.
            bool protectedFailure = request.financingContingencyPresent && !deadlineMissed;
            float pressureMultiplier = Mathf.Lerp(0.85f, 1.35f, sellerPressure);
            float sensitivityMultiplier = Mathf.Lerp(0.9f, 1.4f, sellerSensitivity);
            float severity = protectedFailure ? 0.45f : 1f;
            if (deadlineMissed)
            {
                severity += 0.35f;
            }

            int forfeitedEarnestMoneyCents = protectedFailure
                ? Mathf.RoundToInt(earnestMoneyCents * 0.25f)
                : earnestMoneyCents;
            float reputationDelta = -0.035f * severity * sensitivityMultiplier * reputationFragility;
            float lenderTrustDelta = -0.045f * severity * lenderTrustFragility;
            float sellerTrustDelta = -0.06f * severity * pressureMultiplier * sensitivityMultiplier;
            int cooldownDays = protectedFailure ? 14 : 35;
            if (deadlineMissed)
            {
                cooldownDays += 14;
            }

            int failureStrikes = protectedFailure ? 1 : 2;
            if (deadlineMissed)
            {
                failureStrikes++;
            }

            float lenderCaution01 = Mathf.Clamp01(
                0.16f
                + severity * 0.26f
                + (protectedFailure ? -0.03f : 0.08f)
                + (deadlineMissed ? 0.09f : 0f));

            string summary = BuildTentativeAgreementFailureSummary(
                protectedFailure,
                deadlineMissed,
                request.daysUntilClosingDeadline,
                forfeitedEarnestMoneyCents,
                cooldownDays,
                request.failureReason);

            return new FinancingFailureResult(
                request.tentativeAgreementId,
                reputationDelta,
                lenderTrustDelta,
                sellerTrustDelta,
                forfeitedEarnestMoneyCents,
                cooldownDays,
                failureStrikes,
                lenderCaution01,
                summary,
                reasons);
        }

        private static string BuildPreAgreementSummary(string failureReason)
        {
            string reason = string.IsNullOrWhiteSpace(failureReason) ? string.Empty : $" Reason: {failureReason.Trim()}";
            return $"The bank declined financing before a tentative agreement was signed. Seller trust is unaffected, but the lender will be cautious about an immediate re-file.{reason}";
        }

        private static string BuildTentativeAgreementFailureSummary(
            bool protectedFailure,
            bool deadlineMissed,
            int daysUntilClosingDeadline,
            int forfeitedEarnestMoneyCents,
            int cooldownDays,
            string failureReason)
        {
            string protection = protectedFailure
                ? "The financing contingency limited the seller-side damage."
                : "The file was not protected by a live financing contingency, so seller trust and earnest money are at greater risk.";
            string deadline = deadlineMissed
                ? " The closing deadline was missed, increasing lender caution."
                : daysUntilClosingDeadline <= 2
                    ? " The file was close to its closing deadline, so the bank will still treat the collapse as serious."
                    : string.Empty;
            string earnest = forfeitedEarnestMoneyCents > 0
                ? $" Estimated earnest money at risk: {FormatMoney(forfeitedEarnestMoneyCents)}."
                : " No earnest money is expected to be forfeited.";
            string reason = string.IsNullOrWhiteSpace(failureReason) ? string.Empty : $" Reason: {failureReason.Trim()}";
            return $"Financing failed after a tentative agreement. {protection}{deadline}{earnest} Expected lender cooling-off period: about {cooldownDays} days.{reason}";
        }

        private static string FormatMoney(int cents)
        {
            return $"${Mathf.Max(0, cents) / 100f:0.00}";
        }
    }
}
