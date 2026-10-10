using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using LandLedgers.World.Property;

namespace LandLedgers.Economy.Financing
{
    /// <summary>Phase F (F2): how an acquisition failed. Append-only.</summary>
    public enum NpcAcquisitionFailureKind
    {
        Unspecified = 0,
        InvalidPlan = 1,
        SellerWalkedAway = 2,
        FinancingFellThrough = 3,
        TitleRefused = 4,
        CashShortfall = 5,
    }

    /// <summary>Phase F: the seller's answer to a buyer offer. Append-only.</summary>
    public enum NpcSellerResponseKind
    {
        Unspecified = 0,
        Accept = 1,
        Counter = 2,
        WalkAway = 3,
    }

    [Serializable]
    public sealed class NpcSellerResponse
    {
        public NpcSellerResponseKind Kind = NpcSellerResponseKind.Unspecified;
        public int CounterPriceCents;
        public string Reason = string.Empty;

        public NpcSellerResponse() { }
    }

    /// <summary>
    /// Phase F: the seller side of a price negotiation. The implementation is
    /// the selling NPC's decision surface (reservation price, counter
    /// policy); the acquisition service only runs the protocol. Tests fake
    /// it; the runtime wires a real seller-side evaluation later.
    /// </summary>
    public interface INpcPropertySellerPolicy
    {
        int ReservationPriceCents(string parcelId, int askingPriceCents);
        NpcSellerResponse RespondToOffer(string parcelId, int askingPriceCents,
            int buyerOfferCents, int round, int maxRounds, List<string> diag);
    }

    [Serializable]
    public sealed class NpcPriceNegotiationResult
    {
        public bool Agreed;
        public int AgreedPriceCents;
        public string FailureReason = string.Empty;
        /// <summary>Every round, both sides — §26 observability.</summary>
        public List<string> Rounds = new List<string>();

        public NpcPriceNegotiationResult() { }
    }

    /// <summary>
    /// Phase F: the fully-validated acquisition plan. Every leg is confirmed
    /// BEFORE execution: the seller's accepted negotiation (for the note
    /// leg), the evaluated lender offer (for the loan leg), and real cash
    /// for the down payment. Conservation is checked up front:
    /// cash down + seller note + lender loan == agreed price, cent for cent.
    /// </summary>
    [Serializable]
    public sealed class NpcAcquisitionPlan
    {
        public string PlanId = string.Empty;
        public int BuyerHouseholdId = -1;
        /// <summary>Cash-store owner name, e.g. "household:7".</summary>
        public string BuyerName = string.Empty;
        /// <summary>The parcel's current holder per the title authority.</summary>
        public string SellerName = string.Empty;
        public string ParcelId = string.Empty;
        /// <summary>The existing property's accommodation space (empty for bare land).</summary>
        public string SpaceId = string.Empty;
        public List<int> OccupantPersonIds = new List<int>();
        public int AgreedPriceCents;
        /// <summary>Cash the buyer puts in at closing (the seller note's down payment when a note leg exists).</summary>
        public int CashDownCents;
        /// <summary>Accepted seller-finance negotiation id (empty = no note leg).</summary>
        public string SellerNegotiationId = string.Empty;
        // Lender leg (empty/null = no loan leg).
        public NpcBorrowerProfile BorrowerProfile;
        public NpcCreditorProfile LenderProfile;
        public CreditOffer LenderOffer;
        public int LenderLoanCents;

        public NpcAcquisitionPlan() { }
    }

    [Serializable]
    public sealed class NpcAcquisitionResult
    {
        public bool Closed;
        public NpcAcquisitionFailureKind FailureKind = NpcAcquisitionFailureKind.Unspecified;
        public string FailureReason = string.Empty;
        /// <summary>True when a mid-deal failure was fully reversed (no partial state left behind).</summary>
        public bool UnwoundCleanly;
        public string ParcelId = string.Empty;
        public string NewHolder = string.Empty;
        public int AgreedPriceCents;
        public int CashToSellerCents;
        public int SellerNoteFinancedCents;
        public int LenderLoanCents;
        public string SellerNoteInstrumentId = string.Empty;
        public string SellerNoteObligationId = string.Empty;
        public string LenderObligationId = string.Empty;
        public string SaleAgreementId = string.Empty;
        public List<string> OccupancyIds = new List<string>();
        public bool CashConserved;
        public List<string> ConservationNotes = new List<string>();

        public NpcAcquisitionResult() { }
    }

