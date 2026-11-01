using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Primitives;
using LandLedgers.World.Property;
using UnityEngine;

namespace LandLedgers.Economy.Financing
{
    /// <summary>
    /// Phase E (E1): the real delinquency sweep. Missed payments move through
    /// the shared authority only (MarkDue / MarkDelinquent / MarkDefaulted),
    /// then each past-due obligation gets its creditor's chosen response via
    /// <see cref="CreditParticipantService.ChooseDelinquencyResponse"/>:
    /// tolerate, request payment, offer workout (restructure the SAME
    /// obligation — never a new principal), accelerate, or enforce security.
    /// Guaranty calls and foreclosure execute here in production flows, not
    /// just in tests. Rent arrears obligations flow through the same sweep —
    /// they are real obligations, so no special-casing is needed.
    /// </summary>
    public sealed class CreditWorkoutService
    {
        /// <summary>TUNING: days past maturity before an Active obligation is marked delinquent by the sweep.</summary>
        public int DelinquentAfterDaysPastMaturity = 7;

        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>
        /// Sweeps every obligation in the authority: marks newly past-due
        /// obligations delinquent, then applies each creditor's chosen
        /// response to every Due/Delinquent/Defaulted obligation.
        /// </summary>
        public void SweepDay(EntityIdRegistry ids, FinancialObligationAuthority authority,
            CreditRegistry registry, CreditCashBridge cash, ForeclosureService foreclosure,
            TitleAuthority titles, IReadOnlyDictionary<string, CreditParticipantService> creditors,
            int dayIndex, CreditEventLog events, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (authority == null) { diag.Add("CreditWorkoutService: no obligation authority — sweep skipped."); return; }

            // Snapshot: workout responses create new obligations (guaranty
            // calls, recovery claims), so the sweep never enumerates live.
            var snapshot = new List<FinancialObligation>(authority.Obligations);

            foreach (FinancialObligation obligation in snapshot)
            {
                if (obligation == null || obligation.Settled) continue;
                if (obligation.Status == FinancialObligationStatus.Active
                    && obligation.MaturityDayIndex >= 0
                    && dayIndex > obligation.MaturityDayIndex + DelinquentAfterDaysPastMaturity)
                {
                    authority.MarkDelinquent(obligation.ObligationId);
                    diag.Add($"CreditWorkoutService: '{obligation.ObligationId}' ({obligation.Debtor} → {obligation.Creditor}) " +
                        $"marked delinquent — past maturity day {obligation.MaturityDayIndex}.");
                    events?.Record(dayIndex, CreditEventKind.DelinquencyMarked, obligation.Debtor, obligation.Creditor,
                        obligation.TotalOutstandingCents,
                        $"obligation '{obligation.ObligationId}' marked delinquent (past maturity).",
                        obligation.ObligationId);
                }
            }

            foreach (FinancialObligation obligation in snapshot)
            {
                if (obligation == null || obligation.Settled) continue;
                if (obligation.Status != FinancialObligationStatus.Due
                    && obligation.Status != FinancialObligationStatus.Delinquent
                    && obligation.Status != FinancialObligationStatus.Defaulted)
                    continue;
                ProcessObligation(ids, authority, registry, cash, foreclosure, titles,
                    creditors, obligation, dayIndex, events, diag);
            }
        }

        /// <summary>
        /// Runs the workout for an explicit set of obligation ids (e.g. rent
        /// arrears records) without sweeping the whole authority.
        /// </summary>
        public void ProcessNamedObligations(EntityIdRegistry ids, FinancialObligationAuthority authority,
            CreditRegistry registry, CreditCashBridge cash, ForeclosureService foreclosure,
            TitleAuthority titles, IReadOnlyDictionary<string, CreditParticipantService> creditors,
            IEnumerable<string> obligationIds, int dayIndex, CreditEventLog events, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (obligationIds == null) return;
            foreach (string obligationId in obligationIds)
            {
                FinancialObligation obligation = authority?.Find(obligationId);
                if (obligation == null || obligation.Settled) continue;
                if (obligation.Status != FinancialObligationStatus.Due
                    && obligation.Status != FinancialObligationStatus.Delinquent
                    && obligation.Status != FinancialObligationStatus.Defaulted)
                    continue;
                ProcessObligation(ids, authority, registry, cash, foreclosure, titles,
                    creditors, obligation, dayIndex, events, diag);
            }
        }

