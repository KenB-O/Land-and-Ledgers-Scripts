using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;

namespace LandLedgers.Population
{
    /// <summary>
    /// Worker C (Pass III), brief §12: schedules and completes domestic work on
    /// the EXISTING TaskAuthority + person-time substrate. Narrow scope: only
    /// DomesticTaskCatalog definitions may be scheduled here.
    ///
    /// Person-time exclusivity (Canon 13.4) is enforced by PersonScheduleTracker:
    /// a domestic reservation overlapping an employment (or any other) window is
    /// refused — domestic work genuinely competes with employment availability.
    ///
    /// Completed domestic work is credited to FamilyLaborLedger with
    /// agreedWageCents = 0: family/domestic work never creates wages (PL-04
    /// taxonomy; Pass II WorkRelationshipGuard). It creates no cash movement.
    /// </summary>
    public sealed class DomesticLaborService
    {
        private readonly List<DomesticAssignment> assignments = new List<DomesticAssignment>();
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<DomesticAssignment> Assignments => assignments;

        /// <summary>
        /// Schedules one domestic task for a household member. Returns null on
        /// success, or a refusal reason (not a domestic definition, person-time
        /// conflict, budget/assign failure). Never invents persons or time.
        /// </summary>
        public string ScheduleDomesticWork(int householdId, int personId, string definitionId,
            int dayIndex, int startMinuteOfDay,
            TaskAuthority tasks, WorkTimeBudgetStore budgets, PersonScheduleTracker schedules,
            List<string> diag)
        {
            if (tasks == null || budgets == null || schedules == null)
                return Refuse(diag, "DomesticLabor: task authority, budget store, or schedule tracker missing.");
            if (personId < 0)
                return Refuse(diag, "DomesticLabor: no real Person — only a real Person does domestic work.");
            if (!DomesticTaskCatalog.IsDomesticDefinition(definitionId))
                return Refuse(diag, "DomesticLabor: '" + (definitionId ?? "?") + "' is not a domestic task definition (narrow scope).");

            TaskDefinition definition = tasks.GetDefinition(definitionId);
            if (definition == null)
                return Refuse(diag, "DomesticLabor: definition '" + definitionId + "' not registered.");
            int plannedMinutes = definition.BaseMinutes;

            // Person-time reservation FIRST: domestic work competes with
            // employment availability through the same exclusive tracker.
            PersonActivityKind activity = string.Equals(definitionId, DomesticTaskCatalog.MealPrepId, StringComparison.Ordinal)
                ? PersonActivityKind.Cooking
                : PersonActivityKind.Work;
            string reservationRefusal = schedules.TryReserve(personId, activity, dayIndex,
                startMinuteOfDay, plannedMinutes,
                "domestic: " + definition.DisplayName + " (household " + householdId + ")");
            if (reservationRefusal != null)
                return Refuse(diag, "DomesticLabor: " + reservationRefusal);
            string reservationId = LastReservationId(schedules, personId, dayIndex);

            EntityId worker = EntityId.For(EntityKind.Person, personId);
            EntityId owner = EntityId.For(EntityKind.Household, householdId);
            WorkTask task = tasks.CreateTask(definitionId, owner, dayIndex);
            if (task == null)
            {
                schedules.Release(reservationId);
                return Refuse(diag, "DomesticLabor: task authority refused to create '" + definitionId + "'.");
            }

            string rejection;
            if (!tasks.AssignTask(task.TaskId, worker, budgets, out rejection))
            {
                schedules.Release(reservationId);
                return Refuse(diag, "DomesticLabor: assign refused — " + rejection);
            }
            if (!tasks.StartTask(task.TaskId, dayIndex, out rejection))
            {
                schedules.Release(reservationId);
                budgets.GetOrCreate(worker).ReleaseCommitment(task.PlannedMinutes);
                return Refuse(diag, "DomesticLabor: start refused — " + rejection);
            }

            assignments.Add(new DomesticAssignment
            {
                TaskId = task.TaskId,
                HouseholdId = householdId,
                PersonId = personId,
                DefinitionId = definitionId,
                DayIndex = dayIndex,
                ReservationId = reservationId,
                Completed = false,
            });
            if (diag != null) diag.Add(string.Format(
                "DomesticLabor: P{0} scheduled '{1}' ({2} min) for household {3} on day {4}",
                personId, definition.DisplayName, plannedMinutes, householdId, dayIndex));
            return null;
        }