    /// <summary>
    /// Phase F (F2): financed acquisition execution. A decided purchase
    /// becomes a real acquisition:
    /// <list type="bullet">
    /// <item>Price negotiation with counter-offers (never take-it-or-leave-it)
    /// through <see cref="INpcPropertySellerPolicy"/>.</item>
    /// <item>Financing assembly: cash down + seller note via
    /// <see cref="SellerFinanceClosingService"/> and/or a lender loan via the
    /// NPC credit loop (<see cref="NpcCreditDecisionEngine"/>) — every leg
    /// through <see cref="FinancialObligationAuthority"/>.</item>
    /// <item>Title transfer through the real <see cref="TitleAuthority"/>
    /// (queryable afterwards), and occupancy registration in
    /// <see cref="HousingAuthority"/>.</item>
    /// </list>
    /// If financing falls through mid-deal the deal FAILS LOUDLY: no partial
    /// ownership, no dangling obligations — any leg that already closed is
    /// unwound through the real payment machinery and the failure is
    /// reported with its exact cause.
    ///
    /// Conservation: seller cash + note == price; buyer obligations ==
    /// financed amount. Both are CHECKED, not just documented.
    /// </summary>
    public sealed class NpcPropertyAcquisitionService
    {
        /// <summary>TUNING: the buyer's opening discount off asking, in basis points. Calibration, not doctrine.</summary>
        public int OpeningOfferDiscountBps { get; set; } = 1000;

        private readonly List<string> diagnostics = new List<string>();
        private int sequence;

        public IReadOnlyList<string> Diagnostics => diagnostics;

        #region Price negotiation

        /// <summary>
        /// Bounded-round price negotiation. The buyer opens below asking
        /// (never above it, never above their means); the seller accepts,
        /// counters, or walks per their policy; the buyer accepts a counter
        /// within their means or makes one final take-it-or-leave-it offer at
        /// their maximum. Every round is recorded.
        /// </summary>
        public NpcPriceNegotiationResult NegotiatePrice(
            INpcPropertySellerPolicy sellerPolicy,
            string parcelId, int askingPriceCents, int buyerMaxCents,
            int maxRounds, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var result = new NpcPriceNegotiationResult();
            if (sellerPolicy == null)
                return FailNegotiation(result, "no seller policy — the seller's side of the negotiation is not wired.", diag);
            if (askingPriceCents <= 0)
                return FailNegotiation(result, "no real asking price — nothing to negotiate.", diag);
            if (buyerMaxCents <= 0)
                return FailNegotiation(result, $"the buyer cannot fund any offer (max {buyerMaxCents}c) — they walk away before bidding.", diag);

            int discount = (int)Math.Floor(askingPriceCents * (Math.Max(0, OpeningOfferDiscountBps) / 10000.0));
            int buyerOffer = Math.Min(askingPriceCents - Math.Max(0, discount), buyerMaxCents);
            if (buyerOffer <= 0)
                return FailNegotiation(result, "the buyer's means do not reach a first offer — they walk away.", diag);

            for (int round = 1; round <= Math.Max(1, maxRounds); round++)
            {
                NpcSellerResponse response = sellerPolicy.RespondToOffer(
                    parcelId, askingPriceCents, buyerOffer, round, maxRounds, diag);
                if (response == null)
                    return FailNegotiation(result, "the seller gave no answer — the negotiation stalls and dies.", diag, buyerOffer, round);
                string roundNote = $"round {round}: buyer offers {buyerOffer}c → seller {response.Kind}" +
                    (response.Kind == NpcSellerResponseKind.Counter ? $" at {response.CounterPriceCents}c" : "") +
                    (!string.IsNullOrWhiteSpace(response.Reason) ? $" ({response.Reason})" : "");
                result.Rounds.Add(roundNote);
                diag.Add("NpcPropertyAcquisition.NegotiatePrice: " + roundNote);

                if (response.Kind == NpcSellerResponseKind.Accept)
                {
                    result.Agreed = true;
                    result.AgreedPriceCents = buyerOffer;
                    diag.Add($"NpcPropertyAcquisition: AGREED at {buyerOffer}c (asking was {askingPriceCents}c).");
                    return result;
                }
                if (response.Kind == NpcSellerResponseKind.WalkAway)
                {
                    return FailNegotiation(result,
                        $"the seller walked away at {buyerOffer}c — {response.Reason}", diag, buyerOffer, round);
                }
                if (response.Kind == NpcSellerResponseKind.Counter && response.CounterPriceCents <= buyerMaxCents)
                {
                    result.Agreed = true;
                    result.AgreedPriceCents = response.CounterPriceCents;
                    result.Rounds.Add($"buyer accepts the counter at {response.CounterPriceCents}c.");
                    diag.Add($"NpcPropertyAcquisition: AGREED at {response.CounterPriceCents}c on the seller's counter.");
                    return result;
                }
                if (round >= maxRounds)
                    return FailNegotiation(result,
                        $"no agreement after {maxRounds} round(s) — the buyer walks away.", diag, buyerOffer, round);
                // The buyer puts their maximum on the table, once, finally.
                buyerOffer = buyerMaxCents;
                result.Rounds.Add($"buyer raises to their maximum, {buyerMaxCents}c, final offer.");
            }
            return FailNegotiation(result, "negotiation exhausted without agreement.", diag, buyerOffer, maxRounds);
        }

