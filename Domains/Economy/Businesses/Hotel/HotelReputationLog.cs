using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Businesses.Hotel
{
    /// <summary>
    /// D2A: the kinds of real experiences the hotel accumulates. Canon
    /// §8.1G: "Reputation should emerge from actual experiences such as
    /// clean rooms, reliable meals, honored terms, theft/security problems,
    /// repeated overcrowding or bad service" — never from a flat
    /// hotel-quality upgrade. Each recorded event is a fact; the standing
    /// is a readout over those facts.
    /// </summary>
    public enum HotelExperienceKind
    {
        /// <summary>Clean rooms turned over and served — the positive default.</summary>
        CleanRooms = 0,
        /// <summary>Meals reliably served in the dining room (Canon §8.1G "reliable meals").</summary>
        ReliableMeals = 1,
        /// <summary>Terms honored as agreed (reservations kept, rates as promised).</summary>
        HonoredTerms = 2,
        /// <summary>A guest complaint logged (dirty room, cold meal, missed service).</summary>
        GuestComplaint = 3,
        /// <summary>A theft or security problem on the premises.</summary>
        TheftOrSecurity = 4,
        /// <summary>A traveler refused for lack of beds — recorded, not hidden (Canon §3.1 needs can go unsatisfied).</summary>
        OvercrowdingRefusal = 5,
        /// <summary>Bad service: rooms cold, desk unkept, staff absent.</summary>
        BadService = 6,
    }

    /// <summary>D2A: one experienced event on the hotel's reputation record.</summary>
    [Serializable]
    public sealed class HotelExperienceEvent
    {
        public int DayIndex;
        public HotelExperienceKind Kind = HotelExperienceKind.CleanRooms;
        public string Note = string.Empty;

        public HotelExperienceEvent() { }
    }

    /// <summary>
    /// D2A: the hotel's reputation record — an event log, not a quality
    /// stat. Reputation emerges from actual experiences (§8.1G): the
    /// runtime posts events as they happen (clean turnovers, meals
    /// served, complaints, refusals, cold rooms). The standing readout is
    /// computed from the trailing 30 days of events, weighted by kind:
    /// it degrades when experience runs bad and recovers when the house
    /// runs well — never a flat upgrade the proprietor buys.
    /// </summary>
    public sealed class HotelReputationLog
    {
        // TUNING: the trailing window the standing reads from (days).
        public const int StandingWindowDays = 30;

        private readonly List<string> diagnostics = new List<string>();
        private readonly List<HotelExperienceEvent> events = new List<HotelExperienceEvent>();

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<HotelExperienceEvent> Events => events;

        /// <summary>Records one experienced event. Events are facts — the log never drops one.</summary>
        public void RecordExperience(int dayIndex, HotelExperienceKind kind, string note, List<string> diag)
        {
            diag = diag ?? diagnostics;
            events.Add(new HotelExperienceEvent
            {
                DayIndex = dayIndex,
                Kind = kind,
                Note = note ?? string.Empty,
            });
        }

        /// <summary>Counts of each kind inside the trailing window ending on the given day.</summary>
        public Dictionary<HotelExperienceKind, int> WindowCounts(int dayIndex)
        {
            var counts = new Dictionary<HotelExperienceKind, int>();
            foreach (HotelExperienceEvent evt in events)
            {
                if (evt == null) continue;
                if (evt.DayIndex < dayIndex - StandingWindowDays || evt.DayIndex > dayIndex) continue;
                if (!counts.ContainsKey(evt.Kind)) counts[evt.Kind] = 0;
                counts[evt.Kind]++;
            }

            return counts;
        }

        /// <summary>
        /// TUNING: the standing score — positive events minus weighted
        /// negative events over the trailing window. The weights are
        /// calibration, not canon: theft/security and repeated
        /// overcrowding bite harder than one complaint.
        /// </summary>
        public int StandingScore(int dayIndex)
        {
            Dictionary<HotelExperienceKind, int> counts = WindowCounts(dayIndex);
            int score = 0;
            foreach (KeyValuePair<HotelExperienceKind, int> pair in counts)
            {
                switch (pair.Key)
                {
                    case HotelExperienceKind.CleanRooms: score += pair.Value; break;
                    case HotelExperienceKind.ReliableMeals: score += pair.Value; break;
                    case HotelExperienceKind.HonoredTerms: score += pair.Value; break;
                    case HotelExperienceKind.GuestComplaint: score -= pair.Value; break;
                    case HotelExperienceKind.TheftOrSecurity: score -= 3 * pair.Value; break;
                    case HotelExperienceKind.OvercrowdingRefusal: score -= 2 * pair.Value; break;
                    case HotelExperienceKind.BadService: score -= 2 * pair.Value; break;
                }
            }

            return score;
        }

        /// <summary>The standing readout — a band over the score, for the proprietor's readouts.</summary>
        public string Standing(int dayIndex)
        {
            int score = StandingScore(dayIndex);
            if (score >= 60) return "A well-kept house — travelers speak of it by name.";
            if (score >= 20) return "Solid: clean rooms, reliable meals, honored terms.";
            if (score >= 0) return "Mixed: travelers come, but complaints are in the air.";
            if (score >= -20) return "Slipping: bad nights are starting to define the place.";
            return "Failing: the town knows it is a place to avoid.";
        }

        /// <summary>Drops events older than the standing window — the ledger keeps the trailing facts, not all history.</summary>
        public void PruneBefore(int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            int cutoff = dayIndex - StandingWindowDays;
            int removed = 0;
            for (int i = events.Count - 1; i >= 0; i--)
            {
                if (events[i] == null) { events.RemoveAt(i); continue; }
                if (events[i].DayIndex < cutoff) { events.RemoveAt(i); removed++; }
            }

            if (removed > 0)
                diag.Add($"HotelReputationLog: pruned {removed} event(s) older than day {cutoff}.");
        }

        #region Save / Load
        [Serializable]
        public sealed class HotelReputationLogSaveDto
        {
            public List<HotelExperienceEvent> Events = new List<HotelExperienceEvent>();
        }

        public HotelReputationLogSaveDto CaptureSaveDto()
        {
            var dto = new HotelReputationLogSaveDto();
            foreach (HotelExperienceEvent evt in events)
            {
                if (evt != null) dto.Events.Add(evt);
            }

            return dto;
        }

        public void LoadFromSaveDto(HotelReputationLogSaveDto dto)
        {
            events.Clear();
            if (dto?.Events == null) return;
            foreach (HotelExperienceEvent evt in dto.Events)
            {
                if (evt != null) events.Add(evt);
            }
        }
        #endregion
    }
}
