// PersonJobDecisions — Phase D: person employment decisions (brief section 7).
//
// Canon: CAN-CE1-03.3 (persons evaluate work against actual lives), CAN-CE1-03.4
// (negotiation: accept/refuse/counter/delay), CAN-CE1-13.1 (worker consent;
// proposal 107 owner-assignment-vs-consent stays PENDING, so refusal is real).
// All decisions use ONLY legitimately-known information carried in
// JobDecisionInput (visible profile, contact graph, public notices) — never
// hidden traits. The integrator maps PersonState.laborUrgency01 and
// HouseholdLedger balances onto JobDecisionInput; this class never reads them.
//
// Lifecycle paths modeled as decisions (brief section 7: workers can seek,
// negotiate, start, perform, change employers, resign, be dismissed, pause):
//   Change employers -> Accept whose Reason names the move.
//   Resign            -> Refuse of continued current terms (caller passes an
//                        offer reflecting the current job; Refuse = walks away).
//   Dismissed         -> employer-side; representable as Refuse with a
//                        "dismissed by employer" reason (not produced here).
//   Pause             -> Delay with a "paused: ..." reason (not produced here).
using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Labor
{
    /// <summary>
    /// Static evaluators: a person decides about a job offer, and about whether
    /// to work on a given day. Pure logic over caller-supplied DTOs; no domain
    /// service access, no randomness. Deterministic.
    /// </summary>
    public static class PersonJobDecisions
    {
        // ---- Thresholds (documented, deterministic) ----
        private const int UrgentReservesLowCents = 200;   // P1-style urgency band
        private const int AdequateReservesCents = 2000;   // cushion counts as "adequate resources"
        private const int StarvationWageCentsPerWeek = 100; // below this is never "reasonable"
        private const float HazardousConditions = 0.15f;  // refused even in need (CAN-CE1-03.5 safety)
        private const float PoorConditions = 0.35f;       // blocker when resources are adequate
        private const int LongCommuteMinutes = 90;        // blocker when resources are adequate
        private const float LowReliability = 0.3f;        // blocker when resources are adequate

        /// <summary>
        /// Evaluates one employment offer against the person's actual life
        /// (brief section 7; CAN-CE1-03.3/03.4). Uses ONLY the input fields:
        /// household reserves, trend, dependents, current employment, offer
        /// terms, commute, conditions, employer reliability, housing,
        /// known alternatives. Never invents employer knowledge.
        /// </summary>
        public static JobDecision EvaluateOffer(JobDecisionInput input, List<string> diag)
        {
            if (input == null)
            {
                if (diag != null) diag.Add("EvaluateOffer: null input -> Refuse (no information)");
                return new JobDecision { Kind = JobDecisionKind.Refuse, Reason = "no offer information" };
            }

            int offerWage = input.OfferWageCentsPerWeek;
            if (offerWage <= 0)
            {
                if (diag != null) diag.Add(string.Format("P{0}: Refuse - no wage offered", input.PersonId));
                return new JobDecision { Kind = JobDecisionKind.Refuse, Reason = "no wage offered" };
            }

            // Unknown employer reliability -> caution, never a blind accept (brief section 7).
            // Urgency does not override this: the person cannot verify the offer.
            if (input.EmployerReliability01 <= 0f)
            {
                if (diag != null) diag.Add(string.Format("P{0}: Delay - employer reliability unknown; waiting for verifiable information", input.PersonId));
                return new JobDecision
                {
                    Kind = JobDecisionKind.Delay,
                    Reason = "employer reliability unknown; will not accept blind"
                };
            }

            bool urgent = !input.CurrentlyEmployed
                          && input.Dependents > 0
                          && input.ReservesTrendCentsPerDay < 0
                          && input.ReservesCents <= UrgentReservesLowCents;

            bool workable = offerWage >= StarvationWageCentsPerWeek
                            && input.ConditionsScore01 >= HazardousConditions;

            if (urgent)
            {
                // Unemployed + declining reserves + dependents -> urgency accepts
                // reasonable offers (brief section 7). Danger/starvation terms are
                // still refused: refusal stays meaningful (CAN-CE1-03.5).
                if (workable)
                {
                    if (diag != null) diag.Add(string.Format("P{0}: Accept - urgency (unemployed, {1} dependents, reserves {2}c declining); offer workable",
                        input.PersonId, input.Dependents, input.ReservesCents));
                    return new JobDecision
                    {
                        Kind = JobDecisionKind.Accept,
                        Reason = "urgency: unemployed with dependents, reserves declining; offer workable"
                    };
                }
                if (diag != null) diag.Add(string.Format("P{0}: Refuse - even in need, terms are dangerous or below starvation wage", input.PersonId));
                return new JobDecision
                {
                    Kind = JobDecisionKind.Refuse,
                    Reason = "refused even in need: dangerous conditions or starvation wage"
                };
            }

            // Non-urgent evaluation: blockers are commute/risk/conditions/reliability.
            var blockers = new List<string>();
            if (input.ConditionsScore01 < PoorConditions)
                blockers.Add("poor conditions");
            bool longCommute = input.CommuteMinutesOneWay > LongCommuteMinutes && !input.HousingOffered;
            if (longCommute)
                blockers.Add("long commute");
            if (input.EmployerReliability01 < LowReliability)
                blockers.Add("low employer reliability");

            bool adequate = input.CurrentlyEmployed || input.ReservesCents >= AdequateReservesCents;

            // Adequate resources + any blocker -> Refuse. Wage alone never wins:
            // no auto-accept of the highest wage (brief section 7).
            if (adequate && blockers.Count > 0)
            {
                string reason = "refused: " + string.Join(", ", blockers.ToArray());
                if (diag != null) diag.Add(string.Format("P{0}: Refuse - adequate resources; {1}", input.PersonId, string.Join(", ", blockers.ToArray())));
                return new JobDecision { Kind = JobDecisionKind.Refuse, Reason = reason };
            }

            if (input.CurrentlyEmployed)
            {
                int current = Math.Max(input.CurrentWageCentsPerWeek, 1);
                int improveBar = (current * 110) / 100; // 10% better = clearly better

                if (blockers.Count == 0)
                {
                    if (offerWage >= improveBar)
                    {
                        if (diag != null) diag.Add(string.Format("P{0}: Accept - job change: {1}c/wk improves on current {2}c/wk with acceptable terms",
                            input.PersonId, offerWage, input.CurrentWageCentsPerWeek));
                        return new JobDecision
                        {
                            Kind = JobDecisionKind.Accept,
                            Reason = "job change: better wage with acceptable commute, conditions and employer"
                        };
                    }
                    // Wage is the only blocker -> counter (brief section 6/7).
                    int counterWage = Math.Max(input.CurrentWageCentsPerWeek, (offerWage * 110) / 100);
                    if (diag != null) diag.Add(string.Format("P{0}: Counter - wage is the only blocker; proposing {1}c/wk", input.PersonId, counterWage));
                    return new JobDecision
                    {
                        Kind = JobDecisionKind.Counter,
                        CounterWageCentsPerWeek = counterWage,
                        Reason = "wage is the only blocker; counter at current pay or 10% above offer"
                    };
                }

                // Employed with blockers: wage cannot buy away commute/risk/family.
                // Known alternatives make refusal easier (they outweigh pay).
                string refuseReason = "refused: " + string.Join(", ", blockers.ToArray());
                if (input.KnownAlternativeCount > 0)
                    refuseReason += "; has " + input.KnownAlternativeCount + " known alternative(s)";
                if (diag != null) diag.Add(string.Format("P{0}: Refuse - {1}", input.PersonId, refuseReason));
                return new JobDecision { Kind = JobDecisionKind.Refuse, Reason = refuseReason };
            }

            // Unemployed but not urgent (reserves hold, or no dependents, or not
            // declining): needs work, can still be selective.
            if (blockers.Count > 0)
            {
                string reason = "refused: " + string.Join(", ", blockers.ToArray());
                if (diag != null) diag.Add(string.Format("P{0}: Refuse - not urgent but terms poor; {1}", input.PersonId, string.Join(", ", blockers.ToArray())));
                return new JobDecision { Kind = JobDecisionKind.Refuse, Reason = reason };
            }
            if (workable)
            {
                if (diag != null) diag.Add(string.Format("P{0}: Accept - unemployed; offer workable ({1}c/wk)", input.PersonId, offerWage));
                return new JobDecision
                {
                    Kind = JobDecisionKind.Accept,
                    Reason = "unemployed; offer workable"
                };
            }
            // Wage alone is the problem -> counter at a modest uplift, unless
            // known alternatives make it not worth the bother.
            if (input.KnownAlternativeCount >= 2)
            {
                if (diag != null) diag.Add(string.Format("P{0}: Refuse - wage too low and alternatives known", input.PersonId));
                return new JobDecision { Kind = JobDecisionKind.Refuse, Reason = "wage too low; pursuing known alternatives" };
            }
            int ask = (offerWage * 120) / 100;
            if (diag != null) diag.Add(string.Format("P{0}: Counter - wage only blocker; proposing {1}c/wk", input.PersonId, ask));
            return new JobDecision
            {
                Kind = JobDecisionKind.Counter,
                CounterWageCentsPerWeek = ask,
                Reason = "wage is the only blocker; counter at 20% above offer"
            };
        }

        /// <summary>
        /// Whether the person will work today. Refusal, fatigue, danger and
        /// contract restrictions are meaningful (brief section 8; CAN-CE1-03.5):
        /// any of them returns false with the reason appended to diag.
        /// </summary>
        /// <param name="personId">Person being asked.</param>
        /// <param name="fatigued">Person-time authority reports fatigue.</param>
        /// <param name="dangerFlag">Non-empty when the task/workplace is dangerous
        /// (null or empty = no known danger).</param>
        /// <param name="contractRestrictions">Non-empty when standing contract
        /// restrictions apply (e.g. "no night work"); null or empty = none.
        /// Without time-of-day context the restriction is honored as a refusal.</param>
        public static bool WillWorkToday(int personId, bool fatigued, string dangerFlag,
            string contractRestrictions, List<string> diag)
        {
            if (fatigued)
            {
                if (diag != null) diag.Add(string.Format("P{0} refuses: fatigued", personId));
                return false;
            }
            if (!string.IsNullOrEmpty(dangerFlag))
            {
                if (diag != null) diag.Add(string.Format("P{0} refuses: danger - {1}", personId, dangerFlag));
                return false;
            }
            if (!string.IsNullOrEmpty(contractRestrictions))
            {
                if (diag != null) diag.Add(string.Format("P{0} refuses: contract restriction - {1}", personId, contractRestrictions));
                return false;
            }
            if (diag != null) diag.Add(string.Format("P{0} willing to work", personId));
            return true;
        }
    }
}
