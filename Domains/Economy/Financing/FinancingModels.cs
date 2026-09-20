using System;
using System.Collections.Generic;
using LandLedgers.Time;
using UnityEngine;

namespace LandLedgers.Economy.Financing
{
    public enum LoanPurpose
    {
        Acquisition = 0,
        Refinance = 1,
        WorkingCapital = 2,
        Construction = 3,
        Emergency = 4,
        PropertyPurchase = 5,
        BusinessAcquisition = 6,
        ProjectConstruction = 7,
        RescueLiquidity = 8,
        Infrastructure = 9,
        Speculation = 10,
        MineDevelopment = 11,
        RailDevelopment = 12
    }

    public static class LoanPurposeRules
    {
        public static LoanPurpose Sanitize(LoanPurpose purpose, LoanPurpose fallback = LoanPurpose.WorkingCapital)
        {
            return Enum.IsDefined(typeof(LoanPurpose), purpose) ? purpose : fallback;
        }

        public static string GetDisplayName(LoanPurpose purpose)
        {
            switch (Sanitize(purpose))
            {
                case LoanPurpose.Acquisition:
                    return "Acquisition";
                case LoanPurpose.Refinance:
                    return "Refinance";
                case LoanPurpose.WorkingCapital:
                    return "Working Capital";
                case LoanPurpose.Construction:
                    return "Construction";
                case LoanPurpose.Emergency:
                    return "Emergency Liquidity";
                case LoanPurpose.PropertyPurchase:
                    return "Property Purchase";
                case LoanPurpose.BusinessAcquisition:
                    return "Business Acquisition";
                case LoanPurpose.ProjectConstruction:
                    return "Project / Construction";
                case LoanPurpose.RescueLiquidity:
                    return "Rescue Liquidity";
                case LoanPurpose.Infrastructure:
                    return "Infrastructure";
                case LoanPurpose.Speculation:
                    return "Speculation";
                case LoanPurpose.MineDevelopment:
                    return "Mine Development";
                case LoanPurpose.RailDevelopment:
                    return "Rail Development";
                default:
                    return "Working Capital";
            }
        }

        public static bool IsAllowedBy(List<LoanPurpose> allowedPurposes, LoanPurpose purpose)
        {
            if (allowedPurposes == null || allowedPurposes.Count == 0)
            {
                return true;
            }

            LoanPurpose sanitized = Sanitize(purpose);
            LoanPurpose broad = GetBroadPurpose(sanitized);
            for (int i = 0; i < allowedPurposes.Count; i++)
            {
                LoanPurpose allowed = Sanitize(allowedPurposes[i], LoanPurpose.Acquisition);
                if (allowed == sanitized || allowed == broad)
                {
                    return true;
                }
            }

            return false;
        }

        public static LoanPurpose GetBroadPurpose(LoanPurpose purpose)
        {
            return Sanitize(purpose) switch
            {
                LoanPurpose.PropertyPurchase => LoanPurpose.Acquisition,
                LoanPurpose.BusinessAcquisition => LoanPurpose.Acquisition,
                LoanPurpose.ProjectConstruction => LoanPurpose.Construction,
                LoanPurpose.Infrastructure => LoanPurpose.Construction,
                LoanPurpose.RescueLiquidity => LoanPurpose.Emergency,
                LoanPurpose.Speculation => LoanPurpose.Acquisition,
                LoanPurpose.MineDevelopment => LoanPurpose.Acquisition,
                LoanPurpose.RailDevelopment => LoanPurpose.Construction,
                _ => Sanitize(purpose)
            };
        }
    }

    public enum LoanCollateralKind
    {
        Land = 0,
        ImprovedProperty = 1,
        OperatingBusiness = 2,
        Inventory = 3,
        Unsecured = 4
    }

    public enum LoanStatus
    {
        Proposed = 0,
        Approved = 1,
        Active = 2,
        PaidOff = 3,
        Delinquent = 4,
        Defaulted = 5,
        Cancelled = 6,
        Recovered = 7
    }

    public enum RepaymentFrequency
    {
        Weekly = 0,
        Monthly = 1
    }

    public enum FinancingDecision
    {
        EstimateOnly = 0,
        Approved = 1,
        ConditionallyApproved = 2,
        Declined = 3
    }

