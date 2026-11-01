using System;
using System.Collections.Generic;
using LandLedgers.Time;
using UnityEngine;

namespace LandLedgers.Economy.Financing
{
    /// <summary>
    /// Pass VIII: the shared working-capital underwriting authority. The
    /// player-debt lane (PlayerDebtManager) and the production business
    /// lender (BusinessLenderService) both underwrite through this one
    /// implementation: real projections, real exposure, real collateral and
    /// lender standing are evaluated here, and the file may be refused.
    /// Extracted verbatim from PlayerDebtManager so the player lane keeps
    /// identical behavior through its delegating entry point.
    /// </summary>
    public static class WorkingCapitalUnderwriting
    {
        private const int LoanTermMonths = 12;
        private const int MinimumPrincipalCapacityCents = 50000;
        private const int FirstPaymentOffsetDays = 7;

        public static BankLoanUnderwritingResult EvaluateWorkingCapitalApplication(
            int requestedPrincipalCents,
            int cashCents,
            int ownedAssetValueCents,
            int weeklyNetCashFlowCents,
            int existingDebtPaymentCents,
            float lenderTrust01,
            int priorFailedFinancingCount,
            int priorDefaultCount,
            bool hasActiveBankLoan,
            LenderProfile lenderProfile,
            int currentDayIndex,
            int lenderCooldownUntilDayIndex,
            float lenderCaution01)
        {
            LenderProfile sanitizedLender = lenderProfile.Sanitized();
            float trust = Mathf.Clamp01(lenderTrust01);
            int cautionDaysRemaining = GetRemainingCooldownDays(currentDayIndex, lenderCooldownUntilDayIndex);
            float activeLenderCaution01 = cautionDaysRemaining > 0 ? Mathf.Clamp01(lenderCaution01) : 0f;
            float effectiveTrust = Mathf.Clamp01(trust - activeLenderCaution01 * 0.16f);
            int effectivePriorFailedFinancingCount = priorFailedFinancingCount + (cautionDaysRemaining > 0 ? Mathf.Max(1, Mathf.CeilToInt(activeLenderCaution01 * 2f)) : 0);
            int principal = Mathf.Max(0, requestedPrincipalCents);
            LoanTermStructure term = BuildTerm(principal, sanitizedLender, effectiveTrust);
            int estimatedPayment = PaymentScheduleBuilder.EstimatePaymentCents(term);
            LoanPaymentSchedule estimateSchedule = new PaymentScheduleBuilder().Build(
                "bank_loan_estimate",
                term,
                new SimulationDate(0, 0, 1, 1, 1, 1, 1),
                FirstPaymentOffsetDays,
                CalendarAnchor.Default);

            int safeCash = Mathf.Max(0, cashCents);
            int safeAssetValue = Mathf.Max(0, ownedAssetValueCents);
            int safeWeeklyNetCashFlow = Mathf.Max(0, weeklyNetCashFlowCents);
            int existingDebtPayment = Mathf.Max(0, existingDebtPaymentCents);
            int principalCapacity = CalculateWorkingCapitalPrincipalCapacityCents(safeCash, safeAssetValue, effectiveTrust, sanitizedLender);
            int weeklyPaymentCapacity = CalculateWorkingCapitalWeeklyPaymentCapacityCents(safeCash, safeWeeklyNetCashFlow, existingDebtPayment, sanitizedLender);
            if (activeLenderCaution01 > 0f)
            {
                principalCapacity = Mathf.RoundToInt(principalCapacity * Mathf.Lerp(1f, 0.82f, activeLenderCaution01));
                weeklyPaymentCapacity = Mathf.RoundToInt(weeklyPaymentCapacity * Mathf.Lerp(1f, 0.86f, activeLenderCaution01));
            }

            float lenderTrustFloor01 = Mathf.Clamp01(GetWorkingCapitalTrustFloor01(sanitizedLender) + activeLenderCaution01 * 0.08f);
            List<FinancingReasonCode> reasons = new();

            if (principal <= 0)
            {
                reasons.Add(FinancingReasonCode.OutsideLenderPolicy);
            }

            if (!sanitizedLender.AllowsPurpose(LoanPurpose.WorkingCapital))
            {
                reasons.Add(FinancingReasonCode.OutsideLenderPolicy);
            }

            if (hasActiveBankLoan)
            {
                reasons.Add(FinancingReasonCode.HeavyDebtBurden);
            }

            if (effectiveTrust < lenderTrustFloor01)
            {
                reasons.Add(FinancingReasonCode.LowLenderTrust);
            }

            if (effectivePriorFailedFinancingCount >= 2 || priorDefaultCount >= 2)
            {
                reasons.Add(FinancingReasonCode.FailedClosingHistory);
            }

            if (cautionDaysRemaining > 0)
            {
                reasons.Add(FinancingReasonCode.RecentLenderSetback);
            }

            if (principal > principalCapacity)
            {
                reasons.Add(FinancingReasonCode.ExceedsLoanToValue);
            }

            if (estimatedPayment > weeklyPaymentCapacity)
            {
                reasons.Add(existingDebtPayment > 0 ? FinancingReasonCode.HeavyDebtBurden : FinancingReasonCode.WeakCashFlow);
            }

            bool approved = reasons.Count == 0;
            float principalUtilization01 = principalCapacity > 0 ? Mathf.Clamp01((float)principal / Mathf.Max(1, principalCapacity)) : 1f;
            float paymentUtilization01 = weeklyPaymentCapacity > 0 ? Mathf.Clamp01((float)estimatedPayment / Mathf.Max(1, weeklyPaymentCapacity)) : 1f;
            BankLoanReviewBand reviewBand = DetermineReviewBand(
                approved,
                effectiveTrust,
                principalUtilization01,
                paymentUtilization01,
                effectivePriorFailedFinancingCount,
                priorDefaultCount,
                sanitizedLender);
            int reviewDelayDays = GetReviewDelayDays(reviewBand, sanitizedLender) + (cautionDaysRemaining > 0 ? 1 : 0);
            int reviewDays = Mathf.Max(1, 1 + reviewDelayDays);

            if (approved)
            {
                reasons.Add(FinancingReasonCode.WithinLenderPolicy);
                if (effectiveTrust >= Mathf.Max(0.55f, lenderTrustFloor01 + 0.1f))
                {
                    reasons.Add(FinancingReasonCode.StrongReputation);
                }

                if (safeWeeklyNetCashFlow > 0)
                {
                    reasons.Add(FinancingReasonCode.PositiveCashFlow);
                }

                if (safeAssetValue > 0)
                {
                    reasons.Add(FinancingReasonCode.StrongCollateral);
                }
            }

            string reviewContext = BuildReviewContext(
                sanitizedLender,
                reviewBand,
                approved,
                effectiveTrust,
                principalUtilization01,
                paymentUtilization01,
                lenderTrustFloor01,
                cautionDaysRemaining);

            return new BankLoanUnderwritingResult
            {
                decision = approved ? FinancingDecision.Approved : FinancingDecision.Declined,
                purpose = LoanPurpose.WorkingCapital,
                term = term,
                estimatedPaymentCents = estimatedPayment,
                estimatedWeeklyEquivalentPaymentCents = PaymentScheduleBuilder.ToWeeklyEquivalentCents(term, estimatedPayment),
                estimatedPrincipalCents = term.principalCents,
                estimatedInterestCents = Mathf.Max(0, estimateSchedule.totalInterestCents),
                estimatedOriginationFeeCents = Mathf.Max(0, term.originationFeeCents),
                estimatedTotalOwedCents = PaymentScheduleBuilder.CalculateTotalBorrowerCostCents(term, estimateSchedule),
                principalCapacityCents = principalCapacity,
                weeklyPaymentCapacityCents = weeklyPaymentCapacity,
                reasonCodes = Distinct(reasons),
                summary = cautionDaysRemaining > 0
                    ? "Working-capital file is temporarily restricted after a recent lender setback"
                    : BuildUnderwritingSummary(approved, reviewBand),
                declineReason = approved ? string.Empty : BuildDeclineReason(reasons),
                reviewBand = reviewBand,
                reviewDelayDays = reviewDelayDays,
                reviewDays = reviewDays,
                reviewContext = reviewContext
            };
        }