        private NpcPriceNegotiationResult FailNegotiation(NpcPriceNegotiationResult result,
            string reason, List<string> diag, int lastOffer = 0, int round = 0)
        {
            result.Agreed = false;
            result.FailureReason = reason;
            if (round > 0) result.Rounds.Add($"negotiation ends: {reason} (last offer {lastOffer}c).");
            diag.Add("NpcPropertyAcquisition.NegotiatePrice: " + reason);
            return result;
        }

        #endregion

        #region Closing

        /// <summary>
        /// Validates the plan without moving anything. Returns null when the
        /// plan is executable, or the exact reason it is not.
        /// </summary>
        public string ValidatePlan(
            NpcAcquisitionPlan plan,
            TitleAuthority titles,
            SellerFinanceNegotiationBook negotiations,
            CreditCashBridge cash,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (plan == null) return "no acquisition plan.";
            if (titles == null) return "no title authority.";
            if (string.IsNullOrWhiteSpace(plan.ParcelId)) return "the plan names no parcel.";
            if (string.IsNullOrWhiteSpace(plan.BuyerName)) return "the plan names no buyer.";
            if (string.IsNullOrWhiteSpace(plan.SellerName)) return "the plan names no seller.";
            if (string.Equals(plan.BuyerName, plan.SellerName, StringComparison.Ordinal))
                return "buyer and seller are the same party — no sale.";
            if (plan.AgreedPriceCents <= 0) return "no agreed price — a closing without a price is not a closing.";
            if (plan.CashDownCents < 0 || plan.CashDownCents > plan.AgreedPriceCents)
                return $"cash down {plan.CashDownCents}c is inconsistent with price {plan.AgreedPriceCents}c.";

            string holder = titles.CurrentHolder(plan.ParcelId);
            if (holder == null)
                return $"parcel '{plan.ParcelId}' is unknown to the title authority — title must be real and queryable.";
            if (!string.Equals(holder, plan.SellerName, StringComparison.Ordinal))
                return $"parcel '{plan.ParcelId}' is held by '{holder}', not the purported seller '{plan.SellerName}'.";

            // Seller-finance leg: the accepted negotiation must map to closing terms that match the plan.
            SellerFinanceClosingTerms terms = null;
            if (!string.IsNullOrWhiteSpace(plan.SellerNegotiationId))
            {
                if (negotiations == null) return "the plan needs a seller-finance leg but no negotiation book was supplied.";
                terms = negotiations.ToClosingTerms(plan.SellerNegotiationId, diag);
                if (terms == null)
                    return $"negotiation '{plan.SellerNegotiationId}' has no accepted closing terms — the note leg is not confirmed.";
                if (!string.Equals(terms.BuyerName, plan.BuyerName, StringComparison.Ordinal))
                    return $"the accepted terms name buyer '{terms.BuyerName}', not the plan's buyer '{plan.BuyerName}'.";
                if (terms.SalePriceCents != plan.AgreedPriceCents)
                    return $"the accepted terms name price {terms.SalePriceCents}c, not the agreed {plan.AgreedPriceCents}c.";
                if (terms.DownPaymentCents != plan.CashDownCents)
                    return $"the accepted terms require {terms.DownPaymentCents}c down, but the plan puts down {plan.CashDownCents}c.";
            }
            int sellerFinanced = terms != null ? terms.SalePriceCents - terms.DownPaymentCents : 0;

            // Lender leg: an evaluated, acceptable offer covering the loan portion.
            if (plan.LenderLoanCents > 0)
            {
                if (plan.LenderOffer == null) return "the plan draws a lender loan but names no evaluated offer.";
                if (plan.LenderOffer.Status != CreditOfferStatus.Proposed
                    && plan.LenderOffer.Status != CreditOfferStatus.CounterAccepted)
                    return $"lender offer '{plan.LenderOffer.OfferId}' is {plan.LenderOffer.Status} — not an acceptable offer.";
                if (plan.LenderOffer.OfferedAmountCents < plan.LenderLoanCents)
                    return $"lender offer covers {plan.LenderOffer.OfferedAmountCents}c but the plan draws {plan.LenderLoanCents}c.";
                if (plan.BorrowerProfile == null || plan.LenderProfile == null)
                    return "the lender leg needs both borrower and lender profiles.";
                if (plan.LenderOffer.AnnualInterestRateBps > plan.BorrowerProfile.MaxRateBps)
                    return $"lender offer rate {plan.LenderOffer.AnnualInterestRateBps}bps exceeds what the borrower will pay ({plan.BorrowerProfile.MaxRateBps}bps).";
            }
            else if (plan.LenderOffer != null || plan.BorrowerProfile != null)
            {
                diag.Add("NpcPropertyAcquisition.ValidatePlan: lender offer/profiles supplied but no loan drawn — the leg is ignored.");
            }

            // Conservation, up front: cash down + seller note + lender loan == price.
            if (plan.CashDownCents + sellerFinanced + plan.LenderLoanCents != plan.AgreedPriceCents)
                return $"conservation fails before closing: {plan.CashDownCents}c down + {sellerFinanced}c note + " +
                    $"{plan.LenderLoanCents}c loan != {plan.AgreedPriceCents}c price.";

            // The buyer must be able to move the full cash portion (down +
            // the lender-financed part, which arrives via disbursement).
            int cashToSeller = plan.AgreedPriceCents - sellerFinanced;
            if (cash == null) return "no cash bridge — closings move real cash only.";
            IRealCashStore buyerStore = cash.FindStore(plan.BuyerName);
            if (buyerStore == null)
                return $"the buyer '{plan.BuyerName}' has no registered real cash store — refusing to close on invented money.";
            if (cash.FindStore(plan.SellerName) == null)
                return $"the seller '{plan.SellerName}' has no registered real cash store — the seller must really receive the price.";
            if (plan.LenderLoanCents > 0 && cash.FindStore(plan.LenderProfile.Name) == null)
                return $"the lender '{plan.LenderProfile.Name}' has no registered real cash store.";
            if (buyerStore.ReadBalanceCents() + plan.LenderLoanCents < cashToSeller)
                return $"the buyer holds {buyerStore.ReadBalanceCents()}c and borrows {plan.LenderLoanCents}c — short of the {cashToSeller}c cash portion.";

            return null;
        }

