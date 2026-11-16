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
    /// - explicit regrowth: stands never regrow on their own. DriveRegrowth
    ///   (DEF-072, Decision 15) applies the calibrated age-class model —
    ///   species maturity years, site quality, seed source, carrying
    ///   capacity — one year-step at a time, driven once per day from the
    ///   daily simulation boundary. New growth is young, never instantly
    ///   merchantable; a clear-cut stand with no seed source establishes
    ///   nothing. See TimberSpecies for the maturity table.
    ///
    /// Produced LogLots always carry StandId provenance, which is exactly
    /// what the W4A sawmill intake requires (anonymous logs are refused
    /// loudly by SawmillLogStock.ReceiveLogLot).
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

        /// <summary>
        /// Note-112 Finding 1 follow-up (plan 29, note 118 C2): the trigger
        /// for re-applying the felling config after the Pass 9 restore. The
        /// restored registry is empty AND stands were authored in config —
        /// an empty registry with no authored stands is the correct state,
        /// not a wipe, so it must not trigger. The bootstrapper reads the
        /// authored count from config (the registry must not); this method
        /// owns only the registry half of the decision.
        /// </summary>
        public bool NeedsFellingConfigReapply(int authoredStandCount) =>
            StandCount == 0 && authoredStandCount > 0;

        /// <summary>
        /// DEF-072 (Decision 15): regrowth defaults for a stand whose
        /// fields were never authored — capacity is the standing stock
        /// at registration (an authored stand starts at its capacity),
        /// site quality is 1 (productive site). Called at registration
        /// and at load, so old saves and direct construction both get
        /// valid state.
        /// </summary>
        private static void NormalizeStand(TimberStand stand)
        {
            if (stand == null) return;
            if (stand.CarryingCapacityUnits <= 0)
                stand.CarryingCapacityUnits = Math.Max(0, stand.StandingTimberUnits);
            if (stand.SiteQuality01 <= 0f)
                stand.SiteQuality01 = 1f;
            else if (stand.SiteQuality01 > 1f)
                stand.SiteQuality01 = 1f;
            if (stand.YoungCohorts == null)
                stand.YoungCohorts = new List<RegrowthCohort>();
            if (stand.MatureAccrual < 0f)
                stand.MatureAccrual = 0f;
        }

        /// <summary>Registers an authored stand. Duplicates and blank ids are refused loudly.</summary>
        public string RegisterStand(TimberStand stand)
        {
            if (stand == null)
                return "LoggingStandRegistry.RegisterStand: no stand supplied — stands are authored, never conjured.";
            if (string.IsNullOrWhiteSpace(stand.StandId))
                return "LoggingStandRegistry.RegisterStand: a timber stand needs a stable StandId.";
            if (stands.ContainsKey(stand.StandId))
                return $"LoggingStandRegistry.RegisterStand: stand '{stand.StandId}' is already registered — stand ids are never reused.";

            NormalizeStand(stand);
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
        /// DEF-072 (Decision 15): calibrated stand regrowth, driven once
        /// per day from the daily simulation boundary. Age-class cohorts:
        /// new growth is young, never instantly merchantable; a cohort
        /// becomes merchantable MaturityYears after establishment
        /// (TimberSpecies table — white pine 80 years is the decided
        /// baseline). Establishment each year is (capacity / maturity
        /// years) x seed fraction x site quality, where the seed
        /// fraction is the remaining mature stock — the mature trees
        /// left standing ARE the seed trees, so a clear-cut stand
        /// establishes nothing and planting is the only return (no
        /// planting system exists yet). Establishment is capped so
        /// mature + young + pending accrual never exceeds capacity
        /// (competition — a full canopy admits no new seedlings).
        /// Year-stepped: each year boundary in (last driven day, today]
        /// is processed in order, so a long jump (Continue after a long
        /// absence, or a test driving 80 years at once) accrues and
        /// matures correctly. A same-day re-call is a no-op (idempotent);
        /// the first drive starts the clock at day 0 (stands are
        /// authored at scenario start). One diagnostic line per stand
        /// per drive, only when establishment or maturation happened —
        /// silent on days with no year boundary crossed.
        /// </summary>
        public void DriveRegrowth(int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            foreach (var stand in stands.Values)
            {
                if (stand == null) continue;
                NormalizeStand(stand);
                if (stand.CarryingCapacityUnits <= 0) continue; // degenerate stand: no capacity, no regrowth
                if (dayIndex <= stand.LastRegrowthDayIndex) continue; // already driven today (or earlier): idempotent
                int startDay = stand.LastRegrowthDayIndex < 0 ? 0 : stand.LastRegrowthDayIndex;
                int maturityYears = TimberSpecies.MaturityYears(stand.Species, diag);
                int maturityDays = maturityYears * 365;
                int firstYearEnd = ((startDay / 365) + 1) * 365;
                for (int yearEnd = firstYearEnd; yearEnd <= dayIndex; yearEnd += 365)
                {
                    DriveRegrowthYear(stand, yearEnd, maturityYears, maturityDays, diag);
                }
                stand.LastRegrowthDayIndex = dayIndex;
            }
        }

        /// <summary>
        /// DEF-072: one regrowth year on one stand. Matures due cohorts
        /// first (so the seed fraction for this year's establishment
        /// counts the newly matured trees), then establishes this
        /// year's cohort. Returns nothing; diagnostics carry the story.
        /// </summary>
        private static void DriveRegrowthYear(TimberStand stand, int yearEnd,
            int maturityYears, int maturityDays, List<string> diag)
        {
            float matureBefore = stand.StandingTimberUnits;
            float youngBefore = stand.YoungGrowthUnits;

            // 1. Mature due cohorts (established MaturityYears ago or more).
            float maturing = 0f;
            for (int i = stand.YoungCohorts.Count - 1; i >= 0; i--)
            {
                RegrowthCohort cohort = stand.YoungCohorts[i];
                if (cohort == null) { stand.YoungCohorts.RemoveAt(i); continue; }
                if (yearEnd - cohort.EstablishDayIndex >= maturityDays)
                {
                    maturing += Math.Max(0f, cohort.Units);
                    stand.YoungCohorts.RemoveAt(i);
                }
            }
            int maturedWhole = 0;
            int maturedDiscarded = 0;
            if (maturing > 0f)
            {
                stand.MatureAccrual += maturing;
                int whole = (int)Math.Floor(stand.MatureAccrual);
                if (whole > 0)
                {
                    int room = stand.CarryingCapacityUnits - stand.StandingTimberUnits;
                    int added = Math.Min(whole, Math.Max(0, room));
                    stand.StandingTimberUnits += added;
                    stand.MatureAccrual -= whole;
                    maturedWhole = added;
                    maturedDiscarded = whole - added;
                }
            }

            // 2. Establish this year's cohort. Seed trees are the mature
            // trees left standing; a clear-cut stand has no seed source.
            float established = 0f;
            float seedSource01 = stand.CarryingCapacityUnits > 0
                ? (float)stand.StandingTimberUnits / stand.CarryingCapacityUnits
                : 0f;
            if (seedSource01 > 0f)
            {
                float annual = (stand.CarryingCapacityUnits / (float)maturityYears)
                    * seedSource01 * stand.SiteQuality01;
                if (annual > 0f)
                {
                    // The deficit MUST count MatureAccrual: maturation
                    // moves young into the accrual before the units enter
                    // the stand, and without counting it the establishment
                    // step would refill the gap maturation just opened and
                    // the stand would overfill past capacity.
                    float deficit = stand.CarryingCapacityUnits
                        - stand.StandingTimberUnits - stand.YoungGrowthUnits - stand.MatureAccrual;
                    established = Math.Min(annual, Math.Max(0f, deficit));
                    if (established > 0f)
                    {
                        RegrowthCohort cohort = null;
                        foreach (var existing in stand.YoungCohorts)
                        {
                            if (existing != null && existing.EstablishDayIndex == yearEnd) { cohort = existing; break; }
                        }
                        if (cohort == null)
                        {
                            cohort = new RegrowthCohort { EstablishDayIndex = yearEnd, Units = 0f };
                            stand.YoungCohorts.Add(cohort);
                        }
                        cohort.Units += established;
                    }
                }
            }

            if (maturedWhole > 0 || established > 0f || maturedDiscarded > 0)
            {
                string line = $"LoggingStandRegistry: stand '{stand.StandId}' regrowth year ending day {yearEnd} — " +
                    $"mature {matureBefore} -> {stand.StandingTimberUnits}, young {youngBefore:0.##} -> {stand.YoungGrowthUnits:0.##}" +
                    (maturedWhole > 0 ? $", matured {maturedWhole}" : string.Empty) +
                    (established > 0f ? $", established {established:0.##}" : string.Empty) +
                    (maturedDiscarded > 0 ? $", {maturedDiscarded} matured units lost to competition (capacity)" : string.Empty) +
                    $" (last felled day {stand.LastFelledDayIndex}).";
                diag.Add(line);
            }
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
                var copiedCohorts = new List<RegrowthCohort>();
                if (stand.YoungCohorts != null)
                {
                    foreach (var cohort in stand.YoungCohorts)
                    {
                        if (cohort == null) continue;
                        copiedCohorts.Add(new RegrowthCohort
                        {
                            EstablishDayIndex = cohort.EstablishDayIndex,
                            Units = cohort.Units,
                        });
                    }
                }
                dto.Stands.Add(new TimberStand
                {
                    StandId = stand.StandId,
                    DisplayName = stand.DisplayName,
                    LocationId = stand.LocationId,
                    TimberRightsHolderId = stand.TimberRightsHolderId,
                    Species = stand.Species,
                    KilogramsPerLog = stand.KilogramsPerLog,
                    MilesToMill = stand.MilesToMill,
                    StandingTimberUnits = stand.StandingTimberUnits,
                    LastFelledDayIndex = stand.LastFelledDayIndex,
                    // DEF-072: regrowth state — every field must be copied
                    // here or it silently drops on save.
                    CarryingCapacityUnits = stand.CarryingCapacityUnits,
                    SiteQuality01 = stand.SiteQuality01,
                    LastRegrowthDayIndex = stand.LastRegrowthDayIndex,
                    MatureAccrual = stand.MatureAccrual,
                    YoungCohorts = copiedCohorts,
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
                    // DEF-072: old saves carry none of the regrowth fields
                    // (capacity 0, quality 0, cohorts null) — normalize to
                    // the same defaults registration applies.
                    NormalizeStand(stand);
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
