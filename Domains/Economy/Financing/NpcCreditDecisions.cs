using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using LandLedgers.World.Property;
using UnityEngine;

namespace LandLedgers.Economy.Financing
{
    /// <summary>
    /// Phase E (E4): what an NPC borrower wants and will accept. Both the
    /// request and the response execute through the real workflow — this
    /// profile only supplies the decision inputs.
    /// </summary>
    [Serializable]
    public sealed class NpcBorrowerProfile
    {
        /// <summary>Owner name as registered with the cash bridge (e.g. "household:7", "Abel").</summary>
        public string Name = string.Empty;
        public int DesiredAmountCents;
        public int MinimumAmountCents;
        public int MaxRateBps;
        public string Purpose = string.Empty;
        public int DesiredTermDays = 365;
        public string CollateralOffered = string.Empty;
        public string GuarantorOffered = string.Empty;
        /// <summary>An honest borrower discloses their real obligations; a desperate one may not.</summary>
        public bool HonestDisclosure = true;
        public bool WillCounter = true;

        public NpcBorrowerProfile() { }
    }

    /// <summary>
    /// Phase E (E4): an NPC creditor. Exactly one of Participant,
    /// PrivateLender or Bank is the executing side; Policy always describes
    /// how this lender differs.
    /// </summary>
    [Serializable]
    public sealed class NpcCreditorProfile
    {
        public string Name = string.Empty;
        public CreditParticipantService Participant;
        public PrivateLenderCreditParticipant PrivateLender;
        public BankCreditParticipant Bank;
        public CreditLenderPolicy Policy = CreditLenderPolicy.ForPrivateIndividual();
        public bool RelationshipMatters = true;

        public NpcCreditorProfile() { }
    }

    [Serializable]
    public sealed class NpcCreditLoopResult
    {
        public bool RequestFiled;
        public string RequestId = string.Empty;
        public bool OfferMade;
        public string OfferId = string.Empty;
        public bool Closed;
        public string ObligationId = string.Empty;
        public bool CashConserved;
        public int PaymentsCollected;
        public int PaymentsMissed;
        public FinancialObligationStatus FinalStatus;
        public int FinalOutstandingCents;
        public List<string> Notes = new List<string>();

        public NpcCreditLoopResult() { }
    }

    /// <summary>
    /// Phase E (E4): the full NPC credit loop, executed through the real
    /// systems — never stopped at NPC ids in an API. Request → evidence
    /// (disclosed only) → creditor evaluation → negotiation → legitimate
    /// acceptance → conserved cash movement → obligation → repayment →
    /// missed payment → creditor workout or enforcement. Both NPC creditor
    /// AND NPC borrower decisions execute.
    /// </summary>
    public sealed class NpcCreditDecisionEngine
    {
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>
        /// Legitimate relationship knowledge: the creditor's own history with
        /// this borrower from the shared authority — prior satisfied vs open
        /// obligations between the same two parties. This is NOT a universal
        /// credit score; it is what this creditor could actually know.
        /// </summary>
        public static void KnownRepaymentHistory(FinancialObligationAuthority authority,
            string borrower, string creditor, out int satisfiedCount, out int openOwedCents)
        {
            satisfiedCount = 0; openOwedCents = 0;
            if (authority == null) return;
            foreach (FinancialObligation obligation in authority.Obligations)
            {
                if (obligation == null) continue;
                if (!string.Equals(obligation.Debtor, borrower, StringComparison.Ordinal)) continue;
                if (!string.Equals(obligation.Creditor, creditor, StringComparison.Ordinal)) continue;
                if (obligation.Settled) satisfiedCount++;
                else openOwedCents += obligation.TotalOutstandingCents;
            }
        }

