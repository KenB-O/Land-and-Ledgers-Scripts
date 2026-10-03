using System;
using LandLedgers.Orchestration.Player;
using LandLedgers.Orchestration.Scenarios;
using LandLedgers.Skills;
using LandLedgers.Time;
using UnityEngine;

namespace LandLedgers.Orchestration.Systems
{
    /// <summary>
    /// CLN-2: thin per-tick drivers for the simulation authorities. Kennedy attaches
    /// this to the one scene (next to the SimulationSystemsHub). The drivers own no
    /// logic — they subscribe to the TimeManager's tick events and forward to the
    /// authorities owned by the hub.
    ///
    /// What goes where:
    /// - Awake: installs the skill-based duration estimator into the TaskAuthority
    ///   (TTS-3) and hands the hub's authorities to the ScenarioDirector.
    /// - DayChanged: WorkTimeBudgetStore.EnsureDay (TTS-1 daily rollover) and
    ///   ButcherRuntime.AgeLotsToDay (BIZ-4 perishability).
    /// - ShortTick: PlayerDirector.RecordMovementProgress (whole minutes elapsed).
    /// - TaskAuthority.RecordWork + SkillService.ApplyPracticeFromTask: called from
    ///   task execution sites (not a tick) — see DRIVERS.md.
    /// - Freight/butcher AdvanceGameSeconds: shipments advance inside
    ///   LogisticsRuntimeManager and FreightCompanyRuntime (already MonoBehaviours).
    /// - Scenario goal evaluation: FirstLedgerGoalEvaluator.Evaluate runs on the
    ///   scenario tick once CLN-3 provides the live IFirstLedgerGameState.
    /// </summary>
    public sealed class SimulationDrivers : MonoBehaviour
    {
        [SerializeField, Tooltip("CLN-1 hub owning the authorities. Found automatically if empty.")]
        private SimulationSystemsHub hub;

        [SerializeField, Tooltip("Found automatically if empty.")]
        private TimeManager timeManager;

        [SerializeField, Tooltip("Optional: receives the hub's TaskAuthority. Found automatically if empty.")]
        private ScenarioDirector scenarioDirector;

        [SerializeField, Tooltip("Optional: driven with whole minutes per ShortTick. Found automatically if empty.")]
        private PlayerDirector playerDirector;

        /// <summary>
        /// CLN-3 hook: scenario goal evaluation runs here. The bootstrap assigns this
        /// (e.g. FirstLedgerGoalEvaluator.Evaluate with the live game state); when
        /// null, the scenario tick is a no-op.
        /// </summary>
        public Action OnScenarioTick { get; set; }

        private void Awake()
        {
            hub ??= FindAnyObjectByType<SimulationSystemsHub>();
            timeManager ??= TimeManager.Instance != null ? TimeManager.Instance : FindAnyObjectByType<TimeManager>();
            scenarioDirector ??= FindAnyObjectByType<ScenarioDirector>();
            playerDirector ??= FindAnyObjectByType<PlayerDirector>();

            if (hub == null)
            {
                Debug.LogWarning("[SimulationDrivers] No SimulationSystemsHub found — drivers idle.");
                return;
            }

            // TTS-3: skill shaves task time. Installed once; the authority owns it after.
            hub.Tasks.SetDurationEstimator(new SkillTaskDurationEstimator(hub.Skills));

            if (scenarioDirector != null)
            {
                scenarioDirector.TaskAuthority = hub.Tasks;
                scenarioDirector.PlayerDirector = playerDirector;
            }
        }

        private void OnEnable()
        {
            if (timeManager != null)
            {
                timeManager.DayChanged += OnDayChanged;
                timeManager.ShortTick += OnShortTick;
            }
        }

        private void OnDisable()
        {
            if (timeManager != null)
            {
                timeManager.DayChanged -= OnDayChanged;
                timeManager.ShortTick -= OnShortTick;
            }
        }

        private void OnDayChanged(SimulationDateChangedContext context)
        {
            if (hub == null || timeManager == null)
            {
                return;
            }

            int absoluteDayIndex = timeManager.CurrentAbsoluteDayIndex;

            // TTS-1: roll every person's daily work-time budget.
            hub.WorkTimeBudgets.EnsureDay(absoluteDayIndex);

            // BIZ-4: age butcher lots (fresh → aging → spoiled).
            foreach (var runtime in hub.ButcherRuntimes)
            {
                runtime?.AgeLotsToDay(absoluteDayIndex);
            }
        }

        private void OnShortTick(SimulationTickContext context)
        {
            if (hub == null)
            {
                return;
            }

            // Player travel advances with the clock (whole minutes only).
            if (playerDirector != null)
            {
                int wholeMinutes = Mathf.FloorToInt(context.TickIntervalGameSeconds / 60f);
                if (wholeMinutes > 0)
                {
                    playerDirector.RecordMovementProgress(wholeMinutes, hub.WorkTimeBudgets);
                }
            }

            // Scenario goal evaluation (CLN-3 assigns OnScenarioTick).
            if (scenarioDirector != null && scenarioDirector.HasActiveScenario)
            {
                OnScenarioTick?.Invoke();
            }
        }
    }
}
