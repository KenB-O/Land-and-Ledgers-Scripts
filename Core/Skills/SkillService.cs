using System;
using System.Collections.Generic;
using UnityEngine;
using LandLedgers.Primitives;
using LandLedgers.Tasks;

namespace LandLedgers.Skills
{
    /// <summary>
    /// TTS-3: the skill authority. Owns the skill registry (extension path for new
    /// skills), every Person's skill states, rudimentary XP, and the skill-based
    /// task duration estimator that plugs into <see cref="TaskAuthority"/>.
    ///
    /// Duration curve (Kennedy's rule): each skill level above 1 shaves one minute
    /// off the task's base minutes, floored at 1. TUNING VALUE — the curve shape is
    /// data to tune, not logic to rewrite.
    /// </summary>
    [Serializable]
    public sealed class SkillService
    {
        /// <summary>XP per skill level. TUNING VALUE.</summary>
        public const int ExperiencePerLevel = 100;

        [SerializeField]
        private List<SkillDefinition> skillDefinitions = new List<SkillDefinition>();

        [SerializeField]
        private List<PersonSkillEntry> skillStates = new List<PersonSkillEntry>();

        private readonly Dictionary<string, SkillDefinition> definitionLookup =
            new Dictionary<string, SkillDefinition>(StringComparer.Ordinal);

        private readonly Dictionary<SkillKey, PersonSkillState> stateLookup =
            new Dictionary<SkillKey, PersonSkillState>();

        [Serializable]
        private sealed class PersonSkillEntry
        {
            [SerializeField]
            public EntityId personId;
            [SerializeField]
            public PersonSkillState state;
        }

        private struct SkillKey : IEquatable<SkillKey>
        {
            public readonly EntityId PersonId;
            public readonly string SkillId;

            public SkillKey(EntityId personId, string skillId)
            {
                PersonId = personId;
                SkillId = skillId ?? string.Empty;
            }

            public bool Equals(SkillKey other)
            {
                return PersonId.Equals(other.PersonId) && string.Equals(SkillId, other.SkillId, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is SkillKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (PersonId.GetHashCode() * 397) ^ (SkillId != null ? SkillId.GetHashCode() : 0);
                }
            }
        }

        /// <summary>
        /// Registers a skill definition. The extension path: mods and future systems
        /// call this with new skill ids — no existing code changes. Duplicates rejected.
        /// </summary>
        public bool RegisterSkill(SkillDefinition definition, out string rejectionReason)
        {
            rejectionReason = null;
            if (definition == null || string.IsNullOrEmpty(definition.SkillId))
            {
                rejectionReason = "SkillDefinition requires a non-empty SkillId.";
                return false;
            }

            RebuildLookupsIfNeeded();
            if (definitionLookup.ContainsKey(definition.SkillId))
            {
                rejectionReason = "Duplicate SkillId: " + definition.SkillId;
                return false;
            }

            skillDefinitions.Add(definition);
            definitionLookup[definition.SkillId] = definition;
            return true;
        }

        /// <summary>Registers the TTS-3 starter set. Safe to call once at boot.</summary>
        public void RegisterStarterSet()
        {
            string ignored;
            RegisterSkill(new SkillDefinition(SkillIds.CustomerTending, "Customer Tending", "Serving and waiting on customers. The store is not self-serve."), out ignored);
            RegisterSkill(new SkillDefinition(SkillIds.FreightHandling, "Freight Handling", "Loading and unloading goods; speed scales with lot size and care."), out ignored);
            RegisterSkill(new SkillDefinition(SkillIds.Stocking, "Stocking", "Shelving goods and keeping displays faced."), out ignored);
            RegisterSkill(new SkillDefinition(SkillIds.AnimalHusbandry, "Animal Husbandry", "Feeding, milking and basic animal care."), out ignored);
            RegisterSkill(new SkillDefinition(SkillIds.CropTending, "Crop Tending", "Planting, weeding and harvest help."), out ignored);
            RegisterSkill(new SkillDefinition(SkillIds.Cooking, "Cooking", "Meal preparation."), out ignored);
            RegisterSkill(new SkillDefinition(SkillIds.Cleaning, "Cleaning", "Sweeping, tidying and upkeep."), out ignored);
            RegisterSkill(new SkillDefinition(SkillIds.BasicRepair, "Basic Repair", "Simple fixes to tools, fixtures and fittings."), out ignored);
        }

