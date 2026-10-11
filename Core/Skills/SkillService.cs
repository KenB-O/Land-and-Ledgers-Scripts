using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using UnityEngine;
using LandLedgers.Persistence;
using LandLedgers.Primitives;
using LandLedgers.Tasks;

namespace LandLedgers.Skills
{
    /// <summary>
    /// TTS-3: the skill/capability authority. Owns the skill registry (extension
    /// path for new skills), every Person's capability state, the experience
    /// ledger, aptitude records, domain familiarity, and observer-side
    /// capability evidence.
    ///
    /// Pass III Worker D — MODEL MIGRATION (canon correction):
    /// The XP/level ladder (1 XP/min, 100 XP/level, max 10) and the flat
    /// "−1 minute per level" duration rule were canon-violating:
    ///   - Rev VII §1.1: "Do not implement progression as … level-ups"
    ///   - Tech XIV Part II §2.4: no flat SkillMultiplier shortcuts
    ///   - XXXI §4.5: the seven-band display is superseded
    /// They are replaced by:
    ///   - Continuous capability 0..1 (PersonCapabilityStore; Rev VII §1.1)
    ///   - Experience ledger with diminishing-returns formation (Rev VII §1.2,
    ///     Rev XVII §12) — NO gains from calendar time alone
    ///   - Aptitude as learning-rate tendency (Rev XVII §3; never grants
    ///     fictional prior training)
    ///   - Causal outcome resolution (Tech II §2.4; XXXI §3.3)
    ///   - Observer-side evidence readings (XXXI §§4.4-4.5)
    ///
    /// Storage/registry/save-DTO plumbing is preserved; only the model inside
    /// migrated. Legacy PersonSkillState XP is deterministically migrated on
    /// load (see PersonSkillState.EnsureMigrated) — never silently dropped.
    ///
    /// Canon: XXXI Part I §§1.2-1.4, Part III §§3.1-3.5, Part IV §§4.4-4.6;
    /// Tech XIV Part II §§2.1-2.5, Part III §§3.4-3.5; preserved Rev VII
    /// Part I §§1.1-1.5; Rev XVII integration §§3-4, §12; Rev XVI §2.2.
    /// </summary>
    [Serializable]
    public sealed class SkillService
    {
        /// <summary>Legacy XP per skill level (pre-Pass III). Retained for the
        /// deterministic migration formula only.</summary>
        public const int ExperiencePerLevel = 100;

        [SerializeField]
        private List<SkillDefinition> skillDefinitions = new List<SkillDefinition>();

        /// <summary>
        /// Legacy per-person skill entries (pre-Pass III). Read on load and
        /// migrated into the capability store; never written by new code.
        /// </summary>
        [SerializeField]
        private List<PersonSkillEntry> legacySkillStates = new List<PersonSkillEntry>();

        private readonly Dictionary<string, SkillDefinition> definitionLookup =
            new Dictionary<string, SkillDefinition>(StringComparer.Ordinal);

        // Pass III Worker D: the four capability authorities + evidence.
        private readonly AptitudeStore aptitudeStore = new AptitudeStore();
        private readonly ExperienceLedger experienceLedger = new ExperienceLedger();
        private readonly DomainFamiliarityStore familiarityStore = new DomainFamiliarityStore();
        private readonly PersonCapabilityStore capabilityStore = new PersonCapabilityStore();
        private readonly CapabilityEvidenceService evidenceService = new CapabilityEvidenceService();
        private readonly ExperienceFormationService formationService = new ExperienceFormationService();
        private readonly ITaskOutcomeResolver outcomeResolver = new CausalTaskOutcomeResolver();

        public AptitudeStore Aptitudes => aptitudeStore;
        public ExperienceLedger Experience => experienceLedger;
        public DomainFamiliarityStore Familiarities => familiarityStore;
        public PersonCapabilityStore Capabilities => capabilityStore;
        public CapabilityEvidenceService Evidence => evidenceService;

        [Serializable]
        public sealed class PersonSkillEntry
        {
            [SerializeField]
            public EntityId personId;
            [SerializeField]
            public PersonSkillState state;
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

            RebuildDefinitionLookupIfNeeded();
            if (definitionLookup.ContainsKey(definition.SkillId))
            {
                rejectionReason = "Duplicate SkillId: " + definition.SkillId;
                return false;
            }

            skillDefinitions.Add(definition);
            definitionLookup[definition.SkillId] = definition;
            return true;
        }

