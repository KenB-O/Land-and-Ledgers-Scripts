using System;
using System.Collections.Generic;
using LandLedgers.Economy.Equipment;
using LandLedgers.Primitives;
using LandLedgers.Skills;
using LandLedgers.Tasks;
using UnityEngine;

namespace LandLedgers.Economy.Farming.Timber
{
    /// <summary>
    /// T1E: an authored timber stand — the primary-production end of the lumber chain.
    /// Standing timber is a natural resource with an owner (who holds the timber rights);
    /// felling depletes it. Regrowth is slow and explicit, never assumed.
    /// </summary>
    [Serializable]
    public sealed class TimberStand
    {
        public string StandId = string.Empty;
        public string DisplayName = string.Empty;
        public string LocationId = string.Empty; // JRN-1 location
        public string TimberRightsHolderId = string.Empty; // business/household/town that may fell here
        public string Species = string.Empty; // e.g. "white pine"
        public int StandingTimberUnits; // logs still standing
        public int LastFelledDayIndex = -1;

        public TimberStand() { }
    }

    /// <summary>
    /// T1E: felled logs with provenance — which stand, which worker, which day.
    /// Logs are the sawmill's input; they do not become lumber without sawing.
    /// </summary>
    [Serializable]
    public sealed class LogLot
    {
        public EntityId LotId = EntityId.Invalid; // EntityKind.Lot (HF-1)
        public int LogUnits;
        public string StandId = string.Empty;
        public string Species = string.Empty;
        public EntityId FelledBy = EntityId.Invalid; // worker person id (labor provenance)
        public int FelledDayIndex;

        public LogLot() { }
    }

    /// <summary>
    /// T1E: sawn lumber with provenance — which logs, which mill, which worker.
    /// This is what the lumber yard retails and what FarmConstruction buys.
    /// </summary>
    [Serializable]
    public sealed class LumberLot
    {
        public EntityId LotId = EntityId.Invalid; // EntityKind.Lot (HF-1)
        public int LumberUnits;
        public string SourceLogLotId = string.Empty; // upstream provenance
        public string MillBusinessId = string.Empty;
        public EntityId SawedBy = EntityId.Invalid;
        public int SawedDayIndex;

        public LumberLot() { }
    }

    /// <summary>
    /// T1E: the timber harvest authority. Felling is labor: historical practice is
    /// axe + crosscut saw work by a small crew, logs skidded by draft animals —
    /// so felling consumes worker time (TTS task) and the stand depletes per log.
    /// No logs appear without felling labor (Canon §7.4C: machines let workers
    /// complete more work in time; they do not create yield).
    /// </summary>
    public static class TimberHarvest
    {
        public const string FellTimberTaskId = "fell-timber";
        public const string ForestrySkillId = "forestry";

        /// <summary>Calibration: minutes to fell and limb one log (crew of two, hand tools).</summary>
        public const int FellMinutesPerLog = 90;

        /// <summary>Registers the forestry skill via the TTS-3 extension path (not a starter skill).</summary>
        public static void RegisterSkills(SkillService skillService, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (skillService == null)
            {
                diagnostics.Add("TimberHarvest: no SkillService — forestry skill not registered.");
                return;
            }

            if (skillService.GetSkill(ForestrySkillId) == null)
            {
                string rejection;
                if (!skillService.RegisterSkill(
                    new SkillDefinition(ForestrySkillId, "Forestry",
                        "Felling, limbing, and skidding timber with hand tools and draft animals. Registered by the timber chain (TTS-3 extension path)."),
                    out rejection))
                {
                    diagnostics.Add("TimberHarvest: forestry skill rejected: " + rejection);
                }
            }
        }

        /// <summary>Registers the fell-timber task definition (TTS-2).</summary>
        public static void RegisterTaskDefinitions(TaskAuthority authority)
        {
            if (authority == null) return;

            var fell = new TaskDefinition(FellTimberTaskId, "Fell timber", FellMinutesPerLog);
            fell.SetRequiredSkill(ForestrySkillId, new[] { "felling" });
            // NX-1A: coded — felling without an axe or saw is not a real method
            // (Canon 4.1). Either hand tool or the two-man saw satisfies.
            fell.EquipmentClasses.Add(
                EquipmentRequirementCodes.Asset("felling-axe") + "|" +
                EquipmentRequirementCodes.Asset("crosscut-saw"));
            string ignored;
            authority.RegisterDefinition(fell, out ignored);
        }

