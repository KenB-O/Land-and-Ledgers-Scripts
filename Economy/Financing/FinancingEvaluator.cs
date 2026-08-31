using System;
using System.Collections.Generic;
using System.Reflection;
using LandLedgers.Time;
using UnityEngine;

namespace LandLedgers.Economy.Financing
{
    public sealed class FinancingEvaluator
    {
        private const int DefaultTermMonths = 60;
        private const int DefaultOriginationFeeBps = 150;
        private const float MinimumApprovalLenderTrust01 = 0.22f;
        private const float MinimumConditionalLenderTrust01 = 0.18f;
        private const float MinimumApprovalOperationalReliability01 = 0.24f;
        private const float MinimumConditionalOperationalReliability01 = 0.2f;
        private const float ConditionalReputationGrace01 = 0.12f;
        private const int ConditionalDownPaymentGraceCents = 5000;
        private const float ConditionalDownPaymentGraceRatio = 0.03f;
        private const int ConditionalCashFlowGraceCents = 3000;
        private const float ConditionalCashFlowGraceRatio = 0.2f;
        // Acquisition-side files can survive a very short bank cooling-off tail as conditional only; longer setbacks should not read as immediately bankable.
        private const int ConditionalLenderSetbackGraceDays = 3;

        private readonly struct PurposeUnderwritingPolicy
        {
            public PurposeUnderwritingPolicy(
                int rateAdjustmentBps,
                float downPaymentMultiplier,
                float collateralSupportMultiplier,
                float cashFlowCoverageMultiplier,
                float originationFeeMultiplier,
                bool materiallyStricter,
                string conditionText)
            {
                RateAdjustmentBps = rateAdjustmentBps;
                DownPaymentMultiplier = downPaymentMultiplier;
                CollateralSupportMultiplier = collateralSupportMultiplier;
                CashFlowCoverageMultiplier = cashFlowCoverageMultiplier;
                OriginationFeeMultiplier = originationFeeMultiplier;
                MateriallyStricter = materiallyStricter;
                ConditionText = conditionText ?? string.Empty;
            }

            public int RateAdjustmentBps { get; }
            public float DownPaymentMultiplier { get; }
            public float CollateralSupportMultiplier { get; }
            public float CashFlowCoverageMultiplier { get; }
            public float OriginationFeeMultiplier { get; }
            public bool MateriallyStricter { get; }
            public string ConditionText { get; }
        }

