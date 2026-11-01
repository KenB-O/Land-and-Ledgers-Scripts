// Worker D: Autonomous Labor, Phase G - bounded employment migration.
// Connects distant job opportunities to housing/household systems WITHOUT a
// demographic engine: no auto-spawn (a job posting never creates a worker, a
// vacant house never creates a resident), no auto-awareness (a person only
// considers a distant job they legitimately learned about through the contact
// graph), no movement execution here (the integrator maps accepted intents to
// journey tasks). Evaluation only - the decision factors are documented on
// the record.
using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Labor
{
    /// <summary>
    /// Everything a person legitimately knows when weighing a distant job.
    /// Callers fill ONLY legitimately-known values: travel estimates come from
    /// the journey model, housing fit from the existing housing search, moving
    /// costs from the household's circumstances - never hidden world state.
    /// </summary>
    [Serializable]
    public sealed class MigrationEvaluationInput
    {
        public int PersonId = -1;
        public int HouseholdId = -1;

        /// <summary>
        /// True only when the person learned of the job through a documented
        /// contact (contact graph, letter, referral). False = no auto-awareness.
        /// </summary>
        public bool ContactAware;

        public int TravelMinutesOneWay;
        public int TravelCostCents;

        /// <summary>Housing suitability at the destination (existing housing search).</summary>
        public bool HousingFit;

        public int MovingCostCents;
        public int OfferWageCentsPerWeek;
        public int CurrentWageCentsPerWeek;
        public int HouseholdReservesCents;
        public int Dependents;
        public string HouseholdCircumstances = string.Empty; // free-text note, recorded as a factor
        public int DayIndex = -1;
    }

    /// <summary>
    /// The documented decision. When Proceed is true, TravelTaskRequested is an
    /// INTENT the integrator maps to journey tasks - this linker moves nobody.
    /// </summary>
    [Serializable]
    public sealed class MigrationEvaluation
    {
        public int PersonId = -1;
        public bool Proceed;
        public bool Delay;
        public string Decision = string.Empty;
        public List<string> Factors = new List<string>();
        public string RefusalReason = string.Empty;
        public bool TravelTaskRequested;
        public string Notes = string.Empty;
        public int DayIndex = -1;
    }

    /// <summary>
    /// BOUNDED distant-job evaluation. Stateless: evaluates one person's
    /// decision from documented factors and returns it. Creates nothing.
    /// </summary>
    public sealed class LaborMigrationLinker
    {
        /// <summary>
        /// Half a year of weekly wage gain must cover the move, or the move is
        /// not worth it (documented payback rule, not a hidden constant the
        /// caller cannot see - the weeks are named in the factor text).
        /// </summary>
        public const int PaybackWeeks = 26;

        /// <summary>
        /// Beyond this one-way travel time the job is not commutable, so the
        /// household must relocate (housing fit becomes decisive).
        /// </summary>
        public const int CommutableMinutesOneWay = 90;

        public MigrationEvaluation EvaluateDistantJob(MigrationEvaluationInput input, List<string> diag)
        {
            diag = diag ?? new List<string>();
            var evaluation = new MigrationEvaluation
            {
                DayIndex = input != null ? input.DayIndex : -1,
                PersonId = input != null ? input.PersonId : -1
            };

            if (input == null)
            {
                evaluation.Decision = "refused";
                evaluation.RefusalReason = "no evaluation input - nothing decided, nobody moved";
                diag.Add("LaborMigrationLinker: null input refused.");
                return evaluation;
            }

            // Rule 1: no auto-awareness. A person who never legitimately heard
            // of the job cannot consider it - the linker invents no knowledge.
            if (!input.ContactAware)
            {
                evaluation.Decision = "refused";
                evaluation.RefusalReason = "person has no documented contact awareness of this job - no auto-awareness";
                evaluation.Factors.Add("contactAware=false: the job was never legitimately learned about");
                diag.Add($"LaborMigrationLinker: P{input.PersonId} refused - no contact awareness (no auto-awareness).");
                return evaluation;
            }

            evaluation.Factors.Add("contactAware=true: job learned through a documented contact");

            bool mustRelocate = input.TravelMinutesOneWay > CommutableMinutesOneWay;
            evaluation.Factors.Add(mustRelocate
                ? $"travel {input.TravelMinutesOneWay}min one-way exceeds the {CommutableMinutesOneWay}min commutable bound - relocation required"
                : $"travel {input.TravelMinutesOneWay}min one-way is commutable - no relocation required");

            int weeklyGain = input.OfferWageCentsPerWeek - input.CurrentWageCentsPerWeek;
            int moveCost = Math.Max(0, input.MovingCostCents) + Math.Max(0, input.TravelCostCents);
            evaluation.Factors.Add($"wage {input.CurrentWageCentsPerWeek}c -> {input.OfferWageCentsPerWeek}c per week (gain {weeklyGain}c); " +
                                   $"move cost {moveCost}c (moving {input.MovingCostCents}c + travel {input.TravelCostCents}c)");

            if (!string.IsNullOrWhiteSpace(input.HouseholdCircumstances))
            {
                evaluation.Factors.Add($"household circumstances: {input.HouseholdCircumstances.Trim()}");
            }

            // Rule 2: relocation without fitting housing is refused for
            // dependents, delayed otherwise - a vacant house never
            // auto-generates a resident, and a family is not moved into unfit
            // housing.
            if (mustRelocate && !input.HousingFit)
            {
                if (input.Dependents > 0)
                {
                    evaluation.Decision = "refused";
                    evaluation.RefusalReason = $"no fitting housing at destination for a household with {input.Dependents} dependents - family not moved into unfit housing";
                    evaluation.Factors.Add("housingFit=false with dependents: refused (no auto-generated resident)");
                    diag.Add($"LaborMigrationLinker: P{input.PersonId} refused - housing unfit for dependents.");
                    return evaluation;
                }

                evaluation.Decision = "delayed";
                evaluation.Delay = true;
                evaluation.RefusalReason = "no fitting housing at destination yet - delayed until housing is found, not forced";
                evaluation.Factors.Add("housingFit=false, no dependents: delayed (housing search continues; nobody auto-spawned)");
                diag.Add($"LaborMigrationLinker: P{input.PersonId} delayed - housing unfit, no dependents.");
                return evaluation;
            }

            // Rule 3: the wage gain must pay back the move within the payback
            // window, and the household must be able to front the cost.
            long paybackCents = (long)weeklyGain * PaybackWeeks;
            if (paybackCents < moveCost)
            {
                evaluation.Decision = "refused";
                evaluation.RefusalReason = $"wage gain ({weeklyGain}c/week x {PaybackWeeks} weeks = {paybackCents}c) does not cover the {moveCost}c move cost";
                evaluation.Factors.Add($"payback fails: {paybackCents}c < {moveCost}c over {PaybackWeeks} weeks");
                diag.Add($"LaborMigrationLinker: P{input.PersonId} refused - move does not pay back.");
                return evaluation;
            }

            evaluation.Factors.Add($"payback holds: {paybackCents}c >= {moveCost}c over {PaybackWeeks} weeks");

            if (mustRelocate && moveCost > Math.Max(0, input.HouseholdReservesCents))
            {
                evaluation.Decision = "delayed";
                evaluation.Delay = true;
                evaluation.RefusalReason = $"household reserves ({input.HouseholdReservesCents}c) cannot front the {moveCost}c move - delayed until affordable";
                evaluation.Factors.Add("reserves insufficient to front the move: delayed, not debt-financed by fiat");
                diag.Add($"LaborMigrationLinker: P{input.PersonId} delayed - cannot front move cost.");
                return evaluation;
            }

            evaluation.Decision = "proceed";
            evaluation.Proceed = true;
            evaluation.TravelTaskRequested = mustRelocate;
            evaluation.Notes = mustRelocate
                ? "intent: integrator maps to journey tasks; housing secured via the existing housing search before travel"
                : "commutable: no relocation, no journey tasks needed";
            evaluation.Factors.Add(evaluation.Notes);
            diag.Add($"LaborMigrationLinker: P{input.PersonId} proceeds - {evaluation.Notes}.");
            return evaluation;
        }
    }
}
