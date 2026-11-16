using System;
using System.Collections.Generic;
using LandLedgers.Economy.Equipment;

namespace LandLedgers.Economy.Businesses.Sawmill
{
    /// <summary>
    /// C-5 (expanded campaign, Phase 1 forestry): the mill's lubrication
    /// economy — one writer for the lubrication store, which lives in TWO
    /// places that must always agree:
    ///   (a) SawmillShopRuntime.MillLubricationUnits — the single source of
    ///       truth, persisted in SawmillShopRuntimeSaveDto;
    ///   (b) the hub support context's "consumable:mill-lubrication" key —
    ///       the live gate input (Supportability evaluates the key, never
    ///       the runtime field), which has NO save seam of its own.
    /// The bootstrapper syncs (b) FROM (a) on every boot
    /// (SyncSupportFromMill), so a Continue restores the drawn-down store
    /// instead of resetting it to the opening 30 (the pre-C-5 behaviour).
    /// Consumption is 1 unit per completed sawing day (not per run — a day
    /// with 3 runs still burns 1), and replenishment adds quarts of lamp oil
    /// 1:1 (see SawmillLubricationPurchase). No other code writes either
    /// store; a disagreement between them is refused loudly, never silently
    /// resynced, because it means a writer outside this class moved one.
    /// </summary>
    public static class SawmillLubrication
    {
        /// <summary>Lubrication burned per completed sawing day (C-3 calibration).</summary>
        public const int LubricationUnitsPerDay = 1;

        /// <summary>
        /// Restock trigger: when the store is at or below this many units
        /// (about a week of sawing days), the driver buys lamp oil from the
        /// General Store. Provisional calibration, owner corrects.
        /// </summary>
        public const int ReorderThresholdUnits = 5;

        /// <summary>
        /// Restock target: a purchase tops the store up to this many units
        /// (one month of sawing days — the C-3 opening store). Provisional
        /// calibration, owner corrects.
        /// </summary>
        public const int ReorderTargetUnits = 30;

        /// <summary>Support-context key for the mill lubrication store.</summary>
        public static string LubricationKey => SupportContext.Key("consumable", "mill-lubrication");

        /// <summary>
        /// Syncs the support-context key FROM the runtime field — the single
        /// direction that keeps a Continue from resetting the store. Called
        /// by the bootstrapper after RegisterMillSupport, on New Game and on
        /// Continue alike. Returns null on success, or a diagnostic on a
        /// structural problem (moving nothing).
        /// </summary>
        public static string SyncSupportFromMill(SawmillShopRuntime mill, SupportContext support, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (mill == null || support == null)
            {
                string refusal = "SawmillLubrication: mill runtime and support context are required to sync the lubrication store.";
                diag.Add(refusal);
                return refusal;
            }
            support.ConsumableUnits[LubricationKey] = Math.Max(0, mill.MillLubricationUnits);
            diag.Add($"SawmillLubrication: lubrication store synced from the mill runtime — {mill.MillLubricationUnits} unit(s) on hand.");
            return null;
        }

        /// <summary>
        /// Draws one unit for a completed sawing day from BOTH stores together.
        /// Refuses (moving nothing, never below 0) when either store is empty
        /// or when the two disagree — the gate already refuses sawing below 1
        /// unit, so an empty store means no sawing happened that day and the
        /// driver should not have called this. Returns null on success, or a
        /// diagnostic on refusal.
        /// </summary>
        public static string TryConsumeDay(SawmillShopRuntime mill, SupportContext support, int dayIndex, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (mill == null || support == null)
            {
                string refusal = "SawmillLubrication: mill runtime and support context are required to consume lubrication.";
                diag.Add(refusal);
                return refusal;
            }
            int onHand = 0;
            support.ConsumableUnits.TryGetValue(LubricationKey, out onHand);
            if (mill.MillLubricationUnits < LubricationUnitsPerDay)
            {
                string refusal = $"SawmillLubrication: mill lubrication store is empty ({mill.MillLubricationUnits} unit(s) in the runtime) on day {dayIndex} — nothing consumed, sawing stays gated until lamp oil is restocked.";
                diag.Add(refusal);
                return refusal;
            }
            if (onHand < LubricationUnitsPerDay)
            {
                string refusal = $"SawmillLubrication: support-context lubrication store is empty ({onHand} unit(s)) on day {dayIndex} — nothing consumed, sawing stays gated until lamp oil is restocked.";
                diag.Add(refusal);
                return refusal;
            }
            if (mill.MillLubricationUnits != onHand)
            {
                string refusal = $"SawmillLubrication: lubrication stores disagree on day {dayIndex} — runtime {mill.MillLubricationUnits} unit(s), support context {onHand} unit(s); nothing consumed, resync from the runtime before sawing.";
                diag.Add(refusal);
                return refusal;
            }
            mill.MillLubricationUnits -= LubricationUnitsPerDay;
            support.ConsumableUnits[LubricationKey] = mill.MillLubricationUnits;
            diag.Add($"SawmillLubrication: consumed {LubricationUnitsPerDay} lubrication unit(s) for sawing day {dayIndex} — {mill.MillLubricationUnits} unit(s) remain.");
            return null;
        }

        /// <summary>
        /// Adds quarts of lamp oil to BOTH stores together (the receipt leg of
        /// a SawmillLubricationPurchase). Refuses (moving nothing) when the
        /// two disagree — the purchase checks agreement before any cash moves,
        /// so this refusal should never fire after settlement; if it does, the
        /// purchase rolls the cash back and says so loudly. Returns null on
        /// success, or a diagnostic on refusal.
        /// </summary>
        public static string AddUnits(SawmillShopRuntime mill, SupportContext support, int quarts, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (mill == null || support == null)
            {
                string refusal = "SawmillLubrication: mill runtime and support context are required to receive lubrication.";
                diag.Add(refusal);
                return refusal;
            }
            if (quarts <= 0)
            {
                string refusal = "SawmillLubrication: a positive quart count is required to receive lubrication — whole quarts only, never a partial quart.";
                diag.Add(refusal);
                return refusal;
            }
            int onHand = 0;
            support.ConsumableUnits.TryGetValue(LubricationKey, out onHand);
            if (mill.MillLubricationUnits != onHand)
            {
                string refusal = $"SawmillLubrication: lubrication stores disagree — runtime {mill.MillLubricationUnits} unit(s), support context {onHand} unit(s); {quarts} quart(s) refused, resync from the runtime before restocking.";
                diag.Add(refusal);
                return refusal;
            }
            mill.MillLubricationUnits += quarts;
            support.ConsumableUnits[LubricationKey] = mill.MillLubricationUnits;
            diag.Add($"SawmillLubrication: received {quarts} lubrication unit(s) — {mill.MillLubricationUnits} unit(s) on hand.");
            return null;
        }

        /// <summary>True when the store is low enough to trigger a restock purchase.</summary>
        public static bool ShouldRestock(SawmillShopRuntime mill) =>
            mill != null && mill.MillLubricationUnits <= ReorderThresholdUnits;

        /// <summary>Quarts to buy to top the store up to the reorder target (0 when already at target).</summary>
        public static int QuartsToRestock(SawmillShopRuntime mill) =>
            mill == null ? 0 : Math.Max(0, ReorderTargetUnits - mill.MillLubricationUnits);

        /// <summary>Deterministic transaction id for one lubrication purchase (duplicate-safe, per the flour pattern).</summary>
        public static string TransactionIdFor(string millBusinessId, int dayIndex, int sequence) =>
            $"mill-lubrication-purchase:{millBusinessId}:{dayIndex}:{sequence}";
    }
}
