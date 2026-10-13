using System.Collections.Generic;
using LandLedgers.Orchestration.Scenarios.FirstLedger;
using UnityEngine;

namespace LandLedgers.Orchestration.Scenarios
{
    /// <summary>
    /// DEV-1: the authored scenario definition. Lives as a ScriptableObject asset so it is
    /// editable in the editor AND readable at runtime (design notes rule 1: scenario
    /// definitions live in assets, never only in memory).
    ///
    /// Runtime completion state (which goals are done, live tunable overrides) is kept in
    /// ScenarioRuntimeState, NOT here — stopping play mode must never corrupt the authored
    /// definition (DEV-3 stop-play rules).
    /// </summary>
    [CreateAssetMenu(
        fileName = "NewScenario",
        menuName = "Land & Ledgers/Scenario Asset",
        order = 100)]
    public sealed class ScenarioAsset : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField]
        [Tooltip("Unique scenario id (e.g. 'founding-household'). Never rename once referenced.")]
        private string scenarioId = string.Empty;

        [SerializeField]
        private string displayName = "New Scenario";

        [SerializeField]
        [TextArea(2, 5)]
        private string description = string.Empty;

        [Header("Player Household (GHOST-DEF-006)")]
        [SerializeField]
        private PlayerHouseholdDeclaration playerHousehold = new PlayerHouseholdDeclaration();

        [Header("Population Inclusion (Tech X §2.1)")]
        [SerializeField]
        private PopulationInclusionRules populationInclusion = new PopulationInclusionRules();

        [Header("Starting World (P1-1 scenario presets)")]
        [SerializeField]
        [Tooltip("Authored starting-world configuration: start date, population range, " +
            "businesses, player start, property, economy and credit policy. Older assets " +
            "without this section get a default 55-95 configuration that still validates.")]
        private StartingWorldConfiguration startingWorld = new StartingWorldConfiguration();

        [Header("Goals & Objectives")]
        [SerializeField]
        private List<ScenarioGoal> goals = new List<ScenarioGoal>();

        [SerializeField]
        private List<ScenarioObjective> objectives = new List<ScenarioObjective>();

        [Header("Starting Tasks (TTS-2 definition ids)")]
        [SerializeField]
        [Tooltip("TaskDefinition ids enqueued at scenario bootstrap.")]
        private List<string> startingTaskDefinitionIds = new List<string>();

        [Header("Tunables")]
        [SerializeField]
        private List<TunableValue> tunables = new List<TunableValue>();

        public string ScenarioId => scenarioId;
        public string DisplayName => displayName;
        public string Description => description;
        public PlayerHouseholdDeclaration PlayerHousehold => playerHousehold;
        public PopulationInclusionRules PopulationInclusion => populationInclusion;
        public IReadOnlyList<ScenarioGoal> Goals
        {
            get
            {
                if (goals != null && goals.Count > 0)
                {
                    return goals;
                }

                // Compatibility for older imported FirstLedger assets that were
                // authored before the serialized goal list was added. The asset id
                // still selects the authored scenario; this does not create runtime
                // completion state or mutate the asset.
                return scenarioId == FirstLedgerScenario.ScenarioId
                    ? FirstLedgerScenario.BuildGoals()
                    : new List<ScenarioGoal>();
            }
        }

        public IReadOnlyList<ScenarioObjective> Objectives
        {
            get
            {
                if (objectives != null && objectives.Count > 0)
                {
                    return objectives;
                }

                return scenarioId == FirstLedgerScenario.ScenarioId
                    ? FirstLedgerScenario.BuildObjectives()
                    : new List<ScenarioObjective>();
            }
        }
        public IReadOnlyList<string> StartingTaskDefinitionIds => startingTaskDefinitionIds;
        public IReadOnlyList<TunableValue> Tunables => tunables;

        /// <summary>
        /// P1-1: authored starting-world configuration. Never null: assets
        /// authored before this section existed get a default configuration
        /// (55-95 residents, 1880-03-01 start) that still validates, so old
        /// assets keep working without silent behavior changes.
        /// </summary>
        public StartingWorldConfiguration StartingWorld
        {
            get
            {
                startingWorld ??= new StartingWorldConfiguration();

                // Additive migration for assets authored before the Pass V
                // starting-world section existed. The asset's established
                // scenario identity is documentary input; this does not
                // synthesize persons, money, property, or businesses.
                if (string.IsNullOrWhiteSpace(startingWorld.ScenarioId)
                    && !string.IsNullOrWhiteSpace(scenarioId))
                {
                    startingWorld.SetScenarioId(scenarioId);
                }

                return startingWorld;
            }
        }

        /// <summary>
        /// P1-1: converts this asset into a versioned ScenarioPreset for the
        /// coordinator. The preset is a deep copy; editing it never mutates the asset.
        /// </summary>
        public ScenarioPreset ToPreset(string presetId, string presetName, string presetVersion)
        {
            var preset = new ScenarioPreset();
            preset.SetIdentity(presetId, presetName, presetVersion);
            Generation.ScenarioPresetLibrary.DeepCopyIntoStartingWorld(StartingWorld, preset.StartingWorld);
            return preset;
        }

        /// <summary>
        /// DEV-1: asset self-validation. Returns human-readable problems; empty means valid.
        /// The ScenarioService refuses to bootstrap an invalid asset and says why (loud, not silent).
        /// </summary>
        public List<string> Validate()
        {
            var problems = new List<string>();

            if (string.IsNullOrWhiteSpace(scenarioId))
            {
                problems.Add("ScenarioId is empty. Set a unique id before bootstrapping.");
            }

            var seenGoals = new HashSet<string>();
            foreach (ScenarioGoal goal in goals)
            {
                if (string.IsNullOrWhiteSpace(goal.GoalId))
                {
                    problems.Add("A goal has an empty GoalId.");
                }
                else if (!seenGoals.Add(goal.GoalId))
                {
                    problems.Add($"Duplicate GoalId '{goal.GoalId}'.");
                }
            }

            var seenObjectives = new HashSet<string>();
            foreach (ScenarioObjective objective in objectives)
            {
                if (string.IsNullOrWhiteSpace(objective.ObjectiveId))
                {
                    problems.Add("An objective has an empty ObjectiveId.");
                }
                else if (!seenObjectives.Add(objective.ObjectiveId))
                {
                    problems.Add($"Duplicate ObjectiveId '{objective.ObjectiveId}'.");
                }
                else if (!string.IsNullOrWhiteSpace(objective.LinkedGoalId) && !seenGoals.Contains(objective.LinkedGoalId))
                {
                    problems.Add($"Objective '{objective.ObjectiveId}' links to unknown GoalId '{objective.LinkedGoalId}'.");
                }
            }

            // P1-1: the starting-world configuration must validate too; an asset
            // whose world config is broken is refused at bootstrap, loudly.
            if (!Generation.ScenarioPresetLibrary.ValidateStartingWorld(StartingWorld, problems))
            {
                problems.Add("StartingWorld configuration is invalid (see problems above).");
            }

            return problems;
        }

        /// <summary>
        /// DEV-1: explicit asset write-back for a tunable. Only called when Kennedy
        /// deliberately chooses "persist to asset" in the puppet master — runtime live
        /// edits otherwise stay in the runtime override (ScenarioService), never silently
        /// rewriting the authored asset. Callers in the editor are responsible for marking
        /// the asset dirty (UnityEditor API must not leak into runtime files).
        /// </summary>
        public void WriteTunableToAsset(string key, TunableValue newValue, List<string> auditLog)
        {
            if (tunables == null)
            {
                return;
            }

            for (int i = 0; i < tunables.Count; i++)
            {
                if (tunables[i].Key == key)
                {
                    tunables[i] = newValue;
                    auditLog?.Add($"ScenarioAsset '{scenarioId}': tunable '{key}' written to ASSET (explicit persist).");
                    return;
                }
            }

            tunables.Add(newValue);
            auditLog?.Add($"ScenarioAsset '{scenarioId}': tunable '{key}' written to ASSET (explicit persist; new entry).");
        }
    }
}
