using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Businesses.LumberYard;

namespace LandLedgers.Orchestration.Systems
{
    /// <summary>
    /// Slice 02: the yard backed IConstructionMaterialPricePort over the
    /// existing LumberYardShopRuntime, so every construction pricing check
    /// can read a real price instead of refusing with unpriced. The yard
    /// runtime is complete and tested but has no production caller; this
    /// port is a tested adapter with no production binding, because its
    /// binding needs a yard runtime owned by the hub (C2 landed the hub
    /// ownership), and the binding itself is C3.
    ///
    /// Lumber is priced with the yard QuoteRetailLumberPrice under no
    /// species or grade filter, because the requirement GradeId is a yard
    /// grade catalog id only when present and no filter may be invented
    /// from it. The price policy defaults to the class default (25 percent
    /// markup, no grade multipliers); a caller may supply a priced policy.
    /// The price port and the sale port agree on every lumber price on
    /// this line because the adapter prices through this port (it calls
    /// UnitPriceCents per requirement) and the sale charges the port's
    /// quote verbatim (§2.3).
    /// Hardware is priced only from a real yard hardware lot cost
    /// already on the lot, the first stocked lot in FIFO order, which is
    /// the order the yard itself withdraws in. A lot with no readable
    /// unit cost, or no stocked hardware lot at all, refuses with minus
    /// one rather than inventing a price. Masonry, paint, glass, roofing
    /// and anything else return minus one; they are never mapped to
    /// lumber or hardware.
    ///
    /// Never moves money. Never creates a lot, never seeds stock, never
    /// calls the bootstrap endowment. Never touches the category level
    /// lumber fallback or the Regional freight hardware path.
    /// </summary>
    public sealed class LumberYardConstructionPricePort : IConstructionMaterialPricePort
    {
        private readonly LumberYardShopRuntime yard;
        private readonly LumberYardGradePricePolicy pricePolicy;

        public LumberYardConstructionPricePort(
            LumberYardShopRuntime yard,
            LumberYardGradePricePolicy pricePolicy = null)
        {
            this.yard = yard;
            this.pricePolicy = pricePolicy ?? new LumberYardGradePricePolicy();
        }

        public int UnitPriceCents(ConstructionMaterialRequirement requirement)
        {
            if (yard == null || requirement == null) return -1;
            if (requirement.RequiredUnits <= 0) return -1;
            if (requirement.MaterialKind == ConstructionMaterialKind.Lumber)
            {
                return yard.QuoteRetailLumberPrice(null, null, pricePolicy, new List<string>());
            }
            if (requirement.MaterialKind == ConstructionMaterialKind.Hardware)
            {
                return FirstStockedHardwareUnitCost();
            }
            return -1;
        }

        /// <summary>Unit cost of the first stocked hardware lot, in the
        /// order the yard withdraws in. Minus one when no stocked lot
        /// carries a readable unit cost, matching the P6 adapter.</summary>
        private int FirstStockedHardwareUnitCost()
        {
            if (yard.HardwareStock == null || yard.HardwareStock.Lots == null) return -1;
            foreach (LumberYardHardwareLot lot in yard.HardwareStock.Lots)
            {
                if (lot == null || lot.HardwareUnits <= 0) continue;
                return lot.UnitCostCents <= 0 ? -1 : lot.UnitCostCents;
            }
            return -1;
        }
    }
}
