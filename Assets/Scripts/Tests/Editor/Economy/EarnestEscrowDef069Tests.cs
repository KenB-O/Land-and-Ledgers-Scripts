// DEF-069 (Decision 12): the earnest escrow ledger.
//
// The forfeited portion of a failed deal was credited to nobody, a deal
// failing before earnest was committed could refund money that was never
// paid, and business earnest was impossible because no escrow record
// existed. These tests pin the ledger contract directly: one open escrow
// per listing, every settlement conserves the deposit (forfeited +
// refunded == committed, or seller + buyer == committed on a dispute
// resolution), no double settlement, no settlement past a dispute, and a
// save/load round trip that preserves records, states, and the id
// sequence. The manager wiring (CommitEarnest records, ApplyDealFailure
// settles from the escrow, CloseAcquisition applies to price) is
// source-reviewed only — the manager needs TestWorld/AssetDatabase,
// and AcquisitionVpFlowTests.cs is harness-excluded; Unity verification
// of the wiring is owed from Codex.
using System.Collections.Generic;
using LandLedgers.Economy.Financing;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Economy
{
    [TestFixture]
    public sealed class EarnestEscrowDef069Tests
    {
        private const string Terms =
            "financing contingency present; option through day 14; no closing deadline; " +
            "forfeit per FinancingFailureEvaluator: 0% before tentative agreement, " +
            "25% on a protected failure, 100% on an unprotected failure.";

        private static EarnestEscrowLedger Ledger()
        {
            return new EarnestEscrowLedger();
        }

        private static EarnestEscrowRecord Commit(
            EarnestEscrowLedger ledger, string listingId, int amountCents, int dayIndex = 4)
        {
            return ledger.RecordCommit(
                listingId, "Plot 001 parcel", "Player", "Grace Boniecki", "Land",
                amountCents, dayIndex, Terms, new List<string>());
        }

        [Test]
        public void T01_Commit_RecordsHeldEscrowWithStakeholdersAndTerms()
        {
            EarnestEscrowLedger ledger = Ledger();

            EarnestEscrowRecord record = Commit(ledger, "land_1", 5000);

            Assert.NotNull(record);
            Assert.AreEqual("escrow-1", record.EscrowId);
            Assert.AreEqual(EarnestEscrowState.Held, record.State);
            Assert.AreEqual("land_1", record.ListingId);
            Assert.AreEqual("Player", record.BuyerLabel);
            Assert.AreEqual("Grace Boniecki", record.SellerLabel);
            Assert.AreEqual("Land", record.KindLabel);
            Assert.AreEqual(5000, record.AmountCents);
            Assert.AreEqual(4, record.CommittedDayIndex);
            Assert.AreEqual(Terms, record.ForfeitTermsSummary);
            Assert.Greater(record.History.Count, 0);
            Assert.AreEqual(5000, ledger.TotalCommittedCents);
            Assert.AreEqual(5000, ledger.TotalHeldCents);
            Assert.AreEqual(0, ledger.TotalReleasedToSellerCents);
            Assert.AreEqual(0, ledger.TotalRefundedToBuyerCents);
            Assert.AreEqual(0, ledger.TotalAppliedToPriceCents);
        }

        [Test]
        public void T02_Commit_RefusesNonPositiveAmount()
        {
            EarnestEscrowLedger ledger = Ledger();

            Assert.IsNull(Commit(ledger, "land_1", 0));
            Assert.IsNull(Commit(ledger, "land_2", -100));
            Assert.AreEqual(0, ledger.Records.Count);
            Assert.AreEqual(0, ledger.TotalCommittedCents);
        }

        [Test]
        public void T03_Commit_RefusesSecondOpenEscrowForSameListing()
        {
            EarnestEscrowLedger ledger = Ledger();

            EarnestEscrowRecord first = Commit(ledger, "land_1", 5000);
            Assert.NotNull(first);
            Assert.IsNull(Commit(ledger, "land_1", 7000, dayIndex: 5));
            Assert.AreEqual(1, ledger.Records.Count);
            Assert.AreEqual(5000, ledger.TotalCommittedCents);
            Assert.AreEqual("escrow-1", ledger.FindOpenForListing("land_1").EscrowId);
        }

        [Test]
        public void T04_SettleFailure_Unprotected_ForfeitsEverythingToSeller()
        {
            EarnestEscrowLedger ledger = Ledger();
            EarnestEscrowRecord record = Commit(ledger, "land_1", 5000);

            string problem = ledger.SettleFailure(record.EscrowId, 5000, 0, 10, "buyer defaulted without a live contingency", new List<string>());

            Assert.IsNull(problem);
            Assert.AreEqual(EarnestEscrowState.ReleasedToSeller, record.State);
            Assert.AreEqual(5000, record.ReleasedToSellerCents);
            Assert.AreEqual(0, record.RefundedToBuyerCents);
            Assert.AreEqual(0, ledger.TotalHeldCents);
            Assert.AreEqual(5000, ledger.TotalReleasedToSellerCents);
            Assert.AreEqual(0, ledger.TotalRefundedToBuyerCents);
        }

        [Test]
        public void T05_SettleFailure_Protected_SplitsTwentyFiveSeventyFive()
        {
            EarnestEscrowLedger ledger = Ledger();
            EarnestEscrowRecord record = Commit(ledger, "land_1", 4000);

            string problem = ledger.SettleFailure(record.EscrowId, 1000, 3000, 10, "financing failed under a live contingency", new List<string>());

            Assert.IsNull(problem);
            Assert.AreEqual(EarnestEscrowState.SplitSettlement, record.State);
            Assert.AreEqual(1000, record.ReleasedToSellerCents);
            Assert.AreEqual(3000, record.RefundedToBuyerCents);
            Assert.AreEqual(1000, ledger.TotalReleasedToSellerCents);
            Assert.AreEqual(3000, ledger.TotalRefundedToBuyerCents);
        }

        [Test]
        public void T06_SettleFailure_PreAgreement_RefundsEverythingToBuyer()
        {
            EarnestEscrowLedger ledger = Ledger();
            EarnestEscrowRecord record = Commit(ledger, "land_1", 5000);

            string problem = ledger.SettleFailure(record.EscrowId, 0, 5000, 10, "option expired before any agreement", new List<string>());

            Assert.IsNull(problem);
            Assert.AreEqual(EarnestEscrowState.RefundedToBuyer, record.State);
            Assert.AreEqual(0, record.ReleasedToSellerCents);
            Assert.AreEqual(5000, record.RefundedToBuyerCents);
            Assert.AreEqual(5000, ledger.TotalRefundedToBuyerCents);
        }

        [Test]
        public void T07_SettleFailure_MismatchedSplitRefused_RecordStaysHeld()
        {
            EarnestEscrowLedger ledger = Ledger();
            EarnestEscrowRecord record = Commit(ledger, "land_1", 5000);

            // 4000 + 500 = 4500, not the 5000 held: money would be destroyed.
            string problem = ledger.SettleFailure(record.EscrowId, 4000, 500, 10, "short split", new List<string>());

            Assert.IsFalse(string.IsNullOrWhiteSpace(problem));
            Assert.AreEqual(EarnestEscrowState.Held, record.State);
            Assert.AreEqual(0, record.ReleasedToSellerCents);
            Assert.AreEqual(0, record.RefundedToBuyerCents);
            Assert.AreEqual(5000, ledger.TotalHeldCents);

            // A negative portion is refused the same way.
            string negative = ledger.SettleFailure(record.EscrowId, -100, 5100, 10, "negative split", new List<string>());
            Assert.IsFalse(string.IsNullOrWhiteSpace(negative));
            Assert.AreEqual(EarnestEscrowState.Held, record.State);
        }

        [Test]
        public void T08_SettleFailure_TerminalRecordRefused_NoDoubleSettlement()
        {
            EarnestEscrowLedger ledger = Ledger();
            EarnestEscrowRecord record = Commit(ledger, "land_1", 5000);
            Assert.IsNull(ledger.SettleFailure(record.EscrowId, 5000, 0, 10, "first settlement", new List<string>()));

            string problem = ledger.SettleFailure(record.EscrowId, 5000, 0, 11, "second settlement", new List<string>());

            Assert.IsFalse(string.IsNullOrWhiteSpace(problem));
            Assert.AreEqual(EarnestEscrowState.ReleasedToSeller, record.State);
            Assert.AreEqual(5000, ledger.TotalReleasedToSellerCents);
        }

        [Test]
        public void T09_ApplyToPrice_MovesHeldToApplied()
        {
            EarnestEscrowLedger ledger = Ledger();
            EarnestEscrowRecord record = Commit(ledger, "land_1", 5000);

            string problem = ledger.ApplyToPrice(record.EscrowId, 20, "applied to the closing price", new List<string>());

            Assert.IsNull(problem);
            Assert.AreEqual(EarnestEscrowState.AppliedToPrice, record.State);
            Assert.AreEqual(5000, record.AppliedToPriceCents);
            Assert.AreEqual(5000, ledger.TotalAppliedToPriceCents);
            Assert.AreEqual(0, ledger.TotalHeldCents);

            string second = ledger.ApplyToPrice(record.EscrowId, 21, "second apply", new List<string>());
            Assert.IsFalse(string.IsNullOrWhiteSpace(second));
        }

        [Test]
        public void T10_Dispute_PreservesFunds_OnlyResolveDisputeSettles()
        {
            EarnestEscrowLedger ledger = Ledger();
            EarnestEscrowRecord record = Commit(ledger, "land_1", 5000);

            string dispute = ledger.Dispute(record.EscrowId, 8, "seller and buyer disagree on the boundary", new List<string>());
            Assert.IsNull(dispute);
            Assert.AreEqual(EarnestEscrowState.Disputed, record.State);
            Assert.AreEqual(5000, ledger.TotalDisputedCents);
            Assert.AreEqual(0, ledger.TotalHeldCents);

            // The failure path cannot bypass a dispute.
            string bypass = ledger.SettleFailure(record.EscrowId, 5000, 0, 9, "failure path", new List<string>());
            Assert.IsFalse(string.IsNullOrWhiteSpace(bypass));
            Assert.AreEqual(EarnestEscrowState.Disputed, record.State);

            // A mismatched resolution is refused and the funds stay disputed.
            string mismatch = ledger.ResolveDispute(record.EscrowId, 3000, 1000, 12, "short split", new List<string>());
            Assert.IsFalse(string.IsNullOrWhiteSpace(mismatch));
            Assert.AreEqual(EarnestEscrowState.Disputed, record.State);

            // The negotiated split settles the whole deposit.
            string resolved = ledger.ResolveDispute(record.EscrowId, 3000, 2000, 12, "negotiated: seller keeps 3000c for the lost season, buyer recovers 2000c", new List<string>());
            Assert.IsNull(resolved);
            Assert.AreEqual(EarnestEscrowState.SplitSettlement, record.State);
            Assert.AreEqual(3000, record.ReleasedToSellerCents);
            Assert.AreEqual(2000, record.RefundedToBuyerCents);
            Assert.AreEqual(0, ledger.TotalDisputedCents);

            // A terminal record cannot be disputed.
            string late = ledger.Dispute(record.EscrowId, 13, "too late", new List<string>());
            Assert.IsFalse(string.IsNullOrWhiteSpace(late));
        }

        [Test]
        public void T11_SaveLoad_RoundTripsRecordsStatesAndSequence()
        {
            EarnestEscrowLedger ledger = Ledger();
            EarnestEscrowRecord first = Commit(ledger, "land_1", 5000);
            EarnestEscrowRecord second = Commit(ledger, "business_2", 8000, dayIndex: 5);
            Assert.IsNull(ledger.SettleFailure(first.EscrowId, 1000, 4000, 10, "protected failure", new List<string>()));
            Assert.IsNull(ledger.Dispute(second.EscrowId, 9, "title questioned", new List<string>()));

            EarnestEscrowSaveDto dto = ledger.CaptureSaveDto();
            var restored = new EarnestEscrowLedger();
            restored.LoadFromSaveDto(dto, new List<string>());

            Assert.AreEqual(2, restored.Records.Count);
            EarnestEscrowRecord restoredFirst = restored.Find("escrow-1");
            Assert.NotNull(restoredFirst);
            Assert.AreEqual(EarnestEscrowState.SplitSettlement, restoredFirst.State);
            Assert.AreEqual(1000, restoredFirst.ReleasedToSellerCents);
            Assert.AreEqual(4000, restoredFirst.RefundedToBuyerCents);
            Assert.AreEqual("Grace Boniecki", restoredFirst.SellerLabel);
            Assert.AreEqual(Terms, restoredFirst.ForfeitTermsSummary);
            Assert.Greater(restoredFirst.History.Count, 0);
            EarnestEscrowRecord restoredSecond = restored.Find("escrow-2");
            Assert.NotNull(restoredSecond);
            Assert.AreEqual(EarnestEscrowState.Disputed, restoredSecond.State);
            Assert.AreEqual(13000, restored.TotalCommittedCents);

            // The sequence continues past the restored ids — no collision.
            EarnestEscrowRecord third = restored.RecordCommit(
                "land_3", "Plot 003 parcel", "Player", "Town", "Land",
                2000, 15, Terms, new List<string>());
            Assert.NotNull(third);
            Assert.AreEqual("escrow-3", third.EscrowId);

            // An old save (null DTO) starts empty.
            var empty = new EarnestEscrowLedger();
            empty.LoadFromSaveDto(null, new List<string>());
            Assert.AreEqual(0, empty.Records.Count);
            EarnestEscrowRecord fresh = Commit(empty, "land_9", 1000);
            Assert.AreEqual("escrow-1", fresh.EscrowId);
        }

        [Test]
        public void T12_Conservation_BookBalancesToTheCentAcrossMixedSettlements()
        {
            EarnestEscrowLedger ledger = Ledger();
            EarnestEscrowRecord first = Commit(ledger, "land_1", 1000);
            EarnestEscrowRecord second = Commit(ledger, "land_2", 2000);
            EarnestEscrowRecord third = Commit(ledger, "land_3", 3000);

            Assert.IsNull(ledger.SettleFailure(first.EscrowId, 250, 750, 10, "protected failure", new List<string>()));
            Assert.IsNull(ledger.Dispute(second.EscrowId, 9, "disputed", new List<string>()));
            Assert.IsNull(ledger.ResolveDispute(second.EscrowId, 2000, 0, 12, "seller keeps the whole deposit", new List<string>()));
            Assert.IsNull(ledger.ApplyToPrice(third.EscrowId, 20, "closed", new List<string>()));

            Assert.AreEqual(6000, ledger.TotalCommittedCents);
            Assert.AreEqual(0, ledger.TotalHeldCents);
            Assert.AreEqual(0, ledger.TotalDisputedCents);
            Assert.AreEqual(2250, ledger.TotalReleasedToSellerCents);
            Assert.AreEqual(750, ledger.TotalRefundedToBuyerCents);
            Assert.AreEqual(3000, ledger.TotalAppliedToPriceCents);
            Assert.AreEqual(
                ledger.TotalCommittedCents,
                ledger.TotalHeldCents + ledger.TotalDisputedCents
                    + ledger.TotalReleasedToSellerCents + ledger.TotalRefundedToBuyerCents
                    + ledger.TotalAppliedToPriceCents);
        }
    }
}