    public enum FinancingReasonCode
    {
        None = 0,
        StrongReputation = 1,
        WeakReputation = 2,
        StrongCollateral = 3,
        WeakCollateral = 4,
        SufficientDownPayment = 5,
        ThinDownPayment = 6,
        PositiveCashFlow = 7,
        WeakCashFlow = 8,
        HeavyDebtBurden = 9,
        LowLenderTrust = 10,
        ExceedsLoanToValue = 11,
        MissingCollateral = 12,
        FailedClosingHistory = 13,
        WithinLenderPolicy = 14,
        OutsideLenderPolicy = 15,
        FinancingContingencyProtected = 16,
        FinancingContingencyMissing = 17,
        DeadlineMissed = 18,
        TentativeAgreementAtRisk = 19,
        PreAgreementDenial = 20,
        RecentLenderSetback = 21,
        StrongOperationalRecord = 22,
        WeakOperationalRecord = 23
    }

    public enum LoanPaymentStatus
    {
        Scheduled = 0,
        PartiallyPaid = 1,
        Paid = 2,
        Late = 3,
        Skipped = 4
    }

    [Serializable]
    public struct LenderProfile
    {
        public string lenderId;
        public string displayName;
        public float trust01;
        public int baseAnnualInterestRateBps;
        public float maxLoanToValue01;
        public float minimumDownPaymentRatio01;
        public float minimumReputation01;
        public float minimumCashFlowCoverageRatio;
        public float collateralHaircut01;
        public float riskTolerance01;
        public List<LoanPurpose> allowedPurposes;

        public static LenderProfile DefaultLocalBank => new LenderProfile
        {
            lenderId = "frontier_local_bank",
            displayName = "Frontier Local Bank",
            trust01 = 0.58f,
            baseAnnualInterestRateBps = 900,
            maxLoanToValue01 = 0.72f,
            minimumDownPaymentRatio01 = 0.2f,
            minimumReputation01 = 0.35f,
            minimumCashFlowCoverageRatio = 1.15f,
            collateralHaircut01 = 0.15f,
            riskTolerance01 = 0.48f,
            allowedPurposes = new List<LoanPurpose>
            {
                LoanPurpose.Acquisition,
                LoanPurpose.Refinance,
                LoanPurpose.WorkingCapital,
                LoanPurpose.Construction
            }
        };

        public LenderProfile Sanitized()
        {
            LenderProfile sanitized = this;
            sanitized.lenderId = string.IsNullOrWhiteSpace(sanitized.lenderId) ? "frontier_local_bank" : sanitized.lenderId;
            sanitized.displayName = string.IsNullOrWhiteSpace(sanitized.displayName) ? sanitized.lenderId : sanitized.displayName;
            sanitized.trust01 = Mathf.Clamp01(sanitized.trust01);
            sanitized.baseAnnualInterestRateBps = Mathf.Max(0, sanitized.baseAnnualInterestRateBps);
            sanitized.maxLoanToValue01 = Mathf.Clamp(sanitized.maxLoanToValue01 <= 0f ? 0.72f : sanitized.maxLoanToValue01, 0f, 0.95f);
            sanitized.minimumDownPaymentRatio01 = Mathf.Clamp(sanitized.minimumDownPaymentRatio01 <= 0f ? 0.2f : sanitized.minimumDownPaymentRatio01, 0f, 0.9f);
            sanitized.minimumReputation01 = Mathf.Clamp01(sanitized.minimumReputation01);
            sanitized.minimumCashFlowCoverageRatio = Mathf.Max(0f, sanitized.minimumCashFlowCoverageRatio <= 0f ? 1.15f : sanitized.minimumCashFlowCoverageRatio);
            sanitized.collateralHaircut01 = Mathf.Clamp01(sanitized.collateralHaircut01);
            sanitized.riskTolerance01 = Mathf.Clamp01(sanitized.riskTolerance01);
            if (sanitized.allowedPurposes == null || sanitized.allowedPurposes.Count == 0)
            {
                sanitized.allowedPurposes = new List<LoanPurpose> { LoanPurpose.Acquisition };
            }
            else
            {
                for (int i = 0; i < sanitized.allowedPurposes.Count; i++)
                {
                    sanitized.allowedPurposes[i] = LoanPurposeRules.Sanitize(sanitized.allowedPurposes[i], LoanPurpose.Acquisition);
                }
            }

            return sanitized;
        }

        public bool AllowsPurpose(LoanPurpose purpose)
        {
            return LoanPurposeRules.IsAllowedBy(allowedPurposes, purpose);
        }
    }

    [Serializable]
    public struct FinancingApplicantProfile
    {
        public string applicantId;
        public float reputation01;
        public float lenderTrust01;
        public float operationalReliability01;
        public int availableCashCents;
        public int existingDebtPaymentCents;
        public int weeklyNetCashFlowCents;
        public int ownedAssetValueCents;
        public int priorFailedFinancingCount;
        public int lenderReapplyCooldownDays;
        public float lenderCaution01;

