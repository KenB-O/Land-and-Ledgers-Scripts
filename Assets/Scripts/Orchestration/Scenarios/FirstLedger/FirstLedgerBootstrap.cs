using System;
using System.Collections.Generic;
using System.Linq;
using LandLedgers.Economy;
using LandLedgers.Orchestration.Scenarios.Generation;
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

        /// <summary>
        /// Pass XI WS1: registers this scenario's dated qualifiers on
        /// the hub objective service from live evaluator state. The
        /// First Ledger has no authored deadlines, so every goal uses
        /// the no-deadline sentinel (it latches when met, never fails
        /// on time). Idempotent: safe on start, on restore, and on
        /// rebind. Also adopts fresh definitions for old saves whose
        /// run state carries no persisted registrations.
        /// </summary>
        public void RegisterObjectiveQualifiers()
        {
            if (systemsHub == null || evaluator == null)
            {
                Debug.LogWarning("[FirstLedgerBootstrap] Cannot register objective qualifiers — hub or evaluator missing.");
                return;
            }

            ScenarioObjectiveService objectives = systemsHub.ScenarioObjectives;
            if (objectives == null || !string.Equals(objectives.ScenarioId, FirstLedgerScenario.ScenarioId, StringComparison.Ordinal))
            {
                Debug.LogWarning("[FirstLedgerBootstrap] Objective service is not tracking the First Ledger — qualifiers not registered.");
                return;
            }

            var definitions = new List<ScenarioQualifierRegistration>();
            foreach (string goalId in FirstLedgerScenario.GoalIdsInOrder)
            {
                definitions.Add(new ScenarioQualifierRegistration
                {
                    GoalId = goalId,
                    DeadlineDayIndex = int.MaxValue,
                    PredicateKey = objectives.PredicateKeyFor(goalId),
                });
            }
            objectives.EnsureQualifierRegistrations(definitions);

            var livePredicates = new Dictionary<string, Func<bool>>();
            foreach (string goalId in FirstLedgerScenario.GoalIdsInOrder)
            {
                string capturedGoalId = goalId;
                livePredicates[capturedGoalId] = () => evaluator.CompletedGoalIds.Contains(capturedGoalId);
            }
            List<string> unrebuilt = objectives.RebuildQualifiers(livePredicates);
            foreach (string goalId in unrebuilt)
            {
                Debug.LogWarning($"[FirstLedgerBootstrap] Objective qualifier '{goalId}' could not be rebuilt from live goal state — it can neither latch nor fail until re-registered.");
            }
        }

        /// <summary>
        /// Pass XI WS1: reconciles the three goal stores (director
        /// state, evaluator list, hub latched list) to their union and
        /// writes the union back to all three, logging every
        /// disagreement. Never un-completes a goal, never un-fails a run.
        /// </summary>
        private void ReconcileGoalStores()
        {
            if (scenarioDirector == null || systemsHub == null || evaluator == null)
            {
                return;
            }

            ScenarioSaveDto directorDto = scenarioDirector.CaptureSaveDto();
            ScenarioGoalStoreReconciler.Reconciliation reconciliation =
                ScenarioGoalStoreReconciler.Reconcile(
                    directorDto.completedGoalIds,
                    evaluator.CompletedGoalIds,
                    systemsHub.ScenarioObjectives.State.LatchedCompleteGoalIds);
            foreach (string diagnostic in reconciliation.Diagnostics)
            {
                Debug.LogWarning($"[FirstLedgerBootstrap] {diagnostic}");
            }

            evaluator.RestoreCompletedGoals(reconciliation.CompletedGoals);
            foreach (string goalId in reconciliation.CompletedGoals)
            {
                scenarioDirector.RecordLiveGoalCompletion(FirstLedgerScenario.ScenarioId, goalId);
            }
            systemsHub.ScenarioObjectives.MergeLatchedGoals(reconciliation.CompletedGoals);
        }

        public ScenarioSaveDto CaptureSaveDto()
        {
            ScenarioSaveDto dto = scenarioDirector != null ? scenarioDirector.CaptureSaveDto() : new ScenarioSaveDto();
            // Pass XI WS1 capture order: reconcile first, then write all
            // three stores from the reconciled state. The hub capture
            // that follows in SaveLoadManager then carries the same union.
            if (scenarioDirector != null && scenarioDirector.HasActiveScenario
                && string.Equals(scenarioDirector.ActiveScenarioId, FirstLedgerScenario.ScenarioId, StringComparison.Ordinal)
                && evaluator != null)
            {
                ReconcileGoalStores();
                dto.completedGoalIds = new List<string>(evaluator.CompletedGoalIds);
            }
            else if (evaluator != null
                && (scenarioDirector == null || !scenarioDirector.HasActiveScenario))
            {
                // Director-less capture keeps the historical evaluator
                // overwrite. When another scenario is active, the
                // director dto stands and that scenario's bootstrap
                // capture owns the evaluator list.
                dto.completedGoalIds = new List<string>(evaluator.CompletedGoalIds);
            }
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

            // Pass XI WS1 restore order: director state, then evaluator
            // list, then hub latched list, reconciled to one union, then
            // qualifiers rebuilt from the persisted registrations. The
            // evaluator restore is skipped when the restored scenario is
            // not this one (a Saloon save must not seed First Ledger goals).
            if (!string.Equals(scenarioDirector.ActiveScenarioId, FirstLedgerScenario.ScenarioId, StringComparison.Ordinal))
            {
                return;
            }

            evaluator?.RestoreCompletedGoals(dto.completedGoalIds);
            ReconcileGoalStores();
            RegisterObjectiveQualifiers();
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

            // Pass XI WS1: the director's objective hook needs the
            // hub-owned service, and qualifier registration happens on
            // the ScenarioStarted event after BeginScenario clears the
            // previous run's qualifiers.
            if (scenarioDirector != null && systemsHub != null)
            {
                scenarioDirector.ObjectiveService ??= systemsHub.ScenarioObjectives;
                scenarioDirector.ScenarioStarted += OnScenarioStarted;

                // Pass XI WS6 first-gate (M2): ScenarioStarted fires
                // once. A bootstrap awakening after the first start
                // subscribed too late and its qualifier list stayed
                // empty forever. If the director already runs this
                // scenario, register now; the guard inside
                // RegisterObjectiveQualifiers additionally requires
                // the hub service to track this scenario, and
                // registration is idempotent (Ensure adopts only when
                // no registrations exist; Rebuild clear-and-rebuilds
                // from persisted registrations), so a later
                // ScenarioStarted re-registration cannot duplicate
                // qualifiers.
                if (scenarioDirector.HasActiveScenario
                    && string.Equals(scenarioDirector.ActiveScenarioId, FirstLedgerScenario.ScenarioId, StringComparison.Ordinal))
                {
                    RegisterObjectiveQualifiers();
                }
            }

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

            if (scenarioDirector != null)
            {
                scenarioDirector.ScenarioStarted -= OnScenarioStarted;
            }

            if (drivers != null)
            {
                drivers.OnScenarioTick -= RunEvaluation;
            }
        }

        private void OnScenarioStarted(string scenarioId)
        {
            if (!string.Equals(scenarioId, FirstLedgerScenario.ScenarioId, StringComparison.Ordinal))
            {
                return;
            }

            RegisterObjectiveQualifiers();
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
