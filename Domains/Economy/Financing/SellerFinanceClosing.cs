using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using LandLedgers.World.Property;
using UnityEngine;

namespace LandLedgers.Economy.Financing
{
    /// <summary>
    /// Phase E (E2): the accepted terms of a seller-financed sale, as handed
    /// off by a negotiation (Phase D's <c>TermsAccepted</c> maps to this via
    /// the negotiation book's connector). Plain data — no Unity, no world
    /// references — so the closing logic stays testable.
    /// </summary>
    [Serializable]
    public sealed class SellerFinanceClosingTerms
    {
        public string NegotiationId = string.Empty;
        public string SellerName = string.Empty;
        public string BuyerName = string.Empty;
        public string AssetDescription = string.Empty;
        public string AssetInstanceId = string.Empty;
        public int SalePriceCents;
        public int DownPaymentCents;
        public int AnnualRateBps;
        public int TermDays;
        public string GuarantorName = string.Empty;

        public SellerFinanceClosingTerms() { }
    }

    [Serializable]
    public sealed class SellerFinanceClosingResult
    {
        public bool Closed;
        public string FailureReason = string.Empty;
        public string NoteInstrumentId = string.Empty;
        public string ObligationId = string.Empty;
        public int CashToSellerCents;
        public int FinancedCents;
        public bool TitleTransferred;
        public string SecurityInterestId = string.Empty;

        public SellerFinanceClosingResult() { }
    }

    /// <summary>
    /// Phase E (E2): the full seller-finance closing. On success:
    /// <list type="bullet">
    /// <item>The buyer pays the down payment in REAL cash — the seller receives actual cash.</item>
    /// <item>The seller's deferred claim becomes a real <see cref="SellerFinanceNote"/>
    /// through <see cref="CreditRegistry.IssueSellerFinanceNote"/> (its first
    /// production caller) with the shared obligation behind it.</item>
    /// <item>The buyer's obligation is carried by
    /// <see cref="FinancialObligationAuthority"/>; the seller owns the creditor claim.</item>
    /// <item>Title/control transfers to the buyer per the deal; the seller's
    /// security interest stays SEPARATE from ownership — outstanding seller
    /// finance never magically restores seller ownership.</item>
    /// </list>
    /// Conservation: cash to seller + financed note == sale price, cent for cent.
    /// </summary>
    public sealed class SellerFinanceClosingService
    {
        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;

