using System;
using System.Collections.Generic;

namespace LandLedgers.Reporting
{
    /// <summary>
    /// Pass IV (Worker 4, Phase Q): one business's live work picture,
    /// production-backed. Open/active/waiting tasks, workers on the floor,
    /// labor demand, equipment occupancy, shortages, today's outputs and
    /// failures — all derived from the task authority, the manager allocator
    /// reports, and the equipment reservation authority. The bounded history
    /// keeps the last 32 significant work events, never a tick log.
    ///
    /// Observer-visibility: call <see cref="RedactForObserver"/>. The work
    /// floor (tasks, workers, occupancy) is visible to anyone present at the
    /// business location or to the owner/manager. Financials (cash position,
    /// wages paid, margins) are visible ONLY to the owner and delegated
    /// managers — never to a competitor standing in the shop.
    /// </summary>
    [Serializable]
    public sealed class BusinessWorkReadModel
    {
        public const int MaxHistory = 32;

        public string BusinessInstanceId = string.Empty;
        public string DisplayName = string.Empty;
        public int AsOfDay = -1;
        public int AsOfMinuteOfDay;

        public List<TaskRow> OpenTasks = new List<TaskRow>();
        public List<TaskRow> ActiveTasks = new List<TaskRow>();
        public List<TaskRow> WaitingTasks = new List<TaskRow>();
        public List<WorkerRow> Workers = new List<WorkerRow>();
        /// <summary>Unassigned work, in minutes, by reason.</summary>
        public List<LaborDemandRow> LaborDemand = new List<LaborDemandRow>();
        /// <summary>Equipment key → occupied minutes today.</summary>
        public Dictionary<string, int> EquipmentOccupancyMinutes = new Dictionary<string, int>();
        public List<string> Shortages = new List<string>();
        /// <summary>Item → units produced today.</summary>
        public Dictionary<string, int> OutputsToday = new Dictionary<string, int>();
        public List<string> Failures = new List<string>();
        // --- financials: owner/manager eyes only ---
        public int CashCents = -1; // -1 = not disclosed
        public int WagesPaidCentsToday = -1;
        public List<WorkEvent> RecentHistory = new List<WorkEvent>();
        public bool Redacted;

        /// <summary>Returns a copy filtered by the observer-visibility rule.</summary>
        public BusinessWorkReadModel RedactForObserver(ObserverContext observer)
        {
            var copy = new BusinessWorkReadModel
            {
                BusinessInstanceId = BusinessInstanceId,
                DisplayName = DisplayName,
                AsOfDay = AsOfDay,
                AsOfMinuteOfDay = AsOfMinuteOfDay,
            };

            bool seesFloor = observer != null && ObserverVisibility.CanSeeBusinessFloor(
                observer.ObserverPersonId, BusinessInstanceId,
                observer.ObserverLocationId, BusinessLocationId,
                observer.OwnerPersonIdOfBusiness, observer.IsDelegatedManagerOfBusiness);
            bool seesFinancials = observer != null && ObserverVisibility.CanSeeBusinessFinancials(
                observer.ObserverPersonId, BusinessInstanceId,
                observer.OwnerPersonIdOfBusiness, observer.IsDelegatedManagerOfBusiness);

            if (seesFloor)
            {
                copy.OpenTasks.AddRange(OpenTasks);
                copy.ActiveTasks.AddRange(ActiveTasks);
                copy.WaitingTasks.AddRange(WaitingTasks);
                copy.Workers.AddRange(Workers);
                copy.LaborDemand.AddRange(LaborDemand);
                foreach (var kv in EquipmentOccupancyMinutes) copy.EquipmentOccupancyMinutes[kv.Key] = kv.Value;
                copy.Shortages.AddRange(Shortages);
                foreach (var kv in OutputsToday) copy.OutputsToday[kv.Key] = kv.Value;
                copy.Failures.AddRange(Failures);
            }
            else
            {
                copy.Redacted = true;
            }

            if (seesFinancials)
            {
                copy.CashCents = CashCents;
                copy.WagesPaidCentsToday = WagesPaidCentsToday;
            }

            // History redacts like the live fields it summarizes.
            foreach (WorkEvent e in RecentHistory)
            {
                if (e == null)
                {
                    continue;
                }

                bool financial = e.IsFinancial;
                if ((financial && seesFinancials) || (!financial && seesFloor))
                {
                    copy.RecentHistory.Add(e);
                }
            }

            if (!seesFinancials)
            {
                copy.Redacted = true;
            }

            return copy;
        }

        /// <summary>Business premises location (not exposed to observers).</summary>
        public string BusinessLocationId = string.Empty;
    }