        internal static LoanTermStructure BuildTerm(int principalCents, LenderProfile lenderProfile, float trust01)
        {
            LenderProfile sanitized = lenderProfile.Sanitized();
            float conservatism01 = GetLenderConservatism01(sanitized);
            int trustAdjustmentBps = Mathf.RoundToInt(Mathf.Lerp(250f, -100f, Mathf.Clamp01(trust01)));
            int postureAdjustmentBps = Mathf.RoundToInt(Mathf.Lerp(-40f, 85f, conservatism01));
            int rateAdjustmentBps = trustAdjustmentBps + postureAdjustmentBps;
            return new LoanTermStructure
            {
                principalCents = Mathf.Max(0, principalCents),
                downPaymentCents = 0,
                annualInterestRateBps = Mathf.Max(0, sanitized.baseAnnualInterestRateBps + rateAdjustmentBps),
                termMonths = LoanTermMonths,
                amortizationMonths = LoanTermMonths,
                repaymentFrequency = RepaymentFrequency.Weekly,
                originationFeeCents = 0,
                balloonPaymentCents = 0
            };
        }

        private static List<FinancingReasonCode> Distinct(List<FinancingReasonCode> reasonCodes)
        {
            List<FinancingReasonCode> distinct = new();
            if (reasonCodes == null)
            {
                return distinct;
            }

            for (int i = 0; i < reasonCodes.Count; i++)
            {
                FinancingReasonCode code = reasonCodes[i];
                if (code != FinancingReasonCode.None && !distinct.Contains(code))
                {
                    distinct.Add(code);
                }
            }

            return distinct;
        }

