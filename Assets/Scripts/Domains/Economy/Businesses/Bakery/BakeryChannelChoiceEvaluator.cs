using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Businesses.Bakery
{
    /// <summary>
    /// Lane 10a (H-5): the observed signals Glenn's deterministic morning
    /// sales-channel re-evaluation reads. Every value is an observed signal
    /// that already exists in code (recorded counter sales, open household
    /// need, retail-available shelf, due wholesale terms, prices, production
    /// capacity) — nothing is sensed new and nothing is invented. Counts are
    /// loaves unless named; prices are cents per loaf. Negative inputs are
    /// sanitized to zero by the evaluator; the decision record uses -1 for
    /// "not measured", which never reaches this struct.
    /// </summary>
    public sealed class BakeryChannelSignals
    {
        public int DayIndex;
        public int RecordedRetailAverageUnitsPerDay;
        public int OpenHouseholdBreadNeedUnits;
        public int RetailUnitsAvailable;
        public int DueWholesaleUnits;
        public int DueWholesaleRevenueCents;
        public int DeliveryCostCents;
        public int DailyProductionCapacityUnits;
        public int RetailPriceCentsPerUnit;
        public BakerySupplyProtectionPolicy CurrentPolicy;
    }

    /// <summary>
    /// Lane 10a (H-5): the evaluator's inspectable proposal. RecommendedPolicy
    /// is Unspecified when the current policy should stand; PauseDueWholesaleAccounts
    /// is set only when the due accounts themselves must be paused for the
    /// counter to be served. Reasons always name the numbers behind the call,
    /// including on a no-change — the diagnostic never invents a reason, and
    /// "no supporting signal" is a valid outcome.
    /// </summary>
    public sealed class BakeryChannelProposal
    {
        public BakerySupplyProtectionPolicy RecommendedPolicy = BakerySupplyProtectionPolicy.Unspecified;
        public bool PauseDueWholesaleAccounts;
        public List<string> Reasons = new List<string>();

        public bool HasChange => RecommendedPolicy != BakerySupplyProtectionPolicy.Unspecified
            || PauseDueWholesaleAccounts;
    }

    /// <summary>
    /// Lane 10a (H-5): Glenn's deterministic morning sales-channel choice
    /// (Canon §7.3E trade-off — throughput visibility and guaranteed demand
    /// versus reserved stock that might otherwise serve walk-in households —
    /// decided by the NPC owner per Canon §7.5, in the same pattern as
    /// RivalOwnerDecisionEvaluator: deterministic, inputs in, inspectable
    /// proposal out, never a hidden pick, never an unrecorded default).
    ///
    /// Rules, in order (exact thresholds are TUNING under Owner Decisions
    /// 1–15; the rule structure is the Agent H §3.3 recommendation):
    ///   R0  No production capacity: nothing to allocate, no change.
    ///   R0b No wholesale due today: the policy is moot today, no change.
    ///   R1  Retail starved: open household need exceeds what the counter
    ///       can sell while wholesale is due — protect the retail counter.
    ///   R2  Retail absorbs everything: recorded retail demand (3-day
    ///       average + open need) meets or exceeds daily capacity — the
    ///       wholesale account adds no throughput, protect retail.
    ///   R3  Wholesale adds throughput that pays: due units exceed the
    ///       retail-absorbable surplus AND the net wholesale revenue beats
    ///       the expected retail revenue for the same loaves (units ×
    ///       counter price × observed sell-through) — protect the due
    ///       contracts. Needs at least one observed retail day to compare
    ///       against; with no recorded sales the comparison would be
    ///       invented, so the policy stands.
    ///   R4  Pause: only when the policy is already ProtectRetailCounter
    ///       and the counter is still starved while retail demand meets or
    ///       exceeds capacity — the policy flip alone gets one morning to
    ///       free stock, then the due accounts pause (a recorded decision
    ///       per account, a lawful transition, never a silent wipe).
    /// No rule fires: no change, and the reasons say so.
    /// </summary>
    public static class BakeryChannelChoiceEvaluator
    {
        /// <summary>
        /// TUNING (Owner Decisions 1–15): a due wholesale commitment at or
        /// above 75% of the shop's daily production capacity is flagged as
        /// single-buyer concentration (Canon §7.3E) in the reasons. It never
        /// flips a policy by itself — it names the risk the decider is
        /// already weighing in R1/R2/R3.
        /// </summary>
        public const float SingleBuyerConcentrationThreshold01 = 0.75f;

        public static BakeryChannelProposal Evaluate(BakeryChannelSignals signals)
        {
            var proposal = new BakeryChannelProposal();
            if (signals == null)
            {
                proposal.Reasons.Add("no channel signals supplied — current policy stands");
                return proposal;
            }

            int recordedAvg = Math.Max(0, signals.RecordedRetailAverageUnitsPerDay);
            int openNeed = Math.Max(0, signals.OpenHouseholdBreadNeedUnits);
            int retailAvailable = Math.Max(0, signals.RetailUnitsAvailable);
            int dueUnits = Math.Max(0, signals.DueWholesaleUnits);
            int dueRevenue = Math.Max(0, signals.DueWholesaleRevenueCents);
            int deliveryCost = Math.Max(0, signals.DeliveryCostCents);
            int capacity = Math.Max(0, signals.DailyProductionCapacityUnits);
            int retailPrice = Math.Max(0, signals.RetailPriceCentsPerUnit);
            BakerySupplyProtectionPolicy current = signals.CurrentPolicy;

            // R0: nothing can bake, nothing to allocate.
            if (capacity <= 0)
            {
                proposal.Reasons.Add("no production capacity — nothing to allocate, current policy stands");
                return proposal;
            }

            // R0b: with nothing due, the scarcity policy changes nothing today.
            if (dueUnits <= 0)
            {
                proposal.Reasons.Add("no wholesale due today — the policy is moot today, current policy stands");
                return proposal;
            }

            int retailDemand = recordedAvg + openNeed;
            int retailAbsorbableSurplus = Math.Max(0, capacity - retailDemand);
            float sellThrough = Math.Min(1f, recordedAvg / (float)Math.Max(1, capacity));
            int expectedRetailRevenueCents = (int)Math.Round(dueUnits * (float)retailPrice * sellThrough);
            int netWholesaleRevenueCents = dueRevenue - deliveryCost;
            bool concentrated = dueUnits >= SingleBuyerConcentrationThreshold01 * capacity;
            string concentrationNote = concentrated
                ? $" Single-buyer concentration: due wholesale {dueUnits}u is {dueUnits * 100 / capacity}% of daily capacity {capacity}u."
                : string.Empty;

            // R1: the counter is starved while wholesale is due.
            if (openNeed > retailAvailable)
            {
                if (current != BakerySupplyProtectionPolicy.ProtectRetailCounter)
                    proposal.RecommendedPolicy = BakerySupplyProtectionPolicy.ProtectRetailCounter;
                proposal.Reasons.Add($"open household need {openNeed}u exceeds retail-available {retailAvailable}u while {dueUnits}u wholesale is due — the counter is starved.{concentrationNote}");
                // R4: the policy flip alone gets one morning to free stock;
                // only when already retail-first and still starved do the
                // due accounts pause.
                if (current == BakerySupplyProtectionPolicy.ProtectRetailCounter && retailDemand >= capacity)
                {
                    proposal.PauseDueWholesaleAccounts = true;
                    proposal.Reasons.Add($"retail demand {retailDemand}u meets or exceeds daily capacity {capacity}u and the counter is still starved — pause the due wholesale accounts so the shop's whole output can serve walk-in households.{concentrationNote}");
                }
                return proposal;
            }

            // R2: retail demand absorbs everything the shop can make.
            if (retailDemand >= capacity)
            {
                if (current != BakerySupplyProtectionPolicy.ProtectRetailCounter)
                    proposal.RecommendedPolicy = BakerySupplyProtectionPolicy.ProtectRetailCounter;
                proposal.Reasons.Add($"recorded retail demand {retailDemand}u (3-day average {recordedAvg}u + open need {openNeed}u) meets or exceeds daily capacity {capacity}u — wholesale adds no throughput.{concentrationNote}");
                return proposal;
            }

            // R3: wholesale adds throughput the counter cannot absorb, and pays.
            // Needs at least one observed retail day: with no recorded sales
            // the expected-retail comparison would be invented.
            if (dueUnits > retailAbsorbableSurplus && recordedAvg > 0
                && netWholesaleRevenueCents > expectedRetailRevenueCents)
            {
                if (current != BakerySupplyProtectionPolicy.ProtectWholesaleContracts)
                    proposal.RecommendedPolicy = BakerySupplyProtectionPolicy.ProtectWholesaleContracts;
                proposal.Reasons.Add($"due wholesale {dueUnits}u exceeds the retail-absorbable surplus {retailAbsorbableSurplus}u and its net revenue {netWholesaleRevenueCents}c beats the expected retail revenue {expectedRetailRevenueCents}c for the same loaves (3-day average {recordedAvg}u, sell-through {sellThrough:0.##}) — the account adds throughput that pays.{concentrationNote}");
                return proposal;
            }

            // No supporting signal: the current policy stands, and the
            // reasons say which comparison failed instead of inventing one.
            if (recordedAvg <= 0)
            {
                proposal.Reasons.Add("no recorded retail sales yet — the wholesale value cannot be compared against observed retail demand, current policy stands");
            }
            else
            {
                proposal.Reasons.Add($"due wholesale {dueUnits}u fits the retail-absorbable surplus {retailAbsorbableSurplus}u or does not beat expected retail revenue {expectedRetailRevenueCents}c (net wholesale {netWholesaleRevenueCents}c) — no supporting signal, current policy stands");
            }
            return proposal;
        }
    }
}
