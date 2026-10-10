using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Economy.Liabilities;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Economy.Financing
{
    /// <summary>
    /// T2A: the credit registry — issues and tracks the five structurally
    /// distinct instruments, keeps contingent guaranty exposure SEPARATE from
    /// principal debt, and runs the default → guaranty-call transition.
    /// Tech X §12.2 (Bakery Buyout): after closing, principal debt can be zero
    /// while contingent guaranteed exposure remains; on buyer default the
    /// guaranty transitions through its agreement rather than disappearing.
    /// Principal obligations post to the SWN-3 liability ledger; contingent
    /// exposure never does until called.
    /// </summary>
    public sealed class CreditRegistry
    {
        private readonly Dictionary<string, PromissoryNote> notes =
            new Dictionary<string, PromissoryNote>(StringComparer.Ordinal);
        private readonly Dictionary<string, SellerFinanceNote> sellerNotes =
            new Dictionary<string, SellerFinanceNote>(StringComparer.Ordinal);
        private readonly Dictionary<string, MortgageDeed> mortgages =
            new Dictionary<string, MortgageDeed>(StringComparer.Ordinal);
        private readonly Dictionary<string, PropertyLien> liens =
            new Dictionary<string, PropertyLien>(StringComparer.Ordinal);
        private readonly Dictionary<string, GuarantyAgreement> guaranties =
            new Dictionary<string, GuarantyAgreement>(StringComparer.Ordinal);

        private readonly List<string> diagnostics = new List<string>();
        private FinancialObligationAuthority financialAuthority;
        public IReadOnlyList<string> Diagnostics => diagnostics;
        public FinancialObligationAuthority FinancialAuthority => financialAuthority;

        public FinancialPaymentRecord ApplyInstrumentPayment(string instrumentId, int amountCents,
            int dayIndex, string payer, string payee, List<string> diag = null)
        {
            string obligationId = FindInstrumentObligationId(instrumentId);
            if (financialAuthority == null || string.IsNullOrWhiteSpace(obligationId))
            {
                diag?.Add("CreditRegistry: instrument has no shared obligation for payment.");
                return null;
            }
            FinancialPaymentRecord payment = financialAuthority.ApplyPayment(
                obligationId, amountCents, dayIndex, payer, payee, instrumentId);
            if (payment == null) diag?.Add("CreditRegistry: shared obligation rejected instrument payment.");
            return payment;
        }

        /// <summary>Attaches the single mutable balance authority; registry remains an instrument index.</summary>
        public void AttachFinancialAuthority(FinancialObligationAuthority authority)
        {
            financialAuthority = authority;
        }

        private string Key(EntityId id) => id.IsValid ? id.ToString() : string.Empty;

        private bool Named(string value, string what, List<string> diag)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                diag.Add($"CreditRegistry: {what} must be named — there are no anonymous parties in credit.");
                return false;
            }
            return true;
        }

        public PromissoryNote IssuePromissoryNote(
            EntityIdRegistry ids, string makerName, string payeeName,
            int principalCents, string terms, int dayIndex,
            List<string> collateralEquipmentAssetIds, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (ids == null) { diag.Add("CreditRegistry: id registry required."); return null; }
            if (!Named(makerName, "maker", diag) | !Named(payeeName, "payee", diag)) return null;
            if (principalCents <= 0) { diag.Add("CreditRegistry: note principal must be positive."); return null; }

            var note = new PromissoryNote
            {
                InstrumentId = ids.Allocate(EntityKind.Contract),
                MakerName = makerName,
                PayeeName = payeeName,
                PrincipalCents = principalCents,
                Terms = terms ?? string.Empty,
                SignedDayIndex = dayIndex,
            };
            if (collateralEquipmentAssetIds != null)
                note.CollateralEquipmentAssetIds.AddRange(collateralEquipmentAssetIds);
            FinancialObligation obligation = financialAuthority?.Create(
                ids, FinancialObligationKind.Loan, makerName, payeeName, principalCents,
                dayIndex, terms, "promissory note");
            if (financialAuthority != null && obligation == null)
            {
                diag.Add("CreditRegistry: shared obligation creation failed.");
                return null;
            }
            note.ObligationId = obligation?.ObligationId ?? string.Empty;
            if (obligation != null && note.CollateralEquipmentAssetIds.Count > 0)
            {
                SecurityInterestRecord security = financialAuthority.AttachSecurity(
                    ids, obligation.ObligationId, payeeName, makerName,
                    note.CollateralEquipmentAssetIds, dayIndex, 1, true);
                if (security == null)
                {
                    diag.Add("CreditRegistry: note security attachment failed.");
                    return null;
                }
            }
            notes[Key(note.InstrumentId)] = note;
            diag.Add($"CreditRegistry: promissory note {note.InstrumentId} — {makerName} promises {payeeName} {principalCents}c ({terms}).");
            return note;
        }

        public SellerFinanceNote IssueSellerFinanceNote(
            EntityIdRegistry ids, string sellerName, string buyerName,
            string assetDescription, string assetInstanceId,
            int salePriceCents, int downPaymentCents, string terms, int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (ids == null) { diag.Add("CreditRegistry: id registry required."); return null; }
            if (!Named(sellerName, "seller", diag) | !Named(buyerName, "buyer", diag)) return null;
            if (salePriceCents <= 0 || downPaymentCents < 0 || downPaymentCents > salePriceCents)
            { diag.Add("CreditRegistry: seller-finance price/down-payment inconsistent."); return null; }

            var note = new SellerFinanceNote
            {
                InstrumentId = ids.Allocate(EntityKind.Contract),
                SellerName = sellerName,
                BuyerName = buyerName,
                AssetDescription = assetDescription ?? string.Empty,
                AssetInstanceId = assetInstanceId ?? string.Empty,
                SalePriceCents = salePriceCents,
                DownPaymentCents = downPaymentCents,
                FinancedCents = salePriceCents - downPaymentCents,
                Terms = terms ?? string.Empty,
                SignedDayIndex = dayIndex,
            };
            if (note.FinancedCents > 0)
            {
                FinancialObligation obligation = financialAuthority?.Create(
                    ids, FinancialObligationKind.SellerFinance, buyerName, sellerName,
                    note.FinancedCents, dayIndex, terms, "seller finance", note.InstrumentId.ToString());
                if (financialAuthority != null && obligation == null)
                {
                    diag.Add("CreditRegistry: shared seller-finance obligation creation failed.");
                    return null;
                }
                note.ObligationId = obligation?.ObligationId ?? string.Empty;
            }
            sellerNotes[Key(note.InstrumentId)] = note;
            diag.Add($"CreditRegistry: seller-finance note {note.InstrumentId} — {sellerName} carries {note.FinancedCents}c for {buyerName} on '{assetDescription}'.");
            return note;
        }

        public MortgageDeed IssueMortgage(
            EntityIdRegistry ids, string borrowerName, string lenderName,
            string propertyId, string propertyDescription,
            int principalCents, string terms, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (ids == null) { diag.Add("CreditRegistry: id registry required."); return null; }
            if (!Named(borrowerName, "borrower", diag) | !Named(lenderName, "lender", diag)) return null;
            if (string.IsNullOrWhiteSpace(propertyId))
            { diag.Add("CreditRegistry: a mortgage is secured by real property — no property, no mortgage."); return null; }
            if (principalCents <= 0) { diag.Add("CreditRegistry: mortgage principal must be positive."); return null; }

            var mortgage = new MortgageDeed
            {
                InstrumentId = ids.Allocate(EntityKind.Contract),
                BorrowerName = borrowerName,
                LenderName = lenderName,
                PropertyId = propertyId,
                PropertyDescription = propertyDescription ?? string.Empty,
                PrincipalCents = principalCents,
                Terms = terms ?? string.Empty,
                SignedDayIndex = dayIndex,
            };
            FinancialObligation obligation = financialAuthority?.Create(
                ids, FinancialObligationKind.Loan, borrowerName, lenderName, principalCents,
                dayIndex, terms, "property finance");
            if (financialAuthority != null && obligation == null)
            {
                diag.Add("CreditRegistry: shared mortgage obligation creation failed.");
                return null;
            }
            mortgage.ObligationId = obligation?.ObligationId ?? string.Empty;
            if (obligation != null)
            {
                SecurityInterestRecord security = financialAuthority.AttachSecurity(
                    ids, obligation.ObligationId, lenderName, borrowerName,
                    new[] { propertyId }, dayIndex, 1, true);
                if (security == null)
                {
                    diag.Add("CreditRegistry: mortgage security attachment failed.");
                    return null;
                }
            }
            mortgages[Key(mortgage.InstrumentId)] = mortgage;
            diag.Add($"CreditRegistry: mortgage {mortgage.InstrumentId} — {borrowerName} / {lenderName}, {principalCents}c secured by '{propertyDescription}'.");
            return mortgage;
        }

        public PropertyLien FileLien(
            EntityIdRegistry ids, string claimantName, string debtorName,
            string propertyOrAssetId, int amountCents, string reason, int dayIndex,
            List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (ids == null) { diag.Add("CreditRegistry: id registry required."); return null; }
            if (!Named(claimantName, "claimant", diag) | !Named(debtorName, "debtor", diag)) return null;
            if (amountCents <= 0) { diag.Add("CreditRegistry: lien amount must be positive."); return null; }

            var lien = new PropertyLien
            {
                InstrumentId = ids.Allocate(EntityKind.Contract),
                ClaimantName = claimantName,
                DebtorName = debtorName,
                PropertyOrAssetId = propertyOrAssetId ?? string.Empty,
                AmountCents = amountCents,
                Reason = reason ?? string.Empty,
                FiledDayIndex = dayIndex,
            };
            liens[Key(lien.InstrumentId)] = lien;
            diag.Add($"CreditRegistry: lien {lien.InstrumentId} — {claimantName} claims {amountCents}c against '{propertyOrAssetId}' ({reason}). Possession does not transfer.");
            return lien;
        }

        public GuarantyAgreement IssueGuaranty(
            EntityIdRegistry ids, string guarantorName, string creditorName,
            string debtorName, string coveredInstrumentId,
            int maxExposureCents, string terms, int dayIndex, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (ids == null) { diag.Add("CreditRegistry: id registry required."); return null; }
            if (!Named(guarantorName, "guarantor", diag) | !Named(creditorName, "creditor", diag) | !Named(debtorName, "debtor", diag)) return null;
            if (maxExposureCents <= 0) { diag.Add("CreditRegistry: guaranty exposure must be positive."); return null; }

            var guaranty = new GuarantyAgreement
            {
                InstrumentId = ids.Allocate(EntityKind.Contract),
                GuarantorName = guarantorName,
                CreditorName = creditorName,
                DebtorName = debtorName,
                CoveredInstrumentId = coveredInstrumentId ?? string.Empty,
                MaxExposureCents = maxExposureCents,
                Terms = terms ?? string.Empty,
                SignedDayIndex = dayIndex,
            };
            if (financialAuthority != null)
            {
                string coveredObligationId = FindInstrumentObligationId(coveredInstrumentId);
                if (string.IsNullOrWhiteSpace(coveredObligationId))
                {
                    diag.Add("CreditRegistry: guaranty requires a shared covered obligation.");
                    return null;
                }
                GuarantyRecord shared = financialAuthority.AddGuaranty(
                    ids, coveredObligationId, creditorName, debtorName, guarantorName, maxExposureCents);
                if (shared == null)
                {
                    diag.Add("CreditRegistry: shared guaranty creation failed.");
                    return null;
                }
                guaranty.FinancialGuarantyId = shared.GuarantyId;
            }
            guaranties[Key(guaranty.InstrumentId)] = guaranty;
            diag.Add($"CreditRegistry: guaranty {guaranty.InstrumentId} — {guarantorName} contingently answers up to {maxExposureCents}c for {debtorName}'s debt to {creditorName}. Contingent, not principal.");
            return guaranty;
        }

        /// <summary>
        /// T2A: contingent exposure is ONLY active guaranties. Called, satisfied
        /// or released guaranties contribute zero. This is the Tech X §12.2
        /// read model: principal debt versus contingent exposure.
        /// </summary>
        public int ContingentExposureFor(string guarantorName)
        {
            int total = 0;
            foreach (GuarantyAgreement g in guaranties.Values)
            {
                if (g.Status == CreditInstrumentStatus.Active &&
                    string.Equals(g.GuarantorName, guarantorName, StringComparison.Ordinal))
                    total += g.MaxExposureCents;
            }
            return total;
        }

        /// <summary>
        /// T2A: amounts actually called under guaranties (now real obligations).
        /// </summary>
        public int CalledExposureFor(string guarantorName)
        {
            int total = 0;
            foreach (GuarantyAgreement g in guaranties.Values)
            {
                if (g.Status == CreditInstrumentStatus.Called &&
                    string.Equals(g.GuarantorName, guarantorName, StringComparison.Ordinal))
                    total += g.CalledAmountCents;
            }
            return total;
        }

        /// <summary>
        /// T2A: records default on a covered instrument and transitions every
        /// active guaranty on it THROUGH ITS AGREEMENT: Active → Called, with
        /// the called amount posted as a real liability for the guarantor when
        /// a business instance id and ledger are supplied. The guaranty never
        /// silently disappears.
        /// </summary>
        public string RecordDefault(
            string coveredInstrumentId, int defaultedAmountCents, int dayIndex,
            BusinessLiabilityLedger liabilityLedger, EntityIdRegistry ids,
            string guarantorBusinessInstanceId, List<string> diag)
        {
            diag = diag ?? diagnostics;
            MarkDefaulted(coveredInstrumentId, diag);
            string coveredObligationId = FindInstrumentObligationId(coveredInstrumentId);
            if (financialAuthority != null && !string.IsNullOrWhiteSpace(coveredObligationId))
                financialAuthority.MarkDelinquent(coveredObligationId);

            bool anyCalled = false;
            foreach (GuarantyAgreement g in guaranties.Values)
            {
                if (g.Status != CreditInstrumentStatus.Active) continue;
                if (!string.Equals(g.CoveredInstrumentId, coveredInstrumentId, StringComparison.Ordinal)) continue;

                g.Status = CreditInstrumentStatus.Called;
                g.CalledAmountCents = Math.Min(g.MaxExposureCents, Math.Max(0, defaultedAmountCents));
                anyCalled = true;
                diag.Add($"CreditRegistry: guaranty {g.InstrumentId} CALLED — {g.GuarantorName} now answers {g.CalledAmountCents}c to {g.CreditorName} per '{g.Terms}'.");

                if (financialAuthority != null && ids != null && !string.IsNullOrWhiteSpace(g.FinancialGuarantyId))
                {
                    FinancialObligation called = financialAuthority.CallGuaranty(
                        ids, g.FinancialGuarantyId, g.CalledAmountCents, dayIndex);
                    if (called == null)
                        diag.Add($"CreditRegistry: shared guaranty call for {g.InstrumentId} was rejected.");
                }
                else if (liabilityLedger != null && ids != null && !string.IsNullOrWhiteSpace(guarantorBusinessInstanceId))
                {
                    liabilityLedger.BuyOnCredit(
                        ids, guarantorBusinessInstanceId, g.CreditorName, g.CalledAmountCents,
                        $"guaranty {g.InstrumentId} called: {g.DebtorName} defaulted on {coveredInstrumentId}",
                        dayIndex, diag);
                }
                else
                {
                    diag.Add($"CreditRegistry: guaranty {g.InstrumentId} called for {g.CalledAmountCents}c — no business ledger supplied, so the obligation is recorded on the guaranty, not yet on a ledger. This is a wiring gap, not forgiven debt.");
                }
            }

            if (!anyCalled)
                diag.Add($"CreditRegistry: default recorded on '{coveredInstrumentId}' — no active guaranty covers it.");
            return null;
        }

        private void MarkDefaulted(string instrumentId, List<string> diag)
        {
            if (notes.TryGetValue(instrumentId, out PromissoryNote n)) n.Status = CreditInstrumentStatus.Defaulted;
            else if (sellerNotes.TryGetValue(instrumentId, out SellerFinanceNote s)) s.Status = CreditInstrumentStatus.Defaulted;
            else if (mortgages.TryGetValue(instrumentId, out MortgageDeed m)) m.Status = CreditInstrumentStatus.Defaulted;
            else diag.Add($"CreditRegistry: default recorded on unknown instrument '{instrumentId}'.");
        }

        private string FindInstrumentObligationId(string instrumentId)
        {
            if (notes.TryGetValue(instrumentId ?? string.Empty, out PromissoryNote note)) return note.ObligationId;
            if (sellerNotes.TryGetValue(instrumentId ?? string.Empty, out SellerFinanceNote seller)) return seller.ObligationId;
            if (mortgages.TryGetValue(instrumentId ?? string.Empty, out MortgageDeed mortgage)) return mortgage.ObligationId;
            return string.Empty;
        }

        /// <summary>
        /// T2A: the underlying debt is satisfied → the guaranty is RELEASED
        /// (not silently dropped): contingent exposure returns to zero through
        /// an explicit terminal state.
        /// </summary>
        public string SatisfyInstrument(string instrumentId, List<string> diag)
        {
            diag = diag ?? diagnostics;
            string linkedObligationId = FindInstrumentObligationId(instrumentId);
            if (financialAuthority != null && !string.IsNullOrWhiteSpace(linkedObligationId))
            {
                FinancialObligation linked = financialAuthority.Find(linkedObligationId);
                if (linked != null && !linked.Settled)
                    return $"CreditRegistry: instrument '{instrumentId}' cannot be satisfied while shared obligation has {linked.TotalOutstandingCents}c outstanding.";
            }
            if (notes.TryGetValue(instrumentId, out PromissoryNote n)) n.Status = CreditInstrumentStatus.Satisfied;
            else if (sellerNotes.TryGetValue(instrumentId, out SellerFinanceNote s)) s.Status = CreditInstrumentStatus.Satisfied;
            else if (mortgages.TryGetValue(instrumentId, out MortgageDeed m)) m.Status = CreditInstrumentStatus.Satisfied;
            else if (liens.TryGetValue(instrumentId, out PropertyLien l)) l.Status = CreditInstrumentStatus.Satisfied;
            else return $"CreditRegistry: unknown instrument '{instrumentId}'.";

            foreach (GuarantyAgreement g in guaranties.Values)
            {
                if (g.Status == CreditInstrumentStatus.Active &&
                    string.Equals(g.CoveredInstrumentId, instrumentId, StringComparison.Ordinal))
                {
                    g.Status = CreditInstrumentStatus.Released;
                    diag.Add($"CreditRegistry: guaranty {g.InstrumentId} RELEASED — underlying debt satisfied. Exposure was contingent and is now zero.");
                }
            }
            return null;
        }

        /// <summary>
        /// W7C: read-only lookup so the note desk can verify paper before
        /// discounting or redeeming it. The registry stays the authority;
        /// this only exposes what it already holds.
        /// </summary>
        public bool TryGetPromissoryNote(EntityId instrumentId, out PromissoryNote note)
        {
            note = null;
            if (instrumentId.IsValid && notes.TryGetValue(Key(instrumentId), out PromissoryNote found))
            {
                note = found;
                return true;
            }
            return false;
        }

        public int InstrumentCount =>
            notes.Count + sellerNotes.Count + mortgages.Count + liens.Count + guaranties.Count;

        #region Save / Load
        [Serializable]
        public sealed class CreditRegistrySaveDto
        {
            public List<PromissoryNote> Notes = new List<PromissoryNote>();
            public List<SellerFinanceNote> SellerNotes = new List<SellerFinanceNote>();
            public List<MortgageDeed> Mortgages = new List<MortgageDeed>();
            public List<PropertyLien> Liens = new List<PropertyLien>();
            public List<GuarantyAgreement> Guaranties = new List<GuarantyAgreement>();
        }

        public CreditRegistrySaveDto CaptureSaveDto()
        {
            return new CreditRegistrySaveDto
            {
                Notes = new List<PromissoryNote>(notes.Values),
                SellerNotes = new List<SellerFinanceNote>(sellerNotes.Values),
                Mortgages = new List<MortgageDeed>(mortgages.Values),
                Liens = new List<PropertyLien>(liens.Values),
                Guaranties = new List<GuarantyAgreement>(guaranties.Values),
            };
        }

        public void LoadFromSaveDto(CreditRegistrySaveDto dto)
        {
            notes.Clear(); sellerNotes.Clear(); mortgages.Clear(); liens.Clear(); guaranties.Clear();
            if (dto == null) return;
            foreach (var n in dto.Notes) notes[Key(n.InstrumentId)] = n;
            foreach (var s in dto.SellerNotes) sellerNotes[Key(s.InstrumentId)] = s;
            foreach (var m in dto.Mortgages) mortgages[Key(m.InstrumentId)] = m;
            foreach (var l in dto.Liens) liens[Key(l.InstrumentId)] = l;
            foreach (var g in dto.Guaranties) guaranties[Key(g.InstrumentId)] = g;
        }
        #endregion
    }
}