        public FinancingApplicantProfile Sanitized()
        {
            FinancingApplicantProfile sanitized = this;
            sanitized.applicantId ??= string.Empty;
            sanitized.reputation01 = Mathf.Clamp01(sanitized.reputation01);
            sanitized.lenderTrust01 = Mathf.Clamp01(sanitized.lenderTrust01);
            sanitized.operationalReliability01 = Mathf.Clamp01(sanitized.operationalReliability01);
            sanitized.availableCashCents = Mathf.Max(0, sanitized.availableCashCents);
            sanitized.existingDebtPaymentCents = Mathf.Max(0, sanitized.existingDebtPaymentCents);
            sanitized.ownedAssetValueCents = Mathf.Max(0, sanitized.ownedAssetValueCents);
            sanitized.priorFailedFinancingCount = Mathf.Max(0, sanitized.priorFailedFinancingCount);
            sanitized.lenderReapplyCooldownDays = Mathf.Max(0, sanitized.lenderReapplyCooldownDays);
            sanitized.lenderCaution01 = Mathf.Clamp01(sanitized.lenderCaution01);
            return sanitized;
        }
    }

    [Serializable]
    public struct CollateralProfile
    {
        public string collateralId;
        public LoanCollateralKind kind;
        public int estimatedValueCents;
        public int lienPriority;
        public float liquidity01;
        public float condition01;
        public int encumberedValueCents;

        public int UnencumberedValueCents => Mathf.Max(0, estimatedValueCents - encumberedValueCents);

        public CollateralProfile Sanitized()
        {
            CollateralProfile sanitized = this;
            sanitized.collateralId ??= string.Empty;
            sanitized.estimatedValueCents = Mathf.Max(0, sanitized.estimatedValueCents);
            sanitized.lienPriority = Mathf.Max(0, sanitized.lienPriority);
            sanitized.liquidity01 = Mathf.Clamp01(sanitized.liquidity01);
            sanitized.condition01 = Mathf.Clamp01(sanitized.condition01);
            sanitized.encumberedValueCents = Mathf.Clamp(sanitized.encumberedValueCents, 0, sanitized.estimatedValueCents);
            return sanitized;
        }
    }

    [Serializable]
    public struct LoanTermStructure
    {
        public int principalCents;
        public int downPaymentCents;
        public int annualInterestRateBps;
        public int termMonths;
        public int amortizationMonths;
        public RepaymentFrequency repaymentFrequency;
        public int originationFeeCents;
        public int balloonPaymentCents;

        public LoanTermStructure Sanitized()
        {
            LoanTermStructure sanitized = this;
            sanitized.principalCents = Mathf.Max(0, sanitized.principalCents);
            sanitized.downPaymentCents = Mathf.Max(0, sanitized.downPaymentCents);
            sanitized.annualInterestRateBps = Mathf.Max(0, sanitized.annualInterestRateBps);
            sanitized.termMonths = Mathf.Max(1, sanitized.termMonths <= 0 ? 60 : sanitized.termMonths);
            sanitized.amortizationMonths = Mathf.Max(sanitized.termMonths, sanitized.amortizationMonths <= 0 ? sanitized.termMonths : sanitized.amortizationMonths);
            sanitized.originationFeeCents = Mathf.Max(0, sanitized.originationFeeCents);
            sanitized.balloonPaymentCents = Mathf.Clamp(sanitized.balloonPaymentCents, 0, sanitized.principalCents);
            sanitized.repaymentFrequency = Enum.IsDefined(typeof(RepaymentFrequency), sanitized.repaymentFrequency) ? sanitized.repaymentFrequency : RepaymentFrequency.Weekly;
            return sanitized;
        }
    }

    [Serializable]
    public sealed class LoanOffer
    {
        public string offerId;
        public string lenderId;
        public LoanPurpose purpose;
        public LoanTermStructure termStructure;
        public int maxPrincipalCents;
        public int requiredDownPaymentCents;
        public FinancingDecision decision;
        public List<FinancingReasonCode> reasonCodes = new List<FinancingReasonCode>();
        public SimulationDate expiresOnDate;

        // Keep compact offer readouts close to the offer data so callers do not rebuild these terms inconsistently.
        public int EstimatedPeriodicPaymentCents => PaymentScheduleBuilder.EstimatePaymentCents(termStructure);
        public int EstimatedTotalBorrowerCostCents => PaymentScheduleBuilder.CalculateTotalBorrowerCostCents(termStructure, null);
        public int EstimatedOriginationFeeCents => termStructure.Sanitized().originationFeeCents;
        public int EstimatedPrincipalCents => termStructure.Sanitized().principalCents;
    }

