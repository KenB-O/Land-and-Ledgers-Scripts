using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Bakery;

namespace LandLedgers.Economy.BreadDrivers
{
    /// <summary>Explicit inputs for one daily fuel-blocker diagnostic line.</summary>
    public sealed class BakeryFuelBlockerInputs
    {
        public int BatchesPlanned;
        public int FuelUnitsOnHand;
        /// <summary>The authored D-2 conversion factor (intake units per
        /// bakery fuel unit), or null when unset. A factor of 0 or less is
        /// treated as unset — never as 1, which would be an invented
        /// conversion.</summary>
        public int? AuthoredFactor;
        /// <summary>Whether a live fuel-yard intake exists in the First
        /// Ledger daily flow. False in First Ledger: the only production
        /// construction of FuelYardByproductIntake is the scripted
        /// ThreeBusinessSlice, not the daily flow (grep-verified E4).</summary>
        public bool YardIntakeExistsInDailyFlow;
        /// <summary>Whether a dealer fuel source is attached to the bakery
        /// (Series2DealerBakeryFuelSource). False in First Ledger: the
        /// attached source is the in-stock stand-in, which verifies but
        /// never supplies.</summary>
        public bool DealerSourceAttached;
        /// <summary>Fuel units the attached dealer holds, when a dealer
        /// source is attached; ignored otherwise.</summary>
        public int DealerFuelUnitsOnHand;
    }

    /// <summary>
    /// Plan 10 E4 (Obj 8): the fuel-blocker diagnostic line. Pure C# —
    /// it names the exact missing piece(s) of the fuel supply from
    /// observed state and never claims a fuel unit is supplied. The
    /// forwarder builds it once per planned day; the tests pin its
    /// content for each missing-piece combination. No fuel moves here,
    /// no source is attached or replaced, and the D-2/D-7 decisions
    /// stay open and named.
    /// </summary>
    public static class BakeryFuelBlockerDiagnostics
    {
        public static string BuildFuelLine(BakeryFuelBlockerInputs inputs)
        {
            if (inputs == null)
                return "BakeryFuelBlocker: no fuel inputs supplied — the fuel blocker cannot be diagnosed.";
            int batches = Math.Max(0, inputs.BatchesPlanned);
            if (batches <= 0)
                return "BakeryFuelBlocker: no batches planned today — no fuel needed.";
            int need = batches * BakeryBreadCatalog.FuelUnitsPerFiring;
            int onHand = Math.Max(0, inputs.FuelUnitsOnHand);
            int? factor = inputs.AuthoredFactor.HasValue && inputs.AuthoredFactor.Value > 0
                ? inputs.AuthoredFactor : null;
            string factorClause = factor.HasValue
                ? $"factor authored ({factor.Value} intake units per bakery unit)"
                : "factor unset (D-2)";
            string intakeClause = inputs.YardIntakeExistsInDailyFlow
                ? "yard intake present in the daily flow"
                : "no live fuel-yard intake in the First Ledger daily flow";
            string dealerClause = inputs.DealerSourceAttached
                ? $"dealer source attached (dealer holds {Math.Max(0, inputs.DealerFuelUnitsOnHand)}u)"
                : "no dealer fuel source attached — in-stock stand-in only (verifies, never supplies)";

            if (onHand >= need)
                return $"BakeryFuelBlocker: fuel sufficient — {onHand}u on hand, need {need}u for {batches} batch(es); {factorClause}; {intakeClause}; {dealerClause}. No fuel blocker today.";

            var blockers = new List<string> { $"fuel store short by {need - onHand}u" };
            if (!factor.HasValue)
                blockers.Add("no dealer conversion (D-2 factor unset)");
            if (!inputs.YardIntakeExistsInDailyFlow)
                blockers.Add("no live fuel-yard intake");
            if (!inputs.DealerSourceAttached)
                blockers.Add("no dealer source attached");
            return $"BakeryFuelBlocker: fuel {onHand}u on hand, need {need}u for {batches} batch(es) — BLOCKED: {string.Join("; ", blockers)}. " +
                $"State: {factorClause}; {intakeClause}; {dealerClause}. " +
                "Open decisions: D-2 (fuel-dealer conversion factor), D-7 (fuel import execution design).";
        }
    }
}
