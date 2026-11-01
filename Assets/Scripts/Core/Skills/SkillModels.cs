using System;
using UnityEngine;

namespace LandLedgers.Skills
{
    /// <summary>
    /// TTS-3: authored data for one skill. Skills are registered, never hard-coded
    /// into logic — new skills register without touching existing code.
    ///
    /// Pass III Worker D: a skill definition optionally names its capability
    /// family (Rev XVII §4, layer 1) so aptitude (learning-rate tendency) can be
    /// resolved per skill. Family defaults to Unknown (aptitude 1.0).
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

        /// <summary>Maximum attainable level. TUNING VALUE. Legacy: kept for
        /// serialized compat; the level ladder is superseded by continuous
        /// capability (Rev VII §1.1).</summary>
        [SerializeField]
        private int maxLevel = 10;

        /// <summary>Broad transferable capability family (Rev XVII §4).</summary>
        [SerializeField]
        private CapabilityFamily family = CapabilityFamily.Unknown;

        public string SkillId => skillId;
        public string DisplayName => displayName;
        public string Description => description;
        public int MaxLevel => Math.Max(1, maxLevel);
        public CapabilityFamily Family => family;

        public SkillDefinition()
        {
        }

        public SkillDefinition(string skillId, string displayName, string description = null,
            CapabilityFamily family = CapabilityFamily.Unknown)
        {
            this.skillId = skillId ?? throw new ArgumentNullException(nameof(skillId));
            this.displayName = displayName ?? skillId;
            this.description = description ?? string.Empty;
            this.family = family;
        }
    }

    /// <summary>
    /// Well-known skill ids. These are the TTS-3 starter set — deliberately small and
    /// rudimentary; the registry accepts any additional skill id without code changes.
    /// In the Pass III model these ids are task-capability ids (Rev XVII §4, layer 2:
    /// what materially affects execution).
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
    /// Pass III Worker D: legacy per-person skill record, retained ONLY for
    /// old-save deserialization and deterministic migration.
    ///
    /// The XP/level ladder (1 XP/min, 100 XP/level, max 10) was an RPG-style
    /// progression — canon-violating (Rev VII §1.1: "Do not implement
    /// progression as … level-ups"). It is replaced by continuous capability
    /// (PersonCapabilityStore) + the experience ledger.
    ///
    /// MIGRATION (deterministic, documented, never silent):
    ///   level    = 1 + legacyXp / 100            (integer division)
    ///   residual = legacyXp % 100
    ///   capability01 = 0.05 + (level-1)*0.06 + (residual/100)*0.06
    /// The entire old ladder maps into Ordinary (0.05..0.65): time-served XP
    /// never certified excellence (Rev VII §1.3 anti-inflation; Rev XVII §1.2:
    /// "Experience does not equal excellence"). The source XP is preserved in
    /// MigratedFromXp — never silently dropped.
    /// </summary>
    [Serializable]
    public sealed class PersonSkillState
    {
        [SerializeField]
        private string skillId;

        /// <summary>Legacy XP. Retained for deserialization; consumed by migration.</summary>
        [SerializeField]
        private int experience;

        [SerializeField]
        private double capability01;

        [SerializeField]
        private int lastUpdatedDay;

        [SerializeField]
        private bool migrated;

        /// <summary>The legacy XP value this record migrated from; -1 when the
        /// record is native to the new model (no legacy data existed).</summary>
        [SerializeField]
        private int migratedFromXp = -1;

        public string SkillId => skillId;
        public double Capability01 => capability01;
        public int MigratedFromXp => migratedFromXp;

        /// <summary>Legacy: accumulated practice XP (pre-Pass III).</summary>
        public int LegacyExperience => experience;

        public PersonSkillState()
        {
        }

        public PersonSkillState(string skillId)
        {
            this.skillId = skillId ?? string.Empty;
        }

        internal void SetCapability(double value, int day)
        {
            capability01 = Math.Max(0.0, Math.Min(1.0, value));
            lastUpdatedDay = day;
            migrated = true;
            if (migratedFromXp < 0)
            {
                migratedFromXp = -1;
            }
        }

        /// <summary>
        /// One-time deterministic migration of legacy XP to continuous
        /// capability. Idempotent: safe to call on every load.
        /// </summary>
        public void EnsureMigrated()
        {
            if (migrated)
            {
                return;
            }

            migrated = true;
            if (experience > 0)
            {
                int level = 1 + experience / SkillService.ExperiencePerLevel;
                int residual = experience % SkillService.ExperiencePerLevel;
                capability01 = 0.05 + (level - 1) * 0.06 + (residual / 100.0) * 0.06;
                capability01 = Math.Max(0.0, Math.Min(1.0, capability01));
                migratedFromXp = experience;
                // 'experience' is intentionally retained as provenance.
            }
            else if (capability01 <= 0.0)
            {
                capability01 = PersonCapabilityStore.NoviceCapability;
                migratedFromXp = -1;
            }
        }

        /// <summary>
        /// Legacy level projection (pre-Pass III API). Kept for the
        /// deterministic migration formula only.
        /// </summary>
        public int LegacyLevel(int maxLevel)
        {
            return Math.Min(Math.Max(1, maxLevel), 1 + experience / SkillService.ExperiencePerLevel);
        }
    }
}
