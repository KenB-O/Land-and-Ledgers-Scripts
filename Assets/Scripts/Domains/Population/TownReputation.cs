using System;
using System.Collections.Generic;

namespace LandLedgers.Population
{
    /// <summary>
    /// Pass XL phase 3, item 5B.2: the single checkable town reputation /
    /// attractiveness read model. Before this pass, immigration conditions
    /// were scattered: housing, work, growth, and "people think the town
    /// is good" had no one value that conversation spread, traveler
    /// word-of-mouth, and immigration gating could all read, so each
    /// channel would have invented its own check.
    ///
    /// This model aggregates ONLY caller-supplied, checkable signals:
    /// housing availability (vacant / total), work availability
    /// (open / open+filled), town growth (caller-supplied 0-1 score, e.g.
    /// from population/business change over the last month), and resident
    /// good-opinion (caller-supplied 0-1 score, e.g. from conversation /
    /// satisfaction evidence). It does NOT invent an opinion model, and
    /// it does NOT compute growth from thin air: the production driver
    /// must supply those two scores from real registry state, or leave
    /// them at 0 and say so in diagnostics.
    ///
    /// Weights are caller-supplied, never canon. Tests supply weights
    /// (e.g. work 0.5, housing 0.3, growth 0.1, opinion 0.1); the model
    /// normalises weights that do not sum to 1 and diagnoses it, rather
    /// than silently scaling. Work is the biggest factor in Kennedy's
    /// design, but the weight that makes it so is a Kennedy decision,
    /// recorded in the final report, not a constant here.
    ///
    /// Staleness: a snapshot is recomputed on day/week change, NOT every
    /// tick. <see cref="TownReputationSnapshot.IsStale"/> reports
    /// staleness after a caller-supplied max age in days (default 7 in
    /// the helper, a test parameter, not canon). A stale snapshot must
    /// not gate immigration; the driver must recompute it first.
    /// Plain C#, no Unity dependency.
    /// </summary>
    public sealed class TownReputationInputs
    {
        public int HousingVacantUnits;
        public int HousingTotalUnits;
        public int OpenJobSlots;
        public int FilledJobSlots;
        public double GrowthScore01; // caller-supplied, 0-1
        public double GoodOpinionScore01; // caller-supplied, 0-1
    }

    public sealed class TownReputationWeights
    {
        public double Housing;
        public double Work;
        public double Growth;
        public double Opinion;
    }

    public sealed class TownReputationSnapshot
    {
        public double Score01;
        public double HousingComponent01;
        public double WorkComponent01;
        public double GrowthComponent01;
        public double OpinionComponent01;
        public int ComputedDayIndex;
        public List<string> Diagnostics = new List<string>();

        public bool IsStale(int currentDayIndex, int maxAgeDays)
        {
            if (maxAgeDays < 0) maxAgeDays = 0;
            return currentDayIndex - ComputedDayIndex > maxAgeDays;
        }
    }

    public static class TownReputationModel
    {
        public static TownReputationSnapshot Compute(
            TownReputationInputs inputs, TownReputationWeights weights, int dayIndex)
        {
            var snapshot = new TownReputationSnapshot { ComputedDayIndex = dayIndex };
            if (inputs == null || weights == null)
            {
                snapshot.Diagnostics.Add("TownReputation: null inputs or weights, score 0.");
                return snapshot;
            }

            snapshot.HousingComponent01 = inputs.HousingTotalUnits > 0
                ? Clamp01((double)Math.Max(0, inputs.HousingVacantUnits) / inputs.HousingTotalUnits) : 0;
            int totalJobs = Math.Max(0, inputs.OpenJobSlots) + Math.Max(0, inputs.FilledJobSlots);
            snapshot.WorkComponent01 = totalJobs > 0
                ? Clamp01((double)Math.Max(0, inputs.OpenJobSlots) / totalJobs) : 0;
            snapshot.GrowthComponent01 = Clamp01(inputs.GrowthScore01);
            snapshot.OpinionComponent01 = Clamp01(inputs.GoodOpinionScore01);

            double sum = weights.Housing + weights.Work + weights.Growth + weights.Opinion;
            if (sum <= 0)
            {
                snapshot.Diagnostics.Add("TownReputation: weights sum to 0, score 0.");
                return snapshot;
            }
            if (Math.Abs(sum - 1.0) > 0.0001)
                snapshot.Diagnostics.Add($"TownReputation: weights sum to {sum}, normalised (not canon).");

            snapshot.Score01 = Clamp01(
                (weights.Housing * snapshot.HousingComponent01
                 + weights.Work * snapshot.WorkComponent01
                 + weights.Growth * snapshot.GrowthComponent01
                 + weights.Opinion * snapshot.OpinionComponent01) / sum);
            snapshot.Diagnostics.Add(
                $"TownReputation: score {snapshot.Score01:F2} " +
                $"(housing {snapshot.HousingComponent01:F2}, work {snapshot.WorkComponent01:F2}, " +
                $"growth {snapshot.GrowthComponent01:F2}, opinion {snapshot.OpinionComponent01:F2}).");
            return snapshot;
        }

