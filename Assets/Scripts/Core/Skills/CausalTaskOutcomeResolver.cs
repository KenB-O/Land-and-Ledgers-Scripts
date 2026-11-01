using EntityId = LandLedgers.Primitives.EntityId;

using System;
using LandLedgers.Primitives;
using LandLedgers.Tasks;

namespace LandLedgers.Skills
{
    /// <summary>
    /// Pass III Worker D: causal task-outcome resolution.
    ///
    /// Canon: Tech XIV Part II §2.4 (resolve duration, quality, waste, failure,
    /// rework, safety from causal inputs — NO hard gates, NO flat SkillMultiplier);
    /// XXXI §3.3 (competence changes execution: speed, quality, consistency,
    /// waste, failure, injury, rework — novice work is possible, not equally
    /// effective); XXXI §3.1 CANON LOCK (no hard skill gates on ordinary
    /// production).
    ///
    /// Every output is a deterministic function of the inputs. There is no
    /// hidden RNG state: the outcome "roll" derives from a stable hash of
    /// (task, worker, day), so repeated runs and save/load restores agree.
    /// </summary>
    public sealed class CausalTaskOutcomeResolver : ITaskOutcomeResolver
    {
        public CausalOutcomeResult Resolve(CausalOutcomeInputs inputs)
        {
            if (inputs == null)
            {
                return new CausalOutcomeResult { DurationMinutes = 1 };
            }

            double capability = Clamp01(inputs.Capability01);
            double relevance = Clamp01(inputs.ExperienceRelevance01);
            double difficulty = Clamp01(inputs.Difficulty01);
            double equipment = Clamp01(inputs.EquipmentCondition01);
            double fatigue = Clamp01(inputs.Fatigue01);
            double pressure = Clamp01(inputs.TimePressure01);
            double instruction = Clamp01(inputs.InstructionQuality01);
            double baseMinutes = Math.Max(1.0, inputs.BaseMinutes);

            // Effective competence blends present capability with relevant
            // experience (Rev XVII §3: skill and experience are distinct).
            double competence = 0.6 * capability + 0.4 * relevance;
            double gap = difficulty - competence; // >0 overmatched, <0 undermatched

            // Duration: overmatched workers take longer; practiced workers are
            // quicker but never below half the base (no free work). Fatigue and
            // poor equipment slow the work; hurrying trades time for quality.
            double durationFactor = 1.0
                + 0.8 * Math.Max(0.0, gap)
                - 0.3 * Math.Max(0.0, -gap)
                + 0.5 * fatigue
                - 0.2 * pressure
                + 0.5 * (1.0 - equipment);
            durationFactor = Clamp(durationFactor, 0.5, 3.0);

            // Quality: -1 (ruined) .. +1 (exceptional); 0 = ordinary acceptable.
            double quality = 0.5 * (competence - difficulty)
                + 0.15 * (inputs.Supervised ? 1.0 : 0.0)
                - 0.3 * fatigue
                - 0.25 * pressure
                + 0.2 * (instruction - 0.5);
            quality = Clamp(quality, -1.0, 1.0);

            // Waste: overmatched or tired workers spoil more material.
            double waste = 1.0
                + 0.6 * Math.Max(0.0, gap)
                + 0.2 * fatigue
                + 0.1 * (1.0 - equipment);

            // Failure risk: supervision and undermatched ease cut it; fatigue,
            // pressure and overmatch raise it. Never certain, never zero.
            double failure = 0.03
                + 0.7 * Math.Max(0.0, gap)
                + 0.15 * fatigue
                + 0.12 * pressure
                - 0.25 * (inputs.Supervised ? 1.0 : 0.0)
                - 0.1 * Math.Max(0.0, -gap);
            failure = Clamp(failure, 0.0, 0.95);

            return new CausalOutcomeResult
            {
                DurationMinutes = baseMinutes * durationFactor,
                QualityDelta = quality,
                WasteFactor = Math.Max(1.0, waste),
                FailureRisk01 = failure,
                ReworkMinutes = failure * baseMinutes * 0.5,
            };
        }

        private static double Clamp01(double value)
        {
            return Math.Max(0.0, Math.Min(1.0, value));
        }

        private static double Clamp(double value, double min, double max)
        {
            return Math.Max(min, Math.Min(max, value));
        }
    }

