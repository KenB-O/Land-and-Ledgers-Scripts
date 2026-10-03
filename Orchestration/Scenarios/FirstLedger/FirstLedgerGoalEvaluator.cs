using System;
using System.Collections.Generic;

namespace LandLedgers.Orchestration.Scenarios.FirstLedger
{
    /// <summary>
    /// BIZ-6: live game state the First Ledger goal ladder reads. The Unity side
    /// implements this against the real managers (business registry, PKG-6
    /// EmploymentRelationship registry, BIZ-5 valuation read model).
    /// </summary>
    public interface IFirstLedgerGameState
    {
        /// <summary>Businesses the player owns (any ownership share counts).</summary>
        int PlayerOwnedBusinessCount { get; }

        /// <summary>Active wage employments at the player's businesses.</summary>
        int ActivePlayerEmployeeCount { get; }

        /// <summary>
        /// Player's total owner equity in cents — MUST come from the BIZ-5
        /// EnterpriseValuationReadModel (OwnerEquityValue), never cash-in-business
        /// (Canon §11.4).
        /// </summary>
        int PlayerOwnerEquityCents { get; }
    }

    /// <summary>
    /// BIZ-6: live evaluation of the First Ledger goal ladder against game state.
    /// Goals complete in ladder order; each completion is reported once. The scenario
    /// is complete when the final equity goal completes.
    /// </summary>
    public sealed class FirstLedgerGoalEvaluator
    {
        private readonly HashSet<string> completedGoalIds = new HashSet<string>();

        /// <summary>Raised once per goal, in ladder order, when it completes.</summary>
        public event Action<string> GoalCompleted;

        public IReadOnlyCollection<string> CompletedGoalIds => completedGoalIds;

        public bool IsScenarioComplete => completedGoalIds.Contains("equity-15000");

        /// <summary>
        /// Evaluates the ladder against current state. Returns the goal ids that
        /// completed on THIS call (in ladder order).
        /// </summary>
        public List<string> Evaluate(IFirstLedgerGameState state)
        {
            var newlyCompleted = new List<string>();
            if (state == null)
            {
                return newlyCompleted;
            }

            Check("acquire-first-business", state.PlayerOwnedBusinessCount >= 1, newlyCompleted);
            Check("hire-first-employee", state.ActivePlayerEmployeeCount >= 1, newlyCompleted);
            Check("equity-5000", state.PlayerOwnerEquityCents >= FirstLedgerScenario.EquityThresholdsCents[0], newlyCompleted);
            Check("own-two-businesses", state.PlayerOwnedBusinessCount >= 2, newlyCompleted);
            Check("three-employees", state.ActivePlayerEmployeeCount >= 3, newlyCompleted);
            Check("equity-10000", state.PlayerOwnerEquityCents >= FirstLedgerScenario.EquityThresholdsCents[1], newlyCompleted);
            Check("own-three-businesses", state.PlayerOwnedBusinessCount >= 3, newlyCompleted);
            Check("equity-15000", state.PlayerOwnerEquityCents >= FirstLedgerScenario.EquityThresholdsCents[2], newlyCompleted);

            return newlyCompleted;
        }

        /// <summary>Progress summary for the HUD: completed count + next goal id.</summary>
        public (int completed, int total, string nextGoalId) Progress()
        {
            foreach (string goalId in FirstLedgerScenario.GoalIdsInOrder)
            {
                if (!completedGoalIds.Contains(goalId))
                {
                    return (completedGoalIds.Count, FirstLedgerScenario.GoalIdsInOrder.Length, goalId);
                }
            }

            return (completedGoalIds.Count, FirstLedgerScenario.GoalIdsInOrder.Length, null);
        }

        private void Check(string goalId, bool satisfied, List<string> newlyCompleted)
        {
            if (satisfied && completedGoalIds.Add(goalId))
            {
                newlyCompleted.Add(goalId);
                GoalCompleted?.Invoke(goalId);
            }
        }
    }
}