        /// <summary>
        /// Completes a scheduled domestic task: records work, completes the task,
        /// releases the person-time reservation, and credits FamilyLaborLedger
        /// with agreedWageCents = 0 (never wages). Returns null on success.
        /// </summary>
        public string CompleteDomesticWork(EntityId taskId, int minutesWorked, int dayIndex,
            TaskAuthority tasks, WorkTimeBudgetStore budgets, PersonScheduleTracker schedules,
            FamilyLaborLedger familyLabor, List<string> diag)
        {
            if (tasks == null || budgets == null || schedules == null || familyLabor == null)
                return Refuse(diag, "DomesticLabor: completion requires task authority, budgets, schedules, and family-labor ledger.");

            DomesticAssignment assignment = null;
            foreach (DomesticAssignment a in assignments)
            {
                if (a != null && !a.Completed && a.TaskId == taskId)
                {
                    assignment = a;
                    break;
                }
            }
            if (assignment == null)
                return Refuse(diag, "DomesticLabor: no open domestic assignment for task " + taskId + ".");

            string rejection;
            if (!tasks.RecordWork(taskId, minutesWorked, budgets, dayIndex, out rejection))
                return Refuse(diag, "DomesticLabor: RecordWork refused — " + rejection);
            // RecordWork auto-completes when the planned minutes are fulfilled;
            // only complete explicitly when the task is still open.
            WorkTask task = tasks.FindTask(taskId);
            if (task != null && task.IsOpen)
            {
                if (!tasks.CompleteTask(taskId, TaskOutcome.Success, dayIndex, budgets))
                    return Refuse(diag, "DomesticLabor: CompleteTask refused for " + taskId + ".");
            }

            schedules.Release(assignment.ReservationId);

            string contributionRejection = familyLabor.RecordContribution(
                new FamilyLaborContribution
                {
                    PersonId = assignment.PersonId,
                    HouseholdId = assignment.HouseholdId,
                    BusinessId = string.Empty, // household domestic work, not a business
                    TaskCategory = "domestic",
                    DayIndex = dayIndex,
                    MinutesWorked = Math.Max(0, minutesWorked),
                },
                agreedWageCents: 0); // PL-04: family/domestic work never carries wage terms
            if (contributionRejection != null)
                return Refuse(diag, "DomesticLabor: family-labor credit refused — " + contributionRejection);

            assignment.Completed = true;
            if (diag != null) diag.Add(string.Format(
                "DomesticLabor: P{0} completed '{1}' ({2} min) — credited as family labor, no wages",
                assignment.PersonId, assignment.DefinitionId, minutesWorked));
            return null;
        }

        private static string LastReservationId(PersonScheduleTracker schedules, int personId, int dayIndex)
        {
            List<PersonActivityReservation> active = schedules.ActiveFor(personId, dayIndex);
            return active.Count > 0 ? active[active.Count - 1].ReservationId : string.Empty;
        }

        private string Refuse(List<string> diag, string reason)
        {
            if (diag != null) diag.Add(reason);
            diagnostics.Add(reason);
            return reason;
        }

        /// <summary>Captures in-flight domestic assignments for the save pipeline.</summary>
        public DomesticLaborServiceSaveDto CaptureSaveDto()
        {
            return new DomesticLaborServiceSaveDto
            {
                assignments = new List<DomesticAssignment>(assignments),
            };
        }

        /// <summary>
        /// Clear-then-load. Restored assignments resume tracking without
        /// re-scheduling tasks, re-reserving time, or re-crediting family labor
        /// (no repeated Task experience credit — the task itself persists in
        /// TaskAuthority).
        /// </summary>
        public void LoadFromSaveDto(DomesticLaborServiceSaveDto dto)
        {
            assignments.Clear();
            if (dto == null || dto.assignments == null) return;
            foreach (DomesticAssignment a in dto.assignments)
            {
                if (a == null) continue;
                assignments.Add(a);
            }
        }
    }

    /// <summary>One scheduled domestic task: links the TaskAuthority task to the
    /// household member and their person-time reservation.</summary>
    [Serializable]
    public sealed class DomesticAssignment
    {
        public EntityId TaskId;
        public int HouseholdId;
        public int PersonId;
        public string DefinitionId = string.Empty;
        public int DayIndex;
        public string ReservationId = string.Empty;
        public bool Completed;
    }

    /// <summary>Save data for the domestic labor service.</summary>
    [Serializable]
    public sealed class DomesticLaborServiceSaveDto
    {
        public List<DomesticAssignment> assignments = new List<DomesticAssignment>();
    }
}
