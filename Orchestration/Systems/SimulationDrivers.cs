using System;
using System.Collections.Generic;
using System.Linq;
using LandLedgers.Economy;
using LandLedgers.Orchestration.Player;
using LandLedgers.Orchestration.Scenarios;
using LandLedgers.Population;
using EntityId = LandLedgers.Primitives.EntityId;
using LandLedgers.Primitives;
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
    ///   a non-empty journey model (SimulationDrivers.Journeys, falling back to
    ///   the hub-owned model); supplier registration is scene wiring (T1A
    ///   pattern).
    /// - P6 travel &amp; communication daily drives: the hub's RouteConditionService
    ///   advances the weather (NX-2C closures actually close roads), PostalService
    ///   dispatches/arrives mail on real schedules (NX-2A letters actually move),
    ///   MoneyOrderService advances advices/expiry (D4E), RegisteredMailService
    ///   audits overdue items (D4F), and RecruitmentService accrues inquiries and
    ///   resolves letters — postal-routed ones only on real arrival (T2B).
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
        private bool subscribedToTimeManager;

        /// <summary>
        /// CLN-3 hook: scenario goal evaluation runs here. Scenario bootstraps
        /// subscribe their evaluators (e.g. FirstLedgerGoalEvaluator.Evaluate
        /// with the live game state); when no subscriber is attached, the
        /// scenario tick is a no-op. P7: a multicast event, not a settable
        /// property — two scenario bootstraps in one scene must not silently
        /// steal the tick from each other (last-writer-wins dropped one
        /// evaluator's goals entirely).
        /// </summary>
        public event Action OnScenarioTick;

        /// <summary>
        /// The bootstrap owns player identity because the Person is generated with the
        /// opening population. Replacing the stale serialized placeholder here keeps
        /// daily work-time and weekly owner-labor accounting on the real Person.
        /// </summary>
        public PlayerDirector PlayerDirector
        {
            get => playerDirector;
            set
            {
                playerDirector = value;
                if (scenarioDirector != null)
                {
                    scenarioDirector.PlayerDirector = value;
                }
            }
        }

        /// <summary>
        /// Rebinds the live business authority after a legacy scene bootstrap has
        /// created/configured its managers. Dynamic bootstrap order can run this
        /// component's Awake before the manager finishes its dependency wiring; the
        /// explicit handoff keeps weekly valuation and employment events on the same
        /// production authority.
        /// </summary>
        public void AttachBusinessRuntime(SharedBusinessRuntimeManager runtime)
        {
            sharedBusinessRuntime = runtime;
            hub ??= FindAnyObjectByType<SimulationSystemsHub>();
            timeManager ??= TimeManager.Instance != null ? TimeManager.Instance : FindAnyObjectByType<TimeManager>();
            SubscribeToTimeManager();
            if (sharedBusinessRuntime == null || hub == null)
            {
                return;
            }

            sharedBusinessRuntime.PreWeeklyResetCallback = PostWeeklyProfitToValuation;
            sharedBusinessRuntime.EmploymentRegistry = hub.Employments;
            sharedBusinessRuntime.WireEmploymentRegistryToBusinesses();
            WireHouseholdCashAuthorities();
        }

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

                // P1: payroll reads the PKG-6 employment authority. The hub owns the
                // registry (save-persisted); the manager wires it into every
                // business runtime state so ResolveWeeklyPayroll pays agreed
                // employment wages instead of the legacy slot-template path.
                sharedBusinessRuntime.EmploymentRegistry = hub.Employments;
                sharedBusinessRuntime.WireEmploymentRegistryToBusinesses();
                WireHouseholdCashAuthorities();
            }
        }

        /// <summary>
        /// Phase B (Real People): wires the single household-cash truth. The
        /// hub's HouseholdLedgerRegistry flows into the shared business
        /// runtime (doctor payments + payroll wage crediting), the population
        /// manager (genesis/settlement cash sweeps), and every business
        /// runtime state (per-worker household crediting). Person-to-household
        /// resolution comes from PopulationState via the membership fields.
        /// </summary>
        private void WireHouseholdCashAuthorities()
        {
            if (hub == null)
            {
                return;
            }

            if (sharedBusinessRuntime != null)
            {
                sharedBusinessRuntime.HouseholdLedgers = hub.HouseholdLedgers;
                sharedBusinessRuntime.PersonHouseholdIdLookup = ResolvePersonHouseholdId;
                sharedBusinessRuntime.WireHouseholdCashToBusinesses();
            }

            if (populationManager != null)
            {
                populationManager.HouseholdLedgers = hub.HouseholdLedgers;
            }
        }

        private int ResolvePersonHouseholdId(int personId)
        {
            if (populationManager == null || populationManager.State == null)
            {
                return -1;
            }

            var person = populationManager.State.GetPerson(personId);
            return person != null ? person.householdId : -1;
        }

        private void OnEnable()
        {
            SubscribeToTimeManager();
        }

        private void SubscribeToTimeManager()
        {
            if (subscribedToTimeManager || timeManager == null)
            {
                return;
            }

            timeManager.DayChanged += OnDayChanged;
            timeManager.WeekChanged += OnWeekChanged;
            timeManager.ShortTick += OnShortTick;
            subscribedToTimeManager = true;
        }

        private void OnDisable()
        {
            if (subscribedToTimeManager && timeManager != null)
            {
                timeManager.DayChanged -= OnDayChanged;
                timeManager.WeekChanged -= OnWeekChanged;
                timeManager.ShortTick -= OnShortTick;
                subscribedToTimeManager = false;
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

            // P1: SWN-3 liability ledger -> BIZ-5 valuation. Liabilities change
            // during the week (loans drawn, payables accrued); re-sync weekly so
            // the valuation read model reflects real balances, not boot-time ones.
            hub.Liabilities.SyncAllToValuation(null);

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
            if (playerDirector != null && playerDirector.PlayerPersonId.IsValid)
            {
                var budget = hub.WorkTimeBudgets.GetOrCreate(playerDirector.PlayerPersonId);
                weeklyPlayerWorkedMinutes += budget.MinutesWorked;
            }

            // TTS-1: roll every person's daily work-time budget.
            hub.WorkTimeBudgets.EnsureDay(absoluteDayIndex);

            // Generic production processes are persisted on their Business and
            // advance through the same TaskAuthority on the daily simulation
            // boundary. This is execution of an already-started physical process;
            // it does not grant capability or create output from a policy alone.
            DriveGenericProduction(absoluteDayIndex);

            // BIZ-4: age butcher lots (fresh → aging → spoiled).
            foreach (var runtime in hub.ButcherRuntimes)
            {
                runtime?.AgeLotsToDay(absoluteDayIndex);
            }

            // P6: travel & communication daily drives. Weather actually changes
            // (NX-2C: blizzards close roads through the journey model's
            // condition provider), mail actually moves on office schedules
            // (NX-2A: posted → in-transit → arrived → collected), money-order
            // advices arrive and stale orders expire (D4E), overdue registered
            // items are audited (D4F), and recruitment letters resolve —
            // postal-routed ones only on the mail item's real arrival (T2B).
            DriveTravelAndComms(absoluteDayIndex);

            // NX-2C drive-by: DailyNeedsService.ExecuteDay had NO live caller —
            // the NX-1B nutrition teeth never bit. Wire it here with the
            // work-time budgets so missed meals reduce usable minutes for real.
            DriveDailyNeeds(absoluteDayIndex);
        }

        private void DriveGenericProduction(int absoluteDayIndex)
        {
            if (hub == null || sharedBusinessRuntime == null) return;
            foreach (BusinessInstanceState business in sharedBusinessRuntime.Businesses)
            {
                if (business == null) continue;
                EntityId businessId = EntityId.For(EntityKind.Business, StableBusinessRuntimeId(business.InstanceId));

                // Policy scheduling is deliberately performed before process
                // advancement. It selects a real employed/owner Person, then
                // routes active work through the shared TaskAuthority. No policy
                // call itself creates goods.
                if (business.GenericConfiguration.ActiveProcesses.All(process => process == null || process.Completed))
                {
                    var workers = new List<EntityId>();
                    if (business.Owner != null && business.Owner.PersonId >= 0)
                    {
                        workers.Add(EntityId.For(EntityKind.Person, business.Owner.PersonId));
                    }
                    if (business.RuntimeState?.EmploymentRegistry != null)
                    {
                        foreach (EmploymentRelationship relationship in business.RuntimeState.EmploymentRegistry.GetActiveByEmployer(business.InstanceId))
                        {
                            if (relationship != null && relationship.EmployeePersonId >= 0)
                            {
                                workers.Add(EntityId.For(EntityKind.Person, relationship.EmployeePersonId));
                            }
                        }
                    }
                    hub.GenericProduction.TryStartEligiblePolicy(business, businessId, absoluteDayIndex, workers,
                        out _, out _);
                }

                if (business.GenericConfiguration.ActiveProcesses.Count == 0) continue;
                hub.GenericProduction.AdvanceBusinessProcesses(
                    business,
                    businessId,
                    absoluteDayIndex,
                    24 * 60);
            }
        }

        private static int StableBusinessRuntimeId(string value)
        {
            unchecked
            {
                int hash = 17;
                foreach (char character in value ?? string.Empty) hash = hash * 31 + character;
                return hash & 0x7fffffff;
            }
        }

        /// <summary>
        /// P6: advances the hub's travel &amp; communication authorities one day.
        /// Safe no-ops when nothing is registered yet (no offices, no weather
        /// attributes, no recruitment efforts): the services loop over empty
        /// registries instead of inventing traffic.
        /// </summary>
        private void DriveTravelAndComms(int absoluteDayIndex)
        {
            if (hub == null) return;
            var diag = new List<string>();

            hub.RouteConditions.AdvanceDay(absoluteDayIndex, hub.WeatherSeed, diag);
            if (hub.RouteConditions.IsBlizzard())
            {
                Debug.LogWarning($"[SimulationDrivers] BLIZZARD day {absoluteDayIndex} — " +
                    "roads closed; journeys route around or refuse (NX-2C).");
            }

            hub.Postal.AdvanceDay(hub.Journeys, absoluteDayIndex, diag);
            hub.MoneyOrders.AdvanceDay(absoluteDayIndex, diag);
            hub.RegisteredMail.AuditOverdue(absoluteDayIndex, diag);

            // T2B: open efforts accrue inquiries from real persons; letters
            // resolve — postal-routed ones only when the mail item really
            // arrived (no instant information transfer).
            if (populationManager != null && populationManager.State != null)
            {
                hub.Recruiting.AdvanceDay(
                    populationManager.State, hub.Employments, absoluteDayIndex, diag, hub.Postal);
            }
        }

        private void DriveDailyNeeds(int absoluteDayIndex)
        {
            if (hub == null) return;
            if (populationManager == null || populationManager.State == null)
            {
                return; // no population yet — nothing to feed
            }
            // P6: fall back to the hub's journey model when the driver carries
            // no explicit one — the model is hub-owned now. An empty model (no
            // locations authored yet) still skips honestly instead of routing
            // embodied purchases through nothing.
            JourneyModel journeys = Journeys ?? hub.Journeys;
            if (!HasAnyLocation(journeys))
            {
                Debug.LogWarning("[SimulationDrivers] DailyNeedsService skipped: no journey locations " +
                    "registered (author the world layout in scene bootstrap — see DRIVERS.md).");
                return;
            }

            dailyNeedsService ??= new DailyNeedsService();
            consumptionPlanner ??= new HouseholdConsumptionPlanner();
            // SupplierDirectory starts empty: scene wiring registers suppliers
            // (T1A pattern — the general store registers its stocked categories).
            supplierDirectory ??= new SupplierDirectory();
            // P2: wire the legacy person-int id space to HF-1 EntityIds so the
            // travel-time leg of embodied purchasing actually gates on the
            // person's TTS-1 work-time budget (previously always skipped).
            purchaseExecutor ??= new EmbodiedPurchaseExecutor(
                populationManager.State, hub.HouseholdLedgers, supplierDirectory, journeys,
                null, hub.WorkTimeBudgets, pid => EntityId.For(EntityKind.Person, pid));

            // P2: W2B/W2C/W3B nutrition links — count real prepared meals
            // served by live eating-house runtimes (Canon §2.5) so households
            // never double-feed a member who genuinely ate out. Empty
            // registries compose to zero meals: pre-W2B/W2C/W3B behavior.
            SimulationSystemsHub.BuildMealSourceComposites(
                hub.RestaurantMealSources,
                hub.BoardingHouseMealSources,
                hub.HotelMealSources,
                out var restaurantMeals,
                out var boardingMeals,
                out var hotelMeals);

            var diag = new List<string>();
            DailyNeedsService.DayReport report = dailyNeedsService.ExecuteDay(
                populationManager.State, consumptionPlanner, purchaseExecutor,
                absoluteDayIndex, diag, hub.WorkTimeBudgets,
                restaurantMeals, boardingMeals, hotelMeals);
            if (report.MealsMissed > 0 || report.PurchasesMade > 0)
            {
                Debug.Log($"[SimulationDrivers] daily needs day {absoluteDayIndex}: " +
                    $"{report.MealsEaten} eaten, {report.MealsMissed} missed, " +
                    $"{report.PurchasesMade} purchases ({report.SpendCents}c).");
            }
        }

        /// <summary>P6: true when the journey model has any routable location.</summary>
        private static bool HasAnyLocation(JourneyModel journeys)
        {
            if (journeys == null) return false;
            foreach (JourneyLocation location in journeys.AllLocations())
                if (location != null) return true;
            return false;
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

            // Scenario goal evaluation (CLN-3 bootstraps subscribe OnScenarioTick).
            if (scenarioDirector != null && scenarioDirector.HasActiveScenario)
            {
                OnScenarioTick?.Invoke();
            }
        }
    }
}
