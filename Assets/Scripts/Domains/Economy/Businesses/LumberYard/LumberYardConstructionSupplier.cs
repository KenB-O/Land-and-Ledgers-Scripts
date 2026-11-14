using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Population;

namespace LandLedgers.Economy.Businesses.LumberYard
{
    /// <summary>
    /// Slice C3: the yard as a supplier-directory supplier. Reads the
    /// yard's real stock — sale-available lumber (earmarks excluded,
    /// Canon 6.4) and hardware units — so
    /// ConstructionProcurementService.DiscoverSupplier finds the yard
    /// for construction-lumber and construction-hardware. Prices are
    /// informational only: a 1-unit probe requirement quoted through
    /// the C1 price port (clamped to 0 when unpriced); the sale total
    /// is always charged from the port via the sale adapter at sale
    /// time, never from this directory price. Directory sales are
    /// refused: the yard sells construction material only through the
    /// construction sale adapter, so Sell always returns 0 and moves
    /// no stock. The port may be null (HasCategory/StockUnits still
    /// answer; PricePerUnitCents returns 0). Never throws on an
    /// empty yard.
    /// </summary>
    public sealed class LumberYardConstructionSupplier : IGoodsSupplier
    {
        private readonly LumberYardShopRuntime yard;
        private readonly IConstructionMaterialPricePort pricePort;

        public LumberYardConstructionSupplier(LumberYardShopRuntime yard, IConstructionMaterialPricePort pricePort)
        {
            this.yard = yard;
            this.pricePort = pricePort;
        }

        public string SupplierBusinessId => yard != null ? yard.BusinessInstanceId : string.Empty;

        public string SupplierName => yard != null ? yard.BusinessInstanceId : string.Empty;

        public string LocationId => "business:" + SupplierBusinessId;

        public bool HasCategory(string categoryId)
        {
            return StockUnits(categoryId) > 0;
        }

        public int StockUnits(string categoryId)
        {
            if (yard == null) return 0;
            if (string.Equals(categoryId, "construction-lumber", StringComparison.Ordinal))
            {
                if (yard.LumberStock == null) return 0;
                return Math.Max(0, yard.LumberStock.AvailableUnitsForSale(null, null));
            }
            if (string.Equals(categoryId, "construction-hardware", StringComparison.Ordinal))
            {
                if (yard.HardwareStock == null) return 0;
                return Math.Max(0, yard.HardwareStock.AvailableUnits(null));
            }
            return 0;
        }

        public int PricePerUnitCents(string categoryId)
        {
            if (pricePort == null) return 0;
            ConstructionMaterialRequirement probe = ProbeRequirement(categoryId);
            if (probe == null) return 0;
            return Math.Max(0, pricePort.UnitPriceCents(probe));
        }

        public int Sell(string categoryId, int requestedUnits, int dayIndex, List<string> diagnostics)
        {
            if (diagnostics != null)
                diagnostics.Add("LumberYardConstructionSupplier: the yard sells construction material only through the construction sale adapter — directory sales are refused.");
            return 0;
        }

        public static void RegisterIn(SupplierDirectory directory, LumberYardShopRuntime yard, IConstructionMaterialPricePort pricePort)
        {
            if (directory == null || yard == null) return;
            directory.Register(new LumberYardConstructionSupplier(yard, pricePort));
        }

        private static ConstructionMaterialRequirement ProbeRequirement(string categoryId)
        {
            if (string.Equals(categoryId, "construction-lumber", StringComparison.Ordinal))
            {
                return new ConstructionMaterialRequirement
                {
                    RequirementId = "price-probe",
                    MaterialKind = ConstructionMaterialKind.Lumber,
                    MaterialDisplayName = "lumber",
                    RequiredUnits = 1,
                    UnitLabel = "board feet",
                };
            }
            if (string.Equals(categoryId, "construction-hardware", StringComparison.Ordinal))
            {
                return new ConstructionMaterialRequirement
                {
                    RequirementId = "price-probe",
                    MaterialKind = ConstructionMaterialKind.Hardware,
                    MaterialDisplayName = "nails",
                    RequiredUnits = 1,
                    UnitLabel = "units",
                };
            }
            return null;
        }
    }
}
