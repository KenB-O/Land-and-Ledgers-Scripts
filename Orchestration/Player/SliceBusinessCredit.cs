using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy;
using LandLedgers.Economy.Financing;
using LandLedgers.Population;
using LandLedgers.Primitives;
using UnityEngine;

namespace LandLedgers.Orchestration.Player
{
    /// <summary>
    /// Pass XL phase 8 (Kennedy addition): credit as a business tool,
    /// with lots of options, not a single on/off. This is a POLICY and
    /// a sale bridge over the ONE existing debt authority,
    /// FinancialObligationAuthority (via the existing
    /// TradeCreditAuthorityPort). No new credit authority is created,
    /// no cash is created, and credit is never granted silently: every
    /// credit sale names its policy, its limits, and its terms, and
    /// every refusal names the limit it hit.
    ///
    /// THE OPTIONS (all caller-supplied; the defaults below are
    /// bridge defaults for tests, NOT canon policy, and the default
    /// credit policy for the slice is a Kennedy decision, flagged in
    /// the phase 8 report)
    ///   - OfferCredit: whether this business gives credit at all.
    ///     False is a valid, tested path: the phase 1 cash-only sale
    ///     still works unchanged, and a credit request is refused.
    ///   - PerSaleLimitCents: the largest single credit sale.
    ///   - PerCustomerOutstandingLimitCents: the most one household
    ///     may owe this business at once, across all its credit sales.
    ///   - TotalOutstandingLimitCents: the most the business may have
    ///     out on credit in total, across all customers.
    ///   - NetDays: payment terms for each credit sale (net N, due on
    ///     sale day + N). Registered in the existing TradeCreditBook,
    ///     which ages the invoice.
    ///   - BlockNewCreditWhenPastDue: when true, a customer with any
    ///     invoice at ReminderIssued or beyond gets no new credit
    ///     until it pays. When false, new credit is judged on limits
    ///     alone (the aging stages still advance either way).
    ///
    /// NON-PAYMENT (what actually exists, no invention)
    /// The existing TradeCreditBook.EvaluateDay advances an unpaid
    /// invoice through Current, ReminderIssued (1 day past due),
    /// FormalDemand (31), InCollection (61), Defaulted (121), and
    /// moves the obligation's own status through MarkDue,
    /// MarkDelinquent, and MarkDefaulted. That IS the non-payment
    /// handling: there is no forgiveness, write-off, or void API on
    /// FinancialObligationAuthority, so a defaulted debt stays
    /// outstanding on the books until it is paid. Whether the slice
    /// wants a write-off/forgiveness rule is a Kennedy decision; this
    /// bridge does not invent one by deleting obligations.
    ///
    /// WHERE CONFIGURATION LIVES (Codex/production task)
    /// This policy is a plain C# object handed to the bridge per
    /// sale. In production it belongs on the business, not in code:
    /// a per-business credit policy record (a ScriptableObject or a
    /// business-definition field, alongside the General Store
    /// business definition), set by the owner in the Main Scene or
    /// business UI, read by the sales driver before it offers
    /// credit, and persisted with the business. No such asset or
    /// field exists yet; that is the missing configuration link,
    /// and until it exists the policy must be supplied by the caller
    /// (tests do) and must never be defaulted silently to "credit on".
    ///
    /// WHAT IS NOT BUILT HERE
    /// Credit sales for bakery bread and dealer fuel are not bridged
    /// in this pass: the policy class is reusable for them, but their
    /// cash bridges (ThreeBusinessSlice) have no credit leg yet, and
    /// a credit leg for them needs the same consume-then-obligate-
    /// then-deliver ordering proved here for store flour. A household
    /// credit path for the store's OTHER goods does not exist beyond
    /// this bridge: phase 1 household sales remain cash-only unless
    /// the caller explicitly routes through SellOnCredit with a
    /// policy that offers credit.
    ///
    /// ORDERING (why consume, then obligate, then deliver)
    /// The lot consumption must happen before the obligation, or a
    /// refused consumption would leave a debt for goods never taken;
    /// the obligation must happen before delivery, or delivered goods
    /// would have no debt behind them. Delivery failure after both
    /// is near-impossible (the SKU is pre-validated against
    /// HouseholdItemCatalog, units are positive, the source label is
    /// non-blank), but if it happens the consumption is undone and
    /// the diagnostic says the obligation needs manual reconciliation,
    /// because no obligation-void API exists to erase it cleanly.
    /// </summary>
    public sealed class BusinessCreditPolicy
    {
        public bool OfferCredit;
        public int PerSaleLimitCents;
        public int PerCustomerOutstandingLimitCents;
        public int TotalOutstandingLimitCents;
        public int NetDays = 30;
        public bool BlockNewCreditWhenPastDue = true;

        public static BusinessCreditPolicy CashOnly()
        {
            return new BusinessCreditPolicy
            {
                OfferCredit = false,
                PerSaleLimitCents = 0,
                PerCustomerOutstandingLimitCents = 0,
                TotalOutstandingLimitCents = 0,
                NetDays = 30,
                BlockNewCreditWhenPastDue = true,
            };
        }
    }

