using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Animals;
using LandLedgers.Economy.Farming.Delivery;
using LandLedgers.Primitives;
using LandLedgers.Skills;
using LandLedgers.Tasks;
using UnityEngine;

namespace LandLedgers.Economy.Farming.Integration
{
    /// <summary>
    /// FVS-4: the chicken → egg → store loop. Egg output is biological and
    /// seasonal (Canon §9.8) — never a fixed Eggs Per Day business stat.
    /// Eggs set for incubation are unavailable for sale; chicks do not become
    /// layers immediately (canon: physical outcomes, not stats).
    /// </summary>
    public sealed class PoultryChain
    {
        public const string CollectEggsTaskId = "collect-eggs";

        /// <summary>Calibration: base chance a laying hen lays on a given day (spring).</summary>
        public const float BaseLayChancePerDay = 0.7f;

        private readonly HashSet<EntityId> hens = new HashSet<EntityId>();
        private readonly HashSet<EntityId> collectedBatchIds = new HashSet<EntityId>();
        private readonly HashSet<EntityId> incubationBatchIds = new HashSet<EntityId>();

        public static void RegisterTaskDefinitions(TaskAuthority authority)
        {
            if (authority == null) return;
            var collect = new TaskDefinition(CollectEggsTaskId, "Collect eggs", 1);
            collect.SetRequiredSkill(SkillIds.AnimalHusbandry, new[] { "egg-collection" });
            authority.RegisterDefinition(collect, out _);
        }

        public void RegisterHen(EntityId henId)
        {
            if (henId.IsValid) hens.Add(henId);
        }

        public int HenCount => hens.Count;

        /// <summary>Save support: the registered hen ids.</summary>
        public IEnumerable<EntityId> HenIds => hens;

        /// <summary>
        /// One day of laying: each hen may lay a clutch (usually 1 egg) into an
        /// HF-2 EggBatchState with her as dam (provenance). Seasonal factor
        /// scales the biological chance (Canon §9.8).
        /// </summary>
        public List<EggBatchState> LayDaily(
            AnimalRegistry registry,
            System.Random rng,
            int dayIndex,
            int locationBuildingId,
            float seasonLayingFactor,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            var batches = new List<EggBatchState>();
            if (registry == null) return batches;

            foreach (var henId in hens)
            {
                AnimalState hen = registry.GetAnimal(henId);
                if (hen == null || !hen.IsActive) continue;
                if (hen.Species != AnimalSpecies.Chicken || hen.Sex != AnimalSex.Female) continue;

                double roll = rng != null ? rng.NextDouble() : 0.5;
                if (roll < BaseLayChancePerDay * seasonLayingFactor)
                {
                    EggBatchState batch = PoultryLifecycle.LayClutch(
                        registry, henId, EntityId.Invalid, 1, dayIndex, locationBuildingId);
                    if (batch != null) batches.Add(batch);
                }
            }
            return batches;
        }

        /// <summary>
        /// Reserves a batch for incubation: those eggs can never be sold
        /// (Canon §9.8). Hatching later promotes them to chicks with parentage.
        /// </summary>
        public string MarkForIncubation(AnimalRegistry registry, EntityId batchId)
        {
            if (registry == null) return "PoultryChain: no registry.";
            EggBatchState batch;
            if (!registry.EggBatches.TryGetValue(batchId, out batch) || batch == null)
            {
                return $"PoultryChain: unknown egg batch {batchId}.";
            }
            if (collectedBatchIds.Contains(batchId))
            {
                return $"PoultryChain: batch {batchId} was already collected for sale — cannot incubate sold eggs.";
            }
            incubationBatchIds.Add(batchId);
            return null;
        }

        /// <summary>
        /// Hatches a reserved batch: explicit sex counts so male chicks are
        /// never silently dropped (Tech X §2.3).
        /// </summary>
        public List<AnimalState> HatchBatch(
            AnimalRegistry registry, EntityId batchId, int hatchDayIndex,
            int maleChicks, int femaleChicks, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            var empty = new List<AnimalState>();
            if (!incubationBatchIds.Contains(batchId))
            {
                diagnostics.Add($"PoultryChain: batch {batchId} was not reserved for incubation.");
                return empty;
            }
            return PoultryLifecycle.RecordHatch(registry, batchId, hatchDayIndex,
                maleChicks, femaleChicks, 0);
        }

        /// <summary>
        /// Converts a collected egg batch into a saleable FarmProduceLot with
        /// provenance (batch id + dam). Incubation-reserved or already-collected
        /// batches are refused — eggs sell exactly once.
        /// </summary>
        public FarmProduceLot CollectBatchToProduceLot(
            AnimalRegistry registry,
            EntityId batchId,
            string farmBusinessId,
            string farmBusinessName,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (registry == null)
            {
                diagnostics.Add("PoultryChain: no registry.");
                return null;
            }
            EggBatchState batch;
            if (!registry.EggBatches.TryGetValue(batchId, out batch) || batch == null)
            {
                diagnostics.Add($"PoultryChain: unknown egg batch {batchId}.");
                return null;
            }
            if (incubationBatchIds.Contains(batchId))
            {
                diagnostics.Add($"PoultryChain: batch {batchId} is reserved for incubation — unavailable for sale (Canon §9.8).");
                return null;
            }
            if (collectedBatchIds.Contains(batchId))
            {
                diagnostics.Add($"PoultryChain: batch {batchId} was already collected — eggs sell exactly once.");
                return null;
            }
            if (batch.EggCount <= 0)
            {
                diagnostics.Add($"PoultryChain: batch {batchId} has no eggs.");
                return null;
            }

            collectedBatchIds.Add(batchId);
            return new FarmProduceLot(
                "egg-" + batch.BatchId,
                "eggs",
                batch.EggCount,
                dayIndex,
                farmBusinessId ?? string.Empty,
                farmBusinessName ?? string.Empty);
        }

        /// <summary>Task minutes for egg collection: 1 min per 5 hens, min 1 (TTS-1 quantum).</summary>
        public static int CollectionMinutes(int henCount)
        {
            return Math.Max(1, Mathf.CeilToInt(henCount / 5f));
        }
    }
}