        public SkillDefinition GetSkill(string skillId)
        {
            RebuildLookupsIfNeeded();
            SkillDefinition definition;
            return definitionLookup.TryGetValue(skillId ?? string.Empty, out definition) ? definition : null;
        }

        /// <summary>Current level for a Person in a skill. Unknown person/skill = level 1.</summary>
        public int GetLevel(EntityId personId, string skillId)
        {
            SkillDefinition definition = GetSkill(skillId);
            int maxLevel = definition != null ? definition.MaxLevel : 10;
            PersonSkillState state = FindState(personId, skillId);
            return state != null ? state.Level(maxLevel) : 1;
        }

        /// <summary>
        /// Grants practice XP (1 XP per minute worked is the convention). Returns the
        /// new level so callers can react to level-ups.
        /// </summary>
        public int GrantPractice(EntityId personId, string skillId, int minutesWorked)
        {
            if (personId.Kind != EntityKind.Person || string.IsNullOrEmpty(skillId))
            {
                return 1;
            }

            PersonSkillState state = GetOrCreateState(personId, skillId);
            state.AddExperience(Math.Max(0, minutesWorked));
            SkillDefinition definition = GetSkill(skillId);
            return state.Level(definition != null ? definition.MaxLevel : 10);
        }

        /// <summary>
        /// Practice hook for the task system: after minutes are worked on a task,
        /// grant XP toward each of the definition's learning tags. Called by the game
        /// loop after <see cref="TaskAuthority.RecordWork"/> — the authority itself
        /// stays decoupled from skills.
        /// </summary>
        public void ApplyPracticeFromTask(TaskAuthority authority, EntityId taskId, int minutesWorked)
        {
            if (authority == null)
            {
                return;
            }

            WorkTask task = authority.FindTask(taskId);
            if (task == null || !task.HasAssignee)
            {
                return;
            }

            TaskDefinition definition = authority.GetDefinition(task.DefinitionId);
            if (definition == null)
            {
                return;
            }

            foreach (string skillId in definition.LearningTags)
            {
                GrantPractice(task.AssigneeId, skillId, minutesWorked);
            }
        }

        /// <summary>
        /// Skill-scaled duration: base minutes minus one per level above 1, floored
        /// at 1 (Kennedy's rule). Unskilled tasks use base minutes unchanged.
        /// </summary>
        public int ScaleMinutes(int baseMinutes, int skillLevel)
        {
            int scaled = Math.Max(1, baseMinutes) - Math.Max(0, skillLevel - 1);
            return Math.Max(1, scaled);
        }

        private PersonSkillState FindState(EntityId personId, string skillId)
        {
            RebuildLookupsIfNeeded();
            PersonSkillState state;
            return stateLookup.TryGetValue(new SkillKey(personId, skillId), out state) ? state : null;
        }

        private PersonSkillState GetOrCreateState(EntityId personId, string skillId)
        {
            PersonSkillState state = FindState(personId, skillId);
            if (state != null)
            {
                return state;
            }

            state = new PersonSkillState(skillId);
            var entry = new PersonSkillEntry { personId = personId, state = state };
            skillStates.Add(entry);
            stateLookup[new SkillKey(personId, skillId)] = state;
            return state;
        }

        private void RebuildLookupsIfNeeded()
        {
            if (definitionLookup.Count == skillDefinitions.Count && stateLookup.Count == skillStates.Count)
            {
                return;
            }

            definitionLookup.Clear();
            foreach (SkillDefinition definition in skillDefinitions)
            {
                definitionLookup[definition.SkillId] = definition;
            }

            stateLookup.Clear();
            foreach (PersonSkillEntry entry in skillStates)
            {
                if (entry != null && entry.state != null)
                {
                    stateLookup[new SkillKey(entry.personId, entry.state.SkillId)] = entry.state;
                }
            }
        }
    }

    /// <summary>
    /// TTS-3: <see cref="ITaskDurationEstimator"/> implementation that scales task
    /// durations by the worker's skill level. Install via
    /// <see cref="TaskAuthority.SetDurationEstimator"/>.
    /// </summary>
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
            return skillService.ScaleMinutes(baseMinutes, level);
        }
    }
}
