using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;
using LandLedgers.World.Journeys;

namespace LandLedgers.Population
{
    public enum DomesticActivityMovementResult { Traveling, Arrived, Unsupported }

    /// <summary>
    /// Pass IV (Worker 4): the LIVE driver for <see cref="DomesticLaborService"/>.
    /// The service previously had tests but no live driver — domestic work only
    /// happened in single instant calls. This driver moves domestic tasks through
    /// real time:
    ///
    ///   Day boundary: needs-driven domestic tasks are PLANNED (meal prep when
    ///     the household eats at home, fuel handling in cold season, animal care
    ///     when the household keeps animals, baseline chores) and assigned to
    ///     members who are actually free — never invented persons or time.
    ///   Tick/due-event: when a planned window arrives the task is scheduled
    ///     (the service reserves person-time and starts it) and a
    ///     TaskCompletionDue event is registered; when the event fires the work
    ///     is recorded and the service completes it (family-labor credit, zero
    ///     wages, reservation released).
    ///
    /// Because completions are event-driven, the same day produces identical
    /// consequential outcomes at 1-minute ticks and at 100x jumps.
    ///
    /// Orphaned-assignment fix: a task whose start failed used to sit Assigned
    /// forever (the service released the reservation and budget but left the
    /// TaskAuthority task). ReconcileStalled requeues such tasks via
    /// InterruptTask (release + back to Queued) so the next DriveDay re-drives
    /// them — or cancels them when nobody can ever do the work.
    /// </summary>
    public sealed class DomesticLaborDriver
    {
        /// <summary>Inputs for one household's domestic day.</summary>
        [Serializable]
        public sealed class DomesticDayInputs
        {
            public int HouseholdId = -1;
            public List<int> MemberIds = new List<int>();
            public Func<int, AgeBand> AgeBandFor;
            /// <summary>Optional external hard-commitment query, e.g. an authored paid-work window.</summary>
            public Func<int, int, int, bool> IsWindowUnavailable;
            /// <summary>Meals the household eats at home today (meal-prep trigger).</summary>
            public int MealsAtHomeCount;
            /// <summary>True only when an actual household need requires compatible fuel to be handled.</summary>
            public bool FuelHandlingNeeded;
            /// <summary>
            /// Whether the household home currently has a valid authored
            /// firewood pickup point. Null preserves legacy callers that do
            /// not supply building-authority data; false is a known physical
            /// blocker and true permits a previously deferred trip to resume.
            /// </summary>
            public bool? FirewoodActivityLocationAvailable;
            /// <summary>
            /// Fix #1 (A4 fallback): whether the household home currently
            /// has a stove the fuel-handling leg can fall back to when no
            /// Firewood point is authored. True means a missing Firewood
            /// point is not a capability block: the trip runs as a single
            /// leg to the stove (resolved by
            /// DomesticActivityLocationResolver.ResolveFuelLeg0LocationId).
            /// Null preserves legacy callers that do not supply the flag
            /// (no fallback known, old park behaviour). Stove-less
            /// households never reach this gate: minting already refuses
            /// a fuel-handling task with no stove at all.
            /// </summary>
            public bool? FuelHandlingStoveFallbackAvailable;
            /// <summary>True only when a supported household cleanliness need exists.</summary>
            public bool CleaningNeeded;
            public bool ColdSeason;
            public bool HasAnimals;
            /// <summary>
            /// Lane 9c (M-R4, Agent M §3.1/§7): whether the household home
            /// currently has a usable animal-care destination. Null
            /// preserves the field for legacy callers that do not supply
            /// building-authority data; false is a known physical
            /// blocker and true permits scheduling. Unlike the firewood
            /// flag, null does NOT preserve legacy scheduling — the
            /// legacy behaviour (schedule daily with no destination) is
            /// the defect, so unknown fails closed. No pen/pasture
            /// activity location type exists today, so production
            /// supplies false until the owner authors a destination.
            /// </summary>
            public bool? AnimalCareDestinationAvailable;
            /// <summary>
            /// B2 (Agent B, expanded campaign 2026-11-16): the household's
            /// stove registry, when the caller has one (Agent G wires it
            /// from the hub/state in production). Null means no stove
            /// information — meal-prep plans as before, with
            /// MealPrepMode Unspecified. The registry never blocks the
            /// task: without a ready stove the mode is ColdFoodFallback
            /// and the household eats cold food from stores.
            /// </summary>
            public HouseholdStoveRegistry Stoves;
            /// <summary>
            /// B2: the household's fuel-wood reserve units as the caller
            /// reads them, for the stove gate's fuel support check.
            /// -1 means unknown (legacy callers) — only fuel already in
            /// the stove stores counts. Agent G wires the real value
            /// from the household reserve.
            /// </summary>
            public int FuelWoodReserveUnits = -1;
            /// <summary>
            /// B3 (Agent B, expanded campaign 2026-11-16): the household's
            /// state, when the caller has it (SimulationDrivers passes it
            /// from the daily drive). The driver reads the fuel-wood
            /// reserve and the heating forecast from it for the fuel trip
            /// cargo. Null means unknown — the fuel trip cannot pick up
            /// fuel and the cooking settlement is skipped.
            /// </summary>
            public HouseholdState Household;
            /// <summary>
            /// B3: the household's inventory, when the caller has it
            /// (hub.HouseholdInventories). The cooking settlement reads
            /// flour from it. Null means unknown — cooking produces
            /// nothing.
            /// </summary>
            public HouseholdInventory Inventory;
            /// <summary>
            /// Heating lane (Nov 20 2026): the exterior mean temperature of
            /// the forecast day the fuel trip carries for (tomorrow's burn),
            /// in C. Null means unknown — the driver resolves the forecast
            /// day's temperature with the default scenario seed. Callers
            /// that already resolved the day's temperature (the daily
            /// drive, tests pinning the burn) pass it in so the cargo
            /// matches the burn the stove store will feed next.
            /// </summary>
            public float? FuelForecastExteriorMeanCelsius;
            /// <summary>
            /// B3: the meal authority, when the caller has it (constructed
            /// from hub.MealLog). The cooking settlement records
            /// production through it. Null means unknown — cooking
            /// produces nothing.
            /// </summary>
            public HouseholdMealService MealService;
        }

        /// <summary>One planned domestic task: not yet scheduled.</summary>
        [Serializable]
        public sealed class PendingDomesticTask
        {
            public int HouseholdId = -1;
            public int PersonId = -1;
            public EntityId ExistingTaskId;
            public string DefinitionId = string.Empty;
            public int DayIndex;
            public int StartMinuteOfDay;
            public string Status = "Pending"; // Pending | Started | Failed
            public string Note = string.Empty;
            [NonSerialized] public Func<int, int, int, bool> IsWindowUnavailable;
            /// <summary>
            /// B2 (Agent B, expanded campaign 2026-11-16): how this
            /// meal-prep task cooks — StoveCooking when the household's
            /// stove gate passed, ColdFoodFallback when it fell back to
            /// cold food from stores, Unspecified when no stove registry
            /// was consulted (legacy). Only meal-prep tasks carry a
            /// mode; B3 reads it at cook time (fuel from the stove
            /// store for StoveCooking, none for ColdFoodFallback).
            /// Additive: old saves deserialize it as Unspecified — the
            /// honest old-save default (not evaluated).
            /// </summary>
            public HouseholdStoveMealPrepMode MealPrepMode = HouseholdStoveMealPrepMode.Unspecified;
        }

        /// <summary>One in-flight domestic task: scheduled, awaiting its due event.</summary>
        [Serializable]
        public sealed class InFlightDomesticTask
        {
            public EntityId TaskId;
            public string TaskIdString = string.Empty;
            public int HouseholdId = -1;
            public int PersonId = -1;
            public string DefinitionId = string.Empty;
            public int DayIndex;
            public string DueEventId = string.Empty;
            public bool RequiresPhysicalArrival;
            public string ActivityLocationId = string.Empty;
            public int BuildingId = -1;
            /// <summary>
            /// B3 (Agent B, expanded campaign 2026-11-16): the fuel-handling
            /// trip leg — 0 = travelling to the firewood pile, 1 = carrying
            /// fuel to the stove. Only fuel-handling flights use it; every
            /// other definition stays on leg 0. Persisted: a save mid-trip
            /// resumes on the same leg with its hold intact.
            /// </summary>
            public int FuelHandlingLeg;
            /// <summary>
            /// B3: fuel-wood units currently carried by this person on a fuel
            /// trip (the in-transit reservation, plan §5.1 step 3). The
            /// reserve is debited only on stove receipt; a failed trip
            /// releases the hold with nothing debited.
            /// </summary>
            public int FuelUnitsInTransit;
            /// <summary>B3: the stove the in-transit fuel is bound for (asset id). Empty when nothing is carried.</summary>
            public string FuelStoveAssetId = string.Empty;
            /// <summary>
            /// B3: how this meal-prep task cooks, copied from the pending
            /// task at start time — StoveCooking burns stove fuel and
            /// produces through the meal authority, ColdFoodFallback and
            /// Unspecified produce nothing. Only meal-prep flights carry a
            /// mode.
            /// </summary>
            public HouseholdStoveMealPrepMode MealPrepMode = HouseholdStoveMealPrepMode.Unspecified;
        }

        /// <summary>
        /// Keeps a task recoverable after a blocked physical route without
        /// reclaiming a worker every day. The task identity, retry date and
        /// last route reason survive Save/Continue.
        /// </summary>
        [Serializable]
        public sealed class DomesticRouteRecoveryRecord
        {
            public EntityId TaskId;
            public int FailureCount;
            public int RetryAfterDay;
            /// <summary>
            /// Structural location blockers wait for a capability change
            /// instead of waking a worker on an endless exponential retry.
            /// The saved queued task remains the household's open need.
            /// </summary>
            public bool WaitForCapabilityChange;
            public string ActivityLocationId = string.Empty;
            public string LastFailureReason = string.Empty;
        }