        public CreditRequest SubmitBorrowerRequest(EntityIdRegistry ids, CreditOfferWorkflow workflow,
            FinancialObligationAuthority authority, NpcBorrowerProfile borrower,
            NpcCreditorProfile creditor, int dayIndex, CreditEventLog events, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (ids == null || workflow == null || borrower == null || creditor == null)
            {
                diag.Add("NpcCredit: borrower request needs ids, workflow and both profiles.");
                return null;
            }
            CreditRequest request = workflow.SubmitRequest(ids, borrower.Name, creditor.Name,
                Math.Max(1, borrower.DesiredAmountCents), borrower.Purpose,
                Math.Max(1, borrower.DesiredTermDays), dayIndex,
                borrower.Name + " income and assets",
                borrower.CollateralOffered, borrower.GuarantorOffered, "");
            if (request == null)
            {
                diag.Add($"NpcCredit: '{borrower.Name}' request to '{creditor.Name}' refused by the workflow.");
                events?.Record(dayIndex, CreditEventKind.DecisionFailed, borrower.Name, creditor.Name,
                    borrower.DesiredAmountCents, "borrower request refused by the workflow.");
                return null;
            }
            // The borrower discloses what they choose — an honest borrower
            // discloses their real obligations (legitimate self-knowledge).
            if (borrower.HonestDisclosure && authority != null)
            {
                foreach (FinancialObligation obligation in authority.Obligations)
                {
                    if (obligation == null || obligation.Settled) continue;
                    if (!string.Equals(obligation.Debtor, borrower.Name, StringComparison.Ordinal)) continue;
                    workflow.DiscloseEvidence(ids, request, "borrower-disclosure",
                        $"existing {obligation.Kind} obligation to '{obligation.Creditor}': {obligation.TotalOutstandingCents}c outstanding",
                        obligation.ObligationId, dayIndex);
                }
            }
            request.RequiredCashContributionCents = 0;
            diag.Add($"NpcCredit: '{borrower.Name}' requested {borrower.DesiredAmountCents}c from '{creditor.Name}' " +
                $"('{borrower.Purpose}'). Honest disclosure: {borrower.HonestDisclosure}.");
            events?.Record(dayIndex, CreditEventKind.NegotiationSubmitted, borrower.Name, creditor.Name,
                borrower.DesiredAmountCents,
                $"'{borrower.Name}' filed a credit request for {borrower.DesiredAmountCents}c ('{borrower.Purpose}').",
                "", request.RequestId);
            return request;
        }

        public CreditOffer CreditorEvaluate(EntityIdRegistry ids, CreditOfferWorkflow workflow,
            FinancialObligationAuthority authority, NpcBorrowerProfile borrower,
            NpcCreditorProfile creditor, CreditRequest request, int dayIndex,
            CreditEventLog events, List<string> diag)
        {
            diag = diag ?? diagnostics;
            KnownRepaymentHistory(authority, borrower.Name, creditor.Name,
                out int satisfied, out int openOwed);
            diag.Add($"NpcCredit: '{creditor.Name}' knows '{borrower.Name}' from {satisfied} satisfied prior obligation(s), {openOwed}c still owed to them.");
            if (creditor.PrivateLender != null)
                return creditor.PrivateLender.EvaluateOffer(ids, workflow, authority, request, dayIndex, events, diag);
            if (creditor.Bank != null)
                return creditor.Bank.EvaluateOffer(ids, workflow, authority, request, dayIndex, events, diag);
            if (creditor.Participant != null)
            {
                CreditOffer offer = creditor.Participant.ConsiderRequest(ids, workflow, authority, request, dayIndex);
                if (offer != null)
                    events?.Record(dayIndex, offer.Status == CreditOfferStatus.Proposed ? CreditEventKind.OfferMade : CreditEventKind.OfferRefused,
                        borrower.Name, creditor.Name, offer.OfferedAmountCents,
                        $"'{creditor.Name}' evaluation: {offer.Status} — {offer.DecisionReason}", "", request.RequestId);
                return offer;
            }
            diag.Add($"NpcCredit: '{creditor.Name}' has no executing participant — cannot evaluate.");
            return null;
        }

