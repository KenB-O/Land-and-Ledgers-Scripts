using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Orchestration.Player;
using LandLedgers.Orchestration.Scenarios;
using LandLedgers.Population;
using LandLedgers.ReadModels.Valuation;
using LandLedgers.Skills;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
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
    /// - NX-2C drive-by: DailyNeedsService.ExecuteDay runs on DayChanged with
    ///   the hub's WorkTimeBudgetStore (it had no live caller — the NX-1B
    ///   nutrition teeth never bit). Needs a PopulationManager in the scene and
    ///   SimulationDrivers.Journeys assigned by bootstrap; supplier registration
    ///   is scene wiring (T1A pattern).
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

        [SerializeField, Tooltip("Optional: weekly profit posts hook into its reset. Found automatically if empty.")]
        private SharedBusinessRuntimeManager sharedBusinessRuntime;

        [SerializeField, Tooltip("Optional: drives DailyNeedsService. Found automatically if empty.")]
        private PopulationManager populationManager;

        /// <summary>
        /// NX-2C: the journey model for embodied needs execution. Assigned by
        /// scene bootstrap (like OnScenarioTick) — without it, daily needs
        /// cannot execute embodied purchases and are skipped with a warning.
        /// </summary>
        public JourneyModel Journeys { get; set; }

        // NX-2C drive-by: daily-needs execution state (owned by the driver).
        private DailyNeedsService dailyNeedsService;
        private HouseholdConsumptionPlanner consumptionPlanner;
        private SupplierDirectory supplierDirectory;
        private EmbodiedPurchaseExecutor purchaseExecutor;

        /// <summary>
        /// CLN-3 hook: scenario goal evaluation runs here. The bootstrap assigns this
        /// (e.g. FirstLedgerGoalEvaluator.Evaluate with the live game state); when
        /// null, the scenario tick is a no-op.
        /// </summary>
        public Action OnScenarioTick { get; set; }

        // CLN-4: accumulated player worked minutes for the current week (owner labor).
        private int weeklyPlayerWorkedMinutes;

        private void Awake()
        {
            hub ??= FindAnyObjectByType<SimulationSystemsHub>();
            timeManager ??= TimeManager.Instance != null ? TimeManager.Instance : FindAnyObjectByType<TimeManager>();
            scenarioDirector ??= FindAnyObjectByType<ScenarioDirector>();
            // PlayerDirector is a plain C# authority, not a UnityEngine.Object; scene
            // auto-discovery is not available here. Bootstrap code may assign it.
            sharedBusinessRuntime ??= FindAnyObjectByType<SharedBusinessRuntimeManager>();
            populationManager ??= FindAnyObjectByType<PopulationManager>();

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

            // CLN-4: weekly profit posts to the BIZ-5 valuation read model from the real
            // settlement path (event-fed, never per-frame).
            if (sharedBusinessRuntime != null)
            {
                sharedBusinessRuntime.PreWeeklyResetCallback = PostWeeklyProfitToValuation;
            }
        }

        private void OnEnable()
        {
            if (timeManager != null)
            {
                timeManager.DayChanged += OnDayChanged;
                timeManager.WeekChanged += OnWeekChanged;
                timeManager.ShortTick += OnShortTick;
            }
        }

        private void OnDisable()
        {
            if (timeManager != null)
            {
                timeManager.DayChanged -= OnDayChanged;
                timeManager.WeekChanged -= OnWeekChanged;
                timeManager.ShortTick -= OnShortTick;
            }

            if (sharedBusinessRuntime != null
                && sharedBusinessRuntime.PreWeeklyResetCallback == (Action<BusinessInstanceState>)PostWeeklyProfitToValuation)
            {
                sharedBusinessRuntime.PreWeeklyResetCallback = null;
            }
        }

        /// <summary>
        /// CLN-4: posts a business's weekly net to the valuation read model before the
        /// settlement reset clears it. Called from the real weekly settlement path.
        /// </summary>
        private void PostWeeklyProfitToValuation(BusinessInstanceState business)
        {
            if (hub == null || business == null || business.RuntimeState == null)
            {
                return;
            }

            hub.Valuation.RecordWeeklyProfit(business.InstanceId, business.RuntimeState.WeekToDateNetCents);
        }

        private void OnWeekChanged(SimulationDateChangedContext context)
        {
            if (hub == null)
            {
                return;
            }

            // CLN-4: owner labor — the player's accumulated weekly worked minutes feed
            // the valuation for each player-owned business (real work-time path).
            if (playerDirector != null && weeklyPlayerWorkedMinutes > 0
                && sharedBusinessRuntime != null)
            {
                float weeklyHours = weeklyPlayerWorkedMinutes / 60f;
                foreach (BusinessInstanceState business in sharedBusinessRuntime.Businesses)
                {
                    if (business != null && business.Owner != null
                        && business.Owner.OwnerKind == BusinessOwnerKind.Player)
                    {
                        hub.Valuation.RegisterBusiness(business.InstanceId, "player", true);
                        // NX-1B: owner labor at a resolved replacement rate —
                        // local payroll evidence first, documented calibration
                        // fallback; never silently zero.
                        OwnerLaborRateResolver.ResolvedRate rate =
                            OwnerLaborRateResolver.Resolve(hub.Employments, business.InstanceId);
                        hub.Valuation.RecordOwnerLabor(business.InstanceId, weeklyHours, rate.CentsPerHour, 0);
                        if (rate.Source == OwnerLaborRateResolver.RateSource.CalibrationFallback)
                        {
                            Debug.LogWarning($"[SimulationDrivers] owner-labor rate for {business.InstanceId}: {rate.BasisNote}");
                        }
                    }
                }
            }

            weeklyPlayerWorkedMinutes = 0;
        }

        private void OnDayChanged(SimulationDateChangedContext context)
        {
            if (hub == null || timeManager == null)
            {
                return;
            }

            int absoluteDayIndex = timeManager.CurrentAbsoluteDayIndex;

            // CLN-4: accumulate the player's daily worked minutes for the weekly
            // owner-labor valuation post. Read BEFORE EnsureDay resets the day.
            if (playerDirector != null)
            {
                var budget = hub.WorkTimeBudgets.GetOrCreate(playerDirector.PlayerPersonId);
                weeklyPlayerWorkedMinutes += budget.MinutesWorked;
            }

            // TTS-1: roll every person's daily work-time budget.
            hub.WorkTimeBudgets.EnsureDay(absoluteDayIndex);

            // BIZ-4: age butcher lots (fresh → aging → spoiled).
            foreach (var runtime in hub.ButcherRuntimes)
            {
                runtime?.AgeLotsToDay(absoluteDayIndex);
            }

            // NX-2C drive-by: DailyNeedsService.ExecuteDay had NO live caller —
            // the NX-1B nutrition teeth never bit. Wire it here with the
            // work-time budgets so missed meals reduce usable minutes for real.
            DriveDailyNeeds(absoluteDayIndex);
        }

        private void DriveDailyNeeds(int absoluteDayIndex)
        {
            if (hub == null) return;
            if (populationManager == null || populationManager.State == null)
            {
                return; // no population yet — nothing to feed
            }
            if (Journeys == null)
            {
                Debug.LogWarning("[SimulationDrivers] DailyNeedsService skipped: no JourneyModel assigned " +
                    "(assign SimulationDrivers.Journeys in scene bootstrap — see DRIVERS.md).");
                return;
            }

            dailyNeedsService ??= new DailyNeedsService();
            consumptionPlanner ??= new HouseholdConsumptionPlanner();
            // SupplierDirectory starts empty: scene wiring registers suppliers
            // (T1A pattern — the general store registers its stocked categories).
            supplierDirectory ??= new SupplierDirectory();
            purchaseExecutor ??= new EmbodiedPurchaseExecutor(
                populationManager.State, hub.HouseholdLedgers, supplierDirectory, Journeys,
                null, hub.WorkTimeBudgets);

            var diag = new List<string>();
            DailyNeedsService.DayReport report = dailyNeedsService.ExecuteDay(
                populationManager.State, consumptionPlanner, purchaseExecutor,
                absoluteDayIndex, diag, hub.WorkTimeBudgets);
            if (report.MealsMissed > 0 || report.PurchasesMade > 0)
            {
                Debug.Log($"[SimulationDrivers] daily needs day {absoluteDayIndex}: " +
                    $"{report.MealsEaten} eaten, {report.MealsMissed} missed, " +
                    $"{report.PurchasesMade} purchases ({report.SpendCents}c).");
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