        private static double Clamp01(double value) => value < 0 ? 0 : value > 1 ? 1 : value;
    }

    /// <summary>
    /// Pass XL phase 3, item 3: immigration gating that READS the
    /// reputation model above, instead of duplicating its own condition
    /// checks. Thresholds are caller-supplied, never canon.
    /// </summary>
    public sealed class ImmigrationThresholds
    {
        public int MinOpenJobs;
        public int MinHousingVacancies;
        public double MinReputationScore01;
    }

    public sealed class ImmigrationEvaluation
    {
        public bool Eligible;
        public List<string> Reasons = new List<string>();
    }

    public static class ImmigrationConditions
    {
        public static ImmigrationEvaluation Evaluate(
            TownReputationSnapshot reputation, int housingVacancies, int openJobs,
            bool allowNewArrivals, ImmigrationThresholds thresholds)
        {
            var result = new ImmigrationEvaluation();
            if (thresholds == null)
            {
                result.Reasons.Add("Immigration: no thresholds supplied, not eligible.");
                return result;
            }
            if (!allowNewArrivals)
            {
                result.Reasons.Add("Immigration: six-person slice suppression is active (AllowNewArrivals false); no arrivals, regardless of conditions.");
                return result;
            }
            // Work is the biggest factor: it is checked first, and a
            // town with no work is never eligible, even with housing.
            if (openJobs < thresholds.MinOpenJobs)
                result.Reasons.Add($"Immigration: work fails — {openJobs} open jobs, need at least {thresholds.MinOpenJobs}. Work is the biggest factor.");
            if (housingVacancies < thresholds.MinHousingVacancies)
                result.Reasons.Add($"Immigration: housing fails — {housingVacancies} vacancies, need at least {thresholds.MinHousingVacancies}.");
            if (reputation == null)
                result.Reasons.Add("Immigration: no reputation snapshot supplied, reputation check fails.");
            else if (reputation.Score01 < thresholds.MinReputationScore01)
                result.Reasons.Add($"Immigration: reputation fails — score {reputation.Score01:F2}, need at least {thresholds.MinReputationScore01:F2}.");

            result.Eligible = result.Reasons.Count == 0;
            if (result.Eligible)
                result.Reasons.Add("Immigration: eligible — housing, work, and reputation thresholds all pass.");
            return result;
        }

        public static ImmigrationEvaluation EvaluateAtDay(
            TownReputationSnapshot reputation, int currentDayIndex, int maxReputationAgeDays,
            int housingVacancies, int openJobs, bool allowNewArrivals, ImmigrationThresholds thresholds)
        {
            if (reputation != null && reputation.IsStale(currentDayIndex, maxReputationAgeDays))
            {
                return new ImmigrationEvaluation
                {
                    Eligible = false,
                    Reasons = new List<string>
                    {
                        $"Immigration: reputation snapshot from day {reputation.ComputedDayIndex} is stale at day {currentDayIndex} (max age {maxReputationAgeDays} days); recompute before gating."
                    }
                };
            }
            return Evaluate(reputation, housingVacancies, openJobs, allowNewArrivals, thresholds);
        }
    }
}