        public FinancingEstimate EstimateFinancing(FinancingEstimateRequest request)
        {
            LenderProfile lender = GetLender(request.lender);
            LoanPurpose purpose = LoanPurposeRules.Sanitize(request.purpose, LoanPurpose.Acquisition);
            PurposeUnderwritingPolicy purposePolicy = GetPurposePolicy(purpose);
            FinancingApplicantProfile applicant = request.applicant.Sanitized();
            List<FinancingReasonCode> reasons = new List<FinancingReasonCode>();

            int purchasePriceCents = Mathf.Max(1, request.expectedAcquisitionPriceCents);
            int collateralValueCents = Mathf.Max(0, request.estimatedCollateralValueCents);
            int desiredDownPaymentCents = Mathf.Clamp(request.desiredDownPaymentCents, 0, purchasePriceCents);
            int requiredDownPaymentCents = CalculateRequiredDownPaymentCents(purchasePriceCents, lender, purposePolicy);
            int collateralCapacityCents = CalculateCollateralCapacityCents(collateralValueCents, lender, LoanCollateralKind.ImprovedProperty, purposePolicy);
            int requestedPrincipalCents = Mathf.Max(0, purchasePriceCents - desiredDownPaymentCents);
            int maxPrincipalCents = Mathf.Min(Mathf.Max(0, purchasePriceCents - requiredDownPaymentCents), collateralCapacityCents);
            int rateBps = CalculateRateBps(lender, applicant, collateralValueCents, purchasePriceCents, purposePolicy);

            LoanTermStructure estimatedTerm = BuildRequestedTerm(request.requestedTerm, Mathf.Min(requestedPrincipalCents, maxPrincipalCents), lender);
            estimatedTerm.downPaymentCents = Mathf.Max(desiredDownPaymentCents, requiredDownPaymentCents);
            estimatedTerm.annualInterestRateBps = rateBps;
            estimatedTerm.originationFeeCents = CalculateOriginationFeeCents(estimatedTerm.principalCents, purposePolicy);
            estimatedTerm = estimatedTerm.Sanitized();
            int estimatedPaymentCents = PaymentScheduleBuilder.EstimatePaymentCents(estimatedTerm);

            AddProfileReasons(reasons, applicant, lender);
            AddDownPaymentReasons(reasons, desiredDownPaymentCents, requiredDownPaymentCents);
            AddCollateralReasons(reasons, collateralValueCents, purchasePriceCents);
            AddCashFlowReasons(reasons, applicant, estimatedTerm, estimatedPaymentCents, lender, purposePolicy);

            if (lender.AllowsPurpose(purpose)
                && estimatedTerm.principalCents > 0
                && requestedPrincipalCents <= maxPrincipalCents)
            {
                reasons.Add(FinancingReasonCode.WithinLenderPolicy);
            }
            else
            {
                reasons.Add(FinancingReasonCode.OutsideLenderPolicy);
            }

            return new FinancingEstimate(
                request.requestId,
                lender.lenderId,
                FinancingDecision.EstimateOnly,
                maxPrincipalCents,
                requiredDownPaymentCents,
                rateBps,
                estimatedPaymentCents,
                estimatedTerm,
                Distinct(reasons));
        }

