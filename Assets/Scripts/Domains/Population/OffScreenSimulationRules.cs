using System;
using System.Collections.Generic;

namespace LandLedgers.Population
{
    /// <summary>
    /// Pass IV (Worker 4, Phase P): the rules for coarse / off-screen
    /// simulation. Off-screen households and persons are simulated at low
    /// resolution, but ALWAYS against the same authoritative state as the
    /// rendered world — the schedule tracker, work-time budgets, task
    /// authority, inventories, ledgers, and due-event queue are the single
    /// source of truth. There is no parallel shadow economy.
    ///
    /// HARD RULES (violations are loud refusals, never silent fudges):
    ///  1. Never manufacture labor time: batched work may only consume
    ///     minutes the person's budget actually holds, inside windows the
    ///     schedule tracker confirms free.
    ///  2. Never manufacture goods: batched production may only convert
    ///     inputs the inventory actually holds into outputs at the real
    ///     recipe ratio; waste is recorded, not deleted.
    ///  3. Never manufacture money: every cash movement goes through the
    ///     HouseholdLedger single writer. Batching changes WHEN entries post,
    ///     never WHETHER they post.
    ///  4. Never manufacture capacity or travel: equipment occupancy requires
    ///     a real reservation; a journey completes only when its due time has
    ///     passed — no teleporting.
    ///  5. Never manufacture customers: demand comes from real purchasing
    ///     needs, never from a quota.
    ///  6. Reconciliation on return: when a household/person becomes rendered
    ///     again, <see cref="OffScreenReconciler"/> verifies inventory, cash,
    ///     task, and location state against the authorities and reports any
    ///     divergence instead of papering over it.
    ///
    /// What batching IS allowed: low-consequence, contention-free actions may
    /// be advanced in whole chunks — sleep, rest, meals (consumption from real
    /// lots), uninterrupted domestic tasks with no equipment needs, travel
    /// legs whose due times have passed. Anything touching shared equipment,
    /// other persons' time, or cash beyond routine settlement stays at full
    /// resolution even off-screen (it keeps its due events).
    /// </summary>
    public static class OffScreenSimulationRules
    {
        /// <summary>
        /// True when the action may be advanced in a coarse chunk off-screen.
        /// Everything else keeps full-resolution due events.
        /// </summary>
        public static bool MayBatch(OffScreenActionKind kind)
        {
            switch (kind)
            {
                case OffScreenActionKind.Sleep:
                case OffScreenActionKind.Rest:
                case OffScreenActionKind.Meal:
                case OffScreenActionKind.UncontendedDomesticWork:
                case OffScreenActionKind.CompletedJourneyLeg:
                    return true;
                case OffScreenActionKind.EquipmentWork:
                case OffScreenActionKind.CashTransaction:
                case OffScreenActionKind.SharedTask:
                case OffScreenActionKind.ShoppingSettlement:
                default:
                    return false;
            }
        }

        /// <summary>
        /// Validates a proposed coarse step against the authoritative state.
        /// Returns null when the step is legal; otherwise the loud reason it
        /// is refused. The caller must NOT apply a refused step.
        /// </summary>
        public static string ValidateBatchStep(OffScreenBatchStep step)
        {
            if (step == null)
            {
                return "null batch step";
            }

            if (!MayBatch(step.Kind))
            {
                return $"action {step.Kind} must stay at full resolution off-screen — refusing to batch";
            }

            if (step.Minutes <= 0)
            {
                return "batch step must advance positive minutes";
            }

            if (step.BudgetMinutesRemaining < step.Minutes)
            {
                return $"P{step.PersonId} budget holds {step.BudgetMinutesRemaining} min — cannot batch {step.Minutes} min (no manufactured labor time)";
            }

            if (!step.WindowFree)
            {
                return $"P{step.PersonId} has no free schedule window for the batched {step.Minutes} min (no manufactured labor time)";
            }

            if (step.GoodsConsumed != null)
            {
                foreach (var kv in step.GoodsConsumed)
                {
                    int have = 0;
                    if (step.InventoryUnits != null)
                    {
                        step.InventoryUnits.TryGetValue(kv.Key, out have);
                    }

                    if (have < kv.Value)
                    {
                        return $"batch would consume {kv.Value}u {kv.Key} but only {have}u exist (no manufactured goods)";
                    }
                }
            }

            if (step.CashDeltaCents != 0)
            {
                return "batch steps may not move cash — cash posts through HouseholdLedger only";
            }

            if (step.RequiresEquipment && !step.EquipmentReserved)
            {
                return "batch steps may not use equipment without a real reservation (no manufactured capacity)";
            }

            if (step.Kind == OffScreenActionKind.CompletedJourneyLeg && !step.JourneyLegDue)
            {
                return "journey leg may only complete after its due time (no teleporting)";
            }

            return null;
        }
    }

