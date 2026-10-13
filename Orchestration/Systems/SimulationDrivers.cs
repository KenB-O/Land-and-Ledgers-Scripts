using System;
using System.Collections.Generic;
using System.Linq;
using LandLedgers.Animals;
using LandLedgers.Economy;
using LandLedgers.Economy.Delegation;
using LandLedgers.Economy.Farming.Integration;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Businesses.BoardingHouse;
using LandLedgers.Economy.Creation;
using LandLedgers.Economy.Labor;
using LandLedgers.Economy.Recruitment;
using LandLedgers.Orchestration.Player;
using LandLedgers.Orchestration.Scenarios;
using LandLedgers.Population;
using EntityId = LandLedgers.Primitives.EntityId;
using LandLedgers.Primitives;
using LandLedgers.ReadModels.Valuation;
using LandLedgers.Skills;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;
using LandLedgers.World;
using LandLedgers.World.Property;
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
        private HouseholdMembershipRegistry householdMemberships;
        private ShopperSellerKnowledge sellerKnowledge = new ShopperSellerKnowledge();
        private readonly Dictionary<string, LaborDemandReport> latestDemandByBusiness =
            new Dictionary<string, LaborDemandReport>(StringComparer.Ordinal);
        private bool managerExecutionConfigured;
        private HouseholdShoppingLoop householdShoppingLoop;
        private readonly LiveTenancyPayeeDirectory tenancyPayees = new LiveTenancyPayeeDirectory();
        private readonly Dictionary<EntityId, string> autonomousTaskReservations = new Dictionary<EntityId, string>();
        private bool subscribedToTimeManager;

        /// <summary>
        /// Phase C: registers the REAL general store as an <see cref="IGoodsSupplier"/>
        /// in the shared <see cref="SupplierDirectory"/>, so the embodied
        /// shopping path is the one path (C5). Scene wiring calls this with a
        /// <see cref="GeneralStoreTradingPort"/> built over the live
        /// <see cref="GeneralStoreRuntimeManager"/> and the scenario's authored
        /// category offers (Codex integrates; the store's stocking is authored,
        /// never invented here).
        /// </summary>
        public void RegisterGeneralStoreSupplier(
            LandLedgers.Economy.Businesses.GeneralStore.IGeneralStoreTradingPort storePort)
        {
            supplierDirectory ??= new SupplierDirectory();
            if (hub != null)
            {
                HouseholdShoppingLoop.RegisterTaskDefinitions(hub.Tasks);
            }
            LandLedgers.Economy.Businesses.GeneralStore.GeneralStoreSupplierAdapter.RegisterIn(
                supplierDirectory, storePort);
            BuildHouseholdShoppingLoop();
        }

        /// <summary>Attaches the scenario's real membership authority for the production shopping loop.</summary>
        public void AttachHouseholdMemberships(HouseholdMembershipRegistry memberships, ShopperSellerKnowledge knowledge = null)
        {
            householdMemberships = memberships;
            if (knowledge != null) sellerKnowledge = knowledge;
            BuildHouseholdShoppingLoop();
        }

        /// <summary>
        /// Returns the production construction-material adapter for a real
        /// commissioning household. The adapter is intentionally scoped to
        /// that household's existing inventory; it does not create stock.
        /// </summary>
        public IConstructionMaterialSource CreateHouseholdConstructionMaterialSource(int householdId)
        {
            if (hub == null) return null;
            HouseholdInventory inventory = hub.HouseholdInventories.Get(householdId);
            return inventory == null ? null : new HouseholdConstructionMaterialSource(inventory);
        }

        /// <summary>Returns the production tool-custody adapter over hub inventories.</summary>
        public IHouseholdToolCustody CreateHouseholdToolCustody()
        {
            return hub == null ? null : new HouseholdInventoryToolCustody(hub.HouseholdInventories);
        }

        /// <summary>
        /// Registers a scene-owned boarding-house runtime with the existing
        /// hub. The runtime remains the detailed occupancy/custody authority;
        /// the hub only owns its persistence and settlement scheduling.
        /// </summary>
        public void RegisterBoardingHouseRuntime(BoardingHouseShopRuntime runtime)
        {
            if (runtime == null || hub == null) return;
            if (sharedBusinessRuntime != null && sharedBusinessRuntime.TryFindByInstanceId(runtime.BusinessInstanceId, out BusinessInstanceState business))
                runtime.BindBusiness(business);
            hub.RegisterBoardingHouseRuntime(runtime);
            hub.RegisterBoardingHouseMealSource(runtime.MealDaySource);
        }

        private void BuildHouseholdShoppingLoop()
        {
            if (hub == null || populationManager?.State == null || householdMemberships == null || supplierDirectory == null)
            {
                return;
            }

            householdShoppingLoop ??= new HouseholdShoppingLoop(
                populationManager.State,
                householdMemberships,
                hub.HouseholdLedgers,
                hub.HouseholdInventories,
                hub.PurchasingNeeds,
                supplierDirectory,
                Journeys ?? hub.Journeys,
                hub.Tasks,
                hub.WorkTimeBudgets,
                hub.PersonSchedules,
                sellerKnowledge,
                hub.FinancialObligations,
                hub.Ids,
                personEntityId: null,
                spendingPolicyProvider: householdId => hub.HouseholdDecisionDriver.GetSpendingPolicy(householdId));

            // The live store is an ordinary town seller. Seed only the
            // legitimate same-simulation knowledge graph; this does not grant
            // omniscient access to suppliers that are not registered here.
            if (populationManager.State.people != null)
            {
                foreach (PersonState person in populationManager.State.people)
                {
                    if (person != null && person.deathDayIndex < 0)
                    {
                        foreach (IGoodsSupplier supplier in supplierDirectory.All)
                        {
                            sellerKnowledge.LearnSupplier(
                                person.id,
                                supplier.SupplierBusinessId,
                                "opening-town merchant knowledge",
                                timeManager != null ? timeManager.CurrentAbsoluteDayIndex : 0);
                        }
                    }
                }
            }
        }

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
            ConfigureManagerExecution();
            sharedBusinessRuntime.EmploymentRegistry = hub.Employments;
            sharedBusinessRuntime.WireEmploymentRegistryToBusinesses();
            WireHouseholdCashAuthorities();
            WireNpcBusinessCashStores();
            BindNpcBusinessCreation(sharedBusinessRuntime);
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

            TownWorldController townWorld = FindAnyObjectByType<TownWorldController>();
            AcquisitionMarketManager acquisitionMarket = FindAnyObjectByType<AcquisitionMarketManager>();
            hub.AttachParcelWorld(townWorld, acquisitionMarket != null ? acquisitionMarket.Titles : null);

            // Pass IV (Worker 1): the universal V2 duration estimator. Method estimators
            // own their definition ids (handling, travel); everything else falls back
            // to the causal skill estimator, preserving pre-Pass IV behavior exactly.
            // Worker capability for method estimators comes from the Pass III stores.
            var causalEstimator = new CausalTaskDurationEstimator(hub.Skills);
            Func<TaskDurationInputs, DurationEstimate> legacyFallback = inputs =>
            {
                int baseMinutes = Math.Max(1, inputs != null ? inputs.BaseMinutes : 1);
                string skillId = inputs != null ? inputs.SkillId ?? string.Empty : string.Empty;
                EntityId worker = inputs != null && inputs.WorkerPersonId >= 0
                    ? EntityId.For(EntityKind.Person, inputs.WorkerPersonId)
                    : default(EntityId);
                int minutes = causalEstimator.EstimateMinutesForBase(baseMinutes, skillId, worker);
                return DurationEstimate.FromGameSeconds(
                    minutes * DurationConventions.SecondsPerMinute, "fallback:causal-v1");
            };
            var universalEstimator = new UniversalTaskDurationEstimator(
                new ITaskDurationEstimatorV2[]
                {
                    new HandlingDurationEstimator(HandlingDurationEstimator.DefaultDefinitionIds),
                    new TravelDurationEstimator(TravelDurationEstimator.DefaultDefinitionIds),
                },
                legacyFallback);
            hub.Tasks.SetDurationEstimatorV2(universalEstimator,
                (workerId, skillId) => (float)hub.Skills.GetCapability(workerId, skillId));

            // Pass IV (Worker 1): ambient budget store so the new lifecycle states keep
            // worker commitments truthful (WaitingOnPassiveProcess releases the worker;
            // ResumeTask re-commits).
            hub.Tasks.SetWorkTimeBudgetStore(hub.WorkTimeBudgets);

            // Pass IV Worker 2: install the equipment execution gate (Canon 4.1)
            // on the production TaskAuthority. Tasks with equipment, motive-
            // power, or workspace requirements are refused loudly when no
            // usable, unreserved instance exists — one plow cannot satisfy N
            // simultaneous tasks. Suppliers are hub-owned truth (unified
            // reservation authority + asset registry); nothing is assumed.
            hub.InstallEquipmentTaskGate(AbsoluteNowGameSeconds);

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
                WireNpcBusinessCashStores();
                BindNpcBusinessCreation(sharedBusinessRuntime);
            }

        }

        private void BindNpcBusinessCreation(SharedBusinessRuntimeManager runtime)
        {
            if (hub == null || runtime == null) return;
            hub.NpcBusinessCreation.PremisesAssigner = kind =>
                runtime.TryAssignNpcBusinessPremises(kind, out int buildingId) ? buildingId : -1;
            hub.NpcBusinessCreation.BusinessCreated = business =>
            {
                if (!runtime.TryAdoptNpcBusiness(business, out string message))
                    Debug.LogWarning("[SimulationDrivers] NPC Business adoption refused: " + message);
                else
                    WireNpcBusinessCashStores();
            };
        }

        /// <summary>
        /// Pass IV Worker 2: absolute game seconds on the TimeManager timeline
        /// for reservation overlap checks in the equipment task gate.
        /// </summary>
        private float AbsoluteNowGameSeconds()
        {
            if (timeManager == null) return 0f;
            return timeManager.CurrentAbsoluteDayIndex * 86400f + timeManager.GameSecondsIntoDay;
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

        /// <summary>
        /// Phase G: binds every live NPC Business to the shared credit cash
        /// bridge. The bridge is only a workflow adapter; BusinessRuntimeState
        /// remains the single business-cash authority.
        /// </summary>
        private void WireNpcBusinessCashStores()
        {
            if (hub == null || sharedBusinessRuntime == null)
            {
                return;
            }

            NpcBusinessRuntimeCashPort port = new NpcBusinessRuntimeCashPort(sharedBusinessRuntime);
            foreach (BusinessInstanceState business in sharedBusinessRuntime.Businesses)
            {
                if (business == null || business.Owner == null || business.Owner.OwnerKind == BusinessOwnerKind.Player)
                {
                    continue;
                }

                hub.CreditCash.Register(
                    "business:" + business.InstanceId,
                    new NpcBusinessCashStore(business.InstanceId, port));
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

            int absoluteDayIndex = timeManager != null
                ? timeManager.CurrentAbsoluteDayIndex
                : hub.LaborDayIndex;

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

            // Autonomous Labor: weekly wage settlement per business, guarded
            // against double-posting by the settled-week marker.
            DriveLaborMarketWeek(absoluteDayIndex);
            DriveBoardingHouseWeeks(absoluteDayIndex);
            // Pass V P2-3: autonomous entrepreneurship — observation to
            // formation, no quotas.
            DriveEntrepreneurshipWeek(absoluteDayIndex);
        }

        /// <summary>
        /// Pass V P2-3: runs the entrepreneurship driver once per week. Every
        /// evidence source is real: unfulfilled needs from the purchasing
        /// registry, suppliers from the supplier directory, premises from the
        /// runtime's unassigned buildings (query only — claiming happens at
        /// formation), equipment provenance from a live smithy, financing
        /// from the founder household's own ledger. Formation goes through
        /// the same adapter the player uses.
        /// </summary>
        private void DriveEntrepreneurshipWeek(int absoluteDayIndex)
        {
            if (hub == null || populationManager?.State == null || householdMemberships == null) return;
            var diag = new List<string>();
            hub.NpcEntrepreneurship.EvaluateWeek(absoluteDayIndex,
                new NpcEntrepreneurshipDriver.EntrepreneurshipContext
                {
                    Population = populationManager.State,
                    Memberships = householdMemberships,
                    Ledgers = hub.HouseholdLedgers,
                    Needs = hub.PurchasingNeeds,
                    Schedules = hub.PersonSchedules,
                    Events = hub.NpcBusinessEvents,
                    CreationPort = hub.NpcBusinessCreation,
                    Suppliers = supplierDirectory,
                    PersonAlreadyOwnsBusiness = PersonOwnsBusiness,
                    PremisesFinder = opportunity =>
                    {
                        if (sharedBusinessRuntime == null || opportunity == null) return null;
                        if (!sharedBusinessRuntime.TryAssignNpcBusinessPremises(
                            PremisesKind.DedicatedStorefront, out int buildingId))
                            return null;
                        return new NpcPremisesOption
                        {
                            Description = "unassigned storefront identified for the opportunity",
                            Tenure = NpcPremisesTenure.Rented,
                            ParcelOrBuildingRef = "bldg-" + buildingId,
                            UpfrontCostCents = 3000,
                            RecurringCostCents = 500,
                        };
                    },
                    EquipmentFinder = opportunity =>
                    {
                        string smithyId = FindCraftBusinessId();
                        if (string.IsNullOrWhiteSpace(smithyId)) return null;
                        return new NpcEquipmentOption
                        {
                            Description = "shop fittings (counter, scales, shelves)",
                            ItemId = "shop_fittings",
                            CostCents = 1500,
                            Provenance = "purchase:" + smithyId,
                        };
                    },
                }, diag);
            if (diag.Count > 0)
            {
                Debug.Log("[SimulationDrivers] entrepreneurship week " + absoluteDayIndex + ": " +
                    string.Join(" | ", diag));
            }
        }

        private bool PersonOwnsBusiness(int personId)
        {
            if (sharedBusinessRuntime == null) return false;
            foreach (BusinessInstanceState business in sharedBusinessRuntime.Businesses)
            {
                if (business?.Owner == null) continue;
                if (business.Owner.OwnerKind == BusinessOwnerKind.Npc
                    && business.Owner.PersonId == personId)
                    return true;
            }

            return false;
        }

        private string FindCraftBusinessId()
        {
            if (sharedBusinessRuntime == null) return null;
            foreach (BusinessInstanceState business in sharedBusinessRuntime.Businesses)
            {
                if (business == null) continue;
                if (business.BusinessType == BusinessType.Blacksmith
                    && !string.IsNullOrWhiteSpace(business.InstanceId))
                    return business.InstanceId;
            }

            return null;
        }

        private void OnDayChanged(SimulationDateChangedContext context)
        {
            if (hub == null || timeManager == null)
            {
                return;
            }

            int absoluteDayIndex = timeManager.CurrentAbsoluteDayIndex;

            // Pass IV (Worker 1): drain due events due at the day boundary before the
            // daily drivers advance.
            ProcessDueEvents();

            // Pass V (P0-2/P1-2): demographic day — births due, mortality,
            // birthday scheduling, and monthly conceptions/marriages/migration/
            // household splits. Idempotent per day via deterministic draws.
            populationManager?.DriveDemographicDay(absoluteDayIndex);

            // Population may be generated after Awake; bind the now-real
            // household ledgers before any daily finance workflow can open a
            // cash window.
            RefreshHouseholdCashBindings();

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

            // Muse Phase I: trade-credit aging is a daily read/transition over
            // the shared FinancialObligationAuthority. It never creates cash
            // or principal; it only advances registered invoice terms and
            // records the resulting credit events.
            var creditDiagnostics = new List<string>();
            hub.TradeCredit.EvaluateDay(
                hub.FinancialObligations,
                absoluteDayIndex,
                hub.CreditEvents,
                creditDiagnostics);
            if (creditDiagnostics.Count > 0)
            {
                Debug.LogWarning($"[SimulationDrivers] trade-credit aging day {absoluteDayIndex}: " +
                    string.Join(" | ", creditDiagnostics));
            }

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
            DriveBoardingHouseDays(absoluteDayIndex);
            DriveDailyNeeds(absoluteDayIndex);
            // Pass VII Workstream B (P1-5): daily horse care had no production
            // caller — horses never tired and never needed care. Fed from the
            // farm's real feed stock now; unfed horses degrade loudly.
            DriveHorseCareDay(absoluteDayIndex);
            DriveTenancyPayments(absoluteDayIndex);
            // Pass V drive-by: DomesticLaborDriver had NO live caller (tests
            // only). Wire it here: need-driven domestic tasks (meals, fuel,
            // animals, chores) scheduled on real person time through the real
            // TaskAuthority, budgets, schedule tracker, and due events.
            DriveDomesticLaborDay(absoluteDayIndex);

            // Autonomous Labor: daily labor-market drives. The hub's authority
            // predicate needs the current day for delegation bounds.
            hub.LaborDayIndex = absoluteDayIndex;
            DriveLaborMarketDay(absoluteDayIndex);
            DriveAutonomousLabor(absoluteDayIndex);
        }

        /// <summary>
        /// Settles agreements from the hub-owned HousingAuthority once per
        /// simulation day. Payees resolve to the same household ledgers or
        /// BusinessRuntimeState cash stores used everywhere else; an unknown
        /// payee therefore refuses the payment rather than losing tenant cash.
        /// </summary>
        /// <summary>
        /// Pass V drive-by: daily domestic labor. Each household's
        /// need-driven tasks (meals for members at home, fuel in cold season,
        /// animal care when the household owns animals, baseline chores) are
        /// planned by DomesticLaborDriver and scheduled on real person time.
        /// </summary>
        private void DriveDomesticLaborDay(int absoluteDayIndex)
        {
            if (hub == null || populationManager?.State?.households == null || householdMemberships == null) return;
            var diag = new List<string>();
            // Register once: re-registering daily would spam duplicate
            // rejections into the diagnostics.
            if (hub.Tasks.GetDefinition(DomesticTaskCatalog.MealPrepId) == null)
                DomesticTaskCatalog.RegisterAll(hub.Tasks, diag);
            bool coldSeason = IsColdSeasonMonth();
            foreach (HouseholdState household in populationManager.State.households)
            {
                if (household == null) continue;
                List<int> members = householdMemberships.GetActiveMembers(household.id);
                if (members.Count == 0) continue;
                var inputs = new DomesticLaborDriver.DomesticDayInputs
                {
                    HouseholdId = household.id,
                    MemberIds = members,
                    AgeBandFor = personId =>
                    {
                        PersonState person = populationManager.State.GetPerson(personId);
                        return person != null ? person.ageBand : AgeBand.Adult18Plus;
                    },
                    MealsAtHomeCount = members.Count,
                    ColdSeason = coldSeason,
                    HasAnimals = HouseholdOwnsAnimals(household.id, members),
                };
                hub.DomesticLaborDriver.DriveDay(inputs, absoluteDayIndex, hub.Tasks,
                    hub.WorkTimeBudgets, hub.PersonSchedules, hub.DomesticLabor,
                    hub.DueEvents, diag);
            }

            if (diag.Count > 0)
            {
                Debug.Log("[SimulationDrivers] domestic labor day " + absoluteDayIndex + ": " +
                    string.Join(" | ", diag));
            }
        }

        private bool HouseholdOwnsAnimals(int householdId, List<int> memberIds)
        {
            if (hub?.Animals == null) return false;
            string householdKey = householdId.ToString();
            var memberKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (int memberId in memberIds) memberKeys.Add(memberId.ToString());
            foreach (AnimalState animal in hub.Animals.ActiveAnimals)
            {
                if (animal == null) continue;
                if (animal.OwnerKind == AnimalOwnerKind.Household
                    && string.Equals(animal.OwnerId, householdKey, StringComparison.Ordinal))
                    return true;
                if (animal.OwnerKind == AnimalOwnerKind.Person
                    && memberKeys.Contains(animal.OwnerId ?? string.Empty))
                    return true;
            }

            return false;
        }

        private void DriveTenancyPayments(int absoluteDayIndex)
        {
            if (hub == null) return;
            tenancyPayees.Bind(populationManager, hub.HouseholdLedgers, sharedBusinessRuntime);
            var diagnostics = new List<string>();
            List<TenancySettlementResult> results = hub.TenancyPayments.SettleDueRents(
                hub.Housing,
                absoluteDayIndex,
                hub.HouseholdLedgers,
                tenancyPayees,
                hub.FinancialObligations,
                hub.Ids,
                diagnostics);
            if (results.Count > 0)
            {
                Debug.Log($"[SimulationDrivers] tenancy day {absoluteDayIndex}: " +
                    string.Join(" | ", results.Select(result =>
                        $"{result.AgreementId}:{result.Outcome}:{result.PaidCents}c")));
            }
            if (diagnostics.Count > 0)
            {
                Debug.LogWarning("[SimulationDrivers] tenancy: " + string.Join(" | ", diagnostics));
            }
        }

        private void DriveBoardingHouseDays(int absoluteDayIndex)
        {
            if (hub == null || hub.BoardingHouseRuntimes == null) return;
            var diagnostics = new List<string>();
            foreach (BoardingHouseShopRuntime runtime in hub.BoardingHouseRuntimes)
            {
                if (runtime == null) continue;
                runtime.ExecuteDay(absoluteDayIndex, 0, diagnostics);
            }
            if (diagnostics.Count > 0)
                Debug.Log("[SimulationDrivers] boarding-house: " + string.Join(" | ", diagnostics));
        }

        private void DriveBoardingHouseWeeks(int absoluteDayIndex)
        {
            if (hub == null || hub.BoardingHouseRuntimes == null) return;
            var diagnostics = new List<string>();
            foreach (BoardingHouseShopRuntime runtime in hub.BoardingHouseRuntimes)
            {
                if (runtime == null) continue;
                List<BoarderRentDue> dues = runtime.ExecuteWeek(absoluteDayIndex, diagnostics);
                hub.RentCollection.SettleDues(
                    dues,
                    runtime,
                    hub.HouseholdLedgers,
                    householdMemberships,
                    hub.FinancialObligations,
                    hub.Ids,
                    absoluteDayIndex,
                    diagnostics);
            }
            if (diagnostics.Count > 0)
                Debug.Log("[SimulationDrivers] boarding-house weekly: " + string.Join(" | ", diagnostics));
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
                null, hub.WorkTimeBudgets, pid => EntityId.For(EntityKind.Person, pid),
                hub.HouseholdInventories);

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
            IEmbodiedPurchaseExecutor dailyPurchaseExecutor = householdShoppingLoop == null
                ? purchaseExecutor
                : null;
            DailyNeedsService.DayReport report = dailyNeedsService.ExecuteDay(
                populationManager.State, consumptionPlanner, dailyPurchaseExecutor,
                absoluteDayIndex, diag, hub.WorkTimeBudgets,
                restaurantMeals, boardingMeals, hotelMeals,
                hub.HouseholdInventories, hub.MealLog, null);
            if (report.MealsMissed > 0 || report.PurchasesMade > 0)
            {
                Debug.Log($"[SimulationDrivers] daily needs day {absoluteDayIndex}: " +
                    $"{report.MealsEaten} eaten, {report.MealsMissed} missed, " +
                    $"{report.PurchasesMade} purchases ({report.SpendCents}c).");
            }

            DriveHouseholdShortageMonitor(absoluteDayIndex, diag);
            DriveEmbodiedShopping(absoluteDayIndex, diag);
            DriveHouseholdAutonomy(absoluteDayIndex, diag);
        }

        /// <summary>
        /// Pass VII Workstream B (P1-5): production daily horse care. Every
        /// active horse is fed from the farm's real, finite feed stock
        /// (HorseFeedStockAdapter) and recovers through
        /// HorseAuthority.ApplyCare; horses the stock cannot cover degrade and
        /// are named loudly. A horse holding a draft reservation eats the
        /// working ration — draft power has honest upkeep.
        /// </summary>
        private void DriveHorseCareDay(int absoluteDayIndex)
        {
            if (hub == null || hub.Animals == null || hub.FarmSlice == null || hub.DraftPower == null) return;
            var diagnostics = new List<string>();
            int unfed = HorseCareDriver.CareDay(
                hub.Animals,
                new HorseFeedStockAdapter(hub.FarmSlice.Feed),
                horse => horse != null && hub.DraftPower.IsAnimalReserved(horse.AnimalId),
                absoluteDayIndex,
                diagnostics);
            if (unfed > 0 || diagnostics.Count > 0)
            {
                Debug.Log("[SimulationDrivers] horse care day " + absoluteDayIndex + ": " +
                    unfed + " unfed | " + string.Join(" | ", diagnostics));
            }
        }

        private void DriveHouseholdAutonomy(int absoluteDayIndex, List<string> diag)
        {
            if (hub == null || populationManager?.State?.households == null || householdMemberships == null) return;
            // Pass V P2-2: the hardship asset-sale channel (household surplus
            // sold to real buyer households; no guaranteed buyer).
            var assetSaleService = new HouseholdAssetSaleService(
                hub.HouseholdLedgers,
                hub.HouseholdInventories,
                hub.PurchasingNeeds,
                householdId => populationManager.State.households.Exists(h => h != null && h.id == householdId),
                () =>
                {
                    var ids = new List<int>();
                    foreach (HouseholdState h in populationManager.State.households)
                        if (h != null) ids.Add(h.id);
                    return ids;
                },
                itemId =>
                {
                    int best = 0;
                    if (supplierDirectory != null)
                    {
                        foreach (IGoodsSupplier supplier in supplierDirectory.All)
                        {
                            var resolving = supplier as IItemResolvingSupplier;
                            if (resolving == null || !resolving.OffersItem(itemId)) continue;
                            int price = resolving.ItemPricePerUnitCents(itemId);
                            if (price > 0 && (best == 0 || price < best)) best = price;
                        }
                    }

                    return best;
                });
            var assetSalePort = new HouseholdToHouseholdAssetSalePort(assetSaleService);
            foreach (HouseholdState household in populationManager.State.households)
            {
                if (household == null) continue;
                hub.HouseholdAutonomy.EvaluateHouseholdDay(household.id, absoluteDayIndex,
                    new HouseholdAutonomyDriver.AutonomyContext
                    {
                        Ledgers = hub.HouseholdLedgers,
                        Memberships = householdMemberships,
                        Employments = hub.Employments,
                        ContactGraph = hub.ContactGraph,
                        Decisions = hub.HouseholdDecisions,
                        DecisionDriver = hub.HouseholdDecisionDriver,
                        HousingSearchNeeds = hub.HousingSearchNeeds,
                        FinancialObligations = hub.FinancialObligations,
                        Ids = hub.Ids,
                        Lenders = hub.HouseholdLenders,
                        PersonNameOf = personId =>
                        {
                            PersonState person = populationManager.State.GetPerson(personId);
                            return person != null
                                ? (person.firstName + " " + person.lastName).Trim()
                                : string.Empty;
                        },
                        AssetSale = assetSalePort,
                        SaleableSurplusValueFor = householdId =>
                            assetSaleService.EvaluateSaleableValue(householdId, null),
                    }, diag);
            }
        }

        /// <summary>
        /// Autonomous Labor: daily labor-market drives. Offer expiries and
        /// start-day activations advance here. Per-business manager and work-
        /// scheduler cycles are assembled by game wiring (see the Codex
        /// handoff) because they need the live business registry alongside
        /// the hub services.
        /// </summary>
        private void DriveLaborMarketDay(int absoluteDayIndex)
        {
            if (hub == null) return;
            var diag = new List<string>();
            hub.EmploymentNegotiation.AdvanceDay(absoluteDayIndex, hub.Employments, diag);
            if (diag.Count > 0)
            {
                Debug.LogWarning("[SimulationDrivers] labor-market day " + absoluteDayIndex + ": " +
                    string.Join(" | ", diag));
            }
        }

        /// <summary>
        /// Pass VII Workstream D: wires the manager decision execution service
        /// to the hub's real production authorities. The recruitment callback
        /// is the existing ExecuteManagerRecruitment path, preserved verbatim.
        /// No production business lender is arranged: non-escalated borrowing
        /// fails honestly, and the brain escalates all borrowing to a pending
        /// owner-approval request.
        /// </summary>
        private void ConfigureManagerExecution()
        {
            if (managerExecutionConfigured || hub == null || sharedBusinessRuntime == null)
            {
                return;
            }

            hub.ManagerDecisionExecution.Configure(new ManagerExecutionServices
            {
                FindBusiness = id =>
                {
                    BusinessInstanceState found;
                    return sharedBusinessRuntime.TryFindByInstanceId(id, out found) ? found : null;
                },
                AllBusinesses = () => sharedBusinessRuntime.Businesses,
                DemandFor = id =>
                {
                    LaborDemandReport report;
                    return latestDemandByBusiness.TryGetValue(id, out report) ? report : null;
                },
                BusinessEntityFor = id => EntityId.For(EntityKind.Business, StableBusinessRuntimeId(id)),
                Tasks = hub.Tasks,
                WorkScheduling = hub.WorkScheduling,
                PersonSchedules = hub.PersonSchedules,
                WorkTimeBudgets = hub.WorkTimeBudgets,
                Employments = hub.Employments,
                FinancialObligations = hub.FinancialObligations,
                Ids = hub.Ids,
                Recruiting = hub.Recruiting,
                SupplierKnowledge = sellerKnowledge,
                BusinessLender = null,
                OpenRecruitment = (business, demand, decision, day, diag) =>
                    ExecuteManagerRecruitment(business, demand, decision, day, diag),
            });
            managerExecutionConfigured = true;
        }

        /// <summary>
        /// Runs the labor services against the real business task queues. The
        /// labor DTOs are read models and decision inputs; TaskAuthority and
        /// PersonScheduleTracker remain the only writers for work assignment,
        /// Person time, and task state.
        /// </summary>
        private void DriveAutonomousLabor(int absoluteDayIndex)
        {
            if (hub == null || sharedBusinessRuntime == null)
            {
                return;
            }

            ConfigureManagerExecution();
            var diagnostics = new List<string>();
            hub.ManagerDecisionExecution.AdvanceShipments(absoluteDayIndex, diagnostics);
            foreach (BusinessInstanceState business in sharedBusinessRuntime.Businesses)
            {
                if (business == null || string.IsNullOrWhiteSpace(business.InstanceId))
                {
                    continue;
                }

                EntityId businessId = EntityId.For(EntityKind.Business, StableBusinessRuntimeId(business.InstanceId));
                List<WorkTask> openTasks = hub.Tasks.GetOpenTasks(businessId);
                if (openTasks == null || openTasks.Count == 0)
                {
                    continue;
                }

                var demandInputs = new LaborDemandInputs
                {
                    BusinessInstanceId = business.InstanceId,
                    DayIndex = absoluteDayIndex,
                };
                var workers = new List<SchedulerAvailableWorker>();
                var seenWorkers = new HashSet<int>();
                EmploymentRelationshipRegistry registry = business.RuntimeState?.EmploymentRegistry ?? hub.Employments;

                if (registry != null)
                {
                    foreach (EmploymentRelationship relationship in registry.GetActiveByEmployer(business.InstanceId))
                    {
                        AddLaborWorker(relationship?.EmployeePersonId ?? -1, seenWorkers, workers, absoluteDayIndex);
                    }
                }

                if (business.Owner != null && business.Owner.PersonId >= 0)
                {
                    AddLaborWorker(business.Owner.PersonId, seenWorkers, workers, absoluteDayIndex);
                }

                var schedulerWork = new List<SchedulerOpenWorkItem>();
                var taskByTextId = new Dictionary<string, WorkTask>(StringComparer.Ordinal);
                foreach (WorkTask task in openTasks)
                {
                    if (task == null || !task.IsOpen)
                    {
                        continue;
                    }

                    TaskDefinition definition = hub.Tasks.GetDefinition(task.DefinitionId);
                    string family = definition?.DisplayName;
                    if (string.IsNullOrWhiteSpace(family)) family = task.DefinitionId;
                    int minutes = Math.Max(1, task.MinutesRemaining);
                    if (task.Status == TaskStatus.Queued)
                    {
                        schedulerWork.Add(new SchedulerOpenWorkItem
                        {
                            TaskId = task.TaskId.ToString(),
                            Family = family,
                            MinutesRequired = minutes,
                            RequiredEquipment = definition != null && definition.EquipmentClasses.Count > 0
                                ? definition.EquipmentClasses[0] : string.Empty,
                            LocationId = task.LocationId,
                        });
                        taskByTextId[task.TaskId.ToString()] = task;
                    }

                    if (!demandInputs.OpenTaskMinutesByFamily.ContainsKey(family))
                    {
                        demandInputs.OpenTaskMinutesByFamily[family] = 0;
                    }
                    demandInputs.OpenTaskMinutesByFamily[family] += minutes;
                }

                demandInputs.ActiveEmployeeCount = workers.Count;
                demandInputs.AvailableEmployeeMinutes = workers.Sum(worker => worker.AvailableMinutes);
                LaborDemandReport demand = hub.LaborDemand.Assess(demandInputs, diagnostics);

                // A delegated manager is a real Person with a persisted grant.
                // No manager is synthesized for an owner or for an unconfigured
                // business; absent authority remains a visible non-operation.
                ManagerDecisionResult managerDecision = null;
                foreach (SchedulerAvailableWorker worker in workers)
                {
                    DelegatedAuthority authority = hub.Delegation.GetActiveAuthority(
                        business.InstanceId, worker.PersonId, absoluteDayIndex);
                    if (authority == null)
                    {
                        continue;
                    }

                    managerDecision = hub.Managers.DailyCycle(new ManagerCycleInputs
                    {
                        BusinessInstanceId = business.InstanceId,
                        ManagerPersonId = worker.PersonId,
                        CashCents = business.RuntimeState?.CurrentCashCents ?? 0,
                        DemandReport = demand,
                        Instructions = hub.Managers.Instructions.ForBusiness(business.InstanceId),
                        DelegatedScopes = ScopeNames(authority.Scopes),
                        DayIndex = absoluteDayIndex,
                        JudgmentSeed = absoluteDayIndex ^ StableBusinessRuntimeId(business.InstanceId),
                        JudgmentQuality01 = 0.8f,
                        PurchaseLimitCents = authority.PurchaseLimitCents,
                        MaxBorrowingCents = 0,
                    }, diagnostics);
                    // Pass VII Workstream D: every decision kind executes
                    // through a real production authority (or becomes a
                    // legitimate pending request, or fails with a reason).
                    // The OpenRecruitment executor calls back into the
                    // existing ExecuteManagerRecruitment path, preserved.
                    latestDemandByBusiness[business.InstanceId] = demand;
                    ManagerExecutionOutcome executionOutcome = hub.ManagerDecisionExecution.ExecuteDecision(
                        business, authority, worker.PersonId, demand, managerDecision,
                        absoluteDayIndex, diagnostics);
                    if (executionOutcome != null && !string.IsNullOrWhiteSpace(executionOutcome.Reason))
                    {
                        diagnostics.Add($"manager execution for {business.InstanceId}: {executionOutcome.Reason}.");
                    }
                    if (managerDecision != null && managerDecision.Acted &&
                        string.Equals(managerDecision.ActionKind, ManagerActKinds.Schedule, StringComparison.Ordinal))
                    {
                        // The manager's Schedule execution starts tasks through
                        // the real scheduler; rebuild from fresh task state so
                        // the blanket pass below never double-schedules work
                        // the manager already placed.
                        openTasks = hub.Tasks.GetOpenTasks(businessId);
                        schedulerWork.Clear();
                        taskByTextId.Clear();
                        foreach (WorkTask task in openTasks)
                        {
                            if (task == null || !task.IsOpen || task.Status != TaskStatus.Queued)
                            {
                                continue;
                            }

                            TaskDefinition refreshedDefinition = hub.Tasks.GetDefinition(task.DefinitionId);
                            string refreshedFamily = refreshedDefinition?.DisplayName;
                            if (string.IsNullOrWhiteSpace(refreshedFamily)) refreshedFamily = task.DefinitionId;
                            schedulerWork.Add(new SchedulerOpenWorkItem
                            {
                                TaskId = task.TaskId.ToString(),
                                Family = refreshedFamily,
                                MinutesRequired = Math.Max(1, task.MinutesRemaining),
                                RequiredEquipment = refreshedDefinition != null && refreshedDefinition.EquipmentClasses.Count > 0
                                    ? refreshedDefinition.EquipmentClasses[0] : string.Empty,
                                LocationId = task.LocationId,
                            });
                            taskByTextId[task.TaskId.ToString()] = task;
                        }
                    }
                    break;
                }

                if (schedulerWork.Count == 0 || workers.Count == 0)
                {
                    continue;
                }

                WorkAssignmentReport assignments = hub.WorkScheduling.AssignDailyWork(
                    new SchedulerInputs
                    {
                        BusinessInstanceId = business.InstanceId,
                        DayIndex = absoluteDayIndex,
                        OpenWork = schedulerWork,
                        Workers = workers,
                    }, diagnostics);
                StartAssignedBusinessTasks(assignments, taskByTextId, absoluteDayIndex, diagnostics);
            }

            if (diagnostics.Count > 0)
            {
                Debug.LogWarning("[SimulationDrivers] autonomous labor: " + string.Join(" | ", diagnostics));
            }
        }

        private void AddLaborWorker(int personId, HashSet<int> seen, List<SchedulerAvailableWorker> workers, int dayIndex)
        {
            if (personId < 0 || !seen.Add(personId)) return;
            EntityId personEntity = EntityId.For(EntityKind.Person, personId);
            WorkTimeBudget budget = hub.WorkTimeBudgets.GetOrCreate(personEntity);
            int availableMinutes = budget.MinutesRemaining;
            // Pass VII Workstream B (P2-10): the draft-driver hold is a real
            // person-time reservation in the unified authority. A person
            // driving a draft team is not available for scheduled work —
            // worker time is single-bookable, and the hold is visible to the
            // WorkScheduler as zero remaining minutes (no second scheduler).
            if (hub != null && hub.DraftPower != null && hub.DraftPower.IsDriverReserved(personEntity))
                availableMinutes = 0;
            workers.Add(new SchedulerAvailableWorker
            {
                PersonId = personId,
                AvailableMinutes = availableMinutes,
                SkillEvidence = new Dictionary<string, float>(),
            });
        }

        private void StartAssignedBusinessTasks(WorkAssignmentReport report, Dictionary<string, WorkTask> tasks, int dayIndex, List<string> diagnostics)
        {
            // Pass VII Workstream D: one shared implementation with the manager
            // Schedule executor (ManagerWorkSchedulerDrive) — no drift.
            ManagerWorkSchedulerDrive.StartAssignedTasks(
                report, tasks, hub.Tasks, hub.PersonSchedules, hub.WorkTimeBudgets,
                dayIndex, diagnostics, TaskReservationLabel,
                (taskId, reservationId) => autonomousTaskReservations[taskId] = reservationId);
        }

        private static List<string> ScopeNames(DelegationScope scopes)
        {
            var names = new List<string>();
            if (scopes.HasFlag(DelegationScope.Purchasing)) names.Add(ManagerScopeNames.Purchasing);
            if (scopes.HasFlag(DelegationScope.Scheduling)) names.Add(ManagerScopeNames.Scheduling);
            if (scopes.HasFlag(DelegationScope.Maintenance)) names.Add(ManagerScopeNames.Maintenance);
            if (scopes.HasFlag(DelegationScope.CashHandling)) names.Add(ManagerScopeNames.CashHandling);
            if (scopes.HasFlag(DelegationScope.Inventory)) names.Add(ManagerScopeNames.Inventory);
            if (scopes.HasFlag(DelegationScope.Staffing)) names.Add(ManagerScopeNames.Staffing);
            return names;
        }

        private static string TaskReservationLabel(WorkTask task)
        {
            return task == null
                ? string.Empty
                : $"task:{task.TaskId}|{task.DefinitionId}";
        }

        private string ExecuteManagerRecruitment(
            BusinessInstanceState business,
            LaborDemandReport demand,
            ManagerDecisionResult decision,
            int dayIndex,
            List<string> diagnostics)
        {
            if (business == null || demand == null || decision == null ||
                !decision.Acted || !string.Equals(decision.ActionKind, ManagerActKinds.OpenRecruitment, StringComparison.Ordinal))
            {
                return null;
            }

            string role = "general labor";
            if (demand.Lines != null)
            {
                for (int i = 0; i < demand.Lines.Count; i++)
                {
                    LaborDemandLine line = demand.Lines[i];
                    if (line != null && !string.IsNullOrWhiteSpace(line.TaskFamily) && line.OpenMinutes > 0)
                    {
                        role = line.TaskFamily;
                        break;
                    }
                }
            }

            RecruitmentEffort effort = hub.Recruiting.OpenEffort(
                business.InstanceId,
                role,
                RecruitmentChannel.WordOfMouth,
                dayIndex,
                dayIndex ^ StableBusinessRuntimeId(business.InstanceId),
                diagnostics);
            if (effort == null)
            {
                diagnostics.Add($"manager recruitment for {business.InstanceId}: RecruitmentService refused the effort");
                return null;
            }

            // The activity record is an immutable historical read model from
            // ManagerBrain; the real effort id is still surfaced in runtime
            // diagnostics and remains save-persisted by RecruitmentService.
            diagnostics.Add($"manager recruitment for {business.InstanceId}: opened {effort.EffortId} for {role}");
            return effort.EffortId;
        }

        /// <summary>
        /// Pass IV (Worker 1): later workers (production, household) register handlers
        /// for their due-event kinds here. The driver owns TaskCompletionDue,
        /// PassiveProcessComplete and ResourceReleaseDue; unhandled kinds are logged
        /// loudly — never silently skipped.
        /// </summary>
        public void RegisterDueEventHandler(DueEventKind kind, Action<DueEvent> handler)
        {
            if (handler == null)
            {
                dueEventHandlers.Remove(kind);
            }
            else
            {
                dueEventHandlers[kind] = handler;
            }
        }

        private readonly Dictionary<DueEventKind, Action<DueEvent>> dueEventHandlers =
            new Dictionary<DueEventKind, Action<DueEvent>>();

        /// <summary>
        /// Pass IV (Worker 1): pops all due events up to the current clock time in due
        /// order and dispatches them before any further advancement.
        /// </summary>
        private void ProcessDueEvents()
        {
            if (hub == null || timeManager == null)
            {
                return;
            }

            List<DueEvent> due = hub.DueEvents.PopDue(timeManager.AbsoluteGameSeconds);
            if (due.Count == 0)
            {
                return;
            }

            int dayIndex = timeManager.CurrentAbsoluteDayIndex;
            var diag = new List<string>();
            foreach (DueEvent dueEvent in due)
            {
                DispatchDueEvent(dueEvent, dayIndex, diag);
            }

            if (diag.Count > 0)
            {
                Debug.LogWarning("[SimulationDrivers] due events: " + string.Join(" | ", diag));
            }
        }

        private void DispatchDueEvent(DueEvent dueEvent, int dayIndex, List<string> diag)
        {
            Action<DueEvent> handler;
            if (dueEventHandlers.TryGetValue(dueEvent.Kind, out handler) && handler != null)
            {
                handler(dueEvent);
                return;
            }

            switch (dueEvent.Kind)
            {
                case DueEventKind.TaskCompletionDue:
                    CompleteTaskAtDueEvent(dueEvent, dayIndex, diag);
                    break;
                case DueEventKind.PassiveProcessComplete:
                    ResumeTaskAfterPassiveProcess(dueEvent, diag);
                    break;
                case DueEventKind.ResourceReleaseDue:
                    ReleaseReservationAtDueEvent(dueEvent, diag);
                    break;
                case DueEventKind.Birthday:
                    // Pass V P0-2: birthday due-events re-derive the person's
                    // age from the birth-date authority (never increment) and
                    // reschedule next year's event.
                    HandleBirthdayDueEvent(dueEvent, dayIndex, diag);
                    break;
                default:
                    diag.Add("due event " + dueEvent.EventId + " (" + dueEvent.Kind +
                        ", payload '" + dueEvent.PayloadRef + "') has no registered handler — consumed without effect");
                    break;
            }
        }

        /// <summary>
        /// Pass V P0-2: handles a fired birthday due-event through the
        /// birth-date authority (age is re-derived, never incremented) and
        /// reschedules next year's event on the hub scheduler.
        /// </summary>
        private void HandleBirthdayDueEvent(DueEvent dueEvent, int dayIndex, List<string> diag)
        {
            if (hub == null || populationManager == null || populationManager.State == null || timeManager == null)
            {
                diag.Add("birthday event " + dueEvent.EventId + " skipped: population or clock unavailable");
                return;
            }

            CalendarAnchor anchor = timeManager.Settings != null ? timeManager.Settings.Anchor : CalendarAnchor.Default;
            float secondsPerDay = timeManager.Settings != null ? timeManager.Settings.GameSecondsPerDay : 86400f;
            BirthdayService.HandleBirthdayDueEvent(
                dueEvent, populationManager.State, hub.DueEvents, anchor, dayIndex, secondsPerDay, diag);
        }

        /// <summary>
        /// Pass IV (Worker 1): every InProgress task with an assignee carries a
        /// TaskCompletionDue backstop (idempotent reschedule by stable id), so
        /// accelerated ticks cannot skip completions for tasks outside the per-tick
        /// drivers. Completion cancels the event; the per-tick path usually wins.
        /// </summary>
        private void SyncTaskCompletionDueEvents()
        {
            if (hub == null || timeManager == null)
            {
                return;
            }

            float now = timeManager.AbsoluteGameSeconds;
            foreach (WorkTask task in hub.Tasks.GetAllOpenTasks())
            {
                if (task == null || task.Status != TaskStatus.InProgress || !task.HasAssignee)
                {
                    continue;
                }

                string eventId;
                hub.DueEvents.Schedule(
                    DueEventKind.TaskCompletionDue,
                    now + Math.Max(0, task.MinutesRemaining) * DurationConventions.SecondsPerMinute,
                    task.TaskId.ToString(),
                    out eventId);
            }
        }

        private void CompleteTaskAtDueEvent(DueEvent dueEvent, int dayIndex, List<string> diag)
        {
            WorkTask task = hub.Tasks.FindTaskByIdString(dueEvent.PayloadRef);
            if (task == null)
            {
                diag.Add("TaskCompletionDue for unknown task '" + dueEvent.PayloadRef + "' — ignored");
                return;
            }

            if (task.Status != TaskStatus.InProgress)
            {
                return; // already handled; completion cancels its events
            }

            // Top up any unrecorded minutes so budget accounting stays truthful,
            // then resolve the outcome exactly like the per-tick driver.
            if (task.MinutesRemaining > 0)
            {
                string progressReason;
                bool reached;
                hub.Tasks.RecordWorkProgress(task.TaskId, task.MinutesRemaining, hub.WorkTimeBudgets,
                    dayIndex, out reached, out progressReason);
            }

            CompleteTaskWithCausalOutcome(task, dayIndex, 1);
            if (task.IsOpen)
            {
                diag.Add("TaskCompletionDue could not complete task " + task.TaskId);
            }
            else
            {
                ReleaseAutonomousTaskReservation(task, dayIndex);
            }
        }

        private void ResumeTaskAfterPassiveProcess(DueEvent dueEvent, List<string> diag)
        {
            WorkTask task = hub.Tasks.FindTaskByIdString(dueEvent.PayloadRef);
            if (task == null)
            {
                diag.Add("PassiveProcessComplete for unknown task '" + dueEvent.PayloadRef + "' — ignored");
                return;
            }

            if (task.Status != TaskStatus.WaitingOnPassiveProcess)
            {
                return;
            }

            string reason;
            if (!hub.Tasks.ResumeTask(task.TaskId, "passive process complete", out reason))
            {
                diag.Add("PassiveProcessComplete could not resume task " + task.TaskId + ": " + reason);
            }
        }

        private void ReleaseReservationAtDueEvent(DueEvent dueEvent, List<string> diag)
        {
            var authority = hub.Tasks.ReservationAuthority;
            if (authority == null)
            {
                diag.Add("ResourceReleaseDue " + dueEvent.EventId +
                    ": no reservation authority installed — reservation '" + dueEvent.PayloadRef + "' NOT released");
                return;
            }

            authority.Release(dueEvent.PayloadRef);
        }

        /// <summary>
        /// Pass IV (Worker 1): resolves a finished task's outcome through the causal
        /// resolver and completes it — the single completion path shared by the
        /// per-tick driver, the due-event drain, and player work.
        /// </summary>
        private void CompleteTaskWithCausalOutcome(WorkTask task, int dayIndex, int baseMinutesFallback)
        {
            hub.Tasks.CompleteTask(task.TaskId, ResolveCausalOutcome(task, dayIndex, baseMinutesFallback), dayIndex, hub.WorkTimeBudgets);
        }

        private TaskOutcome ResolveCausalOutcome(WorkTask task, int dayIndex, int baseMinutesFallback = 1)
        {
            TaskDefinition definition = hub.Tasks.GetDefinition(task.DefinitionId);
            string skillId = definition != null ? definition.RequiredSkillId : string.Empty;
            CausalOutcomeInputs inputs = hub.Skills.BuildCausalInputs(
                task.AssigneeId, skillId, definition != null ? definition.BaseMinutes : baseMinutesFallback);
            return hub.Skills.ResolveTaskCompletion(
                task.AssigneeId, skillId, inputs,
                difficulty01: 0.5, novelty01: 0.25, supervised: false,
                day: dayIndex, taskIdForDeterminism: task.TaskId.ToString());
        }

        private PlayerTaskProgressDriver playerWorkDriver;

        /// <summary>
        /// Pass IV (Worker 1): advances player-issued tasks in real time (they
        /// previously stalled in InProgress with no per-tick driver).
        /// </summary>
        private void AdvancePlayerWork(int wholeMinutes, int dayIndex)
        {
            if (hub == null || playerDirector == null || wholeMinutes <= 0)
            {
                return;
            }

            if (!playerDirector.PlayerPersonId.IsValid)
            {
                return;
            }

            playerWorkDriver ??= new PlayerTaskProgressDriver(hub.Tasks, hub.WorkTimeBudgets);
            var diag = new List<string>();
            playerWorkDriver.AdvancePlayerTasks(
                playerDirector.PlayerPersonId, wholeMinutes, dayIndex,
                (task, d) => ResolveCausalOutcome(task, d), diag);
            if (diag.Count > 0)
            {
                Debug.LogWarning("[SimulationDrivers] player work: " + string.Join(" | ", diag));
            }
        }

        private void AdvanceAutonomousWork(int wholeMinutes, int dayIndex, int minuteOfDay)
        {
            if (hub == null || wholeMinutes <= 0 || sharedBusinessRuntime == null) return;
            foreach (BusinessInstanceState business in sharedBusinessRuntime.Businesses)
            {
                if (business == null) continue;
                EntityId owner = EntityId.For(EntityKind.Business, StableBusinessRuntimeId(business.InstanceId));
                foreach (WorkTask task in hub.Tasks.GetOpenTasks(owner))
                {
                    if (task == null || task.Status != TaskStatus.InProgress || !task.HasAssignee) continue;
                    List<PersonActivityReservation> activeReservations =
                        hub.PersonSchedules.ActiveFor(task.AssigneeId.Id, dayIndex);
                    bool scheduledNow = false;
                    for (int i = 0; i < activeReservations.Count; i++)
                    {
                        PersonActivityReservation reservation = activeReservations[i];
                        if (reservation != null &&
                            reservation.Activity == PersonActivityKind.Work &&
                            string.Equals(reservation.Label, TaskReservationLabel(task), StringComparison.Ordinal) &&
                            minuteOfDay >= reservation.StartMinuteOfDay &&
                            minuteOfDay < reservation.EndMinuteOfDay)
                        {
                            scheduledNow = true;
                            break;
                        }
                    }
                    if (!scheduledNow) continue;
                    if (hub.Tasks.RecordWorkProgress(task.TaskId, wholeMinutes, hub.WorkTimeBudgets,
                        dayIndex, out bool reachedCompletion, out _))
                    {
                        if (reachedCompletion)
                        {
                            CompleteTaskWithCausalOutcome(task, dayIndex, wholeMinutes);
                        }
                        if (!task.IsOpen)
                        {
                            ReleaseAutonomousTaskReservation(task, dayIndex);
                        }
                    }
                }
            }
        }

        private void ReleaseAutonomousTaskReservation(WorkTask task, int dayIndex)
        {
            if (task == null || hub == null) return;

            if (autonomousTaskReservations.TryGetValue(task.TaskId, out string trackedReservation))
            {
                hub.PersonSchedules.Release(trackedReservation);
                autonomousTaskReservations.Remove(task.TaskId);
                return;
            }

            // The dictionary is intentionally transient. After save/load the
            // TaskAuthority and PersonScheduleTracker are authoritative; find
            // the persisted reservation by its stable task label.
            List<PersonActivityReservation> active =
                hub.PersonSchedules.ActiveFor(task.AssigneeId.Id, dayIndex);
            for (int i = 0; i < active.Count; i++)
            {
                PersonActivityReservation reservation = active[i];
                if (reservation != null &&
                    string.Equals(reservation.Label, TaskReservationLabel(task), StringComparison.Ordinal))
                {
                    hub.PersonSchedules.Release(reservation.ReservationId);
                    return;
                }
            }
        }

        /// <summary>
        /// Autonomous Labor: weekly wage settlement per business. The
        /// settled-week marker makes a repeated boundary in the same week
        /// (or a load followed by a boundary) a no-op — wages are never
        /// posted twice.
        /// </summary>
        private void DriveLaborMarketWeek(int absoluteDayIndex)
        {
            if (hub == null || sharedBusinessRuntime == null) return;
            int weekIndex = absoluteDayIndex / 7;
            var diag = new List<string>();
            foreach (BusinessInstanceState business in sharedBusinessRuntime.Businesses)
            {
                if (business?.RuntimeState == null || business.BusinessType != BusinessType.Generic)
                {
                    // Specialized domains retain their existing weekly
                    // settlement writer in SharedBusinessRuntimeManager. The
                    // shared labor writer owns Generic Businesses only, so a
                    // business can never receive two payroll passes at one
                    // calendar boundary.
                    continue;
                }
                var registry = business.RuntimeState.EmploymentRegistry ?? hub.Employments;
                hub.WageSettlement.SettleWeeklyPayroll(
                    business.RuntimeState, registry, weekIndex, absoluteDayIndex, diag);
            }
            if (diag.Count > 0)
            {
                Debug.LogWarning("[SimulationDrivers] labor-market week " + weekIndex + ": " +
                    string.Join(" | ", diag));
            }
        }

        /// <summary>
        /// Executes open purchasing needs only after the real household loop
        /// has been explicitly bound to a scenario's membership and supplier
        /// authorities. DailyNeedsService is then lot-consumption only, so
        /// this is the one production purchase writer for that scenario.
        /// </summary>
        private void DriveEmbodiedShopping(int absoluteDayIndex, List<string> diagnostics)
        {
            if (householdShoppingLoop == null || populationManager?.State?.households == null || hub == null)
            {
                return;
            }

            foreach (HouseholdState household in populationManager.State.households)
            {
                if (household == null)
                {
                    continue;
                }

                // Pass V P2-1: batch the household's open needs into one
                // planned errand through the hub-owned coordinator (one trip
                // serving several needs, one shopper, one person-time window,
                // claims so no need is ever bought twice). Needs the trip
                // cannot serve stay open and unclaimed for a later day.
                var errandCtx = new HouseholdErrandCoordinator.CoordinatorContext
                {
                    Population = populationManager.State,
                    Memberships = householdMemberships,
                    Ledgers = hub.HouseholdLedgers,
                    Inventories = hub.HouseholdInventories,
                    Needs = hub.PurchasingNeeds,
                    Schedules = hub.PersonSchedules,
                    Tasks = hub.Tasks,
                    Budgets = hub.WorkTimeBudgets,
                    PersonEntityId = id => EntityId.For(EntityKind.Person, id),
                };
                List<HouseholdErrandPlan> plans = hub.HouseholdErrands.PlanDay(
                    household.id, absoluteDayIndex, errandCtx, diagnostics);
                foreach (HouseholdErrandPlan plan in plans)
                {
                    if (plan == null) continue;
                    HouseholdShoppingLoop.BatchShoppingResult batch = hub.HouseholdErrands.ExecutePlan(
                        plan,
                        absoluteDayIndex,
                        errandCtx,
                        householdShoppingLoop,
                        new ShoppingLoopOptions { DepartureMinuteOfDay = 8 * 60, Diagnostics = diagnostics },
                        diagnostics);
                    if (batch != null && !batch.TripExecuted && !string.IsNullOrWhiteSpace(batch.FailureReason))
                    {
                        diagnostics.Add($"[SimulationDrivers] household errands H{household.id}: {batch.FailureReason}");
                    }
                }
            }
        }

        /// <summary>
        /// Rebinds finance windows to the current household ledgers after
        /// population generation or save restore. This is explicit because
        /// PopulationManager may not have populated its state during Awake.
        /// </summary>
        public void RefreshHouseholdCashBindings()
        {
            if (hub == null || populationManager == null || populationManager.State?.households == null)
            {
                return;
            }

            foreach (HouseholdState household in populationManager.State.households)
            {
                if (household == null || household.id < 0)
                {
                    continue;
                }

                // Do not create a ledger merely to expose a finance window:
                // legacy/new-game cash migration may still be pending, and
                // manufacturing an empty ledger here would disable the
                // deliberate fallback path. Bind only ledgers that already
                // exist in the authoritative registry.
                HouseholdLedger ledger = hub.HouseholdLedgers.Get(household.id);
                if (ledger != null)
                {
                    hub.CreditCash.Register(
                        "household:" + household.id,
                        new HouseholdCashStore(household.id, ledger));
                }
            }
        }

        /// <summary>
        /// Phase B (Real People): shortage detection. After meals are served,
        /// project each household's needs from actual members, current lot
        /// inventory, expected consumption, season, and supply access. Open
        /// needs are REAL purchasing-need objects for Phase C's shopping loop;
        /// they carry no money and post no sales.
        /// </summary>
        private void DriveHouseholdShortageMonitor(int absoluteDayIndex, List<string> diag)
        {
            if (hub == null || populationManager == null || populationManager.State == null)
            {
                return;
            }

            var policy = MealSchedulingPolicy.Default;
            var monitor = new HouseholdShortageMonitor(hub.PurchasingNeeds);
            bool isColdSeason = IsColdSeasonMonth();
            foreach (HouseholdState household in populationManager.State.households)
            {
                if (household == null)
                {
                    continue;
                }

                HouseholdInventory inventory = hub.HouseholdInventories.GetOrCreate(household.id);
                int members = Math.Max(1, household.ConsumptionMemberCount);
                var expectedDailyUse = new Dictionary<string, int>();
                foreach (string itemId in policy.MealItemPriority)
                {
                    HouseholdItemDefinition definition = HouseholdItemCatalog.Get(itemId);
                    if (definition == null || !definition.IsFood)
                    {
                        continue;
                    }

                    expectedDailyUse[itemId] =
                        members * policy.GetServingUnits(itemId) * policy.ExpectedMealsPerDay;
                }

                int ledgerBalance = hub.HouseholdLedgers.Get(household.id)?.GetBalanceCents() ?? 0;
                var supplyAccess = new HouseholdSupplyAccess
                {
                    HasGeneralStore = true,
                    HasOffMapAccess = true,
                    CanProduce = false,
                    HasCash = ledgerBalance > 0,
                };

                monitor.Evaluate(
                    household,
                    inventory,
                    expectedDailyUse,
                    isColdSeason,
                    supplyAccess,
                    upcomingObligationCents: 0,
                    expectedIncomeCents: 0,
                    dayIndex: absoluteDayIndex);
            }

            foreach (string monitorDiagnostic in monitor.Diagnostics)
            {
                diag.Add($"[SimulationDrivers] {monitorDiagnostic}");
            }
        }

        private bool IsColdSeasonMonth()
        {
            if (timeManager == null)
            {
                return false;
            }

            int month = timeManager.CurrentDate.Month;
            return month == 11 || month == 12 || month == 1 || month == 2 || month == 3;
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

            // Pass IV (Worker 1): due events pop in due order BEFORE any further
            // advancement, so accelerated ticks cannot skip task completions,
            // passive completions, or deadlines.
            ProcessDueEvents();
            SyncTaskCompletionDueEvents();

            // Player travel advances with the clock (whole minutes only).
            if (playerDirector != null)
            {
                int wholeMinutes = Mathf.FloorToInt(context.TickIntervalGameSeconds / 60f);
                if (wholeMinutes > 0)
                {
                    playerDirector.RecordMovementProgress(wholeMinutes, hub.WorkTimeBudgets);
                    AdvanceAutonomousWork(wholeMinutes, context.Date.AbsoluteDayIndex,
                        Mathf.FloorToInt(context.TimeOfDay01 * 1440f));
                }
            }
            else
            {
                int wholeMinutes = Mathf.FloorToInt(context.TickIntervalGameSeconds / 60f);
                if (wholeMinutes > 0)
                {
                    AdvanceAutonomousWork(wholeMinutes, context.Date.AbsoluteDayIndex,
                        Mathf.FloorToInt(context.TimeOfDay01 * 1440f));
                }
            }

            // Pass IV (Worker 1): player-issued tasks progress in real time instead of
            // stalling in InProgress (extends the AdvanceAutonomousWork pattern).
            int playerWholeMinutes = Mathf.FloorToInt(context.TickIntervalGameSeconds / 60f);
            if (playerWholeMinutes > 0)
            {
                AdvancePlayerWork(playerWholeMinutes, context.Date.AbsoluteDayIndex);
            }

            // Scenario goal evaluation (CLN-3 bootstraps subscribe OnScenarioTick).
            if (scenarioDirector != null && scenarioDirector.HasActiveScenario)
            {
                OnScenarioTick?.Invoke();
            }
        }
    }

    /// <summary>
    /// Production payee directory for the shared tenancy authority. Person
    /// payees resolve through the current PopulationState; business payees
    /// resolve through the existing BusinessRuntimeState cash authority.
    /// </summary>
    internal sealed class LiveTenancyPayeeDirectory : ITenancyPayeeDirectory
    {
        private PopulationManager population;
        private HouseholdLedgerRegistry ledgers;
        private SharedBusinessRuntimeManager businesses;

        public void Bind(PopulationManager populationManager, HouseholdLedgerRegistry householdLedgers,
            SharedBusinessRuntimeManager businessRuntime)
        {
            population = populationManager;
            ledgers = householdLedgers;
            businesses = businessRuntime;
        }

        public int FindPayeeHouseholdId(string payeeHolderId)
        {
            if (string.IsNullOrWhiteSpace(payeeHolderId) || population?.State?.people == null)
                return -1;

            string value = payeeHolderId.Trim();
            if (value.StartsWith("household:", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(value.Substring("household:".Length), out int householdId))
                return ledgers?.Get(householdId) != null ? householdId : -1;

            if (value.StartsWith("person:", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(value.Substring("person:".Length), out int personId))
            {
                PersonState person = population.State.GetPerson(personId);
                return person?.householdId >= 0 && ledgers?.Get(person.householdId) != null
                    ? person.householdId
                    : -1;
            }

            return -1;
        }

        public ITenancyPayeeCashSink FindPayeeBusinessSink(string payeeHolderId)
        {
            if (businesses == null || string.IsNullOrWhiteSpace(payeeHolderId)) return null;
            string value = payeeHolderId.Trim();
            const string prefix = "business:";
            if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
            string businessId = value.Substring(prefix.Length);
            for (int i = 0; i < businesses.Businesses.Count; i++)
            {
                BusinessInstanceState business = businesses.Businesses[i];
                if (business?.RuntimeState != null
                    && string.Equals(business.InstanceId, businessId, StringComparison.OrdinalIgnoreCase))
                    return new BusinessTenancyPayeeSink(business);
            }

            return null;
        }
    }

    internal sealed class BusinessTenancyPayeeSink : ITenancyPayeeCashSink
    {
        private readonly BusinessInstanceState business;

        public BusinessTenancyPayeeSink(BusinessInstanceState business)
        {
            this.business = business;
        }

        public string PayeeHolderId => "business:" + (business?.InstanceId ?? string.Empty);
        public string DisplayName => business?.RuntimeDisplayName ?? PayeeHolderId;

        public void RecordCashInflow(int cents, string label, int dayIndex)
        {
            if (business?.RuntimeState == null || cents <= 0) return;
            business.RuntimeState.AddCashCents(cents);
        }
    }

    /// <summary>Production cash adapter for NPC Business credit workflows.</summary>
    internal sealed class NpcBusinessRuntimeCashPort : INpcBusinessCashPort
    {
        private readonly SharedBusinessRuntimeManager runtime;

        public NpcBusinessRuntimeCashPort(SharedBusinessRuntimeManager runtime)
        {
            this.runtime = runtime;
        }

        private BusinessInstanceState Find(string businessInstanceId)
        {
            if (runtime == null || string.IsNullOrWhiteSpace(businessInstanceId)) return null;
            for (int i = 0; i < runtime.Businesses.Count; i++)
            {
                BusinessInstanceState business = runtime.Businesses[i];
                if (business != null && string.Equals(business.InstanceId, businessInstanceId, StringComparison.OrdinalIgnoreCase))
                {
                    return business;
                }
            }

            return null;
        }

        public int ReadCashCents(string businessInstanceId) => Find(businessInstanceId)?.RuntimeState?.CurrentCashCents ?? 0;

        public string Spend(string businessInstanceId, int dayIndex, int amountCents, string purpose)
        {
            BusinessInstanceState business = Find(businessInstanceId);
            if (business?.RuntimeState == null) return $"NPC business '{businessInstanceId}' is unavailable.";
            if (amountCents <= 0) return "NPC business cash spend must be positive.";
            if (business.RuntimeState.CurrentCashCents < amountCents)
            {
                return $"NPC business '{businessInstanceId}' has only {business.RuntimeState.CurrentCashCents}c; {amountCents}c refused.";
            }

            business.RuntimeState.SpendCents(amountCents);
            return null;
        }

        public string Receive(string businessInstanceId, int dayIndex, int amountCents, string reason)
        {
            BusinessInstanceState business = Find(businessInstanceId);
            if (business?.RuntimeState == null) return $"NPC business '{businessInstanceId}' is unavailable.";
            if (amountCents <= 0) return "NPC business cash receipt must be positive.";
            business.RuntimeState.AddCashCents(amountCents);
            return null;
        }
    }
}
