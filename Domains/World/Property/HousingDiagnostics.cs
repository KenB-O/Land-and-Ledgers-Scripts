using System;
using System.Collections.Generic;

namespace LandLedgers.World.Property
{
    /// <summary>
    /// Phase D (Real People, §26): housing observability event kinds.
    /// Append-only.
    /// </summary>
    public enum HousingDecisionEventKind
    {
        Unspecified = 0,
        SearchDecision = 1,      // a household chose a search path
        RejectedAlternative = 2, // an option rejected, with its reason
        PropertyDecision = 3,    // agreement / parcel / design decisions
        OccupancyChange = 4,     // who moved where, under what arrangement
        ConstructionProgress = 5,// project/package stage completions
        RentFlow = 6,            // rent collected, arrears booked, repaid
        FailedHousingDecision = 7,// a search/execution/construction step that failed, with reasons
    }

    /// <summary>
    /// Phase D (§26): one source-level housing event. Read-only record —
    /// this log never moves money, goods, or people; it only remembers what
    /// was decided and why.
    /// </summary>
    [Serializable]
    public sealed class HousingDecisionEvent
    {
        public string EventId = string.Empty;
        public int DayIndex;
        public HousingDecisionEventKind Kind = HousingDecisionEventKind.Unspecified;
        public int HouseholdId = -1;
        public int PersonId = -1;
        public string Summary = string.Empty;
        public List<string> Details = new List<string>();

        public HousingDecisionEvent() { }
    }

    /// <summary>
    /// Phase D (§26): the housing decision log — source-level observability
    /// for housing search decisions + rejected alternatives, property
    /// decisions, occupancy changes, construction progress, rent flows, and
    /// failed housing decisions with their reasons. A future UI answers from
    /// here; the log itself is read-only projections, never an actor.
    /// </summary>
    public sealed class HousingDecisionLog
    {
        private readonly List<HousingDecisionEvent> events = new List<HousingDecisionEvent>();
        private readonly List<string> diagnostics = new List<string>();
        private int sequence;

        public IReadOnlyList<string> Diagnostics => diagnostics;
        public IReadOnlyList<HousingDecisionEvent> Events => events;

        public HousingDecisionEvent Record(
            int dayIndex,
            HousingDecisionEventKind kind,
            int householdId,
            string summary,
            int personId = -1,
            List<string> details = null)
        {
            var evt = new HousingDecisionEvent
            {
                EventId = $"hev-{sequence++}",
                DayIndex = dayIndex,
                Kind = kind,
                HouseholdId = householdId,
                PersonId = personId,
                Summary = summary ?? string.Empty,
                Details = details != null ? new List<string>(details) : new List<string>(),
            };
            events.Add(evt);
            return evt;
        }

        public List<HousingDecisionEvent> EventsForHousehold(int householdId)
        {
            var result = new List<HousingDecisionEvent>();
            foreach (HousingDecisionEvent evt in events)
                if (evt != null && evt.HouseholdId == householdId)
                    result.Add(evt);
            return result;
        }

        public List<HousingDecisionEvent> EventsOfKind(HousingDecisionEventKind kind)
        {
            var result = new List<HousingDecisionEvent>();
            foreach (HousingDecisionEvent evt in events)
                if (evt != null && evt.Kind == kind)
                    result.Add(evt);
            return result;
        }

        public List<HousingDecisionEvent> FailedDecisions() =>
            EventsOfKind(HousingDecisionEventKind.FailedHousingDecision);

        #region Save / Load
        [Serializable]
        public sealed class HousingDecisionLogSaveDto
        {
            public List<HousingDecisionEvent> Events = new List<HousingDecisionEvent>();
        }

        public HousingDecisionLogSaveDto CaptureSaveDto()
        {
            var dto = new HousingDecisionLogSaveDto();
            foreach (HousingDecisionEvent evt in events)
                if (evt != null) dto.Events.Add(evt);
            return dto;
        }

