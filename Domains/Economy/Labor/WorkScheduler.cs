// WorkScheduler — Phase E: daily work scheduling (brief section 8).
//
// Canon: CAN-CE1-03.2 (occupation is DESCRIPTIVE, never an action-permission
// gate: skill evidence scales effectiveness but never refuses assignment),
// CAN-CE1-03.5 (refusal, fatigue, danger, contract restrictions stay
// meaningful), CAN-CE1-03.6 (one worker, one exclusive activity at a time).
// Pure logic over SchedulerInputs/SchedulerAvailableWorker/SchedulerOpenWorkItem
// from LaborContracts.cs. The integrator binds this to TaskAuthority
// (real open tasks), WorkTimeBudgetStore (real person-time), and
// PersonScheduleTracker (work windows); this class commits minutes against the
// caller-supplied budgets and never invents a worker.
using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Labor
{
    /// <summary>
    /// Assigns each day's open work to available workers. Deterministic:
    /// open work is processed in listed priority order, first-fit worker.
    /// </summary>
    public sealed class WorkScheduler
    {
        /// <summary>
        /// Assigns daily work. Equipment is not gated in this overload
        /// (equipmentCheck null = no gate); see the 3-argument overload.
        /// </summary>
        public WorkAssignmentReport AssignDailyWork(SchedulerInputs inputs, List<string> diag)
        {
            return AssignDailyWork(inputs, null, diag);
        }

        /// <summary>
        /// Assigns daily work with an optional equipment gate. When
        /// <paramref name="equipmentCheck"/> is non-null, a work item whose
        /// RequiredEquipment is set is only given to a worker for whom
        /// equipmentCheck(equipmentId, personId) is true. Null = no gate.
        /// Danger flags and work windows are NOT fields on the scheduler
        /// DTOs; the integrator applies them via TaskAuthority and
        /// PersonScheduleTracker (no supplementary DTO was needed here).
        /// </summary>
        public WorkAssignmentReport AssignDailyWork(SchedulerInputs inputs,
            Func<string, int, bool> equipmentCheck, List<string> diag)
        {
            var report = new WorkAssignmentReport();
            if (inputs == null)
            {
                if (diag != null) diag.Add("WorkScheduler: null inputs; nothing assigned");
                return report;
            }
            report.BusinessInstanceId = inputs.BusinessInstanceId;
            report.DayIndex = inputs.DayIndex;

            List<SchedulerOpenWorkItem> work = inputs.OpenWork ?? new List<SchedulerOpenWorkItem>();
            List<SchedulerAvailableWorker> workers = inputs.Workers ?? new List<SchedulerAvailableWorker>();

            var committed = new Dictionary<int, int>();
            foreach (var w in workers)
            {
                if (w == null) continue;
                if (!committed.ContainsKey(w.PersonId)) committed[w.PersonId] = 0;
            }

            if (diag != null) diag.Add(string.Format("WorkScheduler: {0} open item(s), {1} worker(s), day {2}",
                work.Count, workers.Count, inputs.DayIndex));

            // Iterate open work by priority (as listed).
            foreach (var item in work)
            {
                if (item == null || string.IsNullOrEmpty(item.TaskId))
                {
                    if (diag != null) diag.Add("WorkScheduler: skipping invalid work item (null/empty TaskId)");
                    continue;
                }
                if (item.MinutesRequired <= 0)
                {
                    report.Assignments.Add(new WorkAssignment
                    {
                        TaskId = item.TaskId,
                        PersonId = -1,
                        MinutesCommitted = 0,
                        Note = "no minutes required; nothing to assign"
                    });
                    report.UnassignedTaskIds.Add(item.TaskId);
                    continue;
                }

                SchedulerAvailableWorker chosen = null;
                var skipReasons = new List<string>();

                foreach (var worker in workers)
                {
                    if (worker == null) continue;
                    int remaining = worker.AvailableMinutes - committed[worker.PersonId];

                    // Person-time exclusivity: one worker never gets two
                    // overlapping assignments (CAN-CE1-03.6).
                    if (remaining < item.MinutesRequired)
                    {
                        skipReasons.Add(string.Format("P{0} only {1}m remaining (< {2}m)", worker.PersonId, remaining, item.MinutesRequired));
                        continue;
                    }

                    // Fatigue and contract restrictions are meaningful
                    // (CAN-CE1-03.5); danger is checked by the integrator via
                    // TaskAuthority, which owns workplace danger flags.
                    var willDiag = new List<string>();
                    if (!PersonJobDecisions.WillWorkToday(worker.PersonId, worker.Fatigued, null, worker.ContractRestrictions, willDiag))
                    {
                        skipReasons.Add(string.Format("P{0} unwilling ({1})", worker.PersonId, willDiag.Count > 0 ? willDiag[0] : "refused"));
                        continue;
                    }

                    // Equipment gate: optional and explicit, never a hidden default.
                    if (!string.IsNullOrEmpty(item.RequiredEquipment) && equipmentCheck != null
                        && !equipmentCheck(item.RequiredEquipment, worker.PersonId))
                    {
                        skipReasons.Add(string.Format("P{0} lacks required equipment '{1}'", worker.PersonId, item.RequiredEquipment));
                        continue;
                    }

                    chosen = worker;
                    break; // first-fit in listed worker order; deterministic
                }

                if (chosen != null)
                {
                    committed[chosen.PersonId] += item.MinutesRequired;
                    report.Assignments.Add(new WorkAssignment
                    {
                        TaskId = item.TaskId,
                        PersonId = chosen.PersonId,
                        MinutesCommitted = item.MinutesRequired,
                        Note = EffectivenessNote(chosen, item)
                    });
                    if (diag != null) diag.Add(string.Format("assigned {0} ({1}, {2}m) to P{3}",
                        item.TaskId, item.Family, item.MinutesRequired, chosen.PersonId));
                }
                else
                {
                    // NEVER invent a worker: PersonId -1 with reasons.
                    string reason = skipReasons.Count > 0
                        ? "unassignable: " + string.Join("; ", skipReasons.ToArray())
                        : "unassignable: no workers available";
                    report.Assignments.Add(new WorkAssignment
                    {
                        TaskId = item.TaskId,
                        PersonId = -1,
                        MinutesCommitted = 0,
                        Note = reason
                    });
                    report.UnassignedTaskIds.Add(item.TaskId);
                    if (diag != null) diag.Add(string.Format("{0} unassigned: {1}", item.TaskId, reason));
                }
            }

            return report;
        }

        /// <summary>
        /// Skill evidence is DESCRIPTIVE only (CAN-CE1-03.2): a novice CAN be
        /// assigned unfamiliar work. The note records reduced effectiveness;
        /// refusal on skill grounds never happens here.
        /// </summary>
        private static string EffectivenessNote(SchedulerAvailableWorker worker, SchedulerOpenWorkItem item)
        {
            string family = item.Family ?? "?";
            if (worker.SkillEvidence != null && worker.SkillEvidence.TryGetValue(family, out float e))
            {
                if (e >= 0.6f) return string.Format("skill evidence {0:0.00} in {1}; full effectiveness", e, family);
                if (e >= 0.3f) return string.Format("skill evidence {0:0.00} in {1}; reduced effectiveness", e, family);
                return string.Format("novice in {0} (evidence {1:0.00}); assignable, slower", family, e);
            }
            return string.Format("no skill evidence in {0}; assigned as novice, slower", family);
        }
    }
}
