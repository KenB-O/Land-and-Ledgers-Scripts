using System.Collections.Generic;
using LandLedgers.Economy.Financing;
using LandLedgers.Primitives;
using NUnit.Framework;

namespace LandLedgers.Editor.Financing
{
    /// <summary>
    /// Phase E (E1): the shared credit backend completed through the existing
    /// systems. Real-cash wiring, finite lender funds, guaranty calls from
    /// real delinquency, trade-credit aging/collection, and save/load without
    /// duplicated obligations. All run in the Roslyn harness (real compile,
    /// real run).
    /// </summary>
    [TestFixture]
    public sealed class PhaseECreditBackendTests
    {
        private sealed class Fixture
        {
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public FinancialObligationAuthority Authority = new FinancialObligationAuthority();
            public CreditOfferWorkflow Workflow = new CreditOfferWorkflow();
            public CreditRegistry Registry = new CreditRegistry();
            public CreditCashBridge Cash = new CreditCashBridge();
            public CreditEventLog Events = new CreditEventLog();
            public List<string> Diag = new List<string>();

            public Fixture()
            {
                Registry.AttachFinancialAuthority(Authority);
            }

            public PurseCashStore RegisterPurse(string owner, int balanceCents)
            {
                var purse = new PurseCashStore(owner, balanceCents, "test seed capital");
                Assert.IsNull(Cash.Register(owner, purse));
                return purse;
            }
        }

        [Test]
        public void RealCashWiring_AdvanceConservesCash()
        {
            var f = new Fixture();
            PurseCashStore lenderPurse = f.RegisterPurse("Abel", 200000);
            PurseCashStore borrowerPurse = f.RegisterPurse("Baker", 50000);

            var funds = new PrivateLenderFunds("Abel", 200000);
            var lender = new PrivateLenderCreditParticipant("Abel", funds, lenderPurse);

            CreditRequest request = f.Workflow.SubmitRequest(f.Ids, "Baker", "Abel",
                100000, "seed stock", 360, 0, "shop income");
            CreditOffer offer = lender.EvaluateOffer(f.Ids, f.Workflow, f.Authority, request, 0, f.Events, f.Diag);
            Assert.IsNotNull(offer);
            Assert.AreEqual(CreditOfferStatus.Proposed, offer.Status);

            int before = lenderPurse.ReadBalanceCents() + borrowerPurse.ReadBalanceCents();
            FinancialObligation obligation = lender.AcceptOffer(f.Ids, f.Workflow, f.Authority,
                f.Cash, offer.OfferId, "Baker", 0, f.Events, f.Diag);
            Assert.IsNotNull(obligation);
            int after = lenderPurse.ReadBalanceCents() + borrowerPurse.ReadBalanceCents();
            Assert.AreEqual(before, after, "Cash must be conserved across the advance.");
            Assert.AreEqual(100000, lenderPurse.ReadBalanceCents());
            Assert.AreEqual(150000, borrowerPurse.ReadBalanceCents());
            Assert.AreEqual(100000, funds.AvailableCents(), "Commitment released nothing; 100000c still committed.");
            Assert.AreEqual(1, f.Authority.Obligations.Count);
        }

        [Test]
        public void PrivateLender_CannotLendBeyondCommitments()
        {
            var f = new Fixture();
            PurseCashStore lenderPurse = f.RegisterPurse("Abel", 200000);
            f.RegisterPurse("Baker", 0);
            var funds = new PrivateLenderFunds("Abel", 100000);
            var lender = new PrivateLenderCreditParticipant("Abel", funds, lenderPurse);

            // An offer larger than the lender's finite capital, built through
            // the plain workflow (which does not know about the funds gate).
            CreditRequest request = f.Workflow.SubmitRequest(f.Ids, "Baker", "Abel",
                150000, "seed stock", 360, 0, "shop income");
            CreditOffer offer = f.Workflow.Evaluate(f.Ids, request, f.Authority, 150000, 0);
            Assert.IsNotNull(offer);
            Assert.AreEqual(150000, offer.OfferedAmountCents);

            FinancialObligation obligation = lender.AcceptOffer(f.Ids, f.Workflow, f.Authority,
                f.Cash, offer.OfferId, "Baker", 0, f.Events, f.Diag);
            Assert.IsNull(obligation, "The funds gate must refuse an advance beyond finite capital.");
            Assert.AreEqual(0, f.Authority.Obligations.Count, "No obligation may exist without the advance.");
            Assert.AreEqual(200000, lenderPurse.ReadBalanceCents(), "No cash may move on a refused advance.");
            Assert.IsTrue(f.Events.FindByKind(CreditEventKind.LenderRefused).Count > 0);
        }

