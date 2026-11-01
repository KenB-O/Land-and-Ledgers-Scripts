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
        /// <summary>
        /// Pass III Worker B (additive): the tenant household charged for this
        /// agreement's periods. -1 = resolve from the current residential
        /// occupancy of the agreement's space (HousingFoundation chain).
        /// Canon XXXI Part VI: the tenancy agreement names the parties; the
        /// occupancy record says who actually lives there. When both exist
        /// they must agree; the explicit id wins and mismatch is diagnosed.
        /// </summary>
        public int TenantHouseholdId = -1;
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
        /// <summary>
        /// Pass III Worker B (additive): 0-based period index within the
        /// agreement's payment schedule. The settled-period marker is
        /// (AgreementId, PeriodIndex) — a period is never settled twice,
        /// including across save/load.
        /// </summary>
        public int PeriodIndex = -1;
        /// <summary>
        /// Pass III Worker B (additive): the FinancialObligationAuthority
        /// obligation id when unpaid remainder became real arrears; empty
        /// when the period was paid in full.
        /// </summary>
        public string ArrearsObligationId = string.Empty;
    }
}
