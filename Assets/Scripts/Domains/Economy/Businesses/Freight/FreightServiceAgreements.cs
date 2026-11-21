using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Logistics;
using UnityEngine;

namespace LandLedgers.Economy.Freight
{
    public enum FreightServiceRequestStatus { Open = 0, MeetingScheduled = 1, Quoted = 2, Refused = 3, Accepted = 4, AtMeeting = 5 }
    public enum FreightServiceAgreementStatus { Accepted = 0, Fulfilled = 1, Cancelled = 2, Refused = 3 }

    public enum FreightContactNoteStatus
    {
        LeftAtLocation = 0,
        DiscoveredByHouseholdMember = 1,
        RelayedToRecipient = 2,
        DiscoveredByRecipient = 3,
        Expired = 4,
    }

    /// <summary>
    /// A request-specific physical note left after an unsuccessful in-person
    /// freight contact. It is serialized with the existing request record, so
    /// its identity, location, custody and discovery survive Continue without
    /// creating a second correspondence database.
    /// </summary>
    [Serializable]
    public sealed class FreightContactNote
    {
        public string NoteId = string.Empty;
        public string RequestId = string.Empty;
        public int SenderPersonId = -1;
        public string SenderName = string.Empty;
        public int IntendedRecipientPersonId = -1;
        public string IntendedRecipientName = string.Empty;
        public string Purpose = string.Empty;
        public string OrderReference = string.Empty;
        public string LeftLocationId = string.Empty;
        public string CustodyLocationId = string.Empty;
        public int LeftBuildingId = -1;
        public int CreatedDayIndex = -1;
        public float CreatedGameSeconds;
        public int ExpiresDayIndex = -1;
        public FreightContactNoteStatus Status = FreightContactNoteStatus.LeftAtLocation;
        public int CustodianPersonId = -1;
        public int DiscoveredByPersonId = -1;
        public int DiscoveredDayIndex = -1;
        public int RelayedDayIndex = -1;
    }

    [Serializable]
    public sealed class FreightServiceQuote
    {
        public string QuoteId = string.Empty;
        public string RateCardBusinessId = string.Empty;
        public float OneWayDistanceMiles;
        public int ChargeableMiles;
        public int BaseServiceFeeCents;
        public int RatePerMileCents;
        public int MileageChargeCents;
        public int TotalPriceCents;
        public string DistanceBasis = string.Empty;
        public string DistanceRounding = "Ceiling to whole chargeable mile";
        public string PayerBusinessId = string.Empty;
        public string PaymentTiming = "After successful delivery";
        /// <summary>
        /// DEF-016 (Decision 10): cargo-aware disclosure. The money fields
        /// keep their per-trip card meaning: BaseServiceFeeCents is the
        /// card base per trip, MileageChargeCents is the per-trip mileage
        /// charge, TotalPriceCents is trips x (base + mileage). TripCount
        /// 0 on an old save reads as 1 everywhere it is consumed.
        /// </summary>
        public int TripCount;
        /// <summary>Most units one wagon carries of this cargo (the tighter of the mass and volume bounds).</summary>
        public int UnitsPerTrip;
        /// <summary>Total units priced.</summary>
        public int TotalQuantityUnits;
        /// <summary>Total cargo mass across all trips, kilograms.</summary>
        public float CargoMassKg;
        /// <summary>Total cargo volume across all trips, cubic metres.</summary>
        public float CargoVolumeM3;
        /// <summary>Price of one trip (base + mileage); TotalPriceCents is TripCount x this.</summary>
        public int PerTripTotalCents;
        /// <summary>Road-condition multiplier applied to the mileage (1 = unconditioned, from the journey route).</summary>
        public float ConditionMultiplier;
        /// <summary>One factual line naming the trips, cargo, condition, and price.</summary>
        public string TermsSummary = string.Empty;
    }

    [Serializable]
    public sealed class FreightServiceRequest
    {
        public string RequestId = string.Empty;
        public string RequestingBusinessId = string.Empty;
        public int RequestingPersonId = -1;
        public string CarrierBusinessId = string.Empty;
        public int CarrierOperatorPersonId = -1;
        public string PickupBusinessId = string.Empty;
        public string PickupLocationId = string.Empty;
        public string DestinationBusinessId = string.Empty;
        public string DestinationLocationId = string.Empty;
        public string CargoLotId = string.Empty;
        public string CargoSkuId = string.Empty;
        public int Quantity;
        public string QuantityUnit = string.Empty;
        public string RequestedService = "One-time Millbrook flour collection and Bakery delivery";
        public bool IsRecurring;
        public FreightServiceRequestStatus Status;
        public string RefusalReason = string.Empty;
        public string AgreementId = string.Empty;
        public string ShipmentId = string.Empty;
        public string ContactTravelTaskId = string.Empty;
        public string MeetingTaskId = string.Empty;
        public string MeetingOperatorReservationId = string.Empty;
        public int CompletedContactNoteAttempts;
        public int NextContactAttemptDayIndex;
        public List<FreightContactNote> ContactNotes = new List<FreightContactNote>();
        public FreightServiceQuote Quote;
    }

    [Serializable]
    public sealed class FreightServiceAgreement
    {
        public const int MaximumDispatchTaskRecoveryAttempts = 2;
        public string AgreementId = string.Empty;
        public string RequestId = string.Empty;
        public string CustomerBusinessId = string.Empty;
        public string CarrierBusinessId = string.Empty;
        public int RequesterPersonId = -1;
        public int AcceptingOperatorPersonId = -1;
        public string CargoLotId = string.Empty;
        public string PickupLocationId = string.Empty;
        public string DestinationLocationId = string.Empty;
        public string QuoteId = string.Empty;
        public int ChargeableMiles;
        public int BaseServiceFeeCents;
        public int RatePerMileCents;
        public int AgreedPriceCents;
        public string PayerBusinessId = string.Empty;
        public string PaymentTiming = "After successful delivery";
        public bool IsRecurring;
        public int AcceptedDayIndex;
        public float AcceptedGameSeconds;
        public float PreparedGameSeconds;
        public string DispatchTaskId = string.Empty;
        public int DispatchTaskRecoveryAttempts;
        public string LastDispatchTaskFailureReason = string.Empty;
        public List<string> SupersededDispatchTaskIds = new List<string>();
        public FreightServiceAgreementStatus Status = FreightServiceAgreementStatus.Accepted;
        public string FulfilmentStatus = "Pending pickup";
        public string ShipmentId = string.Empty;
        public bool ShipmentRegistrationPending = true;
        public string ShipmentRegistrationFailureReason = string.Empty;
    }