        public FinancingApprovalResult EvaluateApproval(FinancingApprovalRequest request)
        {
            LenderProfile lender = GetLender(request.lender);
            LoanPurpose purpose = LoanPurposeRules.Sanitize(request.purpose, LoanPurpose.Acquisition);
            PurposeUnderwritingPolicy purposePolicy = GetPurposePolicy(purpose);
            FinancingApplicantProfile applicant = request.applicant.Sanitized();
            CollateralProfile collateral = request.collateral.Sanitized();
            List<FinancingReasonCode> reasons = new List<FinancingReasonCode>();
            List<string> conditions = new List<string>();

            int purchasePriceCents = Mathf.Max(1, request.exactPurchasePriceCents);
            int downPaymentCents = Mathf.Clamp(request.exactDownPaymentCents, 0, purchasePriceCents);
            int principalCents = Mathf.Max(0, purchasePriceCents - downPaymentCents);
            int requiredDownPaymentCents = CalculateRequiredDownPaymentCents(purchasePriceCents, lender, purposePolicy);
            int collateralCapacityCents = CalculateCollateralCapacityCents(collateral.UnencumberedValueCents, lender, collateral.kind, purposePolicy);
            int rateBps = CalculateRateBps(lender, applicant, collateral.UnencumberedValueCents, purchasePriceCents, purposePolicy);
            LoanTermStructure term = BuildRequestedTerm(request.requestedTerm, principalCents, lender);
            term.downPaymentCents = downPaymentCents;
            term.annualInterestRateBps = rateBps;
            term.originationFeeCents = CalculateOriginationFeeCents(principalCents, purposePolicy);
            term = term.Sanitized();
            int estimatedPaymentCents = PaymentScheduleBuilder.EstimatePaymentCents(term);

            int additionalDownPaymentNeededCents = Mathf.Max(0, requiredDownPaymentCents - downPaymentCents);
            int principalSupportShortfallCents = Mathf.Max(0, principalCents - collateralCapacityCents);
            int weeklyCashFlowShortfallCents = CalculateWeeklyCashFlowShortfallCents(applicant, term, estimatedPaymentCents, lender, purposePolicy);
            float reputationShortfall01 = Mathf.Max(0f, lender.minimumReputation01 - applicant.reputation01);
            float lenderTrustShortfall01 = Mathf.Max(0f, MinimumApprovalLenderTrust01 - applicant.lenderTrust01);
            float operatingReliabilityShortfall01 = Mathf.Max(0f, MinimumApprovalOperationalReliability01 - applicant.operationalReliability01);
            bool hasRecentLenderSetback = applicant.lenderReapplyCooldownDays > 0;
            bool setbackWithinConditionalGrace = applicant.lenderReapplyCooldownDays > 0 && applicant.lenderReapplyCooldownDays <= ConditionalLenderSetbackGraceDays;

            AddProfileReasons(reasons, applicant, lender);
            AddDownPaymentReasons(reasons, downPaymentCents, requiredDownPaymentCents);
            AddCollateralReasons(reasons, collateral.UnencumberedValueCents, purchasePriceCents);
            AddCashFlowReasons(reasons, applicant, term, estimatedPaymentCents, lender, purposePolicy);

            bool allowsPurpose = lender.AllowsPurpose(purpose);
            bool hasCollateral = collateral.kind != LoanCollateralKind.Unsecured && collateral.UnencumberedValueCents > 0;
            bool downPaymentMeetsPolicy = additionalDownPaymentNeededCents <= 0;
            bool principalWithinPolicy = principalCents > 0 && principalSupportShortfallCents <= 0;
            bool profileMeetsPolicy = reputationShortfall01 <= 0f
                && lenderTrustShortfall01 <= 0f
                && operatingReliabilityShortfall01 <= 0f
                && applicant.priorFailedFinancingCount <= 2
                && !hasRecentLenderSetback;
            bool cashFlowMeetsPolicy = weeklyCashFlowShortfallCents <= 0;
            bool conditionalDownPaymentWithinRange = IsWithinConditionalDownPaymentRange(purchasePriceCents, additionalDownPaymentNeededCents);
            bool conditionalCashFlowWithinRange = IsWithinConditionalCashFlowRange(term, estimatedPaymentCents, weeklyCashFlowShortfallCents, purposePolicy);

            if (!allowsPurpose)
            {
                reasons.Add(FinancingReasonCode.OutsideLenderPolicy);
                conditions.Add("Lender does not finance this loan purpose.");
            }

            if (!hasCollateral)
            {
                reasons.Add(FinancingReasonCode.MissingCollateral);
                conditions.Add("Verified collateral is required before approval.");
            }

            if (!principalWithinPolicy)
            {
                reasons.Add(FinancingReasonCode.ExceedsLoanToValue);
                AddPrincipalCondition(conditions, principalSupportShortfallCents);
            }

            if (!downPaymentMeetsPolicy)
            {
                AddDownPaymentCondition(conditions, additionalDownPaymentNeededCents);

                if (!conditionalDownPaymentWithinRange)
                {
                    conditions.Add("Current down payment gap is too large for a conditional hold.");
                }
            }

            if (!profileMeetsPolicy)
            {
                AddProfileConditions(conditions, reputationShortfall01, lenderTrustShortfall01, applicant.priorFailedFinancingCount, applicant.lenderReapplyCooldownDays);
                AddOperationalReliabilityCondition(conditions, operatingReliabilityShortfall01);
            }

            if (!cashFlowMeetsPolicy)
            {
                AddCashFlowCondition(conditions, weeklyCashFlowShortfallCents);

                if (!conditionalCashFlowWithinRange)
                {
                    conditions.Add("Current weekly cash-flow gap is too large for a conditional hold.");
                }
            }

            if (purposePolicy.MateriallyStricter && (!downPaymentMeetsPolicy || !principalWithinPolicy || !cashFlowMeetsPolicy))
            {
                conditions.Add(purposePolicy.ConditionText);
            }

            FinancingDecision decision;
            if (allowsPurpose && hasCollateral && downPaymentMeetsPolicy && principalWithinPolicy && profileMeetsPolicy && cashFlowMeetsPolicy)
            {
                decision = FinancingDecision.Approved;
                reasons.Add(FinancingReasonCode.WithinLenderPolicy);
            }
            else if (allowsPurpose
                && hasCollateral
                && principalWithinPolicy
                && conditionalDownPaymentWithinRange
                && conditionalCashFlowWithinRange
                && applicant.priorFailedFinancingCount <= 2
                && (!hasRecentLenderSetback || setbackWithinConditionalGrace)
                && applicant.reputation01 >= Mathf.Max(0f, lender.minimumReputation01 - ConditionalReputationGrace01)
                && applicant.lenderTrust01 >= MinimumConditionalLenderTrust01
                && applicant.operationalReliability01 >= MinimumConditionalOperationalReliability01)
            {
                decision = FinancingDecision.ConditionallyApproved;
            }
            else
            {
                decision = FinancingDecision.Declined;
            }

            LoanOffer offer = new LoanOffer
            {
                offerId = BuildOfferId(request.agreementId, lender.lenderId),
                lenderId = lender.lenderId,
                purpose = purpose,
                termStructure = term,
                maxPrincipalCents = collateralCapacityCents,
                requiredDownPaymentCents = requiredDownPaymentCents,
                decision = decision,
                reasonCodes = new List<FinancingReasonCode>(Distinct(reasons)),
                expiresOnDate = BuildDateFromAbsoluteDay(GetAbsoluteDayIndex(request.approvalDate) + 14, GetDaysPerMonth(request.daysPerMonth))
            };

            return new FinancingApprovalResult(
                request.agreementId,
                decision,
                offer,
                DistinctStrings(conditions),
                offer.reasonCodes,
                additionalDownPaymentNeededCents,
                principalSupportShortfallCents,
                weeklyCashFlowShortfallCents,
                reputationShortfall01,
                lenderTrustShortfall01);
        }

