using System.Collections.Generic;

namespace LandLedgers.Orchestration.Scenarios.FirstLedger
{
    /// <summary>
    /// BIZ-6: the "First Ledger" scenario definition — the first real scenario built
    /// on the DEV-1 infrastructure. A money-goals progression from Kennedy's
    /// playtesting: buy businesses, hire hands, build owner equity.
    ///
    /// Canon requirements honored:
    /// - Opening narration (Canon Part I §1.1).
    /// - Declared population inclusion rules (Tech X §2.1).
    /// - Declared player household (GHOST-DEF-006).
    /// - Equity goals use OwnerEquityValue from BIZ-5, never cash-in-business
    ///   (Canon §11.4).
    /// </summary>
    public static class FirstLedgerScenario
    {
        public const string ScenarioId = "first-ledger";

        public const string OpeningNarration =
            "1870. You step off the stage with your household, a stake of cash, and a " +
            "ledger with nothing in it yet. The town needs merchants. Buy your first " +
            "business, hire your first hand, and build $15,000 of owner equity — " +
            "honestly, one entry at a time.";

        /// <summary>Goal ids in ladder order.</summary>
        public static readonly string[] GoalIdsInOrder =
        {
            "acquire-first-business",
            "hire-first-employee",
            "equity-5000",
            "own-two-businesses",
            "three-employees",
            "equity-10000",
            "own-three-businesses",
            "equity-15000",
        };

        public static List<ScenarioGoal> BuildGoals()
        {
            return new List<ScenarioGoal>
            {
                new ScenarioGoal("acquire-first-business",
                    "Acquire your first business",
                    "Own 1 business"),
                new ScenarioGoal("hire-first-employee",
                    "Hire your first employee",
                    "1 active employee"),
                new ScenarioGoal("equity-5000",
                    "Reach $5,000 owner equity",
                    "$5,000 (owner equity, not cash-in-business — Canon §11.4)"),
                new ScenarioGoal("own-two-businesses",
                    "Own two businesses",
                    "Own 2 businesses"),
                new ScenarioGoal("three-employees",
                    "Employ 3 people across your businesses",
                    "3 active employees"),
                new ScenarioGoal("equity-10000",
                    "Reach $10,000 owner equity",
                    "$10,000 (owner equity — Canon §11.4)"),
                new ScenarioGoal("own-three-businesses",
                    "Own three businesses",
                    "Own 3 businesses"),
                new ScenarioGoal("equity-15000",
                    "Reach $15,000 owner equity",
                    "$15,000 owner equity — scenario complete"),
            };
        }

        public static List<ScenarioObjective> BuildObjectives()
        {
            return new List<ScenarioObjective>
            {
                new ScenarioObjective("choose-first-business",
                    "Choose which business to buy first — the general store is the natural start.",
                    "acquire-first-business"),
                new ScenarioObjective("complete-purchase",
                    "Complete the acquisition (seller meeting, terms, handover).",
                    "acquire-first-business"),
                new ScenarioObjective("offer-employment",
                    "Offer a wage to a worker through the employment relationship.",
                    "hire-first-employee"),
                new ScenarioObjective("second-acquisition",
                    "Acquire a second business — freight or butcher pair well with the store.",
                    "own-two-businesses"),
                new ScenarioObjective("third-acquisition",
                    "Acquire a third business to complete the set.",
                    "own-three-businesses"),
            };
        }

        public static PlayerHouseholdDeclaration BuildPlayerHousehold()
        {
            // Serialized fields are private; the declaration is data Kennedy edits on
            // the asset. This builder documents the intended content.
            return new PlayerHouseholdDeclaration();
        }

        public static PopulationInclusionRules BuildInclusionRules()
        {
            return new PopulationInclusionRules();
        }

        public static List<TunableValue> BuildTunables()
        {
            return new List<TunableValue>
            {
                new TunableValue("startingCashCents", 250000,
                    "Player household starting cash (cents)."),
                new TunableValue("equityTarget1Cents", 500000,
                    "First equity milestone (cents of owner equity)."),
                new TunableValue("equityTarget2Cents", 1000000,
                    "Second equity milestone."),
                new TunableValue("equityTarget3Cents", 1500000,
                    "Final equity milestone — scenario complete."),
            };
        }

        /// <summary>Equity thresholds in cents, ladder order (goals 3, 6, 8).</summary>
        public static readonly int[] EquityThresholdsCents = { 500000, 1000000, 1500000 };
    }
}
