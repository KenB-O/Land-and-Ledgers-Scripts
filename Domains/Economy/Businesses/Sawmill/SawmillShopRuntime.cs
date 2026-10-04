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
    /// D2B: SawMinutes carries the run's labor calibration (profile
    /// SawMinutesPerLog x logs sawn) so the task system schedules honest
    /// work — W4A authored the minutes but never reported them.
    /// </summary>
    [Serializable]
    public sealed class SawmillSawingResult
    {
        public int LogsSawn;
        public string SourceLogLotId = string.Empty;
        public SawmillLumberLot LumberLot;
        public SlabOffcutFuelLot ByproductLot;
        public int SawMinutes;

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
    ///
    /// D2B adds: the blade as a real equipment asset with a spare-blade
    /// inventory (Canon Part V: "replacement saws"; Tech X §3.3), run labor
    /// minutes on every sawing result, and the toll-sawing commercial form
    /// (customer-owned logs in custody, explicit toll policy — the W5A
    /// gristmill toll form mirrored for lumber).
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
        private readonly SawmillBladeInventory bladeInventory;
        private readonly SawmillTollLogStock tollLogStock = new SawmillTollLogStock();
        private readonly SawmillTollPickupRegister tollPickup = new SawmillTollPickupRegister();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public string BusinessInstanceId => businessInstanceId;
        public SawmillLogStock LogStock => logStock;
        public SawmillLumberStock LumberStock => lumberStock;
        public IReadOnlyList<SlabOffcutFuelLot> ByproductStock => byproductStock;
        public SawmillBladeState Blade => blade;
        public SawmillBladeInventory BladeInventory => bladeInventory;
        public SawmillTollLogStock TollLogStock => tollLogStock;
        public SawmillTollPickupRegister TollPickup => tollPickup;

        public SawmillShopRuntime(string businessInstanceId, EntityIdRegistry idRegistry, string bladeId = null)
        {
            this.businessInstanceId = businessInstanceId ?? string.Empty;
            this.idRegistry = idRegistry;
            this.lumberStock = new SawmillLumberStock(this.businessInstanceId);
            this.blade = new SawmillBladeState(string.IsNullOrWhiteSpace(bladeId)
                ? this.businessInstanceId + "-blade" : bladeId);
            this.bladeInventory = new SawmillBladeInventory(this.businessInstanceId);
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
                StackedDayIndex = dayIndex, // D2B: green-lumber handling starts at stacking; drying calibration is research-blocked
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
                SawMinutes = logsToSaw * profile.SawMinutesPerLog, // D2B: honest labor figure for the task system
            };
        }

        /// <summary>W4A: the filer's work — restores the blade, records the event.</summary>
        public string RecordSawFiling(EntityId filer, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            return blade.RecordFiling(dayIndex, diag);
        }

        /// <summary>
        /// D2B: the filer's heavier work — re-sets a BROKEN saw with
        /// set/swage tools (Canon Part V occupation table). Filing cannot
        /// fix a broken saw; re-setting or a spare blade can.
        /// </summary>
        public string RecordSawReset(EntityId filer, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            return blade.RecordReset(dayIndex, diag);
        }

        /// <summary>
        /// D2B: stamps the fitted blade's procurement provenance — a named
        /// supplier or the explicit bootstrap endowment. Anonymous blades
        /// are refused (upstream-provenance doctrine).
        /// </summary>
        public string RecordBladeProcurement(string source, int dayIndex, bool isBootstrapEndowment, List<string> diag)
        {
            diag = diag ?? diagnostics;
            return blade.RecordProcurement(source, dayIndex, isBootstrapEndowment, diag);
        }

        /// <summary>D2B: receives a spare blade into the mill's blade inventory (provenance validated).</summary>
        public string ReceiveSpareBlade(SawmillBladeState spareBlade, List<string> diag)
        {
            diag = diag ?? diagnostics;
            return bladeInventory.ReceiveSpareBlade(spareBlade, diag);
        }

        /// <summary>
        /// D2B: fits the named spare blade on the saw line, parking the
        /// previously fitted blade back in the spares. The mill's blade
        /// economy: broken or retired blades are replaced from real spares,
        /// never conjured. Returns the refusal, or null when fitted.
        /// </summary>
        public string FitSpareBlade(string spareBladeId, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            SawmillBladeState spare = bladeInventory.TryTakeSpare(spareBladeId, diag);
            if (spare == null) return $"SawmillShopRuntime.FitSpareBlade: no spare blade '{spareBladeId}' on hand — the mill stays gated.";

            string oldBladeId = blade.BladeId;
            float oldCondition = blade.Condition01;
            List<string> oldLog = new List<string>(blade.MaintenanceLog);
            bool oldWasRetired = blade.IsRetired;

            // Swap identities: the spare becomes the fitted blade; the old
            // fitted blade (unless retired scrap) parks back as a spare.
            var parked = new SawmillBladeState(oldBladeId)
            {
                Condition01 = oldCondition,
                OwnerBusinessId = businessInstanceId,
                LocationId = businessInstanceId,
                ProcurementSource = blade.ProcurementSource,
                ProcuredDayIndex = blade.ProcuredDayIndex,
                IsBootstrapEndowment = blade.IsBootstrapEndowment,
                ResetCount = blade.ResetCount,
            };
            parked.MaintenanceLog.AddRange(oldLog);

            blade.BladeId = spare.BladeId;
            blade.Condition01 = spare.Condition01;
            blade.MaintenanceLog.Clear();
            blade.MaintenanceLog.AddRange(spare.MaintenanceLog);
            blade.OwnerBusinessId = businessInstanceId;
            blade.LocationId = businessInstanceId;
            blade.ProcurementSource = spare.ProcurementSource;
            blade.ProcuredDayIndex = spare.ProcuredDayIndex;
            blade.IsBootstrapEndowment = spare.IsBootstrapEndowment;
            blade.ResetCount = spare.ResetCount;
            blade.IsRetired = false;
            blade.RetiredReason = string.Empty;
            blade.RetiredDayIndex = -1;
            blade.MaintenanceLog.Add($"Day {dayIndex}: fitted on the saw line (replacing '{oldBladeId}')");

            if (!oldWasRetired)
            {
                string parkRefusal = bladeInventory.ParkAsSpare(parked, diag);
                if (parkRefusal != null) diag.Add("SawmillShopRuntime.FitSpareBlade: " + parkRefusal);
            }
            else
            {
                diag.Add($"SawmillShopRuntime.FitSpareBlade: '{oldBladeId}' was retired scrap — it stays in the scrap register, not the spares.");
            }

            diag.Add($"SawmillShopRuntime {businessInstanceId}: spare blade '{spare.BladeId}' fitted (condition {spare.Condition01:0.00}); "
                + $"'{oldBladeId}' parked as a spare.");
            return null;
        }

        /// <summary>
        /// D2B: retires the fitted blade to scrap with a recorded reason
        /// (e.g. cracked plate). The mill then has no serviceable blade —
        /// sawing stays gated until a spare is fitted. Never auto-replaced.
        /// </summary>
        public string RetireFittedBlade(string reason, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            string refusal = blade.Retire(reason, dayIndex, diag);
            if (refusal != null) return refusal;
            bladeInventory.MoveToScrap(blade, diag);
            return null;
        }

        /// <summary>D2B: toll log intake — the customer's logs into custody, never mill inventory.</summary>
        public string ReceiveTollLogLot(LogLot lot, string customerId, string customerName, int dayIndex, List<string> diag)
        {
            return tollLogStock.ReceiveTollLogLot(lot, customerId, customerName, dayIndex, diag ?? diagnostics);
        }

        /// <summary>
        /// D2B: saws the customer's logs under an explicit toll policy —
        /// the toll-sawing commercial form (cf. W5A gristmill toll grind).
        /// Gate order: toll terms named, NX-1 workstation gate (Canon 4.1),
        /// then the §3.9 blade gate — all refuse loudly. Returns null on
        /// refusal; refused runs consume and wear nothing.
        ///
        /// The customer keeps the lumber minus the mill's in-kind toll;
        /// toll byproducts (slabs/offcuts) belong to the customer too and
        /// wait in the pickup register. The in-kind toll lumber goes to the
        /// mill's own lumber yard. The cash toll is returned on the result
        /// for the ledger authority to post — money never moves here.
        /// </summary>
        public SawmillTollSawingResult RunTollSawing(
            string tollLotId,
            int logsToSaw,
            EntityId worker,
            int dayIndex,
            SawmillTollPolicy policy,
            List<string> diag,
            EquipmentTaskGate gate = null)
        {
            diag = diag ?? diagnostics;
            if (idRegistry == null)
            {
                diag.Add("SawmillShopRuntime: no EntityIdRegistry — toll sawing refused.");
                return null;
            }
            if (policy == null || !policy.OffersTollSawing)
            {
                diag.Add("SawmillShopRuntime: REFUSED toll sawing — no toll terms named. "
                    + "Custom work is refused until the mill sets its toll (cash per log and/or in-kind share); terms are never assumed.");
                return null;
            }
            if (logsToSaw <= 0)
            {
                diag.Add("SawmillShopRuntime: no logs requested.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(tollLotId))
            {
                diag.Add("SawmillShopRuntime: toll sawing requires the customer's toll lot id — anonymous toll sawing refused.");
                return null;
            }

            // NX-1A: Canon 4.1 — the sawmill saw line workstation.
            if (gate != null)
            {
                string blocked = gate.CheckCodes(
                    new List<string> { EquipmentRequirementCodes.Workstation("sawmill-saw-line") },
                    "business", businessInstanceId, dayIndex, diag);
                if (blocked != null) return null;
            }

            // Tech X §3.9: a dull/broken saw gates ALL output until the filer works.
            if (blade.CheckSawGate(dayIndex, diag) != null) return null;

            var take = tollLogStock.TryTakeTollLogs(tollLotId, logsToSaw, diag);
            if (take == null) return null;
            TollLogCustodyLot custody = take.Value.Key;
            int logsSawn = take.Value.Value;
            LogLot sourceLot = custody.Lot;

            string sourceLogLotId = sourceLot.LotId.ToString();
            var profile = SawmillConversionData.DefaultForSpecies(sourceLot.Species);

            blade.ApplyWear(profile.BladeWearPerLog01 * logsSawn);

            int lumberUnits = logsSawn * profile.LumberPerLog;
            int tollInKind = policy.TollInKindUnits(lumberUnits);
            int customerLumber = lumberUnits - tollInKind;

            if (tollInKind > 0)
            {
                var tollLot = new SawmillLumberLot
                {
                    LotId = idRegistry.Allocate(EntityKind.Lot),
                    LumberUnits = tollInKind,
                    SourceLogLotId = sourceLogLotId,
                    StandId = sourceLot.StandId,
                    Species = sourceLot.Species,
                    MillBusinessId = businessInstanceId,
                    SawedBy = worker,
                    SawedDayIndex = dayIndex,
                    ConversionProfileId = profile.ProfileId,
                    StackedDayIndex = dayIndex,
                };
                string refusal = lumberStock.ReceiveLot(tollLot, diag);
                if (refusal != null)
                {
                    diag.Add("SawmillShopRuntime: in-kind toll lumber refused after toll sawing — " + refusal);
                    return null;
                }
            }

            if (customerLumber > 0)
            {
                tollPickup.Hold(new SawmillTollProductCustody
                {
                    LumberLot = new SawmillLumberLot
                    {
                        LotId = idRegistry.Allocate(EntityKind.Lot),
                        LumberUnits = customerLumber,
                        SourceLogLotId = sourceLogLotId,
                        StandId = sourceLot.StandId,
                        Species = sourceLot.Species,
                        MillBusinessId = businessInstanceId,
                        SawedBy = worker,
                        SawedDayIndex = dayIndex,
                        ConversionProfileId = profile.ProfileId,
                        StackedDayIndex = dayIndex,
                    },
                    CustomerId = custody.CustomerId,
                    CustomerName = custody.CustomerName,
                    ProducedDayIndex = dayIndex,
                }, diag);
            }

            int byproductUnits = logsSawn * profile.FuelWoodUnitsPerLog;
            if (byproductUnits > 0)
            {
                tollPickup.Hold(new SawmillTollProductCustody
                {
                    ByproductLot = new SlabOffcutFuelLot
                    {
                        LotId = idRegistry.Allocate(EntityKind.Lot),
                        FuelWoodUnits = byproductUnits,
                        SourceMillBusinessId = businessInstanceId,
                        SourceLogLotId = sourceLogLotId,
                        StandId = sourceLot.StandId,
                        Species = sourceLot.Species,
                        ProducedDayIndex = dayIndex,
                    },
                    CustomerId = custody.CustomerId,
                    CustomerName = custody.CustomerName,
                    ProducedDayIndex = dayIndex,
                }, diag);
            }

            var result = new SawmillTollSawingResult
            {
                LogsSawn = logsSawn,
                SourceTollLotId = tollLotId,
                CustomerId = custody.CustomerId,
                CustomerName = custody.CustomerName,
                CustomerLumberUnits = customerLumber,
                TollInKindLumberUnits = tollInKind,
                TollCashCents = logsSawn * Math.Max(0, policy.TollCashCentsPerLog),
                CustomerFuelWoodUnits = byproductUnits,
                SawMinutes = logsSawn * profile.SawMinutesPerLog,
            };

            diag.Add($"SawmillShopRuntime {businessInstanceId}: TOLL sawed {logsSawn} logs from {custody.CustomerName}'s lot {tollLotId} "
                + $"— customer keeps {customerLumber} lumber units; mill toll {tollInKind} in kind + {result.TollCashCents}c cash; "
                + $"{byproductUnits} fuel-wood units to customer custody. Blade condition now {blade.Condition01:0.00}.");
            if (blade.IsDull)
            {
                diag.Add($"SawmillShopRuntime: blade '{blade.BladeId}' is now dull — the filer must work before the next run.");
            }

            return result;
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

        /// <summary>W4A save contract: lives inside the owning runtime class. D2B adds blade inventory + toll registers.</summary>
        [Serializable]
        public sealed class SawmillShopRuntimeSaveDto
        {
            public string BusinessInstanceId = string.Empty;
            public SawmillLogStock.SawmillLogStockSaveDto LogStock = new SawmillLogStock.SawmillLogStockSaveDto();
            public SawmillLumberStock.SawmillLumberStockSaveDto LumberStock = new SawmillLumberStock.SawmillLumberStockSaveDto();
            public List<SlabOffcutFuelLot> ByproductStock = new List<SlabOffcutFuelLot>();
            public SawmillBladeState.SawmillBladeStateSaveDto Blade = new SawmillBladeState.SawmillBladeStateSaveDto();
            public SawmillBladeInventory.SawmillBladeInventorySaveDto BladeInventory = new SawmillBladeInventory.SawmillBladeInventorySaveDto();
            public SawmillTollLogStock.SawmillTollLogStockSaveDto TollLogStock = new SawmillTollLogStock.SawmillTollLogStockSaveDto();
            public SawmillTollPickupRegister.SawmillTollPickupRegisterSaveDto TollPickup = new SawmillTollPickupRegister.SawmillTollPickupRegisterSaveDto();
        }

        public SawmillShopRuntimeSaveDto CaptureSaveDto()
        {
            var dto = new SawmillShopRuntimeSaveDto
            {
                BusinessInstanceId = businessInstanceId,
                LogStock = logStock.CaptureSaveDto(),
                LumberStock = lumberStock.CaptureSaveDto(),
                Blade = blade.CaptureSaveDto(),
                BladeInventory = bladeInventory.CaptureSaveDto(),
                TollLogStock = tollLogStock.CaptureSaveDto(),
                TollPickup = tollPickup.CaptureSaveDto(),
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
            bladeInventory.LoadFromSaveDto(dto != null ? dto.BladeInventory : null);
            tollLogStock.LoadFromSaveDto(dto != null ? dto.TollLogStock : null);
            tollPickup.LoadFromSaveDto(dto != null ? dto.TollPickup : null);
        }
    }
}