        /// <summary>
        /// Executes a validated plan. Order: lender leg first (so a loan
        /// failure leaves nothing behind), then the seller-note leg, then
        /// the cash movement, then title, then occupancy. Any mid-deal
        /// failure unwinds what already closed through the real payment
        /// machinery and fails LOUDLY.
        /// </summary>
        public NpcAcquisitionResult CloseAcquisition(
            EntityIdRegistry ids,
            NpcAcquisitionPlan plan,
            FinancialObligationAuthority authority,
            CreditRegistry registry,
            CreditCashBridge cash,
            TitleAuthority titles,
            HousingAuthority housing,
            SellerFinanceNegotiationBook negotiations,
            SellerFinanceClosingService closingService,
            NpcCreditDecisionEngine creditEngine,
            CreditOfferWorkflow workflow,
            int dayIndex,
            CreditEventLog events,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            var result = new NpcAcquisitionResult
            {
                ParcelId = plan != null ? plan.ParcelId : string.Empty,
                AgreedPriceCents = plan != null ? plan.AgreedPriceCents : 0,
            };
            string invalid = ValidatePlan(plan, titles, negotiations, cash, diag);
            if (invalid != null)
                return Fail(result, NpcAcquisitionFailureKind.InvalidPlan, "plan invalid: " + invalid,
                    plan, dayIndex, events, diag);

            IRealCashStore buyerStore0 = cash.FindStore(plan.BuyerName);
            IRealCashStore sellerStore0 = cash.FindStore(plan.SellerName);
            IRealCashStore lenderStore0 = plan.LenderLoanCents > 0 ? cash.FindStore(plan.LenderProfile.Name) : null;
            int buyerCashBefore = buyerStore0.ReadBalanceCents();
            int sellerCashBefore = sellerStore0.ReadBalanceCents();
            int lenderCashBefore = lenderStore0 != null ? lenderStore0.ReadBalanceCents() : 0;
            int cashBefore = buyerCashBefore + sellerCashBefore + lenderCashBefore;

            // ---- Leg 1: the lender loan (first, so its failure leaves nothing behind). ----
            FinancialObligation loan = null;
            if (plan.LenderLoanCents > 0)
            {
                bool accepted = creditEngine.BorrowerRespond(workflow, plan.BorrowerProfile,
                    plan.LenderProfile, plan.LenderOffer, dayIndex, events, diag);
                if (!accepted)
                    return Fail(result, NpcAcquisitionFailureKind.FinancingFellThrough,
                        "the borrower could not accept the lender offer at closing — no loan, no deal, nothing moved.",
                        plan, dayIndex, events, diag);
                if (plan.LenderOffer.OfferedAmountCents < plan.LenderLoanCents)
                    return Fail(result, NpcAcquisitionFailureKind.FinancingFellThrough,
                        $"the negotiated offer now covers {plan.LenderOffer.OfferedAmountCents}c, short of the {plan.LenderLoanCents}c the plan draws — nothing moved.",
                        plan, dayIndex, events, diag);
                loan = creditEngine.CloseLoan(ids, workflow, authority, cash,
                    plan.BorrowerProfile, plan.LenderProfile, plan.LenderOffer,
                    dayIndex, events, diag);
                if (loan == null)
                    return Fail(result, NpcAcquisitionFailureKind.FinancingFellThrough,
                        "the lender loan failed to close — no note issued, no title moved, buyer cash untouched.",
                        plan, dayIndex, events, diag);
                result.LenderObligationId = loan.ObligationId;
                result.LenderLoanCents = plan.LenderLoanCents;
                diag.Add($"NpcPropertyAcquisition: lender leg closed — obligation '{loan.ObligationId}' ({plan.LenderLoanCents}c of the price).");
            }

            // ---- Leg 2: the seller note. ----
            SellerFinanceClosingResult noteResult = null;
            if (!string.IsNullOrWhiteSpace(plan.SellerNegotiationId))
            {
                SellerFinanceClosingTerms terms = negotiations.ToClosingTerms(plan.SellerNegotiationId, diag);
                noteResult = closingService.Close(ids, terms, authority, registry, cash, titles,
                    dayIndex, events, diag);
                if (noteResult == null || !noteResult.Closed)
                {
                    string unwind = loan != null
                        ? UnwindLoan(ids, workflow, authority, cash, loan,
                            plan.BorrowerProfile.Name, plan.LenderProfile.Name, dayIndex, diag)
                        : null;
                    result.UnwoundCleanly = loan == null || unwind == null;
                    return Fail(result, NpcAcquisitionFailureKind.FinancingFellThrough,
                        $"the seller-note leg failed ({noteResult?.FailureReason ?? "no result"}) — the deal fails. " +
                        (loan != null ? (unwind == null
                            ? "The lender loan was unwound in full through the real payment machinery."
                            : "WARNING: the lender loan could not be unwound: " + unwind)
                            : "No loan leg had closed."),
                        plan, dayIndex, events, diag);
                }
                if (noteResult.CashToSellerCents + noteResult.FinancedCents != plan.AgreedPriceCents)
                {
                    string unwind = loan != null
                        ? UnwindLoan(ids, workflow, authority, cash, loan,
                            plan.BorrowerProfile.Name, plan.LenderProfile.Name, dayIndex, diag)
                        : null;
                    result.UnwoundCleanly = loan == null || unwind == null;
                    return Fail(result, NpcAcquisitionFailureKind.FinancingFellThrough,
                        $"the seller-note leg broke conservation ({noteResult.CashToSellerCents}c + {noteResult.FinancedCents}c != {plan.AgreedPriceCents}c) — deal aborted" +
                        (unwind == null ? ", loan unwound." : ", loan unwind FAILED: " + unwind),
                        plan, dayIndex, events, diag);
                }
                result.SellerNoteInstrumentId = noteResult.NoteInstrumentId;
                result.SellerNoteObligationId = noteResult.ObligationId;
                result.SellerNoteFinancedCents = noteResult.FinancedCents;
                result.CashToSellerCents = noteResult.CashToSellerCents;
            }

            // ---- Leg 3: the remaining cash to the seller. ----
            // (The note leg already moved its down payment; the lender leg already disbursed to the buyer.)
            int cashToSeller = plan.AgreedPriceCents - result.SellerNoteFinancedCents - result.CashToSellerCents;
            if (cashToSeller > 0)
            {
                string cashProblem = MoveCash(cash, plan.BuyerName, plan.SellerName, cashToSeller, dayIndex,
                    $"property purchase '{plan.ParcelId}' — cash portion", diag);
                if (cashProblem != null)
                {
                    string unwindNote = UnwindNoteLeg(ids, workflow, authority, cash, result,
                        plan.BuyerName, plan.SellerName, dayIndex, diag);
                    string unwindLoan = loan != null
                        ? UnwindLoan(ids, workflow, authority, cash, loan,
                            plan.BorrowerProfile.Name, plan.LenderProfile.Name, dayIndex, diag)
                        : null;
                    result.UnwoundCleanly = cashProblem == null && unwindNote == null && unwindLoan == null;
                    return Fail(result, NpcAcquisitionFailureKind.CashShortfall,
                        $"the cash movement failed mid-deal ({cashProblem}) — deal aborted. " +
                        $"Note unwind: {(unwindNote ?? "clean")}; loan unwind: {(unwindLoan ?? "clean")}.",
                        plan, dayIndex, events, diag);
                }
                result.CashToSellerCents += cashToSeller;
            }

            // ---- Leg 4: title. REQUIRED — a closing without title is not a closing. ----
            bool titleMoved = noteResult != null && noteResult.TitleTransferred;
            string conveyanceId;
            if (!titleMoved)
            {
                conveyanceId = ids != null ? ids.Allocate(EntityKind.Contract).ToString() : Guid.NewGuid().ToString("N");
                string titleProblem = titles.TransferTitle(ids, plan.ParcelId, plan.BuyerName,
                    TitleBasis.Purchase, conveyanceId, dayIndex,
                    $"purchase by '{plan.BuyerName}' for {plan.AgreedPriceCents}c", diag);
                if (titleProblem != null)
                {
                    string unwindNote = UnwindNoteLeg(ids, workflow, authority, cash, result,
                        plan.BuyerName, plan.SellerName, dayIndex, diag);
                    string unwindLoan = loan != null
                        ? UnwindLoan(ids, workflow, authority, cash, loan,
                            plan.BorrowerProfile.Name, plan.LenderProfile.Name, dayIndex, diag)
                        : null;
                    string unwindCash = result.CashToSellerCents > 0
                        ? MoveCash(cash, plan.SellerName, plan.BuyerName, result.CashToSellerCents, dayIndex,
                            $"acquisition unwound — title refused on '{plan.ParcelId}'", diag)
                        : null;
                    result.UnwoundCleanly = unwindNote == null && unwindLoan == null && unwindCash == null;
                    return Fail(result, NpcAcquisitionFailureKind.TitleRefused,
                        $"title transfer refused ({titleProblem}) — the deal fails. Cash returned: {(unwindCash ?? "clean")}; " +
                        $"note unwind: {(unwindNote ?? "clean")}; loan unwind: {(unwindLoan ?? "clean")}.",
                        plan, dayIndex, events, diag);
                }
            }
            else
            {
                conveyanceId = result.SellerNoteInstrumentId;
            }
            result.NewHolder = titles.CurrentHolder(plan.ParcelId);
            if (!string.Equals(result.NewHolder, plan.BuyerName, StringComparison.Ordinal))
            {
                return Fail(result, NpcAcquisitionFailureKind.TitleRefused,
                    $"title verification failed — '{plan.ParcelId}' is held by '{result.NewHolder}', not the buyer. Manual reconciliation required.",
                    plan, dayIndex, events, diag);
            }

            // ---- Leg 5: the sale agreement + occupancy. ----
            PropertyAgreement agreement = housing != null ? housing.RecordAgreement(
                "purchase", plan.SellerName, plan.BuyerName, plan.ParcelId,
                $"purchase of '{plan.ParcelId}' for {plan.AgreedPriceCents}c " +
                $"({result.CashToSellerCents}c cash + {result.SellerNoteFinancedCents}c seller note" +
                (result.LenderLoanCents > 0 ? $" + {result.LenderLoanCents}c lender loan" : "") + ")",
                dayIndex, -1, conveyanceId, diag) : null;
            result.SaleAgreementId = agreement != null ? agreement.AgreementId : string.Empty;

            if (housing != null && !string.IsNullOrWhiteSpace(plan.SpaceId) && plan.OccupantPersonIds != null)
            {
                foreach (int personId in plan.OccupantPersonIds)
                {
                    if (personId < 0) continue;
                    ResidentialOccupancy occ = housing.Occupy(personId, plan.BuyerHouseholdId,
                        plan.SpaceId, AccommodationArrangement.OwnerOccupied, dayIndex, diag);
                    if (occ != null) result.OccupancyIds.Add(occ.OccupancyId);
                }
            }

            // ---- Conservation, CHECKED. ----
            int buyerCashAfter = buyerStore0.ReadBalanceCents();
            int sellerCashAfter = sellerStore0.ReadBalanceCents();
            int lenderCashAfter = lenderStore0 != null ? lenderStore0.ReadBalanceCents() : 0;
            int cashAfter = buyerCashAfter + sellerCashAfter + lenderCashAfter;
            result.CashConserved = cashAfter == cashBefore;
            int sellerReceived = sellerCashAfter - sellerCashBefore;
            result.ConservationNotes.Add($"cash conservation: {cashBefore}c → {cashAfter}c " +
                $"({(result.CashConserved ? "CONSERVED" : "BROKEN")}).");
            result.ConservationNotes.Add($"seller received {sellerReceived}c cash; " +
                $"cash + note = {result.CashToSellerCents + result.SellerNoteFinancedCents}c vs price {plan.AgreedPriceCents}c " +
                $"({(result.CashToSellerCents + result.SellerNoteFinancedCents == plan.AgreedPriceCents ? "MATCH" : "MISMATCH")}).");

            FinancialObligation noteOb = !string.IsNullOrWhiteSpace(result.SellerNoteObligationId)
                ? authority.Find(result.SellerNoteObligationId) : null;
            FinancialObligation loanOb = loan != null ? authority.Find(loan.ObligationId) : null;
            int buyerObligations = (noteOb != null ? noteOb.TotalOutstandingCents : 0)
                + (loanOb != null ? loanOb.TotalOutstandingCents : 0);
            int financedAmount = plan.AgreedPriceCents - plan.CashDownCents;
            result.ConservationNotes.Add($"buyer obligations {buyerObligations}c vs financed amount {financedAmount}c " +
                $"({(buyerObligations == financedAmount ? "MATCH" : "MISMATCH")}).");

            result.Closed = true;
            diag.Add($"NpcPropertyAcquisition: CLOSED — '{plan.BuyerName}' bought '{plan.ParcelId}' for {plan.AgreedPriceCents}c " +
                $"({result.CashToSellerCents}c cash + {result.SellerNoteFinancedCents}c seller note + {result.LenderLoanCents}c lender loan). " +
                $"Title → buyer. Cash {(result.CashConserved ? "conserved" : "NOT conserved")}.");
            events?.Record(dayIndex, CreditEventKind.PropertyDecision, plan.BuyerName, plan.SellerName,
                plan.AgreedPriceCents,
                $"property acquisition: '{plan.BuyerName}' bought '{plan.ParcelId}' for {plan.AgreedPriceCents}c " +
                $"({result.CashToSellerCents}c cash, {result.SellerNoteFinancedCents}c seller note, {result.LenderLoanCents}c lender loan).",
                result.SellerNoteObligationId, result.SellerNoteInstrumentId);
            events?.Record(dayIndex, CreditEventKind.TitleTransferred, plan.BuyerName, plan.SellerName,
                plan.AgreedPriceCents,
                $"title of '{plan.ParcelId}' transferred to '{plan.BuyerName}' on acquisition closing.",
                result.SellerNoteObligationId, conveyanceId);
            return result;
        }

