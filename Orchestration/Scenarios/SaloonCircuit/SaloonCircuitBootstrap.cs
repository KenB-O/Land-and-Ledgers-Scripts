using System;
using System.Collections.Generic;
using LandLedgers.Time;
using LandLedgers.Economy;
using LandLedgers.Orchestration.Scenarios.Generation;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Persistence;
using UnityEngine;

namespace LandLedgers.Orchestration.Scenarios.SaloonCircuit
{
    /// <summary>
    /// T3B: wires the Saloon Circuit scenario's goal evaluator to live game
    /// state. Mirrors the CLN-3 FirstLedgerBootstrap pattern. Kennedy adds
    /// this to the one scene (or it finds everything on Awake).
    ///
    /// The saloon runtimes, traffic-rank source, integrated-sale provenance
    /// query, and scenario debt source are Unity-side wiring — assign the
    /// delegates in the inspector or let the bootstrap find the T2G runtimes.
    /// Unwired inputs read as honest negatives, never invented positives.
    /// </summary>
    public sealed class SaloonCircuitBootstrap : MonoBehaviour
    {
        [SerializeField, Tooltip("Found automatically if empty.")]
        private ScenarioDirector scenarioDirector;

        [SerializeField, Tooltip("Found automatically if empty.")]
        private SimulationSystemsHub systemsHub;

        [SerializeField, Tooltip("Found automatically if empty.")]
        private SimulationDrivers drivers;

        [SerializeField, Tooltip("Found automatically if empty.")]
        private SharedBusinessRuntimeManager sharedBusinessRuntime;

        [SerializeField, Tooltip("Found automatically if empty.")]
        private TimeManager timeManager;

        private SaloonCircuitGoalEvaluator evaluator;
        private SaloonCircuitGameState gameState;

        public SaloonCircuitGoalEvaluator Evaluator => evaluator;

        /// <summary>
        /// Pass XI WS1: registers this scenario's dated qualifiers on
        /// the hub objective service from live evaluator state. The two
        /// authored deadlines (Ash Creek day 365, Prairie Crossing day
        /// 730) come from SaloonCircuitScenario.DeadlineDayByGoal; every
        /// other goal uses the no-deadline sentinel. A qualifier's live
        /// predicate is the evaluator's own completion state for that
        /// goal, so the dated tracker can never disagree with the
        /// evaluator about whether the goal is met. Idempotent.
        /// </summary>
        public void RegisterObjectiveQualifiers()
        {
            if (systemsHub == null || evaluator == null)
            {
                Debug.LogWarning("[SaloonCircuitBootstrap] Cannot register objective qualifiers — hub or evaluator missing.");
                return;
            }

            ScenarioObjectiveService objectives = systemsHub.ScenarioObjectives;
            if (objectives == null || !string.Equals(objectives.ScenarioId, SaloonCircuitScenario.ScenarioId, StringComparison.Ordinal))
            {
                Debug.LogWarning("[SaloonCircuitBootstrap] Objective service is not tracking the Saloon Circuit — qualifiers not registered.");
                return;
            }

            var definitions = new List<ScenarioQualifierRegistration>();
            foreach (string goalId in SaloonCircuitScenario.GoalIdsInOrder)
            {
                int deadline = SaloonCircuitScenario.DeadlineDayByGoal.TryGetValue(goalId, out int day)
                    ? day : int.MaxValue;
                definitions.Add(new ScenarioQualifierRegistration
                {
                    GoalId = goalId,
                    DeadlineDayIndex = deadline,
                    PredicateKey = objectives.PredicateKeyFor(goalId),
                });
            }
            objectives.EnsureQualifierRegistrations(definitions);

            var livePredicates = new Dictionary<string, Func<bool>>();
            foreach (string goalId in SaloonCircuitScenario.GoalIdsInOrder)
            {
                string capturedGoalId = goalId;
                livePredicates[capturedGoalId] = () => evaluator.GoalHolds(capturedGoalId);
            }
            List<string> unrebuilt = objectives.RebuildQualifiers(livePredicates);
            foreach (string goalId in unrebuilt)
            {
                Debug.LogWarning($"[SaloonCircuitBootstrap] Objective qualifier '{goalId}' could not be rebuilt from live goal state — it can neither latch nor fail until re-registered.");
            }
        }

        /// <summary>
        /// Pass XI WS1: reconciles the three goal stores to their union
        /// and writes the union back to all three, logging disagreements.
        /// Never un-completes a goal, never un-fails a run.
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
                    evaluator.CompletedGoalIds(),
                    systemsHub.ScenarioObjectives.State.LatchedCompleteGoalIds);
            foreach (string diagnostic in reconciliation.Diagnostics)
            {
                Debug.LogWarning($"[SaloonCircuitBootstrap] {diagnostic}");
            }