    [Serializable]
    public sealed class FreightServiceAgreementBookSaveDto
    {
        public List<FreightServiceRequest> Requests = new List<FreightServiceRequest>();
        public List<FreightServiceAgreement> Agreements = new List<FreightServiceAgreement>();
    }

    /// <summary>Exactly-once service request, quote, and acceptance records for hired freight.</summary>
    public sealed class FreightServiceAgreementBook
    {
        public delegate bool ShipmentRegistrar(FreightServiceAgreement agreement, out string shipmentId, out string reason);
        private readonly List<FreightServiceRequest> requests = new List<FreightServiceRequest>();
        private readonly List<FreightServiceAgreement> agreements = new List<FreightServiceAgreement>();
        public IReadOnlyList<FreightServiceRequest> Requests => requests;
        public IReadOnlyList<FreightServiceAgreement> Agreements => agreements;

        public FreightServiceRequest FindRequest(string requestId) => requests.Find(r =>
            r != null && string.Equals(r.RequestId, requestId, StringComparison.Ordinal));

        public FreightContactNote FindPendingContactNote(string requestId)
        {
            FreightServiceRequest request = FindRequest(requestId);
            if (request?.ContactNotes == null) return null;
            for (int i = request.ContactNotes.Count - 1; i >= 0; i--)
            {
                FreightContactNote note = request.ContactNotes[i];
                if (note != null && note.Status != FreightContactNoteStatus.RelayedToRecipient
                    && note.Status != FreightContactNoteStatus.DiscoveredByRecipient
                    && note.Status != FreightContactNoteStatus.Expired)
                    return note;
            }
            return null;
        }

        public string RecordContactNote(string requestId, FreightContactNote note)
        {
            FreightServiceRequest request = FindRequest(requestId);
            if (request == null || request.Status != FreightServiceRequestStatus.Open
                || FindAgreementForRequest(requestId) != null
                || note == null || string.IsNullOrWhiteSpace(note.NoteId)
                || !string.Equals(note.RequestId, requestId, StringComparison.Ordinal)
                || note.SenderPersonId != request.RequestingPersonId
                || note.IntendedRecipientPersonId != request.CarrierOperatorPersonId
                || string.IsNullOrWhiteSpace(note.SenderName)
                || string.IsNullOrWhiteSpace(note.IntendedRecipientName)
                || string.IsNullOrWhiteSpace(note.LeftLocationId)
                || string.IsNullOrWhiteSpace(note.Purpose)
                || string.IsNullOrWhiteSpace(note.OrderReference)
                || note.LeftBuildingId < 0 || note.CreatedDayIndex < 0
                || float.IsNaN(note.CreatedGameSeconds) || float.IsInfinity(note.CreatedGameSeconds)
                || note.CreatedGameSeconds < 0f
                || note.ExpiresDayIndex <= note.CreatedDayIndex
                || (!string.IsNullOrWhiteSpace(note.CustodyLocationId)
                    && !string.Equals(note.CustodyLocationId, note.LeftLocationId, StringComparison.Ordinal)))
                return "Freight contact note is missing its real request participants, identity, residence, or valid creation and expiry times.";

            request.ContactNotes ??= new List<FreightContactNote>();
            foreach (FreightContactNote prior in request.ContactNotes)
                if (prior != null && string.Equals(prior.NoteId, note.NoteId, StringComparison.Ordinal))
                    return "Freight contact note identity already exists; duplicate note creation refused.";
            if (FindPendingContactNote(requestId) != null)
                return "A freight contact note is already pending discovery; duplicate notes are refused.";

            note.Status = FreightContactNoteStatus.LeftAtLocation;
            note.CustodyLocationId = string.IsNullOrWhiteSpace(note.CustodyLocationId)
                ? note.LeftLocationId : note.CustodyLocationId;
            request.ContactNotes.Add(note);
            request.CompletedContactNoteAttempts = Math.Max(0, request.CompletedContactNoteAttempts) + 1;
            request.NextContactAttemptDayIndex = Math.Max(request.NextContactAttemptDayIndex, note.ExpiresDayIndex);
            return null;
        }

