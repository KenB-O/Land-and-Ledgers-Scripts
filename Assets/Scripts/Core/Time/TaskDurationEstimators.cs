using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Time
{
    /// <summary>
    /// Pass IV (Worker 1): definition duration data without a Core/Tasks dependency.
    /// Lets the universal estimator (LandLedgers.Time) stay below Core/Tasks in the
    /// layering; the TaskAuthority-side bridge fills this from TaskDefinition.
    /// </summary>
    public struct DefinitionDurationData
    {
        public int BaseMinutes;
        public string SkillId;

        public DefinitionDurationData(int baseMinutes, string skillId)
        {
            BaseMinutes = Math.Max(1, baseMinutes);
            SkillId = skillId ?? string.Empty;
        }
    }

    /// <summary>
    /// Pass IV (Worker 1): the universal duration estimator. Selects the first
    /// registered method estimator whose Handles(definitionId) matches; anything
    /// unhandled falls back to the legacy estimator (supplied by the caller —
    /// production passes the causal skill estimator, preserving current behavior
    /// for definitions no method estimator owns).
    ///
    /// Never a universal multiplier: each method composes its own factors; the
    /// fallback preserves base-minutes behavior for everything else.
    /// </summary>
    public sealed class UniversalTaskDurationEstimator : ITaskDurationEstimatorV2
    {
        private readonly List<ITaskDurationEstimatorV2> methodEstimators;
        private readonly Func<TaskDurationInputs, DurationEstimate> legacyFallback;

        public UniversalTaskDurationEstimator(
            IEnumerable<ITaskDurationEstimatorV2> methodEstimators,
            Func<TaskDurationInputs, DurationEstimate> legacyFallback)
        {
            this.methodEstimators = new List<ITaskDurationEstimatorV2>();
            if (methodEstimators != null)
            {
                this.methodEstimators.AddRange(methodEstimators);
            }

            this.legacyFallback = legacyFallback
                ?? (inputs => DurationEstimate.FromGameSeconds(
                    Math.Max(1, inputs != null ? inputs.BaseMinutes : 1) * DurationConventions.SecondsPerMinute,
                    "fallback:base-minutes"));
        }

        public bool Handles(string definitionId)
        {
            return true;
        }

        public DurationEstimate Estimate(TaskDurationInputs inputs)
        {
            string definitionId = inputs != null ? inputs.DefinitionId ?? string.Empty : string.Empty;
            foreach (ITaskDurationEstimatorV2 estimator in methodEstimators)
            {
                if (estimator != null && estimator.Handles(definitionId))
                {
                    return estimator.Estimate(inputs);
                }
            }

            return legacyFallback(inputs);
        }
    }

    /// <summary>
    /// Pass IV (Worker 1): handling/loading estimator (freight load/unload, carrying,
    /// hauling). Composes per-item mass, bulk, carry distance, access difficulty and
    /// crew size — unloading a 20lb flour sack and moving an industrial oven produce
    /// very different durations; extra workers help only within physical plausibility
    /// (a sack needs at most two hands; an oven crew tops out around six).
    ///
    /// All rates are explicit configurable estimates (DurationContracts: never
    /// falsely-precise hard-coded constants). No universal multiplier.
    /// Canon: XXXI Part III §§3.1-3.7. Technical: XIV Part II §§2.1-2.8.
    /// </summary>
    public sealed class HandlingDurationEstimator : ITaskDurationEstimatorV2
    {
        // ---- Configurable calibration estimates (tuning values, not measurements) ----
        /// <summary>One-person reference load in kg (~ a full sack). Above this, items-per-trip drops.</summary>
        public const float ReferenceLoadKg = 25f;
        /// <summary>Lift/carry/place/orient per item in game seconds, before mass/bulk scaling.</summary>
        public const float BaseHandlingSecondsPerItem = 18f;
        /// <summary>Loaded walking speed in m/s (deliberate carry pace).</summary>
        public const float LoadedWalkMetersPerSecond = 1.1f;
        /// <summary>Unloaded return walking speed in m/s.</summary>
        public const float EmptyWalkMetersPerSecond = 1.4f;
        /// <summary>Mass (kg) above which rigging/setup applies (straps, hand truck, levers).</summary>
        public const float HeavyItemThresholdKg = 50f;
        /// <summary>Extra setup seconds per heavy item (rigging the lift: straps, hand truck, levers).</summary>
        public const float HeavyItemSetupSeconds = 600f;

        private readonly HashSet<string> definitionIds;

        /// <summary>Definition ids handled when no explicit set is supplied.</summary>
        public static readonly string[] DefaultDefinitionIds = { "fr.load-freight", "fr.unload-freight" };

        public HandlingDurationEstimator(IEnumerable<string> definitionIds = null)
        {
            this.definitionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in definitionIds ?? DefaultDefinitionIds)
            {
                if (!string.IsNullOrEmpty(id))
                {
                    this.definitionIds.Add(id);
                }
            }
        }

        public bool Handles(string definitionId)
        {
            return definitionIds.Contains(definitionId ?? string.Empty);
        }

        public DurationEstimate Estimate(TaskDurationInputs inputs)
        {
            if (inputs == null)
            {
                return DurationEstimate.FromGameSeconds(60f, "handling:empty-inputs");
            }

            float massKg = Math.Max(0f, inputs.MassKgPerItem);
            float bulk = Mathf.Clamp01(inputs.Bulk01);
            float access = Mathf.Clamp01(inputs.AccessDifficulty01);
            int itemCount = Math.Max(1, inputs.ItemCount);
            float distance = Math.Max(0f, inputs.DistanceMeters);
            int crew = Math.Max(1, inputs.CrewSize);

            // Per-item handling: lift, orient, place. Scales with mass and bulk;
            // heavy items additionally scale superlinearly (leverage, repositioning).
            float handlingPerItem = BaseHandlingSecondsPerItem * (1f + massKg / ReferenceLoadKg + bulk);
            if (massKg > HeavyItemThresholdKg)
            {
                handlingPerItem *= 1f + (massKg - HeavyItemThresholdKg) / HeavyItemThresholdKg;
                handlingPerItem += HeavyItemSetupSeconds;
            }

            // Carry leg: loaded walk out, empty walk back per trip. Heavy loads and
            // bad access slow the walk; access never stops it entirely.
            float massSlowdown = 1f / (1f + massKg / 50f);
            float accessSlowdown = Math.Max(0.2f, 1f - 0.6f * access);
            float loadedSpeed = Math.Max(0.05f, LoadedWalkMetersPerSecond * massSlowdown * accessSlowdown);
            float emptySpeed = Math.Max(0.05f, EmptyWalkMetersPerSecond * accessSlowdown);
            float carrySecondsPerTrip = distance / loadedSpeed + distance / emptySpeed;

            // Physical plausibility: items per trip fall with mass and bulk.
            int itemsPerTrip = bulk > 0.7f ? 1 : Mathf.Clamp(Mathf.FloorToInt(30f / Math.Max(5f, massKg)), 1, 4);
            int trips = (itemCount + itemsPerTrip - 1) / itemsPerTrip;

            // Crew helps only within physical plausibility: a sack needs at most two
            // people; a bulky/heavy load tops out around six (sides, corners, levers).
            int maxUsefulCrew = (massKg > 100f || bulk > 0.7f) ? 6 : (massKg > 40f ? 4 : 2);
            int effectiveCrew = Math.Min(crew, maxUsefulCrew);

            float capabilityFactor = Mathf.Clamp(1.15f - 0.3f * inputs.WorkerCapability01, 0.85f, 1.15f);
            float environmentFactor = 0.8f + 0.2f * Mathf.Clamp01(inputs.Environment01);
            float precisionFactor = 1f + 0.5f * Mathf.Clamp01(inputs.Precision01);
            float congestionFactor = 1f + 0.5f * Mathf.Clamp01(inputs.Congestion01);

            float totalSeconds = inputs.SetupTeardownGameSeconds
                + trips * (handlingPerItem * itemsPerTrip + carrySecondsPerTrip) / effectiveCrew;
            totalSeconds = totalSeconds * capabilityFactor * precisionFactor * congestionFactor / environmentFactor;

            return DurationEstimate.FromGameSeconds(totalSeconds, "handling:method-estimate");
        }
    }

    /// <summary>
    /// Pass IV (Worker 1): travel-time estimator. Distance over an effective walking
    /// speed reduced by access difficulty and environment; setup/teardown covers
    /// preparing to move. Explicit configurable estimate, not a measured rate.
    /// Canon: XXXI Part III §§3.1-3.7.
    /// </summary>
    public sealed class TravelDurationEstimator : ITaskDurationEstimatorV2
    {
        /// <summary>Unimpeded walking speed in m/s (~4.8 km/h).</summary>
        public const float WalkMetersPerSecond = 1.33f;

        private readonly HashSet<string> definitionIds;

        /// <summary>Definition ids handled when no explicit set is supplied.</summary>
        public static readonly string[] DefaultDefinitionIds = { "travel" };

        public TravelDurationEstimator(IEnumerable<string> definitionIds = null)
        {
            this.definitionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in definitionIds ?? DefaultDefinitionIds)
            {
                if (!string.IsNullOrEmpty(id))
                {
                    this.definitionIds.Add(id);
                }
            }
        }

        public bool Handles(string definitionId)
        {
            return definitionIds.Contains(definitionId ?? string.Empty);
        }

        public DurationEstimate Estimate(TaskDurationInputs inputs)
        {
            if (inputs == null)
            {
                return DurationEstimate.FromGameSeconds(60f, "travel:empty-inputs");
            }

            float distance = Math.Max(0f, inputs.DistanceMeters);
            float access = Mathf.Clamp01(inputs.AccessDifficulty01);
            float environment = Mathf.Clamp01(inputs.Environment01);

            float effectiveSpeed = WalkMetersPerSecond
                * Math.Max(0.2f, 1f - 0.5f * access)
                * (0.7f + 0.3f * environment);
            effectiveSpeed = Math.Max(0.05f, effectiveSpeed);

            float totalSeconds = inputs.SetupTeardownGameSeconds + distance / effectiveSpeed;
            return DurationEstimate.FromGameSeconds(totalSeconds, "travel:method-estimate");
        }
    }
}