        public const string MissingFirewoodStorageLocationId =
            "firewood: no firewood storage is authored on the home.";

        private readonly List<PendingDomesticTask> pending = new List<PendingDomesticTask>();
        private readonly List<InFlightDomesticTask> inFlight = new List<InFlightDomesticTask>();
        private readonly List<DomesticRouteRecoveryRecord> routeRecoveries = new List<DomesticRouteRecoveryRecord>();
        private readonly List<string> diagnostics = new List<string>();
        /// <summary>
        /// B3: the cooking conversion for one meal-prep session — 2 flour
        /// to 1 bread, the conversion pinned by the Phase B household
        /// consumption tests. Provisional default (plan §5.1 step 6):
        /// the smallest meaningful production, one loaf per session.
        /// Agent F owns the recipe book; F or the owner corrects the
        /// ratio and the session output. No recipe is invented here.
        /// </summary>
        public const int FlourUnitsPerLoaf = 2;
        public const int LoavesPerMealPrepSession = 1;
        // B3: per-household cooking/fuel context, supplied each DriveDay
        // from the caller's inputs. TRANSIENT — never persisted (the
        // driver save DTO holds only pending/in-flight/cleanliness).
        // Repopulated every DriveDay; empty after a mid-day Continue
        // until the next day boundary. A flight whose context is missing
        // settles nothing and consumes nothing — the failure is
        // recorded loudly, never silently. LoadFromSaveDto clears
        // these maps, so a restored driver never settles against a
        // previous session's objects.
        private readonly Dictionary<int, HouseholdState> householdById = new Dictionary<int, HouseholdState>();
        private readonly Dictionary<int, HouseholdStoveRegistry> stovesByHousehold = new Dictionary<int, HouseholdStoveRegistry>();
        private readonly Dictionary<int, HouseholdInventory> inventoryByHousehold = new Dictionary<int, HouseholdInventory>();
        private readonly Dictionary<int, HouseholdMealService> mealServiceByHousehold = new Dictionary<int, HouseholdMealService>();
        // Heating lane (Nov 20 2026): per-household forecast temperature for
        // the fuel trip cargo (transient, never persisted — same lifecycle
        // as the other per-household context maps below).
        private readonly Dictionary<int, float> fuelForecastTemperatureByHousehold = new Dictionary<int, float>();

        /// <summary>
        /// Campaign C-24 (DEF-010, Decision 6): the household cleanliness
        /// need state. No cleanliness state existed in the repo, so this
        /// tracker is the new measurable state: use accumulates per occupant
        /// per day, a Chores completion resets it. Owned here because this
        /// driver owns domestic state; persisted through
        /// DomesticLaborDriverSaveDto alongside pending/in-flight.
        /// </summary>
        private readonly HouseholdCleanlinessTracker cleanliness = new HouseholdCleanlinessTracker();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<PendingDomesticTask> Pending => pending;
        public IReadOnlyList<InFlightDomesticTask> InFlight => inFlight;
        public IReadOnlyList<DomesticRouteRecoveryRecord> RouteRecoveries => routeRecoveries;

        /// <summary>
        /// W2 (Obj 45 follow-up): the off-map journey authority, handed in
        /// by the owner — SimulationDrivers.DriveDomesticLaborDay passes its
        /// OffMapJourneys (the same predicate the F3 business-task guard
        /// reads). The driver never constructs or finds the authority
        /// itself. Null = unavailable: the driver logs once per day and
        /// proceeds as before (fail-open with a loud log — never fail-closed
        /// into dropping domestic work for everyone).
        /// </summary>
        public OffMapJourneyAuthority OffMapJourneys { get; set; }

        // W2: day-keyed off-map logging state. One skip line per day per
        // person; one unavailable-authority line per day.
        private int offMapSkipLoggedDay = -1;
        private readonly HashSet<int> offMapSkipLoggedPersons = new HashSet<int>();
        private int offMapUnavailableLoggedDay = -1;

        /// <summary>
        /// Day boundary: plans need-driven domestic tasks and re-drives tasks a
        /// previous start-failure orphaned (requeued by ReconcileStalled).
        /// Never invents persons, time, or budget.
        /// </summary>
        public void DriveDay(
            DomesticDayInputs inputs,
            int dayIndex,
            TaskAuthority tasks,
            WorkTimeBudgetStore budgets,
            PersonScheduleTracker schedules,
            DomesticLaborService domestic,
            IDueEventScheduler dueEvents,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (inputs == null || tasks == null || budgets == null || schedules == null || domestic == null)
            {
                diag.Add("DomesticLaborDriver: missing inputs/authorities — no domestic work planned.");
                return;
            }

            // B3: remember today's per-household context (transient, never
            // persisted). The fuel trip reads the household and the stove
            // registry; the cooking settlement reads the inventory and
            // the meal authority. Repopulated every DriveDay.
            if (inputs.Household != null) householdById[inputs.HouseholdId] = inputs.Household;
            if (inputs.Stoves != null) stovesByHousehold[inputs.HouseholdId] = inputs.Stoves;
            if (inputs.Inventory != null) inventoryByHousehold[inputs.HouseholdId] = inputs.Inventory;
            if (inputs.MealService != null) mealServiceByHousehold[inputs.HouseholdId] = inputs.MealService;
            if (inputs.FuelForecastExteriorMeanCelsius.HasValue) fuelForecastTemperatureByHousehold[inputs.HouseholdId] = inputs.FuelForecastExteriorMeanCelsius.Value;

            // Pass IX (A2-04): yesterday's unstarted plans are stale — prune
            // before planning today so the persisted list cannot grow
            // monotonically (PASS8-009 pattern).
            PruneStalePending(dayIndex, diag);

            // First: requeue anything a start-failure orphaned, then adopt the
            // requeued domestic tasks for this household as today's pending work.
            ReconcileStalled(inputs.HouseholdId, tasks, budgets, schedules, domestic, diag);
            AdoptRequeued(inputs, dayIndex, tasks, budgets, schedules, domestic, diag);

            var wanted = new List<(string definitionId, int startMinute, string why)>();
            if (inputs.MealsAtHomeCount > 0)
            {
                wanted.Add((DomesticTaskCatalog.MealPrepId, 990, inputs.MealsAtHomeCount + " home meal(s) today"));
            }

            if (inputs.FuelHandlingNeeded)
            {
                // B3: no stove = no fuel-handling task. There is nowhere
                // to carry fuel to, so the need stays visible as a
                // procurement need (the household needs a stove and/or
                // fuel), not a handling task. The guard reads the same
                // derived stove list the meal-prep gate reads. Without a
                // registry the caller cannot know, so the task is planned
                // as before (legacy; the pickup then refuses loudly).
                if (inputs.Stoves != null && inputs.Stoves.Stoves.Count == 0)
                {
                    diag.Add("DomesticLaborDriver: household " + inputs.HouseholdId +
                        " needs fuel handling but has no installed stove — no handling task (procurement need, not a trip).");
                }
                else
                {
                    wanted.Add((DomesticTaskCatalog.FuelHandlingId, 540, "household fuel-handling need"));
                }
            }

            if (inputs.HasAnimals)
            {
                // Lane 9c (M-R4): the resolver has no animal-care branch
                // and no pen/pasture destination exists, so an unscheduled
                // task could never arrive — start, fail arrival, interrupt,
                // requeue, repeat. Gate scheduling on a destination
                // existing; the need stays visible as a diagnostic, the
                // same shape as the fuel-handling stove guard above.
                if (DomesticAnimalCareGate.ShouldScheduleAnimalCare(inputs.AnimalCareDestinationAvailable, out string animalCareRefusal))
                {
                    wanted.Add((DomesticTaskCatalog.AnimalCareId, 480, "household animals need tending"));
                }
                else
                {
                    diag.Add("DomesticLaborDriver: household " + inputs.HouseholdId +
                        " owns animals but " + animalCareRefusal);
                }
            }

            if (inputs.CleaningNeeded)
            {
                wanted.Add((DomesticTaskCatalog.ChoresId, 660, "household cleaning need"));
            }

            foreach (var want in wanted)
            {
                TaskDefinition definition = tasks.GetDefinition(want.definitionId);
                if (definition == null)
                {
                    diag.Add("DomesticLaborDriver: definition '" + want.definitionId + "' not registered — skipped.");
                    continue;
                }

                if (HasOpenOrPlannedDemand(inputs.HouseholdId, want.definitionId, dayIndex, tasks, domestic))
                {
                    continue;
                }

                string reason;
                int selectedStartMinute;
                int personId = FindFreeMemberForTaskWindow(inputs, want.definitionId, dayIndex,
                    want.startMinute, tasks, schedules, budgets, domestic, diag, out selectedStartMinute, out reason);
                if (personId < 0)
                {
                    diag.Add("DomesticLaborDriver: no free member for '" + want.definitionId +
                        "' (" + want.why + "): " + reason + " — task deferred, need stays visible.");
                    continue;
                }

                // B2 (Agent B, expanded campaign 2026-11-16): the stove
                // gate for meal-prep. A ready stove cooks on the stove;
                // anything else falls back to cold food from stores —
                // the task is still planned and still credits family
                // labour (DEF-015: meal-prep serves no meal and changes
                // no meal count). The stove never blocks eating, and the
                // fallback is recorded loudly on the task note, never
                // silently. Without a stove registry the mode stays
                // Unspecified (legacy behaviour, Agent G's wiring).
                HouseholdStoveMealPrepMode mealPrepMode = HouseholdStoveMealPrepMode.Unspecified;
                string taskNote = want.why;
                if (string.Equals(want.definitionId, DomesticTaskCatalog.MealPrepId, StringComparison.Ordinal)
                    && inputs.Stoves != null)
                {
                    string stoveReason;
                    mealPrepMode = inputs.Stoves.CheckMealPrep(dayIndex, inputs.FuelWoodReserveUnits, diag, out stoveReason);
                    taskNote = mealPrepMode == HouseholdStoveMealPrepMode.StoveCooking
                        ? want.why + " — stove cooking"
                        : want.why + " — cold food, no stove: " + stoveReason;
                }

                pending.Add(new PendingDomesticTask
                {
                    HouseholdId = inputs.HouseholdId,
                    PersonId = personId,
                    DefinitionId = want.definitionId,
                    DayIndex = dayIndex,
                    StartMinuteOfDay = selectedStartMinute,
                    Status = "Pending",
                    Note = taskNote,
                    IsWindowUnavailable = inputs.IsWindowUnavailable,
                    MealPrepMode = mealPrepMode,
                });
                diag.Add("DomesticLaborDriver: planned '" + definition.DisplayName + "' for P" + personId +
                    " at minute " + want.startMinute + " (" + want.why + ").");
            }
        }