        /// <summary>Registers the TTS-3 starter set with capability families. Safe to call once at boot.</summary>
        public void RegisterStarterSet()
        {
            string ignored;
            RegisterSkill(new SkillDefinition(SkillIds.CustomerTending, "Customer Tending", "Serving and waiting on customers. The store is not self-serve.", CapabilityFamily.CommercialInteraction), out ignored);
            RegisterSkill(new SkillDefinition(SkillIds.FreightHandling, "Freight Handling", "Loading and unloading goods; speed scales with lot size and care.", CapabilityFamily.Transport), out ignored);
            RegisterSkill(new SkillDefinition(SkillIds.Stocking, "Stocking", "Shelving goods and keeping displays faced.", CapabilityFamily.OrganizationManagement), out ignored);
            RegisterSkill(new SkillDefinition(SkillIds.AnimalHusbandry, "Animal Husbandry", "Feeding, milking and basic animal care.", CapabilityFamily.AnimalHandling), out ignored);
            RegisterSkill(new SkillDefinition(SkillIds.CropTending, "Crop Tending", "Planting, weeding and harvest help.", CapabilityFamily.Agricultural), out ignored);
            RegisterSkill(new SkillDefinition(SkillIds.Cooking, "Cooking", "Meal preparation.", CapabilityFamily.FoodProcessing), out ignored);
            RegisterSkill(new SkillDefinition(SkillIds.Cleaning, "Cleaning", "Sweeping, tidying and upkeep.", CapabilityFamily.OrganizationManagement), out ignored);
            RegisterSkill(new SkillDefinition(SkillIds.BasicRepair, "Basic Repair", "Simple fixes to tools, fixtures and fittings.", CapabilityFamily.MechanicalRepair), out ignored);
        }

        public SkillDefinition GetSkill(string skillId)
        {
            RebuildDefinitionLookupIfNeeded();
            SkillDefinition definition;
            return definitionLookup.TryGetValue(skillId ?? string.Empty, out definition) ? definition : null;
        }

        /// <summary>
        /// Present continuous capability 0..1 for a Person in a task capability.
        /// Unknown person/skill = novice (PersonCapabilityStore.NoviceCapability).
        /// Canon: Rev VII §1.1 (continuous capability, no levels).
        /// </summary>
        public double GetCapability(EntityId personId, string skillId)
        {
            if (personId.Kind != EntityKind.Person || string.IsNullOrEmpty(skillId))
            {
                return PersonCapabilityStore.NoviceCapability;
            }

            return capabilityStore.GetCapability(personId.ToString(), skillId);
        }

        /// <summary>
        /// Legacy level projection (1..10) derived from continuous capability.
        /// Kept ONLY for existing trade-runtime journeyman thresholds
        /// (Blacksmith, HarnessMaker, Wheelwright); new code must use
        /// GetCapability. Derived as 1 + floor(capability * 9).
        /// </summary>
        public int GetLevel(EntityId personId, string skillId)
        {
            double capability = GetCapability(personId, skillId);
            return Math.Max(1, Math.Min(10, 1 + (int)Math.Floor(capability * 9.0)));
        }

        /// <summary>
        /// Learning-rate multiplier for a person/skill via the skill's
        /// capability family. 1.0 when no aptitude is recorded.
        /// Canon: Rev XVII §3 (aptitude accelerates; never grants skill).
        /// </summary>
        public double GetLearningRate(EntityId personId, string skillId)
        {
            if (personId.Kind != EntityKind.Person)
            {
                return 1.0;
            }

            SkillDefinition definition = GetSkill(skillId);
            CapabilityFamily family = definition != null ? definition.Family : CapabilityFamily.Unknown;
            return aptitudeStore.GetLearningRate(personId.ToString(), family);
        }

        /// <summary>
        /// How relevant the worker's recorded experience is to this task class,
        /// 0..1. Rises with real episodes; 20 solid episodes ≈ full relevance.
        /// </summary>
        public double GetExperienceRelevance(EntityId personId, string skillId)
        {
            if (personId.Kind != EntityKind.Person || string.IsNullOrEmpty(skillId))
            {
                return 0.0;
            }

            IReadOnlyList<ExperienceEntry> entries =
                experienceLedger.GetEntries(personId.ToString(), skillId);
            if (entries.Count == 0)
            {
                return 0.0;
            }

            double score = 0.0;
            foreach (ExperienceEntry entry in entries)
            {
                score += 0.5 + 0.5 * Math.Max(0.0, Math.Min(1.0, entry.Difficulty01));
            }

            return Math.Min(1.0, score / 20.0);
        }

