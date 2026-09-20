using System;
using System.Collections.Generic;
using LandLedgers.Pathing;
using LandLedgers.Population;
using LandLedgers.Time;
using LandLedgers.World;
using UnityEngine;

namespace LandLedgers.MVP
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(270)]
    public sealed class PopulationPathingDirector : MonoBehaviour
    {
        private const string AgentRootName = "PopulationAgentsRoot";

        private enum AgentRouteMode
        {
            None = 0,
            Scheduled = 1,
            AmbientOutbound = 2,
            AmbientReturning = 3
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
        private int minimumVisibleStreetAgents = 4;

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

        [SerializeField]
        private string lastRouteFailure;

        [SerializeField]
        private string lastMovementStatus;

        private readonly List<ManagedPersonAgent> agents = new();
        private bool workRoutesIssued;
        private bool shoppingRoutesIssued;
        private bool homeRoutesIssued;
        private bool loggedNoManagedAgents;
        private bool loggedAmbientRouteFailure;
        private bool loggedScheduledRouteFailure;
        private int lastDayIndex = -1;

        public string LastMovementStatus => lastMovementStatus ?? string.Empty;
        public string LastRouteFailure => lastRouteFailure ?? string.Empty;
        public int ManagedPeopleCount => managedPeople;
        public int VisibleStreetAgentCount => visibleStreetAgents;

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

            return $"{readiness} Managed={managedPeople}, Visible={visibleStreetAgents}, WorkIssued={workRoutesIssued}, ShoppingIssued={shoppingRoutesIssued}, HomeIssued={homeRoutesIssued}, AmbientStarted={ambientRoutesStartedToday}, AmbientFailures(anchor/path)={ambientAnchorFailuresToday}/{ambientPathFailuresToday}{scheduledFailures}, Last={LastMovementStatus}";
        }

        public void InitializeIfNeeded()
        {
            AutoWire();
            if (populationManager == null || populationManager.State == null || pathingManager == null || townWorld == null)
            {
                lastMovementStatus = "Missing population, pathing, or town world.";
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
                if (TrySendAgent(managed, storeBuildingId, PopulationScheduleState.GoingShopping, out ScheduledRouteFailureKind failureKind))
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

            if (!homeRoutesIssued && context.TimeOfDay01 >= 0.62f)
            {
                routesStartedToday += SendDueWorkersHome(context.TimeOfDay01);
                if (context.TimeOfDay01 >= 0.86f)
                {
                    homeRoutesIssued = true;
                }
            }

            EnsureAmbientStreetLife();
        }

        private void PopulateManagedAgents()
        {
            PopulationState state = populationManager.State;
            for (int i = 0; i < state.people.Count && agents.Count < maxActiveAgents; i++)
            {
                PersonState person = state.people[i];
                if (person.workplaceBuildingId < 0 && person.householdId % 4 != 0)
                {
                    continue;
                }

                BuildingAnchor homeAnchor;
                if (!TryGetDoorAnchor(person.homeBuildingId, out homeAnchor))
                {
                    continue;
                }

                AgentMover mover = CreateAgent(person);
                mover.Configure(pathingManager);
                mover.transform.position = pathingManager.CoordToPathWorld(homeAnchor.coord);
                mover.HideImmediately();

                agents.Add(new ManagedPersonAgent(person, mover, homeAnchor));
            }

            managedPeople = agents.Count;
            RefreshMovementStatus($"Managing {managedPeople} generated people.");
            if (managedPeople == 0 && !loggedNoManagedAgents)
            {
                loggedNoManagedAgents = true;
                Debug.LogWarning("PopulationPathingDirector did not create any visible-agent pool entries. Check generated population homes and front-door anchors.", this);
            }
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
            int sent = SendRouteForState(
                PopulationScheduleState.AtHome,
                PopulationScheduleState.GoingToWork,
                person => person.workplaceBuildingId >= 0 && timeOfDay01 >= GetWorkDepartureTime01(person),
                person => person.workplaceBuildingId);
            if (sent > 0)
            {
                RefreshMovementStatus($"Sent {sent} profession-timed worker(s) to work.");
            }

            return sent;
        }

        private int SendDueWorkersHome(float timeOfDay01)
        {
            InitializeIfNeeded();
            int sent = SendRouteForState(
                PopulationScheduleState.ReturningHome,
                person => person.homeBuildingId >= 0
                    && person.scheduleState != PopulationScheduleState.AtHome
                    && person.scheduleState != PopulationScheduleState.ReturningHome
                    && timeOfDay01 >= GetWorkReturnTime01(person),
                person => person.homeBuildingId);
            if (sent > 0)
            {
                RefreshMovementStatus($"Sent {sent} profession-timed worker(s) home.");
            }

            return sent;
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

        private bool TrySendAgent(ManagedPersonAgent managed, int targetBuildingId, PopulationScheduleState nextState, out ScheduledRouteFailureKind failureKind)
        {
            failureKind = ScheduledRouteFailureKind.CandidateNotReady;
            if (managed == null || managed.person == null || managed.mover == null)
            {
                return false;
            }

            if (!TryGetDoorAnchor(managed.person.currentDestinationBuildingId, out BuildingAnchor startAnchor, out PlacedBuilding startBuilding)
                && !TryGetDoorAnchor(managed.person.homeBuildingId, out startAnchor, out startBuilding))
            {
                failureKind = ScheduledRouteFailureKind.MissingStartAnchor;
                return false;
            }

            if (!TryGetDoorAnchor(targetBuildingId, out BuildingAnchor targetAnchor, out PlacedBuilding targetBuilding))
            {
                failureKind = ScheduledRouteFailureKind.MissingTargetAnchor;
                return false;
            }

            if (!managed.mover.BeginDoorToDoor(startAnchor, targetAnchor, true, startBuilding?.doorAnimator, targetBuilding?.doorAnimator))
            {
                failureKind = ScheduledRouteFailureKind.PathFailed;
                return false;
            }

            managed.routeMode = AgentRouteMode.Scheduled;
            managed.ambientOriginBuildingId = -1;
            managed.ambientTargetBuildingId = -1;
            managed.person.scheduleState = nextState;
            managed.person.currentDestinationBuildingId = targetBuildingId;
            failureKind = ScheduledRouteFailureKind.None;
            return true;
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

            if (!managed.mover.BeginDoorToDoor(startAnchor, targetAnchor, true, startBuilding?.doorAnimator, targetBuilding?.doorAnimator))
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

            if (!managed.mover.BeginDoorToDoor(startAnchor, targetAnchor, true, startBuilding?.doorAnimator, targetBuilding?.doorAnimator))
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

            return pathingManager.TryFindAnchorByBuildingId(buildingId, AnchorType.FrontDoor, out building, out anchor);
        }

        private AgentMover CreateAgent(PersonState person)
        {
            GameObject prefab = person.id % 2 == 0 ? maleAgentVisualPrefab : femaleAgentVisualPrefab;
            GameObject agentObject = new($"Person {person.id:000} {person.DisplayName}");
            agentObject.transform.SetParent(EnsureAgentRoot(), false);

            GameObject visualObject = prefab != null
                ? TryInstantiateGameObject(prefab, agentObject.transform, $"person {person.id:000} visual")
                : null;

            if (visualObject == null)
            {
                visualObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                visualObject.transform.SetParent(agentObject.transform, false);
            }

            visualObject.name = "Visual";
            visualObject.transform.localPosition = Vector3.zero;
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
            public int ambientOriginBuildingId;
            public int ambientTargetBuildingId;
        }
    }
}
