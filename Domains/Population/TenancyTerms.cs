using System;

namespace LandLedgers.Population
{
    /// <summary>
    /// Pass III foundation: tenancy payment terms + rent obligation state.
    /// Canon: XXXI Part VI §§6.1-6.8; Tech XIV Part V §§5.1-5.7.
    ///
    /// PropertyAgreement (HousingFoundation) exists as the legal tenancy record but
    /// carries NO payment terms today — a tenancy exists legally but cannot generate
    /// a payment obligation. These contracts are the ADDITIVE extension point:
    /// attach TenancyPaymentTerms to the agreement; the rent/board loop (Worker B)
    /// turns terms into due events.
    ///
    /// Money movement: HouseholdLedger debit (tenant household) -> landlord credit,
    /// arrears as liabilities through FinancialObligationAuthority. No second
    /// rent-cash authority. No automatic eviction on missed rent (legal enforcement
    /// is its own legitimate process).
    /// </summary>

    [Serializable]
    public class TenancyPaymentTerms
    {
        /// <summary>Rent/board per period, in cents.</summary>
        public long AmountCents;
        public int PeriodDays;
        /// <summary>1-based day-of-period the payment is due.</summary>
        public int DueDayOfPeriod;
        public int StartDay;
        /// <summary>Person/business/government id receiving payment.</summary>
        public string PayeeHolderId;
    }

    public enum RentObligationStatus
    {
        Current = 0,
        Partial = 1,
        Overdue = 2,
    }

    [Serializable]
    public class RentObligationState
    {
        public string AgreementId;
        public TenancyPaymentTerms Terms;
        public int DueDay;
        public long AmountDueCents;
        public long PaidCents;
        public RentObligationStatus Status = RentObligationStatus.Current;
    }
}
