using System;
using System.Collections.Generic;
using LandLedgers.Economy;

namespace LandLedgers.Economy.Businesses.LumberYard
{
    /// <summary>
    /// Bridges the construction procurement port to the existing finite
    /// Lumber Yard lot and sale authorities. The adapter records material
    /// custody only; the construction procurement service posts the cash
    /// transfer through its existing ledger path.
    /// </summary>
    public sealed class LumberYardConstructionSaleAdapter : IConstructionMaterialSalePort
    {
        private readonly LumberYardShopRuntime yard;

        public LumberYardConstructionSaleAdapter(LumberYardShopRuntime yard)
        {
            this.yard = yard;
        }

        public string TrySellToProject(
            string supplierBusinessId,
            string projectId,
            string packageId,
            List<ConstructionMaterialRequirement> requirements,
            IConstructionMaterialPricePort pricePort,
            int dayIndex,
            List<string> diagnostics,
            out ConstructionMaterialSale sale)
        {
            diagnostics = diagnostics ?? new List<string>();
            sale = null;
            if (yard == null)
                return "Lumber Yard sale adapter has no yard runtime.";
            if (!string.Equals(supplierBusinessId, yard.BusinessInstanceId, StringComparison.Ordinal))
                return "Named construction supplier does not match this Lumber Yard.";
            if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(packageId))
                return "Construction material sales require a project and package identity.";
            if (pricePort == null || requirements == null || requirements.Count == 0)
                return "Construction material sale requires priced material requirements.";

            int lumberUnits = 0;
            int hardwareUnits = 0;
            int lumberPrice = -1;
            int hardwarePrice = -1;
            foreach (ConstructionMaterialRequirement requirement in requirements)
            {
                if (requirement == null || !requirement.IsCoherent)
                    return "Construction package contains an incoherent material requirement.";
                if (!string.IsNullOrWhiteSpace(requirement.SupplierBusinessId)
                    && !string.Equals(requirement.SupplierBusinessId, supplierBusinessId, StringComparison.Ordinal))
                    return $"Requirement '{requirement.RequirementId}' names another supplier.";

                int price = pricePort.UnitPriceCents(requirement);
                if (price < 0)
                    return $"No real unit price is available for '{requirement.MaterialDisplayName}'.";

                switch (requirement.MaterialKind)
                {
                    case ConstructionMaterialKind.Lumber:
                        if (lumberPrice >= 0 && lumberPrice != price)
                            return "Lumber Yard sales require one consistent lumber unit price per package.";
                        lumberPrice = price;
                        lumberUnits = checked(lumberUnits + requirement.RequiredUnits);
                        break;
                    case ConstructionMaterialKind.Hardware:
                        if (hardwarePrice >= 0 && hardwarePrice != price)
                            return "Lumber Yard sales require one consistent hardware unit price per package.";
                        hardwarePrice = price;
                        hardwareUnits = checked(hardwareUnits + requirement.RequiredUnits);
                        break;
                    default:
                        return $"Lumber Yard does not stock construction material kind '{requirement.MaterialKind}'.";
                }
            }

            LumberYardSaleRecord yardSale = yard.TrySellToConstructionProject(
                projectId, lumberUnits, Math.Max(0, lumberPrice),
                hardwareUnits, Math.Max(0, hardwarePrice), dayIndex, diagnostics);
            if (yardSale == null)
                return "Lumber Yard refused the sale; finite inventory was left unchanged.";

            sale = new ConstructionMaterialSale
            {
                SaleId = yardSale.SaleId.ToString(),
                SupplierBusinessId = supplierBusinessId,
                ProjectId = projectId,
                PackageId = packageId,
                DayIndex = dayIndex,
            };

            foreach (LumberYardMaterialSaleLine line in yardSale.Lines)
            {
                if (line == null || line.UnitsSold <= 0) continue;
                ConstructionMaterialKind materialKind = line.ResourceKind == ConstructionResourceKind.Lumber
                    ? ConstructionMaterialKind.Lumber
                    : ConstructionMaterialKind.Hardware;
                sale.Lines.Add(new ConstructionMaterialSaleLine
                {
                    DisplayName = materialKind == ConstructionMaterialKind.Lumber ? "Lumber" : "Nails and hardware",
                    Units = line.UnitsSold,
                    UnitLabel = "units",
                    UnitPriceCents = line.UnitPriceCents,
                    SupplierBusinessId = supplierBusinessId,
                    Provenance = string.Join("; ", line.ProvenanceLines()),
                });
            }

            if (sale.TotalCents != yardSale.TotalCents)
            {
                diagnostics.Add($"LumberYardConstructionSaleAdapter: sale total mismatch ({sale.TotalCents}c vs {yardSale.TotalCents}c). Inventory sale remains recorded for audit.");
                return "Construction sale mapping did not preserve the Yard's recorded total.";
            }

            return null;
        }
    }
}
