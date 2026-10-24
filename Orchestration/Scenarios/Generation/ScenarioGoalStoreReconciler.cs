using System;
using System.Collections.Generic;

namespace LandLedgers.Orchestration.Scenarios.Generation
{
    /// <summary>
    /// Pass XI WS1: the single scenario-goal reconciliation rule for the
    /// three goal stores that can diverge across save and load — the
    /// director runtime state, the per-scenario evaluator list, and the
    /// hub objective-service latched list. There is no second scenario
    /// authority: the director stays the active-scenario authority, and
    /// this helper only merges completion sets.
    ///
    /// Rules, applied identically on capture and on restore:
    /// - Completed is the UNION of the three stores, in director, then
    ///   evaluator, then hub order. Reconciliation never un-completes a
    ///   goal: a goal completed in any store stays completed everywhere.
    /// - Failure is NOT merged here. Failure lives only in the hub run
    ///   state (failed scenario and goal ids) and stays sticky there;
    ///   this helper never un-fails a scenario and never invents one.
    /// - Every disagreement is reported as a diagnostic string naming the
    ///   store that lacked the goal, so a divergent save is visible in
    ///   logs instead of silently rewritten.
    /// </summary>
    public static class ScenarioGoalStoreReconciler
    {
        public sealed class Reconciliation
        {
            public List<string> CompletedGoals = new List<string>();
            public List<string> Diagnostics = new List<string>();
        }

        public static Reconciliation Reconcile(
            IEnumerable<string> directorGoals,
            IEnumerable<string> evaluatorGoals,
            IEnumerable<string> hubLatchedGoals)
        {
            var director = ToSet(directorGoals);
            var evaluator = ToSet(evaluatorGoals);
            var hub = ToSet(hubLatchedGoals);

            var result = new Reconciliation();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string goalId in director)
            {
                if (seen.Add(goalId)) result.CompletedGoals.Add(goalId);
            }
            foreach (string goalId in evaluator)
            {
                if (seen.Add(goalId)) result.CompletedGoals.Add(goalId);
            }
            foreach (string goalId in hub)
            {
                if (seen.Add(goalId)) result.CompletedGoals.Add(goalId);
            }

            foreach (string goalId in result.CompletedGoals)
            {
                if (!director.Contains(goalId))
                    result.Diagnostics.Add($"[ScenarioReconcile] director state lacked completed goal '{goalId}' (kept from evaluator/hub).");
                if (!evaluator.Contains(goalId))
                    result.Diagnostics.Add($"[ScenarioReconcile] evaluator list lacked completed goal '{goalId}' (kept from director/hub).");
                if (!hub.Contains(goalId))
                    result.Diagnostics.Add($"[ScenarioReconcile] hub latched list lacked completed goal '{goalId}' (kept from director/evaluator).");
            }
            return result;
        }

        private static List<string> ToSet(IEnumerable<string> goalIds)
        {
            var result = new List<string>();
            if (goalIds == null) return result;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string goalId in goalIds)
            {
                if (string.IsNullOrEmpty(goalId)) continue;
                if (seen.Add(goalId)) result.Add(goalId);
            }
            return result;
        }
    }
}
