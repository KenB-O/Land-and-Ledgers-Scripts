using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.LumberYard;

namespace LandLedgers.Economy.Businesses.Mine
{
    /// <summary>
    /// D3C: one timber requisition — timber sets withdrawn from a real lumber
    /// stock for mine timbering, with the lumber lots' provenance chains
    /// attached for the timbering ledger. Shortfalls are refused loudly,
    /// never auto-ordered (upstream-provenance doctrine).
    /// </summary>
    [Serializable]
    public sealed class MineTimberWithdrawal
    {
        public int TimberSetsRequested { get; }
        public int LumberUnitsWithdrawn { get; }
        public int TimberSetsCovered { get; }
        public int ShortfallSets { get; }
        public List<string> LumberProvenanceChains { get; } = new List<string>();

        public bool FullyCovered => ShortfallSets <= 0;

        public MineTimberWithdrawal(int timberSetsRequested, int lumberUnitsWithdrawn,
            int timberSetsCovered, List<string> lumberProvenanceChains)
        {
            TimberSetsRequested = Math.Max(0, timberSetsRequested);
            LumberUnitsWithdrawn = Math.Max(0, lumberUnitsWithdrawn);
            TimberSetsCovered = Math.Max(0, timberSetsCovered);
            ShortfallSets = Math.Max(0, TimberSetsRequested - TimberSetsCovered);
            if (lumberProvenanceChains != null)
                LumberProvenanceChains.AddRange(lumberProvenanceChains);
        }
    }

    /// <summary>
    /// D3C: the caller-side wiring W8D left open — withdrawing timber sets
    /// from a real lumber stock (W4 sawmill/lumberyard) so the timbering
    /// ledger's provenance requirement can actually be satisfied. The mine
    /// task catalog declares timber-set inputs; this requisition converts
    /// sets into lumber units and withdraws them from the named stock.
    ///
    /// The lumber-units-per-timber-set conversion is a REQUIRED caller
    /// parameter (TUNING / calibration): the Canon marks mine timber
    /// capacities a calibration item and forbids inventing the number to
    /// complete the feature.
    /// </summary>
    public static class MineSupplyRequisition
    {
        /// <summary>
        /// Withdraws timber sets from a lumberyard stock. Returns the
        /// withdrawal with provenance; a shortfall is diagnosed loudly and
        /// the caller must decide (wait, buy elsewhere, or stop work) —
        /// nothing is auto-ordered.
        /// </summary>
        public static MineTimberWithdrawal WithdrawTimberSets(LumberYardLumberStock yardStock,
            int timberSetsNeeded, int lumberUnitsPerTimberSet, List<string> callerDiagnostics)
        {
            callerDiagnostics = callerDiagnostics ?? new List<string>();
            if (yardStock == null)
            {
                callerDiagnostics.Add("MineSupplyRequisition.WithdrawTimberSets: a real lumber stock is required — timber never comes from nowhere.");
                return null;
            }
            if (timberSetsNeeded <= 0)
            {
                callerDiagnostics.Add("MineSupplyRequisition.WithdrawTimberSets: timber sets needed must be positive.");
                return null;
            }
            if (lumberUnitsPerTimberSet <= 0)
            {
                callerDiagnostics.Add("MineSupplyRequisition.WithdrawTimberSets: lumber units per timber set must be positive (TUNING — set by the caller, never invented here).");
                return null;
            }

            int unitsRequested = timberSetsNeeded * lumberUnitsPerTimberSet;
            var diag = new List<string>();
            List<LumberYardLumberDispenseLine> lines =
                yardStock.TryWithdrawUnits(unitsRequested, null, null, diag);

            int unitsWithdrawn = 0;
            var provenance = new List<string>();
            foreach (LumberYardLumberDispenseLine line in lines)
            {
                unitsWithdrawn += Math.Max(0, line.UnitsTaken);
                if (!string.IsNullOrWhiteSpace(line.ProvenanceChain))
                    provenance.Add(line.ProvenanceChain);
            }
            foreach (string line in diag)
                callerDiagnostics.Add($"MineSupplyRequisition <- lumberyard: {line}");

            int setsCovered = unitsWithdrawn / lumberUnitsPerTimberSet;
            int remainder = unitsWithdrawn % lumberUnitsPerTimberSet;
            var withdrawal = new MineTimberWithdrawal(timberSetsNeeded, unitsWithdrawn, setsCovered, provenance);
            if (!withdrawal.FullyCovered)
            {
                callerDiagnostics.Add($"MineSupplyRequisition.WithdrawTimberSets: shortfall — {withdrawal.ShortfallSets} of {timberSetsNeeded} timber sets uncovered " +
                    $"({unitsWithdrawn}/{unitsRequested} lumber units from yard {yardStock.YardBusinessId}). Work waits; nothing auto-ordered.");
            }
            else if (remainder > 0)
            {
                callerDiagnostics.Add($"MineSupplyRequisition.WithdrawTimberSets: {remainder} lumber units withdrawn beyond whole sets — returned to the caller's handling, not the ledger.");
            }
            return withdrawal;
        }
    }
}
