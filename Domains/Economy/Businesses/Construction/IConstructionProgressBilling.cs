using System.Collections.Generic;

namespace LandLedgers.Economy
{
    /// <summary>
    /// D4G: the seam the D4H progress-billing package will consume.
    ///
    /// D4G records payment TERMS (deposit, milestones, completion balance —
    /// Canon §7.3: exact percentages are contract data) and payment
    /// OBLIGATIONS on <see cref="ConstructionContract"/>; it never moves
    /// money. D4H implements this interface: it reads the contract's
    /// <see cref="ConstructionPaymentTerms"/> and
    /// <see cref="ConstructionPaymentObligation"/> records, bills deposits /
    /// milestone / progress / completion-balance amounts through the real
    /// ledgers, and records <see cref="ConstructionBillingEvent"/> entries.
    ///
    /// D4G defines the interface only — there is no implementation here.
    /// </summary>
    public interface IConstructionProgressBilling
    {
        /// <summary>The D4H implementation's service name, for diagnostics.</summary>
        string BillingServiceName { get; }

        /// <summary>
        /// Bills the contract's deposit through the real ledgers and records
        /// the billing event. Returns the refusal, or null on success.
        /// </summary>
        string RecordDeposit(ConstructionContract contract, int dayIndex, List<string> diagnostics);

        /// <summary>
        /// Bills one named milestone (see ConstructionPaymentTerms.Milestones).
        /// Returns the refusal, or null on success.
        /// </summary>
        string RecordMilestone(ConstructionContract contract, string milestoneId, int dayIndex, List<string> diagnostics);

        /// <summary>
        /// Bills the completion balance released at final acceptance.
        /// Returns the refusal, or null on success.
        /// </summary>
        string RecordCompletionBalance(ConstructionContract contract, int dayIndex, List<string> diagnostics);

        /// <summary>All billing events the D4H implementation has recorded for one contract.</summary>
        IReadOnlyList<ConstructionBillingEvent> EventsForContract(string contractId);

        /// <summary>
        /// Records that a payment obligation was settled in the ledgers
        /// outside this system. Recording only — the D4G book never moves
        /// money itself. Returns the refusal, or null on success.
        /// </summary>
        string MarkObligationSettled(ConstructionContract contract, string obligationId, string settledBy, int dayIndex, List<string> diagnostics);
    }
}
