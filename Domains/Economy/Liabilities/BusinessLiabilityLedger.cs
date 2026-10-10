using EntityId = LandLedgers.Primitives.EntityId;

using System;
using System.Collections.Generic;
using LandLedgers.Population;
using LandLedgers.Primitives;
using LandLedgers.ReadModels.Valuation;
using LandLedgers.Economy.Financing;
using UnityEngine;

namespace LandLedgers.Economy.Liabilities
{
    /// <summary>SWN-3: the two ways a business honestly comes to owe money.</summary>
    public enum LiabilityKind
    {
        Unspecified = 0,
        Loan = 1,    // borrowed funds: bank loan, seller-finance (Canon §14.1)
        Payable = 2, // bought on credit: supplies/inputs not yet paid for
    }

    /// <summary>
    /// SWN-3: one business liability. The counterparty is always NAMED — there
    /// are no anonymous lenders. Terms are recorded as written, not synthesized.
    /// </summary>
    [Serializable]
    public sealed class BusinessLiability
    {
        // EntityKind.Contract: a liability is a financial agreement (HF-1).
        public EntityId LiabilityId = EntityId.Invalid;
        /// <summary>Shared FinancialObligation identity; LiabilityId is retained for compatibility.</summary>
        public string ObligationId = string.Empty;
        public string BusinessInstanceId = string.Empty;
        public LiabilityKind Kind;
        public string Counterparty = string.Empty; // lender / supplier name
        public string Terms = string.Empty;        // e.g. "8% annual, due day 900"
        public string Reason = string.Empty;       // why the debt was taken
        public int PrincipalCents;
        public int BalanceCents;
        public int OpenedDayIndex;
        public bool Settled => BalanceCents <= 0;

        public BusinessLiability() { }
    }

    /// <summary>
    /// SWN-3: the business liability ledger. Liabilities arise ONLY from real
    /// events — borrowed money (Loan) or bought-on-credit supplies (Payable).
    /// There is no method that invents a liability: every entry names its
    /// counterparty and reason. This is the settlement path CLN-4 was missing:
    /// every balance change posts to the BIZ-5 valuation read model, so
    /// OwnerEquityValue (gross less business-specific liabilities) finally has
    /// honest inputs. Canon §12.5 lists Debt/Liabilities on the main financial
    /// HUD — this ledger is the source of truth behind that line.
    /// </summary>
    public sealed class BusinessLiabilityLedger
    {
        private readonly List<BusinessLiability> liabilities = new List<BusinessLiability>();
        private EnterpriseValuationReadModel valuation;
        private FinancialObligationAuthority financialAuthority;

        /// <summary>Wires the BIZ-5 read model (called by the systems hub).</summary>
        public void AttachValuation(EnterpriseValuationReadModel valuationReadModel)
        {
            valuation = valuationReadModel;
        }

        /// <summary>Routes new mutations to the shared finance authority when wired.</summary>
        public void AttachFinancialAuthority(FinancialObligationAuthority authority)
        {
            financialAuthority = authority;
        }

        public IReadOnlyList<BusinessLiability> All => liabilities;

