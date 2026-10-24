using System;
using System.Collections.Generic;
using LandLedgers.Economy;
using LandLedgers.Economy.Financing;
using LandLedgers.Economy.Labor;
using LandLedgers.Primitives;
using LandLedgers.ReadModels.Valuation;
using NUnit.Framework;

namespace LandLedgers.Editor.Financing
{
    /// <summary>
    /// Pass XI WS2 (audit finding A-06, RUN-003): bank ledger binding
    /// contract proofs, plus the live-read proof for the existing lender.
    /// The registry is a contract, not a live scene binding: these tests
    /// prove its fail-loud single-ledger rule and its live position reads,
    /// and prove the live lender's underwriting figure moves with its
    /// ledger (no stale deposit snapshot) rather than claiming a scene
    /// hookup that does not exist yet.
    /// </summary>
    [TestFixture]
    public sealed class Pass11Ws2BankBindingTests
    {
        [Test]
        public void BindBankLedger_SecondDistinctLedger_Refused_FirstBindingStands()
        {
            var registry = new BankLedgerBindingRegistry();
            var diag = new List<string>();
            var first = new BankDepositLedger("Frontier Town Bank");
            var second = new BankDepositLedger("Frontier Town Bank");

            Assert.IsNull(registry.BindBankLedger("bank-1", first, diag));
            Assert.AreSame(first, registry.FindLedger("bank-1"));

            string refusal = registry.BindBankLedger("bank-1", second, diag);
            Assert.IsNotNull(refusal, "a second ledger for one bank must be refused, never silently replace the first");
            StringAssert.Contains("already bound", refusal);
            Assert.AreSame(first, registry.FindLedger("bank-1"), "the first binding stands after a refused second bind");

            Assert.IsNull(registry.BindBankLedger("bank-1", first, diag), "re-binding the identical ledger is a no-op");
            Assert.IsNotNull(registry.BindBankLedger("bank-1", null, diag), "binding no ledger binds nothing");
            Assert.IsNull(registry.FindLedger("bank-unknown"), "an unbound bank has no ledger to read");
        }

        [Test]
        public void DepositPosition_ReadsLiveLedger_AfterDepositsChange()
        {
            var registry = new BankLedgerBindingRegistry();
            var diag = new List<string>();
            var ledger = new BankDepositLedger("Frontier Town Bank");
            Assert.IsNull(registry.BindBankLedger("bank-1", ledger, diag));

            Assert.IsTrue(registry.TryReadDepositPosition("bank-1", out int cash, out int owed, out int net));
            Assert.AreEqual(0, cash);
            Assert.AreEqual(0, owed);

            Assert.IsNotNull(ledger.OpenAccount("acct-1", "Abel Hart", DepositKind.Demand, 50000, 10, 0, 0, diag));
            Assert.IsTrue(registry.TryReadDepositPosition("bank-1", out cash, out owed, out net));
            Assert.AreEqual(50000, cash, "cash on hand moved with the deposit — the read is live, not a snapshot");
            Assert.AreEqual(50000, owed, "the liability moved with the deposit");
            Assert.AreEqual(0, net);

            Assert.IsNull(ledger.Deposit("acct-1", 25000, 11, diag));
            Assert.IsTrue(registry.TryReadDepositPosition("bank-1", out cash, out owed, out net));
            Assert.AreEqual(75000, cash);
            Assert.AreEqual(75000, owed);

            Assert.IsFalse(registry.TryReadDepositPosition("bank-unknown", out cash, out owed, out net),
                "an unbound bank reports no position rather than inventing one");
        }

        [Test]
        public void BusinessLender_LendableCents_MovesWithLedger_NoStaleSnapshot()
        {
            var ids = new EntityIdRegistry();
            var obligations = new FinancialObligationAuthority();
            var valuation = new EnterpriseValuationReadModel();
            var lender = new BusinessLenderService("Test Town Bank");
            lender.BindHubServices(ids, obligations, new CreditOfferWorkflow(), new CreditEventLog(), valuation);
            var diag = new List<string>();
            Assert.IsNull(lender.EnsureCharteredCapital(100000, "test charter capital", 100, diag));

            int before = lender.LendableCents();
            Assert.AreEqual(100000, before, "charter capital is the live ledger figure at decision time");

            // A loan decision reads the same live figure: arranging a loan
            // disburses from the ledger, and the next decision sees less.
            // The formal-bank single-borrower cap is 10% of lendable, so
            // the healthy loan here is 10000c against 100000c lendable.
            var business = new BusinessInstanceState();
            Assert.IsTrue(business.TryAssignPremises(42));
            var runtime = new BusinessRuntimeState();
            runtime.AddCashCents(100000);
            var field = business.GetType().GetField("runtimeState",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(field);
            field.SetValue(business, runtime);
            var idField = business.GetType().GetField("instanceId",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(idField);
            idField.SetValue(business, "store-1");
            lender.BindBusinessLookup(id => string.Equals(id, "store-1", StringComparison.Ordinal) ? business : null);
            valuation.RegisterBusiness("store-1", "owner:store-1", false);
            for (int i = 0; i < 6; i++) valuation.RecordWeeklyProfit("store-1", 20000);
            valuation.RecordTransferableAssets("store-1", 200000);
            business.BusinessReputation.stockReliability01 = 0.9f;
            business.BusinessReputation.valueFairness01 = 0.9f;
            business.BusinessReputation.serviceExperience01 = 0.9f;
            business.BusinessReputation.conditionPresentationTrust01 = 0.9f;
            business.BusinessReputation.productTradeConfidence01 = 0.9f;

            FinancialObligation loan = lender.TryArrangeLoan(ids, obligations, "store-1", 10000, 100,
                diag, out string refusal);
            Assert.IsNotNull(loan, "healthy loan refused: " + refusal);
            Assert.AreEqual(90000, lender.LendableCents(),
                "after disbursing 10000c the next underwriting decision must see 90000c lendable, not the old 100000c");
            FinancialObligation oversized = lender.TryArrangeLoan(ids, obligations, "store-1", 95000, 101,
                diag, out string refusal2);
            Assert.IsNull(oversized, "a loan above the live lendable figure must be refused");
            Assert.AreEqual(90000, lender.LendableCents(), "a refused loan moves nothing");
        }
    }
}