        /// <summary>
        /// Fells logsWanted logs from the stand. The stand depletes; the worker and day
        /// are recorded on the lot. Felling without timber-rights holding is refused —
        /// timber theft is a claim, not a harvest (canon property doctrine).
        /// </summary>
        public static LogLot FellLogs(
            EntityIdRegistry idRegistry,
            TimberStand stand,
            string fellerRightsHolderId,
            int logsWanted,
            EntityId worker,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (idRegistry == null || stand == null)
            {
                diagnostics.Add("TimberHarvest: need a registry and a timber stand.");
                return null;
            }

            if (!string.Equals(stand.TimberRightsHolderId, fellerRightsHolderId, StringComparison.Ordinal))
            {
                diagnostics.Add($"TimberHarvest: {fellerRightsHolderId} holds no timber rights on stand '{stand.StandId}' — felling refused.");
                return null;
            }

            int felled = Math.Min(Math.Max(0, logsWanted), Math.Max(0, stand.StandingTimberUnits));
            if (felled <= 0)
            {
                diagnostics.Add($"TimberHarvest: stand '{stand.StandId}' has no standing timber left.");
                return null;
            }

            stand.StandingTimberUnits -= felled;
            stand.LastFelledDayIndex = dayIndex;

            return new LogLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                LogUnits = felled,
                StandId = stand.StandId,
                Species = stand.Species,
                FelledBy = worker,
                FelledDayIndex = dayIndex,
            };
        }
    }

    /// <summary>
    /// T1E: the sawmill — a real business turning logs into lumber. Sawing is labor
    /// (circular-saw mill work); lumberPerLog matches the authored sawmill definition
    /// calibration. Logs in, lumber out, every board traced to its log lot.
    /// </summary>
    public sealed class Sawmill
    {
        public const string SawLogsTaskId = "saw-logs";

        /// <summary>Calibration: minutes to saw one log into boards.</summary>
        public const int SawMinutesPerLog = 30;

        public string MillBusinessId = string.Empty;
        public string MillName = string.Empty;
        public int LumberPerLog = 4;

        public Sawmill() { }

        public Sawmill(string millBusinessId, string millName, int lumberPerLog)
        {
            MillBusinessId = millBusinessId ?? string.Empty;
            MillName = millName ?? string.Empty;
            LumberPerLog = Math.Max(1, lumberPerLog);
        }

        /// <summary>Registers the saw-logs task definition (TTS-2).</summary>
        public static void RegisterTaskDefinitions(TaskAuthority authority)
        {
            if (authority == null) return;

            var saw = new TaskDefinition(SawLogsTaskId, "Saw logs", SawMinutesPerLog);
            saw.SetRequiredSkill(TimberHarvest.ForestrySkillId, new[] { "sawmilling" });
            // NX-1A: coded — sawing requires the sawmill saw line workstation.
            // A dull/broken saw gates all output until the filer works (the
            // EQP-2 filer loop now bites: Tech X §3.9, Canon 4.1).
            saw.EquipmentClasses.Add(EquipmentRequirementCodes.Workstation("sawmill-saw-line"));
            string ignored;
            authority.RegisterDefinition(saw, out ignored);
        }

        /// <summary>Saws a log lot into lumber. The log lot is consumed; the lumber lot carries its id.</summary>
        public LumberLot SawLogs(
            EntityIdRegistry idRegistry,
            LogLot logLot,
            EntityId worker,
            int dayIndex,
            List<string> diagnostics,
            EquipmentTaskGate gate = null)
        {
            diagnostics = diagnostics ?? new List<string>();
            // NX-1A: Canon 4.1 — the sawmill saw line workstation, with its
            // support requirements (Canon 5.2). A dull/broken saw gates all
            // output until the filer works (Tech X §3.9).
            if (gate != null)
            {
                string blocked = gate.CheckCodes(
                    new List<string> { EquipmentRequirementCodes.Workstation("sawmill-saw-line") },
                    "business", MillBusinessId, dayIndex, diagnostics);
                if (blocked != null) return null;
            }
            if (idRegistry == null || logLot == null || logLot.LogUnits <= 0)
            {
                diagnostics.Add("Sawmill: need a registry and a non-empty log lot.");
                return null;
            }

            int lumberUnits = logLot.LogUnits * LumberPerLog;
            var lumber = new LumberLot
            {
                LotId = idRegistry.Allocate(EntityKind.Lot),
                LumberUnits = lumberUnits,
                SourceLogLotId = logLot.LotId.ToString(),
                MillBusinessId = MillBusinessId,
                SawedBy = worker,
                SawedDayIndex = dayIndex,
            };
            logLot.LogUnits = 0; // consumed into lumber — one physical lot is never in two places.

            diagnostics.Add($"Sawmill {MillName}: sawed {lumberUnits} lumber units from log lot {lumber.SourceLogLotId}.");
            return lumber;
        }
    }

    /// <summary>
    /// T1E: the lumber yard — retail end of the timber chain. Finite stock bought from
    /// named sawmills; this is where FarmConstruction buys its lumber. Restocks must
    /// name the supplying mill — suppliers need sources too.
    /// </summary>
    [Serializable]
    public sealed class LumberYard
    {
        public string YardBusinessId = string.Empty;
        public string YardName = string.Empty;
        public int PricePerUnitCents;

        private int stockUnits;
        private string upstreamMill = string.Empty;

        public LumberYard() { }

        public LumberYard(string yardBusinessId, string yardName, int pricePerUnitCents)
        {
            YardBusinessId = yardBusinessId ?? string.Empty;
            YardName = yardName ?? string.Empty;
            PricePerUnitCents = Math.Max(0, pricePerUnitCents);
        }

        public int StockUnits => stockUnits;
        public string UpstreamMill => upstreamMill;

        public string RestockLumber(int units, string sawmillBusinessId, int dayIndex)
        {
            if (units <= 0) return "LumberYard: no units to stock.";
            if (string.IsNullOrWhiteSpace(sawmillBusinessId))
            {
                return "LumberYard: lumber must name its supplying sawmill — no orphan inputs.";
            }

            stockUnits += units;
            upstreamMill = sawmillBusinessId;
            return null;
        }

        /// <summary>Returns units actually sold (finite stock), or -1 with a diagnostic on refusal.</summary>
        public int SellLumber(int requestedUnits, int dayIndex, List<string> diagnostics)
        {
            diagnostics = diagnostics ?? new List<string>();
            if (requestedUnits <= 0)
            {
                diagnostics.Add("LumberYard: no units requested.");
                return -1;
            }

            int sold = Math.Min(requestedUnits, stockUnits);
            if (sold <= 0)
            {
                diagnostics.Add($"LumberYard: {YardName} has no lumber in stock — finite supply (Canon §9.2).");
                return -1;
            }

            stockUnits -= sold;
            return sold;
        }
    }
}
