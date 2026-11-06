using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using UnityEngine;

namespace LandLedgers.Economy.Core
{
    /// <summary>
    /// Plan 04 (2026-11-06) slice M1 (Obj 43, D-S1): the weekly
    /// input-procurement decision, extracted from
    /// SharedBusinessRuntimeManager.ProcureInputsForWeeklyOperation so
    /// the M1 assertions are executable in the Roslyn harness (SBRM is
    /// harness-excluded; this helper is the executable seam, and the
    /// converter tests run against it). SBRM delegates to
    /// ApplyWeeklyInputProcurement and applies nothing else.
    ///
    /// M1 RULE (Note-24): procurement without a named supplier refuses
    /// loudly and never conjures. Only units covered by a pending
    /// reorder reservation are received, and exactly the pending cost
    /// is spent (pending receipt is costed — the M0 trace: pending is
    /// set only by the weekly cadence refresh with no cash movement,
    /// and ProcureInputsForWeeklyOperation's spend line is the single
    /// payment). Any shortfall the pending could not cover keeps the
    /// missing-supplier blocked reason; this method NEVER removes it —
    /// a pending receipt proves a reorder reservation existed, not
    /// that a supplier exists. When cash (not pending) is the binding
    /// constraint, the cash-limited reason names it instead (or as
    /// well). M6 folds this helper into SharedBusinessWeeklyConverter
    /// or keeps it as its procurement delegate.
    ///
    /// Contract: desiredUnits / affordableUnits / unitCost are computed
    /// by the caller (SBRM still owns the landed-cost lookup and the
    /// reserve/budget math; ResolveDesiredUnits owns the covered-vs-
    /// formula selection, with the survival formula (shortfall +
    /// min(pending, planned input units)) transcribed into this helper
    /// — the old SBRM-side formula method was removed in the M5
    /// dead-code sweep). The caller passes both reason strings
    /// preformatted. blockedReasons is appended to, never pruned by
    /// this method.
    /// </summary>
    public static class SharedBusinessWeeklyProcurement
    {
        /// <summary>
        /// M2 (Plan 04, D-S5): sums the quantities of the registered
        /// BUSINESS input intents covering one buyer + category. Only
        /// BuyerKind "business" counts — a household intent for the same
        /// category string is a different buyer and must never be read
        /// as a business need. Returns false when no business intent
        /// covers the pair (the caller keeps the survival formula).
        /// Duplicate registrations sum (the M0 hazard): producers must
        /// clear + re-register weekly (M4), or the weekly pass will
        /// double-count demand.
        /// </summary>
        public static bool TryGetDemandUnits(
            List<PurchaseIntent> intents,
            string buyerBusinessInstanceId,
            string categoryId,
            out int units)
        {
            units = 0;
            if (intents == null
                || string.IsNullOrWhiteSpace(buyerBusinessInstanceId)
                || string.IsNullOrWhiteSpace(categoryId))
            {
                return false;
            }

            bool covered = false;
            for (int i = 0; i < intents.Count; i++)
            {
                PurchaseIntent intent = intents[i];
                if (intent == null
                    || !string.Equals(intent.BuyerKind, "business", StringComparison.Ordinal)
                    || !string.Equals(intent.BuyerId, buyerBusinessInstanceId, StringComparison.Ordinal)
                    || !string.Equals(intent.GoodsOrServiceId, categoryId, StringComparison.Ordinal))
                {
                    continue;
                }

                covered = true;
                units += Mathf.Max(0, intent.QuantityUnits);
            }

            return covered;
        }

        /// <summary>
        /// M2: the desired-units selection the weekly procurement path
        /// reads. Where a registered intent covers (buyer, category)
        /// the intent quantity REPLACES the survival-formula computation
        /// entirely; uncovered categories keep the existing computation
        /// (shortfall + min(pending, planned)) unchanged. A null intent
        /// list is uncovered.
        /// </summary>
        public static int ResolveDesiredUnits(
            int currentStockUnits,
            int pendingUnits,
            int plannedInputUnits,
            List<PurchaseIntent> intents,
            string buyerBusinessInstanceId,
            string categoryId)
        {
            if (TryGetDemandUnits(intents, buyerBusinessInstanceId, categoryId, out int demandUnits))
            {
                return demandUnits;
            }

            int shortfall = Mathf.Max(0, plannedInputUnits - currentStockUnits);
            int pendingBuffer = Mathf.Min(Mathf.Max(0, pendingUnits), Mathf.Max(0, plannedInputUnits));
            return shortfall + pendingBuffer;
        }

        public static int ApplyWeeklyInputProcurement(
            BusinessRuntimeState runtime,
            string categoryId,
            int desiredUnits,
            int affordableUnits,
            int unitCost,
            string missingSupplierReason,
            string cashLimitedReason,
            List<string> blockedReasons)
        {
            if (runtime == null
                || string.IsNullOrWhiteSpace(categoryId)
                || desiredUnits <= 0
                || affordableUnits <= 0
                || unitCost <= 0)
            {
                return 0;
            }

            CategoryStockState stock = runtime.GetCategoryStock(categoryId);
            if (stock == null)
            {
                return 0;
            }

            // STEP B (M1 fix): receive from pending ONLY. There is no
            // direct-conjure branch: units no pending reservation covers
            // are refused, not created. (Note-24.)
            int receivedUnits = stock.PendingReorderUnits > 0
                ? runtime.ReceivePendingReorderUnits(categoryId, Mathf.Min(affordableUnits, stock.PendingReorderUnits))
                : 0;
            int spend = receivedUnits * unitCost;
            if (spend > 0)
            {
                runtime.SpendCents(spend);
                runtime.RecordWeeklyInputProcurement(receivedUnits, spend);
            }

            // The missing-supplier reason is NEVER removed by this
            // method: a pending receipt proves a reorder reservation
            // existed, not that a supplier exists. A shortfall is
            // diagnosed honestly — when pending (not cash) is the
            // binding constraint the missing-supplier reason is kept;
            // when cash binds, the cash-limited reason names it
            // instead (or as well, when both bind).
            if (receivedUnits < desiredUnits)
            {
                if (receivedUnits < Mathf.Min(desiredUnits, affordableUnits))
                {
                    blockedReasons?.Add(missingSupplierReason);
                }

                if (affordableUnits < desiredUnits)
                {
                    blockedReasons?.Add(cashLimitedReason);
                }
            }

            return receivedUnits;
        }
    }
}