        public void LoadFromSaveDto(HousingDecisionLogSaveDto dto)
        {
            events.Clear();
            if (dto?.Events == null) return;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (HousingDecisionEvent evt in dto.Events)
            {
                if (evt == null || string.IsNullOrWhiteSpace(evt.EventId)) continue;
                if (!seen.Add(evt.EventId))
                {
                    diagnostics.Add($"LoadFromSaveDto: duplicate event '{evt.EventId}' skipped.");
                    continue;
                }
                events.Add(evt);
            }
        }
        #endregion
    }

    /// <summary>
    /// Phase D (§26): convenience adapters that turn the other D-workstream
    /// records into log events. Pure logging — no money, goods, or people
    /// move here.
    /// </summary>
    public static class HousingDecisionRecorder
    {
        public static void RecordSearchDecision(HousingDecisionLog log, HousingSearchDecision decision)
        {
            if (log == null || decision == null) return;
            if (decision.Resolved && decision.ChosenOption != null)
            {
                log.Record(decision.DayIndex, HousingDecisionEventKind.SearchDecision,
                    decision.HouseholdId,
                    $"chose {decision.ChosenOption.PathKind} '{decision.ChosenOption.Description}' " +
                    $"from '{decision.ChosenOption.ProviderName}' ({decision.RejectedAlternatives.Count} alternative(s) rejected)");
                foreach (HousingRejectedAlternative rejected in decision.RejectedAlternatives)
                {
                    if (rejected == null) continue;
                    log.Record(decision.DayIndex, HousingDecisionEventKind.RejectedAlternative,
                        decision.HouseholdId,
                        $"rejected {rejected.PathKind} '{rejected.ProviderName}': {rejected.Reason}",
                        details: new List<string> { rejected.Reason });
                }
            }
            else
            {
                log.Record(decision.DayIndex, HousingDecisionEventKind.FailedHousingDecision,
                    decision.HouseholdId,
                    $"housing search failed: {decision.FailureReason}",
                    details: new List<string> { decision.FailureReason ?? string.Empty });
                foreach (HousingRejectedAlternative rejected in decision.RejectedAlternatives)
                {
                    if (rejected == null) continue;
                    log.Record(decision.DayIndex, HousingDecisionEventKind.RejectedAlternative,
                        decision.HouseholdId,
                        $"rejected {rejected.PathKind} '{rejected.ProviderName}': {rejected.Reason}",
                        details: new List<string> { rejected.Reason });
                }
            }
        }

        public static void RecordOccupancyChange(
            HousingDecisionLog log, int personId, int householdId,
            string spaceId, AccommodationArrangement arrangement, int dayIndex, string note)
        {
            if (log == null) return;
            log.Record(dayIndex, HousingDecisionEventKind.OccupancyChange, householdId,
                $"P{personId} now occupies '{spaceId}' as {arrangement}" +
                (string.IsNullOrWhiteSpace(note) ? string.Empty : $" — {note}"),
                personId: personId);
        }

        public static void RecordPropertyDecision(
            HousingDecisionLog log, int householdId, string summary, int dayIndex)
        {
            if (log == null) return;
            log.Record(dayIndex, HousingDecisionEventKind.PropertyDecision, householdId, summary);
        }

        public static void RecordConstructionProgress(
            HousingDecisionLog log, int householdId, string projectId,
            string stageSummary, int dayIndex)
        {
            if (log == null) return;
            log.Record(dayIndex, HousingDecisionEventKind.ConstructionProgress, householdId,
                $"project '{projectId}': {stageSummary}");
        }

        public static void RecordRentFlow(
            HousingDecisionLog log, int householdId, int personId,
            string summary, int dayIndex)
        {
            if (log == null) return;
            log.Record(dayIndex, HousingDecisionEventKind.RentFlow, householdId, summary, personId: personId);
        }

        public static void RecordFailedHousingDecision(
            HousingDecisionLog log, int householdId, string summary,
            List<string> reasons, int dayIndex)
        {
            if (log == null) return;
            log.Record(dayIndex, HousingDecisionEventKind.FailedHousingDecision, householdId,
                summary, details: reasons);
        }
    }
}
