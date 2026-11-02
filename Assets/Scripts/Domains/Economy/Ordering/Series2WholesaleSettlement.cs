// Series 2 Pass 7 (selling): wholesale settlement and the back-to-shelf
// move for Pass 3 wholesale intake. Plain C#, no UnityEngine
// references, Roslyn-testable.
//
// Pass 3's intake deliberately moves NO cash: it returns a settlement
// record (store pays bakery, units x wholesale price, Settled = false)
// and leaves the cash leg to a caller. This file is that caller-side
// settlement, with the guards Pass 3 left open:
//
//   - Cash settlement: payer is debited and payee is credited through
//     caller-supplied IBusinessCashPort ports, which production maps
//     to BusinessRuntimeState — the business cash authority Pass 6
//     already used. CORRECTION TO THE PASS 7 BRIEF, stated plainly:
//     the brief said cash moves "ONLY through HouseholdLedger", but
//     HouseholdLedger records HOUSEHOLD cash only (one ledger per
//     household, inflows requiring a household income source). A
//     store-to-bakery payment is business-to-business cash; booking
//     it into a household ledger would invent a household party to a
//     business payment. Business cash therefore moves through the
//     business cash ports, household cash through HouseholdLedger
//     (in the shopping loop, which already does that), and debt only
//     through FinancialObligationAuthority. No cash moves anywhere
//     else in this file.
//   - Credit settlement: no cash moves at all. The caller supplies an
//     ITradeCreditPort (production: TradeCreditAuthorityPort over
//     FinancialObligationAuthority); settlement marks Settled only
//     when the authority returns a real obligation id. A failed
//     obligation leaves Settled false and moves nothing.
//   - Double settlement is refused: settling an already-Settled
//     record moves nothing, so re-running settlement can never pay
//     twice. Settled flips to true ONLY after the money (or the
//     obligation) is confirmed, never before.
//   - Insufficient payer cash refuses with nothing moved: the payer
//     balance is checked before any debit, and a payee credit failure
//     refunds the payer debit, so a failed settlement leaves both
//     balances exactly as they were.
//   - The delivered lot persists only as far as this pass goes: the
//     intake instance guards duplicates per instance, and this
//     settlement guards double payment per record. Persisting
//     delivered lot ids across saves is Pass 9 save adoption, NOT
//     claimed here — a reload that rebuilds the intake instance
//     without its delivered set could re-intake a lot, and Codex
//     must adopt that set before this path runs in production.
//
// Back-to-shelf move: wholesale lots land in BACK storage only
// (Pass 3 rule). This file's Series2WholesaleShelfMove performs the
// Pass 3 catalog move (back -> shelf, capacity-checked, refusing on
// insufficient back stock) and returns the Pass 2 task descriptor for
// that move: action id gs.move-back-to-shelf, 2 minutes per lot
// (Pass2TaskCatalogs). The catalog move is the stock change; the
// task descriptor is what Codex issues through TaskAuthority so a
// real worker spends the person-time. This pass does NOT claim a
// person performed the move — it claims the stock move is valid and
// the task it must be issued as is named. Retail price for the
// shelf sale is NOT set here: the Pass 3 line's price starts at the
// wholesale price, and the owner must set a retail price/margin via
// an instant decision (Pass 3 rule) before any shelf sale. Selling
// at wholesale price without that decision is a caller error this
// file diagnoses, not a default it silently accepts — see
// Series2WholesaleShelfMove.RequireRetailPriceSet.
using System;
using System.Collections.Generic;
using LandLedgers.Economy.Businesses.GeneralStore;
using LandLedgers.Economy.Labor;

namespace LandLedgers.Economy.Ordering
{
    /// <summary>Result of a wholesale settlement attempt: success plus the settled record, or a refusal with both balances untouched.</summary>
    public sealed class Series2WholesaleSettlementResult
    {
        public bool Settled;
        public string ObligationId = string.Empty;
        public string Refusal = string.Empty;
    }

