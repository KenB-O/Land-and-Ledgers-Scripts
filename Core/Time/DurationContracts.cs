using System;
using UnityEngine;

namespace LandLedgers.Time
{
    /// <summary>
    /// Pass IV (Living World Foundation): duration conventions.
    ///
    /// Game seconds are the precise internal unit for task and process durations.
    /// Whole minutes remain the TTS-1 booking quantum for WorkTimeBudgetStore and
    /// PersonScheduleTracker — budgets and schedule windows are still booked in minutes.
    ///
    /// There is NO universal one-minute minimum for meaningful actions that take less
    /// time; sub-minute work is representable in seconds and rounds UP to 1 minute
    /// only at the booking boundary (WorkTimeMath.WholeMinutesUp).
    ///
    /// There is NO universal DurationMultiplier. Estimators compose task-method-specific
    /// factors (see TaskDurationInputs). Unknown rates are explicit configurable
    /// estimates, never falsely precise hard-coded constants.
    ///
    /// Canon: XXXI Part III §§3.1-3.7. Historical calibration references live in the
    /// docs package (ECON-REF, WORK-REF) — use them as dated, method-specific references,
    /// never as universal rates.
    /// </summary>
    public static class DurationConventions
    {
        /// <summary>Game seconds per work minute (matches WorkTimeMath.SecondsPerMinute).</summary>
        public const int SecondsPerMinute = 60;

        /// <summary>
        /// Converts a precise game-seconds duration to the whole-minute booking quantum,
        /// rounding up so partial minutes still consume budget. Floored at 1.
        /// </summary>
        public static int BookingMinutes(float gameSeconds)
        {
            return WorkTimeMath.WholeMinutesUp(gameSeconds);
        }

        /// <summary>Converts whole booking minutes back to game seconds for clock comparison.</summary>
        public static float BookingMinutesToGameSeconds(int minutes)
        {
            return WorkTimeMath.MinutesToGameSeconds(minutes);
        }
    }

    /// <summary>
    /// Pass IV: physical and operational factors for a duration estimate. Estimators
    /// use the factors relevant to their task method; irrelevant factors stay at
    /// their neutral defaults. All factors are plain data — no Unity dependencies.
    /// </summary>
    [Serializable]
    public sealed class TaskDurationInputs
    {
        /// <summary>Task definition id being estimated (for method lookup).</summary>
        public string DefinitionId = string.Empty;

        /// <summary>Work quantity in the method's natural unit (items, board-feet, acres...).</summary>
        public float WorkQuantity = 1f;

        /// <summary>Number of discrete items handled.</summary>
        public int ItemCount = 1;

        /// <summary>Mass per item in kilograms (0 = not mass-relevant).</summary>
        public float MassKgPerItem;

        /// <summary>Bulk/volume factor 0..1 (0 = compact, 1 = very bulky/awkward).</summary>
        public float Bulk01;

        /// <summary>Carrying/handling distance in meters (0 = in place).</summary>
        public float DistanceMeters;

        /// <summary>Access difficulty 0..1 (doors, stairs, terrain, weather).</summary>
        public float AccessDifficulty01;

        /// <summary>Worker present capability 0..1 for the relevant family (Pass III).</summary>
        public float WorkerCapability01 = 0.5f;

        /// <summary>Worker fatigue 0..1 (0 = fresh).</summary>
        public float WorkerFatigue01;

        /// <summary>Equipment/method id used (empty = hand/no equipment).</summary>
        public string EquipmentMethodId = string.Empty;

        /// <summary>Number of workers on this task instance.</summary>
        public int CrewSize = 1;

        /// <summary>Workspace congestion 0..1 (0 = clear).</summary>
        public float Congestion01;

        /// <summary>Setup and teardown time in game seconds.</summary>
        public float SetupTeardownGameSeconds;

        /// <summary>Required precision 0..1 (0 = rough, 1 = fine).</summary>
        public float Precision01;

        /// <summary>Supervision quality 0..1 (0 = unsupervised).</summary>
        public float Supervision01;

        /// <summary>Environmental factor 0..1 (1 = ideal conditions).</summary>
        public float Environment01 = 1f;

        /// <summary>Material condition 0..1 (1 = ideal).</summary>
        public float MaterialCondition01 = 1f;
    }

    /// <summary>Pass IV: a duration estimate. GameSeconds is precise; WholeMinutes is the booking quantum.</summary>
    [Serializable]
    public struct DurationEstimate
    {
        public float GameSeconds;
        public int WholeMinutes;
        public string Basis;

        public static DurationEstimate FromGameSeconds(float gameSeconds, string basis)
        {
            float clamped = Math.Max(0f, gameSeconds);
            return new DurationEstimate
            {
                GameSeconds = clamped,
                WholeMinutes = DurationConventions.BookingMinutes(clamped),
                Basis = basis ?? string.Empty,
            };
        }
    }

    /// <summary>
    /// Pass IV: composable duration estimator. Implementations estimate ONE task method
    /// (or family) from its physical factors. The universal estimator (Worker 1) selects
    /// the method estimator by definition id and falls back to base-minutes scaling.
    ///
    /// Never a single universal multiplier: each method composes its own factors.
    /// </summary>
    public interface ITaskDurationEstimatorV2
    {
        /// <summary>Definition ids this estimator handles (empty = fallback/default).</summary>
        bool Handles(string definitionId);

        DurationEstimate Estimate(TaskDurationInputs inputs);
    }

    /// <summary>
    /// Pass IV: fallback estimator preserving current behavior — definition base minutes
    /// scaled by the causal skill estimator. Used when no method estimator handles the id.
    /// </summary>
    public sealed class FallbackDurationEstimator : ITaskDurationEstimatorV2
    {
        private readonly int baseMinutes;

        public FallbackDurationEstimator(int baseMinutes)
        {
            this.baseMinutes = Math.Max(1, baseMinutes);
        }

        public bool Handles(string definitionId)
        {
            return false;
        }

        public DurationEstimate Estimate(TaskDurationInputs inputs)
        {
            // Base minutes adjusted by capability: capable workers are faster, floored at 1 minute.
            // (Skill scaling detail lives with Worker 1; this keeps the TTS-1 floor invariant.)
            float capabilityFactor = Mathf.Clamp(1.3f - inputs.WorkerCapability01, 0.5f, 1.3f);
            float seconds = baseMinutes * DurationConventions.SecondsPerMinute * capabilityFactor;
            return DurationEstimate.FromGameSeconds(seconds, "fallback:base-minutes");
        }
    }
}
