using System;
using System.Collections.Generic;
using UnityEngine;

namespace LandLedgers.Economy.Creation
{
    /// <summary>
    /// Phase G (§26): every kind of NPC business-formation event the
    /// simulation records at source level. Append-only. Opportunity
    /// observations, investigation findings and verdicts, formation
    /// decisions (including rejected alternatives), lifecycle events,
    /// failed formations and their reasons are all observable here —
    /// never buried inside a decision engine.
    /// </summary>
    public enum NpcBusinessEventKind
    {
        Unspecified = 0,
        OpportunityObserved = 1,
        OpportunityRecognized = 2,
        OpportunityDismissed = 3,
        InvestigationStarted = 4,
        InvestigationTimeReserved = 5,
        InvestigationFinding = 6,
        InvestigationWalkAway = 7,
        InvestigationProceed = 8,
        FormationValidated = 9,
        FormationCashMoved = 10,
        FormationCreated = 11,
        FormationAssetRecorded = 12,
        FormationHireRegistered = 13,
        FormationAgreementRecorded = 14,
        FormationReady = 15,
        FormationAborted = 16,
        LifeEvaluated = 17,
        StruggleResponse = 18,
        BorrowInitiated = 19,
        PivotStarted = 20,
        PivotComplete = 21,
        CloseStarted = 22,
        ObligationSettled = 23,
        AssetLiquidated = 24,
        StaffReleased = 25,
        CloseComplete = 26,
        BusinessAcquired = 27,
        DemandPlanned = 28,
        DemandPurchased = 29,
    }

    /// <summary>
    /// Phase G (§26): one source-level event in an NPC's business-formation
    /// story. ActorPersonId is -1 when no Person is the actor.
    /// </summary>
    [Serializable]
    public sealed class NpcBusinessEvent
    {
        public int DayIndex;
        public NpcBusinessEventKind Kind = NpcBusinessEventKind.Unspecified;
        public int ActorPersonId = -1;
        public string BusinessRef = string.Empty;
        public string Detail = string.Empty;

        public NpcBusinessEvent() { }

        public NpcBusinessEvent(int dayIndex, NpcBusinessEventKind kind, int actorPersonId,
            string businessRef, string detail)
        {
            DayIndex = dayIndex;
            Kind = kind;
            ActorPersonId = actorPersonId;
            BusinessRef = businessRef ?? string.Empty;
            Detail = detail ?? string.Empty;
        }
    }

    /// <summary>
    /// Phase G (§26): the single append-only event log for NPC business
    /// formation. Observations, findings, decisions (including walk-aways
    /// and rejections), lifecycle transitions and close-downs are recorded
    /// here with their reasons. Save DTO lives in this owning class.
    /// </summary>
    [Serializable]
    public sealed class NpcBusinessEventLog
    {
        [SerializeField]
        private List<NpcBusinessEvent> events = new List<NpcBusinessEvent>();

        public IReadOnlyList<NpcBusinessEvent> Events => events;

        public int Count => events != null ? events.Count : 0;

        public void Record(int dayIndex, NpcBusinessEventKind kind, int actorPersonId,
            string businessRef, string detail)
        {
            events.Add(new NpcBusinessEvent(dayIndex, kind, actorPersonId, businessRef, detail));
        }

        public List<NpcBusinessEvent> ForBusiness(string businessRef)
        {
            var results = new List<NpcBusinessEvent>();
            if (events == null) return results;
            foreach (NpcBusinessEvent e in events)
            {
                if (e != null && string.Equals(e.BusinessRef, businessRef, StringComparison.Ordinal))
                {
                    results.Add(e);
                }
            }

            return results;
        }

        public List<NpcBusinessEvent> ForPerson(int personId)
        {
            var results = new List<NpcBusinessEvent>();
            if (events == null) return results;
            foreach (NpcBusinessEvent e in events)
            {
                if (e != null && e.ActorPersonId == personId)
                {
                    results.Add(e);
                }
            }

            return results;
        }

        public int CountKind(NpcBusinessEventKind kind)
        {
            int count = 0;
            if (events == null) return count;
            foreach (NpcBusinessEvent e in events)
            {
                if (e != null && e.Kind == kind) count++;
            }

            return count;
        }

        public bool HasKindForBusiness(NpcBusinessEventKind kind, string businessRef)
        {
            if (events == null) return false;
            foreach (NpcBusinessEvent e in events)
            {
                if (e != null && e.Kind == kind
                    && string.Equals(e.BusinessRef, businessRef, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>CLN-1: captures the event log for the save pipeline.</summary>
        public NpcBusinessEventLogSaveDto CaptureSaveDto()
        {
            var dto = new NpcBusinessEventLogSaveDto();
            if (events != null)
            {
                foreach (NpcBusinessEvent e in events)
                {
                    if (e == null) continue;
                    dto.Events.Add(new NpcBusinessEvent
                    {
                        DayIndex = e.DayIndex,
                        Kind = e.Kind,
                        ActorPersonId = e.ActorPersonId,
                        BusinessRef = e.BusinessRef,
                        Detail = e.Detail,
                    });
                }
            }

            return dto;
        }

        /// <summary>CLN-1: restores the event log from the save pipeline.</summary>
        public void LoadFromSaveDto(NpcBusinessEventLogSaveDto dto)
        {
            events.Clear();
            if (dto == null || dto.Events == null) return;
            foreach (NpcBusinessEvent e in dto.Events)
            {
                if (e == null) continue;
                events.Add(new NpcBusinessEvent(e.DayIndex, e.Kind, e.ActorPersonId, e.BusinessRef, e.Detail));
            }
        }
    }

    /// <summary>CLN-1: persisted NPC business-formation events (save pipeline).</summary>
    [Serializable]
    public sealed class NpcBusinessEventLogSaveDto
    {
        public List<NpcBusinessEvent> Events = new List<NpcBusinessEvent>();

        public NpcBusinessEventLogSaveDto() { }
    }
}