    /// <summary>Kinds of off-screen activity, split by batchability.</summary>
    public enum OffScreenActionKind
    {
        Sleep = 0,
        Rest = 1,
        Meal = 2,
        UncontendedDomesticWork = 3,
        CompletedJourneyLeg = 4,
        // --- full-resolution only below this line ---
        EquipmentWork = 10,
        CashTransaction = 11,
        SharedTask = 12,
        ShoppingSettlement = 13,
    }

    /// <summary>One proposed coarse step, validated before it is applied.</summary>
    [Serializable]
    public sealed class OffScreenBatchStep
    {
        public OffScreenActionKind Kind;
        public int PersonId = -1;
        public int HouseholdId = -1;
        public int Minutes;
        public int BudgetMinutesRemaining;
        public bool WindowFree;
        /// <summary>Item → units the step consumes (validated against real stock).</summary>
        public Dictionary<string, int> GoodsConsumed;
        /// <summary>Item → units actually on hand (authoritative inventory).</summary>
        public Dictionary<string, int> InventoryUnits;
        /// <summary>Non-zero is always refused: cash moves through the ledger only.</summary>
        public int CashDeltaCents;
        public bool RequiresEquipment;
        public bool EquipmentReserved;
        /// <summary>For journey legs: the leg's due time has passed.</summary>
        public bool JourneyLegDue;
        public string Label = string.Empty;
    }

    /// <summary>
    /// Reconciliation when a household/person returns to a rendered area.
    /// Compares the off-screen ending state against the authorities and
    /// reports divergence LOUDLY instead of papering over it. The
    /// authorities always win: the render state is rebuilt from them.
    /// </summary>
    public sealed class OffScreenReconciler
    {
        public sealed class ReconciliationReport
        {
            public int HouseholdId = -1;
            public bool Clean = true;
            public List<string> Divergences = new List<string>();
            public List<string> Notes = new List<string>();
        }

        /// <summary>
        /// Verifies one household's off-screen record against the authorities.
        /// All providers read live authoritative state.
        /// </summary>
        public ReconciliationReport ReconcileHousehold(
            int householdId,
            OffScreenHouseholdRecord record,
            Func<int, int> ledgerBalanceCentsOf,
            Func<int, Dictionary<string, int>> inventoryUnitsOf,
            Func<int, string> personLocationOf,
            List<string> diag)
        {
            var report = new ReconciliationReport { HouseholdId = householdId };
            diag = diag ?? new List<string>();

            if (record == null)
            {
                report.Clean = false;
                report.Divergences.Add("no off-screen record — cannot verify; rebuilding render state from authorities");
                diag.Add($"OffScreenReconciler: H{householdId} has no off-screen record.");
                return report;
            }

            int ledgerBalance = ledgerBalanceCentsOf != null ? ledgerBalanceCentsOf(householdId) : 0;
            if (ledgerBalance != record.EndingCashCents)
            {
                report.Clean = false;
                report.Divergences.Add(
                    $"cash: off-screen record says {record.EndingCashCents}c but the ledger holds {ledgerBalance}c — ledger wins");
            }

            Dictionary<string, int> inventory =
                inventoryUnitsOf != null ? inventoryUnitsOf(householdId) : new Dictionary<string, int>();
            foreach (var kv in record.EndingInventoryUnits)
            {
                int have = 0;
                inventory.TryGetValue(kv.Key, out have);
                if (have != kv.Value)
                {
                    report.Clean = false;
                    report.Divergences.Add(
                        $"inventory {kv.Key}: off-screen record says {kv.Value}u but the registry holds {have}u — registry wins");
                }
            }

            foreach (var kv in record.EndingPersonLocations)
            {
                string actual = personLocationOf != null ? personLocationOf(kv.Key) : null;
                if (!string.Equals(actual, kv.Value, StringComparison.Ordinal))
                {
                    report.Clean = false;
                    report.Divergences.Add(
                        $"P{kv.Key} location: off-screen record says '{kv.Value}' but authoritative location is '{actual}' — no teleporting, authority wins");
                }
            }

            report.Notes.Add(
                $"H{householdId} reconciled: {record.BatchedStepsApplied} batched steps, {record.FullResolutionEventsKept} full-resolution events kept.");
            diag.Add($"OffScreenReconciler: H{householdId} return — clean={report.Clean}, divergences={report.Divergences.Count}.");
            return report;
        }
    }

    /// <summary>What the off-screen run claims happened (verified on return).</summary>
    [Serializable]
    public sealed class OffScreenHouseholdRecord
    {
        public int HouseholdId = -1;
        public int FromDayIndex;
        public int ToDayIndex;
        public int EndingCashCents;
        public Dictionary<string, int> EndingInventoryUnits = new Dictionary<string, int>();
        public Dictionary<int, string> EndingPersonLocations = new Dictionary<int, string>();
        public int BatchedStepsApplied;
        public int FullResolutionEventsKept;
        public List<string> BatchedStepLog = new List<string>();
    }
}