        private static PurposeUnderwritingPolicy GetPurposePolicy(LoanPurpose purpose)
        {
            switch (LoanPurposeRules.Sanitize(purpose, LoanPurpose.Acquisition))
            {
                case LoanPurpose.PropertyPurchase:
                    return new PurposeUnderwritingPolicy(-35, 0.95f, 1.08f, 0.95f, 0.95f, false, string.Empty);
                case LoanPurpose.BusinessAcquisition:
                    return new PurposeUnderwritingPolicy(70, 1.15f, 0.9f, 1.12f, 1.15f, true, "Business acquisition files need stronger books, collateral, or cash-flow proof before the lender treats the deal as closeable.");
                case LoanPurpose.ProjectConstruction:
                case LoanPurpose.Construction:
                    return new PurposeUnderwritingPolicy(55, 1.12f, 0.92f, 1.1f, 1.08f, true, "Project and construction lending needs extra cushion because cost and completion risk are still open.");
                case LoanPurpose.Infrastructure:
                    return new PurposeUnderwritingPolicy(85, 1.18f, 0.88f, 1.16f, 1.18f, true, "Infrastructure lending needs extra collateral and repayment confidence before the bank will hold the file open.");
                case LoanPurpose.RescueLiquidity:
                case LoanPurpose.Emergency:
                    return new PurposeUnderwritingPolicy(105, 1.1f, 0.84f, 1.2f, 1.22f, true, "Rescue liquidity reads as distress lending, so the bank needs a clearer repayment source.");
                case LoanPurpose.Speculation:
                    return new PurposeUnderwritingPolicy(160, 1.35f, 0.72f, 1.32f, 1.35f, true, "Speculative borrowing is outside normal frontier-bank comfort unless cash and collateral are unusually strong.");
                case LoanPurpose.MineDevelopment:
                    return new PurposeUnderwritingPolicy(145, 1.32f, 0.74f, 1.3f, 1.35f, true, "Mine development is capital-heavy and uncertain, so the lender demands stronger collateral and more borrower cushion.");
                case LoanPurpose.RailDevelopment:
                    return new PurposeUnderwritingPolicy(130, 1.3f, 0.78f, 1.28f, 1.32f, true, "Rail development needs institutional-scale repayment proof before the lender treats it like an ordinary note.");
                case LoanPurpose.Refinance:
                    return new PurposeUnderwritingPolicy(25, 1f, 1f, 1.03f, 1f, false, string.Empty);
                case LoanPurpose.WorkingCapital:
                default:
                    return new PurposeUnderwritingPolicy(0, 1f, 1f, 1f, 1f, false, string.Empty);
            }
        }

