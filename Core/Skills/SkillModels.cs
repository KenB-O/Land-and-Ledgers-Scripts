using System;
using UnityEngine;

namespace LandLedgers.Skills
{
    /// <summary>
    /// TTS-3: authored data for one skill. Skills are registered, never hard-coded
    /// into logic — new skills register without touching existing code.
    /// </summary>
    [Serializable]
    public sealed class SkillDefinition
    {
        [SerializeField]
        private string skillId;

        [SerializeField]
        private string displayName;

        [SerializeField]
        private string description;

        /// <summary>Maximum attainable level. TUNING VALUE.</summary>
        [SerializeField]
        private int maxLevel = 10;

        public string SkillId => skillId;
        public string DisplayName => displayName;
        public string Description => description;
        public int MaxLevel => Math.Max(1, maxLevel);

        public SkillDefinition()
        {
        }

        public SkillDefinition(string skillId, string displayName, string description = null)
        {
            this.skillId = skillId ?? throw new ArgumentNullException(nameof(skillId));
            this.displayName = displayName ?? skillId;
            this.description = description ?? string.Empty;
        }
    }

    /// <summary>
    /// Well-known skill ids. These are the TTS-3 starter set — deliberately small and
    /// rudimentary; the registry accepts any additional skill id without code changes.
    /// </summary>
    public static class SkillIds
    {
        public const string CustomerTending = "customer-tending";
        public const string FreightHandling = "freight-handling";
        public const string Stocking = "stocking";
        public const string AnimalHusbandry = "animal-husbandry";
        public const string CropTending = "crop-tending";
        public const string Cooking = "cooking";
        public const string Cleaning = "cleaning";
        public const string BasicRepair = "basic-repair";
    }

    /// <summary>
    /// TTS-3: one Person's standing in one skill. Level 1 is an untrained beginner.
    /// XP is rudimentary: 1 XP per minute of practiced work, 100 XP per level.
    /// </summary>
    [Serializable]
    public sealed class PersonSkillState
    {
        [SerializeField]
        private string skillId;

        [SerializeField]
        private int experience;

        public string SkillId => skillId;

        /// <summary>Accumulated practice XP.</summary>
        public int Experience => experience;

        /// <summary>Current level: 1 + one per 100 XP, capped at the skill's max level.</summary>
        public int Level(int maxLevel)
        {
            return Math.Min(Math.Max(1, maxLevel), 1 + experience / SkillService.ExperiencePerLevel);
        }

        public PersonSkillState()
        {
        }

        public PersonSkillState(string skillId)
        {
            this.skillId = skillId ?? string.Empty;
        }

        internal void AddExperience(int amount)
        {
            experience = Math.Max(0, experience + Math.Max(0, amount));
        }
    }
}