        public string SetContactNoteStatus(string requestId, string noteId,
            FreightContactNoteStatus status, int personId, int dayIndex)
        {
            FreightServiceRequest request = FindRequest(requestId);
            FreightContactNote note = request?.ContactNotes?.Find(candidate => candidate != null
                && string.Equals(candidate.NoteId, noteId, StringComparison.Ordinal));
            if (note == null) return "Unknown freight contact note.";
            if (dayIndex < note.CreatedDayIndex) return "A freight contact note cannot be discovered before it was created.";
            if (note.Status == FreightContactNoteStatus.RelayedToRecipient
                || note.Status == FreightContactNoteStatus.DiscoveredByRecipient
                || note.Status == FreightContactNoteStatus.Expired)
                return "Freight contact note is already terminal.";

            bool validTransition =
                (status == FreightContactNoteStatus.DiscoveredByHouseholdMember
                    && note.Status == FreightContactNoteStatus.LeftAtLocation
                    && personId >= 0 && personId != note.IntendedRecipientPersonId)
                || (status == FreightContactNoteStatus.DiscoveredByRecipient
                    && (note.Status == FreightContactNoteStatus.LeftAtLocation
                        || note.Status == FreightContactNoteStatus.DiscoveredByHouseholdMember)
                    && personId == note.IntendedRecipientPersonId)
                || (status == FreightContactNoteStatus.RelayedToRecipient
                    && note.Status == FreightContactNoteStatus.DiscoveredByHouseholdMember
                    && personId == note.IntendedRecipientPersonId)
                || (status == FreightContactNoteStatus.Expired
                    && dayIndex >= note.ExpiresDayIndex);
            if (!validTransition) return "Freight contact note status transition is not supported.";

            note.Status = status;
            if (status == FreightContactNoteStatus.DiscoveredByHouseholdMember
                || status == FreightContactNoteStatus.DiscoveredByRecipient)
            {
                note.DiscoveredByPersonId = personId;
                note.DiscoveredDayIndex = dayIndex;
                note.CustodianPersonId = personId;
                note.CustodyLocationId = string.Empty;
            }
            if (status == FreightContactNoteStatus.RelayedToRecipient)
            {
                note.RelayedDayIndex = dayIndex;
                note.CustodianPersonId = personId;
                note.CustodyLocationId = string.Empty;
            }
            return null;
        }

        public FreightServiceAgreement FindAgreement(string agreementId) => agreements.Find(a =>
            a != null && string.Equals(a.AgreementId, agreementId, StringComparison.Ordinal));

        /// <summary>
        /// The LIVE agreement for a request: the first agreement that is
        /// not Cancelled. A cancelled agreement is history, not a live
        /// agreement — it must not block contact notes, deferral,
        /// re-acceptance, or requeue for a request reopened after a
        /// recovery cancelled it (Lane 9d, F6-2). Callers that need the
        /// cancelled record itself use FindCancelledAgreementForRequest.
        /// </summary>
        public FreightServiceAgreement FindAgreementForRequest(string requestId) => agreements.Find(a =>
            a != null && string.Equals(a.RequestId, requestId, StringComparison.Ordinal)
            && a.Status != FreightServiceAgreementStatus.Cancelled);

        /// <summary>
        /// Lane 9d (F6-2): the cancelled agreement for a request, if any —
        /// the record ReopenTerminalRequest must find to prove the request
        /// was terminalized by a cancelled agreement rather than left
        /// hanging. Only the latest cancelled agreement is returned.
        /// </summary>
        public FreightServiceAgreement FindCancelledAgreementForRequest(string requestId)
        {
            FreightServiceAgreement found = null;
            foreach (FreightServiceAgreement agreement in agreements)
            {
                if (agreement != null && string.Equals(agreement.RequestId, requestId, StringComparison.Ordinal)
                    && agreement.Status == FreightServiceAgreementStatus.Cancelled)
                    found = agreement;
            }
            return found;
        }

        /// <summary>
        /// Selects the next accepted, registered shipment which has not yet
        /// entered preparation. Completed agreements remain in the book for
        /// their audit trail, so acceptance alone is not a sufficient
        /// eligibility test for the single opening horse and wagon.
        /// </summary>
        public FreightServiceAgreement FindNextAwaitingPreparation(IReadOnlyList<LogisticsShipmentState> shipments)
        {
            if (shipments == null || shipments.Count == 0) return null;
            foreach (FreightServiceAgreement agreement in agreements)
            {
                if (agreement == null
                    || agreement.Status != FreightServiceAgreementStatus.Accepted
                    || agreement.ShipmentRegistrationPending
                    || string.IsNullOrWhiteSpace(agreement.ShipmentId)
                    || agreement.PreparedGameSeconds > 0f
                    || !string.IsNullOrWhiteSpace(agreement.DispatchTaskId)
                    || !string.Equals(agreement.FulfilmentStatus, "Pending pickup", StringComparison.Ordinal))
                    continue;

                foreach (LogisticsShipmentState shipment in shipments)
                {
                    if (shipment == null
                        || !string.Equals(shipment.ShipmentId, agreement.ShipmentId, StringComparison.Ordinal)
                        || !string.Equals(shipment.FreightServiceAgreementId, agreement.AgreementId, StringComparison.Ordinal)
                        || shipment.Status != LogisticsShipmentStatus.Planned
                        || !shipment.requiresDispatchAuthorization
                        || shipment.loadApplied)
                        continue;
                    return agreement;
                }
            }
            return null;
        }

        public string CreateRequest(FreightServiceRequest request, out FreightServiceRequest existing)
        {
            existing = null;
            if (request == null || string.IsNullOrWhiteSpace(request.RequestId)
                || string.IsNullOrWhiteSpace(request.RequestingBusinessId)
                || string.IsNullOrWhiteSpace(request.CarrierBusinessId)
                || string.IsNullOrWhiteSpace(request.PickupBusinessId)
                || string.IsNullOrWhiteSpace(request.DestinationBusinessId)
                || string.IsNullOrWhiteSpace(request.CargoLotId) || request.Quantity <= 0)
                return "Freight service request is missing a participant, cargo lot, identity, or positive quantity.";
            existing = FindRequest(request.RequestId);
            if (existing != null) return "Freight request already exists; duplicate request creation refused.";
            foreach (FreightServiceRequest prior in requests)
                if (prior != null && string.Equals(prior.CargoLotId, request.CargoLotId, StringComparison.Ordinal)
                    && prior.Status != FreightServiceRequestStatus.Refused)
                    return $"Cargo lot '{request.CargoLotId}' already has request '{prior.RequestId}'.";
            request.Status = FreightServiceRequestStatus.Open;
            requests.Add(request);
            return null;
        }

        public string SetStatus(string requestId, FreightServiceRequestStatus status, string refusal = null)
        {
            FreightServiceRequest request = FindRequest(requestId);
            if (request == null) return "Unknown freight service request.";
            if (request.Status == FreightServiceRequestStatus.Accepted || request.Status == FreightServiceRequestStatus.Refused)
                return "Freight request is already terminal; status cannot be changed.";
            request.Status = status;
            request.RefusalReason = refusal ?? string.Empty;
            return null;
        }

