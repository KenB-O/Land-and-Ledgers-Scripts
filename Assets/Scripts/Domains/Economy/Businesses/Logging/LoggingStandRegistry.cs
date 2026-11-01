using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Farming.Timber;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Logging
{
    /// <summary>
    /// W4C: one timber-rights grant — the explicit, recorded authority to fell
    /// on a named stand. Rights are never assumed and never silent: every
    /// grant names the stand, the holder, the day, and who conveyed the
    /// rights (canon property doctrine; cf. T1E TimberHarvest's
    /// rights-holder refusal — timber theft is a claim, not a harvest).
    /// </summary>
    [Serializable]
    public sealed class TimberRightsGrant
    {
        public string StandId = string.Empty;
        public string HolderId = string.Empty; // business/household/town id that may fell here
        public int GrantDayIndex;
        public string GrantedBy = string.Empty; // who conveyed the rights (seller, town authority, prior owner)
        public string Notes = string.Empty; // e.g. "purchased with the north-forty parcel", "transfer from ..."

        public TimberRightsGrant() { }
    }

    /// <summary>
    /// W4C: the timber-stand registry — the felling-rights authority below the
    /// sawmill (Canon §8.5A causal chain: standing timber -> felling -> ...).
    ///
    /// What this owns:
    /// - the authored timber stands (T1E TimberStand records, keyed by
    ///   StandId — stands are authored, never conjured);
    /// - the explicit timber-rights grants that make felling legitimate;
    /// - the felling gate: ALL felling routes through FellLogs, which refuses
    ///   loudly for unknown stands, rights violations, and exhausted stands
    ///   (the rights check itself stays owned by T1E TimberHarvest — no
    ///   rewrite, no second gate);
    /// - explicit regrowth: stands never regrow on their own. ApplyRegrowth
    ///   takes a caller-supplied rate — the calibration is an open design
    ///   question for Kennedy, so no rate is guessed here (Canon §7.4C:
    ///   regrowth is slow and explicit, never assumed).
    ///
    /// Produced LogLots always carry StandId provenance, which is exactly
    /// what the W4A sawmill intake requires (anonymous logs are refused
    /// loudly by SawmillLogStock.ReceiveLogLot).
    /// </summary>
    public sealed class LoggingStandRegistry
    {
        private readonly Dictionary<string, TimberStand> stands =
            new Dictionary<string, TimberStand>(StringComparer.OrdinalIgnoreCase);
        private readonly List<TimberRightsGrant> rightsGrants = new List<TimberRightsGrant>();
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<TimberRightsGrant> RightsGrants => rightsGrants;
        public int StandCount => stands.Count;

        /// <summary>Registers an authored stand. Duplicates and blank ids are refused loudly.</summary>
        public string RegisterStand(TimberStand stand)
        {
            if (stand == null)
                return "LoggingStandRegistry.RegisterStand: no stand supplied — stands are authored, never conjured.";
            if (string.IsNullOrWhiteSpace(stand.StandId))
                return "LoggingStandRegistry.RegisterStand: a timber stand needs a stable StandId.";
            if (stands.ContainsKey(stand.StandId))
                return $"LoggingStandRegistry.RegisterStand: stand '{stand.StandId}' is already registered — stand ids are never reused.";

            stands[stand.StandId] = stand;
            diagnostics.Add($"LoggingStandRegistry: registered stand '{stand.StandId}' " +
                $"({stand.DisplayName}, {stand.Species}, {stand.StandingTimberUnits} logs standing).");
            return null;
        }

        public TimberStand GetStand(string standId)
        {
            if (string.IsNullOrWhiteSpace(standId)) return null;
            TimberStand stand;
            return stands.TryGetValue(standId, out stand) ? stand : null;
        }

        /// <summary>The current rights holder for a stand (the T1E gate's source of truth).</summary>
        public string CurrentRightsHolder(string standId)
        {
            TimberStand stand = GetStand(standId);
            return stand == null ? string.Empty : stand.TimberRightsHolderId ?? string.Empty;
        }

        /// <summary>
        /// Records an explicit first grant of felling rights on a stand.
        /// The grantor must be named (provenance); a stand that already has
        /// a holder needs TransferTimberRights instead — silent overwrites
        /// are refused.
        /// </summary>
        public string GrantTimberRights(string standId, string holderId, int grantDayIndex,
            string grantedBy, string notes = null)
        {
            TimberStand stand = GetStand(standId);
            if (stand == null)
                return $"LoggingStandRegistry.GrantTimberRights: unknown stand '{standId}' — rights cannot be granted on land that isn't registered.";
            if (string.IsNullOrWhiteSpace(holderId))
                return $"LoggingStandRegistry.GrantTimberRights: a rights holder must be named for stand '{standId}' — rights are never anonymous.";
            if (string.IsNullOrWhiteSpace(grantedBy))
                return $"LoggingStandRegistry.GrantTimberRights: the grantor must be named for stand '{standId}' — rights come from someone.";
            if (!string.IsNullOrWhiteSpace(stand.TimberRightsHolderId))
                return $"LoggingStandRegistry.GrantTimberRights: stand '{standId}' already has a rights holder " +
                    $"('{stand.TimberRightsHolderId}') — use TransferTimberRights for a conveyance.";

            stand.TimberRightsHolderId = holderId;
            rightsGrants.Add(new TimberRightsGrant
            {
                StandId = standId,
                HolderId = holderId,
                GrantDayIndex = grantDayIndex,
                GrantedBy = grantedBy,
                Notes = notes ?? string.Empty,
            });
            diagnostics.Add($"LoggingStandRegistry: felling rights on stand '{standId}' granted to '{holderId}' " +
                $"by '{grantedBy}' (day {grantDayIndex}).");
            return null;
        }

        /// <summary>
        /// Records an explicit conveyance of felling rights to a new holder.
        /// The old holder's tenure ends in the record, never silently.
        /// </summary>
        public string TransferTimberRights(string standId, string newHolderId, int dayIndex, string notes = null)
        {
            TimberStand stand = GetStand(standId);
            if (stand == null)
                return $"LoggingStandRegistry.TransferTimberRights: unknown stand '{standId}'.";
            if (string.IsNullOrWhiteSpace(stand.TimberRightsHolderId))
                return $"LoggingStandRegistry.TransferTimberRights: stand '{standId}' has no current rights holder — use GrantTimberRights for a first grant.";
            if (string.IsNullOrWhiteSpace(newHolderId))
                return $"LoggingStandRegistry.TransferTimberRights: the new rights holder must be named.";
            if (string.Equals(stand.TimberRightsHolderId, newHolderId, StringComparison.Ordinal))
                return $"LoggingStandRegistry.TransferTimberRights: '{newHolderId}' already holds the rights on stand '{standId}' — nothing to transfer.";

            string oldHolder = stand.TimberRightsHolderId;
            stand.TimberRightsHolderId = newHolderId;
            rightsGrants.Add(new TimberRightsGrant
            {
                StandId = standId,
                HolderId = newHolderId,
                GrantDayIndex = dayIndex,
                GrantedBy = oldHolder,
                Notes = string.IsNullOrWhiteSpace(notes)
                    ? $"transfer from '{oldHolder}'"
                    : notes,
            });
            diagnostics.Add($"LoggingStandRegistry: felling rights on stand '{standId}' transferred from " +
                $"'{oldHolder}' to '{newHolderId}' (day {dayIndex}).");
            return null;
        }

        /// <summary>
        /// W4C: the felling gate. ALL felling routes through here — unknown
        /// stands and rights violations are refused LOUDLY, never silently.
        /// The rights check itself is delegated to the T1E authority
        /// (TimberHarvest.FellLogs), which owns the refusal text.
        /// </summary>
        public LogLot FellLogs(
            EntityIdRegistry idRegistry,
            string standId,
            string fellerRightsHolderId,
            int logsWanted,
            EntityId worker,
            int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            TimberStand stand = GetStand(standId);
            if (stand == null)
            {
                diag.Add($"LoggingStandRegistry: felling refused — unknown stand '{standId}'. " +
                    "Logs must come from a registered timber stand.");
                return null;
            }

            return TimberHarvest.FellLogs(idRegistry, stand, fellerRightsHolderId,
                logsWanted, worker, dayIndex, diag);
        }

        /// <summary>
        /// Explicit stand regrowth — caller supplies the rate. Never called
        /// with an invented calibration: timber regrowth pacing is an open
        /// design question for Kennedy (Canon §7.4C: regrowth is slow and
        /// explicit, never assumed). Passing 0 is a no-op, recorded.
        /// </summary>
        public void ApplyRegrowth(int unitsPerStand, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            int applied = Math.Max(0, unitsPerStand);
            foreach (var stand in stands.Values)
            {
                if (stand == null) continue;
                stand.StandingTimberUnits += applied;
            }
            diag.Add($"LoggingStandRegistry: explicit regrowth applied — +{applied} standing-log units " +
                $"per stand (day {dayIndex}). Rate supplied by the caller; nothing regrows on its own.");
        }

        /// <summary>W4C save contract: lives inside the owning registry class.</summary>
        [Serializable]
        public sealed class LoggingStandRegistrySaveDto
        {
            public List<TimberStand> Stands = new List<TimberStand>();
            public List<TimberRightsGrant> RightsGrants = new List<TimberRightsGrant>();
        }

        public LoggingStandRegistrySaveDto CaptureSaveDto()
        {
            var dto = new LoggingStandRegistrySaveDto();
            foreach (var stand in stands.Values)
            {
                if (stand == null) continue;
                dto.Stands.Add(new TimberStand
                {
                    StandId = stand.StandId,
                    DisplayName = stand.DisplayName,
                    LocationId = stand.LocationId,
                    TimberRightsHolderId = stand.TimberRightsHolderId,
                    Species = stand.Species,
                    StandingTimberUnits = stand.StandingTimberUnits,
                    LastFelledDayIndex = stand.LastFelledDayIndex,
                });
            }
            foreach (var grant in rightsGrants)
            {
                if (grant == null) continue;
                dto.RightsGrants.Add(new TimberRightsGrant
                {
                    StandId = grant.StandId,
                    HolderId = grant.HolderId,
                    GrantDayIndex = grant.GrantDayIndex,
                    GrantedBy = grant.GrantedBy,
                    Notes = grant.Notes,
                });
            }
            return dto;
        }

        public void LoadFromSaveDto(LoggingStandRegistrySaveDto dto)
        {
            stands.Clear();
            rightsGrants.Clear();
            if (dto == null) return;
            if (dto.Stands != null)
            {
                foreach (var stand in dto.Stands)
                {
                    if (stand == null || string.IsNullOrWhiteSpace(stand.StandId)) continue;
                    stands[stand.StandId] = stand;
                }
            }
            if (dto.RightsGrants != null)
            {
                foreach (var grant in dto.RightsGrants)
                {
                    if (grant == null) continue;
                    rightsGrants.Add(grant);
                }
            }
        }
    }
}
