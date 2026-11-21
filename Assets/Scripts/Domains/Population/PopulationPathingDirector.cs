using System;
using System.Collections.Generic;
using LandLedgers.Economy.Logistics;
using LandLedgers.Economy.Transport;
using LandLedgers.Pathing;
using LandLedgers.Population;
using LandLedgers.Orchestration.Systems;
using LandLedgers.Diagnostics;
using LandLedgers.Time;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.FirstLedger
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(270)]
    public sealed class PopulationPathingDirector : MonoBehaviour
    {
        private const string AgentRootName = "PopulationAgentsRoot";
        private const float MaleReferenceHeightMetres = 1.71f;
        private const float FemaleReferenceHeightMetres = 1.59f;
        private const float FallbackSelectionHeightMetres = 1.8f;

        private enum AgentRouteMode
        {
            None = 0,
            Scheduled = 1,
            AmbientOutbound = 2,
            AmbientReturning = 3,
            TaskDirected = 4
        }

        private enum AmbientRouteFailureKind
        {
            None = 0,
            CandidateNotReady = 1,
            MissingAnchor = 2,
            PathFailed = 3
        }

        private enum ScheduledRouteFailureKind
        {
            None = 0,
            CandidateNotReady = 1,
            MissingStartAnchor = 2,
            MissingTargetAnchor = 3,
            PathFailed = 4
        }

        private readonly struct ShoppingRouteCandidate
        {
            public ShoppingRouteCandidate(ManagedPersonAgent managed, int priority, int tieBreaker)
            {
                Managed = managed;
                Priority = priority;
                TieBreaker = tieBreaker;
            }

            public ManagedPersonAgent Managed { get; }
            public int Priority { get; }
            public int TieBreaker { get; }
        }

        [Header("Sources")]
        [SerializeField]
        private TownWorldController townWorld;

        [SerializeField]
        private PopulationManager populationManager;

        [SerializeField]
        private PathingManager pathingManager;

        [SerializeField]
        private TimeManager timeManager;

        private SimulationSystemsHub systemsHub;
        public Func<int, bool> ShoppingTripOwnsPerson { get; set; }
        public Func<int, string> ShoppingTripIdentityForPerson { get; set; }

        [SerializeField]
        private GeneralStoreRuntimeManager storeRuntime;

        [Header("Agent Visuals")]
        [SerializeField]
        private Transform agentRoot;

        [SerializeField]
        private GameObject maleAgentVisualPrefab;

        [SerializeField]
        private GameObject femaleAgentVisualPrefab;

        [Header("Movement")]
        [SerializeField, Min(1)]
        private int maxActiveAgents = 14;

        [SerializeField, Range(0f, 1f)]
        private float shoppingHouseholdShare = 0.5f;

        [SerializeField, Min(0)]
        private int minimumVisibleStreetAgents;

        [SerializeField, Min(1)]
        private int ambientRouteAttemptBudget = 24;

        [Header("Runtime Summary")]
        [SerializeField]
        private int managedPeople;

        [SerializeField]
        private int visibleStreetAgents;

        [SerializeField]
        private int routesStartedToday;

        [SerializeField]
        private int ambientRoutesStartedToday;

        [SerializeField]
        private int ambientAnchorFailuresToday;

        [SerializeField]
        private int ambientPathFailuresToday;

        [SerializeField]
        private int scheduledRoutesBlockedToday;

        [SerializeField]
        private int scheduledMissingStartFailuresToday;

        [SerializeField]
        private int scheduledMissingTargetFailuresToday;

        [SerializeField]
        private int scheduledPathFailuresToday;

        [Header("Resident Actor Diagnostics")]
        [SerializeField]
        private int populationRecordsExamined;

        [SerializeField]
        private int recordsRejectedAsNonLocalResidents;

        [SerializeField]
        private int residentsMissingHomeDoorAnchors;

        [SerializeField]
        private int residentActorEntriesCreated;

        // Campaign C (DEF-021): agent-cap truncation diagnostics. The walk in
        // PopulateManagedAgents scans the registry and counts otherwise eligible
        // residents skipped only because agents.Count reached maxActiveAgents.
        // These counters name that cut (count, cap value, resident summary).
        // Appended fields only; nothing renamed or reordered.
        [SerializeField]
        private int residentsCutByAgentCap;

        [SerializeField]
        private int lastAgentCapApplied;

        [SerializeField]
        private string lastCutResidentSummary;

        [SerializeField]
        private int explicitArrivalChecksForCutResidents;

        [SerializeField]
        private string lastRouteFailure;

        [SerializeField]
        private string lastMovementStatus;

        [SerializeField, Min(0.1f)]
        [Tooltip("Maximum horizontal distance from a building's path anchor to confirm an explicit travel task arrival.")]
        private float explicitTravelArrivalRadiusMeters = 1.5f;

        private readonly List<ManagedPersonAgent> agents = new();
        private bool workRoutesIssued;
        private bool shoppingRoutesIssued;
        private bool homeRoutesIssued;
        private bool loggedNoManagedAgents;
        private bool loggedAmbientRouteFailure;
        private bool loggedScheduledRouteFailure;
        // Campaign C (DEF-021): runtime-only cut set for the arrival check.
        // Not serialized: re-derived on every populate.
        private readonly HashSet<int> cutResidentIds = new();
        private readonly HashSet<int> warnedCutArrivalPersonIds = new();
        private int lastDayIndex = -1;
        private readonly Dictionary<int, WorkCommuteEstimate> workCommuteEstimates = new();

        private readonly struct WorkCommuteEstimate
        {
            public WorkCommuteEstimate(int day, int homeBuildingId, int workplaceBuildingId, float minutes)
            { Day = day; HomeBuildingId = homeBuildingId; WorkplaceBuildingId = workplaceBuildingId; Minutes = minutes; }
            public int Day { get; }
            public int HomeBuildingId { get; }
            public int WorkplaceBuildingId { get; }
            public float Minutes { get; }
        }

        public string LastMovementStatus => lastMovementStatus ?? string.Empty;
        public string LastRouteFailure => lastRouteFailure ?? string.Empty;
        public int ManagedPeopleCount => managedPeople;
        public int VisibleStreetAgentCount => visibleStreetAgents;

        public bool TryGetPresentationTransform(int personId, out Transform presentation)
        {
            for (int i = 0; i < agents.Count; i++)
            {
                ManagedPersonAgent agent = agents[i];
                if (agent?.person == null || agent.person.id != personId) continue;
                presentation = agent.mover != null && !agent.mover.IsHidden ? agent.mover.transform : null;
                return presentation != null && presentation.gameObject.activeInHierarchy;
            }
            presentation = null;
            return false;
        }

        public bool TryGetMovementReadout(int personId, out string status, out float remainingDistanceMeters,
            out float estimatedGameSecondsToArrival)
        {
            status = "No active physical route";
            remainingDistanceMeters = 0f;
            estimatedGameSecondsToArrival = 0f;
            for (int i = 0; i < agents.Count; i++)
            {
                ManagedPersonAgent agent = agents[i];
                if (agent?.person == null || agent.person.id != personId || agent.mover == null) continue;
                if (agent.mover.IsMoving)
                {
                    remainingDistanceMeters = agent.mover.RemainingWorldRouteDistance;
                    float speed = pathingManager != null && pathingManager.Settings != null
                        ? Mathf.Max(0.01f, pathingManager.Settings.pedestrianSpeedMetersPerSecond)
                        : 1.2f;
                    float clockRate = timeManager != null ? timeManager.CurrentGameSecondsPerRealSecond : 1f;
                    if (clockRate <= 0f)
                    {
                        estimatedGameSecondsToArrival = float.PositiveInfinity;
                        status = $"Route paused; {remainingDistanceMeters:0.0} m remaining";
                    }
                    else
                    {
                        // ETA is expressed in authoritative game seconds. Visual
                        // animation caps do not change a route's simulated speed.
                        estimatedGameSecondsToArrival = remainingDistanceMeters / speed;
                        status = $"Walking; {remainingDistanceMeters:0.0} m remaining";
                    }
                }
                else if (agent.mover.IsRouteActive) status = "At doorway transition";
                else if (agent.mover.IsArrivedVisible) status = "Arrived and visible";
                else if (agent.mover.IsHidden) status = "No visible on-map presentation";
                else status = "Stationary; no active route";
                return true;
            }
            return false;
        }

        /// <summary>
        /// Supplies the task trace with the managed person's authoritative
        /// household/work destination and current physical route ownership.
        /// This is a read-only diagnostic view; it does not alter routing or
        /// schedule state.
        /// </summary>
        public bool TryGetPersonOperationalReadout(
            int personId,
            out int homeBuildingId,
            out int workplaceBuildingId,
            out int currentDestinationBuildingId,
            out string scheduleState,
            out string routeMode,
            out bool moverVisible,
            out bool routeActive)
        {
            homeBuildingId = -1;
            workplaceBuildingId = -1;
            currentDestinationBuildingId = -1;
            scheduleState = string.Empty;
            routeMode = string.Empty;
            moverVisible = false;
            routeActive = false;
            if (personId < 0) return false;

            for (int i = 0; i < agents.Count; i++)
            {
                ManagedPersonAgent agent = agents[i];
                if (agent?.person == null || agent.person.id != personId) continue;

                homeBuildingId = agent.person.homeBuildingId;
                workplaceBuildingId = agent.person.workplaceBuildingId;
                currentDestinationBuildingId = agent.person.currentDestinationBuildingId;
                scheduleState = agent.person.scheduleState.ToString();
                routeMode = agent.routeMode.ToString();
                moverVisible = agent.mover != null
                    && !agent.mover.IsHidden
                    && agent.mover.gameObject.activeInHierarchy;
                routeActive = agent.mover != null && agent.mover.IsRouteActive;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Lane 9c (M-R8): the presentation facts AdvanceAutonomousWork
        /// needs for a business task's assignee — whether the resident has
        /// a managed on-map actor at all, and whether that actor's mover
        /// is Hidden (or has no mover, which presents the same way: no
        /// body that can move). False when the person has no managed
        /// agent; off-map/dead come from PersonState, not the director.
        /// </summary>
        public bool TryGetPersonPresentationReadout(
            int personId, out bool hasManagedActor, out bool isHidden)
        {
            hasManagedActor = false;
            isHidden = false;
            if (personId < 0) return false;
            for (int i = 0; i < agents.Count; i++)
            {
                ManagedPersonAgent agent = agents[i];
                if (agent?.person == null || agent.person.id != personId) continue;
                hasManagedActor = true;
                isHidden = agent.mover == null || agent.mover.IsHidden;
                return true;
            }
            return false;
        }

        /// <summary>Read-only physical route identity and progress for the development trace.</summary>
        public bool TryGetMovementTraceReadout(int personId, out string routeIdentity, out int routeGeneration,
            out int waypointIndex, out int waypointCount, out Vector3 routeDestination, out bool hasRouteDestination,
            out string routeMode, out string scheduleState, out string activityLocationId,
            out bool moverVisible, out bool routeActive, out string moverState)
        {
            routeIdentity = string.Empty;
            routeGeneration = -1;
            waypointIndex = -1;
            waypointCount = 0;
            routeDestination = Vector3.zero;
            hasRouteDestination = false;
            routeMode = string.Empty;
            scheduleState = string.Empty;
            activityLocationId = string.Empty;
            moverVisible = false;
            routeActive = false;
            moverState = string.Empty;
            if (personId < 0) return false;

            for (int i = 0; i < agents.Count; i++)
            {
                ManagedPersonAgent agent = agents[i];
                if (agent?.person == null || agent.person.id != personId || agent.mover == null) continue;
                routeGeneration = agent.mover.RouteGeneration;
                waypointIndex = agent.mover.CurrentWaypointIndex;
                waypointCount = agent.mover.CurrentPath != null ? agent.mover.CurrentPath.Count : 0;
                routeActive = agent.mover.IsRouteActive;
                moverVisible = !agent.mover.IsHidden && agent.mover.gameObject.activeInHierarchy;
                moverState = agent.mover.CurrentStateName;
                routeMode = agent.routeMode.ToString();
                scheduleState = agent.person.scheduleState.ToString();
                activityLocationId = agent.domesticActivityLocationId ?? string.Empty;
                if (routeActive && routeGeneration >= 0)
                {
                    routeIdentity = "P" + personId + ":route:" + routeGeneration;
                    routeDestination = agent.mover.RouteDestinationWorld;
                    hasRouteDestination = true;
                }
                return true;
            }
            return false;
        }

        public void Configure(
            TownWorldController newTownWorld,
            PopulationManager newPopulationManager,
            PathingManager newPathingManager,
            TimeManager newTimeManager,
            GeneralStoreRuntimeManager newStoreRuntime)
        {
            townWorld = newTownWorld != null ? newTownWorld : townWorld;
            populationManager = newPopulationManager != null ? newPopulationManager : populationManager;
            pathingManager = newPathingManager != null ? newPathingManager : pathingManager;
            timeManager = newTimeManager != null ? newTimeManager : timeManager;
            storeRuntime = newStoreRuntime != null ? newStoreRuntime : storeRuntime;
        }

        public string BuildDiagnosticsSummary()
        {
            AutoWire();
            managedPeople = agents.Count;
            visibleStreetAgents = CountVisibleAgents();

            string readiness = townWorld == null || populationManager == null || populationManager.State == null || pathingManager == null
                ? "Missing population, pathing, or town world."
                : managedPeople > 0
                    ? "Population pathing ready."
                    : "Population pathing has no managed visible-agent pool yet.";

            string scheduledFailures = scheduledRoutesBlockedToday > 0
                ? $", ScheduledBlocked={scheduledRoutesBlockedToday}, ScheduledFailures(start/target/path)={scheduledMissingStartFailuresToday}/{scheduledMissingTargetFailuresToday}/{scheduledPathFailuresToday}, LastRouteFailure={LastRouteFailure}"
                : string.Empty;

            return $"{readiness} Managed={managedPeople}, Visible={visibleStreetAgents}, ResidentsScanned={populationRecordsExamined}, NonLocalOrInvalid={recordsRejectedAsNonLocalResidents}, MissingHomeDoors={residentsMissingHomeDoorAnchors}, ActorEntriesCreated={residentActorEntriesCreated}, CutByAgentCap={residentsCutByAgentCap}, WorkIssued={workRoutesIssued}, ShoppingIssued={shoppingRoutesIssued}, HomeIssued={homeRoutesIssued}, AmbientStarted={ambientRoutesStartedToday}, AmbientFailures(anchor/path)={ambientAnchorFailuresToday}/{ambientPathFailuresToday}{scheduledFailures}, Last={LastMovementStatus}";
        }

        public void InitializeIfNeeded()
        {
            AutoWire();
            if (populationManager == null || populationManager.State == null || pathingManager == null || townWorld == null)
            {
                lastMovementStatus = "Missing population, pathing, or town world.";
                return;
            }

            // TimeManager and this director can receive an early startup tick
            // before FirstLedgerSliceBootstrapper has generated the town. Do
            // not interpret that legitimate ordering window as a failed
            // population: no door anchors can be resolved until the active
            // pathing manager points at this exact generated world grid.
            if (pathingManager.WorldController != townWorld)
            {
                pathingManager.Configure(townWorld, pathingManager.Settings);
            }
            if (!IsGeneratedWorldReadyForPopulation(townWorld, pathingManager))
            {
                int buildingCount = townWorld.Buildings != null ? townWorld.Buildings.Count : 0;
                lastMovementStatus = $"Waiting for generated pathing world (grid={(townWorld.Grid != null ? "ready" : "not ready")}, buildings={buildingCount}).";
                return;
            }

            if (agents.Count > 0)
            {
                EnsureAmbientStreetLife();
                return;
            }

            EnsureAgentRoot();
            PopulateManagedAgents();
            EnsureAmbientStreetLife();
        }

        public static bool IsGeneratedWorldReadyForPopulation(
            TownWorldController expectedWorld,
            PathingManager pathing)
        {
            return expectedWorld != null
                && pathing != null
                && expectedWorld.Grid != null
                && ReferenceEquals(pathing.WorldController, expectedWorld)
                && ReferenceEquals(pathing.Grid, expectedWorld.Grid);
        }

        public void ResetForLoadedState()
        {
            AutoWire();
            UnsubscribeManagedAgents();
            agents.Clear();
            managedPeople = 0;
            workRoutesIssued = false;
            shoppingRoutesIssued = false;
            homeRoutesIssued = false;
            routesStartedToday = 0;
            ambientRoutesStartedToday = 0;
            ambientAnchorFailuresToday = 0;
            ambientPathFailuresToday = 0;
            scheduledRoutesBlockedToday = 0;
            scheduledMissingStartFailuresToday = 0;
            scheduledMissingTargetFailuresToday = 0;
            scheduledPathFailuresToday = 0;
            lastRouteFailure = string.Empty;
            visibleStreetAgents = 0;
            // Campaign C (DEF-021): clear the agent-cap diagnostics with the
            // other populate state; the next populate re-derives them.
            residentsCutByAgentCap = 0;
            lastAgentCapApplied = 0;
            lastCutResidentSummary = string.Empty;
            explicitArrivalChecksForCutResidents = 0;
            cutResidentIds.Clear();
            warnedCutArrivalPersonIds.Clear();
            loggedNoManagedAgents = false;
            loggedAmbientRouteFailure = false;
            loggedScheduledRouteFailure = false;
            lastDayIndex = -1;
            lastMovementStatus = "Resetting visual population routes for restored save state.";
            populationManager?.NormalizeTransientScheduleStates();

            if (agentRoot != null)
            {
                for (int i = agentRoot.childCount - 1; i >= 0; i--)
                {
                    GameObject child = agentRoot.GetChild(i).gameObject;
                    if (Application.isPlaying)
                    {
                        // Destroy is deferred until frame end. Hide the previous
                        // presentation immediately so a restore never renders two
                        // actors for the same persistent Person identity.
                        child.SetActive(false);
                        Destroy(child);
                    }
                    else
                    {
                        DestroyImmediate(child);
                    }
                }
            }

            InitializeIfNeeded();
        }

        [ContextMenu("Send Workers To Work")]
        public void SendWorkersToWork()
        {
            InitializeIfNeeded();
            routesStartedToday += SendDueWorkersToWork(1f);
            workRoutesIssued = true;
        }

        [ContextMenu("Send Workers Home")]
        public void SendWorkersHome()
        {
            InitializeIfNeeded();
            routesStartedToday += SendRouteForState(
                PopulationScheduleState.ReturningHome,
                person => person.homeBuildingId >= 0
                    && person.scheduleState != PopulationScheduleState.AtHome
                    && person.scheduleState != PopulationScheduleState.ReturningHome,
                person => person.homeBuildingId);
            homeRoutesIssued = true;
        }

        [ContextMenu("Send Shoppers To Store")]
        public void SendShoppersToStore()
        {
            InitializeIfNeeded();
            int storeBuildingId = storeRuntime != null ? storeRuntime.StoreBuildingId : -1;
            if (storeBuildingId < 0)
            {
                return;
            }

            List<ShoppingRouteCandidate> candidates = BuildShoppingRouteCandidates();
            int targetRoutes = ResolveShoppingRouteTargetCount(candidates.Count);
            int sent = 0;
            for (int i = 0; i < candidates.Count && sent < targetRoutes; i++)
            {
                ManagedPersonAgent managed = candidates[i].Managed;
                if (TrySendAgent(managed, storeBuildingId, PopulationScheduleState.GoingShopping, out ScheduledRouteFailureKind failureKind,
                        $"destination=building:{storeBuildingId}"))
                {
                    sent++;
                    continue;
                }

                RecordScheduledRouteFailure(failureKind, managed?.person, storeBuildingId, PopulationScheduleState.GoingShopping);
            }

            routesStartedToday += sent;
            shoppingRoutesIssued = true;
            RefreshMovementStatus($"Sent {sent} shoppers to store from {candidates.Count} eligible household(s).");
        }

        private List<ShoppingRouteCandidate> BuildShoppingRouteCandidates()
        {
            Dictionary<int, ShoppingRouteCandidate> bestByHousehold = new();
            for (int i = 0; i < agents.Count; i++)
            {
                ManagedPersonAgent managed = agents[i];
                if (managed == null
                    || managed.person == null
                    || managed.person.scheduleState != PopulationScheduleState.AtHome)
                {
                    continue;
                }

                int householdId = managed.person.householdId;
                int priority = storeRuntime != null ? storeRuntime.EvaluateHouseholdShoppingPriority(householdId) : 0;
                if (priority <= 0)
                {
                    continue;
                }

                ShoppingRouteCandidate candidate = new(managed, priority, BuildShoppingTieBreaker(managed.person));
                if (!bestByHousehold.TryGetValue(householdId, out ShoppingRouteCandidate existing)
                    || IsBetterShoppingCandidate(candidate, existing))
                {
                    bestByHousehold[householdId] = candidate;
                }
            }

            List<ShoppingRouteCandidate> candidates = new(bestByHousehold.Values);
            candidates.Sort(CompareShoppingCandidates);
            return candidates;
        }

        private int ResolveShoppingRouteTargetCount(int candidateCount)
        {
            if (candidateCount <= 0)
            {
                return 0;
            }

            int shareTarget = Mathf.Max(1, Mathf.CeilToInt(candidateCount * Mathf.Clamp01(shoppingHouseholdShare)));
            int waveCap = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(1, maxActiveAgents) * 0.4f));
            return Mathf.Clamp(shareTarget, 1, Mathf.Min(candidateCount, waveCap));
        }

        private int BuildShoppingTieBreaker(PersonState person)
        {
            unchecked
            {
                int salt = 17;
                salt = salt * 31 + (person != null ? person.householdId : 0);
                salt = salt * 31 + (person != null ? person.id : 0);
                salt = salt * 31 + (timeManager != null ? timeManager.CurrentAbsoluteDayIndex : lastDayIndex);
                return salt;
            }
        }

        private static bool IsBetterShoppingCandidate(ShoppingRouteCandidate candidate, ShoppingRouteCandidate existing)
        {
            if (candidate.Priority != existing.Priority)
            {
                return candidate.Priority > existing.Priority;
            }

            return candidate.TieBreaker < existing.TieBreaker;
        }

        private static int CompareShoppingCandidates(ShoppingRouteCandidate left, ShoppingRouteCandidate right)
        {
            int priorityComparison = right.Priority.CompareTo(left.Priority);
            if (priorityComparison != 0)
            {
                return priorityComparison;
            }

            return left.TieBreaker.CompareTo(right.TieBreaker);
        }

        private void Start()
        {
            InitializeIfNeeded();
            SubscribeToTime();
        }

        private void OnEnable()
        {
            SubscribeToTime();
        }

        private void OnDisable()
        {
            if (timeManager != null)
            {
                timeManager.BeforeShortTick -= AdvanceRoutesBeforeShortTick;
                timeManager.ShortTick -= OnShortTick;
            }
        }

        private void OnDestroy()
        {
            UnsubscribeManagedAgents();
        }

        private void OnShortTick(SimulationTickContext context)
        {
            if (context.Date.AbsoluteDayIndex != lastDayIndex)
            {
                lastDayIndex = context.Date.AbsoluteDayIndex;
                routesStartedToday = 0;
                ambientRoutesStartedToday = 0;
                ambientAnchorFailuresToday = 0;
                ambientPathFailuresToday = 0;
                scheduledRoutesBlockedToday = 0;
                scheduledMissingStartFailuresToday = 0;
                scheduledMissingTargetFailuresToday = 0;
                scheduledPathFailuresToday = 0;
                lastRouteFailure = string.Empty;
                loggedAmbientRouteFailure = false;
                loggedScheduledRouteFailure = false;
                workRoutesIssued = false;
                shoppingRoutesIssued = false;
                homeRoutesIssued = false;
                int normalized = populationManager != null ? populationManager.NormalizeTransientScheduleStates() : 0;
                if (normalized > 0)
                {
                    lastMovementStatus = $"Recovered {normalized} stale population schedule state(s) for the new day.";
                }
            }

            if (!workRoutesIssued && context.TimeOfDay01 >= 0.22f)
            {
                routesStartedToday += SendDueWorkersToWork(context.TimeOfDay01);
                if (context.TimeOfDay01 >= 0.52f)
                {
                    workRoutesIssued = true;
                }
            }

            if (!shoppingRoutesIssued && context.TimeOfDay01 >= 0.46f)
            {
                SendShoppersToStore();
            }

            // Keep checking after the return window begins. A long valid commute
            // can arrive after the nominal return time; it should leave on the
            // next simulation tick rather than miss the entire day.
            if (context.TimeOfDay01 >= 0.62f)
            {
                routesStartedToday += SendDueWorkersHome(context.TimeOfDay01);
                if (context.TimeOfDay01 >= 0.86f)
                {
                    homeRoutesIssued = true;
                }
            }

            EnsureAmbientStreetLife();
        }

        private void AdvanceRoutesBeforeShortTick(SimulationTickContext context)
        {
            // Progress journeys at each chronological simulation boundary before
            // schedulers and due-event consumers inspect actor arrival. This keeps
            // visual frame cadence from delaying valid high-speed arrivals.
            double absoluteSeconds = (double)context.Date.AbsoluteDayIndex
                * (timeManager != null && timeManager.Settings != null
                    ? timeManager.Settings.GameSecondsPerDay : 86400d)
                + context.GameSecondsIntoDay;
            for (int i = 0; i < agents.Count; i++)
            {
                AgentMover mover = agents[i]?.mover;
                if (mover != null) mover.AdvanceAuthoritativeRouteTo(absoluteSeconds);
            }
        }

        private void PopulateManagedAgents()
        {
            populationRecordsExamined = 0;
            recordsRejectedAsNonLocalResidents = 0;
            residentsMissingHomeDoorAnchors = 0;
            residentActorEntriesCreated = 0;
            // Campaign C (DEF-021): reset the agent-cap diagnostics with the
            // other populate counters; the walk below re-derives them.
            residentsCutByAgentCap = 0;
            lastAgentCapApplied = maxActiveAgents;
            lastCutResidentSummary = string.Empty;
            cutResidentIds.Clear();
            explicitArrivalChecksForCutResidents = 0;
            warnedCutArrivalPersonIds.Clear();
            PopulationState state = populationManager.State;
            List<string> cutLabels = new List<string>();
            for (int i = 0; i < state.people.Count; i++)
            {
                PersonState person = state.people[i];
                populationRecordsExamined++;
                bool isMapResident = CanRepresentOnMap(person, state);
                if (!isMapResident)
                {
                    recordsRejectedAsNonLocalResidents++;
                    continue;
                }

                BuildingAnchor homeAnchor;
                bool hasHomeDoorAnchor = TryGetDoorAnchor(person.homeBuildingId, out homeAnchor);
                if (!hasHomeDoorAnchor)
                {
                    residentsMissingHomeDoorAnchors++;
                    continue;
                }

                // Scan every record, but attribute cap cuts only to residents
                // who would otherwise receive an actor. The unexamined suffix
                // of state.people is not necessarily a set of local residents:
                // it can contain departed, deceased, unhoused, or unanchored
                // records. Those must retain their own rejection diagnostics.
                if (AgentCapTruncation.IsEligibleResidentCut(
                        isMapResident, hasHomeDoorAnchor, agents.Count, maxActiveAgents))
                {
                    residentsCutByAgentCap++;
                    cutResidentIds.Add(person.id);
                    if (cutLabels.Count < 5)
                    {
                        string cutName = string.IsNullOrWhiteSpace(person.firstName)
                            && string.IsNullOrWhiteSpace(person.lastName)
                            ? string.Empty
                            : (" " + person.firstName + " " + person.lastName).TrimEnd();
                        cutLabels.Add("#" + person.id + cutName);
                    }
                    continue;
                }

                // After restore, transient travel states are normalized to their
                // legitimate arrival state and this stable destination is the
                // authoritative visual location. New residents still default to
                // home; missing destination anchors safely fall back to home.
                BuildingAnchor currentAnchor = homeAnchor;
                if (person.currentDestinationBuildingId >= 0
                    && TryGetDoorAnchor(person.currentDestinationBuildingId, out BuildingAnchor destinationAnchor))
                {
                    currentAnchor = destinationAnchor;
                }

                AgentMover mover = CreateAgent(person);
                mover.Configure(pathingManager);
                // Character prefabs contain alternate crate props for work
                // presentation. Keep both hidden unless a real freight carry
                // route is active.
                mover.SetFreightCargoVisible(false);
                Vector3 presentationPosition = pathingManager.CoordToPathWorld(currentAnchor.coord);
                if (currentAnchor.hasAuthoredWorldPosition)
                {
                    // Keep actors at the authored exterior approach rather than
                    // the doorway cell centre. The latter can fall inside the
                    // building footprint and be mistaken for an interior floor
                    // endpoint before any route has started.
                    presentationPosition.x = currentAnchor.authoredWorldPosition.x;
                    presentationPosition.z = currentAnchor.authoredWorldPosition.z;
                }
                mover.transform.position = presentationPosition;
                mover.ShowAtCurrentLocation();

                agents.Add(new ManagedPersonAgent(person, mover, homeAnchor));
                residentActorEntriesCreated++;
            }

            // Campaign C (DEF-021): only otherwise eligible local residents
            // without an actor belong in this set. Their ids let arrival checks
            // distinguish a genuine cap cut from invalid or unanchored records.
            if (residentsCutByAgentCap > 0)
            {
                lastCutResidentSummary = string.Join(", ", cutLabels)
                    + (residentsCutByAgentCap > 5 ? $" and {residentsCutByAgentCap - 5} more" : string.Empty);
                Debug.LogWarning(
                    $"PopulationPathingDirector cut {residentsCutByAgentCap} resident(s) at the agent cap: " +
                    $"people={state.people.Count}, examined={populationRecordsExamined}, cap={maxActiveAgents}, " +
                    $"cut={lastCutResidentSummary}. Cut residents have no visual agent, so their arrival-gated tasks cannot complete.",
                    this);
            }

            managedPeople = agents.Count;
            RefreshMovementStatus($"Managing {managedPeople} generated people.");
            if (managedPeople == 0 && !loggedNoManagedAgents)
            {
                loggedNoManagedAgents = true;
                int buildingCount = townWorld != null && townWorld.Buildings != null ? townWorld.Buildings.Count : 0;
                Debug.LogWarning(
                    $"PopulationPathingDirector created no visible agents after world readiness: " +
                    $"scanned={populationRecordsExamined}, nonlocal/invalid={recordsRejectedAsNonLocalResidents}, " +
                    $"missing home door anchors={residentsMissingHomeDoorAnchors}, " +
                    $"world grid={(townWorld != null && townWorld.Grid != null ? "ready" : "missing")}, " +
                    $"buildings={buildingCount}. Check household links and registered front-door anchors.", this);
            }
        }

        /// <summary>
        /// A visual street actor is a view of a living, housed, on-map Person.
        /// Historical/departed records remain in PopulationState but must never
        /// be manifested as local residents. The simulation record, not this
        /// predicate or the GameObject, remains the identity authority.
        /// </summary>
        public static bool CanRepresentOnMap(PersonState person, PopulationState population)
        {
            return person != null
                && population != null
                && person.id >= 0
                && person.deathDayIndex < 0
                && !person.isOffMap
                && person.householdId >= 0
                && population.GetHousehold(person.householdId) != null;
        }

        private int SendRouteForState(PopulationScheduleState fromState, PopulationScheduleState nextState, Predicate<PersonState> filter, Func<PersonState, int> targetBuildingId)
        {
            return SendRouteForState(
                nextState,
                person => person.scheduleState == fromState && (filter == null || filter(person)),
                targetBuildingId);
        }

        private int SendDueWorkersToWork(float timeOfDay01)
        {
            InitializeIfNeeded();
            int day = timeManager != null ? timeManager.CurrentAbsoluteDayIndex : lastDayIndex;
            int minuteOfDay = Mathf.Clamp(Mathf.FloorToInt(timeOfDay01 * 1440f), 0, 1439);
            int sent = SendRouteForState(
                PopulationScheduleState.AtHome,
                PopulationScheduleState.GoingToWork,
                person => person.workplaceBuildingId >= 0
                    && !IsReservedForSleepAt(systemsHub != null ? systemsHub.PersonSchedules : null,
                        person.id, day, minuteOfDay)
                    && IsWithinWorkDepartureWindow(timeOfDay01,
                        GetRouteAwareWorkDepartureTime01(person), GetWorkReturnTime01(person)),
                person => person.workplaceBuildingId);
            if (sent > 0)
            {
                RefreshMovementStatus($"Sent {sent} profession-timed worker(s) to work.");
            }

            return sent;
        }

        public static bool IsWithinWorkDepartureWindow(float timeOfDay01, float departureTime01, float returnTime01)
        {
            return timeOfDay01 >= departureTime01 && timeOfDay01 < returnTime01;
        }

        public bool IsCommittedToScheduledWorkWindow(int personId, int dayIndex, int startMinute, int durationMinutes)
        {
            if (populationManager?.State == null || personId < 0 || durationMinutes <= 0) return false;
            PersonState person = populationManager.State.GetPerson(personId);
            if (person == null || person.workplaceBuildingId < 0) return false;
            AutoWire();
            if (timeManager != null && dayIndex != timeManager.CurrentAbsoluteDayIndex) return false;
            int departure = Mathf.FloorToInt(GetRouteAwareWorkDepartureTime01(person) * 1440f);
            int returnHome = Mathf.FloorToInt(GetWorkReturnTime01(person) * 1440f);
            return OverlapsScheduledWorkWindow(startMinute, durationMinutes, departure, returnHome);
        }

        public static bool OverlapsScheduledWorkWindow(int startMinute, int durationMinutes,
            int departureMinute, int returnMinute)
        {
            if (startMinute < 0 || durationMinutes <= 0 || departureMinute < 0 || returnMinute <= departureMinute)
                return false;
            int end = Mathf.Min(1440, startMinute + durationMinutes);
            return startMinute < returnMinute && departureMinute < end;
        }

        /// <summary>
        /// Work departure is route-aware, but a commute must not begin during a
        /// resident's non-interruptible sleep reservation. If the planned early
        /// departure overlaps sleep, the next chronological short tick after
        /// sleep ends may issue the route; the work reservation remains the
        /// authority for the shift itself.
        /// </summary>
        public static bool IsReservedForSleepAt(PersonScheduleTracker schedules,
            int personId, int dayIndex, int minuteOfDay)
        {
            if (schedules == null || personId < 0 || dayIndex < 0) return false;
            int minute = Mathf.Clamp(minuteOfDay, 0, 1439);
            foreach (PersonActivityReservation reservation in schedules.ActiveFor(personId, dayIndex))
            {
                if (reservation.Activity == PersonActivityKind.Sleeping
                    && minute >= reservation.StartMinuteOfDay
                    && minute < reservation.EndMinuteOfDay)
                    return true;
            }
            return false;
        }

        private int SendDueWorkersHome(float timeOfDay01)
        {
            InitializeIfNeeded();
            int sent = SendRouteForState(
                PopulationScheduleState.ReturningHome,
                person => person.homeBuildingId >= 0
                    && IsEligibleForScheduledWorkReturn(person.scheduleState)
                    && timeOfDay01 >= GetWorkReturnTime01(person),
                person => person.homeBuildingId);
            if (sent > 0)
            {
                RefreshMovementStatus($"Sent {sent} profession-timed worker(s) home.");
            }

            return sent;
        }

        public static bool IsEligibleForScheduledWorkReturn(PopulationScheduleState scheduleState)
        {
            // Only a worker who actually reached work may be sent home by the
            // workday schedule. Retargeting GoingToWork used to replace the
            // active route and visually teleport the actor to the workplace.
            return scheduleState == PopulationScheduleState.AtWork;
        }

        private static float GetWorkDepartureTime01(PersonState person)
        {
            string profession = person != null ? person.professionId ?? string.Empty : string.Empty;
            if (ContainsAny(profession, "crop", "ranch", "sawmill", "lumber", "wood_fuel"))
            {
                return 0.24f;
            }

            if (ContainsAny(profession, "bakery", "butcher", "blacksmith", "builder", "wheelwright", "grain_mill"))
            {
                return 0.28f;
            }

            if (ContainsAny(profession, "boarding_house", "saloon", "barber"))
            {
                return 0.36f;
            }

            return 0.31f;
        }

        private float GetRouteAwareWorkDepartureTime01(PersonState person)
        {
            float nominalStart = GetWorkDepartureTime01(person);
            if (person == null) return nominalStart;
            AutoWire();
            int day = timeManager != null ? timeManager.CurrentAbsoluteDayIndex : lastDayIndex;
            if (systemsHub != null)
            {
                int firstWorkMinute = int.MaxValue;
                foreach (PersonActivityReservation reservation in systemsHub.PersonSchedules.ActiveFor(person.id, day))
                    if (reservation.Activity == PersonActivityKind.Work)
                        firstWorkMinute = Mathf.Min(firstWorkMinute, reservation.StartMinuteOfDay);
                if (firstWorkMinute < int.MaxValue) nominalStart = firstWorkMinute / 1440f;
            }
            if (person.homeBuildingId < 0 || person.workplaceBuildingId < 0
                || pathingManager == null || townWorld == null || pathingManager.Settings == null
                || townWorld.Grid == null) return nominalStart;

            if (!workCommuteEstimates.TryGetValue(person.id, out WorkCommuteEstimate estimate)
                || estimate.Day != day || estimate.HomeBuildingId != person.homeBuildingId
                || estimate.WorkplaceBuildingId != person.workplaceBuildingId)
            {
                float minutes = 0f;
                if (TryGetDoorAnchor(person.homeBuildingId, out BuildingAnchor home)
                    && TryGetDoorAnchor(person.workplaceBuildingId, out BuildingAnchor work)
                    && pathingManager.TryFindPath(home, work, out PathingResult route,
                        this, "scheduled worker commute", PathingProfile.Pedestrian)
                    && route != null && route.Success)
                {
                    float meters = 0f;
                    for (int i = 1; i < route.Path.Count; i++)
                    {
                        Vector3 from = pathingManager.CoordToPathWorld(route.Path[i - 1]);
                        Vector3 to = pathingManager.CoordToPathWorld(route.Path[i]);
                        meters += Vector2.Distance(new Vector2(from.x, from.z), new Vector2(to.x, to.z));
                    }
                    float speed = Mathf.Max(0.1f, pathingManager.Settings.pedestrianSpeedMetersPerSecond);
                    minutes = meters / speed / 60f;
                }
                estimate = new WorkCommuteEstimate(day, person.homeBuildingId, person.workplaceBuildingId, minutes);
                workCommuteEstimates[person.id] = estimate;
            }

            // Ten minutes to prepare and a five-minute arrival buffer. A failed route
            // retains the authored profession rhythm instead of inventing a shortcut.
            return CalculateRouteAwareDepartureTime01(nominalStart, estimate.Minutes, 10f, 5f);
        }

        public static float CalculateRouteAwareDepartureTime01(float scheduledStart01,
            float routeMinutes, float preparationMinutes, float arrivalBufferMinutes)
        {
            float minutesBeforeStart = Mathf.Max(0f, routeMinutes)
                + Mathf.Max(0f, preparationMinutes) + Mathf.Max(0f, arrivalBufferMinutes);
            return Mathf.Clamp01(scheduledStart01 - minutesBeforeStart / 1440f);
        }

        private static float GetWorkReturnTime01(PersonState person)
        {
            string profession = person != null ? person.professionId ?? string.Empty : string.Empty;
            if (ContainsAny(profession, "crop", "ranch", "sawmill", "lumber", "wood_fuel"))
            {
                return 0.68f;
            }

            if (ContainsAny(profession, "boarding_house", "saloon"))
            {
                return 0.84f;
            }

            if (ContainsAny(profession, "doctor", "barber"))
            {
                return 0.78f;
            }

            return 0.74f;
        }

        private static bool ContainsAny(string source, params string[] values)
        {
            if (string.IsNullOrWhiteSpace(source) || values == null)
            {
                return false;
            }

            for (int i = 0; i < values.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i])
                    && source.IndexOf(values[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private int SendRouteForState(PopulationScheduleState nextState, Predicate<PersonState> filter, Func<PersonState, int> targetBuildingId)
        {
            int sent = 0;
            for (int i = 0; i < agents.Count; i++)
            {
                ManagedPersonAgent managed = agents[i];
                if (managed == null || managed.person == null || targetBuildingId == null)
                {
                    RecordScheduledRouteFailure(ScheduledRouteFailureKind.CandidateNotReady, managed?.person, -1, nextState);
                    continue;
                }

                if (filter != null && !filter(managed.person))
                {
                    continue;
                }

                if (ShouldPreserveFreightReceivingRoute(managed.freightReceivingTargetBuildingId,
                        managed.mover != null && managed.mover.IsRouteActive,
                        managed.mover != null && managed.mover.IsArrivedVisible))
                    continue;

                if (managed.freightReceivingTargetBuildingId >= 0)
                    managed.freightReceivingTargetBuildingId = -1;

                if (WagonRiderPlacement.ShouldDeferCompetingRoute(managed.transportStagingSeat != null)
                    || (managed.routeMode == AgentRouteMode.TaskDirected && managed.mover != null && managed.mover.IsRouteActive)
                    || (ShoppingTripOwnsPerson != null && ShoppingTripOwnsPerson(managed.person.id)))
                    continue;

                int destinationBuildingId = targetBuildingId(managed.person);
                if (TrySendAgent(managed, destinationBuildingId, nextState, out ScheduledRouteFailureKind failureKind))
                {
                    sent++;
                    continue;
                }

                RecordScheduledRouteFailure(failureKind, managed.person, destinationBuildingId, nextState);
            }

            RefreshMovementStatus($"Sent {sent} agents for {nextState}.");
            return sent;
        }

        /// <summary>
        /// Starts a named person's existing world-space custom path for a task.
        /// Arrival-driven travel remains open until the actor reaches the target
        /// anchor; a JourneyModel estimate does not complete the task.
        /// </summary>
        public bool TryStartExplicitRoute(int personId, int targetBuildingId, PopulationScheduleState nextState)
        {
            return TryStartExplicitRoute(personId, targetBuildingId, nextState, out _);
        }

        /// <summary>Starts a task-directed route and returns the concrete path/anchor refusal when it cannot.</summary>
        public bool TryStartExplicitRoute(int personId, int targetBuildingId,
            PopulationScheduleState nextState, out string refusal)
        {
            refusal = string.Empty;
            if (personId < 0 || targetBuildingId < 0)
            {
                refusal = "Person or destination building ID is invalid.";
                return false;
            }
            for (int i = 0; i < agents.Count; i++)
            {
                ManagedPersonAgent managed = agents[i];
                if (managed?.person == null || managed.person.id != personId) continue;
                // Slice R1 (Objective 22): a rider aboard the wagon must
                // never be route-started — StartRoute would snap the
                // parented agent off the seat. F3 blocks task starts;
                // this flag blocks route starts.
                if (managed.isRiding)
                {
                    refusal = "Resident is currently aboard a wagon.";
                    return false;
                }
                if (WagonRiderPlacement.ShouldDeferCompetingRoute(managed.transportStagingSeat != null))
                {
                    refusal = "Resident is physically approaching an assigned freight wagon; the boarding route must complete or be released before another task can claim movement.";
                    return false;
                }
                // A route already owned by another task is not ours to
                // retarget.  Its task authority must observe arrival or
                // cancel it before this task can claim a different route.
                // This is especially important for freight receiving: a
                // later business-feed trip must not replace the carrier's
                // active journey to the receiving premises.
                if (managed.mover != null && managed.mover.IsRouteActive
                    && ShouldPreserveActiveTaskRoute(true,
                        managed.routeMode == AgentRouteMode.TaskDirected,
                        managed.routeMode == AgentRouteMode.Scheduled,
                        managed.person.currentDestinationBuildingId, targetBuildingId))
                {
                    refusal = $"Resident already has an active task or scheduled route to building {managed.person.currentDestinationBuildingId}; it must arrive or be released before routing to building {targetBuildingId}.";
                    return false;
                }
                if (managed.person.currentDestinationBuildingId == targetBuildingId
                    && managed.mover != null && (managed.mover.IsArrivedVisible || managed.mover.IsRouteActive))
                {
                    managed.person.scheduleState = nextState;
                    managed.routeMode = AgentRouteMode.TaskDirected;
                    return true;
                }
                // Fix (B-5): a Hidden mover refuses every route method.
                // A route request is the recovery point: show the actor at
                // its current authoritative location (populated at the home
                // door anchor) and proceed. This does not alter mover
                // initialization.
                if (managed.mover != null && managed.mover.IsHidden)
                    managed.mover.ShowAtCurrentLocation();
                if (!TrySendAgent(managed, targetBuildingId, nextState,
                        out ScheduledRouteFailureKind failureKind, out refusal,
                        $"destination=building:{targetBuildingId}"))
                {
                    if (string.IsNullOrWhiteSpace(refusal)) refusal = failureKind.ToString();
                    return false;
                }
                // A business task owns this route's destination. The normal
                // workday scheduler must not reinterpret it as a commute and
                // send the actor home before the task authority observes arrival.
                managed.routeMode = AgentRouteMode.TaskDirected;
                return true;
            }
            refusal = "No managed visible agent is registered for this resident.";
            return false;
        }

        internal static bool ShouldPreserveActiveTaskRoute(bool routeActive, bool taskDirected,
            bool scheduled, int activeDestinationBuildingId, int requestedDestinationBuildingId)
        {
            return routeActive
                && activeDestinationBuildingId != requestedDestinationBuildingId
                && (taskDirected || scheduled);
        }

        internal static bool ShouldPreserveFreightReceivingRoute(int claimedBuildingId,
            bool routeActive, bool arrivedVisible)
        {
            return claimedBuildingId >= 0 && (routeActive || arrivedVisible);
        }

        internal static bool ShouldDeferRouteForFreightReceiving(int claimedBuildingId,
            int requestedDestinationBuildingId)
        {
            return claimedBuildingId >= 0 && requestedDestinationBuildingId != claimedBuildingId;
        }

        internal static bool ShouldDeferRouteForFreightMovement(bool hasStagingClaim,
            int receivingBuildingId, int requestedDestinationBuildingId)
        {
            return WagonRiderPlacement.ShouldDeferCompetingRoute(hasStagingClaim)
                || ShouldDeferRouteForFreightReceiving(receivingBuildingId, requestedDestinationBuildingId);
        }

        internal static bool ShouldDeferDomesticActivityForFreightMovement(bool hasStagingClaim,
            int receivingBuildingId)
        {
            // Domestic activity routes start directly from TryReachDomesticActivity
            // and bypass TrySendAgent. Freight owns the person's physical route until
            // its carrier authority completes or releases that movement.
            return hasStagingClaim || receivingBuildingId >= 0;
        }

        /// <summary>Confirms the visible actor has completed its route to the requested building.</summary>
        public bool IsPersonPhysicallyAtBuilding(int personId, int buildingId)
        {
            if (personId < 0 || buildingId < 0) return false;
            for (int i = 0; i < agents.Count; i++)
            {
                ManagedPersonAgent managed = agents[i];
                if (managed?.person == null || managed.person.id != personId) continue;
                return managed.person.currentDestinationBuildingId == buildingId
                    && managed.mover != null
                    && managed.mover.IsArrivedVisible
                    && !managed.mover.IsRouteActive
                    && managed.mover.gameObject.activeInHierarchy;
            }
            return false;
        }

        public bool HasActiveTaskRouteToBuilding(int personId, int buildingId)
        {
            foreach (ManagedPersonAgent managed in agents)
                if (managed?.person != null && managed.person.id == personId)
                    return managed.routeMode == AgentRouteMode.TaskDirected
                        && managed.person.currentDestinationBuildingId == buildingId
                        && managed.mover != null && managed.mover.IsRouteActive;
            return false;
        }

        /// <summary>
        /// Returns a concrete reason a resident's mover cannot be reassigned
        /// to a household shopping trip. Schedule availability alone does not
        /// reveal a freight receiving claim or a task-directed route that is
        /// already owned by another authority.
        /// </summary>
        public string GetShopperMovementBlockReason(int personId)
        {
            ManagedPersonAgent managed = null;
            foreach (ManagedPersonAgent candidate in agents)
            {
                if (candidate?.person == null || candidate.person.id != personId) continue;
                managed = candidate;
                break;
            }

            if (managed == null)
                return $"no managed physical actor is registered for P{personId}";
            if (managed.mover == null)
                return $"P{personId} has no physical movement component";
            if (managed.isRiding)
                return $"P{personId} is aboard a wagon";
            if (WagonRiderPlacement.ShouldDeferCompetingRoute(managed.transportStagingSeat != null))
                return $"P{personId} is approaching an assigned freight wagon";
            if (managed.freightReceivingTargetBuildingId >= 0)
                return $"P{personId} is carrying freight to receiving building {managed.freightReceivingTargetBuildingId}";
            if (managed.mover.IsRouteActive
                && (managed.routeMode == AgentRouteMode.TaskDirected
                    || managed.routeMode == AgentRouteMode.Scheduled))
            {
                return $"P{personId} has an active {managed.routeMode.ToString().ToLowerInvariant()} route "
                    + $"to building {managed.person.currentDestinationBuildingId}";
            }

            return null;
        }

        public float? GetTaskRouteRemainingSeconds(int personId, int buildingId)
        {
            foreach (ManagedPersonAgent managed in agents)
                if (managed?.person != null && managed.person.id == personId
                    && managed.routeMode == AgentRouteMode.TaskDirected
                    && managed.person.currentDestinationBuildingId == buildingId)
                    return managed.mover != null ? managed.mover.EstimatedRemainingRouteSeconds : null;
            return null;
        }

        public void CancelTaskRouteToBuilding(int personId, int buildingId)
        {
            foreach (ManagedPersonAgent managed in agents)
                if (managed?.person != null && managed.person.id == personId
                    && managed.routeMode == AgentRouteMode.TaskDirected
                    && managed.person.currentDestinationBuildingId == buildingId)
                {
                    managed.mover?.CancelActiveRoute();
                    managed.routeMode = AgentRouteMode.None;
                    return;
                }
        }

        /// <summary>
        /// Physically routes an existing on-map freight handler to an authored
        /// business activity point. The packaged crate is an attachment only;
        /// shipment quantity and custody remain with the freight authorities.
        /// </summary>
        public bool TryCarryFreightToActivity(int personId, int targetBuildingId, string activityId,
            out bool arrived, out string reason)
        {
            return TryCarryFreightToActivity(personId, targetBuildingId, activityId, out arrived, out reason, out _);
        }

        /// <summary>
        /// Lane 9d (M-R5): same as the five-argument form, plus
        /// <paramref name="usedDoorFallback"/> — true when the handler was
        /// routed to the authored front door instead of a receiving
        /// station, so the caller can record the delivery point honestly
        /// ("door:&lt;business&gt;") instead of the misleading
        /// "receiving:&lt;business&gt;".
        /// </summary>
        public bool TryCarryFreightToActivity(int personId, int targetBuildingId, string activityId,
            out bool arrived, out string reason, out bool usedDoorFallback)
        {
            arrived = false;
            reason = string.Empty;
            usedDoorFallback = false;
            if (personId < 0 || targetBuildingId < 0 || townWorld == null || pathingManager?.Grid == null
                || !townWorld.TryGetBuildingById(targetBuildingId, out PlacedBuilding building)
                || building?.runtimeFootprintAuthority == null)
            {
                reason = "The freight handler or destination building is unavailable.";
                return false;
            }
            BuildingFootprintAuthority authority = building.runtimeFootprintAuthority;
            // Lane 9d (M-R5): no location carries the id "receiving" on any
            // building in the dev town, so the exact-id lookup fails
            // everywhere and every delivery falls back to the front door.
            // Resolve by Receiving type instead: an authored Receiving-type
            // point (e.g. the lumber yard's
            // "lumberyard-receiving-approach") is a real receiving station
            // and deliveries route to it. Only when no such point exists —
            // or it cannot be resolved as a walkable location — does the
            // door fallback apply, and then explicitly.
            bool resolved = authority.TryGetActivityLocation(activityId, targetBuildingId,
                out ResolvedBuildingActivityLocation activity, out reason);
            if (!resolved && string.Equals(activityId, "receiving", StringComparison.Ordinal))
            {
                resolved = TryResolveReceivingPointByType(authority, targetBuildingId, out activity, out reason);
            }
            if (!resolved)
            {
                // Some operating buildings have a real authored exterior door but
                // no separately authored receiving station. Freight may be handed
                // off at that doorway; never substitute the blocked drop-off cell
                // or infer arrival from shipment state.
                bool hasAuthoredDoor = building.TryGetAnchor(AnchorType.FrontDoor, out BuildingAnchor authoredDoor);
                if (CanUseAuthoredDoorForFreightReceiving(activityId, hasAuthoredDoor ? authoredDoor : default))
                {
                    if (TryCarryFreightToAuthoredDoor(personId, targetBuildingId, building, out arrived, out string doorReason))
                    {
                        usedDoorFallback = true;
                        reason = ResolveFreightDoorRouteReason(arrived, doorReason);
                        return true;
                    }
                    if (!string.IsNullOrEmpty(doorReason)) reason = $"{reason} Doorway fallback: {doorReason}";
                }
                return false;
            }

            if (!activity.RequiresWalkableInterior
                && ShouldUseAuthoredDoorForBlockedExteriorActivity(
                    pathingManager.Grid, pathingManager, activity.WorldPosition))
            {
                bool hasAuthoredDoor = building.TryGetAnchor(AnchorType.FrontDoor, out BuildingAnchor authoredDoor);
                if (CanUseAuthoredDoorForFreightReceiving(activityId, hasAuthoredDoor ? authoredDoor : default))
                {
                    if (TryCarryFreightToAuthoredDoor(personId, targetBuildingId, building,
                            out arrived, out string doorReason))
                    {
                        usedDoorFallback = true;
                        reason = arrived ? string.Empty : doorReason;
                        return true;
                    }
                    reason = string.IsNullOrWhiteSpace(doorReason)
                        ? "The exterior receiving point is blocked and its authored doorway route could not start."
                        : doorReason;
                    return false;
                }
                reason = $"Receiving activity '{activityId}' is not an interior station, but its world point is outside the navigable exterior and no authorized authored front door is available.";
                return false;
            }

            for (int i = 0; i < agents.Count; i++)
            {
                ManagedPersonAgent managed = agents[i];
                if (managed?.person == null || managed.person.id != personId || managed.mover == null) continue;
                AgentMover mover = managed.mover;
                float arrivalRadius = Mathf.Max(1f, pathingManager.Grid.CellSizeMeters * 0.7f);
                Vector3 actorPosition = mover.transform.position;
                float distance = Vector2.Distance(new Vector2(actorPosition.x, actorPosition.z),
                    new Vector2(activity.WorldPosition.x, activity.WorldPosition.z));
                if (!mover.IsRouteActive && mover.IsArrivedVisible
                    && managed.person.currentDestinationBuildingId == targetBuildingId
                    && distance <= arrivalRadius)
                {
                    arrived = true;
                    managed.freightReceivingTargetBuildingId = -1;
                    mover.SetFreightCargoVisible(true);
                    Vector3 forward = activity.WorldForward;
                    forward.y = 0f;
                    if (forward.sqrMagnitude > 0.0001f) mover.transform.rotation = Quaternion.LookRotation(forward.normalized);
                    return true;
                }

                if (mover.IsRouteActive)
                {
                    if (managed.person.currentDestinationBuildingId == targetBuildingId)
                    {
                        // A task-owned route to this same building is valid
                        // progress toward its activity/door. Keep it owned;
                        // after exterior arrival the next call can continue
                        // through the authorized interior route.
                        managed.routeMode = AgentRouteMode.TaskDirected;
                        managed.freightReceivingTargetBuildingId = targetBuildingId;
                        mover.SetFreightCargoVisible(true);
                        reason = DescribeFreightRouteProgress(mover, targetBuildingId, activityId, activity.WorldPosition);
                        return true;
                    }
                    if (managed.routeMode == AgentRouteMode.AmbientOutbound
                        || managed.routeMode == AgentRouteMode.AmbientReturning
                        || managed.routeMode == AgentRouteMode.None)
                    {
                        // Ambient presentation has no task or schedule
                        // reservation to preserve. It may yield to this
                        // already-authorized receiving activity.
                        mover.CancelActiveRoute();
                        managed.routeMode = AgentRouteMode.None;
                    }
                    else
                    {
                        reason = $"The freight handler is already traveling for {managed.routeMode} to building {managed.person.currentDestinationBuildingId}; receiving will wait for that route to resolve.";
                        return false;
                    }
                }

                // If a previous attempt's route ended without arrival, clear
                // its ownership before retrying from the actor's actual cell.
                // The current callback then either starts a fresh route or
                // returns a concrete refusal.
                managed.freightReceivingTargetBuildingId = -1;

                mover.SetFreightCargoVisible(true);

                if (activity.RequiresWalkableInterior)
                {
                    if (authority.TryEvaluateInteriorWorldPoint(mover.transform.position,
                            activity.FloorIndex, activity.FunctionalSpaceId, out _))
                    {
                        if (!mover.BeginBuildingInteriorRoute(authority, activityId, out reason))
                        {
                            mover.SetFreightCargoVisible(false);
                            return false;
                        }
                        MarkFreightRouteStarted(managed.person, targetBuildingId, true);
                        managed.routeMode = AgentRouteMode.TaskDirected;
                        managed.freightReceivingTargetBuildingId = targetBuildingId;
                        reason = DescribeFreightRouteProgress(mover, targetBuildingId, activityId, activity.WorldPosition);
                        return true;
                    }
                    if (!authority.TryGetEntrancePair("main-entrance", out BuildingEntrancePairAuthoring entrance, out reason))
                    {
                        mover.SetFreightCargoVisible(false);
                        return false;
                    }
                    Vector3 outdoor = authority.transform.TransformPoint(entrance.outdoorApproachLocal);
                    float stagingRadius = Mathf.Max(1.25f, entrance.clearWidthMeters * 1.5f);
                    if (Vector3.Distance(mover.transform.position, outdoor) <= stagingRadius)
                    {
                        if (!mover.BeginBuildingActivityRoute(authority, "main-entrance", activityId))
                        {
                            reason = "The authored doorway-to-activity route could not start.";
                            mover.SetFreightCargoVisible(false);
                            return false;
                        }
                        MarkFreightRouteStarted(managed.person, targetBuildingId, true);
                        managed.routeMode = AgentRouteMode.TaskDirected;
                        managed.freightReceivingTargetBuildingId = targetBuildingId;
                        reason = DescribeFreightRouteProgress(mover, targetBuildingId, activityId, activity.WorldPosition);
                        return true;
                    }
                }
                else
                {
                    GridCoord start = pathingManager.Grid.WorldToCoord(mover.transform.position);
                    GridCoord target = pathingManager.Grid.WorldToCoord(activity.WorldPosition);
                    if (mover.BeginGridRoute(start, target))
                    {
                        MarkFreightRouteStarted(managed.person, targetBuildingId, true);
                        managed.routeMode = AgentRouteMode.TaskDirected;
                        managed.freightReceivingTargetBuildingId = targetBuildingId;
                        reason = DescribeFreightRouteProgress(mover, targetBuildingId, activityId, activity.WorldPosition);
                        return true;
                    }
                    reason = "No navigable route reaches the authored receiving point.";
                    mover.SetFreightCargoVisible(false);
                    return false;
                }

                if (!TryStartFreightRoute(managed.person, targetBuildingId,
                        () => TrySendAgent(managed, targetBuildingId, PopulationScheduleState.AtWork, out _)))
                {
                    reason = "No valid town route reaches the destination building entrance.";
                    mover.SetFreightCargoVisible(false);
                    return false;
                }
                // TrySendAgent marks ordinary commute routes as Scheduled.
                // This route is owned by the active physical receiving task,
                // so restore task ownership immediately after it starts.
                managed.routeMode = AgentRouteMode.TaskDirected;
                managed.freightReceivingTargetBuildingId = targetBuildingId;
                reason = DescribeFreightRouteProgress(mover, targetBuildingId, activityId, activity.WorldPosition);
                return true;
            }
            reason = $"No visible managed agent exists for freight handler P{personId}.";
            return false;
        }

        private bool TryCarryFreightToAuthoredDoor(int personId, int targetBuildingId, PlacedBuilding building,
            out bool arrived, out string reason)
        {
            arrived = false;
            reason = string.Empty;
            if (building == null || !building.TryGetAnchor(AnchorType.FrontDoor, out BuildingAnchor authoredDoor)
                || !authoredDoor.fromAuthoredMarker
                || !TryGetDoorAnchor(targetBuildingId, out BuildingAnchor routeDoor, out _))
            {
                reason = "The destination has no valid authored front-door approach.";
                return false;
            }

            Vector3 destination = pathingManager.CoordToPathWorld(routeDoor.coord);
            float arrivalRadius = Mathf.Max(1f, pathingManager.Grid.CellSizeMeters * 0.7f);
            for (int i = 0; i < agents.Count; i++)
            {
                ManagedPersonAgent managed = agents[i];
                if (managed?.person == null || managed.person.id != personId || managed.mover == null) continue;

                AgentMover mover = managed.mover;
                Vector3 actorPosition = mover.transform.position;
                float distance = Vector2.Distance(new Vector2(actorPosition.x, actorPosition.z),
                    new Vector2(destination.x, destination.z));
                if (!mover.IsRouteActive && mover.IsArrivedVisible && distance <= arrivalRadius
                    && managed.person.currentDestinationBuildingId == targetBuildingId)
                {
                    arrived = true;
                    managed.freightReceivingTargetBuildingId = -1;
                    mover.SetFreightCargoVisible(true);
                    Vector3 forward = building.runtimeFootprintAuthority.transform.forward;
                    forward.y = 0f;
                    if (forward.sqrMagnitude > 0.0001f) mover.transform.rotation = Quaternion.LookRotation(forward.normalized);
                    return true;
                }

                if (mover.IsRouteActive)
                {
                    if (managed.person.currentDestinationBuildingId == targetBuildingId)
                    {
                        // This route already reaches the receiving premises;
                        // retain it and let the next arrival check continue.
                        managed.routeMode = AgentRouteMode.TaskDirected;
                        managed.freightReceivingTargetBuildingId = targetBuildingId;
                        mover.SetFreightCargoVisible(true);
                        reason = DescribeFreightRouteProgress(mover, targetBuildingId, "receiving", destination);
                        return true;
                    }
                    if (managed.routeMode == AgentRouteMode.AmbientOutbound
                        || managed.routeMode == AgentRouteMode.AmbientReturning
                        || managed.routeMode == AgentRouteMode.None)
                    {
                        mover.CancelActiveRoute();
                        managed.routeMode = AgentRouteMode.None;
                    }
                    else
                    {
                        reason = $"The freight handler is already traveling for {managed.routeMode} to building {managed.person.currentDestinationBuildingId}; receiving will wait for that route to resolve.";
                        return false;
                    }
                }

                managed.freightReceivingTargetBuildingId = -1;

                mover.SetFreightCargoVisible(true);
                if (!TrySendAgent(managed, targetBuildingId, PopulationScheduleState.AtWork,
                        out _, out reason))
                {
                    mover.SetFreightCargoVisible(false);
                    return false;
                }

                MarkFreightRouteStarted(managed.person, targetBuildingId, true);
                managed.routeMode = AgentRouteMode.TaskDirected;
                managed.freightReceivingTargetBuildingId = targetBuildingId;
                reason = DescribeFreightRouteProgress(mover, targetBuildingId, "receiving", destination);
                return true;
            }

            reason = $"No visible managed agent exists for freight handler P{personId}.";
            return false;
        }

        internal static bool TryStartFreightRoute(PersonState person, int targetBuildingId,
            System.Func<bool> routeStarter)
        {
            if (person == null || targetBuildingId < 0 || routeStarter == null || !routeStarter()) return false;
            person.scheduleState = PopulationScheduleState.AtWork;
            person.currentDestinationBuildingId = targetBuildingId;
            return true;
        }

        internal static bool MarkFreightRouteStarted(PersonState person, int targetBuildingId, bool routeStarted)
        {
            if (!routeStarted || person == null || targetBuildingId < 0) return false;
            person.scheduleState = PopulationScheduleState.AtWork;
            person.currentDestinationBuildingId = targetBuildingId;
            return true;
        }

        public void ReleaseFreightReceivingRoute(int personId, int targetBuildingId)
        {
            for (int i = 0; i < agents.Count; i++)
            {
                ManagedPersonAgent managed = agents[i];
                if (managed?.person == null || managed.person.id != personId
                    || managed.freightReceivingTargetBuildingId != targetBuildingId) continue;
                managed.freightReceivingTargetBuildingId = -1;
                return;
            }
        }

        /// <summary>
        /// Cancels a domestic-activity route only when the resident's active
        /// route is still owned by the supplied obligation and destination.
        /// Sleep reservations use this when a night expires before arrival;
        /// a competing work or freight route is never cancelled.
        /// </summary>
        public bool CancelDomesticActivityRoute(int personId, string taskId, string activityLocationId)
        {
            if (personId < 0 || string.IsNullOrWhiteSpace(taskId)
                || string.IsNullOrWhiteSpace(activityLocationId)) return false;
            foreach (ManagedPersonAgent managed in agents)
            {
                if (managed?.person == null || managed.person.id != personId
                    || !string.Equals(managed.domesticActivityTaskId, taskId, StringComparison.Ordinal)
                    || !string.Equals(managed.domesticActivityLocationId, activityLocationId, StringComparison.Ordinal))
                    continue;
                if (managed.mover != null && managed.mover.IsRouteActive
                    && managed.mover.RouteGeneration == managed.domesticActivityRouteGeneration)
                    managed.mover.CancelActiveRoute();
                ClearDomesticActivityRoute(managed);
                if (managed.routeMode == AgentRouteMode.TaskDirected)
                    managed.routeMode = AgentRouteMode.None;
                return true;
            }
            return false;
        }

        internal static string ResolveFreightDoorRouteReason(bool arrived, string routeReason)
        {
            if (arrived) return string.Empty;
            return string.IsNullOrWhiteSpace(routeReason)
                ? "Carrying: handler is physically travelling to the receiving point."
                : routeReason;
        }

        /// <summary>
        /// Lane 9d (M-R5): resolves a "receiving" request by Receiving type
        /// when no location carries the literal id. Maps the building's
        /// authored locations to the pure resolver's plain candidates, then
        /// validates the resolved id through the same walkable-location
        /// lookup the exact-id path uses — a type match that fails the
        /// interior check is not a receiving station, and the caller falls
        /// back to the door explicitly instead of routing to an unreachable
        /// point.
        /// </summary>
        private static bool TryResolveReceivingPointByType(BuildingFootprintAuthority authority, int targetBuildingId,
            out ResolvedBuildingActivityLocation activity, out string reason)
        {
            activity = default;
            reason = string.Empty;
            if (authority?.ActivityLocations == null)
            {
                reason = "No receiving station is authored on this building; the front door is the only fallback.";
                return false;
            }
            var candidates = new List<FreightReceivingPointResolver.AuthoredReceivingPoint>();
            foreach (BuildingActivityLocationAuthoring location in authority.ActivityLocations)
            {
                if (location == null) continue;
                candidates.Add(new FreightReceivingPointResolver.AuthoredReceivingPoint(
                    location.id, (int)location.type));
            }
            FreightReceivingPointResolver.ReceivingPointResolution resolution =
                FreightReceivingPointResolver.Resolve("receiving", candidates,
                    (int)BuildingActivityLocationType.Receiving);
            if (resolution.Source != FreightReceivingPointResolver.ReceivingPointSource.ReceivingType)
            {
                reason = resolution.Reason;
                return false;
            }
            if (!authority.TryGetActivityLocation(resolution.ActivityLocationId, targetBuildingId, out activity, out reason))
            {
                reason = "The authored Receiving-type point '" + resolution.ActivityLocationId
                    + "' could not be resolved as a walkable location: " + reason
                    + " The front door is the only fallback.";
                return false;
            }
            return true;
        }

        private static string DescribeFreightRouteProgress(AgentMover mover, int targetBuildingId,
            string activityId, Vector3 destination)
        {
            if (mover == null) return "Carrying: waiting for a physical receiving route.";
            string eta = mover.EstimatedRemainingRouteSeconds.HasValue
                ? mover.EstimatedRemainingRouteSeconds.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "s"
                : "n/a";
            return $"Carrying: route to B{targetBuildingId}/{activityId}; active={mover.IsRouteActive}, actor={mover.transform.position}, target={destination}, remaining={mover.RemainingWorldRouteDistance:0.0}m, eta={eta}.";
        }

        internal static bool CanUseAuthoredDoorForFreightReceiving(string activityId, BuildingAnchor frontDoor)
        {
            return string.Equals(activityId, "receiving", System.StringComparison.OrdinalIgnoreCase)
                && frontDoor.fromAuthoredMarker;
        }

        internal static bool ShouldUseAuthoredDoorForBlockedExteriorActivity(TownGrid grid,
            PathingManager pathing, Vector3 activityWorldPosition)
        {
            if (grid == null || pathing == null) return true;
            GridCoord coord = grid.WorldToCoord(activityWorldPosition);
            if (!grid.IsInBounds(coord)) return true;
            TownCell cell = grid.GetCell(coord);
            return cell.HasBuilding || !pathing.TryGetMovementCost(coord, out _);
        }

        public void HideFreightCargoVisual(int personId)
        {
            for (int i = 0; i < agents.Count; i++)
            {
                ManagedPersonAgent managed = agents[i];
                if (managed?.person == null || managed.person.id != personId) continue;
                managed.mover?.HideFreightCargoVisual();
                return;
            }
        }

        /// <summary>Routes a domestic task to an authored residential activity point.</summary>
        public DomesticActivityMovementResult TryReachDomesticActivity(
            DomesticLaborDriver.InFlightDomesticTask flight, string activityLocationId, out string reason)
        {
            return TryReachDomesticActivity(flight, activityLocationId, double.NaN, out reason);
        }

        /// <summary>
        /// Routes a domestic task using the same authoritative clock sample
        /// that advanced its driver tick. This prevents route watchdogs from
        /// silently disabling themselves when the presentation director's
        /// optional TimeManager lookup is unavailable or stale.
        /// </summary>
        public DomesticActivityMovementResult TryReachDomesticActivity(
            DomesticLaborDriver.InFlightDomesticTask flight, string activityLocationId,
            double currentGameSeconds, out string reason)
        {
            reason = string.Empty;
            if (flight == null || flight.PersonId < 0 || activityLocationId == null
                || townWorld == null || pathingManager == null)
            {
                reason = "Residential route, person, or activity authority is unavailable.";
                return DomesticActivityMovementResult.Unsupported;
            }

            ManagedPersonAgent managed = null;
            foreach (ManagedPersonAgent candidate in agents)
                if (candidate?.person != null && candidate.person.id == flight.PersonId) { managed = candidate; break; }
            PersonState resident = populationManager?.State?.GetPerson(flight.PersonId);
            string presentationFailure = DomesticActivityPresentationFailure(
                resident != null && resident.isOffMap,
                managed != null,
                managed?.mover != null,
                managed != null && managed.isRiding);
            if (!string.IsNullOrEmpty(presentationFailure))
            {
                reason = presentationFailure;
                return DomesticActivityMovementResult.Unsupported;
            }
            // Fix (B-5): a Hidden mover refuses every route method, so a
            // route request is the recovery point. Show the actor at its
            // current authoritative location (populated at the home door
            // anchor) and proceed. This does not alter mover
            // initialization; it only recovers a hidden actor when a route
            // is actually requested. (The presentation check above already
            // guarantees managed.mover is non-null here.)
            if (managed.mover.IsHidden)
                managed.mover.ShowAtCurrentLocation();
            if (ShouldDeferDomesticActivityForFreightMovement(
                    managed.transportStagingSeat != null,
                    managed.freightReceivingTargetBuildingId))
            {
                reason = managed.transportStagingSeat != null
                    ? "Domestic activity is deferred while the resident physically approaches an assigned freight wagon."
                    : $"Domestic activity is deferred while the resident carries freight to receiving building {managed.freightReceivingTargetBuildingId}.";
                return DomesticActivityMovementResult.Unsupported;
            }
            if (managed.person.homeBuildingId < 0
                || !townWorld.TryGetBuildingById(managed.person.homeBuildingId, out PlacedBuilding home)
                || home?.runtimeFootprintAuthority == null)
            {
                reason = "The resident has no placed home with a BuildingFootprintAuthority.";
                return DomesticActivityMovementResult.Unsupported;
            }

            BuildingFootprintAuthority authority = home.runtimeFootprintAuthority;
            if (!authority.TryGetActivityLocation(activityLocationId, home.id,
                    out ResolvedBuildingActivityLocation activity, out reason))
                return DomesticActivityMovementResult.Unsupported;
            if (activity.RequiresWalkableInterior
                && !authority.TryBuildInteriorActivityRoute("main-entrance", activityLocationId,
                    out _, out reason))
                return DomesticActivityMovementResult.Unsupported;

            flight.BuildingId = home.id;
            flight.ActivityLocationId = activityLocationId;
            float arrivalRadius = Mathf.Max(0.45f, pathingManager.Grid != null
                ? pathingManager.Grid.CellSizeMeters * 0.65f : 0.75f);
            Vector3 actor = managed.mover.transform.position;
            actor.y = activity.WorldPosition.y;
            if (!managed.mover.IsRouteActive && managed.mover.IsArrivedVisible
                && Vector3.Distance(actor, activity.WorldPosition) <= arrivalRadius)
            {
                Vector3 facing = activity.WorldForward;
                facing.y = 0f;
                if (facing.sqrMagnitude > 0.0001f)
                    managed.mover.transform.rotation = Quaternion.LookRotation(facing.normalized, Vector3.up);
                return DomesticActivityMovementResult.Arrived;
            }

            double routeClock = ResolveDomesticActivityRouteClock(
                currentGameSeconds, CurrentSimulationGameSeconds);
            bool sameTaskAndDestination = ShouldPreserveDomesticActivityRouteDeadline(
                managed.domesticActivityTaskId, managed.domesticActivityLocationId,
                managed.domesticActivityRouteStartedGameSeconds, flight.TaskIdString, activityLocationId);
            bool currentlyOwnedRoute = !managed.mover.IsRouteActive
                || ShouldContinueOwnedDomesticActivityRoute(
                    managed.domesticActivityTaskId, managed.domesticActivityRouteGeneration,
                    managed.domesticActivityLocationId, flight.TaskIdString, activityLocationId,
                    managed.mover.RouteGeneration);
            if (sameTaskAndDestination && currentlyOwnedRoute
                && ShouldAbortStalledDomesticActivityRoute(routeClock,
                    managed.domesticActivityRouteStartedGameSeconds,
                    managed.domesticActivityLastProgressGameSeconds,
                    managed.domesticActivityNoProgressTimeoutGameSeconds,
                    managed.domesticActivityRouteTimeoutGameSeconds,
                    managed.mover.RemainingWorldRouteDistance,
                    managed.domesticActivityLastRemainingDistance))
            {
                string taskId = managed.domesticActivityTaskId;
                string personId = managed.person != null ? $"P{managed.person.id}" : "unknown";
                string position = FormatRouteDiagnosticVector(managed.mover.transform.position);
                float remainingDistance = managed.mover.RemainingWorldRouteDistance;
                double totalElapsed = routeClock - managed.domesticActivityRouteStartedGameSeconds;
                double noProgressElapsed = routeClock - managed.domesticActivityLastProgressGameSeconds;
                string measuredSpeed = managed.mover.MeasuredMovementSpeed.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                string diagnostic = FormatDomesticActivityRouteWatchdogDiagnostic(
                    taskId, personId, activityLocationId, managed.mover.RouteGeneration,
                    managed.mover.IsRouteActive, managed.mover.IsHidden, position,
                    remainingDistance, managed.domesticActivityLastRemainingDistance,
                    measuredSpeed, totalElapsed, noProgressElapsed,
                    managed.domesticActivityNoProgressTimeoutGameSeconds,
                    managed.domesticActivityRouteTimeoutGameSeconds);
                if (managed.mover.IsRouteActive) managed.mover.CancelActiveRoute();
                ClearDomesticActivityRoute(managed);
                managed.routeMode = AgentRouteMode.None;
                reason = diagnostic;
                return DomesticActivityMovementResult.Unsupported;
            }

            if (managed.mover.IsRouteActive)
            {
                if (ShouldContinueOwnedDomesticActivityRoute(
                        managed.domesticActivityTaskId, managed.domesticActivityRouteGeneration,
                        managed.domesticActivityLocationId, flight.TaskIdString, activityLocationId,
                        managed.mover.RouteGeneration))
                {
                    double now = routeClock;
                    float remaining = managed.mover.RemainingWorldRouteDistance;
                    if (IsDomesticActivityRouteMakingProgress(remaining, managed.domesticActivityLastRemainingDistance))
                    {
                        managed.domesticActivityLastRemainingDistance = remaining;
                        if (!double.IsNaN(now) && !double.IsInfinity(now))
                            managed.domesticActivityLastProgressGameSeconds = now;
                    }
                    return DomesticActivityMovementResult.Traveling;
                }

                reason = $"The resident is already following another physical route ({managed.routeMode}); domestic activity '{activityLocationId}' was deferred without taking over that route.";
                return DomesticActivityMovementResult.Unsupported;
            }

            if (activity.RequiresWalkableInterior)
            {
                if (authority.TryEvaluateInteriorWorldPoint(managed.mover.transform.position,
                        activity.FloorIndex, activity.FunctionalSpaceId, out _))
                {
                    if (!managed.mover.BeginBuildingInteriorRoute(authority, activityLocationId, out reason))
                    {
                        RecordMovementTrace(managed, "refused", "ActivityLocationRouteFailed", home.id, reason, activityLocationId);
                        return DomesticActivityMovementResult.Unsupported;
                    }
                    managed.person.scheduleState = PopulationScheduleState.AtHome;
                    managed.person.currentDestinationBuildingId = home.id;
                    managed.routeMode = AgentRouteMode.TaskDirected;
                    RecordMovementTrace(managed, "started", managed.mover.CurrentStateName, home.id, string.Empty, activityLocationId);
                    return TrackDomesticActivityRoute(managed, flight, activityLocationId, routeClock);
                }
                if (!authority.TryGetEntrancePair("main-entrance", out BuildingEntrancePairAuthoring entrance, out reason))
                {
                    RecordMovementTrace(managed, "refused", "MissingEntrance", home.id, reason, activityLocationId);
                    return DomesticActivityMovementResult.Unsupported;
                }
                Vector3 outdoor = authority.transform.TransformPoint(entrance.outdoorApproachLocal);
                float stagingRadius = AgentMover.BuildingActivityStagingRadius(entrance.clearWidthMeters);
                if (Vector3.Distance(managed.mover.transform.position, outdoor) > stagingRadius)
                {
                    if (TryFindReachableInteriorEntrance(authority,
                            managed.mover.transform.position, out BuildingEntrancePairAuthoring interiorEntrance))
                    {
                        // The actor failed validation in the requested activity's
                        // functional space above. A coarse grid cell inside the
                        // building footprint does not make that actor a valid
                        // start point for the same floor/space path request.
                        // Walk the actor to an authored entrance using the actual
                        // entrance floor and functional space instead.
                        if (!managed.mover.BeginBuildingInteriorExitRoute(authority,
                                interiorEntrance.id, out reason))
                        {
                            if (string.IsNullOrWhiteSpace(reason))
                                reason = $"The resident could not physically exit home through authored entrance '{interiorEntrance.id}'.";
                            RecordMovementTrace(managed, "refused", "InteriorExitRouteFailed", home.id, reason, activityLocationId);
                            return DomesticActivityMovementResult.Unsupported;
                        }
                        managed.routeMode = AgentRouteMode.TaskDirected;
                        managed.person.currentDestinationBuildingId = -1;
                        RecordMovementTrace(managed, "started", managed.mover.CurrentStateName, home.id, string.Empty, activityLocationId);
                        return TrackDomesticActivityRoute(managed, flight, activityLocationId, routeClock);
                    }
                    GridCoord actualStart = pathingManager.Grid.WorldToCoord(managed.mover.transform.position);
                    if (home.footprint.Contains(actualStart))
                    {
                        // A rasterized building cell can overlap outdoor space.
                        // Do not ask PathingManager to validate this point as an
                        // interior start; only the authored entrance route above
                        // can establish that the actor is inside and connected.
                        reason = $"The resident's position maps to home {home.id}'s coarse footprint cell, but is not connected to an authored interior entrance or the requested activity space.";
                        RecordMovementTrace(managed, "refused", "InteriorOriginUnreachable", home.id, reason, activityLocationId);
                        return DomesticActivityMovementResult.Unsupported;
                    }
                    if (!TrySendAgent(managed, home.id, PopulationScheduleState.ReturningHome, out _,
                            $"task='{flight.DefinitionId}' activity='{activityLocationId}'"))
                    {
                        reason = "No physical route reaches the resident's home entrance.";
                        return DomesticActivityMovementResult.Unsupported;
                    }
                    managed.routeMode = AgentRouteMode.TaskDirected;
                    return TrackDomesticActivityRoute(managed, flight, activityLocationId, routeClock);
                }

                if (!managed.mover.BeginBuildingActivityRoute(authority, "main-entrance", activityLocationId))
                {
                    reason = "The authored entrance-to-activity route could not start.";
                    RecordMovementTrace(managed, "refused", "ActivityLocationRouteFailed", home.id, reason, activityLocationId);
                    return DomesticActivityMovementResult.Unsupported;
                }
                managed.person.scheduleState = PopulationScheduleState.AtHome;
                managed.person.currentDestinationBuildingId = home.id;
                managed.routeMode = AgentRouteMode.TaskDirected;
                RecordMovementTrace(managed, "started", managed.mover.CurrentStateName, home.id, string.Empty, activityLocationId);
                return TrackDomesticActivityRoute(managed, flight, activityLocationId, routeClock);
            }

            GridCoord exteriorStart = pathingManager.Grid.WorldToCoord(managed.mover.transform.position);
            GridCoord target = pathingManager.Grid.WorldToCoord(activity.WorldPosition);
            if (activity.Type == BuildingActivityLocationType.Porch)
            {
                bool validInteriorOrigin = false;
                Vector3 selectedOutdoor = default;
                GridCoord selectedOutdoorCell = default;
                float selectedDistance = float.PositiveInfinity;
                foreach (BuildingEntrancePairAuthoring entranceCandidate in authority.EntrancePairs)
                {
                    if (entranceCandidate == null
                        || !authority.TryGetEntrancePair(entranceCandidate.id,
                            out BuildingEntrancePairAuthoring entrance, out _)) continue;

                    if (authority.TryEvaluateInteriorWorldPoint(managed.mover.transform.position,
                        entrance.floorIndex, entrance.functionalSpaceId, out _))
                        validInteriorOrigin = true;

                    Vector3 outdoor = authority.transform.TransformPoint(entrance.outdoorApproachLocal);
                    Vector3 toPorch = activity.WorldPosition - outdoor;
                    toPorch.y = 0f;
                    bool directApproachClear = !Physics.Linecast(outdoor + Vector3.up * 0.8f,
                        activity.WorldPosition + Vector3.up * 0.8f, out _, ~0,
                        QueryTriggerInteraction.Ignore);
                    if (!CanApproachPorchActivityDoorway(toPorch.magnitude, directApproachClear)) continue;

                    GridCoord outdoorCell = pathingManager.Grid.WorldToCoord(outdoor);
                    if (!pathingManager.Grid.IsInBounds(outdoorCell)) continue;
                    // The authored outdoor threshold can lie in a coarse grid
                    // cell still occupied by the building footprint. Keep that
                    // cell blocked; route to the first valid exterior cell
                    // along the doorway's outward axis, then follow the short
                    // authored threshold-to-porch segment.
                    Vector3 outward = outdoor - authority.transform.TransformPoint(entrance.indoorPositionLocal);
                    outward.y = 0f;
                    if (outward.sqrMagnitude < 0.01f) continue;
                    outward.Normalize();
                    GridCoord routeCell = default;
                    bool foundExteriorRouteCell = false;
                    float cellSize = pathingManager.Grid.CellSizeMeters;
                    for (float stepDistance = cellSize * 0.5f; stepDistance <= cellSize * 3f; stepDistance += cellSize * 0.25f)
                    {
                        Vector3 probe = outdoor + outward * stepDistance;
                        GridCoord probeCoord = pathingManager.Grid.WorldToCoord(probe);
                        if (probeCoord.Equals(outdoorCell) || !pathingManager.Grid.IsInBounds(probeCoord)) continue;
                        TownCell probeCell = pathingManager.Grid.GetCell(probeCoord);
                        if (probeCell.HasBuilding || probeCell.blocked
                            || !pathingManager.TryGetMovementCost(probeCoord, out _)) continue;

                        Vector3 routePoint = pathingManager.CoordToPathWorld(probeCoord);
                        if (Physics.Linecast(routePoint + Vector3.up * 0.8f,
                            outdoor + Vector3.up * 0.8f, out _, ~0, QueryTriggerInteraction.Ignore)) continue;
                        routeCell = probeCoord;
                        foundExteriorRouteCell = true;
                        break;
                    }
                    if (!foundExteriorRouteCell) continue;

                    float distance = toPorch.sqrMagnitude;
                    if (distance >= selectedDistance) continue;
                    selectedDistance = distance;
                    selectedOutdoor = outdoor;
                    selectedOutdoorCell = routeCell;
                }

                if (selectedDistance == float.PositiveInfinity)
                {
                    reason = "The authored porch point has no nearby unobstructed doorway with a walkable outdoor approach cell.";
                    RecordMovementTrace(managed, "refused", "PorchApproachUnavailable", home.id, reason, activityLocationId);
                    return DomesticActivityMovementResult.Unsupported;
                }

                BuildingAnchor porchTarget = new()
                {
                    type = AnchorType.FrontDoor,
                    coord = selectedOutdoorCell,
                    hasAuthoredWorldPosition = true,
                    authoredWorldPosition = activity.WorldPosition,
                    source = activityLocationId
                };
                if (validInteriorOrigin)
                {
                    if (!managed.mover.BeginInteriorDoorToDoor(authority, porchTarget, out reason))
                    {
                        reason = "The resident has a valid interior origin, but no authorized doorway route reaches the porch activity: " + reason;
                        RecordMovementTrace(managed, "refused", "PorchRouteFailed", home.id, reason, activityLocationId);
                        return DomesticActivityMovementResult.Unsupported;
                    }
                }
                else
                {
                    if (home.footprint.Contains(exteriorStart))
                    {
                        reason = "The resident is within the home footprint but is not on an authorized interior floor point or an exterior route cell.";
                        RecordMovementTrace(managed, "refused", "InvalidInteriorOrigin", home.id, reason, activityLocationId);
                        return DomesticActivityMovementResult.Unsupported;
                    }

                    List<Vector3> finalApproach = new() { selectedOutdoor, activity.WorldPosition };
                    if (!managed.mover.BeginGridRouteWithFinalWaypoints(
                        exteriorStart, selectedOutdoorCell, finalApproach,
                        $"task='{flight.DefinitionId}' activity='{activityLocationId}'"))
                    {
                        reason = $"No valid exterior route from {exteriorStart} reaches doorway approach {selectedOutdoorCell} and porch point '{activityLocationId}'.";
                        RecordMovementTrace(managed, "refused", "PorchRouteFailed", home.id, reason, activityLocationId);
                        return DomesticActivityMovementResult.Unsupported;
                    }
                }

                managed.routeMode = AgentRouteMode.TaskDirected;
                managed.person.currentDestinationBuildingId = home.id;
                RecordMovementTrace(managed, "started", managed.mover.CurrentStateName, home.id, string.Empty, activityLocationId);
                return TrackDomesticActivityRoute(managed, flight, activityLocationId, routeClock);
            }

            // A resident may be physically inside the house even when the
            // exterior activity (for example porch work) uses the ordinary
            // outdoor navigation grid. Preserve the authored doorway gate
            // instead of asking the outdoor graph to path through the wall.
            if (home.footprint.Contains(exteriorStart))
            {
                bool validInteriorOrigin = false;
                string interiorOriginFailure = "The resident's current position is not inside an authorized walkable floor area.";
                foreach (BuildingEntrancePairAuthoring entranceCandidate in authority.EntrancePairs)
                {
                    if (entranceCandidate == null) continue;
                    if (!authority.TryGetEntrancePair(entranceCandidate.id,
                        out BuildingEntrancePairAuthoring entrance, out string entranceFailure))
                    {
                        if (!string.IsNullOrWhiteSpace(entranceFailure)) interiorOriginFailure = entranceFailure;
                        continue;
                    }
                    if (authority.TryEvaluateInteriorWorldPoint(managed.mover.transform.position,
                        entrance.floorIndex, entrance.functionalSpaceId, out string pointFailure))
                    {
                        validInteriorOrigin = true;
                        break;
                    }
                    if (!string.IsNullOrWhiteSpace(pointFailure)) interiorOriginFailure = pointFailure;
                }

                if (!validInteriorOrigin)
                {
                    reason = "The resident is within the home footprint, but cannot use the outdoor path graph from this position: " + interiorOriginFailure;
                    RecordMovementTrace(managed, "refused", "InvalidInteriorOrigin", home.id, reason, activityLocationId);
                    return DomesticActivityMovementResult.Unsupported;
                }

                BuildingAnchor porchTarget = new()
                {
                    type = AnchorType.FrontDoor,
                    coord = target,
                    hasAuthoredWorldPosition = true,
                    authoredWorldPosition = activity.WorldPosition,
                    source = activityLocationId
                };
                if (!managed.mover.BeginInteriorDoorToDoor(authority, porchTarget, out reason))
                {
                    reason = "The resident has a valid interior origin, but no authorized doorway route reaches the porch activity: " + reason;
                    RecordMovementTrace(managed, "refused", "PorchRouteFailed", home.id, reason, activityLocationId);
                    return DomesticActivityMovementResult.Unsupported;
                }
                managed.routeMode = AgentRouteMode.TaskDirected;
                managed.person.currentDestinationBuildingId = home.id;
                RecordMovementTrace(managed, "started", managed.mover.CurrentStateName, home.id, string.Empty, activityLocationId);
                return TrackDomesticActivityRoute(managed, flight, activityLocationId, routeClock);
            }
            if (!managed.mover.BeginGridRoute(exteriorStart, target, false))
            {
                reason = "No reachable outdoor path to the authored porch activity point.";
                RecordMovementTrace(managed, "refused", "PorchRouteFailed", home.id, reason, activityLocationId);
                return DomesticActivityMovementResult.Unsupported;
            }
            managed.person.currentDestinationBuildingId = home.id;
            managed.routeMode = AgentRouteMode.TaskDirected;
            RecordMovementTrace(managed, "started", managed.mover.CurrentStateName, home.id, string.Empty, activityLocationId);
            return TrackDomesticActivityRoute(managed, flight, activityLocationId, routeClock);
        }

        /// <summary>
        /// Porch activity points may sit along a porch rather than beside the
        /// doorway. Their authored geometry and a clear physical segment decide
        /// reachability; door width is not a maximum walking distance.
        /// </summary>
        public static bool CanApproachPorchActivityDoorway(float horizontalDistance, bool directApproachClear)
        {
            return directApproachClear
                && horizontalDistance >= 0f
                && !float.IsNaN(horizontalDistance)
                && !float.IsInfinity(horizontalDistance);
        }

        private DomesticActivityMovementResult TrackDomesticActivityRoute(
            ManagedPersonAgent managed, DomesticLaborDriver.InFlightDomesticTask flight, string activityLocationId,
            double currentGameSeconds)
        {
            bool continuation = ShouldPreserveDomesticActivityRouteDeadline(
                managed.domesticActivityTaskId, managed.domesticActivityLocationId,
                managed.domesticActivityRouteStartedGameSeconds, flight.TaskIdString, activityLocationId);
            managed.domesticActivityTaskId = flight.TaskIdString;
            managed.domesticActivityLocationId = activityLocationId;
            managed.domesticActivityRouteGeneration = managed.mover.RouteGeneration;
            managed.domesticActivityLastRemainingDistance = managed.mover.RemainingWorldRouteDistance;
            double now = currentGameSeconds;
            managed.domesticActivityLastProgressGameSeconds = now;
            float expected = managed.mover.EstimatedRemainingRouteSeconds ?? 0f;
            managed.domesticActivityNoProgressTimeoutGameSeconds = Mathf.Clamp(expected * 3f + 120f, 600f, 3600f);
            if (!continuation)
            {
                managed.domesticActivityRouteStartedGameSeconds = now;
                managed.domesticActivityRouteTimeoutGameSeconds = Mathf.Clamp(expected * 4f + 600f, 1800f, 21600f);
            }
            return DomesticActivityMovementResult.Traveling;
        }

        internal static bool ShouldPreserveDomesticActivityRouteDeadline(string previousTaskId,
            string previousActivityLocationId, double previousStartedGameSeconds,
            string requestedTaskId, string requestedActivityLocationId)
        {
            return !string.IsNullOrWhiteSpace(previousTaskId)
                && string.Equals(previousTaskId, requestedTaskId, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(previousActivityLocationId)
                && string.Equals(previousActivityLocationId, requestedActivityLocationId, StringComparison.Ordinal)
                && !double.IsNaN(previousStartedGameSeconds)
                && !double.IsInfinity(previousStartedGameSeconds);
        }

        private double CurrentSimulationGameSeconds()
        {
            TimeManager clock = timeManager;
            if (clock == null)
            {
                clock = TimeManager.Instance;
                if (clock == null) clock = FindAnyObjectByType<TimeManager>();
                if (clock != null) timeManager = clock;
            }
            return clock != null ? clock.PreciseAbsoluteGameSeconds : double.NaN;
        }

        internal static double ResolveDomesticActivityRouteClock(double suppliedGameSeconds,
            Func<double> fallbackGameSeconds)
        {
            if (!double.IsNaN(suppliedGameSeconds) && !double.IsInfinity(suppliedGameSeconds))
                return suppliedGameSeconds;
            return fallbackGameSeconds != null ? fallbackGameSeconds() : double.NaN;
        }

        internal static bool IsDomesticActivityRouteMakingProgress(float remainingDistance, float previousRemainingDistance)
        {
            return previousRemainingDistance - remainingDistance > 0.05f;
        }

        internal static bool ShouldAbortStalledDomesticActivityRoute(double nowGameSeconds,
            double routeStartedGameSeconds, double lastProgressGameSeconds, float noProgressTimeoutGameSeconds,
            float totalRouteTimeoutGameSeconds, float remainingDistance, float previousRemainingDistance)
        {
            if (double.IsNaN(nowGameSeconds) || double.IsInfinity(nowGameSeconds)
                || double.IsNaN(routeStartedGameSeconds) || double.IsInfinity(routeStartedGameSeconds)
                || double.IsNaN(lastProgressGameSeconds) || double.IsInfinity(lastProgressGameSeconds)
                || noProgressTimeoutGameSeconds <= 0f || totalRouteTimeoutGameSeconds <= 0f)
                return false;
            return nowGameSeconds - routeStartedGameSeconds >= totalRouteTimeoutGameSeconds
                || (!IsDomesticActivityRouteMakingProgress(remainingDistance, previousRemainingDistance)
                    && nowGameSeconds - lastProgressGameSeconds >= noProgressTimeoutGameSeconds);
        }

        internal static string FormatDomesticActivityRouteWatchdogDiagnostic(
            string taskId, string personId, string activityLocationId, int routeGeneration,
            bool routeActive, bool hidden, string position, float remainingDistance,
            float previousRemainingDistance, string measuredSpeed, double totalElapsedSeconds,
            double noProgressElapsedSeconds, float noProgressLimitSeconds, float totalLimitSeconds)
        {
            string elapsed = totalElapsedSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            string stalled = noProgressElapsedSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            string remaining = remainingDistance.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            string previous = previousRemainingDistance.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            string noProgressLimit = noProgressLimitSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            string totalLimit = totalLimitSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            return $"Domestic route watchdog released task '{taskId}' for retry: person={personId}, activity='{activityLocationId}', routeGeneration={routeGeneration}, routeActive={routeActive}, hidden={hidden}, position={position}, remainingDistance={remaining}m (previous={previous}m), measuredSpeed={measuredSpeed}m/s, elapsed={elapsed}/{totalLimit}s, noProgress={stalled}/{noProgressLimit}s.";
        }

        internal static string DomesticActivityPresentationFailure(
            bool isOffMap, bool hasManagedActor, bool hasMover, bool isRiding)
        {
            if (isOffMap) return "The resident is away from town on an off-map journey.";
            if (!hasManagedActor) return "The resident has no managed on-map physical actor.";
            if (!hasMover) return "The resident's physical actor has no movement component.";
            if (isRiding) return "The resident is aboard a wagon and cannot begin a household activity route.";
            return string.Empty;
        }

        private static string FormatRouteDiagnosticVector(Vector3 value) =>
            $"({value.x.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)},{value.y.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)},{value.z.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)})";

        private static void ClearDomesticActivityRoute(ManagedPersonAgent managed)
        {
            if (managed == null) return;
            managed.domesticActivityTaskId = null;
            managed.domesticActivityLocationId = null;
            managed.domesticActivityRouteGeneration = -1;
            managed.domesticActivityRouteStartedGameSeconds = double.NaN;
            managed.domesticActivityLastProgressGameSeconds = double.NaN;
            managed.domesticActivityRouteTimeoutGameSeconds = 0f;
            managed.domesticActivityNoProgressTimeoutGameSeconds = 0f;
            managed.domesticActivityLastRemainingDistance = 0f;
        }

        internal static bool ShouldContinueOwnedDomesticActivityRoute(
            string ownedTaskId, int ownedRouteGeneration, string ownedActivityLocationId,
            string requestedTaskId, string requestedActivityLocationId, int activeRouteGeneration)
        {
            return !string.IsNullOrWhiteSpace(ownedTaskId)
                && string.Equals(ownedTaskId, requestedTaskId, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(ownedActivityLocationId)
                && string.Equals(ownedActivityLocationId, requestedActivityLocationId, StringComparison.Ordinal)
                && ownedRouteGeneration == activeRouteGeneration;
        }

        /// <summary>
        /// Slice F5 (2026-11-06, Objective 25): hide a person via the
        /// mover's hide path (AgentMover.HideImmediately — the F5 coder
        /// verification point confirmed the exact call; the root
        /// GameObject is never deactivated). Same lookup as
        /// TryStartExplicitRoute; false (no-op, no log spam) when the person
        /// has no managed agent or no mover.
        /// </summary>
        public bool TryHidePerson(int personId)
        {
            if (personId < 0) return false;
            for (int i = 0; i < agents.Count; i++)
            {
                ManagedPersonAgent managed = agents[i];
                if (managed?.person == null || managed.person.id != personId) continue;
                if (managed.mover == null) return false;
                managed.mover.HideImmediately();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Slice F5: show a person again at their current location
        /// (AgentMover.ShowAtCurrentLocation — the matching unhide call).
        /// Same lookup and contract as TryHidePerson.
        /// </summary>
        public bool TryShowPersonAtCurrentLocation(int personId)
        {
            if (personId < 0) return false;
            for (int i = 0; i < agents.Count; i++)
            {
                ManagedPersonAgent managed = agents[i];
                if (managed?.person == null || managed.person.id != personId) continue;
                if (managed.mover == null) return false;
                managed.mover.ShowAtCurrentLocation();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Slice R1 (2026-11-06, Objective 22): the seated hip-height
        /// offset applied at the board site. Default 0 — the agent root
        /// (his feet) lands exactly on the seat anchor. VP-R1 (coder,
        /// in-Editor; amended by the coordinator: no Unity editor or
        /// scene access is available to this coder, so the measurement
        /// is Codex's): if the PlayMode fit floats or sinks the rider,
        /// the measured seated hip height from the X Bot@Driving clip
        /// is applied here as a single named constant (negative when
        /// the clip's hips sit above the root) — derived from the
        /// asset, never invented, never a prefab move.
        /// </summary>
        private const float RiderSeatVerticalOffsetMetres = 0f;

        /// <summary>
        /// Slice R1 (2026-11-06, Objective 22): board a person onto
        /// the wagon's DriverSeat. Parents the existing managed agent
        /// to the seat (worldPositionStays false), so the rider
        /// inherits exactly the wagon's position and rotation around
        /// corners — parenting to the transport group root would follow
        /// the horse's path point instead. Idempotent: when already
        /// parented to the seat (the return leg, where he was never
        /// unparented at the exit) the re-parent is skipped and the
        /// sitting state is re-applied. Sets the riding flag, which
        /// gates all three production route entry points until
        /// TryDismountRider clears it.
        ///
        /// ORDERING (Note-R2): ShowAtCurrentLocation reverts the
        /// Animator to Locomotion, so boarding must always run AFTER
        /// any show — the return leg shows him, then boards him.
        /// Returns false (no-op, no log spam beyond the refusal line)
        /// when the person has no managed agent or no mover, the seat
        /// is null, or the board gate refuses; the caller degrades to
        /// a riderless leg.
        /// </summary>
        public bool TryBoardRider(int personId, Transform seat, string legLabel)
        {
            if (personId < 0 || seat == null) return false;
            for (int i = 0; i < agents.Count; i++)
            {
                ManagedPersonAgent managed = agents[i];
                if (managed?.person == null || managed.person.id != personId) continue;
                if (managed.mover == null) return false;
                if (!WagonRiderPlacement.TryAllowBoarding(managed.mover.IsRouteActive, managed.mover.IsHidden,
                        seat.position, managed.mover.transform.position,
                        WagonRiderPlacement.BoardToleranceMetres, out string refusalReason))
                {
                    Debug.LogWarning($"[CoupledTransport] rider boarding refused for {legLabel}: {refusalReason}.", this);
                    return false;
                }
                if (managed.mover.transform.parent != seat)
                {
                    managed.mover.transform.SetParent(seat, true);
                    managed.mover.transform.localPosition = new Vector3(0f, RiderSeatVerticalOffsetMetres, 0f);
                    // the agent's forward is his root +Z: a +90 deg yaw
                    // maps local +Z onto local +X, so he faces along
                    // the heading while seated.
                    managed.mover.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                }
                managed.isRiding = true;
                managed.transportStagingCoord = null;
                managed.transportStagingSeat = null;
                // Sitting application, step 5 (R2): cross-fade to the
                // DrivingWagon state; a false return means the degrade
                // path ran (no Animator / no state) and the rider rides
                // in his current pose — boarding still succeeds.
                bool poseApplied = TrySetPersonSitting(personId, seated: true);
                Debug.Log($"[CoupledTransport] rider boarded: P{personId} seated at DriverSeat for {legLabel} (pose={(poseApplied ? "sitting" : "degraded (no DrivingWagon state)")}).", this);
                return true;
            }
            return false;
        }

        /// <summary>Starts a real pedestrian route to the wagon staging cell when the driver is outside boarding range.</summary>
        public bool TryApproachTransportSeat(int personId, Transform seat, float boardingToleranceMetres, out bool readyToBoard)
            => TryApproachTransportSeat(personId, seat, boardingToleranceMetres, out readyToBoard, out _);

        public bool TryApproachTransportSeat(int personId, Transform seat, float boardingToleranceMetres,
            out bool readyToBoard, out string reason)
        {
            readyToBoard = false;
            reason = string.Empty;
            if (personId < 0 || seat == null || townWorld?.Grid == null || pathingManager == null)
            {
                reason = "person, seat, town grid, or pathing authority is unresolved";
                return false;
            }
            for (int i = 0; i < agents.Count; i++)
            {
                ManagedPersonAgent managed = agents[i];
                if (managed?.person == null || managed.person.id != personId || managed.mover == null) continue;
                if (managed.isRiding)
                {
                    readyToBoard = managed.mover.transform.parent == seat;
                    if (!readyToBoard) reason = "person is riding a different seat";
                    return readyToBoard;
                }
                Vector3 actorPosition = managed.mover.transform.position;
                Vector2 actor = new Vector2(actorPosition.x, actorPosition.z);
                Vector2 target = new Vector2(seat.position.x, seat.position.z);
                GridCoord targetCoord = townWorld.Grid.WorldToCoord(seat.position);
                if (!managed.mover.IsRouteActive && !managed.mover.IsHidden
                    && Vector2.Distance(actor, target) <= Mathf.Max(0.1f, boardingToleranceMetres))
                {
                    managed.transportStagingCoord = townWorld.Grid.WorldToCoord(actorPosition);
                    managed.transportStagingSeat = seat;
                    managed.transportStagingSeatPosition = seat.position;
                    readyToBoard = true;
                    return true;
                }

                if (managed.transportStagingSeat == seat
                    && Vector3.SqrMagnitude(managed.transportStagingSeatPosition - seat.position) <= 0.01f
                    && WagonRiderPlacement.IsActiveApproachRoute(true, managed.mover.IsRouteActive,
                        managed.transportStagingCoord, managed.mover.CurrentPath))
                    return !managed.mover.IsHidden;
                if (managed.mover.IsRouteActive)
                {
                    reason = "operator is completing another physical route before approaching the assigned wagon";
                    return false;
                }
                managed.transportStagingCoord = null;
                managed.transportStagingSeat = null;
                if (managed.mover.IsHidden)
                {
                    reason = "operator is hidden";
                    return false;
                }

                GridCoord startCoord = townWorld.Grid.WorldToCoord(actorPosition);
                int searchRadius = Mathf.CeilToInt(Mathf.Max(0.1f, boardingToleranceMetres)
                    / townWorld.Grid.CellSizeMeters) + 1;
                var candidates = new List<WagonRiderPlacement.ApproachCandidate>();
                for (int dz = -searchRadius; dz <= searchRadius; dz++)
                for (int dx = -searchRadius; dx <= searchRadius; dx++)
                {
                    GridCoord candidateCoord = new(targetCoord.x + dx, targetCoord.z + dz);
                    if (!townWorld.Grid.IsInBounds(candidateCoord)
                        || townWorld.Grid.GetCell(candidateCoord).HasBuilding
                        || !pathingManager.TryGetMovementCost(candidateCoord, out _)) continue;
                    Vector3 candidatePosition = pathingManager.CoordToPathWorld(candidateCoord);
                    if (Vector2.Distance(new Vector2(candidatePosition.x, candidatePosition.z), target)
                        > Mathf.Max(0.1f, boardingToleranceMetres)) continue;
                    PathingRequest probe = PathingRequest.CreateProbe(startCoord, candidateCoord,
                        this, $"P{personId} wagon boarding approach");
                    if (!pathingManager.TryFindPath(probe, out PathingResult path) || path == null || !path.Success) continue;
                    candidates.Add(new WagonRiderPlacement.ApproachCandidate(candidateCoord, candidatePosition, path.TotalCost));
                }

                if (!WagonRiderPlacement.TrySelectApproachCandidate(candidates, seat.position,
                        boardingToleranceMetres, out WagonRiderPlacement.ApproachCandidate approach))
                {
                    reason = $"no walkable, reachable approach cell is within {Mathf.Max(0.1f, boardingToleranceMetres):F1} m of the DriverSeat (seat cell {targetCoord}, operator cell {startCoord})";
                    return false;
                }

                if (!managed.mover.BeginGridRouteWithFinalWaypoints(startCoord, approach.Coord,
                        new[] { approach.WorldPosition }))
                {
                    reason = $"the custom mover refused the route from {startCoord} to boarding cell {approach.Coord}";
                    return false;
                }
                managed.transportStagingCoord = approach.Coord;
                managed.transportStagingSeat = seat;
                managed.transportStagingSeatPosition = seat.position;
                managed.routeMode = AgentRouteMode.TaskDirected;
                managed.ambientOriginBuildingId = -1;
                managed.ambientTargetBuildingId = -1;
                return true;
            }
            reason = $"operator P{personId} has no managed visible movement agent";
            return false;
        }

        public bool IsPersonApproachingTransportSeat(int personId, Transform seat)
        {
            if (personId < 0 || seat == null || townWorld?.Grid == null) return false;
            foreach (ManagedPersonAgent managed in agents)
                if (managed?.person != null && managed.person.id == personId && managed.mover != null
                    && managed.transportStagingSeat == seat && managed.transportStagingCoord.HasValue
                    && !managed.mover.IsHidden
                    && (WagonRiderPlacement.IsActiveApproachRoute(true, managed.mover.IsRouteActive,
                            managed.transportStagingCoord, managed.mover.CurrentPath)
                        || Vector2.Distance(
                        new Vector2(managed.mover.transform.position.x, managed.mover.transform.position.z),
                        new Vector2(seat.position.x, seat.position.z)) <= WagonRiderPlacement.BoardToleranceMetres))
                    return true;
            return false;
        }

        /// <summary>Releases a freight seat approach after its owning operation terminates.</summary>
        public bool ReleaseTransportSeatApproach(int personId, Transform seat)
        {
            if (personId < 0 || seat == null) return false;
            foreach (ManagedPersonAgent managed in agents)
            {
                if (managed?.person == null || managed.person.id != personId || managed.mover == null
                    || managed.transportStagingSeat != seat) continue;
                if (WagonRiderPlacement.IsActiveApproachRoute(true, managed.mover.IsRouteActive,
                        managed.transportStagingCoord, managed.mover.CurrentPath))
                    managed.mover.CancelActiveRoute();
                managed.transportStagingCoord = null;
                managed.transportStagingSeat = null;
                managed.routeMode = AgentRouteMode.None;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Slice R2 (2026-11-06, Objective 22): apply the rider's
        /// sitting pose — cross-fade Ethan's Animator to the
        /// DrivingWagon state, the same HasState-guarded CrossFade
        /// TransportAnimatorState.SetState uses for the horse/wagon
        /// views. Ethan's agent carries no TransportAnimatorState (that
        /// component lives on the horse/wagon view roots), so this
        /// drives his own Animator directly. Degrades, never crashes:
        /// a missing Animator (generated-agent fallback) or an absent
        /// state (a different controller assigned in the scene — VP-R2)
        /// logs the missing piece and returns false, and boarding still
        /// succeeds with the rider in whatever pose his Animator holds.
        /// Only `seated: true` does anything: there is no separate
        /// clear call — dismount clears the pose via
        /// ShowAtCurrentLocation (Note-R2), which restores Locomotion.
        /// Returns false (no-op, no log spam) when the person has no
        /// managed agent or no mover.
        /// </summary>
        public bool TrySetPersonSitting(int personId, bool seated)
        {
            if (!seated) return false;
            if (personId < 0) return false;
            for (int i = 0; i < agents.Count; i++)
            {
                ManagedPersonAgent managed = agents[i];
                if (managed?.person == null || managed.person.id != personId) continue;
                if (managed.mover == null) return false;
                Animator animator = managed.mover.GetComponentInChildren<Animator>(includeInactive: true);
                if (animator == null)
                {
                    Debug.LogWarning($"[CoupledTransport] rider P{personId} has no Animator; riding without the sitting pose.", this);
                    return false;
                }
                int hash = Animator.StringToHash("DrivingWagon");
                if (!animator.HasState(0, hash))
                {
                    string controllerName = animator.runtimeAnimatorController != null
                        ? animator.runtimeAnimatorController.name
                        : "<none>";
                    Debug.LogWarning($"[CoupledTransport] rider P{personId} Animator '{controllerName}' has no DrivingWagon state; riding without the sitting pose.", this);
                    return false;
                }
                if (!managed.mover.TrySetPresentationAnimation("DrivingWagon", managed.mover.transform.parent)) return false;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Slice R3(2026-11-06, Objective 22): dismount a rider at
        /// the return leg's arrival. Idempotent no-op (true) when the
        /// riding flag is not set, so a double call can never unparent
        /// him twice or strand him. Re-parents to the agent root with
        /// world-position-stays, then sets the world position to the
        /// arrival cell centre and the rotation to the leg's last
        /// heading, clears the flag, and calls ShowAtCurrentLocation —
        /// which restores mover state ArrivedVisible, re-enables
        /// renderers, and returns the Animator to the Locomotion state
        /// at Blend 0 (standing idle). The dismount itself clears the
        /// sitting pose; there is no separate "clear sitting" call, and
        /// no code path can leave him parented-but-standing or
        /// unparented-but-sitting.
        ///
        /// ORDERING (Note-R2): dismount runs BEFORE its show — the
        /// show here is the last step, inside this method. Boarding
        /// must always run AFTER any show (see TryBoardRider).
        /// Returns false (no-op, no log spam) when the person has no
        /// managed agent or no mover. Touches no schedule state, task
        /// state, reservation, or shipment.
        /// </summary>
        public bool TryDismountRider(int personId, Vector3 groundPosition, Quaternion facing)
        {
            if (personId < 0) return false;
            for (int i = 0; i < agents.Count; i++)
            {
                ManagedPersonAgent managed = agents[i];
                if (managed?.person == null || managed.person.id != personId) continue;
                if (managed.mover == null) return false;
                if (!managed.isRiding) return true;
                managed.mover.transform.SetParent(EnsureAgentRoot(), true);
                managed.mover.transform.position = groundPosition;
                managed.mover.transform.rotation = facing;
                managed.isRiding = false;
                managed.mover.ClearPresentationAnimation();
                managed.mover.ShowAtCurrentLocation();
                Debug.Log($"[CoupledTransport] rider dismounted: P{personId} at staging.", this);
                return true;
            }
            return false;
        }

        public bool IsAtExplicitBuilding(int personId, int targetBuildingId)
        {
            return TryGetExplicitArrival(personId, targetBuildingId, out _, out _, out _);
        }

        public bool TryGetExplicitArrival(int personId, int targetBuildingId,
            out Vector3 actorPosition, out float distanceToDestination, out bool arrived)
        {
            actorPosition = Vector3.zero;
            distanceToDestination = float.PositiveInfinity;
            arrived = false;
            if (personId < 0 || targetBuildingId < 0 || pathingManager == null) return false;
            for (int i = 0; i < agents.Count; i++)
            {
                ManagedPersonAgent managed = agents[i];
                if (managed?.person == null || managed.person.id != personId) continue;
                if (managed.mover == null || !TryGetDoorAnchor(targetBuildingId, out BuildingAnchor targetAnchor)) return false;
                actorPosition = managed.mover.transform.position;
                Vector3 destination = pathingManager.CoordToPathWorld(targetAnchor.coord);
                Vector2 actorXZ = new Vector2(actorPosition.x, actorPosition.z);
                Vector2 destinationXZ = new Vector2(destination.x, destination.z);
                distanceToDestination = Vector2.Distance(actorXZ, destinationXZ);
                arrived = managed.person.currentDestinationBuildingId == targetBuildingId
                    && managed.mover.IsArrivedVisible && !managed.mover.IsRouteActive
                    && managed.mover.RemainingPathWaypointCount == 0
                    && distanceToDestination <= Mathf.Max(0.1f, explicitTravelArrivalRadiusMeters);
                return true;
            }
            // Campaign C (DEF-021): a cut resident has no agent, so every
            // arrival-gated task for them fails this check silently. Count it
            // and warn once per person; the populate warning names the cut.
            if (cutResidentIds.Contains(personId))
            {
                explicitArrivalChecksForCutResidents++;
                if (warnedCutArrivalPersonIds.Add(personId))
                {
                    Debug.LogWarning(
                        $"PopulationPathingDirector arrival check for person #{personId} found no visual agent: " +
                        $"the resident was cut by the agent cap (cap={lastAgentCapApplied}, cut={residentsCutByAgentCap}). " +
                        "Arrival-gated tasks for this resident cannot complete.",
                        this);
                }
            }
            return false;
        }

        private bool TrySendAgent(ManagedPersonAgent managed, int targetBuildingId, PopulationScheduleState nextState, out ScheduledRouteFailureKind failureKind, string attribution = null)
        {
            return TrySendAgent(managed, targetBuildingId, nextState, out failureKind, out _, attribution);
        }

        private bool TrySendAgent(ManagedPersonAgent managed, int targetBuildingId,
            PopulationScheduleState nextState, out ScheduledRouteFailureKind failureKind,
            out string refusal, string attribution = null)
        {
            failureKind = ScheduledRouteFailureKind.CandidateNotReady;
            refusal = string.Empty;
            if (managed == null || managed.person == null || managed.mover == null)
            {
                refusal = "Managed resident or AgentMover is unavailable.";
                return false;
            }

            if (ShouldDeferRouteForFreightMovement(managed.transportStagingSeat != null,
                    managed.freightReceivingTargetBuildingId, targetBuildingId))
            {
                refusal = managed.transportStagingSeat != null
                    ? "Resident is approaching an assigned freight wagon; the boarding route must arrive or be released before another task can claim movement."
                    : $"Resident is carrying freight to receiving building {managed.freightReceivingTargetBuildingId}; the receiving route must arrive or be released before another destination can claim movement.";
                RecordMovementTrace(managed, "refused", "RouteNotStarted", targetBuildingId, refusal);
                return false;
            }

            if (!TryGetDoorAnchor(managed.person.currentDestinationBuildingId, out BuildingAnchor startAnchor, out PlacedBuilding startBuilding)
                && !TryGetDoorAnchor(managed.person.homeBuildingId, out startAnchor, out startBuilding))
            {
                failureKind = ScheduledRouteFailureKind.MissingStartAnchor;
                refusal = $"No valid exterior start anchor for current destination {managed.person.currentDestinationBuildingId} or home {managed.person.homeBuildingId}.";
                RecordMovementTrace(managed, "refused", "MissingStartAnchor", targetBuildingId, refusal);
                return false;
            }

            if (!TryGetDoorAnchor(targetBuildingId, out BuildingAnchor targetAnchor, out PlacedBuilding targetBuilding))
            {
                failureKind = ScheduledRouteFailureKind.MissingTargetAnchor;
                refusal = $"Building {targetBuildingId} has no valid destination door anchor.";
                RecordMovementTrace(managed, "refused", "MissingTargetAnchor", targetBuildingId, refusal);
                return false;
            }

            // Keep the same actor visible when it reaches home or work. The
            // authoritative Person location changes on arrival; until the world
            // has authored interiors/occupancy rendering, hiding every arrived
            // actor makes residents vanish from the simulation's visible town.
            bool started;
            TownGrid grid = pathingManager != null ? pathingManager.Grid : null;
            GridCoord actorCell = grid != null ? grid.WorldToCoord(managed.mover.transform.position) : default;
            bool startsInsideOrigin = grid != null && grid.IsInBounds(actorCell)
                && grid.GetCell(actorCell).HasBuilding
                && grid.GetCell(actorCell).buildingId == startBuilding?.id
                && startBuilding.runtimeFootprintAuthority != null
                && TryFindReachableInteriorEntrance(startBuilding.runtimeFootprintAuthority,
                    managed.mover.transform.position, out _);
            if (startsInsideOrigin)
            {
                started = managed.mover.BeginInteriorDoorToDoor(
                    startBuilding.runtimeFootprintAuthority, targetAnchor, out refusal);
            }
            else
            {
                // Lane 9c (M-R9): the caller-held attribution (task
                // family / destination) rides the request so a failed
                // exterior route names what it was for.
                started = managed.mover.BeginDoorToDoor(startAnchor, targetAnchor, false,
                    startBuilding?.doorAnimator, targetBuilding?.doorAnimator, attribution);
                if (!started)
                {
                    GridCoord actual = grid != null ? grid.WorldToCoord(managed.mover.transform.position) : default;
                    refusal = $"Exterior path from actor cell {actual} to building {targetBuildingId} door cell {targetAnchor.coord} was rejected.";
                }
            }

            if (!started)
            {
                failureKind = ScheduledRouteFailureKind.PathFailed;
                if (string.IsNullOrWhiteSpace(refusal)) refusal = "Interior-to-exterior route could not be constructed.";
                RecordMovementTrace(managed, "refused", "PathFailed", targetBuildingId, refusal);
                return false;
            }

            managed.routeMode = AgentRouteMode.Scheduled;
            managed.ambientOriginBuildingId = -1;
            managed.ambientTargetBuildingId = -1;
            managed.person.scheduleState = nextState;
            managed.person.currentDestinationBuildingId = targetBuildingId;
            failureKind = ScheduledRouteFailureKind.None;
            RecordMovementTrace(managed, "started", managed.mover.CurrentStateName, targetBuildingId, string.Empty);
            return true;
        }

        /// <summary>
        /// A building grid cell is only a coarse raster. Confirm an actual
        /// walkable interior route to an authored entrance before treating the
        /// resident as indoors for door-to-door movement.
        /// </summary>
        internal static bool TryFindReachableInteriorEntrance(
            BuildingFootprintAuthority authority, Vector3 actorWorldPosition,
            out BuildingEntrancePairAuthoring reachableEntrance)
        {
            reachableEntrance = null;
            if (authority == null || authority.EntrancePairs == null) return false;
            foreach (BuildingEntrancePairAuthoring candidate in authority.EntrancePairs)
            {
                if (candidate == null
                    || !authority.TryGetEntrancePair(candidate.id,
                        out BuildingEntrancePairAuthoring entrance, out _)) continue;
                Vector3 indoor = authority.transform.TransformPoint(entrance.indoorPositionLocal);
                if (!authority.TryBuildInteriorPath(actorWorldPosition, indoor,
                        entrance.floorIndex, entrance.functionalSpaceId, out _, out _)) continue;
                reachableEntrance = entrance;
                return true;
            }
            return false;
        }

        private void RecordMovementTrace(ManagedPersonAgent managed, string transition, string status,
            int destinationBuildingId, string reason, string activityLocationId = null)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (managed?.person == null) return;
            systemsHub ??= FindAnyObjectByType<SimulationSystemsHub>();
            if (systemsHub == null) return;
            NpcActivityTraceRecorder trace = systemsHub.ActivityTrace;
            if (trace == null || !trace.Monitoring || trace.Level < NpcTraceLevel.Detailed) return;

            AgentMover mover = managed.mover;
            int personId = managed.person.id;
            int day = timeManager != null ? timeManager.CurrentAbsoluteDayIndex : lastDayIndex;
            double seconds = timeManager != null ? timeManager.AbsoluteGameSeconds : -1d;
            var movementTask = managed.routeMode == AgentRouteMode.TaskDirected
                ? systemsHub.Tasks.FindBlockingTaskForAssignee(
                    LandLedgers.Primitives.EntityId.For(LandLedgers.Primitives.EntityKind.Person, personId))
                : null;
            string shoppingTripId = ShoppingTripIdentityForPerson?.Invoke(personId);
            float remainingMeters = mover != null && mover.IsRouteActive ? mover.RemainingWorldRouteDistance : 0f;
            bool activeRequestRoute = !string.Equals(transition, "refused", StringComparison.Ordinal);
            string routeId = activeRequestRoute && mover != null && mover.RouteGeneration >= 0
                ? $"P{personId}:route:{mover.RouteGeneration}"
                : string.Empty;
            string destination = destinationBuildingId >= 0 ? $"building:{destinationBuildingId}" : "unknown-destination";
            trace.RecordMovement(personId, day, seconds, transition, status, destination,
                remainingMeters, routeId, reason, destinationBuildingId,
                activeRequestRoute && mover != null ? mover.RouteGeneration : -1,
                managed.routeMode.ToString(), managed.person.scheduleState.ToString(),
                mover != null ? mover.CurrentStateName : string.Empty,
                activeRequestRoute && mover != null && mover.IsRouteActive ? mover.RouteDestinationWorld : (Vector3?)null,
                activityLocationId,
                mover != null ? mover.transform.position : (Vector3?)null,
                mover != null && !mover.IsHidden && mover.gameObject.activeInHierarchy,
                mover != null && mover.IsRouteActive,
                movementTask,
                shoppingTripId);
#endif
        }

        private void EnsureAmbientStreetLife()
        {
            if (agents.Count == 0)
            {
                visibleStreetAgents = 0;
                return;
            }

            int targetVisibleAgents = Mathf.Min(agents.Count, maxActiveAgents, Mathf.Max(0, minimumVisibleStreetAgents));
            visibleStreetAgents = CountVisibleAgents();
            if (targetVisibleAgents <= 0 || visibleStreetAgents >= targetVisibleAgents)
            {
                return;
            }

            int needed = targetVisibleAgents - visibleStreetAgents;
            int started = 0;
            int attempts = Mathf.Min(agents.Count, Mathf.Max(1, ambientRouteAttemptBudget));
            int startIndex = PositiveModulo(BuildAmbientStartSalt(), agents.Count);
            for (int attempt = 0; attempt < attempts && started < needed; attempt++)
            {
                ManagedPersonAgent managed = agents[(startIndex + attempt) % agents.Count];
                if (TrySendAmbientRoute(managed, attempt, out AmbientRouteFailureKind failureKind))
                {
                    started++;
                    continue;
                }

                RecordAmbientRouteFailure(failureKind);
            }

            visibleStreetAgents = CountVisibleAgents();
            if (started > 0)
            {
                RefreshMovementStatus($"Started {started} ambient street-life route(s).");
                return;
            }

            if (visibleStreetAgents < targetVisibleAgents && !loggedAmbientRouteFailure)
            {
                loggedAmbientRouteFailure = true;
                Debug.LogWarning(
                    $"PopulationPathingDirector could not start ambient street-life routes. Managed={managedPeople}, Visible={visibleStreetAgents}, AnchorFailures={ambientAnchorFailuresToday}, PathFailures={ambientPathFailuresToday}.",
                    this);
            }
        }

        private bool TrySendAmbientRoute(ManagedPersonAgent managed, int attemptSalt, out AmbientRouteFailureKind failureKind)
        {
            failureKind = AmbientRouteFailureKind.CandidateNotReady;
            // Slice R1 (Objective 22): never route a rider aboard the
            // wagon — BeginDoorToDoor would snap the parented agent off
            // the seat.
            if (managed != null && managed.isRiding)
            {
                return false;
            }
            if (!CanUseAgentForAmbientRoute(managed))
            {
                return false;
            }

            int originBuildingId = GetAmbientOriginBuildingId(managed.person);
            if (originBuildingId < 0
                || !TryGetDoorAnchor(originBuildingId, out BuildingAnchor startAnchor, out PlacedBuilding startBuilding)
                || !TryPickAmbientTarget(managed, originBuildingId, attemptSalt, out int targetBuildingId, out BuildingAnchor targetAnchor, out PlacedBuilding targetBuilding))
            {
                failureKind = AmbientRouteFailureKind.MissingAnchor;
                return false;
            }

            if (!managed.mover.BeginDoorToDoor(startAnchor, targetAnchor, false, startBuilding?.doorAnimator, targetBuilding?.doorAnimator))
            {
                failureKind = AmbientRouteFailureKind.PathFailed;
                return false;
            }

            managed.routeMode = AgentRouteMode.AmbientOutbound;
            managed.ambientOriginBuildingId = originBuildingId;
            managed.ambientTargetBuildingId = targetBuildingId;
            ambientRoutesStartedToday++;
            failureKind = AmbientRouteFailureKind.None;
            return true;
        }

        private bool TrySendAmbientReturnRoute(ManagedPersonAgent managed)
        {
            int originBuildingId = managed.ambientOriginBuildingId;
            int targetBuildingId = managed.ambientTargetBuildingId;
            if (originBuildingId < 0
                || targetBuildingId < 0
                || !TryGetDoorAnchor(targetBuildingId, out BuildingAnchor startAnchor, out PlacedBuilding startBuilding)
                || !TryGetDoorAnchor(originBuildingId, out BuildingAnchor targetAnchor, out PlacedBuilding targetBuilding))
            {
                ambientAnchorFailuresToday++;
                ClearAmbientRoute(managed);
                return false;
            }

            if (!managed.mover.BeginDoorToDoor(startAnchor, targetAnchor, false, startBuilding?.doorAnimator, targetBuilding?.doorAnimator))
            {
                ambientPathFailuresToday++;
                ClearAmbientRoute(managed);
                return false;
            }

            managed.routeMode = AgentRouteMode.AmbientReturning;
            return true;
        }

        private bool TryPickAmbientTarget(
            ManagedPersonAgent managed,
            int originBuildingId,
            int attemptSalt,
            out int targetBuildingId,
            out BuildingAnchor targetAnchor,
            out PlacedBuilding targetBuilding)
        {
            targetBuildingId = -1;
            targetAnchor = default;
            targetBuilding = null;
            IReadOnlyList<PlacedBuilding> buildings = townWorld != null ? townWorld.Buildings : null;
            if (buildings == null || buildings.Count <= 1)
            {
                return false;
            }

            int startIndex = PositiveModulo(BuildAmbientTargetSalt(managed, attemptSalt), buildings.Count);
            for (int i = 0; i < buildings.Count; i++)
            {
                PlacedBuilding candidate = buildings[(startIndex + i) % buildings.Count];
                if (candidate == null || candidate.id == originBuildingId)
                {
                    continue;
                }

                if (TryGetDoorAnchor(candidate.id, out targetAnchor, out targetBuilding))
                {
                    targetBuildingId = candidate.id;
                    return true;
                }
            }

            return false;
        }

        private static bool CanUseAgentForAmbientRoute(ManagedPersonAgent managed)
        {
            return managed != null
                && managed.person != null
                && managed.mover != null
                && managed.mover.IsHidden
                && managed.routeMode == AgentRouteMode.None
                && IsStableAmbientScheduleState(managed.person.scheduleState);
        }

        private static bool IsStableAmbientScheduleState(PopulationScheduleState scheduleState)
        {
            return scheduleState == PopulationScheduleState.AtHome
                || scheduleState == PopulationScheduleState.AtWork
                || scheduleState == PopulationScheduleState.AtStore;
        }

        private static int GetAmbientOriginBuildingId(PersonState person)
        {
            if (person == null)
            {
                return -1;
            }

            return person.scheduleState switch
            {
                PopulationScheduleState.AtHome => person.homeBuildingId,
                PopulationScheduleState.AtWork => person.workplaceBuildingId >= 0 ? person.workplaceBuildingId : person.currentDestinationBuildingId,
                PopulationScheduleState.AtStore => person.currentDestinationBuildingId,
                _ => -1
            };
        }

        private void RecordAmbientRouteFailure(AmbientRouteFailureKind failureKind)
        {
            if (failureKind == AmbientRouteFailureKind.MissingAnchor)
            {
                ambientAnchorFailuresToday++;
            }
            else if (failureKind == AmbientRouteFailureKind.PathFailed)
            {
                ambientPathFailuresToday++;
            }
        }

        private void RecordScheduledRouteFailure(ScheduledRouteFailureKind failureKind, PersonState person, int targetBuildingId, PopulationScheduleState nextState)
        {
            if (failureKind == ScheduledRouteFailureKind.None || failureKind == ScheduledRouteFailureKind.CandidateNotReady)
            {
                return;
            }

            scheduledRoutesBlockedToday++;
            if (failureKind == ScheduledRouteFailureKind.MissingStartAnchor)
            {
                scheduledMissingStartFailuresToday++;
            }
            else if (failureKind == ScheduledRouteFailureKind.MissingTargetAnchor)
            {
                scheduledMissingTargetFailuresToday++;
            }
            else if (failureKind == ScheduledRouteFailureKind.PathFailed)
            {
                scheduledPathFailuresToday++;
            }

            string personLabel = person != null ? person.DisplayName : "unknown person";
            int startBuildingId = person != null
                ? person.currentDestinationBuildingId >= 0 ? person.currentDestinationBuildingId : person.homeBuildingId
                : -1;
            lastRouteFailure = $"{failureKind} while sending {personLabel} toward {nextState}: start {startBuildingId}, target {targetBuildingId}.";

            // Scheduled failures usually point at generated-door or path-link issues, so log once per day and keep the rest in diagnostics.
            if (!loggedScheduledRouteFailure)
            {
                loggedScheduledRouteFailure = true;
                Debug.LogWarning($"PopulationPathingDirector blocked a scheduled population route. {lastRouteFailure}", this);
            }
        }

        private int CountVisibleAgents()
        {
            int count = 0;
            for (int i = 0; i < agents.Count; i++)
            {
                AgentMover mover = agents[i]?.mover;
                if (mover != null && !mover.IsHidden)
                {
                    count++;
                }
            }

            return count;
        }

        private void RefreshMovementStatus(string status)
        {
            managedPeople = agents.Count;
            visibleStreetAgents = CountVisibleAgents();
            string scheduledFailures = scheduledRoutesBlockedToday > 0
                ? $", ScheduledBlocked={scheduledRoutesBlockedToday}, ScheduledFailures(start/target/path)={scheduledMissingStartFailuresToday}/{scheduledMissingTargetFailuresToday}/{scheduledPathFailuresToday}"
                : string.Empty;
            lastMovementStatus = $"{status} Managed={managedPeople}, Visible={visibleStreetAgents}, AmbientStarted={ambientRoutesStartedToday}, AmbientFailures(anchor/path)={ambientAnchorFailuresToday}/{ambientPathFailuresToday}{scheduledFailures}.";
        }

        private int BuildAmbientStartSalt()
        {
            unchecked
            {
                int salt = 17;
                salt = salt * 31 + routesStartedToday;
                salt = salt * 31 + ambientRoutesStartedToday;
                salt = salt * 31 + (timeManager != null ? timeManager.ShortTickIndex : 0);
                salt = salt * 31 + (timeManager != null ? timeManager.CurrentAbsoluteDayIndex : 0);
                return salt;
            }
        }

        private int BuildAmbientTargetSalt(ManagedPersonAgent managed, int attemptSalt)
        {
            unchecked
            {
                int salt = 23;
                salt = salt * 31 + (managed?.person != null ? managed.person.id : 0);
                salt = salt * 31 + attemptSalt;
                salt = salt * 31 + routesStartedToday;
                salt = salt * 31 + ambientRoutesStartedToday;
                salt = salt * 31 + (timeManager != null ? timeManager.ShortTickIndex : 0);
                return salt;
            }
        }

        private static int PositiveModulo(int value, int divisor)
        {
            if (divisor <= 0)
            {
                return 0;
            }

            int result = value % divisor;
            return result < 0 ? result + divisor : result;
        }

        private bool TryGetDoorAnchor(int buildingId, out BuildingAnchor anchor)
        {
            return TryGetDoorAnchor(buildingId, out anchor, out _);
        }

        private bool TryGetDoorAnchor(int buildingId, out BuildingAnchor anchor, out PlacedBuilding building)
        {
            anchor = default;
            building = null;
            AutoWire();
            if (pathingManager == null)
            {
                return false;
            }

            // Resolve the authored building/door first, even if its coarse
            // marker cell is not traversable. The footprint authority below
            // can then project a valid entrance pair to a real exterior cell.
            // Without an authored entrance authority, retain the strict
            // traversability requirement for the original endpoint.
            if (!pathingManager.TryFindAuthoredAnchorByBuildingId(
                    buildingId, AnchorType.FrontDoor, out building, out anchor))
                return false;

            BuildingFootprintAuthority authority = building != null ? building.runtimeFootprintAuthority : null;
            if (authority == null)
                return pathingManager.TryGetMovementCost(anchor.coord, out _);

            if (authority.EntrancePairs == null || authority.EntrancePairs.Count == 0)
            {
                Vector3 door = authority.transform.TransformPoint(authority.FrontDoorLocalPoint);
                Vector3 outward = authority.transform.TransformDirection(authority.FrontOutwardLocalDirection);
                TownGrid doorGrid = pathingManager.Grid;
                outward.y = 0f;
                if (doorGrid != null && outward.sqrMagnitude > 0.0001f)
                {
                    outward.Normalize();
                    float step = Mathf.Max(0.2f, doorGrid.CellSizeMeters * 0.25f);
                    int maxSteps = Mathf.CeilToInt(doorGrid.CellSizeMeters * 8f / step);
                    for (int i = 0; i <= maxSteps; i++)
                    {
                        Vector3 candidate = door + outward * (i * step);
                        GridCoord coord = doorGrid.WorldToCoord(candidate);
                        if (!doorGrid.IsInBounds(coord)) continue;
                        TownCell cell = doorGrid.GetCell(coord);
                        if (cell.HasBuilding || !pathingManager.TryGetMovementCost(coord, out _)) continue;
                        anchor.coord = coord;
                        anchor.hasAuthoredWorldPosition = true;
                        anchor.authoredWorldPosition = candidate;
                        anchor.wasReconciledToExteriorAccess = true;
                        anchor.reconciliationNote = "Resolved the footprint-derived front door to its walkable exterior approach.";
                        return true;
                    }
                }
                anchor = default;
                building = null;
                return false;
            }

            TownGrid grid = pathingManager.Grid;
            foreach (BuildingEntrancePairAuthoring authored in authority.EntrancePairs)
            {
                if (authored == null
                    || !authority.TryGetEntrancePair(authored.id, out BuildingEntrancePairAuthoring entrance, out _))
                    continue;

                Vector3 authoredOutdoor = authority.transform.TransformPoint(entrance.outdoorApproachLocal);
                Vector3 indoorThreshold = authority.transform.TransformPoint(entrance.indoorPositionLocal);
                Vector3 outward = authoredOutdoor - indoorThreshold;
                outward.y = 0f;
                if (grid == null || outward.sqrMagnitude < 0.0001f) continue;
                outward.Normalize();

                // Entrance authoring can place the outdoor point close enough to
                // the wall that its coarse town-grid cell is still owned by the
                // building footprint. Such a cell is intentionally rejected by
                // PathingManager's interior-floor validation. Walk outward along
                // the authored doorway axis until the first genuinely exterior,
                // traversable cell is found; never treat a building anchor cell
                // as an outdoor path endpoint.
                float step = Mathf.Max(0.2f, grid.CellSizeMeters * 0.25f);
                int maxSteps = Mathf.CeilToInt(grid.CellSizeMeters * 8f / step);
                Vector3 outdoorApproach = default;
                GridCoord approachCoord = default;
                bool foundExteriorCell = false;
                for (int stepIndex = 0; stepIndex <= maxSteps; stepIndex++)
                {
                    Vector3 candidate = authoredOutdoor + outward * (stepIndex * step);
                    GridCoord candidateCoord = grid.WorldToCoord(candidate);
                    if (!grid.IsInBounds(candidateCoord)) continue;
                    TownCell candidateCell = grid.GetCell(candidateCoord);
                    if (candidateCell.HasBuilding || !pathingManager.TryGetMovementCost(candidateCoord, out _))
                        continue;
                    outdoorApproach = candidate;
                    approachCoord = candidateCoord;
                    foundExteriorCell = true;
                    break;
                }
                if (!foundExteriorCell) continue;

                anchor.coord = approachCoord;
                anchor.hasAuthoredWorldPosition = true;
                anchor.authoredWorldPosition = outdoorApproach;
                anchor.wasReconciledToExteriorAccess = true;
                anchor.reconciliationNote = $"Resolved '{entrance.id}' to its validated outdoor approach for physical routing.";
                return true;
            }

            // Do not fall back to an indoor threshold cell when this building
            // explicitly declares entrance pairs but none has usable exterior
            // path access. A refused route is safer than repeatedly attempting
            // movement through a wall or an invalid interior endpoint.
            anchor = default;
            building = null;
            return false;
        }

        private AgentMover CreateAgent(PersonState person)
        {
            GameObject prefab = ResolveAgentVisualPrefab(person, maleAgentVisualPrefab, femaleAgentVisualPrefab);
            GameObject agentObject = new($"Person {person.id:000} {person.DisplayName}");
            agentObject.transform.SetParent(EnsureAgentRoot(), false);

            PersonVisualIdentityBinding identity = agentObject.AddComponent<PersonVisualIdentityBinding>();
            identity.Bind(person.id);
            WorldInspectableTarget inspectable = agentObject.AddComponent<WorldInspectableTarget>();
            inspectable.ConfigurePerson(person.id);
            CapsuleCollider selectionCollider = agentObject.AddComponent<CapsuleCollider>();
            float selectionHeight = prefab != null
                ? (person.isFemale ? FemaleReferenceHeightMetres : MaleReferenceHeightMetres)
                : FallbackSelectionHeightMetres;
            selectionCollider.center = new Vector3(0f, selectionHeight * 0.5f, 0f);
            selectionCollider.radius = 0.28f;
            selectionCollider.height = selectionHeight;
            selectionCollider.isTrigger = true;

            GameObject visualObject = prefab != null
                ? TryInstantiateGameObject(prefab, agentObject.transform, $"person {person.id:000} visual")
                : null;

            if (visualObject == null)
            {
                visualObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                visualObject.transform.SetParent(agentObject.transform, false);
            }

            visualObject.name = "Visual";
            // Keep an authored vertical visual correction (for example, to align
            // scaled character feet with the world floor) while keeping each
            // actor centered on its authoritative X/Z position.
            Vector3 authoredVisualPosition = visualObject.transform.localPosition;
            visualObject.transform.localPosition = new Vector3(0f, authoredVisualPosition.y, 0f);
            AgentMover mover = agentObject.GetComponent<AgentMover>();
            if (mover == null)
            {
                mover = agentObject.AddComponent<AgentMover>();
            }

            mover.ConfigureVisualRoot(visualObject.transform);
            mover.ArrivedAtDestination -= HandleAgentArrivedAtDestination;
            mover.ArrivedAtDestination += HandleAgentArrivedAtDestination;
            return mover;
        }

        private static GameObject ResolveAgentVisualPrefab(
            PersonState person,
            GameObject malePrefab,
            GameObject femalePrefab)
        {
            if (person == null)
            {
                return null;
            }

            return person.isFemale ? femalePrefab : malePrefab;
        }

        private void HandleAgentArrivedAtDestination(AgentMover mover)
        {
            if (mover == null)
            {
                return;
            }

            for (int i = 0; i < agents.Count; i++)
            {
                ManagedPersonAgent managed = agents[i];
                if (managed.mover != mover || managed.person == null)
                {
                    continue;
                }

                RecordMovementTrace(managed, "arrived", "ArrivedVisible",
                    managed.person.currentDestinationBuildingId, string.Empty);

                PopulationScheduleState previous = managed.person.scheduleState;
                if (managed.routeMode == AgentRouteMode.AmbientOutbound)
                {
                    if (TrySendAmbientReturnRoute(managed))
                    {
                        RefreshMovementStatus($"{managed.person.DisplayName} turned back from ambient street walk.");
                    }
                    else
                    {
                        RefreshMovementStatus($"{managed.person.DisplayName} could not return from ambient street walk.");
                    }

                    return;
                }

                if (managed.routeMode == AgentRouteMode.AmbientReturning)
                {
                    ClearAmbientRoute(managed);
                    RefreshMovementStatus($"{managed.person.DisplayName} completed ambient street walk.");
                    return;
                }

                if (managed.routeMode == AgentRouteMode.TaskDirected)
                {
                    // Keep currentDestinationBuildingId and the transient
                    // schedule state intact. The owning task driver verifies
                    // the physical arrival and performs its own state change.
                    managed.routeMode = AgentRouteMode.None;
                    RefreshMovementStatus($"{managed.person.DisplayName} reached a task-directed destination.");
                    return;
                }

                managed.routeMode = AgentRouteMode.None;
                if (!PopulationManager.TryGetArrivalScheduleState(previous, out PopulationScheduleState arrivalState))
                {
                    return;
                }

                managed.person.scheduleState = arrivalState;
                if (arrivalState == PopulationScheduleState.AtHome)
                {
                    managed.person.currentDestinationBuildingId = managed.person.homeBuildingId;
                }
                else if (arrivalState == PopulationScheduleState.AtWork && managed.person.workplaceBuildingId >= 0)
                {
                    managed.person.currentDestinationBuildingId = managed.person.workplaceBuildingId;
                }

                RefreshMovementStatus($"{managed.person.DisplayName} arrived: {previous} -> {arrivalState}.");
                return;
            }
        }

        private static void ClearAmbientRoute(ManagedPersonAgent managed)
        {
            if (managed == null)
            {
                return;
            }

            managed.routeMode = AgentRouteMode.None;
            managed.ambientOriginBuildingId = -1;
            managed.ambientTargetBuildingId = -1;
        }

        private void UnsubscribeManagedAgents()
        {
            for (int i = 0; i < agents.Count; i++)
            {
                if (agents[i].mover != null)
                {
                    agents[i].mover.ArrivedAtDestination -= HandleAgentArrivedAtDestination;
                }
            }
        }

        private static GameObject TryInstantiateGameObject(GameObject prefab, Transform parent, string label)
        {
            if (prefab == null)
            {
                return null;
            }

            try
            {
                UnityEngine.Object instance = parent != null
                    ? Instantiate((UnityEngine.Object)prefab, parent)
                    : Instantiate((UnityEngine.Object)prefab);

                if (instance is GameObject gameObject)
                {
                    return gameObject;
                }

                if (instance is Component component)
                {
                    return component.gameObject;
                }
            }
            catch (System.Exception exception)
            {
                Debug.LogError($"Failed to instantiate {label} prefab '{prefab.name}': {exception.Message}", prefab);
            }

            return null;
        }

        private Transform EnsureAgentRoot()
        {
            if (agentRoot != null)
            {
                return agentRoot;
            }

            Transform existing = transform.Find(AgentRootName);
            if (existing != null)
            {
                agentRoot = existing;
                return agentRoot;
            }

            GameObject root = new(AgentRootName);
            root.transform.SetParent(transform, false);
            agentRoot = root.transform;
            return agentRoot;
        }

        private void SubscribeToTime()
        {
            AutoWire();
            if (timeManager != null)
            {
                timeManager.BeforeShortTick -= AdvanceRoutesBeforeShortTick;
                timeManager.BeforeShortTick += AdvanceRoutesBeforeShortTick;
                timeManager.ShortTick -= OnShortTick;
                timeManager.ShortTick += OnShortTick;
            }
        }

        private void AutoWire()
        {
            townWorld ??= FindAnyObjectByType<TownWorldController>();
            populationManager ??= FindAnyObjectByType<PopulationManager>();
            pathingManager ??= FindAnyObjectByType<PathingManager>();
            storeRuntime ??= FindAnyObjectByType<GeneralStoreRuntimeManager>();
            timeManager ??= TimeManager.Instance != null ? TimeManager.Instance : FindAnyObjectByType<TimeManager>();
            systemsHub ??= FindAnyObjectByType<SimulationSystemsHub>();
        }

        private void OnValidate()
        {
            maxActiveAgents = Mathf.Max(1, maxActiveAgents);
            minimumVisibleStreetAgents = Mathf.Max(0, minimumVisibleStreetAgents);
            ambientRouteAttemptBudget = Mathf.Max(1, ambientRouteAttemptBudget);
        }

        private sealed class ManagedPersonAgent
        {
            public ManagedPersonAgent(PersonState person, AgentMover mover, BuildingAnchor homeAnchor)
            {
                this.person = person;
                this.mover = mover;
                this.homeAnchor = homeAnchor;
                ambientOriginBuildingId = -1;
                ambientTargetBuildingId = -1;
            }

            public readonly PersonState person;
            public readonly AgentMover mover;
            public readonly BuildingAnchor homeAnchor;
            public AgentRouteMode routeMode;
            // Hold a freight receiver's destination through its arrival check;
            // scheduled home/work movement may resume after the handoff consumes it.
            public int freightReceivingTargetBuildingId = -1;
            // Slice R1 (Objective 22): true while the person is boarded
            // on the wagon (set by TryBoardRider, cleared by
            // TryDismountRider). Runtime-only, never serialized —
            // boarding is re-derived at leg start (§8 of the plan).
            public bool isRiding;
            public GridCoord? transportStagingCoord;
            public Transform transportStagingSeat;
            public Vector3 transportStagingSeatPosition;
            public int ambientOriginBuildingId;
            public int ambientTargetBuildingId;
            public string domesticActivityTaskId;
            public string domesticActivityLocationId;
            public int domesticActivityRouteGeneration = -1;
            public double domesticActivityRouteStartedGameSeconds = double.NaN;
            public double domesticActivityLastProgressGameSeconds = double.NaN;
            public float domesticActivityRouteTimeoutGameSeconds;
            public float domesticActivityNoProgressTimeoutGameSeconds;
            public float domesticActivityLastRemainingDistance;
        }
    }
}