        private static string BuildUnderwritingSummary(bool approved, BankLoanReviewBand reviewBand)
        {
            if (!approved)
            {
                return reviewBand == BankLoanReviewBand.Significant
                    ? "Application does not fit local bank policy after significant review."
                    : "Application does not fit local bank policy.";
            }

            return reviewBand switch
            {
                BankLoanReviewBand.Routine => "Application fits routine local bank policy.",
                BankLoanReviewBand.Watchlist => "Application is bankable but should move through watchlist review before funding.",
                _ => "Application is bankable but should move through significant review before funding."
            };
        }

        private static BankLoanReviewBand DetermineReviewBand(
            bool approved,
            float trust01,
            float principalUtilization01,
            float paymentUtilization01,
            int priorFailedFinancingCount,
            int priorDefaultCount,
            LenderProfile lenderProfile)
        {
            float conservatism01 = GetLenderConservatism01(lenderProfile);
            float routineTrustFloor01 = Mathf.Lerp(0.4f, 0.52f, conservatism01);
            float routinePrincipalCap01 = Mathf.Lerp(0.7f, 0.54f, conservatism01);
            float routinePaymentCap01 = Mathf.Lerp(0.64f, 0.5f, conservatism01);
            float significantTrustFloor01 = Mathf.Lerp(0.24f, 0.34f, conservatism01);
            float significantPrincipalFloor01 = Mathf.Lerp(0.9f, 0.78f, conservatism01);
            float significantPaymentFloor01 = Mathf.Lerp(0.86f, 0.74f, conservatism01);

            if (!approved)
            {
                if (principalUtilization01 > significantPrincipalFloor01
                    || paymentUtilization01 > significantPaymentFloor01
                    || trust01 < significantTrustFloor01
                    || priorDefaultCount > 0)
                {
                    return BankLoanReviewBand.Significant;
                }

                return BankLoanReviewBand.Watchlist;
            }

            bool routineSafe = trust01 >= routineTrustFloor01
                && priorFailedFinancingCount <= 0
                && priorDefaultCount <= 0
                && principalUtilization01 <= routinePrincipalCap01
                && paymentUtilization01 <= routinePaymentCap01;

            if (routineSafe)
            {
                return BankLoanReviewBand.Routine;
            }

            bool significant = trust01 < significantTrustFloor01
                || priorDefaultCount > 0
                || principalUtilization01 >= significantPrincipalFloor01
                || paymentUtilization01 >= significantPaymentFloor01;

            return significant
                ? BankLoanReviewBand.Significant
                : BankLoanReviewBand.Watchlist;
        }

