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
    }

    public enum HouseholdDecisionKind
    {
        NoAction = 0,
        SeekIncome = 1,
        Economize = 2,
        Borrow = 3,
        RelocateCheaper = 4,
        NegotiateTerms = 5,
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