        private NpcAcquisitionResult Fail(NpcAcquisitionResult result, NpcAcquisitionFailureKind kind,
            string reason, NpcAcquisitionPlan plan, int dayIndex, CreditEventLog events, List<string> diag)
        {
            result.Closed = false;
            result.FailureKind = kind;
            result.FailureReason = reason;
            diag.Add($"NpcPropertyAcquisition: DEAL FAILED ({kind}) — {reason}");
            events?.Record(dayIndex, CreditEventKind.DecisionFailed,
                plan != null ? plan.BuyerName : "?", plan != null ? plan.SellerName : "?",
                plan != null ? plan.AgreedPriceCents : 0,
                "property acquisition failed (" + kind + "): " + reason);
            return result;
        }

        /// <summary>Moves real cash payer → payee through the bridge. Null on success.</summary>
        private string MoveCash(CreditCashBridge cash, string payer, string payee, int amountCents,
            int dayIndex, string purpose, List<string> diag)
        {
            if (amountCents <= 0) return null;
            CreditCashAccount payerCash = cash.OpenWindow(payer, diag);
            CreditCashAccount payeeCash = cash.OpenWindow(payee, diag);
            if (payerCash == null || payeeCash == null)
            {
                cash.DiscardWindow(payerCash); cash.DiscardWindow(payeeCash);
                return $"no real cash store for '{payer}' or '{payee}'.";
            }
            if (payerCash.BalanceCents < amountCents)
            {
                cash.DiscardWindow(payerCash); cash.DiscardWindow(payeeCash);
                return $"'{payer}' holds {payerCash.BalanceCents}c, needs {amountCents}c.";
            }
            payerCash.BalanceCents -= amountCents;
            payeeCash.BalanceCents += amountCents;
            string p1 = cash.CommitWindow(payerCash, dayIndex, purpose, payee, diag);
            string p2 = cash.CommitWindow(payeeCash, dayIndex, purpose, payer, diag);
            if (p1 != null || p2 != null)
                return $"cash commit refused: {(p1 ?? p2)}";
            return null;
        }