        private static LenderProfile GetLender(LenderProfile requested)
        {
            return string.IsNullOrWhiteSpace(requested.lenderId)
                ? LenderProfile.DefaultLocalBank.Sanitized()
                : requested.Sanitized();
        }

        private static LoanTermStructure BuildRequestedTerm(LoanTermStructure requested, int principalCents, LenderProfile lender)
        {
            LoanTermStructure term = requested;
            term.principalCents = principalCents;
            term.termMonths = term.termMonths <= 0 ? DefaultTermMonths : term.termMonths;
            term.amortizationMonths = term.amortizationMonths <= 0 ? term.termMonths : term.amortizationMonths;
            term.annualInterestRateBps = term.annualInterestRateBps <= 0 ? lender.baseAnnualInterestRateBps : term.annualInterestRateBps;
            return term.Sanitized();
        }

        private static int CalculateRequiredDownPaymentCents(int purchasePriceCents, LenderProfile lender, PurposeUnderwritingPolicy purposePolicy)
        {
            float requiredRatio = Mathf.Clamp01(lender.minimumDownPaymentRatio01 * Mathf.Max(0.1f, purposePolicy.DownPaymentMultiplier));
            return Mathf.CeilToInt(Mathf.Max(0, purchasePriceCents) * requiredRatio);
        }

        private static int CalculateCollateralCapacityCents(int collateralValueCents, LenderProfile lender, LoanCollateralKind kind, PurposeUnderwritingPolicy purposePolicy)
        {
            if (kind == LoanCollateralKind.Unsecured || collateralValueCents <= 0)
            {
                return 0;
            }

            float kindModifier = kind switch
            {
                LoanCollateralKind.Land => 0.92f,
                LoanCollateralKind.ImprovedProperty => 1f,
                LoanCollateralKind.OperatingBusiness => 0.86f,
                LoanCollateralKind.Inventory => 0.58f,
                _ => 0f
            };
            float collateralBasis = Mathf.Clamp01(1f - lender.collateralHaircut01) * kindModifier * Mathf.Max(0f, purposePolicy.CollateralSupportMultiplier);
            return Mathf.FloorToInt(collateralValueCents * lender.maxLoanToValue01 * collateralBasis);
        }

        private static int CalculateRateBps(LenderProfile lender, FinancingApplicantProfile applicant, int collateralValueCents, int purchasePriceCents, PurposeUnderwritingPolicy purposePolicy)
        {
            int riskAdjustmentBps = 0;
            riskAdjustmentBps += Mathf.RoundToInt((1f - applicant.reputation01) * 180f);
            riskAdjustmentBps += Mathf.RoundToInt((1f - applicant.lenderTrust01) * 130f);
            riskAdjustmentBps += Mathf.RoundToInt((1f - applicant.operationalReliability01) * 115f);
            riskAdjustmentBps += applicant.priorFailedFinancingCount * 45;
            riskAdjustmentBps += Mathf.RoundToInt(applicant.lenderCaution01 * 80f);
            riskAdjustmentBps += collateralValueCents < purchasePriceCents ? 75 : 0;
            riskAdjustmentBps -= Mathf.RoundToInt(Mathf.Clamp01(applicant.ownedAssetValueCents / (float)Mathf.Max(1, purchasePriceCents)) * 70f);
            riskAdjustmentBps -= Mathf.RoundToInt(lender.riskTolerance01 * 60f);
            return Mathf.Max(0, lender.baseAnnualInterestRateBps + riskAdjustmentBps + purposePolicy.RateAdjustmentBps);
        }