    /// <summary>Series 2 Pass 7: cash and credit settlement for one Pass 3 wholesale intake record.</summary>
    public static class Series2WholesaleSettlement
    {
        /// <summary>
        /// Settles a wholesale record in cash: payer debited, payee credited,
        /// same amount, through business cash ports. Returns a refusal and
        /// moves nothing when the record is null, already settled, has a
        /// non-positive amount, or the payer cannot cover it.
        /// </summary>
        public static Series2WholesaleSettlementResult SettleCash(
            Pass3WholesaleSettlement settlement,
            IBusinessCashPort payer,
            IBusinessCashPort payee,
            List<string> diag)
        {
            var result = new Series2WholesaleSettlementResult();
            string refusal = Validate(settlement, payer, payee);
            if (refusal != null)
            {
                result.Refusal = refusal;
                if (diag != null) diag.Add("Series2WholesaleSettlement: " + refusal);
                return result;
            }

            int amount = settlement.AmountCents;
            if (payer.BalanceCents < amount)
            {
                result.Refusal = $"payer '{settlement.PayerBusinessId}' holds {payer.BalanceCents}c, cannot settle {amount}c — nothing moved.";
                if (diag != null) diag.Add("Series2WholesaleSettlement: " + result.Refusal);
                return result;
            }

            string debitProblem = payer.TryDebit(amount, $"wholesale settlement lot {settlement.BakeryLotId}");
            if (debitProblem != null)
            {
                result.Refusal = $"payer debit failed ({debitProblem}) — nothing moved.";
                if (diag != null) diag.Add("Series2WholesaleSettlement: " + result.Refusal);
                return result;
            }

            string creditProblem = payee.Credit(amount, $"wholesale settlement lot {settlement.BakeryLotId}");
            if (creditProblem != null)
            {
                // Refund the debit so a failed settlement leaves both
                // balances exactly as they were.
                payer.Credit(amount, $"wholesale settlement refund lot {settlement.BakeryLotId}");
                result.Refusal = $"payee credit failed ({creditProblem}) — payer refunded, nothing settled.";
                if (diag != null) diag.Add("Series2WholesaleSettlement: " + result.Refusal);
                return result;
            }

            settlement.Settled = true;
            result.Settled = true;
            if (diag != null) diag.Add($"Series2WholesaleSettlement: lot '{settlement.BakeryLotId}' settled in cash: {amount}c from '{settlement.PayerBusinessId}' to '{settlement.PayeeBusinessId}'.");
            return result;
        }

        /// <summary>
        /// Settles a wholesale record on trade credit: no cash moves. The
        /// obligation authority must return a real obligation id, or the
        /// record stays unsettled. Debtor is the payer business, creditor
        /// is the payee business — never a household party.
        /// </summary>
        public static Series2WholesaleSettlementResult SettleOnCredit(
            Pass3WholesaleSettlement settlement,
            ITradeCreditPort creditPort,
            int dayIndex,
            List<string> diag)
        {
            var result = new Series2WholesaleSettlementResult();
            if (settlement == null)
            {
                result.Refusal = "no settlement record to settle.";
                if (diag != null) diag.Add("Series2WholesaleSettlement: " + result.Refusal);
                return result;
            }
            if (settlement.Settled)
            {
                result.Refusal = $"lot '{settlement.BakeryLotId}' is already settled — double settlement refused.";
                if (diag != null) diag.Add("Series2WholesaleSettlement: " + result.Refusal);
                return result;
            }
            if (settlement.AmountCents <= 0)
            {
                result.Refusal = $"lot '{settlement.BakeryLotId}' has no positive amount to settle.";
                if (diag != null) diag.Add("Series2WholesaleSettlement: " + result.Refusal);
                return result;
            }
            if (creditPort == null)
            {
                result.Refusal = "no trade-credit port supplied — credit without the debt authority is informal debt and is refused.";
                if (diag != null) diag.Add("Series2WholesaleSettlement: " + result.Refusal);
                return result;
            }

            string obligationId = creditPort.CreateTradeCredit(
                settlement.PayerBusinessId, settlement.PayeeBusinessId,
                settlement.AmountCents, dayIndex,
                "wholesale trade credit, net 30",
                $"wholesale settlement lot {settlement.BakeryLotId}",
                $"wholesale-{settlement.BakeryLotId}",
                $"wholesale-{settlement.BakeryLotId}");
            if (string.IsNullOrWhiteSpace(obligationId))
            {
                result.Refusal = $"trade-credit obligation for lot '{settlement.BakeryLotId}' could not be created — record stays unsettled, no cash moved.";
                if (diag != null) diag.Add("Series2WholesaleSettlement: " + result.Refusal);
                return result;
            }

            settlement.Settled = true;
            result.Settled = true;
            result.ObligationId = obligationId;
            if (diag != null) diag.Add($"Series2WholesaleSettlement: lot '{settlement.BakeryLotId}' settled on trade credit {obligationId} ({settlement.AmountCents}c), no cash moved.");
            return result;
        }

