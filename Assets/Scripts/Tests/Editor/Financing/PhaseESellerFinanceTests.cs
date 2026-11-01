using System.Collections.Generic;
using LandLedgers.Economy.Financing;
using LandLedgers.Primitives;
using LandLedgers.World.Property;
using NUnit.Framework;

namespace LandLedgers.Editor.Financing
{
    /// <summary>
    /// Phase E (E2): the full seller-finance decision and closing. Accept /
    /// counter / refuse paths, closing conservation (cash + note == price),
    /// real title transfer, security separate from ownership, and the Phase D
    /// TermsAccepted handoff. All run in the Roslyn harness.
    /// </summary>
    [TestFixture]
    public sealed class PhaseESellerFinanceTests
    {
        private sealed class Fixture
        {
            public EntityIdRegistry Ids = new EntityIdRegistry();
            public FinancialObligationAuthority Authority = new FinancialObligationAuthority();
            public CreditOfferWorkflow Workflow = new CreditOfferWorkflow();
            public CreditRegistry Registry = new CreditRegistry();
            public CreditCashBridge Cash = new CreditCashBridge();
            public CreditEventLog Events = new CreditEventLog();
            public TitleAuthority Titles = new TitleAuthority();
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

        private static SellerFinanceDecisionInput BaseInput()
        {
            return new SellerFinanceDecisionInput
            {
                SellerName = "Abel",
                BuyerName = "Baker",
                AssetDescription = "corner lot with shop",
                AssetInstanceId = "parcel-12",
                AskingPriceCents = 500000,
                OfferedDownPaymentCents = 150000,
                OfferedRateBps = 700,
                OfferedTermDays = 1095,
                SellerCashCents = 1000000,
                SellerLiquidityNeed01 = 0.2f,
            };
        }

        [Test]
        public void SellerDecision_AcceptsGoodTerms()
        {
            var service = new SellerFinanceDecisionService();
            SellerFinanceDecision decision = service.Decide(new SellerFinancePolicy(),
                BaseInput(), new FinancialObligationAuthority(), 0, new List<string>());
            Assert.AreEqual(SellerFinanceDecisionOutcome.Accept, decision.Outcome);
            Assert.IsNotEmpty(decision.Reasons);
        }

        [Test]
        public void SellerDecision_CountersLowDownPayment()
        {
            var service = new SellerFinanceDecisionService();
            SellerFinanceDecisionInput input = BaseInput();
            input.OfferedDownPaymentCents = 10000; // 2% — under the 20% policy minimum
            SellerFinanceDecision decision = service.Decide(new SellerFinancePolicy(),
                input, new FinancialObligationAuthority(), 0, new List<string>());
            Assert.AreEqual(SellerFinanceDecisionOutcome.Counter, decision.Outcome);
            Assert.GreaterOrEqual(decision.CounterDownPaymentCents, 100000);
            Assert.IsNotEmpty(decision.Reasons);
        }

        [Test]
        public void SellerDecision_RefusesCashAlternative()
        {
            var service = new SellerFinanceDecisionService();
            SellerFinanceDecisionInput input = BaseInput();
            input.AlternativeCashOfferCents = 500000;
            SellerFinanceDecision decision = service.Decide(new SellerFinancePolicy(),
                input, new FinancialObligationAuthority(), 0, new List<string>());
            Assert.AreEqual(SellerFinanceDecisionOutcome.Refuse, decision.Outcome);
        }

        [Test]
        public void SellerDecision_UsesOnlyLegitimateEvidence()
        {
            var authority = new FinancialObligationAuthority();
            var ids = new EntityIdRegistry();
            // Baker owes a THIRD party 400000c — disclosed to Abel.
            FinancialObligation thirdPartyDebt = authority.Create(ids, FinancialObligationKind.Loan,
                "Baker", "Carla", 400000, 0, "terms", "wagon");
            var workflow = new CreditOfferWorkflow();
            CreditRequest request = workflow.SubmitRequest(ids, "Baker", "Abel",
                350000, "shop", 1095, 0, "income");
            CreditEvidenceRecord evidence = workflow.DiscloseEvidence(ids, request,
                "borrower-disclosure", "owes Carla 400000c", thirdPartyDebt.ObligationId, 0);

            var service = new SellerFinanceDecisionService();
            SellerFinanceDecisionInput input = BaseInput();
            input.BuyerDisclosedEvidence.Add(evidence);
            // Financed = 350000c; known disclosed debt 400000c > asking 500000c? No:
            // 400000 < 500000, so no hard counter — but the evidence is counted.
            SellerFinanceDecision decision = service.Decide(new SellerFinancePolicy(),
                input, authority, 0, new List<string>());
            Assert.AreEqual(SellerFinanceDecisionOutcome.Accept, decision.Outcome);
            Assert.IsTrue(string.Join(" ", decision.Reasons).Contains("400000"),
                "The decision must cite the legitimately known debt.");
        }

