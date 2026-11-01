using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Trade;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Businesses.Bakery
{
    /// <summary>
    /// D1D: one oven-fuel lot with full upstream provenance. Every cordwood
    /// unit the firebox burns must trace to a real lot from a real supplier —
    /// a named FuelDealer (sawmill slabs/offcuts and cordwood flow to the
    /// fuel yard per the FuelDealer profile), a declared import order from a
    /// named off-map origin, or a named local supply relationship — or to the
    /// explicit one-time bootstrap endowment. No orphan lots, no synthetic
    /// stock (upstream-provenance doctrine). Mirrors <see cref="BakeryFlourLot"/>.
    /// </summary>
    [Serializable]
    public sealed class BakeryFuelLot
    {
        public EntityId LotId = EntityId.Invalid; // EntityKind.Lot (HF-1)
        public string FuelName = string.Empty; // "bakery-fuelwood"
        public int Units; // cordwood units
        public int AcquiredDayIndex;

        /// <summary>The FuelDealer business whose yard supplied this fuel.</summary>
        public string FuelDealerBusinessId = string.Empty;

        /// <summary>The dealer's own lot reference this fuel was taken from.</summary>
        public string SourceFuelLotId = string.Empty;

        /// <summary>The declared trade link that brought this lot in (ImportOrder.OrderId, or a local supply relationship id).</summary>
        public string ImportOrderId = string.Empty;

        /// <summary>Named off-map or local origin, e.g. "Off-map cordwood, via railhead".</summary>
        public string OriginName = string.Empty;

        /// <summary>Any extra upstream link (woodcutter name, local purchase).</summary>
        public string SupplierNote = string.Empty;

        /// <summary>
        /// True only for the one-time opening endowment applied by
        /// <see cref="BakeryFuelBootstrap"/>. Explicitly marked, never
        /// silently replenished — reorder goes through fuel dealers and
        /// import orders only.
        /// </summary>
        public bool IsBootstrapEndowment;

        public BakeryFuelLot() { }

        /// <summary>Human-readable upstream chain for ledgers and diagnostics.</summary>
        public string ProvenanceChain()
        {
            if (IsBootstrapEndowment)
            {
                return $"BOOTSTRAP endowment (lot {LotId}, day {AcquiredDayIndex}) — one-time opening stock; reorder via fuel dealers and import orders";
            }

            string chain = $"lot {LotId} / day {AcquiredDayIndex}";
            if (!string.IsNullOrWhiteSpace(FuelDealerBusinessId))
            {
                chain += $" / fuel dealer {FuelDealerBusinessId}";
                if (!string.IsNullOrWhiteSpace(SourceFuelLotId))
                {
                    chain += $" ← dealer lot {SourceFuelLotId}";
                }
            }

            string order = string.IsNullOrWhiteSpace(ImportOrderId) ? "no-order" : ImportOrderId;
            string origin = string.IsNullOrWhiteSpace(OriginName) ? "unnamed-origin" : OriginName;
            chain += $" / import order {order} / {origin}";
            if (!string.IsNullOrWhiteSpace(SupplierNote))
            {
                chain += $" / {SupplierNote}";
            }

            return chain;
        }
    }

    /// <summary>
    /// D1D: one line of a fuel dispense result — which lot the units came from
    /// and the full upstream chain. Baked bread lots carry these so the firing
    /// fuel is traceable into the bread, the same way flour is.
    /// </summary>
    [Serializable]
    public sealed class BakeryFuelDispenseLine
    {
        public EntityId LotId = EntityId.Invalid;
        public string FuelName = string.Empty;
        public int UnitsTaken;
        public string ProvenanceChain = string.Empty;

        public BakeryFuelDispenseLine() { }
    }

    /// <summary>
    /// D1D: the bakery's fuel store (cordwood for the oven firebox). Units
    /// dispense FIFO (oldest stock first); every dispense returns the lots
    /// consumed so firings carry provenance. Refusals are loud — a firing
    /// without fuel is never faked. Mirrors <see cref="BakeryFlourStock"/>.
    /// </summary>
    public sealed class BakeryFuelStock
    {
        private readonly List<BakeryFuelLot> lots = new List<BakeryFuelLot>();
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<BakeryFuelLot> Lots => lots;

        /// <summary>
        /// Receives a lot into the fuel store. Lots must name their supplier
        /// chain (fuel dealer + dealer lot, or import order + origin) or carry
        /// the explicit bootstrap flag. Returns a rejection string, or null on
        /// success.
        /// </summary>
        public string ReceiveLot(BakeryFuelLot lot, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (lot == null) return "BakeryFuelStock: null lot refused — no fuel without a lot record.";
            if (string.IsNullOrWhiteSpace(lot.FuelName))
                return "BakeryFuelStock: lot refused — fuel name is required.";
            if (lot.Units <= 0)
                return $"BakeryFuelStock: lot refused — '{lot.FuelName}' needs a positive unit count.";
            if (!lot.IsBootstrapEndowment
                && string.IsNullOrWhiteSpace(lot.FuelDealerBusinessId)
                && (string.IsNullOrWhiteSpace(lot.ImportOrderId) || string.IsNullOrWhiteSpace(lot.OriginName)))
                return $"BakeryFuelStock: lot refused — '{lot.FuelName}' names no fuel dealer and no import order/origin. No orphan lots.";
            if (lot.LotId.IsValid)
            {
                foreach (var existing in lots)
                {
                    if (existing.LotId.Equals(lot.LotId))
                        return $"BakeryFuelStock: lot refused — lot {lot.LotId} already in the fuel store.";
                }
            }

            lots.Add(lot);
            diag.Add($"BakeryFuelStock: received {lot.Units} units '{lot.FuelName}' — {lot.ProvenanceChain()}.");
            return null;
        }

        /// <summary>
        /// Dispenses units FIFO (oldest first). Returns null with a diagnostic
        /// when the store cannot cover the request — no units are conjured.
        /// </summary>
        public List<BakeryFuelDispenseLine> TryDispenseUnits(string fuelName, int units, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var lines = new List<BakeryFuelDispenseLine>();
            if (string.IsNullOrWhiteSpace(fuelName) || units <= 0)
            {
                diag.Add("BakeryFuelStock: dispense refused — fuel name and positive unit count required.");
                return null;
            }

            int available = 0;
            foreach (var lot in lots)
            {
                if (string.Equals(lot.FuelName, fuelName, StringComparison.OrdinalIgnoreCase))
                {
                    available += lot.Units;
                }
            }

            if (available < units)
            {
                diag.Add($"BakeryFuelStock: only {available} units of '{fuelName}' on hand, need {units} — shortfall, no units conjured.");
                return null;
            }

            // FIFO by acquisition day.
            var ordered = new List<BakeryFuelLot>();
            foreach (var lot in lots)
            {
                if (string.Equals(lot.FuelName, fuelName, StringComparison.OrdinalIgnoreCase))
                {
                    ordered.Add(lot);
                }
            }

            ordered.Sort((a, b) => a.AcquiredDayIndex.CompareTo(b.AcquiredDayIndex));

            int remaining = units;
            foreach (var lot in ordered)
            {
                if (remaining <= 0) break;
                int take = Math.Min(remaining, lot.Units);
                lot.Units -= take;
                remaining -= take;
                lines.Add(new BakeryFuelDispenseLine
                {
                    LotId = lot.LotId,
                    FuelName = lot.FuelName,
                    UnitsTaken = take,
                    ProvenanceChain = lot.ProvenanceChain(),
                });
            }

            // Drop emptied lots so the store never carries ghost stock.
            lots.RemoveAll(l => l.Units <= 0);

            diag.Add($"BakeryFuelStock: dispensed {units} units '{fuelName}' (day {dayIndex}) from {lines.Count} lot(s).");
            return lines;
        }

        /// <summary>Counts all on-hand units of a fuel kind.</summary>
        public int UnitsOnHand(string fuelName)
        {
            int total = 0;
            foreach (var lot in lots)
            {
                if (string.Equals(lot.FuelName, fuelName, StringComparison.OrdinalIgnoreCase))
                {
                    total += lot.Units;
                }
            }

            return total;
        }

        #region Save / Load
        [Serializable]
        public sealed class BakeryFuelStockSaveDto
        {
            public List<BakeryFuelLot> Lots = new List<BakeryFuelLot>();
        }

        public BakeryFuelStockSaveDto CaptureSaveDto()
        {
            var dto = new BakeryFuelStockSaveDto();
            dto.Lots.AddRange(lots);
            return dto;
        }

        public void LoadFromSaveDto(BakeryFuelStockSaveDto dto)
        {
            lots.Clear();
            if (dto?.Lots == null) return;
            lots.AddRange(dto.Lots);
        }
        #endregion
    }

    /// <summary>
    /// D1D: the one-time opening endowment of the bakery's fuel store.
    /// Explicitly marked BOOTSTRAP — it stands in for the shop's opening
    /// cordwood pile and is NEVER silently replenished: later fuel comes only
    /// from fuel dealers or import orders of
    /// <see cref="BakeryFuelSupply.FuelMaterialId"/>.
    /// </summary>
    public static class BakeryFuelBootstrap
    {
        /// <summary>TUNING: opening cordwood units in the fuel store.</summary>
        public const int BootstrapFuelUnits = 40;

        public static void ApplyBootstrapEndowment(
            BakeryFuelStock stock,
            EntityIdRegistry idRegistry,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (stock == null)
            {
                diagnostics.Add("BakeryFuelBootstrap: no fuel stock — endowment not applied.");
                return;
            }

            if (idRegistry == null)
            {
                diagnostics.Add("BakeryFuelBootstrap: no id registry — endowment not applied.");
                return;
            }

            string rejection = stock.ReceiveLot(new BakeryFuelLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                FuelName = BakeryBreadCatalog.FuelItemId,
                Units = BootstrapFuelUnits,
                AcquiredDayIndex = dayIndex,
                IsBootstrapEndowment = true,
                SupplierNote = "Opening shop stocking (one-time; reorder via fuel dealers and import orders only).",
            }, diagnostics);
            if (rejection != null)
            {
                diagnostics.Add($"BakeryFuelBootstrap: {rejection}");
                return;
            }

            diagnostics.Add("BakeryFuelBootstrap: BOOTSTRAP endowment applied (one-time opening stock). " +
                "Upstream-provenance doctrine: this lot is explicitly marked and will never auto-replenish.");
        }
    }

    /// <summary>
    /// D1D: the oven-fuel supply link. The honest paths are a named FuelDealer
    /// (the town fuel yard, supplied by sawmill slabs/offcuts and cordwood per
    /// the FuelDealer profile) or a DECLARED off-map trade link (EQU-1
    /// precedent: named origin, real distance and transit days). The shop
    /// reorders through <see cref="ImportService"/>.
    /// </summary>
    public static class BakeryFuelSupply
    {
        public const string FuelMaterialId = "bakery-fuelwood";

        /// <summary>
        /// Registers oven cordwood as an importable material (EQU-1 extension
        /// path). Price/transit are calibration (Canon Part XV).
        /// </summary>
        public static void EnsureBakeryImportables(List<string> diag)
        {
            string problem = ImportCatalog.RegisterMaterial(new ImportMaterial(
                FuelMaterialId, "Cordwood (oven fuel)",
                "Off-map cordwood, via railhead", 250, 14, 2));
            if (problem != null && diag != null)
            {
                diag.Add($"BakeryFuelSupply: fuel import registration: {problem}");
            }
        }

        /// <summary>
        /// Takes fuelwood from a named FuelDealer into the bakery's fuel store.
        /// The caller moves real custody; this records the lot with provenance
        /// (dealer business id + dealer lot reference). Refuses unnamed supply
        /// loudly.
        /// </summary>
        public static string ReceiveFuelDealerLot(
            BakeryFuelStock stock,
            string fuelDealerBusinessId,
            string dealerLotId,
            int units,
            int dayIndex,
            EntityIdRegistry idRegistry,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (stock == null) return "BakeryFuelSupply.ReceiveFuelDealerLot: no fuel stock.";
            if (string.IsNullOrWhiteSpace(fuelDealerBusinessId))
                return "BakeryFuelSupply.ReceiveFuelDealerLot: the fuel dealer business must be named — no orphan stock.";
            if (units <= 0)
                return "BakeryFuelSupply.ReceiveFuelDealerLot: a positive unit count is required — fuel is not conjured.";
            if (idRegistry == null) return "BakeryFuelSupply.ReceiveFuelDealerLot: no id registry.";

            return stock.ReceiveLot(new BakeryFuelLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                FuelName = FuelMaterialId,
                Units = units,
                AcquiredDayIndex = dayIndex,
                FuelDealerBusinessId = fuelDealerBusinessId,
                SourceFuelLotId = dealerLotId ?? string.Empty,
                OriginName = $"FuelDealer {fuelDealerBusinessId} (town fuel yard)",
                IsBootstrapEndowment = false,
            }, diag);
        }

        /// <summary>
        /// Receives an import order's arrival into the fuel store as a named
        /// lot. The caller moves real lots; this only records custody with
        /// provenance.
        /// </summary>
        public static string ReceiveImportArrival(
            BakeryFuelStock stock,
            int units,
            string importOrderId,
            string originName,
            int dayIndex,
            EntityIdRegistry idRegistry,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (stock == null) return "BakeryFuelSupply.ReceiveImportArrival: no fuel stock.";
            if (string.IsNullOrWhiteSpace(importOrderId))
                return "BakeryFuelSupply.ReceiveImportArrival: the import order id must be named — no orphan stock.";
            if (idRegistry == null) return "BakeryFuelSupply.ReceiveImportArrival: no id registry.";

            return stock.ReceiveLot(new BakeryFuelLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                FuelName = FuelMaterialId,
                Units = units,
                AcquiredDayIndex = dayIndex,
                ImportOrderId = importOrderId,
                OriginName = originName ?? string.Empty,
                IsBootstrapEndowment = false,
            }, diag);
        }
    }
}
