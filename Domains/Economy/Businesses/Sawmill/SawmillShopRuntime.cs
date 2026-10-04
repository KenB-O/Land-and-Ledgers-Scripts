using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Farming.Timber;
using LandLedgers.Primitives;
using LandLedgers.Tasks;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Sawmill
{
    /// <summary>
    /// W4A: the result of one sawing run — the real lots produced, each with
    /// identity and provenance. A run consumes from exactly ONE log lot, so
    /// the lumber lot and the byproduct lot trace to exactly one stand.
    /// </summary>
    [Serializable]
    public sealed class SawmillSawingResult
    {
        public int LogsSawn;
        public string SourceLogLotId = string.Empty;
        public SawmillLumberLot LumberLot;
        public SlabOffcutFuelLot ByproductLot;

        public SawmillSawingResult() { }
    }

    /// <summary>
    /// W4A: the per-instance sawmill runtime — the detailed layer behind a
    /// Sawmill business instance (BusinessType.Sawmill = 6). One instance
    /// per business.
    ///
    /// What this owns (the detailed layer):
    /// - log intake with provenance validation (anonymous logs refused loudly);
    /// - log -> lumber conversion with ratios as DATA (species profiles);
    /// - lumber lots with the full provenance chain (stand -> log lot ->
    ///   mill -> sawyer -> day -> conversion profile);
    /// - blade wear and the filer loop: a dull/broken saw gates ALL output
    ///   until the filer works (Tech X §3.9), following the NX-1 equipment
    ///   gate pattern for the workstation itself;
    /// - slab/offcut byproduct lots held as real lots and transferred by
    ///   custody to a named FuelDealer (Canon §8.4; Kennedy decision
    ///   2026-10-03), never synthesized into fuel stock.
    ///
    /// Money moves only through ledger authorities, never here. This sits
    /// ALONGSIDE the T1E TimberChain.Sawmill authority (which keeps owning
    /// the "saw-logs" task registration and the archetype path) — no
    /// rewrite, no rename, no renumber.
    /// </summary>
    public sealed class SawmillShopRuntime
    {
        private readonly List<string> diagnostics = new List<string>();
        private readonly string businessInstanceId;
        private readonly EntityIdRegistry idRegistry;
        private readonly SawmillLogStock logStock = new SawmillLogStock();
        private readonly SawmillLumberStock lumberStock;
        private readonly List<SlabOffcutFuelLot> byproductStock = new List<SlabOffcutFuelLot>();
        private readonly SawmillBladeState blade;

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public string BusinessInstanceId => businessInstanceId;
        public SawmillLogStock LogStock => logStock;
        public SawmillLumberStock LumberStock => lumberStock;
        public IReadOnlyList<SlabOffcutFuelLot> ByproductStock => byproductStock;
        public SawmillBladeState Blade => blade;

        public SawmillShopRuntime(string businessInstanceId, EntityIdRegistry idRegistry, string bladeId = null)
        {
            this.businessInstanceId = businessInstanceId ?? string.Empty;
            this.idRegistry = idRegistry;
            this.lumberStock = new SawmillLumberStock(this.businessInstanceId);
            this.blade = new SawmillBladeState(string.IsNullOrWhiteSpace(bladeId)
                ? this.businessInstanceId + "-blade" : bladeId);
        }

        /// <summary>W4A: log intake — provenance validated by the log stock.</summary>
        public string ReceiveLogLot(LogLot lot, List<string> diag)
        {
            return logStock.ReceiveLogLot(lot, diag ?? diagnostics);
        }

        /// <summary>
        /// W4A: registers ONLY the file-saw maintenance task. The "saw-logs"
        /// task registration stays owned by T1E
        /// (TimberChain.Sawmill.RegisterTaskDefinitions); duplicates are
        /// rejected deterministically, so this package never double-registers.
        /// </summary>
        public void RegisterTaskDefinitions(TaskAuthority authority, List<string> diag)
        {
            SawmillMaintenanceTasks.RegisterTaskDefinitions(authority, diag ?? diagnostics);
        }

        /// <summary>
        /// Saws logsToSaw logs from the oldest log lot into a lumber lot plus
        /// a slab/offcut byproduct lot. Gate order: NX-1 workstation gate
        /// (Canon 4.1), then the §3.9 blade gate — both refuse loudly.
        /// Returns null on refusal; refused runs consume and wear nothing.
        /// </summary>
        public SawmillSawingResult RunSawing(
            int logsToSaw,
            EntityId worker,
            int dayIndex,
            List<string> diag,
            EquipmentTaskGate gate = null)
        {
            diag = diag ?? diagnostics;
            if (idRegistry == null)
            {
                diag.Add("SawmillShopRuntime: no EntityIdRegistry — sawing refused.");
                return null;
            }
            if (logsToSaw <= 0)
            {
                diag.Add("SawmillShopRuntime: no logs requested.");
                return null;
            }

            // NX-1A: Canon 4.1 — the sawmill saw line workstation, with its
            // support requirements (Canon 5.2: saw-filing repair capability,
            // mill lubrication). Mirrors T1E Sawmill.SawLogs gating.
            if (gate != null)
            {
                string blocked = gate.CheckCodes(
                    new List<string> { EquipmentRequirementCodes.Workstation("sawmill-saw-line") },
                    "business", businessInstanceId, dayIndex, diag);
                if (blocked != null) return null;
            }

            // Tech X §3.9: a dull/broken saw gates ALL output until the filer works.
            if (blade.CheckSawGate(dayIndex, diag) != null) return null;

            LogLot sourceLot = logStock.TryTakeOldestLot(logsToSaw, diag);
            if (sourceLot == null) return null;

            string sourceLogLotId = sourceLot.LotId.ToString();
            var profile = SawmillConversionData.DefaultForSpecies(sourceLot.Species);

            blade.ApplyWear(profile.BladeWearPerLog01 * logsToSaw);

            var lumberLot = new SawmillLumberLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                LumberUnits = logsToSaw * profile.LumberPerLog,
                SourceLogLotId = sourceLogLotId,
                StandId = sourceLot.StandId,
                Species = sourceLot.Species,
                MillBusinessId = businessInstanceId,
                SawedBy = worker,
                SawedDayIndex = dayIndex,
                ConversionProfileId = profile.ProfileId,
            };
            string lumberRefusal = lumberStock.ReceiveLot(lumberLot, diag);
            if (lumberRefusal != null)
            {
                diag.Add("SawmillShopRuntime: lumber intake refused after sawing — " + lumberRefusal);
                return null;
            }

            var byproductLot = new SlabOffcutFuelLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                FuelWoodUnits = logsToSaw * profile.FuelWoodUnitsPerLog,
                SourceMillBusinessId = businessInstanceId,
                SourceLogLotId = sourceLogLotId,
                StandId = sourceLot.StandId,
                Species = sourceLot.Species,
                ProducedDayIndex = dayIndex,
            };
            byproductStock.Add(byproductLot);

            diag.Add($"SawmillShopRuntime {businessInstanceId}: sawed {logsToSaw} logs from lot {sourceLogLotId} "
                + $"into lumber lot {lumberLot.LotId} ({lumberLot.LumberUnits} units, profile '{profile.ProfileId}') "
                + $"and byproduct lot {byproductLot.LotId} ({byproductLot.FuelWoodUnits} fuel-wood units). "
                + $"Blade condition now {blade.Condition01:0.00}.");
            if (blade.IsDull)
            {
                diag.Add($"SawmillShopRuntime: blade '{blade.BladeId}' is now dull — the filer must work before the next run.");
            }

            return new SawmillSawingResult
            {
                LogsSawn = logsToSaw,
                SourceLogLotId = sourceLogLotId,
                LumberLot = lumberLot,
                ByproductLot = byproductLot,
            };
        }

        /// <summary>W4A: the filer's work — restores the blade, records the event.</summary>
        public string RecordSawFiling(EntityId filer, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            return blade.RecordFiling(dayIndex, diag);
        }

        /// <summary>
        /// Transfers held slab/offcut lots by custody to a named FuelDealer
        /// business instance — real lots with provenance move into the
        /// yard's intake register (never synthesized). Returns the number
        /// of lots transferred.
        /// </summary>
        public int TransferByproductsToFuelDealer(
            string fuelDealerBusinessId,
            FuelYardByproductIntake intake,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (intake == null)
            {
                diag.Add("SawmillShopRuntime: no FuelDealer intake — byproducts stay in the mill yard.");
                return 0;
            }
            if (byproductStock.Count == 0)
            {
                diag.Add("SawmillShopRuntime: no byproduct lots to transfer.");
                return 0;
            }

            int transferred = 0;
            foreach (var lot in new List<SlabOffcutFuelLot>(byproductStock))
            {
                string refusal = intake.ReceiveByproductLot(fuelDealerBusinessId, lot, diag);
                if (refusal == null)
                {
                    byproductStock.Remove(lot);
                    transferred++;
                }
                else
                {
                    diag.Add($"SawmillShopRuntime: byproduct lot {lot.LotId} held back — {refusal}");
                }
            }

            diag.Add($"SawmillShopRuntime: transferred {transferred} byproduct lot(s) to FuelDealer '{fuelDealerBusinessId}' by custody.");
            return transferred;
        }

        /// <summary>W4A save contract: lives inside the owning runtime class.</summary>
        [Serializable]
        public sealed class SawmillShopRuntimeSaveDto
        {
            public string BusinessInstanceId = string.Empty;
            public SawmillLogStock.SawmillLogStockSaveDto LogStock = new SawmillLogStock.SawmillLogStockSaveDto();
            public SawmillLumberStock.SawmillLumberStockSaveDto LumberStock = new SawmillLumberStock.SawmillLumberStockSaveDto();
            public List<SlabOffcutFuelLot> ByproductStock = new List<SlabOffcutFuelLot>();
            public SawmillBladeState.SawmillBladeStateSaveDto Blade = new SawmillBladeState.SawmillBladeStateSaveDto();
        }

        public SawmillShopRuntimeSaveDto CaptureSaveDto()
        {
            var dto = new SawmillShopRuntimeSaveDto
            {
                BusinessInstanceId = businessInstanceId,
                LogStock = logStock.CaptureSaveDto(),
                LumberStock = lumberStock.CaptureSaveDto(),
                Blade = blade.CaptureSaveDto(),
            };
            foreach (var lot in byproductStock)
            {
                dto.ByproductStock.Add(new SlabOffcutFuelLot
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
            return dto;
        }

        public void LoadFromSaveDto(SawmillShopRuntimeSaveDto dto)
        {
            logStock.LoadFromSaveDto(dto != null ? dto.LogStock : null);
            lumberStock.LoadFromSaveDto(dto != null ? dto.LumberStock : null);
            byproductStock.Clear();
            if (dto != null && dto.ByproductStock != null)
            {
                foreach (var lot in dto.ByproductStock)
                {
                    if (lot == null) continue;
                    byproductStock.Add(lot);
                }
            }
            blade.LoadFromSaveDto(dto != null ? dto.Blade : null);
        }
    }
}