        /// <summary>
        /// Records one real task episode: idempotent ledger entry + capability
        /// update via diminishing-returns formation. Returns false when this
        /// exact entry was already recorded (no double credit).
        /// Canon: Rev VII §1.2; Rev XVII §12.
        /// </summary>
        public bool RecordTaskExperience(EntityId personId, ExperienceFormationInputs inputs)
        {
            if (personId.Kind != EntityKind.Person || inputs == null ||
                string.IsNullOrEmpty(inputs.TaskCapabilityId))
            {
                return false;
            }

            string personKey = personId.ToString();
            inputs.PersonId = personKey;
            if (inputs.AptitudeMultiplier <= 0)
            {
                inputs.AptitudeMultiplier = GetLearningRate(personId, inputs.TaskCapabilityId);
            }

            var entry = new ExperienceEntry
            {
                PersonId = personKey,
                TaskCapabilityId = inputs.TaskCapabilityId,
                Difficulty01 = inputs.Difficulty01,
                Novelty01 = inputs.Novelty01,
                Supervised = inputs.Supervised,
                Outcome = inputs.Outcome,
                Day = inputs.Day,
                MinutesSpent = inputs.MinutesSpent,
            };

            if (!experienceLedger.TryRecord(entry))
            {
                return false; // already recorded: no double credit, no re-formation
            }

            int recentSimilar = experienceLedger.CountRecent(
                personKey, inputs.TaskCapabilityId, inputs.Day, 30);
            // Exclude the entry just recorded from the routine-work count.
            recentSimilar = Math.Max(0, recentSimilar - 1);

            double current = capabilityStore.GetCapability(personKey, inputs.TaskCapabilityId);
            double delta = formationService.ComputeDelta(current, inputs, recentSimilar);
            capabilityStore.SetCapability(
                personKey, inputs.TaskCapabilityId, current + delta, inputs.Day);
            return true;
        }

        /// <summary>
        /// Builds causal outcome inputs for a worker/skill at assignment time.
        /// Day-of conditions (fatigue, pressure, supervision) are neutral here;
        /// the worker's real capability and experience still move the estimate.
        /// </summary>
        public CausalOutcomeInputs BuildCausalInputs(EntityId workerId, string skillId, double baseMinutes)
        {
            return new CausalOutcomeInputs
            {
                BaseMinutes = Math.Max(1.0, baseMinutes),
                Capability01 = GetCapability(workerId, skillId),
                ExperienceRelevance01 = GetExperienceRelevance(workerId, skillId),
                Difficulty01 = 0.5,
                Supervised = false,
                EquipmentCondition01 = 1.0,
                Fatigue01 = 0.0,
                TimePressure01 = 0.0,
                InstructionQuality01 = 0.5,
            };
        }

        /// <summary>
        /// Resolves a task completion: causal outcome from the worker's real
        /// capability, then records the experience episode. Returns the
        /// deterministic TaskOutcome — drivers pass it to
        /// TaskAuthority.CompleteTask, which already accepts varied outcomes.
        /// This is the path that starts varying TaskOutcome by worker
        /// (XXXI §3.3); the authority's auto-complete default is untouched.
        /// </summary>
        public TaskOutcome ResolveTaskCompletion(
            EntityId workerId, string taskCapabilityId, CausalOutcomeInputs outcomeInputs,
            double difficulty01, double novelty01, bool supervised,
            int day, string taskIdForDeterminism)
        {
            if (workerId.Kind != EntityKind.Person || string.IsNullOrEmpty(taskCapabilityId))
            {
                return TaskOutcome.Success;
            }

            CausalOutcomeResult result = outcomeResolver.Resolve(outcomeInputs);
            TaskOutcome outcome = TaskOutcomeDetermination.DetermineOutcome(
                result, taskIdForDeterminism, workerId.ToString(), day);

            var formationInputs = new ExperienceFormationInputs
            {
                TaskCapabilityId = taskCapabilityId,
                Difficulty01 = difficulty01,
                Novelty01 = novelty01,
                Supervised = supervised,
                Outcome = outcome.ToString(),
                AptitudeMultiplier = GetLearningRate(workerId, taskCapabilityId),
                MinutesSpent = Math.Max(1.0, outcomeInputs != null ? outcomeInputs.BaseMinutes : 1.0),
                Day = day,
            };
            RecordTaskExperience(workerId, formationInputs);
            return outcome;
        }

