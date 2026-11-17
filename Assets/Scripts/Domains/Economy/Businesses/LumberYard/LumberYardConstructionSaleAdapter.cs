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
    ///
    /// D6 (F-D2-3): hardware is sold per kind, never as a bucket. Each
    /// hardware requirement is resolved to its kind lines
    /// (ConstructionHardwareKindBreakdown), the yard fulfills each kind
    /// from real lots of that kind, and the sale carries one line per
    /// kind sold — so the site stockpile stages kinds and the package
    /// stages against them. Every kind line is priced at the
    /// requirement's bucket price (the price port prices the whole
    /// hardware requirement from the first stocked lot), so the sale
    /// total is unchanged by the split.
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
            // D6: hardware aggregated per kind across the package's
            // requirements (display name -> units), never as a bucket total.
            var hardwareKinds = new Dictionary<string, int>(StringComparer.Ordinal);
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
                        // D6: resolve the requirement to its kind lines
                        // now — the yard fulfills kinds, never a bucket
                        // total, and a mix that cannot be resolved
                        // refuses the sale before anything is sold.
                        List<ConstructionHardwareKindLine> kindLines;
                        string breakdownRefusal = ConstructionHardwareKindBreakdown.TryBreakdown(requirement, out kindLines);
                        if (breakdownRefusal != null)
                            return $"Lumber Yard cannot sell '{requirement.MaterialDisplayName}' for package '{packageId}': {breakdownRefusal}";
                        foreach (ConstructionHardwareKindLine kindLine in kindLines)
                        {
                            hardwareKinds.TryGetValue(kindLine.DisplayName, out int have);
                            hardwareKinds[kindLine.DisplayName] = checked(have + kindLine.Units);
                        }
                        break;
                    default:
                        return $"Lumber Yard does not stock construction material kind '{requirement.MaterialKind}'.";
                }
            }

            var yardKindLines = new List<LumberYardHardwareKindLine>();
            foreach (var pair in hardwareKinds)
            {
                LumberYardHardwareKind kind;
                if (!LumberYardHardwareKindNames.TryKindForDisplayName(pair.Key, out kind))
                    return $"Lumber Yard cannot sell kind '{pair.Key}' — it is not a stocked hardware kind.";
                yardKindLines.Add(new LumberYardHardwareKindLine { Kind = kind, Units = pair.Value });
            }
            yardKindLines.Sort((a, b) => string.Compare(
                LumberYardHardwareKindNames.DisplayNameFor(a.Kind),
                LumberYardHardwareKindNames.DisplayNameFor(b.Kind), StringComparison.Ordinal));

            LumberYardSaleRecord yardSale = yard.TrySellToConstructionProject(
                projectId, lumberUnits, Math.Max(0, lumberPrice),
                yardKindLines, Math.Max(0, hardwarePrice), dayIndex, diagnostics);
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
                if (line.ResourceKind == ConstructionResourceKind.Lumber)
                {
                    sale.Lines.Add(new ConstructionMaterialSaleLine
                    {
                        DisplayName = lumberDisplayName,
                        Units = line.UnitsSold,
                        UnitLabel = "units",
                        UnitPriceCents = line.UnitPriceCents,
                        SupplierBusinessId = supplierBusinessId,
                        Provenance = string.Join("; ", line.ProvenanceLines()),
                    });
                    continue;
                }
                // D6: one construction sale line per kind sold — the yard's
                // dispense lines already carry the kind, so group them and
                // record each kind with its own units and provenance, all
                // priced at the requirement's bucket price.
                var byKind = new Dictionary<LumberYardHardwareKind, List<LumberYardHardwareDispenseLine>>();
                foreach (LumberYardHardwareDispenseLine dispense in line.HardwareLines)
                {
                    if (dispense == null || dispense.UnitsTaken <= 0) continue;
                    List<LumberYardHardwareDispenseLine> kindDispense;
                    if (!byKind.TryGetValue(dispense.HardwareKind, out kindDispense))
                    {
                        kindDispense = new List<LumberYardHardwareDispenseLine>();
                        byKind[dispense.HardwareKind] = kindDispense;
                    }
                    kindDispense.Add(dispense);
                }
                var kinds = new List<LumberYardHardwareKind>(byKind.Keys);
                kinds.Sort((a, b) => string.Compare(
                    LumberYardHardwareKindNames.DisplayNameFor(a),
                    LumberYardHardwareKindNames.DisplayNameFor(b), StringComparison.Ordinal));
                foreach (LumberYardHardwareKind kind in kinds)
                {
                    int kindUnits = 0;
                    var provenance = new List<string>();
                    foreach (LumberYardHardwareDispenseLine dispense in byKind[kind])
                    {
                        kindUnits += dispense.UnitsTaken;
                        provenance.Add($"{dispense.UnitsTaken} {dispense.HardwareKind} from yard lot {dispense.LotId}: {dispense.ProvenanceChain}");
                    }
                    sale.Lines.Add(new ConstructionMaterialSaleLine
                    {
                        DisplayName = LumberYardHardwareKindNames.DisplayNameFor(kind),
                        Units = kindUnits,
                        UnitLabel = "units",
                        UnitPriceCents = line.UnitPriceCents,
                        SupplierBusinessId = supplierBusinessId,
                        Provenance = string.Join("; ", provenance.ToArray()),
                    });
                }
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