            evaluator.RestoreState(reconciliation.CompletedGoals, reconciliation.CompletedGoals);
            foreach (string goalId in reconciliation.CompletedGoals)
            {
                scenarioDirector.RecordLiveGoalCompletion(SaloonCircuitScenario.ScenarioId, goalId);
            }
            systemsHub.ScenarioObjectives.MergeLatchedGoals(reconciliation.CompletedGoals);
        }

        public ScenarioSaveDto CaptureSaveDto()
        {
            ScenarioSaveDto dto = scenarioDirector != null ? scenarioDirector.CaptureSaveDto() : new ScenarioSaveDto();
            if (scenarioDirector != null && scenarioDirector.HasActiveScenario
                && string.Equals(scenarioDirector.ActiveScenarioId, SaloonCircuitScenario.ScenarioId, StringComparison.Ordinal)
                && evaluator != null)
            {
                ReconcileGoalStores();
                dto.completedGoalIds = evaluator.CompletedGoalIds();
            }
            return dto;
        }

        public void RestoreFromSaveDto(ScenarioSaveDto dto)
        {
            if (dto == null || scenarioDirector == null)
            {
                return;
            }

            // The First Ledger bootstrap already ran the director
            // restore (SaveLoadManager order); this method only owns
            // the Saloon evaluator, reconciliation, and qualifier
            // rebuild, and only when the Saloon Circuit is active.
            if (!string.Equals(scenarioDirector.ActiveScenarioId, SaloonCircuitScenario.ScenarioId, StringComparison.Ordinal))
            {
                return;
            }

            // The flat completed list splits itself: RestoreState files
            // historical ids as milestones and the rest as holding.
            evaluator?.RestoreState(dto.completedGoalIds, dto.completedGoalIds);
            ReconcileGoalStores();
            RegisterObjectiveQualifiers();
        }

        /// <summary>
        /// Unity-side wiring point: assign to feed the traffic-rank predicate
        /// from the real rank system when it exists.
        /// </summary>
        public Func<bool> CrownRankOneSource;

        /// <summary>
        /// Unity-side wiring point: assign to detect integrated sales through
        /// the real provenance chain (Canon Part XV).
        /// </summary>
        public Func<bool> IntegratedSaleSource;

        /// <summary>
        /// Unity-side wiring point: assign per-saloon distinct internally
        /// supplied product counts from the real sales/provenance records.
        /// </summary>
        public Func<string, int> DistinctInternalProductsSource;

        /// <summary>
        /// Unity-side wiring point: assign to read qualifying scenario
        /// principal debt from the T2A/SWN-3 liability ledger.
        /// </summary>
        public Func<int> ScenarioDebtSource;

        private void Awake()
        {
            scenarioDirector ??= FindAnyObjectByType<ScenarioDirector>();
            systemsHub ??= FindAnyObjectByType<SimulationSystemsHub>();
            drivers ??= FindAnyObjectByType<SimulationDrivers>();
            sharedBusinessRuntime ??= FindAnyObjectByType<SharedBusinessRuntimeManager>();
            timeManager ??= FindAnyObjectByType<TimeManager>();

            if (systemsHub == null)
            {
                Debug.LogWarning("[SaloonCircuitBootstrap] No SimulationSystemsHub — goal evaluation idle.");
                return;
            }

            gameState = new SaloonCircuitGameState(
                businessSource: () => sharedBusinessRuntime != null
                    ? sharedBusinessRuntime.Businesses
                    : new List<BusinessInstanceState>(),
                integratedSaleSource: IntegratedSaleSource,
                distinctInternalProductsSource: DistinctInternalProductsSource,
                crownRankOneSource: CrownRankOneSource,
                scenarioDebtSource: ScenarioDebtSource,
                dayIndexSource: timeManager != null
                    ? (Func<int>)(() => timeManager.CurrentAbsoluteDayIndex)
                    : null);

            evaluator = new SaloonCircuitGoalEvaluator();
            evaluator.GoalCompleted += OnGoalCompleted;

            // Pass XI WS1: same director objective hook as First Ledger.
            if (scenarioDirector != null && systemsHub != null)
            {
                scenarioDirector.ObjectiveService ??= systemsHub.ScenarioObjectives;
                scenarioDirector.ScenarioStarted += OnScenarioStarted;
            }

            if (drivers != null)
            {
                // P7: multicast — a second scenario bootstrap in the scene must
                // not silently steal the tick from this evaluator.
                drivers.OnScenarioTick += RunEvaluation;
            }
            else
            {
                Debug.LogWarning("[SaloonCircuitBootstrap] No SimulationDrivers — evaluator will not tick. " +
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
            if (!string.Equals(scenarioId, SaloonCircuitScenario.ScenarioId, StringComparison.Ordinal))
            {
                return;
            }

            RegisterObjectiveQualifiers();
        }

        private void RunEvaluation()
        {
            if (evaluator == null || gameState == null)
            {
                return;
            }

            List<string> newlyCompleted = evaluator.Evaluate(gameState);
            if (newlyCompleted.Count > 0)
            {
                Debug.Log($"[SaloonCircuit] Goals completed: {string.Join(", ", newlyCompleted)}");
            }
        }

        private void OnGoalCompleted(string goalId)
        {
            Debug.Log($"[SaloonCircuit] Goal completed: {goalId}");
            // P7: live completions reach the scenario runtime state — the puppet
            // master panel reads ScenarioRuntimeState, not this evaluator.
            // Guarded by scenario id: never marks goals on another scenario.
            scenarioDirector?.RecordLiveGoalCompletion(SaloonCircuitScenario.ScenarioId, goalId);
        }
    }
}