    public sealed class SliceCreditSaleResult
    {
        public bool Success;
        public string ObligationId = string.Empty;
        public string SaleId = string.Empty;
        public int TotalCents;
        public string Diagnostic = string.Empty;
    }

    public static class SliceBusinessCredit
    {
        private static string DebtorFor(int householdId)
        {
            return "household:" + householdId;
        }

        /// <summary>
        /// Sells store-lot goods to a household on credit. Business
        /// cash does NOT rise on a credit sale (no cash has arrived);
        /// it rises only when the household repays (see RepayCredit).
        /// The household inventory gains the goods immediately, and
        /// the household gains a real obligation for the full price.
        /// </summary>
        public static SliceCreditSaleResult SellOnCredit(
            OffMapNamedSkuProcurementService service,
            BusinessCreditPolicy policy,
            FinancialObligationAuthority authority,
            EntityIdRegistry ids,
            TradeCreditBook book,
            HouseholdLedger householdLedger, HouseholdInventory inventory,
            int householdId, int actingPersonId, string buyerPrincipal,
            string skuId, int units, int unitSalePriceCents, int dayIndex,
            List<string> diag)
        {
            var result = new SliceCreditSaleResult();
            diag = diag ?? new List<string>();
            if (service == null || policy == null || authority == null || ids == null
                || householdLedger == null || inventory == null
                || householdLedger.HouseholdId != householdId || inventory.HouseholdId != householdId
                || string.IsNullOrWhiteSpace(buyerPrincipal) || string.IsNullOrWhiteSpace(skuId)
                || units <= 0 || unitSalePriceCents < 0)
            {
                result.Diagnostic = "SellOnCredit: service, policy, debt authority, id registry, matching household ledger and inventory, buyer, SKU, positive units, and a non-negative price are required.";
                return result;
            }

            if (!policy.OfferCredit)
            {
                result.Diagnostic = $"SellOnCredit refused: business '{service.BusinessId}' does not offer credit (policy OfferCredit false); the cash-only sale path is the valid route for this business.";
                return result;
            }

            if (HouseholdItemCatalog.Get(skuId) == null)
            {
                result.Diagnostic = $"SellOnCredit refused: '{skuId}' is not a household inventory item; delivery could not land, so no debt was created.";
                return result;
            }

            int totalPrice = units * unitSalePriceCents;
            if (totalPrice <= 0)
            {
                result.Diagnostic = "SellOnCredit refused: a credit sale needs a positive total; a zero-price sale creates no obligation.";
                return result;
            }

            if (totalPrice > policy.PerSaleLimitCents)
            {
                result.Diagnostic = $"SellOnCredit refused: sale total {totalPrice}c exceeds the per-sale credit limit {policy.PerSaleLimitCents}c.";
                return result;
            }

            string debtor = DebtorFor(householdId);
            int customerOutstanding = authority.TotalOutstandingFor(debtor);
            if (customerOutstanding + totalPrice > policy.PerCustomerOutstandingLimitCents)
            {
                result.Diagnostic = $"SellOnCredit refused: household H{householdId} already owes {customerOutstanding}c; this sale would exceed its per-customer credit limit {policy.PerCustomerOutstandingLimitCents}c.";
                return result;
            }

            int totalOutstanding = authority.TotalReceivableFor(service.BusinessId);
            if (totalOutstanding + totalPrice > policy.TotalOutstandingLimitCents)
            {
                result.Diagnostic = $"SellOnCredit refused: business '{service.BusinessId}' already has {totalOutstanding}c out on credit; this sale would exceed its total credit limit {policy.TotalOutstandingLimitCents}c.";
                return result;
            }

            if (policy.BlockNewCreditWhenPastDue && book != null)
            {
                foreach (var terms in book.Invoices)
                {
                    if (terms == null || !string.Equals(terms.Debtor, debtor, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    FinancialObligation obligation = authority.Find(terms.ObligationId);
                    if (obligation != null && !obligation.Settled
                        && terms.Stage >= TradeCreditCollectionStage.ReminderIssued)
                    {
                        result.Diagnostic = $"SellOnCredit refused: household H{householdId} has a past-due credit invoice '{terms.ObligationId}' at stage {terms.Stage}; no new credit until it pays.";
                        return result;
                    }
                }
            }

            NamedSkuLotConsumptionRecord consumption =
                service.ConsumeLots(skuId, units, "credit-sale:household:" + householdId, dayIndex);
            if (consumption == null)
            {
                result.Diagnostic = "SellOnCredit refused: store lot consumption refused (shortfall); no debt was created.";
                return result;
            }

            var port = new TradeCreditAuthorityPort(authority, ids);
            string saleId = $"sku-credit-sale-{service.BusinessId}-{Math.Max(0, dayIndex)}-{consumption.ConsumptionId}";
            string obligationId = port.CreateTradeCredit(
                debtor, service.BusinessId, totalPrice, dayIndex,
                $"net {Math.Max(1, policy.NetDays)}",
                $"credit sale {units}u {skuId} to H{householdId}",
                saleId, consumption.ConsumptionId);
            if (string.IsNullOrWhiteSpace(obligationId))
            {
                service.UndoConsumption(consumption);
                result.Diagnostic = "SellOnCredit refused: the debt authority would not create the obligation; consumption undone, nothing sold.";
                return result;
            }

            if (book != null)
            {
                string termsProblem = book.RegisterTerms(authority, obligationId, Math.Max(1, policy.NetDays), dayIndex, diag);
                if (termsProblem != null)
                {
                    diag.Add($"SellOnCredit note: aging terms registration for obligation '{obligationId}' reported '{termsProblem}'; the obligation itself stands and is the debt record.");
                }
            }

            string addProblem = inventory.AddLot(
                skuId, units, dayIndex,
                $"credit sale {saleId}: {units}u {skuId} from {service.BusinessId} on credit, obligation {obligationId}, consumption {consumption.ConsumptionId}",
                qualityBand: string.Empty, sourceKind: "credit-sale", ownerPersonId: actingPersonId);
            if (addProblem != null)
            {
                service.UndoConsumption(consumption);
                result.Diagnostic = $"SellOnCredit failed: household inventory refused the lot ({addProblem}); consumption undone. " +
                    $"WARNING: obligation '{obligationId}' was already created and has no void API — it needs manual reconciliation, never silent deletion.";
                return result;
            }

            result.Success = true;
            result.ObligationId = obligationId;
            result.SaleId = saleId;
            result.TotalCents = totalPrice;
            result.Diagnostic = $"SellOnCredit: H{householdId} bought {units}u {skuId} on credit for {totalPrice}c, obligation {obligationId}, net {Math.Max(1, policy.NetDays)} (due day {dayIndex + Math.Max(1, policy.NetDays)}). Business cash unchanged until repayment.";
            diag.Add(result.Diagnostic);
            return result;
        }

        /// <summary>
        /// Repays a credit obligation in cash: business cash rises,
        /// household cash falls, and the obligation's outstanding
        /// falls by the same amount, through ApplyPayment on the one
        /// debt authority. A failed cash leg reverses the other cash
        /// leg before ApplyPayment runs, so debt is never erased
        /// without payment and payment never lands without debt relief.
        /// </summary>
        public static string RepayCredit(
            FinancialObligationAuthority authority,
            BusinessRuntimeState businessRuntime,
            HouseholdLedger householdLedger,
            string obligationId, int amountCents, int dayIndex,
            List<string> diag)
        {
            diag = diag ?? new List<string>();
            if (authority == null || businessRuntime == null || householdLedger == null
                || string.IsNullOrWhiteSpace(obligationId) || amountCents <= 0)
            {
                return "RepayCredit: debt authority, business runtime, household ledger, obligation id, and a positive amount are required.";
            }

            FinancialObligation obligation = authority.Find(obligationId);
            if (obligation == null || obligation.Settled)
            {
                return $"RepayCredit refused: obligation '{obligationId}' is unknown or already settled.";
            }

            if (amountCents > obligation.TotalOutstandingCents)
            {
                return $"RepayCredit refused: repayment {amountCents}c exceeds outstanding {obligation.TotalOutstandingCents}c on '{obligationId}'.";
            }

            if (amountCents > householdLedger.GetBalanceCents())
            {
                return $"RepayCredit refused: household H{householdLedger.HouseholdId} balance {householdLedger.GetBalanceCents()}c cannot pay {amountCents}c.";
            }

            businessRuntime.AddCashCents(amountCents);
            string outflowProblem = householdLedger.RecordOutflow(
                dayIndex, amountCents, $"credit repayment on obligation {obligationId}", obligation.Creditor);
            if (outflowProblem != null)
            {
                businessRuntime.TrySpendCents(amountCents);
                return "RepayCredit refused: household debit failed (" + outflowProblem + "); business cash restored, obligation untouched.";
            }

            FinancialPaymentRecord payment = authority.ApplyPayment(
                obligationId, amountCents, dayIndex,
                "household:" + householdLedger.HouseholdId, obligation.Creditor, obligationId);
            if (payment == null)
            {
                businessRuntime.TrySpendCents(amountCents);
                householdLedger.RecordInflow(dayIndex, amountCents, HouseholdIncomeSource.SaleProceeds,
                    obligationId, $"refund: reversed credit repayment on {obligationId}", obligation.Creditor);
                return $"RepayCredit failed: the debt authority would not apply the payment to '{obligationId}'; cash legs reversed.";
            }

            diag.Add($"RepayCredit: {amountCents}c paid on obligation '{obligationId}'; outstanding now {obligation.TotalOutstandingCents}c.");
            return null;
        }

        /// <summary>
        /// Ages every registered credit invoice to a day through the
        /// existing TradeCreditBook (reminder, demand, collection,
        /// default). This is the non-payment escalation; it never
        /// erases a debt.
        /// </summary>
        public static void AgeCredit(TradeCreditBook book, FinancialObligationAuthority authority, int dayIndex, List<string> diag)
        {
            if (book == null || authority == null)
            {
                return;
            }

            book.EvaluateDay(authority, dayIndex, null, diag ?? new List<string>());
        }
    }
}
