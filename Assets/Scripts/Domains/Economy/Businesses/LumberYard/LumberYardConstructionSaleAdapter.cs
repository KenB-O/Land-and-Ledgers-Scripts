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
            // C-9: the sale line must carry the requirement's own display
            // name, not a hard-coded label. The site stockpile matches
            // delivered lines to the package requirements by exact display
            // name, and every production design names lumber "lumber" —
            // the hard-coded "Lumber" label stranded paid-for materials at
            // the site, unstaged, on every real chain run. One consistent
            // name per kind, same shape as the price-consistency rule.
            string lumberDisplayName = null;
            string hardwareDisplayName = null;
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
                        if (lumberDisplayName != null
                            && !string.Equals(lumberDisplayName, requirement.MaterialDisplayName, StringComparison.Ordinal))
                            return "Lumber Yard sales require one consistent lumber display name per package.";
                        lumberDisplayName = requirement.MaterialDisplayName;
                        lumberPrice = price;
                        lumberUnits = checked(lumberUnits + requirement.RequiredUnits);
                        break;
                    case ConstructionMaterialKind.Hardware:
                        if (hardwarePrice >= 0 && hardwarePrice != price)
                            return "Lumber Yard sales require one consistent hardware unit price per package.";
                        if (hardwareDisplayName != null
                            && !string.Equals(hardwareDisplayName, requirement.MaterialDisplayName, StringComparison.Ordinal))
                            return "Lumber Yard sales require one consistent hardware display name per package.";
                        hardwareDisplayName = requirement.MaterialDisplayName;
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
                    DisplayName = materialKind == ConstructionMaterialKind.Lumber ? lumberDisplayName : hardwareDisplayName,
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

        /// <summary>
        /// G1: voids a recorded yard sale in full through the yard
        /// runtime — every unit returns to saleable stock, the record is
        /// kept marked voided for audit, and the voided value is returned
        /// for the caller to refund or un-bill. Returns the refusal, or
        /// null on success.
        /// </summary>
        public string TryVoidSale(
            ConstructionMaterialSale sale,
            int dayIndex,
            List<string> diagnostics,
            out int voidedCents)
        {
            diagnostics = diagnostics ?? new List<string>();
            voidedCents = 0;
            if (yard == null)
                return "Lumber Yard sale adapter has no yard runtime.";
            if (sale == null || string.IsNullOrWhiteSpace(sale.SaleId))
                return "Voiding a construction sale requires the recorded sale.";
            if (!string.Equals(sale.SupplierBusinessId, yard.BusinessInstanceId, StringComparison.Ordinal))
                return "Named construction supplier does not match this Lumber Yard.";
            return yard.VoidSale(sale.SaleId, dayIndex, diagnostics, out voidedCents);
        }
    }
}