        private static int CalculateOriginationFeeCents(int principalCents, PurposeUnderwritingPolicy purposePolicy)
        {
            return Mathf.RoundToInt(Mathf.Max(0, principalCents) * DefaultOriginationFeeBps * Mathf.Max(0f, purposePolicy.OriginationFeeMultiplier) / 10000f);
        }

        private static int CalculateWeeklyCashFlowShortfallCents(
            FinancingApplicantProfile applicant,
            LoanTermStructure term,
            int estimatedPaymentCents,
            LenderProfile lender,
            PurposeUnderwritingPolicy purposePolicy)
        {
            if (estimatedPaymentCents <= 0)
            {
                return int.MaxValue;
            }

            int debtAdjustedCashFlow = Mathf.Max(0, applicant.weeklyNetCashFlowCents - applicant.existingDebtPaymentCents);
            int weeklyEquivalentPaymentCents = PaymentScheduleBuilder.ToWeeklyEquivalentCents(term, estimatedPaymentCents);
            float coverageRatio = Mathf.Max(0f, lender.minimumCashFlowCoverageRatio * Mathf.Max(0.1f, purposePolicy.CashFlowCoverageMultiplier));
            int requiredWeeklyCoverage = Mathf.CeilToInt(weeklyEquivalentPaymentCents * coverageRatio);
            return Mathf.Max(0, requiredWeeklyCoverage - debtAdjustedCashFlow);
        }

        private static bool MeetsCashFlowPolicy(
            FinancingApplicantProfile applicant,
            LoanTermStructure term,
            int estimatedPaymentCents,
            LenderProfile lender,
            PurposeUnderwritingPolicy purposePolicy)
        {
            return CalculateWeeklyCashFlowShortfallCents(applicant, term, estimatedPaymentCents, lender, purposePolicy) <= 0;
        }

        private static bool IsWithinConditionalDownPaymentRange(int purchasePriceCents, int additionalDownPaymentNeededCents)
        {
            if (additionalDownPaymentNeededCents <= 0)
            {
                return true;
            }

            int conditionalDownPaymentGraceCents = Mathf.Max(
                ConditionalDownPaymentGraceCents,
                Mathf.RoundToInt(Mathf.Max(0, purchasePriceCents) * ConditionalDownPaymentGraceRatio));
            return additionalDownPaymentNeededCents <= conditionalDownPaymentGraceCents;
        }

        private static bool IsWithinConditionalCashFlowRange(LoanTermStructure term, int estimatedPaymentCents, int weeklyCashFlowShortfallCents, PurposeUnderwritingPolicy purposePolicy)
        {
            if (weeklyCashFlowShortfallCents <= 0)
            {
                return true;
            }

            int weeklyEquivalentPaymentCents = PaymentScheduleBuilder.ToWeeklyEquivalentCents(term, estimatedPaymentCents);
            float graceRatio = purposePolicy.MateriallyStricter ? ConditionalCashFlowGraceRatio * 0.65f : ConditionalCashFlowGraceRatio;
            int conditionalCashFlowGraceCents = Mathf.Max(
                ConditionalCashFlowGraceCents,
                Mathf.RoundToInt(Mathf.Max(0, weeklyEquivalentPaymentCents) * graceRatio));
            return weeklyCashFlowShortfallCents <= conditionalCashFlowGraceCents;
        }

