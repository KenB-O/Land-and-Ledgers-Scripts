using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Sawmill
{
    /// <summary>
    /// W4A: one slab/offcut fuel-wood lot — the sawmill's byproduct as a REAL
    /// lot with identity and full provenance, never synthetic units. Canon
    /// §8.4: sawmill slabs and offcuts are fuel wood (Kennedy decision
    /// 2026-10-03: they feed the FuelDealer's town fuel yard).
    /// </summary>
    [Serializable]
    public sealed class SlabOffcutFuelLot
    {
        public EntityId LotId = EntityId.Invalid; // EntityKind.Lot (HF-1)
        public int FuelWoodUnits;
        public string SourceMillBusinessId = string.Empty;
        public string SourceLogLotId = string.Empty;
        public string StandId = string.Empty;
        public string Species = string.Empty;
        public int ProducedDayIndex;

        public SlabOffcutFuelLot() { }

        public string ProvenanceChain()
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(StandId)) parts.Add($"stand {StandId}");
            if (!string.IsNullOrWhiteSpace(SourceLogLotId)) parts.Add($"log lot {SourceLogLotId}");
            if (!string.IsNullOrWhiteSpace(Species)) parts.Add(Species);
            if (!string.IsNullOrWhiteSpace(SourceMillBusinessId)) parts.Add($"mill {SourceMillBusinessId}");
            parts.Add($"day {ProducedDayIndex}");
            return parts.Count == 0 ? "NO PROVENANCE" : string.Join(" | ", parts.ToArray());
        }
    }

    /// <summary>
    /// W4A: the FuelDealer-side intake register for sawmill slabs and
    /// offcuts. There is no FuelDealer per-instance business package yet —
    /// the yard's existing implementation is the FuelYardProfile (EQP-5
    /// equipment profile), the archetype local-market outlet, and the
    /// recurring-order template "sawmill_offcuts_to_fuel_dealer"
    /// (Sawmill "slabs_offcuts" -> FuelDealer, weekly target 10). Until a
    /// FuelDealer per-instance runtime exists, this register is the intake
    /// contract: custody transfer of NAMED lots to a NAMED FuelDealer
    /// business instance. The FuelDealer takes real lots with provenance —
    /// the byproducts are never synthesized into its stock.
    /// </summary>
    public sealed class FuelYardByproductIntake
    {
        private readonly List<string> diagnostics = new List<string>();
        private readonly Dictionary<string, List<SlabOffcutFuelLot>> lotsByFuelDealer =
            new Dictionary<string, List<SlabOffcutFuelLot>>(StringComparer.Ordinal);

        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>Receives a byproduct lot into a named FuelDealer's custody. Returns the refusal, or null.</summary>
        public string ReceiveByproductLot(string fuelDealerBusinessId, SlabOffcutFuelLot lot, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(fuelDealerBusinessId))
                return "FuelYardByproductIntake.ReceiveByproductLot: no FuelDealer business named — slabs go to a real yard, not the void.";
            if (lot == null)
                return "FuelYardByproductIntake.ReceiveByproductLot: no lot offered — fuel wood is not conjured.";
            if (lot.LotId == EntityId.Invalid)
                return "FuelYardByproductIntake.ReceiveByproductLot: a lot needs an EntityId — anonymous stock is refused.";
            if (lot.FuelWoodUnits <= 0)
                return "FuelYardByproductIntake.ReceiveByproductLot: a lot needs positive fuel-wood units.";
            if (string.IsNullOrWhiteSpace(lot.SourceMillBusinessId))
                return "FuelYardByproductIntake.ReceiveByproductLot: no source mill — orphan fuel refused.";
            if (string.IsNullOrWhiteSpace(lot.SourceLogLotId) || string.IsNullOrWhiteSpace(lot.StandId))
                return "FuelYardByproductIntake.ReceiveByproductLot: incomplete upstream chain — slabs must trace to a log lot and a stand.";

            List<SlabOffcutFuelLot> held;
            if (!lotsByFuelDealer.TryGetValue(fuelDealerBusinessId, out held))
            {
                held = new List<SlabOffcutFuelLot>();
                lotsByFuelDealer[fuelDealerBusinessId] = held;
            }
            held.Add(lot);
            diag.Add($"FuelYardByproductIntake: FuelDealer '{fuelDealerBusinessId}' received {lot.FuelWoodUnits} fuel-wood units "
                + $"(lot {lot.LotId}) — {lot.ProvenanceChain()}.");
            return null;
        }

        public IReadOnlyList<SlabOffcutFuelLot> LotsFor(string fuelDealerBusinessId)
        {
            List<SlabOffcutFuelLot> held;
            if (string.IsNullOrWhiteSpace(fuelDealerBusinessId) || !lotsByFuelDealer.TryGetValue(fuelDealerBusinessId, out held))
                return new List<SlabOffcutFuelLot>();
            return held;
        }

        public int TotalFuelWoodUnitsFor(string fuelDealerBusinessId)
        {
            int total = 0;
            foreach (var lot in LotsFor(fuelDealerBusinessId)) total += Math.Max(0, lot.FuelWoodUnits);
            return total;
        }

        /// <summary>
        /// Pass 2 (task system): one fuel dispense line — a FIFO slice of a
        /// named intake lot, with its provenance carried forward. This is the
        /// real dispense API for the Fuel Dealer. Callers must use it instead
        /// of deducting FIFO in place (the ThreeBusinessSlice bridge pattern,
        /// which this API replaces as the production model; the bridge itself
        /// is left untouched for its orchestrator tests).
        /// </summary>
        [Serializable]
        public sealed class FuelYardDispenseLine
        {
            public string LotId = string.Empty;
            public int UnitsTaken;
            public string ProvenanceChain = string.Empty;

            public FuelYardDispenseLine() { }
        }

        /// <summary>
        /// Pass 2: dispenses fuel-wood units FIFO (oldest lot first) from a
        /// named Fuel Dealer's intake. Returns null with a diagnostic when
        /// the dealer, the request, or the stock cannot support the dispense
        /// (unknown dealer, non-positive units, or shortfall) — shortfall
        /// dispenses NOTHING, never a partial lot, so a sale either moves
        /// whole or moves nothing. Emptied lots are removed, so a lot at
        /// zero units is never re-sold. Provenance is preserved per line.
        /// </summary>
        public List<FuelYardDispenseLine> TryDispenseUnits(
            string fuelDealerBusinessId, int units, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (string.IsNullOrWhiteSpace(fuelDealerBusinessId))
            {
                diag.Add("FuelYardByproductIntake: dispense refused — no FuelDealer business named.");
                return null;
            }
            if (units <= 0)
            {
                diag.Add("FuelYardByproductIntake: dispense refused — positive unit count required.");
                return null;
            }
            List<SlabOffcutFuelLot> held;
            if (!lotsByFuelDealer.TryGetValue(fuelDealerBusinessId, out held))
            {
                diag.Add($"FuelYardByproductIntake: dispense refused — FuelDealer '{fuelDealerBusinessId}' holds no intake lots.");
                return null;
            }
            if (TotalFuelWoodUnitsFor(fuelDealerBusinessId) < units)
            {
                diag.Add($"FuelYardByproductIntake: dispense refused — FuelDealer '{fuelDealerBusinessId}' holds "
                    + $"{TotalFuelWoodUnitsFor(fuelDealerBusinessId)} fuel-wood units, {units} requested; nothing dispensed.");
                return null;
            }

            var lines = new List<FuelYardDispenseLine>();
            int remaining = units;
            foreach (var lot in held)
            {
                if (remaining <= 0) break;
                if (lot == null || lot.FuelWoodUnits <= 0) continue;
                int taken = Math.Min(lot.FuelWoodUnits, remaining);
                lines.Add(new FuelYardDispenseLine
                {
                    LotId = lot.LotId.ToString(),
                    UnitsTaken = taken,
                    ProvenanceChain = lot.ProvenanceChain(),
                });
                lot.FuelWoodUnits -= taken;
                remaining -= taken;
            }
            held.RemoveAll(lot => lot == null || lot.FuelWoodUnits <= 0);
            diag.Add($"FuelYardByproductIntake: FuelDealer '{fuelDealerBusinessId}' dispensed {units} fuel-wood units (day {dayIndex}).");
            return lines;
        }

        /// <summary>W4A save contract: lives inside the owning intake class.</summary>
        [Serializable]
        public sealed class FuelYardByproductIntakeSaveDto
        {
            public List<SlabOffcutFuelLot> Lots = new List<SlabOffcutFuelLot>();
            public List<string> LotOwners = new List<string>(); // parallel to Lots: owning FuelDealer per lot
        }

        public FuelYardByproductIntakeSaveDto CaptureSaveDto()
        {
            var dto = new FuelYardByproductIntakeSaveDto();
            foreach (var entry in lotsByFuelDealer)
            {
                foreach (var lot in entry.Value)
                {
                    dto.LotOwners.Add(entry.Key);
                    dto.Lots.Add(new SlabOffcutFuelLot
                    {
                        LotId = lot.LotId,
                        FuelWoodUnits = lot.FuelWoodUnits,
                        SourceMillBusinessId = lot.SourceMillBusinessId,
                        SourceLogLotId = lot.SourceLogLotId,
                        StandId = lot.StandId,
                        Species = lot.Species,
                        ProducedDayIndex = lot.ProducedDayIndex,
                    });
                }
            }
            return dto;
        }

        public void LoadFromSaveDto(FuelYardByproductIntakeSaveDto dto)
        {
            lotsByFuelDealer.Clear();
            if (dto == null) return;
            int count = Math.Min(dto.Lots.Count, dto.LotOwners.Count);
            for (int i = 0; i < count; i++)
            {
                var lot = dto.Lots[i];
                string owner = dto.LotOwners[i];
                if (lot == null || string.IsNullOrWhiteSpace(owner)) continue;
                List<SlabOffcutFuelLot> held;
                if (!lotsByFuelDealer.TryGetValue(owner, out held))
                {
                    held = new List<SlabOffcutFuelLot>();
                    lotsByFuelDealer[owner] = held;
                }
                held.Add(lot);
            }
        }
    }
}
