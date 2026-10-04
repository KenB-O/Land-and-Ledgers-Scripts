using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Businesses.BoardingHouse
{
    /// <summary>
    /// D1F: boarding-house fuel calibration. Canon §8.1B: food service (and
    /// the house's stoves, §8.1G) creates real demand for "fuel" alongside
    /// meat, flour, vegetables, coffee, water, kitchen labor and storage.
    /// The boarding kitchen burns cordwood per prepared board meal. Heating
    /// demand beyond cooking is routed around — the canon never quantifies
    /// it, so D1F models only the cooking fire the canon unambiguously
    /// describes. All values are TUNING (Canon Part XV).
    /// </summary>
    public static class BoardingHouseFuelSupply
    {
        public const string FuelMaterialId = "boardinghouse-fuelwood";

        /// <summary>TUNING: cordwood units burned per prepared board meal.</summary>
        public const int FuelUnitsPerBoardMeal = 1;

        /// <summary>TUNING: opening cordwood units in the house's wood store.</summary>
        public const int BootstrapFuelUnits = 40;
    }

    /// <summary>
    /// D1F: one stove-fuel lot with full upstream provenance. Every cordwood
    /// unit the boarding-house range burns must trace to a real lot from a
    /// real supplier — a named FuelDealer (the town fuel yard), a declared
    /// import order from a named off-map origin, or a named local supply
    /// relationship — or to the explicit one-time bootstrap endowment. No
    /// orphan lots, no synthetic stock (upstream-provenance doctrine).
    /// Mirrors <see cref="LandLedgers.Economy.Businesses.Restaurant.RestaurantFuelLot"/> (D1E).
    /// </summary>
    [Serializable]
    public sealed class BoardingHouseFuelLot
    {
        public EntityId LotId = EntityId.Invalid; // EntityKind.Lot (HF-1)
        public string FuelName = string.Empty; // "boardinghouse-fuelwood"
        public int Units; // cordwood units
        public int AcquiredDayIndex;

        /// <summary>The FuelDealer business whose yard supplied this fuel.</summary>
        public string FuelDealerBusinessId = string.Empty;

        /// <summary>The dealer's own lot reference this fuel was taken from.</summary>
        public string SourceFuelLotId = string.Empty;

        /// <summary>The declared trade link that brought this lot in (import order id, or a local supply relationship id).</summary>
        public string ImportOrderId = string.Empty;

        /// <summary>Named off-map or local origin, e.g. "Off-map cordwood, via railhead".</summary>
        public string OriginName = string.Empty;

        /// <summary>Any extra upstream link (woodcutter name, local purchase).</summary>
        public string SupplierNote = string.Empty;

        /// <summary>
        /// True only for the one-time opening endowment applied by
        /// <see cref="BoardingHouseFuelBootstrap"/>. Explicitly marked, never
        /// silently replenished — reorder goes through fuel dealers and
        /// import orders only.
        /// </summary>
        public bool IsBootstrapEndowment;

        public BoardingHouseFuelLot() { }

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
    /// D1F: one line of a fuel burn result — which lot the units came from
    /// and the full upstream chain. Prepared board-meal lots carry these so
    /// the firing fuel is traceable into the meal, the same way ingredients
    /// are (D1E precedent).
    /// </summary>
    [Serializable]
    public sealed class BoardingHouseFuelDispenseLine
    {
        public EntityId LotId = EntityId.Invalid;
        public string FuelName = string.Empty;
        public int UnitsTaken;
        public string ProvenanceChain = string.Empty;

        public BoardingHouseFuelDispenseLine() { }
    }

    /// <summary>
    /// D1F: the boarding house's stove-fuel store (cordwood for the kitchen
    /// range). Units burn FIFO (oldest stock first); every burn returns the
    /// lots consumed so prepared meals carry provenance. Burns are atomic:
    /// a shortfall burns nothing and refuses loudly — a meal never cooks on
    /// faked fuel. Mirrors the restaurant's fuel stock (D1E).
    /// </summary>
    public sealed class BoardingHouseFuelStock
    {
        private readonly List<BoardingHouseFuelLot> lots = new List<BoardingHouseFuelLot>();
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<BoardingHouseFuelLot> Lots => lots;

        /// <summary>
        /// Receives a lot into the fuel store. Lots must name their supplier
        /// chain (fuel dealer + dealer lot, or import order + origin) or carry
        /// the explicit bootstrap flag. Returns a rejection string, or null on
        /// success.
        /// </summary>
        public string ReceiveLot(BoardingHouseFuelLot lot, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (lot == null)
                return "BoardingHouseFuelStock.ReceiveLot: no lot — nothing received.";
            if (lot.LotId == EntityId.Invalid)
                return "BoardingHouseFuelStock.ReceiveLot: a fuel lot needs a real lot id.";
            if (lot.Units <= 0)
                return "BoardingHouseFuelStock.ReceiveLot: a fuel lot needs at least one unit.";
            if (string.IsNullOrWhiteSpace(lot.FuelName))
                return "BoardingHouseFuelStock.ReceiveLot: a fuel lot needs a fuel name.";

            if (!lot.IsBootstrapEndowment)
            {
                bool hasDealerChain = !string.IsNullOrWhiteSpace(lot.FuelDealerBusinessId)
                    && !string.IsNullOrWhiteSpace(lot.SourceFuelLotId);
                bool hasImportChain = !string.IsNullOrWhiteSpace(lot.ImportOrderId)
                    && !string.IsNullOrWhiteSpace(lot.OriginName);
                if (!hasDealerChain && !hasImportChain)
                    return "BoardingHouseFuelStock.ReceiveLot: orphan fuel refused — name the fuel dealer + dealer lot, " +
                        "or the import order + origin (upstream-provenance doctrine).";
            }

            lots.Add(lot);
            diag.Add($"BoardingHouseFuelStock: received {lot.Units} × {lot.FuelName} (lot {lot.LotId}, day {lot.AcquiredDayIndex}) — {lot.ProvenanceChain()}.");
            return null;
        }

        /// <summary>
        /// Burns fuel units FIFO. Returns the dispense lines, or null when
        /// the store is short — in which case NOTHING burns (atomic) and the
        /// refusal is loud.
        /// </summary>
        public List<BoardingHouseFuelDispenseLine> TryBurnUnits(string fuelName, int units, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (units <= 0) return new List<BoardingHouseFuelDispenseLine>();
            if (UnitsOnHand(fuelName) < units)
            {
                diag.Add($"BoardingHouseFuelStock: fuel shortfall — {units} × {fuelName} wanted, {UnitsOnHand(fuelName)} on hand (day {dayIndex}). Nothing burned.");
                return null;
            }

            var lines = new List<BoardingHouseFuelDispenseLine>();
            int remaining = units;
            for (int i = 0; i < lots.Count && remaining > 0; i++)
            {
                BoardingHouseFuelLot lot = lots[i];
                if (lot == null) continue;
                if (!string.Equals(lot.FuelName, fuelName, StringComparison.OrdinalIgnoreCase)) continue;
                if (lot.Units <= 0) continue;
                int take = Math.Min(remaining, lot.Units);
                lot.Units -= take;
                remaining -= take;
                lines.Add(new BoardingHouseFuelDispenseLine
                {
                    LotId = lot.LotId,
                    FuelName = lot.FuelName,
                    UnitsTaken = take,
                    ProvenanceChain = lot.ProvenanceChain(),
                });
            }

            for (int i = lots.Count - 1; i >= 0; i--)
                if (lots[i] == null || lots[i].Units <= 0)
                    lots.RemoveAt(i);

            diag.Add($"BoardingHouseFuelStock: burned {units} × {fuelName} (day {dayIndex}).");
            return lines;
        }

        public int UnitsOnHand(string fuelName)
        {
            int total = 0;
            foreach (BoardingHouseFuelLot lot in lots)
            {
                if (lot != null && string.Equals(lot.FuelName, fuelName, StringComparison.OrdinalIgnoreCase))
                    total += Math.Max(0, lot.Units);
            }

            return total;
        }

        #region Save / Load
        [Serializable]
        public sealed class BoardingHouseFuelStockSaveDto
        {
            public List<BoardingHouseFuelLot> Lots = new List<BoardingHouseFuelLot>();
        }

        public BoardingHouseFuelStockSaveDto CaptureSaveDto()
        {
            var dto = new BoardingHouseFuelStockSaveDto();
            foreach (BoardingHouseFuelLot lot in lots)
                if (lot != null) dto.Lots.Add(lot);
            return dto;
        }

        public void LoadFromSaveDto(BoardingHouseFuelStockSaveDto dto)
        {
            lots.Clear();
            if (dto?.Lots == null) return;
            foreach (BoardingHouseFuelLot lot in dto.Lots)
            {
                if (lot == null || lot.LotId == EntityId.Invalid || lot.Units <= 0) continue;
                lots.Add(lot);
            }
        }
        #endregion
    }

    /// <summary>
    /// D1F: the one-time opening endowment of the boarding house's fuel
    /// store. Explicitly marked BOOTSTRAP — it stands in for the house's
    /// opening cordwood pile and is NEVER silently replenished: later fuel
    /// comes only from fuel dealers or import orders of
    /// <see cref="BoardingHouseFuelSupply.FuelMaterialId"/>.
    /// </summary>
    public static class BoardingHouseFuelBootstrap
    {
        public static void ApplyBootstrapEndowment(
            BoardingHouseFuelStock stock,
            EntityIdRegistry idRegistry,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (stock == null)
            {
                diagnostics.Add("BoardingHouseFuelBootstrap: no fuel stock — endowment not applied.");
                return;
            }

            if (idRegistry == null)
            {
                diagnostics.Add("BoardingHouseFuelBootstrap: no id registry — endowment not applied.");
                return;
            }

            string rejection = stock.ReceiveLot(new BoardingHouseFuelLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                FuelName = BoardingHouseFuelSupply.FuelMaterialId,
                Units = BoardingHouseFuelSupply.BootstrapFuelUnits,
                AcquiredDayIndex = dayIndex,
                IsBootstrapEndowment = true,
                SupplierNote = "Opening boarding-house stocking (one-time; reorder via fuel dealers and import orders only).",
            }, diagnostics);
            if (rejection != null)
            {
                diagnostics.Add($"BoardingHouseFuelBootstrap: {rejection}");
                return;
            }

            diagnostics.Add("BoardingHouseFuelBootstrap: BOOTSTRAP endowment applied (one-time opening stock). " +
                "Upstream-provenance doctrine: this lot is explicitly marked and will never auto-replenish.");
        }
    }
}