        /// <summary>
        /// Returns a request to the open queue after a verified temporary
        /// carrier-capacity conflict. The same stable request and cargo lot
        /// are retried; no new request or agreement is created.
        /// </summary>
        public string DeferTemporarily(string requestId, string reason)
        {
            FreightServiceRequest request = FindRequest(requestId);
            if (request == null) return "Unknown freight service request.";
            if (request.Status == FreightServiceRequestStatus.Accepted
                || FindAgreementForRequest(requestId) != null)
                return "An accepted freight agreement cannot be deferred as an unaccepted request.";
            if (string.IsNullOrWhiteSpace(reason)) return "A temporary deferral requires its cause.";

            request.Status = FreightServiceRequestStatus.Open;
            request.RefusalReason = reason.Trim();
            request.ContactTravelTaskId = string.Empty;
            request.MeetingTaskId = string.Empty;
            request.MeetingOperatorReservationId = string.Empty;
            return null;
        }

        /// <summary>
        /// Reopens a terminally refused request whose paid lot is still
        /// shippable, so a request bricked before the meeting-retry repair
        /// can be retried by the next daily drive. Distinct from
        /// DeferTemporarily, which defers before terminal: this records a
        /// reopening after terminal, with the same stable request id, lot,
        /// and quote. The lot branch stays terminal; this method never
        /// reopens a request whose lot is no longer shippable, because the
        /// driver only calls it after FindShippableLot returns that lot.
        /// </summary>
        public string ReopenRefusedRequest(string requestId, string reason)
        {
            FreightServiceRequest request = FindRequest(requestId);
            if (request == null) return "Unknown freight service request.";
            if (request.Status != FreightServiceRequestStatus.Refused)
                return "Only a refused freight request can be reopened.";
            if (FindAgreementForRequest(requestId) != null)
                return "A refused request with an agreement cannot be reopened.";
            if (string.IsNullOrWhiteSpace(reason)) return "Reopening a refused request requires its cause.";

            request.Status = FreightServiceRequestStatus.Open;
            request.RefusalReason = reason.Trim();
            request.ContactTravelTaskId = string.Empty;
            request.MeetingTaskId = string.Empty;
            request.MeetingOperatorReservationId = string.Empty;
            return null;
        }

        /// <summary>
        /// Lane 9d (F6-2): reopens a terminally ACCEPTED request whose
        /// agreement was cancelled by a recovery's terminal disposition, so
        /// the same paid lot can be retried under a fresh agreement instead
        /// of stranding behind a request no code path can move. The pre-load
        /// terminal path already cancels the agreement and logs it; the
        /// post-load/committed strand used to end there too, with the
        /// request still Accepted and the lot still committed. Distinct from
        /// ReopenRefusedRequest (Refused only, no agreement ever existed):
        /// this requires the agreement to be Cancelled — never a live
        /// Accepted or Fulfilled one — and the lot must still be shippable,
        /// which the driver verifies with FindShippableLot before calling.
        /// The same stable request id and lot are retried; re-acceptance
        /// creates a fresh agreement with a retry-suffixed id (Accept), so
        /// the cancelled record stays in the book as history. The lot
        /// commitment itself is released separately (OffMapAidBusinessRuntime
        /// ReleaseFreightCommitment): reopening the request without
        /// releasing the lot would retry into the same refused commit.
        /// </summary>
        public string ReopenTerminalRequest(string requestId, string reason)
        {
            FreightServiceRequest request = FindRequest(requestId);
            if (request == null) return "Unknown freight service request.";
            if (request.Status != FreightServiceRequestStatus.Accepted)
                return "Only a terminally accepted freight request can be reopened as a terminal request.";
            FreightServiceAgreement agreement = FindCancelledAgreementForRequest(requestId);
            if (agreement == null)
                return "A terminally accepted request with no cancelled agreement cannot be reopened as a terminal request.";
            if (string.IsNullOrWhiteSpace(reason)) return "Reopening a terminally accepted request requires its cause.";

            request.Status = FreightServiceRequestStatus.Open;
            request.RefusalReason = reason.Trim();
            request.AgreementId = string.Empty;
            request.ShipmentId = string.Empty;
            request.ContactTravelTaskId = string.Empty;
            request.MeetingTaskId = string.Empty;
            request.MeetingOperatorReservationId = string.Empty;
            return null;
        }

        public string SetQuote(string requestId, FreightServiceQuote quote)
        {
            FreightServiceRequest request = FindRequest(requestId);
            if (request == null || quote == null || string.IsNullOrWhiteSpace(quote.QuoteId)
                || quote.OneWayDistanceMiles <= 0f || quote.ChargeableMiles <= 0
                || quote.BaseServiceFeeCents < 0 || quote.RatePerMileCents < 0
                || quote.MileageChargeCents < 0 || quote.TotalPriceCents < 0
                || quote.TripCount < 0
                || (long)Math.Max(1, quote.TripCount) * (quote.BaseServiceFeeCents + quote.MileageChargeCents) != quote.TotalPriceCents)
                return "Freight quote is incomplete or its integer-cent arithmetic does not reconcile.";
            if (request.Status == FreightServiceRequestStatus.Accepted || request.Status == FreightServiceRequestStatus.Refused)
                return "Freight request is terminal; quote change refused.";
            request.Quote = quote;
            request.Status = FreightServiceRequestStatus.Quoted;
            return null;
        }