        [Test]
        public void UndrawnFacility_IsNotSpendable()
        {
            var f = new Fixture();
            f.RegisterPurse("Abel", 0);
            f.RegisterPurse("Baker", 0);
            CreditFacilityRecord facility = f.Authority.CreateFacility(f.Ids, "Abel", "Baker",
                100000, -1, 365, false, "revolving stock facility");
            Assert.IsNotNull(facility);
            Assert.AreEqual(0, facility.DrawnPrincipalCents);

            CreditCashAccount lenderCash = f.Cash.OpenWindow("Abel", f.Diag);
            CreditCashAccount borrowerCash = f.Cash.OpenWindow("Baker", f.Diag);
            FinancialObligation draw = f.Authority.DrawFacility(f.Ids, facility.FacilityId,
                50000, 0, lenderCash, borrowerCash, out string message);
            Assert.IsNull(draw, "An undrawn facility with no lender cash cannot advance: " + message);
            Assert.AreEqual(0, facility.DrawnPrincipalCents);
            Assert.AreEqual(0, f.Authority.Obligations.Count);
            f.Cash.DiscardWindow(lenderCash);
            f.Cash.DiscardWindow(borrowerCash);
        }

        [Test]
        public void GuarantyCall_CreatesExposureWithoutCloningPrincipal()
        {
            var f = new Fixture();
            FinancialObligation loan = f.Authority.Create(f.Ids, FinancialObligationKind.Loan,
                "Baker", "Abel", 100000, 0, "terms", "stock");
            GuarantyRecord guaranty = f.Authority.AddGuaranty(f.Ids, loan.ObligationId,
                "Abel", "Baker", "Gus", 100000);
            Assert.IsNotNull(guaranty);
            f.Authority.MarkDefaulted(loan.ObligationId);

            var workout = new CreditWorkoutService();
            workout.Accelerate(f.Ids, f.Authority, f.Cash, loan, 10, f.Events, f.Diag);

            FinancialObligation called = f.Authority.Find(guaranty.CalledObligationId);
            Assert.IsNotNull(called, "The guaranty call must create a real obligation.");
            Assert.AreEqual(FinancialObligationKind.GuarantyCall, called.Kind);
            Assert.AreEqual("Gus", called.Debtor);
            Assert.AreEqual(100000, loan.OutstandingPrincipalCents,
                "The covered principal is untouched — the call creates exposure, not a clone.");
            Assert.AreEqual(100000, called.OutstandingPrincipalCents);
            List<string> errors = f.Authority.Reconcile();
            Assert.IsEmpty(errors, string.Join("; ", errors));
            Assert.IsTrue(f.Events.FindByKind(CreditEventKind.GuarantyCalled).Count > 0);
        }

        [Test]
        public void DelinquencyToWorkout_RestructureKeepsPrincipal()
        {
            var f = new Fixture();
            FinancialObligation loan = f.Authority.CreateWithTerms(f.Ids, FinancialObligationKind.Loan,
                "Baker", "Abel", 100000, 0, "terms", "stock",
                new FinancialPaymentTerms
                {
                    AnnualInterestRateBps = 700,
                    Structure = FinancialPaymentStructure.MaturityPrincipal,
                });
            loan.MaturityDayIndex = 100;
            f.Authority.MarkDelinquent(loan.ObligationId);

            var workout = new CreditWorkoutService();
            string problem = workout.Restructure(f.Ids, f.Authority, loan, 110, f.Events, f.Diag);
            Assert.IsNull(problem);
            Assert.AreEqual(100000, loan.OutstandingPrincipalCents, "Restructuring must never rewrite principal.");
            Assert.AreEqual(100000, loan.OriginalPrincipalCents);
            Assert.Greater(loan.MaturityDayIndex, 100, "Maturity must extend.");
            Assert.AreEqual(FinancialPaymentStructure.PrincipalInstallments, loan.PaymentTerms.Structure);
            Assert.IsTrue(f.Events.FindByKind(CreditEventKind.WorkoutRestructured).Count > 0);
            Assert.IsEmpty(f.Authority.Reconcile());
        }

