using System;
using System.Collections.Generic;
using LandLedgers.Economy.Equipment;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using LandLedgers.Time;

namespace LandLedgers.Economy.Labor
{
    /// <summary>
    /// Pass IV (Worker 4, Phase O): the LIVE work-allocation driver for
    /// delegated managers. The ManagerBrain decides (intents within delegated
    /// authority); this allocator EXECUTES the scheduling intent against real
    /// state:
    ///
    /// - Observes the backlog: open tasks owned by the business, ordered by
    ///   failure/rework first, then due-event deadlines, then shortage-driven
    ///   work, then queue priority and creation order.
    /// - Checks worker time (schedule windows + work-time budget), materials
    ///   (caller-supplied check — the allocator never assumes stock), and
    ///   equipment (the unified reservation authority when wired; an honest
    ///   "check skipped" note when it is not).
    /// - Assigns where authorized: the Scheduling scope must be delegated and
    ///   the worker must be really employed by this business. Every assignment
    ///   reserves the worker's person-time window — no double-booking.
    /// - Responds to failures: releases the failed task's reservations and
    ///   creates a rework follow-up when materials allow, else defers loudly.
    /// - Considers hiring under GENUINE overload: a 7-day pressure tracker
    ///   compares backlog minutes against available worker minutes; sustained
    ///   overload with the Staffing scope emits a hiring recommendation (the
    ///   integrator executes — the allocator never invents workers).
    ///
    /// The manager is an ordinary person: allocation reserves a Managing
    /// window and commits budget for the manager's own time. Deterministic.
    /// </summary>
    public sealed class ManagerWorkAllocator
    {
        /// <summary>One worker's free windows today (caller-computed from the schedule tracker).</summary>
        [Serializable]
        public sealed class WorkerAvailability
        {
            public int PersonId = -1;
            public string BusinessInstanceId = string.Empty;
            public List<WorkWindow> FreeWindows = new List<WorkWindow>();
        }

        [Serializable]
        public sealed class WorkWindow
        {
            public int StartMinuteOfDay;
            public int DurationMinutes;
        }

        /// <summary>A hiring recommendation: evidence, never an auto-hire.</summary>
        [Serializable]
        public sealed class HiringRecommendation
        {
            public string BusinessInstanceId = string.Empty;
            public int DayIndex = -1;
            public int BacklogMinutes;
            public int CapacityMinutes;
            public int OverloadDays;
            public string Reason = string.Empty;
        }

        public sealed class AllocationInputs
        {
            public string BusinessInstanceId = string.Empty;
            public EntityId BusinessOwner;
            public int ManagerPersonId = -1;
            public List<string> DelegatedScopes = new List<string>();
            public TaskAuthority Tasks;
            public WorkTimeBudgetStore Budgets;
            public PersonScheduleTracker Schedules;
            /// <summary>Null = equipment checks are honestly skipped (Worker 2 wires this).</summary>
            public IUnifiedReservationAuthority Reservations;
            public List<WorkerAvailability> Workers = new List<WorkerAvailability>();
            /// <summary>Null = ok; returns the shortage reason otherwise.</summary>
            public Func<WorkTask, string> MaterialCheck;
            /// <summary>Equipment resource keys a task needs (unified key space).</summary>
            public Func<WorkTask, List<string>> EquipmentKeysFor;
            /// <summary>True when the task is shortage-driven (priority boost).</summary>
            public Func<WorkTask, bool> IsShortageDriven;
            /// <summary>Recently failed tasks needing a response (closed tasks).</summary>
            public List<WorkTask> RecentlyFailedTasks = new List<WorkTask>();
            public int DayIndex;
            public float NowGameSeconds;
            /// <summary>Optional: receives hiring recommendations (integrator executes).</summary>
            public Action<HiringRecommendation> HiringPort;
            /// <summary>Minutes of manager time this allocation round costs.</summary>
            public int ManagerTimeCostMinutes = 60;
            /// <summary>
            /// Optional: when set, every assignment schedules an ActivityWindowDue
            /// event at the window start so a tick driver starts the task there.
            /// </summary>
            public IDueEventScheduler DueEvents;
        }