        private void ProcessObligation(EntityIdRegistry ids, FinancialObligationAuthority authority,
            CreditRegistry registry, CreditCashBridge cash, ForeclosureService foreclosure,
            TitleAuthority titles, IReadOnlyDictionary<string, CreditParticipantService> creditors,
            FinancialObligation obligation, int dayIndex, CreditEventLog events, List<string> diag)
        {
            CreditParticipantService creditor = null;
            if (creditors != null)
                creditors.TryGetValue(obligation.Creditor, out creditor);

            int creditorCash = 0;
            IRealCashStore creditorStore = cash?.FindStore(obligation.Creditor);
            if (creditorStore != null) creditorCash = creditorStore.ReadBalanceCents();

            bool securityAvailable = obligation.SecurityInterestIds.Count > 0;
            bool relationshipMatters = creditor == null
                || creditor.Role == CreditParticipantRole.PrivatePerson
                || creditor.Role == CreditParticipantRole.Seller
                || creditor.Role == CreditParticipantRole.Supplier;

            CreditDelinquencyResponse response = creditor != null
                ? creditor.ChooseDelinquencyResponse(obligation, creditorCash, securityAvailable, relationshipMatters)
                : CreditDelinquencyResponse.RequestPayment;

            switch (response)
            {
                case CreditDelinquencyResponse.Tolerate:
                    diag.Add($"CreditWorkoutService: '{obligation.Creditor}' tolerates '{obligation.ObligationId}' " +
                        $"({obligation.TotalOutstandingCents}c) — relationship forbearance, no action.");
                    events?.Record(dayIndex, CreditEventKind.WorkoutOffered, obligation.Debtor, obligation.Creditor,
                        obligation.TotalOutstandingCents,
                        $"creditor tolerates delinquency on '{obligation.ObligationId}' (forbearance).",
                        obligation.ObligationId);
                    break;
                case CreditDelinquencyResponse.RequestPayment:
                    diag.Add($"CreditWorkoutService: '{obligation.Creditor}' requests payment on '{obligation.ObligationId}' " +
                        $"({obligation.TotalOutstandingCents}c outstanding).");
                    events?.Record(dayIndex, CreditEventKind.PaymentMissed, obligation.Debtor, obligation.Creditor,
                        obligation.TotalOutstandingCents,
                        $"creditor requested payment on '{obligation.ObligationId}' — awaiting borrower.",
                        obligation.ObligationId);
                    break;
                case CreditDelinquencyResponse.OfferWorkout:
                    Restructure(ids, authority, obligation, dayIndex, events, diag);
                    break;
                case CreditDelinquencyResponse.Accelerate:
                    Accelerate(ids, authority, cash, obligation, dayIndex, events, diag);
                    break;
                case CreditDelinquencyResponse.EnforceSecurity:
                    EnforceSecurity(ids, authority, registry, cash, foreclosure, titles,
                        obligation, dayIndex, events, diag);
                    break;
            }
        }

        /// <summary>
        /// Workout restructuring edits the SAME obligation's terms — maturity
        /// extension and/or installment conversion. Principal is never
        /// rewritten and no successor obligation is created, so the shared
        /// authority's conservation invariants hold.
        /// </summary>
        public string Restructure(EntityIdRegistry ids, FinancialObligationAuthority authority,
            FinancialObligation obligation, int dayIndex, CreditEventLog events, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (authority == null || obligation == null || obligation.Settled)
                return "CreditWorkoutService.Restructure: a live obligation is required.";
            int extensionDays = 90;
            int oldMaturity = obligation.MaturityDayIndex;
            obligation.MaturityDayIndex = (obligation.MaturityDayIndex < 0 ? dayIndex : obligation.MaturityDayIndex) + extensionDays;
            if (obligation.PaymentTerms != null
                && obligation.PaymentTerms.Structure == FinancialPaymentStructure.MaturityPrincipal)
            {
                obligation.PaymentTerms.Structure = FinancialPaymentStructure.PrincipalInstallments;
                obligation.PaymentTerms.PaymentIntervalDays = 30;
                int installments = Math.Max(1, extensionDays / 30);
                obligation.PaymentTerms.InstallmentPrincipalCents =
                    Math.Max(1, obligation.OutstandingPrincipalCents / installments);
            }
            if (obligation.Status == FinancialObligationStatus.Delinquent)
                authority.MarkDue(obligation.ObligationId);
            string summary = $"workout: '{obligation.ObligationId}' restructured — maturity {oldMaturity} → {obligation.MaturityDayIndex}, " +
                $"installments of {obligation.PaymentTerms?.InstallmentPrincipalCents ?? 0}c every 30 days. Principal unchanged at {obligation.OutstandingPrincipalCents}c.";
            diag.Add("CreditWorkoutService: " + summary);
            events?.Record(dayIndex, CreditEventKind.WorkoutRestructured, obligation.Debtor, obligation.Creditor,
                obligation.TotalOutstandingCents, summary, obligation.ObligationId);
            return null;
        }