        /// <summary>
        /// Practice hook for the task system, Pass III form: after minutes are
        /// worked on a task, record experience toward each of the definition's
        /// learning tags. The caller supplies formation context (difficulty,
        /// novelty, supervision, day); outcome resolution is separate.
        /// Replaces the canon-violating ApplyPracticeFromTask (1 XP/min ladder).
        /// </summary>
        public void ApplyExperienceFromTask(
            TaskAuthority authority, EntityId taskId,
            double difficulty01, double novelty01, bool supervised,
            string outcome, int day, double minutesWorked)
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
                var inputs = new ExperienceFormationInputs
                {
                    TaskCapabilityId = skillId,
                    Difficulty01 = difficulty01,
                    Novelty01 = novelty01,
                    Supervised = supervised,
                    Outcome = outcome,
                    AptitudeMultiplier = GetLearningRate(task.AssigneeId, skillId),
                    MinutesSpent = minutesWorked,
                    Day = day,
                };
                RecordTaskExperience(task.AssigneeId, inputs);
            }
        }

        public void SetAptitude(EntityId personId, CapabilityFamily family, double learningRateMultiplier, int day)
        {
            if (personId.Kind != EntityKind.Person)
            {
                return;
            }

            aptitudeStore.SetAptitude(personId.ToString(), family, learningRateMultiplier, day);
        }

        public void SetDomainFamiliarity(EntityId personId, string domain, double familiarity01)
        {
            if (personId.Kind != EntityKind.Person)
            {
                return;
            }

            familiarityStore.SetFamiliarity(personId.ToString(), domain, familiarity01);
        }

        public double GetDomainFamiliarity(EntityId personId, string domain)
        {
            if (personId.Kind != EntityKind.Person)
            {
                return 0.0;
            }

            return familiarityStore.GetFamiliarity(personId.ToString(), domain);
        }

        private void RebuildDefinitionLookupIfNeeded()
        {
            if (definitionLookup.Count == skillDefinitions.Count)
            {
                return;
            }

            definitionLookup.Clear();
            foreach (SkillDefinition definition in skillDefinitions)
            {
                definitionLookup[definition.SkillId] = definition;
            }
        }

        /// <summary>
        /// CLN-1: captures capability state for the save pipeline. Definitions,
        /// the four authority stores, and observer evidence; lookups rebuild on
        /// load. Legacy entries are migrated on load, never written.
        /// </summary>
        public SkillServiceSaveDto CaptureSaveDto()
        {
            return new SkillServiceSaveDto
            {
                definitions = new List<SkillDefinition>(skillDefinitions),
                aptitudes = aptitudeStore.CaptureRecords(),
                experienceEntries = experienceLedger.CaptureEntries(),
                experienceSequence = experienceLedger.CaptureSequence(),
                familiarities = familiarityStore.CaptureRecords(),
                capabilities = capabilityStore.CaptureStates(),
                evidenceItems = evidenceService.CaptureItems(),
            };
        }

        /// <summary>
        /// CLN-1: restores capability state. Clear-then-load on every store;
        /// legacy XP entries are deterministically migrated into the capability
        /// store (PersonSkillState.EnsureMigrated) — never silently dropped,
        /// never double-counted.
        /// </summary>
        public void LoadFromSaveDto(SkillServiceSaveDto dto)
        {
            skillDefinitions.Clear();
            legacySkillStates.Clear();
            aptitudeStore.LoadRecords(null);
            experienceLedger.LoadEntries(null, 0);
            familiarityStore.LoadRecords(null);
            capabilityStore.LoadStates(null);
            evidenceService.LoadItems(null);

            if (dto == null)
            {
                return;
            }

            if (dto.definitions != null)
            {
                skillDefinitions.AddRange(dto.definitions);
            }

            RebuildDefinitionLookupIfNeeded();

            aptitudeStore.LoadRecords(dto.aptitudes);
            experienceLedger.LoadEntries(dto.experienceEntries, dto.experienceSequence);
            familiarityStore.LoadRecords(dto.familiarities);
            capabilityStore.LoadStates(dto.capabilities);
            evidenceService.LoadItems(dto.evidenceItems);

            // Legacy migration: pre-Pass III saves carry XP entries.
            if (dto.legacySkillStates != null)
            {
                foreach (PersonSkillEntry entry in dto.legacySkillStates)
                {
                    if (entry == null || entry.state == null)
                    {
                        continue;
                    }

                    entry.state.EnsureMigrated();
                    capabilityStore.SetCapability(
                        entry.personId.ToString(),
                        entry.state.SkillId,
                        entry.state.Capability01,
                        0);
                }
            }
        }
    }
}
