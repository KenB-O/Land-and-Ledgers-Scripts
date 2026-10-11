using System.Collections.Generic;
using LandLedgers.Economy.Financing;
using NUnit.Framework;

namespace LandLedgers.EditorTests.Financing
{
    public sealed class BankDepositFeedTests
    {
        [Test]
        public void BankCreditParticipant_RefreshesReserveInputFromAuthoritativeDepositLedger()
        {
            var ledger = new BankDepositLedger("Frontier Bank");
            var diagnostics = new List<string>();
            Assert.IsNotNull(ledger.OpenAccount("demand-1", "Household 1", DepositKind.Demand,
                10000, 1, diag: diagnostics));

            var bank = new BankCreditParticipant("Frontier Bank", null, 0);
            bank.RefreshDepositsFrom(ledger);
            Assert.AreEqual(10000, bank.DepositsOwedCents);

            Assert.IsNull(ledger.Deposit("demand-1", 5000, 2, diagnostics));
            Assert.AreEqual(10000, bank.DepositsOwedCents,
                "The snapshot remains unchanged until the bank participant is refreshed.");
            bank.RefreshDepositsFrom(ledger);
            Assert.AreEqual(15000, bank.DepositsOwedCents);
        }
    }
}
