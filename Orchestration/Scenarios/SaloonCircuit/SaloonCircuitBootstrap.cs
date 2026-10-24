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
        /// Pass XI WS6 first-gate (M4): the union is written to the
        /// evaluator as HISTORICAL goals only. A hub-latched final-state
        /// id that no longer holds live must not be written into
        /// predicateHolding (the old RestoreState(union, union) did
        /// exactly that, resurrecting lapsed finals into the saved file
        /// on every save); finals are re-derived by live evaluation.
        /// The hub latched list stays sticky by design (A-14, unchanged),
        /// and director state is only ever marked for goals the
        /// evaluator currently counts as complete — reconcile never
        /// ADDS a lapsed final to the director, though it never un-marks
        /// one live play already set there.
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

            evaluator.RestoreHistoricalGoals(reconciliation.CompletedGoals);
            foreach (string goalId in reconciliation.CompletedGoals)
            {
                if (evaluator.GoalHolds(goalId))
                {
                    scenarioDirector.RecordLiveGoalCompletion(SaloonCircuitScenario.ScenarioId, goalId);
                }
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
                Debug.LogError("[SaloonCircuitBootstrap] Restore skipped: no save dto or no scenario director — Saloon goals cannot be restored.");
                return;
            }

            // Pass XI WS6 first-gate (M1): this restore is
            // self-sufficient. It used to assume the First Ledger
            // bootstrap had already run the director restore
            // (SaveLoadManager order), so a scene holding the Saloon
            // bootstrap without the First Ledger bootstrap never ran
            // director restore at all, and evaluator restore,
            // reconciliation, and qualifier rebuild silently no-op'd,
            // dropping saved Saloon goals on load. When the director
            // has no active scenario, run its restore here; when it
            // already runs a scenario (e.g. the First Ledger restore
            // ran first in SaveLoadManager order), never re-run it.
            if (!scenarioDirector.HasActiveScenario)
            {
                if (!scenarioDirector.RestoreFromSaveDto(dto))
                {
                    Debug.LogError("[SaloonCircuitBootstrap] Restore skipped: director restore failed — Saloon evaluator, reconciliation, and qualifier rebuild did not run, so saved Saloon goals are NOT restored.");
                    return;
                }
            }

            // Only the Saloon evaluator, reconciliation, and qualifier
            // rebuild belong here, and only when the Saloon Circuit is
            // the restored scenario. A different restored scenario is
            // a loud skip, not a silent one: a Saloon save must not
            // seed another scenario's goals, and vice versa.
            if (!string.Equals(scenarioDirector.ActiveScenarioId, SaloonCircuitScenario.ScenarioId, StringComparison.Ordinal))
            {
                Debug.LogError($"[SaloonCircuitBootstrap] Restore skipped: restored scenario is '{scenarioDirector.ActiveScenarioId ?? "<none>"}', not the Saloon Circuit — evaluator restore, reconciliation, and qualifier rebuild did not run.");
                return;
            }

            // Pass XI WS6 first-gate (M4): the saved list restores
            // historical goals only. Final-state ids in the file must
            // not be filed into predicateHolding — a lapsed final
            // (e.g. crown-rank-one) is re-derived by live evaluation,
            // never resurrected from the file. ReconcileGoalStores
            // below applies the same rule to the three-store union.
            evaluator?.RestoreHistoricalGoals(dto.completedGoalIds);
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
