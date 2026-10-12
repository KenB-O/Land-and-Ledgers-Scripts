using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;

namespace LandLedgers.Population
{
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
            /// <summary>Meals the household eats at home today (meal-prep trigger).</summary>
            public int MealsAtHomeCount;
            public bool ColdSeason;
            public bool HasAnimals;
        }

        /// <summary>One planned domestic task: not yet scheduled.</summary>
        [Serializable]
        public sealed class PendingDomesticTask
        {
            public int HouseholdId = -1;
            public int PersonId = -1;
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
        }

        private readonly List<PendingDomesticTask> pending = new List<PendingDomesticTask>();
        private readonly List<InFlightDomesticTask> inFlight = new List<InFlightDomesticTask>();
        private readonly DailyActivityPlanner planner = new DailyActivityPlanner();
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<PendingDomesticTask> Pending => pending;
        public IReadOnlyList<InFlightDomesticTask> InFlight => inFlight;

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

            // First: requeue anything a start-failure orphaned, then adopt the
            // requeued domestic tasks for this household as today's pending work.
            ReconcileStalled(inputs.HouseholdId, tasks, budgets, schedules, domestic, diag);
            AdoptRequeued(inputs.HouseholdId, dayIndex, tasks, diag);

            var wanted = new List<(string definitionId, int startMinute, string why)>();
            if (inputs.MealsAtHomeCount > 0)
            {
                wanted.Add((DomesticTaskCatalog.MealPrepId, 990, inputs.MealsAtHomeCount + " home meal(s) today"));
            }

            if (inputs.ColdSeason)
            {
                wanted.Add((DomesticTaskCatalog.FuelHandlingId, 540, "cold season fuel handling"));
            }

            if (inputs.HasAnimals)
            {
                wanted.Add((DomesticTaskCatalog.AnimalCareId, 480, "household animals need tending"));
            }

            wanted.Add((DomesticTaskCatalog.ChoresId, 660, "baseline daily chores"));

            foreach (var want in wanted)
            {
                TaskDefinition definition = tasks.GetDefinition(want.definitionId);
                if (definition == null)
                {
                    diag.Add("DomesticLaborDriver: definition '" + want.definitionId + "' not registered — skipped.");
                    continue;
                }

                int minutes = Math.Max(1, definition.BaseMinutes);
                string reason;
                int personId = planner.FindFreeMemberForWindow(
                    inputs.MemberIds, inputs.AgeBandFor, dayIndex,
                    want.startMinute, minutes, schedules, budgets, out reason);
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
                    StartMinuteOfDay = want.startMinute,
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
        /// </summary>
        public void AdvanceTick(
            float currentGameSeconds,
            TaskAuthority tasks,
            WorkTimeBudgetStore budgets,
            PersonScheduleTracker schedules,
            DomesticLaborService domestic,
            IDueEventScheduler dueEvents,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (tasks == null || budgets == null || schedules == null || domestic == null || dueEvents == null)
            {
                return;
            }

            int dayIndex = (int)Math.Floor(currentGameSeconds / 86400f);
            int minuteOfDay = (int)Math.Floor((currentGameSeconds - dayIndex * 86400f) / 60f);

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

                var callDiag = new List<string>();
                string error = domestic.ScheduleDomesticWork(
                    item.HouseholdId, item.PersonId, item.DefinitionId,
                    dayIndex, item.StartMinuteOfDay,
                    tasks, budgets, schedules, callDiag);
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
                DomesticAssignment assignment = FindOpenAssignment(domestic, item.HouseholdId, item.PersonId, item.DefinitionId);
                if (assignment == null)
                {
                    diag.Add("DomesticLaborDriver: INCONSISTENCY — scheduled '" + item.DefinitionId +
                        "' but no open assignment tracked; completion will be reconciled, not lost.");
                    continue;
                }

                WorkTask task = tasks.FindTask(assignment.TaskId);
                int plannedMinutes = task != null ? task.PlannedMinutes : 30;
                string taskIdString = assignment.TaskId.ToString();
                string dueEventId;
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
                });
                diag.Add("DomesticLaborDriver: P" + item.PersonId + " started '" + item.DefinitionId +
                    "' (" + plannedMinutes + " min); completion due at +" + plannedMinutes + " min.");
            }
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
                // Task already closed or cancelled externally: release the
                // person's reservation and mark the assignment done-without-credit.
                diag.Add("DomesticLaborDriver: task " + ev.PayloadRef +
                    " is no longer open at its due time — releasing reservation, no work credited.");
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

        private void AdoptRequeued(int householdId, int dayIndex, TaskAuthority tasks, List<string> diag)
        {
            EntityId owner = EntityId.For(EntityKind.Household, householdId);
            List<WorkTask> open = tasks.GetOpenTasks(owner);
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

                bool alreadyPending = false;
                foreach (PendingDomesticTask p in pending)
                {
                    if (p != null && string.Equals(p.Status, "Pending", StringComparison.Ordinal) &&
                        string.Equals(p.DefinitionId, task.DefinitionId, StringComparison.Ordinal) &&
                        p.HouseholdId == householdId && p.DayIndex == dayIndex)
                    {
                        alreadyPending = true;
                        break;
                    }
                }

                if (!alreadyPending)
                {
                    diag.Add("DomesticLaborDriver: re-adopted requeued '" + task.DefinitionId +
                        "' for household " + householdId + " — it will be re-driven today.");
                }
            }
        }

        private static DomesticAssignment FindOpenAssignment(
            DomesticLaborService domestic, int householdId, int personId, string definitionId)
        {
            foreach (DomesticAssignment a in domestic.Assignments)
            {
                if (a != null && !a.Completed && a.HouseholdId == householdId &&
                    a.PersonId == personId &&
                    string.Equals(a.DefinitionId, definitionId, StringComparison.Ordinal))
                {
                    return a;
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
