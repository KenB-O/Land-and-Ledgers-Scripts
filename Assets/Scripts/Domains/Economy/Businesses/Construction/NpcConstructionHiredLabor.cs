using System;
using System.Collections.Generic;
using LandLedgers.Population;
using LandLedgers.Tasks;
using LandLedgers.Time;

namespace LandLedgers.Economy
{
    /// <summary>
    /// D-3: the hired-labour leg of one construction package, through the
    /// real employment and task authorities. The labour port tries the
    /// commissioning household's own members first; when none of them can
    /// work, this helper tries hired candidates in the order supplied.
    /// Workers already employed on this package are tried FIRST, under
    /// their existing employment (CommitHiredLabor) — a worker is never
    /// re-registered for the same package on a later day. Fresh candidates
    /// are hired through HireWorker at the agreed weekly wage. The first
    /// candidate whose time commits works the committed task through the
    /// executor, exactly like own labour; minutes credit only as worked.
    /// Wages are NOT paid here — the driver pays hired wages when the
    /// package completes (PayWorkerForPackage) and ends the employments.
    /// A candidate whose household is unknown is never hired: their wages
    /// would have no payee. The player household is never a candidate —
    /// the caller excludes it when enumerating, never here.
    /// </summary>
    public static class NpcConstructionHiredLabor
    {
        /// <summary>
        /// D-3: the agreed weekly wage for a hired construction labourer.
        /// 750c/week is the in-repo skilled-trade precedent (miller,
        /// AutonomousSocietyDriver.cs:602; sawmill logger, C-1). Provisional
        /// calibration, not doctrine — Agent F owns wage rates, and a
        /// negotiated or role-specific wage replaces this when one exists.
        /// </summary>
        public const int HiredConstructionWeeklyWageCents = 750;

        /// <summary>
        /// Tries each candidate in order until one works the package.
        /// Returns null on success (the candidate's time was committed and
        /// worked through the executor), or the last refusal when no
        /// candidate could work. Never invents a worker, never credits
        /// minutes not worked, never hires a candidate whose household is
        /// unknown.
        /// </summary>
        public static string TryWorkPackage(
            NpcConstructionCommissioner commissioner,
            EmploymentRelationshipRegistry employments,
            ConstructionProjectExecutor executor,
            ConstructionProject project,
            NpcConstructionRecord record,
            WorkPackage package,
            IReadOnlyList<int> candidatePersonIds,
            Func<int, int> householdOfPerson,
            int weeklyWageCents,
            PersonScheduleTracker tracker,
            IHouseholdToolCustody custody,
            TaskAuthority tasks,
            WorkTimeBudgetStore budgets,
            int dayIndex,
            int startMinuteOfDay,
            int durationMinutes,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (commissioner == null || employments == null || executor == null
                || project == null || record == null || package == null)
                return "NpcConstructionHiredLabor: commissioner, employments, executor, project, record and package required.";
            if (candidatePersonIds == null || candidatePersonIds.Count == 0)
                return "no hired candidate supplied — nobody outside the household to hire.";
            NpcBuildPackageFunding funding = record.FindFunding(package.PackageId);

            // Workers already employed on this package first, in funding
            // order, under their existing employment — never a duplicate
            // registration for the same worker and package.
            var tried = new HashSet<int>();
            var ordered = new List<int>();
            if (funding != null && funding.EmploymentIds != null)
            {
                foreach (string employmentId in funding.EmploymentIds)
                {
                    if (string.IsNullOrWhiteSpace(employmentId)) continue;
                    if (!employments.TryGetById(employmentId, out EmploymentRelationship relationship)
                        || relationship == null || !relationship.IsActive) continue;
                    int personId = relationship.EmployeePersonId;
                    if (personId < 0 || tried.Contains(personId)) continue;
                    tried.Add(personId);
                    ordered.Add(personId);
                }
            }
            foreach (int personId in candidatePersonIds)
            {
                if (personId < 0 || tried.Contains(personId)) continue;
                tried.Add(personId);
                ordered.Add(personId);
            }
            if (ordered.Count == 0)
                return "no hired candidate supplied — nobody outside the household to hire.";

            string lastRefusal = "no hired candidate could work the package.";
            foreach (int personId in ordered)
            {
                string employmentId = FindActiveEmploymentForPerson(funding, employments, personId);
                string commit;
                if (employmentId != null)
                {
                    commit = commissioner.CommitHiredLabor(employments, executor, project, record,
                        package, employmentId, tracker, custody, tasks, budgets,
                        dayIndex, startMinuteOfDay, durationMinutes, diag);
                }
                else
                {
                    int workerHouseholdId = householdOfPerson != null ? householdOfPerson(personId) : -1;
                    if (workerHouseholdId < 0)
                    {
                        lastRefusal = $"P{personId} has no household to pay wages to — not hired.";
                        diag.Add("NpcConstructionHiredLabor: " + lastRefusal);
                        continue;
                    }
                    commit = commissioner.HireWorker(employments, executor, project, record,
                        package, personId, workerHouseholdId, $"household:{project.HouseholdId} construction",
                        weeklyWageCents, tracker, custody, tasks, budgets,
                        dayIndex, startMinuteOfDay, durationMinutes, diag);
                }
                if (commit != null) { lastRefusal = commit; continue; }
                string taskId = null;
                for (int i = project.LaborTaskBindings.Count - 1; i >= 0; i--)
                {
                    LaborTaskBinding binding = project.LaborTaskBindings[i];
                    if (binding != null && string.Equals(binding.PackageId, package.PackageId, StringComparison.Ordinal))
                    {
                        taskId = binding.TaskId;
                        break;
                    }
                }
                if (string.IsNullOrWhiteSpace(taskId))
                    return "no labor task bound to the package.";
                return executor.WorkCommittedLabor(project, package, taskId,
                    durationMinutes, tasks, budgets, dayIndex, diag);
            }
            return lastRefusal;
        }

        private static string FindActiveEmploymentForPerson(NpcBuildPackageFunding funding,
            EmploymentRelationshipRegistry employments, int personId)
        {
            if (funding == null || funding.EmploymentIds == null || employments == null) return null;
            foreach (string employmentId in funding.EmploymentIds)
            {
                if (string.IsNullOrWhiteSpace(employmentId)) continue;
                if (!employments.TryGetById(employmentId, out EmploymentRelationship relationship)
                    || relationship == null || !relationship.IsActive) continue;
                if (relationship.EmployeePersonId == personId) return employmentId;
            }
            return null;
        }
    }
}
