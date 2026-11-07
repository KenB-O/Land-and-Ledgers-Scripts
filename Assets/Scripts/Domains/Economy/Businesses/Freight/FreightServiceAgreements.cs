using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using UnityEngine;

namespace LandLedgers.Economy.Freight
{
    public enum FreightServiceRequestStatus { Open = 0, MeetingScheduled = 1, Quoted = 2, Refused = 3, Accepted = 4, AtMeeting = 5 }
    public enum FreightServiceAgreementStatus { Accepted = 0, Fulfilled = 1, Cancelled = 2, Refused = 3 }

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
        public FreightServiceQuote Quote;
    }

    [Serializable]
    public sealed class FreightServiceAgreement
    {
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

        public FreightServiceAgreement FindAgreement(string agreementId) => agreements.Find(a =>
            a != null && string.Equals(a.AgreementId, agreementId, StringComparison.Ordinal));

        public FreightServiceAgreement FindAgreementForRequest(string requestId) => agreements.Find(a =>
            a != null && string.Equals(a.RequestId, requestId, StringComparison.Ordinal));

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

        public string SetQuote(string requestId, FreightServiceQuote quote)
        {
            FreightServiceRequest request = FindRequest(requestId);
            if (request == null || quote == null || string.IsNullOrWhiteSpace(quote.QuoteId)
                || quote.OneWayDistanceMiles <= 0f || quote.ChargeableMiles <= 0
                || quote.BaseServiceFeeCents < 0 || quote.RatePerMileCents < 0
                || quote.MileageChargeCents < 0 || quote.TotalPriceCents < 0
                || (long)quote.BaseServiceFeeCents + quote.MileageChargeCents != quote.TotalPriceCents)
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
            string id = "freight-agreement:" + request.RequestId;
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
            agreement.Status = FreightServiceAgreementStatus.Fulfilled;
            agreement.FulfilmentStatus = "Fulfilled";
            return null;
        }

        /// <summary>
        /// Cancels an accepted agreement with its cause. Harness-pinned
        /// but uncalled: no live permanent-failure site that knows the
        /// agreement has been established (Plan 07 Slice 2 source review),
        /// so the caller question stays open and no caller is invented.
        /// A second transition is refused.
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
            if (!string.IsNullOrWhiteSpace(agreement.DispatchTaskId))
                return string.Equals(agreement.DispatchTaskId, taskId, StringComparison.Ordinal)
                    ? null : "Agreement already references a different dispatch task.";
            if (!string.Equals(agreement.FulfilmentStatus, "Prepared for dispatch", StringComparison.Ordinal))
                return "A dispatch task can only be registered once the shipment is prepared for dispatch.";
            agreement.DispatchTaskId = taskId;
            agreement.FulfilmentStatus = "Dispatch assigned";
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
                PaymentTiming = "After successful delivery"
            };
            return true;
        }
    }
}