        /// <summary>
        /// Unwinds a closed lender leg: the borrower immediately repays the
        /// FULL outstanding amount through the real payment machinery. The
        /// lender gets their cash back; the obligation settles; nothing dangles.
        /// </summary>
        private string UnwindLoan(EntityIdRegistry ids, CreditOfferWorkflow workflow,
            FinancialObligationAuthority authority, CreditCashBridge cash, FinancialObligation loan,
            string borrowerName, string lenderName, int dayIndex, List<string> diag)
        {
            FinancialObligation current = authority.Find(loan.ObligationId);
            if (current == null || current.Settled) return null;
            int owed = current.TotalOutstandingCents;
            CreditCashAccount borrowerCash = cash.OpenWindow(borrowerName, diag);
            CreditCashAccount lenderCash = cash.OpenWindow(lenderName, diag);
            if (borrowerCash == null || lenderCash == null)
            {
                cash.DiscardWindow(borrowerCash); cash.DiscardWindow(lenderCash);
                return "no cash window for the unwind.";
            }
            if (borrowerCash.BalanceCents < owed)
            {
                cash.DiscardWindow(borrowerCash); cash.DiscardWindow(lenderCash);
                return $"the borrower holds {borrowerCash.BalanceCents}c but owes {owed}c on the unwind — the loan stands as a real obligation.";
            }
            FinancialPaymentRecord payment = workflow.CollectPayment(loan.ObligationId, owed, dayIndex,
                authority, borrowerCash, lenderCash, out string message);
            if (payment == null)
            {
                cash.DiscardWindow(borrowerCash); cash.DiscardWindow(lenderCash);
                return "the unwind payment was refused: " + message;
            }
            string p1 = cash.CommitWindow(borrowerCash, dayIndex,
                $"acquisition unwound — full repayment of '{loan.ObligationId}'", lenderName, diag);
            string p2 = cash.CommitWindow(lenderCash, dayIndex,
                $"acquisition unwound — loan '{loan.ObligationId}' repaid in full", borrowerName, diag);
            if (p1 != null || p2 != null)
                return "the unwind cash commit was refused: " + (p1 ?? p2);
            diag.Add($"NpcPropertyAcquisition: lender leg unwound — '{loan.ObligationId}' repaid in full ({owed}c), lender cash restored.");
            return null;
        }

