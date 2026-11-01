using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Trade;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Businesses.Restaurant
{
    /// <summary>
    /// D1E: one stove-fuel lot with full upstream provenance. Every cordwood
    /// unit the kitchen range burns must trace to a real lot from a real
    /// supplier — a named FuelDealer (the town fuel yard), a declared import
    /// order from a named off-map origin, or a named local supply
    /// relationship — or to the explicit one-time bootstrap endowment. No
    /// orphan lots, no synthetic stock (upstream-provenance doctrine).
    /// Canon §8.1B: food service creates real demand for "fuel" alongside
    /// meat, flour, vegetables, coffee, water, kitchen labor and storage.
    /// Mirrors <see cref="BakeryFuelLot"/> (D1D).
    /// </summary>
    [Serializable]
    public sealed class RestaurantFuelLot
    {
        public EntityId LotId = EntityId.Invalid; // EntityKind.Lot (HF-1)
        public string FuelName = string.Empty; // "restaurant-fuelwood"
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
        /// <see cref="RestaurantFuelBootstrap"/>. Explicitly marked, never
        /// silently replenished — reorder goes through fuel dealers and
        /// import orders only.
        /// </summary>
        public bool IsBootstrapEndowment;

        public RestaurantFuelLot() { }

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
    /// D1E: one line of a fuel dispense result — which lot the units came from
    /// and the full upstream chain. Cooked meal lots carry these so the
    /// firing fuel is traceable into the meal, the same way ingredients are.
    /// </summary>
    [Serializable]
    public sealed class RestaurantFuelDispenseLine
    {
        public EntityId LotId = EntityId.Invalid;
        public string FuelName = string.Empty;
        public int UnitsTaken;
        public string ProvenanceChain = string.Empty;

        public RestaurantFuelDispenseLine() { }
    }

    /// <summary>
    /// D1E: the restaurant's stove-fuel store (cordwood for the kitchen
    /// range). Units dispense FIFO (oldest stock first); every dispense
    /// returns the lots consumed so batch cookings carry provenance.
    /// Refusals are loud — a batch never cooks on faked fuel. Mirrors
    /// <see cref="BakeryFuelStock"/> (D1D).
    /// </summary>
    public sealed class RestaurantFuelStock
    {
        private readonly List<RestaurantFuelLot> lots = new List<RestaurantFuelLot>();
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<RestaurantFuelLot> Lots => lots;

        /// <summary>
        /// Receives a lot into the fuel store. Lots must name their supplier
        /// chain (fuel dealer + dealer lot, or import order + origin) or carry
        /// the explicit bootstrap flag. Returns a rejection string, or null on
        /// success.
        /// </summary>
        public string ReceiveLot(RestaurantFuelLot lot, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (lot == null) return "RestaurantFuelStock: null lot refused — no fuel without a lot record.";
            if (string.IsNullOrWhiteSpace(lot.FuelName))
                return "RestaurantFuelStock: lot refused — fuel name is required.";
            if (lot.Units <= 0)
                return $"RestaurantFuelStock: lot refused — '{lot.FuelName}' needs a positive unit count.";
            if (!lot.IsBootstrapEndowment
                && string.IsNullOrWhiteSpace(lot.FuelDealerBusinessId)
                && (string.IsNullOrWhiteSpace(lot.ImportOrderId) || string.IsNullOrWhiteSpace(lot.OriginName)))
                return $"RestaurantFuelStock: lot refused — '{lot.FuelName}' names no fuel dealer and no import order/origin. No orphan lots.";
            if (lot.LotId.IsValid)
            {
                foreach (var existing in lots)
                {
                    if (existing.LotId.Equals(lot.LotId))
                        return $"RestaurantFuelStock: lot refused — lot {lot.LotId} already in the fuel store.";
                }
            }

            lots.Add(lot);
            diag.Add($"RestaurantFuelStock: received {lot.Units} units '{lot.FuelName}' — {lot.ProvenanceChain()}.");
            return null;
        }

        /// <summary>
        /// Dispenses units FIFO (oldest first). Returns null with a diagnostic
        /// when the store cannot cover the request — no units are conjured.
        /// </summary>
        public List<RestaurantFuelDispenseLine> TryDispenseUnits(string fuelName, int units, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var lines = new List<RestaurantFuelDispenseLine>();
            if (string.IsNullOrWhiteSpace(fuelName) || units <= 0)
            {
                diag.Add("RestaurantFuelStock: dispense refused — fuel name and positive unit count required.");
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
                diag.Add($"RestaurantFuelStock: only {available} units of '{fuelName}' on hand, need {units} — shortfall, no units conjured.");
                return null;
            }

            // FIFO by acquisition day.
            var ordered = new List<RestaurantFuelLot>();
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
                lines.Add(new RestaurantFuelDispenseLine
                {
                    LotId = lot.LotId,
                    FuelName = lot.FuelName,
                    UnitsTaken = take,
                    ProvenanceChain = lot.ProvenanceChain(),
                });
            }

            // Drop emptied lots so the store never carries ghost stock.
            lots.RemoveAll(l => l.Units <= 0);

            diag.Add($"RestaurantFuelStock: dispensed {units} units '{fuelName}' (day {dayIndex}) from {lines.Count} lot(s).");
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
        public sealed class RestaurantFuelStockSaveDto
        {
            public List<RestaurantFuelLot> Lots = new List<RestaurantFuelLot>();
        }

        public RestaurantFuelStockSaveDto CaptureSaveDto()
        {
            var dto = new RestaurantFuelStockSaveDto();
            dto.Lots.AddRange(lots);
            return dto;
        }

        public void LoadFromSaveDto(RestaurantFuelStockSaveDto dto)
        {
            lots.Clear();
            if (dto?.Lots == null) return;
            lots.AddRange(dto.Lots);
        }
        #endregion
    }

    /// <summary>
    /// D1E: the one-time opening endowment of the restaurant's fuel store.
    /// Explicitly marked BOOTSTRAP — it stands in for the eating house's
    /// opening cordwood pile and is NEVER silently replenished: later fuel
    /// comes only from fuel dealers or import orders of
    /// <see cref="RestaurantFuelSupply.FuelMaterialId"/>.
    /// </summary>
    public static class RestaurantFuelBootstrap
    {
        /// <summary>TUNING: opening cordwood units in the fuel store.</summary>
        public const int BootstrapFuelUnits = 30;

        public static void ApplyBootstrapEndowment(
            RestaurantFuelStock stock,
            EntityIdRegistry idRegistry,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (stock == null)
            {
                diagnostics.Add("RestaurantFuelBootstrap: no fuel stock — endowment not applied.");
                return;
            }

            if (idRegistry == null)
            {
                diagnostics.Add("RestaurantFuelBootstrap: no id registry — endowment not applied.");
                return;
            }

            string rejection = stock.ReceiveLot(new RestaurantFuelLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                FuelName = RestaurantMealCatalog.FuelItemId,
                Units = BootstrapFuelUnits,
                AcquiredDayIndex = dayIndex,
                IsBootstrapEndowment = true,
                SupplierNote = "Opening eating-house stocking (one-time; reorder via fuel dealers and import orders only).",
            }, diagnostics);
            if (rejection != null)
            {
                diagnostics.Add($"RestaurantFuelBootstrap: {rejection}");
                return;
            }

            diagnostics.Add("RestaurantFuelBootstrap: BOOTSTRAP endowment applied (one-time opening stock). " +
                "Upstream-provenance doctrine: this lot is explicitly marked and will never auto-replenish.");
        }
    }

    /// <summary>
    /// D1E: the stove-fuel supply link. The honest paths are a named
    /// FuelDealer (the town fuel yard) or a DECLARED off-map trade link
    /// (EQU-1 precedent: named origin, real distance and transit days). The
    /// house reorders through <see cref="ImportService"/>.
    /// </summary>
    public static class RestaurantFuelSupply
    {
        public const string FuelMaterialId = "restaurant-fuelwood";

        /// <summary>
        /// Registers stove cordwood as an importable material (EQU-1
        /// extension path). Price/transit are calibration (Canon Part XV).
        /// </summary>
        public static void EnsureRestaurantImportables(List<string> diag)
        {
            string problem = ImportCatalog.RegisterMaterial(new ImportMaterial(
                FuelMaterialId, "Cordwood (stove fuel)",
                "Off-map cordwood, via railhead", 250, 14, 2));
            if (problem != null && diag != null)
            {
                diag.Add($"RestaurantFuelSupply: fuel import registration: {problem}");
            }
        }

        /// <summary>
        /// Takes fuelwood from a named FuelDealer into the restaurant's fuel
        /// store. The caller moves real custody; this records the lot with
        /// provenance (dealer business id + dealer lot reference). Refuses
        /// unnamed supply loudly.
        /// </summary>
        public static string ReceiveFuelDealerLot(
            RestaurantFuelStock stock,
            string fuelDealerBusinessId,
            string dealerLotId,
            int units,
            int dayIndex,
            EntityIdRegistry idRegistry,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (stock == null) return "RestaurantFuelSupply.ReceiveFuelDealerLot: no fuel stock.";
            if (string.IsNullOrWhiteSpace(fuelDealerBusinessId))
                return "RestaurantFuelSupply.ReceiveFuelDealerLot: the fuel dealer business must be named — no orphan stock.";
            if (units <= 0)
                return "RestaurantFuelSupply.ReceiveFuelDealerLot: a positive unit count is required — fuel is not conjured.";
            if (idRegistry == null) return "RestaurantFuelSupply.ReceiveFuelDealerLot: no id registry.";

            return stock.ReceiveLot(new RestaurantFuelLot
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
            RestaurantFuelStock stock,
            int units,
            string importOrderId,
            string originName,
            int dayIndex,
            EntityIdRegistry idRegistry,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (stock == null) return "RestaurantFuelSupply.ReceiveImportArrival: no fuel stock.";
            if (string.IsNullOrWhiteSpace(importOrderId))
                return "RestaurantFuelSupply.ReceiveImportArrival: the import order id must be named — no orphan stock.";
            if (idRegistry == null) return "RestaurantFuelSupply.ReceiveImportArrival: no id registry.";

            return stock.ReceiveLot(new RestaurantFuelLot
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
