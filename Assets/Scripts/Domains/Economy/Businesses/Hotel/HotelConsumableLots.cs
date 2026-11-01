using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Trade;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Businesses.Hotel
{
    /// <summary>
    /// W3A: one housekeeping consumable lot with full upstream provenance.
    /// Every bed-linen set and soap portion the hotel consumes must trace
    /// to a real lot from a real supplier — a declared import order from a
    /// named off-map wholesaler — or to the explicit one-time bootstrap
    /// endowment. No orphan lots, no synthetic stock. Mirrors the W1B
    /// consumable-lot pattern (Canon §8.1E: cleaning/laundry/bed turnover
    /// is real hotel labor; the canon equipment table gives the
    /// boarding-house/hotel keeper "linens" as a real input).
    /// </summary>
    [Serializable]
    public sealed class HotelConsumableLot
    {
        public EntityId LotId = EntityId.Invalid; // EntityKind.Lot (HF-1)
        public string ConsumableName = string.Empty; // "hotel-linen" or "hotel-soap"
        public int Units;
        public int AcquiredDayIndex;

        /// <summary>The declared trade link that brought this lot in (ImportOrder.OrderId).</summary>
        public string ImportOrderId = string.Empty;

        /// <summary>Named off-map origin, e.g. "Off-map dry-goods wholesaler, via railhead".</summary>
        public string OriginName = string.Empty;

        /// <summary>Any extra upstream link (wholesaler name, local purchase, laundry return sources).</summary>
        public string SupplierNote = string.Empty;

        /// <summary>
        /// True only for the one-time opening endowment applied by
        /// <see cref="HotelConsumableBootstrap"/>. Explicitly marked, never
        /// silently replenished — reorder goes through import orders only.
        /// </summary>
        public bool IsBootstrapEndowment;

        /// <summary>
        /// True only for clean linen sets returned to the shelf by
        /// <see cref="HotelHousekeeping"/>'s laundry. Not a purchase: the
        /// sheets are recycled physical stock, and
        /// <see cref="LaundryReturnSourceChains"/> names the dirty-set
        /// source lots and the soap lots consumed in washing them.
        /// </summary>
        public bool IsLaundryReturn;

        /// <summary>Provenance of a laundry-return lot: source lot chains + soap lot chains.</summary>
        public string LaundryReturnSourceChains = string.Empty;

        public HotelConsumableLot() { }

        /// <summary>Human-readable upstream chain for ledgers and diagnostics.</summary>
        public string ProvenanceChain()
        {
            if (IsBootstrapEndowment)
            {
                return $"BOOTSTRAP endowment (lot {LotId}, day {AcquiredDayIndex}) — one-time opening stock; reorder via import orders";
            }

            if (IsLaundryReturn)
            {
                string sources = string.IsNullOrWhiteSpace(LaundryReturnSourceChains) ? "unnamed sources" : LaundryReturnSourceChains;
                return $"LAUNDRY return (lot {LotId}, day {AcquiredDayIndex}) — sheets/towels laundered in-house; sources: {sources}; no new purchase stock created";
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
    /// W3A: one line of a dispense result — which lot the units came from
    /// and the full upstream chain. Room-night sale records carry these so
    /// every linen set is traceable.
    /// </summary>
    [Serializable]
    public sealed class HotelConsumableDispenseLine
    {
        public EntityId LotId = EntityId.Invalid;
        public string ConsumableName = string.Empty;
        public int UnitsTaken;
        public string ProvenanceChain = string.Empty;

        public HotelConsumableDispenseLine() { }
    }

    /// <summary>
    /// W3A: the hotel's housekeeping shelf (bed-linen sets, soap portions).
    /// Units dispense FIFO (oldest stock first); every dispense returns
    /// the lots consumed so room-night records carry provenance.
    /// Refusals are loud — stock shortfalls are never faked.
    /// </summary>
    public sealed class HotelConsumableStock
    {
        private readonly List<HotelConsumableLot> lots = new List<HotelConsumableLot>();
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<HotelConsumableLot> Lots => lots;

        /// <summary>
        /// Receives a lot onto the shelf. Lots must name their supplier chain
        /// (import order + origin) or carry the explicit bootstrap flag.
        /// Returns a rejection string, or null on success.
        /// </summary>
        public string ReceiveLot(HotelConsumableLot lot, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (lot == null) return "HotelConsumableStock: null lot refused — no consumables without a lot record.";
            if (string.IsNullOrWhiteSpace(lot.ConsumableName))
                return "HotelConsumableStock: lot refused — consumable name is required.";
            if (lot.Units <= 0)
                return $"HotelConsumableStock: lot refused — '{lot.ConsumableName}' needs a positive unit count.";
            if (!lot.IsBootstrapEndowment && !lot.IsLaundryReturn
                && (string.IsNullOrWhiteSpace(lot.ImportOrderId) || string.IsNullOrWhiteSpace(lot.OriginName)))
                return $"HotelConsumableStock: lot refused — '{lot.ConsumableName}' names no import order or origin. No orphan lots.";
            if (lot.IsLaundryReturn && string.IsNullOrWhiteSpace(lot.LaundryReturnSourceChains))
                return $"HotelConsumableStock: laundry-return lot refused — '{lot.ConsumableName}' must name its source chains.";
            if (lot.LotId.IsValid)
            {
                foreach (var existing in lots)
                {
                    if (existing.LotId.Equals(lot.LotId))
                        return $"HotelConsumableStock: lot refused — lot {lot.LotId} already on the shelf.";
                }
            }

            lots.Add(lot);
            diag.Add($"HotelConsumableStock: received {lot.Units} units '{lot.ConsumableName}' — {lot.ProvenanceChain()}.");
            return null;
        }

        /// <summary>
        /// Dispenses units FIFO (oldest first). Returns null with a diagnostic
        /// when the shelf cannot cover the request — no units are conjured.
        /// </summary>
        public List<HotelConsumableDispenseLine> TryDispenseUnits(string consumableName, int units, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var lines = new List<HotelConsumableDispenseLine>();
            if (string.IsNullOrWhiteSpace(consumableName) || units <= 0)
            {
                diag.Add("HotelConsumableStock: dispense refused — consumable name and positive unit count required.");
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
                diag.Add($"HotelConsumableStock: only {available} units of '{consumableName}' on hand, need {units} — shortfall, no units conjured.");
                return null;
            }

            // FIFO by acquisition day.
            var ordered = new List<HotelConsumableLot>();
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
                lines.Add(new HotelConsumableDispenseLine
                {
                    LotId = lot.LotId,
                    ConsumableName = lot.ConsumableName,
                    UnitsTaken = take,
                    ProvenanceChain = lot.ProvenanceChain(),
                });
            }

            // Drop emptied lots so the shelf never carries ghost stock.
            lots.RemoveAll(l => l.Units <= 0);

            diag.Add($"HotelConsumableStock: dispensed {units} units '{consumableName}' (day {dayIndex}) from {lines.Count} lot(s).");
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
        public sealed class HotelConsumableStockSaveDto
        {
            public List<HotelConsumableLot> Lots = new List<HotelConsumableLot>();
        }

        public HotelConsumableStockSaveDto CaptureSaveDto()
        {
            var dto = new HotelConsumableStockSaveDto();
            dto.Lots.AddRange(lots);
            return dto;
        }

        public void LoadFromSaveDto(HotelConsumableStockSaveDto dto)
        {
            lots.Clear();
            if (dto?.Lots == null) return;
            lots.AddRange(dto.Lots);
        }
        #endregion
    }

    /// <summary>
    /// W3A: the one-time opening endowment of the hotel's housekeeping
    /// shelf. Explicitly marked BOOTSTRAP — it stands in for the hotel's
    /// opening stocking (a new hotel opened with sheets, towels and soap
    /// on hand) and is NEVER silently replenished: later stock comes only
    /// from import orders of <see cref="HotelConsumableSupply.LinenMaterialId"/>
    /// and <see cref="HotelConsumableSupply.SoapMaterialId"/>.
    /// </summary>
    public static class HotelConsumableBootstrap
    {
        /// <summary>TUNING: opening bed-linen sets on the shelf (roughly two turnovers per bed for a small hotel).</summary>
        public const int BootstrapLinenUnits = 24;

        /// <summary>TUNING: opening soap portions on the shelf.</summary>
        public const int BootstrapSoapUnits = 40;

        public static void ApplyBootstrapEndowment(
            HotelConsumableStock stock,
            EntityIdRegistry idRegistry,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (stock == null)
            {
                diagnostics.Add("HotelConsumableBootstrap: no consumable stock — endowment not applied.");
                return;
            }

            if (idRegistry == null)
            {
                diagnostics.Add("HotelConsumableBootstrap: no id registry — endowment not applied.");
                return;
            }

            string rejection = stock.ReceiveLot(new HotelConsumableLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                ConsumableName = HotelConsumableSupply.LinenMaterialId,
                Units = BootstrapLinenUnits,
                AcquiredDayIndex = dayIndex,
                IsBootstrapEndowment = true,
                SupplierNote = "Opening hotel stocking (one-time; reorder via import orders only).",
            }, diagnostics);
            if (rejection != null)
            {
                diagnostics.Add($"HotelConsumableBootstrap: {rejection}");
                return;
            }

            rejection = stock.ReceiveLot(new HotelConsumableLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                ConsumableName = HotelConsumableSupply.SoapMaterialId,
                Units = BootstrapSoapUnits,
                AcquiredDayIndex = dayIndex,
                IsBootstrapEndowment = true,
                SupplierNote = "Opening hotel stocking (one-time; reorder via import orders only).",
            }, diagnostics);
            if (rejection != null)
            {
                diagnostics.Add($"HotelConsumableBootstrap: {rejection}");
                return;
            }

            diagnostics.Add("HotelConsumableBootstrap: BOOTSTRAP endowment applied (one-time opening stock). " +
                "Upstream-provenance doctrine: these lots are explicitly marked and will never auto-replenish.");
        }
    }

    /// <summary>
    /// W3A: the bed-linen/soap supply link. Survey result: no bed-linen or
    /// housekeeping soap supplier exists anywhere in the import catalog —
    /// so the honest path is a DECLARED off-map trade link (EQU-1
    /// precedent: named origin, real distance and transit days), matching
    /// how frontier hotels stocked sheets, towels and lye soap from
    /// eastern dry-goods and wholesale drug houses by rail. The hotel
    /// reorders through <see cref="ImportService"/>.
    /// </summary>
    public static class HotelConsumableSupply
    {
        public const string LinenMaterialId = "hotel-linen";
        public const string SoapMaterialId = "hotel-soap";

        /// <summary>
        /// Registers bed linen and housekeeping soap as importable materials
        /// (EQU-1 extension path). Historical: 1870s frontier hotels ordered
        /// sheets, towels and lye soap from eastern dry-goods wholesalers and
        /// wholesale drug/toiletries houses shipped by rail — named origins,
        /// real transit, real cost. Prices/transit are calibration (Canon
        /// Part XV).
        /// </summary>
        public static void EnsureHotelImportables(List<string> diag)
        {
            string problem = ImportCatalog.RegisterMaterial(new ImportMaterial(
                LinenMaterialId, "Bed linen sets (sheets and towels)",
                "Off-map dry-goods wholesaler, via railhead", 250, 14, 35));
            if (problem != null && diag != null)
            {
                diag.Add($"HotelConsumableSupply: linen import registration: {problem}");
            }

            problem = ImportCatalog.RegisterMaterial(new ImportMaterial(
                SoapMaterialId, "Housekeeping soap (laundry portions)",
                "Off-map wholesale drug and toiletries house, via railhead", 250, 14, 2));
            if (problem != null && diag != null)
            {
                diag.Add($"HotelConsumableSupply: soap import registration: {problem}");
            }
        }

        /// <summary>
        /// Receives an import order's arrival onto the shelf as a named lot.
        /// The caller moves real lots; this only records custody with provenance.
        /// </summary>
        public static string ReceiveImportArrival(
            HotelConsumableStock stock,
            string consumableName,
            int units,
            string importOrderId,
            string originName,
            int dayIndex,
            EntityIdRegistry idRegistry,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (stock == null) return "HotelConsumableSupply.ReceiveImportArrival: no consumable stock.";
            if (string.IsNullOrWhiteSpace(importOrderId))
                return "HotelConsumableSupply.ReceiveImportArrival: the import order id must be named — no orphan stock.";
            if (idRegistry == null) return "HotelConsumableSupply.ReceiveImportArrival: no id registry.";

            return stock.ReceiveLot(new HotelConsumableLot
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