    /// <summary>
    /// Deterministic TaskOutcome selection from a resolved causal outcome.
    /// The "roll" is a stable FNV-1a hash of (task, worker, day) — no RNG
    /// state, so the same inputs always yield the same outcome.
    /// Canon: XXXI §3.3 (novice work varies: AcceptableWithVariation and
    /// RecoverableFailure are real paths, not just Success).
    /// </summary>
    public static class TaskOutcomeDetermination
    {
        /// <summary>
        /// Maps a resolved outcome to the TaskAuthority's outcome enum.
        /// Novices fail or vary; experts reliably succeed.
        /// </summary>
        public static TaskOutcome DetermineOutcome(
            CausalOutcomeResult result, string taskId, string workerId, int day)
        {
            if (result == null)
            {
                return TaskOutcome.Success;
            }

            double roll = DeterministicRoll(
                (taskId ?? string.Empty) + "|" + (workerId ?? string.Empty) + "|" + day);

            if (roll < result.FailureRisk01 * 0.25)
            {
                return TaskOutcome.SeriousFailure;
            }

            if (roll < result.FailureRisk01)
            {
                return TaskOutcome.RecoverableFailure;
            }

            if (result.QualityDelta < -0.35)
            {
                return TaskOutcome.AcceptableWithVariation;
            }

            return TaskOutcome.Success;
        }

        /// <summary>Deterministic pseudo-roll in [0,1) from a stable string hash.</summary>
        public static double DeterministicRoll(string seed)
        {
            unchecked
            {
                const uint fnvOffset = 2166136261;
                const uint fnvPrime = 16777619;
                uint hash = fnvOffset;
                foreach (char c in seed ?? string.Empty)
                {
                    hash ^= c;
                    hash *= fnvPrime;
                }

                return (hash % 1000000) / 1000000.0;
            }
        }
    }

    /// <summary>
    /// Pass III Worker D: the <see cref="ITaskDurationEstimator"/> that replaces
    /// <c>SkillTaskDurationEstimator</c>'s canon-violating flat rule
    /// ("base minutes minus 1 per skill level above 1" — the forbidden flat
    /// SkillMultiplier, Tech II §2.4).
    ///
    /// Assignment-time estimates use the causal resolver with neutral context
    /// (difficulty 0.5, fresh, unpressured, decent equipment): the worker's real
    /// capability still moves the estimate, but unknown day-of conditions do
    /// not. Install via <see cref="TaskAuthority.SetDurationEstimator"/> — the
    /// authority itself is untouched.
    /// </summary>
    public sealed class CausalTaskDurationEstimator : ITaskDurationEstimator
    {
        private readonly SkillService skillService;
        private readonly ITaskOutcomeResolver resolver;

        public CausalTaskDurationEstimator(SkillService skillService, ITaskOutcomeResolver resolver = null)
        {
            this.skillService = skillService ?? throw new ArgumentNullException(nameof(skillService));
            this.resolver = resolver ?? new CausalTaskOutcomeResolver();
        }

        public int EstimateMinutes(TaskDefinition definition, EntityId workerId)
        {
            if (definition == null)
            {
                return 1;
            }

            return EstimateMinutesForBase(definition.BaseMinutes, definition.RequiredSkillId, workerId);
        }

        public int EstimateMinutesForBase(int baseMinutes, string skillId, EntityId workerId)
        {
            if (string.IsNullOrEmpty(skillId))
            {
                return Math.Max(1, baseMinutes);
            }

            CausalOutcomeInputs inputs = skillService.BuildCausalInputs(workerId, skillId, baseMinutes);
            CausalOutcomeResult result = resolver.Resolve(inputs);
            return Math.Max(1, (int)Math.Round(result.DurationMinutes));
        }
    }

    /// <summary>
    /// Compatibility adapter for older trade-runtime tests and authored
    /// fixtures. Production scheduling installs <see cref="CausalTaskDurationEstimator" />;
    /// this type preserves the old call surface without becoming a second
    /// capability authority.
    /// </summary>
    [Obsolete("Use CausalTaskDurationEstimator for new production scheduling.")]
    public sealed class SkillTaskDurationEstimator : ITaskDurationEstimator
    {
        private readonly SkillService skillService;

        public SkillTaskDurationEstimator(SkillService skillService)
        {
            this.skillService = skillService ?? throw new ArgumentNullException(nameof(skillService));
        }

        public int EstimateMinutes(TaskDefinition definition, EntityId workerId)
        {
            if (definition == null)
            {
                return 1;
            }

            return EstimateMinutesForBase(definition.BaseMinutes, definition.RequiredSkillId, workerId);
        }

        public int EstimateMinutesForBase(int baseMinutes, string skillId, EntityId workerId)
        {
            if (string.IsNullOrEmpty(skillId))
            {
                return Math.Max(1, baseMinutes);
            }

            int level = skillService.GetLevel(workerId, skillId);
            return Math.Max(1, baseMinutes - Math.Max(0, level - 1));
        }
    }
}
