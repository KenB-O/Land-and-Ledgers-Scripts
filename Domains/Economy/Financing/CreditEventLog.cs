using System;
using System.Collections.Generic;

namespace LandLedgers.Economy.Financing
{
    /// <summary>
    /// Phase E (E5): source-level credit observability. Every consequential
    /// credit event — negotiations, property decisions, settlements, failed
    /// decisions with reasons, delinquency and workout stages — lands here as
    /// an append-only record. This is a reporting surface only: it never owns
    /// balances, never creates obligations, and never moves cash.
    /// </summary>
    public enum CreditEventKind
    {
        Unspecified = 0,
        NegotiationSubmitted = 1,
        OfferMade = 2,
        CounterMade = 3,
        OfferAccepted = 4,
        OfferDeclined = 5,
        OfferRefused = 6,
        Settlement = 7,
        PaymentCollected = 8,
        PaymentMissed = 9,
        DelinquencyMarked = 10,
        WorkoutOffered = 11,
        WorkoutRestructured = 12,
        GuarantyCalled = 13,
        GuarantySettled = 14,
        ForeclosureNotice = 15,
        ForeclosureSale = 16,
        PropertyDecision = 17,
        DecisionFailed = 18,
        TradeCreditAged = 19,
        CollectionStageChanged = 20,
        FacilityDrawn = 21,
        SellerNoteIssued = 22,
        TitleTransferred = 23,
        LenderRefused = 24,
    }

    [Serializable]
    public sealed class CreditEvent
    {
        public long Sequence;
        public int DayIndex;
        public CreditEventKind Kind;
        public string ObligationId = string.Empty;
        public string InstrumentId = string.Empty;
        public string Borrower = string.Empty;
        public string Lender = string.Empty;
        public int AmountCents;
        public string Summary = string.Empty;

        public CreditEvent() { }
    }

    /// <summary>
    /// Phase E: the shared credit event log. Append-only; sequence numbers are
    /// assigned on record. Save/load round-trips the full history.
    /// </summary>
    public sealed class CreditEventLog
    {
        private readonly List<CreditEvent> events = new List<CreditEvent>();
        private long sequence;

        public IReadOnlyList<CreditEvent> Events => events;
        public int Count => events.Count;

        public CreditEvent Record(int dayIndex, CreditEventKind kind,
            string borrower, string lender, int amountCents, string summary,
            string obligationId = "", string instrumentId = "")
        {
            var evt = new CreditEvent
            {
                Sequence = sequence++,
                DayIndex = dayIndex,
                Kind = kind,
                Borrower = borrower ?? string.Empty,
                Lender = lender ?? string.Empty,
                AmountCents = amountCents,
                Summary = summary ?? string.Empty,
                ObligationId = obligationId ?? string.Empty,
                InstrumentId = instrumentId ?? string.Empty,
            };
            events.Add(evt);
            return evt;
        }

        public List<CreditEvent> FindByObligation(string obligationId)
        {
            var result = new List<CreditEvent>();
            foreach (CreditEvent evt in events)
                if (evt != null && string.Equals(evt.ObligationId, obligationId, StringComparison.Ordinal))
                    result.Add(evt);
            return result;
        }

        public List<CreditEvent> FindByKind(CreditEventKind kind)
        {
            var result = new List<CreditEvent>();
            foreach (CreditEvent evt in events)
                if (evt != null && evt.Kind == kind)
                    result.Add(evt);
            return result;
        }

        #region Save / Load
        [Serializable]
        public sealed class CreditEventLogSaveDto
        {
            public List<CreditEvent> Events = new List<CreditEvent>();
            public long NextSequence;
        }

        public CreditEventLogSaveDto CaptureSaveDto()
        {
            return new CreditEventLogSaveDto
            {
                Events = new List<CreditEvent>(events),
                NextSequence = sequence,
            };
        }

        public void LoadFromSaveDto(CreditEventLogSaveDto dto)
        {
            events.Clear();
            sequence = 0;
            if (dto == null) return;
            if (dto.Events != null)
                foreach (CreditEvent evt in dto.Events)
                    if (evt != null) events.Add(evt);
            sequence = Math.Max(dto.NextSequence, events.Count);
        }
        #endregion
    }
}