        /// <summary>
        /// Acceleration marks the obligation defaulted and calls every live
        /// guaranty through the shared authority — the guarantor's called
        /// exposure is a real new obligation (FinancialObligationKind.
        /// GuarantyCall), never a clone of the covered principal.
        /// </summary>
        public void Accelerate(EntityIdRegistry ids, FinancialObligationAuthority authority,
            CreditCashBridge cash, FinancialObligation obligation, int dayIndex,
            CreditEventLog events, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (authority == null || obligation == null || obligation.Settled) return;
            authority.MarkDefaulted(obligation.ObligationId);
            diag.Add($"CreditWorkoutService: '{obligation.ObligationId}' accelerated — marked defaulted " +
                $"({obligation.TotalOutstandingCents}c).");
            events?.Record(dayIndex, CreditEventKind.DelinquencyMarked, obligation.Debtor, obligation.Creditor,
                obligation.TotalOutstandingCents,
                $"obligation '{obligation.ObligationId}' accelerated to default.",
                obligation.ObligationId);

            foreach (string guarantyId in obligation.GuarantyIds)
            {
                GuarantyRecord guaranty = null;
                foreach (GuarantyRecord g in authority.Guaranties)
                    if (g != null && string.Equals(g.GuarantyId, guarantyId, StringComparison.Ordinal)) { guaranty = g; break; }
                if (guaranty == null || guaranty.Called || guaranty.Released) continue;

                FinancialObligation called = authority.CallGuaranty(ids, guaranty.GuarantyId,
                    obligation.TotalOutstandingCents, dayIndex);
                if (called == null)
                {
                    diag.Add($"CreditWorkoutService: guaranty '{guarantyId}' call was refused by the authority.");
                    continue;
                }
                diag.Add($"CreditWorkoutService: guaranty '{guarantyId}' CALLED — '{guaranty.Guarantor}' now owes " +
                    $"'{guaranty.Creditor}' {called.TotalOutstandingCents}c as a real guaranty-call obligation. Covered principal is NOT duplicated.");
                events?.Record(dayIndex, CreditEventKind.GuarantyCalled, guaranty.Guarantor, guaranty.Creditor,
                    called.TotalOutstandingCents,
                    $"guaranty '{guarantyId}' called: '{guaranty.Guarantor}' answers {called.TotalOutstandingCents}c for '{obligation.Debtor}'s default.",
                    called.ObligationId, guarantyId);

                TryCollectFromGuarantor(ids, authority, cash, guaranty, called, obligation, dayIndex, events, diag);
            }
        }

