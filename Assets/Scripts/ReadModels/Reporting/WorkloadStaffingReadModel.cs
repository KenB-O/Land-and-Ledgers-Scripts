using System;
using System.Collections.Generic;

namespace LandLedgers.Reporting
{
    /// <summary>
    /// Pass XII Worker C (LL-PLAY-012) — staffing explained by WORKLOAD,
    /// never by a quota. The October 9 playtest showed "Staff 1/2" with
    /// no explanation of what the numbers meant or why the business
    /// needed that staffing. That figure came from template slots
    /// (BusinessRuntimeState.RequiredWorkerCount /
    /// ActiveRequiredWorkerCount — slots flagged RequiredForOpening),
    /// which are a hiring-template artefact, not a measure of work.
    /// This read model replaces it for display: demand is remaining
    /// minutes of real open tasks, coverage is assigned minutes over
    /// demand, unfilled shifts are tasks with no assignee, and wage
    /// obligation comes from employment/payroll rows.
    ///
    /// CONTRACT FOR WORKER D / CODEX (display, not authority):
    /// - Show TotalDemandMinutes, AssignedMinutes, Coverage01 and
    ///   UnfilledShifts with the Explanation text — never show a bare
    ///   "filled/required" slot fraction as staffing truth. The slot
    ///   counts remain only as a business-opening check, not a
    ///   staffing display.
    /// - LaborDemandByReason explains WHY work is unassigned; it is
    ///   NOT additive to TotalDemandMinutes (the allocator's
    ///   unassigned minutes and the tasks' own remaining minutes
    ///   describe the same unassigned work from two sides — summing
    ///   them would double-count it).
    /// - Availability, skills and equipment come from the caller's
    ///   authorities (schedule, task definitions, reservations). This
    ///   model never invents them: unknown availability reports -1
    ///   and a note, tasks with a skill requirement but no supplied
    ///   skill evidence report "match unknown", and equipment appears
    ///   only as occupancy minutes plus blocker text.
    /// - Wage figures are obligations (what the business owes for the
    ///   week), split into current vs arrears (suspended). They are
    ///   not "wages paid" — paid cash lives in payroll settlement.
    /// - This model makes no hiring decision and sets no staffing
    ///   requirement: it describes the work and the workers on
    ///   record, and leaves decisions to the owner/manager.
    /// </summary>
    [Serializable]
    public sealed class WorkloadTaskInput
    {
        public string TaskId = string.Empty;
        public string Status = string.Empty;
        public int PlannedMinutes;
        public int ProgressPercent;
        public int AssigneePersonId = -1;
        public string Blocker = string.Empty;
        public string RequiredSkill = string.Empty;
        public string StationOrEquipmentKey = string.Empty;
    }

    [Serializable]
    public sealed class WorkloadWorkerInput
    {
        public int PersonId = -1;
        public string DisplayName = string.Empty;
        public int MinutesWorkedToday;
        public int AvailableMinutes = -1;
        public string CurrentTaskId = string.Empty;
        public string ConflictNote = string.Empty;
    }

    [Serializable]
    public sealed class WorkloadLaborDemandInput
    {
        public string Reason = string.Empty;
        public int UnassignedMinutes;
    }

    [Serializable]
    public sealed class WorkloadWageInput
    {
        public string EmploymentId = string.Empty;
        public int PersonId = -1;
        public int WeeklyWageCents;
        public bool IsSuspended;
    }

    [Serializable]
    public sealed class WorkloadWorkerRow
    {
        public int PersonId = -1;
        public string DisplayName = string.Empty;
        public int MinutesWorkedToday;
        public int AvailableMinutes = -1;
        public string CurrentTaskId = string.Empty;
        public string ConflictNote = string.Empty;
        /// <summary>Worked / available; -1 when availability is unknown. Above 1 means overtime, not an error.</summary>
        public float Utilization01 = -1f;
    }

    [Serializable]
    public sealed class WorkloadStaffingReadModel
    {
        public string BusinessId = string.Empty;
        public int AsOfDay = -1;