        public string Accept(string requestId, int operatorPersonId, int dayIndex, float gameSeconds,
            out FreightServiceAgreement agreement)
        {
            agreement = null;
            FreightServiceRequest request = FindRequest(requestId);
            if (request == null || (request.Status != FreightServiceRequestStatus.Quoted
                    && request.Status != FreightServiceRequestStatus.MeetingScheduled) || request.Quote == null)
                return "No presented freight quote is available to accept.";
            agreement = FindAgreementForRequest(requestId);
            if (agreement != null) return "This request already has an agreement; duplicate acceptance refused.";
            if (operatorPersonId < 0 || request.CarrierOperatorPersonId != operatorPersonId)
                return "The accepting operator does not match the carrier's assigned person.";
            FreightServiceQuote quote = request.Quote;
            // Lane 9d (F6-2): a retry after a cancelled agreement keeps a
            // distinct id. The first agreement keeps the plain id; a retry
            // is suffixed with its attempt number, derived from the book's
            // own contents so it is stable across save/load. Without the
            // suffix, FindAgreement would return the cancelled record.
            int priorAgreements = 0;
            foreach (FreightServiceAgreement existing in agreements)
            {
                if (existing != null && string.Equals(existing.RequestId, requestId, StringComparison.Ordinal))
                    priorAgreements++;
            }
            string id = "freight-agreement:" + request.RequestId
                + (priorAgreements > 0 ? ":retry-" + (priorAgreements + 1) : string.Empty);
            agreement = new FreightServiceAgreement
            {
                AgreementId = id,
                RequestId = request.RequestId,
                CustomerBusinessId = request.RequestingBusinessId,
                CarrierBusinessId = request.CarrierBusinessId,
                RequesterPersonId = request.RequestingPersonId,
                AcceptingOperatorPersonId = operatorPersonId,
                CargoLotId = request.CargoLotId,
                PickupLocationId = request.PickupLocationId,
                DestinationLocationId = request.DestinationLocationId,
                QuoteId = quote.QuoteId,
                ChargeableMiles = quote.ChargeableMiles,
                BaseServiceFeeCents = quote.BaseServiceFeeCents,
                RatePerMileCents = quote.RatePerMileCents,
                AgreedPriceCents = quote.TotalPriceCents,
                PayerBusinessId = quote.PayerBusinessId,
                PaymentTiming = quote.PaymentTiming,
                IsRecurring = request.IsRecurring,
                AcceptedDayIndex = Math.Max(0, dayIndex),
                AcceptedGameSeconds = Math.Max(0f, gameSeconds),
                Status = FreightServiceAgreementStatus.Accepted,
                FulfilmentStatus = "Pending pickup",
                ShipmentRegistrationPending = true,
                ShipmentRegistrationFailureReason = string.Empty
            };
            agreements.Add(agreement);
            request.CarrierOperatorPersonId = operatorPersonId;
            request.AgreementId = id;
            request.Status = FreightServiceRequestStatus.Accepted;
            return null;
        }

        /// <summary>
        /// Closes an accepted agreement once its linked shipment is
        /// Completed with delivery applied and freight revenue collected.
        /// The shipment state the caller already holds is the evidence;
        /// no cash moves here, the charge was already settled exactly once
        /// by the verified collector. A second transition is refused.
        /// </summary>
        public string MarkFulfilled(string agreementId, LogisticsShipmentState shipment)
        {
            FreightServiceAgreement agreement = FindAgreement(agreementId);
            if (agreement == null || agreement.Status != FreightServiceAgreementStatus.Accepted)
                return "Only an accepted freight agreement can be fulfilled.";
            if (string.IsNullOrWhiteSpace(agreement.ShipmentId)
                || shipment == null
                || !string.Equals(shipment.ShipmentId, agreement.ShipmentId, StringComparison.Ordinal)
                || !string.Equals(shipment.FreightServiceAgreementId, agreement.AgreementId, StringComparison.Ordinal))
                return "Fulfilment requires the agreement's linked shipment.";
            if (shipment.Status != LogisticsShipmentStatus.Completed || !shipment.deliveryApplied
                || !shipment.freightRevenueCollected)
                return "Fulfilment requires a completed shipment with delivery applied and freight revenue collected.";
            if (shipment.HaulingMode != ShipmentHaulingMode.HiredFreight
                || !string.Equals(shipment.CarrierBusinessInstanceId, agreement.CarrierBusinessId, StringComparison.Ordinal)
                || !string.Equals(shipment.FreightPayerBusinessInstanceId, agreement.PayerBusinessId, StringComparison.Ordinal)
                || shipment.FreightChargeCents != Mathf.Max(0, agreement.AgreedPriceCents)
                || (shipment.requiresSourceLotCustody
                    && !string.Equals(shipment.CargoLotId, agreement.CargoLotId, StringComparison.Ordinal)))
                return "Fulfilment shipment terms do not match the accepted carrier, payer, price, hauling mode, or cargo lot.";
            agreement.Status = FreightServiceAgreementStatus.Fulfilled;
            agreement.FulfilmentStatus = "Fulfilled";
            return null;
        }

        /// <summary>
        /// Cancels an accepted agreement with its cause. DEF-071
        /// (Decision 14) supplies the live caller the Plan 07 Slice 2
        /// review left open: FreightProductionDriver.AdvanceTick's
        /// settlement pass cancels the agreement of every Failed shipment
        /// still Accepted, with the shipment's failure cause. A second
        /// transition is refused.
        /// </summary>
        public string CancelAgreement(string agreementId, string reason)
        {
            FreightServiceAgreement agreement = FindAgreement(agreementId);
            if (agreement == null || agreement.Status != FreightServiceAgreementStatus.Accepted)
                return "Only an accepted freight agreement can be cancelled.";
            if (string.IsNullOrWhiteSpace(reason)) return "Cancelling an agreement requires its cause.";
            agreement.Status = FreightServiceAgreementStatus.Cancelled;
            agreement.FulfilmentStatus = "Cancelled: " + reason.Trim();
            return null;
        }

