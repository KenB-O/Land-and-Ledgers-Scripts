using System.Collections.Generic;
using LandLedgers.Economy.Financing;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Financing
{
    /// <summary>
    /// Phase E (E3): banks and private lenders on the same machinery. Both
    /// write through the one FinancialObligationAuthority; they differ through
    /// real liquidity (finite own capital vs deposits with a reserve floor),
    /// institutional powers (senior liens), and underwriting policy. All run
    /// in the Roslyn harness.
    /// </summary>
    [TestFixture]
    public sealed class PhaseELenderDifferentiationTests
    {
        private sealed class Fixture
        {
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public FinancialObligationAuthority Authority = new FinancialObligationAuthority();
            public CreditOfferWorkflow Workflow = new CreditOfferWorkflow();
            public CreditCashBridge Cash = new CreditCashBridge();
            public CreditEventLog Events = new CreditEventLog();
            public List<string> Diag = new List<string>();

            public PurseCashStore RegisterPurse(string owner, int balanceCents)
            {
                var purse = new PurseCashStore(owner, balanceCents, "test seed capital");
                Assert.IsNull(Cash.Register(owner, purse));
                return purse;
            }
        }

        [Test]
        public void PrivateLender_LendsOwnCapital_NoMoneyCreation()
        {
            var f = new Fixture();
            PurseCashStore purse = f.RegisterPurse("Abel", 120000);
            f.RegisterPurse("Baker", 0);
            var funds = new PrivateLenderFunds("Abel", 120000);
            var lender = new PrivateLenderCreditParticipant("Abel", funds, purse);

            CreditRequest request = f.Workflow.SubmitRequest(f.Ids, "Baker", "Abel",
                100000, "stock", 360, 0, "income");
            CreditOffer offer = lender.EvaluateOffer(f.Ids, f.Workflow, f.Authority, request, 0, f.Events, f.Diag);
            FinancialObligation obligation = lender.AcceptOffer(f.Ids, f.Workflow, f.Authority,
                f.Cash, offer.OfferId, "Baker", 0, f.Events, f.Diag);

            Assert.IsNotNull(obligation);
            Assert.AreEqual(20000, funds.AvailableCents(), "Committed capital is no longer available.");
            Assert.AreEqual(20000, purse.ReadBalanceCents(), "The purse — real cash — fell by the advance.");

            // A second loan beyond the remaining capital is refused even
            // though the purse still holds 20000c of uncommitted money.
            CreditRequest request2 = f.Workflow.SubmitRequest(f.Ids, "Baker", "Abel",
                50000, "more stock", 360, 1, "income");
            CreditOffer offer2 = f.Workflow.Evaluate(f.Ids, request2, f.Authority, 50000, 1);
            FinancialObligation second = lender.AcceptOffer(f.Ids, f.Workflow, f.Authority,
                f.Cash, offer2.OfferId, "Baker", 1, f.Events, f.Diag);
            Assert.IsNull(second, "Finite capital refuses the second advance.");
            Assert.AreEqual(1, f.Authority.Obligations.Count);
        }

        [Test]
        public void Bank_ReserveFloorBlocksAdvance()
        {
            var f = new Fixture();
            PurseCashStore vault = f.RegisterPurse("Frontier Bank", 1000000);
            f.RegisterPurse("Baker", 0);
            // Deposits owed 800000c at a 15% reserve floor = 120000c locked.
            var bank = new BankCreditParticipant("Frontier Bank", vault, 800000);

            Assert.AreEqual(120000, bank.ReserveFloorCents());
            Assert.AreEqual(880000, bank.LendableCents());

            CreditRequest request = f.Workflow.SubmitRequest(f.Ids, "Baker", "Frontier Bank",
                900000, "mill", 1095, 0, "mill income");
            // Two disclosed evidence records satisfy the bank's stricter standard.
            f.Workflow.DiscloseEvidence(f.Ids, request, "borrower-disclosure", "owns mill free and clear", "", 0);
            f.Workflow.DiscloseEvidence(f.Ids, request, "borrower-disclosure", "three years of ledgers", "", 0);
            CreditOffer offer = bank.EvaluateOffer(f.Ids, f.Workflow, f.Authority, request, 0, f.Events, f.Diag);
            Assert.IsNull(offer, "The request exceeds lendable cash above the reserve floor.");
            Assert.IsTrue(f.Events.FindByKind(CreditEventKind.LenderRefused).Count > 0);

            CreditRequest request2 = f.Workflow.SubmitRequest(f.Ids, "Baker", "Frontier Bank",
                500000, "mill", 1095, 0, "mill income");
            f.Workflow.DiscloseEvidence(f.Ids, request2, "borrower-disclosure", "owns mill free and clear", "", 0);
            f.Workflow.DiscloseEvidence(f.Ids, request2, "borrower-disclosure", "three years of ledgers", "", 0);
            CreditOffer offer2 = bank.EvaluateOffer(f.Ids, f.Workflow, f.Authority, request2, 0, f.Events, f.Diag);
            Assert.IsNotNull(offer2);
            FinancialObligation obligation = bank.AcceptOffer(f.Ids, f.Workflow, f.Authority,
                f.Cash, offer2.OfferId, "Baker", 0, f.Events, f.Diag);
            Assert.IsNotNull(obligation);
            Assert.AreEqual(500000, vault.ReadBalanceCents());
            Assert.GreaterOrEqual(vault.ReadBalanceCents(), bank.ReserveFloorCents(),
                "The reserve floor survives the advance.");
        }

        [Test]
        public void Bank_StricterEvidenceStandardThanPrivateLender()
        {
            var f = new Fixture();
            f.RegisterPurse("Frontier Bank", 1000000);
            f.RegisterPurse("Abel", 200000);
            f.RegisterPurse("Baker", 0);
            var bank = new BankCreditParticipant("Frontier Bank", f.Cash.FindStore("Frontier Bank") as PurseCashStore, 0);
            var funds = new PrivateLenderFunds("Abel", 200000);
            var privateLender = new PrivateLenderCreditParticipant("Abel", funds,
                f.Cash.FindStore("Abel") as PurseCashStore);

            CreditRequest toBank = f.Workflow.SubmitRequest(f.Ids, "Baker", "Frontier Bank",
                50000, "stock", 360, 0, "income");
            f.Workflow.DiscloseEvidence(f.Ids, toBank, "borrower-disclosure", "one ledger", "", 0);
            Assert.IsNull(bank.EvaluateOffer(f.Ids, f.Workflow, f.Authority, toBank, 0, f.Events, f.Diag),
                "The bank needs two disclosed records; one is not enough.");

            CreditRequest toPrivate = f.Workflow.SubmitRequest(f.Ids, "Baker", "Abel",
                50000, "stock", 360, 0, "income");
            Assert.IsNotNull(privateLender.EvaluateOffer(f.Ids, f.Workflow, f.Authority, toPrivate, 0, f.Events, f.Diag),
                "The private lender decides relationally — no evidence minimum.");
        }

        [Test]
        public void Bank_FilesSeniorLien_PrivateLenderDoesNot()
        {
            var f = new Fixture();
            PurseCashStore vault = f.RegisterPurse("Frontier Bank", 1000000);
            PurseCashStore purse = f.RegisterPurse("Abel", 200000);
            f.RegisterPurse("Baker", 0);
            f.RegisterPurse("Carla", 0);
            var bank = new BankCreditParticipant("Frontier Bank", vault, 0);
            var funds = new PrivateLenderFunds("Abel", 200000);
            var privateLender = new PrivateLenderCreditParticipant("Abel", funds, purse);

            CreditRequest reqBank = f.Workflow.SubmitRequest(f.Ids, "Baker", "Frontier Bank",
                100000, "mill", 1095, 0, "income", "mill-parcel-1");
            f.Workflow.DiscloseEvidence(f.Ids, reqBank, "borrower-disclosure", "owns mill", "", 0);
            f.Workflow.DiscloseEvidence(f.Ids, reqBank, "borrower-disclosure", "ledgers", "", 0);
            CreditOffer bankOffer = bank.EvaluateOffer(f.Ids, f.Workflow, f.Authority, reqBank, 0, f.Events, f.Diag);
            FinancialObligation bankLoan = bank.AcceptOffer(f.Ids, f.Workflow, f.Authority,
                f.Cash, bankOffer.OfferId, "Baker", 0, f.Events, f.Diag);
            Assert.IsNotNull(bankLoan);

            CreditRequest reqPrivate = f.Workflow.SubmitRequest(f.Ids, "Carla", "Abel",
                50000, "stock", 360, 0, "income", "wagon-7");
            CreditOffer privateOffer = privateLender.EvaluateOffer(f.Ids, f.Workflow, f.Authority, reqPrivate, 0, f.Events, f.Diag);
            FinancialObligation privateLoan = privateLender.AcceptOffer(f.Ids, f.Workflow, f.Authority,
                f.Cash, privateOffer.OfferId, "Carla", 0, f.Events, f.Diag);
            Assert.IsNotNull(privateLoan);

            SecurityInterestRecord bankSecurity = null, privateSecurity = null;
            foreach (SecurityInterestRecord s in f.Authority.Securities)
            {
                if (s.ObligationId == bankLoan.ObligationId) bankSecurity = s;
                if (s.ObligationId == privateLoan.ObligationId) privateSecurity = s;
            }
            Assert.IsNotNull(bankSecurity, "The bank files its senior lien (institutional power).");
            Assert.AreEqual(1, bankSecurity.Priority);
            Assert.IsFalse(bankSecurity.PermitsJuniorInterest);
            Assert.IsNotNull(privateSecurity, "The private lender still gets security — through the same authority.");
            Assert.IsTrue(privateSecurity.PermitsJuniorInterest, "But cannot bar junior interests like the bank.");
        }

        [Test]
        public void BothLenders_ShareOneObligationAuthority()
        {
            var f = new Fixture();
            f.RegisterPurse("Frontier Bank", 1000000);
            f.RegisterPurse("Abel", 200000);
            f.RegisterPurse("Baker", 0);
            var bank = new BankCreditParticipant("Frontier Bank",
                f.Cash.FindStore("Frontier Bank") as PurseCashStore, 0);
            var funds = new PrivateLenderFunds("Abel", 200000);
            var privateLender = new PrivateLenderCreditParticipant("Abel", funds,
                f.Cash.FindStore("Abel") as PurseCashStore);

            CreditRequest r1 = f.Workflow.SubmitRequest(f.Ids, "Baker", "Frontier Bank",
                100000, "mill", 1095, 0, "income");
            f.Workflow.DiscloseEvidence(f.Ids, r1, "borrower-disclosure", "owns mill", "", 0);
            f.Workflow.DiscloseEvidence(f.Ids, r1, "borrower-disclosure", "ledgers", "", 0);
            CreditOffer o1 = bank.EvaluateOffer(f.Ids, f.Workflow, f.Authority, r1, 0, f.Events, f.Diag);
            Assert.IsNotNull(bank.AcceptOffer(f.Ids, f.Workflow, f.Authority, f.Cash, o1.OfferId, "Baker", 0, f.Events, f.Diag));

            CreditRequest r2 = f.Workflow.SubmitRequest(f.Ids, "Baker", "Abel",
                50000, "stock", 360, 0, "income");
            CreditOffer o2 = privateLender.EvaluateOffer(f.Ids, f.Workflow, f.Authority, r2, 0, f.Events, f.Diag);
            Assert.IsNotNull(privateLender.AcceptOffer(f.Ids, f.Workflow, f.Authority, f.Cash, o2.OfferId, "Baker", 0, f.Events, f.Diag));

            Assert.AreEqual(2, f.Authority.Obligations.Count);
            Assert.AreEqual(150000, f.Authority.TotalOutstandingFor("Baker"),
                "One debtor, one authority, one total — no second debt ledger.");
            Assert.AreEqual(100000, f.Authority.TotalReceivableFor("Frontier Bank"));
            Assert.AreEqual(50000, f.Authority.TotalReceivableFor("Abel"));
            Assert.IsEmpty(f.Authority.Reconcile());
        }
    }
}