        /// <summary>Remaining minutes of open tasks (planned x uncompleted share).</summary>
        public int TotalDemandMinutes;
        /// <summary>Demand expressed in full-time equivalents at the
        /// existing LaborDemandPlan convention of 600 workday minutes
        /// (not a headcount target — a scale for the minute demand).</summary>
        public float DemandFullTimeEquivalents;
        public int AssignedMinutes;
        public int UnassignedMinutes;
        public float Coverage01 = 1f;
        public int AssignedTaskCount;
        public int UnassignedTaskCount;
        public int BlockedTaskCount;
        public int ExcludedCompletedTaskCount;
        public List<string> Blockers = new List<string>();
        public List<WorkloadWorkerRow> Workers = new List<WorkloadWorkerRow>();
        /// <summary>Sum of known availability; -1 when any worker's availability is unknown.</summary>
        public int TotalAvailableMinutes = -1;
        public int WorkersWithUnknownAvailability;
        public int TotalWorkedMinutes;
        /// <summary>Unfilled shifts = tasks with no assignee. Never a quota.</summary>
        public int UnfilledShifts;
        /// <summary>Explanatory only — see the contract note on the model. Never add to TotalDemandMinutes.</summary>
        public List<WorkloadLaborDemandInput> LaborDemandByReason = new List<WorkloadLaborDemandInput>();
        public Dictionary<string, int> EquipmentOccupancyMinutes = new Dictionary<string, int>();
        public int WageObligationCents;
        public int WageCurrentCents;
        public int WageArrearsCents;
        public int TasksWithSkillRequirementButNoEvidence;
        public List<string> Notes = new List<string>();
        public string Explanation = string.Empty;

