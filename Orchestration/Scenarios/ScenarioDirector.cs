using System;
using System.Collections.Generic;
using LandLedgers.Orchestration.Player;
using LandLedgers.Population;
using LandLedgers.Tasks;
using LandLedgers.Time;
using UnityEngine;

namespace LandLedgers.Orchestration.Scenarios
{
    /// <summary>
    /// DEV-1: the global scenario manager. Lives in the single scene (DEV-4: one scene,
    /// no main menu — scenario switching happens in-scene, never via scene loads).
    ///
    /// ADR-001 (Land_and_Ledgers_ADR_001_One_Scene_Workflow_2026-10-03.md): Kennedy
    /// directed single-scene development with no main menu for now. Do NOT add a menu
    /// scene or multi-scene flow without revisiting that ADR with him — scenario
    /// switching belongs here, in-scene, via SwitchScenario().
    ///
    /// Responsibilities:
    /// - Owns the scenario registry (ScriptableObject assets, inspector-assignable).
    /// - SwitchScenario(): tears down the active run and bootstraps the new one in-scene.
    /// - Live-edit API for the puppet master (DEV-2): goals, objectives, tasks, tunables.
    /// - Declares the player household at bootstrap (GHOST-DEF-006, via HF-3).
    ///
    /// World services (lifecycle, tasks, player) are INJECTED, not constructed here —
    /// the director orchestrates; Kennedy's world bootstrap owns the world state.
    /// Injected services may be null in a bare scene: bootstrap degrades to
    /// registry-only mode and says so loudly in the diagnostics.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ScenarioDirector : MonoBehaviour
    {
        [Header("Scenario Registry")]
        [SerializeField]
        [Tooltip("ScenarioAsset definitions. Editable in the editor; the director never rewrites them silently.")]
        private List<ScenarioAsset> scenarioRegistry = new List<ScenarioAsset>();

        [Header("Boot")]
        [SerializeField]
        [Tooltip("Scenario id to auto-begin on Start. Empty = no auto-begin.")]
        private string bootScenarioId = string.Empty;

        public static ScenarioDirector Instance { get; private set; }

        public ScenarioService Service { get; } = new ScenarioService();

        /// <summary>Injected by the world bootstrap. Null-safe: bootstrap degrades loudly.</summary>
        public HouseholdLifecycleManager Lifecycle { get; set; }

        /// <summary>Injected by the world bootstrap. Null-safe.</summary>
        public TaskAuthority TaskAuthority { get; set; }

        /// <summary>Injected by the world bootstrap. Null-safe.</summary>
        public PlayerDirector PlayerDirector { get; set; }

        /// <summary>
        /// Resolves founder display names (from the scenario asset) to Person ids.
        /// Injected by the world bootstrap; when null, player-household declaration is
        /// skipped with a loud diagnostic (never silent).
        /// </summary>
        public Func<string, List<int>> FounderResolver { get; set; }

        public bool HasActiveScenario => Service.HasActiveScenario;
        public string ActiveScenarioId => Service.ActiveState?.Asset.ScenarioId;

        /// <summary>
        /// Registers an authored scenario for a runtime start screen. The asset remains
        /// the authority; this method only exposes the same registry operation that the
        /// serialized scene list performs during Awake.
        /// </summary>
        public bool RegisterScenarioAsset(ScenarioAsset asset)
        {
            if (asset == null)
            {
                return false;
            }

            if (ReferenceEquals(Service.GetScenario(asset.ScenarioId), asset))
            {
                return true;
            }

            return Service.RegisterScenario(asset, out string rejectionReason)
                || LogRegistrationFailure(rejectionReason);
        }

        private bool LogRegistrationFailure(string rejectionReason)
        {
            Debug.LogError($"[ScenarioDirector] {rejectionReason}", this);
            return false;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[ScenarioDirector] Duplicate director destroyed; keeping the first.", this);
                Destroy(this);
                return;
            }

            Instance = this;

            foreach (ScenarioAsset asset in scenarioRegistry)
            {
                if (asset == null)
                {
                    continue;
                }

                if (!Service.RegisterScenario(asset, out string rejectionReason))
                {
                    Debug.LogError($"[ScenarioDirector] {rejectionReason}", this);
                }
            }
        }