        [Test]
        public void SellerDecision_HardensTermsOnHeavyDisclosedDebt()
        {
            var authority = new FinancialObligationAuthority();
            var ids = new EntityIdRegistry();
            FinancialObligation bigDebt = authority.Create(ids, FinancialObligationKind.Loan,
                "Baker", "Carla", 900000, 0, "terms", "debts");
            var workflow = new CreditOfferWorkflow();
            CreditRequest request = workflow.SubmitRequest(ids, "Baker", "Abel",
                350000, "shop", 1095, 0, "income");
            CreditEvidenceRecord evidence = workflow.DiscloseEvidence(ids, request,
                "borrower-disclosure", "owes Carla 900000c", bigDebt.ObligationId, 0);

            var service = new SellerFinanceDecisionService();
            SellerFinanceDecisionInput input = BaseInput();
            input.BuyerDisclosedEvidence.Add(evidence);
            SellerFinanceDecision decision = service.Decide(new SellerFinancePolicy(),
                input, authority, 0, new List<string>());
            Assert.AreEqual(SellerFinanceDecisionOutcome.Counter, decision.Outcome);
            Assert.Greater(decision.CounterRateBps, input.OfferedRateBps);
        }

        [Test]
        public void Closing_ConservesCashAndNote_AndSeparatesTitleFromSecurity()
        {
            var f = new Fixture();
            PurseCashStore buyerPurse = f.RegisterPurse("Baker", 200000);
            PurseCashStore sellerPurse = f.RegisterPurse("Abel", 10000);
            f.Titles.RegisterParcel("parcel-12", "corner lot with shop", 0.5f, "Abel",
                TitleBasis.Purchase, 0, f.Diag);

            var terms = new SellerFinanceClosingTerms
            {
                NegotiationId = "sfn-1",
                SellerName = "Abel",
                BuyerName = "Baker",
                AssetDescription = "corner lot with shop",
                AssetInstanceId = "parcel-12",
                SalePriceCents = 500000,
                DownPaymentCents = 150000,
                AnnualRateBps = 700,
                TermDays = 1095,
            };
            var closing = new SellerFinanceClosingService();
            SellerFinanceClosingResult result = closing.Close(f.Ids, terms, f.Authority,
                f.Registry, f.Cash, f.Titles, 5, f.Events, f.Diag);

            Assert.IsTrue(result.Closed, result.FailureReason);
            // Conservation: cash to seller + financed note == price.
            Assert.AreEqual(150000, result.CashToSellerCents);
            Assert.AreEqual(350000, result.FinancedCents);
            Assert.AreEqual(terms.SalePriceCents, result.CashToSellerCents + result.FinancedCents);
            Assert.AreEqual(50000, buyerPurse.ReadBalanceCents(), "Buyer paid 150000c down from 200000c.");
            Assert.AreEqual(160000, sellerPurse.ReadBalanceCents(), "Seller received 150000c cash.");

            // The seller owns a real creditor claim through the shared authority.
            FinancialObligation obligation = f.Authority.Find(result.ObligationId);
            Assert.IsNotNull(obligation);
            Assert.AreEqual(FinancialObligationKind.SellerFinance, obligation.Kind);
            Assert.AreEqual("Baker", obligation.Debtor);
            Assert.AreEqual("Abel", obligation.Creditor);
            Assert.AreEqual(350000, obligation.OutstandingPrincipalCents);

            // Title moved to the buyer; the seller keeps a security interest —
            // never the title. Outstanding finance never restores ownership.
            Assert.IsTrue(result.TitleTransferred);
            Assert.AreEqual("Baker", f.Titles.CurrentHolder("parcel-12"));
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.SecurityInterestId));
            SecurityInterestRecord security = null;
            foreach (SecurityInterestRecord s in f.Authority.Securities)
                if (s.SecurityInterestId == result.SecurityInterestId) security = s;
            Assert.IsNotNull(security);
            Assert.AreEqual("Abel", security.Creditor);
            Assert.AreEqual("Baker", security.Grantor);
            Assert.IsTrue(security.CollateralIds.Contains("parcel-12"));

            Assert.IsTrue(f.Events.FindByKind(CreditEventKind.SellerNoteIssued).Count > 0);
            Assert.IsTrue(f.Events.FindByKind(CreditEventKind.TitleTransferred).Count > 0);
            Assert.IsTrue(f.Events.FindByKind(CreditEventKind.Settlement).Count > 0);
            Assert.IsEmpty(f.Authority.Reconcile());
        }

