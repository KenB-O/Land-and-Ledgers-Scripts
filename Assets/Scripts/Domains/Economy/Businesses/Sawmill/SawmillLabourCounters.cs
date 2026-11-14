using System;
using System.Collections.Generic;

// PORT NOTE (F6 Tier 1, 2026-11-13): ported verbatim from the forestry
// branch (main repo at 346d8746, 222 lines) onto this line's baseline
// 2cb4781, following the C4 Tier 1 adapter port. No logic change.
//
// CORRECTION to the forestry header below: its Counter A doc says the
// increment point is "the future driver at its RecordWorkProgress call".
// That sentence is stale on this line. Here the task minutes are worked
// BEFORE RunSawing by the SawmillProductionDriver's own labour sequence
// (mint, assign, start, RecordWorkProgress), and the counters are
// credited AT the production return from the verified RunSawing result
// (SawmillSawingResult SawMinutes / LumberLot LumberUnits). The holder
// counts only; it never decides whether minutes or lumber count.
//
// Brief 6 section 6 and Brief 6 R3 section 7. Diagnostic counter
// holder for sawmill labour, following the Brief 3 counter design.
//
// Counter A counts credited saw minutes, keyed by sawmill business
// id. It is incremented once per minute actually credited at the
// production return from a verified RunSawing result
// (TimberChain Sawmill SawLogsTaskId "saw-logs", 30 minutes per log).
// SawmillShopRuntime RunSawing already returns the honest figure as
// SawmillSawingResult SawMinutes, which is what the sawmill labour
// driver credits.
//
// Counter B counts lumber units produced, keyed by sawmill business
// id. It is incremented once per unit at the production return:
// SawmillShopRuntime RunSawing, reading result LumberLot
// LumberUnits, and TimberChain Sawmill SawLogs, reading LumberLot
// LumberUnits. The scalar resolver output point is not yet
// identified in source and is an open wiring question for Sol, not
// invented here.
//
// This holder counts only. It never decides whether minutes or
// lumber count and never touches stock, money, tasks or freight.
// The driver and the runtimes call the Record methods at the points
// above and own the holder instance and its save slot. Counters are
// monotonic and only grow. There is no reset method. The weekly one
// line per week per sawmill reporting is Sol PlayMode reporting over
// the read methods, not a holder method. A blank id is counted under
// a sentinel key so a mis-wired call shows up as a visible line
// instead of silently faking a zero.
namespace LandLedgers.Economy.Businesses.Sawmill
{
    public sealed class SawmillLabourCounters
    {
        public const string UnknownSawmillId = "unknown-sawmill";

        private readonly Dictionary<string, int> creditedSawMinutesByBusiness =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> lumberUnitsByBusiness =
            new Dictionary<string, int>(StringComparer.Ordinal);

        public int TotalSawMinutes
        {
            get
            {
                int total = 0;
                foreach (int minutes in creditedSawMinutesByBusiness.Values)
                {
                    total += minutes;
                }

                return total;
            }
        }

        public int TotalLumberUnits
        {
            get
            {
                int total = 0;
                foreach (int units in lumberUnitsByBusiness.Values)
                {
                    total += units;
                }

                return total;
            }
        }

        public bool IsSawMinutesZero => TotalSawMinutes == 0;

        public bool IsLumberZero => TotalLumberUnits == 0;

        public IReadOnlyList<string> BusinessIds
        {
            get
            {
                var ids = new List<string>(creditedSawMinutesByBusiness.Keys);
                foreach (string id in lumberUnitsByBusiness.Keys)
                {
                    if (!ids.Contains(id))
                    {
                        ids.Add(id);
                    }
                }

                return ids;
            }
        }

        public void RecordCreditedSawMinutes(string sawmillBusinessId, int minutes)
        {
            if (minutes <= 0)
            {
                return;
            }

            string key = string.IsNullOrWhiteSpace(sawmillBusinessId) ? UnknownSawmillId : sawmillBusinessId;
            AddUnits(creditedSawMinutesByBusiness, key, minutes);
        }

        public void RecordLumberProduced(string sawmillBusinessId, int units)
        {
            if (units <= 0)
            {
                return;
            }

            string key = string.IsNullOrWhiteSpace(sawmillBusinessId) ? UnknownSawmillId : sawmillBusinessId;
            AddUnits(lumberUnitsByBusiness, key, units);
        }

        public int SawMinutesForBusiness(string sawmillBusinessId)
        {
            if (string.IsNullOrWhiteSpace(sawmillBusinessId))
            {
                return 0;
            }

            return creditedSawMinutesByBusiness.TryGetValue(sawmillBusinessId, out int minutes) ? minutes : 0;
        }

        public int LumberUnitsForBusiness(string sawmillBusinessId)
        {
            if (string.IsNullOrWhiteSpace(sawmillBusinessId))
            {
                return 0;
            }

            return lumberUnitsByBusiness.TryGetValue(sawmillBusinessId, out int units) ? units : 0;
        }

        public SawmillLabourCountersDto ToSaveDto()
        {
            SawmillLabourCountersDto dto = new SawmillLabourCountersDto();
            foreach (KeyValuePair<string, int> entry in creditedSawMinutesByBusiness)
            {
                dto.sawMinuteEntries.Add(new SawMinuteCounterEntry
                {
                    businessId = entry.Key,
                    minutes = entry.Value,
                });
            }

            foreach (KeyValuePair<string, int> entry in lumberUnitsByBusiness)
            {
                dto.lumberEntries.Add(new LumberCounterEntry
                {
                    businessId = entry.Key,
                    units = entry.Value,
                });
            }

            return dto;
        }

        public void LoadFromSaveDto(SawmillLabourCountersDto dto)
        {
            creditedSawMinutesByBusiness.Clear();
            lumberUnitsByBusiness.Clear();
            if (dto == null)
            {
                return;
            }

            if (dto.sawMinuteEntries != null)
            {
                foreach (SawMinuteCounterEntry entry in dto.sawMinuteEntries)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.businessId) || entry.minutes <= 0)
                    {
                        continue;
                    }

                    AddUnits(creditedSawMinutesByBusiness, entry.businessId, entry.minutes);
                }
            }

            if (dto.lumberEntries != null)
            {
                foreach (LumberCounterEntry entry in dto.lumberEntries)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.businessId) || entry.units <= 0)
                    {
                        continue;
                    }

                    AddUnits(lumberUnitsByBusiness, entry.businessId, entry.units);
                }
            }
        }

        private static void AddUnits(Dictionary<string, int> counts, string key, int units)
        {
            counts.TryGetValue(key, out int existing);
            counts[key] = existing + units;
        }

        [Serializable]
        public sealed class SawmillLabourCountersDto
        {
            public List<SawMinuteCounterEntry> sawMinuteEntries = new List<SawMinuteCounterEntry>();
            public List<LumberCounterEntry> lumberEntries = new List<LumberCounterEntry>();
        }

        [Serializable]
        public sealed class SawMinuteCounterEntry
        {
            public string businessId = string.Empty;
            public int minutes;
        }

        [Serializable]
        public sealed class LumberCounterEntry
        {
            public string businessId = string.Empty;
            public int units;
        }
    }
}
