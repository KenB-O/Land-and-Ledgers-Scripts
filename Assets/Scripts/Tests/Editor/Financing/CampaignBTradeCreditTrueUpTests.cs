using System;
using System.Collections.Generic;
using LandLedgers.Economy.Financing;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Financing
{
    /// <summary>
    /// CAMPAIGN-B package 3 (DEF-043): ReduceOutstandingPrincipal — the
    /// trade-credit partial-fill true-up. The goods obligation is created
    /// before the sale for the planned units; when the store sells fewer,
    /// the outstanding principal is reduced by the undelivered value so
    /// the household owes only for the goods delivered. The created
    /// principal stays for audit (the CancelObligation convention); the
    /// reduction is recorded in PrincipalReducedCents /
    /// PrincipalReductionReason, never as a payment. Reconcile() counts
    /// reductions in its conservation law (paid + outstanding + reduced
    /// == original) and exempts Cancelled obligations (documented
    /// write-off semantics — otherwise every P28 credit void would
    /// report a conservation mismatch).
    /// </summary>
    [TestFixture]
    public sealed class CampaignBTradeCreditTrueUpTests
    {
        private static FinancialObligation CreateTradeCredit(
            FinancialObligationAuthority authority, EntityIdRegistry ids, int principalCents)
        {
            FinancialObligation obligation = authority.Create(
                ids, FinancialObligationKind.TradeCredit, "household:7", "store-1", principalCents, 0,
                "trade credit, net 30", "test obligation");
            Assert.IsNotNull(obligation);
            return obligation;
        }

        [Test]
        public void ReduceHappyPathTruesOutstandingToDelivered()
        {
            var authority = new FinancialObligationAuthority();
            var ids = new EntityIdRegistry();
            FinancialObligation obligation = CreateTradeCredit(authority, ids, 7200);

            Assert.IsNull(authority.ReduceOutstandingPrincipal(
                obligation.ObligationId, 600, "partial fill: 55u of 60u delivered"));
            Assert.AreEqual(6600, obligation.OutstandingPrincipalCents, "The household owes only the delivered value.");
            Assert.AreEqual(7200, obligation.OriginalPrincipalCents, "The created principal stays for audit.");
            Assert.AreEqual(6600, obligation.TotalOutstandingCents);
            Assert.AreEqual(600, obligation.PrincipalReducedCents, "The reduction is recorded, never a payment.");
            Assert.AreEqual(0, obligation.Payments.Count, "A reduction is not a payment.");
            Assert.AreEqual("partial fill: 55u of 60u delivered", obligation.PrincipalReductionReason);
            Assert.AreEqual(FinancialObligationStatus.Active, obligation.Status, "A partial reduction leaves the obligation active.");
            Assert.AreEqual(0, authority.Reconcile().Count, "The conservation law holds after the true-up.");
        }

        [Test]
        public void ReduceUnknownIdRefuses()
        {
            var authority = new FinancialObligationAuthority();
            string refusal = authority.ReduceOutstandingPrincipal("no-such-id", 600, "test");
            Assert.IsNotNull(refusal);
            Assert.IsTrue(refusal.IndexOf("Unknown financial obligation", StringComparison.Ordinal) >= 0);
        }

        [Test]
        public void ReduceNonPositiveRefuses()
        {
            var authority = new FinancialObligationAuthority();
            var ids = new EntityIdRegistry();
            FinancialObligation obligation = CreateTradeCredit(authority, ids, 7200);
            Assert.IsNotNull(authority.ReduceOutstandingPrincipal(obligation.ObligationId, 0, "test"));
            Assert.IsNotNull(authority.ReduceOutstandingPrincipal(obligation.ObligationId, -100, "test"));
            Assert.AreEqual(7200, obligation.OutstandingPrincipalCents, "A refused reduction moves nothing.");
            Assert.AreEqual(0, obligation.PrincipalReducedCents);
        }

        [Test]
        public void ReduceExceedsOutstandingRefuses()
        {
            var authority = new FinancialObligationAuthority();
            var ids = new EntityIdRegistry();
            FinancialObligation obligation = CreateTradeCredit(authority, ids, 7200);
            string refusal = authority.ReduceOutstandingPrincipal(obligation.ObligationId, 7201, "test");
            Assert.IsNotNull(refusal);
            Assert.IsTrue(refusal.IndexOf("exceeds the outstanding principal", StringComparison.Ordinal) >= 0);
            Assert.AreEqual(7200, obligation.OutstandingPrincipalCents, "A refused reduction moves nothing.");
            Assert.AreEqual(0, obligation.PrincipalReducedCents);
        }

        [Test]
        public void ReduceToZeroSettles()
        {
            var authority = new FinancialObligationAuthority();
            var ids = new EntityIdRegistry();
            FinancialObligation obligation = CreateTradeCredit(authority, ids, 7200);
            Assert.IsNull(authority.ReduceOutstandingPrincipal(obligation.ObligationId, 7200, "nothing delivered"));
            Assert.AreEqual(0, obligation.OutstandingPrincipalCents);
            Assert.AreEqual(0, obligation.TotalOutstandingCents);
            Assert.IsTrue(obligation.Settled, "Settled holds when the outstanding total reaches zero.");
            Assert.AreEqual(7200, obligation.PrincipalReducedCents);
            Assert.AreEqual(0, authority.Reconcile().Count);
        }

        [Test]
        public void ReduceSatisfiedRefuses()
        {
            var authority = new FinancialObligationAuthority();
            var ids = new EntityIdRegistry();
            FinancialObligation obligation = CreateTradeCredit(authority, ids, 7200);
            Assert.IsNotNull(authority.ApplyPayment(obligation.ObligationId, 7200, 0, "household:7", "store-1"));
            Assert.AreEqual(FinancialObligationStatus.Satisfied, obligation.Status);
            string refusal = authority.ReduceOutstandingPrincipal(obligation.ObligationId, 600, "test");
            Assert.IsNotNull(refusal, "A paid obligation is a settled fact and is never reduced away.");
            Assert.AreEqual(0, obligation.OutstandingPrincipalCents, "The paid obligation stays paid.");
            Assert.AreEqual(0, obligation.PrincipalReducedCents, "A refused reduction records nothing.");
        }

        [Test]
        public void ReduceCancelledRefuses()
        {
            var authority = new FinancialObligationAuthority();
            var ids = new EntityIdRegistry();
            FinancialObligation obligation = CreateTradeCredit(authority, ids, 7200);
            Assert.IsNull(authority.CancelObligation(obligation.ObligationId, "sale voided"));
            string refusal = authority.ReduceOutstandingPrincipal(obligation.ObligationId, 600, "test");
            Assert.IsNotNull(refusal, "A cancelled obligation is a settled fact and is never reduced.");
            Assert.AreEqual(0, obligation.PrincipalReducedCents);
        }

        [Test]
        public void ReduceReasonFirstWins()
        {
            var authority = new FinancialObligationAuthority();
            var ids = new EntityIdRegistry();
            FinancialObligation obligation = CreateTradeCredit(authority, ids, 7200);
            Assert.IsNull(authority.ReduceOutstandingPrincipal(obligation.ObligationId, 600, "first reason"));
            Assert.IsNull(authority.ReduceOutstandingPrincipal(obligation.ObligationId, 100, "second reason"));
            Assert.AreEqual(6500, obligation.OutstandingPrincipalCents, "Reductions accumulate.");
            Assert.AreEqual(700, obligation.PrincipalReducedCents);
            Assert.AreEqual("first reason", obligation.PrincipalReductionReason, "A second reduction keeps the first reason.");
            Assert.AreEqual(0, authority.Reconcile().Count);
        }

        [Test]
        public void ReduceAfterPartialPaymentKeepsConservation()
        {
            var authority = new FinancialObligationAuthority();
            var ids = new EntityIdRegistry();
            FinancialObligation obligation = CreateTradeCredit(authority, ids, 7200);
            Assert.IsNotNull(authority.ApplyPayment(obligation.ObligationId, 1000, 0, "household:7", "store-1"));
            Assert.AreEqual(6200, obligation.OutstandingPrincipalCents);
            Assert.IsNull(authority.ReduceOutstandingPrincipal(obligation.ObligationId, 600, "partial fill"));
            Assert.AreEqual(5600, obligation.OutstandingPrincipalCents);
            Assert.AreEqual(600, obligation.PrincipalReducedCents);
            Assert.AreEqual(0, authority.Reconcile().Count, "paid 1000 + outstanding 5600 + reduced 600 == original 7200.");
        }

        [Test]
        public void ReduceThenCancelReconcileEmpty()
        {
            var authority = new FinancialObligationAuthority();
            var ids = new EntityIdRegistry();
            FinancialObligation obligation = CreateTradeCredit(authority, ids, 7200);
            Assert.IsNull(authority.ReduceOutstandingPrincipal(obligation.ObligationId, 600, "partial fill"));
            Assert.IsNull(authority.CancelObligation(obligation.ObligationId, "test write-off"));
            Assert.AreEqual(0, obligation.OutstandingPrincipalCents);
            Assert.AreEqual(7200, obligation.OriginalPrincipalCents);
            Assert.AreEqual(0, authority.Reconcile().Count, "A written-off remainder is not a conservation mismatch.");
        }

        [Test]
        public void CancelAloneReconcileEmpty()
        {
            // Pins the Cancelled exemption: the P28 credit-void path cancels
            // obligations whose created principal must stay for audit, and
            // that alone must not report a conservation mismatch.
            var authority = new FinancialObligationAuthority();
            var ids = new EntityIdRegistry();
            FinancialObligation obligation = CreateTradeCredit(authority, ids, 7200);
            Assert.IsNull(authority.CancelObligation(obligation.ObligationId, "sale voided"));
            Assert.AreEqual(0, authority.Reconcile().Count);
        }

        [Test]
        public void ClonePreservesReduction()
        {
            // Copy-on-load must carry the reduction record, or a save/load
            // would resurrect the mismatch Reconcile() is meant to catch.
            var authority = new FinancialObligationAuthority();
            var ids = new EntityIdRegistry();
            FinancialObligation obligation = CreateTradeCredit(authority, ids, 7200);
            Assert.IsNull(authority.ReduceOutstandingPrincipal(obligation.ObligationId, 600, "partial fill"));
            FinancialObligationSaveDto dto = authority.CaptureSaveDto();

            var restored = new FinancialObligationAuthority();
            restored.LoadFromSaveDto(dto);
            FinancialObligation copy = restored.Find(obligation.ObligationId);
            Assert.IsNotNull(copy);
            Assert.AreEqual(6600, copy.OutstandingPrincipalCents);
            Assert.AreEqual(7200, copy.OriginalPrincipalCents);
            Assert.AreEqual(600, copy.PrincipalReducedCents);
            Assert.AreEqual("partial fill", copy.PrincipalReductionReason);
            Assert.AreEqual(0, restored.Reconcile().Count);
        }
    }
}
