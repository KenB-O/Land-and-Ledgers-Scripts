using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.Sawmill;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Farming.Timber;
using LandLedgers.Primitives;
using LandLedgers.World.Journeys;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Logging
{
    /// <summary>
    /// W4C: the per-instance logging operation runtime — the detailed layer
    /// behind a Logging business instance (BusinessType.Logging = 24). One
    /// instance per business.
    ///
    /// What this owns (the detailed layer):
    /// - the landing stock: felled LogLots staged at the landing, each with
    ///   full stand provenance (stand -> feller -> day);
    /// - the landing's authored staging capacity (D2D): the Canon §8.5A
    ///   bottleneck made explicit — felling above capacity is refused loudly
    ///   at the camp gate, never silently staged past the landing's limits;
    /// - rights-gated felling: RunFelling goes through the shared
    ///   LoggingStandRegistry (which owns the T1E rights gate), and is
    ///   equipment-gated on the T1E "fell-timber" codes (Canon 4.1);
    /// - haul dispatch: whole lots move from the landing into journey-planned
    ///   LogHauls; an unroutable destination or a missing wagon refuses the
    ///   haul loudly — the lot stays at the landing;
    /// - custody discipline: a lot is in exactly one place (landing, haul, or
    ///   mill stock) at every moment — never duplicated, never teleported.
    ///
    /// The timber stands themselves stay owned by the shared
    /// LoggingStandRegistry (land-attached, shared across operations) — this
    /// runtime takes the registry as a parameter and never claims stands.
    /// Skid/limb-buck/load work runs through the TTS-2 task system via the
    /// definitions in LoggingTaskDefinitions; money moves only through ledger
    /// authorities, never here.
    /// </summary>
    public sealed class LoggingOperationRuntime
    {
        private readonly List<string> diagnostics = new List<string>();
        private string businessInstanceId;
        private readonly EntityIdRegistry idRegistry;
        private readonly LogLotCustodyLedger ledger;
        private readonly List<LogLot> landingStock = new List<LogLot>(); // felled lots staged at the landing
        private readonly List<LogHaul> hauls = new List<LogHaul>(); // active + recently finished hauls

        /// <summary>
        /// D2D: how many logs the landing can stage. Canon §8.5A bottleneck
        /// doctrine — "hiring more loggers does not help if the yard or mill
        /// cannot absorb their output." Authored per operation (a real
        /// physical limit of the landing ground); defaults to unbounded so
        /// existing behavior is unchanged until an author sets it.
        /// </summary>
        public int LandingCapacityLogUnits { get; private set; } = int.MaxValue;

        /// <summary>
        /// Authors the landing's staging capacity. Negative values are
        /// refused loudly; the capacity is never silently shrunk below the
        /// logs already staged (that would strand them — refuse instead).
        /// </summary>
        public string SetLandingCapacity(int logUnits, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (logUnits < 0)
            {
                diag.Add($"LoggingOperationRuntime: LANDING CAPACITY REFUSED — {logUnits} is not a capacity.");
                return "negative-capacity";
            }
            if (logUnits < LandingLogUnits)
            {
                diag.Add($"LoggingOperationRuntime: LANDING CAPACITY REFUSED — {LandingLogUnits} logs are " +
                    $"already staged; shrinking capacity to {logUnits} would strand them. Haul logs out first.");
                return "capacity-below-staged";
            }
            LandingCapacityLogUnits = logUnits;
            diag.Add($"LoggingOperationRuntime: landing capacity set to {logUnits} logs.");
            return null;
        }

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public string BusinessInstanceId => businessInstanceId;
        public LogLotCustodyLedger Ledger => ledger;
        public IReadOnlyList<LogLot> LandingStock => landingStock;
        public IReadOnlyList<LogHaul> Hauls => hauls;

        public LoggingOperationRuntime(string businessInstanceId, EntityIdRegistry idRegistry, LogLotCustodyLedger ledger = null)
        {
            this.businessInstanceId = businessInstanceId ?? string.Empty;
            this.idRegistry = idRegistry;
            this.ledger = ledger;
            if (ledger == null)
            {
                diagnostics.Add("LoggingOperationRuntime: no custody ledger — felled lots are not registered; felling, dispatch and delivery proceed.");
            }
        }

        public int LandingLogUnits
        {
            get
            {
                int total = 0;
                foreach (var lot in landingStock) total += Math.Max(0, lot.LogUnits);
                return total;
            }
        }

        /// <summary>
        /// C-2 (OD-C4): how many logs from a lot fit one log-wagon load —
        /// the single fit authority for the 1200 kg two-horse logging
        /// wagon (LogHaul.WagonCargoCapacityKg). floor(capacity / kg-per-log),
        /// clamped to the lot's own units; 0 when not even one log fits.
        /// Used by DispatchLogHaul's split and by the daily driver, which
        /// must know the trip size before dispatching so it can pre-commit
        /// the teamster's minutes for it.
        /// </summary>
        public static int UnitsThatFitWagon(LogLot lot)
        {
            if (lot == null || lot.LogUnits <= 0) return 0;
            float kgPerLog = lot.MassKg / lot.LogUnits;
            if (kgPerLog <= 0f) return 0;
            int unitsThatFit = (int)Math.Floor(LogHaul.WagonCargoCapacityKg / kgPerLog);
            return Math.Min(unitsThatFit, lot.LogUnits);
        }

        /// <summary>
        /// Fells logs from a registered stand into the landing stock. Gate
        /// order: NX-1 equipment gate on the T1E "fell-timber" codes
        /// (Canon 4.1 — axe or crosscut saw), then the registry's rights gate
        /// (T1E refusal for non-rights-holders). Both refuse loudly; a
        /// refusal touches nothing.
        /// </summary>
        public LogLot RunFelling(
            LoggingStandRegistry registry,
            string standId,
            string fellerRightsHolderId,
            int logsWanted,
            EntityId worker,
            int dayIndex,
            List<string> diag,
            EquipmentTaskGate gate)
        {
            diag = diag ?? diagnostics;
            if (registry == null)
            {
                diag.Add("LoggingOperationRuntime: no stand registry — felling refused.");
                return null;
            }
            if (idRegistry == null)
            {
                diag.Add("LoggingOperationRuntime: no EntityIdRegistry — felling refused.");
                return null;
            }

            // NX-1A: Canon 4.1 — felling is hand-tool work (T1E fell-timber
            // codes: felling-axe OR crosscut-saw). Missing equipment authority
            // is a refusal, not permission to skip the tool requirement.
            if (gate == null)
            {
                diag.Add("LoggingOperationRuntime: FELLING REFUSED — no equipment authority was supplied to verify a felling axe or crosscut saw.");
                return null;
            }
            {
                string blocked = gate.CheckCodes(
                    new List<string>
                    {
                        EquipmentRequirementCodes.Asset("felling-axe") + "|" +
                        EquipmentRequirementCodes.Asset("crosscut-saw"),
                    },
                    "business", businessInstanceId, dayIndex, diag);
                if (blocked != null)
                {
                    diag.Add($"LoggingOperationRuntime: FELLING REFUSED — {blocked}");
                    return null;
                }
            }

            LogLot lot = registry.FellLogs(idRegistry, standId, fellerRightsHolderId,
                logsWanted, worker, dayIndex, diag);
            if (lot == null) return null; // rights gate or stand gate refused — logged there

            landingStock.Add(lot);

            // Registration producer (plan 26): register the felled lot at
            // the felling choke point, with the stand's LocationId as the
            // harvest location (note 96 ruling) and the felling holder as
            // custodian. A registration refusal is diagnostic-only: the lot
            // stays staged and felling is never undone.
            if (ledger != null)
            {
                TimberStand stand = registry.GetStand(lot.StandId);
                if (stand == null)
                {
                    diag.Add($"LoggingOperationRuntime: lot '{lot.LotId}' — no stand record for '{lot.StandId}'; the lot is staged unregistered.");
                }
                else
                {
                    string registerRefusal = ledger.RegisterLot(lot, stand.LocationId, lot.OwnerId, string.Empty, dayIndex);
                    if (registerRefusal != null)
                        diag.Add(registerRefusal);
                }
            }

            diag.Add($"LoggingOperationRuntime: felled {lot.LogUnits} logs from stand '{lot.StandId}' " +
                $"(lot {lot.LotId}) — staged at the landing with provenance.");
            return lot;
        }

        /// <summary>
        /// Dispatches ONE whole landing lot to a named sawmill via a
        /// journey-planned haul. Gate order: NX-1 equipment gate on the
        /// "haul-logs-to-mill" codes (a real wagon, Canon 4.1), then journey
        /// routing (unroutable = refused loudly). On success the lot leaves
        /// the landing and is held by the haul — never in two places.
        /// </summary>
        public LogHaul DispatchLogHaul(
            EntityId lotId,
            string millBusinessId,
            string millLocationId,
            JourneyModel journeyModel,
            string originLocationId,
            int dayIndex,
            List<string> diag,
            EquipmentTaskGate gate = null)
        {
            diag = diag ?? diagnostics;
            LogLot lot = null;
            foreach (var candidate in landingStock)
            {
                if (candidate != null && candidate.LotId == lotId)
                {
                    lot = candidate;
                    break;
                }
            }
            if (lot == null)
            {
                diag.Add($"LoggingOperationRuntime: DISPATCH REFUSED — no landing lot '{lotId}' " +
                    "at this operation's landing. Lots are never hauled from elsewhere.");
                return null;
            }

            // NX-1A: Canon 4.1 — hauling needs a usable log wagon. No wagon, no haul.
            if (gate != null)
            {
                string blocked = gate.CheckCodes(
                    new List<string> { EquipmentRequirementCodes.Asset("log-wagon") },
                    "business", businessInstanceId, dayIndex, diag);
                if (blocked != null)
                {
                    diag.Add($"LoggingOperationRuntime: DISPATCH REFUSED — {blocked}");
                    return null;
                }
            }

            // A landing lot may contain several tree-length logs. Load only
            // the largest finite portion that fits this wagon; the balance
            // remains in the landing under its original lot identity.
            LogLot cargoLot = lot;
            bool splitLot = false;
            if (lot.MassKg > LogHaul.WagonCargoCapacityKg)
            {
                int unitsThatFit = UnitsThatFitWagon(lot);
                if (unitsThatFit <= 0)
                {
                    diag.Add($"LoggingOperationRuntime: DISPATCH REFUSED — one log from lot {lot.LotId} exceeds the wagon's {LogHaul.WagonCargoCapacityKg:0.##} kg cargo limit.");
                    return null;
                }

                cargoLot = new LogLot
                {
                    LotId = lot.LotId,
                    LogUnits = unitsThatFit,
                    MassKg = (lot.MassKg / lot.LogUnits) * unitsThatFit,
                    OwnerId = lot.OwnerId,
                    StandId = lot.StandId,
                    Species = lot.Species,
                    FelledBy = lot.FelledBy,
                    FelledDayIndex = lot.FelledDayIndex,
                };
                splitLot = unitsThatFit < lot.LogUnits;
            }

            LogHaul haul = LogHaul.PlanHaul(idRegistry, cargoLot, millBusinessId,
                millLocationId, journeyModel, originLocationId, diag);
            if (haul == null) return null; // refused loudly inside PlanHaul — lot stays at the landing

            landingStock.Remove(lot);
            if (splitLot)
            {
                int hauledUnits = cargoLot.LogUnits;
                float hauledMass = cargoLot.MassKg;
                lot.LogUnits -= hauledUnits;
                lot.MassKg -= hauledMass;
                haul.Lot.LotId = idRegistry.Allocate(EntityKind.Lot);
                landingStock.Add(lot);
                diag.Add($"LoggingOperationRuntime: lot {haul.Lot.LotId} carries {hauledUnits} log unit(s) ({hauledMass:0.##} kg); {lot.LogUnits} unit(s) remain at the landing within the wagon's {LogHaul.WagonCargoCapacityKg:0.##} kg limit.");
            }
            hauls.Add(haul);

            // Registration producer (plan 26): drive Collect + Load for
            // the dispatched cargo. The cargo carries a fresh id when the
            // lot was split, so register it first under that id, reading
            // the harvest location from the original record. A refusal is
            // diagnostic-only: the haul still dispatches.
            if (ledger != null)
            {
                string cargoLotId = haul.Lot.LotId.ToString();
                string originalLotId = lot.LotId.ToString();
                if (!string.Equals(cargoLotId, originalLotId, StringComparison.Ordinal))
                {
                    LogLotCustodyRecord original = ledger.GetLot(originalLotId);
                    string harvestLocationId = original == null ? null : original.HarvestLocationId;
                    if (string.IsNullOrWhiteSpace(harvestLocationId))
                    {
                        diag.Add($"LoggingOperationRuntime: lot '{cargoLotId}' — no harvest location on record for the original lot; the cargo lot is dispatched unregistered.");
                    }
                    else
                    {
                        string cargoRefusal = ledger.RegisterLot(haul.Lot, harvestLocationId, haul.Lot.OwnerId, string.Empty, dayIndex);
                        if (cargoRefusal != null)
                            diag.Add(cargoRefusal);
                    }
                }
                string collectRefusal;
                if (!ledger.CollectLot(cargoLotId, businessInstanceId, dayIndex, out collectRefusal) && collectRefusal != null)
                    diag.Add(collectRefusal);
                string loadRefusal = ledger.LoadLot(cargoLotId, businessInstanceId, dayIndex);
                if (loadRefusal != null)
                    diag.Add(loadRefusal);
            }

            diag.Add($"LoggingOperationRuntime: lot {lot.LotId} dispatched — the haul holds it now, " +
                "not the landing.");
            return haul;
        }

        /// <summary>
        /// Advances all active hauls by whole minutes (Loading -> InTransit -> Arrived).
        /// Registration producer (plan 26): records the Loading -> InTransit
        /// flip in the custody ledger. The day is an optional parameter
        /// because neither the runtime nor the haul holds one; -1 is the
        /// record's unset value. A refusal is diagnostic-only — the wagon
        /// is already on the road.
        /// </summary>
        public void AdvanceHauls(int minutes, List<string> diag, int dayIndex = -1)
        {
            diag = diag ?? diagnostics;
            foreach (var haul in hauls)
            {
                if (haul == null || !haul.IsActive) continue;
                LogHaulStatus before = haul.Status;
                haul.AdvanceMinutes(minutes, diag);
                if (ledger != null && before == LogHaulStatus.Loading && haul.Status != LogHaulStatus.Loading)
                {
                    string refusal = ledger.MarkInTransit(haul.Lot.LotId.ToString(), businessInstanceId, dayIndex);
                    if (refusal != null)
                        diag.Add(refusal);
                }
            }
        }

        /// <summary>
        /// Delivers every arrived haul into the mill's log stock — custody
        /// transfers through the W4A provenance gate (anonymous lots refused
        /// loudly; refused lots stay on the wagon, never dropped).
        /// Registration producer (plan 26): records Delivered in the custody
        /// ledger, from the runtime side, only after ReceiveLogLot succeeds
        /// — never before, never on refusal. The day is an optional
        /// parameter; the live daily driver passes its day index.
        /// </summary>
        public void DeliverArrivedHauls(SawmillLogStock millStock, List<string> diag, int dayIndex = -1)
        {
            diag = diag ?? diagnostics;
            foreach (var haul in hauls)
            {
                if (haul == null) continue;
                if (haul.Status != LogHaulStatus.Arrived) continue;
                string lotId = haul.Lot == null ? null : haul.Lot.LotId.ToString();
                string deliveryRefusal = haul.DeliverToMill(millStock, diag);
                if (deliveryRefusal == null && haul.Status == LogHaulStatus.Delivered && ledger != null)
                {
                    string refusal = ledger.MarkDelivered(lotId, haul.MillBusinessId, dayIndex);
                    if (refusal != null)
                        diag.Add(refusal);
                }
            }
        }

        /// <summary>W4C save contract: lives inside the owning runtime class.</summary>
        [Serializable]
        public sealed class LoggingOperationSaveDto
        {
            public string BusinessInstanceId = string.Empty;
            public int LandingCapacityLogUnits = int.MaxValue; // D2D: authored landing capacity
            public List<LogLot> LandingStock = new List<LogLot>();
            public List<LogHaul.LogHaulSaveDto> Hauls = new List<LogHaul.LogHaulSaveDto>();
        }

        public LoggingOperationSaveDto CaptureSaveDto()
        {
            var dto = new LoggingOperationSaveDto
            {
                BusinessInstanceId = businessInstanceId,
                LandingCapacityLogUnits = LandingCapacityLogUnits,
            };
            foreach (var lot in landingStock)
            {
                if (lot == null) continue;
                dto.LandingStock.Add(new LogLot
                {
                    LotId = lot.LotId,
                    LogUnits = lot.LogUnits,
                    MassKg = lot.MassKg,
                    OwnerId = lot.OwnerId,
                    StandId = lot.StandId,
                    Species = lot.Species,
                    FelledBy = lot.FelledBy,
                    FelledDayIndex = lot.FelledDayIndex,
                });
            }
            foreach (var haul in hauls)
            {
                if (haul == null) continue;
                dto.Hauls.Add(haul.CaptureSaveDto());
            }
            return dto;
        }

        public void LoadFromSaveDto(LoggingOperationSaveDto dto)
        {
            landingStock.Clear();
            hauls.Clear();
            if (dto == null) return;
            businessInstanceId = dto.BusinessInstanceId ?? string.Empty;
            LandingCapacityLogUnits = dto.LandingCapacityLogUnits < 0 ? int.MaxValue : dto.LandingCapacityLogUnits;
            if (dto.LandingStock != null)
            {
                foreach (var lot in dto.LandingStock)
                {
                    if (lot == null) continue;
                    landingStock.Add(lot);
                }
            }
            if (dto.Hauls != null)
            {
                foreach (var haulDto in dto.Hauls)
                {
                    if (haulDto == null) continue;
                    hauls.Add(LogHaul.FromSaveDto(haulDto));
                }
            }
        }
    }
}