        /// <summary>
        /// The borrower's decision on an offer: accept when rate and amount
        /// fit; counter once when they don't; decline otherwise.
        /// </summary>
        public bool BorrowerRespond(CreditOfferWorkflow workflow, NpcBorrowerProfile borrower,
            NpcCreditorProfile creditor, CreditOffer offer, int dayIndex,
            CreditEventLog events, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (workflow == null || borrower == null || offer == null) return false;
            if (offer.Status != CreditOfferStatus.Proposed && offer.Status != CreditOfferStatus.CounterAccepted)
            {
                diag.Add($"NpcCredit: '{borrower.Name}' cannot respond — offer is {offer.Status}.");
                return false;
            }
            bool rateOk = offer.AnnualInterestRateBps <= borrower.MaxRateBps;
            bool amountOk = offer.OfferedAmountCents >= borrower.MinimumAmountCents;
            if (rateOk && amountOk)
            {
                diag.Add($"NpcCredit: '{borrower.Name}' ACCEPTS {offer.OfferedAmountCents}c at {offer.AnnualInterestRateBps}bps.");
                events?.Record(dayIndex, CreditEventKind.OfferAccepted, borrower.Name, creditor.Name,
                    offer.OfferedAmountCents,
                    $"'{borrower.Name}' accepted {offer.OfferedAmountCents}c at {offer.AnnualInterestRateBps}bps, {offer.TermDays} days.",
                    "", offer.OfferId);
                return true;
            }
            if (borrower.WillCounter && offer.Status == CreditOfferStatus.Proposed)
            {
                int counterAmount = Math.Min(offer.RequestedAmountCents,
                    Math.Max(borrower.MinimumAmountCents, offer.OfferedAmountCents));
                int counterRate = Math.Min(offer.AnnualInterestRateBps, borrower.MaxRateBps);
                if (workflow.CounterOffer(offer.OfferId, counterAmount, counterRate, offer.TermDays,
                    "borrower counter: rate capped at what they will pay", out string message, dayIndex))
                {
                    diag.Add($"NpcCredit: '{borrower.Name}' COUNTERED — {counterAmount}c at {counterRate}bps. {message}");
                    events?.Record(dayIndex, CreditEventKind.CounterMade, borrower.Name, creditor.Name,
                        counterAmount, $"'{borrower.Name}' countered: {counterAmount}c at {counterRate}bps.",
                        "", offer.OfferId);
                    // The creditor decides on the counter through their policy.
                    bool rateFits = counterRate >= creditor.Policy.MinimumRateBps;
                    bool termFits = offer.TermDays <= creditor.Policy.MaximumTermDays;
                    if (rateFits && termFits
                        && workflow.AcceptCounterOffer(offer.OfferId, creditor.Name, dayIndex, out _))
                    {
                        diag.Add($"NpcCredit: '{creditor.Name}' accepted the counter.");
                        events?.Record(dayIndex, CreditEventKind.OfferAccepted, borrower.Name, creditor.Name,
                            counterAmount, $"'{creditor.Name}' accepted the counter: {counterAmount}c at {counterRate}bps.",
                            "", offer.OfferId);
                        return true;
                    }
                    workflow.DeclineCounterOffer(offer.OfferId, creditor.Name,
                        $"counter rate {counterRate}bps under policy floor {creditor.Policy.MinimumRateBps}bps or term over maximum.",
                        dayIndex);
                    diag.Add($"NpcCredit: '{creditor.Name}' DECLINED the counter — negotiation failed.");
                    events?.Record(dayIndex, CreditEventKind.OfferDeclined, borrower.Name, creditor.Name,
                        counterAmount, $"'{creditor.Name}' declined the counter — no loan.",
                        "", offer.OfferId);
                    return false;
                }
            }
            workflow.Decline(offer.OfferId, $"'{borrower.Name}' declined: rate {offer.AnnualInterestRateBps}bps over their {borrower.MaxRateBps}bps max or amount short.");
            diag.Add($"NpcCredit: '{borrower.Name}' DECLINED the offer.");
            events?.Record(dayIndex, CreditEventKind.OfferDeclined, borrower.Name, creditor.Name,
                offer.OfferedAmountCents, $"'{borrower.Name}' declined the offer.", "", offer.OfferId);
            return false;
        }

