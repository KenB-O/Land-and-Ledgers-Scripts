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
            /// <summary>True only when a supported household cleanliness need exists.</summary>
            public bool CleaningNeeded;
            public bool ColdSeason;
            public bool HasAnimals;
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
        }

        private readonly List<PendingDomesticTask> pending = new List<PendingDomesticTask>();
        private readonly List<InFlightDomesticTask> inFlight = new List<InFlightDomesticTask>();
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<PendingDomesticTask> Pending => pending;
        public IReadOnlyList<InFlightDomesticTask> InFlight => inFlight;

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
                wanted.Add((DomesticTaskCatalog.FuelHandlingId, 540, "household fuel-handling need"));
            }

            if (inputs.HasAnimals)
            {
                wanted.Add((DomesticTaskCatalog.AnimalCareId, 480, "household animals need tending"));
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

                pending.Add(new PendingDomesticTask
                {
                    HouseholdId = inputs.HouseholdId,
                    PersonId = personId,
                    DefinitionId = want.definitionId,
                    DayIndex = dayIndex,
                    StartMinuteOfDay = selectedStartMinute,
                    Status = "Pending",
                    Note = want.why,
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
                });
                diag.Add("DomesticLaborDriver: P" + item.PersonId + " started '" + item.DefinitionId +
                    "' (" + plannedMinutes + " min)" + (requiresPhysicalArrival
                        ? "; waiting for a real activity arrival before work time starts."
                        : "; completion due at +" + plannedMinutes + " min."));
            }

            if (physicalActivity != null)
                AdvancePhysicalActivities(currentGameSeconds, tasks, budgets, schedules, domestic,
                    dueEvents, physicalActivity, diag);

            // Terminal entries (Started/Failed) have done their job in this
            // list — the in-flight record tracks started work to completion.
            pending.RemoveAll(item => item == null
                || !string.Equals(item.Status, "Pending", StringComparison.Ordinal));
        }

        private void AdvancePhysicalActivities(float currentGameSeconds, TaskAuthority tasks,
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
                    if (tasks.InterruptTask(flight.TaskId, budgets, out string rejection))
                    {
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
            }
        }

        /// <summary>
        /// Orphaned-assignment fix: domestic-definition tasks sitting Assigned with
        /// no open DomesticAssignment are start-failures the service already
        /// released (reservation + budget) but left in the task queue. Requeue
        /// them (InterruptTask: release + back to Queued) so the next DriveDay
        /// re-drives the work instead of losing it silently. When the task is
        /// not interruptible, cancel it loudly.
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
                if (task == null || task.Status != TaskStatus.Assigned)
                {
                    continue;
                }

                if (!DomesticTaskCatalog.IsDomesticDefinition(task.DefinitionId))
                {
                    continue;
                }

                if (HasOpenAssignment(domestic, task.TaskId))
                {
                    continue;
                }

                string rejection;
                if (tasks.InterruptTask(task.TaskId, budgets, out rejection))
                {
                    diag.Add("DomesticLaborDriver: requeued orphaned domestic task " + task.TaskId +
                        " ('" + task.DefinitionId + "') — start-failure no longer leaves it Assigned forever.");
                }
                else
                {
                    // Not interruptible: cancel loudly rather than leak.
                    if (tasks.CancelTask(task.TaskId, budgets))
                    {
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

        // ---------- internals ----------

        private void AdoptRequeued(DomesticDayInputs inputs, int dayIndex, TaskAuthority tasks,
            WorkTimeBudgetStore budgets, PersonScheduleTracker schedules,
            DomesticLaborService domestic, List<string> diag)
        {
            int householdId = inputs.HouseholdId;
            EntityId owner = EntityId.For(EntityKind.Household, householdId);
            List<WorkTask> open = tasks.GetOpenTasks(owner);
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
                });
                diag.Add("DomesticLaborDriver: re-adopted existing task " + task.TaskId +
                    " ('" + task.DefinitionId + "') for household " + householdId + ".");
            }
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

        /// <summary>Save: pending + in-flight driver state (owning runtime class holds its DTO).</summary>
        public DomesticLaborDriverSaveDto CaptureSaveDto()
        {
            return new DomesticLaborDriverSaveDto
            {
                pending = new List<PendingDomesticTask>(pending),
                inFlight = new List<InFlightDomesticTask>(inFlight),
            };
        }

        /// <summary>Save: clear-then-load. Restored pending tasks resume on their day; in-flight tasks resume via their due events.</summary>
        public void LoadFromSaveDto(DomesticLaborDriverSaveDto dto)
        {
            pending.Clear();
            inFlight.Clear();
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
        }
    }

    /// <summary>Save data for the domestic labor driver.</summary>
    [Serializable]
    public sealed class DomesticLaborDriverSaveDto
    {
        public List<DomesticLaborDriver.PendingDomesticTask> pending = new List<DomesticLaborDriver.PendingDomesticTask>();
        public List<DomesticLaborDriver.InFlightDomesticTask> inFlight = new List<DomesticLaborDriver.InFlightDomesticTask>();
    }
}
