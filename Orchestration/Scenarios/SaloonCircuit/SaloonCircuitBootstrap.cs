using System;
using System.Collections.Generic;
using LandLedgers.Core.Time;
using LandLedgers.Economy;
using LandLedgers.Orchestration.Systems;
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

            if (drivers != null)
            {
                drivers.OnScenarioTick = RunEvaluation;
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

            if (drivers != null && drivers.OnScenarioTick == (System.Action)RunEvaluation)
            {
                drivers.OnScenarioTick = null;
            }
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
        }
    }
}