        /// <summary>
        /// Closes the accepted offer: real conserved cash movement, one
        /// obligation, maturity set from the negotiated term.
        /// </summary>
        public FinancialObligation CloseLoan(EntityIdRegistry ids, CreditOfferWorkflow workflow,
            FinancialObligationAuthority authority, CreditCashBridge cash,
            NpcBorrowerProfile borrower, NpcCreditorProfile creditor, CreditOffer offer,
            int dayIndex, CreditEventLog events, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (offer == null) return null;
            IRealCashStore lenderStore = cash?.FindStore(creditor.Name);
            IRealCashStore borrowerStore = cash?.FindStore(borrower.Name);
            int before = (lenderStore?.ReadBalanceCents() ?? 0) + (borrowerStore?.ReadBalanceCents() ?? 0);

            FinancialObligation obligation = null;
            if (creditor.PrivateLender != null)
                obligation = creditor.PrivateLender.AcceptOffer(ids, workflow, authority, cash,
                    offer.OfferId, borrower.Name, dayIndex, events, diag);
            else if (creditor.Bank != null)
                obligation = creditor.Bank.AcceptOffer(ids, workflow, authority, cash,
                    offer.OfferId, borrower.Name, dayIndex, events, diag);
            else if (creditor.Participant != null && cash != null)
            {
                CreditCashAccount lenderCash = cash.OpenWindow(creditor.Name, diag);
                CreditCashAccount borrowerCash = cash.OpenWindow(borrower.Name, diag);
                if (lenderCash != null && borrowerCash != null)
                {
                    creditor.Participant.Cash.BalanceCents = lenderCash.BalanceCents;
                    obligation = creditor.Participant.AcceptOffer(ids, workflow, authority,
                        offer.OfferId, borrowerCash, dayIndex);
                    if (obligation != null)
                    {
                        lenderCash.BalanceCents = creditor.Participant.Cash.BalanceCents;
                        string lp = cash.CommitWindow(lenderCash, dayIndex,
                            $"loan advance to '{borrower.Name}'", borrower.Name, diag);
                        string bp = cash.CommitWindow(borrowerCash, dayIndex,
                            $"loan proceeds from '{creditor.Name}'", creditor.Name, diag);
                        if (lp != null || bp != null)
                        {
                            diag.Add("NpcCredit: INCONSISTENCY — cash commit refused after obligation creation.");
                            return null;
                        }
                    }
                    else { cash.DiscardWindow(lenderCash); cash.DiscardWindow(borrowerCash); }
                }
            }
            if (obligation == null)
            {
                diag.Add($"NpcCredit: closing failed for '{borrower.Name}' ← '{creditor.Name}'.");
                return null;
            }
            // The negotiated term becomes the obligation's maturity — without
            // it, a MaturityPrincipal loan would never come due.
            obligation.MaturityDayIndex = dayIndex + Math.Max(1, offer.TermDays);
            int after = (lenderStore?.ReadBalanceCents() ?? 0) + (borrowerStore?.ReadBalanceCents() ?? 0);
            bool conserved = before == after;
            diag.Add($"NpcCredit: loan closed — obligation '{obligation.ObligationId}', {obligation.TotalOutstandingCents}c. " +
                $"Cash conservation: {before}c → {after}c ({(conserved ? "CONSERVED" : "BROKEN")}).");
            events?.Record(dayIndex, CreditEventKind.Settlement, borrower.Name, creditor.Name,
                obligation.TotalOutstandingCents,
                $"loan closed: '{obligation.ObligationId}' for {obligation.TotalOutstandingCents}c, cash {(conserved ? "conserved" : "NOT conserved")}.",
                obligation.ObligationId, offer.OfferId);
            return obligation;
        }

