using System;
using System.Collections.Generic;
using LandLedgers.Time;

namespace LandLedgers.Economy.Financing
{
    public enum BankLoanApplicationStatus
    {
        None = 0,
        PendingReview = 1,
        Approved = 2,
        Declined = 3
    }

    public enum BankLoanReviewBand
    {
        Routine = 0,
        Watchlist = 1,
        Significant = 2
    }

    [Serializable]
    public sealed class BankLoanApplicationState
    {
        public string applicationId = string.Empty;
        public BankLoanApplicationStatus status = BankLoanApplicationStatus.None;
        public LoanPurpose purpose = LoanPurpose.WorkingCapital;
        public int requestedAmountCents;
        public int submittedDayIndex = -1;
        public int decisionDayIndex = -1;
        public SimulationDate submittedDate;
        public SimulationDate decisionDate;
        public LoanTermStructure estimatedTerm;
        public LoanPaymentSchedule estimatedSchedule;
        public FinancingDecision decision = FinancingDecision.EstimateOnly;
        public List<FinancingReasonCode> reasonCodes = new();
        public string decisionSummary = string.Empty;
        public string declineReason = string.Empty;
        public BankLoanReviewBand reviewBand = BankLoanReviewBand.Routine;
        public int scheduledReviewDays = 1;
        public string reviewContext = string.Empty;
        // Short-lived lender memory keeps nearby retries honest without introducing a broader bank-history authority.
        public int reapplyEarliestDayIndex = -1;
        public SimulationDate reapplyEarliestDate;
        public float lenderCaution01;
        public string lenderMemorySummary = string.Empty;

        public bool IsPending => status == BankLoanApplicationStatus.PendingReview;
    }

    [Serializable]
    public sealed class BankLoanUnderwritingResult
    {
        public FinancingDecision decision = FinancingDecision.Declined;
        public LoanPurpose purpose = LoanPurpose.WorkingCapital;
        public LoanTermStructure term;
        public int estimatedPaymentCents;
        public int estimatedWeeklyEquivalentPaymentCents;
        public int estimatedTotalOwedCents;
        public int estimatedPrincipalCents;
        public int estimatedInterestCents;
        public int estimatedOriginationFeeCents;
        public int principalCapacityCents;
        public int weeklyPaymentCapacityCents;
        public List<FinancingReasonCode> reasonCodes = new();
        public string summary = string.Empty;
        public string declineReason = string.Empty;
        public BankLoanReviewBand reviewBand = BankLoanReviewBand.Routine;
        public int reviewDelayDays;
        // reviewDays is the full turnaround shown to the player; delayDays is the extra time added by band/policy above the routine floor.
        public int reviewDays = 1;
        public string reviewContext = string.Empty;

        public bool Approved => decision == FinancingDecision.Approved;
    }
}