        private void Start()
        {
            if (!string.IsNullOrWhiteSpace(bootScenarioId))
            {
                SwitchScenario(bootScenarioId);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// DEV-1: in-scene scenario switch. No scene loads (DEV-4). Teardown ends the active
        /// run's runtime state (goals/objectives/tunable overrides evaporate — documented
        /// DEV-3 behavior); bootstrap validates the asset, declares the player household,
        /// seeds goals/objectives, and enqueues starting tasks.
        /// </summary>
        public bool SwitchScenario(string scenarioId)
        {
            if (Service.HasActiveScenario)
            {
                Service.EndScenario($"switched to '{scenarioId}'");
            }

            if (!Service.BeginScenario(scenarioId, out string rejectionReason))
            {
                Debug.LogError($"[ScenarioDirector] {rejectionReason}", this);
                return false;
            }

            ScenarioAsset asset = Service.GetScenario(scenarioId);

            // DEV-3 rule 1: scenario state must come from an asset, never memory-only.
            if (!DevGuards.RequireScenarioAsset(asset, this, nameof(SwitchScenario)))
            {
                return false;
            }

            BootstrapPlayerHousehold(asset);
            EnqueueStartingTasks(asset);

            Debug.Log($"[ScenarioDirector] Scenario '{scenarioId}' active. {Service.ActiveState.Diagnostics.Count} diagnostic(s).", this);
            foreach (string diagnostic in Service.ActiveState.Diagnostics)
            {
                Debug.LogWarning($"[ScenarioDirector] {diagnostic}", this);
            }

            return true;
        }

        private void BootstrapPlayerHousehold(ScenarioAsset asset)
        {
            PlayerHouseholdDeclaration declaration = asset.PlayerHousehold;
            if (declaration == null || !declaration.IsDeclared)
            {
                return;
            }

            if (Lifecycle == null)
            {
                Service.ActiveState.AddDiagnostic(
                    "Player household declared but HouseholdLifecycleManager is not injected — " +
                    "declaration skipped. Inject Lifecycle from the world bootstrap.");
                return;
            }

            if (FounderResolver == null)
            {
                Service.ActiveState.AddDiagnostic(
                    "Player household declared but FounderResolver is not injected — " +
                    "cannot resolve founder display names to Person ids. Declaration skipped.");
                return;
            }

            var founderIds = new List<int>();
            foreach (string displayName in declaration.FounderDisplayNames)
            {
                List<int> resolved = FounderResolver(displayName);
                if (resolved != null)
                {
                    founderIds.AddRange(resolved);
                }
            }

            if (founderIds.Count == 0)
            {
                Service.ActiveState.AddDiagnostic(
                    $"Player household '{declaration.HouseholdName}' declared but no founders resolved — " +
                    "check FounderResolver and founder display names.");
                return;
            }

            int dayIndex = TimeManager.Instance != null ? TimeManager.Instance.CurrentAbsoluteDayIndex : 0;
            Lifecycle.DeclarePlayerHousehold(declaration.ScenarioPlayerId, declaration.HouseholdName, founderIds, dayIndex);
            Service.ActiveState.AddDiagnostic(
                $"Player household '{declaration.HouseholdName}' declared with {founderIds.Count} founder(s) (GHOST-DEF-006).");
        }

        private void EnqueueStartingTasks(ScenarioAsset asset)
        {
            if (asset.StartingTaskDefinitionIds == null || asset.StartingTaskDefinitionIds.Count == 0)
            {
                return;
            }

            if (TaskAuthority == null)
            {
                Service.ActiveState.AddDiagnostic(
                    "Scenario lists starting tasks but TaskAuthority is not injected — tasks skipped.");
                return;
            }

            int dayIndex = TimeManager.Instance != null ? TimeManager.Instance.CurrentAbsoluteDayIndex : 0;
            foreach (string definitionId in asset.StartingTaskDefinitionIds)
            {
                TaskDefinition definition = TaskAuthority.GetDefinition(definitionId);
                if (definition == null)
                {
                    Service.ActiveState.AddDiagnostic($"Starting task definition '{definitionId}' is not registered — skipped.");
                    continue;
                }

                // Owner: the scenario run itself is not an entity; tasks are created unowned
                // (Invalid owner) and the puppet master assigns them. Documented, not silent.
                TaskAuthority.CreateTask(definitionId, LandLedgers.Primitives.EntityId.Invalid, dayIndex);
            }
        }

        // ---- Live-edit API (puppet master, DEV-2) ----

        /// <summary>
        /// P7: records a goal completion produced by live evaluation (e.g. the
        /// FirstLedgerGoalEvaluator ticking on SimulationDrivers). The completion
        /// lands in the scenario runtime state, so the puppet master panel and
        /// every other ScenarioRuntimeState reader see it — before P7 the
        /// evaluators only logged and the panel showed every goal incomplete
        /// forever. Guarded: applies only when the named scenario is the active
        /// one, so a stray evaluator tick can never mark goals on the wrong
        /// scenario. Unknown goal ids are rejected quietly (false) — evaluation
        /// runs every tick and must never spam the console.
        /// </summary>
        public bool RecordLiveGoalCompletion(string scenarioId, string goalId)
        {
            if (string.IsNullOrWhiteSpace(scenarioId) || string.IsNullOrWhiteSpace(goalId))
            {
                return false;
            }

            if (!HasActiveScenario
                || !string.Equals(ActiveScenarioId, scenarioId, StringComparison.Ordinal))
            {
                return false;
            }

            return Service.SetGoalCompleted(goalId, true, out _);
        }

        public bool SetGoalCompleted(string goalId, bool completed)
        {
            if (!Service.SetGoalCompleted(goalId, completed, out string rejectionReason))
            {
                Debug.LogError($"[ScenarioDirector] {rejectionReason}", this);
                return false;
            }

            return true;
        }

        public bool RetargetObjectiveText(string objectiveId, string newText)
        {
            if (!Service.RetargetObjectiveText(objectiveId, newText, out string rejectionReason))
            {
                Debug.LogError($"[ScenarioDirector] {rejectionReason}", this);
                return false;
            }

            return true;
        }

        public bool AddObjective(string objectiveId, string text, string linkedGoalId = "")
        {
            if (!Service.AddObjectiveRuntime(objectiveId, text, linkedGoalId, out string rejectionReason))
            {
                Debug.LogError($"[ScenarioDirector] {rejectionReason}", this);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Live value tweak. persistToAsset=false (default): runtime override, reverts at
        /// stop-play. persistToAsset=true: explicit audit-logged write-back to the asset.
        /// </summary>
        public bool SetTunable(string key, TunableValue value, bool persistToAsset = false)
        {
            if (!Service.SetTunable(key, value, persistToAsset, out string rejectionReason))
            {
                Debug.LogError($"[ScenarioDirector] {rejectionReason}", this);
                return false;
            }

            return true;
        }

        public TunableValue GetTunable(string key)
        {
            return Service.GetEffectiveTunable(key);
        }
    }
}
