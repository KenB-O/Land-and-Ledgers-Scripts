using System;
using System.Collections.Generic;

namespace LandLedgers.Orchestration.Scenarios.SaloonCircuit
{
    /// <summary>
    /// T3B: live game state the Saloon Circuit goal ladder reads. The Unity
    /// side implements this against the real managers (business registry for
    /// ownership, the T2G saloon runtime for sales/traffic, the T2A/SWN-3
    /// liability ledger for scenario debt).
    ///
    /// Provenance honesty (Canon Part XV): "Vertical integration requires a
    /// real provenance chain through physical logistics and customer sale."
    /// A sale only counts as internally supplied when the chain is real.
    /// </summary>
    public interface ISaloonCircuitGameState
    {
        /// <summary>Player's ownership share of a saloon, 0.0–1.0.</summary>
        float SaloonOwnershipShare(string saloonId);

        /// <summary>
        /// True when the named saloon has recorded a qualifying customer sale
        /// (a real customer, real money, real product — never a scripted one).
        /// </summary>
        bool HasQualifyingCustomerSale(string saloonId);

        /// <summary>
        /// True when a qualifying sale used a product or input supplied by
        /// another enterprise the player owns, through a real provenance chain.
        /// </summary>
        bool HasIntegratedSale();

        /// <summary>
        /// Count of DISTINCT products sold to the named saloon's customers that
        /// were internally supplied (real provenance chain from an owned
        /// enterprise). Distinct products, not units.
        /// </summary>
        int DistinctInternallySuppliedProductsSold(string saloonId);

        /// <summary>True when the Crown House holds the #1 standardized saloon traffic rank in Granite Junction.</summary>
        bool IsCrownHouseRankOne { get; }

        /// <summary>
        /// Qualifying scenario principal debt in cents — from the real
        /// liability ledger (T2A/SWN-3), never a parallel counter.
        /// </summary>
        int ScenarioPrincipalDebtCents { get; }

        int CurrentDayIndex { get; }
    }

    /// <summary>
    /// T3B: live evaluation of the Saloon Circuit ladder. Canon Part XV
    /// objective semantics:
    /// - HISTORICAL milestones complete once on legitimate execution and
    ///   persist ("remain completed after legitimate execution").
    /// - FINAL-STATE predicates re-evaluate current state every call.
    /// The scenario is complete when all three final-state predicates hold.
    /// </summary>
    public sealed class SaloonCircuitGoalEvaluator
    {
        private readonly HashSet<string> historicalCompleted = new HashSet<string>();
        private readonly HashSet<string> predicateHolding = new HashSet<string>();

        /// <summary>Raised once per goal, in ladder order, when it completes.</summary>
        public event Action<string> GoalCompleted;

        public IReadOnlyCollection<string> HistoricalCompleted => historicalCompleted;
        public IReadOnlyCollection<string> PredicatesHolding => predicateHolding;

        public bool IsScenarioComplete =>
            predicateHolding.Contains("crown-rank-one")
            && predicateHolding.Contains("own-all-three")
            && predicateHolding.Contains("debt-zero");

        private static readonly HashSet<string> HistoricalGoals = new HashSet<string>
        {
            "ash-creek-ownership",
            "prairie-crossing-ownership",
            "integrated-sale",
            "crown-house-ownership",
            "crown-three-products",
            "crown-five-products",
        };

        /// <summary>
        /// Evaluates the ladder against current state. Returns the goal ids
        /// that newly completed on THIS call (in ladder order).
        /// </summary>
        public List<string> Evaluate(ISaloonCircuitGameState state)
        {
            var newlyCompleted = new List<string>();
            if (state == null) return newlyCompleted;

            foreach (string goalId in SaloonCircuitScenario.GoalIdsInOrder)
            {
                bool holds = CheckGoal(goalId, state);
                if (HistoricalGoals.Contains(goalId))
                {
                    if (holds && historicalCompleted.Add(goalId))
                    {
                        newlyCompleted.Add(goalId);
                        GoalCompleted?.Invoke(goalId);
                    }
                }
                else
                {
                    // Final-state predicate: re-evaluates current state.
                    if (holds)
                    {
                        if (predicateHolding.Add(goalId))
                        {
                            newlyCompleted.Add(goalId);
                            GoalCompleted?.Invoke(goalId);
                        }
                    }
                    else
                    {
                        predicateHolding.Remove(goalId);
                    }
                }
            }
            return newlyCompleted;
        }

        private bool CheckGoal(string goalId, ISaloonCircuitGameState state)
        {
            switch (goalId)
            {
                case "ash-creek-ownership":
                    return state.SaloonOwnershipShare(SaloonCircuitScenario.AshCreekSaloonId) >= 1f
                        && state.HasQualifyingCustomerSale(SaloonCircuitScenario.AshCreekSaloonId)
                        && state.CurrentDayIndex <= Deadline("ash-creek-ownership");
                case "prairie-crossing-ownership":
                    return state.SaloonOwnershipShare(SaloonCircuitScenario.PrairieCrossingSaloonId) >= 1f
                        && state.HasQualifyingCustomerSale(SaloonCircuitScenario.PrairieCrossingSaloonId)
                        && state.CurrentDayIndex <= Deadline("prairie-crossing-ownership");
                case "integrated-sale":
                    return state.HasIntegratedSale();
                case "crown-house-ownership":
                    return state.SaloonOwnershipShare(SaloonCircuitScenario.CrownHouseSaloonId) >= 1f
                        && state.HasQualifyingCustomerSale(SaloonCircuitScenario.CrownHouseSaloonId);
                case "crown-three-products":
                    return state.DistinctInternallySuppliedProductsSold(SaloonCircuitScenario.CrownHouseSaloonId) >= 3;
                case "crown-five-products":
                    return state.DistinctInternallySuppliedProductsSold(SaloonCircuitScenario.CrownHouseSaloonId) >= 5;
                case "crown-rank-one":
                    return state.IsCrownHouseRankOne;
                case "own-all-three":
                    return state.SaloonOwnershipShare(SaloonCircuitScenario.AshCreekSaloonId) >= 1f
                        && state.SaloonOwnershipShare(SaloonCircuitScenario.PrairieCrossingSaloonId) >= 1f
                        && state.SaloonOwnershipShare(SaloonCircuitScenario.CrownHouseSaloonId) >= 1f;
                case "debt-zero":
                    return state.ScenarioPrincipalDebtCents == 0;
                default:
                    return false;
            }
        }

        private int Deadline(string goalId)
        {
            return SaloonCircuitScenario.DeadlineDayByGoal.TryGetValue(goalId, out int day) ? day : int.MaxValue;
        }

        /// <summary>Progress summary for the HUD: completed count + next goal id.</summary>
        public (int completed, int total, string nextGoalId) Progress()
        {
            int done = 0;
            string next = null;
            foreach (string goalId in SaloonCircuitScenario.GoalIdsInOrder)
            {
                bool complete = historicalCompleted.Contains(goalId) || predicateHolding.Contains(goalId);
                if (complete) done++;
                else if (next == null) next = goalId;
            }
            return (done, SaloonCircuitScenario.GoalIdsInOrder.Length, next);
        }
    }
}
