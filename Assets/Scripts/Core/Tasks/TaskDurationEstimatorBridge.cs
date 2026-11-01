using EntityId = LandLedgers.Primitives.EntityId;

using System;
using UnityEngine;
using LandLedgers.Primitives;
using LandLedgers.Time;

namespace LandLedgers.Tasks
{
    /// <summary>
    /// Pass IV (Worker 1): adapts an <see cref="ITaskDurationEstimatorV2"/> to the
    /// existing <see cref="ITaskDurationEstimator"/> seam, so the universal estimator
    /// wires into TaskAuthority without breaking existing callers.
    ///
    /// Builds TaskDurationInputs from the definition, the worker, and (when
    /// estimating for a concrete task) the task's per-instance override inputs.
    /// Worker capability comes from the optional lookup; without it a neutral 0.5
    /// is used and method estimators fall back to their neutral defaults.
    /// </summary>
    public sealed class DurationEstimatorV2Adapter : ITaskDurationEstimator
    {
        private readonly ITaskDurationEstimatorV2 v2;
        private readonly Func<EntityId, string, float> capabilityLookup;

        /// <param name="v2">The V2 estimator (required).</param>
        /// <param name="capabilityLookup">
        /// Optional (workerId, skillId) -&gt; capability 0..1. Null = neutral 0.5.
        /// </param>
        public DurationEstimatorV2Adapter(
            ITaskDurationEstimatorV2 v2,
            Func<EntityId, string, float> capabilityLookup = null)
        {
            this.v2 = v2 ?? throw new ArgumentNullException(nameof(v2));
            this.capabilityLookup = capabilityLookup;
        }

        /// <summary>The wrapped V2 estimator.</summary>
        public ITaskDurationEstimatorV2 V2Estimator => v2;

        public int EstimateMinutes(TaskDefinition definition, EntityId workerId)
        {
            return EstimateMinutesForTask(null, definition, workerId);
        }

        public int EstimateMinutesForBase(int baseMinutes, string skillId, EntityId workerId)
        {
            var inputs = new TaskDurationInputs
            {
                BaseMinutes = Math.Max(1, baseMinutes),
                SkillId = skillId ?? string.Empty,
            };
            FillWorkerFields(inputs, workerId, inputs.SkillId);
            return Math.Max(1, v2.Estimate(inputs).WholeMinutes);
        }

        /// <summary>
        /// Task-aware estimate: per-instance override inputs on the task win over
        /// definition defaults; a planned-minutes override becomes the base the
        /// fallback scales (preserving the pre-Pass IV override behavior).
        /// </summary>
        public int EstimateMinutesForTask(WorkTask task, TaskDefinition definition, EntityId workerId)
        {
            TaskDurationInputs inputs = task != null && task.DurationInputsOverride != null
                ? CopyInputs(task.DurationInputsOverride)
                : new TaskDurationInputs();

            string definitionId = definition != null ? definition.DefinitionId ?? string.Empty
                : task != null ? task.DefinitionId ?? string.Empty : string.Empty;
            inputs.DefinitionId = definitionId;

            if (inputs.BaseMinutes <= 0)
            {
                inputs.BaseMinutes = 1;
            }
            if (task != null && task.HasPlannedMinutesOverride)
            {
                inputs.BaseMinutes = Math.Max(1, task.PlannedMinutes);
            }
            else if (definition != null)
            {
                inputs.BaseMinutes = Math.Max(1, definition.BaseMinutes);
            }

            if (string.IsNullOrEmpty(inputs.SkillId))
            {
                inputs.SkillId = definition != null ? definition.RequiredSkillId ?? string.Empty : string.Empty;
            }

            FillWorkerFields(inputs, workerId, inputs.SkillId);
            return Math.Max(1, v2.Estimate(inputs).WholeMinutes);
        }

        private void FillWorkerFields(TaskDurationInputs inputs, EntityId workerId, string skillId)
        {
            inputs.WorkerPersonId = workerId.Kind == EntityKind.Person ? workerId.Id : -1;
            float capability = 0.5f;
            if (capabilityLookup != null)
            {
                try
                {
                    capability = Mathf.Clamp01(capabilityLookup(workerId, skillId ?? string.Empty));
                }
                catch (Exception)
                {
                    capability = 0.5f;
                }
            }

            inputs.WorkerCapability01 = capability;
        }

        private static TaskDurationInputs CopyInputs(TaskDurationInputs source)
        {
            return new TaskDurationInputs
            {
                DefinitionId = source.DefinitionId,
                WorkQuantity = source.WorkQuantity,
                ItemCount = source.ItemCount,
                MassKgPerItem = source.MassKgPerItem,
                Bulk01 = source.Bulk01,
                DistanceMeters = source.DistanceMeters,
                AccessDifficulty01 = source.AccessDifficulty01,
                WorkerCapability01 = source.WorkerCapability01,
                WorkerFatigue01 = source.WorkerFatigue01,
                EquipmentMethodId = source.EquipmentMethodId,
                CrewSize = source.CrewSize,
                Congestion01 = source.Congestion01,
                SetupTeardownGameSeconds = source.SetupTeardownGameSeconds,
                Precision01 = source.Precision01,
                Supervision01 = source.Supervision01,
                Environment01 = source.Environment01,
                MaterialCondition01 = source.MaterialCondition01,
                BaseMinutes = source.BaseMinutes,
                SkillId = source.SkillId,
                WorkerPersonId = source.WorkerPersonId,
            };
        }
    }

    /// <summary>Pass IV (Worker 1): V2 estimator seam on TaskAuthority.</summary>
    public sealed partial class TaskAuthority
    {
        /// <summary>
        /// Installs a V2 duration estimator, adapted onto the existing estimator seam.
        /// Existing ITaskDurationEstimator callers are unaffected. Null restores the
        /// default estimator.
        /// </summary>
        /// <param name="estimator">The V2 estimator (universal or single-method).</param>
        /// <param name="capabilityLookup">
        /// Optional (workerId, skillId) -&gt; capability 0..1 for method estimators.
        /// Null = neutral 0.5.
        /// </param>
        public void SetDurationEstimatorV2(
            ITaskDurationEstimatorV2 estimator,
            Func<EntityId, string, float> capabilityLookup = null)
        {
            durationEstimator = estimator != null
                ? (ITaskDurationEstimator)new DurationEstimatorV2Adapter(estimator, capabilityLookup)
                : new DefaultTaskDurationEstimator();
        }

        /// <summary>
        /// Pass IV: task-aware planned-minutes estimate. V2 adapters receive the task
        /// (per-instance inputs); legacy estimators keep their exact existing paths.
        /// </summary>
        private int EstimatePlannedMinutes(WorkTask task, TaskDefinition definition, EntityId workerId)
        {
            var adapter = durationEstimator as DurationEstimatorV2Adapter;
            if (adapter != null)
            {
                return adapter.EstimateMinutesForTask(task, definition, workerId);
            }

            if (task.HasPlannedMinutesOverride)
            {
                return durationEstimator.EstimateMinutesForBase(
                    task.PlannedMinutes,
                    definition != null ? definition.RequiredSkillId : null,
                    workerId);
            }

            return durationEstimator.EstimateMinutes(definition, workerId);
        }
    }
}