        [Test]
        public void Closing_RefusesWhenBuyerCannotPayDown()
        {
            var f = new Fixture();
            f.RegisterPurse("Baker", 10000); // far short of the 150000c down
            f.RegisterPurse("Abel", 10000);
            f.Titles.RegisterParcel("parcel-12", "corner lot with shop", 0.5f, "Abel",
                TitleBasis.Purchase, 0, f.Diag);

            var terms = new SellerFinanceClosingTerms
            {
                SellerName = "Abel", BuyerName = "Baker",
                AssetDescription = "corner lot with shop", AssetInstanceId = "parcel-12",
                SalePriceCents = 500000, DownPaymentCents = 150000,
                AnnualRateBps = 700, TermDays = 1095,
            };
            var closing = new SellerFinanceClosingService();
            SellerFinanceClosingResult result = closing.Close(f.Ids, terms, f.Authority,
                f.Registry, f.Cash, f.Titles, 5, f.Events, f.Diag);

            Assert.IsFalse(result.Closed);
            Assert.AreEqual(0, f.Registry.InstrumentCount, "No note may be issued without the cash leg.");
            Assert.AreEqual("Abel", f.Titles.CurrentHolder("parcel-12"), "Title must not move on a failed closing.");
            Assert.IsTrue(f.Events.FindByKind(CreditEventKind.DecisionFailed).Count > 0);
        }

        [Test]
        public void Closing_AttachesSecurityEvenWithoutTitleAuthority()
        {
            var f = new Fixture();
            f.RegisterPurse("Baker", 200000);
            f.RegisterPurse("Abel", 0);
            // No parcel registered and no title authority — equipment sale.
            var terms = new SellerFinanceClosingTerms
            {
                SellerName = "Abel", BuyerName = "Baker",
                AssetDescription = "thresher", AssetInstanceId = "equip-thresher-3",
                SalePriceCents = 80000, DownPaymentCents = 30000,
                AnnualRateBps = 800, TermDays = 730,
            };
            var closing = new SellerFinanceClosingService();
            SellerFinanceClosingResult result = closing.Close(f.Ids, terms, f.Authority,
                f.Registry, f.Cash, null, 5, f.Events, f.Diag);

            Assert.IsTrue(result.Closed, result.FailureReason);
            Assert.IsFalse(result.TitleTransferred, "No title authority was wired — nothing fabricated.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.SecurityInterestId),
                "The seller still gets a real security interest in the equipment.");
        }
        [Test]
        public void PhaseDNegotiationBook_TermsAccepted_ClosesThroughRealNote()
        {
            var f = new Fixture();
            f.RegisterPurse("household:7", 200000);
            f.RegisterPurse("Abel", 5000);
            f.Titles.RegisterParcel("parcel-9", "half-section", 320f, "Abel",
                TitleBasis.Purchase, 0, f.Diag);

            // The Phase D negotiation side: inquiry, seller counter, acceptance.
            var book = new LandLedgers.World.Property.SellerFinanceNegotiationBook();
            var negotiation = book.SendInquiry(7, "Abel", "parcel-9", "half-section",
                400000, 0, f.Diag);
            Assert.IsNull(book.RecordSellerCounter(negotiation.NegotiationId,
                120000, 700, 36, 8000, 1, f.Diag));
            Assert.IsNull(book.RecordTermsAccepted(negotiation.NegotiationId, 2, f.Diag));

            // The Phase E handoff: TermsAccepted maps to closing terms.
            SellerFinanceClosingTerms terms = book.ToClosingTerms(negotiation.NegotiationId, f.Diag);
            Assert.IsNotNull(terms, "TermsAccepted must map to closing terms.");
            Assert.AreEqual("household:7", terms.BuyerName);
            Assert.AreEqual(400000, terms.SalePriceCents);
            Assert.AreEqual(120000, terms.DownPaymentCents);

            var closing = new SellerFinanceClosingService();
            SellerFinanceClosingResult result = closing.Close(f.Ids, terms, f.Authority,
                f.Registry, f.Cash, f.Titles, 3, f.Events, f.Diag);

            Assert.IsTrue(result.Closed, result.FailureReason);
            Assert.AreEqual(120000, result.CashToSellerCents);
            Assert.AreEqual(280000, result.FinancedCents);
            Assert.AreEqual("household:7", f.Titles.CurrentHolder("parcel-9"));
            FinancialObligation obligation = f.Authority.Find(result.ObligationId);
            Assert.AreEqual("household:7", obligation.Debtor);
            Assert.AreEqual("Abel", obligation.Creditor);
            Assert.IsEmpty(f.Authority.Reconcile());
        }

        [Test]
        public void PhaseDNegotiationBook_RefusesClosingTermsBeforeAcceptance()
        {
            var book = new LandLedgers.World.Property.SellerFinanceNegotiationBook();
            var negotiation = book.SendInquiry(7, "Abel", "parcel-9", "half-section",
                400000, 0, new List<string>());
            // Still at InquirySent — no closing terms may be produced.
            Assert.IsNull(book.ToClosingTerms(negotiation.NegotiationId, new List<string>()));
            Assert.IsNull(book.ToClosingTerms("no-such-negotiation", new List<string>()));
        }
    }
}
