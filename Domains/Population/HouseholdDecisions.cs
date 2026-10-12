using System;

namespace LandLedgers.Population
{
    /// <summary>
    /// Pass III foundation: household financial pressure -> decision contracts.
    /// Canon: XXXI Parts I/V; the central design principle — a Person seeks income
    /// because of real circumstances (food, rent due, savings, dependents), never
    /// because the simulation needs another worker, and never via a universal
    /// highest-wage rule or arbitrary job-seeking timer.
    ///
    /// Follows the proven JobDecisionInput pattern from Pass II (pressure in,
    /// decision out, real operations only). Decisions here must result in real
    /// offers, tasks, agreements, financial events, or physical state changes —
    /// a decision record alone is not completed economic activity.
    /// </summary>

    [Serializable]
    public class HouseholdPressureInput
    {
        public string HouseholdId;
        public int Day;
        public long ReservesCents;
        /// <summary>Rent/board + debt service + other obligations due in the next period, cents.</summary>
        public long ObligationsDueCents;
        public long ExpectedIncomeCentsPerDay;
        public int Dependents;
        public int KnownOpportunityCount;
        public bool HasShelterRisk;
        /// <summary>
        /// Pass III Worker C (additive): legitimate available credit the household
        /// can draw, in cents. 0 = none known. Never invented by the evaluator.
        /// </summary>
        public long AvailableCreditCents;
        /// <summary>
        /// Pass III Worker C (additive): a legitimate known lender
        /// (person/business id) the household could borrow from. Empty = none
        /// known; the decision driver will not invent a counterparty.
        /// </summary>
        public string KnownCreditorId = string.Empty;
        /// <summary>
        /// Pass III Worker C (additive): a household member's employment ended
        /// recently. A real circumstance the evaluator weighs — not a timer.
        /// </summary>
        public bool RecentJobLoss;
        /// <summary>Pass III Worker C (additive): which employment ended, e.g.
        /// "P3 dismissed from store-1". Empty when unknown.</summary>
        public string JobLossNote = string.Empty;
        /// <summary>
        /// Pass III Worker C (additive): any household member has documented
        /// contact links worth searching for work (contact graph, one-hop).
        /// Searching is itself the legitimate action — the household need not
        /// already know of an opening to look through its own contacts.
        /// </summary>
        public bool HasSearchableContacts;
        /// <summary>
        /// Pass V P2-2 (additive): distress value (cents) of the household's
        /// saleable surplus non-essential goods, from HouseholdAssetSaleService.
        /// Zero when there is nothing saleable or the service is unwired.
        /// </summary>
        public int SaleableSurplusValueCents;
    }

    public enum HouseholdDecisionKind
    {
        NoAction = 0,
        SeekIncome = 1,
        Economize = 2,
        Borrow = 3,
        RelocateCheaper = 4,
        NegotiateTerms = 5,
        /// <summary>
        /// Pass V P2-2 (append-only): sell surplus non-essential goods to real
        /// buyer households before relocating. The sale is a real operation
        /// (goods and cash move); when it cannot cover the shortfall the
        /// driver falls back to economizing for the remainder.
        /// </summary>
        SellAssets = 6,
    }

    [Serializable]
    public class HouseholdDecision
    {
        public string HouseholdId;
        public int Day;
        public HouseholdDecisionKind Kind = HouseholdDecisionKind.NoAction;
        public string Reason;
    }

    public interface IHouseholdDecisionService
    {
        HouseholdDecision Evaluate(HouseholdPressureInput input);
    }
}
