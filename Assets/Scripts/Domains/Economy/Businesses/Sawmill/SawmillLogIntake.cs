using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Farming.Timber;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Sawmill
{
    /// <summary>
    /// W4A: the sawmill's log yard — intake with provenance validation. Log
    /// lots arrive from the timber chain (LogLot, T1E); every lot must name
    /// its timber stand / felling source (Canon §8.5A causal chain:
    /// standing timber -> felling -> ... -> mill intake). The W4C logging
    /// package later adds felling; until then, documented lots are accepted
    /// and ANONYMOUS logs are refused loudly — a lot with no stand is a
    /// claim, not inventory (canon property doctrine, cf. TimberHarvest's
    /// rights-holder refusal).
    ///
    /// FIFO: the oldest log lot is sawn first so stock ages honestly.
    /// </summary>
    public sealed class SawmillLogStock
    {
        private readonly List<string> diagnostics = new List<string>();
        private readonly List<LogLot> lots = new List<LogLot>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<LogLot> Lots => lots;

        public int TotalLogUnits
        {
            get
            {
                int total = 0;
                foreach (var lot in lots) total += Math.Max(0, lot.LogUnits);
                return total;
            }
        }

        /// <summary>Receives a log lot with provenance. Returns the refusal, or null.</summary>
        public string ReceiveLogLot(LogLot lot, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (lot == null)
                return "SawmillLogStock.ReceiveLogLot: no lot offered — logs are not conjured.";
            if (lot.LotId == EntityId.Invalid)
                return "SawmillLogStock.ReceiveLogLot: a log lot needs an EntityId — anonymous stock is refused.";
            if (lot.LogUnits <= 0)
                return "SawmillLogStock.ReceiveLogLot: a log lot needs positive log units.";
            if (string.IsNullOrWhiteSpace(lot.StandId))
            {
                diag.Add($"SawmillLogStock: REFUSED log lot {lot.LotId} — no timber stand named. "
                    + "Anonymous logs are refused loudly: every log must name its timber stand / felling source "
                    + "(W4A provenance gate; felling arrives with W4C).");
                return "SawmillLogStock.ReceiveLogLot: no timber stand named — anonymous logs refused.";
            }

            lots.Add(lot);
            diag.Add($"SawmillLogStock: received {lot.LogUnits} logs (lot {lot.LotId}) from stand '{lot.StandId}'"
                + (string.IsNullOrWhiteSpace(lot.Species) ? "." : $" ({lot.Species})."));
            return null;
        }

        /// <summary>
        /// Saws logs from the oldest lot (FIFO). Returns the source lot when
        /// it could cover the run, or null with a loud refusal — partial
        /// runs are refused rather than blending lots, so every lumber lot
        /// traces to exactly one log lot and one stand.
        /// </summary>
        public LogLot TryTakeOldestLot(int logsWanted, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (logsWanted <= 0)
            {
                diag.Add("SawmillLogStock: no logs requested.");
                return null;
            }

            LogLot oldest = null;
            foreach (var lot in lots)
            {
                if (lot.LogUnits <= 0) continue;
                if (oldest == null || lot.FelledDayIndex < oldest.FelledDayIndex) oldest = lot;
            }

            if (oldest == null)
            {
                diag.Add("SawmillLogStock: log yard is empty — nothing sawn, no stock invented.");
                return null;
            }

            if (oldest.LogUnits < logsWanted)
            {
                diag.Add($"SawmillLogStock: REFUSED run of {logsWanted} logs — oldest lot {oldest.LotId} holds only "
                    + $"{oldest.LogUnits}. Provenance stays one-lot-per-lumber-lot: request the remainder separately.");
                return null;
            }

            oldest.LogUnits -= logsWanted;
            if (oldest.LogUnits <= 0) lots.Remove(oldest);
            return oldest;
        }

        /// <summary>W4A save contract: lives inside the owning stock class.</summary>
        [Serializable]
        public sealed class SawmillLogStockSaveDto
        {
            public List<LogLot> Lots = new List<LogLot>();
        }

        public SawmillLogStockSaveDto CaptureSaveDto()
        {
            var dto = new SawmillLogStockSaveDto();
            foreach (var lot in lots)
            {
                dto.Lots.Add(new LogLot
                {
                    LotId = lot.LotId,
                    LogUnits = lot.LogUnits,
                    StandId = lot.StandId,
                    Species = lot.Species,
                    FelledBy = lot.FelledBy,
                    FelledDayIndex = lot.FelledDayIndex,
                });
            }
            return dto;
        }

        public void LoadFromSaveDto(SawmillLogStockSaveDto dto)
        {
            lots.Clear();
            if (dto == null) return;
            foreach (var lot in dto.Lots)
            {
                if (lot == null) continue;
                lots.Add(lot);
            }
        }
    }
}
