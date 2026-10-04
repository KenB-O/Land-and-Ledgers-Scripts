using EntityId = LandLedgers.Primitives.EntityId;

using System.Collections.Generic;
using LandLedgers.Economy.Trade;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Businesses.LumberYard
{
    /// <summary>
    /// W4B: the lumber yard's supply links. Survey result — nails and simple
    /// hardware are currently supplied by the local BLACKSMITH (forged from
    /// EQU-1 imported iron stock), so the primary path is a named smith, not a
    /// new import. The import registration below is the MINIMAL EQU-1
    /// off-map fallback for hardware (the same honest pattern as the
    /// blacksmith's own iron and the doctor's medicines): a named origin,
    /// real distance and transit days, never a vague "imported" flag.
    /// Historical: 1870s frontier hardware — cut nails, hinges, latches —
    /// came from eastern ironworks by rail; wire nails do not dominate until
    /// the 1880s-90s. Prices/transit are calibration (Canon Part XV).
    /// Lumber needs no new registration: ImportCatalog already carries
    /// "lumber" (off-map timber country, via wagon road).
    /// </summary>
    public static class LumberYardSupply
    {
        public const string HardwareImportMaterialId = "nails-hardware";

        /// <summary>
        /// Registers nails/simple hardware as an importable material (EQU-1
        /// extension path). The origin matches the blacksmith's iron source
        /// (Pittsburgh ironworks, via railhead) — the same trade corridor the
        /// town already uses for iron stock.
        /// </summary>
        public static void EnsureHardwareImportable(List<string> diag)
        {
            string problem = ImportCatalog.RegisterMaterial(new ImportMaterial(
                HardwareImportMaterialId, "Nails and simple hardware (units)",
                "Pittsburgh ironworks, via railhead", 220, 14, 30));
            if (problem != null && diag != null)
            {
                diag.Add($"LumberYardSupply: hardware import registration: {problem}");
            }
            else if (diag != null)
            {
                ImportMaterial material = ImportCatalog.Get(HardwareImportMaterialId);
                diag.Add($"LumberYardSupply: hardware importable — '{material.DisplayName}' from "
                    + $"{material.OriginName} ({material.DistanceMiles} mi, {material.TransitDays} days transit).");
            }
        }
    }

    /// <summary>
    /// W4B: the yard's one-time opening stock. An opening lumber yard needs
    /// shelves on day one, but the upstream-provenance doctrine forbids
    /// synthetic stock — so the opening inventory is an EXPLICIT, marked,
    /// one-time bootstrap endowment (the Doctor W1A precedent): it never
    /// auto-replenishes, and reorders flow only through real intakes
    /// (sawmill lots, named purchases, named import orders). The once-guard
    /// lives on the owning runtime and is serialized with it.
    /// </summary>
    public static class LumberYardOpeningStock
    {
        public const string BootstrapSupplierNote =
            "Opening stock (one-time endowment; reorder via sawmill lots, named purchases, or import orders only).";

        /// <summary>
        /// Applies the one-time opening endowment to a yard runtime. Refuses
        /// loudly if opening stock was already applied — the endowment is
        /// one-time, never repeated. Returns the refusal, or null.
        /// </summary>
        public static string ApplyOneTime(
            LumberYardShopRuntime runtime,
            int lumberUnits,
            string lumberSpecies,
            int nailsUnits,
            int simpleHardwareUnits,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (runtime == null)
                return "LumberYardOpeningStock: no yard runtime — opening stock goes to a real yard.";
            if (runtime.OpeningStockApplied)
                return "LumberYardOpeningStock: opening stock was already applied — the endowment is one-time, it never repeats.";
            if (lumberUnits < 0 || nailsUnits < 0 || simpleHardwareUnits < 0)
                return "LumberYardOpeningStock: endowment units cannot be negative.";

            var ids = runtime.IdRegistry;
            if (ids == null)
                return "LumberYardOpeningStock: no EntityIdRegistry — opening stock refused.";

            if (lumberUnits > 0)
            {
                string refusal = runtime.LumberStock.ReceiveBootstrapEndowment(new LumberYardLumberLot
                {
                    LotId = ids.Allocate(EntityKind.Lot),
                    LumberUnits = lumberUnits,
                    YardGradeId = LumberYardGradeCatalog.DefaultGradeId,
                    Species = lumberSpecies ?? string.Empty,
                    AcquiredDayIndex = dayIndex,
                    SourceKind = LumberYardLumberSourceKind.Bootstrap,
                    IsBootstrapEndowment = true,
                    SupplierNote = BootstrapSupplierNote,
                }, diag);
                if (refusal != null)
                {
                    diag.Add($"LumberYardOpeningStock: lumber endowment failed — {refusal}");
                    return refusal;
                }
            }

            if (nailsUnits > 0)
            {
                string refusal = runtime.HardwareStock.ReceiveBootstrapEndowment(new LumberYardHardwareLot
                {
                    LotId = ids.Allocate(EntityKind.Lot),
                    HardwareUnits = nailsUnits,
                    HardwareKind = LumberYardHardwareKind.Nails,
                    AcquiredDayIndex = dayIndex,
                    IsBootstrapEndowment = true,
                    SupplierNote = BootstrapSupplierNote,
                }, diag);
                if (refusal != null)
                {
                    diag.Add($"LumberYardOpeningStock: nails endowment failed — {refusal}");
                    return refusal;
                }
            }

            if (simpleHardwareUnits > 0)
            {
                string refusal = runtime.HardwareStock.ReceiveBootstrapEndowment(new LumberYardHardwareLot
                {
                    LotId = ids.Allocate(EntityKind.Lot),
                    HardwareUnits = simpleHardwareUnits,
                    HardwareKind = LumberYardHardwareKind.SimpleHardware,
                    AcquiredDayIndex = dayIndex,
                    IsBootstrapEndowment = true,
                    SupplierNote = BootstrapSupplierNote,
                }, diag);
                if (refusal != null)
                {
                    diag.Add($"LumberYardOpeningStock: hardware endowment failed — {refusal}");
                    return refusal;
                }
            }

            runtime.MarkOpeningStockApplied();
            diag.Add("LumberYardOpeningStock: BOOTSTRAP endowment applied (one-time opening stock). "
                + "Upstream-provenance doctrine: these lots are explicitly marked and will never auto-replenish.");
            return null;
        }
    }
}
