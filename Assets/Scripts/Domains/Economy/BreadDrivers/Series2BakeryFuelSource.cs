using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Bakery;

namespace LandLedgers.Economy.BreadDrivers
{
    /// <summary>
    /// Series 2 Pass 6 (bread drivers): the bakery driver's fuel boundary.
    /// The driver never knows where fuel comes from. It asks this interface
    /// how much fuel is on hand for the batches it is about to bake, and
    /// asks it to supply fuel into the shop's fuel store before WorkDay
    /// burns it. Pass 8 (fuel production) implements this interface with
    /// a dealer/off-map source and swaps it in without editing Pass 6.
    /// No fuel is ever conjured: a source that cannot supply refuses,
    /// and the driver then refuses the bake, loudly, with batches still
    /// planned. Plain C#, no Unity dependency, Roslyn-testable.
    /// </summary>
    public interface ISeries2BakeryFuelSource
    {
        /// <summary>Fuel units available for baking, in BakeryBreadCatalog fuel units.</summary>
        int FuelUnitsOnHand();

        /// <summary>
        /// Moves (or verifies) enough fuel into <paramref name="bakery"/>'s
        /// fuel store for <paramref name="unitsNeeded"/> firings. Returns
        /// null on success, or a refusal diagnostic on failure, in which
        /// case the driver must not call WorkDay.
        /// </summary>
        string SupplyTo(BakeryShopRuntime bakery, int unitsNeeded, int dayIndex, List<string> diag);
    }

    /// <summary>
    /// The Pass 6 in-stock source: fuel already sitting in the shop's own
    /// fuel store counts, and SupplyTo verifies it without moving anything.
    /// This is the stand-in Pass 8 replaces: a dealer source's SupplyTo
    /// will receive real dealer/off-map lots into the fuel store first,
    /// then verify, using the same signature.
    /// </summary>
    public sealed class Series2InStockBakeryFuelSource : ISeries2BakeryFuelSource
    {
        private readonly BakeryShopRuntime bakery;

        public Series2InStockBakeryFuelSource(BakeryShopRuntime bakery)
        {
            this.bakery = bakery;
        }

        public int FuelUnitsOnHand()
        {
            if (bakery == null || bakery.FuelStock == null) return 0;
            return bakery.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId);
        }

        public string SupplyTo(BakeryShopRuntime target, int unitsNeeded, int dayIndex, List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (target == null || target.FuelStock == null)
                return "Series2InStockBakeryFuelSource: no bakery fuel store to verify.";
            int onHand = target.FuelStock.UnitsOnHand(BakeryBreadCatalog.FuelItemId);
            if (onHand < unitsNeeded)
                return $"Series2InStockBakeryFuelSource: fuel store holds {onHand}u, need {unitsNeeded}u; nothing supplied, nothing baked.";
            diag.Add($"Series2InStockBakeryFuelSource: {onHand}u fuel on hand covers {unitsNeeded}u needed; no movement required.");
            return null;
        }
    }
}
