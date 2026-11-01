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
        public IReadOnlyList<LogLot> LandingStock => landingStock;
        public IReadOnlyList<LogHaul> Hauls => hauls;

        public LoggingOperationRuntime(string businessInstanceId, EntityIdRegistry idRegistry)
        {
            this.businessInstanceId = businessInstanceId ?? string.Empty;
            this.idRegistry = idRegistry;
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
            EquipmentTaskGate gate = null)
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
            // codes: felling-axe OR crosscut-saw). No usable tool, no felling.
            if (gate != null)
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

            LogHaul haul = LogHaul.PlanHaul(idRegistry, lot, millBusinessId,
                millLocationId, journeyModel, originLocationId, diag);
            if (haul == null) return null; // refused loudly inside PlanHaul — lot stays at the landing

            landingStock.Remove(lot);
            hauls.Add(haul);
            diag.Add($"LoggingOperationRuntime: lot {lot.LotId} dispatched — the haul holds it now, " +
                "not the landing.");
            return haul;
        }

        /// <summary>Advances all active hauls by whole minutes (Loading -> InTransit -> Arrived).</summary>
        public void AdvanceHauls(int minutes, List<string> diag)
        {
            diag = diag ?? diagnostics;
            foreach (var haul in hauls)
            {
                if (haul != null && haul.IsActive)
                    haul.AdvanceMinutes(minutes, diag);
            }
        }

        /// <summary>
        /// Delivers every arrived haul into the mill's log stock — custody
        /// transfers through the W4A provenance gate (anonymous lots refused
        /// loudly; refused lots stay on the wagon, never dropped).
        /// </summary>
        public void DeliverArrivedHauls(SawmillLogStock millStock, List<string> diag)
        {
            diag = diag ?? diagnostics;
            foreach (var haul in hauls)
            {
                if (haul == null) continue;
                if (haul.Status != LogHaulStatus.Arrived) continue;
                haul.DeliverToMill(millStock, diag);
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