        /// <summary>
        /// Tick advancement: starts pending tasks whose window has arrived and
        /// registers their completion due-events. Does NOT pop the scheduler —
        /// the caller pops once and dispatches (single pop site per tick).
        /// Callers pop due events before advancing (see
        /// SimulationDrivers.OnShortTick, which pops before this tick call), so
        /// a completion due before a start window is handled first.
        /// </summary>
        public void AdvanceTick(
            float currentGameSeconds,
            TaskAuthority tasks,
            WorkTimeBudgetStore budgets,
            PersonScheduleTracker schedules,
            DomesticLaborService domestic,
            IDueEventScheduler dueEvents,
            List<string> diag,
            Func<InFlightDomesticTask, float, DomesticActivityMovementResult> physicalActivity = null)
        {
            diag = diag ?? diagnostics;
            if (tasks == null || budgets == null || schedules == null || domestic == null || dueEvents == null)
            {
                return;
            }

            int dayIndex = (int)Math.Floor(currentGameSeconds / 86400f);
            int minuteOfDay = (int)Math.Floor((currentGameSeconds - dayIndex * 86400f) / 60f);

            // Pass IX (A2-04): drop stale/terminal entries first — a pending
            // entry whose day has passed can never start, and Started/Failed
            // entries are history the in-flight list and diagnostics already
            // carry. Without this the persisted list grows without bound.
            PruneStalePending(dayIndex, diag);

            foreach (PendingDomesticTask item in pending)
            {
                if (item == null || !string.Equals(item.Status, "Pending", StringComparison.Ordinal))
                {
                    continue;
                }

                if (item.DayIndex != dayIndex || minuteOfDay < item.StartMinuteOfDay)
                {
                    continue;
                }

                // W2: never START a domestic task for an off-map person — a
                // task planned while home must not start after a mid-day
                // departure. The entry stays Pending (pruned stale the next
                // day); the need re-plans on return.
                if (IsOffMapPerson(item.PersonId, dayIndex, diag))
                {
                    continue;
                }

                var callDiag = new List<string>();
                string error = item.ExistingTaskId.IsValid
                    ? domestic.ResumeDomesticWork(item.ExistingTaskId, item.HouseholdId, item.PersonId,
                        dayIndex, item.StartMinuteOfDay, tasks, budgets, schedules, callDiag)
                    : domestic.ScheduleDomesticWork(item.HouseholdId, item.PersonId, item.DefinitionId,
                        dayIndex, item.StartMinuteOfDay, tasks, budgets, schedules, callDiag);
                foreach (string d in callDiag)
                {
                    diag.Add("DomesticLaborDriver: " + d);
                }

                if (error != null)
                {
                    if (IsLateReservationConflict(error)
                        && TryFindNextWindowForPendingPerson(item, minuteOfDay, dayIndex,
                            tasks, budgets, schedules, out int retryMinute))
                    {
                        item.StartMinuteOfDay = retryMinute;
                        item.Note = error;
                        diag.Add("DomesticLaborDriver: rescheduled '" + item.DefinitionId + "' for P" +
                            item.PersonId + " at minute " + retryMinute +
                            " after a hard reservation appeared after planning.");
                        continue;
                    }

                    item.Status = "Failed";
                    item.Note = error;
                    diag.Add("DomesticLaborDriver: scheduling failed for '" + item.DefinitionId +
                        "' (P" + item.PersonId + "): " + error);
                    continue;
                }

                item.Status = "Started";
                // Find the assignment the service just created to learn the task id.
                DomesticAssignment assignment = FindOpenAssignment(domestic, tasks, item.HouseholdId,
                    item.PersonId, item.DefinitionId, inFlight);
                if (assignment == null)
                {
                    diag.Add("DomesticLaborDriver: INCONSISTENCY — scheduled '" + item.DefinitionId +
                        "' but no open assignment tracked; completion will be reconciled, not lost.");
                    continue;
                }

                WorkTask task = tasks.FindTask(assignment.TaskId);
                int plannedMinutes = task != null ? task.PlannedMinutes : 30;
                string taskIdString = assignment.TaskId.ToString();
                string dueEventId = string.Empty;
                bool requiresPhysicalArrival = physicalActivity != null;
                if (!requiresPhysicalArrival)
                    dueEvents.Schedule(DueEventKind.TaskCompletionDue,
                        currentGameSeconds + plannedMinutes * 60f, taskIdString, out dueEventId);
                inFlight.Add(new InFlightDomesticTask
                {
                    TaskId = assignment.TaskId,
                    TaskIdString = taskIdString,
                    HouseholdId = item.HouseholdId,
                    PersonId = item.PersonId,
                    DefinitionId = item.DefinitionId,
                    DayIndex = dayIndex,
                    DueEventId = dueEventId ?? string.Empty,
                    RequiresPhysicalArrival = requiresPhysicalArrival,
                    // B3: the meal-prep mode travels with the flight so
                    // the completion settlement knows how the task cooks.
                    MealPrepMode = item.MealPrepMode,
                });
                diag.Add("DomesticLaborDriver: P" + item.PersonId + " started '" + item.DefinitionId +
                    "' (" + plannedMinutes + " min)" + (requiresPhysicalArrival
                        ? "; waiting for a real activity arrival before work time starts."
                        : "; completion due at +" + plannedMinutes + " min."));
            }

            if (physicalActivity != null)
                AdvancePhysicalActivities(currentGameSeconds, dayIndex, tasks, budgets, schedules, domestic,
                    dueEvents, physicalActivity, diag);

            // Terminal entries (Started/Failed) have done their job in this
            // list — the in-flight record tracks started work to completion.
            pending.RemoveAll(item => item == null
                || !string.Equals(item.Status, "Pending", StringComparison.Ordinal));
        }