        public string LinkShipment(string agreementId, string shipmentId)
        {
            FreightServiceAgreement agreement = FindAgreement(agreementId);
            if (agreement == null || string.IsNullOrWhiteSpace(shipmentId)) return "Accepted agreement and shipment id are required.";
            if (!string.IsNullOrWhiteSpace(agreement.ShipmentId))
                return string.Equals(agreement.ShipmentId, shipmentId, StringComparison.Ordinal)
                    ? null : "Agreement already references a different shipment.";
            agreement.ShipmentId = shipmentId;
            agreement.ShipmentRegistrationPending = false;
            agreement.ShipmentRegistrationFailureReason = string.Empty;
            FreightServiceRequest request = FindRequest(agreement.RequestId);
            if (request != null) request.ShipmentId = shipmentId;
            return null;
        }

        /// <summary>Retains a technical shipment-registration failure without undoing the verbal agreement.</summary>
        public string RecordShipmentRegistrationFailure(string agreementId, string reason)
        {
            FreightServiceAgreement agreement = FindAgreement(agreementId);
            if (agreement == null || agreement.Status != FreightServiceAgreementStatus.Accepted)
                return "Only an accepted freight agreement can await shipment registration.";
            if (!string.IsNullOrWhiteSpace(agreement.ShipmentId))
                return "Agreement already has a registered shipment; failure cannot replace it.";
            agreement.ShipmentRegistrationPending = true;
            agreement.ShipmentRegistrationFailureReason = string.IsNullOrWhiteSpace(reason)
                ? "Shipment registration failed without a diagnostic." : reason;
            return null;
        }

        /// <summary>Retries one accepted agreement through the existing shipment authority, linking at most one result.</summary>
        public string TryRegisterShipment(string agreementId, ShipmentRegistrar registrar, out bool alreadyRegistered)
        {
            alreadyRegistered = false;
            FreightServiceAgreement agreement = FindAgreement(agreementId);
            if (agreement == null || agreement.Status != FreightServiceAgreementStatus.Accepted)
                return "Only an accepted freight agreement can register a shipment.";
            if (!string.IsNullOrWhiteSpace(agreement.ShipmentId))
            {
                alreadyRegistered = true;
                return null;
            }
            if (registrar == null)
            {
                const string unavailable = "Shipment registrar is unavailable.";
                RecordShipmentRegistrationFailure(agreementId, unavailable);
                return unavailable;
            }
            if (!registrar(agreement, out string shipmentId, out string reason)
                || string.IsNullOrWhiteSpace(shipmentId))
            {
                string failure = string.IsNullOrWhiteSpace(reason)
                    ? "Shipment authority refused registration without a diagnostic." : reason;
                RecordShipmentRegistrationFailure(agreementId, failure);
                return failure;
            }
            string linkRefusal = LinkShipment(agreementId, shipmentId);
            if (linkRefusal != null) RecordShipmentRegistrationFailure(agreementId, linkRefusal);
            return linkRefusal;
        }

        /// <summary>Records preparation only after the linked shipment remains pending dispatch.</summary>
        public string MarkPreparedForDispatch(string agreementId, string shipmentId, float gameSeconds)
        {
            FreightServiceAgreement agreement = FindAgreement(agreementId);
            if (agreement == null || agreement.Status != FreightServiceAgreementStatus.Accepted
                || string.IsNullOrWhiteSpace(agreement.ShipmentId)
                || !string.Equals(agreement.ShipmentId, shipmentId, StringComparison.Ordinal))
                return "Preparation must reference the accepted agreement's registered shipment.";
            if (float.IsNaN(gameSeconds) || float.IsInfinity(gameSeconds) || gameSeconds < agreement.AcceptedGameSeconds)
                return $"Preparation timestamp {gameSeconds:0.##}s precedes agreement acceptance at {agreement.AcceptedGameSeconds:0.##}s.";
            if (agreement.PreparedGameSeconds > 0f)
            {
                if (gameSeconds < agreement.PreparedGameSeconds)
                    return $"Preparation timestamp {gameSeconds:0.##}s precedes the recorded preparation at {agreement.PreparedGameSeconds:0.##}s.";
                return null;
            }
            agreement.PreparedGameSeconds = Math.Max(0f, gameSeconds);
            agreement.FulfilmentStatus = "Prepared for dispatch";
            return null;
        }

        /// <summary>Compatibility entry point for older callers and focused state tests.</summary>
        public string MarkPreparedForDispatch(string agreementId, string shipmentId)
        {
            FreightServiceAgreement agreement = FindAgreement(agreementId);
            return MarkPreparedForDispatch(agreementId, shipmentId,
                agreement != null ? agreement.AcceptedGameSeconds : 0f);
        }

        /// <summary>Links the single queued TaskAuthority assignment for an accepted, prepared shipment.</summary>
        public string RegisterDispatchTask(string agreementId, string shipmentId, string taskId)
        {
            FreightServiceAgreement agreement = FindAgreement(agreementId);
            if (agreement == null || agreement.Status != FreightServiceAgreementStatus.Accepted
                || string.IsNullOrWhiteSpace(agreement.ShipmentId)
                || !string.Equals(agreement.ShipmentId, shipmentId, StringComparison.Ordinal)
                || agreement.PreparedGameSeconds <= 0f)
                return "A dispatch task requires this agreement's accepted, registered, prepared shipment.";
            if (string.IsNullOrWhiteSpace(taskId)) return "A stable TaskAuthority task id is required.";
            if (agreement.SupersededDispatchTaskIds != null
                && agreement.SupersededDispatchTaskIds.Exists(value => string.Equals(value, taskId, StringComparison.Ordinal)))
                return "A failed or cancelled dispatch task id cannot be linked again.";
            if (!string.IsNullOrWhiteSpace(agreement.DispatchTaskId))
                return string.Equals(agreement.DispatchTaskId, taskId, StringComparison.Ordinal)
                    ? null : "Agreement already references a different dispatch task.";
            if (!string.Equals(agreement.FulfilmentStatus, "Prepared for dispatch", StringComparison.Ordinal))
                return "A dispatch task can only be registered once the shipment is prepared for dispatch.";
            agreement.DispatchTaskId = taskId;
            agreement.FulfilmentStatus = "Dispatch assigned";
            return null;
        }

