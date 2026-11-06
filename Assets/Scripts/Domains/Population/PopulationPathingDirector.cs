using System;
using System.Collections.Generic;
using LandLedgers.Economy.Transport;
using LandLedgers.Pathing;
using LandLedgers.Population;
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

            return $"{readiness} Managed={managedPeople}, Visible={visibleStreetAgents}, ResidentsScanned={populationRecordsExamined}, NonLocalOrInvalid={recordsRejectedAsNonLocalResidents}, MissingHomeDoors={residentsMissingHomeDoorAnchors}, ActorEntriesCreated={residentActorEntriesCreated}, WorkIssued={workRoutesIssued}, ShoppingIssued={shoppingRoutesIssued}, HomeIssued={homeRoutesIssued}, AmbientStarted={ambientRoutesStartedToday}, AmbientFailures(anchor/path)={ambientAnchorFailuresToday}/{ambientPathFailuresToday}{scheduledFailures}, Last={LastMovementStatus}";
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

        private void PopulateManagedAgents()
        {
            populationRecordsExamined = 0;
            recordsRejectedAsNonLocalResidents = 0;
            residentsMissingHomeDoorAnchors = 0;
            residentActorEntriesCreated = 0;
            PopulationState state = populationManager.State;
            for (int i = 0; i < state.people.Count && agents.Count < maxActiveAgents; i++)
            {
                PersonState person = state.people[i];
                populationRecordsExamined++;
                if (!CanRepresentOnMap(person, state))
                {
                    recordsRejectedAsNonLocalResidents++;
                    continue;
                }

                BuildingAnchor homeAnchor;
                if (!TryGetDoorAnchor(person.homeBuildingId, out homeAnchor))
                {
                    residentsMissingHomeDoorAnchors++;
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
                mover.transform.position = pathingManager.CoordToPathWorld(currentAnchor.coord);
                mover.ShowAtCurrentLocation();

                agents.Add(new ManagedPersonAgent(person, mover, homeAnchor));
                residentActorEntriesCreated++;
            }

            managedPeople = agents.Count;
            RefreshMovementStatus($"Managing {managedPeople} generated people.");
            if (managedPeople == 0 && !loggedNoManagedAgents)
            {
                loggedNoManagedAgents = true;
                Debug.LogWarning("PopulationPathingDirector did not create any visible-agent pool entries. Check generated population homes and front-door anchors.", this);
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

        /// <summary>
        /// Starts a named person's existing world-space custom path for a task.
        /// Arrival-driven travel remains open until the actor reaches the target
        /// anchor; a JourneyModel estimate does not complete the task.
        /// </summary>
        public bool TryStartExplicitRoute(int personId, int targetBuildingId, PopulationScheduleState nextState)
        {
            if (personId < 0 || targetBuildingId < 0) return false;
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
                    Debug.Log($"[CoupledTransport] refused explicit route for P{personId}: rider is aboard the wagon.", this);
                    return false;
                }
                if (managed.person.currentDestinationBuildingId == targetBuildingId
                    && managed.mover != null && (managed.mover.IsArrivedVisible || managed.mover.IsRouteActive))
                {
                    managed.person.scheduleState = nextState;
                    return true;
                }
                return TrySendAgent(managed, targetBuildingId, nextState, out _);
            }
            return false;
        }

        /// <summary>
        /// Slice F4 (2026-11-06, Objective 23): cell-targeted thin API.
        /// Mirrors TryStartExplicitRoute's agent lookup, but sends the
        /// person to a grid cell instead of a building. Its only
        /// production caller was the F4 coupled-transport escort, which
        /// R1 (Objective 22) deleted — Ethan rides the wagon instead of
        /// walking it. R4 deletes this method if it is still callerless;
        /// until then it stays, guarded by the riding flag. Returns
        /// false (no-op, no log spam) when the person has no managed
        /// agent or no mover. Never touches schedule state: the mover
        /// owns locomotion and arrival, so the S5 arrival gates keep
        /// working.
        /// </summary>
        public bool TrySendPersonToCell(int personId, GridCoord targetCell, bool hideWhenArrived)
        {
            if (personId < 0) return false;
            for (int i = 0; i < agents.Count; i++)
            {
                ManagedPersonAgent managed = agents[i];
                if (managed?.person == null || managed.person.id != personId) continue;
                if (managed.mover == null) return false;
                // Slice R1 (Objective 22): refuse silently while the
                // rider is aboard — same no-log-spam contract as the
                // no-agent / no-mover returns above.
                if (managed.isRiding) return false;
                return managed.mover.MoveFromCurrentCellTo(targetCell, hideWhenArrived);
            }
            return false;
        }

        /// <summary>
        /// Slice F5 (2026-11-06, Objective 25): hide a person via the
        /// mover's hide path (AgentMover.HideImmediately — the F5 coder
        /// verification point confirmed the exact call; the root
        /// GameObject is never deactivated). Same lookup as
        /// TrySendPersonToCell; false (no-op, no log spam) when the person
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
                    managed.mover.transform.SetParent(seat, false);
                    managed.mover.transform.localPosition = new Vector3(0f, RiderSeatVerticalOffsetMetres, 0f);
                    // The wagon's length runs along its local +X and
                    // the agent's forward is his root +Z: a +90 deg yaw
                    // maps local +Z onto local +X, so he faces along
                    // the heading while seated.
                    managed.mover.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                }
                managed.isRiding = true;
                // Sitting application is step 5 (R2 wires it for real;
                // the R1 sequencing stub logs and returns false).
                TrySetPersonSitting(personId, seated: true);
                Debug.Log($"[CoupledTransport] rider boarded: P{personId} seated at DriverSeat for {legLabel}.", this);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Slice R2 (2026-11-06, Objective 22): apply (or clear) the
        /// rider's sitting pose. R1 SEQUENCING STUB — the body is
        /// implemented in R2; until then this logs and returns false so
        /// the R1 commit builds, and boarding still succeeds (the rider
        /// rides parented to the seat in whatever pose his Animator
        /// holds).
        /// </summary>
        public bool TrySetPersonSitting(int personId, bool seated)
        {
            Debug.Log($"[CoupledTransport] sitting trigger not yet wired for P{personId} (seated={seated}); riding without the sitting pose.", this);
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
            return false;
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

            // Keep the same actor visible when it reaches home or work. The
            // authoritative Person location changes on arrival; until the world
            // has authored interiors/occupancy rendering, hiding every arrived
            // actor makes residents vanish from the simulation's visible town.
            if (!managed.mover.BeginDoorToDoor(startAnchor, targetAnchor, false, startBuilding?.doorAnimator, targetBuilding?.doorAnimator))
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

            return pathingManager.TryFindAnchorByBuildingId(buildingId, AnchorType.FrontDoor, out building, out anchor);
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
            selectionCollider.center = new Vector3(0f, 0.9f, 0f);
            selectionCollider.radius = 0.28f;
            selectionCollider.height = 1.8f;
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
            // Slice R1 (Objective 22): true while the person is boarded
            // on the wagon (set by TryBoardRider, cleared by
            // TryDismountRider). Runtime-only, never serialized —
            // boarding is re-derived at leg start (§8 of the plan).
            public bool isRiding;
            public int ambientOriginBuildingId;
            public int ambientTargetBuildingId;
        }
    }
}
