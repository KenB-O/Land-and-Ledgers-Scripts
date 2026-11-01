using System;
using System.Collections.Generic;
using LandLedgers.Economy.Financing;
using LandLedgers.Primitives;

namespace LandLedgers.Economy.Labor
{
    /// <summary>
    /// G-06: NPC-owner auto-resolution for manager pending requests.
    /// Escalated Borrow and always-escalated SellAsset requests had no
    /// resolver and grew unboundedly on the pending-request ledger.
    ///
    /// NPC policy (this class):
    /// - Borrow: routed to BusinessLenderService underwriting instead of
    ///   stalling. A funded loan approves the request and credits the
    ///   business; a lender refusal rejects it with the lender's reason.
    /// - SellAsset: approved only when business cash is below the survival
    ///   reserve AND the proposed price is at/above appraised value;
    ///   denied otherwise with a reason. Approval is the owner's consent —
    ///   the sale's execution (asset transfer + cash) is a separate step.
    ///
    /// Player-owned businesses keep their requests Pending (future UI,
    /// explicitly out of scope). Unknown request kinds stay pending.
    /// Resolution is recorded on the request (Status / ResolutionNote /
    /// ResolvedDayIndex) — never silently dropped, never duplicated.
    /// </summary>
    public sealed class NpcOwnerRequestResolver
    {
        /// <summary>All per-call inputs. Lookups may be null (treated as absent).</summary>
        public sealed class OwnerResolutionContext
        {
            public ManagerDecisionExecutionLedger Ledger;
            public Func<string, BusinessInstanceState> FindBusiness;
            /// <summary>Survival reserve per business; null falls back to 0 (never approve blind).</summary>
            public Func<BusinessInstanceState, int> SurvivalReserveCents;
            /// <summary>
            /// Appraised value for a SellAsset request in cents; negative =
            /// unknown (the owner does not approve blind sales).
            /// </summary>
            public Func<ManagerPendingRequest, int> AppraiseSaleCents;
            public IManagerBusinessLender BusinessLender;
            public FinancialObligationAuthority Obligations;
            public EntityIdRegistry Ids;
            public int DayIndex;
        }

        private readonly List<string> diagnostics = new List<string>();

        public IReadOnlyList<string> Diagnostics => diagnostics;

        /// <summary>Resolves every pending NPC-owner request on the ledger. Idempotent.</summary>
        public void ResolvePending(OwnerResolutionContext ctx, List<string> diag)
        {
            diag = diag ?? diagnostics;
            if (ctx == null || ctx.Ledger == null)
            {
                diag.Add("OwnerResolution: no context or ledger — nothing resolved.");
                return;
            }

            // Snapshot: resolution mutates request status, never the list.
            var pending = new List<ManagerPendingRequest>(ctx.Ledger.PendingRequests);
            foreach (ManagerPendingRequest request in pending)
            {
                if (request == null
                    || !string.Equals(request.Status, "Pending", StringComparison.Ordinal))
                    continue;

                BusinessInstanceState business = ctx.FindBusiness?.Invoke(request.BusinessInstanceId);
                if (business == null)
                {
                    Reject(ctx, request, $"unknown business '{request.BusinessInstanceId}'", diag);
                    continue;
                }

                if (business.Owner != null && business.Owner.OwnerKind == BusinessOwnerKind.Player)
                    continue; // player-owned: future UI, explicitly out of scope

                if (string.Equals(request.Kind, ManagerActKinds.Borrow, StringComparison.Ordinal))
                    ResolveBorrow(ctx, request, business, diag);
                else if (string.Equals(request.Kind, ManagerActKinds.SellAsset, StringComparison.Ordinal))
                    ResolveSellAsset(ctx, request, business, diag);
                // Unknown kinds stay pending — the resolver only speaks NPC policy.
            }
        }

        private static void ResolveBorrow(
            OwnerResolutionContext ctx, ManagerPendingRequest request,
            BusinessInstanceState business, List<string> diag)
        {
            int amount = Math.Max(0, request.AmountCents);
            if (amount <= 0)
            {
                Reject(ctx, request, "no borrowing amount on the request", diag);
                return;
            }

            if (ctx.BusinessLender == null || ctx.Obligations == null || ctx.Ids == null)
            {
                Reject(ctx, request, "no business lender wired — underwriting unavailable", diag);
                return;
            }

            if (business.RuntimeState == null)
            {
                Reject(ctx, request, "the business has no cash ledger", diag);
                return;
            }

            // NPC-owner policy: underwriting decides, not stalling. Mirrors the
            // non-escalated Borrow executor (TryArrangeLoan + cash credit).
            FinancialObligation obligation = ctx.BusinessLender.TryArrangeLoan(
                ctx.Ids, ctx.Obligations, business.InstanceId, amount,
                ctx.DayIndex, diag, out string refusal);
            if (obligation == null)
            {
                Reject(ctx, request, $"lender underwriting refused: {refusal}", diag);
                return;
            }

            business.RuntimeState.AdjustCashCents(amount, recordNetDelta: true);
            Approve(ctx, request,
                $"NPC owner routed to {ctx.BusinessLender.LenderName} underwriting: " +
                $"funded {amount}c (obligation {obligation.ObligationId})", diag);
        }

        private static void ResolveSellAsset(
            OwnerResolutionContext ctx, ManagerPendingRequest request,
            BusinessInstanceState business, List<string> diag)
        {
            int price = Math.Max(0, request.AmountCents);
            int cash = business.RuntimeState != null ? business.RuntimeState.CurrentCashCents : 0;
            int reserve = Math.Max(0, ctx.SurvivalReserveCents != null
                ? ctx.SurvivalReserveCents(business) : 0);

            if (cash >= reserve)
            {
                Reject(ctx, request,
                    $"business cash {cash}c is at/above the {reserve}c survival reserve — no distress sale", diag);
                return;
            }

            if (price <= 0)
            {
                Reject(ctx, request, "no price proposed — the owner cannot approve a priceless sale", diag);
                return;
            }

            int appraised = ctx.AppraiseSaleCents != null ? ctx.AppraiseSaleCents(request) : -1;
            if (appraised < 0)
            {
                Reject(ctx, request, "no appraisal available — the owner does not approve blind asset sales", diag);
                return;
            }

            if (price >= appraised)
            {
                Approve(ctx, request,
                    $"distress sale approved at {price}c (at/above appraised {appraised}c; " +
                    $"cash {cash}c < {reserve}c reserve) — sale execution is a separate step", diag);
            }
            else
            {
                Reject(ctx, request, $"proposed price {price}c is below appraised value {appraised}c", diag);
            }
        }

        private static void Approve(
            OwnerResolutionContext ctx, ManagerPendingRequest request, string note, List<string> diag)
        {
            request.Status = "Approved";
            request.ResolutionNote = note;
            request.ResolvedDayIndex = ctx.DayIndex;
            diag.Add($"OwnerResolution: {request.RequestId} APPROVED — {note}");
        }

        private static void Reject(
            OwnerResolutionContext ctx, ManagerPendingRequest request, string reason, List<string> diag)
        {
            request.Status = "Rejected";
            request.ResolutionNote = reason;
            request.ResolvedDayIndex = ctx.DayIndex;
            diag.Add($"OwnerResolution: {request.RequestId} REJECTED — {reason}");
        }
    }
}