        private void AdvancePhysicalActivities(float currentGameSeconds, int dayIndex, TaskAuthority tasks,
            WorkTimeBudgetStore budgets, PersonScheduleTracker schedules, DomesticLaborService domestic,
            IDueEventScheduler dueEvents,
            Func<InFlightDomesticTask, float, DomesticActivityMovementResult> physicalActivity,
            List<string> diag)
        {
            for (int i = inFlight.Count - 1; i >= 0; i--)
            {
                InFlightDomesticTask flight = inFlight[i];
                if (flight == null || !flight.RequiresPhysicalArrival || !string.IsNullOrEmpty(flight.DueEventId)) continue;
                DomesticActivityMovementResult result = physicalActivity(flight, currentGameSeconds);
                if (result == DomesticActivityMovementResult.Traveling) continue;
                if (result == DomesticActivityMovementResult.Unsupported)
                {
                    // B3: a failed trip releases any in-transit fuel hold
                    // first — the reserve was never debited, so the
                    // release restores nothing, but the hold must not
                    // survive the deferred task.
                    ReleaseFuelHold(flight, diag);
                    if (tasks.InterruptTask(flight.TaskId, budgets, out string rejection))
                    {
                        RecordRouteFailure(flight, dayIndex, flight.ActivityLocationId);
                        dueEvents.Cancel(flight.DueEventId);
                        ReleaseAssignmentReservation(domestic, schedules, flight);
                        inFlight.RemoveAt(i);
                        diag.Add("DomesticLaborDriver: deferred '" + flight.DefinitionId + "' for P" + flight.PersonId
                            + "; no valid residential activity location or route ('" + flight.ActivityLocationId
                            + "'). Task was requeued without completion or family-labor credit.");
                    }
                    else diag.Add("DomesticLaborDriver: could not defer '" + flight.DefinitionId + "' for P"
                        + flight.PersonId + ": " + rejection + ".");
                    continue;
                }

                // Clear the retry history only after the actor actually
                // reaches the authored activity point.
                ClearRouteRecovery(flight.TaskId);

                // B3: a fuel-handling arrival is a leg arrival, not a work
                // start. Leg 0 is the firewood pile (pick up the trip
                // cargo); leg 1 is the stove (deliver the carried fuel
                // into the store). Only a delivered trip schedules its
                // due event — the handling work runs after the stove is
                // fed. A failed leg interrupts the task and removes the
                // flight, with no completion and no labour credit.
                if (string.Equals(flight.DefinitionId, DomesticTaskCatalog.FuelHandlingId, StringComparison.Ordinal))
                {
                    if (flight.FuelHandlingLeg <= 0)
                    {
                        string pickupRefusal = TryPickupFuel(flight, diag);
                        if (pickupRefusal != null)
                        {
                            ReleaseFuelHold(flight, diag);
                            if (tasks.InterruptTask(flight.TaskId, budgets, out string pickupRejection))
                            {
                                dueEvents.Cancel(flight.DueEventId);
                                ReleaseAssignmentReservation(domestic, schedules, flight);
                                inFlight.RemoveAt(i);
                                diag.Add("DomesticLaborDriver: fuel trip for household " + flight.HouseholdId +
                                    " failed at the firewood pile — " + pickupRefusal +
                                    " Task requeued without completion or family-labor credit.");
                            }
                            else diag.Add("DomesticLaborDriver: could not defer the fuel trip for P" +
                                flight.PersonId + ": " + pickupRejection + ".");
                            continue;
                        }
                        flight.FuelHandlingLeg = 1;
                        diag.Add("DomesticLaborDriver: P" + flight.PersonId + " picked up " +
                            flight.FuelUnitsInTransit + " fuel-wood unit(s) at the firewood pile for stove " +
                            flight.FuelStoveAssetId + "; carrying to the stove.");
                        continue;
                    }

                    string deliveryRefusal = TryDeliverFuel(flight, diag);
                    if (deliveryRefusal != null)
                    {
                        ReleaseFuelHold(flight, diag);
                        if (tasks.InterruptTask(flight.TaskId, budgets, out string deliveryRejection))
                        {
                            dueEvents.Cancel(flight.DueEventId);
                            ReleaseAssignmentReservation(domestic, schedules, flight);
                            inFlight.RemoveAt(i);
                            diag.Add("DomesticLaborDriver: fuel trip for household " + flight.HouseholdId +
                                " failed at the stove — " + deliveryRefusal +
                                " Task requeued without completion or family-labor credit.");
                        }
                        else diag.Add("DomesticLaborDriver: could not defer the fuel trip for P" +
                            flight.PersonId + ": " + deliveryRejection + ".");
                        continue;
                    }
                    // Delivered: fall through to the due-event scheduling
                    // below — the handling work now runs.
                }

                WorkTask task = tasks.FindTask(flight.TaskId);
                if (task == null || !task.IsOpen)
                {
                    ReleaseAssignmentReservation(domestic, schedules, flight);
                    inFlight.RemoveAt(i);
                    continue;
                }
                float dueAt = currentGameSeconds + Math.Max(1, task.MinutesRemaining) * 60f;
                dueEvents.Schedule(DueEventKind.TaskCompletionDue, dueAt, flight.TaskIdString, out string eventId);
                flight.DueEventId = eventId ?? string.Empty;
                diag.Add("DomesticLaborDriver: P" + flight.PersonId + " reached '" + flight.ActivityLocationId
                    + "' in building " + flight.BuildingId + "; " + task.MinutesRemaining
                    + " minutes of household work now run until " + dueAt + ".");
            }
        }