    [Serializable]
    public sealed class LoanPaymentDue
    {
        public int paymentNumber;
        public SimulationDate dueDate;
        public int dueDayIndex;
        public int principalCents;
        public int interestCents;
        public int totalDueCents;
        public int paidCents;
        public LoanPaymentStatus status;
    }

    [Serializable]
    public sealed class LoanPaymentSchedule
    {
        public string loanId;
        public int principalCents;
        public int totalPrincipalCents;
        public int totalInterestCents;
        public int totalPaymentCents;
        public int originationFeeCents;
        public List<LoanPaymentDue> payments = new List<LoanPaymentDue>();
    }

    [Serializable]
    public sealed class LoanContract
    {
        public string loanId;
        public string lenderId;
        public LoanPurpose purpose;
        public List<string> collateralIds = new List<string>();
        public LoanStatus status;
        public LoanTermStructure termStructure;
        public int remainingPrincipalCents;
        public SimulationDate nextPaymentDueDate;
        public LoanPaymentSchedule schedule;
        // Keep delinquency-workout state on the contract so save/load can carry revised-note history without a second authority.
        public int workoutCount;
        public int lastWorkoutDayIndex = -1;
    }

    [Serializable]
    public struct FinancingEstimateRequest
    {
        public string requestId;
        public LoanPurpose purpose;
        public int expectedAcquisitionPriceCents;
        public int estimatedCollateralValueCents;
        public int desiredDownPaymentCents;
        public FinancingApplicantProfile applicant;
        public LenderProfile lender;
        public LoanTermStructure requestedTerm;
        public SimulationDate estimateDate;
        public int daysPerMonth;
    }

    [Serializable]
    public sealed class FinancingEstimate
    {
        public FinancingEstimate(
            string requestId,
            string likelyLenderId,
            FinancingDecision decision,
            int maxPrincipalCents,
            int minimumDownPaymentCents,
            int estimatedAnnualInterestRateBps,
            int estimatedPaymentCents,
            LoanTermStructure estimatedTerm,
            IReadOnlyList<FinancingReasonCode> reasonCodes)
        {
            RequestId = requestId ?? string.Empty;
            LikelyLenderId = likelyLenderId ?? string.Empty;
            Decision = decision;
            MaxPrincipalCents = Mathf.Max(0, maxPrincipalCents);
            MinimumDownPaymentCents = Mathf.Max(0, minimumDownPaymentCents);
            EstimatedAnnualInterestRateBps = Mathf.Max(0, estimatedAnnualInterestRateBps);
            EstimatedPaymentCents = Mathf.Max(0, estimatedPaymentCents);
            EstimatedTerm = estimatedTerm.Sanitized();
            ReasonCodes = reasonCodes ?? Array.Empty<FinancingReasonCode>();
        }

        public string RequestId { get; }
        public string LikelyLenderId { get; }
        public FinancingDecision Decision { get; }
        public int MaxPrincipalCents { get; }
        public int MinimumDownPaymentCents { get; }
        public int EstimatedAnnualInterestRateBps { get; }
        public int EstimatedPaymentCents { get; }
        public LoanTermStructure EstimatedTerm { get; }
        public IReadOnlyList<FinancingReasonCode> ReasonCodes { get; }
    }

    [Serializable]
    public struct FinancingApprovalRequest
    {
        public string agreementId;
        public LoanPurpose purpose;
        public int exactPurchasePriceCents;
        public int exactDownPaymentCents;
        public FinancingApplicantProfile applicant;
        public LenderProfile lender;
        public CollateralProfile collateral;
        public LoanTermStructure requestedTerm;
        public SimulationDate approvalDate;
        public int daysPerMonth;
    }

    [Serializable]
    public sealed class FinancingApprovalResult
    {
        public FinancingApprovalResult(
            string agreementId,
            FinancingDecision decision,
            LoanOffer offer,
            IReadOnlyList<string> requiredConditions,
            IReadOnlyList<FinancingReasonCode> reasonCodes,
            int additionalDownPaymentNeededCents,
            int principalSupportShortfallCents,
            int weeklyCashFlowShortfallCents,
            float reputationShortfall01,
            float lenderTrustShortfall01)
        {
            AgreementId = agreementId ?? string.Empty;
            Decision = decision;
            Offer = offer;
            RequiredConditions = requiredConditions ?? Array.Empty<string>();
            ReasonCodes = reasonCodes ?? Array.Empty<FinancingReasonCode>();
            AdditionalDownPaymentNeededCents = Mathf.Max(0, additionalDownPaymentNeededCents);
            PrincipalSupportShortfallCents = Mathf.Max(0, principalSupportShortfallCents);
            WeeklyCashFlowShortfallCents = Mathf.Max(0, weeklyCashFlowShortfallCents);
            ReputationShortfall01 = Mathf.Clamp01(reputationShortfall01);
            LenderTrustShortfall01 = Mathf.Clamp01(lenderTrustShortfall01);
        }