        public SellerFinanceClosingResult Close(EntityIdRegistry ids,
            SellerFinanceClosingTerms terms, FinancialObligationAuthority authority,
            CreditRegistry registry, CreditCashBridge cash, TitleAuthority titles,
            int dayIndex, CreditEventLog events, List<string> diag)
        {
            diag = diag ?? diagnostics;
            var result = new SellerFinanceClosingResult();
            if (ids == null || terms == null || authority == null || registry == null || cash == null)
            {
                result.FailureReason = "Closing needs ids, terms, the obligation authority, the credit registry and a cash bridge.";
                diag.Add("SellerFinanceClosing: " + result.FailureReason);
                events?.Record(dayIndex, CreditEventKind.DecisionFailed,
                    terms?.BuyerName ?? "?", terms?.SellerName ?? "?", 0,
                    "seller-finance closing failed: " + result.FailureReason);
                return result;
            }
            if (string.IsNullOrWhiteSpace(terms.SellerName) || string.IsNullOrWhiteSpace(terms.BuyerName)
                || terms.SalePriceCents <= 0 || terms.DownPaymentCents < 0
                || terms.DownPaymentCents > terms.SalePriceCents)
            {
                result.FailureReason = "Closing terms are inconsistent (names, price or down payment).";
                diag.Add("SellerFinanceClosing: " + result.FailureReason);
                events?.Record(dayIndex, CreditEventKind.DecisionFailed, terms.BuyerName, terms.SellerName, 0,
                    "seller-finance closing failed: " + result.FailureReason);
                return result;
            }

            int financed = terms.SalePriceCents - terms.DownPaymentCents;

            // ---- Real cash: the buyer pays the down payment to the seller. ----
            CreditCashAccount buyerCash = cash.OpenWindow(terms.BuyerName, diag);
            CreditCashAccount sellerCash = cash.OpenWindow(terms.SellerName, diag);
            if (buyerCash == null || sellerCash == null)
            {
                result.FailureReason = "Buyer or seller has no registered real cash store — refusing to close on invented money.";
                cash.DiscardWindow(buyerCash); cash.DiscardWindow(sellerCash);
                diag.Add("SellerFinanceClosing: " + result.FailureReason);
                events?.Record(dayIndex, CreditEventKind.DecisionFailed, terms.BuyerName, terms.SellerName, terms.SalePriceCents,
                    "seller-finance closing failed: " + result.FailureReason);
                return result;
            }
            if (terms.DownPaymentCents > 0 && buyerCash.BalanceCents < terms.DownPaymentCents)
            {
                result.FailureReason = $"Buyer '{terms.BuyerName}' holds {buyerCash.BalanceCents}c but the down payment is {terms.DownPaymentCents}c — cannot close.";
                cash.DiscardWindow(buyerCash); cash.DiscardWindow(sellerCash);
                diag.Add("SellerFinanceClosing: " + result.FailureReason);
                events?.Record(dayIndex, CreditEventKind.DecisionFailed, terms.BuyerName, terms.SellerName, terms.SalePriceCents,
                    "seller-finance closing failed: " + result.FailureReason);
                return result;
            }
            buyerCash.BalanceCents -= terms.DownPaymentCents;
            sellerCash.BalanceCents += terms.DownPaymentCents;

            // ---- The seller note: real instrument + real shared obligation. ----
            string noteTerms = $"{terms.AnnualRateBps}bps, {terms.TermDays} days, " +
                (string.IsNullOrWhiteSpace(terms.GuarantorName) ? "no guarantor" : "guarantor " + terms.GuarantorName);
            SellerFinanceNote note = registry.IssueSellerFinanceNote(ids, terms.SellerName, terms.BuyerName,
                terms.AssetDescription, terms.AssetInstanceId, terms.SalePriceCents, terms.DownPaymentCents,
                noteTerms, dayIndex, diag);
            if (note == null)
            {
                cash.DiscardWindow(buyerCash); cash.DiscardWindow(sellerCash);
                result.FailureReason = "The credit registry refused the seller note — closing aborted, cash untouched.";
                diag.Add("SellerFinanceClosing: " + result.FailureReason);
                events?.Record(dayIndex, CreditEventKind.DecisionFailed, terms.BuyerName, terms.SellerName, terms.SalePriceCents,
                    "seller-finance closing failed: " + result.FailureReason);
                return result;
            }
            if (!string.IsNullOrWhiteSpace(terms.GuarantorName) && !string.IsNullOrWhiteSpace(note.ObligationId))
            {
                authority.AddGuaranty(ids, note.ObligationId, terms.SellerName, terms.BuyerName,
                    terms.GuarantorName, Math.Max(0, financed));
            }

            // ---- Security stays separate from ownership: the seller gets a
            // security interest in the asset, NOT the title. ----
            if (!string.IsNullOrWhiteSpace(note.ObligationId) && !string.IsNullOrWhiteSpace(terms.AssetInstanceId))
            {
                SecurityInterestRecord security = authority.AttachSecurity(ids, note.ObligationId,
                    terms.SellerName, terms.BuyerName, new[] { terms.AssetInstanceId },
                    dayIndex, 1, true);
                if (security != null) result.SecurityInterestId = security.SecurityInterestId;
                else diag.Add($"SellerFinanceClosing: security attachment on '{terms.AssetInstanceId}' was refused — note issued without it.");
            }

            // ---- Title/control transfers to the buyer per the deal. ----
            if (titles != null && !string.IsNullOrWhiteSpace(terms.AssetInstanceId)
                && !string.IsNullOrWhiteSpace(titles.CurrentHolder(terms.AssetInstanceId)))
            {
                string titleProblem = titles.TransferTitle(ids, terms.AssetInstanceId, terms.BuyerName,
                    TitleBasis.Purchase, note.InstrumentId.ToString(), dayIndex,
                    $"seller-financed sale — note {note.InstrumentId}, {financed}c carried by seller", diag);
                if (titleProblem != null)
                {
                    diag.Add("SellerFinanceClosing: title transfer refused: " + titleProblem);
                }
                else
                {
                    result.TitleTransferred = true;
                    events?.Record(dayIndex, CreditEventKind.TitleTransferred, terms.BuyerName, terms.SellerName,
                        terms.SalePriceCents,
                        $"title of '{terms.AssetInstanceId}' transferred to '{terms.BuyerName}' on seller-finance closing (note {note.InstrumentId}).",
                        note.ObligationId, note.InstrumentId.ToString());
                }
            }
            else
            {
                diag.Add($"SellerFinanceClosing: '{terms.AssetInstanceId}' is not a titled parcel known to the title authority — " +
                    "title transfer is the owning authority's job; the note and security are still real.");
            }

            // ---- Commit the real cash movement now that every leg succeeded. ----
            string buyerProblem = cash.CommitWindow(buyerCash, dayIndex,
                $"down payment on seller-financed '{terms.AssetDescription}'", terms.SellerName, diag);
            string sellerProblem = cash.CommitWindow(sellerCash, dayIndex,
                $"down payment received on seller-financed '{terms.AssetDescription}'", terms.BuyerName, diag);
            if (buyerProblem != null || sellerProblem != null)
            {
                result.FailureReason = "Real cash movement was refused after the note was issued — manual reconciliation required.";
                diag.Add("SellerFinanceClosing: INCONSISTENCY: " + result.FailureReason);
                events?.Record(dayIndex, CreditEventKind.DecisionFailed, terms.BuyerName, terms.SellerName, terms.DownPaymentCents,
                    "seller-finance closing: " + result.FailureReason, note.ObligationId, note.InstrumentId.ToString());
                return result;
            }

            result.Closed = true;
            result.NoteInstrumentId = note.InstrumentId.ToString();
            result.ObligationId = note.ObligationId;
            result.CashToSellerCents = terms.DownPaymentCents;
            result.FinancedCents = financed;
            diag.Add($"SellerFinanceClosing: CLOSED — '{terms.BuyerName}' bought '{terms.AssetDescription}' for {terms.SalePriceCents}c: " +
                $"{terms.DownPaymentCents}c cash to '{terms.SellerName}' + note {note.InstrumentId} for {financed}c. " +
                $"Cash + note == price. Seller holds a creditor claim and a security interest — never the title.");
            events?.Record(dayIndex, CreditEventKind.SellerNoteIssued, terms.BuyerName, terms.SellerName,
                financed,
                $"seller-finance note {note.InstrumentId}: '{terms.BuyerName}' bought '{terms.AssetDescription}' for {terms.SalePriceCents}c " +
                $"({terms.DownPaymentCents}c down + {financed}c note at {terms.AnnualRateBps}bps/{terms.TermDays}d).",
                note.ObligationId, note.InstrumentId.ToString());
            events?.Record(dayIndex, CreditEventKind.Settlement, terms.BuyerName, terms.SellerName,
                terms.DownPaymentCents,
                $"seller-finance settlement: {terms.DownPaymentCents}c down payment moved buyer → seller in real cash.",
                note.ObligationId, note.InstrumentId.ToString());
            events?.Record(dayIndex, CreditEventKind.PropertyDecision, terms.BuyerName, terms.SellerName,
                terms.SalePriceCents,
                $"property decision: '{terms.AssetDescription}' sold seller-financed; buyer holds title, seller holds claim + security.",
                note.ObligationId, note.InstrumentId.ToString());
            return result;
        }
    }
}