        /// <summary>
        /// Reopens the prepared stage after the caller has verified that its
        /// linked TaskAuthority task failed, was cancelled/interrupted, or
        /// was pruned. Keeps the accepted agreement and shipment identity;
        /// only the terminal task link is retired. The retry bound prevents a
        /// permanently impossible dispatch task from holding the shipment.
        /// </summary>
        public string RequeueDispatchAfterTaskFailure(string agreementId, string failedTaskId,
            string failureReason, out bool recoveryLimitReached)
        {
            recoveryLimitReached = false;
            FreightServiceAgreement agreement = FindAgreement(agreementId);
            if (agreement == null || agreement.Status != FreightServiceAgreementStatus.Accepted
                || string.IsNullOrWhiteSpace(agreement.ShipmentId)
                || agreement.PreparedGameSeconds <= 0f
                || !string.Equals(agreement.FulfilmentStatus, "Dispatch assigned", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(failedTaskId)
                || !string.Equals(agreement.DispatchTaskId, failedTaskId, StringComparison.Ordinal))
                return "Only the accepted agreement's currently linked, prepared dispatch task can be recovered.";
            if (string.IsNullOrWhiteSpace(failureReason))
                return "Dispatch task recovery requires the observed terminal or missing-task reason.";
            if (agreement.DispatchTaskRecoveryAttempts >= FreightServiceAgreement.MaximumDispatchTaskRecoveryAttempts)
            {
                recoveryLimitReached = true;
                return "The bounded dispatch task recovery limit has been reached.";
            }

            agreement.SupersededDispatchTaskIds ??= new List<string>();
            if (!agreement.SupersededDispatchTaskIds.Exists(value => string.Equals(value, failedTaskId, StringComparison.Ordinal)))
                agreement.SupersededDispatchTaskIds.Add(failedTaskId);
            agreement.DispatchTaskRecoveryAttempts++;
            agreement.LastDispatchTaskFailureReason = failureReason.Trim();
            agreement.DispatchTaskId = string.Empty;
            agreement.FulfilmentStatus = "Prepared for dispatch";
            return null;
        }

        public FreightServiceAgreementBookSaveDto CaptureSaveDto()
        {
            var dto = new FreightServiceAgreementBookSaveDto();
            foreach (FreightServiceRequest request in requests)
                if (request != null) dto.Requests.Add(JsonUtility.FromJson<FreightServiceRequest>(JsonUtility.ToJson(request)));
            foreach (FreightServiceAgreement agreement in agreements)
                if (agreement != null) dto.Agreements.Add(JsonUtility.FromJson<FreightServiceAgreement>(JsonUtility.ToJson(agreement)));
            return dto;
        }

        public void RestoreSaveDto(FreightServiceAgreementBookSaveDto dto)
        {
            requests.Clear();
            agreements.Clear();
            if (dto == null) return;
            foreach (FreightServiceRequest request in dto.Requests ?? new List<FreightServiceRequest>())
                if (request != null && !string.IsNullOrWhiteSpace(request.RequestId)) requests.Add(request);
            foreach (FreightServiceAgreement agreement in dto.Agreements ?? new List<FreightServiceAgreement>())
                if (agreement != null && !string.IsNullOrWhiteSpace(agreement.AgreementId)) agreements.Add(agreement);
        }
    }

    public static class FreightServiceQuoteCalculator
    {
        public static bool TryCalculate(float oneWayDistanceMiles, int baseFeeCents, int ratePerMileCents,
            bool chargeRoundTrip, out FreightServiceQuote quote, out string reason)
        {
            quote = null;
            reason = string.Empty;
            if (float.IsNaN(oneWayDistanceMiles) || float.IsInfinity(oneWayDistanceMiles) || oneWayDistanceMiles <= 0f)
            { reason = "An authored positive one-way distance in miles is required."; return false; }
            if (baseFeeCents < 0 || ratePerMileCents < 0)
            { reason = "Freight rate card contains a negative fee."; return false; }
            double chargeable = chargeRoundTrip ? Math.Ceiling(oneWayDistanceMiles * 2d) : Math.Ceiling(oneWayDistanceMiles);
            if (chargeable <= 0d || chargeable > int.MaxValue)
            { reason = "Chargeable mileage is outside supported whole-mile bounds."; return false; }
            long mileage = checked((long)chargeable * ratePerMileCents);
            long total = mileage + baseFeeCents;
            if (mileage > int.MaxValue || total > int.MaxValue)
            { reason = "Calculated freight quote exceeds supported integer-cent currency bounds."; return false; }
            quote = new FreightServiceQuote
            {
                QuoteId = string.Empty,
                OneWayDistanceMiles = oneWayDistanceMiles,
                ChargeableMiles = (int)chargeable,
                BaseServiceFeeCents = baseFeeCents,
                RatePerMileCents = ratePerMileCents,
                MileageChargeCents = (int)mileage,
                TotalPriceCents = (int)total,
                DistanceBasis = chargeRoundTrip ? "Expected round trip, loaded outward and empty return" : "One-way route",
                DistanceRounding = "Each chargeable mile rounded up to a whole mile",
                PaymentTiming = "After successful delivery",
                // DEF-016: the miles-only quote is a single trip by construction.
                TripCount = 1,
                PerTripTotalCents = (int)total,
                ConditionMultiplier = 1f,
                TermsSummary = "1 trip; " + baseFeeCents + "c dispatch + " + ratePerMileCents + "c/mi "
                    + (chargeRoundTrip ? "round trip" : "one-way") + "; total " + total + "c",
            };
            return true;
        }