        /// <summary>
        /// Simulates repayment day by day: interest accrues, contractual
        /// amounts come due, the borrower pays what they can from real cash,
        /// missed payments are marked delinquent and routed to the creditor's
        /// workout response.
        /// </summary>
        public void SimulateRepayment(EntityIdRegistry ids, CreditOfferWorkflow workflow,
            FinancialObligationAuthority authority, CreditCashBridge cash,
            CreditWorkoutService workout, CreditRegistry registry, ForeclosureService foreclosure,
            TitleAuthority titles, NpcBorrowerProfile borrower, NpcCreditorProfile creditor,
            string obligationId, string offerId, int startDayIndex, int endDayIndex,
            NpcCreditLoopResult result, CreditEventLog events, List<string> diag)
        {
            diag = diag ?? diagnostics;
            for (int day = startDayIndex; day <= endDayIndex; day++)
            {
                FinancialObligation obligation = authority?.Find(obligationId);
                if (obligation == null || obligation.Settled) break;
                authority.AccrueInterestThroughDay(obligationId, day);
                int due = authority.CalculateDueAmountCents(obligationId, day);
                if (due <= 0) continue;

                IRealCashStore borrowerStore = cash?.FindStore(borrower.Name);
                if (borrowerStore != null && borrowerStore.ReadBalanceCents() >= due)
                {
                    CreditCashAccount borrowerCash = cash.OpenWindow(borrower.Name, diag);
                    CreditCashAccount creditorCash = cash.OpenWindow(creditor.Name, diag);
                    if (borrowerCash != null && creditorCash != null)
                    {
                        FinancialPaymentRecord payment = workflow.CollectPayment(obligationId, due, day,
                            authority, borrowerCash, creditorCash, out string message);
                        if (payment != null)
                        {
                            string bp = cash.CommitWindow(borrowerCash, dayIndex: day,
                                $"loan repayment to '{creditor.Name}'", creditor.Name, diag);
                            string cp = cash.CommitWindow(creditorCash, dayIndex: day,
                                $"loan repayment from '{borrower.Name}'", borrower.Name, diag);
                            if (bp == null && cp == null)
                            {
                                result.PaymentsCollected++;
                                diag.Add($"NpcCredit: day {day} — '{borrower.Name}' paid {payment.AmountCents}c " +
                                    $"({payment.PrincipalCents}c principal, {payment.InterestCents}c interest).");
                                events?.Record(day, CreditEventKind.PaymentCollected, borrower.Name, creditor.Name,
                                    payment.AmountCents,
                                    $"repayment {payment.AmountCents}c collected ({payment.PrincipalCents}c principal).",
                                    obligationId);
                                if (creditor.PrivateLender != null)
                                    creditor.PrivateLender.RecordRepayment(obligationId, offerId,
                                        obligation.Settled, day, diag);
                                continue;
                            }
                        }
                        else diag.Add($"NpcCredit: day {day} — payment refused: {message}");
                        cash.DiscardWindow(borrowerCash); cash.DiscardWindow(creditorCash);
                    }
                }

                result.PaymentsMissed++;
                authority.MarkDelinquent(obligationId);
                diag.Add($"NpcCredit: day {day} — '{borrower.Name}' MISSED {due}c (holds {borrowerStore?.ReadBalanceCents() ?? 0}c) — marked delinquent.");
                events?.Record(day, CreditEventKind.PaymentMissed, borrower.Name, creditor.Name, due,
                    $"'{borrower.Name}' missed a {due}c payment — delinquency marked.",
                    obligationId);
                events?.Record(day, CreditEventKind.DelinquencyMarked, borrower.Name, creditor.Name, due,
                    $"obligation '{obligationId}' marked delinquent after a missed {due}c payment.",
                    obligationId);
                var creditorMap = BuildCreditorMap(creditor, cash);
                workout?.ProcessNamedObligations(ids, authority, registry, cash, foreclosure, titles,
                    creditorMap, new[] { obligationId }, day, events, diag);
            }
        }

        private IReadOnlyDictionary<string, CreditParticipantService> BuildCreditorMap(
            NpcCreditorProfile creditor, CreditCashBridge cash)
        {
            var map = new Dictionary<string, CreditParticipantService>(StringComparer.Ordinal);
            if (creditor?.Participant != null)
            {
                IRealCashStore store = cash?.FindStore(creditor.Name);
                if (store != null) creditor.Participant.Cash.BalanceCents = store.ReadBalanceCents();
                map[creditor.Name] = creditor.Participant;
            }
            else if (creditor != null)
            {
                // Private lenders and banks still choose through the shared
                // response policy; their cash is the real store balance.
                var participant = new CreditParticipantService(creditor.Name,
                    creditor.Bank != null ? CreditParticipantRole.Bank : CreditParticipantRole.PrivatePerson,
                    cash?.FindStore(creditor.Name)?.ReadBalanceCents() ?? 0,
                    1f);
                map[creditor.Name] = participant;
            }
            return map;
        }