        /// <summary>
        /// Records borrowing: a Loan liability plus the LoanProceeds inflow on
        /// the borrower's ledger (HF-4: borrowed funds carry the obligation
        /// reference). The lender is named; terms are recorded as written.
        /// </summary>
        public BusinessLiability Borrow(
            EntityIdRegistry idRegistry,
            string businessInstanceId,
            string lenderName,
            int principalCents,
            string terms,
            string reason,
            int dayIndex,
            HouseholdLedger borrowerLedger,
            List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            if (idRegistry == null) return null;
            if (string.IsNullOrWhiteSpace(businessInstanceId))
            {
                diagnostics.Add("BusinessLiabilityLedger.Borrow: no business — debt needs a debtor.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(lenderName))
            {
                diagnostics.Add("BusinessLiabilityLedger.Borrow: no lender named — there are no anonymous lenders.");
                return null;
            }
            if (principalCents <= 0)
            {
                diagnostics.Add("BusinessLiabilityLedger.Borrow: principal must be positive.");
                return null;
            }

            FinancialObligation obligation = financialAuthority?.Create(
                idRegistry, FinancialObligationKind.Loan, businessInstanceId, lenderName,
                principalCents, dayIndex, terms, reason);
            if (financialAuthority != null && obligation == null)
            {
                diagnostics.Add("BusinessLiabilityLedger.Borrow: shared obligation creation failed.");
                return null;
            }

            var liability = new BusinessLiability
            {
                LiabilityId = obligation != null ? obligation.ObligationEntityId : idRegistry.Allocate(EntityKind.Contract),
                ObligationId = obligation?.ObligationId ?? string.Empty,
                BusinessInstanceId = businessInstanceId,
                Kind = LiabilityKind.Loan,
                Counterparty = lenderName,
                Terms = terms ?? string.Empty,
                Reason = reason ?? string.Empty,
                PrincipalCents = principalCents,
                BalanceCents = principalCents,
                OpenedDayIndex = dayIndex,
            };
            liabilities.Add(liability);

            if (borrowerLedger != null)
            {
                string problem = borrowerLedger.RecordInflow(
                    dayIndex, principalCents,
                    HouseholdIncomeSource.LoanProceeds,
                    liability.LiabilityId.ToString(),
                    $"loan: {principalCents}c from {lenderName} ({terms}) — {reason}",
                    lenderName);
                if (problem != null) diagnostics.Add("BusinessLiabilityLedger.Borrow ledger note: " + problem);
            }
            else
            {
                diagnostics.Add("BusinessLiabilityLedger.Borrow: no borrower ledger — the cash inflow is unrecorded. This is a wiring gap, not free money.");
            }

            PostValuation(businessInstanceId);
            diagnostics.Add($"BusinessLiabilityLedger: {businessInstanceId} borrowed {principalCents}c from {lenderName} ({terms}).");
            return liability;
        }

        /// <summary>
        /// Records a bought-on-credit payable: supplies/inputs received, not
        /// yet paid for. No cash moves now — the obligation is the real event.
        /// </summary>
        public BusinessLiability BuyOnCredit(
            EntityIdRegistry idRegistry,
            string businessInstanceId,
            string supplierName,
            int amountCents,
            string description,
            int dayIndex,
            List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            if (idRegistry == null) return null;
            if (string.IsNullOrWhiteSpace(businessInstanceId))
            {
                diagnostics.Add("BusinessLiabilityLedger.BuyOnCredit: no business — payables need a debtor.");
                return null;
            }
            if (string.IsNullOrWhiteSpace(supplierName))
            {
                diagnostics.Add("BusinessLiabilityLedger.BuyOnCredit: no supplier named — no anonymous creditors.");
                return null;
            }
            if (amountCents <= 0)
            {
                diagnostics.Add("BusinessLiabilityLedger.BuyOnCredit: amount must be positive.");
                return null;
            }

            FinancialObligation obligation = financialAuthority?.Create(
                idRegistry, FinancialObligationKind.Payable, businessInstanceId, supplierName,
                amountCents, dayIndex, "due on demand unless agreed otherwise", description);
            if (financialAuthority != null && obligation == null)
            {
                diagnostics.Add("BusinessLiabilityLedger.BuyOnCredit: shared obligation creation failed.");
                return null;
            }

            var liability = new BusinessLiability
            {
                LiabilityId = obligation != null ? obligation.ObligationEntityId : idRegistry.Allocate(EntityKind.Contract),
                ObligationId = obligation?.ObligationId ?? string.Empty,
                BusinessInstanceId = businessInstanceId,
                Kind = LiabilityKind.Payable,
                Counterparty = supplierName,
                Terms = "due on demand unless agreed otherwise",
                Reason = description ?? string.Empty,
                PrincipalCents = amountCents,
                BalanceCents = amountCents,
                OpenedDayIndex = dayIndex,
            };
            liabilities.Add(liability);

            PostValuation(businessInstanceId);
            diagnostics.Add($"BusinessLiabilityLedger: {businessInstanceId} owes {supplierName} {amountCents}c ({description}).");
            return liability;
        }

        /// <summary>
        /// Repays a liability: the outflow carries provenance and the balance
        /// drops. Overpayment is refused — money does not vanish into a
        /// settled obligation.
        /// </summary>
        public string Repay(
            string liabilityId,
            int amountCents,
            int dayIndex,
            HouseholdLedger payerLedger,
            List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            BusinessLiability liability = Find(liabilityId);
            if (liability == null)
            {
                return $"BusinessLiabilityLedger.Repay: unknown liability '{liabilityId}'.";
            }
            if (liability.Settled)
            {
                return $"BusinessLiabilityLedger.Repay: liability {liabilityId} is already settled.";
            }
            if (amountCents <= 0)
            {
                return "BusinessLiabilityLedger.Repay: repayment must be positive.";
            }
            if (amountCents > liability.BalanceCents)
            {
                return $"BusinessLiabilityLedger.Repay: {amountCents}c exceeds the {liability.BalanceCents}c balance — overpayment refused.";
            }
            if (payerLedger == null)
            {
                return "BusinessLiabilityLedger.Repay: no payer ledger — the outflow requires provenance (Canon 13.2).";
            }

            if (financialAuthority != null && !string.IsNullOrWhiteSpace(liability.ObligationId))
            {
                FinancialPaymentRecord payment = financialAuthority.ApplyPayment(
                    liability.ObligationId, amountCents, dayIndex,
                    liability.BusinessInstanceId, liability.Counterparty);
                if (payment == null)
                {
                    return "BusinessLiabilityLedger.Repay: shared financial obligation rejected the payment.";
                }
                liability.BalanceCents = financialAuthority.Find(liability.ObligationId)?.TotalOutstandingCents ?? liability.BalanceCents;
            }
            else
            {
                liability.BalanceCents -= amountCents;
            }
            string problem = payerLedger.RecordOutflow(
                dayIndex, amountCents,
                $"debt repayment: {amountCents}c to {liability.Counterparty} against {liabilityId}",
                liability.Counterparty);
            if (problem != null) diagnostics.Add("BusinessLiabilityLedger.Repay ledger note: " + problem);

            PostValuation(liability.BusinessInstanceId);
            diagnostics.Add($"BusinessLiabilityLedger: repaid {amountCents}c to {liability.Counterparty}; balance now {liability.BalanceCents}c.");
            return null;
        }

        /// <summary>
        /// Accrues interest per the liability's terms. Interest is computed
        /// from the recorded terms by the caller and passed in — this method
        /// never invents a rate.
        /// </summary>
        public string AccrueInterest(string liabilityId, int interestCents, int dayIndex, List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            BusinessLiability liability = Find(liabilityId);
            if (liability == null)
            {
                return $"BusinessLiabilityLedger.AccrueInterest: unknown liability '{liabilityId}'.";
            }
            if (liability.Settled)
            {
                return $"BusinessLiabilityLedger.AccrueInterest: liability {liabilityId} is settled — no interest accrues.";
            }
            if (interestCents <= 0)
            {
                return "BusinessLiabilityLedger.AccrueInterest: interest must be positive.";
            }

            if (financialAuthority != null && !string.IsNullOrWhiteSpace(liability.ObligationId))
            {
                string problem = financialAuthority.AccrueInterest(liability.ObligationId, interestCents);
                if (problem != null) return problem;
                liability.BalanceCents = financialAuthority.Find(liability.ObligationId)?.TotalOutstandingCents ?? liability.BalanceCents;
            }
            else
            {
                liability.BalanceCents += interestCents;
            }
            PostValuation(liability.BusinessInstanceId);
            diagnostics.Add($"BusinessLiabilityLedger: {interestCents}c interest accrued on {liabilityId} ({liability.Terms}).");
            return null;
        }

        /// <summary>
        /// Restructures a received supplier payable into a formal shared note.
        /// The payable is superseded; the compatibility list receives only a
        /// projection of the successor obligation, never a second balance writer.
        /// </summary>
        public BusinessLiability ConvertPayableToNote(
            string liabilityId, EntityIdRegistry idRegistry, string terms, int dayIndex,
            List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            BusinessLiability payable = Find(liabilityId);
            if (financialAuthority == null || payable == null || payable.Kind != LiabilityKind.Payable
                || string.IsNullOrWhiteSpace(payable.ObligationId))
            {
                diagnostics.Add("BusinessLiabilityLedger.ConvertPayableToNote: shared payable authority is required.");
                return null;
            }
            FinancialObligation note = financialAuthority.ConvertPayableToNote(
                idRegistry, payable.ObligationId, terms, dayIndex);
            if (note == null) return null;
            payable.BalanceCents = 0;
            var projection = new BusinessLiability
            {
                LiabilityId = note.ObligationEntityId,
                ObligationId = note.ObligationId,
                BusinessInstanceId = payable.BusinessInstanceId,
                Kind = LiabilityKind.Loan,
                Counterparty = payable.Counterparty,
                Terms = terms ?? string.Empty,
                Reason = "supplier payable restructured as formal note",
                PrincipalCents = note.OriginalPrincipalCents,
                BalanceCents = note.TotalOutstandingCents,
                OpenedDayIndex = dayIndex,
            };
            liabilities.Add(projection);
            PostValuation(payable.BusinessInstanceId);
            return projection;
        }

        /// <summary>Total outstanding liabilities for a business (BIZ-5 input).</summary>
        public int TotalLiabilitiesFor(string businessInstanceId)
        {
            int total = 0;
            foreach (var liability in liabilities)
            {
                if (liability != null && !liability.Settled
                    && string.Equals(liability.BusinessInstanceId, businessInstanceId, StringComparison.Ordinal))
                {
                    total += Math.Max(0, liability.BalanceCents);
                }
            }
            return total;
        }

        private BusinessLiability Find(string liabilityId)
        {
            if (string.IsNullOrWhiteSpace(liabilityId)) return null;
            foreach (var liability in liabilities)
            {
                if (liability != null && liability.LiabilityId.ToString() == liabilityId)
                {
                    return liability;
                }
            }
            return null;
        }

        private void PostValuation(string businessInstanceId)
        {
            if (valuation == null || string.IsNullOrWhiteSpace(businessInstanceId)) return;
            valuation.RecordLiabilities(businessInstanceId, TotalLiabilitiesFor(businessInstanceId));
        }

        /// <summary>
        /// Re-pushes every business's total to the valuation. Call after
        /// businesses register with the valuation read model (registration
        /// order): liabilities recorded before a business registers would
        /// otherwise be invisible to it. The systems hub calls this on Awake;
        /// scenario bootstrap should call it after registering businesses.
        /// </summary>
        public void SyncAllToValuation(List<string> diagnostics)
        {
            diagnostics ??= new List<string>();
            if (valuation == null)
            {
                diagnostics.Add("BusinessLiabilityLedger: no valuation attached — sync skipped.");
                return;
            }
            var touched = new HashSet<string>(StringComparer.Ordinal);
            foreach (var liability in liabilities)
            {
                if (liability != null && !string.IsNullOrWhiteSpace(liability.BusinessInstanceId))
                {
                    if (financialAuthority != null && !string.IsNullOrWhiteSpace(liability.ObligationId))
                    {
                        FinancialObligation obligation = financialAuthority.Find(liability.ObligationId);
                        if (obligation != null) liability.BalanceCents = obligation.TotalOutstandingCents;
                    }
                    touched.Add(liability.BusinessInstanceId);
                }
            }
            foreach (var businessId in touched)
            {
                valuation.RecordLiabilities(businessId, TotalLiabilitiesFor(businessId));
            }
            diagnostics.Add($"BusinessLiabilityLedger: synced {touched.Count} businesses to valuation.");
        }

        /// <summary>Save support (CLN-1 pattern).</summary>
        public LiabilityLedgerSaveDto CaptureSaveDto()
        {
            return new LiabilityLedgerSaveDto { liabilities = new List<BusinessLiability>(liabilities) };
        }

        /// <summary>Save support (CLN-1 pattern). Re-posts valuation on load.</summary>
        public void LoadFromSaveDto(LiabilityLedgerSaveDto dto)
        {
            liabilities.Clear();
            if (dto == null || dto.liabilities == null) return;
            var touched = new HashSet<string>(StringComparer.Ordinal);
            foreach (var liability in dto.liabilities)
            {
                if (liability == null) continue;
                liabilities.Add(liability);
                if (financialAuthority != null && !string.IsNullOrWhiteSpace(liability.ObligationId))
                {
                    FinancialObligation obligation = financialAuthority.Find(liability.ObligationId);
                    if (obligation != null) liability.BalanceCents = obligation.TotalOutstandingCents;
                }
                if (!string.IsNullOrWhiteSpace(liability.BusinessInstanceId))
                {
                    touched.Add(liability.BusinessInstanceId);
                }
            }
            foreach (var businessId in touched) PostValuation(businessId);
        }
    }

    /// <summary>Save DTO for the liability ledger (CLN-1 pattern).</summary>
    [Serializable]
    public sealed class LiabilityLedgerSaveDto
    {
        public List<BusinessLiability> liabilities = new List<BusinessLiability>();
    }
}
