using System;

namespace LandLedgers.Skills
{
    /// <summary>
    /// Pass III foundation: causal task-outcome resolution contracts.
    /// Canon: Tech XIV Part II §2.4 (resolve duration, quality, waste, failure,
    /// rework, safety from causal inputs — NO hard gates, NO flat SkillMultiplier);
    /// XXXI §3.3 (competence changes execution: speed, quality, consistency, waste,
    /// failure, injury, rework — novice work is possible, not equally effective).
    ///
    /// Replaces the canon-violating flat rule in SkillTaskDurationEstimator
    /// ("base minutes minus 1 per skill level above 1"). The existing
    /// ITaskDurationEstimator seam on TaskAuthority is the plug-in point:
    /// Worker D swaps the estimator implementation, not the authority.
    /// </summary>

    [Serializable]
    public class CausalOutcomeInputs
    {
        /// <summary>Worker's present continuous capability at the task class, 0..1.</summary>
        public double Capability01;
        /// <summary>How relevant the worker's experience is to this task, 0..1.</summary>
        public double ExperienceRelevance01;
        /// <summary>Task difficulty, 0..1.</summary>
        public double Difficulty01;
        public bool Supervised;
        /// <summary>Equipment condition, 0..1 (1 = perfect).</summary>
        public double EquipmentCondition01 = 1.0;
        /// <summary>Worker fatigue, 0..1 (0 = fresh).</summary>
        public double Fatigue01;
        /// <summary>Time pressure, 0..1.</summary>
        public double TimePressure01;
        /// <summary>Instruction/briefing quality, 0..1.</summary>
        public double InstructionQuality01 = 0.5;
    }

    [Serializable]
    public class CausalOutcomeResult
    {
        public double DurationMinutes;
        /// <summary>-1 (ruined) .. +1 (exceptional); 0 = ordinary acceptable.</summary>
        public double QualityDelta;
        /// <summary>Material waste multiplier, >= 1.0.</summary>
        public double WasteFactor = 1.0;
        /// <summary>Probability of task failure requiring rework or loss, 0..1.</summary>
        public double FailureRisk01;
        public double ReworkMinutes;
    }

    public interface ITaskOutcomeResolver
    {
        CausalOutcomeResult Resolve(CausalOutcomeInputs inputs);
    }
}
