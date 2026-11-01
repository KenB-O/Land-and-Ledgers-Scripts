using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Businesses.Hotel
{
    /// <summary>
    /// D2A: the hotel's heating-fuel supply calibration. Canon §8.1A bounds
    /// a lodging operation's usable capacity by "heating" alongside beds,
    /// water and sanitation; §8.1E's labor list includes heating as work.
    /// The hotel's wood store feeds stoves in guest rooms and the laundry
    /// boiler — cordwood units, TUNING (Canon Part XV), proprietor-settable
    /// only by replacing this calibration.
    /// </summary>
    public static class HotelFuelSupply
    {
        public const string FuelMaterialId = "hotel-fuelwood";

        /// <summary>TUNING: cordwood units burned per occupied bed-night (room stoves + the day's share of the laundry boiler).</summary>
        public const int FuelUnitsPerBedNight = 1;

        /// <summary>TUNING: opening cordwood units in the hotel's wood store.</summary>
        public const int BootstrapFuelUnits = 60;
    }

    /// <summary>
    /// D2A: one heating-fuel lot with full upstream provenance. Every
    /// cordwood unit the hotel's stoves burn must trace to a real lot from
    /// a real supplier — a named FuelDealer (the town fuel yard), a
    /// declared import order from a named off-map origin, or a named local
    /// supply relationship — or to the explicit one-time bootstrap
    /// endowment. No orphan lots, no synthetic stock (upstream-provenance
    /// doctrine). Mirrors the boarding-house fuel lot (D1F).
    /// </summary>
    [Serializable]
    public sealed class HotelFuelLot
    {
        public EntityId LotId = EntityId.Invalid; // EntityKind.Lot (HF-1)
        public string FuelName = string.Empty; // "hotel-fuelwood"
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
        /// <see cref="HotelFuelBootstrap"/>. Explicitly marked, never
        /// silently replenished — reorder goes through fuel dealers and
        /// import orders only.
        /// </summary>
        public bool IsBootstrapEndowment;

        public HotelFuelLot() { }

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

    /// <summary>D2A: one line of a fuel burn result — which lot the units came from and the full upstream chain.</summary>
    [Serializable]
    public sealed class HotelFuelDispenseLine
    {
        public EntityId LotId = EntityId.Invalid;
        public string FuelName = string.Empty;
        public int UnitsTaken;
        public string ProvenanceChain = string.Empty;

        public HotelFuelDispenseLine() { }
    }

    /// <summary>
    /// D2A: the hotel's wood store. Units burn FIFO (oldest stock first);
    /// every burn returns the lots consumed with provenance. Burns are
    /// atomic: a shortfall burns nothing and refuses loudly — a room is
    /// never heated on faked fuel. Mirrors the boarding-house fuel stock
    /// (D1F).
    /// </summary>
    public sealed class HotelFuelStock
    {
        private readonly List<HotelFuelLot> lots = new List<HotelFuelLot>();
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<HotelFuelLot> Lots => lots;

        /// <summary>
        /// Receives a lot into the wood store. Lots must name their supplier
        /// chain (fuel dealer + dealer lot, or import order + origin) or carry
        /// the explicit bootstrap flag. Returns a rejection string, or null on
        /// success.
        /// </summary>
        public string ReceiveLot(HotelFuelLot lot, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (lot == null)
                return "HotelFuelStock.ReceiveLot: no lot — nothing received.";
            if (lot.LotId == EntityId.Invalid)
                return "HotelFuelStock.ReceiveLot: a fuel lot needs a real lot id.";
            if (lot.Units <= 0)
                return "HotelFuelStock.ReceiveLot: a fuel lot needs at least one unit.";
            if (string.IsNullOrWhiteSpace(lot.FuelName))
                return "HotelFuelStock.ReceiveLot: a fuel lot needs a fuel name.";

            if (!lot.IsBootstrapEndowment)
            {
                bool hasDealerChain = !string.IsNullOrWhiteSpace(lot.FuelDealerBusinessId)
                    && !string.IsNullOrWhiteSpace(lot.SourceFuelLotId);
                bool hasImportChain = !string.IsNullOrWhiteSpace(lot.ImportOrderId)
                    && !string.IsNullOrWhiteSpace(lot.OriginName);
                if (!hasDealerChain && !hasImportChain)
                    return "HotelFuelStock.ReceiveLot: orphan fuel refused — name the fuel dealer + dealer lot, " +
                        "or the import order + origin (upstream-provenance doctrine).";
            }

            lots.Add(lot);
            diag.Add($"HotelFuelStock: received {lot.Units} × {lot.FuelName} (lot {lot.LotId}, day {lot.AcquiredDayIndex}) — {lot.ProvenanceChain()}.");
            return null;
        }

        /// <summary>
        /// Burns fuel units FIFO. Returns the dispense lines, or null when
        /// the store is short — in which case NOTHING burns (atomic) and
        /// the refusal is loud.
        /// </summary>
        public List<HotelFuelDispenseLine> TryBurnUnits(string fuelName, int units, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (units <= 0) return new List<HotelFuelDispenseLine>();
            if (UnitsOnHand(fuelName) < units)
            {
                diag.Add($"HotelFuelStock: fuel shortfall — {units} × {fuelName} wanted, {UnitsOnHand(fuelName)} on hand (day {dayIndex}). Nothing burned.");
                return null;
            }

            var lines = new List<HotelFuelDispenseLine>();
            int remaining = units;
            for (int i = 0; i < lots.Count && remaining > 0; i++)
            {
                HotelFuelLot lot = lots[i];
                if (lot == null) continue;
                if (!string.Equals(lot.FuelName, fuelName, StringComparison.OrdinalIgnoreCase)) continue;
                if (lot.Units <= 0) continue;
                int take = Math.Min(remaining, lot.Units);
                lot.Units -= take;
                remaining -= take;
                lines.Add(new HotelFuelDispenseLine
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

            diag.Add($"HotelFuelStock: burned {units} × {fuelName} (day {dayIndex}).");
            return lines;
        }

        public int UnitsOnHand(string fuelName)
        {
            int total = 0;
            foreach (HotelFuelLot lot in lots)
            {
                if (lot != null && string.Equals(lot.FuelName, fuelName, StringComparison.OrdinalIgnoreCase))
                    total += Math.Max(0, lot.Units);
            }

            return total;
        }

        #region Save / Load
        [Serializable]
        public sealed class HotelFuelStockSaveDto
        {
            public List<HotelFuelLot> Lots = new List<HotelFuelLot>();
        }

        public HotelFuelStockSaveDto CaptureSaveDto()
        {
            var dto = new HotelFuelStockSaveDto();
            foreach (HotelFuelLot lot in lots)
            {
                if (lot != null) dto.Lots.Add(lot);
            }

            return dto;
        }

        public void LoadFromSaveDto(HotelFuelStockSaveDto dto)
        {
            lots.Clear();
            if (dto?.Lots == null) return;
            foreach (HotelFuelLot lot in dto.Lots)
            {
                if (lot == null) continue;
                lots.Add(lot);
            }
        }
        #endregion
    }

    /// <summary>
    /// D2A: the hotel's one-time opening fuel endowment — explicit, marked,
    /// never auto-replenished (upstream-provenance doctrine).
    /// </summary>
    public static class HotelFuelBootstrap
    {
        public static void ApplyBootstrapEndowment(
            HotelFuelStock stock,
            EntityIdRegistry idRegistry,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (stock == null)
            {
                diagnostics.Add("HotelFuelBootstrap: no fuel stock — endowment not applied.");
                return;
            }

            if (idRegistry == null)
            {
                diagnostics.Add("HotelFuelBootstrap: no id registry — endowment not applied.");
                return;
            }

            string rejection = stock.ReceiveLot(new HotelFuelLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                FuelName = HotelFuelSupply.FuelMaterialId,
                Units = HotelFuelSupply.BootstrapFuelUnits,
                AcquiredDayIndex = dayIndex,
                IsBootstrapEndowment = true,
                SupplierNote = "Opening hotel stocking (one-time; reorder via fuel dealers and import orders only).",
            }, diagnostics);
            if (rejection != null)
            {
                diagnostics.Add($"HotelFuelBootstrap: {rejection}");
                return;
            }

            diagnostics.Add("HotelFuelBootstrap: BOOTSTRAP endowment applied (one-time opening stock). " +
                "Upstream-provenance doctrine: this lot is explicitly marked and will never auto-replenish.");
        }
    }
}