        private static int GetReviewDelayDays(BankLoanReviewBand reviewBand, LenderProfile lenderProfile)
        {
            int baseDelay = reviewBand switch
            {
                BankLoanReviewBand.Routine => 0,
                BankLoanReviewBand.Watchlist => 1,
                _ => 2
            };

            float conservatism01 = GetLenderConservatism01(lenderProfile);
            int lenderAdjustment = conservatism01 >= 0.62f ? 1 : conservatism01 <= 0.25f ? -1 : 0;
            int delay = baseDelay + lenderAdjustment;
            int minimumDelay = reviewBand == BankLoanReviewBand.Routine ? 0 : 1;
            return Mathf.Max(minimumDelay, delay);
        }

        private static int CalculateWorkingCapitalPrincipalCapacityCents(int safeCashCents, int safeAssetValueCents, float trust01, LenderProfile lenderProfile)
        {
            // Working-capital files should treat owned assets as support, not as if the bank were taking the full portfolio at face value.
            float conservatism01 = GetLenderConservatism01(lenderProfile);
            float cashAdvanceMultiple = Mathf.Lerp(2.55f, 1.25f, conservatism01) * Mathf.Lerp(0.9f, 1.1f, Mathf.Clamp01(trust01));
            float assetAdvanceRatio = Mathf.Lerp(0.42f, 0.2f, conservatism01);
            assetAdvanceRatio *= Mathf.Lerp(0.9f, 1.05f, lenderProfile.maxLoanToValue01);
            assetAdvanceRatio *= 1f - lenderProfile.collateralHaircut01 * 0.55f;
            return Mathf.Max(
                MinimumPrincipalCapacityCents,
                Mathf.RoundToInt(safeCashCents * cashAdvanceMultiple + safeAssetValueCents * assetAdvanceRatio));
        }

        private static int CalculateWorkingCapitalWeeklyPaymentCapacityCents(int safeCashCents, int safeWeeklyNetCashFlowCents, int existingDebtPaymentCents, LenderProfile lenderProfile)
        {
            float conservatism01 = GetLenderConservatism01(lenderProfile);
            float cashSupportShare = Mathf.Lerp(0.1f, 0.06f, conservatism01);
            float cashFlowSupportShare = Mathf.Lerp(0.4f, 0.28f, conservatism01);
            int grossSupportCents = Mathf.Max(
                Mathf.RoundToInt(safeCashCents * cashSupportShare),
                Mathf.RoundToInt(safeWeeklyNetCashFlowCents * cashFlowSupportShare));
            int netSupportCents = Mathf.Max(0, grossSupportCents - Mathf.Max(0, existingDebtPaymentCents));
            float coverageRatio = Mathf.Max(1.05f, lenderProfile.minimumCashFlowCoverageRatio);
            return Mathf.Max(0, Mathf.FloorToInt(netSupportCents / coverageRatio));
        }

        private static int GetRemainingCooldownDays(int currentDayIndex, int lenderCooldownUntilDayIndex)
        {
            if (lenderCooldownUntilDayIndex < 0)
            {
                return 0;
            }

            return Mathf.Max(0, lenderCooldownUntilDayIndex - Mathf.Max(0, currentDayIndex));
        }

        private static float GetWorkingCapitalTrustFloor01(LenderProfile lenderProfile)
        {
            float conservatism01 = GetLenderConservatism01(lenderProfile);
            return Mathf.Clamp01(Mathf.Max(0.18f, lenderProfile.minimumReputation01 * Mathf.Lerp(0.55f, 0.9f, conservatism01)));
        }