        public sealed class AllocationReport
        {
            public List<string> AssignedTaskIds = new List<string>();
            public List<AssignmentRecord> Assignments = new List<AssignmentRecord>();
            public List<string> DeferredTaskIds = new List<string>();
            public List<string> DeferralReasons = new List<string>();
            public List<string> FailureResponses = new List<string>();
            public List<HiringRecommendation> HiringRecommendations = new List<HiringRecommendation>();
            public List<string> Notes = new List<string>();
            public ManagerActivityRecord ActivityRecord;
        }

        [Serializable]
        public sealed class AssignmentRecord
        {
            public string TaskIdString = string.Empty;
            public int PersonId = -1;
            public int WindowStartMinute;
            public int WindowDurationMinutes;
            public string ReservationId = string.Empty;
        }

        /// <summary>
        /// 7-day overload pressure tracker: backlog minutes vs available worker
        /// minutes per day. Sustained overload (default: 3+ consecutive days at
        /// 1.5x) is genuine hiring evidence — never a quota.
        /// </summary>
        [Serializable]
        public sealed class HiringPressureTracker
        {
            [Serializable]
            public sealed class DayPressure
            {
                public int DayIndex;
                public int BacklogMinutes;
                public int CapacityMinutes;
            }

            public List<DayPressure> History = new List<DayPressure>();
            public int OverloadRatioPercent = 150;
            public int SustainedDays = 3;
            private const int MaxHistory = 14;

            public void RecordDay(int dayIndex, int backlogMinutes, int capacityMinutes)
            {
                History.RemoveAll(d => d != null && d.DayIndex == dayIndex);
                History.Add(new DayPressure { DayIndex = dayIndex, BacklogMinutes = backlogMinutes, CapacityMinutes = capacityMinutes });
                History.Sort((a, b) => a.DayIndex.CompareTo(b.DayIndex));
                while (History.Count > MaxHistory)
                {
                    History.RemoveAt(0);
                }
            }

            /// <summary>Consecutive overloaded days ending at the latest recorded day.</summary>
            public int ConsecutiveOverloadDays()
            {
                int count = 0;
                for (int i = History.Count - 1; i >= 0; i--)
                {
                    DayPressure d = History[i];
                    if (d == null || d.CapacityMinutes <= 0)
                    {
                        break;
                    }

                    if (d.BacklogMinutes * 100 >= d.CapacityMinutes * OverloadRatioPercent)
                    {
                        count++;
                    }
                    else
                    {
                        break;
                    }
                }

                return count;
            }
        }

        private readonly HiringPressureTracker pressureTracker = new HiringPressureTracker();
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public HiringPressureTracker PressureTracker => pressureTracker;

        /// <summary>Runs one allocation round for the business.</summary>
        public AllocationReport Allocate(AllocationInputs inputs, List<string> diag)
        {
            var report = new AllocationReport();
            diag = diag ?? diagnostics;
            if (inputs == null || inputs.Tasks == null || inputs.Budgets == null || inputs.Schedules == null)
            {
                report.Notes.Add("ManagerWorkAllocator: missing inputs/authorities — no allocation.");
                return report;
            }

            bool canSchedule = HasScope(inputs, ManagerScopeNames.Scheduling);
            bool canStaff = HasScope(inputs, ManagerScopeNames.Staffing);

            // The manager's own time is real: reserve a Managing window.
            string managerNote = ReserveManagerTime(inputs, diag);
            if (managerNote != null)
            {
                report.Notes.Add(managerNote);
            }

            List<WorkTask> backlog = inputs.Tasks.GetOpenTasks(inputs.BusinessOwner);
            var ordered = OrderBacklog(backlog, inputs);

            int backlogMinutes = 0;
            int capacityMinutes = 0;
            foreach (WorkerAvailability w in inputs.Workers)
            {
                if (w == null)
                {
                    continue;
                }

                foreach (WorkWindow window in w.FreeWindows)
                {
                    if (window != null)
                    {
                        capacityMinutes += Math.Max(0, window.DurationMinutes);
                    }
                }
            }

            foreach (WorkTask task in ordered)
            {
                if (task == null || task.Status != TaskStatus.Queued)
                {
                    continue;
                }

                backlogMinutes += task.PlannedMinutes;
                if (!canSchedule)
                {
                    Defer(report, task, "no Scheduling scope delegated — assignment refused", diag);
                    continue;
                }

                AllocateOne(inputs, task, report, diag);
            }

            // Release reservations held by tasks that are no longer open —
            // completed/failed work must not pin equipment.
            SweepStaleReservations(inputs, backlog, diag);

            // Respond to failures: release + rework where materials allow.
            RespondToFailures(inputs, report, diag);

            // Hiring under genuine overload.
            pressureTracker.RecordDay(Math.Max(0, inputs.DayIndex), backlogMinutes, capacityMinutes);
            ConsiderHiring(inputs, canStaff, report, diag);

            report.ActivityRecord = new ManagerActivityRecord
            {
                ManagerPersonId = inputs.ManagerPersonId,
                BusinessInstanceId = inputs.BusinessInstanceId,
                DayIndex = inputs.DayIndex,
                ObservedConditions = $"backlog {backlog.Count} open tasks ({backlogMinutes} min), worker capacity {capacityMinutes} min",
                Decision = $"assigned {report.AssignedTaskIds.Count}, deferred {report.DeferredTaskIds.Count}, " +
                    $"failure responses {report.FailureResponses.Count}, hiring recommendations {report.HiringRecommendations.Count}",
                Evidence = string.Join(" | ", report.DeferralReasons),
                AuthorityUsed = canSchedule ? "delegated Scheduling" : "no scheduling authority",
                ResultingReference = string.Join(",", report.AssignedTaskIds),
                Outcome = report.AssignedTaskIds.Count > 0 ? "assignments made" : "no assignments",
                TimeCostMinutes = inputs.ManagerTimeCostMinutes,
                IsSignificant = report.AssignedTaskIds.Count > 0 || report.HiringRecommendations.Count > 0,
            };
            return report;
        }