        /// <summary>
        /// DEF-016 (Decision 10): cargo-aware quotation. The price is the
        /// card applied per trip, times the trips the load needs: each trip
        /// is a real wagon journey with its own dispatch, load, drive, and
        /// unload. Road conditions scale the mileage portion only
        /// (mustering does not get muddier, the miles do); the multiplier
        /// comes from the journey route, and 0 means unconditioned.
        /// Handling, crew, and animals are priced through the per-trip
        /// dispatch and disclosed on the plan — no invented per-horse or
        /// per-hand rate. The old TryCalculate is untouched for callers
        /// with miles only.
        /// </summary>
        public static bool TryCalculateCargo(float oneWayDistanceMiles, int baseFeeCents, int ratePerMileCents,
            bool chargeRoundTrip, int quantityUnits, FreightCargoSpec cargo,
            float wagonMassCapacityKg, float wagonVolumeCapacityM3,
            float conditionMultiplier, out FreightServiceQuote quote, out string reason)
        {
            quote = null;
            reason = string.Empty;
            if (float.IsNaN(oneWayDistanceMiles) || float.IsInfinity(oneWayDistanceMiles) || oneWayDistanceMiles <= 0f)
            { reason = "An authored positive one-way distance in miles is required."; return false; }
            if (baseFeeCents < 0 || ratePerMileCents < 0)
            { reason = "Freight rate card contains a negative fee."; return false; }
            if (quantityUnits <= 0)
            { reason = "A freight quote needs a positive quantity — a load of nothing has no trips to price."; return false; }
            string cargoRefusal = FreightCargoCapacity.ValidateCargoSpec(cargo);
            if (cargoRefusal != null) { reason = cargoRefusal; return false; }
            string wagonRefusal = FreightCargoCapacity.ValidateWagonCapacity(wagonMassCapacityKg, wagonVolumeCapacityM3);
            if (wagonRefusal != null) { reason = wagonRefusal; return false; }
            if (float.IsNaN(conditionMultiplier) || float.IsInfinity(conditionMultiplier) || conditionMultiplier < 0f)
            { reason = "Freight road-condition multiplier must be a finite, non-negative value."; return false; }
            double multiplier = conditionMultiplier == 0f ? 1d : (double)conditionMultiplier;

            int unitsPerTrip = FreightCargoCapacity.MaxUnitsPerTrip(cargo, wagonMassCapacityKg, wagonVolumeCapacityM3);
            if (unitsPerTrip <= 0)
            { reason = "The wagon cannot carry even one unit of this cargo — no trip to quote."; return false; }
            // Ceiling division in long arithmetic: perTrip can be
            // int.MaxValue (a cargo that binds on neither dimension),
            // and quantityUnits + perTrip - 1 overflows an int there.
            int trips = (int)((quantityUnits + (long)unitsPerTrip - 1) / unitsPerTrip);

            double baseChargeable = chargeRoundTrip ? Math.Ceiling(oneWayDistanceMiles * 2d) : Math.Ceiling(oneWayDistanceMiles);
            if (baseChargeable <= 0d || baseChargeable > int.MaxValue)
            { reason = "Chargeable mileage is outside supported whole-mile bounds."; return false; }
            double conditionedChargeable = Math.Ceiling(baseChargeable * multiplier);
            if (conditionedChargeable <= 0d || conditionedChargeable > int.MaxValue)
            { reason = "Chargeable mileage is outside supported whole-mile bounds."; return false; }
            long mileagePerTrip;
            long perTripTotal;
            long total;
            try
            {
                mileagePerTrip = checked((long)conditionedChargeable * ratePerMileCents);
                perTripTotal = checked(mileagePerTrip + baseFeeCents);
                total = checked(perTripTotal * trips);
            }
            catch (OverflowException)
            {
                reason = "Calculated freight quote exceeds supported integer-cent currency bounds.";
                return false;
            }
            if (mileagePerTrip > int.MaxValue || perTripTotal > int.MaxValue || total > int.MaxValue)
            { reason = "Calculated freight quote exceeds supported integer-cent currency bounds."; return false; }

            float cargoMassKg = quantityUnits * cargo.MassKgPerUnit;
            float cargoVolumeM3 = quantityUnits * cargo.VolumeM3PerUnit;
            string terms = trips + (trips == 1 ? " trip, " : " trips, ")
                + quantityUnits + "u total (" + cargoMassKg.ToString("0.0") + "kg, " + cargoVolumeM3.ToString("0.00") + "m3); "
                + baseFeeCents + "c dispatch + " + ratePerMileCents + "c/mi "
                + (chargeRoundTrip ? "round trip" : "one-way")
                + "; total " + total + "c (" + perTripTotal + "c per trip)";
            if (multiplier != 1d)
                terms += "; road conditions " + conditionMultiplier.ToString("0.##") + "x, "
                    + (int)conditionedChargeable + " billed miles for " + (int)baseChargeable + " route miles";
            quote = new FreightServiceQuote
            {
                QuoteId = string.Empty,
                OneWayDistanceMiles = oneWayDistanceMiles,
                ChargeableMiles = (int)conditionedChargeable,
                BaseServiceFeeCents = baseFeeCents,
                RatePerMileCents = ratePerMileCents,
                MileageChargeCents = (int)mileagePerTrip,
                TotalPriceCents = (int)total,
                DistanceBasis = chargeRoundTrip ? "Expected round trip, loaded outward and empty return" : "One-way route",
                DistanceRounding = "Each chargeable mile rounded up to a whole mile",
                PaymentTiming = "After successful delivery",
                TripCount = trips,
                UnitsPerTrip = unitsPerTrip,
                TotalQuantityUnits = quantityUnits,
                CargoMassKg = cargoMassKg,
                CargoVolumeM3 = cargoVolumeM3,
                PerTripTotalCents = (int)perTripTotal,
                ConditionMultiplier = (float)multiplier,
                TermsSummary = terms,
            };
            return true;
        }
    }
}