        private static void AddProfileReasons(List<FinancingReasonCode> reasons, FinancingApplicantProfile applicant, LenderProfile lender)
        {
            if (applicant.reputation01 >= lender.minimumReputation01)
            {
                reasons.Add(FinancingReasonCode.StrongReputation);
            }
            else
            {
                reasons.Add(FinancingReasonCode.WeakReputation);
            }

            if (applicant.lenderTrust01 < MinimumApprovalLenderTrust01)
            {
                reasons.Add(FinancingReasonCode.LowLenderTrust);
            }

            // Keep operating record distinct from general reputation so acquisition-side diagnostics can tell the player
            // whether the bank is reacting to borrower standing or to execution reliability.
            if (applicant.operationalReliability01 >= 0.62f)
            {
                reasons.Add(FinancingReasonCode.StrongOperationalRecord);
            }
            else if (applicant.operationalReliability01 < MinimumApprovalOperationalReliability01)
            {
                reasons.Add(FinancingReasonCode.WeakOperationalRecord);
            }

            if (applicant.priorFailedFinancingCount > 0)
            {
                reasons.Add(FinancingReasonCode.FailedClosingHistory);
            }

            if (applicant.lenderReapplyCooldownDays > 0 || applicant.lenderCaution01 > 0f)
            {
                reasons.Add(FinancingReasonCode.RecentLenderSetback);
            }
        }

        private static void AddDownPaymentReasons(List<FinancingReasonCode> reasons, int downPaymentCents, int requiredDownPaymentCents)
        {
            if (downPaymentCents >= requiredDownPaymentCents)
            {
                reasons.Add(FinancingReasonCode.SufficientDownPayment);
            }
            else
            {
                reasons.Add(FinancingReasonCode.ThinDownPayment);
            }
        }

        private static void AddCollateralReasons(List<FinancingReasonCode> reasons, int collateralValueCents, int purchasePriceCents)
        {
            if (collateralValueCents >= purchasePriceCents)
            {
                reasons.Add(FinancingReasonCode.StrongCollateral);
            }
            else if (collateralValueCents > 0)
            {
                reasons.Add(FinancingReasonCode.WeakCollateral);
            }
            else
            {
                reasons.Add(FinancingReasonCode.MissingCollateral);
            }
        }

        private static void AddCashFlowReasons(
            List<FinancingReasonCode> reasons,
            FinancingApplicantProfile applicant,
            LoanTermStructure term,
            int estimatedPaymentCents,
            LenderProfile lender,
            PurposeUnderwritingPolicy purposePolicy)
        {
            if (MeetsCashFlowPolicy(applicant, term, estimatedPaymentCents, lender, purposePolicy))
            {
                reasons.Add(FinancingReasonCode.PositiveCashFlow);
            }
            else
            {
                reasons.Add(applicant.existingDebtPaymentCents > 0 ? FinancingReasonCode.HeavyDebtBurden : FinancingReasonCode.WeakCashFlow);
            }
        }

        private static void AddDownPaymentCondition(List<string> conditions, int additionalDownPaymentNeededCents)
        {
            if (additionalDownPaymentNeededCents <= 0)
            {
                return;
            }

            conditions.Add($"Add about {FormatMoney(additionalDownPaymentNeededCents)} more down payment.");
        }

        private static void AddPrincipalCondition(List<string> conditions, int principalSupportShortfallCents)
        {
            if (principalSupportShortfallCents <= 0)
            {
                return;
            }

            conditions.Add($"Reduce principal or add about {FormatMoney(principalSupportShortfallCents)} more acceptable collateral support.");
        }

        private static void AddCashFlowCondition(List<string> conditions, int weeklyCashFlowShortfallCents)
        {
            if (weeklyCashFlowShortfallCents <= 0)
            {
                return;
            }

            conditions.Add($"Show about {FormatMoney(weeklyCashFlowShortfallCents)} more weekly free cash flow or reduce existing debt service.");
        }

        private static void AddProfileConditions(
            List<string> conditions,
            float reputationShortfall01,
            float lenderTrustShortfall01,
            int priorFailedFinancingCount,
            int lenderReapplyCooldownDays)
        {
            if (reputationShortfall01 > 0f)
            {
                conditions.Add($"Improve borrower standing by about {Mathf.CeilToInt(reputationShortfall01 * 100f)} points.");
            }

            if (lenderTrustShortfall01 > 0f)
            {
                conditions.Add($"Improve lender standing by about {Mathf.CeilToInt(lenderTrustShortfall01 * 100f)} points.");
            }

            if (priorFailedFinancingCount > 2)
            {
                conditions.Add("Recent failed financing history blocks approval right now.");
            }

            if (lenderReapplyCooldownDays > 0)
            {
                conditions.Add($"The bank is still cooling off after a recent setback. Reapply in about {lenderReapplyCooldownDays} days.");
            }
        }