        private static string Validate(Pass3WholesaleSettlement settlement, IBusinessCashPort payer, IBusinessCashPort payee)
        {
            if (settlement == null) return "no settlement record to settle.";
            if (settlement.Settled) return $"lot '{settlement.BakeryLotId}' is already settled — double settlement refused.";
            if (settlement.AmountCents <= 0) return $"lot '{settlement.BakeryLotId}' has no positive amount to settle.";
            if (payer == null || payee == null) return "settlement needs both a payer and a payee cash port.";
            return null;
        }
    }

    /// <summary>Result of a back-to-shelf move: the stock change plus the Pass 2 task it must be issued as.</summary>
    public sealed class Series2ShelfMoveResult
    {
        public bool Moved;
        public int UnitsMoved;
        public string TaskActionId = string.Empty;
        public int TaskMinutesPerLot;
        public string Refusal = string.Empty;
    }

    /// <summary>Series 2 Pass 7: wholesale back-to-shelf move through the Pass 3 catalog, named as a Pass 2 task.</summary>
    public static class Series2WholesaleShelfMove
    {
        /// <summary>
        /// Moves wholesale stock from back to shelf in the Pass 3 catalog.
        /// Refuses (moving nothing) on bad inputs, insufficient back stock,
        /// or a shelf-cap breach — the catalog's own refusals, not new ones.
        /// The returned task descriptor (gs.move-back-to-shelf, 2 min/lot)
        /// is what Codex issues through TaskAuthority; this method itself
        /// performs the stock move, not the person-time.
        /// </summary>
        public static Series2ShelfMoveResult MoveToShelf(
            Pass3StoreCatalog catalog, string productId, int units, List<string> diag)
        {
            var result = new Series2ShelfMoveResult
            {
                TaskActionId = Pass2TaskCatalogs.MoveBackToShelfId,
                TaskMinutesPerLot = 2,
            };
            if (catalog == null || string.IsNullOrWhiteSpace(productId) || units <= 0)
            {
                result.Refusal = "shelf move needs a catalog, a product id, and a positive quantity.";
                if (diag != null) diag.Add("Series2WholesaleShelfMove: " + result.Refusal);
                return result;
            }
            Pass3ProductLine line = catalog.Find(productId);
            if (line == null)
            {
                result.Refusal = $"product '{productId}' has no catalog line — nothing to move.";
                if (diag != null) diag.Add("Series2WholesaleShelfMove: " + result.Refusal);
                return result;
            }
            if (!line.MoveToShelf(units, diag))
            {
                result.Refusal = $"back-to-shelf move refused for '{productId}' (see diagnostics) — nothing moved.";
                return result;
            }
            result.Moved = true;
            result.UnitsMoved = units;
            if (diag != null) diag.Add($"Series2WholesaleShelfMove: moved {units}u '{productId}' back -> shelf (task {result.TaskActionId}, {result.TaskMinutesPerLot} min/lot to be issued through TaskAuthority).");
            return result;
        }

        /// <summary>
        /// Retail-price guard: a Pass 3 line starts at the wholesale price.
        /// Returns null when the owner has set a distinct retail price
        /// (price != cost, both positive), or a diagnosis otherwise.
        /// Selling at wholesale price without an explicit retail decision
        /// is a caller error, not a default this pass silently accepts.
        /// </summary>
        public static string RequireRetailPriceSet(Pass3ProductLine line)
        {
            if (line == null) return "no catalog line to price.";
            if (line.PriceCents <= 0) return $"product '{line.ProductId}' has no positive retail price.";
            if (line.PriceCents == line.CostCents) return $"product '{line.ProductId}' still prices at its wholesale cost ({line.CostCents}c) — the owner must set a retail price/margin before any shelf sale.";
            return null;
        }
    }
}
