using LandLedgers.UI;
using NUnit.Framework;

namespace LandLedgers.Editor.Orchestration
{
    /// <summary>
    /// Pass XI WS5 (Part A): transaction feedback may only signal
    /// success when the authoritative operation reported success. The
    /// panel call sites (OwnershipWorkflowController loan submit,
    /// GeneralStorePanelController cash transfers / thresholds / toggle)
    /// are outside the harness subset, so these tests lock the decision
    /// core they now call, and the call-site diffs mirror the proven
    /// in-file pattern (GeneralStore SubmitLoanApplication branching on
    /// HasPendingApplication; threshold-adjust branching on `changed`).
    /// </summary>
    [TestFixture]
    public sealed class Pass11Ws5TransactionFeedbackTests
    {
        [Test]
        public void LoanSubmission_RejectedNeverPlaysApprovalStamp()
        {
            Assert.AreEqual(LLFeedbackKind.StampApproval, TransactionFeedbackDecision.ForLoanSubmission(true));
            Assert.AreEqual(LLFeedbackKind.Warning, TransactionFeedbackDecision.ForLoanSubmission(false));
        }

        [Test]
        public void CashTransfer_BlockedNeverPlaysCashSound()
        {
            Assert.AreEqual(LLFeedbackKind.Warning, TransactionFeedbackDecision.ForCashTransfer(false, 999999));
            Assert.AreEqual(LLFeedbackKind.Warning, TransactionFeedbackDecision.ForCashTransfer(false, 0));
        }

        [Test]
        public void CashTransfer_CompletedSizesSoundToAmount()
        {
            Assert.AreEqual(LLFeedbackKind.SmallCash, TransactionFeedbackDecision.ForCashTransfer(true, 50));
            Assert.AreEqual(LLFeedbackKind.SmallCash, TransactionFeedbackDecision.ForCashTransfer(true, 999));
            Assert.AreEqual(LLFeedbackKind.MediumCash, TransactionFeedbackDecision.ForCashTransfer(true, 1000));
            Assert.AreEqual(LLFeedbackKind.MediumCash, TransactionFeedbackDecision.ForCashTransfer(true, 9999));
            Assert.AreEqual(LLFeedbackKind.HeavyCash, TransactionFeedbackDecision.ForCashTransfer(true, 10000));
            // Withdrawals pass a signed amount; sizing uses magnitude.
            Assert.AreEqual(LLFeedbackKind.MediumCash, TransactionFeedbackDecision.ForCashTransfer(true, -5000));
        }

        [Test]
        public void Operation_BranchesOnSuccess()
        {
            Assert.AreEqual(LLFeedbackKind.UIConfirm, TransactionFeedbackDecision.ForOperation(true, LLFeedbackKind.UIConfirm));
            Assert.AreEqual(LLFeedbackKind.Warning, TransactionFeedbackDecision.ForOperation(false, LLFeedbackKind.UIConfirm));
        }
    }
}
