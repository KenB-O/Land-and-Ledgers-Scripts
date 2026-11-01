using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Trade;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Businesses.Barber
{
    /// <summary>
    /// W1B: one consumable lot with full upstream provenance. Every consumed
    /// soap portion and linen unit must trace to a real lot from a real supplier
    /// — a declared import order from the named off-map wholesaler — or to the
    /// explicit one-time bootstrap endowment. No orphan lots, no synthetic stock.
    /// </summary>
    [Serializable]
    public sealed class BarberConsumableLot
    {
        public EntityId LotId = EntityId.Invalid; // EntityKind.Lot (HF-1)
        public string ConsumableName = string.Empty; // "barber-soap" or "barber-linen"
        public int Units;
        public int AcquiredDayIndex;

        /// <summary>The declared trade link that brought this lot in (ImportOrder.OrderId).</summary>
        public string ImportOrderId = string.Empty;

        /// <summary>Named off-map origin, e.g. "Off-map wholesale drug and toiletries house, via railhead".</summary>
        public string OriginName = string.Empty;

        /// <summary>Any extra upstream link (wholesaler name, local purchase).</summary>
        public string SupplierNote = string.Empty;

        /// <summary>
        /// True only for the one-time opening endowment applied by
        /// <see cref="BarberConsumableBootstrap"/>. Explicitly marked, never
        /// silently replenished — reorder goes through import orders only.
        /// </summary>
        public bool IsBootstrapEndowment;

        public BarberConsumableLot() { }

        /// <summary>Human-readable upstream chain for ledgers and diagnostics.</summary>
        public string ProvenanceChain()
        {
            if (IsBootstrapEndowment)
            {
                return $"BOOTSTRAP endowment (lot {LotId}, day {AcquiredDayIndex}) — one-time opening stock; reorder via import orders";
            }

            string order = string.IsNullOrWhiteSpace(ImportOrderId) ? "no-order" : ImportOrderId;
            string origin = string.IsNullOrWhiteSpace(OriginName) ? "unnamed-origin" : OriginName;
            string chain = $"lot {LotId} / import order {order} / {origin}";
            if (!string.IsNullOrWhiteSpace(SupplierNote))
            {
                chain += $" / {SupplierNote}";
            }

            return chain;
        }
    }

    /// <summary>
    /// W1B: one line of a dispense result — which lot the units came from and the
    /// full upstream chain. Service records carry these so every soap portion and
    /// linen unit is traceable.
    /// </summary>
    [Serializable]
    public sealed class BarberConsumableDispenseLine
    {
        public EntityId LotId = EntityId.Invalid;
        public string ConsumableName = string.Empty;
        public int UnitsTaken;
        public string ProvenanceChain = string.Empty;

        public BarberConsumableDispenseLine() { }
    }

    /// <summary>
    /// W1B: the barber's consumable shelf (soap, linen). Units dispense FIFO
    /// (oldest stock first); every dispense returns the lots consumed so service
    /// records carry provenance. Refusals are loud — stock shortfalls are never
    /// faked.
    /// </summary>
    public sealed class BarberConsumableStock
    {
        private readonly List<BarberConsumableLot> lots = new List<BarberConsumableLot>();
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<BarberConsumableLot> Lots => lots;

        /// <summary>
        /// Receives a lot onto the shelf. Lots must name their supplier chain
        /// (import order + origin) or carry the explicit bootstrap flag.
        /// Returns a rejection string, or null on success.
        /// </summary>
        public string ReceiveLot(BarberConsumableLot lot, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (lot == null) return "BarberConsumableStock: null lot refused — no consumables without a lot record.";
            if (string.IsNullOrWhiteSpace(lot.ConsumableName))
                return "BarberConsumableStock: lot refused — consumable name is required.";
            if (lot.Units <= 0)
                return $"BarberConsumableStock: lot refused — '{lot.ConsumableName}' needs a positive unit count.";
            if (!lot.IsBootstrapEndowment
                && (string.IsNullOrWhiteSpace(lot.ImportOrderId) || string.IsNullOrWhiteSpace(lot.OriginName)))
                return $"BarberConsumableStock: lot refused — '{lot.ConsumableName}' names no import order or origin. No orphan lots.";
            if (lot.LotId.IsValid)
            {
                foreach (var existing in lots)
                {
                    if (existing.LotId.Equals(lot.LotId))
                        return $"BarberConsumableStock: lot refused — lot {lot.LotId} already on the shelf.";
                }
            }

            lots.Add(lot);
            diag.Add($"BarberConsumableStock: received {lot.Units} units '{lot.ConsumableName}' — {lot.ProvenanceChain()}.");
            return null;
        }

        /// <summary>
        /// Dispenses units FIFO (oldest first). Returns null with a diagnostic
        /// when the shelf cannot cover the request — no units are conjured.
        /// </summary>
        public List<BarberConsumableDispenseLine> TryDispenseUnits(string consumableName, int units, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var lines = new List<BarberConsumableDispenseLine>();
            if (string.IsNullOrWhiteSpace(consumableName) || units <= 0)
            {
                diag.Add("BarberConsumableStock: dispense refused — consumable name and positive unit count required.");
                return null;
            }

            int available = 0;
            foreach (var lot in lots)
            {
                if (string.Equals(lot.ConsumableName, consumableName, StringComparison.OrdinalIgnoreCase))
                {
                    available += lot.Units;
                }
            }

            if (available < units)
            {
                diag.Add($"BarberConsumableStock: only {available} units of '{consumableName}' on hand, need {units} — shortfall, no units conjured.");
                return null;
            }

            // FIFO by acquisition day.
            var ordered = new List<BarberConsumableLot>();
            foreach (var lot in lots)
            {
                if (string.Equals(lot.ConsumableName, consumableName, StringComparison.OrdinalIgnoreCase))
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
                lines.Add(new BarberConsumableDispenseLine
                {
                    LotId = lot.LotId,
                    ConsumableName = lot.ConsumableName,
                    UnitsTaken = take,
                    ProvenanceChain = lot.ProvenanceChain(),
                });
            }

            // Drop emptied lots so the shelf never carries ghost stock.
            lots.RemoveAll(l => l.Units <= 0);

            diag.Add($"BarberConsumableStock: dispensed {units} units '{consumableName}' (day {dayIndex}) from {lines.Count} lot(s).");
            return lines;
        }

        /// <summary>Counts all on-hand units of a consumable.</summary>
        public int UnitsOnHand(string consumableName)
        {
            int total = 0;
            foreach (var lot in lots)
            {
                if (string.Equals(lot.ConsumableName, consumableName, StringComparison.OrdinalIgnoreCase))
                {
                    total += lot.Units;
                }
            }

            return total;
        }

        #region Save / Load
        [Serializable]
        public sealed class BarberConsumableStockSaveDto
        {
            public List<BarberConsumableLot> Lots = new List<BarberConsumableLot>();
        }

        public BarberConsumableStockSaveDto CaptureSaveDto()
        {
            var dto = new BarberConsumableStockSaveDto();
            dto.Lots.AddRange(lots);
            return dto;
        }

        public void LoadFromSaveDto(BarberConsumableStockSaveDto dto)
        {
            lots.Clear();
            if (dto?.Lots == null) return;
            lots.AddRange(dto.Lots);
        }
        #endregion
    }

    /// <summary>
    /// W1B: the one-time opening endowment of the barber's consumable shelf.
    /// Explicitly marked BOOTSTRAP — it stands in for the shop's opening
    /// stocking (a new barbershop opened with soap and towels on hand) and is
    /// NEVER silently replenished: later stock comes only from import orders of
    /// <see cref="BarberConsumableSupply.SoapMaterialId"/> and
    /// <see cref="BarberConsumableSupply.LinenMaterialId"/>.
    /// </summary>
    public static class BarberConsumableBootstrap
    {
        /// <summary>TUNING: opening soap portions on the shelf.</summary>
        public const int BootstrapSoapUnits = 40;

        /// <summary>TUNING: opening linen units on the shelf.</summary>
        public const int BootstrapLinenUnits = 30;

        public static void ApplyBootstrapEndowment(
            BarberConsumableStock stock,
            EntityIdRegistry idRegistry,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (stock == null)
            {
                diagnostics.Add("BarberConsumableBootstrap: no consumable stock — endowment not applied.");
                return;
            }

            if (idRegistry == null)
            {
                diagnostics.Add("BarberConsumableBootstrap: no id registry — endowment not applied.");
                return;
            }

            string rejection = stock.ReceiveLot(new BarberConsumableLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                ConsumableName = BarberServiceCatalog.SoapItemId,
                Units = BootstrapSoapUnits,
                AcquiredDayIndex = dayIndex,
                IsBootstrapEndowment = true,
                SupplierNote = "Opening shop stocking (one-time; reorder via import orders only).",
            }, diagnostics);
            if (rejection != null)
            {
                diagnostics.Add($"BarberConsumableBootstrap: {rejection}");
                return;
            }

            rejection = stock.ReceiveLot(new BarberConsumableLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                ConsumableName = BarberServiceCatalog.LinenItemId,
                Units = BootstrapLinenUnits,
                AcquiredDayIndex = dayIndex,
                IsBootstrapEndowment = true,
                SupplierNote = "Opening shop stocking (one-time; reorder via import orders only).",
            }, diagnostics);
            if (rejection != null)
            {
                diagnostics.Add($"BarberConsumableBootstrap: {rejection}");
                return;
            }

            diagnostics.Add("BarberConsumableBootstrap: BOOTSTRAP endowment applied (one-time opening stock). " +
                "Upstream-provenance doctrine: these lots are explicitly marked and will never auto-replenish.");
        }
    }

    /// <summary>
    /// W1B: the soap/linen supply link. Survey result: no soap or linen supplier
    /// exists anywhere in the import catalog or the general store's goods — so
    /// the honest path is a DECLARED off-map trade link (EQU-1 precedent: named
    /// origin, real distance and transit days), matching how frontier barbers
    /// stocked shaving soap and towels from eastern wholesale houses by rail.
    /// The shop reorders through <see cref="ImportService"/>.
    /// </summary>
    public static class BarberConsumableSupply
    {
        public const string SoapMaterialId = "barber-soap";
        public const string LinenMaterialId = "barber-linen";

        /// <summary>
        /// Registers shaving soap and barber linen as importable materials
        /// (EQU-1 extension path). Historical: 1870s frontier barbers ordered
        /// shaving soap and towels from eastern wholesale drug/toiletries and
        /// dry-goods houses shipped by rail — named origins, real transit, real
        /// cost. Prices/transit are calibration (Canon Part XV).
        /// </summary>
        public static void EnsureBarberImportables(List<string> diag)
        {
            string problem = ImportCatalog.RegisterMaterial(new ImportMaterial(
                SoapMaterialId, "Shaving soap (service portions)",
                "Off-map wholesale drug and toiletries house, via railhead", 250, 14, 2));
            if (problem != null && diag != null)
            {
                diag.Add($"BarberConsumableSupply: soap import registration: {problem}");
            }

            problem = ImportCatalog.RegisterMaterial(new ImportMaterial(
                LinenMaterialId, "Barber towels and linen (pieces)",
                "Off-map dry-goods wholesaler, via railhead", 250, 14, 35));
            if (problem != null && diag != null)
            {
                diag.Add($"BarberConsumableSupply: linen import registration: {problem}");
            }
        }

        /// <summary>
        /// Receives an import order's arrival onto the shelf as a named lot.
        /// The caller moves real lots; this only records custody with provenance.
        /// </summary>
        public static string ReceiveImportArrival(
            BarberConsumableStock stock,
            string consumableName,
            int units,
            string importOrderId,
            string originName,
            int dayIndex,
            EntityIdRegistry idRegistry,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (stock == null) return "BarberConsumableSupply.ReceiveImportArrival: no consumable stock.";
            if (string.IsNullOrWhiteSpace(importOrderId))
                return "BarberConsumableSupply.ReceiveImportArrival: the import order id must be named — no orphan stock.";
            if (idRegistry == null) return "BarberConsumableSupply.ReceiveImportArrival: no id registry.";

            return stock.ReceiveLot(new BarberConsumableLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                ConsumableName = consumableName ?? string.Empty,
                Units = units,
                AcquiredDayIndex = dayIndex,
                ImportOrderId = importOrderId,
                OriginName = originName ?? string.Empty,
                IsBootstrapEndowment = false,
            }, diag);
        }
    }
}
