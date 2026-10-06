using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Persistence;
using UnityEngine;

namespace LandLedgers.Orchestration.Scenarios.FirstLedger
{
    /// <summary>
    /// CLN-3: wires the First Ledger scenario's goal evaluator to live game state.
    /// Kennedy adds this to the one scene (or it finds everything on Awake). The
    /// evaluator reads the real registries through <see cref="FirstLedgerGameState"/>:
    /// the business registry, the employment registry, and the BIZ-5 valuation read
    /// model. Owner equity comes from valuation, never cash (Canon §11.4).
    ///
    /// GHOST-DEF-006: the FirstLedger.asset founder-naming stays Kennedy's in-editor
    /// step — name the household founders on the asset before bootstrapping. This
    /// bootstrap does not invent founders.
    /// </summary>
    public sealed class FirstLedgerBootstrap : MonoBehaviour
    {
        [SerializeField, Tooltip("Found automatically if empty.")]
        private ScenarioDirector scenarioDirector;

        [SerializeField, Tooltip("Found automatically if empty.")]
        private SimulationSystemsHub systemsHub;

        [SerializeField, Tooltip("Found automatically if empty.")]
        private SimulationDrivers drivers;

        [SerializeField, Tooltip("Found automatically if empty.")]
        private SharedBusinessRuntimeManager sharedBusinessRuntime;

        private FirstLedgerGoalEvaluator evaluator;
        private FirstLedgerGameState gameState;

        public FirstLedgerGoalEvaluator Evaluator => evaluator;

        /// <summary>
        /// Runs the live evaluator immediately and returns the authoritative scenario
        /// completion state. This is used by the HUD/save boundary and by deterministic
        /// production playthroughs; it never mutates a goal directly.
        /// </summary>
        public bool EvaluateNow()
        {
            RunEvaluation();
            return evaluator != null && evaluator.IsScenarioComplete;
        }

        public bool IsScenarioComplete => evaluator != null && evaluator.IsScenarioComplete;
        public int PlayerOwnedBusinessCount => gameState != null ? gameState.PlayerOwnedBusinessCount : 0;
        public int ActivePlayerEmployeeCount => gameState != null ? gameState.ActivePlayerEmployeeCount : 0;
        public int PlayerOwnerEquityCents => gameState != null ? gameState.PlayerOwnerEquityCents : 0;

        public ScenarioSaveDto CaptureSaveDto()
        {
            ScenarioSaveDto dto = scenarioDirector != null ? scenarioDirector.CaptureSaveDto() : new ScenarioSaveDto();
            dto.completedGoalIds = evaluator != null ? new List<string>(evaluator.CompletedGoalIds) : dto.completedGoalIds;
            return dto;
        }

        public void RestoreFromSaveDto(ScenarioSaveDto dto)
        {
            if (dto == null || scenarioDirector == null)
            {
                return;
            }

            if (!scenarioDirector.RestoreFromSaveDto(dto))
            {
                return;
            }

            evaluator?.RestoreCompletedGoals(dto.completedGoalIds);
        }

        private void Awake()
        {
            scenarioDirector ??= FindAnyObjectByType<ScenarioDirector>();
            systemsHub ??= FindAnyObjectByType<SimulationSystemsHub>();
            drivers ??= FindAnyObjectByType<SimulationDrivers>();
            sharedBusinessRuntime ??= FindAnyObjectByType<SharedBusinessRuntimeManager>();

            if (systemsHub == null)
            {
                Debug.LogWarning("[FirstLedgerBootstrap] No SimulationSystemsHub — goal evaluation idle.");
                return;
            }

            gameState = new FirstLedgerGameState(
                () => sharedBusinessRuntime != null
                    ? sharedBusinessRuntime.Businesses
                    : new List<BusinessInstanceState>(),
                systemsHub.Employments,
                systemsHub.Valuation);

            // Scenario evaluation and production hiring must share the same
            // relationship registry even when Unity awakens the business manager
            // before SimulationDrivers. This is an authority handoff, not a second
            // employment store.
            if (sharedBusinessRuntime != null)
            {
                sharedBusinessRuntime.EmploymentRegistry = systemsHub.Employments;
                sharedBusinessRuntime.WireEmploymentRegistryToBusinesses();
            }

            evaluator = new FirstLedgerGoalEvaluator();
            evaluator.GoalCompleted += OnGoalCompleted;

            if (drivers != null)
            {
                // P7: multicast — a second scenario bootstrap in the scene must
                // not silently steal the tick from this evaluator.
                drivers.OnScenarioTick += RunEvaluation;
            }
            else
            {
                Debug.LogWarning("[FirstLedgerBootstrap] No SimulationDrivers — evaluator will not tick. " +
                    "Assign SimulationDrivers.OnScenarioTick manually.");
            }
        }

        private void OnDestroy()
        {
            if (evaluator != null)
            {
                evaluator.GoalCompleted -= OnGoalCompleted;
            }

            if (drivers != null)
            {
                drivers.OnScenarioTick -= RunEvaluation;
            }
        }

        private void RunEvaluation()
        {
            // Unity execution order can awaken this scenario component before the
            // runtime business manager. Resolve the existing authority lazily rather
            // than freezing an empty closure for the lifetime of the scene.
            sharedBusinessRuntime ??= FindAnyObjectByType<SharedBusinessRuntimeManager>();
            if (evaluator == null || gameState == null)
            {
                return;
            }

            List<string> newlyCompleted = evaluator.Evaluate(gameState);
            if (newlyCompleted.Count > 0)
            {
                Debug.Log($"[FirstLedger] Goals completed: {string.Join(", ", newlyCompleted)}");
            }
        }

        private void OnGoalCompleted(string goalId)
        {
            Debug.Log($"[FirstLedger] Goal completed: {goalId}");
            // P7: live completions reach the scenario runtime state — the puppet
            // master panel reads ScenarioRuntimeState, not this evaluator.
            // Guarded by scenario id: never marks goals on another scenario.
            scenarioDirector?.RecordLiveGoalCompletion(FirstLedgerScenario.ScenarioId, goalId);
        }
    }
}