        public string AgreementId { get; }
        public FinancingDecision Decision { get; }
        public LoanOffer Offer { get; }
        public IReadOnlyList<string> RequiredConditions { get; }
        public IReadOnlyList<FinancingReasonCode> ReasonCodes { get; }
        public int AdditionalDownPaymentNeededCents { get; }
        public int PrincipalSupportShortfallCents { get; }
        public int WeeklyCashFlowShortfallCents { get; }
        public float ReputationShortfall01 { get; }
        public float LenderTrustShortfall01 { get; }
        public bool Approved => Decision == FinancingDecision.Approved || Decision == FinancingDecision.ConditionallyApproved;
        public bool CanFundImmediately => Decision == FinancingDecision.Approved;
        public bool HasOffer => Offer != null;
        public bool HasConditions => RequiredConditions != null && RequiredConditions.Count > 0;
        public bool HasOutstandingConditions => Decision == FinancingDecision.ConditionallyApproved && HasConditions;
        public bool HasQuantifiedShortfall => AdditionalDownPaymentNeededCents > 0
            || PrincipalSupportShortfallCents > 0
            || WeeklyCashFlowShortfallCents > 0
            || ReputationShortfall01 > 0f
            || LenderTrustShortfall01 > 0f;
        // Closing should only proceed when the bank is fully approving the file and no quantified blocker still remains.
        public bool HasBlockingReadinessGap => !CanFundImmediately || HasConditions || HasQuantifiedShortfall;
        public int EstimatedPrincipalCents => Offer != null ? Offer.EstimatedPrincipalCents : 0;
        public int EstimatedOriginationFeeCents => Offer != null ? Offer.EstimatedOriginationFeeCents : 0;
        public int EstimatedPeriodicPaymentCents => Offer != null ? Offer.EstimatedPeriodicPaymentCents : 0;
        public int EstimatedTotalBorrowerCostCents => Offer != null ? Offer.EstimatedTotalBorrowerCostCents : 0;
    }

    [Serializable]
    public struct FinancingFailureRequest
    {
        public string tentativeAgreementId;
        public int offeredPriceCents;
        public int earnestMoneyCents;
        public bool hasTentativeAgreement;
        public bool financingContingencyPresent;
        public bool closingDeadlineMissed;
        public float sellerPressure01;
        public float sellerRelationshipSensitivity01;
        public float applicantReputation01;
        public float lenderTrust01;
        public string failureReason;
        public int daysUntilClosingDeadline;
    }

    [Serializable]
    public sealed class FinancingFailureResult
    {
        // These fields intentionally model a local lender setback rather than a permanent global credit score.
        public FinancingFailureResult(
            string tentativeAgreementId,
            float reputationDelta01,
            float lenderTrustDelta01,
            float sellerTrustDelta01,
            int forfeitedEarnestMoneyCents,
            int cooldownDays,
            int failureStrikes,
            float lenderCaution01,
            string summary,
            IReadOnlyList<FinancingReasonCode> reasonCodes)
        {
            TentativeAgreementId = tentativeAgreementId ?? string.Empty;
            ReputationDelta01 = Mathf.Clamp(reputationDelta01, -1f, 1f);
            LenderTrustDelta01 = Mathf.Clamp(lenderTrustDelta01, -1f, 1f);
            SellerTrustDelta01 = Mathf.Clamp(sellerTrustDelta01, -1f, 1f);
            ForfeitedEarnestMoneyCents = Mathf.Max(0, forfeitedEarnestMoneyCents);
            CooldownDays = Mathf.Max(0, cooldownDays);
            FailureStrikes = Mathf.Max(0, failureStrikes);
            LenderCaution01 = Mathf.Clamp01(lenderCaution01);
            Summary = summary ?? string.Empty;
            ReasonCodes = reasonCodes ?? Array.Empty<FinancingReasonCode>();
        }

        public string TentativeAgreementId { get; }
        public float ReputationDelta01 { get; }
        public float LenderTrustDelta01 { get; }
        public float SellerTrustDelta01 { get; }
        public int ForfeitedEarnestMoneyCents { get; }
        public int CooldownDays { get; }
        public int FailureStrikes { get; }
        public float LenderCaution01 { get; }
        public string Summary { get; }
        public IReadOnlyList<FinancingReasonCode> ReasonCodes { get; }
    }
}