        private static bool IsLateReservationConflict(string refusal)
        {
            return !string.IsNullOrWhiteSpace(refusal)
                && refusal.IndexOf(" is already ", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool TryFindNextWindowForPendingPerson(PendingDomesticTask item, int currentMinute,
            int dayIndex, TaskAuthority tasks, WorkTimeBudgetStore budgets,
            PersonScheduleTracker schedules, out int startMinute)
        {
            startMinute = -1;
            if (item == null || tasks == null || budgets == null || schedules == null
                || item.DayIndex != dayIndex || item.PersonId < 0) return false;

            EntityId worker = EntityId.For(EntityKind.Person, item.PersonId);
            WorkTask existingTask = item.ExistingTaskId.IsValid ? tasks.FindTask(item.ExistingTaskId) : null;
            int duration = existingTask != null
                ? tasks.EstimateRemainingMinutesForWorker(item.ExistingTaskId, worker)
                : tasks.EstimateMinutesForWorker(item.DefinitionId, worker);
            if (duration <= 0 || budgets.GetOrCreate(worker).MinutesRemaining < duration) return false;

            // Reuse DriveDay's deterministic quarter-hour opportunities when a
            // new hard reservation lands after this activity was planned.
            int firstMinute = Math.Max(currentMinute, item.StartMinuteOfDay);
            firstMinute = ((firstMinute + 14) / 15) * 15;
            for (int offset = 0; offset <= 180; offset += 15)
            {
                int candidate = firstMinute + offset;
                if (candidate > 1439) break;
                if (item.IsWindowUnavailable != null
                    && item.IsWindowUnavailable(item.PersonId, candidate, duration)) continue;

                bool overlapsHardReservation = false;
                foreach (PersonActivityReservation reservation in schedules.Overlapping(
                    item.PersonId, dayIndex, candidate, duration))
                {
                    if (reservation.Activity != PersonActivityKind.Resting)
                    {
                        overlapsHardReservation = true;
                        break;
                    }
                }
                if (overlapsHardReservation || HasOtherPendingWindowOverlap(item, candidate, duration, tasks))
                    continue;

                startMinute = candidate;
                return true;
            }

            return false;
        }

        private bool HasOtherPendingWindowOverlap(PendingDomesticTask current, int startMinute,
            int durationMinutes, TaskAuthority tasks)
        {
            int endMinute = startMinute + durationMinutes;
            foreach (PendingDomesticTask other in pending)
            {
                if (other == null || ReferenceEquals(other, current) || other.HouseholdId != current.HouseholdId
                    || other.PersonId != current.PersonId || other.DayIndex != current.DayIndex
                    || !string.Equals(other.Status, "Pending", StringComparison.Ordinal)) continue;

                WorkTask existing = other.ExistingTaskId.IsValid ? tasks.FindTask(other.ExistingTaskId) : null;
                int otherDuration = existing != null
                    ? tasks.EstimateRemainingMinutesForWorker(other.ExistingTaskId,
                        EntityId.For(EntityKind.Person, current.PersonId))
                    : tasks.EstimateMinutesForWorker(other.DefinitionId,
                        EntityId.For(EntityKind.Person, current.PersonId));
                if (startMinute < other.StartMinuteOfDay + otherDuration
                    && other.StartMinuteOfDay < endMinute) return true;
            }

            return false;
        }

        /// <summary>
        /// Pass IX (A2-04): removes pending entries that can no longer start:
        /// nulls, terminal (Started/Failed) entries, and Pending entries
        /// planned for a day before <paramref name="currentDayIndex"/> (their
        /// window has passed; the need re-plans on the next DriveDay).
        /// Returns the number pruned.
        /// </summary>
        public int PruneStalePending(int currentDayIndex, List<string> diag = null)
        {
            int before = pending.Count;
            pending.RemoveAll(item => item == null
                || !string.Equals(item.Status, "Pending", StringComparison.Ordinal)
                || item.DayIndex < currentDayIndex);
            int pruned = before - pending.Count;
            if (pruned > 0 && diag != null)
            {
                diag.Add($"DomesticLaborDriver: pruned {pruned} stale pending entr(ies) before day {currentDayIndex}.");
            }

            return pruned;
        }

        /// <summary>
        /// Pass IX (A2-04): true when a TaskCompletionDue payload names one of
        /// this driver's in-flight tasks — the production due-event
        /// dispatcher uses it to route domestic completions here instead of
        /// the generic task completion path.
        /// </summary>
        public bool HasInFlightTask(string taskIdString)
        {
            if (string.IsNullOrWhiteSpace(taskIdString))
            {
                return false;
            }

            foreach (InFlightDomesticTask flight in inFlight)
            {
                if (flight != null && string.Equals(flight.TaskIdString, taskIdString, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The domestic driver owns the fixed completion event for each task it
        /// starts. The general simulation backstop must not replace that event
        /// on every clock tick, because domestic work is credited when its
        /// elapsed-time event fires rather than incrementally by the business
        /// work loop.
        /// </summary>
        public bool RequiresGenericCompletionBackstop(WorkTask task)
        {
            return task != null
                && task.Status == TaskStatus.InProgress
                && task.HasAssignee
                && !HasInFlightTask(task.TaskId.ToString());
        }

        /// <summary>
        /// Handles one due event for a domestic task: records the worked minutes
        /// and completes through the service (family-labor credit, zero wages).
        /// </summary>
        public void HandleDueEvent(
            DueEvent ev,
            TaskAuthority tasks,
            WorkTimeBudgetStore budgets,
            PersonScheduleTracker schedules,
            DomesticLaborService domestic,
            FamilyLaborLedger familyLabor,
            IDueEventScheduler dueEvents,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (ev == null || ev.Kind != DueEventKind.TaskCompletionDue)
            {
                return;
            }

            InFlightDomesticTask flight = null;
            foreach (InFlightDomesticTask f in inFlight)
            {
                if (f != null && string.Equals(f.TaskIdString, ev.PayloadRef, StringComparison.Ordinal))
                {
                    flight = f;
                    break;
                }
            }

            if (flight == null)
            {
                return; // not one of ours
            }

            inFlight.Remove(flight);

            WorkTask task = tasks != null ? tasks.FindTask(flight.TaskId) : null;
            if (task == null || !task.IsOpen)
            {
                // A task may be cancelled or completed by an owning authority
                // while its due event is still in the scheduler. Retire the
                // matching assignment without adding family-labor credit.
                diag.Add("DomesticLaborDriver: task " + ev.PayloadRef +
                    " reached its due event after the task had already closed" +
                    (task != null ? " (" + task.Status + ")" : " (missing task)") +
                    "; releasing its reservation without duplicate work credit.");
                ReleaseAssignmentReservation(domestic, schedules, flight);
                return;
            }

            int remaining = Math.Max(1, task.MinutesRemaining);
            // CompleteDomesticWork records the work itself (RecordWork tops up
            // exactly the remaining minutes and auto-completes); recording here
            // first would double-count minutes and budget.
            string completeError = domestic.CompleteDomesticWork(
                task.TaskId, remaining, dayIndex,
                tasks, budgets, schedules, familyLabor, diag);
            if (completeError != null)
            {
                diag.Add("DomesticLaborDriver: completion failed for " + ev.PayloadRef + ": " + completeError);
            }
            else
            {
                diag.Add("DomesticLaborDriver: P" + flight.PersonId + " completed '" +
                    flight.DefinitionId + "' (" + remaining + " min worked, family labor, no wages).");
                // Campaign C-24 (DEF-010): a completed Chores task cleans the
                // household — the cleanliness accumulation resets and the need
                // is re-earned by further use. Every domestic completion flows
                // through here (ScheduleDomesticWork is called only by this
                // driver), so no completion path can skip the reset.
                if (string.Equals(flight.DefinitionId, DomesticTaskCatalog.ChoresId, StringComparison.Ordinal))
                {
                    cleanliness.ResetAfterCleaning(flight.HouseholdId, dayIndex);
                }
                // B3: a completed meal-prep task settles its cooking —
                // StoveCooking burns stove fuel and produces one loaf
                // through the meal authority; anything else produces
                // nothing. The settlement never serves a meal and never
                // changes a meal count (DEF-015). The labour credit
                // above stands whatever the settlement decides — the
                // time was genuinely spent.
                if (string.Equals(flight.DefinitionId, DomesticTaskCatalog.MealPrepId, StringComparison.Ordinal))
                {
                    SettleCooking(flight, dayIndex, remaining, diag);
                }
            }
        }

        /// <summary>
        /// B3: the fuel a trip carries — the next day's heating forecast
        /// (the stove store is the buffer for the next burn) plus one
        /// unit for today's cooking session when a meal-prep task is
        /// planned, capped by the fuel-wood reserve on hand. The store
        /// is fed today for tomorrow's burn and today's cooking: the
        /// burn runs at the day boundary before the trip, so the trip
        /// refills the store the burn will draw from next. The cooking
        /// unit is HouseholdStoveRegistry.FuelUnitsPerMealPrepSession
        /// (1). Returns 0 when there is no household, no registry, no
        /// burn forecast, and no meal-prep — or when the reserve is
        /// empty.
        /// </summary>
        /// Heating lane (Nov 20 2026): the forecast day's exterior mean
        /// temperature may be passed in; otherwise the day's temperature is
        /// resolved with the default scenario seed. The forecast and the
        /// burn use the same temperature-driven formula, so the trip cargo
        /// matches the burn the stove store will feed next.
        /// </summary>
        public static int ComputeFuelTripCargo(HouseholdState household, int dayIndex, HouseholdStoveRegistry stoves, bool hasMealPrepToday, float? exteriorMeanCelsius = null)
        {
            if (household == null || stoves == null) return 0;
            int heating = exteriorMeanCelsius.HasValue
                ? HouseholdHeatingEvaluator.CalculateDailyFuelUseUnits(household, exteriorMeanCelsius.Value)
                : HouseholdHeatingEvaluator.CalculateDailyFuelUseUnits(household, dayIndex + 1);
            int cooking = hasMealPrepToday ? HouseholdStoveRegistry.FuelUnitsPerMealPrepSession : 0;
            int wanted = heating + cooking;
            if (wanted <= 0) return 0;
            HouseholdReserveState reserve = household.GetReserve(HouseholdReserveCatalog.FuelWoodCategoryId);
            int onHand = reserve != null ? Math.Max(0, reserve.currentUnits) : 0;
            return Math.Min(wanted, onHand);
        }

        /// <summary>B3: true when a meal-prep task is pending or in flight for the household on the given day.</summary>
        private bool HasMealPrepToday(int householdId, int dayIndex)
        {
            foreach (PendingDomesticTask item in pending)
            {
                if (item != null && item.HouseholdId == householdId && item.DayIndex == dayIndex
                    && string.Equals(item.DefinitionId, DomesticTaskCatalog.MealPrepId, StringComparison.Ordinal)
                    && string.Equals(item.Status, "Pending", StringComparison.Ordinal)) return true;
            }
            foreach (InFlightDomesticTask item in inFlight)
            {
                if (item != null && item.HouseholdId == householdId && item.DayIndex == dayIndex
                    && string.Equals(item.DefinitionId, DomesticTaskCatalog.MealPrepId, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>
        /// B3: releases a fuel trip's in-transit hold. The reserve was
        /// never debited for the hold, so releasing restores nothing —
        /// it clears the record so a deferred/failed trip cannot later
        /// deliver fuel that was never reserved. Loud in diagnostics
        /// when a hold existed.
        /// </summary>
        private void ReleaseFuelHold(InFlightDomesticTask flight, List<string> diag)
        {
            if (flight == null || flight.FuelUnitsInTransit <= 0) return;
            if (diag != null)
            {
                diag.Add("DomesticLaborDriver: released the in-transit hold of " + flight.FuelUnitsInTransit +
                    " fuel-wood unit(s) for household " + flight.HouseholdId +
                    " (trip failed/deferred — the reserve was never debited, nothing to return).");
            }
            flight.FuelUnitsInTransit = 0;
            flight.FuelStoveAssetId = string.Empty;
        }

        /// <summary>
        /// B3: the firewood-pile pickup (leg 0 arrival). Computes the
        /// trip cargo and records it as the in-transit hold on the
        /// flight. The reserve is NOT debited here — it is debited only
        /// on stove receipt (TryDeliverFuel, via TryFeedStove).
        /// Refuses loudly with nothing recorded when there is no
        /// household context, no stove registry, no installed stove,
        /// or no fuel to carry. Returns null on success.
        /// </summary>
        private string TryPickupFuel(InFlightDomesticTask flight, List<string> diag)
        {
            if (flight == null) return "no flight to pick up fuel for.";
            if (!householdById.TryGetValue(flight.HouseholdId, out HouseholdState household) || household == null)
                return "no household state (Agent G wiring).";
            if (!stovesByHousehold.TryGetValue(flight.HouseholdId, out HouseholdStoveRegistry stoves) || stoves == null)
                return "no stove registry (Agent G wiring).";
            if (stoves.Stoves.Count == 0)
                return "no installed stove.";
            HouseholdStove stove = stoves.Stoves[0];
            float? forecastTemperature = fuelForecastTemperatureByHousehold.TryGetValue(flight.HouseholdId, out float storedTemperature)
                ? storedTemperature
                : (float?)null;
            int cargo = ComputeFuelTripCargo(household, flight.DayIndex, stoves, HasMealPrepToday(flight.HouseholdId, flight.DayIndex), forecastTemperature);
            if (cargo <= 0)
                return "no fuel to carry (reserve empty or no burn forecast).";
            flight.FuelUnitsInTransit = cargo;
            flight.FuelStoveAssetId = stove.StoveAssetId;
            return null;
        }

        /// <summary>
        /// B3: the stove delivery (leg 1 arrival). Moves the carried
        /// fuel from the reserve into the stove store via TryFeedStove
        /// — the reserve is debited here, exactly what the store is
        /// credited, so the hold converts into custody. Refuses loudly
        /// with the hold released when the reserve is short (a burn may
        /// have taken the reserved units) or the stove is gone; the
        /// trip then fails without completion or credit. Returns null
        /// on success.
        /// </summary>
        private string TryDeliverFuel(InFlightDomesticTask flight, List<string> diag)
        {
            if (flight == null) return "no flight to deliver fuel for.";
            if (flight.FuelUnitsInTransit <= 0 || string.IsNullOrWhiteSpace(flight.FuelStoveAssetId))
                return "nothing is being carried.";
            if (!householdById.TryGetValue(flight.HouseholdId, out HouseholdState household) || household == null)
                return "no household state (Agent G wiring).";
            if (!stovesByHousehold.TryGetValue(flight.HouseholdId, out HouseholdStoveRegistry stoves) || stoves == null)
                return "no stove registry (Agent G wiring).";
            int units = flight.FuelUnitsInTransit;
            string stoveAssetId = flight.FuelStoveAssetId;
            string refusal = stoves.TryFeedStove(stoveAssetId, units, household, diag);
            if (refusal != null) return refusal;
            flight.FuelUnitsInTransit = 0;
            flight.FuelStoveAssetId = string.Empty;
            if (diag != null)
            {
                diag.Add("DomesticLaborDriver: P" + flight.PersonId + " fed " + units +
                    " fuel-wood unit(s) into stove " + stoveAssetId + " (household " + flight.HouseholdId + ").");
            }
            return null;
        }

        /// <summary>
        /// B3: the meal-prep cooking settlement, run at the task's
        /// completion. StoveCooking: burns exactly
        /// FuelUnitsPerMealPrepSession fuel from the representative
        /// stove's store (the first installed stove holding at least a
        /// session of fuel) and converts exactly FlourUnitsPerLoaf
        /// flour into LoavesPerMealPrepSession loaf through the meal
        /// authority (PrepareFromRaw, equipment named, labour named) —
        /// the production record is the cooking output. The checks run
        /// before any debit: no fuel in any stove store, or no flour in
        /// the inventory, refuses LOUDLY and produces nothing (no fuel
        /// burned, no ingredients consumed, no record).
        /// ColdFoodFallback and Unspecified produce nothing and refuse
        /// nothing — the household eats cold food from stores. Missing
        /// context (no registry, inventory, meal service, or household)
        /// skips the settlement loudly — never silently, never
        /// consuming. The settlement never serves a meal and never
        /// changes a meal count (DEF-015).
        /// </summary>
        public void SettleCooking(InFlightDomesticTask flight, int dayIndex, int minutesWorked, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (flight == null) return;
            if (flight.MealPrepMode != HouseholdStoveMealPrepMode.StoveCooking) return;
            if (!stovesByHousehold.TryGetValue(flight.HouseholdId, out HouseholdStoveRegistry stoves) || stoves == null)
            {
                diag.Add("DomesticLaborDriver: cooking settlement skipped for household " + flight.HouseholdId +
                    " — no stove registry (Agent G wiring); no fuel burned, nothing produced.");
                return;
            }
            if (!householdById.TryGetValue(flight.HouseholdId, out HouseholdState household) || household == null)
            {
                diag.Add("DomesticLaborDriver: cooking settlement skipped for household " + flight.HouseholdId +
                    " — no household state (Agent G wiring); no fuel burned, nothing produced.");
                return;
            }
            if (!inventoryByHousehold.TryGetValue(flight.HouseholdId, out HouseholdInventory inventory) || inventory == null)
            {
                diag.Add("DomesticLaborDriver: cooking settlement skipped for household " + flight.HouseholdId +
                    " — no inventory (Agent G wiring); no fuel burned, nothing produced.");
                return;
            }
            if (!mealServiceByHousehold.TryGetValue(flight.HouseholdId, out HouseholdMealService mealService) || mealService == null)
            {
                diag.Add("DomesticLaborDriver: cooking settlement skipped for household " + flight.HouseholdId +
                    " — no meal service (Agent G wiring); no fuel burned, nothing produced.");
                return;
            }
            HouseholdStove stove = null;
            foreach (HouseholdStove candidate in stoves.Stoves)
            {
                if (candidate != null && candidate.FuelWoodUnitsOnHand >= HouseholdStoveRegistry.FuelUnitsPerMealPrepSession)
                {
                    stove = candidate;
                    break;
                }
            }
            if (stove == null)
            {
                diag.Add("DomesticLaborDriver: cooking produced nothing for household " + flight.HouseholdId +
                    " — no fuel in any stove store (stove cooking refused loudly).");
                return;
            }
            if (inventory.GetAvailableUnits(HouseholdItemCatalog.FlourId) < FlourUnitsPerLoaf)
            {
                diag.Add("DomesticLaborDriver: cooking produced nothing for household " + flight.HouseholdId +
                    " — no flour in the household inventory (stove cooking refused loudly); no fuel burned.");
                return;
            }
            string fuelRefusal = stoves.TryConsumeStoveFuel(stove.StoveAssetId, HouseholdStoveRegistry.FuelUnitsPerMealPrepSession, diag);
            if (fuelRefusal != null)
            {
                diag.Add("DomesticLaborDriver: cooking produced nothing for household " + flight.HouseholdId +
                    " — " + fuelRefusal);
                return;
            }
            string prepRefusal = mealService.PrepareFromRaw(inventory, HouseholdItemCatalog.FlourId, HouseholdItemCatalog.BreadId,
                FlourUnitsPerLoaf, LoavesPerMealPrepSession,
                "household stove " + stove.StoveAssetId, Math.Max(0, minutesWorked), dayIndex, "P" + flight.PersonId);
            if (prepRefusal != null)
            {
                // The fuel is already burned — this cannot happen after
                // the checks above (the conversion's inputs were
                // verified); log it loudly as an anomaly, never silently.
                diag.Add("DomesticLaborDriver: cooking produced nothing for household " + flight.HouseholdId +
                    " after burning fuel — " + prepRefusal);
                return;
            }
            diag.Add("DomesticLaborDriver: P" + flight.PersonId + " cooked " + LoavesPerMealPrepSession + " loaf(s) from " +
                FlourUnitsPerLoaf + " flour on stove " + stove.StoveAssetId + " (1 fuel unit, household " +
                flight.HouseholdId + ").");
        }

        /// <summary>
        /// Reconciles domestic tasks that have entered an active state without
        /// a live driver flight. A started domestic task must have an in-flight
        /// record that owns its arrival/completion path; otherwise it can hold
        /// a person forever while no movement or work can advance. Requeue the
        /// same task through TaskAuthority (no work credit) and release any
        /// matching domestic schedule reservation. When interruption is not
        /// permitted, cancel it loudly rather than retain a dead reservation.
        /// </summary>
        public void ReconcileStalled(
            int householdId,
            TaskAuthority tasks,
            WorkTimeBudgetStore budgets,
            PersonScheduleTracker schedules,
            DomesticLaborService domestic,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (tasks == null)
            {
                return;
            }

            EntityId owner = EntityId.For(EntityKind.Household, householdId);
            List<WorkTask> open = tasks.GetOpenTasks(owner);
            foreach (WorkTask task in open)
            {
                if (task == null || (task.Status != TaskStatus.Assigned
                    && task.Status != TaskStatus.InProgress))
                {
                    continue;
                }

                if (!DomesticTaskCatalog.IsDomesticDefinition(task.DefinitionId))
                {
                    continue;
                }

                if (HasInFlightTask(task.TaskId.ToString()))
                {
                    continue;
                }

                string rejection;
                if (tasks.InterruptTask(task.TaskId, budgets, out rejection))
                {
                    ReleaseOrphanedAssignmentReservation(domestic, schedules, task.TaskId);
                    diag.Add("DomesticLaborDriver: requeued orphaned domestic task " + task.TaskId +
                        " ('" + task.DefinitionId + "', previously " + task.Status +
                        ") — no in-flight arrival/completion owner; no work was credited.");
                }
                else
                {
                    // Not interruptible: cancel loudly rather than leak.
                    if (tasks.CancelTask(task.TaskId, budgets))
                    {
                        ReleaseOrphanedAssignmentReservation(domestic, schedules, task.TaskId);
                        diag.Add("DomesticLaborDriver: cancelled non-interruptible orphaned domestic task " +
                            task.TaskId + " (" + rejection + ").");
                    }
                    else
                    {
                        diag.Add("DomesticLaborDriver: UNRESOLVED orphaned domestic task " + task.TaskId +
                            ": " + rejection);
                    }
                }
            }
        }

        private static void ReleaseOrphanedAssignmentReservation(DomesticLaborService domestic,
            PersonScheduleTracker schedules, EntityId taskId)
        {
            if (domestic == null || schedules == null) return;
            foreach (DomesticAssignment assignment in domestic.Assignments)
            {
                if (assignment == null || assignment.Completed || assignment.TaskId != taskId) continue;
                schedules.Release(assignment.ReservationId);
                assignment.Completed = true;
                return;
            }
        }

        // ---------- internals ----------

        private void AdoptRequeued(DomesticDayInputs inputs, int dayIndex, TaskAuthority tasks,
            WorkTimeBudgetStore budgets, PersonScheduleTracker schedules,
            DomesticLaborService domestic, List<string> diag)
        {
            int householdId = inputs.HouseholdId;
            EntityId owner = EntityId.For(EntityKind.Household, householdId);
            List<WorkTask> open = tasks.GetOpenTasks(owner);
            PruneRouteRecoveries(tasks);
            var adoptedDefinitions = new HashSet<string>(StringComparer.Ordinal);
            foreach (WorkTask task in open)
            {
                if (task == null || task.Status != TaskStatus.Queued)
                {
                    continue;
                }

                if (!DomesticTaskCatalog.IsDomesticDefinition(task.DefinitionId))
                {
                    continue;
                }

                DomesticRouteRecoveryRecord routeRecovery = FindRouteRecovery(task.TaskId);
                // Fix #1 (A4 fallback): a missing Firewood point parks the
                // task only when there is no stove to fall back to. With a
                // stove the trip runs as a single leg, so the task is
                // adopted like any other and the resolver supplies the
                // stove id.
                if (string.Equals(task.DefinitionId, DomesticTaskCatalog.FuelHandlingId, StringComparison.Ordinal)
                    && inputs.FirewoodActivityLocationAvailable == false
                    && inputs.FuelHandlingStoveFallbackAvailable != true)
                {
                    if (routeRecovery == null)
                    {
                        routeRecovery = new DomesticRouteRecoveryRecord
                        {
                            TaskId = task.TaskId,
                            WaitForCapabilityChange = true,
                            RetryAfterDay = int.MaxValue,
                            ActivityLocationId = MissingFirewoodStorageLocationId,
                            LastFailureReason = MissingFirewoodStorageLocationId,
                        };
                        routeRecoveries.Add(routeRecovery);
                        diag.Add("DomesticLaborDriver: retained fuel-handling task " + task.TaskId
                            + " in a capability-wait state; no worker or schedule reservation is held because the home has no authored Firewood location.");
                    }
                    else
                    {
                        routeRecovery.WaitForCapabilityChange = true;
                        routeRecovery.RetryAfterDay = int.MaxValue;
                    }
                    continue;
                }

                if (routeRecovery != null && routeRecovery.WaitForCapabilityChange)
                {
                    // Fix #1 (A4 fallback): a retained fuel task resumes
                    // when Firewood is authored OR when the stove fallback
                    // becomes available; only a home with neither stays
                    // capability-waited.
                    bool firewoodNow = inputs.FirewoodActivityLocationAvailable == true;
                    bool fallbackNow = inputs.FuelHandlingStoveFallbackAvailable == true;
                    if (!firewoodNow && !fallbackNow)
                        continue;

                    routeRecovery.WaitForCapabilityChange = false;
                    routeRecovery.RetryAfterDay = dayIndex;
                    diag.Add(firewoodNow
                        ? "DomesticLaborDriver: authored Firewood capability is now available; resuming retained fuel-handling task "
                            + task.TaskId + " without creating a duplicate household need."
                        : "DomesticLaborDriver: fuel-handling stove fallback is available; resuming retained fuel-handling task "
                            + task.TaskId + " as a single-leg stove trip (fix #1, A4) without creating a duplicate household need.");
                }

                if (routeRecovery != null && dayIndex < routeRecovery.RetryAfterDay)
                {
                    continue;
                }

                if (!adoptedDefinitions.Add(task.DefinitionId)
                    || HasOpenOrPlannedDemand(householdId, task.DefinitionId, dayIndex,
                        tasks, null, task.TaskId, ignoreOtherOpenTasks: true)) continue;
                int startMinute = StartMinuteFor(task.DefinitionId);
                string reason;
                int selectedStartMinute;
                int personId = FindFreeMemberForTaskWindow(inputs, task.DefinitionId, dayIndex,
                    startMinute, tasks, schedules, budgets, domestic, diag, out selectedStartMinute, out reason);
                if (personId < 0)
                {
                    diag.Add("DomesticLaborDriver: retained requeued task " + task.TaskId +
                        " but no member can yet cover its estimated duration: " + reason);
                    continue;
                }
                pending.Add(new PendingDomesticTask
                {
                    HouseholdId = householdId,
                    PersonId = personId,
                    ExistingTaskId = task.TaskId,
                    DefinitionId = task.DefinitionId,
                    DayIndex = dayIndex,
                    StartMinuteOfDay = selectedStartMinute,
                    Status = "Pending",
                    Note = "resumed existing task identity after a start refusal",
                    IsWindowUnavailable = inputs.IsWindowUnavailable,
                });
                if (routeRecovery != null)
                {
                    diag.Add("DomesticLaborDriver: retrying task " + task.TaskId + " after "
                        + routeRecovery.FailureCount + " failed physical route attempt(s) to '"
                        + routeRecovery.ActivityLocationId + "'; last blocker: "
                        + routeRecovery.LastFailureReason + ".");
                }
                diag.Add("DomesticLaborDriver: re-adopted existing task " + task.TaskId +
                    " ('" + task.DefinitionId + "') for household " + householdId + ".");
            }
        }

        private void RecordRouteFailure(InFlightDomesticTask flight, int dayIndex, string reason)
        {
            if (flight == null || !flight.TaskId.IsValid) return;
            DomesticRouteRecoveryRecord record = FindRouteRecovery(flight.TaskId);
            if (record == null)
            {
                record = new DomesticRouteRecoveryRecord { TaskId = flight.TaskId };
                routeRecoveries.Add(record);
            }
            record.FailureCount = Math.Max(0, record.FailureCount) + 1;
            if (string.Equals(flight.DefinitionId, DomesticTaskCatalog.FuelHandlingId, StringComparison.Ordinal)
                && string.Equals(flight.ActivityLocationId, MissingFirewoodStorageLocationId, StringComparison.Ordinal))
            {
                record.WaitForCapabilityChange = true;
                record.RetryAfterDay = int.MaxValue;
                record.ActivityLocationId = MissingFirewoodStorageLocationId;
                record.LastFailureReason = MissingFirewoodStorageLocationId;
                return;
            }

            // Back off 2, 4, then 7 days. A route can become available after
            // furniture or occupancy changes, so preserve the same task and
            // recheck periodically instead of cancelling its need.
            int exponent = Math.Min(3, Math.Max(1, record.FailureCount));
            record.RetryAfterDay = dayIndex + Math.Min(7, 1 << exponent);
            record.ActivityLocationId = flight.ActivityLocationId ?? string.Empty;
            record.LastFailureReason = reason ?? string.Empty;
        }

        private DomesticRouteRecoveryRecord FindRouteRecovery(EntityId taskId)
        {
            for (int i = 0; i < routeRecoveries.Count; i++)
            {
                DomesticRouteRecoveryRecord record = routeRecoveries[i];
                if (record != null && record.TaskId == taskId) return record;
            }
            return null;
        }

        private void ClearRouteRecovery(EntityId taskId)
        {
            routeRecoveries.RemoveAll(record => record == null || record.TaskId == taskId);
        }

        private void PruneRouteRecoveries(TaskAuthority tasks)
        {
            if (tasks == null) return;
            routeRecoveries.RemoveAll(record =>
            {
                if (record == null || !record.TaskId.IsValid) return true;
                WorkTask task = tasks.FindTask(record.TaskId);
                return task == null || !task.IsOpen;
            });
        }

        private bool HasOpenOrPlannedDemand(int householdId, string definitionId, int dayIndex,
            TaskAuthority tasks, DomesticLaborService domestic, EntityId excludeTaskId = default,
            bool ignoreOtherOpenTasks = false)
        {
            foreach (PendingDomesticTask item in pending)
                if (item != null && item.HouseholdId == householdId && item.DayIndex == dayIndex
                    && string.Equals(item.DefinitionId, definitionId, StringComparison.Ordinal)
                    && string.Equals(item.Status, "Pending", StringComparison.Ordinal)) return true;
            foreach (InFlightDomesticTask item in inFlight)
                if (item != null && item.HouseholdId == householdId && item.DayIndex == dayIndex
                    && string.Equals(item.DefinitionId, definitionId, StringComparison.Ordinal)) return true;
            if (domestic != null)
                foreach (DomesticAssignment item in domestic.Assignments)
                    if (item != null && item.HouseholdId == householdId && item.DayIndex == dayIndex
                        && string.Equals(item.DefinitionId, definitionId, StringComparison.Ordinal)) return true;
            if (tasks != null && !ignoreOtherOpenTasks)
                foreach (WorkTask task in tasks.GetOpenTasks(EntityId.For(EntityKind.Household, householdId)))
                    if (task != null && task.TaskId != excludeTaskId
                        && string.Equals(task.DefinitionId, definitionId, StringComparison.Ordinal)) return true;
            return false;
        }

        private static int StartMinuteFor(string definitionId)
        {
            if (string.Equals(definitionId, DomesticTaskCatalog.MealPrepId, StringComparison.Ordinal)) return 990;
            if (string.Equals(definitionId, DomesticTaskCatalog.FuelHandlingId, StringComparison.Ordinal)) return 540;
            if (string.Equals(definitionId, DomesticTaskCatalog.AnimalCareId, StringComparison.Ordinal)) return 480;
            return 660;
        }

        /// <summary>
        /// W2: the off-map predicate, read through the handed-in authority.
        /// Day-keyed logging: one skip line per day per person; one
        /// unavailable-authority line per day (fail-open with a loud log).
        /// </summary>
        private bool IsOffMapPerson(int personId, int dayIndex, List<string> diag)
        {
            if (OffMapJourneys == null)
            {
                if (offMapUnavailableLoggedDay != dayIndex)
                {
                    offMapUnavailableLoggedDay = dayIndex;
                    diag?.Add("DomesticLaborDriver: off-map journey authority unavailable — proceeding WITHOUT the off-map guard (fail-open); off-map persons may still be assigned domestic work.");
                }

                return false;
            }

            bool offMap = OffMapJourneys.IsOffMapPerson(personId);
            if (!offMap)
            {
                return false;
            }

            if (offMapSkipLoggedDay != dayIndex)
            {
                offMapSkipLoggedDay = dayIndex;
                offMapSkipLoggedPersons.Clear();
            }

            if (offMapSkipLoggedPersons.Add(personId))
            {
                diag?.Add($"DomesticLaborDriver: P{personId} is off-map — domestic tasks skipped today, need stays open for return.");
            }

            return true;
        }

        private int FindFreeMemberForTaskWindow(DomesticDayInputs inputs, string definitionId,
            int dayIndex, int startMinute, TaskAuthority tasks, PersonScheduleTracker schedules,
            WorkTimeBudgetStore budgets, DomesticLaborService domestic,
            List<string> diag, out int selectedStartMinute, out string reason)
        {
            selectedStartMinute = startMinute;
            reason = null;
            if (inputs?.MemberIds == null || tasks == null)
            {
                reason = "No household members or task authority.";
                return -1;
            }
            var rejections = new List<string>();
            List<int> ordered = new List<int>(inputs.MemberIds);
            ordered.Sort();
            // Domestic responsibilities are opportunities within a useful part of the day,
            // not appointments at one exact minute. Search forward in deterministic
            // 15-minute steps for up to three hours, keeping hard obligations exclusive.
            for (int offset = 0; offset <= 180; offset += 15)
            {
                int candidateStart = startMinute + offset;
                if (candidateStart < 0 || candidateStart > 1439) continue;
                int bestAdult = -1;
                int bestAdultLoad = int.MaxValue;
                int fallback = -1;
                foreach (int id in ordered)
                {
                    // W2: an off-map member is never free for domestic work.
                    if (IsOffMapPerson(id, dayIndex, diag))
                    {
                        rejections.Add("P" + id + ": off-map (freight journey).");
                        continue;
                    }

                    AgeBand age = inputs.AgeBandFor != null ? inputs.AgeBandFor(id) : AgeBand.Adult18Plus;
                    if (age < AgeBand.YoungWorker16To17) { rejections.Add("P" + id + ": too young."); continue; }
                    EntityId worker = EntityId.For(EntityKind.Person, id);
                    int duration = tasks.EstimateMinutesForWorker(definitionId, worker);
                    if (inputs.IsWindowUnavailable != null
                        && inputs.IsWindowUnavailable(id, candidateStart, duration))
                    { rejections.Add("P" + id + ": committed to scheduled work at " + candidateStart + " for " + duration + " min."); continue; }
                    bool conflictsWithHardActivity = false;
                    if (schedules != null)
                    {
                        foreach (PersonActivityReservation reservation in schedules.Overlapping(id, dayIndex, candidateStart, duration))
                            if (reservation.Activity != PersonActivityKind.Resting) { conflictsWithHardActivity = true; break; }
                    }
                    if (conflictsWithHardActivity)
                    { rejections.Add("P" + id + ": occupied at " + candidateStart + " for estimated " + duration + " min."); continue; }
                    if (budgets != null && budgets.GetOrCreate(worker).MinutesRemaining < duration)
                    { rejections.Add("P" + id + ": budget short for estimated " + duration + " min."); continue; }
                    if (HasPlannedDomesticWindowOverlap(inputs.HouseholdId, id, dayIndex,
                            candidateStart, duration, tasks))
                    { rejections.Add("P" + id + ": another household task already uses this window."); continue; }
                    if (age >= AgeBand.Adult18Plus)
                    {
                        int resultingLoad = GetDomesticMinutesAlreadyPlanned(
                            inputs.HouseholdId, id, dayIndex, tasks, domestic) + duration;
                        if (resultingLoad < bestAdultLoad)
                        {
                            bestAdult = id;
                            bestAdultLoad = resultingLoad;
                        }
                        continue;
                    }
                    if (fallback < 0) fallback = id;
                }
                if (bestAdult >= 0) { selectedStartMinute = candidateStart; return bestAdult; }
                if (fallback >= 0) { selectedStartMinute = candidateStart; return fallback; }
            }
            reason = "Nobody has a hard-obligation-free window within three hours for the skill-scaled task duration: " + string.Join(" ", rejections);
            return -1;
        }

        private bool HasPlannedDomesticWindowOverlap(int householdId, int personId, int dayIndex,
            int startMinute, int durationMinutes, TaskAuthority tasks)
        {
            int endMinute = startMinute + durationMinutes;
            foreach (PendingDomesticTask item in pending)
            {
                if (item == null || item.HouseholdId != householdId || item.PersonId != personId
                    || item.DayIndex != dayIndex || !string.Equals(item.Status, "Pending", StringComparison.Ordinal))
                    continue;
                WorkTask pendingTask = item.ExistingTaskId.IsValid ? tasks.FindTask(item.ExistingTaskId) : null;
                int pendingDuration = pendingTask != null
                    ? Math.Max(1, pendingTask.PlannedMinutes)
                    : Math.Max(1, tasks.EstimateMinutesForWorker(item.DefinitionId,
                        EntityId.For(EntityKind.Person, personId)));
                if (startMinute < item.StartMinuteOfDay + pendingDuration
                    && item.StartMinuteOfDay < endMinute) return true;
            }
            return false;
        }

        /// <summary>
        /// Shares same-day household work across available adults. The score
        /// is actual TaskAuthority planned minutes (skill-scaled by the task
        /// estimator), not sex, age-order preference, or a fabricated role.
        /// Hard paid-work and other commitments remain governed by the shared
        /// PersonScheduleTracker above.
        /// </summary>
        private int GetDomesticMinutesAlreadyPlanned(
            int householdId, int personId, int dayIndex, TaskAuthority tasks, DomesticLaborService domestic)
        {
            int minutes = 0;
            var countedTasks = new HashSet<EntityId>();
            foreach (PendingDomesticTask item in pending)
            {
                if (item == null || item.HouseholdId != householdId || item.PersonId != personId
                    || item.DayIndex != dayIndex || !string.Equals(item.Status, "Pending", StringComparison.Ordinal))
                    continue;
                WorkTask task = item.ExistingTaskId.IsValid ? tasks.FindTask(item.ExistingTaskId) : null;
                minutes += task != null
                    ? Math.Max(1, task.PlannedMinutes)
                    : Math.Max(1, tasks.EstimateMinutesForWorker(item.DefinitionId, EntityId.For(EntityKind.Person, personId)));
                if (item.ExistingTaskId.IsValid) countedTasks.Add(item.ExistingTaskId);
            }

            foreach (DomesticAssignment assignment in domestic.Assignments)
            {
                if (assignment == null || assignment.PersonId != personId || assignment.DayIndex != dayIndex
                    || !countedTasks.Add(assignment.TaskId)) continue;
                WorkTask task = tasks.FindTask(assignment.TaskId);
                if (task != null) minutes += Math.Max(1, task.PlannedMinutes);
            }
            return minutes;
        }

        private static DomesticAssignment FindOpenAssignment(
            DomesticLaborService domestic, TaskAuthority tasks, int householdId, int personId, string definitionId,
            List<InFlightDomesticTask> flights)
        {
            for (int i = domestic.Assignments.Count - 1; i >= 0; i--)
            {
                DomesticAssignment a = domestic.Assignments[i];
                if (a != null && !a.Completed && a.HouseholdId == householdId &&
                    a.PersonId == personId &&
                    string.Equals(a.DefinitionId, definitionId, StringComparison.Ordinal))
                {
                    bool alreadyInFlight = false;
                    if (flights != null)
                        foreach (InFlightDomesticTask flight in flights)
                            if (flight != null && flight.TaskId == a.TaskId) { alreadyInFlight = true; break; }
                    WorkTask task = tasks?.FindTask(a.TaskId);
                    if (!alreadyInFlight && task != null && task.IsOpen
                        && task.Status == TaskStatus.InProgress && task.HasAssignee
                        && task.AssigneeId == EntityId.For(EntityKind.Person, personId)) return a;
                }
            }

            return null;
        }

        private static bool HasOpenAssignment(DomesticLaborService domestic, EntityId taskId)
        {
            foreach (DomesticAssignment a in domestic.Assignments)
            {
                if (a != null && !a.Completed && a.TaskId == taskId)
                {
                    return true;
                }
            }

            return false;
        }

        private static void ReleaseAssignmentReservation(
            DomesticLaborService domestic, PersonScheduleTracker schedules, InFlightDomesticTask flight)
        {
            if (domestic == null || schedules == null || flight == null)
            {
                return;
            }

            foreach (DomesticAssignment a in domestic.Assignments)
            {
                if (a != null && !a.Completed && a.TaskId == flight.TaskId)
                {
                    schedules.Release(a.ReservationId);
                    a.Completed = true; // closed without credit — task never completed
                    return;
                }
            }
        }

        /// <summary>
        /// Campaign C-24 (DEF-010): record one day of household use for the
        /// cleaning need (one unit per occupant present). Idempotent per
        /// household per day — the tracker guards the day key. Called by the
        /// daily drive before planning, so the cleaning input reads the
        /// accumulated state including today.
        /// </summary>
        public void AccumulateCleaningUse(int householdId, int membersAtHome, int dayIndex)
        {
            cleanliness.Accumulate(householdId, membersAtHome, dayIndex);
        }

        /// <summary>
        /// Campaign C-24 (DEF-010): true once the household's accumulated use
        /// reaches the cleaning threshold.
        /// </summary>
        public bool CleaningNeeded(int householdId)
        {
            return cleanliness.NeedsCleaning(householdId);
        }

        /// <summary>Save: pending + in-flight driver state (owning runtime class holds its DTO).</summary>
        public DomesticLaborDriverSaveDto CaptureSaveDto()
        {
            return new DomesticLaborDriverSaveDto
            {
                pending = new List<PendingDomesticTask>(pending),
                inFlight = new List<InFlightDomesticTask>(inFlight),
                routeRecoveries = new List<DomesticRouteRecoveryRecord>(routeRecoveries),
                // Campaign C-24 (DEF-010): cleanliness accumulation is domestic
                // state too — a save that lost it would forget how soiled each
                // household is and re-earn every cleaning need from zero.
                cleanliness = cleanliness.CaptureRecords(),
            };
        }

        /// <summary>Save: clear-then-load. Restored pending tasks resume on their day; in-flight tasks resume via their due events.</summary>
        public void LoadFromSaveDto(DomesticLaborDriverSaveDto dto)
        {
            pending.Clear();
            inFlight.Clear();
            routeRecoveries.Clear();
            cleanliness.LoadRecords(null);
            // B3: the transient per-household context is cleared on load —
            // a restored driver must never settle against a previous
            // session's objects. The maps are repopulated at the next
            // DriveDay; a flight due before then settles nothing and
            // consumes nothing (recorded loudly, never silently).
            householdById.Clear();
            stovesByHousehold.Clear();
            inventoryByHousehold.Clear();
            mealServiceByHousehold.Clear();
            fuelForecastTemperatureByHousehold.Clear();
            if (dto == null)
            {
                return;
            }

            if (dto.pending != null)
            {
                pending.AddRange(dto.pending);
            }

            if (dto.inFlight != null)
            {
                inFlight.AddRange(dto.inFlight);
            }

            if (dto.routeRecoveries != null)
            {
                routeRecoveries.AddRange(dto.routeRecoveries);
            }

            cleanliness.LoadRecords(dto.cleanliness);
        }
    }

    /// <summary>Save data for the domestic labor driver.</summary>
    [Serializable]
    public sealed class DomesticLaborDriverSaveDto
    {
        public List<DomesticLaborDriver.PendingDomesticTask> pending = new List<DomesticLaborDriver.PendingDomesticTask>();
        public List<DomesticLaborDriver.InFlightDomesticTask> inFlight = new List<DomesticLaborDriver.InFlightDomesticTask>();
        public List<DomesticLaborDriver.DomesticRouteRecoveryRecord> routeRecoveries = new List<DomesticLaborDriver.DomesticRouteRecoveryRecord>();
        /// <summary>Campaign C-24 (DEF-010): per-household cleanliness accumulation.</summary>
        public List<HouseholdCleanlinessRecord> cleanliness = new List<HouseholdCleanlinessRecord>();
    }
}
