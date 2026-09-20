using System;
using System.Linq;
using LandLedgers.Economy.Financing;
using LandLedgers.Time;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Financing
{
    public sealed class FinancingFoundationTests
    {
        [Test]
        public void EstimateReturnsNonBindingTermsAndGroundedReasonCodes()
        {
            FinancingEstimate estimate = new FinancingEvaluator().EstimateFinancing(new FinancingEstimateRequest
            {
                requestId = "estimate_001",
                purpose = LoanPurpose.Acquisition,
                expectedAcquisitionPriceCents = 500000,
                estimatedCollateralValueCents = 540000,
                desiredDownPaymentCents = 125000,
                applicant = CreateStrongApplicant(),
                requestedTerm = CreateRequestedTerm(),
                estimateDate = CreateStartDate()
            });

            Assert.AreEqual(FinancingDecision.EstimateOnly, estimate.Decision);
            Assert.AreEqual("frontier_local_bank", estimate.LikelyLenderId);
            Assert.Greater(estimate.MaxPrincipalCents, 0);
            Assert.Greater(estimate.MinimumDownPaymentCents, 0);
            Assert.Greater(estimate.EstimatedPaymentCents, 0);
            CollectionAssert.Contains(estimate.ReasonCodes, FinancingReasonCode.SufficientDownPayment);
            CollectionAssert.Contains(estimate.ReasonCodes, FinancingReasonCode.PositiveCashFlow);
        }

        [Test]
        public void ApprovalReturnsApprovedConditionalAndDeclinedFromChangedRiskInputs()
        {
            FinancingEvaluator evaluator = new FinancingEvaluator();

            FinancingApprovalResult approved = evaluator.EvaluateApproval(CreateApprovalRequest(
                "approved",
                CreateStrongApplicant(),
                CreateStrongCollateral(),
                130000));
            FinancingApprovalResult conditional = evaluator.EvaluateApproval(CreateApprovalRequest(
                "conditional",
                CreateStrongApplicant(),
                CreateStrongCollateral(),
                90000));
            FinancingApprovalResult declined = evaluator.EvaluateApproval(CreateApprovalRequest(
                "declined",
                CreateWeakApplicant(),
                CreateWeakCollateral(),
                20000));

            Assert.AreEqual(FinancingDecision.Approved, approved.Decision);
            Assert.AreEqual(FinancingDecision.ConditionallyApproved, conditional.Decision);
            Assert.AreEqual(FinancingDecision.Declined, declined.Decision);
            CollectionAssert.Contains(conditional.ReasonCodes, FinancingReasonCode.ThinDownPayment);
            CollectionAssert.Contains(declined.ReasonCodes, FinancingReasonCode.ExceedsLoanToValue);
        }

        [Test]
        public void ReputationTrustCollateralAndDebtAffectTermsAndApproval()
        {
            FinancingEvaluator evaluator = new FinancingEvaluator();
            FinancingApprovalResult strong = evaluator.EvaluateApproval(CreateApprovalRequest(
                "strong",
                CreateStrongApplicant(),
                CreateStrongCollateral(),
                130000));
            FinancingApprovalResult weak = evaluator.EvaluateApproval(CreateApprovalRequest(
                "weak",
                CreateWeakApplicant(),
                CreateWeakCollateral(),
                130000));

            Assert.AreEqual(FinancingDecision.Approved, strong.Decision);
            Assert.AreEqual(FinancingDecision.Declined, weak.Decision);
            Assert.Greater(weak.Offer.termStructure.annualInterestRateBps, strong.Offer.termStructure.annualInterestRateBps);
            CollectionAssert.Contains(strong.ReasonCodes, FinancingReasonCode.StrongReputation);
            CollectionAssert.Contains(strong.ReasonCodes, FinancingReasonCode.StrongCollateral);
            CollectionAssert.Contains(weak.ReasonCodes, FinancingReasonCode.WeakReputation);
            CollectionAssert.Contains(weak.ReasonCodes, FinancingReasonCode.HeavyDebtBurden);
        }

        [Test]
        public void OperatingReliabilitySeparatelyAffectsRateAndApprovalConditions()
        {
            FinancingEvaluator evaluator = new FinancingEvaluator();
            FinancingApplicantProfile strong = CreateStrongApplicant();
            strong.operationalReliability01 = 0.85f;

            FinancingApplicantProfile weakOperating = CreateStrongApplicant();
            weakOperating.operationalReliability01 = 0.08f;

            FinancingApprovalResult strongResult = evaluator.EvaluateApproval(CreateApprovalRequest(
                "strong_ops",
                strong,
                CreateStrongCollateral(),
                90000));
            FinancingApprovalResult weakOperatingResult = evaluator.EvaluateApproval(CreateApprovalRequest(
                "weak_ops",
                weakOperating,
                CreateStrongCollateral(),
                90000));

            Assert.AreEqual(FinancingDecision.ConditionallyApproved, strongResult.Decision);
            Assert.AreEqual(FinancingDecision.Declined, weakOperatingResult.Decision);
            Assert.Greater(weakOperatingResult.Offer.termStructure.annualInterestRateBps, strongResult.Offer.termStructure.annualInterestRateBps);
            Assert.IsTrue(weakOperatingResult.RequiredConditions.Any(condition => condition.IndexOf("operating", StringComparison.OrdinalIgnoreCase) >= 0));
        }

        [Test]
        public void EstimateCanLookLikelyWhileExactApprovalDeclines()
        {
            FinancingEvaluator evaluator = new FinancingEvaluator();
            FinancingEstimate estimate = evaluator.EstimateFinancing(new FinancingEstimateRequest
            {
                requestId = "estimate_likely",
                purpose = LoanPurpose.Acquisition,
                expectedAcquisitionPriceCents = 500000,
                estimatedCollateralValueCents = 650000,
                desiredDownPaymentCents = 125000,
                applicant = CreateStrongApplicant(),
                requestedTerm = CreateRequestedTerm(),
                estimateDate = CreateStartDate()
            });
            FinancingApprovalResult approval = evaluator.EvaluateApproval(CreateApprovalRequest(
                "exact_decline",
                CreateStrongApplicant(),
                CreateWeakCollateral(),
                125000));

            Assert.AreEqual(FinancingDecision.EstimateOnly, estimate.Decision);
            CollectionAssert.Contains(estimate.ReasonCodes, FinancingReasonCode.WithinLenderPolicy);
            Assert.AreEqual(FinancingDecision.Declined, approval.Decision);
            CollectionAssert.Contains(approval.ReasonCodes, FinancingReasonCode.ExceedsLoanToValue);
        }

        [Test]
        public void PaymentScheduleBalancesPrincipalAndCreatesIncreasingDueDates()
        {
            LoanTermStructure weeklyTerm = CreateRequestedTerm();
            weeklyTerm.principalCents = 390000;
            weeklyTerm.downPaymentCents = 110000;
            weeklyTerm.annualInterestRateBps = 900;
            weeklyTerm.termMonths = 12;
            weeklyTerm.amortizationMonths = 12;
            weeklyTerm.repaymentFrequency = RepaymentFrequency.Weekly;

            LoanPaymentSchedule weekly = new PaymentScheduleBuilder().Build("weekly", weeklyTerm, CreateStartDate());

            Assert.Greater(weekly.payments.Count, 0);
            Assert.AreEqual(weeklyTerm.principalCents, weekly.totalPrincipalCents);
            Assert.Greater(weekly.totalInterestCents, 0);
            Assert.IsTrue(weekly.payments.Zip(weekly.payments.Skip(1), (left, right) => right.dueDayIndex > left.dueDayIndex).All(value => value));

            LoanTermStructure monthlyTerm = weeklyTerm;
            monthlyTerm.repaymentFrequency = RepaymentFrequency.Monthly;
            LoanPaymentSchedule monthly = new PaymentScheduleBuilder().Build("monthly", monthlyTerm, CreateStartDate(), 28, 28);

            Assert.AreEqual(monthlyTerm.termMonths, monthly.payments.Count);
            Assert.AreEqual(monthlyTerm.principalCents, monthly.totalPrincipalCents);
            Assert.IsTrue(monthly.payments.Zip(monthly.payments.Skip(1), (left, right) => right.dueDayIndex > left.dueDayIndex).All(value => value));
        }

        [Test]
        public void FinancingFailureConsequencesEscalateAfterTentativeAgreementWithoutContingency()
        {
            FinancingFailureEvaluator evaluator = new FinancingFailureEvaluator();
            FinancingFailureResult preAgreement = evaluator.EvaluateFailure(new FinancingFailureRequest
            {
                tentativeAgreementId = string.Empty,
                earnestMoneyCents = 50000,
                hasTentativeAgreement = false,
                financingContingencyPresent = false
            });
            FinancingFailureResult protectedFailure = evaluator.EvaluateFailure(CreateFailureRequest(true, false));
            FinancingFailureResult unprotectedFailure = evaluator.EvaluateFailure(CreateFailureRequest(false, true));

            Assert.AreEqual(0f, preAgreement.ReputationDelta01);
            Assert.AreEqual(0, preAgreement.ForfeitedEarnestMoneyCents);
            Assert.Less(protectedFailure.ReputationDelta01, preAgreement.ReputationDelta01);
            Assert.Less(unprotectedFailure.ReputationDelta01, protectedFailure.ReputationDelta01);
            Assert.Less(unprotectedFailure.LenderTrustDelta01, protectedFailure.LenderTrustDelta01);
            Assert.AreEqual(12500, protectedFailure.ForfeitedEarnestMoneyCents);
            Assert.AreEqual(50000, unprotectedFailure.ForfeitedEarnestMoneyCents);
            CollectionAssert.Contains(protectedFailure.ReasonCodes, FinancingReasonCode.FinancingContingencyProtected);
            CollectionAssert.Contains(unprotectedFailure.ReasonCodes, FinancingReasonCode.FinancingContingencyMissing);
        }

        [Test]
        public void FinancingFailureDeadlineMissAddsReasonAndWorsensTrustDamage()
        {
            FinancingFailureEvaluator evaluator = new FinancingFailureEvaluator();
            FinancingFailureResult protectedFailure = evaluator.EvaluateFailure(CreateFailureRequest(true, false));
            FinancingFailureResult missedDeadline = evaluator.EvaluateFailure(CreateFailureRequest(true, true));

            CollectionAssert.Contains(missedDeadline.ReasonCodes, FinancingReasonCode.DeadlineMissed);
            Assert.Less(missedDeadline.ReputationDelta01, protectedFailure.ReputationDelta01);
            Assert.Less(missedDeadline.LenderTrustDelta01, protectedFailure.LenderTrustDelta01);
            Assert.Greater(missedDeadline.CooldownDays, protectedFailure.CooldownDays);
            Assert.Greater(missedDeadline.FailureStrikes, protectedFailure.FailureStrikes);
        }

        private static FinancingApprovalRequest CreateApprovalRequest(
            string agreementId,
            FinancingApplicantProfile applicant,
            CollateralProfile collateral,
            int downPaymentCents)
        {
            return new FinancingApprovalRequest
            {
                agreementId = agreementId,
                purpose = LoanPurpose.Acquisition,
                exactPurchasePriceCents = 500000,
                exactDownPaymentCents = downPaymentCents,
                applicant = applicant,
                collateral = collateral,
                requestedTerm = CreateRequestedTerm(),
                approvalDate = CreateStartDate()
            };
        }

        private static FinancingApplicantProfile CreateStrongApplicant()
        {
            return new FinancingApplicantProfile
            {
                applicantId = "player",
                reputation01 = 0.82f,
                lenderTrust01 = 0.76f,
                operationalReliability01 = 0.78f,
                availableCashCents = 180000,
                existingDebtPaymentCents = 2000,
                weeklyNetCashFlowCents = 18000,
                ownedAssetValueCents = 320000,
                priorFailedFinancingCount = 0
            };
        }

        private static FinancingApplicantProfile CreateWeakApplicant()
        {
            return new FinancingApplicantProfile
            {
                applicantId = "player",
                reputation01 = 0.18f,
                lenderTrust01 = 0.12f,
                operationalReliability01 = 0.16f,
                availableCashCents = 30000,
                existingDebtPaymentCents = 8000,
                weeklyNetCashFlowCents = 12000,
                ownedAssetValueCents = 25000,
                priorFailedFinancingCount = 3
            };
        }

        private static CollateralProfile CreateStrongCollateral()
        {
            return new CollateralProfile
            {
                collateralId = "parcel_001",
                kind = LoanCollateralKind.ImprovedProperty,
                estimatedValueCents = 700000,
                lienPriority = 1,
                liquidity01 = 0.7f,
                condition01 = 0.82f,
                encumberedValueCents = 0
            };
        }

        private static CollateralProfile CreateWeakCollateral()
        {
            return new CollateralProfile
            {
                collateralId = "inventory_only",
                kind = LoanCollateralKind.Inventory,
                estimatedValueCents = 100000,
                lienPriority = 1,
                liquidity01 = 0.35f,
                condition01 = 0.55f,
                encumberedValueCents = 25000
            };
        }

        private static LoanTermStructure CreateRequestedTerm()
        {
            return new LoanTermStructure
            {
                annualInterestRateBps = 0,
                termMonths = 60,
                amortizationMonths = 60,
                repaymentFrequency = RepaymentFrequency.Weekly
            };
        }

        private static FinancingFailureRequest CreateFailureRequest(bool hasContingency, bool missedDeadline)
        {
            return new FinancingFailureRequest
            {
                tentativeAgreementId = "agreement_001",
                offeredPriceCents = 500000,
                earnestMoneyCents = 50000,
                hasTentativeAgreement = true,
                financingContingencyPresent = hasContingency,
                closingDeadlineMissed = missedDeadline,
                sellerPressure01 = 0.7f,
                sellerRelationshipSensitivity01 = 0.8f,
                applicantReputation01 = 0.65f,
                lenderTrust01 = 0.6f,
                failureReason = "Bank declined final underwriting.",
                daysUntilClosingDeadline = missedDeadline ? -1 : 5
            };
        }

        private static SimulationDate CreateStartDate()
        {
            return new SimulationDate(0, 0, 1, 1, 1, 1, 1);
        }
    }
}
