using System.Collections.Generic;

namespace LandLedgers.Orchestration.Scenarios.SaloonCircuit
{
    /// <summary>
    /// T3B: the Saloon Circuit — Three Towns scenario definition. Mirrors the
    /// BIZ-6 First Ledger structure. Canon Part XII (SALOON CIRCUIT SCENARIO
    /// LOCK) locks the completion structure; exact economic numbers, settlement
    /// populations, and the start year remain calibration.
    ///
    /// Objective semantics per Part XV: HISTORICAL milestones complete once on
    /// legitimate execution and persist; FINAL-STATE predicates re-evaluate
    /// current state every tick.
    /// </summary>
    public static class SaloonCircuitScenario
    {
        public const string ScenarioId = "saloon-circuit";

        public const string AshCreekSaloonId = "saloon-ash-creek";
        public const string PrairieCrossingSaloonId = "saloon-prairie-crossing";
        public const string CrownHouseSaloonId = "saloon-crown-house";

        public const string OpeningNarration =
            "Three towns. Three saloons. Ash Creek pours for farmers, Prairie Crossing " +
            "for the railroad crews, and the Crown House in Granite Junction pours for " +
            "everyone with money. Buy them all, make every sale an honest one, and " +
            "leave no debt standing when you're done.";

        /// <summary>Goal ids in ladder order (Canon Part XII §12.2).</summary>
        public static readonly string[] GoalIdsInOrder =
        {
            "ash-creek-ownership",      // historical: 100% Ash Creek + qualifying sale by deadline
            "prairie-crossing-ownership", // historical: 100% Prairie Crossing + qualifying sale by deadline
            "integrated-sale",          // historical: qualifying sale on another owned enterprise's product/input
            "crown-house-ownership",    // historical: 100% Crown House + first qualifying customer sale
            "crown-three-products",    // historical: 3 distinct internally supplied Crown products sold
            "crown-five-products",     // historical: 5 distinct internally supplied Crown products sold
            "crown-rank-one",          // final-state: Crown House #1 Granite Junction saloon traffic rank
            "own-all-three",           // final-state: 100% of all three saloons
            "debt-zero",              // final-state: qualifying scenario principal debt = $0.00
        };

        /// <summary>Authored deadlines (day indices) — calibration, per Canon §12.4.</summary>
        public static readonly Dictionary<string, int> DeadlineDayByGoal = new Dictionary<string, int>
        {
            { "ash-creek-ownership", 365 },
            { "prairie-crossing-ownership", 730 },
        };

        public static List<ScenarioGoal> BuildGoals()
        {
            return new List<ScenarioGoal>
            {
                new ScenarioGoal("ash-creek-ownership",
                    "Own 100% of the Ash Creek saloon and make a qualifying customer sale before the deadline.",
                    "100% ownership + qualifying sale"),
                new ScenarioGoal("prairie-crossing-ownership",
                    "Own 100% of the Prairie Crossing saloon and make a qualifying customer sale before the deadline.",
                    "100% ownership + qualifying sale"),
                new ScenarioGoal("integrated-sale",
                    "Make a qualifying sale using a product or input supplied by another enterprise you own — with a real provenance chain.",
                    "One vertically integrated sale"),
                new ScenarioGoal("crown-house-ownership",
                    "Own 100% of the Crown House and make its first qualifying customer sale.",
                    "100% ownership + first sale"),
                new ScenarioGoal("crown-three-products",
                    "Sell three distinct internally supplied products to Crown House customers.",
                    "3 distinct internal products"),
                new ScenarioGoal("crown-five-products",
                    "Sell five distinct internally supplied products to Crown House customers.",
                    "5 distinct internal products"),
                new ScenarioGoal("crown-rank-one",
                    "Hold the #1 standardized saloon traffic rank in Granite Junction with the Crown House.",
                    "Rank #1 (re-evaluated)"),
                new ScenarioGoal("own-all-three",
                    "Own 100% of Ash Creek, Prairie Crossing, and the Crown House at once.",
                    "All three (re-evaluated)"),
                new ScenarioGoal("debt-zero",
                    "Qualifying scenario principal debt equals $0.00.",
                    "$0.00 (re-evaluated)"),
            };
        }

        public static List<ScenarioObjective> BuildObjectives()
        {
            return new List<ScenarioObjective>
            {
                new ScenarioObjective("scout-ash-creek",
                    "Scout the Ash Creek saloon: who owns it, what it earns, what it needs.",
                    "ash-creek-ownership"),
                new ScenarioObjective("acquire-ash-creek",
                    "Acquire 100% of the Ash Creek saloon through a real purchase.",
                    "ash-creek-ownership"),
                new ScenarioObjective("first-ash-sale",
                    "Record the Ash Creek qualifying customer sale before its deadline.",
                    "ash-creek-ownership"),
                new ScenarioObjective("scout-prairie-crossing",
                    "Scout the Prairie Crossing saloon.",
                    "prairie-crossing-ownership"),
                new ScenarioObjective("acquire-prairie-crossing",
                    "Acquire 100% of the Prairie Crossing saloon through a real purchase.",
                    "prairie-crossing-ownership"),
                new ScenarioObjective("supply-chain-link",
                    "Establish a supply link from another owned enterprise with full provenance.",
                    "integrated-sale"),
                new ScenarioObjective("acquire-crown-house",
                    "Acquire 100% of the Crown House through a real purchase.",
                    "crown-house-ownership"),
                new ScenarioObjective("staff-crown-house",
                    "Staff the Crown House to its observed workload (T2G staffing).",
                    "crown-house-ownership"),
            };
        }

        public static List<TunableValue> BuildTunables()
        {
            return new List<TunableValue>
            {
                new TunableValue("ashCreekDeadlineDay", 365, "Authored deadline (day) for the Ash Creek milestone."),
                new TunableValue("prairieCrossingDeadlineDay", 730, "Authored deadline (day) for the Prairie Crossing milestone."),
                new TunableValue("trafficRankWindowDays", 30, "Observation window (days) for the #1 traffic rank predicate."),
            };
        }
    }
}
