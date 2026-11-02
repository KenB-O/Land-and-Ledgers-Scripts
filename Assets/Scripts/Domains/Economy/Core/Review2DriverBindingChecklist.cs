// Review 2 Pass C (scripts): driver-binding support in C# only.
// This file changes NO production behaviour, wires NO scene, and
// registers NO adapter. It is a checklist a day driver (Codex) can
// call before binding, so the binding obligations the second review
// and Pass A pinned are checked in one place instead of remembered
// from review docs. Every check below restates an existing contract;
// none of them invents, weakens, or "fixes" one:
//
//   - Bakery adapter DayIndex (Pass A pin): the adapter's
//     CheckTradingCapability ignores its absolute-day argument and
//     refuses only while its own DayIndex property is unset
//     (Series2BakerySupplierAdapter, DayIndex defaults to -1).
//     Day equality is NOT enforced by the adapter. A checklist
//     pass therefore means "the day driver set DayIndex", and a
//     mismatch between adapter day and production day is a
//     DIAGNOSTIC (the adapter will price at its own day), not a
//     refusal invented here - inventing a refusal would change
//     adapter semantics without touching the adapter.
//   - Dealer fuel partial supply (Pass A pin): with an off-map
//     callback wired, the dealer portion moves into the bakery
//     store BEFORE the callback can refuse, so a refused supply
//     is NOT a clean rollback. The production driver must run
//     the Pass 6 pre-check first, or explicitly accept
//     partial-store state. This checklist refuses a supply
//     order that claims NEITHER, so the choice is explicit.
//   - Retail WARNING-only cash (Pass A pin): adapters post
//     revenue AFTER stock moves; a failing cash port leaves the
//     sale standing with a WARNING. Pass 9 proved the production
//     port (Series2BusinessRuntimeCashPort) cannot fail a
//     non-negative credit on a non-null runtime, so the WARNING
//     branch is defensive dead code on that port. This checklist
//     does NOT re-prove that here; it lists confirming the
//     production port as a binding step Codex must do.
//   - Save states (Pass 11 wiring): SaveLoadManager carries the
//     Pass 9, Pass 10, and procurement states, but assigning the
//     LIVE state before the first save/load remains Codex work.
//     Wiring is not assigning, and this checklist says so.
//   - Restore re-supply (Pass 9 audit): a restored duty roster
//     has no TaskAuthority WorkTasks until the driver re-issues
//     through the binding; restored auto-eat/auto-sleep has no
//     seller/lodging object until the driver re-supplies one;
//     a restored fuel source has parameters only until the
//     driver rewires its off-map delegate. None of those are
//     fixed by restoring; all are listed as steps.
//
// Plain C#, no Unity dependency, Roslyn-testable. Unity
// compilation, Test Runner, PlayMode, and scene wiring are
// NOT claimed by this file.
using System;
using System.Collections.Generic;

namespace LandLedgers.Economy
{
    /// <summary>Review 2 Pass C binding checklist. Pure diagnostics; no driver, adapter, or scene is touched.</summary>
    public static class Review2DriverBindingChecklist
    {
        /// <summary>
        /// Ordered binding steps for Codex, in the order the second
        /// review ranked them. A step is a human/driver action, not
        /// a claim that this file performs it.
        /// </summary>
        public static IReadOnlyList<string> BindingSteps => new[]
        {
            "1. Register the bakery and fuel supplier adapters in production (FirstLedgerSliceBootstrapper, alongside the already-wired General Store registration); test-only RegisterIn calls do not count.",
            "2. Set the bakery adapter DayIndex at EVERY day start; a DayIndex set once and never updated prices at a stale day, and day equality is not enforced by the adapter.",
            "3. Run the Pass 6 bakery pre-check (flour, notions, fuel, ready oven, hand kit) BEFORE any Pass 8 dealer fuel supply, or explicitly accept partial-store state on a refused off-map supply.",
            "4. Confirm the production cash port in use is Series2BusinessRuntimeCashPort over a real BusinessRuntimeState (Pass 9 port), so the Pass 7 retail WARNING branch stays defensive dead code.",
            "5. Assign live Series2Pass9State, Series2Pass10State, and PassXlProcurementService before the first save/load; Pass 11 wired the sections into Capture/Restore, assigning the live state is still Codex work.",
            "6. After a load, re-issue restored duty roster tasks through the Pass 9 binding, re-supply seller/lodging objects for restored auto-eat/auto-sleep, and rewire the fuel source from RestoredFuelSourceParams.",
            "7. Choose ONE settlement path per freight leg as the normal route (driver collection on the tick, or Pass 8 acceptance settlement); the freightRevenueCollected flag prevents double collection, it does not choose the route.",
            "8. Enforce detailed-only freight for slice legs as a wiring rule; no code guard prevents the timer path and the detailed path from double-booking the same shipment.",
            "9. Wire slice leg receiving callbacks (procurement arrival, Pass 6 feed, Pass 3 wholesale intake, Pass 8 dealer supply, Pass 8 reserve link) before claiming any leg works end to end.",
            "10. Prove one live chain in the Main Scene (one household purchase, one meal, one nutrition change) with a log or screenshot; a harness assertion is not a live proof.",
        };

