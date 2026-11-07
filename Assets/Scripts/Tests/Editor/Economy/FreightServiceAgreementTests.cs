using LandLedgers.Economy.Freight;
using LandLedgers.Economy;
using LogisticsShipmentState = global::LandLedgers.Economy.LogisticsShipmentState;
using LogisticsShipmentStatus = global::LandLedgers.Economy.LogisticsShipmentStatus;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    public sealed class FreightServiceAgreementTests
    {
        [Test]
        public void QuoteCalculator_ChargesAuthoredRoundTripAndOptionalBaseFeeInWholeCents()
        {
            Assert.IsTrue(FreightServiceQuoteCalculator.TryCalculate(50f, 25, 1, true,
                out FreightServiceQuote quote, out string reason), reason);
            Assert.AreEqual(50f, quote.OneWayDistanceMiles);
            Assert.AreEqual(100, quote.ChargeableMiles);
            Assert.AreEqual(100, quote.MileageChargeCents);
            Assert.AreEqual(125, quote.TotalPriceCents);
            Assert.AreEqual("Expected round trip, loaded outward and empty return", quote.DistanceBasis);

            Assert.IsTrue(FreightServiceQuoteCalculator.TryCalculate(50f, 0, 1, false,
                out FreightServiceQuote noDispatchFee, out reason), reason);
            Assert.AreEqual(50, noDispatchFee.ChargeableMiles);
            Assert.AreEqual(50, noDispatchFee.TotalPriceCents);
        }

        [Test]
        public void QuoteCalculator_UsesExplicitWholeMileCeilingAndRejectsInvalidInputs()
        {
            Assert.IsTrue(FreightServiceQuoteCalculator.TryCalculate(1.01f, 0, 1, true,
                out FreightServiceQuote quote, out string reason), reason);
            Assert.AreEqual(3, quote.ChargeableMiles);
            Assert.AreEqual(3, quote.TotalPriceCents);
            Assert.IsFalse(FreightServiceQuoteCalculator.TryCalculate(0f, 25, 1, true, out _, out _));
            Assert.IsFalse(FreightServiceQuoteCalculator.TryCalculate(5f, -1, 1, true, out _, out _));
        }

        [Test]
        public void AgreementBook_RefusesDuplicateRequestAndAcceptanceAndPreservesQuotedTerms()
        {
            var book = new FreightServiceAgreementBook();
            var request = new FreightServiceRequest
            {
                RequestId = "request-1", RequestingBusinessId = "bakery-1",
                CarrierBusinessId = "freight-1", PickupBusinessId = "millbrook",
                DestinationBusinessId = "bakery-1", CargoLotId = "lot-1", Quantity = 1,
                RequestingPersonId = 4, CarrierOperatorPersonId = 7,
                PickupLocationId = "millbrook-pickup", DestinationLocationId = "bakery-door"
            };
            Assert.IsNull(book.CreateRequest(request, out _));
            Assert.That(book.CreateRequest(new FreightServiceRequest
            {
                RequestId = "request-1", RequestingBusinessId = "bakery-1",
                CarrierBusinessId = "freight-1", PickupBusinessId = "millbrook",
                DestinationBusinessId = "bakery-1", CargoLotId = "lot-2", Quantity = 1
            }, out _), Does.Contain("already exists"));

            FreightServiceQuote quote = new FreightServiceQuote
            {
                QuoteId = "quote-1", OneWayDistanceMiles = 50, ChargeableMiles = 100,
                BaseServiceFeeCents = 25, RatePerMileCents = 1, MileageChargeCents = 100,
                TotalPriceCents = 125, PayerBusinessId = "bakery-1"
            };
            Assert.IsNull(book.SetQuote(request.RequestId, quote));
            Assert.IsNull(book.Accept(request.RequestId, 7, 2, 3600f, out FreightServiceAgreement agreement));
            Assert.AreEqual("freight-agreement:request-1", agreement.AgreementId);
            Assert.AreEqual(125, agreement.AgreedPriceCents);
            Assert.AreEqual("quote-1", agreement.QuoteId);
            quote.TotalPriceCents = 999;
            Assert.AreEqual(125, agreement.AgreedPriceCents, "Acceptance keeps a snapshot of the quoted price.");
            Assert.That(book.Accept(request.RequestId, 7, 2, 3600f, out _), Does.Contain("No presented"));
            Assert.That(book.CreateRequest(new FreightServiceRequest
            {
                RequestId = "request-2", RequestingBusinessId = "bakery-1",
                CarrierBusinessId = "freight-1", PickupBusinessId = "millbrook",
                DestinationBusinessId = "bakery-1", CargoLotId = "lot-1", Quantity = 1
            }, out _), Does.Contain("already has request"));
        }

        [Test]
        public void AgreementBook_TemporarilyDefersBusyCarrierWithoutLosingStableRequest()
        {
            var book = new FreightServiceAgreementBook();
            var request = new FreightServiceRequest
            {
                RequestId = "request-repeat-1", RequestingBusinessId = "bakery-1",
                CarrierBusinessId = "freight-1", PickupBusinessId = "millbrook",
                DestinationBusinessId = "bakery-1", CargoLotId = "lot-repeat-1", Quantity = 1,
                RequestingPersonId = 4, CarrierOperatorPersonId = 7,
                MeetingTaskId = "T40", ContactTravelTaskId = "T39",
                MeetingOperatorReservationId = "reservation-ethan-40"
            };
            Assert.IsNull(book.CreateRequest(request, out _));
            Assert.IsNull(book.SetStatus(request.RequestId, FreightServiceRequestStatus.MeetingScheduled));

            Assert.IsNull(book.DeferTemporarily(request.RequestId,
                "Wagon is already bound to an active shipment."));
            Assert.AreEqual(FreightServiceRequestStatus.Open, request.Status);
            Assert.AreEqual("Wagon is already bound to an active shipment.", request.RefusalReason);
            Assert.IsEmpty(request.MeetingTaskId);
            Assert.IsEmpty(request.MeetingOperatorReservationId);
            Assert.IsEmpty(request.ContactTravelTaskId);
            Assert.IsNull(book.FindAgreementForRequest(request.RequestId));

            // A later meeting can present and accept the same stable request;
            // the deferral creates neither a second request nor an agreement.
            Assert.IsNull(book.SetQuote(request.RequestId, new FreightServiceQuote
            {
                QuoteId = "quote-repeat-1", OneWayDistanceMiles = 50f, ChargeableMiles = 100,
                BaseServiceFeeCents = 25, RatePerMileCents = 1,
                MileageChargeCents = 100, TotalPriceCents = 125,
                PayerBusinessId = "bakery-1"
            }));
            Assert.IsNull(book.Accept(request.RequestId, 7, 3, 7200f, out FreightServiceAgreement agreement));
            Assert.AreEqual("freight-agreement:request-repeat-1", agreement.AgreementId);
            Assert.AreEqual("lot-repeat-1", agreement.CargoLotId);
            Assert.AreEqual(125, agreement.AgreedPriceCents);
            Assert.AreEqual(1, book.Requests.Count);
            Assert.AreEqual(1, book.Agreements.Count);
        }

        [Test]
        public void MeetingRefusalIsTerminalAndBricksThePaidLot()
        {
            // Plan 07 Slice 0 discriminator (AUD-334): a meeting refused for
            // a transient cause (bakery till below the 125c quote) leaves a
            // Refused request no driver path can ever reopen, and the paid
            // lot behind it can never ship.
            const string requestId = "freight-request:millbrook_grain_co:flour-reorder-d0-n1";
            const string lotId = "aidlot-millbrook_grain_co:flour-reorder-d0-n1";
            var book = new FreightServiceAgreementBook();
            var request = NewMillbrookRequest(requestId, lotId);
            Assert.IsNull(book.CreateRequest(request, out _));
            Assert.IsNull(book.SetQuote(requestId, New125Quote("quote-d0-n1")));
            Assert.IsNull(book.SetStatus(requestId, FreightServiceRequestStatus.MeetingScheduled));

            // Exactly as the drivers do at SimulationDrivers.cs:4468.
            Assert.IsNull(book.SetStatus(requestId, FreightServiceRequestStatus.Refused,
                "Bakery B1 has 40c available for a 125c agreed charge."));
            Assert.AreEqual(FreightServiceRequestStatus.Refused, request.Status);

            // Observation 2: SetStatus cannot move a Refused request back.
            string reopenRefusal = book.SetStatus(requestId, FreightServiceRequestStatus.Open);
            Assert.IsTrue(!string.IsNullOrWhiteSpace(reopenRefusal), "SetStatus must refuse a terminal request.");
            Assert.IsTrue(reopenRefusal.Contains("already terminal"), "refusal: " + reopenRefusal);
            Assert.AreEqual(FreightServiceRequestStatus.Refused, request.Status);

            // Observation 3: the deterministic request id cannot be recreated.
            Assert.That(book.CreateRequest(NewMillbrookRequest(requestId, "aidlot-other"), out _),
                Does.Contain("already exists"));

            // Observation 4: no agreement exists to advance.
            Assert.IsNull(book.FindAgreementForRequest(requestId));
        }

        [Test]
        public void DeferTemporarilyMechanicallyReopensARefusedRequest()
        {
            // Plan 07 Slice 0 seam proof: the book transition exists and
            // reopens a Refused request with no agreement; the defect is
            // that no driver ever calls it on one. This test must pass
            // before the repair starts.
            const string requestId = "freight-request:millbrook_grain_co:flour-reorder-d0-n1";
            const string lotId = "aidlot-millbrook_grain_co:flour-reorder-d0-n1";
            var book = new FreightServiceAgreementBook();
            var request = NewMillbrookRequest(requestId, lotId);
            request.MeetingTaskId = "T40";
            request.ContactTravelTaskId = "T39";
            request.MeetingOperatorReservationId = "reservation-ethan-40";
            Assert.IsNull(book.CreateRequest(request, out _));
            Assert.IsNull(book.SetStatus(requestId, FreightServiceRequestStatus.MeetingScheduled));
            Assert.IsNull(book.SetStatus(requestId, FreightServiceRequestStatus.Refused,
                "Bakery B1 has 40c available for a 125c agreed charge."));

            Assert.IsNull(book.DeferTemporarily(requestId,
                "Bakery till recovered; retry the meeting for the same lot."));
            Assert.AreEqual(FreightServiceRequestStatus.Open, request.Status);
            Assert.IsEmpty(request.MeetingTaskId);
            Assert.IsEmpty(request.ContactTravelTaskId);
            Assert.IsEmpty(request.MeetingOperatorReservationId);
            Assert.IsNull(book.FindAgreementForRequest(requestId));
        }

        [Test]
        public void CompletedAgreementNeverLeavesAccepted()
        {
            // Plan 07 Slice 0 discriminator (AUD-338): a completed,
            // delivered, collected agreement still carries Accepted status
            // because no production code ever assigns Fulfilled or
            // Cancelled (repo-wide grep: zero assignments in production,
            // no Cancel method on the book).
            FreightServiceAgreementBook book = CreateAcceptedBook(out FreightServiceAgreement agreement);
            Assert.IsNull(book.LinkShipment(agreement.AgreementId, "freight-shipment:request-1"));
            Assert.IsNull(book.MarkPreparedForDispatch(agreement.AgreementId, agreement.ShipmentId, 3600f));
            Assert.IsNull(book.RegisterDispatchTask(agreement.AgreementId, agreement.ShipmentId, "T20"));

            var completedShipment = NewPreparedShipment(
                "freight-shipment:request-1", agreement.AgreementId, LogisticsShipmentStatus.Completed);
            completedShipment.deliveryApplied = true;
            completedShipment.freightRevenueCollected = true;

            Assert.AreEqual(FreightServiceAgreementStatus.Accepted, agreement.Status,
                "A completed, delivered, collected agreement must still be Accepted until Slice 2 closes it.");
            Assert.IsNull(book.FindNextAwaitingPreparation(new[] { completedShipment }),
                "a completed agreement must not be selected for preparation again");
        }

        [Test]
        public void ReopenRefusedRequestRestoresOpenOnlyWhenShippable()
        {
            // Plan 07 Slice 1b (Q02, AUD-334): a Refused request with no
            // agreement reopens to Open with its stable id, lot, and quote
            // intact; every other state refuses.
            const string requestId = "freight-request:millbrook_grain_co:flour-reorder-d0-n1";
            const string lotId = "aidlot-millbrook_grain_co:flour-reorder-d0-n1";
            var book = new FreightServiceAgreementBook();
            var request = NewMillbrookRequest(requestId, lotId);
            request.MeetingTaskId = "T40";
            request.ContactTravelTaskId = "T39";
            request.MeetingOperatorReservationId = "reservation-ethan-40";
            Assert.IsNull(book.CreateRequest(request, out _));
            Assert.IsNull(book.SetStatus(requestId, FreightServiceRequestStatus.MeetingScheduled));
            Assert.IsNull(book.SetStatus(requestId, FreightServiceRequestStatus.Refused,
                "Bakery B1 has 40c available for a 125c agreed charge."));

            Assert.That(book.ReopenRefusedRequest(requestId, string.Empty),
                Does.Contain("requires its cause"));
            Assert.AreEqual(FreightServiceRequestStatus.Refused, request.Status,
                "an empty reason must not reopen the request");

            Assert.IsNull(book.ReopenRefusedRequest(requestId,
                "Lot '" + lotId + "' is still shippable; reopening the refused request for a retried meeting."));
            Assert.AreEqual(FreightServiceRequestStatus.Open, request.Status);
            Assert.IsEmpty(request.MeetingTaskId);
            Assert.IsEmpty(request.ContactTravelTaskId);
            Assert.IsEmpty(request.MeetingOperatorReservationId);

            Assert.IsNull(book.SetQuote(requestId, New125Quote("quote-d0-n1")));
            Assert.IsNull(book.Accept(requestId, 7, 3, 7200f, out FreightServiceAgreement agreement));
            Assert.AreEqual("freight-agreement:" + requestId, agreement.AgreementId);
            Assert.AreEqual(lotId, agreement.CargoLotId);
            Assert.AreEqual(125, agreement.AgreedPriceCents);
            Assert.AreEqual(1, book.Requests.Count);
            Assert.AreEqual(1, book.Agreements.Count);
        }

        [Test]
        public void ReopenRefusedRequestRefusesWhenAnAgreementExists()
        {
            // A Refused request that already has an agreement (reachable
            // through a save made mid-repair) must never reopen: the haul
            // already has an agreement to advance.
            FreightServiceAgreementBook book = CreateAcceptedBook(out FreightServiceAgreement agreement);
            FreightServiceAgreementBookSaveDto dto = book.CaptureSaveDto();
            dto.Requests[0].Status = FreightServiceRequestStatus.Refused;
            var restored = new FreightServiceAgreementBook();
            restored.RestoreSaveDto(dto);
            Assert.IsNotNull(restored.FindAgreementForRequest("request-1"));

            Assert.That(restored.ReopenRefusedRequest("request-1", "lot still shippable"),
                Does.Contain("agreement"));
            Assert.AreEqual(FreightServiceRequestStatus.Refused,
                restored.FindRequest("request-1").Status);
        }

        [Test]
        public void ReopenRefusedRequestRefusesAcceptedRequests()
        {
            FreightServiceAgreementBook book = CreateAcceptedBook(out _);
            Assert.That(book.ReopenRefusedRequest("request-1", "lot still shippable"),
                Does.Contain("Only a refused"));
            Assert.AreEqual(FreightServiceRequestStatus.Accepted,
                book.FindRequest("request-1").Status);
        }

        [Test]
        public void TemporaryMeetingCausesAlreadyDefer()
        {
            // Plan 07 Slice 0 control cases: the three classifier
            // substrings in IsTemporaryFreightCapacityRefusal already
            // defer through DeferTemporarily, so the repair is not
            // credited with fixing them.
            string[] temporaryReasons =
            {
                "Wagon is already bound to live shipment.",
                "Wagon 'opening-freight-wagon' is held by another equipment reservation.",
                "Ethan has an incompatible assigned or in-progress task at acceptance time."
            };
            foreach (string reason in temporaryReasons)
            {
                var book = new FreightServiceAgreementBook();
                var request = NewMillbrookRequest("request-control", "lot-control");
                Assert.IsNull(book.CreateRequest(request, out _));
                Assert.IsNull(book.SetStatus(request.RequestId, FreightServiceRequestStatus.MeetingScheduled));
                Assert.IsNull(book.DeferTemporarily(request.RequestId, reason),
                    "reason must defer: " + reason);
                Assert.AreEqual(FreightServiceRequestStatus.Open, request.Status);
            }
        }

        private static FreightServiceRequest NewMillbrookRequest(string requestId, string lotId)
        {
            return new FreightServiceRequest
            {
                RequestId = requestId, RequestingBusinessId = "bakery-1",
                CarrierBusinessId = "freight-1", PickupBusinessId = "millbrook",
                DestinationBusinessId = "bakery-1", CargoLotId = lotId, Quantity = 1,
                RequestingPersonId = 4, CarrierOperatorPersonId = 7,
                PickupLocationId = "millbrook-pickup", DestinationLocationId = "bakery-door"
            };
        }

        private static FreightServiceQuote New125Quote(string quoteId)
        {
            return new FreightServiceQuote
            {
                QuoteId = quoteId, OneWayDistanceMiles = 50, ChargeableMiles = 100,
                BaseServiceFeeCents = 25, RatePerMileCents = 1, MileageChargeCents = 100,
                TotalPriceCents = 125, PayerBusinessId = "bakery-1"
            };
        }

        [Test]
        public void AgreementBook_RefusesShipmentRelinkingAndCapturesPendingPaymentTerms()
        {
            var book = new FreightServiceAgreementBook();
            var request = new FreightServiceRequest
            {
                RequestId = "request-1", RequestingBusinessId = "bakery-1",
                CarrierBusinessId = "freight-1", PickupBusinessId = "millbrook",
                DestinationBusinessId = "bakery-1", CargoLotId = "lot-1", Quantity = 1,
                CarrierOperatorPersonId = 7
            };
            Assert.IsNull(book.CreateRequest(request, out _));
            Assert.IsNull(book.SetQuote("request-1", new FreightServiceQuote
            {
                QuoteId = "quote-1", OneWayDistanceMiles = 50, ChargeableMiles = 100,
                BaseServiceFeeCents = 25, RatePerMileCents = 1, MileageChargeCents = 100,
                TotalPriceCents = 125, PayerBusinessId = "bakery-1",
                PaymentTiming = "After successful delivery"
            }));
            Assert.IsNull(book.Accept("request-1", 7, 1, 1, out FreightServiceAgreement agreement));
            Assert.IsNull(book.LinkShipment(agreement.AgreementId, "shipment-1"));
            Assert.That(book.LinkShipment(agreement.AgreementId, "shipment-2"), Does.Contain("different shipment"));
            Assert.AreEqual("shipment-1", request.ShipmentId);
            Assert.AreEqual("After successful delivery", agreement.PaymentTiming);
            Assert.AreEqual("Pending pickup", agreement.FulfilmentStatus);

            FreightServiceAgreementBook restored = new FreightServiceAgreementBook();
            restored.RestoreSaveDto(book.CaptureSaveDto());
            FreightServiceAgreement restoredAgreement = restored.FindAgreement(agreement.AgreementId);
            Assert.IsNotNull(restoredAgreement);
            Assert.AreEqual(125, restoredAgreement.AgreedPriceCents);
            Assert.AreEqual("shipment-1", restoredAgreement.ShipmentId);
            Assert.AreEqual("shipment-1", restored.FindRequest("request-1").ShipmentId);
        }

        [Test]
        public void AgreementBook_RetainsAcceptedTermsAcrossRegistrationRefusalAndRetriesOnce()
        {
            FreightServiceAgreementBook book = CreateAcceptedBook(out FreightServiceAgreement agreement);
            Assert.AreEqual(125, agreement.AgreedPriceCents);
            const string refusal = "FreightShipmentService: wagon is temporarily reserved.";
            string first = book.TryRegisterShipment(agreement.AgreementId,
                (FreightServiceAgreement accepted, out string shipmentId, out string reason) =>
                {
                    shipmentId = string.Empty;
                    reason = refusal;
                    return false;
                }, out bool alreadyRegistered);
            Assert.IsFalse(alreadyRegistered);
            Assert.AreEqual(refusal, first);
            Assert.IsTrue(agreement.ShipmentRegistrationPending);
            Assert.AreEqual(refusal, agreement.ShipmentRegistrationFailureReason);
            Assert.AreEqual(FreightServiceAgreementStatus.Accepted, agreement.Status);
            Assert.AreEqual(125, agreement.AgreedPriceCents);

            int createCalls = 0;
            string retry = book.TryRegisterShipment(agreement.AgreementId,
                (FreightServiceAgreement accepted, out string shipmentId, out string reason) =>
                {
                    createCalls++;
                    shipmentId = "freight-shipment:request-1";
                    reason = string.Empty;
                    return true;
                }, out alreadyRegistered);
            Assert.IsNull(retry);
            Assert.IsFalse(alreadyRegistered);
            Assert.AreEqual(1, createCalls);
            Assert.AreEqual("freight-shipment:request-1", agreement.ShipmentId);
            Assert.IsFalse(agreement.ShipmentRegistrationPending);
            Assert.AreEqual(string.Empty, agreement.ShipmentRegistrationFailureReason);
            Assert.AreEqual(125, agreement.AgreedPriceCents);

            Assert.IsNull(book.TryRegisterShipment(agreement.AgreementId,
                (FreightServiceAgreement accepted, out string shipmentId, out string reason) =>
                {
                    createCalls++;
                    shipmentId = "duplicate";
                    reason = string.Empty;
                    return true;
                }, out alreadyRegistered));
            Assert.IsTrue(alreadyRegistered);
            Assert.AreEqual(1, createCalls, "A linked agreement must never invoke shipment creation again.");
            Assert.AreEqual("freight-shipment:request-1", agreement.ShipmentId);
        }

        [Test]
        public void AgreementBook_PreparationRequiresItsLinkedShipment()
        {
            FreightServiceAgreementBook book = CreateAcceptedBook(out FreightServiceAgreement agreement);
            Assert.That(book.MarkPreparedForDispatch(agreement.AgreementId, "unlinked"), Does.Contain("registered shipment"));
            Assert.IsNull(book.LinkShipment(agreement.AgreementId, "freight-shipment:request-1"));
            Assert.That(book.MarkPreparedForDispatch(agreement.AgreementId, agreement.ShipmentId, 3599f), Does.Contain("precedes agreement acceptance"));
            Assert.IsNull(book.MarkPreparedForDispatch(agreement.AgreementId, agreement.ShipmentId, 3600f));
            Assert.AreEqual("Prepared for dispatch", agreement.FulfilmentStatus);
            Assert.AreEqual(3600f, agreement.PreparedGameSeconds);

            Assert.IsNull(book.RegisterDispatchTask(agreement.AgreementId, agreement.ShipmentId, "T20"));
            Assert.AreEqual("T20", agreement.DispatchTaskId);
            Assert.AreEqual("Dispatch assigned", agreement.FulfilmentStatus);
            Assert.IsNull(book.RegisterDispatchTask(agreement.AgreementId, agreement.ShipmentId, "T20"),
                "Repeated registration of the same dispatch task is idempotent.");
            Assert.That(book.RegisterDispatchTask(agreement.AgreementId, agreement.ShipmentId, "T21"),
                Does.Contain("different dispatch task"));

            FreightServiceAgreementBook restored = new FreightServiceAgreementBook();
            restored.RestoreSaveDto(book.CaptureSaveDto());
            FreightServiceAgreement restoredAgreement = restored.FindAgreement(agreement.AgreementId);
            Assert.AreEqual(3600f, restoredAgreement.PreparedGameSeconds);
            Assert.AreEqual("T20", restoredAgreement.DispatchTaskId);
            Assert.AreEqual("Dispatch assigned", restoredAgreement.FulfilmentStatus);
        }

        [Test]
        public void AgreementBook_PreparationSelectionSkipsCompletedPriorAgreementAndFindsNextShipment()
        {
            FreightServiceAgreementBook book = CreateAcceptedBook(out FreightServiceAgreement first);
            Assert.IsNull(book.LinkShipment(first.AgreementId, "shipment-1"));
            first.PreparedGameSeconds = 100f;
            first.DispatchTaskId = "T10";
            first.FulfilmentStatus = "Dispatch assigned";

            FreightServiceAgreement second = CreateSecondAcceptedAgreement(book, "request-2", "lot-2", 200f);
            Assert.IsNull(book.LinkShipment(second.AgreementId, "shipment-2"));

            var firstShipment = NewPreparedShipment("shipment-1", first.AgreementId, LogisticsShipmentStatus.Completed);
            var secondShipment = NewPreparedShipment("shipment-2", second.AgreementId, LogisticsShipmentStatus.Planned);
            Assert.AreSame(second, book.FindNextAwaitingPreparation(new[] { firstShipment, secondShipment }));

            second.PreparedGameSeconds = 300f;
            second.FulfilmentStatus = "Prepared for dispatch";
            Assert.IsNull(book.FindNextAwaitingPreparation(new[] { firstShipment, secondShipment }),
                "already prepared agreements must not create another hitch task on a later daily pass");
        }

        private static FreightServiceAgreement CreateSecondAcceptedAgreement(
            FreightServiceAgreementBook book, string requestId, string lotId, float acceptedAt)
        {
            var request = new FreightServiceRequest
            {
                RequestId = requestId, RequestingBusinessId = "bakery-1",
                CarrierBusinessId = "freight-1", PickupBusinessId = "millbrook",
                DestinationBusinessId = "bakery-1", CargoLotId = lotId, Quantity = 1,
                RequestingPersonId = 4, CarrierOperatorPersonId = 7
            };
            Assert.IsNull(book.CreateRequest(request, out _));
            Assert.IsNull(book.SetQuote(requestId, new FreightServiceQuote
            {
                QuoteId = "quote-" + requestId, OneWayDistanceMiles = 50, ChargeableMiles = 100,
                BaseServiceFeeCents = 25, RatePerMileCents = 1, MileageChargeCents = 100,
                TotalPriceCents = 125, PayerBusinessId = "bakery-1"
            }));
            Assert.IsNull(book.Accept(requestId, 7, 1, acceptedAt, out FreightServiceAgreement agreement));
            return agreement;
        }

        private static LogisticsShipmentState NewPreparedShipment(
            string shipmentId, string agreementId, LogisticsShipmentStatus status)
        {
            return new LogisticsShipmentState
            {
                shipmentId = shipmentId,
                freightServiceAgreementId = agreementId,
                state = status,
                requiresDispatchAuthorization = true,
                loadApplied = false
            };
        }

        private static FreightServiceAgreementBook CreateAcceptedBook(out FreightServiceAgreement agreement)
        {
            FreightServiceAgreementBook book = new FreightServiceAgreementBook();
            var request = new FreightServiceRequest
            {
                RequestId = "request-1", RequestingBusinessId = "bakery-1",
                CarrierBusinessId = "freight-1", PickupBusinessId = "millbrook",
                DestinationBusinessId = "bakery-1", CargoLotId = "lot-1", Quantity = 1,
                RequestingPersonId = 4, CarrierOperatorPersonId = 7
            };
            Assert.IsNull(book.CreateRequest(request, out _));
            Assert.IsNull(book.SetQuote(request.RequestId, new FreightServiceQuote
            {
                QuoteId = "quote-1", OneWayDistanceMiles = 50, ChargeableMiles = 100,
                BaseServiceFeeCents = 25, RatePerMileCents = 1, MileageChargeCents = 100,
                TotalPriceCents = 125, PayerBusinessId = "bakery-1"
            }));
            Assert.IsNull(book.Accept(request.RequestId, 7, 1, 3600f, out agreement));
            return book;
        }
    }
}