        /// <summary>
        /// Attempts immediate settlement of the called guaranty from the
        /// guarantor's real cash. If the guarantor cannot cover it, the called
        /// obligation stays open — the debt is owed, not forgiven.
        /// </summary>
        private void TryCollectFromGuarantor(EntityIdRegistry ids, FinancialObligationAuthority authority,
            CreditCashBridge cash, GuarantyRecord guaranty, FinancialObligation called,
            FinancialObligation covered, int dayIndex, CreditEventLog events, List<string> diag)
        {
            if (cash == null) return;
            IRealCashStore guarantorStore = cash.FindStore(guaranty.Guarantor);
            IRealCashStore creditorStore = cash.FindStore(guaranty.Creditor);
            if (guarantorStore == null || creditorStore == null)
            {
                diag.Add($"CreditWorkoutService: guarantor '{guaranty.Guarantor}' has no registered cash store — called obligation stays open.");
                return;
            }
            int owed = called.TotalOutstandingCents;
            if (guarantorStore.ReadBalanceCents() < owed)
            {
                diag.Add($"CreditWorkoutService: guarantor '{guaranty.Guarantor}' holds {guarantorStore.ReadBalanceCents()}c " +
                    $"against {owed}c called — cannot settle now; the obligation remains open.");
                return;
            }
            CreditCashAccount guarantorCash = cash.OpenWindow(guaranty.Guarantor, diag);
            CreditCashAccount creditorCash = cash.OpenWindow(guaranty.Creditor, diag);
            if (guarantorCash == null || creditorCash == null)
            {
                cash.DiscardWindow(guarantorCash); cash.DiscardWindow(creditorCash);
                return;
            }
            guarantorCash.BalanceCents -= owed;
            creditorCash.BalanceCents += owed;
            string gProblem = cash.CommitWindow(guarantorCash, dayIndex,
                $"guaranty settlement for '{covered.ObligationId}'", guaranty.Creditor, diag);
            string cProblem = cash.CommitWindow(creditorCash, dayIndex,
                $"guaranty receipt for '{covered.ObligationId}'", guaranty.Guarantor, diag);
            if (gProblem != null || cProblem != null)
            {
                diag.Add("CreditWorkoutService: guarantor cash movement refused — obligation accounting left untouched.");
                return;
            }
            FinancialObligation recovery = authority.SettleGuarantyPayment(ids, guaranty.GuarantyId, owed, dayIndex,
                "guaranty-settlement:" + guaranty.GuarantyId + ":" + dayIndex);
            diag.Add($"CreditWorkoutService: guarantor '{guaranty.Guarantor}' paid {owed}c on the call; " +
                $"subrogation claim '{recovery?.ObligationId}' created against '{guaranty.PrimaryDebtor}'. Creditor paid once.");
            events?.Record(dayIndex, CreditEventKind.GuarantySettled, guaranty.Guarantor, guaranty.Creditor,
                owed, $"guaranty '{guaranty.GuarantyId}' settled for {owed}c; subrogation claim against '{guaranty.PrimaryDebtor}'.",
                recovery != null ? recovery.ObligationId : called.ObligationId, guaranty.GuarantyId);
        }

        /// <summary>
        /// Security enforcement: a mortgage-backed obligation opens a real
        /// foreclosure case (notice → sale per the service's own rules);
        /// non-mortgage security is reported honestly as unenforceable here.
        /// </summary>
        public void EnforceSecurity(EntityIdRegistry ids, FinancialObligationAuthority authority,
            CreditRegistry registry, CreditCashBridge cash, ForeclosureService foreclosure,
            TitleAuthority titles, FinancialObligation obligation, int dayIndex,
            CreditEventLog events, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (foreclosure == null)
            {
                diag.Add("CreditWorkoutService: no foreclosure service wired — enforcement deferred, debt stands.");
                return;
            }
            MortgageDeed mortgage = FindMortgageFor(registry, obligation.ObligationId);
            if (mortgage == null)
            {
                diag.Add($"CreditWorkoutService: '{obligation.ObligationId}' has security but no mortgage instrument — " +
                    "no foreclosure case opened. Enforcement of non-mortgage collateral is unwired; the debt stands.");
                events?.Record(dayIndex, CreditEventKind.DecisionFailed, obligation.Debtor, obligation.Creditor,
                    obligation.TotalOutstandingCents,
                    $"enforcement requested on '{obligation.ObligationId}' but no mortgage instrument exists — deferred.",
                    obligation.ObligationId);
                return;
            }
            authority?.MarkDefaulted(obligation.ObligationId);
            ForeclosureCase kase = foreclosure.RecordDefault(ids, mortgage,
                obligation.TotalOutstandingCents, dayIndex, diag);
            if (kase == null)
            {
                diag.Add("CreditWorkoutService: foreclosure case could not be opened.");
                return;
            }
            string noticeProblem = foreclosure.IssueNotice(kase, dayIndex, diag);
            if (noticeProblem != null) diag.Add("CreditWorkoutService: " + noticeProblem);
            events?.Record(dayIndex, CreditEventKind.ForeclosureNotice, obligation.Debtor, obligation.Creditor,
                obligation.TotalOutstandingCents,
                $"foreclosure case '{kase.CaseId}' opened on '{mortgage.PropertyId}' — notice issued, cure period running.",
                obligation.ObligationId, mortgage.InstrumentId.ToString());
        }

        private MortgageDeed FindMortgageFor(CreditRegistry registry, string obligationId)
        {
            if (registry == null || string.IsNullOrWhiteSpace(obligationId)) return null;
            foreach (MortgageDeed mortgage in registry.CaptureSaveDto().Mortgages)
                if (mortgage != null && string.Equals(mortgage.ObligationId, obligationId, StringComparison.Ordinal))
                    return mortgage;
            return null;
        }
    }
}