        private static void AddOperationalReliabilityCondition(List<string> conditions, float operatingReliabilityShortfall01)
        {
            if (operatingReliabilityShortfall01 > 0f)
            {
                conditions.Add($"Strengthen operating record by about {Mathf.CeilToInt(operatingReliabilityShortfall01 * 100f)} points.");
            }
        }

        private static List<FinancingReasonCode> Distinct(List<FinancingReasonCode> reasons)
        {
            List<FinancingReasonCode> distinct = new List<FinancingReasonCode>();
            if (reasons == null)
            {
                return distinct;
            }

            for (int i = 0; i < reasons.Count; i++)
            {
                FinancingReasonCode reason = reasons[i];
                if (reason != FinancingReasonCode.None && !distinct.Contains(reason))
                {
                    distinct.Add(reason);
                }
            }

            return distinct;
        }

        private static IReadOnlyList<string> DistinctStrings(List<string> input)
        {
            List<string> distinct = new List<string>();
            if (input == null)
            {
                return distinct;
            }

            for (int i = 0; i < input.Count; i++)
            {
                string value = input[i];
                if (!string.IsNullOrWhiteSpace(value) && !distinct.Contains(value))
                {
                    distinct.Add(value);
                }
            }

            return distinct;
        }

        private static string BuildOfferId(string agreementId, string lenderId)
        {
            return $"{lenderId}_{agreementId}";
        }

        private static int GetAbsoluteDayIndex(SimulationDate date)
        {
            Type type = date.GetType();
            return GetIntMemberValue(type.GetProperty("AbsoluteDayIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic), date)
                ?? GetIntMemberValue(type.GetProperty("absoluteDayIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic), date)
                ?? GetIntMemberValue(type.GetProperty("AbsoluteDay", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic), date)
                ?? GetIntMemberValue(type.GetField("absoluteDayIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic), date)
                ?? GetIntMemberValue(type.GetField("absoluteDay", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic), date)
                ?? 0;
        }

        private static int? GetIntMemberValue(MemberInfo member, object target)
        {
            if (member is PropertyInfo property && property.CanRead)
            {
                object value = property.GetValue(target, null);
                return value is int intValue ? intValue : null;
            }

            if (member is FieldInfo field)
            {
                object value = field.GetValue(target);
                return value is int intValue ? intValue : null;
            }

            return null;
        }

        private static int GetDaysPerMonth(int requestedDaysPerMonth)
        {
            return requestedDaysPerMonth > 0 ? requestedDaysPerMonth : 28;
        }

        private static SimulationDate BuildDateFromAbsoluteDay(int absoluteDayIndex, int daysPerMonth)
        {
            int safeDaysPerMonth = GetDaysPerMonth(daysPerMonth);
            int clampedDay = Mathf.Max(0, absoluteDayIndex);
            int dayOfWeekIndex = clampedDay % 7;
            int monthIndex = clampedDay / safeDaysPerMonth;
            int dayOfMonth = clampedDay % safeDaysPerMonth + 1;
            int week = clampedDay / 7 + 1;
            int weekOfMonth = (dayOfMonth - 1) / 7 + 1;
            int month = monthIndex % 12 + 1;
            int year = monthIndex / 12 + 1;
            return new SimulationDate(clampedDay, dayOfWeekIndex, dayOfMonth, week, weekOfMonth, month, year);
        }

        private static string FormatMoney(int cents)
        {
            return $"${Mathf.Max(0, cents) / 100f:0.00}";
        }
    }
}