        [Test]
        public void TradeCredit_AgingAndCollectionStages()
        {
            var f = new Fixture();
            FinancialObligation invoice = f.Authority.Create(f.Ids, FinancialObligationKind.TradeCredit,
                "household:7", "General Store", 5000, 0, "net 30", "flour and salt");
            var book = new TradeCreditBook();
            Assert.IsNull(book.RegisterTerms(f.Authority, invoice.ObligationId, 30, 0, f.Diag));

            book.EvaluateDay(f.Authority, 0, f.Events, f.Diag);
            Assert.AreEqual(TradeCreditCollectionStage.Current, book.FindTerms(invoice.ObligationId).Stage);

            book.EvaluateDay(f.Authority, 35, f.Events, f.Diag); // 5 days past due
            Assert.AreEqual(TradeCreditCollectionStage.ReminderIssued, book.FindTerms(invoice.ObligationId).Stage);
            Assert.AreEqual(FinancialObligationStatus.Due, invoice.Status);

            book.EvaluateDay(f.Authority, 65, f.Events, f.Diag); // 35 dpd
            Assert.AreEqual(TradeCreditCollectionStage.FormalDemand, book.FindTerms(invoice.ObligationId).Stage);

            book.EvaluateDay(f.Authority, 95, f.Events, f.Diag); // 65 dpd
            Assert.AreEqual(TradeCreditCollectionStage.InCollection, book.FindTerms(invoice.ObligationId).Stage);
            Assert.AreEqual(FinancialObligationStatus.Delinquent, invoice.Status);

            book.EvaluateDay(f.Authority, 160, f.Events, f.Diag); // 130 dpd
            Assert.AreEqual(TradeCreditCollectionStage.Defaulted, book.FindTerms(invoice.ObligationId).Stage);
            Assert.AreEqual(FinancialObligationStatus.Defaulted, invoice.Status);

            Assert.AreEqual(5000, invoice.OutstandingPrincipalCents, "Aging never touches the balance.");
            Assert.IsTrue(f.Events.FindByKind(CreditEventKind.CollectionStageChanged).Count >= 4);
        }

        [Test]
        public void SaveLoad_NoDuplicatedObligationsPrincipalOrPayments()
        {
            var f = new Fixture();
            FinancialObligation loan = f.Authority.CreateWithTerms(f.Ids, FinancialObligationKind.Loan,
                "Baker", "Abel", 100000, 0, "terms", "stock",
                new FinancialPaymentTerms { AnnualInterestRateBps = 700 });
            f.Authority.ApplyPayment(loan.ObligationId, 10000, 5, "Baker", "Abel", "evt-1");

            FinancialObligationSaveDto dto = f.Authority.CaptureSaveDto();
            var restored = new FinancialObligationAuthority();
            restored.LoadFromSaveDto(dto);

            Assert.AreEqual(1, restored.Obligations.Count);
            FinancialObligation r = restored.Find(loan.ObligationId);
            Assert.AreEqual(100000, r.OriginalPrincipalCents);
            Assert.AreEqual(90000, r.OutstandingPrincipalCents);
            Assert.AreEqual(1, r.Payments.Count);
            Assert.AreEqual(10000, r.Payments[0].AmountCents);
            Assert.IsEmpty(restored.Reconcile());

            CreditOfferWorkflowSaveDto wDto = f.Workflow.CaptureSaveDto();
            var wRestored = new CreditOfferWorkflow();
            wRestored.LoadFromSaveDto(wDto);
            Assert.AreEqual(f.Workflow.Requests.Count, wRestored.Requests.Count);

            CreditEventLog.CreditEventLogSaveDto eDto = f.Events.CaptureSaveDto();
            var eRestored = new CreditEventLog();
            eRestored.LoadFromSaveDto(eDto);
            Assert.AreEqual(f.Events.Count, eRestored.Count);
        }
    }
}