    [Serializable]
    public sealed class TaskRow
    {
        public string TaskId = string.Empty;
        public string DefinitionId = string.Empty;
        public string Status = string.Empty;
        public int AssigneePersonId = -1;
        public string Priority = string.Empty;
        public int PlannedMinutes;
        public int ProgressPercent;
        public string Blocker = string.Empty;
    }

    [Serializable]
    public sealed class WorkerRow
    {
        public int PersonId = -1;
        public string DisplayName = string.Empty;
        public string CurrentTaskId = string.Empty;
        public int MinutesWorkedToday;
    }

    [Serializable]
    public sealed class LaborDemandRow
    {
        public int UnassignedMinutes;
        public string Reason = string.Empty;
    }

    /// <summary>One significant work event — bounded, never a tick log.</summary>
    [Serializable]
    public sealed class WorkEvent
    {
        public int DayIndex;
        public int MinuteOfDay;
        public string Kind = string.Empty; // assigned | completed | failed | deferred | hired | paid
        public string Summary = string.Empty;
        public bool IsFinancial;
    }

    /// <summary>Builds <see cref="BusinessWorkReadModel"/> from live authorities.</summary>
    public sealed class BusinessWorkReadModelBuilder
    {
        public Func<string, string> DisplayNameOf;
        public Func<string, string> LocationOf;
        public Func<string, int, List<TaskRow>> TasksOf; // (businessId, dayIndex)
        public Func<string, int, List<WorkerRow>> WorkersOf;
        public Func<string, int, List<LaborDemandRow>> DemandOf;
        public Func<string, int, Dictionary<string, int>> EquipmentOccupancyOf;
        public Func<string, int, List<string>> ShortagesOf;
        public Func<string, int, Dictionary<string, int>> OutputsOf;
        public Func<string, int, List<string>> FailuresOf;
        public Func<string, int> CashCentsOf;
        public Func<string, int, int> WagesPaidTodayOf;
        public Func<string, List<WorkEvent>> HistoryOf;

        public BusinessWorkReadModel Build(string businessInstanceId, int dayIndex, int minuteOfDay)
        {
            var model = new BusinessWorkReadModel
            {
                BusinessInstanceId = businessInstanceId,
                AsOfDay = dayIndex,
                AsOfMinuteOfDay = minuteOfDay,
                BusinessLocationId = LocationOf != null ? LocationOf(businessInstanceId) ?? string.Empty : string.Empty,
            };
            model.DisplayName = DisplayNameOf != null ? DisplayNameOf(businessInstanceId) ?? string.Empty : string.Empty;

            if (TasksOf != null)
            {
                foreach (TaskRow row in TasksOf(businessInstanceId, dayIndex) ?? new List<TaskRow>())
                {
                    if (row == null)
                    {
                        continue;
                    }

                    switch (row.Status)
                    {
                        case "Active":
                        case "InProgress":
                            model.ActiveTasks.Add(row);
                            break;
                        case "Waiting":
                        case "Blocked":
                            model.WaitingTasks.Add(row);
                            break;
                        default:
                            model.OpenTasks.Add(row);
                            break;
                    }
                }
            }

            if (WorkersOf != null) model.Workers.AddRange(WorkersOf(businessInstanceId, dayIndex) ?? new List<WorkerRow>());
            if (DemandOf != null) model.LaborDemand.AddRange(DemandOf(businessInstanceId, dayIndex) ?? new List<LaborDemandRow>());
            if (EquipmentOccupancyOf != null)
            {
                foreach (var kv in EquipmentOccupancyOf(businessInstanceId, dayIndex) ?? new Dictionary<string, int>())
                {
                    model.EquipmentOccupancyMinutes[kv.Key] = kv.Value;
                }
            }

            if (ShortagesOf != null) model.Shortages.AddRange(ShortagesOf(businessInstanceId, dayIndex) ?? new List<string>());
            if (OutputsOf != null)
            {
                foreach (var kv in OutputsOf(businessInstanceId, dayIndex) ?? new Dictionary<string, int>())
                {
                    model.OutputsToday[kv.Key] = kv.Value;
                }
            }

            if (FailuresOf != null) model.Failures.AddRange(FailuresOf(businessInstanceId, dayIndex) ?? new List<string>());
            model.CashCents = CashCentsOf != null ? CashCentsOf(businessInstanceId) : -1;
            model.WagesPaidCentsToday = WagesPaidTodayOf != null ? WagesPaidTodayOf(businessInstanceId, dayIndex) : -1;

            List<WorkEvent> history = HistoryOf != null ? HistoryOf(businessInstanceId) : null;
            if (history != null)
            {
                int start = Math.Max(0, history.Count - BusinessWorkReadModel.MaxHistory);
                for (int i = start; i < history.Count; i++)
                {
                    if (history[i] != null)
                    {
                        model.RecentHistory.Add(history[i]);
                    }
                }
            }

            return model;
        }
    }
}