        /// <summary>
        /// Routes rent arrears into the credit machinery: the arrears records
        /// name real obligations, so delinquency and workout apply directly.
        /// </summary>
        public void ProcessRentArrears(EntityIdRegistry ids, FinancialObligationAuthority authority,
            CreditRegistry registry, CreditCashBridge cash, CreditWorkoutService workout,
            ForeclosureService foreclosure, TitleAuthority titles,
            NpcCreditorProfile landlord, IEnumerable<string> arrearsObligationIds,
            int dayIndex, CreditEventLog events, List<string> diag)
        {
            diag = diag ?? diagnostics;
            int routed = 0;
            foreach (string obligationId in arrearsObligationIds ?? Array.Empty<string>())
            {
                FinancialObligation obligation = authority?.Find(obligationId);
                if (obligation == null || obligation.Settled) continue;
                if (obligation.Status != FinancialObligationStatus.Delinquent
                    && obligation.Status != FinancialObligationStatus.Defaulted
                    && obligation.Status != FinancialObligationStatus.Due)
                    continue;
                routed++;
            }
            diag.Add($"NpcCredit: {routed} rent-arrears obligation(s) routed into the workout machinery.");
            events?.Record(dayIndex, CreditEventKind.DelinquencyMarked,
                "tenant", landlord?.Name ?? "landlord", 0,
                $"{routed} rent-arrears obligation(s) routed into the workout machinery.");
            workout?.ProcessNamedObligations(ids, authority, registry, cash, foreclosure, titles,
                BuildCreditorMap(landlord, cash), arrearsObligationIds, dayIndex, events, diag);
        }

        /// <summary>
        /// Runs the whole loop: request → evaluate → negotiate → close →
        /// repay → (on misses) workout. Proves the loop; doesn't stop at ids.
        /// </summary>
        public NpcCreditLoopResult RunFullLoop(EntityIdRegistry ids, CreditOfferWorkflow workflow,
            FinancialObligationAuthority authority, CreditCashBridge cash,
            CreditWorkoutService workout, CreditRegistry registry, ForeclosureService foreclosure,
            TitleAuthority titles, NpcBorrowerProfile borrower, NpcCreditorProfile creditor,
            int dayIndex, int simulateThroughDay, CreditEventLog events, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var result = new NpcCreditLoopResult();

            CreditRequest request = SubmitBorrowerRequest(ids, workflow, authority, borrower, creditor,
                dayIndex, events, diag);
            result.RequestFiled = request != null;
            result.RequestId = request?.RequestId ?? string.Empty;
            if (request == null) { result.Notes.Add("request refused"); return result; }

            CreditOffer offer = CreditorEvaluate(ids, workflow, authority, borrower, creditor, request,
                dayIndex, events, diag);
            result.OfferMade = offer != null && (offer.Status == CreditOfferStatus.Proposed || offer.Status == CreditOfferStatus.CounterAccepted);
            result.OfferId = offer?.OfferId ?? string.Empty;
            if (offer == null) { result.Notes.Add("no offer"); return result; }

            if (!BorrowerRespond(workflow, borrower, creditor, offer, dayIndex, events, diag))
            { result.Notes.Add("negotiation failed"); return result; }

            IRealCashStore lenderStore = cash?.FindStore(creditor.Name);
            IRealCashStore borrowerStore = cash?.FindStore(borrower.Name);
            int cashBefore = (lenderStore?.ReadBalanceCents() ?? 0) + (borrowerStore?.ReadBalanceCents() ?? 0);
            FinancialObligation obligation = CloseLoan(ids, workflow, authority, cash, borrower, creditor,
                offer, dayIndex, events, diag);
            result.Closed = obligation != null;
            result.ObligationId = obligation?.ObligationId ?? string.Empty;
            if (obligation == null) { result.Notes.Add("closing failed"); return result; }
            int cashAfter = (lenderStore?.ReadBalanceCents() ?? 0) + (borrowerStore?.ReadBalanceCents() ?? 0);
            result.CashConserved = cashBefore == cashAfter;

            SimulateRepayment(ids, workflow, authority, cash, workout, registry, foreclosure, titles,
                borrower, creditor, obligation.ObligationId, offer.OfferId, dayIndex + 1, simulateThroughDay,
                result, events, diag);

            FinancialObligation final = authority.Find(obligation.ObligationId);
            result.FinalStatus = final?.Status ?? FinancialObligationStatus.Proposed;
            result.FinalOutstandingCents = final?.TotalOutstandingCents ?? 0;
            result.Notes.Add($"final: {result.FinalStatus}, {result.FinalOutstandingCents}c outstanding, " +
                $"{result.PaymentsCollected} collected, {result.PaymentsMissed} missed.");
            return result;
        }
    }
}
