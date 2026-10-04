using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Equipment;
using LandLedgers.Economy.Farming.Timber;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Businesses.Logging
{
    /// <summary>
    /// D2D: the canon crew-role vocabulary for a logging camp (Canon 4.1 tool
    /// list: "Logger / feller" and "Skid / log-haul worker"; Tech X:
    /// "Loggers, haulers, sawmill crews").
    ///
    /// Roles are free-form strings, not an enum, ON PURPOSE: the canon names
    /// no camp cook, and no cooking mechanics are invented here — but a cook
    /// (or any other role Kennedy staffs) can be assigned to the roster
    /// without a code change. Roles with no modeled mechanics are carried as
    /// data and consume rations like everyone else.
    /// </summary>
    public static class LoggingCampRoles
    {
        /// <summary>Fells timber (counts toward two-person felling teams).</summary>
        public const string Logger = "logger";
        /// <summary>Alias of Logger — Canon 4.1's "Logger / feller".</summary>
        public const string Feller = "feller";
        /// <summary>Skids logs stump-to-landing, loads wagons (no felling mechanics modeled).</summary>
        public const string SkidWorker = "skid-worker";
    }

    /// <summary>
    /// D2D: authored camp calibration. Every value is an explicit parameter
    /// Kennedy can adjust — none of them is presented as canon or historical
    /// fact (canon: generated values remain calibration unless explicitly
    /// locked).
    /// </summary>
    [Serializable]
    public sealed class LoggingCampParameters
    {
        /// <summary>Provision units consumed per crew member per camp day. Default 1.0 (unit definition).</summary>
        public float ProvisionsPerHeadPerDay = 1f;
        /// <summary>Minutes of the work-light day lost to camp-to-stump travel (each way is NOT doubled — author the round trip).</summary>
        public int CampToStumpTravelMinutesPerDay = 0;
        /// <summary>Bunkhouse capacity in heads. Negative = not authored — no strain is computed.</summary>
        public int BunkCapacity = -1;
        /// <summary>Whole days the camp cannot fell after a relocation (strike, move, re-pitch). Default 0 — open calibration.</summary>
        public int CampMoveDownDays = 0;

        public LoggingCampParameters() { }
    }

    /// <summary>D2D: one crew assignment on the camp roster.</summary>
    [Serializable]
    public sealed class LoggingCampCrewEntry
    {
        public EntityId Worker = EntityId.Invalid;
        public string Role = string.Empty; // LoggingCampRoles value, or any authored role string
        public int DayJoined;

        public LoggingCampCrewEntry() { }
    }

    /// <summary>D2D: the append-only record of one camp working day.</summary>
    [Serializable]
    public sealed class LoggingCampWorkDay
    {
        public int DayIndex;
        public int MonthIndex;
        public int CrewHeadcount;
        public int LoggerCount;
        public int WorkableMinutes;
        public int LogsWanted;
        public int LogsFelled;
        public float ProvisionsConsumed;
        /// <summary>
        /// Which bound won when LogsFelled &lt; LogsWanted: "crew", "daylight",
        /// "stand", "landing", or "full-request" when everything asked was felled.
        /// </summary>
        public string LimitReason = string.Empty;

        public LoggingCampWorkDay() { }
    }

    /// <summary>
    /// D2D: the logging camp — the worksite BELOW the logging operation on
    /// the Canon §8.5A causal chain (standing timber -> felling -> ...). The
    /// operation (LoggingOperationRuntime) owns the landing stock and the
    /// haul dispatch; the camp owns the human side the width pass left thin:
    ///
    /// - the crew roster (Canon 4.1 roles: logger/feller, skid/log-haul
    ///   worker; Tech X: loggers, haulers) attached to one stand at a time;
    /// - the daylight-limited working day (Canon §8.5: the practical day is
    ///   limited by usable light, travel, physical conditions and available
    ///   timber — never a universal shift);
    /// - the camp supply stock: provisions arrive only by explicit delivery
    ///   with named supplier provenance (remote-camp supply is a canon
    ///   merchant function); an unfed crew is refused LOUDLY and never
    ///   auto-supplied;
    /// - the camp lifecycle: strike when the stand is cut out, relocate to a
    ///   new registered stand with parameterized down days;
    /// - the landing bottleneck: felling is refused loudly when the
    ///   operation's landing cannot absorb more logs (Canon §8.5A: "hiring
    ///   more loggers does not help if the yard or mill cannot absorb their
    ///   output").
    ///
    /// Upstream provenance is preserved end to end: every felled lot still
    /// routes through LoggingOperationRuntime.RunFelling and the shared
    /// LoggingStandRegistry (rights-gated, stand-provenanced). The camp adds
    /// gates; it never bypasses them. Money moves only through ledger
    /// authorities, never here.
    ///
    /// DESIGN FORK recorded (canon + research silent): camp cook. No cook
    /// role or cooking mechanic is invented; roles are free-form strings so a
    /// cook can be rostered without a code change, consuming rations like
    /// any other head.
    /// </summary>
    public sealed class LoggingCampSite
    {
        private readonly List<string> diagnostics = new List<string>();
        private string campId;
        private string businessInstanceId;
        private string standId;
        private int dayEstablished;
        private bool struck;
        private int dayStruck;
        private string strikeReason = string.Empty;
        private int resumeDayIndex; // no felling before this day after a relocation
        private readonly List<LoggingCampCrewEntry> crew = new List<LoggingCampCrewEntry>();
        private readonly LoggingCampSupplyLedger supplyLedger = new LoggingCampSupplyLedger();
        private readonly List<LoggingCampWorkDay> workDays = new List<LoggingCampWorkDay>();
        private LoggingCampParameters parameters = new LoggingCampParameters();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public string CampId => campId;
        public string BusinessInstanceId => businessInstanceId;
        public string StandId => standId;
        public int DayEstablished => dayEstablished;
        public bool IsStruck => struck;
        public IReadOnlyList<LoggingCampCrewEntry> Crew => crew;
        public LoggingCampSupplyLedger SupplyLedger => supplyLedger;
        public IReadOnlyList<LoggingCampWorkDay> WorkDays => workDays;
        public LoggingCampParameters Parameters => parameters;

        public LoggingCampSite(string campId, string businessInstanceId, string standId, int dayEstablished)
        {
            this.campId = campId ?? string.Empty;
            this.businessInstanceId = businessInstanceId ?? string.Empty;
            this.standId = standId ?? string.Empty;
            this.dayEstablished = dayEstablished;
            this.resumeDayIndex = dayEstablished;
        }

        public int CrewHeadcount
        {
            get
            {
                int count = 0;
                foreach (var entry in crew)
                    if (entry != null) count++;
                return count;
            }
        }

        public int LoggerCount
        {
            get
            {
                int count = 0;
                foreach (var entry in crew)
                {
                    if (entry == null) continue;
                    if (string.Equals(entry.Role, LoggingCampRoles.Logger, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(entry.Role, LoggingCampRoles.Feller, StringComparison.OrdinalIgnoreCase))
                        count++;
                }
                return count;
            }
        }

        /// <summary>
        /// Assigns a worker to the camp roster. Duplicate assignments and
        /// blank roles are refused loudly. Bunkhouse over-capacity is
        /// recorded as STRAIN, not a wall (canon residential doctrine:
        /// capacity creates rising strain rather than a hard occupancy wall)
        /// — the assignment still stands.
        /// </summary>
        public string AssignWorker(EntityId worker, string role, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (struck)
            {
                diag.Add($"LoggingCampSite '{campId}': ASSIGNMENT REFUSED — camp is struck.");
                return "camp-struck";
            }
            if (worker.Equals(EntityId.Invalid))
            {
                diag.Add($"LoggingCampSite '{campId}': ASSIGNMENT REFUSED — no worker named.");
                return "no-worker";
            }
            if (string.IsNullOrWhiteSpace(role))
            {
                diag.Add($"LoggingCampSite '{campId}': ASSIGNMENT REFUSED — the role must be named.");
                return "no-role";
            }
            foreach (var entry in crew)
            {
                if (entry != null && entry.Worker.Equals(worker))
                {
                    diag.Add($"LoggingCampSite '{campId}': ASSIGNMENT REFUSED — worker {worker} is already on the roster.");
                    return "duplicate-assignment";
                }
            }

            crew.Add(new LoggingCampCrewEntry { Worker = worker, Role = role.Trim(), DayJoined = dayIndex });
            diag.Add($"LoggingCampSite '{campId}': worker {worker} joined as '{role.Trim()}' (day {dayIndex}).");
            if (parameters != null && parameters.BunkCapacity >= 0 && CrewHeadcount > parameters.BunkCapacity)
            {
                diag.Add($"LoggingCampSite '{campId}': BUNKHOUSE STRAIN — {CrewHeadcount} heads for " +
                    $"{parameters.BunkCapacity} bunks (over by {CrewHeadcount - parameters.BunkCapacity}). " +
                    "Recorded as strain, not a wall; work proceeds.");
            }
            return null;
        }

        /// <summary>Releases a worker from the roster. Unknown workers are refused loudly.</summary>
        public string ReleaseWorker(EntityId worker, List<string> diag)
        {
            diag = diag ?? diagnostics;
            for (int i = 0; i < crew.Count; i++)
            {
                if (crew[i] != null && crew[i].Worker.Equals(worker))
                {
                    crew.RemoveAt(i);
                    diag.Add($"LoggingCampSite '{campId}': worker {worker} released from the roster.");
                    return null;
                }
            }
            diag.Add($"LoggingCampSite '{campId}': RELEASE REFUSED — worker {worker} is not on the roster.");
            return "unknown-worker";
        }

        /// <summary>
        /// Strikes the camp — the worksite closes (stand cut out, season
        /// over, owner decision). Explicit and recorded; the landing stock
        /// and hauls stay with the operation, unaffected.
        /// </summary>
        public string StrikeCamp(int dayIndex, string reason, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (struck)
            {
                diag.Add($"LoggingCampSite '{campId}': STRIKE REFUSED — camp already struck (day {dayStruck}).");
                return "already-struck";
            }
            struck = true;
            dayStruck = dayIndex;
            strikeReason = reason ?? string.Empty;
            diag.Add($"LoggingCampSite '{campId}': camp struck (day {dayIndex}) — {strikeReason}");
            return null;
        }

        /// <summary>
        /// Relocates the camp to another REGISTERED stand (the old stand's
        /// remaining timber stays with the registry). Struck camps cannot
        /// relocate; unknown stands are refused. After the move the camp
        /// cannot fell for Parameters.CampMoveDownDays days (parameterized
        /// open calibration — nothing is guessed about move cost).
        /// </summary>
        public string RelocateCamp(string newStandId, LoggingStandRegistry registry,
            int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (struck)
            {
                diag.Add($"LoggingCampSite '{campId}': RELOCATION REFUSED — camp is struck.");
                return "camp-struck";
            }
            if (registry == null || registry.GetStand(newStandId) == null)
            {
                diag.Add($"LoggingCampSite '{campId}': RELOCATION REFUSED — stand '{newStandId}' is not registered. " +
                    "Camps move to real stands, never to conjured ground.");
                return "unknown-stand";
            }
            if (string.Equals(standId, newStandId, StringComparison.OrdinalIgnoreCase))
            {
                diag.Add($"LoggingCampSite '{campId}': RELOCATION REFUSED — already pitched at stand '{newStandId}'.");
                return "same-stand";
            }

            string oldStand = standId;
            standId = newStandId;
            int downDays = parameters != null ? Math.Max(0, parameters.CampMoveDownDays) : 0;
            resumeDayIndex = dayIndex + downDays;
            diag.Add($"LoggingCampSite '{campId}': relocated from stand '{oldStand}' to stand '{newStandId}' " +
                $"(day {dayIndex}); no felling before day {resumeDayIndex} ({downDays} parameterized down days).");
            return null;
        }

        /// <summary>
        /// The daylight-limited working minutes for a camp day (Canon §8.5):
        /// usable work light for the month minus the authored camp-to-stump
        /// travel. Never negative.
        /// </summary>
        public int WorkableMinutes(int monthIndex)
        {
            int usable = LoggingSeasonalDaylight.UsableWorkMinutes(monthIndex);
            if (usable < 0) return -1; // invalid month — refused upstream
            int travel = parameters != null ? Math.Max(0, parameters.CampToStumpTravelMinutesPerDay) : 0;
            return Math.Max(0, usable - travel);
        }

        /// <summary>
        /// Runs one camp felling day. Gate order: struck -> stand known and
        /// not exhausted -> relocation down days -> felling crew present
        /// (two-person teams, T1E) -> workable daylight -> provisions on hand
        /// (unfed crew refused LOUDLY, never auto-supplied) -> landing room
        /// (bottleneck doctrine). Every refusal touches nothing — no timber
        /// felled, no provisions consumed. A partial day (capacity below
        /// logsWanted) fells what the day allows and records which bound won.
        /// </summary>
        public LogLot RunCampDay(
            LoggingStandRegistry registry,
            LoggingOperationRuntime operation,
            string fellerRightsHolderId,
            int logsWanted,
            EntityId worker,
            int dayIndex,
            int monthIndex,
            List<string> diag,
            EquipmentTaskGate gate = null)
        {
            diag = diag ?? diagnostics;
            if (struck)
            {
                diag.Add($"LoggingCampSite '{campId}': CAMP DAY REFUSED — camp is struck (day {dayStruck}).");
                return null;
            }
            if (registry == null || operation == null)
            {
                diag.Add($"LoggingCampSite '{campId}': CAMP DAY REFUSED — needs a stand registry and an operation.");
                return null;
            }

            TimberStand stand = registry.GetStand(standId);
            if (stand == null)
            {
                diag.Add($"LoggingCampSite '{campId}': CAMP DAY REFUSED — stand '{standId}' is not registered.");
                return null;
            }
            if (stand.StandingTimberUnits <= 0)
            {
                diag.Add($"LoggingCampSite '{campId}': CAMP DAY REFUSED — stand '{standId}' is cut out " +
                    "(no standing timber). Strike the camp or relocate it to a live stand.");
                return null;
            }
            if (dayIndex < resumeDayIndex)
            {
                diag.Add($"LoggingCampSite '{campId}': CAMP DAY REFUSED — camp still moving after relocation " +
                    $"(no felling before day {resumeDayIndex}).");
                return null;
            }

            int teams = LoggerCount / 2; // T1E felling is two-person hand-tool work
            if (teams <= 0)
            {
                diag.Add($"LoggingCampSite '{campId}': CAMP DAY REFUSED — no felling crew. " +
                    $"Felling is two-person work (T1E); the roster holds {LoggerCount} logger(s).");
                return null;
            }

            int workable = WorkableMinutes(monthIndex);
            if (workable < 0)
            {
                diag.Add($"LoggingCampSite '{campId}': CAMP DAY REFUSED — month {monthIndex} is not 1-12; " +
                    "the working day cannot be established, so nothing is guessed.");
                return null;
            }
            if (workable == 0)
            {
                diag.Add($"LoggingCampSite '{campId}': CAMP DAY REFUSED — no workable daylight remains " +
                    $"after travel (month {monthIndex}).");
                return null;
            }

            int headcount = CrewHeadcount;
            float rationRate = parameters != null ? Math.Max(0f, parameters.ProvisionsPerHeadPerDay) : 1f;
            float provisionsNeeded = headcount * rationRate;
            if (supplyLedger.Balance < provisionsNeeded)
            {
                diag.Add($"LoggingCampSite '{campId}': CAMP DAY REFUSED — the crew goes unfed " +
                    $"({supplyLedger.Balance} provisions on hand, {provisionsNeeded} needed for {headcount} heads). " +
                    "No delivery is auto-ordered; arrange a real supply delivery.");
                return null;
            }

            int landingRoom = operation.LandingCapacityLogUnits - operation.LandingLogUnits;
            if (landingRoom <= 0)
            {
                diag.Add($"LoggingCampSite '{campId}': CAMP DAY REFUSED — the landing is full " +
                    $"({operation.LandingLogUnits} logs staged, capacity {operation.LandingCapacityLogUnits}). " +
                    "Canon §8.5A bottleneck: hiring more loggers does not help when the yard cannot absorb their output — " +
                    "haul logs to the mill before felling more.");
                return null;
            }

            int perTeam = workable / TimberHarvest.FellMinutesPerLog; // T1E: 90 min/log, crew of two
            int crewCap = teams * perTeam;
            int standCap = Math.Max(0, stand.StandingTimberUnits);
            int cap = Math.Min(crewCap, Math.Min(standCap, landingRoom));
            if (cap <= 0)
            {
                diag.Add($"LoggingCampSite '{campId}': CAMP DAY REFUSED — nothing fellable today " +
                    $"(crew {crewCap}, stand {standCap}, landing room {landingRoom}).");
                return null;
            }

            int toFell = Math.Min(Math.Max(0, logsWanted), cap);
            if (toFell <= 0)
            {
                diag.Add($"LoggingCampSite '{campId}': CAMP DAY REFUSED — {logsWanted} logs wanted is not a felling day.");
                return null;
            }

            // All camp gates passed — fell through the operation (rights and
            // equipment gates still run there). A refusal there touches
            // nothing here: provisions are consumed only for work actually done.
            LogLot lot = operation.RunFelling(registry, standId, fellerRightsHolderId,
                toFell, worker, dayIndex, diag, gate);
            if (lot == null) return null;

            string consumeRefusal = supplyLedger.Consume(provisionsNeeded, dayIndex, diag);
            if (consumeRefusal != null)
            {
                // Cannot happen: the balance was checked above and nothing
                // else consumes between the check and here. Recorded loudly
                // rather than silently proceeding.
                diag.Add($"LoggingCampSite '{campId}': PROVISION ACCOUNTING FAULT after felling — {consumeRefusal}");
            }

            string limitReason = "full-request";
            if (lot.LogUnits < logsWanted)
            {
                // crewCap already folds the daylight limit in (teams x
                // workable-minutes/90); the work-day record carries both
                // LoggerCount and WorkableMinutes so the bound stays legible.
                if (crewCap <= standCap && crewCap <= landingRoom) limitReason = "crew";
                else if (standCap <= landingRoom) limitReason = "stand";
                else limitReason = "landing";
                diag.Add($"LoggingCampSite '{campId}': partial day — {lot.LogUnits} of {logsWanted} logs felled " +
                    $"(bound: {limitReason}).");
            }

            workDays.Add(new LoggingCampWorkDay
            {
                DayIndex = dayIndex,
                MonthIndex = monthIndex,
                CrewHeadcount = headcount,
                LoggerCount = LoggerCount,
                WorkableMinutes = workable,
                LogsWanted = logsWanted,
                LogsFelled = lot.LogUnits,
                ProvisionsConsumed = provisionsNeeded,
                LimitReason = limitReason,
            });
            diag.Add($"LoggingCampSite '{campId}': camp day complete — {lot.LogUnits} logs felled " +
                $"({teams} team(s) x {perTeam}/team, {workable} workable minutes), {provisionsNeeded} provisions consumed.");
            return lot;
        }

        /// <summary>D2D save contract: lives inside the owning camp class.</summary>
        [Serializable]
        public sealed class LoggingCampSiteSaveDto
        {
            public string CampId = string.Empty;
            public string BusinessInstanceId = string.Empty;
            public string StandId = string.Empty;
            public int DayEstablished;
            public bool Struck;
            public int DayStruck;
            public string StrikeReason = string.Empty;
            public int ResumeDayIndex;
            public LoggingCampParameters Parameters = new LoggingCampParameters();
            public List<LoggingCampCrewEntry> Crew = new List<LoggingCampCrewEntry>();
            public List<LoggingCampWorkDay> WorkDays = new List<LoggingCampWorkDay>();
            public LoggingCampSupplyLedger.LoggingCampSupplyLedgerSaveDto SupplyLedger =
                new LoggingCampSupplyLedger.LoggingCampSupplyLedgerSaveDto();
        }

        public LoggingCampSiteSaveDto CaptureSaveDto()
        {
            var dto = new LoggingCampSiteSaveDto
            {
                CampId = campId,
                BusinessInstanceId = businessInstanceId,
                StandId = standId,
                DayEstablished = dayEstablished,
                Struck = struck,
                DayStruck = dayStruck,
                StrikeReason = strikeReason ?? string.Empty,
                ResumeDayIndex = resumeDayIndex,
                Parameters = new LoggingCampParameters
                {
                    ProvisionsPerHeadPerDay = parameters.ProvisionsPerHeadPerDay,
                    CampToStumpTravelMinutesPerDay = parameters.CampToStumpTravelMinutesPerDay,
                    BunkCapacity = parameters.BunkCapacity,
                    CampMoveDownDays = parameters.CampMoveDownDays,
                },
                SupplyLedger = supplyLedger.CaptureSaveDto(),
            };
            foreach (var entry in crew)
            {
                if (entry == null) continue;
                dto.Crew.Add(new LoggingCampCrewEntry
                {
                    Worker = entry.Worker,
                    Role = entry.Role,
                    DayJoined = entry.DayJoined,
                });
            }
            foreach (var day in workDays)
            {
                if (day == null) continue;
                dto.WorkDays.Add(new LoggingCampWorkDay
                {
                    DayIndex = day.DayIndex,
                    MonthIndex = day.MonthIndex,
                    CrewHeadcount = day.CrewHeadcount,
                    LoggerCount = day.LoggerCount,
                    WorkableMinutes = day.WorkableMinutes,
                    LogsWanted = day.LogsWanted,
                    LogsFelled = day.LogsFelled,
                    ProvisionsConsumed = day.ProvisionsConsumed,
                    LimitReason = day.LimitReason,
                });
            }
            return dto;
        }

        public void LoadFromSaveDto(LoggingCampSiteSaveDto dto)
        {
            crew.Clear();
            workDays.Clear();
            if (dto == null) return;
            campId = dto.CampId ?? string.Empty;
            businessInstanceId = dto.BusinessInstanceId ?? string.Empty;
            standId = dto.StandId ?? string.Empty;
            dayEstablished = dto.DayEstablished;
            struck = dto.Struck;
            dayStruck = dto.DayStruck;
            strikeReason = dto.StrikeReason ?? string.Empty;
            resumeDayIndex = dto.ResumeDayIndex;
            if (dto.Parameters != null) parameters = dto.Parameters;
            if (dto.Crew != null)
            {
                foreach (var entry in dto.Crew)
                {
                    if (entry == null) continue;
                    crew.Add(entry);
                }
            }
            if (dto.WorkDays != null)
            {
                foreach (var day in dto.WorkDays)
                {
                    if (day == null) continue;
                    workDays.Add(day);
                }
            }
            supplyLedger.LoadFromSaveDto(dto.SupplyLedger);
        }
    }
}