        public static WorkloadStaffingReadModel Build(
            string businessId,
            int dayIndex,
            IReadOnlyList<WorkloadTaskInput> tasks,
            IReadOnlyList<WorkloadWorkerInput> workers,
            IReadOnlyList<WorkloadLaborDemandInput> laborDemand,
            IReadOnlyDictionary<string, int> equipmentOccupancy,
            IReadOnlyList<WorkloadWageInput> wages)
        {
            var model = new WorkloadStaffingReadModel
            {
                BusinessId = businessId ?? string.Empty,
                AsOfDay = dayIndex,
            };

            if (tasks != null)
            {
                for (int i = 0; i < tasks.Count; i++)
                {
                    WorkloadTaskInput task = tasks[i];
                    if (task == null)
                    {
                        continue;
                    }

                    string status = (task.Status ?? string.Empty).Trim();
                    if (string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(status, "Complete", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(status, "Done", StringComparison.OrdinalIgnoreCase))
                    {
                        model.ExcludedCompletedTaskCount++;
                        continue;
                    }

                    int planned = Math.Max(0, task.PlannedMinutes);
                    int progress = Math.Max(0, Math.Min(100, task.ProgressPercent));
                    int remaining = (planned * (100 - progress)) / 100;
                    model.TotalDemandMinutes += remaining;
                    if (task.AssigneePersonId >= 0)
                    {
                        model.AssignedMinutes += remaining;
                        model.AssignedTaskCount++;
                    }
                    else
                    {
                        model.UnassignedMinutes += remaining;
                        model.UnassignedTaskCount++;
                    }

                    if (!string.IsNullOrWhiteSpace(task.Blocker))
                    {
                        model.BlockedTaskCount++;
                        model.Blockers.Add(task.Blocker.Trim());
                    }

                    if (!string.IsNullOrWhiteSpace(task.RequiredSkill))
                    {
                        model.TasksWithSkillRequirementButNoEvidence++;
                    }
                }
            }

            model.UnfilledShifts = model.UnassignedTaskCount;
            model.Coverage01 = model.TotalDemandMinutes > 0
                ? (float)model.AssignedMinutes / model.TotalDemandMinutes
                : 1f;
            model.DemandFullTimeEquivalents = model.TotalDemandMinutes / 600f;

            int availableTotal = 0;
            bool anyUnknownAvailability = false;
            if (workers != null)
            {
                for (int i = 0; i < workers.Count; i++)
                {
                    WorkloadWorkerInput worker = workers[i];
                    if (worker == null)
                    {
                        continue;
                    }

                    int worked = Math.Max(0, worker.MinutesWorkedToday);
                    model.TotalWorkedMinutes += worked;
                    var row = new WorkloadWorkerRow
                    {
                        PersonId = worker.PersonId,
                        DisplayName = worker.DisplayName ?? string.Empty,
                        MinutesWorkedToday = worked,
                        AvailableMinutes = worker.AvailableMinutes,
                        CurrentTaskId = worker.CurrentTaskId ?? string.Empty,
                        ConflictNote = worker.ConflictNote ?? string.Empty,
                        Utilization01 = -1f,
                    };
                    if (worker.AvailableMinutes >= 0)
                    {
                        availableTotal += worker.AvailableMinutes;
                        row.Utilization01 = worker.AvailableMinutes > 0
                            ? (float)worked / worker.AvailableMinutes
                            : 0f;
                    }
                    else
                    {
                        anyUnknownAvailability = true;
                        model.WorkersWithUnknownAvailability++;
                    }

                    model.Workers.Add(row);
                }
            }

            model.TotalAvailableMinutes = anyUnknownAvailability ? -1 : availableTotal;

            if (laborDemand != null)
            {
                for (int i = 0; i < laborDemand.Count; i++)
                {
                    WorkloadLaborDemandInput row = laborDemand[i];
                    if (row == null)
                    {
                        continue;
                    }

                    model.LaborDemandByReason.Add(new WorkloadLaborDemandInput
                    {
                        Reason = row.Reason ?? string.Empty,
                        UnassignedMinutes = Math.Max(0, row.UnassignedMinutes),
                    });
                }
            }

            if (equipmentOccupancy != null)
            {
                foreach (KeyValuePair<string, int> kv in equipmentOccupancy)
                {
                    if (!string.IsNullOrWhiteSpace(kv.Key))
                    {
                        model.EquipmentOccupancyMinutes[kv.Key] = Math.Max(0, kv.Value);
                    }
                }
            }

            if (wages != null)
            {
                for (int i = 0; i < wages.Count; i++)
                {
                    WorkloadWageInput wage = wages[i];
                    if (wage == null)
                    {
                        continue;
                    }

                    int amount = Math.Max(0, wage.WeeklyWageCents);
                    model.WageObligationCents += amount;
                    if (wage.IsSuspended)
                    {
                        model.WageArrearsCents += amount;
                    }
                    else
                    {
                        model.WageCurrentCents += amount;
                    }
                }
            }

            if (model.WorkersWithUnknownAvailability > 0)
            {
                model.Notes.Add($"Availability is unknown for {model.WorkersWithUnknownAvailability} worker(s); total available minutes are not stated rather than guessed.");
            }

            if (model.TasksWithSkillRequirementButNoEvidence > 0)
            {
                model.Notes.Add($"{model.TasksWithSkillRequirementButNoEvidence} task(s) record a skill requirement, but no skill evidence was supplied — skill match is unknown, not assumed.");
            }

            if (model.LaborDemandByReason.Count > 0)
            {
                model.Notes.Add("Labor-demand rows explain why work is unassigned; they are not added to demand minutes (that would double-count the same unassigned work).");
            }

            if (model.TotalDemandMinutes <= 0)
            {
                model.Explanation = "No open work is recorded for this business, so no staffing demand arises from workload.";
                if (model.WageObligationCents > 0)
                {
                    model.Explanation += $" Wage obligation {model.WageObligationCents}c, of which {model.WageArrearsCents}c in arrears (suspended).";
                }
            }
            else
            {
                string availability = model.TotalAvailableMinutes >= 0
                    ? $"{model.TotalAvailableMinutes} minutes available"
                    : "availability partly unknown";
                model.Explanation =
                    $"Workload: {model.TotalDemandMinutes} minutes demanded across {model.AssignedTaskCount + model.UnassignedTaskCount} open task(s); " +
                    $"{model.AssignedMinutes} assigned ({model.Coverage01 * 100f:0}% coverage); " +
                    $"{model.UnfilledShifts} unfilled shift(s) ({model.UnassignedMinutes} minutes). " +
                    $"{model.Workers.Count} worker(s) on record, {availability}. " +
                    $"Wage obligation {model.WageObligationCents}c, of which {model.WageArrearsCents}c in arrears (suspended).";
                if (model.BlockedTaskCount > 0)
                {
                    model.Explanation += $" {model.BlockedTaskCount} task(s) blocked.";
                }
            }

            return model;
        }
    }
}
