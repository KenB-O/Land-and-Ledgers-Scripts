using System;
using System.Collections.Generic;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Financing
{
    public enum CreditOfferStatus
    {
        Proposed = 0,
        Countered = 1,
        Accepted = 2,
        Declined = 3,
        Expired = 4,
        Refused = 5,
        CounterAccepted = 6,
    }

    public enum CreditOfferNegotiationAction
    {
        Submitted = 0,
        Offered = 1,
        Countered = 2,
        CounterAccepted = 3,
        CounterDeclined = 4,
        BorrowerDeclined = 5,
        Accepted = 6,
    }

    [Serializable]
    public sealed class CreditRequest
    {
        public string RequestId = string.Empty;
        public string Borrower = string.Empty;
        public string Lender = string.Empty;
        public int RequestedAmountCents;
        public int RequiredCashContributionCents;
        public string Purpose = string.Empty;
        public int DesiredTermDays;
        public int RequiredFundingDayIndex;
        public string RepaymentSource = string.Empty;
        public string ProposedCollateral = string.Empty;
        public string ProposedGuarantor = string.Empty;
        public string OriginatingDealId = string.Empty;
        public List<string> SuppliedInformation = new List<string>();
    }

    [Serializable]
    public sealed class CreditEvidenceRecord
    {
        public string EvidenceId = string.Empty;
        public string Borrower = string.Empty;
        public string ObligationId = string.Empty;
        public string Source = string.Empty;
        public string Description = string.Empty;
        public bool DisclosedToLender;
        public int DayIndex;
    }

    [Serializable]
    public sealed class CreditOffer
    {
        public string OfferId = string.Empty;
        public string RequestId = string.Empty;
        public string Borrower = string.Empty;
        public string Lender = string.Empty;
        public string Purpose = string.Empty;
        public string OriginatingDealId = string.Empty;
        public int RequestedAmountCents;
        public int OfferedAmountCents;
        public int RequiredCashContributionCents;
        public int AnnualInterestRateBps;
        public int TermDays;
        public FinancialPaymentStructure PaymentStructure = FinancialPaymentStructure.MaturityPrincipal;
        public int PaymentIntervalDays;
        public int FeesCents;
        public string SecurityRequired = string.Empty;
        public string GuarantorRequired = string.Empty;
        public string Conditions = string.Empty;
        public int ExpiryDayIndex = -1;
        public bool StagedAdvances;
        public CreditOfferStatus Status = CreditOfferStatus.Proposed;
        public string DecisionReason = string.Empty;
        public string ResultingObligationId = string.Empty;
        public List<string> EvidenceConsidered = new List<string>();
        public List<CreditOfferNegotiationRecord> NegotiationHistory = new List<CreditOfferNegotiationRecord>();
    }

    [Serializable]
    public sealed class CreditOfferNegotiationRecord
    {
        public CreditOfferNegotiationAction Action;
        public string Party = string.Empty;
        public int OfferedAmountCents;
        public int AnnualInterestRateBps;
        public int TermDays;
        public string Conditions = string.Empty;
        public int DayIndex = -1;
    }

    /// <summary>
    /// Shared request, evidence, negotiation and closing workflow. It does not
    /// own debt balances: accepted offers create one FinancialObligation and
    /// cash moves through the explicit lender/borrower accounts supplied by
    /// the caller.
    /// </summary>
    public sealed class CreditOfferWorkflow
    {
        private readonly Dictionary<string, CreditRequest> requests = new(StringComparer.Ordinal);
        private readonly Dictionary<string, CreditOffer> offers = new(StringComparer.Ordinal);
        private readonly List<CreditEvidenceRecord> evidence = new();

        public IReadOnlyCollection<CreditRequest> Requests => requests.Values;
        public IReadOnlyCollection<CreditOffer> Offers => offers.Values;
        public IReadOnlyList<CreditEvidenceRecord> Evidence => evidence;

        public CreditOffer FindOffer(string offerId)
        {
            return !string.IsNullOrWhiteSpace(offerId) && offers.TryGetValue(offerId, out CreditOffer offer)
                ? offer : null;
        }

        public CreditRequest FindRequest(string requestId)
        {
            return !string.IsNullOrWhiteSpace(requestId) && requests.TryGetValue(requestId, out CreditRequest request)
                ? request : null;
        }

        public CreditRequest SubmitRequest(EntityIdRegistry ids, string borrower, string lender,
            int amountCents, string purpose, int desiredTermDays, int fundingDayIndex,
            string repaymentSource, string collateral = "", string guarantor = "", string dealId = "")
        {
            if (ids == null || string.IsNullOrWhiteSpace(borrower) || string.IsNullOrWhiteSpace(lender) || amountCents <= 0) return null;
            EntityId id = ids.Allocate(EntityKind.Contract);
            var request = new CreditRequest
            {
                RequestId = id.ToString(), Borrower = borrower, Lender = lender,
                RequestedAmountCents = amountCents, Purpose = purpose ?? string.Empty,
                DesiredTermDays = Math.Max(0, desiredTermDays), RequiredFundingDayIndex = fundingDayIndex,
                RepaymentSource = repaymentSource ?? string.Empty, ProposedCollateral = collateral ?? string.Empty,
                ProposedGuarantor = guarantor ?? string.Empty, OriginatingDealId = dealId ?? string.Empty
            };
            requests[request.RequestId] = request;
            return request;
        }

        public CreditEvidenceRecord DiscloseEvidence(EntityIdRegistry ids, CreditRequest request,
            string source, string description, string obligationId, int dayIndex)
        {
            if (ids == null || request == null || string.IsNullOrWhiteSpace(source)) return null;
            var record = new CreditEvidenceRecord
            {
                EvidenceId = ids.Allocate(EntityKind.Contract).ToString(), Borrower = request.Borrower,
                ObligationId = obligationId ?? string.Empty, Source = source, Description = description ?? string.Empty,
                DisclosedToLender = true, DayIndex = dayIndex
            };
            evidence.Add(record);
            return record;
        }

        public CreditOffer Evaluate(EntityIdRegistry ids, CreditRequest request,
            FinancialObligationAuthority authority, int lenderAvailableCents, int dayIndex)
        {
            if (ids == null || request == null) return null;
            int knownDebt = 0;
            var considered = new List<string>();
            foreach (CreditEvidenceRecord record in evidence)
            {
                if (record != null && record.DisclosedToLender && string.Equals(record.Borrower, request.Borrower, StringComparison.Ordinal))
                {
                    considered.Add(record.EvidenceId);
                    if (authority != null && !string.IsNullOrWhiteSpace(record.ObligationId))
                        knownDebt += authority.Find(record.ObligationId)?.TotalOutstandingCents ?? 0;
                }
            }

            EntityId id = ids.Allocate(EntityKind.Contract);
            int offered = Math.Min(request.RequestedAmountCents, Math.Max(0, lenderAvailableCents));
            string reason = string.Empty;
            if (knownDebt > 0)
            {
                offered = Math.Min(offered, request.RequestedAmountCents / 2);
                reason = "Known disclosed obligation reduced the offer.";
            }
            if (offered <= 0)
            {
                var refused = new CreditOffer
                {
                    OfferId = id.ToString(), RequestId = request.RequestId, Borrower = request.Borrower,
                    Lender = request.Lender, Purpose = request.Purpose, OriginatingDealId = request.OriginatingDealId,
                    RequestedAmountCents = request.RequestedAmountCents,
                    Status = CreditOfferStatus.Refused, DecisionReason = "Lender liquidity is insufficient."
                };
                refused.EvidenceConsidered.AddRange(considered);
                refused.NegotiationHistory.Add(new CreditOfferNegotiationRecord
                {
                    Action = CreditOfferNegotiationAction.Offered, Party = request.Lender,
                    OfferedAmountCents = 0, DayIndex = dayIndex
                });
                offers[refused.OfferId] = refused; return refused;
            }
            var offer = new CreditOffer
            {
                OfferId = id.ToString(), RequestId = request.RequestId, Borrower = request.Borrower,
                Lender = request.Lender, RequestedAmountCents = request.RequestedAmountCents,
                Purpose = request.Purpose, OriginatingDealId = request.OriginatingDealId,
                OfferedAmountCents = offered, RequiredCashContributionCents = request.RequiredCashContributionCents,
                AnnualInterestRateBps = 700, TermDays = Math.Max(1, request.DesiredTermDays),
                PaymentStructure = FinancialPaymentStructure.MaturityPrincipal,
                SecurityRequired = request.ProposedCollateral, GuarantorRequired = request.ProposedGuarantor,
                ExpiryDayIndex = dayIndex + 7, DecisionReason = reason.Length > 0 ? reason : "Offer fits lender liquidity and disclosed evidence."
            };
            offer.EvidenceConsidered.AddRange(considered);
            offer.NegotiationHistory.Add(new CreditOfferNegotiationRecord
            {
                Action = CreditOfferNegotiationAction.Offered, Party = request.Lender,
                OfferedAmountCents = offered, AnnualInterestRateBps = offer.AnnualInterestRateBps,
                TermDays = offer.TermDays, DayIndex = dayIndex
            });
            offers[offer.OfferId] = offer; return offer;
        }

        public bool CounterOffer(string offerId, int offeredAmountCents, int annualInterestRateBps,
            int termDays, string conditions, out string message, int dayIndex = -1)
        {
            message = string.Empty;
            if (!offers.TryGetValue(offerId ?? string.Empty, out CreditOffer offer)
                || offer.Status != CreditOfferStatus.Proposed)
            { message = "Offer is unavailable for counteroffer."; return false; }
            if (offer.ExpiryDayIndex >= 0 && dayIndex >= 0 && dayIndex > offer.ExpiryDayIndex)
            {
                offer.Status = CreditOfferStatus.Expired;
                message = "Offer has expired.";
                return false;
            }
            if (offeredAmountCents <= 0 || offeredAmountCents > offer.RequestedAmountCents)
            { message = "Counter amount is outside the request."; return false; }
            offer.OfferedAmountCents = offeredAmountCents; offer.AnnualInterestRateBps = Math.Max(0, annualInterestRateBps);
            offer.TermDays = Math.Max(1, termDays); offer.Conditions = conditions ?? string.Empty;
            offer.Status = CreditOfferStatus.Countered; offer.DecisionReason = "Borrower counteroffer awaiting lender acceptance.";
            offer.NegotiationHistory.Add(new CreditOfferNegotiationRecord
            {
                Action = CreditOfferNegotiationAction.Countered, Party = offer.Borrower,
                OfferedAmountCents = offer.OfferedAmountCents, AnnualInterestRateBps = offer.AnnualInterestRateBps,
                TermDays = offer.TermDays, Conditions = offer.Conditions, DayIndex = dayIndex
            });
            return true;
        }

        public bool AcceptCounterOffer(string offerId, string lender, int dayIndex, out string message)
        {
            message = string.Empty;
            if (!offers.TryGetValue(offerId ?? string.Empty, out CreditOffer offer)
                || offer.Status != CreditOfferStatus.Countered
                || !string.Equals(offer.Lender, lender, StringComparison.Ordinal))
            {
                message = "Counteroffer is unavailable to this lender.";
                return false;
            }
            offer.Status = CreditOfferStatus.CounterAccepted;
            offer.DecisionReason = "Lender accepted the negotiated counteroffer.";
            offer.NegotiationHistory.Add(new CreditOfferNegotiationRecord
            {
                Action = CreditOfferNegotiationAction.CounterAccepted, Party = lender,
                OfferedAmountCents = offer.OfferedAmountCents, AnnualInterestRateBps = offer.AnnualInterestRateBps,
                TermDays = offer.TermDays, Conditions = offer.Conditions, DayIndex = dayIndex
            });
            return true;
        }

        public bool DeclineCounterOffer(string offerId, string lender, string reason, int dayIndex = -1)
        {
            if (!offers.TryGetValue(offerId ?? string.Empty, out CreditOffer offer)
                || offer.Status != CreditOfferStatus.Countered
                || !string.Equals(offer.Lender, lender, StringComparison.Ordinal)) return false;
            offer.Status = CreditOfferStatus.Refused;
            offer.DecisionReason = reason ?? "Lender declined the counteroffer.";
            offer.NegotiationHistory.Add(new CreditOfferNegotiationRecord
            {
                Action = CreditOfferNegotiationAction.CounterDeclined, Party = lender,
                OfferedAmountCents = offer.OfferedAmountCents, AnnualInterestRateBps = offer.AnnualInterestRateBps,
                TermDays = offer.TermDays, Conditions = offer.Conditions, DayIndex = dayIndex
            });
            return true;
        }

        public bool Decline(string offerId, string reason)
        {
            if (!offers.TryGetValue(offerId ?? string.Empty, out CreditOffer offer) || offer.Status == CreditOfferStatus.Accepted) return false;
            offer.Status = CreditOfferStatus.Declined;
            offer.DecisionReason = reason ?? "Offer declined.";
            offer.NegotiationHistory.Add(new CreditOfferNegotiationRecord
            {
                Action = CreditOfferNegotiationAction.BorrowerDeclined,
                Party = offer.Borrower,
                OfferedAmountCents = offer.OfferedAmountCents,
                AnnualInterestRateBps = offer.AnnualInterestRateBps,
                TermDays = offer.TermDays,
                Conditions = offer.Conditions,
                DayIndex = -1
            });
            return true;
        }

        public FinancialObligation Accept(EntityIdRegistry ids, string offerId, FinancialObligationAuthority authority,
            CreditCashAccount lenderCash, CreditCashAccount borrowerCash, int dayIndex)
        {
            if (ids == null || authority == null || !offers.TryGetValue(offerId ?? string.Empty, out CreditOffer offer)
                || (offer.Status != CreditOfferStatus.Proposed && offer.Status != CreditOfferStatus.CounterAccepted)
                || offer.OfferedAmountCents <= 0 || lenderCash == null || borrowerCash == null
                || lenderCash.BalanceCents < offer.OfferedAmountCents)
                return null;
            if (offer.ExpiryDayIndex >= 0 && dayIndex > offer.ExpiryDayIndex) { offer.Status = CreditOfferStatus.Expired; return null; }
            lenderCash.BalanceCents -= offer.OfferedAmountCents;
            borrowerCash.BalanceCents += offer.OfferedAmountCents;
            FinancialObligation obligation = authority.CreateWithTerms(ids, FinancialObligationKind.Loan,
                offer.Borrower, offer.Lender, offer.OfferedAmountCents, dayIndex,
                offer.Conditions, "accepted credit offer", new FinancialPaymentTerms
                {
                    AnnualInterestRateBps = offer.AnnualInterestRateBps,
                    Structure = offer.PaymentStructure, PaymentIntervalDays = offer.PaymentIntervalDays
                });
            if (obligation == null) { lenderCash.BalanceCents += offer.OfferedAmountCents; borrowerCash.BalanceCents -= offer.OfferedAmountCents; return null; }
            if (!string.IsNullOrWhiteSpace(offer.SecurityRequired))
                authority.AttachSecurity(ids, obligation.ObligationId, offer.Lender, offer.Borrower,
                    new[] { offer.SecurityRequired }, dayIndex, 1, true);
            if (!string.IsNullOrWhiteSpace(offer.GuarantorRequired))
                authority.AddGuaranty(ids, obligation.ObligationId, offer.Lender, offer.Borrower,
                    offer.GuarantorRequired, obligation.TotalOutstandingCents);
            offer.Status = CreditOfferStatus.Accepted; offer.ResultingObligationId = obligation.ObligationId;
            offer.NegotiationHistory.Add(new CreditOfferNegotiationRecord
            {
                Action = CreditOfferNegotiationAction.Accepted, Party = offer.Borrower,
                OfferedAmountCents = offer.OfferedAmountCents, AnnualInterestRateBps = offer.AnnualInterestRateBps,
                TermDays = offer.TermDays, Conditions = offer.Conditions, DayIndex = dayIndex
            });
            return obligation;
        }

        /// <summary>
        /// Closes a deferred-consideration offer such as a seller note.  No
        /// cash advance is made because the seller is accepting a claim as
        /// consideration at closing; the same negotiated offer and shared
        /// obligation authority are still used.
        /// </summary>
        public FinancialObligation AcceptDeferredConsideration(EntityIdRegistry ids, string offerId,
            FinancialObligationAuthority authority, int dayIndex, FinancialObligationKind kind,
            string purpose, string agreementId = "")
        {
            if (ids == null || authority == null || !offers.TryGetValue(offerId ?? string.Empty, out CreditOffer offer)
                || (offer.Status != CreditOfferStatus.Proposed && offer.Status != CreditOfferStatus.CounterAccepted)
                || offer.OfferedAmountCents <= 0) return null;
            if (offer.ExpiryDayIndex >= 0 && dayIndex > offer.ExpiryDayIndex)
            {
                offer.Status = CreditOfferStatus.Expired;
                return null;
            }
            FinancialObligation obligation = authority.CreateWithTerms(ids, kind,
                offer.Borrower, offer.Lender, offer.OfferedAmountCents, dayIndex,
                offer.Conditions, purpose ?? string.Empty, new FinancialPaymentTerms
                {
                    AnnualInterestRateBps = offer.AnnualInterestRateBps,
                    Structure = offer.PaymentStructure,
                    PaymentIntervalDays = offer.PaymentIntervalDays,
                    AllowsEarlyPayoff = true
                }, agreementId);
            if (obligation == null) return null;
            if (!string.IsNullOrWhiteSpace(offer.SecurityRequired))
                authority.AttachSecurity(ids, obligation.ObligationId, offer.Lender, offer.Borrower,
                    new[] { offer.SecurityRequired }, dayIndex, 1, true);
            if (!string.IsNullOrWhiteSpace(offer.GuarantorRequired))
                authority.AddGuaranty(ids, obligation.ObligationId, offer.Lender, offer.Borrower,
                    offer.GuarantorRequired, obligation.TotalOutstandingCents);
            offer.Status = CreditOfferStatus.Accepted;
            offer.ResultingObligationId = obligation.ObligationId;
            offer.NegotiationHistory.Add(new CreditOfferNegotiationRecord
            {
                Action = CreditOfferNegotiationAction.Accepted, Party = offer.Borrower,
                OfferedAmountCents = offer.OfferedAmountCents, AnnualInterestRateBps = offer.AnnualInterestRateBps,
                TermDays = offer.TermDays, Conditions = offer.Conditions, DayIndex = dayIndex
            });
            return obligation;
        }

        /// <summary>
        /// Executes a borrower payment against the shared obligation and moves
        /// the same amount between explicit cash accounts.  This is deliberately
        /// separate from FinancialObligationAuthority.ApplyPayment so callers
        /// cannot accidentally reduce a balance without recording cash.
        /// </summary>
        public FinancialPaymentRecord CollectPayment(string obligationId, int amountCents,
            int dayIndex, FinancialObligationAuthority authority,
            CreditCashAccount borrowerCash, CreditCashAccount creditorCash,
            out string message)
        {
            message = string.Empty;
            FinancialObligation obligation = authority?.Find(obligationId);
            if (obligation == null)
            {
                message = "Unknown financial obligation.";
                return null;
            }
            if (borrowerCash == null || creditorCash == null
                || !string.Equals(borrowerCash.Owner, obligation.Debtor, StringComparison.Ordinal)
                || !string.Equals(creditorCash.Owner, obligation.Creditor, StringComparison.Ordinal))
            {
                message = "Cash accounts do not match the obligation parties.";
                return null;
            }
            if (amountCents <= 0 || borrowerCash.BalanceCents < amountCents)
            {
                message = "Borrower cash is insufficient for this payment.";
                return null;
            }
            FinancialPaymentRecord payment = authority.ApplyPayment(
                obligationId, amountCents, dayIndex, obligation.Debtor,
                obligation.Creditor, "credit-payment:" + obligationId + ":" + dayIndex);
            if (payment == null)
            {
                message = "Payment is not valid for the obligation's current balance.";
                return null;
            }
            borrowerCash.BalanceCents -= payment.AmountCents;
            creditorCash.BalanceCents += payment.AmountCents;
            return payment;
        }

        /// <summary>Executes the contractually due amount when the borrower can pay.</summary>
        public FinancialPaymentRecord CollectScheduledPayment(string obligationId,
            int dayIndex, FinancialObligationAuthority authority,
            CreditCashAccount borrowerCash, CreditCashAccount creditorCash,
            out string message)
        {
            message = string.Empty;
            FinancialObligation obligation = authority?.Find(obligationId);
            if (obligation == null)
            {
                message = "Unknown financial obligation.";
                return null;
            }
            int due = authority.CalculateDueAmountCents(obligationId, dayIndex);
            if (due <= 0)
            {
                message = "No contractual payment is due on this day.";
                return null;
            }
            return CollectPayment(obligationId, due, dayIndex, authority,
                borrowerCash, creditorCash, out message);
        }

        public CreditOfferWorkflowSaveDto CaptureSaveDto()
        {
            var dto = new CreditOfferWorkflowSaveDto();
            dto.Requests.AddRange(requests.Values);
            dto.Offers.AddRange(offers.Values);
            dto.Evidence.AddRange(evidence);
            return dto;
        }

        public void LoadFromSaveDto(CreditOfferWorkflowSaveDto dto)
        {
            requests.Clear(); offers.Clear(); evidence.Clear();
            if (dto == null) return;
            if (dto.Requests != null) foreach (CreditRequest value in dto.Requests)
                if (value != null && !string.IsNullOrWhiteSpace(value.RequestId)) requests[value.RequestId] = value;
            if (dto.Offers != null) foreach (CreditOffer value in dto.Offers)
                if (value != null && !string.IsNullOrWhiteSpace(value.OfferId)) offers[value.OfferId] = value;
            if (dto.Evidence != null) evidence.AddRange(dto.Evidence);
        }
    }

    [Serializable]
    public sealed class CreditCashAccount
    {
        public string Owner = string.Empty;
        public int BalanceCents;
        public CreditCashAccount(string owner, int balanceCents) { Owner = owner ?? string.Empty; BalanceCents = Math.Max(0, balanceCents); }
    }

    [Serializable]
    public sealed class CreditOfferWorkflowSaveDto
    {
        public List<CreditRequest> Requests = new();
        public List<CreditOffer> Offers = new();
        public List<CreditEvidenceRecord> Evidence = new();
    }
}
