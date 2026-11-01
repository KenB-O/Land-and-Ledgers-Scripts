using System;
using System.Collections.Generic;
using LandLedgers.Animals;

namespace LandLedgers.Economy.Butcher
{
    /// <summary>
    /// SWN-1: species-specific carcass yield fractions. Every species' fractions
    /// sum to 1.0 — meat + hide + tallow + waste always equals the whole animal
    /// (GHOST-DES-010), so there is no free product for any species.
    ///
    /// All numbers are calibration, not canon (Canon Part XV holds). Cattle
    /// returns the exact 1870 beef fractions the butcher has always used.
    /// </summary>
    public static class CarcassYieldFractions
    {
        public static List<(CarcassProduct product, float fraction)> ForSpecies(AnimalSpecies species)
        {
            switch (species)
            {
                case AnimalSpecies.Pig:
                    // Hogs dress high: mostly pork cuts + lard (rendered fat).
                    return new List<(CarcassProduct, float)>
                    {
                        (CarcassProduct.RetailCuts, 0.55f),
                        (CarcassProduct.Hide, 0.00f),
                        (CarcassProduct.Tallow, 0.10f), // lard
                        (CarcassProduct.Waste, 0.35f),
                    };
                case AnimalSpecies.Sheep:
                    // Mutton + sheepskin + tallow.
                    return new List<(CarcassProduct, float)>
                    {
                        (CarcassProduct.RetailCuts, 0.45f),
                        (CarcassProduct.Hide, 0.10f), // sheepskin
                        (CarcassProduct.Tallow, 0.05f),
                        (CarcassProduct.Waste, 0.40f),
                    };
                case AnimalSpecies.Cattle:
                default:
                    // GHOST-DES-010: standard 1870 beef yield fractions.
                    return new List<(CarcassProduct, float)>
                    {
                        (CarcassProduct.RetailCuts, 0.42f),
                        (CarcassProduct.Hide, 0.08f),
                        (CarcassProduct.Tallow, 0.05f),
                        (CarcassProduct.Waste, 0.45f),
                    };
            }
        }
    }
}