        /// <summary>
        /// Checks the bakery adapter day a day driver is about to use.
        /// Returns null when the adapter day is set (even if it differs
        /// from the production day - see the file header: the adapter
        /// does NOT enforce day equality, so this method must not
        /// invent that refusal). Returns a refusal only when the
        /// adapter day is unset, which is the adapter's own refusal.
        /// A mismatch is appended to diag as a warning, never hidden.
        /// </summary>
        public static string CheckBakeryAdapterDay(int adapterDayIndex, int productionDayIndex, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (adapterDayIndex < 0)
            {
                return "Review2DriverBindingChecklist: bakery adapter DayIndex is unset (< 0); the adapter refuses all trading until the day driver sets it at day start. Nothing may be sold through the bakery adapter on this day.";
            }

            if (adapterDayIndex != productionDayIndex)
            {
                diag.Add($"Review2DriverBindingChecklist: bakery adapter DayIndex ({adapterDayIndex}) differs from production day ({productionDayIndex}); " +
                    "the adapter does NOT enforce day equality and will price at its own day, so the day driver must set DayIndex at every day start or accept stale-day pricing explicitly.");
            }

            return null;
        }

        /// <summary>
        /// Checks a planned dealer fuel supply order. Returns null only
        /// when the caller states, explicitly, that the Pass 6
        /// pre-check runs first OR that partial-store state is
        /// accepted. Claiming neither is a refusal: a refused off-map
        /// supply after a dealer portion has already moved is not a
        /// clean rollback (Pass A pin), and silently proceeding
        /// would hide that. This method moves no fuel.
        /// </summary>
        public static string CheckFuelSupplyOrder(bool precheckRunsFirst, bool partialStoreStateAccepted, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (precheckRunsFirst)
            {
                return null;
            }

            if (partialStoreStateAccepted)
            {
                diag.Add("Review2DriverBindingChecklist: fuel supply proceeds WITHOUT the Pass 6 pre-check first; partial-store state on a refused off-map supply is explicitly accepted by the caller. Fuel already in the bakery store stays for the next bake attempt; it is not lost and not rolled back.");
                return null;
            }

            return "Review2DriverBindingChecklist: fuel supply order states neither that the Pass 6 pre-check runs first nor that partial-store state is accepted. " +
                "With an off-map callback wired, a dealer portion moves into the bakery store before the callback can refuse, so a refused supply is not a clean rollback. State one of the two explicitly before supplying.";
        }

        /// <summary>
        /// Checks the save-state assignments a binding must make before
        /// the first save. All three flags are Codex/driver actions;
        /// this method records which are missing, it does not assign
        /// anything. Returns null only when all three are stated.
        /// </summary>
        public static string CheckSaveStateAssignments(
            bool pass9StateAssigned, bool pass10StateAssigned, bool procurementServiceAssigned, List<string> diag)
        {
            diag = diag ?? new List<string>();
            var missing = new List<string>();
            if (!pass9StateAssigned) missing.Add("Series2Pass9State");
            if (!pass10StateAssigned) missing.Add("Series2Pass10State");
            if (!procurementServiceAssigned) missing.Add("PassXlProcurementService (F19)");
            if (missing.Count == 0) return null;
            return "Review2DriverBindingChecklist: live save states not yet assigned before first save: " +
                string.Join(", ", missing) +
                ". Pass 11 wired these sections into Capture/Restore, but wiring is not assigning: a first save without them persists defaults and a restore silently loses Pass 9/10/procurement state.";
        }
    }
}