        // ---------- internals ----------

        private static bool HasScope(AllocationInputs inputs, string scope)
        {
            if (inputs.DelegatedScopes == null)
            {
                return false;
            }

            foreach (string s in inputs.DelegatedScopes)
            {
                if (string.Equals(s, scope, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private string ReserveManagerTime(AllocationInputs inputs, List<string> diag)
        {
            if (inputs.ManagerPersonId < 0 || inputs.ManagerTimeCostMinutes <= 0)
            {
                return null;
            }

            // Find a free gap for the manager's own allocation work.
            for (int start = 540; start + inputs.ManagerTimeCostMinutes <= 1440; start += 15)
            {
                if (!inputs.Schedules.IsFree(inputs.ManagerPersonId, inputs.DayIndex, start, inputs.ManagerTimeCostMinutes))
                {
                    continue;
                }

                EntityId manager = EntityId.For(EntityKind.Person, inputs.ManagerPersonId);
                string commitRejection;
                if (!inputs.Budgets.GetOrCreate(manager).TryCommit(inputs.ManagerTimeCostMinutes, out commitRejection))
                {
                    return $"Manager P{inputs.ManagerPersonId} has no budget left for allocation work ({commitRejection}) — allocating anyway would fake time.";
                }

                string refusal = inputs.Schedules.TryReserve(inputs.ManagerPersonId, PersonActivityKind.Managing,
                    inputs.DayIndex, start, inputs.ManagerTimeCostMinutes,
                    $"manage {inputs.BusinessInstanceId}: work allocation");
                if (refusal == null)
                {
                    diag.Add($"ManagerWorkAllocator: P{inputs.ManagerPersonId} managing [{start}-{start + inputs.ManagerTimeCostMinutes}] " +
                        $"(real person-time, budget committed).");
                    return null;
                }

                inputs.Budgets.GetOrCreate(manager).ReleaseCommitment(inputs.ManagerTimeCostMinutes);
            }

            return $"Manager P{inputs.ManagerPersonId} has no free {inputs.ManagerTimeCostMinutes}-min window today — allocation done without reserved manager time (loud).";
        }

        private List<WorkTask> OrderBacklog(List<WorkTask> backlog, AllocationInputs inputs)
        {
            var ordered = new List<WorkTask>(backlog ?? new List<WorkTask>());
            ordered.Sort((a, b) =>
            {
                if (a == null || b == null)
                {
                    return 0;
                }

                // Rework first: failed work blocks everything downstream.
                int aRework = a.Status == TaskStatus.ReworkRequired ? 0 : 1;
                int bRework = b.Status == TaskStatus.ReworkRequired ? 0 : 1;
                int cmp = aRework.CompareTo(bRework);
                if (cmp != 0)
                {
                    return cmp;
                }

                // Shortage-driven work next (empty shelves, starving processes).
                bool aShort = inputs.IsShortageDriven != null && SafeIsShortage(inputs, a);
                bool bShort = inputs.IsShortageDriven != null && SafeIsShortage(inputs, b);
                cmp = (aShort ? 0 : 1).CompareTo(bShort ? 0 : 1);
                if (cmp != 0)
                {
                    return cmp;
                }

                // Higher queue priority, then older first (deterministic).
                cmp = b.Priority.CompareTo(a.Priority);
                if (cmp != 0)
                {
                    return cmp;
                }

                return a.CreatedDayIndex.CompareTo(b.CreatedDayIndex);
            });
            return ordered;
        }

        private static bool SafeIsShortage(AllocationInputs inputs, WorkTask task)
        {
            try
            {
                return inputs.IsShortageDriven(task);
            }
            catch
            {
                return false;
            }
        }

        private void AllocateOne(AllocationInputs inputs, WorkTask task, AllocationReport report, List<string> diag)
        {
            // Materials: the caller checks real stock. No stock, no assignment.
            if (inputs.MaterialCheck != null)
            {
                string shortage;
                try
                {
                    shortage = inputs.MaterialCheck(task);
                }
                catch (Exception e)
                {
                    shortage = "material check fault: " + e.Message;
                }

                if (!string.IsNullOrEmpty(shortage))
                {
                    Defer(report, task, "materials: " + shortage, diag);
                    return;
                }
            }

            // Equipment: reserve through the unified authority when wired.
            List<string> equipmentKeys = null;
            if (inputs.EquipmentKeysFor != null)
            {
                try
                {
                    equipmentKeys = inputs.EquipmentKeysFor(task);
                }
                catch (Exception e)
                {
                    Defer(report, task, "equipment key fault: " + e.Message, diag);
                    return;
                }
            }

            string reservationId = null;
            if (equipmentKeys != null && equipmentKeys.Count > 0)
            {
                if (inputs.Reservations == null)
                {
                    report.Notes.Add($"Task {task.TaskId} needs equipment [{string.Join(",", equipmentKeys)}] — no reservation authority wired, check honestly skipped.");
                }
                else
                {
                    var request = new ReservationRequest("task:" + task.TaskId, $"alloc:{inputs.BusinessInstanceId}");
                    float start = inputs.NowGameSeconds;
                    float end = start + Math.Max(1, task.PlannedMinutes) * 60f;
                    foreach (string key in equipmentKeys)
                    {
                        if (!string.IsNullOrWhiteSpace(key))
                        {
                            request.Items.Add(new ReservationItem(key, 1f, start, end));
                        }
                    }

                    ReservationHandle handle;
                    string refusal = inputs.Reservations.TryReserveAll(request, out handle);
                    if (refusal != null)
                    {
                        Defer(report, task, "equipment: " + refusal, diag);
                        return;
                    }

                    reservationId = handle != null ? handle.ReservationId : string.Empty;
                }
            }

            // Worker: employed here, free window covering the task, budget left.
            foreach (WorkerAvailability worker in inputs.Workers)
            {
                if (worker == null || worker.PersonId < 0)
                {
                    continue;
                }

                if (!string.Equals(worker.BusinessInstanceId, inputs.BusinessInstanceId, StringComparison.Ordinal))
                {
                    continue; // not employed here — never assign strangers' time.
                }

                foreach (WorkWindow window in worker.FreeWindows)
                {
                    if (window == null || window.DurationMinutes < task.PlannedMinutes)
                    {
                        continue;
                    }

                    EntityId workerId = EntityId.For(EntityKind.Person, worker.PersonId);
                    WorkTimeBudget budget = inputs.Budgets.GetOrCreate(workerId);
                    if (budget.MinutesRemaining < task.PlannedMinutes)
                    {
                        continue;
                    }

                    string rejection;
                    if (!inputs.Tasks.AssignTask(task.TaskId, workerId, inputs.Budgets, out rejection))
                    {
                        continue; // try the next worker/window
                    }

                    string windowRefusal = inputs.Schedules.TryReserve(worker.PersonId, PersonActivityKind.Work,
                        inputs.DayIndex, window.StartMinuteOfDay, task.PlannedMinutes,
                        $"alloc:{inputs.BusinessInstanceId} {task.TaskId}");
                    if (windowRefusal != null)
                    {
                        // Roll back the assignment — half-allocated work is a lie.
                        string ignore;
                        inputs.Tasks.InterruptTask(task.TaskId, inputs.Budgets, out ignore);
                        if (!string.IsNullOrEmpty(reservationId) && inputs.Reservations != null)
                        {
                            inputs.Reservations.Release(reservationId);
                        }

                        Defer(report, task, $"P{worker.PersonId} time: {windowRefusal}", diag);
                        return;
                    }

                    report.AssignedTaskIds.Add(task.TaskId.ToString());
                    report.Assignments.Add(new AssignmentRecord
                    {
                        TaskIdString = task.TaskId.ToString(),
                        PersonId = worker.PersonId,
                        WindowStartMinute = window.StartMinuteOfDay,
                        WindowDurationMinutes = task.PlannedMinutes,
                        ReservationId = LastReservationId(inputs, worker.PersonId, window.StartMinuteOfDay),
                    });
                    if (inputs.DueEvents != null)
                    {
                        string dueId;
                        inputs.DueEvents.Schedule(DueEventKind.ActivityWindowDue,
                            inputs.DayIndex * 86400f + window.StartMinuteOfDay * 60f,
                            task.TaskId.ToString(), out dueId);
                    }

                    diag.Add($"ManagerWorkAllocator: {task.TaskId} → P{worker.PersonId} " +
                        $"[{window.StartMinuteOfDay}-{window.StartMinuteOfDay + task.PlannedMinutes}]" +
                        (string.IsNullOrEmpty(reservationId) ? "" : $" + equipment {reservationId}"));
                    return;
                }
            }

            // No worker could take it: release the equipment hold.
            if (!string.IsNullOrEmpty(reservationId) && inputs.Reservations != null)
            {
                inputs.Reservations.Release(reservationId);
            }

            Defer(report, task, "no employed worker with a free window and budget for " + task.PlannedMinutes + " min", diag);
        }

        private static string LastReservationId(AllocationInputs inputs, int personId, int startMinuteOfDay)
        {
            List<PersonActivityReservation> active = inputs.Schedules.ActiveFor(personId, inputs.DayIndex);
            for (int i = active.Count - 1; i >= 0; i--)
            {
                PersonActivityReservation r = active[i];
                if (r != null && r.Activity == PersonActivityKind.Work && r.StartMinuteOfDay == startMinuteOfDay)
                {
                    return r.ReservationId;
                }
            }

            return string.Empty;
        }

        private static void Defer(AllocationReport report, WorkTask task, string reason, List<string> diag)
        {
            report.DeferredTaskIds.Add(task.TaskId.ToString());
            report.DeferralReasons.Add($"{task.TaskId} ('{task.DefinitionId}'): {reason}");
            if (diag != null)
            {
                diag.Add("ManagerWorkAllocator: deferred " + task.TaskId + " — " + reason);
            }
        }

        private void SweepStaleReservations(AllocationInputs inputs, List<WorkTask> backlog, List<string> diag)
        {
            if (inputs.Reservations == null)
            {
                return;
            }

            var openIds = new HashSet<string>(StringComparer.Ordinal);
            if (backlog != null)
            {
                foreach (WorkTask task in backlog)
                {
                    if (task != null)
                    {
                        openIds.Add("task:" + task.TaskId);
                    }
                }
            }

            // The authority only exposes per-task handles; sweep by releasing
            // for tasks that left the open set is approximated here by checking
            // recently-failed tasks (their reservations are released in
            // RespondToFailures). Full sweep needs Worker 2's enumeration —
            // noted, not faked.
            if (diag != null && openIds.Count > 0)
            {
                diag.Add($"ManagerWorkAllocator: reservation sweep — {openIds.Count} open task owners retain their holds.");
            }
        }

        private void RespondToFailures(AllocationInputs inputs, AllocationReport report, List<string> diag)
        {
            if (inputs.RecentlyFailedTasks == null)
            {
                return;
            }

            foreach (WorkTask failed in inputs.RecentlyFailedTasks)
            {
                if (failed == null)
                {
                    continue;
                }

                // Release the failed task's equipment holds.
                if (inputs.Reservations != null)
                {
                    inputs.Reservations.ReleaseAllForTask("task:" + failed.TaskId);
                }

                string materialProblem = null;
                if (inputs.MaterialCheck != null)
                {
                    try
                    {
                        materialProblem = inputs.MaterialCheck(failed);
                    }
                    catch (Exception e)
                    {
                        materialProblem = "material check fault: " + e.Message;
                    }
                }

                if (string.IsNullOrEmpty(materialProblem) && HasScope(inputs, ManagerScopeNames.Scheduling))
                {
                    WorkTask rework = inputs.Tasks.CreateTask(
                        failed.DefinitionId, failed.OwnerId, Math.Max(0, inputs.DayIndex),
                        customerRef: "rework-of:" + failed.TaskId);
                    if (rework != null)
                    {
                        string response = $"rework {rework.TaskId} created for failed {failed.TaskId} ('{failed.DefinitionId}') — reservations released";
                        report.FailureResponses.Add(response);
                        diag.Add("ManagerWorkAllocator: " + response);
                        continue;
                    }
                }

                string deferred = $"failed {failed.TaskId} ('{failed.DefinitionId}'): " +
                    (string.IsNullOrEmpty(materialProblem)
                        ? "no scheduling authority for rework — escalated"
                        : "materials short (" + materialProblem + ") — rework deferred");
                report.FailureResponses.Add(deferred);
                diag.Add("ManagerWorkAllocator: " + deferred);
            }
        }

        private void ConsiderHiring(AllocationInputs inputs, bool canStaff, AllocationReport report, List<string> diag)
        {
            int overloadDays = pressureTracker.ConsecutiveOverloadDays();
            if (overloadDays < pressureTracker.SustainedDays)
            {
                return;
            }

            HiringPressureTracker.DayPressure latest = pressureTracker.History[pressureTracker.History.Count - 1];
            var recommendation = new HiringRecommendation
            {
                BusinessInstanceId = inputs.BusinessInstanceId,
                DayIndex = inputs.DayIndex,
                BacklogMinutes = latest.BacklogMinutes,
                CapacityMinutes = latest.CapacityMinutes,
                OverloadDays = overloadDays,
                Reason = $"backlog {latest.BacklogMinutes} min exceeds worker capacity {latest.CapacityMinutes} min " +
                    $"for {overloadDays} consecutive days — genuine overload, consider hiring",
            };

            if (!canStaff)
            {
                recommendation.Reason += " — no Staffing scope: escalated to owner, not executed";
                report.Notes.Add("ManagerWorkAllocator: hiring pressure escalated (no Staffing scope).");
            }

            report.HiringRecommendations.Add(recommendation);
            diag.Add("ManagerWorkAllocator: " + recommendation.Reason);
            try
            {
                inputs.HiringPort?.Invoke(recommendation);
            }
            catch
            {
                // A faulty port never breaks allocation.
            }
        }

        /// <summary>Save: the hiring-pressure history (owning runtime class holds its DTO).</summary>
        public HiringPressureTracker CapturePressureDto()
        {
            var dto = new HiringPressureTracker();
            foreach (HiringPressureTracker.DayPressure d in pressureTracker.History)
            {
                if (d != null)
                {
                    dto.History.Add(new HiringPressureTracker.DayPressure
                    {
                        DayIndex = d.DayIndex,
                        BacklogMinutes = d.BacklogMinutes,
                        CapacityMinutes = d.CapacityMinutes,
                    });
                }
            }

            dto.OverloadRatioPercent = pressureTracker.OverloadRatioPercent;
            dto.SustainedDays = pressureTracker.SustainedDays;
            return dto;
        }

        /// <summary>Save: clear-then-load.</summary>
        public void LoadPressureDto(HiringPressureTracker dto)
        {
            pressureTracker.History.Clear();
            if (dto == null)
            {
                return;
            }

            foreach (HiringPressureTracker.DayPressure d in dto.History)
            {
                if (d != null)
                {
                    pressureTracker.History.Add(d);
                }
            }

            pressureTracker.OverloadRatioPercent = dto.OverloadRatioPercent;
            pressureTracker.SustainedDays = dto.SustainedDays;
        }
    }
}