        /// <summary>
        /// Unwinds a closed seller-note leg: the buyer repays the note in full
        /// right now (they hold the cash — the deal just moved it). The
        /// seller keeps the cash they received; the note settles; the
        /// security interest is released.
        /// </summary>
        private string UnwindNoteLeg(EntityIdRegistry ids, CreditOfferWorkflow workflow,
            FinancialObligationAuthority authority, CreditCashBridge cash, NpcAcquisitionResult result,
            string buyerName, string sellerName, int dayIndex, List<string> diag)
        {
            if (string.IsNullOrWhiteSpace(result.SellerNoteObligationId)) return null;
            FinancialObligation note = authority.Find(result.SellerNoteObligationId);
            if (note == null || note.Settled) return null;
            int owed = note.TotalOutstandingCents;
            CreditCashAccount buyerCash = cash.OpenWindow(buyerName, diag);
            CreditCashAccount sellerCash = cash.OpenWindow(sellerName, diag);
            if (buyerCash == null || sellerCash == null)
            {
                cash.DiscardWindow(buyerCash); cash.DiscardWindow(sellerCash);
                return "no cash window for the note unwind.";
            }
            if (buyerCash.BalanceCents < owed)
            {
                cash.DiscardWindow(buyerCash); cash.DiscardWindow(sellerCash);
                return $"the buyer holds {buyerCash.BalanceCents}c but the note needs {owed}c — the note stands as a real obligation.";
            }
            FinancialPaymentRecord payment = workflow.CollectPayment(note.ObligationId, owed, dayIndex,
                authority, buyerCash, sellerCash, out string message);
            if (payment == null)
            {
                cash.DiscardWindow(buyerCash); cash.DiscardWindow(sellerCash);
                return "the note unwind payment was refused: " + message;
            }
            string p1 = cash.CommitWindow(buyerCash, dayIndex,
                $"acquisition unwound — seller note '{note.ObligationId}' repaid in full", sellerName, diag);
            string p2 = cash.CommitWindow(sellerCash, dayIndex,
                $"acquisition unwound — seller note '{note.ObligationId}' settled", buyerName, diag);
            if (p1 != null || p2 != null)
                return "the note unwind cash commit was refused: " + (p1 ?? p2);
            foreach (string securityId in note.SecurityInterestIds)
                authority.ReleaseSecurity(securityId, $"acquisition of '{result.ParcelId}' unwound — note settled");
            diag.Add($"NpcPropertyAcquisition: seller-note leg unwound — note '{note.ObligationId}' settled in full, security released.");
            return null;
        }

        #endregion
    }
}