        private static float GetLenderConservatism01(LenderProfile lenderProfile)
        {
            // This keeps lender personality inside existing profile data instead of introducing a second underwriting authority.
            LenderProfile sanitized = lenderProfile.Sanitized();
            float riskReserve01 = 1f - sanitized.riskTolerance01;
            float coverageTightness01 = Mathf.InverseLerp(1.05f, 1.3f, sanitized.minimumCashFlowCoverageRatio);
            float haircutTightness01 = Mathf.InverseLerp(0.08f, 0.24f, sanitized.collateralHaircut01);
            float leverageTightness01 = 1f - Mathf.InverseLerp(0.65f, 0.85f, sanitized.maxLoanToValue01);
            return Mathf.Clamp01(riskReserve01 * 0.45f + coverageTightness01 * 0.25f + haircutTightness01 * 0.15f + leverageTightness01 * 0.15f);
        }

        private static string BuildReviewContext(
            LenderProfile lenderProfile,
            BankLoanReviewBand reviewBand,
            bool approved,
            float trust01,
            float principalUtilization01,
            float paymentUtilization01,
            float lenderTrustFloor01,
            int cautionDaysRemaining)
        {
            string lenderName = string.IsNullOrWhiteSpace(lenderProfile.displayName) ? "The bank" : lenderProfile.displayName;
            string posture = DescribeLenderWorkingCapitalPosture(lenderProfile);

            if (cautionDaysRemaining > 0)
            {
                return $"{lenderName} is still cooling off for about {Mathf.Max(1, cautionDaysRemaining)} days after a recent setback, so it is not taking a fresh working-capital file yet.";
            }

            if (!approved)
            {
                if (trust01 < lenderTrustFloor01)
                {
                    return $"{lenderName} is lending from a {posture} posture and lender confidence is still below its comfort floor.";
                }

                if (principalUtilization01 > 0.95f || paymentUtilization01 > 0.95f)
                {
                    return $"{lenderName} is lending from a {posture} posture and this file runs past its ordinary working-capital capacity.";
                }

                return $"{lenderName} is lending from a {posture} posture and this file sits outside current local bank comfort.";
            }

            return reviewBand switch
            {
                BankLoanReviewBand.Routine => $"{lenderName} is lending from a {posture} posture and this file sits comfortably inside routine policy.",
                BankLoanReviewBand.Watchlist => $"{lenderName} is lending from a {posture} posture, so this file stays in watchlist review until the bank is satisfied with its headroom.",
                _ => $"{lenderName} is lending from a {posture} posture and wants senior scrutiny before funding this file."
            };
        }

        private static string DescribeLenderWorkingCapitalPosture(LenderProfile lenderProfile)
        {
            float conservatism01 = GetLenderConservatism01(lenderProfile);
            if (conservatism01 >= 0.62f)
            {
                return "conservative local bank";
            }

            if (conservatism01 <= 0.25f)
            {
                return "growth-minded local bank";
            }

            return "balanced local bank";
        }

        private static string BuildDeclineReason(List<FinancingReasonCode> reasonCodes)
        {
            if (reasonCodes == null || reasonCodes.Count == 0)
            {
                return "requested amount too high";
            }

            if (reasonCodes.Contains(FinancingReasonCode.RecentLenderSetback))
            {
                return "bank asks for time before a new file";
            }

            if (reasonCodes.Contains(FinancingReasonCode.ExceedsLoanToValue))
            {
                return "requested amount too high";
            }

            if (reasonCodes.Contains(FinancingReasonCode.LowLenderTrust))
            {
                return "lender confidence too low";
            }

            if (reasonCodes.Contains(FinancingReasonCode.FailedClosingHistory))
            {
                return "recent failed financing history";
            }

            if (reasonCodes.Contains(FinancingReasonCode.HeavyDebtBurden) || reasonCodes.Contains(FinancingReasonCode.WeakCashFlow))
            {
                return "existing debt burden too high";
            }

            return "outside lender policy";
        }
    }
}
